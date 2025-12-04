#include "incremental_parser.h"
#include <algorithm>
#include <sstream>
#include <stdexcept>

std::vector<AffectedDeclaration> IncrementalParser::findAffectedDeclarations(
	const ProgramAST *ast, const EditRegion &edit) const {

	std::vector<AffectedDeclaration> affected;
	if (!ast) {
		return affected;
	}

	SourceRange editRange = edit.toSourceRange();

	// Check functions
	for (size_t i = 0; i < ast->functions.size(); ++i) {
		const auto &func = ast->functions[i];
		SourceRange funcRange = func->getSourceRange();

		if (funcRange.overlaps(editRange)) {
			AffectedDeclaration decl;
			decl.kind = AffectedDeclaration::Kind::Function;
			decl.name = func->name;
			decl.index = i;
			decl.range = funcRange;
			affected.push_back(decl);
		}
	}

	// Check structs
	for (size_t i = 0; i < ast->structs.size(); ++i) {
		const auto &structDef = ast->structs[i];
		SourceRange structRange = structDef->getSourceRange();

		if (structRange.overlaps(editRange)) {
			AffectedDeclaration decl;
			decl.kind = AffectedDeclaration::Kind::Struct;
			decl.name = structDef->name;
			decl.index = i;
			decl.range = structRange;
			affected.push_back(decl);
		}
	}

	// Check enums
	for (size_t i = 0; i < ast->enums.size(); ++i) {
		const auto &enumDef = ast->enums[i];
		SourceRange enumRange = enumDef->getSourceRange();

		if (enumRange.overlaps(editRange)) {
			AffectedDeclaration decl;
			decl.kind = AffectedDeclaration::Kind::Enum;
			decl.name = enumDef->name;
			decl.index = i;
			decl.range = enumRange;
			affected.push_back(decl);
		}
	}

	// Check interfaces
	for (size_t i = 0; i < ast->interfaces.size(); ++i) {
		const auto &iface = ast->interfaces[i];
		SourceRange ifaceRange = iface->getSourceRange();

		if (ifaceRange.overlaps(editRange)) {
			AffectedDeclaration decl;
			decl.kind = AffectedDeclaration::Kind::Interface;
			decl.name = iface->name;
			decl.index = i;
			decl.range = ifaceRange;
			affected.push_back(decl);
		}
	}

	return affected;
}

std::vector<size_t> IncrementalParser::findAffectedStatements(
	const FunctionAST *func, const EditRegion &edit) const {

	std::vector<size_t> affected;
	if (!func) {
		return affected;
	}

	SourceRange editRange = edit.toSourceRange();

	for (size_t i = 0; i < func->body.size(); ++i) {
		const auto &stmt = func->body[i];
		SourceRange stmtRange = stmt->getSourceRange();

		if (stmtRange.overlaps(editRange)) {
			affected.push_back(i);
		}
	}

	return affected;
}

bool IncrementalParser::canHandleIncrementally(
	const ProgramAST *ast, const EditRegion &edit) const {

	if (!ast) {
		return false;
	}

	// Check if the edit spans too many lines
	int editLines = edit.endLine - edit.startLine + 1;
	if (editLines > MAX_INCREMENTAL_EDIT_LINES) {
		return false;
	}

	// Check the number of affected declarations
	auto affected = findAffectedDeclarations(ast, edit);
	if (affected.size() > MAX_AFFECTED_DECLARATIONS) {
		return false;
	}

	// If the edit is entirely within whitespace/comments between declarations,
	// we can handle it incrementally (just retokenize)
	if (affected.empty()) {
		return true;
	}

	return true;
}

std::string IncrementalParser::applyEdit(
	const std::string &source, const EditRegion &edit) {

	size_t startOffset = positionToOffset(source, edit.startLine, edit.startCol);
	size_t endOffset = positionToOffset(source, edit.endLine, edit.endCol);

	// Validate offsets
	if (startOffset > source.size()) {
		startOffset = source.size();
	}
	if (endOffset > source.size()) {
		endOffset = source.size();
	}
	if (startOffset > endOffset) {
		startOffset = endOffset;
	}

	// Apply the edit: source[0..startOffset) + newText + source[endOffset..)
	std::string result;
	result.reserve(source.size() - (endOffset - startOffset) + edit.newText.size());
	result.append(source, 0, startOffset);
	result.append(edit.newText);
	result.append(source, endOffset, std::string::npos);

	return result;
}

