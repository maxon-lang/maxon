---
feature: arithmetic-operators
status: stable
keywords: [operators, arithmetic, add, subtract, multiply, divide, modulo]
category: operators
---

# Arithmetic Operators

## Documentation

Maxon supports standard arithmetic operations on numeric types.

### Operators

- `+` - Addition
- `-` - Subtraction
- `*` - Multiplication
- `/` - Division (int/int produces truncating int; float/float produces float)
- `mod` - Modulo (remainder after division, integers only)

### Precedence

Multiplication, division, and modulo have higher precedence than addition and subtraction:
```text
2 + 3 * 4  // Evaluates to 14, not 20
```
### Example

```maxon
function main() returns ExitCode
	let a = 10
	let b = 3
	let sum = a + b          // 13
	let diff = a - b         // 7
	let prod = a * b         // 30
	let div = a / b          // 3 (truncating integer division)
	let rem = a mod b        // 1

	// Use the values
	print("{sum}\n")
	print("{diff}\n")
	print("{prod}\n")
	print("{div}\n")
	print("{rem}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
13
7
30
3
1
```


## Tests

<!-- test: addition -->
```maxon
function main() returns ExitCode
	return 5 + 3
end 'main'
```
```exitcode
8
```


<!-- test: multiplication -->
```maxon
function main() returns ExitCode
	return 6 * 7
end 'main'
```
```exitcode
42
```


<!-- test: precedence -->
```maxon
function main() returns ExitCode
	return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: division-truncating-int -->
```maxon
function main() returns ExitCode
	return 20 / 3
end 'main'
```
```exitcode
6
```


<!-- test: trunc-division-optimizes -->
```maxon
function main() returns ExitCode
	return 20 / 3             // int/int = truncating int, returns 6
end 'main'
```
```exitcode
6
```


<!-- test: variable-division-optimizes -->
```maxon
function main() returns ExitCode
	let a = 7
	let b = 2
	return a / b              // int/int = truncating int, returns 3
end 'main'
```
```exitcode
3
```


<!-- test: negative-division -->
```maxon
function main() returns ExitCode
	let neg = -7
	let a = neg / 2           // -7/2 = -3 (truncating toward zero)
	if a == -3 'pass'
			return 0
	end 'pass'
	return 1
end 'main'
```
```exitcode
0
```


<!-- test: modulo -->
```maxon
function main() returns ExitCode
	return 17 mod 5
end 'main'
```
```exitcode
2
```


<!-- test: complex-expression -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 3
	let result = (a + b) * 2 - a / b
	return result
end 'main'
```
```exitcode
23
```


<!-- test: mixed-width-operands -->
**An operator with operands of DIFFERENT declared widths — a wasm codegen regression test.** `r`
is a narrow ranged int (`ExitCode`) and `j` is a plain `int`; `r + j` is a binary op whose two
operands were left different WIDTHS by the frontend (an int→int width change carries no IR
conversion op — the value just flows). The register backends never notice — every value sits in a
64-bit register — but wasm's typed stack does: each operand must be pushed at the op's width, so the
narrow one is coerced exactly as a `return` or a phi edge coerces. Without it the emitted core module
fails validation. `base()` is 5 and `j` is 10, so `r + j` = 15. (Same gap covers `<`, `mod`, `/`.)
```maxon
function base() returns ExitCode
	return 5
end 'base'

function widen(j Integer) returns Integer
	let r = base()
	return r + j
end 'widen'

function main() returns ExitCode
	return widen(10)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
15
```

