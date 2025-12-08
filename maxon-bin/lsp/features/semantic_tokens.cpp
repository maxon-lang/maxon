#include "semantic_tokens.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include <algorithm>
#include <set>
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
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {

	// Find all block identifiers in the document
	std::vector<SemanticToken> tokens = findBlockIdentifiers(document);

	// Find type references using cache and stdlib
	std::vector<SemanticToken> typeTokens = findTypeReferences(document, cache, stdlib);
	tokens.insert(tokens.end(), typeTokens.begin(), typeTokens.end());

	if (tokens.empty()) {
		return std::nullopt;
	}

	// Sort tokens by position for proper delta encoding
	std::sort(tokens.begin(), tokens.end(), [](const SemanticToken &a, const SemanticToken &b) {
		if (a.line != b.line)
			return a.line < b.line;
		return a.startChar < b.startChar;
	});

	// Encode tokens in LSP format
	SemanticTokens result;
	result.data = encodeTokens(tokens);

	return result;
}

std::vector<SemanticTokensProvider::SemanticToken> SemanticTokensProvider::findBlockIdentifiers(
	const Document &document) {
	std::vector<SemanticToken> tokens;
	int currentLevel = -1; // Start at -1, function increases to 0

	for (int lineNum = 0; lineNum < document.getLineCount(); ++lineNum) {
		std::string line = document.getLine(lineNum);
		parseLineForBlocks(line, lineNum, currentLevel, tokens);
	}

	return tokens;
}

void SemanticTokensProvider::parseLineForBlocks(
	const std::string &line,
	int lineNumber,
	int &currentLevel,
	std::vector<SemanticToken> &tokens) {
	// Trim leading whitespace for keyword detection
	size_t firstNonSpace = line.find_first_not_of(" \t");
	if (firstNonSpace == std::string::npos) {
		return; // Empty or whitespace-only line
	}

	std::string trimmedLine = line.substr(firstNonSpace);

	// Scan all words in the line to find keywords
	// This handles cases like "export struct string" where struct isn't first
	size_t pos = 0;
	while (pos < trimmedLine.size()) {
		// Skip non-identifier characters
		while (pos < trimmedLine.size() && !std::isalnum(trimmedLine[pos]) && trimmedLine[pos] != '_') {
			++pos;
		}
		if (pos >= trimmedLine.size())
			break;

		// Extract word
		size_t wordStart = pos;
		while (pos < trimmedLine.size() && (std::isalnum(trimmedLine[pos]) || trimmedLine[pos] == '_')) {
			++pos;
		}

		std::string word = trimmedLine.substr(wordStart, pos - wordStart);

		KeywordEntry entry;
		if (KeywordMatcher::match(word.c_str(), word.size(), entry)) {
			// Named block keywords (function, struct, enum, interface)
			// Use the identifier after the keyword as the block label
			if (entry.isNamedBlock) {
				// Skip whitespace after keyword
				size_t nameStart = pos;
				while (nameStart < trimmedLine.size() &&
					   (trimmedLine[nameStart] == ' ' || trimmedLine[nameStart] == '\t')) {
					++nameStart;
				}

				// Extract the name
				size_t nameEnd = nameStart;
				while (nameEnd < trimmedLine.size() &&
					   (std::isalnum(trimmedLine[nameEnd]) || trimmedLine[nameEnd] == '_')) {
					++nameEnd;
				}

				if (nameEnd > nameStart) {
					currentLevel = 0; // Named blocks are at level 0

					// All named block keywords get semantic token highlighting with level0 modifier
					SemanticToken token;
					token.line = lineNumber;
					token.startChar = static_cast<int>(firstNonSpace + nameStart);
					token.length = static_cast<int>(nameEnd - nameStart);
					token.tokenType = LABEL_TOKEN_TYPE;
					token.modifiers = getLevelModifier(currentLevel);
					tokens.push_back(token);
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
						SemanticToken token;
						token.line = lineNumber;
						token.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
						token.length = static_cast<int>(quotePos - openQuote - 1);
						token.tokenType = LABEL_TOKEN_TYPE;
						token.modifiers = getLevelModifier(currentLevel);
						tokens.push_back(token);
					}
				}
				return;
			}
		}
	}

	// Extract first word for special cases
	size_t spacePos = trimmedLine.find(' ');
	std::string firstWord = (spacePos != std::string::npos)
								? trimmedLine.substr(0, spacePos)
								: trimmedLine;

	// Special cases for keywords that have labels but isBlockKeyword=false in lexer

	// Check for else with label: "else 'label'"
	if (firstWord == "else") {
		// else doesn't change nesting level, but may have a label
		size_t quotePos = trimmedLine.rfind('\'');
		if (quotePos != std::string::npos && quotePos > 0) {
			size_t openQuote = trimmedLine.rfind('\'', quotePos - 1);
			if (openQuote != std::string::npos && openQuote < quotePos) {
				SemanticToken token;
				token.line = lineNumber;
				token.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				token.length = static_cast<int>(quotePos - openQuote - 1);
				token.tokenType = LABEL_TOKEN_TYPE;
				token.modifiers = getLevelModifier(currentLevel);
				tokens.push_back(token);
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
				SemanticToken token;
				token.line = lineNumber;
				token.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				token.length = static_cast<int>(quotePos - openQuote - 1);
				token.tokenType = LABEL_TOKEN_TYPE;
				token.modifiers = getLevelModifier(currentLevel);
				tokens.push_back(token);
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
				SemanticToken token;
				token.line = lineNumber;
				token.startChar = static_cast<int>(firstNonSpace + openQuote + 1);
				token.length = static_cast<int>(quotePos - openQuote - 1);
				token.tokenType = LABEL_TOKEN_TYPE;
				token.modifiers = getLevelModifier(currentLevel);
				tokens.push_back(token);
			}
		}

		// Decrease nesting level after processing end
		if (currentLevel >= 0) {
			--currentLevel;
		}
		return;
	}
}

