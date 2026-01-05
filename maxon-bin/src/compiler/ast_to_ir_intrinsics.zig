const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const TypedValue = types.TypedValue;
const ConvertError = types.ConvertError;
const intrinsics_registry = @import("intrinsics_registry.zig");

// Forward reference to main AstToIr module
const AstToIr = @import("4-ast_to_ir.zig").AstToIr;

// ============================================================================
// Common Helper Functions
// ============================================================================

/// Validate argument count and report error if mismatch
fn expectArgCount(self: *AstToIr, call: ast.CallExpr, expected: usize) ConvertError!void {
    if (call.args.len != expected) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }
}

/// Load an i32 field from a struct pointer and sign-extend to i64
fn loadI32AsI64(self: *AstToIr, base_ptr: ir.Value, offset: i32) ConvertError!ir.Value {
    const field_ptr = try self.func().emitGetFieldPtr(base_ptr, offset);
    const val_i32 = try self.func().emitLoad(field_ptr, .i32);
    return self.func().emitUnaryOp(.sext_i32_i64, val_i32, .i64);
}

/// Load an i64 field from a struct pointer
fn loadI64Field(self: *AstToIr, base_ptr: ir.Value, offset: i32) ConvertError!ir.Value {
    const field_ptr = try self.func().emitGetFieldPtr(base_ptr, offset);
    return self.func().emitLoad(field_ptr, .i64);
}

/// Store an i32 value to a field
fn storeI32Field(self: *AstToIr, base_ptr: ir.Value, offset: i32, value: ir.Value) ConvertError!void {
    const field_ptr = try self.func().emitGetFieldPtr(base_ptr, offset);
    try self.func().emitStoreI32(field_ptr, value);
}

/// Store an i64 value to a field
fn storeI64Field(self: *AstToIr, base_ptr: ir.Value, offset: i32, value: ir.Value) ConvertError!void {
    const field_ptr = try self.func().emitGetFieldPtr(base_ptr, offset);
    try self.func().emitStore(field_ptr, value);
}

/// Initialize heap string metadata fields (cap_flags, refcount, parent_off)
/// cap_flags = (len * 4) | 0b01 for heap mode
fn initHeapStringFields(self: *AstToIr, result_ptr: ir.Value, len_i32: ir.Value) ConvertError!void {
    const four = try self.func().emitConstI32(4);
    const cap_shifted = try self.func().emitBinaryOp(.mul, len_i32, four, .i32);
    const one = try self.func().emitConstI32(1);
    const cap_flags = try self.func().emitBinaryOp(.bitor, cap_shifted, one, .i32);
    try storeI32Field(self, result_ptr, 12, cap_flags);

    // refcount = 1
    try storeI32Field(self, result_ptr, 16, one);

    // parent_off = 0
    const zero = try self.func().emitConstI32(0);
    try storeI32Field(self, result_ptr, 20, zero);
}

/// Initialize slice string metadata fields
/// cap_flags = 0b10 (slice mode), refcount = 0, parent_off from parameter
fn initSliceStringFields(self: *AstToIr, result_ptr: ir.Value, parent_off_i32: ir.Value) ConvertError!void {
    // cap_flags = 0b10 (slice mode)
    const two = try self.func().emitConstI32(2);
    try storeI32Field(self, result_ptr, 12, two);

    // refcount = 0
    const zero = try self.func().emitConstI32(0);
    try storeI32Field(self, result_ptr, 16, zero);

    // parent_off
    try storeI32Field(self, result_ptr, 20, parent_off_i32);
}

/// Wrap a nullable pointer result in an optional type
fn wrapInOptional(self: *AstToIr, result_ptr: ir.Value, wrapped_type: []const u8) ConvertError!TypedValue {
    const opt_ptr = try self.func().emitAllocaSized(16);

    const zero = try self.func().emitConstI64(0);
    const is_null = try self.func().emitBinaryOp(.icmp_eq, result_ptr, zero, .i64);

    const one = try self.func().emitConstI64(1);
    const tag = try self.func().emitBinaryOp(.sub, one, is_null, .i64);
    try self.func().emitStore(opt_ptr, tag);

    try storeI64Field(self, opt_ptr, 8, result_ptr);

    return .{
        .value = opt_ptr,
        .ty = .{ .optional_type = .{ .wrapped = .ptr, .wrapped_struct_type = wrapped_type } },
    };
}

