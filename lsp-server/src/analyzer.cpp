#include "analyzer.h"
#include "intrinsics.h"
#include "lexer.h"
#include "parser.h"
#include "semantic_analyzer.h"
#include "type_members.h"
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

namespace fs = std::filesystem;

// ============================================================================
// Constructor
// ============================================================================

Analyzer::Analyzer() {
	// Get keyword info from compiler API - single source of truth
	keywordInfo_ = getKeywordInfo();

	// Initialize array type members from shared type registry
	initializeTypeMembers();
}

// ============================================================================
// Initialization
// ============================================================================

void Analyzer::initializeTypeMembers() {
	// Array type members from shared registry
	for (const auto &member : getArrayTypeMembers()) {
		typeMembers_["[]"].push_back({
			member.name,
			member.isMethod,
			member.returnType,
			member.signature,
			member.documentation
		});
	}
}

void Analyzer::initializeStdlib(const std::string &stdlibPath) {
	stdlibPath_ = stdlibPath;

	// Use compiler API to load stdlib symbols
	stdlibSymbols_ = loadStdlib(stdlibPath);

	// Build lookup tables and namespace hierarchy
	buildStdlibLookups();
	buildNamespaceHierarchy();
}

void Analyzer::buildStdlibLookups() {
	// Clear existing lookups
	stdlibFunctionsByName_.clear();
	stdlibStructsByName_.clear();
	stdlibEnumsByName_.clear();
	stdlibInterfacesByName_.clear();
	typeMembers_.clear();

	// Re-init array type members
	initializeTypeMembers();

	// Build function lookup
	for (auto &func : stdlibSymbols_.functions) {
		stdlibFunctionsByName_[func.name] = &func;
	}

	// Build struct lookup and type members
	for (auto &structSym : stdlibSymbols_.structs) {
		stdlibStructsByName_[structSym.name] = &structSym;
	}

	// Build enum lookup
	for (auto &enumSym : stdlibSymbols_.enums) {
		stdlibEnumsByName_[enumSym.name] = &enumSym;
	}

	// Build interface lookup
	for (auto &ifaceSym : stdlibSymbols_.interfaces) {
		stdlibInterfacesByName_[ifaceSym.name] = &ifaceSym;
	}
}

void Analyzer::buildNamespaceHierarchy() {
	// Build namespace hierarchy from stdlib symbols
	namespaceRoot_ = NamespaceNode{"stdlib", {}, {}};

	// Extract namespace paths from symbol source ranges
	// For now, we just add functions to the root
	// A more complete implementation would parse the source range filenames
	for (const auto &func : stdlibSymbols_.functions) {
		namespaceRoot_.functions.push_back(func.name);
	}
}

void Analyzer::reloadStdlib() {
	if (!stdlibPath_.empty()) {
		initializeStdlib(stdlibPath_);
	}
}

void Analyzer::reloadStdlibFile(const std::string &filePath) {
	// Reload entire stdlib for now - could be optimized to just reload one file
	reloadStdlib();
}

void Analyzer::invalidateAllDocumentCaches() {
	// Clear all document analysis caches
	documentCaches_.clear();
	lastGoodCaches_.clear();
	semanticCache_.clear();
	lastAnalysisTime_.clear();
}

void Analyzer::invalidateDocumentCache(const std::string &uri) {
	// Clear cache for a specific document
	documentCaches_.erase(uri);
	lastGoodCaches_.erase(uri);
	semanticCache_.erase(uri);
	lastAnalysisTime_.erase(uri);
}

bool Analyzer::isStdlibFile(const std::string &filePath) const {
	if (stdlibPath_.empty()) {
		return false;
	}

	try {
		fs::path stdPath = fs::canonical(stdlibPath_);
		fs::path checkPath = fs::canonical(filePath);
		return checkPath.string().find(stdPath.string()) == 0;
	} catch (...) {
		return false;
	}
}

// ============================================================================
// Throttling
// ============================================================================

bool Analyzer::shouldThrottleAnalysis(const std::string &uri, int64_t lastAnalysisMs) {
	auto now = std::chrono::steady_clock::now();
	auto lastTimeIt = lastAnalysisTime_.find(uri);

	if (lastTimeIt == lastAnalysisTime_.end()) {
		lastAnalysisTime_[uri] = now;
		return false;
	}

	// Calculate adaptive delay: max(50ms, 2 * lastAnalysisMs)
	int64_t minDelayMs = std::max(50LL, 2 * lastAnalysisMs);
	auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
		now - lastTimeIt->second).count();

	if (elapsed < minDelayMs) {
		return true;  // Throttle - too soon since last analysis
	}

	lastAnalysisTime_[uri] = now;
	return false;
}

// ============================================================================
// Error Region Detection
// ============================================================================

bool Analyzer::isInsideErrorRegion(const DocumentCache &cache, int line, int column) {
	// Check if position is inside any ErrorStmtAST region
	// This would require walking the AST to find ErrorStmtAST nodes
	// For now, we just check if there are parse errors at or before this line
	for (const auto &error : cache.parseErrors) {
		if (error.line == line) {
			return true;
		}
	}
	return false;
}

// ============================================================================
// Analysis
// ============================================================================

