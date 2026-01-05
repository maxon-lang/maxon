const std = @import("std");
const builtin = @import("builtin");
const types = @import("types.zig");

/// Helper to serialize a value to JSON and append to a buffer
fn jsonStringify(allocator: std.mem.Allocator, value: anytype, buffer: *std.ArrayListUnmanaged(u8)) !void {
    var out = std.io.Writer.Allocating.init(allocator);
    defer out.deinit();
    var stringify: std.json.Stringify = .{
        .writer = &out.writer,
        .options = .{},
    };
    try stringify.write(value);
    try buffer.appendSlice(allocator, out.written());
}

// ============================================================================
// JSON-RPC Message Transport
// Handles reading/writing LSP messages over stdin/stdout
// ============================================================================

/// Get the standard input file handle
fn getStdinFile() std.fs.File {
    if (builtin.os.tag == .windows) {
        const handle = std.os.windows.GetStdHandle(std.os.windows.STD_INPUT_HANDLE) catch unreachable;
        return std.fs.File{ .handle = handle };
    } else {
        return std.fs.File{ .handle = std.posix.STDIN_FILENO };
    }
}

/// Get the standard output file handle
fn getStdoutFile() std.fs.File {
    if (builtin.os.tag == .windows) {
        const handle = std.os.windows.GetStdHandle(std.os.windows.STD_OUTPUT_HANDLE) catch unreachable;
        return std.fs.File{ .handle = handle };
    } else {
        return std.fs.File{ .handle = std.posix.STDOUT_FILENO };
    }
}

