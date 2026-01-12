const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const TypedValue = types.TypedValue;
const ConvertError = types.ConvertError;
const intrinsics_registry = @import("intrinsics_registry.zig");
const struct_helpers = @import("ir_struct_helpers.zig");

// Import type-specific modules for layouts
const string = @import("ast_to_ir_string.zig");
const array = @import("ast_to_ir_array.zig");
const ManagedArray = string.ManagedArray;
const String = string.String;
const Array = array.Array;

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
    const value = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(arg.value), offset);
    return .{ .value = value, .ty = .{ .primitive = "int" } };
}

/// Common pattern for intrinsics that load a single i32 field (sign-extended) and return int
fn intrinsicLoadI32Field(self: *AstToIr, call: ast.CallExpr, offset: i32) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const arg = try self.convertExpression(call.args[0]);
    const value = try struct_helpers.loadI32AsI64(self.func(), ir.toStructPtr(arg.value), offset);
    return .{ .value = value, .ty = .{ .primitive = "int" } };
}

/// Initialize a heap-allocated String struct (40 bytes with _iterPos field)
/// Calls initHeapManagedArray for the __ManagedArray part
fn initHeapString(self: *AstToIr, string_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value) ConvertError!void {
    try initHeapManagedArray(self, string_ptr, buffer, len_i64);
    // Add _iterPos = 0 at offset 32
    const zero_i64 = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(string_ptr), 32, zero_i64);
}

/// Initialize a heap-allocated __ManagedArray struct (32 bytes for strings with mode=1)
fn initHeapManagedArray(self: *AstToIr, array_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value) ConvertError!void {
    // mode = 1 (heap-refcounted)
    const mode_one = try self.func().emitConstI32(1);
    return struct_helpers.initManagedArray(self.func(), ir.toManagedArrayPtr(array_ptr), ir.toRawPtr(buffer), len_i64, len_i64, mode_one);
}

/// Initialize slice array metadata fields (flags=2, parent_off)
/// Assumes buffer and len are already set at offsets 0 and 8
fn initSliceArrayFields(self: *AstToIr, result_ptr: ir.Value, parent_off_i32: ir.Value) ConvertError!void {
    // capacity = 0 for slices at offset 16
    const zero_i64 = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(result_ptr), 16, zero_i64);

    // flags = 2 (slice mode) at offset 24
    const two_i32 = try self.func().emitConstI32(2);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(result_ptr), 24, two_i32);

    // parent_off at offset 28
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(result_ptr), 28, parent_off_i32);
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

/// Allocate a refcounted buffer with 8-byte header for refcounting.
/// Returns the DATA pointer (header is at returned_ptr - 8).
fn emitAllocRefcountedBufferWin(self: *AstToIr, heap: ir.Value, data_size: ir.Value) ConvertError!ir.Value {
    // Allocate data_size + 8 for header
    const header_size = try self.func().emitConstI64(struct_helpers.REFCOUNTED_BUFFER_HEADER_SIZE);
    const total_size = try self.func().emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try emitHeapAllocWin(self, heap, total_size);

    // Initialize refcount in header to 1
    try self.func().emitStore(buffer_with_header, try self.func().emitConstI64(1));

    // Return data pointer (header + 8)
    const eight = try self.func().emitConstI64(8);
    return self.func().emitBinaryOp(.add, buffer_with_header, eight, .ptr);
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
    return self.func().emitCstrLen(str);
}

fn emitMemcpyWin(self: *AstToIr, dest: ir.Value, src: ir.Value, size: ir.Value) ConvertError!void {
    return self.func().emitMemcpyDynamic(ir.toRawPtr(dest), ir.toRawPtr(src), size);
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
    is_enum: bool,
    type_name: ?[]const u8,
};

/// Direction for array shift operations
const ShiftDirection = enum { left, right };

/// Get element size and type info from generic_params or current_type_name
/// Used by managed array intrinsics to determine element size at compile time
/// @param param_name: The generic param to look for ("Element", "Key", or "Value")
fn getElementInfoForParam(self: *AstToIr, param_name: []const u8) ElemInfo {
    // Check generic_params for the specified binding (available in monomorphized methods)
    if (self.generic_params.get(param_name)) |elem_type_name| {
        if (self.type_map.get(elem_type_name)) |type_info| {
            if (type_info == .struct_type) {
                return .{
                    .size = type_info.struct_type.size,
                    .is_struct = true,
                    .is_enum = false,
                    .type_name = elem_type_name,
                };
            } else if (type_info == .enum_type) {
                return .{
                    .size = 8,
                    .is_struct = false,
                    .is_enum = true,
                    .type_name = elem_type_name,
                };
            }
        }
        // Known type but not a struct or enum (int, float, bool, byte)
        return .{ .size = 8, .is_struct = false, .is_enum = false, .type_name = elem_type_name };
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
                        .is_enum = false,
                        .type_name = elem_name,
                    };
                } else if (type_info == .enum_type) {
                    return .{
                        .size = 8,
                        .is_struct = false,
                        .is_enum = true,
                        .type_name = elem_name,
                    };
                }
            }
            return .{ .size = 8, .is_struct = false, .is_enum = false, .type_name = elem_name };
        }
    }

    // Default to primitive (8 bytes)
    return .{ .size = 8, .is_struct = false, .is_enum = false, .type_name = null };
}

