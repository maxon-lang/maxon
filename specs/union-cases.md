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

### A `float` payload slot

The flat payload slot is 8 bytes and every kind of payload shares it, so a `float` payload is a
question about the slot's TYPE, not about its size.

⚠ **THE FRONT END ACCEPTED THIS AND THE REGISTER ALLOCATOR DIED OF IT.** All three lowering
sites — construct, extract and write-back — named the slot `i64` unconditionally, so a `float`
payload's bits, which live in an xmm register, were asked for a general-purpose home they never
had: `error E9001: RegisterManager: value %N has no register and no stack home`, printed with a
four-frame .NET stack trace, at the user. The slot's type is now taken from the value stored in it,
which is why the case below need not bind the payload to fail — `float-payload-constructed-without-binding`
is the CONSTRUCT alone. `maxon-shv2` refuses the same program cleanly with `E2015`;
`maxon-selfhosted` aborts with a `panic` (`IR/Maxon/LowerMaxonToStd.maxon:12528`). Of the three, only
this compiler ever accepted it.

<!-- test: union-payload.float-payload-constructed-without-binding -->
The narrowest form: the payload is never bound, and the arms name no variable. Constructing the
value is enough.
```maxon
typealias Fraction = float(0.0 to 1000.0)
typealias Reading = int(0 to 255)

union Sample
	blank
	measured(d Fraction)
end 'Sample'

function take(s Sample) returns Reading
	return match s 'm'
		blank gives 1
		measured gives 2
	end 'm'
end 'take'

function main() returns ExitCode
	return take(Sample.measured(0.5))
end 'main'
```
```exitcode
2
```

<!-- test: union-payload.float-payload-round-trips-through-the-slot -->
The value stored is the value read back, to the bit — the slot is a reinterpretation of the same
eight bytes, not a conversion through an integer.
```maxon
typealias Fraction = float(0.0 to 1000.0)

union Sample
	blank
	measured(d Fraction)
end 'Sample'

function main() returns ExitCode
	let s = Sample.measured(0.5)
	let d = match s 'm'
		blank gives 0.0
		measured(v) gives v
	end 'm'
	if d == 0.5 'exact'
		return 7
	end 'exact'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: union-payload.float-payload-beside-an-int-payload -->
Two cases whose payloads occupy the SAME slot at different types. The slot is written and read at
whichever type the case declares, so the int case is unaffected by the float one sharing its offset.
```maxon
typealias Fraction = float(0.0 to 1000.0)
typealias Integer = int(i64.min to i64.max)

union Sample
	counted(n Integer)
	measured(d Fraction)
end 'Sample'

function main() returns ExitCode
	let a = Sample.counted(5)
	let b = Sample.measured(0.25)
	let n = match a 'ma'
		counted(v) gives v
		measured gives 0
	end 'ma'
	let d = match b 'mb'
		counted gives 0.0
		measured(v) gives v
	end 'mb'
	if d == 0.25 'exact'
		return n + 2
	end 'exact'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: union-payload.f32-ranged-float-payload-occupies-the-slot-as-a-double -->
An `f32`-ranged alias is the only way to spell a 32-bit float — bare `float32` is not a type — and
it changes nothing here: the slot is eight bytes and the value arrives already lowered as a double,
so the slot is written and read at `f64` like any other float payload. Pinned because the lowering
rule says so in words, and a rule stated in a comment cannot fail.
```maxon
typealias Tiny = float(f32.min to f32.max)

union Sample
	blank
	measured(d Tiny)
end 'Sample'

function main() returns ExitCode
	let v = 0.5 as Tiny
	let s = Sample.measured(v)
	let d = match s 'm'
		blank gives 0.0
		measured(x) gives x
	end 'm'
	if d == 0.5 'exact'
		return 7
	end 'exact'
	return 1
end 'main'
```
```exitcode
7
```
