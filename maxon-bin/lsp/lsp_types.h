#ifndef MAXON_LSP_TYPES_H
#define MAXON_LSP_TYPES_H

#include <map>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace maxon::lsp {

// =============================================================================
// Position and Range Types
// =============================================================================

// Position in a text document (0-based line and character)
struct Position {
    int line = 0;      // 0-based line number
    int character = 0; // 0-based character offset (UTF-16 code units in spec)

    Position() = default;
    Position(int l, int c) : line(l), character(c) {}

    bool operator==(const Position& other) const {
        return line == other.line && character == other.character;
    }
    bool operator!=(const Position& other) const { return !(*this == other); }
    bool operator<(const Position& other) const {
        if (line != other.line) return line < other.line;
        return character < other.character;
    }
    bool operator<=(const Position& other) const {
        return *this < other || *this == other;
    }
    bool operator>(const Position& other) const { return !(*this <= other); }
    bool operator>=(const Position& other) const { return !(*this < other); }
};

// Range in a text document
struct Range {
    Position start;
    Position end;

    Range() = default;
    Range(Position s, Position e) : start(s), end(e) {}
    Range(int startLine, int startChar, int endLine, int endChar)
        : start(startLine, startChar), end(endLine, endChar) {}

    bool operator==(const Range& other) const {
        return start == other.start && end == other.end;
    }
    bool operator!=(const Range& other) const { return !(*this == other); }

    bool contains(const Position& pos) const {
        return start <= pos && pos < end;
    }

    bool isEmpty() const {
        return start == end;
    }
};

// Location in a resource (uri + range)
struct Location {
    std::string uri;
    Range range;

    bool operator==(const Location& other) const {
        return uri == other.uri && range == other.range;
    }
};

// LocationLink for definition/declaration with origin selection
struct LocationLink {
    std::optional<Range> originSelectionRange;
    std::string targetUri;
    Range targetRange;
    Range targetSelectionRange;
};

// =============================================================================
// Document Types
// =============================================================================

// Text document identifier (just URI)
struct TextDocumentIdentifier {
    std::string uri;
};

// Versioned text document identifier
struct VersionedTextDocumentIdentifier {
    std::string uri;
    int version = 0;
};

// Optional versioned text document identifier
struct OptionalVersionedTextDocumentIdentifier {
    std::string uri;
    std::optional<int> version;
};

// Full text document item
struct TextDocumentItem {
    std::string uri;
    std::string languageId;
    int version = 0;
    std::string text;
};

// Text document content change event
struct TextDocumentContentChangeEvent {
    std::optional<Range> range;       // If omitted, full content replacement
    std::optional<int> rangeLength;   // Deprecated, but still used by some clients
    std::string text;
};

// Text document position params (for hover, definition, etc.)
struct TextDocumentPositionParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// =============================================================================
// Diagnostic Types
// =============================================================================

// Diagnostic severity
enum class DiagnosticSeverity {
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
};

// Diagnostic tags
enum class DiagnosticTag {
    Unnecessary = 1,
    Deprecated = 2
};

// Related information for a diagnostic
struct DiagnosticRelatedInformation {
    Location location;
    std::string message;
};

// Code description for diagnostic
struct CodeDescription {
    std::string href;
};

// Diagnostic code (can be integer or string)
using DiagnosticCode = std::variant<int, std::string>;

// Diagnostic
struct Diagnostic {
    Range range;
    std::optional<DiagnosticSeverity> severity;
    std::optional<DiagnosticCode> code;
    std::optional<CodeDescription> codeDescription;
    std::optional<std::string> source;
    std::string message;
    std::optional<std::vector<DiagnosticTag>> tags;
    std::optional<std::vector<DiagnosticRelatedInformation>> relatedInformation;
    std::optional<std::string> data;  // JSON data for code actions
};

// Publish diagnostics params
struct PublishDiagnosticsParams {
    std::string uri;
    std::optional<int> version;
    std::vector<Diagnostic> diagnostics;
};

// =============================================================================
// Text Edit Types
// =============================================================================

// Text edit
struct TextEdit {
    Range range;
    std::string newText;
};

// Annotated text edit
struct AnnotatedTextEdit {
    Range range;
    std::string newText;
    std::string annotationId;
};

// Text document edit
struct TextDocumentEdit {
    OptionalVersionedTextDocumentIdentifier textDocument;
    std::vector<TextEdit> edits;
};

