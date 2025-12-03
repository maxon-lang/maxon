/**
 * MIR Code Generator - Map Operations
 *
 * This file implements hash map code generation for MIR.
 * Includes: isMapMethodCall, generateMapMethod, contains, get, insert, remove
 */

#include "../codegen_mir.h"
#include <stdexcept>

//==============================================================================
// Map Method Detection and Dispatch
//==============================================================================

bool MIRCodeGenerator::isMapMethodCall(const std::string &callee) {
	// Map method calls look like "map<K,V>.methodName"
	if (callee.substr(0, 4) != "map<") {
		return false;
	}
	// Check it has a method suffix after the type
	size_t dotPos = callee.rfind('.');
	if (dotPos == std::string::npos || dotPos < 5) {
		return false;
	}
	// Verify the type ends with '>' before the dot
	if (callee[dotPos - 1] != '>') {
		return false;
	}
	return true;
}

mir::MIRValue *MIRCodeGenerator::generateMapMethod(CallExprAST *callExpr) {
	// Parse the callee: "map<K,V>.methodName"
	// Use resolvedCallee if available
	const std::string &callee = callExpr->resolvedCallee.empty() ? callExpr->callee : callExpr->resolvedCallee;
	size_t dotPos = callee.rfind('.');
	std::string mapType = callee.substr(0, dotPos);
	std::string methodName = callee.substr(dotPos + 1);

	// Extract key and value types from "map<K,V>"
	size_t openBracket = mapType.find('<');
	size_t comma = mapType.find(',', openBracket);
	size_t closeBracket = mapType.rfind('>');
	std::string keyTypeStr = mapType.substr(openBracket + 1, comma - openBracket - 1);
	std::string valueTypeStr = mapType.substr(comma + 1, closeBracket - comma - 1);

	mir::MIRType *keyType = getTypeFromString(keyTypeStr);
	mir::MIRType *valueType = getTypeFromString(valueTypeStr);

	// Get the map struct type
	std::string mapStructName = "__map_" + keyTypeStr + "_" + valueTypeStr;
	mir::MIRType *mapStructType = structTypes[mapStructName];
	if (!mapStructType) {
		throw std::runtime_error("Map struct type not found: " + mapStructName);
	}

	// Get the map struct pointer from the first argument
	if (callExpr->args.empty()) {
		throw std::runtime_error("Map method requires at least the map argument (self)");
	}

	auto *mapVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
	if (!mapVar) {
		throw std::runtime_error("Map method first argument must be a map variable");
	}

	std::string mapVarName = mapVar->name;
	mir::MIRValue *mapAlloca = namedValues[mapVarName];
	if (!mapAlloca) {
		throw std::runtime_error("Map variable not found: " + mapVarName);
	}

	// Map struct layout (from codegen_mir_stmt.cpp):
	// Field 0: _keys (ptr to key array)
	// Field 1: _values (ptr to value array)
	// Field 2: _states (ptr to byte array)
	// Field 3: _count (i32)
	// Field 4: _capacity (i32)

	if (methodName == "count") {
		// Return the _count field
		mir::MIRValue *countPtr = builder->createStructGEP(mapStructType, mapAlloca, 3, "count.ptr");
		return builder->createLoad(mir::MIRType::getInt32(), countPtr, "count");
	}

	if (methodName == "capacity") {
		// Return the _capacity field
		mir::MIRValue *capacityPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "capacity.ptr");
		return builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");
	}

	if (methodName == "contains") {
		if (callExpr->args.size() < 2) {
			throw std::runtime_error("contains() requires key argument");
		}
		mir::MIRValue *key = generateExpr(callExpr->args[1].get());
		return generateMapContains(mapAlloca, key, keyType, valueType, keyTypeStr, mapStructType);
	}

	if (methodName == "get") {
		if (callExpr->args.size() < 2) {
			throw std::runtime_error("get() requires key argument");
		}
		mir::MIRValue *key = generateExpr(callExpr->args[1].get());
		return generateMapGet(mapAlloca, key, keyType, valueType, keyTypeStr, mapStructType);
	}

	if (methodName == "insert") {
		if (callExpr->args.size() < 3) {
			throw std::runtime_error("insert() requires key and value arguments");
		}
		mir::MIRValue *key = generateExpr(callExpr->args[1].get());
		mir::MIRValue *value = generateExpr(callExpr->args[2].get());
		return generateMapInsert(mapAlloca, key, value, keyType, valueType, keyTypeStr, valueTypeStr, mapStructType);
	}

	if (methodName == "remove") {
		if (callExpr->args.size() < 2) {
			throw std::runtime_error("remove() requires key argument");
		}
		mir::MIRValue *key = generateExpr(callExpr->args[1].get());
		return generateMapRemove(mapAlloca, key, keyType, valueType, keyTypeStr, mapStructType);
	}

	throw std::runtime_error("Unknown map method: " + methodName);
}

