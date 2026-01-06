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

/// Common pattern for intrinsics that load a single i64 field and return int
fn intrinsicLoadI64Field(self: *AstToIr, call: ast.CallExpr, offset: i32) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const arg = try self.convertExpression(call.args[0]);
    const value = try loadI64Field(self, arg.value, offset);
    return .{ .value = value, .ty = .{ .primitive = "int" } };
}

/// Common pattern for intrinsics that load a single i32 field (sign-extended) and return int
fn intrinsicLoadI32Field(self: *AstToIr, call: ast.CallExpr, offset: i32) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const arg = try self.convertExpression(call.args[0]);
    const value = try loadI32AsI64(self, arg.value, offset);
    return .{ .value = value, .ty = .{ .primitive = "int" } };
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

/// Initialize a heap-allocated string struct
/// Layout: buffer(8) + len(4) + cap_flags(4) + refcount(4) + parent_off(4) [+ _iterPos(8) if include_iter_pos]
/// cap_flags = alloc_size | 0x01 for heap mode
fn initHeapStringCore(self: *AstToIr, string_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value, alloc_size_i64: ir.Value, include_iter_pos: bool) ConvertError!void {
    // Store buffer pointer (offset 0)
    try self.func().emitStore(string_ptr, buffer);

    // Store len (offset 8, i32)
    const len_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, len_i64, .i32);
    try storeI32Field(self, string_ptr, 8, len_i32);

    // Store cap_flags = alloc_size | 0x01 (offset 12, i32)
    const one_i64 = try self.func().emitConstI64(1);
    const cap_flags_i64 = try self.func().emitBinaryOp(.bitor, alloc_size_i64, one_i64, .i64);
    const cap_flags_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, cap_flags_i64, .i32);
    try storeI32Field(self, string_ptr, 12, cap_flags_i32);

    // Store refcount = 1 (offset 16, i32)
    const one_i32 = try self.func().emitConstI32(1);
    try storeI32Field(self, string_ptr, 16, one_i32);

    // Store parent_off = 0 (offset 20, i32)
    const zero_i32 = try self.func().emitConstI32(0);
    try storeI32Field(self, string_ptr, 20, zero_i32);

    // Store _iterPos = 0 (offset 24, i64) for String (not __ManagedString)
    if (include_iter_pos) {
        const zero_i64 = try self.func().emitConstI64(0);
        try storeI64Field(self, string_ptr, 24, zero_i64);
    }
}

/// Initialize a heap-allocated String struct (32 bytes with _iterPos field)
fn initHeapString(self: *AstToIr, string_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value, alloc_size_i64: ir.Value) ConvertError!void {
    return initHeapStringCore(self, string_ptr, buffer, len_i64, alloc_size_i64, true);
}

/// Initialize a heap-allocated __ManagedString struct (24 bytes, no _iterPos)
fn initHeapManagedString(self: *AstToIr, string_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value, alloc_size_i64: ir.Value) ConvertError!void {
    return initHeapStringCore(self, string_ptr, buffer, len_i64, alloc_size_i64, false);
}

/// Wrap a nullable pointer result in an optional type
/// Layout: [tag: 8 bytes][ptr: 8 bytes] - stores pointer, not inline data
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
// Windows API Helpers (for intrinsics that need direct DLL calls)
// ============================================================================

/// Generic helper for extern calls that return a value
fn externCall(self: *AstToIr, dll: []const u8, func_name: []const u8, args: []const ir.Value, ret_type: ir.Type) ConvertError!ir.Value {
    const args_copy = try self.allocator.alloc(ir.Value, args.len);
    @memcpy(args_copy, args);
    const result = try self.func().emitExternCall(dll, func_name, args_copy, ret_type);
    return result orelse try self.func().emitConstI64(0);
}

