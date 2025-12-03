/**
 * MIR Code Generator - String and Character Literal Generation
 *
 * This file implements string and char literal code generation for MIR.
 */

#include "../codegen_mir.h"
#include <cstring>

//==============================================================================
// String Literal Generation
//==============================================================================

// Get Maxon type from an AST expression
// This is used for type-aware code generation (e.g., Equatable comparison)
std::string MIRCodeGenerator::getExpressionMaxonType(ExprAST *expr) {
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		auto it = variableTypes.find(varExpr->name);
		if (it != variableTypes.end()) {
			return it->second;
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
		if (binExpr->op == '+') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());
			std::string rightType = getExpressionMaxonType(binExpr->right.get());
			if (leftType == "string" && rightType == "string") {
				return "string";
			}
		}
		// For other binary ops, return empty (will use default numeric behavior)
		return "";
	}
	// Handle call expressions - look up return type from function registry
	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Check if this is an enum case construction
		if (callExpr->isEnumCaseConstruction()) {
			return callExpr->resolvedEnumName;
		}

		const std::string &callee = callExpr->callee;

		// Look up the function return type from the semantic analyzer's registry
		auto it = functionReturnTypes.find(callee);
		if (it != functionReturnTypes.end()) {
			return it->second;
		}

		// For method calls on a known type (e.g., s.toLower() becomes string.toLower),
		// try looking up as Type.method if the first arg has a known type
		if (!callExpr->args.empty()) {
			std::string firstArgType = getExpressionMaxonType(callExpr->args[0].get());
			if (!firstArgType.empty()) {
				std::string qualifiedName = firstArgType + "." + callee;
				auto qualIt = functionReturnTypes.find(qualifiedName);
				if (qualIt != functionReturnTypes.end()) {
					return qualIt->second;
				}
			}
		}

		// For other calls, return empty (will use default behavior)
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
		mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
		if (!unsizedArrayType) {
			unsizedArrayType = module->getOrCreateStructType(
				"__unsized_array_byte",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
			structTypes["__unsized_array_byte"] = unsizedArrayType;
		}
		managedDataType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["__ManagedStringData"] = managedDataType;
	}
	mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

	// Create alloca for the string struct
	mir::MIRValue *stringAlloca = builder->createAlloca(stringType, "str.literal");

	// Get the string content
	const std::string &str = strExpr->value;
	size_t len = str.length();

	// SSO threshold: 15 bytes (to match spec's 16-byte inline layout)
	constexpr size_t SSO_THRESHOLD = 15;

	mir::MIRValue *dataPtr;
	bool isHeapAllocated = (len > SSO_THRESHOLD);
	int capacity = 0; // 0 = constant/SSO data, >0 = heap with refcount

	if (isHeapAllocated) {
		// Large string: heap-allocate with refcount header using _managed_string_alloc
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
		capacity = static_cast<int>(len);
		mir::MIRValue *sizeVal = builder->getInt64(len);
		dataPtr = builder->createCall(stringAllocFunc, {sizeVal, tag}, "str.heap.alloc");

		// Create a global constant for the string data (source for copy)
		std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
		mir::MIRValue *constDataPtr = module->createGlobalString(globalName, str);

		// Copy from constant data to heap buffer
		builder->createCall(memcpyFunc, {dataPtr, constDataPtr, sizeVal}, "str.heap.copy");

		// Track for cleanup at scope exit using _managed_string_release
		// We need to store the data pointer somewhere we can load it from during cleanup
		mir::MIRValue *dataPtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), "str.heap.ptr");
		builder->createStore(dataPtr, dataPtrAlloca);
		if (!scopeStack.empty()) {
			scopeStack.back().heapAllocatedStrings.push_back({"str.heap", dataPtrAlloca});
		}
	} else {
		// Small string: use constant data in read-only memory (no heap allocation)
		std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
		dataPtr = module->createGlobalString(globalName, str);
		capacity = 0; // Constant data, no refcount
	}

	// Allocate the __ManagedStringData struct on the stack
	mir::MIRValue *managedDataAlloca = builder->createAlloca(managedDataType, "managed.data");

	// Populate the __ManagedStringData fields
	// Field 0: _buffer []byte (fat pointer: {ptr, i32})
	mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 0, "managed._buffer");

	// Store the data pointer (field 0 of the fat pointer)
	mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "buffer.ptr");
	builder->createStore(dataPtr, bufferPtrPtr);

	// Store the length in the fat pointer (field 1 of the fat pointer)
	mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "buffer.len");
	builder->createStore(builder->getInt32(static_cast<int>(len)), bufferLenPtr);

	// Field 1: _len int
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 1, "managed._len");
	builder->createStore(builder->getInt32(static_cast<int>(len)), lenFieldPtr);

	// Field 2: _capacity int (0 = constant/SSO, >0 = heap with refcount header)
	mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 2, "managed._capacity");
	builder->createStore(builder->getInt32(capacity), capacityFieldPtr);

	// Store the POINTER to the managed data in the string struct's _managed field
	// string._managed is of type ptr (opaque pointer)
	mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, stringAlloca, 0, "str._managed");
	builder->createStore(managedDataAlloca, managedFieldPtr);

	// Initialize _iterPos field to 0 (field 1 of string struct)
	// This is required for the Iterable.next() implementation
	// FIX: Initialize _iterPos for iteration support
	mir::MIRValue *iterPosFieldPtr = builder->createStructGEP(stringType, stringAlloca, 1, "str._iterPos.FIXED");
	builder->createStore(builder->getInt32(0), iterPosFieldPtr);

	// Return the alloca pointer
	return stringAlloca;
}

