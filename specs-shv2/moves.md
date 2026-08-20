---
feature: moves
status: experimental
keywords: [ownership, move, use-after-move, single-owner, drop, reassign, string, struct]
category: memory
---

# Static Single-Owner Moves

## Documentation

Under shv2's static single-owner model an owned heap value (an owned `String`, a struct box) has
exactly ONE owner **per NAME that holds it**: a binding owns one reference and releases it once, at
its scope exit. A value reaches a second owner only by a DURABLE STORE, which takes a reference of its
own (⚖ 2026-08-12) — so the record's refcount always equals the number of live owners, and each owner
releases exactly the reference it took. What a binding-to-binding bind may never do is CONFER a second
owner without a second reference, which is what the move rule below exists to prevent.

**What decides whether a bare-reference bind MOVES that ownership or merely ALIASES it is the
SOURCE's MUTABILITY** (`specs/ownership.md`; user ruling, 2026-08-04). Maxon is single-ownership and
everything is a reference — `clone()` is the only copy — so two names for one value are safe exactly
when neither can be written through:

```maxon
let t = build(1)   // t owns the box
let u = t          // t is IMMUTABLE, so u is a SECOND REFERENCE; both stay valid
print(u)
print(t)           // fine — the box drops ONCE, at t's scope exit
```

```maxon
var t = build(1)   // t owns the box
let u = t          // t is MUTABLE, so ownership MOVES to u; t is now moved-from
print(u)           // u still usable
// print(t)        // ERROR: use of moved value 't'
```

