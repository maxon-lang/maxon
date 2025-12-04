#include "document_symbols.h"
#include <sstream>

namespace maxon_lsp {

std::vector<DocumentSymbol> DocumentSymbolsProvider::getDocumentSymbols(
    const Document& document,
    const AnalysisCache* cache
) {
    std::vector<DocumentSymbol> symbols;

    if (!cache || !cache->ast) {
        return symbols;
    }

    const ProgramAST* ast = cache->ast.get();

    // Add functions (excluding methods, which are nested in structs)
    for (const auto& func : ast->functions) {
        if (func && !func->isMethod()) {
            symbols.push_back(buildFunctionSymbol(func.get()));
        }
    }

    // Add structs with fields and methods as children
    for (const auto& structDef : ast->structs) {
        if (structDef) {
            symbols.push_back(buildStructSymbol(structDef.get()));
        }
    }

    // Add enums with cases as children
    for (const auto& enumDef : ast->enums) {
        if (enumDef) {
            symbols.push_back(buildEnumSymbol(enumDef.get()));
        }
    }

    // Add interfaces with method signatures as children
    for (const auto& iface : ast->interfaces) {
        if (iface) {
            symbols.push_back(buildInterfaceSymbol(iface.get()));
        }
    }

    return symbols;
}

std::vector<SymbolInformation> DocumentSymbolsProvider::getSymbolInformation(
    const Document& document,
    const AnalysisCache* cache
) {
    std::vector<SymbolInformation> symbols;

    if (!cache || !cache->ast) {
        return symbols;
    }

    currentUri_ = document.uri;
    const ProgramAST* ast = cache->ast.get();

    // Add functions (excluding methods)
    for (const auto& func : ast->functions) {
        if (func && !func->isMethod()) {
            SymbolInformation info;
            info.name = func->name;
            info.kind = SymbolKind::Function;
            info.location.uri = currentUri_;
            info.location.range = buildRange(func->line, func->column,
                                             func->endLine > 0 ? func->endLine : func->line,
                                             func->endColumn > 0 ? func->endColumn : func->column + static_cast<int>(func->name.length()));
            symbols.push_back(info);
        }
    }

    // Add structs and their members
    for (const auto& structDef : ast->structs) {
        if (structDef) {
            // Add struct itself
            SymbolInformation structInfo;
            structInfo.name = structDef->name;
            structInfo.kind = SymbolKind::Struct;
            structInfo.location.uri = currentUri_;
            structInfo.location.range = buildRange(structDef->line, structDef->column,
                                                   structDef->endLine > 0 ? structDef->endLine : structDef->line,
                                                   structDef->endColumn > 0 ? structDef->endColumn : structDef->column + static_cast<int>(structDef->name.length()));
            symbols.push_back(structInfo);

            // Add fields
            for (const auto& field : structDef->fields) {
                SymbolInformation fieldInfo;
                fieldInfo.name = field.name;
                fieldInfo.kind = SymbolKind::Field;
                fieldInfo.location.uri = currentUri_;
                fieldInfo.location.range = buildSelectionRange(field.line, field.column, static_cast<int>(field.name.length()));
                fieldInfo.containerName = structDef->name;
                symbols.push_back(fieldInfo);
            }

            // Add methods
            for (const auto& method : structDef->methods) {
                if (method) {
                    SymbolInformation methodInfo;
                    methodInfo.name = method->name;
                    methodInfo.kind = SymbolKind::Method;
                    methodInfo.location.uri = currentUri_;
                    methodInfo.location.range = buildRange(method->line, method->column,
                                                          method->endLine > 0 ? method->endLine : method->line,
                                                          method->endColumn > 0 ? method->endColumn : method->column + static_cast<int>(method->name.length()));
                    methodInfo.containerName = structDef->name;
                    symbols.push_back(methodInfo);
                }
            }
        }
    }

    // Add enums and their cases
    for (const auto& enumDef : ast->enums) {
        if (enumDef) {
            // Add enum itself
            SymbolInformation enumInfo;
            enumInfo.name = enumDef->name;
            enumInfo.kind = SymbolKind::Enum;
            enumInfo.location.uri = currentUri_;
            enumInfo.location.range = buildRange(enumDef->line, enumDef->column,
                                                 enumDef->endLine > 0 ? enumDef->endLine : enumDef->line,
                                                 enumDef->endColumn > 0 ? enumDef->endColumn : enumDef->column + static_cast<int>(enumDef->name.length()));
            symbols.push_back(enumInfo);

            // Add cases
            for (const auto& enumCase : enumDef->cases) {
                SymbolInformation caseInfo;
                caseInfo.name = enumCase.name;
                caseInfo.kind = SymbolKind::EnumMember;
                caseInfo.location.uri = currentUri_;
                caseInfo.location.range = buildSelectionRange(enumCase.line, enumCase.column, static_cast<int>(enumCase.name.length()));
                caseInfo.containerName = enumDef->name;
                symbols.push_back(caseInfo);
            }

            // Add enum methods
            for (const auto& method : enumDef->methods) {
                if (method) {
                    SymbolInformation methodInfo;
                    methodInfo.name = method->name;
                    methodInfo.kind = SymbolKind::Method;
                    methodInfo.location.uri = currentUri_;
                    methodInfo.location.range = buildRange(method->line, method->column,
                                                          method->endLine > 0 ? method->endLine : method->line,
                                                          method->endColumn > 0 ? method->endColumn : method->column + static_cast<int>(method->name.length()));
                    methodInfo.containerName = enumDef->name;
                    symbols.push_back(methodInfo);
                }
            }
        }
    }

    // Add interfaces and their methods
    for (const auto& iface : ast->interfaces) {
        if (iface) {
            // Add interface itself
            SymbolInformation ifaceInfo;
            ifaceInfo.name = iface->name;
            ifaceInfo.kind = SymbolKind::Interface;
            ifaceInfo.location.uri = currentUri_;
            ifaceInfo.location.range = buildRange(iface->line, iface->column,
                                                  iface->endLine > 0 ? iface->endLine : iface->line,
                                                  iface->endColumn > 0 ? iface->endColumn : iface->column + static_cast<int>(iface->name.length()));
            symbols.push_back(ifaceInfo);

            // Add method signatures
            for (const auto& method : iface->methods) {
                SymbolInformation methodInfo;
                methodInfo.name = method.name;
                methodInfo.kind = SymbolKind::Method;
                methodInfo.location.uri = currentUri_;
                methodInfo.location.range = buildSelectionRange(method.line, method.column, static_cast<int>(method.name.length()));
                methodInfo.containerName = iface->name;
                symbols.push_back(methodInfo);
            }
        }
    }

    return symbols;
}

DocumentSymbol DocumentSymbolsProvider::buildFunctionSymbol(const FunctionAST* func) {
    DocumentSymbol symbol;
    symbol.name = func->name;
    symbol.kind = SymbolKind::Function;
    symbol.detail = buildFunctionDetail(func);

    // Full range from function keyword to end
    symbol.range = buildRange(
        func->line, func->column,
        func->endLine > 0 ? func->endLine : func->line,
        func->endColumn > 0 ? func->endColumn : func->column + static_cast<int>(func->name.length())
    );

    // Selection range is just the function name
    // The name typically starts after "function " (9 chars), but we use the line/column from AST
    // which points to the "function" keyword. We add an offset for the name position.
    symbol.selectionRange = buildSelectionRange(func->line, func->column, static_cast<int>(func->name.length()));

    // Note: We don't include parameters as children for brevity in the outline view

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildStructSymbol(const StructDefAST* structDef) {
    DocumentSymbol symbol;
    symbol.name = structDef->name;
    symbol.kind = SymbolKind::Struct;

    // Build detail showing interface conformance
    if (!structDef->conformsTo.empty()) {
        std::ostringstream detail;
        detail << "is ";
        for (size_t i = 0; i < structDef->conformsTo.size(); ++i) {
            if (i > 0) detail << ", ";
            detail << structDef->conformsTo[i];
        }
        symbol.detail = detail.str();
    }

    // Full range from struct keyword to end
    symbol.range = buildRange(
        structDef->line, structDef->column,
        structDef->endLine > 0 ? structDef->endLine : structDef->line,
        structDef->endColumn > 0 ? structDef->endColumn : structDef->column + static_cast<int>(structDef->name.length())
    );

    // Selection range is just the struct name
    symbol.selectionRange = buildSelectionRange(structDef->line, structDef->column, static_cast<int>(structDef->name.length()));

    // Add fields as children
    std::vector<DocumentSymbol> children;
    for (const auto& field : structDef->fields) {
        children.push_back(buildFieldSymbol(field.name, field.type, field.line, field.column));
    }

    // Add methods as children
    for (const auto& method : structDef->methods) {
        if (method) {
            children.push_back(buildMethodSymbol(method.get()));
        }
    }

    if (!children.empty()) {
        symbol.children = std::move(children);
    }

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildEnumSymbol(const EnumDefAST* enumDef) {
    DocumentSymbol symbol;
    symbol.name = enumDef->name;
    symbol.kind = SymbolKind::Enum;

    // Build detail showing raw value type if present
    if (!enumDef->rawValueType.empty()) {
        symbol.detail = "raw value: " + enumDef->rawValueType;
    }

    // Full range from enum keyword to end
    symbol.range = buildRange(
        enumDef->line, enumDef->column,
        enumDef->endLine > 0 ? enumDef->endLine : enumDef->line,
        enumDef->endColumn > 0 ? enumDef->endColumn : enumDef->column + static_cast<int>(enumDef->name.length())
    );

    // Selection range is just the enum name
    symbol.selectionRange = buildSelectionRange(enumDef->line, enumDef->column, static_cast<int>(enumDef->name.length()));

    // Add cases as children
    std::vector<DocumentSymbol> children;
    for (const auto& enumCase : enumDef->cases) {
        children.push_back(buildEnumCaseSymbol(enumCase.name, enumCase.line, enumCase.column));
    }

    // Add methods as children
    for (const auto& method : enumDef->methods) {
        if (method) {
            children.push_back(buildMethodSymbol(method.get()));
        }
    }

    if (!children.empty()) {
        symbol.children = std::move(children);
    }

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildInterfaceSymbol(const InterfaceDefAST* iface) {
    DocumentSymbol symbol;
    symbol.name = iface->name;
    symbol.kind = SymbolKind::Interface;

    // Full range from interface keyword to end
    symbol.range = buildRange(
        iface->line, iface->column,
        iface->endLine > 0 ? iface->endLine : iface->line,
        iface->endColumn > 0 ? iface->endColumn : iface->column + static_cast<int>(iface->name.length())
    );

    // Selection range is just the interface name
    symbol.selectionRange = buildSelectionRange(iface->line, iface->column, static_cast<int>(iface->name.length()));

    // Add method signatures as children
    std::vector<DocumentSymbol> children;
    for (const auto& method : iface->methods) {
        children.push_back(buildInterfaceMethodSymbol(method));
    }

    if (!children.empty()) {
        symbol.children = std::move(children);
    }

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildFieldSymbol(
    const std::string& name,
    const std::string& type,
    int line,
    int col
) {
    DocumentSymbol symbol;
    symbol.name = name;
    symbol.kind = SymbolKind::Field;
    symbol.detail = type;

    // For fields, the range is just the field declaration line
    symbol.range = buildSelectionRange(line, col, static_cast<int>(name.length()));
    symbol.selectionRange = symbol.range;

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildMethodSymbol(const FunctionAST* method) {
    DocumentSymbol symbol;
    symbol.name = method->name;
    symbol.kind = SymbolKind::Method;
    symbol.detail = buildFunctionDetail(method);

    // Full range from function keyword to end
    symbol.range = buildRange(
        method->line, method->column,
        method->endLine > 0 ? method->endLine : method->line,
        method->endColumn > 0 ? method->endColumn : method->column + static_cast<int>(method->name.length())
    );

    // Selection range is just the method name
    symbol.selectionRange = buildSelectionRange(method->line, method->column, static_cast<int>(method->name.length()));

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildEnumCaseSymbol(
    const std::string& name,
    int line,
    int col
) {
    DocumentSymbol symbol;
    symbol.name = name;
    symbol.kind = SymbolKind::EnumMember;

    // For enum cases, the range is just the case name
    symbol.range = buildSelectionRange(line, col, static_cast<int>(name.length()));
    symbol.selectionRange = symbol.range;

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildParameterSymbol(
    const std::string& name,
    const std::string& type,
    int line,
    int col
) {
    DocumentSymbol symbol;
    symbol.name = name;
    symbol.kind = SymbolKind::Variable;  // Parameters are shown as variables
    symbol.detail = type;

    symbol.range = buildSelectionRange(line, col, static_cast<int>(name.length()));
    symbol.selectionRange = symbol.range;

    return symbol;
}

DocumentSymbol DocumentSymbolsProvider::buildInterfaceMethodSymbol(const InterfaceMethodSignature& method) {
    DocumentSymbol symbol;
    symbol.name = method.name;
    symbol.kind = SymbolKind::Method;
    symbol.detail = buildInterfaceMethodDetail(method);

    symbol.range = buildSelectionRange(method.line, method.column, static_cast<int>(method.name.length()));
    symbol.selectionRange = symbol.range;

    return symbol;
}

SymbolKind DocumentSymbolsProvider::mapSymbolKind(const std::string& kind) {
    if (kind == "function") return SymbolKind::Function;
    if (kind == "struct") return SymbolKind::Struct;
    if (kind == "enum") return SymbolKind::Enum;
    if (kind == "interface") return SymbolKind::Interface;
    if (kind == "field") return SymbolKind::Field;
    if (kind == "method") return SymbolKind::Method;
    if (kind == "variable") return SymbolKind::Variable;
    if (kind == "parameter") return SymbolKind::Variable;  // Parameters shown as variables
    if (kind == "enumMember") return SymbolKind::EnumMember;
    if (kind == "constant") return SymbolKind::Constant;
    return SymbolKind::Variable;  // Default
}

Range DocumentSymbolsProvider::buildRange(int startLine, int startCol, int endLine, int endCol) {
    // Convert 1-based compiler positions to 0-based LSP positions
    // Handle edge cases where positions might be 0 (unset)
    int sl = startLine > 0 ? startLine - 1 : 0;
    int sc = startCol > 0 ? startCol - 1 : 0;
    int el = endLine > 0 ? endLine - 1 : sl;
    int ec = endCol > 0 ? endCol - 1 : sc;

    return Range(Position(sl, sc), Position(el, ec));
}

Range DocumentSymbolsProvider::buildSelectionRange(int line, int col, int nameLength) {
    // Convert 1-based compiler position to 0-based LSP position
    int l = line > 0 ? line - 1 : 0;
    int c = col > 0 ? col - 1 : 0;

    return Range(Position(l, c), Position(l, c + nameLength));
}

std::string DocumentSymbolsProvider::buildFunctionDetail(const FunctionAST* func) {
    std::ostringstream detail;

    // Build parameter list
    detail << "(";
    for (size_t i = 0; i < func->parameters.size(); ++i) {
        if (i > 0) detail << ", ";
        const auto& param = func->parameters[i];
        detail << param.name << " " << param.type;
    }
    detail << ")";

    // Add return type if not void
    if (!func->returnType.empty() && func->returnType != "void") {
        detail << " " << func->returnType;
    }

    return detail.str();
}

std::string DocumentSymbolsProvider::buildInterfaceMethodDetail(const InterfaceMethodSignature& method) {
    std::ostringstream detail;

    // Build parameter list (skip self parameter)
    detail << "(";
    bool first = true;
    for (const auto& param : method.parameters) {
        if (param.name == "self") continue;  // Skip self parameter
        if (!first) detail << ", ";
        first = false;
        detail << param.name << " " << param.type;
    }
    detail << ")";

    // Add return type if not void
    if (!method.returnType.empty() && method.returnType != "void") {
        detail << " " << method.returnType;
    }

    return detail.str();
}

} // namespace maxon_lsp
