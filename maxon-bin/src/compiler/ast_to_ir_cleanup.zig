/// Cleanup IR emission helpers.
/// Contains temporary string tracking, borrow checking, and heap variable cleanup.
const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const err = @import("error.zig");
const types = @import("ast_to_ir_types.zig");
const VarInfo = types.VarInfo;
const ValueType = types.ValueType;
const OwnershipState = types.OwnershipState;
const ConvertError = types.ConvertError;

const string = @import("ast_to_ir_string.zig");
const cstring = @import("ast_to_ir_cstring.zig");
const map_helpers = @import("ast_to_ir_map.zig");
const ManagedArray = string.ManagedArray;
const String = string.String;
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;
const DeferredBlocks = ast_to_ir.DeferredBlocks;

// ============================================================================
// Temporary String Cleanup
// ============================================================================

/// Clean up all temporary strings and clear the list
pub fn cleanupTemporaryStrings(self: *AstToIr) !void {
    for (self.temporary_strings.items) |str_ptr| {
        try string.emitStringDecref(self, ir.toStringPtr(str_ptr), "<temp>");
    }
    self.temporary_strings.clearRetainingCapacity();
}

/// Remove a specific value from the temporary strings list (used when assigned to a variable
/// or when ownership transfers to an array via __managed_array_set_at)
pub fn removeFromTemporaries(self: *AstToIr, value: ir.Value) void {
    var i: usize = 0;
    while (i < self.temporary_strings.items.len) {
        if (self.temporary_strings.items[i] == value) {
            _ = self.temporary_strings.swapRemove(i);
        } else {
            i += 1;
        }
    }
}

/// Check if a value is in the temporary strings list
pub fn isInTemporaries(self: *AstToIr, value: ir.Value) bool {
    for (self.temporary_strings.items) |temp| {
        if (temp == value) return true;
    }
    return false;
}

// ============================================================================
// Borrow Checking Helpers
// ============================================================================

/// Mark a source string variable as borrowed and record the borrower
pub fn markStringBorrowed(self: *AstToIr, source_var_name: []const u8) void {
    if (self.var_map.getPtr(source_var_name)) |var_info| {
        if (string.isStringType(var_info.ty)) {
            var_info.borrow_state = .borrowed;
        }
    }
}

/// Clear the borrow state from a parent variable when the slice goes out of scope
pub fn clearBorrowFromParent(self: *AstToIr, slice_var_info: *const VarInfo) void {
    if (slice_var_info.borrowed_from) |parent_name| {
        if (self.var_map.getPtr(parent_name)) |parent_info| {
            parent_info.borrow_state = .none;
        }
    }
}

/// Check if a string variable can be modified (not borrowed)
/// Returns an error if the variable is currently borrowed
pub fn checkStringNotBorrowed(self: *AstToIr, var_name: []const u8) ConvertError!void {
    if (self.var_map.get(var_name)) |var_info| {
        if (string.isStringType(var_info.ty) and var_info.borrow_state == .borrowed) {
            const msg = std.fmt.allocPrint(self.allocator, "Cannot modify string '{s}' while it is borrowed", .{var_name}) catch {
                self.reportError(.E020, var_name);
                return error.SemanticError;
            };
            self.last_error = .{
                .code = .E020,
                .message = msg,
                .location = err.SourceLocation.init(@intCast(self.current_line), 1),
                .message_allocated = true,
            };
            return error.SemanticError;
        }
    }
}

/// Check that no borrowed strings go out of scope
/// Called before freeing heap variables at scope end
pub fn checkNoOutstandingBorrows(self: *AstToIr) ConvertError!void {
    var iter = self.var_map.iterator();
    while (iter.next()) |entry| {
        const var_info = entry.value_ptr.*;
        if (string.isStringType(var_info.ty) and var_info.borrow_state == .borrowed) {
            const msg = std.fmt.allocPrint(self.allocator, "String '{s}' goes out of scope while still borrowed", .{entry.key_ptr.*}) catch {
                self.reportError(.E021, entry.key_ptr.*);
                return error.SemanticError;
            };
            self.last_error = .{
                .code = .E021,
                .message = msg,
                .location = err.SourceLocation.init(@intCast(self.current_line), 1),
                .message_allocated = true,
            };
            return error.SemanticError;
        }
    }
}

