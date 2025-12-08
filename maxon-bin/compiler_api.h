#ifndef COMPILER_API_H
#define COMPILER_API_H

/**
 * Compiler API for LSP Integration
 *
 * Provides a clean interface for the LSP server to access compiler functionality.
 * This makes the compiler the single source of truth for all language information.
 */

#include "ast.h"
#include "lexer/lexer_keyword_matcher.h"
#include "semantic_analyzer.h"
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

// Forward declare ParseError from parser.h
struct ParseError;

// ============================================================================
// LSP Symbol Information
// ============================================================================

/**
 * Parameter information for function/method symbols
 */
struct LSPParameterInfo {
	std::string name;
	std::string type;

	LSPParameterInfo() = default;

	LSPParameterInfo(const std::string &n, const std::string &t)
		: name(n), type(t) {}
};

/**
 * Field information for struct symbols
 */
struct LSPFieldInfo {
	std::string name;
	std::string type;
	bool isImmutable; // true for 'let', false for 'var'

	LSPFieldInfo() = default;

	LSPFieldInfo(const std::string &n, const std::string &t, bool immutable = false)
		: name(n), type(t), isImmutable(immutable) {}
};

/**
 * Symbol information for LSP features (hover, completion, go-to-definition)
 */
struct LSPSymbolInfo {
	std::string name;
	std::string kind;						  // "function", "struct", "enum", "interface", "variable", "field", "method"
	std::string type;						  // Type signature as string
	std::string documentation;				  // Doc comment content
	SourceRange sourceRange;				  // Location in source
	std::string filePath;					  // Absolute path to the source file
	std::vector<LSPParameterInfo> parameters; // For functions/methods
	std::string returnType;					  // For functions/methods
	std::vector<std::string> conformsTo;	  // For structs: interfaces this struct conforms to
	std::vector<LSPFieldInfo> fields;		  // For structs: field definitions

	LSPSymbolInfo() = default;

	LSPSymbolInfo(const std::string &n, const std::string &k, const std::string &t,
				  const std::string &doc, const SourceRange &range)
		: name(n), kind(k), type(t), documentation(doc), sourceRange(range) {}
};

// ============================================================================
// LSP Analysis Result
// ============================================================================

/**
 * Complete analysis result for a source file
 * Contains AST, symbols, semantic info, and all errors/warnings
 */
struct LSPAnalysisResult {
	std::unique_ptr<ProgramAST> ast;
	std::vector<LSPSymbolInfo> symbols;
	std::vector<ParseError> parseErrors;
	std::vector<SemanticError> semanticErrors;

	// Semantic info from analyzer (for hover/completion)
	std::map<std::string, VariableInfo> variables;
	std::map<std::string, FunctionInfo> functions;
	std::map<std::string, StructInfo> structs;
	std::map<std::string, InterfaceInfo> interfaces;

	// Check if analysis was successful (no parse errors)
	bool hasParseErrors() const { return !parseErrors.empty(); }

	// Check if there are any semantic errors
	bool hasSemanticErrors() const { return !semanticErrors.empty(); }

	// Check if there are any errors at all
	bool hasErrors() const { return hasParseErrors() || hasSemanticErrors(); }
};

// ============================================================================
// Standard Library Symbols
// ============================================================================

/**
 * Aggregated symbols from the standard library
 */
struct StdlibSymbols {
	std::vector<LSPSymbolInfo> functions;
	std::vector<LSPSymbolInfo> structs;
	std::vector<LSPSymbolInfo> enums;
	std::vector<LSPSymbolInfo> interfaces;

	// Get total symbol count
	size_t totalCount() const {
		return functions.size() + structs.size() + enums.size() + interfaces.size();
	}

	// Check if stdlib is empty (not loaded or no symbols found)
	bool empty() const { return totalCount() == 0; }
};

// ============================================================================
// Compiler API Functions
// ============================================================================

/**
 * Analyze source code for LSP features
 *
 * Parses the source code, runs semantic analysis (collecting errors rather than
 * failing), and extracts all symbols from the AST.
 *
 * @param source The source code to analyze
 * @param filename The filename (used for error messages and namespace derivation)
 * @return LSPAnalysisResult containing AST, symbols, and errors
 */
LSPAnalysisResult analyzeForLSP(const std::string &source, const std::string &filename);

