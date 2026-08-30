---
feature: float-type
status: stable
keywords: [float, floating-point, double, f64]
category: types
---

# Float Type

## Documentation

The `float` type stores 64-bit double-precision floating-point numbers.

### Syntax

```maxon
var pi = 3.14159
let ratio = 2.5
```
Float literals must include a decimal point:
- Valid: `3.14`, `2.0`, `0.5`
- Invalid: `3` (this is an int)

### Example

```maxon

typealias Radius = float(f64.min to f64.max)

function circleArea(radius Radius) returns Radius
	return 3.14159 * radius * radius
end 'circleArea'

function main() returns ExitCode
	let area = circleArea(5.0)
	return trunc(area)  // Returns 78
end 'main'
```
```exitcode
78
```


## Tests

<!-- test: basic-float -->
```maxon
function main() returns ExitCode
	let x = 3.14
	let y = 2.0
	let z = x + y
	let result = trunc(z)
	return result
end 'main'
```
```exitcode
5
```


<!-- test: float-comparison -->
```maxon
function main() returns ExitCode
	let x = 3.5
	let y = 2.1
	if x > y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: float-arithmetic -->
```maxon
function main() returns ExitCode
	let a = 10.0
	let b = 3.0
	let result = a / b
	return trunc(result)
end 'main'
```
```exitcode
3
```


<!-- test: float-promotion -->
```maxon
function main() returns ExitCode
	let x = 5
	let y = 2.0
	let result = x + y
	return trunc(result)
end 'main'
```
```exitcode
7
```


<!-- test: float-phi-loop-carried -->
A float ACCUMULATOR — the commonest float idiom there is. The loop header takes a phi whose
value is a float, so the phi's own register class must be XMM. Every other float test in this
suite keeps its floats in straight-line code, where no phi is minted at all.
```maxon
function main() returns ExitCode
	var f = 0.0
	var i = 0
	while i < 4 'loop'
		f = f + 2.5
		i = i + 1
	end 'loop'
	return trunc(f)
end 'main'
```
```exitcode
10
```


<!-- test: float-phi-through-branch -->
An if-merge phi carrying a float, guarded by an INT compare. The int guard is the point: it
isolates the phi's class from the float-compare lowering, so a failure here can only be the
phi. `float-compare-branch.md` puts a phi on a float compare's edges, but the value it carries
is an `int` — so a float-TYPED phi was covered by nothing.
```maxon
function work(a Integer) returns Integer
	var f = a + 0.0
	if a > 5 'gt'
		f = f + 1.5
	end 'gt'
	return trunc(f)
end 'work'

function main() returns ExitCode
	return work(19)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
20
```


<!-- test: float-phi-both-arms -->
Both arms assign the float, so the merge phi has two real incoming values rather than one
incoming plus the fall-through definition.
```maxon
function work(a Integer) returns Integer
	var f = a + 0.0
	if a > 100 'big'
		f = f * 2.0
	end 'big' else 'small'
		f = f - 4.5
	end 'small'
	return trunc(f)
end 'work'

function main() returns ExitCode
	return work(19)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
14
```


<!-- test: float-return-from-function -->
```maxon
typealias Float = float(f64.min to f64.max)

function computePi() returns Float
	return 3.14
end 'computePi'

function main() returns ExitCode
	let x = computePi()
	let result = trunc(x)
	return result
end 'main'
```
```exitcode
3
```


<!-- test: float-struct-field -->
⭐ **A STRUCT FIELD DECLARED `float`** — stored at construction, overwritten by a write, and read back
into an arithmetic expression. Nothing in this suite had a float FIELD before, which is exactly why
the field doors were the last place an int reached an f64 slot unconverted: every float test kept its
floats in locals, parameters and returns.
```maxon
type Particle
	export var mass as Real
	export var velocity as Real

	export static function make(mass Real, velocity Real) returns Self
		return Self{mass: mass, velocity: velocity}
	end 'make'

	export function momentum() returns Real
		return self.mass * self.velocity
	end 'momentum'
end 'Particle'

function main() returns ExitCode
	var p = Particle.make(2.5, velocity: 4.0)
	p.velocity = 6.0
	return trunc(p.momentum())
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
15
```


<!-- test: float-alias-struct-field -->
The same struct with its fields declared through a ranged FLOAT typealias. It is not a spelling
variant: a `float(low to high)` alias reaches a field as a bare NAME, and a name is the one form the
whole-program declaration sweep cannot resolve — it is handed the index it is still building. Read as
an ordinary `named` type the alias is an INTEGER, so this exact program was `E3009: cannot implicitly
convert 'float' to 'int'` on the perfectly legal `Self{mass: 2.5}`.
```maxon

typealias Weight = float(f64.min to f64.max)

type Particle
	export var mass as Weight
	export var velocity as Weight

	export static function make(mass Weight, velocity Weight) returns Self
		return Self{mass: mass, velocity: velocity}
	end 'make'

	export function momentum() returns Weight
		return self.mass * self.velocity
	end 'momentum'
end 'Particle'

function main() returns ExitCode
	var p = Particle.make(2.5, velocity: 4.0)
	p.velocity = 6.0
	return trunc(p.momentum())
