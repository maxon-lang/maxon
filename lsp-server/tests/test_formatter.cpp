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