//==============================================================================
// Hash Function Generation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateHashForKey(mir::MIRValue *key, const std::string &keyTypeStr) {
	// Generate hash value based on key type
	// For built-in types, use simple hash functions
	if (keyTypeStr == "int") {
		// Simple int hash: multiply by a prime and XOR with shifted value
		// hash = key * 2654435761 (Knuth's multiplicative hash)
		// Use signed constant to avoid overflow warning
		mir::MIRValue *prime = builder->getInt32(static_cast<int32_t>(0x9E3779B1));
		return builder->createMul(key, prime, "hash");
	} else if (keyTypeStr == "byte" || keyTypeStr == "character") {
		// Extend to int32 and hash
		mir::MIRValue *extended = builder->createSExt(key, mir::MIRType::getInt32(), "key.ext");
		mir::MIRValue *prime = builder->getInt32(static_cast<int32_t>(0x9E3779B1));
		return builder->createMul(extended, prime, "hash");
	} else if (keyTypeStr == "string") {
		// For strings, we need to call a hash function
		// Use a simple DJB2-style hash for now
		// This needs to call into the string's internal hash method
		// For now, use the string length as a simple hash (will be improved)
		mir::MIRType *stringType = getOrCreateManagedStringType();
		mir::MIRValue *lenPtr = builder->createStructGEP(stringType, key, 1, "str.len.ptr");
		mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "str.len");
		return len; // Simple placeholder - should be proper string hash
	}

	// Default: just return the value as-is (for types with custom hash)
	return key;
}

