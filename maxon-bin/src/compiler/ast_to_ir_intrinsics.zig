const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const TypedValue = types.TypedValue;
const ConvertError = types.ConvertError;

// Forward reference to main AstToIr module
const AstToIr = @import("4-ast_to_ir.zig").AstToIr;

// ============================================================================
// Built-in Functions
// ============================================================================

const Builtin = struct {
    name: []const u8,
    op: ir.Instruction.Op,
    arg_type: ir.Type,
    ret_type: ir.Type,
};

const builtins = [_]Builtin{
    .{ .name = "trunc", .op = .fptosi, .arg_type = .f64, .ret_type = .i64 },
    .{ .name = "abs", .op = .fabs, .arg_type = .f64, .ret_type = .f64 },
    .{ .name = "sqrt", .op = .fsqrt, .arg_type = .f64, .ret_type = .f64 },
    .{ .name = "ceil", .op = .fceil, .arg_type = .f64, .ret_type = .i64 },
    .{ .name = "floor", .op = .ffloor, .arg_type = .f64, .ret_type = .i64 },
    .{ .name = "round", .op = .fround, .arg_type = .f64, .ret_type = .i64 },
};

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

    const builtin = for (builtins) |b| {
        if (std.mem.eql(u8, call.func_name, b.name)) break b;
    } else return error.NotABuiltin;

    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const arg = try self.convertExpression(call.args[0]);
    const arg_prim = arg.ty.toPrimitiveType();

    // Get the actual value to use, with implicit int-to-float promotion if needed
    const actual_value = if (arg_prim != builtin.arg_type) blk: {
        // Allow int -> float promotion for builtins expecting float
        if (builtin.arg_type == .f64 and arg_prim == .i64) {
            break :blk self.func().emitUnaryOp(.sitofp, arg.value, .f64) catch return error.OutOfMemory;
        }
        const msg = std.fmt.allocPrint(self.allocator, "{s}() requires {s} argument", .{ call.func_name, builtin.arg_type.toMaxonName() }) catch call.func_name;
        self.reportError(.E022, msg);
        return error.TypeMismatch;
    } else arg.value;

    const result = self.func().emitUnaryOp(builtin.op, actual_value, builtin.ret_type) catch return error.OutOfMemory;

    return .{ .value = result, .ty = .{ .primitive = builtin.ret_type.toMaxonName() } };
}

// ============================================================================
// __ManagedArray Intrinsics (stdlib-only)
// ============================================================================

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
    } else {
        self.reportError(.E019, name);
        return error.SemanticError;
    }
}

/// __managed_array_len(managed) -> int
/// Returns the length field of the __ManagedArray
fn intrinsicManagedArrayLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    // Load _len field (offset 8 in __ManagedArray)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len = try self.func().emitLoad(len_ptr, .i64);

    return .{ .value = len, .ty = .{ .primitive = "int" } };
}

/// __managed_array_capacity(managed) -> int
/// Returns the capacity field of the __ManagedArray
fn intrinsicManagedArrayCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    // Load _capacity field (offset 16 in __ManagedArray)
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const cap = try self.func().emitLoad(cap_ptr, .i64);

    return .{ .value = cap, .ty = .{ .primitive = "int" } };
}

/// __managed_array_create(capacity) -> __ManagedArray
/// Creates a new managed array with the given size/capacity (length = capacity)
/// Used for "Array of N type" syntax where all elements are immediately accessible
/// Returns a pointer to the 24-byte __ManagedArray struct
fn intrinsicManagedArrayCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const capacity = try self.convertExpression(call.args[0]);

    // Allocate the __ManagedArray struct (24 bytes: ptr + len + cap)
    const managed_ptr = try self.func().emitAllocaSized(24);

    // Calculate buffer size: capacity * 8
    const eight = try self.func().emitConstI64(8);
    const buf_size = try self.func().emitBinaryOp(.mul, capacity.value, eight, .i64);

    // Allocate buffer on heap
    const buf_ptr = try self.func().emitHeapAlloc(buf_size);

    // Store buffer pointer at offset 0
    try self.func().emitStore(managed_ptr, buf_ptr);

    // Store length = capacity at offset 8 (for sized arrays, all elements are accessible)
    const len_ptr = try self.func().emitGetFieldPtr(managed_ptr, 8);
    try self.func().emitStore(len_ptr, capacity.value);

    // Store capacity at offset 16
    const cap_ptr = try self.func().emitGetFieldPtr(managed_ptr, 16);
    try self.func().emitStore(cap_ptr, capacity.value);

    return .{ .value = managed_ptr, .ty = .{ .primitive = "ptr" } };
}

