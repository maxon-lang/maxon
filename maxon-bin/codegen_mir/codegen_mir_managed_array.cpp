/**
 * MIR Code Generator - Managed Array Intrinsics (Phase 2)
 *
 * This file implements codegen for __managed_array_* intrinsics that work with
 * _ManagedArray<T> arrays using the struct-based layout:
 *
 * __ManagedArrayData<T>: { _buffer ptr, _len i32, _capacity i32 }
 *
 * - _buffer: pointer to the actual array data (heap or stack allocated)
 * - _len: current number of elements
 * - _capacity: allocated capacity (0 for static arrays)
 *
 * For local variables, an alloca of __ManagedArrayData<T> struct is created.
 * For struct fields, the field itself is the __ManagedArrayData<T> struct.
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"

// =============================================================================
// Helper: getManagedArrayInfo
// Returns information about a _ManagedArray variable for use in intrinsics.
// This handles both regular variables and implicit struct field access.
// =============================================================================

MIRCodeGenerator::ArrayFieldInfo MIRCodeGenerator::getManagedArrayInfo(ExprAST *arrayArg, int line, int column) {
	ArrayFieldInfo info = {};
	info.isStackArray = false;
	info.isStructField = false;
	info.fieldIndex = -1;

	std::string arrayVarName;
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
		arrayVarName = varExpr->name;
	} else {
		reportError("Managed array intrinsic requires a variable argument", line, column);
		return info;
	}

	// First, check if this is a regular variable (not a struct field)
	mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
	if (arrayAlloca) {
		// Regular variable found - it's a pointer to __ManagedArrayData struct
		info.dataPtr = arrayAlloca;
		info.lengthAlloca = nullptr;   // New layout uses struct fields, not separate allocas
		info.capacityAlloca = nullptr; // New layout uses struct fields, not separate allocas
		info.isStackArray = (stackAllocatedArrays.count(arrayVarName) > 0);

		std::string arrType = variableTypes[arrayVarName];
		if (maxon::TypeConversion::isArrayType(arrType)) {
			info.elementType = maxon::TypeConversion::getArrayElementType(arrType);
		} else {
			info.elementType = "int"; // Default fallback
		}
		return info;
	}

	// Not a regular variable - check if it's an implicit struct field access
	if (!currentReceiverType.empty()) {
		auto fieldsIt = structFields.find(currentReceiverType);
		if (fieldsIt != structFields.end()) {
			for (size_t i = 0; i < fieldsIt->second.size(); i++) {
				if (fieldsIt->second[i].first == arrayVarName) {
					// Found the field - it's an array field in the current struct
					std::string fieldType = fieldsIt->second[i].second;

					// Get pointer to the field via self
					mir::MIRValue *selfAlloca = namedValues["self"];
					if (selfAlloca) {
						mir::MIRValue *selfPtr;
						if (isStructParameter("self")) {
							selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
						} else {
							selfPtr = selfAlloca;
						}
						mir::MIRType *structType = structTypes[currentReceiverType];
						mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, static_cast<int>(i), arrayVarName + ".ptr");

						info.isStructField = true;
						info.fieldIndex = static_cast<int>(i);
						info.dataPtr = fieldPtr;	   // Pointer to the __ManagedArrayData struct (embedded in parent struct)
						info.lengthAlloca = nullptr;   // New layout uses struct fields
						info.capacityAlloca = nullptr; // New layout uses struct fields

						if (maxon::TypeConversion::isArrayType(fieldType)) {
							info.elementType = maxon::TypeConversion::getArrayElementType(fieldType);
						} else {
							info.elementType = "int";
						}
						return info;
					}
				}
			}
		}
	}

	reportError("Variable is not a managed array: " + arrayVarName, line, column);
	return info;
}

// =============================================================================
// Managed Array Intrinsics
// These work with the new struct-based layout: { _buffer ptr, _len i32, _capacity i32 }
// Field indices: 0 = _buffer, 1 = _len, 2 = _capacity
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_len(CallExprAST *callExpr) {
	// __managed_array_len(arr) - get current length from struct field 1
	ExprAST *arrayArg = callExpr->args[0].get();
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct (for local vars, info.dataPtr is the alloca)
	mir::MIRValue *structPtr = info.dataPtr;

	// GEP to _len field (index 1)
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "arr.len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_capacity(CallExprAST *callExpr) {
	// __managed_array_capacity(arr) - get current capacity from struct field 2
	ExprAST *arrayArg = callExpr->args[0].get();
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// GEP to _capacity field (index 2)
	mir::MIRValue *capPtr = builder->createStructGEP(managedArrayType, structPtr, 2, "arr._cap.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), capPtr, "arr.cap");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_set_length(CallExprAST *callExpr) {
	// __managed_array_set_length(arr, newLen) - set length in struct field 1
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *newLen = generateExpr(callExpr->args[1].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// GEP to _len field (index 1) and store
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	builder->createStore(newLen, lenPtr);
	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_grow(CallExprAST *callExpr) {
	// __managed_array_grow(arr, minCapacity) - grow array if needed
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *minCapacity = generateExpr(callExpr->args[1].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load current capacity from field 2
	mir::MIRValue *capPtr = builder->createStructGEP(managedArrayType, structPtr, 2, "arr._cap.ptr");
	mir::MIRValue *capacity = builder->createLoad(mir::MIRType::getInt32(), capPtr, "capacity");

	// Check if we need to grow: minCapacity > capacity
	mir::MIRFunction *currentFunc = builder->getFunction();
	mir::MIRBasicBlock *growBlock = currentFunc->createBasicBlock("grow.do");
	mir::MIRBasicBlock *doneBlock = currentFunc->createBasicBlock("grow.done");

	mir::MIRValue *needGrow = builder->createICmpSGT(minCapacity, capacity, "need.grow");
	builder->createCondBr(needGrow, growBlock, doneBlock);

	// Grow block
	builder->setInsertPoint(growBlock);

	// Calculate new capacity: max(minCapacity, capacity * 2), minimum 4
	mir::MIRValue *doubledCap = builder->createMul(capacity, builder->getInt32(2), "doubled.cap");

	// Use minCapacity if larger than doubled
	mir::MIRBasicBlock *useMinBlock = currentFunc->createBasicBlock("grow.usemin");
	mir::MIRBasicBlock *useDoubleBlock = currentFunc->createBasicBlock("grow.usedouble");
	mir::MIRBasicBlock *checkMinBlock = currentFunc->createBasicBlock("grow.checkmin");

	mir::MIRValue *minLarger = builder->createICmpSGT(minCapacity, doubledCap, "min.larger");
	builder->createCondBr(minLarger, useMinBlock, useDoubleBlock);

	builder->setInsertPoint(useMinBlock);
	builder->createBr(checkMinBlock);

	builder->setInsertPoint(useDoubleBlock);
	builder->createBr(checkMinBlock);

	builder->setInsertPoint(checkMinBlock);
	mir::MIRValue *newCapPhi = builder->createPhi(mir::MIRType::getInt32(), "new.cap.phi");
	builder->addPhiIncoming(newCapPhi, minCapacity, useMinBlock);
	builder->addPhiIncoming(newCapPhi, doubledCap, useDoubleBlock);

	// Ensure minimum capacity of 4
	mir::MIRBasicBlock *useMinFourBlock = currentFunc->createBasicBlock("grow.minfour");
	mir::MIRBasicBlock *doReallocBlock = currentFunc->createBasicBlock("grow.realloc");

	mir::MIRValue *lessThanFour = builder->createICmpSLT(newCapPhi, builder->getInt32(4), "lt.four");
	builder->createCondBr(lessThanFour, useMinFourBlock, doReallocBlock);

	builder->setInsertPoint(useMinFourBlock);
	builder->createBr(doReallocBlock);

	builder->setInsertPoint(doReallocBlock);
	mir::MIRValue *newCapacity = builder->createPhi(mir::MIRType::getInt32(), "new.capacity");
	builder->addPhiIncoming(newCapacity, builder->getInt32(4), useMinFourBlock);
	builder->addPhiIncoming(newCapacity, newCapPhi, checkMinBlock);

	// Calculate old and new sizes in bytes
	mir::MIRValue *elemSizeVal = builder->getInt64(elemSize);
	mir::MIRValue *capacity64 = builder->createSExt(capacity, mir::MIRType::getInt64(), "cap64");
	mir::MIRValue *newCapacity64 = builder->createSExt(newCapacity, mir::MIRType::getInt64(), "newcap64");
	mir::MIRValue *oldSize = builder->createMul(capacity64, elemSizeVal, "old.size");
	mir::MIRValue *newSize = builder->createMul(newCapacity64, elemSizeVal, "new.size");

	// Load current buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *arrPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "arr.ptr");

	// Call realloc
	mir::MIRFunction *reallocFunc = getOrDeclareFunction("realloc", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getInt64(), mir::MIRType::getInt64()});
	mir::MIRValue *newArrPtr = builder->createCall(reallocFunc, {arrPtr, oldSize, newSize}, "new.arr");

	// Store new buffer pointer to field 0
	builder->createStore(newArrPtr, bufferPtr);

	// Store new capacity to field 2
	builder->createStore(newCapacity, capPtr);
	builder->createBr(doneBlock);

	// Done block
	builder->setInsertPoint(doneBlock);
	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_set_at(CallExprAST *callExpr) {
	// __managed_array_set_at(arr, index, value) - set element at index
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *index = generateExpr(callExpr->args[1].get());
	mir::MIRValue *value = generateExpr(callExpr->args[2].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type
	mir::MIRType *elemType = getTypeFromString(info.elementType);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Calculate element pointer and store
	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, dataPtr, index64, "elem.ptr");
	builder->createStore(value, elemPtr);

	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_get_at(CallExprAST *callExpr) {
	// __managed_array_get_at(arr, index) - get element at index
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *index = generateExpr(callExpr->args[1].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	mir::MIRType *elemType = getTypeFromString(info.elementType);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Calculate element pointer and load
	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, dataPtr, index64, "elem.ptr");
	return builder->createLoad(elemType, elemPtr, "elem.val");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_shift_right(CallExprAST *callExpr) {
	// __managed_array_shift_right(arr, start, count) - shift elements right for insert
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Get current length from field 1
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	// Calculate number of elements to move: length - start
	mir::MIRValue *elementsToMove = builder->createSub(length, start, "elems.to.move");

	// Calculate byte size to move
	mir::MIRValue *elementsToMove64 = builder->createSExt(elementsToMove, mir::MIRType::getInt64(), "elems64");
	mir::MIRValue *byteSize = builder->createMul(elementsToMove64, builder->getInt64(elemSize), "byte.size");

	// Source pointer: arr + start * elemSize
	mir::MIRValue *start64 = builder->createSExt(start, mir::MIRType::getInt64(), "start64");
	mir::MIRValue *srcPtr = builder->createArrayGEP(elemType, dataPtr, start64, "src.ptr");

	// Dest pointer: arr + (start + count) * elemSize
	mir::MIRValue *destIndex = builder->createAdd(start, count, "dest.idx");
	mir::MIRValue *destIndex64 = builder->createSExt(destIndex, mir::MIRType::getInt64(), "dest64");
	mir::MIRValue *destPtr = builder->createArrayGEP(elemType, dataPtr, destIndex64, "dest.ptr");

	// Call memmove (handles overlapping regions)
	mir::MIRFunction *memmoveFunc = getOrDeclareFunction("memmove", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memmoveFunc, {destPtr, srcPtr, byteSize}, "");

	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_shift_left(CallExprAST *callExpr) {
	// __managed_array_shift_left(arr, start, count) - shift elements left for remove
	ExprAST *arrayArg = callExpr->args[0].get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Get current length from field 1
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	// Calculate number of elements to move: length - start - count
	mir::MIRValue *elementsToMove = builder->createSub(length, start, "elems.tmp");
	elementsToMove = builder->createSub(elementsToMove, count, "elems.to.move");

	// Calculate byte size to move
	mir::MIRValue *elementsToMove64 = builder->createSExt(elementsToMove, mir::MIRType::getInt64(), "elems64");
	mir::MIRValue *byteSize = builder->createMul(elementsToMove64, builder->getInt64(elemSize), "byte.size");

	// Source pointer: arr + (start + count) * elemSize
	mir::MIRValue *srcIndex = builder->createAdd(start, count, "src.idx");
	mir::MIRValue *srcIndex64 = builder->createSExt(srcIndex, mir::MIRType::getInt64(), "src64");
	mir::MIRValue *srcPtr = builder->createArrayGEP(elemType, dataPtr, srcIndex64, "src.ptr");

	// Dest pointer: arr + start * elemSize
	mir::MIRValue *start64 = builder->createSExt(start, mir::MIRType::getInt64(), "start64");
	mir::MIRValue *destPtr = builder->createArrayGEP(elemType, dataPtr, start64, "dest.ptr");

	// Call memmove (handles overlapping regions)
	mir::MIRFunction *memmoveFunc = getOrDeclareFunction("memmove", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memmoveFunc, {destPtr, srcPtr, byteSize}, "");

	return builder->getInt32(0);
}
