const std = @import("std");
const ast = @import("ast.zig");
const ir = @import("ir.zig");
const types = @import("ast_to_ir_types.zig");
const TypedValue = types.TypedValue;
const ConvertError = types.ConvertError;
const intrinsics_registry = @import("intrinsics_registry.zig");
const struct_helpers = @import("ir_struct_helpers.zig");

// Import type-specific modules for layouts
const array = @import("ast_to_ir_managed_memory.zig");
const ManagedMemory = array.ManagedMemory;

// Forward reference to main AstToIr module
const ast_to_ir = @import("4-ast_to_ir.zig");
const AstToIr = ast_to_ir.AstToIr;
const DeferredBlocks = ast_to_ir.DeferredBlocks;
const BranchBuilder = ast_to_ir.BranchBuilder;

// ============================================================================
// Common Helper Functions
// ============================================================================

/// Validate argument count and report error if mismatch
fn expectArgCount(self: *AstToIr, call: ast.CallExpr, expected: usize) ConvertError!void {
    if (call.args.len != expected) {
        self.reportError(.E011, call.func_name, @src());
        return error.WrongArgumentCount;
    }
}

/// Common pattern for intrinsics that load a single i64 field and return int
fn intrinsicLoadI64Field(self: *AstToIr, call: ast.CallExpr, offset: i32) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const arg = try self.convertExpression(call.args[0]);
    const value = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(arg.value), offset);
    return .{ .value = value, .ty = .{ .primitive = .int } };
}

/// Common pattern for intrinsics that load a single i32 field (sign-extended) and return int
fn intrinsicLoadI32Field(self: *AstToIr, call: ast.CallExpr, offset: i32) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const arg = try self.convertExpression(call.args[0]);
    const value = try struct_helpers.loadI32AsI64(self.func(), ir.toStructPtr(arg.value), offset);
    return .{ .value = value, .ty = .{ .primitive = .int } };
}

/// Initialize a heap-allocated __ManagedMemory struct (32 bytes for strings with mode=1)
fn initHeapManagedMemory(self: *AstToIr, array_ptr: ir.Value, buffer: ir.Value, len_i64: ir.Value) ConvertError!void {
    // mode = 1 (heap-refcounted)
    const mode_one = try self.func().emitConstI32(1);
    return ManagedMemory.init(self.func(), ir.toManagedMemoryPtr(array_ptr), ir.toRawPtr(buffer), len_i64, len_i64, mode_one);
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
    // Allocate data_size + HEADER_SIZE for header
    const header_size = try self.func().emitConstI64(ManagedMemory.HEADER_SIZE);
    const total_size = try self.func().emitBinaryOp(.add, data_size, header_size, .i64);
    const buffer_with_header = try emitHeapAllocWin(self, heap, total_size);

    // Initialize refcount in header to 1
    try self.func().emitStore(buffer_with_header, try self.func().emitConstI64(1));

    // Return data pointer (header + HEADER_SIZE)
    return self.func().emitBinaryOp(.add, buffer_with_header, header_size, .ptr);
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
/// Internal types like __ManagedMemory can only be used in stdlib code
pub fn isInternalType(type_name: []const u8) bool {
    return type_name.len > 0 and type_name[0] == '_';
}

/// Check if internal type access is allowed, returns error if not
pub fn checkInternalTypeAccess(self: *AstToIr, type_name: []const u8) ConvertError!void {
    if (isInternalType(type_name) and !isStdlibFile(self)) {
        self.reportError(.E018, type_name, @src());
        return error.SemanticError;
    }
}

pub fn convertBuiltin(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    // Check category-based intrinsics first (prefix matching)
    if (intrinsics_registry.findCategory(call.func_name)) |category| {
        // Enforce visibility
        if (category.visibility == .stdlib_only and !isStdlibFile(self)) {
            self.reportError(.E016, call.func_name, @src());
            return error.SemanticError;
        }

        // Dispatch based on codegen strategy
        return switch (category.codegen) {
            .managed_memory => convertManagedMemoryIntrinsic(self, call),
            .cstring => convertCstringIntrinsic(self, call),
            .make_char => convertMakeCharIntrinsic(self, call),
            .file_io => convertFileIntrinsic(self, call),
            .process => convertProcessIntrinsic(self, call),
            .command_line => convertCommandLineIntrinsic(self, call),
            .unary_op, .custom => unreachable, // These use individual lookup below
        };
    }

    // Look up individual intrinsics (math builtins)
    const builtin = intrinsics_registry.isMathBuiltin(call.func_name) orelse return error.NotABuiltin;

    if (call.args.len != 1) {
        self.reportError(.E011, call.func_name, @src());
        return error.WrongArgumentCount;
    }

    const arg = try self.convertExpression(call.args[0]);
    const arg_prim = arg.ty.toIrType();
    const arg_ir_type = builtin.arg_ir_type.?;

    // Get the actual value to use, with implicit int-to-float promotion if needed
    const actual_value = if (arg_prim != arg_ir_type) blk: {
        // Allow int -> float promotion for builtins expecting float
        if (arg_ir_type == .f64 and arg_prim == .i64) {
            break :blk self.func().emitUnaryOp(.sitofp, arg.value, .f64) catch return error.OutOfMemory;
        }
        const msg = std.fmt.allocPrint(self.allocator, "{s}() requires {s} argument", .{ call.func_name, arg_ir_type.toMaxonName() }) catch call.func_name;
        self.reportError(.E022, msg, @src());
        return error.TypeMismatch;
    } else arg.value;

    const result = self.func().emitUnaryOp(builtin.ir_op.?, actual_value, builtin.return_ir_type) catch return error.OutOfMemory;

    return .{ .value = result, .ty = if (types.Primitive.fromString(builtin.return_type_name)) |prim|
        .{ .primitive = prim }
    else
        try self.typeNameToValueType(builtin.return_type_name) };
}

// ============================================================================
// __ManagedMemory Intrinsics (stdlib-only)
// ============================================================================

/// Element info for managed memory operations
const ElemInfo = struct {
    size: i32,
    is_struct: bool,
    is_enum: bool,
    type_name: ?[]const u8,
    /// True if the element type has COW semantics (has_managed_buffer flag set)
    has_managed_buffer: bool = false,
    /// Offset of __ManagedMemory within the struct (for types with has_managed_buffer)
    managed_buffer_offset: i32 = 0,
};

/// Direction for array shift operations
const ShiftDirection = enum { left, right };

/// Get element size and type info from generic_params or current_type_name
/// Used by managed memory intrinsics to determine element size at compile time
/// @param param_name: The generic param to look for ("Element", "Key", or "Value")
/// Returns error if element type cannot be determined - this indicates a compiler bug
fn getElementInfoForParam(self: *AstToIr, param_name: []const u8) ConvertError!ElemInfo {
    // Check generic_params for the specified binding (available in monomorphized methods)
    if (self.generic_params.get(param_name)) |elem_type_name| {
        if (self.type_map.get(elem_type_name)) |type_info| {
            if (type_info == .struct_type) {
                return .{
                    .size = type_info.struct_type.size,
                    .is_struct = true,
                    .is_enum = false,
                    .type_name = elem_type_name,
                    .has_managed_buffer = type_info.struct_type.has_managed_buffer,
                    .managed_buffer_offset = type_info.struct_type.managed_buffer_offset,
                };
            } else if (type_info == .enum_type) {
                return .{
                    .size = type_info.enum_type.arrayElementSize(),
                    .is_struct = false,
                    .is_enum = true,
                    .type_name = elem_type_name,
                };
            }
        }
        // Known type but not a struct or enum - must be a primitive
        if (types.getPrimitiveTypeInfo(elem_type_name)) |prim_info| {
            return .{ .size = prim_info.array_element_size, .is_struct = false, .is_enum = false, .type_name = elem_type_name };
        }
        self.reportInternalError("unknown element type in array intrinsic", @src());
        return error.SemanticError;
    }

    // Cannot determine element type - this is a compiler error
    self.reportInternalError("cannot determine array element type - missing generic param", @src());
    return error.SemanticError;
}

/// Get element size and type info - default "Element" param
fn getElementInfo(self: *AstToIr) ConvertError!ElemInfo {
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
    const elem_info = try getElementInfo(self);
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
    self.reportError(.E019, call.func_name, @src());
    return error.SemanticError;
}

const managed_memory_intrinsics = .{
    .{ "__managed_memory_len", intrinsicManagedMemoryLen },
    .{ "__managed_memory_capacity", intrinsicManagedMemoryCapacity },
    .{ "__managed_memory_create", intrinsicManagedMemoryCreate },
    .{ "__managed_memory_set_at", intrinsicManagedMemorySetAt },
    .{ "__managed_memory_set_length", intrinsicManagedMemorySetLength },
    .{ "__managed_memory_grow", intrinsicManagedMemoryGrow },
    .{ "__managed_memory_shift_right", intrinsicManagedMemoryShiftRight },
    .{ "__managed_memory_shift_left", intrinsicManagedMemoryShiftLeft },
    .{ "__managed_memory_get_unchecked", intrinsicManagedMemoryGetUnchecked },
    .{ "__element_size", intrinsicElementSize },
    // Map initialization intrinsics - use Key/Value generic params
    .{ "__map_get_init_key", intrinsicMapGetInitKey },
    .{ "__map_get_init_value", intrinsicMapGetInitValue },
    // String-related intrinsics (now unified under __managed_memory)
    .{ "__managed_memory_byte_at", intrinsicManagedMemoryByteAt },
    .{ "__managed_memory_slice", intrinsicManagedMemorySlice },
    .{ "__managed_memory_concat", intrinsicManagedMemoryConcat },
    .{ "__managed_memory_make_unique", intrinsicManagedMemoryMakeUnique },
    .{ "__managed_memory_set_byte", intrinsicManagedMemorySetByte },
    .{ "__managed_memory_to_cstring", intrinsicManagedMemoryToCstring },
    .{ "__managed_memory_from_bytes", intrinsicManagedMemoryFromBytes },
    .{ "__managed_memory_incref", intrinsicManagedMemoryIncref },
    .{ "__managed_memory_refcount", intrinsicManagedMemoryRefcount },
    .{ "__managed_memory_is_refcounted", intrinsicManagedMemoryIsRefcounted },
    .{ "__managed_memory_flags", intrinsicManagedMemoryFlags },
};

fn convertManagedMemoryIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, managed_memory_intrinsics);
}

