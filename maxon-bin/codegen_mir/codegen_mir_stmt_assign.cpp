/**
 * MIR Code Generator - Assignment Statements
 *
 * This file implements code generation for various assignment types.
 */

#include "../codegen_mir.h"
#include <stdexcept>

void MIRCodeGenerator::generateAssign(AssignStmtAST *assign, mir::MIRFunction *function) {
	mir::MIRValue *alloca = namedValues[assign->name];
	std::string varType;

	// Check for implicit self field access (when name is a field of the receiver type)
	if (!alloca && !currentReceiverType.empty()) {
		auto fieldsIt = structFields.find(currentReceiverType);
		if (fieldsIt != structFields.end()) {
			const auto &fields = fieldsIt->second;
			for (size_t i = 0; i < fields.size(); i++) {
				if (fields[i].first == assign->name) {
					// Found the field - get pointer through self
					mir::MIRValue *selfAlloca = namedValues["self"];
					if (selfAlloca) {
						mir::MIRType *selfStructType = structTypes[currentReceiverType];
						mir::MIRValue *selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.load");
						mir::MIRValue *fieldPtr = builder->createStructGEP(selfStructType, selfPtr, static_cast<int>(i), assign->name);
						alloca = fieldPtr; // Use field pointer
						varType = fields[i].second;
					}
					break;
				}
			}
		}
	}

	if (!alloca) {
		reportError("Unknown variable name: " + assign->name,
					assign->line, assign->column);
	}

	if (varType.empty()) {
		varType = variableTypes[assign->name];
	}

	// COW: For string reassignment, save the old data pointer BEFORE evaluating RHS
	// This is critical for self-assignment like: s = s + "x"
	// We need to release the old buffer after the new value is computed
	mir::MIRValue *oldDataPtr = nullptr;
	mir::MIRValue *oldCapacity = nullptr;
	if (varType == "string") {
		mir::MIRType *stringType = structTypes["string"];
		mir::MIRType *managedStringType = structTypes["__ManagedStringData"];
		mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

		if (stringType && managedStringType && unsizedArrayType) {
			// Load the _managed pointer from the string struct
			mir::MIRValue *managedFieldPtr = builder->createStructGEP(stringType, alloca, 0, assign->name + ".old._managed.ptr");
			mir::MIRValue *managedPtr = builder->createLoad(mir::MIRType::getPtr(), managedFieldPtr, assign->name + ".old._managed");

			// Load capacity to check if heap-allocated (_capacity > 0)
			mir::MIRValue *capPtr = builder->createStructGEP(managedStringType, managedPtr, 2, assign->name + ".old._capacity.ptr");
			oldCapacity = builder->createLoad(mir::MIRType::getInt32(), capPtr, assign->name + ".old._capacity");

			// Load the data pointer from _buffer.ptr
			mir::MIRValue *bufferPtr = builder->createStructGEP(managedStringType, managedPtr, 0, assign->name + ".old._buffer");
			mir::MIRValue *dataPtrPtr = builder->createStructGEP(unsizedArrayType, bufferPtr, 0, assign->name + ".old.data.ptr.ptr");
			oldDataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrPtr, assign->name + ".old.data.ptr");
		}
	}

	// Now evaluate the RHS expression
	mir::MIRValue *val = generateExpr(assign->value.get());
	if (!val) {
		reportError("Failed to generate assignment value",
					assign->line, assign->column);
	}

	// COW: Release the old string buffer if it was heap-allocated
	if (varType == "string" && oldDataPtr && oldCapacity) {
		// Check if capacity > 0 (heap-allocated)
		mir::MIRValue *isHeap = builder->createICmpSGT(oldCapacity, builder->getInt32(0), assign->name + ".old.isHeap");

		mir::MIRBasicBlock *releaseBlock = builder->createBasicBlock(assign->name + ".release");
		mir::MIRBasicBlock *assignBlock = builder->createBasicBlock(assign->name + ".assign");

		builder->createCondBr(isHeap, releaseBlock, assignBlock);

		// Release block: call _managed_string_release on old data
		builder->setInsertPoint(releaseBlock);
		mir::MIRValue *tag = module->createGlobalString(".__tag.reassign." + assign->name, "string reassign");
		mir::MIRFunction *stringReleaseFunc = getOrDeclareFunction(
			"_managed_string_release",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
		builder->createCall(stringReleaseFunc, {oldDataPtr, tag});
		builder->createBr(assignBlock);

		// Continue to assignment
		builder->setInsertPoint(assignBlock);
	}

	// Check if this is a struct assignment
	// For structs, val is a pointer to the source struct, so we need memcpy
	mir::MIRType *structType = structTypes.count(varType) ? structTypes[varType] : nullptr;

	if (structType && val->type->kind == mir::MIRTypeKind::Ptr) {
		// Struct assignment - use memcpy to copy the struct contents
		mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");
		if (!memcpyFunc) {
			// Declare memcpy: ptr memcpy(ptr dest, ptr src, i64 count)
			memcpyFunc = getOrDeclareFunction("memcpy",
											  mir::MIRType::getPtr(),
											  {mir::MIRType::getPtr(), mir::MIRType::getPtr(), mir::MIRType::getInt64()});
		}
		mir::MIRValue *sizeVal = builder->getInt64(structType->sizeInBytes);
		builder->createCall(memcpyFunc, {alloca, val, sizeVal});
	} else {
		builder->createStore(val, alloca);
	}
}

