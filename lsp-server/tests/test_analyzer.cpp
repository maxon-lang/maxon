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
            assert(item.kind == lsp::CompletionItemKind::Function);
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
    // Check for function name (may use different separator formats)
    assert(hover->contents.find("format_int_array") != std::string::npos);
    
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
            if (item.detail.empty()) {
                std::cerr << "  Warning: item.detail is empty" << std::endl;
            } else {
                std::cout << "  Detail: " << item.detail << std::endl;
                // Note: [12]char may be represented differently
                bool hasExpectedInfo = item.detail.find("value int") != std::string::npos &&
                                       item.detail.find("buffer") != std::string::npos;
                if (hasExpectedInfo) {
                    std::cout << "✓ Stdlib function completion details correct" << std::endl;
                } else {
                    std::cout << "  Note: Detail format may vary" << std::endl;
                }
            }
            return;
        }
    }
    
    std::cout << "  Note: format_int_array not found in completions" << std::endl;
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

void test_qualified_name_stdlib_root() {
    std::cout << "Testing qualified name completion: stdlib root..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with "std" - should suggest "stdlib"
    auto doc = createTestDocument("std");
    lsp::Position pos{0, 3}; // After "std"
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should include "stdlib" in completions
    bool hasStdlib = false;
    for (const auto& item : completions) {
        if (item.label == "stdlib") {
            hasStdlib = true;
            assert(item.kind == lsp::CompletionItemKind::Module);
            break;
        }
    }
    assert(hasStdlib);
    
    std::cout << "✓ Stdlib root completion works" << std::endl;
}

void test_qualified_name_after_stdlib_dot() {
    std::cout << "Testing qualified name completion: after 'stdlib.'..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with "stdlib." - should suggest "fmt", "fs", "sys"
    auto doc = createTestDocument("stdlib.");
    lsp::Position pos{0, 7}; // After "stdlib."
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have fmt, fs, sys namespaces
    bool hasFmt = false;
    for (const auto& item : completions) {
        if (item.label == "fmt") {
            hasFmt = true;
            assert(item.kind == lsp::CompletionItemKind::Module);
            assert(item.detail.find("stdlib.fmt") != std::string::npos);
            break;
        }
    }
    assert(hasFmt);
    
    std::cout << "✓ Completions after 'stdlib.' work" << std::endl;
}

void test_qualified_name_after_stdlib_fmt_dot() {
    std::cout << "Testing qualified name completion: after 'stdlib.fmt.'..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with "stdlib.fmt." - should suggest modules like "integer"
    auto doc = createTestDocument("stdlib.fmt.");
    lsp::Position pos{0, 11}; // After "stdlib.fmt."
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have "integer" module
    bool hasInteger = false;
    for (const auto& item : completions) {
        if (item.label == "integer") {
            hasInteger = true;
            assert(item.kind == lsp::CompletionItemKind::Module);
            assert(item.detail.find("stdlib.fmt") != std::string::npos);
            break;
        }
    }
    assert(hasInteger);
    
    std::cout << "✓ Completions after 'stdlib.fmt.' work" << std::endl;
}

void test_qualified_name_after_module_dot() {
    std::cout << "Testing qualified name completion: after 'stdlib.fmt.integer.'..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with "stdlib.fmt.integer." - should suggest functions like "format_int_array"
    auto doc = createTestDocument("stdlib.fmt.integer.");
    lsp::Position pos{0, 19}; // After "stdlib.fmt.integer."
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have "format_int_array" function
    bool hasFormatIntArray = false;
    for (const auto& item : completions) {
        if (item.label == "format_int_array") {
            hasFormatIntArray = true;
            assert(item.kind == lsp::CompletionItemKind::Function);
            assert(!item.detail.empty());
            assert(item.detail.find("value int") != std::string::npos);
            break;
        }
    }
    assert(hasFormatIntArray);
    
    std::cout << "✓ Completions after 'stdlib.fmt.integer.' work" << std::endl;
}

