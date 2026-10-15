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
var pi = 3.14159 as FullRangeFloat
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
	var f = 0.0 as FullRangeFloat
	var i = 0 as Count
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
	var f = a as FullRangeFloat + 0.0
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
	var f = a as FullRangeFloat + 0.0
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

	static function make(mass Real, velocity Real) returns Self
		return Self{mass: mass, velocity: velocity}
	end 'make'

	function momentum() returns Real
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

	static function make(mass Weight, velocity Weight) returns Self
		return Self{mass: mass, velocity: velocity}
	end 'make'

	function momentum() returns Weight
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
	export var mass = 3 as Weight

	static function make() returns Self
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
The pair: a FLOAT literal default through the same alias, which must NOT be widened a second time, nor
refused with a false `E3009: cannot implicitly convert 'float' to 'int'` — a lossy-conversion rejection of a
float meeting a float. The declaration sweep reads an unresolved alias NAME as an integer, so the
verdict waits for the whole-program index; the widening decision is a separate question the
recorded tag answers, so neither half stands on the other.
```maxon

typealias Weight = float(f64.min to f64.max)

type Particle
	export var mass = 2.5 as Weight

	static function make() returns Self
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
	export var mass = 0 as Weight
	export var vel = 0.0 as Weight

	static function make() returns Self
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

<!-- test: float.panic-in-a-float-returning-function -->
**THE CONSTRUCT `float.fromString` IS BUILT ON, PINNED IN USER CODE.** `stdlib/Builtins.maxon`'s
`__float_fromString` divides under `try (digit / fracDiv) otherwise panic(…)`, so `parsable.float-fromstring`
reaches a `panic()` inside a FLOAT-returning function.

A diverging `panic()` still owes its block a terminator, and `Parser.emitDeadReturn` emits a `ret` whose
value is dead. Its BITS are dead; its REGISTER FILE is not — `ret` moves the value into the return register,
which is XMM0 here, so an integer zero would be a register-to-register move from rax to xmm0 across register
files. `LowerMaxonToStd`'s `emitZeroConstOfReturnType` states this rule for the THROW edge, and the
parser's dead return reads the same fact (through `floatResolvedTag`, so a `returns FullRangeFloat` ranged
alias is XMM-classed too).

⚠ **IT NEEDS NO `try` AND NO `Parsable` — THE REPRODUCER IS BELOW, AND THAT IS WHY IT LIVES HERE.** It
reaches the compiler through `float.fromString` only because `stdlib/Builtins.maxon` happens to write
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

<!-- test: error.a-float-alias-local-does-not-pass-as-another-alias -->
A float alias is nominal: a local of one alias does not pass where another is expected.
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

function show(w Weight)
	print("{w}\n")
end 'show'

function main() returns ExitCode
	let half = 0.5 as Ratio
	show(half)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-float-alias-local-does-not-pass-as-another-alias.maxon:11:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: error.a-float-alias-parameter-does-not-pass-as-another-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

function show(w Weight)
	print("{w}\n")
end 'show'

function relay(r Ratio)
	show(r)
end 'relay'

function main() returns ExitCode
	relay(0.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-float-alias-parameter-does-not-pass-as-another-alias.maxon:10:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: error.a-float-alias-field-does-not-pass-as-another-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

type Mix
	export var share = 0.5 as Ratio

	static function create() returns Self
		return Self{}
	end 'create'
end 'Mix'

function show(w Weight)
	print("{w}\n")
end 'show'

function main() returns ExitCode
	let m = Mix.create()
	show(m.share)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-float-alias-field-does-not-pass-as-another-alias.maxon:19:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: error.a-float-alias-mutated-parameter-does-not-pass-as-another-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

function show(w Weight)
	print("{w}\n")
end 'show'

function settle(r Ratio)
	r = 0.25
	show(r)
end 'settle'

var level = 0.5 as Ratio

function main() returns ExitCode
	settle(level)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-float-alias-mutated-parameter-does-not-pass-as-another-alias.maxon:11:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: error.a-captured-float-alias-does-not-pass-as-another-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

function describe(w Weight) returns String
	return "{w}"
end 'describe'

function main() returns ExitCode
	let half = 0.5 as Ratio
	let later = function() gives describe(half)
	print("{later()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-captured-float-alias-does-not-pass-as-another-alias.maxon:11:31: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: error.a-float-alias-array-element-does-not-pass-as-another-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)
typealias RatioArray = Array with Ratio

function show(w Weight)
	print("{w}\n")
end 'show'

function main() returns ExitCode
	var rs = RatioArray.create()
	rs.push(0.5)
	let first = try rs.get(0) otherwise return 1
	show(first)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/float-type/error.a-float-alias-array-element-does-not-pass-as-another-alias.maxon:14:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: unsigned-values-convert-to-float-at-runtime -->
A runtime value of an unsigned range converts to float as its unsigned value, rounding to nearest even.
```maxon
typealias Wide = int(0 to u64.max)
typealias Real = float(f64.min to f64.max)

