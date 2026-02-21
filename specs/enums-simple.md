---
feature: enums
status: experimental
keywords: [enum, enumeration, associated values, raw values]
category: type-system
---

# Enums

## Documentation

# Enums

Enums define a type with a fixed set of named variants called cases. Maxon supports three kinds of enums: simple enums, raw value enums, and enums with associated values.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Raw Value Enums

All enums support `.rawValue` access. Simple enums return their ordinal (0, 1, 2...), while backed enums return their explicit value.

#### Integer-backed Enums

```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

var status = HttpStatus.ok
var code = status.rawValue    // 200
```

#### Float-backed Enums

```maxon
enum Weights
  light = 1.5
  medium = 2.5
  heavy = 3.5
end 'Weights'

var w = Weights.medium
if w.rawValue > 2.0 'check'
  // weight is above 2.0
end 'check'
```

#### Simple Enum rawValue

Simple enums (no explicit values) also support `.rawValue`, returning their ordinal:

```maxon
enum Color
  red     // rawValue = 0
  green   // rawValue = 1
  blue    // rawValue = 2
end 'Color'

var c = Color.green
var ordinal = c.rawValue  // 1
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
  north
  south
  east
  west

  function opposite() returns Direction
    match self 'check'
      Direction.north then return Direction.south
      Direction.south then return Direction.north
      Direction.east then return Direction.west
      Direction.west then return Direction.east
    end 'check'
  end 'opposite'

  function isVertical() returns bool
    var result = match self 'check'
      Direction.north gives true
      Direction.south gives true
      Direction.east gives false
      Direction.west gives false
    end 'check'
    return result
  end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

### fromRawValue

The `fromRawValue` static method converts a raw value back to an enum. It returns an error union that succeeds with the enum value if the raw value matches a case, or fails with `EnumError.invalidName` if no match is found.

For simple enums (no explicit backing values), the raw value is the ordinal (0, 1, 2, ...):

```maxon
enum Color
  red     // ordinal 0
  green   // ordinal 1
  blue    // ordinal 2
end 'Color'

var c = try Color.fromRawValue(1) otherwise Color.red  // Color.green
```

For backed enums, the raw value is the explicit backing value:

```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

var status = try HttpStatus.fromRawValue(404) otherwise HttpStatus.ok
// status == HttpStatus.notFound
```

## Tests

<!-- test: simple-enum -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var dir = Direction.north
  var result = match dir 'check'
    Direction.north gives 1
    Direction.south gives 0
    Direction.east gives 0
    Direction.west gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-assignment -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.red
  c = Color.blue
  var result = match c 'check'
    Color.red gives 0
    Color.green gives 0
    Color.blue gives 1
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-not-equal -->
```maxon
enum Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s = Status.pending
  var result = match s 'check'
    Status.active gives 0
    Status.pending gives 1
    Status.done gives 1
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-comparison -->
```maxon
enum Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s1 = Status.pending
  match s1 'check'
    Status.pending then return 1
    Status.active then return 0
    Status.done then return 0
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-function-param -->
```maxon
enum Status
  on
  off
end 'Status'

function isOn(s Status) returns bool
  var result = match s 'check'
    Status.on gives true
    Status.off gives false
  end 'check'
  return result
end 'isOn'

function main() returns ExitCode
  var status = Status.on
  if isOn(status) 'test'
    return 1
  end 'test'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-return-type -->
```maxon
enum Result
  success
  failure
end 'Result'

function getResult(succeed bool) returns Result
  if succeed 'check'
    return Result.success
  end 'check'
  return Result.failure
end 'getResult'

function main() returns ExitCode
  var r = getResult(true)
  var result = match r 'handle'
    Result.success gives 1
    Result.failure gives 0
  end 'handle'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: raw-value-int -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var status = HttpStatus.ok
  if status.rawValue == 200 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: raw-value-int-comparison -->
```maxon
enum Priority
  low = 1
  medium = 5
  high = 10
end 'Priority'

function main() returns ExitCode
  var p = Priority.high
  if p.rawValue > 5 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method -->