std::vector<SemanticTokensProvider::SemanticToken> SemanticTokensProvider::findTypeReferences(
	const Document &document,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	std::vector<SemanticToken> tokens;

	// Build set of known type names from analysis cache and stdlib
	std::set<std::string> knownTypes;

	// Add struct names from cache
	if (cache) {
		for (const auto &[name, info] : cache->structs) {
			knownTypes.insert(name);
		}

		// Add interface names from cache
		for (const auto &[name, info] : cache->interfaces) {
			knownTypes.insert(name);
		}
	}

	// Add stdlib structs
	for (const auto &symbol : stdlib.structs) {
		knownTypes.insert(symbol.name);
	}

	// Add stdlib interfaces
	for (const auto &symbol : stdlib.interfaces) {
		knownTypes.insert(symbol.name);
	}

	// Scan each line for type references
	for (int lineNum = 0; lineNum < document.getLineCount(); ++lineNum) {
		std::string line = document.getLine(lineNum);
		findTypesInLine(line, lineNum, knownTypes, tokens);
	}

	return tokens;
}

void SemanticTokensProvider::findTypesInLine(
	const std::string &line,
	int lineNumber,
	const std::set<std::string> &knownTypes,
	std::vector<SemanticToken> &tokens) {

	// Find where comment starts (if any)
	size_t commentStart = line.find("//");

	// Skip entirely if line starts with comment
	size_t firstNonSpace = line.find_first_not_of(" \t");
	if (firstNonSpace != std::string::npos && line.substr(firstNonSpace, 2) == "//") {
		return;
	}

	// Track positions to skip (definition sites already handled by block identifiers)
	std::set<size_t> skipPositions;

	// Find definition sites: identifier immediately after a named block keyword
	size_t pos = 0;
	while (pos < line.size() && (commentStart == std::string::npos || pos < commentStart)) {
		// Skip non-identifier characters
		while (pos < line.size() && !std::isalnum(line[pos]) && line[pos] != '_') {
			++pos;
		}
		if (pos >= line.size() || (commentStart != std::string::npos && pos >= commentStart))
			break;

		// Extract identifier (potential keyword)
		size_t wordStart = pos;
		while (pos < line.size() && (std::isalnum(line[pos]) || line[pos] == '_')) {
			++pos;
		}

		std::string word = line.substr(wordStart, pos - wordStart);

		// Check if this word is a named block keyword
		KeywordEntry entry;
		if (KeywordMatcher::match(word.c_str(), word.size(), entry) && entry.isNamedBlock) {
			// Skip whitespace after keyword
			size_t nameStart = pos;
			while (nameStart < line.size() && (line[nameStart] == ' ' || line[nameStart] == '\t')) {
				++nameStart;
			}
			// Mark the start of the identifier as a position to skip
			if (nameStart < line.size() && (std::isalpha(line[nameStart]) || line[nameStart] == '_')) {
				skipPositions.insert(nameStart);
			}
		}
	}

	// Find all identifiers in the line and check if they're types
	pos = 0;
	while (pos < line.size() && (commentStart == std::string::npos || pos < commentStart)) {
		// Skip non-identifier characters
		while (pos < line.size() && !std::isalnum(line[pos]) && line[pos] != '_') {
			++pos;
		}

		if (pos >= line.size() || (commentStart != std::string::npos && pos >= commentStart))
			break;

		// Extract identifier
		size_t start = pos;
		while (pos < line.size() && (std::isalnum(line[pos]) || line[pos] == '_')) {
			++pos;
		}

		// Skip if this is a definition site (already handled as block label)
		if (skipPositions.count(start) > 0) {
			continue;
		}

		std::string identifier = line.substr(start, pos - start);

		// Check if it's a known type from analysis
		if (knownTypes.count(identifier) > 0) {
			SemanticToken token;
			token.line = lineNumber;
			token.startChar = static_cast<int>(start);
			token.length = static_cast<int>(identifier.length());
			token.tokenType = TYPE_TOKEN_TYPE;
			token.modifiers = 0;
			tokens.push_back(token);
			continue;
		}

		// Check if it's an internal type (starts with _ followed by capital letter)
		if (identifier.length() >= 2 && identifier[0] == '_' && std::isupper(identifier[1])) {
			SemanticToken token;
			token.line = lineNumber;
			token.startChar = static_cast<int>(start);
			token.length = static_cast<int>(identifier.length());
			token.tokenType = TYPE_TOKEN_TYPE;
			token.modifiers = 0;
			tokens.push_back(token);
		}
	}
}

std::vector<int> SemanticTokensProvider::encodeTokens(
	const std::vector<SemanticToken> &tokens) {
	std::vector<int> data;
	data.reserve(tokens.size() * 5);

	int prevLine = 0;
	int prevChar = 0;

	for (const auto &token : tokens) {
		// Delta encoding
		int deltaLine = token.line - prevLine;
		int deltaChar = (deltaLine == 0) ? (token.startChar - prevChar) : token.startChar;

		data.push_back(deltaLine);
		data.push_back(deltaChar);
		data.push_back(token.length);
		data.push_back(token.tokenType);
		data.push_back(token.modifiers);

		prevLine = token.line;
		prevChar = token.startChar;
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
