#ifndef MAXON_LSP_DOCUMENT_SYMBOLS_H
#define MAXON_LSP_DOCUMENT_SYMBOLS_H

#include "../lsp_types.h"
#include "../document_manager.h"
#include "../../compiler_api.h"
#include "../../ast.h"
#include <vector>

namespace maxon_lsp {

// Aliases for easier access to LSP types
using DocumentSymbol = maxon::lsp::DocumentSymbol;
using SymbolInformation = maxon::lsp::SymbolInformation;
using SymbolKind = maxon::lsp::SymbolKind;
using Range = maxon::lsp::Range;
using Position = maxon::lsp::Position;
using Location = maxon::lsp::Location;

/**
 * Provides document symbol information for outline view and symbol navigation.
 *
 * Supports hierarchical symbols for:
 * - Functions (top-level)
 * - Structs with nested fields and methods
 * - Enums with nested cases
 * - Interfaces with nested method signatures
 *
 * Also provides flat symbol information for older LSP clients.
 */
class DocumentSymbolsProvider {
public:
    /**
     * Get hierarchical document symbols for outline view.
     *
     * Returns a tree of symbols where structs, enums, and interfaces
     * contain their members as children.
     *
     * @param document The document to extract symbols from
     * @param cache Analysis cache containing the AST
     * @return Vector of top-level DocumentSymbol with nested children
     */
    std::vector<DocumentSymbol> getDocumentSymbols(
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Get flat symbol information for older clients.
     *
     * Returns a flat list of all symbols with containerName set
     * for nested symbols.
     *
     * @param document The document to extract symbols from
     * @param cache Analysis cache containing the AST
     * @return Vector of SymbolInformation
     */
    std::vector<SymbolInformation> getSymbolInformation(
        const Document& document,
        const AnalysisCache* cache
    );

private:
    /**
     * Build DocumentSymbol from a function AST node.
     *
     * @param func The function AST node
     * @return DocumentSymbol representing the function
     */
    DocumentSymbol buildFunctionSymbol(const FunctionAST* func);

    /**
     * Build DocumentSymbol from a struct definition AST node.
     * Includes fields and methods as children.
     *
     * @param structDef The struct definition AST node
     * @return DocumentSymbol representing the struct with children
     */
    DocumentSymbol buildStructSymbol(const StructDefAST* structDef);

    /**
     * Build DocumentSymbol from an enum definition AST node.
     * Includes enum cases as children.
     *
     * @param enumDef The enum definition AST node
     * @return DocumentSymbol representing the enum with children
     */
    DocumentSymbol buildEnumSymbol(const EnumDefAST* enumDef);

    /**
     * Build DocumentSymbol from an interface definition AST node.
     * Includes method signatures as children.
     *
     * @param iface The interface definition AST node
     * @return DocumentSymbol representing the interface with children
     */
    DocumentSymbol buildInterfaceSymbol(const InterfaceDefAST* iface);

    /**
     * Build DocumentSymbol for a struct field.
     *
     * @param name Field name
     * @param type Field type
     * @param line 1-based line number
     * @param col 1-based column number
     * @return DocumentSymbol representing the field
     */
    DocumentSymbol buildFieldSymbol(const std::string& name, const std::string& type,
                                    int line, int col);

    /**
     * Build DocumentSymbol for a method.
     *
     * @param method The method function AST node
     * @return DocumentSymbol representing the method
     */
    DocumentSymbol buildMethodSymbol(const FunctionAST* method);

    /**
     * Build DocumentSymbol for an enum case.
     *
     * @param name Case name
     * @param line 1-based line number
     * @param col 1-based column number
     * @return DocumentSymbol representing the enum case
     */
    DocumentSymbol buildEnumCaseSymbol(const std::string& name, int line, int col);

    /**
     * Build DocumentSymbol for a function parameter.
     *
     * @param name Parameter name
     * @param type Parameter type
     * @param line 1-based line number
     * @param col 1-based column number
     * @return DocumentSymbol representing the parameter
     */
    DocumentSymbol buildParameterSymbol(const std::string& name, const std::string& type,
                                        int line, int col);

    /**
     * Build DocumentSymbol for an interface method signature.
     *
     * @param method The interface method signature
     * @return DocumentSymbol representing the method signature
     */
    DocumentSymbol buildInterfaceMethodSymbol(const InterfaceMethodSignature& method);

    /**
     * Map a kind string to LSP SymbolKind.
     *
     * @param kind Kind string ("function", "struct", "enum", etc.)
     * @return Corresponding SymbolKind enum value
     */
    SymbolKind mapSymbolKind(const std::string& kind);

    /**
     * Build an LSP Range from source positions.
     * Converts 1-based compiler positions to 0-based LSP positions.
     *
     * @param startLine 1-based start line
     * @param startCol 1-based start column
     * @param endLine 1-based end line
     * @param endCol 1-based end column
     * @return 0-based LSP Range
     */
    Range buildRange(int startLine, int startCol, int endLine, int endCol);

    /**
     * Build an LSP Range for just a symbol name (selection range).
     * Converts 1-based compiler position to 0-based LSP position.
     *
     * @param line 1-based line number
     * @param col 1-based column number
     * @param nameLength Length of the symbol name
     * @return 0-based LSP Range covering just the name
     */
    Range buildSelectionRange(int line, int col, int nameLength);

    /**
     * Build a detail string for a function signature.
     *
     * @param func The function AST node
     * @return Detail string showing return type and parameters
     */
    std::string buildFunctionDetail(const FunctionAST* func);

    /**
     * Build a detail string for an interface method signature.
     *
     * @param method The interface method signature
     * @return Detail string showing return type and parameters
     */
    std::string buildInterfaceMethodDetail(const InterfaceMethodSignature& method);

    // Store document URI for SymbolInformation locations
    std::string currentUri_;
};

} // namespace maxon_lsp

#endif // MAXON_LSP_DOCUMENT_SYMBOLS_H
