#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include "intrinsic_codegen.h"
#include "managed_string_builder.h"

// Helper type accessor implementations
mir::MIRType *MIRCodeGenerator::getOrCreateManagedStringType() {
	mir::MIRType *managedStringType = structTypes["__ManagedStringData"];
	if (!managedStringType) {
		mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();
		managedStringType = module->getOrCreateStructType(
			"__ManagedStringData",
			{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["__ManagedStringData"] = managedStringType;
	}
	return managedStringType;
}

mir::MIRType *MIRCodeGenerator::getOrCreateUnsizedArrayType() {
	mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
	if (!unsizedArrayType) {
		unsizedArrayType = module->getOrCreateStructType(
			"_ManagedArray_byte",
			{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
		structTypes["_ManagedArray_byte"] = unsizedArrayType;
	}
	return unsizedArrayType;
}

mir::MIRType *MIRCodeGenerator::getOrCreateCstringType() {
	mir::MIRType *cstringType = structTypes["cstring"];
	if (!cstringType) {
		cstringType = module->getOrCreateStructType(
			"cstring",
			{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
		structTypes["cstring"] = cstringType;
	}
	return cstringType;
}

mir::MIRType *MIRCodeGenerator::getOrCreateSubstringType() {
	mir::MIRType *substringType = structTypes["substring"];
	if (!substringType) {
		substringType = module->getOrCreateStructType(
			"substring",
			{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes["substring"] = substringType;
	}
	return substringType;
}

mir::MIRType *MIRCodeGenerator::getOrCreateManagedArrayDataType(const std::string &elementType) {
	// Create the internal __ManagedArrayData struct type for the given element type
	// Layout: { _buffer ptr, _len i32, _capacity i32 }
	// - _buffer: pointer to the actual array data
	// - _len: current number of elements
	// - _capacity: allocated capacity (0 for static arrays)
	std::string typeName = "__ManagedArrayData_" + elementType;
	mir::MIRType *dataType = structTypes[typeName];
	if (!dataType) {
		dataType = module->getOrCreateStructType(
			typeName,
			{mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt32()});
		structTypes[typeName] = dataType;
	}
	return dataType;
}

mir::MIRType *MIRCodeGenerator::getOrCreateArrayStructType(const std::string &elementType) {
	// Create the array<T> struct type as defined in stdlib/collections/array.maxon
	// Layout: { managed _ManagedArray<T>, iterIndex int }
	// where _ManagedArray<T> is { _buffer ptr, _len i32, _capacity i32 }
	std::string typeName = "array<" + elementType + ">";

	// Check if already exists (either created here or by instantiateGenericStruct)
	auto it = structTypes.find(typeName);
	if (it != structTypes.end()) {
		return it->second;
	}

	// Ensure the __ManagedArrayData type exists for the intrinsics
	(void)getOrCreateManagedArrayDataType(elementType);

	// Use the standard generic struct instantiation mechanism
	// This creates the correct nested layout and populates structFields correctly
	if (structDefinitions.find("array") != structDefinitions.end()) {
		std::map<std::string, std::string> typeBindings = {{"Element", elementType}};
		instantiateGenericStruct("array", typeBindings);

		// Now the type should exist in structTypes
		it = structTypes.find(typeName);
		if (it != structTypes.end()) {
			return it->second;
		}
	}

	// Fallback: if stdlib array not available, create the type manually
	// This maintains backward compatibility for cases where stdlib isn't loaded
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementType);
	mir::MIRType *arrayStructType = module->getOrCreateStructType(
		typeName,
		{managedArrayType, mir::MIRType::getInt32()});
	structTypes[typeName] = arrayStructType;
	structFields[typeName] = {
		{"managed", "_ManagedArray<" + elementType + ">"},
		{"iterIndex", "int"}};
	return arrayStructType;
}

// Helper to get _ManagedString pointer from expression
mir::MIRValue *MIRCodeGenerator::getManagedStringPtr(ExprAST *arg) {
	// Ensure type is created for later use
	(void)getOrCreateManagedStringType();

	// Check if this is an implicit field access (just a field name like _managed)
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg)) {
		// Check for implicit field access (field name from current struct)
		if (!currentReceiverType.empty()) {
			auto fieldsIt = structFields.find(currentReceiverType);
			if (fieldsIt != structFields.end()) {
				for (size_t i = 0; i < fieldsIt->second.size(); i++) {
					if (fieldsIt->second[i].first == varExpr->name) {
						// Found the field - get pointer to it via self
						mir::MIRValue *selfAlloca = namedValues["self"];
						if (selfAlloca) {
							mir::MIRValue *selfPtr;
							if (isStructParameter("self")) {
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}
							mir::MIRType *structType = structTypes[currentReceiverType];
							mir::MIRValue *managedFieldPtr = builder->createStructGEP(structType, selfPtr, static_cast<int>(i), varExpr->name + ".ptr");
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
	if (auto *memberExpr = dynamic_cast<MemberAccessExprAST *>(arg)) {
		mir::MIRValue *objectPtr = nullptr;
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
							mir::MIRValue *selfAlloca = namedValues["self"];
							if (selfAlloca) {
								mir::MIRValue *selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType *selfStructType = structTypes[currentReceiverType];
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
						mir::MIRType *structType = structTypes[objectType];
						mir::MIRValue *fieldPtr = builder->createStructGEP(structType, objectPtr, static_cast<int>(i), memberExpr->memberName + ".ptr");
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
mir::MIRValue *MIRCodeGenerator::getCstringPtr(ExprAST *arg) {
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg)) {
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
	// For call expressions and other non-variable expressions,
	// the result is a cstring value. We need to store it to a temp
	// alloca and return a pointer to it.
	mir::MIRValue *csValue = generateExpr(arg);
	mir::MIRType *cstringType = getOrCreateCstringType();
	mir::MIRValue *tempAlloca = builder->createAlloca(cstringType, "cstring.temp");
	builder->createStore(csValue, tempAlloca);
	return tempAlloca;
}

// Helper to get substring struct pointer from expression
mir::MIRValue *MIRCodeGenerator::getSubstringPtr(ExprAST *arg) {
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg)) {
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

// Helper to get array field info - handles both regular variables and implicit struct field access
// This is needed for array intrinsics like __array_len, __array_set_at, etc. when called from
// struct methods where the array is a field (e.g., _data in array<T>)
MIRCodeGenerator::ArrayFieldInfo MIRCodeGenerator::getArrayFieldInfo(ExprAST *arrayArg, int line, int column) {
	ArrayFieldInfo info = {};
	info.isStackArray = false;
	info.isStructField = false;
	info.fieldIndex = -1;

	std::string arrayVarName;
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
		arrayVarName = varExpr->name;
	} else {
		reportError("Array intrinsic requires a variable argument", line, column);
		return info;
	}

	// First, check if this is a regular variable (not a struct field)
	mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
	if (arrayAlloca) {
		// Regular variable found - get its type and allocas
		info.dataPtr = arrayAlloca;
		info.lengthAlloca = namedValues[arrayVarName + ".__length"];
		info.capacityAlloca = namedValues[arrayVarName + ".__capacity"];
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
						info.dataPtr = fieldPtr;	   // This is a pointer to the array field (which itself holds a data pointer)
						info.lengthAlloca = nullptr;   // Struct fields don't have hidden length allocas
						info.capacityAlloca = nullptr; // Struct fields don't have hidden capacity allocas

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

	reportError("Variable is not an array: " + arrayVarName, line, column);
	return info;
}

// =============================================================================
// String Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_string_len(CallExprAST *callExpr) {
	ManagedStringBuilder msb(*this);
	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());
	return msb.getLength(managedPtr, "len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_byte_at(CallExprAST *callExpr) {
	ManagedStringBuilder msb(*this);
	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());

	mir::MIRValue *bufferPtr = msb.getDataPtr(managedPtr, "buffer.ptr");
	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), bufferPtr, index64, "byte.ptr");
	return builder->createLoad(mir::MIRType::getInt8(), bytePtr, "byte");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_slice(CallExprAST *callExpr) {
	ManagedStringBuilder msb(*this);
	mir::MIRType *substringType = msb.getSubstringType();

	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());
	mir::MIRValue *startIdx = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *endIdx = generateExpr(callExpr->args[2].value.get());

	mir::MIRValue *newLen = builder->createSub(endIdx, startIdx, "slice.len");

	mir::MIRValue *srcPtr = msb.getDataPtr(managedPtr, "src.ptr");
	mir::MIRValue *start64 = builder->createSExt(startIdx, mir::MIRType::getInt64(), "start64");
	mir::MIRValue *slicePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, start64, "slice.ptr");

	mir::MIRValue *newSub = msb.allocateSubstringStruct("slice.substring");
	msb.populateSubstringStruct(newSub, managedPtr, slicePtr, newLen, builder->getInt32(0));

	// Retain parent if heap-allocated
	msb.emitRetainIfHeap(managedPtr);

	return builder->createLoad(substringType, newSub, "slice.result");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_parent_managed(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());

	// Get _parentManaged field (field 0)
	mir::MIRValue *parentPtr = builder->createStructGEP(substringType, subPtr, 0, "sub._parentManaged");
	return builder->createLoad(mir::MIRType::getPtr(), parentPtr, "parent.managed");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_byte_offset(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());

	// Get _parentManaged field (field 0)
	mir::MIRValue *parentManagedPtr = builder->createStructGEP(substringType, subPtr, 0, "sub._parentManaged");
	mir::MIRValue *parentManaged = builder->createLoad(mir::MIRType::getPtr(), parentManagedPtr, "parent.managed");

	// Get parent's data pointer from _buffer field (field 0)
	mir::MIRValue *parentBufPtr = builder->createStructGEP(managedStringType, parentManaged, 0, "parent._buffer");
	mir::MIRValue *parentDataPtrPtr = builder->createStructGEP(unsizedArrayType, parentBufPtr, 0, "parent.ptr.ptr");
	mir::MIRValue *parentDataPtr = builder->createLoad(mir::MIRType::getPtr(), parentDataPtrPtr, "parent.data.ptr");

	// Get substring's _ptr field (field 1)
	mir::MIRValue *subDataPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "sub._ptr");
	mir::MIRValue *subDataPtr = builder->createLoad(mir::MIRType::getPtr(), subDataPtrPtr, "sub.data.ptr");

	// Calculate byte offset: subDataPtr - parentDataPtr
	mir::MIRValue *parentInt = builder->createPtrToInt(parentDataPtr, mir::MIRType::getInt64(), "parent.int");
	mir::MIRValue *subInt = builder->createPtrToInt(subDataPtr, mir::MIRType::getInt64(), "sub.int");
	mir::MIRValue *offset64 = builder->createSub(subInt, parentInt, "offset64");
	return builder->createTrunc(offset64, mir::MIRType::getInt32(), "offset");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_concat(CallExprAST *callExpr) {
	ManagedStringBuilder msb(*this);
	mir::MIRType *managedStringType = msb.getManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *m1Ptr = getManagedStringPtr(callExpr->args[0].value.get());
	mir::MIRValue *m2Ptr = getManagedStringPtr(callExpr->args[1].value.get());

	mir::MIRValue *len1 = msb.getLength(m1Ptr, "len1");
	mir::MIRValue *len2 = msb.getLength(m2Ptr, "len2");

	mir::MIRValue *totalLen = builder->createAdd(len1, len2, "total.len");
	mir::MIRValue *totalLenPlus1 = builder->createAdd(totalLen, builder->getInt32(1), "total.len.plus1");

	mir::MIRValue *ptr1 = msb.getDataPtr(m1Ptr, "ptr1");
	mir::MIRValue *ptr2 = msb.getDataPtr(m2Ptr, "ptr2");

	mir::MIRValue *newBuffer = msb.allocateBuffer(totalLenPlus1, "string concat");

	mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
														{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue *len1_64 = builder->createSExt(len1, mir::MIRType::getInt64(), "len1_64");
	builder->createCall(memcpyFunc, {newBuffer, ptr1, len1_64});

	mir::MIRValue *destOffset = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {len1_64}, "dest.offset");
	mir::MIRValue *len2_64 = builder->createSExt(len2, mir::MIRType::getInt64(), "len2_64");
	builder->createCall(memcpyFunc, {destOffset, ptr2, len2_64});

	mir::MIRValue *totalLen64 = builder->createSExt(totalLen, mir::MIRType::getInt64(), "total_len64_for_null");
	mir::MIRValue *nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {totalLen64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	mir::MIRValue *managedSize = builder->getInt64(32);
	mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "concat.managed");

	mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(totalLen, dstBufLenPtr);

	mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(totalLen, dstLenPtr);

	mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(totalLen, dstCapPtr);

	return newManaged;
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_make_unique(CallExprAST *callExpr) {
	// This intrinsic ensures a string has exclusive ownership before mutation.
	// For heap strings (capacity > 0), it calls the runtime to check refcount and copy if needed.
	// For constant strings (capacity == 0), it allocates a new heap buffer and copies the data.
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());

	mir::MIRValue *lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
	mir::MIRValue *capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");

	mir::MIRValue *isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock *heapBlock = builder->createBasicBlock("make_unique.heap");
	mir::MIRBasicBlock *constBlock = builder->createBasicBlock("make_unique.const");
	mir::MIRBasicBlock *mergeBlock = builder->createBasicBlock("make_unique.merge");

	builder->createCondBr(isHeap, heapBlock, constBlock);

	// Heap path: call runtime to make unique (handles refcount check)
	builder->setInsertPoint(heapBlock);
	mir::MIRValue *srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
	mir::MIRValue *srcDataPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.data.ptr");

	mir::MIRValue *tag = module->createGlobalString(".__tag.str.cow", "string COW");
	mir::MIRFunction *makeUniqueFunc = getOrDeclareFunction("_managed_string_make_unique",
															mir::MIRType::getPtr(), {mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getPtr()});
	mir::MIRValue *newDataPtr = builder->createCall(makeUniqueFunc, {srcDataPtr, len, tag}, "unique.data");

	builder->createStore(newDataPtr, srcPtrPtr);
	builder->createBr(mergeBlock);

	// Const path: allocate new heap buffer and copy constant data
	builder->setInsertPoint(constBlock);
	mir::MIRValue *constBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "const._buffer");
	mir::MIRValue *constPtrPtr = builder->createStructGEP(unsizedArrayType, constBufPtr, 0, "const.ptr.ptr");
	mir::MIRValue *constDataPtr = builder->createLoad(mir::MIRType::getPtr(), constPtrPtr, "const.data.ptr");

	// Allocate new buffer with _managed_string_alloc (handles header and refcount)
	// Request len + 1 bytes (for data + null terminator)
	mir::MIRValue *allocSize = builder->createAdd(len, builder->getInt32(1), "alloc.size");
	mir::MIRValue *allocSize64 = builder->createSExt(allocSize, mir::MIRType::getInt64(), "alloc.size64");
	mir::MIRValue *allocTag = module->createGlobalString(".__tag.str.cow", "string COW const");
	mir::MIRFunction *allocFunc = getOrDeclareFunction("_managed_string_alloc",
													   mir::MIRType::getPtr(), {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
	mir::MIRValue *newDataStart = builder->createCall(allocFunc, {allocSize64, allocTag}, "new.data");

	// Copy constant data to new buffer
	mir::MIRValue *len64 = builder->createSExt(len, mir::MIRType::getInt64(), "len64");
	mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy",
														mir::MIRType::getPtr(), {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	builder->createCall(memcpyFunc, {newDataStart, constDataPtr, len64}, "");

	// Null-terminate
	mir::MIRValue *nullPos = builder->createArrayGEP(mir::MIRType::getInt8(), newDataStart, len64, "null.pos");
	builder->createStore(builder->getInt8(0), nullPos);

	// Update managed string to point to new heap buffer
	builder->createStore(newDataStart, constPtrPtr);

	// Set capacity to len+1 to indicate heap allocation
	mir::MIRValue *newCap = builder->createAdd(len, builder->getInt32(1), "new.cap");
	builder->createStore(newCap, capPtr);

	builder->createBr(mergeBlock);

	builder->setInsertPoint(mergeBlock);
	return managedPtr;
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_set_byte(CallExprAST *callExpr) {
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *value = generateExpr(callExpr->args[2].value.get());

	mir::MIRValue *bufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "managed._buffer");
	mir::MIRValue *ptrPtr = builder->createStructGEP(unsizedArrayType, bufPtr, 0, "buffer.ptr.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), ptrPtr, "data.ptr");

	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), dataPtr, index64, "byte.ptr");

	mir::MIRValue *byteVal = value;
	if (value->type->kind != mir::MIRTypeKind::Int8) {
		byteVal = builder->createTrunc(value, mir::MIRType::getInt8(), "byte.val");
	}
	builder->createStore(byteVal, bytePtr);

	return builder->getNull();
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_get_refcount(CallExprAST *callExpr) {
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());

	mir::MIRValue *capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");
	mir::MIRValue *isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock *heapBlock = builder->createBasicBlock("refcount.heap");
	mir::MIRBasicBlock *constBlock = builder->createBasicBlock("refcount.const");
	mir::MIRBasicBlock *mergeBlock = builder->createBasicBlock("refcount.merge");

	builder->createCondBr(isHeap, heapBlock, constBlock);

	builder->setInsertPoint(heapBlock);
	mir::MIRValue *srcBufPtr = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue *srcPtrPtr = builder->createStructGEP(unsizedArrayType, srcBufPtr, 0, "src.ptr.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "data.ptr");

	mir::MIRValue *headerPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr, {builder->getInt64(-8)}, "header.ptr");
	mir::MIRValue *heapRefcount = builder->createLoad(mir::MIRType::getInt32(), headerPtr, "heap.refcount");
	builder->createBr(mergeBlock);

	builder->setInsertPoint(constBlock);
	mir::MIRValue *constRefcount = builder->getInt32(-1);
	builder->createBr(mergeBlock);

	builder->setInsertPoint(mergeBlock);
	mir::MIRValue *refcount = builder->createPhi(mir::MIRType::getInt32(), "refcount");
	builder->addPhiIncoming(refcount, heapRefcount, heapBlock);
	builder->addPhiIncoming(refcount, constRefcount, constBlock);

	return refcount;
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_to_cstring(CallExprAST *callExpr) {
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRType *cstringType = getOrCreateCstringType();

	mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());

	mir::MIRValue *bufPtrField = builder->createStructGEP(managedStringType, managedPtr, 0, "src._buffer");
	mir::MIRValue *bufPtrPtr = builder->createStructGEP(unsizedArrayType, bufPtrField, 0, "src.ptr.ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufPtrPtr, "data.ptr");

	mir::MIRValue *lenPtr = builder->createStructGEP(managedStringType, managedPtr, 1, "src._len");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	mir::MIRValue *capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, "src._capacity");
	mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), capPtr, "cap");
	mir::MIRValue *isHeap = builder->createICmpSGT(cap, builder->getInt32(0), "isHeap");

	// For SSO strings, we need to allocate a null-terminated buffer
	mir::MIRBasicBlock *ssoBlock = builder->createBasicBlock("cstring.sso");
	mir::MIRBasicBlock *heapBlock = builder->createBasicBlock("cstring.heap");
	mir::MIRBasicBlock *continueBlock = builder->createBasicBlock("cstring.continue");
	builder->createCondBr(isHeap, heapBlock, ssoBlock);

	// SSO path: allocate null-terminated buffer
	builder->setInsertPoint(ssoBlock);
	mir::MIRFunction *allocFunc = getOrDeclareFunction("_managed_string_alloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
	mir::MIRValue *tag = module->createGlobalString(".__tag.cstr", "cstring conversion");
	mir::MIRValue *allocSize = builder->createAdd(len, builder->getInt32(1), "alloc.size");
	mir::MIRValue *allocSize64 = builder->createZExt(allocSize, mir::MIRType::getInt64(), "alloc.size.64");
	mir::MIRValue *ssoDataPtr = builder->createCall(allocFunc, {allocSize64, tag}, "sso.cstr.alloc");

	// Copy data and null-terminate
	mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");
	mir::MIRValue *len64 = builder->createZExt(len, mir::MIRType::getInt64(), "len.64");
	builder->createCall(memcpyFunc, {ssoDataPtr, dataPtr, len64}, "sso.copy");
	mir::MIRValue *nullPtr = builder->createGEP(mir::MIRType::getInt8(), ssoDataPtr, {len64}, "null.ptr");
	builder->createStore(builder->getInt8(0), nullPtr);
	builder->createBr(continueBlock);

	// Heap path: data is already null-terminated
	builder->setInsertPoint(heapBlock);
	mir::MIRFunction *retainFunc = getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(retainFunc, {dataPtr});
	builder->createBr(continueBlock);

	builder->setInsertPoint(continueBlock);
	mir::MIRValue *finalDataPtr = builder->createPhi(mir::MIRType::getPtr(), "data.phi");
	builder->addPhiIncoming(finalDataPtr, ssoDataPtr, ssoBlock);
	builder->addPhiIncoming(finalDataPtr, dataPtr, heapBlock);

	mir::MIRValue *cstringAlloc = builder->createAlloca(cstringType, "cstring.alloc");

	// Store the final data pointer (SSO-allocated or heap-retained)
	mir::MIRValue *cstringDataPtr = builder->createStructGEP(cstringType, cstringAlloc, 0, "cstring.data");
	builder->createStore(finalDataPtr, cstringDataPtr);

	mir::MIRValue *cstringLenPtr = builder->createStructGEP(cstringType, cstringAlloc, 1, "cstring.len");
	builder->createStore(len, cstringLenPtr);

	mir::MIRValue *cstringManagedPtr = builder->createStructGEP(cstringType, cstringAlloc, 2, "cstring.managed");
	builder->createStore(managedPtr, cstringManagedPtr);

	// Track cstring for cleanup at scope exit
	if (!scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({"cs", cstringAlloc});
	}

	return builder->createLoad(cstringType, cstringAlloc, "cstring.result");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_string_from_chars(CallExprAST *callExpr) {
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *length = generateExpr(callExpr->args[1].value.get());

	// Extract buffer pointer from array struct
	// For array<T> or _ManagedArray<T>, we need to get the alloca pointer and extract field 0
	mir::MIRValue *srcBuffer;
	std::string argType = inferExprType(callExpr->args[0].value.get());

	if (maxon::TypeConversion::isArrayStructType(argType) || maxon::TypeConversion::isManagedArrayType(argType)) {
		// Get the alloca pointer for the array (don't load the value)
		mir::MIRValue *arrayPtr = nullptr;
		std::string varName;
		if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].value.get())) {
			arrayPtr = namedValues[varExpr->name];
			varName = varExpr->name;
		}
		if (!arrayPtr) {
			// Fallback: generate the expression and hope it returns a pointer
			arrayPtr = generateExpr(callExpr->args[0].value.get());
		}

		// Extract buffer pointer from the array struct
		if (maxon::TypeConversion::isArrayStructType(argType)) {
			// array<T> struct with nested layout:
			// Field 0: managed (__ManagedArrayData<T>)
			//   Field 0 of managed: _buffer
			// Field 1: iterIndex
			mir::MIRType *arrayStructType = getOrCreateArrayStructType("byte");
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType("byte");
			// For struct parameters, alloca contains a pointer to the struct - need to load it first
			mir::MIRValue *structBase = arrayPtr;
			if (!varName.empty() && isStructParameter(varName)) {
				structBase = builder->createLoad(mir::MIRType::getPtr(), arrayPtr, "arr.structptr");
			}
			// First GEP to get managed field (field 0 of array<T>)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structBase, 0, "arr.managed");
			// Second GEP to get _buffer field (field 0 of managed)
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, "arr.managed._buffer.ptr");
			srcBuffer = builder->createLoad(mir::MIRType::getPtr(), bufferField, "arr.buffer");
		} else {
			std::string elemType = maxon::TypeConversion::getArrayElementType(argType);
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, arrayPtr, 0, "arr._buffer.ptr");
			srcBuffer = builder->createLoad(mir::MIRType::getPtr(), bufferField, "arr.buffer");
		}
	} else {
		// Raw pointer - generate and use directly
		srcBuffer = generateExpr(callExpr->args[0].value.get());
	}

	mir::MIRValue *lengthPlus1 = builder->createAdd(length, builder->getInt32(1), "len.plus1");
	mir::MIRValue *lengthPlus1_64 = builder->createSExt(lengthPlus1, mir::MIRType::getInt64(), "len64");
	mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue *newBuffer = builder->createCall(mallocFunc, {lengthPlus1_64}, "fromchars.buffer");

	mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
														{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue *length_64 = builder->createSExt(length, mir::MIRType::getInt64(), "len_64");
	builder->createCall(memcpyFunc, {newBuffer, srcBuffer, length_64});

	mir::MIRValue *nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {length_64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	mir::MIRValue *managedSize = builder->getInt64(32);
	mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "fromchars.managed");

	mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(length, dstBufLenPtr);

	mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(length, dstLenPtr);

	mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(length, dstCapPtr);

	return newManaged;
}

// =============================================================================
// Cstring Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_cstring_len(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *lenPtr = builder->createStructGEP(cstringType, csPtr, 1, "cstring.len.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "cstring.len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_cstring_write_stdout(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());

	mir::MIRValue *dataPtr = builder->createStructGEP(cstringType, csPtr, 0, "cstring.data.ptr");
	mir::MIRValue *data = builder->createLoad(mir::MIRType::getPtr(), dataPtr, "cstring.data");

	mir::MIRValue *lenPtr = builder->createStructGEP(cstringType, csPtr, 1, "cstring.len.ptr");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "cstring.len");

	mir::MIRFunction *writeStdout = getOrDeclareFunction(
		"write_stdout", mir::MIRType::getInt32(), {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
	return builder->createCall(writeStdout, {data, len}, "write.result");
}

// =============================================================================
// File I/O Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_read_file(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get path cstring
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *pathDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "path.data.ptr");
	mir::MIRValue *pathData = builder->createLoad(mir::MIRType::getPtr(), pathDataPtr, "path.data");

	// Call runtime __read_file (returns ptr, null on error)
	mir::MIRFunction *readFileFunc = getOrDeclareFunction(
		"__read_file", mir::MIRType::getPtr(), {mir::MIRType::getPtr()});
	mir::MIRValue *rawResult = builder->createCall(readFileFunc, {pathData}, "read.raw");

	// Convert raw pointer to Optional<ptr>
	// The optional type is { i8 tag, ptr value }
	mir::MIRType *optionalType = mir::MIRType::getOptional(mir::MIRType::getPtr());

	// Allocate optional on stack
	mir::MIRValue *optAlloca = builder->createAlloca(optionalType, "opt.result");

	// Get tag field pointer (field 0)
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optAlloca, 0, "opt.tag.ptr");

	// Get value field pointer (field 1)
	mir::MIRValue *valPtr = builder->createStructGEP(optionalType, optAlloca, 1, "opt.val.ptr");

	// Check if result is null - compare pointer to 0
	mir::MIRValue *ptrToInt = builder->createPtrToInt(rawResult, mir::MIRType::getInt64(), "ptr.as.int");
	mir::MIRValue *isNull = builder->createICmpEq(ptrToInt, builder->getInt64(0), "is.null");

	// Create blocks for branching
	mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("read.hasvalue");
	mir::MIRBasicBlock *nilBlock = builder->createBasicBlock("read.nil");
	mir::MIRBasicBlock *continueBlock = builder->createBasicBlock("read.continue");

	builder->createCondBr(isNull, nilBlock, hasValueBlock);

	// Has value block - set tag=1 and store value
	builder->setInsertPoint(hasValueBlock);
	builder->createStore(builder->getInt8(1), tagPtr);
	builder->createStore(rawResult, valPtr);
	builder->createBr(continueBlock);

	// Nil block - set tag=0
	builder->setInsertPoint(nilBlock);
	builder->createStore(builder->getInt8(0), tagPtr);
	builder->createBr(continueBlock);

	// Continue block - load and return the optional
	builder->setInsertPoint(continueBlock);
	return builder->createLoad(optionalType, optAlloca, "opt.loaded");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_write_file(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get path cstring
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *pathDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "path.data.ptr");
	mir::MIRValue *pathData = builder->createLoad(mir::MIRType::getPtr(), pathDataPtr, "path.data");

	// Get content cstring
	mir::MIRValue *contentCsPtr = getCstringPtr(callExpr->args[1].value.get());

	// Call runtime __write_file
	mir::MIRFunction *writeFileFunc = getOrDeclareFunction(
		"__write_file", mir::MIRType::getInt32(), {mir::MIRType::getPtr(), mir::MIRType::getPtr()});

	return builder->createCall(writeFileFunc, {pathData, contentCsPtr}, "write.result");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_write_file_binary(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get path cstring
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *pathDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "path.data.ptr");
	mir::MIRValue *pathData = builder->createLoad(mir::MIRType::getPtr(), pathDataPtr, "path.data");

	// Get array info
	ArrayFieldInfo info = getManagedArrayInfo(callExpr->args[1].value.get(), callExpr->line, callExpr->column);
	if (!info.dataPtr) {
		return builder->getInt32(-1);
	}

	// Get buffer pointer
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(info.elementType);
	mir::MIRValue *bufPtrField = builder->createStructGEP(managedArrayType, info.dataPtr, 0, "arr._buffer");
	mir::MIRValue *managedDataPtr = builder->createLoad(mir::MIRType::getPtr(), bufPtrField, "arr.managed_data");

	// Get length
	mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, info.dataPtr, 1, "arr._len");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	// Call runtime write_file
	mir::MIRFunction *writeFileFunc = getOrDeclareFunction(
		"__write_file_binary", mir::MIRType::getInt32(), {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt32()});

	return builder->createCall(writeFileFunc, {pathData, managedDataPtr, len}, "write.result");
}

// =============================================================================
// Directory Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_list_directory(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get path cstring and extract the data pointer
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *pathDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "path.data.ptr");
	mir::MIRValue *pathData = builder->createLoad(mir::MIRType::getPtr(), pathDataPtr, "path.data");

	// Call runtime __list_directory which returns ptr to __ManagedArrayData<string> (or null on error)
	mir::MIRFunction *listDirFunc = getOrDeclareFunction(
		"__list_directory", mir::MIRType::getPtr(), {mir::MIRType::getPtr()});
	mir::MIRValue *resultPtr = builder->createCall(listDirFunc, {pathData}, "list.result.ptr");

	// Check if result is null (error case)
	mir::MIRValue *ptrToInt = builder->createPtrToInt(resultPtr, mir::MIRType::getInt64(), "ptr.as.int");
	mir::MIRValue *isNull = builder->createICmpEq(ptrToInt, builder->getInt64(0), "is.null");

	// Create blocks for branching
	mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("listdir.hasvalue");
	mir::MIRBasicBlock *nilBlock = builder->createBasicBlock("listdir.nil");
	mir::MIRBasicBlock *continueBlock = builder->createBasicBlock("listdir.continue");

	// The optional wraps an array<string> struct
	mir::MIRType *arrayStructType = getOrCreateArrayStructType("string");
	mir::MIRType *optionalType = mir::MIRType::getOptional(arrayStructType);

	// Allocate optional on stack
	mir::MIRValue *optAlloca = builder->createAlloca(optionalType, "opt.result");

	// Get tag field pointer (field 0)
	mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optAlloca, 0, "opt.tag.ptr");

	// Get value field pointer (field 1)
	mir::MIRValue *valPtr = builder->createStructGEP(optionalType, optAlloca, 1, "opt.val.ptr");

	builder->createCondBr(isNull, nilBlock, hasValueBlock);

	// Has value block - build the array struct and set tag=1
	builder->setInsertPoint(hasValueBlock);

	// Load the __ManagedArrayData<string> fields from the pointer
	mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType("string");

	mir::MIRValue *bufferPtrField = builder->createStructGEP(managedArrayType, resultPtr, 0, "managed.buffer.ptr");
	mir::MIRValue *buffer = builder->createLoad(mir::MIRType::getPtr(), bufferPtrField, "managed.buffer");

	mir::MIRValue *lenPtrField = builder->createStructGEP(managedArrayType, resultPtr, 1, "managed.len.ptr");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtrField, "managed.len");

	mir::MIRValue *capPtrField = builder->createStructGEP(managedArrayType, resultPtr, 2, "managed.cap.ptr");
	mir::MIRValue *cap = builder->createLoad(mir::MIRType::getInt32(), capPtrField, "managed.cap");

	// Free the temporary heap allocation for the result struct
	mir::MIRFunction *freeFunc = getOrDeclareFunction("free", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(freeFunc, {resultPtr});

	// Build array<string> struct inside the optional's value field
	// valPtr points to array<string> = { __ManagedArrayData<string>, i32 iterIndex }

	// Set managed.buffer (field 0 of field 0)
	mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, valPtr, 0, "array.managed");
	mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, "array.buffer");
	builder->createStore(buffer, bufferField);

	// Set managed.length (field 1 of field 0)
	mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, "array.len");
	builder->createStore(len, lenField);

	// Set managed.capacity (field 2 of field 0)
	mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, "array.cap");
	builder->createStore(cap, capField);

	// Set iterIndex (field 1) to 0
	mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, valPtr, 1, "array.iter");
	builder->createStore(builder->getInt32(0), iterField);

	// Set tag = 1 (has value)
	builder->createStore(builder->getInt8(1), tagPtr);
	builder->createBr(continueBlock);

	// Nil block - set tag=0
	builder->setInsertPoint(nilBlock);
	builder->createStore(builder->getInt8(0), tagPtr);
	builder->createBr(continueBlock);

	// Continue block - load and return the optional
	builder->setInsertPoint(continueBlock);
	return builder->createLoad(optionalType, optAlloca, "opt.loaded");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_is_directory(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get path cstring and extract the data pointer
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *pathDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "path.data.ptr");
	mir::MIRValue *pathData = builder->createLoad(mir::MIRType::getPtr(), pathDataPtr, "path.data");

	// Call runtime __is_directory
	mir::MIRFunction *isDirFunc = getOrDeclareFunction(
		"__is_directory", mir::MIRType::getInt32(), {mir::MIRType::getPtr()});
	return builder->createCall(isDirFunc, {pathData}, "is_dir.result");
}