A `var` source moves because a second name for its value could watch it change: `t = <other>` would
drop the very box `u` still names. An immutable source cannot be rebound, so the alias costs nothing
and needs no refcount — the ONE drop stays with the owner, which structurally outlives every alias of
it (an alias can only be declared later, in the owner's scope or one nested inside it).

The mirror shape — a MUTABLE binding made from an immutable name (`var u = t`) — is refused outright
as **E3078**, because it would reach the immutable name's storage through a writable one. See
`specs/var-should-be-let.md`.

When a move does happen the source is left MOVED-FROM: reading it is a compile error
(use-after-move), and its scope-exit drop is SKIPPED — the value drops once, through its new owner. A
fresh owned temporary (`build()`, `"{x}"`) is owned by no binding, so binding it is a CONSUME, not a
move — nothing is poisoned.

⭐ **A DURABLE STORE IS NOT A MOVE, AND THAT IS THE BOUNDARY OF EVERYTHING ABOVE (⚖ user ruling,
2026-08-12).** A CONSUMING hand-off — a call argument the callee keeps, a struct-literal or union-payload
field, a container element, a write into a field, a global's slot, or a by-reference parameter's cell —
hands the value to storage that owes a drop of its OWN. So the sink takes its own reference and the
source binding stays LIVE, releasing the reference it always held at its own scope exit. Both sinks
co-own; the refcount rises by one per sink and falls by one per owner. No name is poisoned, and an ALIAS
of the source stays readable too.

> This prose used to say the opposite — that a consuming hand-off *"moves the value out of whichever
> binding owns it regardless of mutability, and poisons every live name that reads it"*. The
> justification was that shv2 is move-only and has no `__mm_incref`. It does have one, and the rule was
> retracted: a value stored into a long-lived field AND into a transient local array needs a reference
> for each, and no rewriting of the source can supply that under a move
> (`specs-shv2/witness-managed-return.md` is the program that forced it).

⚠ **A second NAME is still not a sink.** `let u = t` / `s = t` over an ordinary local gives `u` no drop
of its own — it inherits the one `t` owed — so it MOVES, and every use-after-move case below is
unchanged. The one rebind that IS a durable store is a write to a by-reference parameter, whose cell is
the CALLER's storage and outlives this frame.

A WRITE to a moved-from `var` REVIVES it: the binding owns the new value and is usable again. A value
moved on some-but-not-all paths of an `if`/`else`/`match` is DROPPED path-sensitively — its drop is
placed on the paths that did not move it, reconciled at the control-flow join (see
`conditional-move-drops.md`) — but READING it past the join stays a CONSERVATIVE use-after-move: being
maybe-moved, it may not be read, even though it is correctly dropped where it was not moved.

## Tests

### Legal Move From a Var, Source Unused

`t` (a `var`) is bound then moved into `u`; `t` is never read again. The value drops exactly once —
through `u` at scope exit, with `t`'s drop skipped — so no leak and no double-free.

<!-- test: legal-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = t
	print(u)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```

### An Immutable Rebind Is a Second Reference, Not a Move

`t` is a `let`, so `let u = t` ALIASES rather than moves: both names stay readable, and the box drops
exactly ONCE — at `t`'s scope exit, with `u` owning nothing. Two drops would be a double-free the leak
gate reports as exit 101; zero would be a leak it reports the same way. No refcount is involved: the
alias is simply not enrolled as an owner.

<!-- test: immutable-rebind-aliases -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let t = build(1)
	let u = t
	print(u)
	print(t)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1v1
```

### A CONSUME Through Any Name Leaves Every Name Live

`let b = a` aliases; `Box.create(b)` then hands the value to a callee that stores it. A consuming
argument is a DURABLE SINK, so the callee takes its OWN reference (⚖ 2026-08-12) instead of stealing this
frame's — the frame still owns the box through both names, and the later `print(a)` reads a live record.
The box carries two references and is released twice: once by `held`'s destructor, once by `a`'s
scope-exit drop.

⚠ **This is the ONE boundary of the move rule the rest of this file pins.** A move happens between
BINDINGS (`let u = t`, `s = t`), where the new name owes the drop the old one owed; it does not happen
into a durable sink, where the sink owes a drop of its OWN. Every use-after-move case below is a
binding-to-binding move and is unaffected by that distinction; the two consuming-hand-off cases that are
NOT (this one and `call-arg-consumed-at-two-positions`) both show the co-owning side. (This case used to
be E3102, on the premise that shv2 has no `__mm_incref`.)

<!-- test: consume-through-an-alias-keeps-both-live -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

type Box
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = build(1)
	let b = a
	let held = Box.create(b)
	print(held.text)
	print(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1v1
```

### Use After Move-On-Bind

`t` is a `var`, so `let u = t` MOVES it; the following `print(t)` reads the moved-from binding and is
rejected at the use. (With `let t` the same two lines are an ALIAS and both names stay readable — see
`immutable-rebind-aliases` below.)

<!-- test: use-after-move-on-bind -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = t
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Owned Var Assigned From Owned Var (#41)

`s = t` overwrites `s`'s box (dropped at the assignment) and MOVES `t`'s box into `s`. Before Wave C
both bindings ended up owning `t`'s box and it was decref'd twice at scope exit — a double-free the
leak gate reported as exit 101. Now `t` is moved-from and skipped, so each box drops exactly once.

<!-- test: assign-owned-from-owned -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	var t = build(2)
	s = t
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v2
```

### Use After Move-On-Assign

`s = t` moves `t`; the following `print(t)` reads the moved-from binding and is rejected at the use.

<!-- test: use-after-move-on-assign -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var s = build(1)
	var t = build(2)
	s = t
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Reassign Revives a Moved-From Var

`a` is moved into `b`, then `a` is reassigned a fresh value. The write REVIVES `a` — it owns the new
box and is usable again — while `b` owns the box moved out of `a`. Both print, and the old box is NOT
double-dropped by the reassignment (it belongs to `b` now, not `a`).

<!-- test: reassign-revives -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var a = build(10)
	let b = a
	a = build(20)
	print(b)
	print(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v10v20
```

### Second Alias Of a Moved Value Is Use-After-Move

`a` is a `var`, so `let b = a` MOVES it; `let c = a` then reads the moved-from `a` and is rejected at
the use. A second alias of a value that is still OWNED is legal — that is `immutable-rebind-aliases`.

<!-- test: multiple-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var a = build(1)
	let b = a
	let c = a
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### Conditional Move Poisons Conservatively

`a` is moved inside the `if` body. `movedFrom` is a flag set unconditionally where the move is
written, so `a` is treated as moved on EVERY path past the merge — reading it after the `if` is a
use-after-move even though the non-taken branch never moved it. This over-rejection is the intended
sound minimum for this rung (a moved-on-all-paths dataflow join is a later rung).

<!-- test: conditional-poisoning -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var a = build(1)
	let flag = 1
	if flag > 0 'b'
		let u = a
		print(u)
	end 'b'
	print(a)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:8: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### Struct Move Drops Once (No Double-Free)

A struct box is owned by its type. `let q = p` moves `p`'s box into `q`; `p` is left moved-from and
its drop is skipped, so the box drops exactly once through `q`. Without the move both would decref it
and the leak gate would fire (exit 101).

<!-- test: struct-move-drop-skip -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(7)
	let q = p
	return q.x
end 'main'
```
```exitcode
7
```
```stdout
```

### Move Through Redundant Parentheses Drops Once (No Double-Free)

`let u = (t)` — the initializer is a bare local reference wrapped in redundant parentheses.
`parseParenthesizedExpression` returns its inner value UNCHANGED, so `(t)` aliases `t`'s owned box
exactly as a bare `t` would: it MOVES. A move gate that decided "bare local" by counting tokens saw
three tokens (`( t )`) and called this a consume, left `t` unpoisoned, and both bindings decref'd the
one box at scope exit — a double-free (exit 101). The gate now strips redundant parentheses, so `t` is
moved-from and skipped and the box drops exactly once.

<!-- test: paren-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = (t)
	print(u)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```

### Use After Move Through Parentheses

`let u = (t)` moves the `var` `t` through redundant parentheses; the following `print(t)` reads the
moved-from binding and is rejected at the use. The parens do not exempt the source from poisoning — the
gate sees through them to the bare local reference underneath, and reads its mutability there.

<!-- test: paren-use-after-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let u = (t)
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

### Field Read After Move Is Use-After-Move

`let q = p` moves `p`'s box into `q`; `return p.x` then READS a field out of the moved-from `p`. The
use-after-move guard fires at every binding-use site, not only the bare read — reading a field through
a moved-from base is rejected at the base, before the field load is emitted. (Without the guard this
returned the moved struct's field: a latent use-after-free once the new owner drops first.)

<!-- test: field-read-after-move -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(7)
	let q = p
	return p.x
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Field Store After Move Is Use-After-Move (Not a Revive)

`let q = p` moves `p`'s box into `q`; `p.x = 99` then WRITES a field through the moved-from `p`. A
field store on a moved-from binding is a USE, not a revive: `p.x = …` mutates the box `p` no longer
owns (the one `q` holds), so it is rejected at the base. Only a FULL reassignment `p = <expr>` revives
`p`. Without the guard this silently mutated `q`'s aliased box and `return q.x` returned **99** — an
observable wrong answer for a program shv2 must reject.

<!-- test: field-store-after-move -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(7)
	let q = p
	p.x = 99
	return q.x
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:2: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Method Call After Move Is Use-After-Move

`let q = p` moves `p`'s box into `q`; `p.getX()` then calls an instance method with the moved-from `p`
as receiver. The receiver is a use of `p`, so it is rejected at the base before the call is emitted.

<!-- test: method-call-after-move -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'

	function getX() returns int
		return x
	end 'getX'
end 'Point'

function main() returns ExitCode
	var p = Point.create(5)
	let q = p
	return p.getX()
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

### Full Reassignment Revives, Then a Field Store Is Legal

`let q = p` moves `p`'s box into `q`; `p = Point.create(3)` is a FULL reassignment that REVIVES `p` —
it owns a fresh box now — so the following `p.x = 5` writes that new box legally, and `return p.x`
reads it back as 5. `q` owns the box moved out of `p`, `p` owns the box from `create(3)`; each drops
exactly once (no leak). This pins that a field store reads the CURRENT moved-from state (post-revive),
not a stale one.

<!-- test: reassign-revives-then-field-store -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1)
	let q = p
	print("{q.x}")
	p = Point.create(3)
	p.x = 5
	return p.x
end 'main'
```
```exitcode
5
```
```stdout
1

```

### Field Access and Method Call on a Live Struct (Positive Control)

`p` is never moved, so both the field store `p.x = 0` and the method call `p.getX()` are legal — the
use-after-move guard fires ONLY when the base binding is moved-from. Reads back 0.

<!-- test: field-access-on-live-struct -->
```maxon
type Point
	export var x as int

	static function create(x int) returns Point
		return Self{x: x}
	end 'create'

	function getX() returns int
		return x
	end 'getX'
end 'Point'

function main() returns ExitCode
	var p = Point.create(4)
	p.x = 0
	return p.getX()
end 'main'
```
```exitcode
0
```
```stdout
```

### One Owned Value at TWO Consuming Argument Positions

`Pair.create(box, b: box)` hands the SAME owned `box` to TWO CONSUMING factory parameters. Each
consuming position is a durable sink and takes its OWN reference (⚖ 2026-08-12), so the box ends at
three — `a`, `b` and the caller's `box` — and is released three times: twice by `Pair`'s destructor
cascade, once by `box`'s scope-exit drop. Both fields print the same name, which is the observable half
of "one record, two owners".

⛔ **This was E3102 until the durable-sink ruling, and the reason it was is now false.** The guard's
justification was that shv2 is move-only and a single transferred `+1` cannot answer for two owners —
true of a move, and not of a reference taken per position. It is the CALL analog of the struct-literal
double-owning-store case (`struct-managed-field/managed-double-store-co-owns`), which was retracted in
the same change and for the same reason; the repeat-detection core survives for the one sink that still
MOVES, an opaque `T` slot in a shared generic body. The reference oracle refcounts and has always
accepted this program.

<!-- test: call-arg-consumed-at-two-positions -->
```maxon
type Box
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Box'

type Pair
	export var a as Box
	export var b as Box

	static function create(a Box, b Box) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let box = Box.create("a long string that needs real heap allocation for the box")
	let p = Pair.create(box, b: box)
	print("{p.a.name}\n")
	print("{p.b.name}\n")
	print("{box.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a long string that needs real heap allocation for the box
a long string that needs real heap allocation for the box
a long string that needs real heap allocation for the box
```

### Same Owned Value at Two BORROW Parameters (Positive Control)

The double-consume guard fires ONLY when BOTH argument positions CONSUME. `firstOf(a, b)` only READS its
parameters (they borrow), so passing the same owned `box` to both is legal — nothing is moved, and `box`
drops exactly once at scope exit. Prints the label twice and returns 0.

<!-- test: same-owned-at-two-borrow-params -->
```maxon
type Box
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function label() returns String
		return self.name
	end 'label'
end 'Box'

function firstOf(a Box, b Box) returns String
	print("{b.label()}\n")
	return a.label()
end 'firstOf'

function main() returns ExitCode
	let box = Box.create("owned managed box string long enough to require heap now")
	let s = firstOf(box, b: box)
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
owned managed box string long enough to require heap now
owned managed box string long enough to require heap now
```

### A Mutable Binding May Not Be Made From an Immutable Name

The mirror of the alias above. `let t = …` then `var u = t` would reach `t`'s storage through a
writable name, so it is refused at the bind rather than tolerated — the remedies the message names are
the two that keep single ownership intact: leave `u` immutable, or take an independent value with
`clone()`. (A third fix lives at the DECLARATION: make `t` a `var` in the first place.)

⚠ A **value**-typed source is unaffected — `int`/`float`/`bool`/`byte` copy, so there is no shared
storage to reach. Only reference types (struct, union, function) are refused, and a PARAMETER is
exempt: its storage is already the caller's copy, which is the line `E3019` draws too.

<!-- test: error.var-from-immutable-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let t = Box.create(5)
	var u = t
	u.value = 9
	return u.value
end 'main'
```
```maxoncstderr
error E3078: <fragment>:14:6: cannot assign immutable variable 't' to mutable binding 'u'; use 'let' instead of 'var', or use clone()
```

### The Refusal Follows the Chain, Parentheses and Tuple Positions Included

`var u = t` and `var u = t.field` are one rule — *"whose storage would this writable name reach?"* — so the
spellings that reach the same storage get the same answer. Redundant parentheses change nothing about what
`(t).field` names, and a tuple's positional member `t.0` is its field `_0` under another spelling.

<!-- test: error.var-from-immutable-through-parens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(5))
	var i = (o).inner
	i.value = 9
	return o.inner.value
end 'main'
```
```maxoncstderr
error E3078: <fragment>:22:6: cannot assign from immutable variable to mutable binding 'i'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.var-from-immutable-tuple-element -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let t = (Box.create(5), 7)
	var b = t.0
	b.value = 9
	return b.value
end 'main'
```
```maxoncstderr
error E3078: <fragment>:14:6: cannot assign from immutable variable to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

### A Destructuring Is Its Element Bindings

`var (a, b) = t` means `var a = t.0` and `var b = t.1`, so the rule above is asked of each ELEMENT and the
sentence blames the name the author wrote. Scalars copy, so a tuple of numbers destructures out of an
immutable source freely — there is no shared storage for the writable names to reach.

<!-- test: var-destructure-scalars-from-immutable -->
```maxon
function main() returns ExitCode
	let t = (10, 20)
	var (a, b) = t
	a = a + b
	b = b + 1
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30 21
```

<!-- test: error.var-destructure-managed-element-from-immutable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let t = (Box.create(5), 7)
	var (b, n) = t
	b.value = 9
	return b.value + n
end 'main'
```
```maxoncstderr
error E3078: <fragment>:14:7: cannot assign from immutable variable to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```
