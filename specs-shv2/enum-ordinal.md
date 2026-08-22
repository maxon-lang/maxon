---
feature: enum-ordinal
status: experimental
keywords: [enum, ordinal, position]
category: type-system
---

## Documentation

# Enum Ordinal

All enum have an `.ordinal` property that returns the zero-based position of the case in its declaration order, always as an `int`.

This is different from `.rawValue` for backed enum — `.ordinal` always returns the declaration position:

```text
enum HttpStatus
  ok = 200
  notFound = 404
  serverError = 500
end 'HttpStatus'

var s = HttpStatus.notFound
var pos = s.ordinal    // 1 (second case declared)
var code = s.rawValue  // 404 (the backing value)
```

For simple enum (no explicit values), `.ordinal` and `.rawValue` are identical:

```text
enum Color
  red       // ordinal 0, rawValue 0
  green     // ordinal 1, rawValue 1
  blue      // ordinal 2, rawValue 2
end 'Color'
```

## Tests

### Simple Enum

<!-- test: enum-ordinal.simple -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.green
	// `.ordinal` may only be observed as data, not compared (E3097) — surface
	// it as the exit code; green is the second case, so ordinal 1.
	return c.ordinal as ExitCode
end 'main'
```
```exitcode
1
```

### All Cases

<!-- test: enum-ordinal.all-cases -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let n = Direction.north
	let s = Direction.south
	let e = Direction.east
	let w = Direction.west
	// Ordinals follow declaration order; print all four (comparing them is E3097).
	print("{n.ordinal}{s.ordinal}{e.ordinal}{w.ordinal}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0123
```

### Int-Backed Enum

<!-- test: enum-ordinal.int-backed -->
```maxon
enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'

function main() returns ExitCode
	let s = HttpStatus.serverError
	// ordinal is 2 (third case), not 500 (the raw value)
	return s.ordinal as ExitCode
end 'main'
```
```exitcode
2
```

### Float-Backed Enum

<!-- test: enum-ordinal.float-backed -->
```maxon
enum Threshold
	low = 0.1
	medium = 0.5
	high = 0.9
end 'Threshold'

function main() returns ExitCode
	let t = Threshold.high
	return t.ordinal as ExitCode
end 'main'
```
```exitcode
2
```

### String-Backed Enum

<!-- test: enum-ordinal.string-backed -->
```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
	plain = "text/plain"
end 'ContentType'

function main() returns ExitCode
	let ct = ContentType.html
	return ct.ordinal as ExitCode
end 'main'
```
```exitcode
1
```

### Char-Backed Enum

<!-- test: enum-ordinal.char-backed -->
```maxon
enum Grade
	a = 'A'
	b = 'B'
	c = 'C'
end 'Grade'

function main() returns ExitCode
	let g = Grade.c
	return g.ordinal as ExitCode
end 'main'
```
```exitcode
2
```

### Ordinal in Arithmetic

<!-- test: enum-ordinal.arithmetic -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.blue
	let result = c.ordinal + 10
	if result == 12 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Bare Enum Case in Arithmetic Yields int

An enum case used directly as an arithmetic operand (no `.ordinal`) is its
ordinal — an integer — so the `*` / `+` result is `int`, NOT the enum type. The
result must flow into an int-typed parameter (here a table index): a row-major
table lookup `state * COUNT + col` is the canonical DFA-transition-table idiom.
If the binop result kept the operand's enum type, the `lookup(index Idx)` call
would reject it.

<!-- test: enum-ordinal.bare-case-arithmetic-index -->
```maxon
typealias Idx = int(0 to u64.max)
typealias IdxArray = Array with Idx

enum Col
	a
	b
	c
	COUNT
end 'Col'

enum Row
	x
	y
	COUNT
end 'Row'

function lookup(table IdxArray, index Idx) returns Idx
	return try table.get(index) otherwise 0
end 'lookup'

function main() returns ExitCode
	var table = IdxArray.create()
	var i = 0
	while i < 100 'fill'
		table.push(i)
		i = i + 1
	end 'fill'
	let col = Col.b
	let idx = Row.y * Col.COUNT + col
	return lookup(table, index: idx)
end 'main'
```
```exitcode
4
```

### Ordinal from Function

