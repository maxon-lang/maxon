#include "analyzer.h"
#include "intrinsics.h"
#include "semantic_analyzer.h"
#include "type_members.h"
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

namespace fs = std::filesystem;

Analyzer::Analyzer() {
	// Get keywords from the lexer - this ensures we always have the latest keywords
	keywords = Lexer::getKeywords();

	// Get keyword metadata for hover info
	auto keywordInfo = Lexer::getKeywordInfo();
	for (const auto &info : keywordInfo) {
		keywordMetadata[info.name] = info;
	}

	// Initialize array type members from shared type registry
	// This ensures compiler and LSP use the same definitions
	for (const auto &member : getArrayTypeMembers()) {
		typeMembers["[]"].push_back({member.name, member.isMethod, member.returnType, member.signature, member.documentation});
	}
}

std::vector<lsp::Diagnostic> Analyzer::analyze(std::shared_ptr<Document> doc) {
	std::vector<lsp::Diagnostic> diagnostics;

	try {
		std::vector<Token> tokens = tokenize(doc->text);

		// Check for lexer errors (UNKNOWN tokens)
		for (const auto &token : tokens) {
			if (token.type == TokenType::UNKNOWN) {
				lsp::Diagnostic diag;
				diag.range = tokenToRange(token);
				diag.message = "Unknown token: " + token.value;
				diag.severity = 1; // Error
				diag.source = "maxon";
				diagnostics.push_back(diag);
			}
		}

		// Try to parse
		try {
			Parser parser(tokens);
			auto program = parser.parse();

			// Run semantic analysis
			SemanticAnalyzer semanticAnalyzer;

			// Register all built-in functions (runtime functions, string methods, etc.)
			semanticAnalyzer.registerBuiltinFunctions();

			// Get the set of function names defined in the current document
			// Include both top-level functions and struct methods (qualified as StructName.methodName)
			std::set<std::string> currentDocFunctions;
			for (const auto &func : program->functions) {
				currentDocFunctions.insert(func->name);
			}
			// Also collect struct methods from the current document
			for (const auto &structDef : program->structs) {
				for (const auto &method : structDef->methods) {
					std::string qualifiedName = structDef->name + "." + method->name;
					currentDocFunctions.insert(qualifiedName);
				}
			}

			// Register stdlib functions with the semantic analyzer,
			// but SKIP functions that are defined in the current document
			// to avoid duplicate definition errors when editing stdlib files
			for (const auto &pair : stdlibFunctions) {
				const StdlibFunction &func = pair.second;
				// Only register as external if NOT defined in current document
				if (currentDocFunctions.find(func.name) == currentDocFunctions.end()) {
					semanticAnalyzer.registerExternalFunction(func.name, func.returnType, func.parameters);
				}
			}

			// Register stdlib struct methods with the semantic analyzer
			// Use qualified names (StructName.methodName) for proper method resolution
			for (const auto &method : stdlibStructMethods) {
				std::string qualifiedName = method.structName + "." + method.methodName;
				// Only register if NOT defined in current document
				if (currentDocFunctions.find(qualifiedName) == currentDocFunctions.end()) {
					semanticAnalyzer.registerExternalFunction(qualifiedName, method.returnType, method.parameters);
				}
			}

			// Get the set of struct names defined in the current document
			std::set<std::string> currentDocStructs;
			for (const auto &structDef : program->structs) {
				currentDocStructs.insert(structDef->name);
			}

			// Register stdlib structs with the semantic analyzer
			// This allows files to reference structs defined in other stdlib files
			for (const auto &pair : stdlibStructs) {
				const StdlibStruct &structInfo = pair.second;
				// Only register as external if NOT defined in current document
				if (currentDocStructs.find(structInfo.name) == currentDocStructs.end()) {
					semanticAnalyzer.registerExternalStruct(structInfo.name, structInfo.fields, structInfo.conformsTo, structInfo.typeAssignments);
				}
			}

			std::vector<SemanticError> semanticErrors = semanticAnalyzer.analyze(program.get());

			// Cache semantic analysis results for this document
			SemanticInfo &semInfo = semanticCache[doc->uri];
			semInfo.variables = semanticAnalyzer.getAllVariables();
			semInfo.functions = semanticAnalyzer.getFunctions();
			semInfo.structs = semanticAnalyzer.getStructs();
			semInfo.interfaces = semanticAnalyzer.getInterfaces();

			// Convert semantic errors to LSP diagnostics
			for (const auto &error : semanticErrors) {
				lsp::Diagnostic diag;
				diag.range.start.line = error.line > 0 ? error.line - 1 : 0;
				diag.range.start.character = error.column > 0 ? error.column - 1 : 0;
				diag.range.end.line = error.line > 0 ? error.line - 1 : 0;
				diag.range.end.character = error.column > 0 ? error.column : 1;
				diag.message = error.message;
				diag.severity = error.severity; // Use severity from semantic error (1 = Error, 2 = Warning)
				diag.source = "maxon";
				if (!error.code.empty()) {
					diag.code = error.code;
				}
				diagnostics.push_back(diag);
			}

		} catch (const std::exception &e) {
			lsp::Diagnostic diag;

			// Try to extract line and column from error message
			// Parser errors have format: "...Location: line X, column Y"
			std::string errorMsg = e.what();
			int errorLine = 0;
			int errorColumn = 0;

			size_t locPos = errorMsg.find("Location: line ");
			if (locPos != std::string::npos) {
				size_t lineStart = locPos + 15; // Length of "Location: line "
				size_t lineEnd = errorMsg.find(',', lineStart);
				if (lineEnd != std::string::npos) {
					std::string lineStr = errorMsg.substr(lineStart, lineEnd - lineStart);
					errorLine = std::stoi(lineStr);

					size_t colPos = errorMsg.find("column ", lineEnd);
					if (colPos != std::string::npos) {
						size_t colStart = colPos + 7; // Length of "column "
						size_t colEnd = errorMsg.find_first_not_of("0123456789", colStart);
						if (colEnd == std::string::npos) {
							colEnd = errorMsg.length();
						}
						std::string colStr = errorMsg.substr(colStart, colEnd - colStart);
						errorColumn = std::stoi(colStr);
					}
				}
			}

			diag.range.start.line = errorLine > 0 ? errorLine - 1 : 0;
			diag.range.start.character = errorColumn > 0 ? errorColumn - 1 : 0;
			diag.range.end.line = errorLine > 0 ? errorLine - 1 : 0;
			diag.range.end.character = errorColumn > 0 ? errorColumn : 1;
			diag.message = std::string("Parse error: ") + errorMsg;
			diag.severity = 1; // Error
			diag.source = "maxon";
			diagnostics.push_back(diag);
		}

	} catch (const std::exception &e) {
		lsp::Diagnostic diag;
		diag.range.start.line = 0;
		diag.range.start.character = 0;
		diag.range.end.line = 0;
		diag.range.end.character = 1;
		diag.message = std::string("Lexer error: ") + e.what();
		diag.severity = 1; // Error
		diag.source = "maxon";
		diagnostics.push_back(diag);
	}

	return diagnostics;
}