/// __managed_memory_len(managed) -> int
fn intrinsicManagedMemoryLen(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return intrinsicLoadI64Field(self, call, 8);
}

/// __managed_memory_capacity(managed) -> int
fn intrinsicManagedMemoryCapacity(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return intrinsicLoadI64Field(self, call, 16);
}

/// __managed_memory_create(capacity, elem_size) -> __ManagedMemory
/// Creates a new managed memory with the given capacity and element size (length = capacity)
/// Uses mode=1 (heap-refcounted) with COW semantics - buffer has 8-byte refcount header.
fn intrinsicManagedMemoryCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const capacity = try self.convertExpression(call.args[0]);
    const elem_size = try self.convertExpression(call.args[1]);

    const managed_ptr = try ManagedMemory.alloca(self.func());

    const buf_size = try self.func().emitBinaryOp(.mul, capacity.value, elem_size.value, .i64);
    const buf_ptr = try array.emitAllocRefcountedBuffer(self, buf_size, "array buffer");

    // Initialize with mode=1 (heap-refcounted with COW semantics)
    const mode_heap = try self.func().emitConstI32(array.MODE_HEAP);
    try ManagedMemory.init(self.func(), managed_ptr, buf_ptr, capacity.value, capacity.value, mode_heap);

    // Return as struct type so memcpy works correctly when returning from functions
    return .{ .value = managed_ptr.raw(), .ty = try self.typeNameToValueType("__ManagedMemory") };
}

/// __managed_memory_set_at(managed, index, value) -> void
fn intrinsicManagedMemorySetAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const value = try self.convertExpression(call.args[2]);

    // Use getElementInfo which properly handles all types including byte arrays
    const elem_info = try getElementInfo(self);
    const elem_size = elem_info.size;

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_size);

    if (elem_info.is_struct) {
        // Structs are always passed by pointer, so copy the data regardless of size
        try self.func().emitMemcpy(elem_ptr.asRawPtr(), ir.toRawPtr(value.value), @intCast(elem_size));
    } else if (elem_size == 1) {
        // For byte-sized elements, use i8 store to avoid writing 8 bytes
        try self.func().emitStoreI8(elem_ptr.raw(), value.value);
    } else if (elem_size == 4) {
        // For 32-bit elements, use i32 store
        try self.func().emitStoreI32(elem_ptr.raw(), value.value);
    } else {
        try self.func().emitStore(elem_ptr.raw(), value.value);
    }

    // When storing an element with COW semantics (has_managed_buffer) into an array,
    // we need to incref the buffer header. The buffer header refcount is shared by
    // all copies pointing to the same buffer. Both the source variable and the array
    // slot now reference the buffer, so refcount must be incremented.
    // The source variable may still be cleaned up at scope exit (decref).
    // When the array is destroyed, it will decref each element.
    if (value.ty == .struct_type) {
        const struct_info = value.ty.struct_type;
        // Check for multiple managed fields (nested structs with managed buffers)
        if (struct_info.managed_field_offsets) |offsets| {
            for (offsets) |offset| {
                const managed_ptr = try array.getManagedMemoryPtr(self, value.value, offset);
                try array.emitManagedMemoryIncref(self, managed_ptr, "<array_store nested>");
            }
        } else if (struct_info.has_managed_buffer) {
            const managed_ptr = try array.getManagedMemoryPtr(self, value.value, struct_info.managed_buffer_offset);
            try array.emitManagedMemoryIncref(self, managed_ptr, "<array_store>");
        }
    }

    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_set_length(managed, new_len) -> void
fn intrinsicManagedMemorySetLength(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const new_len = try self.convertExpression(call.args[1]);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(managed.value), 8, new_len.value);
    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_grow(managed, new_capacity) -> void
/// Grows the array buffer, accounting for the 8-byte refcount header.
/// If buffer is null (empty array), allocates new buffer; otherwise reallocates.
fn intrinsicManagedMemoryGrow(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const new_capacity = try self.convertExpression(call.args[1]);

    const elem_info = try getElementInfo(self);
    const elem_size_val = try self.func().emitConstI64(elem_info.size);
    const data_size = try self.func().emitBinaryOp(.mul, new_capacity.value, elem_size_val, .i64);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const zero = try self.func().emitConstI64(0);
    const is_null = try self.func().emitBinaryOp(.icmp_eq, buf_ptr, zero, .i64);

    // Create blocks for branch
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const alloc_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("grow_alloc");
    const realloc_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("grow_realloc");
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("grow_end");

    // Branch: if null, allocate new; else realloc
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_null }, .{ .block_ref = alloc_block_idx }, .{ .block_ref = realloc_block_idx } },
    });

    // Defer blocks: after this, we're back at entry block
    // Order after defer: deferred[2]=grow_alloc, deferred[1]=grow_realloc, deferred[0]=grow_end
    var deferred = try DeferredBlocks.init(self.allocator, 3);
    defer deferred.deinit();
    deferred.deferBlocks(self, 3);

    // Alloc block: allocate new refcounted buffer
    try deferred.restore(self, 2); // Restore grow_alloc block to emit to it
    const new_buf_alloc = try array.emitAllocRefcountedBuffer(self, data_size, "array grow");
    try self.func().emitStore(managed.value, new_buf_alloc.raw());
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(managed.value), 16, new_capacity.value);
    // Set mode to heap (1) since we're now using a refcounted buffer
    const mode_heap = try self.func().emitConstI32(array.MODE_HEAP);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(managed.value), 24, mode_heap);
    try self.func().emitBr(end_block_idx);

    // Realloc block
    try deferred.restore(self, 1); // Restore grow_realloc block
    const header_size = try self.func().emitConstI64(ManagedMemory.HEADER_SIZE);
    const total_size = try self.func().emitBinaryOp(.add, data_size, header_size, .i64);
    const buf_ptr2 = try ManagedMemory.loadBuffer(self.func(), ir.toManagedMemoryPtr(managed.value));
    const header_ptr = try ManagedMemory.getHeaderPtr(self.func(), buf_ptr2);
    const new_header = try self.func().emitHeapRealloc(ir.toRawPtr(header_ptr), total_size, "array grow");
    const new_data_ptr = try self.func().emitBinaryOp(.add, new_header.raw(), header_size, .ptr);
    try self.func().emitStore(managed.value, new_data_ptr);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(managed.value), 16, new_capacity.value);
    try self.func().emitBr(end_block_idx);

    // End block
    try deferred.restore(self, 0); // Restore grow_end block

    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_shift_right(managed, start_index, count) -> void