/// Clear all slice borrows when slices go out of scope
/// This should be called at the end of scopes to release borrows
pub fn clearSliceBorrows(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(OwnershipState)) void {
    var iter = self.var_map.iterator();
    while (iter.next()) |entry| {
        if (exclude_vars) |excluded| {
            if (excluded.contains(entry.key_ptr.*)) continue;
        }
        const var_info = entry.value_ptr;
        if (var_info.borrow_state == .slice) {
            clearBorrowFromParent(self, var_info);
        }
    }
}

// ============================================================================
// Array String Elements Cleanup
// ============================================================================

/// Emit cleanup for all String elements in an Array$String.
/// Iterates through each element and decrefs its buffer using COW semantics.
/// array_ptr points to an Array$String struct (with __ManagedArray at offset 0).
pub fn emitArrayStringElementsCleanup(self: *AstToIr, array_ptr: ir.Value) !void {
    // Array$String layout: __ManagedArray (24 bytes) + iterIndex (8 bytes)
    // __ManagedArray layout: buffer_ptr (8) + length (8) + capacity (8)
    // String layout: __ManagedArray (32 bytes) + _iterPos (8 bytes) = 40 bytes per element
    // __ManagedArray layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)

    // Load length first to check if empty
    const len = try ManagedArray.loadLen(self.func(), ir.toManagedArrayPtr(array_ptr));

    // Skip cleanup if empty
    const zero = try self.func().emitConstI64(0);
    const is_empty = try self.func().emitBinaryOp(.icmp_eq, len, zero, .i64);

    // Allocate loop counter on stack (BEFORE creating new blocks - still in entry block)
    const counter_ptr = try self.func().emitAlloca(.i64);
    try self.func().emitStore(counter_ptr.raw(), zero);

    // Create blocks: entry -> cond -> body -> decref -> heap_free -> next -> end
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_cond");
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_body");
    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_decref");
    const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_heap_free");
    const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_next");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_str_cleanup_end");

    // Branch from entry: if empty, skip to end; otherwise go to condition
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_empty }, .{ .block_ref = end_block_idx }, .{ .block_ref = cond_block_idx } },
    });

    // Defer blocks in reverse order: end(0), next(1), heap_free(2), decref(3), body(4)
    var deferred = try DeferredBlocks.init(self.allocator, 5);
    defer deferred.deinit();
    deferred.deferBlocks(self, 5);

    // Condition block: reload len and check if i < len
    const cond_len = try ManagedArray.loadLen(self.func(), ir.toManagedArrayPtr(array_ptr));
    const cond_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_len, .i64);

    // Branch from cond: if done, go to end; otherwise go to body
    try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
    });

    // Restore body block to emit loop body
    try deferred.restore(self, 4);

    // Body block: calculate element pointer and check if heap mode
    const body_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
    const body_i = try self.func().emitLoad(counter_ptr.raw(), .i64);

    // Calculate element pointer: buf_ptr + i * 40 (String size)
    const elem_size = try self.func().emitConstI64(String.size());
    const offset = try self.func().emitBinaryOp(.mul, body_i, elem_size, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, body_buf_ptr, offset, .ptr);

    // Check if heap mode: flags & 3 == 1
    // The String contains __ManagedArray at offset 0
    const is_heap = try ManagedArray.isHeapMode(self.func(), ir.toManagedArrayPtr(elem_ptr));

    // Branch: if heap mode, go to decref block; otherwise go to next
    try self.func().blocks.items[body_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore decref block
    try deferred.restore(self, 3);

    // Decref block: decrement refcount in buffer header and check if zero
    const decref_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
    const decref_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const decref_offset = try self.func().emitBinaryOp(.mul, decref_i, elem_size, .i64);
    const decref_elem_ptr = try self.func().emitBinaryOp(.add, decref_buf_ptr, decref_offset, .ptr);

    // Buffer header refcount: load buf_ptr from String, subtract 8 to get header
    const str_buf_ptr = try self.func().emitLoad(decref_elem_ptr, .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, str_buf_ptr, eight, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_ref = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for decref
    if (self.track_memory) {
        try self.func().emitTrackDecref("<array element>", new_ref);
    }

    // Check if refcount is now zero
    const zero_ref = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero_ref, .i64);

    // Branch: if zero, go to heap_free; otherwise go to next
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = heap_free_block_idx }, .{ .block_ref = next_block_idx } },
    });

    // Restore heap_free block
    try deferred.restore(self, 2);

    // Heap free block: reload element ptr and free buffer (at header, which is buf_ptr - 8)
    const free_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
    const free_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const free_offset = try self.func().emitBinaryOp(.mul, free_i, elem_size, .i64);
    const free_elem_ptr = try self.func().emitBinaryOp(.add, free_buf_ptr, free_offset, .ptr);
    const free_str_buf_ptr = try self.func().emitLoad(free_elem_ptr, .ptr);
    const free_eight = try self.func().emitConstI64(8);
    const free_header_ptr = try self.func().emitBinaryOp(.sub, free_str_buf_ptr, free_eight, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(free_header_ptr), "string cleanup");

    // Branch to next
    try self.func().emitBr(next_block_idx);

    // Restore next block
    try deferred.restore(self, 1);

    // Next block: increment counter and loop back
    const one = try self.func().emitConstI64(1);
    const curr_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const next_i = try self.func().emitBinaryOp(.add, curr_i, one, .i64);
    try self.func().emitStore(counter_ptr.raw(), next_i);

    // Branch back to condition
    try self.func().emitBr(cond_block_idx);

    // Switch to end block for subsequent code
    try deferred.restore(self, 0);
}

