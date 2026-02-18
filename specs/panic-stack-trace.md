---
feature: Panic Stack Trace
status: implemented
category: runtime
---

## Notes

When a runtime panic occurs (e.g., ranged type check failure), the program prints a stack trace to stderr showing the call chain from the panicking function back to `_start`. This helps developers identify where the error occurred.

The stack trace is printed after the panic message and walks the RBP chain to resolve function names from an embedded symbol table.

## Tests

### Simple panic with stack trace

<!-- test: simple-panic -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to 255)

function dangerous(value Integer) returns Byte
  return Byte{value}
end

function main() returns ExitCode
  let _result = dangerous(Integer{300})
  return 0 as ExitCode
end
```
```exitcode
1
```
```stderr
panic at simple-panic.test:6: Range check failed for type 'Byte': value outside int(0 to 255)
Stack trace:
  in panic-stack-trace.dangerous
  in panic-stack-trace.main
  in _start
```

### Nested call chain

<!-- test: nested-calls -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SmallInt = int(0 to 100)

function validate(n Integer) returns SmallInt
  return SmallInt{n}
end

function process(n Integer) returns SmallInt
  return validate(n)
end

function caller(n Integer) returns SmallInt
  return process(n)
end

function main() returns ExitCode
  let _result = caller(Integer{999})
  return 0 as ExitCode
end
```
```exitcode
1
```
```stderr
panic at nested-calls.test:6: Range check failed for type 'SmallInt': value outside int(0 to 100)
Stack trace:
  in panic-stack-trace.validate
  in panic-stack-trace.process
  in panic-stack-trace.caller
  in panic-stack-trace.main
  in _start
```

### Panic in main directly

<!-- test: panic-in-main -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bounded = int(10 to 20)

function getVal() returns Integer
  return Integer{5}
end

function main() returns ExitCode
  let _x = Bounded{getVal()}
  return 0 as ExitCode
end
```
```exitcode
1
```
```stderr
panic at panic-in-main.test:10: Range check failed for type 'Bounded': value outside int(10 to 20)
Stack trace:
  in panic-stack-trace.main
  in _start
```
