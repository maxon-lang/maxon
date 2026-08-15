---
feature: postfix-member-walk
status: stable
keywords: [postfix, member, method, receiver, chain, literal, call-result, field, parenthesized]
category: type-system
---

# A `.member` Applies To A VALUE, Not Only To A NAME

## Documentation

A `.method(…)` or `.field` is applied to whatever the expression to its left evaluated to. The
receiver may be a binding (`s.byteLength()`), but it may equally be a **literal**
(`"abc".byteLength()`), a **call result** (`Inner.make(9).get()`), a **parenthesised expression**
(`(i).get()`), a **field read** (`o.inner.get()`), or a previous hop of the same chain
(`Leaf.make(1).bump().bump().size()`).

Every one of those is the same question — *what type is this value, and what does that type call
this member?* — so the parser answers it in exactly one place (`Parser.dispatchMethodOnReceiver`),
reached both by the binding door (`dispatchMethodOnBinding`, which additionally owns the
live-binding guard, the self-field materialization, the E3019 blame name and the E3070 subject) and
by the postfix loop.

⚠ **Before this, the two were separate chains and the difference between them was not a design.** It
was whichever receiver types someone had happened to need from a value: `[1,2].get(0)` worked and
`"ab".byteLength()` did not; `a.get().compare(b)` worked and `Inner.make(9).get()` did not. Anything
with no arm fell out of the postfix loop with the cursor still sitting on the `.`, which then
reached the statement dispatcher's catch-all and printed the nonsensical
`E2015: Unsupported: . statement`.

A chain of FIELDS still walks as a chain (`o.inner.x`); the walk stops one hop short of a
`.member(`, so the receiver of the call is an ordinary value and the same dispatcher decides what
the member is.

Every exit code below was measured against the C# bootstrap first.

## Tests

<!-- test: postfix.method-on-a-string-literal -->
A method on a `String` LITERAL. The receiver is bound to no name, so nothing can be blamed for a
write through it and nothing needs a live-binding check — which is exactly why the String dispatcher
could not take a `VarInfo`.
```maxon
function main() returns ExitCode
	return "abc".byteLength()
end 'main'
```
```exitcode
3
```

<!-- test: postfix.method-on-a-call-result -->
A method on a STRUCT a call just returned. `a.get().compare(b.get())` already worked because an
`Integer` result rides the builtin-conformer arm; a struct result had no arm at all.
```maxon
typealias Wide = int(i64.min to i64.max)

type Inner
	export var v as Wide

	export static function make(v Wide) returns Self
		return Self{v: v}
	end 'make'

	export function get() returns Wide
		return self.v
	end 'get'
end 'Inner'

function main() returns ExitCode
	return Inner.make(9).get()
end 'main'
```
```exitcode
9
```

<!-- test: postfix.method-on-a-parenthesized-receiver -->
The receiver is a PARENTHESISED expression. `(i)` yields the same value `i` does, but it arrives at
the `.` as a temporary rather than as a base token, so the binding door never sees it.
```maxon
typealias Wide = int(i64.min to i64.max)

type Inner
	export var v as Wide

	export static function make(v Wide) returns Self
		return Self{v: v}
	end 'make'

	export function get() returns Wide
		return self.v
	end 'get'
end 'Inner'

function main() returns ExitCode
	let i = Inner.make(5)
	return (i).get()
end 'main'
```
```exitcode
5
```

<!-- test: postfix.method-after-a-struct-field -->
A method whose receiver is a struct-typed FIELD — `o.inner.get()`. The chain walk used to consume
`get` as the chain's last FIELD and report `E3018 type 'Inner' has no field named 'get'`; it now
stops at `inner`, loads it, and the loaded value's own type decides what `get` is.
```maxon
typealias Wide = int(i64.min to i64.max)

type Inner
	export var v as Wide

	export static function make(v Wide) returns Self
		return Self{v: v}
	end 'make'

	export function get() returns Wide
		return self.v
	end 'get'
end 'Inner'

type Outer
	export var inner as Inner

	export static function make(inner Inner) returns Self
		return Self{inner: inner}
	end 'make'
end 'Outer'

function main() returns ExitCode
	let o = Outer.make(Inner.make(3))
	return o.inner.get()
end 'main'
```
```exitcode
3
```

