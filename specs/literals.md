---
feature: literals
status: stable
keywords: [literal, constant, int, float, character, string, bool]
category: expressions
---

# Literals

## Documentation

Literals are constant values used directly in code.

### Integer Literals

Decimal integers:
```maxon
42
-17
0
```

Hexadecimal integers (prefix `0x`):
```maxon
0xff
0x1a2b
0x0
```

Binary integers (prefix `0b`):
```maxon
0b1010
0b11111111
0b0
```

Octal integers (prefix `0o`):
```maxon
0o777
0o52
0o0
```

Underscore separators can be used for readability in any integer literal:
```maxon
1_000_000
0xff_ff
0b1111_0000
0o77_77
```
### Float Literals
Must include decimal point:
```maxon
3.14
-2.5
0.0
```

Scientific notation with `e` or `E`:
```maxon
1.5e10
2.0e-3
4.84143144246472090e+00
6.9e+05
```
### Character Literals
Single character in single quotes:
```maxon
'A'
'z'
'\n'
```
### String Literals
Text in double quotes:
```maxon
"Hello, World!"
"Line1\nLine2"
```
### Boolean Literals
```maxon
true
false
```
## Tests

<!-- test: integer -->
```maxon
function main() returns ExitCode
	return 5
end 'main'
```
```exitcode
5
```

<!-- test: hex-integer -->
```maxon
function main() returns ExitCode
	return 0x7d
end 'main'
```
```exitcode
125
```

<!-- test: hex-integer-uppercase -->
```maxon
function main() returns ExitCode
	return 0x5A
end 'main'
```
```exitcode
90
```

<!-- test: binary-integer -->
```maxon
function main() returns ExitCode
	return 0b1010
end 'main'
```
```exitcode
10
```

<!-- test: octal-integer -->
```maxon
function main() returns ExitCode
	return 0o77
end 'main'
```
```exitcode
63
```

<!-- test: underscore-separator -->
```maxon
function main() returns ExitCode
	let x = 1_000
	return x - 990
end 'main'
```
```exitcode
10
```

<!-- test: hex-underscore -->
```maxon
function main() returns ExitCode
	return 0xff_ff - 65525
end 'main'
```
```exitcode
10
```

<!-- test: binary-underscore -->
```maxon
function main() returns ExitCode
	return 0b0101_1010
end 'main'
```
```exitcode
90
```

<!-- test: large-hex-literal -->
```maxon
// Test hex literal above 32-bit range (0x140000000 = 5368709120)
function main() returns ExitCode
	let x = 0x0000000140000000
	// Verify the value wasn't truncated to 32-bit (which would give 0x40000000 = 1073741824)
	if x == 5368709120 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: large-hex-literal-underscore -->
```maxon
// Test large hex literal with underscore separators
function main() returns ExitCode
	let x = 0x0000_0001_4000_0000
	if x == 5368709120 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: int64-max -->
```maxon
// Test INT64_MAX (9223372036854775807)
function main() returns ExitCode
	let x = 9223372036854775807
	if x > 0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: unsigned-literal-reaches-u64-max -->
An integer literal above `i64.max` is the unsigned value; it fits an alias whose range admits it.
```maxon
typealias Wide = int(0 to 18446744073709551615)

