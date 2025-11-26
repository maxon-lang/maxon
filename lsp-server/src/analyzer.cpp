#include "analyzer.h"
#include "semantic_analyzer.h"
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
}

std::vector<lsp::Diagnostic> Analyzer::analyze(std::shared_ptr<Document> doc) {
	std::vector<lsp::Diagnostic> diagnostics;

	try {
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

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

			// Get the set of function names defined in the current document
			std::set<std::string> currentDocFunctions;
			for (const auto &func : program->functions) {
				currentDocFunctions.insert(func->name);
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

			std::vector<SemanticError> semanticErrors = semanticAnalyzer.analyze(program.get());

			// Cache semantic analysis results for this document
			SemanticInfo &semInfo = semanticCache[doc->uri];
			semInfo.variables = semanticAnalyzer.getAllVariables();
			semInfo.functions = semanticAnalyzer.getFunctions();
			semInfo.structs = semanticAnalyzer.getStructs();

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
			diag.range.start.line = 0;
			diag.range.start.character = 0;
			diag.range.end.line = 0;
			diag.range.end.character = 1;
			diag.message = std::string("Parse error: ") + e.what();
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
		// We're after a dot - provide qualified name completions
		// Extract the prefix before the dot (e.g., "stdlib" from "stdlib.")
		size_t start = lastDot;
		while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
							 textBeforeCursor[start - 1] == '_' || textBeforeCursor[start - 1] == '.')) {
			start--;
		}
		std::string prefix = textBeforeCursor.substr(start, lastDot - start);

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
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

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

std::optional<lsp::Hover> Analyzer::getHover(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::string word = getWordAtPosition(doc->text, pos);

	if (word.empty()) {
		return std::nullopt;
	}

	lsp::Hover hover;

	// Check cached semantic info for this document
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

		// Check for function (user-defined)
		auto funcIt = semInfo.functions.find(word);
		if (funcIt != semInfo.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;
			std::string sig = "function " + funcInfo.name + "(";
			for (size_t i = 0; i < funcInfo.parameters.size(); i++) {
				if (i > 0)
					sig += ", ";
				sig += funcInfo.parameters[i].name + " " + funcInfo.parameters[i].type;
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
		case KeywordCategory::MathIntrinsic:
			categoryName = "math intrinsic";
			break;
		case KeywordCategory::Literal:
			categoryName = "literal";
			break;
		case KeywordCategory::Operator:
			categoryName = "operator";
			break;
		}
		hover.contents = "**" + word + "** (" + categoryName + ")\n\n" + meta.description;
	} else {
		hover.contents = "**" + word + "**\n\nIdentifier";
	}

	return hover;
}

std::optional<lsp::Location> Analyzer::getDefinition(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::string word = getWordAtPosition(doc->text, pos);

	if (word.empty()) {
		return std::nullopt;
	}

	// Try to find the first occurrence of this identifier being declared
	try {
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

		// Look for "var <word>" or "function <word>"
		for (size_t i = 0; i < tokens.size() - 1; i++) {
			if ((tokens[i].value == "var" || tokens[i].value == "function") &&
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
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

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
		Lexer lexer(content);
		std::vector<Token> tokens = lexer.tokenize();

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
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

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
		Lexer lexer(doc->text);
		std::vector<Token> tokens = lexer.tokenize();

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

					// Function name at declaration
					if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
						i > 0 && tokens[i - 1].value == "function") {
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
