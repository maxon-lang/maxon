---
feature: top-level-factory-globals
status: stable
keywords: global, static, factory, initializer, E3220
category: language
---
# A Call In A Module-Level Initializer

## Documentation

A module-level `let`, and a `static let` in a type, is initialized by a literal. A call in its initializer is
refused: **E3220** names the call and the binding it initializes. This holds for every kind of callee: a user
`static` factory, a generic alias's `create()`, a factory of a stdlib module, a factory in another file or
directory, a factory returning a record, a tuple alias, a generic alias, a boxed union or an interface, and a
call given arguments.

The refusal is a property of the program, not of the callee: the callee's visibility, existence and
arguments are not reported beside it. The fix is the one the message states: write a literal
(`X from [..]`, `[..]` and `{..}` are literals, not calls), or build the value where it is used, in a function
that returns it.

## Tests

<!-- test: error.user-generic-instance-factory-at-file-scope -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

let b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3220: <fragment>:14:9: the initializer of 'b' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.user-generic-instance-with-no-create-at-file-scope -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function make() returns Self
		return Self{n: 9}
	end 'make'
end 'Box'

typealias IntBox = Box with Count

let b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3220: <fragment>:14:9: the initializer of 'b' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.user-generic-instance-with-no-create-in-a-function-body -->
The same call in a function body.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function make() returns Self
		return Self{n: 9}
	end 'make'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```maxoncstderr
error E3004: <fragment>:15:17: call to undefined function 'Box.create'
```

<!-- test: error.user-generic-instance-create-returning-a-scalar-at-file-scope-is-not-a-constant -->
A `create` that returns a `Count` makes the initializer a scalar call, which a file-scope binding
cannot hold.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Count
		return 5
	end 'create'
end 'Box'

typealias IntBox = Box with Count

let b = IntBox.create()

function main() returns ExitCode
	return 3
end 'main'
```
```maxoncstderr
error E2045: <fragment>:14:16: Function calls are not allowed in global variable initializers; 'create()' is not a constant expression
```

<!-- test: user-generic-instance-create-returning-a-scalar-in-a-function-body -->
The same call in a function body binds the `Count` it returns.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Count
		return 5
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.arguments-to-a-generic-instance-create-at-file-scope -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

let b = IntBox.create(9)

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3220: <fragment>:14:9: the initializer of 'b' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: arguments-to-a-generic-instance-create-in-a-function-body -->
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create(9)
	return b.n
end 'main'
```
```exitcode
9
```

<!-- test: user-generic-instance-factory-in-a-function -->
The same `IntBox.create()` written inside a function, which answers identically — the file-scope form
above emits the same call, dictionary arguments included.
```maxon
typealias Count = int(0 to u64.max)

type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias IntBox = Box with Count

function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```exitcode
7
```

<!-- test: error.borrowed-record-argument-is-released -->
The initializer of a module-level or `static` `let` calls `Counter.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Counter
	export var n as Count

	static function create(xs Bytes) returns Self
		return Self{n: xs.count() as Count}
	end 'create'
end 'Counter'

let c = Counter.create(Bytes.create(0))

function main() returns ExitCode
	return c.n
end 'main'
```
```maxoncstderr
error E3220: <fragment>:14:9: the initializer of 'c' calls 'Counter.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.cross-file-file-private-generic-create -->
The initializer of a module-level or `static` `let` calls `Box.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
// --- file: api/box.maxon
export typealias Count = int(0 to u64.max)

export type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

export typealias IntBox = Box with Count

// --- file: bin/main.maxon
let b = IntBox.create()

function main() returns ExitCode
	return b.n
end 'main'
```
```maxoncstderr
error E3220: bin/<fragment>:16:9: the initializer of 'b' calls 'Box.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.cross-file-file-private-generic-create-in-a-function-body -->
The byte-identical call in a function body, reported at the same token.
```maxon
// --- file: api/box.maxon
export typealias Count = int(0 to u64.max)

export type Box uses T
	export var n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

export typealias IntBox = Box with Count

// --- file: bin/main.maxon
function main() returns ExitCode
	let b = IntBox.create()
	return b.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:17:17: function 'Box.create' is not exported
