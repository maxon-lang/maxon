# Swift-like Generic Array Implementation Plan

This document outlines the plan to implement Swift-like generic arrays in Maxon's stdlib, where stdlib owns the high-level logic while the compiler provides minimal bridging intrinsics.

## Overview

**Goal**: Create a `array<Element>` struct in stdlib with methods like `push`, `pop`, `insert`, `remove`, `get`, `first`, `last`, etc. Methods return optionals (`Element or nil`) where appropriate.

**Design Principles**:
1. **stdlib owns logic** — High-level methods implemented in Maxon
2. **Compiler provides bridging intrinsics** — Low-level memory operations only
3. **Raw `[]T` arrays keep existing intrinsics** — Backwards compatibility
4. **Optionals for safety** — `pop()`, `first`, `last`, `get()`, `remove()` return `Element or nil`
5. **Explicit `reserve()` for capacity** — No auto-shrink
6. **Insert clamps/appends at bounds** — Swift-like behavior
7. **Remove returns nil for invalid index** — Safe behavior

## Current State

### Existing Array Infrastructure

**Memory Layout** (heap-allocated arrays):
```
[length:i32][capacity:i32][...data...]
     -8          -4           0+ (data pointer points here)
```

**Existing Intrinsics** (`intrinsics_defs.h`):
- `__array_len(arr)` → `int` — Reads length from offset -8

**Existing Raw Array Operations** (`codegen_mir_expr_array.cpp`):
- `push(arr, value)` — Grows array if needed, stores value, increments length
- `pop(arr)` → element — Decrements length, returns last element (no bounds check!)

**Current stdlib wrapper** (`stdlib/collections/array.maxon`):
```maxon
export struct array uses Element is Iterable with Element
    var _data []Element
    
    export function count() int
        return __array_len(_data)
    end 'count'
end 'array'
```

## Implementation Plan

### Phase 1: New Bridging Intrinsics

Add these intrinsics to `maxon-bin/intrinsics_defs.h`:

```cpp
// Array intrinsics (__array_*)
{"__array_len", "int", {IntrinsicParamDef::arrayOf({})}, "intrinsic_array_len"},
{"__array_capacity", "int", {IntrinsicParamDef::arrayOf({})}, "intrinsic_array_capacity"},
{"__array_set_length", "void", {IntrinsicParamDef::arrayOf({}), {"int"}}, "intrinsic_array_set_length"},
{"__array_grow", "void", {IntrinsicParamDef::arrayOf({}), {"int"}}, "intrinsic_array_grow"},
{"__array_set_at", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, {"?"}}, "intrinsic_array_set_at"},
{"__array_shift_right", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, {"int"}}, "intrinsic_array_shift_right"},
{"__array_shift_left", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, {"int"}}, "intrinsic_array_shift_left"},
```

**Note**: `__array_set_at` needs special handling for generic element type. May need to use the array's actual element type at codegen time.

#### 1.1 `__array_capacity(arr)` → `int`

**Purpose**: Read capacity from heap array header (offset -4)

**Implementation** (`codegen_mir_intrinsics.cpp`):
```cpp
mir::MIRValue *MIRCodeGenerator::intrinsic_array_capacity(CallExprAST *callExpr) {
    // Similar to intrinsic_array_len but read from offset -4
    ExprAST *arrayArg = callExpr->args[0].get();
    std::string arrayVarName;
    if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
        arrayVarName = varExpr->name;
    } else {
        reportError("__array_capacity requires a variable argument", callExpr->line, callExpr->column);
    }
    
    // Check for hidden __capacity alloca first
    std::string capacityVar = arrayVarName + ".__capacity";
    mir::MIRValue *capacityAlloca = namedValues[capacityVar];
    if (capacityAlloca) {
        return builder->createLoad(mir::MIRType::getInt32(), capacityAlloca, "array.cap");
    }
    
    // For heap arrays, read from dataPtr - 4
    mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
    if (arrayAlloca) {
        mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
        mir::MIRValue *capacityPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
                                                        {builder->getInt64(-4)}, "capacity.ptr");
        return builder->createLoad(mir::MIRType::getInt32(), capacityPtr, "array.cap");
    }
    
    reportError("Variable is not an array: " + arrayVarName, callExpr->line, callExpr->column);
}
```

