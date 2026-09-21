# Maxon Language Reference

This reference describes the syntax and semantics of the Maxon language. The grammar is in
[BNF_SYNTAX.md](BNF_SYNTAX.md), the standard library in the [standard library reference](STDLIB_REFERENCE.md), and
the `maxon` command in the [CLI reference](CLI_REFERENCE.md).

## Program Structure

### Entry Point

A program starts at a function named `main` that returns `ExitCode`:

```maxon
function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

The value `main` returns is the process exit code. `ExitCode`'s range depends on the target: `0` to
`u32.max` on Windows, `0` to `255` on Linux, macOS and WASI. A literal outside that range is a compile error,
and a computed one panics at the `return`. A project run with [`maxon test`](#testing)
needs no `main`; a program compiled with `maxon build` or `maxon execute` without one is **E3001**.

### Files and Projects

- A program is one `.maxon` file or a directory of them, compiled together. There are no `import`
  statements.
- A file contains top-level declarations: functions, types, enums, unions, interfaces, extensions,
  typealiases, variables and — in `*.test.maxon` files — tests. There are no top-level statements.
- A file's namespace comes from its directory (see [Namespaces](#namespaces)).
- Declarations are private to their file unless marked `export`, `public` or `module`.
- Declaration order does not matter: a function may call one declared later or in another file.
- The standard library is available to every file.

### Blocks and Labels

Every block opens with a keyword and a quoted **label**, and closes with `end` and the same label:

```maxon
typealias Count = int(0 to 100)

function countDown(from Count)
	var n = from
	while n > 0 'tick'
		print("{n}\n")
		n = n - 1
	end 'tick'
end 'countDown'
```

A function's label is its name; a type's, enum's, union's, interface's or extension's label is the type's
name; a test's label is its quoted name. Labels on `if`, `else`, `while`, `for`, `match`, `try` and `otherwise`
blocks are chosen by you and name the block for `break` and `continue`. A label written after `end` must be
the block's own label: anything else is **E2008** (`Mismatched end label: expected 'tick', got 'loop'`), and
**E2043** for a `match`. Only a `test` must write it; on every other block the label after `end` may be
omitted. Statements end at the end of the line; there are no semicolons and no braces around blocks.

### Conditional Compilation

`#if`, `#else` and `#endif` include or exclude code by compilation target. They are evaluated while parsing:

```maxon
#if os(Windows)
let separator = "\\"
#else
let separator = "/"
#endif

function main() returns ExitCode
	#if (os(Linux) or os(Macos)) and arch(x64)
		print("x64 Unix\n")
	#else
		print("separator {separator}\n")
	#endif

	return 0
end 'main'
```

**Conditions:**

- `os(Windows)`, `os(Linux)`, `os(Macos)`, `os(Wasi)` — the target operating system
- `arch(x64)`, `arch(arm64)`, `arch(wasm32)` — the target architecture
- `testing(...)`, `rcSanitize(...)` and `leakReport(...)` name build flags the compiler does not have, so
  each flag is off: `testing(false)` holds and `testing(true)` never does, including under `maxon test`
- `debugstream(true)` holds in a `--debugstream` build and `debugstream(false)` otherwise

An `os` or `arch` argument the compiler does not recognize is false rather than an error, so portable code
can name a platform the compiler does not target.

Conditions combine with `not`, `and` and `or` (in increasing looseness) and parentheses. An unknown condition
is **E2064**.

Directives may surround top-level declarations, statements inside a function body, and members inside a
`type`, `enum`, `union`, `interface` or `extension` body. `#if` blocks nest.

---

## Lexical Elements

Maxon source is UTF-8 text.

### Comments

```maxon
// A line comment runs to the end of the line.

/* A block comment
   may span lines. */
```

Block comments do not nest: the first `*/` ends the comment. An unterminated block comment is **E1007**.

### Doc Comments

A line starting with `///` is a **doc comment**. Consecutive `///` lines directly above a declaration
document it; editors show them when hovering over the declaration or its uses, and `maxon fmt` preserves
them.

```maxon
typealias Size = int(0 to 100)

/// Doubles a size.
/// Shown when hovering over `twice` in an editor.
function twice(n Size) returns Size
	return n * 2
end 'twice'
```

A doc comment is not a statement, so a block containing only a doc comment is still empty (**E3082**).

### Shebang Line

When a file's first two bytes are `#!`, the first line is ignored, so a Maxon file can be run directly as a
script:

