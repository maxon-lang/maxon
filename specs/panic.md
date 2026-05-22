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

The argument can be a string literal or an interpolated string (see `panic-interpolation` spec). The program prints a panic message to stderr including the source file and line number, followed by a stack trace, then exits with code 1.

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
  in mrt_start
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
  in main
  in mrt_start
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
  in fail
  in main
  in mrt_start
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
		return check(-1)
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.after-condition.test:6: negative value
Stack trace:
  in check
  in main
  in mrt_start
```

<!-- test: panic.two-distinct-messages -->
<!--
Two user panics with different messages. Each must land in its own label
slot so whichever one fires prints the correct message. Canary for the
panic-label-collision bug: if both panics shared a label, the
second-registered data would be unreachable (or clobber the first) and
runtime would print the wrong message.
-->
```maxon
typealias Integer = int(i64.min to i64.max)

function runA(n Integer) returns Integer
	if n < 0 'a'
		panic("message A")
	end 'a'
	return n
end 'runA'

function runB(n Integer) returns Integer
	if n < 0 'b'
		panic("message B")
	end 'b'
	return n
end 'runB'

function main() returns ExitCode
	let a = runA(1)  // does not panic
	let b = runB(-1) // should print "message B"
	return a + b
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.two-distinct-messages.test:13: message B
Stack trace:
  in runB
  in main
  in mrt_start
```

