const std = @import("std");
const types = @import("types.zig");

// ============================================================================
// Test Helpers
// Helper functions to build JSON-RPC messages for LSP testing
// ============================================================================

/// Wrapper for a JSON message that tracks allocated strings for proper cleanup
pub const Message = struct {
    parsed: std.json.Parsed(std.json.Value),
    allocated_strings: std.ArrayListUnmanaged([]const u8),

    pub fn deinit(self: *Message, allocator: std.mem.Allocator) void {
        // Free all allocated strings
        for (self.allocated_strings.items) |str| {
            allocator.free(str);
        }
        self.allocated_strings.deinit(allocator);

        // Free the JSON structure (but not strings, we already freed them)
        freeJsonValueWithoutStrings(self.parsed.value, allocator);
    }
};

/// Recursively free a JSON value without freeing strings
fn freeJsonValueWithoutStrings(value: std.json.Value, allocator: std.mem.Allocator) void {
    switch (value) {
        .object => |obj| {
            // Recursively free values in the object
            var it = obj.iterator();
            while (it.next()) |entry| {
                freeJsonValueWithoutStrings(entry.value_ptr.*, allocator);
            }
            // Free the object map itself
            var mutable_obj = obj;
            mutable_obj.deinit();
        },
        .array => |arr| {
            // Recursively free items in the array
            for (arr.items) |item| {
                freeJsonValueWithoutStrings(item, allocator);
            }
            // Free the array itself
            var mutable_arr = arr;
            mutable_arr.deinit();
        },
        .string => {
            // Don't free strings - caller handles them
        },
        else => {},
    }
}

/// Build a JSON object from a struct
fn buildJsonObject(allocator: std.mem.Allocator, value: anytype) !std.json.Value {
    // Serialize to JSON string first
    var list = std.ArrayListUnmanaged(u8){};
    defer list.deinit(allocator);

    try std.json.stringify(value, .{}, list.writer(allocator));

    // Parse back to std.json.Value
    return try std.json.parseFromSlice(std.json.Value, allocator, list.items, .{ .allocate = .alloc_always });
}

