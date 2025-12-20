---
feature: challenge-sized-arrays
status: stable
keywords: array, sized, allocation, memory
category: semantics
---

# Developer Notes

Tests for Challenge 5 from DEVELOPMENT_CHALLENGES.md: Empty and Zero-initialized Collections.

Empty collections must be handled correctly without memory leaks or invalid access.

# Documentation

## Sized Arrays

Sized arrays allocate space for a fixed number of elements.

## Tests

<!-- test: sized-array-default-values -->
```maxon
function main() returns int
    var arr = array of 3 int
    arr[1] = 42
    if let val = arr[1] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
42
```

<!-- test: sized-array-all-elements-writable -->
```maxon
function main() returns int
    var arr = array of 5 int
    arr[0] = 1
    arr[1] = 2
    arr[2] = 3
    arr[3] = 4
    arr[4] = 5
    var sum = 0
    if let a = arr[0] 'g0'
        sum = sum + a
    end 'g0'
    if let b = arr[1] 'g1'
        sum = sum + b
    end 'g1'
    if let c = arr[2] 'g2'
        sum = sum + c
    end 'g2'
    if let d = arr[3] 'g3'
        sum = sum + d
    end 'g3'
    if let e = arr[4] 'g4'
        sum = sum + e
    end 'g4'
    return sum
end 'main'
```
```exitcode
15
```

<!-- test: variable-sized-array-basic -->
```maxon
function main() returns int
    var n = 3
    var arr = array of n int
    arr[0] = 10
    arr[1] = 20
    arr[2] = 30
    var sum = 0
    if let a = arr[0] 'g0'
        sum = sum + a
    end 'g0'
    if let b = arr[1] 'g1'
        sum = sum + b
    end 'g1'
    if let c = arr[2] 'g2'
        sum = sum + c
    end 'g2'
    return sum
end 'main'
```
```exitcode
60
```

<!-- test: variable-sized-array-from-parameter -->
```maxon
function make_array(size int) returns int
    var arr = array of size int
    arr[0] = 42
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
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
    var arr = array of size int
    arr[5] = 99
    if let val = arr[5] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'main'
```
```exitcode
99
```

<!-- test: error.sized-array-negative-size -->
```maxon
function main() returns int
    var arr = array of -3 int
    return 0
end 'main'
```
```maxoncstderr
error E003: specs\fragments\challenge-sized-arrays.error.sized-array-negative-size.1.test:3:24: expected expression
```

## Ownership Tests

<!-- test: array-borrow-semantics -->
Passing an array to a function that only reads it borrows the array.

```maxon
function readFirst(arr array of 3 int) returns int
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'readFirst'

function main() returns int
    var arr = array of 3 int
    arr[0] = 42
    var result = readFirst(arr)
    var val = 0
    if let v = arr[0] 'get'
        val = v
    end 'get'
    return val + result
end 'main'
```
```exitcode
84
```

<!-- test: array-move-semantics -->
Passing an array to a function that modifies it moves the array.

```maxon
function mutate(arr array of 3 int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'mutate'

function main() returns int
    var arr = array of 3 int
    arr[0] = 42
    return mutate(arr)
end 'main'
```
```exitcode
100
```

<!-- test: error.array-use-after-move -->
Using an array after it has been moved should be a compile-time error.

```maxon
function mutate(arr array of 3 int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'mutate'

function main() returns int
    var arr = array of 3 int
    arr[0] = 42
    var result = mutate(arr)
    if let val = arr[0] 'get'
        return val + result
    end 'get' else 'nil'
        return result
    end 'nil'
end 'main'
```
```maxoncstderr
error E008: specs\fragments\challenge-sized-arrays.error.array-use-after-move.1.test:8:1: use after move
```

<!-- test: array-multiple-borrows -->
Multiple borrows of the same array should be allowed.

```maxon
function readFirst(arr array of 3 int) returns int
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'readFirst'

function main() returns int
    var arr = array of 3 int
    arr[0] = 10
    var a = readFirst(arr)
    var b = readFirst(arr)
    var c = readFirst(arr)
    return a + b + c
end 'main'
```
```exitcode
30
```

<!-- test: error.let-array-cannot-move -->
Immutable array cannot be passed to function that mutates it.

```maxon
function mutate(arr array of 3 int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'mutate'

function main() returns int
    let arr = [1, 2, 3]
    return mutate(arr)
end 'main'
```
```maxoncstderr
error E010: specs\fragments\challenge-sized-arrays.error.let-array-cannot-move.1.test:6:1: cannot move from immutable variable
```

<!-- test: array-move-then-reassign -->
After moving an array, reassigning it should allow using it again.

```maxon
function consume(arr array of 3 int) returns int
    arr[0] = 100
    if let val = arr[0] 'get'
        return val
    end 'get' else 'nil'
        return 0
    end 'nil'
end 'consume'

function main() returns int
    var arr = array of 3 int
    arr[0] = 10
    var first = consume(arr)
    arr = array of 3 int
    arr[0] = 20
    var second = consume(arr)
    return first + second
end 'main'
```
```exitcode
200
```