//==============================================================================
// Map Contains Operation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMapContains(mir::MIRValue *mapAlloca, mir::MIRValue *key,
													 mir::MIRType *keyType, mir::MIRType *valueType,
													 const std::string &keyTypeStr, mir::MIRType *mapStructType) {
	// Get current function for basic block creation
	mir::MIRFunction *function = builder->getFunction();

	// Load map fields
	mir::MIRValue *keysPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "keys.ptr.ptr");
	mir::MIRValue *keys = builder->createLoad(mir::MIRType::getPtr(), keysPtr, "keys");
	mir::MIRValue *statesPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "states.ptr.ptr");
	mir::MIRValue *states = builder->createLoad(mir::MIRType::getPtr(), statesPtr, "states");
	mir::MIRValue *capacityPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "capacity.ptr");
	mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");

	// Compute initial index: hash % capacity
	mir::MIRValue *hash = generateHashForKey(key, keyTypeStr);
	mir::MIRValue *index = builder->createSRem(hash, capacity, "index");

	// Handle negative index using conditional branches (no select instruction)
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *isNeg = builder->createICmpSLT(index, zero, "is.neg");
	mir::MIRValue *fixedIndex = builder->createAdd(index, capacity, "fixed.idx");

	// Use alloca to handle the select logic
	mir::MIRValue *indexFixAlloca = builder->createAlloca(mir::MIRType::getInt32(), "index.fix.alloca");
	builder->createStore(index, indexFixAlloca);

	mir::MIRBasicBlock *fixNegBlock = function->createBasicBlock("fix.neg");
	mir::MIRBasicBlock *afterFixBlock = function->createBasicBlock("after.fix");
	builder->createCondBr(isNeg, fixNegBlock, afterFixBlock);

	builder->setInsertPoint(fixNegBlock);
	builder->createStore(fixedIndex, indexFixAlloca);
	builder->createBr(afterFixBlock);

	builder->setInsertPoint(afterFixBlock);
	index = builder->createLoad(mir::MIRType::getInt32(), indexFixAlloca, "abs.index");

	// Create result alloca for the return value
	mir::MIRValue *resultAlloca = builder->createAlloca(mir::MIRType::getInt1(), "contains.result");
	builder->createStore(builder->getInt1(false), resultAlloca);

	// Create index alloca for the probe loop
	mir::MIRValue *indexAlloca = builder->createAlloca(mir::MIRType::getInt32(), "index.alloca");
	builder->createStore(index, indexAlloca);

	// Create probes counter alloca
	mir::MIRValue *probesAlloca = builder->createAlloca(mir::MIRType::getInt32(), "probes.alloca");
	builder->createStore(zero, probesAlloca);

	// Create basic blocks for the probe loop
	mir::MIRBasicBlock *loopCond = function->createBasicBlock("contains.loop.cond");
	mir::MIRBasicBlock *loopBody = function->createBasicBlock("contains.loop.body");
	mir::MIRBasicBlock *foundBlock = function->createBasicBlock("contains.found");
	mir::MIRBasicBlock *notFoundBlock = function->createBasicBlock("contains.not.found");
	mir::MIRBasicBlock *nextProbe = function->createBasicBlock("contains.next");
	mir::MIRBasicBlock *exitBlock = function->createBasicBlock("contains.exit");

	// Jump to loop condition
	builder->createBr(loopCond);

	// Loop condition: probes < capacity
	builder->setInsertPoint(loopCond);
	mir::MIRValue *probes = builder->createLoad(mir::MIRType::getInt32(), probesAlloca, "probes");
	mir::MIRValue *continueLoop = builder->createICmpSLT(probes, capacity, "continue.loop");
	builder->createCondBr(continueLoop, loopBody, notFoundBlock);

	// Loop body: check state and key
	builder->setInsertPoint(loopBody);
	mir::MIRValue *currentIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "curr.idx");
	mir::MIRValue *idx64 = builder->createSExt(currentIndex, mir::MIRType::getInt64(), "idx64");

	// Load state
	mir::MIRValue *statePtr = builder->createArrayGEP(mir::MIRType::getInt8(), states, idx64, "state.ptr");
	mir::MIRValue *state = builder->createLoad(mir::MIRType::getInt8(), statePtr, "state");
	mir::MIRValue *stateInt = builder->createZExt(state, mir::MIRType::getInt32(), "state.int");

	// Check if empty (state == 0) -> not found
	mir::MIRValue *isEmpty = builder->createICmpEq(stateInt, zero, "is.empty");

	mir::MIRBasicBlock *checkOccupied = function->createBasicBlock("check.occupied");
	builder->createCondBr(isEmpty, notFoundBlock, checkOccupied);

	// Check if occupied (state == 1) -> compare keys
	builder->setInsertPoint(checkOccupied);
	mir::MIRValue *one = builder->getInt32(1);
	mir::MIRValue *isOccupied = builder->createICmpEq(stateInt, one, "is.occupied");

	mir::MIRBasicBlock *compareKey = function->createBasicBlock("compare.key");
	builder->createCondBr(isOccupied, compareKey, nextProbe);

	// Compare keys
	builder->setInsertPoint(compareKey);
	mir::MIRValue *keyPtr = builder->createArrayGEP(keyType, keys, idx64, "key.ptr");
	mir::MIRValue *storedKey = builder->createLoad(keyType, keyPtr, "stored.key");

	// Generate key comparison based on type
	mir::MIRValue *keysEqual;
	if (keyTypeStr == "int" || keyTypeStr == "byte" || keyTypeStr == "character") {
		keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	} else if (keyTypeStr == "string") {
		// For strings, need to compare contents - simplified for now
		// TODO: proper string comparison
		keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	} else {
		keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	}

	builder->createCondBr(keysEqual, foundBlock, nextProbe);

	// Found block: set result to true
	builder->setInsertPoint(foundBlock);
	builder->createStore(builder->getInt1(true), resultAlloca);
	builder->createBr(exitBlock);

	// Not found block: set result to false
	builder->setInsertPoint(notFoundBlock);
	builder->createStore(builder->getInt1(false), resultAlloca);
	builder->createBr(exitBlock);

	// Next probe: increment index and probes
	builder->setInsertPoint(nextProbe);
	mir::MIRValue *nextIndex = builder->createAdd(currentIndex, one, "next.idx");
	mir::MIRValue *wrappedIndex = builder->createSRem(nextIndex, capacity, "wrapped.idx");
	builder->createStore(wrappedIndex, indexAlloca);

	mir::MIRValue *nextProbes = builder->createAdd(probes, one, "next.probes");
	builder->createStore(nextProbes, probesAlloca);
	builder->createBr(loopCond);

	// Exit block: return result
	builder->setInsertPoint(exitBlock);
	return builder->createLoad(mir::MIRType::getInt1(), resultAlloca, "contains.result");
}

