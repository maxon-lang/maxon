#include "signature_help.h"
#include <sstream>

namespace maxon_lsp {

std::optional<SignatureHelp> SignatureHelpProvider::getSignatureHelp(
    const Document& document,
    const Position& position,
    const AnalysisCache* cache,
    const StdlibSymbols& stdlib
) {
    // Find the call context at the cursor position
    CallContext context = findCallContext(document, position);

    if (!context.valid || context.functionName.empty()) {
        return std::nullopt;
    }

    // Look up the function signature
    auto signatureOpt = lookupFunction(context.functionName, cache, stdlib);
    if (!signatureOpt.has_value()) {
        return std::nullopt;
    }

    // Build the signature help response
    SignatureHelp help;
    help.signatures.push_back(std::move(signatureOpt.value()));
    help.activeSignature = 0;
    help.activeParameter = context.argumentIndex;

    return help;
}

SignatureHelpProvider::CallContext SignatureHelpProvider::findCallContext(
    const Document& document,
    const Position& position
) {
    CallContext result;
    result.valid = false;
    result.argumentIndex = 0;

    // Get the line up to the cursor
    if (position.line < 0 || position.line >= document.getLineCount()) {
        return result;
    }

    // Collect all text from start of document to cursor position
    // We need to handle multiline function calls
    std::string textBeforeCursor;
    for (int i = 0; i <= position.line && i < document.getLineCount(); ++i) {
        std::string line = document.getLine(i);
        if (i == position.line) {
            // Only take text up to cursor on the current line
            if (position.character >= 0 && static_cast<size_t>(position.character) <= line.length()) {
                textBeforeCursor += line.substr(0, position.character);
            } else {
                textBeforeCursor += line;
            }
        } else {
            textBeforeCursor += line + "\n";
        }
    }

    if (textBeforeCursor.empty()) {
        return result;
    }

    // Scan backwards to find the innermost unclosed '('
    // Track nesting levels for different bracket types
    int parenDepth = 0;
    int bracketDepth = 0;
    int braceDepth = 0;
    bool inString = false;
    bool inCharLiteral = false;
    char stringDelim = 0;
    int commaCount = 0;

    // Position of the opening paren we're looking for
    int openParenPos = -1;

    // Scan from the end backwards
    for (int i = static_cast<int>(textBeforeCursor.length()) - 1; i >= 0; --i) {
        char c = textBeforeCursor[i];
        char prevChar = (i > 0) ? textBeforeCursor[i - 1] : 0;

        // Handle escape sequences in strings (check if previous char is backslash)
        // But only if we're currently in a string
        if (inString && prevChar == '\\') {
            // This character is escaped, skip it
            continue;
        }

        // Handle string literals
        if (c == '"' && !inCharLiteral) {
            if (inString && stringDelim == '"') {
                inString = false;
                stringDelim = 0;
            } else if (!inString) {
                inString = true;
                stringDelim = '"';
            }
            continue;
        }

        // Handle character literals
        if (c == '\'' && !inString) {
            // Simple toggle - character literals are typically single chars
            inCharLiteral = !inCharLiteral;
            continue;
        }

        // Skip if inside string or char literal
        if (inString || inCharLiteral) {
            continue;
        }

        // Track bracket nesting (going backwards, so close increases, open decreases)
        switch (c) {
            case ')':
                parenDepth++;
                break;
            case '(':
                if (parenDepth > 0) {
                    parenDepth--;
                } else {
                    // Found an unclosed open paren - this is our call site
                    openParenPos = i;

                    // Now count commas between this paren and cursor
                    // We already have commaCount from earlier scanning at depth 0

                    // Extract function name before this paren
                    result.callStart = Position(0, openParenPos);  // Will be adjusted below

                    // Convert position back to line/character
                    int lineNum = 0;
                    int charInLine = 0;
                    for (int j = 0; j < openParenPos; ++j) {
                        if (textBeforeCursor[j] == '\n') {
                            lineNum++;
                            charInLine = 0;
                        } else {
                            charInLine++;
                        }
                    }
                    result.callStart = Position(lineNum, charInLine);

                    // Get function name
                    result.functionName = getIdentifierBefore(document, result.callStart);
                    result.argumentIndex = commaCount;
                    result.valid = !result.functionName.empty();
                    return result;
                }
                break;
            case ']':
                bracketDepth++;
                break;
            case '[':
                if (bracketDepth > 0) {
                    bracketDepth--;
                }
                break;
            case '}':
                braceDepth++;
                break;
            case '{':
                if (braceDepth > 0) {
                    braceDepth--;
                }
                break;
            case ',':
                // Only count commas at the current paren depth (depth 0)
                if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0) {
                    commaCount++;
                }
                break;
            default:
                break;
        }
    }

