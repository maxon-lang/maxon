---
feature: cross-file-signatures
status: stable
keywords: [cross-file, signatures, types, bool, int, short-circuit, undefined-function]
category: diagnostics
---

# Cross-File Callee Signatures

## Documentation

A call's result type is decided by the **parser**, and it must be exact. Two things depend on it
that no later pass can retrofit:

- `and` / `or` over `bool` operands is **short-circuit control flow**, not an operator — it lowers
  to blocks and a merge phi. Whether the right operand is evaluated *at all* follows from the left
  operand's type.
- `not` picks a **different opcode** per operand type: a bit-flip on a `bool`, a 64-bit complement
  on an `int`.

So the parser reads every function declared **anywhere in the program**, from tokens, before any
file is parsed. A callee's return type is therefore exact whether it is declared above the call,
below it, or in another file. Declaration order — and file boundaries — do not decide what a
program means.

### What this replaces, and why it was a wrong answer

A callee the parser could not see used to be typed `unresolved`, which agrees with **everything**.
That is the right move for a type you genuinely cannot know, and a catastrophic one for a type you
simply did not look for:

```text
// --- file: a.maxon
export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	let x = isReady()
	return x + 41       // compiled, and returned 42 — the bool's 1 payload, added
end 'main'
```

The same program in **one** file was already rejected. The bug was not the deferral; it was that
nothing had looked.

Its twin is worse, because deferral does not merely fail to reject — it **mints a false tag**. A
word operator whose operands *agree only because one of them deferred* takes the bool reading, so
`flag and crossFileInt()` produced a merge phi tagged `bool` that carried the integer `7`, and
`if m` then branched on `7`.

### A callee no file declares

`unresolved` is still reachable, for exactly one thing: a call to a function that does not exist.
That program is rejected — `E3030: call to undefined function` — so the deferral has nothing left
to lie to.

## Tests

<!-- test: cross-file-bool-plus-int -->
A `bool` returned from another file cannot be added to an `int`. This is the headline case: it
compiled, and returned 42.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	let x = isReady()
	return x + 41
end 'main'
```
```maxoncstderr
error E2004: <fragment>:12:11: Cannot operate on bool and int
```

<!-- test: cross-file-word-operator-mixed-operands -->
The false-tag twin. `flag and crossFileInt()` used to mint a merge phi tagged `bool` carrying the
int `7`; `if m` branched on `7` and the program returned 1.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function seven() returns Integer
	return 7
end 'seven'

// --- file: b.maxon
function main() returns ExitCode
	let flag = true
	let m = flag and seven()
	if m 'branch'
		return 1
	end 'branch'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:15: operator 'and' requires both operands to be the same type (both bool or both int)
```

<!-- test: cross-file-comparison-mixed-operands -->
A comparison is class-strict across files too: the only thing there is to compare is the bool's 0/1
payload, which is not what the source wrote.
```maxon
// --- file: a.maxon
export function isReady() returns bool
	return true
end 'isReady'

// --- file: b.maxon
function main() returns ExitCode
	if isReady() < 4 'compare'
		return 1
	end 'compare'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:15: type mismatch: 'cannot compare bool with int'
```

<!-- test: cross-file-int-call-still-compiles -->
⚠ THE OVER-REJECTION GUARD. Refusing an operand whose type the parser could not pin would reject
this — a correct program — and over-rejection is the worse failure. The fix was to LOOK, not to
refuse.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function crossFileInt() returns Integer
	return 41
end 'crossFileInt'

// --- file: b.maxon
function main() returns ExitCode
	return crossFileInt() + 1
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-bool-short-circuits -->
⚠ THE SECOND OVER-REJECTION GUARD, and the one that rules out the tempting one-liner. Making the
word operator DEFER on an unpinned operand would type `ready() and enabled()` as unknown, and `not`
refuses an unknown operand — so this correct program would stop compiling. It compiles, and the
`and` genuinely short-circuits: `enabled()` divides by zero and is never reached, because `ready()`
is false. A clean exit IS the proof.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export function ready() returns bool
	return false
end 'ready'

export function enabled() returns bool
	var zero = 0
	return 1 / zero == 0
end 'enabled'

// --- file: b.maxon
function main() returns ExitCode
	if not (ready() and enabled()) 'skipped'
		return 42
	end 'skipped'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-declaration-order -->
A callee declared in a file that sorts AFTER the caller is typed exactly the same. Order does not
decide meaning — here the `and` short-circuits over two cross-file bools regardless.
```maxon
// --- file: a-caller.maxon
function main() returns ExitCode
	if alwaysTrue() and alwaysTrue() 'both'
		return 42
	end 'both'
	return 1
end 'main'

// --- file: z-callee.maxon
export function alwaysTrue() returns bool
	return true
end 'alwaysTrue'
```
```exitcode
42
```

<!-- test: undefined-function-rejected -->
The one thing `unresolved` still means: a callee no file declares. The program is rejected, so the
deferral can never reach codegen.
```maxon
function main() returns ExitCode
	return bogus() + 1
end 'main'
```
```maxoncstderr
error E3030: <fragment>:3:9: call to undefined function 'bogus'
```