<!-- test: enum-ordinal.from-function -->
```maxon
enum Priority
	low
	medium
	high
end 'Priority'

typealias OrdinalValue = int(0 to 100)

function getOrdinal(p Priority) returns OrdinalValue
	return p.ordinal
end 'getOrdinal'

function main() returns ExitCode
	let p = Priority.high
	if getOrdinal(p) == 2 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Ordinal on Simple Enum

`.ordinal` is available on all enums.

<!-- test: enum-ordinal.error-enum-ordinal -->
```maxon
enum Shape
	circle
	square
end 'Shape'

function main() returns ExitCode
	let s = Shape.square
	return s.ordinal
end 'main'
```
```exitcode
1
```

### An accessor read off a struct FIELD of enum type

⛔⛔ **THE THREE ACCESSORS ARE MEMBERS OF THE VALUE, SO A FIELD HOLDING ONE SERVES THEM TOO — and shv2
refused that on its own source.** `token.kind.rawValue` (`Compiler/Lexer.maxon:340`, where `kind` is
declared `as TokenKind`) was `E2015: a field access through 'Token.kind', which is declared 'int' and not
a struct`. The message is true of the ERASURE — a declared enum erases to an integer, which is why
`typeTagName` prints its `named` tag as `int` — and false of the program: the field-chain walk had
consumed `rawValue` as if it were a second FIELD and asked what struct it lived in.

⭐ **THE MACHINERY WAS ALL THERE; ONLY THE ROUTING WAS MISSING.** `match b.c`, `b.c == Color.green` and
`let x = b.c` followed by `x.name` all already worked, because the loaded field value carries the
enum's `named(X)` tag. What did not work was reading the accessor directly off the field, and the cure
is the rule the walk already states for a `.member(` call: **a member that is not a FIELD ends the
chain** — the caller loads the field and the accessor is read off the loaded value, through the same
`emitEnumInstanceAccessor` the binding spelling (`s.ordinal`) and the type-qualified spelling
(`Op.doubleOp.rawValue`) hand their tag to. Three spellings, one answer.

⚠ **A `(` STILL MEANS A METHOD**, so an enum's real method is untouched: the admission rule excludes a
call, exactly as the type-qualified door does. And the field's DECLARED type is what decides — a struct
field is not diverted, which the last case below pins with a field literally named `name`.

<!-- test: enum-ordinal.accessors-on-a-struct-field -->
All three accessors, off a field of an int-backed enum, where `.ordinal` and `.rawValue` differ.
```maxon
enum Status
	ok = 200
	notFound = 404
end 'Status'

type Holder
	export let s as Status

	export static function create(s Status) returns Self
		return Self{s: s}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Status.notFound)
	print("name={h.s.name} ord={h.s.ordinal} raw={h.s.rawValue}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
name=notFound ord=1 raw=404
```

<!-- test: enum-ordinal.accessor-through-a-two-hop-field-chain -->
The chain still WALKS every struct field; it stops only at the enum-typed one, so a hop through an
intermediate struct is unchanged.
```maxon
enum Color
	red
	green
end 'Color'

type Inner
	export let c as Color

	export static function create(c Color) returns Self
		return Self{c: c}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	export static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(Color.red))
	print("chained={o.inner.c.name} ord={o.inner.c.ordinal}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
chained=red ord=0
```

<!-- test: enum-ordinal.a-struct-field-spelled-name-is-still-a-field -->
⭐ **THE DISJOINTNESS CASE, and it is the one that says the divert is keyed on the field's TYPE rather
than on the member's spelling.** `h.n.name` reads an ordinary `Integer` field that happens to be called
`name`, in the same program as `h.s.name` reading an enum accessor. Key the divert on the member and
this program answers `Named` for the second one.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Status
	ok = 200
	notFound = 404
end 'Status'

type Named
	export let name as Integer

	export static function create(name Integer) returns Self
		return Self{name: name}
	end 'create'
end 'Named'

type Holder
	export let s as Status
	export let n as Named

	export static function create(s Status, n Named) returns Self
		return Self{s: s, n: n}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Status.notFound, n: Named.create(7))
	print("enumAccessor={h.s.name} plainField={h.n.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
enumAccessor=notFound plainField=7
```