/// Get element size and type info - default "Element" param
fn getElementInfo(self: *AstToIr) ElemInfo {
    return getElementInfoForParam(self, "Element");
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
    const len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed.value), 8);

    const one = try self.func().emitConstI64(1);
    const zero = try self.func().emitConstI64(0);

    // Initialize loop counter
    const i_ptr = try self.func().emitAllocaSized(8);

    // Direction-specific setup
    const cond_name = if (direction == .right) "shift_right_cond" else "shift_left_cond";
    const body_name = if (direction == .right) "shift_right_body" else "shift_left_body";
    const end_name = if (direction == .right) "shift_right_end" else "shift_left_end";

    if (direction == .right) {
        // Right shift: start at len-1
        const init_i = try self.func().emitBinaryOp(.sub, len, one, .i64);
        try self.func().emitStore(i_ptr.raw(), init_i);
    } else {
        // Left shift: start at 0
        try self.func().emitStore(i_ptr.raw(), zero);
    }

    var loop = try self.createLoopBlocks(cond_name, body_name, end_name);

    // Condition check
    const i_val = try self.func().emitLoad(i_ptr.raw(), .i64);
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
    const i_val2 = try self.func().emitLoad(i_ptr.raw(), .i64);

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
        try self.func().emitMemcpy(ir.toRawPtr(dst_ptr), ir.toRawPtr(src_ptr), elem_info.size);
    } else {
        const elem_val = try self.func().emitLoad(src_ptr, .i64);
        try self.func().emitStore(dst_ptr, elem_val);
    }

    // Update loop counter
    const new_i = if (direction == .right)
        try self.func().emitBinaryOp(.sub, i_val2, one, .i64)
    else
        try self.func().emitBinaryOp(.add, i_val2, one, .i64);
    try self.func().emitStore(i_ptr.raw(), new_i);
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
    // Map initialization intrinsics - use Key/Value generic params
    .{ "__map_get_init_key", intrinsicMapGetInitKey },
    .{ "__map_get_init_value", intrinsicMapGetInitValue },
    // String-related intrinsics (now unified under __managed_array)
    .{ "__managed_array_byte_at", intrinsicManagedArrayByteAt },
    .{ "__managed_array_slice", intrinsicManagedArraySlice },
    .{ "__managed_array_concat", intrinsicManagedArrayConcat },
    .{ "__managed_array_make_unique", intrinsicManagedArrayMakeUnique },
    .{ "__managed_array_set_byte", intrinsicManagedArraySetByte },
    .{ "__managed_array_to_cstring", intrinsicManagedArrayToCstring },
    .{ "__managed_array_from_bytes", intrinsicManagedArrayFromBytes },
    .{ "__managed_array_incref", intrinsicManagedArrayIncref },
    .{ "__managed_array_refcount", intrinsicManagedArrayRefcount },
    .{ "__managed_array_is_refcounted", intrinsicManagedArrayIsRefcounted },
    .{ "__managed_array_flags", intrinsicManagedArrayFlags },
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
/// Mode is 0 (no refcounting) for regular arrays
fn intrinsicManagedArrayCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const capacity = try self.convertExpression(call.args[0]);

    const managed_ptr = try self.func().emitAllocaSized(ManagedArray.SIZE);

    const elem_info = getElementInfo(self);
    const elem_size_val = try self.func().emitConstI64(elem_info.size);
    const buf_size = try self.func().emitBinaryOp(.mul, capacity.value, elem_size_val, .i64);
    const buf_ptr = try self.func().emitHeapAlloc(buf_size, "array buffer");

    // Initialize with mode=0 (no refcounting for regular arrays)
    const zero_i32 = try self.func().emitConstI32(0);
    try struct_helpers.initManagedArray(self.func(), managed_ptr.asManagedArrayPtr(), buf_ptr, capacity.value, capacity.value, zero_i32);

    return .{ .value = managed_ptr.raw(), .ty = .{ .primitive = "ptr" } };
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
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_size);

    if (value.ty == .struct_type and elem_size > 8) {
        try self.func().emitMemcpy(elem_ptr.asRawPtr(), ir.toRawPtr(value.value), @intCast(elem_size));
    } else {
        try self.func().emitStore(elem_ptr.raw(), value.value);
    }

    // When storing a String into an array, we need to incref the buffer header.
    // The buffer header refcount is shared by all String copies pointing to the same buffer.
    // Both the source variable and the array slot now reference the buffer, so refcount must be 2.
    // The source variable may still be cleaned up at scope exit (decref to 1).
    // When the array is destroyed, it will decref each element (to 0, then free).
    if (value.ty == .struct_type) {
        if (std.mem.eql(u8, value.ty.struct_type, "String")) {
            try string.emitStringIncref(self, ir.toStringPtr(value.value), "<array_store>");
        }
    }

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_set_length(managed, new_len) -> void
fn intrinsicManagedArraySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const new_len = try self.convertExpression(call.args[1]);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(managed.value), 8, new_len.value);
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
    const new_buf = try self.func().emitHeapRealloc(ir.toRawPtr(buf_ptr), new_size, "array grow");

    try self.func().emitStore(managed.value, new_buf.raw());
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(managed.value), 16, new_capacity.value);

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
/// Returns a pointer to the element in the array buffer.
/// For struct elements (including String), returns pointer directly to array buffer.
/// Caller is responsible for copying and incref if needed.
fn intrinsicManagedArrayGetUnchecked(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = getElementInfo(self);
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    if (elem_info.is_struct) {
        return .{ .value = elem_ptr.raw(), .ty = .{ .struct_type = elem_info.type_name.? } };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = elem_info.type_name orelse "int" } };
    }
}

/// __map_get_init_key(managed, index) -> Key
/// Like __managed_array_get_unchecked but uses "Key" generic param instead of "Element"
/// For String keys: copies the struct and increfs the buffer so the caller owns the String
fn intrinsicMapGetInitKey(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = getElementInfoForParam(self, "Key");
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    if (elem_info.is_struct) {
        const type_name = elem_info.type_name.?;

        // String keys need special handling: copy struct and incref so caller owns it
        if (std.mem.eql(u8, type_name, "String")) {
            // Allocate a new String struct on the stack
            const new_string_ptr = try self.func().emitAllocaSized(String.SIZE);
            // Copy the String struct (memcpy 16 bytes)
            try self.func().emitMemcpy(new_string_ptr, elem_ptr.asRawPtr(), String.SIZE);
            // Incref the buffer header so caller has their own reference
            try string.emitStringIncref(self, ir.toStringPtr(new_string_ptr.val), "<array index String>");
            return .{ .value = new_string_ptr.val, .ty = .{ .struct_type = type_name } };
        }

        return .{ .value = elem_ptr.raw(), .ty = .{ .struct_type = type_name } };
    } else if (elem_info.is_enum) {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .enum_type = elem_info.type_name.? } };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = elem_info.type_name orelse "int" } };
    }
}

