#include "../codegen_mir.h"
#include "intrinsic_codegen.h"

// Helper type accessor implementations
mir::MIRType* MIRCodeGenerator::getOrCreateManagedStringType() {
	mir::MIRType* managedStringType = structTypes["__ManagedStringData"];
	if (!managedStringType) {
		mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
		managedStringType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["__ManagedStringData"] = managedStringType;
	}
	return managedStringType;
}

mir::MIRType* MIRCodeGenerator::getOrCreateUnsizedArrayType() {
	mir::MIRType* unsizedArrayType = structTypes["__unsized_array_byte"];
	if (!unsizedArrayType) {
		unsizedArrayType = module->getOrCreateStructType(
			"__unsized_array_byte",
			{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
		structTypes["__unsized_array_byte"] = unsizedArrayType;
	}
	return unsizedArrayType;
}

mir::MIRType* MIRCodeGenerator::getOrCreateCstringType() {
	mir::MIRType* cstringType = structTypes["cstring"];
	if (!cstringType) {
		cstringType = module->getOrCreateStructType(
			"cstring",
			{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
		structTypes["cstring"] = cstringType;
	}
	return cstringType;
}

mir::MIRType* MIRCodeGenerator::getOrCreateSubstringType() {
	mir::MIRType* substringType = structTypes["substring"];
	if (!substringType) {
		substringType = module->getOrCreateStructType(
			"substring",
			{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["substring"] = substringType;
	}
	return substringType;
}

// Helper to get _ManagedString pointer from expression
mir::MIRValue* MIRCodeGenerator::getManagedStringPtr(ExprAST* arg) {
	// Ensure type is created for later use
	(void)getOrCreateManagedStringType();

	// Check if this is an implicit field access (just a field name like _managed)
	if (auto* varExpr = dynamic_cast<VariableExprAST*>(arg)) {
		// Check for implicit field access (field name from current struct)
		if (!currentReceiverType.empty()) {
			auto fieldsIt = structFields.find(currentReceiverType);
			if (fieldsIt != structFields.end()) {
				for (size_t i = 0; i < fieldsIt->second.size(); i++) {
					if (fieldsIt->second[i].first == varExpr->name) {
						// Found the field - get pointer to it via self
						mir::MIRValue* selfAlloca = namedValues["self"];
						if (selfAlloca) {
							mir::MIRValue* selfPtr;
							if (isStructParameter("self")) {
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}
							mir::MIRType* structType = structTypes[currentReceiverType];
							mir::MIRValue* managedFieldPtr = builder->createStructGEP(structType, selfPtr, static_cast<int>(i), varExpr->name + ".ptr");
							return builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, varExpr->name + ".val");
						}
					}
				}
			}
		}
		// Regular variable - return its value (should be a ptr)
		auto it = namedValues.find(varExpr->name);
		if (it != namedValues.end()) {
			return builder->createLoad(mir::MIRType::getPtr(), it->second, varExpr->name + ".val");
		}
	}
	// Check for member access like str._managed or _str._managed
	if (auto* memberExpr = dynamic_cast<MemberAccessExprAST*>(arg)) {
		mir::MIRValue* objectPtr = nullptr;
		std::string objectType;

		if (memberExpr->object) {
			objectPtr = generateExpr(memberExpr->object.get());
			// Need to infer type from expression
			objectType = variableTypes.count(memberExpr->objectName) ? variableTypes[memberExpr->objectName] : "";
		} else {
			auto allocaIt = namedValues.find(memberExpr->objectName);
			if (allocaIt != namedValues.end() && allocaIt->second != nullptr) {
				objectType = variableTypes[memberExpr->objectName];
				if (isStructParameter(memberExpr->objectName)) {
					objectPtr = builder->createLoad(mir::MIRType::getPtr(), allocaIt->second, "struct.ptr");
				} else {
					objectPtr = allocaIt->second;
				}
			} else if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == memberExpr->objectName) {
							mir::MIRValue* selfAlloca = namedValues["self"];
							if (selfAlloca) {
								mir::MIRValue* selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType* selfStructType = structTypes[currentReceiverType];
								objectPtr = builder->createStructGEP(selfStructType, selfPtr, static_cast<int>(i), memberExpr->objectName + ".field.ptr");
								objectType = fieldsIt->second[i].second;
								break;
							}
						}
					}
				}
			}
		}

		if (objectPtr) {
			auto fieldsIt = structFields.find(objectType);
			if (fieldsIt != structFields.end()) {
				for (size_t i = 0; i < fieldsIt->second.size(); i++) {
					if (fieldsIt->second[i].first == memberExpr->memberName) {
						mir::MIRType* structType = structTypes[objectType];
						mir::MIRValue* fieldPtr = builder->createStructGEP(structType, objectPtr, static_cast<int>(i), memberExpr->memberName + ".ptr");
						return builder->createLoad(mir::MIRType::getPtr(), fieldPtr, memberExpr->memberName + ".val");
					}
				}
			}
		}
	}
	// Fallback - generate expr and assume it's a ptr value
	return generateExpr(arg);
}

// Helper to get cstring struct pointer from expression
mir::MIRValue* MIRCodeGenerator::getCstringPtr(ExprAST* arg) {
	if (auto* varExpr = dynamic_cast<VariableExprAST*>(arg)) {
		if (varExpr->name == "self") {
			auto it = namedValues.find("self");
			if (it != namedValues.end()) {
				return builder->createLoad(mir::MIRType::getPtr(), it->second, "self.ptr");
			}
		}
		auto it = namedValues.find(varExpr->name);
		if (it != namedValues.end()) {
			return it->second;
		}
	}
	return generateExpr(arg);
}

// Helper to get substring struct pointer from expression
mir::MIRValue* MIRCodeGenerator::getSubstringPtr(ExprAST* arg) {
	if (auto* varExpr = dynamic_cast<VariableExprAST*>(arg)) {
		if (varExpr->name == "self") {
			auto it = namedValues.find("self");
			if (it != namedValues.end()) {
				return builder->createLoad(mir::MIRType::getPtr(), it->second, "self.ptr");
			}
		}
		auto it = namedValues.find(varExpr->name);
		if (it != namedValues.end()) {
			// Struct parameters are passed by pointer - load the pointer first
			if (isStructParameter(varExpr->name)) {
				return builder->createLoad(mir::MIRType::getPtr(), it->second, varExpr->name + ".ptr");
			}
			return it->second;
		}
	}
	return generateExpr(arg);
}

// =============================================================================
// String Intrinsics
// =============================================================================

mir::MIRValue* MIRCodeGenerator::intrinsic_string_len(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());
	mir::MIRValue* lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "managed._len");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_byte_at(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());
	mir::MIRValue* index = generateExpr(callExpr->args[1].get());

	mir::MIRValue* bufferFieldPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "managed._buffer");
	mir::MIRValue* bufferPtrPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "buffer.ptr.ptr");
	mir::MIRValue* bufferPtr = builder->createLoad(mir::MIRType::getPtr(), bufferPtrPtr, "buffer.ptr");

	mir::MIRValue* index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue* bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), bufferPtr, index64, "byte.ptr");
	return builder->createLoad(mir::MIRType::getInt8(), bytePtr, "byte");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_slice(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRType* substringType = getOrCreateSubstringType();

	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());
	mir::MIRValue* startIdx = generateExpr(callExpr->args[1].get());
	mir::MIRValue* endIdx = generateExpr(callExpr->args[2].get());

	mir::MIRValue* newLen = builder->createSub(endIdx, startIdx, "slice.len");

	mir::MIRValue* srcBufferPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue* srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufferPtr, 0, "src.ptr.ptr");
	mir::MIRValue* srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");
	mir::MIRValue* start64 = builder->createSExt(startIdx, mir::MIRType::getInt64(), "start64");
	mir::MIRValue* slicePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, start64, "slice.ptr");

	mir::MIRValue* newSub = builder->createAlloca(substringType, "slice.substring");

	mir::MIRValue* dstParentPtr = builder->createStructGEP(substringType, newSub, 0, "dst._parentManaged");
	builder->createStore(managedPtr, dstParentPtr);

	mir::MIRValue* dstPtrPtr = builder->createStructGEP(substringType, newSub, 1, "dst._ptr");
	builder->createStore(slicePtr, dstPtrPtr);

	mir::MIRValue* dstLenPtr = builder->createStructGEP(substringType, newSub, 2, "dst._len");
	builder->createStore(newLen, dstLenPtr);

	mir::MIRValue* dstIterPosPtr = builder->createStructGEP(substringType, newSub, 3, "dst._iterPos");
	builder->createStore(builder->getInt32(0), dstIterPosPtr);

	// Retain parent if heap-allocated
	mir::MIRValue* parentCapPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "parent._capacity");
	mir::MIRValue* parentCap = builder->createLoad(mir::MIRType::getInt32(), parentCapPtr, "parent.cap");
	mir::MIRValue* isHeap = builder->createICmpSGT(parentCap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock* retainBlock = builder->createBasicBlock("slice.retain");
	mir::MIRBasicBlock* continueBlock = builder->createBasicBlock("slice.continue");
	builder->createCondBr(isHeap, retainBlock, continueBlock);

	builder->setInsertPoint(retainBlock);
	mir::MIRFunction* retainFunc = getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(retainFunc, {srcPtr});
	builder->createBr(continueBlock);

	builder->setInsertPoint(continueBlock);
	return builder->createLoad(substringType, newSub, "slice.result");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_parent_managed(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());

	// Get _parentManaged field (field 0)
	mir::MIRValue* parentPtr = builder->createStructGEP(substringType, subPtr, 0, "sub._parentManaged");
	return builder->createLoad(mir::MIRType::getPtr(), parentPtr, "parent.managed");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_byte_offset(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());

	// Get _parentManaged field (field 0)
	mir::MIRValue* parentManagedPtr = builder->createStructGEP(substringType, subPtr, 0, "sub._parentManaged");
	mir::MIRValue* parentManaged = builder->createLoad(mir::MIRType::getPtr(), parentManagedPtr, "parent.managed");

	// Get parent's data pointer from _buffer field (field 0)
	mir::MIRValue* parentBufPtr = builder->createStructGEP(managedStringType, parentManaged, 0, "parent._buffer");
	mir::MIRValue* parentDataPtrPtr = builder->createStructGEP(unsizedArrayType, parentBufPtr, 0, "parent.ptr.ptr");
	mir::MIRValue* parentDataPtr = builder->createLoad(mir::MIRType::getPtr(), parentDataPtrPtr, "parent.data.ptr");

	// Get substring's _ptr field (field 1)
	mir::MIRValue* subDataPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "sub._ptr");
	mir::MIRValue* subDataPtr = builder->createLoad(mir::MIRType::getPtr(), subDataPtrPtr, "sub.data.ptr");

	// Calculate byte offset: subDataPtr - parentDataPtr
	mir::MIRValue* parentInt = builder->createPtrToInt(parentDataPtr, mir::MIRType::getInt64(), "parent.int");
	mir::MIRValue* subInt = builder->createPtrToInt(subDataPtr, mir::MIRType::getInt64(), "sub.int");
	mir::MIRValue* offset64 = builder->createSub(subInt, parentInt, "offset64");
	return builder->createTrunc(offset64, mir::MIRType::getInt32(), "offset");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_concat(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* m1Ptr = getManagedStringPtr(callExpr->args[0].get());
	mir::MIRValue* m2Ptr = getManagedStringPtr(callExpr->args[1].get());

	mir::MIRValue* len1Ptr = builder->createStructGEP(managedStringType, m1Ptr, 1, "m1._len");
	mir::MIRValue* len1 = builder->createLoad(mir::MIRType::getInt32(), len1Ptr, "len1");
	mir::MIRValue* len2Ptr = builder->createStructGEP(managedStringType, m2Ptr, 1, "m2._len");
	mir::MIRValue* len2 = builder->createLoad(mir::MIRType::getInt32(), len2Ptr, "len2");

	mir::MIRValue* totalLen = builder->createAdd(len1, len2, "total.len");
	mir::MIRValue* totalLenPlus1 = builder->createAdd(totalLen, builder->getInt32(1), "total.len.plus1");
	mir::MIRValue* totalLenPlus1_64 = builder->createSExt(totalLenPlus1, mir::MIRType::getInt64(), "total64");

	mir::MIRValue* buf1Ptr = builder->createStructGEP(managedStringType, m1Ptr, 0, "m1._buffer");
	mir::MIRValue* ptr1Ptr = builder->createStructGEP(unsizedArrayType, buf1Ptr, 0, "ptr1.ptr");
	mir::MIRValue* ptr1 = builder->createLoad(mir::MIRType::getPtr(), ptr1Ptr, "ptr1");

	mir::MIRValue* buf2Ptr = builder->createStructGEP(managedStringType, m2Ptr, 0, "m2._buffer");
	mir::MIRValue* ptr2Ptr = builder->createStructGEP(unsizedArrayType, buf2Ptr, 0, "ptr2.ptr");
	mir::MIRValue* ptr2 = builder->createLoad(mir::MIRType::getPtr(), ptr2Ptr, "ptr2");

	mir::MIRFunction* mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue* newBuffer = builder->createCall(mallocFunc, {totalLenPlus1_64}, "concat.buffer");

	mir::MIRFunction* memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
		{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue* len1_64 = builder->createSExt(len1, mir::MIRType::getInt64(), "len1_64");
	builder->createCall(memcpyFunc, {newBuffer, ptr1, len1_64});

	mir::MIRValue* destOffset = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {len1_64}, "dest.offset");
	mir::MIRValue* len2_64 = builder->createSExt(len2, mir::MIRType::getInt64(), "len2_64");
	builder->createCall(memcpyFunc, {destOffset, ptr2, len2_64});

	mir::MIRValue* totalLen64 = builder->createSExt(totalLen, mir::MIRType::getInt64(), "total_len64_for_null");
	mir::MIRValue* nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {totalLen64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	mir::MIRValue* managedSize = builder->getInt64(32);
	mir::MIRValue* newManaged = builder->createCall(mallocFunc, {managedSize}, "concat.managed");

	mir::MIRValue* dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue* dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue* dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(totalLen, dstBufLenPtr);

	mir::MIRValue* dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(totalLen, dstLenPtr);

	mir::MIRValue* dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(totalLen, dstCapPtr);

	return newManaged;
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_make_unique(CallExprAST* callExpr) {
	// This intrinsic ensures a string has exclusive ownership before mutation.
	// For heap strings (capacity > 0), it calls the runtime to check refcount and copy if needed.
	// For constant strings (capacity == 0), it allocates a new heap buffer and copies the data.
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());

	mir::MIRValue* lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
	mir::MIRValue* len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
	mir::MIRValue* capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue* cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");

	mir::MIRValue* isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock* heapBlock = builder->createBasicBlock("make_unique.heap");
	mir::MIRBasicBlock* constBlock = builder->createBasicBlock("make_unique.const");
	mir::MIRBasicBlock* mergeBlock = builder->createBasicBlock("make_unique.merge");

	builder->createCondBr(isHeap, heapBlock, constBlock);

	// Heap path: call runtime to make unique (handles refcount check)
	builder->setInsertPoint(heapBlock);
	mir::MIRValue* srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue* srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
	mir::MIRValue* srcDataPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.data.ptr");

	mir::MIRValue* tag = module->createGlobalString(".__tag.str.cow", "string COW");
	mir::MIRFunction* makeUniqueFunc = getOrDeclareFunction("_managed_string_make_unique",
		mir::MIRType::getPtr(), {mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
	mir::MIRValue* newDataPtr = builder->createCall(makeUniqueFunc, {srcDataPtr, len, tag}, "unique.data");

	builder->createStore(newDataPtr, srcPtrPtr);
	builder->createBr(mergeBlock);

	// Const path: allocate new heap buffer and copy constant data
	builder->setInsertPoint(constBlock);
	mir::MIRValue* constBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "const._buffer");
	mir::MIRValue* constPtrPtr = builder->createStructGEP(unsizedArrayType, constBufPtr, 0, "const.ptr.ptr");
	mir::MIRValue* constDataPtr = builder->createLoad(mir::MIRType::getPtr(), constPtrPtr, "const.data.ptr");

	// Allocate new buffer: len + 1 for null terminator + 8 for refcount header
	mir::MIRValue* allocSize = builder->createAdd(len, builder->getInt32(9), "alloc.size");
	mir::MIRValue* allocSize64 = builder->createSExt(allocSize, mir::MIRType::getInt64(), "alloc.size64");
	mir::MIRFunction* allocFunc = getOrDeclareFunction("_managed_string_alloc",
		mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue* newBufPtr = builder->createCall(allocFunc, {allocSize64}, "new.buf");

	// Initialize refcount to 1 (stored at offset 0)
	builder->createStore(builder->getInt64(1), newBufPtr);

	// Get pointer to data (after 8-byte refcount header)
	mir::MIRValue* eight = builder->getInt64(8);
	mir::MIRValue* newDataStart = builder->createArrayGEP(mir::MIRType::getInt8(), newBufPtr, eight, "new.data");

	// Copy constant data to new buffer
	mir::MIRValue* len64 = builder->createSExt(len, mir::MIRType::getInt64(), "len64");
	mir::MIRFunction* memcpyFunc = getOrDeclareFunction("memcpy",
		mir::MIRType::getPtr(), {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memcpyFunc, {newDataStart, constDataPtr, len64}, "");

	// Null-terminate
	mir::MIRValue* nullPos = builder->createArrayGEP(mir::MIRType::getInt8(), newDataStart, len64, "null.pos");
	builder->createStore(builder->getInt8(0), nullPos);

	// Update managed string to point to new heap buffer
	builder->createStore(newDataStart, constPtrPtr);

	// Set capacity to len+1 to indicate heap allocation
	mir::MIRValue* newCap = builder->createAdd(len, builder->getInt32(1), "new.cap");
	builder->createStore(newCap, capPtr);

	builder->createBr(mergeBlock);

	builder->setInsertPoint(mergeBlock);
	return managedPtr;
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_set_byte(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());
	mir::MIRValue* index = generateExpr(callExpr->args[1].get());
	mir::MIRValue* value = generateExpr(callExpr->args[2].get());

	mir::MIRValue* bufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "managed._buffer");
	mir::MIRValue* ptrPtr = builder->createStructGEP(unsizedArrayType, bufPtr, 0, "buffer.ptr.ptr");
	mir::MIRValue* dataPtr = builder->createLoad(mir::MIRType::getPtr(), ptrPtr, "data.ptr");

	mir::MIRValue* index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue* bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), dataPtr, index64, "byte.ptr");

	mir::MIRValue* byteVal = value;
	if (value->type->kind != mir::MIRTypeKind::Int8) {
		byteVal = builder->createTrunc(value, mir::MIRType::getInt8(), "byte.val");
	}
	builder->createStore(byteVal, bytePtr);

	return builder->getNull();
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_get_refcount(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());

	mir::MIRValue* capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue* cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");
	mir::MIRValue* isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock* heapBlock = builder->createBasicBlock("refcount.heap");
	mir::MIRBasicBlock* constBlock = builder->createBasicBlock("refcount.const");
	mir::MIRBasicBlock* mergeBlock = builder->createBasicBlock("refcount.merge");

	builder->createCondBr(isHeap, heapBlock, constBlock);

	builder->setInsertPoint(heapBlock);
	mir::MIRValue* srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue* srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
	mir::MIRValue* dataPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "data.ptr");

	mir::MIRValue* headerPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr, {builder->getInt64(-8)}, "header.ptr");
	mir::MIRValue* heapRefcount = builder->createLoad(mir::MIRType::getInt32(), headerPtr, "heap.refcount");
	builder->createBr(mergeBlock);

	builder->setInsertPoint(constBlock);
	mir::MIRValue* constRefcount = builder->getInt32(-1);
	builder->createBr(mergeBlock);

	builder->setInsertPoint(mergeBlock);
	mir::MIRValue* refcount = builder->createPhi(mir::MIRType::getInt32(), "refcount");
	builder->addPhiIncoming(refcount, heapRefcount, heapBlock);
	builder->addPhiIncoming(refcount, constRefcount, constBlock);

	return refcount;
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_to_cstring(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRType* cstringType = getOrCreateCstringType();

	mir::MIRValue* managedPtr = getManagedStringPtr(callExpr->args[0].get());

	mir::MIRValue* bufPtrField = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue* bufPtrPtr = builder->createStructGEP(unsizedArrayType, bufPtrField, 0, "src.ptr.ptr");
	mir::MIRValue* dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufPtrPtr, "data.ptr");

	mir::MIRValue* lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
	mir::MIRValue* len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	mir::MIRValue* capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue* cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");
	mir::MIRValue* isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock* retainBlock = builder->createBasicBlock("cstring.retain");
	mir::MIRBasicBlock* continueBlock = builder->createBasicBlock("cstring.continue");
	builder->createCondBr(isHeap, retainBlock, continueBlock);

	builder->setInsertPoint(retainBlock);
	mir::MIRFunction* retainFunc = getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(retainFunc, {dataPtr});
	builder->createBr(continueBlock);

	builder->setInsertPoint(continueBlock);

	mir::MIRValue* cstringAlloc = builder->createAlloca(cstringType, "cstring.alloc");

	mir::MIRValue* cstringDataPtr = builder->createStructGEP(cstringType, cstringAlloc, 0, "cstring.data");
	builder->createStore(dataPtr, cstringDataPtr);

	mir::MIRValue* cstringLenPtr = builder->createStructGEP(cstringType, cstringAlloc, 1, "cstring.len");
	builder->createStore(len, cstringLenPtr);

	mir::MIRValue* cstringManagedPtr = builder->createStructGEP(cstringType, cstringAlloc, 2, "cstring.managed");
	builder->createStore(managedPtr, cstringManagedPtr);

	return builder->createLoad(cstringType, cstringAlloc, "cstring.result");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_string_from_chars(CallExprAST* callExpr) {
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* srcBuffer = generateExpr(callExpr->args[0].get());
	mir::MIRValue* length = generateExpr(callExpr->args[1].get());

	mir::MIRValue* lengthPlus1 = builder->createAdd(length, builder->getInt32(1), "len.plus1");
	mir::MIRValue* lengthPlus1_64 = builder->createSExt(lengthPlus1, mir::MIRType::getInt64(), "len64");
	mir::MIRFunction* mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue* newBuffer = builder->createCall(mallocFunc, {lengthPlus1_64}, "fromchars.buffer");

	mir::MIRFunction* memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
		{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue* length_64 = builder->createSExt(length, mir::MIRType::getInt64(), "len_64");
	builder->createCall(memcpyFunc, {newBuffer, srcBuffer, length_64});

	mir::MIRValue* nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {length_64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	mir::MIRValue* managedSize = builder->getInt64(32);
	mir::MIRValue* newManaged = builder->createCall(mallocFunc, {managedSize}, "fromchars.managed");

	mir::MIRValue* dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue* dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue* dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(length, dstBufLenPtr);

	mir::MIRValue* dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(length, dstLenPtr);

	mir::MIRValue* dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(length, dstCapPtr);

	return newManaged;
}

// =============================================================================
// Cstring Intrinsics
// =============================================================================

mir::MIRValue* MIRCodeGenerator::intrinsic_cstring_len(CallExprAST* callExpr) {
	mir::MIRType* cstringType = getOrCreateCstringType();
	mir::MIRValue* csPtr = getCstringPtr(callExpr->args[0].get());
	mir::MIRValue* lenPtr = builder->createStructGEP(cstringType, csPtr, 1, "cstring.len.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "cstring.len");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_cstring_write_stdout(CallExprAST* callExpr) {
	mir::MIRType* cstringType = getOrCreateCstringType();
	mir::MIRValue* csPtr = getCstringPtr(callExpr->args[0].get());

	mir::MIRValue* dataPtr = builder->createStructGEP(cstringType, csPtr, 0, "cstring.data.ptr");
	mir::MIRValue* data = builder->createLoad(mir::MIRType::getPtr(), dataPtr, "cstring.data");

	mir::MIRValue* lenPtr = builder->createStructGEP(cstringType, csPtr, 1, "cstring.len.ptr");
	mir::MIRValue* len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "cstring.len");

	mir::MIRFunction* writeStdout = getOrDeclareFunction(
		"write_stdout", mir::MIRType::getInt32(), {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
	return builder->createCall(writeStdout, {data, len}, "write.result");
}

// =============================================================================
// Substring Intrinsics
// =============================================================================

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_len(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());
	mir::MIRValue* lenPtr = builder->createStructGEP(substringType, subPtr, 2, "sub._len");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_byte_at(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());
	mir::MIRValue* index = generateExpr(callExpr->args[1].get());

	mir::MIRValue* ptrPtr = builder->createStructGEP(substringType, subPtr, 1, "sub._ptr");
	mir::MIRValue* dataPtr = builder->createLoad(mir::MIRType::getPtr(), ptrPtr, "data.ptr");

	mir::MIRValue* index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue* bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), dataPtr, index64, "byte.ptr");
	return builder->createLoad(mir::MIRType::getInt8(), bytePtr, "byte");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_iter_pos(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());
	mir::MIRValue* iterPosPtr = builder->createStructGEP(substringType, subPtr, 3, "sub._iterPos");
	return builder->createLoad(mir::MIRType::getInt32(), iterPosPtr, "iterPos");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_with_iter_pos(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());
	mir::MIRValue* newPos = generateExpr(callExpr->args[1].get());

	mir::MIRValue* newSub = builder->createAlloca(substringType, "new.substring");

	// Copy all fields from source
	for (int i = 0; i < 4; i++) {
		mir::MIRValue* srcField = builder->createStructGEP(substringType, subPtr, i, "src.field" + std::to_string(i));
		mir::MIRValue* dstField = builder->createStructGEP(substringType, newSub, i, "dst.field" + std::to_string(i));
		mir::MIRType* fieldType = (i < 2) ? mir::MIRType::getPtr() : mir::MIRType::getInt32();
		mir::MIRValue* val = builder->createLoad(fieldType, srcField, "val" + std::to_string(i));
		builder->createStore(val, dstField);
	}

	// Set new iter position
	mir::MIRValue* iterPosPtr = builder->createStructGEP(substringType, newSub, 3, "dst._iterPos");
	builder->createStore(newPos, iterPosPtr);

	return builder->createLoad(substringType, newSub, "new.substring.result");
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_to_string(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRType* managedStringType = getOrCreateManagedStringType();
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());

	mir::MIRValue* srcPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "src._ptr");
	mir::MIRValue* srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");
	mir::MIRValue* lenPtr = builder->createStructGEP(substringType, subPtr, 2, "src._len");
	mir::MIRValue* len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	mir::MIRValue* lenPlus1 = builder->createAdd(len, builder->getInt32(1), "len.plus1");
	mir::MIRValue* lenPlus1_64 = builder->createSExt(lenPlus1, mir::MIRType::getInt64(), "len64");
	mir::MIRFunction* mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue* newBuffer = builder->createCall(mallocFunc, {lenPlus1_64}, "tostring.buffer");

	mir::MIRFunction* memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
		{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue* len_64 = builder->createSExt(len, mir::MIRType::getInt64(), "len_64");
	builder->createCall(memcpyFunc, {newBuffer, srcPtr, len_64});

	mir::MIRValue* nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {len_64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	mir::MIRValue* managedSize = builder->getInt64(32);
	mir::MIRValue* newManaged = builder->createCall(mallocFunc, {managedSize}, "tostring.managed");

	mir::MIRValue* dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue* dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue* dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(len, dstBufLenPtr);

	mir::MIRValue* dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(len, dstLenPtr);

	mir::MIRValue* dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(len, dstCapPtr);

	return newManaged;
}

mir::MIRValue* MIRCodeGenerator::intrinsic_substring_slice(CallExprAST* callExpr) {
	mir::MIRType* substringType = getOrCreateSubstringType();
	mir::MIRType* managedStringType = getOrCreateManagedStringType();

	mir::MIRValue* subPtr = getSubstringPtr(callExpr->args[0].get());
	mir::MIRValue* startIdx = generateExpr(callExpr->args[1].get());
	mir::MIRValue* endIdx = generateExpr(callExpr->args[2].get());

	mir::MIRValue* newLen = builder->createSub(endIdx, startIdx, "slice.len");

	mir::MIRValue* srcPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "src._ptr");
	mir::MIRValue* srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");
	mir::MIRValue* start64 = builder->createSExt(startIdx, mir::MIRType::getInt64(), "start64");
	mir::MIRValue* slicePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, start64, "slice.ptr");

	mir::MIRValue* parentPtr = builder->createStructGEP(substringType, subPtr, 0, "src._parentManaged");
	mir::MIRValue* parentManaged = builder->createLoad(mir::MIRType::getPtr(), parentPtr, "parent.managed");

	mir::MIRValue* newSub = builder->createAlloca(substringType, "slice.substring");

	mir::MIRValue* dstParentPtr = builder->createStructGEP(substringType, newSub, 0, "dst._parentManaged");
	builder->createStore(parentManaged, dstParentPtr);

	mir::MIRValue* dstPtrPtr = builder->createStructGEP(substringType, newSub, 1, "dst._ptr");
	builder->createStore(slicePtr, dstPtrPtr);

	mir::MIRValue* dstLenPtr = builder->createStructGEP(substringType, newSub, 2, "dst._len");
	builder->createStore(newLen, dstLenPtr);

	mir::MIRValue* dstIterPosPtr = builder->createStructGEP(substringType, newSub, 3, "dst._iterPos");
	builder->createStore(builder->getInt32(0), dstIterPosPtr);

	// Retain parent if heap-allocated
	mir::MIRValue* parentCapPtr = builder->createStructGEP(managedStringType, parentManaged, 2, "parent._capacity");
	mir::MIRValue* parentCap = builder->createLoad(mir::MIRType::getInt32(), parentCapPtr, "parent.cap");
	mir::MIRValue* isHeap = builder->createICmpSGT(parentCap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock* retainBlock = builder->createBasicBlock("slice.retain");
	mir::MIRBasicBlock* continueBlock = builder->createBasicBlock("slice.continue");
	builder->createCondBr(isHeap, retainBlock, continueBlock);

	builder->setInsertPoint(retainBlock);
	mir::MIRType* unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRValue* parentBufPtr = builder->createStructGEP(managedStringType, parentManaged, 0, "parent._buffer");
	mir::MIRValue* parentDataPtrPtr = builder->createStructGEP(unsizedArrayType, parentBufPtr, 0, "parent.ptr.ptr");
	mir::MIRValue* parentDataPtr = builder->createLoad(mir::MIRType::getPtr(), parentDataPtrPtr, "parent.data.ptr");
	mir::MIRFunction* retainFunc = getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(retainFunc, {parentDataPtr});
	builder->createBr(continueBlock);

	builder->setInsertPoint(continueBlock);
	return builder->createLoad(substringType, newSub, "slice.result");
}
