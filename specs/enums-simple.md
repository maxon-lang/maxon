---
feature: enums-simple
status: experimental
keywords: [enum, enumeration, associated values]
category: type-system
---

# Enums

## Documentation

# Enums

Enums define a type with a fixed set of named variants called cases. Maxon enums support simple enums and enums with associated values.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
	north
	south
	east
	west

	function opposite() returns Direction
		match self 'check'
			north then return Direction.south
			south then return Direction.north
			east then return Direction.west
			west then return Direction.east
		end 'check'
	end 'opposite'

	function isVertical() returns bool
		let result = match self 'check'
			north gives true
			south gives true
			east gives false
			west gives false
		end 'check'
		return result
	end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

## Tests

<!-- test: simple-enum -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let dir = Direction.north
	let result = match dir 'check'
		north gives 1
		south gives 0
		east gives 0
		west gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-assignment -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.red
	c = Color.blue
	let result = match c 'check'
		red gives 0
		green gives 0
		blue gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-not-equal -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	let s = Status.pending
	let result = match s 'check'
		active gives 0
		pending gives 1
		done gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-comparison -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	let s1 = Status.pending
	match s1 'check'
		pending then return 1
		active then return 0
		done then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-function-param -->
```maxon
enum Status
	on
	off
end 'Status'

function isOn(s Status) returns bool
	let result = match s 'check'
		on gives true
		off gives false
	end 'check'
	return result
end 'isOn'

function main() returns ExitCode
	let status = Status.on
	if isOn(status) 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-return-type -->
```maxon
enum Result
	success
	failure
end 'Result'

function getResult(succeed bool) returns Result
	if succeed 'check'
		return Result.success
	end 'check'
	return Result.failure
end 'getResult'

function main() returns ExitCode
	let r = getResult(true)
	let result = match r 'handle'
		success gives 1
		failure gives 0
	end 'handle'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum FloatBacked
	North = 1.1
	South = 2.2
	East = 3.3
end 'FloatBacked'

function main() returns ExitCode
	let f = FloatBacked.North
	let result = match f 'check'
		North gives 1
		South gives 0
		East gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-negative -->
```maxon
enum FloatSigned
	below = -2.2
	zero = 0.0
	above = 1.1
end 'FloatSigned'

function main() returns ExitCode
	let f = FloatSigned.below
	let result = match f 'check'
		below gives 2
		zero gives 0
		above gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: float-backed-signed-zero -->
```maxon
enum SignedZero
	negz = -0.0
	posz = 0.0
end 'SignedZero'

function main() returns ExitCode
	let x = SignedZero.negz
	return match x 'check'
		negz gives 4
		posz gives 7
	end 'check'
end 'main'
```
```exitcode
4
```

<!-- test: enum-method -->
```maxon
enum Direction
	north
	south

	function isNorth() returns bool
		let result = match self 'check'
			north gives true
			south gives false
		end 'check'
		return result
	end 'isNorth'
end 'Direction'

function main() returns ExitCode
	let d = Direction.north
	if d.isNorth() 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method-returns-enum -->
```maxon
enum Toggle
	on
	off

	function flip() returns Toggle
		let result = match self 'check'
			on gives Toggle.off
			off gives Toggle.on
		end 'check'
		return result
	end 'flip'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.on
	let flipped = t.flip()
	let result = match flipped 'check'
		off gives 1
		on gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
enum Color
	red
	red
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/enums-simple/error.duplicate-case.test:4:2: duplicate enum case: 'red'
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
	red
	blue
end 'Color'

function main() returns ExitCode
	let _c = Color.green
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enums-simple/error.unknown-enum-case.test:8:11: unknown enum case: 'green'
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status
	ok = 200
	success = 200
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3031: specs/fragments/enums-simple/error.duplicate-raw-value.test:4:2: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status
	ok = 100
	fail = 5.0
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enums-simple/error.raw-value-type-mismatch.test:4:2: raw value type mismatch: 'expected int, got float'
```