void MIRCodeGenerator::generateArrayAssign(ArrayAssignStmtAST *arrayAssign, mir::MIRFunction *function) {
	mir::MIRValue *indexVal = generateExpr(arrayAssign->index.get());
	if (!indexVal) {
		reportError("Failed to generate array index",
					arrayAssign->line, arrayAssign->column);
	}

	mir::MIRValue *alloca = namedValues[arrayAssign->arrayName];
	std::string varType;

	// Check for implicit self field access (when arrayName is a field of the receiver type)
	if (!alloca && !currentReceiverType.empty()) {
		auto fieldsIt = structFields.find(currentReceiverType);
		if (fieldsIt != structFields.end()) {
			const auto &fields = fieldsIt->second;
			for (size_t i = 0; i < fields.size(); i++) {
				if (fields[i].first == arrayAssign->arrayName) {
					// Found the field - load from self
					mir::MIRValue *selfAlloca = namedValues["self"];
					if (selfAlloca) {
						mir::MIRType *selfStructType = structTypes[currentReceiverType];
						mir::MIRValue *selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.load");
						mir::MIRValue *fieldPtr = builder->createStructGEP(selfStructType, selfPtr, static_cast<int>(i), arrayAssign->arrayName);
						alloca = fieldPtr; // Use field pointer instead of a separate alloca
						varType = fields[i].second;
					}
					break;
				}
			}
		}
	}

	if (!alloca) {
		reportError("Unknown array name: " + arrayAssign->arrayName,
					arrayAssign->line, arrayAssign->column);
	}

	// Determine element type
	if (varType.empty()) {
		varType = variableTypes[arrayAssign->arrayName];
	}
	std::string elementTypeStr = "int";
	if (varType.size() > 2 && varType[0] == '[') {
		size_t closeBracket = varType.find(']');
		if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
			elementTypeStr = varType.substr(closeBracket + 1);
		}
	}
	mir::MIRType *elementType = getTypeFromString(elementTypeStr);

	// Get the array pointer (stack vs heap allocated)
	mir::MIRValue *arrayPtr;
	if (stackAllocatedArrays.count(arrayAssign->arrayName) > 0) {
		arrayPtr = alloca;
	} else {
		arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
	}
	mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");

	// Handle struct literal assignment
	if (auto *structInit = dynamic_cast<StructInitExprAST *>(arrayAssign->value.get())) {
		mir::MIRType *structType = structTypes[structInit->structName];
		if (!structType) {
			reportError("Unknown struct type: " + structInit->structName,
						structInit->line, structInit->column);
		}

		const auto &fieldList = structFields[structInit->structName];
		const auto &defaults = structFieldDefaults[structInit->structName];

		// Build a map of provided field values
		std::map<std::string, const StructInitField *> providedFields;
		for (const auto &field : structInit->fields) {
			providedFields[field.name] = &field;
		}

		for (size_t fieldIndex = 0; fieldIndex < fieldList.size(); fieldIndex++) {
			const std::string &fieldName = fieldList[fieldIndex].first;

			// Get the value expression - either from provided init or from default
			ExprAST *valueExpr = nullptr;
			auto providedIt = providedFields.find(fieldName);
			if (providedIt != providedFields.end()) {
				valueExpr = providedIt->second->value.get();
			} else {
				auto defaultIt = defaults.find(fieldName);
				if (defaultIt != defaults.end()) {
					valueExpr = defaultIt->second;
				}
			}

			if (valueExpr == nullptr) {
				reportError("No value for field '" + fieldName +
							"' in struct '" + structInit->structName + "'",
							structInit->line, structInit->column);
			}

			mir::MIRValue *fieldPtr = builder->createStructGEP(structType, elementPtr,
															   static_cast<int>(fieldIndex), fieldName);
			mir::MIRValue *fieldVal = generateExpr(valueExpr);
			builder->createStore(fieldVal, fieldPtr);
		}
	} else {
		mir::MIRValue *val = generateExpr(arrayAssign->value.get());
		if (!val) {
			reportError("Failed to generate array assignment value",
						arrayAssign->line, arrayAssign->column);
		}
		builder->createStore(val, elementPtr);
	}
}