<!-- test: postfix.field-read-after-a-struct-field-still-walks -->
The CONTROL for the case above: a member with no `(` after it is still a FIELD, so a pure read
chain is unchanged and reaches the field through the walk rather than through the dispatcher.
```maxon
typealias Wide = int(i64.min to i64.max)

type Inner
	export var v as Wide

	export static function make(v Wide) returns Self
		return Self{v: v}
	end 'make'
end 'Inner'

type Outer
	export var inner as Inner

	export static function make(inner Inner) returns Self
		return Self{inner: inner}
	end 'make'
end 'Outer'

function main() returns ExitCode
	let o = Outer.make(Inner.make(7))
	return o.inner.v
end 'main'
```
```exitcode
7
```

<!-- test: postfix.three-hop-chain-off-a-call-result -->
THREE hops off a call result, each one's receiver being the previous one's value. The loop must
re-examine the value it just produced rather than stopping after the first member.
```maxon
typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'

	export function size() returns Wide
		return self.tally
	end 'size'

	export function bump() returns Leaf
		return Leaf{tally: self.tally + 1}
	end 'bump'
end 'Leaf'

function main() returns ExitCode
	return Leaf.make(1).bump().bump().size()
end 'main'
```
```exitcode
3
```

<!-- test: postfix.field-read-on-a-call-result -->
A plain FIELD on a call result — the member the chain walk structurally cannot reach, because its
base is not a name. It is the same dispatcher arm the method call takes, split by the `(`.
```maxon
typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(tally Wide) returns Self
		return Self{tally: tally}
	end 'make'
end 'Leaf'

function main() returns ExitCode
	return Leaf.make(4).tally
end 'main'
```
```exitcode
4
```

<!-- test: postfix.method-on-a-set-call-result -->
A `Set` method on a `Set` a static call returned. The `Array` arm had a value form and the `Set` arm
did not, which is the whole shape of the gap this rung closes: the set is not a different question,
it just had not been asked from a value yet.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideSet = Set with Wide

function main() returns ExitCode
	return WideSet.create().count() + 11
end 'main'
```
```exitcode
11
```

<!-- test: postfix.string-method-on-a-string-result -->
A String method on a String another String method returned. `toUpper()` yields an OWNED record, so
this also pins that the intermediate temporary is dropped — a leak here is exit 101, not a wrong
answer.
```maxon
function main() returns ExitCode
	let s = "hello"
	return "ab".toUpper().byteLength() + s.clone().byteLength()
end 'main'
```
```exitcode
7
```

<!-- test: postfix.enum-method-on-a-call-result -->
D1 gave an `enum`/`union` receiver its methods off a BINDING. Routing the enum arm through the one
dispatcher gives it a VALUE receiver at no extra cost — the oracle accepts `pick().score()` and shv2
answered `E2015: Unsupported: . statement`.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Direction
	north
	south

	function score() returns Wide
		if self == Direction.north 'n'
			return 7
		end 'n'
		return 3
	end 'score'
end 'Direction'

function pick() returns Direction
	return Direction.north
end 'pick'

function main() returns ExitCode
	return pick().score()
end 'main'
```
```exitcode
7
```

<!-- test: postfix.enum-method-on-an-enum-literal -->
The same arm reached from an enum CASE reference, which is a value the parser builds and binds to
nothing at all.
```maxon
typealias Wide = int(i64.min to i64.max)

enum Direction
	north
	south

	function score() returns Wide
		if self == Direction.north 'n'
			return 7
		end 'n'
		return 3
	end 'score'
end 'Direction'

function main() returns ExitCode
	return Direction.south.score()
end 'main'
```
```exitcode
3
```

<!-- test: postfix.union-method-on-a-call-result -->
The enum arm is reached on the `named` tag plus an ENUM registration, and a `union` registers the
same way — so routing the arm through the one dispatcher gave a union receiver its value spellings at
the same moment it gave them to an enum. Every committed union-receiver case binds its receiver to a
name first (`let o = …; o.isPass()`), so the value door had no union witness at all until this case:
the whole of `enum-union-method-receiver.md` survives deleting `parsePostfix`'s member arm.
```maxon
typealias Wide = int(i64.min to i64.max)

union Shape
	dot
	box(w Wide)

	function area() returns Wide
		match self 'm'
			dot then return 1
			box(w) then return w
		end 'm'
	end 'area'
end 'Shape'

function pick() returns Shape
	return Shape.box(21)
end 'pick'

function main() returns ExitCode
	return pick().area() * 2
end 'main'
```
```exitcode
42
```

