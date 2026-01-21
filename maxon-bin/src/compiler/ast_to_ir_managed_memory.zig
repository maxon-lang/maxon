/// ManagedMemory and Array IR emission helpers.
/// Contains layout constants, COW (copy-on-write) refcounting operations,
/// string/array creation, and struct copy/move helpers.
/// ManagedMemory is the unified managed type for both strings and arrays.
const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const debug = @import("debug.zig");
const struct_helpers = @import("ir_struct_helpers.zig");
const types = @import("ast_to_ir_types.zig");
const ast_to_ir = @import("4-ast_to_ir.zig");
const cleanup = @import("ast_to_ir_cleanup.zig");

const AstToIr = ast_to_ir.AstToIr;

// ============================================================================
// Mode Constants for ManagedMemory
// ============================================================================
/// Mode values for __ManagedMemory flags (bits 0-1):
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
// ManagedMemory Layout (32 bytes)
// ============================================================================
/// __ManagedMemory struct layout (32 bytes total)
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
pub const ManagedMemory = struct {
    const SIZE: i32 = 32;
    const BUFFER_OFFSET: i32 = 0;
    const LEN_OFFSET: i32 = 8;
    const CAPACITY_OFFSET: i32 = 16;
    const FLAGS_OFFSET: i32 = 24;
    const PARENT_OFF_OFFSET: i32 = 28;

    comptime {
        if (PARENT_OFF_OFFSET + 4 != SIZE) {
            @compileError("ManagedMemory layout mismatch: PARENT_OFF_OFFSET + 4 != SIZE");
        }
        if (LEN_OFFSET != BUFFER_OFFSET + 8) {
            @compileError("ManagedMemory layout mismatch: LEN_OFFSET != BUFFER_OFFSET + 8");
        }
        if (CAPACITY_OFFSET != LEN_OFFSET + 8) {
            @compileError("ManagedMemory layout mismatch: CAPACITY_OFFSET != LEN_OFFSET + 8");
        }
        if (FLAGS_OFFSET != CAPACITY_OFFSET + 8) {
            @compileError("ManagedMemory layout mismatch: FLAGS_OFFSET != CAPACITY_OFFSET + 8");
        }
        if (PARENT_OFF_OFFSET != FLAGS_OFFSET + 4) {
            @compileError("ManagedMemory layout mismatch: PARENT_OFF_OFFSET != FLAGS_OFFSET + 4");
        }
    }

    // ========================================================================
    // Helper functions for ManagedMemory layout
    // ========================================================================

    /// Returns the size of ManagedMemory struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Allocate a ManagedMemory on the stack
    pub fn alloca(func: *ir.Function) !ir.ManagedMemoryPtr {
        const raw_ptr = try func.emitAllocaSized(SIZE);
        return raw_ptr.asManagedMemoryPtr();
    }

    /// Load the buffer pointer (offset 0)
    pub fn loadBuffer(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        return func.emitLoad(ptr.raw(), .ptr);
    }

    /// Load the length field (offset 8)
    pub fn loadLen(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const len_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), LEN_OFFSET);
        return func.emitLoad(len_ptr.raw(), .i64);
    }

    /// Get pointer to the length field
    pub fn getLenPtr(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), LEN_OFFSET);
    }

    /// Load the capacity field (offset 16)
    pub fn loadCapacity(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const cap_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), CAPACITY_OFFSET);
        return func.emitLoad(cap_ptr.raw(), .i64);
    }

    /// Get pointer to the capacity field
    pub fn getCapacityPtr(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), CAPACITY_OFFSET);
    }

    /// Load the flags field (offset 24) as i32
    pub fn loadFlags(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const flags_ptr = try func.emitGetFieldPtr(ptr.asStructPtr(), FLAGS_OFFSET);
        return func.emitLoad(flags_ptr.raw(), .i32);
    }

    /// Get pointer to the flags field
    pub fn getFlagsPtr(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.StructPtr {
        return func.emitGetFieldPtr(ptr.asStructPtr(), FLAGS_OFFSET);
    }

    /// Check if the ManagedMemory is in heap mode (flags & 3 == 1)
    pub fn isHeapMode(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const flags = try loadFlags(func, ptr);
        const three = try func.emitConstI32(3);
        const masked = try func.emitBinaryOp(.band, flags, three, .i32);
        const one = try func.emitConstI32(1);
        return func.emitBinaryOp(.icmp_eq, masked, one, .i32);
    }

    /// Check if the ManagedMemory is in slice mode (flags & 3 == 2)
    pub fn isSliceMode(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const flags = try loadFlags(func, ptr);
        const three = try func.emitConstI32(3);
        const masked = try func.emitBinaryOp(.band, flags, three, .i32);
        const two = try func.emitConstI32(2);
        return func.emitBinaryOp(.icmp_eq, masked, two, .i32);
    }

    /// Load the mode value (flags & 3)
    pub fn loadMode(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !ir.Value {
        const flags = try loadFlags(func, ptr);
        const three = try func.emitConstI32(3);
        return func.emitBinaryOp(.band, flags, three, .i32);
    }

    /// Initialize a ManagedMemory with all fields
    pub fn init(func: *ir.Function, ptr: ir.ManagedMemoryPtr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value, flags: ir.Value) !void {
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

    /// Initialize a ManagedMemory as empty (null buffer, zero len/capacity/flags/parent_off)
    pub fn initEmpty(func: *ir.Function, ptr: ir.ManagedMemoryPtr) !void {
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
// ManagedMemory Helpers
// ============================================================================

/// Allocate a __ManagedMemory on the stack and initialize it as empty.
pub fn emitEmptyManagedMemory(self: *AstToIr) !ir.ManagedMemoryPtr {
    const ptr = try ManagedMemory.alloca(self.func());
    try ManagedMemory.initEmpty(self.func(), ptr);
    return ptr;
}

/// Initialize an existing __ManagedMemory as empty.
pub fn initManagedMemoryEmpty(self: *AstToIr, managed_ptr: ir.ManagedMemoryPtr) !void {
    return ManagedMemory.initEmpty(self.func(), managed_ptr);
}

/// Initialize an existing __ManagedMemory with buffer, length, and capacity values.
/// Uses mode=0 (no refcounting) for regular arrays.
pub fn initManagedMemory(self: *AstToIr, managed_ptr: ir.ManagedMemoryPtr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !void {
    const zero_flags = try self.func().emitConstI32(0); // mode=0 (no refcounting for regular arrays)
    return ManagedMemory.init(self.func(), managed_ptr, buffer, len, capacity, zero_flags);
}

/// Allocate a __ManagedMemory on the stack and initialize with buffer, length, and capacity.
/// Uses mode=1 (heap) for regular arrays that own their buffer.
pub fn emitManagedMemory(self: *AstToIr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !ir.ManagedMemoryPtr {
    const ptr = try ManagedMemory.alloca(self.func());
    const heap_flags = try self.func().emitConstI32(MODE_HEAP); // mode=1 (heap, owns buffer)
    try ManagedMemory.init(self.func(), ptr, buffer, len, capacity, heap_flags);
    return ptr;
}

/// Allocate a refcounted buffer and create a __ManagedMemory pointing to it.
/// The buffer includes an 8-byte refcount header at (buffer - 8).
/// Use this for arrays that need COW semantics with shared ownership.
pub fn emitRefcountedManagedMemory(self: *AstToIr, data_size: ir.Value, len: ir.Value, capacity: ir.Value, tag: []const u8) !ir.ManagedMemoryPtr {
    const buffer = try emitAllocRefcountedBuffer(self, data_size, tag);
    return emitManagedMemory(self, buffer, len, capacity);
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
/// The refcount is initialized to 1 (for the ManagedMemory being created).
pub fn emitAllocRefcountedBuffer(self: *AstToIr, data_size: ir.Value, tag: []const u8) !ir.RawPtr {
    const func = self.func();
    // Allocate data_size + 8 for header
    const header_size = try func.emitConstI64(REFCOUNTED_BUFFER_HEADER_SIZE);
    const total_size = try func.emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try func.emitHeapAlloc(total_size, tag);

    // Initialize refcount in header to 1 (for the ManagedMemory being created)
    const one = try func.emitConstI64(1);
    try func.emitStore(buffer_with_header.raw(), one);

    // Track the initial refcount as an incref
    if (self.track_memory) {
        const one_i32 = try func.emitConstI32(1);
        try func.emitTrackIncref(tag, one_i32);
    }

    // Return data pointer (header + 8)
    const eight = try func.emitConstI64(8);
    const data_ptr = try func.emitBinaryOp(.add, buffer_with_header.raw(), eight, .ptr);
    return ir.toRawPtr(data_ptr);
}

/// Emit incref for a ManagedMemory.
/// The ptr should point to a __ManagedMemory or a struct with __ManagedMemory at offset 0.
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// This is shared by all ManagedMemory copies that reference the same buffer.
pub fn emitManagedMemoryIncref(self: *AstToIr, ptr: ir.ManagedMemoryPtr, tag: []const u8) !void {
    // Get heap mode flag in entry block
    const is_heap = try ManagedMemory.isHeapMode(self.func(), ptr);

    // Create all blocks upfront
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const heap_incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("heap_incref");
    const slice_check_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_check");
    const slice_incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_incref");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("incref_end");

    // Defer blocks: end(0), slice_incref(1), slice_check(2), leaving heap_incref as current
    var deferred = try ast_to_ir.DeferredBlocks.init(self.allocator, 3);
    defer deferred.deinit();
    deferred.deferBlocks(self, 3);

    // Entry block: if heap mode -> heap_incref, else -> slice_check
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = heap_incref_block_idx }, .{ .block_ref = slice_check_block_idx } },
    });

    // ===== HEAP INCREF BLOCK (current) =====
    // Increment refcount in buffer header (at buffer_ptr - 8)
    const buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackIncref(tag, new_ref_i32);
    }

    try self.func().emitBr(end_block_idx);

    // ===== SLICE CHECK BLOCK =====
    try deferred.restore(self, 2);

    const is_slice = try ManagedMemory.isSliceMode(self.func(), ptr);
    try self.func().emitBrCond(is_slice, slice_incref_block_idx, end_block_idx);

    // ===== SLICE INCREF BLOCK =====
    try deferred.restore(self, 1);

    // For slices, incref the PARENT buffer
    // Parent buffer = slice_buf - parent_off
    const slice_buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const parent_off_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(ptr.raw()), 28);
    const parent_off_i32 = try self.func().emitLoad(parent_off_ptr.raw(), .i32);
    const parent_off_i64 = try self.func().emitUnaryOp(.sext_i32_i64, parent_off_i32, .i64);
    const parent_buf_ptr = try self.func().emitBinaryOp(.sub, slice_buf_ptr, parent_off_i64, .ptr);

    // Incref parent header at parent_buf - 8
    const eight2 = try self.func().emitConstI64(8);
    const parent_header_ptr = try self.func().emitBinaryOp(.sub, parent_buf_ptr, eight2, .ptr);
    const parent_old_ref = try self.func().emitLoad(parent_header_ptr, .i64);
    const one2 = try self.func().emitConstI64(1);
    const parent_new_ref = try self.func().emitBinaryOp(.add, parent_old_ref, one2, .i64);
    try self.func().emitStore(parent_header_ptr, parent_new_ref);

    if (self.track_memory) {
        const parent_new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, parent_new_ref, .i32);
        try self.func().emitTrackIncref("slice parent", parent_new_ref_i32);
    }

    try self.func().emitBr(end_block_idx);

    // ===== END BLOCK =====
    try deferred.restore(self, 0);
}

