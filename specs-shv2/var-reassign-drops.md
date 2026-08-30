---
feature: var-reassign-drops
status: experimental
keywords: [ownership, drop, reassign, var, loop, struct, leak]
category: memory
---

# Drop-on-Reassign

## Documentation

Reassigning a `var` that owns a heap value (an owned `String`, a struct box) releases the
value being OVERWRITTEN before the binding takes the new one. Without that release every
value but the LAST leaks: a binding's scope-exit drop only ever reaches its final value.

```maxon
var s = build(1)   // owns the box from build(1)
s = build(2)       // build(1) is dropped HERE, before s takes build(2)
s = build(3)       // build(2) is dropped HERE
print(s)           // s == build(3), dropped at scope exit
```

Ownership is BINDING-level: whether a reassignment must drop is decided by whether the
binding owns a value, not by a bit on the value being overwritten. Inside a loop the
overwritten value is a header phi — which carries no owned-heap provenance — so the binding
is the only record that survives, and a design that read the value's bit would leak every
loop-carried value.

The transition also retracks the binding for its NEW value: an owned-to-borrowed reassign
(`s = "literal"`) takes the binding OUT of the drop set (so scope exit does not decref
read-only rdata), and a borrowed-to-owned one (`var s = ""; s = build(1)`) puts it IN.

## Tests

### Straight-Line Reassign Frees the Intermediates

`build(1)` and `build(2)` are each dropped at the reassignment that overwrites them; only
`build(3)` reaches scope exit. The exit code balances either way (the bump allocator reclaims
nothing), but the golden pins the three decrefs that make it a clean single-free of each.

<!-- test: straight-line -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	s = build(2)
	s = build(3)
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v3
```

### Loop-Carried Reassign Frees Each Iteration

The var is declared before the loop and reassigned each time round. The overwritten value is
the loop header phi: on the first iteration it is `build(99)` (the entry value), afterwards
the previous iteration's `build(i)`. Each is dropped exactly once at the reassignment, and the
final value once at scope exit — no leak, no double-free of the exit value.

<!-- test: loop-carried -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(99)
	var i = 0
	while i < 3 'loop'
		s = build(i)
		print(s)
		i = i + 1
	end 'loop'
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v0v1v2v2
```

### Owned Reassigned to a Borrowed Literal

`s` owns `build(1)`, then is reassigned a string literal. The old owned value is dropped, and
the binding LEAVES the owned set — so scope exit does not decref the borrowed rdata literal
(which would fault on read-only memory).

<!-- test: owned-to-borrowed -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	s = "lit"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lit
```

### Outer Owned Var Reassigned to a Borrowed Literal Inside an If

The owned `s` lives at function scope while a fresh owned `t` is bound inside the `if` body, then
`s` is reassigned a literal from within that body. `s` stays owned (the literal is promoted to an
owned copy), so it is never deleted from the middle of the owned-binding stack — which would strand
`t`'s block-local drop and reference a value on a path where it is undefined.

<!-- test: owned-to-borrowed-in-if -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	let flag = 1
	if flag > 0 'b'
		let t = build(2)
		print(t)
		s = "lit"
	end 'b'
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v2lit
```

### Outer Owned Var Reassigned to a Borrowed Literal Inside a Loop

The same, per iteration: `t` is a block-local owned binding dropped at each loop-body `end`, while
the outer `s` is reassigned a literal inside the body. `s` stays owned across the loop; nothing is
removed from the owned stack, so the loop body's drop floor stays valid.

<!-- test: owned-to-borrowed-in-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	var i = 0
	while i < 3 'loop'
		let t = build(i)
		print(t)
		s = "lit"
		i = i + 1
	end 'loop'
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v0v1v2lit
```

### Owned, Owned, Then Borrowed

Two owned reassignments followed by a borrowed one: `build(1)` and `build(2)` are each
dropped at their overwrite, and the final literal takes the binding out of the owned set.

<!-- test: owned-owned-borrowed -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	s = build(2)
	s = "lit"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lit
```

### Borrowed Reassigned to an Owned Value

`s` starts as a borrowed empty literal, then is reassigned an owned `build(1)`. The binding
JOINS the owned set, and the owned value is not also statement-dropped — so it survives to
`print` and is freed exactly once at scope exit.

<!-- test: borrowed-to-owned -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = ""
	s = build(1)
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```

### Struct Reassign Frees the Old Box

A struct binding owns its box via its type, not a provenance bit. Reassigning it drops the
overwritten box; without the drop the first box leaks (the leak counter catches it directly,
because a struct box is never enrolled as a statement temporary to be drained).

<!-- test: struct-reassign -->
```maxon
type Point
	var x as Integer

	static function create(x Integer) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1)
	p = Point.create(2)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
```

### Rebinding a Value to Itself Drops Nothing — but It Still MOVES

Reassigning a binding to the value it already holds emits no refcount traffic: the value and its drop
are unchanged, so nothing is decref'd and nothing is promoted. The `oldValue == value` guard is what
prevents a drop-then-keep (which would decref a value still bound).

⚠ The textual spelling `s = s` **cannot** be used to reach that guard, because it is a compile error —
`E3067`, see `self-assignment.md`. This case aliases through a second binding instead, which is the
better pin regardless: the guard compares **value identity** (`ValueId`), not spelling, so `let t = s`
followed by `s = t` reaches it by the property it actually tests and would keep reaching it if the
E3067 rule ever became semantic.

⭐ **THE SURVIVING READ IS `s`, THE ASSIGNMENT TARGET — "no-op" IS ABOUT THE REFCOUNTS, NEVER ABOUT THE
OWNERSHIP.** `let t = s` moves the ownership to `t`; `s = t` hands it straight back, so afterwards `s`
is live again and `t` is moved-from (E3102 on a later `t`). A move does not mint a value — it re-homes
one — so a hand-back reaches the identity guard while meaning the exact opposite of a no-op, and the
guard is therefore gated on `not binding.movedFrom`. This is the same rule the door already applied to
every OTHER bare-local source — measured: `var s = build(1); let t = build(2); s = t; print(t)` has
always been E3102 — so the source surviving HERE was never a rule, only the elision showing through.
`/specs/optimizer-refcount.md` pins the same shape one type over (`var b = a; a = b; a.x`) and reads
the target.

<!-- test: self-assign -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	let t = s
	s = t
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```
