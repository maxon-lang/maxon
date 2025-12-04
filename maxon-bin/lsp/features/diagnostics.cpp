#include "diagnostics.h"

namespace maxon_lsp {

std::vector<Diagnostic> DiagnosticsProvider::generateDiagnostics(
    const std::vector<ParseError>& parseErrors,
    const std::vector<SemanticError>& semanticErrors
) {
    std::vector<Diagnostic> diagnostics;
    diagnostics.reserve(parseErrors.size() + semanticErrors.size());

    // Convert parse errors
    for (size_t i = 0; i < parseErrors.size(); ++i) {
        Diagnostic diag = fromParseError(parseErrors[i]);
        // If no code was set, generate one
        if (!diag.code.has_value()) {
            diag.code = generateErrorCode("parse", static_cast<int>(i));
        }
        diagnostics.push_back(std::move(diag));
    }

    // Convert semantic errors
    for (size_t i = 0; i < semanticErrors.size(); ++i) {
        Diagnostic diag = fromSemanticError(semanticErrors[i]);
        // If no code was set, generate one
        if (!diag.code.has_value()) {
            diag.code = generateErrorCode("semantic", static_cast<int>(i));
        }
        diagnostics.push_back(std::move(diag));
    }

    return diagnostics;
}

Diagnostic DiagnosticsProvider::fromParseError(const ParseError& error) {
    Diagnostic diag;

    // Convert from 1-based compiler positions to 0-based LSP positions
    // Ensure we don't go negative if line/column is 0 or less
    int startLine = (error.line > 0) ? error.line - 1 : 0;
    int startChar = (error.column > 0) ? error.column - 1 : 0;
    int endLine = (error.endLine > 0) ? error.endLine - 1 : startLine;
    int endChar = (error.endColumn > 0) ? error.endColumn - 1 : startChar;

    // If end position equals start position, extend to at least one character
    // to make the diagnostic visible in the editor
    if (endLine == startLine && endChar <= startChar) {
        endChar = startChar + 1;
    }

    diag.range = maxon::lsp::Range(startLine, startChar, endLine, endChar);
    diag.severity = mapSeverity(error.severity);
    diag.source = "maxon";
    diag.message = error.message;

    return diag;
}

Diagnostic DiagnosticsProvider::fromSemanticError(const SemanticError& error) {
    Diagnostic diag;

    // Convert from 1-based compiler positions to 0-based LSP positions
    int startLine = (error.line > 0) ? error.line - 1 : 0;
    int startChar = (error.column > 0) ? error.column - 1 : 0;

    // SemanticError doesn't have endLine/endColumn, so we estimate
    // a reasonable range based on the error type and message
    int endLine = startLine;
    int endChar = startChar + 1;  // Default: single character

    // Try to provide better ranges based on error code patterns
    if (!error.code.empty()) {
        // Use the error code as the diagnostic code
        diag.code = error.code;

        // For certain error types, we can estimate better ranges
        // Type mismatch errors often involve the expression
        // Undefined errors often involve an identifier
        // These are conservative estimates; the actual token length
        // would require additional parser support
    }

    diag.range = maxon::lsp::Range(startLine, startChar, endLine, endChar);
    diag.severity = mapSeverity(error.severity);
    diag.source = "maxon";
    diag.message = error.message;

    return diag;
}

DiagnosticSeverity DiagnosticsProvider::mapSeverity(int compilerSeverity) {
    switch (compilerSeverity) {
        case 1:
            return DiagnosticSeverity::Error;
        case 2:
            return DiagnosticSeverity::Warning;
        case 3:
            return DiagnosticSeverity::Information;
        case 4:
            return DiagnosticSeverity::Hint;
        default:
            // Unknown severity defaults to Error for safety
            return DiagnosticSeverity::Error;
    }
}

std::string DiagnosticsProvider::generateErrorCode(const std::string& errorType, int index) {
    // Generate a code like "maxon-parse-0" or "maxon-semantic-5"
    // This provides filtering capability and uniqueness
    return "maxon-" + errorType + "-" + std::to_string(index);
}

} // namespace maxon_lsp