std::vector<lsp::CompletionItem> Analyzer::getCompletions(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::vector<lsp::CompletionItem> items;

	// Get text before cursor to check for qualified name context
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);

	// Check if we're in a qualified name context (e.g., "stdlib.", "stdlib.fmt.", etc.)
	// Look for pattern: word followed by dots
	size_t lastDot = textBeforeCursor.find_last_of('.');

	if (lastDot != std::string::npos) {
		// We're after a dot - could be qualified name (stdlib.fmt) or member access (arr.length)
		// Extract the prefix before the dot (e.g., "stdlib" from "stdlib." or "arr" from "arr.")
		size_t start = lastDot;
		while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
							 textBeforeCursor[start - 1] == '_' || textBeforeCursor[start - 1] == '.')) {
			start--;
		}
		std::string prefix = textBeforeCursor.substr(start, lastDot - start);

		// Check if prefix is a variable name with a known type
		// Extract just the identifier (last part before the dot)
		size_t lastDotInPrefix = prefix.find_last_of('.');
		std::string identifierName = (lastDotInPrefix != std::string::npos)
										 ? prefix.substr(lastDotInPrefix + 1)
										 : prefix;

		// Look up the identifier in semantic cache
		auto cacheIt = semanticCache.find(doc->uri);
		if (cacheIt != semanticCache.end()) {
			const SemanticInfo &semInfo = cacheIt->second;
			auto varIt = semInfo.variables.find(identifierName);

			if (varIt != semInfo.variables.end()) {
				const VariableInfo &varInfo = varIt->second;
				// Get member completions for this type (handles arrays, structs, etc.)
				return getMemberCompletions(varInfo.type, semInfo);
			}
		}

		// Not a known variable, try qualified name completions (stdlib.fmt, etc.)
		return getQualifiedNameCompletions(prefix);
	}

	// No dot context - provide regular completions

	// Add "stdlib" as a root namespace option
	items.push_back({"stdlib", lsp::CompletionItemKind::Module, "Standard library namespace", ""});

	// Add keyword completions with metadata
	for (const auto &keyword : keywords) {
		auto metaIt = keywordMetadata.find(keyword);
		lsp::CompletionItem item;
		item.label = keyword;

		// Set appropriate kind based on category
		if (metaIt != keywordMetadata.end()) {
			const auto &meta = metaIt->second;
			switch (meta.category) {
			case KeywordCategory::Type:
				item.kind = lsp::CompletionItemKind::TypeParameter;
				break;
			case KeywordCategory::MathIntrinsic:
				item.kind = lsp::CompletionItemKind::Function;
				break;
			default:
				item.kind = lsp::CompletionItemKind::Keyword;
				break;
			}
			item.detail = meta.description;
		} else {
			item.kind = lsp::CompletionItemKind::Keyword;
			item.detail = "Maxon keyword";
		}

		items.push_back(item);
	}

	// Add stdlib function completions
	for (const auto &pair : stdlibFunctions) {
		const StdlibFunction &func = pair.second;
		lsp::CompletionItem item;
		item.label = func.name;
		item.kind = lsp::CompletionItemKind::Function;
		item.detail = func.signature;
		item.documentation = func.documentation;
		items.push_back(item);
	}

	// Try to extract identifiers from document for variable/function completions
	try {
		std::vector<Token> tokens = tokenize(doc->text);

		std::set<std::string> identifiers;
		for (const auto &token : tokens) {
			if (token.type == TokenType::IDENTIFIER) {
				identifiers.insert(token.value);
			}
		}

		for (const auto &id : identifiers) {
			lsp::CompletionItem item;
			item.label = id;
			item.kind = lsp::CompletionItemKind::Variable;
			item.detail = "Identifier";
			items.push_back(item);
		}

	} catch (...) {
		// If lexing fails, just return keywords
	}

	return items;
}

// Helper function to check if position is inside a string or character literal
// Returns: 0 = not in literal, 1 = in string literal, 2 = in character literal
static int getLiteralTypeAtPosition(const std::string &text, lsp::Position pos) {
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	while (std::getline(stream, line) && currentLine < pos.line) {
		currentLine++;
	}

	if (currentLine != pos.line || pos.character >= (int)line.length()) {
		return 0;
	}

	// Scan the line from the beginning to check if we're inside a literal
	bool inString = false;
	bool inChar = false;
	int literalStart = -1;

	for (int i = 0; i < (int)line.length(); i++) {
		char c = line[i];
		char prev = (i > 0) ? line[i - 1] : '\0';

		// Handle escape sequences (skip escaped quotes)
		if (prev == '\\') {
			continue;
		}

		if (c == '"' && !inChar) {
			if (!inString) {
				inString = true;
				literalStart = i;
			} else {
				inString = false;
				// Check if cursor was in this string
				if (pos.character > literalStart && pos.character <= i) {
					return 1; // String literal
				}
			}
		} else if (c == '\'' && !inString) {
			if (!inChar) {
				inChar = true;
				literalStart = i;
			} else {
				inChar = false;
				// Check if cursor was in this char
				if (pos.character > literalStart && pos.character <= i) {
					return 2; // Character literal
				}
			}
		}
	}

	// If still in a literal at cursor position
	if (inString && pos.character > literalStart) {
		return 1;
	}
	if (inChar && pos.character > literalStart) {
		return 2;
	}

	return 0;
}

// Returns the type of numeric literal at the given position:
// 0 = not a number, 1 = integer, 2 = float, 3 = byte literal
static int getNumericLiteralType(const std::string &text, lsp::Position pos) {
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	while (std::getline(stream, line) && currentLine < pos.line) {
		currentLine++;
	}

	if (currentLine != pos.line || pos.character >= (int)line.length()) {
		return 0;
	}

	// First check if the character at the cursor is even a digit
	char cursorChar = line[pos.character];
	if (!std::isdigit(cursorChar)) {
		return 0;
	}

	// Find the word boundaries at cursor position
	int start = pos.character;
	int end = pos.character;

	// Expand to include digits, dots, and 'b' suffix
	while (start > 0 && (std::isdigit(line[start - 1]) || line[start - 1] == '.')) {
		start--;
	}

	while (end < (int)line.length() && (std::isdigit(line[end]) || line[end] == '.' || line[end] == 'b')) {
		end++;
	}

	// Check if this digit is part of an identifier (e.g., "c2", "var2name")
	// An identifier can have letters/underscores before or after
	if (start > 0 && (std::isalpha(line[start - 1]) || line[start - 1] == '_')) {
		return 0; // It's part of an identifier, not a numeric literal
	}
	if (end < (int)line.length() && (std::isalpha(line[end]) || line[end] == '_')) {
		return 0; // It's part of an identifier, not a numeric literal
	}

	if (start >= end) {
		return 0;
	}

	std::string literal = line.substr(start, end - start);

	// Check if this is a valid numeric literal
	if (literal.empty() || !std::isdigit(literal[0])) {
		return 0;
	}

	// Check for byte literal (ends with 'b')
	if (literal.size() > 1 && literal.back() == 'b') {
		return 3;
	}

	// Check for float (contains '.')
	if (literal.find('.') != std::string::npos) {
		return 2;
	}

	// Integer literal
	return 1;
}

