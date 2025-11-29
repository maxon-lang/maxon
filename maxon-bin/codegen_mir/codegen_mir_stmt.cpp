/**
 * MIR Code Generator - Statement Generation
 *
 * This file implements statement code generation for MIR.
 */

#include "../codegen_mir.h"
#include <stdexcept>

void MIRCodeGenerator::generateStmt(StmtAST *stmt, mir::MIRFunction *function) {
	if (auto *varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
		// Handle array literal initialization
		if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(varDecl->initializer.get())) {
			int arraySize;
			mir::MIRType *elementType;
			std::string elementTypeName;
			std::vector<mir::MIRValue *> initValues;

			if (arrayLiteral->size > 0) {
				// [size]type form - zero-initialized
				arraySize = arrayLiteral->size;
				elementTypeName = arrayLiteral->elementType;
				elementType = getTypeFromString(elementTypeName);
			} else {
				// [val1, val2, ...] form - value-initialized
				arraySize = static_cast<int>(arrayLiteral->values.size());

				for (auto &valExpr : arrayLiteral->values) {
					mir::MIRValue *val = generateExpr(valExpr.get());
					if (!val) {
						throw std::runtime_error("Failed to generate array element value");
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
					elementTypeName = "char";
				} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
					elementTypeName = "ptr";
				} else {
					throw std::runtime_error("Unsupported array element type");
				}
			}

			// Calculate size
			uint64_t elementSize = elementType->sizeInBytes;
			uint64_t totalSize = arraySize * elementSize;

			// Threshold for stack allocation (4KB) - larger arrays go on heap
			constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
			bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

			mir::MIRValue *arrayPtr;

			if (useHeap) {
				// Large array: heap-allocate
				initHeapManagement();
				mir::MIRFunction *mallocFunc = module->getFunction("malloc");
				mir::MIRValue *sizeVal = builder->getInt64(totalSize);
				arrayPtr = builder->createCall(mallocFunc, {sizeVal}, varDecl->name + ".malloc");

				// Create alloca to store the pointer
				mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name);
				builder->createStore(arrayPtr, ptrAlloca);
				namedValues[varDecl->name] = ptrAlloca;
				variableTypes[varDecl->name] = "[]" + elementTypeName; // Dynamic array type

				// Track for cleanup
				if (!scopeStack.empty()) {
					scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, ptrAlloca});
				}

				// Store array capacity as hidden variable (for dynamic arrays)
				mir::MIRValue *capacityAlloca = builder->createAlloca(mir::MIRType::getInt32(), varDecl->name + ".__capacity");
				mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
				builder->createStore(arraySizeVal, capacityAlloca);
				namedValues[varDecl->name + ".__capacity"] = capacityAlloca;
			} else {
				// Small array: stack-allocate directly (much faster)
				mir::MIRType *arrayType = mir::MIRType::getArray(elementType, arraySize);
				arrayPtr = builder->createAlloca(arrayType, varDecl->name);
				namedValues[varDecl->name] = arrayPtr;
				stackAllocatedArrays.insert(varDecl->name); // Mark as stack-allocated
				variableTypes[varDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;
			}

			// Store array length as hidden variable
			mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), varDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
			builder->createStore(arraySizeVal, lengthAlloca);
			namedValues[varDecl->name + ".__length"] = lengthAlloca;

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
				for (int i = 0; i < arraySize; i++) {
					mir::MIRValue *indexVal = builder->getInt32(i);
					mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
					builder->createStore(initValues[i], elementPtr);
				}
			}

			return;
		}

		// Handle struct initialization
		if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(varDecl->initializer.get())) {
			mir::MIRType *structType = structTypes[structInitExpr->structName];
			if (!structType) {
				throw std::runtime_error("Unknown struct type: " + structInitExpr->structName);
			}

			mir::MIRValue *structAlloca = builder->createAlloca(structType, varDecl->name);
			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = structInitExpr->structName;

			// Initialize fields
			const auto &fields = structFields[structInitExpr->structName];
			for (const auto &initField : structInitExpr->fields) {
				int fieldIndex = -1;
				std::string fieldTypeStr;
				for (size_t j = 0; j < fields.size(); j++) {
					if (fields[j].first == initField.name) {
						fieldIndex = static_cast<int>(j);
						fieldTypeStr = fields[j].second;
						break;
					}
				}

				if (fieldIndex < 0) {
					throw std::runtime_error("Unknown field '" + initField.name +
											 "' in struct '" + structInitExpr->structName + "'");
				}

				mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
																   fieldIndex, initField.name);

				// Handle array field initialization
				if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(initField.value.get())) {
					// Array literal for struct field - initialize in place
					if (!arrayLit->values.empty()) {
						// Value-initialized array [1, 2, 3]
						for (size_t i = 0; i < arrayLit->values.size(); i++) {
							mir::MIRValue *elemValue = generateExpr(arrayLit->values[i].get());
							mir::MIRValue *elemPtr = builder->createArrayGEP(elemValue->type, fieldPtr,
																			 mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), i),
																			 initField.name + ".elem");
							builder->createStore(elemValue, elemPtr);
						}
					} else {
						// Zero-initialized array [16]byte - already zero from alloca
						// Nothing extra needed
					}
				} else {
					// Regular field value
					mir::MIRValue *fieldValue = generateExpr(initField.value.get());
					builder->createStore(fieldValue, fieldPtr);
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
				throw std::runtime_error("Failed to generate variable initializer for '" + varDecl->name + "'");
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
		return;
	}

	if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		// Handle array literal initialization - static arrays
		if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(letDecl->initializer.get())) {
			// Semantic analyzer ensures only value literals are used with let
			// (not [N]type form), so we always have initValues
			int arraySize = static_cast<int>(arrayLiteral->values.size());
			mir::MIRType *elementType;
			std::string elementTypeName;
			std::vector<mir::MIRValue *> initValues;

			for (auto &valExpr : arrayLiteral->values) {
				mir::MIRValue *val = generateExpr(valExpr.get());
				if (!val) {
					throw std::runtime_error("Failed to generate array element value");
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
				elementTypeName = "char";
			} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
				elementTypeName = "ptr";
			} else {
				throw std::runtime_error("Unsupported array element type");
			}

			uint64_t elementSize = elementType->sizeInBytes;
			uint64_t totalSize = arraySize * elementSize;

			// Threshold for stack allocation (4KB) - larger arrays go on heap
			constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;

			mir::MIRValue *arrayPtr;
			bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

			if (useHeap) {
				// Large array: heap-allocate (but still immutable)
				initHeapManagement();
				mir::MIRFunction *mallocFunc = module->getFunction("malloc");
				mir::MIRValue *sizeVal = builder->getInt64(totalSize);
				arrayPtr = builder->createCall(mallocFunc, {sizeVal}, letDecl->name + ".malloc");

				// Create alloca to store the pointer
				mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
				builder->createStore(arrayPtr, ptrAlloca);
				namedValues[letDecl->name] = ptrAlloca;

				// Track for cleanup
				if (!scopeStack.empty()) {
					scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, ptrAlloca});
				}
			} else {
				// Small array: stack-allocate directly
				mir::MIRType *arrayType = mir::MIRType::getArray(elementType, arraySize);
				arrayPtr = builder->createAlloca(arrayType, letDecl->name);
				namedValues[letDecl->name] = arrayPtr;
				stackAllocatedArrays.insert(letDecl->name); // Mark as stack-allocated
															// No heap cleanup needed for stack arrays
			}

			// Store array length as hidden variable (for .length access)
			mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), letDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
			builder->createStore(arraySizeVal, sizeAlloca);
			namedValues[letDecl->name + ".__length"] = sizeAlloca;

			// Initialize array elements
			for (int i = 0; i < arraySize; i++) {
				mir::MIRValue *indexVal = builder->getInt32(i);
				mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
				builder->createStore(initValues[i], elementPtr);
			}

			variableTypes[letDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;

			return;
		}

		// Handle string literal initialization
		if (auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(letDecl->initializer.get())) {
			mir::MIRValue *stringPtr = generateStringLiteral(strLiteral);
			namedValues[letDecl->name] = stringPtr;
			variableTypes[letDecl->name] = "string";
			return;
		}

		// Non-array variable
		mir::MIRValue *initVal = nullptr;
		if (letDecl->initializer) {
			initVal = generateExpr(letDecl->initializer.get());
			if (!initVal) {
				throw std::runtime_error("Failed to generate let initializer");
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
		return;
	}

	if (auto *assign = dynamic_cast<AssignStmtAST *>(stmt)) {
		mir::MIRValue *val = generateExpr(assign->value.get());
		if (!val) {
			throw std::runtime_error("Failed to generate assignment value");
		}

		mir::MIRValue *alloca = namedValues[assign->name];
		if (!alloca) {
			throw std::runtime_error("Unknown variable name: " + assign->name);
		}

		// Check if this is a struct assignment
		// For structs, val is a pointer to the source struct, so we need memcpy
		std::string varType = variableTypes[assign->name];
		mir::MIRType *structType = structTypes.count(varType) ? structTypes[varType] : nullptr;

		if (structType && val->type->kind == mir::MIRTypeKind::Ptr) {
			// Struct assignment - use memcpy to copy the struct contents
			mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");
			if (!memcpyFunc) {
				// Declare memcpy: ptr memcpy(ptr dest, ptr src, i64 count)
				memcpyFunc = getOrDeclareFunction("memcpy",
												  mir::MIRType::getPtr(),
												  {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
			}
			mir::MIRValue *sizeVal = builder->getInt64(structType->sizeInBytes);
			builder->createCall(memcpyFunc, {alloca, val, sizeVal});
		} else {
			builder->createStore(val, alloca);
		}
		return;
	}

	if (auto *arrayAssign = dynamic_cast<ArrayAssignStmtAST *>(stmt)) {
		mir::MIRValue *indexVal = generateExpr(arrayAssign->index.get());
		if (!indexVal) {
			throw std::runtime_error("Failed to generate array index");
		}

		mir::MIRValue *alloca = namedValues[arrayAssign->arrayName];
		if (!alloca) {
			throw std::runtime_error("Unknown array name: " + arrayAssign->arrayName);
		}

		// Determine element type
		std::string varType = variableTypes[arrayAssign->arrayName];
		std::string elementTypeStr = "int";
		if (varType.size() > 2 && varType[0] == '[') {
			size_t closeBracket = varType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
				elementTypeStr = varType.substr(closeBracket + 1);
			}
		}
		mir::MIRType *elementType = getTypeFromString(elementTypeStr);

		// Get the array pointer (stack vs heap allocated)
		mir::MIRValue *arrayPtr;
		if (stackAllocatedArrays.count(arrayAssign->arrayName) > 0) {
			arrayPtr = alloca;
		} else {
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		}
		mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");

		// Handle struct literal assignment
		if (auto *structInit = dynamic_cast<StructInitExprAST *>(arrayAssign->value.get())) {
			mir::MIRType *structType = structTypes[structInit->structName];
			if (!structType) {
				throw std::runtime_error("Unknown struct type: " + structInit->structName);
			}

			for (const auto &field : structInit->fields) {
				const auto &fieldList = structFields[structInit->structName];
				int fieldIndex = -1;
				for (size_t j = 0; j < fieldList.size(); j++) {
					if (fieldList[j].first == field.name) {
						fieldIndex = static_cast<int>(j);
						break;
					}
				}

				if (fieldIndex < 0) {
					throw std::runtime_error("Unknown field '" + field.name +
											 "' in struct '" + structInit->structName + "'");
				}

				mir::MIRValue *fieldPtr = builder->createStructGEP(structType, elementPtr,
																   fieldIndex, field.name);
				mir::MIRValue *fieldVal = generateExpr(field.value.get());
				builder->createStore(fieldVal, fieldPtr);
			}
		} else {
			mir::MIRValue *val = generateExpr(arrayAssign->value.get());
			if (!val) {
				throw std::runtime_error("Failed to generate array assignment value");
			}
			builder->createStore(val, elementPtr);
		}

		return;
	}

	if (auto *arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST *>(stmt)) {
		mir::MIRValue *indexVal = generateExpr(arrayMemberAssign->index.get());
		if (!indexVal) {
			throw std::runtime_error("Failed to generate array index");
		}

		mir::MIRValue *alloca = namedValues[arrayMemberAssign->arrayName];
		if (!alloca) {
			throw std::runtime_error("Unknown array name: " + arrayMemberAssign->arrayName);
		}

		// Determine element type
		std::string varType = variableTypes[arrayMemberAssign->arrayName];
		std::string elementTypeStr;
		if (varType.size() > 2 && varType[0] == '[') {
			size_t closeBracket = varType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
				elementTypeStr = varType.substr(closeBracket + 1);
			}
		}

		mir::MIRType *structType = structTypes[elementTypeStr];
		if (!structType) {
			throw std::runtime_error("Element type is not a struct: " + elementTypeStr);
		}

		// Get pointer to array element
		mir::MIRValue *arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		mir::MIRValue *elementPtr = builder->createArrayGEP(structType, arrayPtr, indexVal, "arrayidx");

		// Find field index
		const auto &fields = structFields[elementTypeStr];
		int fieldIndex = -1;
		for (size_t i = 0; i < fields.size(); i++) {
			if (fields[i].first == arrayMemberAssign->memberName) {
				fieldIndex = static_cast<int>(i);
				break;
			}
		}

		if (fieldIndex < 0) {
			throw std::runtime_error("Unknown field '" + arrayMemberAssign->memberName +
									 "' in struct '" + elementTypeStr + "'");
		}

		mir::MIRValue *fieldPtr = builder->createStructGEP(structType, elementPtr,
														   fieldIndex, arrayMemberAssign->memberName);
		mir::MIRValue *val = generateExpr(arrayMemberAssign->value.get());
		builder->createStore(val, fieldPtr);

		return;
	}

	if (auto *memberAssign = dynamic_cast<MemberAssignStmtAST *>(stmt)) {
		// Struct member assignment: obj.field = value
		mir::MIRValue *alloca = namedValues[memberAssign->objectName];
		if (!alloca) {
			throw std::runtime_error("Unknown variable: " + memberAssign->objectName);
		}

		// Get the struct type
		std::string structTypeName = variableTypes[memberAssign->objectName];
		mir::MIRType *structType = structTypes[structTypeName];
		if (!structType) {
			throw std::runtime_error("Variable '" + memberAssign->objectName + "' is not a struct type");
		}

		// Find field index
		const auto &fields = structFields[structTypeName];
		int fieldIndex = -1;
		for (size_t i = 0; i < fields.size(); i++) {
			if (fields[i].first == memberAssign->memberName) {
				fieldIndex = static_cast<int>(i);
				break;
			}
		}

		if (fieldIndex < 0) {
			throw std::runtime_error("Unknown field '" + memberAssign->memberName +
									 "' in struct '" + structTypeName + "'");
		}

		// Get pointer to the field
		mir::MIRValue *fieldPtr = builder->createStructGEP(structType, alloca,
														   fieldIndex, memberAssign->memberName);

		// Generate the value and store it
		mir::MIRValue *val = generateExpr(memberAssign->value.get());
		builder->createStore(val, fieldPtr);

		return;
	}

	if (auto *memberArrayAssign = dynamic_cast<MemberArrayAssignStmtAST *>(stmt)) {
		// Struct member array element assignment: obj.arrayField[i] = value
		mir::MIRValue *alloca = namedValues[memberArrayAssign->objectName];
		if (!alloca) {
			throw std::runtime_error("Unknown variable: " + memberArrayAssign->objectName);
		}

		// Get the struct type
		std::string structTypeName = variableTypes[memberArrayAssign->objectName];
		mir::MIRType *structType = structTypes[structTypeName];
		if (!structType) {
			throw std::runtime_error("Variable '" + memberArrayAssign->objectName + "' is not a struct type");
		}

		// Find field index and type
		const auto &fields = structFields[structTypeName];
		int fieldIndex = -1;
		std::string fieldTypeStr;
		for (size_t i = 0; i < fields.size(); i++) {
			if (fields[i].first == memberArrayAssign->memberName) {
				fieldIndex = static_cast<int>(i);
				fieldTypeStr = fields[i].second;
				break;
			}
		}

		if (fieldIndex < 0) {
			throw std::runtime_error("Unknown field '" + memberArrayAssign->memberName +
									 "' in struct '" + structTypeName + "'");
		}

		// Get pointer to the array field
		mir::MIRValue *fieldPtr = builder->createStructGEP(structType, alloca,
														   fieldIndex, memberArrayAssign->memberName);

		// Generate index
		mir::MIRValue *indexVal = generateExpr(memberArrayAssign->index.get());

		// Determine element type from field type "[16]byte" -> "byte"
		std::string elemTypeStr = "int";
		if (fieldTypeStr.size() > 2 && fieldTypeStr[0] == '[') {
			size_t closeBracket = fieldTypeStr.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < fieldTypeStr.size()) {
				elemTypeStr = fieldTypeStr.substr(closeBracket + 1);
			}
		}
		mir::MIRType *elemType = getTypeFromString(elemTypeStr);

		// Get pointer to the array element
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, fieldPtr, indexVal, "fieldarray.elem");

		// Generate value and store
		mir::MIRValue *val = generateExpr(memberArrayAssign->value.get());
		builder->createStore(val, elemPtr);

		return;
	}

	if (auto *exprStmt = dynamic_cast<ExprStmtAST *>(stmt)) {
		generateExpr(exprStmt->expression.get());
		return;
	}

	if (auto *ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
		mir::MIRValue *condVal = generateExpr(ifStmt->condition.get());
		if (!condVal) {
			throw std::runtime_error("Failed to generate if condition");
		}

		// Convert to bool if needed
		if (condVal->type->kind != mir::MIRTypeKind::Int1) {
			mir::MIRValue *zero = builder->getInt32(0);
			condVal = builder->createICmpNe(condVal, zero, "ifcond");
		}

		mir::MIRBasicBlock *thenBB = builder->createBasicBlock("then");
		mir::MIRBasicBlock *elseBB = nullptr;
		mir::MIRBasicBlock *mergeBB = builder->createBasicBlock("ifcont");

		bool hasElse = !ifStmt->elseBody.empty();

		if (hasElse) {
			elseBB = builder->createBasicBlock("else");
			builder->createCondBr(condVal, thenBB, elseBB);
		} else {
			builder->createCondBr(condVal, thenBB, mergeBB);
		}

		// Generate then block
		builder->setInsertPoint(thenBB);
		for (auto &s : ifStmt->thenBody) {
			generateStmt(s.get(), function);
		}
		// Check current insert block (may differ from thenBB if body contains control flow)
		mir::MIRBasicBlock *thenEndBB = builder->getInsertBlock();
		if (!thenEndBB->hasTerminator()) {
			builder->createBr(mergeBB);
		}

		// Generate else block
		bool elseTerminated = false;
		if (hasElse) {
			builder->setInsertPoint(elseBB);
			for (auto &s : ifStmt->elseBody) {
				generateStmt(s.get(), function);
			}
			// Check current insert block (may differ from elseBB if body contains control flow)
			mir::MIRBasicBlock *elseEndBB = builder->getInsertBlock();
			elseTerminated = elseEndBB->hasTerminator();
			if (!elseTerminated) {
				builder->createBr(mergeBB);
			}
		}

		// Set insert point to merge block
		builder->setInsertPoint(mergeBB);
		return;
	}

	if (auto *whileStmt = dynamic_cast<WhileStmtAST *>(stmt)) {
		mir::MIRBasicBlock *condBB = builder->createBasicBlock("whilecond");
		mir::MIRBasicBlock *loopBB = builder->createBasicBlock("loop");
		mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterloop");

		// Push loop context
		loopStack.push_back({whileStmt->blockId, condBB, afterBB});

		// Jump to condition block
		builder->createBr(condBB);

		// Generate condition block
		builder->setInsertPoint(condBB);
		mir::MIRValue *condVal = generateExpr(whileStmt->condition.get());
		if (!condVal) {
			throw std::runtime_error("Failed to generate while condition");
		}

		if (condVal->type->kind != mir::MIRTypeKind::Int1) {
			mir::MIRValue *zero = builder->getInt32(0);
			condVal = builder->createICmpNe(condVal, zero, "loopcond");
		}

		builder->createCondBr(condVal, loopBB, afterBB);

		// Generate loop body
		builder->setInsertPoint(loopBB);
		for (auto &s : whileStmt->body) {
			generateStmt(s.get(), function);
		}
		// Check current insert block (may differ from loopBB if body contains control flow)
		mir::MIRBasicBlock *currentBB = builder->getInsertBlock();
		if (!currentBB->hasTerminator()) {
			builder->createBr(condBB);
		}

		// Pop loop context
		loopStack.pop_back();

		// Generate after block
		builder->setInsertPoint(afterBB);
		return;
	}

	if (auto *forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		// Check if iterating over an array variable
		bool isArrayIteration = false;
		std::string arrayVarName;
		std::string elementTypeStr = "int";
		mir::MIRType *elementType = mir::MIRType::getInt32();

		if (auto *varExpr = dynamic_cast<VariableExprAST *>(forStmt->iterable.get())) {
			arrayVarName = varExpr->name;
			auto it = variableTypes.find(arrayVarName);
			if (it != variableTypes.end()) {
				const std::string &varType = it->second;
				// Check if it's an array type like "[4]int"
				if (varType.size() > 2 && varType[0] == '[') {
					isArrayIteration = true;
					size_t closeBracket = varType.find(']');
					if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
						elementTypeStr = varType.substr(closeBracket + 1);
						elementType = getTypeFromString(elementTypeStr);
					}
				}
			}
		}

		if (isArrayIteration) {
			// Array iteration: generate inline index-based loop
			// This is equivalent to: for (int i = 0; i < arr.length; i++) { x = arr[i]; ... }

			mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
			if (!arrayAlloca) {
				throw std::runtime_error("Unknown array variable: " + arrayVarName);
			}

			// Get array length
			mir::MIRValue *lengthAlloca = namedValues[arrayVarName + ".__length"];
			if (!lengthAlloca) {
				throw std::runtime_error("Array length not found for: " + arrayVarName);
			}
			mir::MIRValue *arrayLength = builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "arrlen");

			// Get array pointer (stack vs heap allocated)
			mir::MIRValue *arrayPtr;
			if (stackAllocatedArrays.count(arrayVarName) > 0) {
				arrayPtr = arrayAlloca;
			} else {
				arrayPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "arrptr");
			}

			// Create index variable (starts at 0)
			mir::MIRValue *indexAlloca = builder->createAlloca(mir::MIRType::getInt32(), "__arr_idx");
			builder->createStore(builder->getInt32(0), indexAlloca);

			mir::MIRBasicBlock *condBB = builder->createBasicBlock("forcond");
			mir::MIRBasicBlock *loopBB = builder->createBasicBlock("forloop");
			mir::MIRBasicBlock *incrementBB = builder->createBasicBlock("forincrement");
			mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterfor");

			loopStack.push_back({forStmt->blockId, incrementBB, afterBB});

			builder->createBr(condBB);

			// Condition: index < length
			builder->setInsertPoint(condBB);
			mir::MIRValue *currentIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "idx");
			mir::MIRValue *condVal = builder->createICmpSLT(currentIndex, arrayLength, "arrcond");
			builder->createCondBr(condVal, loopBB, afterBB);

			// Loop body: load current element into loop variable
			builder->setInsertPoint(loopBB);
			mir::MIRValue *currentIndexForGEP = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "idx.gep");
			mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, currentIndexForGEP, "elemptr");
			mir::MIRValue *elementVal = builder->createLoad(elementType, elementPtr, forStmt->loopVar);

			// Create loop variable and store current element
			mir::MIRValue *loopVarAlloca = builder->createAlloca(elementType, forStmt->loopVar);
			builder->createStore(elementVal, loopVarAlloca);
			namedValues[forStmt->loopVar] = loopVarAlloca;
			variableTypes[forStmt->loopVar] = elementTypeStr;

			// Generate loop body statements
			for (auto &s : forStmt->body) {
				generateStmt(s.get(), function);
			}

			if (!builder->getInsertBlock()->hasTerminator()) {
				builder->createBr(incrementBB);
			}

			// Increment: index++
			builder->setInsertPoint(incrementBB);
			mir::MIRValue *oldIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "oldidx");
			mir::MIRValue *newIndex = builder->createAdd(oldIndex, builder->getInt32(1), "newidx");
			builder->createStore(newIndex, indexAlloca);
			builder->createBr(condBB);

			namedValues.erase(forStmt->loopVar);
			loopStack.pop_back();

			builder->setInsertPoint(afterBB);
			return;
		}

		// Check for range() call - compile inline for performance
		// This avoids function call overhead for iter.hasNext/getCurrent/next on every iteration
		if (auto *callExpr = dynamic_cast<CallExprAST *>(forStmt->iterable.get())) {
			if (callExpr->callee == "range" && callExpr->args.size() == 2) {
				// Generate inline range loop: for i in range(start, end) becomes:
				// var i = start; while i < end { ... ; i = i + 1 }

				mir::MIRValue *startVal = generateExpr(callExpr->args[0].get());
				mir::MIRValue *endVal = generateExpr(callExpr->args[1].get());

				if (!startVal || !endVal) {
					throw std::runtime_error("Failed to generate range bounds");
				}

				// Create loop variable alloca and initialize with start value
				mir::MIRValue *loopVarAlloca = builder->createAlloca(mir::MIRType::getInt32(), forStmt->loopVar);
				builder->createStore(startVal, loopVarAlloca);
				namedValues[forStmt->loopVar] = loopVarAlloca;
				variableTypes[forStmt->loopVar] = "int";

				// Store end value in alloca (in case it's a complex expression)
				mir::MIRValue *endAlloca = builder->createAlloca(mir::MIRType::getInt32(), "__range_end");
				builder->createStore(endVal, endAlloca);

				mir::MIRBasicBlock *condBB = builder->createBasicBlock("forcond");
				mir::MIRBasicBlock *loopBB = builder->createBasicBlock("forloop");
				mir::MIRBasicBlock *incrementBB = builder->createBasicBlock("forincrement");
				mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterfor");

				loopStack.push_back({forStmt->blockId, incrementBB, afterBB});

				builder->createBr(condBB);

				// Condition: loopVar < end
				builder->setInsertPoint(condBB);
				mir::MIRValue *currentVal = builder->createLoad(mir::MIRType::getInt32(), loopVarAlloca, "i");
				mir::MIRValue *endValLoad = builder->createLoad(mir::MIRType::getInt32(), endAlloca, "end");
				mir::MIRValue *condVal = builder->createICmpSLT(currentVal, endValLoad, "rangecond");
				builder->createCondBr(condVal, loopBB, afterBB);

				// Loop body
				builder->setInsertPoint(loopBB);

				for (auto &s : forStmt->body) {
					generateStmt(s.get(), function);
				}

				if (!builder->getInsertBlock()->hasTerminator()) {
					builder->createBr(incrementBB);
				}

				// Increment: loopVar++
				builder->setInsertPoint(incrementBB);
				mir::MIRValue *oldVal = builder->createLoad(mir::MIRType::getInt32(), loopVarAlloca, "oldval");
				mir::MIRValue *newVal = builder->createAdd(oldVal, builder->getInt32(1), "newval");
				builder->createStore(newVal, loopVarAlloca);
				builder->createBr(condBB);

				namedValues.erase(forStmt->loopVar);
				loopStack.pop_back();

				builder->setInsertPoint(afterBB);
				return;
			}
		}

		// Non-array iteration (range, etc.): use iterator interface
		mir::MIRValue *iteratorVal = generateExpr(forStmt->iterable.get());
		if (!iteratorVal) {
			throw std::runtime_error("Failed to generate for-loop iterable expression");
		}

		// Create alloca for iterator
		mir::MIRValue *iteratorAlloca = builder->createAlloca(iteratorVal->type, "__iter");
		builder->createStore(iteratorVal, iteratorAlloca);

		// Get the iterator type name to look up the correct Iterable methods
		std::string iterTypeName = iteratorVal->type->structName;

		mir::MIRBasicBlock *condBB = builder->createBasicBlock("forcond");
		mir::MIRBasicBlock *loopBB = builder->createBasicBlock("forloop");
		mir::MIRBasicBlock *incrementBB = builder->createBasicBlock("forincrement");
		mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterfor");

		loopStack.push_back({forStmt->blockId, incrementBB, afterBB});

		builder->createBr(condBB);

		// Condition block
		builder->setInsertPoint(condBB);

		// Look up TypeName.hasNext first, then fall back to suffix matching
		mir::MIRFunction *hasNextFunc = nullptr;
		if (!iterTypeName.empty()) {
			hasNextFunc = module->getFunction(iterTypeName + ".hasNext");
		}
		if (!hasNextFunc) {
			hasNextFunc = module->getFunction("hasNext");
		}
		if (!hasNextFunc) {
			// Try suffix matching as last resort
			for (auto &func : module->functions) {
				if (func->name.size() > 8 &&
					func->name.substr(func->name.size() - 8) == ".hasNext") {
					hasNextFunc = func.get();
					break;
				}
			}
		}
		if (!hasNextFunc) {
			throw std::runtime_error("For-loop requires 'hasNext' function from stdlib");
		}

		mir::MIRValue *hasNextResult = builder->createCall(hasNextFunc, {iteratorAlloca}, "hasNext.result");
		mir::MIRValue *one = builder->getInt32(1);
		mir::MIRValue *condVal = builder->createICmpEq(hasNextResult, one, "forcond");

		builder->createCondBr(condVal, loopBB, afterBB);

		// Loop body
		builder->setInsertPoint(loopBB);

		// Look up TypeName.getCurrent first, then fall back to suffix matching
		mir::MIRFunction *getCurrentFunc = nullptr;
		if (!iterTypeName.empty()) {
			getCurrentFunc = module->getFunction(iterTypeName + ".getCurrent");
		}
		if (!getCurrentFunc) {
			getCurrentFunc = module->getFunction("getCurrent");
		}
		if (!getCurrentFunc) {
			for (auto &func : module->functions) {
				if (func->name.size() > 11 &&
					func->name.substr(func->name.size() - 11) == ".getCurrent") {
					getCurrentFunc = func.get();
					break;
				}
			}
		}
		if (!getCurrentFunc) {
			throw std::runtime_error("For-loop requires 'getCurrent' function from stdlib");
		}

		mir::MIRValue *currentVal = builder->createCall(getCurrentFunc, {iteratorAlloca}, forStmt->loopVar);

		mir::MIRValue *loopVarAlloca = builder->createAlloca(mir::MIRType::getInt32(), forStmt->loopVar);
		builder->createStore(currentVal, loopVarAlloca);
		namedValues[forStmt->loopVar] = loopVarAlloca;

		for (auto &s : forStmt->body) {
			generateStmt(s.get(), function);
		}

		if (!builder->getInsertBlock()->hasTerminator()) {
			builder->createBr(incrementBB);
		}

		// Increment block
		builder->setInsertPoint(incrementBB);

		// Look up TypeName.next first, then fall back to suffix matching
		mir::MIRFunction *nextFunc = nullptr;
		if (!iterTypeName.empty()) {
			nextFunc = module->getFunction(iterTypeName + ".next");
		}
		if (!nextFunc) {
			nextFunc = module->getFunction("next");
		}
		if (!nextFunc) {
			for (auto &func : module->functions) {
				if (func->name.size() > 5 &&
					func->name.substr(func->name.size() - 5) == ".next") {
					nextFunc = func.get();
					break;
				}
			}
		}
		if (!nextFunc) {
			throw std::runtime_error("For-loop requires 'next' function from stdlib");
		}

		mir::MIRValue *nextResult = builder->createCall(nextFunc, {iteratorAlloca}, "__iter.next");
		builder->createStore(nextResult, iteratorAlloca);

		builder->createBr(condBB);

		namedValues.erase(forStmt->loopVar);
		loopStack.pop_back();

		builder->setInsertPoint(afterBB);
		return;
	}

	if (auto *breakStmt = dynamic_cast<BreakStmtAST *>(stmt)) {
		if (loopStack.empty()) {
			throw std::runtime_error("Break statement outside of loop");
		}

		mir::MIRBasicBlock *targetBlock = nullptr;

		if (breakStmt->targetLabel.empty()) {
			targetBlock = loopStack.back().afterBlock;
		} else {
			for (auto it = loopStack.rbegin(); it != loopStack.rend(); ++it) {
				if (it->label == breakStmt->targetLabel) {
					targetBlock = it->afterBlock;
					break;
				}
			}

			if (!targetBlock) {
				throw std::runtime_error("Break target label '" + breakStmt->targetLabel +
										 "' not found in enclosing loops");
			}
		}

		builder->createBr(targetBlock);

		// Create dead code block
		mir::MIRBasicBlock *deadBB = builder->createBasicBlock("afterbreak");
		builder->setInsertPoint(deadBB);
		return;
	}

	if (auto *continueStmt = dynamic_cast<ContinueStmtAST *>(stmt)) {
		if (loopStack.empty()) {
			throw std::runtime_error("Continue statement outside of loop");
		}

		mir::MIRBasicBlock *targetBlock = nullptr;

		if (continueStmt->targetLabel.empty()) {
			targetBlock = loopStack.back().condBlock;
		} else {
			for (auto it = loopStack.rbegin(); it != loopStack.rend(); ++it) {
				if (it->label == continueStmt->targetLabel) {
					targetBlock = it->condBlock;
					break;
				}
			}

			if (!targetBlock) {
				throw std::runtime_error("Continue target label '" + continueStmt->targetLabel +
										 "' not found in enclosing loops");
			}
		}

		builder->createBr(targetBlock);

		mir::MIRBasicBlock *deadBB = builder->createBasicBlock("aftercontinue");
		builder->setInsertPoint(deadBB);
		return;
	}

	if (auto *retStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		mir::MIRValue *retVal = nullptr;

		// Special handling for struct literals in return statements
		if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(retStmt->value.get())) {
			mir::MIRType *structType = structTypes[structInitExpr->structName];
			if (!structType) {
				throw std::runtime_error("Unknown struct type: " + structInitExpr->structName);
			}

			// Create temporary alloca for the struct
			mir::MIRValue *structAlloca = builder->createAlloca(structType, "ret.tmp");

			// Initialize fields
			const auto &fields = structFields[structInitExpr->structName];
			for (const auto &initField : structInitExpr->fields) {
				int fieldIndex = -1;
				std::string fieldType;
				for (size_t j = 0; j < fields.size(); j++) {
					if (fields[j].first == initField.name) {
						fieldIndex = static_cast<int>(j);
						fieldType = fields[j].second;
						break;
					}
				}

				if (fieldIndex < 0) {
					throw std::runtime_error("Unknown field '" + initField.name +
											 "' in struct '" + structInitExpr->structName + "'");
				}

				mir::MIRValue *fieldValue = generateExpr(initField.value.get());
				mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
																   fieldIndex, initField.name);

				// If the field is an unsized array type (like []byte), the value from generateExpr
				// is a pointer to the fat pointer. We need to load the fat pointer struct and
				// store it (copy semantics for the fat pointer).
				if (fieldType.size() >= 2 && fieldType[0] == '[' && fieldType[1] == ']') {
					// Unsized array - load the fat pointer struct
					mir::MIRType *fatPtrType = structType->fieldTypes[fieldIndex];
					mir::MIRValue *fatPtrValue = builder->createLoad(fatPtrType, fieldValue, initField.name + ".fatptr");
					builder->createStore(fatPtrValue, fieldPtr);
				} else {
					builder->createStore(fieldValue, fieldPtr);
				}
			}

			// Load the struct for return
			retVal = builder->createLoad(structType, structAlloca, "ret.val");
		} else {
			retVal = generateExpr(retStmt->value.get());
			if (!retVal) {
				throw std::runtime_error("Failed to generate return value");
			}
		}

		// Clean up all scopes before returning
		while (!scopeStack.empty()) {
			generateScopeCleanup(function);
			scopeStack.pop_back();
		}

		builder->createRet(retVal);
		return;
	}

	throw std::runtime_error("Unknown statement type");
}
