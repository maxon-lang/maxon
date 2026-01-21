const std = @import("std");
const types = @import("types.zig");
const transport = @import("transport.zig");
const test_transport = @import("test_transport.zig");
const analyzer = @import("analyzer.zig");

// ============================================================================
// LSP Server
// Main server loop handling JSON-RPC messages
// Generic over transport type to support both stdio and testing transports
// ============================================================================

pub fn Server(comptime TransportType: type) type {
    return struct {
        allocator: std.mem.Allocator,
        transport: TransportType,
        analyzer: analyzer.Analyzer,
        initialized: bool = false,
        shutdown_received: bool = false, // Server received shutdown request
        exit_received: bool = false, // Server received exit notification

        const Self = @This();

        pub fn init(allocator: std.mem.Allocator, trans: TransportType) Self {
            return .{
                .allocator = allocator,
                .transport = trans,
                .analyzer = analyzer.Analyzer.init(allocator),
            };
        }

        pub fn deinit(self: *Self) void {
            self.analyzer.deinit();
        }

        /// Run the main server loop
        pub fn run(self: *Self) !void {
            while (!self.exit_received) {
                // Read next message
                var parsed = self.transport.readMessage() catch |err| {
                    if (err == error.EndOfStream) {
                        break;
                    }
                    continue;
                };
                defer parsed.deinit();

                // Handle the message
                self.handleMessage(parsed.value) catch |err| {
                    std.debug.print("Error handling message: {}\n", .{err});
                };
            }
        }

        /// Handle a single JSON-RPC message
        pub fn handleMessage(self: *Self, msg: std.json.Value) !void {
        const obj = switch (msg) {
            .object => |o| o,
            else => return,
        };

        const method = transport.getString(obj, "method") orelse return;
        const id = if (obj.get("id")) |id_val| transport.parseRequestId(id_val) else null;

        // Dispatch based on method
        if (std.mem.eql(u8, method, "initialize")) {
            try self.handleInitialize(id, obj);
        } else if (std.mem.eql(u8, method, "initialized")) {
            // Notification - no response needed
        } else if (std.mem.eql(u8, method, "shutdown")) {
            try self.handleShutdown(id);
        } else if (std.mem.eql(u8, method, "exit")) {
            self.exit_received = true;
        } else if (std.mem.eql(u8, method, "textDocument/didOpen")) {
            try self.handleDidOpen(obj);
        } else if (std.mem.eql(u8, method, "textDocument/didChange")) {
            try self.handleDidChange(obj);
        } else if (std.mem.eql(u8, method, "textDocument/didClose")) {
            try self.handleDidClose(obj);
        } else if (std.mem.eql(u8, method, "textDocument/completion")) {
            try self.handleCompletion(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/hover")) {
            try self.handleHover(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/definition")) {
            try self.handleDefinition(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/documentSymbol")) {
            try self.handleDocumentSymbol(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/formatting")) {
            try self.handleFormatting(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/foldingRange")) {
            try self.handleFoldingRange(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/linkedEditingRange")) {
            try self.handleLinkedEditingRange(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/rename")) {
            try self.handleRename(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/codeAction")) {
            try self.handleCodeAction(id, obj);
        } else if (std.mem.eql(u8, method, "textDocument/semanticTokens/full")) {
            try self.handleSemanticTokensFull(id, obj);
        } else {
            // Unknown method
            if (id != null) {
                try self.transport.writeError(id, types.ErrorCodes.MethodNotFound, "Method not found");
            }
        }
    }

    /// Handle initialize request
            fn handleInitialize(self: *Self, id: ?types.Request.Id, _: std.json.ObjectMap) !void {
        self.initialized = true;

        const result = types.InitializeResult{
            .capabilities = .{
                .textDocumentSync = .{
                    .openClose = true,
                    .change = .full,
                },
                .completionProvider = .{
                    .triggerCharacters = &.{"."},
                    .resolveProvider = false,
                },
                .hoverProvider = true,
                .definitionProvider = true,
                .documentSymbolProvider = true,
                .documentFormattingProvider = true,
                .foldingRangeProvider = true,
                .linkedEditingRangeProvider = true,
                .renameProvider = true,
                .codeActionProvider = true,
                .semanticTokensProvider = .{
                    .legend = .{
                        .tokenTypes = &.{ "keyword", "function", "variable", "type", "string", "number", "comment", "operator", "property", "modifier", "enumMember" },
                        .tokenModifiers = &.{ "declaration", "definition", "readonly" },
                    },
                    .full = true,
                    .range = false,
                },
            },
        };

        try self.transport.writeResult(id, result);
    }

    /// Handle shutdown request
            fn handleShutdown(self: *Self, id: ?types.Request.Id) !void {
        self.shutdown_received = true;
        try self.transport.writeResult(id, null);
    }

    /// Handle textDocument/didOpen notification
            fn handleDidOpen(self: *Self, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse return;
        const text_doc = transport.getObject(params, "textDocument") orelse return;

        const uri = transport.getString(text_doc, "uri") orelse return;
        const text = transport.getString(text_doc, "text") orelse return;
        const version = transport.getInt(text_doc, "version") orelse 0;

        try self.analyzer.openDocument(uri, text, version);
    }

    /// Handle textDocument/didChange notification
    fn handleDidChange(self: *Self, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse return;
        const text_doc = transport.getObject(params, "textDocument") orelse return;

        const uri = transport.getString(text_doc, "uri") orelse return;
        const version = transport.getInt(text_doc, "version") orelse 0;

        // Get content changes
        const changes = transport.getArray(params, "contentChanges") orelse return;
        if (changes.items.len == 0) return;

        // For full sync, just take the last change's text
        const last_change = switch (changes.items[changes.items.len - 1]) {
            .object => |o| o,
            else => return,
        };
        const text = transport.getString(last_change, "text") orelse return;

        try self.analyzer.updateDocument(uri, text, version);
    }

    /// Handle textDocument/didClose notification
    fn handleDidClose(self: *Self, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse return;
        const text_doc = transport.getObject(params, "textDocument") orelse return;
        const uri = transport.getString(text_doc, "uri") orelse return;

        self.analyzer.closeDocument(uri);
    }

    /// Handle textDocument/completion request
    fn handleCompletion(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, types.CompletionList{ .items = &.{} });
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, types.CompletionList{ .items = &.{} });
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, types.CompletionList{ .items = &.{} });
            return;
        };

        const position = transport.getObject(params, "position") orelse {
            try self.transport.writeResult(id, types.CompletionList{ .items = &.{} });
            return;
        };

        const line = @as(u32, @intCast(transport.getInt(position, "line") orelse 0));
        const character = @as(u32, @intCast(transport.getInt(position, "character") orelse 0));

        // Get the context (was this triggered by '.'?)
        var trigger_char: ?[]const u8 = null;
        if (transport.getObject(params, "context")) |ctx| {
            trigger_char = transport.getString(ctx, "triggerCharacter");
        }

        // Get completions
        const items = try self.getCompletions(uri, line, character, trigger_char);
        // Free allocated completions after sending response
        // Note: getCompletions returns &.{} (empty literal) or an allocated slice
        defer if (items.len > 0) self.allocator.free(items);

        const result = types.CompletionList{
            .isIncomplete = false,
            .items = items,
        };

        try self.transport.writeResult(id, result);
    }

    /// Get completions at the given position
    fn getCompletions(self: *Self, uri: []const u8, line: u32, character: u32, trigger_char: ?[]const u8) ![]const types.CompletionItem {
        // If triggered by '.', we need to find what's before the dot
        if (trigger_char != null and std.mem.eql(u8, trigger_char.?, ".")) {
            // Find the expression before the dot
            const expr_before_dot = try self.getExpressionBeforeDot(uri, line, character);

            if (expr_before_dot) |prefix| {
                // If prefix looks like a variable name (starts with lowercase), use getCompletionsForVariable
                // This handles arrays and other types where we need the full ValueType info
                if (prefix.len > 0 and std.ascii.isLower(prefix[0])) {
                    const completions = try self.analyzer.getCompletionsForVariable(uri, prefix);
                    if (completions.len > 0) {
                        return completions;
                    }
                }

                // If prefix looks like a type name (starts with uppercase), get its members
                // This handles cases like "Array." for static methods/constructors
                if (prefix.len > 0 and std.ascii.isUpper(prefix[0])) {
                    const completions = try self.analyzer.getMemberCompletions(uri, prefix);
                    if (completions.len > 0) {
                        return completions;
                    }
                }
            }
        }

        // Default: return empty for now
        return &.{};
    }

    /// Get the expression text before the dot at the given position
    fn getExpressionBeforeDot(self: *Self, uri: []const u8, line: u32, character: u32) !?[]const u8 {
        const doc = self.analyzer.documents.get(uri) orelse return null;

        // Find the line
        var lines = std.mem.splitScalar(u8, doc.content, '\n');
        var current_line: u32 = 0;
        while (lines.next()) |line_content| : (current_line += 1) {
            if (current_line == line) {
                // character is the position after the dot
                // We need to find the identifier before the dot
                // So we need at least 2 characters (one for the identifier, one for the dot)
                if (character < 2) return null;

                // The dot is at character - 1, so the identifier ends at character - 2
                const end_col = @min(character - 1, @as(u32, @intCast(line_content.len)));
                if (end_col == 0) return null;

                // Scan backwards from before the dot to find start of identifier
                var start: usize = end_col;
                while (start > 0) {
                    const c = line_content[start - 1];
                    if (!std.ascii.isAlphanumeric(c) and c != '_') break;
                    start -= 1;
                }

                if (start < end_col) {
                    return line_content[start..end_col];
                }
                return null;
            }
        }

        return null;
    }

    /// Handle textDocument/hover request
    fn handleHover(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const position = transport.getObject(params, "position") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const line = @as(u32, @intCast(transport.getInt(position, "line") orelse 0));
        const character = @as(u32, @intCast(transport.getInt(position, "character") orelse 0));

        if (self.analyzer.getHoverInfo(uri, line, character)) |hover_content| {
            defer self.allocator.free(hover_content);
            try self.transport.writeResult(id, types.Hover{
                .contents = .{ .kind = "markdown", .value = hover_content },
            });
        } else {
            try self.transport.writeResult(id, null);
        }
    }

    /// Handle textDocument/definition request
    fn handleDefinition(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const position = transport.getObject(params, "position") orelse {
            try self.transport.writeResult(id, null);
            return;
        };

        const line = @as(u32, @intCast(transport.getInt(position, "line") orelse 0));
        const character = @as(u32, @intCast(transport.getInt(position, "character") orelse 0));

        if (self.analyzer.findDefinition(uri, line, character)) |loc| {
            // Return Location
            try self.transport.writeResult(id, types.Location{
                .uri = loc.uri,
                .range = .{
                    .start = .{ .line = loc.line, .character = loc.character },
                    .end = .{ .line = loc.line, .character = loc.character },
                },
            });
        } else {
            try self.transport.writeResult(id, null);
        }
    }

    /// Handle textDocument/documentSymbol request
    fn handleDocumentSymbol(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?[]const types.SymbolInformation, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?[]const types.SymbolInformation, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?[]const types.SymbolInformation, null));
            return;
        };

        const symbols = try self.analyzer.getDocumentSymbols(uri);
        defer if (symbols.len > 0) self.allocator.free(symbols);

        try self.transport.writeResult(id, symbols);
    }

    /// Handle textDocument/formatting request
    fn handleFormatting(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
            return;
        };

        const options = transport.getObject(params, "options");
        const tab_size: u32 = if (options) |opts| @as(u32, @intCast(transport.getInt(opts, "tabSize") orelse 4)) else 4;
        const insert_spaces: bool = if (options) |opts| transport.getBool(opts, "insertSpaces") orelse false else false;

        if (self.analyzer.formatDocument(uri, tab_size, insert_spaces)) |formatted_text| {
            defer self.allocator.free(formatted_text);

            // Get the document to determine its line count for the range
            const doc = self.analyzer.getDocument(uri) orelse {
                try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
                return;
            };

            // Count lines in original document
            var line_count: u32 = 0;
            var last_line_len: u32 = 0;
            for (doc.content) |c| {
                if (c == '\n') {
                    line_count += 1;
                    last_line_len = 0;
                } else {
                    last_line_len += 1;
                }
            }

            // Create a TextEdit that replaces the entire document
            var edits = self.allocator.alloc(types.TextEdit, 1) catch {
                try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
                return;
            };
            defer self.allocator.free(edits);

            edits[0] = types.TextEdit{
                .range = .{
                    .start = .{ .line = 0, .character = 0 },
                    .end = .{ .line = line_count, .character = last_line_len },
                },
                .newText = formatted_text,
            };

            try self.transport.writeResult(id, edits);
        } else {
            try self.transport.writeResult(id, @as(?[]const types.TextEdit, null));
        }
    }

    /// Handle textDocument/foldingRange request
    fn handleFoldingRange(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?[]const types.FoldingRange, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?[]const types.FoldingRange, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?[]const types.FoldingRange, null));
            return;
        };

        const ranges = try self.analyzer.getFoldingRanges(uri);
        defer if (ranges.len > 0) self.allocator.free(ranges);

        try self.transport.writeResult(id, ranges);
    }

    /// Handle textDocument/linkedEditingRange request
    fn handleLinkedEditingRange(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?types.LinkedEditingRanges, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?types.LinkedEditingRanges, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?types.LinkedEditingRanges, null));
            return;
        };

        const position = transport.getObject(params, "position") orelse {
            try self.transport.writeResult(id, @as(?types.LinkedEditingRanges, null));
            return;
        };

        const line = @as(u32, @intCast(transport.getInt(position, "line") orelse 0));
        const character = @as(u32, @intCast(transport.getInt(position, "character") orelse 0));

        if (self.analyzer.getLinkedEditingRanges(uri, line, character)) |result| {
            defer self.allocator.free(result.ranges);
            try self.transport.writeResult(id, result);
        } else {
            try self.transport.writeResult(id, @as(?types.LinkedEditingRanges, null));
        }
    }

    /// Handle textDocument/rename request
    fn handleRename(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
            return;
        };

        const position = transport.getObject(params, "position") orelse {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
            return;
        };

        const new_name = transport.getString(params, "newName") orelse {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
            return;
        };

        const line = @as(u32, @intCast(transport.getInt(position, "line") orelse 0));
        const character = @as(u32, @intCast(transport.getInt(position, "character") orelse 0));

        if (self.analyzer.getRenameEdits(uri, line, character, new_name)) |edits| {
            defer self.allocator.free(edits);
            // Build workspace edit with the text edits
            var changes = std.json.ArrayHashMap([]const types.TextEdit){};
            defer changes.map.deinit(self.allocator);
            changes.map.put(self.allocator, uri, edits) catch {
                try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
                return;
            };
            const result = types.WorkspaceEdit{
                .changes = changes,
            };
            try self.transport.writeResult(id, result);
        } else {
            try self.transport.writeResult(id, @as(?types.WorkspaceEdit, null));
        }
    }

    /// Handle textDocument/codeAction request
    fn handleCodeAction(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
            return;
        };

        const range = transport.getObject(params, "range") orelse {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
            return;
        };

        const start = transport.getObject(range, "start") orelse {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
            return;
        };

        const start_line = @as(u32, @intCast(transport.getInt(start, "line") orelse 0));

        if (self.analyzer.getCodeActions(uri, start_line)) |actions| {
            defer self.allocator.free(actions);
            try self.transport.writeResult(id, actions);
        } else {
            try self.transport.writeResult(id, @as([]const types.CodeAction, &.{}));
        }
    }

    /// Handle textDocument/semanticTokens/full request
    fn handleSemanticTokensFull(self: *Self, id: ?types.Request.Id, msg: std.json.ObjectMap) !void {
        const params = transport.getObject(msg, "params") orelse {
            try self.transport.writeResult(id, @as(?types.SemanticTokens, null));
            return;
        };

        const text_doc = transport.getObject(params, "textDocument") orelse {
            try self.transport.writeResult(id, @as(?types.SemanticTokens, null));
            return;
        };

        const uri = transport.getString(text_doc, "uri") orelse {
            try self.transport.writeResult(id, @as(?types.SemanticTokens, null));
            return;
        };

        if (self.analyzer.getSemanticTokens(uri)) |tokens| {
            defer self.allocator.free(tokens);
            const result = types.SemanticTokens{
                .data = tokens,
            };
            try self.transport.writeResult(id, result);
        } else {
            try self.transport.writeResult(id, @as(?types.SemanticTokens, null));
        }
    }
    };
}

// Type aliases for convenience
pub const StdioServer = Server(transport.Transport);
pub const TestServer = Server(test_transport.TestTransport);

/// Entry point for the LSP server (uses stdio transport)
pub fn run(allocator: std.mem.Allocator) !void {
    var server = StdioServer.init(allocator, transport.Transport.init(allocator));
    defer server.deinit();
    try server.run();
}
