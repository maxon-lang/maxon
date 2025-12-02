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
	bool insideInterface = false; // Track if we're inside an interface block

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
			// Check if we're ending an interface block
			if (insideInterface) {
				insideInterface = false;
			}
		}

		// Decrease indent for 'else' statements (should be at same level as 'if')
		if (shouldDecreaseIndentForElse(trimmedLine)) {
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

		// Check for else-unwrap syntax: var x = expr else 'blockid'
		// This pattern increases indent for the else block body
		bool isElseUnwrap = false;
		if (trimmedLine.find(" else '") != std::string::npos) {
			// Verify this is an else-unwrap (not a standalone else statement)
			// It should have content before the 'else' keyword
			size_t elsePos = trimmedLine.find(" else '");
			if (elsePos > 0) {
				isElseUnwrap = true;
			}
		}

		// Check for opening brace at end of line (struct literal start)
		size_t lastNonWhitespace = trimmedLine.find_last_not_of(" \t");
		bool endsWithOpenBrace = lastNonWhitespace != std::string::npos && trimmedLine[lastNonWhitespace] == '{';

		if (endsWithOpenBrace) {
			braceDepth++;
			currentIndentLevel++;
		} else if (isElseUnwrap) {
			// Increase indent for else-unwrap block body
			currentIndentLevel++;
		} else if (shouldIncreaseIndentForKeyword(trimmedLine, insideInterface)) {
			// Increase indent for control flow keywords (function, if, while, etc.)
			currentIndentLevel++;
			// Check if we're entering an interface block
			if (trimmedLine.find("interface ") == 0 || trimmedLine.find("export interface ") == 0) {
				insideInterface = true;
			}
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

bool Formatter::shouldIncreaseIndentForKeyword(const std::string &line, bool insideInterface) {
	// Remove leading whitespace for checking
	std::string trimmed = line;
	trimmed.erase(0, trimmed.find_first_not_of(" \t"));

	// Inside an interface, function declarations don't increase indent
	// (they're just signatures, not block-opening definitions)
	if (insideInterface && trimmed.find("function ") == 0) {
		return false;
	}

	// Check for single-line if with 'then' but NO block identifier
	// e.g., "if x > 5 then return 1" is complete on one line (no else/end needed)
	// But "if x > 5 'check' then return 1" has a block identifier, so else/end may follow
	if (trimmed.find("if ") == 0 && trimmed.find(" then ") != std::string::npos) {
		// Check if there's a block identifier (single-quoted string before 'then')
		size_t thenPos = trimmed.find(" then ");
		bool hasBlockId = false;
		for (size_t i = 3; i < thenPos; i++) {
			if (trimmed[i] == '\'') {
				hasBlockId = true;
				break;
			}
		}
		// If no block identifier, check for pattern: if ... then ... else 'id' at end
		// This means multi-line else follows
		if (!hasBlockId) {
			// Check if line ends with else 'identifier'
			// Pattern: "if ... then ... else 'id'"
			if (endsWithElseBlockId(trimmed)) {
				return true; // Multi-line else block follows
			}
			return false;
		}
		// Has block identifier with then - the if body is on this line, so don't increase indent
		// But else/end may follow, which will be handled by decrease/increase for else
		return false;
	}

	// Check for single-line else without block identifier - does NOT increase indent
	// e.g., "else return 0" is complete on one line
	if (trimmed.find("else ") == 0) {
		// Check if there's a block identifier (starts with ')
		size_t afterElse = 5; // length of "else "
		while (afterElse < trimmed.length() && std::isspace(trimmed[afterElse])) {
			afterElse++;
		}
		// If what follows is NOT a block identifier ('), this is single-line else
		if (afterElse < trimmed.length() && trimmed[afterElse] != '\'') {
			return false;
		}
		// Has block identifier - check if there's a statement after it
		// e.g., "else 'check' return 0" is single-line else with block id
		if (afterElse < trimmed.length() && trimmed[afterElse] == '\'') {
			// Find end of block identifier
			size_t blockIdStart = afterElse;
			size_t blockIdEnd = trimmed.find('\'', blockIdStart + 1);
			if (blockIdEnd != std::string::npos) {
				// Check if there's anything after the block identifier
				size_t afterBlockId = blockIdEnd + 1;
				while (afterBlockId < trimmed.length() && std::isspace(trimmed[afterBlockId])) {
					afterBlockId++;
				}
				// If there's more content after the block id, this is single-line else
				if (afterBlockId < trimmed.length()) {
					return false;
				}
			}
		}
	}

	// Check for if-let syntax: "if let varname = expr 'blockid'"
	// This should always increase indent (it's a multi-line construct)
	if (trimmed.find("if let ") == 0) {
		return true;
	}

	// Check if line starts with block-opening keywords (or export + keyword)
	const char *openingKeywords[] = {"function", "if", "else", "while", "for", "struct", "interface", "export function", "export struct", "export interface"};
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

bool Formatter::shouldDecreaseIndentForElse(const std::string &line) {
	// Remove leading whitespace for checking
	std::string trimmed = line;
	trimmed.erase(0, trimmed.find_first_not_of(" \t"));

	// Check if line starts with 'else'
	if (trimmed.find("else") == 0) {
		size_t keywordLen = 4;
		if (keywordLen < trimmed.length()) {
			char nextChar = trimmed[keywordLen];
			if (!std::isalnum(nextChar) && nextChar != '_') {
				// Check if this is a multi-line else (has block identifier)
				// Skip whitespace after 'else'
				size_t pos = keywordLen;
				while (pos < trimmed.length() && std::isspace(trimmed[pos])) {
					pos++;
				}
				// If what follows is a block identifier ('), this is multi-line else
				if (pos < trimmed.length() && trimmed[pos] == '\'') {
					return true;
				}
				// Single-line else (e.g., "else return 0") - don't decrease indent
				return false;
			}
		} else if (keywordLen == trimmed.length()) {
			// Just "else" alone - treat as multi-line
			return true;
		}
	}

	return false;
}

bool Formatter::endsWithElseBlockId(const std::string &line) {
	// Check if line ends with pattern: else 'identifier'
	// This indicates a single-line if with multi-line else

	// Look for " else '" near the end of the line
	size_t elsePos = line.rfind(" else '");
	if (elsePos == std::string::npos) {
		return false;
	}

	// Find the closing quote
	size_t openQuotePos = elsePos + 7; // position after " else '"
	size_t closeQuotePos = line.find('\'', openQuotePos);
	if (closeQuotePos == std::string::npos) {
		return false;
	}

	// Check if there's anything meaningful after the closing quote (just whitespace is ok)
	for (size_t i = closeQuotePos + 1; i < line.length(); i++) {
		if (!std::isspace(line[i])) {
			return false; // There's something after the block id
		}
	}

	return true;
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
