/**
 * MIR Code Generator - Array Intrinsic Generation
 *
 * This file implements array intrinsic code generation for MIR (push, pop).
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <stdexcept>

//==============================================================================
// Array Intrinsics (push, pop)
//==============================================================================

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
			reportError("push() requires exactly 2 arguments: array and value",
						callExpr->line, callExpr->column);
		}

		// First arg must be an array variable
		auto *arrVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
		if (!arrVar) {
			reportError("push() first argument must be an array variable",
						callExpr->line, callExpr->column);
		}

		std::string arrName = arrVar->name;
		std::string arrType = variableTypes[arrName];

		// Check it's a dynamic array (has capacity)
		mir::MIRValue *capacityAlloca = namedValues[arrName + ".__capacity"];
		if (!capacityAlloca) {
			reportError("push() can only be used on dynamic (var) arrays, not static (let) arrays",
						callExpr->line, callExpr->column);
		}

		// Get array's internal allocas
		mir::MIRValue *arrAlloca = namedValues[arrName];
		mir::MIRValue *lengthAlloca = namedValues[arrName + ".__length"];

		if (!arrAlloca || !lengthAlloca) {
			reportError("Array variable not found: " + arrName,
						callExpr->line, callExpr->column);
		}

		// Generate the value to push
		mir::MIRValue *value = generateExpr(callExpr->args[1].get());
		if (!value) {
			reportError("Failed to generate value for push()",
						callExpr->line, callExpr->column);
		}

		// Determine element type and size
		std::string elemTypeStr = "int";
		if (maxon::TypeConversion::isArrayType(arrType)) {
			elemTypeStr = maxon::TypeConversion::getArrayElementType(arrType);
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
			reportError("pop() requires exactly 1 argument: array",
						callExpr->line, callExpr->column);
		}

		// First arg must be an array variable
		auto *arrVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
		if (!arrVar) {
			reportError("pop() argument must be an array variable",
						callExpr->line, callExpr->column);
		}

		std::string arrName = arrVar->name;
		std::string arrType = variableTypes[arrName];

		// Check it's a dynamic array
		mir::MIRValue *capacityAlloca = namedValues[arrName + ".__capacity"];
		if (!capacityAlloca) {
			reportError("pop() can only be used on dynamic (var) arrays, not static (let) arrays",
						callExpr->line, callExpr->column);
		}

		// Get array's internal allocas
		mir::MIRValue *arrAlloca = namedValues[arrName];
		mir::MIRValue *lengthAlloca = namedValues[arrName + ".__length"];

		if (!arrAlloca || !lengthAlloca) {
			reportError("Array variable not found: " + arrName,
						callExpr->line, callExpr->column);
		}

		// Determine element type
		std::string elemTypeStr = "int";
		if (maxon::TypeConversion::isArrayType(arrType)) {
			elemTypeStr = maxon::TypeConversion::getArrayElementType(arrType);
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

	reportError("Unknown array intrinsic: " + callExpr->callee,
				callExpr->line, callExpr->column);
}

//==============================================================================
// Map Intrinsic: map(arr, transform) -> []ReturnType
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMapIntrinsic(CallExprAST *callExpr) {
	// map(arr, transform) - creates a new array by applying transform to each element
	// Returns a new dynamic array with the transformed elements
	//
	// Inline implementation:
	//   1. Get source array length
	//   2. Allocate new array with same length
	//   3. Loop over elements, call transform, store in new array
	//   4. Return new array

	if (callExpr->args.size() != 2) {
		reportError("map() requires exactly 2 arguments: array and transform function",
					callExpr->line, callExpr->column);
	}

	// Get source array info
	mir::MIRValue *srcArray = generateExpr(callExpr->args[0].get());
	if (!srcArray) {
		reportError("Failed to generate source array for map()",
					callExpr->line, callExpr->column);
	}

	// Get source array type and element type
	std::string srcArrType = inferExprType(callExpr->args[0].get());
	std::string srcElemType = maxon::TypeConversion::getArrayElementType(srcArrType);
	mir::MIRType *srcElemMIRType = getTypeFromString(srcElemType);

	// Get transform function
	mir::MIRValue *transformFunc = generateExpr(callExpr->args[1].get());
	if (!transformFunc) {
		reportError("Failed to generate transform function for map()",
					callExpr->line, callExpr->column);
	}

	// Determine result element type from transform function
	std::string funcType = inferExprType(callExpr->args[1].get());
	size_t arrowPos = funcType.find(")->");
	if (arrowPos == std::string::npos) {
		reportError("Invalid function type for map(): " + funcType,
					callExpr->line, callExpr->column);
	}
	std::string resultElemType = funcType.substr(arrowPos + 3);
	mir::MIRType *resultElemMIRType = getTypeFromString(resultElemType);
	int resultElemSize = static_cast<int>(resultElemMIRType->sizeInBytes);

	// Get source array length
	mir::MIRValue *srcLength = nullptr;
	auto *srcVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
	if (srcVar) {
		std::string varName = srcVar->name;
		// First check for hidden .__length alloca (used by static arrays and function params)
		mir::MIRValue *lengthAlloca = namedValues[varName + ".__length"];
		if (lengthAlloca) {
			srcLength = builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "src.length");
		} else {
			// For managed arrays, length is in the _len field of __ManagedArrayData struct
			mir::MIRValue *arrAlloca = namedValues[varName];
			if (arrAlloca && maxon::TypeConversion::isManagedArrayType(srcArrType)) {
				// Get the _len field (index 1) from the struct
				mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(srcElemType);
				mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, arrAlloca, 1, varName + "._len");
				srcLength = builder->createLoad(mir::MIRType::getInt32(), lenField, "src.length");
			}
		}
	}

	if (!srcLength) {
		// Try to get length from static array type
		if (maxon::TypeConversion::isStaticArrayType(srcArrType)) {
			int staticLen = maxon::TypeConversion::getStaticArraySize(srcArrType);
			srcLength = builder->getInt32(staticLen);
		} else {
			reportError("Cannot determine array length for map()",
						callExpr->line, callExpr->column);
		}
	}

	mir::MIRFunction *currentFunc = builder->getFunction();

	// Allocate new array: malloc(length * element_size)
	mir::MIRValue *length64 = builder->createSExt(srcLength, mir::MIRType::getInt64(), "len64");
	mir::MIRValue *elemSizeVal = builder->getInt64(resultElemSize);
	mir::MIRValue *totalSize = builder->createMul(length64, elemSizeVal, "alloc.size");

	mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(),
														{mir::MIRType::getInt64()});
	mir::MIRValue *resultArray = builder->createCall(mallocFunc, {totalSize}, "result.arr");

	// Create loop to transform elements
	// Loop header: check i < length
	mir::MIRBasicBlock *loopHeader = currentFunc->createBasicBlock("map.header");
	mir::MIRBasicBlock *loopBody = currentFunc->createBasicBlock("map.body");
	mir::MIRBasicBlock *loopExit = currentFunc->createBasicBlock("map.exit");

	// Allocate loop counter
	mir::MIRValue *counterAlloca = builder->createAlloca(mir::MIRType::getInt32(), "map.counter");
	builder->createStore(builder->getInt32(0), counterAlloca);
	builder->createBr(loopHeader);

	// Loop header
	builder->setInsertPoint(loopHeader);
	mir::MIRValue *counter = builder->createLoad(mir::MIRType::getInt32(), counterAlloca, "i");
	mir::MIRValue *loopCond = builder->createICmpSLT(counter, srcLength, "map.cond");
	builder->createCondBr(loopCond, loopBody, loopExit);

	// Loop body
	builder->setInsertPoint(loopBody);

	// Get source pointer (buffer pointer for managed arrays)
	mir::MIRValue *srcPtr = srcArray;
	if (srcVar) {
		mir::MIRValue *srcAlloca = namedValues[srcVar->name];
		if (srcAlloca && maxon::TypeConversion::isManagedArrayType(srcArrType)) {
			// Get the _buffer field (index 0) from the __ManagedArrayData struct
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(srcElemType);
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, srcAlloca, 0, srcVar->name + "._buffer");
			srcPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "src.ptr");
		} else if (srcAlloca) {
			srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcAlloca, "src.ptr");
		}
	}

	// Load element from source: src[i]
	mir::MIRValue *idx64 = builder->createSExt(counter, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *srcElemPtr = builder->createArrayGEP(srcElemMIRType, srcPtr, idx64, "src.elem.ptr");
	mir::MIRValue *srcElem = builder->createLoad(srcElemMIRType, srcElemPtr, "src.elem");

	// Call transform function with the element
	mir::MIRValue *transformedElem = builder->createCallIndirect(
		transformFunc, resultElemMIRType, {srcElemMIRType}, {srcElem}, "transformed");

	// Store result in destination array: result[i] = transformed
	mir::MIRValue *destElemPtr = builder->createArrayGEP(resultElemMIRType, resultArray, idx64, "dest.elem.ptr");
	builder->createStore(transformedElem, destElemPtr);

	// Increment counter
	mir::MIRValue *nextCounter = builder->createAdd(counter, builder->getInt32(1), "next.i");
	builder->createStore(nextCounter, counterAlloca);
	builder->createBr(loopHeader);

	// Loop exit - return the result array
	builder->setInsertPoint(loopExit);

	// Store the result array info for the caller
	// The caller needs: ptr, length, capacity
	// We'll need to store these in the result struct or use a different approach
	// For now, store them as the last generated map result
	lastMapResultLength = srcLength;
	lastMapResultCapacity = srcLength; // capacity equals length for new arrays

	return resultArray;
}

//==============================================================================
// Array Collection Methods: count, get, set, map
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateArrayCollectionMethod(CallExprAST *callExpr, const std::string &methodName) {
	// All Collection methods take the array as the first argument (self)
	if (callExpr->args.empty()) {
		reportError("Collection method '" + methodName + "' requires array argument",
					callExpr->line, callExpr->column);
	}

	// Get the array variable
	auto *arrVar = dynamic_cast<VariableExprAST *>(callExpr->args[0].get());
	if (!arrVar) {
		reportError("Collection method '" + methodName + "' requires a variable as first argument",
					callExpr->line, callExpr->column);
	}

	std::string varName = arrVar->name;
	mir::MIRValue *arrAlloca = namedValues[varName];
	if (!arrAlloca) {
		reportError("Array variable '" + varName + "' not found",
					callExpr->line, callExpr->column);
	}

	// Get element type from array type
	std::string arrType = inferExprType(callExpr->args[0].get());
	std::string elemType = maxon::TypeConversion::getArrayElementType(arrType);
	mir::MIRType *elemMIRType = getTypeFromString(elemType);
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);

	if (methodName == "count") {
		// count() -> int: return the length field
		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, arrAlloca, 1, varName + "._len");
		return builder->createLoad(mir::MIRType::getInt32(), lenField, "count");

	} else if (methodName == "get") {
		// get(index) -> Element or nil: bounds-checked access
		if (callExpr->args.size() != 2) {
			reportError("get() requires index argument", callExpr->line, callExpr->column);
		}

		mir::MIRValue *index = generateExpr(callExpr->args[1].get());
		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, arrAlloca, 1, varName + "._len");
		mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lenField, "length");

		// Create the result optional struct type: { hasValue i1, value Element }
		mir::MIRType *optionalType = mir::MIRType::getOptional(elemMIRType);
		mir::MIRValue *resultAlloca = builder->createAlloca(optionalType, "get.result");

		// Create bounds check: index >= 0 && index < length
		mir::MIRFunction *currentFunc = builder->getFunction();
		mir::MIRBasicBlock *inBoundsBlock = currentFunc->createBasicBlock("get.inbounds");
		mir::MIRBasicBlock *outOfBoundsBlock = currentFunc->createBasicBlock("get.oob");
		mir::MIRBasicBlock *mergeBlock = currentFunc->createBasicBlock("get.merge");

		mir::MIRValue *zero = builder->getInt32(0);
		mir::MIRValue *geZero = builder->createICmpSGE(index, zero, "ge.zero");
		mir::MIRValue *ltLen = builder->createICmpSLT(index, length, "lt.len");
		mir::MIRValue *inBounds = builder->createAnd(geZero, ltLen, "inbounds");
		builder->createCondBr(inBounds, inBoundsBlock, outOfBoundsBlock);

		// In bounds: load and return value
		builder->setInsertPoint(inBoundsBlock);
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, arrAlloca, 0, varName + "._buffer");
		mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "buffer");
		mir::MIRValue *idx64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemMIRType, bufferPtr, idx64, "elem.ptr");
		mir::MIRValue *elemValue = builder->createLoad(elemMIRType, elemPtr, "elem");

		// Store hasValue = true and value
		mir::MIRValue *hasValueField = builder->createStructGEP(optionalType, resultAlloca, 0, "hasValue.ptr");
		builder->createStore(builder->getInt1(true), hasValueField);
		mir::MIRValue *valueField = builder->createStructGEP(optionalType, resultAlloca, 1, "value.ptr");
		builder->createStore(elemValue, valueField);
		builder->createBr(mergeBlock);

		// Out of bounds: return nil
		builder->setInsertPoint(outOfBoundsBlock);
		mir::MIRValue *hasValueFieldOob = builder->createStructGEP(optionalType, resultAlloca, 0, "hasValue.ptr.oob");
		builder->createStore(builder->getInt1(false), hasValueFieldOob);
		builder->createBr(mergeBlock);

		// Merge and return
		builder->setInsertPoint(mergeBlock);
		return builder->createLoad(optionalType, resultAlloca, "get.optional");

	} else if (methodName == "set") {
		// set(index, value) -> Self: bounds-checked set
		if (callExpr->args.size() != 3) {
			reportError("set() requires index and value arguments", callExpr->line, callExpr->column);
		}

		mir::MIRValue *index = generateExpr(callExpr->args[1].get());
		mir::MIRValue *value = generateExpr(callExpr->args[2].get());
		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, arrAlloca, 1, varName + "._len");
		mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lenField, "length");

		// Create bounds check
		mir::MIRFunction *currentFunc = builder->getFunction();
		mir::MIRBasicBlock *inBoundsBlock = currentFunc->createBasicBlock("set.inbounds");
		mir::MIRBasicBlock *mergeBlock = currentFunc->createBasicBlock("set.merge");

		mir::MIRValue *zero = builder->getInt32(0);
		mir::MIRValue *geZero = builder->createICmpSGE(index, zero, "ge.zero");
		mir::MIRValue *ltLen = builder->createICmpSLT(index, length, "lt.len");
		mir::MIRValue *inBounds = builder->createAnd(geZero, ltLen, "inbounds");
		builder->createCondBr(inBounds, inBoundsBlock, mergeBlock);

		// In bounds: store value
		builder->setInsertPoint(inBoundsBlock);
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, arrAlloca, 0, varName + "._buffer");
		mir::MIRValue *bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "buffer");
		mir::MIRValue *idx64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
		mir::MIRValue *elemPtr = builder->createArrayGEP(elemMIRType, bufferPtr, idx64, "elem.ptr");
		builder->createStore(value, elemPtr);
		builder->createBr(mergeBlock);

		// Merge and return self (the array)
		builder->setInsertPoint(mergeBlock);
		return builder->createLoad(managedArrayType, arrAlloca, "set.self");

	} else if (methodName == "map") {
		// map(transform) -> Self: delegate to existing map intrinsic
		return generateMapIntrinsic(callExpr);
	}

	reportError("Unknown array Collection method: " + methodName, callExpr->line, callExpr->column);
	return nullptr;
}
