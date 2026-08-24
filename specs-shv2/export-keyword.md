---
feature: export-keyword
status: stable
keywords: [export, visibility, module, function, type]
category: infrastructure
---

# Export Keyword

## Documentation

### Export Keyword

All declarations — functions, types, enums, typealiases, and top-level variables — are file-scoped by default. The `export` keyword makes them visible to other modules. Without `export`, a declaration can only be used within the file where it is defined.

```text
export function publicApi() returns Integer
  return privateHelper()
end 'publicApi'

function privateHelper() returns Integer
  return 42
end 'privateHelper'
```

When modules are compiled together, only exported symbols from earlier modules can be called by later modules. Non-exported symbols from other files are invisible — attempting to use them produces a compile error.

### Exporting Types

Types can be exported to make them available to other modules. Without `export`, a type is only usable within its file:

```text
export type Point
  export var x as Integer
  export var y as Integer
end 'Point'
```

### Exporting Enums

Enums follow the same visibility rules as types:

```text
export enum Color
  red
  green
  blue
end 'Color'
```

Without `export`, a enum is only visible within its declaring file.

### Exporting Type Aliases

Typealiases are also file-scoped by default. Use `export` for cross-file visibility:

```text
export typealias Score = int(0 to 100)
```

The standard library exports commonly-used aliases like `Integer`, `Float`, `Byte`, `Count`, `Index`, and `ExitCode`.

### Exporting Methods

Methods within types can be individually exported:

```text
export type Calculator
  var result as Integer

  export function add(n Integer)
    result = result + n
  end 'add'

  function internalReset()
    result = 0
  end 'internalReset'
end 'Calculator'
```

### Namespace Disambiguation

A file's namespace is the directory it lives in (see `specs/namespaces.md`). When two files in different directories both export a function with the same bare name, an unqualified call from a third file is ambiguous and must be rewritten with the directory-qualified form:

```text
// math/ops.maxon and text/ops.maxon both export 'add'.
// In app/main.maxon:
var result1 = math.add(1, 2)         // calls math/ops.maxon's add
var result2 = text.add("hi", "lo")   // calls text/ops.maxon's add
```

A bare `add(...)` from `app/main.maxon` is rejected by the self-hosted compiler with E3095:

```text
error E3095: Ambiguous bare-name call to 'add': multiple visible definitions found.
  Qualify with a directory name. Candidates: math.add, text.add
```

When there is no collision, unqualified cross-file calls continue to work via the cross-file fallback. See `specs/namespaces.md` for the canonical resolution rules and the `error.cross-file-bare-name-ambiguous` test that pins this diagnostic.

The same model applies to **typealiases**: two exported typealiases with the same bare name in different directories are accepted at decl time, and a bare reference from a third file is rejected with **E3063** (`Ambiguous typealias 'Score': multiple visible definitions found. Qualify with a directory name. Candidates: api.Score, legacy.Score`). The user writes `api.Score` or `legacy.Score` to disambiguate. Same-file duplicate typealiases remain E3061 — qualification cannot resolve two declarations in the same file. See `specs/typealias-collision.md` for the canonical tests.

## Tests

<!-- test: export-function-basic -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
	return 21
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return helper() + helper()
end 'main'
```
```exitcode
42
```

<!-- test: export-type-basic -->
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
	var x as Integer
	var y as Integer

	export function sum() returns Integer
		return x + y
	end 'sum'

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(20, y: 22)
	return p.sum()
end 'main'
```
```exitcode
42
```

<!-- test: non-export-function-works -->
```maxon

typealias Integer = int(i64.min to i64.max)

function helper() returns Integer
	return 42
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: mixed-export-and-non-export -->
```maxon
// --- file: api/lib.maxon
typealias Integer = int(i64.min to i64.max)

export function publicFunc() returns Integer
	return privateFunc() + 20
end 'publicFunc'

function privateFunc() returns Integer
	return 22
end 'privateFunc'

// --- file: app/main.maxon
function main() returns ExitCode
	return publicFunc()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-basic -->
⚠ This gates the `Array with T` TYPEALIAS reaching another file, not the `export`
keyword: shv2 enforces export visibility for `var`s only, so deleting `export` here
still compiles. `error.non-exported-typealias-cross-file` below is the case that will
gate the keyword, and it stays disabled until that enforcement exists.
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

// --- file: app/main.maxon
function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-in-type-field -->
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

export type Container
	export var items as IntArray

	export static function create() returns Self
		return Container{items: IntArray.create()}
	end 'create'

	export function add(n Integer)
		items.push(n)
	end 'add'

	export function sum() returns Integer
		var total = 0
		for item in items 'loop'
			total = total + item
		end 'loop'
		return total
	end 'sum'
end 'Container'

