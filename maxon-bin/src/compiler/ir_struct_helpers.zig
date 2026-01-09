const std = @import("std");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const layouts = @import("builtin_struct_layouts.zig");

const Allocator = std.mem.Allocator;
const Function = ir.Function;
const Value = ir.Value;

// ============================================================================
// Field Access Helpers
// ============================================================================

/// Store an i32 value to a struct field at the given offset
pub fn storeI32Field(func: *Function, ptr: Value, offset: i32, value: Value) !void {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    try func.emitStoreI32(field_ptr, value);
}

/// Store an i64 value to a struct field at the given offset
pub fn storeI64Field(func: *Function, ptr: Value, offset: i32, value: Value) !void {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    try func.emitStore(field_ptr, value);
}

/// Load an i32 field from a struct and sign-extend to i64
pub fn loadI32AsI64(func: *Function, ptr: Value, offset: i32) !Value {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    const val_i32 = try func.emitLoad(field_ptr, .i32);
    return func.emitUnaryOp(.sext_i32_i64, val_i32, .i64);
}

/// Load an i64 field from a struct
pub fn loadI64Field(func: *Function, ptr: Value, offset: i32) !Value {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    return func.emitLoad(field_ptr, .i64);
}

// ============================================================================
// __ManagedArray Helpers
// ============================================================================

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
pub fn emitAllocRefcountedBuffer(func: *Function, data_size: Value, tag: []const u8) !Value {
    // Allocate data_size + 8 for header
    const header_size = try func.emitConstI64(REFCOUNTED_BUFFER_HEADER_SIZE);
    const total_size = try func.emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try func.emitHeapAlloc(total_size, tag);

    // Initialize refcount in header to 1 (for the ManagedArray being created)
    try func.emitStore(buffer_with_header, try func.emitConstI64(1));

    // Return data pointer (header + 8)
    const eight = try func.emitConstI64(8);
    return func.emitBinaryOp(.add, buffer_with_header, eight, .ptr);
}

/// Initialize __ManagedArray fields.
/// Layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)
///
/// Mode flags (bits 0-1 of flags field):
/// - 0 = SSO (future) - small inline storage
/// - 1 = heap-refcounted - refcount at buffer-8
/// - 2 = slice - view into parent buffer
pub fn initManagedArray(func: *Function, ptr: Value, buffer: Value, len: Value, capacity: Value, flags: Value) !void {
    // buffer pointer at offset 0
    try func.emitStore(ptr, buffer);

    // len (i64) at offset 8
    try storeI64Field(func, ptr, layouts.ManagedArray.LEN_OFFSET, len);

    // capacity (i64) at offset 16
    try storeI64Field(func, ptr, layouts.ManagedArray.CAPACITY_OFFSET, capacity);

    // flags (i32) at offset 24
    try storeI32Field(func, ptr, layouts.ManagedArray.FLAGS_OFFSET, flags);

    // parent_off (i32) at offset 28 - initialized to 0
    const zero_i32 = try func.emitConstI32(0);
    try storeI32Field(func, ptr, layouts.ManagedArray.PARENT_OFF_OFFSET, zero_i32);
}

/// Initialize __ManagedArray as empty (all zeros, SSO mode)
pub fn initManagedArrayEmpty(func: *Function, ptr: Value) !void {
    const null_ptr = try func.emitConstI64(0);
    const zero_i32 = try func.emitConstI32(0);

    try func.emitStore(ptr, null_ptr); // buffer at offset 0
    try storeI64Field(func, ptr, layouts.ManagedArray.LEN_OFFSET, null_ptr); // len
    try storeI64Field(func, ptr, layouts.ManagedArray.CAPACITY_OFFSET, null_ptr); // capacity
    try storeI32Field(func, ptr, layouts.ManagedArray.FLAGS_OFFSET, zero_i32); // flags
    try storeI32Field(func, ptr, layouts.ManagedArray.PARENT_OFF_OFFSET, zero_i32); // parent_off
}

/// Initialize __ManagedArray in slice mode (mode 2).
/// flags = 2 (slice mode)
pub fn initManagedArraySlice(func: *Function, ptr: Value, buffer: Value, len: Value, parent_off: Value) !void {
    // buffer pointer at offset 0
    try func.emitStore(ptr, buffer);

    // len (i64) at offset 8
    try storeI64Field(func, ptr, layouts.ManagedArray.LEN_OFFSET, len);

    // capacity = 0 for slices at offset 16
    const zero_i64 = try func.emitConstI64(0);
    try storeI64Field(func, ptr, layouts.ManagedArray.CAPACITY_OFFSET, zero_i64);

    // flags = 2 (slice mode) at offset 24
    const two_i32 = try func.emitConstI32(2);
    try storeI32Field(func, ptr, layouts.ManagedArray.FLAGS_OFFSET, two_i32);

    // parent_off (i32) at offset 28
    try storeI32Field(func, ptr, layouts.ManagedArray.PARENT_OFF_OFFSET, parent_off);
}

/// Allocate and initialize a __ManagedArray on the stack
pub fn emitManagedArray(func: *Function, buffer: Value, len: Value, capacity: Value, flags: Value) !Value {
    const ptr = try func.emitAllocaSized(layouts.ManagedArray.SIZE);
    try initManagedArray(func, ptr, buffer, len, capacity, flags);
    return ptr;
}

/// Allocate and initialize an empty __ManagedArray on the stack
pub fn emitEmptyManagedArray(func: *Function) !Value {
    const ptr = try func.emitAllocaSized(layouts.ManagedArray.SIZE);
    try initManagedArrayEmpty(func, ptr);
    return ptr;
}
