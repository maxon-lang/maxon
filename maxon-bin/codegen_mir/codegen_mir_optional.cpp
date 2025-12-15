/**
 * MIR Code Generator - Optional Type Generation
 *
 * This file implements code generation for optional types (T or nil).
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"

//==============================================================================
// Optional Type Helpers
//==============================================================================

/**
 * Create a nil optional value
 * Layout: [tag: i8 = 0][padding][unused value space]
 */
mir::MIRValue *MIRCodeGenerator::createNilOptional(mir::MIRType *optionalType) {
	// Allocate optional on stack
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "nil.optional");

	// Get pointer to tag (first byte) - use optionalType for correct offset calculation
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");

	// Store tag = 0 (nil)
	builder->createStore(builder->getInt8(0), tagPtr);

	// Load and return the optional value
	return builder->createLoad(optionalType, optionalAlloca, "nil.optional.val");
}

/**
 * Create a some optional value (wrapping a non-nil value)
 * Layout: [tag: i8 = 1][padding][value: T]
 */
mir::MIRValue *MIRCodeGenerator::createSomeOptional(mir::MIRType *optionalType, mir::MIRValue *value) {
	// Allocate optional on stack
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "some.optional");

	// Get pointer to tag (first byte) - use optionalType for correct offset calculation
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");

	// Store tag = 1 (some/has value)
	builder->createStore(builder->getInt8(1), tagPtr);

	// Get pointer to value (field 1, after tag and padding) - use optionalType for correct offset
	mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");

	// Store the value
	builder->createStore(value, valuePtr);

	// Load and return the optional value
	return builder->createLoad(optionalType, optionalAlloca, "some.optional.val");
}

//==============================================================================
// If-Let Statement Generation
//==============================================================================

void MIRCodeGenerator::generateIfLet(IfLetStmtAST *ifLet, mir::MIRFunction *function) {
	// Evaluate optional expression
	mir::MIRValue *optionalValue = generateExpr(ifLet->optionalExpr.get());

	// Get the optional type from the expression
	std::string optionalTypeStr = getExpressionMaxonType(ifLet->optionalExpr.get());
	mir::MIRType *optionalType = getTypeFromString(optionalTypeStr);

	// Get the unwrapped type using centralized type conversion
	std::string unwrappedTypeStr = maxon::TypeConversion::unwrapOptionalType(optionalTypeStr);
	mir::MIRType *unwrappedType = getTypeFromString(unwrappedTypeStr);

	// Store optional to stack
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "iflet.optional");
	builder->createStore(optionalValue, optionalAlloca);

	// Load tag (first byte) - use optionalType for correct offset calculation
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
	mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");

	// Compare tag == 1 (has value)
	mir::MIRValue *hasValue = builder->createICmpEq(tag, builder->getInt8(1), "has.value");

	// Create basic blocks
	mir::MIRBasicBlock *thenBlock = builder->createBasicBlock("iflet.then");
	mir::MIRBasicBlock *elseBlock = nullptr;
	mir::MIRBasicBlock *afterBlock = builder->createBasicBlock("iflet.after");

	if (!ifLet->elseBody.empty()) {
		elseBlock = builder->createBasicBlock("iflet.else");
		builder->createCondBr(hasValue, thenBlock, elseBlock);
	} else {
		builder->createCondBr(hasValue, thenBlock, afterBlock);
	}

	// Then block: extract value and bind to variable
	builder->setInsertPoint(thenBlock);

	// Extract value from optional (field 1, after tag) - use optionalType for correct offset
	mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
	mir::MIRValue *unwrappedValue = builder->createLoad(unwrappedType, valuePtr, "unwrapped.val");

	// Create alloca for the bound variable
	mir::MIRValue *bindingAlloca = builder->createAlloca(unwrappedType, ifLet->bindingName);
	builder->createStore(unwrappedValue, bindingAlloca);

	// Register the binding in scope
	namedValues[ifLet->bindingName] = bindingAlloca;
	variableTypes[ifLet->bindingName] = unwrappedTypeStr;

	// Generate then body
	pushScope();
	for (const auto &s : ifLet->thenBody) {
		generateStmt(s.get(), function);
	}
	popScope(function);

	// Branch to after block (if not already terminated)
	mir::MIRBasicBlock *thenEndBB = builder->getInsertBlock();
	if (!thenEndBB->hasTerminator()) {
		builder->createBr(afterBlock);
	}

	// Remove binding from scope
	namedValues.erase(ifLet->bindingName);
	variableTypes.erase(ifLet->bindingName);

	// Else block (if present)
	if (elseBlock) {
		builder->setInsertPoint(elseBlock);

		pushScope();
		for (const auto &s : ifLet->elseBody) {
			generateStmt(s.get(), function);
		}
		popScope(function);

		mir::MIRBasicBlock *elseEndBB = builder->getInsertBlock();
		if (!elseEndBB->hasTerminator()) {
			builder->createBr(afterBlock);
		}
	}

	// Continue after if-let
	builder->setInsertPoint(afterBlock);
}

//==============================================================================
// Else-Unwrap Statement Generation
//==============================================================================

