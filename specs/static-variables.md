---
feature: static-variables
status: experimental
keywords: [static, var, global, mutable, module, type]
category: language
---
# Static Variables

## Documentation

### Top-Level `var` Declarations

Top-level `var` declarations define mutable module-level variables. Unlike `let` constants which are compile-time evaluated and stored in read-only memory, `var` declarations create mutable storage in the program's data section.

#### Syntax

```maxon
var counter = 0
export var globalState = false
```

#### Features

- **Runtime storage**: Variables are stored in the writable data section
- **Initialization**: Initializers are evaluated at program start before `main`
- **Type inference**: Type is inferred from the initializer
- **Export support**: Use `export var` to make variables available to other modules

#### Initializer Requirements

Top-level `var` initializers must be constant expressions (same rules as `let`):
- Literals: integers, floats, booleans, strings, bytes, characters
- Arithmetic and logical operations on constants
- References to other top-level constants
- Enum member access

Function calls and runtime expressions are not allowed in initializers.

### Static Fields in Types

Types can have static fields that are shared across all instances. Static fields use the `static` keyword before `var` or `let`.

#### Syntax

```maxon
typealias Score = int(i64.min to i64.max)

type Counter
  static var count = 0       // Mutable static field
  static let MAX = 100       // Compile-time static constant

  export var value Score       // Instance field
end 'Counter'
```

#### Features

- **Shared storage**: One copy exists for the type, not per instance
- **Direct access**: Access via `TypeName.fieldName` syntax
- **Static let**: Compile-time constant (same as top-level `let`)
- **Static var**: Mutable storage (same as top-level `var`)

#### Access Patterns

```maxon
Counter.count = Counter.count + 1   // Access static field
var c = Counter{value: 10}          // Create instance
c.value = 20                        // Access instance field
```

## Tests

<!-- test: top-level-var-basic -->
```maxon
var counter = 0

function main() returns ExitCode
  counter = 42
  return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-increment -->
```maxon

typealias Integer = int(i64.min to i64.max)

var total = 10

function add(n Integer)
  total = total + n
end 'add'

function main() returns ExitCode
  add(5)
  add(27)
  return total
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-multiple -->
```maxon
var a = 1
var b = 2
var c = 3

function main() returns ExitCode
  a = a * 10
  b = b * 10
  c = c * 10
  return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: top-level-var-with-let -->
```maxon
let BASE = 40
var offset = 0

function main() returns ExitCode
  offset = 2
  return BASE + offset
end 'main'
```
```exitcode
42
```

<!-- test: static-var-basic -->
```maxon
type Counter
  static var count = 0
end 'Counter'

function main() returns ExitCode
  Counter.count = 42
  return Counter.count
end 'main'
```
```exitcode
42
```

<!-- test: static-var-increment -->
```maxon
type Counter
  static var count = 0

  static function increment()
    Counter.count = Counter.count + 1
  end 'increment'
end 'Counter'

function main() returns ExitCode
  Counter.increment()
  Counter.increment()
  Counter.increment()
  return Counter.count
end 'main'
```
```exitcode
3
```

<!-- test: static-let-basic -->
```maxon
type Config
  static let MAX_SIZE = 42
end 'Config'

function main() returns ExitCode
  return Config.MAX_SIZE
end 'main'
```
```exitcode
42
```

<!-- test: static-var-multiple-types -->
```maxon
type TypeA
  static var value = 10
end 'TypeA'

type TypeB
  static var value = 20
end 'TypeB'

function main() returns ExitCode
  TypeA.value = TypeA.value + 2
  TypeB.value = TypeB.value + 10
  return TypeA.value + TypeB.value
end 'main'
```
```exitcode
42
```

<!-- test: static-and-instance-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Thing
  static var created = 0
  export var id Integer

  static function make(n Integer) returns Thing
    Thing.created = Thing.created + 1
    return {id: n}
  end 'make'
end 'Thing'

function main() returns ExitCode
  var a = Thing.make(10)
  var b = Thing.make(20)
  return Thing.created + a.id + b.id
end 'main'
```
```exitcode
32
```

<!-- test: static-var-bool -->
```maxon
var initialized = false

function init()
  initialized = true
end 'init'

function main() returns ExitCode
  if initialized 'check1'
    return 1
  end 'check1'
  init()
  if initialized 'check2'
    return 42
  end 'check2'
  return 0
end 'main'
```
```exitcode
42
```

