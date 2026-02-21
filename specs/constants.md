---
feature: constants
status: experimental
keywords: [constants, named constants, integer constants]
category: type-system
---

# Constants

## Documentation

# Constants

Constants define a type with a fixed set of named integer values. Unlike enums, constants are integer-only, have no methods, no `.rawValue`, no `.name`, no `fromRawValue()`, and no `fromName()`. They are a distinct type — you cannot mix them with plain `int` without an explicit cast.

### Basic Constants

```text
constants HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'
```

Create constant values using dot notation:

```text
var status = HttpStatus.ok
```

### Auto-Increment Values

Cases without explicit values auto-increment from 0 (or from the previous explicit value + 1):

```text
constants Color
  red       // 0
  green     // 1
  blue      // 2
end 'Color'
```

### Mixed Explicit and Auto-Increment

```text
constants Priority
  low         // 0
  medium      // 1
  high = 10
  critical    // 11
end 'Priority'
```

### Type Safety

Constants are a distinct type. Use `as int` to convert to an integer, and `as ConstantsType` to convert from an integer:

```text
var status = HttpStatus.ok
var code = status as int        // 200
var s2 = 404 as HttpStatus      // HttpStatus.notFound
```

### Comparison

Constants of the same type can be compared using `==` and `!=`:

```text
var status = HttpStatus.ok
if status == HttpStatus.notFound 'check'
  // ...
end 'check'
```

### Export

Constants can be exported for cross-file visibility:

```text
export constants Flags
  none = 0
  read = 1
  write = 2
end 'Flags'
```

## Tests

<!-- disabled-test: simple-constants -->
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

<!-- disabled-test: constants-explicit-values -->
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

<!-- disabled-test: constants-auto-increment -->
```maxon
constants Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.blue
  return c as int
end 'main'
```
```exitcode
2
```

<!-- disabled-test: constants-mixed-values -->
```maxon
constants Priority
  low
  medium
  high = 10
  critical
end 'Priority'

function main() returns ExitCode
  return Priority.critical as int
end 'main'
```
```exitcode
11
```

<!-- disabled-test: constants-negative-values -->
```maxon
constants Temperature
  freezing = 0
  cold = -10
  warm = 25
end 'Temperature'

function main() returns ExitCode
  var t = Temperature.warm
  return t as int
end 'main'
```
```exitcode
25
```

<!-- disabled-test: constants-cast-to-int -->
```maxon
constants HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  var s = HttpStatus.ok
  var code = s as int
  if code == 200 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- disabled-test: constants-cast-from-int -->
```maxon
constants HttpStatus
  ok = 200
  notFound = 404
end 'HttpStatus'

function main() returns ExitCode
  var s = 404 as HttpStatus
  if s == HttpStatus.notFound 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
1
```

<!-- disabled-test: constants-not-equal -->
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

<!-- disabled-test: constants-assignment -->
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

<!-- disabled-test: constants-function-param -->
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

<!-- disabled-test: constants-return-type -->
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

<!-- disabled-test: constants-keyword-as-case-name -->
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

<!-- disabled-test: constants-match -->
Constants can be used in match statements like integers.
```maxon
constants Color
  red
  green
  blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  var result = match c as int 'check'
    0 gives 10
    1 gives 20
    2 gives 30
    default gives 0
  end 'check'
  return result
end 'main'
```
```exitcode
20
```

<!-- disabled-test: constants-in-arithmetic -->
Constants can participate in arithmetic after casting to int.
```maxon
constants Offset
  small = 10
  medium = 20
  large = 30
end 'Offset'

function main() returns ExitCode
  var o = Offset.medium
  var result = (o as int) + 5
  return result
end 'main'
```
```exitcode
25
```

<!-- disabled-test: constants-top-level -->
Constants can be used as top-level constant initializers.
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

<!-- disabled-test: constants-export -->
Exported constants can be used from other files.
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
  return p as int
end 'main'
```
```exitcode
1
```

<!-- disabled-test: error.duplicate-case -->
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

<!-- disabled-test: error.duplicate-value -->
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

<!-- disabled-test: error.non-integer-value-float -->
```maxon
constants Weights
  light = 1.5
end 'Weights'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/constants/error.non-integer-value-float.test:3:3: constants only support integer values: 'got float'
```

<!-- disabled-test: error.non-integer-value-string -->
```maxon
constants Names
  first = "hello"
end 'Names'

function main() returns ExitCode
  return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/constants/error.non-integer-value-string.test:3:3: constants only support integer values: 'got String'
```

<!-- disabled-test: error.unknown-case -->
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
