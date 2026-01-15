/// Array and ManagedArray IR emission helpers.
/// Contains layout constants for ManagedArray and Array types.
/// ManagedArray is the unified managed type for both strings and arrays.
const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const struct_helpers = @import("ir_struct_helpers.zig");
const types = @import("ast_to_ir_types.zig");
const ast_to_ir = @import("4-ast_to_ir.zig");
const string = @import("ast_to_ir_managed.zig");
const cleanup = @import("ast_to_ir_cleanup.zig");

const AstToIr = ast_to_ir.AstToIr;

// ============================================================================
// Mode Constants for ManagedArray
// ============================================================================
/// Mode values for __ManagedArray flags (bits 0-1):
pub const MODE_SSO: i32 = 0; // Small storage optimization (future) - inline storage in struct
pub const MODE_HEAP: i32 = 1; // Refcounted heap allocation, decref at buffer-8
pub const MODE_SLICE: i32 = 2; // Borrowed view, do not free
pub const MODE_STATIC: i32 = 3; // Read-only data section, never free
pub const MODE_MASK: i32 = 0x3; // Mask for mode bits (2 bits)
const BranchBuilder = ast_to_ir.BranchBuilder;
const TypedValue = types.TypedValue;
const ValueType = types.ValueType;
const VarInfo = types.VarInfo;
const ConvertError = types.ConvertError;

// ============================================================================
// ManagedArray Layout (32 bytes)
// ============================================================================
/// __ManagedArray struct layout (32 bytes total)
/// Unified managed type for both strings and arrays.
/// Layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)
///
/// Fields:
/// - buffer: *T (8 bytes) - pointer to element data (for refcounted mode, refcount is at buffer - 8)
/// - len: i64 (8 bytes) - current length
/// - capacity: i64 (8 bytes) - allocated capacity
/// - flags: i32 (4 bytes) - mode flags (bits 0-1: 0=SSO, 1=heap-refcounted, 2=slice, 3=static)
/// - parent_off: i32 (4 bytes) - offset from parent buffer for slice mode
///
/// Mode semantics:
/// - Mode 0 (SSO): Small storage optimization (future) - inline storage in struct
/// - Mode 1 (heap-refcounted): Ref-counted heap allocation, refcount at buffer - 8
/// - Mode 2 (slice): Zero-copy view into parent buffer, uses parent_off
/// - Mode 3 (static): Pointer to read-only data section, never freed
pub const ManagedArray = struct {
    const SIZE: i32 = 32;
    const BUFFER_OFFSET: i32 = 0;
    const LEN_OFFSET: i32 = 8;
    const CAPACITY_OFFSET: i32 = 16;
    const FLAGS_OFFSET: i32 = 24;
    const PARENT_OFF_OFFSET: i32 = 28;

    comptime {
        if (PARENT_OFF_OFFSET + 4 != SIZE) {
            @compileError("ManagedArray layout mismatch: PARENT_OFF_OFFSET + 4 != SIZE");
        }
        if (LEN_OFFSET != BUFFER_OFFSET + 8) {
            @compileError("ManagedArray layout mismatch: LEN_OFFSET != BUFFER_OFFSET + 8");
        }
        if (CAPACITY_OFFSET != LEN_OFFSET + 8) {
            @compileError("ManagedArray layout mismatch: CAPACITY_OFFSET != LEN_OFFSET + 8");
        }
        if (FLAGS_OFFSET != CAPACITY_OFFSET + 8) {
            @compileError("ManagedArray layout mismatch: FLAGS_OFFSET != CAPACITY_OFFSET + 8");
        }
        if (PARENT_OFF_OFFSET != FLAGS_OFFSET + 4) {
            @compileError("ManagedArray layout mismatch: PARENT_OFF_OFFSET != FLAGS_OFFSET + 4");
        }
    }

    // ========================================================================
    // Helper functions for ManagedArray layout
    // ========================================================================

    /// Returns the size of ManagedArray struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Allocate a ManagedArray on the stack
    pub fn alloca(func: *ir.Function) !ir.ManagedArrayPtr {
        const raw_ptr = try func.emitAllocaSized(SIZE);
        return raw_ptr.asManagedArrayPtr();
    }

    /// Load the buffer pointer (offset 0)
    pub fn loadBuffer(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        return func.emitLoad(ptr.raw(), .ptr);
    }

    /// Load the length field (offset 8)
    pub fn loadLen(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        const len_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), LEN_OFFSET);
        return func.emitLoad(len_ptr.raw(), .i64);
    }

    /// Get pointer to the length field
    pub fn getLenPtr(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), LEN_OFFSET);
    }

    /// Load the capacity field (offset 16)
    pub fn loadCapacity(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        const cap_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), CAPACITY_OFFSET);
        return func.emitLoad(cap_ptr.raw(), .i64);
    }

    /// Get pointer to the capacity field
    pub fn getCapacityPtr(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), CAPACITY_OFFSET);
    }

    /// Load the flags field (offset 24) as i32
    pub fn loadFlags(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        const flags_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), FLAGS_OFFSET);
        return func.emitLoad(flags_ptr.raw(), .i32);
    }

    /// Get pointer to the flags field
    pub fn getFlagsPtr(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), FLAGS_OFFSET);
    }

    /// Check if the ManagedArray is in heap mode (flags & 3 == 1)
    pub fn isHeapMode(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        const flags = try loadFlags(func, ptr);
        const three = try func.emitConstI32(3);
        const masked = try func.emitBinaryOp(.band, flags, three, .i32);
        const one = try func.emitConstI32(1);
        return func.emitBinaryOp(.icmp_eq, masked, one, .i32);
    }

    /// Load the mode value (flags & 3)
    pub fn loadMode(func: *ir.Function, ptr: ir.ManagedArrayPtr) !ir.Value {
        const flags = try loadFlags(func, ptr);
        const three = try func.emitConstI32(3);
        return func.emitBinaryOp(.band, flags, three, .i32);
    }

    /// Initialize a ManagedArray with all fields
    pub fn init(func: *ir.Function, ptr: ir.ManagedArrayPtr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value, flags: ir.Value) !void {
        const struct_ptr = ptr.asStructPtr();
        // buffer pointer at offset 0
        try func.emitStore(struct_ptr.raw(), buffer.raw());
        // len (i64) at offset 8
        try struct_helpers.storeI64Field(func, struct_ptr, LEN_OFFSET, len);
        // capacity (i64) at offset 16
        try struct_helpers.storeI64Field(func, struct_ptr, CAPACITY_OFFSET, capacity);
        // flags (i32) at offset 24
        try struct_helpers.storeI32Field(func, struct_ptr, FLAGS_OFFSET, flags);
        // parent_off (i32) at offset 28 - initialized to 0
        const zero_i32 = try func.emitConstI32(0);
        try struct_helpers.storeI32Field(func, struct_ptr, PARENT_OFF_OFFSET, zero_i32);
    }

    /// Initialize a ManagedArray as empty (null buffer, zero len/capacity/flags/parent_off)
    pub fn initEmpty(func: *ir.Function, ptr: ir.ManagedArrayPtr) !void {
        const struct_ptr = ptr.asStructPtr();
        const null_ptr = try func.emitConstI64(0);
        const zero_i32 = try func.emitConstI32(0);

        try func.emitStore(struct_ptr.raw(), null_ptr); // buffer at offset 0
        try struct_helpers.storeI64Field(func, struct_ptr, LEN_OFFSET, null_ptr); // len
        try struct_helpers.storeI64Field(func, struct_ptr, CAPACITY_OFFSET, null_ptr); // capacity
        try struct_helpers.storeI32Field(func, struct_ptr, FLAGS_OFFSET, zero_i32); // flags
        try struct_helpers.storeI32Field(func, struct_ptr, PARENT_OFF_OFFSET, zero_i32); // parent_off
    }

    /// Create field definitions for type registration
    pub fn createFieldDefs(allocator: std.mem.Allocator) ![]types.FieldInfo {
        const fields = try allocator.alloc(types.FieldInfo, 5);
        fields[0] = .{ .name = "_buffer", .offset = BUFFER_OFFSET, .size = 8, .value_type = .{ .primitive = .ptr } };
        fields[1] = .{ .name = "_len", .offset = LEN_OFFSET, .size = 8, .value_type = .{ .primitive = .int } };
        fields[2] = .{ .name = "_capacity", .offset = CAPACITY_OFFSET, .size = 8, .value_type = .{ .primitive = .int } };
        fields[3] = .{ .name = "_flags", .offset = FLAGS_OFFSET, .size = 4, .value_type = .{ .primitive = .int } };
        fields[4] = .{ .name = "_parent_off", .offset = PARENT_OFF_OFFSET, .size = 4, .value_type = .{ .primitive = .int } };
        return fields;
    }
};