/// Shifts elements right, iterating backwards to handle overlap
fn intrinsicManagedMemoryShiftRight(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);
    try emitArrayShift(self, managed, start_index, count, .right);
    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_shift_left(managed, start_index, count) -> void
/// Shifts elements left, iterating forwards to handle overlap
fn intrinsicManagedMemoryShiftLeft(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const start_index = try self.convertExpression(call.args[1]);
    const count = try self.convertExpression(call.args[2]);
    try emitArrayShift(self, managed, start_index, count, .left);
    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_get_unchecked(managed, index) -> Element
/// Returns a pointer to the element in the array buffer.
/// For struct elements (including String), returns pointer directly to array buffer.
/// Caller is responsible for copying and incref if needed.
fn intrinsicManagedMemoryGetUnchecked(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = try getElementInfo(self);
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    const type_name = elem_info.type_name orelse {
        self.reportInternalError("element type name not set in array get intrinsic", @src());
        return error.SemanticError;
    };

    if (elem_info.is_struct) {
        return .{ .value = elem_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
    } else if (elem_info.is_enum) {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = try self.typeNameToValueType(type_name) };
    } else if (elem_info.size == 1) {
        // Byte elements: load as i8 (will be auto zero-extended by codegen)
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i8);
        return .{ .value = elem_val, .ty = .{ .primitive = types.Primitive.fromString(type_name) orelse .byte } };
    } else if (elem_info.size == 8) {
        // 8-byte primitives (int, float, bool)
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = types.Primitive.fromString(type_name) orelse .int } };
    } else {
        self.reportInternalError("unsupported element size in array get intrinsic", @src());
        return error.SemanticError;
    }
}

/// __element_size() -> int
/// Returns the element size for the current generic type context
/// Used by Array.maxon to pass explicit element size to __managed_memory_create
fn intrinsicElementSize(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 0);
    const elem_info = try getElementInfo(self);
    const size_val = try self.func().emitConstI64(elem_info.size);
    return .{ .value = size_val, .ty = .{ .primitive = .int } };
}

/// __map_get_init_key(managed, index) -> Key
/// Like __managed_memory_get_unchecked but uses "Key" generic param instead of "Element"
/// For keys with COW semantics: copies the struct and increfs the buffer so the caller owns it
fn intrinsicMapGetInitKey(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = try getElementInfoForParam(self, "Key");
    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    if (elem_info.is_struct) {
        const type_name = elem_info.type_name.?;

        // Keys with COW semantics need special handling: copy struct and incref so caller owns it
        if (elem_info.has_managed_buffer) {
            // Allocate a new struct on the stack
            const new_ptr = try self.func().emitAllocaSized(elem_info.size);
            // Copy the struct
            try self.func().emitMemcpy(new_ptr, elem_ptr.asRawPtr(), @intCast(elem_info.size));
            // Incref the buffer header so caller has their own reference
            const managed_ptr = try array.getManagedMemoryPtr(self, new_ptr.raw(), elem_info.managed_buffer_offset);
            try array.emitManagedMemoryIncref(self, managed_ptr, "<map_key>");
            return .{ .value = new_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
        }

        return .{ .value = elem_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
    } else if (elem_info.is_enum) {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = try self.typeNameToValueType(elem_info.type_name.?) };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = if (elem_info.type_name) |tn| types.Primitive.fromString(tn) orelse .int else .int } };
    }
}

/// __map_get_init_value(managed, index) -> Value
/// Like __managed_memory_get_unchecked but uses "Value" generic param instead of "Element"
/// For values with COW semantics: copies the struct and increfs the buffer so the caller owns it
fn intrinsicMapGetInitValue(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const elem_info = try getElementInfoForParam(self, "Value");

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const elem_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, elem_info.size);

    if (elem_info.is_struct) {
        const type_name = elem_info.type_name.?;

        // Values with COW semantics need special handling: copy struct and incref so caller owns it
        if (elem_info.has_managed_buffer) {
            // Allocate a new struct on the stack
            const new_ptr = try self.func().emitAllocaSized(elem_info.size);
            // Copy the struct
            try self.func().emitMemcpy(new_ptr, elem_ptr.asRawPtr(), @intCast(elem_info.size));
            // Incref the buffer header so caller has their own reference
            const managed_ptr = try array.getManagedMemoryPtr(self, new_ptr.raw(), elem_info.managed_buffer_offset);
            try array.emitManagedMemoryIncref(self, managed_ptr, "<map_value>");
            return .{ .value = new_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
        }

        return .{ .value = elem_ptr.raw(), .ty = try self.typeNameToValueType(type_name) };
    } else if (elem_info.is_enum) {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = try self.typeNameToValueType(elem_info.type_name.?) };
    } else {
        const elem_val = try self.func().emitLoad(elem_ptr.raw(), .i64);
        return .{ .value = elem_val, .ty = .{ .primitive = if (elem_info.type_name) |tn| types.Primitive.fromString(tn) orelse .int else .int } };
    }
}

// ============================================================================
// __ManagedMemory Intrinsics for Strings (stdlib-only)
// ============================================================================
//
// __ManagedMemory memory layout (32 bytes):
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
    return .{ .value = result orelse try self.func().emitConstI64(0), .ty = .{ .primitive = .int } };
}

/// __cstring_to_managed(cstr) -> __ManagedMemory
/// Converts a cstring struct to a __ManagedMemory (string mode)
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

    // Allocate __ManagedMemory struct (32 bytes)
    const array_size = try self.func().emitConstI64(32);
    const array_ptr = try emitHeapAllocWin(self, heap, array_size);

    // Initialize the struct (mode=1 for heap-refcounted)
    try initHeapManagedMemory(self, array_ptr, buffer, len);

    return .{ .value = array_ptr, .ty = try self.typeNameToValueType("__ManagedMemory") };
}

const make_char_intrinsics = .{
    .{ "__make_char_from_bytes", intrinsicMakeCharFromBytes },
};

fn convertMakeCharIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, make_char_intrinsics);
}

