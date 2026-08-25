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

Because `Shape.unionCases` is a regular enum it inherits `.allCases`, `.allCaseNames`, `.rawValue`, `.fromRawValue`, `.name`, `.ordinal`, and the synthesized `.hash()` / `.equals()` every enum gets. Match arms over a `Shape.unionCases` value are exhaustiveness-checked, just like match arms over the union itself.

The companion is minted by the union's own declaration, so its synthesized members are built once, by the file that declares the union, and reach exactly as far as the union does.

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

⚠ **THE FRONT END ACCEPTED THIS AND THE REGISTER ALLOCATOR DIED OF IT.** The three lowering
sites this section covers — construct, extract and write-back — named the slot `i64`
unconditionally, so a `float`
payload's bits, which live in an xmm register, were asked for a general-purpose home they never
had: `error E9001: RegisterManager: value %N has no register and no stack home`, printed with a
four-frame .NET stack trace, at the user. The slot's type is now taken from the value stored in it,
which is why the case below need not bind the payload to fail — `float-payload-constructed-without-binding`
is the CONSTRUCT alone. `maxon-shv2` refuses the same program cleanly with `E2015`;
`maxon-selfhosted` aborts with a `panic` (`IR/Maxon/LowerMaxonToStd.maxon:12528`). Of the three, only
this compiler ever accepted it.

⚠ **THERE IS A FOURTH CONSTRUCT SITE AND IT IS NOT ONE OF THESE THREE** — `U.fromName(…)`, which
reaches the slot through a runtime SELECT rather than a store. It had the same defect for a wider
set of payloads and is covered by its own section further down; the three named above are the ones
`maxon-selfhosted` guards.

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

### `fromName` writes the same slot, and it is a FOURTH construct site

`U.fromName("case", args…)` builds a union from a runtime string, and it reaches the payload slot
by a different route from `U.case(args…)`: it selects between the current slot contents and the new
value, so it writes through `StdSelectI64Op` rather than storing the argument directly.

⚠ **IT CAST EVERY PAYLOAD TO `StdI64` AND WROTE AN `i64` SLOT UNCONDITIONALLY**, so any payload
whose lowered value is not an `StdI64` died in the conversion with an unhandled .NET cast:
`E9001 ... Unable to cast object of type 'StdF64' to type 'StdI64'` for a float and
`... 'StdBool' to type 'StdI64'` for a bool, each with a four-frame stack trace at the user. It is
not a float question — the slot is eight bytes and every scalar payload has to be WIDENED into it,
which is what the direct construct has always done by storing at the value's own type. Both arms are
pinned below because fixing only the one that was reported would leave the other exactly as it was.

<!-- test: union-payload.from-name-writes-a-float-payload-slot -->
```maxon
typealias Fraction = float(0.0 to 1000.0)

union Sample
	blank
	measured(d Fraction)
end 'Sample'

function main() returns ExitCode
	let s = try Sample.fromName("measured", 0.5) otherwise Sample.blank
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

<!-- test: union-payload.from-name-writes-a-bool-payload-slot -->
The arm nobody reported. A `bool` payload is stored widened and read back with `!= 0`, so it needs
the same widening into the slot that a float needs — for a different reason and through the same
door.
```maxon
union Flagged
	blank
	set(b bool)
end 'Flagged'

function main() returns ExitCode
	let s = try Flagged.fromName("set", true) otherwise Flagged.blank
	let v = match s 'm'
		blank gives false
		set(b) gives b
	end 'm'
	if v 'yes'
		return 7
	end 'yes'
	return 1
end 'main'
```
```exitcode
7
```

### `fromName` with a HEAP payload

⛔ **THE SLOT WAS WRITTEN WITH AN UNRELATED NUMBER, AND THE UNION NEVER TOOK A REFERENCE.** `fromName` picks its
case at RUNTIME, so it writes every payload slot branchlessly — load the slot, `arith.select` between that and
the new value, store the result. Two things follow from that shape and neither was handled.

⚠ **A MANAGED PAYLOAD IS A SYMBOLIC HANDLE, NOT AN SSA VALUE.** An `StdHeapPtr` names the VARIABLE holding the
pointer and carries an id from the Maxon value space, so as a `select` operand it aliased whatever Std value
happened to share that id. MEASURED for `Named.fromName("titled", <String>)`: the operand printed as `%21` — the
call's own no-match flag — so the slot was written with the CONSTANT 1, and the first `mm_incref` through the
payload dereferenced it. The direct construct (`MaxonEnumConstructOp`) has always LOADED the pointer from its
variable first.

⚠ **AND THE REFERENCE THE UNION TAKES IS CONDITIONAL.** The direct construct increfs unconditionally because it
knows its case at compile time; this site does not, so the incref is selected on the same `isMatch` the slot's
own store is and goes through the null-guarded call. An unconditional one would leak the argument once per
non-matching case.

<!-- test: union-payload.from-name-writes-a-heap-payload-slot -->
```maxon
union Named
	blank
	titled(t String)