std::vector<lsp::Diagnostic> Analyzer::analyze(std::shared_ptr<Document> doc) {
	std::vector<lsp::Diagnostic> diagnostics;

	// Check throttling based on last analysis time
	auto cacheIt = documentCaches_.find(doc->uri);
	if (cacheIt != documentCaches_.end()) {
		if (shouldThrottleAnalysis(doc->uri, cacheIt->second.lastAnalysisMs)) {
			// Return cached diagnostics
			for (const auto &err : cacheIt->second.parseErrors) {
				lsp::Diagnostic diag;
				diag.range.start.line = err.line > 0 ? err.line - 1 : 0;
				diag.range.start.character = err.column > 0 ? err.column - 1 : 0;
				diag.range.end.line = err.endLine > 0 ? err.endLine - 1 : diag.range.start.line;
				diag.range.end.character = err.endColumn > 0 ? err.endColumn - 1 : diag.range.start.character + 1;
				diag.message = err.message;
				diag.severity = err.severity;
				diag.source = "maxon";
				diagnostics.push_back(diag);
			}
			for (const auto &err : cacheIt->second.semanticErrors) {
				lsp::Diagnostic diag;
				diag.range.start.line = err.line > 0 ? err.line - 1 : 0;
				diag.range.start.character = err.column > 0 ? err.column - 1 : 0;
				diag.range.end.line = err.line > 0 ? err.line - 1 : 0;
				diag.range.end.character = err.column > 0 ? err.column : 1;
				diag.message = err.message;
				diag.severity = err.severity;
				diag.source = "maxon";
				diagnostics.push_back(diag);
			}
			return diagnostics;
		}
	}

	// Measure analysis time
	auto startTime = std::chrono::steady_clock::now();

	// Use compiler API for analysis
	std::string filename = doc->uri;
	// Convert URI to file path if needed
	if (filename.find("file:///") == 0) {
		filename = filename.substr(8);
		// Handle Windows paths
		if (filename.length() > 2 && filename[0] == '/' && filename[2] == ':') {
			filename = filename.substr(1);
		}
	}

	LSPAnalysisResult result = analyzeForLSP(doc->text, filename);

	auto endTime = std::chrono::steady_clock::now();
	int64_t analysisMs = std::chrono::duration_cast<std::chrono::milliseconds>(
		endTime - startTime).count();

	// Create document cache entry
	DocumentCache cache;
	cache.ast = std::move(result.ast);
	cache.symbols = std::move(result.symbols);
	cache.parseErrors = std::move(result.parseErrors);
	cache.semanticErrors = std::move(result.semanticErrors);
	cache.lastAnalysisMs = analysisMs;
	cache.version = doc->version;

	// If analysis succeeded (no parse errors), update lastGoodCache
	if (!cache.hasParseErrors() && cache.ast) {
		// We need to deep copy for lastGoodCache since we're moving the current cache
		// For now, we'll just update semantic cache from the current analysis
		updateSemanticCache(doc->uri, cache);
	}

	// Convert errors to diagnostics
	for (const auto &err : cache.parseErrors) {
		lsp::Diagnostic diag;
		diag.range.start.line = err.line > 0 ? err.line - 1 : 0;
		diag.range.start.character = err.column > 0 ? err.column - 1 : 0;
		diag.range.end.line = err.endLine > 0 ? err.endLine - 1 : diag.range.start.line;
		diag.range.end.character = err.endColumn > 0 ? err.endColumn - 1 : diag.range.start.character + 1;
		diag.message = "Parse error: " + err.message;
		diag.severity = err.severity;
		diag.source = "maxon";
		diagnostics.push_back(diag);
	}

	for (const auto &err : cache.semanticErrors) {
		lsp::Diagnostic diag;
		diag.range.start.line = err.line > 0 ? err.line - 1 : 0;
		diag.range.start.character = err.column > 0 ? err.column - 1 : 0;
		diag.range.end.line = err.line > 0 ? err.line - 1 : 0;
		diag.range.end.character = err.column > 0 ? err.column : 1;
		diag.message = err.message;
		diag.severity = err.severity;
		diag.source = "maxon";
		if (!err.code.empty()) {
			diag.code = err.code;
		}
		diagnostics.push_back(diag);
	}

	// Store cache
	documentCaches_[doc->uri] = std::move(cache);

	return diagnostics;
}

