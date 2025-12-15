#include "managed_array_builder.h"
#include "../codegen_mir.h"

ManagedArrayBuilder::ManagedArrayBuilder(MIRCodeGenerator &gen, const std::string &elementType)
	: gen_(gen), module_(gen.getModule()), elementType_(elementType) {
}

// ========== Type Accessors ==========

mir::MIRType *ManagedArrayBuilder::getManagedArrayDataType() {
	return gen_.getOrCreateManagedArrayDataType(elementType_);
}

mir::MIRType *ManagedArrayBuilder::getArrayStructType() {
	return gen_.getOrCreateArrayStructType(elementType_);
}

mir::MIRType *ManagedArrayBuilder::getElementMIRType() {
	return gen_.getTypeFromString(elementType_);
}

int ManagedArrayBuilder::getElementSize() {
	mir::MIRType *elemType = getElementMIRType();
	return static_cast<int>(elemType->getSizeInBytes());
}

// ========== Field Extraction ==========

mir::MIRValue *ManagedArrayBuilder::getDataPtr(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *bufferPtrPtr = gen_.builder->createStructGEP(managedType, managedPtr, 0, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, name);
}

mir::MIRValue *ManagedArrayBuilder::getLength(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(managedType, managedPtr, 1, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getInt64(), lenPtr, name);
}

mir::MIRValue *ManagedArrayBuilder::getCapacity(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *capPtr = gen_.builder->createStructGEP(managedType, managedPtr, 2, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getInt64(), capPtr, name);
}

mir::MIRValue *ManagedArrayBuilder::isHeapAllocated(mir::MIRValue *managedPtr, const std::string &name) {
	// Capacity values:
	//   0 = stack allocated (no heap ownership)
	//  >0 = heap allocated with ownership
	mir::MIRValue *cap = getCapacity(managedPtr, "cap.check");
	mir::MIRValue *zero = gen_.builder->getInt64(0);
	return gen_.builder->createICmpSGT(cap, zero, name);
}

// ========== Field Modification ==========

void ManagedArrayBuilder::setDataPtr(mir::MIRValue *managedPtr, mir::MIRValue *dataPtr) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *bufferPtrPtr = gen_.builder->createStructGEP(managedType, managedPtr, 0, "buffer.ptr.ptr");
	gen_.builder->createStore(dataPtr, bufferPtrPtr);
}

void ManagedArrayBuilder::setLength(mir::MIRValue *managedPtr, mir::MIRValue *length) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(managedType, managedPtr, 1, "len.ptr");
	gen_.builder->createStore(length, lenPtr);
}

void ManagedArrayBuilder::setCapacity(mir::MIRValue *managedPtr, mir::MIRValue *capacity) {
	mir::MIRType *managedType = getManagedArrayDataType();
	mir::MIRValue *capPtr = gen_.builder->createStructGEP(managedType, managedPtr, 2, "cap.ptr");
	gen_.builder->createStore(capacity, capPtr);
}

// ========== Allocation ==========

mir::MIRValue *ManagedArrayBuilder::allocateBuffer(mir::MIRValue *numElements, const std::string &tag) {
	mir::MIRValue *tagStr = createTag(".__tag.alloc." + tag, tag);

	int elemSize = getElementSize();

	// Calculate byte size: numElements * elemSize
	mir::MIRValue *numElements64 = gen_.builder->createSExt(numElements, mir::MIRType::getInt64(), "nelems64");
	mir::MIRValue *elemSize64 = gen_.builder->getInt64(elemSize);
	mir::MIRValue *byteSize = gen_.builder->createMul(numElements64, elemSize64, "byte.size");

	mir::MIRFunction *allocFunc =
		getOrDeclareFunction("_managed_array_alloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64(), mir::MIRType::getPtr()});

	return gen_.builder->createCall(allocFunc, {byteSize, tagStr}, "arr.buffer.alloc");
}

mir::MIRValue *ManagedArrayBuilder::allocateManagedStruct(const std::string &name) {
	return gen_.builder->createAlloca(getManagedArrayDataType(), name);
}

mir::MIRValue *ManagedArrayBuilder::allocateArrayStruct(const std::string &name) {
	return gen_.builder->createAlloca(getArrayStructType(), name);
}

// ========== Struct Population ==========

void ManagedArrayBuilder::populateManagedStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
												mir::MIRValue *capacity) {
	mir::MIRType *managedType = getManagedArrayDataType();

	mir::MIRValue *bufferPtrPtr = gen_.builder->createStructGEP(managedType, structPtr, 0, "buffer.ptr.ptr");
	gen_.builder->createStore(dataPtr, bufferPtrPtr);

	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(managedType, structPtr, 1, "len.ptr");
	gen_.builder->createStore(length, lenPtr);

	mir::MIRValue *capPtr = gen_.builder->createStructGEP(managedType, structPtr, 2, "cap.ptr");
	gen_.builder->createStore(capacity, capPtr);
}