/// __managed_array_set_at(managed, index, value) -> void
/// Sets element at index (no bounds checking - caller must verify)
fn intrinsicManagedArraySetAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const value = try self.convertExpression(call.args[2]);

    // Load buffer pointer (offset 0)
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Calculate element address: buf_ptr + index * 8
    const elem_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 8);

    // Store value
    try self.func().emitStore(elem_ptr, value.value);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_set_length(managed, new_len) -> void
/// Sets the length field of the __ManagedArray
fn intrinsicManagedArraySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const new_len = try self.convertExpression(call.args[1]);

    // Store to _len field (offset 8)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    try self.func().emitStore(len_ptr, new_len.value);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_grow(managed, new_capacity) -> void
/// Reallocates buffer to new_capacity (must be > current capacity)
fn intrinsicManagedArrayGrow(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const new_capacity = try self.convertExpression(call.args[1]);

    // Calculate new buffer size: new_capacity * 8
    const eight = try self.func().emitConstI64(8);
    const new_size = try self.func().emitBinaryOp(.mul, new_capacity.value, eight, .i64);

    // Load current buffer pointer
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Realloc: new_buf = heap_realloc(buf_ptr, new_size)
    const new_buf = try self.func().emitHeapRealloc(buf_ptr, new_size);

    // Store new buffer pointer (offset 0)
    try self.func().emitStore(managed.value, new_buf);

    // Store new capacity (offset 16)
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    try self.func().emitStore(cap_ptr, new_capacity.value);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_shift_right(managed, start_index, count) -> void
/// Shifts elements from start_index right by count positions
/// Iterates backwards from end to start to handle overlap correctly
fn intrinsicManagedArrayShiftRight(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);

    // Load buffer and length
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len = try self.func().emitLoad(len_ptr, .i64);

    const eight = try self.func().emitConstI64(8);
    const one = try self.func().emitConstI64(1);

    // Loop from i = len-1 down to start_index, moving each element right by count
    const i_ptr = try self.func().emitAllocaSized(8);
    const init_i = try self.func().emitBinaryOp(.sub, len, one, .i64);
    try self.func().emitStore(i_ptr, init_i);

    // Create loop blocks using helper
    var loop = try self.createLoopBlocks("shift_right_cond", "shift_right_body", "shift_right_end");

    // Condition: i >= start_index (cond block is current)
    const i_val = try self.func().emitLoad(i_ptr, .i64);
    const cond = try self.func().emitBinaryOp(.icmp_ge, i_val, start_index.value, .i64);

    // Emit conditional branch and switch to body block
    try self.emitLoopCondBranch(&loop, cond);

    // Body: arr[i + count] = arr[i]; i--;
    const i_val2 = try self.func().emitLoad(i_ptr, .i64);
    const src_offset = try self.func().emitBinaryOp(.mul, i_val2, eight, .i64);
    const src_ptr = try self.func().emitBinaryOp(.add, buf_ptr, src_offset, .ptr);
    const elem_val = try self.func().emitLoad(src_ptr, .i64);

    const dst_idx = try self.func().emitBinaryOp(.add, i_val2, count.value, .i64);
    const dst_offset = try self.func().emitBinaryOp(.mul, dst_idx, eight, .i64);
    const dst_ptr = try self.func().emitBinaryOp(.add, buf_ptr, dst_offset, .ptr);
    try self.func().emitStore(dst_ptr, elem_val);

    const new_i = try self.func().emitBinaryOp(.sub, i_val2, one, .i64);
    try self.func().emitStore(i_ptr, new_i);
    try self.func().emitBr(loop.cond_block_idx);

    // Finish loop and restore end block
    try self.finishLoop(&loop);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_shift_left(managed, start_index, count) -> void
/// Shifts elements from start_index+count left by count positions
/// Iterates forwards from start to end to handle overlap correctly
fn intrinsicManagedArrayShiftLeft(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);

    // Load buffer and length
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len = try self.func().emitLoad(len_ptr, .i64);

    const eight = try self.func().emitConstI64(8);
    const one = try self.func().emitConstI64(1);

    // Source start index: start_index + count
    const src_start = try self.func().emitBinaryOp(.add, start_index.value, count.value, .i64);

    // Loop from i = 0, while src_start + i < len
    const i_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(i_ptr, zero);

    // Create loop blocks using helper
    var loop = try self.createLoopBlocks("shift_left_cond", "shift_left_body", "shift_left_end");

    // Condition: src_start + i < len (cond block is current)
    const i_val = try self.func().emitLoad(i_ptr, .i64);
    const src_idx = try self.func().emitBinaryOp(.add, src_start, i_val, .i64);
    const cond = try self.func().emitBinaryOp(.icmp_lt, src_idx, len, .i64);

    // Emit conditional branch and switch to body block
    try self.emitLoopCondBranch(&loop, cond);

    // Body: arr[start_index + i] = arr[src_start + i]; i++;
    const i_val2 = try self.func().emitLoad(i_ptr, .i64);
    const src_idx2 = try self.func().emitBinaryOp(.add, src_start, i_val2, .i64);
    const src_offset = try self.func().emitBinaryOp(.mul, src_idx2, eight, .i64);
    const src_ptr = try self.func().emitBinaryOp(.add, buf_ptr, src_offset, .ptr);
    const elem_val = try self.func().emitLoad(src_ptr, .i64);

    const dst_idx = try self.func().emitBinaryOp(.add, start_index.value, i_val2, .i64);
    const dst_offset = try self.func().emitBinaryOp(.mul, dst_idx, eight, .i64);
    const dst_ptr = try self.func().emitBinaryOp(.add, buf_ptr, dst_offset, .ptr);
    try self.func().emitStore(dst_ptr, elem_val);

    const new_i = try self.func().emitBinaryOp(.add, i_val2, one, .i64);
    try self.func().emitStore(i_ptr, new_i);
    try self.func().emitBr(loop.cond_block_idx);

    // Finish loop and restore end block
    try self.finishLoop(&loop);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
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
    } else if (std.mem.eql(u8, name, "__string_decref")) {
        return intrinsicStringDecref(self, call);
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
/// Writes the cstring to stdout, returns bytes written
fn intrinsicCstringWriteStdout(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const cstring = try self.convertExpression(call.args[0]);

    // Load data pointer from cstring (offset 0)
    const data_ptr = try self.func().emitLoad(cstring.value, .ptr);

    // Load length from cstring (offset 8)
    const len_field = try self.func().emitGetFieldPtr(cstring.value, 8);
    const length = try self.func().emitLoad(len_field, .i64);

    // Call __write_stdout(buffer, length) -> bytes_written
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
/// Creates a Character by extracting bytes from a __ManagedString
/// Creates a slice-mode __ManagedString for the Character's _managed field
/// Layout is identical to __string_slice but returns Character type
fn intrinsicMakeCharFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const pos = try self.convertExpression(call.args[1]);
    const len = try self.convertExpression(call.args[2]);

    // Allocate Character struct (24 bytes - same as __ManagedString)
    const char_ptr = try self.func().emitAllocaSized(24);

    // Load buffer pointer from parent managed string
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Calculate slice buffer = parent_buf_ptr + pos (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, pos.value, .ptr);
    try self.func().emitStore(char_ptr, slice_buf);

    // Store len (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len.value, .i32);
    const len_field = try self.func().emitGetFieldPtr(char_ptr, 8);
    try self.func().emitStoreI32(len_field, len_i32);

    // Set cap_flags = 0b10 (slice mode) (offset 12, i32)
    const two = try self.func().emitConstI32(2);
    const cap_field = try self.func().emitGetFieldPtr(char_ptr, 12);
    try self.func().emitStoreI32(cap_field, two);

    // Set refcount = 0 (unused for slices) (offset 16, i32)
    const zero_i32 = try self.func().emitConstI32(0);
    const ref_field = try self.func().emitGetFieldPtr(char_ptr, 16);
    try self.func().emitStoreI32(ref_field, zero_i32);

    // Store parent_off = pos (byte offset into parent buffer) (offset 20, i32)
    const pos_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, pos.value, .i32);
    const off_field = try self.func().emitGetFieldPtr(char_ptr, 20);
    try self.func().emitStoreI32(off_field, pos_i32);

    return .{
        .value = char_ptr,
        .ty = .{ .struct_type = "Character" },
    };
}

