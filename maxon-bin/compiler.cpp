#include "compiler.h"

#include "codegen_mir.h"
#include "error_formatter.h"
#include "lexer.h"
#include "logger.h"
#include "semantic_analyzer.h"

// SIMD-optimized lexer (optional, enabled with MAXON_SIMD_ENABLED)
#ifdef MAXON_SIMD_ENABLED
#include "simd/simd.h"
#endif

#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <vector>

std::unique_ptr<ProgramAST> parseFile(const std::string &filePath, Logger &logger) {
	try {
		std::string source = readFile(filePath);

		auto lexStart = logger.startTimer();

#ifdef MAXON_SIMD_ENABLED
		// Use SIMD lexer for larger files, scalar for smaller files
		std::vector<Token> tokens;
		if (simd::should_use_simd(source.size())) {
			simd::SIMDLexer lexer(source);
			tokens = lexer.tokenize();
			logger.trace(LogPhase::Lexer, "Using SIMD lexer (", simd::get_simd_capability(), ")");
		} else {
			Lexer lexer(source);
			tokens = lexer.tokenize();
			logger.trace(LogPhase::Lexer, "Using scalar lexer (small file)");
		}
#else
		Lexer lexer(source);
		std::vector<Token> tokens = lexer.tokenize();
#endif

		logger.logElapsed(LogPhase::Lexer, "Tokenization time", lexStart);

		logger.progress(LogPhase::Lexer, "Tokenized: ", tokens.size(), " tokens from ", normalizePathForDisplay(filePath));

		if (logger.isEnabled(2)) {
			// Count token types for detailed logging
			std::map<TokenType, int> tokenCounts;
			for (const auto &token : tokens) {
				tokenCounts[token.type]++;
			}
			std::ostringstream details;
			bool first = true;
			for (const auto &[type, count] : tokenCounts) {
				std::string typeName;
				switch (type) {
				case TokenType::KEYWORD:
					typeName = "keywords";
					break;
				case TokenType::IDENTIFIER:
					typeName = "identifiers";
					break;
				case TokenType::NUMBER:
					typeName = "numbers";
					break;
				case TokenType::FLOAT_LITERAL:
					typeName = "floats";
					break;
				case TokenType::STRING:
					typeName = "strings";
					break;
				case TokenType::CHARACTER:
					typeName = "chars";
					break;
				case TokenType::BLOCK_ID:
					typeName = "block_ids";
					break;
				default:
					typeName = "";
					break;
				}
				if (typeName.empty())
					continue;
				if (!first)
					details << ", ";
				details << count << " " << typeName;
				first = false;
			}
			logger.detail(LogPhase::Lexer, "Token breakdown: ", details.str());
		}

		// Trace level: log individual tokens
		if (logger.isEnabled(3)) {
			for (size_t i = 0; i < tokens.size() && i < 50; ++i) {
				const auto &tok = tokens[i];
				logger.trace(LogPhase::Lexer, "Token[", i, "]: type=", static_cast<int>(tok.type),
							 " value='", tok.value, "' line=", tok.line, " col=", tok.column);
			}
			if (tokens.size() > 50) {
				logger.trace(LogPhase::Lexer, "... and ", tokens.size() - 50, " more tokens");
			}
		}

		auto parseStart = logger.startTimer();
		Parser parser(tokens);
		std::string fileNamespace = deriveNamespace(filePath);
		parser.setDefaultNamespace(fileNamespace);
		std::unique_ptr<ProgramAST> program = parser.parse();
		logger.logElapsed(LogPhase::Parser, "Parse time", parseStart);

		int functionCount = program->functions.size();
		int structCount = program->structs.size();
		logger.progress(LogPhase::Parser, "Parsed: ", functionCount, " function(s), ", structCount, " struct(s)");

		if (logger.isEnabled(2) && !fileNamespace.empty()) {
			logger.detail(LogPhase::Parser, "Namespace: ", fileNamespace);
		}

		// Trace level: log function names
		if (logger.isEnabled(3)) {
			for (const auto &func : program->functions) {
				logger.trace(LogPhase::Parser, "Function: ", func->name, " -> ", func->returnType,
							 " (", func->parameters.size(), " params)");
			}
			for (const auto &st : program->structs) {
				logger.trace(LogPhase::Parser, "Struct: ", st->name, " (", st->fields.size(), " fields)");
			}
		}

		return program;
	} catch (const std::runtime_error &e) {
		// Re-throw with file context if not already in message
		std::string errorMsg = e.what();
		if (errorMsg.find("File:") == std::string::npos) {
			throw std::runtime_error("In file '" + normalizePathForDisplay(filePath) + "':\n" + errorMsg);
		}
		throw;
	}
}