/// Build initialize request message
pub fn buildInitializeMessage(allocator: std.mem.Allocator, id: i64) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "initialize" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    try params_obj.put("processId", .{ .null = {} });
    try params_obj.put("rootUri", .{ .null = {} });

    var capabilities_obj = std.json.ObjectMap.init(allocator);
    errdefer capabilities_obj.deinit();
    try params_obj.put("capabilities", .{ .object = capabilities_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build initialized notification message
pub fn buildInitializedMessage(allocator: std.mem.Allocator) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("method", .{ .string = "initialized" });
    try obj.put("params", .{ .object = std.json.ObjectMap.init(allocator) });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build shutdown request message
pub fn buildShutdownMessage(allocator: std.mem.Allocator, id: i64) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "shutdown" });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build exit notification message
pub fn buildExitMessage(allocator: std.mem.Allocator) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("method", .{ .string = "exit" });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/didOpen notification message
pub fn buildDidOpenMessage(allocator: std.mem.Allocator, uri: []const u8, content: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("method", .{ .string = "textDocument/didOpen" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    const content_copy = try allocator.dupe(u8, content);
    try allocated_strings.append(allocator, content_copy);

    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try text_doc_obj.put("languageId", .{ .string = "maxon" });
    try text_doc_obj.put("version", .{ .integer = 1 });
    try text_doc_obj.put("text", .{ .string = content_copy });

    try params_obj.put("textDocument", .{ .object = text_doc_obj });
    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/didChange notification message
pub fn buildDidChangeMessage(allocator: std.mem.Allocator, uri: []const u8, content: []const u8, version: i64) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("method", .{ .string = "textDocument/didChange" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try text_doc_obj.put("version", .{ .integer = version });

    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    // Build contentChanges array
    var changes_array = std.json.Array.init(allocator);
    errdefer changes_array.deinit();

    var change_obj = std.json.ObjectMap.init(allocator);
    errdefer change_obj.deinit();

    const content_copy = try allocator.dupe(u8, content);
    try allocated_strings.append(allocator, content_copy);
    try change_obj.put("text", .{ .string = content_copy });

    try changes_array.append(.{ .object = change_obj });
    try params_obj.put("contentChanges", .{ .array = changes_array });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/didClose notification message
pub fn buildDidCloseMessage(allocator: std.mem.Allocator, uri: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("method", .{ .string = "textDocument/didClose" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });

    try params_obj.put("textDocument", .{ .object = text_doc_obj });
    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/completion request message
pub fn buildCompletionMessage(
    allocator: std.mem.Allocator,
    id: i64,
    uri: []const u8,
    line: u32,
    character: u32,
    trigger_char: ?[]const u8,
) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/completion" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var position_obj = std.json.ObjectMap.init(allocator);
    errdefer position_obj.deinit();
    try position_obj.put("line", .{ .integer = @intCast(line) });
    try position_obj.put("character", .{ .integer = @intCast(character) });
    try params_obj.put("position", .{ .object = position_obj });

    // Add context if trigger character provided
    if (trigger_char) |tc| {
        var context_obj = std.json.ObjectMap.init(allocator);
        errdefer context_obj.deinit();
        try context_obj.put("triggerKind", .{ .integer = 2 }); // trigger_character
        const tc_copy = try allocator.dupe(u8, tc);
    try allocated_strings.append(allocator, tc_copy);
        try context_obj.put("triggerCharacter", .{ .string = tc_copy });
        try params_obj.put("context", .{ .object = context_obj });
    }

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/hover request message
pub fn buildHoverMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, line: u32, character: u32) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/hover" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var position_obj = std.json.ObjectMap.init(allocator);
    errdefer position_obj.deinit();
    try position_obj.put("line", .{ .integer = @intCast(line) });
    try position_obj.put("character", .{ .integer = @intCast(character) });
    try params_obj.put("position", .{ .object = position_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/definition request message
pub fn buildDefinitionMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, line: u32, character: u32) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/definition" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var position_obj = std.json.ObjectMap.init(allocator);
    errdefer position_obj.deinit();
    try position_obj.put("line", .{ .integer = @intCast(line) });
    try position_obj.put("character", .{ .integer = @intCast(character) });
    try params_obj.put("position", .{ .object = position_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/documentSymbol request message
pub fn buildDocumentSymbolMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/documentSymbol" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/formatting request message
pub fn buildFormattingMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, tab_size: u32, insert_spaces: bool) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/formatting" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var options_obj = std.json.ObjectMap.init(allocator);
    errdefer options_obj.deinit();
    try options_obj.put("tabSize", .{ .integer = @intCast(tab_size) });
    try options_obj.put("insertSpaces", .{ .bool = insert_spaces });
    try params_obj.put("options", .{ .object = options_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/foldingRange request message
pub fn buildFoldingRangeMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/foldingRange" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/linkedEditingRange request message
pub fn buildLinkedEditingRangeMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, line: u32, character: u32) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/linkedEditingRange" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var position_obj = std.json.ObjectMap.init(allocator);
    errdefer position_obj.deinit();
    try position_obj.put("line", .{ .integer = @intCast(line) });
    try position_obj.put("character", .{ .integer = @intCast(character) });
    try params_obj.put("position", .{ .object = position_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/rename request message
pub fn buildRenameMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, line: u32, character: u32, new_name: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/rename" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var position_obj = std.json.ObjectMap.init(allocator);
    errdefer position_obj.deinit();
    try position_obj.put("line", .{ .integer = @intCast(line) });
    try position_obj.put("character", .{ .integer = @intCast(character) });
    try params_obj.put("position", .{ .object = position_obj });

    const new_name_copy = try allocator.dupe(u8, new_name);
    try allocated_strings.append(allocator, new_name_copy);
    try params_obj.put("newName", .{ .string = new_name_copy });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/codeAction request message
pub fn buildCodeActionMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8, start_line: u32, start_char: u32, end_line: u32, end_char: u32) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/codeAction" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    var range_obj = std.json.ObjectMap.init(allocator);
    errdefer range_obj.deinit();

    var start_obj = std.json.ObjectMap.init(allocator);
    errdefer start_obj.deinit();
    try start_obj.put("line", .{ .integer = @intCast(start_line) });
    try start_obj.put("character", .{ .integer = @intCast(start_char) });
    try range_obj.put("start", .{ .object = start_obj });

    var end_obj = std.json.ObjectMap.init(allocator);
    errdefer end_obj.deinit();
    try end_obj.put("line", .{ .integer = @intCast(end_line) });
    try end_obj.put("character", .{ .integer = @intCast(end_char) });
    try range_obj.put("end", .{ .object = end_obj });

    try params_obj.put("range", .{ .object = range_obj });

    var context_obj = std.json.ObjectMap.init(allocator);
    errdefer context_obj.deinit();
    var diagnostics_array = std.json.Array.init(allocator);
    errdefer diagnostics_array.deinit();
    try context_obj.put("diagnostics", .{ .array = diagnostics_array });
    try params_obj.put("context", .{ .object = context_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}

/// Build textDocument/semanticTokens/full request message
pub fn buildSemanticTokensFullMessage(allocator: std.mem.Allocator, id: i64, uri: []const u8) !Message {
    var allocated_strings = std.ArrayListUnmanaged([]const u8){};
    errdefer {
        for (allocated_strings.items) |str| allocator.free(str);
        allocated_strings.deinit(allocator);
    }

    var obj = std.json.ObjectMap.init(allocator);
    errdefer obj.deinit();

    try obj.put("jsonrpc", .{ .string = "2.0" });
    try obj.put("id", .{ .integer = id });
    try obj.put("method", .{ .string = "textDocument/semanticTokens/full" });

    // Build params
    var params_obj = std.json.ObjectMap.init(allocator);
    errdefer params_obj.deinit();

    var text_doc_obj = std.json.ObjectMap.init(allocator);
    errdefer text_doc_obj.deinit();

    const uri_copy = try allocator.dupe(u8, uri);
    try allocated_strings.append(allocator, uri_copy);
    try text_doc_obj.put("uri", .{ .string = uri_copy });
    try params_obj.put("textDocument", .{ .object = text_doc_obj });

    try obj.put("params", .{ .object = params_obj });

    return Message{
        .parsed = std.json.Parsed(std.json.Value){
            .arena = undefined,
            .value = .{ .object = obj },
        },
        .allocated_strings = allocated_strings,
    };
}
