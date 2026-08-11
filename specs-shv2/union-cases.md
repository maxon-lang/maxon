---
feature: union-cases
status: experimental
keywords: [union, unionCases, discriminant, exhaustive, match, serialization]
category: type-system
---

## Documentation

# Union unionCases

Every `union` with associated values has a compiler-synthesized companion type `U.unionCases` — a simple enum with one bare case per variant of `U`, in declaration order. It exposes the union's discriminant as a first-class enum value so reader/decoder code can match exhaustively on the tag.

```text
union Shape
  circle(radius i64)
  square(side i64)
  point
end 'Shape'

// Shape.unionCases is conceptually:
//   enum Shape.unionCases
//     circle    // rawValue 0
//     square    // rawValue 1
//     point     // rawValue 2
//   end
```

Because `Shape.unionCases` is a regular enum it inherits `.allCases`, `.allCaseNames`, `.rawValue`, `.fromRawValue`, `.name`, and `.ordinal`. Match arms over a `Shape.unionCases` value are exhaustiveness-checked, just like match arms over the union itself.

The intended use is symmetric (de)serialization: write the variant's `rawValue` to a buffer alongside its payload; on read, lift the raw `int` back to a `U.unionCases` via `fromRawValue` and match on it to dispatch the payload reader. Adding a new variant to the union forces a non-exhaustive-match build error in *both* writer and reader.

`.unionCases` is only synthesized for unions with associated values. Plain enums (no payloads) already expose `.allCases` / `.fromRawValue` directly.

## Tests

### Basic case construction