// ============================================================================
// Built-in Functions
// ============================================================================

/// Check if current source file is part of stdlib
/// Internal types are allowed in stdlib code and monomorphized stdlib methods
pub fn isStdlibFile(self: *AstToIr) bool {
    // Allow stdlib builtins when converting monomorphized stdlib methods
    if (self.in_stdlib_method) return true;

    const path = self.source_file orelse return false;
    // Check for /stdlib/ or \stdlib\ in path (including at the start)
    return std.mem.indexOf(u8, path, "/stdlib/") != null or
        std.mem.indexOf(u8, path, "\\stdlib\\") != null or
        std.mem.startsWith(u8, path, "stdlib/") or
        std.mem.startsWith(u8, path, "stdlib\\");
}

/// Check if a type name is internal (starts with underscore)
/// Internal types like __ManagedArray can only be used in stdlib code
pub fn isInternalType(type_name: []const u8) bool {
    return type_name.len > 0 and type_name[0] == '_';
}

/// Check if internal type access is allowed, returns error if not
pub fn checkInternalTypeAccess(self: *AstToIr, type_name: []const u8) ConvertError!void {
    if (isInternalType(type_name) and !isStdlibFile(self)) {
        self.reportError(.E018, type_name);
        return error.SemanticError;
    }
}

pub fn convertBuiltin(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    // Check for __managed_array_* intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__managed_array_")) {
        if (!isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertManagedArrayIntrinsic(self, call);
    }

    // Check for __string_* intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__string_")) {
        if (!isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertStringIntrinsic(self, call);
    }

    // Check for __cstring_* intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__cstring_")) {
        if (!isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertCstringIntrinsic(self, call);
    }

    // Check for __make_char_* intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__make_char_")) {
        if (!isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertMakeCharIntrinsic(self, call);
    }

    // Check for file I/O intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__read_file") or
        std.mem.startsWith(u8, call.func_name, "__write_file") or
        std.mem.startsWith(u8, call.func_name, "__list_directory") or
        std.mem.startsWith(u8, call.func_name, "__is_directory"))
    {
        if (!isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertFileIntrinsic(self, call);
    }

    // Look up math builtin in registry
    const builtin = intrinsics_registry.isMathBuiltin(call.func_name) orelse return error.NotABuiltin;

    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const arg = try self.convertExpression(call.args[0]);
    const arg_prim = arg.ty.toPrimitiveType();
    const arg_ir_type = builtin.arg_ir_type.?;

    // Get the actual value to use, with implicit int-to-float promotion if needed
    const actual_value = if (arg_prim != arg_ir_type) blk: {
        // Allow int -> float promotion for builtins expecting float
        if (arg_ir_type == .f64 and arg_prim == .i64) {
            break :blk self.func().emitUnaryOp(.sitofp, arg.value, .f64) catch return error.OutOfMemory;
        }
        const msg = std.fmt.allocPrint(self.allocator, "{s}() requires {s} argument", .{ call.func_name, arg_ir_type.toMaxonName() }) catch call.func_name;
        self.reportError(.E022, msg);
        return error.TypeMismatch;
    } else arg.value;

    const result = self.func().emitUnaryOp(builtin.ir_op.?, actual_value, builtin.return_ir_type) catch return error.OutOfMemory;

    return .{ .value = result, .ty = .{ .primitive = builtin.return_type_name } };
}

// ============================================================================
// __ManagedArray Intrinsics (stdlib-only)
// ============================================================================

/// Element info for managed array operations
const ElemInfo = struct {
    size: i32,
    is_struct: bool,
    type_name: ?[]const u8,
};

/// Direction for array shift operations
const ShiftDirection = enum { left, right };