/// Generic helper for extern calls that return void
fn externCallVoid(self: *AstToIr, dll: []const u8, func_name: []const u8, args: []const ir.Value, ret_type: ir.Type) ConvertError!void {
    const args_copy = try self.allocator.alloc(ir.Value, args.len);
    @memcpy(args_copy, args);
    _ = try self.func().emitExternCall(dll, func_name, args_copy, ret_type);
}

fn emitGetProcessHeap(self: *AstToIr) ConvertError!ir.Value {
    return externCall(self, "kernel32.dll", "GetProcessHeap", &.{}, .ptr);
}

fn emitHeapAllocWin(self: *AstToIr, heap: ir.Value, size: ir.Value) ConvertError!ir.Value {
    const zero = try self.func().emitConstI64(0);
    return externCall(self, "kernel32.dll", "HeapAlloc", &.{ heap, zero, size }, .ptr);
}

fn emitGetCommandLineW(self: *AstToIr) ConvertError!ir.Value {
    return externCall(self, "kernel32.dll", "GetCommandLineW", &.{}, .ptr);
}

fn emitCommandLineToArgvW(self: *AstToIr, cmdline: ir.Value, argc_ptr: ir.Value) ConvertError!ir.Value {
    return externCall(self, "shell32.dll", "CommandLineToArgvW", &.{ cmdline, argc_ptr }, .ptr);
}

fn emitWideCharToMultiByte(self: *AstToIr, src: ir.Value, dest: ir.Value, dest_size: ir.Value) ConvertError!ir.Value {
    const cp_utf8 = try self.func().emitConstI64(65001);
    const zero = try self.func().emitConstI64(0);
    const neg_one = try self.func().emitConstI64(@bitCast(@as(i64, -1)));
    return externCall(self, "kernel32.dll", "WideCharToMultiByte", &.{ cp_utf8, zero, src, neg_one, dest, dest_size, zero, zero }, .i64);
}

fn emitLocalFree(self: *AstToIr, ptr: ir.Value) ConvertError!void {
    return externCallVoid(self, "kernel32.dll", "LocalFree", &.{ptr}, .ptr);
}

fn emitCreateFileA(self: *AstToIr, path: ir.Value, access: u32, share_mode: u32, disposition: u32, flags: u32) ConvertError!ir.Value {
    const zero = try self.func().emitConstI64(0);
    return externCall(self, "kernel32.dll", "CreateFileA", &.{
        path,
        try self.func().emitConstI64(access),
        try self.func().emitConstI64(share_mode),
        zero, // lpSecurityAttributes = NULL
        try self.func().emitConstI64(disposition),
        try self.func().emitConstI64(flags),
        zero, // hTemplateFile = NULL
    }, .ptr);
}

fn emitGetFileSize(self: *AstToIr, handle: ir.Value) ConvertError!ir.Value {
    const zero = try self.func().emitConstI64(0);
    return externCall(self, "kernel32.dll", "GetFileSize", &.{ handle, zero }, .i64);
}

fn emitReadFile(self: *AstToIr, handle: ir.Value, buffer: ir.Value, size: ir.Value, bytes_read_ptr: ir.Value) ConvertError!ir.Value {
    const zero = try self.func().emitConstI64(0);
    return externCall(self, "kernel32.dll", "ReadFile", &.{ handle, buffer, size, bytes_read_ptr, zero }, .i64);
}

fn emitWriteFileWin(self: *AstToIr, handle: ir.Value, buffer: ir.Value, size: ir.Value, bytes_written_ptr: ir.Value) ConvertError!ir.Value {
    const zero = try self.func().emitConstI64(0);
    return externCall(self, "kernel32.dll", "WriteFile", &.{ handle, buffer, size, bytes_written_ptr, zero }, .i64);
}

fn emitCloseHandle(self: *AstToIr, handle: ir.Value) ConvertError!void {
    return externCallVoid(self, "kernel32.dll", "CloseHandle", &.{handle}, .i64);
}

fn emitStrlen(self: *AstToIr, str: ir.Value) ConvertError!ir.Value {
    return externCall(self, "ntdll.dll", "strlen", &.{str}, .i64);
}

