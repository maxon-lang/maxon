# Managed Arrays in Maxon

This document describes the internal implementation of Maxon's managed array system, including memory layout, reference counting, and the C++ helper API for compiler developers.

## Overview

All Maxon arrays use the unified `array<T>` struct type from the standard library. Arrays use a hybrid storage strategy based on capacity:

1. **Stack-allocated** (capacity = 0): Fixed-size arrays declared with `let arr = array of N T` or `let arr = [1, 2, 3]`
2. **Heap-allocated** (capacity > 0): Dynamic arrays declared with `var` that can grow via `push()`

The compiler tracks array allocations and automatically releases heap-allocated buffers at scope exit.

## Type System

All array variables have type `array<T>` regardless of how they're declared:

| Declaration | Type | Storage |
|-------------|------|----------|
| `let arr = [1, 2, 3]` | `array<int>` | Stack buffer, capacity = 0 |
| `let arr = array of 5 int` | `array<int>` | Stack buffer, capacity = 0 |
| `var arr = [1, 2, 3]` | `array<int>` | Heap buffer, capacity > 0 |
| `var arr = array of 5 int` | `array<int>` | Heap buffer, capacity > 0 |

This unified type system means all arrays support the same methods (`.count()`, `.push()`, etc.) though mutating methods like `.push()` require a `var` declaration.

## Memory Layout

### Capacity Semantics

The capacity field determines array ownership:

| Capacity | Meaning |
|----------|---------|
| 0 | Stack-allocated buffer - no heap ownership |
| > 0 | Heap-allocated with ownership - buffer can be freed |

### Stack Arrays (capacity = 0)

Arrays declared with `let` store their buffer on the stack:

```
__ManagedArrayData<T> (stack mode):
+--------+--------+--------+
| buffer | length | cap=0  |
| (ptr)  | (i64)  | (i64)  |
+--------+--------+--------+
    |
    +---> Points to stack-allocated buffer [N x T]
```

Stack arrays have capacity = 0 to indicate they don't own the buffer (no heap allocation to free).

### Heap-Allocated Arrays (capacity > 0)

Arrays declared with `var` use heap memory with a 16-byte header for reference counting:

```
Heap allocation:
+----------+----------+----------------+
| refcount | dataSize | element data   |
| (i64)    | (i64)    | ...            |
+----------+----------+----------------+
^          ^          ^
offset 0   offset 8   offset 16 (data pointer returned by _managed_array_alloc)

__ManagedArrayData<T> (heap mode):
+--------+--------+--------+
| buffer | length | cap    |
| (ptr)  | (i64)  | (i64)  |
+--------+--------+--------+
    |
    +---> Points to data area in heap allocation (offset +16 from raw)
```

### Array Type Structure

The full `array<T>` stdlib struct includes an iteration index for for-loop support:

```
array<T> struct (stdlib/array.maxon):
+-------------------+----------+
| managed           | iterIndex |
| (__ManagedArrayData| (i64)     |
| embedded struct)  |           |
+-------------------+----------+

Field 0 (managed):
  +--------+--------+--------+
  | _buffer| _len   | _capacity|
  | (ptr)  | (i64)  | (i64)    |
  +--------+--------+--------+

Field 1 (iterIndex): i64 - used by iterator protocol
```

All array operations access the nested `managed` field to get buffer, length, and capacity.

## Scope Tracking

The compiler tracks allocations in `scopeStack` for automatic cleanup:

```cpp
type HeapArrayInfo {
    std::string name;        // Descriptive name
    mir::MIRValue* alloca;   // Alloca for the array struct
    std::string elementType; // Element type name (e.g., "int")
    bool isDynamic;          // True if buffer can be reallocated
};

type ScopeInfo {
    // Arrays that need _managed_array_release() at scope exit
    std::vector<HeapArrayInfo> heapAllocatedArrays;
};
```

At scope exit (`popScope`), the compiler emits cleanup code for all tracked allocations.

## ManagedArrayBuilder Helper Class

The `ManagedArrayBuilder` class provides a clean C++ API for generating array-related MIR code.

### Usage Example