<!-- test: postfix.union-method-on-a-case-constructor-result -->
A union CASE CONSTRUCTOR is not an ordinary call, and a payload-less case is not a call at all — but
both yield a union value bound to no name, so both are receivers. Pinned in one program because they
are one question asked of two producers.
```maxon
typealias Wide = int(i64.min to i64.max)

union Shape
	dot
	box(w Wide)

	function area() returns Wide
		match self 'm'
			dot then return 1
			box(w) then return w
		end 'm'
	end 'area'
end 'Shape'

function main() returns ExitCode
	return Shape.box(21).area() + Shape.dot.area()
end 'main'
```
```exitcode
22
```

<!-- test: postfix.union-method-on-a-managed-temporary-in-a-loop -->
The receiver is a union temporary owning a HEAP payload, and no binding exists to hang its drop on.
A per-iteration leak here is exit **101**, not a wrong answer, which is why the loop is 200 rounds
rather than one — the answer is identical either way and only the leak count is not.
```maxon
union Outcome
	pass
	fail(reason String)

	export function isPass() returns bool
		return match self 'p'
			pass gives true
			fail gives false
		end 'p'
	end 'isPass'
end 'Outcome'

function main() returns ExitCode
	var i = 0

	while i < 200 'loop'
		if Outcome.fail("a rather long failure reason to force a heap allocation").isPass() 'y'
			return 1
		end 'y'
		i = i + 1
	end 'loop'

	return 7
end 'main'
```
```exitcode
7
```

<!-- test: error.member-on-a-value-with-no-members -->
A receiver whose type carries no members at all is a POSITIONED refusal naming the member and the
type — never the `. statement` catch-all that a `break`ing postfix loop used to leave behind. The
oracle refuses the same program at the same column (`E4006 Cannot access field on non-struct
value`, at the member token).
```maxon
typealias Wide = int(i64.min to i64.max)
typealias UnaryOp = function(Wide) returns Wide

function twice(v Wide) returns Wide
	return v * 2
end 'twice'

function pickFn() returns UnaryOp
	return twice
end 'pickFn'

function main() returns ExitCode
	return pickFn().x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the byte records (`String`, `Character`) carry members here
```

**A FIELD READ OFF A TEMPORARY — THE BORROW WHOSE OWNER DIES AT THE SEMICOLON.**

A field read is a **borrow**: `Parser.emitFieldLoad` never marks its result owned, so the box keeps
its `+1` and drops what the read points at **at the box's own scope exit**. Every field access in
the language rests on that, and it holds because a box is reached through a NAME that outlives the
read.

⚠ **A receiver bound to no name broke it, and this rung was the first door that could hand one over.**
`Box.make("hello")` was enrolled as a STATEMENT-scoped owned temporary; the statement's pending drops
freed it at the semicolon. A MANAGED field read out of it therefore handed back a pointer into freed
memory the moment the result outlived the statement. Measured, on every managed field kind — each of
them a program the C# oracle runs and prints:

| written | shv2 |
|---|---|
| `let s = Box.make("hello").name` (String field) | **0xC0000005**, no diagnostic |
| `let o = Box.make().ops` then `o.get(0)` (Array field) | **0xC0000005**, no diagnostic |
| `let i = Outer.make(Inner.make(3)).inner` (struct field) | exit **0x3F3F3F3F** — the freed-fill byte read back as user data: a **wrong answer with no crash** |
| `let s = makePair().1` (tuple element) | **0xC0000005**, no diagnostic |

⚖ **THE RULING CAME, AND IT WAS TO EXTEND THE TEMPORARY'S LIFETIME (A3h, 2026-08-01).** It used to
be **refused**, and the refusal's own message named the rung that would lift it — *"keeping a
temporary alive for a borrow taken out of it is the ownership rung's"*. That rung landed: the box is
now promoted to a nameless owned binding of the enclosing scope (`giveTemporaryScopeLifetime`), so it
is freed once at the frame's exit, after every read of what was borrowed out of it. The full argument,
and the array-element half of the same fact, is `temporary-borrow-lifetime.md`. A **SCALAR** field is
copied rather than borrowed, so `Leaf.make(4).tally` above needs none of this — the gate is the
managed classifier, not the temporary.

