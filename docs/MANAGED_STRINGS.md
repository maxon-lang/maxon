# Managed Strings in Maxon

This document describes the internal implementation of Maxon's managed string system, including memory layout, reference counting, and the C++ helper API for compiler developers.

## Overview

Maxon strings use a hybrid storage strategy:

1. **SSO (Small String Optimization)**: Strings up to 15 bytes are stored inline without heap allocation
2. **Heap-allocated**: Larger strings use heap memory with reference counting for efficient sharing

The compiler tracks string allocations and automatically releases them at scope exit.

## Memory Layout

### Capacity Semantics

The capacity field determines string ownership:

| Capacity | Meaning |
|----------|---------|
| -1 | SSO (small string optimization) - data stored inline |
| 0 | Constant string - no heap ownership, don't retain/release |
| > 0 | Heap-allocated with ownership - use retain/release |

### SSO Strings (capacity = -1)

Small strings store data inline in the `__ManagedStringData` struct:

```
__ManagedStringData (SSO mode):
+--------+--------+--------+
| data   | length | cap=-1 |
| (ptr)  | (i64)  | (i64)  |
+--------+--------+--------+
    |
    +---> Points to inline buffer (within the struct)
```

### Heap-Allocated Strings (capacity > 0)

Larger strings use heap memory with an 8-byte header:

```
Heap allocation:
+----------+----------+------------------+------+
| refcount | data_sz  |      data        | null |
|  (i32)   |  (i32)   |   (variable)     | term |
+----------+----------+------------------+------+
^          ^          ^
|          |          +-- data pointer (returned by _managed_string_alloc)
|          +-- offset 4: data size in bytes
+-- offset 0: refcount starts at 1

__ManagedStringData (heap mode):
+--------+--------+--------+
| data   | length | cap    |
| (ptr)  | (i64)  | (i64)  |
+--------+--------+--------+
    |
    +---> Points to data area in heap allocation (offset +8 from raw)
```

### Related Types

#### `cstring`

Used for C-compatible null-terminated strings (e.g., for printing):

```
cstring struct:
+--------+--------+----------+
| data   | length | managed  |
| (ptr)  | (i64)  | (ptr)    |
+--------+--------+----------+
    |               |
    |               +-- Pointer to original __ManagedStringData (for SSO)
    |                   or null (for heap strings)
    +---> C-string data (may be a copy or retained from parent)
```

#### `substring`

A view into a parent string:

```
substring struct:
+----------+--------+--------+---------+
| parent   | data   | length | iterPos |
| (ptr)    | (ptr)  | (i64)  | (i64)   |
+----------+--------+--------+---------+
     |         |
     |         +---> Points into parent's data buffer
     +-- Pointer to parent __ManagedStringData (retained)
```

## Reference Counting

Heap strings use atomic reference counting:

1. **Initial allocation**: refcount = 1
2. **Retain** (`_managed_string_retain`): refcount += 1
3. **Release** (`_managed_string_release`): refcount -= 1; if 0, free memory

### When to Retain

- Creating a substring from a heap string
- Creating a cstring from a heap string
- Assigning a heap string to a new variable (copy semantics use COW)

### When to Release

- At scope exit for locally allocated strings
- When reassigning a string variable (release old value)
- When a substring or cstring goes out of scope

## Runtime Functions

Located in `maxon-runtime/runtime.mir`:

| Function | Signature | Purpose |
|----------|-----------|---------|
| `_managed_string_alloc` | `(i64 capacity, ptr tag) -> ptr` | Allocate buffer with header |
| `_managed_string_release` | `(ptr data, ptr tag) -> void` | Decrement refcount, free if 0 |
| `_managed_string_retain` | `(ptr data) -> void` | Increment refcount |

## Scope Tracking

The compiler tracks allocations in `scopeStack` for automatic cleanup:

```cpp
struct ScopeInfo {
    // Heap string buffers (data pointers) that need release at scope exit
    std::vector<std::pair<std::string, mir::MIRValue*>> heapAllocatedStrings;

    // __ManagedStringData metadata structs that need freeing at scope exit
    // These are the 32-byte structs that hold {data_ptr, length, capacity}
    std::vector<mir::MIRValue*> managedStringDataPtrs;

    // Cstrings that need release (field 0 is the data pointer)
    std::vector<std::pair<std::string, mir::MIRValue*>> cstringAllocas;

    // Substrings that need parent release
    std::vector<std::pair<std::string, mir::MIRValue*>> substringAllocas;
};
```

### Ownership Transfer

When an interpolated string result is assigned to a variable, ownership of the 
`__ManagedStringData` struct transfers to that variable. The compiler calls
`transferManagedStringDataOwnership()` to remove the last tracked metadata struct
from cleanup, since it's now owned by the variable and will be freed when that
variable goes out of scope.

At scope exit (`popScope`), the compiler emits cleanup code for all tracked allocations.

## ManagedStringBuilder Helper Class

The `ManagedStringBuilder` class provides a clean C++ API for generating string-related MIR code.

### Usage Example

