---
feature: stdlib-array
status: draft
keywords: array, generic, collection, stdlib, push, pop, insert, remove, iterator
category: stdlib
---

## Developer Notes

The stdlib `array<T>` struct provides a Swift-like generic array wrapper around Maxon's raw dynamic arrays. It uses bridging intrinsics for low-level memory operations while exposing a safe, ergonomic API.

**Status:** The struct definition and intrinsics are implemented, but generic struct instantiation syntax (`array<int>()`) is not yet supported by the parser. Tests will be enabled once that syntax is added.

**Bridging Intrinsics:**
- `__array_len(arr)` - get length from hidden alloca or heap header
- `__array_capacity(arr)` - get capacity from hidden alloca or heap header  
- `__array_set_length(arr, len)` - write new length
- `__array_grow(arr, minCapacity)` - grow array if needed (realloc + update)
- `__array_set_at(arr, index, value)` - write element without bounds check
- `__array_shift_right(arr, start, count)` - shift elements right (for insert)
- `__array_shift_left(arr, start, count)` - shift elements left (for remove)

**Memory Layout:**
- `_data` is a raw `[]T` dynamic array
- Heap arrays: `[length:i32][capacity:i32][...data...]` at offsets -8, -4, 0+
- The struct wraps this with safe methods returning optionals

**Implementation Notes:**
- All element access methods return `T or nil` for safety
- Bounds checking happens in Maxon code, not intrinsics
- `insert` clamps index to `[0, count]` instead of erroring
- `remove` returns `nil` for out-of-bounds indices
- Uses `memmove` for overlapping memory shifts (insert/remove)

**Implicit Self:**
- When `_data` is accessed in struct methods, codegen resolves it via `currentReceiverType`
- `getArrayFieldInfo()` helper handles implicit struct field access for all intrinsics

**Iterator:**
- Implements `Iterable` interface with `next()` method
- `_iterIndex` tracks position; resets for each `for` loop via Iterable protocol

**TODO:** Implement generic struct instantiation syntax in parser:
- Parse `array<int>()` as generic struct constructor
- Support `array<string>()`, `array<Point>()`, etc.

## Documentation

# stdlib array<T>

The `array<T>` struct provides a safe, generic array with Swift-like API. All element access operations return optionals to handle out-of-bounds access safely.

**Note:** Generic struct instantiation syntax is not yet implemented. The documentation below describes the intended API.

## Creating Arrays

```maxon
var nums = array<int>()      // Empty array of ints
var names = array<string>()  // Empty array of strings
```

## Properties

```maxon
var arr = array<int>()
arr.push(1)
arr.push(2)

print(arr.count())     // 2
print(arr.capacity())  // >= 2
print(arr.isEmpty())   // false
```

## Element Access

Use `get()` for safe access with bounds checking:

```maxon
var arr = array<int>()
arr.push(10)
arr.push(20)

if let val = arr.get(0) 'g'
    print(val)  // 10
end 'g'

if let val = arr.get(99) 'g'
    print(val)
else 'g'
    print("out of bounds")  // prints this
end 'g'
```

Use `first()` and `last()` for convenient access:

```maxon
if let f = arr.first() 'f'
    print(f)  // First element
end 'f'

if let l = arr.last() 'l'
    print(l)  // Last element
end 'l'
```

## Mutating Operations

```maxon
var arr = array<int>()

// Add elements
arr.push(1)
arr.push(2)
arr.push(3)

// Remove last element
if let val = arr.pop() 'p'
    print(val)  // 3
end 'p'

// Insert at position
arr.insert(1, 99)  // Insert 99 at index 1

// Remove at position
if let removed = arr.remove(1) 'r'
    print(removed)  // 99
end 'r'

// Clear all elements
arr.clear()
```

## Iteration

```maxon
var arr = array<int>()
arr.push(10)
arr.push(20)
arr.push(30)

for val in arr 'iter'
    print(val)
end 'iter'
// Output: 10, 20, 30
```

## Tests

Tests are commented out until generic struct instantiation syntax is implemented.

<!--
The following tests will be enabled once `array<T>()` syntax is supported:

test: push-pop-basic
```maxon
function main() int
    var arr = array<int>()
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    var sum = 0
    if let a = arr.pop() 'p'
        sum = sum + a
    end 'p'
    if let b = arr.pop() 'p'
        sum = sum + b
    end 'p'
    if let c = arr.pop() 'p'
        sum = sum + c
    end 'p'
    return sum
end 'main'
```
Expected: exitcode 60

test: count-and-isEmpty
```maxon
function main() int
    var arr = array<int>()
    var result = 0
    if arr.isEmpty() 'e'
        result = result + 1
    end 'e'
    arr.push(42)
    if arr.count() == 1 'c'
        result = result + 10
    end 'c'
    if not arr.isEmpty() 'e'
        result = result + 100
    end 'e'
    return result
end 'main'
```
Expected: exitcode 111

test: first-last
```maxon
function main() int
    var arr = array<int>()
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    var result = 0
    if let f = arr.first() 'f'
        result = result + f
    end 'f'
    if let l = arr.last() 'l'
        result = result + l
    end 'l'
    return result
end 'main'
```
Expected: exitcode 40

test: get-bounds
```maxon
function main() int
    var arr = array<int>()
    arr.push(5)
    arr.push(15)
    arr.push(25)
    
    var sum = 0
    if let v = arr.get(0) 'g'
        sum = sum + v
    end 'g'
    if let v = arr.get(1) 'g'
        sum = sum + v
    end 'g'
    if let v = arr.get(2) 'g'
        sum = sum + v
    end 'g'
    return sum
end 'main'
```
Expected: exitcode 45

test: insert-middle
```maxon
function main() int
    var arr = array<int>()
    arr.push(1)
    arr.push(3)
    arr.insert(1, 2)
    
    var result = 0
    if let v = arr.get(0) 'g'
        if v == 1 'c'
            result = result + 1
        end 'c'
    end 'g'
    if let v = arr.get(1) 'g'
        if v == 2 'c'
            result = result + 10
        end 'c'
    end 'g'
    if let v = arr.get(2) 'g'
        if v == 3 'c'
            result = result + 100
        end 'c'
    end 'g'
    if arr.count() == 3 'c'
        result = result + 1000
    end 'c'
    return result
end 'main'
```
Expected: exitcode 1111

test: remove-middle
```maxon
function main() int
    var arr = array<int>()
    arr.push(1)
    arr.push(2)
    arr.push(3)
    
    var result = 0
    if let removed = arr.remove(1) 'r'
        result = removed
    end 'r'
    
    if arr.count() == 2 'c'
        result = result + 100
    end 'c'
    
    return result
end 'main'
```
Expected: exitcode 102

test: clear
```maxon
function main() int
    var arr = array<int>()
    arr.push(1)
    arr.push(2)
    arr.push(3)
    arr.clear()
    
    var result = 0
    if arr.count() == 0 'c'
        result = result + 1
    end 'c'
    if arr.isEmpty() 'e'
        result = result + 10
    end 'e'
    return result
end 'main'
```
Expected: exitcode 11

test: iteration
```maxon
function main() int
    var arr = array<int>()
    arr.push(10)
    arr.push(20)
    arr.push(30)
    
    var sum = 0
    for val in arr 'iter'
        sum = sum + val
    end 'iter'
    return sum
end 'main'
```
Expected: exitcode 60
-->
