/**
 * MIR Code Generator (Next Generation)
 *
 * Minimal implementation that generates MIR from the AST.
 * Currently supports only the features needed for examples/basic.maxon.
 */

#include "codegen_ng.h"
#include "compiler_stats.h"
#include "mir/optimizer.h"

#include <iostream>

//==============================================================================
// Constructor / Destructor
//==============================================================================

MIRCodeGenerator::MIRCodeGenerator(const std::string &moduleName, bool debugInfo,
								   int verboseLevel, bool trackAllocs)
	: verboseLevel(verboseLevel) {
	(void)debugInfo;   // Not used in minimal implementation
	(void)trackAllocs; // Not used in minimal implementation

	module = std::make_unique<mir::MIRModule>(moduleName);
#ifdef _WIN32
	module->targetTriple = "x86_64-pc-windows-msvc";
#else
	module->targetTriple = "x86_64-pc-linux-gnu";
#endif
	builder = std::make_unique<mir::MIRBuilder>(module.get());
}

MIRCodeGenerator::~MIRCodeGenerator() = default;

//==============================================================================
// Logging Helpers
//==============================================================================

void MIRCodeGenerator::logProgress(const std::string &msg) {
	if (verboseLevel >= 1) {
		std::cout << "[MIR] " << msg << std::endl;
	}
}

void MIRCodeGenerator::logDetail(const std::string &msg) {
	if (verboseLevel >= 2) {
		std::cout << "[MIR]   " << msg << std::endl;
	}
}

//==============================================================================
// Type Conversion
//==============================================================================

mir::MIRType *MIRCodeGenerator::getTypeFromString(const std::string &typeStr) {
	if (typeStr == "int") {
		return mir::MIRType::getInt64();
	}
	if (typeStr == "void" || typeStr.empty()) {
		return mir::MIRType::getVoid();
	}
	throw std::runtime_error("Unsupported type: " + typeStr);
}

//==============================================================================
// Main Generate Function
//==============================================================================

void MIRCodeGenerator::generate(ProgramAST *program, bool needsEntryPoint,
								const std::map<std::string, size_t> *,
								const std::map<std::string, std::string> *) {
	logProgress("Generating MIR...");

	// First pass: declare all functions (so calls can resolve)
	for (auto &func : program->functions) {
		declareFunction(func.get());
	}

	// Second pass: generate function bodies
	for (auto &func : program->functions) {
		generateFunction(func.get());
	}

	// Create entry point if needed
	if (needsEntryPoint) {
		createEntryPoint();
	}

	logProgress("MIR generation complete: " + std::to_string(module->functions.size()) + " functions");
}

//==============================================================================
// Optimization
//==============================================================================

void MIRCodeGenerator::optimize(CompilerStats *stats) {
	logProgress("Running optimization passes...");

	mir::MIROptimizer optimizer = mir::MIROptimizer::createOptimizerPipeline(verboseLevel, false);
	optimizer.runPasses(*module, 10, stats);

	logProgress("Optimization complete");
}

void MIRCodeGenerator::optimizeForExplorer() {
	logProgress("Running explorer optimization passes...");

	mir::MIROptimizer optimizer = mir::MIROptimizer::createOptimizerPipeline(verboseLevel, true);
	optimizer.runPasses(*module, 10, nullptr);

	logProgress("Explorer optimization complete");
}

void MIRCodeGenerator::runDeadCodeElimination() {
	logDetail("Running dead code elimination");
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(verboseLevel);
	dce.run(*module);
}

size_t MIRCodeGenerator::getInstructionCount() const {
	size_t count = 0;
	for (const auto &func : module->functions) {
		if (!func->isExternal) {
			for (const auto &block : func->basicBlocks) {
				count += block->instructions.size();
			}
		}
	}
	return count;
}
