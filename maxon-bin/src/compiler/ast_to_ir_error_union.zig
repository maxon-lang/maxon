/// Error union IR emission helpers.
/// Contains layout constants for error union types (T or Error).

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
    pub const SIZE: i32 = 16;
    pub const TAG_OFFSET: i32 = 0;
    pub const VALUE_OFFSET: i32 = 8;

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
};
