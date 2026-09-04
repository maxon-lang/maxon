---
feature: type-casting
status: stable
keywords: [cast, as, type, conversion, widening, narrowing]
category: type-system
---

# Type Casting

## Documentation

The `as` keyword performs safe type casting between Maxon's primitive types (`int`, `float`, `bool`).

### Safe Casts (Allowed)

Only widening casts that never lose data are permitted:

```text
int -> float      // 64-bit signed to 64-bit double (may lose precision for large values)
same -> same      // No-op (any type to itself)
```

Casts between ranged-int typealiases (e.g. `int(0 to u8.max)` to `int(i64.min to i64.max)`) are
always permitted, and they are REQUIRED: every `typealias` is a nominally distinct type, and `as` is
the only door between two of them, in both directions (`nominal-typealias.md`). Out-of-range literals
are rejected at compile time; out-of-range expressions are rejected at runtime by the target's range
check, which a widening cast provably cannot fail and so does not emit.

### Syntax

```text
expression as TargetType
```

### Examples

```text
typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
var b = 42 as Byte       // int literal in range (OK)
var i = b as Integer     // ranged int -> wider ranged int (OK)
var g = 100 as Real      // int -> float widening (OK)

// A cast TARGET must name a ranged typealias. The bare keyword is legal only as
// a `typealias` RHS, so `b as int` / `100 as float` are E3005, not casts:
//   Cannot cast to bare 'int'. Define a typealias with range constraints, ...
```

### Unsafe Casts (Compile Error E3009)

Lossy conversions are not allowed. The compiler reports error E3009:

```text
var i = 5.0 as Integer   // ERROR: use trunc/round/floor/ceil instead
var i = true as Integer  // ERROR: bool -> int not allowed
var f = true as Real     // ERROR: bool -> float not allowed
var b = 0 as bool        // ERROR: int -> bool not allowed
var b = 0.0 as bool      // ERROR: float -> bool not allowed

// Each target names a declared alias. A BARE `int`/`float` target is refused
// earlier and by a different rule (E3005), so it never reaches E3009 at all.
```

For float-to-integer conversion, use the explicit conversion functions:
- `trunc(x)` -- truncate toward zero
- `round(x)` -- round to nearest
- `floor(x)` -- round toward negative infinity
- `ceil(x)` -- round toward positive infinity

## Tests

### Safe Casts

<!-- test: int-literal-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	return b
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-zero-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 0 as Byte
	return b
end 'main'
```
```exitcode
0
```

<!-- test: int-literal-max-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 255 as Byte
	return b
end 'main'
```
```exitcode
255
```

<!-- test: byte-to-int -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 100 as Byte
	return b
end 'main'
```
```exitcode
100
```

<!-- test: byte-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)

function toFloat(b Byte) returns Float
	return b + 0.0
end 'toFloat'

function main() returns ExitCode
	let b = 50 as Byte
	let f = toFloat(b)
	return trunc(f)
end 'main'
```
```exitcode
50
```

<!-- test: int-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let x = 42
	let f = x as Float
	return trunc(f)
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let f = 99 as Float
	return trunc(f)
end 'main'
```
```exitcode
99
```

<!-- test: cast-in-expression -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 10 as Byte
	let result = b + 32
	return result
end 'main'
```
```exitcode
42
```

<!-- test: chained-byte-int-float -->
```maxon

typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)

function toFloat(b Byte) returns Float
	return b + 0.0
end 'toFloat'

function main() returns ExitCode
	let b = 25 as Byte
	let f = toFloat(b)
	return trunc(f)
end 'main'
```
```exitcode
25
```

### Unsafe Casts (Compile Errors)

<!-- test: error.int-literal-out-of-range -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let x = 256 as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-casting/error.int-literal-out-of-range.test:6:14: Value 256 is outside the range of 'Byte' (int(0 to 255))
```

<!-- test: error.negative-literal-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let x = -1 as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-casting/error.negative-literal-to-byte.test:6:13: Value -1 is outside the range of 'Byte' (int(0 to 255))
```

<!-- test: error.float-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 5.0 as Integer
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.float-to-int.test:6:14: Cannot cast from float to int
```

<!-- test: error.bool-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let b = true
	let x = b as Integer
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-int.test:7:12: Cannot cast from bool to int
```

<!-- test: error.bool-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let b = true
	let x = b as Float
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-float.test:7:12: Cannot cast from bool to float
```

<!-- test: error.bool-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = true
	let x = b as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-byte.test:7:12: Cannot cast from bool to int
```

<!-- test: error.int-to-bool -->
```maxon
function main() returns ExitCode
	let x = 0 as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.int-to-bool.test:3:12: Cannot cast from int to bool
```

<!-- test: error.float-to-bool -->
```maxon
function main() returns ExitCode
	let x = 0.0 as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.float-to-bool.test:3:14: Cannot cast from float to bool
```

