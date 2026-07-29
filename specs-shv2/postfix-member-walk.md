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
error E2015: <fragment>:14:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the builtin types (`String`, `Character`, `Array`, `Set`, `StringIndex`, `CharacterSet`) carry members here
```