pub const Transport = struct {
    allocator: std.mem.Allocator,
    stdin: std.fs.File,
    stdout: std.fs.File,

    pub fn init(allocator: std.mem.Allocator) Transport {
        return .{
            .allocator = allocator,
            .stdin = getStdinFile(),
            .stdout = getStdoutFile(),
        };
    }

    /// Read the next JSON-RPC message from stdin
    /// Returns the parsed JSON value, caller owns the memory
    pub fn readMessage(self: *Transport) !std.json.Parsed(std.json.Value) {
        // Read headers until we find Content-Length
        var content_length: ?usize = null;

        // Line buffer for reading headers
        var line_buf: [1024]u8 = undefined;
        var line_pos: usize = 0;

        while (true) {
            // Read one byte at a time to find line endings
            var byte_buf: [1]u8 = undefined;
            const bytes_read = try self.stdin.read(&byte_buf);
            if (bytes_read == 0) return error.EndOfStream;

            const byte = byte_buf[0];

            if (byte == '\n') {
                // Remove trailing \r if present (Windows line endings)
                const line_end = if (line_pos > 0 and line_buf[line_pos - 1] == '\r')
                    line_pos - 1
                else
                    line_pos;

                const line = line_buf[0..line_end];

                // Empty line signals end of headers
                if (line.len == 0) break;

                // Parse Content-Length header
                if (std.mem.startsWith(u8, line, "Content-Length: ")) {
                    const len_str = line["Content-Length: ".len..];
                    content_length = std.fmt.parseInt(usize, len_str, 10) catch continue;
                }

                // Reset for next line
                line_pos = 0;
            } else {
                if (line_pos < line_buf.len) {
                    line_buf[line_pos] = byte;
                    line_pos += 1;
                }
            }
        }

        const len = content_length orelse return error.MissingContentLength;

        // Read the JSON content
        const content = try self.allocator.alloc(u8, len);
        defer self.allocator.free(content);

        var total_read: usize = 0;
        while (total_read < len) {
            const bytes_read = try self.stdin.read(content[total_read..]);
            if (bytes_read == 0) return error.UnexpectedEndOfStream;
            total_read += bytes_read;
        }

        // Parse JSON
        return std.json.parseFromSlice(std.json.Value, self.allocator, content, .{
            .allocate = .alloc_always,
        });
    }

    /// Write a string to stdout
    fn writeAll(self: *Transport, data: []const u8) !void {
        var written: usize = 0;
        while (written < data.len) {
            const n = try self.stdout.write(data[written..]);
            if (n == 0) return error.WriteError;
            written += n;
        }
    }

    /// Write a JSON-RPC response to stdout
    pub fn writeResponse(self: *Transport, response: anytype) !void {
        // Serialize response to JSON
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        try jsonStringify(self.allocator, response, &buffer);

        // Write headers and content
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});
        try self.writeAll(header);
        try self.writeAll(buffer.items);
    }

    /// Write a raw JSON value response
    pub fn writeJsonResponse(self: *Transport, id: ?types.Request.Id, result: std.json.Value) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        const writer = buffer.writer(self.allocator);

        try writer.writeAll("{\"jsonrpc\":\"2.0\",");

        // Write id
        if (id) |request_id| {
            switch (request_id) {
                .integer => |i| try writer.print("\"id\":{d},", .{i}),
                .string => |s| try writer.print("\"id\":\"{s}\",", .{s}),
            }
        } else {
            try writer.writeAll("\"id\":null,");
        }

        // Write result
        try writer.writeAll("\"result\":");
        try jsonStringify(self.allocator, result, &buffer);
        try writer.writeAll("}");

        // Write to stdout
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});
        try self.writeAll(header);
        try self.writeAll(buffer.items);
    }

    /// Write a simple JSON response with a struct result
    pub fn writeResult(self: *Transport, id: ?types.Request.Id, result: anytype) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        const writer = buffer.writer(self.allocator);

        try writer.writeAll("{\"jsonrpc\":\"2.0\",");

        // Write id
        if (id) |request_id| {
            switch (request_id) {
                .integer => |i| try writer.print("\"id\":{d},", .{i}),
                .string => |s| try writer.print("\"id\":\"{s}\",", .{s}),
            }
        } else {
            try writer.writeAll("\"id\":null,");
        }

        // Write result
        try writer.writeAll("\"result\":");
        try jsonStringify(self.allocator, result, &buffer);
        try writer.writeAll("}");

        // Write to stdout
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});
        try self.writeAll(header);
        try self.writeAll(buffer.items);
    }

    /// Write an error response
    pub fn writeError(self: *Transport, id: ?types.Request.Id, code: i32, message: []const u8) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        const writer = buffer.writer(self.allocator);

        try writer.writeAll("{\"jsonrpc\":\"2.0\",");

        // Write id
        if (id) |request_id| {
            switch (request_id) {
                .integer => |i| try writer.print("\"id\":{d},", .{i}),
                .string => |s| try writer.print("\"id\":\"{s}\",", .{s}),
            }
        } else {
            try writer.writeAll("\"id\":null,");
        }

        // Write error
        try writer.print("\"error\":{{\"code\":{d},\"message\":\"{s}\"}}}}", .{ code, message });

        // Write to stdout
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});
        try self.writeAll(header);
        try self.writeAll(buffer.items);
    }

    /// Send a notification (no id)
    pub fn writeNotification(self: *Transport, method: []const u8, params: anytype) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        const writer = buffer.writer(self.allocator);

        try writer.print("{{\"jsonrpc\":\"2.0\",\"method\":\"{s}\",\"params\":", .{method});
        try jsonStringify(self.allocator, params, &buffer);
        try writer.writeAll("}");

        // Write to stdout
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});
        try self.writeAll(header);
        try self.writeAll(buffer.items);
    }
};

/// Parse a request ID from JSON
pub fn parseRequestId(value: std.json.Value) ?types.Request.Id {
    return switch (value) {
        .integer => |i| .{ .integer = i },
        .string => |s| .{ .string = s },
        .number_string => |s| .{ .string = s },
        else => null,
    };
}

/// Get a string field from a JSON object
pub fn getString(obj: std.json.ObjectMap, key: []const u8) ?[]const u8 {
    if (obj.get(key)) |val| {
        return switch (val) {
            .string => |s| s,
            else => null,
        };
    }
    return null;
}

/// Get an integer field from a JSON object
pub fn getInt(obj: std.json.ObjectMap, key: []const u8) ?i64 {
    if (obj.get(key)) |val| {
        return switch (val) {
            .integer => |i| i,
            else => null,
        };
    }
    return null;
}

/// Get an object field from a JSON object
pub fn getObject(obj: std.json.ObjectMap, key: []const u8) ?std.json.ObjectMap {
    if (obj.get(key)) |val| {
        return switch (val) {
            .object => |o| o,
            else => null,
        };
    }
    return null;
}

/// Get an array field from a JSON object
pub fn getArray(obj: std.json.ObjectMap, key: []const u8) ?std.json.Array {
    if (obj.get(key)) |val| {
        return switch (val) {
            .array => |a| a,
            else => null,
        };
    }
    return null;
}