size_t IncrementalParser::positionToOffset(
	const std::string &source, int line, int col) {

	if (line <= 0) {
		return 0;
	}

	size_t offset = 0;
	int currentLine = 1;

	// Find the start of the target line
	while (offset < source.size() && currentLine < line) {
		if (source[offset] == '\n') {
			currentLine++;
		}
		offset++;
	}

	// Add the column offset
	size_t colOffset = static_cast<size_t>(col);
	size_t lineEnd = source.find('\n', offset);
	if (lineEnd == std::string::npos) {
		lineEnd = source.size();
	}

	// Clamp column to line length
	size_t lineLength = lineEnd - offset;
	if (colOffset > lineLength) {
		colOffset = lineLength;
	}

	return offset + colOffset;
}

std::pair<int, int> IncrementalParser::getRetokenizationRange(
	const TokenStream &tokens, const EditRegion &edit) {

	// Start from the edit's start line, but expand to include any tokens
	// that might span across lines (like multi-line strings)
	int startLine = edit.startLine;
	int endLine = edit.endLine;

	// Look at the line index to find tokens that might be affected
	auto [startIdx, endIdx] = tokens.getTokenIndicesForLineRange(startLine, endLine);

	// If we found tokens, include one token before and after to handle edge cases
	if (startIdx > 0) {
		int prevLine = static_cast<int>(tokens[startIdx - 1].get_line());
		if (prevLine < startLine) {
			startLine = prevLine;
		}
	}

	if (endIdx < tokens.size()) {
		int nextLine = static_cast<int>(tokens[endIdx].get_line());
		if (nextLine > endLine) {
			endLine = nextLine;
		}
	}

	return {startLine, endLine};
}

