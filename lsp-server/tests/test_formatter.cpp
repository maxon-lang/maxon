#include "../include/document_manager.h"
#include "../include/formatter.h"
#include "catch_amalgamated.hpp"
#include <iostream>

std::shared_ptr<Document> createTestDocFormatter(const std::string &text) {
	return std::make_shared<Document>("file:///test.maxon", text, 0);
}

TEST_CASE("format_function_with_tabs", "[formatter]") {
	Formatter formatter;

	// Code with inconsistent indentation and spacing
	std::string source =
		"function main() int\n"
		"  print_int(42)\n"
		"    return 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);

	// Should have one edit replacing the whole document
	REQUIRE(edits.size() == 1);

	// Check that the new text uses tab character
	REQUIRE(edits[0].newText.find('\t') != std::string::npos);
}

TEST_CASE("format_function_with_spaces", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"  print_int(42)\n"
		"    return 0\n"
		"end 'main'";

	// Format with spaces (4 spaces per indent level)
	auto edits = formatter.formatDocument(source, true, 4);

	REQUIRE(edits.size() == 1);

	// Check that edits use space characters for indentation
	REQUIRE(edits[0].newText.find("    ") != std::string::npos);
}

TEST_CASE("format_if_statement_with_tabs", "[formatter]") {
	Formatter formatter;

	std::string source =
		"if x > 0 'check'\n"
		"  print_int(x)\n"
		"end 'check'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// If statement body should be indented with one tab
	// "if x > 0 'check'\n\tprint_int(x)\nend 'check'\n"
	REQUIRE(edits[0].newText.find("\tprint_int(x)") != std::string::npos);
}

TEST_CASE("format_nested_blocks", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function outer() int\n"
		"if x > 0 'check'\n"
		"print_int(x)\n"
		"end 'check'\n"
		"return 0\n"
		"end 'outer'";

	auto edits = formatter.formatDocument(source, false, 4);

	REQUIRE(edits.size() == 1);

	// Check indentation levels
	// function outer
	// \tif x > 0
	// \t\tprint_int(x)
	// \tend
	// \treturn 0
	// end

	REQUIRE(edits[0].newText.find("\t\tprint_int(x)") != std::string::npos);
}

TEST_CASE("format_removes_trailing_whitespace", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int   \n"
		"\treturn 0   \n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Check that trailing spaces are removed
	std::string newText = edits[0].newText;
	std::stringstream ss(newText);
	std::string line;
	while (std::getline(ss, line)) {
		if (!line.empty()) {
			REQUIRE(line.find_last_not_of(" \t") == line.length() - 1);
		}
	}
}

TEST_CASE("format_empty_lines_unchanged", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"\n"
		"\tprint_int(42)\n"
		"\n"
		"\treturn 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Check that blank lines are preserved (as empty lines)
	// "function main() int\n\n\tprint_int(42)\n\n\treturn 0\nend 'main'\n"
	REQUIRE(edits[0].newText.find("\n\n") != std::string::npos);
}

TEST_CASE("format_while_loop", "[formatter]") {
	Formatter formatter;

	std::string source =
		"while i < 10 'loop'\n"
		"  print_int(i)\n"
		"  i = i + 1\n"
		"end 'loop'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Loop body should be indented
	REQUIRE(edits[0].newText.find("\tprint_int(i)") != std::string::npos);
	REQUIRE(edits[0].newText.find("\ti = i + 1") != std::string::npos);
}

TEST_CASE("format_tab_size_respected", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"print_int(42)\n"
		"return 0\n"
		"end 'main'";

	// Format with 2-space indent (but still spaces mode)
	auto edits = formatter.formatDocument(source, true, 2);
	REQUIRE(edits.size() == 1);

	// Check for 2-space indentation
	REQUIRE(edits[0].newText.find("  print_int(42)") != std::string::npos);
}

TEST_CASE("format_range_formatting", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"  print_int(42)\n"
		"  return 0\n"
		"end 'main'";

	lsp::Range range;
	range.start = {1, 0};
	range.end = {2, 0};

	auto edits = formatter.formatRange(source, range, false, 4);

	// Range formatting currently formats the whole document
	REQUIRE(edits.size() == 1);
}

