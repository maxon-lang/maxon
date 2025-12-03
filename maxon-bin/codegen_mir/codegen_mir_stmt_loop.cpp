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
		reportError("Failed to generate if condition",
					ifStmt->line, ifStmt->column);
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
		reportError("Failed to generate while condition",
					whileStmt->line, whileStmt->column);
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
			reportError("Unknown array variable: " + arrayVarName,
						forStmt->line, forStmt->column);
		}

		// Get array length
		mir::MIRValue *lengthAlloca = namedValues[arrayVarName + ".__length"];
		if (!lengthAlloca) {
			reportError("Array length not found for: " + arrayVarName,
						forStmt->line, forStmt->column);
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
				reportError("Failed to generate range bounds",
							forStmt->line, forStmt->column);
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
		reportError("Failed to generate for-loop iterable expression",
					forStmt->line, forStmt->column);
	}

	// Create alloca for iterator
	mir::MIRValue *iteratorAlloca = builder->createAlloca(iteratorVal->type, "__iter");
	builder->createStore(iteratorVal, iteratorAlloca);

	// Get the iterator type name to look up the correct Iterable methods
	std::string iterTypeName = iteratorVal->type->structName;

	mir::MIRBasicBlock *condBB = builder->createBasicBlock("forcond");
	mir::MIRBasicBlock *loopBB = builder->createBasicBlock("forloop");
	mir::MIRBasicBlock *afterBB = builder->createBasicBlock("afterfor");

	loopStack.push_back({forStmt->blockId, condBB, afterBB});

	builder->createBr(condBB);

	// Condition block - call next() and check if result is nil
	builder->setInsertPoint(condBB);

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
		reportError("For-loop requires 'next' function from stdlib",
					forStmt->line, forStmt->column);
	}

	// Call next() to get optional result
	mir::MIRValue *optionalResult = builder->createCall(nextFunc, {iteratorAlloca}, "__iter.next");

	// next() returns "Element or nil" which is an optional type with layout:
	// { i8 tag, [padding], Element value }
	// Tag = 0 for nil, 1 for has value
	mir::MIRType *optionalType = nextFunc->returnType;
	if (optionalType->kind != mir::MIRTypeKind::Optional) {
		reportError("For-loop next() must return optional type (Element or nil)",
					forStmt->line, forStmt->column);
	}

	// Store optional result
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "__opt");
	builder->createStore(optionalResult, optionalAlloca);

	// Extract tag (field 0) to check if we have a value - use optionalType for correct offset
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
	mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");

	// Check if tag == 1 (has value)
	mir::MIRValue *one = builder->getInt8(1);
	mir::MIRValue *hasValue = builder->createICmpEq(tag, one, "has.value");

	builder->createCondBr(hasValue, loopBB, afterBB);

	// Loop body
	builder->setInsertPoint(loopBB);

	// Extract the wrapped value from field 1 of the optional
	// The wrapped type is the element type (what the iterator yields)
	mir::MIRType *wrappedType = optionalType->wrappedType;
	if (!wrappedType) {
		reportError("For-loop optional type must have wrappedType",
					forStmt->line, forStmt->column);
	}

	// Use optionalType for GEP since we're indexing into the Optional struct, not the wrapped type
	mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
	mir::MIRValue *currentVal = builder->createLoad(wrappedType, valuePtr, forStmt->loopVar);

	// Determine loop variable type
	mir::MIRType *loopVarType = wrappedType;
	std::string loopVarTypeStr = "int"; // Default for variableTypes map

	// Get the type string from the wrapped type for variableTypes tracking
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
		builder->createBr(condBB);
	}

	namedValues.erase(forStmt->loopVar);
	loopStack.pop_back();

	builder->setInsertPoint(afterBB);
}

void MIRCodeGenerator::generateBreak(BreakStmtAST *breakStmt, mir::MIRFunction *function) {
	if (loopStack.empty()) {
		reportError("Break statement outside of loop",
					breakStmt->line, breakStmt->column);
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
			reportError("Break target label '" + breakStmt->targetLabel +
						"' not found in enclosing loops",
						breakStmt->line, breakStmt->column);
		}
	}

	builder->createBr(targetBlock);

	// Create dead code block
	mir::MIRBasicBlock *deadBB = builder->createBasicBlock("afterbreak");
	builder->setInsertPoint(deadBB);
}

void MIRCodeGenerator::generateContinue(ContinueStmtAST *continueStmt, mir::MIRFunction *function) {
	if (loopStack.empty()) {
		reportError("Continue statement outside of loop",
					continueStmt->line, continueStmt->column);
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
			reportError("Continue target label '" + continueStmt->targetLabel +
						"' not found in enclosing loops",
						continueStmt->line, continueStmt->column);
		}
	}

	builder->createBr(targetBlock);

	mir::MIRBasicBlock *deadBB = builder->createBasicBlock("aftercontinue");
	builder->setInsertPoint(deadBB);
}

