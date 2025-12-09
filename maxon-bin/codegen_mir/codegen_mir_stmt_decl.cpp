/**
 * MIR Code Generator - Variable and Let Declaration Statements
 *
 * This file implements code generation for var and let declarations.
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <algorithm>
#include <stdexcept>

void MIRCodeGenerator::generateVarDecl(VarDeclStmtAST *varDecl, mir::MIRFunction *function) {
	// Handle array literal initialization: [val1, val2, ...]
	if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(varDecl->initializer.get())) {
		mir::MIRType *elementType;
		std::string elementTypeName;
		std::vector<mir::MIRValue *> initValues;
		int constantArraySize = static_cast<int>(arrayLiteral->values.size());

		for (auto &valExpr : arrayLiteral->values) {
			mir::MIRValue *val = generateExpr(valExpr.get());
			if (!val) {
				reportError("Failed to generate array element value",
							varDecl->line, varDecl->column);
			}
			initValues.push_back(val);
		}

		// Infer element type from first value
		elementType = initValues[0]->type;
		if (elementType->kind == mir::MIRTypeKind::Int32) {
			elementTypeName = "int";
		} else if (elementType->kind == mir::MIRTypeKind::Float64) {
			elementTypeName = "float";
		} else if (elementType->kind == mir::MIRTypeKind::Int8) {
			elementTypeName = "character";
		} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
			elementTypeName = "ptr";
		} else {
			reportError("Unsupported array element type",
						varDecl->line, varDecl->column);
		}

		uint64_t elementSize = elementType->sizeInBytes;
		mir::MIRValue *arrayPtr;
		uint64_t totalSize = constantArraySize * elementSize;

		// Threshold for stack allocation (4KB) - larger arrays go on heap
		constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
		bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

		if (useHeap) {
			// Large array uses array<T> struct layout (4 fields)
			// Struct: { _buffer ptr, _len i32, _capacity i32, _iterIndex i32 }
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// Allocate data buffer (no header needed)
			mir::MIRValue *sizeVal = builder->getInt64(totalSize);
			mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {sizeVal}, varDecl->name + ".buffer");

			// Create array<T> struct alloca (this also instantiates array methods)
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, varDecl->name);

			// Get __ManagedArrayData type for the nested managed field
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
			mir::MIRValue *lengthVal = builder->getInt32(constantArraySize);

			// Field 0: managed (nested __ManagedArrayData struct)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, varDecl->name + ".managed");

			// Initialize the nested __ManagedArrayData fields
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, varDecl->name + ".managed._buffer");
			builder->createStore(bufferPtr, bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, varDecl->name + ".managed._len");
			builder->createStore(lengthVal, lenField);

			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, varDecl->name + ".managed._capacity");
			builder->createStore(lengthVal, capField);

			// Field 1: iterIndex
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, varDecl->name + ".iterIndex");
			builder->createStore(builder->getInt32(0), iterField);

			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
			arrayPtr = bufferPtr;

			// Track array struct for cleanup (dynamic - buffer can be reallocated)
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, structAlloca, elementTypeName, true});
			}
		} else {
			// Small array: stack-allocate directly (much faster)
			// Use array<T> struct with stack-allocated buffer
			mir::MIRType *arrayType = mir::MIRType::getArray(elementType, constantArraySize);
			mir::MIRValue *stackBuffer = builder->createAlloca(arrayType, varDecl->name + ".stack_buffer");

			// Create array<T> struct alloca (this also instantiates array methods)
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, varDecl->name);

			// Get __ManagedArrayData type for the nested managed field
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
			// NOTE: capacity = 0 signals stack-allocated buffer to push() intrinsic
			// When push needs to grow, it will malloc a new heap buffer instead of realloc
			mir::MIRValue *arraySizeVal = builder->getInt32(constantArraySize);
			mir::MIRValue *zeroCapacity = builder->getInt32(0);

			// Field 0: managed (nested __ManagedArrayData struct)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, varDecl->name + ".managed");

			// Initialize the nested __ManagedArrayData fields
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, varDecl->name + ".managed._buffer");
			builder->createStore(stackBuffer, bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, varDecl->name + ".managed._len");
			builder->createStore(arraySizeVal, lenField);

			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, varDecl->name + ".managed._capacity");
			builder->createStore(zeroCapacity, capField);

			// Field 1: iterIndex
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, varDecl->name + ".iterIndex");
			builder->createStore(builder->getInt32(0), iterField);

			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
			arrayPtr = stackBuffer;

			// Track array struct for cleanup (dynamic - buffer could be promoted to heap by push())
			// Cleanup will check capacity>0 to know if free is needed
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, structAlloca, elementTypeName, true});
			}
		}

		// Store each value
		for (int i = 0; i < constantArraySize; i++) {
			mir::MIRValue *indexVal = builder->getInt32(i);
			mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
			builder->createStore(initValues[i], elementPtr);
		}

		return;
	}

	// Handle sized array expression: array of N T or array of T
	if (auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(varDecl->initializer.get())) {
		std::string elementTypeName = sizedArray->elementType;

		// Substitute type parameters if we're inside a generic method
		if (!currentTypeBindings.empty()) {
			auto bindingIt = currentTypeBindings.find(elementTypeName);
			if (bindingIt != currentTypeBindings.end()) {
				elementTypeName = bindingIt->second;
			}
		}

		mir::MIRType *elementType = getTypeFromString(elementTypeName);
		uint64_t elementSize = elementType->sizeInBytes;

		// Handle variable-sized arrays (size is an expression)
		if (sizedArray->hasVariableSize()) {
			// Generate the size expression
			mir::MIRValue *sizeVal = generateExpr(sizedArray->sizeExpr.get());
			if (!sizeVal) {
				reportError("Failed to generate array size expression", varDecl->line, varDecl->column);
			}

			// Variable-sized arrays always use heap allocation
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// Calculate total size: size * elementSize
			mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
			mir::MIRValue *sizeExt = builder->createSExt(sizeVal, mir::MIRType::getInt64(), "size.ext");
			mir::MIRValue *totalSize = builder->createMul(sizeExt, elemSizeVal, "total.size");

			// Allocate buffer
			mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {totalSize}, varDecl->name + ".buffer");

			// Create array<T> struct alloca (2 fields: { managed, iterIndex })
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, varDecl->name);

			// Get __ManagedArrayData type for the nested managed field
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
			// Field 0: managed (nested __ManagedArrayData struct)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, varDecl->name + ".managed");

			// Initialize the nested __ManagedArrayData fields
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, varDecl->name + ".managed._buffer");
			builder->createStore(bufferPtr, bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, varDecl->name + ".managed._len");
			builder->createStore(sizeVal, lenField);

			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, varDecl->name + ".managed._capacity");
			builder->createStore(sizeVal, capField);

			// Field 1: iterIndex
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, varDecl->name + ".iterIndex");
			builder->createStore(builder->getInt32(0), iterField);

			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);

			// Zero-initialize using memset
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			builder->createCall(memsetFunc, {bufferPtr, zeroVal, totalSize});

			// Track array struct for cleanup (dynamic - buffer can be reallocated)
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, structAlloca, elementTypeName, true});
			}

			return;
		}

		// Constant-sized array
		int arraySize = sizedArray->size;
		uint64_t totalSize = arraySize * elementSize;
		mir::MIRValue *arrayPtr;

		// Threshold for stack allocation (4KB) - larger arrays go on heap
		constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
		bool useHeap = totalSize > STACK_ARRAY_THRESHOLD || arraySize == 0;

		if (useHeap || arraySize == 0) {
			// Heap-allocate using array<T> struct layout
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// For empty arrays, allocate minimal buffer
			uint64_t allocSize = (arraySize > 0) ? totalSize : 8;
			mir::MIRValue *sizeVal = builder->getInt64(allocSize);
			mir::MIRValue *bufferPtr = builder->createCall(mallocFunc, {sizeVal}, varDecl->name + ".buffer");

			// Create array<T> struct alloca (2 fields: { managed, iterIndex })
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, varDecl->name);

			// Get __ManagedArrayData type for the nested managed field
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
			mir::MIRValue *lengthVal = builder->getInt32(arraySize);

			// Field 0: managed (nested __ManagedArrayData struct)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, varDecl->name + ".managed");

			// Initialize the nested __ManagedArrayData fields
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, varDecl->name + ".managed._buffer");
			builder->createStore(bufferPtr, bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, varDecl->name + ".managed._len");
			builder->createStore(lengthVal, lenField);

			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, varDecl->name + ".managed._capacity");
			builder->createStore(lengthVal, capField);

			// Field 1: iterIndex
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, varDecl->name + ".iterIndex");
			builder->createStore(builder->getInt32(0), iterField);

			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
			arrayPtr = bufferPtr;

			// Track array struct for cleanup (dynamic - buffer can be reallocated)
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, structAlloca, elementTypeName, true});
			}
		} else {
			// Small array: stack-allocate directly
			mir::MIRType *mirArrayType = mir::MIRType::getArray(elementType, arraySize);
			mir::MIRValue *stackBuffer = builder->createAlloca(mirArrayType, varDecl->name + ".stack_buffer");

			// Create array<T> struct alloca (2 fields: { managed, iterIndex })
			mir::MIRType *arrayStructType = getOrCreateArrayStructType(elementTypeName);
			mir::MIRValue *structAlloca = builder->createAlloca(arrayStructType, varDecl->name);

			// Get __ManagedArrayData type for the nested managed field
			mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elementTypeName);

			// Initialize nested struct layout: { managed: { _buffer, _len, _capacity }, iterIndex }
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);

			// Field 0: managed (nested __ManagedArrayData struct)
			mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, structAlloca, 0, varDecl->name + ".managed");

			// Initialize the nested __ManagedArrayData fields
			mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedField, 0, varDecl->name + ".managed._buffer");
			builder->createStore(stackBuffer, bufferField);

			mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedField, 1, varDecl->name + ".managed._len");
			builder->createStore(arraySizeVal, lenField);

			// capacity = 0 signals stack-allocated buffer to push() intrinsic
			mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedField, 2, varDecl->name + ".managed._capacity");
			builder->createStore(builder->getInt32(0), capField);

			// Field 1: iterIndex
			mir::MIRValue *iterField = builder->createStructGEP(arrayStructType, structAlloca, 1, varDecl->name + ".iterIndex");
			builder->createStore(builder->getInt32(0), iterField);

			namedValues[varDecl->name] = structAlloca;
			variableTypes[varDecl->name] = maxon::TypeConversion::makeArrayStructType(elementTypeName);
			arrayPtr = stackBuffer;

			// Track array struct for cleanup (dynamic - buffer could be promoted to heap by push())
			if (!scopeStack.empty()) {
				scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, structAlloca, elementTypeName, true});
			}
		}

		// Zero-initialize using memset
		if (arraySize > 0) {
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			mir::MIRValue *memsetSizeVal = builder->getInt64(totalSize);
			builder->createCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
		}

		return;
	}

	// Handle map literal initialization: map from K to V
	if (auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(varDecl->initializer.get())) {
		const std::string &keyType = mapLiteral->keyType;
		const std::string &valueType = mapLiteral->valueType;

		// Build the specialized type name
		std::string specializedName = "map<" + keyType + "," + valueType + ">";

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			// Check if the map template struct exists
			if (structDefinitions.find("map") != structDefinitions.end()) {
				std::map<std::string, std::string> typeBindings = {
					{"KeyType", keyType},
					{"ValueType", valueType}};
				instantiateGenericStruct("map", typeBindings);
			} else {
				reportError("Generic struct 'map' not found - ensure stdlib/collections/map.maxon is compiled",
							varDecl->line, varDecl->column);
			}
		}

		// Get the specialized struct type
		mir::MIRType *mapStructType = structTypes[specializedName];
		if (!mapStructType) {
			reportError("Failed to instantiate map type: " + specializedName,
						varDecl->line, varDecl->column);
		}

		// Map is implemented as a map struct with arrays for keys, values, and states
		// Initial capacity is 16 buckets
		const int initialCapacity = 16;

		// Get MIR types for key and value
		mir::MIRType *keyMirType = getTypeFromString(keyType);
		mir::MIRType *valueMirType = getTypeFromString(valueType);

		// Mark struct type as used
		mapStructType->used = true;

		// Allocate the map struct on the stack
		mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, varDecl->name);
		namedValues[varDecl->name] = mapAlloca;
		variableTypes[varDecl->name] = specializedName;

		// Initialize the map with default capacity
		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");

		// Allocate keys array with header: [length:i32][capacity:i32][...data...]
		uint64_t keySize = keyMirType->sizeInBytes;
		mir::MIRValue *keysDataSize = builder->getInt64(initialCapacity * keySize + 8);
		mir::MIRValue *keysBase = builder->createCall(mallocFunc, {keysDataSize}, varDecl->name + ".keys.base");

		// Store length and capacity in keys header
		mir::MIRValue *keysLenPtr = keysBase;
		builder->createStore(builder->getInt32(initialCapacity), keysLenPtr);
		mir::MIRValue *keysCapPtr = builder->createGEP(mir::MIRType::getInt8(), keysBase, {builder->getInt64(4)}, "keys.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), keysCapPtr);

		// Data pointer for keys
		mir::MIRValue *keysData = builder->createGEP(mir::MIRType::getInt8(), keysBase, {builder->getInt64(8)}, varDecl->name + ".keys.data");

		// Zero-initialize keys
		mir::MIRFunction *memsetFunc = module->getFunction("memset");
		builder->createCall(memsetFunc, {keysData, builder->getInt32(0), builder->getInt64(initialCapacity * keySize)});

		// Allocate values array with header
		uint64_t valueSize = valueMirType->sizeInBytes;
		mir::MIRValue *valuesDataSize = builder->getInt64(initialCapacity * valueSize + 8);
		mir::MIRValue *valuesBase = builder->createCall(mallocFunc, {valuesDataSize}, varDecl->name + ".values.base");

		// Store length and capacity in values header
		builder->createStore(builder->getInt32(initialCapacity), valuesBase);
		mir::MIRValue *valuesCapPtr = builder->createGEP(mir::MIRType::getInt8(), valuesBase, {builder->getInt64(4)}, "values.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), valuesCapPtr);

		// Data pointer for values
		mir::MIRValue *valuesData = builder->createGEP(mir::MIRType::getInt8(), valuesBase, {builder->getInt64(8)}, varDecl->name + ".values.data");

		// Zero-initialize values
		builder->createCall(memsetFunc, {valuesData, builder->getInt32(0), builder->getInt64(initialCapacity * valueSize)});

		// Allocate states array with header (byte array)
		mir::MIRValue *statesDataSize = builder->getInt64(initialCapacity + 8);
		mir::MIRValue *statesBase = builder->createCall(mallocFunc, {statesDataSize}, varDecl->name + ".states.base");

		// Store length and capacity in states header
		builder->createStore(builder->getInt32(initialCapacity), statesBase);
		mir::MIRValue *statesCapPtr = builder->createGEP(mir::MIRType::getInt8(), statesBase, {builder->getInt64(4)}, "states.cap.ptr");
		builder->createStore(builder->getInt32(initialCapacity), statesCapPtr);

		// Data pointer for states
		mir::MIRValue *statesData = builder->createGEP(mir::MIRType::getInt8(), statesBase, {builder->getInt64(8)}, varDecl->name + ".states.data");

		// Zero-initialize states (all EMPTY = 0)
		builder->createCall(memsetFunc, {statesData, builder->getInt32(0), builder->getInt64(initialCapacity)});

		// Store pointers in the HashMap struct
		mir::MIRValue *keysFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 0, "_keys");
		builder->createStore(keysData, keysFieldPtr);

		mir::MIRValue *valuesFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 1, "_values");
		builder->createStore(valuesData, valuesFieldPtr);

		mir::MIRValue *statesFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 2, "_states");
		builder->createStore(statesData, statesFieldPtr);

		mir::MIRValue *countFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 3, "_count");
		builder->createStore(builder->getInt32(0), countFieldPtr);

		mir::MIRValue *capacityFieldPtr = builder->createStructGEP(mapStructType, mapAlloca, 4, "_capacity");
		builder->createStore(builder->getInt32(initialCapacity), capacityFieldPtr);

		// Track base pointers for cleanup (static allocations - not reallocated)
		if (!scopeStack.empty()) {
			mir::MIRValue *keysBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__keys_base");
			builder->createStore(keysBase, keysBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._keys", keysBaseAlloca, "", false});

			mir::MIRValue *valuesBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__values_base");
			builder->createStore(valuesBase, valuesBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._values", valuesBaseAlloca, "", false});

			mir::MIRValue *statesBaseAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__states_base");
			builder->createStore(statesBase, statesBaseAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._states", statesBaseAlloca, "", false});
		}

		return;
	}

	// Handle set from array initialization: set from [1, 2, 3]
	if (auto *setFromExpr = dynamic_cast<SetFromExprAST *>(varDecl->initializer.get())) {
		const std::string &elemType = setFromExpr->inferredElementType;

		// Build the specialized type name
		std::string specializedName = "set<" + elemType + ">";
		logDetail("Processing set from expression: " + specializedName);

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			logDetail("  Need to instantiate generic struct");
			if (structDefinitions.find("set") != structDefinitions.end()) {
				logDetail("  Found 'set' in structDefinitions");
				std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
				instantiateGenericStruct("set", typeBindings);
			} else {
				logDetail("  ERROR: 'set' NOT found in structDefinitions");
				logDetail("  Available struct definitions:");
				for (const auto &pair : structDefinitions) {
					logDetail("    - " + pair.first);
				}
				reportError("Generic struct 'set' not found - ensure stdlib/collections/set.maxon is compiled",
							varDecl->line, varDecl->column);
			}
		}

		// Get the specialized struct type
		mir::MIRType *setStructType = structTypes[specializedName];
		if (!setStructType) {
			reportError("Failed to instantiate set type: " + specializedName,
						varDecl->line, varDecl->column);
		}

		// Mark struct type as used
		setStructType->used = true;

		// Allocate the set struct on the stack
		mir::MIRValue *setAlloca = builder->createAlloca(setStructType, varDecl->name);
		namedValues[varDecl->name] = setAlloca;
		variableTypes[varDecl->name] = specializedName;

		// Get the array literal to extract elements
		auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(setFromExpr->arrayExpr.get());
		if (!arrayLiteral) {
			reportError("set from expression requires an array literal",
						varDecl->line, varDecl->column);
			return;
		}

		// Calculate initial capacity - at least 16, or next power of 2 above element count * 2
		int numElements = static_cast<int>(arrayLiteral->values.size());
		int initialCapacity = 16;
		while (initialCapacity < numElements * 2) {
			initialCapacity *= 2;
		}

		// Get MIR type for element
		mir::MIRType *elemMirType = getTypeFromString(elemType);
		uint64_t elemSize = elemMirType->sizeInBytes;

		initHeapManagement();
		mir::MIRFunction *mallocFunc = module->getFunction("malloc");
		mir::MIRFunction *memsetFunc = module->getFunction("memset");

		// Allocate buffer for elements array (no header needed for array<T> struct)
		mir::MIRValue *elemsBufferSize = builder->getInt64(initialCapacity * elemSize);
		mir::MIRValue *elemsBuffer = builder->createCall(mallocFunc, {elemsBufferSize}, varDecl->name + ".elements.buffer");

		// Zero-initialize elements buffer
		builder->createCall(memsetFunc, {elemsBuffer, builder->getInt32(0), elemsBufferSize});

		// Allocate buffer for states array (byte array)
		mir::MIRValue *statesBufferSize = builder->getInt64(initialCapacity);
		mir::MIRValue *statesBuffer = builder->createCall(mallocFunc, {statesBufferSize}, varDecl->name + ".states.buffer");

		// Zero-initialize states (all EMPTY = 0)
		builder->createCall(memsetFunc, {statesBuffer, builder->getInt32(0), statesBufferSize});

		// Get array<T> struct type for elements: { managed: { _buffer, _len, _capacity }, iterIndex }
		mir::MIRType *elemArrayStructType = getOrCreateArrayStructType(elemType);
		mir::MIRType *byteArrayStructType = getOrCreateArrayStructType("byte");
		// Get __ManagedArrayData types for nested fields
		mir::MIRType *elemManagedType = getOrCreateManagedArrayDataType(elemType);
		mir::MIRType *byteManagedType = getOrCreateManagedArrayDataType("byte");

		// Initialize elements array struct in field 0 of set
		mir::MIRValue *elemsFieldPtr = builder->createStructGEP(setStructType, setAlloca, 0, "elements.field");
		// Field 0 of array<T>: managed (nested __ManagedArrayData struct)
		mir::MIRValue *elemsManagedField = builder->createStructGEP(elemArrayStructType, elemsFieldPtr, 0, "elements.managed");
		// Initialize the nested __ManagedArrayData fields
		mir::MIRValue *elemsBufferField = builder->createStructGEP(elemManagedType, elemsManagedField, 0, "elements.managed._buffer");
		builder->createStore(elemsBuffer, elemsBufferField);
		mir::MIRValue *elemsLenField = builder->createStructGEP(elemManagedType, elemsManagedField, 1, "elements.managed._len");
		builder->createStore(builder->getInt32(initialCapacity), elemsLenField);
		mir::MIRValue *elemsCapField = builder->createStructGEP(elemManagedType, elemsManagedField, 2, "elements.managed._capacity");
		builder->createStore(builder->getInt32(initialCapacity), elemsCapField);
		// Field 1 of array<T>: iterIndex
		mir::MIRValue *elemsIterField = builder->createStructGEP(elemArrayStructType, elemsFieldPtr, 1, "elements.iterIndex");
		builder->createStore(builder->getInt32(0), elemsIterField);

		// Initialize states array struct in field 1 of set
		mir::MIRValue *statesFieldPtr = builder->createStructGEP(setStructType, setAlloca, 1, "states.field");
		// Field 0 of array<byte>: managed (nested __ManagedArrayData struct)
		mir::MIRValue *statesManagedField = builder->createStructGEP(byteArrayStructType, statesFieldPtr, 0, "states.managed");
		// Initialize the nested __ManagedArrayData fields
		mir::MIRValue *statesBufferField = builder->createStructGEP(byteManagedType, statesManagedField, 0, "states.managed._buffer");
		builder->createStore(statesBuffer, statesBufferField);
		mir::MIRValue *statesLenField = builder->createStructGEP(byteManagedType, statesManagedField, 1, "states.managed._len");
		builder->createStore(builder->getInt32(initialCapacity), statesLenField);
		mir::MIRValue *statesCapField = builder->createStructGEP(byteManagedType, statesManagedField, 2, "states.managed._capacity");
		builder->createStore(builder->getInt32(initialCapacity), statesCapField);
		// Field 1 of array<byte>: iterIndex
		mir::MIRValue *statesIterField = builder->createStructGEP(byteArrayStructType, statesFieldPtr, 1, "states.iterIndex");
		builder->createStore(builder->getInt32(0), statesIterField);

		// Initialize count and capacity fields
		mir::MIRValue *countFieldPtr = builder->createStructGEP(setStructType, setAlloca, 2, "_count");
		builder->createStore(builder->getInt32(0), countFieldPtr);

		mir::MIRValue *capacityFieldPtr = builder->createStructGEP(setStructType, setAlloca, 3, "_capacity");
		builder->createStore(builder->getInt32(initialCapacity), capacityFieldPtr);

		// Insert each element from the array literal
		std::string insertMethodName = specializedName + ".insert";
		mir::MIRFunction *insertFunc = module->getFunction(insertMethodName);

		if (!insertFunc) {
			reportError("set.insert method not found for type: " + specializedName,
						varDecl->line, varDecl->column);
			return;
		}

		for (auto &valExpr : arrayLiteral->values) {
			mir::MIRValue *elemValue = generateExpr(valExpr.get());
			if (!elemValue) {
				reportError("Failed to generate set element value",
							varDecl->line, varDecl->column);
				continue;
			}
			builder->createCall(insertFunc, {setAlloca, elemValue});
		}

		// Track buffer pointers for cleanup (static allocations - not reallocated)
		if (!scopeStack.empty()) {
			mir::MIRValue *elemsBufferAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__elements_buffer");
			builder->createStore(elemsBuffer, elemsBufferAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._elements", elemsBufferAlloca, "", false});

			mir::MIRValue *statesBufferAlloca = builder->createAlloca(mir::MIRType::getPtr(), varDecl->name + ".__states_buffer");
			builder->createStore(statesBuffer, statesBufferAlloca);
			scopeStack.back().heapAllocatedArrays.push_back({varDecl->name + "._states", statesBufferAlloca, "", false});
		}

		return;
	}

	// Handle struct initialization
	if (auto *structInitExpr = dynamic_cast<StructInitExprAST *>(varDecl->initializer.get())) {
		// Substitute struct name if we're inside a generic method body
		// E.g., "set" -> "set<int>" when generating set<int>.init
		// Also handle "Self" -> current receiver type
		std::string structName = structInitExpr->structName;
		if (!currentReceiverType.empty()) {
			// Handle "Self" type - substitute with current receiver type
			if (structName == "Self") {
				structName = currentReceiverType;
			} else if (!currentTypeBindings.empty()) {
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
		}

		mir::MIRType *structType = structTypes[structName];
		if (!structType) {
			reportError("Unknown struct type: " + structName,
						structInitExpr->line, structInitExpr->column);
		}
		// Mark struct type as used for lazy type emission
		structType->used = true;
		markFieldTypesUsed(structType);

		mir::MIRValue *structAlloca = builder->createAlloca(structType, varDecl->name);
		namedValues[varDecl->name] = structAlloca;
		variableTypes[varDecl->name] = structName;

		// Initialize fields - iterate over all struct fields and use provided value or default
		const auto &fields = structFields[structName];
		const auto &defaults = structFieldDefaults[structName];

		// Build a map of provided field values for quick lookup
		std::map<std::string, const StructInitField *> providedFields;
		for (const auto &initField : structInitExpr->fields) {
			providedFields[initField.name] = &initField;
		}

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
				// Use default value
				auto defaultIt = defaults.find(fieldName);
				if (defaultIt != defaults.end()) {
					valueExpr = defaultIt->second;
				}
			}

			if (valueExpr == nullptr) {
				reportError("No value for field '" + fieldName +
								"' in struct '" + structInitExpr->structName + "'",
							structInitExpr->line, structInitExpr->column);
			}

			// Handle array field initialization
			if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(valueExpr)) {
				// Array literal for struct field - initialize in place
				if (!arrayLit->values.empty()) {
					// Value-initialized array [1, 2, 3]
					for (size_t i = 0; i < arrayLit->values.size(); i++) {
						mir::MIRValue *elemValue = generateExpr(arrayLit->values[i].get());
						mir::MIRValue *elemPtr = builder->createArrayGEP(elemValue->type, fieldPtr,
																		 mir::MIRValue::createConstantInt(mir::MIRType::getInt32(), i),
																		 fieldName + ".elem");
						builder->createStore(elemValue, elemPtr);
					}
				} else {
					// Zero-initialized array [16]byte - already zero from alloca
					// Nothing extra needed
				}
			} else if (maxon::TypeConversion::isManagedArrayType(fieldTypeStr)) {
				// Slice field ([]T) - need to create fat pointer {ptr, len}
				// Check if the value is a variable reference to a variable-sized array
				std::string elementTypeStr = maxon::TypeConversion::getArrayElementType(fieldTypeStr);
				std::string unsizedArrayTypeName = "_ManagedArray_" + elementTypeStr;

				// Get or create the unsized array struct type
				mir::MIRType *unsizedArrayType = structTypes[unsizedArrayTypeName];
				if (!unsizedArrayType) {
					unsizedArrayType = module->getOrCreateStructType(
						unsizedArrayTypeName,
						{mir::MIRType::getPtr(), mir::MIRType::getInt32()});
					structTypes[unsizedArrayTypeName] = unsizedArrayType;
				}

				auto *varExpr = dynamic_cast<VariableExprAST *>(valueExpr);
				if (varExpr) {
					std::string varType = variableTypes[varExpr->name];
					// Check if this is a variable-sized array (type starts with [])
					if (maxon::TypeConversion::isManagedArrayType(varType)) {
						// Variable-sized array - load data pointer and length from header
						mir::MIRValue *arrayAlloca = namedValues[varExpr->name];
						if (arrayAlloca) {
							// Load the data pointer
							mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");

							// Load length from header at dataPtr - 8
							mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
																		  {builder->getInt64(-8)}, "length.ptr");
							mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthPtr, "length");

							// Store into the fat pointer field
							mir::MIRValue *dataPtrField = builder->createStructGEP(unsizedArrayType, fieldPtr, 0, fieldName + ".ptr");
							builder->createStore(dataPtr, dataPtrField);
							mir::MIRValue *lenField = builder->createStructGEP(unsizedArrayType, fieldPtr, 1, fieldName + ".len");
							builder->createStore(length, lenField);
						} else {
							reportError("Unknown variable in struct field init: " + varExpr->name,
										varExpr->line, varExpr->column);
						}
					} else {
						// Not a variable-sized array - use regular field value
						mir::MIRValue *fieldValue = generateExpr(valueExpr);
						builder->createStore(fieldValue, fieldPtr);
					}
				} else {
					// Not a variable reference - use regular field value
					mir::MIRValue *fieldValue = generateExpr(valueExpr);
					builder->createStore(fieldValue, fieldPtr);
				}
			} else {
				// Regular field value
				mir::MIRValue *fieldValue = generateExpr(valueExpr);

				// Get the MIR type for this field
				mir::MIRType *fieldType = getTypeFromString(fieldTypeStr);

				// Handle optional field wrapping
				// If field type is optional and value is nil or unwrapped, wrap it
				if (maxon::TypeConversion::isOptionalType(fieldTypeStr)) {
					// Get the value type
					std::string valueTypeStr = getExpressionMaxonType(valueExpr);

					// Check if value is nil (generateExpr returns nullptr for nil)
					bool isNilValue = (!fieldValue && dynamic_cast<NilExprAST *>(valueExpr));

					if (isNilValue) {
						// Create nil optional
						fieldValue = createNilOptional(fieldType);
					} else if (fieldValue && !maxon::TypeConversion::isOptionalType(valueTypeStr)) {
						// Value is unwrapped type - wrap in Some optional
						fieldValue = createSomeOptional(fieldType, fieldValue);
					}
					// else: value is already optional, use as-is
				}

				// Check if this is a struct field (char, string, etc.)
				// For struct types, generateExpr returns a pointer to an alloca
				// We need to load the struct value and store it (not store the pointer)
				if (fieldValue && fieldType->isStruct() && fieldValue->type == mir::MIRType::getPtr()) {
					// Load the struct value from the alloca
					mir::MIRValue *loadedValue = builder->createLoad(fieldType, fieldValue, fieldName + ".load");
					builder->createStore(loadedValue, fieldPtr);
				} else if (fieldValue) {
					builder->createStore(fieldValue, fieldPtr);
				} else {
					reportError("Failed to generate value for field '" + fieldName + "'",
								structInitExpr->line, structInitExpr->column);
				}
			}
		}

		return;
	}

	// Handle string literal initialization
	// generateStringLiteral creates an alloca, initializes it, and returns the pointer
	// We use that alloca directly (don't create a second one)
	if (auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(varDecl->initializer.get())) {
		mir::MIRValue *stringAlloca = generateStringLiteral(strLiteral);

		// Use the alloca from generateStringLiteral directly
		// Rename it to match the variable name
		stringAlloca->name = varDecl->name;

		namedValues[varDecl->name] = stringAlloca;
		variableTypes[varDecl->name] = "string";
		return;
	}

	// Handle char literal initialization
	// generateCharLiteral creates an alloca, initializes it, and returns the pointer
	// We use that alloca directly (don't create a second one)
	if (auto *charLiteral = dynamic_cast<CharacterExprAST *>(varDecl->initializer.get())) {
		mir::MIRValue *charAlloca = generateCharLiteral(charLiteral);

		// Use the alloca from generateCharLiteral directly
		// Rename it to match the variable name
		charAlloca->name = varDecl->name;

		namedValues[varDecl->name] = charAlloca;
		variableTypes[varDecl->name] = "character";
		return;
	}

	// Handle string concatenation (string + string produces a string struct)
	// The expression returns an alloca pointer - use it directly like string literals
	// This also handles chained concatenation like a + b + c
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(varDecl->initializer.get())) {
		if (isStringConcatExpr(binExpr)) {
			mir::MIRValue *stringAlloca = generateExpr(varDecl->initializer.get());

			// Use the alloca from concat directly
			stringAlloca->name = varDecl->name;

			namedValues[varDecl->name] = stringAlloca;
			variableTypes[varDecl->name] = "string";
			return;
		}
	}

	// Non-array, non-struct variable
	mir::MIRValue *initVal = nullptr;
	if (varDecl->initializer) {
		initVal = generateExpr(varDecl->initializer.get());
		if (!initVal) {
			reportError("Failed to generate variable initializer for '" + varDecl->name + "'",
						varDecl->line, varDecl->column);
		}
	}

	// Determine type
	mir::MIRType *allocaType;
	if (!varDecl->type.empty()) {
		allocaType = getTypeFromString(varDecl->type);
	} else if (initVal) {
		allocaType = initVal->type;
	} else {
		allocaType = mir::MIRType::getInt32();
	}

	mir::MIRValue *alloca = builder->createAlloca(allocaType, varDecl->name);

	if (initVal) {
		builder->createStore(initVal, alloca);
	} else {
		// Zero-initialize
		mir::MIRValue *zeroVal;
		if (allocaType->isInteger()) {
			zeroVal = builder->getInt32(0);
		} else if (allocaType->isFloat()) {
			zeroVal = builder->getFloat64(0.0);
		} else {
			zeroVal = builder->getNull();
		}
		builder->createStore(zeroVal, alloca);
	}

	namedValues[varDecl->name] = alloca;
	// If type was explicitly specified, use it; otherwise derive from the alloca type
	if (!varDecl->type.empty()) {
		variableTypes[varDecl->name] = varDecl->type;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		variableTypes[varDecl->name] = allocaType->structName;
	} else {
		// Derive type from the allocated MIR type
		variableTypes[varDecl->name] = getMaxonTypeFromMIRType(allocaType);
	}

	// Track substring variables for cleanup at scope exit
	// Substrings may hold a reference to a heap-allocated parent's buffer
	std::string finalType = variableTypes[varDecl->name];
	if (finalType == "substring" && !scopeStack.empty()) {
		scopeStack.back().substringAllocas.push_back({varDecl->name, alloca});
	}
	// Track cstring variables for cleanup at scope exit
	// Cstrings hold a reference to the underlying managed string
	if (finalType == "cstring" && !scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({varDecl->name, alloca});
	}
}

void MIRCodeGenerator::generateLetDecl(LetDeclStmtAST *letDecl, mir::MIRFunction *function) {
	// Handle array literal initialization: [val1, val2, ...]
	if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(letDecl->initializer.get())) {
		mir::MIRType *elementType;
		std::string elementTypeName;
		std::vector<mir::MIRValue *> initValues;
		int constantArraySize = static_cast<int>(arrayLiteral->values.size());

		for (auto &valExpr : arrayLiteral->values) {
			mir::MIRValue *val = generateExpr(valExpr.get());
			if (!val) {
				reportError("Failed to generate array element value",
							letDecl->line, letDecl->column);
			}
			initValues.push_back(val);
		}

		// Infer element type from first value
		elementType = initValues[0]->type;
		if (elementType->kind == mir::MIRTypeKind::Int32) {
			elementTypeName = "int";
		} else if (elementType->kind == mir::MIRTypeKind::Float64) {
			elementTypeName = "float";
		} else if (elementType->kind == mir::MIRTypeKind::Int8) {
			elementTypeName = "character";
		} else if (elementType->kind == mir::MIRTypeKind::Ptr) {
			elementTypeName = "ptr";
		} else {
			reportError("Unsupported array element type",
						letDecl->line, letDecl->column);
		}

		uint64_t elementSize = elementType->sizeInBytes;
		mir::MIRValue *arrayPtr;
		uint64_t totalSize = constantArraySize * elementSize;

		// Threshold for stack allocation (4KB)
		constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
		bool useHeap = totalSize > STACK_ARRAY_THRESHOLD;

		if (useHeap) {
			// Large array: heap-allocate with header
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");
			mir::MIRValue *sizeVal = builder->getInt64(8 + totalSize);
			mir::MIRValue *basePtr = builder->createCall(mallocFunc, {sizeVal}, letDecl->name + ".base");

			// Store length at offset 0 (basePtr already points here)
			mir::MIRValue *lengthVal = builder->getInt32(constantArraySize);
			builder->createStore(lengthVal, basePtr);

			// Store capacity at offset 4
			mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
													   {builder->getInt64(4)}, "cap.ptr");
			builder->createStore(lengthVal, capPtr);

			// Data pointer is at offset 8
			arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
										  {builder->getInt64(8)}, letDecl->name + ".data");

			// Create alloca to store the data pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);
			namedValues[letDecl->name] = ptrAlloca;

			// Track base pointer for cleanup (static - let arrays can't be reallocated)
			if (!scopeStack.empty()) {
				mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name + ".__base");
				builder->createStore(basePtr, basePtrAlloca);
				scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, basePtrAlloca, "", false});
			}
		} else {
			// Small array: stack-allocate directly
			mir::MIRType *arrayType = mir::MIRType::getArray(elementType, constantArraySize);
			arrayPtr = builder->createAlloca(arrayType, letDecl->name);
			namedValues[letDecl->name] = arrayPtr;
			stackAllocatedArrays.insert(letDecl->name);

			// Store length in hidden alloca for .length access
			mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), letDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(constantArraySize);
			builder->createStore(arraySizeVal, sizeAlloca);
			namedValues[letDecl->name + ".__length"] = sizeAlloca;
		}

		// Initialize array elements
		for (int i = 0; i < constantArraySize; i++) {
			mir::MIRValue *indexVal = builder->getInt32(i);
			mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, arrayPtr, indexVal, "arrayidx");
			builder->createStore(initValues[i], elementPtr);
		}

		variableTypes[letDecl->name] = maxon::TypeConversion::makeStaticArrayType(constantArraySize, elementTypeName);

		return;
	}

	// Handle sized array expression: array of N T
	if (auto *sizedArray = dynamic_cast<SizedArrayExprAST *>(letDecl->initializer.get())) {
		std::string elementTypeName = sizedArray->elementType;
		mir::MIRType *elementType = getTypeFromString(elementTypeName);
		uint64_t elementSize = elementType->sizeInBytes;

		// Handle variable-sized arrays (size is an expression)
		if (sizedArray->hasVariableSize()) {
			// Generate the size expression
			mir::MIRValue *sizeVal = generateExpr(sizedArray->sizeExpr.get());
			if (!sizeVal) {
				reportError("Failed to generate array size expression", letDecl->line, letDecl->column);
			}

			// Variable-sized arrays always use heap allocation
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// Calculate total size for header + data: 8 bytes header + (size * elementSize)
			mir::MIRValue *elemSizeVal = builder->getInt64(elementSize);
			mir::MIRValue *sizeExt = builder->createSExt(sizeVal, mir::MIRType::getInt64(), "size.ext");
			mir::MIRValue *dataSize = builder->createMul(sizeExt, elemSizeVal, "data.size");
			mir::MIRValue *headerSize = builder->getInt64(8);
			mir::MIRValue *totalSize = builder->createAdd(headerSize, dataSize, "total.size");

			// Allocate buffer with header
			mir::MIRValue *basePtr = builder->createCall(mallocFunc, {totalSize}, letDecl->name + ".base");

			// Store length at offset 0
			builder->createStore(sizeVal, basePtr);

			// Store capacity at offset 4
			mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
													   {builder->getInt64(4)}, "cap.ptr");
			builder->createStore(sizeVal, capPtr);

			// Data pointer is at offset 8
			mir::MIRValue *arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
														 {builder->getInt64(8)}, letDecl->name + ".data");

			// Create alloca to store the data pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);
			namedValues[letDecl->name] = ptrAlloca;

			// Zero-initialize using memset
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			builder->createCall(memsetFunc, {arrayPtr, zeroVal, dataSize});

			// Track base pointer for cleanup (static - let arrays can't be reallocated)
			if (!scopeStack.empty()) {
				mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name + ".__base");
				builder->createStore(basePtr, basePtrAlloca);
				scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, basePtrAlloca, "", false});
			}

			variableTypes[letDecl->name] = maxon::TypeConversion::makeManagedArrayType(elementTypeName);
			return;
		}

		// Constant-sized array
		int arraySize = sizedArray->size;
		mir::MIRValue *arrayPtr;
		uint64_t totalSize = arraySize * elementSize;

		// Threshold for stack allocation (4KB)
		constexpr uint64_t STACK_ARRAY_THRESHOLD = 4096;
		bool useHeap = totalSize > STACK_ARRAY_THRESHOLD || arraySize == 0;

		if (useHeap || arraySize == 0) {
			// Large/empty array: heap-allocate with header
			initHeapManagement();
			mir::MIRFunction *mallocFunc = module->getFunction("malloc");

			// For empty arrays, allocate minimal buffer
			uint64_t allocSize = (arraySize > 0) ? (8 + totalSize) : 8;
			mir::MIRValue *sizeVal = builder->getInt64(allocSize);
			mir::MIRValue *basePtr = builder->createCall(mallocFunc, {sizeVal}, letDecl->name + ".base");

			// Store length at offset 0 (basePtr already points here)
			mir::MIRValue *lengthVal = builder->getInt32(arraySize);
			builder->createStore(lengthVal, basePtr);

			// Store capacity at offset 4
			mir::MIRValue *capPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
													   {builder->getInt64(4)}, "cap.ptr");
			builder->createStore(lengthVal, capPtr);

			// Data pointer is at offset 8
			arrayPtr = builder->createGEP(mir::MIRType::getInt8(), basePtr,
										  {builder->getInt64(8)}, letDecl->name + ".data");

			// Create alloca to store the data pointer
			mir::MIRValue *ptrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name);
			builder->createStore(arrayPtr, ptrAlloca);
			namedValues[letDecl->name] = ptrAlloca;

			// Track base pointer for cleanup (static - let arrays can't be reallocated)
			if (!scopeStack.empty()) {
				mir::MIRValue *basePtrAlloca = builder->createAlloca(mir::MIRType::getPtr(), letDecl->name + ".__base");
				builder->createStore(basePtr, basePtrAlloca);
				scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, basePtrAlloca, "", false});
			}
		} else {
			// Small array: stack-allocate directly
			mir::MIRType *arrayType = mir::MIRType::getArray(elementType, arraySize);
			arrayPtr = builder->createAlloca(arrayType, letDecl->name);
			namedValues[letDecl->name] = arrayPtr;
			stackAllocatedArrays.insert(letDecl->name);

			// Store length in hidden alloca for .length access
			mir::MIRValue *sizeAlloca = builder->createAlloca(mir::MIRType::getInt32(), letDecl->name + ".__length");
			mir::MIRValue *arraySizeVal = builder->getInt32(arraySize);
			builder->createStore(arraySizeVal, sizeAlloca);
			namedValues[letDecl->name + ".__length"] = sizeAlloca;
		}

		// Zero-initialize using memset
		if (arraySize > 0) {
			mir::MIRFunction *memsetFunc = module->getFunction("memset");
			if (!memsetFunc) {
				initHeapManagement();
				memsetFunc = module->getFunction("memset");
			}
			mir::MIRValue *zeroVal = builder->getInt32(0);
			mir::MIRValue *memsetSizeVal = builder->getInt64(totalSize);
			builder->createCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
		}

		variableTypes[letDecl->name] = maxon::TypeConversion::makeStaticArrayType(arraySize, elementTypeName);

		return;
	}

	// Handle string literal initialization
	if (auto *strLiteral = dynamic_cast<StringLiteralExprAST *>(letDecl->initializer.get())) {
		mir::MIRValue *stringPtr = generateStringLiteral(strLiteral);
		namedValues[letDecl->name] = stringPtr;
		variableTypes[letDecl->name] = "string";
		return;
	}

	// Handle string concatenation (string + string produces a string struct)
	// The expression returns an alloca pointer - use it directly like string literals
	// This also handles chained concatenation like a + b + c
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(letDecl->initializer.get())) {
		if (isStringConcatExpr(binExpr)) {
			mir::MIRValue *stringAlloca = generateExpr(letDecl->initializer.get());

			// Use the alloca from concat directly
			stringAlloca->name = letDecl->name;

			namedValues[letDecl->name] = stringAlloca;
			variableTypes[letDecl->name] = "string";
			return;
		}
	}

	// Non-array variable
	mir::MIRValue *initVal = nullptr;
	if (letDecl->initializer) {
		initVal = generateExpr(letDecl->initializer.get());
		if (!initVal) {
			reportError("Failed to generate let initializer",
						letDecl->line, letDecl->column);
		}
	}

	mir::MIRType *allocaType;
	if (!letDecl->type.empty()) {
		allocaType = getTypeFromString(letDecl->type);
	} else if (initVal) {
		allocaType = initVal->type;
	} else {
		allocaType = mir::MIRType::getInt32();
	}

	mir::MIRValue *alloca = builder->createAlloca(allocaType, letDecl->name);

	if (initVal) {
		builder->createStore(initVal, alloca);
	} else {
		mir::MIRValue *zeroVal;
		if (allocaType->isInteger()) {
			zeroVal = builder->getInt32(0);
		} else if (allocaType->isFloat()) {
			zeroVal = builder->getFloat64(0.0);
		} else {
			zeroVal = builder->getNull();
		}
		builder->createStore(zeroVal, alloca);
	}

	namedValues[letDecl->name] = alloca;
	// If type was explicitly specified, use it; otherwise derive from the alloca type
	if (!letDecl->type.empty()) {
		variableTypes[letDecl->name] = letDecl->type;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		variableTypes[letDecl->name] = allocaType->structName;
	} else {
		// Derive type from the allocated MIR type
		variableTypes[letDecl->name] = getMaxonTypeFromMIRType(allocaType);
	}

	// Track substring variables for cleanup at scope exit
	// Substrings may hold a reference to a heap-allocated parent's buffer
	std::string finalType = variableTypes[letDecl->name];
	if (finalType == "substring" && !scopeStack.empty()) {
		scopeStack.back().substringAllocas.push_back({letDecl->name, alloca});
	}
	// Track cstring variables for cleanup at scope exit
	// Cstrings hold a reference to the underlying managed string
	if (finalType == "cstring" && !scopeStack.empty()) {
		scopeStack.back().cstringAllocas.push_back({letDecl->name, alloca});
	}
}
