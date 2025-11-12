#ifndef ANALYZER_H
#define ANALYZER_H

#include "lsp_types.h"
#include "document_manager.h"
#include "lexer.h"
#include "parser.h"
#include <vector>
#include <memory>
#include <set>
#include <map>

// Information about a stdlib function
struct StdlibFunction {
    std::string name;           // Unqualified name (e.g., "format_int_array")
    std::string qualifiedName;  // Fully qualified name (e.g., "stdlib::fmt::format_int_array")
    std::string signature;      // Function signature for display
    std::string documentation;  // Documentation from comments
};

class Analyzer {
public:
    Analyzer();
    
    // Initialize stdlib function cache
    void initializeStdlib(const std::string& stdlibPath);
    
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
    
private:
    std::vector<std::string> keywords;
    std::map<std::string, StdlibFunction> stdlibFunctions; // Key: unqualified name
    
    // Helper functions
    std::string getWordAtPosition(const std::string& text, lsp::Position pos);
    bool isKeyword(const std::string& word) const;
    lsp::Range tokenToRange(const Token& token);
    void loadStdlibFile(const std::string& filePath, const std::string& namespaceName);
    std::vector<std::string> findStdlibFiles(const std::string& stdlibPath);
    std::string extractDocumentation(const std::string& sourceText, const std::string& functionName, int functionLine);
};

#endif // ANALYZER_H
