/**
 * MIR Code Generator - Statement Generation
 *
 * This file implements statement code generation for MIR.
 * It dispatches to specialized files for different statement types:
 * - codegen_mir_stmt_decl.cpp: var and let declarations
 * - codegen_mir_stmt_assign.cpp: assignments
 * - codegen_mir_stmt_loop.cpp: if, while, for, break, continue
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <algorithm>
#include <stdexcept>

void MIRCodeGenerator::generateStmt(StmtAST *stmt, mir::MIRFunction *function) {
	if (auto *varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
		generateVarDecl(varDecl, function);
		return;
	}

	if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		generateLetDecl(letDecl, function);
		return;
	}

	if (auto *assign = dynamic_cast<AssignStmtAST *>(stmt)) {
		generateAssign(assign, function);
		return;
	}

	if (auto *arrayAssign = dynamic_cast<ArrayAssignStmtAST *>(stmt)) {
		generateArrayAssign(arrayAssign, function);
		return;
	}

	if (auto *arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST *>(stmt)) {
		generateArrayMemberAssign(arrayMemberAssign, function);
		return;
	}

	if (auto *memberAssign = dynamic_cast<MemberAssignStmtAST *>(stmt)) {
		generateMemberAssign(memberAssign, function);
		return;
	}

	if (auto *memberArrayAssign = dynamic_cast<MemberArrayAssignStmtAST *>(stmt)) {
		generateMemberArrayAssign(memberArrayAssign, function);
		return;
	}

	if (auto *exprStmt = dynamic_cast<ExprStmtAST *>(stmt)) {
		generateExpr(exprStmt->expression.get());
		return;
	}

	if (auto *ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
		generateIf(ifStmt, function);
		return;
	}

	if (auto *ifLet = dynamic_cast<IfLetStmtAST *>(stmt)) {
		generateIfLet(ifLet, function);
		return;
	}

	if (auto *elseUnwrap = dynamic_cast<ElseUnwrapStmtAST *>(stmt)) {
		generateElseUnwrap(elseUnwrap, function);
		return;
	}

	if (auto *guardLet = dynamic_cast<GuardLetStmtAST *>(stmt)) {
		generateGuardLet(guardLet, function);
		return;
	}

	if (auto *whileStmt = dynamic_cast<WhileStmtAST *>(stmt)) {
		generateWhile(whileStmt, function);
		return;
	}

	if (auto *forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		generateFor(forStmt, function);
		return;
	}

	if (auto *breakStmt = dynamic_cast<BreakStmtAST *>(stmt)) {
		generateBreak(breakStmt, function);
		return;
	}

	if (auto *continueStmt = dynamic_cast<ContinueStmtAST *>(stmt)) {
		generateContinue(continueStmt, function);
		return;
	}

	if (auto *matchStmt = dynamic_cast<MatchStmtAST *>(stmt)) {
		generateMatch(matchStmt, function);
		return;
	}

	if (auto *retStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		mir::MIRValue *retVal = nullptr;

		// Special handling for struct literals in return statements
		if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(retStmt->value.get())) {
			// Substitute "Self" with current receiver type
			std::string structName = structInitExpr->structName;
			if (structName == "Self" && !currentReceiverType.empty()) {
				structName = currentReceiverType;
			}

			mir::MIRType *structType = structTypes[structName];
			if (!structType) {
				reportError("Unknown struct type: " + structName,
							structInitExpr->line, structInitExpr->column);
			}

			// Create temporary alloca for the struct
			mir::MIRValue *structAlloca = builder->createAlloca(structType, "ret.tmp");

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
				const std::string &fieldType = fields[fieldIndex].second;

				// Get the value expression - either from provided init or from default
				ExprAST *valueExpr = nullptr;
				auto providedIt = providedFields.find(fieldName);
				if (providedIt != providedFields.end()) {
					valueExpr = providedIt->second->value.get();
				} else {
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

				mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
																   static_cast<int>(fieldIndex), fieldName);

				// Handle sized array expression: array of T (empty) or array of N T (sized)
				if (auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(valueExpr)) {
					std::string elementTypeName = sizedArray->elementType;

					// Substitute type parameters if we're inside a generic method
					if (!currentTypeBindings.empty()) {
						auto bindingIt = currentTypeBindings.find(elementTypeName);
						if (bindingIt != currentTypeBindings.end()) {
							elementTypeName = bindingIt->second;
						}
					}

					mir::MIRType *elementType = getTypeFromString(elementTypeName);
					mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
					mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

					if (sizedArray->hasVariableSize() || sizedArray->size > 0) {
						// Sized array - allocate buffer
						int64_t arraySize = sizedArray->size;
						mir::MIRValue *sizeVal = nullptr;
						if (sizedArray->hasVariableSize()) {
							sizeVal = generateExpr(sizedArray->sizeExpr.get());
						} else {
							sizeVal = builder->getInt64(arraySize);
						}

						initHeapManagement();
						mir::MIRFunction *mallocFunc = module->getFunction("malloc");
						uint64_t elementSize = elementType->sizeInBytes;
						mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
						mir::MIRValue *sizeExt = builder->createSExt(sizeVal, mir::MIRType::getInt64(), "size.ext");
						mir::MIRValue *totalSize = builder->createMul(sizeExt, elemSizeVal, "total.size");
						mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {totalSize}, fieldName + ".buffer");

						mir::MIRFunction *memsetFunc = module->getFunction("memset");
						if (!memsetFunc) {
							initHeapManagement();
							memsetFunc = module->getFunction("memset");
						}
						builder->createCall(memsetFunc, {bufferPtr, builder->getInt64(0), totalSize});

						mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, fieldName + ".managed");
						mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, fieldName + "._buffer");
						builder->createStore(bufferPtr, bufferField);
						mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, fieldName + "._len");
						builder->createStore(sizeVal, lenField);
						mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, fieldName + "._capacity");
						builder->createStore(sizeVal, capField);
						mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, fieldPtr, 1, fieldName + ".iterIndex");
						builder->createStore(builder->getInt64(0), iterField);
					} else {
						// Empty array - initialize with nullptr and zero length
						mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, fieldName + ".managed");
						mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, fieldName + "._buffer");
						builder->createStore(mir::MIRValue::createConstantNull(), bufferField);
						mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, fieldName + "._len");
						builder->createStore(builder->getInt64(0), lenField);
						mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, fieldName + "._capacity");
						builder->createStore(builder->getInt64(0), capField);
						mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, fieldPtr, 1, fieldName + ".iterIndex");
						builder->createStore(builder->getInt64(0), iterField);
					}
					continue;
				}

				// Handle empty map literal: map from K to V
				if (auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(valueExpr)) {
					const std::string &keyType = mapLiteral->keyType;
					const std::string &valueType = mapLiteral->valueType;
					std::string specializedName = "map<" + keyType + "," + valueType + ">";

					// Instantiate the generic struct if not already done
					if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
						if (structDefinitions.find("map") != structDefinitions.end()) {
							std::map<std::string, std::string> typeBindings = {
								{"Key", keyType},
								{"Value", valueType}};
							instantiateGenericStruct("map", typeBindings);
						} else {
							reportError("Generic struct 'map' not found", mapLiteral->line, mapLiteral->column);
						}
					}

					mir::MIRType *mapStructType = structTypes[specializedName];
					if (!mapStructType) {
						reportError("Failed to instantiate map type: " + specializedName,
									mapLiteral->line, mapLiteral->column);
					}

					// Get element types
					mir::MIRType *keyMirType = getTypeFromString(keyType);
					mir::MIRType *valueMirType = getTypeFromString(valueType);

					// Create empty _ManagedArray<Key> struct for keys
					mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
					mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

					// Create empty _ManagedArray<Value> struct for values
					mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
					mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

					// Create minimal stack buffers
					mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, 1);
					mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

					mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, 1);
					mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

					// Initialize empty keys _ManagedArray struct fields
					mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
					builder->createStore(keysStackBuffer, keysBufferField);
					mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
					builder->createStore(builder->getInt64(0), keysLenField);
					mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
					builder->createStore(builder->getInt64(0), keysCapField);

					// Initialize empty values _ManagedArray struct fields
					mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
					builder->createStore(valuesStackBuffer, valuesBufferField);
					mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
					builder->createStore(builder->getInt64(0), valuesLenField);
					mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
					builder->createStore(builder->getInt64(0), valuesCapField);

					// Call map<K,V>.init(fieldPtr, keysManaged, valuesManaged)
					std::string initMethodName = specializedName + ".init";
					mir::MIRFunction *initFunc = module->getFunction(initMethodName);
					if (!initFunc) {
						reportError("map.init method not found for type: " + specializedName,
									mapLiteral->line, mapLiteral->column);
					}

					builder->createCall(initFunc, {fieldPtr, keysManaged, valuesManaged}, "");
					continue;
				}

				mir::MIRValue *fieldValue = generateExpr(valueExpr);

				// If the field is an unsized array type (like []byte), we need to handle
				// the conversion from either a fat pointer or variable-sized array
				if (maxon::TypeConversion::isManagedArrayType(fieldType)) {
					// Get the value's type if it's a variable
					std::string valueType;
					auto *varExpr = dynamic_cast<VariableExprAST *>(valueExpr);
					if (varExpr) {
						auto it = variableTypes.find(varExpr->name);
						if (it != variableTypes.end()) {
							valueType = it->second;
						}
					}

					// Check if the value is a variable-sized (managed) array
					bool isVariableSizedArray = maxon::TypeConversion::isManagedArrayType(valueType);

					mir::MIRType *fatPtrType = structType->fieldTypes[fieldIndex];

					if (isVariableSizedArray && varExpr) {
						// Variable-sized array - load data pointer and length from header
						mir::MIRValue *arrayAlloca = namedValues[varExpr->name];
						if (arrayAlloca) {
							// Load the data pointer
							mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");

							// Load length from header at dataPtr - 8
							mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
																		  {builder->getInt64(-8)}, "length.ptr");
							mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt64(), lengthPtr, "length");

							// Store into the fat pointer field
							mir::MIRValue *fatPtrDataPtr = builder->createStructGEP(fatPtrType, fieldPtr, 0,
																					fieldName + ".ptr");
							builder->createStore(dataPtr, fatPtrDataPtr);
							mir::MIRValue *fatPtrLenPtr = builder->createStructGEP(fatPtrType, fieldPtr, 1,
																				   fieldName + ".len");
							builder->createStore(length, fatPtrLenPtr);
						} else {
							reportError("Unknown variable in return struct field init: " + varExpr->name,
										varExpr->line, varExpr->column);
						}
					} else {
						// Unsized array - load the fat pointer struct and store it
						mir::MIRValue *fatPtrValue = builder->createLoad(fatPtrType, fieldValue, fieldName + ".fatptr");
						builder->createStore(fatPtrValue, fieldPtr);
					}
				} else {
					builder->createStore(fieldValue, fieldPtr);
				}
			}

			// Load the struct for return
			retVal = builder->createLoad(structType, structAlloca, "ret.val");

			// Handle implicit wrapping for optional return types (struct literals)
			std::string returnTypeStr = functionReturnTypes[function->name];
			if (maxon::TypeConversion::isOptionalType(returnTypeStr)) {
				mir::MIRType *returnType = getTypeFromString(returnTypeStr);
				retVal = createSomeOptional(returnType, retVal);
			}
		} else if (retStmt->value) {
			retVal = generateExpr(retStmt->value.get());

			// For struct types (like string), generateExpr returns an alloca pointer.
			// We need to load the value to return it by-value.
			if (retVal && retVal->type == mir::MIRType::getPtr()) {
				std::string returnTypeStr = functionReturnTypes[function->name];
				// Strip "or nil" suffix for base type lookup
				std::string baseType = returnTypeStr;
				size_t orNilPos = returnTypeStr.find(" or nil");
				if (orNilPos != std::string::npos) {
					baseType = returnTypeStr.substr(0, orNilPos);
				}
				mir::MIRType *returnType = getTypeFromString(baseType);
				if (returnType && returnType->isStruct()) {
					retVal = builder->createLoad(returnType, retVal, "ret.val");
				}
			}

			// Handle nil literal (generateExpr returns nullptr for nil)
			if (!retVal) {
				// Get function return type
				std::string returnTypeStr = functionReturnTypes[function->name];
				mir::MIRType *returnType = getTypeFromString(returnTypeStr);

				// Create nil optional
				retVal = createNilOptional(returnType);
			}
			// Handle implicit wrapping for optional return types
			else {
				std::string returnTypeStr = functionReturnTypes[function->name];

				// Check if return type is optional
				if (maxon::TypeConversion::isOptionalType(returnTypeStr)) {
					// Check if the expression type is already optional
					std::string exprType = getExpressionMaxonType(retStmt->value.get());

					// Only wrap if the expression is not already an optional type
					if (!maxon::TypeConversion::isOptionalType(exprType)) {
						mir::MIRType *returnType = getTypeFromString(returnTypeStr);

						// Wrap the value in an optional
						retVal = createSomeOptional(returnType, retVal);
					}
				}
			}
		}
		// else retVal stays nullptr for void return

		// Clean up all scopes before returning
		while (!scopeStack.empty()) {
			generateScopeCleanup(function);
			scopeStack.pop_back();
		}

		if (retVal) {
			builder->createRet(retVal);
		} else {
			builder->createRetVoid();
		}
		return;
	}

	reportError("Unknown statement type", stmt->line, stmt->column);
}