// ============================================================================
// Heap Variable Cleanup
// ============================================================================

/// Free a single heap variable's resources.
pub fn freeHeapVar(self: *AstToIr, var_info: VarInfo, skip_if_parameter: bool, var_name: []const u8) !void {
    // Don't free if already moved
    if (var_info.state == .moved) {
        debug.astToIr("freeHeapVar: skipped (moved)", .{});
        return;
    }

    if (var_info.ty == .array_type) {
        const arr_info = var_info.ty.array_type;
        if (arr_info.storage == .heap) {
            // var_info.ptr points to a stack slot holding a ptr to __ManagedArray
            // __ManagedArray layout: [buffer_ptr, len, capacity] at offsets [0, 8, 16]
            const managed_ptr = try self.func().emitLoad(var_info.ptr.?, .ptr);

            // For arrays of strings, we need to decref each string element before freeing the buffer
            if (arr_info.element_struct_type) |elem_type| {
                if (std.mem.eql(u8, elem_type, "String")) {
                    try emitArrayStringElementsCleanup(self, managed_ptr);
                }
            }

            const buffer_ptr = try self.func().emitLoad(managed_ptr, .ptr);
            try self.func().emitHeapFree(ir.toRawPtr(buffer_ptr), "array cleanup");
        }
    }

    if (var_info.ty == .struct_type) {
        const struct_name = var_info.ty.struct_type.name;
        debug.astToIr("freeHeapVar: struct {s}", .{struct_name});

        // Handle stdlib Array types (Array$int, etc.)
        if (std.mem.startsWith(u8, struct_name, "Array$")) {
            if (skip_if_parameter and var_info.is_parameter) return;

            // For Array$String, we need to decref each string element before freeing the buffer
            if (std.mem.eql(u8, struct_name, "Array$String")) {
                try emitArrayStringElementsCleanup(self, var_info.ptr.?);
            }

            // Buffer pointer is at offset 0 within the inlined __ManagedArray
            const buf_ptr = try self.func().emitLoad(var_info.ptr.?, .ptr);
            try self.func().emitHeapFree(ir.toRawPtr(buf_ptr), "array cleanup");
        }

        // Handle String type with COW semantics
        if (string.isStringType(var_info.ty)) {
            if (skip_if_parameter and var_info.is_parameter) return;
            try string.emitStringDecref(self, ir.toStringPtr(var_info.ptr.?), var_name);
        }

        // Handle cstring type cleanup
        if (std.mem.eql(u8, struct_name, "cstring")) {
            if (skip_if_parameter and var_info.is_parameter) return;
            try cstring.emitCstringCleanup(self, var_info.ptr.?);
        }

        // Handle stdlib Map types (Map$K$V)
        // Map layout: keys(Array 32b) + values(Array 32b) + states(Array 32b) + count(8) + capacity(8) + iterIndex(8)
        // Each Array layout: buffer_ptr(8) + len(8) + cap(8) + iterIndex(8)
        if (std.mem.startsWith(u8, struct_name, "Map$")) {
            debug.astToIr("freeHeapVar: emitting Map cleanup for {s}", .{struct_name});
            if (skip_if_parameter and var_info.is_parameter) return;
            try map_helpers.emitMapCleanup(self, var_info.ptr.?, struct_name);
        }
    }
}

