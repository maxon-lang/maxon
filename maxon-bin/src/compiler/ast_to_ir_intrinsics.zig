const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const TypedValue = types.TypedValue;
const PrimitiveInfo = types.PrimitiveInfo;
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
        self.reportErrorWithDetails(.E018, type_name);
        return error.SemanticError;
    }
}

pub fn convertBuiltin(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    // Check for __managed_array_* intrinsics (stdlib-only)
    if (std.mem.startsWith(u8, call.func_name, "__managed_array_")) {
        if (!isStdlibFile(self)) {
            self.reportErrorWithDetails(.E016, call.func_name);
            return error.SemanticError;
        }
        return convertManagedArrayIntrinsic(self, call);
    }

    const builtin = for (builtins) |b| {
        if (std.mem.eql(u8, call.func_name, b.name)) break b;
    } else return error.NotABuiltin;

    if (call.args.len != 1) {
        self.reportError(.E011);
        return error.WrongArgumentCount;
    }

    const arg = try self.convertExpression(call.args[0]);
    if (arg.ty.toPrimitiveType() != builtin.arg_type) {
        self.reportError(.E011);
        return error.TypeMismatch;
    }

    const result = self.func().emitUnaryOp(builtin.op, arg.value, builtin.ret_type) catch return error.OutOfMemory;

    return .{ .value = result, .ty = .{ .primitive = PrimitiveInfo.fromIrType(builtin.ret_type) } };
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
        self.reportErrorWithDetails(.E016, name);
        return error.SemanticError;
    }
}

/// __managed_array_len(managed) -> int
/// Returns the length field of the __ManagedArray
fn intrinsicManagedArrayLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    // Load _len field (offset 8 in __ManagedArray)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    const len = try self.func().emitLoad(len_ptr, .i64);

    return .{ .value = len, .ty = .{ .primitive = .{ .ir_type = .i64, .name = "int" } } };
}

/// __managed_array_capacity(managed) -> int
/// Returns the capacity field of the __ManagedArray
fn intrinsicManagedArrayCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    // Load _capacity field (offset 16 in __ManagedArray)
    const cap_ptr = try self.func().emitGetFieldPtr(managed.value, 16);
    const cap = try self.func().emitLoad(cap_ptr, .i64);

    return .{ .value = cap, .ty = .{ .primitive = .{ .ir_type = .i64, .name = "int" } } };
}

/// __managed_array_create(capacity) -> __ManagedArray
/// Creates a new managed array with the given size/capacity (length = capacity)
/// Used for "Array of N type" syntax where all elements are immediately accessible
/// Returns a pointer to the 24-byte __ManagedArray struct
fn intrinsicManagedArrayCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 1) {
        self.reportError(.E011);
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

    return .{ .value = managed_ptr, .ty = .{ .primitive = .{ .ir_type = .ptr, .name = "ptr" } } };
}

/// __managed_array_set_at(managed, index, value) -> void
/// Sets element at index (no bounds checking - caller must verify)
fn intrinsicManagedArraySetAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011);
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

    return .{ .value = 0, .ty = .{ .primitive = .{ .ir_type = .void, .name = "void" } } };
}

/// __managed_array_set_length(managed, new_len) -> void
/// Sets the length field of the __ManagedArray
fn intrinsicManagedArraySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011);
        return error.WrongArgumentCount;
    }

    const managed = try self.convertExpression(call.args[0]);
    const new_len = try self.convertExpression(call.args[1]);

    // Store to _len field (offset 8)
    const len_ptr = try self.func().emitGetFieldPtr(managed.value, 8);
    try self.func().emitStore(len_ptr, new_len.value);

    return .{ .value = 0, .ty = .{ .primitive = .{ .ir_type = .void, .name = "void" } } };
}

/// __managed_array_grow(managed, new_capacity) -> void
/// Reallocates buffer to new_capacity (must be > current capacity)
fn intrinsicManagedArrayGrow(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 2) {
        self.reportError(.E011);
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

    return .{ .value = 0, .ty = .{ .primitive = .{ .ir_type = .void, .name = "void" } } };
}

/// __managed_array_shift_right(managed, start_index, count) -> void
/// Shifts elements from start_index right by count positions
/// Iterates backwards from end to start to handle overlap correctly
fn intrinsicManagedArrayShiftRight(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011);
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

    return .{ .value = 0, .ty = .{ .primitive = .{ .ir_type = .void, .name = "void" } } };
}

/// __managed_array_shift_left(managed, start_index, count) -> void
/// Shifts elements from start_index+count left by count positions
/// Iterates forwards from start to end to handle overlap correctly
fn intrinsicManagedArrayShiftLeft(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    if (call.args.len != 3) {
        self.reportError(.E011);
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

    return .{ .value = 0, .ty = .{ .primitive = .{ .ir_type = .void, .name = "void" } } };
}