TEST_CASE("format_windows_line_endings", "[formatter]") {
	Formatter formatter;

	// File with Windows line endings (\r\n)
	std::string source =
		"function main() int\r\n"
		"\tvar x = 5\r\n"
		"\r\n"
		"\tif x > 0 'check'\r\n"
		"\t\tprint_int(x)\r\n"
		"\tend 'check'\r\n"
		"\treturn 0\r\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should handle Windows line endings correctly and convert to LF
	REQUIRE(edits[0].newText.find('\r') == std::string::npos);
	REQUIRE(edits[0].newText.find('\n') != std::string::npos);
}

TEST_CASE("format_multiple_blank_lines", "[formatter]") {
	Formatter formatter;

	// Code with multiple consecutive blank lines and blank lines with whitespace
	std::string source =
		"function main() int\n"
		"\tvar x = 5\n"
		"\n"
		"  \n" // Blank line with spaces
		"\t\n" // Blank line with tab
		"\tif x > 0 'check'\n"
		"\t\tprint_int(x)\n"
		"\tend 'check'\n"
		"\treturn 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should collapse multiple blank lines into one
	// "function main() int\n\tvar x = 5\n\n\tif x > 0 'check'..."
	// Check that we don't have 3 newlines in a row
	REQUIRE(edits[0].newText.find("\n\n\n") == std::string::npos);
	// Check that we do have 2 newlines (one blank line)
	REQUIRE(edits[0].newText.find("\n\n") != std::string::npos);
}

TEST_CASE("format_blank_line_with_whitespace", "[formatter]") {
	Formatter formatter;

	// Code where blank lines have tabs/spaces that need cleaning
	std::string source =
		"function main() int\n"
		"\tvar x = 5\n"
		"\t\t\n" // Blank line with 2 tabs
		"\tif x > 0 'check'\n"
		"\t\tprint_int(x)\n"
		"\tend 'check'\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should remove whitespace from blank line
	// Check that we don't have "\n\t\t\n"
	REQUIRE(edits[0].newText.find("\n\t\t\n") == std::string::npos);
	// Check that we have "\n\n"
	REQUIRE(edits[0].newText.find("\n\n") != std::string::npos);
}

TEST_CASE("format_file_ends_with_newline", "[formatter]") {
	Formatter formatter;

	// Source code that doesn't end with a newline
	std::string source = "function main() int\n\treturn 0\nend 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should have a newline at the end
	REQUIRE(edits[0].newText.back() == '\n');
}

TEST_CASE("format_file_already_ends_with_newline", "[formatter]") {
	Formatter formatter;

	// Source code that already ends with a newline
	std::string source = "function main() int\n\treturn 0\nend 'main'\n";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should still end with exactly one newline
	REQUIRE(edits[0].newText.back() == '\n');
	// Check it doesn't end with \n\n (unless there was a blank line before end, but here there isn't)
	REQUIRE(edits[0].newText.substr(edits[0].newText.length() - 2) != "\n\n");
}

TEST_CASE("format_empty_source", "[formatter]") {
	Formatter formatter;

	// Empty source should not get a newline added
	std::string source = "";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Empty source should result in empty text
	REQUIRE(edits[0].newText.empty());
}

TEST_CASE("format_consolidate_consecutive_blanks", "[formatter]") {
	Formatter formatter;

	// This simulates the hello-world.maxon file with consecutive blank lines
	std::string source =
		"function main() int\n"
		"\tvar x = 5\n"
		"\tvar i = 3\n"
		"\n" // First blank line
		"\n" // Second blank line - should be removed
		"\twhile i > 0 'loop'\n"
		"\t\tx = x + 2\n"
		"\t\ti = i - 1\n"
		"\tend 'loop'\n"
		"\treturn x\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Should have only one blank line
	REQUIRE(edits[0].newText.find("\n\n\n") == std::string::npos);
	REQUIRE(edits[0].newText.find("\n\n") != std::string::npos);
}

