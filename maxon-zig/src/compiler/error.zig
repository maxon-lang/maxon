const std = @import("std");

/// Error codes for the Maxon compiler
pub const ErrorCode = enum {
    E001, // Lexer error (invalid token)
    E002, // Parser error (unexpected token)
    E003, // Parser error (expected expression)
    E004, // Parser error (expected newline)
    E005, // Undefined variable
    E006, // Unknown type
    E007, // Unknown field
    E008, // Use after move
    E009, // Immutable assignment
    E010, // Immutable move
    E011, // Wrong argument count
    E012, // Control flow error (break/continue outside loop)
    E013, // Sized array requires var
    E014, // Unused variable

    pub fn format(self: ErrorCode) []const u8 {
        return switch (self) {
            .E001 => "E001",
            .E002 => "E002",
            .E003 => "E003",
            .E004 => "E004",
            .E005 => "E005",
            .E006 => "E006",
            .E007 => "E007",
            .E008 => "E008",
            .E009 => "E009",
            .E010 => "E010",
            .E011 => "E011",
            .E012 => "E012",
            .E013 => "E013",
            .E014 => "E014",
        };
    }

    pub fn message(self: ErrorCode) []const u8 {
        return switch (self) {
            .E001 => "invalid token",
            .E002 => "unexpected token",
            .E003 => "expected expression",
            .E004 => "expected newline",
            .E005 => "undefined variable",
            .E006 => "unknown type",
            .E007 => "unknown field",
            .E008 => "use after move",
            .E009 => "cannot assign to immutable variable",
            .E010 => "cannot move from immutable variable",
            .E011 => "wrong argument count",
            .E012 => "break/continue outside loop",
            .E013 => "sized arrays require 'var' declaration",
            .E014 => "unused variable",
        };
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
pub const CompileError = struct {
    code: ErrorCode,
    message: []const u8,
    location: SourceLocation,

    /// Format error message: "error E001: file.maxon:10:5: message"
    pub fn print(self: CompileError, writer: anytype) !void {
        try writer.print("error {s}: ", .{self.code.format()});
        if (self.location.file) |file| {
            try writer.print("{s}:", .{file});
        }
        try writer.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }

    /// Format error message to stderr
    pub fn printToStderr(self: CompileError) void {
        std.debug.print("error {s}: ", .{self.code.format()});
        if (self.location.file) |file| {
            std.debug.print("{s}:", .{file});
        }
        std.debug.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }
};