/// Get element size and type info from generic_params or current_type_name
/// Used by managed array intrinsics to determine element size at compile time
fn getElementInfo(self: *AstToIr) ElemInfo {
    // Check generic_params for "Element" binding (available in monomorphized methods)
    if (self.generic_params.get("Element")) |elem_type_name| {
        if (self.type_map.get(elem_type_name)) |type_info| {
            if (type_info == .struct_type) {
                return .{
                    .size = type_info.struct_type.size,
                    .is_struct = true,
                    .type_name = elem_type_name,
                };
            }
        }
        // Known type but not a struct (int, float, bool, byte)
        return .{ .size = 8, .is_struct = false, .type_name = elem_type_name };
    }

    // Fallback: extract from current_type_name (e.g., "Array$String")
    if (self.current_type_name) |type_name| {
        if (std.mem.startsWith(u8, type_name, "Array$")) {
            const elem_name = type_name[6..];
            if (self.type_map.get(elem_name)) |type_info| {
                if (type_info == .struct_type) {
                    return .{
                        .size = type_info.struct_type.size,
                        .is_struct = true,
                        .type_name = elem_name,
                    };
                }
            }
            return .{ .size = 8, .is_struct = false, .type_name = elem_name };
        }
    }

    // Default to primitive (8 bytes)
    return .{ .size = 8, .is_struct = false, .type_name = null };
}

/// Helper for shift operations - handles both left and right directions
/// For right shift: iterates backwards (i = len-1 down to start_index), dst = i + count
/// For left shift: iterates forwards (i = 0 while src_start + i < len), src = start_index + count + i
fn emitArrayShift(
    self: *AstToIr,
    managed: TypedValue,
    start_index: TypedValue,
    count: TypedValue,
    direction: ShiftDirection,
) ConvertError!void {
    const elem_info = getElementInfo(self);
    const elem_size_val = try self.func().emitConstI64(elem_info.size);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const len = try loadI64Field(self, managed.value, 8);

    const one = try self.func().emitConstI64(1);
    const zero = try self.func().emitConstI64(0);

    // Initialize loop counter
    const i_ptr = try self.func().emitAllocaSized(8);

    // Direction-specific setup
    const loop_name = if (direction == .right) "shift_right" else "shift_left";
    const cond_name = if (direction == .right) "shift_right_cond" else "shift_left_cond";
    const body_name = if (direction == .right) "shift_right_body" else "shift_left_body";
    const end_name = if (direction == .right) "shift_right_end" else "shift_left_end";
    _ = loop_name;

    if (direction == .right) {
        // Right shift: start at len-1
        const init_i = try self.func().emitBinaryOp(.sub, len, one, .i64);
        try self.func().emitStore(i_ptr, init_i);
    } else {
        // Left shift: start at 0
        try self.func().emitStore(i_ptr, zero);
    }

    var loop = try self.createLoopBlocks(cond_name, body_name, end_name);

    // Condition check
    const i_val = try self.func().emitLoad(i_ptr, .i64);
    const cond = if (direction == .right)
        // Right: i >= start_index
        try self.func().emitBinaryOp(.icmp_ge, i_val, start_index.value, .i64)
    else blk: {
        // Left: start_index + count + i < len
        const src_idx = try self.func().emitBinaryOp(.add, start_index.value, count.value, .i64);
        const src_idx_i = try self.func().emitBinaryOp(.add, src_idx, i_val, .i64);
        break :blk try self.func().emitBinaryOp(.icmp_lt, src_idx_i, len, .i64);
    };

    try self.emitLoopCondBranch(&loop, cond);

    // Loop body: copy element from src to dst
    const i_val2 = try self.func().emitLoad(i_ptr, .i64);

    const src_idx: ir.Value = if (direction == .right)
        i_val2
    else blk: {
        // Left: src_idx = start_index + count + i
        const base = try self.func().emitBinaryOp(.add, start_index.value, count.value, .i64);
        break :blk try self.func().emitBinaryOp(.add, base, i_val2, .i64);
    };

    const dst_idx: ir.Value = if (direction == .right)
        // Right: dst_idx = i + count
        try self.func().emitBinaryOp(.add, i_val2, count.value, .i64)
    else
        // Left: dst_idx = start_index + i
        try self.func().emitBinaryOp(.add, start_index.value, i_val2, .i64);

    const src_offset = try self.func().emitBinaryOp(.mul, src_idx, elem_size_val, .i64);
    const src_ptr = try self.func().emitBinaryOp(.add, buf_ptr, src_offset, .ptr);

    const dst_offset = try self.func().emitBinaryOp(.mul, dst_idx, elem_size_val, .i64);
    const dst_ptr = try self.func().emitBinaryOp(.add, buf_ptr, dst_offset, .ptr);

    // Copy element: struct uses memcpy, primitive uses load/store
    if (elem_info.is_struct) {
        try self.func().emitMemcpy(dst_ptr, src_ptr, elem_info.size);
    } else {
        const elem_val = try self.func().emitLoad(src_ptr, .i64);
        try self.func().emitStore(dst_ptr, elem_val);
    }

    // Update loop counter
    const new_i = if (direction == .right)
        try self.func().emitBinaryOp(.sub, i_val2, one, .i64)
    else
        try self.func().emitBinaryOp(.add, i_val2, one, .i64);
    try self.func().emitStore(i_ptr, new_i);
    try self.func().emitBr(loop.cond_block_idx);

    try self.finishLoop(&loop);
}

