/**
 * MIR Code Generator - Variable and Let Declaration Statements
 *
 * This file implements code generation for var and let declarations.
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <algorithm>
#include <stdexcept>

void MIRCodeGenerator::generateVarDecl(VarDeclStmtAST *varDecl, mir::MIRFunction *function) {
	// Handle array literal initialization
	if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(varDecl->initializer.get())) {
		mir::MIRType *elementType;
		std::string elementTypeName;
		std::vector<mir::MIRValue *> initValues;
		mir::MIRValue *runtimeSize = nullptr; // For variable-sized arrays
		int constantArraySize = 0;

		if (arrayLiteral->hasVariableSize()) {
			// [expr]type form - variable-sized array (runtime size)
			runtimeSize = generateExpr(arrayLiteral->sizeExpr.get());
			elementTypeName = arrayLiteral->elementType;
			elementType = getTypeFromString(elementTypeName);
		} else if (arrayLiteral->size > 0) {
			// [size]type form - constant-size zero-initialized
			constantArraySize = arrayLiteral->size;
			elementTypeName = arrayLiteral->elementType;
			elementType = getTypeFromString(elementTypeName);
		} else {
			// [val1, val2, ...] form - value-initialized
			constantArraySize = static_cast<int>(arrayLiteral->values.size());

			for (auto &valExpr : arrayLiteral->values) {
				mir::MIRValue *val = generateExpr(valExpr.get());
				if (!val) {
					reportError("Failed to generate array element value",
								varDecl->line, varDecl->column);
				}
				initValues.push_back(val);
			}

			// Infer element type from first value
			elementType = initValues[0]->type;
			if (elementType->kind == mir::MIRTypeKind::Int32) {
				elementTypeName = "int";
			} else if (elementType->kind == mir::MIRTypeKind::Float64) {
				elementTypeName = "float";
			} else if (elementType->kind == mir::MIRTypeKind::Int8) {
				elementTypeName = "character";
			} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
				elementTypeName = "ptr";
			} else {
				reportError("Unsupported array element type",
							varDecl->line, varDecl->column);
			}
		}

		uint64_t elementSize = elementType->sizeInBytes;
		mir::MIRValue *arrayPtr;

		if (runtimeSize) {
			// Variable-sized array: always heap-allocate with header
			// Layout: [length:i32][capacity:i32][...data...]
			//              -8          -4           0+
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// Calculate total size: 8 (header) + size * elementSize
			mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
			mir::MIRValue *size64 = builder->createSExt(runtimeSize, mir::MIRType::getInt64(), "size.ext");
			mir::MIRValue *dataSize = builder->createMul(size64, elemSizeVal, "data.size");
			mir::MIRValue *headerSize = builder->getInt64(8);
			mir::MIRValue *totalSize = builder->createAdd(dataSize, headerSize, "total.size");

			// Allocate memory
			mir::MIRValue *basePtr = builder->createCall(mallocFunc, {totalSize}, varDecl->name + ".base");

			// Store length at offset 0 (basePtr already points here)
			builder->createStore(runtimeSize, basePtr);

			// Store capacity at offset 4
			mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
													   {builder->getInt64(4)}, "cap.ptr");
			builder->createStore(runtimeSize, capPtr); // capacity = length initially

			// Data pointer is at offset 8
			arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
										  {builder->getInt64(8)}, varDecl->name + ".data");

			// Create alloca to store the data pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);
			namedValues[varDecl->name] = ptrAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeManagedArrayType(elementTypeName);

			// Track base pointer for cleanup (we need to free basePtr, not dataPtr)
			if (!scopeStack.empty()) {
				// Store base pointer in a hidden alloca for cleanup
				mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__base");
				builder->createStore(basePtr, basePtrAlloca);
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, basePtrAlloca});
			}

			// Zero-initialize data using memset
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			builder->createCall(memsetFunc, {arrayPtr, zeroVal, dataSize});

		} else {
			// Constant-sized array
			uint64_t totalSize = constantArraySize * elementSize;

			// Threshold for stack allocation (4KB) - larger arrays go on heap
			constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
			bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

			if (useHeap) {
				// Large array: heap-allocate with header
				// Layout: [length:i32][capacity:i32][...data...]
				initHeapManagement();
				mir::MIRFunction *mallocFunc = module->getFunction("malloc");
				mir::MIRValue *sizeVal = builder->getInt64(8 + totalSize);
				mir::MIRValue *basePtr = builder->createCall(mallocFunc, {sizeVal}, varDecl->name + ".base");

				// Store length at offset 0 (basePtr already points here)
				mir::MIRValue *lengthVal = builder->getInt32(constantArraySize);
				builder->createStore(lengthVal, basePtr);

				// Store capacity at offset 4
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
														   {builder->getInt64(4)}, "cap.ptr");
				builder->createStore(lengthVal, capPtr);

				// Data pointer is at offset 8
				arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
											  {builder->getInt64(8)}, varDecl->name + ".data");

				// Create alloca to store the data pointer
				mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name);
				builder->createStore(arrayPtr, ptrAlloca);
				namedValues[varDecl->name] = ptrAlloca;
				variableTypes[varDecl->name] = maxon::TypeConversion::makeManagedArrayType(elementTypeName);

				// Track base pointer for cleanup
				if (!scopeStack.empty()) {
					mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__base");
					builder->createStore(basePtr, basePtrAlloca);
					scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, basePtrAlloca});
				}
			} else {
				// Small array: stack-allocate directly (much faster)
				mir::MIRType *arrayType = mir::MIRType::getArray(elementType, constantArraySize);
				arrayPtr = builder->createAlloca(arrayType, varDecl->name);
				namedValues[varDecl->name] = arrayPtr;
				stackAllocatedArrays.insert(varDecl->name);
				variableTypes[varDecl->name] = "[" + std::to_string(constantArraySize) + "]" + elementTypeName;

				// For stack arrays, still store length in hidden alloca for .length access
				mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), varDecl->name + ".__length");
				mir::MIRValue *arraySizeVal = builder->getInt32(constantArraySize);
				builder->createStore(arraySizeVal, lengthAlloca);
				namedValues[varDecl->name + ".__length"] = lengthAlloca;
			}

			// Initialize array elements
			if (initValues.empty()) {
				// Zero-initialize using memset
				mir::MIRFunction *memsetFunc = module->getFunction("memset");
				if (!memsetFunc) {
					initHeapManagement();
					memsetFunc = module->getFunction("memset");
				}
				mir::MIRValue *zeroVal = builder->getInt32(0);
				mir::MIRValue *memsetSizeVal = builder->getInt64(totalSize);
				builder->createCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
			} else {
				// Store each value
				for (int i = 0; i < constantArraySize; i++) {
					mir::MIRValue *indexVal = builder->getInt32(i);
					mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
					builder->createStore(initValues[i], elementPtr);
				}
			}
		}

		return;
	}

	// Handle map literal initialization: map from K to V
	if (auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(varDecl->initializer.get())) {
		const std::string &keyType = mapLiteral->keyType;
		const std::string &valueType = mapLiteral->valueType;

		// Build the specialized type name
		std::string specializedName = "map<" + keyType + "," + valueType + ">";

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			// Check if the map template struct exists
			if (structDefinitions.find("map") != structDefinitions.end()) {
				std::map<std::string, std::string> typeBindings = {
					{"KeyType", keyType},
					{"ValueType", valueType}};
				instantiateGenericStruct("map", typeBindings);
			} else {
				reportError("Generic struct 'map' not found - ensure stdlib/collections/map.maxon is compiled",
							varDecl->line, varDecl->column);
			}
		}

		// Get the specialized struct type
		mir::MIRType *mapStructType = structTypes[specializedName];
		if (!mapStructType) {
			reportError("Failed to instantiate map type: " + specializedName,
						varDecl->line, varDecl->column);
		}

		// Map is implemented as a map struct with arrays for keys, values, and states
		// Initial capacity is 16 buckets
		const int initialCapacity = 16;

		// Get MIR types for key and value
		mir::MIRType *keyMirType = getTypeFromString(keyType);
		mir::MIRType *valueMirType = getTypeFromString(valueType);

		// Mark struct type as used
		mapStructType->used = true;

		// Allocate the map struct on the stack
		mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, varDecl->name);
		namedValues[varDecl->name] = mapAlloca;
		variableTypes[varDecl->name] = specializedName;

		// Initialize the map with default capacity
		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");

		// Allocate keys array with header: [length:i32][capacity:i32][...data...]
		uint64_t keySize = keyMirType->sizeInBytes;
		mir::MIRValue *keysDataSize = builder->getInt64(initialCapacity * keySize + 8);
		mir::MIRValue *keysBase = builder->createCall(mallocFunc, {keysDataSize}, varDecl->name + ".keys.base");

		// Store length and capacity in keys header
		mir::MIRValue *keysLenPtr = keysBase;
		builder->createStore(builder->getInt32(initialCapacity), keysLenPtr);
		mir::MIRValue *keysCapPtr = builder->createGEP(mir::MIRType::getInt8(), keysBase, {builder->getInt64(4)}, "keys.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), keysCapPtr);

		// Data pointer for keys
		mir::MIRValue *keysData = builder->createGEP(mir::MIRType::getInt8(), keysBase, {builder->getInt64(8)}, varDecl->name + ".keys.data");

		// Zero-initialize keys
		mir::MIRFunction *memsetFunc = module->getFunction("memset");
		builder->createCall(memsetFunc, {keysData, builder->getInt32(0), builder->getInt64(initialCapacity * keySize)});

		// Allocate values array with header
		uint64_t valueSize = valueMirType->sizeInBytes;
		mir::MIRValue *valuesDataSize = builder->getInt64(initialCapacity * valueSize + 8);
		mir::MIRValue *valuesBase = builder->createCall(mallocFunc, {valuesDataSize}, varDecl->name + ".values.base");

		// Store length and capacity in values header
		builder->createStore(builder->getInt32(initialCapacity), valuesBase);
		mir::MIRValue *valuesCapPtr = builder->createGEP(mir::MIRType::getInt8(), valuesBase, {builder->getInt64(4)}, "values.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), valuesCapPtr);

		// Data pointer for values
		mir::MIRValue *valuesData = builder->createGEP(mir::MIRType::getInt8(), valuesBase, {builder->getInt64(8)}, varDecl->name + ".values.data");

		// Zero-initialize values
		builder->createCall(memsetFunc, {valuesData, builder->getInt32(0), builder->getInt64(initialCapacity * valueSize)});

		// Allocate states array with header (byte array)
		mir::MIRValue *statesDataSize = builder->getInt64(initialCapacity + 8);
		mir::MIRValue *statesBase = builder->createCall(mallocFunc, {statesDataSize}, varDecl->name + ".states.base");

		// Store length and capacity in states header
		builder->createStore(builder->getInt32(initialCapacity), statesBase);
		mir::MIRValue *statesCapPtr = builder->createGEP(mir::MIRType::getInt8(), statesBase, {builder->getInt64(4)}, "states.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), statesCapPtr);

		// Data pointer for states
		mir::MIRValue *statesData = builder->createGEP(mir::MIRType::getInt8(), statesBase, {builder->getInt64(8)}, varDecl->name + ".states.data");

		// Zero-initialize states (all EMPTY = 0)
		builder->createCall(memsetFunc, {statesData, builder->getInt32(0), builder->getInt64(initialCapacity)});

		// Store pointers in the HashMap struct
		mir::MIRValue *keysFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "_keys");
		builder->createStore(keysData, keysFieldPtr);

		mir::MIRValue *valuesFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 1, "_values");
		builder->createStore(valuesData, valuesFieldPtr);

		mir::MIRValue *statesFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "_states");
		builder->createStore(statesData, statesFieldPtr);

		mir::MIRValue *countFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 3, "_count");
		builder->createStore(builder->getInt32(0), countFieldPtr);

		mir::MIRValue *capacityFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "_capacity");
		builder->createStore(builder->getInt32(initialCapacity), capacityFieldPtr);

		// Track base pointers for cleanup
		if (!scopeStack.empty()) {
			mir::MIRValue *keysBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__keys_base");
			builder->createStore(keysBase, keysBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._keys", keysBaseAlloca});

			mir::MIRValue *valuesBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__values_base");
			builder->createStore(valuesBase, valuesBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._values", valuesBaseAlloca});

			mir::MIRValue *statesBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__states_base");
			builder->createStore(statesBase, statesBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._states", statesBaseAlloca});
		}

		return;
	}

	// Handle struct initialization
	if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(varDecl->initializer.get())) {
		mir::MIRType *structType = structTypes[structInitExpr->structName];
		if (!structType) {
			reportError("Unknown struct type: " + structInitExpr->structName,
						structInitExpr->line, structInitExpr->column);
		}
		// Mark struct type as used for lazy type emission
		structType->used = true;
		markFieldTypesUsed(structType);

		mir::MIRValue *structAlloca = builder->createAlloca(structType, varDecl->name);
		namedValues[varDecl->name] = structAlloca;
		variableTypes[varDecl->name] = structInitExpr->structName;

		// Initialize fields - iterate over all struct fields and use provided value or default
		const auto &fields = structFields[structInitExpr->structName];
		const auto &defaults = structFieldDefaults[structInitExpr->structName];

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
				// Array literal for struct field - initialize in place
				if (!arrayLit->values.empty()) {
					// Value-initialized array [1, 2, 3]
					for (size_t i = 0; i < arrayLit->values.size(); i++) {
						mir::MIRValue *elemValue = generateExpr(arrayLit->values[i].get());
						mir::MIRValue *elemPtr = builder->createArrayGEP(elemValue->type, fieldPtr,
																		 mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), i),
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
						{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
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

							// Load length from header at dataPtr - 8
							mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
																		  {builder->getInt64(-8)}, "length.ptr");
							mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");

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

		return;
	}

	// Handle string literal initialization
	// generateStringLiteral creates an alloca, initializes it, and returns the pointer
	// We use that alloca directly (don't create a second one)
	if (auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(varDecl->initializer.get())) {
		mir::MIRValue *stringAlloca = generateStringLiteral(strLiteral);

		// Use the alloca from generateStringLiteral directly
		// Rename it to match the variable name
		stringAlloca->name = varDecl->name;

		namedValues[varDecl->name] = stringAlloca;
		variableTypes[varDecl->name] = "string";
		return;
	}

	// Handle char literal initialization
	// generateCharLiteral creates an alloca, initializes it, and returns the pointer
	// We use that alloca directly (don't create a second one)
	if (auto *charLiteral = dynamic_cast<CharacterExprAST *>(varDecl->initializer.get())) {
		mir::MIRValue *charAlloca = generateCharLiteral(charLiteral);

		// Use the alloca from generateCharLiteral directly
		// Rename it to match the variable name
		charAlloca->name = varDecl->name;

		namedValues[varDecl->name] = charAlloca;
		variableTypes[varDecl->name] = "character";
		return;
	}

	// Handle string concatenation (string + string produces a string struct)
	// The expression returns an alloca pointer - use it directly like string literals
	// This also handles chained concatenation like a + b + c
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(varDecl->initializer.get())) {
		if (isStringConcatExpr(binExpr)) {
			mir::MIRValue *stringAlloca = generateExpr(varDecl->initializer.get());

			// Use the alloca from concat directly
			stringAlloca->name = varDecl->name;

			namedValues[varDecl->name] = stringAlloca;
			variableTypes[varDecl->name] = "string";
			return;
		}
	}

	// Non-array, non-struct variable
	mir::MIRValue *initVal = nullptr;
	if (varDecl->initializer) {
		initVal = generateExpr(varDecl->initializer.get());
		if (!initVal) {
			reportError("Failed to generate variable initializer for '" + varDecl->name + "'",
						varDecl->line, varDecl->column);
		}
	}

	// Determine type
	mir::MIRType *allocaType;
	if (!varDecl->type.empty()) {
		allocaType = getTypeFromString(varDecl->type);
	} else if (initVal) {
		allocaType = initVal->type;
	} else {
		allocaType = mir::MIRType::getInt32();
	}

	mir::MIRValue *alloca = builder->createAlloca(allocaType, varDecl->name);

	if (initVal) {
		builder->createStore(initVal, alloca);
	} else {
		// Zero-initialize
		mir::MIRValue *zeroVal;
		if (allocaType->isInteger()) {
			zeroVal = builder->getInt32(0);
		} else if (allocaType->isFloat()) {
			zeroVal = builder->getFloat64(0.0);
		} else {
			zeroVal = builder->getNull();
		}
		builder->createStore(zeroVal, alloca);
	}

	namedValues[varDecl->name] = alloca;
	// If type was explicitly specified, use it; otherwise derive from the alloca type
	if (!varDecl->type.empty()) {
		variableTypes[varDecl->name] = varDecl->type;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		variableTypes[varDecl->name] = allocaType->structName;
	} else {
		// Derive type from the allocated MIR type
		variableTypes[varDecl->name] = getMaxonTypeFromMIRType(allocaType);
	}

	// Track substring variables for cleanup at scope exit
	// Substrings may hold a reference to a heap-allocated parent's buffer
	std::string finalType = variableTypes[varDecl->name];
	if (finalType == "substring" && !scopeStack.empty()) {
		scopeStack.back().substringAllocas.push_back({varDecl->name, alloca});
	}
	// Track cstring variables for cleanup at scope exit
	// Cstrings hold a reference to the underlying managed string
	if (finalType == "cstring" && !scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({varDecl->name, alloca});
	}
}

void MIRCodeGenerator::generateLetDecl(LetDeclStmtAST *letDecl, mir::MIRFunction *function) {
	// Handle array literal initialization - static or variable-sized arrays
	if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(letDecl->initializer.get())) {
		mir::MIRType *elementType;
		std::string elementTypeName;
		std::vector<mir::MIRValue *> initValues;
		mir::MIRValue *runtimeSize = nullptr;
		int constantArraySize = 0;

		if (arrayLiteral->hasVariableSize()) {
			// [expr]type form - variable-sized immutable array
			runtimeSize = generateExpr(arrayLiteral->sizeExpr.get());
			elementTypeName = arrayLiteral->elementType;
			elementType = getTypeFromString(elementTypeName);
		} else {
			// Value literal form [val1, val2, ...]
			constantArraySize = static_cast<int>(arrayLiteral->values.size());

			for (auto &valExpr : arrayLiteral->values) {
				mir::MIRValue *val = generateExpr(valExpr.get());
				if (!val) {
					reportError("Failed to generate array element value",
								letDecl->line, letDecl->column);
				}
				initValues.push_back(val);
			}

			// Infer element type from first value
			elementType = initValues[0]->type;
			if (elementType->kind == mir::MIRTypeKind::Int32) {
				elementTypeName = "int";
			} else if (elementType->kind == mir::MIRTypeKind::Float64) {
				elementTypeName = "float";
			} else if (elementType->kind == mir::MIRTypeKind::Int8) {
				elementTypeName = "character";
			} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
				elementTypeName = "ptr";
			} else {
				reportError("Unsupported array element type",
							letDecl->line, letDecl->column);
			}
		}

		uint64_t elementSize = elementType->sizeInBytes;
		mir::MIRValue *arrayPtr;

		if (runtimeSize) {
			// Variable-sized array: always heap-allocate with header
			// Layout: [length:i32][capacity:i32][...data...]
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// Calculate total size: 8 (header) + size * elementSize
			mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
			mir::MIRValue *size64 = builder->createSExt(runtimeSize, mir::MIRType::getInt64(), "size.ext");
			mir::MIRValue *dataSize = builder->createMul(size64, elemSizeVal, "data.size");
			mir::MIRValue *headerSize = builder->getInt64(8);
			mir::MIRValue *totalSize = builder->createAdd(dataSize, headerSize, "total.size");

			// Allocate memory
			mir::MIRValue *basePtr = builder->createCall(mallocFunc, {totalSize}, letDecl->name + ".base");

			// Store length at offset 0 (basePtr already points here)
			builder->createStore(runtimeSize, basePtr);

			// Store capacity at offset 4
			mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
													   {builder->getInt64(4)}, "cap.ptr");
			builder->createStore(runtimeSize, capPtr);

			// Data pointer is at offset 8
			arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
										  {builder->getInt64(8)}, letDecl->name + ".data");

			// Create alloca to store the data pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);
			namedValues[letDecl->name] = ptrAlloca;
			variableTypes[letDecl->name] = maxon::TypeConversion::makeManagedArrayType(elementTypeName);

			// Track base pointer for cleanup
			if (!scopeStack.empty()) {
				mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name + ".__base");
				builder->createStore(basePtr, basePtrAlloca);
				scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, basePtrAlloca});
			}

			// Zero-initialize data using memset
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			builder->createCall(memsetFunc, {arrayPtr, zeroVal, dataSize});

		} else {
			// Constant-sized array (value literals)
			uint64_t totalSize = constantArraySize * elementSize;

			// Threshold for stack allocation (4KB)
			constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
			bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

			if (useHeap) {
				// Large array: heap-allocate with header
				initHeapManagement();
				mir::MIRFunction *mallocFunc = module->getFunction("malloc");
				mir::MIRValue *sizeVal = builder->getInt64(8 + totalSize);
				mir::MIRValue *basePtr = builder->createCall(mallocFunc, {sizeVal}, letDecl->name + ".base");

				// Store length at offset 0 (basePtr already points here)
				mir::MIRValue *lengthVal = builder->getInt32(constantArraySize);
				builder->createStore(lengthVal, basePtr);

				// Store capacity at offset 4
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
														   {builder->getInt64(4)}, "cap.ptr");
				builder->createStore(lengthVal, capPtr);

				// Data pointer is at offset 8
				arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
											  {builder->getInt64(8)}, letDecl->name + ".data");

				// Create alloca to store the data pointer
				mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
				builder->createStore(arrayPtr, ptrAlloca);
				namedValues[letDecl->name] = ptrAlloca;

				// Track base pointer for cleanup
				if (!scopeStack.empty()) {
					mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name + ".__base");
					builder->createStore(basePtr, basePtrAlloca);
					scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, basePtrAlloca});
				}
			} else {
				// Small array: stack-allocate directly
				mir::MIRType *arrayType = mir::MIRType::getArray(elementType, constantArraySize);
				arrayPtr = builder->createAlloca(arrayType, letDecl->name);
				namedValues[letDecl->name] = arrayPtr;
				stackAllocatedArrays.insert(letDecl->name);

				// Store length in hidden alloca for .length access
				mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), letDecl->name + ".__length");
				mir::MIRValue *arraySizeVal = builder->getInt32(constantArraySize);
				builder->createStore(arraySizeVal, sizeAlloca);
				namedValues[letDecl->name + ".__length"] = sizeAlloca;
			}

			// Initialize array elements
			for (int i = 0; i < constantArraySize; i++) {
				mir::MIRValue *indexVal = builder->getInt32(i);
				mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
				builder->createStore(initValues[i], elementPtr);
			}

			variableTypes[letDecl->name] = "[" + std::to_string(constantArraySize) + "]" + elementTypeName;
		}

		return;
	}

	// Handle string literal initialization
	if (auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(letDecl->initializer.get())) {
		mir::MIRValue *stringPtr = generateStringLiteral(strLiteral);
		namedValues[letDecl->name] = stringPtr;
		variableTypes[letDecl->name] = "string";
		return;
	}

	// Handle string concatenation (string + string produces a string struct)
	// The expression returns an alloca pointer - use it directly like string literals
	// This also handles chained concatenation like a + b + c
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(letDecl->initializer.get())) {
		if (isStringConcatExpr(binExpr)) {
			mir::MIRValue *stringAlloca = generateExpr(letDecl->initializer.get());

			// Use the alloca from concat directly
			stringAlloca->name = letDecl->name;

			namedValues[letDecl->name] = stringAlloca;
			variableTypes[letDecl->name] = "string";
			return;
		}
	}

	// Non-array variable
	mir::MIRValue *initVal = nullptr;
	if (letDecl->initializer) {
		initVal = generateExpr(letDecl->initializer.get());
		if (!initVal) {
			reportError("Failed to generate let initializer",
						letDecl->line, letDecl->column);
		}
	}

	mir::MIRType *allocaType;
	if (!letDecl->type.empty()) {
		allocaType = getTypeFromString(letDecl->type);
	} else if (initVal) {
		allocaType = initVal->type;
	} else {
		allocaType = mir::MIRType::getInt32();
	}

	mir::MIRValue *alloca = builder->createAlloca(allocaType, letDecl->name);

	if (initVal) {
		builder->createStore(initVal, alloca);
	} else {
		mir::MIRValue *zeroVal;
		if (allocaType->isInteger()) {
			zeroVal = builder->getInt32(0);
		} else if (allocaType->isFloat()) {
			zeroVal = builder->getFloat64(0.0);
		} else {
			zeroVal = builder->getNull();
		}
		builder->createStore(zeroVal, alloca);
	}

	namedValues[letDecl->name] = alloca;
	// If type was explicitly specified, use it; otherwise derive from the alloca type
	if (!letDecl->type.empty()) {
		variableTypes[letDecl->name] = letDecl->type;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		variableTypes[letDecl->name] = allocaType->structName;
	} else {
		// Derive type from the allocated MIR type
		variableTypes[letDecl->name] = getMaxonTypeFromMIRType(allocaType);
	}

	// Track substring variables for cleanup at scope exit
	// Substrings may hold a reference to a heap-allocated parent's buffer
	std::string finalType = variableTypes[letDecl->name];
	if (finalType == "substring" && !scopeStack.empty()) {
		scopeStack.back().substringAllocas.push_back({letDecl->name, alloca});
	}
	// Track cstring variables for cleanup at scope exit
	// Cstrings hold a reference to the underlying managed string
	if (finalType == "cstring" && !scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({letDecl->name, alloca});
	}
}