/// __make_char_from_bytes(managed, pos, len) -> Character
/// Creates a slice-mode Character struct (32 bytes)
/// For static strings, creates a static-mode character (no refcounting needed)
fn intrinsicMakeCharFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const pos = try self.convertExpression(call.args[1]);
    const len = try self.convertExpression(call.args[2]);

    // Allocate result char on stack BEFORE branching so both paths can use it
    const char_ptr = try ManagedMemory.alloca(self.func());

    // Check if source is STATIC mode - if so, don't incref and create static char
    const source_mode = try ManagedMemory.loadMode(self.func(), ir.toManagedMemoryPtr(managed.value));
    const three = try self.func().emitConstI32(3);
    const is_static = try self.func().emitBinaryOp(.icmp_eq, source_mode, three, .i32);

    // Create blocks - use manual approach since else block has nested block creation
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const static_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("char_static");
    _ = try self.func().addBlock("char_refcounted");
    _ = try self.func().addBlock("char_merge");

    // Defer refcounted and merge blocks (stored[0]=merge, stored[1]=refcounted)
    var deferred = try DeferredBlocks.init(self.allocator, 2);
    defer deferred.deinit();
    deferred.deferBlocks(self, 2);

    // Entry block branches to static or refcounted
    // We'll patch in refcounted index later after restoring it
    // For now, emit conditional branch to static block (current)
    // The refcounted block index will be known after we restore it

    // ===== STATIC BLOCK (current) =====
    // For static strings, just set flags=3 (static), no incref needed
    const static_parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const static_slice_buf = try self.func().emitBinaryOp(.add, static_parent_buf_ptr, pos.value, .ptr);
    try self.func().emitStore(char_ptr.raw(), static_slice_buf);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(char_ptr.raw()), 8, len.value);

    // capacity = 0, flags = 3 (static), parent_off = 0
    const zero_i64 = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(char_ptr.raw()), 16, zero_i64);
    const three_i32 = try self.func().emitConstI32(3);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(char_ptr.raw()), 24, three_i32);
    const zero_i32 = try self.func().emitConstI32(0);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(char_ptr.raw()), 28, zero_i32);

    // Save static block end for later branch patching
    const static_end_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // ===== REFCOUNTED BLOCK =====
    try deferred.restore(self, 1); // restore refcounted
    const refcounted_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Incref the parent buffer - Character slices share ownership with their source
    // NOTE: This creates nested blocks (heap_incref, slice_check, etc.)
    try array.emitManagedMemoryIncref(self, ir.toManagedMemoryPtr(managed.value), "char parent");

    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer pointer (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, pos.value, .ptr);
    try self.func().emitStore(char_ptr.raw(), slice_buf);

    // Store len (offset 8, i64)
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(char_ptr.raw()), 8, len.value);

    // Initialize slice metadata fields with parent_off = source.parent_off + pos
    const source_parent_off_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(managed.value), 28);
    const source_parent_off_i32 = try self.func().emitLoad(source_parent_off_ptr.raw(), .i32);
    const pos_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, pos.value, .i32);
    const new_parent_off = try self.func().emitBinaryOp(.add, source_parent_off_i32, pos_i32, .i32);
    try initSliceArrayFields(self, char_ptr.raw(), new_parent_off);

    // Save refcounted block end for branch
    const refcounted_end_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // ===== MERGE BLOCK =====
    try deferred.restore(self, 0); // restore merge
    const merge_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Now patch in all the branches with correct indices
    // Entry: branch to static or refcounted
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_static }, .{ .block_ref = static_block_idx }, .{ .block_ref = refcounted_block_idx } },
    });

    // Static end: branch to merge
    try self.func().blocks.items[static_end_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
    });

    // Refcounted end: branch to merge
    try self.func().blocks.items[refcounted_end_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
    });

    return .{ .value = char_ptr.raw(), .ty = try self.typeNameToValueType("Character") };
}

/// __managed_memory_byte_at(managed, index) -> byte
/// Returns the byte at the given index in the buffer
fn intrinsicManagedMemoryByteAt(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, 1);
    const byte_val = try self.func().emitLoad(byte_ptr.raw(), .i8);

    return .{ .value = byte_val, .ty = .{ .primitive = .byte } };
}

/// __managed_memory_slice(managed, start, end) -> __ManagedMemory (slice mode)
/// Creates a slice view into the managed memory
/// Returns a 32-byte __ManagedMemory with flags = 2 (slice mode)
/// Layout: buffer(8) + len(8) + capacity(8) + flags(4) + parent_off(4)
///
/// IMPORTANT: If the parent is heap-allocated (mode=1), we incref the parent's
/// buffer. Slices keep the parent alive via this incref. When the slice is
/// cleaned up (decref), no free happens (mode=2), but the parent's eventual
/// cleanup will decref and potentially free the buffer.
fn intrinsicManagedMemorySlice(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
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

    // Allocate result slice on stack BEFORE branching so both paths can use it
    const slice_ptr = try ManagedMemory.alloca(self.func());

    // Check if source is STATIC mode - if so, don't incref and create static slice
    const source_mode = try ManagedMemory.loadMode(self.func(), ir.toManagedMemoryPtr(managed.value));
    const three = try self.func().emitConstI32(3);
    const is_static = try self.func().emitBinaryOp(.icmp_eq, source_mode, three, .i32);

    // Create blocks - use manual approach since else block has nested block creation
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);
    const static_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("slice_static");
    _ = try self.func().addBlock("slice_refcounted");
    _ = try self.func().addBlock("slice_merge");

    // Defer refcounted and merge blocks (stored[0]=merge, stored[1]=refcounted)
    var deferred = try DeferredBlocks.init(self.allocator, 2);
    defer deferred.deinit();
    deferred.deferBlocks(self, 2);

    // ===== STATIC BLOCK (current) =====
    // For static strings, just set flags=3 (static), no incref needed
    const static_parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const static_slice_buf = try self.func().emitBinaryOp(.add, static_parent_buf_ptr, start.value, .ptr);
    try self.func().emitStore(slice_ptr.raw(), static_slice_buf);

    const static_len_i64 = try self.func().emitBinaryOp(.sub, end.value, start.value, .i64);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 8, static_len_i64);

    // capacity = 0, flags = 3 (static), parent_off = 0
    const zero_i64 = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 16, zero_i64);
    const three_i32 = try self.func().emitConstI32(3);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 24, three_i32);
    const zero_i32 = try self.func().emitConstI32(0);
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 28, zero_i32);

    // Save static block end for later branch patching
    const static_end_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // ===== REFCOUNTED BLOCK =====
    try deferred.restore(self, 1); // restore refcounted
    const refcounted_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Incref the parent buffer if it's heap-allocated.
    // This keeps the parent buffer alive as long as slices reference it.
    // NOTE: This creates nested blocks (heap_incref, slice_check, etc.)
    try array.emitManagedMemoryIncref(self, ir.toManagedMemoryPtr(managed.value), "slice parent");

    const parent_buf_ptr = try self.func().emitLoad(managed.value, .ptr);

    // Store slice buffer = parent_buf_ptr + start (offset 0)
    const slice_buf = try self.func().emitBinaryOp(.add, parent_buf_ptr, start.value, .ptr);
    try self.func().emitStore(slice_ptr.raw(), slice_buf);

    // Store len = end - start (offset 8, i64)
    const len_i64 = try self.func().emitBinaryOp(.sub, end.value, start.value, .i64);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(slice_ptr.raw()), 8, len_i64);

    // Initialize slice metadata (flags=2, parent_off=source.parent_off + start)
    const source_parent_off_ptr = try self.func().emitGetFieldPtr(ir.toStructPtr(managed.value), 28);
    const source_parent_off_i32 = try self.func().emitLoad(source_parent_off_ptr.raw(), .i32);
    const start_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, start.value, .i32);
    const new_parent_off = try self.func().emitBinaryOp(.add, source_parent_off_i32, start_i32, .i32);
    try initSliceArrayFields(self, slice_ptr.raw(), new_parent_off);

    // Save refcounted block end for branch
    const refcounted_end_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // ===== MERGE BLOCK =====
    try deferred.restore(self, 0); // restore merge
    const merge_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Now patch in all the branches with correct indices
    // Entry: branch to static or refcounted
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = is_static }, .{ .block_ref = static_block_idx }, .{ .block_ref = refcounted_block_idx } },
    });

    // Static end: branch to merge
    try self.func().blocks.items[static_end_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
    });

    // Refcounted end: branch to merge
    try self.func().blocks.items[refcounted_end_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = merge_block_idx }, .none, .none },
    });

    return .{ .value = slice_ptr.raw(), .ty = try self.typeNameToValueType("__ManagedMemory") };
}

/// __managed_memory_concat(a, b) -> __ManagedMemory
/// Concatenates two managed memorys (strings) into a new heap-allocated array
fn intrinsicManagedMemoryConcat(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
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
    const buf_ptr = try array.emitAllocRefcountedBuffer(self, total_len, "array concat");

    // Copy both arrays into the new buffer
    try self.func().emitMemcpyDynamic(buf_ptr, ir.toRawPtr(a_buf), a_len);
    const dst_offset = try self.func().emitBinaryOp(.add, buf_ptr.raw(), a_len, .ptr);
    try self.func().emitMemcpyDynamic(ir.toRawPtr(dst_offset), ir.toRawPtr(b_buf), b_len);

    // Initialize __ManagedMemory struct (mode=1 for heap-refcounted)
    const result_ptr = try ManagedMemory.alloca(self.func());
    const mode_one = try self.func().emitConstI32(1);
    try ManagedMemory.init(self.func(), result_ptr, buf_ptr, total_len, total_len, mode_one);

    return .{ .value = result_ptr.raw(), .ty = try self.typeNameToValueType("__ManagedMemory") };
}

