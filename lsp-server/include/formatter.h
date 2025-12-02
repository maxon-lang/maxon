#ifndef FORMATTER_H
#define FORMATTER_H

#include "lsp_types.h"
#include <string>
#include <vector>

// Text edit representing a change to the document
struct TextEdit {
	lsp::Range range;
	std::string newText;
};

// Formatter for Maxon language code
class Formatter {
  public:
	Formatter() = default;

	// Format entire document
	// insertSpaces: true for spaces, false for tabs
	// tabSize: number of spaces per tab (used only if insertSpaces is true)
	std::vector<TextEdit> formatDocument(
		const std::string &source,
		bool insertSpaces = false,
		int tabSize = 4);

	// Format a specific range in the document
	std::vector<TextEdit> formatRange(
		const std::string &source,
		const lsp::Range &range,
		bool insertSpaces = false,
		int tabSize = 4);

  private:
	// Helper methods
	std::string getIndent(int indentLevel, bool insertSpaces, int tabSize);
	int calculateIndentLevel(const std::string &line);
	bool shouldIncreaseIndentForKeyword(const std::string &line, bool insideInterface = false);
	bool shouldDecreaseIndentForEnd(const std::string &line);
	bool shouldDecreaseIndentForElse(const std::string &line);
	bool endsWithElseBlockId(const std::string &line);
	std::string normalizeLine(const std::string &line, bool insertSpaces, int tabSize);
	std::string normalizeEncoding(const std::string &source);
	std::vector<std::string> splitLines(const std::string &source);
	std::string joinLines(const std::vector<std::string> &lines);
	int getLineCount(const std::string &source);
};

#endif // FORMATTER_H
