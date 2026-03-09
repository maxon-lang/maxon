---
feature: enum
status: experimental
keywords: [enum, named constants]
category: type-system
---

## Documentation

# Enum

Enum define a named group of typed constant values. Unlike unions, enum support direct `==` and `!=` comparison and have no methods.

### Integer Enum

Cases without explicit values auto-increment from 0 (or from the previous explicit value + 1):

```text
enum Color
  red       // 0
  green     // 1
  blue      // 2
end 'Color'

enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

enum Priority
  low         // 0
  medium      // 1
  high = 10
  critical    // 11
end 'Priority'
```

### Float Enum

```text
enum Threshold
  low = 0.1
  medium = 0.5
  high = 0.9
end 'Threshold'
```

### String Enum

```text
enum ContentType
  json = "application/json"
  html = "text/html"
  plain = "text/plain"
end 'ContentType'
```

### Character Enum

```text
enum Escape
  newline = '\n'
  tab = '\t'
  null = '\0'
end 'Escape'
```

### Raw Value Access

All enum support `.rawValue` access. Simple enum return their ordinal (0, 1, 2...), while backed enum return their explicit value.

```text
enum Color
  red       // rawValue = 0
  green     // rawValue = 1
  blue      // rawValue = 2
end 'Color'

var c = Color.green
var ordinal = c.rawValue  // 1
```

```text
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

var status = HttpStatus.ok
var code = status.rawValue    // 200
```

### name

All enum have a `.name` property that returns the member's name as a String:

```text
enum Color
  red
  green
  blue
end 'Color'

var c = Color.green
var n = c.name  // "green"
```

This is different from `rawValue` for backed enum - `name` always returns the case name:

```text
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

var s = HttpStatus.notFound
var code = s.rawValue  // 404
var n = s.name         // "notFound"
```

### fromRawValue

The `fromRawValue` static method converts a raw value back to an enum. It returns an error union that succeeds with the enum value if the raw value matches a case, or fails if no match is found.

For simple enum (no explicit backing values), the raw value is the ordinal (0, 1, 2, ...):

```text
enum Color
  red     // ordinal 0
  green   // ordinal 1
  blue    // ordinal 2
end 'Color'

var c = try Color.fromRawValue(1) otherwise Color.red  // Color.green
```

For backed enum, the raw value is the explicit backing value:

```text
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

var status = try HttpStatus.fromRawValue(404) otherwise HttpStatus.ok
// status is HttpStatus.notFound
```

### fromName

The `fromName` static method converts a string name back to an enum. It returns an error union that succeeds with the enum value if the name matches a case, or fails if no match is found.

```text
enum Color
  red
  green
  blue
end 'Color'

var c = try Color.fromName("green") otherwise Color.red  // Color.green
```

### Comparison

Enum support `==` and `!=` directly:

```text
var status = HttpStatus.ok
if status == HttpStatus.ok 'check'
  // ...
end 'check'
```

### Export

```text
export enum Permission
  none = 0
  read = 1
  write = 2
end 'Permission'
```

## Tests

<!-- test: basic-int -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  if c == Color.green 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-int-values -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.notFound
  if s == HttpStatus.notFound 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: auto-increment -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.blue
  var result = match c 'check'
    Color.red gives 10
    Color.green gives 20
    Color.blue gives 30
  end 'check'
  return result
end 'main'
```
```exitcode
30
```

<!-- test: mixed-values -->
```maxon
enum Priority
  low
  medium
  high = 10
  critical
end 'Priority'

function main() returns ExitCode
  var result = match Priority.critical 'check'
    Priority.low gives 0
    Priority.medium gives 1
    Priority.high gives 10
    Priority.critical gives 11
  end 'check'
  return result
end 'main'
```
```exitcode
11
```

<!-- test: negative-int -->
```maxon
enum Temperature
  freezing = 0
  cold = -10
  warm = 25
end 'Temperature'