// Create file options
struct CreateFileOptions {
    std::optional<bool> overwrite;
    std::optional<bool> ignoreIfExists;
};

// Create file operation
struct CreateFile {
    std::string kind = "create";
    std::string uri;
    std::optional<CreateFileOptions> options;
    std::optional<std::string> annotationId;
};

// Rename file options
struct RenameFileOptions {
    std::optional<bool> overwrite;
    std::optional<bool> ignoreIfExists;
};

// Rename file operation
struct RenameFile {
    std::string kind = "rename";
    std::string oldUri;
    std::string newUri;
    std::optional<RenameFileOptions> options;
    std::optional<std::string> annotationId;
};

// Delete file options
struct DeleteFileOptions {
    std::optional<bool> recursive;
    std::optional<bool> ignoreIfNotExists;
};

// Delete file operation
struct DeleteFile {
    std::string kind = "delete";
    std::string uri;
    std::optional<DeleteFileOptions> options;
    std::optional<std::string> annotationId;
};

// Document change (variant of operations)
using DocumentChange = std::variant<TextDocumentEdit, CreateFile, RenameFile, DeleteFile>;

// Change annotation
struct ChangeAnnotation {
    std::string label;
    std::optional<bool> needsConfirmation;
    std::optional<std::string> description;
};

// Workspace edit
struct WorkspaceEdit {
    std::optional<std::map<std::string, std::vector<TextEdit>>> changes;
    std::optional<std::vector<DocumentChange>> documentChanges;
    std::optional<std::map<std::string, ChangeAnnotation>> changeAnnotations;
};

// =============================================================================
// Completion Types
// =============================================================================

// Completion item kind
enum class CompletionItemKind {
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25
};

// Insert text format
enum class InsertTextFormat {
    PlainText = 1,
    Snippet = 2
};

// Completion trigger kind
enum class CompletionTriggerKind {
    Invoked = 1,
    TriggerCharacter = 2,
    TriggerForIncompleteCompletions = 3
};

// Completion item tag
enum class CompletionItemTag {
    Deprecated = 1
};

// Insert text mode
enum class InsertTextMode {
    AsIs = 1,
    AdjustIndentation = 2
};

// Markup kind
enum class MarkupKind {
    PlainText,
    Markdown
};

// Markup content
struct MarkupContent {
    MarkupKind kind = MarkupKind::PlainText;
    std::string value;
};

// Documentation (can be string or MarkupContent)
using Documentation = std::variant<std::string, MarkupContent>;

// Insert/replace edit (for completion items)
struct InsertReplaceEdit {
    std::string newText;
    Range insert;
    Range replace;
};

// Completion item label details
struct CompletionItemLabelDetails {
    std::optional<std::string> detail;
    std::optional<std::string> description;
};

// Command
struct Command {
    std::string title;
    std::string command;
    std::optional<std::vector<std::string>> arguments;
};

// Completion item
struct CompletionItem {
    std::string label;
    std::optional<CompletionItemLabelDetails> labelDetails;
    std::optional<CompletionItemKind> kind;
    std::optional<std::vector<CompletionItemTag>> tags;
    std::optional<std::string> detail;
    std::optional<Documentation> documentation;
    std::optional<bool> deprecated;
    std::optional<bool> preselect;
    std::optional<std::string> sortText;
    std::optional<std::string> filterText;
    std::optional<std::string> insertText;
    std::optional<InsertTextFormat> insertTextFormat;
    std::optional<InsertTextMode> insertTextMode;
    std::optional<std::variant<TextEdit, InsertReplaceEdit>> textEdit;
    std::optional<std::string> textEditText;
    std::optional<std::vector<TextEdit>> additionalTextEdits;
    std::optional<std::vector<std::string>> commitCharacters;
    std::optional<Command> command;
    std::optional<std::string> data;  // JSON string for resolve
};

// Completion list
struct CompletionList {
    bool isIncomplete = false;
    std::optional<std::string> itemDefaults;  // JSON string
    std::vector<CompletionItem> items;
};

// Completion context
struct CompletionContext {
    CompletionTriggerKind triggerKind = CompletionTriggerKind::Invoked;
    std::optional<std::string> triggerCharacter;
};

// Completion params
struct CompletionParams {
    TextDocumentIdentifier textDocument;
    Position position;
    std::optional<CompletionContext> context;
};

// =============================================================================
// Hover Types
// =============================================================================

