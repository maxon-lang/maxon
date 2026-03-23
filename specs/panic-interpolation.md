---
feature: panic-interpolation
status: experimental
keywords: [panic, string interpolation, runtime error, formatted message]
category: runtime
---

# Panic with String Interpolation

## Documentation

The `panic` statement supports string interpolation, allowing dynamic values to be included in panic messages. This makes it easier to provide context about what went wrong.

### Syntax

```text
panic("message with {expression}")
panic("value was {x}, expected {y}")
panic("index {i} out of bounds for length {len}")
```

The argument can be a plain string literal or an interpolated string. The program prints a panic message to stderr including the source file and line number, followed by a stack trace, then exits with code 1.

### Example

```text
function processValue(x int) returns int
    if x < 0 'negative'
        panic("processValue: got {x}, expected non-negative")
    end 'negative'
    return x * 2
end 'processValue'
```

Output when called with `-5`:
```text
panic at example.maxon:3: processValue: got -5, expected non-negative
Stack trace:
  in example.processValue
  in example.main
  in _start
```

## Tests

<!-- test: panic-interpolation.basic-int -->
```maxon
function main() returns ExitCode
    var x = 42
    panic("value is {x}")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.basic-int.test:4: value is 42
Stack trace:
  in main
  in _start
```

<!-- test: panic-interpolation.multiple-values -->
```maxon
function main() returns ExitCode
    var a = 10
    var b = 20
    panic("{a} != {b}")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.multiple-values.test:5: 10 != 20
Stack trace:
  in main
  in _start
```

<!-- test: panic-interpolation.expression -->
```maxon
function main() returns ExitCode
    var a = 3
    var b = 4
    panic("result: {a + b}")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.expression.test:5: result: 7
Stack trace:
  in main
  in _start
```

<!-- test: panic-interpolation.format-spec -->
```maxon
function main() returns ExitCode
    var x = 42
    panic("hex: {x:x}")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.format-spec.test:4: hex: 2a
Stack trace:
  in main
  in _start
```

<!-- test: panic-interpolation.in-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(n Integer)
    if n < 0 'neg'
        panic("check failed: {n} is negative")
    end 'neg'
end 'check'

function main() returns ExitCode
    check(Integer{-5})
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.in-function.test:6: check failed: -5 is negative
Stack trace:
  in panic-interpolation.check
  in main
  in _start
```

<!-- test: panic-interpolation.plain-string-unchanged -->
```maxon
function main() returns ExitCode
    panic("simple message")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation.plain-string-unchanged.test:3: simple message
Stack trace:
  in main
  in _start
```
