---
feature: Panic Stack Trace
status: implemented
category: runtime
---

## Notes

When a runtime panic occurs (e.g., ranged type check failure), the program prints a stack trace to stderr showing the call chain from the panicking function back to `mrt_start`. This helps developers identify where the error occurred.

The stack trace is printed after the panic message and walks the frame-pointer chain to resolve function names from an embedded symbol table. It prints at most 100 frames; a deeper chain ends with an `...additional frames elided...` line.

## Tests

### Simple panic with stack trace

<!-- test: simple-panic -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function dangerous(value Integer) returns Byte
	return value as Byte
end

function main() returns ExitCode
	return dangerous(300)
end
```
```exitcode
1
```
```stderr
panic at simple-panic.test:6: Range check failed: value outside typealias 'Byte'
Stack trace:
  in dangerous
  in main
  in mrt_start
```

### Nested call chain

<!-- test: nested-calls -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SmallInt = int(0 to 100)

function validate(n Integer) returns SmallInt
	return n as SmallInt
end

function process(n Integer) returns SmallInt
	return validate(n)
end

function caller(n Integer) returns SmallInt
	return process(n)
end

function main() returns ExitCode
	return caller(999)
end
```
```exitcode
1
```
```stderr
panic at nested-calls.test:6: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in validate
  in process
  in caller
  in main
  in mrt_start
```

### Panic in main directly

<!-- test: panic-in-main -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bounded = int(10 to 20)

function getVal() returns Integer
	return 5
end

function main() returns ExitCode
	let b = getVal() as Bounded
	return b
end
```
```exitcode
1
```
```stderr
panic at panic-in-main.test:10: Range check failed: value outside typealias 'Bounded'
Stack trace:
  in main
  in mrt_start
```

### A caller that passes an argument on the stack

A call with nine integer arguments puts one of them on the stack, and on arm64 the caller's frame record sits
above that one-word outgoing region — so its frame pointer is word-aligned but not 16-byte aligned. The walk
still follows it to `main`.

<!-- test: caller-with-a-stack-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SmallInt = int(0 to 100)

function total(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer) returns SmallInt
	return (a + b + c + d + e + f + g + h + i) as SmallInt
end 'total'

function spread(n Integer) returns SmallInt
	return total(n, b: n, c: n, d: n, e: n, f: n, g: n, h: n, i: n)
end 'spread'

function main() returns ExitCode
	return spread(20)
end 'main'
```
```exitcode
1
```
```stderr
panic at caller-with-a-stack-argument.test:6: Range check failed: value outside typealias 'SmallInt'
Stack trace:
  in total
  in spread
  in main
  in mrt_start
```