/// __map_get_init_value(managed, index) -> Value
/// Like __managed_array_get_unchecked but uses "Value" generic param instead of "Element"
/// For String values: copies the struct and increfs the buffer so the caller owns the String
fn intrinsicMapGetInitValue(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = getElementInfoForParam(self, "Value");

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    if (elem_info.is_struct) {
        const type_name = elem_info.type_name.?;

        // String values need special handling: copy struct and incref so caller owns it
        if (std.mem.eql(u8, type_name, "String")) {
            // Allocate a new String struct on the stack
            const new_string_ptr = try self.func().emitAllocaSized(String.SIZE);
            // Copy the String struct (memcpy 16 bytes)
            try self.func().emitMemcpy(new_string_ptr, elem_ptr.asRawPtr(), String.SIZE);
            // Incref the buffer header so caller has their own reference
            try string.emitStringIncref(self, ir.toStringPtr(new_string_ptr.val), "<map_init_value>");
            return .{ .value = new_string_ptr.val, .ty = .{ .struct_type = type_name } };
        }

        return .{ .value = elem_ptr.raw(), .ty = .{ .struct_type = type_name } };
    } else if (elem_info.is_enum) {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .enum_type = elem_info.type_name.? } };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = elem_info.type_name orelse "int" } };
    }
}

// ============================================================================
// __ManagedArray Intrinsics for Strings (stdlib-only)
// ============================================================================
//
// __ManagedArray memory layout (32 bytes):
//   Offset 0: ptr buffer      - Pointer to data (8 bytes)
//   Offset 8: i64 len         - Length (8 bytes)
//   Offset 16: i64 capacity   - Capacity (8 bytes)
//   Offset 24: i32 flags      - Mode flags (4 bytes) - bits 0-1: 0=SSO, 1=refcounted, 2=slice
//   Offset 28: i32 parent_off - Parent offset for slices (4 bytes)
//
// For mode 1 (heap-refcounted), buffer points to data with refcount header at buffer-8:

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
    const length = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(cstring.value), 8);

    const args = try self.allocator.alloc(ir.Value, 2);
    args[0] = data_ptr;
    args[1] = length;

    const result = try self.func().emitCall("__write_stdout", args, .i64);
    return .{ .value = result orelse try self.func().emitConstI64(0), .ty = .{ .primitive = "int" } };
}

/// __cstring_to_managed(cstr) -> __ManagedArray
/// Converts a cstring struct to a __ManagedArray (string mode)
/// The string data is copied to a new heap buffer with header for refcounting
/// cstring struct layout: data(8) + length(8) + managed(8)
fn intrinsicCstringToManaged(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const cstr_typed = try self.convertExpression(call.args[0]);

    // Extract data pointer from cstring struct (offset 0)
    const src_ptr = try self.func().emitLoad(cstr_typed.value, .ptr);

    // Extract length from cstring struct (offset 8)
    const len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(cstr_typed.value), 8);

    // Allocate buffer with header (len + 1 for null terminator)
    const heap = try emitGetProcessHeap(self);
    const one = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, len, one, .i64);
    const buffer = try emitAllocRefcountedBufferWin(self, heap, alloc_size);

    // Copy string to buffer (include null terminator)
    try emitMemcpyWin(self, buffer, src_ptr, alloc_size);

    // Allocate __ManagedArray struct (32 bytes)
    const array_size = try self.func().emitConstI64(32);
    const array_ptr = try emitHeapAllocWin(self, heap, array_size);

    // Initialize the struct (mode=1 for heap-refcounted)
    try initHeapManagedArray(self, array_ptr, buffer, len);

    return .{ .value = array_ptr, .ty = .{ .struct_type = "__ManagedArray" } };
}

const make_char_intrinsics = .{
    .{ "__make_char_from_bytes", intrinsicMakeCharFromBytes },
};

fn convertMakeCharIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, make_char_intrinsics);
}

/// __make_char_from_bytes(managed, pos, len) -> Character
/// Creates a slice-mode Character struct (ManagedArray.SIZE bytes)
fn intrinsicMakeCharFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const pos = try self.convertExpression(call.args[1]);
    const len = try self.convertExpression(call.args[2]);

    const char_ptr = try self.func().emitAllocaSized(ManagedArray.SIZE);
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer pointer (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, pos.value, .ptr);
    try self.func().emitStore(char_ptr.raw(), slice_buf);

    // Store len (offset 8, i64)
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(char_ptr.raw()), 8, len.value);

    // Initialize slice metadata fields
    const pos_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, pos.value, .i32);
    try initSliceArrayFields(self, char_ptr.raw(), pos_i32);

    return .{ .value = char_ptr.raw(), .ty = .{ .struct_type = "Character" } };
}

/// __managed_array_byte_at(managed, index) -> byte
/// Returns the byte at the given index in the buffer
fn intrinsicManagedArrayByteAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, 1);
    const byte_val = try self.func().emitLoad(byte_ptr.raw(), .i8);

    return .{ .value = byte_val, .ty = .{ .primitive = "byte" } };
}

