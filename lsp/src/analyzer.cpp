#include "analyzer.h"
#include "semantic_analyzer.h"
#include <algorithm>
#include <sstream>

Analyzer::Analyzer() {
    keywords = {
        "function", "var", "let", "while", "if", "else", "end", 
        "return", "break", "continue", "true", "false", "int", "string"
    };
}

std::vector<lsp::Diagnostic> Analyzer::analyze(std::shared_ptr<Document> doc) {
    std::vector<lsp::Diagnostic> diagnostics;
    
    try {
        Lexer lexer(doc->text);
        std::vector<Token> tokens = lexer.tokenize();
        
        // Check for lexer errors (UNKNOWN tokens)
        for (const auto& token : tokens) {
            if (token.type == TokenType::UNKNOWN) {
                lsp::Diagnostic diag;
                diag.range = tokenToRange(token);
                diag.message = "Unknown token: " + token.value;
                diag.severity = 1; // Error
                diag.source = "maxon-lsp";
                diagnostics.push_back(diag);
            }
        }
        
        // Try to parse
        try {
            Parser parser(tokens);
            auto program = parser.parse();
            
            // Run semantic analysis
            SemanticAnalyzer semanticAnalyzer;
            std::vector<SemanticError> semanticErrors = semanticAnalyzer.analyze(program.get());
            
            // Convert semantic errors to LSP diagnostics
            for (const auto& error : semanticErrors) {
                lsp::Diagnostic diag;
                diag.range.start.line = error.line > 0 ? error.line - 1 : 0;
                diag.range.start.character = error.column > 0 ? error.column - 1 : 0;
                diag.range.end.line = error.line > 0 ? error.line - 1 : 0;
                diag.range.end.character = error.column > 0 ? error.column : 1;
                diag.message = error.message;
                diag.severity = 1; // Error
                diag.source = "maxon-lsp";
                diagnostics.push_back(diag);
            }
            
        } catch (const std::exception& e) {
            lsp::Diagnostic diag;
            diag.range.start.line = 0;
            diag.range.start.character = 0;
            diag.range.end.line = 0;
            diag.range.end.character = 1;
            diag.message = std::string("Parse error: ") + e.what();
            diag.severity = 1; // Error
            diag.source = "maxon-lsp";
            diagnostics.push_back(diag);
        }
        
    } catch (const std::exception& e) {
        lsp::Diagnostic diag;
        diag.range.start.line = 0;
        diag.range.start.character = 0;
        diag.range.end.line = 0;
        diag.range.end.character = 1;
        diag.message = std::string("Lexer error: ") + e.what();
        diag.severity = 1; // Error
        diag.source = "maxon-lsp";
        diagnostics.push_back(diag);
    }
    
    return diagnostics;
}

std::vector<lsp::CompletionItem> Analyzer::getCompletions(std::shared_ptr<Document> doc, lsp::Position pos) {
    std::vector<lsp::CompletionItem> items;
    
    // Add keyword completions
    for (const auto& keyword : keywords) {
        lsp::CompletionItem item;
        item.label = keyword;
        item.kind = 14; // Keyword
        item.detail = "Maxon keyword";
        items.push_back(item);
    }
    
    // Add built-in types
    items.push_back({"int", 14, "Integer type", ""});
    items.push_back({"string", 14, "String type", ""});
    
    // Try to extract identifiers from document for variable/function completions
    try {
        Lexer lexer(doc->text);
        std::vector<Token> tokens = lexer.tokenize();
        
        std::set<std::string> identifiers;
        for (const auto& token : tokens) {
            if (token.type == TokenType::IDENTIFIER) {
                identifiers.insert(token.value);
            }
        }
        
        for (const auto& id : identifiers) {
            lsp::CompletionItem item;
            item.label = id;
            item.kind = 6; // Variable
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
    
    if (isKeyword(word)) {
        hover.contents = "**" + word + "** (keyword)\n\nMaxon language keyword";
    } else if (word == "int") {
        hover.contents = "**int** (type)\n\nInteger type in Maxon";
    } else if (word == "string") {
        hover.contents = "**string** (type)\n\nString type in Maxon";
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
            if ((tokens[i].type == TokenType::VAR || tokens[i].type == TokenType::FUNCTION) &&
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
            if (tokens[i].type == TokenType::FUNCTION &&
                tokens[i + 1].type == TokenType::IDENTIFIER) {
                
                lsp::SymbolInformation sym;
                sym.name = tokens[i + 1].value;
                sym.kind = 12; // Function
                sym.location.uri = doc->uri;
                sym.location.range = tokenToRange(tokens[i + 1]);
                symbols.push_back(sym);
            }
            else if (tokens[i].type == TokenType::VAR &&
                     tokens[i + 1].type == TokenType::IDENTIFIER) {
                
                lsp::SymbolInformation sym;
                sym.name = tokens[i + 1].value;
                sym.kind = 13; // Variable
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

std::string Analyzer::getWordAtPosition(const std::string& text, lsp::Position pos) {
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

bool Analyzer::isKeyword(const std::string& word) const {
    return std::find(keywords.begin(), keywords.end(), word) != keywords.end();
}

lsp::Range Analyzer::tokenToRange(const Token& token) {
    lsp::Range range;
    range.start.line = token.line - 1; // LSP is 0-indexed
    range.start.character = token.column - 1;
    range.end.line = token.line - 1;
    range.end.character = token.column - 1 + token.value.length();
    return range;
}
