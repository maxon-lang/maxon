/// Managed buffer IR emission helpers.
/// Contains layout constants and COW (copy-on-write) refcounting operations.
const std = @import("std");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const struct_helpers = @import("ir_struct_helpers.zig");
const types = @import("ast_to_ir_types.zig");
const ConvertError = types.ConvertError;
const ValueType = types.ValueType;
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;
const DeferredBlocks = ast_to_ir.DeferredBlocks;
const array = @import("ast_to_ir_array.zig");

// Re-export ManagedArray from array module for convenience
pub const ManagedArray = array.ManagedArray;

// ============================================================================
// String Helpers
// ============================================================================

/// Create and initialize a __ManagedArray on the stack from string bytes.
/// Returns a pointer to the allocated struct.
///
/// String buffer layout with header:
///   [refcount: i64] [data bytes...] [null terminator]
///   ^               ^
///   |               +-- data_ptr (stored in struct)
///   +-- allocation base (header)
///
/// The refcount is stored at (data_ptr - 8), shared by all String copies.
pub fn emitManagedArrayFromBytes(self: *AstToIr, str_bytes: []const u8) !ir.ManagedArrayPtr {
    const managed_ptr = try ManagedArray.alloca(self.func());

    if (str_bytes.len == 0) {
        try ManagedArray.initEmpty(self.func(), managed_ptr);
    } else {
        // Heap allocation for string data WITH 8-byte header for refcount
        // Layout: [refcount:i64][data...][null]
        const buffer_size = try self.func().emitConstI64(@intCast(str_bytes.len + 1 + 8));
        const buffer_with_header = try self.func().emitHeapAlloc(buffer_size, "string buffer");

        // Initialize refcount in header to 1 (for the String being created)
        const one_refcount = try self.func().emitConstI64(1);
        if (debug.enabled) std.debug.print("[DEBUG] Emitting store 1 to header at {d}\n", .{buffer_with_header.raw()});
        try self.func().emitStore(buffer_with_header.raw(), one_refcount);

        // Data pointer is header + 8
        const data_ptr = try self.func().emitGetElemPtr(buffer_with_header, try self.func().emitConstI64(8), 1);

        // Store string bytes at data_ptr
        for (str_bytes, 0..) |byte, i| {
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const byte_ptr = try self.func().emitGetElemPtr(data_ptr.asRawPtr(), idx_val, 1);
            try self.func().emitStoreI8(byte_ptr.raw(), try self.func().emitConstI8(byte));
        }
        // Null terminate
        const null_idx = try self.func().emitConstI64(@intCast(str_bytes.len));
        try self.func().emitStoreI8((try self.func().emitGetElemPtr(data_ptr.asRawPtr(), null_idx, 1)).raw(), try self.func().emitConstI8(0));

        // Initialize __ManagedArray fields (mode=1 for heap-refcounted)
        // Store data_ptr (not the allocation base with header)
        try self.func().emitStore(managed_ptr.raw(), data_ptr.raw());
        const len_i64 = try self.func().emitConstI64(@intCast(str_bytes.len));
        try struct_helpers.storeI64Field(self.func(), managed_ptr.asStructPtr(), 8, len_i64);
        try struct_helpers.storeI64Field(self.func(), managed_ptr.asStructPtr(), 16, len_i64); // capacity = len
        try struct_helpers.storeI32Field(self.func(), managed_ptr.asStructPtr(), 24, try self.func().emitConstI32(1)); // flags = 1 (refcounted)
        try struct_helpers.storeI32Field(self.func(), managed_ptr.asStructPtr(), 28, try self.func().emitConstI32(0)); // parent_off = 0
    }

    return managed_ptr;
}