std::optional<lsp::Hover> Analyzer::getHover(std::shared_ptr<Document> doc, lsp::Position pos) {
	// Check if cursor is inside a string or character literal first
	int literalType = getLiteralTypeAtPosition(doc->text, pos);
	if (literalType == 1) {
		lsp::Hover hover;
		hover.contents = "```maxon\nstring\n```\n\nString literal";
		return hover;
	} else if (literalType == 2) {
		lsp::Hover hover;
		hover.contents = "```maxon\nchar\n```\n\nCharacter literal";
		return hover;
	}

	// Check if cursor is on a numeric literal
	int numericType = getNumericLiteralType(doc->text, pos);
	if (numericType == 1) {
		lsp::Hover hover;
		hover.contents = "```maxon\nint\n```\n\nInteger literal";
		return hover;
	} else if (numericType == 2) {
		lsp::Hover hover;
		hover.contents = "```maxon\nfloat\n```\n\nFloating-point literal";
		return hover;
	} else if (numericType == 3) {
		lsp::Hover hover;
		hover.contents = "```maxon\nbyte\n```\n\nByte literal";
		return hover;
	}

	std::string word = getWordAtPosition(doc->text, pos);

	if (word.empty()) {
		return std::nullopt;
	}

	lsp::Hover hover;

	// Check if this is a field name in a struct literal (e.g., "value" in "Counter{value: 10}")
	// Pattern: StructName{..., fieldName: ...}
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);

	// Check if we're inside a struct literal by looking for pattern: Identifier{...fieldName
	// where fieldName is followed by a colon
	std::string fullLine;
	{
		std::istringstream stream(doc->text);
		std::string line;
		int currentLine = 0;
		while (std::getline(stream, line) && currentLine < pos.line) {
			currentLine++;
		}
		if (currentLine == pos.line) {
			fullLine = line;
		}
	}

	// Check if word is followed by a colon (indicating it's a field name in struct literal)
	size_t wordEnd = pos.character;
	while (wordEnd < fullLine.length() && (std::isalnum(fullLine[wordEnd]) || fullLine[wordEnd] == '_')) {
		wordEnd++;
	}
	// Skip whitespace after the word
	size_t colonPos = wordEnd;
	while (colonPos < fullLine.length() && std::isspace(fullLine[colonPos])) {
		colonPos++;
	}

	if (colonPos < fullLine.length() && fullLine[colonPos] == ':') {
		// This word is followed by a colon - likely a field name in struct literal
		// Now find the struct name by looking backwards for "StructName{"
		// Find the last '{' before the cursor
		size_t bracePos = textBeforeCursor.find_last_of('{');
		if (bracePos != std::string::npos && bracePos > 0) {
			// Extract struct name before the brace
			size_t structEnd = bracePos;
			size_t structStart = structEnd - 1;
			while (structStart > 0 && (std::isalnum(textBeforeCursor[structStart - 1]) || textBeforeCursor[structStart - 1] == '_')) {
				structStart--;
			}
			std::string structName = textBeforeCursor.substr(structStart, structEnd - structStart);

			if (!structName.empty()) {
				// Look up the struct in semantic cache
				auto cacheIt = semanticCache.find(doc->uri);
				if (cacheIt != semanticCache.end()) {
					const SemanticInfo &semInfo = cacheIt->second;
					auto structIt = semInfo.structs.find(structName);
					if (structIt != semInfo.structs.end()) {
						// Find the field
						for (const auto &field : structIt->second.fields) {
							if (field.name == word) {
								std::string mutability = field.isImmutable ? "let" : "var";
								std::string fieldDisplay = mutability + " " + field.name + ": " + field.type;
								if (field.isImmutable && !field.defaultValue.empty()) {
									fieldDisplay += " = " + field.defaultValue;
								}
								hover.contents = "```maxon\n(field) " + fieldDisplay + "\n```\n\nField of struct `" + structName + "`";
								return hover;
							}
						}
					}
				}
			}
		}
	}

	// Check if this is a member access (e.g., hovering over "capacity" in "arr.capacity")
	size_t lastDot = textBeforeCursor.find_last_of('.');

	if (lastDot != std::string::npos) {
		// Check if the word we're hovering over is right after the dot
		// Extract text between the dot and cursor
		std::string afterDot = textBeforeCursor.substr(lastDot + 1);

		// If afterDot matches the word (or is a prefix), this is likely member access
		if (word.find(afterDot) == 0 || afterDot.find(word) == 0) {
			// Extract the object name before the dot
			size_t start = lastDot;
			while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
								 textBeforeCursor[start - 1] == '_')) {
				start--;
			}
			std::string objectName = textBeforeCursor.substr(start, lastDot - start);

			// Look up the object in semantic cache
			auto cacheIt = semanticCache.find(doc->uri);
			if (cacheIt != semanticCache.end()) {
				const SemanticInfo &semInfo = cacheIt->second;
				auto varIt = semInfo.variables.find(objectName);

				if (varIt != semInfo.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check if it's a struct type
					auto structIt = semInfo.structs.find(typeName);
					if (structIt != semInfo.structs.end()) {
						// Find the field
						for (const auto &field : structIt->second.fields) {
							if (field.name == word) {
								std::string mutability = field.isImmutable ? "let" : "var";
								std::string fieldDisplay = mutability + " " + field.name + ": " + field.type;
								if (field.isImmutable && !field.defaultValue.empty()) {
									fieldDisplay += " = " + field.defaultValue;
								}
								hover.contents = "```maxon\n(field) " + fieldDisplay + "\n```\n\nField of struct `" + typeName + "`";
								return hover;
							}
						}
					}

					// Check if it's an array type
					if (!typeName.empty() && typeName[0] == '[') {
						if (word == "length") {
							hover.contents = "```maxon\n(property) length: int\n```\n\nNumber of elements in the array";
							return hover;
						} else if (word == "capacity") {
							hover.contents = "```maxon\n(property) capacity: int\n```\n\nCapacity of the array (number of elements it can hold without reallocation)";
							return hover;
						}
					}

					// Check for methods on the type (including parameterized types like map<int,int>)
					// First check typeMembers registry for stdlib types
					auto typeIt = typeMembers.find(typeName);
					if (typeIt != typeMembers.end()) {
						for (const auto &member : typeIt->second) {
							if (member.name == word && member.isMethod) {
								hover.contents = "```maxon\n(method) " + word + member.signature + " " + member.returnType + "\n```\n\n" + member.documentation;
								return hover;
							}
						}
					}

					// For parameterized types like map<K,V>, check the base type (e.g., "map")
					size_t anglePos = typeName.find('<');
					if (anglePos != std::string::npos) {
						std::string baseType = typeName.substr(0, anglePos);
						auto baseTypeIt = typeMembers.find(baseType);
						if (baseTypeIt != typeMembers.end()) {
							for (const auto &member : baseTypeIt->second) {
								if (member.name == word && member.isMethod) {
									hover.contents = "```maxon\n(method) " + word + member.signature + " " + member.returnType + "\n```\n\n" + member.documentation;
									return hover;
								}
							}
						}
					}

					// Check registered functions with pattern TypeName.methodName
					std::string qualifiedMethod = typeName + "." + word;
					auto funcIt = semInfo.functions.find(qualifiedMethod);
					if (funcIt != semInfo.functions.end()) {
						const FunctionInfo &funcInfo = funcIt->second;
						std::string sig = "function " + word + "(";
						bool first = true;
						for (size_t i = 0; i < funcInfo.parameters.size(); i++) {
							// Skip implicit 'self' parameter for methods
							if (funcInfo.parameters[i].name == "self") {
								continue;
							}
							if (!first)
								sig += ", ";
							sig += funcInfo.parameters[i].name + " " + funcInfo.parameters[i].type;
							first = false;
						}
						sig += ") " + funcInfo.returnType;
						hover.contents = "```maxon\n" + sig + "\n```\n\nMethod of `" + typeName + "`";
						return hover;
					}
				}
			}
		}
	}

	// Not a member access, check cached semantic info for this document
	auto cacheIt = semanticCache.find(doc->uri);
	if (cacheIt != semanticCache.end()) {
		const SemanticInfo &semInfo = cacheIt->second;

		// Check for variable
		auto varIt = semInfo.variables.find(word);
		if (varIt != semInfo.variables.end()) {
			const VariableInfo &varInfo = varIt->second;
			std::string mutability = varInfo.isImmutable ? "let" : "var";
			std::string hoverText = mutability + " " + varInfo.name + ": " + varInfo.type;
			if (varInfo.isImmutable && !varInfo.initialValue.empty()) {
				hoverText += " = " + varInfo.initialValue;
			}
			hover.contents = "```maxon\n" + hoverText + "\n```";
			if (varInfo.isParameter) {
				hover.contents += "\n\nFunction parameter";
			}
			return hover;
		}

		// Check if we're inside a struct method and word is a field (implicit self access)
		// Look for struct context by scanning the source text
		std::string containingStruct = findContainingStruct(doc->text, pos);
		if (!containingStruct.empty()) {
			auto structIt = semInfo.structs.find(containingStruct);
			if (structIt != semInfo.structs.end()) {
				for (const auto &field : structIt->second.fields) {
					if (field.name == word) {
						std::string mutability = field.isImmutable ? "let" : "var";
						std::string fieldDisplay = mutability + " " + field.name + ": " + field.type;
						if (field.isImmutable && !field.defaultValue.empty()) {
							fieldDisplay += " = " + field.defaultValue;
						}
						hover.contents = "```maxon\n(field) " + fieldDisplay + "\n```\n\nField of struct `" + containingStruct + "`";
						return hover;
					}
				}
			}
		}

		// Check for function (user-defined)
		auto funcIt = semInfo.functions.find(word);
		if (funcIt != semInfo.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;
			std::string sig = "function " + funcInfo.name + "(";
			bool first = true;
			for (size_t i = 0; i < funcInfo.parameters.size(); i++) {
				// Skip implicit 'self' parameter for methods
				if (funcInfo.parameters[i].name == "self") {
					continue;
				}
				if (!first)
					sig += ", ";
				sig += funcInfo.parameters[i].name + " " + funcInfo.parameters[i].type;
				first = false;
			}
			sig += ") " + funcInfo.returnType;
			hover.contents = "```maxon\n" + sig + "\n```";
			return hover;
		}

		// Check for struct
		auto structIt = semInfo.structs.find(word);
		if (structIt != semInfo.structs.end()) {
			const StructInfo &structInfo = structIt->second;
			std::string structDef = "struct " + structInfo.name + "\n";
			for (const auto &field : structInfo.fields) {
				structDef += "    " + field.name + " " + field.type + "\n";
			}
			structDef += "end";
			hover.contents = "```maxon\n" + structDef + "\n```";
			return hover;
		}
	}

	// Check if it's a stdlib function
	auto it = stdlibFunctions.find(word);
	if (it != stdlibFunctions.end()) {
		const StdlibFunction &func = it->second;
		hover.contents = "**" + func.qualifiedName + "**\n\n```maxon\n" +
						 func.signature + "\n```\n\n" + func.documentation;
		return hover;
	}

	// Check if it's a keyword with metadata
	auto metaIt = keywordMetadata.find(word);
	if (metaIt != keywordMetadata.end()) {
		const auto &meta = metaIt->second;
		std::string categoryName;
		switch (meta.category) {
		case KeywordCategory::Type:
			categoryName = "type";
			break;
		case KeywordCategory::ControlFlow:
			categoryName = "control flow";
			break;
		case KeywordCategory::Declaration:
			categoryName = "declaration";
			break;
		case KeywordCategory::MathIntrinsic: {
			const MathIntrinsicInfo *mathInfo = Lexer::getMathIntrinsicInfo(word);
			if (mathInfo) {
				std::string sig = "function " + word + "(x float) " + mathInfo->returnType;
				hover.contents = "```maxon\n" + sig + "\n```\n\n(math intrinsic) " + meta.description;
				return hover;
			}
			categoryName = "math intrinsic";
			break;
		}
		case KeywordCategory::Literal:
			categoryName = "literal";
			break;
		case KeywordCategory::Operator:
			categoryName = "operator";
			break;
		}
		hover.contents = "**" + word + "** (" + categoryName + ")\n\n" + meta.description;
	} else {
		// Check if it's a compiler intrinsic
		const IntrinsicInfo *intrinsic = IntrinsicRegistry::instance().lookup(word);
		if (intrinsic) {
			std::string sig = "function " + intrinsic->name + "(";
			for (size_t i = 0; i < intrinsic->params.size(); i++) {
				if (i > 0)
					sig += ", ";
				const auto &param = intrinsic->params[i];
				if (!param.allowedTypes.empty()) {
					// Show allowed types for polymorphic parameters
					if (param.isArrayType) {
						sig += "[]" + param.allowedTypes[0];
					} else if (param.allowedTypes.size() == 1) {
						sig += param.allowedTypes[0];
					} else {
						sig += param.allowedTypes[0];
						for (size_t j = 1; j < param.allowedTypes.size(); j++) {
							sig += "|" + param.allowedTypes[j];
						}
					}
				} else {
					sig += param.type;
				}
			}
			sig += ") " + intrinsic->returnType;
			hover.contents = "```maxon\n" + sig + "\n```\n\n(compiler intrinsic)";
		} else {
			hover.contents = "**" + word + "**\n\nIdentifier";
		}
	}

	return hover;
}

