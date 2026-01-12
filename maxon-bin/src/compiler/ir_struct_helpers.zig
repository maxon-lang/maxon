const ir = @import("ir.zig");

const Function = ir.Function;
const Value = ir.Value;
const StructPtr = ir.StructPtr;

// ============================================================================
// Field Access Helpers
// ============================================================================

/// Store an i32 value to a struct field at the given offset
pub fn storeI32Field(func: *Function, ptr: StructPtr, offset: i32, value: Value) !void {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    try func.emitStoreI32(field_ptr.raw(), value);
}

/// Store an i64 value to a struct field at the given offset
pub fn storeI64Field(func: *Function, ptr: StructPtr, offset: i32, value: Value) !void {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    try func.emitStore(field_ptr.raw(), value);
}

/// Load an i32 field from a struct and sign-extend to i64
pub fn loadI32AsI64(func: *Function, ptr: StructPtr, offset: i32) !Value {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    const val_i32 = try func.emitLoad(field_ptr.raw(), .i32);
    return func.emitUnaryOp(.sext_i32_i64, val_i32, .i64);
}

/// Load an i64 field from a struct
pub fn loadI64Field(func: *Function, ptr: StructPtr, offset: i32) !Value {
    const field_ptr = try func.emitGetFieldPtr(ptr, offset);
    return func.emitLoad(field_ptr.raw(), .i64);
}
