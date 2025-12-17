#include "code_lens.h"
#include <sstream>

namespace maxon_lsp {

std::vector<CodeLens> CodeLensProvider::getCodeLenses(const Document &document,
													  const AnalysisCache *cache) {
	std::vector<CodeLens> lenses;

	if (!cache || !cache->ast) {
		return lenses;
	}

	const ProgramAST *ast = cache->ast.get();

	// Process top-level functions
	for (const auto &func : ast->functions) {
		if (!func || func->isMethod()) {
			continue; // Skip methods (processed with structs)
		}

		// Build function key to lookup mutation info
		std::string funcKey;
		if (!func->namespaceName.empty()) {
			funcKey = func->namespaceName + "." + func->name;
		} else {
			funcKey = func->name;
		}

		// Get mutated parameters (empty set if not found = pure)
		std::set<std::string> mutatedParams;
		auto it = cache->functionMutations.find(funcKey);
		if (it != cache->functionMutations.end()) {
			mutatedParams = it->second;
		}

		lenses.push_back(buildFunctionCodeLens(func->name, func->line, mutatedParams));
	}

	// Process struct methods
	for (const auto &structDef : ast->structs) {
		if (!structDef)
			continue;

		for (const auto &method : structDef->methods) {
			if (!method)
				continue;

			// Build method key
			std::string methodKey = structDef->name + "." + method->name;

			std::set<std::string> mutatedParams;
			auto it = cache->functionMutations.find(methodKey);
			if (it != cache->functionMutations.end()) {
				mutatedParams = it->second;
			}

			lenses.push_back(buildFunctionCodeLens(method->name, method->line, mutatedParams));
		}
	}

	// Process enum methods
	for (const auto &enumDef : ast->enums) {
		if (!enumDef)
			continue;

		for (const auto &method : enumDef->methods) {
			if (!method)
				continue;

			std::string methodKey = enumDef->name + "." + method->name;

			std::set<std::string> mutatedParams;
			auto it = cache->functionMutations.find(methodKey);
			if (it != cache->functionMutations.end()) {
				mutatedParams = it->second;
			}

			lenses.push_back(buildFunctionCodeLens(method->name, method->line, mutatedParams));
		}
	}

	return lenses;
}

CodeLens CodeLensProvider::buildFunctionCodeLens(const std::string &funcName, int line,
												 const std::set<std::string> &mutatedParams) {
	CodeLens lens;
	lens.range = buildCodeLensRange(line);

	// Build command (display only, no action on click)
	Command cmd;
	if (mutatedParams.empty()) {
		cmd.title = "pure";
	} else {
		// Show "mutating" with parameter names
		std::ostringstream title;
		title << "mutating: ";
		bool first = true;
		for (const auto &param : mutatedParams) {
			if (!first)
				title << ", ";
			first = false;
			title << param;
		}
		cmd.title = title.str();
	}
	cmd.command = ""; // No command to execute (info-only CodeLens)
	lens.command = cmd;

	return lens;
}

Range CodeLensProvider::buildCodeLensRange(int line) {
	// Convert 1-based line to 0-based LSP position
	// CodeLens appears at the start of the function declaration line
	int l = line > 0 ? line - 1 : 0;
	return Range(Position(l, 0), Position(l, 0));
}

} // namespace maxon_lsp
