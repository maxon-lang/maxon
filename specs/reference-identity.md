---
feature: reference-identity
status: experimental
keywords: [is, is not, reference, identity, pointer, equality]
category: operators
---

# Reference Identity

## Documentation

### Overview

In Maxon, all struct-typed variables are references (heap pointers). The `==` operator compares **contents** (value equality via `Equatable`), while the `is` operator compares **reference identity** — whether two variables point to the same object in memory.

Assignment (`var b = a`) creates a **copy** — `b` is a new, independent object with the same field values. To create a reference alias where both variables point to the same object, use `var b = ref a`. To check if two references point to the same object, use `is`.

### Operators

- `a is b` — returns `true` if `a` and `b` refer to the same object
- `a is not b` — returns `true` if `a` and `b` refer to different objects

### Example

```text
function areSame(a Point, b Point) returns bool
  return a is b
end 'areSame'

var p = Point{x: 1, y: 2}
areSame(p, b: p)  // true  — same reference passed twice
```

### Rules

- `is` and `is not` work on struct-typed values (including String, Array, and user-defined types).
- Using `is` or `is not` on primitive types (int, float, bool, byte) is a compile error — primitives are values, not references.
- Both operands must be the same type.

## Tests

<!-- test: self-identity -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  if a is a 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: ref-creates-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = ref a
  if a is b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: copy-creates-new-object -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = a
  if a is b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: different-objects -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = Point{x: 1, y: 2}
  if a is b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: is-not-operator -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = Point{x: 1, y: 2}
  if a is not b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-not-self -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  if a is not a 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: function-same-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
  export var value Integer
end 'Box'

function areSame(a Box, b Box) returns bool
  return a is b
end 'areSame'

function main() returns ExitCode
  var x = Box{value: 42}
  var y = Box{value: 42}
  var result = 0
  if areSame(x, b: x) 'same'
    result = result + 1
  end 'same'
  if areSame(x, b: y) 'diff'
    result = result + 10
  end 'diff'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: string-identity -->
```maxon
function main() returns ExitCode
  var a = "hello"
  var b = "hello"
  var result = 0
  if a is a 'self'
    result = result + 1
  end 'self'
  if a is b 'diff'
    result = result + 10
  end 'diff'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: mutation-through-ref -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = ref a
  b.x = 99
  return a.x
end 'main'
```
```exitcode
99
```

<!-- test: copy-isolates-mutation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var a = Point{x: 1, y: 2}
  var b = a
  b.x = 99
  return a.x
end 'main'
```
```exitcode
1
```

<!-- test: primitive-error -->
```maxon
function main() returns ExitCode
  var a = 42
  var b = 42
  if a is b 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```maxoncstderr
error E3068: specs/fragments/reference-identity/primitive-error.test:5:8: 'is' requires reference types (structs), not primitive values
```
