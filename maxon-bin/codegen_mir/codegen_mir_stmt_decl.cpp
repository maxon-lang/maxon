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

	// Handle empty map literal: map from K to V
	// Uses ExpressibleByMapLiteral interface pattern - creates empty _ManagedArrays and calls init()
	if (auto *mapLiteral = dynamic_cast<MapLiteralExprAST *>(varDecl->initializer.get())) {
		const std::string &keyType = mapLiteral->keyType;
		const std::string &valueType = mapLiteral->valueType;

		// Build the specialized type name
		std::string specializedName = "map<" + keyType + "," + valueType + ">";
		logDetail("Processing empty map literal: " + specializedName);

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			logDetail("  Need to instantiate generic struct");
			if (structDefinitions.find("map") != structDefinitions.end()) {
				logDetail("  Found 'map' in structDefinitions");
				std::map<std::string, std::string> typeBindings = {
					{"Key", keyType},
					{"Value", valueType}};
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

		// Mark struct type as used
		mapStructType->used = true;

		// Get element types
		mir::MIRType *keyMirType = getTypeFromString(keyType);
		mir::MIRType *valueMirType = getTypeFromString(valueType);

		// Create empty _ManagedArray<Key> struct for keys
		mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
		mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

		// Create empty _ManagedArray<Value> struct for values
		mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
		mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

		// Create minimal stack buffers (need at least 1 element for type correctness)
		mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, 1);
		mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

		mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, 1);
		mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

		// Initialize empty keys _ManagedArray struct fields
		mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
		builder->createStore(keysStackBuffer, keysBufferField);
		mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
		builder->createStore(builder->getInt32(0), keysLenField); // 0 elements
		mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
		builder->createStore(builder->getInt32(0), keysCapField); // capacity=0 means stack data

		// Initialize empty values _ManagedArray struct fields
		mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
		builder->createStore(valuesStackBuffer, valuesBufferField);
		mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
		builder->createStore(builder->getInt32(0), valuesLenField); // 0 elements
		mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
		builder->createStore(builder->getInt32(0), valuesCapField); // capacity=0 means stack data

		// Call map<K,V>.init(null, keysManaged, valuesManaged)
		std::string initMethodName = specializedName + ".init";
		mir::MIRFunction *initFunc = module->getFunction(initMethodName);
		if (!initFunc) {
			reportError("map.init method not found for type: " + specializedName +
							" - ensure ExpressibleByMapLiteral.init is implemented",
						varDecl->line, varDecl->column);
			return;
		}

		// Pass null for self (factory method returns new instance)
		mir::MIRValue *nullSelf = builder->getNull();
		mir::MIRValue *mapResult = builder->createCall(initFunc, {nullSelf, keysManaged, valuesManaged}, "map.init");

		// Allocate storage for the map and store the result
		mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, varDecl->name);
		builder->createStore(mapResult, mapAlloca);
		namedValues[varDecl->name] = mapAlloca;
		variableTypes[varDecl->name] = specializedName;

		return;
	}

	// Handle map literal with entries: ["key1": value1, "key2": value2]
	// Uses ExpressibleByMapLiteral interface pattern - creates _ManagedArrays and calls init()
	if (auto *mapWithEntries = dynamic_cast<MapLiteralWithEntriesExprAST *>(varDecl->initializer.get())) {
		const std::string &keyType = mapWithEntries->inferredKeyType;
		const std::string &valueType = mapWithEntries->inferredValueType;

		// Build the specialized type name
		std::string specializedName = "map<" + keyType + "," + valueType + ">";
		logDetail("Processing map literal with entries: " + specializedName);

		// Instantiate the generic struct if not already done
		if (instantiatedGenericStructs.find(specializedName) == instantiatedGenericStructs.end()) {
			logDetail("  Need to instantiate generic struct");
			if (structDefinitions.find("map") != structDefinitions.end()) {
				logDetail("  Found 'map' in structDefinitions");
				std::map<std::string, std::string> typeBindings = {
					{"Key", keyType},
					{"Value", valueType}};
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

		// Mark struct type as used
		mapStructType->used = true;

		// Get number of entries and element types
		int numEntries = static_cast<int>(mapWithEntries->entries.size());
		mir::MIRType *keyMirType = getTypeFromString(keyType);
		mir::MIRType *valueMirType = getTypeFromString(valueType);

		// Create _ManagedArray<Key> struct for keys
		mir::MIRType *keyManagedArrayType = getOrCreateManagedArrayDataType(keyType);
		mir::MIRValue *keysManaged = builder->createAlloca(keyManagedArrayType, "managed.keys");

		// Create _ManagedArray<Value> struct for values
		mir::MIRType *valueManagedArrayType = getOrCreateManagedArrayDataType(valueType);
		mir::MIRValue *valuesManaged = builder->createAlloca(valueManagedArrayType, "managed.values");

		// Allocate stack buffers for keys and values
		mir::MIRType *keysStackArrayType = mir::MIRType::getArray(keyMirType, numEntries > 0 ? numEntries : 1);
		mir::MIRValue *keysStackBuffer = builder->createAlloca(keysStackArrayType, "keys.buffer");

		mir::MIRType *valuesStackArrayType = mir::MIRType::getArray(valueMirType, numEntries > 0 ? numEntries : 1);
		mir::MIRValue *valuesStackBuffer = builder->createAlloca(valuesStackArrayType, "values.buffer");

		// Store keys and values in their respective stack buffers
		for (int i = 0; i < numEntries; i++) {
			const auto &entry = mapWithEntries->entries[i];

			// Store key
			mir::MIRValue *keyVal = generateExpr(entry.key.get());
			mir::MIRValue *keyPtr = builder->createArrayGEP(keyMirType, keysStackBuffer,
														   builder->getInt64(i), "key.ptr");
			// For struct types (like string), generateExpr returns a pointer to an alloca
			// We need to load the struct value before storing it
			if (keyMirType->isStruct()) {
				mir::MIRValue *keyLoaded = builder->createLoad(keyMirType, keyVal, "key.loaded");
				builder->createStore(keyLoaded, keyPtr);
			} else {
				builder->createStore(keyVal, keyPtr);
			}

			// Store value
			mir::MIRValue *valVal = generateExpr(entry.value.get());
			mir::MIRValue *valPtr = builder->createArrayGEP(valueMirType, valuesStackBuffer,
														   builder->getInt64(i), "val.ptr");
			// For struct types, load the value before storing
			if (valueMirType->isStruct()) {
				mir::MIRValue *valLoaded = builder->createLoad(valueMirType, valVal, "val.loaded");
				builder->createStore(valLoaded, valPtr);
			} else {
				builder->createStore(valVal, valPtr);
			}
		}

		// Initialize keys _ManagedArray struct fields
		mir::MIRValue *keysBufferField = builder->createStructGEP(keyManagedArrayType, keysManaged, 0, "keys._buffer");
		builder->createStore(keysStackBuffer, keysBufferField);
		mir::MIRValue *keysLenField = builder->createStructGEP(keyManagedArrayType, keysManaged, 1, "keys._len");
		builder->createStore(builder->getInt32(numEntries), keysLenField);
		mir::MIRValue *keysCapField = builder->createStructGEP(keyManagedArrayType, keysManaged, 2, "keys._capacity");
		builder->createStore(builder->getInt32(0), keysCapField); // capacity=0 means stack data

		// Initialize values _ManagedArray struct fields
		mir::MIRValue *valuesBufferField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 0, "values._buffer");
		builder->createStore(valuesStackBuffer, valuesBufferField);
		mir::MIRValue *valuesLenField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 1, "values._len");
		builder->createStore(builder->getInt32(numEntries), valuesLenField);
		mir::MIRValue *valuesCapField = builder->createStructGEP(valueManagedArrayType, valuesManaged, 2, "values._capacity");
		builder->createStore(builder->getInt32(0), valuesCapField); // capacity=0 means stack data

		// Call map<K,V>.init(null, keysManaged, valuesManaged)
		std::string initMethodName = specializedName + ".init";
		mir::MIRFunction *initFunc = module->getFunction(initMethodName);
		if (!initFunc) {
			reportError("map.init method not found for type: " + specializedName +
							" - ensure ExpressibleByMapLiteral.init is implemented",
						varDecl->line, varDecl->column);
			return;
		}

		// Pass null for self (factory method returns new instance)
		mir::MIRValue *nullSelf = builder->getNull();
		mir::MIRValue *mapResult = builder->createCall(initFunc, {nullSelf, keysManaged, valuesManaged}, "map.init");

		// Allocate storage for the map and store the result
		mir::MIRValue *mapAlloca = builder->createAlloca(mapStructType, varDecl->name);
		builder->createStore(mapResult, mapAlloca);
		namedValues[varDecl->name] = mapAlloca;
		variableTypes[varDecl->name] = specializedName;

		return;
	}

	// Handle set from array initialization: set from [1, 2, 3]
	// Uses ExpressibleByArrayLiteral interface pattern
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

		// Get the array literal to extract elements
		auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(setFromExpr->arrayExpr.get());
		if (!arrayLiteral) {
			reportError("set from expression requires an array literal",
						varDecl->line, varDecl->column);
			return;
		}

		// Create _ManagedArray<Element> struct to pass to init()
		// Layout: { ptr _buffer, i32 _len, i32 _capacity }
		mir::MIRType *managedArrayType = getOrCreateManagedArrayDataType(elemType);
		mir::MIRValue *managedAlloca = builder->createAlloca(managedArrayType, "managed.array");

		// Get element info
		int numElements = static_cast<int>(arrayLiteral->values.size());
		mir::MIRType *elemMirType = getTypeFromString(elemType);

		// Allocate stack buffer for elements (capacity=0 means constant/stack data)
		mir::MIRType *stackArrayType = mir::MIRType::getArray(elemMirType, numElements > 0 ? numElements : 1);
		mir::MIRValue *stackBuffer = builder->createAlloca(stackArrayType, "literal.buffer");

		// Store elements in the stack buffer
		for (int i = 0; i < numElements; i++) {
			mir::MIRValue *elemVal = generateExpr(arrayLiteral->values[i].get());
			mir::MIRValue *elemPtr = builder->createArrayGEP(elemMirType, stackBuffer,
															 builder->getInt64(i), "elem.ptr");
			builder->createStore(elemVal, elemPtr);
		}

		// Initialize _ManagedArray struct fields
		// Field 0: _buffer pointer
		mir::MIRValue *bufferField = builder->createStructGEP(managedArrayType, managedAlloca, 0, "managed._buffer");
		builder->createStore(stackBuffer, bufferField);

		// Field 1: _len
		mir::MIRValue *lenField = builder->createStructGEP(managedArrayType, managedAlloca, 1, "managed._len");
		builder->createStore(builder->getInt32(numElements), lenField);

		// Field 2: _capacity = 0 (constant/stack data - init() will copy to its own arrays)
		mir::MIRValue *capField = builder->createStructGEP(managedArrayType, managedAlloca, 2, "managed._capacity");
		builder->createStore(builder->getInt32(0), capField);

		// Call set<Element>.init(null, managedArray)
		// _ManagedArray<T> is an opaque pointer type (like _ManagedString), no hidden length
		std::string initMethodName = specializedName + ".init";
		mir::MIRFunction *initFunc = module->getFunction(initMethodName);
		if (!initFunc) {
			reportError("set.init method not found for type: " + specializedName +
							" - ensure ExpressibleByArrayLiteral.init is implemented",
						varDecl->line, varDecl->column);
			return;
		}

		// Pass null for self (factory method returns new instance)
		mir::MIRValue *nullSelf = builder->getNull();
		mir::MIRValue *setResult = builder->createCall(initFunc, {nullSelf, managedAlloca}, "set.init");

		// Allocate storage for the set and store the result
		mir::MIRValue *setAlloca = builder->createAlloca(setStructType, varDecl->name);
		builder->createStore(setResult, setAlloca);
		namedValues[varDecl->name] = setAlloca;
		variableTypes[varDecl->name] = specializedName;

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
		// Substitute type parameters if we're in a generic method context
		std::string explicitType = varDecl->type;
		auto bindingIt = currentTypeBindings.find(explicitType);
		if (bindingIt != currentTypeBindings.end()) {
			explicitType = bindingIt->second;
		}
		variableTypes[varDecl->name] = explicitType;
	} else if (allocaType->kind == mir::MIRTypeKind::Struct) {
		// For structs, use the struct name so member access works
		variableTypes[varDecl->name] = allocaType->structName;
	} else if (allocaType->kind == mir::MIRTypeKind::Ptr && varDecl->initializer) {
		// For pointer types from array indexing, try to get the element type from the array
		// This handles cases like "var element = oldElements[i]" where element is a struct
		std::string derivedType = "ptr";
		if (auto *arrayIndexExpr = dynamic_cast<ArrayIndexExprAST *>(varDecl->initializer.get())) {
			std::string arrayName;
			if (!arrayIndexExpr->arrayName.empty()) {
				arrayName = arrayIndexExpr->arrayName;
			} else if (arrayIndexExpr->arrayExpr) {
				if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayIndexExpr->arrayExpr.get())) {
					arrayName = varExpr->name;
				}
			}
			if (!arrayName.empty()) {
				std::string arrayType = variableTypes[arrayName];
				if (maxon::TypeConversion::isManagedArrayType(arrayType)) {
					derivedType = maxon::TypeConversion::getArrayElementType(arrayType);
				} else if (maxon::TypeConversion::isArrayStructType(arrayType)) {
					derivedType = maxon::TypeConversion::getArrayStructElementType(arrayType);
				}
			}
		}
		variableTypes[varDecl->name] = derivedType;
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
