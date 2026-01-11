---
feature: arrays
status: stable
keywords: [arrays, array literal, get, set, sized array]
category: types
---
# Arrays

## Documentation

Arrays are ordered collections of elements of the same type.

## Mutable Array Literals

Create a mutable array using `var` with square brackets:

```text
var numbers = [10, 20, 30]
numbers.set(index: 0, value: 100)  // Can modify elements
```

## Immutable Array Literals

Create an immutable array using `let` with square brackets:

```text
let constants = [10, 20, 30]
var x = try constants.get(index: 1) otherwise 0  // Can read elements
```

## Sized Arrays

Create a fixed-size array using `Array of N T` syntax:

```text
var buffer = Array of 10 int
buffer.set(index: 0, value: 42)
buffer.set(index: 1, value: 100)
```

## Element Access

Access array elements using the `.get(index:)` method with a zero-based index.
The method returns an optional value (throws on out of bounds), so use `try ... otherwise default`:

```text
var arr = [10, 20, 30]
var first = try arr.get(index: 0) otherwise 0   // 10
var second = try arr.get(index: 1) otherwise 0  // 20
var third = try arr.get(index: 2) otherwise 0   // 30
```

## Element Assignment

Modify mutable array elements using the `.set(index:, value:)` method:

```text
var arr = [10, 20, 30]
arr.set(index: 0, value: 100)
arr.set(index: 1, value: 200)
```

## Tests

<!-- test: literal-first -->
```maxon
function main() returns int
    return try [10, 20, 30].get(index: 0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: literal-middle -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return try arr.get(index: 1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: literal-last -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    return try arr.get(index: 2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: five-elements -->
```maxon
function main() returns int
    var arr = [5, 10, 15, 20, 25]
    return try arr.get(index: 4) otherwise 0
end 'main'
```
```exitcode
25
```

<!-- test: index-assignment -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    arr.set(index: 0, value: 100)
    return try arr.get(index: 0) otherwise 0
end 'main'
```
```exitcode
100
```

<!-- test: assignment-middle -->
```maxon
function main() returns int
    var arr = [1, 2, 3]
    arr.set(index: 1, value: 42)
    return try arr.get(index: 1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: assignment-last -->
```maxon
function main() returns int
    var arr = [1, 2, 3, 4, 5]
    arr.set(index: 4, value: 99)
    return try arr.get(index: 4) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: multiple-access -->
```maxon
function main() returns int
    var arr = [5, 10, 15, 20, 25]
    var a = try arr.get(index: 2) otherwise 0
    var b = try arr.get(index: 4) otherwise 0
    return a + b
end 'main'
```
```exitcode
40
```

<!-- test: assignment-preserves-others -->
```maxon
function main() returns int
    var arr = [10, 20, 30]
    arr.set(index: 0, value: 100)
    return try arr.get(index: 1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: multiple-assignments -->
```maxon
function main() returns int
    var arr = [0, 0, 0]
    arr.set(index: 0, value: 1)
    arr.set(index: 1, value: 2)
    arr.set(index: 2, value: 3)
    var a = try arr.get(index: 0) otherwise 0
    var b = try arr.get(index: 1) otherwise 0
    var c = try arr.get(index: 2) otherwise 0
    return a + b + c
end 'main'
```
```exitcode
6
```

<!-- test: let-array-first -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return try arr.get(index: 0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: let-array-middle -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return try arr.get(index: 1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: let-array-last -->
```maxon
function main() returns int
    let arr = [10, 20, 30]
    return try arr.get(index: 2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: let-array-multiple-access -->
```maxon
function main() returns int
    let arr = [5, 10, 15, 20]
    var a = try arr.get(index: 0) otherwise 0
    var b = try arr.get(index: 3) otherwise 0
    return a + b
end 'main'
```
```exitcode
25
```

<!-- test: sized-array-write-read -->
```maxon
function main() returns int
    var arr = Array of 5 int
    arr.set(index: 0, value: 42)
    return try arr.get(index: 0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: sized-array-multiple -->
```maxon
function main() returns int
    var arr = Array of 3 int
    arr.set(index: 0, value: 10)
    arr.set(index: 1, value: 20)
    arr.set(index: 2, value: 30)
    return try arr.get(index: 1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: sized-array-sum -->
```maxon
function main() returns int
    var arr = Array of 4 int
    arr.set(index: 0, value: 1)
    arr.set(index: 1, value: 2)
    arr.set(index: 2, value: 3)
    arr.set(index: 3, value: 4)
    var a = try arr.get(index: 0) otherwise 0
    var b = try arr.get(index: 1) otherwise 0
    var c = try arr.get(index: 2) otherwise 0
    var d = try arr.get(index: 3) otherwise 0
    return a + b + c + d
end 'main'
```
```exitcode
10
```

## Error Cases

<!-- test: error.let-sized-array-invalid -->
Sized arrays must be mutable since they have no initial contents.

```maxon
function main() returns int
    let arr = Array of 5 int
    return 0
end 'main'
```
```maxoncstderr
error E013: specs/fragments/arrays.error.let-sized-array-invalid.1.test:3:5: sized arrays require 'var' declaration: 'arr'
```