fn emitMemcpyWin(self: *AstToIr, dest: ir.Value, src: ir.Value, size: ir.Value) ConvertError!void {
    return externCallVoid(self, "ntdll.dll", "memcpy", &.{ dest, src, size }, .ptr);
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
    // Check category-based intrinsics first (prefix matching)
    if (intrinsics_registry.findCategory(call.func_name)) |category| {
        // Enforce visibility
        if (category.visibility == .stdlib_only and !isStdlibFile(self)) {
            self.reportError(.E016, call.func_name);
            return error.SemanticError;
        }

        // Dispatch based on codegen strategy
        return switch (category.codegen) {
            .managed_array => convertManagedArrayIntrinsic(self, call),
            .string => convertStringIntrinsic(self, call),
            .cstring => convertCstringIntrinsic(self, call),
            .make_char => convertMakeCharIntrinsic(self, call),
            .file_io => convertFileIntrinsic(self, call),
            .unary_op, .custom => unreachable, // These use individual lookup below
        };
    }

    // Look up individual intrinsics (math builtins)
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

const IntrinsicHandler = *const fn (*AstToIr, ast.CallExpr) ConvertError!TypedValue;

fn dispatchIntrinsic(self: *AstToIr, call: ast.CallExpr, comptime table: anytype) ConvertError!TypedValue {
    inline for (table) |entry| {
        if (std.mem.eql(u8, call.func_name, entry[0])) {
            return entry[1](self, call);
        }
    }
    self.reportError(.E019, call.func_name);
    return error.SemanticError;
}

const managed_array_intrinsics = .{
    .{ "__managed_array_len", intrinsicManagedArrayLen },
    .{ "__managed_array_capacity", intrinsicManagedArrayCapacity },
    .{ "__managed_array_create", intrinsicManagedArrayCreate },
    .{ "__managed_array_set_at", intrinsicManagedArraySetAt },
    .{ "__managed_array_set_length", intrinsicManagedArraySetLength },
    .{ "__managed_array_grow", intrinsicManagedArrayGrow },
    .{ "__managed_array_shift_right", intrinsicManagedArrayShiftRight },
    .{ "__managed_array_shift_left", intrinsicManagedArrayShiftLeft },
    .{ "__managed_array_get_unchecked", intrinsicManagedArrayGetUnchecked },
};

fn convertManagedArrayIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, managed_array_intrinsics);
}

/// __managed_array_len(managed) -> int
fn intrinsicManagedArrayLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return intrinsicLoadI64Field(self, call, 8);
}

/// __managed_array_capacity(managed) -> int
fn intrinsicManagedArrayCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return intrinsicLoadI64Field(self, call, 16);
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

const string_intrinsics = .{
    .{ "__string_len", intrinsicStringLen },
    .{ "__string_byte_at", intrinsicStringByteAt },
    .{ "__string_slice", intrinsicStringSlice },
    .{ "__string_concat", intrinsicStringConcat },
    .{ "__string_make_unique", intrinsicStringMakeUnique },
    .{ "__string_set_byte", intrinsicStringSetByte },
    .{ "__string_to_cstring", intrinsicStringToCstring },
    .{ "__string_from_characters", intrinsicStringFromCharacters },
    .{ "__string_incref", intrinsicStringIncref },
    .{ "__string_refcount", intrinsicStringRefcount },
    .{ "__string_is_heap", intrinsicStringIsHeap },
    .{ "__string_cap_flags", intrinsicStringCapFlags },
};

fn convertStringIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, string_intrinsics);
}

const cstring_intrinsics = .{
    .{ "__cstring_write_stdout", intrinsicCstringWriteStdout },
    .{ "__cstring_to_managed", intrinsicCstringToManaged },
};

fn convertCstringIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, cstring_intrinsics);
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