// Hover
struct Hover {
    std::variant<std::string, MarkupContent, std::vector<std::variant<std::string, MarkupContent>>> contents;
    std::optional<Range> range;
};

// Hover params
struct HoverParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// =============================================================================
// Signature Help Types
// =============================================================================

// Parameter information
struct ParameterInformation {
    std::variant<std::string, std::pair<int, int>> label;  // string or [start, end] tuple
    std::optional<Documentation> documentation;
};

// Signature information
struct SignatureInformation {
    std::string label;
    std::optional<Documentation> documentation;
    std::optional<std::vector<ParameterInformation>> parameters;
    std::optional<int> activeParameter;
};

// Signature help
struct SignatureHelp {
    std::vector<SignatureInformation> signatures;
    std::optional<int> activeSignature;
    std::optional<int> activeParameter;
};

// Signature help trigger kind
enum class SignatureHelpTriggerKind {
    Invoked = 1,
    TriggerCharacter = 2,
    ContentChange = 3
};

// Signature help context
struct SignatureHelpContext {
    SignatureHelpTriggerKind triggerKind = SignatureHelpTriggerKind::Invoked;
    std::optional<std::string> triggerCharacter;
    bool isRetrigger = false;
    std::optional<SignatureHelp> activeSignatureHelp;
};

// Signature help params
struct SignatureHelpParams {
    TextDocumentIdentifier textDocument;
    Position position;
    std::optional<SignatureHelpContext> context;
};

// =============================================================================
// Symbol Types
// =============================================================================

// Symbol kind
enum class SymbolKind {
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26
};

// Symbol tag
enum class SymbolTag {
    Deprecated = 1
};

// Document symbol (hierarchical)
struct DocumentSymbol {
    std::string name;
    std::optional<std::string> detail;
    SymbolKind kind = SymbolKind::Variable;
    std::optional<std::vector<SymbolTag>> tags;
    std::optional<bool> deprecated;
    Range range;
    Range selectionRange;
    std::optional<std::vector<DocumentSymbol>> children;
};

// Symbol information (flat)
struct SymbolInformation {
    std::string name;
    SymbolKind kind = SymbolKind::Variable;
    std::optional<std::vector<SymbolTag>> tags;
    std::optional<bool> deprecated;
    Location location;
    std::optional<std::string> containerName;
};

// Document symbol params
struct DocumentSymbolParams {
    TextDocumentIdentifier textDocument;
};

// Workspace symbol params
struct WorkspaceSymbolParams {
    std::string query;
};

// =============================================================================
// Code Action Types
// =============================================================================

// Code action kind constants
namespace CodeActionKind {
    inline const std::string Empty = "";
    inline const std::string QuickFix = "quickfix";
    inline const std::string Refactor = "refactor";
    inline const std::string RefactorExtract = "refactor.extract";
    inline const std::string RefactorInline = "refactor.inline";
    inline const std::string RefactorRewrite = "refactor.rewrite";
    inline const std::string Source = "source";
    inline const std::string SourceOrganizeImports = "source.organizeImports";
    inline const std::string SourceFixAll = "source.fixAll";
}

// Code action trigger kind
enum class CodeActionTriggerKind {
    Invoked = 1,
    Automatic = 2
};

// Code action context
struct CodeActionContext {
    std::vector<Diagnostic> diagnostics;
    std::optional<std::vector<std::string>> only;
    std::optional<CodeActionTriggerKind> triggerKind;
};

// Code action disabled reason
struct CodeActionDisabled {
    std::string reason;
};

// Code action
struct CodeAction {
    std::string title;
    std::optional<std::string> kind;
    std::optional<std::vector<Diagnostic>> diagnostics;
    std::optional<bool> isPreferred;
    std::optional<CodeActionDisabled> disabled;
    std::optional<WorkspaceEdit> edit;
    std::optional<Command> command;
    std::optional<std::string> data;  // JSON string for resolve
};

// Code action params
struct CodeActionParams {
    TextDocumentIdentifier textDocument;
    Range range;
    CodeActionContext context;
};

// =============================================================================
// Formatting Types
// =============================================================================

// Formatting options
struct FormattingOptions {
    int tabSize = 4;
    bool insertSpaces = true;
    std::optional<bool> trimTrailingWhitespace;
    std::optional<bool> insertFinalNewline;
    std::optional<bool> trimFinalNewlines;
    // Additional options can be stored as key-value pairs
    std::map<std::string, std::variant<bool, int, std::string>> additionalOptions;
};

