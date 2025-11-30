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
// String Literal Generation
//==============================================================================

// Helper function to get Maxon type from an AST expression
// This is used for type-aware code generation (e.g., Equatable comparison)
// currentReceiverType is used for implicit field access resolution
static std::string getExpressionMaxonType(ExprAST *expr,
										  const std::map<std::string, std::string> &variableTypes,
										  const std::map<std::string, mir::MIRType *> &structTypes,
										  const std::map<std::string, std::vector<std::pair<std::string, std::string>>> &structFields,
										  const std::string &currentReceiverType = "") {
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
		// Determine the type of the object being accessed
		std::string objectType;
		if (memberAccess->object) {
			// Recursive case: complex expression (e.g., a.b in a.b.c)
			objectType = getExpressionMaxonType(memberAccess->object.get(), variableTypes, structTypes, structFields, currentReceiverType);
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
			std::string leftType = getExpressionMaxonType(binExpr->left.get(), variableTypes, structTypes, structFields, currentReceiverType);
			std::string rightType = getExpressionMaxonType(binExpr->right.get(), variableTypes, structTypes, structFields, currentReceiverType);
			if (leftType == "string" && rightType == "string") {
				return "string";
			}
		}
		// For other binary ops, return empty (will use default numeric behavior)
		return "";
	}
	// Handle call expressions - check for known string-returning methods
	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Check if callee is a string method that returns string
		const std::string &callee = callExpr->callee;
		// Common string methods that return string
		// The callee can be either unqualified "toLower" (method call syntax s.toLower())
		// or qualified "string.toLower" (direct call syntax string.toLower(s))
		if (callee == "string.toLower" || callee == "string.toUpper" ||
			callee == "string.concat" || callee == "string.trimWhitespace" ||
			callee == "string.slice" ||
			callee == "toLower" || callee == "toUpper" ||
			callee == "concat" || callee == "trimWhitespace" ||
			callee == "slice") {
			// For unqualified method calls, check if first arg is a string
			if (!callExpr->args.empty()) {
				std::string firstArgType = getExpressionMaxonType(callExpr->args[0].get(), variableTypes, structTypes, structFields, currentReceiverType);
				if (firstArgType == "string") {
					return "string";
				}
			}
			// For qualified calls, return string directly
			if (callee.find("string.") == 0) {
				return "string";
			}
		}
		// For other calls, return empty (will use default behavior)
		return "";
	}
	// For other expression types, return empty (will use default behavior)
	return "";
}

//==============================================================================
// Helper: Generate pointer to struct field (without loading)
//==============================================================================

// Helper struct to return both pointer and type info
struct MemberAddressResult {
	mir::MIRValue *ptr;
	std::string type;
};

// String layout: struct string { data []byte, _len int, _capacity int, _iterPos int }
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
	// The data at that pointer has layout: { _buffer []byte, _len int, _capacity int, _iterPos int }
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
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
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

	// Field 3: _iterPos int (iteration position, starts at 0)
	mir::MIRValue *iterPosFieldPtr = builder->createStructGEP(managedDataType, managedDataAlloca, 3, "managed._iterPos");
	builder->createStore(builder->getInt32(0), iterPosFieldPtr);

	// Store the POINTER to the managed data in the string struct's _managed field
	// string._managed is of type ptr (opaque pointer)
	mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, stringAlloca, 0, "str._managed");
	builder->createStore(managedDataAlloca, managedFieldPtr);

	// Return the alloca pointer
	return stringAlloca;
}

