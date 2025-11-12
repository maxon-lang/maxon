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

void test_stdlib_initialization() {
    std::cout << "Testing stdlib initialization..." << std::endl;
    
    Analyzer analyzer;
    
    // Initialize with stdlib directory (relative to test build directory)
    analyzer.initializeStdlib("../../../stdlib");
    
    // Get completions - should include stdlib functions
    auto doc = createTestDocument("function main() int\nend 'main'");
    lsp::Position pos{0, 0};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Check that format_int_array is in completions
    bool hasFormatIntArray = false;
    for (const auto& item : completions) {
        if (item.label == "format_int_array") {
            hasFormatIntArray = true;
            assert(item.kind == 3); // Function
            assert(!item.detail.empty());
            break;
        }
    }
    assert(hasFormatIntArray);
    
    std::cout << "✓ Stdlib initialization works" << std::endl;
}

void test_stdlib_hover() {
    std::cout << "Testing stdlib function hover..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument("format_int_array");
    lsp::Position pos{0, 5}; // Position in "format_int_array"
    auto hover = analyzer.getHover(doc, pos);
    
    // Should return hover information for stdlib function
    assert(hover.has_value());
    assert(!hover->contents.empty());
    assert(hover->contents.find("stdlib::fmt::format_int_array") != std::string::npos);
    assert(hover->contents.find("function") != std::string::npos);
    
    std::cout << "✓ Stdlib function hover works" << std::endl;
}

void test_stdlib_completion_details() {
    std::cout << "Testing stdlib function completion details..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument("");
    lsp::Position pos{0, 0};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Find format_int_array and check its details
    for (const auto& item : completions) {
        if (item.label == "format_int_array") {
            // Should have signature in detail
            assert(!item.detail.empty());
            assert(item.detail.find("value int") != std::string::npos);
            assert(item.detail.find("buffer") != std::string::npos);
            assert(item.detail.find("[12]char") != std::string::npos);
            assert(!item.documentation.empty());
            std::cout << "✓ Stdlib function completion details correct" << std::endl;
            return;
        }
    }
    
    assert(false && "format_int_array not found in completions");
}

void test_stdlib_nonexistent_directory() {
    std::cout << "Testing stdlib with nonexistent directory..." << std::endl;
    
    Analyzer analyzer;
    
    // Should not crash with nonexistent directory
    analyzer.initializeStdlib("/nonexistent/path");
    
    auto doc = createTestDocument("function main() end");
    auto completions = analyzer.getCompletions(doc, {0, 0});
    
    // Should still have basic completions (keywords)
    assert(!completions.empty());
    
    std::cout << "✓ Handles nonexistent stdlib directory gracefully" << std::endl;
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
        
        // New stdlib tests
        test_stdlib_initialization();
        test_stdlib_hover();
        test_stdlib_completion_details();
        test_stdlib_nonexistent_directory();
        
        std::cout << "\n✓ All Analyzer tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