// Document formatting params
struct DocumentFormattingParams {
    TextDocumentIdentifier textDocument;
    FormattingOptions options;
};

// Document range formatting params
struct DocumentRangeFormattingParams {
    TextDocumentIdentifier textDocument;
    Range range;
    FormattingOptions options;
};

// Document on type formatting params
struct DocumentOnTypeFormattingParams {
    TextDocumentIdentifier textDocument;
    Position position;
    std::string ch;
    FormattingOptions options;
};

// =============================================================================
// Folding Range Types
// =============================================================================

// Folding range kind
namespace FoldingRangeKind {
    inline const std::string Comment = "comment";
    inline const std::string Imports = "imports";
    inline const std::string Region = "region";
}

// Folding range
struct FoldingRange {
    int startLine = 0;
    std::optional<int> startCharacter;
    int endLine = 0;
    std::optional<int> endCharacter;
    std::optional<std::string> kind;
    std::optional<std::string> collapsedText;
};

// Folding range params
struct FoldingRangeParams {
    TextDocumentIdentifier textDocument;
};

// =============================================================================
// Semantic Tokens Types
// =============================================================================

// Semantic token types (standard types)
namespace SemanticTokenTypes {
    inline const std::string Namespace = "namespace";
    inline const std::string Type = "type";
    inline const std::string Class = "class";
    inline const std::string Enum = "enum";
    inline const std::string Interface = "interface";
    inline const std::string Struct = "struct";
    inline const std::string TypeParameter = "typeParameter";
    inline const std::string Parameter = "parameter";
    inline const std::string Variable = "variable";
    inline const std::string Property = "property";
    inline const std::string EnumMember = "enumMember";
    inline const std::string Event = "event";
    inline const std::string Function = "function";
    inline const std::string Method = "method";
    inline const std::string Macro = "macro";
    inline const std::string Keyword = "keyword";
    inline const std::string Modifier = "modifier";
    inline const std::string Comment = "comment";
    inline const std::string String = "string";
    inline const std::string Number = "number";
    inline const std::string Regexp = "regexp";
    inline const std::string Operator = "operator";
    inline const std::string Decorator = "decorator";
}

// Semantic token modifiers (standard modifiers)
namespace SemanticTokenModifiers {
    inline const std::string Declaration = "declaration";
    inline const std::string Definition = "definition";
    inline const std::string Readonly = "readonly";
    inline const std::string Static = "static";
    inline const std::string Deprecated = "deprecated";
    inline const std::string Abstract = "abstract";
    inline const std::string Async = "async";
    inline const std::string Modification = "modification";
    inline const std::string Documentation = "documentation";
    inline const std::string DefaultLibrary = "defaultLibrary";
}

// Semantic tokens legend
struct SemanticTokensLegend {
    std::vector<std::string> tokenTypes;
    std::vector<std::string> tokenModifiers;
};

// Semantic tokens
struct SemanticTokens {
    std::optional<std::string> resultId;
    std::vector<int> data;  // Encoded: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers]...
};

// Semantic tokens delta edit
struct SemanticTokensEdit {
    int start = 0;
    int deleteCount = 0;
    std::optional<std::vector<int>> data;
};

// Semantic tokens delta
struct SemanticTokensDelta {
    std::optional<std::string> resultId;
    std::vector<SemanticTokensEdit> edits;
};

// Semantic tokens params
struct SemanticTokensParams {
    TextDocumentIdentifier textDocument;
};

// Semantic tokens range params
struct SemanticTokensRangeParams {
    TextDocumentIdentifier textDocument;
    Range range;
};

// Semantic tokens delta params
struct SemanticTokensDeltaParams {
    TextDocumentIdentifier textDocument;
    std::string previousResultId;
};

// =============================================================================
// Definition/Declaration/References Types
// =============================================================================

// Definition params
struct DefinitionParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// Declaration params
struct DeclarationParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// Type definition params
struct TypeDefinitionParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// Implementation params
struct ImplementationParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// References context
struct ReferenceContext {
    bool includeDeclaration = false;
};

// References params
struct ReferenceParams {
    TextDocumentIdentifier textDocument;
    Position position;
    ReferenceContext context;
};

// =============================================================================
// Rename Types
// =============================================================================

