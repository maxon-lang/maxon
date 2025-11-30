#pragma once

#include <chrono>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

/**
 * CompilerStats - Collects and reports statistics from compilation phases.
 *
 * Statistics include:
 * - Per-phase timing (lexer, parser, semantic, MIR gen, optimizer, codegen)
 * - Token count, AST node count, function count
 * - MIR instruction counts (before/after optimization)
 * - Per-optimizer-pass statistics
 * - Final executable size
 *
 * Usage:
 *   CompilerStats stats;
 *   stats.startPhase("Lexer");
 *   // ... do lexing ...
 *   stats.endPhase("Lexer");
 *   stats.setTokenCount(1234);
 *   // ... at end ...
 *   if (showStats) stats.print();
 */
class CompilerStats {
  public:
	using Clock = std::chrono::high_resolution_clock;
	using TimePoint = Clock::time_point;
	using Duration = std::chrono::microseconds;

	CompilerStats() : totalStartTime_(Clock::now()) {}

	//==========================================================================
	// Phase Timing
	//==========================================================================

	void startPhase(const std::string &phaseName) {
		phaseStartTimes_[phaseName] = Clock::now();
	}

	void endPhase(const std::string &phaseName) {
		auto it = phaseStartTimes_.find(phaseName);
		if (it != phaseStartTimes_.end()) {
			auto elapsed = std::chrono::duration_cast<Duration>(Clock::now() - it->second);
			phaseDurations_[phaseName] += elapsed;
			phaseOrder_.push_back(phaseName);
		}
	}

	Duration getPhaseDuration(const std::string &phaseName) const {
		auto it = phaseDurations_.find(phaseName);
		return it != phaseDurations_.end() ? it->second : Duration(0);
	}

	//==========================================================================
	// Metrics
	//==========================================================================

	void setTokenCount(size_t count) { tokenCount_ = count; }
	void setFunctionCount(size_t count) { functionCount_ = count; }
	void setStructCount(size_t count) { structCount_ = count; }
	void setAstExpressionCount(size_t count) { astExpressionCount_ = count; }
	void setAstStatementCount(size_t count) { astStatementCount_ = count; }
	void setMirInstructionsBefore(size_t count) { mirInstructionsBefore_ = count; }
	void setMirInstructionsAfter(size_t count) { mirInstructionsAfter_ = count; }
	void setCodeSize(size_t bytes) { codeSize_ = bytes; }
	void setExecutableSize(size_t bytes) { executableSize_ = bytes; }

	size_t getTokenCount() const { return tokenCount_; }
	size_t getFunctionCount() const { return functionCount_; }
	size_t getAstExpressionCount() const { return astExpressionCount_; }
	size_t getAstStatementCount() const { return astStatementCount_; }
	size_t getMirInstructionsBefore() const { return mirInstructionsBefore_; }
	size_t getMirInstructionsAfter() const { return mirInstructionsAfter_; }

	//==========================================================================
	// Per-File Statistics
	//==========================================================================

	struct FileStats {
		std::string filename;
		size_t tokens = 0;
		size_t functions = 0;
		size_t structs = 0;
		size_t expressions = 0;
		size_t statements = 0;
		Duration lexTime{0};
		Duration parseTime{0};
	};

	void recordFile(const std::string &filename, size_t tokens, size_t functions, size_t structs,
					size_t expressions, size_t statements, Duration lexTime, Duration parseTime) {
		FileStats fs;
		fs.filename = filename;
		fs.tokens = tokens;
		fs.functions = functions;
		fs.structs = structs;
		fs.expressions = expressions;
		fs.statements = statements;
		fs.lexTime = lexTime;
		fs.parseTime = parseTime;
		fileStats_.push_back(fs);
	}

	//==========================================================================
	// Optimizer Pass Statistics
	//==========================================================================

	struct OptimizerPassStats {
		std::string passName;
		int applicationCount = 0;		  // Number of times this pass made changes
		Duration totalTime{0};			  // Total time spent in this pass
		int totalInstructionsRemoved = 0; // Total instructions eliminated by this pass
		int passSpecificStat = 0;		  // Pass-specific statistic count
		std::string passSpecificLabel;	  // Label for the pass-specific stat
	};

	void recordOptimizerPass(const std::string &passName, Duration elapsed,
							 bool madeChanges, size_t instructionsBefore, size_t instructionsAfter) {
		auto &stats = optimizerPasses_[passName];
		stats.passName = passName;
		stats.totalTime += elapsed;
		if (madeChanges) {
			stats.applicationCount++;
			// Track instructions removed (could be negative if pass adds instructions)
			int delta = static_cast<int>(instructionsBefore) - static_cast<int>(instructionsAfter);
			stats.totalInstructionsRemoved += delta;
		}
	}

	// Record a pass-specific statistic with a custom label
	void addPassSpecificStat(const std::string &passName, int count, const std::string &label) {
		if (count == 0)
			return;
		auto &stats = optimizerPasses_[passName];
		stats.passName = passName;
		stats.passSpecificStat += count;
		if (stats.passSpecificLabel.empty()) {
			stats.passSpecificLabel = label;
		}
	}

	void setOptimizerIterations(int iterations) { optimizerIterations_ = iterations; }

	//==========================================================================
	// Output
	//==========================================================================

