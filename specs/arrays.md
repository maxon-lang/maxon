---
feature: arrays
status: stable
keywords: array, collection, indexing, memory
category: type-system
---

## Developer Notes

Arrays are fixed-size, heap-allocated collections with automatic memory management.

**Implementation Details:**
- Syntax: `[size]type` for fixed-size arrays
- Syntax: `[]type` for unsized (function parameter) arrays
- Value initialization: `[val1, val2, val3]`
- Parser: `parseArrayType()` and `parseArrayLiteral()` in parser.cpp
- AST nodes: `ArrayTypeAST`, `ArrayLiteralExprAST`

**Memory Management:**
- Heap allocated using Windows HeapAlloc
- Automatic deallocation at end of scope (scope-based cleanup)
- Array pointer stored on stack
- Cleanup code generated at scope exit

**Type System:**
- Fixed-size: `[10]int` - array of 10 integers
- Unsized: `[]int` - for function parameters (size unknown)
- Element type can be any Maxon type (int, float, struct, etc.)
- Arrays have `.length` property

**Indexing:**
- Zero-based indexing
- No bounds checking at runtime (undefined behavior for out-of-bounds)
- Index must be integer expression

## Documentation

# Arrays

Fixed-size, heap-allocated collections with automatic memory management.

**Syntax:**

Fixed-size array:
```maxon
var arr = [5]int        // Array of 5 integers
```
Value-initialized array:
```maxon
var values = [10, 20, 30]  // Array with initial values
```
Array parameter (unsized):
```maxon
function process(arr []int) int
    return arr.length
end 'process'
```
**Example:**

```maxon
var arr = [5]int        // Create array of 5 integers
arr[0] = 10             // Set first element
arr[1] = 20             // Set second element
return arr[1]           // Returns 20
```
**Example with length:**

```maxon
var values = [1, 2, 3, 4, 5]
return values.length    // Returns 5
```
**Notes:**
- Arrays are zero-indexed
- All arrays are heap-allocated
- Memory is automatically freed at end of scope
- Arrays have a `.length` property
- Fixed-size arrays must specify size: `[10]int`
- Function parameters use unsized syntax: `[]int`

## Tests

<!-- test: arrays.basic -->
```maxon
// Test new array syntax
function main() int
    var arr = [5]int
    arr[0] = 10
    arr[1] = 20
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: arrays.values -->
```maxon
function main() int
    var values = [10, 20, 30]
    return values[1]
end 'main'
```
```exitcode
20
```

<!-- test: arrays.length -->
```maxon
function main() int
    var arr = [5]int
    return arr.length
end 'main'
```
```exitcode
5
```

<!-- test: arrays.value-length -->
```maxon
function main() int
    var values = [1, 2, 3, 4, 5]
    return values.length
end 'main'
```
```exitcode
5
```

<!-- test: arrays.float -->
```maxon
function main() int
    var arr = [3]float
    arr[0] = 1.5
    arr[1] = 2.5
    arr[2] = 3.5
    var sum = arr[0] + arr[1] + arr[2]
    return sum as int
end 'main'
```
```exitcode
7
```


<!-- test: heap-allocation -->
```maxon
function main() int
    var arr = [5]int
    arr[0] = 10
    arr[1] = 20
    arr[2] = 30
    var sum = arr[0] + arr[1] + arr[2]
    return sum
end 'main'
```
```exitcode
60
```


<!-- test: function-parameter -->
```maxon
function sum_array(arr []int, len int) int
    var total = 0
    var i = 0
    while i < len 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum_array'

function main() int
    var data = [3]int
    data[0] = 5
    data[1] = 10
    data[2] = 15
    return sum_array(data, 3)
end 'main'
```
```exitcode
30
```


<!-- test: unsized-parameter -->
```maxon
function get_first(arr []int) int
    return arr[0]
end 'get_first'

function main() int
    var nums = [4]int
    nums[0] = 42
    nums[1] = 10
    nums[2] = 20
    nums[3] = 30
    return get_first(nums)
end 'main'
```
```exitcode
42
```

