---
feature: union-managed-payload
status: experimental
keywords: [union, enum, payload, String, struct, ownership, move, drop, decref]
category: ownership
---

# Union Managed Payloads (P1.3 slice 2)

## Documentation

A `union`/`enum` case may carry a **managed** associated value — a `String` or a
`struct`. The union stays a heap box (`8 + maxArity*8`, i64 tag at offset 0,
payload slot `i` at `8 + i*8`); a managed payload slot holds an owned heap
pointer.

Ownership is static single-owner, exactly as for a `String` or a struct binding:

- **Construct gives the payload slot its OWN reference (⚖ user ruling,
  2026-08-12).** `U.case(s)` leaves the slot holding exactly one reference, and
  which act supplies it depends on what `s` is: a borrowed String literal is
  promoted to an owned heap copy; an owned TEMPORARY has no other owner, so the
  slot ADOPTS its `+1`; and a live owned BINDING is CO-OWNED — the slot increfs
  and `s` stays readable, releasing its own reference at scope exit. Either way
  the box owns a droppable payload and releases exactly what it took.

  > This bullet used to read *"Construct is a MOVE … no incref, no copy. The
  > source binding is moved-from (a later read is `E3102`)"*. The move-only
  > premise behind it was retracted: two sinks for one value each need a
  > reference of their own. `construct-co-owns-string-source` and
  > `construct-co-owns-struct-source` below are the two cases that were flipped.
  > **The MOVE-OUT rules below are a different question and are unchanged** — a
  > `match` binding still moves a payload OUT of a solely-owned box, because it
  > nulls the slot rather than adding an owner.
- **A match binding on a SOLELY-OWNED union is a MOVE-OUT.** `match u { case(x) then … }`
  loads the managed field into `x` (which becomes an owned binding, dropped at its
  own scope exit) and clears the box slot. After the match `u` is moved-from (a
  later read is `E3102`). A discard `_`, an unbound tag-only arm, and a
  payload-free / scalar arm bind nothing and leave the box owned — `u` is dropped
  at scope exit. **Sole ownership is the requirement, not ownership**: a
  freshly-constructed `let u = U.case(s)` qualifies; a box this frame merely holds
  *a* reference to does not — see the co-owned bullet below.
- **A match binding on a BORROWED union is a RETAIN.** Where the scrutinee is a
  parameter or a method receiver, the box's owner is the caller's local and
  survives the call, so ownership cannot be moved out of it. The bind emits
  `__mm_incref` on the loaded payload instead: the binding owns that *second*
  reference and drops it at the arm's own exit, and **the box slot is left
  intact** so the owner keeps its own reference and may re-read the field, match
  the union again, or move the payload out itself. The refcount balances at
  exactly one free, and the borrowed union is **not** consumed by the match — a
  later read of it is legal, not `E3102`. The retain is unconditional (no escape
  analysis): the binding's release is structural — its scope exit — so a blanket
  acquire is blanket-balanced.
- **A match binding on a CO-OWNED union takes the SAME RETAIN, and co-ownership is
  the third state the two bullets above do not cover.** A scrutinee can hold an
  owned reference while something else holds the same box: `let borrowed = h.ty`
  (a read out of a mutable field, or an array element bound to a `var`, promoted by
  `__mm_retain`), a payload retained out of a borrowed union and then matched again,
  a call's owned result (a `return h.ty` promotes its borrow by incref), a caught
  error box (the thrower may have retained it), and a consumed parameter (the caller
  may have increfed a borrow to transfer it). Nulling such a box's slot steals it
  from an owner that is still reading, so the acquisition asks *"is this frame the
  box's SOLE owner?"* rather than *"does this value own a reference?"* — and, as with
  a borrowed scrutinee, a retaining match consumes nothing: a later read is legal.
  Recovering the move-out for a call result needs a whole-program *"does this
  function's return launder a borrow"* fact, which shv2 does not compute.
- **A SOLE box may hold a CO-OWNED payload, so soleness is NOT transitive.** The
  construct co-owns a borrowed argument by `__mm_incref` rather than moving it in, so
  `Wrap.held(<a borrowed union>)` yields a box this frame really is the only owner of
  whose *slot* holds a shared reference. Vacating that slot proves the frame now holds
  the slot's reference; it proves nothing about the allocation. **So a payload moved
  out of a sole box is itself CO-OWNED**, and a nested `match` on it retains — one
  refcount pair per nested move-out, against a stolen slot. The same is true of a
  container element handed back by a `remove`: the container no longer holds it, but
  whoever co-owned it into the container still does.
- **Drop is a tag-conditional STATIC cascade.** When an owned managed-payload
  union is dropped, its `__destruct_<U>` loads the tag, and for the live case
  drops each still-present managed field through its own type's destructor (a
  `String` field via `__str_decref`, a `struct` field via `__mm_decref`), then
  frees the box. A moved-out slot is null and is skipped, so a payload is freed
  exactly once whether it was moved out, discarded, or left in place.

Passing a managed-payload union across a call boundary as a *return value* is
still the cross-call ownership ruling deferred beyond this rung; passing one as a
**parameter** and binding its managed payload out is the retain above (D1b).

## Tests

<!-- test: struct-payload-drop-leak-free -->
An owned union with a struct payload, dropped at scope exit without being matched,
frees its struct payload through the cascade — no leak (a leak is exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	print("{s}")
	return 7
end 'main'
```
```exitcode
7
```
```stdout
solid

```

<!-- test: struct-payload-match-consume -->
Matching an owned struct-payload union binds the struct, reads its field, and
consumes the union — the struct is freed once (via the binding), the box once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	match s 'check'
		empty then return 0
		solid(b) then return b.mass
	end 'check'
end 'main'
```
```exitcode
5
```

<!-- test: string-payload-drop-leak-free -->
An owned union with a String payload, dropped at scope exit without being matched,
frees its String through `__str_decref` — no leak.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("hello world this is a long enough string to be heap")
	print("{m}")
	return 9
end 'main'
```
```exitcode
9
```
```stdout
text

```

<!-- test: string-payload-match-consume -->
Matching an owned String-payload union binds the String, prints it, and consumes
the union. The String is freed once (via the binding), the box once.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("hi")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: string-payload-interpolated -->
An interpolated String moved into a union payload, then matched back out and
printed. The interpolation temporary is owned; the move transfers it into the box.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let m = Message.text("v{41}")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v41
```

<!-- test: match-borrow-no-managed-binding -->
A match that binds no managed payload (a tag-only arm) borrows: the union is not
consumed and is dropped at scope exit, freeing its struct payload once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(3))
	let code = match s 'check'
		empty gives 1
		solid gives 2
	end 'check'
	return code
end 'main'
```
```exitcode
2
```

<!-- test: match-borrow-binds-managed-payload -->
The "IS bound" twin of the case directly above: a match on a BORROWED union (a
parameter) that DOES bind its managed payload. The payload cannot be moved out — its
owner is the caller's local — so it is RETAINED (`__mm_incref` at the bind), the
binding owns that second reference and drops it at the arm's exit, and the box slot is
left intact. The caller's union is NOT consumed by the call.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function massOf(s Shape) returns Integer
	return match s 'check'
		empty gives 1
		solid(b) gives b.mass
	end 'check'
end 'massOf'

function main() returns ExitCode
	let s = Shape.solid(Body.create(3))
	return massOf(s) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: borrowed-bind-then-the-owner-moves-it-out -->
⭐ **THE CASE THAT CATCHES A NULLED SLOT.** A retain must NOT clear the slot the payload
was loaded from, and the sharpest reader of that slot is the OWNER doing its own
move-out afterwards: `massOf(s)` binds the struct payload out of a borrowed parameter,
then `match s` in the caller MOVES the same payload out and reads `b.mass`. A nulling
implementation loads 0 here and dereferences it. It also pins the refcount: the borrow's
retained reference is dropped at the callee's arm exit, the moved-out binding drops the
last one at `main`'s scope exit, and the box's cascade skips the (now genuinely) nulled
slot — one free, no leak.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function massOf(s Shape) returns Integer
	return match s 'check'
		empty gives 1
		solid(b) gives b.mass
	end 'check'
end 'massOf'

function main() returns ExitCode
	let s = Shape.solid(Body.create(3))
	let borrowed = massOf(s)
	let owned = match s 'own'
		empty gives 1
		solid(b) gives b.mass
	end 'own'
	return (borrowed + owned) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: borrowed-bind-in-an-arm-that-returns -->
A retained payload binding leaves its arm through a TERMINATED exit (`then return`)
rather than the live fall-through every case above uses, so it drops through
`emitScopeDrops` down to the arm's floor instead of through `dropArmScopedOwned` —
the other half of the arm-exit rule, and the half a `gives` arm cannot reach.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function show(m M) returns Integer
	match m 'k'
		silent then return 0
		text(s) then print(s)
	end 'k'
	return 3
end 'show'

function main() returns ExitCode
	let m = M.text("a returned-arm payload string, long enough to be a real heap allocation")
	return show(m) as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
a returned-arm payload string, long enough to be a real heap allocation
```

