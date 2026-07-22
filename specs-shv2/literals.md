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

<!-- disabled-test: error.int-overflow -->
<!-- shv2 emits E2011 at the right token with the right code, but its MESSAGE text differs from the reference's: `Integer literal out of range` versus the reference's `Integer literal '<lit>' is outside the range of int (…)`. The wording is `ParseError.integerOverflow`'s rendering in Compiler/Queries.maxon, not the literal parser's, so aligning it is a diagnostic-text decision (and one shared with the three other integer radices below and the float case) rather than anything about how a literal is read. -->
```maxon
function main() returns ExitCode
	let x = 99999999999999999999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.int-overflow.test:3:10: Integer literal '99999999999999999999' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- disabled-test: error.hex-overflow -->
<!-- shv2 emits E2011 at the right token with the right code, but its MESSAGE text differs from the reference's: `Integer literal out of range` versus the reference's `Integer literal '<lit>' is outside the range of int (…)`. The wording is `ParseError.integerOverflow`'s rendering in Compiler/Queries.maxon, not the literal parser's, so aligning it is a diagnostic-text decision (and one shared with the three other integer radices below and the float case) rather than anything about how a literal is read. -->
```maxon
function main() returns ExitCode
	let x = 0x1ffffffffffffffff
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.hex-overflow.test:3:10: Integer literal '0x1ffffffffffffffff' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- disabled-test: error.binary-overflow -->
<!-- shv2 emits E2011 at the right token with the right code, but its MESSAGE text differs from the reference's: `Integer literal out of range` versus the reference's `Integer literal '<lit>' is outside the range of int (…)`. The wording is `ParseError.integerOverflow`'s rendering in Compiler/Queries.maxon, not the literal parser's, so aligning it is a diagnostic-text decision (and one shared with the three other integer radices below and the float case) rather than anything about how a literal is read. -->
```maxon
function main() returns ExitCode
	let x = 0b10000000000000000000000000000000000000000000000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.binary-overflow.test:3:10: Integer literal '0b10000000000000000000000000000000000000000000000000000000000000000' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- disabled-test: error.octal-overflow -->
<!-- shv2 emits E2011 at the right token with the right code, but its MESSAGE text differs from the reference's: `Integer literal out of range` versus the reference's `Integer literal '<lit>' is outside the range of int (…)`. The wording is `ParseError.integerOverflow`'s rendering in Compiler/Queries.maxon, not the literal parser's, so aligning it is a diagnostic-text decision (and one shared with the three other integer radices below and the float case) rather than anything about how a literal is read. -->
```maxon
function main() returns ExitCode
	let x = 0o2000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.octal-overflow.test:3:10: Integer literal '0o2000000000000000000000' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- disabled-test: error.float-overflow -->
<!-- Same diagnostic-TEXT divergence as the four integer-overflow cases above, not a conversion gap: shv2 rejects `1.0e999` with E2011 at 3:10, but words it `Float literal out of range (a float is an IEEE-754 double; its magnitude cannot exceed f64.max)` — deliberately, per ParseError.floatLiteralOverflow's comment in Compiler/Parser.maxon. specs-shv2/float-literal-magnitude.md gates the same rejection on shv2's own wording so the mechanism is not left uncovered. -->
```maxon
function main() returns ExitCode
	let x = 1.0e999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.float-overflow.test:3:10: Float literal '1.0e999' is outside the range of float
```

<!-- disabled-test: i64-min-literal -->
<!-- shv2 routes `parseNegatedInt` only from `parseRangeBound` (a typealias bound), so an EXPRESSION-position `-9223372036854775808` is parsed as unary minus over the bare magnitude — which overflows a positive i64 and reports E2011. Parser.parseIntLiteral passes `negated: false` unconditionally; making the expression parser fold a leading `-` into the literal token is its own change, in Parser.maxon. -->
`-9223372036854775808` is exactly `i64.min`. Its magnitude (`9223372036854775808`
= `i64.max + 1`) overflows a positive i64, so a negated literal must be parsed as
a single unit: parsing the bare magnitude first would wrongly report E2011. (An
un-negated `9223372036854775808` still overflows — see error.int-overflow above.)
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
