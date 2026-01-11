---
feature: challenge-sized-arrays
status: stable
keywords: array, sized, allocation, memory
category: semantics
---
# Challenge Sized Arrays

## Documentation

## Sized Arrays

Sized arrays allocate space for a fixed number of elements.

## Tests

<!-- test: sized-array-default-values -->
```maxon
function main() returns int
    var arr = Array of 3 int
    arr.set(1, value: 42)
    return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: sized-array-all-elements-writable -->
```maxon
function main() returns int
    var arr = Array of 5 int
    arr.set(0, value: 1)
    arr.set(1, value: 2)
    arr.set(2, value: 3)
    arr.set(3, value: 4)
    arr.set(4, value: 5)
    var a = try arr.get(0) otherwise 0
    var b = try arr.get(1) otherwise 0
    var c = try arr.get(2) otherwise 0
    var d = try arr.get(3) otherwise 0
    var e = try arr.get(4) otherwise 0
    return a + b + c + d + e
end 'main'
```
```exitcode
15
```

<!-- test: variable-sized-array-basic -->
```maxon
function main() returns int
    var n = 3
    var arr = Array of n int
    arr.set(0, value: 10)
    arr.set(1, value: 20)
    arr.set(2, value: 30)
    var a = try arr.get(0) otherwise 0
    var b = try arr.get(1) otherwise 0
    var c = try arr.get(2) otherwise 0
    return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: variable-sized-array-from-parameter -->
```maxon
function make_array(size int) returns int
    var arr = Array of size int
    arr.set(0, value: 42)
    return try arr.get(0) otherwise 0
end 'make_array'

function main() returns int
    return make_array(5)
end 'main'
```
```exitcode
42
```

<!-- test: variable-sized-array-computed-size -->
```maxon
function main() returns int
    var base = 2
    var multiplier = 3
    var size = base * multiplier
    var arr = Array of size int
    arr.set(5, value: 99)
    return try arr.get(5) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: error.sized-array-negative-size -->
```maxon
function main() returns int
    var arr = Array of -3 int
    return 0
end 'main'
```
```maxoncstderr
error E014: specs/fragments/challenge-sized-arrays.error.sized-array-negative-size.1.test:4:5: unused variable: 'arr'
```

## Ownership Tests

<!-- test: array-borrow-semantics -->
Passing an array to a function that only reads it borrows the array.

```maxon
function readFirst(arr Array of 3 int) returns int
    return try arr.get(0) otherwise 0
end 'readFirst'

function main() returns int
    var arr = Array of 3 int
    arr.set(0, value: 42)
    var result = readFirst(arr)
    var val = try arr.get(0) otherwise 0
    return val + result
end 'main'
```
```exitcode
84
```

<!-- test: array-move-semantics -->
Passing an array to a function that modifies it moves the array.

```maxon
function mutate(arr Array of 3 int) returns int
    arr.set(0, value: 100)
    return try arr.get(0) otherwise 0
end 'mutate'

function main() returns int
    var arr = Array of 3 int
    arr.set(0, value: 42)
    return mutate(arr)
end 'main'
```
```exitcode
100
```

<!-- test: error.array-use-after-move -->
Using an array after it has been moved should be a compile-time error.

```maxon
function mutate(arr Array of 3 int) returns int
    arr.set(0, value: 100)
    return try arr.get(0) otherwise 0
end 'mutate'

function main() returns int
    var arr = Array of 3 int
    arr.set(0, value: 42)
    var result = mutate(arr)
    var val = try arr.get(0) otherwise 0
    return val + result
end 'main'
```
```maxoncstderr
error E008: specs/fragments/challenge-sized-arrays.error.array-use-after-move.1.test:11:1
```

<!-- test: error.let-array-cannot-move -->
Immutable array cannot be passed to function that mutates it.

```maxon
function mutate(arr Array of 3 int) returns int
    arr.set(0, value: 100)
    return try arr.get(0) otherwise 0
end 'mutate'

function main() returns int
    let arr = [1, 2, 3]
    return mutate(arr)
end 'main'
```
```maxoncstderr
error E010: specs/fragments/challenge-sized-arrays.error.let-array-cannot-move.1.test:9
```