/// __cstring_to_managed(cstr) -> __ManagedString
/// Converts a cstring struct to a __ManagedString
/// The string data is copied to a new heap buffer
/// cstring struct layout: data(8) + length(8) + managed(8)
fn intrinsicCstringToManaged(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const cstr_typed = try self.convertExpression(call.args[0]);

    // Extract data pointer from cstring struct (offset 0)
    const src_ptr = try self.func().emitLoad(cstr_typed.value, .ptr);

    // Extract length from cstring struct (offset 8)
    const len = try loadI64Field(self, cstr_typed.value, 8);

    // Allocate buffer (len + 1 for null terminator)
    const heap = try emitGetProcessHeap(self);
    const one = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, len, one, .i64);
    const buffer = try emitHeapAllocWin(self, heap, alloc_size);

    // Copy string to buffer (include null terminator)
    try emitMemcpyWin(self, buffer, src_ptr, alloc_size);

    // Allocate __ManagedString struct (24 bytes)
    const string_size = try self.func().emitConstI64(24);
    const string_ptr = try emitHeapAllocWin(self, heap, string_size);

    // Initialize the struct
    try initHeapManagedString(self, string_ptr, buffer, len, alloc_size);

    return .{ .value = string_ptr, .ty = .{ .struct_type = "__ManagedString" } };
}

const make_char_intrinsics = .{
    .{ "__make_char_from_bytes", intrinsicMakeCharFromBytes },
};

fn convertMakeCharIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, make_char_intrinsics);
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
    return intrinsicLoadI32Field(self, call, 8);
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
    return intrinsicLoadI32Field(self, call, 16);
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
    return intrinsicLoadI32Field(self, call, 12);
}

// ============================================================================
// File I/O Intrinsics (stdlib-only)
// ============================================================================

/// Generate IR to write data to a file
/// Calls CreateFileA, WriteFile, CloseHandle
/// Returns 0 on success, non-zero on failure
fn emitWriteFileIR(self: *AstToIr, path_cstr: TypedValue, data_source: TypedValue) ConvertError!TypedValue {
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);
    const data_ptr = try self.func().emitLoad(data_source.value, .ptr);
    const data_len = try loadI64Field(self, data_source.value, 8);

    // CreateFileA(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL)
    // GENERIC_WRITE = 0x40000000, CREATE_ALWAYS = 2, FILE_ATTRIBUTE_NORMAL = 0x80
    const handle = try emitCreateFileA(self, path_ptr, 0x40000000, 0, 2, 0x80);

    // Check for INVALID_HANDLE_VALUE (-1)
    const invalid_handle = try self.func().emitConstI64(@as(i64, @bitCast(@as(u64, 0xFFFFFFFFFFFFFFFF))));
    const is_invalid = try self.func().emitBinaryOp(.icmp_eq, handle, invalid_handle, .i64);

    // Allocate bytes_written on stack
    const bytes_written_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_written_ptr, zero);

    // WriteFile(handle, buffer, length, &bytesWritten, NULL)
    const write_result = try emitWriteFileWin(self, handle, data_ptr, data_len, bytes_written_ptr);

    // Close handle
    try emitCloseHandle(self, handle);

    // Return 0 on success (write_result != 0 AND handle was valid), 1 on failure
    // write_result is BOOL (non-zero = success)
    const write_success = try self.func().emitBinaryOp(.icmp_ne, write_result, zero, .i64);
    const one = try self.func().emitConstI64(1);
    const not_invalid = try self.func().emitBinaryOp(.sub, one, is_invalid, .i64);
    const success = try self.func().emitBinaryOp(.mul, not_invalid, write_success, .i64);

    // Return 0 on success, 1 on failure
    const result = try self.func().emitBinaryOp(.sub, one, success, .i64);

    return .{ .value = result, .ty = .{ .primitive = "int" } };
}

