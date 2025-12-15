/**
 * MIR Code Generator - Managed Array Intrinsics (Phase 2)
 *
 * This file implements codegen for __managed_array_* intrinsics that work with
 * _ManagedArray<T> arrays using the struct-based layout:
 *
 * __ManagedArrayData<T>: { _buffer ptr, _len i64, _capacity i64 }
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
#include "managed_array_builder.h"
#include <iostream>

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
		// For struct parameters (passed by pointer), we need to load the pointer first
		mir::MIRValue *dataPtr = arrayAlloca;
		if (isStructParameter(arrayVarName)) {
			dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, arrayVarName + ".ptr");
		}
		info.lengthAlloca = nullptr;   // New layout uses struct fields, not separate allocas
		info.capacityAlloca = nullptr; // New layout uses struct fields, not separate allocas
		info.isStackArray = (stackAllocatedArrays.count(arrayVarName) > 0);

		std::string arrType = variableTypes[arrayVarName];
		if (maxon::TypeConversion::isArrayStructType(arrType)) {
			// array<T> struct: { __ManagedArrayData<T> _data, i32 _iter_pos }
			// Need to GEP to field 0 to get the __ManagedArrayData part
			info.elementType = maxon::TypeConversion::getArrayStructElementType(arrType);
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(info.elementType);
			mir::MIRValue *managedDataPtr = builder->createStructGEP(arrayStructType, dataPtr, 0, arrayVarName + ".managed");
			info.dataPtr = managedDataPtr;
		} else if (maxon::TypeConversion::isManagedArrayType(arrType)) {
			// _ManagedArray<T>: directly the __ManagedArrayData struct
			info.elementType = maxon::TypeConversion::getArrayElementType(arrType);
			info.dataPtr = dataPtr;
		} else if (maxon::TypeConversion::isArrayType(arrType)) {
			// Legacy array type
			info.elementType = maxon::TypeConversion::getArrayElementType(arrType);
			info.dataPtr = dataPtr;
		} else {
			info.elementType = "int"; // Default fallback
			info.dataPtr = dataPtr;
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
						auto structIt = structTypes.find(currentReceiverType);
						if (structIt == structTypes.end()) {
							reportError("struct type not found: " + currentReceiverType, line, column);
							return info;
						}
						mir::MIRType *structType = structIt->second;
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
	ExprAST *arrayArg = callExpr->args[0].value.get();
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	return mab.getLength(info.dataPtr, "arr.len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_capacity(CallExprAST *callExpr) {
	// __managed_array_capacity(arr) - get current capacity from struct field 2
	ExprAST *arrayArg = callExpr->args[0].value.get();
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	return mab.getCapacity(info.dataPtr, "arr.cap");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_set_length(CallExprAST *callExpr) {
	// __managed_array_set_length(arr, newLen) - set length in struct field 1
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *newLen = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	mab.setLength(info.dataPtr, newLen);
	return builder->getInt64(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_set_capacity(CallExprAST *callExpr) {
	// __managed_array_set_capacity(arr, newCap) - set capacity in struct field 2
	// Used to "transfer ownership" - setting capacity to 0 prevents cleanup
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *newCap = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	mab.setCapacity(info.dataPtr, newCap);
	return builder->getInt64(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_grow(CallExprAST *callExpr) {
	// __managed_array_grow(arr, minCapacity) - grow array if needed
	// Uses tracked allocation via _managed_array_alloc/_managed_array_release
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *minCapacity = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	ManagedArrayBuilder mab(*this, info.elementType);
	int elemSize = mab.getElementSize();
	mir::MIRValue *structPtr = info.dataPtr;

	// Load current capacity from field 2
	mir::MIRValue *capacity = mab.getCapacity(structPtr, "capacity");

	// Check if we need to grow: minCapacity > capacity
	mir::MIRFunction *currentFunc = builder->getFunction();
	mir::MIRBasicBlock *growBlock = currentFunc->createBasicBlock("grow.do");
	mir::MIRBasicBlock *doneBlock = currentFunc->createBasicBlock("grow.done");

	mir::MIRValue *needGrow = builder->createICmpSGT(minCapacity, capacity, "need.grow");
	builder->createCondBr(needGrow, growBlock, doneBlock);

	// Grow block
	builder->setInsertPoint(growBlock);

	// Calculate new capacity: max(minCapacity, capacity * 2), minimum 4
	mir::MIRValue *doubledCap = builder->createMul(capacity, builder->getInt64(2), "doubled.cap");

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
	mir::MIRValue *newCapPhi = builder->createPhi(mir::MIRType::getInt64(), "new.cap.phi");
	builder->addPhiIncoming(newCapPhi, minCapacity, useMinBlock);
	builder->addPhiIncoming(newCapPhi, doubledCap, useDoubleBlock);

	// Ensure minimum capacity of 4
	mir::MIRBasicBlock *useMinFourBlock = currentFunc->createBasicBlock("grow.minfour");
	mir::MIRBasicBlock *doAllocBlock = currentFunc->createBasicBlock("grow.alloc");

	mir::MIRValue *lessThanFour = builder->createICmpSLT(newCapPhi, builder->getInt64(4), "lt.four");
	builder->createCondBr(lessThanFour, useMinFourBlock, doAllocBlock);

	builder->setInsertPoint(useMinFourBlock);
	builder->createBr(doAllocBlock);

	builder->setInsertPoint(doAllocBlock);
	mir::MIRValue *newCapacity = builder->createPhi(mir::MIRType::getInt64(), "new.capacity");
	builder->addPhiIncoming(newCapacity, builder->getInt64(4), useMinFourBlock);
	builder->addPhiIncoming(newCapacity, newCapPhi, checkMinBlock);

	// Load current buffer pointer and length
	mir::MIRValue *arrPtr = mab.getDataPtr(structPtr, "arr.ptr");
	mir::MIRValue *currentLen = mab.getLength(structPtr, "current.len");

	// Allocate new buffer using tracked allocation
	mir::MIRValue *newHeapBuffer = mab.allocateBuffer(newCapacity, "array grow");

	// Copy existing elements: memcpy(newHeapBuffer, arrPtr, currentLen * elemSize)
	mir::MIRValue *elemSizeVal = builder->getInt64(elemSize);
	mir::MIRValue *copySize = builder->createMul(currentLen, elemSizeVal, "copy.size");
	mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
														{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memcpyFunc, {newHeapBuffer, arrPtr, copySize});

	// Release old buffer if it was heap-allocated (capacity > 0)
	mir::MIRBasicBlock *releaseOldBlock = currentFunc->createBasicBlock("grow.release.old");
	mir::MIRBasicBlock *afterReleaseBlock = currentFunc->createBasicBlock("grow.after.release");

	mir::MIRValue *wasHeap = builder->createICmpSGT(capacity, builder->getInt64(0), "was.heap");
	builder->createCondBr(wasHeap, releaseOldBlock, afterReleaseBlock);

	builder->setInsertPoint(releaseOldBlock);
	mab.emitRelease(arrPtr, "array grow");
	builder->createBr(afterReleaseBlock);

	builder->setInsertPoint(afterReleaseBlock);

	// Store new buffer pointer to field 0
	mab.setDataPtr(structPtr, newHeapBuffer);

	// Store new capacity to field 2
	mab.setCapacity(structPtr, newCapacity);
	builder->createBr(doneBlock);

	// Done block
	builder->setInsertPoint(doneBlock);
	return builder->getInt64(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_set_at(CallExprAST *callExpr) {
	// __managed_array_set_at(arr, index, value) - set element at index
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *value = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	mir::MIRValue *dataPtr = mab.getDataPtr(info.dataPtr, "data.ptr");
	mir::MIRValue *elemPtr = mab.getElementPtr(dataPtr, index, "elem.ptr");
	builder->createStore(value, elemPtr);

	return builder->getInt64(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_get_at(CallExprAST *callExpr) {
	// __managed_array_get_at(arr, index) - get element at index
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	if (!info.dataPtr) {
		return nullptr;
	}

	ManagedArrayBuilder mab(*this, info.elementType);
	mir::MIRValue *dataPtr = mab.getDataPtr(info.dataPtr, "data.ptr");
	mir::MIRValue *elemPtr = mab.getElementPtr(dataPtr, index, "elem.ptr");
	return builder->createLoad(mab.getElementMIRType(), elemPtr, "elem.val");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_shift_right(CallExprAST *callExpr) {
	// __managed_array_shift_right(arr, start, count) - shift elements right for insert
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->getSizeInBytes());

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Get current length from field 1
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt64(), lenPtr, "len");

	// Calculate number of elements to move: length - start
	mir::MIRValue *elementsToMove = builder->createSub(length, start, "elems.to.move");

	// Calculate byte size to move
	mir::MIRValue *byteSize = builder->createMul(elementsToMove, builder->getInt64(elemSize), "byte.size");

	// Source pointer: arr + start * elemSize
	mir::MIRValue *srcPtr = builder->createArrayGEP(elemType, dataPtr, start, "src.ptr");

	// Dest pointer: arr + (start + count) * elemSize
	mir::MIRValue *destIndex = builder->createAdd(start, count, "dest.idx");
	mir::MIRValue *destPtr = builder->createArrayGEP(elemType, dataPtr, destIndex, "dest.ptr");

	// Call memmove (handles overlapping regions)
	mir::MIRFunction *memmoveFunc = getOrDeclareFunction("memmove", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memmoveFunc, {destPtr, srcPtr, byteSize}, "");

	return builder->getInt64(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_managed_array_shift_left(CallExprAST *callExpr) {
	// __managed_array_shift_left(arr, start, count) - shift elements left for remove
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getManagedArrayInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->getSizeInBytes());

	// Get the __ManagedArrayData struct type
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);

	// Get pointer to the struct
	mir::MIRValue *structPtr = info.dataPtr;

	// Load buffer pointer from field 0
	mir::MIRValue *bufferPtr = builder->createStructGEP(managedArrayType, structPtr, 0, "arr._buffer.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtr, "data.ptr");

	// Get current length from field 1
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, structPtr, 1, "arr._len.ptr");
	mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt64(), lenPtr, "len");

	// Calculate number of elements to move: length - start - count
	mir::MIRValue *elementsToMove = builder->createSub(length, start, "elems.tmp");
	elementsToMove = builder->createSub(elementsToMove, count, "elems.to.move");

	// Calculate byte size to move
	mir::MIRValue *byteSize = builder->createMul(elementsToMove, builder->getInt64(elemSize), "byte.size");

	// Source pointer: arr + (start + count) * elemSize
	mir::MIRValue *srcIndex = builder->createAdd(start, count, "src.idx");
	mir::MIRValue *srcPtr = builder->createArrayGEP(elemType, dataPtr, srcIndex, "src.ptr");

	// Dest pointer: arr + start * elemSize
	mir::MIRValue *destPtr = builder->createArrayGEP(elemType, dataPtr, start, "dest.ptr");

	// Call memmove (handles overlapping regions)
	mir::MIRFunction *memmoveFunc = getOrDeclareFunction("memmove", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memmoveFunc, {destPtr, srcPtr, byteSize}, "");

	return builder->getInt64(0);
}