/// Emit decref for a ManagedMemory with conditional free when refcount reaches 0.
/// The ptr should point to a __ManagedMemory or a struct with __ManagedMemory at offset 0.
/// tag is used for memory tracking (variable name or description).
///
/// Refcount is stored in the buffer header at (buffer_ptr - 8).
/// When refcount reaches 0, we free (buffer_ptr - 8) to include the header.
/// Decref a managed memory buffer (COW semantics).
/// If refcount reaches 0, frees the buffer.
/// `tag` is used for memory tracking (variable name).
/// `cleanup_tag` is used for heap free description (e.g., "string cleanup", "array cleanup").
pub fn emitManagedMemoryDecref(self: *AstToIr, ptr: ir.ManagedMemoryPtr, tag: []const u8, cleanup_tag: []const u8) !void {
    // Get heap mode flag before creating any blocks (computed in entry block)
    const is_heap = try ManagedMemory.isHeapMode(self.func(), ptr);

    // Create all blocks upfront
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const heap_decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("heap_decref");
    const heap_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("heap_free");
    const slice_decref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_decref");
    const slice_free_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_free");
    const slice_check_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_check");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("decref_end");

    // Defer all blocks except heap_decref (which becomes current)
    // Order after deferBlocks(5): stored[0]=end, stored[1]=slice_check, stored[2]=slice_free, stored[3]=slice_decref, stored[4]=heap_free
    var deferred = try ast_to_ir.DeferredBlocks.init(self.allocator, 5);
    defer deferred.deinit();
    deferred.deferBlocks(self, 5);

    // Entry block: if heap mode -> heap_decref, else -> slice_check
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = heap_decref_block_idx }, .{ .block_ref = slice_check_block_idx } },
    });

    // ===== HEAP DECREF BLOCK (current) =====
    const buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackDecref(tag, new_ref_i32);
    }

    const zero = try self.func().emitConstI64(0);
    const is_zero = try self.func().emitBinaryOp(.icmp_eq, new_ref, zero, .i64);
    try self.func().emitBrCond(is_zero, heap_free_block_idx, end_block_idx);

    // ===== HEAP FREE BLOCK =====
    try deferred.restore(self, 4);

    const buf_ptr2 = try self.func().emitLoad(ptr.raw(), .ptr);
    const eight2 = try self.func().emitConstI64(8);
    const header_ptr2 = try self.func().emitBinaryOp(.sub, buf_ptr2, eight2, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(header_ptr2), cleanup_tag);
    try self.func().emitBr(end_block_idx);

    // ===== SLICE DECREF BLOCK =====
    try deferred.restore(self, 3);

    // Compute parent buffer: slice_buf - parent_off
    const slice_buf_ptr = try self.func().emitLoad(ptr.raw(), .ptr);
    const parent_off_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(ptr.raw()), 28);
    const parent_off_i32 = try self.func().emitLoad(parent_off_ptr.raw(), .i32);
    const parent_off_i64 = try self.func().emitUnaryOp(.sext_i32_i64, parent_off_i32, .i64);
    const parent_buf_ptr = try self.func().emitBinaryOp(.sub, slice_buf_ptr, parent_off_i64, .ptr);

    // Decref parent header at parent_buf - 8
    const eight3 = try self.func().emitConstI64(8);
    const parent_header_ptr = try self.func().emitBinaryOp(.sub, parent_buf_ptr, eight3, .ptr);
    const parent_old_ref = try self.func().emitLoad(parent_header_ptr, .i64);
    const one2 = try self.func().emitConstI64(1);
    const parent_new_ref = try self.func().emitBinaryOp(.sub, parent_old_ref, one2, .i64);
    try self.func().emitStore(parent_header_ptr, parent_new_ref);

    if (self.track_memory) {
        const parent_new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, parent_new_ref, .i32);
        try self.func().emitTrackDecref("slice parent", parent_new_ref_i32);
    }

    const zero2 = try self.func().emitConstI64(0);
    const parent_is_zero = try self.func().emitBinaryOp(.icmp_eq, parent_new_ref, zero2, .i64);
    try self.func().emitBrCond(parent_is_zero, slice_free_block_idx, end_block_idx);

    // ===== SLICE FREE BLOCK =====
    try deferred.restore(self, 2);

    // Free parent buffer at header - need to reload values
    const slice_buf_ptr2 = try self.func().emitLoad(ptr.raw(), .ptr);
    const parent_off_ptr2 = try self.func().emitGetFieldPtr(ir.toStructPtr(ptr.raw()), 28);
    const parent_off_i32_2 = try self.func().emitLoad(parent_off_ptr2.raw(), .i32);
    const parent_off_i64_2 = try self.func().emitUnaryOp(.sext_i32_i64, parent_off_i32_2, .i64);
    const parent_buf_ptr2 = try self.func().emitBinaryOp(.sub, slice_buf_ptr2, parent_off_i64_2, .ptr);
    const eight4 = try self.func().emitConstI64(8);
    const parent_header_ptr2 = try self.func().emitBinaryOp(.sub, parent_buf_ptr2, eight4, .ptr);
    try self.func().emitHeapFree(ir.toRawPtr(parent_header_ptr2), "slice parent cleanup");
    try self.func().emitBr(end_block_idx);

    // ===== SLICE CHECK BLOCK =====
    try deferred.restore(self, 1);

    // Compute is_slice HERE in slice_check block (not in entry block)
    const is_slice = try ManagedMemory.isSliceMode(self.func(), ptr);
    // Branch: if slice mode -> slice_decref, else -> end
    try self.func().emitBrCond(is_slice, slice_decref_block_idx, end_block_idx);

    // ===== END BLOCK =====
    try deferred.restore(self, 0);
}

