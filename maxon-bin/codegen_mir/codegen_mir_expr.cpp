/**
 * MIR Code Generator - Expression Generation
 *
 * This file implements expression code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../lexer.h"
#include <cmath>
#include <stdexcept>

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return builder->getInt32(numExpr->value);
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
		// Create a global constant for the string
		mir::MIRGlobal *strGlobal = builder->createStringConstant(".str", strExpr->value);
		// Return reference to the global (pointer to first char)
		return mir::MIRValue::createGlobal(mir::MIRType::getPtr(), strGlobal->name);
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

		// Pointer to pointer
		if (sourceType->isPointer() && targetType->isPointer()) {
			return builder->createBitcast(value, targetType, "ptrcasttmp");
		}

		throw std::runtime_error("Unsupported cast");
	}

	if (auto *addrExpr = dynamic_cast<AddressOfExprAST *>(expr)) {
		mir::MIRValue *alloca = namedValues[addrExpr->varName];
		if (!alloca) {
			throw std::runtime_error("Unknown variable name: " + addrExpr->varName);
		}
		return alloca; // Alloca is already a pointer
	}

	if (auto *derefExpr = dynamic_cast<DerefExprAST *>(expr)) {
		mir::MIRValue *ptr = generateExpr(derefExpr->expr.get());
		if (!ptr) {
			throw std::runtime_error("Failed to generate pointer expression for dereference");
		}
		// Default to loading i32
		return builder->createLoad(mir::MIRType::getInt32(), ptr, "dereftmp");
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

		// For arrays, first load the pointer, then GEP
		mir::MIRValue *arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");

		if (isStructElement) {
			return elementPtr; // Return pointer for struct elements
		} else {
			return builder->createLoad(elementType, elementPtr, "arrayelem");
		}
	}

	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
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

		// Initialize standard library if needed
		if (callExpr->callee == "print" || callExpr->callee == "print_float") {
			initStandardLibrary();
		}

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

					// Pass hidden length parameter if expected
					if (argIdx < calleeF->parameters.size()) {
						std::string lengthVarName = varExpr->name + ".__length";
						if (namedValues.find(lengthVarName) != namedValues.end()) {
							mir::MIRValue *lengthAlloca = namedValues[lengthVarName];
							mir::MIRValue *lengthVal = builder->createLoad(
								mir::MIRType::getInt32(), lengthAlloca, "length");
							argsV.push_back(lengthVal);
							argIdx++;
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

	return builder->createCall(mathFunc, args);
}
