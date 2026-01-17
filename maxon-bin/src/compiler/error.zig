const std = @import("std");

/// Normalize a path in-place in a buffer by resolving .. and . components.
/// Returns a slice of the buffer containing the normalized path.
fn normalizePath(path: []const u8, buf: []u8) []const u8 {
    // Handle Windows absolute paths (e.g., "C:\...")
    var start: usize = 0;
    var root_len: usize = 0;
    if (path.len >= 2 and path[1] == ':') {
        if (path.len >= 3 and (path[2] == '\\' or path[2] == '/')) {
            root_len = 3;
        } else {
            root_len = 2;
        }
        start = root_len;
    } else if (path.len >= 1 and (path[0] == '\\' or path[0] == '/')) {
        root_len = 1;
        start = 1;
    }

    // Copy root prefix
    if (root_len > buf.len) return path;
    @memcpy(buf[0..root_len], path[0..root_len]);

    // Determine separator
    const sep: u8 = if (std.mem.indexOf(u8, path, "\\") != null) '\\' else '/';

    // Process path components
    var out_pos: usize = root_len;
    var iter = std.mem.tokenizeAny(u8, path[start..], "\\/");
    var first = true;
    while (iter.next()) |component| {
        if (std.mem.eql(u8, component, "..")) {
            // Go up one directory - find last separator and truncate
            if (out_pos > root_len) {
                out_pos -= 1; // skip past trailing content
                while (out_pos > root_len and buf[out_pos - 1] != sep) {
                    out_pos -= 1;
                }
                if (out_pos > root_len) {
                    out_pos -= 1; // remove separator too
                }
                first = (out_pos == root_len);
            }
        } else if (!std.mem.eql(u8, component, ".")) {
            // Add separator if not first component
            if (!first) {
                if (out_pos >= buf.len) return path;
                buf[out_pos] = sep;
                out_pos += 1;
            }
            first = false;
            // Copy component
            if (out_pos + component.len > buf.len) return path;
            @memcpy(buf[out_pos..][0..component.len], component);
            out_pos += component.len;
        }
    }

    return buf[0..out_pos];
}

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
    E025,
    E026,
    E027,
    E028,
    E029,
    E030, // duplicate enum case
    E031, // duplicate raw value
    E032, // raw value type mismatch
    E033, // rawValue on simple enum
    E034, // unknown enum case
    E035, // wrong binding count in match
    E036, // duplicate block identifier
    E037, // missing return statement
    E042, // missing block identifier
    E043, // block identifier mismatch
    E045, // unknown parameter name
    E046, // positional argument for default parameter
    E047, // duplicate argument
    E048, // positional argument after named argument
    E049, // missing required argument
    E050, // unexported field access
    E051, // unknown interface
    E052, // missing parameter name
    E053, // bracket indexing not supported
    E054, // main cannot throw
    E055, // if try requires throwing expression
    E056, // generic type requires braces
    E057, // throwing function requires try
    E058, // otherwise requires try
    E059, // redundant type annotation

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
        .{ .code = "E017", .message = "reserved" },
        .{ .code = "E018", .message = "internal type used outside stdlib" },
        .{ .code = "E019", .message = "unknown intrinsic" },
        .{ .code = "E020", .message = "cannot modify borrowed string" },
        .{ .code = "E021", .message = "string goes out of scope while borrowed" },
        .{ .code = "E022", .message = "type mismatch" },
        .{ .code = "E023", .message = "Error interface can only be implemented by enums" },
        .{ .code = "E024", .message = "undefined function" },
        .{ .code = "E025", .message = "match fallthrough with return" },
        .{ .code = "E026", .message = "match not exhaustive" },
        .{ .code = "E027", .message = "duplicate pattern in match" },
        .{ .code = "E028", .message = "pattern type mismatch" },
        .{ .code = "E029", .message = "default case must be last" },
        .{ .code = "E030", .message = "duplicate enum case" },
        .{ .code = "E031", .message = "duplicate raw value" },
        .{ .code = "E032", .message = "raw value type mismatch" },
        .{ .code = "E033", .message = "rawValue requires raw value enum" },
        .{ .code = "E034", .message = "unknown enum case" },
        .{ .code = "E035", .message = "wrong binding count" },
        .{ .code = "E036", .message = "duplicate block identifier" },
        .{ .code = "E037", .message = "missing return statement" },
        .{ .code = "E042", .message = "missing block identifier" },
        .{ .code = "E043", .message = "block identifier mismatch" },
        .{ .code = "E045", .message = "unknown parameter name" },
        .{ .code = "E046", .message = "positional argument for parameter with default value" },
        .{ .code = "E047", .message = "duplicate argument" },
        .{ .code = "E048", .message = "All positional arguments must come before named arguments" },
        .{ .code = "E049", .message = "missing required argument" },
        .{ .code = "E050", .message = "cannot access unexported field" },
        .{ .code = "E051", .message = "unknown interface" },
        .{ .code = "E052", .message = "arguments must include parameter name" },
        .{ .code = "E053", .message = "bracket indexing not supported" },
        .{ .code = "E054", .message = "main cannot throw" },
        .{ .code = "E055", .message = "try requires a throwing function" },
        .{ .code = "E056", .message = "generic type instantiation requires '{}'" },
        .{ .code = "E057", .message = "throwing function requires try" },
        .{ .code = "E058", .message = "otherwise requires try expression" },
        .{ .code = "E059", .message = "redundant type annotation" },
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
    file_allocated: bool = false, // Whether file was duplicated and needs to be freed

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
    message_allocated: bool = false,
    caller_location: ?std.builtin.SourceLocation = null,

    /// Format error message: "error E001(file:line): file.maxon:10:5: message"
    /// or for internal errors: "internal error: file.maxon:10:5: message"
    pub fn print(self: CompileError, writer: anytype) !void {
        if (self.code) |code| {
            if (self.caller_location) |loc| {
                const filename = std.fs.path.basename(loc.file);
                try writer.print("error {s}({s}:{d}): ", .{ code.format(), filename, loc.line });
            } else {
                try writer.print("error {s}: ", .{code.format()});
            }
        } else {
            try writer.writeAll("internal error: ");
        }
        if (self.location.file) |file| {
            var path_buf: [std.fs.max_path_bytes]u8 = undefined;
            const normalized = normalizePath(file, &path_buf);
            try writer.print("{s}:", .{normalized});
        }
        try writer.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }

    /// Format error message to stderr
    pub fn printToStderr(self: CompileError) void {
        if (self.code) |code| {
            if (self.caller_location) |loc| {
                const filename = std.fs.path.basename(loc.file);
                std.debug.print("error {s}({s}:{d}): ", .{ code.format(), filename, loc.line });
            } else {
                std.debug.print("error {s}: ", .{code.format()});
            }
        } else {
            std.debug.print("internal error: ", .{});
        }
        if (self.location.file) |file| {
            var path_buf: [std.fs.max_path_bytes]u8 = undefined;
            const normalized = normalizePath(file, &path_buf);
            std.debug.print("{s}:", .{normalized});
        }
        std.debug.print("{d}:{d}: {s}\n", .{ self.location.line, self.location.column, self.message });
    }
};