fn convertManagedArrayIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    const name = call.func_name;

    if (std.mem.eql(u8, name, "__managed_array_len")) {
        return intrinsicManagedArrayLen(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_capacity")) {
        return intrinsicManagedArrayCapacity(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_create")) {
        return intrinsicManagedArrayCreate(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_set_at")) {
        return intrinsicManagedArraySetAt(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_set_length")) {
        return intrinsicManagedArraySetLength(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_grow")) {
        return intrinsicManagedArrayGrow(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_shift_right")) {
        return intrinsicManagedArrayShiftRight(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_shift_left")) {
        return intrinsicManagedArrayShiftLeft(self, call);
    } else if (std.mem.eql(u8, name, "__managed_array_get_unchecked")) {
        return intrinsicManagedArrayGetUnchecked(self, call);
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}

/// __managed_array_len(managed) -> int
fn intrinsicManagedArrayLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);
    const len = try loadI64Field(self, managed.value, 8);
    return .{ .value = len, .ty = .{ .primitive = "int" } };
}

/// __managed_array_capacity(managed) -> int
fn intrinsicManagedArrayCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);
    const cap = try loadI64Field(self, managed.value, 16);
    return .{ .value = cap, .ty = .{ .primitive = "int" } };
}

/// __managed_array_create(capacity) -> __ManagedArray
/// Creates a new managed array with the given size/capacity (length = capacity)
fn intrinsicManagedArrayCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const capacity = try self.convertExpression(call.args[0]);

    const managed_ptr = try self.func().emitAllocaSized(24);

    const elem_info = getElementInfo(self);
    const elem_size_val = try self.func().emitConstI64(elem_info.size);
    const buf_size = try self.func().emitBinaryOp(.mul, capacity.value, elem_size_val, .i64);
    const buf_ptr = try self.func().emitHeapAlloc(buf_size, "array buffer");

    try self.func().emitStore(managed_ptr, buf_ptr);
    try storeI64Field(self, managed_ptr, 8, capacity.value);
    try storeI64Field(self, managed_ptr, 16, capacity.value);

    return .{ .value = managed_ptr, .ty = .{ .primitive = "ptr" } };
}

/// __managed_array_set_at(managed, index, value) -> void
fn intrinsicManagedArraySetAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const value = try self.convertExpression(call.args[2]);

    const elem_size: i32 = if (value.ty == .struct_type) blk: {
        if (self.type_map.get(value.ty.struct_type)) |type_info| {
            if (type_info == .struct_type) {
                break :blk @intCast(type_info.struct_type.size);
            }
        }
        break :blk 8;
    } else 8;

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, elem_size);

    if (value.ty == .struct_type and elem_size > 8) {
        try self.func().emitMemcpy(elem_ptr, value.value, @intCast(elem_size));
    } else {
        try self.func().emitStore(elem_ptr, value.value);
    }

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_set_length(managed, new_len) -> void
fn intrinsicManagedArraySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const new_len = try self.convertExpression(call.args[1]);
    try storeI64Field(self, managed.value, 8, new_len.value);
    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_grow(managed, new_capacity) -> void
fn intrinsicManagedArrayGrow(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const new_capacity = try self.convertExpression(call.args[1]);

    const elem_info = getElementInfo(self);
    const elem_size_val = try self.func().emitConstI64(elem_info.size);
    const new_size = try self.func().emitBinaryOp(.mul, new_capacity.value, elem_size_val, .i64);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const new_buf = try self.func().emitHeapRealloc(buf_ptr, new_size, "array grow");

    try self.func().emitStore(managed.value, new_buf);
    try storeI64Field(self, managed.value, 16, new_capacity.value);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_shift_right(managed, start_index, count) -> void
/// Shifts elements right, iterating backwards to handle overlap
fn intrinsicManagedArrayShiftRight(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);
    try emitArrayShift(self, managed, start_index, count, .right);
    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_shift_left(managed, start_index, count) -> void
/// Shifts elements left, iterating forwards to handle overlap
fn intrinsicManagedArrayShiftLeft(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);
    try emitArrayShift(self, managed, start_index, count, .left);
    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_get_unchecked(managed, index) -> Element
fn intrinsicManagedArrayGetUnchecked(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = getElementInfo(self);
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, elem_info.size);

    if (elem_info.is_struct) {
        return .{ .value = elem_ptr, .ty = .{ .struct_type = elem_info.type_name.? } };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr, .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = elem_info.type_name orelse "int" } };
    }
}

