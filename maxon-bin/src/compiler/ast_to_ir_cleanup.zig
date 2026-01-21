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
const FieldInfo = types.FieldInfo;

const cstring = @import("ast_to_ir_cstring.zig");
const array_helpers = @import("ast_to_ir_managed_memory.zig");
const ManagedMemory = array_helpers.ManagedMemory;
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;
const DeferredBlocks = ast_to_ir.DeferredBlocks;

// ============================================================================
// Temporary Managed Buffer Cleanup
// ============================================================================

/// Clean up all temporary managed buffers (strings, etc.) and clear the list
pub fn cleanupTemporaryManagedBuffers(self: *AstToIr) !void {
    for (self.temporary_strings.items) |managed_ptr| {
        try array_helpers.emitManagedMemoryDecref(self, ir.toManagedMemoryPtr(managed_ptr), "<temp>", "temp cleanup");
    }
    self.temporary_strings.clearRetainingCapacity();
}

/// Remove a specific value from the temporary strings list (used when assigned to a variable
/// or when ownership transfers to an array via __managed_memory_set_at)
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

/// Mark a variable with managed buffer as borrowed and record the borrower
pub fn markManagedBorrowed(self: *AstToIr, source_var_name: []const u8) void {
    if (self.var_map.getPtr(source_var_name)) |var_info| {
        if (var_info.ty == .struct_type and var_info.ty.struct_type.has_managed_buffer) {
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

/// Check if a variable with managed buffer can be modified (not borrowed)
/// Returns an error if the variable is currently borrowed
pub fn checkManagedNotBorrowed(self: *AstToIr, var_name: []const u8) ConvertError!void {
    if (self.var_map.get(var_name)) |var_info| {
        if (var_info.ty == .struct_type and var_info.ty.struct_type.has_managed_buffer and var_info.borrow_state == .borrowed) {
            const msg = std.fmt.allocPrint(self.allocator, "Cannot modify '{s}' while it is borrowed", .{var_name}) catch {
                self.reportError(.E020, var_name, @src());
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

/// Check that no borrowed variables with managed buffers go out of scope
/// Called before freeing heap variables at scope end
pub fn checkNoOutstandingBorrows(self: *AstToIr) ConvertError!void {
    var iter = self.var_map.iterator();
    while (iter.next()) |entry| {
        const var_info = entry.value_ptr.*;
        if (var_info.ty == .struct_type and var_info.ty.struct_type.has_managed_buffer and var_info.borrow_state == .borrowed) {
            const msg = std.fmt.allocPrint(self.allocator, "Variable '{s}' goes out of scope while still borrowed", .{entry.key_ptr.*}) catch {
                self.reportError(.E021, entry.key_ptr.*, @src());
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
// Array Element Cleanup with Refcount Check
// ============================================================================

/// Emit element cleanup only if we're the sole owner of the array buffer.
/// This prevents double-free when arrays are shared via COW semantics.
/// If refcount > 1, we're not the sole owner, so we just decref the container
/// and let the final owner clean up the elements.
fn emitConditionalElementCleanup(self: *AstToIr, managed_ptr: ir.ManagedMemoryPtr, elem_struct_info: *const types.StructTypeInfo) !void {
    // Check if array is in heap mode (do this BEFORE creating new blocks)
    const is_heap = try ManagedMemory.isHeapMode(self.func(), managed_ptr);

    // Also check for null buffer (empty array)
    const buf_ptr = try self.func().emitLoad(managed_ptr.raw(), .ptr);
    const zero = try self.func().emitConstI64(0);
    const not_null = try self.func().emitBinaryOp(.icmp_ne, buf_ptr, zero, .i64);
    const should_check = try self.func().emitBinaryOp(.band, not_null, is_heap, .i64);

    // Create blocks: entry -> check_refcount -> cleanup -> end
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const check_refcount_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_check_rc");
    const cleanup_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_do");
    _ = try self.func().addBlock("elem_cleanup_end");

    // Emit branch from entry block with placeholder end index (will be patched later)
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = should_check }, .{ .block_ref = check_refcount_block_idx }, .{ .block_ref = 0 } }, // end patched later
    });

    // Defer blocks in reverse order: end(0), cleanup(1)
    var deferred = try DeferredBlocks.init(self.allocator, 2);
    defer deferred.deinit();
    deferred.deferBlocks(self, 2);

    // Now we're in check_refcount block
    // Check refcount block: load refcount and check if == 1
    const buf_ptr2 = try self.func().emitLoad(managed_ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr2, eight, .ptr);
    const refcount = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const is_sole_owner = try self.func().emitBinaryOp(.icmp_eq, refcount, one, .i64);

    // Branch: if sole owner (refcount == 1), do cleanup; otherwise skip to end
    // NOTE: We use deferred.idx(0) for end block - the DeferredBlocks correctly calculates
    // the FINAL index where the block will be when ALL deferred blocks are restored.
    try self.func().blocks.items[check_refcount_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_sole_owner }, .{ .block_ref = cleanup_block_idx }, .{ .block_ref = deferred.idx(0) } },
    });

    // Restore cleanup block (this makes it the current block)
    try deferred.restore(self, 1);

    // Do element cleanup - use appropriate method based on element type
    if (elem_struct_info.has_managed_buffer) {
        try emitArrayManagedElementsCleanup(self, managed_ptr.raw(), elem_struct_info);
    } else {
        try emitElementCleanup(self, managed_ptr.raw(), elem_struct_info);
    }

    // Restore end block FIRST, so we know its actual index
    try deferred.restore(self, 0);
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Now go back and patch the branches that need to point to end_block_idx
    // Entry block branch was emitted before we knew the final end_block_idx, so patch it
    self.func().blocks.items[entry_block_idx].instructions.items[self.func().blocks.items[entry_block_idx].instructions.items.len - 1].operands[2] = .{ .block_ref = end_block_idx };

    // Also patch the check_refcount branch
    self.func().blocks.items[check_refcount_block_idx].instructions.items[self.func().blocks.items[check_refcount_block_idx].instructions.items.len - 1].operands[2] = .{ .block_ref = end_block_idx };

    // Branch from cleanup's end block to our end block
    // The cleanup functions left us in their final block, which is now the second-to-last block
    const after_cleanup_block_idx: u32 = end_block_idx - 1;
    try self.func().blocks.items[after_cleanup_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, undefined, undefined },
    });
}

// ============================================================================
// Array Managed Elements Cleanup (for elements with COW semantics)
// ============================================================================

/// Emit cleanup for all elements with has_managed_buffer in an array.
/// Iterates through each element and decrefs its buffer using COW semantics.
/// array_ptr points to an Array$T struct (with __ManagedMemory at offset 0).
/// elem_struct_info describes the element type (must have has_managed_buffer = true).
fn emitArrayManagedElementsCleanup(self: *AstToIr, array_ptr: ir.Value, elem_struct_info: *const types.StructTypeInfo) !void {
    // Array$T layout: __ManagedMemory (32 bytes) + iterIndex (8 bytes)
    // __ManagedMemory layout: buffer_ptr (8) + length (8) + capacity (8) + flags (4) + parent_off (4)
    // Element layout: __ManagedMemory at offset 0 (has_managed_buffer types)

    // Get element size from struct info
    const elem_size_int: i64 = @intCast(elem_struct_info.size);

    // Load length first to check if empty
    const len = try ManagedMemory.loadLen(self.func(), ir.toManagedMemoryPtr(array_ptr));

    // Skip cleanup if empty
    const zero = try self.func().emitConstI64(0);
    const is_empty = try self.func().emitBinaryOp(.icmp_eq, len, zero, .i64);

    // Allocate loop counter on stack (BEFORE creating new blocks - still in entry block)
    const counter_ptr = try self.func().emitAlloca(.i64);
    try self.func().emitStore(counter_ptr.raw(), zero);

    // Create blocks: entry -> cond -> body -> decref -> heap_free -> next -> end
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_cond");
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_body");
    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_decref");
    const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_heap_free");
    const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_next");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("arr_managed_cleanup_end");

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
    const cond_len = try ManagedMemory.loadLen(self.func(), ir.toManagedMemoryPtr(array_ptr));
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

    // Calculate element pointer: buf_ptr + i * elem_size
    const elem_size = try self.func().emitConstI64(elem_size_int);
    const offset = try self.func().emitBinaryOp(.mul, body_i, elem_size, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, body_buf_ptr, offset, .ptr);

    // Check if heap mode: flags & 3 == 1
    // The element contains __ManagedMemory at offset 0
    const is_heap = try ManagedMemory.isHeapMode(self.func(), ir.toManagedMemoryPtr(elem_ptr));

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

    // Buffer header refcount: load buf_ptr from element, subtract 8 to get header
    const elem_buf_ptr = try self.func().emitLoad(decref_elem_ptr, .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, elem_buf_ptr, eight, .ptr);
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
    const free_elem_buf_ptr = try self.func().emitLoad(free_elem_ptr, .ptr);
    const free_eight = try self.func().emitConstI64(8);
    const free_header_ptr = try self.func().emitBinaryOp(.sub, free_elem_buf_ptr, free_eight, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(free_header_ptr), "element cleanup");

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

/// Free a single heap variable's resources based on the needs_cleanup flag.
/// Uses recursive field-based cleanup for struct types.
pub fn freeHeapVar(self: *AstToIr, var_info: VarInfo, skip_if_parameter: bool, var_name: []const u8) !void {
    // Don't free if already moved
    if (var_info.state == .moved) {
        debug.astToIr("freeHeapVar: skipped (moved)", .{});
        return;
    }

    if (var_info.ty == .struct_type) {
        const struct_info = var_info.ty.struct_type;

        // Only clean up types that have heap allocations
        if (!struct_info.needs_cleanup) {
            debug.astToIr("freeHeapVar: skipped {s} (no cleanup needed)", .{struct_info.name});
            return;
        }

        // Skip internal types (like __ManagedMemory) at top-level - they should only be
        // cleaned up as a field of another struct (String, Array). Standalone internal
        // type variables are typically slices which don't own memory, and they may be
        // declared in conditional blocks where they're not initialized on all paths.
        if (struct_info.is_internal_type) {
            debug.astToIr("freeHeapVar: skipped {s} (internal type)", .{struct_info.name});
            return;
        }

        if (skip_if_parameter and var_info.is_parameter) return;

        debug.astToIr("freeHeapVar: cleaning up struct {s}", .{struct_info.name});

        // Get the pointer to the struct data
        const struct_ptr = if (var_info.is_heap_allocated)
            try self.func().emitLoad(var_info.ptr.?, .ptr)
        else
            var_info.ptr.?;

        try cleanupStruct(self, struct_ptr, struct_info, var_name);
    }
}

/// Cleanup old field value before reassignment (for types with managed buffers).
/// This is used when reassigning to a struct field to properly release the old value.
pub fn cleanupFieldBeforeReassign(self: *AstToIr, field_ptr: ir.StructPtr, field_info: FieldInfo, context: []const u8) !void {
    // Only cleanup struct types
    if (field_info.value_type != .struct_type) return;

    const struct_name = field_info.structName() orelse return;

    // Look up the struct type info
    const type_info = self.type_map.get(struct_name) orelse return;
    if (type_info != .struct_type) return;

    const struct_info = &type_info.struct_type;

    // Only clean up types that have heap allocations
    if (!struct_info.needs_cleanup) return;

    debug.astToIr("cleanupFieldBeforeReassign: cleaning up field {s} of type {s}", .{ context, struct_name });

    try cleanupStruct(self, field_ptr.raw(), struct_info, context);
}

/// Clean up a struct by recursively cleaning up its fields that need cleanup.
/// Uses mode-based dispatch for types with __ManagedMemory.
/// For composite structs (with multiple fields needing cleanup), uses cleanupStructFields
/// to properly recurse through each field, including arrays with elements needing cleanup.
fn cleanupStruct(self: *AstToIr, struct_ptr: ir.Value, struct_info: *const types.StructTypeInfo, var_name: []const u8) ConvertError!void {
    const name = struct_info.name;

    // cstring has its own cleanup logic (special pattern with data/length/managed fields)
    if (std.mem.eql(u8, name, "cstring")) {
        try cstring.emitCstringCleanup(self, struct_ptr);
        return;
    }

    // Check if struct has __ManagedMemory field - use unified COW decref
    // This handles String, Array$T, and similar single-managed-buffer types.
    // For composite structs with multiple managed fields (like Program with FunctionArray
    // and ExprArray), we fall through to cleanupStructFields which properly recurses.
    if (struct_info.has_managed_buffer) {
        // First, clean up elements if this is a collection with element cleanup needs
        // Use element_type_name from struct_info if available, otherwise parse from name
        const elem_type_name = struct_info.element_type_name orelse blk: {
            if (std.mem.startsWith(u8, name, "Array$")) {
                break :blk name["Array$".len..];
            }
            break :blk null;
        };

        // Get pointer to __ManagedMemory at the correct offset
        const managed_ptr = try array_helpers.getManagedMemoryPtr(self, struct_ptr, struct_info.managed_buffer_offset);

        if (elem_type_name) |eln| {
            // Check if element type needs cleanup
            if (self.type_map.get(eln)) |elem_type_info| {
                if (elem_type_info == .struct_type and elem_type_info.struct_type.needs_cleanup) {
                    // Only clean elements if we're the sole owner of this array buffer.
                    // If the array is shared via COW (refcount > 1), other copies still
                    // reference the same elements IN the same buffer. We should only decref
                    // the container, not the elements - the final owner will clean elements.
                    //
                    // We check: if heap mode AND refcount == 1, clean elements.
                    // Otherwise skip element cleanup entirely.
                    //
                    // IMPORTANT: We must check heap mode FIRST before accessing the refcount,
                    // because in non-heap modes (SSO, slice, static), the buffer_ptr may be
                    // null or invalid, and dereferencing buf_ptr - 8 would crash.
                    const is_heap = try ManagedMemory.isHeapMode(self.func(), managed_ptr);

                    // Record entry block index BEFORE creating any new blocks
                    const entry_block_idx = @as(u32, @intCast(self.func().blocks.items.len - 1));

                    // Create a block to check the refcount (only entered if is_heap is true)
                    const check_refcount_block_idx = @as(u32, @intCast(self.func().blocks.items.len));
                    _ = try self.func().addBlock("elem_cleanup_check_refcount");

                    // Create cleanup_block - this is where element cleanup code will go
                    const cleanup_block_idx = @as(u32, @intCast(self.func().blocks.items.len));
                    _ = try self.func().addBlock("elem_cleanup_if_owner");

                    // Now in cleanup block (it's the last created) - do element cleanup
                    // These functions may add many blocks internally
                    if (elem_type_info.struct_type.has_managed_buffer) {
                        try emitArrayManagedElementsCleanup(self, managed_ptr.raw(), &elem_type_info.struct_type);
                    } else {
                        try emitElementCleanup(self, managed_ptr.raw(), &elem_type_info.struct_type);
                    }

                    // After cleanup, we're in whatever block the cleanup function left us in.
                    // Record this for adding branch later.
                    const after_cleanup_block_idx = @as(u32, @intCast(self.func().blocks.items.len - 1));

                    // Create end block AFTER all cleanup code - now its index is stable
                    const end_block_idx = @as(u32, @intCast(self.func().blocks.items.len));
                    _ = try self.func().addBlock("elem_cleanup_done");

                    // Now patch all the branches:
                    // 1. Entry -> check_refcount (if is_heap) OR end (if not heap)
                    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
                        .op = .br_cond,
                        .operands = .{ .{ .value = is_heap }, .{ .block_ref = check_refcount_block_idx }, .{ .block_ref = end_block_idx } },
                    });

                    // 2. Check refcount block: load refcount and branch to cleanup or end
                    // Insert instructions at the BEGINNING of check_refcount_block (it's currently empty)
                    const check_block = &self.func().blocks.items[check_refcount_block_idx];

                    // Load buf_ptr, compute header_ptr = buf_ptr - 8, load refcount
                    // We need to emit these into the check_refcount block
                    // Unfortunately we can't easily emit into a non-current block, so we'll build the instructions manually

                    // Emit the refcount check instructions into check_refcount_block
                    const buf_ptr_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .load,
                        .operands = .{ .{ .value = managed_ptr.raw() }, undefined, undefined },
                        .result = buf_ptr_val,
                        .result_type = .ptr,
                    });

                    const eight_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .const_i64,
                        .operands = .{ .{ .immediate_i64 = 8 }, undefined, undefined },
                        .result = eight_val,
                        .result_type = .i64,
                    });

                    const header_ptr_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .sub,
                        .operands = .{ .{ .value = buf_ptr_val }, .{ .value = eight_val }, undefined },
                        .result = header_ptr_val,
                        .result_type = .ptr,
                    });

                    const refcount_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .load,
                        .operands = .{ .{ .value = header_ptr_val }, undefined, undefined },
                        .result = refcount_val,
                        .result_type = .i64,
                    });

                    const one_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .const_i64,
                        .operands = .{ .{ .immediate_i64 = 1 }, undefined, undefined },
                        .result = one_val,
                        .result_type = .i64,
                    });

                    const is_sole_owner_val = self.func().newValue();
                    try check_block.instructions.append(self.allocator, .{
                        .op = .icmp_eq,
                        .operands = .{ .{ .value = refcount_val }, .{ .value = one_val }, undefined },
                        .result = is_sole_owner_val,
                        .result_type = .i64,
                    });

                    // Branch: if sole owner, cleanup; else skip to end
                    try check_block.instructions.append(self.allocator, .{
                        .op = .br_cond,
                        .operands = .{ .{ .value = is_sole_owner_val }, .{ .block_ref = cleanup_block_idx }, .{ .block_ref = end_block_idx } },
                    });

                    // 3. After cleanup -> end
                    try self.func().blocks.items[after_cleanup_block_idx].instructions.append(self.allocator, .{
                        .op = .br,
                        .operands = .{ .{ .block_ref = end_block_idx }, undefined, undefined },
                    });

                    // We're now in end block (it was just added, so it's the current block)
                }
            }
        }

        // Clean up the buffer based on mode using COW decref
        // Determine cleanup tag: use "string cleanup" for String type (element size 1),
        // otherwise "array cleanup" for collections
        const cleanup_tag: []const u8 = if (struct_info.element_type_name == null and struct_info.has_managed_buffer)
            "string cleanup" // String type (no element_type_name but has_managed_buffer)
        else
            "array cleanup";
        try array_helpers.emitManagedMemoryDecref(self, managed_ptr, var_name, cleanup_tag);
        return;
    }

    // Internal types standalone (like __ManagedMemory) - use decref with header
    if (struct_info.is_internal_type) {
        try array_helpers.emitManagedMemoryDecref(self, ir.toManagedMemoryPtr(struct_ptr), var_name, "array cleanup");
        return;
    }

    // For other struct types, recursively clean up fields
    try cleanupStructFields(self, struct_ptr, struct_info, var_name);
}