void MIRCodeGenerator::generateArrayMemberAssign(ArrayMemberAssignStmtAST *arrayMemberAssign, mir::MIRFunction *function) {
	mir::MIRValue *indexVal = generateExpr(arrayMemberAssign->index.get());
	if (!indexVal) {
		reportError("Failed to generate array index",
					arrayMemberAssign->line, arrayMemberAssign->column);
	}

	mir::MIRValue *alloca = namedValues[arrayMemberAssign->arrayName];
	if (!alloca) {
		reportError("Unknown array name: " + arrayMemberAssign->arrayName,
					arrayMemberAssign->line, arrayMemberAssign->column);
	}

	// Determine element type
	std::string varType = variableTypes[arrayMemberAssign->arrayName];
	std::string elementTypeStr;
	if (varType.size() > 2 && varType[0] == '[') {
		size_t closeBracket = varType.find(']');
		if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
			elementTypeStr = varType.substr(closeBracket + 1);
		}
	}

	mir::MIRType *structType = structTypes[elementTypeStr];
	if (!structType) {
		reportError("Element type is not a struct: " + elementTypeStr,
					arrayMemberAssign->line, arrayMemberAssign->column);
	}

	// Get pointer to array element
	mir::MIRValue *arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
	mir::MIRValue *elementPtr = builder->createArrayGEP(structType, arrayPtr, indexVal, "arrayidx");

	// Find field index
	const auto &fields = structFields[elementTypeStr];
	int fieldIndex = -1;
	for (size_t i = 0; i < fields.size(); i++) {
		if (fields[i].first == arrayMemberAssign->memberName) {
			fieldIndex = static_cast<int>(i);
			break;
		}
	}

	if (fieldIndex < 0) {
		reportError("Unknown field '" + arrayMemberAssign->memberName +
					"' in struct '" + elementTypeStr + "'",
					arrayMemberAssign->line, arrayMemberAssign->column);
	}

	mir::MIRValue *fieldPtr = builder->createStructGEP(structType, elementPtr,
													   fieldIndex, arrayMemberAssign->memberName);
	mir::MIRValue *val = generateExpr(arrayMemberAssign->value.get());
	builder->createStore(val, fieldPtr);
}

