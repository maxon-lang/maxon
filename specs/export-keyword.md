---
feature: export-keyword
status: stable
keywords: [export, visibility, module, function, type]
category: infrastructure
---

# Export Keyword

## Documentation

### Export Keyword

All declarations — functions, types, enums, typealiases, and top-level variables — are file-scoped by default. The `export` keyword makes them visible to other modules. Without `export`, a declaration can only be used within the file where it is defined.

```text
export function publicApi() returns Integer
  return privateHelper()
end 'publicApi'

function privateHelper() returns Integer
  return 42
end 'privateHelper'
```

When modules are compiled together, only exported symbols from earlier modules can be called by later modules. Non-exported symbols from other files are invisible — attempting to use them produces a compile error.

### Exporting Types

Types can be exported to make them available to other modules. Without `export`, a type is only usable within its file:

```text
export type Point
  export var x Integer
  export var y Integer
end 'Point'
```

### Exporting Enums

Enums follow the same visibility rules as types:

```text
export enum Color
  red
  green
  blue
end 'Color'
```

Without `export`, an enum is only visible within its declaring file.

### Exporting Type Aliases

Typealiases are also file-scoped by default. Use `export` for cross-file visibility:

```text
export typealias Score = int(0 to 100)
```

The standard library exports commonly-used aliases like `Integer`, `Float`, `Byte`, `Count`, `Index`, and `ExitCode`.

### Exporting Methods

Methods within types can be individually exported:

```text
export type Calculator
  var result Integer

  export function add(n Integer)
    result = result + n
  end 'add'

  function internalReset()
    result = 0
  end 'internalReset'
end 'Calculator'
```

### Namespace Disambiguation

When multiple modules export symbols with the same name, you must use qualified names to disambiguate:

```text
// If both math.maxon and string.maxon export 'add':
var result1 = math.add(1, 2)        // Calls math.maxon's add
var result2 = string.add("a", "b")  // Calls string.maxon's add
```

If you use an unqualified name that is ambiguous, the compiler will report an error:
```text
error E061: ambiguous symbol reference: 'add' - defined in 'math' and 'string', use 'math.add' or 'string.add'
```

Note: When there's no collision, unqualified names continue to work normally.

## Tests

<!-- test: export-function-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
  return 21
end 'helper'

function main() returns ExitCode
  return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: export-type-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

export type Point
  var x Integer
  var y Integer

  export function sum() returns Integer
    return x + y
  end 'sum'
end 'Point'

function main() returns ExitCode
  var p = Point{x: 20, y: 22}
  return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: non-export-function-works -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
  return 42
end 'helper'

function main() returns ExitCode
  return helper()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-and-non-export -->
```maxon

typealias Integer = int(i64.min to i64.max)

export function publicFunc() returns Integer
  return privateFunc() + 20
end 'publicFunc'

function privateFunc() returns Integer
  return 22
end 'privateFunc'

function main() returns ExitCode
  return publicFunc()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-basic -->
```maxon

typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(42)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-in-type-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

type Container
  export var items IntArray

  static function create() returns Self
    return {items: IntArray{}}
  end 'create'

  function add(n Integer)
    items.push(n)
  end 'add'

  function sum() returns Integer
    var total = 0
    for item in items 'loop'
      total = total + item
    end 'loop'
    return total
  end 'sum'
end 'Container'

function main() returns ExitCode
  var c = Container.create()
  c.add(20)
  c.add(22)
  return c.sum()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-as-return-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

function makeArray() returns IntArray
  var arr = IntArray{}
  arr.push(42)
  return arr
end 'makeArray'

function main() returns ExitCode
  var arr = makeArray()
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: non-export-typealias-in-same-file -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(42)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: exported-function-cross-file -->
```maxon
// --- file: helper.maxon
typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
  return 42
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
  return helper()
end 'main'
```
```exitcode
42
```

<!-- test: non-exported-function-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
  return 99
end 'privateHelper'

function main() returns ExitCode
  return privateHelper()
end 'main'
```
```exitcode
99
```

<!-- test: error.non-exported-function-cross-file -->
```maxon
// --- file: helper.maxon
typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
  return 99
end 'privateHelper'

