#include "references.h"
#include <algorithm>

namespace maxon_lsp {

std::vector<Location> ReferencesProvider::findReferences(
	const Document &document,
	const Position &position,
	bool includeDeclaration,
	const AnalysisCache *cache,
	const std::string & /*workspaceRoot*/
) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	// Identify the symbol at the cursor position
	auto symbolInfoOpt = getSymbolInfo(document, position, cache);
	if (!symbolInfoOpt.has_value()) {
		return refs;
	}

	const SymbolInfo &symbol = symbolInfoOpt.value();

	// Find references based on symbol kind
	switch (symbol.kind) {
	case SymbolKind::Variable:
		refs = findVariableReferences(symbol.name, document, cache);
		break;

	case SymbolKind::Function:
		refs = findFunctionReferences(symbol.name, document, cache);
		break;

	case SymbolKind::Type:
		refs = findTypeReferences(symbol.name, document, cache);
		break;

	case SymbolKind::Field:
		refs = findFieldReferences(symbol.containingType, symbol.name, document, cache);
		break;

	case SymbolKind::Parameter:
		refs = findParameterReferences(symbol.name, symbol.containingFunction, document, cache);
		break;

	case SymbolKind::EnumCase:
		refs = findEnumCaseReferences(symbol.containingType, symbol.name, document, cache);
		break;

	case SymbolKind::Unknown:
		// Try a general search by name across all categories
		refs = findVariableReferences(symbol.name, document, cache);
		if (refs.empty()) {
			refs = findFunctionReferences(symbol.name, document, cache);
		}
		if (refs.empty()) {
			refs = findTypeReferences(symbol.name, document, cache);
		}
		break;
	}

	// Filter out the declaration if not requested
	if (!includeDeclaration && symbol.declarationRange.startLine > 0) {
		refs.erase(
			std::remove_if(refs.begin(), refs.end(),
						   [&symbol](const Location &loc) {
							   // Check if this location matches the declaration
							   // Convert LSP 0-based to 1-based for comparison
							   int locLine = loc.range.start.line + 1;
							   int locCol = loc.range.start.character + 1;
							   return locLine == symbol.declarationRange.startLine &&
									  locCol == symbol.declarationRange.startCol;
						   }),
			refs.end());
	}

	return refs;
}

