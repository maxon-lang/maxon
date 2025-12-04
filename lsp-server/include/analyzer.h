#ifndef ANALYZER_H
#define ANALYZER_H

#include "compiler_api.h"
#include "document_manager.h"
#include "lexer.h"
#include "lsp_types.h"
#include "parser.h"
#include "semantic_analyzer.h"
#include <chrono>
#include <map>
#include <memory>
#include <set>
#include <vector>

// Information about a type method or property (for member completions)
struct TypeMember {
	std::string name;		   // Method/property name
	bool isMethod;			   // true = method (needs parens), false = property
	std::string returnType;	   // Return type
	std::string signature;	   // For methods: "(arg1 type, arg2 type)"
	std::string documentation; // Description
};

// Structure to track namespace hierarchy for completions
struct NamespaceNode {
	std::string name;
	std::map<std::string, NamespaceNode> children; // subnamespaces or modules
	std::vector<std::string> functions;			   // function names at this level
};

// Document analysis cache for a single document
struct DocumentCache {
	std::unique_ptr<ProgramAST> ast;
	std::vector<LSPSymbolInfo> symbols;
	std::vector<ParseError> parseErrors;
	std::vector<SemanticError> semanticErrors;
	int64_t lastAnalysisMs;  // Time taken for last analysis in milliseconds
	int version;             // Document version

	DocumentCache() : lastAnalysisMs(0), version(0) {}

	// Move constructor and assignment (needed because of unique_ptr)
	DocumentCache(DocumentCache&&) = default;
	DocumentCache& operator=(DocumentCache&&) = default;

	// Delete copy operations
	DocumentCache(const DocumentCache&) = delete;
	DocumentCache& operator=(const DocumentCache&) = delete;

	bool hasParseErrors() const { return !parseErrors.empty(); }
	bool hasSemanticErrors() const { return !semanticErrors.empty(); }
	bool hasErrors() const { return hasParseErrors() || hasSemanticErrors(); }
};

// Semantic info extracted from analysis (for LSP features)
struct SemanticInfo {
	std::map<std::string, VariableInfo> variables;
	std::map<std::string, FunctionInfo> functions;
	std::map<std::string, StructInfo> structs;
	std::map<std::string, InterfaceInfo> interfaces;
	std::map<std::string, std::string> enums;  // enum name -> file path

	// Store enum info for completions
	struct EnumInfo {
		std::string name;
		std::string rawValueType;
		std::string filePath;
		int line;
		int column;
		struct CaseInfo {
			std::string name;
			std::vector<std::pair<std::string, std::string>> associatedValues;
			bool hasRawValue;
			int line;
			int column;
		};
		std::vector<CaseInfo> cases;
	};
	std::map<std::string, EnumInfo> enumDetails;
};

class Analyzer {
  public:
	Analyzer();

	// Initialize stdlib using compiler API
	void initializeStdlib(const std::string &stdlibPath);

	// Reload entire stdlib (called when stdlib files change via file watcher)
	void reloadStdlib();

	// Reload a single stdlib file (called when a stdlib file is modified)
	void reloadStdlibFile(const std::string &filePath);

	// Check if a file path is within the stdlib directory
	bool isStdlibFile(const std::string &filePath) const;

	// Invalidate all document caches (called after stdlib reload)
	void invalidateAllDocumentCaches();

	// Invalidate cache for a specific document
	void invalidateDocumentCache(const std::string &uri);

	// Analyze document and return diagnostics
	std::vector<lsp::Diagnostic> analyze(std::shared_ptr<Document> doc);

	// Get completions at position
	std::vector<lsp::CompletionItem> getCompletions(std::shared_ptr<Document> doc, lsp::Position pos);

	// Get hover information
	std::optional<lsp::Hover> getHover(std::shared_ptr<Document> doc, lsp::Position pos);

	// Get definition location
	std::optional<lsp::Location> getDefinition(std::shared_ptr<Document> doc, lsp::Position pos);

	// Get document symbols
	std::vector<lsp::SymbolInformation> getSymbols(std::shared_ptr<Document> doc);

	// Get rename edits for block identifiers
	std::optional<lsp::WorkspaceEdit> getRename(std::shared_ptr<Document> doc, lsp::Position pos, const std::string &newName);

	// Get linked editing ranges (for live typing rename)
	std::optional<std::vector<lsp::Range>> getLinkedEditingRanges(std::shared_ptr<Document> doc, lsp::Position pos);

  private:
	std::string stdlibPath_;  // Stored stdlib path for reloading

	// Compiler API data (single source of truth)
	StdlibSymbols stdlibSymbols_;  // All stdlib symbols from compiler API
	std::vector<KeywordLSPInfo> keywordInfo_;  // All keyword info from compiler API

	// Derived data for efficient lookup
	std::map<std::string, LSPSymbolInfo*> stdlibFunctionsByName_;  // Quick lookup by name
	std::map<std::string, LSPSymbolInfo*> stdlibStructsByName_;
	std::map<std::string, LSPSymbolInfo*> stdlibEnumsByName_;
	std::map<std::string, LSPSymbolInfo*> stdlibInterfacesByName_;

	// Type member info (for dot-completion on types)
	std::map<std::string, std::vector<TypeMember>> typeMembers_;

	// Namespace hierarchy for qualified name completions
	NamespaceNode namespaceRoot_;

	// Document analysis caches
	std::map<std::string, DocumentCache> documentCaches_;    // Current analysis per document
	std::map<std::string, DocumentCache> lastGoodCaches_;    // Last successful analysis (for error resilience)

	// Semantic info cache (extracted from analysis)
	std::map<std::string, SemanticInfo> semanticCache_;

	// Throttling state
	std::map<std::string, std::chrono::steady_clock::time_point> lastAnalysisTime_;

	// Helper functions
	std::string getWordAtPosition(const std::string &text, lsp::Position pos);
	std::string getTextBeforePosition(const std::string &text, lsp::Position pos);
	std::string findContainingStruct(const std::string &text, lsp::Position pos);
	bool isKeyword(const std::string &word) const;
	lsp::Range tokenToRange(const Token &token);

	// Stdlib loading helpers
	void buildStdlibLookups();
	void buildNamespaceHierarchy();
	void initializeTypeMembers();

	// Completion helpers
	std::vector<lsp::CompletionItem> getQualifiedNameCompletions(const std::string &prefix);
	std::vector<lsp::CompletionItem> getMemberCompletions(const std::string &typeName, const SemanticInfo &semInfo);
	std::vector<lsp::CompletionItem> getKeywordCompletions(const std::string &prefix);

	// Analysis helpers
	bool shouldThrottleAnalysis(const std::string &uri, int64_t lastAnalysisMs);
	void updateSemanticCache(const std::string &uri, const DocumentCache &cache);
	bool isInsideErrorRegion(const DocumentCache &cache, int line, int column);
};

#endif // ANALYZER_H