function main() returns ExitCode
  var result = match Temperature.warm 'check'
    Temperature.freezing gives 0
    Temperature.cold gives -10
    Temperature.warm gives 25
  end 'check'
  return result
end 'main'
```
```exitcode
25
```

<!-- test: not-equal -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.red
  if c != Color.blue 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: assignment -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var d = Direction.north
  d = Direction.west
  if d == Direction.west 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: function-param -->
```maxon
enum Status
  on
  off
end 'Status'

function isOn(s Status) returns bool
  if s == Status.on 'check'
    return true
  end 'check'
  return false
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

<!-- test: return-type -->
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
  if r == Result.success 'test'
    return 1
  end 'test'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: keyword-as-case-name -->
Keywords can be used as enum case names.
```maxon
enum TokenKind
  function
  return
  end
  if
end 'TokenKind'

function main() returns ExitCode
  var t = TokenKind.function
  if t == TokenKind.function 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum Threshold
  low = 0.1
  medium = 0.5
  high = 0.9
end 'Threshold'

function main() returns ExitCode
  var t = Threshold.medium
  if t == Threshold.medium 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-backed -->
```maxon
enum ContentType
  json = "application/json"
  html = "text/html"
  plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
  var ct = ContentType.json
  if ct == ContentType.json 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: char-backed -->
```maxon
enum Escape
  newline = '\n'
  tab = '\t'
end 'Escape'

function main() returns ExitCode
  var e = Escape.newline
  if e == Escape.newline 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: top-level-constant -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

let DEFAULT_COLOR = Color.green

function main() returns ExitCode
  if DEFAULT_COLOR == Color.green 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: export -->
```maxon
// --- file: defs.maxon
export enum Permission
  none = 0
  read = 1
  write = 2
end 'Permission'

// --- file: main.maxon
function main() returns ExitCode
  var p = Permission.read
  if p == Permission.read 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: match-with-default -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.notFound
  var result = match s 'check'
    HttpStatus.ok gives 1
    HttpStatus.notFound gives 2
    HttpStatus.serverError gives 3
  end 'check'
  return result
end 'main'
```
```exitcode
2
```

<!-- test: string-interpolation -->
Integer enum interpolate as their backing value, not their case name.
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.notFound
  var msg = "status: {s}"
  if msg == "status: 404" 'check'
    return 1
  end 'check'
  return 0
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
error E3030: specs/fragments/constants/error.duplicate-case.test:4:3: duplicate enum case: 'red'
```

<!-- test: error.duplicate-value -->
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
error E3031: specs/fragments/constants/error.duplicate-value.test:4:3: duplicate raw value: '200'
```

<!-- test: error.mixed-backing-types -->
```maxon
enum Mixed
  first = 1
  second = "two"
end 'Mixed'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/constants/error.mixed-backing-types.test:4:3: raw value type mismatch: 'expected int, got String'
```

<!-- test: arithmetic-with-int -->
Integer-backed enum can be used in arithmetic expressions with integers.
```maxon
enum Step
  first
  second
  third
end 'Step'

function main() returns ExitCode
  let stride = 10
  let offset = Step.second * stride
  return offset
end 'main'
```
```exitcode
10
```

<!-- test: arithmetic-as-array-index -->
Integer-backed enum can be used to compute array indices.
```maxon
enum Slot
  a
  b
  c
end 'Slot'

let NUM_SLOTS = 3

function main() returns ExitCode
  let idx = Slot.b * NUM_SLOTS + Slot.a
  return idx
end 'main'
```
```exitcode
3
```

<!-- test: comparison-with-int -->
Integer-backed enum can be compared with integer values.
```maxon
enum State
  idle
  running
  done
end 'State'

function main() returns ExitCode
  var s = State.running
  if s >= State.idle and s <= State.done 'inRange'
    return 1
  end 'inRange'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-arithmetic -->