<!-- test: error.byte-to-bool -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let x = b as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.byte-to-bool.test:7:12: Cannot cast from int to bool
```

### Unneeded Casts (Compile Error E3010)

A cast whose target names the value's OWN alias is rejected: it converts nothing.
A cast naming a DIFFERENT alias is real work whichever way the two ranges run — it
re-declares the value's type and carries the target's range check — so it compiles.

<!-- test: error.unneeded.same-type-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let x = identity(42)
	let y = x as Integer
	return y
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-int.test:11:12: unneeded cast: 'Integer' already fits in 'Integer'
```

<!-- test: error.unneeded.same-type-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function identity(x Float) returns Float
	return x
end 'identity'

function main() returns ExitCode
	let f = identity(42.0)
	let g = f as Float
	return trunc(g)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-float.test:11:12: unneeded cast: 'Float' already fits in 'Float'
```

<!-- test: error.unneeded.same-type-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let c = b as Byte
	return c
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-byte.test:7:12: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: error.unneeded.same-alias-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let c = b as Byte
	return c
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-byte.test:7:12: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: error.unneeded.same-alias-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let x = identity(42)
	let y = x as Integer
	return y
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-int.test:11:12: unneeded cast: 'Integer' already fits in 'Integer'
```

<!-- test: error.unneeded.same-alias-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function identity(x Float) returns Float
	return x
end 'identity'

function main() returns ExitCode
	let f = identity(42.0)
	let g = f as Float
	return trunc(g)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-float.test:11:12: unneeded cast: 'Float' already fits in 'Float'
```

<!-- test: widening-byte-to-integer-is-a-real-cast -->
`Byte` fits inside `Integer`, and that is not what decides: the two are different types, so `b as
Integer` is the cast that lets a `Byte` reach an `Integer` parameter at all.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function asInteger(x Integer) returns Integer
	return x
end 'asInteger'

function main() returns ExitCode
	let b = 42 as Byte
	let i = b as Integer
	return asInteger(i)
end 'main'
```
```exitcode
42
```

### A RANGE-CONTESTED alias is quoted as SOURCE spells it, here as everywhere

`typealias Byte = int(0 to 200)` beside an `Array with Byte` CONTESTS the stdlib's own `Byte`, and a
contested name is stored under the compiler's mint (`Byte$0_200`) so the two declarations can occupy one
registry. That spelling names no line the author wrote, so every alias name a diagnostic prints goes
through `sourceSpelledAliasName` — "quoted as source spells it" is a property of the diagnostic
VOCABULARY and not of one E-code. E3010 prints both sides of the cast through it.

<!-- test: error.unneeded.contested-alias-quoted-as-source-spells-it -->
```maxon

typealias Byte = int(0 to 200)
typealias Bytes = Array with Byte

function takes(b Bytes) returns Byte
	return (try b.get(0) otherwise 0) as Byte
end 'takes'

function main() returns ExitCode
	var a = Bytes.create()
	a.push(7)
	return takes(a)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.contested-alias-quoted-as-source-spells-it.test:7:36: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: a-contested-alias-cast-to-another-alias-is-a-real-cast -->
The legal twin: the contested `Byte` reaches a `Wide` through the one door there is.
```maxon

typealias Byte = int(0 to 200)
typealias Bytes = Array with Byte
typealias Wide = int(0 to 1000)

function takes(b Bytes) returns Wide
	return (try b.get(0) otherwise 0) as Wide
end 'takes'

function main() returns ExitCode
	var a = Bytes.create()
	a.push(7)
	return takes(a)
end 'main'
```
```exitcode
7
```

<!-- test: widening-int-to-float-is-a-real-cast -->
An `Integer` into a float alias is a conversion AND a change of type, and `as` is how both are spelled.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias JsonFloat = float(f64.min to f64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let i = identity(42)
	let f = i as JsonFloat
	return trunc(f)
end 'main'
```
```exitcode
42
```

<!-- test: error.unneeded.call-result-same-alias -->
```maxon

typealias Score = int(0 to 100)

function getScore() returns Score
	return 42
end 'getScore'

function main() returns ExitCode
	let result = getScore() as Score
	return result
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.call-result-same-alias.test:10:26: unneeded cast: 'Score' already fits in 'Score'
```

E3010 is a recoverable diagnostic: the parser keeps walking the function so every
unneeded cast in the file is reported in a single compile, not just the first.