// Prepare rename params
struct PrepareRenameParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// Prepare rename result (can be Range, { range, placeholder }, or { defaultBehavior })
struct PrepareRenameResult {
    Range range;
    std::optional<std::string> placeholder;
};

// Prepare rename default behavior result
struct PrepareRenameDefaultBehavior {
    bool defaultBehavior = true;
};

// Rename params
struct RenameParams {
    TextDocumentIdentifier textDocument;
    Position position;
    std::string newName;
};

// =============================================================================
// Linked Editing Range Types
// =============================================================================

// Linked editing ranges
struct LinkedEditingRanges {
    std::vector<Range> ranges;
    std::optional<std::string> wordPattern;
};

// Linked editing range params
struct LinkedEditingRangeParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// =============================================================================
// Inlay Hint Types
// =============================================================================

// Inlay hint kind
enum class InlayHintKind {
    Type = 1,
    Parameter = 2
};

// Inlay hint label part
struct InlayHintLabelPart {
    std::string value;
    std::optional<std::string> tooltip;  // Can be string or MarkupContent
    std::optional<Location> location;
    std::optional<Command> command;
};

// Inlay hint
struct InlayHint {
    Position position;
    std::variant<std::string, std::vector<InlayHintLabelPart>> label;
    std::optional<InlayHintKind> kind;
    std::optional<std::vector<TextEdit>> textEdits;
    std::optional<std::string> tooltip;
    std::optional<bool> paddingLeft;
    std::optional<bool> paddingRight;
    std::optional<std::string> data;  // JSON string for resolve
};

// Inlay hint params
struct InlayHintParams {
    TextDocumentIdentifier textDocument;
    Range range;
};

// =============================================================================
// CodeLens Types
// =============================================================================

// CodeLens represents a command that should be shown along with source text
struct CodeLens {
    Range range;                        // Range in the document where lens applies
    std::optional<Command> command;     // Command to execute when clicked (optional for info-only)
    std::optional<std::string> data;    // JSON data for resolve (optional)
};

// CodeLens params
struct CodeLensParams {
    TextDocumentIdentifier textDocument;
};

// =============================================================================
// Call Hierarchy Types
// =============================================================================

// Call hierarchy item
struct CallHierarchyItem {
    std::string name;
    SymbolKind kind = SymbolKind::Function;
    std::optional<std::vector<SymbolTag>> tags;
    std::optional<std::string> detail;
    std::string uri;
    Range range;
    Range selectionRange;
    std::optional<std::string> data;  // JSON string
};

// Call hierarchy incoming call
struct CallHierarchyIncomingCall {
    CallHierarchyItem from;
    std::vector<Range> fromRanges;
};

// Call hierarchy outgoing call
struct CallHierarchyOutgoingCall {
    CallHierarchyItem to;
    std::vector<Range> fromRanges;
};

// Call hierarchy prepare params
struct CallHierarchyPrepareParams {
    TextDocumentIdentifier textDocument;
    Position position;
};

// Call hierarchy incoming calls params
struct CallHierarchyIncomingCallsParams {
    CallHierarchyItem item;
};

// Call hierarchy outgoing calls params
struct CallHierarchyOutgoingCallsParams {
    CallHierarchyItem item;
};

// =============================================================================
// Initialization Types
// =============================================================================

// Text document sync kind
enum class TextDocumentSyncKind {
    None = 0,
    Full = 1,
    Incremental = 2
};

// Trace value
enum class TraceValue {
    Off,
    Messages,
    Verbose
};

// Client info
struct ClientInfo {
    std::string name;
    std::optional<std::string> version;
};

// Server info
struct ServerInfo {
    std::string name;
    std::optional<std::string> version;
};

// Workspace folder
struct WorkspaceFolder {
    std::string uri;
    std::string name;
};

// Completion client capabilities (subset)
struct CompletionClientCapabilities {
    std::optional<bool> dynamicRegistration;
    struct CompletionItemCapabilities {
        std::optional<bool> snippetSupport;
        std::optional<bool> commitCharactersSupport;
        std::optional<std::vector<std::string>> documentationFormat;
        std::optional<bool> deprecatedSupport;
        std::optional<bool> preselectSupport;
        std::optional<bool> insertReplaceSupport;
        std::optional<bool> labelDetailsSupport;
    };
    std::optional<CompletionItemCapabilities> completionItem;
    std::optional<bool> contextSupport;
    std::optional<bool> insertTextModeSupport;
};

