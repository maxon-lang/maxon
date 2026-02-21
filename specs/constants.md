---
feature: constants
status: experimental
keywords: [constants, named constants]
category: type-system
---

## Documentation

# Constants

Constants define a named group of typed constant values. Unlike enums, constants support direct `==` and `!=` comparison, and have no methods, no `.rawValue`, no `.name`, no `fromRawValue()`, and no `fromName()`.

### Integer Constants

Cases without explicit values auto-increment from 0 (or from the previous explicit value + 1):

```text
constants Color
  red       // 0
  green     // 1
  blue      // 2
end 'Color'

constants HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

constants Priority
  low         // 0
  medium      // 1
  high = 10
  critical    // 11
end 'Priority'
```

### Float Constants

```text
constants Threshold
  low = 0.1
  medium = 0.5
  high = 0.9
end 'Threshold'
```

### String Constants

```text
constants ContentType
  json = "application/json"
  html = "text/html"
  plain = "text/plain"
end 'ContentType'
```

### Character Constants

```text
constants Escape
  newline = '\n'
  tab = '\t'
  null = '\0'
end 'Escape'
```

### Comparison

Constants support `==` and `!=` directly:

```text
var status = HttpStatus.ok
if status == HttpStatus.ok 'check'
  // ...
end 'check'
```

### Export

```text
export constants Permission
  none = 0
  read = 1
  write = 2
end 'Permission'
```

## Tests

<!-- test: basic-int -->
```maxon
constants Color
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
constants HttpStatus
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
constants Color
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
    default gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
30
```

<!-- test: mixed-values -->
```maxon
constants Priority
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
    default gives 99
  end 'check'
  return result
end 'main'
```
```exitcode
11
```

<!-- test: negative-int -->
```maxon
constants Temperature
  freezing = 0
  cold = -10
  warm = 25
end 'Temperature'

function main() returns ExitCode
  var result = match Temperature.warm 'check'
    Temperature.freezing gives 0
    Temperature.cold gives -10
    Temperature.warm gives 25
    default gives 99
  end 'check'
  return result
end 'main'
```
```exitcode
25
```

<!-- test: not-equal -->
```maxon
constants Color
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
constants Direction
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
constants Status
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
constants Result
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
Keywords can be used as constants case names.
```maxon
constants TokenKind
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
constants Threshold
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
constants ContentType
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
constants Escape
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
constants Color
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
export constants Permission
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
constants HttpStatus
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
    default gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
2
```

<!-- test: string-interpolation -->
Integer constants interpolate as their backing value, not their case name.
```maxon
constants HttpStatus
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
constants Color
  red
  red
end 'Color'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/constants/error.duplicate-case.test:4:3: duplicate constants case: 'red'
```

<!-- test: error.duplicate-value -->
```maxon
constants Status
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
constants Mixed
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
Integer-backed constants can be used in arithmetic expressions with integers.
```maxon
constants Step
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
Integer-backed constants can be used to compute array indices.
```maxon
constants Slot
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
Integer-backed constants can be compared with integer values.
```maxon
constants State
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
Float-backed constants can be used in arithmetic with floats.
```maxon
constants Weight
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
Float-backed constants can be compared with float values.
```maxon
constants Threshold
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
String-backed constants can be compared with string values.
```maxon
constants ContentType
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
Character-backed constants can be compared with character values.
```maxon
constants Escape
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
constants Color
  red
  blue
end 'Color'

function main() returns ExitCode
  let _c = Color.green
  return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/constants/error.unknown-case.test:8:12: unknown constants case: 'green'
```
