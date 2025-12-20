---
feature: arrays
status: stable
keywords: [arrays, indexing, array literal, index access, index assignment, sized array, push, count, length]
category: types
---

## Developer Notes

Arrays are contiguous sequences of elements of the same type. The implementation supports:

1. **Mutable array literals** - `var arr = [1, 2, 3]` creates a stack-allocated mutable array
2. **Immutable array literals** - `let arr = [1, 2, 3]` creates a read-only array (currently stack-allocated, future: data section)
3. **Sized arrays** - `var arr = array of N T` creates a fixed-size array (currently stack-allocated, future: heap for large arrays)
4. **Index access** - `arr[i]` reads element at index i
5. **Index assignment** - `arr[i] = value` writes to element at index i (mutable arrays only)

### AST Nodes

- `ArrayLiteralExpr` - holds slice of element expressions
- `IndexExpr` - base expression and index expression
- `SizedArrayExpr` - size expression and element type name
- `IndexAssign` - statement for `arr[i] = value`

### IR Instructions

- `alloca_sized` - allocates stack space for the array
- `getelemptr` - calculates pointer to element: base + (index * element_size)
- `store` - writes value to element pointer
- `load` - reads value from element pointer

### Codegen

All elements are 8 bytes (i64, f64, or ptr). The `getelemptr` instruction generates:
```text
; Load base address to RAX
lea rax, [rbp+base_offset]
; Load index to RCX
mov rcx, [rbp+index_offset]
; Multiply index by 8 (element size)
shl rcx, 3
; Add to get element address
add rax, rcx
```

### Optimizer Considerations

Dead store elimination must preserve stores to array elements. The optimizer tracks `getelemptr` base pointers to ensure that if any element of an array is loaded, all stores to that array are preserved.

### Array Methods

#### push(value)

The `push` method appends an element to a dynamic array, growing it by one element. This requires:

1. Tracking the current size in a companion `_size` variable
2. Reallocating the array to accommodate one more element
3. Storing the new element at the end
4. Updating the size variable

Implementation details:
- Arrays using `push` must have a companion size variable: `var arr_size = 0`
- `arr.push(value)` reallocates to `(size + 1) * 8` bytes
- The new pointer is stored back to the array's stack slot
- Element is stored at offset `old_size * 8`

Limitations:
- Requires manual size tracking via `arr_size` variable
- Cannot push to fixed-size arrays (compile error)

#### count()

The `count()` method returns the number of elements in an array. It works on both:

1. **Fixed-size arrays** - size is known at compile time, returns a constant
2. **Dynamic arrays** - reads from the companion `_size` variable

Implementation details:
- For fixed-size arrays: `arr_info.size` contains the compile-time size
- For dynamic arrays: loads from `{name}_size` variable
- Returns an `int` value that can be used in expressions

Limitations:
- Dynamic arrays require a companion size variable `{name}_size`

## Documentation

# Arrays

Arrays are ordered collections of elements of the same type.

## Mutable Array Literals

Create a mutable array using `var` with square brackets:

```text
var numbers = [10, 20, 30]
numbers[0] = 100  // Can modify elements
```

## Immutable Array Literals

Create an immutable array using `let` with square brackets:

```text
let constants = [10, 20, 30]
var x = constants[1]  // Can read elements
// constants[0] = 5   // Error: cannot modify immutable array
```

## Sized Arrays

Create a fixed-size array using `array of N T` syntax:

```text
var buffer = array of 10 int
buffer[0] = 42
buffer[1] = 100
```

## Index Access

Access array elements using square bracket notation with a zero-based index:

```text
var arr = [10, 20, 30]
var first = arr[0]   // 10
var second = arr[1]  // 20
var third = arr[2]   // 30
```

## Index Assignment

Modify mutable array elements using index assignment:

```text
var arr = [10, 20, 30]
arr[0] = 100
arr[1] = 200
```

## Push Method

Append elements to dynamic arrays:

```text
var arr_size = 0
var arr = array of arr_size int
arr.push(42)
arr.push(100)
```

Requirements:
- Array must be declared with `var` (mutable)
- A companion size variable `{name}_size` must exist
- The array must be heap-allocated (dynamic size)

## Count Method

Get the number of elements in an array:

```text
let arr = [1, 2, 3]
let size = arr.count()  // returns 3
```

Works with:
- Fixed-size array literals: `[1, 2, 3]`
- Fixed-size arrays: `array of 5 int`
- Dynamic arrays: `array of n int` (requires `n_size` variable)

## Tests

<!-- test: literal-first -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return arr[0]
end 'main'
```
```exitcode
10
```

<!-- test: literal-middle -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: literal-last -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return arr[2]
end 'main'
```
```exitcode
30
```

<!-- test: five-elements -->
```maxon
function main() returns int
    var arr = [5, 10, 15, 20, 25]
    return arr[4]
end 'main'
```
```exitcode
25
```

<!-- test: index-assignment -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    arr[0] = 100
    return arr[0]
end 'main'
```
```exitcode
100
```

<!-- test: assignment-middle -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    arr[1] = 42
    return arr[1]
end 'main'
```
```exitcode
42
```

<!-- test: assignment-last -->
```maxon
function main() returns int
    var arr = [1, 2, 3, 4, 5]
    arr[4] = 99
    return arr[4]
end 'main'
```
```exitcode
99
```

<!-- test: multiple-access -->
```maxon
function main() returns int
    var arr = [5, 10, 15, 20, 25]
    return arr[2] + arr[4]
end 'main'
```
```exitcode
40
```

