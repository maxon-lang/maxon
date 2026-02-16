---
feature: vector
status: experimental
keywords: [vector, fixed size, stack, collection, generic]
category: stdlib
---

# Vector

## Documentation

### Overview

`Vector` is a generic fixed-size collection. 

### Creating Vectors

Create a concrete vector type using `typealias` with element type and size:

```text
typealias Vec3 = Vector with 3 int
var v = Vec3{}  // zero-initialized, 3 elements on the stack
```

The size is part of the type. A `Vector with 3 int` is a different type from `Vector with 4 int`.

### Creating from Array Literals

Vectors implement `BuiltinArrayLiteral`, so you can initialize them from an array literal using `from`. The element type and size are inferred from the literal:

```text
var v = Vector from [10, 20, 30]  // inferred as Vector with 3 int
```

The inferred type is compatible with a typealias of the same element type and size, so a `Vector from [...]` can be passed to a function expecting the typealias:

```text
typealias Vec3 = Vector with 3 int

function process(v Vec3) returns int
  return try v.get(0) otherwise 0
end 'process'

var v = Vector from [10, 20, 30]
process(v)  // works — inferred type matches Vec3
```

### Element Access

Access elements with `.get()`:

```text
var value = try v.get(0) otherwise 0
```

Modify elements with `.set()`:

```text
v.set(0, value: 42)
```

### Size and Count

The `.count()` method always returns the fixed size of the vector:

```text
typealias Vec4 = Vector with 4 int
var v = Vec4{}
var n = v.count()  // always 4
```

### Stack vs Heap

Vectors are designed for small, fixed-size data. The compiler places the storage on the stack when the total byte size (element size x count) is 8192 bytes or less. Larger vectors are automatically heap-allocated.

```text
typealias SmallVec = Vector with 100 int    // 800 bytes → stack
typealias LargeVec = Vector with 2000 int   // 16000 bytes → heap
```

### Use Cases

Vectors are ideal for:
- Small fixed-size collections (coordinates, colors, matrices)
- Performance-sensitive code where heap allocation is undesirable
- Types with a known compile-time size

```text
typealias Point3D = Vector with 3 float
typealias Color = Vector with 4 byte      // RGBA
typealias Mat2x2 = Vector with 4 float    // 2x2 matrix stored flat
```

### Iteration

Vectors support `for-in` loops:

```text
typealias Vec3 = Vector with 3 int
var v = Vec3{}
v.set(0, value: 10)
v.set(1, value: 20)
v.set(2, value: 30)

for elem in v 'loop'
  print("{elem}")
end 'loop'
```

## Tests

<!-- test: create-zero-initialized -->
```maxon
typealias Vec3 = Vector with 3 int

function main() returns ExitCode
  var v = Vec3{}
  return try v.get(0) otherwise -1
end 'main'
```
```exitcode
0
```

<!-- test: count -->
```maxon
typealias Vec4 = Vector with 4 int

function main() returns ExitCode
  var v = Vec4{}
  return v.count()
end 'main'
```
```exitcode
4
```

<!-- test: set-and-get -->
```maxon
typealias Vec3 = Vector with 3 int

function main() returns ExitCode
  var v = Vec3{}
  v.set(0, value: 42)
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: set-all-elements -->
```maxon
typealias Vec3 = Vector with 3 int

function main() returns ExitCode
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: get-out-of-bounds -->
Accessing an index beyond the fixed size throws ArrayError.
```maxon
typealias Vec2 = Vector with 2 int

function main() returns ExitCode
  var v = Vec2{}
  v.set(0, value: 10)
  var result = try v.get(5) otherwise -1
  print("{result}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: get-out-of-bounds -->
```maxon
typealias Vec2 = Vector with 2 int