#### 1.2 `__array_set_length(arr, len)` → `void`

**Purpose**: Write new length to hidden alloca or heap header

**Implementation**:
```cpp
mir::MIRValue *MIRCodeGenerator::intrinsic_array_set_length(CallExprAST *callExpr) {
    ExprAST *arrayArg = callExpr->args[0].get();
    mir::MIRValue *newLen = generateExpr(callExpr->args[1].get());
    
    std::string arrayVarName;
    if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
        arrayVarName = varExpr->name;
    } else {
        reportError("__array_set_length requires a variable argument", callExpr->line, callExpr->column);
    }
    
    // Check for hidden __length alloca
    std::string lengthVar = arrayVarName + ".__length";
    mir::MIRValue *lengthAlloca = namedValues[lengthVar];
    if (lengthAlloca) {
        builder->createStore(newLen, lengthAlloca);
        return builder->getInt32(0);
    }
    
    // For heap arrays, write to dataPtr - 8
    mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
    if (arrayAlloca) {
        mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
        mir::MIRValue *lengthPtr = builder->createGEP(mir::MIRType::getInt8(), dataPtr,
                                                      {builder->getInt64(-8)}, "length.ptr");
        builder->createStore(newLen, lengthPtr);
        return builder->getInt32(0);
    }
    
    reportError("Variable is not an array: " + arrayVarName, callExpr->line, callExpr->column);
}
```

#### 1.3 `__array_grow(arr, min_capacity)` → `void`

**Purpose**: Grow array if needed (realloc + update pointer). Encapsulates the growth logic currently in `push`.

**Implementation**: Extract growth logic from `generateArrayIntrinsic` push implementation:
- If min_capacity > current_capacity:
  - Calculate new capacity (max(min_capacity, capacity * 2), minimum 4)
  - Call `realloc(ptr, old_size, new_size)`
  - Update data pointer alloca
  - Update capacity alloca/header

#### 1.4 `__array_set_at(arr, index, value)` → `void`

**Purpose**: Write element at index without bounds checking (stdlib handles bounds)

**Implementation**:
```cpp
mir::MIRValue *MIRCodeGenerator::intrinsic_array_set_at(CallExprAST *callExpr) {
    ExprAST *arrayArg = callExpr->args[0].get();
    mir::MIRValue *index = generateExpr(callExpr->args[1].get());
    mir::MIRValue *value = generateExpr(callExpr->args[2].get());
    
    std::string arrayVarName;
    if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
        arrayVarName = varExpr->name;
    } else {
        reportError("__array_set_at requires a variable argument", callExpr->line, callExpr->column);
    }
    
    std::string arrType = variableTypes[arrayVarName];
    std::string elemTypeStr = maxon::TypeConversion::getArrayElementType(arrType);
    mir::MIRType *elemType = getTypeFromString(elemTypeStr);
    
    mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
    mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
    mir::MIRValue *index64 = builder->createSExt(index, mir::MIRType::getInt64(), "idx64");
    mir::MIRValue *elemPtr = builder->createArrayGEP(elemType, dataPtr, index64, "elem.ptr");
    builder->createStore(value, elemPtr);
    
    return builder->getInt32(0);
}
```

#### 1.5 `__array_shift_right(arr, start, count)` → `void`

**Purpose**: Shift elements from `start` to end right by `count` positions (for insert)

**Implementation**:
- Calculate byte size to move: `(length - start) * elem_size`
- Source: `arr + start * elem_size`
- Dest: `arr + (start + count) * elem_size`
- Call `memmove(dest, src, size)` (overlapping regions!)