void MIRCodeGenerator::generateMatch(MatchStmtAST *matchStmt, mir::MIRFunction *function) {
	// Evaluate scrutinee once and store
	mir::MIRValue *scrutineeVal = generateExpr(matchStmt->scrutinee.get());
	if (!scrutineeVal) {
		reportError("Failed to generate match scrutinee",
					matchStmt->line, matchStmt->column);
	}

	mir::MIRValue *scrutineeAlloca = builder->createAlloca(scrutineeVal->type, "match.scrutinee");
	builder->createStore(scrutineeVal, scrutineeAlloca);

	// Create merge block (where all cases join)
	mir::MIRBasicBlock *mergeBB = builder->createBasicBlock("match.merge");

	// Create blocks for each case body
	std::vector<mir::MIRBasicBlock *> caseBodyBlocks;
	for (size_t i = 0; i < matchStmt->cases.size(); i++) {
		caseBodyBlocks.push_back(builder->createBasicBlock("match.case." + std::to_string(i)));
	}

	// Find default case index (if any)
	int defaultIndex = -1;
	for (size_t i = 0; i < matchStmt->cases.size(); i++) {
		if (matchStmt->cases[i].isDefault) {
			defaultIndex = static_cast<int>(i);
			break;
		}
	}

	// The fallback block is either default case or merge block
	mir::MIRBasicBlock *fallbackBB = (defaultIndex >= 0) ? caseBodyBlocks[defaultIndex] : mergeBB;

	// Generate pattern checks (chain of comparisons)
	for (size_t i = 0; i < matchStmt->cases.size(); i++) {
		const auto &matchCase = matchStmt->cases[i];

		// Skip default case in pattern check phase - it's the fallback
		if (matchCase.isDefault) {
			continue;
		}

		// Create check block for this case
		mir::MIRBasicBlock *checkBB = builder->createBasicBlock("match.check." + std::to_string(i));
		builder->createBr(checkBB);
		builder->setInsertPoint(checkBB);

		// Reload scrutinee
		mir::MIRValue *scrutinee = builder->createLoad(scrutineeVal->type, scrutineeAlloca, "scrutinee");

		// Generate OR'd comparison for all patterns in this case
		mir::MIRValue *cmpResult = nullptr;
		for (const auto &pattern : matchCase.patterns) {
			mir::MIRValue *patternVal = generateExpr(pattern.get());
			if (!patternVal) {
				reportError("Failed to generate match pattern",
							pattern->line, pattern->column);
			}

			// Generate comparison based on type
			mir::MIRValue *cmp = nullptr;
			if (scrutinee->type == mir::MIRType::getInt32() ||
				scrutinee->type == mir::MIRType::getInt8() ||
				scrutinee->type == mir::MIRType::getInt1()) {
				cmp = builder->createICmpEq(scrutinee, patternVal, "match.cmp");
			} else if (scrutinee->type->isStruct() && scrutinee->type->structName == "string") {
				// String comparison - call string.equals from stdlib
				mir::MIRFunction *strEquals = module->getFunction("string.equals");
				if (!strEquals) {
					reportError("string.equals function not found - ensure stdlib is imported",
						matchStmt->line, matchStmt->column);
				}
				cmp = builder->createCall(strEquals, {scrutineeAlloca, patternVal}, "match.strcmp");
			} else {
				// Default to integer comparison
				cmp = builder->createICmpEq(scrutinee, patternVal, "match.cmp");
			}

			if (cmpResult == nullptr) {
				cmpResult = cmp;
			} else {
				cmpResult = builder->createOr(cmpResult, cmp, "match.orcmp");
			}
		}

		// Determine next check block
		mir::MIRBasicBlock *nextCheckBB = fallbackBB;
		for (size_t j = i + 1; j < matchStmt->cases.size(); j++) {
			if (!matchStmt->cases[j].isDefault) {
				nextCheckBB = builder->createBasicBlock("match.check." + std::to_string(j));
				break;
			}
		}

		builder->createCondBr(cmpResult, caseBodyBlocks[i], nextCheckBB);
	}

	// Generate case bodies
	for (size_t i = 0; i < matchStmt->cases.size(); i++) {
		const auto &matchCase = matchStmt->cases[i];

		builder->setInsertPoint(caseBodyBlocks[i]);

		// Generate case statement
		if (matchCase.statement) {
			generateStmt(matchCase.statement.get(), function);
		}

		// Check if we need to branch (no terminator yet)
		if (!builder->getInsertBlock()->hasTerminator()) {
			if (matchCase.hasFallthrough && i + 1 < matchStmt->cases.size()) {
				// Fallthrough: branch to next case body
				builder->createBr(caseBodyBlocks[i + 1]);
			} else {
				// Normal case: branch to merge
				builder->createBr(mergeBB);
			}
		}
	}

	// Set insert point to merge block
	builder->setInsertPoint(mergeBB);
}

