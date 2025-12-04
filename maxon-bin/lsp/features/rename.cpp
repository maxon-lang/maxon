#include "rename.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include <algorithm>
#include <regex>

namespace maxon_lsp {

std::optional<PrepareRenameResult> RenameProvider::prepareRename(
    const Document& document,
    const Position& position,
    const AnalysisCache* cache
) {
    // Get the symbol at the cursor position
    std::string symbol = getSymbolAtPosition(document, position);
    if (symbol.empty()) {
        return std::nullopt;
    }

    // Check if this symbol can be renamed
    if (!canRename(symbol, cache)) {
        return std::nullopt;
    }

    // Return the symbol range and current name as placeholder
    PrepareRenameResult result;
    result.range = getSymbolRange(document, position);
    result.placeholder = symbol;
    return result;
}

std::optional<WorkspaceEdit> RenameProvider::rename(
    const Document& document,
    const Position& position,
    const std::string& newName,
    const AnalysisCache* cache,
    const std::string& workspaceRoot
) {
    // Get the symbol at the cursor position
    std::string symbol = getSymbolAtPosition(document, position);
    if (symbol.empty()) {
        return std::nullopt;
    }

    // Validate the new name is a valid identifier
    if (!isValidIdentifier(newName)) {
        return std::nullopt;
    }

    // Check for naming conflicts
    if (hasNamingConflict(newName, cache, document, position)) {
        return std::nullopt;
    }

    // Check if this symbol can be renamed (not keyword, not stdlib)
    if (!canRename(symbol, cache)) {
        return std::nullopt;
    }

    // Find all references using ReferencesProvider
    std::vector<Location> refs = referencesProvider_.findReferences(
        document,
        position,
        true,  // include declaration
        cache,
        workspaceRoot
    );

    if (refs.empty()) {
        return std::nullopt;
    }

    // For functions, structs, enums, and interfaces, find and add the end label
    std::string kind = getSymbolKind(symbol, cache);

    if (kind == "function") {
        auto endLabel = findFunctionEndLabel(document, symbol, cache);
        if (endLabel) {
            refs.push_back(*endLabel);
        }
    } else if (kind == "struct") {
        auto endLabel = findStructEndLabel(document, symbol, cache);
        if (endLabel) {
            refs.push_back(*endLabel);
        }
    } else if (kind == "enum") {
        auto endLabel = findEnumEndLabel(document, symbol, cache);
        if (endLabel) {
            refs.push_back(*endLabel);
        }
    } else if (kind == "interface") {
        auto endLabel = findInterfaceEndLabel(document, symbol, cache);
        if (endLabel) {
            refs.push_back(*endLabel);
        }
    }

    // Build and return the workspace edit
    return buildWorkspaceEdit(refs, newName);
}

bool RenameProvider::canRename(const std::string& symbolName, const AnalysisCache* cache) {
    // Cannot rename keywords or built-in types
    if (isKeywordOrBuiltin(symbolName)) {
        return false;
    }

    // Check if symbol exists in cache (must be a known symbol)
    if (cache) {
        // Check if it's a variable
        if (cache->variables.find(symbolName) != cache->variables.end()) {
            return true;
        }

        // Check if it's a function
        if (cache->functions.find(symbolName) != cache->functions.end()) {
            return true;
        }

        // Check if it's a struct
        if (cache->structs.find(symbolName) != cache->structs.end()) {
            return true;
        }

        // Check if it's an interface
        if (cache->interfaces.find(symbolName) != cache->interfaces.end()) {
            return true;
        }

        // Check AST for enums
        if (cache->ast) {
            for (const auto& enumDef : cache->ast->enums) {
                if (enumDef->name == symbolName) {
                    return true;
                }
                // Check enum cases
                for (const auto& enumCase : enumDef->cases) {
                    if (enumCase.name == symbolName) {
                        return true;
                    }
                }
            }
        }
    }

    // Unknown symbols can still be renamed if they appear to be identifiers
    // (allow renaming even if analysis failed)
    return true;
}

bool RenameProvider::isValidIdentifier(const std::string& name) {
    if (name.empty()) {
        return false;
    }

    // Must start with letter or underscore
    char first = name[0];
    if (!((first >= 'a' && first <= 'z') ||
          (first >= 'A' && first <= 'Z') ||
          first == '_')) {
        return false;
    }

    // Must contain only letters, digits, underscores
    for (char c : name) {
        if (!((c >= 'a' && c <= 'z') ||
              (c >= 'A' && c <= 'Z') ||
              (c >= '0' && c <= '9') ||
              c == '_')) {
            return false;
        }
    }

    // Must not be a keyword
    if (isKeywordOrBuiltin(name)) {
        return false;
    }

    return true;
}

bool RenameProvider::hasNamingConflict(
    const std::string& newName,
    const AnalysisCache* cache,
    const Document& document,
    const Position& position
) {
    (void)document;
    (void)position;

    if (!cache) {
        return false;
    }

    // Check if the new name already exists as a variable
    if (cache->variables.find(newName) != cache->variables.end()) {
        return true;
    }

    // Check if the new name already exists as a function
    if (cache->functions.find(newName) != cache->functions.end()) {
        return true;
    }

    // Check if the new name already exists as a struct
    if (cache->structs.find(newName) != cache->structs.end()) {
        return true;
    }

    // Check if the new name already exists as an interface
    if (cache->interfaces.find(newName) != cache->interfaces.end()) {
        return true;
    }

    // Check for enum with this name
    if (cache->ast) {
        for (const auto& enumDef : cache->ast->enums) {
            if (enumDef->name == newName) {
                return true;
            }
        }
    }

    return false;
}

std::string RenameProvider::getSymbolAtPosition(const Document& document, const Position& position) {
    if (position.line < 0 || position.line >= document.getLineCount()) {
        return "";
    }

    std::string line = document.getLine(position.line);
    if (line.empty() || position.character < 0) {
        return "";
    }

    int pos = position.character;
    if (pos >= static_cast<int>(line.size())) {
        pos = static_cast<int>(line.size()) - 1;
        if (pos < 0) {
            return "";
        }
    }

    auto isIdentChar = [](char c) {
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9') ||
               c == '_';
    };

    // Find the start of the identifier
    int start = pos;
    while (start > 0 && isIdentChar(line[start - 1])) {
        --start;
    }

    // Find the end of the identifier
    int end = pos;
    while (end < static_cast<int>(line.size()) && isIdentChar(line[end])) {
        ++end;
    }

    if (start == end) {
        return "";
    }

    // Store the symbol range
    currentSymbolRange_ = Range(position.line, start, position.line, end);

    return line.substr(start, end - start);
}

Range RenameProvider::getSymbolRange(const Document& document, const Position& position) {
    (void)document;
    (void)position;
    return currentSymbolRange_;
}

bool RenameProvider::isKeywordOrBuiltin(const std::string& name) {
    // Check if it's a keyword using KeywordMatcher
    if (KeywordMatcher::is_keyword(name)) {
        return true;
    }

    return false;
}

bool RenameProvider::isStdlibSymbol(const std::string& name, const StdlibSymbols& stdlib) {
    // Check functions
    for (const auto& func : stdlib.functions) {
        if (func.name == name) {
            return true;
        }
    }

    // Check structs
    for (const auto& structSym : stdlib.structs) {
        if (structSym.name == name) {
            return true;
        }
    }

    // Check enums
    for (const auto& enumSym : stdlib.enums) {
        if (enumSym.name == name) {
            return true;
        }
    }

    // Check interfaces
    for (const auto& ifaceSym : stdlib.interfaces) {
        if (ifaceSym.name == name) {
            return true;
        }
    }

    return false;
}

std::optional<Location> RenameProvider::findFunctionEndLabel(
    const Document& document,
    const std::string& functionName,
    const AnalysisCache* cache
) {
    if (!cache || !cache->ast) {
        return std::nullopt;
    }

    // Find the function in the AST to get its line range
    for (const auto& func : cache->ast->functions) {
        if (func->name == functionName) {
            // Search from the function start line to find the end label
            // The end label format is: end 'functionName'
            int startLine = func->line;
            int endLine = func->endLine > 0 ? func->endLine : document.getLineCount();

            for (int lineIdx = startLine; lineIdx <= endLine && lineIdx <= document.getLineCount(); ++lineIdx) {
                std::string line = document.getLine(lineIdx - 1);  // Convert to 0-based

                // Look for: end 'functionName' or end "functionName"
                // Use regex to find the pattern
                std::string pattern = "end\\s+['\"]" + functionName + "['\"]";
                std::regex endLabelRegex(pattern);
                std::smatch match;

                if (std::regex_search(line, match, endLabelRegex)) {
                    // Find the position of the function name within the match
                    size_t matchPos = match.position();
                    size_t namePos = line.find(functionName, matchPos);
                    if (namePos != std::string::npos) {
                        Location loc;
                        loc.uri = document.uri;
                        loc.range = Range(
                            lineIdx - 1,  // 0-based line
                            static_cast<int>(namePos),
                            lineIdx - 1,
                            static_cast<int>(namePos + functionName.size())
                        );
                        return loc;
                    }
                }
            }
            break;
        }
    }

    return std::nullopt;
}

std::optional<Location> RenameProvider::findStructEndLabel(
    const Document& document,
    const std::string& structName,
    const AnalysisCache* cache
) {
    if (!cache || !cache->ast) {
        return std::nullopt;
    }

    // Find the struct in the AST
    for (const auto& structDef : cache->ast->structs) {
        if (structDef->name == structName) {
            int startLine = structDef->line;
            int endLine = structDef->endLine > 0 ? structDef->endLine : document.getLineCount();

            for (int lineIdx = startLine; lineIdx <= endLine && lineIdx <= document.getLineCount(); ++lineIdx) {
                std::string line = document.getLine(lineIdx - 1);

                std::string pattern = "end\\s+['\"]" + structName + "['\"]";
                std::regex endLabelRegex(pattern);
                std::smatch match;

                if (std::regex_search(line, match, endLabelRegex)) {
                    size_t matchPos = match.position();
                    size_t namePos = line.find(structName, matchPos);
                    if (namePos != std::string::npos) {
                        Location loc;
                        loc.uri = document.uri;
                        loc.range = Range(
                            lineIdx - 1,
                            static_cast<int>(namePos),
                            lineIdx - 1,
                            static_cast<int>(namePos + structName.size())
                        );
                        return loc;
                    }
                }
            }
            break;
        }
    }

    return std::nullopt;
}

std::optional<Location> RenameProvider::findEnumEndLabel(
    const Document& document,
    const std::string& enumName,
    const AnalysisCache* cache
) {
    if (!cache || !cache->ast) {
        return std::nullopt;
    }

    for (const auto& enumDef : cache->ast->enums) {
        if (enumDef->name == enumName) {
            int startLine = enumDef->line;
            int endLine = enumDef->endLine > 0 ? enumDef->endLine : document.getLineCount();

            for (int lineIdx = startLine; lineIdx <= endLine && lineIdx <= document.getLineCount(); ++lineIdx) {
                std::string line = document.getLine(lineIdx - 1);

                std::string pattern = "end\\s+['\"]" + enumName + "['\"]";
                std::regex endLabelRegex(pattern);
                std::smatch match;

                if (std::regex_search(line, match, endLabelRegex)) {
                    size_t matchPos = match.position();
                    size_t namePos = line.find(enumName, matchPos);
                    if (namePos != std::string::npos) {
                        Location loc;
                        loc.uri = document.uri;
                        loc.range = Range(
                            lineIdx - 1,
                            static_cast<int>(namePos),
                            lineIdx - 1,
                            static_cast<int>(namePos + enumName.size())
                        );
                        return loc;
                    }
                }
            }
            break;
        }
    }

    return std::nullopt;
}

std::optional<Location> RenameProvider::findInterfaceEndLabel(
    const Document& document,
    const std::string& interfaceName,
    const AnalysisCache* cache
) {
    if (!cache || !cache->ast) {
        return std::nullopt;
    }

    for (const auto& iface : cache->ast->interfaces) {
        if (iface->name == interfaceName) {
            int startLine = iface->line;
            int endLine = iface->endLine > 0 ? iface->endLine : document.getLineCount();

            for (int lineIdx = startLine; lineIdx <= endLine && lineIdx <= document.getLineCount(); ++lineIdx) {
                std::string line = document.getLine(lineIdx - 1);

                std::string pattern = "end\\s+['\"]" + interfaceName + "['\"]";
                std::regex endLabelRegex(pattern);
                std::smatch match;

                if (std::regex_search(line, match, endLabelRegex)) {
                    size_t matchPos = match.position();
                    size_t namePos = line.find(interfaceName, matchPos);
                    if (namePos != std::string::npos) {
                        Location loc;
                        loc.uri = document.uri;
                        loc.range = Range(
                            lineIdx - 1,
                            static_cast<int>(namePos),
                            lineIdx - 1,
                            static_cast<int>(namePos + interfaceName.size())
                        );
                        return loc;
                    }
                }
            }
            break;
        }
    }

    return std::nullopt;
}

WorkspaceEdit RenameProvider::buildWorkspaceEdit(
    const std::vector<Location>& locations,
    const std::string& newName
) {
    WorkspaceEdit edit;

    // Group edits by document URI
    std::map<std::string, std::vector<TextEdit>> changes;

    for (const auto& loc : locations) {
        TextEdit textEdit;
        textEdit.range = loc.range;
        textEdit.newText = newName;

        changes[loc.uri].push_back(textEdit);
    }

    // Sort edits within each document in reverse order (bottom to top, right to left)
    // This ensures that edits don't invalidate positions of subsequent edits
    for (auto& [uri, edits] : changes) {
        std::sort(edits.begin(), edits.end(), [](const TextEdit& a, const TextEdit& b) {
            if (a.range.start.line != b.range.start.line) {
                return a.range.start.line > b.range.start.line;
            }
            return a.range.start.character > b.range.start.character;
        });
    }

    edit.changes = std::move(changes);
    return edit;
}

std::string RenameProvider::getSymbolKind(const std::string& symbolName, const AnalysisCache* cache) {
    if (!cache) {
        return "unknown";
    }

    // Check if it's a function
    if (cache->functions.find(symbolName) != cache->functions.end()) {
        return "function";
    }

    // Check if it's a struct
    if (cache->structs.find(symbolName) != cache->structs.end()) {
        return "struct";
    }

    // Check if it's an interface
    if (cache->interfaces.find(symbolName) != cache->interfaces.end()) {
        return "interface";
    }

    // Check for enum in AST
    if (cache->ast) {
        for (const auto& enumDef : cache->ast->enums) {
            if (enumDef->name == symbolName) {
                return "enum";
            }
        }
    }

    // Check if it's a variable
    if (cache->variables.find(symbolName) != cache->variables.end()) {
        return "variable";
    }

    return "unknown";
}

} // namespace maxon_lsp
