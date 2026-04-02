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
	return 0xff
end 'main'
```
```exitcode
255
```

<!-- test: hex-integer-uppercase -->
```maxon
function main() returns ExitCode
	return 0xaB
end 'main'
```
```exitcode
171
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
	var x = 1_000
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
	return 0b1111_0000
end 'main'
```
```exitcode
240
```

<!-- test: large-hex-literal -->
```maxon
// Test hex literal above 32-bit range (0x140000000 = 5368709120)
function main() returns ExitCode
	var x = 0x0000000140000000
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
	var x = 0x0000_0001_4000_0000
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
	var x = 9223372036854775807
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
	var x = 5368709120
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
	var x = 3.14
	return trunc(x)
end 'main'
```
```exitcode
3
```


<!-- test: boolean -->
```maxon
function main() returns ExitCode
	var flag = true
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
	var x = 1.5e2
	return trunc(x) - 140
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-negative-exponent -->
```maxon
function main() returns ExitCode
	var x = 5.0e-1
	return trunc(x * 20.0)
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-explicit-positive -->
```maxon
function main() returns ExitCode
	var x = 2.5e+02
	return trunc(x) - 240
end 'main'
```
```exitcode
10
```

<!-- test: scientific-notation-uppercase -->
```maxon
function main() returns ExitCode
	var x = 1.0E3
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
	var x = 99999999999999999999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.int-overflow.test:3:10: Integer literal '99999999999999999999' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- test: error.hex-overflow -->
```maxon
function main() returns ExitCode
	var x = 0x1ffffffffffffffff
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.hex-overflow.test:3:10: Integer literal '0x1ffffffffffffffff' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- test: error.binary-overflow -->
```maxon
function main() returns ExitCode
	var x = 0b10000000000000000000000000000000000000000000000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.binary-overflow.test:3:10: Integer literal '0b10000000000000000000000000000000000000000000000000000000000000000' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- test: error.octal-overflow -->
```maxon
function main() returns ExitCode
	var x = 0o2000000000000000000000
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.octal-overflow.test:3:10: Integer literal '0o2000000000000000000000' is outside the range of int (-9223372036854775808 to 9223372036854775807)
```

<!-- test: error.float-overflow -->
```maxon
function main() returns ExitCode
	var x = 1.0e999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/literals/error.float-overflow.test:3:10: Float literal '1.0e999' is outside the range of float
```
