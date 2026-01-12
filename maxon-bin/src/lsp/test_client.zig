const std = @import("std");
const types = @import("types.zig");
const server = @import("server.zig");
const test_transport = @import("test_transport.zig");
const test_helpers = @import("test_helpers.zig");
const transport = @import("transport.zig");

// ============================================================================
// Test Client
// In-process LSP client for testing - directly calls server methods
// ============================================================================

const TestServer = server.TestServer;

pub const TestClient = struct {
    allocator: std.mem.Allocator,
    server: TestServer,
    next_id: i64 = 1,

    pub fn init(allocator: std.mem.Allocator) !TestClient {
        const test_trans = test_transport.TestTransport.init(allocator);
        const lsp_server = TestServer.init(allocator, test_trans);
        return .{
            .allocator = allocator,
            .server = lsp_server,
            .next_id = 1,
        };
    }

    pub fn deinit(self: *TestClient) void {
        self.server.transport.deinit();
        self.server.deinit();
    }

    fn getNextId(self: *TestClient) i64 {
        const id = self.next_id;
        self.next_id += 1;
        return id;
    }

    /// Initialize result
    pub const InitializeResult = struct {
        has_text_document_sync: bool,
        has_completion_provider: bool,
    };

    pub fn initialize(self: *TestClient) !InitializeResult {
        const id = self.getNextId();
        var msg = try test_helpers.buildInitializeMessage(self.allocator, id);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            else => return error.InvalidResponse,
        };

        const capabilities = transport.getObject(result_obj, "capabilities") orelse return error.NoCapabilities;

        return InitializeResult{
            .has_text_document_sync = capabilities.get("textDocumentSync") != null,
            .has_completion_provider = capabilities.get("completionProvider") != null,
        };
    }

    pub fn initialized(self: *TestClient) !void {
        var msg = try test_helpers.buildInitializedMessage(self.allocator);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    pub fn shutdown(self: *TestClient) !void {
        const id = self.getNextId();
        var msg = try test_helpers.buildShutdownMessage(self.allocator, id);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    pub fn exit(self: *TestClient) !void {
        var msg = try test_helpers.buildExitMessage(self.allocator);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    pub fn openDocument(self: *TestClient, uri: []const u8, content: []const u8) !void {
        var msg = try test_helpers.buildDidOpenMessage(self.allocator, uri, content);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    pub fn changeDocument(self: *TestClient, uri: []const u8, content: []const u8, version: i64) !void {
        var msg = try test_helpers.buildDidChangeMessage(self.allocator, uri, content, version);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    pub fn closeDocument(self: *TestClient, uri: []const u8) !void {
        var msg = try test_helpers.buildDidCloseMessage(self.allocator, uri);
        defer msg.deinit(self.allocator);
        try self.server.handleMessage(msg.parsed.value);
    }

    /// Completion result
    pub const CompletionResult = struct {
        items: []types.CompletionItem,
        allocator: std.mem.Allocator,

        pub fn deinit(self: CompletionResult) void {
            for (self.items) |item| {
                self.allocator.free(item.label);
                if (item.detail) |d| self.allocator.free(d);
                if (item.documentation) |doc| self.allocator.free(doc);
                if (item.insertText) |t| self.allocator.free(t);
            }
            self.allocator.free(self.items);
        }
    };

    pub fn completion(self: *TestClient, uri: []const u8, line: u32, character: u32) !CompletionResult {
        return self.completionWithTrigger(uri, line, character, null);
    }

    pub fn completionWithTrigger(self: *TestClient, uri: []const u8, line: u32, character: u32, trigger_char: ?[]const u8) !CompletionResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildCompletionMessage(self.allocator, id, uri, line, character, trigger_char);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        // Extract completion items
        var items = std.ArrayListUnmanaged(types.CompletionItem){};
        errdefer {
            for (items.items) |item| {
                self.allocator.free(item.label);
                if (item.detail) |d| self.allocator.free(d);
                if (item.documentation) |doc| self.allocator.free(doc);
                if (item.insertText) |t| self.allocator.free(t);
            }
            items.deinit(self.allocator);
        }

        const result_array = switch (parsed.value) {
            .array => |arr| arr,
            .object => |obj| blk: {
                if (transport.getArray(obj, "items")) |arr| {
                    break :blk arr;
                }
                return error.InvalidResponse;
            },
            else => return error.InvalidResponse,
        };

        for (result_array.items) |item_val| {
            const item_obj = switch (item_val) {
                .object => |obj| obj,
                else => continue,
            };

            const label = transport.getString(item_obj, "label") orelse continue;
            const detail = if (transport.getString(item_obj, "detail")) |d| try self.allocator.dupe(u8, d) else null;
            const documentation = if (transport.getString(item_obj, "documentation")) |doc| try self.allocator.dupe(u8, doc) else null;
            const insert_text = if (transport.getString(item_obj, "insertText")) |t| try self.allocator.dupe(u8, t) else null;

            const kind: ?types.CompletionItemKind = if (transport.getInt(item_obj, "kind")) |k| @enumFromInt(@as(u8, @intCast(k))) else null;

            try items.append(self.allocator, .{
                .label = try self.allocator.dupe(u8, label),
                .kind = kind,
                .detail = detail,
                .documentation = documentation,
                .insertText = insert_text,
                .insertTextFormat = null,
            });
        }

        return CompletionResult{
            .items = try items.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Hover result
    pub const HoverResult = struct {
        contents: []const u8,
        allocator: std.mem.Allocator,

        pub fn deinit(self: HoverResult) void {
            self.allocator.free(self.contents);
        }
    };

    pub fn hover(self: *TestClient, uri: []const u8, line: u32, character: u32) !HoverResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildHoverMessage(self.allocator, id, uri, line, character);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            .null => return error.NoHoverInfo,
            else => return error.InvalidResponse,
        };

        const contents_obj = transport.getObject(result_obj, "contents") orelse return error.NoContents;
        const value = transport.getString(contents_obj, "value") orelse return error.NoValue;

        return HoverResult{
            .contents = try self.allocator.dupe(u8, value),
            .allocator = self.allocator,
        };
    }

    /// Definition result
    pub const DefinitionResult = struct {
        uri: []const u8,
        start_line: u32,
        start_char: u32,
        end_line: u32,
        end_char: u32,
        allocator: std.mem.Allocator,

        pub fn deinit(self: DefinitionResult) void {
            self.allocator.free(self.uri);
        }
    };

    pub fn definition(self: *TestClient, uri: []const u8, line: u32, character: u32) !DefinitionResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildDefinitionMessage(self.allocator, id, uri, line, character);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            .null => return error.NoDefinition,
            else => return error.InvalidResponse,
        };

        const result_uri = transport.getString(result_obj, "uri") orelse return error.NoUri;
        const range = transport.getObject(result_obj, "range") orelse return error.NoRange;
        const start = transport.getObject(range, "start") orelse return error.NoStart;
        const end = transport.getObject(range, "end") orelse return error.NoEnd;

        const start_line = transport.getInt(start, "line") orelse return error.NoLine;
        const start_char = transport.getInt(start, "character") orelse return error.NoCharacter;
        const end_line = transport.getInt(end, "line") orelse return error.NoLine;
        const end_char = transport.getInt(end, "character") orelse return error.NoCharacter;

        return DefinitionResult{
            .uri = try self.allocator.dupe(u8, result_uri),
            .start_line = @intCast(start_line),
            .start_char = @intCast(start_char),
            .end_line = @intCast(end_line),
            .end_char = @intCast(end_char),
            .allocator = self.allocator,
        };
    }

    /// Formatting result
    pub const FormattingResult = struct {
        new_text: []const u8,
        allocator: std.mem.Allocator,

        pub fn deinit(self: FormattingResult) void {
            self.allocator.free(self.new_text);
        }
    };

    pub fn formatting(self: *TestClient, uri: []const u8, tab_size: u32, insert_spaces: bool) !FormattingResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildFormattingMessage(self.allocator, id, uri, tab_size, insert_spaces);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_array = switch (parsed.value) {
            .array => |arr| arr,
            .null => return error.NoFormatting,
            else => return error.InvalidResponse,
        };

        if (result_array.items.len == 0) return error.NoFormatting;

        const first_edit = switch (result_array.items[0]) {
            .object => |obj| obj,
            else => return error.InvalidResponse,
        };

        const new_text = transport.getString(first_edit, "newText") orelse return error.NoNewText;

        return FormattingResult{
            .new_text = try self.allocator.dupe(u8, new_text),
            .allocator = self.allocator,
        };
    }

    /// Document symbols result
    pub const DocumentSymbolsResult = struct {
        symbols: []types.SymbolInformation,
        allocator: std.mem.Allocator,

        pub fn deinit(self: DocumentSymbolsResult) void {
            for (self.symbols) |sym| {
                self.allocator.free(sym.name);
                self.allocator.free(sym.location.uri);
            }
            self.allocator.free(self.symbols);
        }
    };

    pub fn documentSymbols(self: *TestClient, uri: []const u8) !DocumentSymbolsResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildDocumentSymbolMessage(self.allocator, id, uri);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_array = switch (parsed.value) {
            .array => |arr| arr,
            .null => return DocumentSymbolsResult{ .symbols = &.{}, .allocator = self.allocator },
            else => return error.InvalidResponse,
        };

        var symbols = std.ArrayListUnmanaged(types.SymbolInformation){};
        errdefer {
            for (symbols.items) |sym| {
                self.allocator.free(sym.name);
                self.allocator.free(sym.location.uri);
            }
            symbols.deinit(self.allocator);
        }

        for (result_array.items) |sym_val| {
            const sym_obj = switch (sym_val) {
                .object => |obj| obj,
                else => continue,
            };

            const name = transport.getString(sym_obj, "name") orelse continue;
            const kind_int = transport.getInt(sym_obj, "kind") orelse continue;
            const kind: types.SymbolKind = @enumFromInt(@as(u8, @intCast(kind_int)));

            const location = transport.getObject(sym_obj, "location") orelse continue;
            const loc_uri = transport.getString(location, "uri") orelse continue;
            const range = transport.getObject(location, "range") orelse continue;
            const start = transport.getObject(range, "start") orelse continue;
            const end = transport.getObject(range, "end") orelse continue;

            const start_line = transport.getInt(start, "line") orelse continue;
            const start_char = transport.getInt(start, "character") orelse continue;
            const end_line = transport.getInt(end, "line") orelse continue;
            const end_char = transport.getInt(end, "character") orelse continue;

            try symbols.append(self.allocator, .{
                .name = try self.allocator.dupe(u8, name),
                .kind = kind,
                .location = .{
                    .uri = try self.allocator.dupe(u8, loc_uri),
                    .range = .{
                        .start = .{ .line = @intCast(start_line), .character = @intCast(start_char) },
                        .end = .{ .line = @intCast(end_line), .character = @intCast(end_char) },
                    },
                },
            });
        }

        return DocumentSymbolsResult{
            .symbols = try symbols.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Folding ranges result
    pub const FoldingRangesResult = struct {
        ranges: []types.FoldingRange,
        allocator: std.mem.Allocator,

        pub fn deinit(self: FoldingRangesResult) void {
            for (self.ranges) |range| {
                if (range.kind) |k| self.allocator.free(k);
            }
            self.allocator.free(self.ranges);
        }
    };

    pub fn foldingRange(self: *TestClient, uri: []const u8) !FoldingRangesResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildFoldingRangeMessage(self.allocator, id, uri);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_array = switch (parsed.value) {
            .array => |arr| arr,
            .null => return FoldingRangesResult{ .ranges = &.{}, .allocator = self.allocator },
            else => return error.InvalidResponse,
        };

        var ranges = std.ArrayListUnmanaged(types.FoldingRange){};
        errdefer {
            for (ranges.items) |range| {
                if (range.kind) |k| self.allocator.free(k);
            }
            ranges.deinit(self.allocator);
        }

        for (result_array.items) |range_val| {
            const range_obj = switch (range_val) {
                .object => |obj| obj,
                else => continue,
            };

            const start_line = transport.getInt(range_obj, "startLine") orelse continue;
            const end_line = transport.getInt(range_obj, "endLine") orelse continue;
            const kind = if (transport.getString(range_obj, "kind")) |k| try self.allocator.dupe(u8, k) else null;

            try ranges.append(self.allocator, .{
                .startLine = @intCast(start_line),
                .endLine = @intCast(end_line),
                .kind = kind,
            });
        }

        return FoldingRangesResult{
            .ranges = try ranges.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Linked editing ranges result
    pub const LinkedEditingRangesResult = struct {
        ranges: []types.Range,
        allocator: std.mem.Allocator,

        pub fn deinit(self: LinkedEditingRangesResult) void {
            self.allocator.free(self.ranges);
        }
    };

    pub fn linkedEditingRange(self: *TestClient, uri: []const u8, line: u32, character: u32) !LinkedEditingRangesResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildLinkedEditingRangeMessage(self.allocator, id, uri, line, character);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            .null => return LinkedEditingRangesResult{ .ranges = &.{}, .allocator = self.allocator },
            else => return error.InvalidResponse,
        };

        const ranges_array = transport.getArray(result_obj, "ranges") orelse return LinkedEditingRangesResult{ .ranges = &.{}, .allocator = self.allocator };

        var ranges = std.ArrayListUnmanaged(types.Range){};
        errdefer ranges.deinit(self.allocator);

        for (ranges_array.items) |range_val| {
            const range_obj = switch (range_val) {
                .object => |obj| obj,
                else => continue,
            };

            const start = transport.getObject(range_obj, "start") orelse continue;
            const end = transport.getObject(range_obj, "end") orelse continue;

            const start_line = transport.getInt(start, "line") orelse continue;
            const start_char = transport.getInt(start, "character") orelse continue;
            const end_line = transport.getInt(end, "line") orelse continue;
            const end_char = transport.getInt(end, "character") orelse continue;

            try ranges.append(self.allocator, .{
                .start = .{ .line = @intCast(start_line), .character = @intCast(start_char) },
                .end = .{ .line = @intCast(end_line), .character = @intCast(end_char) },
            });
        }

        return LinkedEditingRangesResult{
            .ranges = try ranges.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Rename result
    pub const RenameResult = struct {
        uri: []const u8,
        edits: []types.TextEdit,
        allocator: std.mem.Allocator,

        pub fn deinit(self: RenameResult) void {
            self.allocator.free(self.uri);
            for (self.edits) |edit| {
                self.allocator.free(edit.newText);
            }
            self.allocator.free(self.edits);
        }
    };

    pub fn rename(self: *TestClient, uri: []const u8, line: u32, character: u32, new_name: []const u8) !RenameResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildRenameMessage(self.allocator, id, uri, line, character, new_name);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            .null => return error.NoRename,
            else => return error.InvalidResponse,
        };

        const changes = transport.getObject(result_obj, "changes") orelse return error.NoChanges;

        // Get the first (and usually only) entry in changes
        var it = changes.iterator();
        const entry = it.next() orelse return error.NoChanges;

        const edits_array = switch (entry.value_ptr.*) {
            .array => |arr| arr,
            else => return error.InvalidResponse,
        };

        var edits = std.ArrayListUnmanaged(types.TextEdit){};
        errdefer {
            for (edits.items) |edit| {
                self.allocator.free(edit.newText);
            }
            edits.deinit(self.allocator);
        }

        for (edits_array.items) |edit_val| {
            const edit_obj = switch (edit_val) {
                .object => |obj| obj,
                else => continue,
            };

            const range = transport.getObject(edit_obj, "range") orelse continue;
            const start = transport.getObject(range, "start") orelse continue;
            const end = transport.getObject(range, "end") orelse continue;
            const new_text = transport.getString(edit_obj, "newText") orelse continue;

            const start_line = transport.getInt(start, "line") orelse continue;
            const start_char = transport.getInt(start, "character") orelse continue;
            const end_line = transport.getInt(end, "line") orelse continue;
            const end_char = transport.getInt(end, "character") orelse continue;

            try edits.append(self.allocator, .{
                .range = .{
                    .start = .{ .line = @intCast(start_line), .character = @intCast(start_char) },
                    .end = .{ .line = @intCast(end_line), .character = @intCast(end_char) },
                },
                .newText = try self.allocator.dupe(u8, new_text),
            });
        }

        return RenameResult{
            .uri = try self.allocator.dupe(u8, entry.key_ptr.*),
            .edits = try edits.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Code action result
    pub const CodeActionResult = struct {
        actions: []types.CodeAction,
        allocator: std.mem.Allocator,

        pub fn deinit(self: CodeActionResult) void {
            for (self.actions) |action| {
                self.allocator.free(action.title);
                if (action.kind) |k| self.allocator.free(k);
            }
            self.allocator.free(self.actions);
        }
    };

    pub fn codeAction(self: *TestClient, uri: []const u8, start_line: u32, start_char: u32, end_line: u32, end_char: u32) !CodeActionResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildCodeActionMessage(self.allocator, id, uri, start_line, start_char, end_line, end_char);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_array = switch (parsed.value) {
            .array => |arr| arr,
            .null => return CodeActionResult{ .actions = &.{}, .allocator = self.allocator },
            else => return error.InvalidResponse,
        };

        var actions = std.ArrayListUnmanaged(types.CodeAction){};
        errdefer {
            for (actions.items) |action| {
                self.allocator.free(action.title);
                if (action.kind) |k| self.allocator.free(k);
            }
            actions.deinit(self.allocator);
        }

        for (result_array.items) |action_val| {
            const action_obj = switch (action_val) {
                .object => |obj| obj,
                else => continue,
            };

            const title = transport.getString(action_obj, "title") orelse continue;
            const kind = if (transport.getString(action_obj, "kind")) |k| try self.allocator.dupe(u8, k) else null;

            try actions.append(self.allocator, .{
                .title = try self.allocator.dupe(u8, title),
                .kind = kind,
                .diagnostics = null,
                .edit = null,
                .command = null,
            });
        }

        return CodeActionResult{
            .actions = try actions.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }

    /// Semantic tokens result
    pub const SemanticTokensResult = struct {
        data: []u32,
        allocator: std.mem.Allocator,

        pub fn deinit(self: SemanticTokensResult) void {
            self.allocator.free(self.data);
        }
    };

    pub fn semanticTokensFull(self: *TestClient, uri: []const u8) !SemanticTokensResult {
        const id = self.getNextId();
        self.server.transport.clear();
        var msg = try test_helpers.buildSemanticTokensFullMessage(self.allocator, id, uri);
        defer msg.deinit(self.allocator);

        try self.server.handleMessage(msg.parsed.value);

        const response = self.server.transport.getLastResponse() orelse return error.NoResponse;

        // Parse the result JSON
        const parsed = try std.json.parseFromSlice(std.json.Value, self.allocator, response.result_json, .{ .allocate = .alloc_always });
        defer parsed.deinit();

        const result_obj = switch (parsed.value) {
            .object => |obj| obj,
            .null => return SemanticTokensResult{ .data = &.{}, .allocator = self.allocator },
            else => return error.InvalidResponse,
        };

        const data_array = transport.getArray(result_obj, "data") orelse return SemanticTokensResult{ .data = &.{}, .allocator = self.allocator };

        var data = std.ArrayListUnmanaged(u32){};
        errdefer data.deinit(self.allocator);

        for (data_array.items) |val| {
            const num = switch (val) {
                .integer => |n| @as(u32, @intCast(n)),
                else => continue,
            };
            try data.append(self.allocator, num);
        }

        return SemanticTokensResult{
            .data = try data.toOwnedSlice(self.allocator),
            .allocator = self.allocator,
        };
    }
};
