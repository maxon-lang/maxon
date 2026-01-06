const std = @import("std");
const windows = std.os.windows;

/// Default timeout for waiting on the LSP server (in milliseconds)
/// Needs to be long enough to allow GPA leak reporting to complete
const DEFAULT_TIMEOUT_MS: u32 = 500;

/// Reads stderr in a background thread to prevent deadlock when the server
/// writes large amounts of output (e.g., GPA leak reports with stack traces)
const StderrReader = struct {
    thread: ?std.Thread = null,
    stderr: ?std.fs.File = null,
    buffer: [16384]u8 = undefined,
    bytes_read: usize = 0,

    fn start(self: *StderrReader, stderr: std.fs.File) void {
        self.stderr = stderr;
        self.thread = std.Thread.spawn(.{}, readStderr, .{self}) catch null;
    }

    fn readStderr(self: *StderrReader) void {
        if (self.stderr) |s| {
            // Read all available stderr data
            var total: usize = 0;
            while (total < self.buffer.len) {
                const n = s.read(self.buffer[total..]) catch break;
                if (n == 0) break;
                total += n;
            }
            self.bytes_read = total;
        }
    }

    fn join(self: *StderrReader) []const u8 {
        if (self.thread) |t| {
            t.join();
        }
        return self.buffer[0..self.bytes_read];
    }
};

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

/// Wrapper for parsed JSON response that manages memory
pub const ParsedResponse = struct {
    parsed: std.json.Parsed(std.json.Value),

    pub fn deinit(self: *ParsedResponse) void {
        self.parsed.deinit();
    }

    pub fn value(self: *const ParsedResponse) std.json.Value {
        return self.parsed.value;
    }

    pub fn getId(self: *const ParsedResponse) ?i64 {
        if (self.parsed.value == .object) {
            if (self.parsed.value.object.get("id")) |id_val| {
                if (id_val == .integer) {
                    return id_val.integer;
                }
            }
        }
        return null;
    }

    pub fn getResult(self: *const ParsedResponse) ?std.json.Value {
        if (self.parsed.value == .object) {
            return self.parsed.value.object.get("result");
        }
        return null;
    }

    pub fn getError(self: *const ParsedResponse) ?ResponseError {
        if (self.parsed.value == .object) {
            if (self.parsed.value.object.get("error")) |err_val| {
                if (err_val == .object) {
                    const err_obj = err_val.object;
                    return ResponseError{
                        .code = if (err_obj.get("code")) |c| (if (c == .integer) @intCast(c.integer) else 0) else 0,
                        .message = if (err_obj.get("message")) |m| (if (m == .string) m.string else "Unknown error") else "Unknown error",
                    };
                }
            }
        }
        return null;
    }
};

pub const ResponseError = struct {
    code: i32,
    message: []const u8,
};

/// Completion item from LSP
pub const CompletionItem = struct {
    label: []const u8,
    kind: ?i64 = null,
    detail: ?[]const u8 = null,
};

/// Completion result from LSP
pub const CompletionResult = struct {
    items: []CompletionItem,
    labels: [][]const u8, // Owns the label strings
    allocator: std.mem.Allocator,

    pub fn deinit(self: *CompletionResult) void {
        for (self.labels) |label| {
            self.allocator.free(label);
        }
        self.allocator.free(self.labels);
        self.allocator.free(self.items);
    }
};

/// Initialize result capabilities
pub const InitializeResult = struct {
    has_completion_provider: bool,
    has_text_document_sync: bool,
};