// ============================================================================
// __ManagedString Intrinsics (stdlib-only)
// ============================================================================
//
// __ManagedString memory layout (16 bytes):
//   Offset 0: ptr buffer   - Pointer to UTF-8 string data (8 bytes)
//   Offset 8: i32 len      - Byte length of string (4 bytes)
//   Offset 12: i32 capacity - Buffer capacity (4 bytes)
//
// When capacity = 0, the string is a constant (no heap cleanup needed).
// When capacity > 0, the string is heap-allocated with refcount header:
//   [refcount:i32][capacity:i32][...data bytes...]
//        -8           -4            0+

fn convertStringIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    const name = call.func_name;

    if (std.mem.eql(u8, name, "__string_len")) {
        return intrinsicStringLen(self, call);
    } else if (std.mem.eql(u8, name, "__string_byte_at")) {
        return intrinsicStringByteAt(self, call);
    } else if (std.mem.eql(u8, name, "__string_slice")) {
        return intrinsicStringSlice(self, call);
    } else if (std.mem.eql(u8, name, "__string_concat")) {
        return intrinsicStringConcat(self, call);
    } else if (std.mem.eql(u8, name, "__string_make_unique")) {
        return intrinsicStringMakeUnique(self, call);
    } else if (std.mem.eql(u8, name, "__string_set_byte")) {
        return intrinsicStringSetByte(self, call);
    } else if (std.mem.eql(u8, name, "__string_to_cstring")) {
        return intrinsicStringToCstring(self, call);
    } else if (std.mem.eql(u8, name, "__string_from_characters")) {
        return intrinsicStringFromCharacters(self, call);
    } else if (std.mem.eql(u8, name, "__string_incref")) {
        return intrinsicStringIncref(self, call);
    } else if (std.mem.eql(u8, name, "__string_refcount")) {
        return intrinsicStringRefcount(self, call);
    } else if (std.mem.eql(u8, name, "__string_is_heap")) {
        return intrinsicStringIsHeap(self, call);
    } else if (std.mem.eql(u8, name, "__string_cap_flags")) {
        return intrinsicStringCapFlags(self, call);
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}

/// Dispatch __cstring_* intrinsics
fn convertCstringIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    const name = call.func_name;

    if (std.mem.eql(u8, name, "__cstring_write_stdout")) {
        return intrinsicCstringWriteStdout(self, call);
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}

/// __cstring_write_stdout(cs) -> int
fn intrinsicCstringWriteStdout(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const cstring = try self.convertExpression(call.args[0]);

    const data_ptr = try self.func().emitLoad(cstring.value, .ptr);
    const length = try loadI64Field(self, cstring.value, 8);

    const args = try self.allocator.alloc(ir.Value, 2);
    args[0] = data_ptr;
    args[1] = length;

    const result = try self.func().emitCall("__write_stdout", args, .i64);
    return .{ .value = result orelse try self.func().emitConstI64(0), .ty = .{ .primitive = "int" } };
}

/// Dispatch __make_char_* intrinsics
fn convertMakeCharIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    const name = call.func_name;

    if (std.mem.eql(u8, name, "__make_char_from_bytes")) {
        return intrinsicMakeCharFromBytes(self, call);
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}

/// __make_char_from_bytes(managed, pos, len) -> Character
/// Creates a slice-mode Character struct
fn intrinsicMakeCharFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const pos = try self.convertExpression(call.args[1]);
    const len = try self.convertExpression(call.args[2]);

    const char_ptr = try self.func().emitAllocaSized(24);
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer pointer (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, pos.value, .ptr);
    try self.func().emitStore(char_ptr, slice_buf);

    // Store len (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len.value, .i32);
    try storeI32Field(self, char_ptr, 8, len_i32);

    // Initialize slice metadata fields
    const pos_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, pos.value, .i32);
    try initSliceStringFields(self, char_ptr, pos_i32);

    return .{ .value = char_ptr, .ty = .{ .struct_type = "Character" } };
}

