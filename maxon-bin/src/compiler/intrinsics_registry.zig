const std = @import("std");
const ir = @import("ir.zig");

// ============================================================================
// Central Intrinsics Registry
// ============================================================================
//
// This module defines all compiler intrinsics in one place. Both semantic
// analysis (for type checking and LSP) and code generation use this registry.

/// Parameter definition for an intrinsic
pub const IntrinsicParam = struct {
    name: []const u8,
    type_name: []const u8,
};

/// Visibility of an intrinsic
pub const IntrinsicVisibility = enum {
    /// Available to all user code (e.g., math builtins like trunc, sqrt)
    public,
    /// Only available in stdlib code (e.g., __managed_memory_*, __string_*)
    stdlib_only,
};

/// Code generation strategy for an intrinsic
pub const IntrinsicCodegen = enum {
    /// Single unary IR operation (e.g., sqrt -> fsqrt)
    unary_op,
    /// Custom code generation handler required
    custom,
    /// Managed memory intrinsics (__managed_memory_*) - unified for arrays and strings
    managed_memory,
    /// C-string intrinsics (__cstring_*)
    cstring,
    /// Character creation intrinsics (__make_char_*)
    make_char,
    /// File I/O intrinsics (__read_file, __write_file, etc.)
    file_io,
    /// Process execution intrinsics (__process_*)
    process,
    /// Command line argument intrinsics (__command_line_*)
    command_line,
};

/// Intrinsic definition
pub const Intrinsic = struct {
    name: []const u8,
    params: []const IntrinsicParam,
    return_type_name: []const u8,
    return_ir_type: ir.Type,
    visibility: IntrinsicVisibility,
    codegen: IntrinsicCodegen,
    /// For unary_op codegen: the IR operation to emit
    ir_op: ?ir.Instruction.Op = null,
    /// For unary_op codegen: the expected argument IR type
    arg_ir_type: ?ir.Type = null,
    /// Help text displayed on hover in IDE
    help_text: []const u8 = "",
};

// ============================================================================
// Math Builtins (public)
// ============================================================================

const float_param = [_]IntrinsicParam{.{ .name = "value", .type_name = "float" }};

pub const math_intrinsics = [_]Intrinsic{
    .{
        .name = "trunc",
        .params = &float_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .fptosi,
        .arg_ir_type = .f64,
        .help_text = "Truncates a float to an integer by removing the decimal portion.\n\nExample: `trunc(3.7)` returns `3`, `trunc(-2.9)` returns `-2`",
    },
    .{
        .name = "abs",
        .params = &float_param,
        .return_type_name = "float",
        .return_ir_type = .f64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .fabs,
        .arg_ir_type = .f64,
        .help_text = "Returns the absolute value of a float.\n\nExample: `abs(-5.5)` returns `5.5`",
    },
    .{
        .name = "sqrt",
        .params = &float_param,
        .return_type_name = "float",
        .return_ir_type = .f64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .fsqrt,
        .arg_ir_type = .f64,
        .help_text = "Returns the square root of a float.\n\nExample: `sqrt(16.0)` returns `4.0`",
    },
    .{
        .name = "ceil",
        .params = &float_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .fceil,
        .arg_ir_type = .f64,
        .help_text = "Rounds a float up to the nearest integer.\n\nExample: `ceil(3.2)` returns `4`, `ceil(-2.9)` returns `-2`",
    },
    .{
        .name = "floor",
        .params = &float_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .ffloor,
        .arg_ir_type = .f64,
        .help_text = "Rounds a float down to the nearest integer.\n\nExample: `floor(3.7)` returns `3`, `floor(-2.1)` returns `-3`",
    },
    .{
        .name = "round",
        .params = &float_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .public,
        .codegen = .unary_op,
        .ir_op = .fround,
        .arg_ir_type = .f64,
        .help_text = "Rounds a float to the nearest integer (half away from zero).\n\nExample: `round(3.5)` returns `4`, `round(3.4)` returns `3`",
    },
};

// ============================================================================
// File I/O Intrinsics (stdlib-only)
// ============================================================================

const cstring_param = [_]IntrinsicParam{.{ .name = "path", .type_name = "cstring" }};
const write_file_params = [_]IntrinsicParam{
    .{ .name = "path", .type_name = "cstring" },
    .{ .name = "data", .type_name = "array" },
};
const handle_param = [_]IntrinsicParam{.{ .name = "handle", .type_name = "ptr" }};
const size_param = [_]IntrinsicParam{.{ .name = "size", .type_name = "int" }};

