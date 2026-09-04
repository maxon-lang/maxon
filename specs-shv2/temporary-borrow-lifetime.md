---
feature: temporary-borrow-lifetime
status: stable
keywords: [ownership, borrow, temporary, lifetime, array, element, field, use-after-free]
category: type-system
---

# A temporary that yields a borrow outlives the borrow

## Documentation

An expression whose result is bound to no name is a **temporary**: the statement that built it owns
it, and the statement's end is where it is freed. That is correct for a value nothing else points
into — and WRONG the moment the statement takes a **borrow** out of it, because a borrow is a
pointer into heap the temporary owns:

```text
typealias Bytes = Array with ByteArray
let n = try make().get(0) otherwise ByteArray.create()   // `make()` is a temporary
print("{n.count()}")                                     // reads into the freed array
```

`get`/`first`/`last` hand back the element the container KEEPS (a borrow — the container's own
`__managed_decref` walk destroys it), and a managed field read hands back the field the box KEEPS. Freed
at the end of the statement, the container takes the borrowed element with it, and the next read is
of poisoned memory — measured as `0x3F3F3F3F3F3F3F3F`, `__mm_free`'s fill byte read back as a
`count()`. A **wrong answer**, with no crash, no refusal, and no leak: the free is legitimate, so
nothing in the compiler or the runtime has anything to report.

⚖ **USER RULING, 2026-08-01 — the temporary's LIFETIME is extended.** A temporary that yields a
borrow is promoted from statement-scoped to the **borrower's scope**: it becomes a nameless owned
binding of the innermost open scope frame, dropped exactly once at that frame's exit, on every path.
So `make().get(0)` answers **5** and reads as it looks. This matches the oracle, which accepts the
program and answers 5.

It was chosen over the two alternatives:

* **refusing the borrow** — a loud, cheap divergence that would reject natural code the reference
  compilers both compile;
* **retaining at the accessor** — which would make `get` mean two things depending on the
  receiver's PROVENANCE.

Both reference compilers extend the lifetime rather than retain. v1 does it as a per-value liveness
EXTENSION over SSA (`StdLiveness.maxon`'s "interior-borrow liveness extension (the UAF fix)":
`baseOf[result] = receiver`, and every read of the borrow counts as a read of its base). shv2 has no
liveness pass at drop-insertion time — it emits drops at parse, statically scoped — so it states the
same fact in its own vocabulary: **the base's drop moves from the statement to the scope.** That is
the conservative direction v1's own soundness note names: *"the worst case is a slightly-later
decref … never an early free."*

### What is promoted, and what is not

The promotion asks ONE question — *"is the value this borrow came out of owned by nothing but the
current statement?"* — and only an UNCONSUMED owned temporary answers yes. Everything else is
already correct and stays untouched:

| written | why |
|---|---|
| `make().pop()` / `make().remove(i)` | an owned MOVE-OUT: the runtime nulls the slot, so the element outlives the container by construction |
| `make().get(0)` on `Array with Integer` | a TRIVIAL element is COPIED out — a bare word, with no pointer to dangle |
| `let held = make()` then `held.get(0)` | a binding already has scope lifetime; a second enrolment would drop the one record twice |
| `Leaf.make(4).tally` | a SCALAR field is copied, not borrowed |

### Whose scope, exactly

The **innermost open scope frame** — which is the borrower's, because the binding that takes the
borrow is declared into that same frame in that same statement. So a promoted record dies at a loop
body's `end` rather than at the function's, and a `return`, a `throw` and a `break` each release it
on their own path, through the machinery that already drops every owned binding.

A borrow that leaves the frame is LAUNDERED by machinery that already existed, and the promotion is
what makes those launders happen while the source is still alive:

| where the borrow goes | who gives it a reference of its own |
|---|---|
| a `return` / a `throw` | `promoteBorrowedToOwned` — the hand-off door |
| a struct field / a union payload / a consuming argument | `moveManagedValueInto` / `transferConsumedArg` |
| a `var` binding | `declareInitializedBinding`'s borrowed-binding promotion |
| a `try … otherwise <owned>` | `finishValueTry`'s ok-edge incref |
| a `match` / ternary arm's `gives` | `settleArmGive` — **new**, and the one that was missing |

