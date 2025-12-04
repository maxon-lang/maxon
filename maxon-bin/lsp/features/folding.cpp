#include "folding.h"
#include <sstream>

namespace maxon_lsp {

std::vector<FoldingRange> FoldingRangeProvider::getFoldingRanges(
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<FoldingRange> ranges;

	// Extract from AST if available
	if (cache && cache->ast) {
		extractFromAST(cache->ast.get(), ranges);
	}

	// Also extract comment blocks from raw text
	extractCommentBlocks(document.content, ranges);

	return ranges;
}

void FoldingRangeProvider::extractFromAST(const ASTNode *ast, std::vector<FoldingRange> &ranges) {
	if (!ast)
		return;

	// Handle different AST node types
	if (auto *program = dynamic_cast<const ProgramAST *>(ast)) {
		// Process all functions
		for (const auto &func : program->functions) {
			extractFromAST(func.get(), ranges);
		}
		// Process all structs
		for (const auto &structDef : program->structs) {
			extractFromAST(structDef.get(), ranges);
		}
		// Process all enums
		for (const auto &enumDef : program->enums) {
			extractFromAST(enumDef.get(), ranges);
		}
		// Process all interfaces
		for (const auto &interfaceDef : program->interfaces) {
			extractFromAST(interfaceDef.get(), ranges);
		}
	} else if (auto *func = dynamic_cast<const FunctionAST *>(ast)) {
		// Function: fold from first line to end line
		if (func->line > 0 && func->endLine > func->line) {
			ranges.push_back(createRange(func->line, func->endLine, ""));
		}
		// Recurse into body statements
		for (const auto &stmt : func->body) {
			extractFromAST(stmt.get(), ranges);
		}
	} else if (auto *structDef = dynamic_cast<const StructDefAST *>(ast)) {
		// Struct: fold from first line to end line
		if (structDef->line > 0 && structDef->endLine > structDef->line) {
			ranges.push_back(createRange(structDef->line, structDef->endLine, ""));
		}
		// Recurse into methods
		for (const auto &method : structDef->methods) {
			extractFromAST(method.get(), ranges);
		}
	} else if (auto *enumDef = dynamic_cast<const EnumDefAST *>(ast)) {
		// Enum: fold from first line to end line
		if (enumDef->line > 0 && enumDef->endLine > enumDef->line) {
			ranges.push_back(createRange(enumDef->line, enumDef->endLine, ""));
		}
		// Recurse into methods
		for (const auto &method : enumDef->methods) {
			extractFromAST(method.get(), ranges);
		}
	} else if (auto *interfaceDef = dynamic_cast<const InterfaceDefAST *>(ast)) {
		// Interface: fold from first line to end line
		if (interfaceDef->line > 0 && interfaceDef->endLine > interfaceDef->line) {
			ranges.push_back(createRange(interfaceDef->line, interfaceDef->endLine, ""));
		}
	} else if (auto *ifStmt = dynamic_cast<const IfStmtAST *>(ast)) {
		// If statement: fold if we have end position
		if (ifStmt->line > 0 && ifStmt->endLine > ifStmt->line) {
			ranges.push_back(createRange(ifStmt->line, ifStmt->endLine, ""));
		}
		// Recurse into then/else bodies
		for (const auto &stmt : ifStmt->thenBody) {
			extractFromAST(stmt.get(), ranges);
		}
		for (const auto &stmt : ifStmt->elseBody) {
			extractFromAST(stmt.get(), ranges);
		}
	} else if (auto *whileStmt = dynamic_cast<const WhileStmtAST *>(ast)) {
		// While loop
		if (whileStmt->line > 0 && whileStmt->endLine > whileStmt->line) {
			ranges.push_back(createRange(whileStmt->line, whileStmt->endLine, ""));
		}
		for (const auto &stmt : whileStmt->body) {
			extractFromAST(stmt.get(), ranges);
		}
	} else if (auto *forStmt = dynamic_cast<const ForStmtAST *>(ast)) {
		// For loop
		if (forStmt->line > 0 && forStmt->endLine > forStmt->line) {
			ranges.push_back(createRange(forStmt->line, forStmt->endLine, ""));
		}
		for (const auto &stmt : forStmt->body) {
			extractFromAST(stmt.get(), ranges);
		}
	} else if (auto *matchStmt = dynamic_cast<const MatchStmtAST *>(ast)) {
		// Match statement
		if (matchStmt->line > 0 && matchStmt->endLine > matchStmt->line) {
			ranges.push_back(createRange(matchStmt->line, matchStmt->endLine, ""));
		}
		// Recurse into case statements
		for (const auto &caseArm : matchStmt->cases) {
			if (caseArm.statement) {
				extractFromAST(caseArm.statement.get(), ranges);
			}
		}
	}
}

void FoldingRangeProvider::extractCommentBlocks(const std::string &content, std::vector<FoldingRange> &ranges) {
	std::istringstream stream(content);
	std::string line;
	int lineNum = 0;
	int blockStart = -1;
	int blockEnd = -1;

	while (std::getline(stream, line)) {
		lineNum++;

		// Check if line starts with comment (allowing leading whitespace)
		size_t firstNonSpace = line.find_first_not_of(" \t");
		bool isComment = false;
		if (firstNonSpace != std::string::npos && firstNonSpace + 1 < line.size()) {
			if (line[firstNonSpace] == '/' && line[firstNonSpace + 1] == '/') {
				isComment = true;
			}
		}

		if (isComment) {
			if (blockStart < 0) {
				blockStart = lineNum;
			}
			blockEnd = lineNum;
		} else {
			// End of comment block
			if (blockStart > 0 && blockEnd > blockStart) {
				ranges.push_back(createRange(blockStart, blockEnd, "comment"));
			}
			blockStart = -1;
			blockEnd = -1;
		}
	}

	// Handle comment block at end of file
	if (blockStart > 0 && blockEnd > blockStart) {
		ranges.push_back(createRange(blockStart, blockEnd, "comment"));
	}
}

FoldingRange FoldingRangeProvider::createRange(int startLine, int endLine, const std::string &kind) {
	FoldingRange range;
	// Convert from 1-based (AST) to 0-based (LSP)
	range.startLine = startLine - 1;
	range.endLine = endLine - 1;
	if (!kind.empty()) {
		range.kind = kind;
	}
	return range;
}

} // namespace maxon_lsp