// Text document client capabilities (subset)
struct TextDocumentClientCapabilities {
    std::optional<bool> synchronization;
    std::optional<CompletionClientCapabilities> completion;
    std::optional<bool> hover;
    std::optional<bool> signatureHelp;
    std::optional<bool> declaration;
    std::optional<bool> definition;
    std::optional<bool> typeDefinition;
    std::optional<bool> implementation;
    std::optional<bool> references;
    std::optional<bool> documentHighlight;
    std::optional<bool> documentSymbol;
    std::optional<bool> codeAction;
    std::optional<bool> codeLens;
    std::optional<bool> documentLink;
    std::optional<bool> colorProvider;
    std::optional<bool> formatting;
    std::optional<bool> rangeFormatting;
    std::optional<bool> onTypeFormatting;
    std::optional<bool> rename;
    std::optional<bool> publishDiagnostics;
    std::optional<bool> foldingRange;
    std::optional<bool> selectionRange;
    std::optional<bool> linkedEditingRange;
    std::optional<bool> callHierarchy;
    std::optional<bool> semanticTokens;
    std::optional<bool> inlayHint;
};

// Workspace client capabilities (subset)
struct WorkspaceClientCapabilities {
    std::optional<bool> applyEdit;
    std::optional<bool> workspaceEdit;
    std::optional<bool> didChangeConfiguration;
    std::optional<bool> didChangeWatchedFiles;
    std::optional<bool> symbol;
    std::optional<bool> executeCommand;
    std::optional<bool> workspaceFolders;
    std::optional<bool> configuration;
    std::optional<bool> semanticTokens;
    std::optional<bool> codeLens;
    std::optional<bool> fileOperations;
    std::optional<bool> inlayHint;
};

// General client capabilities
struct GeneralClientCapabilities {
    std::optional<bool> staleRequestSupport;
    std::optional<bool> regularExpressions;
    std::optional<bool> markdown;
    std::optional<std::vector<std::string>> positionEncodings;
};

// Client capabilities
struct ClientCapabilities {
    std::optional<WorkspaceClientCapabilities> workspace;
    std::optional<TextDocumentClientCapabilities> textDocument;
    std::optional<GeneralClientCapabilities> general;
    std::optional<std::string> experimental;  // JSON object
};

// Text document sync options
struct TextDocumentSyncOptions {
    std::optional<bool> openClose;
    std::optional<TextDocumentSyncKind> change;
    std::optional<bool> willSave;
    std::optional<bool> willSaveWaitUntil;
    struct SaveOptions {
        std::optional<bool> includeText;
    };
    std::optional<SaveOptions> save;
};

// Completion options
struct CompletionOptions {
    std::optional<std::vector<std::string>> triggerCharacters;
    std::optional<std::vector<std::string>> allCommitCharacters;
    std::optional<bool> resolveProvider;
    std::optional<bool> workDoneProgress;
};

// Signature help options
struct SignatureHelpOptions {
    std::optional<std::vector<std::string>> triggerCharacters;
    std::optional<std::vector<std::string>> retriggerCharacters;
    std::optional<bool> workDoneProgress;
};

// Code action options
struct CodeActionOptions {
    std::optional<std::vector<std::string>> codeActionKinds;
    std::optional<bool> resolveProvider;
    std::optional<bool> workDoneProgress;
};

// Document formatting options
struct DocumentFormattingOptions {
    std::optional<bool> workDoneProgress;
};

// Document range formatting options
struct DocumentRangeFormattingOptions {
    std::optional<bool> workDoneProgress;
};

// Document on type formatting options
struct DocumentOnTypeFormattingOptions {
    std::string firstTriggerCharacter;
    std::optional<std::vector<std::string>> moreTriggerCharacter;
};

// Rename options
struct RenameOptions {
    std::optional<bool> prepareProvider;
    std::optional<bool> workDoneProgress;
};

// Folding range options
struct FoldingRangeOptions {
    std::optional<bool> workDoneProgress;
};

// Semantic tokens options
struct SemanticTokensOptions {
    SemanticTokensLegend legend;
    std::optional<bool> range;
    struct FullOptions {
        std::optional<bool> delta;
    };
    std::optional<std::variant<bool, FullOptions>> full;
    std::optional<bool> workDoneProgress;
};

// Inlay hint options
struct InlayHintOptions {
    std::optional<bool> resolveProvider;
    std::optional<bool> workDoneProgress;
};