//==============================================================================
// Map Get Operation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMapGet(mir::MIRValue *mapAlloca, mir::MIRValue *key,
												mir::MIRType *keyType, mir::MIRType *valueType,
												const std::string &keyTypeStr, mir::MIRType *mapStructType) {
	// Similar to contains, but return the value instead of bool
	mir::MIRFunction *function = builder->getFunction();

	// Load map fields
	mir::MIRValue *keysPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "keys.ptr.ptr");
	mir::MIRValue *keys = builder->createLoad(mir::MIRType::getPtr(), keysPtr, "keys");
	mir::MIRValue *valuesPtr = builder->createStructGEP(mapStructType, mapAlloca, 1, "values.ptr.ptr");
	mir::MIRValue *values = builder->createLoad(mir::MIRType::getPtr(), valuesPtr, "values");
	mir::MIRValue *statesPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "states.ptr.ptr");
	mir::MIRValue *states = builder->createLoad(mir::MIRType::getPtr(), statesPtr, "states");
	mir::MIRValue *capacityPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "capacity.ptr");
	mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");

	// Compute initial index
	mir::MIRValue *hash = generateHashForKey(key, keyTypeStr);
	mir::MIRValue *index = builder->createSRem(hash, capacity, "index");
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *isNeg = builder->createICmpSLT(index, zero, "is.neg");
	mir::MIRValue *fixedIndex = builder->createAdd(index, capacity, "fixed.idx");

	// Use alloca to handle the select logic (no select instruction available)
	mir::MIRValue *indexFixAlloca = builder->createAlloca(mir::MIRType::getInt32(), "get.index.fix");
	builder->createStore(index, indexFixAlloca);

	mir::MIRBasicBlock *fixNegBlock = function->createBasicBlock("get.fix.neg");
	mir::MIRBasicBlock *afterFixBlock = function->createBasicBlock("get.after.fix");
	builder->createCondBr(isNeg, fixNegBlock, afterFixBlock);

	builder->setInsertPoint(fixNegBlock);
	builder->createStore(fixedIndex, indexFixAlloca);
	builder->createBr(afterFixBlock);

	builder->setInsertPoint(afterFixBlock);
	index = builder->createLoad(mir::MIRType::getInt32(), indexFixAlloca, "abs.index");

	// Create result alloca
	mir::MIRValue *resultAlloca = builder->createAlloca(valueType, "get.result");
	// Initialize with default value (zero)
	if (valueType == mir::MIRType::getInt32()) {
		builder->createStore(builder->getInt32(0), resultAlloca);
	} else if (valueType == mir::MIRType::getInt1()) {
		builder->createStore(builder->getInt1(false), resultAlloca);
	}

	// Create allocas for loop variables
	mir::MIRValue *indexAlloca = builder->createAlloca(mir::MIRType::getInt32(), "index.alloca");
	builder->createStore(index, indexAlloca);
	mir::MIRValue *probesAlloca = builder->createAlloca(mir::MIRType::getInt32(), "probes.alloca");
	builder->createStore(zero, probesAlloca);

	// Create basic blocks
	mir::MIRBasicBlock *loopCond = function->createBasicBlock("get.loop.cond");
	mir::MIRBasicBlock *loopBody = function->createBasicBlock("get.loop.body");
	mir::MIRBasicBlock *foundBlock = function->createBasicBlock("get.found");
	mir::MIRBasicBlock *nextProbe = function->createBasicBlock("get.next");
	mir::MIRBasicBlock *exitBlock = function->createBasicBlock("get.exit");

	builder->createBr(loopCond);

	// Loop condition
	builder->setInsertPoint(loopCond);
	mir::MIRValue *probes = builder->createLoad(mir::MIRType::getInt32(), probesAlloca, "probes");
	mir::MIRValue *continueLoop = builder->createICmpSLT(probes, capacity, "continue.loop");
	builder->createCondBr(continueLoop, loopBody, exitBlock);

	// Loop body
	builder->setInsertPoint(loopBody);
	mir::MIRValue *currentIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "curr.idx");
	mir::MIRValue *idx64 = builder->createSExt(currentIndex, mir::MIRType::getInt64(), "idx64");

	mir::MIRValue *statePtr = builder->createArrayGEP(mir::MIRType::getInt8(), states, idx64, "state.ptr");
	mir::MIRValue *state = builder->createLoad(mir::MIRType::getInt8(), statePtr, "state");
	mir::MIRValue *stateInt = builder->createZExt(state, mir::MIRType::getInt32(), "state.int");

	// Empty -> exit with default
	mir::MIRValue *isEmpty = builder->createICmpEq(stateInt, zero, "is.empty");
	mir::MIRBasicBlock *checkOccupied = function->createBasicBlock("get.check.occupied");
	builder->createCondBr(isEmpty, exitBlock, checkOccupied);

	// Check occupied
	builder->setInsertPoint(checkOccupied);
	mir::MIRValue *one = builder->getInt32(1);
	mir::MIRValue *isOccupied = builder->createICmpEq(stateInt, one, "is.occupied");
	mir::MIRBasicBlock *compareKey = function->createBasicBlock("get.compare.key");
	builder->createCondBr(isOccupied, compareKey, nextProbe);

	// Compare keys
	builder->setInsertPoint(compareKey);
	mir::MIRValue *keyPtr = builder->createArrayGEP(keyType, keys, idx64, "key.ptr");
	mir::MIRValue *storedKey = builder->createLoad(keyType, keyPtr, "stored.key");
	mir::MIRValue *keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	builder->createCondBr(keysEqual, foundBlock, nextProbe);

	// Found: store value
	builder->setInsertPoint(foundBlock);
	mir::MIRValue *valuePtr = builder->createArrayGEP(valueType, values, idx64, "value.ptr");
	mir::MIRValue *foundValue = builder->createLoad(valueType, valuePtr, "found.value");
	builder->createStore(foundValue, resultAlloca);
	builder->createBr(exitBlock);

	// Next probe
	builder->setInsertPoint(nextProbe);
	mir::MIRValue *nextIndex = builder->createAdd(currentIndex, one, "next.idx");
	mir::MIRValue *wrappedIndex = builder->createSRem(nextIndex, capacity, "wrapped.idx");
	builder->createStore(wrappedIndex, indexAlloca);
	mir::MIRValue *nextProbes = builder->createAdd(probes, one, "next.probes");
	builder->createStore(nextProbes, probesAlloca);
	builder->createBr(loopCond);

	// Exit
	builder->setInsertPoint(exitBlock);
	return builder->createLoad(valueType, resultAlloca, "get.result");
}

