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

				mir::MIRValue *fieldValue = generateExpr(valueExpr);
				mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
																   static_cast<int>(fieldIndex), fieldName);

				// If the field is an unsized array type (like []byte), we need to handle
				// the conversion from either a fat pointer, static array, or variable-sized array
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

					// Check if the value is a static array or variable-sized (managed) array
					bool isStaticArray = maxon::TypeConversion::isStaticArrayType(valueType);
					bool isVariableSizedArray = maxon::TypeConversion::isManagedArrayType(valueType);
					int staticArraySize = 0;
					std::string elemType;
					if (isStaticArray) {
						staticArraySize = maxon::TypeConversion::getStaticArraySize(valueType);
						elemType = maxon::TypeConversion::getArrayElementType(valueType);
					} else if (isVariableSizedArray) {
						elemType = maxon::TypeConversion::getArrayElementType(valueType);
					}

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
							mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");

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
					} else if (isStaticArray) {
						// Convert static array to fat pointer:
						// fieldValue is a pointer to the static array [size x elemType]
						// We need to store {ptr, len} into the fat pointer field

						// Store the data pointer (field 0 of the fat pointer)
						mir::MIRValue *fatPtrDataPtr = builder->createStructGEP(fatPtrType, fieldPtr, 0,
																				fieldName + ".ptr");
						builder->createStore(fieldValue, fatPtrDataPtr);

						// Store the length (field 1 of the fat pointer)
						// Use the actual string length from _len field, not the buffer size
						// But we don't have _len yet - use the static array size as max
						// Actually, we need to get _len from the struct literal
						// For now, find the _len field in the struct literal
						int strLen = staticArraySize; // Default to buffer size
						for (const auto &otherField : structInitExpr->fields) {
							if (otherField.name == "_len") {
								if (auto *memberExpr = dynamic_cast<MemberAccessExprAST *>(otherField.value.get())) {
									// _len: _len means copy from self._len
									// We need to load this from self
									if (memberExpr->memberName == "_len") {
										// Get self pointer
										auto selfIt = namedValues.find("self");
										if (selfIt != namedValues.end()) {
											mir::MIRType *selfStructType = structTypes[structInitExpr->structName];
											// Find _len field index
											const auto &selfFields = structFields[structInitExpr->structName];
											for (size_t fi = 0; fi < selfFields.size(); fi++) {
												if (selfFields[fi].first == "_len") {
													mir::MIRValue *selfLenPtr = builder->createStructGEP(
														selfStructType, selfIt->second, static_cast<int>(fi), "self._len.ptr");
													mir::MIRValue *selfLenVal = builder->createLoad(
														mir::MIRType::getInt32(), selfLenPtr, "self._len.val");
													mir::MIRValue *fatPtrLenPtr = builder->createStructGEP(
														fatPtrType, fieldPtr, 1, fieldName + ".len");
													builder->createStore(selfLenVal, fatPtrLenPtr);
													goto done_len_store;
												}
											}
										}
									}
								}
								break;
							}
						}
						{
							// Fallback: use static array size
							mir::MIRValue *fatPtrLenPtr = builder->createStructGEP(fatPtrType, fieldPtr, 1,
																				   fieldName + ".len");
							builder->createStore(builder->getInt32(strLen), fatPtrLenPtr);
						}
					done_len_store:;
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
		} else if (retStmt->value) {
			retVal = generateExpr(retStmt->value.get());

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
					mir::MIRType *returnType = getTypeFromString(returnTypeStr);

					// Wrap the value in an optional
					retVal = createSomeOptional(returnType, retVal);
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
