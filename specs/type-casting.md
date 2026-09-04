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
always permitted; out-of-range literals are rejected at compile time, and out-of-range expressions
are rejected at runtime by the function-return range check.

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

<!-- test: widening-int-to-float-is-a-real-cast -->
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
	return (1 if negate else (0 - 1)) as Mask
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

## E3010 SEES A DECLARED ALIAS THROUGH A FIELD READ AND A `try … otherwise` RESULT (R-4)

⭐⭐ **BOTH COMPILERS ASK THE SAME QUESTION OF WHAT *STATES* A DECLARED ALIAS.** `TryGetSourceRangedType`
reads three sources: a direct variable reference, the alias the EXPRESSION ITSELF states (a struct FIELD read,
a `try … otherwise` RESULT), and a snapshot that carries chained casts and call returns. A door it cannot see
through goes silent, and a cast to the value's own alias then passes for one that does work — which is why each
door below is pinned twice: the same-alias cast E3010 refuses, and the cross-alias cast that compiles.

⚠ **THE NAME RIDES THE EXPRESSION RESULT, NOT `_lastRangedTypeName`, AND THAT IS MEASURED.** The obvious
route is the existing channel — but it is NOT diagnostic-only: a shift reads it to choose arithmetic vs logical
fill, and the optimal-type scan reads it to size a value. **Writing a field read's name there moves two
`per-instance-typealias` goldens (`x64.mov r8, 2` becomes `x64.mov r8, 1`)** — emitted code changing for a rule
that only ever refuses. On `ExprResult.Direct` there is no staleness to manage either: the name belongs to that
expression and dies with it.

⚠ **THE FIELD READ RESOLVES THROUGH THE OWNER'S PER-INSTANCE ALIASES**, exactly as a call return does. A field
declared `Idx` inside `StrWrapper` denotes `StrWrapper__Idx` at the read, so handing back the declaration's own
name would put two different type names on the two sides of one cast.

<!-- test: error.unneeded.through-a-field-read -->
```maxon
typealias Narrow = int(0 to 63)

type Holder
	export var n as Narrow

	export static function make() returns Holder
		return Self{n: 5}
	end 'make'
end 'Holder'

function main() returns ExitCode
	let h = Holder.make()
	let w = h.n as Narrow
	print("w={w}")
	return 0
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.through-a-field-read.test:14:14: unneeded cast: 'Narrow' already fits in 'Narrow'
```

<!-- test: a-field-read-cast-to-another-alias-is-a-real-cast -->
```maxon
typealias Narrow = int(0 to 63)
typealias Mid = int(0 to 1000)

type Holder
	export var n as Narrow

	export static function make() returns Holder
		return Self{n: 5}
	end 'make'
end 'Holder'

function main() returns ExitCode
	let h = Holder.make()
	let w = h.n as Mid
	print("w={w}")
	return 0
end 'main'
```
```stdout
w=5
```

<!-- test: error.unneeded.through-a-try-otherwise-result -->
⚠ **THE PARENTHESES ARE LOAD-BEARING AND THEY RECORD A DIVERGENCE.** Written bare, `try pick(true) otherwise 0
as Narrow`, the two compilers PARSE it differently: this one reads `(try … otherwise 0) as Narrow` — `as` binding
looser than `otherwise` — while shv2 reads `try … otherwise (0 as Narrow)`, the cast binding to its operand.
Parenthesized, both answer the same diagnostic at the same position, which is what this case pins. **The bare
form's precedence is a real disagreement and is filed, not fixed here** — shv2's tighter reading is the
conventional one for a cast.
```maxon
typealias Narrow = int(0 to 63)

enum PickError implements Error
	nope
end 'PickError'

function pick(ok bool) returns Narrow throws PickError
	if not ok 'bad'
		throw PickError.nope
	end 'bad'
	return 5
end 'pick'

function main() returns ExitCode
	let w = (try pick(true) otherwise 0) as Narrow
	print("w={w}")
	return 0
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.through-a-try-otherwise-result.test:16:39: unneeded cast: 'Narrow' already fits in 'Narrow'
```

