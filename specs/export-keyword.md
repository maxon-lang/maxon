---
feature: export-keyword
status: stable
keywords: [export, visibility, module, function, type]
category: infrastructure
---

# Export Keyword

## Documentation

### Export Keyword

The `export` keyword marks functions, types, and interfaces as visible to other modules. This is primarily used in the standard library to expose public APIs while keeping implementation details private.

```text
export function publicApi() returns int
  return privateHelper()
end 'publicApi'

function privateHelper() returns int
  return 42
end 'privateHelper'
```

When modules are compiled together, only exported symbols from earlier modules can be called by later modules.

### Exporting Types

Types can be exported to make them available to other modules:

```text
export type Point
  var x int
  var y int
end 'Point'
```

### Exporting Methods

Methods within types can be individually exported:

```text
export type Calculator
  var result int

  export function add(n int)
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
export function helper() returns int
  return 21
end 'helper'

function main() returns int
  return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: export-type-basic -->
```maxon
export type Point
  var x int
  var y int

  export function sum() returns int
    return x + y
  end 'sum'
end 'Point'

function main() returns int
  var p = Point{x: 20, y: 22}
  return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: non-export-function-works -->
```maxon
function helper() returns int
  return 42
end 'helper'

function main() returns int
  return helper()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-and-non-export -->
```maxon
export function publicFunc() returns int
  return privateFunc() + 20
end 'publicFunc'

function privateFunc() returns int
  return 22
end 'privateFunc'

function main() returns int
  return publicFunc()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-basic -->
```maxon
export typealias IntArray is Array with int

function main() returns int
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
export typealias IntArray is Array with int

type Container
  export var items IntArray

  static function create() returns Self
    return {items: IntArray{}}
  end 'create'

  function add(n int)
    items.push(n)
  end 'add'

  function sum() returns int
    var total = 0
    for item in items 'loop'
      total = total + item
    end 'loop'
    return total
  end 'sum'
end 'Container'

function main() returns int
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
export typealias IntArray is Array with int

function makeArray() returns IntArray
  var arr = IntArray{}
  arr.push(42)
  return arr
end 'makeArray'

function main() returns int
  var arr = makeArray()
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: non-export-typealias-in-same-file -->
```maxon
typealias IntArray is Array with int

function main() returns int
  var arr = IntArray{}
  arr.push(42)
  return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: error.typealias-with-unknown-element-type -->
```maxon
typealias BadArray is Array with UnknownType

type Container
  var items BadArray
end 'Container'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/export-keyword/error.typealias-with-unknown-element-type.test:2:45: Unknown type: UnknownType
```