/// __managed_array_slice(managed, start, end) -> __ManagedArray (slice mode)
/// Creates a slice view into the managed array
/// Returns a 32-byte __ManagedArray with flags = 2 (slice mode)
/// Layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)
fn intrinsicManagedArraySlice(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
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

    const slice_ptr = try self.func().emitAllocaSized(ManagedArray.SIZE);
    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer = parent_buf_ptr + start (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, start.value, .ptr);
    try self.func().emitStore(slice_ptr.raw(), slice_buf);

    // Store len = end - start (offset 8, i64)
    const len_i64 = try self.func().emitBinaryOp(.sub, end.value, start.value, .i64);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 8, len_i64);

    // Initialize slice metadata (flags=2, parent_off=start)
    const start_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, start.value, .i32);
    try initSliceArrayFields(self, slice_ptr.raw(), start_i32);

    return .{ .value = slice_ptr.raw(), .ty = .{ .primitive = "__ManagedArray" } };
}

/// __managed_array_concat(a, b) -> __ManagedArray
/// Concatenates two managed arrays (strings) into a new heap-allocated array
fn intrinsicManagedArrayConcat(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);

    const a = try self.convertExpression(call.args[0]);
    const b = try self.convertExpression(call.args[1]);

    // Load buffer pointers BEFORE allocating new struct to avoid aliasing in loops
    const a_buf = try self.func().emitLoad(a.value, .ptr);
    const b_buf = try self.func().emitLoad(b.value, .ptr);

    // Load lengths (offset 8, i64)
    const a_len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(a.value), 8);
    const b_len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(b.value), 8);
    const total_len = try self.func().emitBinaryOp(.add, a_len, b_len, .i64);

    // Allocate with header for refcounting
    const buf_ptr = try struct_helpers.emitAllocRefcountedBuffer(self.func(), total_len, "array concat");

    // Copy both arrays into the new buffer
    try self.func().emitMemcpyDynamic(buf_ptr, ir.toRawPtr(a_buf), a_len);
    const dst_offset = try self.func().emitBinaryOp(.add, buf_ptr.raw(), a_len, .ptr);
    try self.func().emitMemcpyDynamic(ir.toRawPtr(dst_offset), ir.toRawPtr(b_buf), b_len);

    // Initialize __ManagedArray struct (mode=1 for heap-refcounted)
    const mode_one = try self.func().emitConstI32(1);
    const result_ptr = try struct_helpers.emitManagedArray(self.func(), buf_ptr, total_len, total_len, mode_one);

    return .{ .value = result_ptr.raw(), .ty = .{ .primitive = "__ManagedArray" } };
}

/// __managed_array_make_unique(managed) -> __ManagedArray
/// Creates a mutable copy of the array (COW - copy on write)
fn intrinsicManagedArrayMakeUnique(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load length and original buffer (offset 8, i64)
    const len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed.value), 8);
    const orig_buf = try self.func().emitLoad(managed.value, .ptr);

    // Allocate with header for refcounting
    const new_buf = try struct_helpers.emitAllocRefcountedBuffer(self.func(), len, "array buffer");
    try self.func().emitMemcpyDynamic(new_buf, ir.toRawPtr(orig_buf), len);

    // Initialize __ManagedArray struct (mode=1 for heap-refcounted)
    const mode_one = try self.func().emitConstI32(1);
    const result_ptr = try struct_helpers.emitManagedArray(self.func(), new_buf, len, len, mode_one);

    return .{ .value = result_ptr.raw(), .ty = .{ .primitive = "__ManagedArray" } };
}

/// __managed_array_set_byte(managed, index, byte) -> void
/// Sets a byte at the given index (caller must ensure array is unique)
fn intrinsicManagedArraySetByte(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const byte_val = try self.convertExpression(call.args[2]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, 1);
    const byte_i8 = try self.func().emitUnaryOp(.trunc_i64_i8, byte_val.value, .i8);
    try self.func().emitStoreI8(byte_ptr.raw(), byte_i8);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_to_cstring(managed) -> cstring struct
/// Creates a cstring struct from a __ManagedArray (string).
/// For heap-refcounted mode: shares buffer (already null-terminated), holds reference to managed array
/// For Slice mode: copies data to new null-terminated buffer (cstring owns its buffer)
fn intrinsicManagedArrayToCstring(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const cstring_ptr = try self.func().emitAllocaSized(24);

    // Load flags (offset 24) to determine mode
    const flags = try self.func().emitLoad((try self.func().emitGetFieldPtr(ir.toStructPtr(managed.value), ManagedArray.FLAGS_OFFSET)).raw(), .i32);
    const three = try self.func().emitConstI32(3);
    const mode = try self.func().emitBinaryOp(.band, flags, three, .i32);
    const two = try self.func().emitConstI32(2);
    const is_slice = try self.func().emitBinaryOp(.icmp_eq, mode, two, .i32);

    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const slice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_slice");

    // === SLICE BLOCK: Copy data to new null-terminated buffer ===
    const slice_buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const slice_len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed.value), 8);

    const one_i64 = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, slice_len, one_i64, .i64);
    const new_buf = try self.func().emitHeapAlloc(alloc_size, "cstring conversion");

    try self.func().emitMemcpyDynamic(new_buf, ir.toRawPtr(slice_buf_ptr), slice_len);

    // Write null terminator
    const null_pos = try self.func().emitBinaryOp(.add, new_buf.raw(), slice_len, .ptr);
    try self.func().emitStoreI8(null_pos, try self.func().emitConstI64(0));

    // Store cstring fields (data, length, managed=null)
    try self.func().emitStore(cstring_ptr.raw(), new_buf.raw());
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 8, slice_len);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 16, try self.func().emitConstI64(0));

    // Create nonslice block
    const nonslice_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_nonslice");

    // === NON-SLICE BLOCK: Share existing buffer ===
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const length = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed.value), 8);

    // Store cstring fields (data, length, managed)
    try self.func().emitStore(cstring_ptr.raw(), buf_ptr);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 8, length);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 16, managed.value);

    // Check if heap-refcounted mode for incref
    const one_i32 = try self.func().emitConstI32(1);
    const is_refcounted = try self.func().emitBinaryOp(.icmp_eq, mode, one_i32, .i32);

    const incref_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cstr_incref");

    // Increment refcount in buffer header (buf_ptr - 8)
    const incref_buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const eight_incref = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, incref_buf_ptr, eight_incref, .ptr);
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one_ref = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one_ref, .i64);
    try self.func().emitStore(header_ptr, new_ref);

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
        .operands = .{ .{ .value = is_refcounted }, .{ .block_ref = incref_block_idx }, .{ .block_ref = end_block_idx } },
    });
    try self.func().blocks.items[incref_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
        .result = null,
    });

    return .{ .value = cstring_ptr.raw(), .ty = .{ .struct_type = "cstring" } };
}

