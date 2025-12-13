#include "hover.h"
#include "../../intrinsics_defs.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include "../../types/type_conversion.h"

namespace maxon_lsp {

std::optional<Hover> HoverProvider::getHover(
	const Document &document,
	const Position &position,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	// Don't provide hover info inside comments
	if (isPositionInComment(document, position)) {
		return std::nullopt;
	}

	// Get the token at the cursor position
	std::string token = getTokenAtPosition(document, position);
	if (token.empty()) {
		return std::nullopt;
	}

	// Get the range for highlighting (stored in member variable)
	(void)getTokenRange(document, position);

	// Check for dot-separated field access (e.g., "point.x") or method call (e.g., "s.cstr")
	size_t dotPos = token.find('.');
	if (dotPos != std::string::npos) {
		std::string prefix = token.substr(0, dotPos);
		std::string suffix = token.substr(dotPos + 1);

		// Try to look up as struct.field
		auto hover = lookupField(prefix, suffix, cache, stdlib);
		if (hover) {
			return hover;
		}

		// If prefix is a variable, get its type and look up field or method on that type
		if (cache) {
			// Find the variable using qualified lookup
			std::string varType;

			// Find enclosing function to build qualified key
			std::string enclosingFunction;
			if (cache->ast) {
				for (const auto &func : cache->ast->functions) {
					if (position.line >= func->line - 1 && position.line <= func->endLine - 1) {
						if (!func->namespaceName.empty()) {
							enclosingFunction = func->namespaceName + "." + func->name;
						} else {
							enclosingFunction = func->name;
						}
						break;
					}
				}
				for (const auto &structDef : cache->ast->structs) {
					for (const auto &method : structDef->methods) {
						if (position.line >= method->line - 1 && position.line <= method->endLine - 1) {
							enclosingFunction = structDef->name + "." + method->name;
							break;
						}
					}
				}
			}

			// Try qualified lookup first
			if (!enclosingFunction.empty()) {
				std::string qualifiedKey = enclosingFunction + "::" + prefix;
				auto varIt = cache->variables.find(qualifiedKey);
				if (varIt != cache->variables.end()) {
					varType = varIt->second.type;
				}
			}

			// Fall back to unqualified lookup
			if (varType.empty()) {
				auto varIt = cache->variables.find(prefix);
				if (varIt != cache->variables.end()) {
					varType = varIt->second.type;
				}
			}

			if (!varType.empty()) {
				// Try field lookup first
				auto fieldHover = lookupField(varType, suffix, cache, stdlib);
				if (fieldHover) {
					return fieldHover;
				}

				// Try method lookup: look for Type.method in stdlib functions
				std::string qualifiedMethodName = varType + "." + suffix;
				auto methodHover = lookupFunction(qualifiedMethodName, cache, stdlib);
				if (methodHover) {
					return methodHover;
				}
			}
		}

		// Try to look up as Type.method directly (prefix is already a type name)
		auto methodHover = lookupFunction(token, cache, stdlib);
		if (methodHover) {
			return methodHover;
		}

		// Try to look up the prefix as a type (e.g., enum name in EnumName.case)
		auto prefixTypeHover = lookupType(prefix, cache, stdlib);
		if (prefixTypeHover) {
			return prefixTypeHover;
		}
	}

	// Try lookups in order: intrinsic, keyword, struct field declaration, variable, function, type

	// 0. Try intrinsic lookup (tokens starting with __)
	if (token.size() > 2 && token[0] == '_' && token[1] == '_') {
		auto intrinsicHover = lookupIntrinsic(token);
		if (intrinsicHover) {
			return intrinsicHover;
		}
	}

	// 1. Try keyword lookup
	auto keywordHover = lookupKeyword(token);
	if (keywordHover) {
		return keywordHover;
	}

	// 2. Try struct field declaration lookup (when cursor is on a field in type definition)
	auto fieldDeclHover = lookupFieldDeclaration(token, cache, position);
	if (fieldDeclHover) {
		return fieldDeclHover;
	}

	// 3. Try variable lookup
	auto variableHover = lookupVariable(token, cache, position);
	if (variableHover) {
		return variableHover;
	}

	// 4. Try function lookup
	auto functionHover = lookupFunction(token, cache, stdlib);
	if (functionHover) {
		return functionHover;
	}

	// 5. Try type lookup
	auto typeHover = lookupType(token, cache, stdlib);
	if (typeHover) {
		return typeHover;
	}

	return std::nullopt;
}

std::string HoverProvider::getTokenAtPosition(const Document &document, const Position &position) {
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

Range HoverProvider::getTokenRange(const Document &document, const Position &position) {
	// This is called after getTokenAtPosition, which sets currentTokenRange_
	return currentTokenRange_;
}

std::optional<Hover> HoverProvider::lookupKeyword(const std::string &token) {
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

std::optional<Hover> HoverProvider::lookupVariable(const std::string &name, const AnalysisCache *cache, const Position &position) {
	if (!cache) {
		return std::nullopt;
	}

	// Find which function the cursor is in to use the correct qualified variable name
	// The key must match getFunctionKey() format: namespace.funcName for namespaced functions,
	// StructName.methodName for methods, or just funcName for standalone functions
	std::string enclosingFunction;
	if (cache->ast) {
		for (const auto &func : cache->ast->functions) {
			// Check if position is within this function's range
			if (position.line >= func->line - 1 && position.line <= func->endLine - 1) {
				// Use namespace-qualified name if function is in a namespace
				if (!func->namespaceName.empty()) {
					enclosingFunction = func->namespaceName + "." + func->name;
				} else {
					enclosingFunction = func->name;
				}
				break;
			}
		}
		// Also check struct methods (only if not already found in top-level functions)
		if (enclosingFunction.empty()) {
			for (const auto &structDef : cache->ast->structs) {
				bool found = false;
				for (const auto &method : structDef->methods) {
					if (position.line >= method->line - 1 && position.line <= method->endLine - 1) {
						enclosingFunction = structDef->name + "." + method->name;
						found = true;
						break;
					}
				}
				if (found)
					break;
			}
		}
		// Also check enum methods
		if (enclosingFunction.empty()) {
			for (const auto &enumDef : cache->ast->enums) {
				bool found = false;
				for (const auto &method : enumDef->methods) {
					if (position.line >= method->line - 1 && position.line <= method->endLine - 1) {
						enclosingFunction = enumDef->name + "." + method->name;
						found = true;
						break;
					}
				}
				if (found)
					break;
			}
		}
	}

	// Try function-qualified lookup first
	if (!enclosingFunction.empty()) {
		std::string qualifiedKey = enclosingFunction + "::" + name;
		auto it = cache->variables.find(qualifiedKey);
		if (it != cache->variables.end()) {
			const VariableInfo &var = it->second;
			std::string markdown = formatVariableHover(var.name, var.type, !var.isImmutable, var.initialValue, var.isParameter);
			return buildHover(markdown, currentTokenRange_);
		}
	}

	// Fall back to unqualified lookup (for global variables or if function not found)
	auto it = cache->variables.find(name);
	if (it != cache->variables.end()) {
		const VariableInfo &var = it->second;
		std::string markdown = formatVariableHover(var.name, var.type, !var.isImmutable, var.initialValue, var.isParameter);
		return buildHover(markdown, currentTokenRange_);
	}

	return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupFieldDeclaration(const std::string &name, const AnalysisCache *cache, const Position &position) {
	if (!cache || !cache->ast) {
		return std::nullopt;
	}

	// Check if position is inside a struct definition
	for (const auto &structDef : cache->ast->structs) {
		// Check if position is within this struct's range (1-based lines in AST, 0-based in Position)
		int startLine = structDef->line - 1;
		int endLine = structDef->endLine - 1;

		if (position.line >= startLine && position.line <= endLine) {
			// We're inside this struct, look for the field
			for (const auto &field : structDef->fields) {
				if (field.name == name) {
					// Found the field - format hover info
					std::string markdown;
					std::string mutability = field.isImmutable ? "let" : "var";
					markdown = "```maxon\n" + mutability + " " + name + " " + field.type;
					if (field.defaultValue) {
						markdown += " = ..."; // Default value is an AST expression, not a string
					}
					markdown += "\n```\n---\n";
					markdown += "Field of type `" + structDef->name + "`";
					return buildHover(markdown, currentTokenRange_);
				}
			}
		}
	}

	return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupFunction(const std::string &name, const AnalysisCache *cache, const StdlibSymbols &stdlib) {
	// First check in cache with exact match
	if (cache) {
		auto it = cache->functions.find(name);
		if (it != cache->functions.end()) {
			std::string markdown = formatFunctionHover(it->second);
			return buildHover(markdown, currentTokenRange_);
		}

		// If not found, search for namespaced functions that end with ".name"
		// This handles the case where user hovers over "getPermutation" but the function
		// is stored as "examples.getPermutation" due to file namespace
		std::string suffix = "." + name;
		for (const auto &[funcName, funcInfo] : cache->functions) {
			if (funcName.size() > suffix.size() &&
				funcName.compare(funcName.size() - suffix.size(), suffix.size(), suffix) == 0) {
				std::string markdown = formatFunctionHover(funcInfo);
				return buildHover(markdown, currentTokenRange_);
			}
		}
	}

	// Check in stdlib functions
	for (const auto &func : stdlib.functions) {
		if (func.name == name) {
			std::string markdown = formatFunctionHover(func);
			return buildHover(markdown, currentTokenRange_);
		}
	}

	return std::nullopt;
}

std::optional<Hover> HoverProvider::lookupType(const std::string &name, const AnalysisCache *cache, const StdlibSymbols &stdlib) {
	// Check built-in types from keyword matcher
	KeywordEntry entry;
	if (KeywordMatcher::match(name.data(), name.size(), entry) &&
		entry.category == KeywordCategory::Type) {
		std::string markdown = "```maxon\n" + name + "\n```\n\n" + entry.description + "\n";
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

		// Check enums in AST
		if (cache->ast) {
			for (const auto &enumDef : cache->ast->enums) {
				if (enumDef->name == name) {
					// Build EnumInfo from AST node
					EnumInfo enumInfo(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);

					int tagValue = 0;
					for (const auto &caseNode : enumDef->cases) {
						EnumCaseInfo caseInfo(caseNode.name, tagValue++, caseNode.line, caseNode.column);
						caseInfo.hasRawValue = (caseNode.rawValue != nullptr);
						// TODO: extract raw value if needed

						for (const auto &assoc : caseNode.associatedValues) {
							EnumAssocValueInfo assocInfo(assoc.name, assoc.type, assoc.line, assoc.column);
							caseInfo.associatedValues.push_back(assocInfo);
						}

						enumInfo.cases.push_back(caseInfo);
					}

					std::string markdown = formatEnumHover(enumInfo, "");
					return buildHover(markdown, currentTokenRange_);
				}
			}
		}
	}

	// Check stdlib structs
	for (const auto &structSym : stdlib.structs) {
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
	for (const auto &enumSym : stdlib.enums) {
		if (enumSym.name == name) {
			std::string markdown = "```maxon\nenum " + name + "\n```\n";
			if (!enumSym.documentation.empty()) {
				markdown += "\n" + enumSym.documentation + "\n";
			}
			return buildHover(markdown, currentTokenRange_);
		}
	}

	// Check stdlib interfaces
	for (const auto &ifaceSym : stdlib.interfaces) {
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

std::optional<Hover> HoverProvider::lookupField(const std::string &structName, const std::string &fieldName,
												const AnalysisCache *cache, const StdlibSymbols &stdlib) {
	// Check structs in cache
	if (cache) {
		auto structIt = cache->structs.find(structName);
		if (structIt != cache->structs.end()) {
			for (const auto &field : structIt->second.fields) {
				if (field.name == fieldName) {
					std::string markdown = formatFieldHover(structName, fieldName, field.type);
					return buildHover(markdown, currentTokenRange_);
				}
			}
		}
	}

	// Check stdlib structs
	for (const auto &structSym : stdlib.structs) {
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

std::optional<Hover> HoverProvider::lookupIntrinsic(const std::string &name) {
	// Look up in the intrinsic definitions
	auto intrinsics = getIntrinsicDefinitions();
	for (const auto &def : intrinsics) {
		if (def.name == name) {
			std::string markdown = formatIntrinsicHover(def.name, def.returnType, def.params);
			return buildHover(markdown, currentTokenRange_);
		}
	}
	return std::nullopt;
}

std::string HoverProvider::formatIntrinsicHover(const std::string &name, const std::string &returnType,
												const std::vector<IntrinsicParamDef> &params) {
	std::string md = "```maxon\n";
	md += "(intrinsic) " + name + "(";

	// Format parameters
	bool first = true;
	int paramIndex = 0;
	for (const auto &param : params) {
		if (!first) {
			md += ", ";
		}
		first = false;

		// Generate a descriptive parameter type
		std::string paramType;
		if (param.isAnyType) {
			paramType = "any";
		} else if (param.isArrayType) {
			if (param.allowedTypes.empty()) {
				paramType = "array of T";
			} else {
				paramType = "array of " + param.allowedTypes[0];
			}
		} else if (!param.allowedTypes.empty()) {
			// Multiple allowed types
			paramType = param.allowedTypes[0];
			for (size_t i = 1; i < param.allowedTypes.size(); ++i) {
				paramType += " | " + param.allowedTypes[i];
			}
		} else {
			paramType = param.type;
		}

		// Convert internal types to display format
		paramType = maxon::TypeConversion::arrayTypeToDisplayString(paramType);

		md += "p" + std::to_string(paramIndex++) + " " + paramType;
	}

	md += ") returns ";

	// Convert return type to display format
	std::string displayReturnType = maxon::TypeConversion::arrayTypeToDisplayString(returnType);
	md += displayReturnType;
	md += "\n```\n";

	md += "\n(compiler intrinsic)\n";

	return md;
}

std::string HoverProvider::formatKeywordHover(const KeywordLSPInfo &keyword) {
	std::string md = "```maxon\n";
	md += keyword.name;

	// For math intrinsics, show the return type
	if (!keyword.returnType.empty()) {
		md += "(...) " + keyword.returnType;
	}

	md += "\n```\n";

	// Add category information
	std::string categoryName;
	switch (keyword.category) {
	case KeywordCategory::Type:
		categoryName = "type";
		break;
	case KeywordCategory::ControlFlow:
		categoryName = "control flow keyword";
		break;
	case KeywordCategory::Declaration:
		categoryName = "declaration keyword";
		break;
	case KeywordCategory::MathIntrinsic:
		categoryName = "math intrinsic";
		break;
	case KeywordCategory::Literal:
		categoryName = "literal keyword";
		break;
	case KeywordCategory::Operator:
		categoryName = "operator keyword";
		break;
	}

	if (!categoryName.empty()) {
		md += "\n(" + categoryName + ")\n";
	}

	if (!keyword.documentation.empty()) {
		md += "\n" + keyword.documentation + "\n";
	}

	return md;
}

std::string HoverProvider::formatVariableHover(const std::string &name, const std::string &type,
											   bool isMutable, const std::string &value, bool isParameter, const std::string &doc) {
	// Convert internal type format to display format (e.g., _ManagedArray<int> -> array of int)
	std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(type);

	std::string md = "```maxon\n";
	md += isMutable ? "var " : "let ";
	md += name + " " + displayType;
	// Show value for immutable variables (like constants)
	if (!isMutable && !value.empty()) {
		md += " = " + value;
	}
	md += "\n```\n";

	if (isParameter) {
		md += "\nFunction parameter\n";
	}

	if (!doc.empty()) {
		md += "\n" + doc + "\n";
	}

	return md;
}

std::string HoverProvider::formatFunctionHover(const FunctionInfo &func, const std::string &doc) {
	std::string md = "```maxon\n";

	// Extract the simple function name (after the last dot, if any)
	// This handles namespaced functions like "examples.getPermutation" -> "getPermutation"
	std::string displayName = func.name;
	size_t lastDot = displayName.rfind('.');
	if (lastDot != std::string::npos) {
		displayName = displayName.substr(lastDot + 1);
	}

	md += "function " + displayName + "(";

	// Add parameters
	bool first = true;
	for (const auto &param : func.parameters) {
		if (!first) {
			md += ", ";
		}
		first = false;
		std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(param.type);
		md += param.name + " " + displayType;
	}

	std::string returnDisplayType = maxon::TypeConversion::arrayTypeToDisplayString(func.returnType);
	md += ") " + returnDisplayType;
	md += "\n```\n";

	if (!doc.empty()) {
		md += "\n" + doc + "\n";
	}

	return md;
}

std::string HoverProvider::formatFunctionHover(const LSPSymbolInfo &symbol) {
	std::string md = "```maxon\n";
	md += "function " + symbol.name + "(";

	// Add parameters from LSPSymbolInfo
	bool first = true;
	for (const auto &param : symbol.parameters) {
		if (!first) {
			md += ", ";
		}
		first = false;
		std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(param.type);
		md += param.name + " " + displayType;
	}

	md += ")";
	if (!symbol.returnType.empty()) {
		std::string returnDisplayType = maxon::TypeConversion::arrayTypeToDisplayString(symbol.returnType);
		md += " " + returnDisplayType;
	}
	md += "\n```\n";

	if (!symbol.documentation.empty()) {
		md += "\n" + symbol.documentation + "\n";
	}

	return md;
}

std::string HoverProvider::formatStructHover(const StructInfo &structInfo, const std::string &doc) {
	std::string md = "```maxon\n";
	md += "struct " + structInfo.name;

	// Show interface conformance if any
	if (!structInfo.conformsTo.empty()) {
		md += " is ";
		bool first = true;
		for (const auto &iface : structInfo.conformsTo) {
			if (!first) {
				md += ", ";
			}
			first = false;
			md += iface;
		}
	}

	md += "\n";

	// Show fields
	for (const auto &field : structInfo.fields) {
		md += "    ";
		md += field.isImmutable ? "let " : "var ";
		std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(field.type);
		md += field.name + " " + displayType;
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

std::string HoverProvider::formatEnumHover(const EnumInfo &enumInfo, const std::string &doc) {
	std::string md = "```maxon\n";
	md += "enum " + enumInfo.name;

	// Show raw value type if present
	if (!enumInfo.rawValueType.empty()) {
		md += " " + enumInfo.rawValueType;
	}

	md += "\n";

	// Show cases
	for (const auto &enumCase : enumInfo.cases) {
		md += "    case " + enumCase.name;

		// Show associated values if any
		if (!enumCase.associatedValues.empty()) {
			md += "(";
			bool first = true;
			for (const auto &assoc : enumCase.associatedValues) {
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

std::string HoverProvider::formatInterfaceHover(const InterfaceInfo &iface, const std::string &doc) {
	std::string md = "```maxon\n";
	md += "interface " + iface.name + "\n";

	// Show associated types
	for (const auto &assocType : iface.associatedTypes) {
		md += "    type " + assocType + "\n";
	}

	// Show methods
	for (const auto &method : iface.methods) {
		md += "    function " + method.name + "(";

		bool first = true;
		for (const auto &param : method.parameters) {
			if (!first) {
				md += ", ";
			}
			first = false;
			std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(param.type);
			md += param.name + " " + displayType;
		}

		std::string returnDisplayType = maxon::TypeConversion::arrayTypeToDisplayString(method.returnType);
		md += ") " + returnDisplayType + "\n";
	}

	md += "end '" + iface.name + "'\n";
	md += "```\n";

	if (!doc.empty()) {
		md += "\n" + doc + "\n";
	}

	return md;
}

std::string HoverProvider::formatFieldHover(const std::string &structName, const std::string &fieldName,
											const std::string &type) {
	std::string displayType = maxon::TypeConversion::arrayTypeToDisplayString(type);
	std::string md = "```maxon\n";
	md += "(field) " + structName + "." + fieldName + " " + displayType;
	md += "\n```\n";
	return md;
}

Hover HoverProvider::buildHover(const std::string &markdown, const Range &range) {
	Hover hover;

	// Create MarkupContent with markdown
	MarkupContent content;
	content.kind = MarkupKind::Markdown;
	content.value = markdown;

	hover.contents = content;
	hover.range = range;

	return hover;
}

bool HoverProvider::isPositionInComment(const Document &document, const Position &position) {
	// Check for line comment on the current line first (most common case)
	if (position.line >= 0 && position.line < document.getLineCount()) {
		std::string line = document.getLine(position.line);

		// Find if there's a // before our position (not inside a string)
		bool inString = false;
		for (int i = 0; i < static_cast<int>(line.size()) && i < position.character; i++) {
			if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) {
				inString = !inString;
			}
			if (!inString && i + 1 < static_cast<int>(line.size()) &&
				line[i] == '/' && line[i + 1] == '/') {
				// Found line comment start before our position
				return true;
			}
		}
	}

	// Check for block comments /* ... */
	// We need to scan from the start of the document to find unclosed block comments
	bool inBlockComment = false;
	for (int lineNum = 0; lineNum <= position.line && lineNum < document.getLineCount(); lineNum++) {
		std::string line = document.getLine(lineNum);
		int endCol = (lineNum == position.line) ? position.character : static_cast<int>(line.size());

		bool inString = false;
		for (int i = 0; i < endCol && i < static_cast<int>(line.size()); i++) {
			if (!inBlockComment) {
				// Track string state to avoid false positives
				if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) {
					inString = !inString;
				}
				// Check for block comment start
				if (!inString && i + 1 < static_cast<int>(line.size()) &&
					line[i] == '/' && line[i + 1] == '*') {
					inBlockComment = true;
					i++; // Skip the *
				}
			} else {
				// Inside block comment, look for */
				if (i + 1 < static_cast<int>(line.size()) &&
					line[i] == '*' && line[i + 1] == '/') {
					inBlockComment = false;
					i++; // Skip the /
				}
			}
		}
	}

	return inBlockComment;
}

} // namespace maxon_lsp
