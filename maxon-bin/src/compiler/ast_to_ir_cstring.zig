/// CString IR emission helpers.
/// Contains layout constants and cleanup operations for C-style null-terminated strings.
const std = @import("std");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const string = @import("ast_to_ir_string.zig");
const ManagedArray = string.ManagedArray;

const AstToIr = @import("4-ast_to_ir.zig").AstToIr;

// ============================================================================
// CString Layout (24 bytes)
// ============================================================================
/// cstring struct layout (24 bytes total)
/// Layout: data(8) + length(8) + managed(8)
///
/// Fields:
/// - data: *u8 (8 bytes) - pointer to null-terminated C string data
/// - length: i64 (8 bytes) - length of string (not including null terminator)
/// - managed: *__ManagedArray (8 bytes) - pointer to parent ManagedArray if borrowed, null if owned
pub const CString = struct {
    const SIZE: i32 = 24;
    const DATA_OFFSET: i32 = 0;
    const LENGTH_OFFSET: i32 = 8;
    const MANAGED_OFFSET: i32 = 16;

    comptime {
        if (MANAGED_OFFSET + 8 != SIZE) {
            @compileError("CString layout mismatch: MANAGED_OFFSET + 8 != SIZE");
        }
        if (DATA_OFFSET != 0) {
            @compileError("CString layout mismatch: DATA_OFFSET != 0");
        }
        if (LENGTH_OFFSET != DATA_OFFSET + 8) {
            @compileError("CString layout mismatch: LENGTH_OFFSET != DATA_OFFSET + 8");
        }
        if (MANAGED_OFFSET != LENGTH_OFFSET + 8) {
            @compileError("CString layout mismatch: MANAGED_OFFSET != LENGTH_OFFSET + 8");
        }
    }

    // ========================================================================
    // Helper functions for CString layout
    // ========================================================================

    /// Returns the size of CString struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Load the data pointer (offset 0)
    pub fn loadData(func: *ir.Function, ptr: ir.Value) !ir.Value {
        return func.emitLoad(ptr, .ptr);
    }

    /// Load the length field (offset 8)
    pub fn loadLength(func: *ir.Function, ptr: ir.Value) !ir.Value {
        const len_ptr = try func.emitGetFieldPtr(ir.toStructPtr(ptr), LENGTH_OFFSET);
        return func.emitLoad(len_ptr.raw(), .i64);
    }

    /// Load the managed pointer (offset 16)
    pub fn loadManaged(func: *ir.Function, ptr: ir.Value) !ir.Value {
        const managed_ptr = try func.emitGetFieldPtr(ir.toStructPtr(ptr), MANAGED_OFFSET);
        return func.emitLoad(managed_ptr.raw(), .ptr);
    }

    /// Get pointer to the managed field
    pub fn getManagedPtr(func: *ir.Function, ptr: ir.Value) !ir.FieldPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(ptr), MANAGED_OFFSET);
    }

    /// Create field definitions for type registration
    pub fn createFieldDefs(allocator: std.mem.Allocator) ![]types.FieldInfo {
        const fields = try allocator.alloc(types.FieldInfo, 3);
        fields[0] = .{ .name = "data", .offset = DATA_OFFSET, .size = 8, .value_type = .{ .primitive = types.PTR } };
        fields[1] = .{ .name = "length", .offset = LENGTH_OFFSET, .size = 8, .value_type = .{ .primitive = types.INT } };
        fields[2] = .{ .name = "managed", .offset = MANAGED_OFFSET, .size = 8, .value_type = .{ .primitive = types.PTR } };
        return fields;
    }
};

// ============================================================================
// CString Cleanup
// ============================================================================

/// Emit cleanup for a cstring variable.
/// cstring struct: data(8) + length(8) + managed(8)
/// If managed != null: decref the __ManagedString (cstring borrowed from String)
/// If managed == null: free the data pointer (cstring owns buffer from slice copy)
pub fn emitCstringCleanup(self: *AstToIr, cstring_ptr: ir.Value) !void {
    // Load managed pointer (offset 16)
    const managed_field = try self.func().emitGetFieldPtr(ir.toStructPtr(cstring_ptr), CString.MANAGED_OFFSET);
    const managed_ptr = try self.func().emitLoad(managed_field.raw(), .ptr);

    // Check if managed != null
    const null_val = try self.func().emitConstI64(0);
    const is_not_null = try self.func().emitBinaryOp(.icmp_ne, managed_ptr, null_val, .i64);

    // Create all blocks in execution order
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_decref");

    // === DECREF BLOCK: managed != null, decref the __ManagedString ===
    const cap_ptr = try ManagedArray.getFlagsPtr(self.func(), ir.toManagedArrayPtr(managed_ptr));
    const cap_flags = try self.func().emitLoad(cap_ptr.raw(), .i32);
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
    const one_i32 = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

    const do_decref_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_do_decref");

    // In do_decref block: decrement refcount in buffer header and free if zero
    const decref_buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
    const eight_decref = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, decref_buf_ptr, eight_decref, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_ref = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one_ref, .i64);
    try self.func().emitStore(header_ptr, new_ref);
    const zero = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i64);

    const do_free_managed_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_free_managed");

    // In free managed block: free the buffer including header
    const free_buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
    const eight_free = try self.func().emitConstI64(8);
    const free_header_ptr = try self.func().emitBinaryOp(.sub, free_buf_ptr, eight_free, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(free_header_ptr), "string cleanup");

    const skip_decref_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_skip_decref");

    const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_free");

    // === FREE BLOCK: managed == null, free the data pointer ===
    const data_ptr = try self.func().emitLoad(cstring_ptr, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(data_ptr), "cstring release");

    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_cleanup_end");

    // Now go back and add all the branch instructions

    // Entry: if managed != null -> decref, else -> free
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_not_null }, .{ .block_ref = decref_block_idx }, .{ .block_ref = free_block_idx } },
    });

    // Decref block: if heap mode -> do_decref, else -> skip_decref
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = do_decref_idx }, .{ .block_ref = skip_decref_idx } },
    });

    // Do_decref block: if refcount zero -> free_managed, else -> skip_decref
    try self.func().blocks.items[do_decref_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = do_free_managed_idx }, .{ .block_ref = skip_decref_idx } },
    });

    // Free_managed block: goto end
    try self.func().blocks.items[do_free_managed_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });

    // Skip_decref block: goto end
    try self.func().blocks.items[skip_decref_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });

    // Free block: goto end
    try self.func().blocks.items[free_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });
}
