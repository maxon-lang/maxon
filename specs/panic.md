---
feature: panic
status: experimental
keywords: [panic, abort, crash, runtime error]
category: runtime
---

# Panic

## Documentation

The `panic` statement immediately terminates the program with an error message and stack trace. It is used to signal unrecoverable errors — situations that represent bugs in the program rather than expected error conditions.

### Syntax

```text
panic("error message")
```

The argument must be a string literal. The program prints a panic message to stderr including the source file and line number, followed by a stack trace, then exits with code 1.

### Example

```text
function processValue(x int) returns int
    if x < 0 'negative'
        panic("processValue: negative input not allowed")
    end 'negative'
    return x * 2
end 'processValue'
```

Output when called with a negative value:
```text
panic at example.maxon:3: processValue: negative input not allowed
Stack trace:
  in example.processValue
  in example.main
  in _start
```

### When to Use

Use `panic` for invariant violations and unreachable code paths — situations that indicate a bug in the program. For expected error conditions (invalid user input, missing files, etc.), use error handling with `throw`/`try`/`otherwise` instead.

## Tests

<!-- test: panic.basic -->
```maxon
function main() returns ExitCode
    panic("something went wrong")
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.basic.test:3: something went wrong
Stack trace:
  in panic.main
  in _start
```

<!-- test: panic.in-function -->
```maxon
function fail()
    panic("failure in helper")
end 'fail'

function main() returns ExitCode
    fail()
    return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.in-function.test:3: failure in helper
Stack trace:
  in panic.fail
  in panic.main
  in _start
```

<!-- test: panic.after-condition -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(n Integer) returns Integer
    if n < 0 'negative'
        panic("negative value")
    end 'negative'
    return n
end 'check'

function main() returns ExitCode
    return check(Integer{0 - 1})
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.after-condition.test:6: negative value
Stack trace:
  in panic.check
  in panic.main
  in _start
```
