/// Array and ManagedArray IR emission helpers.
/// Contains layout constants for ManagedArray and Array types.
/// ManagedArray is the unified managed type for both strings and arrays.
const std = @import("std");
const ir = @import("ir.zig");
const struct_helpers = @import("ir_struct_helpers.zig");
const types = @import("ast_to_ir_types.zig");
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;

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

// ============================================================================
// ManagedArray Helpers
// ============================================================================

/// Allocate a __ManagedArray on the stack and initialize it as empty.
pub fn emitEmptyManagedArray(self: *AstToIr) !ir.ManagedArrayPtr {
    return struct_helpers.emitEmptyManagedArray(self.func());
}

/// Initialize an existing __ManagedArray as empty.
pub fn initManagedArrayEmpty(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr) !void {
    return struct_helpers.initManagedArrayEmpty(self.func(), managed_ptr);
}

/// Initialize an existing __ManagedArray with buffer, length, and capacity values.
/// Uses mode=0 (no refcounting) for regular arrays.
pub fn initManagedArray(self: *AstToIr, managed_ptr: ir.ManagedArrayPtr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !void {
    const zero_flags = try self.func().emitConstI32(0); // mode=0 (no refcounting for regular arrays)
    return struct_helpers.initManagedArray(self.func(), managed_ptr, buffer, len, capacity, zero_flags);
}

/// Allocate a __ManagedArray on the stack and initialize with buffer, length, and capacity.
/// Uses mode=0 (no refcounting) for regular arrays.
pub fn emitManagedArray(self: *AstToIr, buffer: ir.RawPtr, len: ir.Value, capacity: ir.Value) !ir.ManagedArrayPtr {
    const zero_flags = try self.func().emitConstI32(0); // mode=0 (no refcounting for regular arrays)
    return struct_helpers.emitManagedArray(self.func(), buffer, len, capacity, zero_flags);
}