    return result;
}

std::optional<SignatureInformation> SignatureHelpProvider::lookupFunction(
    const std::string& name,
    const AnalysisCache* cache,
    const StdlibSymbols& stdlib
) {
    // First, look in the analysis cache (local functions)
    if (cache != nullptr) {
        auto it = cache->functions.find(name);
        if (it != cache->functions.end()) {
            return buildSignature(it->second);
        }

        // Also check struct methods - methods are stored as "StructName.methodName"
        // If the name contains a dot, look for it directly
        // Otherwise, we might need to search all methods
        for (const auto& [funcName, funcInfo] : cache->functions) {
            // Check if this is a method matching our name (after the dot)
            size_t dotPos = funcName.rfind('.');
            if (dotPos != std::string::npos) {
                std::string methodName = funcName.substr(dotPos + 1);
                if (methodName == name) {
                    return buildSignature(funcInfo);
                }
            }
        }
    }

    // Look in stdlib functions
    for (const auto& symbol : stdlib.functions) {
        if (symbol.name == name) {
            return buildSignature(symbol);
        }
    }

    return std::nullopt;
}

SignatureInformation SignatureHelpProvider::buildSignature(
    const FunctionInfo& func,
    const std::string& doc
) {
    SignatureInformation sig;

    // Build the signature label: "function name(param1 type1, param2 type2) returnType"
    std::ostringstream label;
    label << "function " << func.name << "(";

    for (size_t i = 0; i < func.parameters.size(); ++i) {
        if (i > 0) {
            label << ", ";
        }
        label << func.parameters[i].name << " " << func.parameters[i].type;
    }

    label << ")";

    if (!func.returnType.empty() && func.returnType != "void") {
        label << " " << func.returnType;
    }

    sig.label = label.str();

    // Add documentation if provided
    if (!doc.empty()) {
        MarkupContent markup;
        markup.kind = MarkupKind::Markdown;
        markup.value = doc;
        sig.documentation = markup;
    }

    // Build parameter information
    std::vector<ParameterInformation> params;
    for (const auto& param : func.parameters) {
        params.push_back(buildParameter(param.name, param.type));
    }
    sig.parameters = std::move(params);

    return sig;
}

SignatureInformation SignatureHelpProvider::buildSignature(const LSPSymbolInfo& symbol) {
    SignatureInformation sig;

    // Build the signature label
    std::ostringstream label;
    label << "function " << symbol.name << "(";

    for (size_t i = 0; i < symbol.parameters.size(); ++i) {
        if (i > 0) {
            label << ", ";
        }
        label << symbol.parameters[i].name << " " << symbol.parameters[i].type;
    }

    label << ")";

    if (!symbol.returnType.empty() && symbol.returnType != "void") {
        label << " " << symbol.returnType;
    }

    sig.label = label.str();

    // Add documentation if available
    if (!symbol.documentation.empty()) {
        MarkupContent markup;
        markup.kind = MarkupKind::Markdown;
        markup.value = symbol.documentation;
        sig.documentation = markup;
    }

    // Build parameter information
    std::vector<ParameterInformation> params;
    for (const auto& param : symbol.parameters) {
        params.push_back(buildParameter(param.name, param.type));
    }
    sig.parameters = std::move(params);

    return sig;
}