/// Generate IR to read a file into a __ManagedString
/// Calls CreateFileA, GetFileSize, HeapAlloc, ReadFile, CloseHandle
/// Returns pointer to __ManagedString or NULL on failure
fn emitReadFileIR(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // CreateFileA(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL)
    // GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, OPEN_EXISTING = 3
    const handle = try emitCreateFileA(self, path_ptr, 0x80000000, 1, 3, 0);

    // Check for INVALID_HANDLE_VALUE (-1)
    const invalid_handle = try self.func().emitConstI64(@as(i64, @bitCast(@as(u64, 0xFFFFFFFFFFFFFFFF))));
    const is_invalid = try self.func().emitBinaryOp(.icmp_eq, handle, invalid_handle, .i64);

    // Get file size
    const file_size = try emitGetFileSize(self, handle);

    // Allocate buffer (size + 1 for null terminator)
    const heap = try emitGetProcessHeap(self);
    const one_i64 = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, file_size, one_i64, .i64);
    const buffer = try emitHeapAllocWin(self, heap, alloc_size);

    // Allocate bytes_read on stack
    const bytes_read_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_read_ptr, zero);

    // ReadFile(handle, buffer, size, &bytesRead, NULL)
    _ = try emitReadFile(self, handle, buffer, file_size, bytes_read_ptr);

    // Load bytes_read
    const bytes_read = try self.func().emitLoad(bytes_read_ptr, .i64);

    // Null-terminate the buffer
    const null_pos = try self.func().emitBinaryOp(.add, buffer, bytes_read, .ptr);
    try self.func().emitStoreI8(null_pos, try self.func().emitConstI64(0));

    // Close handle
    try emitCloseHandle(self, handle);

    // Allocate __ManagedString struct (24 bytes) if handle was valid
    // For simplicity, we always allocate but return NULL if invalid
    const string_size = try self.func().emitConstI64(24);
    const string_ptr = try emitHeapAllocWin(self, heap, string_size);

    try initHeapManagedString(self, string_ptr, buffer, bytes_read, alloc_size);

    // If handle was invalid, return NULL, else return string_ptr
    // result = string_ptr * (1 - is_invalid) = string_ptr if valid, 0 if invalid
    const not_invalid = try self.func().emitBinaryOp(.sub, one_i64, is_invalid, .i64);
    const result_ptr = try self.func().emitBinaryOp(.mul, string_ptr, not_invalid, .ptr);

    return wrapInOptional(self, result_ptr, "__ManagedString");
}

fn intrinsicWriteFile(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const path_cstr = try self.convertExpression(call.args[0]);
    const data = try self.convertExpression(call.args[1]);
    return emitWriteFileIR(self, path_cstr, data);
}

const file_intrinsics = .{
    .{ "__read_file", emitReadFileIR },
    .{ "__write_file", intrinsicWriteFile },
    .{ "__write_file_binary", intrinsicWriteFile },
};

fn convertFileIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, file_intrinsics);
}

// ============================================================================
// Command Line Arguments (generates IR instead of calling x86-64 runtime)
// ============================================================================

