---
feature: reference-identity
status: experimental
keywords: [is, is not, reference, identity, pointer, equality]
category: operators
---

# Reference Identity

## Documentation

### Overview

In Maxon, all struct-typed variables are references (heap pointers). The `==` operator compares **contents** (value equality via `Equatable`), while the `is` operator compares **reference identity** — whether two variables point to the same object in memory.

Binding one name to another (`let b = a`) creates a **reference** (alias) — `b` points to the same object as `a`. To create a new independent object, use `var b = a.clone()`. To check if two references point to the same object, use `is`.

> **shv2 retraction (user ruling, 2026-08-13).** Canonical says `var b = a` also aliases, and pins it
> with a `mutation-through-alias` case expecting a write through `b` to be visible through `a`. Under
> Maxon's ownership model as ruled on 2026-08-04 — *"single ownership, everything is a reference … the
> key is mutability"* — that shape is a **move**, precisely because a second name could watch the value
> change; shv2 answers `E3102 use of moved value`. The ruling wins, so that one case is retracted here.
> `let b = a` from an immutable `a` is still a second reference, which is what `assignment-creates-alias`
> pins and what shv2 does. Nothing else in this file is affected: reference identity itself is
> orthogonal to how a binding acquires its reference.

### Operators

- `a is b` — returns `true` if `a` and `b` refer to the same object
- `a is not b` — returns `true` if `a` and `b` refer to different objects

### Example

```text
function areSame(a Point, b Point) returns bool
  return a is b
end 'areSame'

var p = Point{x: 1, y: 2}
areSame(p, b: p)  // true  — same reference passed twice
```

### Rules

- `is` and `is not` work on struct-typed values (including String, Array, and user-defined types).
- Using `is` or `is not` on primitive types (int, float, bool, byte) is a compile error — primitives are values, not references.
- Both operands must be the same type.
- **Literal interning:** identical string/byte/character literals that are provably never mutated are emitted once as a single shared immortal object. Two such literals with the same value therefore refer to the same object, so `is` returns `true` for them (e.g. `"hello" is "hello"`). This is safe precisely because the shared object is immutable; a literal that *is* mutated gets its own independent object.

## Tests

<!-- test: self-identity -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	if a is a 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: assignment-creates-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = a
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: clone-creates-new-object -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = a.clone()
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: different-objects -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = Point.create(1, y: 2)
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: is-not-operator -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = Point.create(1, y: 2)
	if a is not b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-not-self -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	if a is not a 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: function-same-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function areSame(a Box, b Box) returns bool
	return a is b
end 'areSame'

function main() returns ExitCode
	let x = Box.create(42)
	let y = Box.create(42)
	var result = 0
	if areSame(x, b: x) 'same'
		result = result + 1
	end 'same'
	if areSame(x, b: y) 'diff'
		result = result + 10
	end 'diff'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: string-identity -->
```maxon
function main() returns ExitCode
	// Two identical string literals are INTERNED: a never-mutated literal is emitted once as a
	// shared immortal object, so `a` and `b` refer to the same object and `a is b` is true.
	let a = "hello"
	let b = "hello"
	var result = 0
	if a is a 'self'
		result = result + 1
	end 'self'
	if a is b 'diff'
		result = result + 10
	end 'diff'
	return result
end 'main'
```
```exitcode
11
```

<!-- test: clone-isolates-mutation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	var b = a.clone()
	b.x = 99
	return a.x
end 'main'
```
```exitcode
1
```

<!-- test: primitive-error -->
```maxon
function main() returns ExitCode
	let a = 42
	let b = 42
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3068: specs/fragments/reference-identity/primitive-error.test:5:7: 'is' requires reference types (structs), not primitive values
```

<!-- test: byte-array-constant-identity -->
A module-level `let` holding a byte string is image data, not a heap object: the whole program reads ONE
`.rdata` record for it, and the record table pools by bytes. So `is` answers the same way it does for the
interned string literals above — a constant is itself, and two constants with the same bytes are one
object. An INLINE `b"…"` is a different storage class (a fresh heap record the statement drops), so it is
its own object and shares with neither.
```maxon
let A = b"hi"
let B = b"hi"
let C = b"ho"

function main() returns ExitCode
	var result = 0
	if A is A 'self'
		result = result + 1
	end 'self'
	if A is B 'sameBytes'
		result = result + 2
	end 'sameBytes'
	if A is not C 'otherBytes'
		result = result + 4
	end 'otherBytes'
	if A is not b"hi" 'inlineIsItsOwn'
		result = result + 8
	end 'inlineIsItsOwn'
	return result
end 'main'
```
```exitcode
15
```

<!-- test: static-array-of-literals -->
An array of never-mutated string literals is a shared immortal record: its inline pointer table
references the elements' own static records. Iterating and indexing it allocate nothing, the
elements stay valid, and going out of scope frees nothing extra (no leak).
```maxon
function main() returns ExitCode
	let colors = ["red", "green", "blue"]
	var joined = ""
	for c in colors 'each'
		joined = "{joined}{c} "
	end 'each'
	let mid = try colors.get(1) otherwise "?"
	print("{joined}| {mid} | {colors.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
red green blue | green | 3
```

<!-- test: a-durable-store-of-a-constant-copies-it -->
⭐ **BINDING A CONSTANT KEEPS ITS IDENTITY; STORING ONE INTO A CONTAINER DOES NOT, AND THE DIFFERENCE IS
THE POINT.** A module-level `let` is image data on a read-only page. `let b = A` is a borrow and shares
the one record, so `b is A` — that is `assignment-creates-alias` and it holds here unchanged. But a
container OUTLIVES the statement and may be written through later, and a write through it would land on
the image. So a durable store takes a private copy, and a copy is a different object.

**The copy is not a cost the identity pays for; it is the thing that makes the store safe.** Push a
constant into an array, take the element back out and `push` to it: without the copy that write reaches
`.rdata` and faults on x64, or — with no read-only section — silently rewrites the constant on
wasm32-wasi. The seven `*-detaches-from-rdata` cases in `byte-string-literal.md` pin the mutation half;
this pins what the reader sees.

⚠ **THE BOOTSTRAP ANSWERS `2` HERE AND THAT IS NOT A BUG IN EITHER.** Its constant is an ordinary heap
record, so it can co-own where shv2 must copy, and identity survives its store. The two compilers make
different representation choices and `is` is where the difference becomes visible; nothing in `specs/`
pins a store, and this case is where shv2 says which answer is its own. `self-identity`,
`assignment-creates-alias` and `byte-array-constant-identity` are unaffected and agree on both.
```maxon
typealias Num = int(0 to 1000)
typealias NumArray = Array with Num
typealias Holder = Array with NumArray

let Anchor = NumArray.create()

function main() returns ExitCode
	var result = 0
	let bound = Anchor
	if bound is Anchor 'bindingBorrows'
		result = result + 1
	end 'bindingBorrows'

	var h = Holder.create()
	h.push(Anchor)
	let stored = try h.get(0) otherwise NumArray.create()
	if stored is not Anchor 'storeCopies'
		result = result + 2
	end 'storeCopies'

	return result
end 'main'
```
```exitcode
3
```
