---
feature: field-visibility-is-directory-scoped
status: stable
keywords: field, visibility, export, E3014, directory-scope, encapsulation
category: type-system
---
# Field Visibility Is Scoped To The Type's DIRECTORY

## Documentation

An unexported field is reachable from every file in the directory that declares its type, and from that
directory's subdirectories: a method of another type, a free function and an `extension` written there all
read and write it. A reader in any other directory is refused with `E3014`, `field 'T.f' is not exported`, at
the field token, whether it reaches the field through a value, through `self` in an `extension`, or by its bare
name. The standard library's types are declared under `stdlib/`, outside every program directory, so their
unexported fields are refused to every program file.

The gate is the declaring type's directory, never the reading body's type and never its file. A compiler that
asked "am I inside the declaring type?" would refuse the same-directory readers below, and one that asked "am I
inside any type?" would let another directory's methods reach the field. The read and the write are pinned
separately because the check has two callers.

## Tests

<!-- test: another-type-in-the-same-file-reads-an-unmarked-field -->
`type B`'s method reads `type A`'s unmarked field. Same file, different type: the directory is the gate, so the
read is legal.
```maxon

typealias Integer = int(i64.min to i64.max)

type A
	var secret as Integer

	static function create() returns Self
		return A{secret: 17}
	end 'create'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return B{n: 0}
	end 'create'

	function peek() returns Integer
		let a = A.create()
		return a.secret + n
	end 'peek'
end 'B'

function main() returns ExitCode
	let b = B.create()
	return b.peek()
end 'main'
```
```exitcode
17
```

<!-- test: another-type-in-the-same-file-writes-an-unmarked-field -->
The WRITE half of the same access, pinned separately.
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
		let a = A.create()
		a.secret = 42
		return a.secret + n
	end 'poke'
end 'B'

function main() returns ExitCode
	let b = B.create()
	return b.poke()
end 'main'
```
```exitcode
42
```

<!-- test: own-type-reaches-its-own-private-field -->
`type A`'s own method reaches A's unexported field through a LOCAL of its own type, not through `self`.
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

<!-- test: error.a-type-in-another-directory-cannot-read-an-unmarked-field -->
`Box` is exported and its field `v` is unmarked, so a method of a type in another directory may name `Box` and
may not read `v`. The diagnostic points at the FIELD token, never at the value it is read from.
```maxon
// --- file: lib/box.maxon
export type Box
	var v as ExitCode

	export static function create(v ExitCode) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: app/main.maxon
type Reader
	var n as ExitCode

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function peek() returns ExitCode
		let b = Box.create(5)
		return b.v + n
	end 'peek'
end 'Reader'

function main() returns ExitCode
	return Reader.create().peek()
end 'main'
```
```maxoncstderr
error E3014: app/<fragment>:21:12: field 'Box.v' is not exported
```

<!-- test: error.a-type-in-another-directory-cannot-write-an-unmarked-field -->
The WRITE half, refused at the same token for the same reason.
```maxon
// --- file: lib/box.maxon
export type Box
	var v as ExitCode

	export static function create(v ExitCode) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

// --- file: app/main.maxon
type Writer
	var n as ExitCode

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function poke() returns ExitCode
		let b = Box.create(5)
		b.v = 9
		return n
	end 'poke'
end 'Writer'

function main() returns ExitCode
	return Writer.create().poke()
end 'main'
```
```maxoncstderr
error E3014: app/<fragment>:21:5: field 'Box.v' is not exported
```

<!-- test: error.an-extension-outside-the-types-directory-cannot-read-an-unmarked-field-of-the-extended-type -->
`Map` is declared in the standard library, so an `extension Map` in a program file is outside its directory:
`self.keys` is `Map`'s unmarked field.

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
error E3014: <fragment>:6:15: field 'Map.keys' is not exported
```

<!-- test: error.an-extension-outside-the-types-directory-cannot-write-an-unmarked-field-of-the-extended-type -->
A write of an unmarked field breaks the type's invariants, so it is refused the same way.

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
error E3014: <fragment>:6:8: field 'Map.capacity' is not exported
```

<!-- test: error.an-extension-outside-the-types-directory-cannot-read-an-unmarked-field-by-its-bare-name -->
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
error E3014: <fragment>:6:10: field 'Map.keys' is not exported
```

<!-- test: an-extension-in-the-types-own-file-reaches-its-unmarked-field -->
An extension in the file that declares the type reaches its unmarked field, by `self.hidden` and by the bare
`hidden`.

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
	let c = Counter.create(40)
	c.bump()
	return c.peek() as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-extension-in-a-sibling-file-reaches-an-unmarked-field-by-self-and-by-its-bare-name -->
An extension in another file of the type's directory reaches the unmarked field exactly as one in the type's own
file does.

```maxon
// --- file: counter.maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var hidden as Integer

	static function create(hidden Integer) returns Self
		return Self{hidden: hidden}
	end 'create'
end 'Counter'

// --- file: bump.maxon
extension Counter
	function peek() returns Integer
		return self.hidden
	end 'peek'

	function bump()
		self.hidden = self.hidden + 1
		hidden = hidden + 1
	end 'bump'
end 'Counter'

// --- file: main.maxon
function main() returns ExitCode
	let c = Counter.create(40)
	c.bump()
	return c.peek() as ExitCode
end 'main'
```
```exitcode
42
```