void Analyzer::updateSemanticCache(const std::string &uri, const DocumentCache &cache) {
	if (!cache.ast) return;

	SemanticInfo &semInfo = semanticCache_[uri];

	// Run semantic analyzer to get variable/function info
	SemanticAnalyzer analyzer;
	analyzer.registerBuiltinFunctions();

	// Register stdlib symbols
	for (const auto &func : stdlibSymbols_.functions) {
		// Parse the signature to get parameters
		// For now, we just register with empty parameters
		analyzer.registerExternalFunction(func.name, "void", {});
	}

	// Analyze the AST
	analyzer.analyze(cache.ast.get());

	// Cache the results
	semInfo.variables = analyzer.getAllVariables();
	semInfo.functions = analyzer.getFunctions();
	semInfo.structs = analyzer.getStructs();
	semInfo.interfaces = analyzer.getInterfaces();

	// Extract enums from AST
	for (const auto &enumDef : cache.ast->enums) {
		SemanticInfo::EnumInfo enumInfo;
		enumInfo.name = enumDef->name;
		enumInfo.rawValueType = enumDef->rawValueType;
		enumInfo.line = enumDef->line;
		enumInfo.column = enumDef->column;

		for (const auto &caseDef : enumDef->cases) {
			SemanticInfo::EnumInfo::CaseInfo caseInfo;
			caseInfo.name = caseDef.name;
			caseInfo.hasRawValue = caseDef.rawValue != nullptr;
			caseInfo.line = caseDef.line;
			caseInfo.column = caseDef.column;
			for (const auto &av : caseDef.associatedValues) {
				caseInfo.associatedValues.push_back({av.name, av.type});
			}
			enumInfo.cases.push_back(caseInfo);
		}

		semInfo.enumDetails[enumDef->name] = enumInfo;

		// Also add to typeMembers for dot-completion
		std::vector<TypeMember> enumMembers;
		for (const auto &caseDef : enumDef->cases) {
			TypeMember member;
			member.name = caseDef.name;
			member.isMethod = !caseDef.associatedValues.empty();
			member.returnType = enumDef->name;
			if (!caseDef.associatedValues.empty()) {
				std::string sig = "(";
				bool first = true;
				for (const auto &av : caseDef.associatedValues) {
					if (!first) sig += ", ";
					sig += av.name + " " + av.type;
					first = false;
				}
				sig += ")";
				member.signature = sig;
			}
			member.documentation = "Case of enum " + enumDef->name;
			enumMembers.push_back(member);
		}

		if (!enumDef->rawValueType.empty()) {
			TypeMember rawValueMember;
			rawValueMember.name = "rawValue";
			rawValueMember.isMethod = false;
			rawValueMember.returnType = enumDef->rawValueType;
			rawValueMember.documentation = "Raw value of the enum case";
			enumMembers.push_back(rawValueMember);
		}

		for (const auto &method : enumDef->methods) {
			TypeMember member;
			member.name = method->name;
			member.isMethod = true;
			member.returnType = method->returnType;
			std::string sig = "(";
			bool first = true;
			for (const auto &param : method->parameters) {
				if (param.name == "self") continue;
				if (!first) sig += ", ";
				sig += param.name + " " + param.type;
				first = false;
			}
			sig += ")";
			member.signature = sig;
			member.documentation = "Method of enum " + enumDef->name;
			enumMembers.push_back(member);
		}

		if (!enumMembers.empty()) {
			typeMembers_[enumDef->name] = enumMembers;
		}
	}

	// Extract struct members for type completions
	for (const auto &structDef : cache.ast->structs) {
		std::vector<TypeMember> members;

		for (const auto &field : structDef->fields) {
			TypeMember member;
			member.name = field.name;
			member.isMethod = false;
			member.returnType = field.type;
			member.documentation = "Field of " + structDef->name;
			members.push_back(member);
		}

		for (const auto &method : structDef->methods) {
			TypeMember member;
			member.name = method->name;
			member.isMethod = true;
			member.returnType = method->returnType;
			std::string sig = "(";
			bool first = true;
			for (const auto &param : method->parameters) {
				if (param.name == "self") continue;
				if (!first) sig += ", ";
				sig += param.name + " " + param.type;
				first = false;
			}
			sig += ")";
			member.signature = sig;
			member.documentation = "Method of " + structDef->name;
			members.push_back(member);
		}

		if (!members.empty()) {
			typeMembers_[structDef->name] = members;
		}
	}
}

// ============================================================================
// Completions
// ============================================================================

std::vector<lsp::CompletionItem> Analyzer::getKeywordCompletions(const std::string &prefix) {
	std::vector<lsp::CompletionItem> items;

	// Use compiler API for keyword completions
	auto keywords = prefix.empty() ? keywordInfo_ : getKeywordsForCompletion(prefix);

	for (const auto &kw : keywords) {
		lsp::CompletionItem item;
		item.label = kw.name;
		item.detail = kw.documentation;
		item.insertText = kw.insertText;

		// Map completion kind
		switch (kw.completionKind) {
		case KeywordCompletionKind::Function:
			item.kind = lsp::CompletionItemKind::Function;
			break;
		case KeywordCompletionKind::Constant:
			item.kind = lsp::CompletionItemKind::Constant;
			break;
		case KeywordCompletionKind::Operator:
			item.kind = lsp::CompletionItemKind::Operator;
			break;
		case KeywordCompletionKind::TypeParameter:
			item.kind = lsp::CompletionItemKind::TypeParameter;
			break;
		default:
			item.kind = lsp::CompletionItemKind::Keyword;
			break;
		}

		items.push_back(item);
	}

	return items;
}