std::optional<ReferencesProvider::SymbolInfo> ReferencesProvider::getSymbolInfo(
	const Document &document,
	const Position &position,
	const AnalysisCache *cache) {
	std::string identifier = getIdentifierAtPosition(document, position);
	if (identifier.empty()) {
		return std::nullopt;
	}

	SymbolInfo info;
	info.name = identifier;
	info.kind = SymbolKind::Unknown;

	// Check if this is a field access (identifier after a dot)
	std::string line = document.getLine(position.line);
	int charPos = position.character;

	// Look backwards for a dot to detect field access
	int dotPos = -1;
	for (int i = charPos - 1; i >= 0; --i) {
		char c = line[i];
		if (c == '.') {
			dotPos = i;
			break;
		}
		if (!std::isalnum(c) && c != '_') {
			break;
		}
	}

	if (dotPos >= 0) {
		// This might be a field access - find the object name before the dot
		int objEnd = dotPos;
		int objStart = objEnd - 1;
		while (objStart >= 0 && (std::isalnum(line[objStart]) || line[objStart] == '_')) {
			--objStart;
		}
		++objStart;

		if (objStart < objEnd) {
			std::string objectName = line.substr(objStart, objEnd - objStart);

			// Try to find the type of the object
			if (cache) {
				auto varIt = cache->variables.find(objectName);
				if (varIt != cache->variables.end()) {
					// It's a variable - get its type
					std::string varType = varIt->second.type;

					// Check if the type is a known struct
					auto structIt = cache->structs.find(varType);
					if (structIt != cache->structs.end()) {
						// Verify the field exists
						for (const auto &field : structIt->second.fields) {
							if (field.name == identifier) {
								info.kind = SymbolKind::Field;
								info.containingType = varType;
								info.declarationRange = SourceRange(
									field.line, field.column,
									field.line, field.column + static_cast<int>(field.name.size()));
								return info;
							}
						}
					}
				}

				// Check if the object is a type name (static field/method or enum case)
				auto structIt = cache->structs.find(objectName);
				if (structIt != cache->structs.end()) {
					for (const auto &field : structIt->second.fields) {
						if (field.name == identifier) {
							info.kind = SymbolKind::Field;
							info.containingType = objectName;
							info.declarationRange = SourceRange(
								field.line, field.column,
								field.line, field.column + static_cast<int>(field.name.size()));
							return info;
						}
					}
				}

				// Check if it's an enum case access
				if (cache->ast) {
					for (const auto &enumDef : cache->ast->enums) {
						if (enumDef->name == objectName) {
							for (const auto &enumCase : enumDef->cases) {
								if (enumCase.name == identifier) {
									info.kind = SymbolKind::EnumCase;
									info.containingType = objectName;
									info.declarationRange = SourceRange(
										enumCase.line, enumCase.column,
										enumCase.line, enumCase.column + static_cast<int>(enumCase.name.size()));
									return info;
								}
							}
						}
					}
				}
			}
		}
	}

	// Check if it's a variable
	if (cache) {
		auto varIt = cache->variables.find(identifier);
		if (varIt != cache->variables.end()) {
			const VariableInfo &var = varIt->second;
			info.kind = var.isParameter ? SymbolKind::Parameter : SymbolKind::Variable;
			info.declarationRange = SourceRange(
				var.line, var.column,
				var.line, var.column + static_cast<int>(var.name.size()));

			// Find the containing function for parameters
			if (var.isParameter && cache->ast) {
				// Find which function contains this position
				// Convert 0-based LSP position to 1-based
				int lineNum = position.line + 1;
				for (const auto &func : cache->ast->functions) {
					if (func->line <= lineNum &&
						(func->endLine == 0 || func->endLine >= lineNum)) {
						info.containingFunction = func->name;
						break;
					}
				}
			}
			return info;
		}
	}

	// Check if it's a function
	if (cache) {
		auto funcIt = cache->functions.find(identifier);
		if (funcIt != cache->functions.end()) {
			info.kind = SymbolKind::Function;
			info.declarationRange = SourceRange(
				funcIt->second.line, funcIt->second.column,
				funcIt->second.line, funcIt->second.column + static_cast<int>(identifier.size()));
			return info;
		}
	}

	// Check if it's a type (struct, enum, interface)
	if (cache) {
		auto structIt = cache->structs.find(identifier);
		if (structIt != cache->structs.end()) {
			info.kind = SymbolKind::Type;
			info.declarationRange = SourceRange(
				structIt->second.line, structIt->second.column,
				structIt->second.line, structIt->second.column + static_cast<int>(identifier.size()));
			return info;
		}

		auto ifaceIt = cache->interfaces.find(identifier);
		if (ifaceIt != cache->interfaces.end()) {
			info.kind = SymbolKind::Type;
			info.declarationRange = SourceRange(
				ifaceIt->second.line, ifaceIt->second.column,
				ifaceIt->second.line, ifaceIt->second.column + static_cast<int>(identifier.size()));
			return info;
		}

		// Check enums in AST
		if (cache->ast) {
			for (const auto &enumDef : cache->ast->enums) {
				if (enumDef->name == identifier) {
					info.kind = SymbolKind::Type;
					info.declarationRange = SourceRange(
						enumDef->line, enumDef->column,
						enumDef->line, enumDef->column + static_cast<int>(identifier.size()));
					return info;
				}
			}
		}
	}

	// Unknown symbol - return with Unknown kind so we can still try to find references
	return info;
}

std::string ReferencesProvider::getIdentifierAtPosition(const Document &document, const Position &position) {
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

	return line.substr(start, end - start);
}

std::vector<Location> ReferencesProvider::findVariableReferences(
	const std::string &name,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	collectReferencesInAST(
		cache->ast.get(),
		name,
		SymbolKind::Variable,
		"", // No containing type for variables
		refs,
		document.uri);

	return refs;
}

std::vector<Location> ReferencesProvider::findFunctionReferences(
	const std::string &name,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	collectReferencesInAST(
		cache->ast.get(),
		name,
		SymbolKind::Function,
		"",
		refs,
		document.uri);

	return refs;
}

std::vector<Location> ReferencesProvider::findTypeReferences(
	const std::string &name,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	collectReferencesInAST(
		cache->ast.get(),
		name,
		SymbolKind::Type,
		"",
		refs,
		document.uri);

	return refs;
}

