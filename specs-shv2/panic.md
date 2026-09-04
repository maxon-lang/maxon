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
		if true 'check'
				panic("something went wrong")
		end 'check'
		return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.basic.test:4: something went wrong
Stack trace:
  in main
  in mrt_start
```

<!-- test: panic.message-decodes-escapes -->
### A panic message is read like every other string literal
MEASURED 2026-08-26: `panic`'s two arms disagreed with each other. An INTERPOLATED message was
decoded, a plain literal one kept the raw token slice — so `panic("a{x}b\n")` emitted a newline
while `panic("a\nb")` emitted a backslash and an `n`. shv2 emitted the newline for both, so the two
compilers printed different bytes for the same program. Both spellings of `panic` — the statement
and the match arm — now read their message through the one door.
```maxon
function main() returns ExitCode
	panic("first\nsecond")
end 'main'
```
```exitcode
1
```
```stderr
panic at panic.message-decodes-escapes.test:3: first
second
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

<!-- test: panic.specialized-panic-keeps-its-own-message -->
A panic's label is decided where its message is written down, and every copy keeps it. The string
operations put a long run of CONCRETE stdlib panics into the program ahead of the guard that fires —
the grapheme, hash, Unicode-category, UTF-8 and UTF-16 helpers all panic on invariants they never
break — so a label re-derived rather than carried would print one of THEIRS instead of the guard that
actually fired. That is what this case pins, and the ballast is what makes it discriminating. The `-2`
is laundered through a function because an `ElementIndex` starts at 0: a literal `-2` is refused at
compile time and the program would never run.

⚠ **THE FRAME IS `main`, AND THAT BOUNDS WHAT THIS CASE CAN CLAIM.** A range check happens where the
value CROSSES into the alias — at the `as ElementIndex` door in the caller — not inside the callee that
receives it, so no stdlib frame appears and the guard is not a specialization's. `arrays.md`'s
`insert-laundered-negative-index-panics-at-the-door` pins the same shape and the same two frames.
⇒ **This case does NOT pin a panic label inside a monomorphized specialization**, and none can by this
route: every remaining `panic(` in `stdlib/Array.maxon` is an unreachable internal invariant. A case
that wants a stdlib callee frame needs a reachable PRECONDITION instead — `String.sliceBytes`'s is one
(`string-index.md`).
```maxon
typealias Signed = int(i64.min to i64.max)

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	let s = "  héllo wörld  "
	var n = s.trim(CharacterSet.letters()).count() as Signed
	n = n + (s.bytes().count() as Signed)
	for cp in "héllo".codepoints() 'c'
		n = n + (cp as Signed)
	end 'c'
	for u in "h€llo".utf16() 'u'
		n = n + (u as Signed)
	end 'u'
	let m = ["a": 1, "b": 2]
	n = n + (m.count() as Signed)
	print("{n}\n")
	var a = [10, 20, 30]
	a.insert(launder(-2) as ElementIndex, value: 99)
	return 0
end 'main'
```
```exitcode
1
```
```stdout
9493
```
```stderr
panic at panic.specialized-panic-keeps-its-own-message.test:22: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: panic.specialized-panic-keeps-its-own-message-twin -->
The same program again, deliberately. The runner compiles on a pool of worker threads, and a label
derived from per-thread state answers differently on a thread that parsed the stdlib and one that did
not — so a single copy could be handed the one thread that happens to agree with itself. Two
independent work items cannot both land there.
```maxon
typealias Signed = int(i64.min to i64.max)

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	let s = "  héllo wörld  "
	var n = s.trim(CharacterSet.letters()).count() as Signed
	n = n + (s.bytes().count() as Signed)
	for cp in "héllo".codepoints() 'c'
		n = n + (cp as Signed)
	end 'c'
	for u in "h€llo".utf16() 'u'
		n = n + (u as Signed)
	end 'u'
	let m = ["a": 1, "b": 2]
	n = n + (m.count() as Signed)
	print("{n}\n")
	var a = [10, 20, 30]
	a.insert(launder(-2) as ElementIndex, value: 99)
	return 0
end 'main'
```
```exitcode
1
```
```stdout
9493
```
```stderr
panic at panic.specialized-panic-keeps-its-own-message-twin.test:22: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```