// ============================================================================
// Array Layout (40 bytes)
// ============================================================================
/// Array struct layout (40 bytes total)
/// This is the user-facing Array type that wraps __ManagedArray
/// Layout: iterIndex(8) + __ManagedArray(32) = 40 bytes
pub const Array = struct {
    const SIZE: i32 = 40;
    const ITER_INDEX_OFFSET: i32 = 0;
    const MANAGED_ARRAY_OFFSET: i32 = 8;

    comptime {
        if (SIZE < ManagedArray.SIZE) {
            @compileError("Array layout mismatch: SIZE < ManagedArray.SIZE");
        }
        if (MANAGED_ARRAY_OFFSET + ManagedArray.SIZE != SIZE) {
            @compileError("Array layout mismatch: MANAGED_ARRAY_OFFSET + ManagedArray.SIZE != SIZE");
        }
        if (ITER_INDEX_OFFSET + 8 != MANAGED_ARRAY_OFFSET) {
            @compileError("Array layout mismatch: ITER_INDEX_OFFSET + 8 != MANAGED_ARRAY_OFFSET");
        }
    }

    // ========================================================================
    // Helper functions for Array layout
    // ========================================================================

    /// Returns the size of Array struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Allocate an Array on the stack
    pub fn alloca(func: *ir.Function) !ir.RawPtr {
        return func.emitAllocaSized(SIZE);
    }

    /// Store the iter index field (offset 0)
    pub fn storeIterIndex(func: *ir.Function, ptr: ir.StructPtr, value: ir.Value) !void {
        try struct_helpers.storeI64Field(func, ptr, ITER_INDEX_OFFSET, value);
    }

    /// Load the iter index field (offset 0)
    pub fn loadIterIndex(func: *ir.Function, ptr: ir.StructPtr) !ir.Value {
        return struct_helpers.loadI64Field(func, ptr, ITER_INDEX_OFFSET);
    }
};

// ============================================================================
// ManagedArray Helpers
// ============================================================================

/// Allocate a __ManagedArray on the stack and initialize it as empty.
pub fn emitEmptyManagedArray(self: *AstToIr) !ir.ManagedArrayPtr {
    const ptr = try ManagedArray.alloca(self.func());
    try ManagedArray.initEmpty(self.func(), ptr);
    return ptr;
}

/// Initialize an existing __ManagedArray as empty.
pub fn initManagedArrayEmpty(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr) !void {
    return ManagedArray.initEmpty(self.func(), managed_ptr);
}