std::vector<lsp::CompletionItem> Analyzer::getCompletions(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::vector<lsp::CompletionItem> items;

	// Get text before cursor to check for qualified name context
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);

	// Check if we're in a qualified name context (e.g., "stdlib.", "arr.")
	size_t lastDot = textBeforeCursor.find_last_of('.');

	if (lastDot != std::string::npos) {
		// Extract the prefix before the dot
		size_t start = lastDot;
		while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
							 textBeforeCursor[start - 1] == '_' ||
							 textBeforeCursor[start - 1] == '.')) {
			start--;
		}
		std::string prefix = textBeforeCursor.substr(start, lastDot - start);

		// Get the last identifier (for member access check)
		size_t lastDotInPrefix = prefix.find_last_of('.');
		std::string identifierName = (lastDotInPrefix != std::string::npos)
			? prefix.substr(lastDotInPrefix + 1)
			: prefix;

		// Check semantic cache for variable type
		auto cacheIt = semanticCache_.find(doc->uri);
		if (cacheIt != semanticCache_.end()) {
			const SemanticInfo &semInfo = cacheIt->second;
			auto varIt = semInfo.variables.find(identifierName);

			if (varIt != semInfo.variables.end()) {
				return getMemberCompletions(varIt->second.type, semInfo);
			}

			// Check for enum type
			auto enumIt = semInfo.enumDetails.find(identifierName);
			if (enumIt != semInfo.enumDetails.end()) {
				return getMemberCompletions(identifierName, semInfo);
			}
		}

		// Check stdlib enums
		auto stdlibEnumIt = stdlibEnumsByName_.find(identifierName);
		if (stdlibEnumIt != stdlibEnumsByName_.end()) {
			auto cacheIt2 = semanticCache_.find(doc->uri);
			if (cacheIt2 != semanticCache_.end()) {
				return getMemberCompletions(identifierName, cacheIt2->second);
			}
		}

		// Try qualified name completions (stdlib.fmt, etc.)
		return getQualifiedNameCompletions(prefix);
	}

	// No dot context - provide regular completions

	// Add "stdlib" as root namespace
	items.push_back({"stdlib", lsp::CompletionItemKind::Module, "Standard library namespace", ""});

	// Add keyword completions from compiler API
	auto keywordItems = getKeywordCompletions("");
	items.insert(items.end(), keywordItems.begin(), keywordItems.end());

	// Add stdlib function completions
	for (const auto &func : stdlibSymbols_.functions) {
		lsp::CompletionItem item;
		item.label = func.name;
		item.kind = lsp::CompletionItemKind::Function;
		item.detail = func.type;  // type contains the signature
		item.documentation = func.documentation;
		items.push_back(item);
	}

	// Add identifiers from the current document
	try {
		Lexer lexer(doc->text);
		auto tokens = lexer.tokenize();

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
		// If lexing fails, continue without identifiers
	}

	return items;
}

std::vector<lsp::CompletionItem> Analyzer::getMemberCompletions(const std::string &typeName, const SemanticInfo &semInfo) {
	std::vector<lsp::CompletionItem> items;

	// Check type members registry
	auto typeIt = typeMembers_.find(typeName);
	if (typeIt != typeMembers_.end()) {
		for (const auto &member : typeIt->second) {
			lsp::CompletionItem item;
			item.label = member.name;
			item.kind = member.isMethod ? lsp::CompletionItemKind::Method : lsp::CompletionItemKind::Property;
			item.detail = member.isMethod ? member.signature + " " + member.returnType : member.returnType;
			item.documentation = member.documentation;
			if (member.isMethod) {
				item.insertText = member.name + "()";
			}
			items.push_back(item);
		}
		return items;
	}

	// Check struct type from semantic cache
	auto structIt = semInfo.structs.find(typeName);
	if (structIt != semInfo.structs.end()) {
		for (const auto &field : structIt->second.fields) {
			lsp::CompletionItem item;
			item.label = field.name;
			item.kind = lsp::CompletionItemKind::Field;
			item.detail = field.type;
			item.documentation = "Field of struct " + typeName;
			items.push_back(item);
		}
		return items;
	}

	// Check array type
	if (!typeName.empty() && typeName[0] == '[') {
		auto arrayIt = typeMembers_.find("[]");
		if (arrayIt != typeMembers_.end()) {
			for (const auto &member : arrayIt->second) {
				lsp::CompletionItem item;
				item.label = member.name;
				item.kind = member.isMethod ? lsp::CompletionItemKind::Method : lsp::CompletionItemKind::Property;
				item.detail = member.isMethod ? member.signature + " " + member.returnType : member.returnType;
				item.documentation = member.documentation;
				if (member.isMethod) {
					item.insertText = member.name + "()";
				}
				items.push_back(item);
			}
		}
		return items;
	}

	return items;
}

std::vector<lsp::CompletionItem> Analyzer::getQualifiedNameCompletions(const std::string &prefix) {
	std::vector<lsp::CompletionItem> items;

	// Split prefix by dots
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

	if (parts.empty()) {
		return items;
	}

	// Navigate namespace hierarchy
	if (parts[0] == "stdlib") {
		if (parts.size() == 1) {
			// After "stdlib." - show top-level namespaces
			const NamespaceNode *node = &namespaceRoot_;
			for (const auto &child : node->children) {
				lsp::CompletionItem item;
				item.label = child.first;
				item.kind = lsp::CompletionItemKind::Module;
				item.detail = "stdlib." + child.first + " namespace";
				items.push_back(item);
			}
		} else if (parts.size() == 2) {
			// After "stdlib.fmt." - show modules
			const NamespaceNode *node = &namespaceRoot_;
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
			const NamespaceNode *node = &namespaceRoot_;
			auto it1 = node->children.find(parts[1]);
			if (it1 != node->children.end()) {
				auto it2 = it1->second.children.find(parts[2]);
				if (it2 != it1->second.children.end()) {
					for (const auto &funcName : it2->second.functions) {
						auto funcIt = stdlibFunctionsByName_.find(funcName);
						if (funcIt != stdlibFunctionsByName_.end()) {
							lsp::CompletionItem item;
							item.label = funcIt->second->name;
							item.kind = lsp::CompletionItemKind::Function;
							item.detail = funcIt->second->type;
							item.documentation = funcIt->second->documentation;
							items.push_back(item);
						}
					}
				}
			}
		}
	}

	return items;
}

// ============================================================================
// Hover
// ============================================================================