```cpp
mir::MIRValue *MIRCodeGenerator::intrinsic_array_shift_right(CallExprAST *callExpr) {
    ExprAST *arrayArg = callExpr->args[0].get();
    mir::MIRValue *start = generateExpr(callExpr->args[1].get());
    mir::MIRValue *count = generateExpr(callExpr->args[2].get());
    
    std::string arrayVarName;
    if (auto *varExpr = dynamic_cast<VariableExprAST *>(arrayArg)) {
        arrayVarName = varExpr->name;
    } else {
        reportError("__array_shift_right requires a variable argument", callExpr->line, callExpr->column);
    }
    
    std::string arrType = variableTypes[arrayVarName];
    std::string elemTypeStr = maxon::TypeConversion::getArrayElementType(arrType);
    mir::MIRType *elemType = getTypeFromString(elemTypeStr);
    int elemSize = static_cast<int>(elemType->sizeInBytes);
    
    // Get current length
    mir::MIRValue *lengthAlloca = namedValues[arrayVarName + ".__length"];
    mir::MIRValue *length = builder->createLoad(mir::MIRType::getInt32(), lengthAlloca, "len");
    
    // Calculate number of elements to move
    mir::MIRValue *elementsToMove = builder->createSub(length, start, "elems.to.move");
    
    // Calculate byte size
    mir::MIRValue *elementsToMove64 = builder->createSExt(elementsToMove, mir::MIRType::getInt64(), "elems64");
    mir::MIRValue *byteSize = builder->createMul(elementsToMove64, builder->getInt64(elemSize), "byte.size");
    
    // Get data pointer
    mir::MIRValue *arrayAlloca = namedValues[arrayVarName];
    mir::MIRValue *dataPtr = builder->createLoad(mir::MIRType::getPtr(), arrayAlloca, "data.ptr");
    
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
```

#### 1.6 `__array_shift_left(arr, start, count)` → `void`

**Purpose**: Shift elements from `start + count` to end left by `count` positions (for remove)

**Implementation**: Similar to shift_right but reversed direction.

### Phase 2: Register Intrinsics

#### 2.1 Add to `intrinsics_defs.h`

```cpp
// Array intrinsics (__array_*)
{"__array_len", "int", {IntrinsicParamDef::arrayOf({})}, "intrinsic_array_len"},
{"__array_capacity", "int", {IntrinsicParamDef::arrayOf({})}, "intrinsic_array_capacity"},
{"__array_set_length", "void", {IntrinsicParamDef::arrayOf({}), {"int"}}, "intrinsic_array_set_length"},
{"__array_grow", "void", {IntrinsicParamDef::arrayOf({}), {"int"}}, "intrinsic_array_grow"},
{"__array_set_at", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, IntrinsicParamDef::anyOf({})}, "intrinsic_array_set_at"},
{"__array_shift_right", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, {"int"}}, "intrinsic_array_shift_right"},
{"__array_shift_left", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, {"int"}}, "intrinsic_array_shift_left"},
```

**Note**: `__array_set_at` third parameter needs special handling — it should accept any type matching the array's element type. This may require:
1. A new `IntrinsicParamDef` marker for "element type of first arg"
2. Or special-case validation in semantic analyzer

#### 2.2 Declare in `codegen_mir.h`

Add to the "Array intrinsics" section:
```cpp
// Array intrinsics
mir::MIRValue *intrinsic_array_len(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_capacity(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_set_length(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_grow(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_set_at(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_shift_right(CallExprAST *callExpr);
mir::MIRValue *intrinsic_array_shift_left(CallExprAST *callExpr);
```

#### 2.3 Register in `intrinsic_codegen.cpp`