std::string compileProgram(const CompilationOptions &options) {
	Logger logger(options.verboseLevel);
	[[maybe_unused]] auto totalStart = logger.startTimer();

	logger.progress(LogPhase::General, "=== Maxon Compiler ===");
	logger.detail(LogPhase::General, "Input files: ", options.inputFiles.size());
	logger.detail(LogPhase::General, "Optimize: ", options.optimize ? "yes" : "no");
	logger.detail(LogPhase::General, "Debug info: ", options.debugInfo ? "yes" : "no");

	// Generate output filename early to determine what files to clean up
	std::string baseFilename;
	if (!options.outputFile.empty()) {
		// Use provided outputFile and derive base from it
		size_t lastDot = options.outputFile.find_last_of('.');
		if (lastDot != std::string::npos) {
			baseFilename = options.outputFile.substr(0, lastDot);
		} else {
			baseFilename = options.outputFile;
		}
	} else {
		// Derive from first input file, preserving the directory path
		size_t lastDot = options.inputFiles[0].find_last_of('.');
		if (lastDot != std::string::npos) {
			baseFilename = options.inputFiles[0].substr(0, lastDot);
		} else {
			baseFilename = options.inputFiles[0];
		}
	}

	// Delete existing IR and executable files before compilation starts
	std::string irFile = baseFilename + ".ir";
	std::string exeFile = baseFilename + ".exe";

	if (std::filesystem::exists(irFile)) {
		std::filesystem::remove(irFile);
		logger.trace(LogPhase::General, "Removed existing IR file: ", irFile);
	}
	if (std::filesystem::exists(exeFile)) {
		std::filesystem::remove(exeFile);
		logger.trace(LogPhase::General, "Removed existing exe file: ", exeFile);
	}

	std::vector<std::unique_ptr<ProgramAST>> programs;
	std::vector<std::string> sources;
	std::vector<std::string> allFiles = options.inputFiles;
	std::set<std::string> processedFiles(options.inputFiles.begin(), options.inputFiles.end());

	for (const auto &inputFile : options.inputFiles) {
		sources.push_back(readFile(inputFile));
		programs.push_back(parseFile(inputFile, logger));
	}

	int iteration = 0;
	const int maxIterations = 10;
	bool discoveredNewFiles = false;

	do {
		iteration++;
		logger.trace(LogPhase::Semantic, "Dependency resolution iteration ", iteration);
		discoveredNewFiles = false;

		std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
		for (auto &prog : programs) {
			for (auto &func : prog->functions) {
				mergedProgram->functions.push_back(std::move(func));
			}
			for (auto &structDef : prog->structs) {
				mergedProgram->structs.push_back(std::move(structDef));
			}
		}

		programs.clear();

		SemanticAnalyzer analyzer;

		// Register runtime functions so stdlib can use them
		analyzer.registerExternalFunction("write_stdout", "int",
										  {FunctionParameter("buf", "[]char", 0, 0), FunctionParameter("count", "int", 0, 0)});

		// Register string methods (these are runtime intrinsics)
		analyzer.registerExternalFunction("starts_with", "bool",
										  {FunctionParameter("str", "string", 0, 0), FunctionParameter("prefix", "string", 0, 0)});
		analyzer.registerExternalFunction("ends_with", "bool",
										  {FunctionParameter("str", "string", 0, 0), FunctionParameter("suffix", "string", 0, 0)});
		analyzer.registerExternalFunction("contains", "bool",
										  {FunctionParameter("haystack", "string", 0, 0), FunctionParameter("needle", "string", 0, 0)});
		analyzer.registerExternalFunction("find", "int",
										  {FunctionParameter("haystack", "string", 0, 0), FunctionParameter("needle", "string", 0, 0)});
		analyzer.registerExternalFunction("to_upper", "string",
										  {FunctionParameter("str", "string", 0, 0)});
		analyzer.registerExternalFunction("to_lower", "string",
										  {FunctionParameter("str", "string", 0, 0)});
		analyzer.registerExternalFunction("trim", "string",
										  {FunctionParameter("str", "string", 0, 0)});

		auto semanticStart = logger.startTimer();
		std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());
		logger.logElapsed(LogPhase::Semantic, "Analysis time", semanticStart);

		std::set<std::string> undefinedFunctions = analyzer.getUndefinedFunctions();

		if (!undefinedFunctions.empty()) {
			logger.trace(LogPhase::Semantic, "Undefined functions: ", undefinedFunctions.size());
			std::vector<std::string> stdlibFiles = findStdlibFilesDefining(undefinedFunctions);

			if (!stdlibFiles.empty()) {
				logger.progress(LogPhase::Semantic, "Auto-importing ", stdlibFiles.size(), " stdlib file(s)");

				for (const auto &file : stdlibFiles) {
					if (processedFiles.find(file) == processedFiles.end()) {
						logger.detail(LogPhase::Semantic, "Adding stdlib: ", normalizePathForDisplay(file));
						processedFiles.insert(file);
						allFiles.push_back(file);
						discoveredNewFiles = true;
						std::string source = readFile(file);
						sources.push_back(source);
					}
				}

				if (discoveredNewFiles) {
					programs.clear();
					for (const auto &file : allFiles) {
						// Use a silent logger for re-parsing to avoid duplicate output
						Logger silentLogger(0);
						programs.push_back(parseFile(file, silentLogger));
					}
				}

				continue;
			}
		}

		if (!semanticErrors.empty()) {
			for (const auto &error : semanticErrors) {
				std::string formattedError = ErrorFormatter::formatError(
					error.message,
					sources[0],
					error.line,
					error.column,
					"Semantic Error");
				std::cerr << formattedError << std::endl;
			}
			throw std::runtime_error("");
		}

		programs.clear();
		for (const auto &file : allFiles) {
			// Use a silent logger for re-parsing to avoid duplicate output
			Logger silentLogger(0);
			programs.push_back(parseFile(file, silentLogger));
		}
		break;

	} while (discoveredNewFiles && iteration < maxIterations);

	std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
	for (auto &prog : programs) {
		for (auto &func : prog->functions) {
			mergedProgram->functions.push_back(std::move(func));
		}
		for (auto &structDef : prog->structs) {
			mergedProgram->structs.push_back(std::move(structDef));
		}
	}

	std::string moduleName = options.inputFiles.size() == 1 ? options.inputFiles[0] : "merged";
	// For temp files (in temp/ directory), use just the filename without path to keep IR clean
	if (moduleName.find("temp") != std::string::npos) {
		std::filesystem::path p(moduleName);
		moduleName = p.filename().string();
	}

	logger.progress(LogPhase::Semantic, "Semantic analysis complete");
	logger.detail(LogPhase::Semantic, "Final AST: ", mergedProgram->functions.size(), " function(s), ",
				  mergedProgram->structs.size(), " struct(s)");

	// Trace level: list all functions in final AST
	if (logger.isEnabled(3)) {
		for (const auto &func : mergedProgram->functions) {
			logger.trace(LogPhase::Semantic, "Final function: ", func->name);
		}
	}

	logger.progress(LogPhase::MIR, "Generating MIR...");

	MIRCodeGenerator codegen(moduleName, options.debugInfo, options.verboseLevel);

	auto codegenStart = logger.startTimer();
	codegen.generate(mergedProgram.get(), !options.compileOnly);
	logger.logElapsed(LogPhase::MIR, "MIR generation time", codegenStart);

	auto dcStart = logger.startTimer();
	// Always run dead code elimination to remove unused internal functions
	codegen.runDeadCodeElimination();
	logger.logElapsed(LogPhase::Opt, "Dead code elimination time", dcStart);
	logger.progress(LogPhase::Opt, "Dead code elimination complete");

	if (options.optimize) {
		logger.progress(LogPhase::Opt, "Running optimization passes...");

		auto optStart = logger.startTimer();
		codegen.optimize();
		logger.logElapsed(LogPhase::Opt, "Optimization time", optStart);
	}

	std::string exeOutputFile = baseFilename + ".exe";

	if (options.emitIR) {
		std::string llOutputFile = baseFilename + ".ir";
		codegen.writeIRToFile(llOutputFile);
		logger.detail(LogPhase::MIR, "MIR written to: ", llOutputFile);
	}

	std::string outputFile;
	if (options.compileOnly) {
		std::string objOutputFile = baseFilename + ".obj";
		logger.progress(LogPhase::x86, "Generating object file...");
		codegen.writeObjectFile(objOutputFile);
		outputFile = objOutputFile;
		logger.detail(LogPhase::x86, "Object file: ", objOutputFile);
	} else {
		logger.progress(LogPhase::x86, "Generating executable...");
		auto exeStart = logger.startTimer();
		codegen.writeExecutable(exeOutputFile);
		logger.logElapsed(LogPhase::PE, "Executable generation time", exeStart);
		outputFile = exeOutputFile;
	}

	// Print output info
	logger.progress(LogPhase::General, "Output: ", outputFile);
	// Get file size using standard library
	std::ifstream file(outputFile, std::ios::binary | std::ios::ate);
	if (file) {
		uint64_t outputFileSize = file.tellg();
		logger.detail(LogPhase::General, "Size: ", outputFileSize, " bytes");
	}

	logger.logTotalElapsed("Total compilation time");

	return exeOutputFile;
}