// Helper: check if position is inside a string or character literal
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

	bool inString = false;
	bool inChar = false;
	int literalStart = -1;

	for (int i = 0; i < (int)line.length(); i++) {
		char c = line[i];
		char prev = (i > 0) ? line[i - 1] : '\0';

		if (prev == '\\') continue;

		if (c == '"' && !inChar) {
			if (!inString) {
				inString = true;
				literalStart = i;
			} else {
				inString = false;
				if (pos.character > literalStart && pos.character <= i) {
					return 1;
				}
			}
		} else if (c == '\'' && !inString) {
			if (!inChar) {
				inChar = true;
				literalStart = i;
			} else {
				inChar = false;
				if (pos.character > literalStart && pos.character <= i) {
					return 2;
				}
			}
		}
	}

	if (inString && pos.character > literalStart) return 1;
	if (inChar && pos.character > literalStart) return 2;

	return 0;
}

// Helper: get numeric literal type
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

	char cursorChar = line[pos.character];
	if (!std::isdigit(cursorChar)) return 0;

	int start = pos.character;
	int end = pos.character;

	while (start > 0 && (std::isdigit(line[start - 1]) || line[start - 1] == '.')) {
		start--;
	}

	while (end < (int)line.length() && (std::isdigit(line[end]) || line[end] == '.' || line[end] == 'b')) {
		end++;
	}

	if (start > 0 && (std::isalpha(line[start - 1]) || line[start - 1] == '_')) return 0;
	if (end < (int)line.length() && (std::isalpha(line[end]) || line[end] == '_')) return 0;

	if (start >= end) return 0;

	std::string literal = line.substr(start, end - start);
	if (literal.empty() || !std::isdigit(literal[0])) return 0;

	if (literal.size() > 1 && literal.back() == 'b') return 3;
	if (literal.find('.') != std::string::npos) return 2;

	return 1;
}

