---
feature: enum-ordinal
status: experimental
keywords: [enum, ordinal, position]
category: type-system
---

## Documentation

# Enum Ordinal

All enum have an `.ordinal` property that returns the zero-based position of the case in its declaration order, always as an `int`.

This is different from `.rawValue` for backed enum — `.ordinal` always returns the declaration position:

```text
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

var s = HttpStatus.notFound
var pos = s.ordinal    // 1 (second case declared)
var code = s.rawValue  // 404 (the backing value)
```

For simple enum (no explicit values), `.ordinal` and `.rawValue` are identical:

```text
enum Color
  red       // ordinal 0, rawValue 0
  green     // ordinal 1, rawValue 1
  blue      // ordinal 2, rawValue 2
end 'Color'
```

## Tests

### Simple Enum

<!-- test: enum-ordinal.simple -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  if c.ordinal == 1 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### All Cases

<!-- test: enum-ordinal.all-cases -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var n = Direction.north
  var s = Direction.south
  var e = Direction.east
  var w = Direction.west
  if n.ordinal == 0 and s.ordinal == 1 and e.ordinal == 2 and w.ordinal == 3 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Int-Backed Enum

<!-- test: enum-ordinal.int-backed -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.serverError
  // ordinal is 2 (third case), not 500 (the raw value)
  if s.ordinal == 2 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Float-Backed Enum

<!-- test: enum-ordinal.float-backed -->
```maxon
enum Threshold
  low = 0.1
  medium = 0.5
  high = 0.9
end 'Threshold'

function main() returns ExitCode
  var t = Threshold.high
  if t.ordinal == 2 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### String-Backed Enum

<!-- test: enum-ordinal.string-backed -->
```maxon
enum ContentType
  json = "application/json"
  html = "text/html"
  plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
  var ct = ContentType.html
  if ct.ordinal == 1 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Char-Backed Enum

<!-- test: enum-ordinal.char-backed -->
```maxon
enum Grade
  a = 'A'
  b = 'B'
  c = 'C'
end 'Grade'

function main() returns ExitCode
  var g = Grade.c
  if g.ordinal == 2 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Ordinal in Arithmetic

<!-- test: enum-ordinal.arithmetic -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.blue
  var result = c.ordinal + 10
  if result == 12 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Ordinal from Function

<!-- test: enum-ordinal.from-function -->
```maxon
enum Priority
  low
  medium
  high
end 'Priority'

typealias OrdinalValue = int(0 to 100)

function getOrdinal(p Priority) returns OrdinalValue
  return p.ordinal
end 'getOrdinal'

function main() returns ExitCode
  var p = Priority.high
  if getOrdinal(p) == 2 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Error: Ordinal on Union

<!-- test: enum-ordinal.error-union-ordinal -->
```maxon
union Shape
  circle
  square
end 'Shape'

function main() returns ExitCode
  var s = Shape.circle
  var o = s.ordinal
  return 0
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/enum-ordinal/enum-ordinal.error-union-ordinal.test:9:13: union type 'Shape' has no property 'ordinal'
```