/// __managed_memory_make_unique(managed) -> __ManagedMemory
/// Creates a mutable copy of the array (COW - copy on write)
fn intrinsicManagedMemoryMakeUnique(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load length and original buffer (offset 8, i64)
    const len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed.value), 8);
    const orig_buf = try self.func().emitLoad(managed.value, .ptr);

    // Allocate with header for refcounting
    const new_buf = try array.emitAllocRefcountedBuffer(self, len, "array buffer");
    try self.func().emitMemcpyDynamic(new_buf, ir.toRawPtr(orig_buf), len);

    // Initialize __ManagedMemory struct (mode=1 for heap-refcounted)
    const result_ptr = try ManagedMemory.alloca(self.func());
    const mode_one2 = try self.func().emitConstI32(1);
    try ManagedMemory.init(self.func(), result_ptr, new_buf, len, len, mode_one2);

    return .{ .value = result_ptr.raw(), .ty = try self.typeNameToValueType("__ManagedMemory") };
}

/// __managed_memory_set_byte(managed, index, byte) -> void
/// Sets a byte at the given index (caller must ensure array is unique)
fn intrinsicManagedMemorySetByte(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const managed = try self.convertExpression(call.args[0]);
    const index = try self.convertExpression(call.args[1]);
    const byte_val = try self.convertExpression(call.args[2]);

    const buf_ptr = try self.func().emitLoad(managed.value, .ptr);
    const byte_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(buf_ptr), index.value, 1);
    const byte_i8 = try self.func().emitUnaryOp(.trunc_i64_i8, byte_val.value, .i8);
    try self.func().emitStoreI8(byte_ptr.raw(), byte_i8);

    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_to_cstring(managed) -> cstring struct
/// Creates a cstring struct from a __ManagedMemory (string).
/// For heap-refcounted mode: shares buffer (already null-terminated), holds reference to managed memory
/// For Slice mode: copies data to new null-terminated buffer (cstring owns its buffer)
fn intrinsicManagedMemoryToCstring(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const cstring_ptr = try self.func().emitAllocaSized(24);

    // Load flags to determine mode
    const flags = try ManagedMemory.loadFlags(self.func(), ir.toManagedMemoryPtr(managed.value));
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

    // Increment refcount in buffer header using primitives
    const incref_buf_ptr = try ManagedMemory.loadBuffer(self.func(), ir.toManagedMemoryPtr(managed.value));
    const new_ref = try ManagedMemory.incrementRefcount(self.func(), incref_buf_ptr);

    // Emit tracking call for incref
    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackIncref("<cstr>", new_ref_i32);
    }

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

    return .{ .value = cstring_ptr.raw(), .ty = try self.typeNameToValueType("cstring") };
}

/// __managed_memory_from_bytes(buffer, length) -> __ManagedMemory
/// Creates a new managed memory (string) from a byte array buffer
fn intrinsicManagedMemoryFromBytes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const buffer = try self.convertExpression(call.args[0]);
    const length = try self.convertExpression(call.args[1]);

    // Allocate with header for refcounting
    const new_buf = try array.emitAllocRefcountedBuffer(self, length.value, "array buffer");

    // Copy data from source buffer
    const src_buf = try self.func().emitLoad(buffer.value, .ptr);
    try self.func().emitMemcpyDynamic(new_buf, ir.toRawPtr(src_buf), length.value);

    // Initialize __ManagedMemory struct (mode=1 for heap-refcounted)
    const result_ptr = try ManagedMemory.alloca(self.func());
    const mode_one = try self.func().emitConstI32(1);
    try ManagedMemory.init(self.func(), result_ptr, new_buf, length.value, length.value, mode_one);

    return .{ .value = result_ptr.raw(), .ty = try self.typeNameToValueType("__ManagedMemory") };
}

// ============================================================================
// Reference Counting Intrinsics for COW Strings
// ============================================================================

/// __managed_memory_incref(managed) -> void
/// Increments the reference count in buffer header for heap-refcounted arrays
fn intrinsicManagedMemoryIncref(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Increment refcount using primitives
    const buf_ptr = try ManagedMemory.loadBuffer(self.func(), ir.toManagedMemoryPtr(managed.value));
    const new_ref = try ManagedMemory.incrementRefcount(self.func(), buf_ptr);

    // Track the incref
    if (self.track_memory) {
        const new_ref_i32 = try self.func().emitUnaryOp(.trunc_i64_i32, new_ref, .i32);
        try self.func().emitTrackIncref("<incref intrinsic>", new_ref_i32);
    }

    return .{ .value = 0, .ty = .{ .primitive = .void } };
}

/// __managed_memory_refcount(managed) -> int
/// Returns the refcount from buffer header (buf_ptr - HEADER_SIZE)
fn intrinsicManagedMemoryRefcount(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    // Load refcount using primitives
    const buf_ptr = try ManagedMemory.loadBuffer(self.func(), ir.toManagedMemoryPtr(managed.value));
    const header_ptr = try ManagedMemory.getHeaderPtr(self.func(), buf_ptr);
    const refcount = try ManagedMemory.loadRefcount(self.func(), header_ptr);

    return .{ .value = refcount, .ty = .{ .primitive = .int } };
}

/// __managed_memory_is_refcounted(managed) -> bool
/// Returns true if the array is heap-refcounted (flags & 0x1 == 1)
fn intrinsicManagedMemoryIsRefcounted(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const managed = try self.convertExpression(call.args[0]);

    const flags = try ManagedMemory.loadFlags(self.func(), ir.toManagedMemoryPtr(managed.value));
    const one = try self.func().emitConstI32(1);
    const is_refcounted = try self.func().emitBinaryOp(.band, flags, one, .i32);

    return .{ .value = is_refcounted, .ty = .{ .primitive = .bool } };
}

/// __managed_memory_flags(managed) -> int
/// Returns the flags field (offset 24, i32)
fn intrinsicManagedMemoryFlags(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
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

    // Get the managed buffer pointer at the correct offset within the struct.
    // For types with __ManagedMemory (String, Array$T), the managed buffer may not be at offset 0.
    const managed_offset: i32 = if (data_source.ty == .struct_type)
        data_source.ty.struct_type.managed_buffer_offset
    else
        0;
    const managed_ptr = try array.getManagedMemoryPtr(self, data_source.value, managed_offset);
    const data_ptr = try self.func().emitLoad(managed_ptr.raw(), .ptr);
    const data_len = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(managed_ptr.raw()), 8);

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

    return .{ .value = result, .ty = .{ .primitive = .int } };
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
// __file_read_all(cstr) -> __ManagedMemory : High-level: reads entire file (null on failure)

/// __file_open_read(cstr) -> int
/// Opens a file for reading. Returns handle or -1 (INVALID_HANDLE_VALUE) on failure.
fn intrinsicFileOpenRead(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, OPEN_EXISTING = 3
    const handle = try emitCreateFileA(self, path_ptr, 0x80000000, 1, 3, 0);
    return .{ .value = handle, .ty = .{ .primitive = .int } };
}

/// __file_open_write(cstr) -> int
/// Opens/creates a file for writing. Returns handle or -1 on failure.
fn intrinsicFileOpenWrite(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // GENERIC_WRITE = 0x40000000, share_mode = 0, CREATE_ALWAYS = 2, FILE_ATTRIBUTE_NORMAL = 0x80
    const handle = try emitCreateFileA(self, path_ptr, 0x40000000, 0, 2, 0x80);
    return .{ .value = handle, .ty = .{ .primitive = .int } };
}

/// __file_size(handle) -> int
/// Gets the size of an open file in bytes.
fn intrinsicFileSize(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_typed = try self.convertExpression(call.args[0]);
    const size = try emitGetFileSize(self, handle_typed.value);
    return .{ .value = size, .ty = .{ .primitive = .int } };
}

