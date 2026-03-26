---
feature: match-exhaustive-default-panic
status: experimental
keywords: [match, exhaustive, default, panic, throws]
category: control-flow
---

# Match Exhaustiveness & Default Panic

## Documentation

All match statements and expressions must be exhaustive. For enum and union types, this means every case must be explicitly covered, or a `default throws` / `default panic` must be provided. For other types (integers, strings, floats, characters), a `default` arm is always required since all values cannot be enumerated.

### `default panic("message")`

Use `default panic("message")` when unmatched cases represent a programming error that should terminate the program immediately. Unlike `default throws`, which throws a recoverable error, `default panic` halts the program with an error message and stack trace.

```text
match statusCode 'handle'
    200 then print("OK")
    404 then print("Not Found")
    default panic("unexpected status code")
end 'handle'
```

This works with both match statements and match expressions, and with all scrutinee types including enums, unions, and union range patterns:

```text
match color 'check'
    red then print("red")
    default panic("unhandled color")
end 'check'
```

## Tests

<!-- test: default-panic.statement -->
```maxon
function main() returns ExitCode
  var x = 2
  match x 'check'
    1 then return 10
    2 then return 20
    default panic("unexpected value")
  end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: default-panic.statement-hit -->
```maxon
function main() returns ExitCode
  var x = 99
  match x 'check'
    1 then return 10
    2 then return 20
    default panic("unexpected value")
  end 'check'
end 'main'
```
```exitcode
1
```
```stderr
panic at default-panic.statement-hit.test:7: unexpected value
Stack trace:
  in main
  in _start
```

<!-- test: default-panic.expression -->
```maxon
function main() returns ExitCode
  var x = 3
  let result = match x 'eval'
    1 gives 10
    2 gives 20
    3 gives 30
    default panic("unexpected value")
  end 'eval'
  return result
end 'main'
```
```exitcode
30
```

<!-- test: default-panic.enum-statement -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.red
  match c 'check'
    red then return 1
    default panic("unhandled color")
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: default-panic.enum-expression -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.green
  let result = match c 'eval'
    red gives 1
    green gives 2
    default panic("unhandled color")
  end 'eval'
  return result
end 'main'
```
```exitcode
2
```

<!-- test: default-panic.union-range-statement -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
  add
  sub
  mul
  exec(code Integer)
end 'Op'

function main() returns ExitCode
  var op = Op.sub
  match op 'check'
    add to mul then return 1
    default panic("unexpected op")
  end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: default-panic.union-range-expression -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
  add
  sub
  mul
  exec(code Integer)
end 'Op'

function main() returns ExitCode
  var op = Op.mul
  let result = match op 'eval'
    add to sub gives 1
    mul gives 2
    default panic("unexpected op")
  end 'eval'
  return result
end 'main'
```
```exitcode
2
```

<!-- test: error.no-default-statement -->
```maxon
function main() returns ExitCode
  var x = 1
  match x 'check'
    1 then return 10
    2 then return 20
  end 'check'
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-exhaustive-default-panic/error.no-default-statement.test:7:3: match is not exhaustive: add a 'default' arm
```

<!-- test: error.no-default-expression -->
```maxon
function main() returns ExitCode
  var x = 1
  let result = match x 'eval'
    1 gives 10
    2 gives 20
  end 'eval'
  return result
end 'main'
```
```maxoncstderr
error E2026: specs/fragments/match-exhaustive-default-panic/error.no-default-expression.test:7:3: match expression is not exhaustive: add a 'default' arm
```

<!-- test: error.enum-default-plain -->
```maxon
enum Color
    red
    green
    blue
end 'Color'

function main() returns ExitCode
  var c = Color.blue
  match c 'check'
    red then return 1
    default then return 0
  end 'check'
end 'main'
```
```maxoncstderr
error E2046: specs/fragments/match-exhaustive-default-panic/error.enum-default-plain.test:12:5: 'default' in a match on enum 'Color' must be followed by 'throws <error>' or 'panic("message")'
```
