#pragma once

#include <chrono>
#include <iostream>
#include <sstream>
#include <string>

/// Compiler phases for log prefixes
enum class LogPhase {
	General,  // No prefix
	Lexer,	  // [Lexer]
	Parser,	  // [Parser]
	Semantic, // [Semantic]
	MIR,	  // [MIR]
	Opt,	  // [Opt]
	RegAlloc, // [RegAlloc]
	x86,	  // [x86]
	PE,		  // [PE]
	ELF,	  // [ELF]
};

/// Logger class for consistent, verbosity-aware logging throughout the compiler.
///
/// Verbosity levels:
///   0 = Silent (errors only)
///   1 = Progress (phase names, summary info)
///   2 = Detailed (counts, timing, decisions)
///   3 = Trace (individual items, deep debugging)
///
/// Usage:
///   Logger log(verboseLevel);
///   log.progress(LogPhase::Lexer, "Tokenizing...");
///   log.detail(LogPhase::Lexer, "Found ", tokenCount, " tokens");
///   log.trace(LogPhase::Parser, "Parsing expression at line ", line);
class Logger {
  public:
	explicit Logger(int verboseLevel = 0) : verboseLevel_(verboseLevel) {
		startTime_ = std::chrono::high_resolution_clock::now();
	}

	/// Set the verbosity level
	void setVerboseLevel(int level) { verboseLevel_ = level; }
	int getVerboseLevel() const { return verboseLevel_; }

	/// Check if a given verbosity level is enabled
	bool isEnabled(int level) const { return verboseLevel_ >= level; }

	/// Log at progress level (level 1) - phase names, high-level status
	template <typename... Args>
	void progress(LogPhase phase, Args &&...args) {
		if (verboseLevel_ >= 1) {
			logImpl(phase, std::forward<Args>(args)...);
		}
	}

	/// Log at detail level (level 2) - counts, timing, intermediate results
	template <typename... Args>
	void detail(LogPhase phase, Args &&...args) {
		if (verboseLevel_ >= 2) {
			logImpl(phase, "  ", std::forward<Args>(args)...);
		}
	}

	/// Log at trace level (level 3) - individual items, deep debugging
	template <typename... Args>
	void trace(LogPhase phase, Args &&...args) {
		if (verboseLevel_ >= 3) {
			logImpl(phase, "    ", std::forward<Args>(args)...);
		}
	}

	/// Log without phase prefix (for continuation lines)
	template <typename... Args>
	void raw(int level, Args &&...args) {
		if (verboseLevel_ >= level) {
			(std::cout << ... << std::forward<Args>(args));
			std::cout << std::endl;
		}
	}

	/// Log an error (always printed, to stderr)
	template <typename... Args>
	void error(LogPhase phase, Args &&...args) {
		std::ostringstream ss;
		ss << phasePrefix(phase) << "[ERROR] ";
		(ss << ... << std::forward<Args>(args));
		std::cerr << ss.str() << std::endl;
	}

	/// Log a warning (always printed, to stderr)
	template <typename... Args>
	void warning(LogPhase phase, Args &&...args) {
		std::ostringstream ss;
		ss << phasePrefix(phase) << "[WARN] ";
		(ss << ... << std::forward<Args>(args));
		std::cerr << ss.str() << std::endl;
	}

	/// Start a timed section and return the start time
	std::chrono::high_resolution_clock::time_point startTimer() const {
		return std::chrono::high_resolution_clock::now();
	}

	/// Log elapsed time since startTime at detail level
	void logElapsed(LogPhase phase, const std::string &label,
					std::chrono::high_resolution_clock::time_point startTime) {
		if (verboseLevel_ >= 2) {
			auto endTime = std::chrono::high_resolution_clock::now();
			auto duration = std::chrono::duration_cast<std::chrono::microseconds>(endTime - startTime);
			if (duration.count() >= 1000) {
				auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(duration);
				detail(phase, label, ": ", ms.count(), "ms");
			} else {
				detail(phase, label, ": ", duration.count(), "µs");
			}
		}
	}

	/// Log total elapsed time since logger creation
	void logTotalElapsed(const std::string &label) {
		if (verboseLevel_ >= 1) {
			auto endTime = std::chrono::high_resolution_clock::now();
			auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime_);
			std::cout << label << ": " << duration.count() << "ms" << std::endl;
		}
	}

	/// Enter a named scope (for hierarchical logging)
	void enterScope(LogPhase phase, const std::string &scopeName) {
		if (verboseLevel_ >= 3) {
			trace(phase, ">>> Entering: ", scopeName);
		}
		scopeDepth_++;
	}

	/// Exit a named scope
	void exitScope(LogPhase phase, const std::string &scopeName) {
		scopeDepth_--;
		if (verboseLevel_ >= 3) {
			trace(phase, "<<< Exiting: ", scopeName);
		}
	}

  private:
	int verboseLevel_;
	int scopeDepth_ = 0;
	std::chrono::high_resolution_clock::time_point startTime_;

	/// Get the string prefix for a phase
	static const char *phasePrefix(LogPhase phase) {
		switch (phase) {
		case LogPhase::General:
			return "";
		case LogPhase::Lexer:
			return "[Lexer] ";
		case LogPhase::Parser:
			return "[Parser] ";
		case LogPhase::Semantic:
			return "[Semantic] ";
		case LogPhase::MIR:
			return "[MIR] ";
		case LogPhase::Opt:
			return "[Opt] ";
		case LogPhase::RegAlloc:
			return "[RegAlloc] ";
		case LogPhase::x86:
			return "[x86] ";
		case LogPhase::PE:
			return "[PE] ";
		case LogPhase::ELF:
			return "[ELF] ";
		}
		return "";
	}

	/// Internal implementation that does the actual logging
	template <typename... Args>
	void logImpl(LogPhase phase, Args &&...args) {
		std::cout << phasePrefix(phase);
		(std::cout << ... << std::forward<Args>(args));
		std::cout << std::endl;
	}
};

/// RAII helper for scope-based logging
class LogScope {
  public:
	LogScope(Logger &logger, LogPhase phase, const std::string &name)
		: logger_(logger), phase_(phase), name_(name) {
		logger_.enterScope(phase_, name_);
	}
	~LogScope() { logger_.exitScope(phase_, name_); }

	// Non-copyable
	LogScope(const LogScope &) = delete;
	LogScope &operator=(const LogScope &) = delete;

  private:
	Logger &logger_;
	LogPhase phase_;
	std::string name_;
};

/// Macro for convenient scope logging
#define LOG_SCOPE(logger, phase, name) LogScope _logScope##__LINE__(logger, phase, name)