std::optional<lsp::Location> Analyzer::getDefinition(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::string word = getWordAtPosition(doc->text, pos);

	if (word.empty()) {
		return std::nullopt;
	}

	// Check if this is a member access (e.g., "field" in "myStruct.field")
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);
	size_t lastDot = textBeforeCursor.find_last_of('.');

	if (lastDot != std::string::npos) {
		// Check if the word we're on is right after the dot
		std::string afterDot = textBeforeCursor.substr(lastDot + 1);

		if (word.find(afterDot) == 0 || afterDot.find(word) == 0) {
			// Extract the object name before the dot
			size_t start = lastDot;
			while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
								 textBeforeCursor[start - 1] == '_')) {
				start--;
			}
			std::string objectName = textBeforeCursor.substr(start, lastDot - start);

			// Look up the object in semantic cache to get its type
			auto cacheIt = semanticCache.find(doc->uri);
			if (cacheIt != semanticCache.end()) {
				const SemanticInfo &semInfo = cacheIt->second;
				auto varIt = semInfo.variables.find(objectName);

				if (varIt != semInfo.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check if it's a struct type and find the field
					auto structIt = semInfo.structs.find(typeName);
					if (structIt != semInfo.structs.end()) {
						for (const auto &field : structIt->second.fields) {
							if (field.name == word) {
								// Navigate to field definition within the struct
								lsp::Location loc;
								loc.uri = doc->uri;
								loc.range.start.line = field.line > 0 ? field.line - 1 : 0;
								loc.range.start.character = field.column > 0 ? field.column - 1 : 0;
								loc.range.end.line = loc.range.start.line;
								loc.range.end.character = loc.range.start.character + field.name.length();
								return loc;
							}
						}
					}

					// Check for struct methods in stdlib
					for (const auto &method : stdlibStructMethods) {
						if (method.structName == typeName && method.methodName == word) {
							if (!method.filePath.empty() && method.line > 0) {
								lsp::Location loc;
								loc.uri = "file:///" + method.filePath;
								std::replace(loc.uri.begin(), loc.uri.end(), '\\', '/');
								loc.range.start.line = method.line - 1;
								loc.range.start.character = method.column > 0 ? method.column - 1 : 0;
								loc.range.end.line = loc.range.start.line;
								loc.range.end.character = loc.range.start.character + method.methodName.length();
								return loc;
							}
						}
					}
				}
			}
		}
	}

	// Not a member access - check cached semantic info
	auto cacheIt = semanticCache.find(doc->uri);
	if (cacheIt != semanticCache.end()) {
		const SemanticInfo &semInfo = cacheIt->second;

		// Check for variable
		auto varIt = semInfo.variables.find(word);
		if (varIt != semInfo.variables.end()) {
			const VariableInfo &varInfo = varIt->second;
			lsp::Location loc;
			loc.uri = doc->uri;
			loc.range.start.line = varInfo.line > 0 ? varInfo.line - 1 : 0;
			loc.range.start.character = varInfo.column > 0 ? varInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + varInfo.name.length();
			return loc;
		}

		// Check for function
		auto funcIt = semInfo.functions.find(word);
		if (funcIt != semInfo.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;

			// Only use local semantic info if function has a valid source location
			// External/stdlib functions registered via registerExternalFunction have line=0
			if (funcInfo.line > 0) {
				// If this function implements an interface, navigate to the interface declaration
				if (!funcInfo.implementsInterface.empty()) {
					auto ifaceIt = semInfo.interfaces.find(funcInfo.implementsInterface);
					if (ifaceIt != semInfo.interfaces.end()) {
						const InterfaceInfo &ifaceInfo = ifaceIt->second;
						lsp::Location loc;
						loc.uri = doc->uri;
						loc.range.start.line = ifaceInfo.line > 0 ? ifaceInfo.line - 1 : 0;
						loc.range.start.character = ifaceInfo.column > 0 ? ifaceInfo.column - 1 : 0;
						loc.range.end.line = loc.range.start.line;
						loc.range.end.character = loc.range.start.character + ifaceInfo.name.length();
						return loc;
					}
				}

				// Navigate to the function definition in the current document
				lsp::Location loc;
				loc.uri = doc->uri;
				loc.range.start.line = funcInfo.line - 1;
				loc.range.start.character = funcInfo.column > 0 ? funcInfo.column - 1 : 0;
				loc.range.end.line = loc.range.start.line;
				loc.range.end.character = loc.range.start.character + word.length();
				return loc;
			}
			// If line is 0, fall through to check stdlibFunctions
		}

		// Check for struct type
		auto structIt = semInfo.structs.find(word);
		if (structIt != semInfo.structs.end()) {
			const StructInfo &structInfo = structIt->second;
			lsp::Location loc;
			loc.uri = doc->uri;
			loc.range.start.line = structInfo.line > 0 ? structInfo.line - 1 : 0;
			loc.range.start.character = structInfo.column > 0 ? structInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + structInfo.name.length();
			return loc;
		}

		// Check for interface type
		auto ifaceIt = semInfo.interfaces.find(word);
		if (ifaceIt != semInfo.interfaces.end()) {
			const InterfaceInfo &ifaceInfo = ifaceIt->second;
			lsp::Location loc;
			loc.uri = doc->uri;
			loc.range.start.line = ifaceInfo.line > 0 ? ifaceInfo.line - 1 : 0;
			loc.range.start.character = ifaceInfo.column > 0 ? ifaceInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + ifaceInfo.name.length();
			return loc;
		}
	}

	// Check stdlib functions
	auto stdlibFuncIt = stdlibFunctions.find(word);
	if (stdlibFuncIt != stdlibFunctions.end()) {
		const StdlibFunction &func = stdlibFuncIt->second;
		if (!func.filePath.empty() && func.line > 0) {
			lsp::Location loc;
			// Convert file path to URI format
			loc.uri = "file:///" + func.filePath;
			// Replace backslashes with forward slashes for URI
			std::replace(loc.uri.begin(), loc.uri.end(), '\\', '/');
			loc.range.start.line = func.line - 1;
			loc.range.start.character = func.column > 0 ? func.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + func.name.length();
			return loc;
		}
	}

	// Check stdlib structs
	auto stdlibStructIt = stdlibStructs.find(word);
	if (stdlibStructIt != stdlibStructs.end()) {
		const StdlibStruct &structInfo = stdlibStructIt->second;
		if (!structInfo.filePath.empty() && structInfo.line > 0) {
			lsp::Location loc;
			// Convert file path to URI format
			loc.uri = "file:///" + structInfo.filePath;
			// Replace backslashes with forward slashes for URI
			std::replace(loc.uri.begin(), loc.uri.end(), '\\', '/');
			loc.range.start.line = structInfo.line - 1;
			loc.range.start.character = structInfo.column > 0 ? structInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + structInfo.name.length();
			return loc;
		}
	}

	// Check stdlib interfaces
	auto stdlibIfaceIt = stdlibInterfaces.find(word);
	if (stdlibIfaceIt != stdlibInterfaces.end()) {
		const StdlibInterface &ifaceInfo = stdlibIfaceIt->second;
		if (!ifaceInfo.filePath.empty() && ifaceInfo.line > 0) {
			lsp::Location loc;
			// Convert file path to URI format
			loc.uri = "file:///" + ifaceInfo.filePath;
			// Replace backslashes with forward slashes for URI
			std::replace(loc.uri.begin(), loc.uri.end(), '\\', '/');
			loc.range.start.line = ifaceInfo.line - 1;
			loc.range.start.character = ifaceInfo.column > 0 ? ifaceInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + ifaceInfo.name.length();
			return loc;
		}
	}

	// Fallback: tokenize and search for declaration (for cases not in semantic cache)
	try {
		std::vector<Token> tokens = tokenize(doc->text);

		// Look for "var <word>", "let <word>", or "function <word>"
		for (size_t i = 0; i < tokens.size() - 1; i++) {
			if ((tokens[i].value == "var" || tokens[i].value == "let" || tokens[i].value == "function") &&
				tokens[i + 1].type == TokenType::IDENTIFIER &&
				tokens[i + 1].value == word) {

				lsp::Location loc;
				loc.uri = doc->uri;
				loc.range = tokenToRange(tokens[i + 1]);
				return loc;
			}
		}

	} catch (...) {
		// If something fails, return nullopt
	}

	return std::nullopt;
}

