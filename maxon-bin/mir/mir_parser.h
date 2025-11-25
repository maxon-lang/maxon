#pragma once

#include "mir.h"
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace mir {

//==============================================================================
// MIR Text Format Parser
//==============================================================================
// Parses textual MIR format into MIRModule data structures.
// The format is similar to LLVM IR but simplified for MIR's instruction set.
//
// Example:
//   ; Comment
//   define f64 @floor(f64 %x) {
//   entry:
//     %truncated = fptosi f64 %x to i64
//     %result = sitofp i64 %truncated to f64
//     ret f64 %result
//   }
//
//   declare ptr @HeapAlloc(ptr, i32, i64)
//==============================================================================

//------------------------------------------------------------------------------
// Parse Errors
//------------------------------------------------------------------------------

struct MIRParseError {
	int line;
	int column;
	std::string message;

	std::string toString() const;
};

//------------------------------------------------------------------------------
// Parser Result
//------------------------------------------------------------------------------

struct MIRParseResult {
	std::unique_ptr<MIRModule> module;
	std::vector<MIRParseError> errors;

	bool success() const { return errors.empty() && module != nullptr; }
};

//------------------------------------------------------------------------------
// MIR Parser
//------------------------------------------------------------------------------

class MIRParser {
  public:
	// Parse MIR from a string
	static MIRParseResult parse(const std::string &source,
								const std::string &moduleName = "module");

	// Parse MIR from a file
	static MIRParseResult parseFile(const std::string &path);

	// Merge multiple MIR modules into one
	// (used to combine runtime.mir + platform.mir)
	static std::unique_ptr<MIRModule> merge(std::vector<std::unique_ptr<MIRModule>> modules);

  private:
	std::string source;
	std::string moduleName;
	size_t pos = 0;
	int line = 1;
	int column = 1;
	std::vector<MIRParseError> errors;

	// Current module being built
	std::unique_ptr<MIRModule> module;
	MIRFunction *currentFunction = nullptr;
	MIRBasicBlock *currentBlock = nullptr;

	// Value table for resolving %name references
	std::unordered_map<std::string, MIRValue *> valueTable;

	// Forward references for blocks (for branches to not-yet-defined blocks)
	std::unordered_map<std::string, MIRBasicBlock *> blockTable;

	MIRParser(const std::string &src, const std::string &name);

	MIRParseResult run();

	//--------------------------------------------------------------------------
	// Lexing helpers
	//--------------------------------------------------------------------------

	char peek() const;
	char peekNext() const;
	char advance();
	bool isAtEnd() const;
	void skipWhitespace();
	void skipLineComment();
	bool match(char expected);
	bool match(const char *str);

	std::string readIdentifier();
	std::string readGlobalName(); // @name
	std::string readLocalName();  // %name
	std::string readBlockLabel(); // label:
	int64_t readInteger();
	double readFloat();
	std::string readString();

	//--------------------------------------------------------------------------
	// Parsing helpers
	//--------------------------------------------------------------------------

	void error(const std::string &msg);
	bool expect(char c, const std::string &context = "");
	bool expect(const char *str, const std::string &context = "");

	//--------------------------------------------------------------------------
	// Type parsing
	//--------------------------------------------------------------------------

	MIRType *parseType();

	//--------------------------------------------------------------------------
	// Top-level parsing
	//--------------------------------------------------------------------------

	void parseModule();
	void parseDefine();	 // define <type> @name(...) { ... }
	void parseDeclare(); // declare <type> @name(...)
	void parseGlobal();	 // @name = global/constant <type> <init>

	//--------------------------------------------------------------------------
	// Function body parsing
	//--------------------------------------------------------------------------

	void parseFunctionBody(MIRFunction *func);
	void parseBasicBlock();
	void parseInstruction();

	//--------------------------------------------------------------------------
	// Instruction parsing
	//--------------------------------------------------------------------------

	// Binary operations: %r = add/sub/mul/... <type> %a, %b
	MIRInstruction *parseBinaryOp(MIROpcode op, MIRValue *result);

	// Comparisons: %r = icmp/fcmp <cond> <type> %a, %b
	MIRInstruction *parseICmp(MIRValue *result);
	MIRInstruction *parseFCmp(MIRValue *result);

	// Memory: %r = alloca/load, store <type> %val, ptr %ptr
	MIRInstruction *parseAlloca(MIRValue *result);
	MIRInstruction *parseLoad(MIRValue *result);
	MIRInstruction *parseStore();
	MIRInstruction *parseGEP(MIRValue *result);

	// Conversions: %r = trunc/zext/sext/... <type> %val to <type>
	MIRInstruction *parseConversion(MIROpcode op, MIRValue *result);

	// Control flow: br, ret
	MIRInstruction *parseBr();
	MIRInstruction *parseRet();

	// Calls: %r = call <type> @func(...)
	MIRInstruction *parseCall(MIRValue *result);

	// Phi: %r = phi <type> [%v1, %bb1], [%v2, %bb2], ...
	MIRInstruction *parsePhi(MIRValue *result);

	//--------------------------------------------------------------------------
	// Value parsing
	//--------------------------------------------------------------------------

	MIRValue *parseValue(MIRType *expectedType = nullptr);
	MIRValue *parseConstant(MIRType *type);

	//--------------------------------------------------------------------------
	// Helpers
	//--------------------------------------------------------------------------

	MIRValue *getOrCreateValue(const std::string &name, MIRType *type);
	MIRBasicBlock *getOrCreateBlock(const std::string &name);
};

} // namespace mir
