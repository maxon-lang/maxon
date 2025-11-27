---
feature: arrays
status: stable
keywords: array, collection, indexing, memory, static, dynamic
category: type-system
---

## Developer Notes

Arrays come in two forms: **static** (stack-allocated, immutable) and **dynamic** (heap-allocated, growable).

**Static Arrays (let):**
- Declared with `let` and value literals: `let arr = [1, 2, 3]`
- Type inferred from values: `[3]int`
- Stack-allocated, no heap allocation
- Immutable: elements cannot be modified after creation
- No automatic cleanup needed (stack memory)
- `.length` is a compile-time constant

**Dynamic Arrays (var):**
- Declared with `var`: `var arr = [5]int` or `var arr = [1, 2, 3]`
- Heap-allocated with capacity tracking
- Mutable: elements can be modified
- Growable via `push()` method
- Automatic deallocation at end of scope
- `.length` and `.capacity` properties

**Type System:**
- Static: `[N]type` - size is part of the type (e.g., `[3]int`)
- Dynamic: `[]type` - size not part of type, tracked at runtime
- Static and dynamic are incompatible types (no implicit conversion)
- Function parameters:
  - `fn(arr [3]int)` - accepts static array of exactly 3 ints (by reference)
  - `fn(arr []int)` - accepts dynamic array (by reference, ptr+len+cap)

**Method Syntax:**
- `arr.push(val)` transforms to `push(arr, val)` at parse time
- Methods implemented in stdlib for dynamic arrays only

**Memory Layout:**
- Static: just the array data on stack
- Dynamic: pointer to heap data, plus `__length` and `__capacity` on stack

**Parser:**
- `parseArrayType()` and `parseArrayLiteral()` in parser.cpp
- Method calls: `expr.method(args)` → `method(expr, args)`

**AST nodes:** `ArrayTypeAST`, `ArrayLiteralExprAST`

**Semantic Checks:**
- `let arr = [5]int` → error (let requires value literal)
- `arr[i] = val` on let array → error (immutable)

## Documentation

# Arrays

Maxon has two types of arrays: **static arrays** (immutable, stack-allocated) and **dynamic arrays** (mutable, heap-allocated, growable).

## Static Arrays

Static arrays are declared with `let` using value literals. They are stack-allocated, immutable, and their size is part of the type.

```maxon
let arr = [1, 2, 3]     // Static array of type [3]int
let x = arr[0]          // Read element: OK
// arr[0] = 10          // ERROR: static arrays are immutable
```

**Properties:**
- Type is inferred from values: `[1, 2, 3]` has type `[3]int`
- Elements cannot be modified after creation
- `.length` is a compile-time constant
- No heap allocation, very efficient

## Dynamic Arrays

Dynamic arrays are declared with `var`. They are heap-allocated, mutable, and can grow.

```maxon
var arr = [5]int        // Dynamic array with 5 zeros, capacity 5
var vals = [10, 20, 30] // Dynamic array with values, capacity 3
var empty = []int       // Empty dynamic array, capacity 0
```

**Properties:**
- Mutable: elements can be read and written
- Growable: use `.push()` to add elements
- `.length` returns current number of elements
- `.capacity` returns allocated capacity
- Automatic cleanup at end of scope

**Methods (via stdlib):**
```maxon
var arr = [1, 2, 3]
arr.push(4)             // Add element, grow if needed
var last = arr.pop()    // Remove and return last element
var cap = arr.capacity  // Get capacity
arr.reserve(100)        // Preallocate space
```

## Function Parameters

**Static array parameters** require the exact size in the type:
```maxon
function process(arr [3]int) int
    return arr[0] + arr[1] + arr[2]
end 'process'

let data = [10, 20, 30]
return process(data)    // OK: [3]int matches [3]int
```

**Dynamic array parameters** use unsized syntax:
```maxon
function sum(arr []int) int
    var total = 0
    var i = 0
    while i < arr.length 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum'

var data = [1, 2, 3, 4, 5]
return sum(data)        // OK: passes ptr+len+cap
```

**Note:** Static and dynamic arrays are incompatible types. A static `[3]int` cannot be passed to a function expecting `[]int`.

## Tests

<!-- test: static.basic -->
```maxon
function main() int
    let arr = [10, 20, 30]
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: static.length -->
```maxon
function main() int
    let arr = [1, 2, 3, 4, 5]
    return arr.length
end 'main'
```
```exitcode
5
```

<!-- test: dynamic.basic -->
```maxon
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

<!-- test: dynamic.values -->
```maxon
function main() int
    var values = [10, 20, 30]
    values[0] = 100
    return values[0]
end 'main'
```
```exitcode
100
```

<!-- test: dynamic.length -->
```maxon
function main() int
    var arr = [5]int
    return arr.length
end 'main'
```
```exitcode
5
```

<!-- test: dynamic.float -->
```maxon
function main() int
    var arr = [3]float
    arr[0] = 1.5
    arr[1] = 2.5
    arr[2] = 3.5
    var sum = arr[0] + arr[1] + arr[2]
    return trunc(sum)
end 'main'
```
```exitcode
7
```

<!-- test: dynamic.heap-allocation -->
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

<!-- test: dynamic.function-parameter -->
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

<!-- test: dynamic.unsized-parameter -->
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
