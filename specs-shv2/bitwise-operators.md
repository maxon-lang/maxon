---
feature: bitwise-operators
status: implemented
keywords: [bitwise, and, or, xor, shift, not, shl, shr, operators]
category: operators
---

# Bitwise Operators

## Documentation

Maxon provides bitwise operators for manipulating individual bits of integer values. The `and`, `or`, `xor`, and `not` keywords are context-dependent: they perform bitwise operations on integers and logical operations on booleans.

### Bitwise AND (`and`)

Returns 1 for each bit position where both operands have 1:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a and b  // 1000 = 8
```

### Bitwise OR (`or`)

Returns 1 for each bit position where either operand has 1:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a or b  // 1110 = 14
```

### Bitwise XOR (`xor`)

Returns 1 for each bit position where operands differ:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a xor b  // 0110 = 6
```

### Bitwise NOT (`not`)

Flips all bits of an integer value:

```maxon
var a = 5        // ...0101 in binary
var b = not a    // ...1010 = -6
```

### Left Shift (`shl`)

Shifts bits left by the specified amount, filling with zeros:

```maxon
var a = 1
var b = a shl 3  // 1000 = 8
```

### Right Shift (`shr`)

Shifts bits right by the specified amount:

```maxon
var a = 16
var b = a shr 2  // 0100 = 4
```

### Shift Count Range

An `int` is 64 bits, so the only shift distances that name distinct results are `0` through
`63`. A shift count written as a **literal** outside that range is a compile error (**E2054**):

```maxon
var a = 1
var b = a shl -1   // E2054: shift count -1 is outside the range 0 to 63
var c = a shl 64   // E2054
```

The rule exists because the hardware does not reject an out-of-range count — it **masks** it
into range, and the result is a plausible-looking wrong answer. `a shl -1` reads to a human as
"shift the other way", and would silently compute `a shl 63` — the *maximum left shift*, with
the opposite sign. `a shl 64` would silently compute `a shl 0`, leaving `a` unchanged.

A shift by a **runtime value** is unaffected and stays legal. The count is passed in `cl`,
where the hardware masks it, and the compiler has no fact to check:

```maxon
let count = 64
var d = a shl count  // legal: 64 masks to 0, so this is `a shl 0`
```

## Tests

<!-- test: bitwise-and -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a and b
end 'main'
```
```exitcode
8
```

<!-- test: bitwise-or -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a or b
end 'main'
```
```exitcode
14
```

<!-- test: bitwise-xor -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a xor b
end 'main'
```
```exitcode
6
```

<!-- test: left-shift -->
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 3
end 'main'
```
```exitcode
8
```

<!-- test: right-shift -->
```maxon
function main() returns ExitCode
	let a = 16
	return a shr 2
end 'main'
```
```exitcode
4
```

<!-- test: shift-chained -->
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 4 shr 2
end 'main'
```
```exitcode
4
```

<!-- test: bitwise-and-or-precedence -->
```maxon
function main() returns ExitCode
	// and has higher precedence than or
	// 12 and 10 = 8, then 8 or 1 = 9
	return 12 and 10 or 1
end 'main'
```
```exitcode
9
```

<!-- test: bitwise-xor-precedence -->
```maxon
function main() returns ExitCode
	// and has higher precedence than xor
	// 12 and 10 = 8, then 8 xor 3 = 11
	return 12 and 10 xor 3
end 'main'
```
```exitcode
11
```

<!-- test: shl-count-negative -->
A negative shift count reads as "shift the other way" and is not that at all: the hardware
masks it, so `shl -1` would silently become `shl 63` — the MAXIMUM left shift, a wrong answer
with the opposite sign. The compiler is holding the count, so it rejects it.
```maxon
function main() returns ExitCode
	let a = 1
	return a shl -1
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-negative.test:4:15: Shift count -1 is outside the range 0 to 63: an int is 64 bits, so any other count is silently masked into that range
```

<!-- test: shr-count-negative -->
The same rule, asked of `shr`.
```maxon
function main() returns ExitCode
	let a = 128
	return a shr -1
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shr-count-negative.test:4:15: Shift count -1 is outside the range 0 to 63: an int is 64 bits, so any other count is silently masked into that range
```

<!-- test: shl-count-64 -->
64 is one past the last distance a 64-bit shift distinguishes. Masked, it becomes `shl 0` —
which leaves the value UNCHANGED, the least likely thing the author meant.
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 64
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-64.test:4:15: Shift count 64 is outside the range 0 to 63: an int is 64 bits, so any other count is silently masked into that range
```

<!-- test: shl-count-100 -->
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 100
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-100.test:4:15: Shift count 100 is outside the range 0 to 63: an int is 64 bits, so any other count is silently masked into that range
```

<!-- test: shift-by-variable-count -->
⚠ THE GUARD AGAINST OVER-REJECTING. Only a LITERAL count is checked. A count that arrives as a
VALUE is still legal and still MASKS: it goes through `cl`, which reads only the low 6 bits. 64
masks to 0, so this is `7 shl 0` — 7, and not a compile error. Tightening E2054 to cover this
would break the shift-by-a-computed-distance idiom the language has always had.
```maxon
function main() returns ExitCode
	let a = 7
	let count = 64
	return a shl count
end 'main'
```
```exitcode
7
```

<!-- test: shift-by-parameter-count -->
The same, with a count no pass can see at all: a parameter. 65 masks to 1, so `7 shl 65` is
`7 shl 1` = 14.
```maxon
function shiftLeft(value ExitCode, count ExitCode) returns ExitCode
	return value shl count
end 'shiftLeft'

function main() returns ExitCode
	return shiftLeft(7, count: 65)
end 'main'
```
```exitcode
14
```

