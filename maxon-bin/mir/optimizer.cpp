#include "optimizer.h"
#include "../compiler_stats.h"
#include <chrono>

namespace mir {

//==============================================================================
// MIR Optimizer (Pass Manager) Implementation
//==============================================================================

// Helper to count total instructions in a module
static size_t countModuleInstructions(MIRModule &module) {
	size_t count = 0;
	for (const auto &func : module.functions) {
		if (!func->isExternal) {
			for (const auto &block : func->basicBlocks) {
				count += block->instructions.size();
			}
		}
	}
	return count;
}

void MIROptimizer::addPass(std::unique_ptr<OptimizationPass> pass) {
	pass->setVerboseLevel(verboseLevel_);
	passes.push_back(std::move(pass));
}

int MIROptimizer::runAllPasses(MIRModule &module, CompilerStats *stats) {
	Logger &logger = GlobalLogger::instance();

	// Debug: dump MIR before optimization to find corruption
	if (logger.isEnabled(3)) {
		logger.trace(LogPhase::Opt, "Dumping MIR before optimization:");
		for (const auto &func : module.functions) {
			logger.trace(LogPhase::Opt, "  Function: ", func->name);
			if (func->isExternal) {
				logger.trace(LogPhase::Opt, "    (external)");
				continue;
			}
			for (const auto &block : func->basicBlocks) {
				logger.trace(LogPhase::Opt, "    Block: ", block->name);
				for (size_t i = 0; i < block->instructions.size(); i++) {
					auto &inst = block->instructions[i];
					logger.trace(LogPhase::Opt, "      Inst ", i, ": opcode=", static_cast<int>(inst->opcode),
								 " operands=", inst->operands.size());
					for (size_t j = 0; j < inst->operands.size(); j++) {
						auto *op = inst->operands[j];
						if (op) {
							logger.trace(LogPhase::Opt, "        Op ", j, ": kind=", static_cast<int>(op->kind));
						} else {
							logger.trace(LogPhase::Opt, "        Op ", j, ": NULL");
						}
					}
				}
			}
		}
	}
	int totalChanges = 0;
	bool anyChange;
	int iteration = 0;

	logger.progress(LogPhase::Opt, "Starting optimization passes (", passes.size(), " passes)");

	do {
		anyChange = false;
		iteration++;
		logger.detail(LogPhase::Opt, "Iteration ", iteration);
		for (auto &pass : passes) {
			size_t instrBefore = stats ? countModuleInstructions(module) : 0;
			auto passStart = std::chrono::high_resolution_clock::now();

			if (stats) {
				pass->resetStats();
			}
			bool madeChanges = pass->run(module);

			if (stats) {
				auto passEnd = std::chrono::high_resolution_clock::now();
				auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(passEnd - passStart);
				size_t instrAfter = countModuleInstructions(module);
				stats->recordOptimizerPass(pass->getName(), elapsed, madeChanges, instrBefore, instrAfter);
				// Record pass-specific stats
				auto [count, label] = pass->getPassSpecificStats();
				if (count > 0 && label) {
					stats->addPassSpecificStat(pass->getName(), count, label);
				}
			}

			if (madeChanges) {
				anyChange = true;
				totalChanges++;
				logger.detail(LogPhase::Opt, "Pass '", pass->getName(), "' made changes");
			}
		}
	} while (anyChange);

	if (stats) {
		stats->setOptimizerIterations(iteration);
	}

	logger.progress(LogPhase::Opt, "Optimization complete after ", iteration, " iteration(s), ",
					totalChanges, " total changes");

	return totalChanges;
}

void MIROptimizer::runPasses(MIRModule &module, int maxIterations, CompilerStats *stats) {
	Logger &logger = GlobalLogger::instance();
	logger.progress(LogPhase::Opt, "Running passes (max ", maxIterations, " iterations)");

	int iteration = 0;
	for (int i = 0; i < maxIterations; ++i) {
		bool anyChange = false;
		iteration++;
		for (auto &pass : passes) {
			size_t instrBefore = stats ? countModuleInstructions(module) : 0;
			auto passStart = std::chrono::high_resolution_clock::now();

			if (stats) {
				pass->resetStats();
			}
			bool madeChanges = pass->run(module);

			if (stats) {
				auto passEnd = std::chrono::high_resolution_clock::now();
				auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(passEnd - passStart);
				size_t instrAfter = countModuleInstructions(module);
				stats->recordOptimizerPass(pass->getName(), elapsed, madeChanges, instrBefore, instrAfter);
				// Record pass-specific stats
				auto [count, label] = pass->getPassSpecificStats();
				if (count > 0 && label) {
					stats->addPassSpecificStat(pass->getName(), count, label);
				}
			}

			if (madeChanges) {
				anyChange = true;
				logger.detail(LogPhase::Opt, "Pass '", pass->getName(), "' made changes");
			}
		}
		if (!anyChange) {
			logger.progress(LogPhase::Opt, "Converged after ", (i + 1), " iteration(s)");
			break;
		}
	}

	if (stats) {
		stats->setOptimizerIterations(iteration);
	}
}

void MIROptimizer::clearPasses() {
	passes.clear();
}

MIROptimizer MIROptimizer::createOptimizerPipeline(int verboseLevel, bool skipForExplorer) {
	MIROptimizer optimizer;
	optimizer.setVerboseLevel(verboseLevel);

	// Order matters: run mem2reg first, then cleanup passes
	optimizer.addPass(std::make_unique<Mem2RegPass>());
	optimizer.addPass(std::make_unique<ConstantFoldingPass>());
	optimizer.addPass(std::make_unique<ConstantPropagationPass>());
	optimizer.addPass(std::make_unique<AlgebraicSimplificationPass>());
	optimizer.addPass(std::make_unique<StrengthReductionPass>());
	optimizer.addPass(std::make_unique<CopyPropagationPass>());
	optimizer.addPass(std::make_unique<RedundantLoadStoreEliminationPass>());
	optimizer.addPass(std::make_unique<IntegerDivisionOptimizationPass>());
	if (!skipForExplorer) {
		optimizer.addPass(std::make_unique<DeadCodeEliminationPass>());
	}
	optimizer.addPass(std::make_unique<UnreachableBlockEliminationPass>());
	if (!skipForExplorer) {
		optimizer.addPass(std::make_unique<SimpleFunctionInliningPass>());
	}
	return optimizer;
}

} // namespace mir