/// __file_read(handle, managed_memory, size) -> int
/// Reads from file into managed memory's buffer. Returns bytes read.
fn intrinsicFileRead(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 3);
    const handle_typed = try self.convertExpression(call.args[0]);
    const array_typed = try self.convertExpression(call.args[1]);
    const size_typed = try self.convertExpression(call.args[2]);

    // Get buffer pointer from __ManagedMemory (offset 0)
    const buffer = try self.func().emitLoad(array_typed.value, .ptr);

    // Allocate bytes_read on stack
    const bytes_read_ptr = try self.func().emitAllocaSized(8);
    const zero = try self.func().emitConstI64(0);
    try self.func().emitStore(bytes_read_ptr.raw(), zero);

    // ReadFile(handle, buffer, size, &bytesRead, NULL)
    _ = try emitReadFile(self, handle_typed.value, buffer, size_typed.value, bytes_read_ptr.raw());

    // Return bytes read
    const bytes_read = try self.func().emitLoad(bytes_read_ptr.raw(), .i64);
    return .{ .value = bytes_read, .ty = .{ .primitive = .int } };
}

/// __file_close(handle) -> int
/// Closes a file handle. Returns 0.
fn intrinsicFileClose(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_typed = try self.convertExpression(call.args[0]);
    try emitCloseHandle(self, handle_typed.value);
    return .{ .value = try self.func().emitConstI64(0), .ty = .{ .primitive = .int } };
}

/// __file_delete(cstr) -> int
/// Deletes a file. Returns 0 on success, -1 on failure.
fn intrinsicFileDelete(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_cstr = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_cstr.value, .ptr);

    // Call DeleteFileA(path) - returns non-zero on success, 0 on failure
    const result = try externCall(self, "kernel32.dll", "DeleteFileA", &.{path_ptr}, .i64);

    // Convert Windows convention (non-zero=success) to our convention (0=success, -1=failure)
    // If result is 0 (failure), return -1; else return 0
    const zero = try self.func().emitConstI64(0);
    const is_failure = try self.func().emitBinaryOp(.icmp_eq, result, zero, .i64);
    const neg_one = try self.func().emitConstI64(-1);
    // Return: is_failure * (-1) + (1 - is_failure) * 0 = is_failure * (-1)
    const return_val = try self.func().emitBinaryOp(.mul, is_failure, neg_one, .i64);
    return .{ .value = return_val, .ty = .{ .primitive = .int } };
}

/// __file_read_all(cstr) -> __ManagedMemory
/// High-level intrinsic: reads entire file into a managed memory.
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

    // Allocate __ManagedMemory struct (32 bytes)
    const array_size = try self.func().emitConstI64(32);
    const array_ptr = try emitHeapAllocWin(self, heap, array_size);

    try initHeapManagedMemory(self, array_ptr, buffer, bytes_read);

    // If handle was invalid, return -1 (as int), else return array_ptr
    // This lets Maxon code check: if result == -1 then error
    // Use: result = invalid_handle * is_invalid + array_ptr * (1 - is_invalid)
    const not_invalid = try self.func().emitBinaryOp(.sub, one_i64, is_invalid, .i64);
    const valid_result = try self.func().emitBinaryOp(.mul, array_ptr, not_invalid, .ptr);
    const invalid_result = try self.func().emitBinaryOp(.mul, invalid_handle, is_invalid, .i64);
    const result_ptr = try self.func().emitBinaryOp(.add, valid_result, invalid_result, .ptr);

    return .{ .value = result_ptr, .ty = try self.typeNameToValueType("__ManagedMemory") };
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
    .{ "__file_delete", intrinsicFileDelete },
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

    return .{ .value = result, .ty = .{ .primitive = .ptr } };
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

    return .{ .value = result, .ty = .{ .primitive = .int } };
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

    return .{ .value = close_result, .ty = .{ .primitive = .int } };
}

/// __find_filename(handle ptr) returns cstring
/// Gets current filename (cFileName) from the handle's find_data.
/// Returns a cstring struct (data_ptr + length + managed)
/// The filename is copied to heap memory so the cstring owns its buffer.
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

    // Allocate heap buffer for the filename copy (strlen + 1 for null terminator)
    const one = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, strlen, one, .i64);
    const heap_buf = try self.func().emitHeapAlloc(alloc_size, "filename copy");

    // Copy the filename to heap buffer
    try self.func().emitMemcpyDynamic(heap_buf, ir.toRawPtr(filename_ptr), strlen);

    // Null-terminate the copy
    const end_ptr = try self.func().emitBinaryOp(.add, heap_buf.raw(), strlen, .ptr);
    const zero_byte = try self.func().emitConstI8(0);
    try self.func().emitStoreI8(end_ptr, zero_byte);

    // Allocate cstring struct on stack (24 bytes: data_ptr + length + managed)
    const cstr = try self.func().emitAllocaSized(24);

    // Store heap buffer pointer at offset 0
    try self.func().emitStore(cstr.raw(), heap_buf.raw());

    // Store length at offset 8
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(cstr.raw()), 8, strlen);

    // Store null managed pointer at offset 16 (cstring owns its heap buffer)
    const null_ptr = try self.func().emitConstI64(0);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(cstr.raw()), 16, null_ptr);

    return .{ .value = cstr.raw(), .ty = try self.typeNameToValueType("cstring") };
}

/// __get_file_attributes(path cstring) returns int
/// Calls GetFileAttributesA. Returns attributes or -1 on failure.
fn intrinsicGetFileAttributes(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const path_arg = try self.convertExpression(call.args[0]);
    const path_ptr = try self.func().emitLoad(path_arg.value, .ptr);

    const result = try externCall(self, "kernel32.dll", "GetFileAttributesA", &.{path_ptr}, .i64);
    return .{ .value = result, .ty = .{ .primitive = .int } };
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

    return .{ .value = result, .ty = .{ .primitive = .bool } };
}

// ============================================================================
// Process Intrinsics (stdlib-only)
// ============================================================================
//
// Opaque process handle layout (64 bytes total):
//   Offset  0: hProcess (8 bytes)
//   Offset  8: hThread (8 bytes)
//   Offset 16: hJob (8 bytes)
//   Offset 24: hStdoutRead (8 bytes)
//   Offset 32: hStderrRead (8 bytes)
//   Offset 40: exitCode (8 bytes)
//   Offset 48: hasExited flag (8 bytes)
//   Offset 56: reserved (8 bytes)

const PROCESS_HANDLE_SIZE: i64 = 64;
const PROCESS_HANDLE_HPROCESS: i32 = 0;
const PROCESS_HANDLE_HTHREAD: i32 = 8;
const PROCESS_HANDLE_HJOB: i32 = 16;
const PROCESS_HANDLE_HSTDOUT_READ: i32 = 24;
const PROCESS_HANDLE_HSTDERR_READ: i32 = 32;
const PROCESS_HANDLE_EXIT_CODE: i32 = 40;
const PROCESS_HANDLE_HAS_EXITED: i32 = 48;
const PROCESS_HANDLE_RESERVED: i32 = 56;

// Windows constants
const STARTF_USESTDHANDLES: u32 = 0x00000100;
const GENERIC_READ: u32 = 0x80000000;
const GENERIC_WRITE: u32 = 0x40000000;
const FILE_SHARE_READ: u32 = 0x00000001;
const FILE_SHARE_WRITE: u32 = 0x00000002;
const CREATE_SUSPENDED: u32 = 0x00000004;
const CREATE_NO_WINDOW: u32 = 0x08000000;
const JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: u32 = 0x00002000;
const WAIT_OBJECT_0: u32 = 0;
const WAIT_TIMEOUT: u32 = 258;
const INFINITE: u32 = 0xFFFFFFFF;

