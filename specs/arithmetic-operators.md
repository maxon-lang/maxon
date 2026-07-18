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

### Division by zero

Integer `/` and `mod` are **fallible operations**: dividing by zero is not a silent 0, an
unhandled panic, or a CPU trap — the failure lives in the type system.

- A divisor the compiler can prove **non-zero** — a non-zero literal (`x / 4`), or a value
  whose ranged type excludes 0 (`int(1 to 100)`) — compiles to a bare divide with no check.
- A divisor the compiler cannot prove non-zero (a plain `int`, which includes 0) makes the
  divide **throw `DivisionByZero`**. Like any throwing operation it must be handled with
  `try`, or propagated:

  ```maxon
  let q = try (a / b) otherwise 0        // supply a fallback
  ```
  ```maxon
  // or give the divisor a non-zero type so the divide is provably safe:
  function ratio(a Integer, b int(1 to i64.max)) returns Integer
  	return a / b                       // bare divide, no check
  end 'ratio'
  ```

- A divisor the compiler holds as the constant **0** (`a / 0`) is neither recoverable nor
  safe — it is a bug, and is rejected at compile time.

Because the check is in the language rather than in a CPU trap, the behavior is identical on
every target (float division is unaffected and never throws).
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

