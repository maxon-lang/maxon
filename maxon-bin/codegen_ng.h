#ifndef CODEGEN_NG_H
#define CODEGEN_NG_H

#include "ast.h"
#include "mir/mir.h"
#include "mir/mir_builder.h"
#include "semantic_analyzer.h"
#include <map>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

class CompilerStats;

/**
 * MIR Code Generator (Next Generation)
 *
 * Minimal implementation that generates MIR from the AST.
 * Currently supports only the features needed for examples/basic.maxon.
 */
class MIRCodeGenerator {
  private:
	std::unique_ptr<mir::MIRModule> module;
	std::unique_ptr<mir::MIRBuilder> builder;

	int verboseLevel;

	// Variable tracking: name -> alloca value
	std::map<std::string, mir::MIRValue *> namedValues;

	// Code generation methods
	mir::MIRValue *generateExpr(ExprAST *expr);
	void generateStmt(StmtAST *stmt, mir::MIRFunction *function);
	void declareFunction(FunctionAST *func);
	void generateFunction(FunctionAST *func);
	void createEntryPoint();


	// Logging helpers
	void logProgress(const std::string &msg);
	void logDetail(const std::string &msg);

	// Output helpers (platform-specific)
#ifdef _WIN32
	void writeWindowsExecutable(
		const std::string &exeFile,
		std::vector<uint8_t> &code,
		const std::vector<uint8_t> &data,
		const std::unordered_map<std::string, size_t> &funcOffsets,
		const std::vector<std::pair<size_t, std::string>> &importRelocs,
		const std::vector<std::pair<size_t, size_t>> &dataRelocs);
#else
	void writeLinuxExecutable(
		const std::string &exeFile,
		const std::vector<uint8_t> &code,
		const std::vector<uint8_t> &data,
		const std::unordered_map<std::string, size_t> &funcOffsets);
#endif

  public:
	MIRCodeGenerator(const std::string &moduleName, bool debugInfo = false,
					 int verboseLevel = 0, bool trackAllocs = false);
	~MIRCodeGenerator();

	void generate(ProgramAST *program, bool needsEntryPoint = true,
				  const std::map<std::string, size_t> *functionIndices = nullptr,
				  const std::map<std::string, std::string> *functionReturnTypes = nullptr);

	void setSynthesizedMethods(const std::vector<FunctionInfo> &) {} // stub
	void optimize(CompilerStats *stats = nullptr);
	void optimizeForExplorer();
	void runDeadCodeElimination();

	void printIR();
	void writeExecutable(const std::string &exeFile);
	void writeIRToFile(const std::string &filename);
	void writeAsmToFile(const std::string &filename);
	void writeObjectFile(const std::string &filename);

	size_t getInstructionCount() const;
	mir::MIRModule *getModule() { return module.get(); }

	// Accessors for AST generate() methods
	mir::MIRBuilder *getBuilder() { return builder.get(); }
	mir::MIRValue *lookupVariable(const std::string &name);
	void trackVariable(const std::string &name, mir::MIRValue *value);

	// Variable/parameter generation helper
	void generateLocalVariable(const std::string &name, const std::string &typeStr,
							   ExprAST *initializer, mir::MIRValue *existingValue);
};

#endif // CODEGEN_NG_H
