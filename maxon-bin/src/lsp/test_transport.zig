const std = @import("std");
const types = @import("types.zig");

// ============================================================================
// Test Transport
// Mock transport for in-process LSP testing - captures responses instead of
// writing to stdout
// ============================================================================

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

pub const TestTransport = struct {
    allocator: std.mem.Allocator,
    responses: std.ArrayListUnmanaged(Response),
    errors: std.ArrayListUnmanaged(ErrorResponse),
    notifications: std.ArrayListUnmanaged(Notification),

    pub const Response = struct {
        id: ?types.Request.Id,
        result_json: []const u8, // Owned by TestTransport

        pub fn deinit(self: Response, allocator: std.mem.Allocator) void {
            allocator.free(self.result_json);
        }
    };

    pub const ErrorResponse = struct {
        id: ?types.Request.Id,
        code: i32,
        message: []const u8, // Owned by TestTransport

        pub fn deinit(self: ErrorResponse, allocator: std.mem.Allocator) void {
            allocator.free(self.message);
        }
    };

    pub const Notification = struct {
        method: []const u8, // Owned by TestTransport
        params_json: []const u8, // Owned by TestTransport

        pub fn deinit(self: Notification, allocator: std.mem.Allocator) void {
            allocator.free(self.method);
            allocator.free(self.params_json);
        }
    };

    pub fn init(allocator: std.mem.Allocator) TestTransport {
        return .{
            .allocator = allocator,
            .responses = .empty,
            .errors = .empty,
            .notifications = .empty,
        };
    }

    pub fn deinit(self: *TestTransport) void {
        for (self.responses.items) |response| {
            response.deinit(self.allocator);
        }
        self.responses.deinit(self.allocator);

        for (self.errors.items) |err_response| {
            err_response.deinit(self.allocator);
        }
        self.errors.deinit(self.allocator);

        for (self.notifications.items) |notif| {
            notif.deinit(self.allocator);
        }
        self.notifications.deinit(self.allocator);
    }

    /// Write a simple JSON response with a struct result
    pub fn writeResult(self: *TestTransport, id: ?types.Request.Id, result: anytype) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        try jsonStringify(self.allocator, result, &buffer);

        const result_json = try self.allocator.dupe(u8, buffer.items);
        try self.responses.append(self.allocator, .{
            .id = if (id) |request_id| try self.copyRequestId(request_id) else null,
            .result_json = result_json,
        });
    }

    /// Write an error response
    pub fn writeError(self: *TestTransport, id: ?types.Request.Id, code: i32, message: []const u8) !void {
        const message_copy = try self.allocator.dupe(u8, message);
        try self.errors.append(self.allocator, .{
            .id = if (id) |request_id| try self.copyRequestId(request_id) else null,
            .code = code,
            .message = message_copy,
        });
    }

    /// Send a notification (no id)
    pub fn writeNotification(self: *TestTransport, method: []const u8, params: anytype) !void {
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        try jsonStringify(self.allocator, params, &buffer);

        const method_copy = try self.allocator.dupe(u8, method);
        const params_json = try self.allocator.dupe(u8, buffer.items);

        try self.notifications.append(self.allocator, .{
            .method = method_copy,
            .params_json = params_json,
        });
    }

    /// Get the last response (most recent)
    pub fn getLastResponse(self: *TestTransport) ?Response {
        if (self.responses.items.len == 0) return null;
        return self.responses.items[self.responses.items.len - 1];
    }

    /// Get the last error response (most recent)
    pub fn getLastError(self: *TestTransport) ?ErrorResponse {
        if (self.errors.items.len == 0) return null;
        return self.errors.items[self.errors.items.len - 1];
    }

    /// Get the last notification (most recent)
    pub fn getLastNotification(self: *TestTransport) ?Notification {
        if (self.notifications.items.len == 0) return null;
        return self.notifications.items[self.notifications.items.len - 1];
    }

    /// Clear all captured responses, errors, and notifications
    pub fn clear(self: *TestTransport) void {
        for (self.responses.items) |response| {
            response.deinit(self.allocator);
        }
        self.responses.clearRetainingCapacity();

        for (self.errors.items) |err_response| {
            err_response.deinit(self.allocator);
        }
        self.errors.clearRetainingCapacity();

        for (self.notifications.items) |notif| {
            notif.deinit(self.allocator);
        }
        self.notifications.clearRetainingCapacity();
    }

    /// Helper to copy a request ID
    fn copyRequestId(self: *TestTransport, id: types.Request.Id) !types.Request.Id {
        return switch (id) {
            .integer => |i| .{ .integer = i },
            .string => |s| .{ .string = try self.allocator.dupe(u8, s) },
        };
    }
};