void MIRCodeGenerator::generateElseUnwrap(ElseUnwrapStmtAST *elseUnwrap, mir::MIRFunction *function) {
	// Evaluate optional expression
	mir::MIRValue *optionalValue = generateExpr(elseUnwrap->optionalExpr.get());

	// Get the optional type
	std::string optionalTypeStr = getExpressionMaxonType(elseUnwrap->optionalExpr.get());
	mir::MIRType *optionalType = getTypeFromString(optionalTypeStr);

	// Get the unwrapped type using centralized type conversion
	std::string unwrappedTypeStr = maxon::TypeConversion::unwrapOptionalType(optionalTypeStr);
	mir::MIRType *unwrappedType = getTypeFromString(unwrappedTypeStr);

	// Allocate the result variable (unwrapped type)
	mir::MIRValue *resultAlloca = builder->createAlloca(unwrappedType, elseUnwrap->name);

	// Register the variable for else block to assign to
	namedValues[elseUnwrap->name] = resultAlloca;
	variableTypes[elseUnwrap->name] = unwrappedTypeStr;

	// Store optional to stack
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "unwrap.optional");
	builder->createStore(optionalValue, optionalAlloca);

	// Load tag (first byte) - use optionalType for correct offset calculation
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
	mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");

	// Compare tag == 1 (has value)
	mir::MIRValue *hasValue = builder->createICmpEq(tag, builder->getInt8(1), "has.value");

	// Create basic blocks
	mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("unwrap.hasvalue");
	mir::MIRBasicBlock *elseBlock = builder->createBasicBlock("unwrap.else");
	mir::MIRBasicBlock *afterBlock = builder->createBasicBlock("unwrap.after");

	builder->createCondBr(hasValue, hasValueBlock, elseBlock);

	// Has-value block: extract and store to result variable
	builder->setInsertPoint(hasValueBlock);
	// Extract value from optional (field 1, after tag) - use optionalType for correct offset
	mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
	mir::MIRValue *unwrappedValue = builder->createLoad(unwrappedType, valuePtr, "unwrapped.val");
	builder->createStore(unwrappedValue, resultAlloca);
	builder->createBr(afterBlock);

	// Else block: execute user code (must assign to variable)
	builder->setInsertPoint(elseBlock);
	pushScope();
	for (const auto &s : elseUnwrap->elseBody) {
		generateStmt(s.get(), function);
	}
	popScope(function);

	// Branch to after (if not already terminated)
	mir::MIRBasicBlock *elseEndBB = builder->getInsertBlock();
	if (!elseEndBB->hasTerminator()) {
		builder->createBr(afterBlock);
	}

	// Continue - result variable is now initialized on all paths
	builder->setInsertPoint(afterBlock);
}

//==============================================================================
// Guard-Let Statement Generation
//==============================================================================

void MIRCodeGenerator::generateGuardLet(GuardLetStmtAST *guardLet, mir::MIRFunction *function) {
	// Evaluate optional expression
	mir::MIRValue *optionalValue = generateExpr(guardLet->optionalExpr.get());

	// Get the optional type
	std::string optionalTypeStr = getExpressionMaxonType(guardLet->optionalExpr.get());
	mir::MIRType *optionalType = getTypeFromString(optionalTypeStr);

	// Get the unwrapped type using centralized type conversion
	std::string unwrappedTypeStr = maxon::TypeConversion::unwrapOptionalType(optionalTypeStr);
	mir::MIRType *unwrappedType = getTypeFromString(unwrappedTypeStr);

	// Allocate the result variable (unwrapped type)
	mir::MIRValue *resultAlloca = builder->createAlloca(unwrappedType, guardLet->name);

	// Register the variable in scope (needed for guard body in case it references it)
	namedValues[guardLet->name] = resultAlloca;
	variableTypes[guardLet->name] = unwrappedTypeStr;

	// Store optional to stack
	mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "guard.optional");
	builder->createStore(optionalValue, optionalAlloca);

	// Load tag (first byte) - use optionalType for correct offset calculation
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
	mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");

	// Compare tag == 1 (has value)
	mir::MIRValue *hasValue = builder->createICmpEq(tag, builder->getInt8(1), "has.value");

	// Create basic blocks
	mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("guard.hasvalue");
	mir::MIRBasicBlock *guardBlock = builder->createBasicBlock("guard.nil");
	mir::MIRBasicBlock *afterBlock = builder->createBasicBlock("guard.after");

	builder->createCondBr(hasValue, hasValueBlock, guardBlock);

	// Has-value block: extract and store to result variable, then continue
	builder->setInsertPoint(hasValueBlock);
	mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
	mir::MIRValue *unwrappedValue = builder->createLoad(unwrappedType, valuePtr, "unwrapped.val");

	// For struct types, the optional stores a pointer to the struct.
	// We need to load the actual struct data from that pointer.
	if (structTypes.find(unwrappedTypeStr) != structTypes.end()) {
		// unwrappedValue is a pointer to the struct - load the struct data
		mir::MIRValue *structData = builder->createLoad(unwrappedType, unwrappedValue, "struct.data");
		builder->createStore(structData, resultAlloca);
	} else {
		builder->createStore(unwrappedValue, resultAlloca);
	}
	builder->createBr(afterBlock);

	// Guard block: nil case - execute guard body (must exit scope)
	builder->setInsertPoint(guardBlock);
	pushScope();
	for (const auto &s : guardLet->guardBody) {
		generateStmt(s.get(), function);
	}
	popScope(function);

	// Note: Guard body MUST exit scope (return/break/continue)
	// Semantic analysis already enforced this
	// But if somehow we get here, branch to after block (unreachable in practice)
	mir::MIRBasicBlock *guardEndBB = builder->getInsertBlock();
	if (!guardEndBB->hasTerminator()) {
		builder->createBr(afterBlock);
	}

	// Continue after guard-let - variable is now bound
	builder->setInsertPoint(afterBlock);
}