<!-- test: keyword-as-case-name -->
Keywords can be used as enum case names (e.g., `function`, `return`, `end`).
```maxon
enum TokenType
	function
	return
	end
	if
	else
	let
	var
	identifier
end 'TokenType'

function main() returns ExitCode
	let t = TokenType.function
	let result = match t 'check'
		function gives 1
		return gives 0
		end gives 0
		if gives 0
		else gives 0
		let gives 0
		var gives 0
		identifier gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-arithmetic-between-two-cases -->
A float-backed enum's tag IS the f64's IEEE-754 bit pattern, so an ARITHMETIC operand must be decoded
before the operator runs. Before A4o this printed `-9217742537320562688` — the two bit patterns added as
integers. The oracle prints `6.5`, and `Weight.light * 2` `5.0`: an integer operand promotes against the
DECODED double, not against its encoding.
```maxon
enum Weight
	light = 2.5
	heavy = 4.0
end 'Weight'

function main() returns ExitCode
	print("{Weight.light + Weight.heavy} {Weight.light * 2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6.5 5.0
```

<!-- test: float-backed-ordering-two-negative-cases -->
⭐ THE CASE A POSITIVE-ONLY ENUM CANNOT MAKE. IEEE-754 bit patterns of POSITIVE doubles are monotonically
ordered as integers, so `low < high` passes under both readings and pins neither — which is why
`enum-full`'s and `enum-match-exhaustive`'s float cases were green over this bug for their whole lives.
Two NEGATIVE cases invert: `bits(-2.2)` is the LARGER signed i64, so before A4o this answered `ge` where
the oracle answers `lt`.
```maxon
enum FloatSigned
	below = -2.2
	mid = -1.1
	above = 1.1
end 'FloatSigned'

function main() returns ExitCode
	let f = FloatSigned.below
	if f < FloatSigned.mid 'lt'
		print("lt")
	end 'lt' else 'ge'
		print("ge")
	end 'ge'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
lt
```

<!-- test: float-backed-comparison-against-a-float -->
An ORDERING comparison against a genuine `float`, both ways round — the shape `specs/constants.md`'s
`float-comparison` pins — plus an EQUALITY against a float literal, which names no case and so is also a
question about the number. All three were `E3005 cannot compare float with int` before A4o, because the
enum's `named` tag reads as integral and a comparison is domain-strict.
```maxon
enum Threshold
	low = 0.1
	high = 0.9
end 'Threshold'

function main() returns ExitCode
	let val = 0.5
	if val > Threshold.low and val < Threshold.high 'inRange'
		if Threshold.high == 0.9 'exact'
			return 1
		end 'exact'
	end 'inRange'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-backed-equality-is-the-tag-compare -->
⭐ THE ONE COMPARISON A4o DELIBERATELY LEFT ALONE, and the negative control on the decode above. `==`
between two values of ONE enum asks WHICH CASE, so it stays the i64 tag compare `match` dispatch uses —
`-0.0` and `+0.0` are distinct raw values here (`float-backed-signed-zero`), and a `ucomisd` would report
them EQUAL while `match` still selected `negz`. One equality, two operators, one answer.
```maxon
enum SignedZero
	negz = -0.0
	posz = 0.0
end 'SignedZero'

function main() returns ExitCode
	let x = SignedZero.negz
	if x == SignedZero.posz 'floatEquality'
		return 9
	end 'floatEquality'
	return match x 'check'
		negz gives 4
		posz gives 7
	end 'check'
end 'main'
```
```exitcode
4
```

<!-- test: float-backed-enum-into-a-float-parameter -->
The one decode site the PARSER cannot own: the conversion is forced by the CALLEE's declared parameter
type, a whole-program fact a file's parse may not hold, so it is paid in `LowerMaxonToStd.floatWidenedArg`
instead. Before A4o the argument was WIDENED rather than reinterpreted and this printed
`4612811918334231000.0`.
```maxon
typealias Real = float(f64.min to f64.max)

enum Weight
	light = 2.5
end 'Weight'

function takesFloat(f Real) returns Real
	return f
end 'takesFloat'

function main() returns ExitCode
	print("{takesFloat(Weight.light)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.5
```

<!-- test: error.float-backed-enum-cast -->
A cast whose SOURCE is an enum has no conversion — the oracle's rule, word for word. Before A4o this was
a representational no-op that handed back the raw i64 wearing the target's type, so `Weight.light as Real`
printed `4612811918334231000.0`: a silent wrong answer where the `as` is supposed to BE the type check.
```maxon
typealias Real = float(f64.min to f64.max)

enum Weight
	light = 2.5
end 'Weight'

function main() returns ExitCode
	let f = Weight.light as Real
	print("{f}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:9:23: Cannot cast from enum to float
```

<!-- test: error.int-backed-enum-cast -->
ONE rule, BOTH backings — the oracle refuses `Code.ok as Whole` too. An int-backed enum's no-op returned
the right NUMBER, so refusing it looks like a loss; what that spelling provided was `.rawValue` under
another name, an accessor this compiler does not have and the oracle does not reach this way either.
```maxon
typealias Whole = int(i64.min to i64.max)

enum Code
	ok = 200
end 'Code'

function main() returns ExitCode
	let n = Code.ok as Whole
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:9:18: Cannot cast from enum to int
```

<!-- test: error.float-backed-enum-shift -->
A shift on a float-backed enum is refused in the SHIFT's own words, because the decode makes the operand
a float and a double's bits are a sign/exponent/mantissa triple rather than a magnitude. Before A4o it
compiled and shifted the mantissa.
```maxon
enum Weight
	light = 2.5
end 'Weight'

function main() returns ExitCode
	let x = Weight.light shl 1
	print("{x}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:23: operator 'shl' requires integer operands, but this one is float
```

<!-- test: float-backed-negation -->
⭐ THE SIXTH READ SITE. A prefix `-` is arithmetic, so it is a question about the NUMBER — but `parseUnary`
reads the operand's raw tag rather than the number's, and a float-backed enum's `named` tag is not
`tagIsFloat`, so before this case `-Weight.light` emitted an INTEGER negate over the IEEE-754 bits and
printed `-4612811918334230528`. The int-backed arm beside it is the negative control: its `-` must stay
the integer negate it always was.
```maxon
enum Weight
	light = 2.5
end 'Weight'

enum Level
	high = 200
end 'Level'

function main() returns ExitCode
	print("{-Weight.light} {-Level.high}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-2.5 -200
```

<!-- test: error.float-backed-enum-not -->
`not` is the shift's twin — a bitwise operator with no reading of a sign/exponent/mantissa triple — so it
is refused in its own words, exactly as `error.float-backed-enum-shift` is. Before this case `parseUnary`
complemented the encoding and printed `-4612811918334230529`; the oracle refuses.
```maxon
enum Weight
	light = 2.5
end 'Weight'

function main() returns ExitCode
	let x = not Weight.light
	print("{x}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:10: operator 'not' requires integer operands, but this one is float
```

<!-- test: float-backed-enum-as-a-parameter-default -->
A float-backed enum reaching a parameter DEFAULT is the seam between two rungs that landed an hour
apart in one lane: `A4q` made a default expression consume the whole token region the capture skipped,
and `A4o` made every read of a float-backed enum DECODE its i64 rather than read it as a magnitude. The
default is parsed from a lifted token region rather than from a statement, so it reaches the decode by a
path no other case here takes — and neither rung's own cases cross it. Both spellings are pinned
together: the default the caller omits and the argument the caller supplies must give the same answer
for the same case.
```maxon
typealias Real = float(f64.min to f64.max)

enum Weight
	light = 2.5
	heavy = 4.0
end 'Weight'

function show(w Weight = Weight.light) returns Real
	print("{w} ")
	return w + 0.0
end 'show'

function main() returns ExitCode
	let a = show()
	let b = show(Weight.heavy)
	print("{a + b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.5 4.0 6.5
```
