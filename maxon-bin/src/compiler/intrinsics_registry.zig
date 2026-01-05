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
    /// Only available in stdlib code (e.g., __managed_array_*, __string_*)
    stdlib_only,
};

/// Code generation strategy for an intrinsic
pub const IntrinsicCodegen = enum {
    /// Single unary IR operation (e.g., sqrt -> fsqrt)
    unary_op,
    /// Custom code generation handler required
    custom,
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

pub const file_intrinsics = [_]Intrinsic{
    .{
        .name = "__read_file",
        .params = &cstring_param,
        .return_type_name = "string?",
        .return_ir_type = .ptr,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Reads the entire contents of a file. Returns nil if the file cannot be read.",
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
    .{
        .name = "__list_directory",
        .params = &cstring_param,
        .return_type_name = "Array$String?",
        .return_ir_type = .ptr,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Lists files and directories in a directory. Returns nil if the directory cannot be read.",
    },
    .{
        .name = "__is_directory",
        .params = &cstring_param,
        .return_type_name = "int",
        .return_ir_type = .i64,
        .visibility = .stdlib_only,
        .codegen = .custom,
        .help_text = "Checks if a path is a directory. Returns 1 if true, 0 if false.",
    },
};

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
    return std.mem.startsWith(u8, name, "__managed_array_") or
        std.mem.startsWith(u8, name, "__string_") or
        std.mem.startsWith(u8, name, "__cstring_") or
        std.mem.startsWith(u8, name, "__make_char_") or
        std.mem.startsWith(u8, name, "__read_file") or
        std.mem.startsWith(u8, name, "__write_file") or
        std.mem.startsWith(u8, name, "__list_directory") or
        std.mem.startsWith(u8, name, "__is_directory");
}
