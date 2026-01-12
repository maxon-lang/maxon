/// Error union IR emission helpers.
/// Contains layout constants for error union types (T or Error).
const ir = @import("ir.zig");

// ============================================================================
// Error Union Layout (16 bytes)
// ============================================================================
/// Error union struct layout (16 bytes total)
/// Layout: tag(8) + value(8)
///
/// Fields:
/// - tag: i64 (8 bytes) - 0 = success (value is T), non-zero = error code
/// - value: T or error (8 bytes) - the success value or error enum value
pub const ErrorUnion = struct {
    const SIZE: i32 = 16;
    const TAG_OFFSET: i32 = 0;
    const VALUE_OFFSET: i32 = 8;

    comptime {
        if (VALUE_OFFSET + 8 != SIZE) {
            @compileError("ErrorUnion layout mismatch: VALUE_OFFSET + 8 != SIZE");
        }
        if (TAG_OFFSET != 0) {
            @compileError("ErrorUnion layout mismatch: TAG_OFFSET != 0");
        }
        if (VALUE_OFFSET != TAG_OFFSET + 8) {
            @compileError("ErrorUnion layout mismatch: VALUE_OFFSET != TAG_OFFSET + 8");
        }
    }

    /// Returns the total size of the error union struct in bytes
    pub fn size() i32 {
        return SIZE;
    }

    /// Returns a field pointer to the value field at VALUE_OFFSET
    pub fn getValuePtr(func: *ir.Function, eu_ptr: ir.Value) !ir.StructPtr {
        return func.emitGetFieldPtr(ir.toStructPtr(eu_ptr), VALUE_OFFSET);
    }

    /// Stores a success value: tag=0, value=value
    pub fn storeSuccess(func: *ir.Function, eu_ptr: ir.Value, value: ir.Value) !void {
        const zero = try func.emitConstI64(0);
        try func.emitStore(eu_ptr, zero); // tag at offset 0
        const value_ptr = try getValuePtr(func, eu_ptr);
        try func.emitStore(value_ptr.raw(), value);
    }

    /// Stores an error value: tag=1, value=error_value
    pub fn storeError(func: *ir.Function, eu_ptr: ir.Value, error_value: ir.Value) !void {
        const one = try func.emitConstI64(1);
        try func.emitStore(eu_ptr, one); // tag at offset 0
        const value_ptr = try getValuePtr(func, eu_ptr);
        try func.emitStore(value_ptr.raw(), error_value);
    }

    /// Loads the tag from the error union (at offset 0)
    pub fn loadTag(func: *ir.Function, eu_ptr: ir.Value) !ir.Value {
        return func.emitLoad(eu_ptr, .i64);
    }
};
