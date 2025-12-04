#ifndef MAXON_LSP_COMPLETION_H
#define MAXON_LSP_COMPLETION_H

#include "../lsp_types.h"
#include "../document_manager.h"
#include "../../compiler_api.h"
#include <string>
#include <vector>

namespace maxon_lsp {

// Alias for easier access to LSP types
using CompletionList = maxon::lsp::CompletionList;
using CompletionItem = maxon::lsp::CompletionItem;
using CompletionItemKind = maxon::lsp::CompletionItemKind;
using InsertTextFormat = maxon::lsp::InsertTextFormat;
using Position = maxon::lsp::Position;

// Context for completion
enum class CompletionContext {
    TopLevel,           // At top level (functions, structs, etc.)
    InsideFunction,     // Inside function body
    AfterDot,           // After '.' for member access
    AfterColon,         // After ':' for type annotation
    InTypePosition,     // Where a type is expected
    AfterEndQuote,      // After end ' for block label
    Unknown
};

/**
 * Provides code completion functionality for the Maxon LSP.
 *
 * Handles completion in various contexts:
 * - Keywords and snippets for control flow and declarations
 * - Type completions (built-in types, structs, enums, interfaces)
 * - Variable and parameter completions
 * - Function completions from current file and stdlib
 * - Member completions after '.' for struct fields and methods
 * - Block label completions after "end '"
 */
class CompletionProvider {
public:
    CompletionProvider();

    /**
     * Get completions at the given position.
     *
     * @param document The document to provide completions for
     * @param position The cursor position (0-based line/character)
     * @param cache Analysis cache with symbols and semantic info
     * @param stdlib Standard library symbols
     * @return CompletionList with matching items
     */
    CompletionList getCompletions(
        const Document& document,
        const Position& position,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );

private:
    // Determine what kind of completion context we're in
    CompletionContext determineContext(
        const Document& document,
        const Position& position
    );

    // Get the partial identifier being typed (for filtering)
    std::string getPrefix(const Document& document, const Position& position);

    // Get the type name before the dot (for member completion)
    std::string getTypeBeforeDot(
        const Document& document,
        const Position& position,
        const AnalysisCache* cache
    );

    // Completion providers for different contexts
    std::vector<CompletionItem> getKeywordCompletions(const std::string& prefix);
    std::vector<CompletionItem> getTypeCompletions(
        const std::string& prefix,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );
    std::vector<CompletionItem> getVariableCompletions(
        const std::string& prefix,
        const AnalysisCache* cache,
        const Position& position
    );
    std::vector<CompletionItem> getFunctionCompletions(
        const std::string& prefix,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );
    std::vector<CompletionItem> getMemberCompletions(
        const std::string& typeName,
        const std::string& prefix,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );
    std::vector<CompletionItem> getBlockLabelCompletions(
        const Document& document,
        const Position& position
    );

    // Build CompletionItem from different sources
    CompletionItem buildKeywordItem(const KeywordLSPInfo& keyword);
    CompletionItem buildVariableItem(
        const std::string& name,
        const std::string& type,
        bool isMutable
    );
    CompletionItem buildFunctionItem(const FunctionInfo& func);
    CompletionItem buildFunctionItem(const LSPSymbolInfo& symbol);
    CompletionItem buildStructItem(const StructInfo& structInfo);
    CompletionItem buildStructItem(const LSPSymbolInfo& symbol);
    CompletionItem buildEnumItem(const EnumInfo& enumInfo);
    CompletionItem buildEnumItem(const LSPSymbolInfo& symbol);
    CompletionItem buildInterfaceItem(const InterfaceInfo& interfaceInfo);
    CompletionItem buildInterfaceItem(const LSPSymbolInfo& symbol);
    CompletionItem buildFieldItem(const std::string& name, const std::string& type);
    CompletionItem buildMethodItem(const std::string& name, const std::string& signature);
    CompletionItem buildBlockLabelItem(const std::string& label);

    // Filter and sort completions
    std::vector<CompletionItem> filterByPrefix(
        std::vector<CompletionItem>& items,
        const std::string& prefix
    );
    void sortCompletions(std::vector<CompletionItem>& items, const std::string& prefix);

    // Check if string starts with prefix (case-insensitive)
    bool matchesPrefix(const std::string& text, const std::string& prefix);

    // Convert KeywordCompletionKind to LSP CompletionItemKind
    CompletionItemKind convertKind(KeywordCompletionKind kind);

    // Build function signature for display
    std::string buildFunctionSignature(const FunctionInfo& func);
    std::string buildFunctionInsertText(const FunctionInfo& func);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_COMPLETION_H
