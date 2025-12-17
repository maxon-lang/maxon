#include "compiler.h"

#include "ast.h"
#include "call_graph.h"
#include "codegen_ng.h"
#include "compiler_stats.h"
#include "error_formatter.h"
#include "lexer.h"
#include "logger.h"
#include "semantic_analyzer.h"
#include "token_stream.h"

#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <vector>

// Forward declarations for AST counting
static void countAstExpr(const ExprAST *expr, size_t &exprCount);
static void countAstStmt(const StmtAST *stmt, size_t &exprCount, size_t &stmtCount);

// Count AST nodes in an expression
static void countAstExpr(const ExprAST *expr, size_t &exprCount) {
	if (!expr)
		return;
	exprCount++;

	if (auto *binary = dynamic_cast<const BinaryExprAST *>(expr)) {
		countAstExpr(binary->left.get(), exprCount);
		countAstExpr(binary->right.get(), exprCount);
	} else if (auto *unary = dynamic_cast<const UnaryExprAST *>(expr)) {
		countAstExpr(unary->operand.get(), exprCount);
	} else if (auto *call = dynamic_cast<const CallExprAST *>(expr)) {
		for (const auto &arg : call->args) {
			countAstExpr(arg.value.get(), exprCount);
		}
	} else if (auto *arrIdx = dynamic_cast<const ArrayIndexExprAST *>(expr)) {
		countAstExpr(arrIdx->arrayExpr.get(), exprCount);
		countAstExpr(arrIdx->index.get(), exprCount);
	} else if (auto *slice = dynamic_cast<const SliceExprAST *>(expr)) {
		countAstExpr(slice->start.get(), exprCount);
		countAstExpr(slice->end.get(), exprCount);
	} else if (auto *arrLit = dynamic_cast<const ArrayLiteralExprAST *>(expr)) {
		for (const auto &val : arrLit->values) {
			countAstExpr(val.get(), exprCount);
		}
	} else if (auto *member = dynamic_cast<const MemberAccessExprAST *>(expr)) {
		countAstExpr(member->object.get(), exprCount);
	} else if (auto *cast = dynamic_cast<const CastExprAST *>(expr)) {
		countAstExpr(cast->expr.get(), exprCount);
	} else if (auto *structInit = dynamic_cast<const StructInitExprAST *>(expr)) {
		for (const auto &field : structInit->fields) {
			countAstExpr(field.value.get(), exprCount);
		}
	}
}

// Count AST nodes in a statement
static void countAstStmt(const StmtAST *stmt, size_t &exprCount, size_t &stmtCount) {
	if (!stmt)
		return;
	stmtCount++;

	if (auto *varDecl = dynamic_cast<const VarDeclStmtAST *>(stmt)) {
		countAstExpr(varDecl->initializer.get(), exprCount);
	} else if (auto *letDecl = dynamic_cast<const LetDeclStmtAST *>(stmt)) {
		countAstExpr(letDecl->initializer.get(), exprCount);
	} else if (auto *assign = dynamic_cast<const AssignStmtAST *>(stmt)) {
		countAstExpr(assign->value.get(), exprCount);
	} else if (auto *arrAssign = dynamic_cast<const ArrayAssignStmtAST *>(stmt)) {
		countAstExpr(arrAssign->index.get(), exprCount);
		countAstExpr(arrAssign->value.get(), exprCount);
	} else if (auto *arrMemAssign = dynamic_cast<const ArrayMemberAssignStmtAST *>(stmt)) {
		countAstExpr(arrMemAssign->index.get(), exprCount);
		countAstExpr(arrMemAssign->value.get(), exprCount);
	} else if (auto *memAssign = dynamic_cast<const MemberAssignStmtAST *>(stmt)) {
		countAstExpr(memAssign->value.get(), exprCount);
	} else if (auto *memArrAssign = dynamic_cast<const MemberArrayAssignStmtAST *>(stmt)) {
		countAstExpr(memArrAssign->index.get(), exprCount);
		countAstExpr(memArrAssign->value.get(), exprCount);
	} else if (auto *ifStmt = dynamic_cast<const IfStmtAST *>(stmt)) {
		countAstExpr(ifStmt->condition.get(), exprCount);
		for (const auto &s : ifStmt->thenBody) {
			countAstStmt(s.get(), exprCount, stmtCount);
		}
		for (const auto &s : ifStmt->elseBody) {
			countAstStmt(s.get(), exprCount, stmtCount);
		}
	} else if (auto *whileStmt = dynamic_cast<const WhileStmtAST *>(stmt)) {
		countAstExpr(whileStmt->condition.get(), exprCount);
		for (const auto &s : whileStmt->body) {
			countAstStmt(s.get(), exprCount, stmtCount);
		}
	} else if (auto *forStmt = dynamic_cast<const ForStmtAST *>(stmt)) {
		countAstExpr(forStmt->iterable.get(), exprCount);
		for (const auto &s : forStmt->body) {
			countAstStmt(s.get(), exprCount, stmtCount);
		}
	} else if (auto *retStmt = dynamic_cast<const ReturnStmtAST *>(stmt)) {
		countAstExpr(retStmt->value.get(), exprCount);
	} else if (auto *exprStmt = dynamic_cast<const ExprStmtAST *>(stmt)) {
		countAstExpr(exprStmt->expression.get(), exprCount);
	}
}

