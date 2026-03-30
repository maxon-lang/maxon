---
feature: range-check-panic
status: experimental
keywords: [range, typealias, panic, runtime, bounds check]
category: runtime
---

# Range Check Panic

## Documentation

When a function returns a ranged typealias, the compiler inserts a runtime range check before the return. If the value is outside the declared range, the program panics with a message identifying the type and its bounds, followed by a stack trace.

### Example

```text
typealias Percent = int(0 to 100)

function clamp(x Percent) returns Percent
    return x
end 'clamp'
```

Calling `clamp(101)` produces:
```text
Range check failed: value outside typealias 'Percent'
Stack trace:
  in example.clamp
  in main
  in mrt_start
```

## Tests

<!-- test: range-check-panic.upper-bound -->
```maxon
typealias Percent = int(0 to 100)

function clamp(x Percent) returns Percent
  return x
end 'clamp'

function main() returns ExitCode
  let result = clamp(101)
  return result
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.upper-bound.test:5: Range check failed: value outside typealias 'Percent'
Stack trace:
  in range-check-panic.clamp
  in main
  in mrt_start
```

<!-- test: range-check-panic.lower-bound -->
```maxon
typealias Natural = int(0 to i64.max)

function check(n Natural) returns Natural
  return n
end 'check'

function main() returns ExitCode
  let result = check(-1)
  return result
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.lower-bound.test:5: Range check failed: value outside typealias 'Natural'
Stack trace:
  in range-check-panic.check
  in main
  in mrt_start
```

<!-- test: range-check-panic.in-range -->
```maxon
typealias SmallInt = int(0 to 10)

function check(x SmallInt) returns SmallInt
  return x
end 'check'

function main() returns ExitCode
  return check(5)
end 'main'
```
```exitcode
5
```

<!-- test: range-check-panic.nested-call -->
```maxon
typealias Score = int(0 to 100)

function validate(s Score) returns Score
  return s
end 'validate'

function process(x Score) returns Score
  return validate(x)
end 'process'

function main() returns ExitCode
  let result = process(200)
  return result
end 'main'
```
```exitcode
1
```
```stderr
panic at range-check-panic.nested-call.test:5: Range check failed: value outside typealias 'Score'
Stack trace:
  in range-check-panic.validate
  in range-check-panic.process
  in main
  in mrt_start
```
