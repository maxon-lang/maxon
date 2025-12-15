/**
 * MIR Code Generator - Variable and Let Declaration Statements
 *
 * This file implements code generation for var and let declarations.
 * Common logic is extracted into shared helper methods (tryGenerate*Decl)
 * to reduce duplication between generateVarDecl and generateLetDecl.
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <algorithm>
#include <stdexcept>

// ============================================================================
// Shared Declaration Helpers
// These handle common initialization patterns for both var and let declarations
// ============================================================================

bool MIRCodeGenerator::tryGenerateArrayLiteralDecl(const DeclInfo &decl) {
	auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(decl.initializer);
	if (!arrayLiteral) {
		return false;
	}

	mir::MIRType *elementType;
	std::string elementTypeName;
	std::vector<mir::MIRValue *> initValues;
	int constantArraySize = static_cast<int>(arrayLiteral->values.size());

	for (auto &valExpr : arrayLiteral->values) {
		mir::MIRValue *val = generateExpr(valExpr.get());
		if (!val) {
			reportError("Failed to generate array element value", decl.line, decl.column);
		}
		initValues.push_back(val);
	}

	// Infer element type from first value
	elementType = initValues[0]->type;
	if (elementType->kind == mir::MIRTypeKind::Int64) {
		elementTypeName = "int";
	} else if (elementType->kind == mir::MIRTypeKind::Float64) {
		elementTypeName = "float";
	} else if (elementType->kind == mir::MIRTypeKind::Int8) {
		elementTypeName = "character";
	} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
		elementTypeName = "ptr";
	} else {
		reportError("Unsupported array element type", decl.line, decl.column);
	}

	uint64_t elementSize = elementType->sizeInBytes;
	mir::MIRValue *arrayPtr;
	uint64_t totalSize = constantArraySize * elementSize;

	// Threshold for stack allocation (4KB)
	constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
	bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

	if (useHeap) {
		// Large array: heap-allocate using array<T> struct layout
		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");

		// Allocate data buffer
		mir::MIRValue *sizeVal = builder->getInt64(totalSize);
		mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {sizeVal}, decl.name + ".buffer");

		// Create array<T> struct alloca
		mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
		mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, decl.name);

		// Get __ManagedArrayData type for the nested managed field
		mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

		// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
		mir::MIRValue *lengthVal = builder->getInt64(constantArraySize);

		// Field 0: managed (nested __ManagedArrayData struct)
		mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, decl.name + ".managed");

		// Initialize the nested __ManagedArrayData fields
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, decl.name + ".managed._buffer");
		builder->createStore(bufferPtr, bufferField);

		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, decl.name + ".managed._len");
		builder->createStore(lengthVal, lenField);

		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, decl.name + ".managed._capacity");
		builder->createStore(lengthVal, capField);

		// Field 1: iterIndex
		mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, decl.name + ".iterIndex");
		builder->createStore(builder->getInt64(0), iterField);

		namedValues[decl.name] = structAlloca;
		variableTypes[decl.name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
		arrayPtr = bufferPtr;

		// Track array struct for cleanup
		// var: isDynamic=true (buffer can be reallocated), let: isDynamic=false
		if (!scopeStack.empty()) {
			scopeStack.back().heapAllocatedArrays.push_back({decl.name, structAlloca, elementTypeName, decl.isMutable});
		}
	} else {
		// Small array: stack-allocate buffer but use array<T> struct wrapper
		mir::MIRType *rawArrayType = mir::MIRType::getArray(elementType, constantArraySize);
		mir::MIRValue *stackBuffer = builder->createAlloca(rawArrayType, decl.name + ".stack_buffer");

		// Create array<T> struct alloca
		mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
		mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, decl.name);

		// Get __ManagedArrayData type for the nested managed field
		mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

		// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
		// capacity = 0 signals stack-allocated buffer (can't grow for let, may promote for var)
		mir::MIRValue *arraySizeVal = builder->getInt64(constantArraySize);
		mir::MIRValue *zeroCapacity = builder->getInt64(0);

		// Field 0: managed (nested __ManagedArrayData struct)
		mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, decl.name + ".managed");

		// Initialize the nested __ManagedArrayData fields
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, decl.name + ".managed._buffer");
		builder->createStore(stackBuffer, bufferField);

		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, decl.name + ".managed._len");
		builder->createStore(arraySizeVal, lenField);

		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, decl.name + ".managed._capacity");
		builder->createStore(zeroCapacity, capField);

		// Field 1: iterIndex
		mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, decl.name + ".iterIndex");
		builder->createStore(builder->getInt64(0), iterField);

		namedValues[decl.name] = structAlloca;
		variableTypes[decl.name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
		arrayPtr = stackBuffer;

		// Track array struct for cleanup
		// var: isDynamic=true (buffer could be promoted to heap by push()), let: no tracking needed
		if (decl.isMutable && !scopeStack.empty()) {
			scopeStack.back().heapAllocatedArrays.push_back({decl.name, structAlloca, elementTypeName, true});
		}
	}

	// Initialize array elements
	for (int i = 0; i < constantArraySize; i++) {
		mir::MIRValue *indexVal = builder->getInt64(i);
		mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
		builder->createStore(initValues[i], elementPtr);
	}

	return true;
}

bool MIRCodeGenerator::tryGenerateSizedArrayDecl(const DeclInfo &decl) {
	auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(decl.initializer);
	if (!sizedArray) {
		return false;
	}

	std::string elementTypeName = sizedArray->elementType;

	// Substitute type parameters if we're inside a generic method
	if (!currentTypeBindings.empty()) {
		auto bindingIt = currentTypeBindings.find(elementTypeName);
		if (bindingIt != currentTypeBindings.end()) {
			elementTypeName = bindingIt->second;
		}
	}

	mir::MIRType *elementType = getTypeFromString(elementTypeName);
	uint64_t elementSize = elementType->sizeInBytes;

	// Handle variable-sized arrays (size is an expression)
	if (sizedArray->hasVariableSize()) {
		// Generate the size expression
		mir::MIRValue *sizeVal = generateExpr(sizedArray->sizeExpr.get());
		if (!sizeVal) {
			reportError("Failed to generate array size expression", decl.line, decl.column);
		}

		// Variable-sized arrays always use heap allocation with array<T> struct layout
		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");

		// Calculate total size for data buffer: size * elementSize
		mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
		mir::MIRValue *sizeExt = builder->createSExt(sizeVal, mir::MIRType::getInt64(), "size.ext");
		mir::MIRValue *dataSize = builder->createMul(sizeExt, elemSizeVal, "data.size");

		// Allocate buffer (no header - array<T> struct stores metadata separately)
		mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {dataSize}, decl.name + ".buffer");

		// Create array<T> struct alloca
		mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
		mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, decl.name);

		// Get __ManagedArrayData type for the nested managed field
		mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

		// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
		// Field 0: managed (nested __ManagedArrayData struct)
		mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, decl.name + ".managed");

		// Initialize the nested __ManagedArrayData fields
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, decl.name + ".managed._buffer");
		builder->createStore(bufferPtr, bufferField);

		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, decl.name + ".managed._len");
		builder->createStore(sizeVal, lenField);

		// capacity > 0 means heap-allocated
		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, decl.name + ".managed._capacity");
		builder->createStore(sizeVal, capField);

		// Field 1: iterIndex
		mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, decl.name + ".iterIndex");
		builder->createStore(builder->getInt64(0), iterField);

		namedValues[decl.name] = structAlloca;
		variableTypes[decl.name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);

		// Zero-initialize using memset
		mir::MIRFunction *memsetFunc = module->getFunction("memset");
		if (!memsetFunc) {
			initHeapManagement();
			memsetFunc = module->getFunction("memset");
		}
		mir::MIRValue *zeroVal = builder->getInt64(0);
		builder->createCall(memsetFunc, {bufferPtr, zeroVal, dataSize});

		// Track array struct for cleanup
		// var: isDynamic=true (buffer can be reallocated), let: isDynamic=false
		if (!scopeStack.empty()) {
			scopeStack.back().heapAllocatedArrays.push_back({decl.name, structAlloca, elementTypeName, decl.isMutable});
		}

		return true;
	}

	// Constant-sized array - use array<T> struct layout
	int arraySize = sizedArray->size;
	uint64_t totalSize = arraySize * elementSize;

	// Threshold for stack allocation (4KB)
	constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
	bool useHeap = totalSize > STACK_ARRAY_THRESHOLD || (decl.isMutable && arraySize == 0);

	// Create array<T> struct alloca
	mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
	mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, decl.name);

	// Get __ManagedArrayData type for the nested managed field
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

	mir::MIRValue *bufferPtr;

	if (useHeap) {
		// Large array: heap-allocate buffer
		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");

		// Allocate data buffer (no header - array<T> struct stores metadata)
		uint64_t allocSize = (arraySize > 0) ? totalSize : (decl.isMutable ? 8 : 1);
		mir::MIRValue *sizeVal = builder->getInt64(allocSize);
		bufferPtr = builder->createCall(mallocFunc, {sizeVal}, decl.name + ".buffer");

		// Initialize nested struct layout with capacity > 0 (heap-allocated)
		mir::MIRValue *lengthVal = builder->getInt64(arraySize);

		// Field 0: managed
		mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, decl.name + ".managed");

		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, decl.name + ".managed._buffer");
		builder->createStore(bufferPtr, bufferField);

		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, decl.name + ".managed._len");
		builder->createStore(lengthVal, lenField);

		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, decl.name + ".managed._capacity");
		builder->createStore(lengthVal, capField); // capacity = length for fixed-size

		// Field 1: iterIndex
		mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, decl.name + ".iterIndex");
		builder->createStore(builder->getInt64(0), iterField);

		// Track array struct for cleanup
		if (!scopeStack.empty()) {
			scopeStack.back().heapAllocatedArrays.push_back({decl.name, structAlloca, elementTypeName, decl.isMutable});
		}
	} else {
		// Small array: stack-allocate buffer
		mir::MIRType *mirArrayType = mir::MIRType::getArray(elementType, arraySize);
		bufferPtr = builder->createAlloca(mirArrayType, decl.name + ".stack_buffer");

		// Initialize nested struct layout with capacity = 0 (stack-allocated)
		mir::MIRValue *arraySizeVal = builder->getInt64(arraySize);
		mir::MIRValue *zeroCapacity = builder->getInt64(0);

		// Field 0: managed
		mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, decl.name + ".managed");

		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, decl.name + ".managed._buffer");
		builder->createStore(bufferPtr, bufferField);

		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, decl.name + ".managed._len");
		builder->createStore(arraySizeVal, lenField);

		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, decl.name + ".managed._capacity");
		builder->createStore(zeroCapacity, capField);

		// Field 1: iterIndex
		mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, decl.name + ".iterIndex");
		builder->createStore(builder->getInt64(0), iterField);

		// Track for cleanup only if mutable (may get promoted to heap)
		if (decl.isMutable && !scopeStack.empty()) {
			scopeStack.back().heapAllocatedArrays.push_back({decl.name, structAlloca, elementTypeName, true});
		}
	}

	namedValues[decl.name] = structAlloca;
	variableTypes[decl.name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);

	// Zero-initialize buffer using memset
	if (arraySize > 0) {
		mir::MIRFunction *memsetFunc = module->getFunction("memset");
		if (!memsetFunc) {
			initHeapManagement();
			memsetFunc = module->getFunction("memset");
		}
		mir::MIRValue *zeroVal = builder->getInt64(0);
		mir::MIRValue *memsetSizeVal = builder->getInt64(totalSize);
		builder->createCall(memsetFunc, {bufferPtr, zeroVal, memsetSizeVal});
	}

	return true;
}

bool MIRCodeGenerator::tryGenerateStringLiteralDecl(const DeclInfo &decl) {
	auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(decl.initializer);
	if (!strLiteral) {
		return false;
	}

	mir::MIRValue *stringAlloca = generateStringLiteral(strLiteral);
	stringAlloca->name = decl.name;

	namedValues[decl.name] = stringAlloca;
	variableTypes[decl.name] = "string";

	// Track the string variable for cleanup at scope exit (var only)
	// We track the alloca so cleanup reads the CURRENT buffer pointer
	// This handles reassignment where the variable may hold heap data later
	if (decl.isMutable && !scopeStack.empty()) {
		scopeStack.back().stringVariables.push_back({decl.name, stringAlloca});
	}

	return true;
}

bool MIRCodeGenerator::tryGenerateCharLiteralDecl(const DeclInfo &decl) {
	auto *charLiteral = dynamic_cast<CharacterExprAST *>(decl.initializer);
	if (!charLiteral) {
		return false;
	}

	mir::MIRValue *charAlloca = generateCharLiteral(charLiteral);
	charAlloca->name = decl.name;

	namedValues[decl.name] = charAlloca;
	variableTypes[decl.name] = "character";
	return true;
}

bool MIRCodeGenerator::tryGenerateInterpolatedStringDecl(const DeclInfo &decl) {
	auto *interpExpr = dynamic_cast<InterpolatedStringExprAST *>(decl.initializer);
	if (!interpExpr) {
		return false;
	}

	mir::MIRValue *stringAlloca = generateInterpolatedString(interpExpr);
	stringAlloca->name = decl.name;

	namedValues[decl.name] = stringAlloca;
	variableTypes[decl.name] = "string";

	// Track for cleanup
	if (decl.isMutable && !scopeStack.empty()) {
		// var: track the variable itself for cleanup
		scopeStack.back().stringVariables.push_back({decl.name, stringAlloca});
	} else if (!decl.isMutable && !scopeStack.empty()) {
		// let: track the specific buffer for cleanup
		mir::MIRType *stringType = structTypes["string"];
		mir::MIRType *managedStringDataType = structTypes["__ManagedStringData"];
		mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

		mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, stringAlloca, 0, decl.name + ".managed.field");
		mir::MIRValue *managedPtr = builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, decl.name + ".managed.ptr");
		mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringDataType, managedPtr, 0, decl.name + ".buffer.field");
		mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, decl.name + ".buffer.ptr.ptr");
		mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, decl.name + ".buffer.ptr");

		mir::MIRValue *trackAlloca = builder->createAlloca(mir::MIRType::getPtr(), decl.name + ".track.ptr");
		builder->createStore(bufferPtr, trackAlloca);
		scopeStack.back().heapAllocatedStrings.push_back({decl.name + ".interp", trackAlloca});
	}

	return true;
}

bool MIRCodeGenerator::tryGenerateStringConcatDecl(const DeclInfo &decl) {
	auto *binExpr = dynamic_cast<BinaryExprAST *>(decl.initializer);
	if (!binExpr || !isStringConcatExpr(binExpr)) {
		return false;
	}

	mir::MIRValue *stringAlloca = generateExpr(decl.initializer);
	stringAlloca->name = decl.name;

	namedValues[decl.name] = stringAlloca;
	variableTypes[decl.name] = "string";
	return true;
}

bool MIRCodeGenerator::tryGenerateArrayIndexStructDecl(const DeclInfo &decl) {
	auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(decl.initializer);
	if (!arrayIndexExpr) {
		return false;
	}

	// Generate the expression first to get the pointer
	mir::MIRValue *initVal = generateExpr(decl.initializer);
	if (!initVal) {
		reportError("Failed to generate array index expression", decl.line, decl.column);
	}

	std::string arrayName;
	if (!arrayIndexExpr->arrayName.empty()) {
		arrayName = arrayIndexExpr->arrayName;
	} else if (arrayIndexExpr->arrayExpr) {
		if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayIndexExpr->arrayExpr.get())) {
			arrayName = varExpr->name;
		}
	}
	if (!arrayName.empty()) {
		std::string arrayType = variableTypes[arrayName];
		std::string elementTypeStr;
		if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
			elementTypeStr = maxon::TypeConversion::getArrayElementType(arrayType);
		} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
			elementTypeStr = maxon::TypeConversion::getArrayStructElementType(arrayType);
		} else if (arrayType.rfind("array of ", 0) == 0) {
			// Handle "array of T" parameter format (e.g., "array of string")
			elementTypeStr = arrayType.substr(9); // Skip "array of "
		}
		// If element type is a struct, we need to load the value from the pointer
		if (!elementTypeStr.empty() && structTypes.find(elementTypeStr) != structTypes.end()) {
			mir::MIRType *structType = structTypes[elementTypeStr];
			mir::MIRValue *varAlloca = builder->createAlloca(structType, decl.name);
			mir::MIRValue *loadedVal = builder->createLoad(structType, initVal, decl.name + ".tmp");
			builder->createStore(loadedVal, varAlloca);
			namedValues[decl.name] = varAlloca;
			variableTypes[decl.name] = elementTypeStr;
			return true;
		}
	}

	// Not a struct element - fall through to normal handling
	return false;
}

void MIRCodeGenerator::generateSimpleDecl(const DeclInfo &decl, mir::MIRValue *initVal) {
	mir::MIRType *allocaType;
	if (!decl.type.empty()) {
		allocaType = getTypeFromString(decl.type);
	} else if (initVal) {
		allocaType = initVal->type;
	} else {
		allocaType = mir::MIRType::getInt64();
	}

	mir::MIRValue *alloca = builder->createAlloca(allocaType, decl.name);

	if (initVal) {
		builder->createStore(initVal, alloca);
	} else {
		// Zero-initialize
		mir::MIRValue *zeroVal;
		if (allocaType->isInteger()) {
			zeroVal = builder->getInt64(0);
		} else if (allocaType->isFloat()) {
			zeroVal = builder->getFloat64(0.0);
		} else {
			zeroVal = builder->getNull();
		}
		builder->createStore(zeroVal, alloca);
	}

	namedValues[decl.name] = alloca;
	trackDeclTypeInfo(decl, alloca, allocaType);
}

void MIRCodeGenerator::trackDeclTypeInfo(const DeclInfo &decl, mir::MIRValue *alloca, mir::MIRType *allocaType) {
	// If type was explicitly specified, use it; otherwise derive from the alloca type
	std::string finalType;

	if (!decl.type.empty()) {
		// Substitute type parameters if we're in a generic method context
		std::string explicitType = decl.type;
		auto bindingIt = currentTypeBindings.find(explicitType);
		if (bindingIt != currentTypeBindings.end()) {
			explicitType = bindingIt->second;
		}
		finalType = explicitType;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		finalType = allocaType->structName;
	} else if (allocaType->kind == mir::MIRTypeKind::Ptr && decl.initializer) {
		// For pointer types from array indexing, try to get the element type from the array
		std::string derivedType = "ptr";
		if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(decl.initializer)) {
			std::string arrayName;
			if (!arrayIndexExpr->arrayName.empty()) {
				arrayName = arrayIndexExpr->arrayName;
			} else if (arrayIndexExpr->arrayExpr) {
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayIndexExpr->arrayExpr.get())) {
					arrayName = varExpr->name;
				}
			}
			if (!arrayName.empty()) {
				std::string arrayType = variableTypes[arrayName];
				if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
					derivedType = maxon::TypeConversion::getArrayElementType(arrayType);
				} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
					derivedType = maxon::TypeConversion::getArrayStructElementType(arrayType);
				}
			}
		}
		finalType = derivedType;
	} else if (decl.initializer) {
		// Try to get type from initializer expression
		// For cast expressions (e.g., "... as Value"), use the cast target type
		if (auto *castExpr = dynamic_cast<CastExprAST *>(decl.initializer)) {
			std::string targetType = castExpr->targetType;
			// Substitute type parameters if we're in a generic method context
			auto bindingIt = currentTypeBindings.find(targetType);
			if (bindingIt != currentTypeBindings.end()) {
				targetType = bindingIt->second;
			}
			finalType = targetType;
		} else {
			// Derive type from the allocated MIR type
			finalType = getMaxonTypeFromMIRType(allocaType);
		}
	} else {
		// Derive type from the allocated MIR type
		finalType = getMaxonTypeFromMIRType(allocaType);
	}

	variableTypes[decl.name] = finalType;

	// Track substring variables for cleanup at scope exit
	// Substrings may hold a reference to a heap-allocated parent's buffer
	if (finalType == "substring" && !scopeStack.empty()) {
		scopeStack.back().substringAllocas.push_back({decl.name, alloca});
	}
	// Track cstring variables for cleanup at scope exit
	// Cstrings hold a reference to the underlying managed string
	if (finalType == "cstring" && !scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({decl.name, alloca});
	}
}

// ============================================================================
// Struct Initialization Handler (shared by var/let)
// ============================================================================

bool MIRCodeGenerator::tryGenerateStructInitDecl(const DeclInfo &decl) {
	auto *structInitExpr = dynamic_cast<StructInitExprAST *>(decl.initializer);
	if (!structInitExpr)
		return false;

	// Substitute struct name if we're inside a generic method body
	// E.g., "set" -> "set<int>" when generating set<int>.init
	// Also handle "Self" -> current receiver type
	std::string structName = structInitExpr->structName;
	if (!currentReceiverType.empty()) {
		// Handle "Self" type - substitute with current receiver type
		if (structName == "Self") {
			structName = currentReceiverType;
		} else if (!currentTypeBindings.empty()) {
			// Extract base template name from currentReceiverType (e.g., "set<int>" -> "set")
			std::string baseTemplateName = currentReceiverType;
			size_t anglePos = currentReceiverType.find('<');
			if (anglePos != std::string::npos) {
				baseTemplateName = currentReceiverType.substr(0, anglePos);
			}
			// If struct initializer is the base template, use specialized type
			if (structName == baseTemplateName) {
				structName = currentReceiverType;
			}
		}
	}

	mir::MIRType *structType = structTypes[structName];
	if (!structType) {
		reportError("Unknown struct type: " + structName,
					structInitExpr->line, structInitExpr->column);
	}
	// Mark struct type as used for lazy type emission
	structType->used = true;
	markFieldTypesUsed(structType);

	mir::MIRValue *structAlloca = builder->createAlloca(structType, decl.name);
	namedValues[decl.name] = structAlloca;
	variableTypes[decl.name] = structName;

	// Initialize fields - iterate over all struct fields and use provided value or default
	const auto &fields = structFields[structName];
	const auto &defaults = structFieldDefaults[structName];

	// Build a map of provided field values for quick lookup
	std::map<std::string, const StructInitField *> providedFields;
	for (const auto &initField : structInitExpr->fields) {
		providedFields[initField.name] = &initField;
	}

	for (size_t fieldIndex = 0; fieldIndex < fields.size(); fieldIndex++) {
		const std::string &fieldName = fields[fieldIndex].first;
		const std::string &fieldTypeStr = fields[fieldIndex].second;

		mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
														   static_cast<int>(fieldIndex), fieldName);

		// Get the value expression - either from provided init or from default
		ExprAST *valueExpr = nullptr;
		auto providedIt = providedFields.find(fieldName);
		if (providedIt != providedFields.end()) {
			valueExpr = providedIt->second->value.get();
		} else {
			// Use default value
			auto defaultIt = defaults.find(fieldName);
			if (defaultIt != defaults.end()) {
				valueExpr = defaultIt->second;
			}
		}

		if (valueExpr == nullptr) {
			reportError("No value for field '" + fieldName +
							"' in struct '" + structInitExpr->structName + "'",
						structInitExpr->line, structInitExpr->column);
		}

		// Handle array field initialization
		if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(valueExpr)) {
			// Check if field type is array<T> struct type (e.g., array<string>)
			if (maxon::TypeConversion::isArrayStructType(fieldTypeStr)) {
				// array<T> struct field - initialize with proper struct layout
				// This handles: var field array of T initialized with [elem1, elem2, ...]
				std::string elemTypeStr = maxon::TypeConversion::getArrayStructElementType(fieldTypeStr);

				// Generate element values
				std::vector<mir::MIRValue *> initValues;
				for (auto &valExpr : arrayLit->values) {
					mir::MIRValue *val = generateExpr(valExpr.get());
					if (!val) {
						reportError("Failed to generate array element value",
									arrayLit->line, arrayLit->column);
					}
					initValues.push_back(val);
				}

				int constantArraySize = static_cast<int>(initValues.size());
				mir::MIRType *elementType = initValues.empty() ? mir::MIRType::getInt64() : initValues[0]->type;
				uint64_t elementSize = elementType->sizeInBytes;
				(void)elementSize; // May be useful for future heap allocation threshold check

				// For struct fields, stack-allocate the buffer (same alloca as the struct)
				mir::MIRType *arrayType = mir::MIRType::getArray(elementType, constantArraySize);
				mir::MIRValue *stackBuffer = builder->createAlloca(arrayType, fieldName + ".stack_buffer");

				// Get array struct types
				mir::MIRType *arrayStructType = getOrCreateArrayStructType(elemTypeStr);
				mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemTypeStr);

				// Initialize nested struct layout in the field
				// Field structure: { managed: { _buffer, _len, _capacity }, iterIndex }
				mir::MIRValue *arraySizeVal = builder->getInt64(constantArraySize);
				mir::MIRValue *zeroCapacity = builder->getInt64(0);

				// Field 0: managed (nested __ManagedArrayData struct)
				mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, fieldName + ".managed");

				// Initialize the nested __ManagedArrayData fields
				mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, fieldName + ".managed._buffer");
				builder->createStore(stackBuffer, bufferField);

				mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, fieldName + ".managed._len");
				builder->createStore(arraySizeVal, lenField);

				mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, fieldName + ".managed._capacity");
				builder->createStore(zeroCapacity, capField);

				// Field 1: iterIndex
				mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, fieldPtr, 1, fieldName + ".iterIndex");
				builder->createStore(builder->getInt64(0), iterField);

				// Store each value into the buffer
				for (int i = 0; i < constantArraySize; i++) {
					mir::MIRValue *indexVal = builder->getInt64(i);
					mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, stackBuffer, indexVal, "arrayidx");
					builder->createStore(initValues[i], elementPtr);
				}
			} else if (!arrayLit->values.empty()) {
				// Static array field [N]T - initialize in place
				// Value-initialized array [1, 2, 3]
				for (size_t i = 0; i < arrayLit->values.size(); i++) {
					mir::MIRValue *elemValue = generateExpr(arrayLit->values[i].get());
					mir::MIRValue *elemPtr = builder->createArrayGEP(elemValue->type, fieldPtr,
																	 mir::MIRValue::createConstantInt(mir::MIRType::getInt64(), i),
																	 fieldName + ".elem");
					builder->createStore(elemValue, elemPtr);
				}
			} else {
				// Zero-initialized array [16]byte - already zero from alloca
				// Nothing extra needed
			}
		} else if (maxon::TypeConversion::isManagedArrayType(fieldTypeStr)) {
			// Slice field ([]T) - need to create fat pointer {ptr, len}
			// Check if the value is a variable reference to a variable-sized array
			std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(fieldTypeStr);
			std::string unsizedArrayTypeName = "_ManagedArray_" + elementTypeStr;

			// Get or create the unsized array struct type
			mir::MIRType *unsizedArrayType = structTypes[unsizedArrayTypeName];
			if (!unsizedArrayType) {
				unsizedArrayType = module->getOrCreateStructType(
					unsizedArrayTypeName,
					{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
				structTypes[unsizedArrayTypeName] = unsizedArrayType;
			}

			auto *varExpr = dynamic_cast<VariableExprAST *>(valueExpr);
			if (varExpr) {
				std::string varType = variableTypes[varExpr->name];
				// Check if this is a variable-sized array (type starts with [])
				if (maxon::TypeConversion::isManagedArrayType(varType)) {
					// Variable-sized array - load data pointer and length from header
					mir::MIRValue *arrayAlloca = namedValues[varExpr->name];
					if (arrayAlloca) {
						// Load the data pointer
						mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");

						// Load length from header at dataPtr - 16 (Int64 now)
						mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
																	  {builder->getInt64(-16)}, "length.ptr");
						mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt64(), lengthPtr, "length");

						// Store into the fat pointer field
						mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, fieldName + ".ptr");
						builder->createStore(dataPtr, dataPtrField);
						mir::MIRValue *lenField = builder->createStructGEP(unsizedArrayType, fieldPtr, 1, fieldName + ".len");
						builder->createStore(length, lenField);
					} else {
						reportError("Unknown variable in struct field init: " + varExpr->name,
									varExpr->line, varExpr->column);
					}
				} else {
					// Not a variable-sized array - use regular field value
					mir::MIRValue *fieldValue = generateExpr(valueExpr);
					builder->createStore(fieldValue, fieldPtr);
				}
			} else {
				// Not a variable reference - use regular field value
				mir::MIRValue *fieldValue = generateExpr(valueExpr);
				builder->createStore(fieldValue, fieldPtr);
			}
		} else {
			// Regular field value
			mir::MIRValue *fieldValue = generateExpr(valueExpr);

			// Get the MIR type for this field
			mir::MIRType *fieldType = getTypeFromString(fieldTypeStr);

			// Handle optional field wrapping
			// If field type is optional and value is nil or unwrapped, wrap it
			if (maxon::TypeConversion::isOptionalType(fieldTypeStr)) {
				// Get the value type
				std::string valueTypeStr = getExpressionMaxonType(valueExpr);

				// Check if value is nil (generateExpr returns nullptr for nil)
				bool isNilValue = (!fieldValue && dynamic_cast<NilExprAST *>(valueExpr));

				if (isNilValue) {
					// Create nil optional
					fieldValue = createNilOptional(fieldType);
				} else if (fieldValue && !maxon::TypeConversion::isOptionalType(valueTypeStr)) {
					// Value is unwrapped type - wrap in Some optional
					fieldValue = createSomeOptional(fieldType, fieldValue);
				}
				// else: value is already optional, use as-is
			}

			// Check if this is a struct field (char, string, etc.)
			// For struct types, generateExpr returns a pointer to an alloca
			// We need to load the struct value and store it (not store the pointer)
			if (fieldValue && fieldType->isStruct() && fieldValue->type == mir::MIRType::getPtr()) {
				// Load the struct value from the alloca
				mir::MIRValue *loadedValue = builder->createLoad(fieldType, fieldValue, fieldName + ".load");
				builder->createStore(loadedValue, fieldPtr);
			} else if (fieldValue) {
				builder->createStore(fieldValue, fieldPtr);
			} else {
				reportError("Failed to generate value for field '" + fieldName + "'",
							structInitExpr->line, structInitExpr->column);
			}
		}
	}

	return true;
}

// ============================================================================
// Enum Case Initialization Handler (shared by var/let)
// ============================================================================

bool MIRCodeGenerator::tryGenerateEnumCaseDecl(const DeclInfo &decl) {
	std::string inferredType;

	// Handle MemberAccess enum case (e.g., Direction.north)
	if (auto *memberExpr = dynamic_cast<MemberAccessExprAST *>(decl.initializer)) {
		if (memberExpr->isEnumCase() && !memberExpr->resolvedEnumName.empty()) {
			inferredType = memberExpr->resolvedEnumName;
			auto enumIt = enumTypes.find(inferredType);
			if (enumIt != enumTypes.end() && enumIt->second.hasAssociatedValues) {
				// Generate the enum value (returns alloca pointer)
				mir::MIRValue *tempAlloca = generateExpr(decl.initializer);

				// Create our own alloca and copy the struct content
				mir::MIRValue *varAlloca = builder->createAlloca(enumIt->second.mirType, decl.name);
				mir::MIRValue *loadedVal = builder->createLoad(enumIt->second.mirType, tempAlloca, decl.name + ".tmp");
				builder->createStore(loadedVal, varAlloca);

				namedValues[decl.name] = varAlloca;
				variableTypes[decl.name] = inferredType;
				return true;
			}
		}
	}

	// Handle CallExpr enum case (e.g., Result.success(1, 2))
	if (auto *callExpr = dynamic_cast<CallExprAST *>(decl.initializer)) {
		if (callExpr->isEnumCaseConstruction() && !callExpr->resolvedEnumName.empty()) {
			inferredType = callExpr->resolvedEnumName;
			auto enumIt = enumTypes.find(inferredType);
			if (enumIt != enumTypes.end()) {
				// Generate the enum value (returns alloca pointer)
				mir::MIRValue *tempAlloca = generateExpr(decl.initializer);

				// Create our own alloca and copy the struct content
				mir::MIRValue *varAlloca = builder->createAlloca(enumIt->second.mirType, decl.name);
				mir::MIRValue *loadedVal = builder->createLoad(enumIt->second.mirType, tempAlloca, decl.name + ".tmp");
				builder->createStore(loadedVal, varAlloca);

				namedValues[decl.name] = varAlloca;
				variableTypes[decl.name] = inferredType;
				return true;
			}
		}
	}

	return false;
}

// ============================================================================
// Empty Map Literal Handler (shared by var/let)
// ============================================================================

bool MIRCodeGenerator::tryGenerateEmptyMapLiteralDecl(const DeclInfo &decl) {
	auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(decl.initializer);
	if (!mapLiteral)
		return false;

	const std::string &keyType = mapLiteral->keyType;
	const std::string &valueType = mapLiteral->valueType;

	// Build the specialized type name
	std::string specializedName = "map<" + keyType + "," + valueType + ">";
	logDetail("Processing empty map literal: " + specializedName);

	// Instantiate the generic struct if not already done
	if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
		logDetail("  Need to instantiate generic struct");
		if (structDefinitions.find("map") != structDefinitions.end()) {
			logDetail("  Found 'map' in structDefinitions");
			std::map<std::string, std::string> typeBindings = {
				{"Key", keyType},
				{"Value", valueType}};
			instantiateGenericStruct("map", typeBindings);
		} else {
			reportError("Generic struct 'map' not found - ensure stdlib/collections/map.maxon is compiled",
						decl.line, decl.column);
		}
	}

	// Get the specialized struct type
	mir::MIRType *mapStructType = structTypes[specializedName];
	if (!mapStructType) {
		reportError("Failed to instantiate map type: " + specializedName,
					decl.line, decl.column);
	}

	// Mark struct type as used
	mapStructType->used = true;

	// Get element types
	mir::MIRType *keyMirType = getTypeFromString(keyType);
	mir::MIRType *valueMirType = getTypeFromString(valueType);

	// Create empty _ManagedArray<Key> struct for keys
	mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
	mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

	// Create empty _ManagedArray<Value> struct for values
	mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
	mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

	// Create minimal stack buffers (need at least 1 element for type correctness)
	mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, 1);
	mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

	mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, 1);
	mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

	// Initialize empty keys _ManagedArray struct fields
	mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
	builder->createStore(keysStackBuffer, keysBufferField);
	mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
	builder->createStore(builder->getInt64(0), keysLenField); // 0 elements
	mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
	builder->createStore(builder->getInt64(0), keysCapField); // capacity=0 means stack data

	// Initialize empty values _ManagedArray struct fields
	mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
	builder->createStore(valuesStackBuffer, valuesBufferField);
	mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
	builder->createStore(builder->getInt64(0), valuesLenField); // 0 elements
	mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
	builder->createStore(builder->getInt64(0), valuesCapField); // capacity=0 means stack data

	// Call map<K,V>.init(null, keysManaged, valuesManaged)
	std::string initMethodName = specializedName + ".init";
	mir::MIRFunction *initFunc = module->getFunction(initMethodName);
	if (!initFunc) {
		reportError("map.init method not found for type: " + specializedName +
						" - ensure InitableFromMapLiteral.init is implemented",
					decl.line, decl.column);
		return true; // Still handled, just with error
	}

	// Call static factory method (no self parameter)
	mir::MIRValue *mapResult = builder->createCall(initFunc, {keysManaged, valuesManaged}, "map.init");

	// Allocate storage for the map and store the result
	mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, decl.name);
	builder->createStore(mapResult, mapAlloca);
	namedValues[decl.name] = mapAlloca;
	variableTypes[decl.name] = specializedName;

	return true;
}

// ============================================================================
// Map With Entries Handler (shared by var/let)
// ============================================================================

bool MIRCodeGenerator::tryGenerateMapWithEntriesDecl(const DeclInfo &decl) {
	auto *mapWithEntries = dynamic_cast<MapLiteralWithEntriesExprAST *>(decl.initializer);
	if (!mapWithEntries)
		return false;

	const std::string &keyType = mapWithEntries->inferredKeyType;
	const std::string &valueType = mapWithEntries->inferredValueType;

	// Build the specialized type name
	std::string specializedName = "map<" + keyType + "," + valueType + ">";
	logDetail("Processing map literal with entries: " + specializedName);

	// Instantiate the generic struct if not already done
	if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
		logDetail("  Need to instantiate generic struct");
		if (structDefinitions.find("map") != structDefinitions.end()) {
			logDetail("  Found 'map' in structDefinitions");
			std::map<std::string, std::string> typeBindings = {
				{"Key", keyType},
				{"Value", valueType}};
			instantiateGenericStruct("map", typeBindings);
		} else {
			reportError("Generic struct 'map' not found - ensure stdlib/collections/map.maxon is compiled",
						decl.line, decl.column);
		}
	}

	// Get the specialized struct type
	mir::MIRType *mapStructType = structTypes[specializedName];
	if (!mapStructType) {
		reportError("Failed to instantiate map type: " + specializedName,
					decl.line, decl.column);
	}

	// Mark struct type as used
	mapStructType->used = true;

	// Get number of entries and element types
	int numEntries = static_cast<int>(mapWithEntries->entries.size());
	mir::MIRType *keyMirType = getTypeFromString(keyType);
	mir::MIRType *valueMirType = getTypeFromString(valueType);

	// Create _ManagedArray<Key> struct for keys
	mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
	mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

	// Create _ManagedArray<Value> struct for values
	mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
	mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

	// Allocate stack buffers for keys and values
	mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, numEntries > 0 ? numEntries : 1);
	mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

	mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, numEntries > 0 ? numEntries : 1);
	mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

	// Store keys and values in their respective stack buffers
	for (int i = 0; i < numEntries; i++) {
		const auto &entry = mapWithEntries->entries[i];

		// Store key
		mir::MIRValue *keyVal = generateExpr(entry.key.get());
		mir::MIRValue *keyPtr = builder->createArrayGEP(keyMirType, keysStackBuffer,
														builder->getInt64(i), "key.ptr");
		// For struct types (like string), generateExpr returns a pointer to an alloca
		// We need to load the struct value before storing it
		if (keyMirType->isStruct()) {
			mir::MIRValue *keyLoaded = builder->createLoad(keyMirType, keyVal, "key.loaded");
			builder->createStore(keyLoaded, keyPtr);
		} else {
			builder->createStore(keyVal, keyPtr);
		}

		// Store value
		mir::MIRValue *valVal = generateExpr(entry.value.get());
		mir::MIRValue *valPtr = builder->createArrayGEP(valueMirType, valuesStackBuffer,
														builder->getInt64(i), "val.ptr");
		// For struct types, load the value before storing
		if (valueMirType->isStruct()) {
			mir::MIRValue *valLoaded = builder->createLoad(valueMirType, valVal, "val.loaded");
			builder->createStore(valLoaded, valPtr);
		} else {
			builder->createStore(valVal, valPtr);
		}
	}

	// Initialize keys _ManagedArray struct fields
	mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
	builder->createStore(keysStackBuffer, keysBufferField);
	mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
	builder->createStore(builder->getInt64(numEntries), keysLenField);
	mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
	builder->createStore(builder->getInt64(0), keysCapField); // capacity=0 means stack data

	// Initialize values _ManagedArray struct fields
	mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
	builder->createStore(valuesStackBuffer, valuesBufferField);
	mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
	builder->createStore(builder->getInt64(numEntries), valuesLenField);
	mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
	builder->createStore(builder->getInt64(0), valuesCapField); // capacity=0 means stack data

	// Call map<K,V>.init(null, keysManaged, valuesManaged)
	std::string initMethodName = specializedName + ".init";
	mir::MIRFunction *initFunc = module->getFunction(initMethodName);
	if (!initFunc) {
		reportError("map.init method not found for type: " + specializedName +
						" - ensure InitableFromMapLiteral.init is implemented",
					decl.line, decl.column);
		return true; // Still handled, just with error
	}

	// Call static factory method (no self parameter)
	mir::MIRValue *mapResult = builder->createCall(initFunc, {keysManaged, valuesManaged}, "map.init");

	// Allocate storage for the map and store the result
	mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, decl.name);
	builder->createStore(mapResult, mapAlloca);
	namedValues[decl.name] = mapAlloca;
	variableTypes[decl.name] = specializedName;

	return true;
}

// ============================================================================
// Set From Array Handler (shared by var/let)
// ============================================================================

bool MIRCodeGenerator::tryGenerateSetFromArrayDecl(const DeclInfo &decl) {
	auto *setFromExpr = dynamic_cast<SetFromExprAST *>(decl.initializer);
	if (!setFromExpr)
		return false;

	const std::string &elemType = setFromExpr->inferredElementType;

	// Build the specialized type name
	std::string specializedName = "set<" + elemType + ">";
	logDetail("Processing set from expression: " + specializedName);

	// Instantiate the generic struct if not already done
	if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
		logDetail("  Need to instantiate generic struct");
		if (structDefinitions.find("set") != structDefinitions.end()) {
			logDetail("  Found 'set' in structDefinitions");
			std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
			instantiateGenericStruct("set", typeBindings);
		} else {
			reportError("Generic struct 'set' not found - ensure stdlib/collections/set.maxon is compiled",
						decl.line, decl.column);
		}
	}

	// Get the specialized struct type
	mir::MIRType *setStructType = structTypes[specializedName];
	if (!setStructType) {
		reportError("Failed to instantiate set type: " + specializedName,
					decl.line, decl.column);
	}

	// Mark struct type as used
	setStructType->used = true;

	// Get the array literal to extract elements
	auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(setFromExpr->arrayExpr.get());
	if (!arrayLiteral) {
		reportError("set from expression requires an array literal",
					decl.line, decl.column);
		return true; // Still handled, just with error
	}

	// Create _ManagedArray<Element> struct to pass to init()
	// Layout: { ptr _buffer, i32 _len, i32 _capacity }
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
	mir::MIRValue *managedAlloca = builder->createAlloca(managedArrayType, "managed.array");

	// Get element info
	int numElements = static_cast<int>(arrayLiteral->values.size());
	mir::MIRType *elemMirType = getTypeFromString(elemType);

	// Allocate stack buffer for elements (capacity=0 means constant/stack data)
	mir::MIRType *stackArrayType = mir::MIRType::getArray(elemMirType, numElements > 0 ? numElements : 1);
	mir::MIRValue *stackBuffer = builder->createAlloca(stackArrayType, "literal.buffer");

	// Store elements in the stack buffer
	for (int i = 0; i < numElements; i++) {
		mir::MIRValue *elemVal = generateExpr(arrayLiteral->values[i].get());
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemMirType, stackBuffer,
														 builder->getInt64(i), "elem.ptr");
		builder->createStore(elemVal, elemPtr);
	}

	// Initialize _ManagedArray struct fields
	// Field 0: _buffer pointer
	mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedAlloca, 0, "managed._buffer");
	builder->createStore(stackBuffer, bufferField);

	// Field 1: _len
	mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedAlloca, 1, "managed._len");
	builder->createStore(builder->getInt64(numElements), lenField);

	// Field 2: _capacity = 0 (constant/stack data - init() will copy to its own arrays)
	mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedAlloca, 2, "managed._capacity");
	builder->createStore(builder->getInt64(0), capField);

	// Call set<Element>.init(null, managedArray)
	// _ManagedArray<T> is an opaque pointer type (like _ManagedString), no hidden length
	std::string initMethodName = specializedName + ".init";
	mir::MIRFunction *initFunc = module->getFunction(initMethodName);
	if (!initFunc) {
		reportError("set.init method not found for type: " + specializedName +
						" - ensure InitableFromArrayLiteral.init is implemented",
					decl.line, decl.column);
		return true; // Still handled, just with error
	}

	// Call static factory method (no self parameter)
	mir::MIRValue *setResult = builder->createCall(initFunc, {managedAlloca}, "set.init");

	// Allocate storage for the set and store the result
	mir::MIRValue *setAlloca = builder->createAlloca(setStructType, decl.name);
	builder->createStore(setResult, setAlloca);
	namedValues[decl.name] = setAlloca;
	variableTypes[decl.name] = specializedName;

	return true;
}

// ============================================================================
// Main Declaration Functions
// ============================================================================

void MIRCodeGenerator::generateVarDecl(VarDeclStmtAST *varDecl, mir::MIRFunction *function) {
	DeclInfo decl{varDecl->name, varDecl->type, varDecl->initializer.get(),
				  varDecl->line, varDecl->column, true /* isMutable */};
	generateDeclFromInfo(decl);
}

