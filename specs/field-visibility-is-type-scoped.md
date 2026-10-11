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

**That is not hypothetical.** Replacing `namesEnclosingType(layout.name)` with
`not enclosingType.isEmpty()` in `Parser.requireFieldAccessible` leaves every other case green,
both `error.unexported-field-read` and `error.unexported-field-write` included. The two cases below
are what make that sabotage go red.

## Tests

<!-- test: error.other-type-cannot-read-private -->
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
error E3014: specs/field-visibility-is-type-scoped/error.other-type-cannot-read-private.maxon:22:12: cannot access unexported field: 'secret' outside of type 'A'
```

<!-- test: error.other-type-cannot-write-private -->
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
error E3014: specs/field-visibility-is-type-scoped/error.other-type-cannot-write-private.maxon:22:5: cannot access unexported field: 'secret' outside of type 'A'
```

<!-- test: own-type-reaches-its-own-private-field -->
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

<!-- test: error.an-extension-in-another-file-cannot-read-a-private-field-of-the-extended-type -->
`Map` is declared in the standard library, so an `extension Map` in a program file is outside the type: `self.keys` is
`Map`'s private field.

```maxon
typealias Index = Map with (String, Count)

extension Map
	function slots() returns Count
		return self.keys.count()
	end 'slots'
end 'Map'

function main() returns ExitCode
	var index = Index.create()
	index.upsert("key", value: 1)
	return index.slots() as ExitCode
end 'main'
```
```maxoncstderr
error E3014: <fragment>:6:15: cannot access unexported field: 'keys' outside of type 'Map'
```

<!-- test: error.an-extension-in-another-file-cannot-write-a-private-field-of-the-extended-type -->
A write of a private field breaks the type's invariants, so it is refused the same way.

```maxon
typealias Index = Map with (String, Count)

extension Map
	function corrupt()
		self.capacity = 0
	end 'corrupt'
end 'Map'

function main() returns ExitCode
	var index = Index.create()
	index.corrupt()
	return 0
end 'main'
```
```maxoncstderr
error E3014: <fragment>:6:8: cannot access unexported field: 'capacity' outside of type 'Map'
```

<!-- test: error.an-extension-in-another-file-cannot-read-a-private-field-by-its-bare-name -->
The bare spelling of the same read.

```maxon
typealias Index = Map with (String, Count)

extension Map
	function slots() returns Count
		return keys.count()
	end 'slots'
end 'Map'

function main() returns ExitCode
	var index = Index.create()
	index.upsert("key", value: 1)
	return index.slots() as ExitCode
end 'main'
```
```maxoncstderr
error E3014: <fragment>:6:10: cannot access unexported field: 'keys' outside of type 'Map'
```

<!-- test: an-extension-in-the-types-own-file-reaches-its-private-field -->
An extension that sits in the file that declares the type is part of it: `self.hidden` and the bare `hidden` are the
type's own private field.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var hidden as Integer

	static function create(hidden Integer) returns Self
		return Self{hidden: hidden}
	end 'create'
end 'Counter'

extension Counter
	function peek() returns Integer
		return self.hidden
	end 'peek'

	function bump()
		self.hidden = self.hidden + 1
		hidden = hidden + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(40)
	c.bump()
	return c.peek() as ExitCode
end 'main'
```
```exitcode
42
```
