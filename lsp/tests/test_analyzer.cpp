#include "../include/analyzer.h"
#include <cassert>
#include <iostream>
#include <memory>

std::shared_ptr<Document> createTestDocument(const std::string& text) {
    return std::make_shared<Document>("file:///test.maxon", text, 0);
}

void test_analyze_valid_code() {
    std::cout << "Testing analysis of valid code..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() end");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // For now, just check that analysis doesn't crash
    // The actual validity depends on the parser implementation
    std::cout << "  Found " << diagnostics.size() << " diagnostic(s)" << std::endl;
    
    std::cout << "✓ Code analysis completes without crashing" << std::endl;
}

void test_analyze_invalid_token() {
    std::cout << "Testing analysis of invalid token..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() @ end");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should detect the invalid @ token
    assert(!diagnostics.empty());
    
    std::cout << "✓ Invalid token detected" << std::endl;
}

void test_keyword_completions() {
    std::cout << "Testing keyword completions..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("func");
    
    lsp::Position pos{0, 4};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have keyword completions
    assert(!completions.empty());
    
    // Check that "function" is in completions
    bool hasFunction = false;
    for (const auto& item : completions) {
        if (item.label == "function") {
            hasFunction = true;
            break;
        }
    }
    assert(hasFunction);
    
    std::cout << "✓ Keyword completions work" << std::endl;
}

void test_type_completions() {
    std::cout << "Testing type completions..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("var x: ");
    
    lsp::Position pos{0, 7};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Check that "int" and "string" are in completions
    bool hasInt = false;
    bool hasString = false;
    for (const auto& item : completions) {
        if (item.label == "int") hasInt = true;
        if (item.label == "string") hasString = true;
    }
    assert(hasInt);
    assert(hasString);
    
    std::cout << "✓ Type completions work" << std::endl;
}

void test_identifier_completions() {
    std::cout << "Testing identifier completions..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("var myVariable\nvar my");
    
    lsp::Position pos{1, 6};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should find "myVariable" from the document
    bool hasMyVariable = false;
    for (const auto& item : completions) {
        if (item.label == "myVariable") {
            hasMyVariable = true;
            break;
        }
    }
    assert(hasMyVariable);
    
    std::cout << "✓ Identifier completions work" << std::endl;
}

void test_hover_on_keyword() {
    std::cout << "Testing hover on keyword..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() end");
    
    lsp::Position pos{0, 3}; // Position in "function"
    auto hover = analyzer.getHover(doc, pos);
    
    // Should return hover information for keyword
    assert(hover.has_value());
    assert(!hover->contents.empty());
    
    std::cout << "✓ Hover on keyword works" << std::endl;
}

void test_document_symbols() {
    std::cout << "Testing document symbols..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("function main()\nvar x\nend");
    
    auto symbols = analyzer.getSymbols(doc);
    
    // Should find function declaration
    assert(!symbols.empty());
    
    std::cout << "✓ Document symbols work" << std::endl;
}

void test_analyze_syntax_error() {
    std::cout << "Testing analysis of syntax error..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("function main( end"); // Missing closing paren
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should detect syntax error
    assert(!diagnostics.empty());
    
    std::cout << "✓ Syntax error detected" << std::endl;
}

void test_empty_document() {
    std::cout << "Testing empty document..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument("");
    
    auto diagnostics = analyzer.analyze(doc);
    auto completions = analyzer.getCompletions(doc, {0, 0});
    
    // Empty document should still provide completions
    assert(!completions.empty());
    
    std::cout << "✓ Empty document handled correctly" << std::endl;
}

int main() {
    std::cout << "Running Analyzer Tests...\n" << std::endl;
    
    try {
        test_analyze_valid_code();
        test_analyze_invalid_token();
        test_keyword_completions();
        test_type_completions();
        test_identifier_completions();
        test_hover_on_keyword();
        test_document_symbols();
        test_analyze_syntax_error();
        test_empty_document();
        
        std::cout << "\n✓ All Analyzer tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