void MIRCodeGenerator::generateLetDecl(LetDeclStmtAST *letDecl, mir::MIRFunction *function) {
	DeclInfo decl{letDecl->name, letDecl->type, letDecl->initializer.get(),
				  letDecl->line, letDecl->column, false /* isMutable */};
	generateDeclFromInfo(decl);
}

void MIRCodeGenerator::generateDeclFromInfo(const DeclInfo &decl) {
	// Try shared handlers (array literal, sized array, map/set, struct init, enum cases, string/char literals, etc.)
	if (tryGenerateArrayLiteralDecl(decl))
		return;
	if (tryGenerateSizedArrayDecl(decl))
		return;
	if (tryGenerateEmptyMapLiteralDecl(decl))
		return;
	if (tryGenerateMapWithEntriesDecl(decl))
		return;
	if (tryGenerateSetFromArrayDecl(decl))
		return;
	if (tryGenerateStructInitDecl(decl))
		return;
	if (tryGenerateEnumCaseDecl(decl))
		return;
	if (tryGenerateStringLiteralDecl(decl))
		return;
	if (tryGenerateCharLiteralDecl(decl))
		return;
	if (tryGenerateInterpolatedStringDecl(decl))
		return;
	if (tryGenerateStringConcatDecl(decl))
		return;
	if (tryGenerateArrayIndexStructDecl(decl))
		return;

	// Fallback: generate initializer and use simple declaration helper
	mir::MIRValue *initVal = nullptr;
	if (decl.initializer) {
		initVal = generateExpr(decl.initializer);
		if (!initVal) {
			reportError("Failed to generate initializer for '" + decl.name + "'",
						decl.line, decl.column);
		}
	}

	generateSimpleDecl(decl, initVal);
}