function main() returns ExitCode
  var v = Vec2{}
  v.set(0, value: 10)
  var result = try v.get(5) otherwise -1
  print("{result}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: set-out-of-bounds-noop -->
Setting an out-of-bounds index is a no-op, matching Array behavior.
```maxon
typealias Vec2 = Vector with 2 int

function main() returns ExitCode
  var v = Vec2{}
  v.set(0, value: 10)
  v.set(5, value: 99)
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: single-element -->
```maxon
typealias Vec1 = Vector with 1 int

function main() returns ExitCode
  var v = Vec1{}
  v.set(0, value: 77)
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
77
```

<!-- test: larger-vector -->
```maxon
typealias Vec10 = Vector with 10 int

function main() returns ExitCode
  var v = Vec10{}
  var i = 0
  while i < 10 'fill'
    v.set(i, value: i * 10)
    i = i + 1
  end 'fill'
  var first = try v.get(0) otherwise -1
  var last = try v.get(9) otherwise -1
  return first + last
end 'main'
```
```exitcode
90
```

<!-- test: count-single -->
```maxon
typealias Vec1 = Vector with 1 int

function main() returns ExitCode
  var v = Vec1{}
  return v.count()
end 'main'
```
```exitcode
1
```

<!-- test: overwrite-element -->
```maxon
typealias Vec3 = Vector with 3 int

function main() returns ExitCode
  var v = Vec3{}
  v.set(1, value: 10)
  v.set(1, value: 42)
  return try v.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: float-vector -->
```maxon
typealias Vec2F = Vector with 2 float

function main() returns ExitCode
  var v = Vec2F{}
  v.set(0, value: 2.5)
  v.set(1, value: 3.5)
  var a = try v.get(0) otherwise 0.0
  var b = try v.get(1) otherwise 0.0
  return trunc(a + b)
end 'main'
```
```exitcode
6
```

<!-- test: byte-vector -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias ByteVec4 = Vector with 4 byte

function main() returns ExitCode
  var v = ByteVec4{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  v.set(3, value: 40)
  var a = try v.get(0) otherwise 0
  var b = try v.get(3) otherwise 0
  return (a as Integer) + (b as Integer)
end 'main'
```
```exitcode
50
```

<!-- test: pass-to-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 int

function sum(v Vec3) returns Integer
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'sum'

function main() returns ExitCode
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 12)
  return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: return-from-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec2 = Vector with 2 int

function makeVec(a Integer, b Integer) returns Vec2
  var v = Vec2{}
  v.set(0, value: a)
  v.set(1, value: b)
  return v
end 'makeVec'

function main() returns ExitCode
  var v = makeVec(30, b: 12)
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  return a + b
end 'main'
```
```exitcode
42
```

<!-- test: iterate -->
```maxon
typealias Vec4 = Vector with 4 int

function main() returns ExitCode
  var v = Vec4{}
  v.set(0, value: 1)
  v.set(1, value: 2)
  v.set(2, value: 3)
  v.set(3, value: 4)
  var sum = 0
  for elem in v 'loop'
    sum = sum + elem
  end 'loop'
  return sum
end 'main'
```
```exitcode
10
```

<!-- test: let-vector-read -->
```maxon
typealias Vec3 = Vector with 3 int

function makeVec() returns Vec3
  var v = Vec3{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 12)
  return v
end 'makeVec'

function main() returns ExitCode
  let v = makeVec()
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
42
```

<!-- test: from-array-literal -->
```maxon
function main() returns ExitCode
  var v = Vector from [10, 20, 30]
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-sum -->
```maxon
function main() returns ExitCode
  var v = Vector from [10, 20, 30]
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: from-array-literal-float -->
```maxon
function main() returns ExitCode
  var v = Vector from [1.5, 2.5]
  var a = try v.get(0) otherwise 0.0
  var b = try v.get(1) otherwise 0.0
  return trunc(a + b)
end 'main'
```
```exitcode
4
```

<!-- test: from-array-literal-iterate -->
```maxon
function main() returns ExitCode
  var v = Vector from [1, 2, 3, 4]
  var sum = 0
  for elem in v 'loop'
    sum = sum + elem
  end 'loop'
  return sum
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-single -->
```maxon
function main() returns ExitCode
  var v = Vector from [99]
  return try v.get(0) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: from-literal-typealias-compatible -->
The inferred type from a literal is compatible with a typealias of the same element type and size.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 int

function sum(v Vec3) returns Integer
  var a = try v.get(0) otherwise 0
  var b = try v.get(1) otherwise 0
  var c = try v.get(2) otherwise 0
  return a + b + c
end 'sum'

function main() returns ExitCode
  var v = Vector from [10, 20, 12]
  return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: accumulate-sum -->
```maxon
typealias Vec5 = Vector with 5 int

function main() returns ExitCode
  var v = Vec5{}
  v.set(0, value: 10)
  v.set(1, value: 20)
  v.set(2, value: 30)
  v.set(3, value: 40)
  v.set(4, value: 50)
  var sum = 0
  var i = 0
  while i < v.count() 'loop'
    sum = sum + (try v.get(i) otherwise 0)
    i = i + 1
  end 'loop'
  return sum
end 'main'
```
```exitcode
150
```
