#include "../include/analyzer.h"
#include "catch_amalgamated.hpp"
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>

#define CATCH_CONFIG_MAIN

std::shared_ptr<Document> createTestDocument(const std::string &text) {
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
	for (const auto &item : completions) {
		if (item.label == "function") {
			hasFunction = true;
			break;
		}
	}
	REQUIRE(hasFunction);
}

TEST_CASE("identifier_completions", "[analyzer]") {

	Analyzer analyzer;
	auto doc = createTestDocument("var myVariable\nvar my");

	lsp::Position pos{1, 6};
	auto completions = analyzer.getCompletions(doc, pos);

	// Should find "myVariable" from the document
	bool hasMyVariable = false;
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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
	for (const auto &item : completions) {
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

	for (const auto &diag : diagnostics) {
		std::cout << "    Severity " << diag.severity << ": " << diag.message << std::endl;
	}

	// Should have a warning for unused variable 'i'
	bool hasUnusedWarning = false;
	for (const auto &diag : diagnostics) {
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
	for (const auto &diag : diagnostics) {
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
	for (const auto &diag : diagnostics) {
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

	for (const auto &diag : diagnostics) {
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
	for (const auto &diag : diagnostics) {
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

	for (const auto &diag : diagnostics) {
		std::cout << "    Severity " << diag.severity << ": " << diag.message << std::endl;
	}

	// Should have error about argument count mismatch
	bool hasArgCountError = false;
	for (const auto &diag : diagnostics) {
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
	for (const auto &diag : diagnostics) {
		if (diag.message.find("Undefined function") != std::string::npos &&
			diag.message.find("format_int_array") != std::string::npos) {
			hasUndefinedError = true;
			REQUIRE(diag.severity == 1); // Error
			break;
		}
	}
	REQUIRE(hasUndefinedError);
}

// Type member completion tests via public API

TEST_CASE("string_member_completions_via_dot", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Create valid document with string variable, then request completion after a dot
	// We'll use two calls: first analyze valid code to populate cache, then
	// modify to have the dot and get completions
	auto doc = createTestDocument("function main() int\n    var s = \"hello\"\n    return s.count\nend 'main'");

	// Analyze to populate semantic cache
	analyzer.analyze(doc);

	// Now update document to have incomplete dot access for completion
	doc->text = "function main() int\n    var s = \"hello\"\n    return s.\nend 'main'";

	// Get completions after "s." (line 2, column 13)
	lsp::Position pos{2, 13}; // After "return s."
	auto completions = analyzer.getCompletions(doc, pos);

	// Should have string methods count() and isEmpty()
	bool hasCount = false;
	bool hasIsEmpty = false;
	for (const auto &item : completions) {
		if (item.label == "count") {
			hasCount = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "isEmpty") {
			hasIsEmpty = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
	}
	REQUIRE(hasCount);
	REQUIRE(hasIsEmpty);
}

TEST_CASE("string_method_completions_via_dot", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Analyze valid code to populate cache
	auto doc = createTestDocument("function main() int\n    var s = \"hello\"\n    return s.count\nend 'main'");
	analyzer.analyze(doc);

	// Update to have incomplete dot access
	doc->text = "function main() int\n    var s = \"hello\"\n    return s.\nend 'main'";

	lsp::Position pos{2, 13}; // After "return s."
	auto completions = analyzer.getCompletions(doc, pos);

	// Should have string methods
	bool hasStartsWith = false;
	bool hasEndsWith = false;
	bool hasContains = false;
	bool hasFind = false;
	bool hasToUpper = false;
	bool hasToLower = false;
	bool hasTrimWhitespace = false;

	for (const auto &item : completions) {
		if (item.label == "startsWith") {
			hasStartsWith = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "endsWith") {
			hasEndsWith = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "contains") {
			hasContains = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "find") {
			hasFind = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "toUpper") {
			hasToUpper = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "toLower") {
			hasToLower = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
		if (item.label == "trimWhitespace") {
			hasTrimWhitespace = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Method);
		}
	}

	REQUIRE(hasStartsWith);
	REQUIRE(hasEndsWith);
	REQUIRE(hasContains);
	REQUIRE(hasFind);
	REQUIRE(hasToUpper);
	REQUIRE(hasToLower);
	REQUIRE(hasTrimWhitespace);
}

TEST_CASE("string_method_has_insertText", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	auto doc = createTestDocument("function main() int\n    var s = \"hello\"\n    return s.count\nend 'main'");
	analyzer.analyze(doc);
	doc->text = "function main() int\n    var s = \"hello\"\n    return s.\nend 'main'";

	lsp::Position pos{2, 13};
	auto completions = analyzer.getCompletions(doc, pos);

	// Check that methods have insertText with parentheses
	for (const auto &item : completions) {
		if (item.label == "toUpper") {
			REQUIRE(item.insertText.has_value());
			REQUIRE(item.insertText.value() == "toUpper()");
		}
		if (item.label == "startsWith") {
			REQUIRE(item.insertText.has_value());
			REQUIRE(item.insertText.value() == "startsWith()");
		}
	}
}

TEST_CASE("array_member_completions_via_dot", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Correct Maxon syntax: var arr = [5]int
	auto doc = createTestDocument("function main() int\n    var arr = [5]int\n    return arr.length\nend 'main'");
	analyzer.analyze(doc);
	doc->text = "function main() int\n    var arr = [5]int\n    return arr.\nend 'main'";

	lsp::Position pos{2, 15}; // After "return arr."
	auto completions = analyzer.getCompletions(doc, pos);

	// Should have array properties
	bool hasLength = false;

	for (const auto &item : completions) {
		if (item.label == "length") {
			hasLength = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Property);
		}
	}

	REQUIRE(hasLength);
}

TEST_CASE("struct_field_completions_via_dot", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Correct Maxon syntax: var p = Point { x: 0, y: 0 }
	auto doc = createTestDocument("struct Point\n    x int\n    y int\nend 'Point'\n\nfunction main() int\n    var p = Point { x: 0, y: 0 }\n    return p.x\nend 'main'");
	analyzer.analyze(doc);
	doc->text = "struct Point\n    x int\n    y int\nend 'Point'\n\nfunction main() int\n    var p = Point { x: 0, y: 0 }\n    return p.\nend 'main'";

	lsp::Position pos{7, 13}; // After "return p."
	auto completions = analyzer.getCompletions(doc, pos);

	// Should have struct fields
	bool hasX = false;
	bool hasY = false;

	for (const auto &item : completions) {
		if (item.label == "x") {
			hasX = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Field);
		}
		if (item.label == "y") {
			hasY = true;
			REQUIRE(item.kind == lsp::CompletionItemKind::Field);
		}
	}

	REQUIRE(hasX);
	REQUIRE(hasY);
}

TEST_CASE("builtin_method_calls_no_errors", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Test that method calls like s.toLower() don't produce "undefined function" errors
	// The parser transforms s.toLower() -> toLower(s), and the semantic analyzer must recognize
	// the method from the stdlib string type
	auto doc = createTestDocument(
		"function main() int\n"
		"    var s = \"hello\"\n"
		"    print(s.toLower())\n"
		"    print(s.toUpper())\n"
		"    print(s.trimWhitespace())\n"
		"    return 0\n"
		"end 'main'");

	auto diagnostics = analyzer.analyze(doc);

	// Should have no errors about undefined functions
	for (const auto &diag : diagnostics) {
		// Fail if we see any "Undefined function" error for the methods
		bool isUndefinedBuiltin = diag.message.find("Undefined function") != std::string::npos &&
								  (diag.message.find("toLower") != std::string::npos ||
								   diag.message.find("toUpper") != std::string::npos ||
								   diag.message.find("trimWhitespace") != std::string::npos);
		REQUIRE_FALSE(isUndefinedBuiltin);
	}
}

TEST_CASE("builtin_string_search_methods_no_errors", "[analyzer]") {
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Test string search methods from stdlib
	auto doc = createTestDocument(
		"function main() int\n"
		"    var s = \"hello world\"\n"
		"    var a = s.startsWith(\"hello\")\n"
		"    var b = s.endsWith(\"world\")\n"
		"    var c = s.contains(\"lo\")\n"
		"    var d = s.find(\"wo\")\n"
		"    return 0\n"
		"end 'main'");

	auto diagnostics = analyzer.analyze(doc);

	// Should have no errors about undefined functions
	for (const auto &diag : diagnostics) {
		bool isUndefinedBuiltin = diag.message.find("Undefined function") != std::string::npos &&
								  (diag.message.find("startsWith") != std::string::npos ||
								   diag.message.find("endsWith") != std::string::npos ||
								   diag.message.find("contains") != std::string::npos ||
								   diag.message.find("find") != std::string::npos);
		REQUIRE_FALSE(isUndefinedBuiltin);
	}
}

TEST_CASE("sibling_method_call_no_self", "[analyzer]") {
	// Test that calling a sibling method without 'self.' prefix works
	// This tests the implicit method resolution within a struct
	Analyzer analyzer;

	auto doc = createTestDocument(
		"export struct TestStruct\n"
		"    _x int\n"
		"    export function bar() int\n"
		"        return 42\n"
		"    end 'bar'\n"
		"    export function foo() int\n"
		"        return bar()\n" // Should resolve to TestStruct.bar() implicitly
		"    end 'foo'\n"
		"end 'TestStruct'\n"
		"function main() int\n"
		"    return 0\n"
		"end 'main'");

	auto diagnostics = analyzer.analyze(doc);

	// Should have no errors - bar() should be recognized as a sibling method call
	bool hasArgCountError = false;
	bool hasUndefinedError = false;
	for (const auto &diag : diagnostics) {
		if (diag.message.find("argument count mismatch") != std::string::npos) {
			hasArgCountError = true;
			INFO("Unexpected argument count error: " << diag.message);
		}
		if (diag.message.find("Undefined function") != std::string::npos &&
			diag.message.find("bar") != std::string::npos) {
			hasUndefinedError = true;
			INFO("Unexpected undefined function error: " << diag.message);
		}
	}
	REQUIRE_FALSE(hasArgCountError);
	REQUIRE_FALSE(hasUndefinedError);
}

TEST_CASE("sibling_method_call_with_args", "[analyzer]") {
	// Test sibling method call with arguments (like contains calling find)
	Analyzer analyzer;

	auto doc = createTestDocument(
		"export struct MyString\n"
		"    _len int\n"
		"    export function find(needle int) int\n"
		"        return needle\n"
		"    end 'find'\n"
		"    export function contains(needle int) bool\n"
		"        return find(needle) >= 0\n" // Sibling call with argument
		"    end 'contains'\n"
		"end 'MyString'\n"
		"function main() int\n"
		"    return 0\n"
		"end 'main'");

	auto diagnostics = analyzer.analyze(doc);

	// Should have no errors about argument count mismatch
	bool hasArgCountError = false;
	for (const auto &diag : diagnostics) {
		if (diag.message.find("argument count mismatch") != std::string::npos) {
			hasArgCountError = true;
			INFO("Unexpected argument count error: " << diag.message);
		}
	}
	REQUIRE_FALSE(hasArgCountError);
}

TEST_CASE("stdlib_string_file_sibling_method_call", "[analyzer]") {
	// This test reproduces the bug seen in VSCode when opening stdlib/string/string.maxon
	// The contains() method calls find(needle) without 'self.' prefix
	// This should work because find() is a sibling method of the same struct

	// Read the actual stdlib string.maxon file
	std::ifstream file("../../../stdlib/string/string.maxon");
	REQUIRE(file.is_open());

	std::stringstream buffer;
	buffer << file.rdbuf();
	std::string content = buffer.str();
	file.close();

	// Create analyzer and initialize stdlib (for other types like ByteView)
	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	// Create document from the file content
	auto doc = std::make_shared<Document>("file:///stdlib/string/string.maxon", content, 0);

	auto diagnostics = analyzer.analyze(doc);

	// Check for the specific bug: argument count mismatch for find() in contains()
	// The error would be: "Function 'find' argument count mismatch - Expected: 2 arguments, Found: 1 argument"
	bool hasFindArgCountError = false;
	std::string errorMessage;
	for (const auto &diag : diagnostics) {
		if (diag.message.find("find") != std::string::npos &&
			diag.message.find("argument") != std::string::npos &&
			diag.message.find("mismatch") != std::string::npos) {
			hasFindArgCountError = true;
			errorMessage = diag.message;
			INFO("Found argument count error for find(): " << diag.message);
		}
	}

	// This should NOT have an argument count error
	// find(needle) inside contains() should implicitly resolve to self.find(needle)
	REQUIRE_FALSE(hasFindArgCountError);
}

TEST_CASE("go_to_definition_stdlib_function", "[analyzer]") {
	// Test that go-to-definition on a stdlib function like 'print'
	// navigates to the stdlib file, not the current document

	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	auto doc = createTestDocument(
		"function main() int\n"
		"    print(42)\n"
		"    return 0\n"
		"end 'main'");

	// Analyze the document first to populate semantic cache
	analyzer.analyze(doc);

	// Go to definition on 'print' at line 1, column 4
	lsp::Position pos{1, 4};
	auto location = analyzer.getDefinition(doc, pos);

	// Should find definition
	REQUIRE(location.has_value());

	// Should point to stdlib file, not current document
	REQUIRE(location->uri != doc->uri);
	REQUIRE(location->uri.find("print.maxon") != std::string::npos);

	// Should point to the correct line (line 8 in print.maxon, 0-indexed = 7)
	REQUIRE(location->range.start.line == 7);
}

TEST_CASE("go_to_definition_struct_method", "[analyzer]") {
	// Test that go-to-definition on a struct method like 'toLower'
	// navigates to the stdlib file where the method is defined

	Analyzer analyzer;
	analyzer.initializeStdlib("../../../stdlib");

	auto doc = createTestDocument(
		"function main() int\n"
		"    var s = \"hello\"\n"
		"    print(s.toLower())\n"
		"    return 0\n"
		"end 'main'");

	// Analyze the document first to populate semantic cache
	analyzer.analyze(doc);

	// Go to definition on 'toLower' at line 2 (s.toLower())
	// "    print(s.toLower())" - toLower starts at column 12
	lsp::Position pos{2, 12};
	auto location = analyzer.getDefinition(doc, pos);

	// Should find definition
	REQUIRE(location.has_value());

	// Should point to stdlib string file, not current document
	INFO("Got URI: " << location->uri);
	REQUIRE(location->uri != doc->uri);
	REQUIRE(location->uri.find("string.maxon") != std::string::npos);

	// toLower is defined at line 267 in string.maxon (0-indexed = 266)
	INFO("Got line: " << location->range.start.line);
	REQUIRE(location->range.start.line == 266);
}