std::vector<lsp::SymbolInformation> Analyzer::getSymbols(std::shared_ptr<Document> doc) {
	std::vector<lsp::SymbolInformation> symbols;

	try {
		std::vector<Token> tokens = tokenize(doc->text);

		// Find function declarations
		for (size_t i = 0; i < tokens.size() - 1; i++) {
			if (tokens[i].value == "function" &&
				tokens[i + 1].type == TokenType::IDENTIFIER) {

				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Function;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			} else if (tokens[i].value == "var" &&
					   tokens[i + 1].type == TokenType::IDENTIFIER) {

				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Variable;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			}
		}

	} catch (...) {
		// Return empty list on error
	}

	return symbols;
}

std::string Analyzer::getWordAtPosition(const std::string &text, lsp::Position pos) {
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	while (std::getline(stream, line) && currentLine < pos.line) {
		currentLine++;
	}

	if (currentLine != pos.line || pos.character >= (int)line.length()) {
		return "";
	}

	// Find word boundaries
	int start = pos.character;
	int end = pos.character;

	while (start > 0 && (std::isalnum(line[start - 1]) || line[start - 1] == '_')) {
		start--;
	}

	while (end < (int)line.length() && (std::isalnum(line[end]) || line[end] == '_')) {
		end++;
	}

	return line.substr(start, end - start);
}

std::string Analyzer::findContainingStruct(const std::string &text, lsp::Position pos) {
	// Parse the text line by line to track struct/function scopes
	// We need to find if the cursor position is inside a method of a struct
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	std::string currentStruct;
	int structDepth = 0;
	bool inFunction = false;
	int functionDepth = 0;

	while (std::getline(stream, line) && currentLine <= pos.line) {
		// Trim leading whitespace
		size_t firstNonSpace = line.find_first_not_of(" \t");
		if (firstNonSpace == std::string::npos) {
			currentLine++;
			continue;
		}
		std::string trimmedLine = line.substr(firstNonSpace);

		// Check for struct declaration
		if (trimmedLine.find("struct ") == 0) {
			// Extract struct name - it's the word after "struct "
			size_t nameStart = 7; // length of "struct "
			size_t nameEnd = nameStart;
			while (nameEnd < trimmedLine.length() &&
				   (std::isalnum(trimmedLine[nameEnd]) || trimmedLine[nameEnd] == '_')) {
				nameEnd++;
			}
			currentStruct = trimmedLine.substr(nameStart, nameEnd - nameStart);
			structDepth = 1;
		}
		// Check for function inside struct
		else if (structDepth > 0 &&
				 (trimmedLine.find("function ") == 0 || trimmedLine.find("export function ") == 0)) {
			inFunction = true;
			functionDepth = 1;
		}
		// Check for end - could end function or struct
		else if (trimmedLine.find("end ") == 0 || trimmedLine == "end") {
			if (inFunction && functionDepth > 0) {
				functionDepth--;
				if (functionDepth == 0) {
					inFunction = false;
				}
			} else if (structDepth > 0) {
				structDepth--;
				if (structDepth == 0) {
					currentStruct.clear();
				}
			}
		}

		currentLine++;
	}

	// If we're inside a struct and inside a function, return the struct name
	if (structDepth > 0 && inFunction) {
		return currentStruct;
	}

	return "";
}

bool Analyzer::isKeyword(const std::string &word) const {
	return std::find(keywords.begin(), keywords.end(), word) != keywords.end();
}

lsp::Range Analyzer::tokenToRange(const Token &token) {
	lsp::Range range;
	range.start.line = token.line - 1; // LSP is 0-indexed
	range.start.character = token.column - 1;
	range.end.line = token.line - 1;
	range.end.character = token.column - 1 + token.value.length();
	return range;
}

void Analyzer::initializeStdlib(const std::string &stdlibPath) {
	// Store the stdlib path for later use (e.g., reloading modified files)
	stdlibPath_ = stdlibPath;

	auto files = findStdlibFiles(stdlibPath);

	for (const auto &filePath : files) {
		// Extract namespace from path (e.g., stdlib/fmt/integer.maxon -> stdlib.fmt)
		fs::path path(filePath);
		fs::path relativePath = fs::relative(path, stdlibPath);

		std::string namespaceName = "stdlib";
		if (relativePath.has_parent_path()) {
			for (const auto &part : relativePath.parent_path()) {
				namespaceName += "." + part.string();
			}
		}

		loadStdlibFile(filePath, namespaceName);
	}

	// After loading all files, resolve interfaceTypeBindings to typeAssignments for structs
	// This requires interface definitions to be loaded first
	for (auto &pair : stdlibStructs) {
		StdlibStruct &structInfo = pair.second;
		for (const auto &binding : structInfo.interfaceTypeBindings) {
			const std::string &interfaceName = binding.first;
			const std::vector<std::string> &withTypes = binding.second;

			// Look up the interface
			auto ifaceIt = stdlibInterfaces.find(interfaceName);
			if (ifaceIt != stdlibInterfaces.end()) {
				const StdlibInterface &iface = ifaceIt->second;
				// Map positionally: withTypes[i] -> associatedTypes[i]
				for (size_t i = 0; i < withTypes.size() && i < iface.associatedTypes.size(); i++) {
					const std::string &assocTypeName = iface.associatedTypes[i];
					const std::string &concreteType = withTypes[i];
					structInfo.typeAssignments[assocTypeName] = concreteType;
				}
			}
		}
	}
}

std::vector<std::string> Analyzer::findStdlibFiles(const std::string &stdlibPath) {
	std::vector<std::string> files;

	try {
		for (const auto &entry : fs::recursive_directory_iterator(stdlibPath)) {
			if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
				files.push_back(entry.path().string());
			}
		}
	} catch (...) {
		// Ignore errors (e.g., directory doesn't exist)
	}

	return files;
}