/// Initialize an existing __ManagedArray with buffer, length, and capacity values.
/// Uses mode=0 (no refcounting) for regular arrays.
pub fn initManagedArray(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !void {
    const zero_flags = try self.func().emitConstI32(0); // mode=0 (no refcounting for regular arrays)
    return ManagedArray.init(self.func(), managed_ptr, buffer, len, capacity, zero_flags);
}

/// Allocate a __ManagedArray on the stack and initialize with buffer, length, and capacity.
/// Uses mode=1 (heap) for regular arrays that own their buffer.
pub fn emitManagedArray(self: *AstToIr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !ir.ManagedArrayPtr {
    const ptr = try ManagedArray.alloca(self.func());
    const heap_flags = try self.func().emitConstI32(MODE_HEAP); // mode=1 (heap, owns buffer)
    try ManagedArray.init(self.func(), ptr, buffer, len, capacity, heap_flags);
    return ptr;
}

/// Allocate a refcounted buffer and create a __ManagedArray pointing to it.
/// The buffer includes an 8-byte refcount header at (buffer - 8).
/// Use this for arrays that need COW semantics with shared ownership.
pub fn emitRefcountedManagedArray(self: *AstToIr, data_size: ir.Value, len: ir.Value, capacity: ir.Value, tag: []const u8) !ir.ManagedArrayPtr {
    const buffer = try emitAllocRefcountedBuffer(self, data_size, tag);
    return emitManagedArray(self, buffer, len, capacity);
}

/// Size of refcounted buffer header (refcount as i64)
pub const REFCOUNTED_BUFFER_HEADER_SIZE: i64 = 8;

/// Allocate a refcounted buffer with header.
/// Returns the DATA pointer (header is at returned_ptr - 8).
///
/// Buffer layout:
///   [refcount: i64] [data bytes...]
///   ^               ^
///   |               +-- returned data_ptr
///   +-- allocation base (header)
///
/// The refcount is initialized to 1 (for the ManagedArray being created).
pub fn emitAllocRefcountedBuffer(self: *AstToIr, data_size: ir.Value, tag: []const u8) !ir.RawPtr {
    const func = self.func();
    // Allocate data_size + 8 for header
    const header_size = try func.emitConstI64(REFCOUNTED_BUFFER_HEADER_SIZE);
    const total_size = try func.emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try func.emitHeapAlloc(total_size, tag);

    // Initialize refcount in header to 1 (for the ManagedArray being created)
    try func.emitStore(buffer_with_header.raw(), try func.emitConstI64(1));

    // Return data pointer (header + 8)
    const eight = try func.emitConstI64(8);
    const data_ptr = try func.emitBinaryOp(.add, buffer_with_header.raw(), eight, .ptr);
    return ir.toRawPtr(data_ptr);
}

/// Emit incref for a ManagedArray.
/// The ptr should point to a __ManagedArray or a struct with __ManagedArray at offset 0.
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// This is shared by all ManagedArray copies that reference the same buffer.
pub fn emitManagedArrayIncref(self: *AstToIr, ptr: ir.ManagedArrayPtr, tag: []const u8) !void {
    const is_heap = try ManagedArray.isHeapMode(self.func(), ptr);

    // Create blocks for conditional incref
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("incref");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("incref_end");

    // Defer end block
    var deferred = try ast_to_ir.DeferredBlocks.init(self.allocator, 1);
    defer deferred.deinit();
    deferred.deferBlocks(self, 1);

    // Emit conditional branch from entry block
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In incref block: increment refcount in buffer header (at buffer_ptr - 8)
    const buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for incref
    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackIncref(tag, new_ref_i32);
    }

    // Branch to end
    try self.func().emitBr(end_block_idx);

    // Restore end block
    try deferred.restore(self, 0);
}

/// Emit decref for a ManagedArray with conditional free when refcount reaches 0.
/// The ptr should point to a __ManagedArray or a struct with __ManagedArray at offset 0.
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// When refcount reaches 0, we free (buffer_ptr - 8) to include the header.
/// Decref a managed array buffer (COW semantics).
/// If refcount reaches 0, frees the buffer.
/// `tag` is used for memory tracking (variable name).
/// `cleanup_tag` is used for heap free description (e.g., "string cleanup", "array cleanup").
pub fn emitManagedArrayDecref(self: *AstToIr, ptr: ir.ManagedArrayPtr, tag: []const u8, cleanup_tag: []const u8) !void {
    const is_heap = try ManagedArray.isHeapMode(self.func(), ptr);

    // Create blocks for conditional decref
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref");
    const free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref_free");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref_end");

    // Defer end and free blocks
    var deferred = try ast_to_ir.DeferredBlocks.init(self.allocator, 2);
    defer deferred.deinit();
    deferred.deferBlocks(self, 2);

    // Emit conditional branch from entry block to decref or end
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = decref_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In decref block: load buffer pointer, compute header address, decrement refcount
    const buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);

    // Decrement the i64 refcount in the header
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    // Emit tracking call for decref
    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackDecref(tag, new_ref_i32);
    }

    // Check if refcount is now zero
    const zero = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i64);

    // Restore free block
    try deferred.restore(self, 1);

    // Emit branch from decref block to free or end
    try self.func().blocks.items[decref_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_zero }, .{ .block_ref = free_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // In free block: free the buffer WITH header (header_ptr, not buf_ptr)
    const buf_ptr2 = try self.func().emitLoad(ptr.raw(), .ptr);
    const header_ptr2 = try self.func().emitBinaryOp(.sub, buf_ptr2, eight, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(header_ptr2), cleanup_tag);

    // Branch to end
    try self.func().emitBr(end_block_idx);

    // Restore end block
    try deferred.restore(self, 0);
}