/// Create and initialize a __ManagedArray from a runtime buffer pointer and length.
/// Used for runtime string conversions (int to string, float to string, etc.)
pub fn emitManagedArrayFromBuffer(self: *AstToIr, buffer: ir.RawPtr, len_i64: ir.Value) !ir.ManagedArrayPtr {
    const ptr = try ManagedArray.alloca(self.func());
    const mode_one = try self.func().emitConstI32(1); // mode=1 for heap-refcounted
    try ManagedArray.init(self.func(), ptr, buffer, len_i64, len_i64, mode_one);
    return ptr;
}

/// Create a __ManagedArray pointing to static data in the .rdata section.
/// No heap allocation, no refcount. Mode=3 ensures it's never freed.
/// The string data is stored directly in the executable's code section.
pub fn emitManagedArrayFromStaticBytes(self: *AstToIr, str_bytes: []const u8) !ir.ManagedArrayPtr {
    const managed_ptr = try ManagedArray.alloca(self.func());

    if (str_bytes.len == 0) {
        try ManagedArray.initEmpty(self.func(), managed_ptr);
    } else {
        // Allocate space for the string data plus null terminator in the static data section
        const static_bytes = try self.allocator.alloc(u8, str_bytes.len + 1);
        @memcpy(static_bytes[0..str_bytes.len], str_bytes);
        static_bytes[str_bytes.len] = 0; // null terminator
        try self.module.trackString(static_bytes);

        // Get pointer to static string in .rdata section
        const static_ptr = try self.func().emitStringConstant(static_bytes);

        // Store static pointer as buffer
        try self.func().emitStore(managed_ptr.raw(), static_ptr.raw());

        // Set length and capacity
        const len_i64 = try self.func().emitConstI64(@intCast(str_bytes.len));
        try struct_helpers.storeI64Field(self.func(), managed_ptr.asStructPtr(), 8, len_i64);
        try struct_helpers.storeI64Field(self.func(), managed_ptr.asStructPtr(), 16, len_i64);

        // Set flags = 3 (static mode)
        try struct_helpers.storeI32Field(self.func(), managed_ptr.asStructPtr(), 24, try self.func().emitConstI32(3));
        try struct_helpers.storeI32Field(self.func(), managed_ptr.asStructPtr(), 28, try self.func().emitConstI32(0));
    }

    return managed_ptr;
}

// ============================================================================
// Type Creation via stdlib BuiltinLiteral interfaces
// ============================================================================

/// Create a String by calling the stdlib's String$init (BuiltinStringLiteral.init).
/// This is the proper way to create a String from a __ManagedArray - delegates
/// to stdlib rather than having compiler knowledge of String layout.
pub fn emitStringFromManaged(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr) !ir.Value {
    const result = try self.emitTypeInit("String", managed_ptr.raw());
    return result.ptr;
}

/// Create a Character by calling the stdlib's Character$init (BuiltinCharLiteral.init).
/// This is the proper way to create a Character from a __ManagedArray - delegates
/// to stdlib rather than having compiler knowledge of Character layout.
pub fn emitCharacterFromManaged(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr) !ir.Value {
    const result = try self.emitTypeInit("Character", managed_ptr.raw());
    return result.ptr;
}

/// Allocate a type's struct on the stack by looking up its size from the type_map.
/// Returns an uninitialized pointer that can be passed to functions.
pub fn emitTypeAlloca(self: *AstToIr, type_name: []const u8) !ir.RawPtr {
    const type_info = self.type_map.get(type_name) orelse {
        self.reportInternalError(type_name);
        return error.UnknownType;
    };
    return self.func().emitAllocaSized(type_info.struct_type.size);
}

// ============================================================================
// Struct Helpers
// ============================================================================

