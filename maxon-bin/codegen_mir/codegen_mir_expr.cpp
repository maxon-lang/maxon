#include <iostream>
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
		return builder->getInt64(numExpr->value);
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

	if (auto *interpExpr = dynamic_cast<InterpolatedStringExprAST *>(expr)) {
		// Generate interpolated string: "Hello {name}!"
		return generateInterpolatedString(interpExpr);
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

	// Handle empty map literal: map from K to V
	// Creates an empty map struct and calls init()
	if (auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(expr)) {
		const std::string &keyType = mapLiteral->keyType;
		const std::string &valueType = mapLiteral->valueType;
		std::string specializedName = "map<" + keyType + "," + valueType + ">";

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			if (structDefinitions.find("map") != structDefinitions.end()) {
				std::map<std::string, std::string> typeBindings = {
					{"Key", keyType},
					{"Value", valueType}};
				instantiateGenericStruct("map", typeBindings);
			} else {
				reportError("Generic struct 'map' not found", mapLiteral->line, mapLiteral->column);
			}
		}

		mir::MIRType *mapStructType = structTypes[specializedName];
		if (!mapStructType) {
			reportError("Failed to instantiate map type: " + specializedName,
						mapLiteral->line, mapLiteral->column);
		}

		// Get element types
		mir::MIRType *keyMirType = getTypeFromString(keyType);
		mir::MIRType *valueMirType = getTypeFromString(valueType);

		// Allocate the map struct
		mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, "map.tmp");

		// Create empty _ManagedArray<Key> struct for keys
		mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
		mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

		// Create empty _ManagedArray<Value> struct for values
		mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
		mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

		// Create minimal stack buffers
		mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, 1);
		mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

		mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, 1);
		mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

		// Initialize empty keys _ManagedArray struct fields
		mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
		builder->createStore(keysStackBuffer, keysBufferField);
		mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
		builder->createStore(builder->getInt64(0), keysLenField);
		mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
		builder->createStore(builder->getInt64(0), keysCapField);

		// Initialize empty values _ManagedArray struct fields
		mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
		builder->createStore(valuesStackBuffer, valuesBufferField);
		mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
		builder->createStore(builder->getInt64(0), valuesLenField);
		mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
		builder->createStore(builder->getInt64(0), valuesCapField);

		// Call map<K,V>.init(keysManaged, valuesManaged) - returns the initialized map struct
		std::string initMethodName = specializedName + ".init";
		mir::MIRFunction *initFunc = module->getFunction(initMethodName);
		if (!initFunc) {
			reportError("map.init method not found for type: " + specializedName,
						mapLiteral->line, mapLiteral->column);
		}

		// init returns the map struct by value - store it in the alloca
		mir::MIRValue *mapResult = builder->createCall(initFunc, {keysManaged, valuesManaged}, "map.init.result");
		builder->createStore(mapResult, mapAlloca);
		return mapAlloca;
	}

	if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		// Check for InitableFromStringLiteral: "literal" as Type
		// Transform to: Type.init(managed)
		// where managed is a _ManagedString struct
		if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(castExpr->expr.get())) {
			// Check if target type conforms to InitableFromStringLiteral
			auto structIt = structConformsTo.find(castExpr->targetType);
			if (structIt != structConformsTo.end()) {
				bool conformsToExpressible = false;
				for (const auto &iface : structIt->second) {
					if (iface == "InitableFromStringLiteral") {
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
								{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
							structTypes["_ManagedArray_byte"] = unsizedArrayType;
						}
						managedStringType = module->getOrCreateStructType(
							"_ManagedString",
							{unsizedArrayType, mir::MIRType::getInt64(), mir::MIRType::getInt64()});
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
					builder->createStore(builder->getInt64(static_cast<int64_t>(len)), bufferLenPtr);

					// Field 1: _len int
					mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 1, "managed._len");
					builder->createStore(builder->getInt64(static_cast<int64_t>(len)), lenFieldPtr);

					// Field 2: _capacity int (0 = constant data)
					mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 2, "managed._capacity");
					builder->createStore(builder->getInt64(0), capacityFieldPtr);

					// Call Type.init(self, managed)
					// Note: self is null since this is a factory method that creates a new instance
					std::string methodName = castExpr->targetType + ".init";
					mir::MIRFunction *initFunc = module->getFunction(methodName);
					if (!initFunc) {
						reportError("Type '" + castExpr->targetType +
										"' conforms to InitableFromStringLiteral but has no init method",
									castExpr->line, castExpr->column);
					}

					// Call static factory method (no self parameter)
					return builder->createCall(initFunc, {managedAlloca}, "literal.init");
				}
			}
		}

		// Check for InitableFromCharLiteral: 'c' as Type
		// Transform to: Type.init(managed)
		// where managed is a _ManagedString struct containing the character's UTF-8 bytes
		if (auto *charLit = dynamic_cast<CharacterExprAST *>(castExpr->expr.get())) {
			// Check if target type conforms to InitableFromCharLiteral
			auto structIt = structConformsTo.find(castExpr->targetType);
			if (structIt != structConformsTo.end()) {
				bool conformsToExpressible = false;
				for (const auto &iface : structIt->second) {
					if (iface == "InitableFromCharLiteral") {
						conformsToExpressible = true;
						break;
					}
				}
				if (conformsToExpressible) {
					// Get or create the _ManagedString struct type
					// Layout: { _buffer []byte, _len int, _capacity int }
					mir::MIRType *managedStringType = structTypes["_ManagedString"];
					if (!managedStringType) {
						mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];
						if (!unsizedArrayType) {
							unsizedArrayType = module->getOrCreateStructType(
								"_ManagedArray_byte",
								{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
							structTypes["_ManagedArray_byte"] = unsizedArrayType;
						}
						managedStringType = module->getOrCreateStructType(
							"_ManagedString",
							{unsizedArrayType, mir::MIRType::getInt64(), mir::MIRType::getInt64()});
						structTypes["_ManagedString"] = managedStringType;
					}

					// Create the _ManagedString struct
					mir::MIRValue *managedAlloca = builder->createAlloca(managedStringType, "managed.char");

					// Get character content (UTF-8 encoded)
					const std::string &str = charLit->value;
					size_t len = str.length();

					// Create global constant for character data
					std::string globalName = ".chr." + std::to_string(stringLiteralCounter++);
					mir::MIRValue *dataPtr = module->createGlobalString(globalName, str);

					// Get the unsized array type for []byte (the _buffer field)
					mir::MIRType *unsizedArrayType = structTypes["_ManagedArray_byte"];

					// Field 0: _buffer []byte (fat pointer: {ptr, i32})
					mir::MIRValue *bufferFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 0, "managed._buffer");
					mir::MIRValue *bufferDataPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 0, "managed._buffer.ptr");
					builder->createStore(dataPtr, bufferDataPtr);
					mir::MIRValue *bufferLenPtr = builder->createStructGEP(unsizedArrayType, bufferFieldPtr, 1, "managed._buffer.len");
					builder->createStore(builder->getInt64(static_cast<int64_t>(len)), bufferLenPtr);

					// Field 1: _len int
					mir::MIRValue *lenFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 1, "managed._len");
					builder->createStore(builder->getInt64(static_cast<int64_t>(len)), lenFieldPtr);

					// Field 2: _capacity int (0 = constant data)
					mir::MIRValue *capacityFieldPtr = builder->createStructGEP(managedStringType, managedAlloca, 2, "managed._capacity");
					builder->createStore(builder->getInt64(0), capacityFieldPtr);

					// Call Type.init(self, managed)
					std::string methodName = castExpr->targetType + ".init";
					mir::MIRFunction *initFunc = module->getFunction(methodName);
					if (!initFunc) {
						reportError("Type '" + castExpr->targetType +
										"' conforms to InitableFromCharLiteral but has no init method",
									castExpr->line, castExpr->column);
					}

					// Call static factory method (no self parameter)
					return builder->createCall(initFunc, {managedAlloca}, "char.literal.init");
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
			uint64_t sourceBits = sourceType->getSizeInBytes() * 8;
			uint64_t targetBits = targetType->getSizeInBytes() * 8;
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
							if (!structType) {
								reportError("Unknown receiver type: " + currentReceiverType,
											varExpr->line, varExpr->column);
							}
							mir::MIRValue *fieldPtr = builder->createStructGEP(structType, selfPtr, fieldIndex, varExpr->name + ".ptr");

							// For array fields (both _ManagedArray<T> and array<T>), return pointer
							// This allows method calls like items.push() to modify the original field
							if (maxon::TypeConversion::isArrayType(fieldType) ||
								maxon::TypeConversion::isArrayStructType(fieldType)) {
								return fieldPtr;
							}
							// Load the field value for non-array fields
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

			// Check if this is a global constant
			mir::MIRGlobal *global = module->getGlobal(varExpr->name);
			if (global) {
				// Load the global value
				return builder->createLoad(global->type, mir::MIRValue::createGlobal(global->type, global->name), varExpr->name);
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
			loadType = mir::MIRType::getInt64();
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
		return generateStructLiteral(structInitExpr);
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
			// Return the alloca pointer (enum variables hold pointers)
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
					mir::MIRValue *resultAlloca = builder->createAlloca(mir::MIRType::getInt64(), "rawValue.alloca");
					builder->createStore(builder->getInt64(0), resultAlloca); // Default value

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
						builder->createStore(builder->getInt64(rawValue), resultAlloca);
						builder->createBr(endBlock);

						if (nextBlock != endBlock) {
							builder->setInsertPoint(nextBlock);
						}
					}

					builder->setInsertPoint(endBlock);
					return builder->createLoad(mir::MIRType::getInt64(), resultAlloca, "rawValue");
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
				// Check both internal array types (_ManagedArray<T>) and struct types (array<T>)
				if (maxon::TypeConversion::isArrayType(arrayType)) {
					varType = maxon::TypeConversion::getArrayElementType(arrayType);
				} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
					varType = maxon::TypeConversion::getArrayStructElementType(arrayType);
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
				// Other complex expression (e.g., method call returning a struct)
				mir::MIRValue *objectValue = generateExpr(memberAccessExpr->object.get());
				varType = getExpressionMaxonType(memberAccessExpr->object.get());

				// If the expression returns a struct value (not a pointer), we need to
				// store it in a temporary alloca so we can GEP into it
				if (objectValue->type->isStruct() ||
					(objectValue->type->kind == mir::MIRTypeKind::Optional && objectValue->type->getSizeInBytes() > 8)) {
					// Create a temporary alloca and store the struct value
					mir::MIRValue *tempAlloca = builder->createAlloca(objectValue->type, "temp.struct");
					builder->createStore(objectValue, tempAlloca);
					objectPtr = tempAlloca;
				} else {
					objectPtr = objectValue;
				}
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
			if (isStructParameter(memberAccessExpr->objectName)) {
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
			if (maxon::TypeConversion::isArrayType(fieldTypeStr) ||
				maxon::TypeConversion::isArrayStructType(fieldTypeStr)) {
				// Array field - return pointer to the array (for subsequent indexing)
				return fieldPtr;
			}

			// Load the field value
			mir::MIRType *fieldType = structType->getFieldTypes()[fieldIndex];
			return builder->createLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
		}

		// Handle array.length
		if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
			mir::MIRValue *arrayAlloca = namedValues[memberAccessExpr->objectName];
			if (arrayAlloca) {
				std::string arrType = variableTypes[memberAccessExpr->objectName];

				// Check if this is a _ManagedArray type (uses struct layout for local vars)
				if (maxon::TypeConversion::isManagedArrayType(arrType)) {
					std::string elemType = maxon::TypeConversion::getArrayElementType(arrType);
					mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
					// GEP to _len field (index 1) and load
					mir::MIRValue *lenPtr = builder->createStructGEP(managedArrayType, arrayAlloca, 1, "arr._len.ptr");
					return builder->createLoad(mir::MIRType::getInt64(), lenPtr, "length");
				}

				// Legacy fallback: For heap-allocated arrays with header layout
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
															  {builder->getInt64(-16)}, "length.ptr");
				return builder->createLoad(mir::MIRType::getInt64(), lengthPtr, "length");
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
					return builder->createLoad(mir::MIRType::getInt64(), capacityAlloca, "capacity");
				}

				// Phase 2: Check if this is a _ManagedArray type (uses struct layout for local vars)
				if (maxon::TypeConversion::isManagedArrayType(arrType)) {
					std::string elemType = maxon::TypeConversion::getArrayElementType(arrType);
					mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
					// GEP to _capacity field (index 2) and load
					mir::MIRValue *capPtr = builder->createStructGEP(managedArrayType, arrayAlloca, 2, "arr._cap.ptr");
					return builder->createLoad(mir::MIRType::getInt64(), capPtr, "capacity");
				}

				// Legacy fallback: For heap-allocated arrays with header layout
				mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
				mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
														   {builder->getInt64(-8)}, "cap.ptr");
				return builder->createLoad(mir::MIRType::getInt64(), capPtr, "capacity");
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
			bool isArrayStruct = false;

			// Get the array field type using getExpressionMaxonType for nested member access
			std::string arrayFieldType = getExpressionMaxonType(arrayIndexExpr->arrayExpr.get());
			logTrace("ArrayIndex in '" + currentReceiverType + "': arrayFieldType = '" + arrayFieldType + "'");
			if (maxon::TypeConversion::isArrayStructType(arrayFieldType)) {
				elementTypeStr = maxon::TypeConversion::getArrayStructElementType(arrayFieldType);
				isArrayStruct = true;
				logTrace("ArrayIndex in '" + currentReceiverType + "': elementTypeStr = '" + elementTypeStr + "', isArrayStruct = true");
			} else if (maxon::TypeConversion::isArrayType(arrayFieldType)) {
				elementTypeStr = maxon::TypeConversion::getArrayElementType(arrayFieldType);
				isUnsizedArray = maxon::TypeConversion::isManagedArrayType(arrayFieldType);
				logTrace("ArrayIndex in '" + currentReceiverType + "': elementTypeStr = '" + elementTypeStr + "', isUnsizedArray = " + (isUnsizedArray ? "true" : "false"));
			}

			mir::MIRType *elementType = getTypeFromString(elementTypeStr);

			// Extract the data pointer from the array structure
			mir::MIRValue *dataPtr = arrayVal;
			if (isArrayStruct) {
				// array<T> struct: { __ManagedArrayData { ptr, count, capacity }, iterIndex }
				mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeStr);
				mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
				// GEP to field 0 (managed), then field 0 (buffer)
				mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, arrayVal, 0, "array.managed");
				mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, "array._buffer.ptr");
				dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "array.buffer");
			} else if (isUnsizedArray) {
				// _ManagedArray<T> struct: { ptr, count }
				mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
					"_ManagedArray_" + elementTypeStr,
					{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
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
		bool isGlobalArray = false;
		mir::MIRGlobal *globalArray = nullptr;

		if (!alloca) {
			// Check if it's a global array
			globalArray = module->getGlobal(arrayIndexExpr->arrayName);
			if (globalArray && globalVariableTypes.find(arrayIndexExpr->arrayName) != globalVariableTypes.end()) {
				alloca = mir::MIRValue::createGlobal(globalArray->type, arrayIndexExpr->arrayName);
				isGlobalArray = true;
			}
		}

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

					// Check for both _ManagedArray<T> and array<T> struct types
					bool isArrayField = maxon::TypeConversion::isArrayType(fieldType) ||
										maxon::TypeConversion::isArrayStructType(fieldType);

					if (fieldIndex >= 0 && isArrayField) {
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

							// Determine element type based on field type format
							std::string elementTypeStr;
							bool isUnsizedArray = maxon::TypeConversion::isManagedArrayType(fieldType);
							bool isArrayStruct = maxon::TypeConversion::isArrayStructType(fieldType);

							if (isArrayStruct) {
								elementTypeStr = maxon::TypeConversion::getArrayStructElementType(fieldType);
							} else {
								elementTypeStr = maxon::TypeConversion::getArrayElementType(fieldType);
							}
							mir::MIRType *elementType = getTypeFromString(elementTypeStr);

							// For array<T> struct, load buffer pointer from field 0
							// For _ManagedArray<T>, also load from field 0
							mir::MIRValue *dataPtr = fieldPtr;
							if (isUnsizedArray) {
								mir::MIRType *unsizedArrayType = module->getOrCreateStructType(
									"_ManagedArray_" + elementTypeStr,
									{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
								mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, "unsized.data.ptr");
								dataPtr = builder->createLoad(mir::MIRType::getPtr(), dataPtrField, "unsized.data");
							} else if (isArrayStruct) {
								// array<T> struct: { __ManagedArrayData { ptr, i32, i32 }, i32 iterIndex }
								mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeStr);
								mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
								// GEP to field 0 (managed), then field 0 (buffer)
								mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, "array.managed");
								mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, "array._buffer.ptr");
								dataPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "array.buffer");
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
		// Check global variable types if not found locally
		if (varType.empty() && isGlobalArray) {
			varType = globalVariableTypes[arrayIndexExpr->arrayName];
		}
		if (maxon::TypeConversion::isArrayStructType(varType)) {
			elementTypeStr = maxon::TypeConversion::getArrayStructElementType(varType);
		} else if (maxon::TypeConversion::isArrayType(varType)) {
			elementTypeStr = maxon::TypeConversion::getArrayElementType(varType);
		}
		mir::MIRType *elementType = getTypeFromString(elementTypeStr);
		bool isStructElement = structTypes.find(elementTypeStr) != structTypes.end();

		// Get the array data pointer
		mir::MIRValue *arrayPtr;

		if (isGlobalArray) {
			// Global array - use the ManagedArray struct layout
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, alloca, 0, "global.arr._buffer.ptr");
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "global.arr.buffer");
		} else if (stackAllocatedArrays.count(arrayIndexExpr->arrayName) > 0) {
			// Static stack array (let arr = [1,2,3]) - alloca is directly the array memory
			arrayPtr = alloca;
		} else if (arrayParameters.count(arrayIndexExpr->arrayName) > 0) {
			// Function parameter - alloca holds the data pointer directly (old ABI)
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), alloca, "arrayptr");
		} else if (maxon::TypeConversion::isArrayStructType(varType)) {
			// array<T> struct layout: { __ManagedArrayData { ptr, i32, i32 }, i32 iterIndex }
			// We need to: GEP to field 0 (managed), then GEP to field 0 (buffer), then load
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeStr);
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeStr);
			// For struct parameters, alloca contains a pointer to the struct, need to load it first
			mir::MIRValue *structBase = alloca;
			if (isStructParameter(arrayIndexExpr->arrayName)) {
				structBase = builder->createLoad(mir::MIRType::getPtr(), alloca, "arr.structptr");
			}
			// GEP to field 0 to get __ManagedArrayData struct
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structBase, 0, "arr.managed");
			// GEP to field 0 of __ManagedArrayData to get buffer pointer field
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, "arr._buffer.ptr");
			arrayPtr = builder->createLoad(mir::MIRType::getPtr(), bufferField, "arr.buffer");
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

	// Nil coalescing: optionalExpr or defaultExpr
	// Returns the unwrapped value if optional has value, otherwise the default
	if (auto *coalesceExpr = dynamic_cast<OrCoalesceExprAST *>(expr)) {
		// Evaluate optional expression
		mir::MIRValue *optionalValue = generateExpr(coalesceExpr->optionalExpr.get());

		// Get the optional type
		std::string optionalTypeStr = getExpressionMaxonType(coalesceExpr->optionalExpr.get());
		mir::MIRType *optionalType = getTypeFromString(optionalTypeStr);

		// Get the unwrapped type using centralized type conversion
		std::string unwrappedTypeStr = maxon::TypeConversion::unwrapOptionalType(optionalTypeStr);
		mir::MIRType *unwrappedType = getTypeFromString(unwrappedTypeStr);

		// Store optional to stack
		mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "coalesce.optional");
		builder->createStore(optionalValue, optionalAlloca);

		// Load tag (first byte) - field 0
		mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
		mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");

		// Compare tag == 1 (has value)
		mir::MIRValue *hasValue = builder->createICmpEq(tag, builder->getInt8(1), "has.value");

		// Create basic blocks
		mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("coalesce.hasvalue");
		mir::MIRBasicBlock *defaultBlock = builder->createBasicBlock("coalesce.default");
		mir::MIRBasicBlock *afterBlock = builder->createBasicBlock("coalesce.after");

		// Allocate result on stack
		mir::MIRValue *resultAlloca = builder->createAlloca(unwrappedType, "coalesce.result");

		builder->createCondBr(hasValue, hasValueBlock, defaultBlock);

		// Has-value block: extract and store to result
		builder->setInsertPoint(hasValueBlock);
		mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
		mir::MIRValue *unwrappedValue = builder->createLoad(unwrappedType, valuePtr, "unwrapped.val");
		builder->createStore(unwrappedValue, resultAlloca);
		builder->createBr(afterBlock);

		// Default block: evaluate default expression and store
		builder->setInsertPoint(defaultBlock);
		mir::MIRValue *defaultValue = generateExpr(coalesceExpr->defaultExpr.get());
		// For struct types, generateExpr returns a pointer, need to load the value
		if (structTypes.find(unwrappedTypeStr) != structTypes.end()) {
			defaultValue = builder->createLoad(unwrappedType, defaultValue, "default.val");
		}
		builder->createStore(defaultValue, resultAlloca);
		builder->createBr(afterBlock);

		// After block: load and return result
		builder->setInsertPoint(afterBlock);
		return builder->createLoad(unwrappedType, resultAlloca, "coalesce.result.val");
	}

	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		// Special case: 'or' with optional left operand is nil coalescing
		// This handles cases where the parser created BinaryExprAST for what is semantically nil coalescing
		if (binExpr->op == 'O') {
			std::string leftTypeStr = getExpressionMaxonType(binExpr->left.get());
			if (maxon::TypeConversion::isOptionalType(leftTypeStr)) {
				// Generate nil coalescing code (same as OrCoalesceExprAST)
				mir::MIRValue *optionalValue = generateExpr(binExpr->left.get());
				mir::MIRType *optionalType = getTypeFromString(leftTypeStr);

				std::string unwrappedTypeStr = maxon::TypeConversion::unwrapOptionalType(leftTypeStr);
				mir::MIRType *unwrappedType = getTypeFromString(unwrappedTypeStr);

				mir::MIRValue *optionalAlloca = builder->createAlloca(optionalType, "coalesce.optional");
				builder->createStore(optionalValue, optionalAlloca);

				mir::MIRValue *tagPtr = builder->createStructGEP(optionalType, optionalAlloca, 0, "tag.ptr");
				mir::MIRValue *tag = builder->createLoad(mir::MIRType::getInt8(), tagPtr, "tag");
				mir::MIRValue *hasValue = builder->createICmpEq(tag, builder->getInt8(1), "has.value");

				mir::MIRBasicBlock *hasValueBlock = builder->createBasicBlock("coalesce.hasvalue");
				mir::MIRBasicBlock *defaultBlock = builder->createBasicBlock("coalesce.default");
				mir::MIRBasicBlock *afterBlock = builder->createBasicBlock("coalesce.after");

				mir::MIRValue *resultAlloca = builder->createAlloca(unwrappedType, "coalesce.result");
				builder->createCondBr(hasValue, hasValueBlock, defaultBlock);

				builder->setInsertPoint(hasValueBlock);
				mir::MIRValue *valuePtr = builder->createStructGEP(optionalType, optionalAlloca, 1, "value.ptr");
				mir::MIRValue *unwrappedValue = builder->createLoad(unwrappedType, valuePtr, "unwrapped.val");
				builder->createStore(unwrappedValue, resultAlloca);
				builder->createBr(afterBlock);

				builder->setInsertPoint(defaultBlock);
				mir::MIRValue *defaultValue = generateExpr(binExpr->right.get());
				// For struct types, generateExpr returns a pointer, need to load the value
				if (structTypes.find(unwrappedTypeStr) != structTypes.end()) {
					defaultValue = builder->createLoad(unwrappedType, defaultValue, "default.val");
				}
				builder->createStore(defaultValue, resultAlloca);
				builder->createBr(afterBlock);

				builder->setInsertPoint(afterBlock);
				return builder->createLoad(unwrappedType, resultAlloca, "coalesce.result.val");
			}
		}

		// For == and != on enum types, compare tag values
		if (binExpr->op == 'E' || binExpr->op == 'N') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			// Check if this is an enum type
			auto enumIt = enumTypes.find(leftType);
			if (enumIt != enumTypes.end()) {
				const EnumCodegenInfo &enumInfo = enumIt->second;

				// For simple enums (no associated values), compare the i8 values directly
				if (!enumInfo.hasAssociatedValues) {
					mir::MIRValue *leftVal = generateExpr(binExpr->left.get());
					mir::MIRValue *rightVal = generateExpr(binExpr->right.get());
					mir::MIRValue *result = (binExpr->op == 'E')
												? builder->createICmpEq(leftVal, rightVal, "enum.eq")
												: builder->createICmpNe(leftVal, rightVal, "enum.ne");
					return result;
				}

				// For enums with associated values, we need pointers to access the tag field
				// Get pointer to left operand
				mir::MIRValue *leftPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
					leftPtr = namedValues[varExpr->name];
				} else {
					// Other expressions (like enum case construction) return alloca pointers
					leftPtr = generateExpr(binExpr->left.get());
				}

				// Get pointer to right operand
				mir::MIRValue *rightPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
					rightPtr = namedValues[varExpr->name];
				} else {
					rightPtr = generateExpr(binExpr->right.get());
				}

				// Get tag from left (field 0)
				mir::MIRValue *leftTagPtr = builder->createStructGEP(enumInfo.mirType, leftPtr, 0, "left.tag.ptr");
				mir::MIRValue *leftTag = builder->createLoad(mir::MIRType::getInt8(), leftTagPtr, "left.tag");

				// Get tag from right (field 0)
				mir::MIRValue *rightTagPtr = builder->createStructGEP(enumInfo.mirType, rightPtr, 0, "right.tag.ptr");
				mir::MIRValue *rightTag = builder->createLoad(mir::MIRType::getInt8(), rightTagPtr, "right.tag");

				mir::MIRValue *result = (binExpr->op == 'E')
											? builder->createICmpEq(leftTag, rightTag, "enum.eq")
											: builder->createICmpNe(leftTag, rightTag, "enum.ne");
				return result;
			}

			// Check if this is an Equatable struct type (has TypeName.equals function)
			auto structIt = structTypes.find(leftType);
			if (structIt != structTypes.end() && typeIsEquatable(leftType)) {
				// Generate call to TypeName.equals(left, right)
				std::string equalsFuncName = leftType + ".equals";
				mir::MIRFunction *equalsFunc = module->getFunction(equalsFuncName);

				if (equalsFunc) {
					// Helper to get pointer for an Equatable operand
					// Handles: local variables, implicit field access, method calls, etc.
					auto getEquatableOperandPtr = [&](ExprAST *expr) -> mir::MIRValue * {
						// Case 1: Local variable or parameter
						if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
							mir::MIRValue *ptr = namedValues[varExpr->name];
							if (ptr) {
								// For struct parameters, the alloca contains a pointer to the struct
								// We need to load that pointer to get the actual struct pointer
								if (isStructParameter(varExpr->name)) {
									return builder->createLoad(mir::MIRType::getPtr(), ptr, varExpr->name + ".eqptr");
								}
								return ptr;
							}
							// Case 2: Implicit field access (e.g., 'pos' in a method)
							if (!currentReceiverType.empty()) {
								auto fieldsIt = structFields.find(currentReceiverType);
								if (fieldsIt != structFields.end()) {
									for (size_t i = 0; i < fieldsIt->second.size(); i++) {
										if (fieldsIt->second[i].first == varExpr->name) {
											// Generate field pointer through implicit 'self'
											mir::MIRValue *selfAlloca = namedValues["self"];
											if (selfAlloca) {
												mir::MIRValue *selfPtr;
												if (isStructParameter("self")) {
													selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self.ptr");
												} else {
													selfPtr = selfAlloca;
												}
												mir::MIRType *structType = structTypes[currentReceiverType];
												return builder->createStructGEP(structType, selfPtr, static_cast<int>(i), varExpr->name + ".ptr");
											}
											break;
										}
									}
								}
							}
							return nullptr;
						}
						// Case 3: String literal
						if (auto *strLit = dynamic_cast<StringLiteralExprAST *>(expr)) {
							return generateStringLiteral(strLit);
						}
						// Case 4: Other expressions (method calls, etc.) - need to store result in temp
						mir::MIRValue *val = generateExpr(expr);
						if (val && val->type->isStruct()) {
							mir::MIRValue *tempAlloca = builder->createAlloca(val->type, "eq.tmp");
							builder->createStore(val, tempAlloca);
							return tempAlloca;
						}
						return val;
					};

					mir::MIRValue *leftPtr = getEquatableOperandPtr(binExpr->left.get());
					mir::MIRValue *rightPtr = getEquatableOperandPtr(binExpr->right.get());

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

		// For <, >, <=, >= on character type, compare by first codepoint value
		// This provides lexicographic ordering for single-codepoint chars
		if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || binExpr->op == 'G') {
			std::string leftType = getExpressionMaxonType(binExpr->left.get());

			if (leftType == "character") {
				// Get pointers to both char structs
				mir::MIRValue *leftPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->left.get())) {
					leftPtr = namedValues[varExpr->name];
					// For struct parameters (including character), need to load the pointer
					if (isStructParameter(varExpr->name)) {
						leftPtr = builder->createLoad(mir::MIRType::getPtr(), leftPtr, varExpr->name + ".ptr");
					}
				} else if (auto *charLit = dynamic_cast<CharacterExprAST *>(binExpr->left.get())) {
					leftPtr = generateCharLiteral(charLit);
				} else {
					leftPtr = generateExpr(binExpr->left.get());
				}

				mir::MIRValue *rightPtr = nullptr;
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(binExpr->right.get())) {
					rightPtr = namedValues[varExpr->name];
					// For struct parameters (including character), need to load the pointer
					if (isStructParameter(varExpr->name)) {
						rightPtr = builder->createLoad(mir::MIRType::getPtr(), rightPtr, varExpr->name + ".ptr");
					}
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

				if (!charType || !managedDataType || !unsizedArrayType) {
					reportError("character comparison requires character and __ManagedStringData types",
								binExpr->line, binExpr->column);
					return nullptr;
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
			uint64_t leftBits = left->type->getSizeInBytes() * 8;
			uint64_t rightBits = right->type->getSizeInBytes() * 8;
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
					mir::MIRValue *argValue = generateExpr(callExpr->args[i].value.get());
					if (!argValue) {
						reportError("Failed to generate argument for enum case construction",
									callExpr->line, callExpr->column);
					}

					mir::MIRType *argType = getTypeFromString(assocValues[i].second);

					// For struct types (like string), generateExpr may return an alloca pointer
					// We need to load the value before storing into the payload
					if (argType->isStruct() && argValue->type == mir::MIRType::getPtr()) {
						argValue = builder->createLoad(argType, argValue, "enum.arg." + assocValues[i].first);
					}

					// Store at offset within payload
					mir::MIRValue *fieldPtr = builder->createGEP(mir::MIRType::getInt8(), payloadPtr,
																 {builder->getInt64(static_cast<int64_t>(offset))},
																 "enum.payload." + assocValues[i].first);
					// Cast to appropriate pointer type and store
					builder->createStore(argValue, fieldPtr);

					offset += argType->getSizeInBytes();
				}
			}

			// Return the alloca pointer (enum variables hold pointers)
			return enumAlloca;
		}

		// Handle math intrinsics
		if (Lexer::isMathIntrinsic(callExpr->callee)) {
			return generateMathIntrinsic(callExpr);
		}

		std::string effectiveCallee = callExpr->resolvedCallee.empty() ? callExpr->callee : callExpr->resolvedCallee;

		// Resolve primitive type methods when called on typed arguments
		// This handles cases like `key.hash()` where key is a type parameter bound to int
		// Also handles `_keys[index].equals(key)` where _keys is []KeyType
		// Also extract bare method name from qualified callee like "Element.hash" -> "hash"
		std::string bareMethodName = effectiveCallee;
		size_t dotPos = bareMethodName.rfind('.');
		if (dotPos != std::string::npos) {
			bareMethodName = bareMethodName.substr(dotPos + 1);
		}
		if ((bareMethodName == "hash" || bareMethodName == "equals") && !callExpr->args.empty()) {
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
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].value.get())) {
				argType = lookupVarType(varExpr->name);
			}
			// Check if first arg is an array index (e.g., _keys[index] or elements[index])
			else if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(callExpr->args[0].value.get())) {
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
					// Extract element type from array type like "_ManagedArray<int>" or "array<int>"
					if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
						argType = maxon::TypeConversion::getArrayElementType(arrayType);
					} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
						argType = maxon::TypeConversion::getArrayStructElementType(arrayType);
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
				effectiveCallee = argType + "." + bareMethodName;
			}
			// Check if it's a struct type that implements Hashable/Equatable
			else if (!argType.empty()) {
				// For struct types like string, always transform hash/equals calls
				// Function names are argType.methodName (not argType.InterfaceName.methodName)
				std::string structMethod = argType + "." + bareMethodName;
				effectiveCallee = structMethod;

				// Ensure the function is declared - it may not have been added to reachable set
				// because the call graph analysis happens before generic instantiation
				if (!module->getFunction(structMethod)) {
					ensureStructMethodDeclared(argType, bareMethodName);
				}
			}
		}

		// Handle string/substring/cstring intrinsics via registry
		if (auto method = IntrinsicCodegenRegistry::instance().getMethod(effectiveCallee)) {
			mir::MIRValue *result = (this->*method)(callExpr);
			return result;
		}

		// Handle primitive type methods (int.hash, int.equals, etc.)
		if (effectiveCallee == "int.hash" || effectiveCallee == "byte.hash" || effectiveCallee == "character.hash") {
			// Multiplicative hash using Knuth's golden ratio constant
			// hash = value * 2654435769 (which is 0x9E3779B9)
			if (callExpr->args.empty()) {
				reportError("hash() requires self argument",
							callExpr->line, callExpr->column);
			}
			mir::MIRValue *value = generateExpr(callExpr->args[0].value.get());
			// Extend byte/character to int if needed
			if (effectiveCallee != "int.hash") {
				value = builder->createZExt(value, mir::MIRType::getInt64(), "hash.extend");
			}
			mir::MIRValue *multiplier = mir::MIRValue::createConstantInt(mir::MIRType::getInt64(), 0x9E3779B97F4A7C15ULL);
			return builder->createMul(value, multiplier, "hash");
		}

		if (effectiveCallee == "int.equals" || effectiveCallee == "byte.equals" || effectiveCallee == "character.equals") {
			// Simple equality comparison
			if (callExpr->args.size() < 2) {
				reportError("equals() requires self and other arguments",
							callExpr->line, callExpr->column);
			}
			mir::MIRValue *self = generateExpr(callExpr->args[0].value.get());
			mir::MIRValue *other = generateExpr(callExpr->args[1].value.get());
			return builder->createICmpEq(self, other, "equals");
		}

		// Handle int.parse(string) -> int or nil (static method)
		if (effectiveCallee == "int.parse") {
			if (callExpr->args.size() != 1) {
				reportError("int.parse() requires exactly one string argument",
							callExpr->line, callExpr->column);
			}

			// Get the managed pointer from the string using the helper
			mir::MIRValue *managedPtr = getManagedStringPtr(callExpr->args[0].value.get());

			// The result is Optional<int64> - allocate space for it
			mir::MIRType *optionalType = mir::MIRType::getOptional(mir::MIRType::getInt64());
			mir::MIRValue *resultAlloca = builder->createAlloca(optionalType, "int.parse.result");

			// Call runtime function __int_parse(result_ptr, managed_ptr)
			// It takes an out-pointer for the result and returns void
			mir::MIRFunction *parseFunc = getOrDeclareFunction(
				"__int_parse",
				mir::MIRType::getVoid(),
				{mir::MIRType::getPtr(), mir::MIRType::getPtr()});
			builder->createCall(parseFunc, {resultAlloca, managedPtr}, "");

			// Load and return the result
			return builder->createLoad(optionalType, resultAlloca, "parsed");
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
				mir::MIRValue *argVal = generateExpr(callExpr->args[i].value.get());
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

			// For synthesized default method bodies, method calls like "count" or "Collection.count"
			// need to be resolved to the concrete type method like "array<int>.count"
			if (!calleeF && !currentReceiverType.empty()) {
				// Extract the bare method name from qualified names like "Collection.count"
				std::string bareMethodName = effectiveCallee;
				size_t dotPos = effectiveCallee.find('.');
				if (dotPos != std::string::npos) {
					bareMethodName = effectiveCallee.substr(dotPos + 1);
				}
				// Try looking up the method on the current receiver type
				std::string receiverMethod = currentReceiverType + "." + bareMethodName;
				calleeF = module->getFunction(receiverMethod);
				if (calleeF) {
					effectiveCallee = receiverMethod; // Update for later use
				}
			}

			// For generic struct methods like "array.count", specialize based on first arg type
			// e.g., array.count(errors) where errors is array<SemanticError> -> array<SemanticError>.count
			if (!calleeF && !callExpr->args.empty()) {
				std::string qualifier = effectiveCallee;
				std::string bareMethodName = effectiveCallee;
				size_t dotPos = effectiveCallee.find('.');
				if (dotPos != std::string::npos) {
					qualifier = effectiveCallee.substr(0, dotPos);
					bareMethodName = effectiveCallee.substr(dotPos + 1);
				}

				// Check if the qualifier is a generic template (array, map, set, etc.)
				if (qualifier == "array" || qualifier == "map" || qualifier == "set") {
					// Get the type of the first argument (the receiver)
					std::string firstArgType;
					if (auto *varExpr = dynamic_cast<VariableExprAST *>(callExpr->args[0].value.get())) {
						auto it = variableTypes.find(varExpr->name);
						if (it != variableTypes.end()) {
							firstArgType = it->second;
						} else if (!currentReceiverType.empty()) {
							// Check struct fields
							auto fieldsIt = structFields.find(currentReceiverType);
							if (fieldsIt != structFields.end()) {
								for (const auto &field : fieldsIt->second) {
									if (field.first == varExpr->name) {
										firstArgType = field.second;
										break;
									}
								}
							}
						}
					}

					// If first arg is a specialized type like array<SemanticError>, use that
					if (!firstArgType.empty() && firstArgType.find('<') != std::string::npos) {
						std::string specializedMethod = firstArgType + "." + bareMethodName;
						calleeF = module->getFunction(specializedMethod);
						if (calleeF) {
							effectiveCallee = specializedMethod;
						}
					}
				}
			}

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
			// Before erroring, check if this is actually a function variable call
			// that wasn't detected by semantic analysis (e.g., in synthesized default method bodies)
			auto varTypeIt = variableTypes.find(effectiveCallee);
			if (varTypeIt != variableTypes.end() && maxon::TypeConversion::isFunctionType(varTypeIt->second)) {
				// This is a function variable call - handle it as an indirect call
				auto it = namedValues.find(effectiveCallee);
				if (it != namedValues.end()) {
					mir::MIRValue *funcPtrAlloca = it->second;
					mir::MIRValue *funcPtr = builder->createLoad(mir::MIRType::getPtr(), funcPtrAlloca, "funcptr");

					// Generate arguments
					std::vector<mir::MIRValue *> argsV;
					for (size_t i = 0; i < callExpr->args.size(); i++) {
						mir::MIRValue *argVal = generateExpr(callExpr->args[i].value.get());
						argsV.push_back(argVal);
					}

					// Parse the function type to determine return type
					std::vector<std::string> paramTypes;
					std::string returnTypeStr;
					maxon::TypeConversion::parseFunctionType(varTypeIt->second, paramTypes, returnTypeStr);

					mir::MIRType *returnType = getTypeFromString(returnTypeStr);
					std::vector<mir::MIRType *> mirParamTypes;
					for (const auto &pt : paramTypes) {
						mirParamTypes.push_back(getTypeFromString(pt));
					}
					return builder->createCallIndirect(funcPtr, returnType, mirParamTypes, argsV, "fncall");
				}
			}
			reportError("Unknown function referenced: " + effectiveCallee,
						callExpr->line, callExpr->column);
		}

		// Generate arguments
		std::vector<mir::MIRValue *> argsV;
		size_t argIdx = 0;

		// Detect sibling method calls at codegen time for generic struct methods
		// Generic struct method bodies are not analyzed by semantic analyzer, so isSiblingMethodCall may not be set
		bool isSiblingCall = callExpr->isSiblingMethodCall;
		if (!isSiblingCall && !currentReceiverType.empty() && namedValues.find("self") != namedValues.end()) {
			// Check if this is a method call on the current receiver type
			// effectiveCallee would be "set<int>.grow" and currentReceiverType would be "set<int>"
			std::string expectedPrefix = currentReceiverType + ".";
			if (effectiveCallee.find(expectedPrefix) == 0 && calleeF && !calleeF->parameters.empty()) {
				// Check if the function expects 'self' as first parameter but we're not passing it
				if (calleeF->parameters[0]->name == "self" && callExpr->args.size() < calleeF->parameters.size()) {
					isSiblingCall = true;
				}
			}
		}

		// For sibling method calls, inject 'self' as the first argument
		if (isSiblingCall && !currentReceiverType.empty()) {
			// Load self pointer from the first parameter (which is always 'self' in methods)
			if (namedValues.find("self") != namedValues.end()) {
				mir::MIRValue *selfAlloca = namedValues["self"];
				mir::MIRValue *selfPtr = builder->createLoad(mir::MIRType::getPtr(), selfAlloca, "self");
				argsV.push_back(selfPtr);
				argIdx++;
			}
		}

		// Build inverse mapping: paramToArgMapping[paramIdx] = argIdx
		// This allows us to generate arguments in parameter definition order
		std::vector<int> paramToArgMapping;
		size_t numSourceParams = callExpr->args.size(); // number of source-level arguments
		if (!callExpr->argToParamMapping.empty()) {
			// Find max param index to size the inverse mapping
			size_t maxParamIdx = 0;
			for (size_t paramIdx : callExpr->argToParamMapping) {
				maxParamIdx = std::max(maxParamIdx, paramIdx);
			}
			paramToArgMapping.resize(maxParamIdx + 1, -1);
			for (size_t i = 0; i < callExpr->argToParamMapping.size(); i++) {
				paramToArgMapping[callExpr->argToParamMapping[i]] = static_cast<int>(i);
			}
			numSourceParams = maxParamIdx + 1;
		}

		for (size_t paramIdx = 0; paramIdx < numSourceParams; paramIdx++) {
			// Find which argument corresponds to this parameter
			size_t i;
			if (!paramToArgMapping.empty()) {
				int argIdxForParam = paramToArgMapping[paramIdx];
				if (argIdxForParam < 0) {
					// Parameter not provided - should have default value (handled by semantic analyzer)
					continue;
				}
				i = static_cast<size_t>(argIdxForParam);
			} else {
				// No reordering needed - arguments are in parameter order
				if (paramIdx >= callExpr->args.size())
					break;
				i = paramIdx;
			}
			auto &arg = callExpr->args[i];
			mir::MIRType *paramType = (argIdx < calleeF->parameters.size())
										  ? calleeF->parameters[argIdx]->type
										  : mir::MIRType::getInt64();

			// Handle array and struct arguments
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(arg.value.get())) {
				std::string varType = variableTypes[varExpr->name];
				mir::MIRValue *alloca = namedValues[varExpr->name];

				// Handle array struct arguments - pass pointer to struct
				if (alloca && (maxon::TypeConversion::isArrayStructType(varType) ||
							   maxon::TypeConversion::isManagedArrayType(varType))) {
					// Arrays use struct types and are passed by pointer (like structs)
					if (isStructParameter(varExpr->name)) {
						mir::MIRValue *ptrVal = builder->createLoad(mir::MIRType::getPtr(), alloca, varExpr->name);
						argsV.push_back(ptrVal);
					} else {
						argsV.push_back(alloca);
					}
					argIdx++;
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
			mir::MIRValue *argVal = generateExpr(arg.value.get());

			// Check if argument is nil (generateExpr returns nullptr for nil)
			bool isNilArg = (!argVal && dynamic_cast<NilExprAST *>(arg.value.get()));
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

			// If the argument is a pointer (from variable alloca) but parameter expects
			// a value type (not pointer, not struct), load the value from the alloca
			// This handles enum and primitive type arguments passed to value parameters
			if (argVal && argVal->type->isPointer() && !paramType->isPointer() && !paramType->isStruct()) {
				argVal = builder->createLoad(paramType, argVal, "arg.load");
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
				// Use paramIdx (parameter position) not i (call-site argument position) for named args
				size_t actualParamIdx = paramIdx + (callExpr->isSiblingMethodCall ? 1 : 0);
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
							std::string argMaxonType = getExpressionMaxonType(arg.value.get());

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

			// Track struct parameters (including array struct types and _ManagedArray types)
			if (structTypes.find(param.type) != structTypes.end() ||
				maxon::TypeConversion::isArrayStructType(param.type) ||
				maxon::TypeConversion::isManagedArrayType(param.type)) {
				structParameters.insert(param.name);
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
					builder->createRet(builder->getInt64(0));
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
		mir::MIRValue *argVal = generateExpr(arg.value.get());
		// Promote int to float
		if (argVal->type->isInteger()) {
			argVal = builder->createSIToFP(argVal, mir::MIRType::getFloat64(), "promotetmp");
		}
		args.push_back(argVal);
	}

	mir::MIRValue *result = builder->createCall(mathFunc, args);

	// Rounding functions return int in Maxon, convert from float
	if (isRoundingFunction) {
		result = builder->createFPToSI(result, mir::MIRType::getInt64(), "fptositmp");
	}

	return result;
}

//==============================================================================
// Struct Literal Generation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateStructLiteral(StructInitExprAST *structInit,
													   mir::MIRValue *targetAlloca) {
	// Substitute "Self" with current receiver type
	std::string structName = structInit->structName;
	if (structName == "Self" && !currentReceiverType.empty()) {
		structName = currentReceiverType;
	} else if (!currentTypeBindings.empty() && !currentReceiverType.empty()) {
		// Extract base template name from currentReceiverType (e.g., "set<int>" -> "set")
		std::string baseTemplateName = currentReceiverType;
		size_t anglePos = currentReceiverType.find('<');
		if (anglePos != std::string::npos) {
			baseTemplateName = currentReceiverType.substr(0, anglePos);
		}
		// If struct initializer is the base template, use specialized type
		if (structName == baseTemplateName) {
			structName = currentReceiverType;
		}
	}

	mir::MIRType *structType = structTypes[structName];
	if (!structType) {
		reportError("Unknown struct type: " + structName,
					structInit->line, structInit->column);
	}

	// Mark struct type as used for lazy type emission
	structType->used = true;
	markFieldTypesUsed(structType);

	// Create alloca if not provided
	mir::MIRValue *structAlloca = targetAlloca;
	if (!structAlloca) {
		structAlloca = builder->createAlloca(structType, "struct.tmp");
	}

	// Get field definitions and defaults
	const auto &fields = structFields[structName];
	const auto &defaults = structFieldDefaults[structName];

	// Build a map of provided field values for quick lookup
	std::map<std::string, const StructInitField *> providedFields;
	for (const auto &initField : structInit->fields) {
		providedFields[initField.name] = &initField;
	}

	// Initialize each field
	for (size_t fieldIndex = 0; fieldIndex < fields.size(); fieldIndex++) {
		const std::string &fieldName = fields[fieldIndex].first;
		const std::string &fieldTypeStr = fields[fieldIndex].second;

		mir::MIRValue *fieldPtr = builder->createStructGEP(structType, structAlloca,
														   static_cast<int>(fieldIndex), fieldName);

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

		// Handle array field initialization
		if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(valueExpr)) {
			if (!arrayLit->values.empty()) {
				// Value-initialized array [1, 2, 3]
				for (size_t i = 0; i < arrayLit->values.size(); i++) {
					mir::MIRValue *elemValue = generateExpr(arrayLit->values[i].get());
					mir::MIRValue *elemPtr = builder->createArrayGEP(elemValue->type, fieldPtr,
																	 mir::MIRValue::createConstantInt(mir::MIRType::getInt64(), i),
																	 fieldName + ".elem");
					builder->createStore(elemValue, elemPtr);
				}
			}
			// Zero-initialized array - already zero from alloca, nothing needed
		} else if (auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(valueExpr)) {
			// Handle sized array expression: array of T (empty) or array of N T (sized)
			std::string elementTypeName = sizedArray->elementType;

			// Substitute type parameters if we're inside a generic method
			if (!currentTypeBindings.empty()) {
				auto bindingIt = currentTypeBindings.find(elementTypeName);
				if (bindingIt != currentTypeBindings.end()) {
					elementTypeName = bindingIt->second;
				}
			}

			mir::MIRType *elementType = getTypeFromString(elementTypeName);

			// Get or create the array struct type for this element type
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// For empty arrays (no size specified), create zero-initialized struct
			// For sized arrays, allocate buffer and initialize
			if (sizedArray->hasVariableSize() || sizedArray->size > 0) {
				// Get or compute size
				int64_t arraySize = sizedArray->size;
				mir::MIRValue *sizeVal = nullptr;
				if (sizedArray->hasVariableSize()) {
					sizeVal = generateExpr(sizedArray->sizeExpr.get());
				} else {
					sizeVal = builder->getInt64(arraySize);
				}

				// Allocate buffer
				initHeapManagement();
				mir::MIRFunction *mallocFunc = module->getFunction("malloc");
				uint64_t elementSize = elementType->getSizeInBytes();
				mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
				mir::MIRValue *sizeExt = builder->createSExt(sizeVal, mir::MIRType::getInt64(), "size.ext");
				mir::MIRValue *totalSize = builder->createMul(sizeExt, elemSizeVal, "total.size");
				mir::MIRValue *arrayTag = module->createGlobalString(".__tag.arr.init." + fieldName, "init array");
				mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {totalSize, arrayTag}, fieldName + ".buffer");

				// Zero-initialize the buffer
				mir::MIRFunction *memsetFunc = module->getFunction("memset");
				if (!memsetFunc) {
					initHeapManagement();
					memsetFunc = module->getFunction("memset");
				}
				builder->createCall(memsetFunc, {bufferPtr, builder->getInt64(0), totalSize});

				// Initialize the nested struct
				mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, fieldName + ".managed");
				mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, fieldName + "._buffer");
				builder->createStore(bufferPtr, bufferField);
				mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, fieldName + "._len");
				builder->createStore(sizeVal, lenField);
				mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, fieldName + "._capacity");
				builder->createStore(sizeVal, capField);
				mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, fieldPtr, 1, fieldName + ".iterIndex");
				builder->createStore(builder->getInt64(0), iterField);
			} else {
				// Empty array (array of T) - initialize with nullptr and zero length
				mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, fieldName + ".managed");
				mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, fieldName + "._buffer");
				builder->createStore(mir::MIRValue::createConstantNull(), bufferField);
				mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, fieldName + "._len");
				builder->createStore(builder->getInt64(0), lenField);
				mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, fieldName + "._capacity");
				builder->createStore(builder->getInt64(0), capField);
				mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, fieldPtr, 1, fieldName + ".iterIndex");
				builder->createStore(builder->getInt64(0), iterField);
			}
		} else if (maxon::TypeConversion::isManagedArrayType(fieldTypeStr)) {
			// Slice field ([]T) - need to create fat pointer {ptr, len}
			std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(fieldTypeStr);
			std::string unsizedArrayTypeName = "_ManagedArray_" + elementTypeStr;

			mir::MIRType *unsizedArrayType = structTypes[unsizedArrayTypeName];
			if (!unsizedArrayType) {
				unsizedArrayType = module->getOrCreateStructType(
					unsizedArrayTypeName,
					{mir::MIRType::getPtr(), mir::MIRType::getInt64()});
				structTypes[unsizedArrayTypeName] = unsizedArrayType;
			}

			auto *varExpr = dynamic_cast<VariableExprAST *>(valueExpr);
			if (varExpr) {
				std::string varType = variableTypes[varExpr->name];
				if (maxon::TypeConversion::isManagedArrayType(varType)) {
					// Variable-sized array - load data pointer and length from header
					mir::MIRValue *arrayAlloca = namedValues[varExpr->name];
					if (arrayAlloca) {
						mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
						mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
																	  {builder->getInt64(-16)}, "length.ptr");
						mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt64(), lengthPtr, "length");

						mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, fieldName + ".ptr");
						builder->createStore(dataPtr, dataPtrField);
						mir::MIRValue *lenField = builder->createStructGEP(unsizedArrayType, fieldPtr, 1, fieldName + ".len");
						builder->createStore(length, lenField);
					} else {
						reportError("Unknown variable in struct field init: " + varExpr->name,
									varExpr->line, varExpr->column);
					}
				} else {
					mir::MIRValue *fieldValue = generateExpr(valueExpr);
					builder->createStore(fieldValue, fieldPtr);
				}
			} else {
				mir::MIRValue *fieldValue = generateExpr(valueExpr);
				builder->createStore(fieldValue, fieldPtr);
			}
		} else {
			// Regular field value
			mir::MIRValue *fieldValue = generateExpr(valueExpr);

			mir::MIRType *fieldType = getTypeFromString(fieldTypeStr);

			// Handle optional field wrapping
			if (maxon::TypeConversion::isOptionalType(fieldTypeStr)) {
				std::string valueTypeStr = getExpressionMaxonType(valueExpr);
				bool isNilValue = (!fieldValue && dynamic_cast<NilExprAST *>(valueExpr));

				if (isNilValue) {
					fieldValue = createNilOptional(fieldType);
				} else if (fieldValue && !maxon::TypeConversion::isOptionalType(valueTypeStr)) {
					fieldValue = createSomeOptional(fieldType, fieldValue);
				}
			}

			// For struct types, generateExpr returns a pointer to an alloca
			// We need to load the struct value and store it
			if (fieldValue && fieldType->isStruct() && fieldValue->type == mir::MIRType::getPtr()) {
				mir::MIRValue *loadedValue = builder->createLoad(fieldType, fieldValue, fieldName + ".load");
				builder->createStore(loadedValue, fieldPtr);
			} else if (fieldValue) {
				builder->createStore(fieldValue, fieldPtr);
			} else {
				reportError("Failed to generate value for field '" + fieldName + "'",
							structInit->line, structInit->column);
			}
		}
	}

	return structAlloca;
}

//==============================================================================
// Type Trait Helpers
//==============================================================================

bool MIRCodeGenerator::typeIsEquatable(const std::string &typeName) const {
	return module->getFunction(typeName + ".equals") != nullptr;
}