	void print(std::ostream &out = std::cerr) const {
		out << "\n";
		out << "=== Compilation Statistics ===\n";
		out << "\n";

		// Summary metrics
		out << "  Tokens: " << tokenCount_
			<< "  Functions: " << functionCount_
			<< "  Structs: " << structCount_ << "\n";

		if (astExpressionCount_ > 0 || astStatementCount_ > 0) {
			out << "  AST Nodes: " << (astExpressionCount_ + astStatementCount_)
				<< " (" << astExpressionCount_ << " expressions, "
				<< astStatementCount_ << " statements)\n";
		}

		if (mirInstructionsBefore_ > 0) {
			int reduction = 0;
			if (mirInstructionsBefore_ > mirInstructionsAfter_) {
				reduction = static_cast<int>(100.0 * (mirInstructionsBefore_ - mirInstructionsAfter_) /
											 mirInstructionsBefore_);
			}
			out << "  MIR Instructions: " << mirInstructionsBefore_
				<< " -> " << mirInstructionsAfter_
				<< " (" << reduction << "% reduction)\n";
		}

		if (executableSize_ > 0) {
			out << "  Executable size: " << executableSize_ << " bytes\n";
		}

		// Per-file statistics
		if (fileStats_.size() > 1) {
			out << "\n";
			out << "Per-File Statistics:\n";
			for (const auto &fs : fileStats_) {
				out << "  " << fs.filename << "\n";
				out << "    Tokens: " << fs.tokens
					<< "  Functions: " << fs.functions
					<< "  Structs: " << fs.structs
					<< "  AST: " << (fs.expressions + fs.statements) << "\n";
				out << "    Lex: " << formatDuration(fs.lexTime)
					<< "  Parse: " << formatDuration(fs.parseTime) << "\n";
			}
		}

		// Phase timings
		out << "\n";
		out << "Phase Timings:\n";

		// Use a specific order for main phases
		std::vector<std::string> mainPhases = {
			"Lexer", "Parser", "Semantic", "MIR Generation",
			"Dead Code Elimination", "Optimization", "x86 CodeGen", "PE/ELF Writing"};

		for (const auto &phase : mainPhases) {
			auto it = phaseDurations_.find(phase);
			if (it != phaseDurations_.end()) {
				out << "  " << std::left << std::setw(25) << phase
					<< std::right << std::setw(12) << formatDuration(it->second) << "\n";
			}
		}

		// Optimizer pass details
		if (!optimizerPasses_.empty()) {
			out << "\n";
			out << "Optimizer Passes (" << optimizerIterations_ << " iterations):\n";

			// Sort passes by order they were added to pipeline
			std::vector<std::string> passOrder = {
				"mem2reg", "constant-folding", "constant-propagation",
				"dead-code-elimination", "unreachable-block-elimination",
				"strength-reduction", "algebraic-simplification",
				"simple-function-inlining", "redundant-load-store-elimination",
				"copy-propagation", "phi-elimination"};

			for (const auto &passName : passOrder) {
				auto it = optimizerPasses_.find(passName);
				if (it != optimizerPasses_.end()) {
					const auto &stats = it->second;
					out << "  " << std::left << std::setw(32) << stats.passName
						<< std::right << std::setw(3) << stats.applicationCount << "x"
						<< std::setw(12) << formatDuration(stats.totalTime);
					if (stats.totalInstructionsRemoved != 0) {
						if (stats.totalInstructionsRemoved > 0) {
							out << "  -" << stats.totalInstructionsRemoved << " instr";
						} else {
							out << "  +" << (-stats.totalInstructionsRemoved) << " instr";
						}
					}
					if (stats.passSpecificStat > 0 && !stats.passSpecificLabel.empty()) {
						out << "  (" << stats.passSpecificStat << " " << stats.passSpecificLabel << ")";
					}
					out << "\n";
				}
			}
		}

		// Total time
		auto totalElapsed = std::chrono::duration_cast<Duration>(Clock::now() - totalStartTime_);
		out << "\n";
		out << "Total: " << formatDuration(totalElapsed) << "\n";
	}

  private:
	// Timing
	TimePoint totalStartTime_;
	std::map<std::string, TimePoint> phaseStartTimes_;
	std::map<std::string, Duration> phaseDurations_;
	std::vector<std::string> phaseOrder_;

	// Metrics
	size_t tokenCount_ = 0;
	size_t functionCount_ = 0;
	size_t structCount_ = 0;
	size_t astExpressionCount_ = 0;
	size_t astStatementCount_ = 0;
	size_t mirInstructionsBefore_ = 0;
	size_t mirInstructionsAfter_ = 0;
	size_t codeSize_ = 0;
	size_t executableSize_ = 0;

	// Per-file stats
	std::vector<FileStats> fileStats_;

	// Optimizer stats
	std::map<std::string, OptimizerPassStats> optimizerPasses_;
	int optimizerIterations_ = 0;

	static std::string formatDuration(Duration d) {
		auto us = d.count();
		if (us >= 1000000) {
			// Seconds
			double secs = us / 1000000.0;
			std::ostringstream ss;
			ss << std::fixed << std::setprecision(2) << secs << " s";
			return ss.str();
		} else if (us >= 1000) {
			// Milliseconds
			double ms = us / 1000.0;
			std::ostringstream ss;
			ss << std::fixed << std::setprecision(2) << ms << " ms";
			return ss.str();
		} else {
			// Microseconds
			std::ostringstream ss;
			ss << us << " us";
			return ss.str();
		}
	}
};