// ============================================================================
// String Helpers
// ============================================================================

/// Create and initialize a __ManagedMemory on the stack from string bytes.
/// Returns a pointer to the allocated struct.
///
/// String buffer layout with header:
///   [refcount: i64] [data bytes...] [null terminator]
///   ^               ^
///   |               +-- data_ptr (stored in struct)
///   +-- allocation base (header)
///
/// The refcount is stored at (data_ptr - 8), shared by all String copies.
pub fn emitManagedMemoryFromBytes(self: *AstToIr, str_bytes: []const u8) !ir.ManagedMemoryPtr {
    const managed_ptr = try ManagedMemory.alloca(self.func());

    if (str_bytes.len == 0) {
        try ManagedMemory.initEmpty(self.func(), managed_ptr);
    } else {
        // Heap allocation for string data WITH 8-byte header for refcount
        // Layout: [refcount:i64][data...][null]
        const buffer_size = try self.func().emitConstI64(@intCast(str_bytes.len + 1 + 8));
        const buffer_with_header = try self.func().emitHeapAlloc(buffer_size, "string buffer");

        // Initialize refcount in header to 1 (for the String being created)
        const one_refcount = try self.func().emitConstI64(1);
        if (debug.enabled) std.debug.print("[DEBUG] Emitting store 1 to header at {d}\n", .{buffer_with_header.raw()});
        try self.func().emitStore(buffer_with_header.raw(), one_refcount);

        // Track the initial refcount as an incref
        if (self.track_memory) {
            const one_i32 = try self.func().emitConstI32(1);
            try self.func().emitTrackIncref("string buffer", one_i32);
        }

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

        // Initialize __ManagedMemory fields (mode=1 for heap-refcounted)
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

/// Create and initialize a __ManagedMemory from a runtime buffer pointer and length.
/// Used for runtime string conversions (int to string, float to string, etc.)
pub fn emitManagedMemoryFromBuffer(self: *AstToIr, buffer: ir.RawPtr, len_i64: ir.Value) !ir.ManagedMemoryPtr {
    const ptr = try ManagedMemory.alloca(self.func());
    const mode_one = try self.func().emitConstI32(1); // mode=1 for heap-refcounted
    try ManagedMemory.init(self.func(), ptr, buffer, len_i64, len_i64, mode_one);
    return ptr;
}

/// Create a __ManagedMemory pointing to static data in the .rdata section.
/// No heap allocation, no refcount. Mode=3 ensures it's never freed.
/// The string data is stored directly in the executable's code section.
pub fn emitManagedMemoryFromStaticBytes(self: *AstToIr, str_bytes: []const u8) !ir.ManagedMemoryPtr {
    const managed_ptr = try ManagedMemory.alloca(self.func());

    if (str_bytes.len == 0) {
        try ManagedMemory.initEmpty(self.func(), managed_ptr);
    } else {
        // Allocate space for the string data plus null terminator in the static data section
        const static_bytes = try self.allocator.alloc(u8, str_bytes.len + 1);
        @memcpy(static_bytes[0..str_bytes.len], str_bytes);
        static_bytes[str_bytes.len] = 0; // null terminator
        try self.module.trackString(static_bytes);

        // Get pointer to static string in .rdata section
        const static_ptr = try self.func().emitStringConstant(static_bytes, try self.sourceLabel());

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

/// Allocate a type's struct on the stack by looking up its size from the type_map.
/// Returns an uninitialized pointer that can be passed to functions.
pub fn emitTypeAlloca(self: *AstToIr, type_name: []const u8) !ir.RawPtr {
    const type_info = self.type_map.get(type_name) orelse {
        self.reportInternalError(type_name, @src());
        return error.UnknownType;
    };
    return self.func().emitAllocaSized(type_info.struct_type.size);
}

// ============================================================================
// Struct Helpers
// ============================================================================

/// Copy a struct and handle refcount increment for types with __ManagedMemory.
/// This is the single point of truth for struct copying with COW semantics.
/// Handles String, Array$T, cstring, and any other type with __ManagedMemory at any offset.
/// Also handles nested structs that contain managed buffers.
pub fn emitStructCopy(self: *AstToIr, dest_ptr: ir.StructPtr, src_ptr: ir.StructPtr, size: i32, struct_name: ?[]const u8) !void {
    try self.func().emitMemcpy(dest_ptr.asRawPtr(), src_ptr.asRawPtr(), size);

    // Handle refcount for types with __ManagedMemory
    if (struct_name) |name| {
        // Skip internal __ManagedMemory type (it's moved, not copied)
        if (std.mem.eql(u8, name, "__ManagedMemory")) return;

        // Look up struct info to check for __ManagedMemory field
        if (self.type_map.get(name)) |type_info| {
            if (type_info == .struct_type) {
                const struct_info = &type_info.struct_type;
                // Check for multiple managed fields (nested structs with managed buffers)
                if (struct_info.managed_field_offsets) |offsets| {
                    // Incref all managed buffers
                    for (offsets) |offset| {
                        const managed_ptr = try getManagedMemoryPtr(self, dest_ptr.raw(), offset);
                        try emitManagedMemoryIncref(self, managed_ptr, "<struct copy nested>");
                    }
                } else if (struct_info.has_managed_buffer) {
                    // Single managed buffer at managed_buffer_offset
                    const managed_ptr = try getManagedMemoryPtr(self, dest_ptr.raw(), struct_info.managed_buffer_offset);
                    try emitManagedMemoryIncref(self, managed_ptr, "<struct copy>");
                } else if (struct_info.is_cstring) {
                    // cstring has a managed pointer (offset 16) that may reference a __ManagedMemory.
                    // If managed != null and the buffer is heap mode, we need to incref.
                    try emitCstringCopyIncref(self, dest_ptr);
                }
            }
        }
    }
}

/// Get pointer to __ManagedMemory at the given offset within a struct.
pub fn getManagedMemoryPtr(self: *AstToIr, struct_ptr: ir.Value, offset: i32) !ir.ManagedMemoryPtr {
    if (offset == 0) {
        return ir.toManagedMemoryPtr(struct_ptr);
    } else {
        const field_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(struct_ptr), offset);
        return ir.toManagedMemoryPtr(field_ptr.raw());
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
/// If managed != null and points to a heap-mode __ManagedMemory, incref its buffer.
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

    // Check if the managed memory is in heap mode (flags & 3 == 1)
    const flags_ptr = try ManagedMemory.getFlagsPtr(self.func(), ir.toManagedMemoryPtr(managed_ptr));
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

// ============================================================================
// Array Indexing
// ============================================================================

/// Index into a __ManagedMemory: managed[i]
/// Returns the element as an error union (bounds-checked, throws ArrayError on out of bounds)
/// Uses current_type_name or var_name context to determine element type
pub fn convertManagedMemoryIndex(self: *AstToIr, managed_ptr: ir.Value, index_expr: ast.Expression, var_name: ?[]const u8) ConvertError!TypedValue {
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
        self.reportInternalError("unknown element type in array index", @src());
        return error.SemanticError;
    } else {
        self.reportInternalError("cannot determine array element type - missing type context", @src());
        return error.SemanticError;
    };

    // Load buffer pointer (offset 0) and length (offset 8)
    const buf_ptr = try self.func().emitLoad(managed_ptr, .ptr);
    const len_ptr = try ManagedMemory.getLenPtr(self.func(), ir.toManagedMemoryPtr(managed_ptr));
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
        // since emitManagedMemoryIncref will create its own blocks
        const entry_idx: u32 = @intCast(self.func().blocks.items.len - 1);
        const incref_idx: u32 = @intCast(self.func().blocks.items.len);
        _ = try self.func().addBlock("array_get_incref");

        // We'll add the end block AFTER emitManagedMemoryIncref to avoid index confusion
        // For now, emit conditional branch with placeholder (we'll fix the end index)
        const branch_instr_idx = self.func().blocks.items[entry_idx].instructions.items.len;
        try self.func().blocks.items[entry_idx].instructions.append(self.allocator, .{
            .op = .br_cond,
            .operands = .{ .{ .value = is_success }, .{ .block_ref = incref_idx }, .{ .block_ref = 0 } }, // placeholder for end
        });

        // In incref block: increment refcount of the element at value_slot
        // emitManagedMemoryIncref will create its own blocks and end up in its incref_end block
        const elem_managed_ptr = try getManagedMemoryPtr(self, value_slot.raw(), elem_info.managed_buffer_offset);
        try emitManagedMemoryIncref(self, elem_managed_ptr, "<array index>");

        // After emitManagedMemoryIncref, we're in its incref_end block. We need to branch
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

/// Convert indexing on types implementing Indexed interface by calling the .get() method
pub fn convertIndexedGet(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression) ConvertError!TypedValue {
    const type_name = base_typed.ty.struct_type.name;

    // Convert the index expression
    const idx_typed = try self.convertExpression(index_expr);

    // Get the get method: Array$int$get
    const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$get", .{type_name});
    try self.module.trackString(mangled_name);

    const func_info = self.func_map.get(mangled_name) orelse {
        const msg = std.fmt.allocPrint(self.allocator, "Indexed type '{s}' missing 'get' method", .{type_name}) catch "Indexed type missing get method";
        self.reportInternalError(msg, @src());
        return error.SemanticError;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(mangled_name);
    }

    // The get method returns Element throws ArrayError (an error union)
    // Args: sret_ptr, self_ptr, index
    const return_type = func_info.return_value_type orelse {
        self.reportInternalError("get method has no return type", @src());
        return error.SemanticError;
    };

    // Calculate sret buffer size for error union: 8 (tag) + max(success_size, error_size)
    const sret_size: i32 = if (return_type == .error_union_type) blk: {
        const eu_info = return_type.error_union_type;
        const success_size = self.getErrorUnionSuccessSize(eu_info) orelse {
            self.reportInternalError("unknown error union success type size", @src());
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

/// Convert index assignment on types implementing Indexed interface by calling the .set() method
pub fn convertIndexedSet(self: *AstToIr, base_typed: TypedValue, index_expr: ast.Expression, value_expr: ast.Expression) ConvertError!void {
    const type_name = base_typed.ty.struct_type.name;

    // Convert index and value expressions
    const idx_typed = try self.convertExpression(index_expr);
    const val_typed = try self.convertExpression(value_expr);

    // Get the set method: Array$int$set
    const mangled_name = try std.fmt.allocPrint(self.allocator, "{s}$set", .{type_name});
    try self.module.trackString(mangled_name);

    const func_info = self.func_map.get(mangled_name) orelse {
        const msg = std.fmt.allocPrint(self.allocator, "Indexed type '{s}' missing 'set' method", .{type_name}) catch "Indexed type missing set method";
        self.reportInternalError(msg, @src());
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

/// Convert InitableFromArrayLiteral
/// Creates a __ManagedMemory and calls Type$init(managed)
pub fn convertInitableFromArrayLiteral(self: *AstToIr, decl: ast.VarDecl, gen: ast.GenericTypeExpr) !void {
    try convertInitableFromArrayLiteralImpl(self, decl, gen.base_type);
}

/// Implementation of InitableFromArrayLiteral transformation
/// Array is special-cased: receives __ManagedMemory directly.
/// Other types receive an Array (following Swift's ExpressibleByArrayLiteral pattern).
pub fn convertInitableFromArrayLiteralImpl(self: *AstToIr, decl: ast.VarDecl, type_name: []const u8) !void {
    const arr_lit = decl.value.array_literal;
    const elements = arr_lit.elements;

    debug.astToIr("InitableFromArrayLiteral: {s} with {d} elements", .{ type_name, elements.len });

    const managed_ptr = if (elements.len == 0)
        try emitEmptyManagedMemory(self)
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

        break :blk try emitManagedMemory(self, buffer, elem_count, elem_count);
    };

    // Types implementing BuiltinArrayLiteral receive __ManagedMemory directly
    // Other types receive an Array (we create one first, then pass it to their init)
    const is_builtin_type = self.isBuiltinLiteralType(type_name, "BuiltinArrayLiteral");

    // For non-Builtin types, first create an Array from the __ManagedMemory
    var init_arg: ir.Value = undefined;
    if (is_builtin_type) {
        init_arg = managed_ptr.raw();
    } else {
        // Extract element type from the monomorphized type name (e.g., "Set$int" -> "int")
        const elem_type_name = if (std.mem.indexOf(u8, type_name, "$")) |dollar_pos|
            type_name[dollar_pos + 1 ..]
        else
            "int"; // Default to int for non-generic types

        // Create an Array$ElementType by calling Array$ElementType$init(__ManagedMemory)
        var type_args = [_][]const u8{elem_type_name};
        const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);
        const array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
        try self.module.trackString(array_init_name);
        const array_func_info = self.func_map.get(array_init_name) orelse {
            self.reportInternalError("Array init not found for InitableFromArrayLiteral", @src());
            return error.UnknownFunction;
        };

        if (!array_func_info.ir_generated) {
            try self.ensureMethodGenerated(array_init_name);
        }

        // Get Array type info for size
        const array_type_info = self.type_map.get(array_type_name) orelse {
            self.reportError(.E006, array_type_name, @src());
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
        self.reportError(.E003, init_func_name, @src());
        return error.UnknownFunction;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(init_func_name);
    }

    const type_info = self.type_map.get(type_name) orelse {
        self.reportError(.E006, type_name, @src());
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
        self.reportError(.E006, "no type implements BuiltinArrayLiteral interface for array literals", @src());
        return error.SemanticError;
    };

    if (elements.len == 0) {
        const managed_ptr = try emitEmptyManagedMemory(self);

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
            self.reportInternalError("Array init not found for empty array literal", @src());
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
            self.reportInternalError("unknown struct size in array literal", @src());
            return error.SemanticError;
        };
        break :blk @intCast(size);
    } else blk: {
        // Must have a primitive type
        const prim = elem_primitive_type orelse {
            self.reportInternalError("cannot determine element type in array literal", @src());
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
                try emitStructCopy(self, elem_ptr.asStructPtr(), ir.toStructPtr(typed.value), @intCast(elem_size), struct_name);
            }
        } else {
            // For primitives, store the value directly
            try self.func().emitStore(elem_ptr.raw(), typed.value);
        }
    }

    // Initialize __ManagedMemory with buffer, length, and capacity
    const managed_ptr = try emitManagedMemory(self, buffer, elem_count, elem_count);

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
        self.reportInternalError("Array init not found for array literal", @src());
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

    // Create __ManagedMemory for elements
    var managed_ptr: ir.Value = undefined;

    if (elements.len == 0) {
        managed_ptr = (try emitEmptyManagedMemory(self)).raw();
    } else {
        // Infer element type from first element
        const first_typed = try self.convertExpression(elements[0]);
        elem_type_name = first_typed.ty.getTypeName() orelse {
            debug.astToIr("error: element type must be a named type", .{});
            self.reportError(.E006, "element type must be a named type", @src());
            return error.UnknownType;
        };

        // Allocate refcounted buffer for elements
        const elem_count = try self.func().emitConstI64(@intCast(elements.len));
        // Get element size - must be a known primitive type
        const prim_info = types.getPrimitiveTypeInfo(elem_type_name) orelse {
            self.reportInternalError("unknown primitive type in init from array", @src());
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

        managed_ptr = (try emitManagedMemory(self, buffer, elem_count, elem_count)).raw();
    }

    // Build the monomorphized type name: TypeName$ElementType
    var type_args = [_][]const u8{elem_type_name};
    const target_type_name = try self.getOrCreateMonomorphizedType(ifa.type_name, &type_args);

    debug.astToIr("InitFromArray target type: {s}", .{target_type_name});

    // Create an Array from the __ManagedMemory (InitableFromArrayLiteral.init takes Array)
    // First create the monomorphized Array type for the element type
    const array_type_name = try self.getOrCreateMonomorphizedType("Array", &type_args);
    const array_init_name = try std.fmt.allocPrint(self.allocator, "{s}$init", .{array_type_name});
    try self.module.trackString(array_init_name);
    const array_func_info = self.func_map.get(array_init_name) orelse {
        self.reportInternalError("Array init not found for InitableFromArrayLiteral", @src());
        return error.UnknownFunction;
    };

    if (!array_func_info.ir_generated) {
        try self.ensureMethodGenerated(array_init_name);
    }

    // Get Array type info for size
    const array_type_info = self.type_map.get(array_type_name) orelse {
        self.reportError(.E006, array_type_name, @src());
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
        self.reportInternalError(msg, @src());
        return error.UnknownFunction;
    };

    // Trigger lazy generation if needed
    if (!func_info.ir_generated) {
        try self.ensureMethodGenerated(init_func_name);
    }

    const type_info = self.type_map.get(target_type_name) orelse {
        self.reportError(.E006, target_type_name, @src());
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