<!-- test: borrowed-bind-in-an-arm-that-breaks -->
The third arm exit: a `break` out of an enclosing loop drops the retained binding
through the LOOP's floor. The arm binds but never prints, so nothing but the leak gate
can tell the drop happened — which is exactly the point.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 100)

union M
	silent
	text(body String)
end 'M'

function show(m M) returns Integer
	var i = 0 as Round
	while i < 10 'spin'
		match m 'k'
			silent then return 0
			text(s) then break 'spin'
		end 'k'
		i = i + 1
	end 'spin'
	return 5
end 'show'

function main() returns ExitCode
	let m = M.text("a broken-arm payload string, long enough to be a real heap allocation")
	return show(m) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: borrowed-bind-then-moved-into-another-union -->
The retained `+1` TRANSFERS: the binding is moved into a second union's payload, so its
own scope-exit drop is skipped (`movedFrom`) and the new box owns the reference instead.
The source union still holds its slot and drops the other reference at `main`'s exit —
two owners, two drops, one free.
```maxon
union M
	silent
	text(body String)
end 'M'

union Held
	nothing
	holds(v String)
end 'Held'

function rewrap(m M) returns Held
	return match m 'k'
		silent gives Held.nothing
		text(s) gives Held.holds(s)
	end 'k'
end 'rewrap'

function main() returns ExitCode
	let m = M.text("a rewrapped payload string, long enough to be a real heap allocation")
	let h = rewrap(m)
	match h 'j'
		nothing then return 0
		holds(v) then print(v)
	end 'j'
	return 8
end 'main'
```
```exitcode
8
```
```stdout
a rewrapped payload string, long enough to be a real heap allocation
```

<!-- test: three-managed-payloads-out-of-one-borrowed-union -->
Three String payloads bound out of ONE borrowed union in a single arm: three increfs at
the bind, three drops at the arm's exit, and one of them escapes as the arm's `gives`
value. A retain emitted once per ARM rather than once per BINDING would leak two.
```maxon
union R
	one(a String)
	three(a String, b String, c String)
end 'R'

function first(r R) returns String
	return match r 's'
		one(a) gives a
		three(a, b, c) gives a
	end 's'
end 'first'

function main() returns ExitCode
	let r = R.three("a first payload string long enough to heap", b: "a second payload string long enough to heap", c: "a third payload string long enough to heap")
	print(first(r))
	return 6
end 'main'
```
```exitcode
6
```
```stdout
a first payload string long enough to heap
```

<!-- test: retained-payload-that-escapes-shares-the-owner-s-record -->
The retain is an INCREF, not a copy, so a payload that escapes the borrow is a SECOND
owner of the SAME record — and an in-place `append` through it is visible to the union
that still holds it. That is not a shv2 quirk: the **bootstrap answers identically**
(measured — same two lines of stdout), so the observable semantics of a payload bound
out of a borrowed union agree across the two compilers. It is pinned because it is the
one place the incref-vs-copy choice is *observable* rather than internal: were the bind
to copy the way `promoteBorrowedToOwned` copies a borrowed String for a `var`, this
program would print the unmutated original and disagree with the reference.
```maxon
union M
	silent
	text(body String)
end 'M'

function grab(m M) returns String
	return match m 'k'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'k'
end 'grab'

function main() returns ExitCode
	let m = M.text("original payload, long enough to be a real heap allocation")
	var escaped = grab(m)
	escaped.append(" MUTATED")
	print(grab(m))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original payload, long enough to be a real heap allocation MUTATED
```

<!-- test: borrowed-struct-payload-bind-leak-free -->
⭐ **THE LEAK GATE, struct half.** 300 rounds of binding a STRUCT payload out of a
borrowed parameter. An unbalanced `incref` leaves the `Body` box's refcount at 300 when
the union's cascade releases it, so it is never freed and the run exits 101; an
unbalanced `decref` frees it under the union that still points at it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 1000)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function massOf(s Shape) returns Integer
	return match s 'check'
		empty gives 1
		solid(b) gives b.mass
	end 'check'
end 'massOf'

function main() returns ExitCode
	let s = Shape.solid(Body.create(3))
	var i = 0 as Round
	var total = 0 as Integer
	while i < 300 'spin'
		total = total + massOf(s)
		i = i + 1
	end 'spin'
	if total != 900 'wrong'
		return 1
	end 'wrong'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: discard-managed-field -->
A `_` discard of a managed field binds nothing and does not consume: the union is
dropped at scope exit and the cascade frees the discarded String.

⚠ The discard is PARTIAL (`text(_, tag)`), and it has to be: an all-discard list
(`text(_)`) is `E3081` — the bare case name is the only spelling of "ignore every
payload" (`match-payload-discard-bindings.md`), and the bare form is already the case
directly above. It would also not test THIS: with no binding list there is no discard
for `declarePayloadBindings` to skip, so the slot that stays in the box for the cascade
would never be exercised.
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String, code Integer)
end 'Message'

function main() returns ExitCode
	let m = Message.text("discard me, i am long enough to be a heap string", code: 4)
	let code = match m 'check'
		silent gives 0
		text(_, tag) gives tag
	end 'check'
	return code
end 'main'
```
```exitcode
4
```

<!-- test: retained-payload-outlives-its-container -->
⭐ **THE LIFETIME CASE.** The retained reference outlives the box it came out of: `grab(m)`
returns the payload, then `m` — the only other owner — dies at `makeAndExtract`'s scope exit and
its cascade releases the box's reference. The payload must survive on the caller's reference
alone. Without the retain this is a use-after-free printing freed bytes; with a retain but no
drop it is exit 101.
```maxon
union M
	silent
	text(body String)
end 'M'

function grab(m M) returns String
	return match m 'k'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'k'
end 'grab'

function makeAndExtract() returns String
	let m = M.text("the payload that must outlive its own container, heap-long")
	return grab(m)
end 'makeAndExtract'

function main() returns ExitCode
	print(makeAndExtract())
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the payload that must outlive its own container, heap-long
```

<!-- test: retained-struct-payload-outlives-its-container -->
The struct half of the case above, and a sharper detector: the container is gone and then a
FIELD of the escaped payload is read. A dangling box does not have to crash — it can simply
answer with whatever the freed memory holds, which is the wrong-answer shape a `print` would
hide and an exit code will not.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function bodyOf(s Shape) returns Body
	return match s 'k'
		empty gives Body.create(0)
		solid(b) gives b
	end 'k'
end 'bodyOf'

function makeAndExtract(mass Integer) returns Body
	let s = Shape.solid(Body.create(mass))
	return bodyOf(s)
end 'makeAndExtract'

function main() returns ExitCode
	let b = makeAndExtract(37)
	return b.mass as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: var-container-reassigned-while-a-retained-payload-is-live -->
The container is a `var` and is REASSIGNED while a payload retained out of it is still live.
The reassignment drops the OLD box, whose cascade releases its own reference to the first
payload — the escaped binding holds the other one, so the first string must still read intact
afterwards, and the second box's payload must be independent of it.
```maxon
union M
	silent
	text(body String)
end 'M'

function grab(m M) returns String
	return match m 'k'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'k'
end 'grab'

function main() returns ExitCode
	var m = M.text("the FIRST payload string, long enough to be a real heap allocation")
	let escaped = grab(m)
	m = M.text("the SECOND payload string, long enough to be a real heap allocation")
	print(escaped)
	print(grab(m))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the FIRST payload string, long enough to be a real heap allocationthe SECOND payload string, long enough to be a real heap allocation
```

<!-- test: owned-move-out-and-borrowed-retain-in-one-function -->
Both bind paths in ONE function and one `ownedBindings` list: a locally built union is MOVED
out of (its slot nulled, the union consumed) and a borrowed parameter is RETAINED from (slot
intact, not consumed), with the two arms' bindings dropping through the same arm-exit machinery.
A drop-floor that confused the two would double-free one of them.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function mixed(borrowed M) returns Integer
	let owned = M.text("a locally built payload string long enough for the heap")
	let a = match owned 'o'
		silent gives 0
		text(s) gives 1
	end 'o'
	let b = match borrowed 'b'
		silent gives 0
		text(t) gives 2
	end 'b'
	return a + b
end 'mixed'

function main() returns ExitCode
	let m = M.text("the caller's payload string, long enough to be a real heap allocation")
	return mixed(m) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: discard-on-a-borrowed-union-increfs-nothing -->
The `_` discard path is unchanged by the retain: a discard binds nothing, so it emits no
`incref` and leaves its payload in the box for the owner's cascade. Called twice on the same
borrowed union — an incref with no matching binding to drop it would leak (exit 101), and a
decref with no incref would free the caller's payload under it.
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String, code Integer)
end 'Message'

function codeOf(m Message) returns Integer
	return match m 'k'
		silent gives 0
		text(_, tag) gives tag
	end 'k'
end 'codeOf'

function main() returns ExitCode
	let m = Message.text("a discarded payload string long enough to be a heap one", code: 4)
	let a = codeOf(m)
	let b = codeOf(m)
	return (a + b) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: repeated-borrowed-binds-in-a-loop-with-an-early-return -->
Five binds of the same borrowed payload inside a loop, with an early `return` reachable from
inside the loop body. Each iteration's retain must be released on the iteration's own exit
rather than accumulating to the function's end.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 100)

union M
	silent
	text(body String)
end 'M'

function scan(m M) returns Integer
	var i = 0 as Round
	var seen = 0 as Integer
	while i < 5 'spin'
		let n = match m 'k'
			silent gives 0
			text(s) gives 1
		end 'k'
		if n == 0 'stop'
			return 0
		end 'stop'
		seen = seen + n
		i = i + 1
	end 'spin'
	return seen
end 'scan'

function main() returns ExitCode
	let m = M.text("a repeatedly scanned payload string, long enough for the heap")
	let a = scan(m)
	print("{a}")
	return a as ExitCode
end 'main'
```
```exitcode
5
```
```stdout
5
```

