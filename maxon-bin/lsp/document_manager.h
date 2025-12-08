#ifndef MAXON_LSP_DOCUMENT_MANAGER_H
#define MAXON_LSP_DOCUMENT_MANAGER_H

#include "../compiler_api.h"
#include "../parser.h"
#include "lsp_types.h"
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace maxon_lsp {

// Namespace alias for easier access to LSP types
namespace lsp = maxon::lsp;

// URI utility functions for file:// URI handling
std::string uriToPath(const std::string &uri);
std::string pathToUri(const std::string &path);

// URL encoding/decoding for special characters
std::string urlEncode(const std::string &str);
std::string urlDecode(const std::string &str);

/**
 * Represents an open document in the editor.
 * Maintains content and provides efficient position/offset conversions.
 */
struct Document {
	std::string uri;
	std::string languageId;
	int version;
	std::string content;
	std::vector<std::string> lines; // Cached line splits for fast position lookups

	Document() : version(0) {}

	Document(const std::string &u, const std::string &lang, int v, const std::string &text)
		: uri(u), languageId(lang), version(v), content(text) {
		updateLines();
	}

	// Rebuild lines cache from content
	void updateLines();

	// Convert byte offset to LSP Position (0-based line/character)
	lsp::Position offsetToPosition(size_t offset) const;

	// Convert LSP Position (0-based) to byte offset
	size_t positionToOffset(const lsp::Position &pos) const;

	// Get line count
	int getLineCount() const { return static_cast<int>(lines.size()); }

	// Get a specific line (0-indexed)
	std::string getLine(int lineIndex) const;
};

/**
 * Cached analysis results for a document.
 * Stores AST, symbols, and errors from the last analysis.
 */
struct AnalysisCache {
	std::unique_ptr<ProgramAST> ast;
	std::vector<LSPSymbolInfo> symbols;
	std::vector<ParseError> parseErrors;
	std::vector<SemanticError> semanticErrors;
	int version;
	int64_t analysisTimeMs;

	// Semantic info from analyzer (for hover/completion)
	// Variables are stored with function-qualified keys (e.g., "funcName::varName")
	// to avoid collisions when different functions have same-named parameters
	std::map<std::string, VariableInfo> variables;
	std::map<std::string, FunctionInfo> functions;
	std::map<std::string, StructInfo> structs;
	std::map<std::string, InterfaceInfo> interfaces;

	AnalysisCache() : version(0), analysisTimeMs(0) {}

	// Move constructor and assignment (needed because of unique_ptr)
	AnalysisCache(AnalysisCache &&) = default;
	AnalysisCache &operator=(AnalysisCache &&) = default;

	// Delete copy operations
	AnalysisCache(const AnalysisCache &) = delete;
	AnalysisCache &operator=(const AnalysisCache &) = delete;

	bool hasParseErrors() const { return !parseErrors.empty(); }
	bool hasSemanticErrors() const { return !semanticErrors.empty(); }
	bool hasErrors() const { return hasParseErrors() || hasSemanticErrors(); }

	/**
	 * Find the enclosing function key for a given line position.
	 * Returns namespace.funcName, StructName.methodName, or just funcName.
	 */
	std::string findEnclosingFunction(int line) const;

	/**
	 * Look up a variable by name, optionally using the enclosing function for qualification.
	 * If position is provided, tries qualified lookup first (funcName::varName).
	 * Falls back to searching for any variable ending with ::varName or exact match.
	 */
	const VariableInfo *findVariable(const std::string &name, int line = -1) const;
};

/**
 * Manages open documents and their analysis caches.
 * Handles incremental updates and provides position utilities.
 */
class DocumentManager {
  public:
	DocumentManager() = default;

	// Document lifecycle
	void openDocument(const std::string &uri, const std::string &languageId,
					  int version, const std::string &content);
	void closeDocument(const std::string &uri);

	// Incremental document updates (applies changes in correct order)
	void updateDocument(const std::string &uri, int version,
						const std::vector<lsp::TextDocumentContentChangeEvent> &changes);

	// Full document replacement
	void replaceDocument(const std::string &uri, int version, const std::string &content);

	// Document access
	std::optional<Document *> getDocument(const std::string &uri);
	std::vector<std::string> getOpenDocumentUris() const;
	bool hasDocument(const std::string &uri) const;

	// Analysis cache management
	AnalysisCache *getAnalysis(const std::string &uri);
	AnalysisCache *getLastGoodAnalysis(const std::string &uri);
	void invalidateAnalysis(const std::string &uri);
	void setAnalysis(const std::string &uri, AnalysisCache cache);

  private:
	std::map<std::string, Document> documents_;
	std::map<std::string, AnalysisCache> analysisCache_;
	std::map<std::string, AnalysisCache> lastGoodCache_;

	// Apply a single change to a document
	void applyChange(Document &doc, const lsp::TextDocumentContentChangeEvent &change);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_DOCUMENT_MANAGER_H
