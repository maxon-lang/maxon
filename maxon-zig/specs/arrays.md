---
feature: arrays
status: stable
keywords: [arrays, indexing, array literal, index access, index assignment, sized array]
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
error E009: specs\fragments\arrays.error.let-array-element-assign.1.test:2:1: cannot assign to immutable array element
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
error E011: specs\fragments\arrays.error.let-sized-array-invalid.1.test:1:1: sized array must be mutable
```
