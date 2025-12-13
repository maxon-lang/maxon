#include "definition.h"
#include <algorithm>

namespace maxon_lsp {

std::optional<DefinitionProvider::DefinitionResult> DefinitionProvider::getDefinition(
	const Document &document,
	const Position &position,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib,
	const StdlibSymbols &projectSymbols,
	const std::string &workspaceRoot) {
	// Get the symbol at the cursor position
	std::string symbol = getSymbolAtPosition(document, position);
	if (symbol.empty()) {
		return std::nullopt;
	}

	// Check for member access (obj.field or Type.field)
	std::string objectName, memberName;
	bool cursorOnObject = false;
	if (isMemberAccess(document, position, objectName, memberName, cursorOnObject)) {
		// Try to resolve the object's type and look up the field
		if (cache) {
			// Check if objectName is a variable (use qualified lookup with position)
			auto varPtr = cache->findVariable(objectName, position.line);
			if (varPtr) {
				if (cursorOnObject) {
					// Cursor is on the variable name, return variable definition
					return buildLocation(document.uri, varPtr->line, varPtr->column);
				}
				// Look up field on the variable's type
				auto fieldLoc = lookupField(varPtr->type, memberName, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
				if (fieldLoc) {
					return *fieldLoc;
				}
			}

			// Check if objectName is a struct type (static field access)
			auto structIt = cache->structs.find(objectName);
			if (structIt != cache->structs.end()) {
				if (cursorOnObject) {
					// Cursor is on the struct type name, return struct definition
					return buildLocation(document.uri, structIt->second.line, structIt->second.column);
				}
				auto fieldLoc = lookupField(objectName, memberName, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
				if (fieldLoc) {
					return *fieldLoc;
				}
			}

			// Check if objectName is an enum type (enum case access like Color.red)
			if (cache->ast) {
				for (const auto &enumDef : cache->ast->enums) {
					if (enumDef->name == objectName) {
						if (cursorOnObject) {
							// Cursor is on the enum type name, return enum definition
							return buildLocation(document.uri, enumDef->line, enumDef->column);
						}
						// Cursor is on the case name, look for the case
						for (const auto &enumCase : enumDef->cases) {
							if (enumCase.name == memberName) {
								// Return location of the enum case
								return buildLocation(document.uri, enumCase.line, enumCase.column);
							}
						}
						// Case not found, return enum definition as fallback
						return buildLocation(document.uri, enumDef->line, enumDef->column);
					}
				}
			}
		}

		// Try stdlib structs
		for (const auto &structSym : stdlib.structs) {
			if (structSym.name == objectName) {
				if (cursorOnObject) {
					// Cursor is on the struct type name
					if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
						std::string uri = toUri(structSym.filePath);
						return buildLocation(uri,
											 structSym.sourceRange.startLine,
											 structSym.sourceRange.startCol,
											 structSym.sourceRange.endLine,
											 structSym.sourceRange.endCol);
					}
				}
				auto fieldLoc = lookupField(objectName, memberName, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
				if (fieldLoc) {
					return *fieldLoc;
				}
			}
		}

		// Try project structs
		for (const auto &structSym : projectSymbols.structs) {
			if (structSym.name == objectName) {
				if (cursorOnObject) {
					// Cursor is on the struct type name
					if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
						std::string uri = toUri(structSym.filePath);
						return buildLocation(uri,
											 structSym.sourceRange.startLine,
											 structSym.sourceRange.startCol,
											 structSym.sourceRange.endLine,
											 structSym.sourceRange.endCol);
					}
				}
				auto fieldLoc = lookupField(objectName, memberName, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
				if (fieldLoc) {
					return *fieldLoc;
				}
			}
		}

		// Try stdlib enums
		for (const auto &enumSym : stdlib.enums) {
			if (enumSym.name == objectName) {
				// Return location of the enum definition
				if (!enumSym.filePath.empty() && enumSym.sourceRange.startLine > 0) {
					std::string uri = toUri(enumSym.filePath);
					return buildLocation(uri,
										 enumSym.sourceRange.startLine,
										 enumSym.sourceRange.startCol,
										 enumSym.sourceRange.endLine,
										 enumSym.sourceRange.endCol);
				}
			}
		}

		// Try project enums
		for (const auto &enumSym : projectSymbols.enums) {
			if (enumSym.name == objectName) {
				// Return location of the enum definition
				if (!enumSym.filePath.empty() && enumSym.sourceRange.startLine > 0) {
					std::string uri = toUri(enumSym.filePath);
					return buildLocation(uri,
										 enumSym.sourceRange.startLine,
										 enumSym.sourceRange.startCol,
										 enumSym.sourceRange.endLine,
										 enumSym.sourceRange.endCol);
				}
			}
		}

		// If we couldn't resolve the field, fall through to regular lookup
		// (the member name might be the actual symbol we want to look up)
		symbol = memberName;
	}

	// Try lookups in order of specificity

	// 1. Try parameter lookup (parameters in current function)
	auto paramLoc = lookupParameter(symbol, cache, document, position);
	if (paramLoc) {
		return *paramLoc;
	}

	// 2. Try variable lookup (local variables)
	auto varLoc = lookupVariable(symbol, cache, document, position);
	if (varLoc) {
		return *varLoc;
	}

	// 3. Try function lookup
	auto funcLoc = lookupFunction(symbol, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
	if (funcLoc) {
		return *funcLoc;
	}

	// 4. Try type lookup (struct, enum, interface)
	auto typeLoc = lookupType(symbol, cache, stdlib, projectSymbols, workspaceRoot, document.uri);
	if (typeLoc) {
		return *typeLoc;
	}

	return std::nullopt;
}

std::string DefinitionProvider::getSymbolAtPosition(const Document &document, const Position &position) {
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

	// Store the symbol range for later use
	currentSymbolRange_ = Range(position.line, start, position.line, end);

	return token;
}

Range DefinitionProvider::getSymbolRange(const Document &document, const Position &position) {
	// This is called after getSymbolAtPosition, which sets currentSymbolRange_
	(void)document;
	(void)position;
	return currentSymbolRange_;
}

bool DefinitionProvider::isMemberAccess(const Document &document, const Position &position,
										std::string &objectName, std::string &memberName, bool &cursorOnObject) {
	if (position.line < 0 || position.line >= document.getLineCount()) {
		return false;
	}

	std::string line = document.getLine(position.line);
	if (line.empty() || position.character < 0) {
		return false;
	}

	int pos = position.character;
	if (pos >= static_cast<int>(line.size())) {
		pos = static_cast<int>(line.size()) - 1;
		if (pos < 0) {
			return false;
		}
	}

	auto isIdentChar = [](char c) {
		return (c >= 'a' && c <= 'z') ||
			   (c >= 'A' && c <= 'Z') ||
			   (c >= '0' && c <= '9') ||
			   c == '_';
	};

	// Find bounds of current token
	int tokenStart = pos;
	while (tokenStart > 0 && isIdentChar(line[tokenStart - 1])) {
		tokenStart--;
	}

	int tokenEnd = pos;
	while (tokenEnd < static_cast<int>(line.size()) && isIdentChar(line[tokenEnd])) {
		tokenEnd++;
	}

	if (tokenStart == tokenEnd) {
		return false;
	}

	// Check if there's a dot before the current token
	if (tokenStart > 0 && line[tokenStart - 1] == '.') {
		// Find the object name before the dot
		int dotPos = tokenStart - 1;
		int objEnd = dotPos;
		int objStart = objEnd;

		while (objStart > 0 && isIdentChar(line[objStart - 1])) {
			objStart--;
		}

		if (objStart < objEnd) {
			objectName = line.substr(objStart, objEnd - objStart);
			memberName = line.substr(tokenStart, tokenEnd - tokenStart);
			cursorOnObject = false; // Cursor is on the member part (after dot)
			return true;
		}
	}

	// Check if there's a dot after the current token and cursor is on the object
	if (tokenEnd < static_cast<int>(line.size()) && line[tokenEnd] == '.') {
		int memberStart = tokenEnd + 1;
		int memberEnd = memberStart;

		while (memberEnd < static_cast<int>(line.size()) && isIdentChar(line[memberEnd])) {
			memberEnd++;
		}

		if (memberStart < memberEnd) {
			objectName = line.substr(tokenStart, tokenEnd - tokenStart);
			memberName = line.substr(memberStart, memberEnd - memberStart);
			cursorOnObject = true; // Cursor is on the object part (before dot)
			return true;
		}
	}

	return false;
}

std::optional<Location> DefinitionProvider::lookupVariable(const std::string &name,
														   const AnalysisCache *cache,
														   const Document &document,
														   const Position &position) {
	if (!cache) {
		return std::nullopt;
	}

	auto varPtr = cache->findVariable(name, position.line);
	if (varPtr) {
		// Skip parameters - they are handled by lookupParameter
		if (varPtr->isParameter) {
			return std::nullopt;
		}

		// Build location in the current document
		// Compiler uses 1-based line/column, LSP uses 0-based
		return buildLocation(document.uri, varPtr->line, varPtr->column);
	}

	return std::nullopt;
}

std::optional<Location> DefinitionProvider::lookupFunction(const std::string &name,
														   const AnalysisCache *cache,
														   const StdlibSymbols &stdlib,
														   const StdlibSymbols &projectSymbols,
														   const std::string &workspaceRoot,
														   const std::string &documentUri) {
	(void)workspaceRoot;

	// First check in symbols from cache (includes source range and file path)
	if (cache) {
		for (const auto &symbol : cache->symbols) {
			if (symbol.name == name && symbol.kind == "function") {
				std::string uri = symbol.filePath.empty() ? documentUri : toUri(symbol.filePath);
				if (uri.empty()) {
					continue;
				}
				return buildLocation(uri,
									 symbol.sourceRange.startLine,
									 symbol.sourceRange.startCol,
									 symbol.sourceRange.endLine,
									 symbol.sourceRange.endCol);
			}
		}

		// Check in functions map (for local definitions without file path in symbols)
		auto it = cache->functions.find(name);
		if (it != cache->functions.end()) {
			const FunctionInfo &func = it->second;
			if (func.line > 0) {
				// Use the current document URI for local functions
				return buildLocation(documentUri, func.line, func.column);
			}
		}
	}

	// Check in stdlib functions
	for (const auto &func : stdlib.functions) {
		if (func.name == name) {
			if (!func.filePath.empty() && func.sourceRange.startLine > 0) {
				std::string uri = toUri(func.filePath);
				return buildLocation(uri,
									 func.sourceRange.startLine,
									 func.sourceRange.startCol,
									 func.sourceRange.endLine,
									 func.sourceRange.endCol);
			}
		}
	}

	// Check in project functions
	for (const auto &func : projectSymbols.functions) {
		if (func.name == name) {
			if (!func.filePath.empty() && func.sourceRange.startLine > 0) {
				std::string uri = toUri(func.filePath);
				return buildLocation(uri,
									 func.sourceRange.startLine,
									 func.sourceRange.startCol,
									 func.sourceRange.endLine,
									 func.sourceRange.endCol);
			}
		}
	}

	return std::nullopt;
}

std::optional<Location> DefinitionProvider::lookupType(const std::string &name,
													   const AnalysisCache *cache,
													   const StdlibSymbols &stdlib,
													   const StdlibSymbols &projectSymbols,
													   const std::string &workspaceRoot,
													   const std::string &documentUri) {
	(void)workspaceRoot;

	// Check in symbols from cache first (includes source range and file path)
	if (cache) {
		for (const auto &symbol : cache->symbols) {
			if (symbol.name == name && (symbol.kind == "struct" || symbol.kind == "enum" || symbol.kind == "interface")) {
				std::string uri = symbol.filePath.empty() ? documentUri : toUri(symbol.filePath);
				if (!uri.empty()) {
					return buildLocation(uri,
										 symbol.sourceRange.startLine,
										 symbol.sourceRange.startCol,
										 symbol.sourceRange.endLine,
										 symbol.sourceRange.endCol);
				}
			}
		}

		// Check structs in cache (for local definitions without symbol entry)
		auto structIt = cache->structs.find(name);
		if (structIt != cache->structs.end()) {
			const StructInfo &structInfo = structIt->second;
			if (structInfo.line > 0) {
				return buildLocation(documentUri, structInfo.line, structInfo.column);
			}
		}

		// Check interfaces in cache
		auto ifaceIt = cache->interfaces.find(name);
		if (ifaceIt != cache->interfaces.end()) {
			const InterfaceInfo &ifaceInfo = ifaceIt->second;
			if (ifaceInfo.line > 0) {
				return buildLocation(documentUri, ifaceInfo.line, ifaceInfo.column);
			}
		}
	}

	// Check stdlib structs
	for (const auto &structSym : stdlib.structs) {
		if (structSym.name == name) {
			if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
				std::string uri = toUri(structSym.filePath);
				return buildLocation(uri,
									 structSym.sourceRange.startLine,
									 structSym.sourceRange.startCol,
									 structSym.sourceRange.endLine,
									 structSym.sourceRange.endCol);
			}
		}
	}

	// Check stdlib enums
	for (const auto &enumSym : stdlib.enums) {
		if (enumSym.name == name) {
			if (!enumSym.filePath.empty() && enumSym.sourceRange.startLine > 0) {
				std::string uri = toUri(enumSym.filePath);
				return buildLocation(uri,
									 enumSym.sourceRange.startLine,
									 enumSym.sourceRange.startCol,
									 enumSym.sourceRange.endLine,
									 enumSym.sourceRange.endCol);
			}
		}
	}

	// Check stdlib interfaces
	for (const auto &ifaceSym : stdlib.interfaces) {
		if (ifaceSym.name == name) {
			if (!ifaceSym.filePath.empty() && ifaceSym.sourceRange.startLine > 0) {
				std::string uri = toUri(ifaceSym.filePath);
				return buildLocation(uri,
									 ifaceSym.sourceRange.startLine,
									 ifaceSym.sourceRange.startCol,
									 ifaceSym.sourceRange.endLine,
									 ifaceSym.sourceRange.endCol);
			}
		}
	}

	// Check project structs
	for (const auto &structSym : projectSymbols.structs) {
		if (structSym.name == name) {
			if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
				std::string uri = toUri(structSym.filePath);
				return buildLocation(uri,
									 structSym.sourceRange.startLine,
									 structSym.sourceRange.startCol,
									 structSym.sourceRange.endLine,
									 structSym.sourceRange.endCol);
			}
		}
	}

	// Check project enums
	for (const auto &enumSym : projectSymbols.enums) {
		if (enumSym.name == name) {
			if (!enumSym.filePath.empty() && enumSym.sourceRange.startLine > 0) {
				std::string uri = toUri(enumSym.filePath);
				return buildLocation(uri,
									 enumSym.sourceRange.startLine,
									 enumSym.sourceRange.startCol,
									 enumSym.sourceRange.endLine,
									 enumSym.sourceRange.endCol);
			}
		}
	}

	// Check project interfaces
	for (const auto &ifaceSym : projectSymbols.interfaces) {
		if (ifaceSym.name == name) {
			if (!ifaceSym.filePath.empty() && ifaceSym.sourceRange.startLine > 0) {
				std::string uri = toUri(ifaceSym.filePath);
				return buildLocation(uri,
									 ifaceSym.sourceRange.startLine,
									 ifaceSym.sourceRange.startCol,
									 ifaceSym.sourceRange.endLine,
									 ifaceSym.sourceRange.endCol);
			}
		}
	}

	return std::nullopt;
}

std::optional<Location> DefinitionProvider::lookupField(const std::string &structName,
														const std::string &fieldName,
														const AnalysisCache *cache,
														const StdlibSymbols &stdlib,
														const StdlibSymbols &projectSymbols,
														const std::string &workspaceRoot,
														const std::string &documentUri) {
	(void)workspaceRoot;

	// Check structs in cache
	if (cache) {
		auto structIt = cache->structs.find(structName);
		if (structIt != cache->structs.end()) {
			const StructInfo &structInfo = structIt->second;
			for (const auto &field : structInfo.fields) {
				if (field.name == fieldName) {
					// Find the struct's file path in symbols, or use current document
					std::string uri = documentUri;
					for (const auto &symbol : cache->symbols) {
						if (symbol.name == structName && symbol.kind == "struct") {
							if (!symbol.filePath.empty()) {
								uri = toUri(symbol.filePath);
							}
							break;
						}
					}

					if (!uri.empty() && field.line > 0) {
						return buildLocation(uri, field.line, field.column);
					}
				}
			}
		}
	}

	// Check stdlib structs - we don't have detailed field info in stdlib symbols
	// but we can at least point to the struct definition
	for (const auto &structSym : stdlib.structs) {
		if (structSym.name == structName) {
			if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
				// We don't have field-level source ranges in stdlib, so point to the struct
				std::string uri = toUri(structSym.filePath);
				return buildLocation(uri,
									 structSym.sourceRange.startLine,
									 structSym.sourceRange.startCol,
									 structSym.sourceRange.endLine,
									 structSym.sourceRange.endCol);
			}
		}
	}

	// Check project structs - we don't have detailed field info in project symbols
	// but we can at least point to the struct definition
	for (const auto &structSym : projectSymbols.structs) {
		if (structSym.name == structName) {
			if (!structSym.filePath.empty() && structSym.sourceRange.startLine > 0) {
				std::string uri = toUri(structSym.filePath);
				return buildLocation(uri,
									 structSym.sourceRange.startLine,
									 structSym.sourceRange.startCol,
									 structSym.sourceRange.endLine,
									 structSym.sourceRange.endCol);
			}
		}
	}

	return std::nullopt;
}

std::optional<Location> DefinitionProvider::lookupParameter(const std::string &name,
															const AnalysisCache *cache,
															const Document &document,
															const Position &position) {
	if (!cache) {
		return std::nullopt;
	}

	// Check if this name is a parameter in the variables map (use qualified lookup)
	auto varPtr = cache->findVariable(name, position.line);
	if (!varPtr || !varPtr->isParameter) {
		return std::nullopt;
	}

	// The parameter's line/column points to its definition in the function signature
	return buildLocation(document.uri, varPtr->line, varPtr->column);
}

Location DefinitionProvider::buildLocation(const std::string &uri, int line, int column,
										   int endLine, int endColumn) {
	Location loc;
	loc.uri = uri;

	// Convert from 1-based (compiler) to 0-based (LSP)
	int startLine = line > 0 ? line - 1 : 0;
	int startCol = column > 0 ? column - 1 : 0;

	// Handle end position
	int endLn, endCol_adj;
	if (endLine > 0 && endColumn > 0) {
		endLn = endLine - 1;
		endCol_adj = endColumn - 1;
	} else {
		// If no end position, make a small range at the start
		endLn = startLine;
		endCol_adj = startCol + 1;
	}

	loc.range = Range(startLine, startCol, endLn, endCol_adj);
	return loc;
}

std::string DefinitionProvider::toUri(const std::string &path) {
	// Use the utility function from document_manager
	return pathToUri(path);
}

} // namespace maxon_lsp