// Generate a string literal as a []byte slice (fat pointer: {ptr, len})
// Used by ExpressibleByStringLiteral to pass raw bytes to initFromStringLiteral
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
		// Check for ExpressibleByStringLiteral: "literal" as Type
		// Transform to: Type.initFromStringLiteral(managed)
		// where managed is a _ManagedString struct
		if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(castExpr->expr.get())) {
			// Check if target type conforms to ExpressibleByStringLiteral
			auto structIt = structConformsTo.find(castExpr->targetType);
			if (structIt != structConformsTo.end()) {
				bool conformsToExpressible = false;
				for (const auto &iface : structIt->second) {
					if (iface == "ExpressibleByStringLiteral") {
						conformsToExpressible = true;
						break;
					}
				}
				if (conformsToExpressible) {
					// Get or create the _ManagedString struct type
					// Layout: { _buffer []byte, _len int, _capacity int, _iterPos int }
					mir::MIRType *managedStringType = structTypes["_ManagedString"];
					if (!managedStringType) {
						// _ManagedString should be defined in stdlib, but create fallback
						mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
						if (!unsizedArrayType) {
							unsizedArrayType = module->getOrCreateStructType(
								"__unsized_array_byte",
								{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
							structTypes["__unsized_array_byte"] = unsizedArrayType;
						}
						managedStringType = module->getOrCreateStructType(
							"_ManagedString",
							{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
						structTypes["_ManagedString"] = managedStringType;
					}

					// Create the _ManagedString struct
					mir::MIRValue *managedAlloca = builder->createAlloca(managedStringType, "managed.literal");

					// Get string content
					const std::string &str = strLit->value;
					size_t len = str.length();

					// Create global constant for string data
					std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
					mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

					// Get the unsized array type for []byte (the _buffer field)
					mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

					// Field 0: _buffer []byte (fat pointer: {ptr, i32})
					mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 0, "managed._buffer");
					mir::MIRValue *bufferDataPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "managed._buffer.ptr");
					builder->createStore(dataPtr, bufferDataPtr);
					mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "managed._buffer.len");
					builder->createStore(builder->getInt32(static_cast<int>(len)), bufferLenPtr);

					// Field 1: _len int
					mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 1, "managed._len");
					builder->createStore(builder->getInt32(static_cast<int>(len)), lenFieldPtr);

					// Field 2: _capacity int (0 = constant data)
					mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 2, "managed._capacity");
					builder->createStore(builder->getInt32(0), capacityFieldPtr);

					// Field 3: _iterPos int (0 = start)
					mir::MIRValue *iterPosFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 3, "managed._iterPos");
					builder->createStore(builder->getInt32(0), iterPosFieldPtr);

					// Call Type.initFromStringLiteral(self, managed)
					// Note: self is null since this is a factory method that creates a new instance
					std::string methodName = castExpr->targetType + ".initFromStringLiteral";
					mir::MIRFunction *initFunc = module->getFunction(methodName);
					if (!initFunc) {
						throw std::runtime_error("Type '" + castExpr->targetType +
												 "' conforms to ExpressibleByStringLiteral but has no initFromStringLiteral method");
					}

					// Pass null for self (factory method doesn't use it)
					mir::MIRValue *nullSelf = builder->getNull();
					return builder->createCall(initFunc, {nullSelf, managedAlloca}, "literal.init");
				}
			}
		}

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
			// If we're in a method and variable not found, check if it's a struct field
			// This enables implicit self field access: 'count' instead of 'self.count'
			if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == varExpr->name) {
							fieldIndex = static_cast<int>(i);
							fieldType = fieldsIt->second[i].second;
							break;
						}
					}

					if (fieldIndex >= 0) {
						// Generate field access through implicit 'self' parameter
						// self is always a struct pointer (first parameter)
						mir::MIRValue *selfAlloca = namedValues["self"];
						if (selfAlloca) {
							// self is a struct - load pointer to it
							mir::MIRValue *selfPtr;
							if (isStructParameter("self")) {
								// Load the pointer from the alloca
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}

							// Get field pointer with GEP
							mir::MIRType *structType = structTypes[currentReceiverType];
							mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, varExpr->name + ".ptr");

							// For array/unsized array fields, return pointer (for subsequent indexing or struct init)
							if (fieldType.size() > 2 && fieldType[0] == '[') {
								return fieldPtr;
							}

							// Load the field value for non-array fields
							mir::MIRType *fieldMIRType = getTypeFromString(fieldType);
							return builder->createLoad(fieldMIRType, fieldPtr, varExpr->name);
						}
					}
				}
			}
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

		// For struct parameters, we need to load the pointer first, then load the struct
		// The alloca contains a pointer to the struct (passed by reference)
		if (isStructParameter(varExpr->name)) {
			// Load the pointer to the struct
			mir::MIRValue *structPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name + ".ptr");
			// Load the struct value through the pointer
			return builder->createLoad(loadType, structPtr, varExpr->name);
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
			// Complex expression (e.g., arr[0].field or self._str._len)
			if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(memberAccessExpr->object.get())) {
				// Array index expression (e.g., arr[0].field)
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
				if (arrayType.size() > 2 && arrayType[0] == '[') {
					size_t closeBracket = arrayType.find(']');
					if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
						varType = arrayType.substr(closeBracket + 1);
					}
				}
				objectPtr = objectValue;
			} else if (auto *nestedMemberExpr = dynamic_cast<MemberAccessExprAST *>(memberAccessExpr->object.get())) {
				// Nested member access (e.g., self._str in self._str._len)
				// We need to get a pointer to the intermediate struct field, NOT load it!
				(void)nestedMemberExpr; // Used for type check above

				// First, determine the base object and its type
				std::string baseObjType;
				mir::MIRValue *basePtr = nullptr;

				// Collect the chain of member accesses
				std::vector<MemberAccessExprAST *> chain;
				ExprAST *current = memberAccessExpr->object.get();
				while (auto *ma = dynamic_cast<MemberAccessExprAST *>(current)) {
					chain.push_back(ma);
					if (ma->object) {
						current = ma->object.get();
					} else {
						// Reached the base variable
						baseObjType = variableTypes[ma->objectName];
						mir::MIRValue *structAlloca = namedValues[ma->objectName];
						if (!structAlloca) {
							// Check for implicit field access
							if (!currentReceiverType.empty()) {
								auto fieldsIt = structFields.find(currentReceiverType);
								if (fieldsIt != structFields.end()) {
									int fieldIndex = -1;
									std::string fieldType;
									for (size_t i = 0; i < fieldsIt->second.size(); i++) {
										if (fieldsIt->second[i].first == ma->objectName) {
											fieldIndex = static_cast<int>(i);
											fieldType = fieldsIt->second[i].second;
											break;
										}
									}
									if (fieldIndex >= 0) {
										mir::MIRValue *selfAlloca = namedValues["self"];
										if (selfAlloca) {
											mir::MIRValue *selfPtr;
											if (isStructParameter("self")) {
												selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
											} else {
												selfPtr = selfAlloca;
											}
											mir::MIRType *structType = structTypes[currentReceiverType];
											basePtr = builder->createStructGEP(structType, selfPtr, fieldIndex, ma->objectName + ".ptr");
											baseObjType = fieldType;
											break;
										}
									}
								}
							}
							if (!basePtr) {
								throw std::runtime_error("Unknown variable: " + ma->objectName);
							}
						} else {
							if (isStructParameter(ma->objectName)) {
								basePtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
							} else {
								basePtr = structAlloca;
							}
						}
						break;
					}
				}

				// Now traverse the chain from base to get the pointer to the intermediate field
				// The chain is in reverse order, so we need to reverse it
				std::reverse(chain.begin(), chain.end());

				mir::MIRValue *currentPtr = basePtr;
				std::string currentType = baseObjType;

				for (auto *ma : chain) {
					// Get the struct type
					if (structTypes.find(currentType) == structTypes.end()) {
						throw std::runtime_error("Expected struct type for member access: " + currentType);
					}
					mir::MIRType *structType = structTypes[currentType];
					const auto &fields = structFields[currentType];

					// Find the field index
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fields.size(); i++) {
						if (fields[i].first == ma->memberName) {
							fieldIndex = static_cast<int>(i);
							fieldType = fields[i].second;
							break;
						}
					}

					if (fieldIndex < 0) {
						throw std::runtime_error("Unknown field '" + ma->memberName + "' in struct '" + currentType + "'");
					}

					// GEP to get pointer to the field
					currentPtr = builder->createStructGEP(structType, currentPtr, fieldIndex, ma->memberName);
					currentType = fieldType;
				}

				// Now objectPtr points to the intermediate struct field
				objectPtr = currentPtr;
				varType = currentType;
			} else {
				// Other complex expression
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				varType = getExpressionMaxonType(memberAccessExpr->object.get(), variableTypes, structTypes, structFields, currentReceiverType);
				objectPtr = objectValue;
			}
		} else {
			// Simple variable access
			varType = variableTypes[memberAccessExpr->objectName];
			mir::MIRValue *structAlloca = namedValues[memberAccessExpr->objectName];
			if (!structAlloca) {
				// Check for implicit field access
				if (!currentReceiverType.empty()) {
					auto fieldsIt = structFields.find(currentReceiverType);
					if (fieldsIt != structFields.end()) {
						int fieldIndex = -1;
						std::string fieldType;
						for (size_t i = 0; i < fieldsIt->second.size(); i++) {
							if (fieldsIt->second[i].first == memberAccessExpr->objectName) {
								fieldIndex = static_cast<int>(i);
								fieldType = fieldsIt->second[i].second;
								break;
							}
						}
						if (fieldIndex >= 0) {
							mir::MIRValue *selfAlloca = namedValues["self"];
							if (selfAlloca) {
								mir::MIRValue *selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType *structType = structTypes[currentReceiverType];
								objectPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, memberAccessExpr->objectName + ".ptr");
								varType = fieldType;
								goto handle_member_access;
							}
						}
					}
				}
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

	handle_member_access:

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

			// Check if the field is an array type - return pointer instead of loading
			std::string fieldTypeStr = fields[fieldIndex].second;
			if (fieldTypeStr.size() > 2 && fieldTypeStr[0] == '[') {
				// Array field - return pointer to the array (for subsequent indexing)
				return fieldPtr;
			}

			// Load the field value
			mir::MIRType *fieldType = structType->fieldTypes[fieldIndex];
			return builder->createLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
		}

		// Handle array.length
		if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
			// First check for hidden __length alloca (stack-allocated arrays)
			std::string lengthVar = memberAccessExpr->objectName + ".__length";
			mir::MIRValue *lengthAlloca = namedValues[lengthVar];
			if (lengthAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
			}

			// For heap-allocated arrays with header layout, read from dataPtr - 8
			// Layout: [length:i32][capacity:i32][...data...]
			//              -8          -4           0+
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				// Load the data pointer
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				// Get pointer to length at offset -8 (use GEP with i8 type for byte offsets)
				mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
															  {builder->getInt64(-8)}, "length.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");
			}
			throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
		}

		// Handle array.capacity (dynamic arrays only)
		if (memberAccessExpr->memberName == "capacity" && !memberAccessExpr->object) {
			// First check for hidden __capacity alloca (old-style heap arrays)
			std::string capacityVar = memberAccessExpr->objectName + ".__capacity";
			mir::MIRValue *capacityAlloca = namedValues[capacityVar];
			if (capacityAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");
			}

			// For heap-allocated arrays with header layout, read from dataPtr - 4
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				// Load the data pointer
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				// Get pointer to capacity at offset -4 (use GEP with i8 type for byte offsets)
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
														   {builder->getInt64(-4)}, "cap.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), capPtr, "capacity");
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
		mir::MIRValue *indexVal = generateExpr(arrayIndexExpr->index.get());
		if (!indexVal) {
			throw std::runtime_error("Failed to generate array index");
		}

		// Handle complex array access (e.g., struct.field[i])
		if (arrayIndexExpr->hasArrayExpr()) {
			// Generate the array expression (e.g., struct.field)
			mir::MIRValue *arrayVal = generateExpr(arrayIndexExpr->arrayExpr.get());
			if (!arrayVal) {
				throw std::runtime_error("Failed to generate array expression");
			}

			// For member access on struct, the result is a pointer to the array field
			// We need to determine the element type from the expression
			std::string elementTypeStr = "byte"; // Default for now
			bool isUnsizedArray = false;

			// Get the array field type using getExpressionMaxonType for nested member access
			std::string arrayFieldType = getExpressionMaxonType(arrayIndexExpr->arrayExpr.get(), variableTypes, structTypes, structFields, currentReceiverType);
			logTrace("ArrayIndex in '" + currentReceiverType + "': arrayFieldType = '" + arrayFieldType + "'");
			if (arrayFieldType.size() > 2 && arrayFieldType[0] == '[') {
				size_t closeBracket = arrayFieldType.find(']');
				if (closeBracket != std::string::npos && closeBracket + 1 < arrayFieldType.size()) {
					std::string sizeStr = arrayFieldType.substr(1, closeBracket - 1);
					elementTypeStr = arrayFieldType.substr(closeBracket + 1);
					// Check for unsized array: []type (empty size)
					isUnsizedArray = sizeStr.empty();
					logTrace("ArrayIndex in '" + currentReceiverType + "': elementTypeStr = '" + elementTypeStr + "', isUnsizedArray = " + (isUnsizedArray ? "true" : "false"));
				}
			}

			mir::MIRType *elementType = getTypeFromString(elementTypeStr);

			// For unsized arrays, arrayVal points to the fat pointer struct {ptr, i32}
			// We need to extract the data pointer (field 0) and load it
			mir::MIRValue *dataPtr = arrayVal;
			if (isUnsizedArray) {
				// Get the unsized array struct type
				mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
					"__unsized_array_" + elementTypeStr,
					{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
				// Get pointer to the data pointer field (field 0)
				mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, arrayVal, 0, "unsized.data.ptr");
				// Load the actual data pointer
				dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrField, "unsized.data");
			}

			mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, dataPtr, indexVal, "fieldarray.idx");
			return builder->createLoad(elementType, elementPtr, "fieldarray.elem");
		}

		// Simple array[i] access
		mir::MIRValue *alloca = namedValues[arrayIndexExpr->arrayName];
		if (!alloca) {
			// If we're in a method and array not found, check if it's a struct field
			// This enables implicit self field access for arrays: 'data[i]' instead of 'self.data[i]'
			if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == arrayIndexExpr->arrayName) {
							fieldIndex = static_cast<int>(i);
							fieldType = fieldsIt->second[i].second;
							break;
						}
					}

					if (fieldIndex >= 0 && fieldType.size() > 2 && fieldType[0] == '[') {
						// Found array field - generate access through implicit 'self' parameter
						mir::MIRValue *selfAlloca = namedValues["self"];
						if (selfAlloca) {
							mir::MIRValue *selfPtr;
							if (isStructParameter("self")) {
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}

							// Get field pointer with GEP
							mir::MIRType *structType = structTypes[currentReceiverType];
							mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, arrayIndexExpr->arrayName + ".ptr");

							// Determine element type
							size_t closeBracket = fieldType.find(']');
							std::string elementTypeStr = fieldType.substr(closeBracket + 1);
							std::string sizeStr = fieldType.substr(1, closeBracket - 1);
							bool isUnsizedArray = sizeStr.empty();
							mir::MIRType *elementType = getTypeFromString(elementTypeStr);

							// For unsized arrays, load the data pointer from fat pointer
							mir::MIRValue *dataPtr = fieldPtr;
							if (isUnsizedArray) {
								mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
									"__unsized_array_" + elementTypeStr,
									{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
								mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, "unsized.data.ptr");
								dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrField, "unsized.data");
							}

							mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, dataPtr, indexVal, "implicitfield.array.idx");
							return builder->createLoad(elementType, elementPtr, "implicitfield.array.elem");
						}
					}
				}
			}
			throw std::runtime_error("Unknown array name: " + arrayIndexExpr->arrayName);
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
		// For == and != on Equatable struct types, route to equals method
		if (binExpr->op == 'E' || binExpr->op == 'N') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get(), variableTypes, structTypes, structFields, currentReceiverType);

			// Check if this is an Equatable struct type
			auto structIt = structTypes.find(leftType);
			if (structIt != structTypes.end()) {
				auto conformsIt = structConformsTo.find(leftType);
				if (conformsIt != structConformsTo.end()) {
					bool isEquatable = false;
					for (const auto &iface : conformsIt->second) {
						if (iface == "Equatable") {
							isEquatable = true;
							break;
						}
					}

					if (isEquatable) {
						// Generate call to TypeName.equals(left, right)
						std::string equalsFuncName = leftType + ".equals";
						mir::MIRFunction *equalsFunc = module->getFunction(equalsFuncName);

						if (equalsFunc) {
							// For struct types, we need to pass pointers, not loaded values
							// Get pointer to left operand
							mir::MIRValue *leftPtr = nullptr;
							if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
								leftPtr = namedValues[varExpr->name];
							} else if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(binExpr->left.get())) {
								leftPtr = generateStringLiteral(strLit);
							} else {
								leftPtr = generateExpr(binExpr->left.get());
							}

							// Get pointer to right operand
							mir::MIRValue *rightPtr = nullptr;
							if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
								rightPtr = namedValues[varExpr->name];
							} else if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(binExpr->right.get())) {
								rightPtr = generateStringLiteral(strLit);
							} else {
								rightPtr = generateExpr(binExpr->right.get());
							}

							if (!leftPtr || !rightPtr) {
								throw std::runtime_error("Failed to generate Equatable comparison operands");
							}

							std::vector<mir::MIRValue *> args = {leftPtr, rightPtr};
							mir::MIRValue *result = builder->createCall(equalsFunc, args, "eqtmp");

							// For !=, negate the result
							if (binExpr->op == 'N') {
								result = builder->createICmpEq(result,
															   mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0), "neqtmp");
							}

							return result;
						}
					}
				}
			}
		}

		// Special handling for string + string -> call concat and heap-allocate result
		if (binExpr->op == '+') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get(), variableTypes, structTypes, structFields, currentReceiverType);
			std::string rightType = getExpressionMaxonType(binExpr->right.get(), variableTypes, structTypes, structFields, currentReceiverType);

			if (leftType == "string" && rightType == "string") {
				// Initialize heap management for _managed_string_alloc
				initHeapManagement();

				// Get pointers to both string structs
				mir::MIRValue *leftPtr = nullptr;
				mir::MIRValue *rightPtr = nullptr;

				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
					leftPtr = namedValues[varExpr->name];
					if (isStructParameter(varExpr->name)) {
						leftPtr = builder->createLoad(mir::MIRType::getPtr(), leftPtr, "left_str");
					}
				} else {
					leftPtr = generateExpr(binExpr->left.get());
				}

				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
					rightPtr = namedValues[varExpr->name];
					if (isStructParameter(varExpr->name)) {
						rightPtr = builder->createLoad(mir::MIRType::getPtr(), rightPtr, "right_str");
					}
				} else {
					rightPtr = generateExpr(binExpr->right.get());
				}

				mir::MIRType *stringType = structTypes["string"];
				mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
				mir::MIRType *managedStringDataType = structTypes["__ManagedStringData"];
				if (!managedStringDataType) {
					managedStringDataType = module->getOrCreateStructType(
						"__ManagedStringData",
						{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
					structTypes["__ManagedStringData"] = managedStringDataType;
				}

				// Get _managed pointers from both strings (field 0 of string is _managed ptr)
				mir::MIRValue *leftManagedFieldPtr = builder->createStructGEP(stringType, leftPtr, 0, "left_managed_field");
				mir::MIRValue *leftManagedPtr = builder->createLoad(mir::MIRType::getPtr(), leftManagedFieldPtr, "left_managed_ptr");
				mir::MIRValue *rightManagedFieldPtr = builder->createStructGEP(stringType, rightPtr, 0, "right_managed_field");
				mir::MIRValue *rightManagedPtr = builder->createLoad(mir::MIRType::getPtr(), rightManagedFieldPtr, "right_managed_ptr");

				// Get lengths from __ManagedStringData (field 1 = _len)
				mir::MIRValue *leftLenPtr = builder->createStructGEP(managedStringDataType, leftManagedPtr, 1, "left_len_ptr");
				mir::MIRValue *leftLen = builder->createLoad(mir::MIRType::getInt32(), leftLenPtr, "left_len");
				mir::MIRValue *rightLenPtr = builder->createStructGEP(managedStringDataType, rightManagedPtr, 1, "right_len_ptr");
				mir::MIRValue *rightLen = builder->createLoad(mir::MIRType::getInt32(), rightLenPtr, "right_len");

				// Calculate combined length
				mir::MIRValue *combinedLen = builder->createAdd(leftLen, rightLen, "combined_len");

				// Allocate heap buffer with refcount header using _managed_string_alloc
				mir::MIRFunction *stringAllocFunc = module->getFunction("_managed_string_alloc");
				if (!stringAllocFunc) {
					stringAllocFunc = builder->declareFunction("_managed_string_alloc",
															   mir::MIRType::getPtr(),
															   {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
				}
				// Create tag for tracking
				mir::MIRValue *tag = module->createGlobalString(".__tag.str.concat", "string concat");

				mir::MIRValue *combinedLen64 = builder->createSExt(combinedLen, mir::MIRType::getInt64(), "combined_len64");
				mir::MIRValue *newDataPtr = builder->createCall(stringAllocFunc, {combinedLen64, tag}, "concat_alloc");

				// Get memcpy function
				mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");

				// Copy left string data - get buffer ptr from __ManagedStringData
				mir::MIRValue *leftBufFieldPtr = builder->createStructGEP(managedStringDataType, leftManagedPtr, 0, "left_buf_field");
				mir::MIRValue *leftDataPtrPtr = builder->createStructGEP(unsizedArrayType, leftBufFieldPtr, 0, "left_data_ptr_ptr");
				mir::MIRValue *leftDataPtr = builder->createLoad(mir::MIRType::getPtr(), leftDataPtrPtr, "left_data_ptr");
				mir::MIRValue *leftLen64 = builder->createSExt(leftLen, mir::MIRType::getInt64(), "left_len64");
				builder->createCall(memcpyFunc, {newDataPtr, leftDataPtr, leftLen64}, "copy_left");

				// Copy right string data (at offset = leftLen)
				mir::MIRValue *destOffset = builder->createGEP(mir::MIRType::getInt8(), newDataPtr, {leftLen64}, "dest_offset");
				mir::MIRValue *rightBufFieldPtr = builder->createStructGEP(managedStringDataType, rightManagedPtr, 0, "right_buf_field");
				mir::MIRValue *rightDataPtrPtr = builder->createStructGEP(unsizedArrayType, rightBufFieldPtr, 0, "right_data_ptr_ptr");
				mir::MIRValue *rightDataPtr = builder->createLoad(mir::MIRType::getPtr(), rightDataPtrPtr, "right_data_ptr");
				mir::MIRValue *rightLen64 = builder->createSExt(rightLen, mir::MIRType::getInt64(), "right_len64");
				builder->createCall(memcpyFunc, {destOffset, rightDataPtr, rightLen64}, "copy_right");

				// Heap-allocate the __ManagedStringData struct for the result
				mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
				mir::MIRValue *managedSize = builder->getInt64(32);
				mir::MIRValue *newManagedPtr = builder->createCall(mallocFunc, {managedSize}, "concat_managed");

				// Set up the __ManagedStringData struct
				// Field 0: _buffer (fat pointer)
				mir::MIRValue *newBufFieldPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 0, "new_buf_field");
				mir::MIRValue *newBufPtrPtr = builder->createStructGEP(unsizedArrayType, newBufFieldPtr, 0, "new_buf_ptr_ptr");
				builder->createStore(newDataPtr, newBufPtrPtr);
				mir::MIRValue *newBufLenPtr = builder->createStructGEP(unsizedArrayType, newBufFieldPtr, 1, "new_buf_len_ptr");
				builder->createStore(combinedLen, newBufLenPtr);

				// Field 1: _len
				mir::MIRValue *newLenPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 1, "new_len_ptr");
				builder->createStore(combinedLen, newLenPtr);

				// Field 2: _capacity
				mir::MIRValue *newCapPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 2, "new_cap_ptr");
				builder->createStore(combinedLen, newCapPtr);

				// Field 3: _iterPos
				mir::MIRValue *newIterPosPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 3, "new_iterpos_ptr");
				builder->createStore(builder->getInt32(0), newIterPosPtr);

				// Create the result string struct on stack
				mir::MIRValue *resultAlloca = builder->createAlloca(stringType, "concat_result");

				// Field 0: _managed (ptr to __ManagedStringData)
				mir::MIRValue *resultManagedFieldPtr = builder->createStructGEP(stringType, resultAlloca, 0, "result_managed_field");
				builder->createStore(newManagedPtr, resultManagedFieldPtr);

				// Track for cleanup at scope exit - track both the buffer and the managed struct
				mir::MIRValue *dataPtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), "concat.heap.ptr");
				builder->createStore(newDataPtr, dataPtrAlloca);
				if (!scopeStack.empty()) {
					scopeStack.back().heapAllocatedStrings.push_back({"concat.heap", dataPtrAlloca});
				}

				return resultAlloca;
			}
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
		case '&': // bitwise AND
			return builder->createAnd(left, right, "bandtmp");
		case '|': // bitwise OR
			return builder->createOr(left, right, "bortmp");
		case '^': // bitwise XOR
			return builder->createXor(left, right, "bxortmp");
		case 'S': // shift left (<<)
			return builder->createShl(left, right, "shltmp");
		case 'H': // shift right (>>) - logical shift for unsigned semantics
			return builder->createLShr(left, right, "lshrtmp");
		case 'A': // logical AND (and keyword)
			// Both operands should be booleans (i1 or i32)
			// Result is 1 if both are non-zero, 0 otherwise
			return builder->createAnd(left, right, "andtmp");
		case 'O': // logical OR (or keyword)
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

		// Handle string intrinsics (__string_* functions)
		if (isStringIntrinsic(callExpr->callee)) {
			return generateStringIntrinsic(callExpr);
		}

		// Initialize standard library if needed
		if (callExpr->callee == "print" || callExpr->callee == "print_float") {
			initStandardLibrary();
		}

		// Special handling for print(string) - generate inline code to call write_stdout
		if (callExpr->callee == "print" && callExpr->args.size() == 1) {
			// Check if the argument is a string type
			std::string argType;
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].get())) {
				argType = variableTypes[varExpr->name];
			} else if (dynamic_cast<StringLiteralExprAST *>(callExpr->args[0].get())) {
				argType = "string";
			} else if (auto *callArg = dynamic_cast<CallExprAST *>(callExpr->args[0].get())) {
				// Check if it's a method call returning string (e.g., s.toLower())
				argType = getExpressionMaxonType(callArg, variableTypes, structTypes, structFields, currentReceiverType);
			} else if (auto *binArg = dynamic_cast<BinaryExprAST *>(callExpr->args[0].get())) {
				// Check if it's a binary expression returning string (e.g., a + b where both are strings)
				argType = getExpressionMaxonType(binArg, variableTypes, structTypes, structFields, currentReceiverType);
			}

			if (argType == "string") {
				// Generate inline string printing:
				// string = { _managed _ManagedString }
				// _ManagedString = { _buffer []byte, _len int, _capacity int, _iterPos int }
				// []byte = { ptr, i32 }
				//
				// So: string._managed._buffer.ptr = field 0, field 0, field 0
				//     string._managed._len = field 0, field 1

				mir::MIRValue *strPtr = nullptr;
				mir::MIRType *stringType = structTypes["string"];

				if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].get())) {
					mir::MIRValue *alloca = namedValues[varExpr->name];
					if (isStructParameter(varExpr->name)) {
						strPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
					} else {
						strPtr = alloca;
					}
				} else if (dynamic_cast<StringLiteralExprAST *>(callExpr->args[0].get())) {
					// String literal - generate it and get its pointer
					strPtr = generateExpr(callExpr->args[0].get());
				} else if (dynamic_cast<BinaryExprAST *>(callExpr->args[0].get())) {
					// Binary expression (e.g., a + b) returns a pointer to string struct
					strPtr = generateExpr(callExpr->args[0].get());
				} else {
					// Method call or other expression returning a string value
					// Generate the expression - it may return a string VALUE (not pointer)
					mir::MIRValue *strValue = generateExpr(callExpr->args[0].get());
					// Check if it's already a pointer (alloca) or a loaded value
					// If the result type is %string (struct), we need to store it
					// If it's ptr, we can use it directly
					if (strValue->type->isPointer()) {
						strPtr = strValue;
					} else {
						// Allocate storage and store the value
						strPtr = builder->createAlloca(stringType, "print.str.tmp");
						builder->createStore(strValue, strPtr);
					}
				}

				// Get __ManagedStringData type (the internal struct that _managed points to)
				mir::MIRType *managedStringDataType = structTypes["__ManagedStringData"];
				mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

				// Get _managed field pointer: string._managed (field 0) - this is a ptr field
				mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, strPtr, 0, "_managed.field.ptr");
				// Load the _managed pointer value (it points to __ManagedStringData)
				mir::MIRValue *managedPtr = builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, "_managed.ptr");

				// Get the data pointer: managedData._buffer.ptr (field 0, subfield 0)
				mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringDataType, managedPtr, 0, "_buffer.ptr");
				mir::MIRValue *dataPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "data_ptr_ptr");
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrPtr, "data_ptr");

				// Get the length: managedData._len (field 1)
				mir::MIRValue *lenPtr = builder->createStructGEP(managedStringDataType, managedPtr, 1, "_len.ptr");
				mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len"); // Call write_stdout(data, length)
				mir::MIRFunction *writeStdout = getOrDeclareFunction("write_stdout",
																	 mir::MIRType::getInt32(),
																	 {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
				mir::MIRValue *result = builder->createCall(writeStdout, {dataPtr, len});

				// Print newline if write succeeded
				mir::MIRBasicBlock *thenBlock = builder->createBasicBlock("print_newline");
				mir::MIRBasicBlock *contBlock = builder->createBasicBlock("print_cont");

				// Check if result >= 0
				mir::MIRValue *zero = builder->getInt32(0);
				mir::MIRValue *cond = builder->createICmpSGE(result, zero, "write_ok");
				builder->createCondBr(cond, thenBlock, contBlock);

				// Print newline
				builder->setInsertPoint(thenBlock);
				mir::MIRValue *newlineArr = builder->createAlloca(mir::MIRType::getArray(mir::MIRType::getInt8(), 1), "newline");
				mir::MIRValue *newlinePtr = builder->createGEP(mir::MIRType::getInt8(), newlineArr,
															   {builder->getInt64(0), builder->getInt64(0)}, "newline_ptr");
				builder->createStore(builder->getInt8(10), newlinePtr); // '\n'
				builder->createCall(writeStdout, {newlineArr, builder->getInt32(1)});
				builder->createBr(contBlock);

				builder->setInsertPoint(contBlock);
				return result;
			}
		}

		// Check if this is an extern function that should go through Safe FFI
		bool usesSafeFfi = externFunctions.find(callExpr->callee) != externFunctions.end();

		// Look up function - use O(1) function ID lookup if available
		mir::MIRFunction *calleeF = nullptr;

		if (callExpr->functionId != SIZE_MAX) {
			// Fast path: function was resolved during semantic analysis
			auto it = functionIdToMIR.find(callExpr->functionId);
			if (it != functionIdToMIR.end()) {
				calleeF = it->second;
			}
		}

		// Fallback to name-based lookup if function ID not available
		if (!calleeF) {
			calleeF = module->getFunction(callExpr->callee);

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
		}

		if (!calleeF) {
			throw std::runtime_error("Unknown function referenced: " + callExpr->callee);
		}

		// Generate arguments
		std::vector<mir::MIRValue *> argsV;
		size_t argIdx = 0;

		// For sibling method calls, inject 'self' as the first argument
		if (callExpr->isSiblingMethodCall && !currentReceiverType.empty()) {
			// Load self pointer from the first parameter (which is always 'self' in methods)
			if (namedValues.find("self") != namedValues.end()) {
				mir::MIRValue *selfAlloca = namedValues["self"];
				mir::MIRValue *selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self");
				argsV.push_back(selfPtr);
				argIdx++;
			}
		}

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
					mir::MIRValue *ptrVal;
					if (stackAllocatedArrays.count(varExpr->name) > 0) {
						// Stack-allocated array: alloca IS the array pointer
						ptrVal = alloca;
					} else {
						// Heap-allocated array: alloca contains a pointer to the array
						ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
					}
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

