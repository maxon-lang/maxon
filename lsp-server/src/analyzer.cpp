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
		typeMembers_["[]"].push_back({member.name,
									  member.isMethod,
									  member.returnType,
									  member.signature,
									  member.documentation});
	}
}

void Analyzer::initializeStdlib(const std::string &stdlibPath) {
	stdlibPath_ = stdlibPath;

	// Use compiler API to load stdlib symbols
	stdlibSymbols_ = loadStdlib(stdlibPath);

	// Build lookup tables and namespace hierarchy
	buildStdlibLookups();
	buildNamespaceHierarchy();

	// Build type members for stdlib types (for dot-completion)
	buildStdlibTypeMembers();
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
	// Build namespace hierarchy from stdlib symbols based on file paths
	namespaceRoot_ = NamespaceNode{"stdlib", {}, {}, {}, {}, {}};

	if (stdlibPath_.empty()) {
		return;
	}

	// Normalize the stdlib path for comparison
	fs::path stdlibBasePath;
	try {
		stdlibBasePath = fs::canonical(stdlibPath_);
	} catch (...) {
		stdlibBasePath = fs::path(stdlibPath_);
	}

	// Helper lambda to get namespace path from a file path
	// Returns empty string for root-level files, or the subdirectory name for nested files
	auto getNamespacePath = [&stdlibBasePath](const std::string &filePath) -> std::string {
		if (filePath.empty()) {
			return "";
		}

		fs::path absPath;
		try {
			absPath = fs::canonical(filePath);
		} catch (...) {
			absPath = fs::path(filePath);
		}

		// Get relative path from stdlib base
		std::string absStr = absPath.string();
		std::string baseStr = stdlibBasePath.string();

		// Normalize path separators for comparison
		std::replace(absStr.begin(), absStr.end(), '\\', '/');
		std::replace(baseStr.begin(), baseStr.end(), '\\', '/');

		// Check if the file is under stdlib path
		if (absStr.find(baseStr) != 0) {
			return "";
		}

		// Get the relative path (remove stdlib base + separator)
		std::string relPath = absStr.substr(baseStr.length());
		if (!relPath.empty() && relPath[0] == '/') {
			relPath = relPath.substr(1);
		}

		// Extract directory portion (everything before the filename)
		size_t lastSlash = relPath.find_last_of('/');
		if (lastSlash == std::string::npos) {
			// Root-level file (no subdirectory)
			return "";
		}

		return relPath.substr(0, lastSlash);
	};

	// Helper lambda to check if a file/symbol should be skipped (starts with _)
	auto shouldSkip = [](const std::string &name) -> bool {
		if (name.empty())
			return false;

		// Check the last component of the path
		size_t lastSlash = name.find_last_of('/');
		std::string basename = (lastSlash == std::string::npos) ? name : name.substr(lastSlash + 1);

		// Skip if filename or symbol starts with underscore
		return !basename.empty() && basename[0] == '_';
	};

	// Helper lambda to get or create a namespace node for a path
	auto getOrCreateNode = [this](const std::string &namespacePath) -> NamespaceNode * {
		if (namespacePath.empty()) {
			return &namespaceRoot_;
		}

		// Split the namespace path by '/'
		std::vector<std::string> parts;
		std::string current;
		for (char c : namespacePath) {
			if (c == '/') {
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

		// Navigate/create the hierarchy
		NamespaceNode *node = &namespaceRoot_;
		for (const auto &part : parts) {
			auto it = node->children.find(part);
			if (it == node->children.end()) {
				node->children[part] = NamespaceNode{part, {}, {}, {}, {}, {}};
			}
			node = &node->children[part];
		}

		return node;
	};

	// Process functions
	for (const auto &func : stdlibSymbols_.functions) {
		// Skip internal symbols (starting with _)
		if (shouldSkip(func.name)) {
			continue;
		}

		std::string namespacePath = getNamespacePath(func.filePath);

		// Skip files starting with _ (like _grapheme.maxon)
		if (shouldSkip(namespacePath)) {
			continue;
		}

		NamespaceNode *node = getOrCreateNode(namespacePath);
		node->functions.push_back(func.name);
	}

	// Process structs
	for (const auto &structSym : stdlibSymbols_.structs) {
		if (shouldSkip(structSym.name)) {
			continue;
		}

		std::string namespacePath = getNamespacePath(structSym.filePath);
		if (shouldSkip(namespacePath)) {
			continue;
		}

		NamespaceNode *node = getOrCreateNode(namespacePath);
		node->structs.push_back(structSym.name);
	}

	// Process enums
	for (const auto &enumSym : stdlibSymbols_.enums) {
		if (shouldSkip(enumSym.name)) {
			continue;
		}

		std::string namespacePath = getNamespacePath(enumSym.filePath);
		if (shouldSkip(namespacePath)) {
			continue;
		}

		NamespaceNode *node = getOrCreateNode(namespacePath);
		node->enums.push_back(enumSym.name);
	}

	// Process interfaces
	for (const auto &ifaceSym : stdlibSymbols_.interfaces) {
		if (shouldSkip(ifaceSym.name)) {
			continue;
		}

		std::string namespacePath = getNamespacePath(ifaceSym.filePath);
		if (shouldSkip(namespacePath)) {
			continue;
		}

		NamespaceNode *node = getOrCreateNode(namespacePath);
		node->interfaces.push_back(ifaceSym.name);
	}
}

void Analyzer::buildStdlibTypeMembers() {
	// Build type members for stdlib types from method signatures
	// Methods have kind == "method" and type field like "function TypeName.methodName(params) returnType"

	for (const auto &func : stdlibSymbols_.functions) {
		if (func.kind != "method") {
			continue;
		}

		// Parse the type field to extract struct name
		// Format: "function TypeName.methodName(params) returnType"
		const std::string &typeField = func.type;

		// Skip if doesn't start with "function "
		if (typeField.find("function ") != 0) {
			continue;
		}

		// Find the dot that separates TypeName from methodName
		size_t funcStart = 9; // Skip "function "
		size_t dotPos = typeField.find('.', funcStart);
		if (dotPos == std::string::npos) {
			continue; // Not a method signature
		}

		// Extract the struct/type name
		std::string typeName = typeField.substr(funcStart, dotPos - funcStart);

		// Skip if type name is empty or starts with underscore (internal)
		if (typeName.empty() || typeName[0] == '_') {
			continue;
		}

		// Build parameter signature from the parameters array
		std::string signature = "(";
		bool first = true;
		for (const auto &param : func.parameters) {
			if (!first)
				signature += ", ";
			signature += param.name + " " + param.type;
			first = false;
		}
		signature += ")";

		// Create TypeMember entry
		TypeMember member;
		member.name = func.name;
		member.isMethod = true;
		member.returnType = func.returnType;
		member.signature = signature;
		member.documentation = func.documentation;

		// Check if we already have this member (avoid duplicates)
		auto &members = typeMembers_[typeName];
		bool exists = false;
		for (const auto &existing : members) {
			if (existing.name == member.name) {
				exists = true;
				break;
			}
		}

		if (!exists) {
			members.push_back(member);
		}
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
	lastAnalysisTime_.clear();
}

void Analyzer::invalidateDocumentCache(const std::string &uri) {
	// Clear cache for a specific document
	documentCaches_.erase(uri);
	lastGoodCaches_.erase(uri);
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
					   now - lastTimeIt->second)
					   .count();

	if (elapsed < minDelayMs) {
		return true; // Throttle - too soon since last analysis
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

	// Use the overloaded analyzeForLSP that accepts stdlib symbols
	// This ensures stdlib functions are registered with the semantic analyzer
	LSPAnalysisResult result = analyzeForLSP(doc->text, filename, stdlibSymbols_);

	auto endTime = std::chrono::steady_clock::now();
	int64_t analysisMs = std::chrono::duration_cast<std::chrono::milliseconds>(
							 endTime - startTime)
							 .count();

	// Create document cache entry from analysis result
	DocumentCache cache;
	cache.ast = std::move(result.ast);
	cache.symbols = std::move(result.symbols);
	cache.parseErrors = std::move(result.parseErrors);
	cache.semanticErrors = std::move(result.semanticErrors);
	cache.lastAnalysisMs = analysisMs;
	cache.version = doc->version;

	// Move semantic info from analysis result (no duplicate analysis needed!)
	cache.variables = std::move(result.variables);
	cache.functions = std::move(result.functions);
	cache.structs = std::move(result.structs);
	cache.interfaces = std::move(result.interfaces);

	// Extract enum details from AST for hover and type members (for dot-completion)
	if (cache.ast) {
		for (const auto &enumDef : cache.ast->enums) {
			// Build enum details for hover using EnumInfo from semantic_analyzer.h
			::EnumInfo enumInfo(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);
			int tagValue = 0;
			for (const auto &caseDef : enumDef->cases) {
				::EnumCaseInfo caseInfo(caseDef.name, tagValue++, caseDef.line, caseDef.column);
				caseInfo.hasRawValue = (caseDef.rawValue != nullptr);
				for (const auto &av : caseDef.associatedValues) {
					caseInfo.associatedValues.push_back({av.name, av.type});
				}
				enumInfo.cases.push_back(caseInfo);
			}
			cache.enumDetails[enumDef->name] = std::move(enumInfo);

			// Build type members for dot-completion
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
						if (!first)
							sig += ", ";
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
					if (param.name == "self")
						continue;
					if (!first)
						sig += ", ";
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
					if (param.name == "self")
						continue;
					if (!first)
						sig += ", ";
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

		// Check document cache for variable type
		std::string varType;
		static const std::map<std::string, StructInfo> emptyStructs;

		auto cacheIt = documentCaches_.find(doc->uri);
		if (cacheIt != documentCaches_.end()) {
			const DocumentCache &cache = cacheIt->second;
			auto varIt = cache.variables.find(identifierName);

			if (varIt != cache.variables.end()) {
				varType = varIt->second.type;
			}

			// Check for enum type (even if not a variable) - check typeMembers_ which is populated from AST
			if (varType.empty()) {
				auto enumIt = typeMembers_.find(identifierName);
				if (enumIt != typeMembers_.end()) {
					return getMemberCompletions(identifierName, cache.structs);
				}
			}
		}

		// Check stdlib enums
		if (varType.empty()) {
			auto stdlibEnumIt = stdlibEnumsByName_.find(identifierName);
			if (stdlibEnumIt != stdlibEnumsByName_.end()) {
				if (cacheIt != documentCaches_.end()) {
					return getMemberCompletions(identifierName, cacheIt->second.structs);
				}
				return getMemberCompletions(identifierName, emptyStructs);
			}
		}

		// If type is empty or not resolved, try to infer from initialization
		if (varType.empty()) {
			varType = inferTypeFromInit(doc->text, identifierName);
		}

		// If we found a type, get member completions
		if (!varType.empty()) {
			if (cacheIt != documentCaches_.end()) {
				return getMemberCompletions(varType, cacheIt->second.structs);
			}
			return getMemberCompletions(varType, emptyStructs);
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
		item.detail = func.type; // type contains the signature
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

std::vector<lsp::CompletionItem> Analyzer::getMemberCompletions(const std::string &typeName, const std::map<std::string, StructInfo> &structs) {
	std::vector<lsp::CompletionItem> items;

	// Helper lambda to add members from type registry
	auto addMembersFromRegistry = [&items](const std::vector<TypeMember> &members) {
		for (const auto &member : members) {
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
	};

	// First, try exact type name match in type members registry
	auto typeIt = typeMembers_.find(typeName);
	if (typeIt != typeMembers_.end()) {
		addMembersFromRegistry(typeIt->second);
		return items;
	}

	// Try normalized type name (strips parameterization like map<int,int> -> map)
	std::string normalizedType = normalizeTypeForLookup(typeName);
	if (normalizedType != typeName) {
		typeIt = typeMembers_.find(normalizedType);
		if (typeIt != typeMembers_.end()) {
			addMembersFromRegistry(typeIt->second);
			return items;
		}
	}

	// Check struct type from document cache
	auto structIt = structs.find(typeName);
	if (structIt != structs.end()) {
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

	// Check array type (starts with '[')
	if (!typeName.empty() && typeName[0] == '[') {
		auto arrayIt = typeMembers_.find("[]");
		if (arrayIt != typeMembers_.end()) {
			addMembersFromRegistry(arrayIt->second);
		}
		return items;
	}

	// Check if this is a stdlib struct type that has methods registered
	// The type might be a type alias like "string" which maps to the string struct
	auto stdlibStructIt = stdlibStructsByName_.find(typeName);
	if (stdlibStructIt != stdlibStructsByName_.end()) {
		// Look up methods for this stdlib type in typeMembers_
		typeIt = typeMembers_.find(typeName);
		if (typeIt != typeMembers_.end()) {
			addMembersFromRegistry(typeIt->second);
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

	if (parts.empty() || parts[0] != "stdlib") {
		return items;
	}

	// Navigate to the target namespace node
	const NamespaceNode *node = &namespaceRoot_;
	for (size_t i = 1; i < parts.size(); ++i) {
		auto it = node->children.find(parts[i]);
		if (it == node->children.end()) {
			return items; // Path not found
		}
		node = &it->second;
	}

	// Build namespace path string for detail descriptions
	std::string namespacePath = "stdlib";
	for (size_t i = 1; i < parts.size(); ++i) {
		namespacePath += "." + parts[i];
	}

	// Add child namespaces (subdirectories)
	for (const auto &child : node->children) {
		lsp::CompletionItem item;
		item.label = child.first;
		item.kind = lsp::CompletionItemKind::Module;
		item.detail = namespacePath + "." + child.first + " namespace";
		items.push_back(item);
	}

	// Add functions at this namespace level
	for (const auto &funcName : node->functions) {
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

	// Add structs at this namespace level
	for (const auto &structName : node->structs) {
		auto structIt = stdlibStructsByName_.find(structName);
		if (structIt != stdlibStructsByName_.end()) {
			lsp::CompletionItem item;
			item.label = structIt->second->name;
			item.kind = lsp::CompletionItemKind::Struct;
			item.detail = "struct " + structIt->second->name;
			item.documentation = structIt->second->documentation;
			items.push_back(item);
		}
	}

	// Add enums at this namespace level
	for (const auto &enumName : node->enums) {
		auto enumIt = stdlibEnumsByName_.find(enumName);
		if (enumIt != stdlibEnumsByName_.end()) {
			lsp::CompletionItem item;
			item.label = enumIt->second->name;
			item.kind = lsp::CompletionItemKind::Enum;
			item.detail = "enum " + enumIt->second->name;
			item.documentation = enumIt->second->documentation;
			items.push_back(item);
		}
	}

	// Add interfaces at this namespace level
	for (const auto &ifaceName : node->interfaces) {
		auto ifaceIt = stdlibInterfacesByName_.find(ifaceName);
		if (ifaceIt != stdlibInterfacesByName_.end()) {
			lsp::CompletionItem item;
			item.label = ifaceIt->second->name;
			item.kind = lsp::CompletionItemKind::Interface;
			item.detail = "interface " + ifaceIt->second->name;
			item.documentation = ifaceIt->second->documentation;
			items.push_back(item);
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

		if (prev == '\\')
			continue;

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

	if (inString && pos.character > literalStart)
		return 1;
	if (inChar && pos.character > literalStart)
		return 2;

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
	if (!std::isdigit(cursorChar))
		return 0;

	int start = pos.character;
	int end = pos.character;

	while (start > 0 && (std::isdigit(line[start - 1]) || line[start - 1] == '.')) {
		start--;
	}

	while (end < (int)line.length() && (std::isdigit(line[end]) || line[end] == '.' || line[end] == 'b')) {
		end++;
	}

	if (start > 0 && (std::isalpha(line[start - 1]) || line[start - 1] == '_'))
		return 0;
	if (end < (int)line.length() && (std::isalpha(line[end]) || line[end] == '_'))
		return 0;

	if (start >= end)
		return 0;

	std::string literal = line.substr(start, end - start);
	if (literal.empty() || !std::isdigit(literal[0]))
		return 0;

	if (literal.size() > 1 && literal.back() == 'b')
		return 3;
	if (literal.find('.') != std::string::npos)
		return 2;

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
	if (word.empty())
		return std::nullopt;

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

			auto cacheIt = documentCaches_.find(doc->uri);
			if (cacheIt != documentCaches_.end()) {
				const DocumentCache &cache = cacheIt->second;
				auto varIt = cache.variables.find(objectName);

				if (varIt != cache.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check struct field
					auto structIt = cache.structs.find(typeName);
					if (structIt != cache.structs.end()) {
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

	// Check document cache
	auto cacheIt = documentCaches_.find(doc->uri);
	if (cacheIt != documentCaches_.end()) {
		const DocumentCache &cache = cacheIt->second;

		// Check variable
		auto varIt = cache.variables.find(word);
		if (varIt != cache.variables.end()) {
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
		auto funcIt = cache.functions.find(word);
		if (funcIt != cache.functions.end()) {
			const FunctionInfo &funcInfo = funcIt->second;
			std::string sig = "function " + funcInfo.name + "(";
			bool first = true;
			for (const auto &param : funcInfo.parameters) {
				if (param.name == "self")
					continue;
				if (!first)
					sig += ", ";
				sig += param.name + " " + param.type;
				first = false;
			}
			sig += ") " + funcInfo.returnType;
			hover.contents = "```maxon\n" + sig + "\n```";
			return hover;
		}

		// Check struct
		auto structIt = cache.structs.find(word);
		if (structIt != cache.structs.end()) {
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
		auto enumIt = cache.enumDetails.find(word);
		if (enumIt != cache.enumDetails.end()) {
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
						if (!first)
							enumDef += ", ";
						enumDef += av.name + " " + av.type;
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
			if (i > 0)
				sig += ", ";
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
	if (word.empty())
		return std::nullopt;

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

			auto cacheIt = documentCaches_.find(doc->uri);
			if (cacheIt != documentCaches_.end()) {
				const DocumentCache &cache = cacheIt->second;
				auto varIt = cache.variables.find(objectName);

				if (varIt != cache.variables.end()) {
					const std::string &typeName = varIt->second.type;

					// Check local struct field first
					auto structIt = cache.structs.find(typeName);
					if (structIt != cache.structs.end()) {
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

					// Check stdlib method on this type
					auto stdlibMethodLoc = findStdlibMethodDefinition(typeName, word);
					if (stdlibMethodLoc) {
						return stdlibMethodLoc;
					}
				}
			}

			// Also check if objectName is a stdlib struct type directly (e.g., for static methods)
			auto stdlibStructIt = stdlibStructsByName_.find(objectName);
			if (stdlibStructIt != stdlibStructsByName_.end()) {
				auto methodLoc = findStdlibMethodDefinition(objectName, word);
				if (methodLoc) {
					return methodLoc;
				}
			}
		}
	}

	// Check document cache for local definitions
	auto cacheIt = documentCaches_.find(doc->uri);
	if (cacheIt != documentCaches_.end()) {
		const DocumentCache &cache = cacheIt->second;

		// Check variable
		auto varIt = cache.variables.find(word);
		if (varIt != cache.variables.end()) {
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
		auto funcIt = cache.functions.find(word);
		if (funcIt != cache.functions.end()) {
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
		auto structIt = cache.structs.find(word);
		if (structIt != cache.structs.end()) {
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
		auto ifaceIt = cache.interfaces.find(word);
		if (ifaceIt != cache.interfaces.end()) {
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
		auto enumIt = cache.enumDetails.find(word);
		if (enumIt != cache.enumDetails.end()) {
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
		if (!sym->filePath.empty() && sym->sourceRange.startLine > 0) {
			lsp::Location loc;
			loc.uri = pathToUri(sym->filePath);
			loc.range.start.line = sym->sourceRange.startLine - 1;
			loc.range.start.character = sym->sourceRange.startCol > 0 ? sym->sourceRange.startCol - 1 : 0;
			loc.range.end.line = sym->sourceRange.startLine - 1;
			loc.range.end.character = loc.range.start.character + sym->name.length();
			return loc;
		}
	}

	auto stdlibStructIt = stdlibStructsByName_.find(word);
	if (stdlibStructIt != stdlibStructsByName_.end()) {
		const LSPSymbolInfo *sym = stdlibStructIt->second;
		if (!sym->filePath.empty() && sym->sourceRange.startLine > 0) {
			lsp::Location loc;
			loc.uri = pathToUri(sym->filePath);
			loc.range.start.line = sym->sourceRange.startLine - 1;
			loc.range.start.character = sym->sourceRange.startCol > 0 ? sym->sourceRange.startCol - 1 : 0;
			loc.range.end.line = sym->sourceRange.startLine - 1;
			loc.range.end.character = loc.range.start.character + sym->name.length();
			return loc;
		}
	}

	auto stdlibEnumIt = stdlibEnumsByName_.find(word);
	if (stdlibEnumIt != stdlibEnumsByName_.end()) {
		const LSPSymbolInfo *sym = stdlibEnumIt->second;
		if (!sym->filePath.empty() && sym->sourceRange.startLine > 0) {
			lsp::Location loc;
			loc.uri = pathToUri(sym->filePath);
			loc.range.start.line = sym->sourceRange.startLine - 1;
			loc.range.start.character = sym->sourceRange.startCol > 0 ? sym->sourceRange.startCol - 1 : 0;
			loc.range.end.line = sym->sourceRange.startLine - 1;
			loc.range.end.character = loc.range.start.character + sym->name.length();
			return loc;
		}
	}

	auto stdlibIfaceIt = stdlibInterfacesByName_.find(word);
	if (stdlibIfaceIt != stdlibInterfacesByName_.end()) {
		const LSPSymbolInfo *sym = stdlibIfaceIt->second;
		if (!sym->filePath.empty() && sym->sourceRange.startLine > 0) {
			lsp::Location loc;
			loc.uri = pathToUri(sym->filePath);
			loc.range.start.line = sym->sourceRange.startLine - 1;
			loc.range.start.character = sym->sourceRange.startCol > 0 ? sym->sourceRange.startCol - 1 : 0;
			loc.range.end.line = sym->sourceRange.startLine - 1;
			loc.range.end.character = loc.range.start.character + sym->name.length();
			return loc;
		}
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

	auto cacheIt = documentCaches_.find(doc->uri);
	if (cacheIt == documentCaches_.end() || !cacheIt->second.ast) {
		return symbols;
	}

	const auto &ast = cacheIt->second.ast;

	// Functions
	for (const auto &func : ast->functions) {
		lsp::SymbolInformation sym;
		sym.name = func->name;
		sym.kind = lsp::SymbolKind::Function;
		sym.location.uri = doc->uri;
		sym.location.range.start.line = func->line > 0 ? func->line - 1 : 0;
		sym.location.range.start.character = func->column > 0 ? func->column - 1 : 0;
		sym.location.range.end.line = func->endLine > 0 ? func->endLine - 1 : sym.location.range.start.line;
		sym.location.range.end.character = func->endColumn > 0 ? func->endColumn - 1 : sym.location.range.start.character + func->name.length();
		symbols.push_back(sym);
	}

	// Structs and their methods
	for (const auto &structDef : ast->structs) {
		lsp::SymbolInformation sym;
		sym.name = structDef->name;
		sym.kind = lsp::SymbolKind::Struct;
		sym.location.uri = doc->uri;
		sym.location.range.start.line = structDef->line > 0 ? structDef->line - 1 : 0;
		sym.location.range.start.character = structDef->column > 0 ? structDef->column - 1 : 0;
		sym.location.range.end.line = structDef->endLine > 0 ? structDef->endLine - 1 : sym.location.range.start.line;
		sym.location.range.end.character = structDef->endColumn > 0 ? structDef->endColumn - 1 : sym.location.range.start.character + structDef->name.length();
		symbols.push_back(sym);

		for (const auto &method : structDef->methods) {
			lsp::SymbolInformation methodSym;
			methodSym.name = structDef->name + "." + method->name;
			methodSym.kind = lsp::SymbolKind::Method;
			methodSym.location.uri = doc->uri;
			methodSym.location.range.start.line = method->line > 0 ? method->line - 1 : 0;
			methodSym.location.range.start.character = method->column > 0 ? method->column - 1 : 0;
			methodSym.location.range.end.line = method->endLine > 0 ? method->endLine - 1 : methodSym.location.range.start.line;
			methodSym.location.range.end.character = method->endColumn > 0 ? method->endColumn - 1 : methodSym.location.range.start.character + method->name.length();
			methodSym.containerName = structDef->name;
			symbols.push_back(methodSym);
		}
	}

	// Enums and their methods
	for (const auto &enumDef : ast->enums) {
		lsp::SymbolInformation sym;
		sym.name = enumDef->name;
		sym.kind = lsp::SymbolKind::Enum;
		sym.location.uri = doc->uri;
		sym.location.range.start.line = enumDef->line > 0 ? enumDef->line - 1 : 0;
		sym.location.range.start.character = enumDef->column > 0 ? enumDef->column - 1 : 0;
		sym.location.range.end.line = enumDef->endLine > 0 ? enumDef->endLine - 1 : sym.location.range.start.line;
		sym.location.range.end.character = enumDef->endColumn > 0 ? enumDef->endColumn - 1 : sym.location.range.start.character + enumDef->name.length();
		symbols.push_back(sym);

		for (const auto &method : enumDef->methods) {
			lsp::SymbolInformation methodSym;
			methodSym.name = enumDef->name + "." + method->name;
			methodSym.kind = lsp::SymbolKind::Method;
			methodSym.location.uri = doc->uri;
			methodSym.location.range.start.line = method->line > 0 ? method->line - 1 : 0;
			methodSym.location.range.start.character = method->column > 0 ? method->column - 1 : 0;
			methodSym.location.range.end.line = method->endLine > 0 ? method->endLine - 1 : methodSym.location.range.start.line;
			methodSym.location.range.end.character = method->endColumn > 0 ? method->endColumn - 1 : methodSym.location.range.start.character + method->name.length();
			methodSym.containerName = enumDef->name;
			symbols.push_back(methodSym);
		}
	}

	// Interfaces
	for (const auto &ifaceDef : ast->interfaces) {
		lsp::SymbolInformation sym;
		sym.name = ifaceDef->name;
		sym.kind = lsp::SymbolKind::Interface;
		sym.location.uri = doc->uri;
		sym.location.range.start.line = ifaceDef->line > 0 ? ifaceDef->line - 1 : 0;
		sym.location.range.start.character = ifaceDef->column > 0 ? ifaceDef->column - 1 : 0;
		sym.location.range.end.line = ifaceDef->endLine > 0 ? ifaceDef->endLine - 1 : sym.location.range.start.line;
		sym.location.range.end.character = ifaceDef->endColumn > 0 ? ifaceDef->endColumn - 1 : sym.location.range.start.character + ifaceDef->name.length();
		symbols.push_back(sym);
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

		if (!targetToken)
			return std::nullopt;

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

		if (edits.empty())
			return std::nullopt;

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

		if (!targetToken)
			return std::nullopt;

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

		if (ranges.empty())
			return std::nullopt;
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

std::string Analyzer::pathToUri(const std::string &filePath) {
	std::string result = "file:///";

	// Normalize path separators to forward slashes
	std::string normalizedPath = filePath;
	for (char &c : normalizedPath) {
		if (c == '\\') {
			c = '/';
		}
	}

	// For Windows paths like C:/foo/bar, we need file:///C:/foo/bar
	// The path is already absolute, so just append it
	result += normalizedPath;

	return result;
}

std::optional<lsp::Location> Analyzer::findStdlibMethodDefinition(const std::string &typeName, const std::string &methodName) {
	// Search through stdlib functions for methods matching TypeName.methodName pattern
	// Methods are stored with kind == "method" and their type signature contains the receiver type
	for (const auto &func : stdlibSymbols_.functions) {
		if (func.kind == "method" && func.name == methodName) {
			// Check if this method belongs to the requested type
			// The type field contains something like "function TypeName.methodName(...)"
			std::string expectedPrefix = "function " + typeName + ".";
			if (func.type.find(expectedPrefix) == 0) {
				if (!func.filePath.empty() && func.sourceRange.startLine > 0) {
					lsp::Location loc;
					loc.uri = pathToUri(func.filePath);
					loc.range.start.line = func.sourceRange.startLine - 1;
					loc.range.start.character = func.sourceRange.startCol > 0 ? func.sourceRange.startCol - 1 : 0;
					loc.range.end.line = func.sourceRange.startLine - 1;
					loc.range.end.character = loc.range.start.character + func.name.length();
					return loc;
				}
			}
		}
	}
	return std::nullopt;
}

// ============================================================================
// Type Inference Helpers
// ============================================================================

std::string Analyzer::inferTypeFromInit(const std::string &text, const std::string &varName) {
	// Search for variable declaration patterns:
	// var varName = ... or let varName = ...

	// Build pattern to search for
	std::string varPattern = "var " + varName + " =";
	std::string letPattern = "let " + varName + " =";
	std::string varTypedPattern = "var " + varName + " ";
	std::string letTypedPattern = "let " + varName + " ";

	size_t pos = std::string::npos;
	bool isTypedDecl = false;

	// First try var/let name = pattern
	pos = text.find(varPattern);
	if (pos == std::string::npos) {
		pos = text.find(letPattern);
	}

	// Also check for typed declarations like "var x int = ..."
	if (pos == std::string::npos) {
		pos = text.find(varTypedPattern);
		if (pos != std::string::npos) {
			isTypedDecl = true;
		}
	}
	if (pos == std::string::npos) {
		pos = text.find(letTypedPattern);
		if (pos != std::string::npos) {
			isTypedDecl = true;
		}
	}

	if (pos == std::string::npos) {
		return "";
	}

	// For typed declarations, extract the type directly
	if (isTypedDecl) {
		// Move past "var varName "
		size_t typeStart = pos + 4 + varName.length() + 1; // "var " + varName + " "
		size_t typeEnd = typeStart;

		// Skip any leading whitespace
		while (typeEnd < text.length() && std::isspace(text[typeEnd])) {
			typeStart++;
			typeEnd++;
		}

		// Read the type until = or end of declaration
		while (typeEnd < text.length()) {
			char c = text[typeEnd];
			if (c == '=' || c == '\n' || c == '\r') {
				break;
			}
			typeEnd++;
		}

		std::string typePart = text.substr(typeStart, typeEnd - typeStart);
		// Trim whitespace
		size_t start = typePart.find_first_not_of(" \t");
		size_t end = typePart.find_last_not_of(" \t");
		if (start != std::string::npos && end != std::string::npos) {
			return typePart.substr(start, end - start + 1);
		}
	}

	// Move past "var/let varName = " to the initialization expression
	size_t initStart = pos + 4 + varName.length() + 2; // "var " + varName + " ="
	if (text[pos] == 'l') {
		initStart = pos + 4 + varName.length() + 2; // "let " + varName + " ="
	}

	// Skip whitespace after =
	while (initStart < text.length() && std::isspace(text[initStart])) {
		initStart++;
	}

	if (initStart >= text.length()) {
		return "";
	}

	// Check for string literal: "..."
	if (text[initStart] == '"') {
		return "string";
	}

	// Check for character literal: '...'
	if (text[initStart] == '\'') {
		// Need to distinguish between 'a' (char) and 'blockLabel' (block id)
		// Character literals are single characters or escape sequences
		size_t endQuote = text.find('\'', initStart + 1);
		if (endQuote != std::string::npos && endQuote - initStart <= 4) {
			// Likely a character literal (short enough)
			return "character";
		}
	}

	// Check for array literal: [size]type
	if (text[initStart] == '[') {
		size_t bracketEnd = text.find(']', initStart);
		if (bracketEnd != std::string::npos) {
			// Find the type after ]
			size_t typeStart = bracketEnd + 1;
			size_t typeEnd = typeStart;
			while (typeEnd < text.length() && (std::isalnum(text[typeEnd]) || text[typeEnd] == '_')) {
				typeEnd++;
			}
			if (typeEnd > typeStart) {
				// Return the full array type including the brackets
				return text.substr(initStart, typeEnd - initStart);
			}
		}
	}

	// Check for struct instantiation: StructName { ... }
	// Look for identifier followed by {
	if (std::isalpha(text[initStart]) || text[initStart] == '_') {
		size_t identEnd = initStart;
		while (identEnd < text.length() && (std::isalnum(text[identEnd]) || text[identEnd] == '_')) {
			identEnd++;
		}

		// Skip whitespace after identifier
		size_t afterIdent = identEnd;
		while (afterIdent < text.length() && std::isspace(text[afterIdent])) {
			afterIdent++;
		}

		if (afterIdent < text.length() && text[afterIdent] == '{') {
			// This is a struct instantiation
			return text.substr(initStart, identEnd - initStart);
		}
	}

	// Check for map creation: map from K to V
	if (text.substr(initStart, 4) == "map ") {
		return "map";
	}

	return "";
}

std::string Analyzer::normalizeTypeForLookup(const std::string &typeName) {
	// Strip type parameters from parameterized types
	// e.g., "map<int,int>" -> "map"
	// This allows looking up members in the base type registry

	if (typeName.empty()) {
		return typeName;
	}

	// Check for parameterized type: Type<...>
	size_t anglePos = typeName.find('<');
	if (anglePos != std::string::npos) {
		return typeName.substr(0, anglePos);
	}

	return typeName;
}
