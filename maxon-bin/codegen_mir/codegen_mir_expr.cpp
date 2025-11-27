/**
 * MIR Code Generator - Expression Generation
 *
 * This file implements expression code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../lexer.h"
#include <cmath>
#include <cstring>
#include <stdexcept>

//==============================================================================
// String Literal Generation (Small String Optimization)
//==============================================================================

// SSO constants
constexpr size_t SSO_MAX_LENGTH = 15; // Max string length for inline storage

mir::MIRValue *MIRCodeGenerator::generateStringLiteral(StringLiteralExprAST *strExpr) {
	// All string literals use SSO (Small String Optimization)
	if (strExpr->value.length() <= SSO_MAX_LENGTH) {
		return generateSmallStringLiteral(strExpr->value);
	} else {
		return generateLargeStringLiteral(strExpr->value);
	}
}

mir::MIRValue *MIRCodeGenerator::generateSmallStringLiteral(const std::string &str) {
	// Small String Optimization (SSO)
	// Layout: 16 bytes total
	// [bytes 0-14: UTF-8 data, zero-padded]
	// [byte 15: remaining capacity (15 - length), MSB = 0 for small string]

	mir::MIRType *stringType = getTypeFromString("string");

	// Create alloca for the string struct
	mir::MIRValue *stringAlloca = builder->createAlloca(stringType, "str.sso");

	// Pack the string data into two i64 values
	// First i64: bytes 0-7
	// Second i64: bytes 8-15 (includes capacity byte at position 7 of this word)

	uint64_t word0 = 0;
	uint64_t word1 = 0;
	size_t len = str.length();

	// Copy bytes into word0 (bytes 0-7)
	for (size_t i = 0; i < std::min(len, size_t(8)); i++) {
		word0 |= (static_cast<uint64_t>(static_cast<unsigned char>(str[i])) << (i * 8));
	}

	// Copy bytes into word1 (bytes 8-14), leave byte 15 for capacity
	for (size_t i = 8; i < std::min(len, size_t(15)); i++) {
		word1 |= (static_cast<uint64_t>(static_cast<unsigned char>(str[i])) << ((i - 8) * 8));
	}

	// Set capacity byte at position 15 (byte 7 of word1)
	// Capacity = 15 - length, MSB = 0 for small string
	uint8_t capacityByte = static_cast<uint8_t>(SSO_MAX_LENGTH - len);
	word1 |= (static_cast<uint64_t>(capacityByte) << 56);

	// Store word0 (first i64)
	mir::MIRValue *field0Ptr = builder->createStructGEP(stringType, stringAlloca, 0, "str.word0.ptr");
	builder->createStore(builder->getInt64(static_cast<int64_t>(word0)), field0Ptr);

	// Store word1 (second i64)
	mir::MIRValue *field1Ptr = builder->createStructGEP(stringType, stringAlloca, 1, "str.word1.ptr");
	builder->createStore(builder->getInt64(static_cast<int64_t>(word1)), field1Ptr);

	// Return the alloca pointer (the string struct is by-value in Maxon)
	return stringAlloca;
}

mir::MIRValue *MIRCodeGenerator::generateLargeStringLiteral(const std::string &str) {
	// Large string: heap-allocated with copy-on-write semantics
	// Layout: 16 bytes total
	// [bytes 0-7: pointer to heap buffer (tagged)]
	// [bytes 8-11: count (length in bytes)]
	// [bytes 12-15: capacity and flags, MSB of byte 15 = 1 for large string]

	mir::MIRType *stringType = getTypeFromString("string");

	// Create a global constant for the string data (for static strings)
	mir::MIRGlobal *strGlobal = builder->createStringConstant(".str", str);

	// Create alloca for the string struct
	mir::MIRValue *stringAlloca = builder->createAlloca(stringType, "str.large");

	// For static string literals, we point directly to the global data
	// The global is already null-terminated by createStringConstant

	// Get pointer to the global string data
	mir::MIRValue *strPtr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), strGlobal->name);

	// First i64: pointer (with a tag bit set if we want, but for static strings, no tag needed)
	mir::MIRValue *ptrAsInt = builder->createPtrToInt(strPtr, mir::MIRType::getInt64(), "str.ptr.int");

	// Store pointer as first word
	mir::MIRValue *field0Ptr = builder->createStructGEP(stringType, stringAlloca, 0, "str.ptr.field");
	builder->createStore(ptrAsInt, field0Ptr);

	// Second i64: pack count (32-bit) and capacity+flags (32-bit)
	// Lower 32 bits: count (string length)
	// Upper 32 bits: capacity (31 bits) + is_large flag (1 bit, MSB)
	size_t len = str.length();
	size_t capacity = len + 1; // Include null terminator space

	// Pack: count in lower 32 bits, capacity | 0x80000000 in upper 32 bits
	uint64_t word1 = static_cast<uint64_t>(len) |
					 (static_cast<uint64_t>(capacity | 0x80000000) << 32);

	mir::MIRValue *field1Ptr = builder->createStructGEP(stringType, stringAlloca, 1, "str.meta.field");
	builder->createStore(builder->getInt64(static_cast<int64_t>(word1)), field1Ptr);

	return stringAlloca;
}

mir::MIRValue *MIRCodeGenerator::generateStringEquals(mir::MIRValue *left, mir::MIRValue *right) {
	// Call __string_equals runtime function
	// bool __string_equals(ptr str1, ptr str2)

	// Declare the runtime function if not already declared
	mir::MIRFunction *strEqualsFunc = module->getFunction("__string_equals");
	if (!strEqualsFunc) {
		std::vector<mir::MIRType *> paramTypes = {mir::MIRType::getPtr(), mir::MIRType::getPtr()};
		strEqualsFunc = builder->declareFunction("__string_equals", mir::MIRType::getInt1(), paramTypes);
	}

	// Both left and right are pointers to string structs (from alloca)
	// Pass them directly to the comparison function
	std::vector<mir::MIRValue *> args = {left, right};
	return builder->createCall(strEqualsFunc, args, "streq");
}

mir::MIRValue *MIRCodeGenerator::generateStringConcat(mir::MIRValue *left, mir::MIRValue *right) {
	// Call __string_concat runtime function
	// ptr __string_concat(ptr dest, ptr str1, ptr str2)

	// Declare the runtime function if not already declared
	mir::MIRFunction *strConcatFunc = module->getFunction("__string_concat");
	if (!strConcatFunc) {
		std::vector<mir::MIRType *> paramTypes = {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getPtr()};
		strConcatFunc = builder->declareFunction("__string_concat", mir::MIRType::getPtr(), paramTypes);
	}

	// Allocate destination string struct
	mir::MIRType *stringType = getTypeFromString("string");
	mir::MIRValue *destAlloca = builder->createAlloca(stringType, "str.concat");

	// Call __string_concat(dest, left, right)
	std::vector<mir::MIRValue *> args = {destAlloca, left, right};
	builder->createCall(strConcatFunc, args, "concat");

	// Return the dest pointer (it's the result string)
	return destAlloca;
}

bool MIRCodeGenerator::isStringExpression(ExprAST *expr) {
	// Check if expression has string type
	if (dynamic_cast<StringLiteralExprAST *>(expr)) {
		return true;
	}
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		auto it = variableTypes.find(varExpr->name);
		if (it != variableTypes.end() && (it->second == "string" || it->second == "__string")) {
			return true;
		}
	}
	// Check for string concatenation (binary + with string operands)
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		if (binExpr->op == '+' && isStringExpression(binExpr->left.get()) &&
			isStringExpression(binExpr->right.get())) {
			return true;
		}
	}
	return false;
}

mir::MIRValue *MIRCodeGenerator::generateStringOperand(ExprAST *expr) {
	// Generate a pointer to a string struct
	if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(expr)) {
		// String literal returns alloca pointer directly
		return generateStringLiteral(strExpr);
	}
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		// Variable - load the pointer to the string struct
		mir::MIRValue *alloca = namedValues[varExpr->name];
		if (!alloca) {
			throw std::runtime_error("Unknown variable name: " + varExpr->name);
		}
		// Load the pointer that points to the string struct
		return builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name + ".strptr");
	}
	// Handle binary expressions (string concatenation)
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		if (binExpr->op == '+' && isStringExpression(binExpr->left.get()) &&
			isStringExpression(binExpr->right.get())) {
			mir::MIRValue *leftStr = generateStringOperand(binExpr->left.get());
			mir::MIRValue *rightStr = generateStringOperand(binExpr->right.get());
			return generateStringConcat(leftStr, rightStr);
		}
	}
	throw std::runtime_error("Unsupported string expression type");
}

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return builder->getInt32(numExpr->value);
	}

	if (auto *byteExpr = dynamic_cast<ByteExprAST *>(expr)) {
		return builder->getInt8(static_cast<int8_t>(byteExpr->value));
	}

	if (auto *floatExpr = dynamic_cast<FloatExprAST *>(expr)) {
		return builder->getFloat64(floatExpr->value);
	}

	if (auto *boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return builder->getInt1(boolExpr->value);
	}

	if (auto *charExpr = dynamic_cast<CharacterExprAST *>(expr)) {
		return builder->getInt8(static_cast<int8_t>(charExpr->value));
	}

	if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(expr)) {
		// Use new string literal generation with SSO support
		return generateStringLiteral(strExpr);
	}

	if (dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		// Array literals should only appear as initializers
		throw std::runtime_error("Array literal can only be used as an initializer in variable declarations");
	}

	if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		mir::MIRValue *value = generateExpr(castExpr->expr.get());
		if (!value) {
			throw std::runtime_error("Failed to generate expression for cast");
		}

		mir::MIRType *targetType = getTypeFromString(castExpr->targetType);
		mir::MIRType *sourceType = value->type;

		// Handle different cast scenarios
		if (sourceType == targetType) {
			return value;
		}

		// Integer to integer
		if (sourceType->isInteger() && targetType->isInteger()) {
			uint64_t sourceBits = sourceType->sizeInBytes * 8;
			uint64_t targetBits = targetType->sizeInBytes * 8;
			if (sourceBits < targetBits) {
				return builder->createZExt(value, targetType, "zexttmp");
			} else if (sourceBits > targetBits) {
				return builder->createTrunc(value, targetType, "trunctmp");
			}
			return value;
		}

		// Integer to float
		if (sourceType->isInteger() && targetType->isFloat()) {
			return builder->createSIToFP(value, targetType, "int2floattmp");
		}

		// Float to integer
		if (sourceType->isFloat() && targetType->isInteger()) {
			return builder->createFPToSI(value, targetType, "float2inttmp");
		}

		// Integer to pointer
		if (sourceType->isInteger() && targetType->isPointer()) {
			return builder->createIntToPtr(value, targetType, "int2ptrtmp");
		}

		// Pointer to integer
		if (sourceType->isPointer() && targetType->isInteger()) {
			return builder->createPtrToInt(value, targetType, "ptr2inttmp");
		}

		throw std::runtime_error("Unsupported cast");
	}

	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		mir::MIRValue *alloca = namedValues[varExpr->name];
		if (!alloca) {
			throw std::runtime_error("Unknown variable name: " + varExpr->name);
		}

		// Determine the type to load
		std::string typeStr = variableTypes[varExpr->name];
		mir::MIRType *loadType;
		if (!typeStr.empty()) {
			loadType = getTypeFromString(typeStr);
		} else {
			loadType = mir::MIRType::getInt32();
		}

		// For array parameters (pointers), load the pointer type
		if (isArrayParam(typeStr)) {
			loadType = mir::MIRType::getPtr();
		}

		return builder->createLoad(loadType, alloca, varExpr->name);
	}

	if (dynamic_cast<StructInitExprAST *>(expr)) {
		throw std::runtime_error("Struct literal can only be used as an initializer in variable declarations");
	}

	if (auto *memberAccessExpr = dynamic_cast<MemberAccessExprAST *>(expr)) {
		std::string varType;
		mir::MIRValue *objectPtr = nullptr;

		if (memberAccessExpr->object) {
			// Complex expression (e.g., arr[0].field)
			mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());

			if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(memberAccessExpr->object.get())) {
				std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
				if (arrayType.size() > 2 && arrayType[0] == '[') {
					size_t closeBracket = arrayType.find(']');
					if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
						varType = arrayType.substr(closeBracket + 1);
					}
				}
				objectPtr = objectValue;
			}
		} else {
			// Simple variable access
			varType = variableTypes[memberAccessExpr->objectName];
			mir::MIRValue *structAlloca = namedValues[memberAccessExpr->objectName];
			if (!structAlloca) {
				throw std::runtime_error("Unknown variable: " + memberAccessExpr->objectName);
			}

			// Check if this is a pointer to struct or struct directly
			std::string storedType = variableTypes[memberAccessExpr->objectName];
			if (isArrayParam(storedType)) {
				// It's an array parameter - load the pointer first
				objectPtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
			} else if (isStructParameter(memberAccessExpr->objectName)) {
				// Struct parameters are passed by pointer - load the pointer first
				objectPtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
			} else {
				objectPtr = structAlloca;
			}
		}

		// Check for struct member access
		if (structTypes.find(varType) != structTypes.end()) {
			mir::MIRType *structType = structTypes[varType];
			const auto &fields = structFields[varType];

			int fieldIndex = -1;
			for (size_t i = 0; i < fields.size(); i++) {
				if (fields[i].first == memberAccessExpr->memberName) {
					fieldIndex = static_cast<int>(i);
					break;
				}
			}

			if (fieldIndex < 0) {
				throw std::runtime_error("Unknown field '" + memberAccessExpr->memberName +
										 "' in struct '" + varType + "'");
			}

			// GEP to get field pointer
			mir::MIRValue *fieldPtr = builder->createStructGEP(structType, objectPtr,
															   fieldIndex, memberAccessExpr->memberName);

			// Load the field value
			mir::MIRType *fieldType = structType->fieldTypes[fieldIndex];
			return builder->createLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
		}

		// Handle array.length
		if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
			std::string lengthVar = memberAccessExpr->objectName + ".__length";
			mir::MIRValue *lengthAlloca = namedValues[lengthVar];
			if (lengthAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
			}
			throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
		}

		// Handle array.capacity (dynamic arrays only)
		if (memberAccessExpr->memberName == "capacity" && !memberAccessExpr->object) {
			std::string capacityVar = memberAccessExpr->objectName + ".__capacity";
			mir::MIRValue *capacityAlloca = namedValues[capacityVar];
			if (capacityAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");
			}
			// Static arrays don't have capacity - capacity == length
			std::string lengthVar = memberAccessExpr->objectName + ".__length";
			mir::MIRValue *lengthAlloca = namedValues[lengthVar];
			if (lengthAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "capacity");
			}
			throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
		}

		throw std::runtime_error("Unknown member: " + memberAccessExpr->memberName);
	}

	if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		mir::MIRValue *alloca = namedValues[arrayIndexExpr->arrayName];
		if (!alloca) {
			throw std::runtime_error("Unknown array name: " + arrayIndexExpr->arrayName);
		}

		mir::MIRValue *indexVal = generateExpr(arrayIndexExpr->index.get());
		if (!indexVal) {
			throw std::runtime_error("Failed to generate array index");
		}

		// Determine element type
		std::string elementTypeStr = "int";
		std::string varType = variableTypes[arrayIndexExpr->arrayName];
		if (varType.size() > 2 && varType[0] == '[') {
			size_t closeBracket = varType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
				elementTypeStr = varType.substr(closeBracket + 1);
			}
		}
		mir::MIRType *elementType = getTypeFromString(elementTypeStr);
		bool isStructElement = structTypes.find(elementTypeStr) != structTypes.end();

		// For stack-allocated arrays, alloca IS the array pointer directly
		// For heap-allocated arrays, alloca holds a pointer that must be loaded
		mir::MIRValue *arrayPtr;
		if (stackAllocatedArrays.count(arrayIndexExpr->arrayName) > 0) {
			// Stack array: alloca is directly the array memory
			arrayPtr = alloca;
		} else {
			// Heap array: load the pointer from alloca
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		}
		mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");

		if (isStructElement) {
			return elementPtr; // Return pointer for struct elements
		} else {
			return builder->createLoad(elementType, elementPtr, "arrayelem");
		}
	}

	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		// Check for string comparison BEFORE generating expressions
		// We need to check the source type, not the MIR type
		bool leftIsString = isStringExpression(binExpr->left.get());
		bool rightIsString = isStringExpression(binExpr->right.get());

		if (leftIsString && rightIsString && (binExpr->op == 'E' || binExpr->op == 'N')) {
			// String comparison - get pointers to string structs
			mir::MIRValue *left = generateStringOperand(binExpr->left.get());
			mir::MIRValue *right = generateStringOperand(binExpr->right.get());

			if (binExpr->op == 'E') { // ==
				return generateStringEquals(left, right);
			} else { // !=
				mir::MIRValue *eq = generateStringEquals(left, right);
				// Negate the result
				return builder->createICmpEq(eq, mir::MIRValue::createConstantInt(mir::MIRType::getInt1(), 0), "strnetmp");
			}
		}

		// String concatenation with + operator
		if (leftIsString && rightIsString && binExpr->op == '+') {
			mir::MIRValue *leftStr = generateStringOperand(binExpr->left.get());
			mir::MIRValue *rightStr = generateStringOperand(binExpr->right.get());
			return generateStringConcat(leftStr, rightStr);
		}

		mir::MIRValue *left = generateExpr(binExpr->left.get());
		mir::MIRValue *right = generateExpr(binExpr->right.get());

		if (!left || !right) {
			throw std::runtime_error("Failed to generate binary expression operands");
		}

		bool leftIsFloat = left->type->isFloat();
		bool rightIsFloat = right->type->isFloat();
		bool needsFloatOp = leftIsFloat || rightIsFloat;

		// Promote int to float if mixed types
		if (needsFloatOp) {
			if (!leftIsFloat) {
				left = builder->createSIToFP(left, mir::MIRType::getFloat64(), "promotetmp");
			}
			if (!rightIsFloat) {
				right = builder->createSIToFP(right, mir::MIRType::getFloat64(), "promotetmp");
			}
		}

		switch (binExpr->op) {
		case '+':
			return needsFloatOp ? builder->createFAdd(left, right, "faddtmp")
								: builder->createAdd(left, right, "addtmp");
		case '-':
			return needsFloatOp ? builder->createFSub(left, right, "fsubtmp")
								: builder->createSub(left, right, "subtmp");
		case '*':
			return needsFloatOp ? builder->createFMul(left, right, "fmultmp")
								: builder->createMul(left, right, "multmp");
		case '/':
			return needsFloatOp ? builder->createFDiv(left, right, "fdivtmp")
								: builder->createSDiv(left, right, "divtmp");
		case '%':
			return builder->createSRem(left, right, "modtmp");
		case '>':
			return needsFloatOp ? builder->createFCmpGT(left, right, "fcmptmp")
								: builder->createICmpSGT(left, right, "cmptmp");
		case '<':
			return needsFloatOp ? builder->createFCmpLT(left, right, "fcmptmp")
								: builder->createICmpSLT(left, right, "cmptmp");
		case 'G': // >=
			return needsFloatOp ? builder->createFCmpGE(left, right, "fcmptmp")
								: builder->createICmpSGE(left, right, "cmptmp");
		case 'L': // <=
			return needsFloatOp ? builder->createFCmpLE(left, right, "fcmptmp")
								: builder->createICmpSLE(left, right, "cmptmp");
		case 'E': // ==
			return needsFloatOp ? builder->createFCmpEq(left, right, "fcmptmp")
								: builder->createICmpEq(left, right, "cmptmp");
		case 'N': // !=
			return needsFloatOp ? builder->createFCmpNe(left, right, "fcmptmp")
								: builder->createICmpNe(left, right, "cmptmp");
		case '&': // logical and
			// Both operands should be booleans (i1 or i32)
			// Result is 1 if both are non-zero, 0 otherwise
			return builder->createAnd(left, right, "andtmp");
		case '|': // logical or
			// Result is 1 if either is non-zero, 0 otherwise
			return builder->createOr(left, right, "ortmp");
		default:
			throw std::runtime_error("Unknown binary operator");
		}
	}

	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		mir::MIRValue *operand = generateExpr(unaryExpr->operand.get());
		if (!operand) {
			throw std::runtime_error("Failed to generate unary expression operand");
		}

		bool isFloat = operand->type->isFloat();

		switch (unaryExpr->op) {
		case '-':
			if (isFloat) {
				return builder->createFNeg(operand, "fnegtmp");
			} else {
				return builder->createNeg(operand, "negtmp");
			}
		case '+':
			return operand;
		case '!': // logical not
			// Result is 1 if operand is 0, 0 otherwise
			return builder->createICmpEq(operand,
										 mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0), "nottmp");
		default:
			throw std::runtime_error("Unknown unary operator");
		}
	}

	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Handle math intrinsics
		if (Lexer::isMathIntrinsic(callExpr->callee)) {
			return generateMathIntrinsic(callExpr);
		}

		// Handle array intrinsics (push, pop)
		if (callExpr->callee == "push" || callExpr->callee == "pop") {
			return generateArrayIntrinsic(callExpr);
		}

		// Handle print() with string argument - call __string_print runtime function
		if (callExpr->callee == "print" && callExpr->args.size() == 1 &&
			isStringExpression(callExpr->args[0].get())) {
			mir::MIRValue *strPtr = generateStringOperand(callExpr->args[0].get());

			// Declare __string_print if not already declared
			mir::MIRFunction *strPrintFunc = module->getFunction("__string_print");
			if (!strPrintFunc) {
				std::vector<mir::MIRType *> paramTypes = {mir::MIRType::getPtr()};
				strPrintFunc = builder->declareFunction("__string_print", mir::MIRType::getInt32(), paramTypes);
			}

			// __string_print calls write_stdout internally, so declare it
			initStandardLibrary();

			std::vector<mir::MIRValue *> args = {strPtr};
			return builder->createCall(strPrintFunc, args, "strprint");
		}

		// Initialize standard library if needed
		if (callExpr->callee == "print" || callExpr->callee == "print_float") {
			initStandardLibrary();
		}

		// Check if this is an extern function that should go through Safe FFI
		bool usesSafeFfi = externFunctions.find(callExpr->callee) != externFunctions.end();

		// Look up function
		mir::MIRFunction *calleeF = module->getFunction(callExpr->callee);

		// If not found, try suffix matching for namespaced functions
		if (!calleeF && callExpr->callee.find(".") == std::string::npos) {
			std::string searchSuffix = "." + callExpr->callee;
			for (auto &func : module->functions) {
				const std::string &funcName = func->name;
				if (funcName.size() > searchSuffix.size() &&
					funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
					calleeF = func.get();
					// Also check if namespaced version is extern
					if (externFunctions.find(funcName) != externFunctions.end()) {
						usesSafeFfi = true;
					}
					break;
				}
			}
		}

		if (!calleeF) {
			throw std::runtime_error("Unknown function referenced: " + callExpr->callee);
		}

		// Generate arguments
		std::vector<mir::MIRValue *> argsV;
		size_t argIdx = 0;

		for (size_t i = 0; i < callExpr->args.size(); i++) {
			auto &arg = callExpr->args[i];
			mir::MIRType *paramType = (argIdx < calleeF->parameters.size())
										  ? calleeF->parameters[argIdx]->type
										  : mir::MIRType::getInt32();

			// Handle array and struct arguments
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg.get())) {
				std::string varType = variableTypes[varExpr->name];
				mir::MIRValue *alloca = namedValues[varExpr->name];

				if (alloca && isArrayParam(varType)) {
					// Pass array pointer and length
					mir::MIRValue *ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
					argsV.push_back(ptrVal);
					argIdx++;

					// Pass hidden length parameter if expected by the callee
					// Check if the callee's next parameter is a hidden length param (name ends with ".__length")
					if (argIdx < calleeF->parameters.size()) {
						mir::MIRValue *nextParam = calleeF->parameters[argIdx];
						bool expectsHiddenLength = !nextParam->name.empty() &&
												   nextParam->name.size() > 9 &&
												   nextParam->name.substr(nextParam->name.size() - 9) == ".__length";

						if (expectsHiddenLength) {
							std::string lengthVarName = varExpr->name + ".__length";
							if (namedValues.find(lengthVarName) != namedValues.end()) {
								mir::MIRValue *lengthAlloca = namedValues[lengthVarName];
								mir::MIRValue *lengthVal = builder->createLoad(
									mir::MIRType::getInt32(), lengthAlloca, "length");
								argsV.push_back(lengthVal);
								argIdx++;
							}
						}
					}
					continue;
				}

				// Handle struct arguments - pass pointer instead of value
				if (alloca && structTypes.find(varType) != structTypes.end()) {
					// If this is a struct parameter, it's already a pointer - load it
					// Otherwise it's a local struct variable - pass the alloca pointer
					if (isStructParameter(varExpr->name)) {
						mir::MIRValue *ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
						argsV.push_back(ptrVal);
					} else {
						argsV.push_back(alloca);
					}
					argIdx++;
					continue;
				}
			}

			// Normal argument
			mir::MIRValue *argVal = generateExpr(arg.get());
			if (!argVal) {
				throw std::runtime_error("Failed to generate function argument");
			}

			// Type promotion if needed
			if (paramType->isFloat() && argVal->type->isInteger()) {
				argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "inttofp");
			}

			argsV.push_back(argVal);
			argIdx++;
		}

		// Use Safe FFI for all extern functions (both DLLs and static libs)
		// Static libs are linked into the worker executable, DLLs are loaded dynamically
		if (usesSafeFfi) {
			return generateSafeFFICall(calleeF->name, argsV, calleeF->returnType);
		}
		return builder->createCall(calleeF, argsV);
	}

	throw std::runtime_error("Unknown expression type");
}

//==============================================================================
// Math Intrinsic Generation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMathIntrinsic(CallExprAST *callExpr) {
	const std::string &name = callExpr->callee;

	// Declare the math function if not already declared
	mir::MIRFunction *mathFunc = nullptr;

	// Rounding functions (trunc, floor, ceil, round) return int in Maxon
	// But the underlying C library functions return float, so we need to convert
	bool isRoundingFunction = (name == "trunc" || name == "floor" ||
							   name == "ceil" || name == "round");

	if (name == "sin" || name == "cos" || name == "tan" ||
		name == "sqrt" || name == "floor" || name == "ceil" ||
		name == "round" || name == "trunc" || name == "abs") {
		mathFunc = getOrDeclareFunction(name, mir::MIRType::getFloat64(),
										{mir::MIRType::getFloat64()});
	} else if (name == "pow" || name == "fmod") {
		mathFunc = getOrDeclareFunction(name, mir::MIRType::getFloat64(),
										{mir::MIRType::getFloat64(), mir::MIRType::getFloat64()});
	} else {
		throw std::runtime_error("Unknown math intrinsic: " + name);
	}

	// Generate arguments
	std::vector<mir::MIRValue *> args;
	for (auto &arg : callExpr->args) {
		mir::MIRValue *argVal = generateExpr(arg.get());
		// Promote int to float
		if (argVal->type->isInteger()) {
			argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "promotetmp");
		}
		args.push_back(argVal);
	}

	mir::MIRValue *result = builder->createCall(mathFunc, args);

	// Rounding functions return int in Maxon, convert from float
	if (isRoundingFunction) {
		result = builder->createFPToSI(result, mir::MIRType::getInt32(), "fptositmp");
	}

	return result;
}

mir::MIRValue *MIRCodeGenerator::generateArrayIntrinsic(CallExprAST *callExpr) {
	// Array intrinsics: push(arr, value), pop(arr)
	// These are generated from method syntax: arr.push(value), arr.pop()
	// The operations are inlined directly rather than calling runtime functions.

	if (callExpr->callee == "push") {
		// push(arr, value) - requires arr to be a dynamic (var) array
		// Inline implementation:
		//   1. Load length and capacity
		//   2. If length >= capacity, grow array (realloc with 2x capacity)
		//   3. Store value at arr[length]
		//   4. Increment length

		if (callExpr->args.size() != 2) {
			throw std::runtime_error("push() requires exactly 2 arguments: array and value");
		}

		// First arg must be an array variable
		auto *arrVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
		if (!arrVar) {
			throw std::runtime_error("push() first argument must be an array variable");
		}

		std::string arrName = arrVar->name;
		std::string arrType = variableTypes[arrName];

		// Check it's a dynamic array (has capacity)
		mir::MIRValue *capacityAlloca = namedValues[arrName + ".__capacity"];
		if (!capacityAlloca) {
			throw std::runtime_error("push() can only be used on dynamic (var) arrays, not static (let) arrays");
		}

		// Get array's internal allocas
		mir::MIRValue *arrAlloca = namedValues[arrName];
		mir::MIRValue *lengthAlloca = namedValues[arrName + ".__length"];

		if (!arrAlloca || !lengthAlloca) {
			throw std::runtime_error("Array variable not found: " + arrName);
		}

		// Generate the value to push
		mir::MIRValue *value = generateExpr(callExpr->args[1].get());
		if (!value) {
			throw std::runtime_error("Failed to generate value for push()");
		}

		// Determine element type and size
		std::string elemTypeStr = "int";
		if (arrType.size() > 2 && arrType[0] == '[') {
			size_t closeBracket = arrType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < arrType.size()) {
				elemTypeStr = arrType.substr(closeBracket + 1);
			}
		}

		mir::MIRType *elemType = getTypeFromString(elemTypeStr);
		int elemSize = static_cast<int>(elemType->sizeInBytes);

		// Load current values
		mir::MIRValue *arrPtr = builder->createLoad(mir::MIRType::getPtr(), arrAlloca, "arr.ptr");
		mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
		mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");

		// Create basic blocks for the growth check
		mir::MIRFunction *currentFunc = builder->getFunction();
		mir::MIRBasicBlock *growBlock = currentFunc->createBasicBlock("push.grow");
		mir::MIRBasicBlock *storeBlock = currentFunc->createBasicBlock("push.store");

		// Check if we need to grow: length >= capacity
		mir::MIRValue *needGrow = builder->createICmpSGE(length, capacity, "need.grow");
		builder->createCondBr(needGrow, growBlock, storeBlock);

		// Grow block: double capacity and realloc
		builder->setInsertPoint(growBlock);

		// Calculate new capacity: capacity * 2, but at least 4
		// We need to handle zero capacity specially
		mir::MIRValue *doubledCap = builder->createMul(capacity, builder->getInt32(2), "doubled.cap");

		// If capacity is 0, use 4 instead
		// We do this with: newCap = doubled + 4, then if doubled > 0, newCap = doubled
		// Simpler: branch on isZero
		mir::MIRBasicBlock *initCapBlock = currentFunc->createBasicBlock("push.initcap");
		mir::MIRBasicBlock *doubleCapBlock = currentFunc->createBasicBlock("push.doublecap");
		mir::MIRBasicBlock *doReallocBlock = currentFunc->createBasicBlock("push.dorealloc");

		mir::MIRValue *isZero = builder->createICmpEq(capacity, builder->getInt32(0), "is.zero");
		builder->createCondBr(isZero, initCapBlock, doubleCapBlock);

		// Init cap block: use 4
		builder->setInsertPoint(initCapBlock);
		builder->createBr(doReallocBlock);

		// Double cap block: use doubled
		builder->setInsertPoint(doubleCapBlock);
		builder->createBr(doReallocBlock);

		// Do realloc block: phi for new capacity
		builder->setInsertPoint(doReallocBlock);
		mir::MIRValue *newCapacity = builder->createPhi(mir::MIRType::getInt32(), "new.capacity");
		builder->addPhiIncoming(newCapacity, builder->getInt32(4), initCapBlock);
		builder->addPhiIncoming(newCapacity, doubledCap, doubleCapBlock);

		// Calculate old and new sizes
		mir::MIRValue *elemSizeVal = builder->getInt64(elemSize);
		mir::MIRValue *capacity64 = builder->createSExt(capacity, mir::MIRType::getInt64(), "cap64");
		mir::MIRValue *newCapacity64 = builder->createSExt(newCapacity, mir::MIRType::getInt64(), "newcap64");
		mir::MIRValue *oldSize = builder->createMul(capacity64, elemSizeVal, "old.size");
		mir::MIRValue *newSize = builder->createMul(newCapacity64, elemSizeVal, "new.size");

		// Call realloc(ptr, old_size, new_size)
		mir::MIRFunction *reallocFunc = getOrDeclareFunction("realloc", mir::MIRType::getPtr(),
															 {mir::MIRType::getPtr(), mir::MIRType::getInt64(), mir::MIRType::getInt64()});
		mir::MIRValue *newArrPtr = builder->createCall(reallocFunc, {arrPtr, oldSize, newSize}, "new.arr");

		// Store new pointer and capacity
		builder->createStore(newArrPtr, arrAlloca);
		builder->createStore(newCapacity, capacityAlloca);
		builder->createBr(storeBlock);

		// Store block: store value at arr[length]
		// Reload array pointer from alloca (may have been updated by realloc)
		builder->setInsertPoint(storeBlock);
		mir::MIRValue *finalArrPtr = builder->createLoad(mir::MIRType::getPtr(), arrAlloca, "arr.final");

		// Calculate element pointer and store
		mir::MIRValue *length64 = builder->createSExt(length, mir::MIRType::getInt64(), "len64");
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, finalArrPtr, length64, "elem.ptr");
		builder->createStore(value, elemPtr);

		// Increment length
		mir::MIRValue *newLength = builder->createAdd(length, builder->getInt32(1), "new.length");
		builder->createStore(newLength, lengthAlloca);

		// push returns void, but we return a dummy value
		return builder->getInt32(0);

	} else if (callExpr->callee == "pop") {
		// pop(arr) - returns the popped value
		// Inline implementation:
		//   1. Decrement length
		//   2. Load and return value at arr[new_length]

		if (callExpr->args.size() != 1) {
			throw std::runtime_error("pop() requires exactly 1 argument: array");
		}

		// First arg must be an array variable
		auto *arrVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
		if (!arrVar) {
			throw std::runtime_error("pop() argument must be an array variable");
		}

		std::string arrName = arrVar->name;
		std::string arrType = variableTypes[arrName];

		// Check it's a dynamic array
		mir::MIRValue *capacityAlloca = namedValues[arrName + ".__capacity"];
		if (!capacityAlloca) {
			throw std::runtime_error("pop() can only be used on dynamic (var) arrays, not static (let) arrays");
		}

		// Get array's internal allocas
		mir::MIRValue *arrAlloca = namedValues[arrName];
		mir::MIRValue *lengthAlloca = namedValues[arrName + ".__length"];

		if (!arrAlloca || !lengthAlloca) {
			throw std::runtime_error("Array variable not found: " + arrName);
		}

		// Determine element type
		std::string elemTypeStr = "int";
		if (arrType.size() > 2 && arrType[0] == '[') {
			size_t closeBracket = arrType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < arrType.size()) {
				elemTypeStr = arrType.substr(closeBracket + 1);
			}
		}

		mir::MIRType *elemType = getTypeFromString(elemTypeStr);

		// Load current length and decrement
		mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
		mir::MIRValue *newLength = builder->createSub(length, builder->getInt32(1), "new.length");
		builder->createStore(newLength, lengthAlloca);

		// Load array pointer and get element at new_length
		mir::MIRValue *arrPtr = builder->createLoad(mir::MIRType::getPtr(), arrAlloca, "arr.ptr");
		mir::MIRValue *newLength64 = builder->createSExt(newLength, mir::MIRType::getInt64(), "newlen64");
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, arrPtr, newLength64, "elem.ptr");

		// Load and return the value
		return builder->createLoad(elemType, elemPtr, "pop.result");
	}

	throw std::runtime_error("Unknown array intrinsic: " + callExpr->callee);
}
