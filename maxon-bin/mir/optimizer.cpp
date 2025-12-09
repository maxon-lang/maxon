#include "optimizer.h"
#include "../compiler_stats.h"
#include <chrono>
#include <iostream>

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
	// Debug: dump MIR before optimization to find corruption
	if (verboseLevel_ >= 3) {
		std::cout << "[Opt Debug] Dumping MIR before optimization:" << std::endl;
		for (const auto &func : module.functions) {
			std::cout << "  Function: " << func->name << std::endl;
			if (func->isExternal) {
				std::cout << "    (external)" << std::endl;
				continue;
			}
			for (const auto &block : func->basicBlocks) {
				std::cout << "    Block: " << block->name << std::endl;
				for (size_t i = 0; i < block->instructions.size(); i++) {
					auto &inst = block->instructions[i];
					std::cout << "      Inst " << i << ": opcode=" << static_cast<int>(inst->opcode)
							  << " operands=" << inst->operands.size() << std::endl;
					for (size_t j = 0; j < inst->operands.size(); j++) {
						auto *op = inst->operands[j];
						if (op) {
							std::cout << "        Op " << j << ": kind=" << static_cast<int>(op->kind) << std::endl;
						} else {
							std::cout << "        Op " << j << ": NULL" << std::endl;
						}
					}
				}
			}
		}
	}
	int totalChanges = 0;
	bool anyChange;
	int iteration = 0;

	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Starting optimization passes (" << passes.size() << " passes)" << std::endl;
	}

	do {
		anyChange = false;
		iteration++;
		if (verboseLevel_ >= 2) {
			std::cout << "[Opt] Iteration " << iteration << std::endl;
		}
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
				if (verboseLevel_ >= 2) {
					std::cout << "[Opt]   Pass '" << pass->getName() << "' made changes" << std::endl;
				}
			}
		}
	} while (anyChange);

	if (stats) {
		stats->setOptimizerIterations(iteration);
	}

	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Optimization complete after " << iteration << " iteration(s), "
				  << totalChanges << " total changes" << std::endl;
	}

	return totalChanges;
}

void MIROptimizer::runPasses(MIRModule &module, int maxIterations, CompilerStats *stats) {
	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Running passes (max " << maxIterations << " iterations)" << std::endl;
	}

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
				if (verboseLevel_ >= 2) {
					std::cout << "[Opt]   Pass '" << pass->getName() << "' made changes" << std::endl;
				}
			}
		}
		if (!anyChange) {
			if (verboseLevel_ >= 1) {
				std::cout << "[Opt] Converged after " << (i + 1) << " iteration(s)" << std::endl;
			}
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

MIROptimizer MIROptimizer::createStandardPipeline(int verboseLevel) {
	MIROptimizer optimizer;
	optimizer.setVerboseLevel(verboseLevel);

	// Order matters: run mem2reg first, then cleanup passes
	optimizer.addPass(std::make_unique<Mem2RegPass>());
	// optimizer.addPass(std::make_unique<ConstantFoldingPass>());
	// optimizer.addPass(std::make_unique<ConstantPropagationPass>());
	// optimizer.addPass(std::make_unique<AlgebraicSimplificationPass>());
	// optimizer.addPass(std::make_unique<StrengthReductionPass>());
	// optimizer.addPass(std::make_unique<CopyPropagationPass>());
	// optimizer.addPass(std::make_unique<RedundantLoadStoreEliminationPass>());
	// optimizer.addPass(std::make_unique<IntegerDivisionOptimizationPass>());
	// optimizer.addPass(std::make_unique<DeadCodeEliminationPass>());
	// optimizer.addPass(std::make_unique<UnreachableBlockEliminationPass>());
	// optimizer.addPass(std::make_unique<SimpleFunctionInliningPass>());

	return optimizer;
}

} // namespace mir