/// Free all heap variables in the current scope.
pub fn freeHeapVars(self: *AstToIr, exclude_vars: ?*std.StringHashMapUnmanaged(OwnershipState)) !void {
    // Borrow checking: first clear borrows from slice variables that are going out of scope
    clearSliceBorrows(self, exclude_vars);

    // Borrow checking: verify no borrowed strings go out of scope
    try checkNoOutstandingBorrows(self);

    var iter = self.var_map.iterator();
    while (iter.next()) |entry| {
        debug.astToIr("freeHeapVars: checking '{s}'", .{entry.key_ptr.*});
        if (exclude_vars) |excluded| {
            if (excluded.contains(entry.key_ptr.*)) continue;
        }
        try freeHeapVar(self, entry.value_ptr.*, true, entry.key_ptr.*);
    }
}

/// Free all heap allocations including temporaries.
pub fn freeHeapAllocations(self: *AstToIr) !void {
    try freeHeapVars(self, null);
    // Clean up any remaining temporary strings (e.g., method call arguments)
    try cleanupTemporaryStrings(self);
    // Also clean up runtime-initialized constants that have heap resources (Maps, Arrays, etc.)
    try freeRuntimeConstants(self);
}

/// Free heap resources for runtime-initialized constants (Maps, Arrays, Strings)
pub fn freeRuntimeConstants(self: *AstToIr) !void {
    var iter = self.converted_runtime_constants.iterator();
    while (iter.next()) |entry| {
        const typed_value = entry.value_ptr.*;
        // Create a temporary VarInfo to reuse freeHeapVar logic
        const var_info = VarInfo.init(
            typed_value.value,
            typed_value.ty,
            false, // is_mutable
            false, // uses_slot
        );
        try freeHeapVar(self, var_info, false, "<const>");
    }
}

/// Free loop-scoped heap variables (variables not in pre_loop_vars).
pub fn freeLoopScopedHeapVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(OwnershipState)) !void {
    try freeHeapVars(self, pre_loop_vars);
}

/// Remove loop-scoped variables from var_map (variables not in pre_loop_vars).
pub fn removeLoopScopedVars(self: *AstToIr, pre_loop_vars: *std.StringHashMapUnmanaged(OwnershipState)) !void {
    var to_remove = std.ArrayListUnmanaged([]const u8){};
    defer to_remove.deinit(self.allocator);
    var var_iter = self.var_map.keyIterator();
    while (var_iter.next()) |key| {
        if (!pre_loop_vars.contains(key.*)) {
            try to_remove.append(self.allocator, key.*);
        }
    }
    for (to_remove.items) |key| {
        _ = self.var_map.remove(key);
    }
}