/// Index into a __ManagedArray: managed[i]
/// Returns the element as an error union (bounds-checked, throws ArrayError on out of bounds)
/// Uses current_type_name or var_name context to determine element type
pub fn convertManagedArrayIndex(self: *AstToIr, managed_ptr: ir.Value, index_expr: ast.Expression, var_name: ?[]const u8) ConvertError!TypedValue {
    const idx_typed = try self.convertExpression(index_expr);

    // Extract element type from context
    // 1. First try current_type_name (e.g., "Array$String" -> "String")
    // 2. If that fails and we're in a Map context with a known var name, use the appropriate type param
    const elem_type_name: ?[]const u8 = blk: {
        // Try Array$* pattern first
        if (self.current_type_name) |type_name| {
            if (std.mem.startsWith(u8, type_name, "Array$")) {
                break :blk type_name[6..]; // Skip "Array$" prefix
            }
        }
        // Check for Map$K$V context with known var names
        if (var_name) |vn| {
            if (self.current_type_name) |type_name| {
                if (std.mem.startsWith(u8, type_name, "Map$")) {
                    // In Map$Key$Value, "keys" param has Key elements, "values" param has Value elements
                    // Use generic_params to resolve to the actual types
                    if (std.mem.eql(u8, vn, "keys")) {
                        break :blk self.generic_params.get("Key");
                    } else if (std.mem.eql(u8, vn, "values")) {
                        break :blk self.generic_params.get("Value");
                    }
                }
            }
        }
        break :blk null;
    };

    // Determine element size and whether it's a struct (with monomorphization fallback)
    const ElemInfo = struct { size: i32, is_struct: bool, name: ?[]const u8, has_managed_buffer: bool = false, managed_buffer_offset: i32 = 0 };
    const elem_info: ElemInfo = if (elem_type_name) |tn| blk: {
        // First try direct lookup
        if (self.type_map.get(tn)) |type_info| {
            if (type_info == .struct_type) {
                break :blk ElemInfo{ .size = type_info.struct_type.size, .is_struct = true, .name = tn, .has_managed_buffer = type_info.struct_type.has_managed_buffer, .managed_buffer_offset = type_info.struct_type.managed_buffer_offset };
            }
        }
        // Try with monomorphization for generic types like Array$String
        if (self.getStructSizeWithMonomorphization(tn)) |size| {
            // After monomorphization, look up has_managed_buffer and managed_buffer_offset
            const hmb = if (self.type_map.get(tn)) |ti| ti == .struct_type and ti.struct_type.has_managed_buffer else false;
            const mbo = if (self.type_map.get(tn)) |ti| if (ti == .struct_type) ti.struct_type.managed_buffer_offset else 0 else 0;
            break :blk ElemInfo{ .size = size, .is_struct = true, .name = tn, .has_managed_buffer = hmb, .managed_buffer_offset = mbo };
        }
        // Must be a primitive type - get its actual size
        if (types.getPrimitiveTypeInfo(tn)) |prim_info| {
            break :blk ElemInfo{ .size = prim_info.array_element_size, .is_struct = false, .name = tn };
        }
        self.reportInternalError("unknown element type in array index");
        return error.SemanticError;
    } else {
        self.reportInternalError("cannot determine array element type - missing type context");
        return error.SemanticError;
    };

    // Load buffer pointer (offset 0) and length (offset 8)
    const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
    const len_ptr = try ManagedArray.getLenPtr(self.func(), ir.toManagedArrayPtr(managed_ptr));
    const len = try self.func().emitLoad(len_ptr.raw(), .i64);

    // Calculate error union size: 8 (tag) + max(element size, 8 for error enum)
    const eu_size: i32 = 8 + @max(elem_info.size, 8);
    const eu_ptr = try self.func().emitAllocaSized(eu_size);

    // Bounds check: index >= 0 AND index < len
    const zero = try self.func().emitConstI64(0);
    const is_non_negative = try self.func().emitBinaryOp(.icmp_ge, idx_typed.value, zero, .i64);
    const is_less_than_len = try self.func().emitBinaryOp(.icmp_lt, idx_typed.value, len, .i64);
    const in_bounds = try self.func().emitBinaryOp(.mul, is_non_negative, is_less_than_len, .i64);

    // Create 2-way branch: in_bounds -> get element (success), else -> return error
    var branch = try BranchBuilder.init(self, in_bounds, "managed_index_in_bounds", "managed_index_out_of_bounds", "managed_index_merge");
    defer branch.deinit();

    // Then block: success - store element (tag=0 means success)
    const success_tag = try self.func().emitConstI64(0);
    try self.func().emitStore(eu_ptr.raw(), success_tag);

    // Calculate element pointer: buf_ptr + index * elem_size
    const elem_size_val = try self.func().emitConstI64(elem_info.size);
    const offset = try self.func().emitBinaryOp(.mul, idx_typed.value, elem_size_val, .i64);
    const elem_ptr = try self.func().emitBinaryOp(.add, buf_ptr, offset, .ptr);

    const value_slot = try self.func().emitGetFieldPtr(ir.toStructPtr(eu_ptr.raw()), 8); // value at offset 8

    if (elem_info.is_struct) {
        // For struct elements, copy the struct data into the error union
        try self.func().emitMemcpy(value_slot.asRawPtr(), ir.toRawPtr(elem_ptr), elem_info.size);
    } else {
        // For primitive elements, load and store the value
        const val = try self.func().emitLoad(elem_ptr, .i64);
        try self.func().emitStore(value_slot.raw(), val);
    }

    // Switch to else block
    try branch.switchToElse(true);

    // Else block: error - store ArrayError.IndexOutOfBounds (ordinal 0, tag=1 means error)
    const error_tag = try self.func().emitConstI64(1);
    try self.func().emitStore(eu_ptr.raw(), error_tag);
    const error_ordinal = try self.func().emitConstI64(0); // IndexOutOfBounds = 0
    const error_slot = try self.func().emitGetFieldPtr(ir.toStructPtr(eu_ptr.raw()), 8);
    try self.func().emitStore(error_slot.raw(), error_ordinal);

    // Switch to merge block
    try branch.switchToMerge(true);

    // For elements with COW semantics (has_managed_buffer), we need to increment the
    // refcount since we're copying out of the array. The array still owns its copy,
    // and the caller will own this copy.
    // Note: We do this AFTER the merge block so we can safely create branch blocks
    // for the conditional incref without interfering with the BranchBuilder.
    if (elem_info.has_managed_buffer) {
        // Only incref if the error union has a success value (tag == 0)
        const tag = try self.func().emitLoad(eu_ptr.raw(), .i64);
        const zero_const = try self.func().emitConstI64(0);
        const is_success = try self.func().emitBinaryOp(.icmp_eq, tag, zero_const, .i64);

        // Create conditional block for incref - using simple pattern without deferred blocks
        // since emitManagedArrayIncref will create its own blocks
        const entry_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const incref_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("array_get_incref");

        // We'll add the end block AFTER emitManagedArrayIncref to avoid index confusion
        // For now, emit conditional branch with placeholder (we'll fix the end index)
        const branch_instr_idx = self.func().blocks.items[entry_idx].instructions.items.len;
        try self.func().blocks.items[entry_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_success }, .{ .block_ref = incref_idx }, .{ .block_ref = 0 } }, // placeholder for end
        });

        // In incref block: increment refcount of the element at value_slot
        // emitManagedArrayIncref will create its own blocks and end up in its incref_end block
        const elem_managed_ptr = try string.getManagedArrayPtr(self, value_slot.raw(), elem_info.managed_buffer_offset);
        try emitManagedArrayIncref(self, elem_managed_ptr, "<array index>");

        // After emitManagedArrayIncref, we're in its incref_end block. We need to branch
        // to our end block, so emit the branch NOW (before adding the end block).
        // The end block will be at the current length after we add it.
        const end_idx: u32 = @intCast(self.func().blocks.items.len);
        try self.func().emitBr(end_idx);

        // Now add the final end block (this becomes the current block)
        _ = try self.func().addBlock("array_get_incref_end");

        // Fix up the conditional branch to point to the correct end block
        self.func().blocks.items[entry_idx].instructions.items[branch_instr_idx].operands[2] = .{ .block_ref = end_idx };
    }

    // Return the error union with proper type info
    return .{
        .value = eu_ptr.raw(),
        .ty = .{ .error_union_type = .{
            .success_type = if (elem_info.is_struct) .ptr else .i64,
            .success_primitive_type = if (elem_info.is_struct) null else if (elem_info.name) |n| types.Primitive.fromString(n) else null,
            .success_struct_type = if (elem_info.is_struct) elem_info.name else null,
            .error_enum_type = "ArrayError",
        } },
    };
}

