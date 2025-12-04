#ifndef MAXON_LSP_REFERENCES_H
#define MAXON_LSP_REFERENCES_H

#include "../lsp_types.h"
#include "../document_manager.h"
#include "../../compiler_api.h"
#include "../../ast.h"
#include <vector>

namespace maxon_lsp {

// Alias for easier access to LSP types
using Location = maxon::lsp::Location;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;

/**
 * Provides "Find All References" functionality for Maxon source code.
 *
 * Finds all references to a symbol at a given position, including:
 * - Variable references (local variables, parameters)
 * - Function calls and definitions
 * - Type references (structs, enums, interfaces)
 * - Field accesses
 */
class ReferencesProvider {
public:
    /**
     * Find all references to the symbol at the given position.
     *
     * @param document The document containing the symbol
     * @param position The cursor position on the symbol
     * @param includeDeclaration Whether to include the declaration in results
     * @param cache Analysis cache with AST and semantic info
     * @param workspaceRoot Workspace root for multi-file search (future)
     * @return Vector of locations where the symbol is referenced
     */
    std::vector<Location> findReferences(
        const Document& document,
        const Position& position,
        bool includeDeclaration,
        const AnalysisCache* cache,
        const std::string& workspaceRoot
    );

private:
    // Symbol classification for targeted reference finding
    enum class SymbolKind {
        Variable,   // Local variable or parameter
        Function,   // Function or method
        Type,       // Struct, enum, or interface
        Field,      // Struct field
        Parameter,  // Function parameter
        EnumCase,   // Enum case
        Unknown
    };

    // Information about the symbol at the cursor
    struct SymbolInfo {
        std::string name;
        SymbolKind kind;
        std::string containingType;    // For fields/methods: the struct/enum name
        std::string containingFunction; // For parameters/locals: the function name
        SourceRange declarationRange;  // Location of the declaration
    };

    /**
     * Identify the symbol at the given position.
     *
     * Determines the symbol name, its kind, and any containing context
     * (like struct name for fields or function name for parameters).
     *
     * @param document The document to search in
     * @param position The cursor position
     * @param cache Analysis cache with semantic info
     * @return Information about the symbol, or nullopt if no symbol found
     */
    std::optional<SymbolInfo> getSymbolInfo(
        const Document& document,
        const Position& position,
        const AnalysisCache* cache
    );

    /**
     * Get the identifier token at the given position.
     *
     * @param document The document to search in
     * @param position The cursor position
     * @return The identifier string, or empty string if not on an identifier
     */
    std::string getIdentifierAtPosition(const Document& document, const Position& position);

    // Reference finding by symbol kind

    /**
     * Find all references to a variable within its scope.
     */
    std::vector<Location> findVariableReferences(
        const std::string& name,
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Find all references to a function (calls and definition).
     */
    std::vector<Location> findFunctionReferences(
        const std::string& name,
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Find all references to a type (in type annotations, instantiations, etc.).
     */
    std::vector<Location> findTypeReferences(
        const std::string& name,
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Find all references to a struct field (in member accesses).
     */
    std::vector<Location> findFieldReferences(
        const std::string& structName,
        const std::string& fieldName,
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Find all references to a function parameter within the function body.
     */
    std::vector<Location> findParameterReferences(
        const std::string& paramName,
        const std::string& functionName,
        const Document& document,
        const AnalysisCache* cache
    );

    /**
     * Find all references to an enum case.
     */
    std::vector<Location> findEnumCaseReferences(
        const std::string& enumName,
        const std::string& caseName,
        const Document& document,
        const AnalysisCache* cache
    );

    // AST traversal helpers

    /**
     * Collect all identifier references matching the target name.
     * Visits variable expressions, function calls, type annotations, etc.
     */
    void collectReferencesInAST(
        const ProgramAST* ast,
        const std::string& targetName,
        SymbolKind targetKind,
        const std::string& containingType,
        std::vector<Location>& refs,
        const std::string& uri
    );

    /**
     * Visit a function and collect references within it.
     */
    void visitFunction(
        const FunctionAST* func,
        const std::string& targetName,
        SymbolKind targetKind,
        const std::string& containingType,
        std::vector<Location>& refs,
        const std::string& uri
    );

    /**
     * Visit a statement and collect references within it.
     */
    void visitStatement(
        const StmtAST* stmt,
        const std::string& targetName,
        SymbolKind targetKind,
        const std::string& containingType,
        std::vector<Location>& refs,
        const std::string& uri
    );

    /**
     * Visit an expression and collect references within it.
     */
    void visitExpression(
        const ExprAST* expr,
        const std::string& targetName,
        SymbolKind targetKind,
        const std::string& containingType,
        std::vector<Location>& refs,
        const std::string& uri
    );

    /**
     * Build a Location from source coordinates.
     * Converts 1-based compiler positions to 0-based LSP positions.
     */
    Location buildLocation(
        const std::string& uri,
        int line,
        int col,
        int endLine = -1,
        int endCol = -1
    );

    /**
     * Check if a position is within a source range.
     */
    bool isPositionInRange(const Position& pos, const SourceRange& range);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_REFERENCES_H