void Analyzer::loadStdlibFile(const std::string &filePath, const std::string &namespaceName) {

	try {
		std::ifstream file(filePath);
		if (!file.is_open()) {
			return;
		}

		std::string content((std::istreambuf_iterator<char>(file)),
							std::istreambuf_iterator<char>());
		file.close();

		// Parse the file to extract function signatures
		std::vector<Token> tokens = tokenize(content);

		Parser parser(tokens);
		auto program = parser.parse();

		// Extract module name from file path (e.g., "integer" from "stdlib/fmt/integer.maxon")
		fs::path path(filePath);
		std::string moduleName = path.stem().string();

		// Parse namespace to extract parts (e.g., "stdlib.fmt" -> ["stdlib", "fmt"])
		std::vector<std::string> nsParts;
		std::string current;
		for (char c : namespaceName) {
			if (c == '.') {
				if (!current.empty()) {
					nsParts.push_back(current);
					current.clear();
				}
			} else {
				current += c;
			}
		}
		if (!current.empty()) {
			nsParts.push_back(current);
		}

		// Build namespace hierarchy (stdlib -> fmt -> integer)
		// We expect: nsParts = ["stdlib", "fmt"], moduleName = "integer"
		if (!nsParts.empty() && nsParts[0] == "stdlib") {
			// Initialize root if needed
			if (namespaceRoot.name.empty()) {
				namespaceRoot.name = "stdlib";
			}

			NamespaceNode *current = &namespaceRoot;

			// Navigate/create intermediate nodes (e.g., "fmt")
			for (size_t i = 1; i < nsParts.size(); i++) {
				const std::string &part = nsParts[i];
				if (current->children.find(part) == current->children.end()) {
					current->children[part] = NamespaceNode{part, {}, {}};
				}
				current = &current->children[part];
			}

			// Add module node (e.g., "integer")
			if (current->children.find(moduleName) == current->children.end()) {
				current->children[moduleName] = NamespaceNode{moduleName, {}, {}};
			}
			NamespaceNode *moduleNode = &current->children[moduleName];

			// Extract function information
			for (const auto &func : program->functions) {
				StdlibFunction stdlibFunc;
				stdlibFunc.name = func->name;
				stdlibFunc.qualifiedName = namespaceName + "." + func->name;
				stdlibFunc.namespacePath = namespaceName;
				stdlibFunc.moduleName = moduleName;
				stdlibFunc.returnType = func->returnType;
				stdlibFunc.parameters = func->parameters;
				stdlibFunc.filePath = filePath;
				stdlibFunc.line = func->line;
				stdlibFunc.column = func->column;

				// Build signature
				std::string sig = "function " + func->name + "(";
				for (size_t i = 0; i < func->parameters.size(); i++) {
					if (i > 0)
						sig += ", ";
					sig += func->parameters[i].name + " " + func->parameters[i].type;
				}
				sig += ") " + func->returnType;
				stdlibFunc.signature = sig;

				// Extract documentation from comments before the function declaration
				stdlibFunc.documentation = extractDocumentation(content, func->name, func->line);
				if (stdlibFunc.documentation.empty()) {
					stdlibFunc.documentation = "Standard library function from " + namespaceName;
				}

				// Store by unqualified name
				stdlibFunctions[func->name] = stdlibFunc;

				// Add function to module node
				moduleNode->functions.push_back(func->name);
			}

			// Extract struct definitions and their members for type completion
			for (const auto &structDef : program->structs) {
				std::vector<TypeMember> members;

				// Add fields as properties
				for (const auto &field : structDef->fields) {
					TypeMember member;
					member.name = field.name;
					member.isMethod = false;
					member.returnType = field.type;
					member.signature = "";
					member.documentation = "Field of " + structDef->name;
					members.push_back(member);
				}

				// Add methods
				for (const auto &method : structDef->methods) {
					TypeMember member;
					member.name = method->name;
					member.isMethod = true;
					member.returnType = method->returnType;

					// Build method signature (skip first 'self' parameter for display)
					std::string sig = "(";
					bool first = true;
					for (size_t i = 0; i < method->parameters.size(); i++) {
						// Skip 'self' parameter
						if (method->parameters[i].name == "self") {
							continue;
						}
						if (!first) {
							sig += ", ";
						}
						sig += method->parameters[i].name + " " + method->parameters[i].type;
						first = false;
					}
					sig += ")";
					member.signature = sig;

					// Extract documentation
					member.documentation = extractDocumentation(content, method->name, method->line);
					if (member.documentation.empty()) {
						member.documentation = "Method of " + structDef->name;
					}

					members.push_back(member);

					// Also store for semantic analysis registration
					StdlibStructMethod structMethod;
					structMethod.structName = structDef->name;
					structMethod.methodName = method->name;
					structMethod.returnType = method->returnType;
					structMethod.parameters = method->parameters;
					structMethod.filePath = filePath;
					structMethod.line = method->line;
					structMethod.column = method->column;
					stdlibStructMethods.push_back(structMethod);
				}

				// Store members by struct name
				if (!members.empty()) {
					typeMembers[structDef->name] = members;
				}

				// Store struct for semantic analysis registration
				// This allows files that reference this struct to find it
				StdlibStruct stdlibStruct;
				stdlibStruct.name = structDef->name;
				for (const auto &field : structDef->fields) {
					stdlibStruct.fields.push_back(StructFieldInfo(field.name, field.type, field.isImmutable,
																  field.defaultValue != nullptr, "", field.line, field.column));
				}
				stdlibStruct.conformsTo = structDef->conformsTo;
				stdlibStruct.interfaceTypeBindings = structDef->interfaceTypeBindings;
				stdlibStruct.filePath = filePath;
				stdlibStruct.line = structDef->line;
				stdlibStruct.column = structDef->column;
				stdlibStructs[structDef->name] = stdlibStruct;
			}

			// Extract interface definitions for go-to-definition
			for (const auto &ifaceDef : program->interfaces) {
				StdlibInterface stdlibInterface;
				stdlibInterface.name = ifaceDef->name;
				stdlibInterface.associatedTypes = ifaceDef->associatedTypes;
				stdlibInterface.filePath = filePath;
				stdlibInterface.line = ifaceDef->line;
				stdlibInterface.column = ifaceDef->column;
				stdlibInterfaces[ifaceDef->name] = stdlibInterface;
			}
		}

	} catch (...) {
		// Ignore errors in parsing stdlib files
	}
}

std::string Analyzer::extractDocumentation(const std::string &sourceText, const std::string &functionName, int functionLine) {
	// Extract comments before the function declaration (lines leading up to functionLine)
	// Split source into lines
	std::vector<std::string> lines;
	std::istringstream stream(sourceText);
	std::string line;
	while (std::getline(stream, line)) {
		lines.push_back(line);
	}

	if (functionLine <= 0 || functionLine > static_cast<int>(lines.size())) {
		return "";
	}

	// Collect comment lines immediately before the function (line numbers are 1-based)
	std::vector<std::string> docLines;
	for (int i = functionLine - 2; i >= 0; i--) { // functionLine - 1 is 0-based index, so functionLine - 2 is the line before
		const std::string &currentLine = lines[i];
		std::string trimmed = currentLine;

		// Trim leading whitespace
		size_t start = trimmed.find_first_not_of(" \t\r\n");
		if (start == std::string::npos) {
			// Empty line - stop collecting
			break;
		}
		trimmed = trimmed.substr(start);

		// Check if it's a documentation comment line (///)
		if (trimmed.substr(0, 3) == "///") {
			// Remove the /// prefix and any leading space
			std::string comment = trimmed.substr(3);
			if (!comment.empty() && comment[0] == ' ') {
				comment = comment.substr(1);
			}
			docLines.insert(docLines.begin(), comment); // Insert at beginning to maintain order
		} else if (trimmed.substr(0, 2) == "//") {
			// Regular comment (not documentation) - stop collecting
			break;
		} else {
			// Non-comment, non-empty line - stop collecting
			break;
		}
	}

	// Join the documentation lines
	std::string documentation;
	for (const auto &docLine : docLines) {
		if (!documentation.empty()) {
			documentation += "\n";
		}
		documentation += docLine;
	}

	return documentation;
}

std::string Analyzer::getTextBeforePosition(const std::string &text, lsp::Position pos) {
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	while (std::getline(stream, line) && currentLine < pos.line) {
		currentLine++;
	}

	if (currentLine != pos.line) {
		return "";
	}

	// Return text from start of line up to position
	if (pos.character >= (int)line.length()) {
		return line;
	}

	return line.substr(0, pos.character);
}

std::vector<lsp::CompletionItem> Analyzer::getMemberCompletions(const std::string &typeName, const SemanticInfo &semInfo) {
	std::vector<lsp::CompletionItem> items;

	// First check type members registry - this has complete info for stdlib types (fields + methods)
	auto typeIt = typeMembers.find(typeName);
	if (typeIt != typeMembers.end()) {
		for (const auto &member : typeIt->second) {
			lsp::CompletionItem item;
			item.label = member.name;
			item.kind = member.isMethod ? lsp::CompletionItemKind::Method : lsp::CompletionItemKind::Property;
			item.detail = member.isMethod ? member.signature + " " + member.returnType : member.returnType;
			item.documentation = member.documentation;

			// For methods, add parentheses
			if (member.isMethod) {
				item.insertText = member.name + "()";
			}

			items.push_back(item);
		}
		return items;
	}

	// Check if it's a struct type defined in the current document
	auto structIt = semInfo.structs.find(typeName);
	if (structIt != semInfo.structs.end()) {
		// Return struct fields
		const StructInfo &structInfo = structIt->second;
		for (const auto &field : structInfo.fields) {
			lsp::CompletionItem item;
			item.label = field.name;
			item.kind = lsp::CompletionItemKind::Field;
			item.detail = field.type;
			item.documentation = "Field of struct " + typeName;
			items.push_back(item);
		}
		return items;
	}

	// Check if it's an array type (e.g., "[int]", "[float]")
	if (!typeName.empty() && typeName[0] == '[') {
		// Use array members from registry
		auto arrayIt = typeMembers.find("[]");
		if (arrayIt != typeMembers.end()) {
			for (const auto &member : arrayIt->second) {
				lsp::CompletionItem item;
				item.label = member.name;
				item.kind = member.isMethod ? lsp::CompletionItemKind::Method : lsp::CompletionItemKind::Property;
				item.detail = member.isMethod ? member.signature + " " + member.returnType : member.returnType;
				item.documentation = member.documentation;

				// For methods, add parentheses
				if (member.isMethod) {
					item.insertText = member.name + "()";
				}

				items.push_back(item);
			}
		}
		return items;
	}

	// No members found for this type
	return items;
}

