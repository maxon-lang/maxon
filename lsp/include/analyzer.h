#ifndef ANALYZER_H
#define ANALYZER_H

#include "lsp_types.h"
#include "document_manager.h"
#include "lexer.h"
#include "parser.h"
#include <vector>
#include <memory>
#include <set>

class Analyzer {
public:
    Analyzer();
    
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
    
    // Helper functions
    std::string getWordAtPosition(const std::string& text, lsp::Position pos);
    bool isKeyword(const std::string& word) const;
    lsp::Range tokenToRange(const Token& token);
};

#endif // ANALYZER_H
