#include "managed_string_builder.h"
#include "../codegen_mir.h"

ManagedStringBuilder::ManagedStringBuilder(MIRCodeGenerator &gen)
	: gen_(gen), module_(gen.getModule()) {
}

// ========== Type Accessors ==========

mir::MIRType *ManagedStringBuilder::getManagedStringType() {
	return gen_.getOrCreateManagedStringType();
}

mir::MIRType *ManagedStringBuilder::getSubstringType() {
	return gen_.getOrCreateSubstringType();
}

mir::MIRType *ManagedStringBuilder::getCstringType() {
	return gen_.getOrCreateCstringType();
}

// ========== Field Extraction ==========

mir::MIRValue *ManagedStringBuilder::getDataPtr(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedStringType();
	mir::MIRValue *dataPtrPtr = gen_.builder->createStructGEP(managedType, managedPtr, 0, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getPtr(), dataPtrPtr, name);
}

mir::MIRValue *ManagedStringBuilder::getLength(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedStringType();
	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(managedType, managedPtr, 1, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getInt32(), lenPtr, name);
}

mir::MIRValue *ManagedStringBuilder::getCapacity(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRType *managedType = getManagedStringType();
	mir::MIRValue *capPtr = gen_.builder->createStructGEP(managedType, managedPtr, 2, name + ".ptr");
	return gen_.builder->createLoad(mir::MIRType::getInt32(), capPtr, name);
}

mir::MIRValue *ManagedStringBuilder::isHeapAllocated(mir::MIRValue *managedPtr, const std::string &name) {
	mir::MIRValue *cap = getCapacity(managedPtr, "cap.check");
	mir::MIRValue *zero = gen_.builder->getInt32(0);
	return gen_.builder->createICmpSGE(cap, zero, name);
}

// ========== Allocation ==========

mir::MIRValue *ManagedStringBuilder::allocateBuffer(mir::MIRValue *capacity, const std::string &tag) {
	mir::MIRValue *tagStr = createTag(".__tag.alloc." + tag, tag);

	// Ensure capacity is i64 for _managed_string_alloc
	// Note: We always sign-extend to be safe since capacity should typically be i32
	mir::MIRValue *capacity64 = gen_.builder->createSExt(capacity, mir::MIRType::getInt64(), "cap64");

	mir::MIRFunction *allocFunc =
		getOrDeclareFunction("_managed_string_alloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64(), mir::MIRType::getPtr()});

	return gen_.builder->createCall(allocFunc, {capacity64, tagStr}, "buffer.alloc");
}

mir::MIRValue *ManagedStringBuilder::allocateManagedStruct(const std::string &name) {
	return gen_.builder->createAlloca(getManagedStringType(), name);
}

mir::MIRValue *ManagedStringBuilder::allocateCstringStruct(const std::string &name) {
	return gen_.builder->createAlloca(getCstringType(), name);
}

mir::MIRValue *ManagedStringBuilder::allocateSubstringStruct(const std::string &name) {
	return gen_.builder->createAlloca(getSubstringType(), name);
}

// ========== Struct Population ==========

void ManagedStringBuilder::populateManagedStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
												 mir::MIRValue *capacity) {
	mir::MIRType *managedType = getManagedStringType();

	mir::MIRValue *dataPtrPtr = gen_.builder->createStructGEP(managedType, structPtr, 0, "data.ptr.ptr");
	gen_.builder->createStore(dataPtr, dataPtrPtr);

	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(managedType, structPtr, 1, "len.ptr");
	gen_.builder->createStore(length, lenPtr);

	mir::MIRValue *capPtr = gen_.builder->createStructGEP(managedType, structPtr, 2, "cap.ptr");
	gen_.builder->createStore(capacity, capPtr);
}

void ManagedStringBuilder::populateCstringStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
												 mir::MIRValue *managedPtr) {
	mir::MIRType *cstringType = getCstringType();

	mir::MIRValue *dataPtrPtr = gen_.builder->createStructGEP(cstringType, structPtr, 0, "cstring.data.ptr");
	gen_.builder->createStore(dataPtr, dataPtrPtr);

	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(cstringType, structPtr, 1, "cstring.len.ptr");
	gen_.builder->createStore(length, lenPtr);

	mir::MIRValue *managedPtrPtr = gen_.builder->createStructGEP(cstringType, structPtr, 2, "cstring.managed.ptr");
	gen_.builder->createStore(managedPtr, managedPtrPtr);
}