TEST_CASE("format_removes_trailing_blank_lines", "[formatter]") {
	Formatter formatter;
	std::string source = "function main() int\n\treturn 0\nend 'main'\n\n\n";
	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);
	// Should end with single newline
	REQUIRE(edits[0].newText.back() == '\n');
	// Should NOT end with \n\n
	REQUIRE(edits[0].newText.substr(edits[0].newText.length() - 2) != "\n\n");
}

TEST_CASE("format_struct_literal_multiline", "[formatter]") {
	Formatter formatter;

	// A function that creates a struct literal spanning multiple lines
	// The 'return' statement after the struct literal should be properly indented
	std::string source =
		"function createIterator() Iterator\n"
		"    var it = Iterator{\n"
		"        current: 0,\n"
		"        limit: 10,\n"
		"        step: 1\n"
		"    }\n"
		"    return it\n"
		"end 'createIterator'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// The 'return it' line should be indented at function level (1 tab), not at base level
	REQUIRE(edits[0].newText.find("\treturn it") != std::string::npos);
	// It should NOT be at base level (no indent)
	REQUIRE(edits[0].newText.find("\nreturn it") == std::string::npos);
}

TEST_CASE("format_struct_literal_no_trailing_newline", "[formatter]") {
	Formatter formatter;

	// Same as above but WITHOUT a trailing newline after the closing brace
	// This simulates an edge case in range.maxon where the file was formatted incorrectly
	std::string source =
		"function range_step(start int, end_exclusive int, step int) Iterator\n"
		"    var it = Iterator{\n"
		"        current: start,\n"
		"        limit: end_exclusive,\n"
		"        step: step\n"
		"    }\n"	  // Closing brace for struct literal
		"return it\n" // This line is at column 0 (BUG!) - should be indented
		"end 'range_step'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::cerr << "Formatted output:\n"
			  << edits[0].newText << std::endl;

	// The 'return it' line should be indented at function level (1 tab)
	REQUIRE(edits[0].newText.find("\treturn it") != std::string::npos);
	// It should NOT be at base level (no indent)
	REQUIRE(edits[0].newText.find("\nreturn it") == std::string::npos);
}

TEST_CASE("format_range_is_valid", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"return 0\n"
		"end 'main'\n";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// The range should be valid - start line >= 0 and end line >= start line
	REQUIRE(edits[0].range.start.line >= 0);
	REQUIRE(edits[0].range.start.character >= 0);
	REQUIRE(edits[0].range.end.line >= 0);
	REQUIRE(edits[0].range.end.character >= 0);
	REQUIRE(edits[0].range.end.line >= edits[0].range.start.line);

	// For a 4-line source (lines 0, 1, 2, 3), end line should be 3 (last line index)
	// But we're including a trailing newline, so the actual line count might be 4
	// Let's just ensure it's in a reasonable range
	std::cerr << "Range: start=" << edits[0].range.start.line << "," << edits[0].range.start.character
			  << " end=" << edits[0].range.end.line << "," << edits[0].range.end.character << std::endl;
}

TEST_CASE("format_range_is_valid_for_short_file", "[formatter]") {
	Formatter formatter;

	// Single line file
	std::string source = "return 0\n";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	REQUIRE(edits[0].range.start.line >= 0);
	REQUIRE(edits[0].range.start.character >= 0);
	REQUIRE(edits[0].range.end.line >= 0);
	REQUIRE(edits[0].range.end.character >= 0);
	REQUIRE(edits[0].range.end.line >= edits[0].range.start.line);

	std::cerr << "Single line range: start=" << edits[0].range.start.line << "," << edits[0].range.start.character
			  << " end=" << edits[0].range.end.line << "," << edits[0].range.end.character << std::endl;
}