//==============================================================================
// String Intrinsic Generation
//==============================================================================

bool MIRCodeGenerator::isStringIntrinsic(const std::string &name) {
	return name.rfind("__string_", 0) == 0;
}

mir::MIRValue *MIRCodeGenerator::generateStringIntrinsic(CallExprAST *callExpr) {
	const std::string &name = callExpr->callee;

	// Get or create the internal __ManagedStringData struct type
	// This is the layout of data pointed to by the opaque _ManagedString pointer
	// Layout: { _buffer []byte, _len int, _capacity int, _iterPos int }
	mir::MIRType *managedStringType = structTypes["__ManagedStringData"];
	if (!managedStringType) {
		mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
		if (!unsizedArrayType) {
			unsizedArrayType = module->getOrCreateStructType(
				"__unsized_array_byte",
				{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
			structTypes["__unsized_array_byte"] = unsizedArrayType;
		}
		managedStringType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["__ManagedStringData"] = managedStringType;
	}
	mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

	// Helper lambda to get the _ManagedString POINTER VALUE from an expression
	// _ManagedString is an opaque pointer - the `_managed` field stores a ptr
	// This returns the pointer value (to be used directly for GEP on __ManagedStringData)
	auto getManagedStringPtr = [&](ExprAST *arg) -> mir::MIRValue * {
		// Check if this is an implicit field access (just a field name like _managed)
		if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg)) {
			// Check for implicit field access (field name from current struct)
			if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == varExpr->name) {
							// Found the field - get pointer to it via self
							mir::MIRValue *selfAlloca = namedValues["self"];
							if (selfAlloca) {
								mir::MIRValue *selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType *structType = structTypes[currentReceiverType];
								// Get pointer to the _managed field
								mir::MIRValue *managedFieldPtr = builder->createStructGEP(structType, selfPtr, static_cast<int>(i), varExpr->name + ".ptr");
								// _managed is a ptr type - load the pointer value
								return builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, varExpr->name + ".val");
							}
						}
					}
				}
			}
			// Regular variable - return its value (should be a ptr)
			auto it = namedValues.find(varExpr->name);
			if (it != namedValues.end()) {
				return builder->createLoad(mir::MIRType::getPtr(), it->second, varExpr->name + ".val");
			}
		}
		// Check for member access like str._managed or _str._managed
		if (auto *memberExpr = dynamic_cast<MemberAccessExprAST *>(arg)) {
			// Generate the object pointer
			mir::MIRValue *objectPtr = nullptr;
			std::string objectType;

			if (memberExpr->object) {
				// Explicit object expression - generate it
				objectPtr = generateExpr(memberExpr->object.get());
				objectType = getExpressionMaxonType(memberExpr->object.get(), variableTypes, structTypes, structFields, currentReceiverType);
			} else {
				// Just objectName - could be a local variable or an implicit self field access
				auto allocaIt = namedValues.find(memberExpr->objectName);
				// Check if it exists AND has a non-null value (namedValues can have null entries)
				if (allocaIt != namedValues.end() && allocaIt->second != nullptr) {
					// It's a local variable
					objectType = variableTypes[memberExpr->objectName];
					if (isStructParameter(memberExpr->objectName)) {
						objectPtr = builder->createLoad(mir::MIRType::getPtr(), allocaIt->second, "struct.ptr");
					} else {
						objectPtr = allocaIt->second;
					}
				} else if (!currentReceiverType.empty()) {
					// Check if it's an implicit self field access
					auto fieldsIt = structFields.find(currentReceiverType);
					if (fieldsIt != structFields.end()) {
						for (size_t i = 0; i < fieldsIt->second.size(); i++) {
							if (fieldsIt->second[i].first == memberExpr->objectName) {
								// It's a field of self - get pointer to it
								mir::MIRValue *selfAlloca = namedValues["self"];
								if (!selfAlloca) {
									throw std::runtime_error("Field found but no self: " + memberExpr->objectName + " in " + currentReceiverType);
								}
								mir::MIRValue *selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType *selfStructType = structTypes[currentReceiverType];
								if (!selfStructType) {
									throw std::runtime_error("No struct type for: " + currentReceiverType);
								}
								objectPtr = builder->createStructGEP(selfStructType, selfPtr, static_cast<int>(i), memberExpr->objectName + ".field.ptr");
								objectType = fieldsIt->second[i].second;
								break;
							}
						}
					}
				}

				if (!objectPtr) {
					std::string debugInfo = "Unknown variable: " + memberExpr->objectName +
											" (currentReceiverType=" + currentReceiverType +
											", structFields has " + std::to_string(structFields.count(currentReceiverType)) + " entry";
					auto fieldsIt = structFields.find(currentReceiverType);
					if (fieldsIt != structFields.end()) {
						debugInfo += ", fields: [";
						for (size_t i = 0; i < fieldsIt->second.size(); i++) {
							if (i > 0)
								debugInfo += ", ";
							debugInfo += fieldsIt->second[i].first + ":" + fieldsIt->second[i].second;
						}
						debugInfo += "]";
					}
					debugInfo += ")";
					throw std::runtime_error(debugInfo);
				}
			}

			// Find the _managed field index and load the pointer value
			auto fieldsIt = structFields.find(objectType);
			if (fieldsIt != structFields.end()) {
				for (size_t i = 0; i < fieldsIt->second.size(); i++) {
					if (fieldsIt->second[i].first == memberExpr->memberName) {
						mir::MIRType *structType = structTypes[objectType];
						mir::MIRValue *fieldPtr = builder->createStructGEP(structType, objectPtr, static_cast<int>(i), memberExpr->memberName + ".ptr");
						// _managed is ptr type - load the pointer value
						return builder->createLoad(mir::MIRType::getPtr(), fieldPtr, memberExpr->memberName + ".val");
					}
				}
			}
		}
		// Fallback - generate expr and assume it's a ptr value
		return generateExpr(arg);
	};

	// __string_len(managed) - get byte length
	if (name == "__string_len") {
		if (callExpr->args.size() != 1) {
			throw std::runtime_error("__string_len requires exactly 1 argument");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());

		// _len is at field index 1
		mir::MIRValue *lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "managed._len");
		return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
	}

	// __string_buffer(managed) - get pointer to the raw buffer bytes
	if (name == "__string_buffer") {
		if (callExpr->args.size() != 1) {
			throw std::runtime_error("__string_buffer requires exactly 1 argument");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());

		// Get buffer pointer: _buffer.ptr is at field 0, subfield 0
		mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "managed._buffer");
		mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "buffer.ptr.ptr");
		return builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, "buffer.ptr");
	}

	// __string_byte_at(managed, index) - get byte at index
	if (name == "__string_byte_at") {
		if (callExpr->args.size() != 2) {
			throw std::runtime_error("__string_byte_at requires exactly 2 arguments");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());
		mir::MIRValue *index = generateExpr(callExpr->args[1].get());

		// Get buffer pointer: _buffer.ptr is at field 0, subfield 0
		mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "managed._buffer");
		mir::MIRValue *bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "buffer.ptr.ptr");
		mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, "buffer.ptr");

		// Index into buffer
		mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
		mir::MIRValue *bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), bufferPtr, index64, "byte.ptr");
		return builder->createLoad(mir::MIRType::getInt8(), bytePtr, "byte");
	}

	// __string_iter_pos(managed) - get current iteration position
	if (name == "__string_iter_pos") {
		if (callExpr->args.size() != 1) {
			throw std::runtime_error("__string_iter_pos requires exactly 1 argument");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());

		// _iterPos is at field index 3
		mir::MIRValue *iterPosPtr = builder->createStructGEP(managedStringType, managedPtr, 3, "managed._iterPos");
		return builder->createLoad(mir::MIRType::getInt32(), iterPosPtr, "iterPos");
	}

	// __string_with_iter_pos(managed, pos) - create copy with new iteration position
	// Returns a heap-allocated _ManagedString pointer (opaque ptr to __ManagedStringData)
	// This shares the same buffer as the original string (COW semantics)
	if (name == "__string_with_iter_pos") {
		if (callExpr->args.size() != 2) {
			throw std::runtime_error("__string_with_iter_pos requires exactly 2 arguments");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());
		mir::MIRValue *newPos = generateExpr(callExpr->args[1].get());

		// Heap-allocate a new __ManagedStringData struct (32 bytes for alignment)
		mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
		mir::MIRValue *managedSize = builder->getInt64(32);
		mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "new.managed");

		// Copy buffer field (ptr and len) - sharing the underlying buffer
		mir::MIRValue *srcBufferPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
		mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
		// Copy buffer.ptr
		mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufferPtr, 0, "src.ptr.ptr");
		mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
		mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "buffer.ptr");
		builder->createStore(bufferPtr, dstPtrPtr);
		// Copy buffer.len
		mir::MIRValue *srcBufLenPtr = builder->createStructGEP(unsizedArrayType, srcBufferPtr, 1, "src.buflen.ptr");
		mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
		mir::MIRValue *bufLen = builder->createLoad(mir::MIRType::getInt32(), srcBufLenPtr, "buf.len");
		builder->createStore(bufLen, dstBufLenPtr);

		// Copy _len
		mir::MIRValue *srcLenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
		mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
		mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), srcLenPtr, "len");
		builder->createStore(len, dstLenPtr);

		// Copy _capacity
		mir::MIRValue *srcCapPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
		mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
		mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), srcCapPtr, "capacity");
		builder->createStore(cap, dstCapPtr);

		// Store the new iteration position
		mir::MIRValue *dstIterPosPtr = builder->createStructGEP(managedStringType, newManaged, 3, "dst._iterPos");
		builder->createStore(newPos, dstIterPosPtr);

		// Return the pointer to the new __ManagedStringData (opaque _ManagedString)
		return newManaged;
	}

	// __string_slice(managed, start, end) - extract substring
	// Returns a heap-allocated _ManagedString pointer (opaque ptr to __ManagedStringData)
	if (name == "__string_slice") {
		if (callExpr->args.size() != 3) {
			throw std::runtime_error("__string_slice requires exactly 3 arguments");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());
		mir::MIRValue *startIdx = generateExpr(callExpr->args[1].get());
		mir::MIRValue *endIdx = generateExpr(callExpr->args[2].get());

		// Calculate new length
		mir::MIRValue *newLen = builder->createSub(endIdx, startIdx, "slice.len");

		// Get source buffer pointer
		mir::MIRValue *srcBufferPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
		mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufferPtr, 0, "src.ptr.ptr");
		mir::MIRValue *srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");

		// Offset to start position
		mir::MIRValue *start64 = builder->createSExt(startIdx, mir::MIRType::getInt64(), "start64");
		mir::MIRValue *slicePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, start64, "slice.ptr");

		// Allocate new buffer for the string data
		mir::MIRValue *newLen64 = builder->createSExt(newLen, mir::MIRType::getInt64(), "newlen64");
		mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
		mir::MIRValue *newBuffer = builder->createCall(mallocFunc, {newLen64}, "slice.buffer");

		// Copy data: memcpy(dest, src, len)
		mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
															{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
		builder->createCall(memcpyFunc, {newBuffer, slicePtr, newLen64});

		// Heap-allocate the __ManagedStringData struct (32 bytes for alignment)
		mir::MIRValue *managedSize = builder->getInt64(32);
		mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "slice.managed");

		// Set buffer fields: _buffer.ptr
		mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
		mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
		builder->createStore(newBuffer, dstPtrPtr);
		// Set _buffer.len
		mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
		builder->createStore(newLen, dstBufLenPtr);

		// Set _len
		mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
		builder->createStore(newLen, dstLenPtr);

		// Set _capacity (same as len for new allocation)
		mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
		builder->createStore(newLen, dstCapPtr);

		// Set _iterPos to 0
		mir::MIRValue *dstIterPosPtr = builder->createStructGEP(managedStringType, newManaged, 3, "dst._iterPos");
		builder->createStore(builder->getInt32(0), dstIterPosPtr);

		// Return the pointer to the new __ManagedStringData (opaque _ManagedString)
		return newManaged;
	}

	// __string_concat(m1, m2) - concatenate two strings
	// Returns a heap-allocated _ManagedString pointer (opaque ptr to __ManagedStringData)
	if (name == "__string_concat") {
		if (callExpr->args.size() != 2) {
			throw std::runtime_error("__string_concat requires exactly 2 arguments");
		}
		mir::MIRValue *m1Ptr = getManagedStringPtr(callExpr->args[0].get());
		mir::MIRValue *m2Ptr = getManagedStringPtr(callExpr->args[1].get());

		// Get lengths
		mir::MIRValue *len1Ptr = builder->createStructGEP(managedStringType, m1Ptr, 1, "m1._len");
		mir::MIRValue *len1 = builder->createLoad(mir::MIRType::getInt32(), len1Ptr, "len1");
		mir::MIRValue *len2Ptr = builder->createStructGEP(managedStringType, m2Ptr, 1, "m2._len");
		mir::MIRValue *len2 = builder->createLoad(mir::MIRType::getInt32(), len2Ptr, "len2");

		// Calculate total length
		mir::MIRValue *totalLen = builder->createAdd(len1, len2, "total.len");
		mir::MIRValue *totalLen64 = builder->createSExt(totalLen, mir::MIRType::getInt64(), "total64");

		// Get source pointers
		mir::MIRValue *buf1Ptr = builder->createStructGEP(managedStringType, m1Ptr, 0, "m1._buffer");
		mir::MIRValue *ptr1Ptr = builder->createStructGEP(unsizedArrayType, buf1Ptr, 0, "ptr1.ptr");
		mir::MIRValue *ptr1 = builder->createLoad(mir::MIRType::getPtr(), ptr1Ptr, "ptr1");

		mir::MIRValue *buf2Ptr = builder->createStructGEP(managedStringType, m2Ptr, 0, "m2._buffer");
		mir::MIRValue *ptr2Ptr = builder->createStructGEP(unsizedArrayType, buf2Ptr, 0, "ptr2.ptr");
		mir::MIRValue *ptr2 = builder->createLoad(mir::MIRType::getPtr(), ptr2Ptr, "ptr2");

		// Allocate buffer for string data
		mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
		mir::MIRValue *newBuffer = builder->createCall(mallocFunc, {totalLen64}, "concat.buffer");

		// Copy first string
		mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
															{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
		mir::MIRValue *len1_64 = builder->createSExt(len1, mir::MIRType::getInt64(), "len1_64");
		builder->createCall(memcpyFunc, {newBuffer, ptr1, len1_64});

		// Copy second string to newBuffer + len1 (pointer arithmetic with single index)
		mir::MIRValue *destOffset = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {len1_64}, "dest.offset");
		mir::MIRValue *len2_64 = builder->createSExt(len2, mir::MIRType::getInt64(), "len2_64");
		builder->createCall(memcpyFunc, {destOffset, ptr2, len2_64});

		// Heap-allocate the __ManagedStringData struct (24 bytes: 2*ptr + 3*i32 + padding)
		// __ManagedStringData = { __unsized_array_byte, i32, i32, i32 }
		// __unsized_array_byte = { ptr, i32 } = 16 bytes on 64-bit
		// So total: 16 + 4 + 4 + 4 = 28 bytes, rounded to 32 for alignment
		mir::MIRValue *managedSize = builder->getInt64(32);
		mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "concat.managed");

		// Set buffer fields: _buffer.ptr
		mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
		mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
		builder->createStore(newBuffer, dstPtrPtr);
		// Set _buffer.len
		mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
		builder->createStore(totalLen, dstBufLenPtr);

		// Set _len
		mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
		builder->createStore(totalLen, dstLenPtr);

		// Set _capacity
		mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
		builder->createStore(totalLen, dstCapPtr);

		// Set _iterPos to 0
		mir::MIRValue *dstIterPosPtr = builder->createStructGEP(managedStringType, newManaged, 3, "dst._iterPos");
		builder->createStore(builder->getInt32(0), dstIterPosPtr);

		// Return the pointer to the new __ManagedStringData (opaque _ManagedString)
		return newManaged;
	}

	// __string_to_lower(managed) - convert to lowercase
	if (name == "__string_to_lower") {
		if (callExpr->args.size() != 1) {
			throw std::runtime_error("__string_to_lower requires exactly 1 argument");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());

		// Get length
		mir::MIRValue *lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
		mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
		mir::MIRValue *len64 = builder->createSExt(len, mir::MIRType::getInt64(), "len64");

		// Allocate new buffer
		mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
		mir::MIRValue *newBuffer = builder->createCall(mallocFunc, {len64}, "lower.buffer");

		// Get source buffer
		mir::MIRValue *srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
		mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
		mir::MIRValue *srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");

		// Loop to convert each byte
		mir::MIRBasicBlock *loopEntry = builder->createBasicBlock("lower.loop.entry");
		mir::MIRBasicBlock *loopBody = builder->createBasicBlock("lower.loop.body");
		mir::MIRBasicBlock *loopExit = builder->createBasicBlock("lower.loop.exit");

		mir::MIRValue *idxAlloca = builder->createAlloca(mir::MIRType::getInt32(), "idx");
		builder->createStore(builder->getInt32(0), idxAlloca);
		builder->createBr(loopEntry);

		builder->setInsertPoint(loopEntry);
		mir::MIRValue *idx = builder->createLoad(mir::MIRType::getInt32(), idxAlloca, "idx");
		mir::MIRValue *cond = builder->createICmpSLT(idx, len, "cond");
		builder->createCondBr(cond, loopBody, loopExit);

		builder->setInsertPoint(loopBody);
		mir::MIRValue *idx64 = builder->createSExt(idx, mir::MIRType::getInt64(), "idx64");
		mir::MIRValue *srcBytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, idx64, "src.byte.ptr");
		mir::MIRValue *byte = builder->createLoad(mir::MIRType::getInt8(), srcBytePtr, "byte");

		// Check if byte is uppercase (65-90 = 'A'-'Z') and convert to lowercase
		// Use control flow with PHI node instead of select
		mir::MIRValue *byte32 = builder->createZExt(byte, mir::MIRType::getInt32(), "byte32");
		mir::MIRValue *isUpper1 = builder->createICmpSGE(byte32, builder->getInt32(65), "isUpper1");
		mir::MIRValue *isUpper2 = builder->createICmpSLE(byte32, builder->getInt32(90), "isUpper2");
		mir::MIRValue *isUpper = builder->createAnd(isUpper1, isUpper2, "isUpper");

		mir::MIRBasicBlock *convertBlock = builder->createBasicBlock("lower.convert");
		mir::MIRBasicBlock *storeBlock = builder->createBasicBlock("lower.store");
		builder->createCondBr(isUpper, convertBlock, storeBlock);

		// Convert block: add 32 to uppercase letter
		builder->setInsertPoint(convertBlock);
		mir::MIRValue *lowered32 = builder->createAdd(byte32, builder->getInt32(32), "lowered32");
		builder->createBr(storeBlock);

		// Store block: use PHI to select between original and converted
		builder->setInsertPoint(storeBlock);
		mir::MIRValue *result32 = builder->createPhi(mir::MIRType::getInt32(), "result32");
		builder->addPhiIncoming(result32, lowered32, convertBlock);
		builder->addPhiIncoming(result32, byte32, loopBody);
		mir::MIRValue *result8 = builder->createTrunc(result32, mir::MIRType::getInt8(), "result8");

		mir::MIRValue *dstBytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), newBuffer, idx64, "dst.byte.ptr");
		builder->createStore(result8, dstBytePtr);

		// Increment index
		mir::MIRValue *nextIdx = builder->createAdd(idx, builder->getInt32(1), "next.idx");
		builder->createStore(nextIdx, idxAlloca);
		builder->createBr(loopEntry);

		builder->setInsertPoint(loopExit);

		// Heap-allocate the __ManagedStringData struct (32 bytes for alignment)
		mir::MIRValue *managedSize = builder->getInt64(32);
		mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "lower.managed");

		// Set buffer fields: _buffer.ptr
		mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
		mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
		builder->createStore(newBuffer, dstPtrPtr);
		// Set _buffer.len
		mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
		builder->createStore(len, dstBufLenPtr);

		// Set _len
		mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
		builder->createStore(len, dstLenPtr);

		// Set _capacity
		mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
		builder->createStore(len, dstCapPtr);

		// Set _iterPos to 0
		mir::MIRValue *dstIterPosPtr = builder->createStructGEP(managedStringType, newManaged, 3, "dst._iterPos");
		builder->createStore(builder->getInt32(0), dstIterPosPtr);

		// Return the pointer to the new __ManagedStringData (opaque _ManagedString)
		return newManaged;
	}

	// __string_to_upper(managed) - convert to uppercase
	if (name == "__string_to_upper") {
		if (callExpr->args.size() != 1) {
			throw std::runtime_error("__string_to_upper requires exactly 1 argument");
		}
		mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].get());

		// Get length
		mir::MIRValue *lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
		mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
		mir::MIRValue *len64 = builder->createSExt(len, mir::MIRType::getInt64(), "len64");

		// Allocate new buffer
		mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
		mir::MIRValue *newBuffer = builder->createCall(mallocFunc, {len64}, "upper.buffer");

		// Get source buffer
		mir::MIRValue *srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
		mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
		mir::MIRValue *srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");

		// Loop to convert each byte
		mir::MIRBasicBlock *loopEntry = builder->createBasicBlock("upper.loop.entry");
		mir::MIRBasicBlock *loopBody = builder->createBasicBlock("upper.loop.body");
		mir::MIRBasicBlock *loopExit = builder->createBasicBlock("upper.loop.exit");

		mir::MIRValue *idxAlloca = builder->createAlloca(mir::MIRType::getInt32(), "idx");
		builder->createStore(builder->getInt32(0), idxAlloca);
		builder->createBr(loopEntry);

		builder->setInsertPoint(loopEntry);
		mir::MIRValue *idx = builder->createLoad(mir::MIRType::getInt32(), idxAlloca, "idx");
		mir::MIRValue *cond = builder->createICmpSLT(idx, len, "cond");
		builder->createCondBr(cond, loopBody, loopExit);

		builder->setInsertPoint(loopBody);
		mir::MIRValue *idx64 = builder->createSExt(idx, mir::MIRType::getInt64(), "idx64");
		mir::MIRValue *srcBytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, idx64, "src.byte.ptr");
		mir::MIRValue *byte = builder->createLoad(mir::MIRType::getInt8(), srcBytePtr, "byte");

		// Check if byte is lowercase (97-122 = 'a'-'z') and convert to uppercase
		// Use control flow with PHI node instead of select
		mir::MIRValue *byte32 = builder->createZExt(byte, mir::MIRType::getInt32(), "byte32");
		mir::MIRValue *isLower1 = builder->createICmpSGE(byte32, builder->getInt32(97), "isLower1");
		mir::MIRValue *isLower2 = builder->createICmpSLE(byte32, builder->getInt32(122), "isLower2");
		mir::MIRValue *isLower = builder->createAnd(isLower1, isLower2, "isLower");

		mir::MIRBasicBlock *convertBlock = builder->createBasicBlock("upper.convert");
		mir::MIRBasicBlock *storeBlock = builder->createBasicBlock("upper.store");
		builder->createCondBr(isLower, convertBlock, storeBlock);

		// Convert block: subtract 32 from lowercase letter
		builder->setInsertPoint(convertBlock);
		mir::MIRValue *uppered32 = builder->createSub(byte32, builder->getInt32(32), "uppered32");
		builder->createBr(storeBlock);

		// Store block: use PHI to select between original and converted
		builder->setInsertPoint(storeBlock);
		mir::MIRValue *result32 = builder->createPhi(mir::MIRType::getInt32(), "result32");
		builder->addPhiIncoming(result32, uppered32, convertBlock);
		builder->addPhiIncoming(result32, byte32, loopBody);
		mir::MIRValue *result8 = builder->createTrunc(result32, mir::MIRType::getInt8(), "result8");

		mir::MIRValue *dstBytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), newBuffer, idx64, "dst.byte.ptr");
		builder->createStore(result8, dstBytePtr);

		// Increment index
		mir::MIRValue *nextIdx = builder->createAdd(idx, builder->getInt32(1), "next.idx");
		builder->createStore(nextIdx, idxAlloca);
		builder->createBr(loopEntry);

		builder->setInsertPoint(loopExit);

		// Heap-allocate the __ManagedStringData struct (32 bytes for alignment)
		mir::MIRValue *managedSize = builder->getInt64(32);
		mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "upper.managed");

		// Set buffer fields: _buffer.ptr
		mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
		mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
		builder->createStore(newBuffer, dstPtrPtr);
		// Set _buffer.len
		mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
		builder->createStore(len, dstBufLenPtr);

		// Set _len
		mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
		builder->createStore(len, dstLenPtr);

		// Set _capacity
		mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
		builder->createStore(len, dstCapPtr);

		// Set _iterPos to 0
		mir::MIRValue *dstIterPosPtr = builder->createStructGEP(managedStringType, newManaged, 3, "dst._iterPos");
		builder->createStore(builder->getInt32(0), dstIterPosPtr);

		// Return the pointer to the new __ManagedStringData (opaque _ManagedString)
		return newManaged;
	}

	throw std::runtime_error("Unknown string intrinsic: " + name);
}