/**
 * Analyze source code for LSP features with stdlib integration
 *
 * Parses the source code, runs semantic analysis with stdlib symbols registered,
 * and extracts all symbols from the AST. This version ensures stdlib functions
 * are recognized and don't produce "Undefined function" errors.
 *
 * @param source The source code to analyze
 * @param filename The filename (used for error messages and namespace derivation)
 * @param stdlib The loaded stdlib symbols to register with the semantic analyzer
 * @return LSPAnalysisResult containing AST, symbols, and errors
 */
LSPAnalysisResult analyzeForLSP(const std::string &source, const std::string &filename,
								const StdlibSymbols &stdlib);

/**
 * Load symbols from the standard library
 *
 * Scans the stdlib directory, parses each .maxon file, and extracts
 * public (exported) symbols with their documentation.
 *
 * @param stdlibPath Path to the stdlib directory
 * @return StdlibSymbols containing all exported symbols
 */
StdlibSymbols loadStdlib(const std::string &stdlibPath);

/**
 * Load symbols from the standard library with a content provider
 *
 * Like loadStdlib, but uses a content provider callback to get file contents.
 * This allows the LSP to provide in-memory content for open documents.
 *
 * @param stdlibPath Path to the stdlib directory
 * @param contentProvider Callback that takes a file path and returns optional content.
 *                        If nullopt, the file is read from disk.
 * @return StdlibSymbols containing all exported symbols
 */
StdlibSymbols loadStdlibWithContentProvider(
	const std::string &stdlibPath,
	std::function<std::optional<std::string>(const std::string &)> contentProvider);

/**
 * Get all keyword information for LSP
 *
 * Delegates to KeywordMatcher::getLSPKeywordInfo() to provide keyword
 * completions and hover information.
 *
 * @return Vector of KeywordLSPInfo for all Maxon keywords
 */
std::vector<KeywordLSPInfo> getKeywordInfo();

/**
 * Get keywords matching a prefix for completion
 *
 * Delegates to KeywordMatcher::getKeywordsForCompletion() to provide
 * filtered keyword completions.
 *
 * @param prefix The prefix to match against keyword names
 * @return Vector of KeywordLSPInfo for matching keywords
 */
std::vector<KeywordLSPInfo> getKeywordsForCompletion(const std::string &prefix);

// ============================================================================
// Documentation Extraction
// ============================================================================

/**
 * Extract documentation comment from source code
 *
 * Looks backwards from the declaration line for consecutive /// comment lines
 * and concatenates them into a single documentation string.
 *
 * @param source The source code
 * @param declarationLine The line number of the declaration (1-based)
 * @return The extracted documentation string (empty if no doc comment found)
 */
std::string extractDocComment(const std::string &source, int declarationLine);

// ============================================================================
// Symbol Extraction Helpers
// ============================================================================

/**
 * Extract symbols from a parsed AST
 *
 * Walks the AST and extracts all symbols (functions, structs, enums, interfaces)
 * with their type information and source ranges.
 *
 * @param program The parsed program AST
 * @param source The original source code (for doc comment extraction)
 * @param exportedOnly If true, only extract exported symbols
 * @return Vector of LSPSymbolInfo for all extracted symbols
 */
std::vector<LSPSymbolInfo> extractSymbolsFromAST(const ProgramAST *program,
												 const std::string &source,
												 bool exportedOnly = false);

/**
 * Build a function signature string for display
 *
 * Creates a human-readable signature like:
 * "function name(param1 int, param2 string) returnType"
 *
 * @param func The function AST node
 * @return Formatted function signature string
 */
std::string buildFunctionSignature(const FunctionAST *func);

/**
 * Build a struct signature string for display
 *
 * Creates a human-readable signature showing fields:
 * "struct Name { field1 int, field2 string }"
 *
 * @param structDef The struct definition AST node
 * @return Formatted struct signature string
 */
std::string buildStructSignature(const StructDefAST *structDef);

/**
 * Build an enum signature string for display
 *
 * Creates a human-readable signature showing cases:
 * "enum Name { case1, case2(value int) }"
 *
 * @param enumDef The enum definition AST node
 * @return Formatted enum signature string
 */
std::string buildEnumSignature(const EnumDefAST *enumDef);

/**
 * Build an interface signature string for display
 *
 * Creates a human-readable signature showing methods:
 * "interface Name { method1(param int) returnType }"
 *
 * @param interfaceDef The interface definition AST node
 * @return Formatted interface signature string
 */
std::string buildInterfaceSignature(const InterfaceDefAST *interfaceDef);

#endif // COMPILER_API_H