void ManagedStringBuilder::populateSubstringStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr,
												   mir::MIRValue *parentManaged, mir::MIRValue *length,
												   mir::MIRValue *iterPos) {
	mir::MIRType *substringType = getSubstringType();

	mir::MIRValue *dataPtrPtr = gen_.builder->createStructGEP(substringType, structPtr, 0, "substr.data.ptr");
	gen_.builder->createStore(dataPtr, dataPtrPtr);

	mir::MIRValue *parentPtr = gen_.builder->createStructGEP(substringType, structPtr, 1, "substr.parent.ptr");
	gen_.builder->createStore(parentManaged, parentPtr);

	mir::MIRValue *lenPtr = gen_.builder->createStructGEP(substringType, structPtr, 2, "substr.len.ptr");
	gen_.builder->createStore(length, lenPtr);

	mir::MIRValue *iterPosPtr = gen_.builder->createStructGEP(substringType, structPtr, 3, "substr.iter.ptr");
	gen_.builder->createStore(iterPos, iterPosPtr);
}

// ========== Reference Counting ==========

void ManagedStringBuilder::emitReleaseIfHeap(mir::MIRValue *managedPtr, const std::string &tag) {
	// Check if heap allocated (capacity >= 0)
	mir::MIRValue *isHeap = isHeapAllocated(managedPtr, "release.check");

	// Create basic blocks for the conditional release
	mir::MIRBasicBlock *releaseBlock = gen_.builder->createBasicBlock("release.heap");
	mir::MIRBasicBlock *continueBlock = gen_.builder->createBasicBlock("release.done");

	gen_.builder->createCondBr(isHeap, releaseBlock, continueBlock);

	// Release block - call _managed_string_release
	gen_.builder->setInsertPoint(releaseBlock);
	mir::MIRValue *dataPtr = getDataPtr(managedPtr, "release.data");
	emitRelease(dataPtr, tag);
	gen_.builder->createBr(continueBlock);

	// Continue
	gen_.builder->setInsertPoint(continueBlock);
}

void ManagedStringBuilder::emitRetainIfHeap(mir::MIRValue *managedPtr) {
	// Check if heap allocated (capacity >= 0)
	mir::MIRValue *isHeap = isHeapAllocated(managedPtr, "retain.check");

	// Create basic blocks for the conditional retain
	mir::MIRBasicBlock *retainBlock = gen_.builder->createBasicBlock("retain.heap");
	mir::MIRBasicBlock *continueBlock = gen_.builder->createBasicBlock("retain.done");

	gen_.builder->createCondBr(isHeap, retainBlock, continueBlock);

	// Retain block - call _managed_string_retain
	gen_.builder->setInsertPoint(retainBlock);
	mir::MIRValue *dataPtr = getDataPtr(managedPtr, "retain.data");
	emitRetain(dataPtr);
	gen_.builder->createBr(continueBlock);

	// Continue
	gen_.builder->setInsertPoint(continueBlock);
}

void ManagedStringBuilder::emitRelease(mir::MIRValue *dataPtr, const std::string &tag) {
	mir::MIRValue *tagStr = createTag(".__tag.free." + tag, tag);

	mir::MIRFunction *releaseFunc =
		getOrDeclareFunction("_managed_string_release", mir::MIRType::getVoid(), {mir::MIRType::getPtr(), mir::MIRType::getPtr()});

	gen_.builder->createCall(releaseFunc, {dataPtr, tagStr});
}

void ManagedStringBuilder::emitRetain(mir::MIRValue *dataPtr) {
	mir::MIRFunction *retainFunc =
		getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});

	gen_.builder->createCall(retainFunc, {dataPtr});
}

// ========== Scope Tracking ==========

void ManagedStringBuilder::trackHeapString(const std::string &name, mir::MIRValue *dataPtrAlloca) {
	if (!gen_.scopeStack.empty()) {
		gen_.scopeStack.back().heapAllocatedStrings.push_back({name, dataPtrAlloca});
	}
}

void ManagedStringBuilder::trackCstring(const std::string &name, mir::MIRValue *cstringAlloca) {
	if (!gen_.scopeStack.empty()) {
		gen_.scopeStack.back().cstringAllocas.push_back({name, cstringAlloca});
	}
}

void ManagedStringBuilder::trackSubstring(const std::string &name, mir::MIRValue *substringAlloca) {
	if (!gen_.scopeStack.empty()) {
		gen_.scopeStack.back().substringAllocas.push_back({name, substringAlloca});
	}
}

// ========== Utility ==========

mir::MIRValue *ManagedStringBuilder::createTag(const std::string &name, const std::string &content) {
	return module_->createGlobalString(name, content);
}

mir::MIRFunction *ManagedStringBuilder::getOrDeclareFunction(const std::string &name, mir::MIRType *returnType,
															 const std::vector<mir::MIRType *> &paramTypes) {
	return gen_.getOrDeclareFunction(name, returnType, paramTypes);
}
