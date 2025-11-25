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

			initHeapManagement();

			// Calculate size
			uint64_t elementSize = elementType->sizeInBytes;
			uint64_t totalSize = arraySize * elementSize;

			// Call malloc
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");
			mir::MIRValue *sizeVal = builder->getInt64(totalSize);
			mir::MIRValue *arrayPtr = builder->createCall(mallocFunc, {sizeVal}, varDecl->name + ".malloc");

			// Create alloca to store the pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);

			// Store array size as hidden variable
			mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), varDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
			builder->createStore(arraySizeVal, sizeAlloca);
			namedValues[varDecl->name + ".__length"] = sizeAlloca;

			// Initialize array elements
			if (initValues.empty()) {
				// Zero-initialize using memset
				mir::MIRFunction *memsetFunc = module->getFunction("memset");
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

			namedValues[varDecl->name] = ptrAlloca;
			variableTypes[varDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;

			// Track for cleanup
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, ptrAlloca});
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
				for (size_t j = 0; j < fields.size(); j++) {
					if (fields[j].first == initField.name) {
						fieldIndex = static_cast<int>(j);
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
				builder->createStore(fieldValue, fieldPtr);
			}

			return;
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
		variableTypes[varDecl->name] = varDecl->type;
		return;
	}

	if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		// Handle array literal initialization (same as var)
		if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(letDecl->initializer.get())) {
			int arraySize;
			mir::MIRType *elementType;
			std::string elementTypeName;
			std::vector<mir::MIRValue *> initValues;

			if (arrayLiteral->size > 0) {
				arraySize = arrayLiteral->size;
				elementTypeName = arrayLiteral->elementType;
				elementType = getTypeFromString(elementTypeName);
			} else {
				arraySize = static_cast<int>(arrayLiteral->values.size());
				for (auto &valExpr : arrayLiteral->values) {
					mir::MIRValue *val = generateExpr(valExpr.get());
					if (!val) {
						throw std::runtime_error("Failed to generate array element value");
					}
					initValues.push_back(val);
				}

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

			initHeapManagement();

			uint64_t elementSize = elementType->sizeInBytes;
			uint64_t totalSize = arraySize * elementSize;

			mir::MIRFunction *mallocFunc = module->getFunction("malloc");
			mir::MIRValue *sizeVal = builder->getInt64(totalSize);
			mir::MIRValue *arrayPtr = builder->createCall(mallocFunc, {sizeVal}, letDecl->name + ".malloc");

			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);

			mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), letDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
			builder->createStore(arraySizeVal, sizeAlloca);
			namedValues[letDecl->name + ".__length"] = sizeAlloca;

			if (initValues.empty()) {
				mir::MIRFunction *memsetFunc = module->getFunction("memset");
				mir::MIRValue *zeroVal = builder->getInt32(0);
				mir::MIRValue *memsetSizeVal = builder->getInt64(totalSize);
				builder->createCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
			} else {
				for (int i = 0; i < arraySize; i++) {
					mir::MIRValue *indexVal = builder->getInt32(i);
					mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
					builder->createStore(initValues[i], elementPtr);
				}
			}

			namedValues[letDecl->name] = ptrAlloca;
			variableTypes[letDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;

			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, ptrAlloca});
			}

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
		variableTypes[letDecl->name] = letDecl->type;
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

		builder->createStore(val, alloca);
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

		// Load the array pointer
		mir::MIRValue *arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
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

	if (auto *derefAssign = dynamic_cast<DerefAssignStmtAST *>(stmt)) {
		mir::MIRValue *ptr = generateExpr(derefAssign->pointer.get());
		if (!ptr) {
			throw std::runtime_error("Failed to generate pointer for dereference assignment");
		}

		mir::MIRValue *val = generateExpr(derefAssign->value.get());
		if (!val) {
			throw std::runtime_error("Failed to generate value for dereference assignment");
		}

		builder->createStore(val, ptr);
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
		bool thenTerminated = thenBB->hasTerminator();
		if (!thenTerminated) {
			builder->createBr(mergeBB);
		}

		// Generate else block
		bool elseTerminated = false;
		if (hasElse) {
			builder->setInsertPoint(elseBB);
			for (auto &s : ifStmt->elseBody) {
				generateStmt(s.get(), function);
			}
			elseTerminated = elseBB->hasTerminator();
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
		// Generate iterator initialization
		mir::MIRValue *iteratorVal = generateExpr(forStmt->iterable.get());
		if (!iteratorVal) {
			throw std::runtime_error("Failed to generate for-loop iterable expression");
		}

		// Create alloca for iterator
		mir::MIRValue *iteratorAlloca = builder->createAlloca(iteratorVal->type, "__iter");
		builder->createStore(iteratorVal, iteratorAlloca);

		mir::MIRBasicBlock *condBB = builder->createBasicBlock("forcond");
		mir::MIRBasicBlock *loopBB = builder->createBasicBlock("forloop");
		mir::MIRBasicBlock *incrementBB = builder->createBasicBlock("forincrement");
		mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterfor");

		loopStack.push_back({forStmt->blockId, incrementBB, afterBB});

		builder->createBr(condBB);

		// Condition block
		builder->setInsertPoint(condBB);

		mir::MIRFunction *hasNextFunc = module->getFunction("hasNext");
		if (!hasNextFunc) {
			// Try suffix matching
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

		mir::MIRFunction *getCurrentFunc = module->getFunction("getCurrent");
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

		mir::MIRFunction *nextFunc = module->getFunction("next");
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
		mir::MIRValue *retVal = generateExpr(retStmt->value.get());
		if (!retVal) {
			throw std::runtime_error("Failed to generate return value");
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