<!-- test: a-try-otherwise-result-cast-to-another-alias-is-a-real-cast -->
```maxon
typealias Narrow = int(0 to 63)
typealias Mid = int(0 to 1000)

enum PickError implements Error
	nope
end 'PickError'

function pick(ok bool) returns Narrow throws PickError
	if not ok 'bad'
		throw PickError.nope
	end 'bad'
	return 5
end 'pick'

function main() returns ExitCode
	let w = (try pick(true) otherwise 0) as Mid
	print("w={w}")
	return 0
end 'main'
```
```stdout
w=5
```

## E3010 SEES A DECLARED ALIAS THROUGH A UNION PAYLOAD BINDING TOO — THE THIRD DOOR OF THE SAME FAMILY

⛔ **A `match` ARM'S PAYLOAD BINDING MUST CARRY ITS DECLARED ALIAS, AND MISSING IT COSTS TWICE.** A binding
that records a declared type name only for a struct or an enum drops a ranged alias: the door above goes SILENT
for it, and — nothing having examined the cast — a cross-kind `int` → `float` cast reaches the backend
unprepared and dies there as `E9001: Unable to cast object of type 'StdI64' to type 'StdF64'` with a four-frame
.NET stack trace printed at the user.

⭐ **THE THREE SHAPES ARE HELD IN ONE PROGRAM, IN DECLARATION ORDER, SO THE ASSERTION IS THAT THEY ANSWER
IDENTICALLY.** A plain local, a struct FIELD and a union PAYLOAD BINDING are three ways of stating the same
alias, and a door open for two of them is what identifies the third as missing rather than as a policy choice.
The cross-alias twin is the `int` → `float` cast itself, which is where the backend's half of this is pinned.

<!-- test: error.unneeded.through-a-union-payload-binding -->
```maxon
typealias Count = int(0 to 255)

union Tagged
	blank
	one(n Count)
end 'Tagged'

type Holder
	export var n as Count

	export static function make() returns Holder
		return Self{n: 5}
	end 'make'
end 'Holder'

function fromPayload(t Tagged) returns Count
	return match t 'k'
		blank gives 0
		one(n) gives n as Count
	end 'k'
end 'fromPayload'

function main() returns ExitCode
	let local = 5 as Count
	let a = local as Count
	let h = Holder.make()
	let b = h.n as Count
	let c = fromPayload(Tagged.one(3))
	print("{a} {b} {c}")
	return 0
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.through-a-union-payload-binding.test:20:18: unneeded cast: 'Count' already fits in 'Count'
error E3010: specs/fragments/type-casting/error.unneeded.through-a-union-payload-binding.test:26:16: unneeded cast: 'Count' already fits in 'Count'
error E3010: specs/fragments/type-casting/error.unneeded.through-a-union-payload-binding.test:28:14: unneeded cast: 'Count' already fits in 'Count'
```

<!-- test: crossing-aliases-through-all-three-doors-is-a-real-cast -->
```maxon
typealias Count = int(0 to 255)
typealias Small = float(0.0 to 10.0)

union Tagged
	blank
	one(n Count)
end 'Tagged'

type Holder
	export var n as Count

	export static function make() returns Holder
		return Self{n: 5}
	end 'make'
end 'Holder'

function fromPayload(t Tagged) returns Small
	return match t 'k'
		blank gives 0.0
		one(n) gives n as Small
	end 'k'
end 'fromPayload'

function main() returns ExitCode
	let local = 5 as Count
	let a = local as Small
	let h = Holder.make()
	let b = h.n as Small
	let c = fromPayload(Tagged.one(3))
	print("{a} {b} {c}")
	return 0
end 'main'
```
```stdout
5.0 5.0 3.0
```