//==============================================================================
// Map Insert Operation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMapInsert(mir::MIRValue *mapAlloca, mir::MIRValue *key,
												   mir::MIRValue *value, mir::MIRType *keyType,
												   mir::MIRType *valueType, const std::string &keyTypeStr,
												   const std::string &valueTypeStr, mir::MIRType *mapStructType) {
	mir::MIRFunction *function = builder->getFunction();

	// Load map fields
	mir::MIRValue *keysPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "keys.ptr.ptr");
	mir::MIRValue *keys = builder->createLoad(mir::MIRType::getPtr(), keysPtr, "keys");
	mir::MIRValue *valuesPtr = builder->createStructGEP(mapStructType, mapAlloca, 1, "values.ptr.ptr");
	mir::MIRValue *values = builder->createLoad(mir::MIRType::getPtr(), valuesPtr, "values");
	mir::MIRValue *statesPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "states.ptr.ptr");
	mir::MIRValue *states = builder->createLoad(mir::MIRType::getPtr(), statesPtr, "states");
	mir::MIRValue *countPtr = builder->createStructGEP(mapStructType, mapAlloca, 3, "count.ptr");
	mir::MIRValue *count = builder->createLoad(mir::MIRType::getInt32(), countPtr, "count");
	mir::MIRValue *capacityPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "capacity.ptr");
	mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");

	// TODO: Add grow/resize logic when load factor exceeds 75%
	// For now, assume capacity is sufficient

	// Compute initial index
	mir::MIRValue *hash = generateHashForKey(key, keyTypeStr);
	mir::MIRValue *index = builder->createSRem(hash, capacity, "index");
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *isNeg = builder->createICmpSLT(index, zero, "is.neg");
	mir::MIRValue *fixedIndex = builder->createAdd(index, capacity, "fixed.idx");

	// Use alloca to handle the select logic
	mir::MIRValue *indexFixAlloca = builder->createAlloca(mir::MIRType::getInt32(), "insert.index.fix");
	builder->createStore(index, indexFixAlloca);

	mir::MIRBasicBlock *fixNegBlock = function->createBasicBlock("insert.fix.neg");
	mir::MIRBasicBlock *afterFixBlock = function->createBasicBlock("insert.after.fix");
	builder->createCondBr(isNeg, fixNegBlock, afterFixBlock);

	builder->setInsertPoint(fixNegBlock);
	builder->createStore(fixedIndex, indexFixAlloca);
	builder->createBr(afterFixBlock);

	builder->setInsertPoint(afterFixBlock);
	index = builder->createLoad(mir::MIRType::getInt32(), indexFixAlloca, "abs.index");

	// Create allocas
	mir::MIRValue *indexAlloca = builder->createAlloca(mir::MIRType::getInt32(), "index.alloca");
	builder->createStore(index, indexAlloca);
	mir::MIRValue *probesAlloca = builder->createAlloca(mir::MIRType::getInt32(), "probes.alloca");
	builder->createStore(zero, probesAlloca);
	mir::MIRValue *firstDeletedAlloca = builder->createAlloca(mir::MIRType::getInt32(), "first.deleted.alloca");
	mir::MIRValue *negOne = builder->getInt32(-1);
	builder->createStore(negOne, firstDeletedAlloca);

	// Create basic blocks
	mir::MIRBasicBlock *loopCond = function->createBasicBlock("insert.loop.cond");
	mir::MIRBasicBlock *loopBody = function->createBasicBlock("insert.loop.body");
	mir::MIRBasicBlock *insertHere = function->createBasicBlock("insert.here");
	mir::MIRBasicBlock *updateExisting = function->createBasicBlock("insert.update");
	mir::MIRBasicBlock *nextProbe = function->createBasicBlock("insert.next");
	mir::MIRBasicBlock *exitBlock = function->createBasicBlock("insert.exit");

	builder->createBr(loopCond);

	// Loop condition
	builder->setInsertPoint(loopCond);
	mir::MIRValue *probes = builder->createLoad(mir::MIRType::getInt32(), probesAlloca, "probes");
	mir::MIRValue *continueLoop = builder->createICmpSLT(probes, capacity, "continue.loop");
	builder->createCondBr(continueLoop, loopBody, exitBlock);

	// Loop body
	builder->setInsertPoint(loopBody);
	mir::MIRValue *currentIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "curr.idx");
	mir::MIRValue *idx64 = builder->createSExt(currentIndex, mir::MIRType::getInt64(), "idx64");

	mir::MIRValue *statePtr = builder->createArrayGEP(mir::MIRType::getInt8(), states, idx64, "state.ptr");
	mir::MIRValue *state = builder->createLoad(mir::MIRType::getInt8(), statePtr, "state");
	mir::MIRValue *stateInt = builder->createZExt(state, mir::MIRType::getInt32(), "state.int");

	// Check state == 0 (empty) -> insert here
	mir::MIRValue *isEmpty = builder->createICmpEq(stateInt, zero, "is.empty");
	mir::MIRBasicBlock *checkDeleted = function->createBasicBlock("insert.check.deleted");
	builder->createCondBr(isEmpty, insertHere, checkDeleted);

	// Check deleted (state == 2)
	builder->setInsertPoint(checkDeleted);
	mir::MIRValue *two = builder->getInt32(2);
	mir::MIRValue *isDeleted = builder->createICmpEq(stateInt, two, "is.deleted");
	mir::MIRBasicBlock *markDeleted = function->createBasicBlock("insert.mark.deleted");
	mir::MIRBasicBlock *checkOccupied = function->createBasicBlock("insert.check.occupied");
	builder->createCondBr(isDeleted, markDeleted, checkOccupied);

	// Mark first deleted slot
	builder->setInsertPoint(markDeleted);
	mir::MIRValue *firstDeleted = builder->createLoad(mir::MIRType::getInt32(), firstDeletedAlloca, "first.deleted");
	mir::MIRValue *noDeletedYet = builder->createICmpSLT(firstDeleted, zero, "no.deleted.yet");
	mir::MIRBasicBlock *saveDeleted = function->createBasicBlock("insert.save.deleted");
	mir::MIRBasicBlock *afterMarkDeleted = function->createBasicBlock("insert.after.mark.deleted");
	builder->createCondBr(noDeletedYet, saveDeleted, afterMarkDeleted);

	builder->setInsertPoint(saveDeleted);
	builder->createStore(currentIndex, firstDeletedAlloca);
	builder->createBr(afterMarkDeleted);

	builder->setInsertPoint(afterMarkDeleted);
	builder->createBr(checkOccupied);

	// Check occupied (state == 1) -> compare keys
	builder->setInsertPoint(checkOccupied);
	mir::MIRValue *one = builder->getInt32(1);
	mir::MIRValue *isOccupied = builder->createICmpEq(stateInt, one, "is.occupied");
	mir::MIRBasicBlock *compareKey = function->createBasicBlock("insert.compare.key");
	builder->createCondBr(isOccupied, compareKey, nextProbe);

	// Compare keys
	builder->setInsertPoint(compareKey);
	mir::MIRValue *keyPtr = builder->createArrayGEP(keyType, keys, idx64, "key.ptr");
	mir::MIRValue *storedKey = builder->createLoad(keyType, keyPtr, "stored.key");
	mir::MIRValue *keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	builder->createCondBr(keysEqual, updateExisting, nextProbe);

	// Insert at current or first deleted slot
	builder->setInsertPoint(insertHere);
	mir::MIRValue *firstDeletedForInsert = builder->createLoad(mir::MIRType::getInt32(), firstDeletedAlloca, "fd.for.insert");
	mir::MIRValue *useDeleted = builder->createICmpSGE(firstDeletedForInsert, zero, "use.deleted");

	// Use alloca to handle the select logic for insert index
	mir::MIRValue *insertIdxAlloca = builder->createAlloca(mir::MIRType::getInt32(), "insert.idx.alloca");
	builder->createStore(currentIndex, insertIdxAlloca);

	mir::MIRBasicBlock *useDeletedBlock = function->createBasicBlock("insert.use.deleted");
	mir::MIRBasicBlock *afterSelectBlock = function->createBasicBlock("insert.after.select");
	builder->createCondBr(useDeleted, useDeletedBlock, afterSelectBlock);

	builder->setInsertPoint(useDeletedBlock);
	builder->createStore(firstDeletedForInsert, insertIdxAlloca);
	builder->createBr(afterSelectBlock);

	builder->setInsertPoint(afterSelectBlock);
	mir::MIRValue *insertIdx = builder->createLoad(mir::MIRType::getInt32(), insertIdxAlloca, "insert.idx");
	mir::MIRValue *insertIdx64 = builder->createSExt(insertIdx, mir::MIRType::getInt64(), "insert.idx64");

	// Store key, value, state
	mir::MIRValue *insertKeyPtr = builder->createArrayGEP(keyType, keys, insertIdx64, "insert.key.ptr");
	builder->createStore(key, insertKeyPtr);
	mir::MIRValue *insertValuePtr = builder->createArrayGEP(valueType, values, insertIdx64, "insert.value.ptr");
	builder->createStore(value, insertValuePtr);
	mir::MIRValue *insertStatePtr = builder->createArrayGEP(mir::MIRType::getInt8(), states, insertIdx64, "insert.state.ptr");
	builder->createStore(builder->getInt8(1), insertStatePtr);

	// Increment count
	mir::MIRValue *newCount = builder->createAdd(count, one, "new.count");
	builder->createStore(newCount, countPtr);
	builder->createBr(exitBlock);

	// Update existing value
	builder->setInsertPoint(updateExisting);
	mir::MIRValue *updateValuePtr = builder->createArrayGEP(valueType, values, idx64, "update.value.ptr");
	builder->createStore(value, updateValuePtr);
	builder->createBr(exitBlock);

	// Next probe
	builder->setInsertPoint(nextProbe);
	mir::MIRValue *nextIndex = builder->createAdd(currentIndex, one, "next.idx");
	mir::MIRValue *wrappedIndex = builder->createSRem(nextIndex, capacity, "wrapped.idx");
	builder->createStore(wrappedIndex, indexAlloca);
	mir::MIRValue *nextProbes = builder->createAdd(probes, one, "next.probes");
	builder->createStore(nextProbes, probesAlloca);
	builder->createBr(loopCond);

	// Exit (void return)
	builder->setInsertPoint(exitBlock);
	return nullptr;
}

