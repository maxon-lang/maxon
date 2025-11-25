#include "compiler.h"

#include "codegen.h"
#include "error_formatter.h"
#include "lexer.h"
#include "semantic_analyzer.h"

#include <chrono>
#include <filesystem>
#include <iostream>
#include <map>
#include <set>
#include <vector>

std::unique_ptr<ProgramAST> parseFile(const std::string &filePath, int verboseLevel)
{
	try
	{
		std::string source = readFile(filePath);

		Lexer lexer(source);
		std::vector<Token> tokens = lexer.tokenize();

		if (verboseLevel >= 1)
		{
			std::cout << "Lexical analysis: " << tokens.size() << " tokens";
			if (verboseLevel >= 2)
			{
				// Count token types
				std::map<TokenType, int> tokenCounts;
				for (const auto &token : tokens)
				{
					tokenCounts[token.type]++;
				}
				std::cout << " (";
				bool first = true;
				for (const auto &[type, count] : tokenCounts)
				{
					std::string typeName;
					switch (type)
					{
					case TokenType::KEYWORD:
						typeName = "keyword";
						break;
					case TokenType::IDENTIFIER:
						typeName = "identifier";
						break;
					case TokenType::NUMBER:
						typeName = "number";
						break;
					case TokenType::FLOAT_LITERAL:
						typeName = "float";
						break;
					case TokenType::STRING:
						typeName = "string";
						break;
					case TokenType::CHARACTER:
						typeName = "char";
						break;
					case TokenType::BLOCK_ID:
						typeName = "block_id";
						break;
					default:
						typeName = "";
						break;
					}
					if (typeName.empty())
						continue;
					if (!first)
						std::cout << ", ";
					std::cout << count << " " << typeName;
					first = false;
				}
				std::cout << ")";
			}
			std::cout << std::endl;
		}

		Parser parser(tokens);
		std::string fileNamespace = deriveNamespace(filePath);
		parser.setDefaultNamespace(fileNamespace);
		std::unique_ptr<ProgramAST> program = parser.parse();

		if (verboseLevel >= 1)
		{
			int functionCount = program->functions.size();
			int structCount = program->structs.size();
			std::cout << "Parsing: " << functionCount << " function(s), " << structCount << " struct(s)";
			if (verboseLevel >= 2 && !fileNamespace.empty())
			{
				std::cout << " (namespace: " << fileNamespace << ")";
			}
			std::cout << std::endl;
		}

		return program;
	}
	catch (const std::runtime_error &e)
	{
		// Re-throw with file context if not already in message
		std::string errorMsg = e.what();
		if (errorMsg.find("File:") == std::string::npos)
		{
			throw std::runtime_error("In file '" + filePath + "':\n" + errorMsg);
		}
		throw;
	}
}

