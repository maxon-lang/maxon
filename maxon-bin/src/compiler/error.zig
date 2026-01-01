const std = @import("std");

/// Error codes for the Maxon compiler
pub const ErrorCode = enum {
    E001,
    E002,
    E003,
    E004,
    E005,
    E006,
    E007,
    E008,
    E009,
    E010,
    E011,
    E012,
    E013,
    E014,
    E015,
    E016,
    E017,
    E018,
    E019,
    E020,
    E021,
    E022,
    E023,
    E024,

    const Info = struct {
        code: []const u8,
        message: []const u8,
    };

    const table = [_]Info{
        .{ .code = "E001", .message = "invalid token" },
        .{ .code = "E002", .message = "unexpected token" },
        .{ .code = "E003", .message = "expected expression" },
        .{ .code = "E004", .message = "expected newline" },
        .{ .code = "E005", .message = "undefined variable" },
        .{ .code = "E006", .message = "unknown type" },
        .{ .code = "E007", .message = "unknown field" },
        .{ .code = "E008", .message = "use after move" },
        .{ .code = "E009", .message = "cannot assign to immutable variable" },
        .{ .code = "E010", .message = "cannot move from immutable variable" },
        .{ .code = "E011", .message = "wrong argument count" },
        .{ .code = "E012", .message = "break/continue outside loop" },
        .{ .code = "E013", .message = "sized arrays require 'var' declaration" },
        .{ .code = "E014", .message = "unused variable" },
        .{ .code = "E015", .message = "missing interface method" },
        .{ .code = "E016", .message = "stdlib-only intrinsic called from user code" },
        .{ .code = "E017", .message = "nil coalescing requires optional type" },
        .{ .code = "E018", .message = "internal type used outside stdlib" },
        .{ .code = "E019", .message = "unknown intrinsic" },
        .{ .code = "E020", .message = "cannot modify borrowed string" },
        .{ .code = "E021", .message = "string goes out of scope while borrowed" },
        .{ .code = "E022", .message = "type mismatch" },
        .{ .code = "E023", .message = "Error interface can only be implemented by enums" },
        .{ .code = "E024", .message = "undefined function" },
    };

    pub fn format(self: ErrorCode) []const u8 {
        return table[@intFromEnum(self)].code;
    }

    pub fn message(self: ErrorCode) []const u8 {
        return table[@intFromEnum(self)].message;
    }
};

/// Source location in the original source file
pub const SourceLocation = struct {
    file: ?[]const u8,
    line: u32,
    column: u32,

    pub fn init(line: u32, column: u32) SourceLocation {
        return .{ .file = null, .line = line, .column = column };
    }

    pub fn withFile(file: []const u8, line: u32, column: u32) SourceLocation {
        return .{ .file = file, .line = line, .column = column };
    }
};

/// Structured compile error with location and code
/// Internal errors (compiler bugs/limitations) have code = null
pub const CompileError = struct {
    code: ?ErrorCode,
    message: []const u8,
    location: SourceLocation,

    /// Format error message: "error E001: file.maxon:10:5: message"
    /// or for internal errors: "internal error: file.maxon:10:5: message"
    pub fn print(self: CompileError, writer: anytype) !void {
        if (self.code) |code| {
            try writer.print("error {s}: ", .{code.format()});
        } else {
            try writer.writeAll("internal error: ");
        }
        if (self.location.file) |file| {
            try writer.print("{s}:", .{file});
        }
        try writer.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }

    /// Format error message to stderr
    pub fn printToStderr(self: CompileError) void {
        if (self.code) |code| {
            std.debug.print("error {s}: ", .{code.format()});
        } else {
            std.debug.print("internal error: ", .{});
        }
        if (self.location.file) |file| {
            std.debug.print("{s}:", .{file});
        }
        std.debug.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }
};