```cpp
#include "codegen_mir/managed_array_builder.h"

void someIntrinsicCodegen(MIRCodeGenerator& gen) {
    ManagedArrayBuilder mab(gen, "int");  // Note: element type is required

    // Get type
    mir::MIRType* managedType = mab.getManagedArrayDataType();

    // Extract fields
    mir::MIRValue* dataPtr = mab.getDataPtr(managedPtr);
    mir::MIRValue* length = mab.getLength(managedPtr);
    mir::MIRValue* capacity = mab.getCapacity(managedPtr);

    // Check if heap allocated
    mir::MIRValue* isHeap = mab.isHeapAllocated(managedPtr);

    // Allocate new buffer (with automatic tracking tag)
    mir::MIRValue* newBuffer = mab.allocateBuffer(numElements, "array grow");

    // Allocate type on stack
    mir::MIRValue* resultAlloca = mab.allocateManagedStruct("result");

    // Populate type fields
    mab.populateManagedStruct(resultAlloca, newBuffer, length, capacity);

    // Get element pointer
    mir::MIRValue* elemPtr = mab.getElementPtr(dataPtr, index, "elem");

    // Track for cleanup
    mab.trackHeapArray("result", resultAlloca, "int", true);

    // Emit conditional release (only if heap-allocated)
    mab.emitReleaseIfHeap(managedPtr, "array cleanup");
}
```

### API Reference

#### Constructor

- `ManagedArrayBuilder(MIRCodeGenerator &gen, const std::string &elementType)` - Contype builder for arrays of given element type

#### Type Accessors

- `getManagedArrayDataType()` - Returns `__ManagedArrayData<T>` type type
- `getArrayStructType()` - Returns `array<T>` type type (with iterIndex)
- `getElementMIRType()` - Returns MIR type for the element
- `getElementSize()` - Returns element size in bytes

#### Field Extraction

- `getDataPtr(managedPtr, name)` - Get buffer pointer (field 0)
- `getLength(managedPtr, name)` - Get length (field 1)
- `getCapacity(managedPtr, name)` - Get capacity (field 2)
- `isHeapAllocated(managedPtr, name)` - Check if capacity > 0 (heap-owned)

#### Field Modification

- `setDataPtr(managedPtr, dataPtr)` - Set buffer pointer (field 0)
- `setLength(managedPtr, length)` - Set length (field 1)
- `setCapacity(managedPtr, capacity)` - Set capacity (field 2)

#### Allocation

- `allocateBuffer(numElements, tag)` - Allocate heap buffer for N elements
- `allocateManagedStruct(name)` - Stack-allocate `__ManagedArrayData`
- `allocateArrayStruct(name)` - Stack-allocate `array<T>` (with iterIndex)

#### Struct Population

- `populateManagedStruct(ptr, data, len, cap)` - Fill all fields

#### Element Access

- `getElementPtr(dataPtr, index, name)` - Get pointer to element at index

#### Reference Counting

- `emitReleaseIfHeap(managedPtr, tag)` - Conditional release (checks capacity)
- `emitRetainIfHeap(managedPtr)` - Conditional retain
- `emitRelease(dataPtr, tag)` - Unconditional release
- `emitRetain(dataPtr)` - Unconditional retain

#### Scope Tracking

- `trackHeapArray(name, structAlloca, elementType, isDynamic)` - Track for cleanup

#### Utility

- `createTag(name, content)` - Create global string for tracking tags
- `getOrDeclareFunction(name, returnType, paramTypes)` - Get runtime function

## Key Differences from ManagedStringBuilder

| Aspect | ManagedStringBuilder | ManagedArrayBuilder |
|--------|---------------------|---------------------|
| Element type | Fixed (byte) | Parameterized (constructor arg) |
| Capacity semantics | -1=SSO, 0=const, >0=heap | 0=stack, >0=heap |
| Size calculation | Direct bytes | elemCount * elemSize |
| Element access | N/A | `getElementPtr()` with stride |
| Reference counting | Used for heap strings | Used for heap arrays |

## Common Patterns

### Array Push with Growth

```cpp
// 1. Check if we need to grow
auto len = mab.getLength(structPtr);
auto cap = mab.getCapacity(structPtr);
auto needsGrow = builder->createICmpSGE(len, cap);

// 2. If needed, allocate larger buffer
// ... grow logic ...

// 3. Set element at current length
auto dataPtr = mab.getDataPtr(structPtr);
auto elemPtr = mab.getElementPtr(dataPtr, len);
builder->createStore(value, elemPtr);

// 4. Increment length
auto newLen = builder->createAdd(len, builder->getInt64(1));
mab.setLength(structPtr, newLen);
```

### Scope Cleanup

The compiler automatically emits cleanup at scope exit:

