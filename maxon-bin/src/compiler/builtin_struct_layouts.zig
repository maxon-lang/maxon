/// Compile-time validated layouts for internal Maxon structs.
/// This is the single source of truth for all internal struct layouts.
///
/// All field offsets and sizes are validated at compile time to ensure
/// consistency and catch layout mismatches before they cause runtime errors.
/// __ManagedArray struct layout (32 bytes total)
/// Unified managed type for both strings and arrays.
/// Layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)
///
/// Fields:
/// - buffer: *T (8 bytes) - pointer to element data (for refcounted mode, refcount is at buffer - 8)
/// - len: i64 (8 bytes) - current length
/// - capacity: i64 (8 bytes) - allocated capacity
/// - flags: i32 (4 bytes) - mode flags (bits 0-1: 0=SSO, 1=heap-refcounted, 2=slice)
/// - parent_off: i32 (4 bytes) - offset from parent buffer for slice mode
///
/// Mode semantics:
/// - Mode 0 (SSO): Small storage optimization (future) - inline storage in struct
/// - Mode 1 (heap-refcounted): Ref-counted heap allocation, refcount at buffer - 8
/// - Mode 2 (slice): Zero-copy view into parent buffer, uses parent_off
pub const ManagedArray = struct {
    pub const SIZE: i32 = 32;
    pub const BUFFER_OFFSET: i32 = 0;
    pub const LEN_OFFSET: i32 = 8;
    pub const CAPACITY_OFFSET: i32 = 16;
    pub const FLAGS_OFFSET: i32 = 24;
    pub const PARENT_OFF_OFFSET: i32 = 28;

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
};

/// String struct layout (40 bytes total)
/// This is the user-facing String type that wraps __ManagedArray
/// Layout: __ManagedArray(32) + _iterPos(8) = 40 bytes
pub const String = struct {
    pub const SIZE: i32 = 40;
    pub const MANAGED_ARRAY_OFFSET: i32 = 0;
    pub const ITER_POS_OFFSET: i32 = 32;

    comptime {
        if (SIZE < ManagedArray.SIZE) {
            @compileError("String layout mismatch: SIZE < ManagedArray.SIZE");
        }
        if (ITER_POS_OFFSET != ManagedArray.SIZE) {
            @compileError("String layout mismatch: ITER_POS_OFFSET != ManagedArray.SIZE");
        }
        if (ITER_POS_OFFSET + 8 != SIZE) {
            @compileError("String layout mismatch: ITER_POS_OFFSET + 8 != SIZE");
        }
    }
};

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

/// cstring struct layout (24 bytes total)
/// Layout: data(8) + length(8) + managed(8)
///
/// Fields:
/// - data: *u8 (8 bytes) - pointer to null-terminated C string data
/// - length: i64 (8 bytes) - length of string (not including null terminator)
/// - managed: *__ManagedArray (8 bytes) - pointer to parent ManagedArray if borrowed, null if owned
pub const CString = struct {
    pub const SIZE: i32 = 24;
    pub const DATA_OFFSET: i32 = 0;
    pub const LENGTH_OFFSET: i32 = 8;
    pub const MANAGED_OFFSET: i32 = 16;

    comptime {
        if (MANAGED_OFFSET + 8 != SIZE) {
            @compileError("CString layout mismatch: MANAGED_OFFSET + 8 != SIZE");
        }
        if (DATA_OFFSET != 0) {
            @compileError("CString layout mismatch: DATA_OFFSET != 0");
        }
        if (LENGTH_OFFSET != DATA_OFFSET + 8) {
            @compileError("CString layout mismatch: LENGTH_OFFSET != DATA_OFFSET + 8");
        }
        if (MANAGED_OFFSET != LENGTH_OFFSET + 8) {
            @compileError("CString layout mismatch: MANAGED_OFFSET != LENGTH_OFFSET + 8");
        }
    }
};

/// Map$K$V struct layout (120 bytes total)
/// Layout: keys(32) + values(32) + states(32) + count(8) + capacity(8) + iter_index(8)
///
/// Fields:
/// - keys: Array$K (32 bytes) - array of keys
/// - values: Array$V (32 bytes) - array of values
/// - states: Array$int (32 bytes) - array of bucket states (0=empty, 1=occupied, 2=deleted)
/// - count: i64 (8 bytes) - number of key-value pairs
/// - capacity: i64 (8 bytes) - capacity of the hash table
/// - iter_index: i64 (8 bytes) - current iteration index
pub const Map = struct {
    pub const SIZE: i32 = 120;
    pub const KEYS_OFFSET: i32 = 0;
    pub const VALUES_OFFSET: i32 = 32;
    pub const STATES_OFFSET: i32 = 64;
    pub const COUNT_OFFSET: i32 = 96;
    pub const CAPACITY_OFFSET: i32 = 104;
    pub const ITER_INDEX_OFFSET: i32 = 112;

    comptime {
        if (VALUES_OFFSET != KEYS_OFFSET + 32) {
            @compileError("Map layout mismatch: VALUES_OFFSET != KEYS_OFFSET + 32");
        }
        if (STATES_OFFSET != VALUES_OFFSET + 32) {
            @compileError("Map layout mismatch: STATES_OFFSET != VALUES_OFFSET + 32");
        }
        if (COUNT_OFFSET != STATES_OFFSET + 32) {
            @compileError("Map layout mismatch: COUNT_OFFSET != STATES_OFFSET + 32");
        }
        if (CAPACITY_OFFSET != COUNT_OFFSET + 8) {
            @compileError("Map layout mismatch: CAPACITY_OFFSET != COUNT_OFFSET + 8");
        }
        if (ITER_INDEX_OFFSET != CAPACITY_OFFSET + 8) {
            @compileError("Map layout mismatch: ITER_INDEX_OFFSET != CAPACITY_OFFSET + 8");
        }
        if (ITER_INDEX_OFFSET + 8 != SIZE) {
            @compileError("Map layout mismatch: ITER_INDEX_OFFSET + 8 != SIZE");
        }
    }
};
