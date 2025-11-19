#ifndef LSP_TYPES_H
#define LSP_TYPES_H

#include <string>
#include <vector>
#include <optional>
#include <map>

namespace lsp {

// LSP CompletionItemKind enum
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

// LSP SymbolKind enum
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

struct Position {
    int line;
    int character;
};

struct Range {
    Position start;
    Position end;
};

struct Location {
    std::string uri;
    Range range;
};

struct Diagnostic {
    Range range;
    std::string message;
    int severity; // 1=Error, 2=Warning, 3=Information, 4=Hint
    std::optional<std::string> source;
    std::optional<std::string> code; // Diagnostic code for identifying specific warnings
};

struct TextDocumentIdentifier {
    std::string uri;
};

struct VersionedTextDocumentIdentifier : TextDocumentIdentifier {
    int version;
};

struct TextDocumentItem {
    std::string uri;
    std::string languageId;
    int version;
    std::string text;
};

struct TextDocumentContentChangeEvent {
    std::optional<Range> range;
    std::string text;
};

struct CompletionItem {
    std::string label;
    CompletionItemKind kind;
    std::string detail;
    std::string documentation;
    std::optional<std::string> insertText;
};

struct Hover {
    std::string contents;
    std::optional<Range> range;
};

struct SymbolInformation {
    std::string name;
    SymbolKind kind;
    Location location;
    std::optional<std::string> containerName;
};

struct DocumentSymbol {
    std::string name;
    SymbolKind kind;
    Range range;
    Range selectionRange;
    std::vector<DocumentSymbol> children;
};

struct TextEdit {
    Range range;
    std::string newText;
};

struct WorkspaceEdit {
    std::map<std::string, std::vector<TextEdit>> changes;
};

struct Command {
    std::string title;
    std::string command;
    std::optional<std::vector<std::string>> arguments;
};

struct CodeAction {
    std::string title;
    std::optional<std::string> kind; // "quickfix", "refactor", etc.
    std::optional<std::vector<Diagnostic>> diagnostics;
    std::optional<WorkspaceEdit> edit;
    std::optional<Command> command;
};

} // namespace lsp

#endif // LSP_TYPES_H