Add to `methodsByName` map:
```cpp
// Array intrinsics
{"intrinsic_array_len", &MIRCodeGenerator::intrinsic_array_len},
{"intrinsic_array_capacity", &MIRCodeGenerator::intrinsic_array_capacity},
{"intrinsic_array_set_length", &MIRCodeGenerator::intrinsic_array_set_length},
{"intrinsic_array_grow", &MIRCodeGenerator::intrinsic_array_grow},
{"intrinsic_array_set_at", &MIRCodeGenerator::intrinsic_array_set_at},
{"intrinsic_array_shift_right", &MIRCodeGenerator::intrinsic_array_shift_right},
{"intrinsic_array_shift_left", &MIRCodeGenerator::intrinsic_array_shift_left},
```

### Phase 3: Runtime Support

#### 3.1 Add `memmove` to Runtime

Check if `memmove` exists in `maxon-runtime/`. If not, add it:

**Windows** (`maxon-runtime/windows/runtime.mir`):
```mir
; memmove - like memcpy but handles overlapping regions
; For Windows, we can use RtlMoveMemory from ntdll
define ptr @memmove(ptr %dest, ptr %src, i64 %n) {
entry:
    ; Implementation using RtlMoveMemory or manual copy
    ...
}
```

Alternatively, implement a simple backward/forward copy based on overlap direction.

### Phase 4: stdlib Array Implementation

Update `stdlib/collections/array.maxon`:

```maxon
// stdlib/collections/array.maxon
// Generic array wrapper with Swift-like API
//
// Provides safe, optional-returning methods for array manipulation.
// Uses bridging intrinsics for low-level memory operations.

export struct array uses Element is Iterable with Element, Collection with Element
    var _data []Element
    var _iterIndex int
    
    // =========================================================================
    // Properties
    // =========================================================================
    
    // Get the number of elements in the array
    export function count() int
        return __array_len(_data)
    end 'count'
    
    // Get the current capacity (allocated space)
    export function capacity() int
        return __array_capacity(_data)
    end 'capacity'
    
    // Check if array is empty
    export function isEmpty() bool
        return __array_len(_data) == 0
    end 'isEmpty'
    
    // Get first element, or nil if empty
    export function first() Element or nil
        if __array_len(_data) == 0 'empty'
            return nil
        end 'empty'
        return _data[0]
    end 'first'
    
    // Get last element, or nil if empty
    export function last() Element or nil
        var len = __array_len(_data)
        if len == 0 'empty'
            return nil
        end 'empty'
        return _data[len - 1]
    end 'last'
    
    // =========================================================================
    // Element Access (Collection interface)
    // =========================================================================
    
    // Get element at index, or nil if out of bounds
    export function get(index int) Element or nil
        var len = __array_len(_data)
        if index < 0 'bounds'
            return nil
        end 'bounds'
        if index >= len 'bounds'
            return nil
        end 'bounds'
        return _data[index]
    end 'get'
    
    // Set element at index (returns self for chaining)
    // No-op if index out of bounds
    export function set(index int, value Element) Self
        var len = __array_len(_data)
        if index >= 0 'bounds'
            if index < len 'bounds'
                __array_set_at(_data, index, value)
            end 'bounds'
        end 'bounds'
        return self
    end 'set'
    
    // =========================================================================
    // Mutating Operations
    // =========================================================================
    
    // Append element to end
    export function push(value Element)
        var len = __array_len(_data)
        var cap = __array_capacity(_data)
        if len >= cap 'grow'
            // Double capacity, minimum 4
            var newCap = cap * 2
            if newCap < 4 'min'
                newCap = 4
            end 'min'
            __array_grow(_data, newCap)
        end 'grow'
        __array_set_at(_data, len, value)
        __array_set_length(_data, len + 1)
    end 'push'
    
    // Remove and return last element, or nil if empty
    export function pop() Element or nil
        var len = __array_len(_data)
        if len == 0 'empty'
            return nil
        end 'empty'
        var newLen = len - 1
        var value = _data[newLen]
        __array_set_length(_data, newLen)
        return value
    end 'pop'
    
    // Insert element at index (clamps to [0, count])
    export function insert(at int, value Element)
        var len = __array_len(_data)
        var index = at
        
        // Clamp index to valid range
        if index < 0 'clamp'
            index = 0
        end 'clamp'
        if index > len 'clamp'
            index = len
        end 'clamp'
        
        // Ensure capacity
        var cap = __array_capacity(_data)
        if len >= cap 'grow'
            var newCap = cap * 2
            if newCap < 4 'min'
                newCap = 4
            end 'min'
            __array_grow(_data, newCap)
        end 'grow'
        
        // Shift elements right to make room
        if index < len 'shift'
            __array_shift_right(_data, index, 1)
        end 'shift'
        
        // Insert element
        __array_set_at(_data, index, value)
        __array_set_length(_data, len + 1)
    end 'insert'
    
    // Remove and return element at index, or nil if out of bounds
    export function remove(at int) Element or nil
        var len = __array_len(_data)
        
        // Check bounds
        if at < 0 'bounds'
            return nil
        end 'bounds'
        if at >= len 'bounds'
            return nil
        end 'bounds'
        
        // Save element before shifting
        var value = _data[at]
        
        // Shift elements left to fill gap
        if at < len - 1 'shift'
            __array_shift_left(_data, at, 1)
        end 'shift'
        
        __array_set_length(_data, len - 1)
        return value
    end 'remove'
    
    // Remove all elements (keeps capacity)
    export function clear()
        __array_set_length(_data, 0)
    end 'clear'
    
    // =========================================================================
    // Capacity Management
    // =========================================================================
    
    // Pre-allocate capacity for at least minCapacity elements
    export function reserve(minCapacity int)
        var cap = __array_capacity(_data)
        if minCapacity > cap 'grow'
            __array_grow(_data, minCapacity)
        end 'grow'
    end 'reserve'
    
    // =========================================================================
    // Iterable Interface
    // =========================================================================
    
    // Get next element in iteration, or nil when exhausted
    export function next() Element or nil
        var len = __array_len(_data)
        if _iterIndex >= len 'done'
            return nil
        end 'done'
        var value = _data[_iterIndex]
        _iterIndex = _iterIndex + 1
        return value
    end 'next'
    
end 'array'
```