pub const file_intrinsics = [_]Intrinsic{
    .{
        .name = "__read_file",
        .params = &cstring_param,
        .return_type_name = "String?",
        .return_ir_type = .ptr,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Reads the entire contents of a file. Throws if the file cannot be read.",
    },
    .{
        .name = "__write_file",
        .params = &write_file_params,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Writes data to a file. Returns 0 on success, -1 on failure.",
    },
    .{
        .name = "__write_file_binary",
        .params = &write_file_params,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Writes binary data to a file. Returns 0 on success, -1 on failure.",
    },
    // Directory intrinsics - opaque handle API
    // The handle contains both the Windows HANDLE and the WIN32_FIND_DATAA buffer
    .{
        .name = "__find_first_file",
        .params = &cstring_param,
        .return_type_name = "ptr",
        .return_ir_type = .ptr,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Calls FindFirstFileA. Allocates find_data internally. Returns opaque handle or 0 on failure.",
    },
    .{
        .name = "__find_next_file",
        .params = &handle_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Calls FindNextFileA on the handle. Returns non-zero on success, 0 when no more files.",
    },
    .{
        .name = "__find_close",
        .params = &handle_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Calls FindClose and frees the handle.",
    },
    .{
        .name = "__find_filename",
        .params = &handle_param,
        .return_type_name = "cstring",
        .return_ir_type = .ptr,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Gets current filename (cFileName) from the handle.",
    },
    .{
        .name = "__get_file_attributes",
        .params = &cstring_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Calls GetFileAttributesA. Returns attributes or -1 (INVALID_FILE_ATTRIBUTES) on failure.",
    },
    .{
        .name = "__directory_exists",
        .params = &cstring_param,
        .return_type_name = "bool",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Returns true if path exists and is a directory.",
    },
};

// ============================================================================
// Intrinsic Categories (prefix-based dispatch)
// ============================================================================

/// Category for prefix-based intrinsic lookup
pub const IntrinsicCategory = struct {
    prefix: []const u8,
    visibility: IntrinsicVisibility,
    codegen: IntrinsicCodegen,
};

/// All intrinsic categories - used for dispatch in convertBuiltin
pub const intrinsic_categories = [_]IntrinsicCategory{
    .{ .prefix = "__managed_memory_", .visibility = .stdlib_only, .codegen = .managed_memory },
    .{ .prefix = "__map_get_init_", .visibility = .stdlib_only, .codegen = .managed_memory },
    .{ .prefix = "__element_size", .visibility = .stdlib_only, .codegen = .managed_memory },
    .{ .prefix = "__cstring_", .visibility = .stdlib_only, .codegen = .cstring },
    .{ .prefix = "__make_char_", .visibility = .stdlib_only, .codegen = .make_char },
    .{ .prefix = "__file_", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__write_file", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__find_first_file", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__find_next_file", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__find_close", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__find_filename", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__get_file_attributes", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__directory_exists", .visibility = .stdlib_only, .codegen = .file_io },
    .{ .prefix = "__process_", .visibility = .stdlib_only, .codegen = .process },
    .{ .prefix = "__command_line_", .visibility = .stdlib_only, .codegen = .command_line },
};

/// Find a category by name prefix
pub fn findCategory(name: []const u8) ?*const IntrinsicCategory {
    for (&intrinsic_categories) |*cat| {
        if (std.mem.startsWith(u8, name, cat.prefix)) return cat;
    }
    return null;
}

// ============================================================================
// All Intrinsics
// ============================================================================

/// Get all intrinsics as a single slice for iteration
pub fn allIntrinsics() []const Intrinsic {
    // Use comptime to build a combined array
    const combined = math_intrinsics ++ file_intrinsics;
    return &combined;
}

/// Look up an intrinsic by name
pub fn findIntrinsic(name: []const u8) ?*const Intrinsic {
    for (allIntrinsics()) |*intr| {
        if (std.mem.eql(u8, intr.name, name)) {
            return intr;
        }
    }
    return null;
}

/// Check if a name is a math builtin (for code generation)
pub fn isMathBuiltin(name: []const u8) ?*const Intrinsic {
    for (&math_intrinsics) |*intr| {
        if (std.mem.eql(u8, intr.name, name)) {
            return intr;
        }
    }
    return null;
}

/// Check if a function name starts with a known stdlib intrinsic prefix
pub fn isStdlibIntrinsicPrefix(name: []const u8) bool {
    if (findCategory(name)) |cat| {
        return cat.visibility == .stdlib_only;
    }
    return false;
}