/// __string_len(managed) -> int
/// Returns the byte length of the __ManagedString (offset 8, i32)
fn intrinsicStringLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    // Load _len field (offset 8 in __ManagedString, 4 bytes)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len_i32 = try self.func().emitLoad(len_ptr, .i32);
    // Sign-extend to i64 for Maxon int type
    const len = try self.func().emitUnaryOp(.sext_i32_i64, len_i32, .i64);

    return .{ .value = len, .ty = .{ .primitive = "int" } };
}

/// __string_byte_at(managed, index) -> byte
/// Returns the byte at the given index in the string buffer
fn intrinsicStringByteAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    // Load buffer pointer (offset 0)
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Calculate byte address using getelemptr (handles pointer arithmetic properly)
    const byte_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 1);

    // Load single byte
    const byte_val = try self.func().emitLoad(byte_ptr, .i8);

    return .{ .value = byte_val, .ty = .{ .primitive = "byte" } };
}

/// __string_slice(managed, start, end) -> __ManagedString (slice mode)
/// Creates a slice view into the managed string
/// Returns a 24-byte __ManagedString with cap_flags = 0b10 (slice mode)
/// Layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
/// For slice mode:
///   - buffer points into parent's buffer at slice start
///   - len is the slice byte length
///   - cap_flags = 0b10 (slice mode indicator)
///   - refcount = 0 (unused for slices)
///   - parent_off = byte offset from parent buffer start
fn intrinsicStringSlice(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    // Track borrow source for compile-time borrow checking
    // The first arg is a __ManagedString, typically accessed via _managed field
    // We trace back to find the String variable being borrowed
    if (call.args[0] == .field_access) {
        const fa = call.args[0].field_access;
        if (fa.base.* == .identifier) {
            // Field access on a variable: str._managed -> borrow from "str"
            self.pending_borrow_source = fa.base.identifier;
        } else if (fa.base.* == .self_expr) {
            // Field access on self: self._managed (or just _managed) -> borrow from caller's variable
            // This is inside a method, so we'll track via "self" context
            // The actual caller variable is tracked when method returns
            self.pending_borrow_source = "self";
        }
    } else if (call.args[0] == .identifier) {
        // Direct variable access (unlikely but handle it)
        self.pending_borrow_source = call.args[0].identifier;
    }

    const managed = try self.convertExpression(call.args[0]);
    const start = try self.convertExpression(call.args[1]);
    const end = try self.convertExpression(call.args[2]);

    // Allocate slice __ManagedString struct (24 bytes)
    const slice_ptr = try self.func().emitAllocaSized(24);

    // Load buffer pointer from parent managed string
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Calculate slice buffer = parent_buf_ptr + start (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, start.value, .ptr);
    try self.func().emitStore(slice_ptr, slice_buf);

    // Calculate len = end - start (offset 8, i32)
    const len_i64 = try self.func().emitBinaryOp(.sub, end.value, start.value, .i64);
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len_i64, .i32);
    const len_field = try self.func().emitGetFieldPtr(slice_ptr, 8);
    try self.func().emitStoreI32(len_field, len_i32);

    // Set cap_flags = 0b10 (slice mode) (offset 12, i32)
    const two = try self.func().emitConstI32(2);
    const cap_field = try self.func().emitGetFieldPtr(slice_ptr, 12);
    try self.func().emitStoreI32(cap_field, two);

    // Set refcount = 0 (unused for slices) (offset 16, i32)
    const zero_i32 = try self.func().emitConstI32(0);
    const ref_field = try self.func().emitGetFieldPtr(slice_ptr, 16);
    try self.func().emitStoreI32(ref_field, zero_i32);

    // Store parent_off = start (byte offset into parent buffer) (offset 20, i32)
    const start_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, start.value, .i32);
    const off_field = try self.func().emitGetFieldPtr(slice_ptr, 20);
    try self.func().emitStoreI32(off_field, start_i32);

    return .{ .value = slice_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_concat(a, b) -> __ManagedString
/// Concatenates two managed strings into a new heap-allocated string
/// New layout (24 bytes): buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
fn intrinsicStringConcat(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const a = try self.convertExpression(call.args[0]);
    const b = try self.convertExpression(call.args[1]);

    // IMPORTANT: Load buffer pointers BEFORE allocating new struct
    // In loops, alloca.sized reuses the same stack location, so we must read
    // from the old struct before potentially overwriting it when a == result
    const a_buf = try self.func().emitLoad(a.value, .ptr);
    const b_buf = try self.func().emitLoad(b.value, .ptr);

    // Load lengths (offset 8, i32)
    const a_len_ptr = try self.func().emitGetFieldPtr(a.value, 8);
    const a_len_i32 = try self.func().emitLoad(a_len_ptr, .i32);
    const a_len = try self.func().emitUnaryOp(.sext_i32_i64, a_len_i32, .i64);

    const b_len_ptr = try self.func().emitGetFieldPtr(b.value, 8);
    const b_len_i32 = try self.func().emitLoad(b_len_ptr, .i32);
    const b_len = try self.func().emitUnaryOp(.sext_i32_i64, b_len_i32, .i64);

    // Calculate total length
    const total_len = try self.func().emitBinaryOp(.add, a_len, b_len, .i64);

    // Allocate new __ManagedString struct (24 bytes)
    const result_ptr = try self.func().emitAllocaSized(24);

    // Allocate buffer on heap: total_len bytes
    const buf_ptr = try self.func().emitHeapAlloc(total_len);

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, buf_ptr);

    // Store length (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, total_len, .i32);
    const len_field = try self.func().emitGetFieldPtr(result_ptr, 8);
    try self.func().emitStoreI32(len_field, len_i32);

    // Store cap_flags = (capacity * 4) | 0b01 for heap mode (offset 12, i32)
    // Using multiply by 4 instead of shift left by 2
    const four = try self.func().emitConstI32(4);
    const cap_shifted = try self.func().emitBinaryOp(.mul, len_i32, four, .i32);
    const one = try self.func().emitConstI32(1);
    const cap_flags = try self.func().emitBinaryOp(.bitor, cap_shifted, one, .i32);
    const cap_field = try self.func().emitGetFieldPtr(result_ptr, 12);
    try self.func().emitStoreI32(cap_field, cap_flags);

    // Store refcount = 1 (offset 16, i32)
    const ref_field = try self.func().emitGetFieldPtr(result_ptr, 16);
    try self.func().emitStoreI32(ref_field, one);

    // Store parent_off = 0 (offset 20, i32)
    const zero = try self.func().emitConstI32(0);
    const off_field = try self.func().emitGetFieldPtr(result_ptr, 20);
    try self.func().emitStoreI32(off_field, zero);

    // Copy first string: memcpy(buf_ptr, a.buffer, a_len)
    // Note: a_buf was loaded before alloca.sized to avoid aliasing issues in loops
    try self.func().emitMemcpyDynamic(buf_ptr, a_buf, a_len);

    // Copy second string: memcpy(buf_ptr + a_len, b.buffer, b_len)
    // Note: b_buf was loaded before alloca.sized to avoid aliasing issues in loops
    const dst_offset = try self.func().emitBinaryOp(.add, buf_ptr, a_len, .ptr);
    try self.func().emitMemcpyDynamic(dst_offset, b_buf, b_len);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_make_unique(managed) -> __ManagedString
/// Creates a mutable copy of the string (COW - copy on write)
/// Always creates a new independent copy with refcount = 1
/// New layout (24 bytes): buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4)
fn intrinsicStringMakeUnique(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load length (offset 8, i32)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len_i32 = try self.func().emitLoad(len_ptr, .i32);
    const len = try self.func().emitUnaryOp(.sext_i32_i64, len_i32, .i64);

    // Allocate new __ManagedString struct (24 bytes)
    const result_ptr = try self.func().emitAllocaSized(24);

    // Allocate new buffer on heap
    const new_buf = try self.func().emitHeapAlloc(len);

    // Copy data from original buffer
    const orig_buf = try self.func().emitLoad(managed.value, .ptr);
    try self.func().emitMemcpyDynamic(new_buf, orig_buf, len);

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, new_buf);

    // Store length (offset 8, i32)
    const new_len_field = try self.func().emitGetFieldPtr(result_ptr, 8);
    try self.func().emitStoreI32(new_len_field, len_i32);

    // Store cap_flags = (capacity * 4) | 0b01 for heap mode (offset 12, i32)
    // Using multiply by 4 instead of shift left by 2
    const four = try self.func().emitConstI32(4);
    const cap_shifted = try self.func().emitBinaryOp(.mul, len_i32, four, .i32);
    const one = try self.func().emitConstI32(1);
    const cap_flags = try self.func().emitBinaryOp(.bitor, cap_shifted, one, .i32);
    const cap_field = try self.func().emitGetFieldPtr(result_ptr, 12);
    try self.func().emitStoreI32(cap_field, cap_flags);

    // Store refcount = 1 (offset 16, i32)
    const ref_field = try self.func().emitGetFieldPtr(result_ptr, 16);
    try self.func().emitStoreI32(ref_field, one);

    // Store parent_off = 0 (offset 20, i32)
    const zero = try self.func().emitConstI32(0);
    const off_field = try self.func().emitGetFieldPtr(result_ptr, 20);
    try self.func().emitStoreI32(off_field, zero);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

/// __string_set_byte(managed, index, byte) -> void
/// Sets a byte at the given index (caller must ensure string is unique)
fn intrinsicStringSetByte(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const byte_val = try self.convertExpression(call.args[2]);

    // Load buffer pointer (offset 0)
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Calculate byte address using getelemptr (handles pointer arithmetic properly)
    const byte_ptr = try self.func().emitGetElemPtr(buf_ptr, index.value, 1);

    // Truncate byte value from i64 to i8 and store
    const byte_i8 = try self.func().emitUnaryOp(.trunc_i64_i8, byte_val.value, .i8);
    try self.func().emitStoreI8(byte_ptr, byte_i8);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __string_to_cstring(managed) -> cstring struct
/// Creates a cstring struct from a __ManagedString.
/// For SSO/Heap mode: shares buffer (already null-terminated), holds reference to managed string
/// For Slice mode: copies data to new null-terminated buffer (cstring owns its buffer)
fn intrinsicStringToCstring(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Allocate cstring struct on stack (24 bytes: data + length + managed)
    const cstring_ptr = try self.func().emitAllocaSized(24);

    // Load cap_flags (offset 12) to determine mode
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 12);
    const cap_flags = try self.func().emitLoad(cap_ptr, .i32);

    // Check if slice mode: (cap_flags & 0x3) == 2
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, cap_flags, three, .i32);
    const two = try self.func().emitConstI32(2);
    const is_slice = try self.func().emitBinaryOp(.icmp_eq, mode, two, .i32);

    // Create all blocks upfront without popping - add them in execution order
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const slice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_slice");

    // We need to defer emitting the slice block code until after we set up the branch
    // For now, let's use a simpler approach: just build everything linearly

    // First, emit the conditional branch to decide slice vs nonslice
    // We'll patch the target indices after creating all blocks

    // === SLICE BLOCK: Copy data to new null-terminated buffer ===
    // Load source buffer pointer (offset 0)
    const slice_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Load length (offset 8, i32) and extend to i64
    const slice_len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const slice_len_i32 = try self.func().emitLoad(slice_len_ptr, .i32);
    const slice_len = try self.func().emitUnaryOp(.sext_i32_i64, slice_len_i32, .i64);

    // Allocate new buffer of size (length + 1) for null terminator
    const one_i64 = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, slice_len, one_i64, .i64);
    const new_buf = try self.func().emitHeapAlloc(alloc_size);

    // memcpy from slice buffer to new buffer
    try self.func().emitMemcpyDynamic(new_buf, slice_buf_ptr, slice_len);

    // Write null terminator at new_buf[length]
    const null_pos = try self.func().emitBinaryOp(.add, new_buf, slice_len, .ptr);
    const zero_byte = try self.func().emitConstI64(0);
    try self.func().emitStoreI8(null_pos, zero_byte);

    // Store into cstring struct
    // cstring.data = new_buf (offset 0)
    try self.func().emitStore(cstring_ptr, new_buf);
    // cstring.length = slice_len (offset 8)
    const cstr_len_field = try self.func().emitGetFieldPtr(cstring_ptr, 8);
    try self.func().emitStore(cstr_len_field, slice_len);
    // cstring.managed = null (offset 16) - cstring owns its buffer
    const cstr_managed_field = try self.func().emitGetFieldPtr(cstring_ptr, 16);
    const null_ptr = try self.func().emitConstI64(0);
    try self.func().emitStore(cstr_managed_field, null_ptr);

    // Create nonslice block
    const nonslice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_nonslice");

    // === NON-SLICE BLOCK: Share existing buffer ===
    // Load buffer pointer (offset 0) - already null-terminated
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Load length (offset 8, i32) and extend to i64
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len_i32 = try self.func().emitLoad(len_ptr, .i32);
    const length = try self.func().emitUnaryOp(.sext_i32_i64, len_i32, .i64);

    // Store into cstring struct
    // cstring.data = buf_ptr (offset 0)
    try self.func().emitStore(cstring_ptr, buf_ptr);
    // cstring.length = length (offset 8)
    const ns_len_field = try self.func().emitGetFieldPtr(cstring_ptr, 8);
    try self.func().emitStore(ns_len_field, length);
    // cstring.managed = managed pointer (offset 16)
    const ns_managed_field = try self.func().emitGetFieldPtr(cstring_ptr, 16);
    try self.func().emitStore(ns_managed_field, managed.value);

    // Incref the managed string if heap mode: (cap_flags & 0x3) == 1
    const one_i32 = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

    // Create incref block
    const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_incref");

    // In incref block: increment refcount (offset 16 in __ManagedString)
    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const old_ref = try self.func().emitLoad(ref_ptr, .i32);
    const one_ref = try self.func().emitConstI32(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one_ref, .i32);
    try self.func().emitStoreI32(ref_ptr, new_ref);

    // Create end block
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_end");

    // Now go back and add the branch instructions to the right blocks

    // Entry block: branch to slice or nonslice based on is_slice
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_slice }, .{ .block_ref = slice_block_idx }, .{ .block_ref = nonslice_block_idx } },
    });

    // Slice block ends with branch to end
    try self.func().blocks.items[slice_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });

    // Nonslice block ends with branch to incref or end based on is_heap
    try self.func().blocks.items[nonslice_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_heap }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // Incref block ends with branch to end
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
    if (call.args.len != 2) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const buffer = try self.convertExpression(call.args[0]);
    const length = try self.convertExpression(call.args[1]);

    // Allocate new __ManagedString struct (24 bytes)
    const result_ptr = try self.func().emitAllocaSized(24);

    // Allocate buffer on heap
    const new_buf = try self.func().emitHeapAlloc(length.value);

    // Copy data from source buffer (buffer is an array, load its data pointer)
    const src_buf = try self.func().emitLoad(buffer.value, .ptr);
    try self.func().emitMemcpyDynamic(new_buf, src_buf, length.value);

    // Store buffer pointer (offset 0)
    try self.func().emitStore(result_ptr, new_buf);

    // Store length (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, length.value, .i32);
    const len_field = try self.func().emitGetFieldPtr(result_ptr, 8);
    try self.func().emitStoreI32(len_field, len_i32);

    // Store cap_flags = (capacity * 4) | 0b01 for heap mode (offset 12, i32)
    // Using multiply by 4 instead of shift left by 2
    const four = try self.func().emitConstI32(4);
    const cap_shifted = try self.func().emitBinaryOp(.mul, len_i32, four, .i32);
    const one = try self.func().emitConstI32(1);
    const cap_flags = try self.func().emitBinaryOp(.bitor, cap_shifted, one, .i32);
    const cap_field = try self.func().emitGetFieldPtr(result_ptr, 12);
    try self.func().emitStoreI32(cap_field, cap_flags);

    // Store refcount = 1 (offset 16, i32)
    const ref_field = try self.func().emitGetFieldPtr(result_ptr, 16);
    try self.func().emitStoreI32(ref_field, one);

    // Store parent_off = 0 (offset 20, i32)
    const zero = try self.func().emitConstI32(0);
    const off_field = try self.func().emitGetFieldPtr(result_ptr, 20);
    try self.func().emitStoreI32(off_field, zero);

    return .{ .value = result_ptr, .ty = .{ .primitive = "__ManagedString" } };
}