<!-- test: error.unneeded.multiple-in-one-function -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let a = 1 as Byte
	let b = a as Byte
	let c = b as Byte
	let d = c as Byte
	return d
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.multiple-in-one-function.test:7:12: unneeded cast: 'Byte' already fits in 'Byte'
error E3010: specs/fragments/type-casting/error.unneeded.multiple-in-one-function.test:8:12: unneeded cast: 'Byte' already fits in 'Byte'
error E3010: specs/fragments/type-casting/error.unneeded.multiple-in-one-function.test:9:12: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: error.unneeded.multiple-across-functions -->
```maxon

typealias Byte = int(0 to u8.max)

function first(b Byte) returns Byte
	let x = b as Byte
	return x
end 'first'

function second(b Byte) returns Byte
	let y = b as Byte
	return y
end 'second'

function main() returns ExitCode
	let a = 1 as Byte
	let b = first(a)
	let c = second(b)
	return c
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.multiple-across-functions.test:6:12: unneeded cast: 'Byte' already fits in 'Byte'
error E3010: specs/fragments/type-casting/error.unneeded.multiple-across-functions.test:11:12: unneeded cast: 'Byte' already fits in 'Byte'
```

### Cast applied to a control-flow expression

<!-- test: cast-on-ternary-expression -->
```maxon
typealias Mask = int(i64.min to i64.max)

// The cast operand is a ternary whose result is defined in a merge block, so
// `currentBlock` advances past the block the cast started in. The `convert`
// (and its `castCheck`) must emit into the merge block where the operand value
// is bound — emitting into the original block referenced the value before its
// definition and tripped E3013 "unresolved value name" at lowering.
function maskFor(negate bool) returns Mask
	return (1 if negate else (-1)) as Mask
end 'maskFor'

function main() returns ExitCode
	let m = maskFor(false)
	if m < 0 'neg'
		return 2
	end 'neg'
	return 1
end 'main'
```
```exitcode
2
```

### E3010 through a MERGED BINDING — the two joins whose incoming set is complete

⭐⭐ **A MERGE MAY NOT SPEND ITS DECLARED ALIAS AS A PROOF (G14), BUT IT MUST STILL SPEND IT WHERE EVERY
EDGE PROVES IT — AND THESE TWO CASES ARE THE DIFFERENCE.** G14 withheld the ranged-alias claim from every
phi, because a LOOP HEADER cannot answer: its back edge is not parsed when it is minted, so nothing at the
mint can see what will reach it. An `if` continuation and a `match`'s carried binding are not loop headers
— both predecessors are branched in one statement after the phi is minted — and withholding there cost two
things at once, neither visible in a green suite: the `return` below grew a range cascade over a value the
two entry guards had already proved, and this E3010 stopped being reported at all while the bootstrap
oracle went on reporting it. MEASURED both ways on 2026-08-03 (G14 review): shv2 compiled and ran these
two programs, `maxon-sharp` refused them at the same line and column.

<!-- test: error.unneeded.if-merged-binding-same-alias -->
```maxon
typealias Num = int(0 to 1000)

function pick(c bool, a Num, b Num) returns Num
	var t = a
	if c 'x'
		t = b
	end 'x'
	return t as Num
end 'pick'

function main() returns ExitCode
	return pick(true, a: 1, b: 2)
end 'main'
```
```maxoncstderr
error E3010: <fragment>:9:11: unneeded cast: 'Num' already fits in 'Num'
```

<!-- test: error.unneeded.match-carried-binding-same-alias -->
```maxon
typealias Num = int(0 to 1000)

enum K
	a
	b
end 'K'

function pick(k K, x Num, y Num) returns Num
	var t = x
	match k 'm'
		a then t = y
		b then break 'm'
	end 'm'
	return t as Num
end 'pick'

function main() returns ExitCode
	return pick(K.a, x: 1, y: 2)
end 'main'
```
```maxoncstderr
error E3010: <fragment>:15:11: unneeded cast: 'Num' already fits in 'Num'
```

### The SOURCES that state a declared alias — a field read and a `try` result

⭐⭐ **E3010's gate is "the cast names the value's OWN alias", and the question that gate turns on is
WHICH EXPRESSIONS STATE ONE.** The cases above all read a local. A struct field states the alias it is
DECLARED with, and a `try … otherwise` result states its callee's declared return type. Each door is
pinned twice: the same-alias cast E3010 refuses, and the cross-alias cast through the same door that is
the one legal way across.

<!-- test: error.unneeded.through-a-struct-field-read -->
```maxon
typealias Narrow = int(0 to u32.max)

type Segment
	export let base as Narrow

	export static function create(base Narrow) returns Self
		return Self{base: base}
	end 'create'
end 'Segment'

function main() returns ExitCode
	let seg = Segment.create(7)
	let w = seg.base as Narrow
	return w as ExitCode
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.through-a-struct-field-read.test:14:19: unneeded cast: 'Narrow' already fits in 'Narrow'
```

