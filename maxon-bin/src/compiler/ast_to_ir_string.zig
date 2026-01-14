/// String IR emission helpers.
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
// String Layout (40 bytes)
// ============================================================================
/// String struct layout (40 bytes total)
/// This is the user-facing String type that wraps __ManagedArray
/// Layout: __ManagedArray(32) + _iterPos(8) = 40 bytes
pub const String = struct {
    const SIZE: i32 = 40;
    const MANAGED_ARRAY_OFFSET: i32 = 0;
    const ITER_POS_OFFSET: i32 = 32;

    comptime {
        if (SIZE < ManagedArray.size()) {
            @compileError("String layout mismatch: SIZE < ManagedArray.size()");
        }
        if (ITER_POS_OFFSET != ManagedArray.size()) {
            @compileError("String layout mismatch: ITER_POS_OFFSET != ManagedArray.size()");
        }
        if (ITER_POS_OFFSET + 8 != SIZE) {
            @compileError("String layout mismatch: ITER_POS_OFFSET + 8 != SIZE");
        }
    }

    // ========================================================================
    // Helper functions for String layout
    // ========================================================================

    /// Returns the size of String struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Allocate a String on the stack
    pub fn alloca(func: *ir.Function) !ir.RawPtr {
        return func.emitAllocaSized(SIZE);
    }

    /// Store a value to the _iterPos field
    pub fn storeIterPos(func: *ir.Function, ptr: ir.Value, value: ir.Value) !void {
        const iter_pos_ptr = try func.emitGetFieldPtr(ir.toStructPtr(ptr), ITER_POS_OFFSET);
        try func.emitStore(iter_pos_ptr.raw(), value);
    }

    /// Initialize the _iterPos field to 0
    pub fn initIterPos(func: *ir.Function, ptr: ir.Value) !void {
        const zero = try func.emitConstI64(0);
        try storeIterPos(func, ptr, zero);
    }

    /// Initialize a String from a ManagedArray by copying the ManagedArray portion
    /// and initializing _iterPos to 0
    pub fn initFromManagedArray(func: *ir.Function, string_ptr: ir.RawPtr, managed_ptr: ir.ManagedArrayPtr) !void {
        try func.emitMemcpy(string_ptr, managed_ptr.asRawPtr(), ManagedArray.size());
        try initIterPos(func, string_ptr.raw());
    }
};

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
// String Helpers
// ============================================================================

/// Allocate a String struct on the stack.
/// Returns an uninitialized String pointer that can be passed to functions
/// or initialized with emitStringFromManaged.
pub fn emitStringAlloca(self: *AstToIr) !ir.RawPtr {
    return self.func().emitAllocaSized(String.SIZE);
}

/// Wrap a __ManagedArray pointer in a full String struct (adds _iterPos field).
pub fn emitStringFromManaged(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr) !ir.StringPtr {
    const string_ptr = try emitStringAlloca(self);
    try self.func().emitMemcpy(string_ptr, managed_ptr.asRawPtr(), ManagedArray.size());
    const iter_pos_ptr = try self.func().emitGetFieldPtr(string_ptr.asStruct(), String.ITER_POS_OFFSET);
    try self.func().emitStore(iter_pos_ptr.raw(), try self.func().emitConstI64(0));
    return string_ptr.asStringPtr();
}

/// Check if a type is the String type (which contains __ManagedString)
pub fn isStringType(ty: ValueType) bool {
    return ty == .struct_type and std.mem.eql(u8, ty.struct_type.name, "String");
}

/// Check if a struct has __ManagedArray as its first field.
/// This indicates the struct uses COW refcounting for its buffer.
pub fn hasManagedArrayFirstField(struct_info: *const types.StructTypeInfo) bool {
    if (struct_info.fields.len == 0) return false;
    const first_field = struct_info.fields[0];
    if (first_field.value_type != .struct_type) return false;
    return std.mem.eql(u8, first_field.value_type.struct_type.name, "__ManagedArray");
}

/// Copy a struct and handle refcount increment for types with __ManagedArray.
/// This is the single point of truth for struct copying with COW semantics.
/// Handles String, Array$T, and any other type with __ManagedArray at offset 0.
pub fn emitStructCopy(self: *AstToIr, dest_ptr: ir.StructPtr, src_ptr: ir.StructPtr, size: i32, struct_name: ?[]const u8) !void {
    try self.func().emitMemcpy(dest_ptr.asRawPtr(), src_ptr.asRawPtr(), size);

    // Handle refcount for types with __ManagedArray at offset 0
    if (struct_name) |name| {
        // Skip internal __ManagedArray type (it's moved, not copied)
        if (std.mem.eql(u8, name, "__ManagedArray")) return;

        // Look up struct info to check for __ManagedArray first field
        if (self.type_map.get(name)) |type_info| {
            if (type_info == .struct_type) {
                if (hasManagedArrayFirstField(&type_info.struct_type)) {
                    // Incref the buffer since we're copying the struct
                    try array.emitManagedArrayIncref(self, ir.toManagedArrayPtr(dest_ptr.raw()), "<struct copy>");
                }
            }
        }
    }
}