TEST_CASE("format_struct_literal_preserves_function_indent", "[formatter]") {
	Formatter formatter;

	// This is the exact pattern from range.maxon that was failing
	// The struct literal should NOT affect the indentation of code after it
	std::string source =
		"export function range(start int, end_exclusive int) Iterator\n"
		"var it = Iterator{\n"
		"current: start,\n"
		"limit: end_exclusive,\n"
		"step: 1\n"
		"}\n"
		"return it\n"
		"end 'range'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Formatted result:\n"
			  << result << std::endl;

	// Check structure:
	// export function range(...) Iterator   <- level 0
	// \tvar it = Iterator{                  <- level 1 (inside function)
	// \t\tcurrent: start,                   <- level 2 (inside struct literal)
	// \t\tlimit: end_exclusive,             <- level 2
	// \t\tstep: 1                           <- level 2
	// \t}                                   <- level 1 (closing brace)
	// \treturn it                           <- level 1 (still inside function!)
	// end 'range'                           <- level 0

	REQUIRE(result.find("\tvar it = Iterator{") != std::string::npos);
	REQUIRE(result.find("\t\tcurrent: start") != std::string::npos);
	REQUIRE(result.find("\t\tlimit: end_exclusive") != std::string::npos);
	REQUIRE(result.find("\t\tstep: 1") != std::string::npos);
	REQUIRE(result.find("\t}") != std::string::npos);
	REQUIRE(result.find("\treturn it") != std::string::npos);
	REQUIRE(result.find("\nreturn it") == std::string::npos); // NOT at column 0
	REQUIRE(result.find("end 'range'") != std::string::npos);
	REQUIRE(result.find("\tend 'range'") == std::string::npos); // end should NOT be indented
}

TEST_CASE("format_multiple_struct_literals_in_function", "[formatter]") {
	Formatter formatter;

	// Two struct literals in sequence
	std::string source =
		"function test() int\n"
		"var a = Point{\n"
		"x: 1,\n"
		"y: 2\n"
		"}\n"
		"var b = Point{\n"
		"x: 3,\n"
		"y: 4\n"
		"}\n"
		"return 0\n"
		"end 'test'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;

	// Both var statements should be at level 1
	// Both struct literal contents should be at level 2
	// return should be at level 1
	REQUIRE(result.find("\tvar a = Point{") != std::string::npos);
	REQUIRE(result.find("\t\tx: 1") != std::string::npos);
	REQUIRE(result.find("\tvar b = Point{") != std::string::npos);
	REQUIRE(result.find("\treturn 0") != std::string::npos);
}

// ============================================
// If/Else formatting tests
// ============================================

TEST_CASE("format_single_line_if_with_then", "[formatter]") {
	Formatter formatter;

	// Single-line if with then keyword (no block identifier needed)
	std::string source =
		"function main() int\n"
		"var x = 10\n"
		"if x > 5 then return 1\n"
		"return 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Single-line if with then:\n"
			  << result << std::endl;

	// All statements inside function should be at level 1
	REQUIRE(result.find("\tvar x = 10") != std::string::npos);
	REQUIRE(result.find("\tif x > 5 then return 1") != std::string::npos);
	REQUIRE(result.find("\treturn 0") != std::string::npos);
}

TEST_CASE("format_single_line_if_else_with_then", "[formatter]") {
	Formatter formatter;

	// Single-line if-else - must be entirely on one line
	std::string source =
		"function main() int\n"
		"var x = 3\n"
		"if x > 5 then return 1 else return 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Single-line if-else with then:\n"
			  << result << std::endl;

	// All statements should be at level 1
	REQUIRE(result.find("\tvar x = 3") != std::string::npos);
	REQUIRE(result.find("\tif x > 5 then return 1 else return 0") != std::string::npos);
}

TEST_CASE("format_multiline_if_with_block_id", "[formatter]") {
	Formatter formatter;

	// Multi-line if with block identifier
	std::string source =
		"function main() int\n"
		"var x = 10\n"
		"if x > 5 'check'\n"
		"return 1\n"
		"end 'check'\n"
		"return 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Multi-line if with block id:\n"
			  << result << std::endl;

	// function body at level 1
	REQUIRE(result.find("\tvar x = 10") != std::string::npos);
	REQUIRE(result.find("\tif x > 5 'check'") != std::string::npos);
	// if body at level 2
	REQUIRE(result.find("\t\treturn 1") != std::string::npos);
	// end at level 1
	REQUIRE(result.find("\tend 'check'") != std::string::npos);
	// after if block, back to level 1
	REQUIRE(result.find("\treturn 0") != std::string::npos);
}