/// LSP Test Client - spawns and communicates with the LSP server
pub const LspClient = struct {
    allocator: std.mem.Allocator,
    process: std.process.Child,
    next_id: i64,
    exe_path_buf: []u8,

    /// Initialize the LSP client by spawning the server process
    pub fn init(allocator: std.mem.Allocator) !LspClient {
        const exe_path = try findMaxonExecutable(allocator);

        const argv: []const []const u8 = &.{ exe_path, "lsp-server" };
        var child = std.process.Child.init(argv, allocator);
        child.stdin_behavior = .Pipe;
        child.stdout_behavior = .Pipe;
        child.stderr_behavior = .Pipe;

        try child.spawn();

        return LspClient{
            .allocator = allocator,
            .process = child,
            .next_id = 1,
            .exe_path_buf = exe_path,
        };
    }

    /// Clean up the client and terminate the server process
    pub fn deinit(self: *LspClient) void {
        // Try graceful termination first
        _ = self.process.kill() catch {};
        _ = self.process.wait() catch {};
        self.allocator.free(self.exe_path_buf);
    }

    /// Clean shutdown with memory leak check
    /// Call this after exit() to verify the server had no memory leaks
    pub fn deinitAndCheckLeaks(self: *LspClient) !void {
        defer self.allocator.free(self.exe_path_buf);

        // Close stdin to signal EOF to the server - this allows it to exit
        if (self.process.stdin) |stdin| {
            stdin.close();
            self.process.stdin = null;
        }

        // Close stdout to avoid blocking - we don't need the responses anymore
        if (self.process.stdout) |stdout| {
            stdout.close();
            self.process.stdout = null;
        }

        try self.waitAndCheckLeaks();
    }

    /// Send an initialize request and return capabilities
    pub fn initialize(self: *LspClient) !InitializeResult {
        const params = .{
            .processId = @as(?i64, null),
            .capabilities = .{},
            .rootUri = @as(?[]const u8, null),
        };

        var response = try self.sendRequest("initialize", params);
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Initialize error: {s}\n", .{err.message});
            return error.InitializeFailed;
        }

        const result = response.getResult() orelse return error.NoResult;

        // Parse capabilities from result
        var has_completion = false;
        var has_text_sync = false;

        if (result == .object) {
            if (result.object.get("capabilities")) |caps| {
                if (caps == .object) {
                    has_completion = caps.object.get("completionProvider") != null;
                    has_text_sync = caps.object.get("textDocumentSync") != null;
                }
            }
        }

        return InitializeResult{
            .has_completion_provider = has_completion,
            .has_text_document_sync = has_text_sync,
        };
    }

    /// Send initialized notification
    pub fn initialized(self: *LspClient) !void {
        try self.sendNotification("initialized", .{});
    }

    /// Send shutdown request
    pub fn shutdown(self: *LspClient) !void {
        var response = try self.sendRequest("shutdown", null);
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Shutdown error: {s}\n", .{err.message});
            return error.ShutdownFailed;
        }
    }

    /// Send exit notification
    pub fn exit(self: *LspClient) !void {
        try self.sendNotification("exit", .{});
    }

    /// Open a document
    pub fn openDocument(self: *LspClient, uri: []const u8, content: []const u8) !void {
        const params = .{
            .textDocument = .{
                .uri = uri,
                .languageId = "maxon",
                .version = @as(i64, 1),
                .text = content,
            },
        };
        try self.sendNotification("textDocument/didOpen", params);
    }

    /// Update a document's content
    pub fn changeDocument(self: *LspClient, uri: []const u8, content: []const u8, version: i64) !void {
        const params = .{
            .textDocument = .{
                .uri = uri,
                .version = version,
            },
            .contentChanges = &[_]struct { text: []const u8 }{
                .{ .text = content },
            },
        };
        try self.sendNotification("textDocument/didChange", params);
    }

    /// Close a document
    pub fn closeDocument(self: *LspClient, uri: []const u8) !void {
        const params = .{
            .textDocument = .{
                .uri = uri,
            },
        };
        try self.sendNotification("textDocument/didClose", params);
    }

    /// Request completions at a position
    pub fn completion(self: *LspClient, uri: []const u8, line: u32, character: u32) !CompletionResult {
        return self.completionWithTrigger(uri, line, character, null);
    }

    /// Hover result from LSP
    pub const HoverResult = struct {
        content: ?[]const u8,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *HoverResult) void {
            if (self.content) |c| {
                self.allocator.free(c);
            }
        }
    };

    /// Request hover at a position
    pub fn hover(self: *LspClient, uri: []const u8, line: u32, character: u32) !HoverResult {
        var response = try self.sendRequest("textDocument/hover", .{
            .textDocument = .{ .uri = uri },
            .position = .{ .line = line, .character = character },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Hover error: {s}\n", .{err.message});
            return error.HoverFailed;
        }

        const result = response.getResult() orelse return HoverResult{
            .content = null,
            .allocator = self.allocator,
        };

        // Result can be null
        if (result == .null) {
            return HoverResult{
                .content = null,
                .allocator = self.allocator,
            };
        }

        // Parse hover content
        if (result == .object) {
            if (result.object.get("contents")) |contents| {
                // MarkupContent format: { kind: "markdown", value: "..." }
                if (contents == .object) {
                    if (contents.object.get("value")) |val| {
                        if (val == .string) {
                            return HoverResult{
                                .content = try self.allocator.dupe(u8, val.string),
                                .allocator = self.allocator,
                            };
                        }
                    }
                } else if (contents == .string) {
                    return HoverResult{
                        .content = try self.allocator.dupe(u8, contents.string),
                        .allocator = self.allocator,
                    };
                }
            }
        }

        return HoverResult{
            .content = null,
            .allocator = self.allocator,
        };
    }

    /// Definition location from LSP
    pub const DefinitionResult = struct {
        uri: ?[]const u8,
        line: ?u32,
        character: ?u32,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *DefinitionResult) void {
            if (self.uri) |u| {
                self.allocator.free(u);
            }
        }
    };

    /// Request go-to-definition at a position
    pub fn definition(self: *LspClient, uri: []const u8, line: u32, character: u32) !DefinitionResult {
        var response = try self.sendRequest("textDocument/definition", .{
            .textDocument = .{ .uri = uri },
            .position = .{ .line = line, .character = character },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Definition error: {s}\n", .{err.message});
            return error.DefinitionFailed;
        }

        const result = response.getResult() orelse return DefinitionResult{
            .uri = null,
            .line = null,
            .character = null,
            .allocator = self.allocator,
        };

        if (result == .null) {
            return DefinitionResult{
                .uri = null,
                .line = null,
                .character = null,
                .allocator = self.allocator,
            };
        }

        // Result can be Location, Location[], or LocationLink[]
        var loc: ?std.json.Value = null;
        if (result == .array and result.array.items.len > 0) {
            loc = result.array.items[0];
        } else if (result == .object) {
            loc = result;
        }

        if (loc) |location| {
            if (location == .object) {
                const target_uri = if (location.object.get("uri")) |u| (if (u == .string) u.string else null) else null;
                var target_line: ?u32 = null;
                var target_char: ?u32 = null;

                if (location.object.get("range")) |range| {
                    if (range == .object) {
                        if (range.object.get("start")) |start| {
                            if (start == .object) {
                                if (start.object.get("line")) |l| {
                                    if (l == .integer) target_line = @intCast(l.integer);
                                }
                                if (start.object.get("character")) |c| {
                                    if (c == .integer) target_char = @intCast(c.integer);
                                }
                            }
                        }
                    }
                }

                return DefinitionResult{
                    .uri = if (target_uri) |u| try self.allocator.dupe(u8, u) else null,
                    .line = target_line,
                    .character = target_char,
                    .allocator = self.allocator,
                };
            }
        }

        return DefinitionResult{
            .uri = null,
            .line = null,
            .character = null,
            .allocator = self.allocator,
        };
    }

    /// Formatting result from LSP
    pub const FormattingResult = struct {
        new_text: ?[]const u8,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *FormattingResult) void {
            if (self.new_text) |t| {
                self.allocator.free(t);
            }
        }
    };

    /// Request document formatting
    pub fn formatting(self: *LspClient, uri: []const u8, tab_size: u32, insert_spaces: bool) !FormattingResult {
        var response = try self.sendRequest("textDocument/formatting", .{
            .textDocument = .{ .uri = uri },
            .options = .{
                .tabSize = tab_size,
                .insertSpaces = insert_spaces,
            },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Formatting error: {s}\n", .{err.message});
            return error.FormattingFailed;
        }

        const result = response.getResult() orelse return FormattingResult{
            .new_text = null,
            .allocator = self.allocator,
        };

        if (result == .null) {
            return FormattingResult{
                .new_text = null,
                .allocator = self.allocator,
            };
        }

        // Result is TextEdit[] - we take the first edit's newText
        if (result == .array and result.array.items.len > 0) {
            const first_edit = result.array.items[0];
            if (first_edit == .object) {
                if (first_edit.object.get("newText")) |new_text| {
                    if (new_text == .string) {
                        return FormattingResult{
                            .new_text = try self.allocator.dupe(u8, new_text.string),
                            .allocator = self.allocator,
                        };
                    }
                }
            }
        }

        return FormattingResult{
            .new_text = null,
            .allocator = self.allocator,
        };
    }

    /// Document symbol from LSP
    pub const DocumentSymbol = struct {
        name: []const u8,
        kind: i64,
    };

    /// Document symbols result
    pub const DocumentSymbolsResult = struct {
        symbols: []DocumentSymbol,
        names: [][]const u8,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *DocumentSymbolsResult) void {
            for (self.names) |name| {
                self.allocator.free(name);
            }
            self.allocator.free(self.names);
            self.allocator.free(self.symbols);
        }
    };

    /// Request document symbols
    pub fn documentSymbols(self: *LspClient, uri: []const u8) !DocumentSymbolsResult {
        var response = try self.sendRequest("textDocument/documentSymbol", .{
            .textDocument = .{ .uri = uri },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("DocumentSymbol error: {s}\n", .{err.message});
            return error.DocumentSymbolFailed;
        }

        const result = response.getResult() orelse return DocumentSymbolsResult{
            .symbols = &.{},
            .names = &.{},
            .allocator = self.allocator,
        };

        if (result == .null or result != .array) {
            return DocumentSymbolsResult{
                .symbols = &.{},
                .names = &.{},
                .allocator = self.allocator,
            };
        }

        var symbols = try self.allocator.alloc(DocumentSymbol, result.array.items.len);
        var names = try self.allocator.alloc([]const u8, result.array.items.len);
        var count: usize = 0;

        for (result.array.items) |item| {
            if (item == .object) {
                const name = if (item.object.get("name")) |n| (if (n == .string) n.string else null) else null;
                const kind = if (item.object.get("kind")) |k| (if (k == .integer) k.integer else null) else null;

                if (name != null and kind != null) {
                    names[count] = try self.allocator.dupe(u8, name.?);
                    symbols[count] = .{
                        .name = names[count],
                        .kind = kind.?,
                    };
                    count += 1;
                }
            }
        }

        return DocumentSymbolsResult{
            .symbols = symbols[0..count],
            .names = names[0..count],
            .allocator = self.allocator,
        };
    }

    /// Linked editing range from LSP
    pub const LinkedEditingRange = struct {
        start_line: u32,
        start_char: u32,
        end_line: u32,
        end_char: u32,
    };

    /// Linked editing ranges result
    pub const LinkedEditingRangesResult = struct {
        ranges: []LinkedEditingRange,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *LinkedEditingRangesResult) void {
            self.allocator.free(self.ranges);
        }
    };

    /// Request linked editing ranges
    pub fn linkedEditingRange(self: *LspClient, uri: []const u8, line: u32, character: u32) !LinkedEditingRangesResult {
        var response = try self.sendRequest("textDocument/linkedEditingRange", .{
            .textDocument = .{ .uri = uri },
            .position = .{ .line = line, .character = character },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("LinkedEditingRange error: {s}\n", .{err.message});
            return error.LinkedEditingRangeFailed;
        }

        const result = response.getResult() orelse return LinkedEditingRangesResult{
            .ranges = &.{},
            .allocator = self.allocator,
        };

        if (result == .null) {
            return LinkedEditingRangesResult{
                .ranges = &.{},
                .allocator = self.allocator,
            };
        }

        // Result is { ranges: Range[] }
        if (result == .object) {
            if (result.object.get("ranges")) |ranges_val| {
                if (ranges_val == .array) {
                    var ranges = try self.allocator.alloc(LinkedEditingRange, ranges_val.array.items.len);
                    var count: usize = 0;

                    for (ranges_val.array.items) |range| {
                        if (range == .object) {
                            const start = range.object.get("start");
                            const end = range.object.get("end");

                            if (start != null and end != null and start.? == .object and end.? == .object) {
                                const start_line = if (start.?.object.get("line")) |l| (if (l == .integer) @as(u32, @intCast(l.integer)) else null) else null;
                                const start_char = if (start.?.object.get("character")) |c| (if (c == .integer) @as(u32, @intCast(c.integer)) else null) else null;
                                const end_line = if (end.?.object.get("line")) |l| (if (l == .integer) @as(u32, @intCast(l.integer)) else null) else null;
                                const end_char = if (end.?.object.get("character")) |c| (if (c == .integer) @as(u32, @intCast(c.integer)) else null) else null;

                                if (start_line != null and start_char != null and end_line != null and end_char != null) {
                                    ranges[count] = .{
                                        .start_line = start_line.?,
                                        .start_char = start_char.?,
                                        .end_line = end_line.?,
                                        .end_char = end_char.?,
                                    };
                                    count += 1;
                                }
                            }
                        }
                    }

                    return LinkedEditingRangesResult{
                        .ranges = ranges[0..count],
                        .allocator = self.allocator,
                    };
                }
            }
        }

        return LinkedEditingRangesResult{
            .ranges = &.{},
            .allocator = self.allocator,
        };
    }

    /// Folding range from LSP
    pub const FoldingRange = struct {
        start_line: u32,
        end_line: u32,
        kind: ?[]const u8,
    };

    /// Folding ranges result
    pub const FoldingRangesResult = struct {
        ranges: []FoldingRange,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *FoldingRangesResult) void {
            self.allocator.free(self.ranges);
        }
    };

    /// Request folding ranges
    pub fn foldingRange(self: *LspClient, uri: []const u8) !FoldingRangesResult {
        var response = try self.sendRequest("textDocument/foldingRange", .{
            .textDocument = .{ .uri = uri },
        });
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("FoldingRange error: {s}\n", .{err.message});
            return error.FoldingRangeFailed;
        }

        const result = response.getResult() orelse return FoldingRangesResult{
            .ranges = &.{},
            .allocator = self.allocator,
        };

        if (result == .null or result != .array) {
            return FoldingRangesResult{
                .ranges = &.{},
                .allocator = self.allocator,
            };
        }

        var ranges = try self.allocator.alloc(FoldingRange, result.array.items.len);
        var count: usize = 0;

        for (result.array.items) |item| {
            if (item == .object) {
                const start_line = if (item.object.get("startLine")) |l| (if (l == .integer) @as(u32, @intCast(l.integer)) else null) else null;
                const end_line = if (item.object.get("endLine")) |l| (if (l == .integer) @as(u32, @intCast(l.integer)) else null) else null;

                if (start_line != null and end_line != null) {
                    ranges[count] = .{
                        .start_line = start_line.?,
                        .end_line = end_line.?,
                        .kind = null, // We don't need kind for our tests
                    };
                    count += 1;
                }
            }
        }

        return FoldingRangesResult{
            .ranges = ranges[0..count],
            .allocator = self.allocator,
        };
    }

    /// Request completions at a position with a trigger character
    pub fn completionWithTrigger(self: *LspClient, uri: []const u8, line: u32, character: u32, trigger_char: ?[]const u8) !CompletionResult {
        var response: ParsedResponse = undefined;

        if (trigger_char) |tc| {
            // With trigger character context
            response = try self.sendRequest("textDocument/completion", .{
                .textDocument = .{
                    .uri = uri,
                },
                .position = .{
                    .line = line,
                    .character = character,
                },
                .context = .{
                    .triggerKind = @as(i32, 2), // TriggerCharacter
                    .triggerCharacter = tc,
                },
            });
        } else {
            // Invoked without trigger character
            response = try self.sendRequest("textDocument/completion", .{
                .textDocument = .{
                    .uri = uri,
                },
                .position = .{
                    .line = line,
                    .character = character,
                },
                .context = .{
                    .triggerKind = @as(i32, 1), // Invoked
                },
            });
        }
        defer response.deinit();

        if (response.getError()) |err| {
            std.debug.print("Completion error: {s}\n", .{err.message});
            return error.CompletionFailed;
        }

        const result = response.getResult() orelse return CompletionResult{
            .items = &.{},
            .labels = &.{},
            .allocator = self.allocator,
        };

        // Parse completion items
        var items_array: ?std.json.Array = null;

        if (result == .object) {
            // CompletionList format: { items: [...] }
            if (result.object.get("items")) |items_val| {
                if (items_val == .array) {
                    items_array = items_val.array;
                }
            }
        } else if (result == .array) {
            // Direct array format
            items_array = result.array;
        }

        if (items_array) |arr| {
            var items = try self.allocator.alloc(CompletionItem, arr.items.len);
            var labels = try self.allocator.alloc([]const u8, arr.items.len);
            var count: usize = 0;

            for (arr.items) |item| {
                if (item == .object) {
                    if (item.object.get("label")) |label_val| {
                        if (label_val == .string) {
                            // Copy label string so it survives response.deinit()
                            const label_copy = try self.allocator.dupe(u8, label_val.string);
                            labels[count] = label_copy;
                            items[count] = .{
                                .label = label_copy,
                                .kind = if (item.object.get("kind")) |k| (if (k == .integer) k.integer else null) else null,
                                .detail = if (item.object.get("detail")) |d| (if (d == .string) d.string else null) else null,
                            };
                            count += 1;
                        }
                    }
                }
            }

            return CompletionResult{
                .items = items[0..count],
                .labels = labels[0..count],
                .allocator = self.allocator,
            };
        }

        return CompletionResult{
            .items = &.{},
            .labels = &.{},
            .allocator = self.allocator,
        };
    }

    // ========================================================================
    // TDD Methods - for features not yet implemented in server
    // ========================================================================

    /// Rename result from LSP
    pub const RenameResult = struct {
        has_edits: bool,
        edit_count: usize,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *RenameResult) void {
            _ = self;
            // Nothing to free
        }
    };

    /// Request rename at a position
    pub fn rename(self: *LspClient, uri: []const u8, line: u32, character: u32, new_name: []const u8) !RenameResult {
        var response = try self.sendRequest("textDocument/rename", .{
            .textDocument = .{ .uri = uri },
            .position = .{ .line = line, .character = character },
            .newName = new_name,
        });
        defer response.deinit();

        if (response.getError()) |_| {
            // Server may return MethodNotFound if not implemented
            return RenameResult{
                .has_edits = false,
                .edit_count = 0,
                .allocator = self.allocator,
            };
        }

        const result = response.getResult() orelse return RenameResult{
            .has_edits = false,
            .edit_count = 0,
            .allocator = self.allocator,
        };

        if (result == .null) {
            return RenameResult{
                .has_edits = false,
                .edit_count = 0,
                .allocator = self.allocator,
            };
        }

        // Result is WorkspaceEdit: { changes: { [uri]: TextEdit[] } } or { documentChanges: [...] }
        var edit_count: usize = 0;
        if (result == .object) {
            if (result.object.get("changes")) |changes| {
                if (changes == .object) {
                    var it = changes.object.iterator();
                    while (it.next()) |entry| {
                        if (entry.value_ptr.* == .array) {
                            edit_count += entry.value_ptr.array.items.len;
                        }
                    }
                }
            }
        }

        return RenameResult{
            .has_edits = edit_count > 0,
            .edit_count = edit_count,
            .allocator = self.allocator,
        };
    }

    /// Code action result from LSP
    pub const CodeActionResult = struct {
        action_count: usize,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *CodeActionResult) void {
            _ = self;
        }
    };

    /// Request code actions at a position
    pub fn codeAction(self: *LspClient, uri: []const u8, start_line: u32, start_char: u32, end_line: u32, end_char: u32) !CodeActionResult {
        var response = try self.sendRequest("textDocument/codeAction", .{
            .textDocument = .{ .uri = uri },
            .range = .{
                .start = .{ .line = start_line, .character = start_char },
                .end = .{ .line = end_line, .character = end_char },
            },
            .context = .{
                .diagnostics = &[_]struct{}{},
            },
        });
        defer response.deinit();

        if (response.getError()) |_| {
            return CodeActionResult{
                .action_count = 0,
                .allocator = self.allocator,
            };
        }

        const result = response.getResult() orelse return CodeActionResult{
            .action_count = 0,
            .allocator = self.allocator,
        };

        if (result == .null) {
            return CodeActionResult{
                .action_count = 0,
                .allocator = self.allocator,
            };
        }

        // Result is (Command | CodeAction)[]
        if (result == .array) {
            return CodeActionResult{
                .action_count = result.array.items.len,
                .allocator = self.allocator,
            };
        }

        return CodeActionResult{
            .action_count = 0,
            .allocator = self.allocator,
        };
    }

    /// Semantic tokens result from LSP
    pub const SemanticTokensResult = struct {
        has_data: bool,
        data_length: usize,
        allocator: std.mem.Allocator,

        pub fn deinit(self: *SemanticTokensResult) void {
            _ = self;
        }
    };

    /// Request semantic tokens for a document
    pub fn semanticTokensFull(self: *LspClient, uri: []const u8) !SemanticTokensResult {
        var response = try self.sendRequest("textDocument/semanticTokens/full", .{
            .textDocument = .{ .uri = uri },
        });
        defer response.deinit();

        if (response.getError()) |_| {
            return SemanticTokensResult{
                .has_data = false,
                .data_length = 0,
                .allocator = self.allocator,
            };
        }

        const result = response.getResult() orelse return SemanticTokensResult{
            .has_data = false,
            .data_length = 0,
            .allocator = self.allocator,
        };

        if (result == .null) {
            return SemanticTokensResult{
                .has_data = false,
                .data_length = 0,
                .allocator = self.allocator,
            };
        }

        // Result is { data: number[] }
        if (result == .object) {
            if (result.object.get("data")) |data| {
                if (data == .array) {
                    return SemanticTokensResult{
                        .has_data = data.array.items.len > 0,
                        .data_length = data.array.items.len,
                        .allocator = self.allocator,
                    };
                }
            }
        }

        return SemanticTokensResult{
            .has_data = false,
            .data_length = 0,
            .allocator = self.allocator,
        };
    }

    /// Send a JSON-RPC request and wait for response
    fn sendRequest(self: *LspClient, method: []const u8, params: anytype) !ParsedResponse {
        const id = self.next_id;
        self.next_id += 1;

        // Build and send request
        try self.writeMessage(.{
            .jsonrpc = "2.0",
            .id = id,
            .method = method,
            .params = params,
        });

        // Read response
        return try self.readResponse();
    }

    /// Send a JSON-RPC notification (no response expected)
    fn sendNotification(self: *LspClient, method: []const u8, params: anytype) !void {
        try self.writeMessage(.{
            .jsonrpc = "2.0",
            .method = method,
            .params = params,
        });
    }

    /// Write a JSON-RPC message with Content-Length header
    fn writeMessage(self: *LspClient, message: anytype) !void {
        const stdin = self.process.stdin orelse return error.NoStdin;

        // Serialize to JSON
        var buffer: std.ArrayListUnmanaged(u8) = .empty;
        defer buffer.deinit(self.allocator);

        try jsonStringify(self.allocator, message, &buffer);

        // Write Content-Length header and content
        var header_buf: [64]u8 = undefined;
        const header = try std.fmt.bufPrint(&header_buf, "Content-Length: {d}\r\n\r\n", .{buffer.items.len});

        _ = try stdin.write(header);
        _ = try stdin.write(buffer.items);
    }

    /// Read a JSON-RPC response from the server
    fn readResponse(self: *LspClient) !ParsedResponse {
        const stdout = self.process.stdout orelse return error.NoStdout;

        // Read headers until we find Content-Length
        var content_length: ?usize = null;
        var line_buf: [1024]u8 = undefined;
        var line_pos: usize = 0;

        while (true) {
            var byte_buf: [1]u8 = undefined;
            const bytes_read = try stdout.read(&byte_buf);
            if (bytes_read == 0) return error.EndOfStream;

            const byte = byte_buf[0];

            if (byte == '\n') {
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
            const bytes_read = try stdout.read(content[total_read..]);
            if (bytes_read == 0) return error.UnexpectedEndOfStream;
            total_read += bytes_read;
        }

        // Parse JSON response - caller owns the memory
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, content, .{
            .allocate = .alloc_always,
        });

        return ParsedResponse{ .parsed = parsed };
    }

    /// Wait for the server process to exit and return exit code
    pub fn waitForExit(self: *LspClient) !u32 {
        const term = try self.process.wait();
        return switch (term) {
            .Exited => |code| code,
            else => error.AbnormalTermination,
        };
    }

    /// Wait for the server to exit and check for memory leaks
    /// Returns error.MemoryLeak if the server reported a memory leak
    /// Returns error.Timeout if the server doesn't exit within the timeout
    pub fn waitAndCheckLeaks(self: *LspClient) !void {
        return self.waitAndCheckLeaksTimeout(DEFAULT_TIMEOUT_MS);
    }

    /// Wait for the server to exit with a custom timeout (in milliseconds)
    pub fn waitAndCheckLeaksTimeout(self: *LspClient, timeout_ms: u32) !void {
        // Start reading stderr in background BEFORE waiting for process exit.
        // This prevents deadlock: if the server writes more to stderr than fits
        // in the pipe buffer (e.g., GPA leak reports with stack traces), it will
        // block waiting for someone to read. If we wait for exit first, deadlock.
        var stderr_reader = StderrReader{};
        if (self.process.stderr) |stderr| {
            stderr_reader.start(stderr);
        }

        // Use Windows API to wait with timeout
        const handle = self.process.id;
        const result = windows.kernel32.WaitForSingleObject(handle, timeout_ms);

        if (result == windows.WAIT_TIMEOUT) {
            _ = self.process.kill() catch {};
            _ = self.process.wait() catch {};
            _ = stderr_reader.join();
            return error.Timeout;
        }

        if (result == windows.WAIT_FAILED) {
            _ = stderr_reader.join();
            return error.WaitFailed;
        }

        // Process has exited, now get the actual termination status
        const term = try self.process.wait();

        // Join the stderr reader thread and get output
        const stderr_output = stderr_reader.join();

        // Check for memory leak messages
        if (std.mem.indexOf(u8, stderr_output, "Memory leak detected!")) |_| {
            return error.MemoryLeak;
        }
        if (std.mem.indexOf(u8, stderr_output, "Leak detected")) |_| {
            return error.MemoryLeak;
        }

        // Check exit code
        switch (term) {
            .Exited => |code| {
                if (code != 0) {
                    return error.NonZeroExitCode;
                }
            },
            .Signal => {
                return error.AbnormalTermination;
            },
            else => {
                return error.AbnormalTermination;
            },
        }
    }
};

/// Find the maxon executable path
fn findMaxonExecutable(allocator: std.mem.Allocator) ![]u8 {
    // Try relative path from project root (../bin/maxon.exe)
    // The tests run from maxon-bin directory, so we go up one level
    const paths_to_try = [_][]const u8{
        "bin/maxon.exe",
        "../bin/maxon.exe",
        "zig-out/bin/maxon.exe",
    };

    for (paths_to_try) |path| {
        // Check if file exists by trying to open it
        if (std.fs.cwd().openFile(path, .{})) |file| {
            file.close();
            return try allocator.dupe(u8, path);
        } else |_| {}
    }

    // Fallback: assume it's in PATH
    return try allocator.dupe(u8, "maxon");
}