<!-- test: borrowed-bind-two-levels-deep-then-the-owner-moves-it-out -->
The container is borrowed by `outer`, bound there, and then handed on to `inner`, which borrows
and binds it again — two live retains of one payload at different frames — and only afterwards
does the owner in `main` move it out for itself.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function inner(m M) returns Integer
	return match m 'k'
		silent gives 0
		text(s) gives 3
	end 'k'
end 'inner'

function outer(m M) returns Integer
	let first = match m 'k'
		silent gives 0
		text(s) gives 4
	end 'k'
	return first + inner(m)
end 'outer'

function main() returns ExitCode
	let m = M.text("a payload string handed down two borrow levels, heap-long")
	let a = outer(m)
	match m 'own'
		silent then return 0
		text(s) then print(s)
	end 'own'
	return a as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
a payload string handed down two borrow levels, heap-long
```

<!-- test: retained-give-merged-with-literal-gives -->
A result phi joining a RETAINED give (`one(a) gives a`) with two freshly built literal gives.
All three edges must arrive owning their value, or the merged result is freed once too often
on one path and never on another.
```maxon
union M
	one(a String)
	two(b String)
	none
end 'M'

function pick(m M) returns String
	return match m 'k'
		one(a) gives a
		two(b) gives "a rebuilt literal give long enough to be a heap string"
		none gives "a third literal give also long enough to be a heap string"
	end 'k'
end 'pick'

function main() returns ExitCode
	let x = M.one("the retained give, long enough to be a real heap allocation")
	let y = M.two("the discarded payload, long enough to be a real heap allocation")
	print(pick(x))
	print(pick(y))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the retained give, long enough to be a real heap allocationa rebuilt literal give long enough to be a heap string
```

<!-- test: nested-matches-of-two-borrowed-unions -->
A borrowed union matched INSIDE an arm of another borrowed union's match: two retains live at
once, in nested arm scopes, dropped in the right order. Called twice, so a consumed scrutinee
would show as a use-after-move on the second call.
```maxon
typealias Integer = int(i64.min to i64.max)

union Inner
	quiet
	loud(word String)
end 'Inner'

union Outer
	blank
	holds(label String)
end 'Outer'

function both(o Outer, i Inner) returns Integer
	return match o 'x'
		blank gives 0
		holds(label) gives match i 'y'
			quiet gives 1
			loud(word) gives 2
		end 'y'
	end 'x'
end 'both'

function main() returns ExitCode
	let o = Outer.holds("the outer label string, long enough to be a heap allocation")
	let i = Inner.loud("the inner word string, long enough to be a heap allocation")
	let a = both(o, i: i)
	let b = both(o, i: i)
	return (a + b) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: the-same-borrowed-container-matched-again-inside-its-own-arm -->
Re-entrancy: the arm body matches the SAME borrowed container a second time while its own
retain is still live. Legal exactly because a retain does not consume — the inner match sees
the tag and the slot unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function depth(m M) returns Integer
	return match m 'k'
		silent gives 0
		text(s) gives match m 'again'
			silent gives 1
			text(t) gives 6
		end 'again'
	end 'k'
end 'depth'

function main() returns ExitCode
	let m = M.text("a re-entrantly matched payload string, long enough for the heap")
	return depth(m) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: borrowed-bind-out-of-a-temporary-container -->
The caller's container is a TEMPORARY (`peek(M.text(…))`), so it is owned by no binding and
dies at the statement's end. The callee still borrows it and retains from it, which is the
ordinary case — the interesting half is that the temporary's own drop must still happen exactly
once afterwards.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function peek(m M) returns Integer
	return match m 'k'
		silent gives 0
		text(s) gives 11
	end 'k'
end 'peek'

function main() returns ExitCode
	return peek(M.text("a temporary container's payload, long enough for the heap")) as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: retained-give-routed-through-a-try-channel -->
The retained payload leaves its function through a `throws` signature and is joined with an
`otherwise` fallback at the call site — the ok-edge/error-edge phi reconciliation, over a value
whose `+1` came from a retain rather than from a fresh allocation.
```maxon
union Fail implements Error
	because(why String)
end 'Fail'

union M
	silent
	text(body String)
end 'M'

function grab(m M) returns String throws Fail
	return match m 'k'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'k'
end 'grab'

function main() returns ExitCode
	let m = M.text("a payload string routed through a try channel, heap-long")
	let out = try grab(m) otherwise "an otherwise literal long enough to be a heap string"
	print(out)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a payload string routed through a try channel, heap-long
```

