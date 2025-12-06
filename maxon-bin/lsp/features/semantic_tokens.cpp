#include "semantic_tokens.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include <algorithm>
#include <regex>
#include <stack>

namespace maxon_lsp {

SemanticTokensLegend SemanticTokensProvider::getLegend() {
	SemanticTokensLegend legend;

	// Token types - must match the indices expected by tests
	// Index 22 is 'label' for block identifiers
	legend.tokenTypes = {
		"namespace",	 // 0
		"type",			 // 1
		"class",		 // 2
		"enum",			 // 3
		"interface",	 // 4
		"struct",		 // 5
		"typeParameter", // 6
		"parameter",	 // 7
		"variable",		 // 8
		"property",		 // 9
		"enumMember",	 // 10
		"event",		 // 11
		"function",		 // 12
		"method",		 // 13
		"macro",		 // 14
		"keyword",		 // 15
		"modifier",		 // 16
		"comment",		 // 17
		"string",		 // 18
		"number",		 // 19
		"regexp",		 // 20
		"operator",		 // 21
		"label"			 // 22 - for block identifiers
	};

	// Token modifiers - include level modifiers for nesting colorization
	// Bits 10-15 are for nesting levels 0-5
	legend.tokenModifiers = {
		"declaration",	  // 0
		"definition",	  // 1
		"readonly",		  // 2
		"static",		  // 3
		"deprecated",	  // 4
		"abstract",		  // 5
		"async",		  // 6
		"modification",	  // 7
		"documentation",  // 8
		"defaultLibrary", // 9
		"level0",		  // 10 - nesting level 0 (function level)
		"level1",		  // 11 - nesting level 1
		"level2",		  // 12 - nesting level 2
		"level3",		  // 13 - nesting level 3
		"level4",		  // 14 - nesting level 4
		"level5"		  // 15 - nesting level 5
	};

	return legend;
}

std::optional<SemanticTokens> SemanticTokensProvider::getSemanticTokens(
	const Document &document,
	const AnalysisCache *cache) {
	(void)cache; // May use cache in future for more sophisticated analysis

	// Find all block identifiers in the document
	std::vector<BlockIdentifier> identifiers = findBlockIdentifiers(document);

	if (identifiers.empty()) {
		return std::nullopt;
	}

	// Encode tokens in LSP format
	SemanticTokens result;
	result.data = encodeTokens(identifiers);

	return result;
}

std::vector<SemanticTokensProvider::BlockIdentifier> SemanticTokensProvider::findBlockIdentifiers(
	const Document &document) {
	std::vector<BlockIdentifier> identifiers;
	int currentLevel = -1; // Start at -1, function increases to 0

	for (int lineNum = 0; lineNum < document.getLineCount(); ++lineNum) {
		std::string line = document.getLine(lineNum);
		parseLine(line, lineNum, currentLevel, identifiers);
	}

	return identifiers;
}

void SemanticTokensProvider::parseLine(
	const std::string &line,
	int lineNumber,
	int &currentLevel,
	std::vector<BlockIdentifier> &identifiers) {
	// Trim leading whitespace for keyword detection
	size_t firstNonSpace = line.find_first_not_of(" \t");
	if (firstNonSpace == std::string::npos) {
		return; // Empty or whitespace-only line
	}

	std::string trimmedLine = line.substr(firstNonSpace);

	// Extract first word to look up in keyword table
	size_t spacePos = trimmedLine.find(' ');
	std::string firstWord = (spacePos != std::string::npos)
								? trimmedLine.substr(0, spacePos)
								: trimmedLine;

	KeywordEntry entry;
	if (KeywordMatcher::match(firstWord.c_str(), firstWord.size(), entry)) {
		// Named block keywords (function, struct, enum, interface)
		// Use the identifier after the keyword as the block label
		if (entry.isNamedBlock) {
			size_t nameStart = firstWord.length() + 1; // After "keyword "
			size_t nameEnd = nameStart;
			while (nameEnd < trimmedLine.length() &&
				   (std::isalnum(trimmedLine[nameEnd]) || trimmedLine[nameEnd] == '_')) {
				++nameEnd;
			}

			if (nameEnd > nameStart) {
				currentLevel = 0; // Named blocks are at level 0

				// Only function names are block identifiers for semantic tokens
				// struct/enum/interface names are not highlighted as labels
				if (firstWord == "function") {
					BlockIdentifier id;
					id.line = lineNumber;
					id.startChar = static_cast<int>(firstNonSpace + nameStart);
					id.length = static_cast<int>(nameEnd - nameStart);
					id.nestingLevel = currentLevel;
					id.name = trimmedLine.substr(nameStart, nameEnd - nameStart);
					identifiers.push_back(id);
				}
			}
			return;
		}

		// Control flow block keywords (if, while, for, match)
		// Use the quoted 'label' at the end of the line
		if (entry.isBlockKeyword) {
			++currentLevel;

			// Look for block label at end of line: 'label'
			size_t quotePos = trimmedLine.rfind('\'');
			if (quotePos != std::string::npos && quotePos > 0) {
				size_t openQuote = trimmedLine.rfind('\'', quotePos - 1);
				if (openQuote != std::string::npos && openQuote < quotePos) {
					BlockIdentifier id;
					id.line = lineNumber;
					id.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
					id.length = static_cast<int>(quotePos - openQuote - 1);
					id.nestingLevel = currentLevel;
					id.name = trimmedLine.substr(openQuote + 1, quotePos - openQuote - 1);
					identifiers.push_back(id);
				}
			}
			return;
		}
	}

	// Special cases for keywords that have labels but isBlockKeyword=false in lexer

	// Check for else with label: "else 'label'"
	if (firstWord == "else") {
		// else doesn't change nesting level, but may have a label
		size_t quotePos = trimmedLine.rfind('\'');
		if (quotePos != std::string::npos && quotePos > 0) {
			size_t openQuote = trimmedLine.rfind('\'', quotePos - 1);
			if (openQuote != std::string::npos && openQuote < quotePos) {
				BlockIdentifier id;
				id.line = lineNumber;
				id.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				id.length = static_cast<int>(quotePos - openQuote - 1);
				id.nestingLevel = currentLevel;
				id.name = trimmedLine.substr(openQuote + 1, quotePos - openQuote - 1);
				identifiers.push_back(id);
			}
		}
		return;
	}

	// Check for continue/break with target label: "continue 'label'"
	if (firstWord == "continue" || firstWord == "break") {
		size_t quotePos = trimmedLine.rfind('\'');
		if (quotePos != std::string::npos && quotePos > 0) {
			size_t openQuote = trimmedLine.rfind('\'', quotePos - 1);
			if (openQuote != std::string::npos && openQuote < quotePos) {
				BlockIdentifier id;
				id.line = lineNumber;
				id.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				id.length = static_cast<int>(quotePos - openQuote - 1);
				id.nestingLevel = currentLevel;
				id.name = trimmedLine.substr(openQuote + 1, quotePos - openQuote - 1);
				identifiers.push_back(id);
			}
		}
		return;
	}

	// Check for end with label: "end 'label'"
	if (firstWord == "end") {
		size_t quotePos = trimmedLine.rfind('\'');
		if (quotePos != std::string::npos && quotePos > 0) {
			size_t openQuote = trimmedLine.rfind('\'', quotePos - 1);
			if (openQuote != std::string::npos && openQuote < quotePos) {
				BlockIdentifier id;
				id.line = lineNumber;
				id.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				id.length = static_cast<int>(quotePos - openQuote - 1);
				id.nestingLevel = currentLevel;
				id.name = trimmedLine.substr(openQuote + 1, quotePos - openQuote - 1);
				identifiers.push_back(id);
			}
		}

		// Decrease nesting level after processing end
		if (currentLevel >= 0) {
			--currentLevel;
		}
		return;
	}
}

std::vector<int> SemanticTokensProvider::encodeTokens(
	const std::vector<BlockIdentifier> &identifiers) {
	std::vector<int> data;
	data.reserve(identifiers.size() * 5);

	int prevLine = 0;
	int prevChar = 0;

	for (const auto &id : identifiers) {
		// Delta encoding
		int deltaLine = id.line - prevLine;
		int deltaChar = (deltaLine == 0) ? (id.startChar - prevChar) : id.startChar;

		// Token type is 'label' (index 22)
		int tokenType = LABEL_TOKEN_TYPE;

		// Token modifier is the level bit
		int tokenModifiers = getLevelModifier(id.nestingLevel);

		data.push_back(deltaLine);
		data.push_back(deltaChar);
		data.push_back(id.length);
		data.push_back(tokenType);
		data.push_back(tokenModifiers);

		prevLine = id.line;
		prevChar = id.startChar;
	}

	return data;
}

int SemanticTokensProvider::getLevelModifier(int level) {
	// Cycle through levels 0-5
	int cycledLevel = level % NUM_LEVELS;
	if (cycledLevel < 0) {
		cycledLevel = 0;
	}

	// Set the appropriate bit (level0 is bit 10, level1 is bit 11, etc.)
	return 1 << (LEVEL_MODIFIER_OFFSET + cycledLevel);
}

} // namespace maxon_lsp
