#include "hover.h"
#include "../../lexer/lexer_keyword_matcher.h"

namespace maxon_lsp {

std::optional<Hover> HoverProvider::getHover(
    const Document& document,
    const Position& position,
    const AnalysisCache* cache,
    const StdlibSymbols& stdlib
) {
    // Get the token at the cursor position
    std::string token = getTokenAtPosition(document, position);
    if (token.empty()) {
        return std::nullopt;
    }

    // Get the range for highlighting (stored in member variable)
    (void)getTokenRange(document, position);

    // Check for dot-separated field access (e.g., "point.x")
    size_t dotPos = token.find('.');
    if (dotPos != std::string::npos) {
        std::string prefix = token.substr(0, dotPos);
        std::string suffix = token.substr(dotPos + 1);

        // Try to look up as struct.field
        auto hover = lookupField(prefix, suffix, cache, stdlib);
        if (hover) {
            return hover;
        }

        // If prefix is a variable, get its type and look up field on that type
        if (cache) {
            auto varIt = cache->variables.find(prefix);
            if (varIt != cache->variables.end()) {
                auto fieldHover = lookupField(varIt->second.type, suffix, cache, stdlib);
                if (fieldHover) {
                    return fieldHover;
                }
            }
        }
    }

    // Try lookups in order: keyword, variable, function, type

    // 1. Try keyword lookup
    auto keywordHover = lookupKeyword(token);
    if (keywordHover) {
        return keywordHover;
    }

    // 2. Try variable lookup
    auto variableHover = lookupVariable(token, cache, position);
    if (variableHover) {
        return variableHover;
    }

    // 3. Try function lookup
    auto functionHover = lookupFunction(token, cache, stdlib);
    if (functionHover) {
        return functionHover;
    }

    // 4. Try type lookup
    auto typeHover = lookupType(token, cache, stdlib);
    if (typeHover) {
        return typeHover;
    }

    return std::nullopt;
}

std::string HoverProvider::getTokenAtPosition(const Document& document, const Position& position) {
    // Get the line at the position
    if (position.line < 0 || position.line >= document.getLineCount()) {
        return "";
    }

    std::string line = document.getLine(position.line);
    if (line.empty() || position.character < 0) {
        return "";
    }

    // Clamp position to line bounds
    int pos = position.character;
    if (pos >= static_cast<int>(line.size())) {
        pos = static_cast<int>(line.size()) - 1;
        if (pos < 0) {
            return "";
        }
    }

    // Helper to check if a character is part of an identifier
    auto isIdentChar = [](char c) {
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9') ||
               c == '_';
    };

    // Find the start of the token
    int start = pos;
    while (start > 0 && isIdentChar(line[start - 1])) {
        start--;
    }

    // Find the end of the token
    int end = pos;
    while (end < static_cast<int>(line.size()) && isIdentChar(line[end])) {
        end++;
    }

    // Check if we're on a valid identifier
    if (start == end) {
        return "";
    }

    std::string token = line.substr(start, end - start);

    // Check if there's a dot following the token (field access)
    if (end < static_cast<int>(line.size()) && line[end] == '.') {
        // Get the field name after the dot
        int fieldStart = end + 1;
        int fieldEnd = fieldStart;
        while (fieldEnd < static_cast<int>(line.size()) && isIdentChar(line[fieldEnd])) {
            fieldEnd++;
        }
        if (fieldEnd > fieldStart) {
            token += "." + line.substr(fieldStart, fieldEnd - fieldStart);
        }
    }

    // Check if cursor is on the field part (after a dot)
    if (start > 0 && line[start - 1] == '.') {
        // Find the struct/variable name before the dot
        int prefixEnd = start - 1;
        int prefixStart = prefixEnd;
        while (prefixStart > 0 && isIdentChar(line[prefixStart - 1])) {
            prefixStart--;
        }
        if (prefixStart < prefixEnd) {
            token = line.substr(prefixStart, prefixEnd - prefixStart) + "." + token;
            start = prefixStart;
        }
    }

    // Store the token range for later use
    // Handle dot notation: find the actual full token range
    size_t dotPos = token.find('.');
    if (dotPos != std::string::npos) {
        // For field access, highlight just the field name
        currentTokenRange_ = Range(position.line, start + static_cast<int>(dotPos) + 1,
                                   position.line, start + static_cast<int>(token.size()));
    } else {
        currentTokenRange_ = Range(position.line, start, position.line, end);
    }

    return token;
}

Range HoverProvider::getTokenRange(const Document& document, const Position& position) {
    // This is called after getTokenAtPosition, which sets currentTokenRange_
    return currentTokenRange_;
}

std::optional<Hover> HoverProvider::lookupKeyword(const std::string& token) {
    // Initialize keyword matcher if needed
    KeywordMatcher::initialize();

    // Try to match as keyword
    KeywordEntry entry;
    if (KeywordMatcher::match(token.c_str(), token.size(), entry)) {
        KeywordLSPInfo keywordInfo;
        keywordInfo.name = entry.keyword;
        keywordInfo.documentation = entry.description;
        keywordInfo.insertText = entry.insertText;
        keywordInfo.completionKind = entry.completionKind;
        keywordInfo.category = entry.category;
        if (entry.has_math_info) {
            keywordInfo.returnType = entry.math_info.returnType;
        }

        std::string markdown = formatKeywordHover(keywordInfo);
        return buildHover(markdown, currentTokenRange_);
    }

    return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupVariable(const std::string& name, const AnalysisCache* cache, const Position& position) {
    if (!cache) {
        return std::nullopt;
    }

    // Search in the variables map
    auto it = cache->variables.find(name);
    if (it != cache->variables.end()) {
        const VariableInfo& var = it->second;
        std::string markdown = formatVariableHover(var.name, var.type, !var.isImmutable);
        return buildHover(markdown, currentTokenRange_);
    }

    return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupFunction(const std::string& name, const AnalysisCache* cache, const StdlibSymbols& stdlib) {
    // First check in cache
    if (cache) {
        auto it = cache->functions.find(name);
        if (it != cache->functions.end()) {
            std::string markdown = formatFunctionHover(it->second);
            return buildHover(markdown, currentTokenRange_);
        }
    }

    // Check in stdlib functions
    for (const auto& func : stdlib.functions) {
        if (func.name == name) {
            std::string markdown = formatFunctionHover(func);
            return buildHover(markdown, currentTokenRange_);
        }
    }

    return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupType(const std::string& name, const AnalysisCache* cache, const StdlibSymbols& stdlib) {
    // Check built-in types first
    if (name == "int" || name == "float" || name == "bool" ||
        name == "byte" || name == "char" || name == "string") {
        std::string markdown = formatBuiltinTypeHover(name);
        return buildHover(markdown, currentTokenRange_);
    }

    // Check structs in cache
    if (cache) {
        auto structIt = cache->structs.find(name);
        if (structIt != cache->structs.end()) {
            std::string markdown = formatStructHover(structIt->second);
            return buildHover(markdown, currentTokenRange_);
        }

        // Check interfaces in cache
        auto ifaceIt = cache->interfaces.find(name);
        if (ifaceIt != cache->interfaces.end()) {
            std::string markdown = formatInterfaceHover(ifaceIt->second);
            return buildHover(markdown, currentTokenRange_);
        }
    }

    // Check stdlib structs
    for (const auto& structSym : stdlib.structs) {
        if (structSym.name == name) {
            // Build a simple struct info for formatting
            std::string markdown = "```maxon\nstruct " + name + "\n```\n";
            if (!structSym.documentation.empty()) {
                markdown += "\n" + structSym.documentation + "\n";
            }
            return buildHover(markdown, currentTokenRange_);
        }
    }

    // Check stdlib enums
    for (const auto& enumSym : stdlib.enums) {
        if (enumSym.name == name) {
            std::string markdown = "```maxon\nenum " + name + "\n```\n";
            if (!enumSym.documentation.empty()) {
                markdown += "\n" + enumSym.documentation + "\n";
            }
            return buildHover(markdown, currentTokenRange_);
        }
    }

    // Check stdlib interfaces
    for (const auto& ifaceSym : stdlib.interfaces) {
        if (ifaceSym.name == name) {
            std::string markdown = "```maxon\ninterface " + name + "\n```\n";
            if (!ifaceSym.documentation.empty()) {
                markdown += "\n" + ifaceSym.documentation + "\n";
            }
            return buildHover(markdown, currentTokenRange_);
        }
    }

    return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupField(const std::string& structName, const std::string& fieldName,
                                                 const AnalysisCache* cache, const StdlibSymbols& stdlib) {
    // Check structs in cache
    if (cache) {
        auto structIt = cache->structs.find(structName);
        if (structIt != cache->structs.end()) {
            for (const auto& field : structIt->second.fields) {
                if (field.name == fieldName) {
                    std::string markdown = formatFieldHover(structName, fieldName, field.type);
                    return buildHover(markdown, currentTokenRange_);
                }
            }
        }
    }

    // Check stdlib structs
    for (const auto& structSym : stdlib.structs) {
        if (structSym.name == structName) {
            // Look through the type signature for field info
            // The struct signature format is "struct Name { field1 type1, field2 type2 }"
            // For now, just indicate it's a field of that struct
            std::string markdown = "```maxon\n(field) " + structName + "." + fieldName + "\n```\n";
            return buildHover(markdown, currentTokenRange_);
        }
    }

    return std::nullopt;
}

std::string HoverProvider::formatKeywordHover(const KeywordLSPInfo& keyword) {
    std::string md = "```maxon\n";
    md += keyword.name;

    // For math intrinsics, show the return type
    if (!keyword.returnType.empty()) {
        md += "(...) " + keyword.returnType;
    }

    md += "\n```\n";

    if (!keyword.documentation.empty()) {
        md += "\n" + keyword.documentation + "\n";
    }

    return md;
}

std::string HoverProvider::formatVariableHover(const std::string& name, const std::string& type,
                                               bool isMutable, const std::string& doc) {
    std::string md = "```maxon\n";
    md += isMutable ? "var " : "let ";
    md += name + " " + type;
    md += "\n```\n";

    if (!doc.empty()) {
        md += "\n" + doc + "\n";
    }

    return md;
}

std::string HoverProvider::formatFunctionHover(const FunctionInfo& func, const std::string& doc) {
    std::string md = "```maxon\n";
    md += "function " + func.name + "(";

    // Add parameters
    bool first = true;
    for (const auto& param : func.parameters) {
        if (!first) {
            md += ", ";
        }
        first = false;
        md += param.name + " " + param.type;
    }

    md += ") " + func.returnType;
    md += "\n```\n";

    if (!doc.empty()) {
        md += "\n" + doc + "\n";
    }

    return md;
}

std::string HoverProvider::formatFunctionHover(const LSPSymbolInfo& symbol) {
    std::string md = "```maxon\n";
    md += "function " + symbol.name + "(";

    // Add parameters from LSPSymbolInfo
    bool first = true;
    for (const auto& param : symbol.parameters) {
        if (!first) {
            md += ", ";
        }
        first = false;
        md += param.name + " " + param.type;
    }

    md += ")";
    if (!symbol.returnType.empty()) {
        md += " " + symbol.returnType;
    }
    md += "\n```\n";

    if (!symbol.documentation.empty()) {
        md += "\n" + symbol.documentation + "\n";
    }

    return md;
}

std::string HoverProvider::formatStructHover(const StructInfo& structInfo, const std::string& doc) {
    std::string md = "```maxon\n";
    md += "struct " + structInfo.name;

    // Show interface conformance if any
    if (!structInfo.conformsTo.empty()) {
        md += " is ";
        bool first = true;
        for (const auto& iface : structInfo.conformsTo) {
            if (!first) {
                md += ", ";
            }
            first = false;
            md += iface;
        }
    }

    md += "\n";

    // Show fields
    for (const auto& field : structInfo.fields) {
        md += "    ";
        md += field.isImmutable ? "let " : "var ";
        md += field.name + " " + field.type;
        if (field.hasDefault) {
            md += " = " + field.defaultValue;
        }
        md += "\n";
    }

    md += "end '" + structInfo.name + "'\n";
    md += "```\n";

    if (!doc.empty()) {
        md += "\n" + doc + "\n";
    }

    return md;
}

std::string HoverProvider::formatEnumHover(const EnumInfo& enumInfo, const std::string& doc) {
    std::string md = "```maxon\n";
    md += "enum " + enumInfo.name;

    // Show raw value type if present
    if (!enumInfo.rawValueType.empty()) {
        md += " " + enumInfo.rawValueType;
    }

    md += "\n";

    // Show cases
    for (const auto& enumCase : enumInfo.cases) {
        md += "    case " + enumCase.name;

        // Show associated values if any
        if (!enumCase.associatedValues.empty()) {
            md += "(";
            bool first = true;
            for (const auto& assoc : enumCase.associatedValues) {
                if (!first) {
                    md += ", ";
                }
                first = false;
                md += assoc.name + " " + assoc.type;
            }
            md += ")";
        }

        // Show raw value if present
        if (enumCase.hasRawValue) {
            if (enumInfo.rawValueType == "int") {
                md += " = " + std::to_string(enumCase.rawIntValue);
            } else if (enumInfo.rawValueType == "string") {
                md += " = \"" + enumCase.rawStringValue + "\"";
            }
        }

        md += "\n";
    }

    md += "end '" + enumInfo.name + "'\n";
    md += "```\n";

    if (!doc.empty()) {
        md += "\n" + doc + "\n";
    }

    return md;
}

std::string HoverProvider::formatInterfaceHover(const InterfaceInfo& iface, const std::string& doc) {
    std::string md = "```maxon\n";
    md += "interface " + iface.name + "\n";

    // Show associated types
    for (const auto& assocType : iface.associatedTypes) {
        md += "    type " + assocType + "\n";
    }

    // Show methods
    for (const auto& method : iface.methods) {
        md += "    function " + method.name + "(";

        bool first = true;
        for (const auto& param : method.parameters) {
            if (!first) {
                md += ", ";
            }
            first = false;
            md += param.name + " " + param.type;
        }

        md += ") " + method.returnType + "\n";
    }

    md += "end '" + iface.name + "'\n";
    md += "```\n";

    if (!doc.empty()) {
        md += "\n" + doc + "\n";
    }

    return md;
}

std::string HoverProvider::formatFieldHover(const std::string& structName, const std::string& fieldName,
                                            const std::string& type) {
    std::string md = "```maxon\n";
    md += "(field) " + structName + "." + fieldName + " " + type;
    md += "\n```\n";
    return md;
}

std::string HoverProvider::formatBuiltinTypeHover(const std::string& typeName) {
    std::string md = "```maxon\n";
    md += typeName;
    md += "\n```\n\n";

    // Add documentation for built-in types
    if (typeName == "int") {
        md += "64-bit signed integer\n";
    } else if (typeName == "float") {
        md += "64-bit floating-point number\n";
    } else if (typeName == "bool") {
        md += "Boolean value (true or false)\n";
    } else if (typeName == "byte") {
        md += "8-bit unsigned integer\n";
    } else if (typeName == "char") {
        md += "Unicode character\n";
    } else if (typeName == "string") {
        md += "UTF-8 string\n";
    }

    return md;
}

Hover HoverProvider::buildHover(const std::string& markdown, const Range& range) {
    Hover hover;

    // Create MarkupContent with markdown
    MarkupContent content;
    content.kind = MarkupKind::Markdown;
    content.value = markdown;

    hover.contents = content;
    hover.range = range;

    return hover;
}

} // namespace maxon_lsp
