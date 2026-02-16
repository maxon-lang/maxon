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

<!-- test: top-level-var-enum-initializer -->
```maxon
enum Color
    Red
    Green
    Blue
end 'Color'

var current = Color.Green

function main() returns ExitCode
  if current == Color.Green 'check'
    current = Color.Blue
    if current == Color.Blue 'check2'
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