### Phase 5: Special Considerations

#### 5.1 Generic Element Type in `__array_set_at`

The intrinsic `__array_set_at(arr, index, value)` needs to accept any element type. Options:

**Option A**: Special intrinsic parameter type
Add new variant to `IntrinsicParamDef`:
```cpp
static IntrinsicParamDef elementTypeOf(int argIndex) {
    IntrinsicParamDef p("");
    p.isElementTypeOfArg = true;
    p.elementTypeArgIndex = argIndex;
    return p;
}
```

**Option B**: Disable type checking for intrinsics with generic arrays
In semantic analyzer, when validating `__array_set_at`:
- Get element type from first argument's array type
- Validate third argument matches that element type

**Option C**: Make it untyped (accept any)
```cpp
{"__array_set_at", "void", {IntrinsicParamDef::arrayOf({}), {"int"}, IntrinsicParamDef::anyOf({})}, "intrinsic_array_set_at"},
```
Where `anyOf({})` with empty vector means "accept any type".

**Recommendation**: Option C is simplest. The stdlib code is trusted, and type safety is already enforced by the Maxon type system at the struct method level.

#### 5.2 Struct Field Access for `_data`

The intrinsics need to access `_data` field from within the `array` struct methods. When `self._data` is passed:
- It's a member access expression
- Codegen needs to resolve it to the actual array alloca/pointer

This should work with existing codegen for member access, but needs testing.

#### 5.3 Handling Implicit Self

In struct methods, `_data` alone should resolve to `self._data`. Check that `getManagedStringPtr` pattern works for arrays or add similar helper:

```cpp
mir::MIRValue *MIRCodeGenerator::getArrayPtr(ExprAST *arg) {
    // Handle implicit field access in struct methods
    // Similar to getManagedStringPtr
}
```

