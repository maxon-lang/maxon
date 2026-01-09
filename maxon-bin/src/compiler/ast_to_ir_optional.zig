/// Optional IR emission helpers.
/// Contains layout constants for Optional types.

// ============================================================================
// Optional Layout (16 bytes)
// ============================================================================
/// Optional<T> struct layout (16 bytes total)
/// Layout: has_value(8) + value(8)
///
/// Fields:
/// - has_value: i64 (8 bytes) - 0 = None, 1 = Some
/// - value: T (8 bytes) - the wrapped value (only valid if has_value == 1)
pub const Optional = struct {
    pub const SIZE: i32 = 16;
    pub const HAS_VALUE_OFFSET: i32 = 0;
    pub const VALUE_OFFSET: i32 = 8;

    comptime {
        if (VALUE_OFFSET + 8 != SIZE) {
            @compileError("Optional layout mismatch: VALUE_OFFSET + 8 != SIZE");
        }
        if (HAS_VALUE_OFFSET != 0) {
            @compileError("Optional layout mismatch: HAS_VALUE_OFFSET != 0");
        }
        if (VALUE_OFFSET != HAS_VALUE_OFFSET + 8) {
            @compileError("Optional layout mismatch: VALUE_OFFSET != HAS_VALUE_OFFSET + 8");
        }
    }
};