/// Generate IR to get command line arguments as Array$String
/// This replaces the old __get_command_args x86-64 runtime function
///
/// Array$String layout (32 bytes):
///   [0]  __ManagedArray managed (24 bytes: ptr + len + cap)
///   [24] int iterIndex (8 bytes)
///
/// String layout (32 bytes):
///   [0]  __ManagedString _managed (24 bytes)
///   [24] int _iterPos (8 bytes)
///
/// __ManagedString layout (24 bytes):
///   [0]  ptr buffer (8 bytes)
///   [8]  i32 len    (4 bytes)
///   [12] i32 cap_flags (4 bytes) - capacity with mode flags in low 2 bits (0b01 = heap)
///   [16] i32 refcount (4 bytes)
///   [20] i32 parent_off (4 bytes)
pub fn emitGetCommandArgs(self: *AstToIr) ConvertError!ir.Value {
    // Get process heap (needed for allocations)
    const heap = try emitGetProcessHeap(self);

    // Allocate argc storage on stack (8 bytes for i32 result, aligned)
    const argc_ptr = try self.func().emitAllocaSized(8);

    // Get command line and parse to argv
    const cmdline_w = try emitGetCommandLineW(self);
    const argv_w = try emitCommandLineToArgvW(self, cmdline_w, argc_ptr);

    // Load argc (it's stored as i32 but we need i64)
    const argc_i32 = try self.func().emitLoad(argc_ptr, .i32);
    const argc = try self.func().emitUnaryOp(.sext_i32_i64, argc_i32, .i64);

    // Allocate Array$String struct (32 bytes)
    const array_size = try self.func().emitConstI64(32);
    const result_ptr = try emitHeapAllocWin(self, heap, array_size);

    // Allocate strings array: argc * 32 bytes (each String is 32 bytes)
    const string_size = try self.func().emitConstI64(32);
    const strings_alloc_size = try self.func().emitBinaryOp(.mul, argc, string_size, .i64);
    const strings_buf = try emitHeapAllocWin(self, heap, strings_alloc_size);

    // Initialize Array$String:
    // [0]  ptr buffer = strings_buf
    // [8]  i64 length = argc
    // [16] i64 capacity = argc
    // [24] i64 iterIndex = 0
    try self.func().emitStore(result_ptr, strings_buf);
    try storeI64Field(self, result_ptr, 8, argc);
    try storeI64Field(self, result_ptr, 16, argc);
    const zero_i64 = try self.func().emitConstI64(0);
    try storeI64Field(self, result_ptr, 24, zero_i64);

    // Loop counter on stack
    const i_ptr = try self.func().emitAllocaSized(8);
    try self.func().emitStore(i_ptr, zero_i64);

    // Create condition block - get its index before creating
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const cond_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmd_args_cond");

    // Emit branch from entry to cond
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = cond_block_idx }, .none, .none },
    });

    // Now in cond block - emit condition check
    const i_val = try self.func().emitLoad(i_ptr, .i64);
    const cond = try self.func().emitBinaryOp(.icmp_lt, i_val, argc, .i64);

    // Create body block
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmd_args_body");

    // Now in body block - convert argv_w[i] to UTF-8 String
    const i_body = try self.func().emitLoad(i_ptr, .i64);

    // Get argv_w[i] pointer (each pointer is 8 bytes)
    const argv_i_ptr_ptr = try self.func().emitGetElemPtr(argv_w, i_body, 8);
    const wide_str = try self.func().emitLoad(argv_i_ptr_ptr, .ptr);

    // Get required UTF-8 buffer size (pass 0 for dest and size to query)
    const utf8_size = try emitWideCharToMultiByte(self, wide_str, zero_i64, zero_i64);

    // Allocate UTF-8 buffer
    const utf8_buf = try emitHeapAllocWin(self, heap, utf8_size);

    // Convert to UTF-8
    _ = try emitWideCharToMultiByte(self, wide_str, utf8_buf, utf8_size);

    // Calculate String element address: strings_buf + i * 32
    const string_ptr = try self.func().emitGetElemPtr(strings_buf, i_body, 32);

    // len = utf8_size - 1 (exclude null terminator)
    const one_i64 = try self.func().emitConstI64(1);
    const len_i64 = try self.func().emitBinaryOp(.sub, utf8_size, one_i64, .i64);
    try initHeapString(self, string_ptr, utf8_buf, len_i64, utf8_size);

    // Increment i
    const new_i = try self.func().emitBinaryOp(.add, i_body, one_i64, .i64);
    try self.func().emitStore(i_ptr, new_i);

    // Branch back to cond
    try self.func().emitBr(cond_block_idx);

    // Create end block - now we know the final index
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmd_args_end");

    // Emit conditional branch in cond block now that we know end_block_idx
    try self.func().blocks.items[cond_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = cond }, .{ .block_ref = body_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // Now in end block - free argv_w and return result
    try emitLocalFree(self, argv_w);

    return result_ptr;
}