### Phase 6: Testing

#### 6.1 Create Spec File

Create `specs/stdlib-array.md`:

```markdown
---
title: Standard Library Array
category: stdlib
status: draft
---

# Developer Notes

The `array` struct wraps raw `[]Element` arrays and provides Swift-like methods.
It uses bridging intrinsics (`__array_*`) for low-level memory operations while
implementing high-level logic in Maxon.

## Architecture

- `_data []Element` - backing raw array
- `_iterIndex int` - iteration state for Iterable conformance
- Intrinsics handle: capacity read, length read/write, grow, element set, shift

## Memory Layout

Heap arrays: `[length:i32][capacity:i32][...data...]` at offsets -8, -4, 0+

# Documentation

## array<Element>

A generic, dynamically-sized array type.

### Creating Arrays

```maxon
var numbers = array<int>()
var names = array<string>()
```

### Properties

- `count() -> int` - Number of elements
- `capacity() -> int` - Current allocated capacity
- `isEmpty() -> bool` - True if count is 0
- `first -> Element or nil` - First element, or nil if empty
- `last -> Element or nil` - Last element, or nil if empty

### Element Access

- `get(index) -> Element or nil` - Element at index, or nil if out of bounds
- `set(index, value) -> Self` - Set element at index (no-op if out of bounds)

### Modifying

- `push(value)` - Append element to end
- `pop() -> Element or nil` - Remove and return last element
- `insert(at, value)` - Insert at index (clamps to valid range)
- `remove(at) -> Element or nil` - Remove and return element at index

### Capacity

- `reserve(minCapacity)` - Pre-allocate space for at least minCapacity elements

### Iteration

- `next() -> Element or nil` - Get next element (Iterable conformance)

# Tests

## test: basic-push-pop

```maxon
var arr = array<int>()
arr.push(1)
arr.push(2)
arr.push(3)
print(arr.count())  // 3

if let val = arr.pop() 'pop'
    print(val)  // 3
end 'pop'

print(arr.count())  // 2
```

## test: empty-pop-returns-nil

```maxon
var arr = array<int>()
if let val = arr.pop() 'pop'
    print("unexpected")
else 'pop'
    print("nil")  // nil
end 'pop'
```

## test: first-last

```maxon
var arr = array<int>()
arr.push(10)
arr.push(20)
arr.push(30)

if let f = arr.first() 'first'
    print(f)  // 10
end 'first'

if let l = arr.last() 'last'
    print(l)  // 30
end 'last'
```

## test: empty-first-last-nil

```maxon
var arr = array<int>()

if let f = arr.first() 'first'
    print("unexpected")
else 'first'
    print("nil")  // nil
end 'first'
```

## test: get-bounds

```maxon
var arr = array<int>()
arr.push(1)
arr.push(2)

if let v = arr.get(0) 'get'
    print(v)  // 1
end 'get'

if let v = arr.get(5) 'get'
    print("unexpected")
else 'get'
    print("nil")  // nil (out of bounds)
end 'get'

if let v = arr.get(-1) 'get'
    print("unexpected")
else 'get'
    print("nil")  // nil (negative index)
end 'get'
```

## test: insert-middle

```maxon
var arr = array<int>()
arr.push(1)
arr.push(3)
arr.insert(1, 2)  // Insert 2 at index 1

if let v0 = arr.get(0) 'g'
    print(v0)  // 1
end 'g'
if let v1 = arr.get(1) 'g'
    print(v1)  // 2
end 'g'
if let v2 = arr.get(2) 'g'
    print(v2)  // 3
end 'g'
print(arr.count())  // 3
```

## test: insert-clamp

```maxon
var arr = array<int>()
arr.insert(100, 5)  // Clamps to index 0 (empty array)
arr.insert(-5, 10)  // Clamps to index 0