/// Emit cleanup for all elements in an array that need cleanup.
/// Iterates through each element and calls cleanupStruct on it.
///
/// Note: This function cannot pre-create all blocks because cleanupStruct may
/// create additional blocks, which would invalidate pre-calculated indices.
/// Instead, we create blocks just-in-time and branch from wherever we are.
fn emitElementCleanup(self: *AstToIr, array_ptr: ir.Value, elem_struct_info: *const types.StructTypeInfo) !void {
    // Load length first to check if empty
    const len = try ManagedMemory.loadLen(self.func(), ir.toManagedMemoryPtr(array_ptr));

    // Skip cleanup if empty
    const zero = try self.func().emitConstI64(0);
    const is_empty = try self.func().emitBinaryOp(.icmp_eq, len, zero, .i64);

    // Allocate loop counter on stack (BEFORE creating new blocks)
    const counter_ptr = try self.func().emitAlloca(.i64);
    try self.func().emitStore(counter_ptr.raw(), zero);

    // We'll need these indices later
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Create cond block and jump to it (we can't conditionally skip yet - need end block)
    const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_cond");

    // We're now in cond block - emit condition check (can't branch yet, need body block)
    const cond_len = try ManagedMemory.loadLen(self.func(), ir.toManagedMemoryPtr(array_ptr));
    const cond_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const done = try self.func().emitBinaryOp(.icmp_ge, cond_i, cond_len, .i64);

    // Create body block
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_body");

    // We're now in body block - emit loop body
    const body_buf_ptr = try self.func().emitLoad(array_ptr, .ptr);
    const body_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const elem_size = try self.func().emitConstI64(@intCast(elem_struct_info.size));
    const offset = try self.func().emitBinaryOp(.mul, body_i, elem_size, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, body_buf_ptr, offset, .ptr);

    // Clean up the element - THIS MAY CREATE NEW BLOCKS!
    // After this call, we may be in a different block than body_block_idx
    try cleanupStruct(self, elem_ptr, elem_struct_info, "<array element>");

    // Now we're in some block (either body if cleanupStruct didn't create blocks,
    // or whatever cleanup end block otherwise). Create next block and branch to it.
    const next_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_next");

    // We're now in next block - increment counter and loop back
    const one = try self.func().emitConstI64(1);
    const curr_i = try self.func().emitLoad(counter_ptr.raw(), .i64);
    const next_i = try self.func().emitBinaryOp(.add, curr_i, one, .i64);
    try self.func().emitStore(counter_ptr.raw(), next_i);
    try self.func().emitBr(cond_block_idx);

    // Create end block
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("elem_cleanup_end");

    // Now wire up the branches that need multiple block indices:
    // Entry -> cond (if not empty) or end (if empty)
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_empty }, .{ .block_ref = end_block_idx }, .{ .block_ref = cond_block_idx } },
    });

    // Cond -> body (if not done) or end (if done)
    try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = done }, .{ .block_ref = end_block_idx }, .{ .block_ref = body_block_idx } },
    });

    // The block we were in after cleanupStruct needs to branch to next.
    // That block is the one BEFORE next_block_idx.
    const cleanup_end_block_idx: u32 = next_block_idx - 1;
    try self.func().blocks.items[cleanup_end_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = next_block_idx }, undefined, undefined },
    });

    // end block is now current, which is what the caller expects
}

/// Recursively clean up struct fields that have needs_cleanup = true.
fn cleanupStructFields(self: *AstToIr, struct_ptr: ir.Value, struct_info: *const types.StructTypeInfo, var_name: []const u8) ConvertError!void {
    for (struct_info.fields) |field| {
        if (field.value_type == .struct_type) {
            const field_struct_info = field.value_type.struct_type;

            if (field_struct_info.needs_cleanup) {
                debug.astToIr("cleanupStructFields: cleaning field '{s}' of type '{s}'", .{ field.name, field_struct_info.name });

                // Get pointer to the field
                const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(struct_ptr), field.offset);

                // Recursively clean up the field (cleanupStruct handles element cleanup)
                try cleanupStruct(self, field_ptr.raw(), field_struct_info, var_name);
            }
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
    // Clean up any remaining temporary managed buffers (e.g., method call arguments)
    try cleanupTemporaryManagedBuffers(self);
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
