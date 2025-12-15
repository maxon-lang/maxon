/**
 * MIR Code Generator - String and Character Literal Generation
 *
 * This file implements string and char literal code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../intrinsics.h"
#include <cstring>

//==============================================================================
// String Literal Generation
//==============================================================================

// Get Maxon type from an AST expression
// This is used for type-aware code generation (e.g., Equatable comparison)
std::string MIRCodeGenerator::getExpressionMaxonType(ExprAST *expr) {
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		// Check local variables first
		auto it = variableTypes.find(varExpr->name);
		if (it != variableTypes.end()) {
			return it->second;
		}
		// Check global variables
		auto git = globalVariableTypes.find(varExpr->name);
		if (git != globalVariableTypes.end()) {
			return git->second;
		}
		// Check for implicit field access
		if (!currentReceiverType.empty()) {
			auto fieldsIt = structFields.find(currentReceiverType);
			if (fieldsIt != structFields.end()) {
				for (const auto &field : fieldsIt->second) {
					if (field.first == varExpr->name) {
						return field.second;
					}
				}
			}
		}
		return "";
	}
	if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(expr)) {
		(void)strLit; // Unused, just for type check
		return "string";
	}
	if (auto *structInit = dynamic_cast<StructInitExprAST *>(expr)) {
		return structInit->structName;
	}
	if (auto *intLit = dynamic_cast<NumberExprAST *>(expr)) {
		(void)intLit;
		return "int";
	}
	if (auto *floatLit = dynamic_cast<FloatExprAST *>(expr)) {
		(void)floatLit;
		return "float";
	}
	if (auto *boolLit = dynamic_cast<BooleanExprAST *>(expr)) {
		(void)boolLit;
		return "bool";
	}
	if (auto *memberAccess = dynamic_cast<MemberAccessExprAST *>(expr)) {
		// Check if this is an enum case expression
		if (memberAccess->isEnumCase()) {
			return memberAccess->resolvedEnumName;
		}

		// Determine the type of the object being accessed
		std::string objectType;
		if (memberAccess->object) {
			// Recursive case: complex expression (e.g., a.b in a.b.c)
			objectType = getExpressionMaxonType(memberAccess->object.get());
		} else {
			// Base case: simple variable access (e.g., self in self.field)
			auto it = variableTypes.find(memberAccess->objectName);
			// Check if key exists AND has a non-empty value (empty values are inserted by operator[])
			if (it != variableTypes.end() && !it->second.empty()) {
				objectType = it->second;
			} else if (!currentReceiverType.empty()) {
				// Check for implicit field access
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					for (const auto &field : fieldsIt->second) {
						if (field.first == memberAccess->objectName) {
							objectType = field.second;
							break;
						}
					}
				}
			}
		}

		// Now look up the field type in the struct
		if (!objectType.empty()) {
			auto fieldsIt = structFields.find(objectType);
			if (fieldsIt != structFields.end()) {
				for (const auto &field : fieldsIt->second) {
					if (field.first == memberAccess->memberName) {
						return field.second; // Return the field's type
					}
				}
			}
		}
		return "";
	}
	// Handle binary expressions - recursively determine result type
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		std::string leftType = getExpressionMaxonType(binExpr->left.get());
		std::string rightType = getExpressionMaxonType(binExpr->right.get());

		// String concatenation (if still supported)
		if (binExpr->op == '+' && leftType == "string" && rightType == "string") {
			return "string";
		}

		// Comparison operators return bool
		if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || // <=
			binExpr->op == 'G' || binExpr->op == 'E' || binExpr->op == 'N' || // >=, ==, !=
			binExpr->op == '&' || binExpr->op == '|') {						  // and, or
			return "bool";
		}

		// Arithmetic operations: promote to float if either operand is float
		if (leftType == "float" || rightType == "float") {
			return "float";
		}

		// Integer arithmetic
		if (leftType == "int" || rightType == "int") {
			return "int";
		}

		// Byte arithmetic
		if (leftType == "byte" || rightType == "byte") {
			return "byte";
		}

		// Fall back to left type if non-empty
		if (!leftType.empty()) {
			return leftType;
		}
		return rightType;
	}
	// Handle unary expressions - type is same as operand
	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		std::string operandType = getExpressionMaxonType(unaryExpr->operand.get());
		// Unary minus/plus doesn't change type
		if (unaryExpr->op == '-' || unaryExpr->op == '+') {
			return operandType;
		}
		// Unary 'not' returns bool
		if (unaryExpr->op == '!') {
			return "bool";
		}
		return operandType;
	}
	// Handle call expressions - look up return type from function registry
	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Check if this is an enum case construction
		if (callExpr->isEnumCaseConstruction()) {
			return callExpr->resolvedEnumName;
		}

		// Use the resolved callee name (includes namespace prefix) for function lookup
		std::string effectiveCallee = callExpr->resolvedCallee.empty() ? callExpr->callee : callExpr->resolvedCallee;

		// Check for intrinsics first (they start with __)
		if (effectiveCallee.rfind("__", 0) == 0) {
			const IntrinsicInfo *intrinsic = IntrinsicRegistry::instance().lookup(effectiveCallee);
			if (intrinsic) {
				return intrinsic->returnType;
			}
		}

		// Look up the function return type from the semantic analyzer's registry
		auto it = functionReturnTypes.find(effectiveCallee);
		if (it != functionReturnTypes.end()) {
			return it->second;
		}

		// For method calls on a known type (e.g., s.toLower() becomes string.toLower),
		// try looking up as Type.method if the first arg has a known type
		if (!callExpr->args.empty()) {
			std::string firstArgType = getExpressionMaxonType(callExpr->args[0].value.get());
			if (!firstArgType.empty()) {
				std::string qualifiedName = firstArgType + "." + callExpr->callee;
				auto qualIt = functionReturnTypes.find(qualifiedName);
				if (qualIt != functionReturnTypes.end()) {
					return qualIt->second;
				}
			}
		}

		// For other calls, return empty (will use default behavior)
		return "";
	}
	// Handle closure expressions - return the function type string
	if (auto *closureExpr = dynamic_cast<ClosureExprAST *>(expr)) {
		// Build function type string: fn(T1,T2)->R
		std::string funcType = "fn(";
		for (size_t i = 0; i < closureExpr->parameters.size(); i++) {
			if (i > 0)
				funcType += ",";
			funcType += closureExpr->parameters[i].type;
		}
		funcType += ")->";

		// Determine return type
		if (!closureExpr->returnType.empty()) {
			funcType += closureExpr->returnType;
		} else if (closureExpr->isSingleExpression && closureExpr->singleExpr) {
			std::string inferredType = getExpressionMaxonType(closureExpr->singleExpr.get());
			funcType += inferredType.empty() ? "int" : inferredType;
		} else {
			funcType += "void";
		}
		return funcType;
	}
	// Handle array index expressions - return the element type
	if (auto *arrayIndex = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		std::string arrayType;
		if (arrayIndex->hasArrayExpr()) {
			arrayType = getExpressionMaxonType(arrayIndex->arrayExpr.get());
		} else {
			auto it = variableTypes.find(arrayIndex->arrayName);
			if (it != variableTypes.end()) {
				arrayType = it->second;
			}
		}
		// Extract element type from array type (e.g., "array of int" -> "int", "[]int" -> "int", "array<int>" -> "int")
		if (!arrayType.empty()) {
			// Handle "array<T>" syntax (most common - used by makeArrayStructType)
			if (arrayType.rfind("array<", 0) == 0 && arrayType.back() == '>') {
				return arrayType.substr(6, arrayType.size() - 7); // Extract T from array<T>
			}
			// Handle "array of T" syntax
			if (arrayType.rfind("array of ", 0) == 0) {
				return arrayType.substr(9); // Skip "array of "
			}
			// Handle "[]T" syntax
			if (arrayType.rfind("[]", 0) == 0) {
				return arrayType.substr(2); // Skip "[]"
			}
			// Handle string indexing - returns character
			if (arrayType == "string") {
				return "character";
			}
		}
		return "";
	}
	// For other expression types, return empty (will use default behavior)
	return "";
}

// String layout: struct string { _managed ptr, _iterPos int }
// The _managed pointer points to __ManagedStringData { _buffer []byte, _len int, _capacity int }
// where []byte is a fat pointer { ptr, i32 }
// SSO threshold: strings <= 15 bytes use constant data; > 15 bytes use heap allocation
// Heap strings use _managed_string_alloc which prepends a refcount header for COW

mir::MIRValue *MIRCodeGenerator::generateStringLiteral(StringLiteralExprAST *strExpr) {
	// Check if we should generate as []byte slice instead of full string struct
	if (strExpr->asByteSlice) {
		return generateStringLiteralAsSlice(strExpr);
	}

	// Get the string struct type (defined by stdlib)
	// string = { _managed ptr }  where _managed is an OPAQUE POINTER
	// The data at that pointer has layout: { _buffer []byte, _len int, _capacity int }
	mir::MIRType *stringType = getTypeFromString("string");

	// Get or create __ManagedStringData type (internal layout at the pointer)
	mir::MIRType *managedDataType = structTypes["__ManagedStringData"];
	if (!managedDataType) {
		mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
		if (!unsizedArrayType) {
			unsizedArrayType = module->getOrCreateStructType(
				"_ManagedArray_byte",
				{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
			structTypes["_ManagedArray_byte"] = unsizedArrayType;
		}
		managedDataType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt64(), mir::MIRType::getInt64()});
		structTypes["__ManagedStringData"] = managedDataType;
	}
	mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

	// Create alloca for the string struct
	mir::MIRValue *stringAlloca = builder->createAlloca(stringType, "str.literal");

	// Get the string content
	const std::string &str = strExpr->value;
	size_t len = str.length();

	// SSO threshold: strings <= 15 bytes use constant data; > 15 bytes use heap allocation
	const size_t SSO_THRESHOLD = 15;
	bool useSSO = len <= SSO_THRESHOLD;

	mir::MIRValue *dataPtr;
	int capacity;

	if (useSSO) {
		// SSO: store data directly in a global constant
		std::string globalName = ".str.sso." + std::to_string(stringLiteralCounter++);
		dataPtr = module->createGlobalString(globalName, str);
		capacity = 0; // 0 indicates SSO/constant
	} else {
		// Heap allocation for longer strings
		initHeapManagement();

		// Declare _managed_string_alloc if not already declared (takes size and tag)
		mir::MIRFunction *stringAllocFunc = module->getFunction("_managed_string_alloc");
		if (!stringAllocFunc) {
			stringAllocFunc = builder->declareFunction("_managed_string_alloc",
													   mir::MIRType::getPtr(),
													   {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
		}
		mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");

		// Create tag for tracking
		mir::MIRValue *tag = module->createGlobalString(".__tag.str.lit", "string literal");

		// Allocate heap buffer with refcount header (returns ptr to data area)
		// Allocate len + 1 to include null terminator for C compatibility
		mir::MIRValue *sizeVal = builder->getInt64(len + 1);
		dataPtr = builder->createCall(stringAllocFunc, {sizeVal, tag}, "str.heap.alloc");

		// Create a global constant for the string data (source for copy)
		std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
		mir::MIRValue *constDataPtr = module->createGlobalString(globalName, str);

		// Copy from constant data to heap buffer
		builder->createCall(memcpyFunc, {dataPtr, constDataPtr, builder->getInt64(len)}, "str.heap.copy");

		// Null-terminate the buffer
		mir::MIRValue *nullPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr, {builder->getInt64(len)}, "null.ptr");
		builder->createStore(builder->getInt8(0), nullPtr);

		// Track for cleanup at scope exit using _managed_string_release.
		// IMPORTANT: when generating __global_init, these literals may escape into globals
		// (e.g., map keys stored in heap buffers). Do not auto-release them.
		if (!inGlobalInit) {
			mir::MIRValue *dataPtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), "str.heap.ptr");
			builder->createStore(dataPtr, dataPtrAlloca);
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedStrings.push_back({"str.heap", dataPtrAlloca});
			}
		}

		capacity = static_cast<int>(len + 1);
	}

	// Allocate the __ManagedStringData struct on the heap.
	// String values can be copied into structs that get returned from functions or stored in
	// globals, so the metadata must live longer than the current stack frame. Always heap-allocate.
	initHeapManagement();
	mir::MIRFunction *mallocFunc = module->getFunction("malloc");
	if (!mallocFunc) {
		reportError("malloc not declared for string literal", strExpr->line, strExpr->column);
	}
	mir::MIRValue *managedDataAlloca = builder->createCall(
		mallocFunc,
		{builder->getInt64(managedDataType->sizeInBytes)},
		"managed.data.heap");

	// Populate the __ManagedStringData fields
	// Field 0: _buffer []byte (fat pointer: {ptr, i32})
	mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 0, "managed._buffer");

	// Store the data pointer (field 0 of the fat pointer)
	mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "buffer.ptr");
	builder->createStore(dataPtr, bufferPtrPtr);

	// Store the length in the fat pointer (field 1 of the fat pointer)
	mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "buffer.len");
	builder->createStore(builder->getInt64(static_cast<int64_t>(len)), bufferLenPtr);

	// Field 1: _len int
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 1, "managed._len");
	builder->createStore(builder->getInt64(static_cast<int64_t>(len)), lenFieldPtr);

	// Field 2: _capacity int (0 = constant/SSO, >0 = heap with refcount header)
	mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 2, "managed._capacity");
	builder->createStore(builder->getInt64(capacity), capacityFieldPtr);

	// Store the POINTER to the managed data in the string struct's _managed field
	// string._managed is of type ptr (opaque pointer)
	mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, stringAlloca, 0, "str._managed");
	builder->createStore(managedDataAlloca, managedFieldPtr);

	// Initialize _iterPos field to 0 (field 1 of string struct)
	// This is required for the Iterable.next() implementation
	// FIX: Initialize _iterPos for iteration support
	mir::MIRValue *iterPosFieldPtr = builder->createStructGEP(stringType, stringAlloca, 1, "str._iterPos.FIXED");
	builder->createStore(builder->getInt64(0), iterPosFieldPtr);

	// Return the alloca pointer
	return stringAlloca;
}

// Generate a string literal as a []byte slice (fat pointer: {ptr, len})
// Used by InitableFromStringLiteral to pass raw bytes to init
mir::MIRValue *MIRCodeGenerator::generateStringLiteralAsSlice(StringLiteralExprAST *strExpr) {
	// Get the string content
	const std::string &str = strExpr->value;
	size_t len = str.length();

	// Create a global constant for the string data
	std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
	mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

	// Get or create the unsized array type for []byte
	mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
	if (!unsizedArrayType) {
		unsizedArrayType = module->getOrCreateStructType(
			"_ManagedArray_byte",
			{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
		structTypes["_ManagedArray_byte"] = unsizedArrayType;
	}

	// Create alloca for the fat pointer struct
	mir::MIRValue *sliceAlloca = builder->createAlloca(unsizedArrayType, "slice.literal");

	// Store the data pointer (field 0)
	mir::MIRValue *ptrFieldPtr = builder->createStructGEP(unsizedArrayType, sliceAlloca, 0, "slice.ptr");
	builder->createStore(dataPtr, ptrFieldPtr);

	// Store the length (field 1)
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(unsizedArrayType, sliceAlloca, 1, "slice.len");
	builder->createStore(builder->getInt64(static_cast<int64_t>(len)), lenFieldPtr);

	// Return pointer to the fat pointer struct
	return sliceAlloca;
}

// Generate a character literal (grapheme cluster)
// character has the same internal layout as string (uses _ManagedString)
mir::MIRValue *MIRCodeGenerator::generateCharLiteral(CharacterExprAST *charExpr) {
	// Get the character struct type (defined by stdlib, same layout as string)
	// character = { _managed ptr } where _managed points to __ManagedStringData
	mir::MIRType *charType = getTypeFromString("character");

	// Get or create __ManagedStringData type (internal layout at the pointer)
	mir::MIRType *managedDataType = structTypes["__ManagedStringData"];
	if (!managedDataType) {
		mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
		if (!unsizedArrayType) {
			unsizedArrayType = module->getOrCreateStructType(
				"_ManagedArray_byte",
				{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
			structTypes["_ManagedArray_byte"] = unsizedArrayType;
		}
		managedDataType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt64(), mir::MIRType::getInt64()});
		structTypes["__ManagedStringData"] = managedDataType;
	}
	mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

	// Create alloca for the char struct
	mir::MIRValue *charAlloca = builder->createAlloca(charType, "char.literal");

	// Get the char content (UTF-8 encoded grapheme cluster)
	const std::string &str = charExpr->value;
	size_t len = str.length();

	// Char literals always use constant data (no heap allocation needed)
	// since they're typically small (even complex emoji are < 25 bytes)
	std::string globalName = ".chr." + std::to_string(stringLiteralCounter++);
	mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

	// Allocate the __ManagedStringData struct on the heap.
	// Character values may escape their defining scope (e.g., returned from functions,
	// stored in containers). Stack allocation would dangle, causing use-after-free.
	// Heap allocation is safe and characters are typically small (even emoji < 25 bytes).
	initHeapManagement();
	mir::MIRFunction *mallocFunc = module->getFunction("malloc");
	if (!mallocFunc) {
		reportError("malloc not declared for char literal", charExpr->line, charExpr->column);
	}
	mir::MIRValue *managedDataAlloca = builder->createCall(
		mallocFunc,
		{builder->getInt64(managedDataType->sizeInBytes)},
		"char.managed.data");

	// Populate the __ManagedStringData fields
	// Field 0: _buffer []byte (fat pointer: {ptr, i32})
	mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 0, "char.managed._buffer");

	// Store the data pointer (field 0 of the fat pointer)
	mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "char.buffer.ptr");
	builder->createStore(dataPtr, bufferPtrPtr);

	// Store the length in the fat pointer (field 1 of the fat pointer)
	mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "char.buffer.len");
	builder->createStore(builder->getInt64(static_cast<int64_t>(len)), bufferLenPtr);

	// Field 1: _len int
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 1, "char.managed._len");
	builder->createStore(builder->getInt64(static_cast<int64_t>(len)), lenFieldPtr);

	// Field 2: _capacity int (0 = constant data, no refcount)
	mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 2, "char.managed._capacity");
	builder->createStore(builder->getInt64(0), capacityFieldPtr);

	// Store the POINTER to the managed data in the char struct's _managed field
	mir::MIRValue *managedFieldPtr = builder->createStructGEP(charType, charAlloca, 0, "char._managed");
	builder->createStore(managedDataAlloca, managedFieldPtr);

	// Return the alloca pointer
	return charAlloca;
}

// Generate an interpolated string like "Hello {name}!"
// Converts each expression to string using toString() and concatenates all parts
mir::MIRValue *MIRCodeGenerator::generateInterpolatedString(InterpolatedStringExprAST *interpExpr) {
	// Get the string struct type
	mir::MIRType *stringType = getTypeFromString("string");

	// If there's only one literal part with no expressions, just generate a regular string
	if (interpExpr->parts.size() == 1 && !interpExpr->parts[0].isExpression) {
		// Create a temporary StringLiteralExprAST
		StringLiteralExprAST tempStrExpr(interpExpr->parts[0].literalValue, interpExpr->line, interpExpr->column);
		return generateStringLiteral(&tempStrExpr);
	}

	// Convert each part to a string and store in a vector
	std::vector<mir::MIRValue *> stringParts;

	for (const auto &part : interpExpr->parts) {
		if (!part.isExpression) {
			// Literal string segment - generate as string literal
			StringLiteralExprAST tempStrExpr(part.literalValue, interpExpr->line, interpExpr->column);
			mir::MIRValue *litStr = generateStringLiteral(&tempStrExpr);
			stringParts.push_back(litStr);
		} else {
			// Expression - need to convert to string via toString()
			mir::MIRValue *exprVal = generateExpr(part.expr.get());
			std::string exprType = getExpressionMaxonType(part.expr.get());

			// Call toString on the expression
			// For built-in types, we call intrinsics; for user types, call Type.toString(spec)
			mir::MIRValue *strVal = nullptr;

			if (exprType == "string") {
				// Already a string - exprVal should be a pointer to the string struct
				// If it's a loaded value, we need to store it in an alloca first
				if (exprVal->type->isStruct()) {
					// It's a loaded string value, store it in alloca
					mir::MIRValue *strAlloca = builder->createAlloca(stringType, "interp.str");
					builder->createStore(exprVal, strAlloca);
					strVal = strAlloca;
				} else {
					// It's already a pointer
					strVal = exprVal;
				}
			} else if (exprType == "int" || exprType == "float" || exprType == "bool" ||
					   exprType == "byte" || exprType == "character") {
				// Built-in types - call intrinsic toString with ptr for format spec
				// Built-in intrinsics take (value, ptr) where ptr is null for no format
				std::string intrinsicName = "__" + exprType + "_toString";

				// Generate format spec: null ptr for no format, string ptr for format
				mir::MIRValue *formatSpecArg;
				if (part.formatSpec.empty()) {
					formatSpecArg = builder->getNull();
				} else {
					StringLiteralExprAST specExpr(part.formatSpec, interpExpr->line, interpExpr->column);
					formatSpecArg = generateStringLiteral(&specExpr);
				}

				// Declare the intrinsic if not already declared
				mir::MIRFunction *toStringFunc = module->getFunction(intrinsicName);
				if (!toStringFunc) {
					toStringFunc = builder->declareFunction(intrinsicName,
															stringType,
															{exprVal->type, mir::MIRType::getPtr()});
				}

				// Call the intrinsic: result = __type_toString(value, spec)
				strVal = builder->createCall(toStringFunc, {exprVal, formatSpecArg}, "toString.result");

				// For struct returns, we need to store in an alloca
				mir::MIRValue *strAlloca = builder->createAlloca(stringType, "interp.str");
				builder->createStore(strVal, strAlloca);
				strVal = strAlloca;
			} else {
				// User type - call Type.toString(format string or nil)
				// The function signature is: toString(self ptr, format Optional<string>)
				std::string methodName = exprType + ".toString";
				mir::MIRFunction *toStringFunc = module->getFunction(methodName);

				if (!toStringFunc) {
					// The method should already be declared by codegen, but declare if needed
					// Second parameter is Optional<string>, i.e. "string or nil"
					mir::MIRType *optStringType = getTypeFromString("string or nil");
					toStringFunc = builder->declareFunction(methodName,
															stringType,
															{mir::MIRType::getPtr(), optStringType});
				}

				// Ensure exprVal is a pointer (methods take self by pointer)
				mir::MIRValue *selfPtr = exprVal;
				if (exprVal->type->isStruct()) {
					// It's a struct value, need to store to alloca and pass pointer
					mir::MIRType *structType = getTypeFromString(exprType);
					mir::MIRValue *alloca = builder->createAlloca(structType, "interp.self");
					builder->createStore(exprVal, alloca);
					selfPtr = alloca;
				}

				// Generate format spec: nil optional for no format, wrapped string for format
				mir::MIRType *optStringType = getTypeFromString("string or nil");
				mir::MIRValue *formatSpecArg;
				if (part.formatSpec.empty()) {
					// Create nil optional
					formatSpecArg = createNilOptional(optStringType);
				} else {
					// Create string and wrap in optional
					StringLiteralExprAST specExpr(part.formatSpec, interpExpr->line, interpExpr->column);
					mir::MIRValue *formatStr = generateStringLiteral(&specExpr);
					// Load the string value (formatStr is a pointer to string)
					mir::MIRValue *formatStrVal = builder->createLoad(stringType, formatStr, "format.str.val");
					formatSpecArg = createSomeOptional(optStringType, formatStrVal);
				}

				// Call: result = Type.toString(self, format)
				strVal = builder->createCall(toStringFunc, {selfPtr, formatSpecArg}, "toString.result");

				// For struct returns, store in alloca
				mir::MIRValue *strAlloca = builder->createAlloca(stringType, "interp.str");
				builder->createStore(strVal, strAlloca);
				strVal = strAlloca;
			}

			stringParts.push_back(strVal);
		}
	}

	// Now concatenate all parts using string.concat()
	// Start with the first part and concatenate the rest
	mir::MIRValue *result = stringParts[0];

	// Only set up types for cleanup tracking if we have multiple parts to concatenate
	mir::MIRType *managedStringDataType = nullptr;
	mir::MIRType *unsizedArrayType = nullptr;
	if (stringParts.size() > 1) {
		// Get types needed for extracting buffer pointer
		managedStringDataType = structTypes["__ManagedStringData"];
		if (!managedStringDataType) {
			unsizedArrayType = structTypes["_ManagedArray_byte"];
			managedStringDataType = module->getOrCreateStructType(
				"__ManagedStringData",
				{unsizedArrayType, mir::MIRType::getInt64(), mir::MIRType::getInt64()});
			structTypes["__ManagedStringData"] = managedStringDataType;
		}
		unsizedArrayType = structTypes["_ManagedArray_byte"];
	}

	for (size_t i = 1; i < stringParts.size(); i++) {
		// Call string.concat(self, other)
		mir::MIRFunction *concatFunc = module->getFunction("string.concat");
		if (!concatFunc) {
			concatFunc = builder->declareFunction("string.concat",
												  stringType,
												  {mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		}

		mir::MIRValue *concatResult = builder->createCall(concatFunc, {result, stringParts[i]}, "concat.result");

		// Store result in alloca for next iteration
		mir::MIRValue *resultAlloca = builder->createAlloca(stringType, "interp.tmp");
		builder->createStore(concatResult, resultAlloca);
		result = resultAlloca;

		// Track intermediate results for cleanup (except the final one)
		// The final result will be tracked when assigned to a variable
		if (i < stringParts.size() - 1 && !scopeStack.empty()) {
			// Extract the buffer pointer from the string struct for cleanup
			// string._managed -> __ManagedStringData._buffer.ptr
			mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, resultAlloca, 0, "interp.managed.field");
			mir::MIRValue *managedPtr = builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, "interp.managed.ptr");
			mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringDataType, managedPtr, 0, "interp.buffer.field");
			mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "interp.buffer.ptr.ptr");
			mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, "interp.buffer.ptr");

			// Store buffer pointer in alloca for cleanup tracking
			mir::MIRValue *trackAlloca = builder->createAlloca(mir::MIRType::getPtr(), "interp.track.ptr");
			builder->createStore(bufferPtr, trackAlloca);
			scopeStack.back().heapAllocatedStrings.push_back({"interp.intermediate", trackAlloca});
		}
	}

	return result;
}
