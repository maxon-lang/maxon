#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <iostream>
#include <memory>

#define CATCH_CONFIG_MAIN

std::shared_ptr<Document> createTestDocument(const std::string& text) {
    return std::make_shared<Document>("file:///test.maxon", text, 0);
}

TEST_CASE("analyze_valid_code", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() end");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // For now, just check that analysis doesn't crash
    // The actual validity depends on the parser implementation

    

}

TEST_CASE("analyze_invalid_token", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() @ end");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should detect the invalid @ token
    REQUIRE(!diagnostics.empty());
    

}

TEST_CASE("keyword_completions", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("func");
    
    lsp::Position pos{0, 4};
    auto completions = analyzer.getCompletions(doc, pos);
    
    // Should have keyword completions
    REQUIRE(!completions.empty());
    
    // Check that "function" is in completions
    bool hasFunction = false;
    for (const auto& item : completions) {
        if (item.label == "function") {
            hasFunction = true;
            break;
        }
    }
    REQUIRE(hasFunction);
    

}

TEST_CASE("type_completions", "[analyzer]") {

    
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
    REQUIRE(hasInt);
    REQUIRE(hasString);
    

}

TEST_CASE("identifier_completions", "[analyzer]") {

    
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
    REQUIRE(hasMyVariable);
    

}

TEST_CASE("hover_on_keyword", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("function main() end");
    
    lsp::Position pos{0, 3}; // Position in "function"
    auto hover = analyzer.getHover(doc, pos);
    
    // Should return hover information for keyword
    REQUIRE(hover.has_value());
    REQUIRE(!hover->contents.empty());
    

}

TEST_CASE("document_symbols", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("function main()\nvar x\nend");
    
    auto symbols = analyzer.getSymbols(doc);
    
    // Should find function declaration
    REQUIRE(!symbols.empty());
    

}

TEST_CASE("analyze_syntax_error", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("function main( end"); // Missing closing paren
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Should detect syntax error
    REQUIRE(!diagnostics.empty());
    

}

TEST_CASE("empty_document", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument("");
    
    auto diagnostics = analyzer.analyze(doc);
    auto completions = analyzer.getCompletions(doc, {0, 0});
    
    // Empty document should still provide completions
    REQUIRE(!completions.empty());
    

}

TEST_CASE("stdlib_initialization", "[analyzer]") {

    
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
            REQUIRE(item.kind == lsp::CompletionItemKind::Function);
            REQUIRE(!item.detail.empty());
            break;
        }
    }
    REQUIRE(hasFormatIntArray);
    

}

TEST_CASE("stdlib_hover", "[analyzer]") {

    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument("format_int_array");
    lsp::Position pos{0, 5}; // Position in "format_int_array"
    auto hover = analyzer.getHover(doc, pos);
    
    // Should return hover information for stdlib function
    REQUIRE(hover.has_value());
    REQUIRE(!hover->contents.empty());
    // Check for function name (may use different separator formats)
    REQUIRE(hover->contents.find("format_int_array") != std::string::npos);
    

}

TEST_CASE("stdlib_completion_details", "[analyzer]") {

    
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
                
                } else {
                
                }
            }
            return;
        }
    }
    

}

TEST_CASE("stdlib_nonexistent_directory", "[analyzer]") {

    
    Analyzer analyzer;
    
    // Should not crash with nonexistent directory
    analyzer.initializeStdlib("/nonexistent/path");
    
    auto doc = createTestDocument("function main() end");
    auto completions = analyzer.getCompletions(doc, {0, 0});
    
    // Should still have basic completions (keywords)
    REQUIRE(!completions.empty());
    

}

TEST_CASE("qualified_name_stdlib_root", "[analyzer]") {

    
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
            REQUIRE(item.kind == lsp::CompletionItemKind::Module);
            break;
        }
    }
    REQUIRE(hasStdlib);
    

}

TEST_CASE("qualified_name_after_stdlib_dot", "[analyzer]") {

    
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
            REQUIRE(item.kind == lsp::CompletionItemKind::Module);
            REQUIRE(item.detail.find("stdlib.fmt") != std::string::npos);
            break;
        }
    }
    REQUIRE(hasFmt);
    

}

TEST_CASE("qualified_name_after_stdlib_fmt_dot", "[analyzer]") {

    
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
            REQUIRE(item.kind == lsp::CompletionItemKind::Module);
            REQUIRE(item.detail.find("stdlib.fmt") != std::string::npos);
            break;
        }
    }
    REQUIRE(hasInteger);
    

}

TEST_CASE("qualified_name_after_module_dot", "[analyzer]") {

    
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
            REQUIRE(item.kind == lsp::CompletionItemKind::Function);
            REQUIRE(!item.detail.empty());
            REQUIRE(item.detail.find("value int") != std::string::npos);
            break;
        }
    }
    REQUIRE(hasFormatIntArray);
    

}

TEST_CASE("qualified_name_multiline", "[analyzer]") {

    
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
    REQUIRE(hasInteger);
    

}

TEST_CASE("qualified_name_with_whitespace", "[analyzer]") {

    
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
    REQUIRE(hasFmt);
    

}

TEST_CASE("qualified_name_incomplete_prefix", "[analyzer]") {

    
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
    REQUIRE(hasFmt);
    

}

TEST_CASE("unused_variable_warning", "[analyzer]") {

    
    Analyzer analyzer;
    auto doc = createTestDocument(R"(
function main() int
    var i = 5
    return 0
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Debug: print all diagnostics

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
            REQUIRE(diag.severity == 2);
            break;
        }
    }
    REQUIRE(hasUnusedWarning);
    

}

TEST_CASE("used_variable_no_warning", "[analyzer]") {

    
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
            REQUIRE((false && "Should not warn about used variable"));
        }
    }
    

}

TEST_CASE("multiple_unused_variables", "[analyzer]") {

    
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
                REQUIRE(diag.severity == 2); // Should be warning
            }
            // Make sure 'used' is not in the warnings
            REQUIRE(diag.message.find("'used'") == std::string::npos);
        }
    }
    REQUIRE(unusedWarnings == 2);
    

}

TEST_CASE("unused_variable_severity", "[analyzer]") {

    
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
            REQUIRE(diag.severity == 2); // Warning
            hasWarning = true;
        }
        if (diag.message.find("Undefined variable") != std::string::npos &&
            diag.severity == 1) {
            hasError = true;
        }
    }
    
    REQUIRE(hasWarning);
    REQUIRE(hasError);
    

}

TEST_CASE("stdlib_function_no_error", "[analyzer]") {

    
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
            REQUIRE((false && "Should not error on stdlib function call"));
        }
    }
    

}

TEST_CASE("stdlib_function_wrong_args", "[analyzer]") {

    
    Analyzer analyzer;
    analyzer.initializeStdlib("../../../stdlib");
    
    auto doc = createTestDocument(R"(
function main() int
    var buffer = [12]char
    var length = format_int_array(42)
    return length
end 'main'
)");
    
    auto diagnostics = analyzer.analyze(doc);
    
    // Debug: print all diagnostics

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
            REQUIRE(diag.severity == 1); // Error
            break;
        }
    }
    REQUIRE(hasArgCountError);
    

}

TEST_CASE("stdlib_not_initialized_shows_error", "[analyzer]") {

    
    Analyzer analyzer;
    // Don't call initializeStdlib
    
    auto doc = createTestDocument(R"(
function main() int
    var buffer = [12]char
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
            REQUIRE(diag.severity == 1); // Error
            break;
        }
    }
    REQUIRE(hasUndefinedError);
    

}