<!-- test: retained-payload-moved-into-a-durable-struct-field -->
⭐ The hardest lifetime shape in the set: the retained payload is moved into a STRUCT FIELD,
and the struct outlives both the arm and the union it was bound out of (`m` dies at
`makeAndWrap`'s exit). The retain's `+1` transfers into the field, the binding's own drop is
skipped as moved-from, and the union's cascade releases the other reference.
```maxon
union M
	silent
	text(body String)
end 'M'

type Holder
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Holder'

function wrap(m M) returns Holder
	return match m 'k'
		silent gives Holder.create("a fallback literal long enough to be a heap string")
		text(s) gives Holder.create(s)
	end 'k'
end 'wrap'

function makeAndWrap() returns Holder
	let m = M.text("a payload moved into a durable struct field, long enough for the heap")
	return wrap(m)
end 'makeAndWrap'

function main() returns ExitCode
	let h = makeAndWrap()
	print(h.label)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a payload moved into a durable struct field, long enough for the heap
```

<!-- test: two-borrowed-unions-a-string-one-and-a-struct-one -->
Both managed payload classes bound out of borrowed parameters in one call, twice over: a
`String` payload (dropped through `__str_decref`) and a `struct` payload (dropped through the
struct's own destructor callee). The retain is emitted the same way for both; only the release
is tag-routed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union A
	none
	one(s String)
end 'A'

union B
	none
	two(b Body)
end 'B'

function both(a A, b B) returns Integer
	let x = match a 'x'
		none gives 0
		one(s) gives 5
	end 'x'
	let y = match b 'y'
		none gives 0
		two(body) gives body.mass
	end 'y'
	return x + y
end 'both'

function main() returns ExitCode
	let a = A.one("the first union's payload, long enough to be a heap string")
	let b = B.two(Body.create(9))
	let r1 = both(a, b: b)
	let r2 = both(a, b: b)
	return (r1 + r2) as ExitCode
end 'main'
```
```exitcode
28
```

<!-- test: two-managed-fields-drop -->
A case with two String fields, dropped at scope exit, frees both.
```maxon

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("the first heap string is long", b: "the second heap string is long too")
	print("{p}")
	return 6
end 'main'
```
```exitcode
6
```
```stdout
both

```

<!-- test: two-managed-fields-bind-one-discard-one -->
A two-String case binds one field and discards the other. The bound one is freed
via its binding; the discarded one is freed by the cascade at scope exit.
```maxon

union Pair
	none
	both(a String, b String)
end 'Pair'

function main() returns ExitCode
	let p = Pair.both("bound first string long enough to heap", b: "discarded second string also heap")
	match p 'check'
		none then return 0
		both(a, _) then print(a)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bound first string long enough to heap
```

<!-- test: fallthrough-out-of-a-binding-arm-into-a-payload-free-one -->
The POSITIVE control for the three refusals below, and the reason they are narrow: falling
OUT of a payload-binding arm is fine — the arm's own bindings were established by the case it
actually matched, and they are dropped on the fallthrough edge like any other live exit. Only
the arm being fallen INTO may not bind.
```maxon
typealias Code = int(0 to 255)

union U
	a(x String)
	b
end 'U'

function main() returns ExitCode
	let u = U.a("the falling-through payload, long enough to be a heap string")
	var t = 0 as Code
	match u 'k'
		a(s) then print(s) and fallthrough
		b then t = 7
	end 'k'
	return t as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
the falling-through payload, long enough to be a heap string
```

<!-- test: error.fallthrough-into-a-managed-binding-arm -->
⭐ **A FALLTHROUGH TARGET MAY NOT BIND A PAYLOAD**, and this is the case that made it a
refusal rather than a rule on paper: `a(s) … and fallthrough` reaches `b(t)`'s body while the
union holds an `a`, so `t` destructures a case the value does not have. **MEASURED before the
refusal existed: SIGSEGV (exit 139)** — arm `a` moved its payload out and nulled the slot, and
`b(t)` then bound the null and printed it.

It is the `or`-pattern rule (`rejectOrPatternBinding`) one construct over: *a payload binding
is meaningful only where exactly one case matched*, and a fallthrough edge is precisely an
arrival from a DIFFERENT case. Neither reference compiler refuses it and both produce
garbage — the bootstrap prints the payload twice here, because it does not null the slot — so
there is no behaviour being broken, only a hole being closed.
```maxon
union U
	a(x String)
	b(y String)
end 'U'

function main() returns ExitCode
	let u = U.a("the fallthrough source payload, long enough to be a heap string")
	match u 'k'
		a(s) then print(s) and fallthrough
		b(t) then print(t)
	end 'k'
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:3: Unsupported: `and fallthrough` into the payload-binding arm 'b' of `union U` — the preceding arm matched a DIFFERENT case, so on that edge these bindings would destructure a case the value does not hold (reading another case's slots, or a slot that arm already moved out and nulled). Fall through into a payload-free arm, or give this arm its own body
```

<!-- test: error.fallthrough-into-a-borrowed-binding-arm -->
The same refusal on the BORROWED scrutinee D1b opens, where the payload is retained rather
than moved so the slot is intact — which makes the failure a silent TYPE CONFUSION instead of
a crash: `b(n)`'s `Code` binding would read the `String` POINTER arm `a` matched. Measured at
exit 1 (the pointer failing `Code`'s range check) before the refusal existed; the bootstrap
reaches the same confusion by its own route.
```maxon
typealias Code = int(0 to 255)

union U
	a(x String)
	b(y Code)
end 'U'

function show(u U) returns Code
	match u 'k'
		a(s) then print(s) and fallthrough
		b(n) then return n
	end 'k'
	return 0
end 'show'

function main() returns ExitCode
	let u = U.a("a heap string long enough to be a real allocation")
	return show(u) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:3: Unsupported: `and fallthrough` into the payload-binding arm 'b' of `union U` — the preceding arm matched a DIFFERENT case, so on that edge these bindings would destructure a case the value does not hold (reading another case's slots, or a slot that arm already moved out and nulled). Fall through into a payload-free arm, or give this arm its own body
```

<!-- test: error.fallthrough-into-a-scalar-binding-arm -->
The refusal is about the BINDING, not about ownership: a SCALAR payload has no drop, no
pointer and no crash, and is therefore the worst of the three — a silently wrong number.
**MEASURED at 18 in BOTH compilers** (`m` reads `a`'s `9`, so `t = 9 + 9`) where `b`'s payload
does not exist at all. That the two references agree on 18 is not evidence it is right; it is
evidence neither of them asks the question.
```maxon
typealias Code = int(0 to 255)

union U
	a(x Code)
	b(y Code)
end 'U'

function main() returns ExitCode
	let u = U.a(9)
	var t = 0 as Code
	match u 'k'
		a(n) then t = n and fallthrough
		b(m) then t = t + m
	end 'k'
	return t as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:3: Unsupported: `and fallthrough` into the payload-binding arm 'b' of `union U` — the preceding arm matched a DIFFERENT case, so on that edge these bindings would destructure a case the value does not hold (reading another case's slots, or a slot that arm already moved out and nulled). Fall through into a payload-free arm, or give this arm its own body
```

<!-- test: error.fallthrough-out-of-a-consuming-arm-into-a-read-of-the-scrutinee -->
Falling OUT of a binding arm stays legal (`fallthrough-out-of-a-binding-arm-into-a-payload-free-one`),
but the arm fallen INTO inherits what the arm fallen FROM did: on that edge the scrutinee's slot is
nulled, so a read of it there is E3102 rather than a null load. The successor is reachable BOTH ways —
by its own dispatch edge, where `m` is whole, and by the fallthrough edge, where it is not — so the
refusal is the conservative answer of the two, and it is the same rule an `and fallthrough` target
already obeys for `movedFrom`.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function grab(m M) returns Integer
	return match m 'g'
		silent gives 0 as Integer
		text(s) gives s.byteLength() as Integer
	end 'g'
end 'grab'

function main() returns ExitCode
	let m = M.text("an owned payload string, long enough to be a real heap allocation")
	var n = 0 as Integer
	match m 'k'
		text(s) then n = s.byteLength() as Integer and fallthrough
		silent then n = n + grab(m)
	end 'k'
	print("n={n}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:28: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: fallthrough-into-a-bare-payload-carrying-arm -->
The blast-radius guard for `error.fallthrough-into-a-*-binding-arm`: the refusal is about the
BINDINGS, not about the case. A payload-carrying case named BARE binds nothing, destructures nothing,
and is a legal fallthrough target — `b` reads no slot at all, so the edge from `a` carries no
misinterpretation. Answers 10 (`a`'s 9, plus the 1 the fallen-into arm adds).
```maxon
typealias Code = int(0 to 255)

union U
	a(x Code)
	b(y Code)
end 'U'

function main() returns ExitCode
	let u = U.a(9)
	var t = 0 as Code
	match u 'k'
		a(n) then t = n and fallthrough
		b then t = t + 1
	end 'k'
	return t as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: fallthrough-into-an-or-pattern-arm -->
The other half of that guard: an `or`-pattern arm can never bind (a payload binding on an `or`-pattern
is `rejectOrPatternBinding`), so it reaches the fallthrough check with an empty binding list and passes
it. A check that keyed on "does this arm's case carry a payload?" rather than on the bindings in hand
would have rejected this one too.
```maxon
typealias Code = int(0 to 255)

union U
	a(x Code)
	b
	c
end 'U'

function main() returns ExitCode
	let u = U.a(9)
	var t = 0 as Code
	match u 'k'
		a(n) then t = n and fallthrough
		b or c then t = t + 1
	end 'k'
	return t as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: two-binding-arms-fall-through -->
Two arms that each bind a managed payload AND fall through: each arm's binding is dropped on ITS OWN
exit edge, not accumulated for the continuation (where the other arm's value would be garbage). Both
paths are leak-free and crash-free.
```maxon

union U
	a(x String)
	b(y String)
end 'U'

function main() returns ExitCode
	let u = U.b("the taken arm b string, long enough to be a real heap allocation")
	match u 'check'
		a(s) then print(s)
		b(t) then print(t)
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the taken arm b string, long enough to be a real heap allocation
```

<!-- test: var-reassign-after-partial-move -->
A `var` union consumed by a binding-match is `partiallyMoved` (a re-read is E3102), but a REASSIGNMENT
revives it: the fresh value has no moved-out slots, so a later match is legal again. The old box (with
its nulled payload slot) is dropped at the reassignment; the new one at scope exit — both leak-free.
```maxon

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	var m = Message.text("reassign first payload string long enough to be a real heap allocation")
	match m 'check'
		silent then return 0
		text(s) then print(s)
	end 'check'
	m = Message.text("reassign second payload string long enough to be a real heap allocation")
	match m 'again'
		silent then return 0
		text(t) then print(t)
	end 'again'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reassign first payload string long enough to be a real heap allocationreassign second payload string long enough to be a real heap allocation
```

<!-- test: construct-co-owns-string-source -->
Storing a String binding into a union payload CO-OWNS it (⚖ 2026-08-12): the payload slot takes its own
reference, `msg` stays live, and its scope-exit drop releases the reference it always held. The record is
freed exactly once, by whichever of the two owners drops last. (This construct used to POISON `msg`, on
the premise that shv2 has no incref — retracted when a durable store started taking a reference.)
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

union Message
	silent
	text(body String)
end 'Message'

function main() returns ExitCode
	let msg = build(1)
	let m = Message.text(msg)
	print("{m}")
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
textv1

```

<!-- test: construct-co-owns-struct-source -->
The struct spelling of the case above. `Shape.solid(b)` gives the payload slot its own reference to `b`'s
box; `b` stays readable and drops its own reference at scope exit, so the box is released once by the
union's destructor and once by `b` — matching the two references taken.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let b = Body.create(5)
	let s = Shape.solid(b)
	print("{s}")
	return b.mass
end 'main'
```
```exitcode
5
```
```stdout
solid

```

<!-- test: error.match-consume-then-use -->
A binding match consumes the union; a later read of the scrutinee is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

type Body
	export var mass as Integer

	static function create(mass Integer) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Shape
	empty
	solid(body Body)
end 'Shape'

function main() returns ExitCode
	let s = Shape.solid(Body.create(5))
	let first = match s 'check'
		empty gives 0
		solid(b) gives b.mass
	end 'check'
	let second = match s 'again'
		empty gives 0
		solid(b) gives b.mass
	end 'again'
	return first + second
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:21: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.read-the-consumed-scrutinee-inside-the-arm-that-moved-it -->
⭐ **THE SIGSEGV D1b WIDENED.** `error.match-consume-then-use` pins the read that comes AFTER the
match; this is the same read INSIDE the arm that did the moving, where the slot is nulled from the
bind onwards. The scrutinee was marked `partiallyMoved` only once the whole arm loop had been parsed,
so this read parsed against a LIVE `m` and compiled — and `grab` binds the payload out of its borrowed
parameter, loads the null the outer arm just stored, and dereferences it. **Measured: exit 139
(SIGSEGV).** It is D1b that made it reachable this way: the borrowed bind `grab` needs was `E2015`
until this rung, so before it the only spelling was a nested `match m` (below). The mark now lands at
the arm that moved, not after the loop.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function grab(m M) returns Integer
	return match m 'g'
		silent gives 0 as Integer
		text(s) gives s.byteLength() as Integer
	end 'g'
end 'grab'

function main() returns ExitCode
	let m = M.text("an owned payload string, long enough to be a real heap allocation")
	let r = match m 'k'
		silent gives 0 as Integer
		text(s) gives grab(m)
	end 'k'
	print("r={r}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:22: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.re-match-the-consumed-scrutinee-inside-its-own-arm -->
The pre-D1b spelling of the case above, and it segfaulted the same way (**measured: exit 139**): the
arm binds `s`, which nulls slot 0, and the nested `match m` in its own body binds `t` from that null.
The borrowed twin — `the-same-borrowed-container-matched-again-inside-its-own-arm` — is LEGAL and stays
legal, because a retain leaves the slot intact. Which of the two a program gets is exactly the
owned/borrowed split, so the two tests are read together.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function main() returns ExitCode
	let m = M.text("an owned payload string, long enough to be a real heap allocation")
	let r = match m 'k'
		silent gives 0 as Integer
		text(s) gives match m 'again'
			silent gives 1 as Integer
			text(t) gives t.byteLength() as Integer
		end 'again'
	end 'k'
	print("r={r}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:23: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: the-consumed-scrutinee-stays-live-in-a-sibling-arm -->
⭐ **THE OVER-REJECTION GUARD for the two refusals above.** The arms of a match are MUTUALLY
EXCLUSIVE, so an arm that moved the payload out says nothing about the arm that did not run: reading
`m` in a SIBLING arm is legal and must stay legal. Marking the scrutinee consumed and leaving it
marked for the rest of the loop would reject this — and would do it ORDER-DEPENDENTLY, since swapping
the two arms makes the same program compile again. Each fresh arm therefore rewinds the bit
(`beginFreshArm`), exactly as it rewinds `movedFrom`.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function grab(m M) returns Integer
	return match m 'g'
		silent gives 0 as Integer
		text(s) gives s.byteLength() as Integer
	end 'g'
end 'grab'

function main() returns ExitCode
	let m = M.text("an owned payload string, long enough to be a real heap allocation")
	let r = match m 'k'
		text(s) gives s.byteLength() as Integer
		silent gives grab(m)
	end 'k'
	print("r={r}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=65
```

<!-- test: error.a-nested-consume-survives-a-sibling-arm -->
⭐ **THE CARRY.** The rewind above is per-PATH, but the code AFTER the match runs on EVERY path, so a
consume on any one of them has to survive it. Here the consume is not made by the outer match's own
arm at all — the `text` arm's body is a NESTED `match m`, and it is that inner match which nulls the
slot. The following `silent` arm is fresh and rewinds the bit; without the carry out of the arm it
happened in, `m` would read LIVE after the outer match and `grab(m)` would load the nulled slot on the
`text` path. Ordering is the whole test: put `silent` first and no rewind ever sees the bit.
```maxon
typealias Integer = int(i64.min to i64.max)

union M
	silent
	text(body String)
end 'M'

function grab(m M) returns Integer
	return match m 'g'
		silent gives 0 as Integer
		text(s) gives s.byteLength() as Integer
	end 'g'
end 'grab'

function main() returns ExitCode
	let m = M.text("an owned payload string, long enough to be a real heap allocation")
	let r = match m 'outer'
		text gives match m 'inner'
			silent gives 0 as Integer
			text(t) gives t.byteLength() as Integer
		end 'inner'
		silent gives 0 as Integer
	end 'outer'
	print("r={r} after={grab(m)}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:27: use of moved value 'm': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrowed-bind-in-an-arm-that-continues -->
The fourth arm exit, beside the fall-through, the `return` and the `break` already pinned: a
`continue` drops the retained binding through the LOOP's floor. 200 rounds, so an unbalanced retain is
exit 101 rather than a coin flip.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 1000)

union M
	silent
	text(body String)
end 'M'

function show(m M) returns Integer
	var i = 0 as Round
	var n = 0 as Integer
	while i < 200 'spin'
		i = i + 1
		match m 'k'
			silent then n = n + 1
			text(s) then continue
		end 'k'
		n = n + 2
	end 'spin'
	return n
end 'show'

function main() returns ExitCode
	let m = M.text("a continued-arm payload string, long enough to be a real heap allocation")
	print("n={show(m)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=0
```

<!-- test: borrowed-bind-in-an-arm-that-throws -->
The ERROR channel exit: the arm binds a payload out of the borrowed parameter and then THROWS, so the
retained reference is released on the propagate path rather than through any of the four normal exits.
200 rounds through a `try … otherwise`, so a reference leaked on the throw edge is exit 101.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 1000)

enum Boom implements Error
	bad
end 'Boom'

union M
	silent
	text(body String)
end 'M'

function show(m M) returns Integer throws Boom
	match m 'k'
		silent then return 0
		text(s) then throw Boom.bad
	end 'k'
	return 3
end 'show'

function main() returns ExitCode
	let m = M.text("a thrown-arm payload string, long enough to be a real heap allocation")
	var i = 0 as Round
	var n = 0 as Integer
	while i < 200 'spin'
		n = try show(m) otherwise 4
		i = i + 1
	end 'spin'
	return n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: borrowed-bind-in-an-arm-that-breaks-the-match -->
A `break` out of the MATCH (not out of a loop) leaves the arm through `MatchContext.breakExits`, whose
marks are trimmed to the match's owned floor (`moveMarkPrefix`) — the one capture site that is not
already at its join's height. The retained binding must be dropped on that edge too. 200 rounds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 1000)

union M
	silent
	text(body String)
end 'M'

function show(m M) returns Integer
	var i = 0 as Round
	var n = 0 as Integer
	while i < 200 'spin'
		match m 'k'
			silent then n = n + 1
			text(s) then break 'k'
		end 'k'
		n = n + 7
		i = i + 1
	end 'spin'
	return n
end 'show'

function main() returns ExitCode
	let m = M.text("a match-break payload string, long enough to be a real heap allocation")
	print("n={show(m)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=1400
```

<!-- test: borrowed-bind-out-of-a-union-held-in-a-struct-field -->
The scrutinee is neither a local nor a parameter but a struct FIELD read (`h.m`), which owns nothing —
so it takes the borrowed/retain path exactly as a parameter does, and the field keeps its own
reference. 200 rounds over the same field: an unbalanced retain leaves the payload's refcount at 200
when the struct's cascade releases it (exit 101), and a missing retain frees it under the struct.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Round = int(0 to 1000)

union M
	silent
	text(body String)
end 'M'

type Holder
	export var m as M

	static function create(m M) returns Self
		return Self{m: m}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(M.text("a field-held payload string, long enough to be a real heap allocation"))
	var i = 0 as Round
	var n = 0 as Integer
	while i < 200 'spin'
		match h.m 'k'
			silent then n = n + 1
			text(s) then n = n + s.byteLength() as Integer
		end 'k'
		i = i + 1
	end 'spin'
	print("n={n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=13800
```

<!-- test: union-struct-payload-ranged-alias -->
The `create` parameter is a RANGED-INT-ALIAS, which adds a name to this file's
interner and shifts its ids relative to the signatures interner's. The union's
`BoxA` payload type is minted in the signatures interner; classifying it against
the file interner without re-interning let the shift misread it as `int` — a
wrong `E3005` reject at construct, and (once the construct is allowed) a
misrouted scope-exit drop that leaks the payload. The payload type is now
ADOPTED into the file interner before it is classified (the `fieldTypeOf`/
`adoptType` door the struct side already used), and the drop callee is chosen
inside `ProgramSignatures` over its own interner, so id and interner always
agree (OPEN #52).
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String

	static function create(x Integer) returns Self
		return Self{label: "v{x}"}
	end 'create'
end 'BoxA'

union UHold
	holds(inner BoxA)
end 'UHold'

function main() returns ExitCode
	let u = UHold.holds(BoxA.create(1))
	print("{u}")
	return 4
end 'main'
```
```exitcode
4
```
```stdout
holds
```

<!-- test: union-struct-payload-ranged-alias-match -->
The match-bind path shares the construct path's interner mismatch: with the
ranged-int alias present, the bound payload's `BoxA` type (a signatures id) was
classified against the file interner, so the managed payload could be misread as
a scalar and never moved out — a leak. Binding the payload now adopts its type
first, so the move-out and the scope-exit drop agree on what the payload is
(OPEN #52).
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String

	static function create(x Integer) returns Self
		return Self{label: "v{x}"}
	end 'create'
end 'BoxA'

union UHold
	holds(inner BoxA)
end 'UHold'

function main() returns ExitCode
	let u = UHold.holds(BoxA.create(1))
	match u 'm'
		holds(inner) then return 6
	end 'm'
end 'main'
```
```exitcode
6
```

<!-- test: labeled-payload-args-two-scalar-fields -->
A two-field case constructed with a labelled second argument
(`pair(7, column: 13)`) follows the call-argument rule — the first argument is
positional, the second is named — and slots each value against the payload field
its label names. So `line` carries 7 and `column` carries 13, and the match sums
them to 20. The construct is layout-parallel, not source-parallel: the label is
the destination, exactly as a struct literal's field name is.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, column: 13)
	match e 'k'
		none then return 0
		pair(line, column) then return (line + column)
	end 'k'
end 'main'
```
```exitcode
20
```

<!-- test: error.payload-second-arg-positional -->
The payload argument list follows the call-argument rule: the second and later
arguments must be named. A bare second argument (`pair(7, 13)`) is rejected — the
same `consumeArgLabel` rule a call obeys.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, 13)
	return 0
end 'main'
```
```maxoncstderr
error E2053: <fragment>:10:25: the second and later arguments must be named ('name: value')
```

<!-- test: error.payload-first-arg-named -->
The FIRST payload argument must be positional. A labelled first argument
(`pair(line: 7, column: 13)`) is rejected — a case's payload is ordered by its
declaration, so the first slot needs no name to find its place.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(line: 7, column: 13)
	return 0
end 'main'
```
```maxoncstderr
error E2052: <fragment>:10:22: the first argument cannot be named; only the second and later arguments take 'name:' labels
```

<!-- test: error.payload-unknown-label -->
A named payload argument whose label names no payload field of the case
(`pair(7, bogus: 9)`) is rejected as an unknown field.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, bogus: 9)
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:10:25: type 'LexErr' has no field named 'bogus'
```

<!-- test: error.payload-duplicate-label -->
Filling the same payload slot twice — here `column` positionally-then-by-name is
avoided, so a genuine repeat (`pair(7, column: 13, column: 14)`) is rejected as a
duplicate.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7, column: 13, column: 14)
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:10:37: field 'column' of 'LexErr' is initialized twice by this literal
```

<!-- test: error.payload-missing-arg -->
Every declared payload slot must be given a value; a union payload has no
defaults. Omitting `column` (`pair(7)`) is rejected as an uninitialized field.
```maxon
typealias Code = int(0 to 100)

union LexErr
	none
	pair(line Code, column Code)
end 'LexErr'

function main() returns ExitCode
	let e = LexErr.pair(7)
	return 0
end 'main'
```
```maxoncstderr
error E3086: <fragment>:10:17: field 'column' of 'LexErr' is not initialized by this literal, and it has no default value
```

<!-- test: error.recursive-union-payload-is-still-a-cycle -->
A union payload may be another payload-bearing (boxed) union — that payload is a
heap pointer whose box carries its own destructor, so nesting costs no codegen at
any depth. What a payload may NOT be is a reference back to the type being
declared: the type graph must be acyclic, and boxing is not an exception to that.
`union Tree { node(left Tree) }` names itself, so the CYCLE guard refuses it, and
it refuses it BEFORE the payload path ever classifies the slot — which is what
keeps a legal nested union from legalizing a recursive one. The body CONSTRUCTS
the recursive case, so the payload path would genuinely be reached: a pin whose
body were `Tree.leaf` would earn the same E4014 off the declaration alone and
prove nothing about the ordering this prose claims.
```maxon
union Tree
	node(left Tree)
	leaf
end 'Tree'

function main() returns ExitCode
	let t = Tree.node(Tree.leaf)
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:2:7: type 'Tree' contains a reference cycle (via Tree → node.left: Tree); recursive type references are not allowed
```

<!-- test: error.wrong-union-in-a-nested-union-payload -->
A nested boxed-union payload slot admits its OWN union and no other. The check is
the shared managed door (`requireManagedValueMatches`) over the slot's `named(<union>)`
IDENTITY, and its second half is what does the work here: a boxed union's value carries
the `named` tag that every payload-free enum and every ranged-int alias also carries, so
the tag comparison alone AGREES for any two of them and only the interned NAME separates
them (`requireSlotAggregateIdentity`). Unchecked, an `Other` box would be stored in an
`Inner` slot and later released by `Inner`'s destructor. Anchored at the ARGUMENT, not at
the case name. ⭐ The bootstrap oracle answers this program character for character,
including the position.
```maxon
typealias Integer = int(i64.min to i64.max)

union Inner
	empty
	a(x Integer)
end 'Inner'

union Other
	none
	b(y Integer)
end 'Other'

union Wrap
	bare
	wrap(inner Inner)
end 'Wrap'

function main() returns ExitCode
	let w = Wrap.wrap(Other.b(5))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:30: type mismatch: 'expected Inner, got Other'
```

<!-- test: nested-union-payload-write-back -->
A nested boxed-union payload bound out of a `var` union is WRITABLE, exactly as a String or a struct
payload is (`payloadBindingAcceptsWrites`) — the two facts a write-back needs are both statically
available for it: the move-out nulled the slot, so there is no previous owner to release, and the box's
own reference is the `__mm_incref` the write-back emits. The displaced `Ty` box is dropped when the
binding it moved into leaves the arm, the replacement is released by `__destruct_Expr` → `__destruct_Ty`
→ `__str_decref` at scope exit, and the second `match` reads the value the FIRST one stored — so the
exit code is the replacement's byte length (43) and not the original's (66). Every one of those
allocations is a real heap record, so a missed drop or a double release would be exit 101 rather than a
wrong number.
```maxon
typealias Integer = int(i64.min to i64.max)

union Ty
	concrete(name String)
	stringy
end 'Ty'

union Expr
	direct(ty Ty)
	unresolved
end 'Expr'

function tyLen(ty Ty) returns Integer
	return match ty 'l'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'l'
end 'tyLen'

function main() returns ExitCode
	var e = Expr.direct(Ty.concrete("the first nested payload, long enough to be a real heap allocation"))

	match e 'write'
		direct(ty) then ty = Ty.concrete("the second one, also a real heap allocation")
		unresolved then print("none")
	end 'write'

	return match e 'read'
		direct(ty) gives tyLen(ty)
		unresolved gives 99
	end 'read'
end 'main'
```
```exitcode
43
```

<!-- test: error.write-back-through-a-retained-nested-union-payload -->
⭐ **THE EDGE OF THE CASE ABOVE, AND THE REFUSAL IS THE STANDING RULE RATHER THAN THIS PAYLOAD KIND'S.**
Writability is decided from MEMBERSHIP (`scrutMutable` — is the scrutinee a `var`?) and the acquisition
from PROVENANCE (`scrutOwned` — does the value carry an owned-heap bit?), and the two disagree on a
loop-carried `var`: its current value inside the loop is a header phi, and a phi carries no provenance
bit, so the payload is acquired by RETAIN and the slot is left occupied. A write-back into an occupied
slot would strand the reference the box still holds, so `declarePayloadBindings` demotes such a binding
to read-only and the assignment is E2013 — never the `writeBackPayload` panic that guards the same
combination one level down.
```maxon
union Ty
	concrete(name String)
	stringy
end 'Ty'

union Expr
	direct(ty Ty)
	unresolved
end 'Expr'

function main() returns ExitCode
	var e = Expr.direct(Ty.concrete("the first nested payload, long enough to be a real heap allocation"))
	var total = 0

	for i in 1 to 2 'round'
		total = total + i
		match e 'write'
			direct(ty) then ty = Ty.concrete("a replacement, also a real heap allocation")
			unresolved then total = total + 1
		end 'write'
		e = Expr.direct(Ty.concrete("the next round's payload, a real heap allocation too"))
	end 'round'

	return total
end 'main'
```
```maxoncstderr
error E2013: <fragment>:19:20: cannot assign to immutable variable: 'ty'
```

<!-- test: error.write-back-through-a-retained-string-payload-is-the-same-refusal -->
⭐⭐ **THE CONTROL FOR THE CASE ABOVE, AND IT IS WHAT MAKES THAT REFUSAL ATTRIBUTABLE.** The identical
shape over a `String` payload — a payload kind that has been writable since `mutable-enums.md` shipped —
earns the identical E2013 at the identical position. So the refusal belongs to the retain acquisition and
not to the nested-union payload kind, and the rung that made a nested boxed union writable neither
introduced it nor is free to remove it. ⚠ Both programs are a DIVERGENCE the bootstrap does not share: it
borrows-and-retains where this tier consumes, so it accepts both and returns 3. Pinned here as shv2's own
ownership rule, which is what this file is for.
```maxon
union Expr
	direct(s String)
	unresolved
end 'Expr'

function main() returns ExitCode
	var e = Expr.direct("the first payload, long enough to be a real heap allocation")
	var total = 0

	for i in 1 to 2 'round'
		total = total + i
		match e 'write'
			direct(s) then s = "a replacement, also a real heap allocation"
			unresolved then total = total + 1
		end 'write'
		e = Expr.direct("the next round's payload, a real heap allocation too")
	end 'round'

	return total
end 'main'
```
```maxoncstderr
error E2013: <fragment>:14:19: cannot assign to immutable variable: 's'
```

<!-- test: co-owned-container-field-bind-then-the-field-is-read-again -->
⭐⭐ **CO-OWNERSHIP IS THE THIRD STATE, AND A MOVE-OUT OF A CO-OWNED BOX IS A THEFT.** `let borrowed = h.ty`
reads a union out of a MUTABLE struct field, and a value read out of a rebindable slot is promoted by
`__mm_retain` (W41) — so `borrowed` carries the owned-heap bit while `h.ty` still points at the SAME box.
The move-out reads that bit and nulls a slot the struct's field is the other owner of; `tyLen(h.ty)` then
loads a null payload and dereferences it. Neither union here is nested and neither is a parameter, so this
is the generic shape: the acquisition question is *"is this frame the box's SOLE owner?"*, which is not
*"does this value own a reference?"*. A co-owned scrutinee takes the RETAIN path, leaves the slot intact,
and both reads answer 67.
```maxon
typealias Integer = int(i64.min to i64.max)

union Ty
	concrete(name String)
	stringy
end 'Ty'

type Holder
	export var ty as Ty

	static function create(ty Ty) returns Self
		return Self{ty: ty}
	end 'create'
end 'Holder'

function tyLen(ty Ty) returns Integer
	return match ty 'l'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'l'
end 'tyLen'

function main() returns ExitCode
	let h = Holder.create(Ty.concrete("a co-owned payload string, long enough to be a real heap allocation"))
	let borrowed = h.ty
	let first = match borrowed 'b'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'b'
	let second = tyLen(h.ty)
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=67 second=67
```

<!-- test: co-owned-nested-payload-bound-inside-a-borrowed-union-s-arm -->
⭐ **THE SAME RULE ONE LEVEL DOWN, and the route the nested-union payload opens.** `steal(e Expr)` binds
`ty` out of a BORROWED parameter, so the outer bind is a retain and `ty` is co-owned with the caller's
box. The INLINE nested `match ty` then reads `ty`'s owned-heap bit and moves the String out of a `Ty` box
the caller still reaches through `e`, so the caller's own re-match loads a nulled slot. The cure is the one
above and not a nested-union special case: a retained payload is co-owned, so the nested match retains too
and the refcount balances at one free per allocation (a second free or a leak is exit 101, not a wrong
number).
```maxon
typealias Integer = int(i64.min to i64.max)

union Ty
	concrete(name String)
	stringy
end 'Ty'

union Expr
	direct(ty Ty)
	unresolved
end 'Expr'

function steal(e Expr) returns Integer
	return match e 'i'
		direct(ty) gives match ty 't'
			concrete(name) gives name.byteLength() as Integer
			stringy gives 0
		end 't'
		unresolved gives 99
	end 'i'
end 'steal'

function main() returns ExitCode
	let e = Expr.direct(Ty.concrete("a nested payload string, long enough to be a real heap allocation"))
	let stolen = steal(e)
	let again = match e 'k'
		direct(ty) gives match ty 'u'
			concrete(name) gives name.byteLength() as Integer
			stringy gives 0
		end 'u'
		unresolved gives 99
	end 'k'
	print("stolen={stolen} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stolen=65 again=65
```

<!-- test: the-same-nested-payload-handed-to-a-helper-instead -->
⭐⭐ **THE CONTROL FOR THE CASE ABOVE, AND THE CONTRAST IS THE DIAGNOSIS.** The identical program with the
inline nested match replaced by a HELPER CALL passed the whole time: `tyLen(ty)` hands the co-owned `Ty`
box to a callee whose own parameter is borrowed, so the callee retains and the destructive move-out is
never reached. So the defect belonged to the ACQUISITION the inline nested match chose and never to the
nesting, the payload kind, or the depth — which is why the cure is a property of the scrutinee's ownership
and not a rule about nested unions. Pinned so that a future acquisition change cannot fix one spelling and
leave the other.
```maxon
typealias Integer = int(i64.min to i64.max)

union Ty
	concrete(name String)
	stringy
end 'Ty'

union Expr
	direct(ty Ty)
	unresolved
end 'Expr'

function tyLen(ty Ty) returns Integer
	return match ty 'l'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'l'
end 'tyLen'

function steal(e Expr) returns Integer
	return match e 'i'
		direct(ty) gives tyLen(ty)
		unresolved gives 99
	end 'i'
end 'steal'

function main() returns ExitCode
	let e = Expr.direct(Ty.concrete("a nested payload string, long enough to be a real heap allocation"))
	let stolen = steal(e)
	let again = match e 'k'
		direct(ty) gives tyLen(ty)
		unresolved gives 99
	end 'k'
	print("stolen={stolen} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stolen=65 again=65
```

<!-- test: co-owned-array-element-bind-then-the-element-is-read-again -->
⭐ **THE CONTAINER ROUTE, AND IT TURNS ON THE BINDING'S MUTABILITY RATHER THAN ON THE CONTAINER.** An array
element read is a BORROW the array keeps (`emitContainerElementAccessor(owned: false)`), so `let e = …get(0)`
is not owned and matches by retain — which is why the `let` spelling of this program was never broken. A `var`
binding promotes its borrowed initializer by `__mm_retain` at the declaration, and THAT reference is co-owned
with the array's slot: the move-out nulled a payload slot `arr.get(0)` reads again. One `var` keyword apart,
and only one of the two faulted, which is what makes the rule a fact about the ACQUISITION and not about
containers.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias TyArray = Array with Ty

union Ty
	concrete(name String)
	stringy
end 'Ty'

function tyLen(ty Ty) returns Integer
	return match ty 'l'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'l'
end 'tyLen'

function main() returns ExitCode
	var arr = TyArray.create()
	arr.push(Ty.concrete("an array element payload, long enough to be a real heap allocation"))
	var borrowed = try arr.get(0) otherwise panic("get failed")
	let first = match borrowed 'b'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'b'
	let second = tyLen(try arr.get(0) otherwise panic("get failed"))
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=66 second=66
```

<!-- test: a-thrown-field-of-a-co-owned-container-is-retained-not-moved-out -->
⭐ **THE SAME DEFECT IN THE THROW CHANNEL, whose move-out asked the same too-weak question.** `throw c.e` out
of a container the frame merely CO-OWNS (`let c = g.inner`, a read out of a mutable field the promotion
retained) nulled the field slot `g.inner.e` still points at, so the caller's `whyLen(g.inner.e)` dereferenced
a hole. `moveOutThrownField` now gates on sole ownership exactly as the match's move-out does, and a co-owned
container takes `retainThrownField` — the box is increfed, the caught reference is consumed by the handler,
the container drops its own, and the refcount balances at one free (a second free or a leak would be exit 101
rather than a wrong number).
```maxon
typealias Integer = int(i64.min to i64.max)

union Err
	bad(why String)
end 'Err'

type Inner
	export var e as Err

	static function create(e Err) returns Self
		return Self{e: e}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function boom(g Outer) returns Integer throws Err
	let c = g.inner
	throw c.e
end 'boom'

function whyLen(e Err) returns Integer
	return match e 'l'
		bad(why) gives why.byteLength() as Integer
	end 'l'
end 'whyLen'

function main() returns ExitCode
	let g = Outer.create(Inner.create(Err.bad("a thrown field payload, long enough to be a real heap allocation")))
	let first = try boom(g) otherwise 7
	let second = whyLen(g.inner.e)
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=7 second=64
```

<!-- test: a-returned-co-owned-union-is-not-moved-out-of -->
⭐⭐ **A CALL'S OWNED RESULT IS A CO-OWNER, BECAUSE A `return` PROMOTES A BORROW BY INCREF.** `getTy`'s body is
`return h.ty`, which `emitOwnedValueReturn` promotes through `__mm_retain` — so the caller's `t` and the
receiver's field are two owners of one box, and nothing in `getTy`'s signature says so. Matching `t` and moving
its payload out therefore stole `h.ty`'s slot. Telling a fresh-box return from a co-owning one is a
whole-program fact shv2 does not compute, so a call result retains; the cost is one refcount pair and the
answer is right on both reads.
```maxon
typealias Integer = int(i64.min to i64.max)

union Ty
	concrete(name String)
	stringy
end 'Ty'

type Holder
	export var ty as Ty

	static function create(ty Ty) returns Self
		return Self{ty: ty}
	end 'create'
end 'Holder'

function getTy(h Holder) returns Ty
	return h.ty
end 'getTy'

function tyLen(ty Ty) returns Integer
	return match ty 'l'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'l'
end 'tyLen'

function main() returns ExitCode
	let h = Holder.create(Ty.concrete("a returned co-owned payload, long enough for the heap"))
	let t = getTy(h)
	let first = match t 'b'
		concrete(name) gives name.byteLength() as Integer
		stringy gives 0
	end 'b'
	let second = tyLen(h.ty)
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=53 second=53
```

<!-- test: a-caught-retained-error-box-is-not-moved-out-of -->
⭐ **AND ACROSS THE ERROR CHANNEL, where the transfer the thrower chose is invisible to the catcher.** `throw
h.e` out of a BORROWED container retains (`retainThrownField`, #64), so the box reaching the handler is
co-owned with `h.e` — but the flag register carries only a pointer, and the catching frame has no way to ask
which of the two transfers produced it. So a caught box is a co-owner and `match e` in the handler retains its
payload; claiming otherwise nulled a slot `whyLen(h.e)` read one line later.
```maxon
typealias Integer = int(i64.min to i64.max)

union Err
	bad(why String)
end 'Err'

type Holder
	export var e as Err

	static function create(e Err) returns Self
		return Self{e: e}
	end 'create'
end 'Holder'

function boom(h Holder) returns Integer throws Err
	throw h.e
end 'boom'

function whyLen(e Err) returns Integer
	return match e 'l'
		bad(why) gives why.byteLength() as Integer
	end 'l'
end 'whyLen'

function main() returns ExitCode
	let h = Holder.create(Err.bad("a caught retained payload, long enough for the heap"))
	var first = 0 as Integer
	try boom(h) otherwise (e) 'caught'
		first = match e 'm'
			bad(why) gives why.byteLength() as Integer
		end 'm'
	end 'caught'
	let second = whyLen(h.e)
	print("first={first} second={second}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first=51 second=51
```

<!-- test: a-co-owned-payload-moved-out-of-a-sole-box-is-still-co-owned -->
⛔⛔ **A SOLE BOX MAY HOLD A CO-OWNED PAYLOAD, AND THE MOVE-OUT MUST NOT LAUNDER THE ONE FACT INTO THE
OTHER.** `Wrap.held(inner)` over a BORROWED `inner` does not move the box in — `moveManagedValueInto`'s
borrowed arm co-owns it by `__mm_incref` — so `w` is genuinely the frame's alone while the `Inner` box in its
slot has two owners. Nulling `w`'s slot proves the frame now holds *that slot's* reference; it proves nothing
about the allocation, so stamping the moved-out payload SOLE re-arms exactly the destructive write this file
exists to disarm, one level down: the INLINE nested `match i` then nulls the CALLER's `Inner` slot and the
caller's own re-match dereferences the hole. A moved-out payload is therefore CO-OWNED — the box's soleness is
a fact about the box and is not inherited by what the box points at.
```maxon
typealias Integer = int(i64.min to i64.max)

union Inner
	text(body String)
end 'Inner'

union Wrap
	held(i Inner)
end 'Wrap'

function probe(inner Inner) returns Integer
	let w = Wrap.held(inner)
	return match w 'o'
		held(i) gives match i 'n'
			text(body) gives body.byteLength() as Integer
		end 'n'
	end 'o'
end 'probe'

function main() returns ExitCode
	let inner = Inner.text("a co-owned payload inside a sole box, long enough for the heap")
	let n = probe(inner)
	let again = match inner 'k'
		text(body) gives body.byteLength() as Integer
	end 'k'
	print("n={n} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=62 again=62
```

<!-- test: a-co-owned-payload-out-of-a-sole-box-through-a-helper -->
⭐ **CONTROL ONE FOR THE CASE ABOVE.** The identical program with the inline nested match replaced by a HELPER
CALL passed throughout: the callee's parameter is borrowed, so it retains and never reaches the destructive
write. Pinned so a future acquisition change cannot fix one spelling and leave the other — the same pairing
`the-same-nested-payload-handed-to-a-helper-instead` makes for the outer level.
```maxon
typealias Integer = int(i64.min to i64.max)

union Inner
	text(body String)
end 'Inner'

union Wrap
	held(i Inner)
end 'Wrap'

function innerLen(i Inner) returns Integer
	return match i 'n'
		text(body) gives body.byteLength() as Integer
	end 'n'
end 'innerLen'

function probe(inner Inner) returns Integer
	let w = Wrap.held(inner)
	return match w 'o'
		held(i) gives innerLen(i)
	end 'o'
end 'probe'

function main() returns ExitCode
	let inner = Inner.text("a co-owned payload inside a sole box, long enough for the heap")
	let n = probe(inner)
	let again = match inner 'k'
		text(body) gives body.byteLength() as Integer
	end 'k'
	print("n={n} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=62 again=62
```

<!-- test: a-freshly-built-payload-in-a-sole-box-is-genuinely-sole -->
⭐⭐ **CONTROL TWO, AND IT IS THE ONE THAT ISOLATES THE DISCRIMINATOR TO MOVE-IN VS CO-OWN-IN.** The same
sole box and the same inline nested match, but the payload is CONSTRUCTED at the construct site — so it is
moved in, the frame really is the allocation's only owner, and the program was correct before this rule and
stays correct after it. Its answer therefore attributes the fault above to the co-own-in and to nothing about
nesting, depth, or the inline spelling. It also pins the cost of the cure: this shape gains a refcount pair
it does not need, which is the price of a box's soleness not being transitive.

⛔ **READ THE NAME AS A CLAIM ABOUT THE PROGRAM, NEVER ABOUT THE EMITTED CODE.** The payload here *is*
genuinely the allocation's only reference — that is why the case is named so — but the compiler deliberately
**no longer CLAIMS `sole` for it**, because proving it would need a per-box "every payload was moved in" bit
and that would be a third ownership state (W55 declined it; see `OwnedHeapExclusivity`). So the `__mm_incref`
in this case's golden is CORRECT AND OWED, not a missed optimization. A future reader who takes the title as a
codegen claim and removes the retain re-arms the destructive write two cases above — which is exactly the
inference this rung retired, arriving through a test name instead of through a comment.
```maxon
typealias Integer = int(i64.min to i64.max)

union Inner
	text(body String)
end 'Inner'

union Wrap
	held(i Inner)
end 'Wrap'

function probe() returns Integer
	let w = Wrap.held(Inner.text("a freshly built payload inside a sole box, heap-long"))
	return match w 'o'
		held(i) gives match i 'n'
			text(body) gives body.byteLength() as Integer
		end 'n'
	end 'o'
end 'probe'

function main() returns ExitCode
	print("n={probe()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=52
```

<!-- test: a-list-element-removed-from-its-container-may-still-be-co-owned -->
⛔ **REMOVING AN ELEMENT PROVES THE CONTAINER NO LONGER HOLDS IT — NOT THAT NOBODY DOES.** `list.append(ty)`
over a BORROWED `ty` co-owns the box through the same `moveManagedValueInto` arm the construct above takes, so
`removeFirst()` hands back a reference the CALLER still shares. The compiler-emitted element accessor stamped
that result SOLE, and matching it nulled the caller's `Ty` slot. The removal is not the question the move-out
asks.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias TyList = List with Ty

union Ty
	concrete(name String)
end 'Ty'

function steal(ty Ty) returns Integer
	var list = TyList.create()
	list.append(ty)
	let got = try list.removeFirst() otherwise panic("removeFirst failed")
	return match got 'm'
		concrete(name) gives name.byteLength() as Integer
	end 'm'
end 'steal'

function main() returns ExitCode
	let ty = Ty.concrete("a list element payload co-owned by its caller, heap-long")
	let n = steal(ty)
	let again = match ty 'k'
		concrete(name) gives name.byteLength() as Integer
	end 'k'
	print("n={n} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=56 again=56
```

<!-- test: the-array-spelling-of-a-removed-element-was-never-wrong -->
⭐⭐ **THE CONTROL THAT NAMES WHICH DOOR LIED.** `Array.remove` is a CORPUS member, so its result arrives
through `enrolOwnedCallTemp` — the user-call door, which is co-owned because a `return` may launder a borrow —
and this program was correct the whole time. `List.removeFirst` is COMPILER-EMITTED and went through
`emitContainerElementAccessor`'s `owned` arm, which claimed sole. Two spellings of one operation disagreeing is
what says the defect belonged to the door and not to removal; both now answer the same way.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias TyArray = Array with Ty

union Ty
	concrete(name String)
end 'Ty'

function steal(ty Ty) returns Integer
	var arr = TyArray.create()
	arr.push(ty)
	let got = try arr.remove(0) otherwise panic("remove failed")
	return match got 'm'
		concrete(name) gives name.byteLength() as Integer
	end 'm'
end 'steal'

function main() returns ExitCode
	let ty = Ty.concrete("a list element payload co-owned by its caller, heap-long")
	let n = steal(ty)
	let again = match ty 'k'
		concrete(name) gives name.byteLength() as Integer
	end 'k'
	print("n={n} again={again}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=56 again=56
```
