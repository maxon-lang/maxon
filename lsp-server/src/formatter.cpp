#include "formatter.h"
#include <algorithm>
#include <cctype>
#include <sstream>

std::vector<TextEdit> Formatter::formatDocument(
	const std::string &source,
	bool insertSpaces,
	int tabSize) {
	std::vector<TextEdit> edits;

	int lineCount = getLineCount(source);

	auto lines = splitLines(source);
	int currentIndentLevel = 0;
	int braceDepth = 0; // Track struct literal brace depth separately
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

		// Check for closing brace (struct literal end)
		bool startsWithCloseBrace = !trimmedLine.empty() && trimmedLine[0] == '}';
		if (startsWithCloseBrace && braceDepth > 0) {
			braceDepth--;
			currentIndentLevel = std::max(0, currentIndentLevel - 1);
		}

		// Decrease indent for 'end' statements (control flow)
		if (shouldDecreaseIndentForEnd(trimmedLine)) {
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

		// Check for opening brace at end of line (struct literal start)
		size_t lastNonWhitespace = trimmedLine.find_last_not_of(" \t");
		bool endsWithOpenBrace = lastNonWhitespace != std::string::npos && trimmedLine[lastNonWhitespace] == '{';

		if (endsWithOpenBrace) {
			braceDepth++;
			currentIndentLevel++;
		} else if (shouldIncreaseIndentForKeyword(trimmedLine)) {
			// Increase indent for control flow keywords (function, if, while, etc.)
			currentIndentLevel++;
		}
	}

	// Create one edit replacing the whole document
	// LSP positions are 0-indexed. For a document with N lines (0 to N-1),
	// we use line N-1 (the last line) with a large character to capture everything.
	// Or we can use line N with character 0, but that may cause issues with some clients.
	// The safest approach is to use the actual last line with character set beyond its length.
	lsp::Position start{0, 0};
	// Use lineCount - 1 as the last line (0-indexed), and a large character to capture the whole line
	lsp::Position end{lineCount > 0 ? lineCount - 1 : 0, 10000};

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

bool Formatter::shouldIncreaseIndentForKeyword(const std::string &line) {
	// Remove leading whitespace for checking
	std::string trimmed = line;
	trimmed.erase(0, trimmed.find_first_not_of(" \t"));

	// Check if line starts with block-opening keywords (or export + keyword)
	const char *openingKeywords[] = {"function", "if", "else", "while", "for", "struct", "export function", "export struct"};
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

	// Note: Opening braces for struct literals are handled separately in formatDocument
	return false;
}

bool Formatter::shouldDecreaseIndentForEnd(const std::string &line) {
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

	// Note: Closing braces for struct literals are handled separately in formatDocument
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