## AN INT CONSTANT AT A FLOAT ALIAS IS JUDGED BEFORE THE CONVERSION, NOT AFTER

⚠ **THE TWO HALVES OF A CROSS-KIND CAST WANT DIFFERENT VALUES, AND GIVING THEM ONE IS A WRONG ANSWER
EITHER WAY.** The runtime guard compares against the target's bounds IN THE TARGET'S KIND, so an
integer source has to be converted before it: handing lowering an i64 where the bounds are f64 is not
a comparison it can make. The COMPILE-TIME refusal wants the opposite — the constant as the author
wrote it — because the conversion mints a value no literal scan reaches, and a constant judged after
it silently stops being judged at all.

⭐ **AN INTEGER LITERAL AT A FLOAT DOOR IS THE SAME CONSTANT WRITTEN WITHOUT A POINT.** `300 as Small`
is refused exactly as `300.0 as Small` is, and `3 as Small` is proved in range and carries no guard.

<!-- test: error.int-literal-outside-a-narrow-float-alias -->
```maxon
typealias Small = float(0.0 to 10.0)

function main() returns ExitCode
	let f = 300 as Small
	print("{f}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-casting/error.int-literal-outside-a-narrow-float-alias.test:5:14: Value 300 is outside the range of 'Small' (float(0 to 10))
```

<!-- test: an-int-literal-inside-a-narrow-float-alias -->
```maxon
typealias Small = float(0.0 to 10.0)

function main() returns ExitCode
	let f = 3 as Small
	print("{f}")
	return 0
end 'main'
```
```stdout
3.0
```

## A CAST TO A GENERIC-INSTANCE OR FUNCTION-TYPE ALIAS OF THE VALUE'S OWN TYPE IS A RE-BRAND

A `typealias` over a generic instance (`Array with Integer`) or over a function type names a type a value may
already have. `xs as Ints` states that type again: the two names denote ONE instance, so the cast converts
nothing and no op survives it. It is the spelling shv2 requires to cross between two such aliases, which it
holds nominally distinct — so shared source carries it and it has to parse here too.

An alias naming a DIFFERENT instance, or a function type of a different shape, is not a type this cast can
take, and the `as` target refuses it as it refuses any other name it cannot use.

<!-- test: cast-to-another-alias-of-the-same-generic-instance -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer
typealias AlsoInts = Array with Integer

function total(xs Ints) returns Integer
	var sum = 0
	for x in xs 'each'
		sum = sum + x
	end 'each'
	return sum
end 'total'

function main() returns ExitCode
	var xs = AlsoInts.create()
	xs.push(20)
	xs.push(22)
	return total(xs as Ints)
end 'main'
```
```exitcode
42
```

<!-- test: cast-to-another-alias-of-the-same-function-type -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function apply(f UnaryOp, n Integer) returns Integer
	return f(n)
end 'apply'

function main() returns ExitCode
	let f = twice as UnaryOp
	return apply(f, n: 21)
end 'main'
```
```exitcode
42
```

<!-- test: error.cast-to-a-different-generic-instance -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ratio = float(f64.min to f64.max)
typealias Ints = Array with Integer
typealias Ratios = Array with Ratio

function total(rs Ratios) returns Integer
	return rs.count()
end 'total'

function main() returns ExitCode
	var xs = Ints.create()
	xs.push(1)
	return total(xs as Ratios)
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/type-casting/error.cast-to-a-different-generic-instance.test:14:21: Expected type name after 'as'
```

<!-- test: error.cast-to-a-different-function-shape -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Predicate = function(Integer) returns bool

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function apply(p Predicate, n Integer) returns Integer
	return 1 if p(n) else 0
end 'apply'

function main() returns ExitCode
	let f = twice as Predicate
	return apply(f, n: 3)
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/type-casting/error.cast-to-a-different-function-shape.test:14:19: Expected type name after 'as'
```
