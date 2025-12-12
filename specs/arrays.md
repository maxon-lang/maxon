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
- Type inferred from values: `array of 3 int`
- Stack-allocated, no heap allocation
- Immutable: elements cannot be modified after creation
- No automatic cleanup needed (stack memory)
- `.count()` returns the length (compile-time constant)

**Dynamic Arrays (var):**
- Declared with `var` using value literals: `var arr = [1, 2, 3]`
- Or using sized syntax: `var arr = array of 5 int`
- Heap-allocated with capacity tracking
- Mutable: elements can be modified
- Growable via `push()` method
- Automatic deallocation at end of scope
- `.count()` and `.capacity()` methods

**Type System:**
- Static: `_StaticArray<N,T>` internally, displayed as `array of N T` (e.g., `array of 3 int`)
- Dynamic: `_ManagedArray<T>` internally, displayed as `array of T`
- Static and dynamic are incompatible types (no implicit conversion)
- Function parameters:
  - `fn(arr array of 3 int)` - accepts static array of exactly 3 ints (by reference)
  - `fn(arr array of int)` - accepts dynamic array (by reference, ptr+len+cap)

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
- `arr[i] = val` on let array → error (immutable)

## Documentation

# Arrays

Maxon has two types of arrays: **static arrays** (immutable, stack-allocated) and **dynamic arrays** (mutable, heap-allocated, growable).

## Static Arrays

Static arrays are declared with `let` using value literals. They are stack-allocated, immutable, and their size is part of the type.

```maxon
let arr = [1, 2, 3]     // Static array of type array of 3 int
let x = arr[0]          // Read element: OK
// arr[0] = 10          // ERROR: static arrays are immutable
```

**Properties:**
- Type is inferred from values: `[1, 2, 3]` has type `array of 3 int`
- Elements cannot be modified after creation
- `.count()` returns the length (compile-time constant)
- No heap allocation, very efficient

## Dynamic Arrays

Dynamic arrays are declared with `var`. They are heap-allocated, mutable, and can grow.

```maxon
var arr = array of 5 int  // Dynamic array with 5 zeros, capacity 5
var vals = [10, 20, 30]   // Dynamic array with values, capacity 3
var empty = array of int  // Empty dynamic array, capacity 0
```

**Properties:**
- Mutable: elements can be read and written
- Growable: use `.push()` to add elements
- `.count()` returns current number of elements
- `.capacity()` returns allocated capacity
- Automatic cleanup at end of scope

**Methods (via stdlib):**
```maxon
var arr = [1, 2, 3]
arr.push(4)             // Add element, grow if needed
var last = arr.pop()    // Remove and return last element
var cap = arr.capacity()  // Get capacity
arr.reserve(100)        // Preallocate space
```

## Function Parameters

**Static array parameters** require the exact size in the type:
```maxon
function process(arr array of 3 int) returns int
    return arr[0] + arr[1] + arr[2]
end 'process'

let data = [10, 20, 30]
return process(data)    // OK: array of 3 int matches array of 3 int
```

**Dynamic array parameters** use unsized syntax:
```maxon
function sum(arr array of int) returns int
    var total = 0
    var i = 0
    while i < arr.count() 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum'

var data = [1, 2, 3, 4, 5]
return sum(data)        // OK: passes ptr+len+cap
```

**Note:** Static and dynamic arrays are incompatible types. A static `array of 3 int` cannot be passed to a function expecting `array of int`.

## Tests

<!-- test: static.basic -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: static.length -->
```maxon
function main() returns int
    let arr = [1, 2, 3, 4, 5]
    return arr.length
end 'main'
```
```exitcode
5
```

<!-- test: dynamic.basic -->
```maxon
function main() returns int
    var arr = array of 5 int
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
function main() returns int
    var values = [10, 20, 30]
    values[0] = 100
    return values[0]
end 'main'
```
```exitcode
100
```

<!-- test: dynamic.push-pop -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    arr.push(4)
    arr.push(5)
    var count_after_push = arr.count()
    var popped = arr.pop() else 'unwrap'
        popped = 0
    end 'unwrap'
    var count_after_pop = arr.count()
    return count_after_push * 100 + popped * 10 + count_after_pop
end 'main'
```
```exitcode
554
```

<!-- test: dynamic.push-to-empty -->
```maxon
function main() returns int
    var arr = array of int
    arr.push(10)
    arr.push(20)
    arr.push(30)
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: dynamic.length -->
```maxon
function main() returns int
    var arr = array of 5 int
    return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: dynamic.float -->
```maxon
function main() returns int
    var arr = array of 3 float
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
function main() returns int
    var arr = array of 5 int
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
function sum_array(arr array of int, len int) returns int
    var total = 0
    var i = 0
    while i < len 'loop'
        total = total + arr[i]
        i = i + 1
    end 'loop'
    return total
end 'sum_array'

function main() returns int
    var data = [5, 10, 15]
    return sum_array(data, 3)
end 'main'
```
```exitcode
30
```

<!-- test: dynamic.unsized-parameter -->
```maxon
function get_first(arr array of int) returns int
    return arr[0]
end 'get_first'

function main() returns int
    var nums = [42, 10, 20, 30]
    return get_first(nums)
end 'main'
```
```exitcode
42
```

<!-- test: error.static-array-assignment -->
```maxon
function main() returns int
    let arr = [1, 2, 3]
    arr[0] = 10
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 5
Cannot assign to read-only array 'arr'
  Array declared with 'let' at line 3, column 5
  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable arrays

  4 |     arr[0] = 10
    |     ^
```

<!-- test: error.push-on-static-array -->
```maxon
function main() returns int
    let arr = [1, 2, 3]
    arr.push(4)
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 14
Function 'push' argument type mismatch
  Parameter 2 ('value')
  Expected type: array<int>
  Found type: int

  4 |     arr.push(4)
    |              ^
```

<!-- test: error.pop-on-static-array -->
```maxon
function main() returns int
    let arr = [1, 2, 3]
    arr.pop()
    return 0
end 'main'
```
```maxoncstderr
Error in function 'main': Unknown function referenced: array.pop at line 4, column 5
```

<!-- test: error.push-type-mismatch -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    arr.push(3.14)
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 14
Function 'push' argument type mismatch
  Parameter 2 ('value')
  Expected type: int
  Found type: float

  4 |     arr.push(3.14)
    |              ^
```

<!-- test: error.non-integer-index -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    return arr[1.5]
end 'main'
```
```maxoncstderr
Semantic Error: line 4, column 12
Array index must be an integer
  Found type: float

  4 |     return arr[1.5]
    |            ^
```
