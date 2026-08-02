---
feature: terminating-statement-temporaries
status: experimental
keywords: [panic, temporaries, ownership, drain, terminator, interpolation, block]
category: semantics
---
# A Terminating Statement Drops Its Temporaries BEFORE Its Terminator

## Documentation

`parseStatement` drains the statement's pending temporaries after each statement, and
skips that drain once the block has been terminated. The justification recorded at the
skip was that a terminating statement drops its temporaries before its terminator, and
that `break`/`continue` carry no expression and so build none.

**`panic` is the case that argument never covered.** It terminates the block AND carries
an expression, and an interpolated message builds an owned `String`. Left in
`pendingTempDrops`, that temporary was released by the drain of the NEXT statement — a
block the interpolation's definitions do not dominate.

### Why this file exists rather than a case inside `panic.md`

`specs-shv2/panic.md` is a byte-identical port of `/specs/panic.md` and may not gain
cases. `/specs/panic-interpolation.md` is queued for a proper port and is deliberately
NOT copied in yet. Without this file the defect below has **no committed gate at all** —
and it is a defect that reached a released compiler on two targets in two different
disguises, so it is exactly the shape that must not be able to come back unobserved.

### ⚠ THE TWO TARGETS FAILED DIFFERENTLY, AND ONLY ONE OF THEM FAILED LOUDLY

Measured on `4ea20b9dd`, the commit before the fix:

- **arm64-macos REFUSED to compile it.** `panic at RegisterAllocator.maxon:1039:
  seedInUse: value 14 is live-in to block 0 but was never colored — a use dominates its
  def (structured-layout invariant broken)`. The allocator was RIGHT.
- **x64-windows COMPILED IT CLEAN and emitted a wrong program.** It spilled the String,
  so the block reached when the interpolation was never built contained
  `loadRegSlot rcx, slot1; callDirect __str_decref` — a decref of an uninitialized frame
  slot, as a `String` record, on the path the `if` did not take. Exit 0, no diagnostic,
  no leak report.

**The crash was the honest lane.** A gate that only ever ran on x64 would have called
this program correct. That is the argument for the second case below: it takes the branch
that does NOT panic, which is the path the silent miscompile corrupted, and it is worth a
case precisely because its exit code alone never distinguished the broken compiler from
the fixed one.

## Tests

### An Interpolated Panic Inside a Block

The reduced repro, and the case that crashed the compiler outright on arm64.

<!-- test: panic-interpolation-in-a-block -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **W3 REMOVED THE REASON THIS LINE EXISTED, AND THE LINE IS KEPT ONLY TO SAY SO.** When A3n wrote
this pin, wasm had no `mrt_panic` at all and a `stderr` expectation could not pass there. W3 built
one, and these cases were **measured green on wasm with their existing goldens, unedited** — so the
restriction is now a list of every target, which is the honest spelling of "no restriction". It is
written out rather than deleted because a `targets:` line that names everything is falsifiable: add
a target and this line is visibly wrong, where an absent line would silently mean "all".

```maxon
function main() returns ExitCode
	let x = 42
	if x > 0 'check'
		panic("value is {x}")
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at panic-interpolation-in-a-block.test:5: value is 42
Stack trace:
  in main
  in mrt_start
```

### The Path That Does NOT Panic

The silent half. Pre-fix, x64 released the interpolation's temporary here — a value this
path never built. The program must reach its own `return` and exit cleanly.

<!-- test: not-taken-path-releases-nothing -->
```maxon
typealias Integer = int(i64.min to i64.max)

function check(n Integer) returns Integer
	if n > 0 'hot'
		panic("value is {n}")
	end 'hot'
	return 7
end 'check'

function main() returns ExitCode
	let a = check(-1)
	let b = check(-2)
	return (a + b) - 14
end 'main'
```
```exitcode
0
```

### An Owned Temporary Still Reaches the Message

The interpolation holds a heap `String` the panic path itself built, so the drain must
happen late enough to render it and early enough not to outlive its block.

<!-- test: owned-temporary-in-the-message -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **W3 REMOVED THE REASON THIS LINE EXISTED, AND THE LINE IS KEPT ONLY TO SAY SO.** When A3n wrote
this pin, wasm had no `mrt_panic` at all and a `stderr` expectation could not pass there. W3 built
one, and these cases were **measured green on wasm with their existing goldens, unedited** — so the
restriction is now a list of every target, which is the honest spelling of "no restriction". It is
written out rather than deleted because a `targets:` line that names everything is falsifiable: add
a target and this line is visibly wrong, where an absent line would silently mean "all".

```maxon
typealias Integer = int(i64.min to i64.max)

function name(n Integer) returns String
	return "item{n}"
end 'name'

function main() returns ExitCode
	let n = 3
	if n > 0 'check'
		panic("missing {name(n)}")
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at owned-temporary-in-the-message.test:11: missing item3
Stack trace:
  in main
  in mrt_start
```