// STARTUPINFOA size: 68 bytes on x64
const STARTUPINFOA_SIZE: i64 = 104; // cb(4) + reserved(4) + desktop(8) + title(8) + pos(16) + size(16) + counters(16) + flags(4) + show(2) + reserved2(2) + reserved3(8) + stdin(8) + stdout(8) + stderr(8)
// PROCESS_INFORMATION size: 24 bytes
const PROCESS_INFORMATION_SIZE: i64 = 24;
// SECURITY_ATTRIBUTES size: 24 bytes
const SECURITY_ATTRIBUTES_SIZE: i64 = 24;
// JOBOBJECT_EXTENDED_LIMIT_INFORMATION size: 48 bytes (BasicLimitInformation is 48 bytes, we only need first 48)
const JOBOBJECT_BASIC_LIMIT_INFORMATION_SIZE: i64 = 48;

const process_intrinsics = .{
    .{ "__process_create", intrinsicProcessCreate },
    .{ "__process_wait", intrinsicProcessWait },
    .{ "__process_read_stdout", intrinsicProcessReadStdout },
    .{ "__process_read_stderr", intrinsicProcessReadStderr },
    .{ "__process_get_exit_code", intrinsicProcessGetExitCode },
    .{ "__process_close", intrinsicProcessClose },
};

fn convertProcessIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, process_intrinsics);
}

/// __process_create(cmdLine cstring, cwd cstring) returns int
/// Creates a process with stdout/stderr capture. Returns opaque handle or 0 on failure.
fn intrinsicProcessCreate(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const cmdline_arg = try self.convertExpression(call.args[0]);
    _ = try self.convertExpression(call.args[1]);

    // Load the cstring data pointer
    const cmdline_ptr = try self.func().emitLoad(cmdline_arg.value, .ptr);

    // Allocate process handle struct
    const heap = try emitGetProcessHeap(self);
    const handle_size = try self.func().emitConstI64(PROCESS_HANDLE_SIZE);
    const handle_ptr = try emitHeapAllocWin(self, heap, handle_size);

    // Zero-initialize the handle struct
    const zero = try self.func().emitConstI64(0);
    var i: i32 = 0;
    while (i < 8) : (i += 1) {
        try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), i * 8, zero);
    }

    // Allocate STARTUPINFOA (104 bytes) - simple version without pipes
    const si = try self.func().emitAllocaSized(STARTUPINFOA_SIZE);
    // Zero initialize
    var k: i32 = 0;
    while (k < 13) : (k += 1) {
        try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(si.raw()), k * 8, zero);
    }
    // cb = sizeof(STARTUPINFOA) = 104 (store as i32 at offset 0)
    const si_size_i32 = try self.func().emitConstI32(@intCast(STARTUPINFOA_SIZE));
    try struct_helpers.storeI32Field(self.func(), ir.toStructPtr(si.raw()), 0, si_size_i32);

    // Allocate PROCESS_INFORMATION (24 bytes)
    const pi = try self.func().emitAllocaSized(PROCESS_INFORMATION_SIZE);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(pi.raw()), 0, zero);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(pi.raw()), 8, zero);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(pi.raw()), 16, zero);

    // CreateProcessA(NULL, cmdline, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)
    const create_result = try externCall(self, "kernel32.dll", "CreateProcessA", &.{
        zero, // lpApplicationName = NULL
        cmdline_ptr, // lpCommandLine
        zero, // lpProcessAttributes = NULL
        zero, // lpThreadAttributes = NULL
        zero, // bInheritHandles = FALSE
        zero, // dwCreationFlags = 0
        zero, // lpEnvironment = NULL
        zero, // lpCurrentDirectory = NULL
        si.raw(), // lpStartupInfo
        pi.raw(), // lpProcessInformation
    }, .i64);

    // Load process and thread handles from PROCESS_INFORMATION
    const h_process = try self.func().emitLoad(pi.raw(), .ptr);
    const h_thread_ptr = try self.func().emitConstI64(8);
    const h_thread_addr = try self.func().emitBinaryOp(.add, pi.raw(), h_thread_ptr, .ptr);
    const h_thread = try self.func().emitLoad(h_thread_addr, .ptr);

    // Store in handle struct
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HPROCESS, h_process);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HTHREAD, h_thread);

    // Store create_result (0 on failure) in a reserved field so caller can check
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_RESERVED, create_result);

    // Always return handle_ptr - caller checks hProcess field for 0 to detect failure
    return .{ .value = handle_ptr, .ty = .{ .primitive = .int } };
}

/// __process_wait(handle int, timeoutMs int) returns int
/// Waits for process. Returns: 0=completed, 1=timeout, -1=error
fn intrinsicProcessWait(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 2);
    const handle_arg = try self.convertExpression(call.args[0]);
    const timeout_arg = try self.convertExpression(call.args[1]);

    const handle_ptr = handle_arg.value;
    const timeout_ms = timeout_arg.value;

    // Load hProcess from handle
    const h_process = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HPROCESS);

    // Convert timeout: 0 means INFINITE
    const zero = try self.func().emitConstI64(0);
    const infinite = try self.func().emitConstI64(INFINITE);
    const timeout_is_zero = try self.func().emitBinaryOp(.icmp_eq, timeout_ms, zero, .i64);
    const not_zero = try self.func().emitBinaryOp(.icmp_ne, timeout_ms, zero, .i64);
    const timeout_part = try self.func().emitBinaryOp(.mul, timeout_ms, not_zero, .i64);
    const infinite_part = try self.func().emitBinaryOp(.mul, infinite, timeout_is_zero, .i64);
    const actual_timeout = try self.func().emitBinaryOp(.bitor, timeout_part, infinite_part, .i64);

    // WaitForSingleObject(hProcess, timeout)
    const wait_result = try externCall(self, "kernel32.dll", "WaitForSingleObject", &.{ h_process, actual_timeout }, .i64);

    // If completed, get exit code and store it
    const exit_code_ptr = try self.func().emitAllocaSized(8);
    try self.func().emitStore(exit_code_ptr.raw(), zero);
    _ = try externCall(self, "kernel32.dll", "GetExitCodeProcess", &.{ h_process, exit_code_ptr.raw() }, .i64);
    const exit_code = try self.func().emitLoad(exit_code_ptr.raw(), .i64);

    // Store exit code in handle
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_EXIT_CODE, exit_code);
    const one = try self.func().emitConstI64(1);
    try struct_helpers.storeI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HAS_EXITED, one);

    // Return wait_result for debugging: WAIT_OBJECT_0 (0) = completed, WAIT_TIMEOUT (258) = timeout
    return .{ .value = wait_result, .ty = .{ .primitive = .int } };
}

/// Helper to read from a pipe handle and return a String
/// Simplified version: reads up to 64KB in a single call
fn emitReadPipeToString(self: *AstToIr, pipe_handle: ir.Value) ConvertError!ir.Value {
    const heap = try emitGetProcessHeap(self);
    const zero = try self.func().emitConstI64(0);

    // Allocate buffer (64KB max)
    const buffer_size = try self.func().emitConstI64(65536);
    const buffer = try emitHeapAllocWin(self, heap, buffer_size);

    // bytes_read storage
    const bytes_read_ptr = try self.func().emitAllocaSized(8);
    try self.func().emitStore(bytes_read_ptr.raw(), zero);

    // ReadFile(handle, buffer, size, &bytes_read, NULL)
    _ = try externCall(self, "kernel32.dll", "ReadFile", &.{
        pipe_handle,
        buffer,
        buffer_size,
        bytes_read_ptr.raw(),
        zero,
    }, .i64);

    const bytes_read = try self.func().emitLoad(bytes_read_ptr.raw(), .i64);

    // Allocate refcounted buffer and copy data
    const one = try self.func().emitConstI64(1);
    const alloc_size = try self.func().emitBinaryOp(.add, bytes_read, one, .i64); // +1 for null terminator
    const string_buffer = try array.emitAllocRefcountedBuffer(self, alloc_size, "string buffer");

    // Copy data: use RtlMoveMemory
    _ = try externCall(self, "kernel32.dll", "RtlMoveMemory", &.{ string_buffer.raw(), buffer, bytes_read }, .ptr);

    // Null terminate
    const term_pos = try self.func().emitBinaryOp(.add, string_buffer.raw(), bytes_read, .ptr);
    const zero_byte = try self.func().emitConstI8(0);
    try self.func().emitStoreI8(term_pos, zero_byte);

    // Free temp buffer
    _ = try externCall(self, "kernel32.dll", "HeapFree", &.{ heap, zero, buffer }, .i64);

    // Create __ManagedMemory on the stack with the buffer
    const managed_ptr = try ManagedMemory.alloca(self.func());
    const mode_one = try self.func().emitConstI32(1); // heap-refcounted
    try ManagedMemory.init(self.func(), managed_ptr, string_buffer, bytes_read, bytes_read, mode_one);

    // Create String via stdlib's String$init
    return (try self.emitTypeInit("String", managed_ptr.raw())).ptr;
}

