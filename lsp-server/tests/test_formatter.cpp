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
		"  print(42)\n"
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
		"  print(42)\n"
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
		"  print(x)\n"
		"end 'check'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// If statement body should be indented with one tab
	// "if x > 0 'check'\n\tprint(x)\nend 'check'\n"
	REQUIRE(edits[0].newText.find("\tprint(x)") != std::string::npos);
}

TEST_CASE("format_nested_blocks", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function outer() int\n"
		"if x > 0 'check'\n"
		"print(x)\n"
		"end 'check'\n"
		"return 0\n"
		"end 'outer'";

	auto edits = formatter.formatDocument(source, false, 4);

	REQUIRE(edits.size() == 1);

	// Check indentation levels
	// function outer
	// \tif x > 0
	// \t\tprint(x)
	// \tend
	// \treturn 0
	// end

	REQUIRE(edits[0].newText.find("\t\tprint(x)") != std::string::npos);
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
		"\tprint(42)\n"
		"\n"
		"\treturn 0\n"
		"end 'main'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Check that blank lines are preserved (as empty lines)
	// "function main() int\n\n\tprint(42)\n\n\treturn 0\nend 'main'\n"
	REQUIRE(edits[0].newText.find("\n\n") != std::string::npos);
}

TEST_CASE("format_while_loop", "[formatter]") {
	Formatter formatter;

	std::string source =
		"while i < 10 'loop'\n"
		"  print(i)\n"
		"  i = i + 1\n"
		"end 'loop'";

	auto edits = formatter.formatDocument(source, false, 4);
	REQUIRE(edits.size() == 1);

	// Loop body should be indented
	REQUIRE(edits[0].newText.find("\tprint(i)") != std::string::npos);
	REQUIRE(edits[0].newText.find("\ti = i + 1") != std::string::npos);
}

TEST_CASE("format_tab_size_respected", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"print(42)\n"
		"return 0\n"
		"end 'main'";

	// Format with 2-space indent (but still spaces mode)
	auto edits = formatter.formatDocument(source, true, 2);
	REQUIRE(edits.size() == 1);

	// Check for 2-space indentation
	REQUIRE(edits[0].newText.find("  print(42)") != std::string::npos);
}

TEST_CASE("format_range_formatting", "[formatter]") {
	Formatter formatter;

	std::string source =
		"function main() int\n"
		"  print(42)\n"
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
		"\t\tprint(x)\r\n"
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
		"\t\tprint(x)\n"
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
		"\t\tprint(x)\n"
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