mir::MIRValue *MIRCodeGenerator::generateMatchExpr(MatchExprAST *matchExpr) {
	// Evaluate scrutinee once and store
	mir::MIRValue *scrutineeVal = generateExpr(matchExpr->scrutinee.get());
	if (!scrutineeVal) {
		reportError("Failed to generate match expression scrutinee",
					matchExpr->line, matchExpr->column);
	}

	mir::MIRValue *scrutineeAlloca = builder->createAlloca(scrutineeVal->type, "matchexpr.scrutinee");
	builder->createStore(scrutineeVal, scrutineeAlloca);

	// Determine result type from first case
	mir::MIRType *resultType = nullptr;
	if (!matchExpr->cases.empty() && matchExpr->cases[0].resultExpr) {
		mir::MIRValue *firstResult = generateExpr(matchExpr->cases[0].resultExpr.get());
		resultType = firstResult->type;
	}
	if (!resultType) {
		reportError("Match expression must have at least one case with result",
					matchExpr->line, matchExpr->column);
	}

	// Create result alloca
	mir::MIRValue *resultAlloca = builder->createAlloca(resultType, "matchexpr.result");

	// Create merge block
	mir::MIRBasicBlock *mergeBB = builder->createBasicBlock("matchexpr.merge");

	// Create blocks for each case body
	std::vector<mir::MIRBasicBlock *> caseBodyBlocks;
	for (size_t i = 0; i < matchExpr->cases.size(); i++) {
		caseBodyBlocks.push_back(builder->createBasicBlock("matchexpr.case." + std::to_string(i)));
	}

	// Find default case index (if any)
	int defaultIndex = -1;
	for (size_t i = 0; i < matchExpr->cases.size(); i++) {
		if (matchExpr->cases[i].isDefault) {
			defaultIndex = static_cast<int>(i);
			break;
		}
	}

	// The fallback block is either default case or merge block
	mir::MIRBasicBlock *fallbackBB = (defaultIndex >= 0) ? caseBodyBlocks[defaultIndex] : mergeBB;

	// Generate pattern checks (chain of comparisons)
	for (size_t i = 0; i < matchExpr->cases.size(); i++) {
		const auto &matchCase = matchExpr->cases[i];

		// Skip default case in pattern check phase
		if (matchCase.isDefault) {
			continue;
		}

		// Create check block for this case
		mir::MIRBasicBlock *checkBB = builder->createBasicBlock("matchexpr.check." + std::to_string(i));
		builder->createBr(checkBB);
		builder->setInsertPoint(checkBB);

		// Reload scrutinee
		mir::MIRValue *scrutinee = builder->createLoad(scrutineeVal->type, scrutineeAlloca, "scrutinee");

		// Generate OR'd comparison for all patterns in this case
		mir::MIRValue *cmpResult = nullptr;
		for (const auto &pattern : matchCase.patterns) {
			mir::MIRValue *patternVal = generateExpr(pattern.get());
			if (!patternVal) {
				reportError("Failed to generate match pattern",
							pattern->line, pattern->column);
			}

			// Generate comparison based on type
			mir::MIRValue *cmp = nullptr;
			if (scrutinee->type == mir::MIRType::getInt32() ||
				scrutinee->type == mir::MIRType::getInt8() ||
				scrutinee->type == mir::MIRType::getInt1()) {
				cmp = builder->createICmpEq(scrutinee, patternVal, "matchexpr.cmp");
			} else if (scrutinee->type->isStruct() && scrutinee->type->structName == "string") {
				// String comparison - call string.equals from stdlib
				mir::MIRFunction *strEquals = module->getFunction("string.equals");
				if (!strEquals) {
					reportError("string.equals function not found - ensure stdlib is imported",
						matchExpr->line, matchExpr->column);
				}
				cmp = builder->createCall(strEquals, {scrutineeAlloca, patternVal}, "matchexpr.strcmp");
			} else {
				cmp = builder->createICmpEq(scrutinee, patternVal, "matchexpr.cmp");
			}

			if (cmpResult == nullptr) {
				cmpResult = cmp;
			} else {
				cmpResult = builder->createOr(cmpResult, cmp, "matchexpr.orcmp");
			}
		}

		// Determine next check block
		mir::MIRBasicBlock *nextCheckBB = fallbackBB;
		for (size_t j = i + 1; j < matchExpr->cases.size(); j++) {
			if (!matchExpr->cases[j].isDefault) {
				nextCheckBB = builder->createBasicBlock("matchexpr.check." + std::to_string(j));
				break;
			}
		}

		builder->createCondBr(cmpResult, caseBodyBlocks[i], nextCheckBB);
	}

	// Generate case bodies
	for (size_t i = 0; i < matchExpr->cases.size(); i++) {
		const auto &matchCase = matchExpr->cases[i];

		builder->setInsertPoint(caseBodyBlocks[i]);

		// Generate result expression and store
		if (matchCase.resultExpr) {
			mir::MIRValue *caseResult = generateExpr(matchCase.resultExpr.get());
			builder->createStore(caseResult, resultAlloca);
		}

		builder->createBr(mergeBB);
	}

	// Set insert point to merge block and load result
	builder->setInsertPoint(mergeBB);
	return builder->createLoad(resultType, resultAlloca, "matchexpr.result");
}