std::string compileProgram(const CompilationOptions &options, llvm::raw_ostream *errorStream)
{
	std::vector<std::unique_ptr<ProgramAST>> programs;
	std::vector<std::string> sources;
	std::vector<std::string> allFiles = options.inputFiles;
	std::set<std::string> processedFiles(options.inputFiles.begin(), options.inputFiles.end());

	for (const auto &inputFile : options.inputFiles)
	{
		sources.push_back(readFile(inputFile));
		programs.push_back(parseFile(inputFile, options.verboseLevel));
	}

	int iteration = 0;
	const int maxIterations = 10;
	bool discoveredNewFiles = false;

	do
	{
		iteration++;
		discoveredNewFiles = false;

		std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
		for (auto &prog : programs)
		{
			for (auto &func : prog->functions)
			{
				mergedProgram->functions.push_back(std::move(func));
			}
			for (auto &structDef : prog->structs)
			{
				mergedProgram->structs.push_back(std::move(structDef));
			}
		}

		programs.clear();

		SemanticAnalyzer analyzer;

		// Register runtime functions so stdlib can use them
		analyzer.registerExternalFunction("write_stdout", "int",
										  {FunctionParameter("buf", "ptr", 0, 0), FunctionParameter("count", "int", 0, 0)});

		std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());

		std::set<std::string> undefinedFunctions = analyzer.getUndefinedFunctions();

		if (!undefinedFunctions.empty())
		{
			std::vector<std::string> stdlibFiles = findStdlibFilesDefining(undefinedFunctions);

			if (!stdlibFiles.empty())
			{
				if (options.verboseLevel >= 1)
				{
					std::cout << "Auto-compiling " << stdlibFiles.size() << " stdlib file(s)" << std::endl;
				}

				for (const auto &file : stdlibFiles)
				{
					if (processedFiles.find(file) == processedFiles.end())
					{
						processedFiles.insert(file);
						allFiles.push_back(file);
						discoveredNewFiles = true;
						std::string source = readFile(file);
						sources.push_back(source);
					}
				}

				if (discoveredNewFiles)
				{
					programs.clear();
					for (const auto &file : allFiles)
					{
						programs.push_back(parseFile(file, 0));
					}
				}

				continue;
			}
		}

		if (!semanticErrors.empty())
		{
			for (const auto &error : semanticErrors)
			{
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
		for (const auto &file : allFiles)
		{
			programs.push_back(parseFile(file, 0));
		}
		break;

	} while (discoveredNewFiles && iteration < maxIterations);

	std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
	for (auto &prog : programs)
	{
		for (auto &func : prog->functions)
		{
			mergedProgram->functions.push_back(std::move(func));
		}
		for (auto &structDef : prog->structs)
		{
			mergedProgram->structs.push_back(std::move(structDef));
		}
	}

	std::string moduleName = options.inputFiles.size() == 1 ? options.inputFiles[0] : "merged";
	// For temp files (in temp/ directory), use just the filename without path to keep IR clean
	if (moduleName.find("temp") != std::string::npos)
	{
		std::filesystem::path p(moduleName);
		moduleName = p.filename().string();
	}

	if (options.verboseLevel >= 1)
	{
		std::cout << "Semantic analysis" << std::endl;
	}

	if (options.verboseLevel >= 2)
	{
		std::cout << "  Final AST: " << mergedProgram->functions.size() << " function(s), "
				  << mergedProgram->structs.size() << " struct(s)" << std::endl;
	}

	if (options.verboseLevel >= 1)
	{
		std::cout << "Code generation" << std::endl;
	}

	CodeGenerator codegen(moduleName, options.debugInfo, options.verboseLevel, options.profile);

	auto codgenStartTime = std::chrono::high_resolution_clock::now();
	codegen.generate(mergedProgram.get(), !options.compileOnly);
	auto codgenEndTime = std::chrono::high_resolution_clock::now();

	if (options.verboseLevel >= 2)
	{
		auto codgenDuration = std::chrono::duration_cast<std::chrono::milliseconds>(codgenEndTime - codgenStartTime);
		std::cout << "  Time: " << codgenDuration.count() << "ms" << std::endl;
	}

	auto dcStartTime = std::chrono::high_resolution_clock::now();
	// Always run dead code elimination to remove unused internal functions
	codegen.runDeadCodeElimination();
	auto dcEndTime = std::chrono::high_resolution_clock::now();

	if (options.verboseLevel >= 1)
	{
		std::cout << "Dead code elimination" << std::endl;
	}

	if (options.verboseLevel >= 2)
	{
		auto dcDuration = std::chrono::duration_cast<std::chrono::milliseconds>(dcEndTime - dcStartTime);
		std::cout << "  Time: " << dcDuration.count() << "ms" << std::endl;
	}

	if (options.optimize)
	{
		if (options.verboseLevel >= 1)
		{
			std::cout << "Optimization" << std::endl;
		}

		auto optStartTime = std::chrono::high_resolution_clock::now();
		codegen.optimize();
		auto optEndTime = std::chrono::high_resolution_clock::now();

		if (options.verboseLevel >= 2)
		{
			auto optDuration = std::chrono::duration_cast<std::chrono::milliseconds>(optEndTime - optStartTime);
			std::cout << "  Time: " << optDuration.count() << "ms" << std::endl;
		}
	}

	// Generate output filename: use outputFile if set, otherwise derive from first input file
	std::string baseFilename;
	if (!options.outputFile.empty())
	{
		// Use provided outputFile and derive base from it
		size_t lastDot = options.outputFile.find_last_of('.');
		if (lastDot != std::string::npos)
		{
			baseFilename = options.outputFile.substr(0, lastDot);
		}
		else
		{
			baseFilename = options.outputFile;
		}
	}
	else
	{
		// Derive from first input file, preserving the directory path
		size_t lastDot = options.inputFiles[0].find_last_of('.');
		if (lastDot != std::string::npos)
		{
			baseFilename = options.inputFiles[0].substr(0, lastDot);
		}
		else
		{
			baseFilename = options.inputFiles[0];
		}
	}

	std::string exeOutputFile = baseFilename + ".exe";

	if (options.emitLLVM)
	{
		std::string llOutputFile = baseFilename + ".ll";
		codegen.writeIRToFile(llOutputFile);
		if (options.verboseLevel >= 2)
		{
			std::cout << "  LLVM IR written to: " << llOutputFile << std::endl;
		}
	}

	std::string outputFile;
	if (options.compileOnly)
	{
		std::string objOutputFile = baseFilename + ".obj";
		codegen.writeObjectFile(objOutputFile);
		outputFile = objOutputFile;
		if (options.verboseLevel >= 2)
		{
			std::cout << "  Object file: " << objOutputFile << std::endl;
		}
	}
	else
	{
		codegen.writeExecutable(exeOutputFile, errorStream);
		outputFile = exeOutputFile;
	}

	// Print output info
	if (options.verboseLevel >= 1)
	{
		std::cout << "\nOutput: " << outputFile;
		uint64_t outputFileSize = 0;
		llvm::sys::fs::file_size(outputFile, outputFileSize);
		std::cout << " (" << outputFileSize << " bytes)" << std::endl;
	}

	return exeOutputFile;
}