Float-backed enum can be used in arithmetic with floats.
```maxon
enum Weight
  light = 0.5
  heavy = 2.0
end 'Weight'

function main() returns ExitCode
  let scale = 4.0
  let result = Weight.light * scale
  if result == 2.0 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-comparison -->
Float-backed enum can be compared with float values.
```maxon
enum Threshold
  low = 0.1
  high = 0.9
end 'Threshold'

function main() returns ExitCode
  let val = 0.5
  if val > Threshold.low and val < Threshold.high 'inRange'
    return 1
  end 'inRange'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-comparison -->
String-backed enum can be compared with string values.
```maxon
enum ContentType
  json = "application/json"
  html = "text/html"
end 'ContentType'

function main() returns ExitCode
  let ct = "application/json"
  if ct == ContentType.json 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: char-comparison -->
Character-backed enum can be compared with character values.
```maxon
enum Escape
  newline = '\n'
  tab = '\t'
end 'Escape'

function main() returns ExitCode
  let ch = '\n'
  if ch == Escape.newline 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.unknown-case -->
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
error E3034: specs/fragments/constants/error.unknown-case.test:8:12: unknown enum case: 'green'
```

### Raw Value Access Tests

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

<!-- test: explicit-string-backed-rawvalue -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
end 'Planet'

function main() returns ExitCode
  var p = Planet.mars
  var name = p.rawValue
  if name == "Mars" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-char-backed-rawvalue -->
```maxon
enum CardSuit
  Hearts = 'H'
  Diamonds = 'D'
  Spades = 'S'
end 'CardSuit'

function main() returns ExitCode
  var suit = CardSuit.Diamonds
  var ch = suit.rawValue
  if ch == 'D' 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-rawvalue-dynamic-comparison -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
end 'Planet'

function getName() returns String
  return "Mars"
end 'getName'

function main() returns ExitCode
  var p = Planet.mars
  var name = p.rawValue
  var expected = getName()
  if name == expected 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-rawvalue-after-reassign -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
end 'Planet'

function main() returns ExitCode
  var p = Planet.earth
  p = Planet.mars
  var name = p.rawValue
  if name == "Mars" 'check'
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

<!-- test: string-rawvalue-function-param -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
  venus = "Venus"
end 'Planet'

function getName(p Planet) returns String
  return p.rawValue
end 'getName'

function main() returns ExitCode
  var planet = Planet.mars
  var name = getName(planet)
  if name == "Mars" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: char-rawvalue-function-param -->
```maxon
enum Grade
  excellent = 'A'
  good = 'B'
  average = 'C'
end 'Grade'

function getLetter(g Grade) returns Character
  return g.rawValue
end 'getLetter'

function main() returns ExitCode
  var grade = Grade.good
  var letter = getLetter(grade)
  if letter == 'B' 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### Name Property Tests

<!-- test: name-simple -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  var n = c.name
  if n == "green" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-int-backed -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.notFound
  if s.name == "notFound" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-string-backed -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
end 'Planet'

function main() returns ExitCode
  var p = Planet.mars
  // rawValue is "Mars", name is "mars"
  if p.name == "mars" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-from-function -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function getName(d Direction) returns String
  return d.name
end 'getName'

function main() returns ExitCode
  var d = Direction.west
  var n = getName(d)
  if n == "west" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-reassign -->
```maxon
enum Status
  pending
  active
  done
end 'Status'

function main() returns ExitCode
  var s = Status.pending
  s = Status.done
  if s.name == "done" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-float-backed -->
```maxon
enum Weights
  light = 1.5
  medium = 2.5
  heavy = 3.5
end 'Weights'

function main() returns ExitCode
  var w = Weights.heavy
  if w.name == "heavy" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: name-char-backed -->
```maxon
enum CardSuit
  Hearts = 'H'
  Diamonds = 'D'
  Spades = 'S'
end 'CardSuit'

function main() returns ExitCode
  var s = CardSuit.Diamonds
  if s.name == "Diamonds" 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