/// __managed_array_from_bytes(buffer, length) -> __ManagedArray
/// Creates a new managed array (string) from a byte array buffer
fn intrinsicManagedArrayFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const buffer = try self.convertExpression(call.args[0]);
    const length = try self.convertExpression(call.args[1]);

    // Allocate with header for refcounting
    const new_buf = try struct_helpers.emitAllocRefcountedBuffer(self.func(), length.value, "array buffer");

    // Copy data from source buffer
    const src_buf = try self.func().emitLoad(buffer.value, .ptr);
    try self.func().emitMemcpyDynamic(new_buf, ir.toRawPtr(src_buf), length.value);

    // Initialize __ManagedArray struct (mode=1 for heap-refcounted)
    const mode_one = try self.func().emitConstI32(1);
    const result_ptr = try struct_helpers.emitManagedArray(self.func(), new_buf, length.value, length.value, mode_one);

    return .{ .value = result_ptr.raw(), .ty = .{ .primitive = "__ManagedArray" } };
}

// ============================================================================
// Reference Counting Intrinsics for COW Strings
// ============================================================================

/// __managed_array_incref(managed) -> void
/// Increments the reference count in buffer header for heap-refcounted arrays
fn intrinsicManagedArrayIncref(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load buffer pointer and compute header address (buf_ptr - 8)
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);

    // Increment refcount in header
    const old_ref = try self.func().emitLoad(header_ptr, .i64);
    const one = try self.func().emitConstI64(1);
    const new_ref = try self.func().emitBinaryOp(.add, old_ref, one, .i64);
    try self.func().emitStore(header_ptr, new_ref);

    return .{ .value = 0, .ty = .{ .primitive = "void" } };
}

/// __managed_array_refcount(managed) -> int
/// Returns the refcount from buffer header (buf_ptr - 8)
fn intrinsicManagedArrayRefcount(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load buffer pointer and compute header address (buf_ptr - 8)
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const eight = try self.func().emitConstI64(8);
    const header_ptr = try self.func().emitBinaryOp(.sub, buf_ptr, eight, .ptr);

    // Load refcount from header
    const refcount = try self.func().emitLoad(header_ptr, .i64);

    return .{ .value = refcount, .ty = .{ .primitive = "int" } };
}

/// __managed_array_is_refcounted(managed) -> bool
/// Returns true if the array is heap-refcounted (flags & 0x1 == 1)
fn intrinsicManagedArrayIsRefcounted(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const flags = try self.func().emitLoad((try self.func().emitGetFieldPtr(ir.toStructPtr(managed.value), ManagedArray.FLAGS_OFFSET)).raw(), .i32);
    const one = try self.func().emitConstI32(1);
    const is_refcounted = try self.func().emitBinaryOp(.band, flags, one, .i32);

    return .{ .value = is_refcounted, .ty = .{ .primitive = "bool" } };
}

/// __managed_array_flags(managed) -> int
/// Returns the flags field (offset 24, i32)
fn intrinsicManagedArrayFlags(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return intrinsicLoadI32Field(self, call, 24);
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
    const data_len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(data_source.value), 8);

    // CreateFileA(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL)
    // GENERIC_WRITE = 0x40000000, CREATE_ALWAYS = 2, FILE_ATTRIBUTE_NORMAL = 0x80
    const handle = try emitCreateFileA(self, path_ptr, 0x40000000, 0, 2, 0x80);

    // Check for INVALID_HANDLE_VALUE (-1)
    const invalid_handle = try self.func().emitConstI64(@as(i64, @bitCast(@as(u64, 0xFFFFFFFFFFFFFFFF))));
    const is_invalid = try self.func().emitBinaryOp(.icmp_eq, handle, invalid_handle, .i64);

    // Allocate bytes_written on stack
    const bytes_written_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_written_ptr.raw(), zero);

    // WriteFile(handle, buffer, length, &bytesWritten, NULL)
    const write_result = try emitWriteFileWin(self, handle, data_ptr, data_len, bytes_written_ptr.raw());

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

// ============================================================================
// Low-level File Handle Intrinsics
// ============================================================================
// These expose Windows file operations directly to Maxon code.
// File.maxon uses these to implement high-level operations with proper error handling.
//
// __file_open_read(cstr) -> int   : Opens file for reading, returns handle (-1 on failure)
// __file_open_write(cstr) -> int  : Opens/creates file for writing, returns handle (-1 on failure)
// __file_size(handle) -> int      : Gets file size in bytes
// __file_read(handle, buffer, size) -> int : Reads into buffer, returns bytes read
// __file_close(handle)            : Closes file handle
// __file_read_all(cstr) -> __ManagedArray : High-level: reads entire file (null on failure)

/// __file_open_read(cstr) -> int
/// Opens a file for reading. Returns handle or -1 (INVALID_HANDLE_VALUE) on failure.
fn intrinsicFileOpenRead(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, OPEN_EXISTING = 3
    const handle = try emitCreateFileA(self, path_ptr, 0x80000000, 1, 3, 0);
    return .{ .value = handle, .ty = .{ .primitive = "int" } };
}

/// __file_open_write(cstr) -> int
/// Opens/creates a file for writing. Returns handle or -1 on failure.
fn intrinsicFileOpenWrite(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GENERIC_WRITE = 0x40000000, share_mode = 0, CREATE_ALWAYS = 2, FILE_ATTRIBUTE_NORMAL = 0x80
    const handle = try emitCreateFileA(self, path_ptr, 0x40000000, 0, 2, 0x80);
    return .{ .value = handle, .ty = .{ .primitive = "int" } };
}