```cpp
void MIRCodeGenerator::popScope(mir::MIRFunction *function) {
    for (const auto& info : scopeStack.back().heapAllocatedArrays) {
        ManagedArrayBuilder mab(*this, info.elementType);
        mir::MIRType *arrayStructType = mab.getArrayStructType();
        mir::MIRValue *managedField = builder->createStructGEP(
            arrayStructType, info.alloca, 0, info.name + ".managed");

        if (info.isDynamic) {
            // Dynamic arrays may or may not be heap-allocated
            mab.emitReleaseIfHeap(managedField, "array cleanup");
        } else {
            // Non-dynamic arrays with capacity > 0 are always heap
            mir::MIRValue *dataPtr = mab.getDataPtr(managedField);
            mab.emitRelease(dataPtr, "array cleanup");
        }
    }
}
```

## Runtime Functions

Located in `maxon-runtime/runtime.mir`:

| Function | Signature | Purpose |
|----------|-----------|---------|
| `_managed_array_alloc` | `(i64 byteSize, ptr tag) -> ptr` | Allocate buffer with header |
| `_managed_array_release` | `(ptr data, ptr tag) -> void` | Decrement refcount, free if 0 |
| `_managed_array_retain` | `(ptr data) -> void` | Increment refcount |
| `_managed_array_get_refcount` | `(ptr data) -> i64` | Get current refcount |

## Debugging Tips

### Memory Tracking

Enable allocation tracking with `--track-allocs` to see all allocations:

```
ALLOC #1: 40 bytes (array grow)
ALLOC #2: 56 bytes (array grow)
FREE #1: 40 bytes (array grow)
ALLOC #3: 88 bytes (array grow)
FREE #2: 56 bytes (array grow)
10
FREE #3: 88 bytes (array cleanup)

=== ALLOC STATS ===
Allocated: 184 bytes
Freed:     184 bytes
Leaked:    0 bytes
```

Both array and string allocations are now fully tracked with reference counting.

### Common Issues

1. **Leaked bytes**: Check that all heap arrays are tracked in `scopeStack`
2. **Double free**: Verify capacity is set to 0 for stack arrays
3. **Use after free**: Arrays are freed at scope exit - don't access after scope
4. **Wrong element size**: Ensure correct element type is passed to builder

## Struct Field Arrays

When a struct has an `array of T` field, special handling is required:

### Declaration

```maxon
type Config
    var sources array of string
end 'Config'
```

### Initialization

Array literal initialization for struct fields creates a proper `array<T>` struct:

```cpp
// In codegen_mir_stmt_decl.cpp, struct field initialization:
if (maxon::TypeConversion::isArrayStructType(fieldTypeStr)) {
    // 1. Stack-allocate buffer for elements
    mir::MIRValue *stackBuffer = builder->createAlloca(arrayType, fieldName + ".stack_buffer");
    
    // 2. Initialize array<T> struct in the field:
    //    { managed: { _buffer, _len, _capacity=0 }, iterIndex=0 }
    mir::MIRValue *managedField = builder->createStructGEP(arrayStructType, fieldPtr, 0, "managed");
    builder->createStore(stackBuffer, bufferField);
    builder->createStore(arraySizeVal, lenField);
    builder->createStore(zeroCapacity, capField);  // 0 = stack-allocated
    builder->createStore(builder->getInt64(0), iterField);
    
    // 3. Store each element value into the buffer
    for (int i = 0; i < constantArraySize; i++) {
        mir::MIRValue *elementPtr = builder->createArrayGEP(elementType, stackBuffer, indexVal, "arrayidx");
        builder->createStore(initValues[i], elementPtr);
    }
}
```

### Method Calls on Struct Field Arrays

When calling methods like `.count()` on struct field arrays:

```maxon
var config = Config{sources: ["a", "b", "c"]}
let count = config.sources.count()  // Works correctly
```

The semantic analyzer:
1. Recognizes the field type is `array<string>` during member access
2. Instantiates generic methods (`array<string>.count`, etc.)
3. Resolves method calls using the specialized method name

```cpp
// In semantic_analyzer_expr.cpp, MemberAccessExprAST handling:
if (maxon::TypeConversion::isArrayStructType(field.type)) {
    std::string elemType = maxon::TypeConversion::getArrayStructElementType(field.type);
    std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
    instantiateGenericStructMethods("array", field.type, typeBindings);
}
```

### Generated IR

For `config.sources.count()`:

```llvm
; Get the array<string> field from Config struct
%field = getelementptr %Config, ptr %config, i64 0, i64 0
%array = load %array<string>, ptr %field

; Create temp alloca and call count method
%temp = alloca %array<string>
store %array<string> %array, ptr %temp
%count = call i64 @array<string>.count(ptr %temp)
```