```cpp
#include "codegen_mir/managed_string_builder.h"

void someIntrinsicCodegen(MIRCodeGenerator& gen) {
    ManagedStringBuilder msb(gen);

    // Get type
    mir::MIRType* managedType = msb.getManagedStringType();

    // Extract fields
    mir::MIRValue* dataPtr = msb.getDataPtr(managedPtr);
    mir::MIRValue* length = msb.getLength(managedPtr);
    mir::MIRValue* capacity = msb.getCapacity(managedPtr);

    // Check if heap allocated
    mir::MIRValue* isHeap = msb.isHeapAllocated(managedPtr);

    // Allocate new buffer (with automatic tracking tag)
    mir::MIRValue* newBuffer = msb.allocateBuffer(capacityVal, "string concat");

    // Allocate type on stack
    mir::MIRValue* resultAlloca = msb.allocateManagedStruct("result");

    // Populate type fields
    msb.populateManagedStruct(resultAlloca, newBuffer, length, capacity);

    // Track for cleanup
    msb.trackHeapString("result", resultAlloca);

    // Emit conditional release (only if heap-allocated)
    msb.emitReleaseIfHeap(managedPtr, "string reassign");

    // Or emit unconditional release (when you know it's heap)
    msb.emitRelease(dataPtr, "string cleanup");
}
```

### API Reference

#### Type Accessors

- `getManagedStringType()` - Returns `__ManagedStringData` type type
- `getSubstringType()` - Returns `substring` type type
- `getCstringType()` - Returns `cstring` type type

#### Field Extraction

- `getDataPtr(managedPtr, name)` - Get data pointer (field 0)
- `getLength(managedPtr, name)` - Get length (field 1)
- `getCapacity(managedPtr, name)` - Get capacity (field 2)
- `isHeapAllocated(managedPtr, name)` - Check if capacity > 0 (heap-owned)

#### Allocation

- `allocateBuffer(capacity, tag)` - Allocate heap buffer with header
- `allocateManagedStruct(name)` - Stack-allocate `__ManagedStringData`
- `allocateCstringStruct(name)` - Stack-allocate `cstring`
- `allocateSubstringStruct(name)` - Stack-allocate `substring`

#### Struct Population

- `populateManagedStruct(ptr, data, len, cap)` - Fill all fields
- `populateCstringStruct(ptr, data, len, managed)` - Fill cstring fields
- `populateSubstringStruct(ptr, parent, data, len, iterPos)` - Fill substring fields

#### Reference Counting

- `emitReleaseIfHeap(managedPtr, tag)` - Conditional release (checks capacity)
- `emitRetainIfHeap(managedPtr)` - Conditional retain
- `emitRelease(dataPtr, tag)` - Unconditional release
- `emitRetain(dataPtr)` - Unconditional retain

#### Scope Tracking

- `trackHeapString(name, dataPtrAlloca)` - Track for cleanup
- `trackCstring(name, cstringAlloca)` - Track cstring
- `trackSubstring(name, substringAlloca)` - Track substring

#### Utility

- `createTag(name, content)` - Create global string for tracking tags
- `getOrDeclareFunction(name, returnType, paramTypes)` - Get runtime function

## Common Patterns

### String Concatenation

```cpp
// 1. Get lengths from both operands
auto len1 = msb.getLength(m1Ptr);
auto len2 = msb.getLength(m2Ptr);
auto totalLen = builder->createAdd(len1, len2, "total.len");

// 2. Allocate buffer for result (len + 1 for null terminator)
auto allocSize = builder->createAdd(totalLen, builder->getInt64(1));
auto newBuffer = msb.allocateBuffer(allocSize, "string concat");

// 3. Copy data from both strings
// ... memcpy calls ...

// 4. Create result struct
auto result = msb.allocateManagedStruct("concat.result");
msb.populateManagedStruct(result, newBuffer, totalLen, totalLen);

// 5. Track for cleanup
msb.trackHeapString("concat", result);
```

### COW (Copy-on-Write) for Mutation

```cpp
// Check if we're the sole owner (refcount == 1)
// If not, allocate new buffer and copy

auto refcount = loadRefcountFromHeader(dataPtr);
auto isUnique = builder->createICmpEQ(refcount, builder->getInt64(1));

// Branch: if unique, modify in place; else allocate copy
```

### Scope Cleanup

The compiler automatically emits cleanup at scope exit:

```cpp
void MIRCodeGenerator::popScope(mir::MIRFunction *function) {
    // Release cstrings (field 0 is data pointer)
    for (auto& [name, cstringAlloca] : scopeStack.back().cstringAllocas) {
        // Load data pointer from cstring
        auto dataPtr = builder->createStructGEP(cstringType, cstringAlloca, 0);
        auto data = builder->createLoad(mir::MIRType::getPtr(), dataPtr);

        // Call release
        builder->createCall(releaseFunc, {data, tag});
    }

    // Similar for substrings (release parent) and heap strings
}
```

## Debugging Tips

### Memory Tracking

Enable allocation tracking with `--track-allocs` to see all allocations:

```
ALLOC #1: 36 bytes (string literal)
ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)
FREE #1: 36 bytes (string literal)

=== ALLOC STATS ===
Allocated: 46 bytes
Freed:     46 bytes
Leaked:    0 bytes
```

### Common Issues

1. **Leaked bytes**: Check that all heap strings are tracked in `scopeStack`
2. **Double free**: Verify refcounting logic, especially for shared strings
3. **Use after free**: Ensure substrings retain their parent before use
4. **Wrong size in tracking**: Header is 8 bytes, allocation is `capacity + 8`

### Header Layout Verification

The header at `data - 8` should have:
- Offset 0-3: refcount (i32)
- Offset 4-7: data_size (i32)

To verify in the debugger:
```
# At breakpoint in _managed_string_release
# data is the argument, header is at data - 8
print *(int32_t*)(data - 8)  # refcount
print *(int32_t*)(data - 4)  # data_size
```