// --- file: main.maxon
function main() returns ExitCode
  return privateHelper()
end 'main'
```
```maxoncstderr
error E3008: main.maxon:2:10: function 'privateHelper' is not exported
```

<!-- test: error.typealias-with-unknown-element-type -->
```maxon
typealias BadArray = Array with UnknownType

type Container
  var items BadArray
end 'Container'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/export-keyword/error.typealias-with-unknown-element-type.test:2:44: Unknown type: UnknownType
```

<!-- test: exported-type-cross-file -->
```maxon
// --- file: point.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
  export var x Integer
  export var y Integer
end 'Point'

// --- file: main.maxon
function main() returns ExitCode
  var p = Point{x: 20, y: 22}
  return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-type-cross-file -->
```maxon
// --- file: point.maxon
typealias Integer = int(i64.min to i64.max)

type InternalPoint
  export var x Integer
end 'InternalPoint'

// --- file: main.maxon
function main() returns ExitCode
  var p = InternalPoint{x: 42}
  return p.x
end 'main'
```
```maxoncstderr
error E2004: main.maxon:2:11: Undefined variable 'InternalPoint'
```

<!-- test: exported-enum-cross-file -->
```maxon
// --- file: color.maxon
export enum Color
  red
  green
  blue
end 'Color'

// --- file: main.maxon
function main() returns ExitCode
  var c = Color.blue
  match c 'check'
    Color.blue then return 42
    Color.red then return 0
    Color.green then return 0
  end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-enum-cross-file -->
```maxon
// --- file: status.maxon
enum InternalStatus
  ok
  err
end 'InternalStatus'

// --- file: main.maxon
function main() returns ExitCode
  var s = InternalStatus.ok
  return 0
end 'main'
```
```maxoncstderr
error E2004: main.maxon:2:11: Undefined variable 'InternalStatus'
```

<!-- test: exported-typealias-cross-file -->
```maxon
// --- file: types.maxon
export typealias Score = int(0 to 100)

// --- file: main.maxon
function main() returns ExitCode
  var s = Score{42}
  return s
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-typealias-cross-file -->
```maxon
// --- file: types.maxon
typealias InternalScore = int(0 to 100)

// --- file: main.maxon
function main() returns ExitCode
  var s = InternalScore{42}
  return s
end 'main'
```
```maxoncstderr
error E3062: types.maxon:1:11: unused typealias: 'InternalScore'
```

<!-- test: error.duplicate-typealias-same-file -->
```maxon
typealias Score = int(0 to 100)
typealias Score = int(0 to 200)

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3061: specs/fragments/export-keyword/error.duplicate-typealias-same-file.test:3:11: Duplicate typealias 'Score'
```

<!-- test: non-exported-type-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

type InternalPoint
  export var x Integer
  export var y Integer
end 'InternalPoint'

function main() returns ExitCode
  var p = InternalPoint{x: 20, y: 22}
  return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: exported-var-cross-file -->
Cross-file access to an exported module-level var with a simple constant value.
```maxon
// --- file: counter.maxon
export var counter = 10

// --- file: main.maxon
function main() returns ExitCode
    return counter
end 'main'
```
```exitcode
10
```

<!-- test: exported-struct-var-cross-file -->
Cross-file access to an exported module-level struct var.
```maxon
// --- file: state.maxon
typealias SmallInt = int(0 to 255)

export type Counter
    export var value SmallInt
end 'Counter'

export var shared = Counter{value: 0}

// --- file: main.maxon
function main() returns ExitCode
    shared.value = 42
    return shared.value
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-var-cross-file -->
Non-exported module-level var should not be accessible from another file.
```maxon
// --- file: state.maxon
var secret = 99

// --- file: main.maxon
function main() returns ExitCode
    return secret
end 'main'
```
```maxoncstderr
error E2004: main.maxon:2:12: Undefined variable 'secret'
```

<!-- test: non-exported-enum-same-file -->
```maxon
enum Direction
  up
  down
end 'Direction'

function main() returns ExitCode
  var d = Direction.up
  match d 'check'
    Direction.up then return 42
    Direction.down then return 0
  end 'check'
end 'main'
```
```exitcode
42
```