/// __process_read_stdout(handle int) returns String
fn intrinsicProcessReadStdout(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_ptr = handle_arg.value;

    // Load stdout pipe handle
    const stdout_pipe = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HSTDOUT_READ);

    const result = try emitReadPipeToString(self, stdout_pipe);
    return .{ .value = result, .ty = try self.typeNameToValueType("String") };
}

/// __process_read_stderr(handle int) returns String
fn intrinsicProcessReadStderr(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_ptr = handle_arg.value;

    // Load stderr pipe handle
    const stderr_pipe = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HSTDERR_READ);

    const result = try emitReadPipeToString(self, stderr_pipe);
    return .{ .value = result, .ty = try self.typeNameToValueType("String") };
}

/// __process_get_exit_code(handle int) returns int
fn intrinsicProcessGetExitCode(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_ptr = handle_arg.value;

    // Load exit code from handle
    const exit_code = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_EXIT_CODE);

    return .{ .value = exit_code, .ty = .{ .primitive = .int } };
}

/// __process_close(handle int) returns void
fn intrinsicProcessClose(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const handle_arg = try self.convertExpression(call.args[0]);
    const handle_ptr = handle_arg.value;

    // Load all handles
    const h_process = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HPROCESS);
    const h_thread = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HTHREAD);
    const h_job = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HJOB);
    const h_stdout = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HSTDOUT_READ);
    const h_stderr = try struct_helpers.loadI64Field(self.func(), ir.toStructPtr(handle_ptr), PROCESS_HANDLE_HSTDERR_READ);

    // Close all handles
    _ = try externCall(self, "kernel32.dll", "CloseHandle", &.{h_stdout}, .i64);
    _ = try externCall(self, "kernel32.dll", "CloseHandle", &.{h_stderr}, .i64);
    _ = try externCall(self, "kernel32.dll", "CloseHandle", &.{h_thread}, .i64);
    _ = try externCall(self, "kernel32.dll", "CloseHandle", &.{h_process}, .i64);
    _ = try externCall(self, "kernel32.dll", "CloseHandle", &.{h_job}, .i64);

    // Free handle struct
    const heap = try emitGetProcessHeap(self);
    const zero = try self.func().emitConstI64(0);
    _ = try externCall(self, "kernel32.dll", "HeapFree", &.{ heap, zero, handle_ptr }, .i64);

    return .{ .value = zero, .ty = .{ .primitive = .void } };
}

// ============================================================================
// Command Line Intrinsics (stdlib-only)
// ============================================================================
//
// These intrinsics provide low-level access to command line arguments.
// Each call re-parses the command line via Windows APIs - no caching.

const command_line_intrinsics = .{
    .{ "__command_line_count", intrinsicCommandLineCount },
    .{ "__command_line_arg", intrinsicCommandLineArg },
};

fn convertCommandLineIntrinsic(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    return dispatchIntrinsic(self, call, command_line_intrinsics);
}

/// __command_line_count() returns int
/// Returns total argument count (argc) including the executable path.
fn intrinsicCommandLineCount(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 0);

    // Allocate argc storage on stack
    const argc_ptr = try self.func().emitAllocaSized(8);

    // Get command line and parse to argv
    const cmdline_w = try emitGetCommandLineW(self);
    const argv_w = try emitCommandLineToArgvW(self, cmdline_w, argc_ptr.raw());

    // Load argc (stored as i32)
    const argc_i32 = try self.func().emitLoad(argc_ptr.raw(), .i32);
    const argc = try self.func().emitUnaryOp(.sext_i32_i64, argc_i32, .i64);

    // Free argv array
    try emitLocalFree(self, argv_w);

    return .{ .value = argc, .ty = .{ .primitive = .int } };
}

/// __command_line_arg(index int) returns cstring
/// Returns the argument at the given index as a cstring struct.
/// Returns cstring with null data pointer if index is out of bounds.
fn intrinsicCommandLineArg(self: *AstToIr, call: ast.CallExpr) ConvertError!TypedValue {
    try expectArgCount(self, call, 1);
    const index_arg = try self.convertExpression(call.args[0]);
    const index = index_arg.value;

    // Allocate cstring struct on stack (24 bytes: data + length + managed)
    const cstring_ptr = try self.func().emitAllocaSized(24);
    const zero = try self.func().emitConstI64(0);
    // Initialize to null data, zero length, null managed
    try self.func().emitStore(cstring_ptr.raw(), zero);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 8, zero);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 16, zero);

    // Allocate argc storage on stack
    const argc_ptr = try self.func().emitAllocaSized(8);

    // Get command line and parse to argv
    const cmdline_w = try emitGetCommandLineW(self);
    const argv_w = try emitCommandLineToArgvW(self, cmdline_w, argc_ptr.raw());

    // Load argc (stored as i32)
    const argc_i32 = try self.func().emitLoad(argc_ptr.raw(), .i32);
    const argc = try self.func().emitUnaryOp(.sext_i32_i64, argc_i32, .i64);

    // Bounds check: if index >= argc, skip to end
    const in_bounds = try self.func().emitBinaryOp(.icmp_lt, index, argc, .i64);

    // Remember the entry block index before creating new blocks
    const entry_block_idx: u32 = @intCast(self.func().blocks.items.len - 1);

    // Create valid block and emit instructions for valid case
    const valid_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmdarg_valid");

    // Now in valid block: get argv[index] and convert to UTF-8

    // Get argv_w[index] pointer (each pointer is 8 bytes)
    const argv_i_ptr_ptr = try self.func().emitGetElemPtr(ir.toRawPtr(argv_w), index, 8);
    const wide_str = try self.func().emitLoad(argv_i_ptr_ptr.raw(), .ptr);

    // Get required UTF-8 buffer size
    const utf8_size = try emitWideCharToMultiByte(self, wide_str, zero, zero);

    // Allocate UTF-8 buffer using tracked allocator
    const utf8_buf = try self.func().emitHeapAlloc(utf8_size, "command line arg");

    // Convert to UTF-8
    _ = try emitWideCharToMultiByte(self, wide_str, utf8_buf.raw(), utf8_size);

    // Store cstring fields: data = utf8_buf, length = utf8_size - 1 (exclude null), managed = null
    try self.func().emitStore(cstring_ptr.raw(), utf8_buf.raw());
    const one = try self.func().emitConstI64(1);
    const len = try self.func().emitBinaryOp(.sub, utf8_size, one, .i64);
    try struct_helpers.storeI64Field(self.func(), cstring_ptr.asStruct(), 8, len);
    // managed is already zero from initialization

    // Create end block
    const end_block_idx: u32 = @intCast(self.func().blocks.items.len);
    _ = try self.func().addBlock("cmdarg_end");

    // Now emit the branches to tie everything together

    // Entry block: conditional branch to valid or end
    try self.func().blocks.items[entry_block_idx].instructions.append(self.allocator, .{
        .op = .br_cond,
        .operands = .{ .{ .value = in_bounds }, .{ .block_ref = valid_block_idx }, .{ .block_ref = end_block_idx } },
    });

    // Valid block: branch to end
    try self.func().blocks.items[valid_block_idx].instructions.append(self.allocator, .{
        .op = .br,
        .operands = .{ .{ .block_ref = end_block_idx }, .none, .none },
    });

    // Now in end block: free argv and return cstring struct pointer
    try emitLocalFree(self, argv_w);

    return .{ .value = cstring_ptr.raw(), .ty = try self.typeNameToValueType("cstring") };
}