```maxon
#!/usr/bin/env maxon

function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

The line must start at the very first byte of the file; `#!` anywhere else is **E1009** (`Unknown compiler
directive`). Line numbers in diagnostics still count the shebang line. `maxon fmt` keeps it as written.
Running such a file is [`maxon execute`](CLI_REFERENCE.md).

### Identifiers

```text
identifier = [a-zA-Z_][a-zA-Z0-9_]*
```

- An identifier starts with a letter or underscore, continues with letters, digits and underscores, and is
  case-sensitive.
- A keyword cannot name a variable (`let type = 3` is **E2010**). A keyword *can* name a function, a
  parameter, or an enum or union case, since those are always written in a position where the keyword
  reading is impossible.
- Names beginning with `__` are reserved for the compiler and runtime (**E2051**).
- `self` is the method receiver and cannot be bound by any declaration.
- `_` alone is the discard name, not a variable.

### Keywords

```text
and        as         async      await      bool       break      continue   countof
cstring    default    else       end        enum       export     extends    extension
fallthrough false     float      for        from       function   gives      if
ignore     implements in         int        interface  is         let        match
mod        not        or         otherwise  panic      public     return     returns
self       Self       shl        shr        sizeof     static     then       throw
throws     to         true       try        type       typealias  union      upto
uses       var        where      while      with       xor
```

**Contextual keywords** have a special meaning only in one position and are ordinary identifiers everywhere
else:

| Word | Special when |
|------|--------------|
| `module` | directly before a declaration, as a visibility modifier |
| `test` | at the top level, followed by a quoted name (see [Testing](#testing)) |
| `spawn` | followed by a type's static factory call (see [Services](#services--spawn)) |
| `bits` | followed by `(` on the right-hand side of a typealias |

`__line__` and `__file__` are valid only as parameter defaults (see
[Caller-Location Defaults](#caller-location-defaults-__line__-__file__)).

### Integer Literals

```maxon
let decimal = 42
let negative = -17
let hex = 0xff           // 255
let binary = 0b1010      // 10
let octal = 0o17         // 15
let million = 1_000_000  // underscores separate digits
```

An integer literal is a signed 64-bit value, `-9223372036854775808` to `9223372036854775807`; one outside that
range is **E2011**. A literal has no typealias of its own, so it fits any integer alias it is in range for.

### Float Literals

```maxon
let pi = 3.14159
let small = -2.5
let big = 1.5e3          // 1500.0
```

A float literal contains a decimal point, optionally followed by an exponent (`e` or `E`, with an optional
sign). `1e10` without a decimal point is not a float literal. A value beyond the float range is **E2011**.

### Boolean Literals

`true` and `false`.

### Character Literals

A character literal holds exactly one user-perceived character — which may be several codepoints and
several UTF-8 bytes — in single quotes:

```maxon
function main() returns ExitCode
	let letter = 'A'
	let accented = 'é'
	let cjk = '中'
	let emoji = '🎉'
	let quote = '\''
	let sigma = 'Σ'
	print("{letter}{accented}{cjk}{emoji}{quote}{sigma}\n")     // Aé中🎉'Σ
	return 0
end 'main'
```

An empty literal `''`, or one holding two characters, is **E2016**. A character literal beside an integer
operand is its codepoint (`cp - '0'`).

### String Literals

```maxon
let greeting = "Hello, World!"
let lines = "Line1\nLine2"
let quoted = "Quote: \"text\""
let hi = "\x48\x69"          // "Hi"
let bang = "hello!"     // "hello!"
```

A string literal cannot contain a raw newline; use `\n`.

**Escape sequences:**

| Escape | Meaning |
|--------|---------|
| `\n` `\t` `\r` `\0` | newline, tab, carriage return, NUL |
| `\\` `\"` `\'` | backslash, double quote, single quote |
| `\{` `\}` | literal braces (string literals only) |
| `\xNN` | the codepoint `U+00NN` (exactly two hex digits) |
| `\uXXXX` | the codepoint `U+XXXX` (exactly four hex digits) |

In a `String`, `\xNN` is a codepoint, not a raw byte: `"\xFF"` is the character `ÿ`, two bytes in UTF-8. Use a
byte string for raw bytes. A malformed escape is **E1004**.

### String Interpolation

`{expression}` inside a string literal inserts the expression's value:

```maxon
function main() returns ExitCode
	let name = "World"
	let x = 5
	print("Hello, {name}!\n")          // Hello, World!
	print("{x} * 2 = {x * 2}\n")       // 5 * 2 = 10
	print("Use \{braces\}\n")          // Use {braces}
	return 0
end 'main'
```

Numbers, `bool`, `Character` and `String` interpolate directly. A type interpolates if it has a
`toString()` method (`Stringable`). A `{` that does not open an interpolation must be escaped as `\{`
(**E1006**).

**Format specifiers.** `{expression:spec}` formats a number:

- Integers: `[0][width][base]` — `0` pads with zeros, `width` is the minimum width (right-aligned), and
  `base` is `x`, `X`, `o` or `b` (decimal otherwise). Hexadecimal, octal and binary print the bit pattern,
  never a sign.
- Floats: `[0][width][.precision]` — `precision` is the number of decimal places, rounded half to even;
  without it the float keeps its shortest round-trip spelling. A width never changes the digits.

```maxon
function main() returns ExitCode
	let n = 42
	print("{n:04} {n:6} {n:x} {n:04X} {-42:06} {255:08b}\n")   // 0042     42 2a 002A -00042 11111111

	let f = 3.14159
	print("{f:.2} {f:.4} {f:8.2} {-3.14:08} {2.5:.0}\n")       // 3.14 3.1416     3.14 -0003.14 2
	return 0
end 'main'
```

A specifier has no effect on a `String` or `bool`. A type that implements `FormattedStringable` receives the
specifier text in `toString(format String)`, so `{value:verbose}` calls `value.toString("verbose")`.

### Byte String Literals

`b"..."` creates a `ByteArray` (`Array with Byte`) directly:

```maxon
let bytes = b"hello"        // [104, 101, 108, 108, 111]
let empty = b""
let raw = b"\xFF\x00"       // [255, 0]
let latin = b"À"            // [192]
```

Every character becomes exactly one byte: `\xNN` is the byte `NN`, and a character or `\uXXXX` escape from
`U+0000` to `U+00FF` is its Latin-1 byte. A character above `U+00FF` has no single-byte encoding and is
**E1004**. `\{` and `\}` are not valid in a byte string.

### Array and Dictionary Literals

```maxon
let primes = [2, 3, 5, 7]              // an Array
let ages = ["ada": 36, "alan": 41]     // a Map
```

The element type comes from the first element (or pair). See
[Initializing From Literals](#initializing-from-literals) for literals of
other collection types.

---

## Types

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

### Primitives Go Through a Typealias

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
[Ranged Type Aliases](#ranged-type-aliases) covers declaring your own.

### Integers

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
  [Division by Zero](#division-by-zero).
- **Overflow wraps.** Integer arithmetic is two's-complement: `i64.max + 1` is `i64.min`. A range is not
  enforced on every intermediate result; it is checked where a value reaches a place declared with the
  alias (see [Range Checks](#range-checks)).
- **Bitwise** operations use the word operators `and`, `or`, `xor`, `not`, `shl` and `shr` (see
  [Expressions](#logical-and-bitwise-operators)).
- An alias whose lower bound is `0` is **unsigned**: `shr` zero-fills it rather than extending the sign,
  and its range check refuses a negative value instead of letting an underflow become a huge number.
  Because a check sees only the 64 bits, a quantity cannot carry a value above `i64.max` through a
  checked door — `int(0 to u64.max)` panics on one. A value that genuinely needs all 64 bits is a bit
  pattern: use [`bits(64)`](#bit-patterns--bitsn).

### Floats

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

### Booleans

`bool` has the literals `true` and `false`. Conditions must be `bool` — there is no truthiness, so
`if count 'x'` is **E3005** (`'if' requires a bool condition, got 'int'`). `bool` and `int` do not mix:
`flag + 1` is **E2004** and comparing a `bool` with an `int` is **E3005**. On `bool` operands, `and` and
`or` are logical and short-circuit.

### Bit Patterns — `bits(n)`

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

### Characters and Strings

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
- The [standard library](STDLIB_REFERENCE.md) documents the `String` and `Character` methods.

### Storage

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
[Expressions](#sizeof-and-countof)).

A `type` whose fields are all `let` and all of packable types, summing to 64 bits or fewer, is itself one
8-byte word rather than a heap record: `sizeof` is 8, and `Array with T` is a dense 8-byte element. See
[Inline Packed Records](#inline-packed-records).

### Primitive Conformances

The primitives implement the standard interfaces directly: `int` and `float` are `Hashable`, `Equatable`,
`Comparable`, `Stringable` and `Cloneable`; `bool` is `Comparable`, `Stringable` and `Cloneable`;
`Character` is `Hashable`, `Equatable`, `Comparable`, `Stringable` and `Cloneable`; `String` is
`Hashable`, `Equatable`, `Cloneable` and `Iterable`. Every integer alias therefore works as a `Map` key or `Set` element. You can add methods to
a primitive with an [extension](#extensions-over-primitives).

### Type Conversions

#### Explicit Conversions (`as`)

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

#### Floats to Integers

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

#### Implicit Conversions

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
  [Implicit Coercion to the Backing Primitive](#implicit-coercion-to-the-backing-primitive).
- **`return`** converts to the declared return alias, exactly as the written cast would (see
  [Ranged Type Aliases](#aliases-are-distinct-types)).

Every other change of type is written with `as`.

---

## Ranged Type Aliases

A `typealias` gives a type a name. Over `int` and `float` it also gives the type a **range**, which moves a
domain rule — a port is 0 to 65535, a percentage is 0 to 100 — into the type system, where the compiler
checks it.

### Declaration

```maxon
typealias Port = int(0 to 65535)
typealias Percentage = float(0.0 to 100.0)
typealias Temperature = int(-273 to 1000)
typealias Score = int(0 upto 100)        // 0 to 99
```

`to` makes the upper bound inclusive and `upto` makes it exclusive.

**Type-qualified bounds.** `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32` and `f64` each
have `.min` and `.max`:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
typealias Offset = int(0 to i64.max)
```

- When both bounds are type-qualified they name the same type: `i8.min to i32.max` is **E3005**
  (`Mismatched type bounds`). A qualified bound may pair with a literal (`0 to u32.max`).
- A range cannot reach both below zero and above `i64.max`: `int(-1 to u64.max)` is refused. Use
  `i64.min to i64.max` or `0 to u64.max`.
- `typealias X = i64` is **E2003**; the sized names exist only as bounds.
- A typealias nothing uses is **E3062**.

`.min` and `.max` are also integer expressions anywhere a literal is valid: `let limit = u16.max`.

**Primitives in `with` clauses.** A generic type argument must be an alias, never a bare `int` or
`float` (**E2061**):

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally      // not Array with int
```

### Aliases Are Distinct Types

**Every typealias is its own type**, even when two aliases spell the same range. A value of one alias
never flows into a place declared with another — a parameter, an assignment, an `otherwise` fallback, a
`match` arm, a struct field, a union payload, a generic argument — unless you write the cast:

```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function main() returns ExitCode
	let a = 30 as Age
	// takesYear(a)                   // E3005: argument type mismatch for 'y': expected 'Year', got 'Age'
	print("{takesYear(a as Year)}\n") // the cast converts
	return 0
end 'main'
```

- `as` converts in both directions. A widening cast (the source range fits the target) emits no check;
  a narrowing cast keeps a run-time check.
- Casting a value to its own alias is **E3010** (`unneeded cast: 'Age' already fits in 'Age'`).
- A value with **no** alias fits any alias of its kind: a literal, a counted-loop counter, a `var`
  initialized from a literal, the raw value of a payload-free enum case. A named value also fits an
  unnamed slot. Only two *different* names conflict.
- The same alias name declared over the same range in two files is one type.

**`return` converts.** `return x` in a function declared `returns T` behaves as `return x as T` — the one
implicit conversion between aliases. A widening return emits no check, a narrowing one keeps its check,
and `main` may return any integer alias without spelling `ExitCode`. A different struct, a union where a
scalar is declared, or a lossy float where an integer is declared is still refused.

### Construction

A literal needs no cast when it flows into a place already declared with an alias — the literal is
checked against that alias directly:

```maxon
typealias Port = int(0 to 65535)

function open(p Port) returns Port
	return p
end 'open'

function main() returns ExitCode
	print("{open(8080)}\n")
	// open(70000)          // E3005: Value 70000 is outside the range of 'Port' (int(0 to 65535))
	return 0
end 'main'
```

Write `value as Alias` when the alias should be visible at the use site, or to convert a value of another
alias.

### Arithmetic

Arithmetic keeps the alias of its operands. Two operands of one alias give that alias; an unnamed operand
(a literal, a loop counter) adopts the named one; two different aliases are **E3005** until one side is
cast. The same rule governs comparisons. A shift takes the alias of its left operand.
Negating a signed alias keeps the alias; negating an unsigned one gives an unnamed value.

```maxon
typealias Score = int(0 to 100)
typealias Meters = int(0 to 1000)

function main() returns ExitCode
	let a = 30 as Score
	let b = 12 as Score
	let m = 5 as Meters
	let sum = a + b              // Score
	let bumped = a + 1           // Score: the literal adopts the alias
	let mixed = a + (m as Score) // cast one side
	// let bad = a + m           // E3005: 'Score' and 'Meters' are different typealiases
	print("{sum} {bumped} {mixed}\n")
	return 0
end 'main'
```

The alias is a name, not a proof: `a + b` over `Score` is a `Score` that may hold 130. Range checks apply
where the value lands (next section). All integer arithmetic is 64-bit and wraps on overflow.

### Range Checks

A value is checked where it reaches a place **declared** with the alias: a call argument, a `return`, a
struct-literal field, a field store, a field's declared default, an array element, or an explicit `as`.

- A value the compiler can compute — a literal, a constant expression — that is out of range is a
  compile error, **E3005** (`Value 101 is outside the range of 'Percent' (int(0 to 100))`).
- Any other value gets a run-time check where needed. A check is omitted when the value's own range
  provably fits.
- A failed run-time check is a **panic**, not a recoverable error: the program prints
  `panic at <file>:<line>: Range check failed: value outside typealias '<Name>'` and a stack trace, and exits
  with code 1. No `try` is involved.

**Reassigning a local is not a checked place**, so a local may hold an out-of-range value until it escapes:

```maxon
typealias Score = int(0 to 100)

function bump(start Score) returns Score
	var s = start
	s = s + 200       // no check: a local rebind
	print("{s}\n")    // prints 210 for bump(10)
	return s          // panics: Range check failed: value outside typealias 'Score'
end 'bump'
```

**`ExitCode`** is the stdlib alias `main` returns. Its range follows the target: `int(0 to u32.max)` on
Windows and `int(0 to 255)` on Linux, macOS and WASI. A literal outside it is a compile error, and a
computed value outside it panics at the `return`.

Integers are plain values: `var pos = start` copies, and advancing `pos` never changes `start`, so a ranged
alias makes a safe loop cursor:

```maxon
typealias Pos = int(0 to i64.max)

function skipSpaces(src ByteArray, startPos Pos) returns Pos
	var pos = startPos
	while pos < src.count() 'scan'
		let b = try src.get(pos) otherwise panic("in range")
		if b != 32 'notSpace'
			break
		end 'notSpace'

		pos = pos + 1
	end 'scan'

	return pos
end 'skipSpaces'
```

### Naming Aliases

Name an alias for its **purpose** — `Tally`, `BytePos`, `Coord`, `Milliseconds` — rather than reaching for
a generic `Count` or `Index`, and declare it in the module it belongs to. The standard library follows the
same pattern and exports a small set of cross-cutting aliases:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | target-dependent (see above) | process exit codes |
| `Byte` | `int(0 to u8.max)` | one byte; `ByteArray` is `Array with Byte` |
| `HashValue` | `int(0 to u32.max)` | `Hashable.hash()` results |
| `Codepoint` | `int(0 to 1114111)` | Unicode scalar values |
| `SourceLineNumber` | `int(1 to i32.max)` | caller line numbers (`__line__`) |
| `Real` | `float(f64.min to f64.max)` | general floating-point values |

Because every alias is its own type, a quantity crossing from one module's alias to another's is cast at
the crossing.

### Generic-Instance and Function-Type Aliases Are Brands

An alias over a generic instance or a function type follows the same rule. `typealias Xs = Array with
Integer` and `typealias Ys = Array with Integer` are one instance (one layout, one method set) under two
**brands**: an `Xs` does not flow into a `Ys` slot unless you write `xs as Ys`, which re-brands the value at
no cost, and `return` re-brands implicitly.

- A `[...]` literal carries no brand and fits either.
- A closure literal or a declared function carries no brand and fits any function alias of its shape.
- A function alias declared **inside a type or extension body** carries no brand either, in both
  directions: no source outside the type can write `Array.SortComparator`, so it is never the name an
  author chose to distinguish two shapes. A value of any other alias of that shape flows into it, and a
  value of it flows into any other alias of that shape.
- Two **file-scope** function aliases of one shape still refuse each other — including two
  directory-qualified ones, `api.Score` and `legacy.Score`, which are file-scope declarations wearing
  their directory and can be written in a type position.
- A non-exported function alias is **private to its file**, so two files declaring one name over
  **different** shapes have two brands and two types: each file's declarations mean its own, and a value
  of one does not fit a door declared with the other. What a slot's spelling *means* in the declaring
  file decides — two files that write the identical `function(Tally) returns Tally` over two different
  `Tally` ranges still have two shapes. Two files that **agree** about the shape share one brand, exactly
  as two files declaring one ranged alias over one range share one type.
- When both declarations are `export`ed, a bare reference from a third file is ambiguous rather than
  split: **E3063**, resolved by qualifying with the directory namespace. This holds whether or not the
  two shapes agree.
- Where a refusal would print the same bare name on both sides, the message adds a parenthesised note —
  the shape where the two shapes differ, otherwise the declaring files. The alias itself is always quoted
  by the name its author wrote.
- Casting to a **different** instance (`Array with Byte` to `Array with Integer`) is **E3131**: the
  elements have different layouts, so build a new container instead.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function runHandler(f Handler) returns Integer
	return f(10)
end 'runHandler'

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function main() returns ExitCode
	let addThree = function(n Integer) gives n + 3
	print("{runHandler(addThree)} {runCallback(addThree)}\n")   // 13 23
	return 0
end 'main'
```

### Per-Instance Typealiases

A ranged alias declared inside a generic type is a distinct type for each instantiation — even for two
aliases of the same instance:

```maxon
typealias Integer = int(i64.min to i64.max)

type Pool uses T
	export typealias Idx = int(0 to u64.max)
	var items as T

	static function create(item T) returns Self
		return Self{items: item}
	end 'create'

	export function checked(at Idx) returns Idx
		return at
	end 'checked'
end 'Pool'

typealias PoolA = Pool with Integer
typealias PoolB = Pool with Integer
```

`PoolA.Idx` and `PoolB.Idx` are different types; passing one where the other is expected is **E3005**
(`expected 'PoolB.Idx', got 'PoolA.Idx'`). Convert with `a as PoolB.Idx`. Literals that fit the range are
accepted by both.

A per-instance **function** alias is not a brand, because no source outside the type can write its name.
`Array.sort` takes an `Array.SortComparator`, so a field declared with your own `typealias RowComparator =
function(Row, Row) returns Ordering` passes straight through: `rows.sort(self.compare)` compiles with no
cast and no wrapping closure.

---

## Composite Types

A `type` declares a record with named fields and methods. A value of a type is a reference to a heap
record (see [Memory Model](#memory-model)), unless the type's whole shape fits one machine word — see
[Inline Packed Records](#inline-packed-records).

### Declaration

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(10, y: 20)
	print("{p.x}, {p.y}\n")
	return 0
end 'main'
```

- `var` fields can be written after construction; `let` fields cannot.
- A field's type is written after `as` and is an alias, `bool`, a type, or another named type — never a bare
  `int` or `float`.
- **Fields are private to the type** unless marked `export`, `module` or `public`. The boundary is the type,
  not the file: another type in the same file reading a private field is **E3014** (`cannot access
  unexported field`). A type's own methods may read the private fields of any instance of that type.
- A type cannot contain itself, directly or through other types: `var next as Node` inside `Node` is
  **E4014** (`recursive type references are not allowed`). Use a container such as `Array with Node` or an
  index.

### Construction

A type literal `Point{x: 1, y: 2}` — or `Self{...}` — is legal only inside the type's own body. Code elsewhere
constructs through a `static` factory, which is where invariants are established. A literal outside the
type is **E3076** (`type 'Point' can only be constructed from within its own methods; use a static factory
method instead`).

### Required Field Initialization

Every field must have a value when a record is constructed. A field is initialized when:

1. **The declaration supplies a default.** `var count = 0` infers the type from an integer, float or
   `true`/`false` literal. `var items as IntArray = IntArray.create()` gives the type explicitly and may use
   any expression — an enum case, a factory call — evaluated each time a literal omits the field.
2. **The literal supplies it**: `Counter{value: 5}`. A supplied value always wins over the default.
3. **A static factory assigns it** with `self.field = expr` on every path before `return Self{}` (or the
   type's own literal).

A literal that leaves a field uninitialized is **E3086**, listing the fields.

```maxon
typealias Tally = int(0 to u64.max)

type Counter
	export var value as Tally
	export var version = 0

	export static function create(initial Tally) returns Self
		self.value = initial     // rule 3
		return Self{}            // version comes from its default
	end 'create'
end 'Counter'
```

Rule 3 requires **definite assignment**: a write on only one branch of an `if`, or only inside a loop body,
does not count.

### Methods

Methods are declared inside the type body. Inside a method, fields and sibling methods are reachable by
bare name; `self` names the receiver explicitly.

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'

	export function add(other Point) returns Point
		return Point.create(x + other.x, y: y + other.y)
	end 'add'

	export function manhattan() returns Coord
		return magnitudeOf(x) + magnitudeOf(y)    // sibling call: self.magnitudeOf(x)
	end 'manhattan'

	function magnitudeOf(v Coord) returns Coord
		return v if v >= 0 else -v
	end 'magnitudeOf'
end 'Point'

function main() returns ExitCode
	let p = Point.create(3, y: -4).add(Point.create(1, y: 1))
	print("{p.manhattan()}\n")     // 7
	return 0
end 'main'
```

- A method is private to its file unless marked `export`, `module` or `public`; its visibility does not
  inherit from the type's. Calling a non-exported method from another file is **E3008**.
- A local declaration inside a method (a `let`/`var`, parameter, pattern binding or loop variable) may not
  reuse a field's name (**E3006**).
- `self` cannot be bound by any declaration (**E2051** / **E2010**).

### Static Methods

A `static function` belongs to the type rather than an instance. It has no `self` and is called as
`Type.method()`. Factories are static methods.

| | Instance method | Static method |
|---|---|---|
| Declaration | `function name()` | `static function name()` |
| Receiver | `self` (implicit) | none |
| Call | `value.name()` | `Type.name()` |

A type may declare a static and an instance method with the same name; `Type.name()` calls the static one
and `value.name()` the instance one.

### Static Fields

`static var` and `static let` declare one value per type, read and written as `Type.name`:

```maxon
typealias Byte = int(0 to u8.max)

type Limits
	static let MAX_COUNT = 1000
	static let SEPARATOR = 61 as Byte
	static var used = 2
end 'Limits'

function main() returns ExitCode
	Limits.used = Limits.used + 1
	print("{Limits.MAX_COUNT} {Limits.SEPARATOR} {Limits.used}\n")   // 1000 61 3
	return 0
end 'main'
```

- A static field needs an initializer. Assigning to a `static let` is **E2013**; naming a static the type
  does not declare is **E2073**.
- An initializer may be a literal, a factory call (`static var shared = Cache.create()`), a literal of the
  enclosing type, or an array literal. A literal of *another* type is **E3076** — call its factory.
- **Initializers run before `main`, exactly once**, whether or not the field is ever read, in dependency
  order rather than declaration order: one that reads another static, directly or through a function it
  calls, runs after it. Initializers that depend on each other in a cycle, or one that reads its own field,
  are **E2012**. Assigning to a `static var` is a plain store; the initializer does not run again.
- A `static let` whose value is decided entirely at compile time — a scalar or byte-string literal, an array
  literal of integers, an empty container, a payload-free union case, or a record whose every field is one of
  these — is **image data**: its bytes are laid down in read-only memory and nothing creates it at run time.
  Any other `static let`, and every `static var`, is built before `main`.
- A static declared in `stdlib/` is kept only when reachable code reads it; one nothing reads is not emitted,
  and neither is its initializer.
- A static field is a top-level binding whose name carries the type as a qualifier, so what its initializer
  may reach follows the [Top-Level Variables](#top-level-variables) rules: a `let`'s initializer may not reach
  a module-level `var` that holds a record (**E3165**), a `var`'s may not take a module-level `let`'s record
  (**E3166**), and a `spawn` reachable from any global initializer is **E3164**.
- A `let` without `static` in a type body is an ordinary field with a default.

### Equality and Copying

- `a == b` and `a != b` on two records call the type's own `equals(other)` method, which must return `bool`.
  Declaring `implements Equatable` is not required. A type with no `equals` cannot be compared: **E3005**
  (`cannot compare struct with struct`). Ordering (`<`) on records is refused the same way.
- `a is b` asks whether two names refer to the **same record**
  (see [Reference Identity](#reference-identity-operators)).
- A type whose fields are all cloneable gets `clone()` automatically, producing an independent deep copy
  (see [Memory Model](#explicit-cloning)).

### Inline Packed Records

A `type` whose whole shape fits one machine word is not a heap record: its value **is** the word. The
qualification is inferred — there is no syntax and no annotation for it. A type qualifies when:

- **every** field is `let`;
- **every** field's declared type is an unsigned zero-based ranged alias (`int(0 to N)`), a `bits(n)`
  alias, or `bool` — resolved as the file declaring the `type` sees the name;
- it takes no type parameters and carries no `where` clause;
- it has at least one field;
- the fields' storage widths sum to **64 bits or fewer**.

A field's storage width is the array-element ladder under [Storage](#storage): `bool` and `int(0 to 1)`
take 1 bit, `int(0 to 3)` 2, `int(0 to 15)` 4, `int(0 to u8.max)` 8, `int(0 to u16.max)` 16,
`int(0 to u32.max)` 32, anything wider 64, and `bits(n)` takes its own `n`.

The word is laid out **first field in the low bits**: the first field occupies bits `0 .. w0-1`, the
second `w0 .. w0+w1-1`, and so on. A `Self{…}` shifts each field into its place, range-checked at its
door exactly as a heap record's field is, and a field read extracts it with a shift and a mask.

```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Pair
		return Pair{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(7, hi: 9)
	print("{p.lo} {p.hi} {sizeof(Pair)}\n")    // 7 9 8
	return 0
end 'main'
```

Because the value is a word and not a box:

- `sizeof(T)` is **8**, whatever the fields sum to.
- A local, a parameter and a return are the word itself. A field of a heap record is one 8-byte slot,
  and `Array with T` is a dense 8-byte element.
- Nothing is allocated, retained, released or destroyed. A module-level `let` or `var` of one always
  holds a scalar word: an initializer the compiler folds is baked in and nothing runs before `main`,
  while one it cannot fold runs its factory before `main` and stores the word into that slot.
- `clone()` is the **identity** — the copy is the same word.
- `is` and `is not` are refused with **E3068**, the diagnostic every value gets: there is no record for
  two names to share (see [Reference Identity](#reference-identity-operators)).
- `==` is unchanged — it calls the type's own `equals`, and a type without one is **E3005**. `Hashable`
  and `Equatable` conformance is unchanged, so an inline record is a `Map` key like any other.

**An enum field is not admitted.** A type with one stays a heap record, as does every type that fails any
part of the rule above — a `var` field, a signed or non-zero-based range, a type parameter, or fields
summing past 64 bits. Nothing about those types changes.

### Interfaces

An interface lists method signatures a type promises to provide:

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	export let side as Tally

	export static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return side * side
	end 'area'
end 'Square'

function describe(item Shape) returns String
	return "area {item.area()}"
end 'describe'

function main() returns ExitCode
	print("{describe(Square.create(3))}\n")    // area 9
	return 0
end 'main'
```

- A type lists every interface it conforms to: `type Foo implements A, B`. An `enum` or a `union` declares
  conformance the same way and on the same terms — see
  [Enum Interface Conformance](#enum-interface-conformance). A missing or mis-typed method is **E3016**.
- `Self` in a requirement means the conforming type.
- A requirement may be `static function`; the conforming type provides it as a static method.
- `interface Derived extends Base` inherits `Base`'s requirements; a type implementing `Derived` provides
  both, and satisfies parameters typed `Base`.

**Interface-typed parameters, returns and fields.** An interface can be used as a type, as `describe`
does above: a parameter typed `Shape` accepts any conforming type, `enum` and `union` conformers
included, a function may return an interface,
and a field may hold one. A struct literal (`Self{shape: Square.create(3)}`) or an assignment
(`self.shape = Strip.create(5)`) stores a conforming value straight into such a field, exactly as passing it
to a `Shape` parameter would; assigning a different conformer releases the one the field held. A value that
does not conform is **E3005**, and a conformer whose associated-type binding contradicts the field's
`with` clause is **E3127**. Where the compiler can see the concrete type, calls dispatch statically;
otherwise they dispatch through a witness table at run time. A record holding such a field is cloneable
on the terms [Explicit Cloning](#explicit-cloning) states.

**The standard interfaces** (declared in the standard library):

| Interface | Requirement |
|-----------|-------------|
| `Equatable` | `function equals(other Self) returns bool` |
| `Hashable` | `function hash() returns HashValue` |
| `Comparable` | `function compare(other Self) returns Ordering` |
| `Cloneable` | `function clone() returns Self` |
| `Stringable` | `function toString() returns String` |
| `FormattedStringable` | `function toString(format String) returns String` |
| `Error` | none — a marker for thrown values |
| `Iterable uses Element, Iter` | `function createIterator() returns Iter throws IterationError` |
| `Parsable` | `static function fromString(input String) returns Self throws Error` |

A type used in string interpolation needs a `toString()` method; one used with a format specifier
(`{value:spec}`) needs `toString(format String)`.

### Hashable and Hasher

A type used as a `Map` key or `Set` element implements `Hashable` and `Equatable`:

```maxon
typealias Score = int(i64.min to i64.max)

type Cell implements Hashable, Equatable
	export let row as Score
	export let col as Score

	static function create(row Score, col Score) returns Self
		return Self{row: row, col: col}
	end 'create'

	function hash() returns HashValue
		return (row * 31 + col) and 0xFFFFFFFF
	end 'hash'

	function equals(other Self) returns bool
		return row == other.row and col == other.col
	end 'equals'
end 'Cell'

typealias CellNames = Map with (Cell, String)

function main() returns ExitCode
	var names = CellNames.create()
	names.upsert(Cell.create(1, col: 2), value: "b1")
	let found = try names.get(Cell.create(1, col: 2)) otherwise "missing"
	print("{found}\n")     // b1
	return 0
end 'main'
```

Payload-free enums are `Hashable` and `Equatable` automatically; unions are not (see
[Unions](#comparing-union-values)).

For a digest over bytes, the standard library's `Hasher` is an incremental 64-bit FNV-1a fold that
returns a `HashDigest` (`bits(64)`):

```maxon
function main() returns ExitCode
	var hasher = Hasher.create()
	hasher.combine("abc".toByteArray())
	print("{hasher.finalize():016x}\n")     // e71fa2190541574b
	return 0
end 'main'
```

The fold carries no length tag — combining `"ab"` then `"c"` equals combining `"abc"` — so combine each
part's length yourself when the boundaries matter. `Hasher.empty()` and `Hasher.combined(state, bytes:)`
are the same fold as pure functions.

### Parsable

`Parsable` is the interface for types that parse themselves from text. `int`, `float` and `bool` provide
`fromString`, which throws `ParseError.invalidFormat`:

```maxon
function main() returns ExitCode
	let n = try int.fromString("42") otherwise 0
	let f = try float.fromString("2.5") otherwise 0.0
	print("{n} {f}\n")     // 42 2.5
	return 0
end 'main'
```

A type conforms with a static `fromString` that throws an error type:

```maxon
typealias Cents = int(0 to i64.max)

enum MoneyError implements Error
	negative
end 'MoneyError'

type Money implements Parsable
	export let cents as Cents

	static function fromString(input String) returns Self throws MoneyError
		let value = try int.fromString(input) otherwise throw MoneyError.negative
		if value < 0 'negative'
			throw MoneyError.negative
		end 'negative'

		return Self{cents: value as Cents}
	end 'fromString'
end 'Money'
```

A `fromString` that does not throw, or throws a type that is not an error, is **E3016**.

### Initializing From Literals

A type can be written from a string or character literal with `Type from "…"`, which calls its
`static function init(value String) returns Self`. The type must declare the conformance:

| Interface | Literal | Requirement |
|-----------|---------|-------------|
| `InitableFromStringLiteral` | `T from "text"` | `static function init(value String) returns Self` |
| `InitableFromCharLiteral` | `T from 'c'` | `static function init(value Character) returns Self` |

```maxon
type Wrapper implements InitableFromStringLiteral
	export let value as String

	static function init(value String) returns Wrapper
		return Wrapper{value: value}
	end 'init'
end 'Wrapper'

let greeting = Wrapper from "hello"

function main() returns ExitCode
	print("{greeting.value}\n")
	return 0
end 'main'
```

`Type from "…"` is also allowed as a module-level initializer. Using it on a type without the conformance is
**E3005** (`Type 'Wrapper' does not conform to InitableFromStringLiteral`).

The standard collections accept bracketed literals: a plain `[1, 2, 3]` is an `Array`, `["a": 1]` is a
`Map`, and `Array`, `Set`, `List` and `Vector` aliases take `Alias from [ ... ]`, for example
`CharSet from ['a', 'e']`; the alias states the element type, and a `Vector` alias's literal must write
exactly as many elements as the alias holds. A type that declares `implements InitableFromArrayLiteral with
Element` takes `Type from [ ... ]`, which is `Type.init(value ElementArray)` over the literal:

```maxon
typealias Digit = int(0 to 9)
typealias DigitArray = Array with Digit
typealias Number = int(0 to i64.max)

type Digits implements InitableFromArrayLiteral with Digit
	export var value as Number

	static function init(digits DigitArray) returns Self
		var total = 0 as Number

		for d in digits 'each'
			total = total * 10 + d
		end 'each'

		return Self{value: total}
	end 'init'
end 'Digits'

function main() returns ExitCode
	let n = Digits from [4, 0, 7]
	print("{n.value}\n")
	return 0
end 'main'
```

### Generic Types

`uses` declares type parameters. A generic type is instantiated with `with`, through a typealias:

```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function get() returns T
		return item
	end 'get'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(42)
	let inferred = Box.create("hi")        // Box with String, from the argument
	print("{b.get()} {inferred.get()}\n")  // 42 hi
	return 0
end 'main'
```

- Several parameters: `type Pair uses A, B`, instantiated `Pair with (Integer, String)`.
- A static factory called on the bare generic name infers the arguments from its parameters.
- One compiled body serves every instantiation.
- Wrong argument count is **E2056**; a bare `int` argument is **E2061**; a `float` type argument is not
  yet supported (**E2062**); a type argument to a non-generic type is **E2055**.

Inside `type Outer uses T`, another generic type written without `with` arguments binds by parameter NAME:
`Inner uses T` means `Inner with T`. A base whose parameters the scope does not declare (`Box uses Element`)
binds nothing and stays the bare base, and a call on it — static (`Box.create(first)`) or through a receiver
of that type — may neither hand a slot written over one of its type parameters (`Element`, or
`Array with Element`) a value typed at `Outer`'s parameters nor call a method that needs a layout descriptor
(**E3162**); an overloaded callee is judged by the member its arguments pick. Name the instance with a
`typealias` instead: `typealias Inner = Box with T`. Outside a generic type body, a layout-needing call on a
bare generic base is **E3162** as well unless the calling function carries a layout descriptor of its own — a
static whose arguments do not fix every one of the base's parameters (`Holder.create()` in `main`), or a
method called through a receiver of the bare base type.

### Where Clauses

`where` constrains a type parameter to conform to interfaces, which lets the body call their methods:

```maxon
typealias Code = int(i64.min to i64.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Tagged uses T where T is Digest
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function itemDigest() returns Code
		return item.digest()
	end 'itemDigest'
end 'Tagged'
```

- Several interfaces on one parameter: `where T is Hashable and Equatable`.
- Several parameters: `where K is Hashable, V is Cloneable`.
- An instantiation whose argument does not conform is **E3017** (`Type 'X' does not satisfy constraint
  'Digest' required by type parameter 'T' of 'Tagged'`).
- A constraint can bind an associated type: `where S is Cursor with E`.

### Associated Types

An interface can declare associated types with `uses`; a conforming type binds them with `with`:

```maxon
typealias Slot = int(0 to u64.max)
typealias Score = int(i64.min to i64.max)

interface Container uses Element
	function at(index Slot) returns Element
end 'Container'

type Scores implements Container with Score
	export var base as Score

	export static function create(base Score) returns Self
		return Self{base: base}
	end 'create'

	function at(index Slot) returns Score
		return base + (index as Score)
	end 'at'
end 'Scores'
```

Several associated types bind positionally: `implements Iterable with (Entry, MapIterator)`. A missing
binding or a method whose types disagree with the binding is **E3016**.

### Parameterized Existentials

An alias over an interface with its associated types bound — `typealias IntegerSeq = Seq with Integer` —
is a value type that holds **any** conforming value and dispatches at run time:

```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Seq'

type Upto implements Seq with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'

		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

typealias IntegerSeq = Seq with Integer

function total(source IntegerSeq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'

	return sum
end 'total'

function main() returns ExitCode
	print("{total(Upto.create(4))}\n")     // 10
	return 0
end 'main'
```

The `with` arguments are a claim checked against every conformer that reaches the existential; a conformer
binding the associated type differently is **E3125**.

### The `Self` Type

Inside a type, interface or extension body, `Self` names the enclosing type. It is legal in signatures
(`returns Self`), in literals (`Self{...}`), and as the base of a member expression: `Self.create(…)`,
`Self.someCase`, `Self.MAX` mean exactly what the type's own name would. `Self` outside a type declaration
is **E2015**.

### Interface Extensions

`extension InterfaceName` adds methods to **every** type that conforms to the interface, with one
implementation:

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

extension Shape
	function doubledArea() returns Tally
		return self.area() * 2
	end 'doubledArea'
end 'Shape'
```

- `self` is the conforming value; the extension may call any requirement of the interface.
- Associated types resolve to each conformer's binding.
- Extensions of a parent interface apply to types conforming to a derived one.

### Conditional Extensions

A `where` clause restricts an extension to conformers whose associated types satisfy it. With the `Seq`
interface above:

```text
extension Seq where Element is Equatable
	function has(target Element) returns bool
		for item in self 'loop'
			if item == target 'found'
				return true
			end 'found'
		end 'loop'

		return false
	end 'has'
end 'Seq'
```

`Upto.create(4).has(3)` is available because `Integer` is `Equatable`. Several constraints on one
parameter join with `and`: `where Key is Hashable and Equatable`.

Conformers that do not satisfy the clause simply do not get the method; calling it on one is **E4006**,
which names the unmet constraint.

### Conditional Interface Conformance

An extension can also add a conformance under a condition:

```text
extension Array implements Hashable, Equatable where Element is Hashable and Equatable
	function hash() returns HashValue
		// ...
	end 'hash'

	function equals(other Self) returns bool
		// ...
	end 'equals'
end 'Array'
```

The constraint is checked wherever an instantiation is created: an explicit typealias
(`typealias IntArr = Array with Integer`), a bracketed literal (`[1, 2]`, `["a": 1]`), or a static factory
call on the bare generic name (`Box.create("hi")`). If `Integer` is `Hashable` and `Equatable`, `Array with
Integer` is too and can be a `Map` key; if not, an instantiation used where the conformance is required is
**E3017**.

### Extensions Over Primitives

`extension int`, `extension float` and `extension bool` add methods to a primitive, and may declare
conformances. Inside, `self` is the value and `Self` the primitive:

```maxon
typealias Integer = int(i64.min to i64.max)

extension int
	export function doubled() returns Integer
		return self * 2
	end 'doubled'
end 'int'

function main() returns ExitCode
	let n = 21
	print("{n.doubled()}\n")     // 42
	return 0
end 'main'
```

A `hash`, `equals`, `compare`, `toString` or `clone` you declare in such an extension replaces the built-in
one. An extension body cannot declare stored `var`/`let` members (**E2015**).

---

## Tuples

A tuple is a fixed-size, ordered group of values that may have different types. Tuples are written with
parentheses, both as values and as types.

```maxon
typealias Amount = int(i64.min to i64.max)

function sum(t (Amount, Amount)) returns Amount
	return t.0 + t.1
end 'sum'

function makePair(a Amount, b Amount) returns (Amount, Amount)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var t = (0, 0)
	t.0 = 20
	t.1 = 22
	print("{sum(t)}\n")                      // 42

	let mixed = (42, "hello")
	print("{mixed.0} {mixed.1}\n")           // 42 hello

	let (x, y) = makePair(10, b: 32)
	print("{x} {y}\n")                       // 10 32
	return 0
end 'main'
```

### Literals and Element Access

- `(a, b)` builds a tuple; a tuple has at least two elements. `(expr)` is just a parenthesized expression.
- Elements are read and written by position: `t.0`, `t.1`, `t.2`.
- A tuple type in a signature is a parenthesized type list: `(Amount, String)`. It can also be named:
  `typealias Span = (Amount, Amount)`.

### Destructuring Declarations

`let (a, b) = expr` and `var (a, b) = expr` bind each element to a new name. `_` discards an element:

```maxon
let (first, _) = makePair(1, b: 2)
```

### Tuple Assignment

A tuple pattern on the left of `=` assigns to existing `var`s, and may declare new names in the same pattern:

```maxon
var p = 0
var q = 0
(p, q) = makePair(1, b: 2)       // both existing
(p, let r) = makePair(3, b: 4)   // p existing, r newly declared
(q, _) = makePair(5, b: 6)       // discard the second element
```

- A name without `var`/`let` must already be a `var`; a `let` target is **E2013**.
- The number of names must match the element count (**E3005**).
- Discarding every element of a pure function's result is **E3064**.

### Destructuring in For Loops

A `for` loop over tuples can destructure each one — a `Map` yields `(key, value)` pairs:

```maxon
let ages = ["ada": 36, "alan": 41]
var total = 0
for (_, age) in ages 'loop'
	total = total + age
end 'loop'
```

### Memory Semantics

A tuple is a reference-counted record, like a `type`. Assigning a tuple shares it; a tuple holding managed
values (strings, records) releases them when the last reference goes away. A returned pair is the one
exception: a two-element tuple of register-wide elements (a whole-word integer or a `bool`), returned by a
function that does not throw and whose address is not taken, comes back in the two return registers and
allocates nothing.

---

## Enums

An `enum` declares a fixed set of named cases. Enum cases carry no associated data — a type whose cases
carry data is a [union](#unions). A case may have a **raw value** (see
[Raw-Value Enums](#raw-value-enums)).

### Simple Enums

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let dir = Direction.north
	if dir == Direction.north 'up'
		print("{dir.name}\n")      // north
	end 'up'

	return 0
end 'main'
```

A case is written `Type.case`. Enum values compare with `==` and `!=`, and every payload-free enum is
automatically `Equatable` and `Hashable`, so it can be a `Map` key or `Set` element. Inside `match` arms,
cases are written bare (see [Match Statement](#match-statement)).

### Enum Methods

An enum can declare instance methods after its cases. Inside one, `self` is the case value; inspect it with
`match self`. `Self.case` names a case of the enclosing enum.

```maxon
enum Direction
	north
	south

	export function opposite() returns Direction
		return match self 'flip'
			north gives Self.south
			south gives Self.north
		end 'flip'
	end 'opposite'
end 'Direction'

function main() returns ExitCode
	print("{Direction.north.opposite().name}\n")    // south
	return 0
end 'main'
```

- A method carries its own visibility — `export`, `module` or `public` — and is file-private without one,
  whatever the enum's own visibility. Calling a private method from another file is **E3008**.
- An enum declares no fields, so `self.something` inside a method is **E2015**; use `match self`.
- `static function` is not supported on an enum (**E2015**).

### Enum Properties

Every enum case has:

| Property | Result |
|----------|--------|
| `.name` | the case name as a `String` |
| `.ordinal` | the zero-based declaration position |
| `.rawValue` | the case's raw value (the ordinal when none is declared) |

and every enum type has:

| Member | Result |
|--------|--------|
| `Type.allCases` | an `Array` of every case, in declaration order |
| `Type.allCaseNames` | an `Array with String` of every case name |
| `Type.fromName(name)` | the case with that name; throws `noSuchCaseName` when none matches |
| `Type.fromRawValue(raw)` | the case with that raw value; throws `noSuchRawValue` when none matches |

```maxon
enum Color
	red
	green
	blue
end 'Color'

function lookup(name String) returns Color
	return try Color.fromName(name) otherwise Color.red
end 'lookup'

function main() returns ExitCode
	for color in Color.allCases 'each'
		print("{color.name}={color.ordinal} ")      // red=0 green=1 blue=2
	end 'each'

	print("\n{lookup("blue").name} {lookup("purple").name}\n")   // blue red
	return 0
end 'main'
```

A literal name or raw value that matches no case is caught at compile time (**E3034**). Both lookups throw,
so they need `try`.

### Struct-Backed Enums

A case's raw value can be a record of compile-time constants, which attaches metadata to each case. Read
it through `.rawValue`:

```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
end 'OpMeta'

enum Instruction
	add = OpMeta{latency: 1, isMemory: false}
	load = OpMeta{latency: 4, isMemory: true}
	store = OpMeta{latency: 3, isMemory: true}
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.load
	print("{op.rawValue.latency} {op.rawValue.isMemory}\n")    // 4 true
	return 0
end 'main'
```

- Every case uses the same struct type and provides a value.
- Field values are compile-time constants: numbers, booleans, enum cases, top-level constants.
- The enum is stored as its ordinal; `.rawValue` builds the record on demand. `fromRawValue` is not
  available for a struct-backed enum.

### Enum Interface Conformance

An enum or a union can declare conformances after its name, and the clause means exactly what a `type`'s
means — most often `Error`, which has no requirements:

```maxon
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'

enum HttpError implements Error
	badRequest = 400
	notFound = 404
end 'HttpError'
```

The header is only `enum Name` and an optional `implements` clause; the backing type is inferred from the
raw values, never written. Anything else on the header line — `enum Colour int` — is **E2001**
(`unexpected token: 'int'`), and the same holds for a `union` header.

**An enum satisfies requirements with its own methods, and its values widen.** Any interface may be named,
not only `Error`: the enum's [methods](#enum-methods) are checked against the interface's requirements the
same way a type's are, and a value of the enum is then accepted wherever that interface is the declared
type — an interface-typed parameter, an interface-typed return, or an interface-typed field. Calls on it
dispatch through the witness table, as they do for any other conformer:

```maxon
interface Greeter
	function greet() returns Integer
end 'Greeter'

enum Step implements Greeter
	one
	two

	function greet() returns Integer
		return match self 'which'
			one gives 41
			two gives 7
		end 'which'
	end 'greet'
end 'Step'

function callGreet(g Greeter) returns Integer
	return g.greet()
end 'callGreet'

function main() returns ExitCode
	return callGreet(Step.one)    // 41
end 'main'
```

A union conformer carrying a payload widens the same way; the widened value releases its box when it drops.

A requirement no method of the enum matches is **E3016**, and a clause naming an interface that does not
exist is **E3015** — both reported at the enum's name, exactly as for a `type`.

A payload-free enum may also name [`Hashable`](#hashable-and-hasher) or `Equatable` explicitly. The
compiler grants both to every payload-free enum already, and the granted implementation satisfies the
declared requirement; writing the clause is a statement of intent, not a demand for a hand-written
`hash()`.

---

## Raw-Value Enums

A raw value gives each case a constant: an integer, a float, a string, a character, a record, or a
function.

### Declaration

```maxon
enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'
```

Cases without a value count up from 0, or from the previous explicit integer value plus one. Negative values
are allowed:

```maxon
enum Priority
	low          // 0
	medium       // 1
	high = 10
	critical     // 11
end 'Priority'

enum Temperature
	cold = -10
	freezing = 0
	warm = 25
end 'Temperature'
```

Counting up applies only to integer raw values; a case without a value beside non-integer values is an
error.

### Backing Types

The raw values decide the backing type:

```maxon
enum Threshold
	low = 0.1
	high = 0.9
end 'Threshold'

enum ContentType
	json = "application/json"
	html = "text/html"
end 'ContentType'

enum Escape
	newline = '\n'
	tab = '\t'
end 'Escape'
```

`.rawValue` has the backing type — `ContentType.json.rawValue` is the `String` `"application/json"`.
[Struct-backed enums](#struct-backed-enums) attach a record per case.

**Function backing** attaches a function to each case; all cases share one signature, and `.rawValue` is
the function value:

```maxon
typealias Operand = int(i64.min to i64.max)

function doubleFn(x Operand) returns Operand
	return x * 2
end 'doubleFn'

function tripleFn(x Operand) returns Operand
	return x * 3
end 'tripleFn'

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function main() returns ExitCode
	let f = Op.tripleOp.rawValue
	print("{f(14)}\n")     // 42
	return 0
end 'main'
```

`fromRawValue` is available for integer, float, string and character backings, not for struct or function
backings.

### Comparison

Cases compare with `==` and `!=`. A string-backed case also compares directly with a `String`, and a
character-backed case with a `Character`, by its raw value:

```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
end 'ContentType'

function isJson(c ContentType) returns bool
	return c == "application/json"
end 'isJson'
```

Comparing `.rawValue` itself with `==` is **E3097** — compare the case (`value == Type.case`) instead.

### Match

A match on an enum names every case, bare (**E2026** lists any that are missing). `Type.case` in an arm is
**E3075**, a plain `default` arm is **E2046**, and covering a case twice is **E2027**. An arm covering
several cases lists them with `or`, one per line:

```maxon
enum Priority
	low
	medium
	high = 10
	critical
end 'Priority'

function urgency(p Priority) returns String
	return match p 'check'
		low or
			medium gives "not urgent"
		high or
			critical gives "urgent"
	end 'check'
end 'urgency'
```

### Implicit Coercion to the Backing Primitive

A case of a simple, integer-backed or float-backed enum is used as its raw value wherever a number is
expected — a function argument, a collection element, a comparison, a `return`, a struct field — with no
`.rawValue`:

```maxon
enum JsonByte
	lBracket = 0x5B
	space = 0x20
end 'JsonByte'

function main() returns ExitCode
	var out = ByteArray.create()
	out.push(JsonByte.lBracket)                     // pushes 0x5B
	out.push(JsonByte.space)
	let first = try out.get(0) otherwise 0
	print("{first == JsonByte.lBracket}\n")         // true
	return 0
end 'main'
```

String-, character-, struct- and function-backed enums do not coerce; use `.rawValue`.

### Keywords as Case Names

A keyword can be a case name, since cases are always written qualified (`TokenKind.end`) or bare inside a
match arm:

```maxon
enum TokenKind
	function
	return
	end
	if
end 'TokenKind'
```

### Error Conditions

| Code | Cause |
|------|-------|
| E3030 | duplicate case name |
| E3031 | duplicate raw value |
| E3032 | raw values of different backing types in one enum |
| E3034 | an unknown case (`Color.purple`), or a literal `fromName`/`fromRawValue` argument that matches no case |
| E3097 | comparing `.rawValue` with `==` |

---

## Unions

A `union` declares a fixed set of cases, each of which may carry **associated values**. A value of a union
is exactly one case with its payload, and `match` is how you read it.

```maxon
typealias Amount = int(i64.min to i64.max)

union Outcome
	success(value Amount)
	failure(code Amount, message String)
	pending
end 'Outcome'

function describe(r Outcome) returns String
	return match r 'show'
		success(v) gives "ok {v}"
		failure(code, message) gives "failed {code}: {message}"
		pending gives "waiting"
	end 'show'
end 'describe'

function main() returns ExitCode
	print("{describe(Outcome.success(42))}\n")
	print("{describe(Outcome.failure(404, message: "not found"))}\n")
	print("{describe(Outcome.pending)}\n")
	return 0
end 'main'
```

### Constructing Cases

A case with a payload is constructed like a call — first argument positional, the rest named after the
payload fields: `Outcome.failure(404, message: "not found")`. A case without a payload is written like an
enum case: `Outcome.pending`.

Payloads may be integers, booleans, strings, records, other unions and collections. A `float` payload is
not supported yet (**E2015**).

### Pattern Matching

In a `match`, `caseName(a, b)` binds the payload for that arm; the bindings are local to the arm.

- The number of bindings matches the case's payload fields.
- Discard one binding with `_`: `failure(_, message)`.
- To ignore the whole payload, omit the parentheses: `success then …`. Writing `success(_)` with every
  binding discarded is **E3081**.
- An `or`-chain can list payload cases bare; their payloads are not accessible in that arm.
- A match on a union must cover every case (**E2026**); use `default throws` or `default panic(…)` for a
  deliberate catch-all (see [Statements](#default-throws-and-default-panic)).

### Mutable Match Bindings

When the matched value is a `var` (or a union parameter the function may write), assigning to a binding
writes back into the union:

```maxon
typealias Amount = int(i64.min to i64.max)

union Box
	empty
	full(value Amount)
end 'Box'

function main() returns ExitCode
	var b = Box.full(10)
	match b 'update'
		full(value) then value = 42
		empty then return 1
	end 'update'

	match b 'read'
		full(value) then print("{value}\n")    // 42
		empty then print("empty\n")
	end 'read'

	return 0
end 'main'
```

When the matched value is a `let`, the bindings are immutable and assigning to one is **E2013**.

### Comparing Union Values

Unions have no `==` or `!=` (**E3066** `cannot compare union values using '==', use 'match' instead`).
Inspecting a union through `match` means that adding a case later flags every place that must decide what
to do with it. For the same reason unions are not automatically `Equatable` or `Hashable`; a union may
still declare `implements Equatable` with its own `equals` method and call it explicitly.

### Union Properties

A union value has `.name`, `.ordinal` and `.rawValue`, and a union type has `Type.allCaseNames` and
`Type.fromName(name)`. `fromName` takes the payload as extra arguments when the name is a literal
(`Container.fromName("value", 42)`); with a run-time string it only produces cases without a payload.
There is no `.allCases`, because a payload case has no single value — use `.unionCases` below.

A case can declare an explicit integer tag, which becomes its `.rawValue` (the `.ordinal` is still the
declaration position):

```maxon
typealias Id = int(0 to 255)

union Instr
	add(dest Id, src Id) = 5
	neg(dest Id) = 9
end 'Instr'
```

### Union Methods

A union can declare instance methods; inspect `self` with `match`:

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	point

	export function area() returns Amount
		return match self 'calc'
			circle(r) gives 3 * r * r
			square(s) gives s * s
			point gives 0
		end 'calc'
	end 'area'
end 'Shape'

function main() returns ExitCode
	print("{Shape.square(4).area()}\n")    // 16
	return 0
end 'main'
```

As with enums, a method carries its own visibility, and `static function` is not supported.

### Union Interface Conformance

```maxon
typealias HttpCode = int(100 to 599)

union FetchError implements Error
	notFound
	status(code HttpCode)
end 'FetchError'
```

Unions and enums are the types that can be thrown (see
[Error Handling](#defining-error-types)).

### Struct-Backed Unions

Each case can also carry a compile-time record, exactly like a
[struct-backed enum](#struct-backed-enums). The payload and the record are
independent: `match` reads the payload and `.rawValue` reads the record.

```maxon
typealias Latency = int(0 to 50)
typealias Slot = int(0 to 255)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
end 'OpMeta'

union MachineOp
	movImm(dest Slot, value Slot) = OpMeta{latency: 1, isMemory: false}
	load(dest Slot, addr Slot) = OpMeta{latency: 4, isMemory: true}
	store(addr Slot, src Slot) = OpMeta{latency: 3, isMemory: true}
end 'MachineOp'

function main() returns ExitCode
	let op = MachineOp.load(1, addr: 2)
	print("{op.rawValue.latency} {op.rawValue.isMemory}\n")    // 4 true
	return 0
end 'main'
```

Every case uses the same record type and provides a value of compile-time constants.

### Union Cases (Discriminant as an Enum)

Every union `U` has a companion enum `U.unionCases` with one bare case per union case, in declaration
order. It is an ordinary enum — `.allCases`, `.allCaseNames`, `.fromRawValue`, `.fromName`, `.name`,
`.ordinal`, `.rawValue` — and a `match` over it is exhaustiveness-checked.

That makes serialization safe to extend: write a case's `rawValue` next to its payload, and on reading, lift
the stored tag back with `fromRawValue` and `match` on it. Adding a case to `U` adds it to `U.unionCases`,
so both the writer's and the reader's `match` stop compiling until they handle it.

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	point
end 'Shape'

function kindOf(tag Amount) returns String
	let kind = try Shape.unionCases.fromRawValue(tag) otherwise panic("unknown Shape tag {tag}")
	return match kind 'kind'
		circle gives "circle"
		square gives "square"
		point gives "point"
	end 'kind'
end 'kindOf'

function main() returns ExitCode
	let s = Shape.square(3)
	print("{kindOf(s.rawValue)}\n")    // square
	return 0
end 'main'
```

The tags are declaration positions unless a case declares its own, so reordering cases changes stored
tags; treat a persisted union as append-only.

---

## Variables

### Mutable Variables (`var`)

```maxon
var x = 42              // type inferred from the initializer
x = x + 5               // reassignment allowed
```

### Immutable Variables (`let`)

```maxon
let pi = 3.14159
let name = "Maxon"
// pi = 3.14            // E2013: cannot assign to immutable variable: 'pi'
```

A `let` cannot be mutated through a method either: calling a method that writes its receiver (`push`,
`append`, `set`, `remove`, `clear`, …) on a `let` is **E3019**.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var items = TallyArray.create()
	items.push(1)                    // fine: items is a var
	// let fixed = TallyArray.create()
	// fixed.push(1)                 // E3019: cannot pass 'fixed' to function that mutates parameter 'self'
	print("{items.count()}\n")
	return 0
end 'main'
```

### Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  Local declarations take no type annotation.
- Variables are block-scoped.
- **A `var` that is never reassigned or mutated is E3077** (`variable 'x' is never reassigned; use 'let'
  instead of 'var'`).
- **Every variable must be used (E3012).** This covers `let`/`var`, parameters, loop variables, pattern
  bindings and closure parameters. The name `_` discards: it creates no binding and may appear several
  times in one pattern (`for (_, _) in pairs`). Names such as `_x` are ordinary variables. A method that
  implements an interface requirement is exempt for its parameters, since the interface dictates them.
- **Records are shared, not copied.** `var b = a` for a record-typed `a` hands `b` the same record; use
  `a.clone()` for an independent copy (see
  [Reference-by-Default Assignment](#reference-by-default-assignment)).
  Numbers, `bool` and other scalar values are always independent copies.
- **A mutable name takes an immutable name's record only by moving it** (see
  [Immutable XOR Mutable](#immutable-xor-mutable)):

  ```maxon
  typealias Coord = int(i64.min to i64.max)

  type Point
  	export var x as Coord

  	export static function create(x Coord) returns Point
  		return Point{x: x}
  	end 'create'
  end 'Point'

  function main() returns ExitCode
  	let a = Point.create(1)
  	var b = a                 // a MOVES into b; reading a afterwards is E3102
  	b.x = 10

  	let c = Point.create(3)
  	let d = c                 // d aliases c: two immutable names
  	// var e = c              // E3078: cannot assign immutable variable 'c' to mutable binding 'e'
  	var f = c.clone()         // an independent record
  	f.x = 30
  	print("{b.x} {c.x} {d.x} {f.x}\n")   // 10 3 3 30
  	return 0
  end 'main'
  ```

### Top-Level Variables

Variables can be declared at file scope, outside any function:

```maxon
typealias Tally = int(0 to u64.max)

var callCount = 0
let MAX_SIZE = 1024
var names = ["ada", "alan"]
let dataFile = FilePath from "data.txt"

function bump() returns Tally
	callCount = callCount + 1
	return callCount
end 'bump'

function main() returns ExitCode
	_ = bump()
	print("{callCount} {MAX_SIZE} {names.count()} {dataFile.filename()}\n")   // 1 1024 2 data.txt
	return 0
end 'main'
```

- `var` declares module state any function in the file can reassign; `let` declares a constant.
- An initializer is a literal, a constant expression, an enum case, an array or dictionary literal,
  `Type from "literal"`, a static factory call (`let shared = Cache.create()`), or a free function call that
  returns a record (`let shared = makeCache()`). A free function call returning a scalar, and any other call,
  is **E2045** (`Function calls are not allowed in global variable initializers`).
- Every initializer runs **before `main`**, once, in dependency order, whether or not anything reads the
  binding. Initializers that depend on each other in a cycle are **E2012**. A `let` whose value is decided at
  compile time is image data, laid down in read-only memory with nothing to run; anything else runs in the
  program's `__module_init` before `main`. A static field follows the same rules
  (see [Static Fields](#static-fields)).
- A field read off another global (`let n = shared.count`) is **E2015**, and a struct literal at file scope
  is **E3076**; call a factory instead, or declare a static field inside the type.
- An initializer cannot name another global, but what it calls may. A `let`'s initializer may not reach a
  module-level `var` that holds a record — a String, an array, a struct, a boxed union — through any function
  it calls (**E3165**, reported at the `var`'s use with its declaration as a note). A `let` is fixed at startup
  and may not hold what a `var` owns, and what the initializer keeps is not followed, so reading only a number
  out of the `var` is refused too. "Reaches" is the call graph after overload resolution, dispatches
  included. A scalar `var` is readable, a `var`'s initializer may reach any global, and a `let`'s may reach
  other `let`s.
- The other direction is refused too: a `var`'s initializer may not call anything whose result may be, lie
  within or hold a module-level `let`'s record (**E3166**, at the `var`'s declaration, naming the call). What
  a call hands back is followed through further calls, witness dispatches and calls through function values;
  a record built fresh from numbers read out of a `let` is legal. The same fact refuses a write, inside a
  function, through a record a call handed back out of a `let` (**E3159**).
- A `spawn` reachable from a global initializer is **E3164**; start services in `main`.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
- A [service](#services--spawn) handler may not read or write a module-level `var`
  (**E3143**); keep service state in its fields.

---

## Functions

### Declaration

```text
function name(param Type, other Type = default) returns ReturnType throws ErrorType
	statements
end 'name'
```

```maxon
typealias Amount = int(i64.min to i64.max)

function add(a Amount, b Amount) returns Amount
	return a + b
end 'add'

function greet(name String)
	print("Hello, {name}\n")
end 'greet'
```

- The label after `end` repeats the function's name.
- A function that returns a value declares `returns Type`; one that returns nothing omits the clause.
- A parameter is written `name Type`. Parameter types follow the
  [typealias rule](#primitives-go-through-a-typealias).
- A function that can fail declares `throws ErrorType` (see
  [Error Handling](#error-handling)).
- Name a parameter `_` to accept and ignore an argument: `function onClick(_ MouseEvent)`.
- A function is private to its file unless marked `export`, `module` or `public` (see
  [Namespaces](#namespaces)).

### Named Arguments

Maxon calls use a **first-positional, rest-named** rule:

- The first argument is positional. Labelling it is **E2052** (`the first argument cannot be named`).
- Every later argument is written `name: value`; omitting a label is **E2053**.
- Named arguments may appear in any order, and parameters with defaults may be omitted.

```maxon
typealias Amount = int(i64.min to i64.max)

function connect(host String, port Amount, secure bool = false) returns String
	return "{host}:{port} secure={secure}"
end 'connect'

function main() returns ExitCode
	print("{connect("localhost", port: 8080)}\n")                    // localhost:8080 secure=false
	print("{connect("example.com", secure: true, port: 443)}\n")     // example.com:443 secure=true
	return 0
end 'main'
```

### Default Values

A parameter may declare a default, used when the call omits that argument. The default is any expression —
a literal, an enum case, a factory call, a byte string — and is evaluated at each call that needs it.

```maxon
typealias Retries = int(0 to 10)

enum Priority
	low
	medium
	high
end 'Priority'

function greet(name String, title String = "Mr.")
	print("Hello, {title} {name}\n")
end 'greet'

function schedule(job String, retries Retries = 3, level Priority = Priority.medium, separator Character = '/')
	print("{job}{separator}{retries}{separator}{level.name}\n")
end 'schedule'

function main() returns ExitCode
	greet("Smith")                          // Hello, Mr. Smith
	greet("Smith", title: "Dr.")            // Hello, Dr. Smith
	schedule("backup")                      // backup/3/medium
	schedule("sync", level: Priority.high)  // sync/3/high
	return 0
end 'main'
```

Parameters with defaults come after the parameters without them.

### Caller-Location Defaults (`__line__`, `__file__`)

`__line__` and `__file__` are legal **only** as a parameter's default value. They expand at each **call
site**, so a helper — an assertion, a logger — can report where it was called from:

| Default | Value at the call site | Declare the parameter as |
|---------|------------------------|--------------------------|
| `__line__` | the line of the callee's name | `SourceLineNumber` |
| `__file__` | the calling file's path, relative to the compile root, with `/` separators | `String` |

```maxon
function check(ok bool, message String, from String = __file__, at SourceLineNumber = __line__)
	if not ok 'failed'
		print("{from}:{at}: {message}\n")
	end 'failed'
end 'check'

function main() returns ExitCode
	check(1 + 1 == 2, message: "arithmetic")
	check(2 + 2 == 5, message: "expected 5")                          // main.maxon:9: expected 5
	check(false, message: "forwarded", from: "other.maxon", at: 42)   // other.maxon:42: forwarded
	return 0
end 'main'
```

- Declare both or neither: a line number without its file names a line in no particular file.
- An explicit argument replaces the default, which is how a helper forwards the location it was given:
  `check(ok, message: m, from: from, at: at)`.
- `__file__` is always relative, so the same source builds the same binary on any machine.
- Anywhere else — an ordinary expression, a struct field default — is **E2060**.

### Function Overloads

Several functions may share a name when their parameters differ.

**By parameter type** — the argument types choose the overload:

```maxon
typealias Tally = int(0 to u64.max)

function measure(value Tally) returns Tally
	return value * 2
end 'measure'

function measure(value String) returns Tally
	return value.count()
end 'measure'

function main() returns ExitCode
	print("{measure(21)} {measure("hello")}\n")    // 42 5
	return 0
end 'main'
```

**By parameter name** — overloads whose later parameters have different names are chosen by the labels
at the call:

```maxon
typealias Integer = int(i64.min to i64.max)

function slice(start Integer, endIndex Integer) returns Integer
	return endIndex - start
end 'slice'

function slice(start Integer, length Integer) returns Integer
	return start + length
end 'slice'

function main() returns ExitCode
	print("{slice(10, endIndex: 32)} {slice(10, length: 32)}\n")    // 22 42
	return 0
end 'main'
```

- A call that more than one overload matches is **E3007** (`Ambiguous overload`). Because the first argument
  is never labelled, two single-parameter overloads of the same type cannot be told apart.
- Declaring the same overload twice, with the same parameter names and types, is a duplicate
  definition (**E3006**). Overloads with the same parameter types but different parameter names are
  separate declarations; a call whose labels cannot tell them apart is **E3007**.
- A type may declare a `static` method and an instance method with the same name and parameters:
  `Type.name()` calls the static one and `value.name()` the instance one.

### Parameter Passing

A parameter the function only reads is passed by value. A parameter the function **assigns to** — directly,
or through one of its fields or elements — is passed by reference, so the write reaches the caller's
variable:

```maxon
typealias Tally = int(0 to u64.max)

function increment(n Tally)
	n = n + 1
end 'increment'

function main() returns ExitCode
	var x = 10
	increment(x)
	print("{x}\n")     // 11
	return 0
end 'main'
```

- Passing a `var` lets the callee's writes propagate. Any storage you could assign to at that point may be
  passed and is written in place: a local `var`, a module-level `var`, a field of a binding (`p.x`), a field
  of the receiver (`count` or `self.count`), and a chain of them (`p.a.b`). The rule is exactly the
  assignment rule — an argument the callee writes is accepted here if and only if writing the same thing at
  the call site would be accepted.
- Passing a `let` to a parameter the callee writes is **E3019** (`cannot pass 'y' to function that mutates
  parameter 'n'`). A method writing a field of its **own** receiver is not a parameter write, so
  `let acc = Accumulator.create()` followed by `acc.add(10)` is legal. An immutable field, or a field of an
  immutable instance, is refused for the same reason the equivalent assignment is.
- Passing a collection to a parameter the callee **reassigns** while a reference into that collection is
  still live is **E3070**: the reassignment frees what the reference points into.
- Passing a literal or another expression gives the callee a temporary; its writes have no visible effect.

### Function Types and Function Values

Functions are values: a bare function name (no parentheses) is a reference to it, and it can be stored,
passed and returned. A function type is written with `function` and is always named by a typealias; the
alias is what appears in parameters, returns, fields and generic arguments.

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function double(x Score) returns Score
	return x * 2
end 'double'

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function pickDouble() returns UnaryOp
	return double
end 'pickDouble'

function main() returns ExitCode
	let f = pickDouble()
	print("{f(21)} {apply(double, x: 4)}\n")    // 42 8
	return 0
end 'main'
```

Omit `returns` for a function type that returns nothing: `typealias Callback = function()`. A function type cannot express `throws`, so a
throwing function cannot be used as a value (**E3101**) — wrap it in a function that handles the error.
Function-type aliases are [brands](#generic-instance-and-function-type-aliases-are-brands).

### Closures

A closure is an anonymous function written `function(parameters) gives expression`:

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	var offset = 10
	let addOffset = function(n Score) gives n + offset
	offset = 20
	print("{apply(addOffset, x: 5)}\n")     // 25: the closure sees the current offset
	return 0
end 'main'
```

- **Captures are by reference.** A closure reads a captured variable's current value when it runs.
- **A closure that captures cannot outlive its frame.** Returning one, or storing it in a field, a
  global, a container or a union payload, is **E3099**. Passing it down to a function that calls it is fine.
  A closure that captures nothing is a plain function reference and can go anywhere.
- A parameter's type may be omitted when the closure is written directly as a call argument whose parameter
  is declared with a function type: the closure's parameters take that type's parameter types, in order —
  `scores.sort(function(a, b) gives b.compare(a))`. A parameter past that function type's arity is **E2003**,
  and an omitted type anywhere else is **E2015**. When overloads of the callee declare different function
  types at that argument, none is offered.
- Closure parameters must be used (**E3012**); write `_` for an unused one.
- Inside an instance method a closure may use `self`; elsewhere `self` is **E2001**.
- Assigning to a captured `let` is an error, as it is outside the closure.

### Function Purity and Discarded Results

A function's result must be used. The compiler infers whether a function is **pure** (no output, no writes
to globals or parameters, only pure callees) or **impure**, and the rules for ignoring a result differ:

| Callee | Bare call statement | `_ = call()` |
|--------|---------------------|--------------|
| pure function | **E3064** — the call does nothing | **E3064** |
| impure function | **E3065** — result not used | allowed |
| chainable method (returns its own receiver type) | allowed | allowed |

```maxon
typealias Tally = int(0 to u64.max)

var counter = 0

function incrementAndGet() returns Tally
	counter = counter + 1
	return counter
end 'incrementAndGet'

function main() returns ExitCode
	_ = incrementAndGet()              // explicitly discarded
	let now = incrementAndGet()        // used
	// incrementAndGet()               // E3065: result of 'incrementAndGet' is not used
	print("{now}\n")                   // 2
	return 0
end 'main'
```

A function that returns nothing has no result to discard. Destructuring a pure function's tuple result must
keep at least one element (`(_, _) = pure()` is **E3064**).

---

## Expressions

### Operator Precedence

From highest to lowest:

1. **Postfix**: `.` (member access), `()` (call)
2. **Unary**: `-` (negation), `not`
3. **Cast**: `as`
4. **Multiplicative**: `*` `/` `mod`
5. **Additive**: `+` `-`
6. **Shift**: `shl` `shr`
7. **Comparison**: `==` `!=` `<` `>` `<=` `>=` `is` `is not`
8. **AND**: `and`
9. **XOR**: `xor`
10. **OR**: `or`
11. **Range**: `to` `upto`
12. **Conditional**: `value if condition else other`

Parentheses override precedence: `(2 + 3) * 5` is `25`. A cast applies to the negated value:
`-x as Small` is `(-x) as Small`.

### Arithmetic Operators

| Operator | Meaning | Operands |
|----------|---------|----------|
| `+` | addition | integers, floats |
| `-` | subtraction | integers, floats |
| `*` | multiplication | integers, floats |
| `/` | division (integers truncate toward zero) | integers, floats |
| `mod` | remainder | integers |

- Both operands have the same type. An unaliased integer mixed with a float is promoted to float; two
  different typealiases are **E3005** until one side is cast (see
  [Arithmetic](#arithmetic)).
- Integer arithmetic is 64-bit two's complement and **wraps** on overflow.
- `+` is not defined on `String`; use interpolation or `append`.
- `bool` values do not take part in arithmetic (**E2004**).

### Division by Zero

Dividing by zero is not undefined behaviour and does not crash: `/` and `mod` whose divisor **might** be zero
**throw** `DivisionByZero`, so the failure is part of the type system and behaves identically on every target.

- **The divisor is provably non-zero** — a non-zero literal (`x / 4`), or a value whose ranged type excludes 0
  — and the divide compiles as-is, with no check.
- **The divisor might be zero** — the divide throws, and must be written `try (a / b) otherwise …` (or
  propagated from a function that `throws`). A bare divide is **E3057** (`throwing division requires try`).
- **The divisor is always zero** — a literal `0`, `0.0`, or a constant bound to one — is **E3103**
  (`division by zero: the divisor of '/' is always 0`).

```maxon
typealias Amount = int(i64.min to i64.max)
typealias NonZero = int(1 to 1000)

function ratio(a Amount, b Amount) returns Amount
	return try (a / b) otherwise 0          // b may be zero: supply a fallback
end 'ratio'

function share(total NonZero, parts NonZero) returns NonZero
	return total / parts                    // parts excludes 0: no try
end 'share'

function main() returns ExitCode
	print("{ratio(10, b: 0)} {ratio(10, b: 3)} {share(100, parts: 4)}\n")   // 0 3 25
	return 0
end 'main'
```

The error value is the case `divisionByZero`, which a handler can match:

```maxon
try (total / count) otherwise (e) 'handle'
	match e 'kind'
		divisionByZero then print("nothing to divide by\n")
	end 'kind'
end 'handle'
```

- **`try` applies to the division itself.** `try (10 + a / b)` is **E2015** (`try must be applied to a
  call`); compute the quotient first, then use it.
- **Float division** throws on a zero divisor too, including `-0.0`. `inf` and `NaN` produced any other way
  are ordinary IEEE values. There is no float `mod`.
- `i64.min mod -1` is `0` on every target. `i64.min / -1` has no representable quotient, and a `try`
  cannot catch it: on every target the program stops with `panic: integer overflow`, a stack trace and
  exit code 1.

### Comparison Operators

| Operator | Meaning |
|----------|---------|
| `==` `!=` | equal, not equal |
| `<` `>` `<=` `>=` | ordering |

All produce `bool`. Numbers, `bool` and `Character` support every comparison; `String` supports only `==`
and `!=`. Comparing two records with `==` calls the type's `equals` method (see
[Equality and Copying](#equality-and-copying)); unions cannot be compared
(**E3066**). Values of different types never compare (**E3005**).

### Reference Identity Operators

`a is b` is `true` when two names refer to the **same** record; `a is not b` is its negation. They apply only
to heap records — on numbers, on `bool`, and on an
[inline packed record](#inline-packed-records) (whose value is a word, not a record) they are **E3068**.

```maxon
function sameRecord(a Point, b Point) returns bool
	return a is b
end 'sameRecord'
```

### Logical and Bitwise Operators

The word operators are logical on `bool` operands and bitwise on integer operands:

| Operator | On `bool` | On integers | Example |
|----------|-----------|-------------|---------|
| `and` | logical AND | bitwise AND | `0xF0 and 0x3C` is `48` |
| `or` | logical OR | bitwise OR | `0xF0 or 0x0F` is `255` |
| `xor` | logical XOR | bitwise XOR | `0xF0 xor 0xFF` is `15` |
| `not` | logical NOT | bitwise NOT | `not 0` is `-1` |

On `bool` operands `and` and `or` **short-circuit**: the right side runs only if the left side does not
already decide the result, so a guard on the left can protect the right:

```maxon
let ok = i < items.count() and (try items.get(i) otherwise 0) > 0
```

Integer `and`/`or` always evaluate both sides.

### Shift Operators

| Operator | Meaning | Example |
|----------|---------|---------|
| `shl` | shift left | `1 shl 4` is `16` |
| `shr` | shift right | `256 shr 4` is `16` |

- **`shr` fills according to the left operand's type.** A signed value shifts arithmetically
  (`(0 - 8) shr 1` is `-4`); an unsigned alias or a `bits(n)` pattern fills with zeros
  (`u64.max as Word shr 60` is `15`). `shl` always fills with zeros.
- **A count of 64 or more shifts every bit out** — `1 shl 64` is `0`. It is not reduced modulo 64.
- **A negative count is an error.** A count known at compile time is **E2054**; one that turns out
  negative at run time panics with `negative shift count`.

### Unary Operators

| Operator | Meaning |
|----------|---------|
| `-x` | negation |
| `not x` | logical NOT (`bool`) or bitwise NOT (integer) |

`-` does not chain: write `-(-x)`, since `--x` is a syntax error. `not not x` is allowed.

On a float, `-x` flips the sign bit (IEEE-754 negation), so `-0.0` is a distinct value and `-(0.0)` prints
`-0.0`. Negating a signed integer alias keeps the alias; negating an unsigned alias gives an unaliased value.

### Conditional Expression

```text
<value> if <condition> else <other>
```

The condition is `bool` and both branches have the same type. The conditional binds more loosely than every
operator, and chains to the right:

```maxon
let magnitude = x if x >= 0 else -x
let tier = "gold" if score > 90 else "silver" if score > 70 else "bronze"
print("Status: {"on" if enabled else "off"}\n")
```

### Ranges

`a to b` (inclusive) and `a upto b` (exclusive) produce a range. In a `for` header or a `match` pattern a
range is syntax; elsewhere an integer range is a `Range` or `OpenRange` value that can be stored and
iterated (see [For Loop](#for-loop)).

### `sizeof` and `countof`

Both take a **type** and produce a compile-time integer.

- `sizeof(T)` is the size of a value of type `T` in bytes: `sizeof(int)` and `sizeof(float)` are `8`,
  `sizeof(bool)` is `1`, and a record type is the size of its fields — except an
  [inline packed record](#inline-packed-records), which is `8` whatever its fields sum to.
- `countof(T)` is the number of elements a **fixed-size** container type holds: `countof(Vector with 3 Int)`
  is `3`. Inside such a container's own body, `countof(Self)` is the receiver's count. A type with no fixed
  element count — a record, a primitive, a growable `Array` — is refused (**E2015**); ask an `Array` for its
  `count()` instead.

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

type Pair
	export var left as Int
	export var right as Int
end 'Pair'

function main() returns ExitCode
	print("{countof(Vec3)} {sizeof(Pair)} {sizeof(bool)}\n")    // 3 16 1
	return 0
end 'main'
```

### Collection Access

Maxon has no subscript operator: `items[0]` is a syntax error. Elements are read and written through
methods, and **every access is bounds-checked**:

- `get(index)` returns the element, or **throws** `ArrayError.indexOutOfBounds` when the index is at or past
  `count()`. Like any throwing call it needs `try`.
- `set(index, value:)` replaces an element and throws the same error for an index past the end.
- `first()`, `last()`, `pop()` and `remove(index)` throw when there is no such element.
- An index is a non-negative integer: a negative literal is **E3005**, and a computed negative index panics
  with a range check.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var values = TallyArray.create()
	values.push(10)
	values.push(20)

	let second = try values.get(1) otherwise 0           // 20
	let missing = try values.get(5) otherwise 0          // 0: index 5 is out of range
	try values.set(0, value: 99) otherwise panic("index out of range")
	print("{second} {missing} {try values.first() otherwise 0}\n")   // 20 0 99
	return 0
end 'main'
```

**Sizing an array.** `create()` makes an empty array; `push(value)` appends; `reserve(n)` grows capacity
without changing `count()`. `resize(n)` changes the length and fills new slots with zero, so it is available
only for elements stored inline — integers, floats, `bool`, payload-free enums. For an array of records,
strings or other managed values, `resize` is **E3106**; grow with `push` or `growFilled(n, value:)` and shrink
with `truncate(n)`:

```maxon
typealias Names = Array with String

function main() returns ExitCode
	var names = Names.create()
	names.growFilled(3, value: "")    // three empty strings
	names.push("ada")
	names.truncate(2)
	print("{names.count()}\n")        // 2
	return 0
end 'main'
```

The collection types and their full method lists are in the [standard library reference](STDLIB_REFERENCE.md).

---

## Statements

Maxon statements are newline-delimited: one statement per line, no semicolons. Every block — `if`,
`else`, `while`, `for`, `match`, `try` — opens with a quoted **block label** and closes with `end` and the
same label. A missing label is **E2042**, and a match whose `end` names a different label is **E2043**.

### Expression Statements

A call on its own line is a statement. Its result must be used or explicitly discarded (see
[Function Purity and Discarded Results](#function-purity-and-discarded-results)):

```maxon
print("hello\n")
_ = incrementAndGet()
```

### Return

```text
return <expression>
return
```

A function with a `returns` clause must return a value on every path; a path that falls off the end is
**E3013** (`missing return statement`). A function without `returns` uses a bare `return`, or none.

### Variable Declaration and Assignment

```maxon
var count = 10
let limit = 20
count = count + limit
```

Assigning to a `let` is **E2013**. Assigning a variable to itself (`x = x`, `p.x = p.x`) has no effect
and is **E3067**. See [Variables](#variables) for the full rules.

### Tuple Assignment

Assign the elements of a tuple to existing `var`s, declare new names in the same pattern, or discard
elements with `_`:

```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)      // x = 10, y = 32
(x, let z) = makePair(3, b: 4)    // x existing, z newly declared
(y, _) = makePair(5, b: 6)        // discard the second element
```

- Every name without `var`/`let` must already be a `var`; a `let` target is **E2013**.
- The number of names must match the tuple's element count (**E3005**).
- Discarding every element of a pure function's result is **E3064**.

### If Statement

```text
if <condition> 'label'
	<statements>
end 'label'
```

`else` follows the closing `end` on the same line and opens its own labelled block. An else-if chain
nests another `if` in that position:

```maxon
if size == 0 'zero'
	print("empty\n")
end 'zero' else if size < 10 'small'
	print("small\n")
end 'small' else 'large'
	print("large\n")
end 'large'
```

- The condition must be `bool`; there is no implicit truthiness.
- An empty block is **E3082**. A block holding only a comment is empty too, since a comment is not a
  statement. This applies to every `if`, `else`, `while`, `for` and `try … otherwise` block.

### While Loop

```maxon
var i = 0
while i < 10 'loop'
	print("{i}\n")
	i = i + 1
end 'loop'
```

### For Loop

`for … in` iterates anything that implements `Iterable`: arrays, strings, maps, sets, lists, ranges and
your own types.

```maxon
let numbers = [1, 2, 3, 4, 5]
for num in numbers 'loop'
	print("{num}\n")
end 'loop'
```

**Ranges.** `a to b` is inclusive and `a upto b` excludes `b`. Integer and `Character` ranges are both
supported:

```maxon
for i in 1 to 5 'inclusive'      // 1, 2, 3, 4, 5
	print("{i}")
end 'inclusive'

for i in 1 upto 5 'exclusive'    // 1, 2, 3, 4
	print("{i}")
end 'exclusive'

for c in 'a' to 'z' 'letters'
	print("{c}")
end 'letters'
```

In a `for` header a range compiles to a counted loop with no allocation. Anywhere else an integer range is
a value: `a to b` is a `Range` and `a upto b` an `OpenRange`, both `Iterable`, so a range can be stored,
passed and iterated later:

```maxon
let r = 1 upto 4
for x in r 'loop'
	print("{x}")                 // 1, 2, 3
end 'loop'
```

**Destructuring.** When the elements are tuples — a map yields `(key, value)` — the loop variable can be
a tuple pattern:

```maxon
let ages = ["ada": 36, "alan": 41]
for (name, age) in ages 'loop'
	print("{name}: {age}\n")
end 'loop'
```

**Positions.** `.withIterator()` pairs each element with the iterator that produced it, which exposes
`index()` and navigation methods such as `advance()`, `retreat()`, `seek(index)` and `peek(ahead)`:

```maxon
let names = ["Alice", "Bob", "Charlie"]
for (iter, name) in names.withIterator() 'loop'
	print("{iter.index()}: {name}\n")
end 'loop'
```

**Notes:**
- The loop variable is immutable.
- Each loop obtains a fresh iterator (`createIterator()`), so a collection can be re-iterated and nested
  loops over one collection are safe.
- Loop variables must be used (**E3012**). Write `_` for one you do not need: `for _ in items 'loop'`,
  `for (key, _) in pairs 'loop'`.

### Break and Continue

```text
break              // leave the innermost loop
break 'label'      // leave the labelled enclosing loop
continue           // next iteration of the innermost loop
continue 'label'   // next iteration of the labelled enclosing loop
```

```maxon
var total = 0
for i in 0 upto 3 'rows'
	for j in 0 upto 3 'cols'
		if j == 2 'skip'
			continue 'rows'
		end 'skip'

		total = total + i + j
	end 'cols'
end 'rows'
```

A label that names the innermost loop is redundant and is **E2048**; use a label only to reach an outer
loop, or to reach a loop from inside a `match` arm (where a bare `break` leaves the `match`).

### Match Statement

`match` compares a value against patterns, one arm per line. Each arm is `pattern then <statement>` — a
single statement.

```maxon
typealias Score = int(i64.min to i64.max)
typealias Category = int(0 to 3)

function classify(n Score) returns Category
	match n 'check'
		0 then return 1
		1 to 5 then return 2
		6 upto 10 then return 3
		default then return 0
	end 'check'
end 'classify'
```

**Patterns:**

| Pattern | Matches |
|---------|---------|
| `42`, `"text"`, `'c'` | a single value |
| `a to b` | an inclusive range: `1 to 5` is 1 through 5 |
| `a upto b` | a range excluding `b`: `1 upto 5` is 1 through 4 |
| `min upto b`, `min to b`, `a to max` | an open-ended range: `min upto 0` is every negative value |
| `caseName` | an enum or union case (bare name, never `Type.case`) |
| `caseName(x, y)` | a union case, binding its associated values |
| `p1 or` ⏎ `p2` | any of several patterns, one per line |
| `default` | anything not matched above; must be the last arm |

Range patterns work on integers, floats and any `Comparable` type such as `Character`. Each bound is a
literal, or `min`/`max` for an open end. A range covering exactly one value (`5 to 5`, `'a' upto 'b'`) is **E2027** — write the
value. Covering a value or case twice is also **E2027**.

**Alternatives.** An arm covering several patterns joins them with `or`, **one alternative per line**; the
last alternative carries `then` and the body. Two alternatives on one line are **E3147**.

```maxon
match score 'grade'
	90 to 100 or
		85 to 89 then print("A\n")
	70 to 84 then print("B\n")
	default then print("C\n")
end 'grade'
```

**Exhaustiveness.** A match on an enum or union must name every case (**E2026** lists the missing ones).
A plain `default` arm on an enum or union is **E2046**: when a case is added later, a silent default would
absorb it. To ignore some cases, name them in an `or`-chain ending in `break`; to treat them as a bug or an
error, use `default panic("…")` or `default throws` (below). Matches on other types (integers, floats,
strings, characters) need a `default` arm unless the patterns cover every value.

```maxon
enum Level
	trace
	info
	warning
	error
end 'Level'

function report(level Level)
	match level 'filter'
		error then print("error!\n")
		trace or
			info or
			warning then break
	end 'filter'
end 'report'
```

There is no range over enum or union cases (`trace to warning` is **E3146**): a case declared inside the
span later would be absorbed without anyone deciding about it.

**Break and fallthrough.** `break` in an arm leaves the `match`; `break 'label'` leaves an enclosing loop.
`<statement> and fallthrough` runs the arm and then the next arm's body without testing its pattern:

```maxon
var result = 0
match x 'cascade'
	1 then result = result + 10 and fallthrough
	2 then result = result + 20
	default then result = 100
end 'cascade'
// x == 1 gives 30, x == 2 gives 20
```

**Union payloads.** `caseName(a, b)` binds a union case's associated values for that arm:

```maxon
typealias Amount = int(i64.min to i64.max)

union Outcome
	success(value Amount)
	failure(code Amount, message String)
	pending
end 'Outcome'

function show(r Outcome)
	match r 'handle'
		success(v) then print("ok {v}\n")
		failure(_, message) then print("failed: {message}\n")
		pending then print("waiting\n")
	end 'handle'
end 'show'
```

**Notes:**
- An arm body is one statement. Block-opening statements (`if`, `while`, `for`, a nested `match`, a
  multi-line `try`) are **E2049** — call a function instead. Every single-line `try` form is allowed.
- Arms use bare case names; `Level.trace` in an arm is **E3075**.
- Bindings must be used (**E3012**); discard one with `_`: `pair(_, second)`. To ignore all of a case's
  payload, omit the parentheses (`success then …`); `success(_)` with every binding discarded is **E3081**.
- `and fallthrough` cannot follow `return`.

### Match Expression

A match that produces a value uses `gives` instead of `then`:

```maxon
let points = match letterGrade 'convert'
	"A" gives 4
	"B" gives 3
	"C" gives 2
	default gives 0
end 'convert'
```

Every `gives` arm must produce the same type. `break` and `and fallthrough` are not allowed in a match
expression, since every arm must yield a value or leave.

**Diverging arms.** An arm may leave instead of producing a value, with `panic("…")` or
`throws ErrorType.case`. The result type comes from the `gives` arms; a diverging arm still counts toward
exhaustiveness. `throws` requires the enclosing function to declare that error type.

```maxon
function weight(c Color) returns Amount
	return match c 'weigh'
		red panic("red has no weight")
		green gives 1
		blue gives 2
	end 'weigh'
end 'weight'
```

### Default Throws and Default Panic

`default throws ErrorType.case` and `default panic("message")` are the two `default` forms allowed on an
enum or union, in both match statements and match expressions:

- **`default throws`** throws the error when no arm matches. The enclosing function must declare
  `throws ErrorType`, and the caller handles it with `try`.
- **`default panic("…")`** stops the program with that message (see
  [Panic](#panic)). Use it for cases that indicate a bug.

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	triangle(base Amount, height Amount)
end 'Shape'

union ShapeError implements Error
	unsupported(name String)
end 'ShapeError'

function area(shape Shape) returns Amount throws ShapeError
	return match shape 'calc'
		circle(r) gives 3 * r * r
		square(s) gives s * s
		default throws ShapeError.unsupported("triangle")
	end 'calc'
end 'area'

function main() returns ExitCode
	let a = try area(Shape.square(4)) otherwise 0
	print("{a}\n")                                    // 16
	return 0
end 'main'
```

---

## Error Handling

Maxon has two kinds of failure:

- **Errors** are expected outcomes — a missing file, invalid input. A function declares the error type it
  `throws`, and every caller handles it with `try`. There are no exceptions that unwind silently, no null
  values and no optional types.
- **Panics** are bugs — a broken invariant, an out-of-range value reaching a checked place. A panic stops
  the program with a message and a stack trace, and cannot be caught.

### Defining Error Types

An error type is an `enum` or a `union` that implements `Error`:

```maxon
typealias HttpCode = int(100 to 599)

enum FileError implements Error
	notFound
	permissionDenied
	alreadyExists
end 'FileError'

union FetchError implements Error
	timedOut
	status(code HttpCode)
end 'FetchError'
```

A union error carries data about the failure, read back with `match`. Only enum and union values can be
thrown; throwing a record is **E3005** (`throw requires an error enum value`).

### Throwing Functions

A function that can fail declares its error type with `throws`, after any `returns`:

```maxon
typealias Amount = int(i64.min to i64.max)

enum ParseError implements Error
	empty
	invalidSyntax
end 'ParseError'

function parseDigit(s String) returns Amount throws ParseError
	if s.isEmpty() 'empty'
		throw ParseError.empty
	end 'empty'

	return match s 'digit'
		"0" gives 0
		"1" gives 1
		"2" gives 2
		default throws ParseError.invalidSyntax
	end 'digit'
end 'parseDigit'

function requireName(name String) throws ParseError
	if name.isEmpty() 'missing'
		throw ParseError.empty
	end 'missing'
end 'requireName'
```

`throw` is legal only in a function that declares `throws`, and the value must be of the declared type.

### Panic

`panic("message")` stops the program. The message may be interpolated. The program writes the message with
its source location and a stack trace to stderr and exits with code **1**:

```maxon
typealias Amount = int(i64.min to i64.max)

function processValue(x Amount) returns Amount
	if x < 0 'negative'
		panic("processValue: negative input, got {x}")
	end 'negative'

	return x * 2
end 'processValue'

function main() returns ExitCode
	print("{processValue(-3)}\n")
	return 0
end 'main'
```

```text
panic at main.maxon:5: processValue: negative input, got -3
Stack trace:
  in processValue
  in main
  in mrt_start
```

The trace lists the call chain innermost first, up to 100 frames. The runtime raises the same kind of panic
for a failed [range check](#range-checks), a negative shift count and `i64.min / -1`
(`panic: integer overflow`). A recursion that outgrows its thread's stack stops with
`panic: stack overflow` and the same trace on every native target; on `wasm32-wasi` it is the engine's own
trap.

Use `panic` for invariant violations and unreachable paths; use `throw` for conditions a caller should
handle.

### Calling Throwing Functions

Every call to a throwing function is marked with `try`. A call without it is **E3057** (`throwing function
requires try`). `try` is followed by exactly one of:

- **nothing** — propagate the error to the caller ([Error Propagation](#error-propagation)), or
- an **`otherwise`** clause that handles it.

### Handling Errors with `otherwise`

#### Default Value

```maxon
let digit = try parseDigit(text) otherwise 0
```

If the call throws, the expression takes the fallback value, which must have the call's result type.

#### Ignore

```maxon
try requireName(name) otherwise ignore
```

Discards the error. Reserve it for best-effort work such as cleanup.

#### Panic

```maxon
let slot = try slots.get(index) otherwise panic("unreachable: index was validated")
```

Turns an error that cannot happen into a panic, instead of hiding it behind a made-up default.

#### Single Statement

`return`, `break`, `continue` or `throw` on the error path:

```maxon
typealias Amount = int(i64.min to i64.max)
typealias StringArray = Array with String

enum AppError implements Error
	badInput
end 'AppError'

function firstDigitOrMinusOne(text String) returns Amount
	let value = try parseDigit(text) otherwise return -1
	return value
end 'firstDigitOrMinusOne'

function sumDigits(parts StringArray) returns Amount
	var total = 0
	for part in parts 'each'
		let d = try parseDigit(part) otherwise continue      // skip bad parts
		total = total + d
	end 'each'

	return total
end 'sumDigits'

function strictDigit(text String) returns Amount throws AppError
	return try parseDigit(text) otherwise throw AppError.badInput   // convert the error
end 'strictDigit'
```

Each statement follows its usual rules: `break` and `continue` need a loop, `throw` needs a `throws` clause.

#### Block Handler

```maxon
try parseDigit(text) otherwise 'failed'
	print("could not parse {text}\n")
	return 1
end 'failed'
```

#### Block with Error Binding

`otherwise (e)` binds the error value for a `match`:

```maxon
try parseDigit(text) otherwise (e) 'failed'
	match e 'kind'
		empty then print("no input\n")
		invalidSyntax then print("not a digit\n")
	end 'kind'
end 'failed'
```

The binding must be used (**E3012**); if the kind of error does not matter, use the plain block form.

### Error Propagation

A bare `try` passes the error on to the caller. It is legal only in a function that declares `throws` with
the **same** error type:

```maxon
function doubleDigit(text String) returns Amount throws ParseError
	let d = try parseDigit(text)       // a ParseError propagates to our caller
	return d * 2
end 'doubleDigit'
```

- In a function with no `throws` clause it is **E3059** (`the error has nowhere to go`).
- When the callee's error type differs from the function's, it is **E3059** (`try propagates 'E' but
  enclosing function throws 'Other' — add 'otherwise' to convert`); convert with
  `otherwise throw Other.case`.
- Inside a [test](#testing), a bare `try` on any error type is allowed: an error that reaches
  it fails the test.

### Try Blocks

A `try` block runs several statements and sends every error to one handler. Inside the block, calls to
throwing functions need no `try` of their own:

```maxon
typealias Amount = int(i64.min to i64.max)

enum FileError implements Error
	notFound
end 'FileError'

enum ConfigError implements Error
	badPort
end 'ConfigError'

function readConfig(name String) returns String throws FileError
	if name == "missing" 'absent'
		throw FileError.notFound
	end 'absent'

	return "port=80"
end 'readConfig'

function parsePort(text String) returns Amount throws ConfigError
	if text == "port=80" 'ok'
		return 80
	end 'ok'

	throw ConfigError.badPort
end 'parsePort'

function load(name String)
	try 'reading'
		let raw = readConfig(name)
		let port = parsePort(raw)
		print("port {port}\n")
	end 'reading'
	otherwise (e) 'handler'
		match e 'kind'
			FileError.notFound then print("missing\n")
			ConfigError.badPort then print("bad config\n")
		end 'kind'
	end 'handler'
end 'load'

function main() returns ExitCode
	load("app")        // port 80
	load("missing")    // missing
	return 0
end 'main'
```

- The block must contain at least one call that throws (**E3083**).
- The `otherwise` clause is one of:
  - a **handler block** `otherwise (e) 'label' … end 'label'`, which must `match` on the binding (**E3084**);
  - **`otherwise [(e)] panic("message")`**, which panics if the block throws;
  - **`otherwise [(e)] throws ErrorType.case`**, which throws a fixed error to the caller — the binding
    can be wrapped as a payload: `otherwise (e) throws AppError.wrap(e)`.
- When the block's calls throw one error type, `e` has that type and arms use bare case names. When they
  throw several, `e` is a combined error: arms name `ErrorType.case`, and a bare case name is accepted only
  when it is unique across the types (**E3085** otherwise). The match is exhaustive over every pair unless
  it has a `default` arm. An arm may join cases with `or`, including cases of different types; an arm that
  names more than one type binds no payloads (**E3129**).
- When one of those types is a union with payloads, every path through the handler must match `e` exactly
  once (**E3161**): not twice, not inside a loop the handler opens (its `while` condition included), and not
  on only some paths — a `return`, `throw`, `break`, `continue` or propagated error before the match is
  refused, as is a match on one branch of an `if`, on the right of `and`/`or`, or in an arm another arm falls
  through into, where the other path continues without one.
  Statements before the match are fine, and so is one match on each branch of an `if`/`else`. Match `e`
  once and bind what the rest of the handler needs in its arms.
- A call inside the block with its own `try … otherwise` handles its own error, which does not reach the
  block's handler. Nested try blocks compose the same way.

### Conditional Try (`if let … = try`)

`if let` runs a block only when a throwing call succeeds, binding its result:

```maxon
if let value = try parseDigit(text) 'parsed'
	print("got {value}\n")
end 'parsed' else (e) 'failed'
	match e 'kind'
		empty then print("no input\n")
		invalidSyntax then print("not a digit\n")
	end 'kind'
end 'failed'
```

- `if var value = try …` makes the binding reassignable inside the block.
- The `else` block is optional, and so is its `(e)` binding.
- `if try call() 'label'`, with no binding, is only for a call that returns nothing; testing a call that
  returns a value that way discards the value and is **E3124**.

### Errors in Other Positions

- A match arm or match expression can throw with `throws ErrorType.case` or `default throws` (see
  [Default Throws and Default Panic](#default-throws-and-default-panic)).
- A promise from a throwing `async` call is awaited with `try await` (see
  [Concurrency](#throwing-async-functions)).
- A division whose divisor might be zero throws `DivisionByZero` (see
  [Division by Zero](#division-by-zero)).
- A function type cannot declare `throws`, so a throwing function cannot be used as a function value
  (**E3101**).

### Standard Library Error Types

| Error | Cases | Thrown by |
|-------|-------|-----------|
| `ArrayError` | `indexOutOfBounds`, `emptySlot` | `Array` element access |
| `MapError` | `keyNotFound`, `keyAlreadyExists` | `Map.get`, `Map.insert` |
| `IterationError` | `exhausted`, `atStart` | iterators |
| `StringError` | `notFound`, `invalidIndex` | `String` search and indexing |
| `ParseError` | `invalidFormat` | `int.fromString`, `float.fromString`, `bool.fromString` |
| `DivisionByZero` | `divisionByZero` | `/` and `mod` with a divisor that may be zero |
| enum lookups | `noSuchCaseName`, `noSuchRawValue` | `fromName`, `fromRawValue` |

The [standard library reference](STDLIB_REFERENCE.md) lists the error types of each module.

---

## Testing

Tests are part of the language: a `test` declaration sits beside the code it tests, and
[`maxon test`](CLI_REFERENCE.md) finds, compiles and runs them.

### Test Declarations

A `test` is a top-level declaration named with a quoted phrase instead of an identifier:

```maxon
test 'adds two numbers'
	try Expect.equal(2 + 2, expected: 4)
end 'adds two numbers'
```

- The name may contain any character except `'`, and the `end` label repeats it exactly (**E2008** on a
  mismatch). An empty name is **E2059**.
- A test takes no parameters and has no `returns` clause.
- `test` is a contextual keyword: it opens a declaration only at the top level and only when a quoted name
  follows, so `test` remains usable as a variable or parameter name.

### Test Files

Tests live in files whose names end in **`.test.maxon`**. A `test` in any other file is **E2058**. A regular
build skips `*.test.maxon` files, and `maxon test` compiles them together with the rest of the project and
a generated entry point — the project does not need a `main`.

```text
temperature/
├── temperature.maxon          # the code
└── temperature.test.maxon     # its tests
```

### Assertions

Every test implicitly declares `throws TestFailure` (you cannot write the clause yourself). The `Expect`
assertions throw `TestFailure.assertion` when they fail, so each is called with `try` — a forgotten `try` is
a compile error (**E3057**), never an assertion whose failure goes unnoticed.

`temperature.maxon`:

```maxon
typealias Celsius = int(-273 to 10000)
typealias Fahrenheit = int(-460 to 18032)

/// Converts a Celsius reading to Fahrenheit, rounding toward zero.
export function toFahrenheit(c Celsius) returns Fahrenheit
	return c * 9 / 5 + 32
end 'toFahrenheit'

export function describe(c Celsius) returns String
	if c <= 0 'freezing'
		return "freezing"
	end 'freezing'

	return "above freezing"
end 'describe'
```

`temperature.test.maxon`:

```maxon
test 'boiling point converts'
	try Expect.equal(toFahrenheit(100) as AssertedInt, expected: 212)
end 'boiling point converts'

test 'zero is freezing'
	try Expect.equal(describe(0), expected: "freezing")
	try Expect.startsWith(describe(5), needle: "above")
end 'zero is freezing'

test 'body temperature'
	try Expect.equal(toFahrenheit(37) as AssertedInt, expected: 98, message: "rounds toward zero")
end 'body temperature'
```

```text
$ maxon test temperature --no-timing
temperature/temperature.test.maxon:
  ✓ boiling point converts
  ✓ zero is freezing
  ✓ body temperature

 3 pass
 0 fail

 3 tests across 1 file.
```

The integer assertions take `AssertedInt` (`int(i64.min to i64.max)`), so a value of another alias is cast
to it; float assertions take `AssertedReal`. The matchers:

| Matcher | Checks |
|---------|--------|
| `Expect.equal(actual, expected:)`, `Expect.notEqual(actual, expected:)` | integers, strings, booleans |
| `Expect.greaterThan(actual, than:)`, `lessThan`, `atLeast`, `atMost` | integers and floats |
| `Expect.close(actual, expected:, within:)` | floats within a tolerance |
| `Expect.isTrue(actual)`, `Expect.isFalse(actual)` | booleans |
| `Expect.contains(haystack, needle:)`, `startsWith`, `endsWith`, `isEmpty` | strings |
| `Expect.fail(message)` | always fails |

Every matcher also takes an optional `message:` that is printed when it fails, and reports the line of the
assertion itself. A failing assertion prints what it expected and what it received — had the last test
expected `99`, the run would report:

```text
FAIL  temperature/temperature.test.maxon > body temperature
  FAIL temperature.test.maxon:11: Expect.equal
    expected: 99
    received: 98
    message: rounds toward zero
```

The full assertion reference is on the [Testing](STDLIB_REFERENCE.md#testing) page of the standard library.

### Uncaught Errors in Tests

Inside a test body, a bare `try` may propagate **any** error type — not only `TestFailure`. An error that
reaches the end of the test fails that test and reports the error and the `try` that threw it:

```maxon
test 'a missing user throws'
	let name = try lookup(0)          // lookup throws LookupError
	try Expect.equal(name, expected: "user")
end 'a missing user throws'
```

```text
FAIL  users/lookup.test.maxon > a missing user throws
  threw LookupError.notFound
  at lookup.test.maxon:2
```

- An `otherwise` clause you write always takes precedence.
- The relaxation applies to the test body only. The same bare `try` in an ordinary function is still
  **E3059**, and a closure written inside a test is an ordinary function.
- A `panic` cannot be caught; the test is reported as crashed.

### Test Diagnostics

| Code | Cause |
|------|-------|
| E2008 | the `end` label does not repeat the test's name |
| E2058 | a `test` declaration outside a `*.test.maxon` file |
| E2059 | an empty test name |
| E3057 | an assertion (or other throwing call) without `try` |
| E3107 | two tests in one file whose names compile to the same symbol — each character outside `A–Z`, `a–z`, `0–9` and `_` becomes `_`, so `'adds two'` and `'adds-two'` collide |

### Running Tests

`maxon test [directory]` discovers every `test` under the directory, runs them and exits `0` when all pass,
`1` when any fails (or there are none), and `2` when the tests do not compile. `--list` prints the tests
without running them. A test binary's heap is checked for leaks like any program's, and a leaking test is
reported as such. See the [CLI reference](CLI_REFERENCE.md) for its flags.

---

## Namespaces

A Maxon program is every `.maxon` file in a project directory, compiled together. There are no import
statements: a file sees its own declarations, the declarations other files make visible, and the standard
library.

### Automatic Derivation

A file's namespace is its directory path relative to the project root, joined with `.`:

| File | Namespace |
|------|-----------|
| `main.maxon` | (none) |
| `utils/helpers.maxon` | `utils` |
| `lib/fmt/integer.maxon` | `lib.fmt` |

Every file in one directory shares that directory's namespace.

### Visibility

Top-level declarations — functions, types, enums, unions, typealiases, variables — are **private to their
file** unless marked. Three modifiers widen that:

| Modifier | Visible to | Checked for unused exports |
|----------|------------|----------------------------|
| *(none)* | the declaring file | — |
| `module` | files in the declaring directory and its subdirectories | yes (**E3094**) |
| `export` | every file | yes (**E3092**, **E3093**) |
| `public` | every file | no |

The same modifiers apply to members inside a type: fields, methods and static members are private to the
type unless marked, independently of the type's own visibility. At most one modifier may be written;
combining two is **E2001** (`'export' and 'public' cannot be combined`).

### `export`

```maxon
typealias Score = int(i64.min to i64.max)

export function publicAdd(a Score, b Score) returns Score
	return a + b
end 'publicAdd'

function privateHelper(x Score) returns Score     // file-private
	return x * 2
end 'privateHelper'
```

Calling a non-exported function from another file is **E3008** (`function 'privateHelper' is not
exported`).

`export` also states an expectation: **this program uses the declaration from another file.** When
nothing outside the declaring file refers to it, the compiler reports **E3092** (`exported function
'geometry.perimeter' is never referenced outside its declaring file`), and when every use is inside the
declaring directory it suggests `module` (**E3093**). These checks run on multi-file programs that
otherwise compile.

### `public`

`public` gives exactly the visibility of `export` and declares the symbol **API surface**: something that
exists for callers outside this program, so "nothing here uses it" is not a finding. Library code marks its
surface `public`; the standard library does so throughout.

```maxon
typealias Length = int(0 to 1000)

public function area(width Length, height Length) returns Length
	return width * height
end 'area'
```

`export` and `public` are reserved words. `module` is contextual: it is a modifier only directly before a
declaration and remains usable as an ordinary name.

### `module`

A `module` declaration is visible to every file in the declaring file's directory and in its subdirectories,
and to nothing outside that subtree — for helpers shared across a feature folder:

```text
project/
├── main.maxon                 # cannot call helper()
└── feature/
    ├── api.maxon              # module function helper() — declared here
    ├── routes.maxon           # can call helper()
    └── internal/
        └── cache.maxon        # can call helper()
```

Using a `module` declaration from outside its subtree is **E3088** (`function 'helper' is module-scoped
and not visible from this directory`). A `module` declaration that nothing outside its file uses is
**E3094**.

### Qualified Names

Qualify a name with its namespace to be explicit, or to choose between two declarations with the same
name:

```maxon
function main() returns ExitCode
	let a = geometry.area(3, height: 4)
	let side = 7 as geometry.Length
	print("{a} {side}\n")
	return 0
end 'main'
```

Qualification works for functions and typealiases (`lib.fmt.format(x)`, `50 as api.Score`). It never
bypasses visibility. Types are referred to by their bare name.

### Bare Names and Ambiguity

A bare name resolves when exactly one visible declaration has it. Only a declaration the referring file may
name is a candidate: a file-private function in another file and a `module` function outside the caller's
subtree never count, and the candidate list an error prints names only visible ones. A bare call or a bare
function value takes the type of the declaration it resolves to, and a function-backed enum case's function
is resolved from the file that declares the enum, whichever file reads the case. When several do:

- a declaration at the project root, or in an enclosing directory, takes precedence over one in a nested
  directory, and a project declaration takes precedence over a standard-library one;
- otherwise the reference is ambiguous. A function call is **E3095** (`Ambiguous bare-name call to 'describe':
  multiple visible definitions found. Qualify with a directory name. Candidates: alpha.describe,
  beta.describe`), worded for a function value or an enum case's backing where the name is one, and a
  typealias is **E3063** — in every alias form, including `export typealias Step = function(…) returns …`.
  Qualify the name to resolve it — a call (`api.format(...)`), a function value
  (`let f = api.format`) and a function-backed enum case (`plain = api.format`) all accept the qualified form.

Two typealiases with the same name in **one** file are **E3061**, which qualification cannot resolve.

The standard library's typealiases are usable from every file, including ones the standard library does not
export, so a value can always be cast to the alias a library signature asks for (`x as AssertedInt`).

### Multi-Project Workspaces

Several projects can share a workspace. Each is a directory, and the directory you build is the one that is
compiled:

```text
workspace/
├── project-a/
│   ├── project.maxon    # how project A is built
│   └── main.maxon
└── project-b/
    ├── project.maxon    # how project B is built
    └── main.maxon
```

See [Build System](#build-system) and [Project Structure](CLI_REFERENCE.md#project-structure) for
what a project directory contains.

**The language server checks open files.** It checks a document together with the standard library, not with the sibling files a
`maxon build` of its directory would compile. It does read the rest of the project to find out which names
those files declare, so it no longer reports an error the build does not — but it can still miss one: a
diagnostic that only the merged program raises is out of reach, and while a buffer is unsaved the
name-dependent diagnostics are withheld until it matches disk again. The build remains the authority.

---

## Concurrency

Maxon has two concurrency tools:

- **`async` / `await`** start a **coroutine** of the current green thread. Coroutines overlap *waiting* — for a
  timer, a socket, a child process — without creating threads.
- **`spawn`** starts a **service**: a green thread of its own, scheduled independently and able to run in
  parallel on another processor. You talk to it through messages.

Both run on the runtime's scheduler, which maps green threads onto a pool of OS threads. There are no
locks or atomics in user code: a value is only ever reachable from one green thread at a time, except
where a service send lends it read-only (below).

### Starting a Coroutine

`async` before a call starts the callee as a coroutine and returns a **promise**:

```maxon
typealias Score = int(0 to 1000)

function slowDouble(n Score, delay Milliseconds) returns Score
	sleep(delay)
	print("finished {n} after {delay} ms\n")
	return n * 2
end 'slowDouble'

function main() returns ExitCode
	let a = async slowDouble(10, delay: 60)
	let b = async slowDouble(20, delay: 20)
	let ra = await a
	let rb = await b
	print("sum={ra + rb}\n")
	return 0
end 'main'
```

```text
finished 20 after 20 ms
finished 10 after 60 ms
sum=60
```

Both coroutines start before either is awaited, so their sleeps overlap and the program takes about 60 ms,
not 80.

- `sleep(milliseconds)` parks the current green thread; it takes a `Milliseconds` value.
- `async` applies to a direct call of a function or a static method. It cannot start a closure, an
  indirect call or an instance method (**E2015**).
- The callee must be able to wait — call `sleep`, `await`, `Runtime.yield()`, or perform file, socket or
  process I/O, directly or through its callees. A function that never yields is **E3073** (`function never
  yields; 'async' is for I/O-concurrent work only`): there is nothing to overlap.

### Awaiting Results

`await promise` returns the coroutine's result. If it has finished, `await` returns at once; otherwise the
awaiting green thread **parks** and the scheduler runs other work until the result is ready. A coroutine
that returns nothing is awaited as a statement: `await p`.

A promise is consumed exactly once. Awaiting it twice, or using it after `.cancel()`, is **E3142**; awaiting
inside a loop a promise that was started outside the loop is **E3100**.

### Throwing Async Functions

A promise from a throwing function is awaited with `try await`, which accepts every `otherwise` form a
synchronous `try` does:

```maxon
enum FetchError implements Error
	notFound
end 'FetchError'

function lookup(key String) returns String throws FetchError
	sleep(5)
	if key == "missing" 'absent'
		throw FetchError.notFound
	end 'absent'

	return "value-of-{key}"
end 'lookup'

function main() returns ExitCode
	let good = async lookup("x")
	let bad = async lookup("missing")
	let g = try await good otherwise "none"
	let m = try await bad otherwise (e) 'failed'
		match e 'why'
			notFound then print("lookup failed: notFound\n")
		end 'why'
		return 0
	end 'failed'

	print("{g} {m}\n")
	return 1
end 'main'
```

Plain `await` on a throwing promise is **E3057**; `try await` on a promise that cannot throw is **E3055**.

### Promises in Collections and Fields

A promise can be stored when its type is named: `Promise with T` for a function returning `T`, and
`Promise with (T, E)` for one that also throws `E`. Storing a throwing promise under a type that omits the
error is **E3098**.

```maxon
typealias Tag = int(0 to 100)
typealias TagPromise = Promise with Tag
typealias TagPromiseArray = Array with TagPromise

function nap(delay Milliseconds, tag Tag) returns Tag
	sleep(delay)
	return tag
end 'nap'

function main() returns ExitCode
	var naps = TagPromiseArray.create()
	naps.push(async nap(200, tag: 1))
	naps.push(async nap(20, tag: 2))
	naps.push(async nap(100, tag: 3))

	let first = __Builtins.awaitAny(naps)
	print("first finished: index {first}\n")     // index 1

	var sum = 0
	for p in naps 'drain'
		sum = sum + await p
	end 'drain'

	print("sum of tags={sum}\n")                  // 6
	return 0
end 'main'
```

**Waiting for the first of several.** `__Builtins.awaitAny(array)` parks until any promise in the array has
finished and returns its index. It consumes nothing: every promise, the winner included, is still awaited
(or dropped) afterwards. An already-finished promise is returned without parking.

**Reading a promise out of storage.** A promise has one owner, so reading one out of the array or the struct
field that holds it starts a MOVE out of that slot: consuming the read empties the slot, and a read never
consumed stays the container's and is dropped with it. `get(i)`, `first()`, `for p in array`, an array
iterator's `current()` and `peek(n)`, a destructured `for (iter, p) in array.withIterator()`, and a read of a
promise-typed field all name their slot. A read that cannot name one is **E3141**: `last()`, a list's elements,
a `Map`'s values, and a `withIterator()` pair bound whole. `pop` and `remove` move the promise out, and the
caller owns it outright. Reading a promise out of a temporary — `try make().get(0)` — keeps that temporary
alive to the end of the enclosing scope, so the promise outlives the expression it came from.

**Consuming a promise.** `await`, `.cancel()`, a `push` or `set` into another container, a store into a field
or a union case, and passing it to a call by value all consume it: each empties the slot the promise was read
out of, and hands the thread either back to the runtime or to its new owner. A store into a promise field also
releases the thread that field was holding, exactly once. Three refusals follow:

- **E3141** — the slot a read came out of has already been spent, at any of those doors. Two reads of one slot
  hold one promise, so only one of them may be consumed; where the compiler cannot see that both name one slot,
  the program aborts at run time with exit code **118** instead.
- **E3102** — a promise already moved into storage is used again afterwards.
- **E2015** — a promise read outside a loop is given away inside it, or inside a `while` condition. Read it
  inside the loop instead.

### Cancellation and Dropped Promises

`promise.cancel()` consumes a promise without waiting for it. A promise that is never awaited is dropped
when it goes out of scope (or is overwritten), which has the same effect.

- A coroutine that has **not started** never runs.
- A coroutine that has **already started** is not interrupted where it stands: it resumes and runs on, and
  its result is discarded. What it may no longer do is **begin** an operation that waits on a far end. Its
  next `TcpClient.connect`, `TcpListener.accept`, `send`, `sendFrom`, `recv`, or `TcpListener.bind` to a
  host **name** throws that operation's own [`NetworkError`](STDLIB_REFERENCE.md#tcpclient) variant at once
  instead of parking for a wake-up nothing will send. `close()` still completes, and so does a `bind` to a
  numeric address.
- A promise held in an array or a field is dropped with its container.

### Yielding

`Runtime.yield()` gives other runnable work a turn and then continues. It never blocks and uses no timer
(unlike `sleep(0)`); when nothing else is runnable it returns promptly. Use it in a loop that polls for a
condition; use `await` or `sleep` to actually wait.

### Services — `spawn`

A **service** is an ordinary `type` started with `spawn`. `spawn Type.factory(args)` calls a static
factory of the type and runs the result on a new green thread, returning a **handle**. The handle's methods
are exactly the type's `export` and `public` instance methods; each call on the handle is a **message** the
service handles one at a time, in order.

```maxon
typealias Count = int(0 to 1000000)

type Calc
	var count as Count

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Count)
		self.count = self.count + by
	end 'bump'

	export function total() returns Count
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.bump(3)                                       // fire-and-forget message
	h.bump(4)
	let n = try await h.total() otherwise 0         // a message with a reply
	print("total={n}\n")                            // total=7

	h.shutdown()
	let after = try await h.total() otherwise (e) 'stopped'
		match e 'why'
			stopped then print("service stopped\n")
		end 'why'
		return 0
	end 'stopped'

	print("unexpected {after}\n")
	return 1
end 'main'
```

- **The same type is still a plain value.** `Calc.create()` without `spawn` is an ordinary record with
  ordinary calls, so a service's logic is unit-testable without threads.
- **Messages.** A method that returns nothing and throws nothing is sent and forgotten. A method that returns
  a value, or throws, is awaited: `try await h.method(…)`. The reply can always fail with
  `ServiceError.stopped`, so plain `await` is **E3057**; the method's own error type merges with it in the
  handler's `match`.
- **Private methods are not messages.** Calling a non-exported method or a static through a handle is
  **E3136**. A service cannot send to itself.
- **Shutdown.** `h.shutdown()` stops the service after the messages already queued. Dropping the last
  handle does the same. Replies requested afterwards fail with `ServiceError.stopped`.
- **The target** of `spawn` must be a static factory that returns its own type; anything else — including a
  bare `spawn f()` — is **E3134**.
- **Generic services.** A generic type can be spawned; its handle type is spelled through an alias,
  `typealias StringBoxHandle = Box.handle with String`.
- Services that `await` each other in a cycle are **E3139**.
- `spawn` is a contextual keyword; it remains usable as a name.

**What crosses a message.** A value sent to a service must not stay reachable from the sender in a way
either side could write, because the two green threads may run at the same time on different processors:

- A `var`, a temporary or a literal argument is **moved** into the service; reading the sender's variable
  afterwards is **E3102**. Factory arguments and replies are moved too.
- A `let` argument that owns its value outright is **lent**: the sender keeps reading it, and from the send
  onwards neither side may store it anywhere writable, return it, capture it or pass it to anything that
  writes it (**E3160**). A handler that writes its parameter's graph at any depth — a method that writes its
  own receiver, called on a record within the parameter, included — refuses every send that lends to it
  (**E3019**).
- A value the sender does not solely own — captured by a closure, held in a container, borrowed from a
  parameter — is **E3138**; send a `.clone()`.
- A parameter type that cannot cross at all — a promise, a function value, an opaque type parameter — is
  **E3135**. A reply that is part of the service's own state is **E3137**; return a copy. For a generic
  service the reply is judged at the `spawn` that fixes `T`, and a `returns T` message that hands back the
  state is **E3137** there whenever `T` resolves to a managed type; a scalar `T` crosses.
- A value held at an interface type crosses as a message argument, in a service's state and as a reply,
  moved or lent like any other value. A conformer sent at its own type whose graph the runtime cannot walk
  (an OS handle) is **E3138**; once it is held at the interface type it is checked through its witness at
  the send, and such a conformer aborts with exit code **96**.
- Before a send, the runtime also checks the value's whole object graph. The graph may reach one record
  several times — two fields, two slots of an array — when every owner of that record is one of those
  references, and the record is walked once however many paths reach it. If some nested record has an owner
  outside the graph, the program aborts with exit code **96** before anything is sent. A reply is checked
  after the handler's locals and the message's arguments are released, so a reply built from them crosses. A
  generic service's reply is checked at the type its `spawn` fixes: a `returns T` message that hands back a
  container or a reference-holding record from the service's own state is **E3137** at the `spawn`, and a
  reply whose graph holds a type the runtime cannot walk (an OS handle) is **E3138** there. A generic service
  cannot be spawned over a value held at an interface type (**E2015**).

**Module-level state.** A service handler — and anything it calls — may not read or write a module-level
`var` (**E3143**). Keep a service's state in its own fields and hand results back through replies.

A module-level `let` stays readable. In a program that spawns a service, every `let` record built before
`main` is marked shared once the last global initializer has returned, so every count a handler steps on it
is atomic; a record two `let`s reach counts both as its owners. A `let` whose graph no walk can mark — one
holding an OS handle, a value held at an interface type, or a generic instance with no base layout — is
**E3163** where a message can read it, and legal where only `main` does. A `spawn` a global initializer can
reach is **E3164**, a `let` whose initializer reaches a module-level `var` holding a record is **E3165**, and
a `var` whose initializer may take a `let`'s record is **E3166** (see
[Top-Level Variables](#top-level-variables)).

**Output order.** Text printed by `main` and by a service handler may interleave in any order; sequence it
through awaited replies when order matters.

### I/O and Waiting

Operations that wait **park** their green thread instead of blocking an OS thread: `sleep`, `await`, socket
operations (`TcpClient.connect`, `send`, `recv`, `TcpListener.accept`) and waiting on a child process. A
parked green thread holds no processor, so a server waiting in `accept()` costs nothing while idle.

File and directory operations yield to other work before each call into the operating system and are
started with `async` like any other waiting function (`try await` for the throwing ones such as
`File.readText`). The [standard library reference](STDLIB_REFERENCE.md) documents the file, network and process
APIs.

### The Scheduler

- **Green threads.** Every green thread starts on a small stack that grows on demand, so thousands are
  cheap. A stack stops growing at 1 GiB; a recursion past it stops the program with `panic: stack overflow`.
- **Parallelism.** By default the scheduler creates one processor per logical CPU. The environment
  variable `MAXON_MAX_PROCS=N` sets the count, clamped to between 1 and the CPU count; a value that is not a
  positive number leaves the default. `Runtime.processorCount()` answers the resolved count. Services use
  the processors in parallel; an `async`-only program's coroutines stay on the green thread that started
  them.
- **Preemption.** A green thread that has run for 10 ms is stopped at its next function entry and moved
  behind the other runnable work, so a CPU-bound loop cannot starve the rest of the program.
  `MAXON_PREEMPT=off` disables preemption; `on`, or unset, is the default, and any other value makes the
  program exit at start-up with code 116.
- **Coroutines switch only where they wait**: at `await`, `sleep`, `Runtime.yield()` and I/O.

The [CLI reference](CLI_REFERENCE.md) lists the environment variables a compiled program reads.

### Exit Codes

| Exit code | Cause |
|-----------|-------|
| 75 | a green thread was neither awaited nor dropped when the program ended |
| 92 | deadlock: `main` has not finished and nothing can ever run again (for example `awaitAny` on an empty array) |
| 96 | a service send found a value with a second owner |
| 116 | `MAXON_PREEMPT` holds a value other than `on` or `off` |
| 120 | a deep copy reached an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled |

### Targets

Green threads, `async`, `sleep`, `awaitAny` and services run on every native target. `wasm32-wasi` has no
green threads, so each of them is **E3104** there, reported at the call.

---

## Build System

A Maxon project is a directory of `.maxon` files — there is nothing to declare. `maxon build <directory>`
compiles every source file beneath it. A directory that wants to say *how* it is built puts a
**`project.maxon`** beside its sources, and `maxon build` with no path runs it.

```text
myproject/
├── project.maxon        # the build manifest, if the project needs one
├── tasks.maxon          # the tasks `maxon run` offers, if it has any
├── main.maxon           # entry point
├── lib.maxon
├── lib.test.maxon       # tests: compiled only by maxon test
└── utils/
    └── math.maxon       # subdirectories are included
```

A directory walk skips three things:

- **`project.maxon` and `tasks.maxon` at the directory you named** — one describes the build and the
  other holds the tasks `maxon run` offers, and neither is part of the program being built. Deeper in
  the tree a file of either name is ordinary source.
- **`*.test.maxon`** — test files; [`maxon test`](#testing) compiles them.
- **any directory containing a `.maxonignore` file**, with everything beneath it.

A `.maxonignore` excludes a directory the walk *discovers*. Naming a path explicitly on the command
line — a file or a directory — compiles it regardless of a marker above it or on it. See
[Project Structure](CLI_REFERENCE.md#project-structure) for the full layout rules.

### A Manifest Is a Program

`project.maxon` is ordinary Maxon with the whole standard library available. The compiler does not parse it
as configuration: it compiles it for the host, runs it, and performs the build it describes. A build can
therefore **compute** what it compiles — list a directory, choose sources by host, derive a version from
git — instead of only spelling it out.

Its entry point is a function named **`build`**, not `main`. It returns `ExitCode`; a non-zero return or a
crash fails the build before anything is compiled.

```maxon
function build() returns ExitCode
	Build.build("src", output: "out/hello")
	return 0
end 'build'
```

The output path omits the extension: the compiler adds the target's (`.exe` on Windows, `.wasm` for
`wasm32-wasi`, none on Linux and macOS). Relative paths resolve against the manifest's directory.

### Describing the Build

`Build` (in the standard library) writes the build description the compiler reads back:

| Call | Meaning |
|------|---------|
| `Build.build(source, output:, debugInfo: true, version: "", defines:)` | compile one file or directory to one output |
| `Build.target(name, source:, output:, debugInfo: true, version: "", defines:)` | describe one named target, returning a `BuildConfig` |
| `Build.buildTargets(targets)` | declare several named targets (a `BuildConfigArray`) |
| `Build.buildWithConfig(config)` | build one `BuildConfig`, whose `sources` may list several files and directories compiled as one program, in order |
| `Build.delegate(name, directory:, target: "")` | hand the whole description to another directory's `project.maxon`, run there |
| `Build.delegateTarget(name, directory:, target: "")` | the same as one named target, returning a `BuildConfig` |

- `debugInfo` controls the debug-information sidecar written beside the executable.
- `version` stamps a dotted version into the executable's metadata.
- `defines` is a `StringArray` of `"name=value"` entries, each replacing the written default of a top-level
  `String` constant — the same as `maxon build --define`. This is how a manifest passes a value it computed,
  such as a version derived from git, into the program.

**Several targets** are listed rather than guessed at:

```maxon
function build() returns ExitCode
	var targets = BuildConfigArray.create()
	targets.push(Build.target("app", source: "app", output: "out/app", version: "1.2.3"))
	targets.push(Build.target("tool", source: "tool", output: "out/tool", debugInfo: false))
	Build.buildTargets(targets)
	return 0
end 'build'
```

`maxon build` with no argument then prints the target names and compiles nothing; `maxon build app`
builds one.

**Several sources in one program** use a `BuildConfig`:

```maxon
function build() returns ExitCode
	var sources = StringArray.create()
	sources.push("src")
	sources.push("vendor/thirdparty")

	let config = BuildConfig.create("myprogram", output: "out/myprogram", sources: sources, debug_info: true)
	Build.buildWithConfig(config)
	return 0
end 'build'
```

Sources are compiled in exactly the order listed. An empty `sources` list is refused (`project.maxon named
no sources to compile`) rather than read as "everything here".

### The Command Line Wins

Flags typed on the command line outrank the manifest: `-o` replaces the output path, `--target` chooses
the target (the manifest itself always runs on the host), a `--define` is applied after the manifest's
defines, and debug information is written only if both the manifest and the command line allow it.

The [CLI reference](CLI_REFERENCE.md) documents `maxon build` and its flags.

### Tasks

A directory may also hold a **`tasks.maxon`**, and `maxon run <task>` runs one of its exported
no-parameter `ExitCode` functions with the caller's own streams and exit code. It is the same language
and the same standard library as a manifest, and the two files divide one job in two:

```maxon
// tasks.maxon
export function build() returns ExitCode
	Build.delegate("compiler", directory: "compiler")
	return 0
end 'build'

export function fmt() returns ExitCode
	return 0
end 'fmt'
```

**`tasks.maxon` marks nothing.** A directory holding one is not thereby a project: what says where a
project begins is a `project.maxon`. A task may describe a build, as `build` does above, and the
compiler performs it once the task exits 0.

---

## Memory Model

Maxon manages memory with **reference counting** decided at compile time. There is no garbage collector and
no manual `free`: a record is released when its last reference goes away, and the compiler proves at compile
time that no reference is used after that.

### Values and Records

- **Scalars** — integers, floats, `bool`, payload-free enum values, and an
  [inline packed record](#inline-packed-records) — are plain values. Assigning one copies it.
- **Records** — values of a `type`, tuples, unions with payloads, strings, arrays and other collections —
  live on the heap. A variable holds a **reference** to its record. A two-element tuple of register-wide
  elements returned from a function is the one exception: it comes back in the two return registers and no
  record is built (see [Memory Semantics](#memory-semantics) under Tuples).

### Reference-by-Default Assignment

Storing a record somewhere else shares it rather than copying it, so a field write through one holder is
visible through every other:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var a = Point.create(1, y: 2)
	var points = PointArray.create()
	points.push(a)                                        // the array shares a's record
	var b = try points.get(0) otherwise panic("pushed")
	b.x = 99
	print("{a.x}\n")                                      // 99: a and b are one record

	b = Point.create(5, y: 6)                             // rebinding b leaves a alone
	a = Point.create(7, y: 8)
	points.push(a)
	print("{points.count()}\n")                           // 2
	return 0
end 'main'
```

Reassigning a variable rebinds it to another record without touching the old one. Use
[`clone()`](#explicit-cloning) when you need an independent copy.

### Immutable XOR Mutable

A record is either **written through mutable holders** or **read through immutable names** — never both at
once. The compiler enforces this, which is what makes sharing by reference safe.

**Immutable names** are `let` bindings (including `for` loop variables and `if let` bindings), module-level
`let` constants, and parameters the function does not write. **Mutable holders** are `var` bindings, fields,
container elements and parameters the function writes. Several holders may share a record: storing it into a
field or container, or passing it to a callee that keeps it, adds a reference.

The checks are by **liveness**: an immutable name matters only while some later statement still reads it.

| Situation | Result |
|-----------|--------|
| `var b = a` where `a` is a `let` that is its record's only reference | `a` **moves** into `b`; reading `a` afterwards is **E3102** |
| `var b = a` where the record is also named by another live `let`, or `a` is a field of an immutable name (`var i = o.inner`) | **E3078** |
| a `var` bound to a value that may be a live `let`'s record — a call result, a container element — and then written | **E3078** |
| a field write, or a method that writes its receiver, on a record a live `let` may still read | **E3159** |
| a `let`, or a value that may be a live `let`'s record, passed to a parameter the callee writes | **E3019** |
| mutating a collection while a `let` borrowed from it is live | **E3070** (see [Borrow Checking](#borrow-checking)) |

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	var arr = PointArray.create()
	arr.push(a)                     // arr co-owns a's record
	var b = a                       // a moves into b
	b.x = 5
	// print("{a.x}\n")             // E3102: use of moved value 'a'

	let first = try arr.get(0) otherwise panic("pushed")
	// var e = try arr.get(0) otherwise panic("pushed")
	// e.x = 9                      // E3078: 'get' may share storage with immutable variable 'first'
	var e = (try arr.get(0) otherwise panic("pushed")).clone()
	e.x = 9                         // fine: an independent record
	print("{first.x} {b.x} {e.x}\n")   // 5 5 9
	return 0
end 'main'
```

Some consequences:

- A `var` that is only rebound and read — walking a chain with `cur = try cur.next()` — writes nothing and is
  never refused.
- A method writing a field of its **own** receiver is not a parameter write: `let acc = Accumulator.create()`
  then `acc.add(10)` is legal.
- A record whose type declares every field `let` can never be written, so none of these checks apply to it.
- `String` and `Character` values a name does not own are copied when bound to a `var` or stored.
- A `let` lent to a [service](#services--spawn) is frozen from the send onwards
  (**E3160**).
- The analysis tracks records made and used within one function. Two fields of a record received from
  elsewhere — a parameter, module storage — are assumed to hold different records.

### Explicit Cloning

`clone()` returns an independent deep copy:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	export static function create(x Coord) returns Point
		return Point{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1)
	var b = a.clone()
	b.x = 99
	print("{a.x} {b.x} {a is b}\n")    // 1 99 false
	return 0
end 'main'
```

`clone()` comes from the `Cloneable` interface (`function clone() returns Self`). The compiler generates it
for any type whose fields are all cloneable; primitives, `String`, and collections of cloneable elements are
cloneable. Declare `clone()` yourself for custom behaviour, or when a field's type is not cloneable.

A field declared at an [interface](#interfaces) type is cloneable when **every** conformer of that interface
in the program is — the copy runs the conformer the value actually holds, which is not known until the
program runs, so the whole program's conformers are what the compiler checks. A conformer that owns an OS
handle, or a generic type, makes the field uncloneable and the `.clone()` is refused with **E2015**, naming
the field, the interface and the conformer. A container ELEMENT held at an interface type is a separate
matter and is never cloneable: an element slot is one machine word and a value at an interface type is two.

A type that both conforms to an interface and holds a value at that interface type cannot be declared at
all — it is a reference cycle, reported as **E4014** (see the ownership rules in `specs/ownership.md`), so no
decorator of that shape reaches the clone check.

### Borrow Checking

A `let` bound to an element read out of a mutable collection **borrows** from that collection. While the
borrow is live, the collection cannot be mutated — directly or through a function that mutates it:

```maxon
function main() returns ExitCode
	var arr = ["hello"]
	let s = try arr.get(0) otherwise ""
	// arr.push("world")      // E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
	print("{s}\n")            // the borrow ends at its last use...
	arr.push("world")         // ...so this is fine
	print("{arr.count()}\n")
	return 0
end 'main'
```

Borrows end at the borrowing variable's **last use**, not at the end of its scope.

### Stack Promotion

A record that never escapes its function — never returned, stored in a field or container, captured by a
closure, or passed to a callee that keeps it — and whose fields are all scalars is allocated on the stack
instead of the heap, with no reference counting. This is automatic and does not change behaviour.
`@heap let p = Point.create(1)` forces heap allocation for one declaration.

### Scope Cleanup

When a reference goes out of scope the record's count drops, and a record whose count reaches zero is freed
along with everything it holds — a container releases each element, a record each field. A returned record
is handed to the caller rather than freed.

```maxon
typealias TokenId = int(0 to 1000)

type Token
	export let id as TokenId

	export static function create(id TokenId) returns Token
		return Token{id: id}
	end 'create'
end 'Token'

typealias TokenList = List with Token

function countTokens() returns TokenId
	var list = TokenList.create()
	list.append(Token.create(1))
	list.append(Token.create(2))
	return list.count()             // list, then both tokens, are freed on return
end 'countTokens'
```

A type cannot contain itself (**E4014**), so reference cycles between records cannot be built and reference
counting reclaims everything.

### Returning Memory to the Operating System

Freeing a record returns its slot to the allocator, not to the operating system. The allocator hands the
slot back to the size class it came from, so the next record of that size reuses it — but a program's
resident set otherwise stays at its high-water mark for the life of the process.

`__Builtins.scavengeMemory()` is the one call that changes that. It returns the number of bytes whose
physical backing was handed back, and it does two things no other road does:

- **Chunks go back to the page layer, where any size class can take them.** A size class the program has
  finished with is holding memory nothing else can use until this call releases it.
- **Whole 64 KiB granules that nothing is using are decommitted.**

```maxon
// after a burst has been allocated and released
_ = __Builtins.scavengeMemory()            // chunks return to the page layer; nothing is decommitted yet
let released = __Builtins.scavengeMemory() // granules still free are decommitted; `released` is the bytes
```

⚠ **The first call after a population is dropped returns 0.** A granule is released only if it was
already entirely free when the previous call looked at it, so a program that reuses its memory between two
calls never pays a decommit and a re-commit for having been busy. **A caller that wants memory back after a
burst therefore calls it twice.**

The address space is not returned and the reservation is not shrunk — only the physical backing goes. What
the operating system then reports as the process's resident set is its own answer, which is why the figure
here is bytes the allocator asked it to drop rather than bytes observed to leave.

The five `__Builtins.slab*Bytes()` readings report what the heap holds at any moment, including how much of
it is the backlog this call would return.

### Attributing the Live Heap

The five readings say how much is out; three more say what it is. `__Builtins.slabCensusTally(mode)` walks
every live slot once and fills a table of 2048 buckets, which `__Builtins.slabCensusBucketCount(i)` and
`__Builtins.slabCensusBucketBytes(i)` read back one bucket at a time.

| `mode` | Bucket `i` holds |
|--------|------------------|
| `0` | the slots whose allocation tag is `i` — available only in a program built with `--debugstream`, which is what puts a tag on a box |
| `1` | the slots of size class `i` |

The tally returns the live bytes *it* walked, which is an independent cross-check: the buckets' sum travels
through the table and the return travels through nothing, so the two figures beside `slabLiveBytes()` tell a
table fault from a walk fault. Bucket 2046 is the slots whose first word is not a tag — a string's bytes, an
element buffer, a green thread's stack — and bucket 2047 is a tag past the end of the table. A `mode` that is
neither `0` nor `1`, or a heap the walk finds inconsistent, ends the program with exit code **119** rather
than answering.

The compiler's own build uses these through
[`--census-by-tag`](CLI_REFERENCE.md#logging).

### Attributing Allocation Churn

A census says what is still held; it cannot see an allocation that was freed before it looked.
`__Builtins.mmAllocTotalByTag(i)` and `__Builtins.mmAllocBytesByTag(i)` answer the other question — how
many allocations tag `i` has made since the process started, and how many bytes they asked for. Nothing
subtracts a free, so both figures only rise and a difference of two readings is the churn between them.

The table is 2048 buckets, like the census's. Bucket 0 is the allocations the compiler had no declared
type to tag, and bucket 2047 collects every tag past the end of the table. An index outside `0` to `2047`
answers 0.

Both are maintained only in a program built with `--debugstream`, which is what puts a tag on a box; in
any other build they answer 0. Summed over every bucket they equal `__Builtins.mmAllocTotal()`, which is
the cross-check to reach for. The compiler's own build uses them through
[`--allocations-by-tag`](CLI_REFERENCE.md#logging).

### The Leak Checker

Every program that uses the heap checks, when `main` returns, that every allocation was released. If any
was not, the program's exit code becomes **101** (nothing else is printed). Reference counting is managed by
the compiler, so a leak is not something ordinary code is expected to cause; `maxon test` reports a leaking
test as leaked. A green thread that was never
awaited or dropped exits with **75** instead (see [Concurrency](#exit-codes)).

### Safety Summary

| Hazard | What Maxon does |
|--------|-----------------|
| Out-of-range collection index | `get`/`set` throw `ArrayError.indexOutOfBounds`; there is no unchecked access |
| Value outside a typealias's range | compile error for a known value, **panic** at run time otherwise |
| Division by zero | compile error for a constant zero; otherwise the division throws `DivisionByZero` |
| Negative shift count | compile error for a known count, **panic** at run time otherwise |
| Integer overflow | wraps (two's complement); a range check catches it where the value reaches a checked place |
| Null | there is no null: every variable, field and element holds a value |
| Use after move, or writing a record an immutable name reads | compile error |
| Mutating a collection while an element is borrowed | compile error (**E3070**) |
| Use after free, double free | prevented by compile-time reference counting |

---

## Code Generation

### Native Backend

- Maxon has its own code generator: no LLVM, no external assembler and no linker step.
- It writes each target's executable format directly, producing a standalone executable.
- The supported targets are listed under `--target` in the [CLI reference](CLI_REFERENCE.md).

### Optimizations

The compiler's optimization passes include:

- constant folding and simplification of constant operands
- inlining of small functions and of functions called once
- common subexpression elimination and loop-invariant code motion
- loop unswitching and jump threading
- value-range analysis, which removes range and bounds checks that provably cannot fail
- strength reduction of division and `mod` by constants
- stack promotion of records that do not escape
- dead function and dead global elimination
- hot/cold code layout and jump tables for `match`
- register allocation

Optimizations never change a program's behaviour, including its panics and range checks.

### Runtime Library

- The runtime — memory management, the scheduler, string and arithmetic helpers — is written in Maxon and
  compiled into each program.
- There is no C runtime dependency and nothing to ship beside the executable.

---

## Common Patterns

### Program Template

```maxon
function main() returns ExitCode
	print("Hello, world!\n")
	return 0
end 'main'
```

### Loop With an Early Exit

```maxon
function main() returns ExitCode
	var i = 0
	while true 'forever'
		if i >= 3 'done'
			break
		end 'done'

		print("{i}\n")
		i = i + 1
	end 'forever'

	return 0
end 'main'
```

### Iterating With an Index

```maxon
function main() returns ExitCode
	let names = ["ada", "alan", "grace"]
	for (iter, name) in names.withIterator() 'each'
		print("{iter.index()}: {name}\n")
	end 'each'

	return 0
end 'main'
```

### Recursion

```maxon
typealias Operand = int(i64.min to i64.max)

function factorial(n Operand) returns Operand
	if n <= 1 'base'
		return 1
	end 'base'

	return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
	print("{factorial(5)}\n")     // 120
	return 0
end 'main'
```

### Building a String

`String` has no `+`; interpolate, or `append` in place:

```maxon
function main() returns ExitCode
	var csv = ""
	for n in 1 to 3 'each'
		csv.append("{n},")
	end 'each'

	print("{csv}\n")     // 1,2,3,
	return 0
end 'main'
```

### A Lookup With a Fallback

```maxon
typealias Age = int(0 to 150)
typealias Ages = Map with (String, Age)

function main() returns ExitCode
	var ages = Ages.create()
	ages.upsert("ada", value: 36)
	let known = try ages.get("ada") otherwise 0
	let unknown = try ages.get("bob") otherwise 0
	print("{known} {unknown}\n")     // 36 0
	return 0
end 'main'
```

### A Factory That Validates

```maxon
typealias Percent = int(0 to 100)

enum PercentError implements Error
	outOfRange
end 'PercentError'

type Progress
	export let done as Percent

	export static function create(done Percent) returns Self
		return Self{done: done}
	end 'create'

	export static function parse(text String) returns Self throws PercentError
		let value = try int.fromString(text) otherwise throw PercentError.outOfRange
		if value < 0 or value > 100 'range'
			throw PercentError.outOfRange
		end 'range'

		return Self{done: value as Percent}
	end 'parse'
end 'Progress'

function main() returns ExitCode
	let p = try Progress.parse("42") otherwise Progress.create(0)
	print("{p.done}%\n")     // 42%
	return 0
end 'main'
```

---

## Common Errors

### Compile-Time Errors

Each example below is refused with the diagnostic shown.

**Type mismatch**

```maxon
let x = 5 + "string"          // E2004: Cannot operate on int and String
```

**Missing return**

```maxon
function compute() returns Tally
	print("working\n")
end 'compute'                 // E3013: missing return statement: 'compute'
```

**Assigning to a `let`**

```maxon
let x = 5
x = 10                        // E2013: cannot assign to immutable variable: 'x'
```

**Self-assignment**

```maxon
x = x                         // E3067: self-assignment has no effect: 'x = x'
```

**A `var` that never changes**

```maxon
var x = 10
return x                      // E3077: variable 'x' is never reassigned; use 'let' instead of 'var'
```

**An unused variable**

```maxon
let unused = 3                // E3012: unused variable: 'unused'
```

**Discarding a result**

```maxon
double(5)                     // E3064: result of pure function 'double' must be used
incrementAndGet()             // E3065: result of 'incrementAndGet' is not used (use '_ = expr' to discard)
_ = 42                        // E3067: expected a function call
```

**A missing `try`**

```maxon
parseDigit("7")               // E3057: throwing function requires try: 'parseDigit'
```

**Mutating through an immutable name**

```maxon
let items = TallyArray.create()
items.push(1)                 // E3019: cannot pass 'items' to function that mutates parameter 'self'
```

**Moving out of a `let`**

```maxon
let a = Point.create(1)
var b = a
b.x = 2
print("{a.x}\n")              // E3102: use of moved value 'a'
```

**Sharing a record between a `var` and a live `let`**

```maxon
let a = Point.create(1)
let b = a
var c = a                     // E3078: cannot assign immutable variable 'a' to mutable binding 'c'; use 'let' instead of 'var', or use clone()
```

**Mutating a borrowed collection**

```maxon
var arr = ["hello"]
let s = try arr.get(0) otherwise ""
arr.push("world")             // E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
print("{s}\n")
```

**A closure escaping its frame**

```maxon
function makeAdder(bump Score) returns UnaryOp
	let f = function(n Score) gives n + bump
	return f                  // E3099: cannot return a closure that captures
end 'makeAdder'
```

**A bare primitive type**

```maxon
function half(n int) returns int     // E3005: Cannot use bare 'int' as a type. Define a typealias with range constraints
```

**Mixing typealiases**

```maxon
let bad = score + meters      // E3005: operator '+' requires both operands to be the same type: 'Score' and 'Meters' are different typealiases — cast one side with 'as'
```

**A mismatched block label**

```maxon
match x 'check'
	1 then print("one\n")
	default then print("other\n")
end 'wrong'                   // E2043: block identifier mismatch: expected 'check', got 'wrong'
```

**An empty block**

```maxon
if x > 0 'check'
end 'check'                   // E3082: empty block: 'check'
```

A block holding only a comment is empty too.

**A non-exhaustive match**

```maxon
match level 'filter'
	error then print("error!\n")
end 'filter'                  // E2026: match on enum 'Level' is not exhaustive, missing: trace, info
```

**A redundant loop label**

```maxon
while i < 3 'loop'
	break 'loop'              // E2048: 'break' with label 'loop' targets its own loop
end 'loop'
```

### Run-Time Behavior

Nothing in Maxon is undefined behaviour. At run time:

| Event | Behavior |
|-------|----------|
| `panic("…")`, a failed range check, a negative shift count | the program prints `panic at <file>:<line>: <message>` and a stack trace to stderr and exits with code **1** |
| an index past the end of a collection | `get`/`set` throw `ArrayError.indexOutOfBounds`, handled with `try` |
| division or `mod` by zero | throws `DivisionByZero`, handled with `try` |
| integer overflow | wraps around (two's complement), with no error |
| an allocation never released | exit code **101** |
| a green thread neither awaited nor dropped | exit code **75** |
| a promise consumed through a second read of one container slot or struct field | exit code **118** |
| `__Builtins.slabCensusTally` asked for a mode it does not implement, or walking a heap it cannot describe | exit code **119** |
| a deep copy of an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled | exit code **120** |
| deadlock | exit code **92** |

`maxon execute` and `maxon test` report these exit codes; see the [CLI reference](CLI_REFERENCE.md).

---

## Best Practices for AI Agents

These rules cover the mistakes code generators make most often when writing Maxon.

1. **Label every block and repeat the label on `end`.** `if x > 0 'positive'` … `end 'positive'`. Choose
   labels that say what the block does.

2. **Never use bare `int` or `float` in a declaration.** Declare a typealias named for the purpose and use
   it for parameters, returns, fields and type arguments:

   ```maxon
   typealias Tally = int(0 to u64.max)
   typealias TallyArray = Array with Tally
   ```

3. **Pass the first argument positionally and name the rest.** `connect("localhost", port: 8080)`. Naming
   the first argument is an error.

4. **Construct records through a static factory.** `Point{x: 1}` is legal only inside `Point`'s own body;
   elsewhere call `Point.create(1)`.

5. **Prefer `let`.** A `var` that is never reassigned or mutated is an error. Every declared name must be
   used; write `_` for one you do not need.

6. **Handle every throwing call.** Write `try call() otherwise <fallback>`, `otherwise panic("why")` when
   failure is impossible, or a bare `try` inside a function that `throws` the same error type.

7. **Access collections through methods, with `try`.** There is no `items[i]`:

   ```maxon
   let value = try items.get(index) otherwise 0
   ```

8. **Guard divisions.** Use `try (a / b) otherwise …`, or give the divisor a range that excludes zero.

9. **Build strings with interpolation.** `"{name}: {count}"`; there is no `+` on `String`.

10. **Match exhaustively.** Name every enum or union case; put several on one arm with `or`, one per line;
    use `default panic("…")` or `default throws` instead of a plain `default`.

11. **Cast between typealiases explicitly.** Two different aliases never mix; write `value as Target`.

12. **Use `clone()` for an independent copy.** Assigning a record shares it.

13. **Keep tests in `*.test.maxon` files** and call every assertion with `try`:

    ```maxon
    test 'adds two numbers'
    	try Expect.equal(2 + 2, expected: 4)
    end 'adds two numbers'
    ```

---

## The Runtime Tier

This section is for contributors to the compiler and runtime; ordinary programs never meet these rules.

`runtime/` sits at the checkout root beside `stdlib/`, and the compiler loads both on every compile. Its
`.maxon` files are a privileged source tier: they parse, type-check and lower like any other file, but the
runtime compiled into every program is written there, in Maxon, rather than built as IR inside the compiler.
Two rules that hold everywhere else are lifted for these files, and two that hold nowhere else are imposed on
them.

**The two directories travel together.** The compiler finds `stdlib/` by walking up from its own executable
and reaches `runtime/` as its sibling, so a compiler with only one of them beside it cannot compile anything.
Release archives, the install scripts, the Docker image and the Homebrew formula all ship both.

### What a Runtime File May Do

| Privilege | Why |
|-----------|-----|
| Declare a `__`-prefixed name — the **E2051** reservation that keeps other code out of that space is lifted | the prefix is the runtime's own name space |
| Declare `maxon_force_segfault` — the one runtime entry without the prefix, because a backtrace prints it; E2051 reserves the name everywhere else | a second free function of that name would rename the runtime's symbol |
| Call a `__Raw.*` intrinsic — the closed table of raw machine and OS operations | a runtime is written on that floor |

A `__Raw` call from outside `runtime/` is **E3152**; the privilege belongs to the tier, so no wrapper or
re-export carries it out. A runtime entry is unreachable from outside the tier: calling one is **E3004**, and
naming one as a function value (`let f = __parallel_boundary`) is **E3155**.

### What a Runtime File May Not Do

| Restriction | Code |
|-------------|------|
| Use a managed value — in a type it spells (a field, return, parameter or cast target) or in a binding whose type is inferred — because reference counting calls into the runtime this tier defines | **E3153** |
| Declare anything wider than `module` — `runtime/` is linked into every program, so an `export` or `public` name would collide with the programs it is part of | **E3154** |