function main() returns ExitCode
	let top = 18446744073709551615 as Wide
	let hex = 0xffffffffffffffff as Wide
	let half = 9223372036854775808 as Wide
	print("{top} {hex} {half}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615 18446744073709551615 9223372036854775808
```

<!-- test: error.unsigned-literal-with-no-alias -->
```maxon
function main() returns ExitCode
	let x = 9223372036854775808
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.unsigned-literal-with-no-alias.maxon:3:10: Integer literal '9223372036854775808' is above i64.max, so it needs an alias whose range admits it: write '9223372036854775808 as <alias>'
```

<!-- test: error.unsigned-literal-into-a-signed-alias -->
```maxon
typealias Offset = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 9223372036854775808 as Offset
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.unsigned-literal-into-a-signed-alias.maxon:5:30: Value 9223372036854775808 is outside the range of 'Offset' (int(-9223372036854775808 to 9223372036854775807))
```

<!-- test: error.unsigned-literal-into-a-signed-parameter -->
```maxon
typealias Offset = int(i64.min to i64.max)

function take(o Offset) returns Offset
	return o
end 'take'

function main() returns ExitCode
	print("{take(9223372036854775808)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.unsigned-literal-into-a-signed-parameter.maxon:9:10: Value 9223372036854775808 is outside the range of 'Offset' (int(-9223372036854775808 to 9223372036854775807))
```

<!-- test: error.unsigned-literal-into-a-signed-field-of-a-global -->
```maxon
typealias Offset = int(i64.min to i64.max)

type Spot
	export var at as Offset

	static function create(at Offset) returns Self
		return Self{at: at}
	end 'create'
end 'Spot'

let origin = Spot.create(9223372036854775808)

function main() returns ExitCode
	print("{origin.at}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.unsigned-literal-into-a-signed-field-of-a-global.maxon:12:26: Value 9223372036854775808 is outside the range of 'Offset' (int(-9223372036854775808 to 9223372036854775807))
```

<!-- test: unsigned-literal-to-a-float-alias-stays-positive -->
A literal above `i64.max` converts to a float as the unsigned value it is.
```maxon
typealias Real = float(f64.min to f64.max)

function main() returns ExitCode
	let r = 9223372036854775808 as Real
	if r > 0.0 'positive'
		return 7
	end 'positive'
	return 3
end 'main'
```
```exitcode
7
```

<!-- test: unsigned-patterns-to-a-float-alias-at-top-level-stay-positive -->
A top-level initializer converts `u64.max` and a hex pattern with the top bit set to a float as the unsigned values they are.
```maxon
typealias Real = float(f64.min to f64.max)

let fromMax = u64.max as Real
let fromHex = 0x8000000000000000 as Real

function main() returns ExitCode
	if fromMax > 0.0 and fromHex > 0.0 'positive'
		return 7
	end 'positive'
	return 3
end 'main'
```
```exitcode
7
```

<!-- test: error.a-top-level-cast-gives-the-global-its-alias -->
A top-level `let` initialized by a cast has the cast's type, so it does not pass where another alias is expected.
```maxon
typealias Tally = int(0 to 1000)
typealias Score = int(0 to 1000)

let base = 5 as Tally

function show(points Score)
	print("{points}\n")
end 'show'

function main() returns ExitCode
	show(base)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.a-top-level-cast-gives-the-global-its-alias.maxon:12:2: argument type mismatch for 'points': expected 'Score', got 'Tally'
```

<!-- test: error.a-top-level-float-cast-gives-the-global-its-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Weight = float(0.0 to 1.0)

let half = 0.5 as Ratio

function show(w Weight)
	print("{w}\n")
end 'show'

function main() returns ExitCode
	show(half)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.a-top-level-float-cast-gives-the-global-its-alias.maxon:12:2: argument type mismatch for 'w': expected 'Weight', got 'Ratio'
```

<!-- test: large-decimal-literal -->
```maxon
// Test decimal literal above 32-bit range
function main() returns ExitCode
	let x = 5368709120
	if x == 0x140000000 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```


<!-- test: float -->
```maxon
function main() returns ExitCode
	let x = 3.14
	return trunc(x)
end 'main'
```
```exitcode
3
```


<!-- test: boolean -->
```maxon
function main() returns ExitCode
	let flag = true
	if flag 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: scientific-notation-positive-exponent -->
```maxon
function main() returns ExitCode
	let x = 1.5e2
	return trunc(x) - 140
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-negative-exponent -->
```maxon
function main() returns ExitCode
	let x = 5.0e-1
	return trunc(x * 20.0)
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-explicit-positive -->
```maxon
function main() returns ExitCode
	let x = 2.5e+02
	return trunc(x) - 240
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-uppercase -->
```maxon
function main() returns ExitCode
	let x = 1.0E3
	return trunc(x) - 990
end 'main'
```
```exitcode
10
```

### Overflow Errors

<!-- test: error.int-overflow -->
```maxon
function main() returns ExitCode
	let x = 99999999999999999999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.int-overflow.maxon:3:10: Integer literal '99999999999999999999' is outside the range of int (-9223372036854775808 to 18446744073709551615)
```

<!-- test: error.hex-overflow -->
```maxon
function main() returns ExitCode
	let x = 0x1ffffffffffffffff
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.hex-overflow.maxon:3:10: Integer literal '0x1ffffffffffffffff' is outside the range of int (-9223372036854775808 to 18446744073709551615)
```

<!-- test: error.binary-overflow -->
```maxon
function main() returns ExitCode
	let x = 0b10000000000000000000000000000000000000000000000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.binary-overflow.maxon:3:10: Integer literal '0b10000000000000000000000000000000000000000000000000000000000000000' is outside the range of int (-9223372036854775808 to 18446744073709551615)
```

<!-- test: error.octal-overflow -->
```maxon
function main() returns ExitCode
	let x = 0o2000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.octal-overflow.maxon:3:10: Integer literal '0o2000000000000000000000' is outside the range of int (-9223372036854775808 to 18446744073709551615)
```

<!-- test: error.float-overflow -->
```maxon
function main() returns ExitCode
	let x = 1.0e999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.float-overflow.maxon:3:10: Float literal '1.0e999' is outside the range of float
```

<!-- test: i64-min-literal -->
`-9223372036854775808` is exactly `i64.min`. Its magnitude (`9223372036854775808`
= `i64.max + 1`) overflows a positive i64, so a negated literal must be parsed as
a single unit: parsing the bare magnitude first would wrongly report E2011. (An
un-negated `9223372036854775808` needs an alias whose range admits it — see
error.unsigned-literal-with-no-alias above.)
```maxon
typealias Big = int(i64.min to i64.max)

function main() returns ExitCode
	let lo = -9223372036854775808 as Big
	let back = lo - i64.min
	return back as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: literal-domain.a-literal-above-i64-max-orders-and-divides-unsigned -->
A decimal or hex literal above `i64.max` is an unsigned operand: it makes a comparison against a declared-unsigned operand run unsigned, and a divide by it run unsigned.
```maxon
typealias Quantity = int(0 to u64.max)

function opaque(n Quantity) returns Quantity
	return n
end 'opaque'

function main() returns ExitCode
	let small = opaque(1)
	let big = opaque(18446744073709551615)

	if small > 9223372036854775808 'smallIsAbove'
		return 1
	end 'smallIsAbove'

	if big < 9223372036854775808 'bigIsBelow'
		return 2
	end 'bigIsBelow'

	if small >= 0x8000000000000000 'smallIsAtLeastHex'
		return 3
	end 'smallIsAtLeastHex'

	if big / 9223372036854775808 != 1 'quotient'
		return 4
	end 'quotient'

	if big mod 9223372036854775808 != 9223372036854775807 'remainder'
		return 5
	end 'remainder'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: literal-domain.an-unsigned-remainder-by-a-runtime-u64-max-is-not-zeroed -->
`x mod u64.max` with the divisor only known at run time is `x` for every `x` below it; the signed `-1` overflow guard does not apply to an unsigned remainder.
```maxon
typealias Quantity = int(0 to u64.max)
typealias Divisor = int(2 to u64.max)

function remainder(n Quantity, d Divisor) returns Quantity
	return try (n mod (d as Quantity)) otherwise panic("d is never zero")
end 'remainder'

function main() returns ExitCode
	if remainder(5, d: 18446744073709551615) != 5 'five'
		return 1
	end 'five'

	if remainder(18446744073709551615, d: 18446744073709551615) != 0 'itself'
		return 2
	end 'itself'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.unsigned-hex-literal-with-no-alias -->
```maxon
function main() returns ExitCode
	let x = 0xFFFFFFFFFFFFFF9C
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.unsigned-hex-literal-with-no-alias.maxon:3:10: Integer literal '0xFFFFFFFFFFFFFF9C' is above i64.max, so it needs an alias whose range admits it: write '0xFFFFFFFFFFFFFF9C as <alias>'
```

<!-- test: error.unsigned-hex-literal-into-a-signed-alias -->
```maxon
typealias Offset = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 0xFFFFFFFFFFFFFF9C as Offset
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.unsigned-hex-literal-into-a-signed-alias.maxon:5:29: Value 18446744073709551516 is outside the range of 'Offset' (int(-9223372036854775808 to 9223372036854775807))
```

<!-- test: error.unsigned-binary-literal-with-no-alias -->
```maxon
function main() returns ExitCode
	let x = 0b1000000000000000000000000000000000000000000000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.unsigned-binary-literal-with-no-alias.maxon:3:10: Integer literal '0b1000000000000000000000000000000000000000000000000000000000000000' is above i64.max, so it needs an alias whose range admits it: write '0b1000000000000000000000000000000000000000000000000000000000000000 as <alias>'
```

<!-- test: error.u64-max-into-a-signed-alias -->
```maxon
typealias Small = int(-10 to 10)

function main() returns ExitCode
	let x = u64.max as Small
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.u64-max-into-a-signed-alias.maxon:5:18: Value 18446744073709551615 is outside the range of 'Small' (int(-10 to 10))
```

<!-- test: error.u64-max-into-a-signed-alias-at-top-level -->
```maxon
typealias Small = int(-10 to 10)

let X = u64.max as Small

function main() returns ExitCode
	print("{X}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.u64-max-into-a-signed-alias-at-top-level.maxon:4:17: Value 18446744073709551615 is outside the range of 'Small' (int(-10 to 10))
```

<!-- test: error.unsigned-literal-interpolated-with-no-alias -->
```maxon
function main() returns ExitCode
	print("{9223372036854775808}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.unsigned-literal-interpolated-with-no-alias.maxon:3:10: Integer literal '9223372036854775808' is above i64.max, so it needs an alias whose range admits it: write '9223372036854775808 as <alias>'
```

<!-- test: error.unsigned-literal-in-arithmetic-with-no-alias -->
```maxon
function main() returns ExitCode
	let y = 9223372036854775808 + 1
	print("{y}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.unsigned-literal-in-arithmetic-with-no-alias.maxon:3:10: Integer literal '9223372036854775808' is above i64.max, so it needs an alias whose range admits it: write '9223372036854775808 as <alias>'
```

<!-- test: error.a-folded-unsigned-constant-into-a-signed-alias -->
```maxon
typealias Small = int(-10 to 10)

function main() returns ExitCode
	let s = (u64.max - 1) as Small
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/literals/error.a-folded-unsigned-constant-into-a-signed-alias.maxon:5:24: Value 18446744073709551614 is outside the range of 'Small' (int(-10 to 10))
```

<!-- test: error.an-unsigned-literal-into-a-type-parameter -->
```maxon
function id(x T) uses T returns T
	return x
end 'id'

function main() returns ExitCode
	print("{id(0xFFFFFFFFFFFFFFFF)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/literals/error.an-unsigned-literal-into-a-type-parameter.maxon:7:13: Integer literal '0xFFFFFFFFFFFFFFFF' is above i64.max, so it needs an alias whose range admits it: write '0xFFFFFFFFFFFFFFFF as <alias>'
```

<!-- test: an-unsigned-literal-reassigned-to-an-unsigned-local -->
```maxon
function main() returns ExitCode
	var c = 0 as Count
	c = 0xFFFFFFFFFFFFFFFF
	print("{c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: an-unsigned-literal-in-a-ternary-arm-beside-an-unsigned-value -->
```maxon
function pick(a Count, flag bool) returns Count
	let c = (a if flag else 0xFFFFFFFFFFFFFFFF)
	return c
end 'pick'

function main() returns ExitCode
	print("{pick(1, flag: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: an-unsigned-literal-as-an-otherwise-fallback -->
```maxon
enum Miss implements Error
	none
end 'Miss'

function find() returns Count throws Miss
	throw Miss.none
end 'find'

function main() returns ExitCode
	let c = try find() otherwise 0xFFFFFFFFFFFFFFFF
	print("{c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: an-unsigned-literal-through-a-function-value -->
```maxon
typealias Show = function(Count) returns Count

function echo(c Count) returns Count
	return c
end 'echo'

function main() returns ExitCode
	let f = echo as Show
	print("{f(18446744073709551615)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: an-unsigned-value-above-i64-max-through-a-function-value -->
A function taken as a value still receives a `Count` above `i64.max`; its argument is checked where it is called, not by reading the word as signed.
```maxon
typealias Show = function(Count) returns Count

function echo(c Count) returns Count
	return c
end 'echo'

function top() returns Count
	return u64.max
end 'top'

function main() returns ExitCode
	let f = echo as Show
	print("{f(top())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: a-negative-value-through-a-function-value-is-refused-at-the-call -->
```maxon

typealias Show = function(Count) returns Count

function echo(c Count) returns Count
	return c
end 'echo'

function main() returns ExitCode
	let f = echo as Show

	for i in 0 upto 2 'each'
		print("{f(i - 1)}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-negative-value-through-a-function-value-is-refused-at-the-call.test:13: Range check failed: value outside typealias 'Count'
Stack trace:
  in main
  in mrt_start
```