function widen(w Wide) returns Real
	return w as Real
end 'widen'

function main() returns ExitCode
	if widen(u64.max) != 18446744073709551616.0 'top'
		return 1
	end 'top'
	if widen(9223372036854776832) != 9223372036854775808.0 'tieToEven'
		return 2
	end 'tieToEven'
	if widen(9223372036854776833) != 9223372036854777856.0 'pastTheTie'
		return 3
	end 'pastTheTie'
	if widen(5) != 5.0 'small'
		return 4
	end 'small'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unsigned-literal-argument-to-a-float-parameter-stays-positive -->
A literal above `i64.max` handed to a float parameter converts as the unsigned value it is.
```maxon
typealias Real = float(f64.min to f64.max)

function positive(r Real) returns bool
	return r > 0.0
end 'positive'

function main() returns ExitCode
	if positive(9223372036854775808) 'p'
		return 7
	end 'p'
	return 3
end 'main'
```
```exitcode
7
```

<!-- test: a-float-array-read-is-range-known -->
An element read from a `let` float array carries the interval of the elements written, and fits a float alias that admits the whole interval.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let xs = [0.25, 0.75]
	let r = try xs.get(0) otherwise 0.5
	print("{take(r)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.25
```

<!-- test: error.a-float-array-read-outside-a-narrower-alias -->
A float read whose interval reaches past the alias is refused at the use, naming the extreme that does.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let xs = [0.25, 2.5]
	let r = try xs.get(0) otherwise 0.5
	let s = take(r)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: Value 2.5 is outside the range of 'Unit' (float(0 to 1))
```

<!-- test: a-for-in-over-a-float-array-is-range-known -->
The element a `for … in` binds over a `let` float array carries the array's interval, and float arithmetic over it carries a computed one.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [1.5, 2.5]
	for x in xs 'each'
		print("{take(x * 2.0)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0
5.0
```

<!-- test: a-literal-counter-times-a-float-is-range-known -->
A both-literal counter promoted to float carries its range, so `i * 2.0` fits an alias that admits `[2.0, 6.0]`.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	for i in 1 upto 4 'each'
		print("{take(i * 2.0)}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.0
4.0
6.0
```

<!-- test: error.a-literal-counter-times-a-float-outside-an-alias -->
The same counter times 4.0 reaches 12.0, which the alias refuses at compile time.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	for i in 1 upto 4 'each'
		print("{take(i * 4.0)}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:11: Value 12 is outside the range of 'Span' (float(0 to 10))
```

<!-- test: a-float-merge-of-literals-is-range-known -->
A ternary of two float literals joins their values, so the result fits an alias that admits both.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let flag = 3 > 2
	let r = 0.25 if flag else 0.75
	print("{take(r)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.25
```

<!-- test: a-float-quotient-by-an-interval-holding-zero-is-unknown -->
A divisor whose interval holds zero leaves the quotient with no known range, so it wears the full-range float alias and a narrower slot refuses it.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let ds = [0.0, 1.0]
	let d = try ds.get(1) otherwise 1.0
	let q = try (1.0 / d) otherwise 0.5
	let s = take(q)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 'v': expected 'Unit', got 'FullRangeFloat'
```

<!-- test: a-float-interval-flows-through-arithmetic -->
Float arithmetic over range-known operands computes an interval, so every result below fits an alias that admits it.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [1.5, 2.5]
	let r = try xs.get(0) otherwise 2.0
	let q = try (4.5 / r) otherwise 0.0
	print("{take(r + r)} {take(r * r)} {take(r - 1.0)} {take(q)} {take(5.0 - r)} {take(abs(-r))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0 2.25 0.5 3.0 3.5 1.5
```

<!-- test: a-float-interval-flows-through-the-math-builtins -->
The rounding builtins, `sqrt`, `min` and `max` map an interval to an interval.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [4.0, 9.0]
	let r = try xs.get(0) otherwise 4.0
	print("{take(sqrt(r))} {take(floor(r))} {take(ceil(r))} {take(round(r))} {take(min(r, 5.0))} {take(max(r, 6.0))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.0 4.0 4.0 4.0 4.0 6.0
```

<!-- test: a-float-absolute-value-folds-an-interval-across-zero -->
`abs` of an interval holding zero is non-negative, so it fits an alias that the interval itself does not.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [-3.0, 2.0]
	let r = try xs.get(0) otherwise 0.0
	print("{take(abs(r))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0
```

<!-- test: error.a-float-product-interval-reaches-past-an-alias -->
A product's interval is taken at its corners, so the largest corner is the extreme the diagnostic names.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [2.0, 6.0]
	let r = try xs.get(0) otherwise 3.0
	let s = take(r * r)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: Value 36 is outside the range of 'Span' (float(0 to 10))
```

<!-- test: error.a-float-difference-interval-reaches-below-an-alias -->
The low end of an interval is refused as well as the high end, and it is the one named when it is the one outside.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [2.0, 6.0]
	let r = try xs.get(0) otherwise 3.0
	let s = take(r - 4.0)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: Value -2 is outside the range of 'Span' (float(0 to 10))
```

<!-- test: a-float-root-of-an-interval-holding-negatives-is-unknown -->
The root of an interval that reaches below zero has no known range, so it wears the full-range float alias and a narrower slot refuses it.
```maxon
typealias Span = float(0.0 to 10.0)

function take(v Span) returns Span
	return v
end 'take'

function main() returns ExitCode
	let xs = [-1.0, 4.0]
	let r = try xs.get(1) otherwise 4.0
	let s = take(sqrt(r))
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'v': expected 'Span', got 'FullRangeFloat'
```

<!-- test: a-let-tuple-float-fields-are-range-known -->
A float field of a `let` tuple is the number written there, or the interval of the value written there.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let xs = [0.25, 0.75]
	let r = try xs.get(0) otherwise 0.5
	let t = (r, 0.5)
	print("{take(t.0)} {take(t.1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.25 0.5
```

<!-- test: error.a-let-tuple-float-field-outside-an-alias -->
A float tuple field written as a literal outside the alias is refused where it is used.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let t = (0.25, 2.0)
	let s = take(t.1)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:10: Value 2 is outside the range of 'Unit' (float(0 to 1))
```

<!-- test: error.a-folded-float-arithmetic-outside-an-alias -->
Float arithmetic over literals folds, and the folded value is checked against the alias like a literal.
```maxon
typealias Unit = float(0.0 to 1.0)

function take(v Unit) returns Unit
	return v
end 'take'

function main() returns ExitCode
	let s = take(0.5 * 4.0)
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:10: Value 2 is outside the range of 'Unit' (float(0 to 1))
```

<!-- test: float-builtins-accept-an-integer-alias -->
`sqrt`, `floor`, `ceil`, `round`, `abs`, `min` and `max` take an integer alias, convert it, and answer a `FullRangeFloat`; the mixed arithmetic `count + 1.5` converts the same way.
```maxon
function echo(v FullRangeFloat) returns FullRangeFloat
	return v
end 'echo'

function main() returns ExitCode
	let n = CommandLine.args().count() + 8
	print("{echo(sqrt(n))} {echo(floor(n))} {echo(ceil(n))} {echo(round(n))} {echo(abs(n))} {echo(min(n, 4.5))} {echo(max(n, 4.5))} {echo(n + 1.5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0 9.0 9.0 9.0 9.0 4.5 9.0 10.5
```
