#include "completion.h"
#include "../../lexer/lexer_keyword_matcher.h"
#include "../../type_members.h"
#include <algorithm>
#include <cctype>
#include <set>

namespace maxon_lsp {

CompletionProvider::CompletionProvider() {}

CompletionList CompletionProvider::getCompletions(
	const Document &document,
	const Position &position,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	CompletionList result;
	result.isIncomplete = false;

	// Determine context and prefix
	CompletionContext context = determineContext(document, position);
	std::string prefix = getPrefix(document, position);

	std::vector<CompletionItem> items;

	switch (context) {
	case CompletionContext::AfterDot: {
		// Member access completion
		std::string typeName = getTypeBeforeDot(document, position, cache);
		if (!typeName.empty()) {
			auto memberItems = getMemberCompletions(typeName, prefix, cache, stdlib);
			items.insert(items.end(), memberItems.begin(), memberItems.end());
		}
		break;
	}

	case CompletionContext::AfterColon:
	case CompletionContext::InTypePosition: {
		// Type completions only
		auto typeItems = getTypeCompletions(prefix, cache, stdlib);
		items.insert(items.end(), typeItems.begin(), typeItems.end());
		break;
	}

	case CompletionContext::AfterEndQuote: {
		// Block label completions
		auto labelItems = getBlockLabelCompletions(document, position);
		items.insert(items.end(), labelItems.begin(), labelItems.end());
		break;
	}

	case CompletionContext::InsideFunction: {
		// Inside function: variables, functions, keywords, types
		auto varItems = getVariableCompletions(prefix, cache, position);
		items.insert(items.end(), varItems.begin(), varItems.end());

		auto funcItems = getFunctionCompletions(prefix, cache, stdlib);
		items.insert(items.end(), funcItems.begin(), funcItems.end());

		auto keywordItems = getKeywordCompletions(prefix);
		items.insert(items.end(), keywordItems.begin(), keywordItems.end());

		auto typeItems = getTypeCompletions(prefix, cache, stdlib);
		items.insert(items.end(), typeItems.begin(), typeItems.end());
		break;
	}

	case CompletionContext::TopLevel:
	default: {
		// Top level: declaration keywords, types, functions
		auto keywordItems = getKeywordCompletions(prefix);
		items.insert(items.end(), keywordItems.begin(), keywordItems.end());

		auto funcItems = getFunctionCompletions(prefix, cache, stdlib);
		items.insert(items.end(), funcItems.begin(), funcItems.end());

		auto typeItems = getTypeCompletions(prefix, cache, stdlib);
		items.insert(items.end(), typeItems.begin(), typeItems.end());
		break;
	}
	}

	// Filter by prefix if we have one
	if (!prefix.empty()) {
		items = filterByPrefix(items, prefix);
	}

	// Sort completions
	sortCompletions(items, prefix);

	result.items = std::move(items);
	return result;
}

CompletionContext CompletionProvider::determineContext(
	const Document &document,
	const Position &position) {
	if (position.line < 0 || position.line >= document.getLineCount()) {
		return CompletionContext::Unknown;
	}

	std::string line = document.getLine(position.line);
	int charPos = position.character;

	// Ensure we don't go out of bounds
	if (charPos > static_cast<int>(line.length())) {
		charPos = static_cast<int>(line.length());
	}

	// Look at characters before cursor
	std::string beforeCursor = line.substr(0, charPos);

	// Trim trailing whitespace for context analysis
	std::string trimmed = beforeCursor;
	while (!trimmed.empty() && std::isspace(trimmed.back())) {
		trimmed.pop_back();
	}

	// Check for "end '" pattern (block label completion)
	if (trimmed.length() >= 5) {
		size_t endPos = trimmed.rfind("end '");
		if (endPos != std::string::npos && endPos == trimmed.length() - 5) {
			return CompletionContext::AfterEndQuote;
		}
		// Also check if we're inside the block label after end '
		size_t quotePos = trimmed.rfind("end '");
		if (quotePos != std::string::npos) {
			// Check if there's no closing quote after end '
			size_t afterQuote = quotePos + 5;
			if (afterQuote <= trimmed.length()) {
				std::string afterEndQuote = trimmed.substr(afterQuote);
				if (afterEndQuote.find('\'') == std::string::npos) {
					return CompletionContext::AfterEndQuote;
				}
			}
		}
	}

	// Check for dot (member access)
	if (!trimmed.empty() && trimmed.back() == '.') {
		return CompletionContext::AfterDot;
	}

	// Check if we're typing after a dot with some characters
	size_t dotPos = trimmed.rfind('.');
	if (dotPos != std::string::npos) {
		// Check if there's an identifier after the dot
		std::string afterDot = trimmed.substr(dotPos + 1);
		bool allIdChars = true;
		for (char c : afterDot) {
			if (!std::isalnum(c) && c != '_') {
				allIdChars = false;
				break;
			}
		}
		if (allIdChars && !afterDot.empty()) {
			return CompletionContext::AfterDot;
		}
	}

	// Check for colon (type annotation)
	if (!trimmed.empty() && trimmed.back() == ':') {
		return CompletionContext::AfterColon;
	}

	// Check for patterns that indicate type position
	// "var name:" or "let name:" or parameter list
	if (trimmed.find(':') != std::string::npos) {
		size_t colonPos = trimmed.rfind(':');
		std::string afterColon = trimmed.substr(colonPos + 1);
		// Trim leading whitespace
		size_t start = afterColon.find_first_not_of(" \t");
		if (start != std::string::npos) {
			afterColon = afterColon.substr(start);
		}
		// If we're typing right after a colon with whitespace
		bool allIdChars = true;
		for (char c : afterColon) {
			if (!std::isalnum(c) && c != '_' && c != '[' && c != ']') {
				allIdChars = false;
				break;
			}
		}
		if (allIdChars) {
			return CompletionContext::InTypePosition;
		}
	}

	// Check if we're inside a function body by looking for function declaration
	// Simple heuristic: scan backwards through lines for "function" without matching "end"
	int functionDepth = 0;
	for (int i = position.line; i >= 0; --i) {
		std::string scanLine = document.getLine(i);

		// Look for "end 'function_name'" or just "end"
		size_t endPos = scanLine.find("end");
		if (endPos != std::string::npos) {
			// Simple check - if "end" appears, increase depth
			functionDepth++;
		}

		// Look for "function" keyword
		size_t funcPos = scanLine.find("function");
		if (funcPos != std::string::npos) {
			// Check it's actually a function declaration (not part of identifier)
			bool isKeyword = true;
			if (funcPos > 0 && (std::isalnum(scanLine[funcPos - 1]) || scanLine[funcPos - 1] == '_')) {
				isKeyword = false;
			}
			size_t afterFunc = funcPos + 8;
			if (afterFunc < scanLine.length() && (std::isalnum(scanLine[afterFunc]) || scanLine[afterFunc] == '_')) {
				isKeyword = false;
			}

			if (isKeyword) {
				if (functionDepth > 0) {
					functionDepth--;
				} else {
					return CompletionContext::InsideFunction;
				}
			}
		}
	}

	return CompletionContext::TopLevel;
}

std::string CompletionProvider::getPrefix(const Document &document, const Position &position) {
	if (position.line < 0 || position.line >= document.getLineCount()) {
		return "";
	}

	std::string line = document.getLine(position.line);
	int charPos = position.character;

	if (charPos > static_cast<int>(line.length())) {
		charPos = static_cast<int>(line.length());
	}

	// Scan backwards from cursor to find start of identifier
	int start = charPos;
	while (start > 0) {
		char c = line[start - 1];
		if (std::isalnum(c) || c == '_') {
			start--;
		} else {
			break;
		}
	}

	if (start >= charPos) {
		return "";
	}

	return line.substr(start, charPos - start);
}

std::string CompletionProvider::getTypeBeforeDot(
	const Document &document,
	const Position &position,
	const AnalysisCache *cache) {
	if (position.line < 0 || position.line >= document.getLineCount()) {
		return "";
	}

	std::string line = document.getLine(position.line);
	int charPos = position.character;

	if (charPos > static_cast<int>(line.length())) {
		charPos = static_cast<int>(line.length());
	}

	// Find the dot position
	int dotPos = charPos - 1;
	while (dotPos >= 0 && line[dotPos] != '.') {
		if (!std::isalnum(line[dotPos]) && line[dotPos] != '_') {
			break;
		}
		dotPos--;
	}

	if (dotPos < 0 || line[dotPos] != '.') {
		return "";
	}

	// Get the identifier before the dot
	int identEnd = dotPos;
	int identStart = identEnd - 1;
	while (identStart >= 0 && (std::isalnum(line[identStart]) || line[identStart] == '_')) {
		identStart--;
	}
	identStart++;

	if (identStart >= identEnd) {
		return "";
	}

	std::string identifier = line.substr(identStart, identEnd - identStart);

	// Look up the type of this identifier in the cache
	if (cache) {
		// Check variables
		auto varIt = cache->variables.find(identifier);
		if (varIt != cache->variables.end()) {
			return varIt->second.type;
		}
	}

	// If we can't find the type, return the identifier itself
	// (might be a type name for static access)
	return identifier;
}

std::vector<CompletionItem> CompletionProvider::getKeywordCompletions(const std::string &prefix) {
	std::vector<CompletionItem> items;

	// Get keywords from the keyword matcher
	auto keywords = KeywordMatcher::getLSPKeywordInfo();

	for (const auto &kw : keywords) {
		if (prefix.empty() || matchesPrefix(kw.name, prefix)) {
			items.push_back(buildKeywordItem(kw));
		}
	}

	return items;
}

std::vector<CompletionItem> CompletionProvider::getTypeCompletions(
	const std::string &prefix,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	std::vector<CompletionItem> items;

	// Built-in types from the keyword matcher (int, float, bool, byte)
	auto keywords = KeywordMatcher::getLSPKeywordInfo();
	for (const auto &kw : keywords) {
		if (kw.category == KeywordCategory::Type) {
			if (prefix.empty() || matchesPrefix(kw.name, prefix)) {
				CompletionItem item;
				item.label = kw.name;
				item.kind = CompletionItemKind::Keyword;
				item.detail = "type";
				item.documentation = kw.documentation;
				item.insertText = kw.name;
				items.push_back(std::move(item));
			}
		}
	}

	// Structs from cache
	if (cache) {
		for (const auto &[name, structInfo] : cache->structs) {
			if (prefix.empty() || matchesPrefix(name, prefix)) {
				items.push_back(buildStructItem(structInfo));
			}
		}
	}

	// Interfaces from cache
	if (cache) {
		for (const auto &[name, interfaceInfo] : cache->interfaces) {
			if (prefix.empty() || matchesPrefix(name, prefix)) {
				items.push_back(buildInterfaceItem(interfaceInfo));
			}
		}
	}

	// Stdlib types
	for (const auto &symbol : stdlib.structs) {
		if (prefix.empty() || matchesPrefix(symbol.name, prefix)) {
			items.push_back(buildStructItem(symbol));
		}
	}

	for (const auto &symbol : stdlib.enums) {
		if (prefix.empty() || matchesPrefix(symbol.name, prefix)) {
			items.push_back(buildEnumItem(symbol));
		}
	}

	for (const auto &symbol : stdlib.interfaces) {
		if (prefix.empty() || matchesPrefix(symbol.name, prefix)) {
			items.push_back(buildInterfaceItem(symbol));
		}
	}

	return items;
}

std::vector<CompletionItem> CompletionProvider::getVariableCompletions(
	const std::string &prefix,
	const AnalysisCache *cache,
	const Position &position) {
	std::vector<CompletionItem> items;

	if (!cache) {
		return items;
	}

	// Add all variables from the analysis cache
	// In a more sophisticated implementation, we would filter by scope
	for (const auto &[name, varInfo] : cache->variables) {
		if (prefix.empty() || matchesPrefix(name, prefix)) {
			items.push_back(buildVariableItem(name, varInfo.type, !varInfo.isImmutable));
		}
	}

	return items;
}

std::vector<CompletionItem> CompletionProvider::getFunctionCompletions(
	const std::string &prefix,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	std::vector<CompletionItem> items;

	// Functions from cache
	if (cache) {
		for (const auto &[name, funcInfo] : cache->functions) {
			if (prefix.empty() || matchesPrefix(name, prefix)) {
				items.push_back(buildFunctionItem(funcInfo));
			}
		}
	}

	// Stdlib functions
	for (const auto &symbol : stdlib.functions) {
		if (prefix.empty() || matchesPrefix(symbol.name, prefix)) {
			items.push_back(buildFunctionItem(symbol));
		}
	}

	return items;
}

std::vector<CompletionItem> CompletionProvider::getMemberCompletions(
	const std::string &typeName,
	const std::string &prefix,
	const AnalysisCache *cache,
	const StdlibSymbols &stdlib) {
	std::vector<CompletionItem> items;

	// Check if this is an array type (starts with '[')
	if (!typeName.empty() && typeName[0] == '[') {
		// Get built-in array members
		auto arrayMembers = getArrayTypeMembers();
		for (const auto &member : arrayMembers) {
			if (prefix.empty() || matchesPrefix(member.name, prefix)) {
				CompletionItem item;
				item.label = member.name;
				item.kind = member.isMethod ? CompletionItemKind::Method : CompletionItemKind::Property;
				item.detail = member.returnType;
				item.documentation = member.documentation;
				if (member.isMethod) {
					item.insertText = member.name + "()";
					item.insertTextFormat = InsertTextFormat::PlainText;
				}
				items.push_back(std::move(item));
			}
		}
	}

	// Look up the struct in cache for user-defined types
	if (cache) {
		auto structIt = cache->structs.find(typeName);
		if (structIt != cache->structs.end()) {
			const StructInfo &structInfo = structIt->second;

			// Add fields
			for (const auto &field : structInfo.fields) {
				if (prefix.empty() || matchesPrefix(field.name, prefix)) {
					items.push_back(buildFieldItem(field.name, field.type));
				}
			}
		}
	}

	// Look for methods in stdlib (methods are named "TypeName.methodName")
	std::string methodPrefix = typeName + ".";
	for (const auto &symbol : stdlib.functions) {
		if (symbol.kind == "method" && symbol.name.substr(0, methodPrefix.length()) == methodPrefix) {
			// Extract just the method name (after the dot)
			std::string methodName = symbol.name.substr(methodPrefix.length());

			// Skip internal methods (starting with underscore)
			if (!methodName.empty() && methodName[0] == '_') {
				continue;
			}

			// Skip interface method implementations (contain another dot like "Iterable.next")
			if (methodName.find('.') != std::string::npos) {
				continue;
			}

			if (prefix.empty() || matchesPrefix(methodName, prefix)) {
				CompletionItem item;
				item.label = methodName;
				item.kind = CompletionItemKind::Method;
				item.detail = symbol.type;
				item.documentation = symbol.documentation;

				// Build insert text with parameter placeholders
				std::string insertText = methodName + "(";
				int placeholderIndex = 1;
				bool first = true;
				for (const auto &param : symbol.parameters) {
					// Skip 'self' parameter
					if (param.name == "self") {
						continue;
					}
					if (!first) {
						insertText += ", ";
					}
					first = false;
					insertText += "${" + std::to_string(placeholderIndex++) + ":" + param.name + "}";
				}
				insertText += ")";

				item.insertText = insertText;
				item.insertTextFormat = InsertTextFormat::Snippet;
				items.push_back(std::move(item));
			}
		}
	}

	// Look for methods (functions with this type as receiver) in cache
	if (cache) {
		for (const auto &[name, funcInfo] : cache->functions) {
			// Check if this is a method for our type
			// Methods typically have the struct name as prefix or as first param
			if (!funcInfo.parameters.empty()) {
				const auto &firstParam = funcInfo.parameters[0];
				if (firstParam.name == "self" && firstParam.type == typeName) {
					if (prefix.empty() || matchesPrefix(name, prefix)) {
						std::string sig = buildFunctionSignature(funcInfo);
						items.push_back(buildMethodItem(name, sig));
					}
				}
			}
		}
	}

	return items;
}

std::vector<CompletionItem> CompletionProvider::getBlockLabelCompletions(
	const Document &document,
	const Position &position) {
	std::vector<CompletionItem> items;

	// Scan backwards to find enclosing blocks and their labels
	std::vector<std::string> labels;

	for (int i = position.line; i >= 0; --i) {
		std::string line = document.getLine(i);

		// Look for block-starting keywords
		static const std::vector<std::string> blockKeywords = KeywordMatcher::getAllBlockKeywords();
		static const std::vector<std::string> namedBlockKeywords = KeywordMatcher::getNamedBlockKeywords();

		for (const auto &keyword : blockKeywords) {
			size_t pos = line.find(keyword);
			if (pos != std::string::npos) {
				// Check it's actually a keyword (not part of identifier)
				bool isKeyword = true;
				if (pos > 0 && (std::isalnum(line[pos - 1]) || line[pos - 1] == '_')) {
					isKeyword = false;
				}
				size_t afterKw = pos + keyword.length();
				if (afterKw < line.length() && (std::isalnum(line[afterKw]) || line[afterKw] == '_')) {
					isKeyword = false;
				}

				if (isKeyword) {
					// Check if this is a named block keyword (uses identifier as label)
					bool isNamedBlock = std::find(namedBlockKeywords.begin(), namedBlockKeywords.end(), keyword) != namedBlockKeywords.end();

					if (isNamedBlock) {
						// Extract the name after the keyword
						size_t nameStart = afterKw;
						while (nameStart < line.length() && std::isspace(line[nameStart])) {
							nameStart++;
						}
						size_t nameEnd = nameStart;
						while (nameEnd < line.length() && (std::isalnum(line[nameEnd]) || line[nameEnd] == '_')) {
							nameEnd++;
						}
						if (nameEnd > nameStart) {
							labels.push_back(line.substr(nameStart, nameEnd - nameStart));
						}
					} else {
						// For control flow, use the keyword itself as label
						labels.push_back(keyword);
					}
				}
			}
		}
	}

	// Remove duplicates and create completion items
	std::set<std::string> seenLabels;
	for (const auto &label : labels) {
		if (seenLabels.insert(label).second) {
			items.push_back(buildBlockLabelItem(label));
		}
	}

	return items;
}

CompletionItem CompletionProvider::buildKeywordItem(const KeywordLSPInfo &keyword) {
	CompletionItem item;
	item.label = keyword.name;
	item.kind = convertKind(keyword.completionKind);
	item.detail = "keyword";
	item.documentation = keyword.documentation;

	// Use snippet format if insertText contains placeholders
	if (keyword.insertText.find('$') != std::string::npos) {
		item.insertText = keyword.insertText;
		item.insertTextFormat = InsertTextFormat::Snippet;
	} else {
		item.insertText = keyword.insertText;
		item.insertTextFormat = InsertTextFormat::PlainText;
	}

	// Add return type info for math intrinsics
	if (!keyword.returnType.empty()) {
		item.detail = "function -> " + keyword.returnType;
	}

	return item;
}

CompletionItem CompletionProvider::buildVariableItem(
	const std::string &name,
	const std::string &type,
	bool isMutable) {
	CompletionItem item;
	item.label = name;
	item.kind = isMutable ? CompletionItemKind::Variable : CompletionItemKind::Constant;
	item.detail = type;
	item.documentation = (isMutable ? "var " : "let ") + name + " " + type;
	item.insertText = name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildFunctionItem(const FunctionInfo &func) {
	CompletionItem item;
	item.label = func.name;
	item.kind = CompletionItemKind::Function;
	item.detail = buildFunctionSignature(func);
	item.insertText = buildFunctionInsertText(func);
	item.insertTextFormat = InsertTextFormat::Snippet;
	return item;
}

CompletionItem CompletionProvider::buildFunctionItem(const LSPSymbolInfo &symbol) {
	CompletionItem item;
	item.label = symbol.name;
	item.kind = CompletionItemKind::Function;
	item.detail = symbol.type;
	item.documentation = symbol.documentation;

	// Build insert text with placeholders from parameters
	std::string insertText = symbol.name + "(";
	for (size_t i = 0; i < symbol.parameters.size(); ++i) {
		if (i > 0)
			insertText += ", ";
		insertText += "${" + std::to_string(i + 1) + ":" + symbol.parameters[i].name + "}";
	}
	insertText += ")";

	item.insertText = insertText;
	item.insertTextFormat = InsertTextFormat::Snippet;
	return item;
}

CompletionItem CompletionProvider::buildStructItem(const StructInfo &structInfo) {
	CompletionItem item;
	item.label = structInfo.name;
	item.kind = CompletionItemKind::Struct;
	item.detail = "struct";

	// Build documentation from fields
	std::string doc = "struct " + structInfo.name + "\n";
	for (const auto &field : structInfo.fields) {
		doc += "  ";
		doc += (field.isImmutable ? "let " : "var ");
		doc += field.name + " " + field.type + "\n";
	}

	item.documentation = doc;
	item.insertText = structInfo.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildStructItem(const LSPSymbolInfo &symbol) {
	CompletionItem item;
	item.label = symbol.name;
	item.kind = CompletionItemKind::Struct;
	item.detail = "struct";
	item.documentation = symbol.documentation;
	item.insertText = symbol.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildEnumItem(const EnumInfo &enumInfo) {
	CompletionItem item;
	item.label = enumInfo.name;
	item.kind = CompletionItemKind::Enum;
	item.detail = "enum";

	// Build documentation from cases
	std::string doc = "enum " + enumInfo.name + "\n";
	for (const auto &c : enumInfo.cases) {
		doc += "  case " + c.name + "\n";
	}

	item.documentation = doc;
	item.insertText = enumInfo.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildEnumItem(const LSPSymbolInfo &symbol) {
	CompletionItem item;
	item.label = symbol.name;
	item.kind = CompletionItemKind::Enum;
	item.detail = "enum";
	item.documentation = symbol.documentation;
	item.insertText = symbol.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildInterfaceItem(const InterfaceInfo &interfaceInfo) {
	CompletionItem item;
	item.label = interfaceInfo.name;
	item.kind = CompletionItemKind::Interface;
	item.detail = "interface";

	// Build documentation from methods
	std::string doc = "interface " + interfaceInfo.name + "\n";
	for (const auto &method : interfaceInfo.methods) {
		doc += "  " + method.name + "(";
		for (size_t i = 0; i < method.parameters.size(); ++i) {
			if (i > 0)
				doc += ", ";
			doc += method.parameters[i].name + " " + method.parameters[i].type;
		}
		doc += ") " + method.returnType + "\n";
	}

	item.documentation = doc;
	item.insertText = interfaceInfo.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildInterfaceItem(const LSPSymbolInfo &symbol) {
	CompletionItem item;
	item.label = symbol.name;
	item.kind = CompletionItemKind::Interface;
	item.detail = "interface";
	item.documentation = symbol.documentation;
	item.insertText = symbol.name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildFieldItem(const std::string &name, const std::string &type) {
	CompletionItem item;
	item.label = name;
	item.kind = CompletionItemKind::Field;
	item.detail = type;
	item.documentation = "field " + name + " " + type;
	item.insertText = name;
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

CompletionItem CompletionProvider::buildMethodItem(const std::string &name, const std::string &signature) {
	CompletionItem item;
	item.label = name;
	item.kind = CompletionItemKind::Method;
	item.detail = signature;
	item.insertText = name + "($1)";
	item.insertTextFormat = InsertTextFormat::Snippet;
	return item;
}

CompletionItem CompletionProvider::buildBlockLabelItem(const std::string &label) {
	CompletionItem item;
	item.label = label;
	item.kind = CompletionItemKind::Text;
	item.detail = "block label";
	item.insertText = label + "'";
	item.insertTextFormat = InsertTextFormat::PlainText;
	return item;
}

std::vector<CompletionItem> CompletionProvider::filterByPrefix(
	std::vector<CompletionItem> &items,
	const std::string &prefix) {
	std::vector<CompletionItem> filtered;

	for (auto &item : items) {
		if (matchesPrefix(item.label, prefix)) {
			filtered.push_back(std::move(item));
		}
	}

	return filtered;
}

void CompletionProvider::sortCompletions(std::vector<CompletionItem> &items, const std::string &prefix) {
	std::string lowerPrefix = prefix;
	std::transform(lowerPrefix.begin(), lowerPrefix.end(), lowerPrefix.begin(), ::tolower);

	std::sort(items.begin(), items.end(), [&lowerPrefix](const CompletionItem &a, const CompletionItem &b) {
		std::string labelA = a.label;
		std::string labelB = b.label;
		std::transform(labelA.begin(), labelA.end(), labelA.begin(), ::tolower);
		std::transform(labelB.begin(), labelB.end(), labelB.begin(), ::tolower);

		// Exact match comes first
		bool exactA = (labelA == lowerPrefix);
		bool exactB = (labelB == lowerPrefix);
		if (exactA != exactB) {
			return exactA;
		}

		// Then prefix match (case-insensitive)
		bool prefixA = (labelA.substr(0, lowerPrefix.length()) == lowerPrefix);
		bool prefixB = (labelB.substr(0, lowerPrefix.length()) == lowerPrefix);
		if (prefixA != prefixB) {
			return prefixA;
		}

		// Then by kind (variables before functions before keywords)
		if (a.kind.has_value() && b.kind.has_value()) {
			// Prefer variables (6) and constants (21), then functions (3), then keywords (14)
			auto kindPriority = [](CompletionItemKind k) -> int {
				switch (k) {
				case CompletionItemKind::Variable:
					return 1;
				case CompletionItemKind::Constant:
					return 1;
				case CompletionItemKind::Field:
					return 2;
				case CompletionItemKind::Method:
					return 3;
				case CompletionItemKind::Function:
					return 4;
				case CompletionItemKind::Struct:
					return 5;
				case CompletionItemKind::Enum:
					return 5;
				case CompletionItemKind::Interface:
					return 5;
				case CompletionItemKind::Keyword:
					return 6;
				default:
					return 10;
				}
			};

			int priorityA = kindPriority(*a.kind);
			int priorityB = kindPriority(*b.kind);
			if (priorityA != priorityB) {
				return priorityA < priorityB;
			}
		}

		// Finally alphabetically
		return labelA < labelB;
	});
}

bool CompletionProvider::matchesPrefix(const std::string &text, const std::string &prefix) {
	if (prefix.empty()) {
		return true;
	}

	if (text.length() < prefix.length()) {
		return false;
	}

	// Case-insensitive prefix comparison
	for (size_t i = 0; i < prefix.length(); ++i) {
		if (std::tolower(text[i]) != std::tolower(prefix[i])) {
			return false;
		}
	}

	return true;
}

CompletionItemKind CompletionProvider::convertKind(KeywordCompletionKind kind) {
	// KeywordCompletionKind enum values match LSP CompletionItemKind
	return static_cast<CompletionItemKind>(static_cast<int>(kind));
}

std::string CompletionProvider::buildFunctionSignature(const FunctionInfo &func) {
	std::string sig = "function " + func.name + "(";

	for (size_t i = 0; i < func.parameters.size(); ++i) {
		if (i > 0)
			sig += ", ";
		sig += func.parameters[i].name + " " + func.parameters[i].type;
	}

	sig += ")";
	if (!func.returnType.empty() && func.returnType != "void") {
		sig += " " + func.returnType;
	}

	return sig;
}

std::string CompletionProvider::buildFunctionInsertText(const FunctionInfo &func) {
	std::string insertText = func.name + "(";

	int placeholderIndex = 1;
	bool first = true;
	for (size_t i = 0; i < func.parameters.size(); ++i) {
		// Skip 'self' parameter for method calls
		if (func.parameters[i].name == "self") {
			continue;
		}

		if (!first) {
			insertText += ", ";
		}
		first = false;

		insertText += "${" + std::to_string(placeholderIndex++) + ":" + func.parameters[i].name + "}";
	}

	insertText += ")";
	return insertText;
}

} // namespace maxon_lsp
