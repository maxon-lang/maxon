#include "document_manager.h"
#include "../semantic_analyzer.h"
#include <algorithm>
#include <cctype>
#include <sstream>

namespace maxon_lsp {

// ============================================================================
// URI Utility Functions
// ============================================================================

// Hex character to integer value
static int hexCharToInt(char c) {
	if (c >= '0' && c <= '9')
		return c - '0';
	if (c >= 'A' && c <= 'F')
		return 10 + (c - 'A');
	if (c >= 'a' && c <= 'f')
		return 10 + (c - 'a');
	return -1;
}

// Integer value to hex character
static char intToHexChar(int value) {
	if (value < 10)
		return static_cast<char>('0' + value);
	return static_cast<char>('A' + value - 10);
}

// Normalize a URI to a canonical form for consistent comparison
// This handles differences between VS Code's URI format and our pathToUri format:
// - VS Code: file:///c%3A/Users/... (lowercase drive, encoded colon)
// - pathToUri: file:///C:/Users/... (uppercase drive, unencoded colon)
std::string normalizeUri(const std::string &uri) {
	std::string result = uri;

	// First URL decode to normalize encoded characters
	std::string decoded;
	decoded.reserve(result.size());
	for (size_t i = 0; i < result.size(); ++i) {
		if (result[i] == '%' && i + 2 < result.size()) {
			int high = hexCharToInt(result[i + 1]);
			int low = hexCharToInt(result[i + 2]);
			if (high >= 0 && low >= 0) {
				decoded += static_cast<char>((high << 4) | low);
				i += 2;
				continue;
			}
		}
		decoded += result[i];
	}
	result = decoded;

#ifdef _WIN32
	// On Windows, normalize the entire path to lowercase for case-insensitive comparison
	// file:///C:/Users/... -> file:///c:/users/...
	const std::string prefix = "file:///";
	if (result.size() > prefix.size() && result.substr(0, prefix.size()) == prefix) {
		std::transform(result.begin() + prefix.size(), result.end(),
					   result.begin() + prefix.size(),
					   [](unsigned char c) { return std::tolower(c); });
	}
#endif

	return result;
}

std::string urlDecode(const std::string &str) {
	std::string result;
	result.reserve(str.size());

	for (size_t i = 0; i < str.size(); ++i) {
		if (str[i] == '%' && i + 2 < str.size()) {
			int high = hexCharToInt(str[i + 1]);
			int low = hexCharToInt(str[i + 2]);
			if (high >= 0 && low >= 0) {
				result += static_cast<char>((high << 4) | low);
				i += 2;
				continue;
			}
		}
		result += str[i];
	}

	return result;
}

std::string urlEncode(const std::string &str) {
	std::string result;
	result.reserve(str.size() * 3); // Worst case: every char encoded

	for (char c : str) {
		// Keep alphanumeric and a few safe characters unencoded
		if (std::isalnum(static_cast<unsigned char>(c)) ||
			c == '-' || c == '_' || c == '.' || c == '~' ||
			c == '/' || c == ':') {
			result += c;
		} else {
			result += '%';
			result += intToHexChar((static_cast<unsigned char>(c) >> 4) & 0x0F);
			result += intToHexChar(static_cast<unsigned char>(c) & 0x0F);
		}
	}

	return result;
}

std::string uriToPath(const std::string &uri) {
	// Handle file:// URIs
	std::string path = uri;

	// Remove file:// prefix
	const std::string filePrefix = "file://";
	if (path.substr(0, filePrefix.size()) == filePrefix) {
		path = path.substr(filePrefix.size());
	}

	// URL decode the path
	path = urlDecode(path);

	// Handle Windows paths: file:///C:/path or file:///c%3A/path
	// After URL decode, we might have /C:/path or /c:/path
#ifdef _WIN32
	// On Windows, remove leading slash if followed by drive letter
	if (path.size() >= 3 && path[0] == '/' &&
		std::isalpha(static_cast<unsigned char>(path[1])) &&
		path[2] == ':') {
		path = path.substr(1);
	}

	// Convert forward slashes to backslashes on Windows
	std::replace(path.begin(), path.end(), '/', '\\');
#endif

	return path;
}

std::string pathToUri(const std::string &path) {
	std::string result = path;

#ifdef _WIN32
	// Convert backslashes to forward slashes
	std::replace(result.begin(), result.end(), '\\', '/');

	// Add leading slash for Windows drive paths (C:/path -> /C:/path)
	if (result.size() >= 2 &&
		std::isalpha(static_cast<unsigned char>(result[0])) &&
		result[1] == ':') {
		result = "/" + result;
	}
#endif

	// URL encode special characters (but keep / and :)
	result = urlEncode(result);

	return "file://" + result;
}

// ============================================================================
// Document Implementation
// ============================================================================

void Document::updateLines() {
	lines.clear();

	if (content.empty()) {
		// Empty document has one empty line
		lines.push_back("");
		return;
	}

	size_t start = 0;
	size_t pos = 0;

	while (pos < content.size()) {
		if (content[pos] == '\n') {
			// Extract line without the newline character
			lines.push_back(content.substr(start, pos - start));
			start = pos + 1;
		}
		++pos;
	}

	// Add the last line (may be empty if content ends with newline)
	lines.push_back(content.substr(start));
}

lsp::Position Document::offsetToPosition(size_t offset) const {
	if (offset == 0) {
		return lsp::Position(0, 0);
	}

	// Clamp offset to content size
	if (offset > content.size()) {
		offset = content.size();
	}

	size_t currentOffset = 0;

	// Walk through lines to find the one containing the offset
	for (size_t i = 0; i < lines.size(); ++i) {
		size_t lineLength = lines[i].size();
		size_t lineEndOffset = currentOffset + lineLength;

		// Account for newline character (except for last line)
		bool hasNewline = (i + 1 < lines.size());
		if (hasNewline) {
			lineEndOffset += 1;
		}

		if (offset < lineEndOffset || (offset == lineEndOffset && !hasNewline)) {
			// Offset is within this line
			int character = static_cast<int>(offset - currentOffset);
			// Clamp character to line length
			if (character > static_cast<int>(lineLength)) {
				character = static_cast<int>(lineLength);
			}
			return lsp::Position(static_cast<int>(i), character);
		}

		currentOffset = lineEndOffset;
	}

	// Offset is at or past end of document
	if (lines.empty()) {
		return lsp::Position(0, 0);
	}

	int lastLine = static_cast<int>(lines.size()) - 1;
	return lsp::Position(lastLine, static_cast<int>(lines[lastLine].size()));
}

size_t Document::positionToOffset(const lsp::Position &pos) const {
	if (lines.empty()) {
		return 0;
	}

	// Clamp line to valid range
	int line = pos.line;
	if (line < 0)
		line = 0;
	if (line >= static_cast<int>(lines.size())) {
		line = static_cast<int>(lines.size()) - 1;
	}

	// Calculate offset to start of line
	size_t offset = 0;
	for (int i = 0; i < line; ++i) {
		offset += lines[i].size() + 1; // +1 for newline
	}

	// Add character offset within line, clamped to line length
	int character = pos.character;
	if (character < 0)
		character = 0;
	int lineLength = static_cast<int>(lines[line].size());
	if (character > lineLength) {
		character = lineLength;
	}

	return offset + static_cast<size_t>(character);
}

std::string Document::getLine(int lineIndex) const {
	if (lineIndex < 0 || lineIndex >= static_cast<int>(lines.size())) {
		return "";
	}
	return lines[lineIndex];
}

// ============================================================================
// DocumentManager Implementation
// ============================================================================

void DocumentManager::openDocument(const std::string &uri, const std::string &languageId,
								   int version, const std::string &content) {
	std::string normalizedUri = normalizeUri(uri);
	documents_.emplace(normalizedUri, Document(normalizedUri, languageId, version, content));
	// Invalidate any existing analysis cache
	analysisCache_.erase(normalizedUri);
}

void DocumentManager::closeDocument(const std::string &uri) {
	std::string normalizedUri = normalizeUri(uri);
	documents_.erase(normalizedUri);
	analysisCache_.erase(normalizedUri);
	lastGoodCache_.erase(normalizedUri);
}

void DocumentManager::updateDocument(const std::string &uri, int version,
									 const std::vector<lsp::TextDocumentContentChangeEvent> &changes) {
	std::string normalizedUri = normalizeUri(uri);
	auto it = documents_.find(normalizedUri);
	if (it == documents_.end()) {
		return;
	}

	Document &doc = it->second;

	// Sort changes by position descending (from end to beginning)
	// This allows us to apply changes without invalidating earlier positions
	std::vector<const lsp::TextDocumentContentChangeEvent *> sortedChanges;
	sortedChanges.reserve(changes.size());
	for (const auto &change : changes) {
		sortedChanges.push_back(&change);
	}

	std::sort(sortedChanges.begin(), sortedChanges.end(),
			  [](const lsp::TextDocumentContentChangeEvent *a,
				 const lsp::TextDocumentContentChangeEvent *b) {
				  // Full document changes (no range) should be processed last
				  if (!a->range.has_value())
					  return false;
				  if (!b->range.has_value())
					  return true;

				  // Sort by position descending
				  const lsp::Range &ra = a->range.value();
				  const lsp::Range &rb = b->range.value();

				  if (ra.start.line != rb.start.line) {
					  return ra.start.line > rb.start.line;
				  }
				  return ra.start.character > rb.start.character;
			  });

	// Apply changes in sorted order
	for (const auto *change : sortedChanges) {
		applyChange(doc, *change);
	}

	doc.version = version;

	// Invalidate analysis cache since document changed
	invalidateAnalysis(uri);
}

void DocumentManager::replaceDocument(const std::string &uri, int version,
									  const std::string &content) {
	auto it = documents_.find(uri);
	if (it == documents_.end()) {
		return;
	}

	Document &doc = it->second;
	doc.content = content;
	doc.version = version;
	doc.updateLines();

	// Invalidate analysis cache since document changed
	invalidateAnalysis(uri);
}

void DocumentManager::applyChange(Document &doc, const lsp::TextDocumentContentChangeEvent &change) {
	if (!change.range.has_value()) {
		// Full content replacement
		doc.content = change.text;
		doc.updateLines();
		return;
	}

	// Range-based change
	const lsp::Range &range = change.range.value();

	// Calculate byte offsets for the range
	size_t startOffset = doc.positionToOffset(range.start);
	size_t endOffset = doc.positionToOffset(range.end);

	// Validate offsets
	if (startOffset > doc.content.size()) {
		startOffset = doc.content.size();
	}
	if (endOffset > doc.content.size()) {
		endOffset = doc.content.size();
	}
	if (startOffset > endOffset) {
		// Invalid range, treat as insertion at start
		endOffset = startOffset;
	}

	// Apply the change: replace text from startOffset to endOffset
	std::string newContent;
	newContent.reserve(startOffset + change.text.size() + (doc.content.size() - endOffset));
	newContent = doc.content.substr(0, startOffset);
	newContent += change.text;
	newContent += doc.content.substr(endOffset);

	doc.content = std::move(newContent);
	doc.updateLines();
}

std::optional<Document *> DocumentManager::getDocument(const std::string &uri) {
	std::string normalizedUri = normalizeUri(uri);
	auto it = documents_.find(normalizedUri);
	if (it == documents_.end()) {
		return std::nullopt;
	}
	return &it->second;
}

bool DocumentManager::hasDocument(const std::string &uri) const {
	std::string normalizedUri = normalizeUri(uri);
	return documents_.find(normalizedUri) != documents_.end();
}

std::vector<std::string> DocumentManager::getOpenDocumentUris() const {
	std::vector<std::string> uris;
	uris.reserve(documents_.size());
	for (const auto &pair : documents_) {
		uris.push_back(pair.first);
	}
	return uris;
}

AnalysisCache *DocumentManager::getAnalysis(const std::string &uri) {
	std::string normalizedUri = normalizeUri(uri);
	auto it = analysisCache_.find(normalizedUri);
	if (it == analysisCache_.end()) {
		return nullptr;
	}
	return &it->second;
}

AnalysisCache *DocumentManager::getLastGoodAnalysis(const std::string &uri) {
	std::string normalizedUri = normalizeUri(uri);
	auto it = lastGoodCache_.find(normalizedUri);
	if (it == lastGoodCache_.end()) {
		return nullptr;
	}
	return &it->second;
}

void DocumentManager::invalidateAnalysis(const std::string &uri) {
	std::string normalizedUri = normalizeUri(uri);
	analysisCache_.erase(normalizedUri);
}

void DocumentManager::setAnalysis(const std::string &uri, AnalysisCache cache) {
	std::string normalizedUri = normalizeUri(uri);
	// If analysis was successful (no parse errors), update last good cache
	if (!cache.hasParseErrors()) {
		// Move a copy to last good cache
		AnalysisCache lastGood;
		lastGood.version = cache.version;
		lastGood.analysisTimeMs = cache.analysisTimeMs;
		lastGood.parseErrors = cache.parseErrors;
		lastGood.semanticErrors = cache.semanticErrors;
		lastGood.symbols = cache.symbols;
		lastGood.variables = cache.variables;
		lastGood.functions = cache.functions;
		lastGood.structs = cache.structs;
		lastGood.interfaces = cache.interfaces;
		lastGood.enums = cache.enums;
		// Note: we don't copy the AST to avoid double ownership issues

		lastGoodCache_[normalizedUri] = std::move(lastGood);
	}

	analysisCache_[normalizedUri] = std::move(cache);
}

// ============================================================================
// AnalysisCache Variable Lookup Helpers
// ============================================================================

std::string AnalysisCache::findEnclosingFunction(int line) const {
	if (!ast) {
		return "";
	}

	// Check regular functions
	for (const auto &func : ast->functions) {
		if (line >= func->line - 1 && line <= func->endLine - 1) {
			return SemanticAnalyzer::getFunctionKeyStatic(func.get());
		}
	}

	// Check struct methods
	for (const auto &structDef : ast->structs) {
		for (const auto &method : structDef->methods) {
			if (line >= method->line - 1 && line <= method->endLine - 1) {
				return SemanticAnalyzer::getMethodKey(structDef->name, method->name);
			}
		}
	}

	// Check enum methods
	for (const auto &enumDef : ast->enums) {
		for (const auto &method : enumDef->methods) {
			if (line >= method->line - 1 && line <= method->endLine - 1) {
				return SemanticAnalyzer::getMethodKey(enumDef->name, method->name);
			}
		}
	}

	return "";
}

const VariableInfo *AnalysisCache::findVariable(const std::string &name, int line) const {
	// If we have position info, try qualified lookup first
	if (line >= 0) {
		std::string enclosingFunc = findEnclosingFunction(line);
		if (!enclosingFunc.empty()) {
			std::string qualifiedKey = enclosingFunc + "::" + name;
			auto it = variables.find(qualifiedKey);
			if (it != variables.end()) {
				return &it->second;
			}
		}
	}

	// Fall back: search for any variable with the given name
	// This handles cases where we don't have position info
	// Look for exact match first (global variables stored without qualification)
	auto it = variables.find(name);
	if (it != variables.end()) {
		return &it->second;
	}

	// Look for any qualified key ending with ::name
	std::string suffix = "::" + name;
	for (const auto &[key, var] : variables) {
		if (key.size() >= suffix.size() &&
			key.compare(key.size() - suffix.size(), suffix.size(), suffix) == 0) {
			return &var;
		}
	}

	return nullptr;
}

} // namespace maxon_lsp
