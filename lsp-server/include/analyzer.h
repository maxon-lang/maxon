#ifndef ANALYZER_H
#define ANALYZER_H

#include "document_manager.h"
#include "lexer.h"
#include "lsp_types.h"
#include "parser.h"
#include "semantic_analyzer.h"
#include <map>
#include <memory>
#include <set>
#include <vector>

// Information about a stdlib function
struct StdlibFunction {
	std::string name;						   // Unqualified name (e.g., "format_int_array")
	std::string qualifiedName;				   // Fully qualified name (e.g., "stdlib.fmt.format_int_array")
	std::string signature;					   // Function signature for display
	std::string documentation;				   // Documentation from comments
	std::string namespacePath;				   // Namespace path (e.g., "stdlib.fmt")
	std::string moduleName;					   // Module name (e.g., "integer")
	std::string returnType;					   // Return type
	std::vector<FunctionParameter> parameters; // Function parameters
};

// Information about a type method or property
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

class Analyzer {
  public:
	Analyzer();

	// Initialize stdlib function cache
	void initializeStdlib(const std::string &stdlibPath);

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
	std::vector<std::string> keywords;
	std::map<std::string, Lexer::KeywordInfo> keywordMetadata;
	std::map<std::string, StdlibFunction> stdlibFunctions;		// Key: unqualified name
	std::map<std::string, std::vector<TypeMember>> typeMembers; // Key: type name (e.g., "string", "[]")
	NamespaceNode namespaceRoot;								// Root of namespace hierarchy ("stdlib")

	// Cache of semantic analysis results per document URI
	struct SemanticInfo {
		std::map<std::string, VariableInfo> variables;
		std::map<std::string, FunctionInfo> functions;
		std::map<std::string, StructInfo> structs;
	};
	std::map<std::string, SemanticInfo> semanticCache;

	// Helper functions
	std::string getWordAtPosition(const std::string &text, lsp::Position pos);
	std::string getTextBeforePosition(const std::string &text, lsp::Position pos);
	bool isKeyword(const std::string &word) const;
	lsp::Range tokenToRange(const Token &token);
	void loadStdlibFile(const std::string &filePath, const std::string &namespaceName);
	std::vector<std::string> findStdlibFiles(const std::string &stdlibPath);
	std::string extractDocumentation(const std::string &sourceText, const std::string &functionName, int functionLine);
	std::vector<lsp::CompletionItem> getQualifiedNameCompletions(const std::string &prefix);
	std::vector<lsp::CompletionItem> getMemberCompletions(const std::string &typeName, const SemanticInfo &semInfo);
	void initializeTypeMembers();
};

#endif // ANALYZER_H