if let v = arr.get(0) 'g'
    print(v)  // 10 (inserted at clamped position)
end 'g'
```

## test: remove-middle

```maxon
var arr = array<int>()
arr.push(1)
arr.push(2)
arr.push(3)

if let removed = arr.remove(1) 'rm'
    print(removed)  // 2
end 'rm'

print(arr.count())  // 2
if let v0 = arr.get(0) 'g'
    print(v0)  // 1
end 'g'
if let v1 = arr.get(1) 'g'
    print(v1)  // 3
end 'g'
```

## test: remove-invalid-returns-nil

```maxon
var arr = array<int>()
arr.push(1)

if let v = arr.remove(5) 'rm'
    print("unexpected")
else 'rm'
    print("nil")  // nil (out of bounds)
end 'rm'

if let v = arr.remove(-1) 'rm'
    print("unexpected")
else 'rm'
    print("nil")  // nil (negative index)
end 'rm'
```

## test: reserve-capacity

```maxon
var arr = array<int>()
arr.reserve(100)
print(arr.capacity() >= 100)  // true
print(arr.count())  // 0 (reserve doesn't add elements)
```

## test: clear

```maxon
var arr = array<int>()
arr.push(1)
arr.push(2)
arr.push(3)
arr.clear()
print(arr.count())  // 0
print(arr.isEmpty())  // true
```

## test: iteration

```maxon
var arr = array<int>()
arr.push(10)
arr.push(20)
arr.push(30)

for val in arr 'iter'
    print(val)  // 10, 20, 30
end 'iter'
```

## test: string-array

```maxon
var names = array<string>()
names.push("Alice")
names.push("Bob")

if let first = names.first() 'f'
    print(first)  // Alice
end 'f'
```
```

### Phase 7: Implementation Order

1. **Add `memmove` to runtime** (if not present)
2. **Add intrinsic definitions** to `intrinsics_defs.h`
3. **Declare intrinsic methods** in `codegen_mir.h`
4. **Register intrinsics** in `intrinsic_codegen.cpp`
5. **Implement intrinsic codegen** in `codegen_mir_intrinsics.cpp`:
   - `intrinsic_array_capacity`
   - `intrinsic_array_set_length`
   - `intrinsic_array_grow`
   - `intrinsic_array_set_at`
   - `intrinsic_array_shift_right`
   - `intrinsic_array_shift_left`
6. **Update semantic analyzer** for `__array_set_at` type checking (if needed)
7. **Update stdlib `array.maxon`** with full implementation
8. **Create spec file** `specs/stdlib-array.md`
9. **Extract and run tests**: `maxon extract-specs && maxon regen-fragments && make test`

### Files to Modify

| File | Changes |
|------|---------|
| `maxon-bin/intrinsics_defs.h` | Add 6 new array intrinsic definitions |
| `maxon-bin/codegen_mir.h` | Declare 6 new intrinsic_array_* methods |
| `maxon-bin/codegen_mir/intrinsic_codegen.cpp` | Register 6 new intrinsics |
| `maxon-bin/codegen_mir/codegen_mir_intrinsics.cpp` | Implement 6 new intrinsic codegen functions |
| `maxon-runtime/windows/runtime.mir` | Add `memmove` if not present |
| `maxon-runtime/linux/runtime.mir` | Add `memmove` if not present |
| `stdlib/collections/array.maxon` | Full Swift-like API implementation |
| `specs/stdlib-array.md` | New spec file with docs and tests |

### Potential Issues

1. **Generic element type for `__array_set_at`** — May need semantic analyzer changes
2. **Struct field access (`self._data`)** — Verify codegen handles member access correctly in intrinsic args
3. **`memmove` for overlapping regions** — Critical for insert/remove correctness
4. **Iterator state (`_iterIndex`)** — Needs reset mechanism for re-iteration (add `resetIterator()` method?)
5. **Copy semantics** — When array struct is copied, does `_data` reference same backing storage? May need CoW later.