std::vector<Location> ReferencesProvider::findFieldReferences(
	const std::string &structName,
	const std::string &fieldName,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	collectReferencesInAST(
		cache->ast.get(),
		fieldName,
		SymbolKind::Field,
		structName,
		refs,
		document.uri);

	return refs;
}

std::vector<Location> ReferencesProvider::findParameterReferences(
	const std::string &paramName,
	const std::string &functionName,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	// Find the specific function and search only within it
	for (const auto &func : cache->ast->functions) {
		if (func->name == functionName) {
			// Add the parameter declaration itself
			for (const auto &param : func->parameters) {
				if (param.name == paramName) {
					refs.push_back(buildLocation(
						document.uri,
						param.line, param.column,
						param.line, param.column + static_cast<int>(param.name.size())));
					break;
				}
			}

			// Search the function body for references
			for (const auto &stmt : func->body) {
				visitStatement(stmt.get(), paramName, SymbolKind::Variable, "", refs, document.uri);
			}
			break;
		}
	}

	return refs;
}

std::vector<Location> ReferencesProvider::findEnumCaseReferences(
	const std::string &enumName,
	const std::string &caseName,
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<Location> refs;

	if (!cache || !cache->ast) {
		return refs;
	}

	collectReferencesInAST(
		cache->ast.get(),
		caseName,
		SymbolKind::EnumCase,
		enumName,
		refs,
		document.uri);

	return refs;
}

void ReferencesProvider::collectReferencesInAST(
	const ProgramAST *ast,
	const std::string &targetName,
	SymbolKind targetKind,
	const std::string &containingType,
	std::vector<Location> &refs,
	const std::string &uri) {
	// Process function definitions
	for (const auto &func : ast->functions) {
		// Check if function name matches (for Function kind)
		if (targetKind == SymbolKind::Function && func->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				func->line, func->column,
				func->line, func->column + static_cast<int>(targetName.size())));
		}

		// Check return type for type references
		if (targetKind == SymbolKind::Type && func->returnType == targetName) {
			// The return type appears after the parameters in the function signature
			// We'll approximate the location
			refs.push_back(buildLocation(
				uri,
				func->line, func->column,
				func->line, func->column + static_cast<int>(targetName.size())));
		}

		// Check parameter types for type references
		for (const auto &param : func->parameters) {
			if (targetKind == SymbolKind::Type && param.type == targetName) {
				refs.push_back(buildLocation(
					uri,
					param.line, param.column,
					param.line, param.column + static_cast<int>(param.name.size())));
			}
		}

		visitFunction(func.get(), targetName, targetKind, containingType, refs, uri);
	}

	// Process struct definitions
	for (const auto &structDef : ast->structs) {
		// Check if struct name matches (for Type kind)
		if (targetKind == SymbolKind::Type && structDef->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				structDef->line, structDef->column,
				structDef->line, structDef->column + static_cast<int>(targetName.size())));
		}

		// Check field types for type references
		for (const auto &field : structDef->fields) {
			if (targetKind == SymbolKind::Type && field.type == targetName) {
				refs.push_back(buildLocation(
					uri,
					field.line, field.column,
					field.line, field.column + static_cast<int>(field.name.size())));
			}

			// Check if this is a field declaration we're looking for
			if (targetKind == SymbolKind::Field &&
				structDef->name == containingType &&
				field.name == targetName) {
				refs.push_back(buildLocation(
					uri,
					field.line, field.column,
					field.line, field.column + static_cast<int>(field.name.size())));
			}

			// Visit field default value expressions
			if (field.defaultValue) {
				visitExpression(field.defaultValue.get(), targetName, targetKind, containingType, refs, uri);
			}
		}

		// Check interface conformance for type references
		if (targetKind == SymbolKind::Type) {
			for (const auto &iface : structDef->conformsTo) {
				if (iface == targetName) {
					// The interface name appears in the struct definition
					refs.push_back(buildLocation(
						uri,
						structDef->line, structDef->column,
						structDef->line, structDef->column + static_cast<int>(iface.size())));
				}
			}
		}

		// Visit struct methods
		for (const auto &method : structDef->methods) {
			visitFunction(method.get(), targetName, targetKind, containingType, refs, uri);
		}
	}

	// Process enum definitions
	for (const auto &enumDef : ast->enums) {
		// Check if enum name matches (for Type kind)
		if (targetKind == SymbolKind::Type && enumDef->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				enumDef->line, enumDef->column,
				enumDef->line, enumDef->column + static_cast<int>(targetName.size())));
		}

		// Check enum cases
		for (const auto &enumCase : enumDef->cases) {
			if (targetKind == SymbolKind::EnumCase &&
				enumDef->name == containingType &&
				enumCase.name == targetName) {
				refs.push_back(buildLocation(
					uri,
					enumCase.line, enumCase.column,
					enumCase.line, enumCase.column + static_cast<int>(enumCase.name.size())));
			}

			// Check associated value types
			for (const auto &assoc : enumCase.associatedValues) {
				if (targetKind == SymbolKind::Type && assoc.type == targetName) {
					refs.push_back(buildLocation(
						uri,
						assoc.line, assoc.column,
						assoc.line, assoc.column + static_cast<int>(assoc.name.size())));
				}
			}

			// Visit raw value expressions
			if (enumCase.rawValue) {
				visitExpression(enumCase.rawValue.get(), targetName, targetKind, containingType, refs, uri);
			}
		}

		// Visit enum methods
		for (const auto &method : enumDef->methods) {
			visitFunction(method.get(), targetName, targetKind, containingType, refs, uri);
		}
	}

	// Process interface definitions
	for (const auto &iface : ast->interfaces) {
		if (targetKind == SymbolKind::Type && iface->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				iface->line, iface->column,
				iface->line, iface->column + static_cast<int>(targetName.size())));
		}

		// Check method return types and parameter types
		for (const auto &method : iface->methods) {
			if (targetKind == SymbolKind::Type && method.returnType == targetName) {
				refs.push_back(buildLocation(
					uri,
					method.line, method.column,
					method.line, method.column + static_cast<int>(method.name.size())));
			}

			for (const auto &param : method.parameters) {
				if (targetKind == SymbolKind::Type && param.type == targetName) {
					refs.push_back(buildLocation(
						uri,
						param.line, param.column,
						param.line, param.column + static_cast<int>(param.name.size())));
				}
			}
		}
	}
}