// Linked editing range options
struct LinkedEditingRangeOptions {
    std::optional<bool> workDoneProgress;
};

// Call hierarchy options
struct CallHierarchyOptions {
    std::optional<bool> workDoneProgress;
};

// Workspace symbol options
struct WorkspaceSymbolOptions {
    std::optional<bool> resolveProvider;
    std::optional<bool> workDoneProgress;
};

// File operation filter
struct FileOperationFilter {
    std::optional<std::string> scheme;
    struct Pattern {
        std::string glob;
        std::optional<std::string> matches;  // "file" | "folder"
        struct Options {
            std::optional<bool> ignoreCase;
        };
        std::optional<Options> options;
    };
    Pattern pattern;
};

// File operation registration options
struct FileOperationRegistrationOptions {
    std::vector<FileOperationFilter> filters;
};

// Workspace server capabilities
struct WorkspaceServerCapabilities {
    struct WorkspaceFoldersCapabilities {
        std::optional<bool> supported;
        std::optional<std::variant<bool, std::string>> changeNotifications;
    };
    std::optional<WorkspaceFoldersCapabilities> workspaceFolders;
    struct FileOperationsCapabilities {
        std::optional<FileOperationRegistrationOptions> didCreate;
        std::optional<FileOperationRegistrationOptions> willCreate;
        std::optional<FileOperationRegistrationOptions> didRename;
        std::optional<FileOperationRegistrationOptions> willRename;
        std::optional<FileOperationRegistrationOptions> didDelete;
        std::optional<FileOperationRegistrationOptions> willDelete;
    };
    std::optional<FileOperationsCapabilities> fileOperations;
};

// Server capabilities
struct ServerCapabilities {
    std::optional<std::string> positionEncoding;
    std::optional<std::variant<TextDocumentSyncOptions, TextDocumentSyncKind>> textDocumentSync;
    std::optional<CompletionOptions> completionProvider;
    std::optional<bool> hoverProvider;
    std::optional<SignatureHelpOptions> signatureHelpProvider;
    std::optional<bool> declarationProvider;
    std::optional<bool> definitionProvider;
    std::optional<bool> typeDefinitionProvider;
    std::optional<bool> implementationProvider;
    std::optional<bool> referencesProvider;
    std::optional<bool> documentHighlightProvider;
    std::optional<bool> documentSymbolProvider;
    std::optional<std::variant<bool, CodeActionOptions>> codeActionProvider;
    std::optional<bool> codeLensProvider;
    std::optional<bool> documentLinkProvider;
    std::optional<bool> colorProvider;
    std::optional<bool> documentFormattingProvider;
    std::optional<bool> documentRangeFormattingProvider;
    std::optional<DocumentOnTypeFormattingOptions> documentOnTypeFormattingProvider;
    std::optional<std::variant<bool, RenameOptions>> renameProvider;
    std::optional<std::variant<bool, FoldingRangeOptions>> foldingRangeProvider;
    std::optional<bool> executeCommandProvider;
    std::optional<bool> selectionRangeProvider;
    std::optional<std::variant<bool, LinkedEditingRangeOptions>> linkedEditingRangeProvider;
    std::optional<std::variant<bool, CallHierarchyOptions>> callHierarchyProvider;
    std::optional<SemanticTokensOptions> semanticTokensProvider;
    std::optional<bool> monikerProvider;
    std::optional<bool> typeHierarchyProvider;
    std::optional<bool> inlineValueProvider;
    std::optional<std::variant<bool, InlayHintOptions>> inlayHintProvider;
    std::optional<bool> diagnosticProvider;
    std::optional<WorkspaceSymbolOptions> workspaceSymbolProvider;
    std::optional<WorkspaceServerCapabilities> workspace;
    std::optional<std::string> experimental;  // JSON object
};

// Initialize params
struct InitializeParams {
    std::optional<int> processId;
    std::optional<ClientInfo> clientInfo;
    std::optional<std::string> locale;
    std::optional<std::string> rootPath;  // Deprecated
    std::optional<std::string> rootUri;
    std::optional<std::string> initializationOptions;  // JSON object
    ClientCapabilities capabilities;
    std::optional<TraceValue> trace;
    std::optional<std::vector<WorkspaceFolder>> workspaceFolders;
};

// Initialize result
struct InitializeResult {
    ServerCapabilities capabilities;
    std::optional<ServerInfo> serverInfo;
};

// Initialize error data
struct InitializeError {
    bool retry = false;
};