<!-- test: managed-field-read-out-of-a-temporary -->
A `String` field read out of a call result: the box lives to the scope's exit, so the text is live
when it is read.
```maxon

type Box
	export var name as String

	export static function make(n String) returns Self
		return Self{name: n}
	end 'make'
end 'Box'

function main() returns ExitCode
	let s = Box.make("hello").name
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: struct-field-read-out-of-a-temporary -->
⭐ **The one of the four that a suite of exit codes would never have caught**: a STRUCT field read
out of a temporary did not crash, it returned `0x3F3F3F3F` — the freed-memory fill byte — as the
program's answer. It answers **3** now, and a temporary freed early would still answer the poison.
```maxon

typealias Wide = int(i64.min to i64.max)

type Inner
	export var v as Wide

	export static function make(v Wide) returns Self
		return Self{v: v}
	end 'make'

	export function get() returns Wide
		return self.v
	end 'get'
end 'Inner'

type Outer
	export var inner as Inner

	export static function make(i Inner) returns Self
		return Self{inner: i}
	end 'make'
end 'Outer'

function main() returns ExitCode
	let i = Outer.make(Inner.make(3)).inner
	return i.get()
end 'main'
```
```exitcode
3
```

<!-- test: tuple-element-read-out-of-a-temporary -->
A TUPLE needs no arm of its own — it is a synthesized struct, so its positional member rides the
same layout, the same classifier and the same promotion.
```maxon

typealias Wide = int(i64.min to i64.max)
typealias Pair = (Wide, String)

function makePair() returns Pair
	return (7, "hello")
end 'makePair'

function main() returns ExitCode
	let s = makePair().1
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: managed-field-read-off-a-binding-is-unaffected -->
**The CONTROL, and it is the half that keeps the refusal honest.** The identical field read through
a NAME is the ordinary borrow it has always been: the binding owns the box, the box outlives the
read. A guard that refused this too would have "fixed" the crash by deleting the feature.
```maxon

type Box
	export var name as String

	export static function make(n String) returns Self
		return Self{name: n}
	end 'make'
end 'Box'

function main() returns ExitCode
	let b = Box.make("hello")
	let s = b.name
	return s.byteLength()
end 'main'
```
```exitcode
5
```

<!-- test: method-on-a-temporary-may-still-return-its-managed-field -->
The refusal is scoped to the READ, not to the temporary. A METHOD on the same temporary that reads
the same field is fine, because the callee borrows the receiver only for the duration of the call —
which ends before the statement does. Measured against the oracle; a leak here would be exit 101.
```maxon

type Box
	export var name as String

	export static function make(n String) returns Self
		return Self{name: n}
	end 'make'

	export function len() returns ExitCode
		return self.name.byteLength()
	end 'len'
end 'Box'

function main() returns ExitCode
	return Box.make("hello").len()
end 'main'
```
```exitcode
5
```

<!-- test: mutating-method-on-a-temporary -->
⭐ **A mutating method on a temporary is LEGAL, and that is the provenance argument paying out.**
The receiver is bound to no name, so no `let` promises anything about it and there is nothing for
E3019 to blame — the empty blame name is the correct answer rather than a missing one. The clone is
then dropped at statement end, so a leak here is exit 101 and the untouched `s` still reads 3.
```maxon

function main() returns ExitCode
	var s = "abc"
	s.clone().append("XYZ")
	return s.byteLength()
end 'main'
```
```exitcode
3
```

<!-- test: error.unknown-field-on-a-call-result -->
An unknown member with no `(` after it cannot be a method, so it is reported as the missing FIELD it
is. Left to the method path it reached `parseCallNamed` and complained about the PUNCTUATION —
`E2010: Expected '(' but got 'newline'` — while the BINDING spelling of the identical access
answered E3018. One question may not have two answers because of the door it came through.
```maxon

typealias Wide = int(i64.min to i64.max)

type Leaf
	export var tally as Wide

	export static function make(t Wide) returns Self
		return Self{tally: t}
	end 'make'
end 'Leaf'

function main() returns ExitCode
	return Leaf.make(4).nope
end 'main'
```
```maxoncstderr
error E3018: <fragment>:14:22: type 'Leaf' has no field named 'nope'
```