### fromRawValue Tests

<!-- test: fromRawValue-simple -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = try Color.fromRawValue(1) otherwise Color.red
  if c == Color.green 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-int-backed -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

function main() returns ExitCode
  var status = try HttpStatus.fromRawValue(404) otherwise HttpStatus.ok
  if status == HttpStatus.notFound 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-float-backed -->
```maxon
enum Weights
  light = 1.5
  medium = 2.5
  heavy = 3.5
end 'Weights'

function main() returns ExitCode
  var w = try Weights.fromRawValue(2.5) otherwise Weights.light
  if w == Weights.medium 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-string-backed -->
```maxon
enum Planet
  earth = "Earth"
  mars = "Mars"
end 'Planet'

function main() returns ExitCode
  var p = try Planet.fromRawValue("Mars") otherwise Planet.earth
  if p == Planet.mars 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-char-backed -->
```maxon
enum Grade
  excellent = 'A'
  good = 'B'
  average = 'C'
end 'Grade'

function main() returns ExitCode
  var g = try Grade.fromRawValue('B') otherwise Grade.average
  if g == Grade.good 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-runtime -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function getCode() returns Integer
  return 404
end 'getCode'

function main() returns ExitCode
  var code = getCode()
  var status = try HttpStatus.fromRawValue(code) otherwise HttpStatus.ok
  if status == HttpStatus.notFound 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromRawValue-failure -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function getCode() returns Integer
  return 999
end 'getCode'

function main() returns ExitCode
  var code = getCode()
  var status = try HttpStatus.fromRawValue(code) otherwise HttpStatus.ok
  if status == HttpStatus.ok 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.fromRawValue-invalid-literal -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  let _s = try HttpStatus.fromRawValue(999) otherwise HttpStatus.ok
  return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/constants/error.fromRawValue-invalid-literal.test:8:27: no enum case with raw value '999': 'HttpStatus'
```

<!-- test: error.fromRawValue-type-mismatch -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  let _s = try HttpStatus.fromRawValue("404") otherwise HttpStatus.ok
  return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/constants/error.fromRawValue-type-mismatch.test:8:27: type mismatch: 'expected int, got String'
```

### fromName Tests

<!-- test: fromName-simple-success -->
```maxon
enum Direction
  north
  south
  east
  west
end 'Direction'

function main() returns ExitCode
  var dir = try Direction.fromName("south") otherwise Direction.north
  if dir == Direction.south 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromName-simple-failure -->
```maxon
enum Direction
  north
  south
end 'Direction'

function getInvalidName() returns String
  return "invalid"
end 'getInvalidName'

function main() returns ExitCode
  var name = getInvalidName()
  var dir = try Direction.fromName(name) otherwise Direction.north
  if dir == Direction.north 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromName-int-backed -->
```maxon
enum HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  var status = try HttpStatus.fromName("notFound") otherwise HttpStatus.ok
  if status.rawValue == 404 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: fromName-dynamic -->
```maxon
enum Color
  red
  green
  blue
end 'Color'

function getName() returns String
  return "green"
end 'getName'

function main() returns ExitCode
  var name = getName()
  var color = try Color.fromName(name) otherwise Color.red
  if color == Color.green 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: error.fromName-invalid-case -->
```maxon
enum Direction
  north
  south
end 'Direction'

function main() returns ExitCode
  let _d = try Direction.fromName("invalid_case_name_that_does_not_exist") otherwise Direction.north
  return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/constants/error.fromName-invalid-case.test:8:26: no enum case named 'invalid_case_name_that_does_not_exist': 'Direction'
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

<!-- test: keyword-case-with-method-rawvalue -->
Keyword-named enum cases can use rawValue.
```maxon
enum TokenType
  function
  return
  end
  if
end 'TokenType'

function main() returns ExitCode
  var t = TokenType.function
  return t.rawValue
end 'main'
```
```exitcode
0
```