//==============================================================================
// Map Remove Operation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMapRemove(mir::MIRValue *mapAlloca, mir::MIRValue *key,
												   mir::MIRType *keyType, mir::MIRType *valueType,
												   const std::string &keyTypeStr, mir::MIRType *mapStructType) {
	mir::MIRFunction *function = builder->getFunction();

	// Load map fields
	mir::MIRValue *keysPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "keys.ptr.ptr");
	mir::MIRValue *keys = builder->createLoad(mir::MIRType::getPtr(), keysPtr, "keys");
	mir::MIRValue *statesPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "states.ptr.ptr");
	mir::MIRValue *states = builder->createLoad(mir::MIRType::getPtr(), statesPtr, "states");
	mir::MIRValue *countPtr = builder->createStructGEP(mapStructType, mapAlloca, 3, "count.ptr");
	mir::MIRValue *count = builder->createLoad(mir::MIRType::getInt32(), countPtr, "count");
	mir::MIRValue *capacityPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "capacity.ptr");
	mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");

	// Compute initial index
	mir::MIRValue *hash = generateHashForKey(key, keyTypeStr);
	mir::MIRValue *index = builder->createSRem(hash, capacity, "index");
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *isNeg = builder->createICmpSLT(index, zero, "is.neg");
	mir::MIRValue *fixedIndex = builder->createAdd(index, capacity, "fixed.idx");

	// Use alloca to handle the select logic
	mir::MIRValue *indexFixAlloca = builder->createAlloca(mir::MIRType::getInt32(), "remove.index.fix");
	builder->createStore(index, indexFixAlloca);

	mir::MIRBasicBlock *fixNegBlock = function->createBasicBlock("remove.fix.neg");
	mir::MIRBasicBlock *afterFixBlock = function->createBasicBlock("remove.after.fix");
	builder->createCondBr(isNeg, fixNegBlock, afterFixBlock);

	builder->setInsertPoint(fixNegBlock);
	builder->createStore(fixedIndex, indexFixAlloca);
	builder->createBr(afterFixBlock);

	builder->setInsertPoint(afterFixBlock);
	index = builder->createLoad(mir::MIRType::getInt32(), indexFixAlloca, "abs.index");

	// Create result and loop variable allocas
	mir::MIRValue *resultAlloca = builder->createAlloca(mir::MIRType::getInt1(), "remove.result");
	builder->createStore(builder->getInt1(false), resultAlloca);
	mir::MIRValue *indexAlloca = builder->createAlloca(mir::MIRType::getInt32(), "index.alloca");
	builder->createStore(index, indexAlloca);
	mir::MIRValue *probesAlloca = builder->createAlloca(mir::MIRType::getInt32(), "probes.alloca");
	builder->createStore(zero, probesAlloca);

	// Create basic blocks
	mir::MIRBasicBlock *loopCond = function->createBasicBlock("remove.loop.cond");
	mir::MIRBasicBlock *loopBody = function->createBasicBlock("remove.loop.body");
	mir::MIRBasicBlock *foundBlock = function->createBasicBlock("remove.found");
	mir::MIRBasicBlock *nextProbe = function->createBasicBlock("remove.next");
	mir::MIRBasicBlock *exitBlock = function->createBasicBlock("remove.exit");

	builder->createBr(loopCond);

	// Loop condition
	builder->setInsertPoint(loopCond);
	mir::MIRValue *probes = builder->createLoad(mir::MIRType::getInt32(), probesAlloca, "probes");
	mir::MIRValue *continueLoop = builder->createICmpSLT(probes, capacity, "continue.loop");
	builder->createCondBr(continueLoop, loopBody, exitBlock);

	// Loop body
	builder->setInsertPoint(loopBody);
	mir::MIRValue *currentIndex = builder->createLoad(mir::MIRType::getInt32(), indexAlloca, "curr.idx");
	mir::MIRValue *idx64 = builder->createSExt(currentIndex, mir::MIRType::getInt64(), "idx64");

	mir::MIRValue *statePtr = builder->createArrayGEP(mir::MIRType::getInt8(), states, idx64, "state.ptr");
	mir::MIRValue *state = builder->createLoad(mir::MIRType::getInt8(), statePtr, "state");
	mir::MIRValue *stateInt = builder->createZExt(state, mir::MIRType::getInt32(), "state.int");

	// Check if empty -> not found
	mir::MIRValue *isEmpty = builder->createICmpEq(stateInt, zero, "is.empty");
	mir::MIRBasicBlock *checkOccupied = function->createBasicBlock("remove.check.occupied");
	builder->createCondBr(isEmpty, exitBlock, checkOccupied);

	// Check if occupied
	builder->setInsertPoint(checkOccupied);
	mir::MIRValue *one = builder->getInt32(1);
	mir::MIRValue *isOccupied = builder->createICmpEq(stateInt, one, "is.occupied");
	mir::MIRBasicBlock *compareKey = function->createBasicBlock("remove.compare.key");
	builder->createCondBr(isOccupied, compareKey, nextProbe);

	// Compare keys
	builder->setInsertPoint(compareKey);
	mir::MIRValue *keyPtr = builder->createArrayGEP(keyType, keys, idx64, "key.ptr");
	mir::MIRValue *storedKey = builder->createLoad(keyType, keyPtr, "stored.key");
	mir::MIRValue *keysEqual = builder->createICmpEq(storedKey, key, "keys.equal");
	builder->createCondBr(keysEqual, foundBlock, nextProbe);

	// Found: mark as deleted (state = 2)
	builder->setInsertPoint(foundBlock);
	builder->createStore(builder->getInt8(2), statePtr);
	mir::MIRValue *newCount = builder->createSub(count, one, "new.count");
	builder->createStore(newCount, countPtr);
	builder->createStore(builder->getInt1(true), resultAlloca);
	builder->createBr(exitBlock);

	// Next probe
	builder->setInsertPoint(nextProbe);
	mir::MIRValue *nextIndex = builder->createAdd(currentIndex, one, "next.idx");
	mir::MIRValue *wrappedIndex = builder->createSRem(nextIndex, capacity, "wrapped.idx");
	builder->createStore(wrappedIndex, indexAlloca);
	mir::MIRValue *nextProbes = builder->createAdd(probes, one, "next.probes");
	builder->createStore(nextProbes, probesAlloca);
	builder->createBr(loopCond);

	// Exit
	builder->setInsertPoint(exitBlock);
	return builder->createLoad(mir::MIRType::getInt1(), resultAlloca, "remove.result");
}