/// __string_len(managed) -> int
/// Returns the byte length of the __ManagedString (offset 8, i32)
fn intrinsicStringLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);
    const len = try loadI32AsI64(self, managed.value, 8);
    return .{ .value = len, .ty = .{ .primitive = "int" } };
}

/// __string_byte_at(managed, index) -> byte
/// Returns the byte at the given index in the string buffer
fn intrinsicStringByteAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 1);
    const byte_val = try self.func().emitLoad(byte_ptr, .i8);

    return .{ .value = byte_val, .ty = .{ .primitive = "byte" } };
}

/// __string_slice(managed, start, end) -> __ManagedString (slice mode)
/// Creates a slice view into the managed string
/// Returns a 24-byte __ManagedString with cap_flags = 0b10 (slice mode)
/// Layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
fn intrinsicStringSlice(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);

    // Track borrow source for compile-time borrow checking
    if (call.args[0] == .field_access) {
        const fa = call.args[0].field_access;
        if (fa.base.* == .identifier) {
            self.pending_borrow_source = fa.base.identifier;
        } else if (fa.base.* == .self_expr) {
            self.pending_borrow_source = "self";
        }
    } else if (call.args[0] == .identifier) {
        self.pending_borrow_source = call.args[0].identifier;
    }

    const managed = try self.convertExpression(call.args[0]);
    const start = try self.convertExpression(call.args[1]);
    const end = try self.convertExpression(call.args[2]);

    const slice_ptr = try self.func().emitAllocaSized(24);
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer = parent_buf_ptr + start (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, start.value, .ptr);
    try self.func().emitStore(slice_ptr, slice_buf);

    // Store len = end - start (offset 8, i32)
    const len_i64 = try self.func().emitBinaryOp(.sub, end.value, start.value, .i64);
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len_i64, .i32);
    try storeI32Field(self, slice_ptr, 8, len_i32);

    // Initialize slice metadata (cap_flags=0b10, refcount=0, parent_off=start)
    const start_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, start.value, .i32);
    try initSliceStringFields(self, slice_ptr, start_i32);

    return .{ .value = slice_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_concat(a, b) -> __ManagedString
/// Concatenates two managed strings into a new heap-allocated string
fn intrinsicStringConcat(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);

    const a = try self.convertExpression(call.args[0]);
    const b = try self.convertExpression(call.args[1]);

    // Load buffer pointers BEFORE allocating new struct to avoid aliasing in loops
    const a_buf = try self.func().emitLoad(a.value, .ptr);
    const b_buf = try self.func().emitLoad(b.value, .ptr);

    // Load lengths (offset 8, i32)
    const a_len = try loadI32AsI64(self, a.value, 8);
    const b_len = try loadI32AsI64(self, b.value, 8);
    const total_len = try self.func().emitBinaryOp(.add, a_len, b_len, .i64);

    const result_ptr = try self.func().emitAllocaSized(24);
    const buf_ptr = try self.func().emitHeapAlloc(total_len, "string concat");

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, buf_ptr);

    // Store length (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, total_len, .i32);
    try storeI32Field(self, result_ptr, 8, len_i32);

    // Initialize heap metadata (cap_flags, refcount, parent_off)
    try initHeapStringFields(self, result_ptr, len_i32);

    // Copy both strings into the new buffer
    try self.func().emitMemcpyDynamic(buf_ptr, a_buf, a_len);
    const dst_offset = try self.func().emitBinaryOp(.add, buf_ptr, a_len, .ptr);
    try self.func().emitMemcpyDynamic(dst_offset, b_buf, b_len);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_make_unique(managed) -> __ManagedString