IncrementalParseResult IncrementalParser::update(
	std::unique_ptr<ProgramAST> oldAST,
	TokenStream &oldTokens,
	const std::string &oldSource,
	const EditRegion &edit,
	const std::string &defaultNamespace) {

	IncrementalParseResult result;
	result.wasFullReparse = false;
	result.tokensRetokenized = 0;
	result.declarationsReparsed = 0;
	result.declarationsPreserved = 0;

	// Check if we can handle this incrementally
	if (!canHandleIncrementally(oldAST.get(), edit)) {
		// Fall back to full reparse
		result.wasFullReparse = true;

		std::string newSource = applyEdit(oldSource, edit);
		Lexer lexer(newSource);
		TokenStream newTokens = lexer.tokenize_stream();
		newTokens.buildLineIndex();

		Parser parser(std::move(newTokens));
		parser.setDefaultNamespace(defaultNamespace);
		result.ast = parser.parse();

		// Copy errors if any
		for (const auto &err : parser.getParseErrors()) {
			result.errors.push_back(err.message);
		}

		// Update the oldTokens reference to the new tokens
		oldTokens = lexer.tokenize_stream();
		oldTokens.buildLineIndex();

		result.tokensRetokenized = oldTokens.size();
		return result;
	}

	// Apply the edit to get new source
	std::string newSource = applyEdit(oldSource, edit);

	// Determine the retokenization range
	auto [retokStartLine, retokEndLine] = getRetokenizationRange(oldTokens, edit);

	// Get the token indices for the affected range
	auto [startTokenIdx, endTokenIdx] = oldTokens.getTokenIndicesForLineRange(
		retokStartLine, retokEndLine);

	// Calculate how many lines changed
	int lineDelta = edit.lineDelta();

	// Extract the portion of new source that needs retokenizing
	// We need to include lines up to the first unaffected line after the edit
	int newRetokEndLine = retokEndLine + lineDelta;
	if (newRetokEndLine < retokStartLine) {
		newRetokEndLine = retokStartLine;
	}

	std::string regionSource = extractLines(newSource, retokStartLine, newRetokEndLine);

	// Tokenize the new region
	Lexer lexer(regionSource);
	std::vector<Token> newTokens = lexer.tokenize();

	// Adjust line numbers in new tokens (they start at line 1 from the lexer)
	// They should start at retokStartLine
	for (auto &tok : newTokens) {
		tok.line = tok.line + retokStartLine - 1;
	}

	// Splice the new tokens into the token stream
	oldTokens.splice(startTokenIdx, endTokenIdx, newTokens);
	result.tokensRetokenized = newTokens.size();

	// If line numbers changed, adjust tokens after the edit region
	if (lineDelta != 0) {
		adjustTokenLines(oldTokens, retokEndLine + 1, lineDelta);
	}

	// Find which declarations were affected
	auto affected = findAffectedDeclarations(oldAST.get(), edit);

	if (affected.empty()) {
		// No declarations affected, just return the old AST
		// (the tokenization was updated but the AST structure is the same)
		result.ast = std::move(oldAST);
		result.declarationsPreserved =
			result.ast->functions.size() +
			result.ast->structs.size() +
			result.ast->enums.size() +
			result.ast->interfaces.size();
		return result;
	}

	// For now, if any declarations are affected, do a full reparse of those declarations
	// In a more sophisticated implementation, we would only reparse the affected portions

	// Create a new AST, preserving unaffected declarations
	result.ast = std::make_unique<ProgramAST>();

	// Track which declarations are affected
	std::vector<bool> functionAffected(oldAST->functions.size(), false);
	std::vector<bool> structAffected(oldAST->structs.size(), false);
	std::vector<bool> enumAffected(oldAST->enums.size(), false);
	std::vector<bool> interfaceAffected(oldAST->interfaces.size(), false);

	for (const auto &aff : affected) {
		switch (aff.kind) {
		case AffectedDeclaration::Kind::Function:
			functionAffected[aff.index] = true;
			break;
		case AffectedDeclaration::Kind::Struct:
			structAffected[aff.index] = true;
			break;
		case AffectedDeclaration::Kind::Enum:
			enumAffected[aff.index] = true;
			break;
		case AffectedDeclaration::Kind::Interface:
			interfaceAffected[aff.index] = true;
			break;
		}
	}

	// Full reparse of the new source to get the new declarations
	// (This is simpler and more robust than trying to reparse individual declarations)
	Lexer fullLexer(newSource);
	TokenStream fullTokens = fullLexer.tokenize_stream();
	fullTokens.buildLineIndex();

	Parser fullParser(std::move(fullTokens));
	fullParser.setDefaultNamespace(defaultNamespace);
	auto newAST = fullParser.parse();

	// Copy errors from full parse
	for (const auto &err : fullParser.getParseErrors()) {
		result.errors.push_back(err.message);
	}

	// The full parse gives us the correct new AST
	result.ast = std::move(newAST);
	result.declarationsReparsed = affected.size();
	result.declarationsPreserved =
		(oldAST->functions.size() - std::count(functionAffected.begin(), functionAffected.end(), true)) +
		(oldAST->structs.size() - std::count(structAffected.begin(), structAffected.end(), true)) +
		(oldAST->enums.size() - std::count(enumAffected.begin(), enumAffected.end(), true)) +
		(oldAST->interfaces.size() - std::count(interfaceAffected.begin(), interfaceAffected.end(), true));

	// Update oldTokens to match the new source
	oldTokens = fullLexer.tokenize_stream();
	oldTokens.buildLineIndex();

	return result;
}

std::string IncrementalParser::extractLines(
	const std::string &source, int startLine, int endLine) {

	if (startLine <= 0 || endLine < startLine) {
		return "";
	}

	size_t startOffset = positionToOffset(source, startLine, 0);

	// Find the end of endLine
	size_t endOffset = startOffset;
	int currentLine = startLine;
	while (endOffset < source.size() && currentLine <= endLine) {
		if (source[endOffset] == '\n') {
			currentLine++;
			if (currentLine > endLine) {
				endOffset++;  // Include the final newline
				break;
			}
		}
		endOffset++;
	}

	if (startOffset >= source.size()) {
		return "";
	}

	return source.substr(startOffset, endOffset - startOffset);
}

int IncrementalParser::offsetToLine(const std::string &source, size_t offset) {
	int line = 1;
	for (size_t i = 0; i < offset && i < source.size(); ++i) {
		if (source[i] == '\n') {
			line++;
		}
	}
	return line;
}