<!-- test: a-struct-field-read-cast-to-another-alias-is-a-real-cast -->
```maxon
typealias Narrow = int(0 to u32.max)
typealias Wide = int(i64.min to i64.max)

type Segment
	export let base as Narrow

	export static function create(base Narrow) returns Self
		return Self{base: base}
	end 'create'
end 'Segment'

function main() returns ExitCode
	let seg = Segment.create(7)
	let w = seg.base as Wide
	return w as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.unneeded.through-a-try-otherwise-result -->
`ByteArray.get` returns the stdlib's `Byte`, and the `try … otherwise` result states it.
```maxon
function main() returns ExitCode
	let bytes = b"A"
	let ch = try bytes.get(0) otherwise panic("get")
	let w = ch as Byte
	return (w - 23) as ExitCode
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.through-a-try-otherwise-result.test:5:13: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: a-try-otherwise-result-cast-to-another-alias-is-a-real-cast -->
```maxon
typealias Wide = int(i64.min to i64.max)

function main() returns ExitCode
	let bytes = b"A"
	let ch = try bytes.get(0) otherwise panic("get")
	let w = ch as Wide
	return (w - 23) as ExitCode
end 'main'
```
```exitcode
42
```

### An int alias and a float alias are two types, and the promotion does not cross them

<!-- test: error.an-int-alias-does-not-reach-a-float-alias-parameter -->
An `Integer` is not a `Ratio`. The int→float promotion is a conversion of the VALUE; it does not change
which type the argument carries, so the argument door refuses it as it refuses any other alias mismatch.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ratio = float(f64.min to f64.max)

function identity(n Integer) returns Integer
	return n
end 'identity'

function showRatio(r Ratio)
	print("r={r}")
end 'showRatio'

function main() returns ExitCode
	showRatio(identity(42))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:2: argument type mismatch for 'r': expected 'Ratio', got 'Integer'
```

<!-- test: an-int-alias-reaches-a-float-alias-parameter-through-as -->
The legal twin, both kinds of crossing: an `Integer` into a `Ratio` parameter, and a `Narrow` into an
`Integer` one. The int→float cast converts the value AND re-declares its type; the int→int cast re-declares
only, and neither is E3010.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ratio = float(f64.min to f64.max)
typealias Narrow = int(0 to u32.max)

function identity(n Integer) returns Integer
	return n
end 'identity'

function showRatio(r Ratio)
	print("r={r}")
end 'showRatio'

function showWide(v Integer)
	print("v={v}")
end 'showWide'

function narrow(n Narrow) returns Narrow
	return n
end 'narrow'

function main() returns ExitCode
	showRatio(identity(42) as Ratio)
	showWide(narrow(7) as Integer)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=42.0v=7
```

## A DIFFERENT CONTAINER INSTANCE IS NOT A CAST TARGET

A cast to another alias of the value's OWN generic instance is a re-brand and costs nothing
(`nominal-generic-alias.md`). A cast to a DIFFERENT instance is refused, and it cannot be a retagging:
`Array with int(0 to u64.max)` strides EIGHT bytes per element and `Array with int(0 to 63)` strides ONE
(`rangedAliasStorageBytes`), so retagging one as the other hands every later read a stride the buffer was not
written at — the silent wrong answer `bytearray-element-size.md` measures at length. Converting instead would
mean a second buffer and a range check per element: a real operation, and not one an `as` should perform in
silence.

⚠ **THE BOOTSTRAP REFUSES THE SYNTAX OUTRIGHT** (`E2003: Expected type name after 'as'`), so there is no
reference answer to match — only a reference REFUSAL, which this agrees with while saying why.

<!-- test: error.a-generic-instance-is-not-a-cast-target -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 63)
typealias WideCol = Array with Wide
typealias NarrowCol = Array with Narrow

function widthOf(c NarrowCol) returns ExitCode
	return c.count() as ExitCode
end 'widthOf'

function main() returns ExitCode
	var w = WideCol.create()
	w.push(5)
	let n = w as NarrowCol
	return widthOf(n)
end 'main'
```
```maxoncstderr
error E3131: specs/fragments/type-casting/error.a-generic-instance-is-not-a-cast-target.test:14:12: Cannot cast to 'NarrowCol': a container's elements have a storage layout of their own, so 'WideCol' cannot be retagged as one — build the container with the element type you need, or convert it element by element
```

<!-- test: a-scalar-alias-is-still-a-cast-target -->
⭐ **THE CONTROL, AND IT IS WHAT SAYS THE REFUSAL DID NOT WIDEN.** The refusal reads the tag the cast TARGET
denotes, so a ranged alias — the overwhelmingly common `as` target — is untouched, in both directions: the
narrowing leg (`w as Narrow`) keeps its guard, and the widening leg (`n as Wide`) is a real cast between
two types and not E3010. Lose this and every `as` in the corpus would be refused with it.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 63)

function main() returns ExitCode
	let w = 5 as Wide
	let n = w as Narrow
	let back = n as Wide
	print("n={n} back={back}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=5 back=5
```
