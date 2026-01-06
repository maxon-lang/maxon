const std = @import("std");

// ============================================================================
// LSP Protocol Types
// JSON-RPC 2.0 and Language Server Protocol data structures
// ============================================================================

/// JSON-RPC 2.0 Request
pub const Request = struct {
    jsonrpc: []const u8 = "2.0",
    id: ?Id = null,
    method: []const u8,
    params: ?std.json.Value = null,

    pub const Id = union(enum) {
        integer: i64,
        string: []const u8,
    };
};

/// JSON-RPC 2.0 Response
pub const Response = struct {
    jsonrpc: []const u8 = "2.0",
    id: ?Request.Id,
    result: ?std.json.Value = null,
    @"error": ?ResponseError = null,
};

pub const ResponseError = struct {
    code: i32,
    message: []const u8,
    data: ?std.json.Value = null,
};

// Error codes
pub const ErrorCodes = struct {
    pub const ParseError: i32 = -32700;
    pub const InvalidRequest: i32 = -32600;
    pub const MethodNotFound: i32 = -32601;
    pub const InvalidParams: i32 = -32602;
    pub const InternalError: i32 = -32603;
    pub const ServerNotInitialized: i32 = -32002;
    pub const RequestCancelled: i32 = -32800;
};

// ============================================================================
// LSP Initialize
// ============================================================================

pub const InitializeParams = struct {
    processId: ?i64 = null,
    rootUri: ?[]const u8 = null,
    rootPath: ?[]const u8 = null,
    capabilities: ClientCapabilities = .{},
};

pub const ClientCapabilities = struct {
    textDocument: ?TextDocumentClientCapabilities = null,
};

pub const TextDocumentClientCapabilities = struct {
    completion: ?CompletionClientCapabilities = null,
};

pub const CompletionClientCapabilities = struct {
    completionItem: ?CompletionItemCapabilities = null,
};

pub const CompletionItemCapabilities = struct {
    snippetSupport: bool = false,
    documentationFormat: ?[]const []const u8 = null,
};

pub const InitializeResult = struct {
    capabilities: ServerCapabilities,
};

pub const ServerCapabilities = struct {
    textDocumentSync: ?TextDocumentSyncOptions = null,
    completionProvider: ?CompletionOptions = null,
    hoverProvider: bool = true,
    definitionProvider: bool = false,
    documentSymbolProvider: bool = false,
    documentFormattingProvider: bool = false,
    foldingRangeProvider: bool = false,
    linkedEditingRangeProvider: bool = false,
    renameProvider: bool = false,
    codeActionProvider: bool = false,
    semanticTokensProvider: ?SemanticTokensOptions = null,
};

pub const TextDocumentSyncOptions = struct {
    openClose: bool = true,
    change: TextDocumentSyncKind = .full,
};

pub const TextDocumentSyncKind = enum(u8) {
    none = 0,
    full = 1,
    incremental = 2,

    // Serialize as integer for LSP protocol
    pub fn jsonStringify(self: TextDocumentSyncKind, jws: anytype) !void {
        try jws.write(@intFromEnum(self));
    }
};

pub const CompletionOptions = struct {
    triggerCharacters: []const []const u8 = &.{"."},
    resolveProvider: bool = false,
};

// ============================================================================
// Text Document Types
// ============================================================================

pub const TextDocumentIdentifier = struct {
    uri: []const u8,
};

pub const VersionedTextDocumentIdentifier = struct {
    uri: []const u8,
    version: i64,
};

pub const TextDocumentItem = struct {
    uri: []const u8,
    languageId: []const u8,
    version: i64,
    text: []const u8,
};

pub const TextDocumentContentChangeEvent = struct {
    text: []const u8,
};

pub const Position = struct {
    line: u32,
    character: u32,
};

pub const Range = struct {
    start: Position,
    end: Position,
};

pub const Location = struct {
    uri: []const u8,
    range: Range,
};

// ============================================================================
// Completion Types
// ============================================================================

pub const CompletionParams = struct {
    textDocument: TextDocumentIdentifier,
    position: Position,
    context: ?CompletionContext = null,
};

pub const CompletionContext = struct {
    triggerKind: CompletionTriggerKind = .invoked,
    triggerCharacter: ?[]const u8 = null,
};

pub const CompletionTriggerKind = enum(u8) {
    invoked = 1,
    trigger_character = 2,
    trigger_for_incomplete_completions = 3,
};

pub const CompletionList = struct {
    isIncomplete: bool = false,
    items: []const CompletionItem,
};

pub const CompletionItem = struct {
    label: []const u8,
    kind: ?CompletionItemKind = null,
    detail: ?[]const u8 = null,
    documentation: ?[]const u8 = null,
    insertText: ?[]const u8 = null,
    insertTextFormat: ?InsertTextFormat = null,
};

