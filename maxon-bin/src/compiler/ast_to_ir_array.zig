/// Array IR emission helpers.
/// Contains layout constants for Array types.
const string = @import("ast_to_ir_string.zig");
const ManagedArray = string.ManagedArray;

// ============================================================================
// Array Layout (40 bytes)
// ============================================================================
/// Array struct layout (40 bytes total)
/// This is the user-facing Array type that wraps __ManagedArray
/// Layout: __ManagedArray(32) + iterIndex(8) = 40 bytes
pub const Array = struct {
    pub const SIZE: i32 = 40;
    pub const MANAGED_ARRAY_OFFSET: i32 = 0;
    pub const ITER_INDEX_OFFSET: i32 = 32;

    comptime {
        if (SIZE < ManagedArray.SIZE) {
            @compileError("Array layout mismatch: SIZE < ManagedArray.SIZE");
        }
        if (ITER_INDEX_OFFSET != ManagedArray.SIZE) {
            @compileError("Array layout mismatch: ITER_INDEX_OFFSET != ManagedArray.SIZE");
        }
        if (ITER_INDEX_OFFSET + 8 != SIZE) {
            @compileError("Array layout mismatch: ITER_INDEX_OFFSET + 8 != SIZE");
        }
    }
};