// ============================================================================
// Reference Counting Intrinsics for COW Strings
// ============================================================================

/// __string_incref(managed) -> void
/// Increments the reference count for heap-allocated strings
/// Does nothing for SSO or constant strings
fn intrinsicStringIncref(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load refcount (offset 16, i32) and increment
    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const old_ref = try self.func().emitLoad(ref_ptr, .i32);
    const one = try self.func().emitConstI32(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i32);
    try self.func().emitStoreI32(ref_ptr, new_ref);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __string_decref(managed) -> void
/// Decrements the reference count for heap-allocated strings
/// Frees the buffer when refcount reaches 0
/// Does nothing for SSO or constant strings (cap_flags & 0x1 == 0)
fn intrinsicStringDecref(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load refcount (offset 16, i32) and decrement
    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const old_ref = try self.func().emitLoad(ref_ptr, .i32);
    const one = try self.func().emitConstI32(1);
    const new_ref = try self.func().emitBinaryOp(.sub, old_ref, one, .i32);
    try self.func().emitStoreI32(ref_ptr, new_ref);

    // TODO: Add conditional free when refcount reaches 0
    // For now, this just decrements - actual free handled by freeHeapVars

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __string_refcount(managed) -> int
/// Returns the current reference count
fn intrinsicStringRefcount(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load refcount (offset 16, i32)
    const ref_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const ref_i32 = try self.func().emitLoad(ref_ptr, .i32);
    const ref = try self.func().emitUnaryOp(.sext_i32_i64, ref_i32, .i64);

    return .{ .value = ref, .ty = .{ .primitive = "int" } };
}

/// __string_is_heap(managed) -> bool
/// Returns true if the string is heap-allocated (cap_flags & 0x1 == 1)
fn intrinsicStringIsHeap(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load cap_flags (offset 12, i32)
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 12);
    const cap_flags = try self.func().emitLoad(cap_ptr, .i32);

    // Check if bit 0 is set (heap mode)
    const one = try self.func().emitConstI32(1);
    const is_heap = try self.func().emitBinaryOp(.band, cap_flags, one, .i32);

    return .{ .value = is_heap, .ty = .{ .primitive = "bool" } };
}

/// __string_cap_flags(managed) -> int
/// Returns the cap_flags field (for debugging/introspection)
fn intrinsicStringCapFlags(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);

    // Load cap_flags (offset 12, i32)
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 12);
    const cap_i32 = try self.func().emitLoad(cap_ptr, .i32);
    const cap = try self.func().emitUnaryOp(.sext_i32_i64, cap_i32, .i64);

    return .{ .value = cap, .ty = .{ .primitive = "int" } };
}
