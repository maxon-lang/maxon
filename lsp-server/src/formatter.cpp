#include "formatter.h"
#include <algorithm>
#include <cctype>
#include <sstream>

std::vector<TextEdit> Formatter::formatDocument(
	const std::string &source,
	bool insertSpaces,
	int tabSize) {
	std::vector<TextEdit> edits;

	auto lines = splitLines(source);
	int currentIndentLevel = 0;
	int consecutiveBlankCount = 0;

	std::string formattedText;

	for (size_t i = 0; i < lines.size(); ++i) {
		const auto &originalLine = lines[i];

		// Check if this is a blank line (contains only whitespace)
		bool isBlankLine = originalLine.find_first_not_of(" \t") == std::string::npos;

		if (isBlankLine) {
			consecutiveBlankCount++;
			continue;
		}

		// Non-blank line found

		// Handle accumulated blank lines (keep at most one)
		if (consecutiveBlankCount > 0) {
			formattedText += "\n";
		}

		// Reset blank line counter
		consecutiveBlankCount = 0;

		// Determine indent level for this line
		std::string trimmedLine = originalLine;
		trimmedLine.erase(0, trimmedLine.find_first_not_of(" \t"));

		// Decrease indent for 'end' statements
		if (shouldDecreaseIndent(trimmedLine)) {
			currentIndentLevel = std::max(0, currentIndentLevel - 1);
		}

		// Normalize the line with correct indentation
		std::string indent = getIndent(currentIndentLevel, insertSpaces, tabSize);
		std::string normalizedLine = indent + trimmedLine;

		// Remove trailing whitespace
		size_t endPos = normalizedLine.find_last_not_of(" \t");
		if (endPos != std::string::npos) {
			normalizedLine = normalizedLine.substr(0, endPos + 1);
		} else {
			normalizedLine = "";
		}

		formattedText += normalizedLine + "\n";

		// Increase indent for statements that open blocks
		if (shouldIncreaseIndent(trimmedLine)) {
			currentIndentLevel++;
		}
	}

	// Create one edit replacing the whole document
	int lineCount = getLineCount(source);
	lsp::Position start{0, 0};
	lsp::Position end{lineCount + 1, 0};

	edits.push_back({{start, end}, formattedText});

	return edits;
}

std::vector<TextEdit> Formatter::formatRange(
	const std::string &source,
	const lsp::Range &range,
	bool insertSpaces,
	int tabSize) {
	// For now, implement range formatting as formatting the entire document
	// In a more sophisticated implementation, we could track indentation
	// context before the range and only format within the range
	return formatDocument(source, insertSpaces, tabSize);
}

std::string Formatter::getIndent(int indentLevel, bool insertSpaces, int tabSize) {
	if (insertSpaces) {
		return std::string(indentLevel * tabSize, ' ');
	} else {
		return std::string(indentLevel, '\t');
	}
}

int Formatter::calculateIndentLevel(const std::string &line) {
	int level = 0;
	for (char c : line) {
		if (c == '\t') {
			level++;
		} else if (c == ' ') {
			// Count 4 spaces as one indent level
			// This is approximate; a more robust implementation would track actual indents
		} else {
			break;
		}
	}
	return level;
}

bool Formatter::shouldIncreaseIndent(const std::string &line) {
	// Remove leading whitespace for checking
	std::string trimmed = line;
	trimmed.erase(0, trimmed.find_first_not_of(" \t"));

	// Check if line starts with block-opening keywords
	const char *openingKeywords[] = {"function", "if", "else", "while", "for"};
	for (const auto &keyword : openingKeywords) {
		if (trimmed.find(keyword) == 0) {
			// Make sure it's a complete keyword (followed by space or non-alphanumeric)
			size_t keywordLen = std::string(keyword).length();
			if (keywordLen < trimmed.length()) {
				char nextChar = trimmed[keywordLen];
				if (!std::isalnum(nextChar) && nextChar != '_') {
					return true;
				}
			} else if (keywordLen == trimmed.length()) {
				return true;
			}
		}
	}

	return false;
}

bool Formatter::shouldDecreaseIndent(const std::string &line) {
	// Remove leading whitespace for checking
	std::string trimmed = line;
	trimmed.erase(0, trimmed.find_first_not_of(" \t"));

	// Check if line starts with 'end'
	if (trimmed.find("end") == 0) {
		size_t keywordLen = 3;
		if (keywordLen < trimmed.length()) {
			char nextChar = trimmed[keywordLen];
			if (!std::isalnum(nextChar) && nextChar != '_') {
				return true;
			}
		} else if (keywordLen == trimmed.length()) {
			return true;
		}
	}

	return false;
}

std::string Formatter::normalizeLine(const std::string &line, bool insertSpaces, int tabSize) {
	// This is a simplified version; a more complete implementation would:
	// - Normalize spacing around operators
	// - Handle special cases like block identifiers
	// - Preserve string content

	std::string result = line;

	// For now, just trim trailing whitespace
	size_t endPos = result.find_last_not_of(" \t");
	if (endPos != std::string::npos) {
		result = result.substr(0, endPos + 1);
	}

	return result;
}

std::vector<std::string> Formatter::splitLines(const std::string &source) {
	std::vector<std::string> lines;
	std::stringstream ss(source);
	std::string line;

	while (std::getline(ss, line)) {
		// Remove carriage return if present (Windows line endings)
		// This normalizes to LF only
		if (!line.empty() && line.back() == '\r') {
			line.pop_back();
		}
		lines.push_back(line);
	}

	return lines;
}

std::string Formatter::normalizeEncoding(const std::string &source) {
	// For now, we assume the source is already valid UTF-8
	// A full implementation would validate and convert non-UTF-8 sequences
	// This is a placeholder that could be enhanced to handle various encodings
	return source;
}

std::string Formatter::joinLines(const std::vector<std::string> &lines) {
	std::string result;
	for (size_t i = 0; i < lines.size(); ++i) {
		result += lines[i];
		if (i < lines.size() - 1) {
			result += '\n';
		}
	}
	return result;
}

int Formatter::getLineCount(const std::string &source) {
	return std::count(source.begin(), source.end(), '\n') + 1;
}