std::vector<lsp::CompletionItem> Analyzer::getQualifiedNameCompletions(const std::string &prefix) {
	std::vector<lsp::CompletionItem> items;

	// Split prefix by dots to navigate namespace hierarchy
	std::vector<std::string> parts;
	std::string current;
	for (char c : prefix) {
		if (c == '.') {
			if (!current.empty()) {
				parts.push_back(current);
				current.clear();
			}
		} else {
			current += c;
		}
	}
	if (!current.empty()) {
		parts.push_back(current);
	}

	// Navigate the namespace hierarchy
	if (parts.empty()) {
		return items;
	}

	// Start from root: "stdlib"
	if (parts[0] == "stdlib") {
		if (parts.size() == 1) {
			// After "stdlib." - show top-level namespaces (fmt, fs, sys)
			const NamespaceNode *node = &namespaceRoot;
			for (const auto &child : node->children) {
				lsp::CompletionItem item;
				item.label = child.first;
				item.kind = lsp::CompletionItemKind::Module;
				item.detail = "stdlib." + child.first + " namespace";
				items.push_back(item);
			}
		} else if (parts.size() == 2) {
			// After "stdlib.fmt." - show modules (integer, etc.)
			const NamespaceNode *node = &namespaceRoot;
			auto it = node->children.find(parts[1]);
			if (it != node->children.end()) {
				for (const auto &child : it->second.children) {
					lsp::CompletionItem item;
					item.label = child.first;
					item.kind = lsp::CompletionItemKind::Module;
					item.detail = "Module in stdlib." + parts[1];
					items.push_back(item);
				}
			}
		} else if (parts.size() == 3) {
			// After "stdlib.fmt.integer." - show functions
			const NamespaceNode *node = &namespaceRoot;
			auto it1 = node->children.find(parts[1]);
			if (it1 != node->children.end()) {
				auto it2 = it1->second.children.find(parts[2]);
				if (it2 != it1->second.children.end()) {
					for (const auto &funcName : it2->second.functions) {
						// Find the full function info
						auto funcIt = stdlibFunctions.find(funcName);
						if (funcIt != stdlibFunctions.end()) {
							const StdlibFunction &func = funcIt->second;
							lsp::CompletionItem item;
							item.label = func.name;
							item.kind = lsp::CompletionItemKind::Function;
							item.detail = func.signature;
							item.documentation = func.documentation;
							items.push_back(item);
						}
					}
				}
			}
		}
	}

	return items;
}

std::optional<lsp::WorkspaceEdit> Analyzer::getRename(std::shared_ptr<Document> doc, lsp::Position pos, const std::string &newName) {
	try {
		std::vector<Token> tokens = tokenize(doc->text);

		// Find the token at the given position
		Token *targetToken = nullptr;
		size_t targetIndex = 0;
		for (size_t i = 0; i < tokens.size(); i++) {
			auto &token = tokens[i];
			// Check if position is within this token
			// Token positions are 1-based, LSP positions are 0-based
			int tokenLine = token.line - 1;
			int tokenCol = token.column - 1;
			int tokenEndCol = tokenCol + token.value.length();

			if (pos.line == tokenLine && pos.character >= tokenCol && pos.character < tokenEndCol) {
				targetToken = &token;
				targetIndex = i;
				break;
			}
		}

		if (!targetToken) {
			return std::nullopt;
		}

		std::vector<lsp::TextEdit> edits;

		// Handle block identifiers (quoted strings like 'Planet')
		if (targetToken->type == TokenType::BLOCK_ID) {
			std::string oldName = targetToken->value;

			// Find all BLOCK_ID tokens with the same value
			for (const auto &token : tokens) {
				if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
					lsp::TextEdit edit;
					edit.range.start.line = token.line - 1;
					edit.range.start.character = token.column - 1;
					edit.range.end.line = token.line - 1;
					edit.range.end.character = token.column - 1 + token.value.length();
					edit.newText = newName;
					edits.push_back(edit);
				}
			}
		}
		// Handle struct name identifiers
		else if (targetToken->type == TokenType::IDENTIFIER && targetIndex > 0) {
			// Check if this is a struct name (preceded by 'struct' keyword)
			if (tokens[targetIndex - 1].value == "struct") {
				std::string oldName = targetToken->value;

				// Find all usages of this struct name:
				// 1. The struct declaration: struct NAME
				// 2. Variable declarations: var x NAME
				// 3. Function parameters: function foo(p NAME)
				// 4. Array types: []NAME
				// 5. The end block identifier: end 'NAME'

				for (size_t i = 0; i < tokens.size(); i++) {
					const auto &token = tokens[i];

					if (token.type == TokenType::IDENTIFIER && token.value == oldName) {
						// Check context to see if this is a type usage
						bool isTypeUsage = false;

						if (i > 0) {
							TokenType prevType = tokens[i - 1].type;
							// After struct keyword
							if (tokens[i - 1].value == "struct") {
								isTypeUsage = true;
							}
							// After function signature closing paren (return type)
							else if (prevType == TokenType::RPAREN) {
								isTypeUsage = true;
							}
							// After variable/parameter name (e.g., "var x Planet" or "param Planet")
							else if (prevType == TokenType::IDENTIFIER) {
								// Check if the previous identifier is itself after var/let/a comma/lparen
								if (i > 1) {
									TokenType prevPrevType = tokens[i - 2].type;
									if (tokens[i - 2].value == "var" ||
										tokens[i - 2].value == "let" ||
										prevPrevType == TokenType::COMMA ||
										prevPrevType == TokenType::LPAREN) {
										isTypeUsage = true;
									}
								}
							}
							// After array bracket ([]NAME)
							else if (prevType == TokenType::RBRACKET) {
								isTypeUsage = true;
							}
						}

						// Check if followed by { for struct literal (e.g., Planet{...})
						if (!isTypeUsage && i + 1 < tokens.size()) {
							if (tokens[i + 1].type == TokenType::LBRACE) {
								isTypeUsage = true;
							}
						}

						if (isTypeUsage) {
							lsp::TextEdit edit;
							edit.range.start.line = token.line - 1;
							edit.range.start.character = token.column - 1;
							edit.range.end.line = token.line - 1;
							edit.range.end.character = token.column - 1 + token.value.length();
							edit.newText = newName;
							edits.push_back(edit);
						}
					}
					// Also rename the block identifier at the end
					// Block IDs: token.value contains content without quotes
					// token.column points to the opening quote
					// Actual source span is: quote + content + quote (2 extra chars)
					else if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
						lsp::TextEdit edit;
						edit.range.start.line = token.line - 1;
						edit.range.start.character = token.column - 1;
						edit.range.end.line = token.line - 1;
						edit.range.end.character = token.column - 1 + token.value.length() + 2; // +2 for quotes
						// Need to include quotes in the new text - detect quote style from source
						// For now, just use single quotes (the most common)
						edit.newText = "'" + newName + "'";
						edits.push_back(edit);
					}
				}
			}
			// Could also be a function name or variable - but for now we'll skip those
			else {
				return std::nullopt;
			}
		} else {
			return std::nullopt;
		}

		if (edits.empty()) {
			return std::nullopt;
		}

		lsp::WorkspaceEdit workspaceEdit;
		workspaceEdit.changes[doc->uri] = edits;

		return workspaceEdit;

	} catch (const std::exception &e) {
		// If lexing fails, return no edits
		return std::nullopt;
	}
}