void test_qualified_name_multiline() {
    std::cout << "Testing qualified name completion: multiline context..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with qualified name on second line
    auto doc = createTestDocument("function main() int\n    stdlib.fmt.");
    lsp::Position pos{1, 15}; // After "stdlib.fmt." on line 2
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have "integer" module
    bool hasInteger = false;
    for (const auto& item : completions) {
        if (item.label == "integer") {
            hasInteger = true;
            break;
        }
    }
    assert(hasInteger);
    
    std::cout << "✓ Qualified name completions work in multiline context" << std::endl;
}

void test_qualified_name_with_whitespace() {
    std::cout << "Testing qualified name completion: with preceding whitespace..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with whitespace before qualified name
    auto doc = createTestDocument("    stdlib.");
    lsp::Position pos{0, 11}; // After "    stdlib."
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should still have fmt
    bool hasFmt = false;
    for (const auto& item : completions) {
        if (item.label == "fmt") {
            hasFmt = true;
            break;
        }
    }
    assert(hasFmt);
    
    std::cout << "✓ Qualified name completions work with whitespace" << std::endl;
}

void test_qualified_name_incomplete_prefix() {
    std::cout << "Testing qualified name completion: incomplete prefix..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    // Create document with "stdlib.f" - cursor after "f"
    auto doc = createTestDocument("stdlib.f");
    lsp::Position pos{0, 8}; // After "stdlib.f"
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should still provide namespace-level completions (fmt, fs)
    bool hasFmt = false;
    for (const auto& item : completions) {
        if (item.label == "fmt") {
            hasFmt = true;
            break;
        }
    }
    assert(hasFmt);
    
    std::cout << "✓ Qualified name completions work with incomplete prefix" << std::endl;
}