/// Creates a mutable copy of the string (COW - copy on write)
fn intrinsicStringMakeUnique(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load length and original buffer
    const len_i32 = try self.func().emitLoad(try self.func().emitGetFieldPtr(managed.value, 8), .i32);
    const len = try self.func().emitUnaryOp(.sext_i32_i64, len_i32, .i64);
    const orig_buf = try self.func().emitLoad(managed.value, .ptr);

    const result_ptr = try self.func().emitAllocaSized(24);
    const new_buf = try self.func().emitHeapAlloc(len, "string buffer");
    try self.func().emitMemcpyDynamic(new_buf, orig_buf, len);

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, new_buf);

    // Store length (offset 8, i32)
    try storeI32Field(self, result_ptr, 8, len_i32);

    // Initialize heap metadata (cap_flags, refcount, parent_off)
    try initHeapStringFields(self, result_ptr, len_i32);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_set_byte(managed, index, byte) -> void
/// Sets a byte at the given index (caller must ensure string is unique)
fn intrinsicStringSetByte(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const byte_val = try self.convertExpression(call.args[2]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 1);
    const byte_i8 = try self.func().emitUnaryOp(.trunc_i64_i8, byte_val.value, .i8);
    try self.func().emitStoreI8(byte_ptr, byte_i8);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __string_to_cstring(managed) -> cstring struct
/// Creates a cstring struct from a __ManagedString.
/// For SSO/Heap mode: shares buffer (already null-terminated), holds reference to managed string
/// For Slice mode: copies data to new null-terminated buffer (cstring owns its buffer)
fn intrinsicStringToCstring(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const cstring_ptr = try self.func().emitAllocaSized(24);

    // Load cap_flags (offset 12) to determine mode
    const cap_flags = try self.func().emitLoad(try self.func().emitGetFieldPtr(managed.value, 12), .i32);
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
    const two = try self.func().emitConstI32(2);
    const is_slice = try self.func().emitBinaryOp(.icmp_eq, mode, two, .i32);

    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const slice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_slice");

    // === SLICE BLOCK: Copy data to new null-terminated buffer ===
    const slice_buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const slice_len = try loadI32AsI64(self, managed.value, 8);

    const one_i64 = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, slice_len, one_i64, .i64);
    const new_buf = try self.func().emitHeapAlloc(alloc_size, "cstring conversion");

    try self.func().emitMemcpyDynamic(new_buf, slice_buf_ptr, slice_len);

    // Write null terminator
    const null_pos = try self.func().emitBinaryOp(.add, new_buf, slice_len, .ptr);
    try self.func().emitStoreI8(null_pos, try self.func().emitConstI64(0));

    // Store cstring fields (data, length, managed=null)
    try self.func().emitStore(cstring_ptr, new_buf);
    try storeI64Field(self, cstring_ptr, 8, slice_len);
    try storeI64Field(self, cstring_ptr, 16, try self.func().emitConstI64(0));

    // Create nonslice block
    const nonslice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_nonslice");

    // === NON-SLICE BLOCK: Share existing buffer ===
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const length = try loadI32AsI64(self, managed.value, 8);

    // Store cstring fields (data, length, managed)
    try self.func().emitStore(cstring_ptr, buf_ptr);
    try storeI64Field(self, cstring_ptr, 8, length);
    try storeI64Field(self, cstring_ptr, 16, managed.value);

    // Check if heap mode for incref
    const one_i32 = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

    const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_incref");

    // Increment refcount (offset 16 in __ManagedString)
    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const old_ref = try self.func().emitLoad(ref_ptr, .i32);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one_i32, .i32);
    try self.func().emitStoreI32(ref_ptr, new_ref);

    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_end");

    // Add branch instructions to blocks
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_slice }, .{ .block_ref = slice_block_idx }, .{ .block_ref = nonslice_block_idx } },
    });
    try self.func().blocks.items[slice_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });
    try self.func().blocks.items[nonslice_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
    });
    try self.func().blocks.items[incref_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });

    return .{ .value = cstring_ptr, .ty = .{ .struct_type = "cstring" } };
}

/// __string_from_characters(buffer, length) -> __ManagedString
/// Creates a new string from a byte array buffer
fn intrinsicStringFromCharacters(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const buffer = try self.convertExpression(call.args[0]);
    const length = try self.convertExpression(call.args[1]);

    const result_ptr = try self.func().emitAllocaSized(24);
    const new_buf = try self.func().emitHeapAlloc(length.value, "string buffer");

    // Copy data from source buffer
    const src_buf = try self.func().emitLoad(buffer.value, .ptr);
    try self.func().emitMemcpyDynamic(new_buf, src_buf, length.value);

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, new_buf);

    // Store length (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, length.value, .i32);
    try storeI32Field(self, result_ptr, 8, len_i32);

    // Initialize heap metadata (cap_flags, refcount, parent_off)
    try initHeapStringFields(self, result_ptr, len_i32);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

