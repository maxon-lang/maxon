/**
 * MIR Code Generator - Control Flow Statements
 *
 * This file implements code generation for loops (while, for),
 * conditionals (if), and control flow (break, continue).
 */

#include "../codegen_mir.h"
#include <stdexcept>

void MIRCodeGenerator::generateIf(IfStmtAST *ifStmt, mir::MIRFunction *function) {
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
		// Stop after terminator - subsequent code is unreachable
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
	}
	// Check current insert block (may differ from thenBB if body contains control flow)
	mir::MIRBasicBlock *thenEndBB = builder->getInsertBlock();
	if (!thenEndBB->hasTerminator()) {
		builder->createBr(mergeBB);
	}

	// Generate else block
	if (hasElse) {
		builder->setInsertPoint(elseBB);
		for (auto &s : ifStmt->elseBody) {
			generateStmt(s.get(), function);
			// Stop after terminator - subsequent code is unreachable
			if (builder->getInsertBlock()->hasTerminator()) {
				break;
			}
		}
		// Check current insert block (may differ from elseBB if body contains control flow)
		mir::MIRBasicBlock *elseEndBB = builder->getInsertBlock();
		if (!elseEndBB->hasTerminator()) {
			builder->createBr(mergeBB);
		}
	}

	// Set insert point to merge block
	builder->setInsertPoint(mergeBB);
}

void MIRCodeGenerator::generateWhile(WhileStmtAST *whileStmt, mir::MIRFunction *function) {
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
		// Stop after terminator (break, continue, return) - subsequent code is unreachable
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
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
}

void MIRCodeGenerator::generateFor(ForStmtAST *forStmt, mir::MIRFunction *function) {
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
			// Stop after terminator - subsequent code is unreachable
			if (builder->getInsertBlock()->hasTerminator()) {
				break;
			}
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
				// Stop after terminator - subsequent code is unreachable
				if (builder->getInsertBlock()->hasTerminator()) {
					break;
				}
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

	// Determine loop variable type from getCurrent's actual return type
	// This ensures we match the struct type correctly (e.g., char struct vs i8)
	mir::MIRType *loopVarType = getCurrentFunc->returnType;
	std::string loopVarTypeStr = "int"; // Default for variableTypes map

	// Get the type string from the return type for variableTypes tracking
	if (loopVarType->isStruct()) {
		loopVarTypeStr = loopVarType->structName;
	} else if (loopVarType == mir::MIRType::getInt32()) {
		loopVarTypeStr = "int";
	} else if (loopVarType == mir::MIRType::getInt8()) {
		loopVarTypeStr = "byte";
	} else if (loopVarType == mir::MIRType::getFloat64()) {
		loopVarTypeStr = "float";
	} else if (loopVarType == mir::MIRType::getInt1()) {
		loopVarTypeStr = "bool";
	} else if (loopVarType == mir::MIRType::getPtr()) {
		loopVarTypeStr = "ptr";
	}

	mir::MIRValue *loopVarAlloca = builder->createAlloca(loopVarType, forStmt->loopVar);
	builder->createStore(currentVal, loopVarAlloca);
	namedValues[forStmt->loopVar] = loopVarAlloca;
	variableTypes[forStmt->loopVar] = loopVarTypeStr;

	for (auto &s : forStmt->body) {
		generateStmt(s.get(), function);
		// Stop after terminator - subsequent code is unreachable
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
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
}

void MIRCodeGenerator::generateBreak(BreakStmtAST *breakStmt, mir::MIRFunction *function) {
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
}

void MIRCodeGenerator::generateContinue(ContinueStmtAST *continueStmt, mir::MIRFunction *function) {
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
}
