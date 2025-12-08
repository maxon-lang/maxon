/**
 * MIR Code Generator - Expression Generation
 *
 * This file implements expression code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../lexer.h"
#include "../types/type_conversion.h"
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

	if (auto *arrayLitExpr = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		// Array literals should only appear as initializers
		reportError("Array literal can only be used as an initializer in variable declarations",
					arrayLitExpr->line, arrayLitExpr->column);
	}

	if (auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(expr)) {
		// Sized array expressions should only appear as initializers
		reportError("Sized array expression can only be used as an initializer in variable declarations",
					sizedArray->line, sizedArray->column);
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
						mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
						if (!unsizedArrayType) {
							unsizedArrayType = module->getOrCreateStructType(
								"_ManagedArray_byte",
								{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
							structTypes["_ManagedArray_byte"] = unsizedArrayType;
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
					mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

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
						reportError("Type '" + castExpr->targetType +
										"' conforms to ExpressibleByStringLiteral but has no init method",
									castExpr->line, castExpr->column);
					}

					// Pass null for self (factory method doesn't use it)
					mir::MIRValue *nullSelf = builder->getNull();
					return builder->createCall(initFunc, {nullSelf, managedAlloca}, "literal.init");
				}
			}
		}

		mir::MIRValue *value = generateExpr(castExpr->expr.get());
		if (!value) {
			reportError("Failed to generate expression for cast",
						castExpr->line, castExpr->column);
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

		reportError("Unsupported cast from " +
						(sourceType->structName.empty() ? getMaxonTypeFromMIRType(sourceType) : sourceType->structName) +
						" to " + castExpr->targetType,
					castExpr->line, castExpr->column);
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
							if (maxon::TypeConversion::isArrayType(fieldType)) {
								return fieldPtr;
							} // Load the field value for non-array fields
							mir::MIRType *fieldMIRType = getTypeFromString(fieldType);
							return builder->createLoad(fieldMIRType, fieldPtr, varExpr->name);
						}
					}
				}
			}
			// Check if this is a function reference (function pointer)
			if (varExpr->isFunctionReference && !varExpr->resolvedFunctionName.empty()) {
				// Get the function from the module
				mir::MIRFunction *func = module->getFunction(varExpr->resolvedFunctionName);
				if (!func) {
					// Try suffix matching for namespaced functions
					std::string searchSuffix = "." + varExpr->resolvedFunctionName;
					for (auto &f : module->functions) {
						if (f->name == varExpr->resolvedFunctionName ||
							(f->name.size() > searchSuffix.size() &&
							 f->name.substr(f->name.size() - searchSuffix.size()) == searchSuffix)) {
							func = f.get();
							break;
						}
					}
				}
				if (func) {
					// Create a function reference (function pointer)
					return mir::MIRValue::createFunctionRef(func->name);
				}
			}

			reportError("Unknown variable name: " + varExpr->name,
						varExpr->line, varExpr->column);
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

	if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(expr)) {
		reportError("Struct literal can only be used as an initializer in variable declarations",
					structInitExpr->line, structInitExpr->column);
	}

	if (auto *memberAccessExpr = dynamic_cast<MemberAccessExprAST *>(expr)) {
		// Check if this is an enum case expression (e.g., Direction.north)
		if (memberAccessExpr->isEnumCase()) {
			const std::string &enumName = memberAccessExpr->resolvedEnumName;
			const std::string &caseName = memberAccessExpr->resolvedEnumCaseName;

			auto enumIt = enumTypes.find(enumName);
			if (enumIt == enumTypes.end()) {
				reportError("Unknown enum type: " + enumName,
							memberAccessExpr->line, memberAccessExpr->column);
			}

			const EnumCodegenInfo &enumInfo = enumIt->second;
			auto tagIt = enumInfo.caseTags.find(caseName);
			if (tagIt == enumInfo.caseTags.end()) {
				reportError("Unknown enum case: " + enumName + "." + caseName,
							memberAccessExpr->line, memberAccessExpr->column);
			}

			int tagValue = tagIt->second;

			// Simple enum (no associated values): just return the tag value
			if (!enumInfo.hasAssociatedValues) {
				return builder->getInt8(static_cast<int8_t>(tagValue));
			}

			// Enum with associated values but this case has none: create struct with tag
			mir::MIRValue *enumAlloca = builder->createAlloca(enumInfo.mirType, enumName + "." + caseName);
			// Set tag field (field 0)
			mir::MIRValue *tagPtr = builder->createStructGEP(enumInfo.mirType, enumAlloca, 0, "enum.tag");
			builder->createStore(builder->getInt8(static_cast<int8_t>(tagValue)), tagPtr);
			return enumAlloca;
		}

		// Check for .rawValue access on enum variable
		if (memberAccessExpr->memberName == "rawValue" && !memberAccessExpr->object) {
			std::string varType = variableTypes[memberAccessExpr->objectName];
			auto enumIt = enumTypes.find(varType);
			if (enumIt != enumTypes.end()) {
				const EnumCodegenInfo &enumInfo = enumIt->second;
				if (enumInfo.rawValueType.empty()) {
					reportError("Enum " + varType + " does not have raw values",
								memberAccessExpr->line, memberAccessExpr->column);
				}

				// Load the enum tag value
				mir::MIRValue *enumAlloca = namedValues[memberAccessExpr->objectName];
				if (!enumAlloca) {
					reportError("Unknown variable: " + memberAccessExpr->objectName,
								memberAccessExpr->line, memberAccessExpr->column);
				}

				mir::MIRValue *tagVal;
				if (enumInfo.hasAssociatedValues) {
					mir::MIRValue *tagPtr = builder->createStructGEP(enumInfo.mirType, enumAlloca, 0, "enum.tag.ptr");
					tagVal = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "enum.tag");
				} else {
					tagVal = builder->createLoad(mir::MIRType::getInt8(), enumAlloca, "enum.tag");
				}

				// For int raw values, use a chain of comparisons with phi-like merging
				if (enumInfo.rawValueType == "int") {
					// Create alloca to store the result
					mir::MIRValue *resultAlloca = builder->createAlloca(mir::MIRType::getInt32(), "rawValue.alloca");
					builder->createStore(builder->getInt32(0), resultAlloca); // Default value

					// Generate conditional branches for each case
					mir::MIRBasicBlock *endBlock = builder->createBasicBlock("rawValue.end");

					for (size_t i = 0; i < enumInfo.caseNames.size(); i++) {
						const auto &caseName = enumInfo.caseNames[i];
						auto rawIt = enumInfo.caseRawIntValues.find(caseName);
						int64_t rawValue = 0;
						if (rawIt != enumInfo.caseRawIntValues.end()) {
							rawValue = rawIt->second;
						} else {
							// Auto-generated raw value
							rawValue = enumInfo.caseTags.at(caseName);
						}

						int tagIdx = enumInfo.caseTags.at(caseName);
						mir::MIRValue *tagConst = builder->getInt8(static_cast<int8_t>(tagIdx));
						mir::MIRValue *isMatch = builder->createICmpEq(tagVal, tagConst, "tag.match." + caseName);

						mir::MIRBasicBlock *matchBlock = builder->createBasicBlock("rawValue.case." + caseName);
						mir::MIRBasicBlock *nextBlock = (i + 1 < enumInfo.caseNames.size())
															? builder->createBasicBlock("rawValue.next." + std::to_string(i))
															: endBlock;

						builder->createCondBr(isMatch, matchBlock, nextBlock);

						builder->setInsertPoint(matchBlock);
						builder->createStore(builder->getInt32(static_cast<int>(rawValue)), resultAlloca);
						builder->createBr(endBlock);

						if (nextBlock != endBlock) {
							builder->setInsertPoint(nextBlock);
						}
					}

					builder->setInsertPoint(endBlock);
					return builder->createLoad(mir::MIRType::getInt32(), resultAlloca, "rawValue");
				} else if (enumInfo.rawValueType == "string") {
					// For string raw values, similar approach but returns string pointers
					reportError("String raw values not yet implemented",
								memberAccessExpr->line, memberAccessExpr->column);
				}
			}
		}

		std::string varType;
		mir::MIRValue *objectPtr = nullptr;

		if (memberAccessExpr->object) {
			// Complex expression (e.g., arr[0].field or self._str._len)
			if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(memberAccessExpr->object.get())) {
				// Array index expression (e.g., arr[0].field)
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
				if (maxon::TypeConversion::isArrayType(arrayType)) {
					varType = maxon::TypeConversion::getArrayElementType(arrayType);
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
								reportError("Unknown variable: " + ma->objectName,
											ma->line, ma->column);
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
						reportError("Expected struct type for member access: " + currentType,
									ma->line, ma->column);
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
						reportError("Unknown field '" + ma->memberName + "' in struct '" + currentType + "'",
									ma->line, ma->column);
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
				reportError("Unknown variable: " + memberAccessExpr->objectName,
							memberAccessExpr->line, memberAccessExpr->column);
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
				reportError("Unknown field '" + memberAccessExpr->memberName +
								"' in struct '" + varType + "'",
							memberAccessExpr->line, memberAccessExpr->column);
			}

			// GEP to get field pointer
			mir::MIRValue *fieldPtr = builder->createStructGEP(structType, objectPtr,
															   fieldIndex, memberAccessExpr->memberName);

			// Check if the field is an array type - return pointer instead of loading
			std::string fieldTypeStr = fields[fieldIndex].second;
			if (maxon::TypeConversion::isArrayType(fieldTypeStr)) {
				// Array field - return pointer to the array (for subsequent indexing)
				return fieldPtr;
			}

			// Load the field value
			mir::MIRType *fieldType = structType->fieldTypes[fieldIndex];
			return builder->createLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
		}

		// Handle array.length
		if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				std::string arrType = variableTypes[memberAccessExpr->objectName];

				// Check for hidden __length alloca first (function parameters and static sized arrays)
				// Function parameters use the old ABI with separate ptr + length arguments
				std::string lengthVar = memberAccessExpr->objectName + ".__length";
				mir::MIRValue *lengthAlloca = namedValues[lengthVar];
				if (lengthAlloca) {
					return builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "length");
				}

				// Phase 2: Check if this is a _ManagedArray type (uses struct layout for local vars)
				if (maxon::TypeConversion::isManagedArrayType(arrType)) {
					std::string elemType = maxon::TypeConversion::getArrayElementType(arrType);
					mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
					// GEP to _len field (index 1) and load
					mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, arrayAlloca, 1, "arr._len.ptr");
					return builder->createLoad(mir::MIRType::getInt32(), lenPtr, "length");
				}

				// Legacy fallback: For heap-allocated arrays with header layout
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
															  {builder->getInt64(-8)}, "length.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");
			}
			reportError("Variable is not an array: " + memberAccessExpr->objectName,
						memberAccessExpr->line, memberAccessExpr->column);
		}

		// Handle array.capacity (dynamic arrays only)
		if (memberAccessExpr->memberName == "capacity" && !memberAccessExpr->object) {
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				std::string arrType = variableTypes[memberAccessExpr->objectName];

				// Check for hidden __capacity alloca first (function parameters)
				std::string capacityVar = memberAccessExpr->objectName + ".__capacity";
				mir::MIRValue *capacityAlloca = namedValues[capacityVar];
				if (capacityAlloca) {
					return builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "capacity");
				}

				// Phase 2: Check if this is a _ManagedArray type (uses struct layout for local vars)
				if (maxon::TypeConversion::isManagedArrayType(arrType)) {
					std::string elemType = maxon::TypeConversion::getArrayElementType(arrType);
					mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
					// GEP to _capacity field (index 2) and load
					mir::MIRValue *capPtr = builder->createStructGEP(managedArrayType, arrayAlloca, 2, "arr._cap.ptr");
					return builder->createLoad(mir::MIRType::getInt32(), capPtr, "capacity");
				}

				// Legacy fallback: For heap-allocated arrays with header layout
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
														   {builder->getInt64(-4)}, "cap.ptr");
				return builder->createLoad(mir::MIRType::getInt32(), capPtr, "capacity");
			}

			reportError("Variable is not an array: " + memberAccessExpr->objectName,
						memberAccessExpr->line, memberAccessExpr->column);
		}

		reportError("Unknown member: " + memberAccessExpr->memberName,
					memberAccessExpr->line, memberAccessExpr->column);
	}

	if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		mir::MIRValue *indexVal = generateExpr(arrayIndexExpr->index.get());
		if (!indexVal) {
			reportError("Failed to generate array index",
						arrayIndexExpr->line, arrayIndexExpr->column);
		}

		// Handle complex array access (e.g., struct.field[i])
		if (arrayIndexExpr->hasArrayExpr()) {
			// Generate the array expression (e.g., struct.field)
			mir::MIRValue *arrayVal = generateExpr(arrayIndexExpr->arrayExpr.get());
			if (!arrayVal) {
				reportError("Failed to generate array expression",
							arrayIndexExpr->line, arrayIndexExpr->column);
			}

			// For member access on struct, the result is a pointer to the array field
			// We need to determine the element type from the expression
			std::string elementTypeStr = "byte"; // Default for now
			bool isUnsizedArray = false;

			// Get the array field type using getExpressionMaxonType for nested member access
			std::string arrayFieldType = getExpressionMaxonType(arrayIndexExpr->arrayExpr.get());
			logTrace("ArrayIndex in '" + currentReceiverType + "': arrayFieldType = '" + arrayFieldType + "'");
			if (maxon::TypeConversion::isArrayType(arrayFieldType)) {
				elementTypeStr = maxon::TypeConversion::getArrayElementType(arrayFieldType);
				isUnsizedArray = maxon::TypeConversion::isManagedArrayType(arrayFieldType);
				logTrace("ArrayIndex in '" + currentReceiverType + "': elementTypeStr = '" + elementTypeStr + "', isUnsizedArray = " + (isUnsizedArray ? "true" : "false"));
			}

			mir::MIRType *elementType = getTypeFromString(elementTypeStr);

			// For unsized arrays, arrayVal points to the fat pointer struct {ptr, i32}
			// We need to extract the data pointer (field 0) and load it
			mir::MIRValue *dataPtr = arrayVal;
			if (isUnsizedArray) {
				// Get the unsized array struct type
				mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
					"_ManagedArray_" + elementTypeStr,
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

					if (fieldIndex >= 0 && maxon::TypeConversion::isArrayType(fieldType)) {
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
							std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(fieldType);
							bool isUnsizedArray = maxon::TypeConversion::isManagedArrayType(fieldType);
							mir::MIRType *elementType = getTypeFromString(elementTypeStr);

							// For unsized arrays, load the data pointer from fat pointer
							mir::MIRValue *dataPtr = fieldPtr;
							if (isUnsizedArray) {
								mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
									"_ManagedArray_" + elementTypeStr,
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
			reportError("Unknown array name: " + arrayIndexExpr->arrayName,
						arrayIndexExpr->line, arrayIndexExpr->column);
		}

		// Determine element type
		std::string elementTypeStr = "int";
		std::string varType = variableTypes[arrayIndexExpr->arrayName];
		if (maxon::TypeConversion::isArrayType(varType)) {
			elementTypeStr = maxon::TypeConversion::getArrayElementType(varType);
		}
		mir::MIRType *elementType = getTypeFromString(elementTypeStr);
		bool isStructElement = structTypes.find(elementTypeStr) != structTypes.end();

		// Get the array data pointer
		mir::MIRValue *arrayPtr;

		if (stackAllocatedArrays.count(arrayIndexExpr->arrayName) > 0) {
			// Static stack array (let arr = [1,2,3]) - alloca is directly the array memory
			arrayPtr = alloca;
		} else if (arrayParameters.count(arrayIndexExpr->arrayName) > 0) {
			// Function parameter - alloca holds the data pointer directly (old ABI)
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		} else if (maxon::TypeConversion::isManagedArrayType(varType)) {
			// Managed array local var - alloca is a __ManagedArrayData struct, load _buffer from field 0
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, alloca, 0, "arr._buffer.ptr");
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "arr.buffer");
		} else {
			// Legacy heap array - alloca holds the data pointer
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
		// For == and != on enum types, compare tag values
		if (binExpr->op == 'E' || binExpr->op == 'N') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			// Check if this is an enum type
			auto enumIt = enumTypes.find(leftType);
			if (enumIt != enumTypes.end()) {
				const EnumCodegenInfo &enumInfo = enumIt->second;

				// Generate left and right operands
				mir::MIRValue *leftVal = generateExpr(binExpr->left.get());
				mir::MIRValue *rightVal = generateExpr(binExpr->right.get());

				// For simple enums (no associated values), compare the i8 values directly
				if (!enumInfo.hasAssociatedValues) {
					mir::MIRValue *result = (binExpr->op == 'E')
												? builder->createICmpEq(leftVal, rightVal, "enum.eq")
												: builder->createICmpNe(leftVal, rightVal, "enum.ne");
					return result;
				}

				// For enums with associated values, compare tags only
				// Get tag from left (field 0)
				mir::MIRValue *leftTagPtr = builder->createStructGEP(enumInfo.mirType, leftVal, 0, "left.tag.ptr");
				mir::MIRValue *leftTag = builder->createLoad(mir::MIRType::getInt8(), leftTagPtr, "left.tag");

				// Get tag from right (field 0)
				mir::MIRValue *rightTagPtr = builder->createStructGEP(enumInfo.mirType, rightVal, 0, "right.tag.ptr");
				mir::MIRValue *rightTag = builder->createLoad(mir::MIRType::getInt8(), rightTagPtr, "right.tag");

				mir::MIRValue *result = (binExpr->op == 'E')
											? builder->createICmpEq(leftTag, rightTag, "enum.eq")
											: builder->createICmpNe(leftTag, rightTag, "enum.ne");
				return result;
			}

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
								reportError("Failed to generate Equatable comparison operands",
											binExpr->line, binExpr->column);
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

		// For <, >, <=, >= on character type, compare by first codepoint value
		// This provides lexicographic ordering for single-codepoint chars
		if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || binExpr->op == 'G') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			if (leftType == "character") {
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
					reportError("Failed to generate character comparison operands",
								binExpr->line, binExpr->column);
				}

				// Extract first byte from each character to compare (for ASCII/simple chars)
				// character = { _managed ptr } where _managed points to __ManagedStringData
				mir::MIRType *charType = structTypes["character"];
				mir::MIRType *managedDataType = structTypes["__ManagedStringData"];
				mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

				if (!managedDataType || !unsizedArrayType) {
					reportError("character comparison requires __ManagedStringData type",
								binExpr->line, binExpr->column);
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
				mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
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
			reportError("Failed to generate binary expression operands",
						binExpr->line, binExpr->column);
		}

		bool leftIsFloat = left->type->isFloat();
		bool rightIsFloat = right->type->isFloat();
		// Use centralized type conversion rules to determine if float promotion is needed
		std::string leftTypeStr = leftIsFloat ? "float" : "int";
		std::string rightTypeStr = rightIsFloat ? "float" : "int";
		bool needsFloatOp = maxon::TypeConversion::needsFloatPromotion(leftTypeStr, rightTypeStr, binExpr->op);

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
			reportError("Unknown binary operator",
						binExpr->line, binExpr->column);
		}
	}

	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		mir::MIRValue *operand = generateExpr(unaryExpr->operand.get());
		if (!operand) {
			reportError("Failed to generate unary expression operand",
						unaryExpr->line, unaryExpr->column);
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
			reportError("Unknown unary operator",
						unaryExpr->line, unaryExpr->column);
		}
	}

	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Handle enum case construction with associated values (e.g., Result.success(42))
		if (callExpr->isEnumCaseConstruction()) {
			const std::string &enumName = callExpr->resolvedEnumName;
			const std::string &caseName = callExpr->resolvedEnumCaseName;

			auto enumIt = enumTypes.find(enumName);
			if (enumIt == enumTypes.end()) {
				reportError("Unknown enum type: " + enumName,
							callExpr->line, callExpr->column);
			}

			const EnumCodegenInfo &enumInfo = enumIt->second;
			auto tagIt = enumInfo.caseTags.find(caseName);
			if (tagIt == enumInfo.caseTags.end()) {
				reportError("Unknown enum case: " + enumName + "." + caseName,
							callExpr->line, callExpr->column);
			}

			int tagValue = tagIt->second;

			// Allocate space for the enum
			mir::MIRValue *enumAlloca = builder->createAlloca(enumInfo.mirType, enumName + "." + caseName);

			// Set tag field (field 0)
			mir::MIRValue *tagPtr = builder->createStructGEP(enumInfo.mirType, enumAlloca, 0, "enum.tag");
			builder->createStore(builder->getInt8(static_cast<int8_t>(tagValue)), tagPtr);

			// Store associated values in the payload
			auto assocIt = enumInfo.caseAssocValues.find(caseName);
			if (assocIt != enumInfo.caseAssocValues.end() && !assocIt->second.empty()) {
				const auto &assocValues = assocIt->second;
				if (callExpr->args.size() != assocValues.size()) {
					reportError("Wrong number of arguments for enum case " + enumName + "." + caseName +
									": expected " + std::to_string(assocValues.size()) + ", got " +
									std::to_string(callExpr->args.size()),
								callExpr->line, callExpr->column);
				}

				// Get pointer to payload (field 1)
				mir::MIRValue *payloadPtr = builder->createStructGEP(enumInfo.mirType, enumAlloca, 1, "enum.payload");

				// Store each associated value
				size_t offset = 0;
				for (size_t i = 0; i < assocValues.size(); i++) {
					mir::MIRValue *argValue = generateExpr(callExpr->args[i].get());
					if (!argValue) {
						reportError("Failed to generate argument for enum case construction",
									callExpr->line, callExpr->column);
					}

					mir::MIRType *argType = getTypeFromString(assocValues[i].second);

					// Store at offset within payload
					mir::MIRValue *fieldPtr = builder->createGEP(mir::MIRType::getInt8(), payloadPtr,
																 {builder->getInt64(static_cast<int64_t>(offset))},
																 "enum.payload." + assocValues[i].first);
					// Cast to appropriate pointer type and store
					builder->createStore(argValue, fieldPtr);

					offset += argType->sizeInBytes;
				}
			}

			return enumAlloca;
		}

		// Handle math intrinsics
		if (Lexer::isMathIntrinsic(callExpr->callee)) {
			return generateMathIntrinsic(callExpr);
		}

		// Handle array intrinsics (push, pop)
		if (callExpr->callee == "push" || callExpr->callee == "pop") {
			return generateArrayIntrinsic(callExpr);
		}

		// Handle map intrinsic: map(arr, transform)
		if (callExpr->callee == "map") {
			return generateMapIntrinsic(callExpr);
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
					// Extract element type from array type like "_ManagedArray<int>" or "_ManagedArray<KeyType>"
					if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
						argType = maxon::TypeConversion::getArrayElementType(arrayType);
					}
				}
			}

			// Check if the argument type is a type parameter that has been bound
			auto typeBindingIt = currentTypeBindings.find(argType);
			if (typeBindingIt != currentTypeBindings.end()) {
				argType = typeBindingIt->second;
			}

			// Check if it's a primitive type that has hash/equals
			if (argType == "int" || argType == "byte" || argType == "character") {
				effectiveCallee = argType + "." + effectiveCallee;
			}
		}

		// Handle string/substring/cstring intrinsics via registry
		if (auto method = IntrinsicCodegenRegistry::instance().getMethod(effectiveCallee)) {
			return (this->*method)(callExpr);
		}

		// Handle primitive type methods (int.hash, int.equals, etc.)
		if (effectiveCallee == "int.hash" || effectiveCallee == "byte.hash" || effectiveCallee == "character.hash") {
			// Multiplicative hash using Knuth's golden ratio constant
			// hash = value * 2654435769 (which is 0x9E3779B9)
			if (callExpr->args.empty()) {
				reportError("hash() requires self argument",
							callExpr->line, callExpr->column);
			}
			mir::MIRValue *value = generateExpr(callExpr->args[0].get());
			// Extend byte/character to int if needed
			if (effectiveCallee != "int.hash") {
				value = builder->createZExt(value, mir::MIRType::getInt32(), "hash.extend");
			}
			mir::MIRValue *multiplier = mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), 0x9E3779B9);
			return builder->createMul(value, multiplier, "hash");
		}

		if (effectiveCallee == "int.equals" || effectiveCallee == "byte.equals" || effectiveCallee == "character.equals") {
			// Simple equality comparison
			if (callExpr->args.size() < 2) {
				reportError("equals() requires self and other arguments",
							callExpr->line, callExpr->column);
			}
			mir::MIRValue *self = generateExpr(callExpr->args[0].get());
			mir::MIRValue *other = generateExpr(callExpr->args[1].get());
			return builder->createICmpEq(self, other, "equals");
		}

		// Handle function variable calls (calling a function pointer stored in a variable)
		if (callExpr->isFunctionVariableCall) {
			// Load the function pointer from the variable
			auto it = namedValues.find(effectiveCallee);
			if (it == namedValues.end()) {
				reportError("Unknown function variable: " + effectiveCallee,
							callExpr->line, callExpr->column);
			}
			mir::MIRValue *funcPtrAlloca = it->second;
			mir::MIRValue *funcPtr = builder->createLoad(mir::MIRType::getPtr(), funcPtrAlloca, "funcptr");

			// Generate arguments
			std::vector<mir::MIRValue *> argsV;
			for (size_t i = 0; i < callExpr->args.size(); i++) {
				mir::MIRValue *argVal = generateExpr(callExpr->args[i].get());
				argsV.push_back(argVal);
			}

			// Create indirect call through function pointer
			// Parse the function type to determine return type
			std::string varType = variableTypes[effectiveCallee];
			std::vector<std::string> paramTypes;
			std::string returnTypeStr;
			maxon::TypeConversion::parseFunctionType(varType, paramTypes, returnTypeStr);

			mir::MIRType *returnType = getTypeFromString(returnTypeStr);
			std::vector<mir::MIRType *> mirParamTypes;
			for (const auto &pt : paramTypes) {
				mirParamTypes.push_back(getTypeFromString(pt));
			}
			return builder->createCallIndirect(funcPtr, returnType, mirParamTypes, argsV, "fncall");
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
			reportError("Unknown function referenced: " + effectiveCallee,
						callExpr->line, callExpr->column);
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

					// Phase 2: Handle _ManagedArray<T> struct layout
					if (maxon::TypeConversion::isManagedArrayType(varType)) {
						// Managed array: struct layout { _buffer ptr, _len i32, _capacity i32 }
						std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(varType);
						mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
						mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, alloca, 0, "arr._buffer.ptr");
						ptrVal = builder->createLoad(mir::MIRType::getPtr(), bufferField, "arr.buffer");
					} else if (stackAllocatedArrays.count(varExpr->name) > 0) {
						// Legacy: Stack-allocated array - alloca IS the array pointer
						ptrVal = alloca;
					} else {
						// Legacy: Heap-allocated array - alloca contains a pointer to the array
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
							// Phase 2: For managed arrays, read length from struct field 1
							if (maxon::TypeConversion::isManagedArrayType(varType)) {
								std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(varType);
								mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
								mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, alloca, 1, "arr._len.ptr");
								mir::MIRValue *lengthVal = builder->createLoad(mir::MIRType::getInt32(), lenField, "length");
								argsV.push_back(lengthVal);
								argIdx++;
							} else {
								// Legacy: Check for hidden __length alloca
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

			// Check if argument is nil (generateExpr returns nullptr for nil)
			bool isNilArg = (!argVal && dynamic_cast<NilExprAST *>(arg.get()));
			if (!argVal && !isNilArg) {
				reportError("Failed to generate function argument",
							callExpr->line, callExpr->column);
			}

			// If the argument is a struct value but the parameter expects a pointer,
			// store the value into a temporary alloca and pass the pointer
			if (argVal && argVal->type->isStruct() && paramType->isPointer()) {
				mir::MIRValue *tempAlloca = builder->createAlloca(argVal->type, "struct.tmp");
				builder->createStore(argVal, tempAlloca);
				argVal = tempAlloca;
			}

			// Promote int/byte to float if parameter expects float (uses centralized type rules)
			if (argVal && paramType->isFloat() && argVal->type->isInteger()) {
				// This is an implicit conversion from int/byte to float (widening, no data loss)
				argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "promotetmp");
			}

			// Handle optional parameter wrapping
			// If parameter type is optional and argument is non-optional, wrap it
			// Look up the Maxon type string for the parameter
			auto paramTypesIt = functionParameterTypes.find(effectiveCallee);
			if (paramTypesIt != functionParameterTypes.end()) {
				// Account for sibling method calls (parameter index offset)
				size_t actualParamIdx = i + (callExpr->isSiblingMethodCall ? 1 : 0);
				if (actualParamIdx < paramTypesIt->second.size()) {
					std::string paramMaxonType = paramTypesIt->second[actualParamIdx];

					// Check if parameter is optional ("T or nil") using centralized type conversion
					if (maxon::TypeConversion::isOptionalType(paramMaxonType)) {
						// Case 1: Argument is nil - create nil optional
						if (isNilArg) {
							argVal = createNilOptional(paramType);
						}
						// Case 2: Argument has a value
						else if (argVal) {
							// Get argument type
							std::string argMaxonType = getExpressionMaxonType(arg.get());

							// Argument is unwrapped type - wrap in Some optional
							if (!maxon::TypeConversion::isOptionalType(argMaxonType)) {
								argVal = createSomeOptional(paramType, argVal);
							}
							// Case 3: Argument is already optional - use as-is (already handled)
						}
					}
				}
			}

			// If still nullptr at this point, something went wrong
			if (!argVal) {
				reportError("Failed to generate optional argument value",
							callExpr->line, callExpr->column);
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

	if (auto *matchExpr = dynamic_cast<MatchExprAST *>(expr)) {
		return generateMatchExpr(matchExpr);
	}

	if (auto *closureExpr = dynamic_cast<ClosureExprAST *>(expr)) {
		// Generate an anonymous function for the closure (non-capturing only for MVP)
		// The closure becomes a function pointer that can be passed to other functions

		// Generate unique name for the closure function
		static int closureCounter = 0;
		std::string closureName = "__closure_" + std::to_string(closureCounter++);

		// Build parameter types for the closure function
		std::vector<std::pair<mir::MIRType *, std::string>> closureParams;
		for (const auto &param : closureExpr->parameters) {
			mir::MIRType *paramType = getParamTypeFromString(param.type);
			closureParams.push_back({paramType, param.name});

			// Handle array parameters with hidden length
			if (isArrayParam(param.type)) {
				closureParams.push_back({mir::MIRType::getInt32(), param.name + ".__length"});
			}
		}

		// Determine return type
		mir::MIRType *returnType;
		if (!closureExpr->returnType.empty()) {
			returnType = getTypeFromString(closureExpr->returnType);
		} else if (closureExpr->isSingleExpression && closureExpr->singleExpr) {
			// Infer return type from the expression
			std::string inferredType = inferExprType(closureExpr->singleExpr.get());
			returnType = getTypeFromString(inferredType.empty() ? "int" : inferredType);
		} else {
			returnType = mir::MIRType::getVoid();
		}

		// Save current builder state to restore after generating the closure
		mir::MIRFunction *savedFunction = builder->getFunction();
		mir::MIRBasicBlock *savedBlock = builder->getInsertBlock();
		std::map<std::string, mir::MIRValue *> savedNamedValues = namedValues;
		std::map<std::string, std::string> savedVariableTypes = variableTypes;
		std::set<std::string> savedStructParameters = structParameters;
		std::set<std::string> savedArrayParameters = arrayParameters;
		std::set<std::string> savedStackAllocatedArrays = stackAllocatedArrays;
		std::string savedReceiverType = currentReceiverType;

		// Create the closure function
		mir::MIRFunction *closureFunc = builder->createFunction(closureName, returnType, closureParams);

		// Set up the closure function context
		builder->setFunction(closureFunc);
		mir::MIRBasicBlock *entry = closureFunc->createBasicBlock("entry");
		builder->setInsertPoint(entry);

		// Clear context for the closure function
		namedValues.clear();
		variableTypes.clear();
		structParameters.clear();
		arrayParameters.clear();
		stackAllocatedArrays.clear();
		currentReceiverType.clear();

		// Allocate stack space for parameters
		size_t argIdx = 0;
		for (const auto &param : closureExpr->parameters) {
			mir::MIRType *paramType = getParamTypeFromString(param.type);
			mir::MIRValue *alloca = builder->createAlloca(paramType, param.name);

			// Store the parameter value
			mir::MIRValue *paramVal = closureFunc->parameters[argIdx];
			builder->createStore(paramVal, alloca);
			namedValues[param.name] = alloca;
			variableTypes[param.name] = param.type;
			argIdx++;

			// Track struct parameters
			if (structTypes.find(param.type) != structTypes.end()) {
				structParameters.insert(param.name);
			}

			// Handle array parameters with hidden length
			if (isArrayParam(param.type)) {
				arrayParameters.insert(param.name);
				std::string lengthVarName = param.name + ".__length";
				mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), lengthVarName);
				mir::MIRValue *lengthParamVal = closureFunc->parameters[argIdx];
				builder->createStore(lengthParamVal, lengthAlloca);
				namedValues[lengthVarName] = lengthAlloca;
				argIdx++;
			}
		}

		// Push a scope for the closure body
		pushScope();

		// Generate the closure body
		if (closureExpr->isSingleExpression && closureExpr->singleExpr) {
			// Single expression closure - evaluate and return
			mir::MIRValue *result = generateExpr(closureExpr->singleExpr.get());
			popScope(closureFunc);
			if (result && !returnType->isVoid()) {
				builder->createRet(result);
			} else {
				builder->createRetVoid();
			}
		} else {
			// Multi-statement closure
			for (auto &stmt : closureExpr->body) {
				generateStmt(stmt.get(), closureFunc);
				if (builder->getInsertBlock()->hasTerminator()) {
					break;
				}
			}

			// Add default return if no terminator
			mir::MIRBasicBlock *currentBlock = builder->getInsertBlock();
			if (currentBlock && !currentBlock->hasTerminator()) {
				popScope(closureFunc);
				if (returnType->isVoid()) {
					builder->createRetVoid();
				} else if (returnType->isInteger()) {
					builder->createRet(builder->getInt32(0));
				} else if (returnType->isFloat()) {
					builder->createRet(builder->getFloat64(0.0));
				} else if (returnType->isPointer()) {
					builder->createRet(builder->getNull());
				} else {
					builder->createRetVoid();
				}
			}
		}

		// Restore the previous builder state
		namedValues = savedNamedValues;
		variableTypes = savedVariableTypes;
		structParameters = savedStructParameters;
		arrayParameters = savedArrayParameters;
		stackAllocatedArrays = savedStackAllocatedArrays;
		currentReceiverType = savedReceiverType;

		if (savedFunction && savedBlock) {
			builder->setFunction(savedFunction);
			builder->setInsertPoint(savedBlock);
		}

		// Return a reference to the closure function as a function pointer
		return mir::MIRValue::createFunctionRef(closureName);
	}

	reportError("Unknown expression type", expr->line, expr->column);
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
		reportError("Unknown math intrinsic: " + name,
					callExpr->line, callExpr->column);
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