// ============================================================================
// Reference Counting Intrinsics for COW Strings
// ============================================================================

/// __string_incref(managed) -> void
/// Increments the reference count for heap-allocated strings
fn intrinsicStringIncref(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const old_ref = try self.func().emitLoad(ref_ptr, .i32);
    const one = try self.func().emitConstI32(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i32);
    try self.func().emitStoreI32(ref_ptr, new_ref);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __string_refcount(managed) -> int
fn intrinsicStringRefcount(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);
    const ref = try loadI32AsI64(self, managed.value, 16);
    return .{ .value = ref, .ty = .{ .primitive = "int" } };
}

/// __string_is_heap(managed) -> bool
/// Returns true if the string is heap-allocated (cap_flags & 0x1 == 1)
fn intrinsicStringIsHeap(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const cap_flags = try self.func().emitLoad(try self.func().emitGetFieldPtr(managed.value, 12), .i32);
    const one = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.band, cap_flags, one, .i32);

    return .{ .value = is_heap, .ty = .{ .primitive = "bool" } };
}

/// __string_cap_flags(managed) -> int
fn intrinsicStringCapFlags(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);
    const cap = try loadI32AsI64(self, managed.value, 12);
    return .{ .value = cap, .ty = .{ .primitive = "int" } };
}

// ============================================================================
// File I/O Intrinsics (stdlib-only)
// ============================================================================

/// Helper for intrinsics that take a single cstring arg and return an optional pointer
/// Used by __read_file and __list_directory
fn emitCstringToOptional(
    self: *AstToIr,
    call: ast.CallExpr,
    func_name: []const u8,
    wrapped_type: []const u8,
) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    const args = try self.allocator.alloc(ir.Value, 1);
    args[0] = path_ptr;

    const raw_result = try self.func().emitCall(func_name, args, .ptr);
    const result_ptr = raw_result orelse try self.func().emitConstI64(0);

    return wrapInOptional(self, result_ptr, wrapped_type);
}

/// Helper for intrinsics that take a cstring path and return int
/// Used by __is_directory
fn emitCstringToInt(
    self: *AstToIr,
    call: ast.CallExpr,
    func_name: []const u8,
) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    const args = try self.allocator.alloc(ir.Value, 1);
    args[0] = path_ptr;

    const result = try self.func().emitCall(func_name, args, .i64);
    return .{ .value = result orelse try self.func().emitConstI64(0), .ty = .{ .primitive = "int" } };
}

/// Helper for write intrinsics that take path + data buffer and return int
/// Used by __write_file and __write_file_binary
fn emitWriteFile(
    self: *AstToIr,
    path_cstr: TypedValue,
    data_source: TypedValue,
) ConvertError!TypedValue {
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);
    const data_ptr = try self.func().emitLoad(data_source.value, .ptr);
    const data_len = try loadI64Field(self, data_source.value, 8);

    const args = try self.allocator.alloc(ir.Value, 3);
    args[0] = path_ptr;
    args[1] = data_ptr;
    args[2] = data_len;

    const result = try self.func().emitCall("__file_write", args, .i64);
    return .{ .value = result orelse try self.func().emitConstI64(0), .ty = .{ .primitive = "int" } };
}

/// Dispatch __read_file, __write_file*, __list_directory, __is_directory intrinsics
fn convertFileIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    const name = call.func_name;

    if (std.mem.eql(u8, name, "__read_file")) {
        return emitCstringToOptional(self, call, "__file_read", "__ManagedString");
    } else if (std.mem.eql(u8, name, "__write_file") or std.mem.eql(u8, name, "__write_file_binary")) {
        // Both write intrinsics work the same: path + data buffer -> int
        try expectArgCount(self, call, 2);
        const path_cstr = try self.convertExpression(call.args[0]);
        const data = try self.convertExpression(call.args[1]);
        return emitWriteFile(self, path_cstr, data);
    } else if (std.mem.eql(u8, name, "__list_directory")) {
        return emitCstringToOptional(self, call, "__dir_list", "Array$String");
    } else if (std.mem.eql(u8, name, "__is_directory")) {
        return emitCstringToInt(self, call, "__dir_is_directory");
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}
