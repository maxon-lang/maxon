#ifndef MAXON_LSP_DIAGNOSTICS_H
#define MAXON_LSP_DIAGNOSTICS_H

#include "../lsp_types.h"
#include "../../parser.h"
#include "../../semantic_analyzer.h"
#include <vector>

namespace maxon_lsp {

// Alias for easier access to LSP types
using Diagnostic = maxon::lsp::Diagnostic;
using DiagnosticSeverity = maxon::lsp::DiagnosticSeverity;
using DiagnosticRelatedInformation = maxon::lsp::DiagnosticRelatedInformation;

/**
 * Provides conversion of compiler errors to LSP diagnostics.
 *
 * The compiler uses 1-based line/column numbers, while LSP uses 0-based.
 * This class handles the conversion and creates properly structured
 * Diagnostic objects for client consumption.
 */
class DiagnosticsProvider {
public:
    /**
     * Convert compiler errors to LSP diagnostics.
     *
     * Combines parse errors and semantic errors into a single diagnostic list.
     * All diagnostics are tagged with source "maxon" for identification.
     *
     * @param parseErrors Parse errors from the parser
     * @param semanticErrors Semantic errors from the semantic analyzer
     * @return Vector of LSP Diagnostic objects
     */
    static std::vector<Diagnostic> generateDiagnostics(
        const std::vector<ParseError>& parseErrors,
        const std::vector<SemanticError>& semanticErrors
    );

    /**
     * Convert a single parse error to an LSP diagnostic.
     *
     * @param error The parse error to convert
     * @return LSP Diagnostic with proper range and severity
     */
    static Diagnostic fromParseError(const ParseError& error);

    /**
     * Convert a single semantic error to an LSP diagnostic.
     *
     * @param error The semantic error to convert
     * @return LSP Diagnostic with proper range and severity
     */
    static Diagnostic fromSemanticError(const SemanticError& error);

private:
    /**
     * Map compiler severity to LSP DiagnosticSeverity.
     *
     * Compiler severity levels:
     *   1 = Error
     *   2 = Warning
     *   3 = Information
     *   4 = Hint
     *
     * @param compilerSeverity The compiler's severity value (1-4)
     * @return Corresponding LSP DiagnosticSeverity
     */
    static DiagnosticSeverity mapSeverity(int compilerSeverity);

    /**
     * Generate an error code string for filtering and identification.
     *
     * Format: "maxon-{errorType}-{index}" for parse errors
     *         or the error's code field for semantic errors.
     *
     * @param errorType The type of error ("parse" or "semantic")
     * @param index Index in the error list (for uniqueness)
     * @return Error code string
     */
    static std::string generateErrorCode(const std::string& errorType, int index);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_DIAGNOSTICS_H