// =============================================================================
// Document Did Open/Change/Close/Save
// =============================================================================

// Did open text document params
struct DidOpenTextDocumentParams {
    TextDocumentItem textDocument;
};

// Did change text document params
struct DidChangeTextDocumentParams {
    VersionedTextDocumentIdentifier textDocument;
    std::vector<TextDocumentContentChangeEvent> contentChanges;
};

// Text document save reason
enum class TextDocumentSaveReason {
    Manual = 1,
    AfterDelay = 2,
    FocusOut = 3
};

// Will save text document params
struct WillSaveTextDocumentParams {
    TextDocumentIdentifier textDocument;
    TextDocumentSaveReason reason = TextDocumentSaveReason::Manual;
};

// Did save text document params
struct DidSaveTextDocumentParams {
    TextDocumentIdentifier textDocument;
    std::optional<std::string> text;
};

// Did close text document params
struct DidCloseTextDocumentParams {
    TextDocumentIdentifier textDocument;
};

// =============================================================================
// File Watching Types
// =============================================================================

// Watch kind flags
namespace WatchKind {
    constexpr int Create = 1;
    constexpr int Change = 2;
    constexpr int Delete = 4;
}

// File system watcher
struct FileSystemWatcher {
    std::string globPattern;
    std::optional<int> kind;  // WatchKind flags
};

// File event
struct FileEvent {
    std::string uri;
    int type = 0;  // 1=created, 2=changed, 3=deleted
};

// Did change watched files params
struct DidChangeWatchedFilesParams {
    std::vector<FileEvent> changes;
};

// Registration options for file watchers
struct DidChangeWatchedFilesRegistrationOptions {
    std::vector<FileSystemWatcher> watchers;
};

// =============================================================================
// Registration/Unregistration Types
// =============================================================================

// Registration
struct Registration {
    std::string id;
    std::string method;
    std::optional<std::string> registerOptions;  // JSON object
};

// Registration params
struct RegistrationParams {
    std::vector<Registration> registrations;
};

// Unregistration
struct Unregistration {
    std::string id;
    std::string method;
};

// Unregistration params
struct UnregistrationParams {
    std::vector<Unregistration> unregistrations;
};

// =============================================================================
// Progress Types
// =============================================================================

// Progress token
using ProgressToken = std::variant<int, std::string>;

// Work done progress begin
struct WorkDoneProgressBegin {
    std::string kind = "begin";
    std::string title;
    std::optional<bool> cancellable;
    std::optional<std::string> message;
    std::optional<int> percentage;
};

// Work done progress report
struct WorkDoneProgressReport {
    std::string kind = "report";
    std::optional<bool> cancellable;
    std::optional<std::string> message;
    std::optional<int> percentage;
};

// Work done progress end
struct WorkDoneProgressEnd {
    std::string kind = "end";
    std::optional<std::string> message;
};

// =============================================================================
// Show Message Types
// =============================================================================

// Message type
enum class MessageType {
    Error = 1,
    Warning = 2,
    Info = 3,
    Log = 4
};

// Show message params
struct ShowMessageParams {
    MessageType type = MessageType::Info;
    std::string message;
};

// Message action item
struct MessageActionItem {
    std::string title;
};

// Show message request params
struct ShowMessageRequestParams {
    MessageType type = MessageType::Info;
    std::string message;
    std::optional<std::vector<MessageActionItem>> actions;
};

// Log message params
struct LogMessageParams {
    MessageType type = MessageType::Info;
    std::string message;
};

// =============================================================================
// Error Codes
// =============================================================================

namespace ErrorCodes {
    // JSON-RPC errors
    constexpr int ParseError = -32700;
    constexpr int InvalidRequest = -32600;
    constexpr int MethodNotFound = -32601;
    constexpr int InvalidParams = -32602;
    constexpr int InternalError = -32603;

    // LSP errors
    constexpr int ServerNotInitialized = -32002;
    constexpr int UnknownErrorCode = -32001;

    // Request cancelled
    constexpr int RequestCancelled = -32800;
    constexpr int ContentModified = -32801;

    // Server errors
    constexpr int ServerCancelled = -32802;
    constexpr int RequestFailed = -32803;
}

// Response error
struct ResponseError {
    int code = 0;
    std::string message;
    std::optional<std::string> data;  // JSON data
};

} // namespace maxon::lsp

#endif // MAXON_LSP_TYPES_H
