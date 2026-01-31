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
numbers.set(0, value: 100)  // Can modify elements
```

## Immutable Array Literals

Create an immutable array using `let` with square brackets:

```text
let constants = [10, 20, 30]
var x = try constants.get(1) otherwise 0  // Can read elements
```

## Preallocated Arrays

Create an array with preallocated capacity and length using `.resize()`:

```text
typealias IntArray is Array with int

var buffer = IntArray{}
buffer.resize(10)   // Length is now 10, elements are zero-initialized
buffer.set(0, value: 42)
```

Use `.reserve()` to allocate capacity without changing length (for performance when appending):

```text
typealias IntArray is Array with int

var buffer = IntArray{}
buffer.reserve(100)  // Capacity is 100, length is still 0
buffer.push(42)      // Now length is 1
```

## Element Access

Access array elements using the `.get()` method with a zero-based index.
The method returns an optional value (throws on out of bounds), so use `try ... otherwise default`:

```text
var arr = [10, 20, 30]
var first = try arr.get(0) otherwise 0   // 10
var second = try arr.get(1) otherwise 0  // 20
var third = try arr.get(2) otherwise 0   // 30
```

## Element Assignment

Modify mutable array elements using the `.set()` method:

```text
var arr = [10, 20, 30]
arr.set(0, value: 100)
arr.set(1, value: 200)
```

## Tests

<!-- test: literal-first -->
```maxon
function main() returns int
  return try [10, 20, 30].get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: literal-middle -->
```maxon
function main() returns int
  var arr = [10, 20, 30]
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: literal-last -->
```maxon
function main() returns int
  var arr = [10, 20, 30]
  return try arr.get(2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: five-elements -->
```maxon
function main() returns int
  var arr = [5, 10, 15, 20, 25]
  return try arr.get(4) otherwise 0
end 'main'
```
```exitcode
25
```

<!-- test: index-assignment -->
```maxon
function main() returns int
  var arr = [10, 20, 30]
  arr.set(0, value: 100)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
100
```

<!-- test: assignment-middle -->
```maxon
function main() returns int
  var arr = [1, 2, 3]
  arr.set(1, value: 42)
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: assignment-last -->
```maxon
function main() returns int
  var arr = [1, 2, 3, 4, 5]
  arr.set(4, value: 99)
  return try arr.get(4) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: multiple-access -->
```maxon
function main() returns int
  var arr = [5, 10, 15, 20, 25]
  var a = try arr.get(2) otherwise 0
  var b = try arr.get(4) otherwise 0
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
  arr.set(0, value: 100)
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: multiple-assignments -->
```maxon
function main() returns int
  var arr = [0, 0, 0]
  arr.set(0, value: 1)
  arr.set(1, value: 2)
  arr.set(2, value: 3)
  var a = try arr.get(0) otherwise 0
  var b = try arr.get(1) otherwise 0
  var c = try arr.get(2) otherwise 0
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
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: let-array-middle -->
```maxon
function main() returns int
  let arr = [10, 20, 30]
  return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: let-array-last -->
```maxon
function main() returns int
  let arr = [10, 20, 30]
  return try arr.get(2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: let-array-multiple-access -->
```maxon
function main() returns int
  let arr = [5, 10, 15, 20]
  var a = try arr.get(0) otherwise 0
  var b = try arr.get(3) otherwise 0
  return a + b
end 'main'
```
```exitcode
25
```

<!-- test: array-with-reserve -->
Test that arrays can be created with `.reserve()` for preallocated capacity.
```maxon
typealias IntArray is Array with int

function main() returns int
  var arr = IntArray{}
  arr.reserve(5)
  arr.push(42)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42```

<!-- test: array-with-resize -->
Test that arrays can be created with `.resize()` for preallocated length.
```maxon
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    arr.resize(5)
    arr.set(0, value: 99)
    return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: array-growth-realloc -->
Test that arrays grow correctly when pushing many elements (triggers multiple reallocs).
```maxon
typealias IntArray is Array with int

function main() returns int
    var arr = IntArray{}
    var i = 0
    while i < 100 'loop'
        arr.push(i)
        i = i + 1
    end 'loop'
    return try arr.get(99) otherwise -1
end 'main'
```
```exitcode
99
```

### Byte Array Push and Get

<!-- test: byte-array-push-get -->
```maxon
typealias ByteArray is Array with byte

function main() returns int
  var arr = ByteArray{}
  arr.push(10 as byte)
  arr.push(20 as byte)
  arr.push(30 as byte)

  var v0 = try arr.get(0) otherwise 0 as byte
  var v1 = try arr.get(1) otherwise 0 as byte
  var v2 = try arr.get(2) otherwise 0 as byte

  return (v0 as int) + (v1 as int) + (v2 as int)
end 'main'
```
```exitcode
60
```

### Byte Array Initialized

<!-- test: byte-array-initialized -->
```maxon
typealias ByteArray is Array with byte

function main() returns int
  var arr = ByteArray{}
  arr.push(1 as byte)
  arr.push(2 as byte)
  arr.push(3 as byte)

  var v0 = try arr.get(0) otherwise 0 as byte
  var v1 = try arr.get(1) otherwise 0 as byte
  var v2 = try arr.get(2) otherwise 0 as byte

  return (v0 as int) + (v1 as int) + (v2 as int)
end 'main'
```
```exitcode
6
```

### Byte Array Set

<!-- test: byte-array-set -->
```maxon
typealias ByteArray is Array with byte

function main() returns int
  var arr = ByteArray{}
  arr.push(10 as byte)
  arr.push(20 as byte)
  arr.push(30 as byte)

  arr.set(1, value: 99 as byte)

  var val = try arr.get(1) otherwise 0 as byte
  return val as int
end 'main'
```
```exitcode
99
```

### Byte Array Max Values

<!-- test: byte-array-max-values -->
```maxon
typealias ByteArray is Array with byte

function main() returns int
  var arr = ByteArray{}
  arr.push(255 as byte)
  arr.push(0 as byte)
  arr.push(128 as byte)

  var v0 = try arr.get(0) otherwise 0 as byte
  var v1 = try arr.get(1) otherwise 99 as byte
  var v2 = try arr.get(2) otherwise 0 as byte

  if (v0 as int) != 255 'c0'
    return 1
  end 'c0'
  if (v1 as int) != 0 'c1'
    return 2
  end 'c1'
  if (v2 as int) != 128 'c2'
    return 3
  end 'c2'

  return 0
end 'main'
```
```exitcode
0
```

### Byte Array Count

<!-- test: byte-array-count -->
```maxon
typealias ByteArray is Array with byte

function main() returns int
  var arr = ByteArray{}
  arr.push(1 as byte)
  arr.push(2 as byte)
  arr.push(3 as byte)
  arr.push(4 as byte)
  arr.push(5 as byte)

  return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: array-literal-constant -->
```maxon
let numbers = [1, 2, 3, 4, 5]

function main() returns int
  var sum = 0
  for n in numbers 'loop'
    sum = sum + n
  end 'loop'
  return sum
end 'main'
```
```exitcode
15
```

<!-- test: array-literal-with-dependency -->
```maxon
let FIRST = 10
let SECOND = 20
let values = [FIRST, SECOND, 30]

function main() returns int
  var v0 = try values.get(0) otherwise 0
  var v1 = try values.get(1) otherwise 0
  var v2 = try values.get(2) otherwise 0
  return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