/// Move a struct WITHOUT incrementing refcount. Used when source is a temporary
/// that will be consumed (not kept alive after the move).
pub fn emitStructMove(self: *AstToIr, dest_ptr: ir.StructPtr, src_ptr: ir.StructPtr, size: i32) !void {
    try self.func().emitMemcpy(dest_ptr.asRawPtr(), src_ptr.asRawPtr(), size);
    // No incref - this is a move, not a copy
}

/// Check if a string is in heap-refcounted mode by examining flags.
/// Returns an IR value representing the boolean result (flags & 0x3 == 1).
/// String layout: _managed at offset 0, flags at offset 24 within _managed.
pub fn emitStringIsHeapMode(self: *AstToIr, string_ptr: ir.StringPtr) !ir.Value {
    const flags_ptr = try ManagedArray.getFlagsPtr(self.func(), ir.toManagedArrayPtr(string_ptr.raw()));
    const flags = try self.func().emitLoad(flags_ptr.raw(), .i32);
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, flags, three, .i32);
    const one_i32 = try self.func().emitConstI32(1);
    return try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);
}

/// Emit incref for a String variable.
/// The string_ptr should point to a String struct (with _managed at offset 0).
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// This is shared by all String copies that reference the same buffer.
pub fn emitStringIncref(self: *AstToIr, string_ptr: ir.StringPtr, tag: []const u8) !void {
    const is_heap = try emitStringIsHeapMode(self, string_ptr);

    // Create blocks for conditional incref
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("incref");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("incref_end");

    // Defer end block
    var deferred = try DeferredBlocks.init(self.allocator, 1);
    defer deferred.deinit();
    deferred.deferBlocks(self, 1);

    // Emit conditional branch from entry block
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In incref block: increment refcount in buffer header (at buffer_ptr - 8)
    // Load buffer pointer from string struct
    const buf_ptr = try self.func().emitLoad(string_ptr.raw(), .ptr);
    // Calculate header address: buf_ptr - 8 (use .ptr for pointer arithmetic)
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);
    // Incref the i64 refcount in the header
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for incref (new_ref is the refcount after increment)
    if (self.track_memory) {
        // Track call expects i32, so truncate
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackIncref(tag, new_ref_i32);
    }

    // Branch to end
    try self.func().emitBr(end_block_idx);

    // Restore end block
    try deferred.restore(self, 0);
}

/// Emit decref for a String variable with conditional free when refcount reaches 0.
/// The string_ptr should point to a String struct (with _managed at offset 0).
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// When refcount reaches 0, we free (buffer_ptr - 8) to include the header.
pub fn emitStringDecref(self: *AstToIr, string_ptr: ir.StringPtr, tag: []const u8) !void {
    const is_heap = try emitStringIsHeapMode(self, string_ptr);

    // Create blocks for conditional decref
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref");
    const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref_free");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref_end");

    debug.astToIr("emitStringDecref: entry={d}, decref={d}, free={d}, end={d}", .{ entry_block_idx, decref_block_idx, free_block_idx, end_block_idx });

    // Defer end and free blocks (end=0, free=1 after pop order)
    var deferred = try DeferredBlocks.init(self.allocator, 2);
    defer deferred.deinit();
    deferred.deferBlocks(self, 2);

    // Emit conditional branch from entry block to decref or end
    debug.astToIr("  br_cond in block {d}: if heap -> {d}, else -> {d}", .{ entry_block_idx, decref_block_idx, end_block_idx });
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In decref block: load buffer pointer, compute header address, decrement refcount
    const buf_ptr = try self.func().emitLoad(string_ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);

    // Decrement the i64 refcount in the header
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for decref (new_ref is the refcount after decrement)
    if (self.track_memory) {
        // Track call expects i32, so truncate
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackDecref(tag, new_ref_i32);
    }

    // Check if refcount is now zero
    const zero = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i64);

    // Restore free block (index 1 = second popped = free_block)
    try deferred.restore(self, 1);

    // Emit branch from decref block to free or end
    debug.astToIr("  br_cond in block {d}: if zero -> {d}, else -> {d}", .{ decref_block_idx, free_block_idx, end_block_idx });
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = free_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In free block: free the buffer WITH header (header_ptr, not buf_ptr)
    // Need to reload header_ptr since we're in a different block
    const buf_ptr2 = try self.func().emitLoad(string_ptr.raw(), .ptr);
    const header_ptr2 = try self.func().emitBinaryOp(.sub, buf_ptr2, eight, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(header_ptr2), "string cleanup");

    // Branch to end
    debug.astToIr("  br in block {d}: -> {d}", .{ free_block_idx, end_block_idx });
    try self.func().emitBr(end_block_idx);

    // Restore end block (index 0 = first popped = end_block)
    try deferred.restore(self, 0);
    debug.astToIr("  restored end block, current block is now {d}", .{self.func().blocks.items.len - 1});
}