<!-- test: union-cases.basic-construct -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	let c = Shape.unionCases.circle
	print("{c.name}={c.rawValue}\n")
	let s = Shape.unionCases.square
	print("{s.name}={s.rawValue}\n")
	let p = Shape.unionCases.point
	print("{p.name}={p.rawValue}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle=0
square=1
point=2
```

### allCases iteration

<!-- test: union-cases.allcases-iteration -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	for kase in Shape.unionCases.allCases 'loop'
		print("{kase.name}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle
square
point
```

### fromRawValue round-trip

<!-- test: union-cases.fromrawvalue-roundtrip -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function main() returns ExitCode
	let k0 = try Shape.unionCases.fromRawValue(0) otherwise return 1
	let k1 = try Shape.unionCases.fromRawValue(1) otherwise return 2
	let k2 = try Shape.unionCases.fromRawValue(2) otherwise return 3
	print("{k0.name}\n")
	print("{k1.name}\n")
	print("{k2.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle
square
point
```

### Exhaustive match dispatch

<!-- test: union-cases.match-exhaustive -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

function describe(k Shape.unionCases) returns Integer
	match k 'tag'
		circle then return 100
		square then return 200
		point then return 300
	end 'tag'
end 'describe'

function main() returns ExitCode
	let c = Shape.unionCases.circle
	let s = Shape.unionCases.square
	let p = Shape.unionCases.point
	let total = describe(c) + describe(s) + describe(p)
	if total == 600 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Accessors on a runtime union value

<!-- test: union-cases.runtime-accessors -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

// `.name` / `.rawValue` / `.ordinal` read off a RUNTIME payload-bearing union
// value (a parameter) — distinct from the compile-time companion access
// (`Shape.unionCases.circle`). The value is a heap box whose i64 tag sits at
// offset 0, so the accessor must load that tag before its ordinal-keyed
// lookup. Without the load the box pointer was used as the ordinal: `.name`
// fell through to the last case ("point") for every input and `.rawValue` /
// `.ordinal` returned the pointer.
function nameOf(sh Shape) returns String
	return sh.name
end 'nameOf'

function tagOf(sh Shape) returns Integer
	return sh.rawValue
end 'tagOf'

function ordOf(sh Shape) returns Integer
	return sh.ordinal
end 'ordOf'

function main() returns ExitCode
	let a = Shape.circle(5)
	let b = Shape.square(9)
	let c = Shape.point
	print("{nameOf(a)}/{tagOf(a)}/{ordOf(a)}\n")
	print("{nameOf(b)}/{tagOf(b)}/{ordOf(b)}\n")
	print("{nameOf(c)}/{tagOf(c)}/{ordOf(c)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
circle/0/0
square/1/1
point/2/2
```

### A payload slot has a declared type, and its IDENTITY is part of it

A case's associated value is declared with a type, so an argument to that case obeys the same rule
every other declared place applies. That check compared only the value's KIND — and `Struct`,
`Enum` and `Function` are each one kind covering every type of that shape — so a payload declared
`Color` accepted any enum, any struct, or any function whatsoever.

<!-- test: union-payload.error.wrong-enum -->
`tint`'s payload is declared `Color` and is handed a `Shade`. This compiled clean and the binding
`c` came back out as a `Shade` ordinal wearing `Color`'s name.
```maxon
enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

union Paint
	tint(c Color)
end 'Paint'

function main() returns ExitCode
	let p = Paint.tint(Shade.light)
	match p 'go'
		tint(c) then return c.ordinal
	end 'go'
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/union-cases/union-payload.error.wrong-enum.test:17:32: type mismatch: 'expected Color, got Shade'
```

<!-- test: union-payload.error.wrong-struct -->
The same hole one kind over: a payload declared `Color` accepting a `Shade` struct. Both are
"a struct", so the kind check agreed, and `c.v` read `Shade`'s field out of `Color`'s layout.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

union Paint
	tint(c Color)
end 'Paint'

function main() returns ExitCode
	let p = Paint.tint(Shade.create(7))
	match p 'go'
		tint(c) then return c.v
	end 'go'
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/union-cases/union-payload.error.wrong-struct.test:25:36: type mismatch: 'expected Color, got Shade'
```

<!-- test: union-payload.matching-payload-types -->
The control: the declared types, passed. Both a struct and an enum payload still construct, match
and read back.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Shade
	dark
	light
end 'Shade'

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

union Paint
	tint(c Color)
	wash(s Shade)
end 'Paint'

function readOne(p Paint) returns Integer
	match p 'go'
		tint(c) then return c.v
		wash(s) then return s.ordinal
	end 'go'
end 'readOne'

function main() returns ExitCode
	return readOne(Paint.tint(Color.create(4))) + readOne(Paint.wash(Shade.light))
end 'main'
```
```exitcode
5
```


## shv2 additions

### The write/read round-trip this file's Documentation describes, COMPOSED

<!-- test: union-cases.rawvalue-companion-roundtrip -->
The Documentation states the companion's purpose: *"write the variant's `rawValue` to a buffer alongside its
payload; on read, lift the raw `int` back to a `U.unionCases` via `fromRawValue`"*. Both halves are pinned
above — `union-cases.runtime-accessors` reads a union's `rawValue`, `union-cases.fromrawvalue-roundtrip`
lifts an `int` — but **nothing composed them**, and the composition is the feature.

⛔ It was BROKEN the moment a boxed union could renumber its cases. The companion builder hard-coded
`tag: i, rawValue: "{i}"`, which was the union's own tag spelled differently only while every boxed union's
tags were `0..n-1`. On the union below, `op.rawValue` answered **5** while `Instr.unionCases.add.rawValue`
answered **0**, so the `fromRawValue` on the next line THREW. ⚠ The C# bootstrap has the identical defect,
so the oracle does not adjudicate it; v1 does, and states the rule — *"Mirror the parent case's ordinal so
`.rawValue` agrees with the union's tag value (the natural pairing for de/serialization)"*.

The three accessors must agree: `rawValue` is the union's TAG, `ordinal` is the DECLARATION INDEX, and they
are different numbers here on purpose.
```maxon
typealias ID = int(i64.min to i64.max)

union Instr
	add(dest ID, src ID) = 5
	nop = 7
end 'Instr'

function main() returns ExitCode
	let op = Instr.add(1, src: 2)
	let k = Instr.unionCases.add

	print("{op.rawValue} {k.rawValue} {k.ordinal} {k.name}\n")

	// The round-trip: the union's own tag, lifted back through the companion.
	let back = try Instr.unionCases.fromRawValue(op.rawValue) otherwise return 42
	match back 'dispatch'
		add then print("add\n")
		nop then print("nop\n")
	end 'dispatch'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5 0 add
add
```

### A non-integer backing keeps the TAG round-trip

<!-- test: union-cases.companion-mirrors-backing -->
The companion mirrors the union's backing only where the tag IS the raw value. For `float` and `character`
the tag is an IEEE bit pattern / a codepoint, so the backing travels with it and `.rawValue` decodes to the
written value. For a `string` backing the raw text rides a column the companion does not carry, so its tag
is the declaration index and `integer` is what that index IS — copying `string` there would have made
`.rawValue` answer the text of its own tag (`"0"`) instead of `"alpha"`.
```maxon
typealias ID = int(i64.min to i64.max)

union F
	a(x ID) = 1.5
	b = 2.5
end 'F'

union C
	m(x ID) = 'm'
	n = 'n'
end 'C'

union S
	p(x ID) = "alpha"
	q = "beta"
end 'S'

function main() returns ExitCode
	let f = F.a(1)
	let c = C.m(1)
	let s = S.p(1)
	print("{f.rawValue} {F.unionCases.a.rawValue}\n")
	print("{c.rawValue} {C.unionCases.m.rawValue}\n")
	print("{s.rawValue} {S.unionCases.p.rawValue} {S.unionCases.q.ordinal}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.5 1.5
m m
alpha 0 1
```
