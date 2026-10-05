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

The standard library makes its commonly-used aliases `public`, such as `ExitCode`, `Byte`, `ElementIndex` and `ByteArray`. A library declaration without `public` is hidden from author code, bare and qualified.

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
error E3095: Ambiguous bare-name call to 'add': more than one visible declaration matches it.
  Qualify it as one of: math.add, text.add
```

When there is no collision, unqualified cross-file calls continue to work via the cross-file fallback. See `specs/namespaces.md` for the canonical resolution rules and the `error.cross-file-bare-name-ambiguous` test that pins this diagnostic.

The same model applies to **type names** — typealiases, types, enums, unions and interfaces: two exported declarations of one bare name in different directories are accepted at decl time, and a bare reference from a third file is rejected with **E3063** (`Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, legacy.Score`). The user writes `api.Score` or `legacy.Score` to disambiguate; a project-root declaration is `export.Score` and the standard library's is `stdlib.Score`. A file's own declaration wins its bare name in that file. Two typealiases of one name in one file, or two nameable ones in one directory, are E3061 — qualification cannot tell them apart. See `specs/typealias-collision.md` for the canonical tests.

Qualification never bypasses visibility, and a type hidden from a file hides its members too: through a value of it, that file reaches no field, method, extension method, accessor or static, nor the calls the compiler makes on the value's behalf (interpolation's `toString`, `==` and ordering operators, `for` iteration, `match`) — E3008, or E3088 for a `module` type outside its subtree. A value held at an exported interface answers that interface's requirements whatever type stands behind it.

## Tests

<!-- test: export-function-basic -->
```maxon
// --- file: api/lib.maxon
export typealias Integer = int(i64.min to i64.max)

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
export typealias Integer = int(i64.min to i64.max)

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
export typealias Integer = int(i64.min to i64.max)

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
⚠ This gates the `Array with T` TYPEALIAS reaching another file, and the `export` keyword is
load-bearing in it: strip `export` and the alias is unreachable from `app/main.maxon`, so the
declaring file is told `E3062: unused typealias: 'IntArray'` and the use site loses its type name.
`error.non-exported-typealias-cross-file` below pins both halves of that refusal.
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
export typealias Integer = int(i64.min to i64.max)

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
export typealias Integer = int(i64.min to i64.max)

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
export typealias Integer = int(i64.min to i64.max)

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
error E3008: specs/export-keyword/error.non-exported-function-cross-file.maxon:11:9: function 'privateHelper' is not exported
```

<!-- test: error.typealias-with-unknown-element-type -->
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
error E3011: specs/export-keyword/error.typealias-with-unknown-element-type.maxon:2:33: Unknown type 'UnknownType'
```

<!-- test: exported-type-cross-file -->
```maxon
// --- file: api/point.maxon
export typealias Integer = int(i64.min to i64.max)

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
error E3008: specs/export-keyword/error.non-exported-type-cross-file.maxon:16:11: type 'InternalPoint' is not exported
error E3008: specs/export-keyword/error.non-exported-type-cross-file.maxon:15:10: type 'InternalPoint' is not exported
```

<!-- test: error.a-non-exported-types-static-is-refused-across-files -->
Calling a static names its type, and the type's tier is what refuses the name: an exported static of a type that is
not exported cannot be called from another file.
```maxon
// --- file: point.maxon
type InternalPoint
	export static function answer() returns ExitCode
		return 42
	end 'answer'
end 'InternalPoint'

// --- file: main.maxon
function main() returns ExitCode
	return InternalPoint.answer()
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-non-exported-types-static-is-refused-across-files.maxon:11:9: type 'InternalPoint' is not exported
```

<!-- test: error.a-non-exported-generic-type-field-read-across-files-names-its-visibility -->
```maxon
// --- file: lib.maxon
type Box uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Box'

export typealias StrBox = Box with String

export function makeBox() returns StrBox
	return StrBox.make("boxed")
end 'makeBox'

// --- file: main.maxon
function main() returns ExitCode
	let b = makeBox()
	let s = b.v
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-non-exported-generic-type-field-read-across-files-names-its-visibility.maxon:20:12: type 'Box' is not exported
```

<!-- test: error.a-hidden-types-field-write-is-refused-through-a-value -->
A type another file may not name hides its members from that file, so a value of it that reached the file through
an exported function cannot be written through either.
```maxon
// --- file: lib.maxon
type Box uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Box'

export typealias StrBox = Box with String

export function makeBox() returns StrBox
	return StrBox.make("boxed")
end 'makeBox'

// --- file: main.maxon
function main() returns ExitCode
	var b = makeBox()
	b.v = "changed"
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-field-write-is-refused-through-a-value.maxon:20:4: type 'Box' is not exported
```