<!-- test: static-var-bool-adjacent-globals -->
Bool global followed by non-zero global must not bleed adjacent data.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
  if flag 'checkFalse'
    print("flag should be false\n")
    return 1
  end 'checkFalse'
  if counter == 42 'checkCounter'
    return 0
  end 'checkCounter'
  print("counter wrong\n")
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: top-level-var-enum-initializer -->
```maxon
union Color
    Red
    Green
    Blue
end 'Color'

var current = Color.Green

function main() returns ExitCode
  var isGreen = match current 'check'
    Color.Green gives true
    Color.Red gives false
    Color.Blue gives false
  end 'check'
  if isGreen 'check'
    current = Color.Blue
    var isBlue = match current 'check2'
      Color.Blue gives true
      Color.Red gives false
      Color.Green gives false
    end 'check2'
    if isBlue 'check2'
      return 42
    end 'check2'
  end 'check'
  return 0
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-const-expr -->
```maxon
let BASE = 20
var offset = BASE + 1

function main() returns ExitCode
  offset = offset * 2
  return offset
end 'main'
```
```exitcode
42
```

<!-- test: top-level-var-array-literal -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var items = [10, 20, 30]

function main() returns ExitCode
  items.set(1, value: 12)
  let a = try items.get(0) otherwise 0
  let b = try items.get(1) otherwise 0
  let c = try items.get(2) otherwise 0
  return a + b + c
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var scores = [10, 20, 30]

function getTotal() returns Integer
  let a = try scores.get(0) otherwise 0
  let b = try scores.get(1) otherwise 0
  let c = try scores.get(2) otherwise 0
  return a + b + c
end 'getTotal'

function setScore(index Integer, value Integer)
  scores.set(index, value: value)
end 'setScore'

function main() returns ExitCode
  setScore(1, value: 12)
  return getTotal()
end 'main'
```
```exitcode
52
```

<!-- test: top-level-var-array-mutate-cross-function -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var counters = [0, 0, 0]

function increment(index Integer)
  let current = try counters.get(index) otherwise 0
  counters.set(index, value: current + 1)
end 'increment'

function total() returns Integer
  let a = try counters.get(0) otherwise 0
  let b = try counters.get(1) otherwise 0
  let c = try counters.get(2) otherwise 0
  return a + b + c
end 'total'

function main() returns ExitCode
  increment(0)
  increment(0)
  increment(1)
  increment(2)
  increment(2)
  increment(2)
  return total()
end 'main'
```
```exitcode
6
```

<!-- test: data-section-bool-1byte -->
A single bool global occupies 1 byte in the .data section.

```maxon
var flag = true

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
```

<!-- test: data-section-i64-8byte -->
A single i64 global occupies 8 bytes in the .data section.

```maxon
var counter = 42

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

<!-- test: data-section-f64-8byte -->
A single f64 global occupies 8 bytes in the .data section.

```maxon
var pi = 3.14

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

<!-- test: data-section-bool-then-i64-sorted -->
A bool and i64 global: sorted largest-first, no padding needed.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
```

<!-- test: data-section-bool-true-then-i64 -->
A true bool and i64: sorted largest-first, no padding needed.

```maxon
var flag = true
var counter = 99

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 99
i8 1
```

<!-- test: data-section-i64-then-bool -->
An i64 followed by a bool: no padding needed since bool has 1-byte alignment.

```maxon
var counter = 7
var flag = true

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 7
i8 1
```

<!-- test: data-section-multiple-bools -->
Multiple consecutive bools occupy 1 byte each with no padding.

```maxon
var a = true
var b = false
var c = true

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
i8 0
i8 1
```

<!-- test: data-section-mixed-types -->
Mixed bool, i64, f64 globals sorted largest-first, no padding.

```maxon
var flag = true
var count = 10
var ratio = 2.5

function main() returns ExitCode
  return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 10
f64 2.5
i8 1
```

<!-- test: top-level-var-byte-ranged-type -->
Module-level var with a byte-sized ranged type.
```maxon
typealias SmallInt = int(0 to u8.max)

var counter = SmallInt{42}

function main() returns ExitCode
    return counter
end 'main'
```
```exitcode
42
```

<!-- test: top-level-let-struct-reassign-error -->
Reassigning an immutable top-level `let` struct variable should error.
```maxon
typealias SmallInt = int(0 to u8.max)

type Point
    export var x SmallInt
    export var y SmallInt
end 'Point'

let origin = Point{x: 0, y: 0}

function main() returns ExitCode
    origin = Point{x: 1, y: 1}
    return 0
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/static-variables/top-level-let-struct-reassign-error.test:12:5: cannot assign to immutable variable: 'origin'
```

<!-- test: top-level-var-function-call-error -->
Function calls are not allowed in module-level `var` initializers.
```maxon
fn getDefault() -> Int
  return 42
end

var value = getDefault()

fn main()
  println(value)
end
```
```maxoncstderr
error E2045: specs/fragments/static-variables/top-level-var-function-call-error.test:6:13: Function calls are not allowed in global variable initializers; 'getDefault()' is not a constant expression
```