// --- file: app/main.maxon
function main() returns ExitCode
	var c = Container.create()
	c.add(20)
	c.add(22)
	return c.sum()
end 'main'
```
```exitcode
42
```

<!-- test: export-typealias-as-return-type -->
The same reach in RETURN position — and the same caveat as `export-typealias-basic`:
the `export` keyword is inert here, so what this gates is the instance typealias
resolving to one type across a file boundary.
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)

export typealias IntArray = Array with Integer

export function makeArray() returns IntArray
	var arr = IntArray.create()
	arr.push(42)
	return arr
end 'makeArray'

// --- file: app/main.maxon
function main() returns ExitCode
	let arr = makeArray()
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: non-export-typealias-in-same-file -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: exported-function-cross-file -->
```maxon
// --- file: api/helper.maxon
typealias Integer = int(i64.min to i64.max)

export function helper() returns Integer
	return 42
end 'helper'

// --- file: app/main.maxon
function main() returns ExitCode
	return helper()
end 'main'
```
```exitcode
42
```

<!-- test: non-exported-function-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
	return 99
end 'privateHelper'

function main() returns ExitCode
	return privateHelper()
end 'main'
```
```exitcode
99
```

<!-- test: error.non-exported-function-cross-file -->
```maxon
// --- file: helper.maxon
typealias Integer = int(i64.min to i64.max)

function privateHelper() returns Integer
	return 99
end 'privateHelper'

// --- file: main.maxon
function main() returns ExitCode
	return privateHelper()
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/export-keyword/error.non-exported-function-cross-file.test:11:9: function 'privateHelper' is not exported
```

<!-- test: error.typealias-with-unknown-element-type -->
<!-- shv2 raises its OWN registered code for this fact: E3011 `SemanticUnknownType` ("a named type resolves to no declared type") is what `docs/error-codes.txt` gives that meaning, and it is the code `TypeResolution` and both `as`-cast sites already raise for it. The oracle spends E2003 (`ParserExpectedType` — "a type was required here and the token stream had something else") because its parser reaches the fact first; using that number here would give one number two meanings. The anchor is the ARGUMENT's own first token rather than the oracle's one-past-the-end column. -->
```maxon
typealias BadArray = Array with UnknownType

type Container
	var items as BadArray
end 'Container'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/export-keyword/error.typealias-with-unknown-element-type.test:2:33: Unknown type 'UnknownType'
```

<!-- test: exported-type-cross-file -->
```maxon
// --- file: api/point.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
	export var x as Integer
	export var y as Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-type-cross-file -->
```maxon
// --- file: point.maxon
typealias Integer = int(i64.min to i64.max)

type InternalPoint
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'InternalPoint'

// --- file: main.maxon
function main() returns ExitCode
	let p = InternalPoint.create(42)
	return p.x
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/export-keyword/error.non-exported-type-cross-file.test:16:11: Unknown type 'InternalPoint' in field access chain
```

<!-- test: exported-enum-cross-file -->
```maxon
// --- file: api/color.maxon
export enum Color
	red
	green
	blue
end 'Color'

// --- file: app/main.maxon
function main() returns ExitCode
	let c = Color.blue
	match c 'check'
		blue then return 42
		red then return 0
		green then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-enum-cross-file -->
```maxon
// --- file: status.maxon
enum InternalStatus
	ok
	err
end 'InternalStatus'

// --- file: main.maxon
function main() returns ExitCode
	let s = InternalStatus.ok
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/export-keyword/error.non-exported-enum-cross-file.test:10:10: Undefined variable 'InternalStatus'
```

<!-- test: exported-typealias-cross-file -->
```maxon
// --- file: api/types.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-typealias-cross-file -->
<!-- export visibility + E3062 unused-typealias -->
```maxon
// --- file: types.maxon
typealias InternalScore = int(0 to 100)

// --- file: main.maxon
function main() returns ExitCode
	let s = 42 as InternalScore
	return s
end 'main'
```
```maxoncstderr
error E3062: specs/fragments/export-keyword/error.non-exported-typealias-cross-file.test:3:11: unused typealias: 'InternalScore'
error E2003: specs/fragments/export-keyword/error.non-exported-typealias-cross-file.test:7:16: Expected type name after 'as'
```

<!-- test: error.duplicate-typealias-same-file -->
```maxon
typealias Score = int(0 to 100)
typealias Score = int(0 to 200)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: specs/fragments/export-keyword/error.duplicate-typealias-same-file.test:3:11: Duplicate typealias 'Score'
```

<!-- test: non-exported-type-same-file -->
```maxon

typealias Integer = int(i64.min to i64.max)

type InternalPoint
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'InternalPoint'

function main() returns ExitCode
	let p = InternalPoint.create(20, y: 22)
	return p.x + p.y