A FORK ARM is the one place the answer is not "the enclosing scope": a ternary arm, a `match` arm, a
short-circuit right-hand side and a `while` CONDITION each run on their own path or on every trip, so
a record held there is released at **that region's** exit instead — and the give leaving the region is
laundered first. Both halves are needed and each has its own case below: releasing at the enclosing
scope instead is a decref on a path that never built the record (the register allocator's *"a use
dominates its def"*) or a leak that grows with the trip count (exit **101**); releasing without the
launder is the original use-after-free one construct in.

## Tests

<!-- test: element-borrowed-out-of-a-temporary-array -->
⛔ **THE DEFECT.** `make()` builds an array of `ByteArray`, `get(0)` borrows the element, and the
array is bound to no name. Before the promotion this printed `4557430888798830399` —
`0x3F3F3F3F3F3F3F3F`, `__mm_free`'s poison byte filling a word.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().get(0) otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: first-borrowed-out-of-a-temporary-array -->
`first()` is the same borrow through a different arm, and it read the same poison. Every
borrow-yielding accessor funnels through one door, so one promotion covers all of them.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().first() otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: last-borrowed-out-of-a-temporary-array -->
`last()`, the third borrowing arm.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().last() otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
3
```

<!-- test: a-var-binding-of-a-borrow-out-of-a-temporary -->
A `var` reads the same poison a `let` does. The mutable-binding promotion
(`declareInitializedBinding`'s `promoteBorrowedBinding`) increfs the borrowed element — but the
incref lands AFTER the container's drop when the container is statement-scoped, so it increfs freed
memory. Extending the container's lifetime is what puts the two back in order.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	var n = try make().get(0) otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: a-diverging-otherwise-keeps-the-temporary-alive -->
The DIVERGING `otherwise` takes a different finish path (`finishTerminatedTry`, no merge phi and no
incref anywhere), so it is a separate reaching route to the same free — and it read the same poison.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function count() returns Wide
	let n = try make().get(0) otherwise return 0
	return n.count()
end 'count'

function main() returns ExitCode
	return count()
end 'main'
```
```exitcode
5
```

<!-- test: a-borrow-out-of-a-temporary-inside-a-ternary-arm -->
A ternary ARM builds its temporary on one path only, and the borrow escapes the arm through the
result phi. The promotion must still land the container's drop where the arm's own definition
reaches it.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let t = true
	let d = ByteArray.create()
	let n = (try make().get(0) otherwise d) if t else d
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: a-borrow-out-of-a-temporary-inside-a-loop-body -->
The borrower's scope is the LOOP BODY's frame, not the function's: the container must drop once per
iteration at the body's `end`, or a loop that runs a thousand times holds a thousand arrays. Read
back on the last iteration, so an early free is still the poison.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	var total = 0 as Wide
	for _ in 0 upto 4 'each'
		let n = try make().get(0) otherwise ByteArray.create()
		total = total + n.count()
	end 'each'
	return total
end 'main'
```
```exitcode
12
```

<!-- test: a-borrow-out-of-a-temporary-returned-out-of-the-frame -->
A `return` LAUNDERS a borrow into an owned value (`promoteBorrowedToOwned` — the aggregate arm
increfs). That incref has to run while the container is still alive, which it now does.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function borrowOut() returns ByteArray
	return try make().get(0) otherwise ByteArray.create()
end 'borrowOut'

function main() returns ExitCode
	let n = borrowOut()
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: two-borrows-out-of-two-temporaries-in-one-statement -->
Two temporaries in ONE statement, each yielding its own borrow: both are promoted, and both drop
exactly once. A promotion that keyed on anything but the value's own identity would drop one twice
and the other never.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Bytes = Array with ByteArray

function make(n Wide) returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	for _ in 0 upto n 'fill'
		inner.push(1)
	end 'fill'
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let a = try make(2).get(0) otherwise ByteArray.create()
	let b = try make(3).get(0) otherwise ByteArray.create()
	return a.count() + b.count()
end 'main'
```
```exitcode
5
```

<!-- test: the-same-temporary-yields-a-borrow-and-an-owned-value -->
One temporary, two answers in one statement: `count()` reads it and `get(0)` borrows out of it. The
promotion is MONOTONE — once a temporary has handed out a borrow it keeps scope lifetime, and the
scalar read alongside changes nothing.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().get(0) otherwise ByteArray.create()
	return n.count() + make().count()
end 'main'
```
```exitcode
6
```

<!-- test: a-managed-field-borrowed-out-of-a-temporary-box -->
A managed FIELD read is the other borrow door, and it is the SAME promotion: the box `makeHolder()`
built is held to the scope's exit, so the text `name` borrows is live when it is read. This program
used to be REFUSED (`E2015`), and the refusal's own sentence named this rung as the one that would
lift it.
```maxon
type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

function makeHolder() returns Holder
	return Holder.create("hello")
end 'makeHolder'

function main() returns ExitCode
	let s = makeHolder().name
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: every-match-arm-gives-a-borrow-out-of-its-own-temporary -->
⛔⛔ **NOTHING ELSE MAKES THE RESULT PHI OWNED HERE, SO NOTHING ELSE LAUNDERS THE ARMS.** Each arm
builds its own box and gives a borrow into it; each box dies at ITS arm's exit, on the edge that
built it. The give is therefore given a reference of its own at the arm's exit, BEFORE the arm's
records are released (`settleArmGive`) — measured at `0x3F3F3F3F3F3F3F3F` without it, both arms.
```maxon
typealias Wide = int(i64.min to i64.max)

type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

enum Pick
	first
	second
end 'Pick'

function makeA() returns Holder
	return Holder.create("aaaaa")
end 'makeA'

function makeB() returns Holder
	return Holder.create("bb")
end 'makeB'

function pickLength(p Pick) returns Wide
	let s = match p 'pick'
		first gives makeA().name
		second gives makeB().name
	end 'pick'
	return s.byteLength()
end 'pickLength'

function main() returns ExitCode
	return pickLength(Pick.first) * 10 + pickLength(Pick.second)
end 'main'
```
```exitcode
52
```

<!-- test: both-ternary-arms-give-a-borrow-out-of-their-own-temporary -->
The ternary twin of the case above, through the same door: a ternary arm is a fork arm, and until
this rung it was the one fork region with no owned-binding floor at all.
```maxon
typealias Wide = int(i64.min to i64.max)

type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

function makeA() returns Holder
	return Holder.create("aaaaa")
end 'makeA'

function makeB() returns Holder
	return Holder.create("bb")
end 'makeB'

function pickLength(t bool) returns Wide
	let s = makeA().name if t else makeB().name
	return s.byteLength()
end 'pickLength'

function main() returns ExitCode
	return pickLength(true) * 10 + pickLength(false)
end 'main'
```
```exitcode
52
```

<!-- test: a-borrow-out-of-a-temporary-inside-a-while-condition -->
⛔ **THE RISK INVERTS HERE, AND THE LEAK GATE IS WHAT CATCHES IT.** A `while` condition is
re-evaluated on every trip, so it builds a FRESH record each time while the promotion records ONE —
held to the frame's exit, that frees the last trip's record and leaks all the others. Measured as
exit **101**. The condition's records are therefore released per iteration, in the condition's own
exit block, exactly as its temporaries already were. 200 trips, so a per-trip leak is unmissable.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	var n = 0 as Wide
	while n < (try make().get(0) otherwise ByteArray.create()).count() * 100 'loop'
		n = n + 1
	end 'loop'
	return n / 100
end 'main'
```
```exitcode
3
```

<!-- test: a-borrow-out-of-a-temporary-inside-a-short-circuit-right-hand-side -->
The right-hand side of an `and`/`or` runs on ONE path, so a record held for a borrow taken inside it
must die there too — the frame's exit is a block the skipped path never reached. Measured, before
the rhs got its own floor, as the allocator's `seedInUse: a use dominates its def`.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let d = ByteArray.create()
	let ok = false or (try make().get(0) otherwise d).count() == 3
	return 7 if ok else 0
end 'main'
```
```exitcode
7
```

<!-- test: control-a-borrow-out-of-a-match-arms-payload-binding -->
**CONTROL.** A borrow out of an arm's PAYLOAD BINDING rather than out of a temporary. The binding's
box belongs to the scrutinee, which the `let` outlives, so the arm's exit releases nothing the give
points into — and the answer was already right. It is here because the settlement rule keys on the
arm's owned-binding tail, which a payload binding also sits in: the rule must not change what this
program means.
```maxon
type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

union Wrap
	held(h Holder)
	nothing
end 'Wrap'

function makeWrap() returns Wrap
	return Wrap.held(Holder.create("hello"))
end 'makeWrap'

function main() returns ExitCode
	let w = makeWrap()
	let s = match w 'k'
		held(h) gives h.name
		nothing gives "zz"
	end 'k'
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: control-an-arm-giving-a-read-of-an-immutable-global -->
⛔ **CONTROL, AND IT WAS RED FOR A WHILE: THE SETTLEMENT RULE MAY NOT LAUNDER A READ OF MODULE
STORAGE.** The arm holds a managed payload binding (`h`), which puts it above its own owned floor —
but the give is `A`, a `let`-declared top-level array, which outlives every scope and so cannot point
into anything the arm releases. Laundering it anyway sends it to the merge promotion, whose aggregate
arm exists precisely to REFUSE an incref of a record the language calls immutable: this program
COMPILED and answered 1 before the rung, and was refused with `E2015 … merging a read of a
`let`-declared top-level global into an OWNED result` after it. Refusing a legal program is a
regression whichever direction it points.
```maxon
typealias Wide = int(i64.min to i64.max)

let A = [1, 2, 3]
let B = [4, 5]

type Holder
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Holder'

union Wrap
	held(h Holder)
	nothing
end 'Wrap'

function makeWrap() returns Wrap
	return Wrap.held(Holder.create("hello"))
end 'makeWrap'

function pick() returns Wide
	let w = makeWrap()
	let xs = match w 'k'
		held(h) gives A if h.name.byteLength() > 0 else B
		nothing gives B
	end 'k'
	return try xs.get(0) otherwise 0
end 'pick'

function main() returns ExitCode
	return pick()
end 'main'
```
```exitcode
1
```

<!-- test: control-pop-out-of-a-temporary-is-an-owned-move-out -->
**CONTROL.** `pop()` MOVES the element out — the runtime nulls the slot, so the element outlives the
container by construction and the result is already OWNED. It was correct before this rung and must
stay correct: a promotion that fired here would keep a container alive for a value it no longer
holds.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().pop() otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
5
```

<!-- test: control-a-trivial-element-out-of-a-temporary-is-copied -->
**CONTROL.** A TRIVIAL element is a bare inline word COPIED out of the slot — there is no pointer to
dangle, and the accessor returns before any ownership question is asked. Correct before, correct
after.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Ints = Array with Wide

function make() returns Ints
	var outer = Ints.create()
	outer.push(41)
	return outer
end 'make'

function main() returns ExitCode
	let n = try make().get(0) otherwise 0
	return n
end 'main'
```
```exitcode
41
```

<!-- test: control-an-element-borrowed-out-of-a-bound-container -->
**CONTROL, and it is the half that keeps the promotion honest.** The identical read through a NAME
was already right: the binding owns the record and drops it at ITS scope exit. Enrolling it a second
time would drop the one record twice — which is why the gate is membership of the pending-temporary
list (the owned values NO binding owns) and not the owned-heap bit.
```maxon
typealias Bytes = Array with ByteArray

function make() returns Bytes
	var outer = Bytes.create()
	var inner = ByteArray.create()
	inner.push(1)
	inner.push(2)
	inner.push(3)
	inner.push(4)
	inner.push(5)
	outer.push(inner)
	return outer
end 'make'

function main() returns ExitCode
	let held = make()
	let n = try held.get(0) otherwise ByteArray.create()
	return n.count()
end 'main'
```
```exitcode
5
```
