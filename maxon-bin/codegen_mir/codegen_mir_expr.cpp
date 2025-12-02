/**
 * MIR Code Generator - Expression Generation
 *
 * This file implements expression code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../lexer.h"
#include "intrinsic_codegen.h"
#include <cmath>
#include <cstring>
#include <stdexcept>

//==============================================================================
// Main Expression Dispatcher
//==============================================================================

// Helper struct to return both pointer and type info
struct MemberAddressResult {
	mir::MIRValue *ptr;
	std::string type;
};

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return builder->getInt32(numExpr->value);
	}

	if (auto *byteExpr = dynamic_cast<ByteExprAST *>(expr)) {
		return builder->getInt8(static_cast<int8_t>(byteExpr->value));
	}

	if (auto *floatExpr = dynamic_cast<FloatExprAST *>(expr)) {
		return builder->getFloat64(floatExpr->value);
	}

	if (auto *boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return builder->getInt1(boolExpr->value);
	}

	if (dynamic_cast<NilExprAST *>(expr)) {
		// Nil literal - type will be determined by context (where it's used)
		// For now, return a nullptr marker that will be wrapped in createNilOptional
		// by the caller (e.g., return statement)
		return nullptr;
	}

	if (auto *charExpr = dynamic_cast<CharacterExprAST *>(expr)) {
		// Character literals are now grapheme clusters (char struct, same layout as string)
		return generateCharLiteral(charExpr);
	}

	if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(expr)) {
		// Use new string literal generation with SSO support
		return generateStringLiteral(strExpr);
	}

	if (dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		// Array literals should only appear as initializers
		throw std::runtime_error("Array literal can only be used as an initializer in variable declarations");
	}

	if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		// Check for ExpressibleByStringLiteral: "literal" as Type
		// Transform to: Type.init(managed)
		// where managed is a _ManagedString struct
		if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(castExpr->expr.get())) {
			// Check if target type conforms to ExpressibleByStringLiteral
			auto structIt = structConformsTo.find(castExpr->targetType);
			if (structIt != structConformsTo.end()) {
				bool conformsToExpressible = false;
				for (const auto &iface : structIt->second) {
					if (iface == "ExpressibleByStringLiteral") {
						conformsToExpressible = true;
						break;
					}
				}
				if (conformsToExpressible) {
					// Get or create the _ManagedString struct type
					// Layout: { _buffer []byte, _len int, _capacity int }
					mir::MIRType *managedStringType = structTypes["_ManagedString"];
					if (!managedStringType) {
						// _ManagedString should be defined in stdlib, but create fallback
						mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
						if (!unsizedArrayType) {
							unsizedArrayType = module->getOrCreateStructType(
								"__unsized_array_byte",
								{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
							structTypes["__unsized_array_byte"] = unsizedArrayType;
						}
						managedStringType = module->getOrCreateStructType(
							"_ManagedString",
							{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
						structTypes["_ManagedString"] = managedStringType;
					}

					// Create the _ManagedString struct
					mir::MIRValue *managedAlloca = builder->createAlloca(managedStringType, "managed.literal");

					// Get string content
					const std::string &str = strLit->value;
					size_t len = str.length();

					// Create global constant for string data
					std::string globalName = ".str." + std::to_string(stringLiteralCounter++);
					mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

					// Get the unsized array type for []byte (the _buffer field)
					mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

					// Field 0: _buffer []byte (fat pointer: {ptr, i32})
					mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 0, "managed._buffer");
					mir::MIRValue *bufferDataPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "managed._buffer.ptr");
					builder->createStore(dataPtr, bufferDataPtr);
					mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "managed._buffer.len");
					builder->createStore(builder->getInt32(static_cast<int>(len)), bufferLenPtr);

					// Field 1: _len int
					mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 1, "managed._len");
					builder->createStore(builder->getInt32(static_cast<int>(len)), lenFieldPtr);

					// Field 2: _capacity int (0 = constant data)
					mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 2, "managed._capacity");
					builder->createStore(builder->getInt32(0), capacityFieldPtr);

					// Call Type.init(self, managed)
					// Note: self is null since this is a factory method that creates a new instance
					std::string methodName = castExpr->targetType + ".init";
					mir::MIRFunction *initFunc = module->getFunction(methodName);
					if (!initFunc) {
						throw std::runtime_error("Type '" + castExpr->targetType +
												 "' conforms to ExpressibleByStringLiteral but has no init method");
					}

					// Pass null for self (factory method doesn't use it)
					mir::MIRValue *nullSelf = builder->getNull();
					return builder->createCall(initFunc, {nullSelf, managedAlloca}, "literal.init");
				}
			}
		}

		mir::MIRValue *value = generateExpr(castExpr->expr.get());
		if (!value) {
			throw std::runtime_error("Failed to generate expression for cast");
		}

		mir::MIRType *targetType = getTypeFromString(castExpr->targetType);
		mir::MIRType *sourceType = value->type;

		// Handle different cast scenarios
		if (sourceType == targetType) {
			return value;
		}

		// Integer to integer
		if (sourceType->isInteger() && targetType->isInteger()) {
			uint64_t sourceBits = sourceType->sizeInBytes * 8;
			uint64_t targetBits = targetType->sizeInBytes * 8;
			if (sourceBits < targetBits) {
				return builder->createZExt(value, targetType, "zexttmp");
			} else if (sourceBits > targetBits) {
				return builder->createTrunc(value, targetType, "trunctmp");
			}
			return value;
		}

		// Integer to float
		if (sourceType->isInteger() && targetType->isFloat()) {
			return builder->createSIToFP(value, targetType, "int2floattmp");
		}

		// Float to integer
		if (sourceType->isFloat() && targetType->isInteger()) {
			return builder->createFPToSI(value, targetType, "float2inttmp");
		}

		// Integer to pointer
		if (sourceType->isInteger() && targetType->isPointer()) {
			return builder->createIntToPtr(value, targetType, "int2ptrtmp");
		}

		// Pointer to integer
		if (sourceType->isPointer() && targetType->isInteger()) {
			return builder->createPtrToInt(value, targetType, "ptr2inttmp");
		}

		throw std::runtime_error("Unsupported cast from " +
								 (sourceType->structName.empty() ? getMaxonTypeFromMIRType(sourceType) : sourceType->structName) +
								 " to " + castExpr->targetType);
	}

	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		mir::MIRValue *alloca = namedValues[varExpr->name];
		if (!alloca) {
			// If we're in a method and variable not found, check if it's a struct field
			// This enables implicit self field access: 'count' instead of 'self.count'
			if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == varExpr->name) {
							fieldIndex = static_cast<int>(i);
							fieldType = fieldsIt->second[i].second;
							break;
						}
					}

					if (fieldIndex >= 0) {
						// Generate field access through implicit 'self' parameter
						// self is always a struct pointer (first parameter)
						mir::MIRValue *selfAlloca = namedValues["self"];
						if (selfAlloca) {
							// self is a struct - load pointer to it
							mir::MIRValue *selfPtr;
							if (isStructParameter("self")) {
								// Load the pointer from the alloca
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}

							// Get field pointer with GEP
							mir::MIRType *structType = structTypes[currentReceiverType];
							mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, varExpr->name + ".ptr");

							// For array/unsized array fields, return pointer (for subsequent indexing or struct init)
							if (fieldType.size() > 2 && fieldType[0] == '[') {
								return fieldPtr;
							}

							// Load the field value for non-array fields
							mir::MIRType *fieldMIRType = getTypeFromString(fieldType);
							return builder->createLoad(fieldMIRType, fieldPtr, varExpr->name);
						}
					}
				}
			}
			throw std::runtime_error("Unknown variable name: " + varExpr->name);
		}

		// Determine the type to load
		std::string typeStr = variableTypes[varExpr->name];
		mir::MIRType *loadType;
		if (!typeStr.empty()) {
			loadType = getTypeFromString(typeStr);
		} else {
			loadType = mir::MIRType::getInt32();
		}

		// For array parameters (pointers), load the pointer type
		if (isArrayParam(typeStr)) {
			loadType = mir::MIRType::getPtr();
		}

		// For struct parameters, we need to load the pointer first, then load the struct
		// The alloca contains a pointer to the struct (passed by reference)
		if (isStructParameter(varExpr->name)) {
			// Load the pointer to the struct
			mir::MIRValue *structPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name + ".ptr");
			// Load the struct value through the pointer
			return builder->createLoad(loadType, structPtr, varExpr->name);
		}

		return builder->createLoad(loadType, alloca, varExpr->name);
	}

	if (dynamic_cast<StructInitExprAST *>(expr)) {
		throw std::runtime_error("Struct literal can only be used as an initializer in variable declarations");
	}

	if (auto *memberAccessExpr = dynamic_cast<MemberAccessExprAST *>(expr)) {
		std::string varType;
		mir::MIRValue *objectPtr = nullptr;

		if (memberAccessExpr->object) {
			// Complex expression (e.g., arr[0].field or self._str._len)
			if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(memberAccessExpr->object.get())) {
				// Array index expression (e.g., arr[0].field)
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
				if (arrayType.size() > 2 && arrayType[0] == '[') {
					size_t closeBracket = arrayType.find(']');
					if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
						varType = arrayType.substr(closeBracket + 1);
					}
				}
				objectPtr = objectValue;
			} else if (auto *nestedMemberExpr = dynamic_cast<MemberAccessExprAST *>(memberAccessExpr->object.get())) {
				// Nested member access (e.g., self._str in self._str._len)
				// We need to get a pointer to the intermediate struct field, NOT load it!
				(void)nestedMemberExpr; // Used for type check above

				// First, determine the base object and its type
				std::string baseObjType;
				mir::MIRValue *basePtr = nullptr;

				// Collect the chain of member accesses
				std::vector<MemberAccessExprAST *> chain;
				ExprAST *current = memberAccessExpr->object.get();
				while (auto *ma = dynamic_cast<MemberAccessExprAST *>(current)) {
					chain.push_back(ma);
					if (ma->object) {
						current = ma->object.get();
					} else {
						// Reached the base variable
						baseObjType = variableTypes[ma->objectName];
						mir::MIRValue *structAlloca = namedValues[ma->objectName];
						if (!structAlloca) {
							// Check for implicit field access
							if (!currentReceiverType.empty()) {
								auto fieldsIt = structFields.find(currentReceiverType);
								if (fieldsIt != structFields.end()) {
									int fieldIndex = -1;
									std::string fieldType;
									for (size_t i = 0; i < fieldsIt->second.size(); i++) {
										if (fieldsIt->second[i].first == ma->objectName) {
											fieldIndex = static_cast<int>(i);
											fieldType = fieldsIt->second[i].second;
											break;
										}
									}
									if (fieldIndex >= 0) {
										mir::MIRValue *selfAlloca = namedValues["self"];
										if (selfAlloca) {
											mir::MIRValue *selfPtr;
											if (isStructParameter("self")) {
												selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
											} else {
												selfPtr = selfAlloca;
											}
											mir::MIRType *structType = structTypes[currentReceiverType];
											basePtr = builder->createStructGEP(structType, selfPtr, fieldIndex, ma->objectName + ".ptr");
											baseObjType = fieldType;
											break;
										}
									}
								}
							}
							if (!basePtr) {
								throw std::runtime_error("Unknown variable: " + ma->objectName);
							}
						} else {
							if (isStructParameter(ma->objectName)) {
								basePtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
							} else {
								basePtr = structAlloca;
							}
						}
						break;
					}
				}

				// Now traverse the chain from base to get the pointer to the intermediate field
				// The chain is in reverse order, so we need to reverse it
				std::reverse(chain.begin(), chain.end());

				mir::MIRValue *currentPtr = basePtr;
				std::string currentType = baseObjType;

				for (auto *ma : chain) {
					// Get the struct type
					if (structTypes.find(currentType) == structTypes.end()) {
						throw std::runtime_error("Expected struct type for member access: " + currentType);
					}
					mir::MIRType *structType = structTypes[currentType];
					const auto &fields = structFields[currentType];

					// Find the field index
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fields.size(); i++) {
						if (fields[i].first == ma->memberName) {
							fieldIndex = static_cast<int>(i);
							fieldType = fields[i].second;
							break;
						}
					}

					if (fieldIndex < 0) {
						throw std::runtime_error("Unknown field '" + ma->memberName + "' in struct '" + currentType + "'");
					}

					// GEP to get pointer to the field
					currentPtr = builder->createStructGEP(structType, currentPtr, fieldIndex, ma->memberName);
					currentType = fieldType;
				}

				// Now objectPtr points to the intermediate struct field
				objectPtr = currentPtr;
				varType = currentType;
			} else {
				// Other complex expression
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				varType = getExpressionMaxonType(memberAccessExpr->object.get());
				objectPtr = objectValue;
			}
		} else {
			// Simple variable access
			varType = variableTypes[memberAccessExpr->objectName];
			mir::MIRValue *structAlloca = namedValues[memberAccessExpr->objectName];
			if (!structAlloca) {
				// Check for implicit field access
				if (!currentReceiverType.empty()) {
					auto fieldsIt = structFields.find(currentReceiverType);
					if (fieldsIt != structFields.end()) {
						int fieldIndex = -1;
						std::string fieldType;
						for (size_t i = 0; i < fieldsIt->second.size(); i++) {
							if (fieldsIt->second[i].first == memberAccessExpr->objectName) {
								fieldIndex = static_cast<int>(i);
								fieldType = fieldsIt->second[i].second;
								break;
							}
						}
						if (fieldIndex >= 0) {
							mir::MIRValue *selfAlloca = namedValues["self"];
							if (selfAlloca) {
								mir::MIRValue *selfPtr;
								if (isStructParameter("self")) {
									selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
								} else {
									selfPtr = selfAlloca;
								}
								mir::MIRType *structType = structTypes[currentReceiverType];
								objectPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, memberAccessExpr->objectName + ".ptr");
								varType = fieldType;
								goto handle_member_access;
							}
						}
					}
				}
				throw std::runtime_error("Unknown variable: " + memberAccessExpr->objectName);
			}

			// Check if this is a pointer to struct or struct directly
			std::string storedType = variableTypes[memberAccessExpr->objectName];
			if (isArrayParam(storedType)) {
				// It's an array parameter - load the pointer first
				objectPtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
			} else if (isStructParameter(memberAccessExpr->objectName)) {
				// Struct parameters are passed by pointer - load the pointer first
				objectPtr = builder->createLoad(mir::MIRType::getPtr(), structAlloca, "struct.ptr");
			} else {
				objectPtr = structAlloca;
			}
		}

	handle_member_access:

		// Check for struct member access
		if (structTypes.find(varType) != structTypes.end()) {
			mir::MIRType *structType = structTypes[varType];
			const auto &fields = structFields[varType];

			int fieldIndex = -1;
			for (size_t i = 0; i < fields.size(); i++) {
				if (fields[i].first == memberAccessExpr->memberName) {
					fieldIndex = static_cast<int>(i);
					break;
				}
			}

			if (fieldIndex < 0) {
				throw std::runtime_error("Unknown field '" + memberAccessExpr->memberName +
										 "' in struct '" + varType + "'");
			}

			// GEP to get field pointer
			mir::MIRValue *fieldPtr = builder->createStructGEP(structType, objectPtr,
															   fieldIndex, memberAccessExpr->memberName);

			// Check if the field is an array type - return pointer instead of loading
			std::string fieldTypeStr = fields[fieldIndex].second;
			if (fieldTypeStr.size() > 2 && fieldTypeStr[0] == '[') {
				// Array field - return pointer to the array (for subsequent indexing)
				return fieldPtr;
			}

			// Load the field value
			mir::MIRType *fieldType = structType->fieldTypes[fieldIndex];
			return builder->createLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
		}

		// Handle array.length
		if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
			// First check for hidden __length alloca (stack-allocated arrays)
			std::string lengthVar = memberAccessExpr->objectName + ".__length";
			mir::MIRValue *lengthAlloca = namedValues[lengthVar];
			if (lengthAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
			}

			// For heap-allocated arrays with header layout, read from dataPtr - 8
			// Layout: [length:i32][capacity:i32][...data...]
			//              -8          -4           0+
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				// Load the data pointer
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				// Get pointer to length at offset -8 (use GEP with i8 type for byte offsets)
				mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
															  {builder->getInt64(-8)}, "length.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");
			}
			throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
		}

		// Handle array.capacity (dynamic arrays only)
		if (memberAccessExpr->memberName == "capacity" && !memberAccessExpr->object) {
			// First check for hidden __capacity alloca (old-style heap arrays)
			std::string capacityVar = memberAccessExpr->objectName + ".__capacity";
			mir::MIRValue *capacityAlloca = namedValues[capacityVar];
			if (capacityAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");
			}

			// For heap-allocated arrays with header layout, read from dataPtr - 4
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				// Load the data pointer
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				// Get pointer to capacity at offset -4 (use GEP with i8 type for byte offsets)
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
														   {builder->getInt64(-4)}, "cap.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), capPtr, "capacity");
			}

			// Static arrays don't have capacity - capacity == length
			std::string lengthVar = memberAccessExpr->objectName + ".__length";
			mir::MIRValue *lengthAlloca = namedValues[lengthVar];
			if (lengthAlloca) {
				return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "capacity");
			}
			throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
		}

		throw std::runtime_error("Unknown member: " + memberAccessExpr->memberName);
	}

	if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		mir::MIRValue *indexVal = generateExpr(arrayIndexExpr->index.get());
		if (!indexVal) {
			throw std::runtime_error("Failed to generate array index");
		}

		// Handle complex array access (e.g., struct.field[i])
		if (arrayIndexExpr->hasArrayExpr()) {
			// Generate the array expression (e.g., struct.field)
			mir::MIRValue *arrayVal = generateExpr(arrayIndexExpr->arrayExpr.get());
			if (!arrayVal) {
				throw std::runtime_error("Failed to generate array expression");
			}

			// For member access on struct, the result is a pointer to the array field
			// We need to determine the element type from the expression
			std::string elementTypeStr = "byte"; // Default for now
			bool isUnsizedArray = false;

			// Get the array field type using getExpressionMaxonType for nested member access
			std::string arrayFieldType = getExpressionMaxonType(arrayIndexExpr->arrayExpr.get());
			logTrace("ArrayIndex in '" + currentReceiverType + "': arrayFieldType = '" + arrayFieldType + "'");
			if (arrayFieldType.size() > 2 && arrayFieldType[0] == '[') {
				size_t closeBracket = arrayFieldType.find(']');
				if (closeBracket != std::string::npos && closeBracket + 1 < arrayFieldType.size()) {
					std::string sizeStr = arrayFieldType.substr(1, closeBracket - 1);
					elementTypeStr = arrayFieldType.substr(closeBracket + 1);
					// Check for unsized array: []type (empty size)
					isUnsizedArray = sizeStr.empty();
					logTrace("ArrayIndex in '" + currentReceiverType + "': elementTypeStr = '" + elementTypeStr + "', isUnsizedArray = " + (isUnsizedArray ? "true" : "false"));
				}
			}

			mir::MIRType *elementType = getTypeFromString(elementTypeStr);

			// For unsized arrays, arrayVal points to the fat pointer struct {ptr, i32}
			// We need to extract the data pointer (field 0) and load it
			mir::MIRValue *dataPtr = arrayVal;
			if (isUnsizedArray) {
				// Get the unsized array struct type
				mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
					"__unsized_array_" + elementTypeStr,
					{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
				// Get pointer to the data pointer field (field 0)
				mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, arrayVal, 0, "unsized.data.ptr");
				// Load the actual data pointer
				dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrField, "unsized.data");
			}

			mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, dataPtr, indexVal, "fieldarray.idx");
			return builder->createLoad(elementType, elementPtr, "fieldarray.elem");
		}

		// Simple array[i] access
		mir::MIRValue *alloca = namedValues[arrayIndexExpr->arrayName];
		if (!alloca) {
			// If we're in a method and array not found, check if it's a struct field
			// This enables implicit self field access for arrays: 'data[i]' instead of 'self.data[i]'
			if (!currentReceiverType.empty()) {
				auto fieldsIt = structFields.find(currentReceiverType);
				if (fieldsIt != structFields.end()) {
					int fieldIndex = -1;
					std::string fieldType;
					for (size_t i = 0; i < fieldsIt->second.size(); i++) {
						if (fieldsIt->second[i].first == arrayIndexExpr->arrayName) {
							fieldIndex = static_cast<int>(i);
							fieldType = fieldsIt->second[i].second;
							break;
						}
					}

					if (fieldIndex >= 0 && fieldType.size() > 2 && fieldType[0] == '[') {
						// Found array field - generate access through implicit 'self' parameter
						mir::MIRValue *selfAlloca = namedValues["self"];
						if (selfAlloca) {
							mir::MIRValue *selfPtr;
							if (isStructParameter("self")) {
								selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
							} else {
								selfPtr = selfAlloca;
							}

							// Get field pointer with GEP
							mir::MIRType *structType = structTypes[currentReceiverType];
							mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, arrayIndexExpr->arrayName + ".ptr");

							// Determine element type
							size_t closeBracket = fieldType.find(']');
							std::string elementTypeStr = fieldType.substr(closeBracket + 1);
							std::string sizeStr = fieldType.substr(1, closeBracket - 1);
							bool isUnsizedArray = sizeStr.empty();
							mir::MIRType *elementType = getTypeFromString(elementTypeStr);

							// For unsized arrays, load the data pointer from fat pointer
							mir::MIRValue *dataPtr = fieldPtr;
							if (isUnsizedArray) {
								mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
									"__unsized_array_" + elementTypeStr,
									{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
								mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, "unsized.data.ptr");
								dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrField, "unsized.data");
							}

							mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, dataPtr, indexVal, "implicitfield.array.idx");
							return builder->createLoad(elementType, elementPtr, "implicitfield.array.elem");
						}
					}
				}
			}
			throw std::runtime_error("Unknown array name: " + arrayIndexExpr->arrayName);
		}

		// Determine element type
		std::string elementTypeStr = "int";
		std::string varType = variableTypes[arrayIndexExpr->arrayName];
		if (varType.size() > 2 && varType[0] == '[') {
			size_t closeBracket = varType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
				elementTypeStr = varType.substr(closeBracket + 1);
			}
		}
		mir::MIRType *elementType = getTypeFromString(elementTypeStr);
		bool isStructElement = structTypes.find(elementTypeStr) != structTypes.end();

		// For stack-allocated arrays, alloca IS the array pointer directly
		// For heap-allocated arrays, alloca holds a pointer that must be loaded
		mir::MIRValue *arrayPtr;
		if (stackAllocatedArrays.count(arrayIndexExpr->arrayName) > 0) {
			// Stack array: alloca is directly the array memory
			arrayPtr = alloca;
		} else {
			// Heap array: load the pointer from alloca
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		}
		mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");

		if (isStructElement) {
			return elementPtr; // Return pointer for struct elements
		} else {
			return builder->createLoad(elementType, elementPtr, "arrayelem");
		}
	}

	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		// For == and != on Equatable struct types, route to equals method
		if (binExpr->op == 'E' || binExpr->op == 'N') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			// Check if this is an Equatable struct type
			auto structIt = structTypes.find(leftType);
			if (structIt != structTypes.end()) {
				auto conformsIt = structConformsTo.find(leftType);
				if (conformsIt != structConformsTo.end()) {
					bool isEquatable = false;
					for (const auto &iface : conformsIt->second) {
						if (iface == "Equatable") {
							isEquatable = true;
							break;
						}
					}

					if (isEquatable) {
						// Generate call to TypeName.equals(left, right)
						std::string equalsFuncName = leftType + ".equals";
						mir::MIRFunction *equalsFunc = module->getFunction(equalsFuncName);

						if (equalsFunc) {
							// For struct types, we need to pass pointers, not loaded values
							// Get pointer to left operand
							mir::MIRValue *leftPtr = nullptr;
							if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
								leftPtr = namedValues[varExpr->name];
							} else if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(binExpr->left.get())) {
								leftPtr = generateStringLiteral(strLit);
							} else {
								leftPtr = generateExpr(binExpr->left.get());
							}

							// Get pointer to right operand
							mir::MIRValue *rightPtr = nullptr;
							if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
								rightPtr = namedValues[varExpr->name];
							} else if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(binExpr->right.get())) {
								rightPtr = generateStringLiteral(strLit);
							} else {
								rightPtr = generateExpr(binExpr->right.get());
							}

							if (!leftPtr || !rightPtr) {
								throw std::runtime_error("Failed to generate Equatable comparison operands");
							}

							std::vector<mir::MIRValue *> args = {leftPtr, rightPtr};
							mir::MIRValue *result = builder->createCall(equalsFunc, args, "eqtmp");

							// For !=, negate the result
							if (binExpr->op == 'N') {
								result = builder->createICmpEq(result,
															   mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0), "neqtmp");
							}

							return result;
						}
					}
				}
			}
		}

		// For <, >, <=, >= on char type, compare by first codepoint value
		// This provides lexicographic ordering for single-codepoint chars
		if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || binExpr->op == 'G') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			if (leftType == "char") {
				// Get pointers to both char structs
				mir::MIRValue *leftPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
					leftPtr = namedValues[varExpr->name];
				} else if (auto *charLit = dynamic_cast<CharacterExprAST *>(binExpr->left.get())) {
					leftPtr = generateCharLiteral(charLit);
				} else {
					leftPtr = generateExpr(binExpr->left.get());
				}

				mir::MIRValue *rightPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
					rightPtr = namedValues[varExpr->name];
				} else if (auto *charLit = dynamic_cast<CharacterExprAST *>(binExpr->right.get())) {
					rightPtr = generateCharLiteral(charLit);
				} else {
					rightPtr = generateExpr(binExpr->right.get());
				}

				if (!leftPtr || !rightPtr) {
					throw std::runtime_error("Failed to generate char comparison operands");
				}

				// Extract first byte from each char to compare (for ASCII/simple chars)
				// char = { _managed ptr } where _managed points to __ManagedStringData
				mir::MIRType *charType = structTypes["char"];
				mir::MIRType *managedDataType = structTypes["__ManagedStringData"];
				mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];

				if (!managedDataType || !unsizedArrayType) {
					throw std::runtime_error("char comparison requires __ManagedStringData type");
				}

				// Get first byte from left char
				mir::MIRValue *leftManagedPtr = builder->createStructGEP(charType, leftPtr, 0, "left.managed.ptr");
				mir::MIRValue *leftManaged = builder->createLoad(mir::MIRType::getPtr(), leftManagedPtr, "left.managed");
				mir::MIRValue *leftBufferPtr = builder->createStructGEP(managedDataType, leftManaged, 0, "left.buffer");
				mir::MIRValue *leftDataPtrPtr = builder->createStructGEP(unsizedArrayType, leftBufferPtr, 0, "left.data.ptr.ptr");
				mir::MIRValue *leftDataPtr = builder->createLoad(mir::MIRType::getPtr(), leftDataPtrPtr, "left.data.ptr");
				mir::MIRValue *leftByte = builder->createLoad(mir::MIRType::getInt8(), leftDataPtr, "left.byte");
				mir::MIRValue *leftVal = builder->createZExt(leftByte, mir::MIRType::getInt32(), "left.val");

				// Get first byte from right char
				mir::MIRValue *rightManagedPtr = builder->createStructGEP(charType, rightPtr, 0, "right.managed.ptr");
				mir::MIRValue *rightManaged = builder->createLoad(mir::MIRType::getPtr(), rightManagedPtr, "right.managed");
				mir::MIRValue *rightBufferPtr = builder->createStructGEP(managedDataType, rightManaged, 0, "right.buffer");
				mir::MIRValue *rightDataPtrPtr = builder->createStructGEP(unsizedArrayType, rightBufferPtr, 0, "right.data.ptr.ptr");
				mir::MIRValue *rightDataPtr = builder->createLoad(mir::MIRType::getPtr(), rightDataPtrPtr, "right.data.ptr");
				mir::MIRValue *rightByte = builder->createLoad(mir::MIRType::getInt8(), rightDataPtr, "right.byte");
				mir::MIRValue *rightVal = builder->createZExt(rightByte, mir::MIRType::getInt32(), "right.val");

				// Compare the byte values
				switch (binExpr->op) {
				case '<':
					return builder->createICmpSLT(leftVal, rightVal, "lttmp");
				case '>':
					return builder->createICmpSGT(leftVal, rightVal, "gttmp");
				case 'L': // <=
					return builder->createICmpSLE(leftVal, rightVal, "letmp");
				case 'G': // >=
					return builder->createICmpSGE(leftVal, rightVal, "getmp");
				}
			}
		}

		// Special handling for string + string -> call concat and heap-allocate result
		if (binExpr->op == '+') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());
			std::string rightType = getExpressionMaxonType(binExpr->right.get());

			if (leftType == "string" && rightType == "string") {
				// Initialize heap management for _managed_string_alloc
				initHeapManagement();

				// Get pointers to both string structs
				mir::MIRValue *leftPtr = nullptr;
				mir::MIRValue *rightPtr = nullptr;

				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
					leftPtr = namedValues[varExpr->name];
					if (isStructParameter(varExpr->name)) {
						leftPtr = builder->createLoad(mir::MIRType::getPtr(), leftPtr, "left_str");
					}
				} else {
					leftPtr = generateExpr(binExpr->left.get());
				}

				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
					rightPtr = namedValues[varExpr->name];
					if (isStructParameter(varExpr->name)) {
						rightPtr = builder->createLoad(mir::MIRType::getPtr(), rightPtr, "right_str");
					}
				} else {
					rightPtr = generateExpr(binExpr->right.get());
				}

				mir::MIRType *stringType = structTypes["string"];
				mir::MIRType *unsizedArrayType = structTypes["__unsized_array_byte"];
				mir::MIRType *managedStringDataType = structTypes["__ManagedStringData"];
				if (!managedStringDataType) {
					managedStringDataType = module->getOrCreateStructType(
						"__ManagedStringData",
						{unsizedArrayType, mir::MIRType::getInt32(), mir::MIRType::getInt32()});
					structTypes["__ManagedStringData"] = managedStringDataType;
				}

				// Get _managed pointers from both strings (field 0 of string is _managed ptr)
				mir::MIRValue *leftManagedFieldPtr = builder->createStructGEP(stringType, leftPtr, 0, "left_managed_field");
				mir::MIRValue *leftManagedPtr = builder->createLoad(mir::MIRType::getPtr(), leftManagedFieldPtr, "left_managed_ptr");
				mir::MIRValue *rightManagedFieldPtr = builder->createStructGEP(stringType, rightPtr, 0, "right_managed_field");
				mir::MIRValue *rightManagedPtr = builder->createLoad(mir::MIRType::getPtr(), rightManagedFieldPtr, "right_managed_ptr");

				// Get lengths from __ManagedStringData (field 1 = _len)
				mir::MIRValue *leftLenPtr = builder->createStructGEP(managedStringDataType, leftManagedPtr, 1, "left_len_ptr");
				mir::MIRValue *leftLen = builder->createLoad(mir::MIRType::getInt32(), leftLenPtr, "left_len");
				mir::MIRValue *rightLenPtr = builder->createStructGEP(managedStringDataType, rightManagedPtr, 1, "right_len_ptr");
				mir::MIRValue *rightLen = builder->createLoad(mir::MIRType::getInt32(), rightLenPtr, "right_len");

				// Calculate combined length
				mir::MIRValue *combinedLen = builder->createAdd(leftLen, rightLen, "combined_len");

				// Allocate heap buffer with refcount header using _managed_string_alloc
				mir::MIRFunction *stringAllocFunc = module->getFunction("_managed_string_alloc");
				if (!stringAllocFunc) {
					stringAllocFunc = builder->declareFunction("_managed_string_alloc",
															   mir::MIRType::getPtr(),
															   {mir::MIRType::getInt64(), mir::MIRType::getPtr()});
				}
				// Create tag for tracking
				mir::MIRValue *tag = module->createGlobalString(".__tag.str.concat", "string concat");

				// Allocate combinedLen + 1 for null terminator
				mir::MIRValue *combinedLenPlus1 = builder->createAdd(combinedLen, builder->getInt32(1), "combined_len_plus1");
				mir::MIRValue *combinedLenPlus1_64 = builder->createSExt(combinedLenPlus1, mir::MIRType::getInt64(), "combined_len64");
				mir::MIRValue *newDataPtr = builder->createCall(stringAllocFunc, {combinedLenPlus1_64, tag}, "concat_alloc");

				// Get memcpy function
				mir::MIRFunction *memcpyFunc = module->getFunction("memcpy");

				// Copy left string data - get buffer ptr from __ManagedStringData
				mir::MIRValue *leftBufFieldPtr = builder->createStructGEP(managedStringDataType, leftManagedPtr, 0, "left_buf_field");
				mir::MIRValue *leftDataPtrPtr = builder->createStructGEP(unsizedArrayType, leftBufFieldPtr, 0, "left_data_ptr_ptr");
				mir::MIRValue *leftDataPtr = builder->createLoad(mir::MIRType::getPtr(), leftDataPtrPtr, "left_data_ptr");
				mir::MIRValue *leftLen64 = builder->createSExt(leftLen, mir::MIRType::getInt64(), "left_len64");
				builder->createCall(memcpyFunc, {newDataPtr, leftDataPtr, leftLen64}, "copy_left");

				// Copy right string data (at offset = leftLen)
				mir::MIRValue *destOffset = builder->createGEP(mir::MIRType::getInt8(), newDataPtr, {leftLen64}, "dest_offset");
				mir::MIRValue *rightBufFieldPtr = builder->createStructGEP(managedStringDataType, rightManagedPtr, 0, "right_buf_field");
				mir::MIRValue *rightDataPtrPtr = builder->createStructGEP(unsizedArrayType, rightBufFieldPtr, 0, "right_data_ptr_ptr");
				mir::MIRValue *rightDataPtr = builder->createLoad(mir::MIRType::getPtr(), rightDataPtrPtr, "right_data_ptr");
				mir::MIRValue *rightLen64 = builder->createSExt(rightLen, mir::MIRType::getInt64(), "right_len64");
				builder->createCall(memcpyFunc, {destOffset, rightDataPtr, rightLen64}, "copy_right");

				// Write null terminator at newDataPtr[combinedLen]
				mir::MIRValue *combinedLen64 = builder->createSExt(combinedLen, mir::MIRType::getInt64(), "combined_len64_for_null");
				mir::MIRValue *nullTermPtr = builder->createGEP(mir::MIRType::getInt8(), newDataPtr, {combinedLen64}, "null_term_ptr");
				builder->createStore(builder->getInt8(0), nullTermPtr);

				// Heap-allocate the __ManagedStringData struct for the result
				mir::MIRFunction *mallocFunc = getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});
				mir::MIRValue *managedSize = builder->getInt64(24);
				mir::MIRValue *newManagedPtr = builder->createCall(mallocFunc, {managedSize}, "concat_managed");

				// Set up the __ManagedStringData struct
				// Field 0: _buffer (fat pointer)
				mir::MIRValue *newBufFieldPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 0, "new_buf_field");
				mir::MIRValue *newBufPtrPtr = builder->createStructGEP(unsizedArrayType, newBufFieldPtr, 0, "new_buf_ptr_ptr");
				builder->createStore(newDataPtr, newBufPtrPtr);
				mir::MIRValue *newBufLenPtr = builder->createStructGEP(unsizedArrayType, newBufFieldPtr, 1, "new_buf_len_ptr");
				builder->createStore(combinedLen, newBufLenPtr);

				// Field 1: _len
				mir::MIRValue *newLenPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 1, "new_len_ptr");
				builder->createStore(combinedLen, newLenPtr);

				// Field 2: _capacity
				mir::MIRValue *newCapPtr = builder->createStructGEP(managedStringDataType, newManagedPtr, 2, "new_cap_ptr");
				builder->createStore(combinedLen, newCapPtr);

				// Create the result string struct on stack
				mir::MIRValue *resultAlloca = builder->createAlloca(stringType, "concat_result");

				// Field 0: _managed (ptr to __ManagedStringData)
				mir::MIRValue *resultManagedFieldPtr = builder->createStructGEP(stringType, resultAlloca, 0, "result_managed_field");
				builder->createStore(newManagedPtr, resultManagedFieldPtr);

				// Track for cleanup at scope exit - track both the buffer and the managed struct
				mir::MIRValue *dataPtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), "concat.heap.ptr");
				builder->createStore(newDataPtr, dataPtrAlloca);
				if (!scopeStack.empty()) {
					scopeStack.back().heapAllocatedStrings.push_back({"concat.heap", dataPtrAlloca});
				}

				return resultAlloca;
			}
		}

		mir::MIRValue *left = generateExpr(binExpr->left.get());
		mir::MIRValue *right = generateExpr(binExpr->right.get());

		if (!left || !right) {
			throw std::runtime_error("Failed to generate binary expression operands");
		}

		bool leftIsFloat = left->type->isFloat();
		bool rightIsFloat = right->type->isFloat();
		bool needsFloatOp = leftIsFloat || rightIsFloat;

		// Promote int to float if mixed types
		if (needsFloatOp) {
			if (!leftIsFloat) {
				left = builder->createSIToFP(left, mir::MIRType::getFloat64(), "promotetmp");
			}
			if (!rightIsFloat) {
				right = builder->createSIToFP(right, mir::MIRType::getFloat64(), "promotetmp");
			}
		}

		// Normalize byte to int for comparisons (zero-extend to preserve unsigned semantics)
		if (!needsFloatOp && left->type->isInteger() && right->type->isInteger()) {
			uint64_t leftBits = left->type->sizeInBytes * 8;
			uint64_t rightBits = right->type->sizeInBytes * 8;
			if (leftBits < rightBits) {
				left = builder->createZExt(left, right->type, "zexttmp");
			} else if (rightBits < leftBits) {
				right = builder->createZExt(right, left->type, "zexttmp");
			}
		}

		switch (binExpr->op) {
		case '+':
			return needsFloatOp ? builder->createFAdd(left, right, "faddtmp")
								: builder->createAdd(left, right, "addtmp");
		case '-':
			return needsFloatOp ? builder->createFSub(left, right, "fsubtmp")
								: builder->createSub(left, right, "subtmp");
		case '*':
			return needsFloatOp ? builder->createFMul(left, right, "fmultmp")
								: builder->createMul(left, right, "multmp");
		case '/':
			return needsFloatOp ? builder->createFDiv(left, right, "fdivtmp")
								: builder->createSDiv(left, right, "divtmp");
		case '%':
			return builder->createSRem(left, right, "modtmp");
		case '>':
			return needsFloatOp ? builder->createFCmpGT(left, right, "fcmptmp")
								: builder->createICmpSGT(left, right, "cmptmp");
		case '<':
			return needsFloatOp ? builder->createFCmpLT(left, right, "fcmptmp")
								: builder->createICmpSLT(left, right, "cmptmp");
		case 'G': // >=
			return needsFloatOp ? builder->createFCmpGE(left, right, "fcmptmp")
								: builder->createICmpSGE(left, right, "cmptmp");
		case 'L': // <=
			return needsFloatOp ? builder->createFCmpLE(left, right, "fcmptmp")
								: builder->createICmpSLE(left, right, "cmptmp");
		case 'E': // ==
			return needsFloatOp ? builder->createFCmpEq(left, right, "fcmptmp")
								: builder->createICmpEq(left, right, "cmptmp");
		case 'N': // !=
			return needsFloatOp ? builder->createFCmpNe(left, right, "fcmptmp")
								: builder->createICmpNe(left, right, "cmptmp");
		case '&': // bitwise AND
			return builder->createAnd(left, right, "bandtmp");
		case '|': // bitwise OR
			return builder->createOr(left, right, "bortmp");
		case '^': // bitwise XOR
			return builder->createXor(left, right, "bxortmp");
		case 'S': // shift left (<<)
			return builder->createShl(left, right, "shltmp");
		case 'H': // shift right (>>) - logical shift for unsigned semantics
			return builder->createLShr(left, right, "lshrtmp");
		case 'A': // logical AND (and keyword)
			// Both operands should be booleans (i1 or i32)
			// Result is 1 if both are non-zero, 0 otherwise
			return builder->createAnd(left, right, "andtmp");
		case 'O': // logical OR (or keyword)
			// Result is 1 if either is non-zero, 0 otherwise
			return builder->createOr(left, right, "ortmp");
		default:
			throw std::runtime_error("Unknown binary operator");
		}
	}

	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		mir::MIRValue *operand = generateExpr(unaryExpr->operand.get());
		if (!operand) {
			throw std::runtime_error("Failed to generate unary expression operand");
		}

		bool isFloat = operand->type->isFloat();

		switch (unaryExpr->op) {
		case '-':
			if (isFloat) {
				return builder->createFNeg(operand, "fnegtmp");
			} else {
				return builder->createNeg(operand, "negtmp");
			}
		case '+':
			return operand;
		case '!': // logical not
			// Result is 1 if operand is 0, 0 otherwise
			return builder->createICmpEq(operand,
										 mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0), "nottmp");
		default:
			throw std::runtime_error("Unknown unary operator");
		}
	}

	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Handle math intrinsics
		if (Lexer::isMathIntrinsic(callExpr->callee)) {
			return generateMathIntrinsic(callExpr);
		}

		// Handle array intrinsics (push, pop)
		if (callExpr->callee == "push" || callExpr->callee == "pop") {
			return generateArrayIntrinsic(callExpr);
		}

		// Map method calls are now handled through monomorphization
		// The specialized methods (e.g., map<string,int>.insert) are generated as regular functions
		// Map method calls are now handled through monomorphization - use resolvedCallee if available
		std::string effectiveCallee = callExpr->resolvedCallee.empty() ? callExpr->callee : callExpr->resolvedCallee;

		// Resolve primitive type methods when called on typed arguments
		// This handles cases like `key.hash()` where key is a type parameter bound to int
		// Also handles `_keys[index].equals(key)` where _keys is []KeyType
		if ((effectiveCallee == "hash" || effectiveCallee == "equals") && !callExpr->args.empty()) {
			// Get the type of the first argument (the receiver in method call syntax)
			std::string argType;

			// Helper to look up type of a variable (local var, param, or struct field)
			auto lookupVarType = [&](const std::string &varName) -> std::string {
				// First check local variables and parameters
				auto typeIt = variableTypes.find(varName);
				if (typeIt != variableTypes.end()) {
					return typeIt->second;
				}
				// Check struct fields if we're inside a method
				if (!currentReceiverType.empty()) {
					auto fieldsIt = structFields.find(currentReceiverType);
					if (fieldsIt != structFields.end()) {
						for (const auto &field : fieldsIt->second) {
							if (field.first == varName) {
								return field.second;
							}
						}
					}
				}
				return "";
			};

			// Check if first arg is a variable
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].get())) {
				argType = lookupVarType(varExpr->name);
			}
			// Check if first arg is an array index (e.g., _keys[index])
			else if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(callExpr->args[0].get())) {
				std::string arrayName;
				if (!arrayIndexExpr->arrayName.empty()) {
					arrayName = arrayIndexExpr->arrayName;
				} else if (arrayIndexExpr->arrayExpr) {
					// Complex array access like struct.field[i]
					if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayIndexExpr->arrayExpr.get())) {
						arrayName = varExpr->name;
					}
				}

				if (!arrayName.empty()) {
					std::string arrayType = lookupVarType(arrayName);
					// Extract element type from array type like "[]int" or "[]KeyType"
					if (arrayType.size() > 2 && arrayType.substr(0, 2) == "[]") {
						argType = arrayType.substr(2);
					}
				}
			}

			// Check if the argument type is a type parameter that has been bound
			auto typeBindingIt = currentTypeBindings.find(argType);
			if (typeBindingIt != currentTypeBindings.end()) {
				argType = typeBindingIt->second;
			}

			// Check if it's a primitive type that has hash/equals
			if (argType == "int" || argType == "byte" || argType == "char") {
				effectiveCallee = argType + "." + effectiveCallee;
			}
		}

		// Handle string/substring/cstring intrinsics via registry
		if (auto method = IntrinsicCodegenRegistry::instance().getMethod(effectiveCallee)) {
			return (this->*method)(callExpr);
		}

		// Handle primitive type methods (int.hash, int.equals, etc.)
		if (effectiveCallee == "int.hash" || effectiveCallee == "byte.hash" || effectiveCallee == "char.hash") {
			// Multiplicative hash using Knuth's golden ratio constant
			// hash = value * 2654435769 (which is 0x9E3779B9)
			if (callExpr->args.empty()) {
				throw std::runtime_error("hash() requires self argument");
			}
			mir::MIRValue *value = generateExpr(callExpr->args[0].get());
			// Extend byte/char to int if needed
			if (effectiveCallee != "int.hash") {
				value = builder->createZExt(value, mir::MIRType::getInt32(), "hash.extend");
			}
			mir::MIRValue *multiplier = mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0x9E3779B9);
			return builder->createMul(value, multiplier, "hash");
		}

		if (effectiveCallee == "int.equals" || effectiveCallee == "byte.equals" || effectiveCallee == "char.equals") {
			// Simple equality comparison
			if (callExpr->args.size() < 2) {
				throw std::runtime_error("equals() requires self and other arguments");
			}
			mir::MIRValue *self = generateExpr(callExpr->args[0].get());
			mir::MIRValue *other = generateExpr(callExpr->args[1].get());
			return builder->createICmpEq(self, other, "equals");
		}

		// Check if this is an extern function that should go through Safe FFI
		bool usesSafeFfi = externFunctions.find(effectiveCallee) != externFunctions.end();

		// Look up function - use O(1) function ID lookup if available
		mir::MIRFunction *calleeF = nullptr;

		if (callExpr->functionId != SIZE_MAX) {
			// Fast path: function was resolved during semantic analysis
			auto it = functionIdToMIR.find(callExpr->functionId);
			if (it != functionIdToMIR.end()) {
				calleeF = it->second;
			}
		}

		// Fallback to name-based lookup if function ID not available
		if (!calleeF) {
			calleeF = module->getFunction(effectiveCallee);

			// If not found, try suffix matching for namespaced functions
			if (!calleeF && effectiveCallee.find(".") == std::string::npos) {
				std::string searchSuffix = "." + effectiveCallee;
				for (auto &func : module->functions) {
					const std::string &funcName = func->name;
					if (funcName.size() > searchSuffix.size() &&
						funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
						calleeF = func.get();
						// Also check if namespaced version is extern
						if (externFunctions.find(funcName) != externFunctions.end()) {
							usesSafeFfi = true;
						}
						break;
					}
				}
			}
		}

		if (!calleeF) {
			throw std::runtime_error("Unknown function referenced: " + effectiveCallee);
		}

		// Generate arguments
		std::vector<mir::MIRValue *> argsV;
		size_t argIdx = 0;

		// For sibling method calls, inject 'self' as the first argument
		if (callExpr->isSiblingMethodCall && !currentReceiverType.empty()) {
			// Load self pointer from the first parameter (which is always 'self' in methods)
			if (namedValues.find("self") != namedValues.end()) {
				mir::MIRValue *selfAlloca = namedValues["self"];
				mir::MIRValue *selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self");
				argsV.push_back(selfPtr);
				argIdx++;
			}
		}

		for (size_t i = 0; i < callExpr->args.size(); i++) {
			auto &arg = callExpr->args[i];
			mir::MIRType *paramType = (argIdx < calleeF->parameters.size())
										  ? calleeF->parameters[argIdx]->type
										  : mir::MIRType::getInt32();

			// Handle array and struct arguments
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg.get())) {
				std::string varType = variableTypes[varExpr->name];
				mir::MIRValue *alloca = namedValues[varExpr->name];

				if (alloca && isArrayParam(varType)) {
					// Pass array pointer and length
					mir::MIRValue *ptrVal;
					if (stackAllocatedArrays.count(varExpr->name) > 0) {
						// Stack-allocated array: alloca IS the array pointer
						ptrVal = alloca;
					} else {
						// Heap-allocated array: alloca contains a pointer to the array
						ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
					}
					argsV.push_back(ptrVal);
					argIdx++;

					// Pass hidden length parameter if expected by the callee
					// Check if the callee's next parameter is a hidden length param (name ends with ".__length")
					if (argIdx < calleeF->parameters.size()) {
						mir::MIRValue *nextParam = calleeF->parameters[argIdx];
						bool expectsHiddenLength = !nextParam->name.empty() &&
												   nextParam->name.size() > 9 &&
												   nextParam->name.substr(nextParam->name.size() - 9) == ".__length";

						if (expectsHiddenLength) {
							std::string lengthVarName = varExpr->name + ".__length";
							if (namedValues.find(lengthVarName) != namedValues.end()) {
								mir::MIRValue *lengthAlloca = namedValues[lengthVarName];
								mir::MIRValue *lengthVal = builder->createLoad(
									mir::MIRType::getInt32(), lengthAlloca, "length");
								argsV.push_back(lengthVal);
								argIdx++;
							}
						}
					}
					continue;
				}

				// Handle struct arguments - pass pointer instead of value
				if (alloca && structTypes.find(varType) != structTypes.end()) {
					// If this is a struct parameter, it's already a pointer - load it
					// Otherwise it's a local struct variable - pass the alloca pointer
					if (isStructParameter(varExpr->name)) {
						mir::MIRValue *ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
						argsV.push_back(ptrVal);
					} else {
						argsV.push_back(alloca);
					}
					argIdx++;
					continue;
				}
			}

			// Normal argument
			mir::MIRValue *argVal = generateExpr(arg.get());
			if (!argVal) {
				throw std::runtime_error("Failed to generate function argument");
			}

			// If the argument is a struct value but the parameter expects a pointer,
			// store the value into a temporary alloca and pass the pointer
			if (argVal->type->isStruct() && paramType->isPointer()) {
				mir::MIRValue *tempAlloca = builder->createAlloca(argVal->type, "struct.tmp");
				builder->createStore(argVal, tempAlloca);
				argVal = tempAlloca;
			}

			// Promote int/byte to float if parameter expects float
			if (paramType->isFloat() && argVal->type->isInteger()) {
				argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "promotetmp");
			}

			argsV.push_back(argVal);
			argIdx++;
		}

		// Use Safe FFI for all extern functions (both DLLs and static libs)
		// Static libs are linked into the worker executable, DLLs are loaded dynamically
		if (usesSafeFfi) {
			return generateSafeFFICall(calleeF->name, argsV, calleeF->returnType);
		}
		return builder->createCall(calleeF, argsV);
	}

	throw std::runtime_error("Unknown expression type");
}

//==============================================================================
// Math Intrinsic Generation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateMathIntrinsic(CallExprAST *callExpr) {
	const std::string &name = callExpr->callee;

	// Declare the math function if not already declared
	mir::MIRFunction *mathFunc = nullptr;

	// Rounding functions (trunc, floor, ceil, round) return int in Maxon
	// But the underlying C library functions return float, so we need to convert
	bool isRoundingFunction = (name == "trunc" || name == "floor" ||
							   name == "ceil" || name == "round");

	if (name == "sin" || name == "cos" || name == "tan" ||
		name == "sqrt" || name == "floor" || name == "ceil" ||
		name == "round" || name == "trunc" || name == "abs") {
		mathFunc = getOrDeclareFunction(name, mir::MIRType::getFloat64(),
										{mir::MIRType::getFloat64()});
	} else if (name == "pow" || name == "fmod") {
		mathFunc = getOrDeclareFunction(name, mir::MIRType::getFloat64(),
										{mir::MIRType::getFloat64(), mir::MIRType::getFloat64()});
	} else {
		throw std::runtime_error("Unknown math intrinsic: " + name);
	}

	// Generate arguments with int-to-float promotion
	std::vector<mir::MIRValue *> args;
	for (auto &arg : callExpr->args) {
		mir::MIRValue *argVal = generateExpr(arg.get());
		// Promote int to float
		if (argVal->type->isInteger()) {
			argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "promotetmp");
		}
		args.push_back(argVal);
	}

	mir::MIRValue *result = builder->createCall(mathFunc, args);

	// Rounding functions return int in Maxon, convert from float
	if (isRoundingFunction) {
		result = builder->createFPToSI(result, mir::MIRType::getInt32(), "fptositmp");
	}

	return result;
}