/// Convert indexing on stdlib Array types (Array$int, etc.) by calling the .get() method
pub fn convertStdlibArrayIndex(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression) ConvertError!TypedValue {
    const type_name = base_typed.ty.struct_type.name;

    // Convert the index expression
    const idx_typed = try self.convertExpression(index_expr);

    // Get the get method: Array$int$get
    const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$get", .{type_name});
    try self.module.trackString(mangled_name);

    const func_info = self.func_map.get(mangled_name) orelse {
        const msg = std.fmt.allocPrint(self.allocator, "stdlib Array type '{s}' missing 'get' method", .{type_name}) catch "stdlib Array missing get method";
        self.reportInternalError(msg);
        return error.SemanticError;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(mangled_name);
    }

    // The get method returns Element throws ArrayError (an error union)
    // Args: sret_ptr, self_ptr, index
    const return_type = func_info.return_value_type orelse {
        self.reportInternalError("get method has no return type");
        return error.SemanticError;
    };

    // Calculate sret buffer size for error union: 8 (tag) + max(success_size, error_size)
    const sret_size: i32 = if (return_type == .error_union_type) blk: {
        const eu_info = return_type.error_union_type;
        const success_size = self.getErrorUnionSuccessSize(eu_info) orelse {
            self.reportInternalError("unknown error union success type size");
            return error.SemanticError;
        };
        const error_size: i32 = 8; // Error enums are always 8 bytes
        break :blk 8 + @max(success_size, error_size);
    } else 16; // Default size if somehow not error union

    const sret_buffer = (try self.func().emitAllocaSized(sret_size)).raw();

    var args = try self.allocator.alloc(ir.Value, 3);
    args[0] = sret_buffer;
    args[1] = base_typed.value;
    args[2] = idx_typed.value;

    _ = try self.func().emitCall(mangled_name, args, .ptr);

    // Return the error union
    return .{
        .value = sret_buffer,
        .ty = return_type,
    };
}

/// Convert index assignment on stdlib Array types (Array$int, etc.) by calling the .set() method
pub fn convertStdlibArrayIndexAssign(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression, value_expr: ast.Expression) ConvertError!void {
    const type_name = base_typed.ty.struct_type.name;

    // Convert index and value expressions
    const idx_typed = try self.convertExpression(index_expr);
    const val_typed = try self.convertExpression(value_expr);

    // Get the set method: Array$int$set
    const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$set", .{type_name});
    try self.module.trackString(mangled_name);

    const func_info = self.func_map.get(mangled_name) orelse {
        const msg = std.fmt.allocPrint(self.allocator, "stdlib Array type '{s}' missing 'set' method", .{type_name}) catch "stdlib Array missing set method";
        self.reportInternalError(msg);
        return error.SemanticError;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(mangled_name);
    }

    // The set method returns void (mutates in place)
    // Args: self_ptr, index, value
    var args = try self.allocator.alloc(ir.Value, 3);
    args[0] = base_typed.value;
    args[1] = idx_typed.value;
    args[2] = val_typed.value;

    _ = try self.func().emitCall(mangled_name, args, func_info.return_type);
}

// ------------------------------------------------------------------------
// Array Expression Conversion
// ------------------------------------------------------------------------

/// Convert InitableFromArrayLiteral with simple type annotation: var arr IntArray = [1, 2, 3]
pub fn convertInitableFromArrayLiteralSimple(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
    try convertInitableFromArrayLiteralImpl(self, decl, type_name);
}

/// Convert InitableFromArrayLiteral: var arr Array of int = [1, 2, 3]
/// Creates a __ManagedArray and calls Type$init(managed)
pub fn convertInitableFromArrayLiteral(self: *AstToIr, decl: ast.VarDecl, gen: ast.GenericTypeExpr) !void {
    try convertInitableFromArrayLiteralImpl(self, decl, gen.base_type);
}

