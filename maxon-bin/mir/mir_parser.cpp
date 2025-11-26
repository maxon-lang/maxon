// mir_parser.cpp - MIR Text Format Parser
// Part of Phase 7: Runtime Library Port for LLVM Elimination Plan

#include "mir_parser.h"
#include <cctype>
#include <cstdlib>
#include <fstream>
#include <sstream>

namespace mir {

//==============================================================================
// MIRParseError
//==============================================================================

std::string MIRParseError::toString() const {
	return "line " + std::to_string(line) + ", column " + std::to_string(column) +
		   ": " + message;
}

//==============================================================================
// MIRParser - Static entry points
//==============================================================================

MIRParseResult MIRParser::parse(const std::string &source,
								const std::string &moduleName) {
	MIRParser parser(source, moduleName);
	return parser.run();
}

MIRParseResult MIRParser::parseFile(const std::string &path) {
	std::ifstream file(path);
	if (!file) {
		MIRParseResult result;
		result.errors.push_back({0, 0, "Could not open file: " + path});
		return result;
	}

	std::stringstream buffer;
	buffer << file.rdbuf();
	return parse(buffer.str(), path);
}

std::unique_ptr<MIRModule> MIRParser::merge(
	std::vector<std::unique_ptr<MIRModule>> modules) {
	if (modules.empty()) {
		return nullptr;
	}

	// Use first module as base
	auto result = std::move(modules[0]);

	// Merge remaining modules
	for (size_t i = 1; i < modules.size(); i++) {
		auto &mod = modules[i];
		if (!mod)
			continue;

		// Move functions
		for (auto &func : mod->functions) {
			result->functions.push_back(std::move(func));
		}

		// Move globals
		for (auto &global : mod->globals) {
			result->globals.push_back(std::move(global));
		}
	}

	return result;
}

//==============================================================================
// MIRParser - Constructor and Main Loop
//==============================================================================

MIRParser::MIRParser(const std::string &src, const std::string &name)
	: source(src), moduleName(name) {}

MIRParseResult MIRParser::run() {
	module = std::make_unique<MIRModule>(moduleName);
	parseModule();

	MIRParseResult result;
	result.module = std::move(module);
	result.errors = std::move(errors);
	return result;
}

//==============================================================================
// Lexing Helpers
//==============================================================================

char MIRParser::peek() const {
	if (isAtEnd())
		return '\0';
	return source[pos];
}

char MIRParser::peekNext() const {
	if (pos + 1 >= source.size())
		return '\0';
	return source[pos + 1];
}

char MIRParser::advance() {
	char c = source[pos++];
	if (c == '\n') {
		line++;
		column = 1;
	} else {
		column++;
	}
	return c;
}

bool MIRParser::isAtEnd() const {
	return pos >= source.size();
}

void MIRParser::skipWhitespace() {
	while (!isAtEnd()) {
		char c = peek();
		if (c == ' ' || c == '\t' || c == '\r' || c == '\n') {
			advance();
		} else if (c == ';') {
			// Line comment
			skipLineComment();
		} else {
			break;
		}
	}
}

void MIRParser::skipLineComment() {
	while (!isAtEnd() && peek() != '\n') {
		advance();
	}
}

bool MIRParser::match(char expected) {
	if (isAtEnd() || peek() != expected)
		return false;
	advance();
	return true;
}

bool MIRParser::match(const char *str) {
	size_t len = strlen(str);
	if (pos + len > source.size())
		return false;
	if (source.compare(pos, len, str) != 0)
		return false;
	for (size_t i = 0; i < len; i++) {
		advance();
	}
	return true;
}

std::string MIRParser::readIdentifier() {
	std::string result;
	while (!isAtEnd()) {
		char c = peek();
		if (std::isalnum(c) || c == '_' || c == '.') {
			result += advance();
		} else {
			break;
		}
	}
	return result;
}

std::string MIRParser::readGlobalName() {
	// Expect @ prefix
	if (!match('@')) {
		error("Expected '@' for global name");
		return "";
	}
	return readIdentifier();
}

std::string MIRParser::readLocalName() {
	// Expect % prefix
	if (!match('%')) {
		error("Expected '%' for local name");
		return "";
	}
	return readIdentifier();
}

std::string MIRParser::readBlockLabel() {
	std::string name = readIdentifier();
	if (!match(':')) {
		error("Expected ':' after block label");
	}
	return name;
}

int64_t MIRParser::readInteger() {
	bool negative = false;
	if (peek() == '-') {
		negative = true;
		advance();
	}

	std::string numStr;

	// Check for hex
	if (peek() == '0' && (peekNext() == 'x' || peekNext() == 'X')) {
		advance(); // '0'
		advance(); // 'x'
		while (!isAtEnd() && std::isxdigit(peek())) {
			numStr += advance();
		}
		int64_t val = std::strtoll(numStr.c_str(), nullptr, 16);
		return negative ? -val : val;
	}

	// Decimal
	while (!isAtEnd() && std::isdigit(peek())) {
		numStr += advance();
	}

	if (numStr.empty()) {
		error("Expected integer");
		return 0;
	}

	int64_t val = std::strtoll(numStr.c_str(), nullptr, 10);
	return negative ? -val : val;
}

double MIRParser::readFloat() {
	std::string numStr;
	bool negative = false;

	if (peek() == '-') {
		negative = true;
		advance();
	}

	// Read integer part
	while (!isAtEnd() && std::isdigit(peek())) {
		numStr += advance();
	}

	// Read decimal part
	if (peek() == '.') {
		numStr += advance();
		while (!isAtEnd() && std::isdigit(peek())) {
			numStr += advance();
		}
	}

	// Read exponent
	if (peek() == 'e' || peek() == 'E') {
		numStr += advance();
		if (peek() == '+' || peek() == '-') {
			numStr += advance();
		}
		while (!isAtEnd() && std::isdigit(peek())) {
			numStr += advance();
		}
	}

	double val = std::strtod(numStr.c_str(), nullptr);
	return negative ? -val : val;
}

std::string MIRParser::readString() {
	if (!match('"')) {
		error("Expected '\"' to start string");
		return "";
	}

	std::string result;
	while (!isAtEnd() && peek() != '"') {
		if (peek() == '\\') {
			advance();
			char c = advance();
			switch (c) {
			case 'n':
				result += '\n';
				break;
			case 't':
				result += '\t';
				break;
			case 'r':
				result += '\r';
				break;
			case '\\':
				result += '\\';
				break;
			case '"':
				result += '"';
				break;
			case '0':
				result += '\0';
				break;
			default:
				result += c;
				break;
			}
		} else {
			result += advance();
		}
	}

	if (!match('"')) {
		error("Expected '\"' to end string");
	}

	return result;
}

//==============================================================================
// Parsing Helpers
//==============================================================================

void MIRParser::error(const std::string &msg) {
	errors.push_back({line, column, msg});
}

bool MIRParser::expect(char c, const std::string &context) {
	skipWhitespace();
	if (!match(c)) {
		std::string msg = "Expected '";
		msg += c;
		msg += "'";
		if (!context.empty()) {
			msg += " " + context;
		}
		error(msg);
		return false;
	}
	return true;
}

bool MIRParser::expect(const char *str, const std::string &context) {
	skipWhitespace();
	if (!match(str)) {
		std::string msg = "Expected '";
		msg += str;
		msg += "'";
		if (!context.empty()) {
			msg += " " + context;
		}
		error(msg);
		return false;
	}
	return true;
}

//==============================================================================
// Type Parsing
//==============================================================================

MIRType *MIRParser::parseType() {
	skipWhitespace();

	// Handle struct types: %StructName
	if (peek() == '%') {
		advance(); // '%'
		std::string structName = readIdentifier();
		// Look up or create the struct type
		// For now, assume Iterator struct with 3 i32 fields
		if (structName == "Iterator") {
			return MIRType::getStruct(structName, {MIRType::getInt32(), MIRType::getInt32(), MIRType::getInt32()});
		}
		// Unknown struct - create placeholder with default size
		return MIRType::getStruct(structName, {});
	}

	std::string typeName = readIdentifier();

	if (typeName == "void")
		return MIRType::getVoid();
	if (typeName == "i1")
		return MIRType::getInt1();
	if (typeName == "i8")
		return MIRType::getInt8();
	if (typeName == "i32")
		return MIRType::getInt32();
	if (typeName == "i64")
		return MIRType::getInt64();
	if (typeName == "f64" || typeName == "double")
		return MIRType::getFloat64();
	if (typeName == "ptr")
		return MIRType::getPtr();

	// Array type: [N x type]
	if (typeName.empty() && peek() == '[') {
		advance(); // '['
		skipWhitespace();
		int64_t size = readInteger();
		skipWhitespace();
		expect('x', "in array type");
		MIRType *elemType = parseType();
		expect(']', "to close array type");
		return MIRType::getArray(elemType, static_cast<uint64_t>(size));
	}

	error("Unknown type: " + typeName);
	return MIRType::getVoid();
}

//==============================================================================
// Top-level Parsing
//==============================================================================

void MIRParser::parseModule() {
	while (!isAtEnd()) {
		skipWhitespace();
		if (isAtEnd())
			break;

		std::string keyword = readIdentifier();

		if (keyword == "define") {
			parseDefine();
		} else if (keyword == "declare") {
			parseDeclare();
		} else if (keyword.empty() && peek() == '@') {
			parseGlobal();
		} else if (keyword == "target") {
			// Skip target triple/datalayout
			while (!isAtEnd() && peek() != '\n') {
				advance();
			}
		} else if (!keyword.empty()) {
			error("Unexpected keyword: " + keyword);
			// Skip to next line
			while (!isAtEnd() && peek() != '\n') {
				advance();
			}
		}
	}
}

void MIRParser::parseDefine() {
	skipWhitespace();

	// Return type
	MIRType *retType = parseType();

	// Function name
	skipWhitespace();
	std::string funcName = readGlobalName();

	// Create function
	MIRFunction *func = module->createFunction(funcName, retType);
	currentFunction = func;
	valueTable.clear();
	blockTable.clear();

	// Parameters
	expect('(', "after function name");
	skipWhitespace();

	while (!isAtEnd() && peek() != ')') {
		MIRType *paramType = parseType();
		skipWhitespace();
		std::string paramName = readLocalName();

		MIRValue *param = func->addParameter(paramType, paramName);
		valueTable[paramName] = param;

		skipWhitespace();
		if (peek() == ',') {
			advance();
			skipWhitespace();
		}
	}

	expect(')', "after parameters");
	skipWhitespace();

	// Function body
	expect('{', "to start function body");
	parseFunctionBody(func);
	expect('}', "to end function body");

	currentFunction = nullptr;
}

void MIRParser::parseDeclare() {
	skipWhitespace();

	// Return type
	MIRType *retType = parseType();

	// Function name
	skipWhitespace();
	std::string funcName = readGlobalName();

	// Create external function declaration
	MIRFunction *func = module->createFunction(funcName, retType);
	func->isExternal = true;

	// Parameters (types only, names optional)
	expect('(', "after function name");
	skipWhitespace();

	std::vector<MIRType *> paramTypes;
	while (!isAtEnd() && peek() != ')') {
		MIRType *paramType = parseType();
		paramTypes.push_back(paramType);

		skipWhitespace();
		// Skip optional parameter name
		if (peek() == '%') {
			readLocalName();
			skipWhitespace();
		}

		if (peek() == ',') {
			advance();
			skipWhitespace();
		}
	}

	expect(')', "after parameters");

	// Add dummy parameters for type info
	for (size_t i = 0; i < paramTypes.size(); i++) {
		func->addParameter(paramTypes[i], "arg" + std::to_string(i));
	}
}

void MIRParser::parseGlobal() {
	std::string name = readGlobalName();
	skipWhitespace();

	expect('=', "after global name");
	skipWhitespace();

	bool isConstant = false;
	std::string keyword = readIdentifier();
	if (keyword == "constant") {
		isConstant = true;
	} else if (keyword == "global") {
		isConstant = false;
	} else if (keyword == "internal") {
		// Skip linkage, read next keyword
		skipWhitespace();
		keyword = readIdentifier();
		if (keyword == "constant") {
			isConstant = true;
		}
	} else {
		error("Expected 'global' or 'constant'");
		return;
	}

	skipWhitespace();
	MIRType *type = parseType();

	MIRGlobal *global = module->createGlobal(name, type);
	global->isConstant = isConstant;

	// Parse initializer if present
	skipWhitespace();
	if (!isAtEnd() && peek() != '\n' && peek() != ';') {
		// Check for zeroinitializer
		if (source.substr(pos, 15) == "zeroinitializer") {
			pos += 15;
			global->hasInitializer = true;
			// Allocate zero-filled buffer
			size_t size = type->sizeInBytes;
			global->initializer.resize(size, 0);
		}
		// Check for c"..." string constant
		else if (peek() == 'c' && pos + 1 < source.size() && source[pos + 1] == '"') {
			advance(); // skip 'c'
			advance(); // skip '"'
			std::string strValue;
			while (!isAtEnd() && peek() != '"') {
				if (peek() == '\\') {
					advance(); // skip backslash
					char escaped = advance();
					if (escaped == '0') {
						// Check for two-digit hex code like \00
						if (!isAtEnd() && std::isxdigit(peek())) {
							char hex1 = advance();
							if (!isAtEnd() && std::isxdigit(peek())) {
								char hex2 = advance();
								int val = 0;
								if (hex1 >= '0' && hex1 <= '9')
									val = (hex1 - '0') * 16;
								else if (hex1 >= 'a' && hex1 <= 'f')
									val = (hex1 - 'a' + 10) * 16;
								else if (hex1 >= 'A' && hex1 <= 'F')
									val = (hex1 - 'A' + 10) * 16;
								if (hex2 >= '0' && hex2 <= '9')
									val += (hex2 - '0');
								else if (hex2 >= 'a' && hex2 <= 'f')
									val += (hex2 - 'a' + 10);
								else if (hex2 >= 'A' && hex2 <= 'F')
									val += (hex2 - 'A' + 10);
								strValue.push_back(static_cast<char>(val));
							} else {
								// Just \0X where X is single hex digit
								int val = 0;
								if (hex1 >= '0' && hex1 <= '9')
									val = hex1 - '0';
								else if (hex1 >= 'a' && hex1 <= 'f')
									val = hex1 - 'a' + 10;
								else if (hex1 >= 'A' && hex1 <= 'F')
									val = hex1 - 'A' + 10;
								strValue.push_back(static_cast<char>(val));
							}
						} else {
							strValue.push_back('\0');
						}
					} else if (escaped == 'n') {
						strValue.push_back('\n');
					} else if (escaped == 't') {
						strValue.push_back('\t');
					} else if (escaped == 'r') {
						strValue.push_back('\r');
					} else if (escaped == '\\') {
						strValue.push_back('\\');
					} else if (escaped == '"') {
						strValue.push_back('"');
					} else {
						strValue.push_back(escaped);
					}
				} else {
					strValue.push_back(advance());
				}
			}
			if (peek() == '"')
				advance(); // skip closing quote
			global->isStringConstant = true;
			global->stringValue = strValue;
		}
		// Skip other initializers for now
		else {
			while (!isAtEnd() && peek() != '\n' && peek() != ';') {
				advance();
			}
		}
	}
}

//==============================================================================
// Function Body Parsing
//==============================================================================

void MIRParser::parseFunctionBody(MIRFunction *func) {
	while (!isAtEnd()) {
		skipWhitespace();

		if (peek() == '}')
			break;

		// Check if this is a block label
		size_t savedPos = pos;
		std::string ident = readIdentifier();
		skipWhitespace();

		if (!ident.empty() && peek() == ':') {
			// This is a block label
			advance(); // consume ':'
			currentBlock = getOrCreateBlock(ident);
			if (currentBlock->parent == nullptr) {
				func->basicBlocks.push_back(
					std::unique_ptr<MIRBasicBlock>(currentBlock));
				currentBlock->parent = func;
			}
		} else {
			// Not a block label, restore position
			pos = savedPos;
			// If no current block, create entry block
			if (currentBlock == nullptr) {
				currentBlock = func->createBasicBlock("entry");
			}
			parseInstruction();
		}
	}
}

void MIRParser::parseInstruction() {
	skipWhitespace();

	// Check if instruction has a result: %name = ...
	MIRValue *result = nullptr;
	if (peek() == '%') {
		std::string resultName = readLocalName();
		skipWhitespace();
		expect('=', "after result name");
		skipWhitespace();

		// Create placeholder value, type will be set later
		result = getOrCreateValue(resultName, nullptr);
	}

	// Read opcode
	std::string opcode = readIdentifier();

	std::unique_ptr<MIRInstruction> inst;

	// Binary arithmetic (integer)
	if (opcode == "add") {
		inst.reset(parseBinaryOp(MIROpcode::Add, result));
	} else if (opcode == "sub") {
		inst.reset(parseBinaryOp(MIROpcode::Sub, result));
	} else if (opcode == "mul") {
		inst.reset(parseBinaryOp(MIROpcode::Mul, result));
	} else if (opcode == "sdiv") {
		inst.reset(parseBinaryOp(MIROpcode::SDiv, result));
	} else if (opcode == "srem") {
		inst.reset(parseBinaryOp(MIROpcode::SRem, result));
	} else if (opcode == "udiv") {
		inst.reset(parseBinaryOp(MIROpcode::UDiv, result));
	} else if (opcode == "urem") {
		inst.reset(parseBinaryOp(MIROpcode::URem, result));
	}
	// Bitwise
	else if (opcode == "and") {
		inst.reset(parseBinaryOp(MIROpcode::And, result));
	} else if (opcode == "or") {
		inst.reset(parseBinaryOp(MIROpcode::Or, result));
	} else if (opcode == "xor") {
		inst.reset(parseBinaryOp(MIROpcode::Xor, result));
	} else if (opcode == "shl") {
		inst.reset(parseBinaryOp(MIROpcode::Shl, result));
	} else if (opcode == "ashr") {
		inst.reset(parseBinaryOp(MIROpcode::AShr, result));
	} else if (opcode == "lshr") {
		inst.reset(parseBinaryOp(MIROpcode::LShr, result));
	}
	// Floating-point arithmetic
	else if (opcode == "fadd") {
		inst.reset(parseBinaryOp(MIROpcode::FAdd, result));
	} else if (opcode == "fsub") {
		inst.reset(parseBinaryOp(MIROpcode::FSub, result));
	} else if (opcode == "fmul") {
		inst.reset(parseBinaryOp(MIROpcode::FMul, result));
	} else if (opcode == "fdiv") {
		inst.reset(parseBinaryOp(MIROpcode::FDiv, result));
	} else if (opcode == "frem") {
		inst.reset(parseBinaryOp(MIROpcode::FRem, result));
	}
	// Negation
	else if (opcode == "fneg") {
		// fneg <type> %val
		MIRType *type = parseType();
		skipWhitespace();
		MIRValue *operand = parseValue(type);

		inst = std::make_unique<MIRInstruction>(MIROpcode::FNeg);
		inst->operands.push_back(operand);
		if (result) {
			result->type = type;
			inst->result = result;
		}
	}
	// Comparisons
	else if (opcode == "icmp") {
		inst.reset(parseICmp(result));
	} else if (opcode == "fcmp") {
		inst.reset(parseFCmp(result));
	}
	// Memory
	else if (opcode == "alloca") {
		inst.reset(parseAlloca(result));
	} else if (opcode == "load") {
		inst.reset(parseLoad(result));
	} else if (opcode == "store") {
		inst.reset(parseStore());
	} else if (opcode == "getelementptr") {
		inst.reset(parseGEP(result));
	}
	// Conversions
	else if (opcode == "trunc") {
		inst.reset(parseConversion(MIROpcode::Trunc, result));
	} else if (opcode == "zext") {
		inst.reset(parseConversion(MIROpcode::ZExt, result));
	} else if (opcode == "sext") {
		inst.reset(parseConversion(MIROpcode::SExt, result));
	} else if (opcode == "fptosi") {
		inst.reset(parseConversion(MIROpcode::FPToSI, result));
	} else if (opcode == "sitofp") {
		inst.reset(parseConversion(MIROpcode::SIToFP, result));
	} else if (opcode == "ptrtoint") {
		inst.reset(parseConversion(MIROpcode::PtrToInt, result));
	} else if (opcode == "inttoptr") {
		inst.reset(parseConversion(MIROpcode::IntToPtr, result));
	} else if (opcode == "bitcast") {
		inst.reset(parseConversion(MIROpcode::Bitcast, result));
	}
	// Control flow
	else if (opcode == "br") {
		inst.reset(parseBr());
	} else if (opcode == "ret") {
		inst.reset(parseRet());
	}
	// Calls
	else if (opcode == "call") {
		inst.reset(parseCall(result));
	}
	// Phi
	else if (opcode == "phi") {
		inst.reset(parsePhi(result));
	} else {
		error("Unknown instruction: " + opcode);
		// Skip to end of line
		while (!isAtEnd() && peek() != '\n') {
			advance();
		}
		return;
	}

	if (inst && currentBlock) {
		if (result) {
			result->definingInst = inst.get();
		}
		currentBlock->addInstruction(std::move(inst));
	}
}

//==============================================================================
// Instruction Parsing
//==============================================================================

MIRInstruction *MIRParser::parseBinaryOp(MIROpcode op, MIRValue *result) {
	skipWhitespace();
	MIRType *type = parseType();
	skipWhitespace();
	MIRValue *lhs = parseValue(type);
	expect(',', "between operands");
	MIRValue *rhs = parseValue(type);

	auto inst = new MIRInstruction(op);
	inst->operands.push_back(lhs);
	inst->operands.push_back(rhs);

	if (result) {
		result->type = type;
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseICmp(MIRValue *result) {
	skipWhitespace();
	std::string cond = readIdentifier();

	MIROpcode op;
	if (cond == "eq")
		op = MIROpcode::ICmpEq;
	else if (cond == "ne")
		op = MIROpcode::ICmpNe;
	else if (cond == "slt")
		op = MIROpcode::ICmpSLT;
	else if (cond == "sle")
		op = MIROpcode::ICmpSLE;
	else if (cond == "sgt")
		op = MIROpcode::ICmpSGT;
	else if (cond == "sge")
		op = MIROpcode::ICmpSGE;
	else if (cond == "ult")
		op = MIROpcode::ICmpULT;
	else if (cond == "ule")
		op = MIROpcode::ICmpULE;
	else if (cond == "ugt")
		op = MIROpcode::ICmpUGT;
	else if (cond == "uge")
		op = MIROpcode::ICmpUGE;
	else {
		error("Unknown icmp condition: " + cond);
		op = MIROpcode::ICmpEq;
	}

	skipWhitespace();
	MIRType *type = parseType();
	skipWhitespace();
	MIRValue *lhs = parseValue(type);
	expect(',', "between operands");
	MIRValue *rhs = parseValue(type);

	auto inst = new MIRInstruction(op);
	inst->operands.push_back(lhs);
	inst->operands.push_back(rhs);

	if (result) {
		result->type = MIRType::getInt1();
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseFCmp(MIRValue *result) {
	skipWhitespace();
	std::string cond = readIdentifier();

	MIROpcode op;
	if (cond == "oeq" || cond == "eq")
		op = MIROpcode::FCmpEq;
	else if (cond == "one" || cond == "ne")
		op = MIROpcode::FCmpNe;
	else if (cond == "olt" || cond == "lt")
		op = MIROpcode::FCmpLT;
	else if (cond == "ole" || cond == "le")
		op = MIROpcode::FCmpLE;
	else if (cond == "ogt" || cond == "gt")
		op = MIROpcode::FCmpGT;
	else if (cond == "oge" || cond == "ge")
		op = MIROpcode::FCmpGE;
	else {
		error("Unknown fcmp condition: " + cond);
		op = MIROpcode::FCmpEq;
	}

	skipWhitespace();
	MIRType *type = parseType();
	skipWhitespace();
	MIRValue *lhs = parseValue(type);
	expect(',', "between operands");
	MIRValue *rhs = parseValue(type);

	auto inst = new MIRInstruction(op);
	inst->operands.push_back(lhs);
	inst->operands.push_back(rhs);

	if (result) {
		result->type = MIRType::getInt1();
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseAlloca(MIRValue *result) {
	skipWhitespace();
	MIRType *allocType = parseType();

	auto inst = new MIRInstruction(MIROpcode::Alloca);
	inst->allocatedType = allocType; // Store the allocated type for code generation

	if (result) {
		result->type = MIRType::getPtr();
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseLoad(MIRValue *result) {
	skipWhitespace();
	MIRType *type = parseType();
	expect(',', "after load type");
	skipWhitespace();

	// Expect ptr keyword
	std::string ptrKeyword = readIdentifier();
	if (ptrKeyword != "ptr") {
		error("Expected 'ptr' in load instruction");
	}
	skipWhitespace();

	MIRValue *ptr = parseValue(MIRType::getPtr());

	auto inst = new MIRInstruction(MIROpcode::Load);
	inst->operands.push_back(ptr);

	if (result) {
		result->type = type;
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseStore() {
	skipWhitespace();
	MIRType *valType = parseType();
	skipWhitespace();
	MIRValue *value = parseValue(valType);
	expect(',', "after store value");
	skipWhitespace();

	// Expect ptr keyword
	std::string ptrKeyword = readIdentifier();
	if (ptrKeyword != "ptr") {
		error("Expected 'ptr' in store instruction");
	}
	skipWhitespace();

	MIRValue *ptr = parseValue(MIRType::getPtr());

	auto inst = new MIRInstruction(MIROpcode::Store);
	inst->operands.push_back(value);
	inst->operands.push_back(ptr);

	return inst;
}

MIRInstruction *MIRParser::parseGEP(MIRValue *result) {
	skipWhitespace();

	// Optional 'inbounds' keyword - need to look ahead carefully
	// because 'i32' also starts with 'i'
	if (peek() == 'i') {
		// Save position to restore if not 'inbounds'
		size_t savedPos = pos;
		int savedLine = line;
		int savedColumn = column;

		std::string keyword = readIdentifier();
		if (keyword == "inbounds") {
			skipWhitespace();
		} else {
			// Not 'inbounds', restore position and parse as type
			pos = savedPos;
			line = savedLine;
			column = savedColumn;
		}
	}

	// Parse element type and store it for code generation
	MIRType *elemType = parseType();
	expect(',', "after GEP base type");
	skipWhitespace();

	// Pointer type and value
	std::string ptrKeyword = readIdentifier();
	if (ptrKeyword != "ptr") {
		error("Expected 'ptr' in GEP");
	}
	skipWhitespace();
	MIRValue *ptr = parseValue(MIRType::getPtr());

	// Indices
	std::vector<MIRValue *> indices;
	while (peek() == ',') {
		advance(); // ','
		skipWhitespace();
		MIRType *idxType = parseType();
		skipWhitespace();
		MIRValue *idx = parseValue(idxType);
		indices.push_back(idx);
	}

	auto inst = new MIRInstruction(MIROpcode::GetElementPtr);
	inst->elementType = elemType; // Store element type for code generation
	inst->operands.push_back(ptr);
	for (auto idx : indices) {
		inst->operands.push_back(idx);
	}

	if (result) {
		result->type = MIRType::getPtr();
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseConversion(MIROpcode op, MIRValue *result) {
	skipWhitespace();
	MIRType *srcType = parseType();
	skipWhitespace();
	MIRValue *value = parseValue(srcType);
	skipWhitespace();
	expect("to", "in conversion");
	skipWhitespace();
	MIRType *destType = parseType();

	auto inst = new MIRInstruction(op);
	inst->operands.push_back(value);

	if (result) {
		result->type = destType;
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parseBr() {
	skipWhitespace();

	// Check if conditional or unconditional
	if (peek() == 'i') {
		// Conditional: br i1 %cond, label %true, label %false
		MIRType *condType = parseType();
		skipWhitespace();
		MIRValue *cond = parseValue(condType);
		expect(',', "after condition");
		skipWhitespace();

		expect("label", "in conditional branch");
		skipWhitespace();
		std::string trueName = readLocalName();
		MIRBasicBlock *trueBlock = getOrCreateBlock(trueName);

		expect(',', "between branch targets");
		skipWhitespace();
		expect("label", "in conditional branch");
		skipWhitespace();
		std::string falseName = readLocalName();
		MIRBasicBlock *falseBlock = getOrCreateBlock(falseName);

		auto inst = new MIRInstruction(MIROpcode::CondBr);
		inst->operands.push_back(cond);
		inst->operands.push_back(MIRValue::createBlockRef(trueBlock));
		inst->operands.push_back(MIRValue::createBlockRef(falseBlock));

		return inst;
	} else {
		// Unconditional: br label %dest
		expect("label", "in branch");
		skipWhitespace();
		std::string destName = readLocalName();
		MIRBasicBlock *destBlock = getOrCreateBlock(destName);

		auto inst = new MIRInstruction(MIROpcode::Br);
		inst->operands.push_back(MIRValue::createBlockRef(destBlock));

		return inst;
	}
}

MIRInstruction *MIRParser::parseRet() {
	skipWhitespace();

	if (peek() == 'v') {
		// ret void
		std::string keyword = readIdentifier();
		if (keyword != "void") {
			error("Expected 'void' in ret instruction");
		}
		return new MIRInstruction(MIROpcode::RetVoid);
	} else {
		// ret <type> <value>
		MIRType *type = parseType();
		skipWhitespace();
		MIRValue *value = parseValue(type);

		auto inst = new MIRInstruction(MIROpcode::Ret);
		inst->operands.push_back(value);
		return inst;
	}
}

MIRInstruction *MIRParser::parseCall(MIRValue *result) {
	skipWhitespace();

	// Return type
	MIRType *retType = parseType();
	skipWhitespace();

	// Callee name
	std::string calleeName = readGlobalName();
	skipWhitespace();

	// Arguments
	expect('(', "after callee name");
	std::vector<MIRValue *> args;
	skipWhitespace();

	while (!isAtEnd() && peek() != ')') {
		MIRType *argType = parseType();
		skipWhitespace();
		MIRValue *arg = parseValue(argType);
		args.push_back(arg);

		skipWhitespace();
		if (peek() == ',') {
			advance();
			skipWhitespace();
		}
	}

	expect(')', "after call arguments");

	auto inst = new MIRInstruction(MIROpcode::Call);
	inst->calleeName = calleeName;
	inst->calleeFunc = module->getFunction(calleeName);
	for (auto arg : args) {
		inst->operands.push_back(arg);
	}

	if (result && retType->kind != MIRTypeKind::Void) {
		result->type = retType;
		inst->result = result;
	}

	return inst;
}

MIRInstruction *MIRParser::parsePhi(MIRValue *result) {
	skipWhitespace();
	MIRType *type = parseType();

	auto inst = new MIRInstruction(MIROpcode::Phi);

	// Parse incoming values: [%val, %block], [%val2, %block2], ...
	skipWhitespace();
	while (peek() == '[') {
		advance(); // '['
		skipWhitespace();

		MIRValue *value = parseValue(type);
		expect(',', "in phi incoming");
		skipWhitespace();

		std::string blockName = readLocalName();
		MIRBasicBlock *block = getOrCreateBlock(blockName);

		expect(']', "to close phi incoming");
		skipWhitespace();

		inst->phiIncoming.push_back({value, block});

		if (peek() == ',') {
			advance();
			skipWhitespace();
		}
	}

	if (result) {
		result->type = type;
		inst->result = result;
	}

	return inst;
}

//==============================================================================
// Value Parsing
//==============================================================================

MIRValue *MIRParser::parseValue(MIRType *expectedType) {
	skipWhitespace();

	if (peek() == '%') {
		// Local value reference
		std::string name = readLocalName();
		return getOrCreateValue(name, expectedType);
	} else if (peek() == '@') {
		// Global reference
		std::string name = readGlobalName();
		return MIRValue::createGlobal(MIRType::getPtr(), name);
	} else if (peek() == 'n') {
		// null
		std::string keyword = readIdentifier();
		if (keyword == "null") {
			return MIRValue::createConstantNull();
		}
		error("Unknown value: " + keyword);
		return MIRValue::createConstantNull();
	} else if (peek() == 't' || peek() == 'f') {
		// true/false
		std::string keyword = readIdentifier();
		if (keyword == "true") {
			return MIRValue::createConstantInt(MIRType::getInt1(), 1);
		} else if (keyword == "false") {
			return MIRValue::createConstantInt(MIRType::getInt1(), 0);
		}
		error("Unknown value: " + keyword);
		return MIRValue::createConstantInt(MIRType::getInt1(), 0);
	} else {
		// Numeric constant
		return parseConstant(expectedType);
	}
}

MIRValue *MIRParser::parseConstant(MIRType *type) {
	// Check for floating-point
	bool isFloat = false;

	// Scan ahead to see if this looks like a float
	bool hasDecimal = false;
	bool hasExponent = false;
	size_t scanPos = pos;
	if (source[scanPos] == '-')
		scanPos++;
	while (scanPos < source.size()) {
		char c = source[scanPos];
		if (c == '.') {
			hasDecimal = true;
		} else if (c == 'e' || c == 'E') {
			hasExponent = true;
		} else if (!std::isdigit(c) && c != '+' && c != '-') {
			break;
		}
		scanPos++;
	}
	isFloat = hasDecimal || hasExponent;

	// Also check expected type
	if (type && type->kind == MIRTypeKind::Float64) {
		isFloat = true;
	}

	if (isFloat) {
		double val = readFloat();
		return MIRValue::createConstantFloat(val);
	} else {
		int64_t val = readInteger();
		MIRType *intType = type ? type : MIRType::getInt64();
		return MIRValue::createConstantInt(intType, val);
	}
}

//==============================================================================
// Helpers
//==============================================================================

MIRValue *MIRParser::getOrCreateValue(const std::string &name, MIRType *type) {
	auto it = valueTable.find(name);
	if (it != valueTable.end()) {
		return it->second;
	}

	// Create new virtual register
	MIRValue *val = currentFunction->createVirtualReg(type ? type : MIRType::getInt64());
	val->name = name;
	valueTable[name] = val;
	return val;
}

MIRBasicBlock *MIRParser::getOrCreateBlock(const std::string &name) {
	auto it = blockTable.find(name);
	if (it != blockTable.end()) {
		return it->second;
	}

	// Create new block (will be added to function later)
	MIRBasicBlock *block = new MIRBasicBlock(name);
	blockTable[name] = block;
	return block;
}

} // namespace mir