// Count all AST nodes in a program
static void countAstNodes(const ProgramAST *program, size_t &exprCount, size_t &stmtCount) {
	exprCount = 0;
	stmtCount = 0;

	for (const auto &func : program->functions) {
		for (const auto &stmt : func->body) {
			countAstStmt(stmt.get(), exprCount, stmtCount);
		}
	}
}

std::unique_ptr<ProgramAST> parseFile(const std::string &filePath, Logger &logger, CompilerStats *stats) {
	try {
		std::string source = readFile(filePath);

		// Track per-file timings
		auto fileStart = std::chrono::high_resolution_clock::now();

		if (stats)
			stats->startPhase("Lexer");
		auto lexStart = logger.startTimer();

		// Use lexer which returns TokenStream directly
		Lexer lexer(source);
		TokenStream stream = lexer.tokenize_stream();
		logger.trace(LogPhase::Lexer, "Using SIMD lexer (", get_lexer_capability(), ")");

		logger.logElapsed(LogPhase::Lexer, "Tokenization time", lexStart);

		auto lexEnd = std::chrono::high_resolution_clock::now();
		auto fileLexTime = std::chrono::duration_cast<std::chrono::microseconds>(lexEnd - fileStart);

		if (stats) {
			stats->endPhase("Lexer");
			stats->setTokenCount(stats->getTokenCount() + stream.size());
		}

		// Save token count before stream is moved
		size_t fileTokenCount = stream.size();

		logger.progress(LogPhase::Lexer, "Tokenized: ", stream.size(), " tokens from ", normalizePathForDisplay(filePath));

		if (logger.isEnabled(2)) {
			// Count token types for detailed logging
			std::map<TokenType, int> tokenCounts;
			for (size_t i = 0; i < stream.size(); ++i) {
				tokenCounts[stream[i].get_type()]++;
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
			for (size_t i = 0; i < stream.size() && i < 50; ++i) {
				const auto &ct = stream[i];
				logger.trace(LogPhase::Lexer, "Token[", i, "]: type=", static_cast<int>(ct.get_type()),
							 " value='", stream.get_value(i), "' line=", ct.get_line(), " col=", ct.get_column());
			}
			if (stream.size() > 50) {
				logger.trace(LogPhase::Lexer, "... and ", stream.size() - 50, " more tokens");
			}
		}

		if (stats)
			stats->startPhase("Parser");
		auto parseStart = logger.startTimer();
		Parser parser(std::move(stream));
		std::string fileNamespace = deriveNamespace(filePath);
		parser.setDefaultNamespace(fileNamespace);
		std::unique_ptr<ProgramAST> program = parser.parse();
		logger.logElapsed(LogPhase::Parser, "Parse time", parseStart);

		// Set source file on all AST nodes for error reporting
		for (auto &func : program->functions) {
			func->sourceFile = filePath;
		}
		for (auto &structDef : program->structs) {
			structDef->sourceFile = filePath;
			for (auto &method : structDef->methods) {
				method->sourceFile = filePath;
			}
		}
		for (auto &interfaceDef : program->interfaces) {
			interfaceDef->sourceFile = filePath;
		}
		for (auto &enumDef : program->enums) {
			enumDef->sourceFile = filePath;
			for (auto &method : enumDef->methods) {
				method->sourceFile = filePath;
			}
		}
		for (auto &global : program->globals) {
			global->sourceFile = filePath;
		}

		auto parseEnd = std::chrono::high_resolution_clock::now();
		auto fileParseTime = std::chrono::duration_cast<std::chrono::microseconds>(parseEnd - lexEnd);

		if (stats) {
			stats->endPhase("Parser");
		}

		int functionCount = program->functions.size();
		int structCount = program->structs.size();
		logger.progress(LogPhase::Parser, "Parsed: ", functionCount, " function(s), ", structCount, " struct(s)");

		// Count AST nodes for stats
		size_t fileExprCount = 0, fileStmtCount = 0;
		if (stats) {
			countAstNodes(program.get(), fileExprCount, fileStmtCount);
			stats->setAstExpressionCount(stats->getAstExpressionCount() + fileExprCount);
			stats->setAstStatementCount(stats->getAstStatementCount() + fileStmtCount);
		}

		// Record per-file stats
		if (stats) {
			std::string displayName = normalizePathForDisplay(filePath);
			stats->recordFile(displayName, fileTokenCount, functionCount, structCount,
							  fileExprCount, fileStmtCount, fileLexTime, fileParseTime);
		}

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
	GlobalLogger::init(options.verboseLevel);
	[[maybe_unused]] auto totalStart = logger.startTimer();

	// Create stats collector if requested
	std::unique_ptr<CompilerStats> stats;
	if (options.showStats) {
		stats = std::make_unique<CompilerStats>();
	}

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
		if (options.showProgress) {
			logger.progress(LogPhase::General, "Compiling: ", normalizePathForDisplay(inputFile));
		}
		sources.push_back(readFile(inputFile));
		programs.push_back(parseFile(inputFile, logger, stats.get()));
	}

	// Check for parse errors before continuing to semantic analysis
	// Only report the first parse error to avoid cascading error noise
	for (size_t i = 0; i < programs.size(); i++) {
		if (programs[i]->hasParseErrors()) {
			const auto &error = programs[i]->parseErrors[0];
			// Format matches what parseFile throws: "In file '...': \n<error message>"
			logger.error(LogPhase::Parser, "In file '", normalizePathForDisplay(allFiles[i]), "':\n",
						 error.message);
			throw std::runtime_error("");
		}
	}

	int iteration = 0;
	const int maxIterations = 10;
	std::map<std::string, size_t> functionIndices;			// Store function indices from semantic analysis
	std::map<std::string, std::string> functionReturnTypes; // Store function return types from semantic analysis
	std::vector<FunctionInfo> synthesizedMethods;			// Store synthesized default methods (copied)
	bool discoveredNewFiles = false;
	std::unique_ptr<ProgramAST> mergedProgram; // Declare outside loop to preserve function IDs

	do {
		iteration++;
		logger.trace(LogPhase::Semantic, "Dependency resolution iteration ", iteration);
		discoveredNewFiles = false;

		mergedProgram = std::make_unique<ProgramAST>();
		for (auto &prog : programs) {
			for (auto &func : prog->functions) {
				mergedProgram->functions.push_back(std::move(func));
			}
			for (auto &structDef : prog->structs) {
				mergedProgram->structs.push_back(std::move(structDef));
			}
			for (auto &interfaceDef : prog->interfaces) {
				mergedProgram->interfaces.push_back(std::move(interfaceDef));
			}
			for (auto &enumDef : prog->enums) {
				mergedProgram->enums.push_back(std::move(enumDef));
			}
			for (auto &global : prog->globals) {
				mergedProgram->globals.push_back(std::move(global));
			}
		}

		programs.clear();

		SemanticAnalyzer analyzer;
		analyzer.setLogger(&logger);

		// Register all built-in functions (runtime functions, string methods, etc.)
		analyzer.registerBuiltinFunctions();

		if (stats)
			stats->startPhase("Semantic");
		auto semanticStart = logger.startTimer();
		std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());
		logger.logElapsed(LogPhase::Semantic, "Analysis time", semanticStart);
		if (stats)
			stats->endPhase("Semantic");

		// Store function indices for codegen optimization
		functionIndices = analyzer.getFunctionIndices();

		// Store function return types for codegen type inference
		functionReturnTypes.clear();
		synthesizedMethods.clear();
		for (const auto &[name, info] : analyzer.getFunctions()) {
			functionReturnTypes[name] = info.returnType;
			// Collect synthesized default methods
			if (info.needsCodeGeneration()) {
				synthesizedMethods.push_back(info);
			}
		}

		// Collect all undefined items and their corresponding stdlib files
		std::set<std::string> filesToImport;

		// Check for undefined functions
		std::set<std::string> undefinedFunctions = analyzer.getUndefinedFunctions();
		if (!undefinedFunctions.empty()) {
			logger.trace(LogPhase::Semantic, "Undefined functions: ", undefinedFunctions.size());
			std::vector<std::string> stdlibFiles = findStdlibFilesDefining(undefinedFunctions);
			for (const auto &file : stdlibFiles) {
				if (processedFiles.find(file) == processedFiles.end()) {
					filesToImport.insert(file);
				}
			}
		}

		// Check for undefined structs
		std::set<std::string> undefinedStructs = analyzer.getUndefinedStructs();
		if (!undefinedStructs.empty()) {
			logger.trace(LogPhase::Semantic, "Undefined structs: ", undefinedStructs.size());
			for (const auto &s : undefinedStructs) {
				logger.trace(LogPhase::Semantic, "  - ", s);
			}
			std::vector<std::string> stdlibFiles = findStdlibFilesDefiningStructs(undefinedStructs);
			for (const auto &file : stdlibFiles) {
				if (processedFiles.find(file) == processedFiles.end()) {
					filesToImport.insert(file);
				}
			}
		}

		// Check for undefined interfaces
		std::set<std::string> undefinedInterfaces = analyzer.getUndefinedInterfaces();
		if (!undefinedInterfaces.empty()) {
			logger.trace(LogPhase::Semantic, "Undefined interfaces: ", undefinedInterfaces.size());
			std::vector<std::string> stdlibFiles = findStdlibFilesDefiningInterfaces(undefinedInterfaces);
			for (const auto &file : stdlibFiles) {
				if (processedFiles.find(file) == processedFiles.end()) {
					filesToImport.insert(file);
				}
			}
		}

		// Import all collected files
		if (!filesToImport.empty()) {
			logger.progress(LogPhase::Semantic, "Auto-importing ", filesToImport.size(), " stdlib file(s)");

			for (const auto &file : filesToImport) {
				if (options.showProgress) {
					logger.progress(LogPhase::General, "Compiling: ", normalizePathForDisplay(file));
				}
				logger.progress(LogPhase::Semantic, "  -> ", normalizePathForDisplay(file));
				processedFiles.insert(file);
				allFiles.push_back(file);
				discoveredNewFiles = true;
				std::string source = readFile(file);
				sources.push_back(source);
			}

			programs.clear();
			for (const auto &file : allFiles) {
				// Use a silent logger for re-parsing to avoid duplicate output
				Logger silentLogger(0);
				programs.push_back(parseFile(file, silentLogger));
			}

			// Check for parse errors in newly parsed stdlib files
			for (size_t i = 0; i < programs.size(); i++) {
				if (programs[i]->hasParseErrors()) {
					const auto &error = programs[i]->parseErrors[0];
					logger.error(LogPhase::Parser, "In file '", normalizePathForDisplay(allFiles[i]), "':\n",
								 error.message);
					throw std::runtime_error("");
				}
			}

			continue;
		}

		if (!semanticErrors.empty()) {
			// Build a map from file path to source content for error formatting
			std::map<std::string, std::string> sourceMap;
			for (size_t i = 0; i < allFiles.size() && i < sources.size(); ++i) {
				sourceMap[allFiles[i]] = sources[i];
			}

			for (const auto &error : semanticErrors) {
				// Look up the source content for this error's file
				std::string sourceContent;
				std::string displayPath;
				if (!error.sourceFile.empty()) {
					auto it = sourceMap.find(error.sourceFile);
					if (it != sourceMap.end()) {
						sourceContent = it->second;
					}
					displayPath = normalizePathForDisplay(error.sourceFile);
				} else if (!sources.empty()) {
					// Fallback to first source if no file tracked
					sourceContent = sources[0];
				}

				std::string formattedError = ErrorFormatter::formatError(
					error.message,
					sourceContent,
					error.line,
					error.column,
					"Semantic Error",
					displayPath);
				logger.error(LogPhase::Semantic, formattedError);
			}
			throw std::runtime_error("");
		}

		// Semantic analysis succeeded - break out of the loop
		// Note: We don't re-parse here because it would lose the function IDs set during semantic analysis
		break;

	} while (discoveredNewFiles && iteration < maxIterations);

	// mergedProgram now contains the AST with function IDs set from semantic analysis

	// NOTE: AST-level DCE is disabled because generic code (e.g., map<string,int>) may call
	// interface methods (e.g., string.hash) that aren't visible to the call graph since
	// type parameters aren't bound at AST level. The MIR-level DCE pass handles this properly
	// after generic instantiation when all concrete method calls are known.

	std::string moduleName = options.inputFiles.size() == 1 ? options.inputFiles[0] : "merged";
	// For temp files (in temp/ directory), use just the filename without path to keep IR clean
	if (moduleName.find("temp") != std::string::npos) {
		std::filesystem::path p(moduleName);
		moduleName = p.filename().string();
	}

	logger.progress(LogPhase::Semantic, "Semantic analysis complete");
	logger.detail(LogPhase::Semantic, "Final AST: ", mergedProgram->functions.size(), " function(s), ",
				  mergedProgram->structs.size(), " struct(s)");

	// Record stats for final AST
	if (stats) {
		stats->setFunctionCount(mergedProgram->functions.size());
		stats->setStructCount(mergedProgram->structs.size());
	}

	// Trace level: list all functions in final AST
	if (logger.isEnabled(3)) {
		for (const auto &func : mergedProgram->functions) {
			logger.trace(LogPhase::Semantic, "Final function: ", func->name);
		}
	}

	logger.progress(LogPhase::MIR, "Generating MIR...");

	MIRCodeGenerator codegen(moduleName, options.debugInfo, options.verboseLevel, options.trackAllocs);
	codegen.setSynthesizedMethods(synthesizedMethods);

	if (stats)
		stats->startPhase("MIR Generation");
	auto codegenStart = logger.startTimer();
	codegen.generate(mergedProgram.get(), !options.compileOnly, &functionIndices, &functionReturnTypes);
	logger.logElapsed(LogPhase::MIR, "MIR generation time", codegenStart);
	if (stats) {
		stats->endPhase("MIR Generation");
		stats->setMirInstructionsBefore(codegen.getInstructionCount());
	}

	if (stats)
		stats->startPhase("Dead Code Elimination");
	auto dcStart = logger.startTimer();
	// Always run dead code elimination to remove unused internal functions
	codegen.runDeadCodeElimination();
	logger.logElapsed(LogPhase::Opt, "Dead code elimination time", dcStart);
	logger.progress(LogPhase::Opt, "Dead code elimination complete");
	if (stats)
		stats->endPhase("Dead Code Elimination");

	if (options.optimize) {
		logger.progress(LogPhase::Opt, "Running optimization passes...");

		if (stats)
			stats->startPhase("Optimization");
		auto optStart = logger.startTimer();
		codegen.optimize(stats.get(), false);
		logger.logElapsed(LogPhase::Opt, "Optimization time", optStart);
		if (stats) {
			stats->endPhase("Optimization");
			stats->setMirInstructionsAfter(codegen.getInstructionCount());
		}
	} else if (stats) {
		// If no optimization, after == before
		stats->setMirInstructionsAfter(codegen.getInstructionCount());
	}

	std::string exeOutputFile = baseFilename + ".exe";

	if (options.emitIR) {
		std::string llOutputFile = baseFilename + ".ir";
		codegen.writeIRToFile(llOutputFile);
		logger.detail(LogPhase::MIR, "MIR written to: ", llOutputFile);
	}

	if (options.emitAsm) {
		std::string asmOutputFile = baseFilename + ".asm";
		codegen.writeAsmToFile(asmOutputFile);
		logger.detail(LogPhase::x86, "Assembly written to: ", asmOutputFile);
	}

	std::string outputFile;
	if (options.compileOnly) {
		std::string objOutputFile = baseFilename + ".obj";
		logger.progress(LogPhase::x86, "Generating object file...");
		if (stats)
			stats->startPhase("x86 CodeGen");
		codegen.writeObjectFile(objOutputFile);
		if (stats)
			stats->endPhase("x86 CodeGen");
		outputFile = objOutputFile;
		logger.detail(LogPhase::x86, "Object file: ", objOutputFile);
	} else {
		logger.progress(LogPhase::x86, "Generating executable...");
		if (stats)
			stats->startPhase("x86 CodeGen");
		auto exeStart = logger.startTimer();
		codegen.writeExecutable(exeOutputFile);
		logger.logElapsed(LogPhase::PE, "Executable generation time", exeStart);
		if (stats)
			stats->endPhase("x86 CodeGen");
		outputFile = exeOutputFile;
	}

	// Print output info
	if (options.showProgress) {
		logger.progress(LogPhase::General, "Output: ", outputFile);
	}
	// Get file size using standard library
	std::ifstream file(outputFile, std::ios::binary | std::ios::ate);
	uint64_t outputFileSize = 0;
	if (file) {
		outputFileSize = file.tellg();
		logger.detail(LogPhase::General, "Size: ", outputFileSize, " bytes");
	}

	logger.logTotalElapsed("Total compilation time");

	// Print stats if requested
	if (stats) {
		stats->setExecutableSize(outputFileSize);
		stats->print();
	}

	return exeOutputFile;
}
