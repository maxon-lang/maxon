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
// __ManagedString Helpers
// ============================================================================

/// Size of string buffer header (refcount as i64)
pub const STRING_BUFFER_HEADER_SIZE: i64 = 8;

/// Allocate a string buffer with header for refcounting.
/// Returns the DATA pointer (header is at returned_ptr - 8).
///
/// Buffer layout:
///   [refcount: i64] [data bytes...]
///   ^               ^
///   |               +-- returned data_ptr
///   +-- allocation base (header)
///
/// The refcount is initialized to 1 (for the ManagedString being created).
pub fn emitAllocStringBuffer(func: *Function, data_size: Value, tag: []const u8) !Value {
    // Allocate data_size + 8 for header
    const header_size = try func.emitConstI64(STRING_BUFFER_HEADER_SIZE);
    const total_size = try func.emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try func.emitHeapAlloc(total_size, tag);

    // Initialize refcount in header to 1 (for the String being created)
    try func.emitStore(buffer_with_header, try func.emitConstI64(1));

    // Return data pointer (header + 8)
    const eight = try func.emitConstI64(8);
    return func.emitBinaryOp(.add, buffer_with_header, eight, .ptr);
}

/// Initialize __ManagedString fields for heap mode.
/// Layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
/// cap_flags = (len << 2) | 0b01 for heap mode
/// refcount field in struct is unused (actual refcount is in buffer header)
pub fn initManagedString(func: *Function, ptr: Value, buffer: Value, len_i64: Value) !void {
    // buffer pointer at offset 0
    try func.emitStore(ptr, buffer);

    // length (i32) at offset 8
    const len_i32 = try func.emitUnaryOp(.trunc_i64_i32, len_i64, .i32);
    try storeI32Field(func, ptr, layouts.ManagedString.LEN_OFFSET, len_i32);

    // cap_flags = (len << 2) | 1 for heap mode at offset 12
    const four = try func.emitConstI32(4);
    const cap_shift = try func.emitBinaryOp(.mul, len_i32, four, .i32);
    const one_i32 = try func.emitConstI32(1);
    const cap_flags = try func.emitBinaryOp(.bitor, cap_shift, one_i32, .i32);
    try storeI32Field(func, ptr, layouts.ManagedString.CAP_FLAGS_OFFSET, cap_flags);

    // refcount = 1 at offset 16
    try storeI32Field(func, ptr, layouts.ManagedString.REFCOUNT_OFFSET, one_i32);

    // parent_off = 0 at offset 20
    const zero_i32 = try func.emitConstI32(0);
    try storeI32Field(func, ptr, layouts.ManagedString.PARENT_OFF_OFFSET, zero_i32);
}

/// Initialize __ManagedString as empty (all zeros, SSO mode)
pub fn initManagedStringEmpty(func: *Function, ptr: Value) !void {
    const null_ptr = try func.emitConstI64(0);
    const zero_i32 = try func.emitConstI32(0);

    try func.emitStore(ptr, null_ptr);
    try storeI32Field(func, ptr, layouts.ManagedString.LEN_OFFSET, zero_i32);
    try storeI32Field(func, ptr, layouts.ManagedString.CAP_FLAGS_OFFSET, zero_i32);
    try storeI32Field(func, ptr, layouts.ManagedString.REFCOUNT_OFFSET, zero_i32);
    try storeI32Field(func, ptr, layouts.ManagedString.PARENT_OFF_OFFSET, zero_i32);
}

/// Initialize __ManagedString in slice mode.
/// cap_flags = 0b10 (slice mode), refcount = 0
pub fn initManagedStringSlice(func: *Function, ptr: Value, buffer: Value, len_i32: Value, parent_off_i32: Value) !void {
    // buffer pointer at offset 0
    try func.emitStore(ptr, buffer);

    // len at offset 8
    try storeI32Field(func, ptr, layouts.ManagedString.LEN_OFFSET, len_i32);

    // cap_flags = 0b10 (slice mode) at offset 12
    const two = try func.emitConstI32(2);
    try storeI32Field(func, ptr, layouts.ManagedString.CAP_FLAGS_OFFSET, two);

    // refcount = 0 at offset 16
    const zero = try func.emitConstI32(0);
    try storeI32Field(func, ptr, layouts.ManagedString.REFCOUNT_OFFSET, zero);

    // parent_off at offset 20
    try storeI32Field(func, ptr, layouts.ManagedString.PARENT_OFF_OFFSET, parent_off_i32);
}

/// Allocate and initialize a __ManagedString on the stack (heap mode)
pub fn emitManagedString(func: *Function, buffer: Value, len_i64: Value) !Value {
    const ptr = try func.emitAllocaSized(layouts.ManagedString.SIZE);
    try initManagedString(func, ptr, buffer, len_i64);
    return ptr;
}

// ============================================================================
// __ManagedArray Helpers
// ============================================================================

/// Initialize __ManagedArray fields.
/// Layout: buffer(8) + len(8) + capacity(8)
pub fn initManagedArray(func: *Function, ptr: Value, buffer: Value, len: Value, capacity: Value) !void {
    try func.emitStore(ptr, buffer); // buffer at offset 0
    try storeI64Field(func, ptr, layouts.ManagedArray.LEN_OFFSET, len);
    try storeI64Field(func, ptr, layouts.ManagedArray.CAPACITY_OFFSET, capacity);
}

/// Initialize __ManagedArray as empty (buffer=null, len=0, capacity=0)
pub fn initManagedArrayEmpty(func: *Function, ptr: Value) !void {
    const null_ptr = try func.emitConstI64(0);
    try func.emitStore(ptr, null_ptr); // buffer at offset 0
    try storeI64Field(func, ptr, layouts.ManagedArray.LEN_OFFSET, null_ptr); // len
    try storeI64Field(func, ptr, layouts.ManagedArray.CAPACITY_OFFSET, null_ptr); // capacity
}

/// Allocate and initialize a __ManagedArray on the stack
pub fn emitManagedArray(func: *Function, buffer: Value, len: Value, capacity: Value) !Value {
    const ptr = try func.emitAllocaSized(layouts.ManagedArray.SIZE);
    try initManagedArray(func, ptr, buffer, len, capacity);
    return ptr;
}

/// Allocate and initialize an empty __ManagedArray on the stack
pub fn emitEmptyManagedArray(func: *Function) !Value {
    const ptr = try func.emitAllocaSized(layouts.ManagedArray.SIZE);
    try initManagedArrayEmpty(func, ptr);
    return ptr;
}