std::optional<std::vector<lsp::Range>> Analyzer::getLinkedEditingRanges(std::shared_ptr<Document> doc, lsp::Position pos) {
	try {
		std::vector<Token> tokens = tokenize(doc->text);

		// Find the token at the given position
		Token *targetToken = nullptr;
		size_t targetIndex = 0;
		for (size_t i = 0; i < tokens.size(); i++) {
			auto &token = tokens[i];
			int tokenLine = token.line - 1;
			int tokenCol = token.column - 1;

			// For BLOCK_ID tokens, the column points to the quote, but we want to match inside the quotes
			if (token.type == TokenType::BLOCK_ID) {
				// The range we want to match is inside the quotes: column+1 to column+1+length
				int tokenStartCol = tokenCol + 1; // Skip opening quote
				int tokenEndCol = tokenStartCol + token.value.length();

				if (pos.line == tokenLine && pos.character >= tokenStartCol && pos.character < tokenEndCol) {
					targetToken = &token;
					targetIndex = i;
					break;
				}
			} else {
				// Regular tokens
				int tokenEndCol = tokenCol + token.value.length();

				if (pos.line == tokenLine && pos.character >= tokenCol && pos.character < tokenEndCol) {
					targetToken = &token;
					targetIndex = i;
					break;
				}
			}
		}

		if (!targetToken) {
			return std::nullopt;
		}

		std::vector<lsp::Range> ranges;

		// Handle block identifiers
		if (targetToken->type == TokenType::BLOCK_ID) {
			std::string oldName = targetToken->value;

			// Find all BLOCK_ID tokens with the same value
			for (const auto &token : tokens) {
				if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
					lsp::Range range;
					range.start.line = token.line - 1;
					// token.column is 1-based and points to the opening quote
					// We want the range to be inside the quotes: skip the opening quote
					range.start.character = token.column; // token.column (1-based) - 1 (to 0-based) + 1 (skip quote) = token.column
					range.end.line = token.line - 1;
					range.end.character = token.column + token.value.length(); // ends before closing quote
					ranges.push_back(range);
				}
			}
		}
		// Handle function name identifiers
		else if (targetToken->type == TokenType::IDENTIFIER && targetIndex > 0) {
			if (tokens[targetIndex - 1].value == "function") {
				std::string functionName = targetToken->value;

				// Find the function name declaration and its matching block identifier
				for (size_t i = 0; i < tokens.size(); i++) {
					const auto &token = tokens[i];

					// Function name at declaration (unqualified: "function foo")
					if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
						i > 0 && tokens[i - 1].value == "function") {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column - 1;
						range.end.line = token.line - 1;
						range.end.character = token.column - 1 + token.value.length();
						ranges.push_back(range);
					}
					// Qualified function name (e.g., "Interface.method" in implementations)
					// Pattern: function <identifier> DOT <functionName>
					else if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
							 i >= 2 &&
							 tokens[i - 1].type == TokenType::DOT &&
							 tokens[i - 2].type == TokenType::IDENTIFIER &&
							 i >= 3 && tokens[i - 3].value == "function") {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column - 1;
						range.end.line = token.line - 1;
						range.end.character = token.column - 1 + token.value.length();
						ranges.push_back(range);
					}
					// Block identifier matching the function name (in quotes at 'end')
					else if (token.type == TokenType::BLOCK_ID && token.value == functionName) {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column; // Skip opening quote
						range.end.line = token.line - 1;
						range.end.character = token.column + token.value.length(); // Before closing quote
						ranges.push_back(range);
					}
				}
			}
			// Handle qualified function name (when clicking after the dot)
			// Pattern: function <identifier> DOT <targetToken>
			else if (targetIndex >= 2 &&
					 tokens[targetIndex - 1].type == TokenType::DOT &&
					 tokens[targetIndex - 2].type == TokenType::IDENTIFIER &&
					 targetIndex >= 3 && tokens[targetIndex - 3].value == "function") {
				std::string functionName = targetToken->value;

				// Find all occurrences of this function name
				for (size_t i = 0; i < tokens.size(); i++) {
					const auto &token = tokens[i];

					// Function name at declaration (unqualified: "function foo")
					if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
						i > 0 && tokens[i - 1].value == "function") {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column - 1;
						range.end.line = token.line - 1;
						range.end.character = token.column - 1 + token.value.length();
						ranges.push_back(range);
					}
					// Qualified function name (e.g., "Interface.method" in implementations)
					else if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
							 i >= 2 &&
							 tokens[i - 1].type == TokenType::DOT &&
							 tokens[i - 2].type == TokenType::IDENTIFIER &&
							 i >= 3 && tokens[i - 3].value == "function") {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column - 1;
						range.end.line = token.line - 1;
						range.end.character = token.column - 1 + token.value.length();
						ranges.push_back(range);
					}
					// Block identifier matching the function name
					else if (token.type == TokenType::BLOCK_ID && token.value == functionName) {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column; // Skip opening quote
						range.end.line = token.line - 1;
						range.end.character = token.column + token.value.length(); // Before closing quote
						ranges.push_back(range);
					}
				}
			}
			// Handle struct name identifiers
			else if (tokens[targetIndex - 1].value == "struct") {
				std::string oldName = targetToken->value;

				// Find all usages of this struct name
				for (size_t i = 0; i < tokens.size(); i++) {
					const auto &token = tokens[i];

					if (token.type == TokenType::IDENTIFIER && token.value == oldName) {
						bool isTypeUsage = false;

						if (i > 0) {
							TokenType prevTokenType = tokens[i - 1].type;
							if (tokens[i - 1].value == "struct") {
								isTypeUsage = true;
							} else if (prevTokenType == TokenType::RPAREN) {
								// Return type after function signature
								isTypeUsage = true;
							} else if (prevTokenType == TokenType::IDENTIFIER) {
								if (i > 1) {
									TokenType prevPrevType = tokens[i - 2].type;
									if (tokens[i - 2].value == "var" ||
										tokens[i - 2].value == "let" ||
										prevPrevType == TokenType::COMMA ||
										prevPrevType == TokenType::LPAREN) {
										isTypeUsage = true;
									}
								}
							} else if (prevTokenType == TokenType::RBRACKET) {
								isTypeUsage = true;
							}
						}

						if (!isTypeUsage && i + 1 < tokens.size()) {
							if (tokens[i + 1].type == TokenType::LBRACE) {
								isTypeUsage = true;
							}
						}

						if (isTypeUsage) {
							lsp::Range range;
							range.start.line = token.line - 1;
							range.start.character = token.column - 1;
							range.end.line = token.line - 1;
							range.end.character = token.column - 1 + token.value.length();
							ranges.push_back(range);
						}
					}
					// Include block identifiers that match the struct name
					// For linked editing, select only the content inside quotes (not the quotes themselves)
					else if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column; // Skip opening quote
						range.end.line = token.line - 1;
						range.end.character = token.column + token.value.length(); // Before closing quote
						ranges.push_back(range);
					}
				}
			} else {
				return std::nullopt;
			}
		} else {
			return std::nullopt;
		}

		if (ranges.empty()) {
			return std::nullopt;
		}

		return ranges;

	} catch (const std::exception &e) {
		return std::nullopt;
	}
}

bool Analyzer::isStdlibFile(const std::string &filePath) const {
	if (stdlibPath_.empty()) {
		return false;
	}

	try {
		fs::path stdlibCanonical = fs::canonical(stdlibPath_);
		fs::path fileCanonical = fs::canonical(filePath);

		// Check if the file path starts with the stdlib path
		auto [rootEnd, nothing] = std::mismatch(stdlibCanonical.begin(), stdlibCanonical.end(), fileCanonical.begin());
		bool isStdlib = rootEnd == stdlibCanonical.end();
		if (isStdlib) {
			std::cerr << "[DEBUG] File is in stdlib: " << filePath << std::endl;
		}
		return isStdlib;
	} catch (...) {
		return false;
	}
}

void Analyzer::reloadStdlibFile(const std::string &filePath) {
	if (stdlibPath_.empty()) {
		std::cerr << "[DEBUG] reloadStdlibFile: stdlibPath_ is empty" << std::endl;
		return;
	}

	std::cerr << "[DEBUG] Reloading stdlib file: " << filePath << std::endl;

	try {
		fs::path path(filePath);
		fs::path relativePath = fs::relative(path, stdlibPath_);

		// Build namespace name from relative path
		std::string namespaceName = "stdlib";
		if (relativePath.has_parent_path()) {
			for (const auto &part : relativePath.parent_path()) {
				namespaceName += "." + part.string();
			}
		}

		// Get module name to clear old data
		std::string moduleName = path.stem().string();
		std::cerr << "[DEBUG] Namespace: " << namespaceName << ", Module: " << moduleName << std::endl;

		// Clear old function entries from this file
		// We need to iterate and remove functions that belong to this namespace/module
		int removedFunctions = 0;
		for (auto it = stdlibFunctions.begin(); it != stdlibFunctions.end();) {
			if (it->second.namespacePath == namespaceName && it->second.moduleName == moduleName) {
				it = stdlibFunctions.erase(it);
				removedFunctions++;
			} else {
				++it;
			}
		}

		// Clear module node's function list to avoid duplicates
		// Navigate to the module node
		NamespaceNode *current = &namespaceRoot;
		std::vector<std::string> nsParts;
		std::string part;
		for (char c : namespaceName) {
			if (c == '.') {
				if (!part.empty()) {
					nsParts.push_back(part);
					part.clear();
				}
			} else {
				part += c;
			}
		}
		if (!part.empty()) {
			nsParts.push_back(part);
		}

		// Navigate to module's parent and clear the module's function list
		for (size_t i = 1; i < nsParts.size(); i++) {
			auto it = current->children.find(nsParts[i]);
			if (it != current->children.end()) {
				current = &it->second;
			}
		}
		auto moduleIt = current->children.find(moduleName);
		if (moduleIt != current->children.end()) {
			moduleIt->second.functions.clear();
		}

		// Clear old struct entries from this file
		// For now, we reload all structs from this file
		// A more precise approach would track which file each struct came from
		for (auto it = stdlibStructs.begin(); it != stdlibStructs.end();) {
			++it;
		}

		// Count functions before reload
		size_t functionsBefore = stdlibFunctions.size();

		// Reload the file
		loadStdlibFile(filePath, namespaceName);

		size_t functionsAfter = stdlibFunctions.size();
		std::cerr << "[DEBUG] Reloaded stdlib file: removed " << removedFunctions
				  << " functions, added " << (functionsAfter - functionsBefore) << std::endl;

	} catch (const std::exception &e) {
		std::cerr << "[DEBUG] Error reloading stdlib file: " << e.what() << std::endl;
	} catch (...) {
		std::cerr << "[DEBUG] Unknown error reloading stdlib file" << std::endl;
	}
}