std::optional<lsp::Hover> Analyzer::getHover(std::shared_ptr<Document> doc, lsp::Position pos) {
	// Check literals
	int literalType = getLiteralTypeAtPosition(doc->text, pos);
	if (literalType == 1) {
		return lsp::Hover{"```maxon\nstring\n```\n\nString literal"};
	} else if (literalType == 2) {
		return lsp::Hover{"```maxon\nchar\n```\n\nCharacter literal"};
	}

	int numericType = getNumericLiteralType(doc->text, pos);
	if (numericType == 1) {
		return lsp::Hover{"```maxon\nint\n```\n\nInteger literal"};
	} else if (numericType == 2) {
		return lsp::Hover{"```maxon\nfloat\n```\n\nFloating-point literal"};
	} else if (numericType == 3) {
		return lsp::Hover{"```maxon\nbyte\n```\n\nByte literal"};
	}

	std::string word = getWordAtPosition(doc->text, pos);
	if (word.empty()) return std::nullopt;

	lsp::Hover hover;
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);

	// Check member access
	size_t lastDot = textBeforeCursor.find_last_of('.');
	if (lastDot != std::string::npos) {
		std::string afterDot = textBeforeCursor.substr(lastDot + 1);
		if (word.find(afterDot) == 0 || afterDot.find(word) == 0) {
			size_t start = lastDot;
			while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
								 textBeforeCursor[start - 1] == '_')) {
				start--;
			}
			std::string objectName = textBeforeCursor.substr(start, lastDot - start);

			auto cacheIt = semanticCache_.find(doc->uri);
			if (cacheIt != semanticCache_.end()) {
				const SemanticInfo &semInfo = cacheIt->second;
				auto varIt = semInfo.variables.find(objectName);

				if (varIt != semInfo.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check struct field
					auto structIt = semInfo.structs.find(typeName);
					if (structIt != semInfo.structs.end()) {
						for (const auto &field : structIt->second.fields) {
							if (field.name == word) {
								std::string mutability = field.isImmutable ? "let" : "var";
								hover.contents = "```maxon\n(field) " + mutability + " " +
									field.name + ": " + field.type + "\n```\n\nField of struct `" + typeName + "`";
								return hover;
							}
						}
					}

					// Check array properties
					if (!typeName.empty() && typeName[0] == '[') {
						if (word == "length") {
							hover.contents = "```maxon\n(property) length: int\n```\n\nNumber of elements in the array";
							return hover;
						}
					}

					// Check type members
					auto typeIt = typeMembers_.find(typeName);
					if (typeIt != typeMembers_.end()) {
						for (const auto &member : typeIt->second) {
							if (member.name == word) {
								if (member.isMethod) {
									hover.contents = "```maxon\n(method) " + word + member.signature +
										" " + member.returnType + "\n```\n\n" + member.documentation;
								} else {
									hover.contents = "```maxon\n(property) " + word + ": " +
										member.returnType + "\n```\n\n" + member.documentation;
								}
								return hover;
							}
						}
					}
				}
			}
		}
	}

	// Check semantic cache
	auto cacheIt = semanticCache_.find(doc->uri);
	if (cacheIt != semanticCache_.end()) {
		const SemanticInfo &semInfo = cacheIt->second;

		// Check variable
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

		// Check function
		auto funcIt = semInfo.functions.find(word);
		if (funcIt != semInfo.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;
			std::string sig = "function " + funcInfo.name + "(";
			bool first = true;
			for (const auto &param : funcInfo.parameters) {
				if (param.name == "self") continue;
				if (!first) sig += ", ";
				sig += param.name + " " + param.type;
				first = false;
			}
			sig += ") " + funcInfo.returnType;
			hover.contents = "```maxon\n" + sig + "\n```";
			return hover;
		}

		// Check struct
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

		// Check enum
		auto enumIt = semInfo.enumDetails.find(word);
		if (enumIt != semInfo.enumDetails.end()) {
			const auto &enumInfo = enumIt->second;
			std::string enumDef = "enum " + enumInfo.name;
			if (!enumInfo.rawValueType.empty()) {
				enumDef += " " + enumInfo.rawValueType;
			}
			enumDef += "\n";
			for (const auto &caseInfo : enumInfo.cases) {
				enumDef += "    case " + caseInfo.name;
				if (!caseInfo.associatedValues.empty()) {
					enumDef += "(";
					bool first = true;
					for (const auto &av : caseInfo.associatedValues) {
						if (!first) enumDef += ", ";
						enumDef += av.first + " " + av.second;
						first = false;
					}
					enumDef += ")";
				}
				enumDef += "\n";
			}
			enumDef += "end";
			hover.contents = "```maxon\n" + enumDef + "\n```";
			return hover;
		}
	}

	// Check stdlib functions
	auto funcIt = stdlibFunctionsByName_.find(word);
	if (funcIt != stdlibFunctionsByName_.end()) {
		hover.contents = "**" + funcIt->second->name + "**\n\n```maxon\n" +
			funcIt->second->type + "\n```\n\n" + funcIt->second->documentation;
		return hover;
	}

	// Check keywords from compiler API
	for (const auto &kw : keywordInfo_) {
		if (kw.name == word) {
			// Special handling for math intrinsics - show function signature
			if (kw.category == KeywordCategory::MathIntrinsic) {
				std::string sig = "function " + word + "(x float) " + kw.returnType;
				hover.contents = "```maxon\n" + sig + "\n```\n\n(math intrinsic) " + kw.documentation;
			} else {
				hover.contents = "**" + word + "** (keyword)\n\n" + kw.documentation;
			}
			return hover;
		}
	}

	// Check compiler intrinsics
	const IntrinsicInfo *intrinsic = IntrinsicRegistry::instance().lookup(word);
	if (intrinsic) {
		std::string sig = "function " + intrinsic->name + "(";
		for (size_t i = 0; i < intrinsic->params.size(); i++) {
			if (i > 0) sig += ", ";
			const auto &param = intrinsic->params[i];
			if (!param.allowedTypes.empty()) {
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
		return hover;
	}

	hover.contents = "**" + word + "**\n\nIdentifier";
	return hover;
}

// ============================================================================
// Definition
// ============================================================================

std::optional<lsp::Location> Analyzer::getDefinition(std::shared_ptr<Document> doc, lsp::Position pos) {
	std::string word = getWordAtPosition(doc->text, pos);
	if (word.empty()) return std::nullopt;

	// Check member access
	std::string textBeforeCursor = getTextBeforePosition(doc->text, pos);
	size_t lastDot = textBeforeCursor.find_last_of('.');

	if (lastDot != std::string::npos) {
		std::string afterDot = textBeforeCursor.substr(lastDot + 1);
		if (word.find(afterDot) == 0 || afterDot.find(word) == 0) {
			size_t start = lastDot;
			while (start > 0 && (std::isalnum(textBeforeCursor[start - 1]) ||
								 textBeforeCursor[start - 1] == '_')) {
				start--;
			}
			std::string objectName = textBeforeCursor.substr(start, lastDot - start);

			auto cacheIt = semanticCache_.find(doc->uri);
			if (cacheIt != semanticCache_.end()) {
				const SemanticInfo &semInfo = cacheIt->second;
				auto varIt = semInfo.variables.find(objectName);

				if (varIt != semInfo.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check struct field
					auto structIt = semInfo.structs.find(typeName);
					if (structIt != semInfo.structs.end()) {
						for (const auto &field : structIt->second.fields) {
							if (field.name == word) {
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
				}
			}
		}
	}

	// Check semantic cache
	auto cacheIt = semanticCache_.find(doc->uri);
	if (cacheIt != semanticCache_.end()) {
		const SemanticInfo &semInfo = cacheIt->second;

		// Check variable
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

		// Check function
		auto funcIt = semInfo.functions.find(word);
		if (funcIt != semInfo.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;
			if (funcInfo.line > 0) {
				lsp::Location loc;
				loc.uri = doc->uri;
				loc.range.start.line = funcInfo.line - 1;
				loc.range.start.character = funcInfo.column > 0 ? funcInfo.column - 1 : 0;
				loc.range.end.line = loc.range.start.line;
				loc.range.end.character = loc.range.start.character + word.length();
				return loc;
			}
		}

		// Check struct
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

		// Check interface
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

		// Check enum
		auto enumIt = semInfo.enumDetails.find(word);
		if (enumIt != semInfo.enumDetails.end()) {
			const auto &enumInfo = enumIt->second;
			lsp::Location loc;
			loc.uri = doc->uri;
			loc.range.start.line = enumInfo.line > 0 ? enumInfo.line - 1 : 0;
			loc.range.start.character = enumInfo.column > 0 ? enumInfo.column - 1 : 0;
			loc.range.end.line = loc.range.start.line;
			loc.range.end.character = loc.range.start.character + enumInfo.name.length();
			return loc;
		}
	}

	// Check stdlib symbols
	auto stdlibFuncIt = stdlibFunctionsByName_.find(word);
	if (stdlibFuncIt != stdlibFunctionsByName_.end()) {
		const LSPSymbolInfo *sym = stdlibFuncIt->second;
		if (sym->sourceRange.startLine > 0) {
			lsp::Location loc;
			// Need to find the file path from the symbol
			// For now, we can't navigate to stdlib definitions without file path
		}
	}

	auto stdlibStructIt = stdlibStructsByName_.find(word);
	if (stdlibStructIt != stdlibStructsByName_.end()) {
		// Similar - need file path
	}

	// Fallback: tokenize and search for declaration
	try {
		Lexer lexer(doc->text);
		auto tokens = lexer.tokenize();

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
	}

	return std::nullopt;
}

// ============================================================================
// Document Symbols
// ============================================================================

std::vector<lsp::SymbolInformation> Analyzer::getSymbols(std::shared_ptr<Document> doc) {
	std::vector<lsp::SymbolInformation> symbols;

	try {
		Lexer lexer(doc->text);
		auto tokens = lexer.tokenize();

		for (size_t i = 0; i < tokens.size() - 1; i++) {
			if (tokens[i].value == "function" && tokens[i + 1].type == TokenType::IDENTIFIER) {
				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Function;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			} else if (tokens[i].value == "var" && tokens[i + 1].type == TokenType::IDENTIFIER) {
				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Variable;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			} else if (tokens[i].value == "struct" && tokens[i + 1].type == TokenType::IDENTIFIER) {
				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Struct;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			} else if (tokens[i].value == "enum" && tokens[i + 1].type == TokenType::IDENTIFIER) {
				lsp::SymbolInformation sym;
				sym.name = tokens[i + 1].value;
				sym.kind = lsp::SymbolKind::Enum;
				sym.location.uri = doc->uri;
				sym.location.range = tokenToRange(tokens[i + 1]);
				symbols.push_back(sym);
			}
		}
	} catch (...) {
	}

	return symbols;
}

// ============================================================================
// Rename
// ============================================================================

std::optional<lsp::WorkspaceEdit> Analyzer::getRename(std::shared_ptr<Document> doc, lsp::Position pos, const std::string &newName) {
	try {
		Lexer lexer(doc->text);
		auto tokens = lexer.tokenize();

		Token *targetToken = nullptr;
		size_t targetIndex = 0;
		for (size_t i = 0; i < tokens.size(); i++) {
			auto &token = tokens[i];
			int tokenLine = token.line - 1;
			int tokenCol = token.column - 1;
			int tokenEndCol = tokenCol + token.value.length();

			if (pos.line == tokenLine && pos.character >= tokenCol && pos.character < tokenEndCol) {
				targetToken = &token;
				targetIndex = i;
				break;
			}
		}

		if (!targetToken) return std::nullopt;

		std::vector<lsp::TextEdit> edits;

		if (targetToken->type == TokenType::BLOCK_ID) {
			std::string oldName = targetToken->value;
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
		} else if (targetToken->type == TokenType::IDENTIFIER && targetIndex > 0) {
			if (tokens[targetIndex - 1].value == "struct") {
				std::string oldName = targetToken->value;
				for (size_t i = 0; i < tokens.size(); i++) {
					const auto &token = tokens[i];
					if (token.type == TokenType::IDENTIFIER && token.value == oldName) {
						bool isTypeUsage = false;
						if (i > 0) {
							TokenType prevType = tokens[i - 1].type;
							if (tokens[i - 1].value == "struct") {
								isTypeUsage = true;
							} else if (prevType == TokenType::RPAREN) {
								isTypeUsage = true;
							} else if (prevType == TokenType::IDENTIFIER && i > 1) {
								TokenType prevPrevType = tokens[i - 2].type;
								if (tokens[i - 2].value == "var" ||
									tokens[i - 2].value == "let" ||
									prevPrevType == TokenType::COMMA ||
									prevPrevType == TokenType::LPAREN) {
									isTypeUsage = true;
								}
							} else if (prevType == TokenType::RBRACKET) {
								isTypeUsage = true;
							}
						}
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
					} else if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
						lsp::TextEdit edit;
						edit.range.start.line = token.line - 1;
						edit.range.start.character = token.column - 1;
						edit.range.end.line = token.line - 1;
						edit.range.end.character = token.column - 1 + token.value.length() + 2;
						edit.newText = "'" + newName + "'";
						edits.push_back(edit);
					}
				}
			} else {
				return std::nullopt;
			}
		} else {
			return std::nullopt;
		}

		if (edits.empty()) return std::nullopt;

		lsp::WorkspaceEdit workspaceEdit;
		workspaceEdit.changes[doc->uri] = edits;
		return workspaceEdit;
	} catch (...) {
		return std::nullopt;
	}
}

// ============================================================================
// Linked Editing Ranges
// ============================================================================

std::optional<std::vector<lsp::Range>> Analyzer::getLinkedEditingRanges(std::shared_ptr<Document> doc, lsp::Position pos) {
	try {
		Lexer lexer(doc->text);
		auto tokens = lexer.tokenize();

		Token *targetToken = nullptr;
		size_t targetIndex = 0;
		for (size_t i = 0; i < tokens.size(); i++) {
			auto &token = tokens[i];
			int tokenLine = token.line - 1;
			int tokenCol = token.column - 1;

			if (token.type == TokenType::BLOCK_ID) {
				int tokenStartCol = tokenCol + 1;
				int tokenEndCol = tokenStartCol + token.value.length();
				if (pos.line == tokenLine && pos.character >= tokenStartCol && pos.character < tokenEndCol) {
					targetToken = &token;
					targetIndex = i;
					break;
				}
			} else {
				int tokenEndCol = tokenCol + token.value.length();
				if (pos.line == tokenLine && pos.character >= tokenCol && pos.character < tokenEndCol) {
					targetToken = &token;
					targetIndex = i;
					break;
				}
			}
		}

		if (!targetToken) return std::nullopt;

		std::vector<lsp::Range> ranges;

		if (targetToken->type == TokenType::BLOCK_ID) {
			std::string oldName = targetToken->value;
			for (const auto &token : tokens) {
				if (token.type == TokenType::BLOCK_ID && token.value == oldName) {
					lsp::Range range;
					range.start.line = token.line - 1;
					range.start.character = token.column;
					range.end.line = token.line - 1;
					range.end.character = token.column + token.value.length();
					ranges.push_back(range);
				}
			}
		} else if (targetToken->type == TokenType::IDENTIFIER && targetIndex > 0) {
			if (tokens[targetIndex - 1].value == "function") {
				std::string functionName = targetToken->value;

				// Find scope boundaries
				size_t structStartIndex = 0;
				size_t structEndIndex = tokens.size();
				bool isInsideStruct = false;
				int depth = 0;

				for (size_t i = targetIndex; i > 0; i--) {
					const auto &tok = tokens[i - 1];
					if (tok.value == "end") {
						depth++;
					} else if (tok.value == "struct") {
						if (depth == 0) {
							isInsideStruct = true;
							structStartIndex = i - 1;
							break;
						}
						depth--;
					}
				}

				if (isInsideStruct) {
					depth = 1;
					for (size_t i = structStartIndex + 1; i < tokens.size(); i++) {
						const auto &tok = tokens[i];
						if (tok.value == "struct" || tok.value == "interface" ||
							tok.value == "function" || tok.value == "if" ||
							tok.value == "while" || tok.value == "for") {
							depth++;
						} else if (tok.value == "end") {
							depth--;
							if (depth == 0) {
								structEndIndex = i + 2;
								if (structEndIndex > tokens.size()) {
									structEndIndex = tokens.size();
								}
								break;
							}
						}
					}
				}

				for (size_t i = 0; i < tokens.size(); i++) {
					if (isInsideStruct && (i < structStartIndex || i >= structEndIndex)) {
						continue;
					}
					const auto &token = tokens[i];
					if (token.type == TokenType::IDENTIFIER && token.value == functionName &&
						i > 0 && tokens[i - 1].value == "function") {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column - 1;
						range.end.line = token.line - 1;
						range.end.character = token.column - 1 + token.value.length();
						ranges.push_back(range);
					} else if (token.type == TokenType::BLOCK_ID && token.value == functionName) {
						lsp::Range range;
						range.start.line = token.line - 1;
						range.start.character = token.column;
						range.end.line = token.line - 1;
						range.end.character = token.column + token.value.length();
						ranges.push_back(range);
					}
				}
			} else {
				return std::nullopt;
			}
		} else {
			return std::nullopt;
		}

		if (ranges.empty()) return std::nullopt;
		return ranges;
	} catch (...) {
		return std::nullopt;
	}
}

// ============================================================================
// Helpers
// ============================================================================

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

	if (pos.character >= (int)line.length()) {
		return line;
	}

	return line.substr(0, pos.character);
}

std::string Analyzer::findContainingStruct(const std::string &text, lsp::Position pos) {
	std::istringstream stream(text);
	std::string line;
	int currentLine = 0;

	std::string currentStruct;
	int structDepth = 0;
	bool inFunction = false;
	int functionDepth = 0;

	while (std::getline(stream, line) && currentLine <= pos.line) {
		size_t firstNonSpace = line.find_first_not_of(" \t");
		if (firstNonSpace == std::string::npos) {
			currentLine++;
			continue;
		}
		std::string trimmedLine = line.substr(firstNonSpace);

		if (trimmedLine.find("struct ") == 0) {
			size_t nameStart = 7;
			size_t nameEnd = nameStart;
			while (nameEnd < trimmedLine.length() &&
				   (std::isalnum(trimmedLine[nameEnd]) || trimmedLine[nameEnd] == '_')) {
				nameEnd++;
			}
			currentStruct = trimmedLine.substr(nameStart, nameEnd - nameStart);
			structDepth = 1;
		} else if (structDepth > 0 &&
				   (trimmedLine.find("function ") == 0 || trimmedLine.find("export function ") == 0)) {
			inFunction = true;
			functionDepth = 1;
		} else if (trimmedLine.find("end ") == 0 || trimmedLine == "end") {
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

	if (structDepth > 0 && inFunction) {
		return currentStruct;
	}

	return "";
}

bool Analyzer::isKeyword(const std::string &word) const {
	for (const auto &kw : keywordInfo_) {
		if (kw.name == word) {
			return true;
		}
	}
	return false;
}

lsp::Range Analyzer::tokenToRange(const Token &token) {
	lsp::Range range;
	range.start.line = token.line - 1;
	range.start.character = token.column - 1;
	range.end.line = token.line - 1;
	range.end.character = token.column - 1 + token.value.length();
	return range;
}