/// Copy a struct and handle refcount increment for types with __ManagedArray.
/// This is the single point of truth for struct copying with COW semantics.
/// Handles String, Array$T, cstring, and any other type with __ManagedArray at any offset.
pub fn emitStructCopy(self: *AstToIr, dest_ptr: ir.StructPtr, src_ptr: ir.StructPtr, size: i32, struct_name: ?[]const u8) !void {
    try self.func().emitMemcpy(dest_ptr.asRawPtr(), src_ptr.asRawPtr(), size);

    // Handle refcount for types with __ManagedArray
    if (struct_name) |name| {
        // Skip internal __ManagedArray type (it's moved, not copied)
        if (std.mem.eql(u8, name, "__ManagedArray")) return;

        // Look up struct info to check for __ManagedArray field
        if (self.type_map.get(name)) |type_info| {
            if (type_info == .struct_type) {
                const struct_info = &type_info.struct_type;
                if (struct_info.has_managed_buffer) {
                    // Get pointer to __ManagedArray at the correct offset
                    const managed_ptr = try getManagedArrayPtr(self, dest_ptr.raw(), struct_info.managed_buffer_offset);
                    // Incref the buffer since we're copying the struct
                    try array.emitManagedArrayIncref(self, managed_ptr, "<struct copy>");
                } else if (struct_info.is_cstring) {
                    // cstring has a managed pointer (offset 16) that may reference a __ManagedArray.
                    // If managed != null and the buffer is heap mode, we need to incref.
                    try emitCstringCopyIncref(self, dest_ptr);
                }
            }
        }
    }
}

/// Get pointer to __ManagedArray at the given offset within a struct.
pub fn getManagedArrayPtr(self: *AstToIr, struct_ptr: ir.Value, offset: i32) !ir.ManagedArrayPtr {
    if (offset == 0) {
        return ir.toManagedArrayPtr(struct_ptr);
    } else {
        const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(struct_ptr), offset);
        return ir.toManagedArrayPtr(field_ptr.raw());
    }
}

/// Move a struct WITHOUT incrementing refcount. Used when source is a temporary
/// that will be consumed (not kept alive after the move).
pub fn emitStructMove(self: *AstToIr, dest_ptr: ir.StructPtr, src_ptr: ir.StructPtr, size: i32) !void {
    try self.func().emitMemcpy(dest_ptr.asRawPtr(), src_ptr.asRawPtr(), size);
    // No incref - this is a move, not a copy
}

/// Emit incref for cstring copy.
/// cstring layout: data(8) + length(8) + managed(8)
/// If managed != null and points to a heap-mode __ManagedArray, incref its buffer.
fn emitCstringCopyIncref(self: *AstToIr, cstring_ptr: ir.StructPtr) !void {
    const cstring = @import("ast_to_ir_cstring.zig");

    // Load the managed pointer (offset 16)
    const managed_field = try self.func().emitGetFieldPtr(cstring_ptr, cstring.CString.MANAGED_OFFSET);
    const managed_ptr = try self.func().emitLoad(managed_field.raw(), .ptr);

    // Check if managed != null
    const null_val = try self.func().emitConstI64(0);
    const is_not_null = try self.func().emitBinaryOp(.icmp_ne, managed_ptr, null_val, .i64);

    // Create blocks for conditional incref
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    const check_heap_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_copy_check_heap");

    // Check if the managed array is in heap mode (flags & 3 == 1)
    const flags_ptr = try ManagedArray.getFlagsPtr(self.func(), ir.toManagedArrayPtr(managed_ptr));
    const flags = try self.func().emitLoad(flags_ptr.raw(), .i32);
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, flags, three, .i32);
    const one = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one, .i32);

    const do_incref_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_copy_incref");

    // Incref: load buffer pointer, subtract 8 to get header, increment refcount
    const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_i64 = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one_i64, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    const end_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_copy_end");

    // Wire up branches
    // Entry: if managed != null -> check_heap, else -> end
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_not_null }, .{ .block_ref = check_heap_idx }, .{ .block_ref = end_idx } },
    });

    // check_heap: if heap mode -> do_incref, else -> end
    try self.func().blocks.items[check_heap_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = do_incref_idx }, .{ .block_ref = end_idx } },
    });

    // do_incref: goto end
    try self.func().blocks.items[do_incref_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_idx }, .none, .none },
        .result = null,
    });
}