/// __file_size(handle) -> int
/// Gets the size of an open file in bytes.
fn intrinsicFileSize(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_typed = try self.convertExpression(call.args[0]);
    const size = try emitGetFileSize(self, handle_typed.value);
    return .{ .value = size, .ty = .{ .primitive = "int" } };
}

/// __file_read(handle, managed_array, size) -> int
/// Reads from file into managed array's buffer. Returns bytes read.
fn intrinsicFileRead(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const handle_typed = try self.convertExpression(call.args[0]);
    const array_typed = try self.convertExpression(call.args[1]);
    const size_typed = try self.convertExpression(call.args[2]);

    // Get buffer pointer from __ManagedArray (offset 0)
    const buffer = try self.func().emitLoad(array_typed.value, .ptr);

    // Allocate bytes_read on stack
    const bytes_read_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_read_ptr.raw(), zero);

    // ReadFile(handle, buffer, size, &bytesRead, NULL)
    _ = try emitReadFile(self, handle_typed.value, buffer, size_typed.value, bytes_read_ptr.raw());

    // Return bytes read
    const bytes_read = try self.func().emitLoad(bytes_read_ptr.raw(), .i64);
    return .{ .value = bytes_read, .ty = .{ .primitive = "int" } };
}

/// __file_close(handle) -> int
/// Closes a file handle. Returns 0.
fn intrinsicFileClose(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_typed = try self.convertExpression(call.args[0]);
    try emitCloseHandle(self, handle_typed.value);
    return .{ .value = try self.func().emitConstI64(0), .ty = .{ .primitive = "int" } };
}

/// __file_read_all(cstr) -> __ManagedArray
/// High-level intrinsic: reads entire file into a managed array.
/// Returns null pointer on failure (caller checks with == -1 pattern or similar).
fn intrinsicFileReadAll(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, OPEN_EXISTING = 3
    const handle = try emitCreateFileA(self, path_ptr, 0x80000000, 1, 3, 0);

    // Check for INVALID_HANDLE_VALUE (-1)
    const invalid_handle = try self.func().emitConstI64(@as(i64, @bitCast(@as(u64, 0xFFFFFFFFFFFFFFFF))));
    const is_invalid = try self.func().emitBinaryOp(.icmp_eq, handle, invalid_handle, .i64);

    // Get file size
    const file_size = try emitGetFileSize(self, handle);

    // Allocate buffer with header (size + 1 for null terminator)
    const heap = try emitGetProcessHeap(self);
    const one_i64 = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, file_size, one_i64, .i64);
    const buffer = try emitAllocRefcountedBufferWin(self, heap, alloc_size);

    // Allocate bytes_read on stack
    const bytes_read_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_read_ptr.raw(), zero);

    // ReadFile(handle, buffer, size, &bytesRead, NULL)
    _ = try emitReadFile(self, handle, buffer, file_size, bytes_read_ptr.raw());

    // Load bytes_read
    const bytes_read = try self.func().emitLoad(bytes_read_ptr.raw(), .i64);

    // Null-terminate the buffer
    const null_pos = try self.func().emitBinaryOp(.add, buffer, bytes_read, .ptr);
    try self.func().emitStoreI8(null_pos, try self.func().emitConstI64(0));

    // Close handle
    try emitCloseHandle(self, handle);

    // Allocate __ManagedArray struct (32 bytes)
    const array_size = try self.func().emitConstI64(32);
    const array_ptr = try emitHeapAllocWin(self, heap, array_size);

    try initHeapManagedArray(self, array_ptr, buffer, bytes_read);

    // If handle was invalid, return -1 (as int), else return array_ptr
    // This lets Maxon code check: if result == -1 then error
    // Use: result = invalid_handle * is_invalid + array_ptr * (1 - is_invalid)
    const not_invalid = try self.func().emitBinaryOp(.sub, one_i64, is_invalid, .i64);
    const valid_result = try self.func().emitBinaryOp(.mul, array_ptr, not_invalid, .ptr);
    const invalid_result = try self.func().emitBinaryOp(.mul, invalid_handle, is_invalid, .i64);
    const result_ptr = try self.func().emitBinaryOp(.add, valid_result, invalid_result, .ptr);

    return .{ .value = result_ptr, .ty = .{ .struct_type = "__ManagedArray" } };
}

fn intrinsicWriteFile(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const path_cstr = try self.convertExpression(call.args[0]);
    const data = try self.convertExpression(call.args[1]);
    return emitWriteFileIR(self, path_cstr, data);
}

const file_intrinsics = .{
    // Low-level file handle operations
    .{ "__file_open_read", intrinsicFileOpenRead },
    .{ "__file_open_write", intrinsicFileOpenWrite },
    .{ "__file_size", intrinsicFileSize },
    .{ "__file_read", intrinsicFileRead },
    .{ "__file_close", intrinsicFileClose },
    // High-level operations (for convenience, returns -1 on failure)
    .{ "__file_read_all", intrinsicFileReadAll },
    .{ "__write_file", intrinsicWriteFile },
    .{ "__write_file_binary", intrinsicWriteFile },
    // Directory operations
    .{ "__find_first_file", intrinsicFindFirstFile },
    .{ "__find_next_file", intrinsicFindNextFile },
    .{ "__find_close", intrinsicFindClose },
    .{ "__find_filename", intrinsicFindFilename },
    .{ "__get_file_attributes", intrinsicGetFileAttributes },
    .{ "__directory_exists", emitDirectoryExistsIR },
};

fn convertFileIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, file_intrinsics);
}

// ============================================================================
// Directory Intrinsics - Opaque Handle API
// ============================================================================
//
// The opaque handle layout (328 bytes total):
//   Offset 0: Windows HANDLE from FindFirstFileA (8 bytes)
//   Offset 8: WIN32_FIND_DATAA struct (320 bytes)
//
// This allows the stdlib to use a simple handle without managing the find_data buffer.

