#ifndef LSP_TYPES_H
#define LSP_TYPES_H

#include <string>
#include <vector>
#include <optional>

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

} // namespace lsp

#endif // LSP_TYPES_H