/// Implementation of InitableFromArrayLiteral transformation
/// Array is special-cased: receives __ManagedArray directly.
/// Other types receive an Array (following Swift's ExpressibleByArrayLiteral pattern).
pub fn convertInitableFromArrayLiteralImpl(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
    const arr_lit = decl.value.array_literal;
    const elements = arr_lit.elements;

    debug.astToIr("InitableFromArrayLiteral: {s} with {d} elements", .{ type_name, elements.len });

    const managed_ptr = if (elements.len == 0)
        try emitEmptyManagedArray(self)
    else blk: {
        // Allocate refcounted buffer for elements
        const elem_count = try self.func().emitConstI64(@intCast(elements.len));
        const elem_size: i64 = 8; // All elements are 8 bytes (i64, f64, or pointer)
        const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
        const buffer = try emitAllocRefcountedBuffer(self, buffer_size, "set buffer");

        // Store elements into buffer
        for (elements, 0..) |elem, i| {
            const typed = try self.convertExpression(elem);
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
            try self.func().emitStore(elem_ptr.raw(), typed.value);
        }

        break :blk try emitManagedArray(self, buffer, elem_count, elem_count);
    };

    // Types implementing BuiltinArrayLiteral receive __ManagedArray directly
    // Other types receive an Array (we create one first, then pass it to their init)
    const is_builtin_type = self.isBuiltinLiteralType(type_name, "BuiltinArrayLiteral");

    // For non-Builtin types, first create an Array from the __ManagedArray
    var init_arg: ir.Value = undefined;
    if (is_builtin_type) {
        init_arg = managed_ptr.raw();
    } else {
        // Extract element type from the monomorphized type name (e.g., "Set$int" -> "int")
        const elem_type_name = if (std.mem.indexOf(u8, type_name, "$")) |dollar_pos|
            type_name[dollar_pos + 1 ..]
        else
            "int"; // Default to int for non-generic types

        // Create an Array$ElementType by calling Array$ElementType$init(__ManagedArray)
        var type_args = [_][]const u8{elem_type_name};
        const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);
        const array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
        try self.module.trackString(array_init_name);
        const array_func_info = self.func_map.get(array_init_name) orelse {
            self.reportInternalError("Array init not found for InitableFromArrayLiteral");
            return error.UnknownFunction;
        };

        if (!array_func_info.ir_generated) {
            try self.ensureMethodGenerated(array_init_name);
        }

        // Get Array type info for size
        const array_type_info = self.type_map.get(array_type_name) orelse {
            self.reportError(.E006, array_type_name);
            return error.UnknownType;
        };

        const array_size = array_type_info.struct_type.size;
        const array_ptr = try self.func().emitAllocaSized(array_size);

        var array_args = try self.func().allocator.alloc(ir.Value, 2);
        array_args[0] = array_ptr.raw();
        array_args[1] = managed_ptr.raw();
        _ = try self.func().emitCall(array_init_name, array_args, array_func_info.return_type);

        init_arg = array_ptr.raw();
    }

    // Call Type$init - the static init method
    const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{type_name});
    try self.module.trackString(init_func_name);

    // Look up the function and type info
    const func_info = self.func_map.get(init_func_name) orelse {
        self.reportError(.E003, init_func_name);
        return error.UnknownFunction;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(init_func_name);
    }

    const type_info = self.type_map.get(type_name) orelse {
        self.reportError(.E006, type_name);
        return error.UnknownType;
    };

    // Check if return type is a struct (needs sret)
    const uses_sret = type_info == .struct_type;

    if (uses_sret) {
        // Allocate space for returned struct
        const struct_size = type_info.struct_type.size;
        const result_ptr = try self.func().emitAllocaSized(struct_size);

        // Build args: [sret_ptr, init_arg]
        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr.raw();
        args[1] = init_arg;

        // Call init with sret
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        // Store result in var_map
        try self.func().setValueName(result_ptr.raw(), decl.name);
        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            result_ptr.raw(),
            try self.typeNameToValueType(type_name),
            self.current_decl_is_mutable,
            false,
        ));
    } else {
        // Non-struct return type (unlikely for InitableFromArrayLiteral)
        var args = try self.func().allocator.alloc(ir.Value, 1);
        args[0] = init_arg;
        const result = try self.func().emitCall(init_func_name, args, func_info.return_type);
        const result_ptr = try self.func().emitAlloca(func_info.return_type);
        try self.func().emitStore(result_ptr.raw(), result orelse 0);
        try self.func().setValueName(result_ptr.raw(), decl.name);
        try self.var_map.put(self.allocator, decl.name, VarInfo.init(
            result_ptr.raw(),
            .{ .primitive = types.Primitive.fromIrType(func_info.return_type) },
            self.current_decl_is_mutable,
            false,
        ));
    }
}

