---
title: Types
description: Primitive types, bit patterns, characters and strings, primitive conformances, and conversions.
sidebar:
  order: 2
---

Maxon has a small set of built-in types. Numbers are always used through a **named, ranged
typealias** — the name documents what the number means and the range documents which values are legal.

| Type | What it is | Written in a declaration as |
|------|------------|-----------------------------|
| `int(lo to hi)` | 64-bit integer quantity restricted to a range | a typealias: `typealias Age = int(0 to 150)` |
| `float(lo to hi)` | IEEE-754 double (64-bit) restricted to a range | a typealias: `typealias Ratio = float(0.0 to 1.0)` |
| `bits(n)` | raw bit pattern of width 1, 2, 4, 8, 16, 32 or 64 | a typealias: `typealias Hash = bits(64)` |
| `bool` | `true` or `false` | `bool` |
| `Character` | one user-perceived character (an extended grapheme cluster) | `Character` |
| `String` | UTF-8 text | `String` |
| `cstring` | NUL-terminated byte pointer, for runtime interop only | `cstring` |

There is no separate `byte` or `short` type: a byte is an `int` alias whose range fits in 8 bits, and the
compiler stores it in one byte where that matters (see [Storage](#storage)).

## Primitives Go Through a Typealias

The keywords `int` and `float` may appear only on the right-hand side of a `typealias`. Everywhere else — a
parameter, a return type, a field, a cast target, a generic type argument — name an alias instead:

```maxon
typealias Port = int(0 to 65535)
typealias Celsius = float(-273.15 to 1000.0)

function connect(port Port) returns bool
	return port != 0
end 'connect'
```

A bare `int` in a declaration is **E3005** (`Cannot use bare 'int' as a type. Define a typealias with
range constraints, e.g., typealias MyInt = int(0 to 100)`); a bare `int` type argument (`Array with int`)
is **E2061**. `bool` and `cstring` are used bare. Local variables never need an annotation — the type is
inferred from the initializer:

```maxon
let count = 0          // an integer
let ratio = 0.5        // a float
let done = false       // bool
```

The standard library declares a few aliases every file can use, among them `ExitCode`, `Byte`
(`int(0 to u8.max)`), `ByteArray` (`Array with Byte`), `Codepoint` (`int(0 to 1114111)`), `HashValue`
(`int(0 to u32.max)`), `SourceLineNumber` and `Real` (`float(f64.min to f64.max)`).
[Ranged Type Aliases](/docs/language/ranged-typealiases/) covers declaring your own.

## Integers

An integer alias declares a **quantity**: `int(lo to hi)` includes `hi`, `int(lo upto hi)` excludes it.
Bounds are literals or the limits of a sized type: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`
(`.min` / `.max`).

```maxon
typealias Score = int(0 to 100)
typealias Offset = int(i64.min to i64.max)
typealias Tally = int(0 to u64.max)
```

- **Literals** are decimal, hexadecimal (`0xff`), binary (`0b1010`) or octal (`0o777`), with optional `_`
  separators. An unannotated literal is a signed 64-bit value; one outside that range is **E2011**. A
  literal carries no alias, so it fits any integer alias it is in range for.
- **Arithmetic** is `+ - * / mod`, always computed at 64 bits. `/` truncates toward zero (`-7 / 2` is
  `-3`). A divisor that might be zero makes the division throw — see
  [Division by Zero](/docs/language/expressions/#division-by-zero).
- **Overflow wraps.** Integer arithmetic is two's-complement: `i64.max + 1` is `i64.min`. A range is not
  enforced on every intermediate result; it is checked where a value reaches a place declared with the
  alias (see [Range Checks](/docs/language/ranged-typealiases/#range-checks)).
- **Bitwise** operations use the word operators `and`, `or`, `xor`, `not`, `shl` and `shr` (see
  [Expressions](/docs/language/expressions/#logical-and-bitwise-operators)).
- An alias whose lower bound is `0` is **unsigned**: `shr` zero-fills it rather than extending the sign,
  and its range check refuses a negative value instead of letting an underflow become a huge number.
  Because a check sees only the 64 bits, a quantity cannot carry a value above `i64.max` through a
  checked door — `int(0 to u64.max)` panics on one. A value that genuinely needs all 64 bits is a bit
  pattern: use [`bits(64)`](#bit-patterns--bitsn).

## Floats

`float` is a 64-bit IEEE-754 double. A float literal needs a decimal point (`3.14`, `1.0`, `1.5e3`).

```maxon
typealias Real = float(f64.min to f64.max)

function area(radius Real) returns Real
	return 3.14159 * radius * radius
end 'area'

function main() returns ExitCode
	print("{area(2)}\n")        // 12.56636
	print("{1.0 / 3.0}\n")      // 0.3333333333333333
	print("{100.0} {1.5e3}\n")  // 100.0 1500.0
	return 0
end 'main'
```

- A float prints as the shortest decimal that reads back as the same value, and always shows a fractional
  part (`100.0`).
- `-x` flips the sign bit, so `-0.0` is distinct from `0.0` when printed.
- `inf` and `NaN` produced by arithmetic are ordinary IEEE values; only division *by zero* is intercepted.
  There is no float `mod`.
- A float alias bounded by `f32.min to f32.max` is still stored as a double; the bound is a range check.

## Booleans

`bool` has the literals `true` and `false`. Conditions must be `bool` — there is no truthiness, so
`if count 'x'` is **E3005** (`'if' requires a bool condition, got 'int'`). `bool` and `int` do not mix:
`flag + 1` is **E2004** and comparing a `bool` with an `int` is **E3005**. On `bool` operands, `and` and
`or` are logical and short-circuit.

## Bit Patterns — `bits(n)`

An `int(...)` alias is a quantity whose every door is range-checked. A **raw bit pattern** — a hash, a
mask, an address — can legitimately set bit 63, so it has its own spelling:

```maxon
typealias Word = bits(64)
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let a = 0xF0 as Word
	let b = 0x0F as Word
	print("{a or b} {a and b} {a shl 4}\n")     // 255 0 3840

	let all = u64.max as Word
	print("{all} {all as Integer}\n")           // 18446744073709551615 -1
	return 0
end 'main'
```

- Legal widths are 1, 2, 4, 8, 16, 32 and 64; any other width is **E3148**.
- `bits(64)` accepts every 64-bit pattern, so a cast into it never fails. A narrower width asks one
  question — does the value fit in `n` bits — and panics if not. Truncation is explicit:
  `x and 0xFFFFFFFF`.
- Casting between a pattern and a signed alias reinterprets the bits without a check: `bits(64)` all-ones
  as `Integer` is `-1`.
- `bits` is not a keyword; it is recognized only as `bits(` on the right of a typealias.

## Characters and Strings

`Character` is one user-perceived character — an extended grapheme cluster, which may be several
Unicode codepoints and several UTF-8 bytes. `String` is UTF-8 text; iterating a `String` yields
`Character`s and `count()` counts characters, not bytes.

```maxon
function main() returns ExitCode
	let c = 'é'
	let s = "héllo"
	print("{c} is {c.bytes().count()} bytes\n")                              // é is 2 bytes
	print("{s} has {s.count()} characters and {s.bytes().count()} bytes\n") // 5 characters, 6 bytes

	for ch in s 'each'
		print("[{ch}]")
	end 'each'
	print("\n")

	var joined = "{c}"
	joined.append(s)
	print("{joined}\n")                                                      // éhéllo
	return 0
end 'main'
```

- `Character` supports `==` and ordering (`<`, `>`); `String` supports only `==` and `!=`
  (ordering a `String` is **E3005**).
- `+` is not defined on `String`; build text with interpolation (`"{a}{b}"`) or `append`.
- A `Character` and a `String` never compare: `'a' == "a"` is **E3005**.
- A character literal next to an integer operand becomes its codepoint (see
  [Implicit Conversions](#implicit-conversions)); otherwise `c - 1` on a `Character` is **E2004**.
- The [standard library](/docs/stdlib/) documents the `String` and `Character` methods.

## Storage

Integer arithmetic is always 64-bit, but an alias with a **non-negative** range is stored in the smallest
unsigned width that holds it wherever values are packed: array elements and module-level variables.

| Range of the alias | Array element storage |
|--------------------|-----------------------|
| `0 to 1` | 1 bit |
| `0 to 3` | 2 bits |
| `0 to 15` | 4 bits |
| `0 to u8.max` | 1 byte |
| `0 to u16.max` | 2 bytes |
| `0 to u32.max` | 4 bytes |
| anything wider, or a negative lower bound | 8 bytes |

`bool` array elements take one bit, and `bits(n)` packs like the unsigned range of the same width. Struct
fields and local variables use 8 bytes. `sizeof(T)` reports a type's size in bytes (see
[Expressions](/docs/language/expressions/#sizeof-and-countof)).

A `type` whose fields are all `let` and all of packable types, summing to 64 bits or fewer, is itself one
8-byte word rather than a heap record: `sizeof` is 8, and `Array with T` is a dense 8-byte element. See
[Inline Packed Records](/docs/language/composite-types/#inline-packed-records).

## Primitive Conformances

The primitives implement the standard interfaces directly: `int` and `float` are `Hashable`, `Equatable`,
`Comparable`, `Stringable` and `Cloneable`; `bool` is `Comparable`, `Stringable` and `Cloneable`;
`Character` is `Hashable`, `Equatable`, `Comparable`, `Stringable` and `Cloneable`; `String` is
`Hashable`, `Equatable`, `Cloneable` and `Iterable`. Every integer alias therefore works as a `Map` key or `Set` element. You can add methods to
a primitive with an [extension](/docs/language/composite-types/#extensions-over-primitives).

## Type Conversions

### Explicit Conversions (`as`)

`value as Alias` converts to a named alias. The target is always an alias (or `bool`); a bare `int` or
`float` target is **E3005**.

```maxon
typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function main() returns ExitCode
	let b = 200 as Byte
	let wide = b as Integer      // widening: no run-time check
	let r = wide as Real         // integer to float
	let back = wide as Byte      // narrowing: checked at run time
	print("{wide} {r} {back}\n") // 200 200.0 200
	return 0
end 'main'
```

| Cast | Result |
|------|--------|
| integer alias → integer alias | legal; widening emits no check, narrowing panics if the value is out of range |
| integer alias or literal → float alias | legal |
| integer alias ↔ `bits(n)` | legal (see [Bit Patterns](#bit-patterns--bitsn)) |
| literal → alias | checked at compile time: `256 as Byte` is **E3005** (`Value 256 is outside the range of 'Byte'`) |
| float → integer | **E3009** `Cannot cast from float to int` — use a rounding function |
| `bool` ↔ number, `String` → number, struct ↔ anything | **E3009** |
| a value to its own alias (`b as Byte` when `b` is a `Byte`) | **E3010** `unneeded cast` |
| one container instance to a different one | **E3131** |

### Floats to Integers

A float never converts to an integer implicitly (**E3009** `cannot implicitly convert 'float' to 'int'`).
Choose the rounding explicitly:

| Function | Result | Example |
|----------|--------|---------|
| `trunc(x)` | integer, toward zero | `trunc(-2.7)` is `-2` |
| `round(x)` | float, nearest, ties to even | `round(2.5)` is `2.0` |
| `floor(x)` | float, toward negative infinity | `floor(-2.5)` is `-3.0` |
| `ceil(x)` | float, toward positive infinity | `ceil(2.1)` is `3.0` |

`trunc` is the one that produces an integer; apply it to the result of `round`, `floor` or `ceil` to get
an integer from those.

### Implicit Conversions

Maxon converts implicitly in only these places:

- **Integer to float in mixed arithmetic and float slots.** `let x = 5` then `x + 2.0` is `7.0`, and an
  unaliased integer (a literal, or a local initialized from one) is accepted where a float is expected. A
  value of a *named* integer alias still needs `as` to reach a float-alias parameter.
- **A character literal beside an integer** is that character's codepoint:

  ```maxon
  let cp = 45
  if cp == '-' 'dash'           // '-' is 45
  	print("{cp - '0'}\n")     // '0' is 48
  end 'dash'
  ```

- **A payload-free enum case where a number is expected** is its raw value — see
  [Implicit Coercion to the Backing Primitive](/docs/language/enums-unions/#implicit-coercion-to-the-backing-primitive).
- **`return`** converts to the declared return alias, exactly as the written cast would (see
  [Ranged Type Aliases](/docs/language/ranged-typealiases/#aliases-are-distinct-types)).

Every other change of type is written with `as`.