// =============================================================================
// Process Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_execute_process(CallExprAST *callExpr) {
	mir::MIRType *cstringType = getOrCreateCstringType();

	// Get command line cstring and extract the data pointer
	mir::MIRValue *csPtr = getCstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *cmdDataPtr = builder->createStructGEP(cstringType, csPtr, 0, "cmd.data.ptr");
	mir::MIRValue *cmdData = builder->createLoad(mir::MIRType::getPtr(), cmdDataPtr, "cmd.data");

	// Call runtime __execute_process
	mir::MIRFunction *execFunc = getOrDeclareFunction(
		"__execute_process", mir::MIRType::getInt32(), {mir::MIRType::getPtr()});
	return builder->createCall(execFunc, {cmdData}, "exec.result");
}

// =============================================================================
// Substring Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_len(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *lenPtr = builder->createStructGEP(substringType, subPtr, 2, "sub._len");
	return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_byte_at(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());

	mir::MIRValue *ptrPtr = builder->createStructGEP(substringType, subPtr, 1, "sub._ptr");
	mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), ptrPtr, "data.ptr");

	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *bytePtr = builder->createArrayGEP(mir::MIRType::getInt8(), dataPtr, index64, "byte.ptr");
	return builder->createLoad(mir::MIRType::getInt8(), bytePtr, "byte");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_iter_pos(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *iterPosPtr = builder->createStructGEP(substringType, subPtr, 3, "sub._iterPos");
	return builder->createLoad(mir::MIRType::getInt32(), iterPosPtr, "iterPos");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_with_iter_pos(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *newPos = generateExpr(callExpr->args[1].value.get());

	mir::MIRValue *newSub = builder->createAlloca(substringType, "new.substring");

	// Copy all fields from source
	for (int i = 0; i < 4; i++) {
		mir::MIRValue *srcField = builder->createStructGEP(substringType, subPtr, i, "src.field" + std::to_string(i));
		mir::MIRValue *dstField = builder->createStructGEP(substringType, newSub, i, "dst.field" + std::to_string(i));
		mir::MIRType *fieldType = (i < 2) ? mir::MIRType::getPtr() : mir::MIRType::getInt32();
		mir::MIRValue *val = builder->createLoad(fieldType, srcField, "val" + std::to_string(i));
		builder->createStore(val, dstField);
	}

	// Set new iter position
	mir::MIRValue *iterPosPtr = builder->createStructGEP(substringType, newSub, 3, "dst._iterPos");
	builder->createStore(newPos, iterPosPtr);

	return builder->createLoad(substringType, newSub, "new.substring.result");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_to_string(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRType *managedStringType = getOrCreateManagedStringType();
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();

	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());

	mir::MIRValue *srcPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "src._ptr");
	mir::MIRValue *srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");
	mir::MIRValue *lenPtr = builder->createStructGEP(substringType, subPtr, 2, "src._len");
	mir::MIRValue *len = builder->createLoad(mir::MIRType::getInt32(), lenPtr, "len");

	// Use _managed_string_alloc to properly allocate buffer with refcount header
	mir::MIRValue *lenPlus1 = builder->createAdd(len, builder->getInt32(1), "len.plus1");
	mir::MIRValue *lenPlus1_64 = builder->createSExt(lenPlus1, mir::MIRType::getInt64(), "len64");

	// Create allocation tag for debugging
	mir::MIRValue *tag = module->createGlobalString(".__tag.tostring", "substring.toString");

	// Use _managed_string_alloc which sets up refcount header properly
	mir::MIRFunction *stringAllocFunc = module->getFunction("_managed_string_alloc");
	if (!stringAllocFunc) {
		stringAllocFunc = builder->declareFunction("_managed_string_alloc",
												   mir::MIRType::getPtr(),
												   {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
	}
	mir::MIRValue *newBuffer = builder->createCall(stringAllocFunc, {lenPlus1_64, tag}, "tostring.buffer");

	// Copy data from substring to new buffer
	mir::MIRFunction *memcpyFunc = getOrDeclareFunction("memcpy", mir::MIRType::getPtr(),
														{mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
	mir::MIRValue *len_64 = builder->createSExt(len, mir::MIRType::getInt64(), "len_64");
	builder->createCall(memcpyFunc, {newBuffer, srcPtr, len_64});

	// Add null terminator
	mir::MIRValue *nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newBuffer, {len_64}, "null_term_ptr");
	builder->createStore(builder->getInt8(0), nullTermPtr);

	// Allocate ManagedStringData struct
	mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
	mir::MIRValue *managedSize = builder->getInt64(32);
	mir::MIRValue *newManaged = builder->createCall(mallocFunc, {managedSize}, "tostring.managed");

	// Set up ManagedStringData fields
	mir::MIRValue *dstBufferPtr = builder->createStructGEP(managedStringType, newManaged, 0, "dst._buffer");
	mir::MIRValue *dstPtrPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 0, "dst.ptr.ptr");
	builder->createStore(newBuffer, dstPtrPtr);
	mir::MIRValue *dstBufLenPtr = builder->createStructGEP(unsizedArrayType, dstBufferPtr, 1, "dst.buflen.ptr");
	builder->createStore(len, dstBufLenPtr);

	mir::MIRValue *dstLenPtr = builder->createStructGEP(managedStringType, newManaged, 1, "dst._len");
	builder->createStore(len, dstLenPtr);

	// Set capacity > 0 to indicate heap allocation (enables proper refcounting)
	mir::MIRValue *dstCapPtr = builder->createStructGEP(managedStringType, newManaged, 2, "dst._capacity");
	builder->createStore(lenPlus1, dstCapPtr);

	return newManaged;
}

mir::MIRValue *MIRCodeGenerator::intrinsic_substring_slice(CallExprAST *callExpr) {
	mir::MIRType *substringType = getOrCreateSubstringType();
	mir::MIRType *managedStringType = getOrCreateManagedStringType();

	mir::MIRValue *subPtr = getSubstringPtr(callExpr->args[0].value.get());
	mir::MIRValue *startIdx = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *endIdx = generateExpr(callExpr->args[2].value.get());

	mir::MIRValue *newLen = builder->createSub(endIdx, startIdx, "slice.len");

	mir::MIRValue *srcPtrPtr = builder->createStructGEP(substringType, subPtr, 1, "src._ptr");
	mir::MIRValue *srcPtr = builder->createLoad(mir::MIRType::getPtr(), srcPtrPtr, "src.ptr");
	mir::MIRValue *start64 = builder->createSExt(startIdx, mir::MIRType::getInt64(), "start64");
	mir::MIRValue *slicePtr = builder->createArrayGEP(mir::MIRType::getInt8(), srcPtr, start64, "slice.ptr");

	mir::MIRValue *parentPtr = builder->createStructGEP(substringType, subPtr, 0, "src._parentManaged");
	mir::MIRValue *parentManaged = builder->createLoad(mir::MIRType::getPtr(), parentPtr, "parent.managed");

	mir::MIRValue *newSub = builder->createAlloca(substringType, "slice.substring");

	mir::MIRValue *dstParentPtr = builder->createStructGEP(substringType, newSub, 0, "dst._parentManaged");
	builder->createStore(parentManaged, dstParentPtr);

	mir::MIRValue *dstPtrPtr = builder->createStructGEP(substringType, newSub, 1, "dst._ptr");
	builder->createStore(slicePtr, dstPtrPtr);

	mir::MIRValue *dstLenPtr = builder->createStructGEP(substringType, newSub, 2, "dst._len");
	builder->createStore(newLen, dstLenPtr);

	mir::MIRValue *dstIterPosPtr = builder->createStructGEP(substringType, newSub, 3, "dst._iterPos");
	builder->createStore(builder->getInt32(0), dstIterPosPtr);

	// Retain parent if heap-allocated
	mir::MIRValue *parentCapPtr = builder->createStructGEP(managedStringType, parentManaged, 2, "parent._capacity");
	mir::MIRValue *parentCap = builder->createLoad(mir::MIRType::getInt32(), parentCapPtr, "parent.cap");
	mir::MIRValue *isHeap = builder->createICmpSGT(parentCap, builder->getInt32(0), "isHeap");

	mir::MIRBasicBlock *retainBlock = builder->createBasicBlock("slice.retain");
	mir::MIRBasicBlock *continueBlock = builder->createBasicBlock("slice.continue");
	builder->createCondBr(isHeap, retainBlock, continueBlock);

	builder->setInsertPoint(retainBlock);
	mir::MIRType *unsizedArrayType = getOrCreateUnsizedArrayType();
	mir::MIRValue *parentBufPtr = builder->createStructGEP(managedStringType, parentManaged, 0, "parent._buffer");
	mir::MIRValue *parentDataPtrPtr = builder->createStructGEP(unsizedArrayType, parentBufPtr, 0, "parent.ptr.ptr");
	mir::MIRValue *parentDataPtr = builder->createLoad(mir::MIRType::getPtr(), parentDataPtrPtr, "parent.data.ptr");
	mir::MIRFunction *retainFunc = getOrDeclareFunction("_managed_string_retain", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});
	builder->createCall(retainFunc, {parentDataPtr});
	builder->createBr(continueBlock);

	builder->setInsertPoint(continueBlock);
	return builder->createLoad(substringType, newSub, "slice.result");
}

// =============================================================================
// Array Intrinsics
// =============================================================================

mir::MIRValue *MIRCodeGenerator::intrinsic_array_len(CallExprAST *callExpr) {
	ExprAST *arrayArg = callExpr->args[0].value.get();
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// If we have a hidden length alloca (regular variable), use it directly
	if (info.lengthAlloca) {
		return builder->createLoad(mir::MIRType::getInt32(), info.lengthAlloca, "array.len");
	}

	// For struct fields or heap arrays without hidden alloca, read from header at dataPtr - 8
	// Layout: [length:i32][capacity:i32][...data...]
	//              -8          -4           0+
	mir::MIRValue *dataPtr;
	if (info.isStructField) {
		// Struct field: info.dataPtr is a pointer to the array field, need to load the data pointer
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else if (info.isStackArray) {
		// Stack array: dataPtr IS the array
		dataPtr = info.dataPtr;
		// Stack arrays always have hidden length allocas, so we shouldn't reach here
		reportError("Stack array missing length alloca", callExpr->line, callExpr->column);
		return nullptr;
	} else {
		// Heap array: info.dataPtr is an alloca holding the data pointer
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
												  {builder->getInt64(-8)}, "length.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "array.len");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_array_capacity(CallExprAST *callExpr) {
	ExprAST *arrayArg = callExpr->args[0].value.get();
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// If we have a hidden capacity alloca (regular variable), use it directly
	if (info.capacityAlloca) {
		return builder->createLoad(mir::MIRType::getInt32(), info.capacityAlloca, "array.cap");
	}

	// For struct fields or heap arrays without hidden alloca, read from header at dataPtr - 4
	// Layout: [length:i32][capacity:i32][...data...]
	//              -8          -4           0+
	mir::MIRValue *dataPtr;
	if (info.isStructField) {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else if (info.isStackArray) {
		reportError("Stack array does not have capacity", callExpr->line, callExpr->column);
		return nullptr;
	} else {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	mir::MIRValue *capacityPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
													{builder->getInt64(-4)}, "capacity.ptr");
	return builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "array.cap");
}

mir::MIRValue *MIRCodeGenerator::intrinsic_array_set_length(CallExprAST *callExpr) {
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *newLen = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// If we have a hidden length alloca (regular variable), use it directly
	if (info.lengthAlloca) {
		builder->createStore(newLen, info.lengthAlloca);
		return builder->getInt32(0);
	}

	// For struct fields or heap arrays without hidden alloca, write to header at dataPtr - 8
	mir::MIRValue *dataPtr;
	if (info.isStructField) {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
												  {builder->getInt64(-8)}, "length.ptr");
	builder->createStore(newLen, lengthPtr);
	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_array_grow(CallExprAST *callExpr) {
	// __array_grow(arr, min_capacity) - grow array if needed
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *minCapacity = generateExpr(callExpr->args[1].value.get());
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// For struct fields, we need to work with the heap array header
	// For regular variables with hidden allocas, use those
	mir::MIRValue *arrAlloca = info.dataPtr;
	mir::MIRValue *capacityAlloca = info.capacityAlloca;

	// Load current capacity
	mir::MIRValue *capacity;
	if (capacityAlloca) {
		capacity = builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");
	} else {
		// For struct fields, read capacity from heap header at dataPtr - 4
		mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrAlloca, "data.ptr");
		mir::MIRValue *capacityPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
														{builder->getInt64(-4)}, "capacity.ptr");
		capacity = builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "capacity");
	}

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

	// Load current array pointer and call realloc
	mir::MIRValue *arrPtr = builder->createLoad(mir::MIRType::getPtr(), arrAlloca, "arr.ptr");
	mir::MIRFunction *reallocFunc = getOrDeclareFunction("realloc", mir::MIRType::getPtr(),
														 {mir::MIRType::getPtr(), mir::MIRType::getInt64(), mir::MIRType::getInt64()});
	mir::MIRValue *newArrPtr = builder->createCall(reallocFunc, {arrPtr, oldSize, newSize}, "new.arr");

	// Store new pointer
	builder->createStore(newArrPtr, arrAlloca);

	// Store new capacity - either to hidden alloca or to heap header
	if (capacityAlloca) {
		builder->createStore(newCapacity, capacityAlloca);
	} else {
		// For struct fields, write capacity to heap header at newArrPtr - 4
		mir::MIRValue *newCapacityPtr = builder->createGEP(mir::MIRType::getInt8(), newArrPtr,
														   {builder->getInt64(-4)}, "new.capacity.ptr");
		builder->createStore(newCapacity, newCapacityPtr);
	}
	builder->createBr(doneBlock);

	// Done block
	builder->setInsertPoint(doneBlock);
	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_array_set_at(CallExprAST *callExpr) {
	// __array_set_at(arr, index, value) - set element at index
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *index = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *value = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type
	mir::MIRType *elemType = getTypeFromString(info.elementType);

	// Get array data pointer
	mir::MIRValue *dataPtr;
	if (info.isStackArray) {
		// Stack array: dataPtr IS the array
		dataPtr = info.dataPtr;
	} else if (info.isStructField) {
		// Struct field: need to load the data pointer from the field
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else {
		// Heap array: dataPtr is an alloca holding the data pointer
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	// Calculate element pointer and store
	mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
	mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, dataPtr, index64, "elem.ptr");
	builder->createStore(value, elemPtr);

	return builder->getInt32(0);
}

mir::MIRValue *MIRCodeGenerator::intrinsic_array_shift_right(CallExprAST *callExpr) {
	// __array_shift_right(arr, start, count) - shift elements right for insert
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// Get data pointer
	mir::MIRValue *dataPtr;
	if (info.isStackArray) {
		dataPtr = info.dataPtr;
	} else if (info.isStructField) {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	// Get current length
	mir::MIRValue *length;
	if (info.lengthAlloca) {
		length = builder->createLoad(mir::MIRType::getInt32(), info.lengthAlloca, "len");
	} else {
		// For struct fields or heap arrays, read from header at dataPtr - 8
		mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
													  {builder->getInt64(-8)}, "length.ptr");
		length = builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "len");
	}

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

mir::MIRValue *MIRCodeGenerator::intrinsic_array_shift_left(CallExprAST *callExpr) {
	// __array_shift_left(arr, start, count) - shift elements left for remove
	ExprAST *arrayArg = callExpr->args[0].value.get();
	mir::MIRValue *start = generateExpr(callExpr->args[1].value.get());
	mir::MIRValue *count = generateExpr(callExpr->args[2].value.get());
	ArrayFieldInfo info = getArrayFieldInfo(arrayArg, callExpr->line, callExpr->column);

	// Get element type and size
	mir::MIRType *elemType = getTypeFromString(info.elementType);
	int elemSize = static_cast<int>(elemType->sizeInBytes);

	// Get data pointer
	mir::MIRValue *dataPtr;
	if (info.isStackArray) {
		dataPtr = info.dataPtr;
	} else if (info.isStructField) {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	} else {
		dataPtr = builder->createLoad(mir::MIRType::getPtr(), info.dataPtr, "data.ptr");
	}

	// Get current length
	mir::MIRValue *length;
	if (info.lengthAlloca) {
		length = builder->createLoad(mir::MIRType::getInt32(), info.lengthAlloca, "len");
	} else {
		// For struct fields or heap arrays, read from header at dataPtr - 8
		mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
													  {builder->getInt64(-8)}, "length.ptr");
		length = builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "len");
	}

	// Calculate source index: start + count
	mir::MIRValue *srcIndex = builder->createAdd(start, count, "src.idx");

	// Calculate number of elements to move: length - srcIndex
	mir::MIRValue *elementsToMove = builder->createSub(length, srcIndex, "elems.to.move");

	// Calculate byte size to move
	mir::MIRValue *elementsToMove64 = builder->createSExt(elementsToMove, mir::MIRType::getInt64(), "elems64");
	mir::MIRValue *byteSize = builder->createMul(elementsToMove64, builder->getInt64(elemSize), "byte.size");

	// Source pointer: arr + srcIndex * elemSize
	mir::MIRValue *srcIndex64 = builder->createSExt(srcIndex, mir::MIRType::getInt64(), "srcidx64");
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
