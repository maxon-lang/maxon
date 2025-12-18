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