ParameterInformation SignatureHelpProvider::buildParameter(
    const std::string& name,
    const std::string& type,
    const std::string& doc
) {
    ParameterInformation param;

    // Label is "name type" (Maxon style - type comes after name)
    param.label = name + " " + type;

    // Add documentation if provided
    if (!doc.empty()) {
        MarkupContent markup;
        markup.kind = MarkupKind::Markdown;
        markup.value = doc;
        param.documentation = markup;
    }

    return param;
}

int SignatureHelpProvider::countArgumentsBefore(
    const Document& document,
    const Position& start,
    const Position& cursor
) {
    // This is now handled inline in findCallContext for efficiency,
    // but we keep this method for potential future use or refactoring

    int commaCount = 0;
    int parenDepth = 0;
    int bracketDepth = 0;
    int braceDepth = 0;
    bool inString = false;
    bool inCharLiteral = false;

    // Iterate through lines from start to cursor
    for (int line = start.line; line <= cursor.line && line < document.getLineCount(); ++line) {
        std::string lineText = document.getLine(line);

        int startChar = (line == start.line) ? start.character + 1 : 0;  // +1 to skip the '('
        int endChar = (line == cursor.line) ? cursor.character : static_cast<int>(lineText.length());

        for (int col = startChar; col < endChar && col < static_cast<int>(lineText.length()); ++col) {
            char c = lineText[col];
            char prevChar = (col > 0) ? lineText[col - 1] : 0;

            // Skip escaped characters in strings
            if (inString && prevChar == '\\') {
                continue;
            }

            // Handle strings
            if (c == '"' && !inCharLiteral) {
                inString = !inString;
                continue;
            }

            // Handle char literals
            if (c == '\'' && !inString) {
                inCharLiteral = !inCharLiteral;
                continue;
            }

            if (inString || inCharLiteral) {
                continue;
            }

            // Track nesting
            switch (c) {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case ',':
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0) {
                        commaCount++;
                    }
                    break;
                default:
                    break;
            }
        }
    }

    return commaCount;
}

std::string SignatureHelpProvider::getIdentifierBefore(
    const Document& document,
    const Position& position
) {
    if (position.line < 0 || position.line >= document.getLineCount()) {
        return "";
    }

    std::string line = document.getLine(position.line);

    if (position.character <= 0 || static_cast<size_t>(position.character) > line.length()) {
        // If position is at start of line or invalid, check previous lines
        // This handles the case where the function name is on a previous line
        if (position.line > 0 && position.character == 0) {
            // Look at end of previous line
            std::string prevLine = document.getLine(position.line - 1);
            // Trim trailing whitespace and get identifier
            int endPos = static_cast<int>(prevLine.length());
            while (endPos > 0 && std::isspace(static_cast<unsigned char>(prevLine[endPos - 1]))) {
                endPos--;
            }
            if (endPos > 0) {
                int startPos = endPos;
                while (startPos > 0 &&
                       (std::isalnum(static_cast<unsigned char>(prevLine[startPos - 1])) ||
                        prevLine[startPos - 1] == '_')) {
                    startPos--;
                }
                return prevLine.substr(startPos, endPos - startPos);
            }
        }
        return "";
    }

    // Scan backwards from position to find the identifier
    int endPos = position.character;

    // Skip any whitespace before the '('
    while (endPos > 0 && std::isspace(static_cast<unsigned char>(line[endPos - 1]))) {
        endPos--;
    }

    if (endPos == 0) {
        return "";
    }

    // Check if we're dealing with a method call (after a '.')
    // In that case, we want just the method name, not the whole chain
    int startPos = endPos;
    while (startPos > 0 &&
           (std::isalnum(static_cast<unsigned char>(line[startPos - 1])) ||
            line[startPos - 1] == '_')) {
        startPos--;
    }

    if (startPos == endPos) {
        return "";
    }

    return line.substr(startPos, endPos - startPos);
}

} // namespace maxon_lsp