void MIRCodeGenerator::generateMemberAssign(MemberAssignStmtAST *memberAssign, mir::MIRFunction *function) {
	// Struct member assignment: obj.field = value
	mir::MIRValue *alloca = namedValues[memberAssign->objectName];
	if (!alloca) {
		reportError("Unknown variable: " + memberAssign->objectName,
					memberAssign->line, memberAssign->column);
	}

	// Get the struct type
	std::string structTypeName = variableTypes[memberAssign->objectName];
	mir::MIRType *structType = structTypes[structTypeName];
	if (!structType) {
		reportError("Variable '" + memberAssign->objectName + "' is not a struct type",
					memberAssign->line, memberAssign->column);
	}

	// Find field index
	const auto &fields = structFields[structTypeName];
	int fieldIndex = -1;
	for (size_t i = 0; i < fields.size(); i++) {
		if (fields[i].first == memberAssign->memberName) {
			fieldIndex = static_cast<int>(i);
			break;
		}
	}

	if (fieldIndex < 0) {
		reportError("Unknown field '" + memberAssign->memberName +
					"' in struct '" + structTypeName + "'",
					memberAssign->line, memberAssign->column);
	}

	// Get pointer to the field
	mir::MIRValue *fieldPtr = builder->createStructGEP(structType, alloca,
													   fieldIndex, memberAssign->memberName);

	// Generate the value and store it
	mir::MIRValue *val = generateExpr(memberAssign->value.get());
	builder->createStore(val, fieldPtr);
}

void MIRCodeGenerator::generateMemberArrayAssign(MemberArrayAssignStmtAST *memberArrayAssign, mir::MIRFunction *function) {
	// Struct member array element assignment: obj.arrayField[i] = value
	mir::MIRValue *alloca = namedValues[memberArrayAssign->objectName];
	if (!alloca) {
		reportError("Unknown variable: " + memberArrayAssign->objectName,
					memberArrayAssign->line, memberArrayAssign->column);
	}

	// Get the struct type
	std::string structTypeName = variableTypes[memberArrayAssign->objectName];
	mir::MIRType *structType = structTypes[structTypeName];
	if (!structType) {
		reportError("Variable '" + memberArrayAssign->objectName + "' is not a struct type",
					memberArrayAssign->line, memberArrayAssign->column);
	}

	// Find field index and type
	const auto &fields = structFields[structTypeName];
	int fieldIndex = -1;
	std::string fieldTypeStr;
	for (size_t i = 0; i < fields.size(); i++) {
		if (fields[i].first == memberArrayAssign->memberName) {
			fieldIndex = static_cast<int>(i);
			fieldTypeStr = fields[i].second;
			break;
		}
	}

	if (fieldIndex < 0) {
		reportError("Unknown field '" + memberArrayAssign->memberName +
					"' in struct '" + structTypeName + "'",
					memberArrayAssign->line, memberArrayAssign->column);
	}

	// Get pointer to the array field
	mir::MIRValue *fieldPtr = builder->createStructGEP(structType, alloca,
													   fieldIndex, memberArrayAssign->memberName);

	// Generate index
	mir::MIRValue *indexVal = generateExpr(memberArrayAssign->index.get());

	// Determine element type from field type "[16]byte" -> "byte"
	std::string elemTypeStr = "int";
	if (fieldTypeStr.size() > 2 && fieldTypeStr[0] == '[') {
		size_t closeBracket = fieldTypeStr.find(']');
		if (closeBracket != std::string::npos && closeBracket + 1 < fieldTypeStr.size()) {
			elemTypeStr = fieldTypeStr.substr(closeBracket + 1);
		}
	}
	mir::MIRType *elemType = getTypeFromString(elemTypeStr);

	// Get pointer to the array element
	mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, fieldPtr, indexVal, "fieldarray.elem");

	// Generate value and store
	mir::MIRValue *val = generateExpr(memberArrayAssign->value.get());
	builder->createStore(val, elemPtr);
}