TEST_CASE("format_multiline_if_else_with_block_id", "[formatter]") {
	Formatter formatter;

	// Multi-line if-else with block identifier
	std::string source =
		"function main() int\n"
		"var x = 5\n"
		"if x == 5 'check'\n"
		"return 1\n"
		"else 'check'\n"
		"return 0\n"
		"end 'check'\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Multi-line if-else with block id:\n"
			  << result << std::endl;

	// function body at level 1
	REQUIRE(result.find("\tvar x = 5") != std::string::npos);
	REQUIRE(result.find("\tif x == 5 'check'") != std::string::npos);
	// if body at level 2
	REQUIRE(result.find("\t\treturn 1") != std::string::npos);
	// else at level 1 (same as if)
	REQUIRE(result.find("\telse 'check'") != std::string::npos);
	// else body at level 2
	REQUIRE(result.find("\t\treturn 0") != std::string::npos);
	// end at level 1
	REQUIRE(result.find("\tend 'check'") != std::string::npos);
}

TEST_CASE("format_nested_if_else", "[formatter]") {
	Formatter formatter;

	// Nested if-else (else-if pattern)
	std::string source =
		"function main() int\n"
		"var x = 3\n"
		"if x == 1 'check'\n"
		"return 1\n"
		"else 'check'\n"
		"if x == 2 'inner'\n"
		"return 2\n"
		"else 'inner'\n"
		"return 3\n"
		"end 'inner'\n"
		"end 'check'\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Nested if-else:\n"
			  << result << std::endl;

	// Level 1: function body
	REQUIRE(result.find("\tvar x = 3") != std::string::npos);
	REQUIRE(result.find("\tif x == 1 'check'") != std::string::npos);
	// Level 2: outer if body
	REQUIRE(result.find("\t\treturn 1") != std::string::npos);
	// Level 1: else (same level as if)
	REQUIRE(result.find("\telse 'check'") != std::string::npos);
	// Level 2: inner if inside else body
	REQUIRE(result.find("\t\tif x == 2 'inner'") != std::string::npos);
	// Level 3: inner if body
	REQUIRE(result.find("\t\t\treturn 2") != std::string::npos);
	// Level 2: inner else
	REQUIRE(result.find("\t\telse 'inner'") != std::string::npos);
	// Level 3: inner else body
	REQUIRE(result.find("\t\t\treturn 3") != std::string::npos);
	// Level 2: inner end
	REQUIRE(result.find("\t\tend 'inner'") != std::string::npos);
	// Level 1: outer end
	REQUIRE(result.find("\tend 'check'") != std::string::npos);
}

TEST_CASE("format_if_in_while_loop", "[formatter]") {
	Formatter formatter;

	// If-else inside a while loop
	std::string source =
		"function main() int\n"
		"var i = 0\n"
		"var sum = 0\n"
		"while i < 10 'loop'\n"
		"if i > 5 'check'\n"
		"sum = sum + i\n"
		"else 'check'\n"
		"sum = sum + 1\n"
		"end 'check'\n"
		"i = i + 1\n"
		"end 'loop'\n"
		"return sum\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "If-else in while loop:\n"
			  << result << std::endl;

	// Level 1: function body
	REQUIRE(result.find("\tvar i = 0") != std::string::npos);
	REQUIRE(result.find("\twhile i < 10 'loop'") != std::string::npos);
	// Level 2: while body
	REQUIRE(result.find("\t\tif i > 5 'check'") != std::string::npos);
	// Level 3: if body
	REQUIRE(result.find("\t\t\tsum = sum + i") != std::string::npos);
	// Level 2: else
	REQUIRE(result.find("\t\telse 'check'") != std::string::npos);
	// Level 3: else body
	REQUIRE(result.find("\t\t\tsum = sum + 1") != std::string::npos);
	// Level 2: end check
	REQUIRE(result.find("\t\tend 'check'") != std::string::npos);
	// Level 2: after if, still in while
	REQUIRE(result.find("\t\ti = i + 1") != std::string::npos);
	// Level 1: end loop
	REQUIRE(result.find("\tend 'loop'") != std::string::npos);
	// Level 1: after while
	REQUIRE(result.find("\treturn sum") != std::string::npos);
}