const FIND_HANDLE_SIZE: i64 = 328;
const FIND_DATA_OFFSET: i64 = 8;
const FILENAME_OFFSET: i64 = 8 + 44; // find_data offset + cFileName offset

/// __find_first_file(pattern cstring) returns ptr
/// Allocates handle, calls FindFirstFileA. Returns handle or 0 on failure.
fn intrinsicFindFirstFile(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const pattern_arg = try self.convertExpression(call.args[0]);
    const pattern_ptr = try self.func().emitLoad(pattern_arg.value, .ptr);

    // Allocate handle struct on heap (328 bytes)
    const heap = try emitGetProcessHeap(self);
    const size = try self.func().emitConstI64(FIND_HANDLE_SIZE);
    const handle_ptr = try emitHeapAllocWin(self, heap, size);

    // Get pointer to find_data at offset 8
    const find_data_offset = try self.func().emitConstI64(FIND_DATA_OFFSET);
    const find_data_ptr = try self.func().emitBinaryOp(.add, handle_ptr, find_data_offset, .ptr);

    // Call FindFirstFileA
    const win_handle = try externCall(self, "kernel32.dll", "FindFirstFileA", &.{ pattern_ptr, find_data_ptr }, .ptr);

    // Store Windows handle at offset 0
    try self.func().emitStore(handle_ptr, win_handle);

    // Check for INVALID_HANDLE_VALUE (-1) and return 0 if invalid
    const invalid_handle = try self.func().emitConstI64(-1);
    const is_valid = try self.func().emitBinaryOp(.icmp_ne, win_handle, invalid_handle, .i64);

    // Multiply handle_ptr by is_valid (0 or 1) to get result
    const result = try self.func().emitBinaryOp(.mul, handle_ptr, is_valid, .i64);

    return .{ .value = result, .ty = .{ .primitive = "ptr" } };
}

/// __find_next_file(handle ptr) returns int
/// Calls FindNextFileA using the handle. Returns non-zero on success, 0 when no more files.
fn intrinsicFindNextFile(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_local = handle_arg.value;

    // Load the opaque handle pointer (heap-allocated struct)
    const handle_struct = try self.func().emitLoad(handle_local, .ptr);

    // Load Windows handle from offset 0 of the handle struct
    const win_handle = try self.func().emitLoad(handle_struct, .ptr);

    // Get pointer to find_data at offset 8 of the handle struct
    const find_data_offset = try self.func().emitConstI64(FIND_DATA_OFFSET);
    const find_data_ptr = try self.func().emitBinaryOp(.add, handle_struct, find_data_offset, .ptr);

    // Call FindNextFileA (returns BOOL which is i32, sign-extend to i64)
    const result_i32 = try externCall(self, "kernel32.dll", "FindNextFileA", &.{ win_handle, find_data_ptr }, .i32);
    const result = try self.func().emitUnaryOp(.sext_i32_i64, result_i32, .i64);

    return .{ .value = result, .ty = .{ .primitive = "int" } };
}

/// __find_close(handle ptr) returns int
/// Calls FindClose and frees the handle memory.
fn intrinsicFindClose(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_local = handle_arg.value;

    // Load the opaque handle pointer (heap-allocated struct)
    const handle_struct = try self.func().emitLoad(handle_local, .ptr);

    // Load Windows handle from offset 0 of the handle struct
    const win_handle = try self.func().emitLoad(handle_struct, .ptr);

    // Call FindClose
    const close_result = try externCall(self, "kernel32.dll", "FindClose", &.{win_handle}, .i64);

    // Free the handle struct memory
    const heap = try emitGetProcessHeap(self);
    const zero = try self.func().emitConstI64(0);
    _ = try externCall(self, "kernel32.dll", "HeapFree", &.{ heap, zero, handle_struct }, .i64);

    return .{ .value = close_result, .ty = .{ .primitive = "int" } };
}

/// __find_filename(handle ptr) returns cstring
/// Gets current filename (cFileName) from the handle's find_data.
/// Returns a cstring struct (data_ptr + length + managed)
fn intrinsicFindFilename(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_local = handle_arg.value;

    // Load the opaque handle pointer (heap-allocated struct)
    const handle_struct = try self.func().emitLoad(handle_local, .ptr);

    // cFileName is at offset 8 (find_data) + 44 (cFileName in WIN32_FIND_DATAA) = 52
    const offset = try self.func().emitConstI64(FILENAME_OFFSET);
    const filename_ptr = try self.func().emitBinaryOp(.add, handle_struct, offset, .ptr);

    // Compute string length using lstrlenA (returns i32, we sign-extend to i64)
    const strlen_i32 = try externCall(self, "kernel32.dll", "lstrlenA", &.{filename_ptr}, .i32);
    const strlen = try self.func().emitUnaryOp(.sext_i32_i64, strlen_i32, .i64);

    // Allocate cstring struct on stack (24 bytes: data_ptr + length + managed)
    const cstr = try self.func().emitAllocaSized(24);

    // Store data pointer at offset 0
    try self.func().emitStore(cstr.raw(), filename_ptr);

    // Store length at offset 8
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(cstr.raw()), 8, strlen);

    // Store null managed pointer at offset 16 (we don't own this memory)
    const null_ptr = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(cstr.raw()), 16, null_ptr);

    return .{ .value = cstr.raw(), .ty = .{ .primitive = "cstring" } };
}

/// __get_file_attributes(path cstring) returns int
/// Calls GetFileAttributesA. Returns attributes or -1 on failure.
fn intrinsicGetFileAttributes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_arg = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_arg.value, .ptr);

    const result = try externCall(self, "kernel32.dll", "GetFileAttributesA", &.{path_ptr}, .i64);
    return .{ .value = result, .ty = .{ .primitive = "int" } };
}

