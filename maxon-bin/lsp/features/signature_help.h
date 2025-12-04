#pragma once
#include "../lsp_types.h"
#include "../document_manager.h"
#include "../../compiler_api.h"
#include <optional>

namespace maxon_lsp {

// Aliases for LSP types used throughout this module
using Position = maxon::lsp::Position;
using SignatureHelp = maxon::lsp::SignatureHelp;
using SignatureInformation = maxon::lsp::SignatureInformation;
using ParameterInformation = maxon::lsp::ParameterInformation;
using MarkupContent = maxon::lsp::MarkupContent;
using MarkupKind = maxon::lsp::MarkupKind;

/**
 * Provides signature help for function calls.
 *
 * Signature help displays parameter information when the user is typing
 * a function call, triggered by '(' and ','. It tracks which parameter
 * is currently being typed based on cursor position.
 */
class SignatureHelpProvider {
public:
    /**
     * Get signature help at the given position.
     *
     * Returns signature information if the cursor is inside a function call,
     * including the function signature and which parameter is currently active.
     *
     * @param document The document being edited
     * @param position The cursor position (0-based line/character)
     * @param cache Analysis cache with function information
     * @param stdlib Standard library symbols
     * @return SignatureHelp if in a function call, nullopt otherwise
     */
    std::optional<SignatureHelp> getSignatureHelp(
        const Document& document,
        const Position& position,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );

private:
    /**
     * Context information about a function call at a given position.
     * Captures which function is being called and which argument the cursor is in.
     */
    struct CallContext {
        std::string functionName;  // Name of the function being called
        int argumentIndex;         // Which argument the cursor is in (0-based)
        Position callStart;        // Position of opening paren
        bool valid;                // Whether we found a valid call context
    };

    /**
     * Find the function call context at the given position.
     *
     * Scans backwards from the cursor to find the innermost function call,
     * handling nested parentheses, brackets, braces, and strings.
     *
     * @param document The document to search
     * @param position The cursor position
     * @return CallContext with function name and argument index
     */
    CallContext findCallContext(const Document& document, const Position& position);

    /**
     * Look up a function by name in the analysis cache or stdlib.
     *
     * @param name The function name to look up
     * @param cache Analysis cache with local function information
     * @param stdlib Standard library symbols
     * @return SignatureInformation if found, nullopt otherwise
     */
    std::optional<SignatureInformation> lookupFunction(
        const std::string& name,
        const AnalysisCache* cache,
        const StdlibSymbols& stdlib
    );

    /**
     * Build SignatureInformation from a FunctionInfo object.
     *
     * Creates a signature label like "function name(param1: type1, param2: type2) returnType"
     * and populates parameter information for each parameter.
     *
     * @param func The function information
     * @param doc Optional documentation string
     * @return SignatureInformation with label and parameters
     */
    SignatureInformation buildSignature(const FunctionInfo& func, const std::string& doc = "");

    /**
     * Build SignatureInformation from an LSPSymbolInfo object.
     *
     * Used for stdlib functions which are stored as LSPSymbolInfo.
     *
     * @param symbol The symbol information
     * @return SignatureInformation with label and parameters
     */
    SignatureInformation buildSignature(const LSPSymbolInfo& symbol);

    /**
     * Build ParameterInformation for a single parameter.
     *
     * @param name Parameter name
     * @param type Parameter type
     * @param doc Optional documentation
     * @return ParameterInformation with label
     */
    ParameterInformation buildParameter(const std::string& name, const std::string& type, const std::string& doc = "");

    /**
     * Count the number of arguments before the cursor position.
     *
     * Counts commas between the opening parenthesis and the cursor,
     * skipping commas inside nested structures and strings.
     *
     * @param document The document
     * @param start Position of the opening parenthesis
     * @param cursor Current cursor position
     * @return Number of commas (arguments) before cursor
     */
    int countArgumentsBefore(const Document& document, const Position& start, const Position& cursor);

    /**
     * Extract identifier before a given position.
     *
     * Used to get the function name before an opening parenthesis.
     *
     * @param document The document
     * @param position Position just before the '('
     * @return The identifier string, or empty if none found
     */
    std::string getIdentifierBefore(const Document& document, const Position& position);
};

} // namespace maxon_lsp