pub const CompletionItemKind = enum(u8) {
    text = 1,
    method = 2,
    function = 3,
    constructor = 4,
    field = 5,
    variable = 6,
    class = 7,
    interface = 8,
    module = 9,
    property = 10,
    unit = 11,
    value = 12,
    @"enum" = 13,
    keyword = 14,
    snippet = 15,
    color = 16,
    file = 17,
    reference = 18,
    folder = 19,
    enum_member = 20,
    constant = 21,
    @"struct" = 22,
    event = 23,
    operator = 24,
    type_parameter = 25,
};

pub const InsertTextFormat = enum(u8) {
    plain_text = 1,
    snippet = 2,
};

// ============================================================================
// Document Notification Params
// ============================================================================

pub const DidOpenTextDocumentParams = struct {
    textDocument: TextDocumentItem,
};

pub const DidChangeTextDocumentParams = struct {
    textDocument: VersionedTextDocumentIdentifier,
    contentChanges: []const TextDocumentContentChangeEvent,
};

pub const DidCloseTextDocumentParams = struct {
    textDocument: TextDocumentIdentifier,
};

// ============================================================================
// Hover Types
// ============================================================================

pub const HoverParams = struct {
    textDocument: TextDocumentIdentifier,
    position: Position,
};

pub const Hover = struct {
    contents: MarkupContent,
    range: ?Range = null,
};

pub const MarkupContent = struct {
    kind: []const u8 = "markdown",
    value: []const u8,
};

// ============================================================================
// Definition Types
// ============================================================================

pub const DefinitionParams = struct {
    textDocument: TextDocumentIdentifier,
    position: Position,
};

// ============================================================================
// Document Symbol Types
// ============================================================================

pub const SymbolKind = enum(u8) {
    file = 1,
    module = 2,
    namespace = 3,
    package = 4,
    class = 5,
    method = 6,
    property = 7,
    field = 8,
    constructor = 9,
    @"enum" = 10,
    interface = 11,
    function = 12,
    variable = 13,
    constant = 14,
    string = 15,
    number = 16,
    boolean = 17,
    array = 18,
    object = 19,
    key = 20,
    null = 21,
    enum_member = 22,
    @"struct" = 23,
    event = 24,
    operator = 25,
    type_parameter = 26,

    pub fn jsonStringify(self: SymbolKind, jws: anytype) !void {
        try jws.write(@intFromEnum(self));
    }
};

pub const SymbolInformation = struct {
    name: []const u8,
    kind: SymbolKind,
    location: Location,
};

// ============================================================================
// Formatting Types
// ============================================================================

pub const TextEdit = struct {
    range: Range,
    newText: []const u8,
};

// ============================================================================
// Folding Range Types
// ============================================================================

pub const FoldingRange = struct {
    startLine: u32,
    endLine: u32,
    startCharacter: ?u32 = null,
    endCharacter: ?u32 = null,
    kind: ?[]const u8 = null,
};

// ============================================================================
// Linked Editing Range Types
// ============================================================================

pub const LinkedEditingRanges = struct {
    ranges: []const Range,
    wordPattern: ?[]const u8 = null,
};

// ============================================================================
// Rename Types
// ============================================================================

pub const WorkspaceEdit = struct {
    changes: ?std.json.ArrayHashMap([]const TextEdit) = null,
    documentChanges: ?[]const TextDocumentEdit = null,
};

pub const TextDocumentEdit = struct {
    textDocument: VersionedTextDocumentIdentifier,
    edits: []const TextEdit,
};

// ============================================================================
// Code Action Types
// ============================================================================

pub const CodeAction = struct {
    title: []const u8,
    kind: ?[]const u8 = null,
    diagnostics: ?[]const Diagnostic = null,
    edit: ?WorkspaceEdit = null,
    command: ?Command = null,
};

pub const Diagnostic = struct {
    range: Range,
    severity: ?u8 = null,
    code: ?[]const u8 = null,
    source: ?[]const u8 = null,
    message: []const u8,
};

pub const Command = struct {
    title: []const u8,
    command: []const u8,
    arguments: ?[]const std.json.Value = null,
};

// ============================================================================
// Semantic Tokens Types
// ============================================================================

pub const SemanticTokensLegend = struct {
    tokenTypes: []const []const u8,
    tokenModifiers: []const []const u8,
};

pub const SemanticTokens = struct {
    resultId: ?[]const u8 = null,
    data: []const u32,
};

pub const SemanticTokensOptions = struct {
    legend: SemanticTokensLegend,
    full: bool = true,
    range: bool = false,
};
