#include "error_formatter.h"
#include <sstream>

std::vector<std::string> ErrorFormatter::splitLines(const std::string &sourceCode) {
	std::vector<std::string> lines;
	std::istringstream stream(sourceCode);
	std::string line;

	while (std::getline(stream, line)) {
		lines.push_back(line);
	}

	return lines;
}

std::string ErrorFormatter::getSourceLine(const std::string &sourceCode, int lineNumber) {
	auto lines = splitLines(sourceCode);
	if (lineNumber < 1 || lineNumber > static_cast<int>(lines.size())) {
		return "";
	}
	return lines[lineNumber - 1];
}

std::string ErrorFormatter::createCaretLine(int column, int length) {
	if (column < 1)
		column = 1;
	if (length < 1)
		length = 1;

	std::string caretLine(column - 1, ' ');
	caretLine += '^';

	// Add additional tildes for multi-character errors
	for (int i = 1; i < length; i++) {
		caretLine += '~';
	}

	return caretLine;
}

std::string ErrorFormatter::formatError(
	const std::string &errorMessage,
	const std::string &sourceCode,
	int line,
	int column,
	const std::string &errorType,
	const std::string &filePath) {
	std::ostringstream formatted;

	// Error header with file and location
	formatted << errorType << ": ";
	if (!filePath.empty()) {
		formatted << filePath;
		if (line > 0) {
			formatted << ":" << line;
			if (column > 0) {
				formatted << ":" << column;
			}
		}
		formatted << "\n";
	} else if (line > 0) {
		formatted << "line " << line;
		if (column > 0) {
			formatted << ", column " << column;
		}
		formatted << "\n";
	}

	// Error message
	formatted << errorMessage << "\n";

	// Show source context if available
	if (line > 0 && !sourceCode.empty()) {
		std::string sourceLine = getSourceLine(sourceCode, line);
		if (!sourceLine.empty()) {
			formatted << "\n";

			// Show line number with padding
			formatted << "  " << line << " | " << sourceLine << "\n";

			// Show caret pointing to error
			if (column > 0) {
				std::string lineNumberPadding(std::to_string(line).length() + 3, ' ');
				formatted << lineNumberPadding << "| " << createCaretLine(column) << "\n";
			}
		}
	}

	return formatted.str();
}