<!-- test: shift-vs-comparison -->
```maxon
function main() returns ExitCode
	// Shift has higher precedence than comparison
	// 1 shl 3 = 8, then 8 > 5 = true
	if 1 shl 3 > 5 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: bitwise-with-logical -->
```maxon
function main() returns ExitCode
	let a = 5 and 3        // 1
	if a > 0 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: bit-masking -->
```maxon
function main() returns ExitCode
	let flags = 5         // binary 101 (bit 0 and bit 2 set)
	return flags and 4    // returns 4 (bit 2 is set)
end 'main'
```
```exitcode
4
```

<!-- test: bit-clear -->
```maxon
function main() returns ExitCode
	var flags = 7        // binary 111
	// Clear bit 1 using xor
	flags = flags xor 2
	return flags         // 5 (binary 101)
end 'main'
```
```exitcode
5
```

<!-- test: power-of-two -->
```maxon
function main() returns ExitCode
	// Calculate 2^n using shift
	let n = 5
	return 1 shl n        // 32
end 'main'
```
```exitcode
32
```

<!-- test: divide-by-power-of-two -->
```maxon
function main() returns ExitCode
	// Divide by 4 using shift
	let value = 100
	return value shr 2    // 25
end 'main'
```
```exitcode
25
```

<!-- test: multiply-by-power-of-two -->
```maxon
function main() returns ExitCode
	// Multiply by 8 using shift
	let value = 13
	return value shl 3    // 104
end 'main'
```
```exitcode
104
```

<!-- disabled-test: bitwise-not-basic -->
<!-- P1.2 String - `print` and string interpolation -->
```maxon
function main() returns ExitCode
	print("{not 0}\n")
	return 0
end 'main'
```
```stdout
-1
```
```exitcode
0
```

<!-- disabled-test: bitwise-not-value -->
<!-- P1.2 String - `print` and string interpolation -->
```maxon
function main() returns ExitCode
	let a = 5
	print("{not a}\n")
	return 0
end 'main'
```
```stdout
-6
```
```exitcode
0
```

<!-- test: bitwise-not-double -->
```maxon
function main() returns ExitCode
	let a = 42
	return not not a
end 'main'
```
```exitcode
42
```

<!-- test: bitwise-not-masking -->
```maxon
function main() returns ExitCode
	let value = 125    // 0x7D
	// Clear lower 4 bits: 125 and not 15 = 112
	return value and not 15
end 'main'
```
```exitcode
112
```

<!-- disabled-test: bitwise-not-const -->
<!-- globals - a top-level `var`/`let`. shv2 has no writable data section, no global load/store ops, and no cross-function binding scope. The ELISION these cases test is proven by specs-shv2/short-circuit-elision.md; also P1.2 String - `print` and string interpolation -->
```maxon
let MASK = not 0xFF

function main() returns ExitCode
	print("{MASK}\n")
	return 0
end 'main'
```
```stdout
-256
```
```exitcode
0
```

<!-- disabled-test: shr-in-method-call-arg -->
<!-- P1.7 Array - array literals and `.push()`/`.get()` -->
```maxon
function main() returns ExitCode
	var buf = [0, 0]
	buf.push(42)
	let x = 0xABCD
	buf.push(x shr 9)
	return try buf.get(3) otherwise 0
end 'main'
```
```exitcode
85
```

<!-- disabled-test: shr-consecutive-method-calls -->
<!-- P1.7 Array - array literals and `.push()`/`.get()` -->
```maxon
function main() returns ExitCode
	var buf = [0]
	let value = 0xAABBCCDD
	buf.push(value and 0xFF)
	buf.push((value shr 8) and 0xFF)
	buf.push((value shr 16) and 0xFF)
	buf.push((value shr 24) and 0xFF)
	let b0 = try buf.get(1) otherwise 0
	let b1 = try buf.get(2) otherwise 0
	let b2 = try buf.get(3) otherwise 0
	let b3 = try buf.get(4) otherwise 0
	if b0 != 0xDD 'c0'
		return 10
	end 'c0'
	if b1 != 0xCC 'c1'
		return 20
	end 'c1'
	if b2 != 0xBB 'c2'
		return 30
	end 'c2'
	if b3 != 0xAA 'c3'
		return 40
	end 'c3'
	return 0
end 'main'
```
```exitcode
0
```


<!-- disabled-test: logical-or-and-on-bool-fields -->
<!-- P1.1 structs - a `type` with `bool` fields -->
`or` / `and` / `xor` are LOGICAL when their operands are `bool` (and bitwise on
ints). A logical word-op over `bool` struct fields produces a `bool`, so its
result flows into a `bool` parameter — the op must NOT silently type as `int`.
Here `flags.a or flags.b` and `flags.a and flags.c` are passed to
`consume(flag bool)`; both must type-check and dispatch the right branch.
```maxon
typealias Tag = int(0 to 100)

type Flags
	export var a as bool
	export var b as bool
	export var c as bool

	export static function make(a bool, b bool, c bool) returns Flags
		return Flags{a: a, b: b, c: c}
	end 'make'
end 'Flags'

function consume(flag bool) returns Tag
	if flag 'isTrue'
		return 1
	end 'isTrue'
	return 0
end 'consume'

function main() returns ExitCode
	let f = Flags.make(true, b: false, c: true)
	let orResult = consume(f.a or f.b)
	let andResult = consume(f.a and f.c)
	let bothFalse = consume(f.b or f.b)
	return orResult * 4 + andResult * 2 + bothFalse
end 'main'
```
```exitcode
6
```