end 'Named'

function main() returns ExitCode
	let n = try Named.fromName("titled", "a payload long enough to be a real heap allocation") otherwise Named.blank
	let s = match n 'k'
		blank gives "blank"
		titled(t) gives t
	end 'k'
	print("{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a payload long enough to be a real heap allocation
```

<!-- test: union-payload.from-name-retains-only-the-matching-case-s-heap-payload -->
TWO cases carry a heap payload into the SAME slot, so the arm the runtime does not take is the one that has to
stay quiet: `subtitled` re-selects slot 0 after `titled` has written it, and an incref there would be a second
reference to a record with one owner. The exit code is the assertion — the runtime reports a surviving
allocation as 101.
```maxon
typealias Small = int(0 to 1000)

union Named
	blank
	titled(t String)
	subtitled(s String)
	numbered(n Small)
end 'Named'

function textOf(n Named) returns String
	return match n 'k'
		blank gives "blank"
		titled(t) gives t
		subtitled(s) gives s
		numbered(v) gives "num{v}"
	end 'k'
end 'textOf'

function main() returns ExitCode
	let a = try Named.fromName("titled", "a payload long enough to be a real heap allocation") otherwise Named.blank
	print("{textOf(a)}\n")
	let b = try Named.fromName("subtitled", "a second payload long enough to be a real heap allocation") otherwise Named.blank
	print("{textOf(b)}\n")
	let c = try Named.fromName("numbered", 7) otherwise Named.blank
	print("{textOf(c)}\n")
	let d = try Named.fromName("blank") otherwise Named.numbered(1)
	print("{textOf(d)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a payload long enough to be a real heap allocation
a second payload long enough to be a real heap allocation
num7
blank
```

⛔ **EVERY `fromName` CASE ABOVE PASSES AN `.rdata` STRING LITERAL, AND NONE OF THEM CAN SEE THE RETAIN AT
ALL.** A literal's record is image data the runtime never counts, so a union that writes the slot without
taking a reference balances by accident. MEASURED on this tree: with the conditional `mm_incref` deleted from
`LowerEnumFromNameAssociated`, all four cases above still PASS — the half of the ownership obligation they were
written for is the one that must NOT retain on a case the runtime did not take, and nothing held the half that
must retain on the case it did.

⭐ **THE DISCRIMINATING PAYLOAD IS A HEAP RECORD THE CALLER STILL OWNS A COUNTED REFERENCE TO**, which a
literal is not: an INTERPOLATED String and an Array literal bound to a local. Each half is separately
sufficient — measured, either one alone dies as `mm_decref: refcount underflow (already zero)`, exit 1, with
its own output already printed correctly, so the assertion is the exit code and not the text.

<!-- test: union-payload.from-name-retains-a-payload-the-caller-still-holds -->
```maxon
typealias Small = int(0 to 1000)
typealias SmallArray = Array with Small

union Named
	blank
	titled(t String)
	arrayed(xs SmallArray)
end 'Named'

function describe(n Named) returns String
	return match n 'k'
		blank gives "blank"
		titled(t) gives t
		arrayed(xs) gives "arrayed:{xs.count()}"
	end 'k'
end 'describe'

function main() returns ExitCode
	let n = 7
	let kept = "a payload long enough to be a real heap allocation {n}"
	let a = try Named.fromName("titled", kept) otherwise Named.blank
	print("{describe(a)}\n")
	print("{kept}\n")
	let xs = [1, 2, 3]
	let b = try Named.fromName("arrayed", xs) otherwise Named.blank
	print("{describe(b)}\n")
	print("{xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a payload long enough to be a real heap allocation 7
a payload long enough to be a real heap allocation 7
arrayed:3
3
```

### `.unionCases` has real `hash` and `equals`

⛔ **THE COMPANION IS A TYPE NO FILE DECLARES, AND FOR A LONG TIME THAT MEANT NO FILE BUILT ITS MEMBERS.** Its
`hash` and `equals` were registered as STUBS and nothing ever filled them in — the only thing that builds an
enum's bodies is `ParseEnumDecl`, and the companion is never "declared". The emitted module contained
`func @Shape.unionCases.equals(self: i64, other: i64) -> i1 { }` verbatim, so a call fell through into whatever
the emitter had placed next in `.text`.

⛔ **WHAT THAT LOOKED LIKE WAS A PROPERTY OF MODULE LAYOUT, NOT OF THE BUG.** A nil dereference, a stack
overflow and a correct-by-luck exit 0 were all observed from ONE compiler over three one-line variants, so a
case keyed on the panic would be a false-red generator wired to an unrelated property. These cases pin the
ANSWER, which is layout-independent. MEASURED before the fix: `circle.equals(square)` answered **true**.

⭐ **THE UNION'S OWN DECLARATION NOW MINTS THE COMPANION**, which is what makes the members buildable at all:
exactly one file owns them, so their bodies are built once and their reach is the union's own rather than
"whichever file wrote `U.unionCases` first". A synthesized member is as visible as the type it belongs to — see
`specs/export-keyword.md`'s *"A member the COMPILER wrote is as visible as the type it belongs to"* — and the
companion is no longer the exception it had to be while the bodies did not exist.

<!-- test: union-cases.companion-equals-and-hash -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(r Integer)
	square(s Integer)
	point
end 'Shape'

function main() returns ExitCode
	let a = Shape.unionCases.circle
	let a2 = Shape.unionCases.circle
	let b = Shape.unionCases.square
	let p = Shape.unionCases.point
	print("aa={a.equals(a2)} ab={a.equals(b)} ap={a.equals(p)}\n")
	print("h={a.hash()} hb={b.hash()} hp={p.hash()}\n")
	print("hashAgrees={a.hash() == a2.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aa=true ab=false ap=false
h=0 hb=1 hp=2
hashAgrees=true
```

<!-- test: union-cases.companion-equals-crosses-a-file-boundary -->
The companion is materialized by a file that is NOT the union's, and the call is in a THIRD. This used to be
`error.union-cases-companion-equals-is-not-callable-cross-file`, a deliberate carve-out holding the member
file-private because you cannot export one that does not exist. The bodies exist, so the fence is retired and
the member reaches exactly as far as `export union Shape` says.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

// --- file: b.maxon
export function tagOf(s Shape) returns Shape.unionCases
	return match s 'm'
		circle gives Shape.unionCases.circle
		square gives Shape.unionCases.square
	end 'm'
end 'tagOf'

// --- file: c.maxon
function main() returns ExitCode
	let a = tagOf(Shape.circle(1))
	let b = tagOf(Shape.square(2))
	if a.equals(b) 'same'
		return 1
	end 'same'
	if not a.equals(tagOf(Shape.circle(9))) 'differs'
		return 2
	end 'differs'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: union-cases.companion-equals-crosses-a-file-boundary-when-the-declaring-file-names-it-first -->
The same program with the DECLARING file naming `.unionCases` first. Both orders are kept because they take
different routes through the compiler, not because the answer could differ: here the declaring file's use site
reads back the companion its own pre-scan minted, and above a foreign parser mints the type itself. Their
answers agreeing is the point — "who asked first" once decided the member's reach.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

export function firstTag() returns Shape.unionCases
	return Shape.unionCases.circle
end 'firstTag'

// --- file: b.maxon
export function tagOf(s Shape) returns Shape.unionCases
	return match s 'm'
		circle gives Shape.unionCases.circle
		square gives Shape.unionCases.square
	end 'm'
end 'tagOf'

// --- file: c.maxon
function main() returns ExitCode
	let a = tagOf(Shape.circle(1))
	let b = tagOf(Shape.square(2))
	if a.equals(b) 'same'
		return 1
	end 'same'
	if not a.equals(firstTag()) 'differsFromFirst'
		return 2
	end 'differsFromFirst'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: union-cases.companion-equals-reaches-a-module-union-s-subtree -->
The `module` half of the same fact, and it needs its own case: the reach the rebuilt bodies take is
read back out of the parser's own tables, and reading only the exported one is half of a two-half
fact — the half that answers WRONG for a `module union`, calling its companion's members file-private
inside the very subtree the modifier opens. `Shape` is declared in one file of `api/` and its
discriminant compared in another.
```maxon
// --- file: api/shapes.maxon
typealias Integer = int(i64.min to i64.max)

module union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'

// --- file: api/tags.maxon
module function tagsDiffer() returns bool
	let a = Shape.unionCases.circle
	let b = Shape.unionCases.square
	return not a.equals(b)
end 'tagsDiffer'

// --- file: api/main.maxon
function main() returns ExitCode
	if tagsDiffer() 'differ'
		return 7
	end 'differ'
	return 1
end 'main'
```
```exitcode
7
```