```

<!-- test: error.consumed-and-borrowed-arguments-in-one-call -->
The initializer of a module-level or `static` `let` calls `Mixed.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Mixed
	export var kept as Bytes
	export var n as Count
	export var tag as String

	static function create(kept Bytes, seen Bytes, tag String) returns Self
		return Self{kept: kept, n: seen.count() as Count, tag: tag}
	end 'create'
end 'Mixed'

let m = Mixed.create(Bytes.create(0), seen: Bytes.create(0), tag: "hi")

function main() returns ExitCode
	print("{m.tag}\n")
	return m.n + (m.kept.count() as Count)
end 'main'
```
```maxoncstderr
error E3220: <fragment>:16:9: the initializer of 'm' calls 'Mixed.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.same-file-file-private-factory -->
The initializer of a module-level or `static` `let` calls `Database.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

let db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3220: <fragment>:10:10: the initializer of 'db' calls 'Database.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-foreign-record -->
The initializer of a module-level or `static` `let` calls `Outer.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Inner
	export var v as Integer

	static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

type Outer
	static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

let a = Outer.build()

function main() returns ExitCode
	return a.v
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3220: <fragment>:16:9: the initializer of 'a' calls 'Outer.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-foreign-record-that-owns-a-managed-field -->
The initializer of a module-level or `static` `let` calls `Outer.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
type Inner
	export var tag as String

	static function make() returns Self
		return Self{tag: "hi"}
	end 'make'
end 'Inner'

type Outer
	static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

let a = Outer.build()

function main() returns ExitCode
	print("{a.tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:16:9: the initializer of 'a' calls 'Outer.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-record-declared-in-another-file -->
The initializer of a module-level or `static` `let` calls `Outer.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
// --- file: api/inner.maxon
export type Inner
	export var v as Integer

	export static function make() returns Self
		return Self{v: 7}
	end 'make'
end 'Inner'

export typealias Integer = int(i64.min to i64.max)
// --- file: api/outer.maxon
export type Outer
	export static function build() returns Inner
		return Inner.make()
	end 'build'
end 'Outer'

// --- file: bin/main.maxon
let a = Outer.build()

function main() returns ExitCode
	return a.v
end 'main'
```
```maxoncstderr
error E3220: bin/<fragment>:20:9: the initializer of 'a' calls 'Outer.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-tuple-alias -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Num = int(0 to 1000)
typealias Pair = (Num, Num)

type Maker
	static function build() returns Pair
		return (11, 22)
	end 'build'
end 'Maker'

let a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:11:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-tuple-alias-declared-in-another-file -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
// --- file: api/a-maker.maxon
export type Maker
	export static function build() returns Pair
		return (11, 22)
	end 'build'
end 'Maker'

// --- file: api/z-pair.maxon
public typealias Num = int(0 to 1000)
export typealias Pair = (Num, Num)

// --- file: bin/main.maxon
let a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: bin/<fragment>:14:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-tuple-that-owns-heap -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Num = int(0 to 1000)
typealias Tagged = (String, Num)

type Maker
	static function build() returns Tagged
		return ("hi", 7)
	end 'build'
end 'Maker'

let a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:11:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-tuple-whose-spelling-is-already-canonical -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Both = (String, String)

type Maker
	static function build() returns Both
		return ("hi", "there")
	end 'build'
end 'Maker'

let a = Maker.build()

