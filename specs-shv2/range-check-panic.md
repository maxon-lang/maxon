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

The CHECK is the language's, and it fires on every target: the program always stops, and always with
a non-zero exit code. The MESSAGE and the STACK TRACE above are the Windows `mrt_panic` runtime
chunk's, and no other target has one yet — x64-linux, arm64-macos, arm64-linux and wasm32-wasi all
exit 1 with empty stderr. That is why the cases below that pin stderr are the only ones here
restricted to `x64-windows`; the in-range case beside them runs everywhere.

## Tests

<!-- test: range-check-panic.upper-bound -->
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
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
  in clamp
  in main
  in mrt_start
```

<!-- test: range-check-panic.lower-bound -->
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
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
  in check
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
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
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
  in validate
  in process
  in main
  in mrt_start
```
