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
  export var x Integer
  export var y Integer
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
  var result Integer

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
	var x Integer
	var y Integer

	export function sum() returns Integer
		return x + y
	end 'sum'

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(x: 20, y: 22)
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
	export var items IntArray

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
```maxon
typealias BadArray = Array with UnknownType

type Container
	var items BadArray
end 'Container'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/export-keyword/error.typealias-with-unknown-element-type.test:2:44: Unknown type: UnknownType
```

<!-- test: exported-type-cross-file -->
```maxon
// --- file: api/point.maxon
typealias Integer = int(i64.min to i64.max)

export type Point
	export var x Integer
	export var y Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

// --- file: app/main.maxon
function main() returns ExitCode
	let p = Point.create(x: 20, y: 22)
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
	export var x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'InternalPoint'

// --- file: main.maxon
function main() returns ExitCode
	let p = InternalPoint.create(x: 42)
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
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'InternalPoint'

function main() returns ExitCode
	let p = InternalPoint.create(x: 20, y: 22)
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
		export var value SmallInt

		export static function create(value SmallInt) returns Self
			return Self{value: value}
		end 'create'
end 'Counter'

export var shared = Counter.create(value: 0)

// --- file: app/main.maxon
function main() returns ExitCode
		let c = Counter.create(value: 1)
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