function main() returns ExitCode
	print("{a.0} {a.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:10:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.factory-returning-a-generic-alias -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var n as Num

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias NumBox = Box with Num

type Maker
	static function build() returns NumBox
		return NumBox.create()
	end 'build'
end 'Maker'

let a = Maker.build()

function main() returns ExitCode
	return a.n as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:20:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: factory-returning-a-generic-alias-in-a-function-body -->
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var n as Num

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

typealias NumBox = Box with Num

type Maker
	static function build() returns NumBox
		return NumBox.create()
	end 'build'
end 'Maker'

function main() returns ExitCode
	let a = Maker.build()
	return a.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.factory-returning-a-boxed-union -->
The initializer of a module-level or `static` `let` calls `Maker.build`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

type Maker
	static function build() returns Shape
		return Shape.circle(7)
	end 'build'
end 'Maker'

let a = Maker.build()

function main() returns ExitCode
	match a 'kind'
		circle(r) then return r
		square(s) then return s
	end 'kind'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3220: <fragment>:13:9: the initializer of 'a' calls 'Maker.build': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: factory-returning-a-boxed-union-in-a-function-body -->
```maxon
union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

type Maker
	static function build() returns Shape
		return Shape.circle(7)
	end 'build'
end 'Maker'

function main() returns ExitCode
	let a = Maker.build()
	match a 'kind'
		circle(r) then return r
		square(s) then return s
	end 'kind'
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: error.cross-file-file-private-factory -->
The initializer of a module-level or `static` `let` calls `Database.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
let db = Database.create()

function main() returns ExitCode
	return db.n
end 'main'
```
```maxoncstderr
error E3220: bin/<fragment>:13:10: the initializer of 'db' calls 'Database.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.cross-file-file-private-factory-in-a-function-body -->
```maxon
// --- file: api/db.maxon
export type Database
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Database'

export typealias Integer = int(i64.min to i64.max)
// --- file: bin/main.maxon
function main() returns ExitCode
	let db = Database.create()
	return db.n
end 'main'
```
```maxoncstderr
error E3008: bin/<fragment>:14:20: function 'Database.create' is not exported
```

<!-- test: error.stdlib-factory-reached-from-the-initializer -->
The initializer of a module-level or `static` `let` calls `FilePath.separator`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
let sep = FilePath.separator()

function main() returns ExitCode
	return sep.byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:2:11: the initializer of 'sep' calls 'FilePath.separator': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.stdlib-factory-with-arguments-reached-from-the-initializer -->
The initializer of a module-level or `static` `let` calls `BuildConfig.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
let cfg = BuildConfig.create(StringArray.create(0), output: "app.exe", debug_info: false)

function main() returns ExitCode
	return cfg.sources.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:2:11: the initializer of 'cfg' calls 'BuildConfig.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.stdlib-generic-create-reached-as-a-factory-argument -->
The initializer of a module-level or `static` `let` calls `Map.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Count = int(0 to u64.max)
typealias StrMap = Map with (String, Count)

type Holder
	export var table as StrMap

	static function create(table StrMap) returns Self
		return Self{table: table}
	end 'create'
end 'Holder'

let h = Holder.create(StrMap.create())

function main() returns ExitCode
	return h.table.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:13:9: the initializer of 'h' calls 'Map.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-factory-returning-an-interface-cannot-fill-a-global -->
The initializer of a module-level or `static` `let` calls `Square.shape`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
typealias Side = int(0 to 100)

interface Shape
	function area() returns Side
end 'Shape'

type Square implements Shape
	let side as Side

	function area() returns Side
		return side
	end 'area'

	static function create(side Side) returns Square
		return Square{side: side}
	end 'create'

	static function shape() returns Shape
		return Square.create(7)
	end 'shape'
end 'Square'

let g = Square.shape()

function main() returns ExitCode
	return g.area() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:24:9: the initializer of 'g' calls 'Square.shape': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: a-factory-returning-an-interface-is-held-by-a-local -->
The same factory in a function body: a local is a second SSA value, so it carries the witness.
```maxon
typealias Side = int(0 to 100)

interface Shape
	function area() returns Side
end 'Shape'

type Square implements Shape
	let side as Side

	function area() returns Side
		return side
	end 'area'

	static function create(side Side) returns Square
		return Square{side: side}
	end 'create'

	static function shape() returns Shape
		return Square.create(7)
	end 'shape'
end 'Square'

function main() returns ExitCode
	let g = Square.shape()
	return g.area() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.a-factory-returning-an-enum-at-file-scope-is-not-a-constant -->
An enum is a `named` scalar, so a factory returning one makes the initializer a non-constant scalar, as a
factory returning a ranged alias does.
```maxon
enum Color
	red
	green
end 'Color'

type Maker
	static function color() returns Color
		return Color.green
	end 'color'
end 'Maker'

let g = Maker.color()

function main() returns ExitCode
	if g == Color.green 'isGreen'
		return 4
	end 'isGreen'

	return 0
end 'main'
```
```maxoncstderr
error E2045: <fragment>:13:15: Function calls are not allowed in global variable initializers; 'color()' is not a constant expression
```