void IncrementalParser::adjustTokenLines(
	TokenStream &tokens, int fromLine, int lineDelta) {

	// TokenStream uses CompactToken which stores line numbers directly
	// We need to iterate and adjust lines for tokens at or after fromLine

	// Unfortunately, TokenStream doesn't expose mutable access to tokens directly
	// in a way that lets us modify line numbers efficiently.
	// For now, we rebuild the line index (which is what splice() does anyway)

	// In a production implementation, we would add a method to TokenStream
	// specifically for adjusting line numbers in-place.

	// Since we already called splice() and it rebuilds the line index,
	// and we're doing a full reparse for affected declarations anyway,
	// we can skip this adjustment for now.
	(void)tokens;
	(void)fromLine;
	(void)lineDelta;
}

std::unique_ptr<FunctionAST> IncrementalParser::cloneFunction(const FunctionAST *func) {
	if (!func) {
		return nullptr;
	}

	// Deep clone of function - for now, we don't actually clone the body
	// because we're doing full reparses. This is a placeholder for future optimization.
	auto clone = std::make_unique<FunctionAST>(
		func->name,
		std::vector<FunctionParameter>(func->parameters),
		func->returnType,
		std::vector<std::unique_ptr<StmtAST>>(),  // Body would need deep clone
		func->isExtern,
		func->line,
		func->column,
		func->namespaceName,
		func->isExported,
		func->dllName,
		func->isStaticLib,
		func->libPath,
		func->receiverType,
		func->implementsInterface
	);
	clone->setEndPosition(func->endLine, func->endColumn);

	return clone;
}

std::unique_ptr<StructDefAST> IncrementalParser::cloneStruct(const StructDefAST *structDef) {
	if (!structDef) {
		return nullptr;
	}

	// Placeholder for deep clone
	std::vector<StructField> clonedFields;
	for (const auto &field : structDef->fields) {
		clonedFields.emplace_back(
			field.name, field.type, field.isImmutable,
			nullptr,  // defaultValue would need deep clone
			field.line, field.column
		);
	}

	auto clone = std::make_unique<StructDefAST>(
		structDef->name,
		std::move(clonedFields),
		structDef->line,
		structDef->column,
		structDef->namespaceName,
		structDef->isExported,
		std::vector<std::string>(structDef->conformsTo),
		std::vector<std::unique_ptr<FunctionAST>>(),  // Methods would need deep clone
		std::map<std::string, std::string>(structDef->typeAssignments),
		std::map<std::string, std::vector<std::string>>(structDef->interfaceTypeBindings),
		std::vector<std::string>(structDef->associatedTypeParams)
	);
	clone->setEndPosition(structDef->endLine, structDef->endColumn);

	return clone;
}

std::unique_ptr<EnumDefAST> IncrementalParser::cloneEnum(const EnumDefAST *enumDef) {
	if (!enumDef) {
		return nullptr;
	}

	// Placeholder for deep clone
	std::vector<EnumCaseAST> clonedCases;
	for (const auto &ec : enumDef->cases) {
		std::vector<EnumAssocValue> clonedAssoc;
		for (const auto &av : ec.associatedValues) {
			clonedAssoc.emplace_back(av.name, av.type, av.line, av.column);
		}
		clonedCases.emplace_back(
			ec.name, ec.line, ec.column,
			std::move(clonedAssoc),
			nullptr  // rawValue would need deep clone
		);
	}

	auto clone = std::make_unique<EnumDefAST>(
		enumDef->name,
		std::move(clonedCases),
		enumDef->line,
		enumDef->column,
		enumDef->namespaceName,
		enumDef->isExported,
		enumDef->rawValueType,
		std::vector<std::unique_ptr<FunctionAST>>()  // Methods would need deep clone
	);
	clone->setEndPosition(enumDef->endLine, enumDef->endColumn);

	return clone;
}

std::unique_ptr<InterfaceDefAST> IncrementalParser::cloneInterface(const InterfaceDefAST *iface) {
	if (!iface) {
		return nullptr;
	}

	std::vector<InterfaceMethodSignature> clonedMethods;
	for (const auto &method : iface->methods) {
		clonedMethods.emplace_back(
			method.name,
			std::vector<FunctionParameter>(method.parameters),
			method.returnType,
			method.line,
			method.column
		);
	}

	auto clone = std::make_unique<InterfaceDefAST>(
		iface->name,
		std::move(clonedMethods),
		iface->line,
		iface->column,
		iface->namespaceName,
		iface->isExported,
		std::vector<std::string>(iface->associatedTypes)
	);
	clone->setEndPosition(iface->endLine, iface->endColumn);

	return clone;
}
