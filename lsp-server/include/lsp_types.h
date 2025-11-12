#ifndef LSP_TYPES_H
#define LSP_TYPES_H

#include <string>
#include <vector>
#include <optional>
#include <map>

namespace lsp {

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
    int kind; // 1=Text, 3=Function, 6=Variable, 14=Keyword
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
    int kind; // 12=Function, 13=Variable
    Location location;
    std::optional<std::string> containerName;
};

struct DocumentSymbol {
    std::string name;
    int kind;
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