<!-- test: assignment-preserves-others -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    arr[0] = 100
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: multiple-assignments -->
```maxon
function main() returns int
    var arr = [0, 0, 0]
    arr[0] = 1
    arr[1] = 2
    arr[2] = 3
    return arr[0] + arr[1] + arr[2]
end 'main'
```
```exitcode
6
```

<!-- test: let-array-first -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return arr[0]
end 'main'
```
```exitcode
10
```

<!-- test: let-array-middle -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: let-array-last -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return arr[2]
end 'main'
```
```exitcode
30
```

<!-- test: let-array-multiple-access -->
```maxon
function main() returns int
    let arr = [5, 10, 15, 20]
    return arr[0] + arr[3]
end 'main'
```
```exitcode
25
```

<!-- test: sized-array-write-read -->
```maxon
function main() returns int
    var arr = array of 5 int
    arr[0] = 42
    return arr[0]
end 'main'
```
```exitcode
42
```

<!-- test: sized-array-multiple -->
```maxon
function main() returns int
    var arr = array of 3 int
    arr[0] = 10
    arr[1] = 20
    arr[2] = 30
    return arr[1]
end 'main'
```
```exitcode
20
```

<!-- test: sized-array-sum -->
```maxon
function main() returns int
    var arr = array of 4 int
    arr[0] = 1
    arr[1] = 2
    arr[2] = 3
    arr[3] = 4
    return arr[0] + arr[1] + arr[2] + arr[3]
end 'main'
```
```exitcode
10
```

## Push Method Tests

<!-- test: push-single-element -->
Push a single element to an empty array.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    arr.push(42)
    return arr[0]
end 'main'
```
```exitcode
42
```

<!-- test: push-multiple-elements -->
Push multiple elements and access them.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    arr.push(10)
    arr.push(20)
    arr.push(30)
    return arr[0] + arr[1] + arr[2]
end 'main'
```
```exitcode
60
```

<!-- test: push-to-existing-array -->
Push to an array that already has elements.

```maxon
function main() returns int
    var arr_size = 2
    var arr = array of arr_size int
    arr[0] = 5
    arr[1] = 10
    arr.push(15)
    return arr[0] + arr[1] + arr[2]
end 'main'
```
```exitcode
30
```

<!-- test: push-return-size -->
Verify size variable is updated after push.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    arr.push(1)
    arr.push(2)
    arr.push(3)
    return arr_size
end 'main'
```
```exitcode
3
```

<!-- test: push-in-loop -->
Push elements in a loop.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    var i = 0
    while i < 3 'loop'
        arr.push(i * 10)
        i = i + 1
    end 'loop'
    return arr[0] + arr[1] + arr[2]
end 'main'
```
```exitcode
30
```

<!-- test: push-single-tracked -->
<!-- TrackAllocs: true -->
Push a single element with allocation tracking.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    arr.push(42)
    return arr[0]
end 'main'
```
```exitcode
42
```
```stdout
ALLOC #1: 8 bytes (dynamic array)
FREE #1: 8 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 8 bytes
Freed:     8 bytes
Leaked:    0 bytes
```

<!-- test: push-to-existing-tracked -->
<!-- TrackAllocs: true -->
Push to existing array with allocation tracking.

```maxon
function main() returns int
    var arr_size = 2
    var arr = array of arr_size int
    arr[0] = 5
    arr[1] = 10
    arr.push(15)
    return arr[0] + arr[1] + arr[2]
end 'main'
```
```exitcode
30
```
```stdout
ALLOC #1: 16 bytes (dynamic array)
REALLOC #1: 16 -> 24 bytes (dynamic array)
FREE #1: 24 bytes (dynamic array)

=== ALLOC STATS ===
Allocated: 40 bytes
Freed:     40 bytes
Leaked:    0 bytes
```

## Count Method Tests

<!-- test: count-fixed-array-literal -->
Count elements in a fixed-size array literal.

```maxon
function main() returns int
    let arr = [10, 20, 30]
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: count-fixed-array -->
Count elements in a fixed-size array declaration.

```maxon
function main() returns int
    var arr = array of 5 int
    return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: count-dynamic-array -->
Count elements in a dynamic array.

```maxon
function main() returns int
    var arr_size = 3
    var arr = array of arr_size int
    return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: count-after-push -->
Count elements after pushing to dynamic array.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    arr.push(10)
    arr.push(20)
    return arr.count()
end 'main'
```
```exitcode
2
```

<!-- test: count-in-expression -->
Use count() in an expression.

```maxon
function main() returns int
    let arr = [1, 2, 3, 4]
    return arr.count() * 10
end 'main'
```
```exitcode
40
```

<!-- test: count-empty-dynamic-array -->
Count on empty dynamic array.

```maxon
function main() returns int
    var arr_size = 0
    var arr = array of arr_size int
    return arr.count()
end 'main'
```
```exitcode
0
```

## Error Cases

<!-- test: error.let-array-element-assign -->
Immutable arrays cannot have their elements modified.

```maxon
function main() returns int
    let arr = [1, 2, 3]
    arr[0] = 42
    return arr[0]
end 'main'
```
```maxoncstderr
error E009: specs\fragments\arrays.error.let-array-element-assign.1.test:2:1: cannot assign to immutable variable
```

<!-- test: error.let-sized-array-invalid -->
Sized arrays must be mutable since they have no initial contents.

```maxon
function main() returns int
    let arr = array of 5 int
    return 0
end 'main'
```
```maxoncstderr
error E011: specs\fragments\arrays.error.let-sized-array-invalid.1.test:1:1: wrong argument count
```