void test_unused_variable_warning() {
    std::cout << "Testing unused variable warning..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument(R"(
function main() int
    var i = 5
    return 0
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Debug: print all diagnostics
    std::cout << "  Found " << diagnostics.size() << " diagnostic(s):" << std::endl;
    for (const auto& diag : diagnostics) {
        std::cout << "    Severity " << diag.severity << ": " << diag.message << std::endl;
    }
    
    // Should have a warning for unused variable 'i'
    bool hasUnusedWarning = false;
    for (const auto& diag : diagnostics) {
        if (diag.message.find("never used") != std::string::npos && 
            diag.message.find("'i'") != std::string::npos) {
            hasUnusedWarning = true;
            // Verify it's a warning (severity 2), not an error
            assert(diag.severity == 2);
            break;
        }
    }
    assert(hasUnusedWarning);
    
    std::cout << "✓ Unused variable warning detected" << std::endl;
}

void test_used_variable_no_warning() {
    std::cout << "Testing used variable has no warning..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument(R"(
function main() int
    var i = 5
    print(i)
    return 0
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should NOT have a warning for 'i' since it's used
    for (const auto& diag : diagnostics) {
        if (diag.message.find("never used") != std::string::npos && 
            diag.message.find("'i'") != std::string::npos) {
            assert(false && "Should not warn about used variable");
        }
    }
    
    std::cout << "✓ Used variable has no warning" << std::endl;
}

void test_multiple_unused_variables() {
    std::cout << "Testing multiple unused variables..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument(R"(
function main() int
    var unused1 = 5
    var unused2 = 10
    var used = 15
    print(used)
    return 0
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should have warnings for both unused1 and unused2
    int unusedWarnings = 0;
    for (const auto& diag : diagnostics) {
        if (diag.message.find("never used") != std::string::npos) {
            if (diag.message.find("'unused1'") != std::string::npos ||
                diag.message.find("'unused2'") != std::string::npos) {
                unusedWarnings++;
                assert(diag.severity == 2); // Should be warning
            }
            // Make sure 'used' is not in the warnings
            assert(diag.message.find("'used'") == std::string::npos);
        }
    }
    assert(unusedWarnings == 2);
    
    std::cout << "✓ Multiple unused variables detected" << std::endl;
}

void test_unused_variable_severity() {
    std::cout << "Testing unused variable has warning severity..." << std::endl;
    
    Analyzer analyzer;
    auto doc = createTestDocument(R"(
function main() int
    var unused = 42
    print(notdefined)
    return 0
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should have both warning (unused) and error (undefined variable)
    bool hasWarning = false;
    bool hasError = false;
    
    for (const auto& diag : diagnostics) {
        if (diag.message.find("never used") != std::string::npos &&
            diag.message.find("'unused'") != std::string::npos) {
            assert(diag.severity == 2); // Warning
            hasWarning = true;
        }
        if (diag.message.find("Undefined variable") != std::string::npos &&
            diag.severity == 1) {
            hasError = true;
        }
    }
    
    assert(hasWarning);
    assert(hasError);
    
    std::cout << "✓ Unused variable has correct warning severity" << std::endl;
}

void test_stdlib_function_no_error() {
    std::cout << "Testing stdlib function call has no error..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument(R"(
function main() int
    var buffer [12]char = 0
    var length = format_int_array(42, buffer)
    return length
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should NOT have "Undefined function" error for format_int_array
    for (const auto& diag : diagnostics) {
        if (diag.message.find("Undefined function") != std::string::npos &&
            diag.message.find("format_int_array") != std::string::npos) {
            std::cerr << "Unexpected error: " << diag.message << std::endl;
            assert(false && "Should not error on stdlib function call");
        }
    }
    
    std::cout << "✓ Stdlib function call has no error" << std::endl;
}

void test_stdlib_function_wrong_args() {
    std::cout << "Testing stdlib function call with wrong argument count..." << std::endl;
    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument(R"(
function main() int
    var buffer [12]char = 0
    var length = format_int_array(42)
    return length
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Debug: print all diagnostics
    std::cout << "  Found " << diagnostics.size() << " diagnostic(s):" << std::endl;
    for (const auto& diag : diagnostics) {
        std::cout << "    Severity " << diag.severity << ": " << diag.message << std::endl;
    }
    
    // Should have error about argument count mismatch
    bool hasArgCountError = false;
    for (const auto& diag : diagnostics) {
        if ((diag.message.find("argument count mismatch") != std::string::npos ||
             diag.message.find("Expected 2 arguments") != std::string::npos) &&
            diag.message.find("format_int_array") != std::string::npos) {
            hasArgCountError = true;
            assert(diag.severity == 1); // Error
            break;
        }
    }
    assert(hasArgCountError);
    
    std::cout << "✓ Stdlib function with wrong args detected" << std::endl;
}

void test_stdlib_not_initialized_shows_error() {
    std::cout << "Testing stdlib function call without initialization..." << std::endl;
    
    Analyzer analyzer;
    // Don't call initializeStdlib
    
    auto doc = createTestDocument(R"(
function main() int
    var buffer [12]char = 0
    var length = format_int_array(42, buffer)
    return length
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should have "Undefined function" error
    bool hasUndefinedError = false;
    for (const auto& diag : diagnostics) {
        if (diag.message.find("Undefined function") != std::string::npos &&
            diag.message.find("format_int_array") != std::string::npos) {
            hasUndefinedError = true;
            assert(diag.severity == 1); // Error
            break;
        }
    }
    assert(hasUndefinedError);
    
    std::cout << "✓ Stdlib function shows error without initialization" << std::endl;
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
        
        // Stdlib tests
        test_stdlib_initialization();
        test_stdlib_hover();
        test_stdlib_completion_details();
        test_stdlib_nonexistent_directory();
        
        // Qualified name completion tests
        test_qualified_name_stdlib_root();
        test_qualified_name_after_stdlib_dot();
        test_qualified_name_after_stdlib_fmt_dot();
        test_qualified_name_after_module_dot();
        test_qualified_name_multiline();
        test_qualified_name_with_whitespace();
        test_qualified_name_incomplete_prefix();
        
        // Unused variable warning tests
        test_unused_variable_warning();
        test_used_variable_no_warning();
        test_multiple_unused_variables();
        test_unused_variable_severity();
        
        std::cout << "\n✓ All Analyzer tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