// ========== Element Access ==========

mir::MIRValue *ManagedArrayBuilder::getElementPtr(mir::MIRValue *dataPtr, mir::MIRValue *index, const std::string &name) {
	mir::MIRType *elemType = getElementMIRType();
	mir::MIRValue *index64 = gen_.builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	return gen_.builder->createArrayGEP(elemType, dataPtr, index64, name);
}

// ========== Reference Counting ==========

void ManagedArrayBuilder::emitReleaseIfHeap(mir::MIRValue *managedPtr, const std::string &tag) {
	// Check if heap allocated (capacity > 0)
	mir::MIRValue *isHeap = isHeapAllocated(managedPtr, "release.check");

	// Create basic blocks for the conditional release
	mir::MIRBasicBlock *releaseBlock = gen_.builder->createBasicBlock("arr.release.heap");
	mir::MIRBasicBlock *continueBlock = gen_.builder->createBasicBlock("arr.release.done");

	gen_.builder->createCondBr(isHeap, releaseBlock, continueBlock);

	// Release block - call _managed_array_release
	gen_.builder->setInsertPoint(releaseBlock);
	mir::MIRValue *dataPtr = getDataPtr(managedPtr, "release.data");
	emitRelease(dataPtr, tag);
	gen_.builder->createBr(continueBlock);

	// Continue
	gen_.builder->setInsertPoint(continueBlock);
}

void ManagedArrayBuilder::emitRetainIfHeap(mir::MIRValue *managedPtr) {
	// Check if heap allocated (capacity > 0)
	mir::MIRValue *isHeap = isHeapAllocated(managedPtr, "retain.check");

	// Create basic blocks for the conditional retain
	mir::MIRBasicBlock *retainBlock = gen_.builder->createBasicBlock("arr.retain.heap");
	mir::MIRBasicBlock *continueBlock = gen_.builder->createBasicBlock("arr.retain.done");

	gen_.builder->createCondBr(isHeap, retainBlock, continueBlock);

	// Retain block - call _managed_array_retain
	gen_.builder->setInsertPoint(retainBlock);
	mir::MIRValue *dataPtr = getDataPtr(managedPtr, "retain.data");
	emitRetain(dataPtr);
	gen_.builder->createBr(continueBlock);

	// Continue
	gen_.builder->setInsertPoint(continueBlock);
}

void ManagedArrayBuilder::emitRelease(mir::MIRValue *dataPtr, const std::string &tag) {
	mir::MIRValue *tagStr = createTag(".__tag.free." + tag, tag);

	mir::MIRFunction *releaseFunc =
		getOrDeclareFunction("_managed_array_release", mir::MIRType::getVoid(), {mir::MIRType::getPtr(), mir::MIRType::getPtr()});

	gen_.builder->createCall(releaseFunc, {dataPtr, tagStr});
}

void ManagedArrayBuilder::emitRetain(mir::MIRValue *dataPtr) {
	mir::MIRFunction *retainFunc =
		getOrDeclareFunction("_managed_array_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});

	gen_.builder->createCall(retainFunc, {dataPtr});
}

// ========== Scope Tracking ==========

void ManagedArrayBuilder::trackHeapArray(const std::string &name, mir::MIRValue *structAlloca,
										 const std::string &elementType, bool isDynamic) {
	if (!gen_.scopeStack.empty()) {
		gen_.scopeStack.back().heapAllocatedArrays.push_back({name, structAlloca, elementType, isDynamic});
	}
}

// ========== Utility ==========

mir::MIRValue *ManagedArrayBuilder::createTag(const std::string &name, const std::string &content) {
	return module_->createGlobalString(name, content);
}

mir::MIRFunction *ManagedArrayBuilder::getOrDeclareFunction(const std::string &name, mir::MIRType *returnType,
															const std::vector<mir::MIRType *> &paramTypes) {
	return gen_.getOrDeclareFunction(name, returnType, paramTypes);
}