// Generate a string literal as a []byte slice (fat pointer: {ptr, len})
// Used by ExpressibleByStringLiteral to pass raw bytes to init
mir::MIRValue *MIRCodeGenerator::generateStringLiteralAsSlice(StringLiteralExprAST *strExpr) {
	// Get the string content
	const std::string &str = strExpr->value;
	size_t len = str.length();

	// Create a global constant for the string data
	std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
	mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

	// Get or create the unsized array type for []byte
	mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
	if (!unsizedArrayType) {
		unsizedArrayType = module->getOrCreateStructType(
			"__unsized_array_byte",
			{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
		structTypes["__unsized_array_byte"] = unsizedArrayType;
	}

	// Create alloca for the fat pointer struct
	mir::MIRValue *sliceAlloca = builder->createAlloca(unsizedArrayType, "slice.literal");

	// Store the data pointer (field 0)
	mir::MIRValue *ptrFieldPtr = builder->createStructGEP(unsizedArrayType, sliceAlloca, 0, "slice.ptr");
	builder->createStore(dataPtr, ptrFieldPtr);

	// Store the length (field 1)
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(unsizedArrayType, sliceAlloca, 1, "slice.len");
	builder->createStore(builder->getInt32(static_cast<int>(len)), lenFieldPtr);

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
		mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
		if (!unsizedArrayType) {
			unsizedArrayType = module->getOrCreateStructType(
				"__unsized_array_byte",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
			structTypes["__unsized_array_byte"] = unsizedArrayType;
		}
		managedDataType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["__ManagedStringData"] = managedDataType;
	}
	mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

	// Create alloca for the char struct
	mir::MIRValue *charAlloca = builder->createAlloca(charType, "char.literal");

	// Get the char content (UTF-8 encoded grapheme cluster)
	const std::string &str = charExpr->value;
	size_t len = str.length();

	// Char literals always use constant data (no heap allocation needed)
	// since they're typically small (even complex emoji are < 25 bytes)
	std::string globalName = ".chr." + std::to_string(stringLiteralCounter++);
	mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

	// Allocate the __ManagedStringData struct on the stack
	mir::MIRValue *managedDataAlloca = builder->createAlloca(managedDataType, "char.managed.data");

	// Populate the __ManagedStringData fields
	// Field 0: _buffer []byte (fat pointer: {ptr, i32})
	mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 0, "char.managed._buffer");

	// Store the data pointer (field 0 of the fat pointer)
	mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "char.buffer.ptr");
	builder->createStore(dataPtr, bufferPtrPtr);

	// Store the length in the fat pointer (field 1 of the fat pointer)
	mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "char.buffer.len");
	builder->createStore(builder->getInt32(static_cast<int>(len)), bufferLenPtr);

	// Field 1: _len int
	mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 1, "char.managed._len");
	builder->createStore(builder->getInt32(static_cast<int>(len)), lenFieldPtr);

	// Field 2: _capacity int (0 = constant data, no refcount)
	mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 2, "char.managed._capacity");
	builder->createStore(builder->getInt32(0), capacityFieldPtr);

	// Store the POINTER to the managed data in the char struct's _managed field
	mir::MIRValue *managedFieldPtr = builder->createStructGEP(charType, charAlloca, 0, "char._managed");
	builder->createStore(managedDataAlloca, managedFieldPtr);

	// Return the alloca pointer
	return charAlloca;
}