TEST_CASE("format_deeply_nested_else_if_chain", "[formatter]") {
	Formatter formatter;

	// Deep else-if chain (common pattern)
	std::string source =
		"function grade(score int) int\n"
		"if score >= 90 'a'\n"
		"return 4\n"
		"else 'a'\n"
		"if score >= 80 'b'\n"
		"return 3\n"
		"else 'b'\n"
		"if score >= 70 'c'\n"
		"return 2\n"
		"else 'c'\n"
		"return 1\n"
		"end 'c'\n"
		"end 'b'\n"
		"end 'a'\n"
		"end 'grade'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Deeply nested else-if chain:\n"
			  << result << std::endl;

	// Check proper nesting levels
	REQUIRE(result.find("function grade(score int) int") != std::string::npos);
	REQUIRE(result.find("\tif score >= 90 'a'") != std::string::npos);
	REQUIRE(result.find("\t\treturn 4") != std::string::npos);
	REQUIRE(result.find("\telse 'a'") != std::string::npos);
	REQUIRE(result.find("\t\tif score >= 80 'b'") != std::string::npos);
	REQUIRE(result.find("\t\t\treturn 3") != std::string::npos);
	REQUIRE(result.find("\t\telse 'b'") != std::string::npos);
	REQUIRE(result.find("\t\t\tif score >= 70 'c'") != std::string::npos);
	REQUIRE(result.find("\t\t\t\treturn 2") != std::string::npos);
	REQUIRE(result.find("\t\t\telse 'c'") != std::string::npos);
	REQUIRE(result.find("\t\t\t\treturn 1") != std::string::npos);
	REQUIRE(result.find("\t\t\tend 'c'") != std::string::npos);
	REQUIRE(result.find("\t\tend 'b'") != std::string::npos);
	REQUIRE(result.find("\tend 'a'") != std::string::npos);
	REQUIRE(result.find("end 'grade'") != std::string::npos);
}

TEST_CASE("format_multiline_if_single_line_else", "[formatter]") {
	Formatter formatter;

	// Multi-line if with single-line else (else 'id' <statement>)
	// This is: if block has multiple lines, else has statement on same line as else 'id'
	std::string source =
		"function main() int\n"
		"var x = 10\n"
		"if x > 5 'check'\n"
		"return 1\n"
		"else 'check' return 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Multi-line if, single-line else:\n"
			  << result << std::endl;

	// Level 1: function body
	REQUIRE(result.find("\tvar x = 10") != std::string::npos);
	REQUIRE(result.find("\tif x > 5 'check'") != std::string::npos);
	// Level 2: if body
	REQUIRE(result.find("\t\treturn 1") != std::string::npos);
	// Level 1: else 'check' return 0 (single line, no indent change after)
	REQUIRE(result.find("\telse 'check' return 0") != std::string::npos);
}

TEST_CASE("format_single_line_if_multiline_else", "[formatter]") {
	Formatter formatter;

	// Single-line if with multi-line else
	std::string source =
		"function main() int\n"
		"var x = 3\n"
		"if x > 5 then return 1 else 'fallback'\n"
		"return 0\n"
		"end 'fallback'\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Single-line if, multi-line else:\n"
			  << result << std::endl;

	// Level 1: function body
	REQUIRE(result.find("\tvar x = 3") != std::string::npos);
	REQUIRE(result.find("\tif x > 5 then return 1 else 'fallback'") != std::string::npos);
	// Level 2: else body
	REQUIRE(result.find("\t\treturn 0") != std::string::npos);
	// Level 1: end fallback
	REQUIRE(result.find("\tend 'fallback'") != std::string::npos);
}

// ============================================
// Interface formatting tests
// ============================================

