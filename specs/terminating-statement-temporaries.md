---
feature: terminating-statement-temporaries
status: experimental
keywords: [panic, temporaries, ownership, drain, terminator, interpolation, block]
category: semantics
---
# A Terminating Statement Drops Its Temporaries BEFORE Its Terminator

## Documentation

`parseStatement` drains the statement's pending temporaries after each statement, and
skips that drain once the block has been terminated. The skip is sound only because a
terminating statement drops its temporaries before its terminator, and
`break`/`continue` carry no expression and so build none.

**`panic` is the terminator that carries an expression.** An interpolated message builds an
owned `String`, so `parsePanicStatement` drains it ahead of the abort op. Left in
`pendingTempDrops`, that temporary would be released by the drain of the NEXT statement — a
block the interpolation's definitions do not dominate.

### Why this file exists beside `panic.md` and `panic-interpolation.md`

`specs/panic.md` and `specs/panic-interpolation.md` pin what a panic PRINTS, and every
interpolated program there takes the panicking branch. The subject here is where the
message's temporary is released: the path that does NOT panic, which only an exit code sees,
and a message holding a heap `String` the panic path built. The defect has two different
disguises on two targets, so it is exactly the shape that must not be able to come back unobserved.

### ⚠ WITHOUT THE DRAIN THE TWO TARGETS FAIL DIFFERENTLY, AND ONLY ONE OF THEM LOUDLY

- **arm64-macos REFUSES to compile it.** `panic at RegisterAllocator.maxon:
  seedInUse: value 14 is live-in to block 0 but was never colored — a use dominates its
  def (structured-layout invariant broken)`. The allocator is RIGHT.
- **x64-windows COMPILES IT CLEAN and emits a wrong program.** It spills the String,
  so the block reached when the interpolation was never built contains
  `loadRegSlot rcx, slot1; callDirect __str_decref` — a decref of an uninitialized frame
  slot, as a `String` record, on the path the `if` did not take. Exit 0, no diagnostic,
  no leak report.

**The crash is the honest lane.** A gate that only ever ran on x64 would call this
program correct. That is the argument for the second case below: it takes the branch
that does NOT panic, which is the path the silent miscompile corrupts, and it is worth a
case precisely because its exit code alone does not distinguish a broken compiler from a
correct one.

## Tests

### An Interpolated Panic Inside a Block

The reduced repro, and the case that crashes a defective compiler outright on arm64.

<!-- test: panic-interpolation-in-a-block -->
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

The silent half. Without the drain, x64 releases the interpolation's temporary here — a value this
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