pub fn convertArrayLiteral(self: *AstToIr, arr_lit: ast.ArrayLiteralExpr) ConvertError!TypedValue {
    const elements = arr_lit.elements;

    // Find the type that implements BuiltinArrayLiteral interface
    const base_type_name = self.findDefaultLiteralType("BuiltinArrayLiteral") orelse {
        self.reportError(.E006, "no type implements BuiltinArrayLiteral interface for array literals");
        return error.SemanticError;
    };

    if (elements.len == 0) {
        const managed_ptr = try emitEmptyManagedArray(self);

        // Get or create the Array$int type (default element type for empty arrays)
        // Extract name and size immediately as later operations may invalidate the type_map pointer
        var type_args = [_][]const u8{"int"};
        const array_type_name = try self.getOrCreateMonomorphizedType(base_type_name, &type_args);
        const array_type_info = &self.type_map.getPtr(array_type_name).?.struct_type;
        const array_size = array_type_info.size;

        // Call init method directly
        const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
        try self.module.trackString(init_func_name);

        const func_info = self.func_map.get(init_func_name) orelse {
            self.reportInternalError("Array init not found for empty array literal");
            return error.UnknownFunction;
        };

        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }

        // Allocate space for the Array struct
        const result_ptr = try self.func().emitAllocaSized(array_size);

        // Re-fetch type_info after potential type_map modifications
        const final_type_info = &self.type_map.getPtr(array_type_name).?.struct_type;

        // Call init with sret: [sret_ptr, managed_ptr]
        var args = try self.func().allocator.alloc(ir.Value, 2);
        args[0] = result_ptr.raw();
        args[1] = managed_ptr.raw();
        _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

        return .{
            .value = result_ptr.raw(),
            .ty = .{ .struct_type = final_type_info },
        };
    }

    const first_typed = try self.convertExpression(elements[0]);
    const elem_type = first_typed.ty.toIrType();
    const elem_struct_type: ?[]const u8 = switch (first_typed.ty) {
        .struct_type => |struct_info| struct_info.name,
        .primitive, .enum_type, .error_union_type, .function_type => null,
    };
    const elem_primitive_type: ?types.Primitive = switch (first_typed.ty) {
        .primitive => |prim| prim,
        else => null,
    };

    // Calculate element size: structs use their actual size (with monomorphization)
    // Primitives use their array element size (byte=1, others=8)
    const elem_size: i64 = if (elem_struct_type) |struct_name| blk: {
        const size = self.getStructSizeWithMonomorphization(struct_name) orelse {
            self.reportInternalError("unknown struct size in array literal");
            return error.SemanticError;
        };
        break :blk @intCast(size);
    } else blk: {
        // Must have a primitive type
        const prim = elem_primitive_type orelse {
            self.reportInternalError("cannot determine element type in array literal");
            return error.SemanticError;
        };
        break :blk prim.arrayElementSize();
    };

    // Allocate refcounted buffer for elements
    const elem_count = try self.func().emitConstI64(@intCast(elements.len));
    const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
    const buffer = try emitAllocRefcountedBuffer(self, buffer_size, "array buffer");

    // Store elements into buffer
    for (elements, 0..) |elem, i| {
        const typed = if (i == 0) first_typed else try self.convertExpression(elem);
        const idx_val = try self.func().emitConstI64(@intCast(i));
        const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
        if (elem_struct_type) |struct_name| {
            // For structs, copy the struct data
            // Check if this is a temporary with COW semantics that we're moving (not copying)
            // In that case, we just memcpy without incref since ownership transfers
            const has_cow_semantics = if (self.type_map.get(struct_name)) |ti|
                ti == .struct_type and ti.struct_type.has_managed_buffer
            else
                false;
            const is_temporary_cow = has_cow_semantics and cleanup.isInTemporaries(self, typed.value);
            if (is_temporary_cow) {
                // Move semantics: just copy data, don't incref (ownership transfers to array)
                try self.func().emitMemcpy(elem_ptr.asRawPtr(), ir.toRawPtr(typed.value), @intCast(elem_size));
                cleanup.removeFromTemporaries(self, typed.value);
            } else {
                // Copy semantics: copy and incref
                try string.emitStructCopy(self, elem_ptr.asStructPtr(), ir.toStructPtr(typed.value), @intCast(elem_size), struct_name);
            }
        } else {
            // For primitives, store the value directly
            try self.func().emitStore(elem_ptr.raw(), typed.value);
        }
    }

    // Initialize __ManagedArray with buffer, length, and capacity
    const managed_ptr = try emitManagedArray(self, buffer, elem_count, elem_count);

    // Determine element type name for creating the Array type
    const elem_name: []const u8 = if (elem_struct_type) |sn|
        sn
    else if (elem_primitive_type) |pt|
        pt.toMaxonName()
    else
        elem_type.toMaxonName();

    // Get or create the monomorphized type using the discovered base type
    var type_args = [_][]const u8{elem_name};
    const array_type_name = try self.getOrCreateMonomorphizedType(base_type_name, &type_args);
    const array_type_info = &self.type_map.getPtr(array_type_name).?.struct_type;
    const array_size = array_type_info.size;

    // Call init method directly
    const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
    try self.module.trackString(init_func_name);

    const func_info = self.func_map.get(init_func_name) orelse {
        self.reportInternalError("Array init not found for array literal");
        return error.UnknownFunction;
    };

    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(init_func_name);
    }

    // Allocate space for the Array struct
    const result_ptr = try self.func().emitAllocaSized(array_size);

    // Re-fetch type_info after potential type_map modifications
    const final_type_info = &self.type_map.getPtr(array_type_name).?.struct_type;

    // Call init with sret: [sret_ptr, managed_ptr]
    var args = try self.func().allocator.alloc(ir.Value, 2);
    args[0] = result_ptr.raw();
    args[1] = managed_ptr.raw();
    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

    return .{
        .value = result_ptr.raw(),
        .ty = .{ .struct_type = final_type_info },
    };
}