TEST_CASE("format_interface_declaration", "[formatter]") {
	Formatter formatter;

	// Basic interface with method declarations
	std::string source =
		"interface Accumulator uses Item\n"
		"function add(self Self, item Item) Self\n"
		"function total(self Self) int\n"
		"end 'Accumulator'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Interface declaration:\n"
			  << result << std::endl;

	// Interface at level 0
	REQUIRE(result.find("interface Accumulator uses Item") != std::string::npos);
	// Method declarations at level 1
	REQUIRE(result.find("\tfunction add(self Self, item Item) Self") != std::string::npos);
	REQUIRE(result.find("\tfunction total(self Self) int") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'Accumulator'") != std::string::npos);
	REQUIRE(result.find("\tend 'Accumulator'") == std::string::npos);
}

TEST_CASE("format_interface_with_struct_implementation", "[formatter]") {
	Formatter formatter;

	// Interface with implementing struct
	std::string source =
		"interface Accumulator uses Item\n"
		"function add(self Self, item Item) Self\n"
		"function total(self Self) int\n"
		"end 'Accumulator'\n"
		"\n"
		"struct IntSum is Accumulator with int\n"
		"var sum int\n"
		"\n"
		"function Accumulator.add(self IntSum, item int) IntSum\n"
		"return IntSum{sum: self.sum + item}\n"
		"end 'add'\n"
		"\n"
		"function Accumulator.total(self IntSum) int\n"
		"return self.sum\n"
		"end 'total'\n"
		"end 'IntSum'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Interface with struct implementation:\n"
			  << result << std::endl;

	// Interface at level 0
	REQUIRE(result.find("interface Accumulator uses Item") != std::string::npos);
	REQUIRE(result.find("\tfunction add(self Self, item Item) Self") != std::string::npos);
	REQUIRE(result.find("\tfunction total(self Self) int") != std::string::npos);
	REQUIRE(result.find("end 'Accumulator'") != std::string::npos);

	// Struct at level 0
	REQUIRE(result.find("struct IntSum is Accumulator with int") != std::string::npos);
	REQUIRE(result.find("\tvar sum int") != std::string::npos);

	// Functions inside struct at level 1
	REQUIRE(result.find("\tfunction Accumulator.add(self IntSum, item int) IntSum") != std::string::npos);
	REQUIRE(result.find("\t\treturn IntSum{sum: self.sum + item}") != std::string::npos);
	REQUIRE(result.find("\tend 'add'") != std::string::npos);

	REQUIRE(result.find("\tfunction Accumulator.total(self IntSum) int") != std::string::npos);
	REQUIRE(result.find("\t\treturn self.sum") != std::string::npos);
	REQUIRE(result.find("\tend 'total'") != std::string::npos);

	// Struct end at level 0
	REQUIRE(result.find("end 'IntSum'") != std::string::npos);
	REQUIRE(result.find("\tend 'IntSum'") == std::string::npos);
}

TEST_CASE("format_exported_interface", "[formatter]") {
	Formatter formatter;

	// Exported interface
	std::string source =
		"export interface Iterator\n"
		"function hasNext(self Self) bool\n"
		"function next(self Self) int\n"
		"end 'Iterator'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Exported interface:\n"
			  << result << std::endl;

	// Export interface at level 0
	REQUIRE(result.find("export interface Iterator") != std::string::npos);
	// Method declarations at level 1
	REQUIRE(result.find("\tfunction hasNext(self Self) bool") != std::string::npos);
	REQUIRE(result.find("\tfunction next(self Self) int") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'Iterator'") != std::string::npos);
}

// ============================================
// Enum formatting tests
// ============================================

TEST_CASE("format_simple_enum", "[formatter]") {
	Formatter formatter;

	// Simple enum with cases at column 0
	std::string source =
		"enum Direction\n"
		"case north\n"
		"case south\n"
		"case east\n"
		"case west\n"
		"end 'Direction'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Simple enum:\n"
			  << result << std::endl;

	// Enum at level 0
	REQUIRE(result.find("enum Direction") != std::string::npos);
	// Cases at level 1
	REQUIRE(result.find("\tcase north") != std::string::npos);
	REQUIRE(result.find("\tcase south") != std::string::npos);
	REQUIRE(result.find("\tcase east") != std::string::npos);
	REQUIRE(result.find("\tcase west") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'Direction'") != std::string::npos);
	REQUIRE(result.find("\tend 'Direction'") == std::string::npos);
}

TEST_CASE("format_enum_with_raw_values", "[formatter]") {
	Formatter formatter;

	// Enum with raw values
	std::string source =
		"enum HttpStatus int\n"
		"case ok = 200\n"
		"case notFound = 404\n"
		"case serverError = 500\n"
		"end 'HttpStatus'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Enum with raw values:\n"
			  << result << std::endl;

	// Enum at level 0
	REQUIRE(result.find("enum HttpStatus int") != std::string::npos);
	// Cases at level 1
	REQUIRE(result.find("\tcase ok = 200") != std::string::npos);
	REQUIRE(result.find("\tcase notFound = 404") != std::string::npos);
	REQUIRE(result.find("\tcase serverError = 500") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'HttpStatus'") != std::string::npos);
}

TEST_CASE("format_enum_with_methods", "[formatter]") {
	Formatter formatter;

	// Enum with method
	std::string source =
		"enum Toggle\n"
		"case on\n"
		"case off\n"
		"\n"
		"function flip() Toggle\n"
		"if self == Toggle.on 'check'\n"
		"return Toggle.off\n"
		"end 'check'\n"
		"return Toggle.on\n"
		"end 'flip'\n"
		"end 'Toggle'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Enum with method:\n"
			  << result << std::endl;

	// Enum at level 0
	REQUIRE(result.find("enum Toggle") != std::string::npos);
	// Cases at level 1
	REQUIRE(result.find("\tcase on") != std::string::npos);
	REQUIRE(result.find("\tcase off") != std::string::npos);
	// Function at level 1
	REQUIRE(result.find("\tfunction flip() Toggle") != std::string::npos);
	// Function body at level 2
	REQUIRE(result.find("\t\tif self == Toggle.on 'check'") != std::string::npos);
	// If body at level 3
	REQUIRE(result.find("\t\t\treturn Toggle.off") != std::string::npos);
	// End check at level 2
	REQUIRE(result.find("\t\tend 'check'") != std::string::npos);
	// Return at level 2
	REQUIRE(result.find("\t\treturn Toggle.on") != std::string::npos);
	// End flip at level 1
	REQUIRE(result.find("\tend 'flip'") != std::string::npos);
	// End enum at level 0
	REQUIRE(result.find("end 'Toggle'") != std::string::npos);
	REQUIRE(result.find("\tend 'Toggle'") == std::string::npos);
}

TEST_CASE("format_exported_enum", "[formatter]") {
	Formatter formatter;

	// Exported enum
	std::string source =
		"export enum Color\n"
		"case red\n"
		"case green\n"
		"case blue\n"
		"end 'Color'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Exported enum:\n"
			  << result << std::endl;

	// Export enum at level 0
	REQUIRE(result.find("export enum Color") != std::string::npos);
	// Cases at level 1
	REQUIRE(result.find("\tcase red") != std::string::npos);
	REQUIRE(result.find("\tcase green") != std::string::npos);
	REQUIRE(result.find("\tcase blue") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'Color'") != std::string::npos);
}

TEST_CASE("format_enum_with_associated_values", "[formatter]") {
	Formatter formatter;

	// Enum with associated values
	std::string source =
		"enum Result\n"
		"case success(value int)\n"
		"case failure(code int, message string)\n"
		"case pending\n"
		"end 'Result'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	std::string result = edits[0].newText;
	std::cerr << "Enum with associated values:\n"
			  << result << std::endl;

	// Enum at level 0
	REQUIRE(result.find("enum Result") != std::string::npos);
	// Cases at level 1
	REQUIRE(result.find("\tcase success(value int)") != std::string::npos);
	REQUIRE(result.find("\tcase failure(code int, message string)") != std::string::npos);
	REQUIRE(result.find("\tcase pending") != std::string::npos);
	// End at level 0
	REQUIRE(result.find("end 'Result'") != std::string::npos);
}