<!-- test: error.a-hidden-types-method-is-refused-through-a-value -->
An exported method of a type the reading file may not name is hidden with the type.
```maxon
// --- file: lib.maxon
type Box uses T
	var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	export function get() returns T
		return self.v
	end 'get'
end 'Box'

export typealias StrBox = Box with String

export function makeBox() returns StrBox
	return StrBox.make("boxed")
end 'makeBox'

// --- file: main.maxon
function main() returns ExitCode
	let b = makeBox()
	print("{b.get()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-method-is-refused-through-a-value.maxon:24:12: type 'Box' is not exported
```

<!-- test: error.a-hidden-types-extension-method-is-refused-through-a-value -->
An extension's exported method belongs to the type it extends, and is hidden with it.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

type Inner
	export var n as Integer

	static function make(n Integer) returns Self
		return Self{n: n}
	end 'make'
end 'Inner'

extension Inner
	export function doubled() returns Integer
		return self.n * 2
	end 'doubled'
end 'Inner'

export type Holder
	export var inner as Inner

	export static function make() returns Self
		return Self{inner: Inner.make(21)}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.make()
	print("{h.inner.doubled()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-extension-method-is-refused-through-a-value.maxon:30:18: type 'Inner' is not exported
```

<!-- test: error.a-hidden-enums-method-is-refused-through-a-value -->
```maxon
// --- file: lib.maxon
enum Mode
	fast
	slow

	export function isFast() returns bool
		return self == Mode.fast
	end 'isFast'
end 'Mode'

export type Settings
	export var mode as Mode

	export static function make() returns Self
		return Self{mode: Mode.fast}
	end 'make'
end 'Settings'

// --- file: main.maxon
function main() returns ExitCode
	let s = Settings.make()

	if s.mode.isFast() 'fast'
		return 42
	end 'fast'

	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-enums-method-is-refused-through-a-value.maxon:24:12: type 'Mode' is not exported
```

<!-- test: error.a-hidden-enums-accessor-is-refused-through-a-value -->
A case's `name`, `ordinal` and `rawValue` are members of the enum, and are hidden with it.
```maxon
// --- file: lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Settings
	export var mode as Mode

	export static function make() returns Self
		return Self{mode: Mode.slow}
	end 'make'
end 'Settings'

// --- file: main.maxon
function main() returns ExitCode
	let s = Settings.make()
	print("{s.mode.name}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-enums-accessor-is-refused-through-a-value.maxon:19:17: type 'Mode' is not exported
```

<!-- test: a-hidden-type-answers-through-an-exported-interface -->
A value held at an exported interface is that interface's surface, so its requirements stay callable whatever type
stands behind it.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Answer
	function answer() returns Integer
end 'Answer'

type Hidden implements Answer
	var value as Integer

	static function make() returns Self
		return Self{value: 42}
	end 'make'

	export function answer() returns Integer
		return self.value
	end 'answer'
end 'Hidden'

export function pick() returns Answer
	return Hidden.make()
end 'pick'

// --- file: main.maxon
function main() returns ExitCode
	let a = pick()
	print("{a.answer()}\n")
	return 0
end 'main'
```
```stdout
42
```

<!-- test: error.a-hidden-interfaces-method-is-refused-through-a-value -->
A value held at an interface the reading file may not name exposes none of that interface's requirements.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function make(side Integer) returns Self
		return Self{side: side}
	end 'make'

	export function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

export type Holder
	export var shape as Shape

	export static function make() returns Self
		return Self{shape: Square.make(6)}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let s = Holder.make().shape
	print("{s.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-interfaces-method-is-refused-through-a-value.maxon:32:12: type 'Shape' is not exported
```

<!-- test: error.a-hidden-types-to-string-is-refused-through-interpolation -->
Interpolating a value calls its type's `toString`, which is hidden with the type.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

type Point implements Stringable
	var x as Integer

	static function make(x Integer) returns Self
		return Self{x: x}
	end 'make'

	export function toString() returns String
		return "p{self.x}"
	end 'toString'
end 'Point'

export type Holder
	export var point as Point

	export static function make() returns Self
		return Self{point: Point.make(4)}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let p = Holder.make().point
	print("{p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-to-string-is-refused-through-interpolation.maxon:28:10: type 'Point' is not exported
```

<!-- test: error.a-hidden-types-equals-is-refused-through-the-equality-operator -->
`==` on two values calls their type's `equals`, which is hidden with the type.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

type Point implements Equatable
	var x as Integer

	static function make(x Integer) returns Self
		return Self{x: x}
	end 'make'

	function equals(other Self) returns bool
		return self.x == other.x
	end 'equals'
end 'Point'

export type Pair
	export var first as Point
	export var second as Point

	export static function make() returns Self
		return Self{first: Point.make(4), second: Point.make(4)}
	end 'make'
end 'Pair'

// --- file: main.maxon
function main() returns ExitCode
	let pair = Pair.make()

	if pair.first == pair.second 'same'
		return 42
	end 'same'

	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-equals-is-refused-through-the-equality-operator.maxon:30:16: type 'Point' is not exported
```

<!-- test: error.a-hidden-types-compare-is-refused-through-an-ordering-operator -->
`<` on two values calls their type's `compare`, which is hidden with the type.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

type Point implements Comparable
	var x as Integer

	static function make(x Integer) returns Self
		return Self{x: x}
	end 'make'

	function compare(other Self) returns Ordering
		if self.x < other.x 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.x > other.x 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

export type Pair
	export var first as Point
	export var second as Point

	export static function make() returns Self
		return Self{first: Point.make(3), second: Point.make(4)}
	end 'make'
end 'Pair'

// --- file: main.maxon
function main() returns ExitCode
	let pair = Pair.make()

	if pair.first < pair.second 'ordered'
		return 42
	end 'ordered'

	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-compare-is-refused-through-an-ordering-operator.maxon:38:16: type 'Point' is not exported
```

<!-- test: error.a-hidden-types-iteration-is-refused-through-a-for-loop -->
A `for` loop over a value calls its type's `createIterator`, which is hidden with the type.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Cursor
	var items as IntArray
	var pos as Integer

	static function create(items IntArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'

		return Self{items: items, pos: 0}
	end 'create'

	function current() returns Integer
		return try self.items.get(self.pos as ElementIndex) otherwise panic("oob")
	end 'current'

	function advance() throws IterationError
		if self.pos + 1 >= (self.items.count() as Integer) 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'

		self.pos = self.pos + 1
	end 'advance'
end 'Cursor'

type Seq
	var items as IntArray

	static function make() returns Self
		return Self{items: [10, 20, 12]}
	end 'make'

	function createIterator() returns Cursor throws IterationError
		return try Cursor.create(self.items)
	end 'createIterator'
end 'Seq'

export type Holder
	export var seq as Seq

	export static function make() returns Self
		return Self{seq: Seq.make()}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let seq = Holder.make().seq
	var sum = 0

	for v in seq 'each'
		sum = sum + v
	end 'each'

	return sum as ExitCode
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-types-iteration-is-refused-through-a-for-loop.maxon:56:2: type 'Seq' is not exported
```

<!-- test: error.a-hidden-enum-is-refused-through-a-match -->
Matching a value reads its case, which is a member of the enum and hidden with it.
```maxon
// --- file: lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Settings
	export var mode as Mode

	export static function make() returns Self
		return Self{mode: Mode.fast}
	end 'make'
end 'Settings'

// --- file: main.maxon
function main() returns ExitCode
	let s = Settings.make()

	match s.mode 'mode'
		fast then return 42
		slow then return 0
	end 'mode'
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-enum-is-refused-through-a-match.maxon:20:2: type 'Mode' is not exported
```

<!-- test: error.a-hidden-unions-payload-is-refused-through-a-match-binding -->
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

union Reading
	value(n Integer)
	missing
end 'Reading'

export type Sensor
	export var reading as Reading

	export static function make() returns Self
		return Self{reading: Reading.value(42)}
	end 'make'
end 'Sensor'

// --- file: main.maxon
function main() returns ExitCode
	let s = Sensor.make()

	match s.reading 'reading'
		value(n) then return n as ExitCode
		missing then return 0
	end 'reading'
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-unions-payload-is-refused-through-a-match-binding.maxon:22:2: type 'Reading' is not exported
```

<!-- test: error.a-hidden-enum-is-refused-through-interpolation -->
Interpolating a case prints its name, which is a member of the enum and hidden with it.
```maxon
// --- file: lib.maxon
enum Mode
	fast
	slow
end 'Mode'

export type Settings
	export var mode as Mode

	export static function make() returns Self
		return Self{mode: Mode.slow}
	end 'make'
end 'Settings'

// --- file: main.maxon
function main() returns ExitCode
	let mode = Settings.make().mode
	print("{mode}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-hidden-enum-is-refused-through-interpolation.maxon:19:10: type 'Mode' is not exported
```

<!-- test: a-hidden-types-values-sort-through-the-librarys-comparable-witness -->
Sorting an array of a hidden type's values calls `compare` through the `Comparable` witness the library's sort
dispatches on, which is the interface's surface rather than the type's, so it stays legal.
```maxon
// --- file: lib.maxon
export typealias Integer = int(i64.min to i64.max)

type Point implements Comparable
	export var x as Integer

	static function make(x Integer) returns Self
		return Self{x: x}
	end 'make'

	function compare(other Self) returns Ordering
		if self.x < other.x 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.x > other.x 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

export typealias PointArray = Array with Point

export type Points
	export var items as PointArray

	export static function make() returns Self
		return Self{items: [Point.make(3), Point.make(1), Point.make(2)]}
	end 'make'

	export function firstX() returns Integer
		let first = try self.items.get(0) otherwise panic("empty")
		return first.x
	end 'firstX'
end 'Points'

// --- file: main.maxon
function main() returns ExitCode
	var points = Points.make()
	points.items.sort()
	print("{points.firstX()}\n")
	return 0
end 'main'
```
```stdout
1
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
error E3008: specs/export-keyword/error.non-exported-enum-cross-file.maxon:10:10: type 'InternalStatus' is not exported
```

<!-- test: error.a-non-exported-unions-case-spawned-across-files -->
```maxon
// --- file: shape.maxon
typealias Radius = int(0 to 100)

union Shape
	circle(r Radius)
	dot
end 'Shape'

// --- file: main.maxon
function main() returns ExitCode
	let p = async Shape.circle(3)
	_ = await p
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/export-keyword/error.a-non-exported-unions-case-spawned-across-files.maxon:12:16: Unsupported: `async Shape.…` — `Shape` is an enum or union, so `Shape.<case>(…)` builds a case rather than calling a function. There is nothing there for a green thread to run
error E3008: specs/export-keyword/error.a-non-exported-unions-case-spawned-across-files.maxon:12:16: type 'Shape' is not exported
```

<!-- test: error.a-non-exported-unions-case-construction-across-files -->
```maxon
// --- file: shape.maxon
typealias Radius = int(0 to 100)

union Shape
	circle(r Radius)
	dot
end 'Shape'

// --- file: main.maxon
function main() returns ExitCode
	let s = Shape.circle(3)

	match s 'shape'
		circle(r) then return r as ExitCode
		dot then return 0
	end 'shape'
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.a-non-exported-unions-case-construction-across-files.maxon:14:2: type 'Shape' is not exported
error E3008: specs/export-keyword/error.a-non-exported-unions-case-construction-across-files.maxon:12:10: type 'Shape' is not exported
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
error E3062: specs/export-keyword/error.non-exported-typealias-cross-file.maxon:3:11: unused typealias: 'InternalScore'
error E3008: specs/export-keyword/error.non-exported-typealias-cross-file.maxon:7:16: typealias 'InternalScore' is not exported
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
error E3061: specs/export-keyword/error.duplicate-typealias-same-file.maxon:3:11: Duplicate typealias 'Score'
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
export typealias SmallInt = int(0 to u8.max)

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
error E2004: specs/export-keyword/error.non-exported-var-cross-file.maxon:7:10: Undefined variable 'secret'
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

Whether `7 as Score` is legal in a file that declares no `Score` depends on whether ANY declaration of
`Score` is visible to it — not on whichever declaration happened to be recorded last, so the answer does
not depend on the order files are walked, which is alphabetical. **The filenames below are load-bearing** —
`a.maxon` sorts before `b.maxon`, so the private declaration is the one recorded last, and the
exported one must still be found.

<!-- test: exported-alias-found-past-a-later-private-one -->
```maxon
// --- file: a.maxon
export typealias Score = int(0 to 100)

function fromA() returns Score
	return 7
end 'fromA'

// --- file: b.maxon
typealias Score = int(0 to 50)

function fromB() returns Score
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
error E3008: specs/export-keyword/error.hidden-alias-const-and-body-cast.maxon:6:14: typealias 'Dead' is not exported
error E3062: specs/export-keyword/error.hidden-alias-const-and-body-cast.maxon:3:11: unused typealias: 'Dead'
```

### A hidden alias of every form is refused in every type position

A non-exported typealias is unreachable from another file whatever its form and wherever another file
writes its name: a parameter type, a return type, a field type, or the head of a static call. Each
spelling gets the hidden-name refusal at the name. The declaring file uses its own alias, so the
declaration is not unused.

<!-- test: error.hidden-ranged-alias-in-every-type-position -->
```maxon
// --- file: probe.maxon
typealias Score = int(0 to 100)

export function probeScore() returns ExitCode
	let s = 3 as Score
	return s
end 'probeScore'

// --- file: main.maxon
type Holder
	var best as Score

	static function create() returns Self
		return Self{best: 0}
	end 'create'

	function isZero() returns bool
		return self.best == 0
	end 'isZero'
end 'Holder'

function kept(s Score) returns Score
	return s
end 'kept'

function main() returns ExitCode
	let h = Holder.create()

	if h.isZero() and kept(4) == 4 'bothHold'
		return probeScore()
	end 'bothHold'

	return 1
end 'main'
```
```maxoncstderr
error E3008: <fragment>:12:14: typealias 'Score' is not exported
error E3008: <fragment>:23:17: typealias 'Score' is not exported
error E3008: <fragment>:23:32: typealias 'Score' is not exported
```

<!-- test: error.hidden-generic-alias-in-every-type-position -->
```maxon
// --- file: probe.maxon
typealias Score = int(0 to 100)
typealias ScoreArray = Array with Score

export function probeScores() returns ExitCode
	var held = ScoreArray.create()
	held.push(3)
	return 0 if held.isEmpty() else 3
end 'probeScores'

// --- file: main.maxon
type Holder
	var scores as ScoreArray

	static function create() returns Self
		return Self{scores: ScoreArray.create()}
	end 'create'

	function isEmpty() returns bool
		return self.scores.isEmpty()
	end 'isEmpty'
end 'Holder'

function kept(scores ScoreArray) returns ScoreArray
	return scores
end 'kept'

function main() returns ExitCode
	let h = Holder.create()
	let k = kept(ScoreArray.create())

	if h.isEmpty() and k.isEmpty() 'bothEmpty'
		return probeScores()
	end 'bothEmpty'

	return 1
end 'main'
```
```maxoncstderr
error E3008: <fragment>:14:16: typealias 'ScoreArray' is not exported
error E3008: <fragment>:17:23: typealias 'ScoreArray' is not exported
error E3008: <fragment>:25:22: typealias 'ScoreArray' is not exported
error E3008: <fragment>:25:42: typealias 'ScoreArray' is not exported
error E3008: <fragment>:31:15: typealias 'ScoreArray' is not exported
```

<!-- test: error.hidden-function-alias-in-every-type-position -->
```maxon
// --- file: probe.maxon
typealias Flip = function(bool) returns bool

function same(b bool) returns bool
	return b
end 'same'

function applied(op Flip, b bool) returns bool
	return op(b)
end 'applied'

export function probeFlip() returns ExitCode
	return 2 if applied(same, b: false) else 0
end 'probeFlip'

// --- file: main.maxon
function negate(b bool) returns bool
	return not b
end 'negate'

type Holder
	var op as Flip

	static function create() returns Self
		return Self{op: negate}
	end 'create'

	function run(b bool) returns bool
		return self.op(b)
	end 'run'
end 'Holder'

function chosen(op Flip) returns Flip
	return op
end 'chosen'

function main() returns ExitCode
	let h = Holder.create()
	let f = chosen(negate)

	if h.run(true) or f(true) 'eitherHolds'
		return 1
	end 'eitherHolds'

	return probeFlip()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:23:12: typealias 'Flip' is not exported
error E3008: <fragment>:34:20: typealias 'Flip' is not exported
error E3008: <fragment>:34:34: typealias 'Flip' is not exported
```

<!-- test: error.hidden-tuple-alias-in-every-type-position -->
```maxon
// --- file: probe.maxon
typealias Pair = (bool, bool)

function made() returns Pair
	return (false, true)
end 'made'

export function probePair() returns ExitCode
	let p = made()
	return 1 if p.0 else 0
end 'probePair'

// --- file: main.maxon
type Holder
	var pair as Pair

	static function create() returns Self
		return Self{pair: (true, false)}
	end 'create'

	function first() returns bool
		return self.pair.0
	end 'first'
end 'Holder'

function flipped(p Pair) returns Pair
	return (p.1, p.0)
end 'flipped'

function main() returns ExitCode
	let h = Holder.create()
	let f = flipped((true, false))

	if h.first() and f.1 'bothHold'
		return probePair()
	end 'bothHold'

	return 1
end 'main'
```
```maxoncstderr
error E3008: <fragment>:16:14: typealias 'Pair' is not exported
error E3008: <fragment>:27:20: typealias 'Pair' is not exported
error E3008: <fragment>:27:34: typealias 'Pair' is not exported
```

### A member the COMPILER wrote crosses a file boundary

<!-- test: synthesized-clone-crosses-an-exported-type-s-file-boundary -->
⭐ **A RATCHET, NOT A GATE.** `clone` is synthesized for a type whose fields all conform, so nobody
writes it and nobody writes a visibility modifier for it. A stub registered with neither `IsExported`
nor `IsModuleVisible` would be file-private, and this exact program would be refused with
`E3008: function 'Holder.clone' is not exported` while `a == b` over the identical pair compiled and
ran — one operation, two spellings, disagreeing. The case is here so a change to how a synthesized
member is registered cannot introduce that split silently.

⚠ The `.equals()` sibling is deliberately NOT here: The compiler synthesizes no `equals` at all and answers
`E3004: call to undefined function 'Holder.equals'`. That is a different gap and belongs to its own
row, not to this one.
```maxon
// --- file: holder.maxon
export typealias Integer = int(i64.min to i64.max)

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

### A STATIC FIELD obeys the export rule its type does

`export` on the type publishes the type, not every slot inside it. A `static var` without its own
`export` is file-private exactly as a top-level function is, and reading or writing it from another
file is the same refusal with a different noun — `function 'x' is not exported` and
`static 'T.x' is not exported` are one rule.

<!-- test: error.non-exported-static-read-cross-file -->
```maxon
// --- file: holder.maxon
typealias Count = int(0 to u64.max)

export type Holder
	static var cached = Holder.build()
	export var value as Count

	static function build() returns Holder
		return Holder{value: 7}
	end 'build'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let a = Holder.cached
	return a.value
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.non-exported-static-read-cross-file.maxon:16:17: static 'Holder.cached' is not exported
```

<!-- test: error.non-exported-static-constant-assign-cross-file -->
The write is refused at the same token for the same reason. Assignment reaches the member through the
same qualified-name lookup a read does, so a refusal that fired only on the read would be a rule
written once and applied in one of the two places it belongs.

The initializer is a constant because a call-built one cannot express this: a global holding a
SCALAR has its value written into the `.data` image before any code runs, so a factory-built global
must return a record (E2015). `error.non-exported-static-read-cross-file` above carries the call-built
form, whose `Holder.build()` returns one. A call-built SCALAR global would need a guard word per scalar
static rather than the in-band pointer sentinel a record slot allows.
```maxon
// --- file: counter.maxon
export type Counter
	static var hits = 7
end 'Counter'

// --- file: main.maxon
function main() returns ExitCode
	Counter.hits = 9
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.non-exported-static-constant-assign-cross-file.maxon:9:10: static 'Counter.hits' is not exported
```

<!-- test: error.hidden-alias-as-a-top-level-container-create -->
A top-level `<alias>.create()` names the alias, so another file's file-private one is refused there as it is in
a body.
```maxon
// --- file: types.maxon
typealias Smalls = Array with ExitCode

export function emptySmalls() returns ExitCode
	let xs = Smalls.create()
	print("{xs.count()}")
	return 0
end 'emptySmalls'

// --- file: main.maxon
let G = Smalls.create()

function main() returns ExitCode
	print("{G.count()}")
	return emptySmalls()
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.hidden-alias-as-a-top-level-container-create.maxon:12:9: typealias 'Smalls' is not exported
```

<!-- test: error.hidden-alias-as-a-top-level-set-from-head -->
A top-level `<alias> from […]` names the alias, so another file's file-private one is refused there as it is in
a body.
```maxon
// --- file: types.maxon
typealias Counts = Set with ExitCode

export function probeCounts() returns ExitCode
	let s = Counts from [1, 2]
	print("{s.count()}")
	return 0
end 'probeCounts'

// --- file: main.maxon
let S = Counts from [3, 4]

function main() returns ExitCode
	print("{S.count()}")
	return probeCounts()
end 'main'
```
```maxoncstderr
error E3008: specs/export-keyword/error.hidden-alias-as-a-top-level-set-from-head.maxon:12:9: typealias 'Counts' is not exported
```