void ReferencesProvider::visitFunction(
	const FunctionAST *func,
	const std::string &targetName,
	SymbolKind targetKind,
	const std::string &containingType,
	std::vector<Location> &refs,
	const std::string &uri) {
	// Visit function body
	for (const auto &stmt : func->body) {
		visitStatement(stmt.get(), targetName, targetKind, containingType, refs, uri);
	}
}

void ReferencesProvider::visitStatement(
	const StmtAST *stmt,
	const std::string &targetName,
	SymbolKind targetKind,
	const std::string &containingType,
	std::vector<Location> &refs,
	const std::string &uri) {
	if (!stmt)
		return;

	// Variable declaration
	if (auto *varDecl = dynamic_cast<const VarDeclStmtAST *>(stmt)) {
		// Check if this is a variable declaration we're looking for
		if (targetKind == SymbolKind::Variable && varDecl->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				varDecl->line, varDecl->column,
				varDecl->line, varDecl->column + static_cast<int>(varDecl->name.size())));
		}

		// Check type annotation for type references
		if (targetKind == SymbolKind::Type && varDecl->type == targetName) {
			// Type comes after the variable name and a space: "var name Type"
			// Calculate the column of the type
			int typeColumn = varDecl->column + static_cast<int>(varDecl->name.size()) + 1;
			refs.push_back(buildLocation(
				uri,
				varDecl->line, typeColumn,
				varDecl->line, typeColumn + static_cast<int>(varDecl->type.size())));
		}

		if (varDecl->initializer) {
			visitExpression(varDecl->initializer.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Let declaration
	else if (auto *letDecl = dynamic_cast<const LetDeclStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && letDecl->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				letDecl->line, letDecl->column,
				letDecl->line, letDecl->column + static_cast<int>(letDecl->name.size())));
		}

		if (targetKind == SymbolKind::Type && letDecl->type == targetName) {
			// Type comes after the variable name and a space: "let name Type"
			int typeColumn = letDecl->column + static_cast<int>(letDecl->name.size()) + 1;
			refs.push_back(buildLocation(
				uri,
				letDecl->line, typeColumn,
				letDecl->line, typeColumn + static_cast<int>(letDecl->type.size())));
		}

		if (letDecl->initializer) {
			visitExpression(letDecl->initializer.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Assignment
	else if (auto *assign = dynamic_cast<const AssignStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && assign->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				assign->line, assign->column,
				assign->line, assign->column + static_cast<int>(assign->name.size())));
		}

		visitExpression(assign->value.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Array assignment
	else if (auto *arrAssign = dynamic_cast<const ArrayAssignStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && arrAssign->arrayName == targetName) {
			refs.push_back(buildLocation(
				uri,
				arrAssign->line, arrAssign->column,
				arrAssign->line, arrAssign->column + static_cast<int>(arrAssign->arrayName.size())));
		}

		visitExpression(arrAssign->index.get(), targetName, targetKind, containingType, refs, uri);
		visitExpression(arrAssign->value.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Member assignment
	else if (auto *memberAssign = dynamic_cast<const MemberAssignStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && memberAssign->objectName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberAssign->line, memberAssign->column,
				memberAssign->line, memberAssign->column + static_cast<int>(memberAssign->objectName.size())));
		}

		// Check for field references
		if (targetKind == SymbolKind::Field && memberAssign->memberName == targetName) {
			// We would need to verify the object type matches containingType
			// For now, add as a potential match
			refs.push_back(buildLocation(
				uri,
				memberAssign->line, memberAssign->column,
				memberAssign->line, memberAssign->column + static_cast<int>(memberAssign->memberName.size())));
		}

		visitExpression(memberAssign->value.get(), targetName, targetKind, containingType, refs, uri);
	}
	// If statement
	else if (auto *ifStmt = dynamic_cast<const IfStmtAST *>(stmt)) {
		visitExpression(ifStmt->condition.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &thenStmt : ifStmt->thenBody) {
			visitStatement(thenStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
		for (const auto &elseStmt : ifStmt->elseBody) {
			visitStatement(elseStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// If-let statement
	else if (auto *ifLet = dynamic_cast<const IfLetStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && ifLet->bindingName == targetName) {
			refs.push_back(buildLocation(
				uri,
				ifLet->line, ifLet->column,
				ifLet->line, ifLet->column + static_cast<int>(ifLet->bindingName.size())));
		}

		visitExpression(ifLet->optionalExpr.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &thenStmt : ifLet->thenBody) {
			visitStatement(thenStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
		for (const auto &elseStmt : ifLet->elseBody) {
			visitStatement(elseStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// While statement
	else if (auto *whileStmt = dynamic_cast<const WhileStmtAST *>(stmt)) {
		visitExpression(whileStmt->condition.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &bodyStmt : whileStmt->body) {
			visitStatement(bodyStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// For statement
	else if (auto *forStmt = dynamic_cast<const ForStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && forStmt->loopVar == targetName) {
			refs.push_back(buildLocation(
				uri,
				forStmt->line, forStmt->column,
				forStmt->line, forStmt->column + static_cast<int>(forStmt->loopVar.size())));
		}

		visitExpression(forStmt->iterable.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &bodyStmt : forStmt->body) {
			visitStatement(bodyStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Return statement
	else if (auto *retStmt = dynamic_cast<const ReturnStmtAST *>(stmt)) {
		if (retStmt->value) {
			visitExpression(retStmt->value.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Expression statement
	else if (auto *exprStmt = dynamic_cast<const ExprStmtAST *>(stmt)) {
		visitExpression(exprStmt->expression.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Match statement
	else if (auto *matchStmt = dynamic_cast<const MatchStmtAST *>(stmt)) {
		visitExpression(matchStmt->scrutinee.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &matchCase : matchStmt->cases) {
			for (const auto &pattern : matchCase.patterns) {
				visitExpression(pattern.get(), targetName, targetKind, containingType, refs, uri);
			}
			if (matchCase.statement) {
				visitStatement(matchCase.statement.get(), targetName, targetKind, containingType, refs, uri);
			}
			if (matchCase.resultExpr) {
				visitExpression(matchCase.resultExpr.get(), targetName, targetKind, containingType, refs, uri);
			}
		}
	}
	// If-case statement
	else if (auto *ifCase = dynamic_cast<const IfCaseStmtAST *>(stmt)) {
		visitExpression(ifCase->enumExpr.get(), targetName, targetKind, containingType, refs, uri);

		// Check bindings for variable references
		for (const auto &binding : ifCase->bindings) {
			if (targetKind == SymbolKind::Variable && binding == targetName) {
				refs.push_back(buildLocation(
					uri,
					ifCase->line, ifCase->column,
					ifCase->line, ifCase->column + static_cast<int>(binding.size())));
			}
		}

		for (const auto &thenStmt : ifCase->thenBody) {
			visitStatement(thenStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
		for (const auto &elseStmt : ifCase->elseBody) {
			visitStatement(elseStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Else-unwrap statement
	else if (auto *elseUnwrap = dynamic_cast<const ElseUnwrapStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && elseUnwrap->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				elseUnwrap->line, elseUnwrap->column,
				elseUnwrap->line, elseUnwrap->column + static_cast<int>(elseUnwrap->name.size())));
		}

		if (targetKind == SymbolKind::Type && elseUnwrap->declaredType == targetName) {
			refs.push_back(buildLocation(
				uri,
				elseUnwrap->line, elseUnwrap->column,
				elseUnwrap->line, elseUnwrap->column + static_cast<int>(elseUnwrap->declaredType.size())));
		}

		visitExpression(elseUnwrap->optionalExpr.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &elseStmt : elseUnwrap->elseBody) {
			visitStatement(elseStmt.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Array member assignment
	else if (auto *arrMemberAssign = dynamic_cast<const ArrayMemberAssignStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && arrMemberAssign->arrayName == targetName) {
			refs.push_back(buildLocation(
				uri,
				arrMemberAssign->line, arrMemberAssign->column,
				arrMemberAssign->line, arrMemberAssign->column + static_cast<int>(arrMemberAssign->arrayName.size())));
		}

		if (targetKind == SymbolKind::Field && arrMemberAssign->memberName == targetName) {
			refs.push_back(buildLocation(
				uri,
				arrMemberAssign->line, arrMemberAssign->column,
				arrMemberAssign->line, arrMemberAssign->column + static_cast<int>(arrMemberAssign->memberName.size())));
		}

		visitExpression(arrMemberAssign->index.get(), targetName, targetKind, containingType, refs, uri);
		visitExpression(arrMemberAssign->value.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Member array assignment
	else if (auto *memberArrAssign = dynamic_cast<const MemberArrayAssignStmtAST *>(stmt)) {
		if (targetKind == SymbolKind::Variable && memberArrAssign->objectName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberArrAssign->line, memberArrAssign->column,
				memberArrAssign->line, memberArrAssign->column + static_cast<int>(memberArrAssign->objectName.size())));
		}

		if (targetKind == SymbolKind::Field && memberArrAssign->memberName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberArrAssign->line, memberArrAssign->column,
				memberArrAssign->line, memberArrAssign->column + static_cast<int>(memberArrAssign->memberName.size())));
		}

		visitExpression(memberArrAssign->index.get(), targetName, targetKind, containingType, refs, uri);
		visitExpression(memberArrAssign->value.get(), targetName, targetKind, containingType, refs, uri);
	}
}

void ReferencesProvider::visitExpression(
	const ExprAST *expr,
	const std::string &targetName,
	SymbolKind targetKind,
	const std::string &containingType,
	std::vector<Location> &refs,
	const std::string &uri) {
	if (!expr)
		return;

	// Variable expression
	if (auto *varExpr = dynamic_cast<const VariableExprAST *>(expr)) {
		if (targetKind == SymbolKind::Variable && varExpr->name == targetName) {
			refs.push_back(buildLocation(
				uri,
				varExpr->line, varExpr->column,
				varExpr->endLine > 0 ? varExpr->endLine : varExpr->line,
				varExpr->endColumn > 0 ? varExpr->endColumn : varExpr->column + static_cast<int>(varExpr->name.size())));
		}
	}
	// Call expression
	else if (auto *callExpr = dynamic_cast<const CallExprAST *>(expr)) {
		if (targetKind == SymbolKind::Function && callExpr->callee == targetName) {
			refs.push_back(buildLocation(
				uri,
				callExpr->line, callExpr->column,
				callExpr->line, callExpr->column + static_cast<int>(callExpr->callee.size())));
		}

		// Check if this is an enum case construction
		if (targetKind == SymbolKind::EnumCase &&
			!callExpr->resolvedEnumName.empty() &&
			callExpr->resolvedEnumName == containingType &&
			callExpr->resolvedEnumCaseName == targetName) {
			refs.push_back(buildLocation(
				uri,
				callExpr->line, callExpr->column,
				callExpr->line, callExpr->column + static_cast<int>(callExpr->callee.size())));
		}

		// Visit arguments
		for (const auto &arg : callExpr->args) {
			visitExpression(arg.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Binary expression
	else if (auto *binExpr = dynamic_cast<const BinaryExprAST *>(expr)) {
		visitExpression(binExpr->left.get(), targetName, targetKind, containingType, refs, uri);
		visitExpression(binExpr->right.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Unary expression
	else if (auto *unaryExpr = dynamic_cast<const UnaryExprAST *>(expr)) {
		visitExpression(unaryExpr->operand.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Member access expression
	else if (auto *memberExpr = dynamic_cast<const MemberAccessExprAST *>(expr)) {
		// Check for field reference
		if (targetKind == SymbolKind::Field && memberExpr->memberName == targetName) {
			// The member name starts after the dot
			int memberStart = memberExpr->column;
			if (!memberExpr->objectName.empty()) {
				memberStart += static_cast<int>(memberExpr->objectName.size()) + 1; // +1 for dot
			}
			refs.push_back(buildLocation(
				uri,
				memberExpr->line, memberStart,
				memberExpr->line, memberStart + static_cast<int>(memberExpr->memberName.size())));
		}

		// Check for variable reference in the object
		if (targetKind == SymbolKind::Variable && memberExpr->objectName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberExpr->line, memberExpr->column,
				memberExpr->line, memberExpr->column + static_cast<int>(memberExpr->objectName.size())));
		}

		// Check for enum case reference
		if (targetKind == SymbolKind::EnumCase &&
			!memberExpr->resolvedEnumName.empty() &&
			memberExpr->resolvedEnumName == containingType &&
			memberExpr->resolvedEnumCaseName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberExpr->line, memberExpr->column,
				memberExpr->endLine > 0 ? memberExpr->endLine : memberExpr->line,
				memberExpr->endColumn > 0 ? memberExpr->endColumn : memberExpr->column + static_cast<int>(memberExpr->objectName.size() + 1 + memberExpr->memberName.size())));
		}

		// Check for type reference (static access)
		if (targetKind == SymbolKind::Type && memberExpr->objectName == targetName) {
			refs.push_back(buildLocation(
				uri,
				memberExpr->line, memberExpr->column,
				memberExpr->line, memberExpr->column + static_cast<int>(memberExpr->objectName.size())));
		}

		// Visit the object expression if it exists
		if (memberExpr->object) {
			visitExpression(memberExpr->object.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Array index expression
	else if (auto *indexExpr = dynamic_cast<const ArrayIndexExprAST *>(expr)) {
		if (targetKind == SymbolKind::Variable && !indexExpr->arrayName.empty() && indexExpr->arrayName == targetName) {
			refs.push_back(buildLocation(
				uri,
				indexExpr->line, indexExpr->column,
				indexExpr->line, indexExpr->column + static_cast<int>(indexExpr->arrayName.size())));
		}

		if (indexExpr->arrayExpr) {
			visitExpression(indexExpr->arrayExpr.get(), targetName, targetKind, containingType, refs, uri);
		}
		visitExpression(indexExpr->index.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Slice expression
	else if (auto *sliceExpr = dynamic_cast<const SliceExprAST *>(expr)) {
		if (targetKind == SymbolKind::Variable && sliceExpr->objectName == targetName) {
			refs.push_back(buildLocation(
				uri,
				sliceExpr->line, sliceExpr->column,
				sliceExpr->line, sliceExpr->column + static_cast<int>(sliceExpr->objectName.size())));
		}

		if (sliceExpr->start) {
			visitExpression(sliceExpr->start.get(), targetName, targetKind, containingType, refs, uri);
		}
		if (sliceExpr->end) {
			visitExpression(sliceExpr->end.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Array literal expression
	else if (auto *arrLit = dynamic_cast<const ArrayLiteralExprAST *>(expr)) {
		for (const auto &value : arrLit->values) {
			visitExpression(value.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Struct init expression
	else if (auto *structInit = dynamic_cast<const StructInitExprAST *>(expr)) {
		if (targetKind == SymbolKind::Type && structInit->structName == targetName) {
			refs.push_back(buildLocation(
				uri,
				structInit->line, structInit->column,
				structInit->line, structInit->column + static_cast<int>(structInit->structName.size())));
		}

		// Check field initializers
		for (const auto &field : structInit->fields) {
			if (targetKind == SymbolKind::Field &&
				containingType == structInit->structName &&
				field.name == targetName) {
				refs.push_back(buildLocation(
					uri,
					field.line, field.column,
					field.line, field.column + static_cast<int>(field.name.size())));
			}

			visitExpression(field.value.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
	// Cast expression
	else if (auto *castExpr = dynamic_cast<const CastExprAST *>(expr)) {
		if (targetKind == SymbolKind::Type && castExpr->targetType == targetName) {
			refs.push_back(buildLocation(
				uri,
				castExpr->line, castExpr->column,
				castExpr->line, castExpr->column + static_cast<int>(castExpr->targetType.size())));
		}

		visitExpression(castExpr->expr.get(), targetName, targetKind, containingType, refs, uri);
	}
	// Map literal expression
	else if (auto *mapLit = dynamic_cast<const MapLiteralExprAST *>(expr)) {
		if (targetKind == SymbolKind::Type) {
			if (mapLit->keyType == targetName) {
				refs.push_back(buildLocation(
					uri,
					mapLit->line, mapLit->column,
					mapLit->line, mapLit->column + static_cast<int>(mapLit->keyType.size())));
			}
			if (mapLit->valueType == targetName) {
				refs.push_back(buildLocation(
					uri,
					mapLit->line, mapLit->column,
					mapLit->line, mapLit->column + static_cast<int>(mapLit->valueType.size())));
			}
		}
	}
	// Match expression
	else if (auto *matchExpr = dynamic_cast<const MatchExprAST *>(expr)) {
		visitExpression(matchExpr->scrutinee.get(), targetName, targetKind, containingType, refs, uri);

		for (const auto &matchCase : matchExpr->cases) {
			for (const auto &pattern : matchCase.patterns) {
				visitExpression(pattern.get(), targetName, targetKind, containingType, refs, uri);
			}
			if (matchCase.resultExpr) {
				visitExpression(matchCase.resultExpr.get(), targetName, targetKind, containingType, refs, uri);
			}
		}
	}
	// Enum case expression
	else if (auto *enumCaseExpr = dynamic_cast<const EnumCaseExprAST *>(expr)) {
		if (targetKind == SymbolKind::Type && enumCaseExpr->enumName == targetName) {
			refs.push_back(buildLocation(
				uri,
				enumCaseExpr->line, enumCaseExpr->column,
				enumCaseExpr->line, enumCaseExpr->column + static_cast<int>(enumCaseExpr->enumName.size())));
		}

		if (targetKind == SymbolKind::EnumCase &&
			enumCaseExpr->enumName == containingType &&
			enumCaseExpr->caseName == targetName) {
			int caseStart = enumCaseExpr->column + static_cast<int>(enumCaseExpr->enumName.size()) + 1;
			refs.push_back(buildLocation(
				uri,
				enumCaseExpr->line, caseStart,
				enumCaseExpr->line, caseStart + static_cast<int>(enumCaseExpr->caseName.size())));
		}

		for (const auto &arg : enumCaseExpr->arguments) {
			visitExpression(arg.get(), targetName, targetKind, containingType, refs, uri);
		}
	}
}

Location ReferencesProvider::buildLocation(
	const std::string &uri,
	int line,
	int col,
	int endLine,
	int endCol) {
	Location loc;
	loc.uri = uri;

	// Convert 1-based compiler positions to 0-based LSP positions
	int startLine = line > 0 ? line - 1 : 0;
	int startCol_ = col > 0 ? col - 1 : 0;
	int endLine_ = endLine > 0 ? endLine - 1 : startLine;
	int endCol_ = endCol > 0 ? endCol - 1 : startCol_ + 1;

	loc.range = Range(startLine, startCol_, endLine_, endCol_);
	return loc;
}

bool ReferencesProvider::isPositionInRange(const Position &pos, const SourceRange &range) {
	// Convert 0-based LSP position to 1-based for comparison
	int line = pos.line + 1;
	int col = pos.character + 1;

	return range.contains(line, col);
}

} // namespace maxon_lsp
