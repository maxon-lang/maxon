---
feature: field-visibility-is-type-scoped
status: stable
keywords: field, visibility, export, E3014, type-scope, encapsulation
category: type-system
---
# Field Visibility Is Scoped To The TYPE, Not The FILE

## Documentation

An unexported field is reachable from its own type's methods and from nowhere else. **The gate is
the TYPE, not the FILE**: `type B` reading `type A`'s unexported field is `E3014` even when A and B
are declared in the same file, one line apart.

This file exists because that sentence is the one thing `specs/export-var-fields.md` cannot say.
Every case in the corpus file reaches the private field from `main()` — a free function, inside no
type at all. So the corpus pins *"outside **any** type ⇒ reject"* and leaves the distinguishing half
unpinned, and a compiler that answered *"am I inside **any** type?"* instead of *"am I inside **the
declaring** type?"* would let every method of every type read every other type's private state — and
pass the entire corpus.

**That is not hypothetical: it was MEASURED.** Replacing `namesEnclosingType(layout.name)` with
`not enclosingType.isEmpty()` in `Parser.requireFieldAccessible` leaves `specs-shv2` at **392/0**,
both `error.unexported-field-read` and `error.unexported-field-write` included. The rule was correct
and enforced by nothing — the shape `OPEN.md` #27 (`isPure`), #23 and #4c all wear: a property held
for a reason nobody had written down, while the reason everybody HAD written down checked nothing.
The two cases below are what make the sabotage go red.

Both are verified against the bootstrap oracle, which produces the identical code, position and text.

## Tests

<!-- test: error.other-type-cannot-read-private -->
<!-- targets: wasm32-wasi -->
`type B`'s method reads `type A`'s unexported field. Same file, different type. The diagnostic points
at the FIELD token — the token that is wrong — never at the value it is read from.
```maxon

typealias Integer = int(i64.min to i64.max)

type A
	var secret as Integer

	static function create() returns Self
		return A{secret: 0}
	end 'create'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return B{n: 0}
	end 'create'

	function peek() returns Integer
		let a = A.create()
		return a.secret
	end 'peek'
end 'B'

function main() returns ExitCode
	let b = B.create()
	return b.peek()
end 'main'
```
```maxoncstderr
error E3014: specs/fragments/field-visibility-is-type-scoped/error.other-type-cannot-read-private.test:22:12: cannot access unexported field: 'secret' outside of type 'A'
```

<!-- test: error.other-type-cannot-write-private -->
<!-- targets: wasm32-wasi -->
The WRITE half, pinned separately. `requireFieldAccessible` has one home and two callers, and this
is the second: a rule taught to the read site alone would let a private field be WRITTEN from another
type where reading it correctly reports. An accepted access that should have been refused is a wrong
answer, not a compile error.
```maxon

typealias Integer = int(i64.min to i64.max)

type A
	var secret as Integer

	static function create() returns Self
		return A{secret: 0}
	end 'create'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return B{n: 0}
	end 'create'

	function poke() returns Integer
		var a = A.create()
		a.secret = 42
		return 0
	end 'poke'
end 'B'

function main() returns ExitCode
	let b = B.create()
	return b.poke()
end 'main'
```
```maxoncstderr
error E3014: specs/fragments/field-visibility-is-type-scoped/error.other-type-cannot-write-private.test:22:5: cannot access unexported field: 'secret' outside of type 'A'
```

<!-- test: own-type-reaches-its-own-private-field -->
<!-- targets: wasm32-wasi -->
The positive companion, and the reason the gate cannot simply be "reject every access through a
binding": `type A`'s own method reaches A's unexported field through a LOCAL of its own type — not
through `self`. This is the access the self-field path never sees, so it is the one that proves the
rule admits what it should.
```maxon

typealias Integer = int(i64.min to i64.max)

type A
	var secret as Integer

	static function create(v Integer) returns Self
		return A{secret: v}
	end 'create'

	function sumWith() returns Integer
		let other = A.create(20)
		return secret + other.secret
	end 'sumWith'
end 'A'

function main() returns ExitCode
	let a = A.create(22)
	return a.sumWith()
end 'main'
```
```exitcode
42
```
