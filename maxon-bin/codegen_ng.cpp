/**
 * MIR Code Generator (Next Generation)
 *
 * Minimal implementation that generates MIR from the AST.
 * Currently supports only the features needed for examples/basic.maxon.
 */

#include "codegen_ng.h"
#include "compiler_stats.h"
#include "mir/optimizer.h"

//==============================================================================
// Constructor / Destructor
//==============================================================================

MIRCodeGenerator::MIRCodeGenerator(const std::string &moduleName, bool debugInfo,
								   int verboseLevel, bool trackAllocs)
	: logger_(GlobalLogger::instance()) {
	(void)debugInfo;	// Not used in minimal implementation
	(void)trackAllocs;	// Not used in minimal implementation
	(void)verboseLevel; // Now handled by GlobalLogger

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
// Variable/Parameter Generation Helper
//==============================================================================

void MIRCodeGenerator::generateLocalVariable(const std::string &name, const std::string &typeStr,
											 ExprAST *initializer, mir::MIRValue *existingValue) {
	mir::MIRType *type = mir::MIRType::fromName(typeStr);

	// Create alloca for the variable
	mir::MIRValue *alloca = builder->createAlloca(type, name);

	// Store either the existing value (for parameters) or generate from initializer
	if (existingValue) {
		builder->createStore(existingValue, alloca);
	} else if (initializer) {
		mir::MIRValue *initVal = initializer->generate(*this);
		builder->createStore(initVal, alloca);
	}

	// Track the variable
	namedValues[name] = alloca;
}

mir::MIRValue *MIRCodeGenerator::lookupVariable(const std::string &name) {
	auto it = namedValues.find(name);
	if (it == namedValues.end()) {
		return nullptr;
	}
	return it->second;
}

void MIRCodeGenerator::trackVariable(const std::string &name, mir::MIRValue *value) {
	namedValues[name] = value;
}

//==============================================================================
// Main Generate Function
//==============================================================================

void MIRCodeGenerator::generate(ProgramAST *program, bool needsEntryPoint,
								const std::map<std::string, size_t> *,
								const std::map<std::string, std::string> *) {
	logger_.progress(LogPhase::MIR, "Generating MIR...");

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

	logger_.progress(LogPhase::MIR, "MIR generation complete: ", module->functions.size(), " functions");
}

//==============================================================================
// Optimization
//==============================================================================

void MIRCodeGenerator::optimize(CompilerStats *stats, bool skipForExplorer) {
	logger_.progress(LogPhase::Opt, "Running optimization passes...");

	mir::MIROptimizer optimizer = mir::MIROptimizer::createOptimizerPipeline(logger_.getVerboseLevel(), skipForExplorer);
	optimizer.runPasses(*module, 10, stats);

	logger_.progress(LogPhase::Opt, "Optimization complete");
}

void MIRCodeGenerator::runDeadCodeElimination() {
	logger_.detail(LogPhase::Opt, "Running dead code elimination");
	mir::DeadCodeEliminationPass dce;
	dce.setVerboseLevel(logger_.getVerboseLevel());
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

//==============================================================================
// Base AST Class Implementations
//==============================================================================

mir::MIRValue *ExprAST::generate(MIRCodeGenerator &cg) const {
	throw std::runtime_error("unimplemented");
}

void StmtAST::generate(MIRCodeGenerator &cg) const {
	throw std::runtime_error("unimplemented");
}