/// Generate IR to check if path is a directory
/// Uses GetFileAttributesA
fn emitDirectoryExistsIR(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GetFileAttributesA returns DWORD, INVALID_FILE_ATTRIBUTES (0xFFFFFFFF) on failure
    const attrs = try externCall(self, "kernel32.dll", "GetFileAttributesA", &.{path_ptr}, .i64);

    // Check if not INVALID_FILE_ATTRIBUTES
    const invalid_attrs = try self.func().emitConstI64(@as(i64, @bitCast(@as(u64, 0xFFFFFFFF))));
    const is_valid = try self.func().emitBinaryOp(.icmp_ne, attrs, invalid_attrs, .i64);

    // Check if FILE_ATTRIBUTE_DIRECTORY bit (0x10) is set
    const dir_flag = try self.func().emitConstI64(0x10);
    const masked = try self.func().emitBinaryOp(.band, attrs, dir_flag, .i64);
    const is_dir = try self.func().emitBinaryOp(.icmp_ne, masked, try self.func().emitConstI64(0), .i64);

    // Result: valid AND is_directory
    const result = try self.func().emitBinaryOp(.mul, is_valid, is_dir, .i64);

    return .{ .value = result, .ty = .{ .primitive = "bool" } };
}

// ============================================================================
// Command Line Arguments (generates IR instead of calling x86-64 runtime)
// ============================================================================

/// Generate IR to get command line arguments as Array$String
/// This replaces the old __get_command_args x86-64 runtime function
///
/// Array$String layout (40 bytes):
///   [0]  __ManagedArray managed (32 bytes)
///   [32] int iterIndex (8 bytes)
///
/// String layout (40 bytes):
///   [0]  __ManagedArray _managed (32 bytes)
///   [32] int _iterPos (8 bytes)
///
/// __ManagedArray layout (32 bytes):
///   [0]  ptr buffer (8 bytes)
///   [8]  i64 len (8 bytes)
///   [16] i64 capacity (8 bytes)
///   [24] i32 flags (4 bytes) - mode flags (bits 0-1: 0=SSO, 1=refcounted, 2=slice)
///   [28] i32 parent_off (4 bytes)
pub fn emitGetCommandArgs(self: *AstToIr) ConvertError!ir.Value {
    // Get process heap (needed for allocations)
    const heap = try emitGetProcessHeap(self);

    // Allocate argc storage on stack (8 bytes for i32 result, aligned)
    const argc_ptr = try self.func().emitAllocaSized(8);

    // Get command line and parse to argv
    const cmdline_w = try emitGetCommandLineW(self);
    const argv_w = try emitCommandLineToArgvW(self, cmdline_w, argc_ptr.raw());

    // Load argc (it's stored as i32 but we need i64)
    const argc_i32 = try self.func().emitLoad(argc_ptr.raw(), .i32);
    const argc = try self.func().emitUnaryOp(.sext_i32_i64, argc_i32, .i64);

    // Allocate Array$String struct (40 bytes: 32 for __ManagedArray + 8 for iterIndex)
    const array_size = try self.func().emitConstI64(Array.SIZE);
    const result_ptr = try emitHeapAllocWin(self, heap, array_size);

    // Allocate strings array: argc * 40 bytes (each String is 40 bytes)
    const string_size = try self.func().emitConstI64(String.SIZE);
    const strings_alloc_size = try self.func().emitBinaryOp(.mul, argc, string_size, .i64);
    const strings_buf = try emitHeapAllocWin(self, heap, strings_alloc_size);

    // Initialize Array$String's __ManagedArray part (first 32 bytes):
    // Mode 0 for regular arrays (not refcounted)
    const zero_i32 = try self.func().emitConstI32(0);
    try struct_helpers.initManagedArray(self.func(), ir.toManagedArrayPtr(result_ptr), ir.toRawPtr(strings_buf), argc, argc, zero_i32);

    // Initialize iterIndex at offset 32
    const zero_i64 = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(result_ptr), Array.ITER_INDEX_OFFSET, zero_i64);

    // Loop counter on stack
    const i_ptr = try self.func().emitAllocaSized(8);
    try self.func().emitStore(i_ptr.raw(), zero_i64);

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
    const i_val = try self.func().emitLoad(i_ptr.raw(), .i64);
    const cond = try self.func().emitBinaryOp(.icmp_lt, i_val, argc, .i64);

    // Create body block
    const body_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmd_args_body");

    // Now in body block - convert argv_w[i] to UTF-8 String
    const i_body = try self.func().emitLoad(i_ptr.raw(), .i64);

    // Get argv_w[i] pointer (each pointer is 8 bytes)
    const argv_i_ptr_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(argv_w), i_body, 8);
    const wide_str = try self.func().emitLoad(argv_i_ptr_ptr.raw(), .ptr);

    // Get required UTF-8 buffer size (pass 0 for dest and size to query)
    const utf8_size = try emitWideCharToMultiByte(self, wide_str, zero_i64, zero_i64);

    // Allocate UTF-8 buffer with refcount header for String
    const utf8_buf = try struct_helpers.emitAllocRefcountedBuffer(self.func(), utf8_size, "string buffer");

    // Convert to UTF-8
    _ = try emitWideCharToMultiByte(self, wide_str, utf8_buf.raw(), utf8_size);

    // Calculate String element address: strings_buf + i * 40
    const string_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(strings_buf), i_body, String.SIZE);

    // len = utf8_size - 1 (exclude null terminator)
    const one_i64 = try self.func().emitConstI64(1);
    const len_i64 = try self.func().emitBinaryOp(.sub, utf8_size, one_i64, .i64);
    try initHeapString(self, string_ptr.raw(), utf8_buf.raw(), len_i64);

    // Increment i
    const new_i = try self.func().emitBinaryOp(.add, i_body, one_i64, .i64);
    try self.func().emitStore(i_ptr.raw(), new_i);

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