/// Convert InitableFromArrayLiteral expression: TypeName from [...]
/// Creates an instance of the target type by calling its init(Array) method
pub fn convertInitFromArray(self: *AstToIr, ifa: ast.InitFromArrayExpr) ConvertError!TypedValue {
    // The elements expression must be an array literal
    const arr_lit = ifa.elements.array_literal;
    const elements = arr_lit.elements;

    debug.astToIr("InitFromArray: type={s}, {d} elements", .{ ifa.type_name, elements.len });

    // Handle empty collection - use 'int' as default element type
    var elem_type_name: []const u8 = "int";

    // Create __ManagedArray for elements
    var managed_ptr: ir.Value = undefined;

    if (elements.len == 0) {
        managed_ptr = (try emitEmptyManagedArray(self)).raw();
    } else {
        // Infer element type from first element
        const first_typed = try self.convertExpression(elements[0]);
        elem_type_name = first_typed.ty.getTypeName() orelse {
            debug.astToIr("error: element type must be a named type", .{});
            self.reportError(.E006, "element type must be a named type");
            return error.UnknownType;
        };

        // Allocate refcounted buffer for elements
        const elem_count = try self.func().emitConstI64(@intCast(elements.len));
        // Get element size - must be a known primitive type
        const prim_info = types.getPrimitiveTypeInfo(elem_type_name) orelse {
            self.reportInternalError("unknown primitive type in init from array");
            return error.SemanticError;
        };
        const elem_size: i64 = prim_info.array_element_size;
        const buffer_size = try self.func().emitConstI64(@intCast(elements.len * @as(usize, @intCast(elem_size))));
        const buffer = try emitAllocRefcountedBuffer(self, buffer_size, "array buffer");

        // Store elements into buffer
        for (elements, 0..) |elem, i| {
            const typed = if (i == 0) first_typed else try self.convertExpression(elem);
            const idx_val = try self.func().emitConstI64(@intCast(i));
            const elem_ptr = try self.func().emitGetElemPtr(buffer, idx_val, @intCast(elem_size));
            try self.func().emitStore(elem_ptr.raw(), typed.value);
        }

        managed_ptr = (try emitManagedArray(self, buffer, elem_count, elem_count)).raw();
    }

    // Build the monomorphized type name: TypeName$ElementType
    var type_args = [_][]const u8{elem_type_name};
    const target_type_name = try self.getOrCreateMonomorphizedType(ifa.type_name, &type_args);

    debug.astToIr("InitFromArray target type: {s}", .{target_type_name});

    // Create an Array from the __ManagedArray (InitableFromArrayLiteral.init takes Array)
    // First create the monomorphized Array type for the element type
    const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);
    const array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
    try self.module.trackString(array_init_name);
    const array_func_info = self.func_map.get(array_init_name) orelse {
        self.reportInternalError("Array init not found for InitableFromArrayLiteral");
        return error.UnknownFunction;
    };

    if (!array_func_info.ir_generated) {
        try self.ensureMethodGenerated(array_init_name);
    }

    // Get Array type info for size
    const array_type_info = self.type_map.get(array_type_name) orelse {
        self.reportError(.E006, array_type_name);
        return error.UnknownType;
    };

    const array_size = array_type_info.struct_type.size;
    const array_ptr = try self.func().emitAllocaSized(array_size);

    var array_args = try self.allocator.alloc(ir.Value, 2);
    array_args[0] = array_ptr.raw();
    array_args[1] = managed_ptr;
    _ = try self.func().emitCall(array_init_name, array_args, array_func_info.return_type);

    // Build init function name: TypeName$Element$init
    const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{target_type_name});
    try self.module.trackString(init_func_name);

    // Look up function and type info
    const func_info = self.func_map.get(init_func_name) orelse {
        const msg = std.fmt.allocPrint(self.allocator, "Type '{s}' missing init method for InitableFromArrayLiteral", .{target_type_name}) catch "missing init method";
        self.reportInternalError(msg);
        return error.UnknownFunction;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(init_func_name);
    }

    const type_info = self.type_map.get(target_type_name) orelse {
        self.reportError(.E006, target_type_name);
        return error.UnknownType;
    };

    // init returns a struct, so use sret calling convention
    const struct_size = type_info.struct_type.size;
    const result_ptr = try self.func().emitAllocaSized(struct_size);

    // Build args: [sret_ptr, array_ptr]
    var args = try self.allocator.alloc(ir.Value, 2);
    args[0] = result_ptr.raw();
    args[1] = array_ptr.raw();

    // Call init with sret
    _ = try self.func().emitCall(init_func_name, args, func_info.return_type);

    return .{
        .value = result_ptr.raw(),
        .ty = try self.typeNameToValueType(target_type_name),
    };
}

pub fn convertArrayType(self: *AstToIr, arr: ast.ArrayTypeExpr) ConvertError!TypedValue {
    // Convert size expression (capacity)
    const capacity_typed = try self.convertExpression(arr.size.*);

    // Get or create monomorphized Array type for the element type
    // Resolve element type first (e.g., "Element" -> "int" when inside a generic type)
    const resolved_elem = self.resolveTypeName(arr.element_type);
    var type_args = [_][]const u8{resolved_elem};
    const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);

    // Get struct info for the monomorphized Array type
    const type_info = self.type_map.get(array_type_name) orelse {
        debug.astToIr("error: Array type not found after monomorphization: {s}", .{array_type_name});
        return error.UnknownType;
    };

    const struct_info = switch (type_info) {
        .struct_type => |s| s,
        .primitive, .enum_type => {
            debug.astToIr("error: Array type is not a struct: {s}", .{array_type_name});
            return error.UnknownType;
        },
    };

    // Build __ManagedArray with the given capacity
    // For Array of N int, len = capacity so all elements are immediately accessible
    // Calculate actual element size from type (with monomorphization fallback)
    const elem_size_val: i64 = if (self.getStructSizeWithMonomorphization(resolved_elem)) |size|
        size
    else if (types.getPrimitiveTypeInfo(resolved_elem)) |prim_info|
        prim_info.array_element_size
    else if (self.type_map.get(resolved_elem)) |elem_type_info| blk: {
        if (elem_type_info == .enum_type) {
            break :blk elem_type_info.enum_type.arrayElementSize();
        }
        self.reportInternalError("unknown element type in Array type expression");
        return error.SemanticError;
    } else {
        self.reportInternalError("unknown element type in Array type expression");
        return error.SemanticError;
    };
    const elem_size = try self.func().emitConstI64(elem_size_val);
    const buffer_size = try self.func().emitBinaryOp(.mul, capacity_typed.value, elem_size, .i64);
    // Allocate refcounted buffer and zero-initialize
    const buffer = try emitAllocRefcountedBuffer(self, buffer_size, "array buffer");
    // Zero-initialize the buffer so hash tables (Map, Set) have correct initial state
    try self.func().emitMemsetDynamic(buffer, 0, buffer_size);
    const managed_ptr = try emitManagedArray(self, buffer, capacity_typed.value, capacity_typed.value);

    // Call Array$init(result_ptr, managed_ptr) via InitableFromArrayLiteral interface
    const init_func_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
    try self.module.trackString(init_func_name);

    // Trigger lazy generation if needed
    if (self.func_map.get(init_func_name)) |func_info| {
        if (!func_info.ir_generated) {
            try self.ensureMethodGenerated(init_func_name);
        }
    }

    // Allocate space for the Array struct (sret)
    const result_ptr = try self.func().emitAllocaSized(@intCast(struct_info.size));

    var args = try self.allocator.alloc(ir.Value, 2);
    args[0] = result_ptr.raw();
    args[1] = managed_ptr.raw();

    _ = try self.func().emitCall(init_func_name, args, .ptr);

    return .{
        .value = result_ptr.raw(),
        .ty = try self.typeNameToValueType(array_type_name),
    };
}