end 'main'
```
```exitcode
42
```

<!-- test: exported-var-cross-file -->
Cross-file access to an exported module-level var with a simple constant value.
```maxon
// --- file: api/counter.maxon
export var counter = 10

// --- file: app/main.maxon
function main() returns ExitCode
		return counter
end 'main'
```
```exitcode
10
```

<!-- test: exported-struct-var-cross-file -->
Cross-file access to an exported module-level struct var.
```maxon
// --- file: api/state.maxon
typealias SmallInt = int(0 to u8.max)

export type Counter
		export var value as SmallInt

		export static function create(value SmallInt) returns Self
			return Self{value: value}
		end 'create'
end 'Counter'

export var shared = Counter.create(0)

// --- file: app/main.maxon
function main() returns ExitCode
		let c = Counter.create(1)
		shared.value = 42 - c.value + c.value
		return shared.value
end 'main'
```
```exitcode
42
```

<!-- test: error.non-exported-var-cross-file -->
Non-exported module-level var should not be accessible from another file.
```maxon
// --- file: state.maxon
var secret = 99

// --- file: main.maxon
function main() returns ExitCode
		return secret
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/export-keyword/error.non-exported-var-cross-file.test:7:10: Undefined variable 'secret'
```

<!-- test: non-exported-enum-same-file -->
```maxon
enum Direction
	up
	down
end 'Direction'

function main() returns ExitCode
	let d = Direction.up
	match d 'check'
		up then return 42
		down then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

### A hidden alias is judged against EVERY declaration of the name, not the last one recorded

Whether `7 as Score` is legal depends on whether ANY declaration of `Score` in scope is exported —
not on whichever declaration happened to be recorded last. Reading only the last one made the answer
depend on the order files are walked, which is alphabetical: the same three files refused the cast
under one set of names and accepted it under another. **The filenames below are load-bearing** —
`a.maxon` sorts before `b.maxon`, so the private declaration is the one recorded last, and the
exported one must still be found.

<!-- test: exported-alias-found-past-a-later-private-one -->
```maxon
// --- file: a.maxon
export typealias Score = int(0 to 100)

public function fromA() returns Score
	return 7
end 'fromA'

// --- file: b.maxon
typealias Score = int(0 to 50)

public function fromB() returns Score
	return 3
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let s = 7 as Score
	return s
end 'main'
```
```exitcode
7
```

### A hidden alias reached from BOTH a top-level constant and a body

The top-level-constant spelling of the hidden-cast rejection is raised from a transient parser whose
artifact is discarded, so it cannot join the deferred queue the body spelling is drained from, and it
therefore lands ahead of the unused-typealias diagnostic. Same code, same message, same anchor as the
body spelling — only the position in the list differs. This case exists to pin that order so it
cannot drift unnoticed; the body-only order is pinned by
`error.non-exported-typealias-cross-file` above.

<!-- test: error.hidden-alias-const-and-body-cast -->
```maxon
// --- file: types.maxon
typealias Dead = int(0 to 100)

// --- file: main.maxon
let K = 5 as Dead

function main() returns ExitCode
	let v = 7 as Dead
	return v + K
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/export-keyword/error.hidden-alias-const-and-body-cast.test:6:14: Expected type name after 'as'
error E3062: specs/fragments/export-keyword/error.hidden-alias-const-and-body-cast.test:3:11: unused typealias: 'Dead'
```

### A member the COMPILER wrote crosses a file boundary

<!-- test: synthesized-clone-crosses-an-exported-type-s-file-boundary -->
⭐ **A RATCHET, NOT A GATE — THIS COMPILER ALREADY GETS IT RIGHT AND THE POINT IS THAT IT KEEPS
DOING SO.** `clone` is synthesized for a type whose fields all conform, so nobody writes it and
nobody writes a visibility modifier for it. The bootstrap registered its stub with neither
`IsExported` nor `IsModuleVisible`, which meant file-private, and refused this exact program with
`E3008: function 'Holder.clone' is not exported` while `a == b` over the identical pair compiled and
ran — one operation, two spellings, disagreeing. shv2 has never had that split; the case is here so
a future change to how a synthesized member is registered cannot reintroduce it silently.

⚠ The `.equals()` sibling is deliberately NOT here: shv2 synthesizes no `equals` at all and answers
`E3004: call to undefined function 'Holder.equals'`. That is a different gap and belongs to its own
row, not to this one.
```maxon
// --- file: holder.maxon
typealias Integer = int(i64.min to i64.max)

export type Holder
	export var count as Integer
	export var scale as Integer

	export static function make(c Integer, s Integer) returns Holder
		return Holder{count: c, scale: s}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.make(3, s: 4)
	let c = h.clone()
	return c.count + c.scale
end 'main'
```
```exitcode
7
```