```maxon
enum Direction
  north
  south

  function isNorth() returns bool
    var result = match self 'check'
      Direction.north gives true
      Direction.south gives false
    end 'check'
    return result
  end 'isNorth'
end 'Direction'

function main() returns ExitCode
  let d = Direction.north
  if d.isNorth() 'test'
    return 1
  end 'test'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method-returns-enum -->
```maxon
enum Toggle
  on
  off

  function flip() returns Toggle
    var result = match self 'check'
      Toggle.on gives Toggle.off
      Toggle.off gives Toggle.on
    end 'check'
    return result
  end 'flip'
end 'Toggle'

function main() returns ExitCode
  let t = Toggle.on
  let flipped = t.flip()
  var result = match flipped 'check'
    Toggle.off gives 1
    Toggle.on gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
enum Color
  red
  red
end 'Color'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/enums-simple/error.duplicate-case.test:4:3: duplicate enum case: 'red'
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
  red
  blue
end 'Color'

function main() returns ExitCode
  let _c = Color.green
  return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enums-simple/error.unknown-enum-case.test:8:12: unknown enum case: 'green'
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status
  ok = 200
  success = 200
end 'Status'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3031: specs/fragments/enums-simple/error.duplicate-raw-value.test:4:3: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status
  ok = 100
  fail = 5.0
end 'Status'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enums-simple/error.raw-value-type-mismatch.test:4:3: raw value type mismatch: 'expected int, got float'
```


<!-- test: simple-enum-rawvalue -->
```maxon
enum Direction
  north
  south
  east
end 'Direction'

function main() returns ExitCode
  var d = Direction.south
  return d.rawValue
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum FloatBacked
  North = 1.1
  South = 2.2
  East = 3.3
end 'FloatBacked'

function main() returns ExitCode
  var f = FloatBacked.North
  var result = match f 'check'
    FloatBacked.North gives 1
    FloatBacked.South gives 0
    FloatBacked.East gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-rawvalue -->
```maxon
enum Weights
  light = 1.5
  medium = 2.5
  heavy = 3.5
end 'Weights'

function main() returns ExitCode
  var w = Weights.medium
  var rawVal = w.rawValue
  if rawVal > 2.0 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-backed-rawvalue -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var status = HttpStatus.notFound
  var code = status.rawValue
  if code == 404 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-rawvalue-in-function -->
```maxon

typealias Float = float(f64.min to f64.max)

enum Weights
  light = 1.5
  medium = 2.5
end 'Weights'

function getRaw(w Weights) returns Float
  return w.rawValue
end 'getRaw'

function main() returns ExitCode
  var w = Weights.medium
  var raw = getRaw(w)
  if raw > 2.0 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-rawvalue-in-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function getCode(s HttpStatus) returns Integer
  return s.rawValue
end 'getCode'

function main() returns ExitCode
  var status = HttpStatus.notFound
  var code = getCode(status)
  if code == 404 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: keyword-as-case-name -->
Keywords can be used as enum case names (e.g., `function`, `return`, `end`).
```maxon
enum TokenType
  function
  return
  end
  if
  else
  let
  var
  identifier
end 'TokenType'

function main() returns ExitCode
  var t = TokenType.function
  var result = match t 'check'
    TokenType.function gives 1
    TokenType.return gives 0
    TokenType.end gives 0
    TokenType.if gives 0
    TokenType.else gives 0
    TokenType.let gives 0
    TokenType.var gives 0
    TokenType.identifier gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
1
```

<!-- test: keyword-case-rawvalue -->
Keyword-named enum cases have correct ordinal raw values.
```maxon
enum TokenType
  function
  return
  end
  if
end 'TokenType'

function main() returns ExitCode
  var t = TokenType.end
  return t.rawValue
end 'main'
```
```exitcode
2
```

<!-- test: keyword-case-with-method -->
Enums with keyword case names can also have methods.
```maxon
enum TokenType
  function
  return
  end
  if

  function isKeyword() returns bool
    return true
  end 'isKeyword'
end 'TokenType'

function main() returns ExitCode
  var t = TokenType.function
  if t.isKeyword() 'check'
    return t.rawValue
  end 'check'
  return 99
end 'main'
```
```exitcode
0
```