end 'main'
```
```exitcode
15
```


<!-- test: float-alias-struct-field-int-default -->
The two halves of that alias meeting each other: the field's type resolves to `float`, so its
DECLARED DEFAULT — recorded by the same sweep, against the same unresolved name — must be widened to
the f64 bit pattern before it fills the slot. Fixing the field's TYPE alone turns this program from a
clean rejection into a silent 1.5e-323.

⭐ **THIS IS THE CASE WHOSE ANSWER IS DECIDED BY THE RECORDED LITERAL'S TAG**, and its pair below is
what proves the decision is self-supporting: the two defaults differ ONLY in the tag, and each must
reach the slot as `3.0` and `2.5` respectively. Break the tag and exactly one of them goes wrong.
```maxon

typealias Weight = float(f64.min to f64.max)

type Particle
	export var mass as Weight = 3

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Particle'

function main() returns ExitCode
	let p = Particle.make()
	if p.mass == 3.0 'exact'
		return trunc(p.mass * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```


<!-- test: float-alias-struct-field-float-default -->
The pair: a FLOAT literal default through the same alias, which must NOT be widened a second time. It
was a false `E3009: cannot implicitly convert 'float' to 'int'` — a lossy-conversion rejection of a
float meeting a float — because the declaration sweep reads an unresolved alias NAME as an integer.
The verdict now waits for the whole-program index; the widening decision is a separate question the
recorded tag answers, so neither half stands on the other.
```maxon

typealias Weight = float(f64.min to f64.max)

type Particle
	export var mass as Weight = 2.5

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Particle'

function main() returns ExitCode
	let p = Particle.make()
	if p.mass == 2.5 'exact'
		return trunc(p.mass * 8.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
20
```


<!-- test: float-alias-struct-field-zero-defaults -->
`= 0` and `= 0.0` are the one pair the payload CANNOT tell apart — zero is the single fixed point of
the int→f64 bit conversion — so they are the sharpest statement of why the literal's tag is recorded
rather than inferred. Both must reach the slot as `0.0`.
```maxon

typealias Weight = float(f64.min to f64.max)

type Particle
	export var mass as Weight = 0
	export var vel as Weight = 0.0

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Particle'

function main() returns ExitCode
	let p = Particle.make()
	if p.mass == 0.0 'zeroMass'
		if p.vel == 0.0 'zeroVel'
			return trunc((p.mass + p.vel + 4.2) * 10.0)
		end 'zeroVel'
	end 'zeroMass'
	return 7
end 'main'
```
```exitcode
42
```


<!-- test: float-print-negative-and-repeat -->
<!-- P1.2 String — `print` + the `{}` interpolation that calls mrt_f64_to_string. The x64 SSA-destruction hazard this case regresses is covered at THIS rung by specs-shv2/float-compare-branch.md, which reaches the same unordered else edge through an ExitCode instead of a formatted string. -->
```maxon
function main() returns ExitCode
	let a = 3.14159
	let b = 2.71828
	// Print `a` twice so its value must survive across the first print's
	// mrt_f64_to_string call, then print a negative and a zero. Regression for
	// an x64 SSA-destruction bug: an f64 compare lowers to a two-conditional-jump
	// else edge (`jp` + `jae`), and only one jump was routed through the phi-copy
	// trampoline. The other bypassed the copy that zeroed mrt_f64_to_string's
	// is_negative flag, so on the second call a positive value was formatted as
	// negative (a stray '-' plus a runaway digit loop that spewed megabytes).
	print("{a}\n")
	print("{a}\n")
	print("{b}\n")
	print("{a + b}\n")
	print("{-a}\n")
	print("{0.0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.14159
3.14159
2.71828
5.85987
-3.14159
0.0
```

Note: Tests for many float parameters (>4) and float parameter preservation across calls are currently disabled due to known codegen bugs with float register allocation. See test fragments for the disabled tests.

<!-- test: float.panic-in-a-float-returning-function -->
**THE CONSTRUCT `float.fromString` IS BUILT ON, PINNED IN USER CODE — and the pre-existing compiler PANIC
that stood between this rung and its two float cases.** `stdlib/Builtins.maxon`'s `__float_fromString`
divides under `try (digit / fracDiv) otherwise panic(…)`, so enabling `parsable.float-fromstring` made a
`panic()` inside a FLOAT-returning function reachable for the first time — and it did not compile:
`panic at X64Backend.maxon:751: a register-to-register move from rax to xmm0 crosses register files`.

The cause is in `Parser.emitDeadReturn`. A diverging `panic()` still owes its block a terminator, and the
parser emitted `ret <integer 0>` on the grounds that the value is dead. Its BITS are dead; its REGISTER FILE
is not — `ret` moves the value into the return register, which is XMM0 here. `LowerMaxonToStd`'s
`emitZeroConstOfReturnType` already states exactly this rule for the THROW edge, quoting the same panic; the
parser's dead return is the same defect one door over, and now reads the same fact (through
`floatResolvedTag`, so a `returns ParsedFloat` ranged alias is XMM-classed too).

⚠ **IT NEEDS NO `try` AND NO `Parsable` — THE REPRODUCER IS BELOW, AND THAT IS WHY IT LIVES HERE.** It
reached shv2 through `float.fromString` (A1s-prim) only because `stdlib/Builtins.maxon` happens to write
that shape; the property is a float function's DEAD RETURN, which every `panic()` in one emits.
```maxon
function scaled(x Real) returns Real
	if x < 0.0 'negative'
		panic("scaled: negative input")
	end 'negative'
	return x * 2.0
end 'scaled'

function main() returns ExitCode
	return trunc(scaled(21.0))
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
42
```
