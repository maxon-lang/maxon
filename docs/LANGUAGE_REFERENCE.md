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

On Linux and macOS a program started with its standard input, output or error closed finds that stream open
on `/dev/null`: reads see end of input, writes are discarded, and a child that inherits the stream gets the
same. When the stream cannot be opened there, the program writes `fatal error: cannot open standard fds` to
standard error and exits with code 2 before `main` runs.

### Files and Projects

- A program is one `.maxon` file or a directory of them, compiled together. There are no `import`
  statements.
- A file contains top-level declarations: functions, types, enums, unions, interfaces, extensions,
  typealiases, variables and — in `*.maxtest` files — tests. There are no top-level statements.
- A file's namespace comes from its directory (see [Namespaces](#namespaces)).
- Declarations are private to their file unless marked `export`, `public` or `module`.
- Declaration order does not matter: a function may call one declared later or in another file.
- The standard library is available to every file. A program that is one file also compiles your
  scriptlib packages ([The scriptlib directory](CLI_REFERENCE.md#the-scriptlib-directory)).

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
omitted. Statements end at the end of the line, and a block runs from its label to its `end`. A block
label, a statement and a match arm each end their line, so the next one starts on a line of its own; a
token after one on the same line is **E2001**:

```maxon
if ready() 'x' return 1 end 'x'   // error E2001: Expected newline after block label, got 'return'
```

A token after a statement or a match arm that is an error in its own right is reported as that error
ahead of E2001: an `otherwise` with no `try` is **E3058**, a statement after a `return`, `throw`, `panic`,
`break` or `continue` is **E3071** (even when it is also a self-assignment), a self-assignment is
**E3067**, a token that opens no statement is **E2001** `unexpected token`, and an arm after a `default`
is **E2029**.

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

Maxon source is UTF-8 text, and a UTF-8 byte order mark at the start of a file is accepted. Outside
string literals and comments, every character belongs to a token. Any other character there — `?`, `;`,
`|`, `&`, `$`, `%`, `~`, `^`, a backtick, a control character or a non-ASCII character — is **E1010**
(`Unexpected character`).

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
A `///` after code on the same line is an ordinary comment.

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
| `spawn` | followed by a type's static factory call, optionally after `(capacity: n)` (see [Services](#services--spawn)) |
| `bits` | followed by `(` on the right-hand side of a typealias |
| `view` | at the start of a statement, or after `if` or `then`, followed by a name, or by a `( … )` list and `=` (see [Views](#views-view)) |
| `readonly` | before an element or value type argument of a container (see [Read-Only Values](#read-only-values)), and after `returns` at a boundary with no body (see [Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly)) |
| `keep`, `sole` | before a parameter's type, and `sole` after `returns`, at a boundary with no body (see [Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly)) |

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

An integer literal in any radix lies between `-9223372036854775808` (`i64.min`) and `18446744073709551615`
(`u64.max`); one outside that range is **E2011**. A literal has no typealias of its own, so it fits any integer
alias it is in range for. A literal above `i64.max` is the unsigned value, and it is legal only where a type
that admits it receives it: an `as` cast, a typed parameter, field, return or slot, a range bound, an operand
beside an alias that admits it, a merge arm or `otherwise` fallback of such a type, or a float slot. Anywhere
else it is **E2011** (`Integer literal '9223372036854775808' is above i64.max, so it needs an alias whose
range admits it: write '9223372036854775808 as <alias>'`); into a signed or narrower alias it is **E3005**.

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
operand is its codepoint, a `Codepoint` (`cp - '0'`).

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
typealias Age = int(0 to 150)
typealias AgeArray = Array with Age

let primes = [2, 3, 5, 7]              // a constant Array
let ages = ["ada": 36, "alan": 41]     // a constant Map
var known = ["ada": 36 as Age]         // a Map with (String, Age) that can grow
var seen = AgeArray from [36, 41]      // an AgeArray that can grow
```

The element type comes from the first element (or pair), and every later element is converted to it the
way an argument is to its parameter: `[1.5, 1]` holds `1.0`, and `["a", 1]` is **E3005**. A `let` bound to a literal of bare numbers is a
constant: its elements fit any element alias that admits them at each use, and it is immutable — a method
that writes it is **E3159**. A `var` of such a literal is **E2004**: cast the numbers, or name the alias of an
`Array`, `Set`, `List` or `FixedArray` (`AgeArray from [...]`). See
[Initializing From Literals](#initializing-from-literals) for literals of other collection types.

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
is **E2061**. `bool` and `cstring` are used bare. A variable takes its type from its initializer, and a
`var` holding a number names its alias there:

```maxon
typealias Tally = int(0 to u64.max)

let limit = 10         // a constant: fits any alias its value fits
let ratio = 0.5        // a float constant
let done = false       // bool
var count = 0 as Tally // a Tally
// var total = 0       // E2004: A var cannot start from a bare number
```

See [Variables](#variables) for the rules.

The standard library declares a few aliases every file can use, among them `ExitCode`, `Byte`
(`int(0 to u8.max)`), `ByteArray` (`Array with Byte`), `Codepoint` (`int(0 to 1114111)`), `HashValue`
(`int(0 to u32.max)`), `Count` (what a collection's `count()` returns), `SourceLineNumber` and `FullRangeFloat`
(`float(f64.min to f64.max)`).
[Ranged Type Aliases](#ranged-type-aliases) covers declaring your own.

### Integers

An integer alias declares a **quantity**: `int(lo to hi)` includes `hi`, `int(lo upto hi)` excludes it.
Bounds are literals or the limits of a sized type: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`
(`.min` / `.max`). `.min` and `.max` belong to the sized types: `Score.max` on a ranged alias is **E2073**.

```maxon
typealias Score = int(0 to 100)
typealias Offset = int(i64.min to i64.max)
typealias Tally = int(0 to u64.max)
```

- **Literals** are decimal, hexadecimal (`0xff`), binary (`0b1010`) or octal (`0o777`), with optional `_`
  separators, from `i64.min` to `u64.max`. A literal above `i64.max` needs a type that admits it (see
  [Integer Literals](#integer-literals)). A literal carries no alias, so it fits any integer alias it is in
  range for.
- **Arithmetic** is `+ - * / mod`, always computed at 64 bits. `/` truncates toward zero (`-7 / 2` is
  `-3`). A divisor that might be zero makes the division throw — see
  [Division by Zero](#division-by-zero).
- **Overflow wraps.** Integer arithmetic is two's-complement: `i64.max + 1` is `i64.min`. A range is not
  enforced on every intermediate result; it is checked where a value reaches a place declared with the
  alias (see [Range Checks](#range-checks)).
- **Bitwise** operations use the word operators `and`, `or`, `xor`, `not`, `shl` and `shr` (see
  [Expressions](#logical-and-bitwise-operators)).
- An alias whose lower bound is `0` is **unsigned**: `shr` zero-fills it, and its range check refuses a
  negative value from a signed source. An alias whose upper bound is above `i64.max` — `int(0 to u64.max)`,
  or `int(0 to 9223372036854775808)` — reads every value as unsigned: it compares, divides, range-checks and
  prints unsigned, and converts to a float as the unsigned value. `int(0 to u64.max)` holds every
  value up to `u64.max`: a value from an unsigned source — an unsigned alias, `u64.max`, a literal above
  `i64.max`, or arithmetic over unsigned operands — passes its door unchecked (see
  [Range Checks](#range-checks)). A value that is a pattern rather than a count is a
  [`bits(64)`](#bit-patterns--bitsn).
- An array, set, list, fixed-array or map-column literal whose bare elements need both a negative value and a value
  above `i64.max` (`[-1, u64.max]`) has no integer type that holds them all, and is **E3005** at the element
  that makes it so (`no common integer type for -1 and 18446744073709551615`); cast the elements to an
  alias. A `let` whose value merges a range-known value above `i64.max` with a non-negative one (`try
  big.get(1) otherwise 0`) is range-known as an unsigned interval; a merge needing both a negative value and a
  value above `i64.max` has no known range.

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
- A float whose range is known — a literal, a `let` constant, an element of a `let` literal, a merge of
  such values, and arithmetic over them — carries that interval, and a use where the interval leaves the
  float alias it reaches is **E3005** at compile time. A quotient whose divisor's interval holds zero, and
  any float whose range is unknown, is a `FullRangeFloat`.

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
`Comparable`, `Stringable` and `Cloneable`; `bool` is `Equatable`, `Comparable`, `Stringable` and
`Cloneable`; `Character` is `Hashable`, `Equatable`, `Comparable`, `Stringable` and `Cloneable`; `String`
is `Hashable`, `Equatable`, `Comparable` (byte order, a shorter prefix first), `Cloneable` and `Iterable`.
Every alias of `int` or `float` conforms as its primitive does. Every integer alias therefore works as a `Map` key or `Set` element. You can add methods to
a primitive with an [extension](#extensions-over-primitives).

### Type Conversions

#### Explicit Conversions (`as`)

`value as Alias` converts to a named alias. The target is always an alias (or `bool`); a bare `int` or
`float` target is **E3005**. The result has the alias as its type, a full-range one included: `10 as Integer`
is an `Integer`, and it reaches a place declared with another alias over the same range only through a cast.

```maxon
typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)

function main() returns ExitCode
	let b = 200 as Byte
	let wide = b as Integer      // widening: no run-time check
	let r = wide as Real         // integer to float
	let back = wide as Byte      // narrowing: checked at run time
	print("{b} {wide} {r} {back}\n") // 200 200 200.0 200
	return 0
end 'main'
```

| Cast | Result |
|------|--------|
| integer alias → integer alias | legal; widening emits no check, narrowing panics if the value is out of range |
| integer alias or literal → float alias | legal; a value of an unsigned alias converts as the unsigned value |
| float alias → float alias | legal; the value is checked against the target's range |
| integer alias ↔ `bits(n)` | legal (see [Bit Patterns](#bit-patterns--bitsn)) |
| literal → alias | checked at compile time: `256 as Byte` is **E3005** (`Value 256 is outside the range of 'Byte'`) |
| float → integer | **E3009** `Cannot cast from float to int` — use a rounding function |
| `bool` ↔ number, `String` → number, struct ↔ anything | **E3009** |
| a value to the type it already has (`b as Byte` when `b` is a `Byte`; a struct, enum, union, `bool` or `String` to itself) | **E3010** `unneeded cast` |
| a literal to the alias its destination declares (`open(8080 as Port)` for a `Port` parameter) | **E3010** `unneeded cast: the literal 8080 already fits in 'Port'` — see [Construction](#construction) |
| a literal in an unannotated `let` every use of which takes the plain constant (`let p = 8080 as Port`, read only as a `Port` argument) | **E3010** `unneeded cast: the literal 8080 already fits every use of 'p'` |
| one container instance to a different one | **E3131**, unless the elements are same-named declarations of one layout, which re-brands the container |
| a cast of a cast (`x as A as B`, `(x as A) as B`) | **E2079** `a cast cannot take another cast as its operand — cast directly to 'B'` |

A cast takes one step. Where two steps each do work — a range check, then a reinterpretation as a
`bits(n)` pattern — bind the first result and cast that: `let a = x as A`, then `a as B`.

#### Floats to Integers

A float never converts to an integer implicitly (**E3009** `cannot implicitly convert 'float' to 'int'`).
Choose the rounding explicitly:

| Function | Result | Example |
|----------|--------|---------|
| `trunc(x)` | `FullRangeInt`, toward zero | `trunc(-2.7)` is `-2` |
| `round(x)` | float, nearest, ties to even | `round(2.5)` is `2.0` |
| `floor(x)` | float, toward negative infinity | `floor(-2.5)` is `-3.0` |
| `ceil(x)` | float, toward positive infinity | `ceil(2.1)` is `3.0` |

`trunc` is the one that produces an integer; apply it to the result of `round`, `floor` or `ceil` to get
an integer from those. Its result is a `FullRangeInt` (`int(i64.min to i64.max)`); cast it to reach a
narrower alias. `round`, `floor`, `ceil`, `sqrt`, `abs` and the float `min` and `max` also accept a value of
an integer alias, which they convert. A float result whose range the compiler knows carries that range and
fits any float alias admitting it; any other is a `FullRangeFloat`. A function the file can see that shares
one of these names, a generic one included, is the function a bare call of that name runs.

#### Implicit Conversions

Maxon converts implicitly in only these places:

- **Integer to float in mixed arithmetic and float slots.** `let x = 5` then `x + 2.0` is `7.0`, and an
  unaliased integer (a literal, or a `let` constant) is accepted where a float is expected. A value of a
  *named* integer alias beside a float operand converts too, and the result is a `FullRangeFloat`. A named
  integer value reaches a float-alias parameter through `as`, and so does a value of one float alias at a
  place declared with another — the standard library's `FullRangeFloat` among them.
- **A character literal beside an integer** is that character's codepoint, a `Codepoint`:

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
- A lower bound above `i64.max` (`int(9223372036854775808 to u64.max)`) is **E3005**, and so is an empty
  range whose lower bound is non-negative and whose upper bound is negative (`int(0 to -1)`).
- An upper bound above `i64.max` makes the range unsigned (see [Integers](#integers)).
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

**Every typealias declaration is its own type**, even when two aliases spell the same range. A value of one
alias reaches a place declared with another — a parameter, an assignment, an `otherwise` fallback, a `match`
arm, a struct field, a union payload, a generic argument — through the cast you write:

```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function yearOfAge(a Age) returns Year
	// return takesYear(a)            // E3005: argument type mismatch for 'y': expected 'Year', got 'Age'
	return takesYear(a as Year)       // the cast converts
end 'yearOfAge'

function main() returns ExitCode
	print("{yearOfAge(30)}\n")        // 30
	return 0
end 'main'
```

- `as` converts in both directions. A widening cast (the source range fits the target) emits no check;
  a narrowing cast keeps a run-time check.
- Casting a value to its own alias is **E3010** (`unneeded cast: 'Age' already fits in 'Age'`).
- A value with **no** alias fits any alias of its kind whose range admits it, checked at compile time at
  each use: a literal, a plain `let` constant, a loop counter between two literal bounds, and a value
  computed from them (see [Variables](#variables)). A named value also fits an unnamed slot. Two
  *different* names conflict unless one implements the other (next section).
- A value keeps its alias wherever it is held: a local or global initialized by a cast has the cast's alias,
  and each tuple element keeps its own, so `(Tally, Tally)` and `(Score, Score)` are two types. Float
  aliases follow these rules exactly as integer aliases do — through locals, parameters, fields, captures
  and array elements.
- Two declarations of one alias name, in two files, are two types too, even over the same range. An
  operator, an argument, a store or a join that mixes them is **E3005** (**E2028** for a join) until one side
  is cast; `as` between them converts, and `return` converts as it does for any two aliases. The rule holds
  for every alias form — ranged, function, generic-instance and tuple — and for the elements of a container
  instance: `Array with Score` over two `Score` declarations is two types.
- An `Array` is indexed by the standard library's `ElementIndex`: `get`, `set` and `resize` take one.
  `count()` returns a `Count`, which implements `ElementIndex`, so a count goes wherever an index is taken
  and an `ElementIndex` goes where a `Count` is declared only by cast. The index must be an `ElementIndex`
  or an alias that implements it; any other alias, or a non-integer such as a `String`, is **E3005** —
  cast it, or declare it `implements ElementIndex`.
  A program's own array record that declares `get`, `set` or `resize` with a first parameter that is not an
  integer alias has an ordinary method: the call is served from that declaration, not the array surface.

**`return` converts.** `return x` in a function declared `returns T` behaves as `return x as T` — the one
implicit conversion between aliases. A widening return emits no check, a narrowing one keeps its check,
and `main` may return any integer alias without spelling `ExitCode`. A different struct, a union where a
scalar is declared, or a lossy float where an integer is declared is still refused.

### Subtypes With `implements`

A ranged alias may name a parent alias after its range. It is then a **subtype** of the parent, and of every
alias the parent implements:

```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias LoopHead = int(0 to u64.max) implements BlockId
typealias VarId = int(0 to u64.max) implements ElementIndex
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	rows.push(9)
	let head = 1 as LoopHead
	let r = try rows.get(head) otherwise 0    // a LoopHead is an ElementIndex
	let other = 3 as VarId
	print("{r} {takesBlock(head)} {other}\n")  // 9 1 3
	// takesBlock(other)                      // E3005: expected 'BlockId', got 'VarId'
	return 0
end 'main'
```

- A subtype goes wherever an ancestor is declared, with no cast. Casting it to an ancestor retypes the value
  as the ancestor: `let hi = xs.count() as ElementIndex` is an `ElementIndex`.
- The relation is one way: an ancestor value needs a cast to become the subtype, and that cast keeps its
  run-time range check. Two subtypes of one parent are two different types.
- The subtype's range lies inside the parent's, over the same primitive and signedness (**E3178**). Its own
  range is what its values are checked against.
- A parent is a ranged typealias (**E2003** otherwise), and may be qualified (`typealias Id = int(0 to 9)
  implements shapes.Index`). A chain that returns to itself is **E3091**.
- `implements` applies to `int` and `float` ranges. An alias of an alias, `typealias X = Y`, is **E2015**.

**Joins.** The arms of a `match` or a ternary that give two different aliases have the type of their nearest
common ancestor; arms with no common ancestor are refused.

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

A cast of that literal to the alias the destination declares converts nothing and is **E3010**
(`unneeded cast: the literal 8080 already fits in 'Port'`). The destinations are a call argument (direct or
through an interface), a `return` (a parameter default included), a struct-literal field and a store to a
declared field — `open(8080 as Port)`, `return 0 as ExitCode` from `main`, `Config{port: 8080 as Port}`.
A cast to a different alias of the same name converts and is legal: this file's `Item = int(0 to 200)` at a
parameter declared with another file's `Item = int(0 to 255)`, or an alias nested in a type or extension
body at a place declared with a file-scope one.

A literal cast in an unannotated `let` (`let p = 8080 as Port`) is **E3010** when every use of the `let`
takes the plain constant the same way (`unneeded cast: the literal 8080 already fits every use of 'p'`):
`let p = 8080` already fits each use. A `public`, `export` or `module` `let` keeps its cast, and so does one a closure captures.

A literal cast is how a type is fixed where nothing declares one, and there it is legal: an unannotated
`var` (`var p = 8080 as Port`), a `let` some use of which needs the alias, a field default whose type the
cast gives (`var port = 8080 as Port`), an array-literal element, an argument or field of a generic type parameter, an operator operand, and a value
joined by `if`/`else`, `gives` or `otherwise`. At an overloaded
call, the unneeded literal casts of one call are reported together, and only when removing all of them
still selects the same overload — a cast that decides which overload runs is needed.

Write `value as Alias` to convert a value of another alias.

### Arithmetic

Arithmetic keeps the alias of its operands. Two operands of one alias give that alias; an unnamed operand
(a literal, a loop counter) adopts the named one, and so does an ancestor (`block + offset` over a
`BlockId` and an `ElementIndex` is a `BlockId`); two aliases with no common ancestor are **E3005** until
one side is cast. The same rule governs comparisons, and a [top-level initializer](#top-level-variables)
refuses the same pairs. A shift takes the alias of its left operand.

Two subtypes of one parent make a *mixed* expression. It lands in any destination one of its operands'
aliases satisfies — a parameter, a field, an `otherwise` or `match` merge — computed into a temporary of the
destination's alias, so the result is range-checked there. Anywhere else its type is the operands' nearest
common ancestor, and a diagnostic names that ancestor. At an overloaded call each operand's alias counts:
`show(block + varId)` against `show(b BlockId)` and `show(v VarId)` is **E3007**, and the candidate list
names the aliases (`Candidates: (b BlockId), (v VarId)`).
Unary `-`, `not`, `or` and `xor` keep the operand's alias only where its range is closed under the operator: a full-width unsigned alias (`int(0 to u64.max)`, `bits(64)`) wraps modulo 2^64 and keeps it, and a range symmetric about zero keeps it under `-`. Over any other alias the result is an unnamed signed value carrying the range it computes (`-b` over `int(0 to 100)` is `[-100, 0]`), and it needs `as` to reach an alias slot. `and` always keeps the alias.

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
	print("{sum} {bumped} {mixed} {m}\n")
	return 0
end 'main'
```

The alias is a name, not a proof: `a + b` over `Score` is a `Score` that may hold 130. Range checks apply
where the value lands (next section). All integer arithmetic is 64-bit and wraps on overflow.

### Range Checks

A value is checked where it reaches a place **declared** with the alias: a call argument, a `return`, a
`var`'s initializer and every assignment to a ranged `var` (local, top-level, `static` or field), a
struct-literal field, a field store, a field's declared default, an array element, a union case's payload,
or an explicit `as`. A `var` therefore always holds a value of its alias: `var t = a + b` over two `Score`s
checks the sum where `t` is born.
The rules below hold for every ranged alias, integer or float, including one declared inside a type or
extension body. A field is checked against the alias its declaring file names, and a literal passed through
an interface is checked at compile time against the interface's own parameter type.

- A value the compiler can compute — a literal, a constant expression — that is out of range is a
  compile error, **E3005** (`Value 101 is outside the range of 'Percent' (int(0 to 100))`).
- Any other value gets a run-time check where needed. A check is omitted when the value's own range
  provably fits.
- A full-unsigned quantity (`int(0 to u64.max)`) is checked only for a negative from a signed source.
  A value from an unsigned alias, `u64.max`, a literal above `i64.max`, or `+ * and or xor shl shr` and
  unsigned `/` `mod` over unsigned operands passes unchecked. An unsigned subtraction panics when it
  borrows (the left operand is smaller than the right); its difference passed through a merge or further
  arithmetic is tested for a negative. A constant expression folds with the same two's-complement
  wrapping as the run time and is judged by that same rule.
- A full-unsigned parameter is checked at the call: a direct call, a call through a function value, an
  interface method call, an `async` call and a service send each check the argument they pass, so a
  negative is refused there and a value above `i64.max` passes. Its function checks it on entry as well
  when some call that reaches it carries no such check.
- A failed run-time check is a **panic**, not a recoverable error: the program prints
  `panic at <file>:<line>: Range check failed: value outside typealias '<Name>'` and a stack trace, and exits
  with code 1. No `try` is involved.

**Reassigning a ranged `var` is a checked place**, so the panic names the assignment that left the range:

```maxon
typealias Score = int(0 to 100)

function bump(start Score) returns Score
	var s = start
	s = s + 200       // bump(10) panics here: Range check failed: value outside typealias 'Score'
	return s
end 'bump'
```

**`ExitCode`** is the stdlib alias `main` returns. Its range follows the target: `int(0 to u32.max)` on
Windows and `int(0 to 255)` on Linux, macOS and WASI. A literal outside it is a compile error, and a
computed value outside it panics at the `return`. A program's own `typealias ExitCode` may be `main`'s
declared return type when it is an integer alias whose range lies within the builtin's; any other is
**E3002**.

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

Name an alias for its **purpose** — `Tally`, `BytePos`, `Coord`, `Milliseconds` — and declare it in the
module it belongs to. The standard library follows the
same pattern and exports a small set of cross-cutting aliases:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | target-dependent (see above) | process exit codes |
| `Byte` | `int(0 to u8.max)` | one byte; `ByteArray` is `Array with Byte` |
| `HashValue` | `int(0 to u32.max)` | `Hashable.hash()` results |
| `Codepoint` | `int(0 to 1114111)` | Unicode scalar values |
| `Count` | `int(0 to u64.max) implements ElementIndex` | a collection's `count()` |
| `SourceLineNumber` | `int(1 to i32.max)` | caller line numbers (`__line__`) |
| `FullRangeFloat` | `float(f64.min to f64.max)` | general floating-point values |

Because every alias is its own type, a quantity crossing from one module's alias to another's is cast at
the crossing.

Where a standard-library alias fits, use it; where none fits, give your own alias a name of its own,
distinct from the library's. A program's declaration of a library name is
what the bare name means in every author file — the library's stays reachable as `stdlib.Name` — so the two
are two types and every value crossing between them needs a cast.

A non-exported ranged alias is private to its file: two files may declare one name over different ranges,
or one over `int` and the other over `float`, and each file's uses mean its own declaration. Exported
declarations of one name live in different directories, whatever their ranges, and a bare name that reaches
more than one of them is ambiguous in every file, the declaring files included (see
[Bare Names and Ambiguity](#bare-names-and-ambiguity)).

### Generic-Instance and Function-Type Aliases Are Brands

An alias over a generic instance or a function type follows the same rule. `typealias Xs = Array with
Integer` and `typealias Ys = Array with Integer` are one instance (one layout, one method set) under two
**brands**: an `Xs` does not flow into a `Ys` slot unless you write `xs as Ys`, which re-brands the value at
no cost, and `return` re-brands implicitly.

- A `[...]` literal carries no brand and fits either, with or without an `Alias from` head.
- An element stored into a branded slot must carry the slot's brand, in a literal as in `push`: with
  `typealias Rows = Array with Xs`, both `Rows from [ys]` and `rows.push(ys)` for a `Ys` value are
  **E3005**.
- A brand travels with the value: through a tuple element (`(Xs, Item)` and `(Ys, Item)` are two types),
  a destructuring, a merge and a read of a `var`. The arms of a merge holding two brands are refused.
- An unbranded `var` takes the brand of the first branded value stored into it, and keeps it: a later
  store of another brand is **E3005** (see [Variables](#rules)).
- Comparing values of two brands (`xs == ys`) is **E3005**: a `Self` parameter, as `equals`' operand is,
  takes the receiver's brand. Inside a generic type, a parameter spelled with the bare base (`other Box`)
  fits every brand of the instance.
- A closure literal or a declared function carries no brand and fits any function alias of its shape.
- A function alias declared **inside a type or extension body** carries no brand either, in both
  directions: `Array.SortComparator` spells the shared body's signature over the type's own parameters,
  and a brand is a file-scope name an author chose to distinguish two shapes. A value of any other alias
  of that shape flows into it, and a value of it flows into any other alias of that shape.
- Two **file-scope** function aliases of one shape still refuse each other — including two
  directory-qualified ones, `api.Score` and `legacy.Score`, which are file-scope declarations wearing
  their directory and can be written in a type position.
- A non-exported function alias is **private to its file**, so two files declaring one name have two
  brands and two types, whatever their shapes: each file's declarations mean its own, and a value of one
  reaches a door declared with the other only through `as`. Two declarations of one generic-instance alias over the
  same instance are two brands in the same way.
- A call's result wears the alias its callee's `returns` clause names, bare or directory-qualified, as the
  file that declares the callee resolves it: a caller's own declaration of that name is another brand.
- A field read, a read of a module-level `var` and a `match` payload binding wear the alias the field,
  variable or payload was declared with, as its declaring file resolves it: with `held(v Xs)`, the
  binding `v` in `held(v) then …` is an `Xs`.
- A bare reference that reaches two `export`ed declarations is ambiguous in every file, a declaring file
  included: **E3063**, resolved by qualifying with the directory namespace or by renaming.
- A diagnostic names each type as source would qualify it: the standard library's as `stdlib.Name`,
  another directory's as `dir.Name`, the root's bare. Where the two sides would still print alike, it adds
  a parenthesised note — the shape where two function shapes differ, otherwise `(declared in <file>)`. A
  view of a buffer prints as `__ManagedMemory with <element>`, noted `(the view of 'Name')` where two views
  read alike.
- Casting to a **different** instance (`Array with Byte` to `Array with Integer`) is **E3131**: the
  elements have different layouts, so build a new container instead. A cast between two instances whose
  elements are same-named declarations of one layout — the same underlying type, range and domain;
  `implements` aside — re-brands the container in place.

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

A ranged alias declared in a type or extension body is a nominal type by the same rule as a file-scope
one: a value of any other alias, over the same range or not, crosses into it with a cast.

A type has one member of each name. When two `extension` bodies of the program declare the same nested
typealias for one type, in one file or two, over one instance or two, and for each conformer of an
[interface extension](#interface-extensions) that both reach, the later declaration is
[E3061](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3061), naming the file of the one kept: the
declaration in the file whose absolute path, with `/` separators, sorts first byte by byte, and within a file
the one written first. A conditional extension declares the member only for the conformers its `where`
clause admits, and only the program's own extensions are compared.

A `type` or `interface` body declares each nested typealias once: a second declaration of the name in the
same body, whatever its form (generic instance, ranged, function or tuple), is
[E3061](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3061) at its name.

A nested typealias follows the rule a method does (see [Static Methods](#static-methods)). An `extension` of a
type that declares a nested typealias the type's own body declares is
[E3061](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3061) at the extension's declaration, in any file and
for a standard-library type too. An [interface extension](#interface-extensions)'s nested typealias is a
default: a conforming type whose body declares the same member keeps its own, and the extension's methods,
compiled for that type, read it.

A nested typealias that an `extension` of the program declares under the name of one a library `extension` of
the same type also declares (`Array.ElementArray`, `Array.SortComparator`, `Iterable.ElementTransform`) is a
separate member. The program's extension bodies, in every file, spell it with the bare name, and callers of
the program's methods see it; the library's bodies and methods keep the library's member. A member without
a visibility modifier is spelled from its own file only: another file's extension body naming it is
[E3008](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3008).

A per-instance **function** alias is unbranded: it spells the shared body's signature over the type's own
parameters. `Array.sort` takes an `Array.SortComparator`, so a field declared with your own `typealias RowComparator =
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

	static function create(x Coord, y Coord) returns Point
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
- A field without a default names its type after `as` (`var x as Coord`); a field with a default takes its
  type from the default (see [Required Field Initialization](#required-field-initialization)). The type is an
  alias, `bool`, a type, or another named type — a bare `int` or `float` is refused.
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

1. **The declaration supplies a default**, evaluated each time a literal omits the field. The field's type
   comes from the default, which takes one of three shapes:
   - a `true`/`false` literal (`bool`) or a string literal (`String`): `var enabled = true`;
   - `Type.member` or `Type.member(...)` — an enum case or a static factory — typed `Type` and checked
     against it: `var level = Priority.low`, `var items = IntArray.create()`;
   - an expression whose outermost node is a cast, typed by the cast: `var count = 0 as Tally`,
     `var span = NoSpan as SpanId`, `var total = (1 + 2) as Tally`.

   A number has no type of its own, so `var count = 0` is **E2004**, as is any other default, such as a
   bare global (`var span = NoSpan`) or `1 + 2 as Tally` (whose outermost node is the `+`). A field declares
   its type after `as` only when it has no default: `var count as Tally = 0` is **E2010**. A cast that gives
   the field its type is legal even on a value that already has it (`NoSpan as SpanId`); a cast on a default
   whose shape already gives the type (`var g = Gauge.make() as Gauge`) is **E3010**.
2. **The literal supplies it**: `Counter{value: 5}`. A supplied value always wins over the default.
3. **A static factory assigns it** with `self.field = expr` on every path before `return Self{}` (or the
   type's own literal).

A literal that leaves a field uninitialized is **E3086**, listing the fields.

```maxon
typealias Tally = int(0 to u64.max)

type Counter
	export var value as Tally
	export var version = 0 as Tally

	static function create(initial Tally) returns Self
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

	static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'

	function add(other Point) returns Point
		return Point.create(x + other.x, y: y + other.y)
	end 'add'

	function manhattan() returns Coord
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

An instance method called with no receiver is [E3193](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3193):
a bare call to it from a static method, a file-scope initializer or a `static var` initializer, or
`Type.name()` with no arguments. `Type.name(value, …)` calls it on `value`.

A type may declare a static and an instance method with the same name, in its body or in an `extension` of
it; `Type.name()` calls the static one and `value.name()` the instance one. A bare `name()` calls the
instance one where `self` is reachable, in an instance method and its closures, and the static one
everywhere else in the type: in a static method and its closures, and in an initializer. This holds
whichever of the two is declared first. An `extension` member with the
name and kind of a member the type's own body declares is
[E3176](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3176), in any file and for a standard-library type
too. An [interface extension](#interface-extensions)'s method is a default: a conforming type may declare its
own method of that name, and its values call that one.

### Static Fields

`static var` and `static let` declare one value per type, read and written as `Type.name`:

```maxon
typealias Byte = int(0 to u8.max)

type Limits
	static let MAX_COUNT = 1000
	static let SEPARATOR = 61 as Byte
	static var used = 2 as Byte
end 'Limits'

function main() returns ExitCode
	Limits.used = Limits.used + 1
	print("{Limits.MAX_COUNT} {Limits.SEPARATOR} {Limits.used}\n")   // 1000 61 3
	return 0
end 'main'
```

- A static field needs an initializer. Assigning to a `static let` is **E2013**; naming a static the type
  does not declare is **E2073**.
- An initializer is any expression a function body accepts, read as though written inside the type's body:
  a literal, a factory call (`static var shared = Cache.create()`), an array literal, or a struct literal of
  the enclosing type. A struct literal of *another* type is **E3076** — call its factory. A scalar static is
  a compile-time constant, so a call that makes its value unknown is **E2045**, and a `static var` whose
  initializer is a bare number is **E2004**.
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
  (**E3078**), and a `spawn` reachable from any global initializer is **E3164** — a
  [`default` declaration](#program-wide-defaults--default) starts a program-wide service before `main`.
- A `let` without `static` in a type body is an ordinary field with a default.

### Equality and Copying

- `a == b` and `a != b` on two records call the type's own `equals(other)` method, which must return `bool`.
  Two values of one generic instance (`Box with Score`) compare the same way, through the generic type's
  `equals`. Declaring `implements Equatable` is not required. A type with no `equals` cannot be compared:
  **E3005** (`cannot compare Box with Box`, naming the type). Ordering (`<`) on records is refused the same way.
- `a == b` on two arrays of one type compares them element by element, through the element's own `equals`.
  It needs an element that is `Hashable` and `Equatable` (**E4006** otherwise).
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

A type the program `spawn`s is a heap record whatever its fields: it is the state its service loop owns.

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

	static function create(lo Half, hi Half) returns Pair
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

	static function create(side Tally) returns Self
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
- A requirement's parameter and return types are matched as declared: a typealias is its own type, and a
  typealias of a generic instance is a [brand](#generic-instance-and-function-type-aliases-are-brands), so
  `make() returns IntArray` is not met by `make() returns Other` over the same instance (**E3016**). A call
  through an interface value or a `where` constraint checks each argument against the requirement's
  declared type as a direct call does (**E3005**), and its result carries the requirement's typealias.
- `Self` in a requirement means the conforming type.
- A requirement may be `static function`; the conforming type provides it as a static method.
- `interface Derived extends Base` inherits `Base`'s requirements; a type implementing `Derived` provides
  both, and satisfies parameters typed `Base`.
- A requirement declares what a conformer may do with its values: `keep` or `sole` on a parameter it keeps,
  `returns sole` or `returns readonly` on its result (see
  [Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly)). A conformer that does more is
  **E3189**.

**Interface-typed parameters, returns and fields.** An interface can be used as a type, as `describe`
does above: a parameter typed `Shape` accepts any conforming type, `enum` and `union` conformers
included, a function may return an interface,
and a field may hold one. A function that returns an interface may `return match …` with a different
conformer in each `gives` arm. A struct literal (`Self{shape: Square.create(3)}`) or an assignment
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
`Map`, and `Array`, `Set`, `List` and `FixedArray` aliases take `Alias from [ ... ]`, for example
`CharSet from ['a', 'e']`; the alias states the element type, and a `FixedArray` alias's literal must write
exactly as many elements as the alias holds. When that element is itself an array alias, each element is a
value of it: another `Alias from [ ... ]`, a bare `[ ... ]` that takes its element type from the alias, or
a local of the inner alias — `Rows from [Ints from [1, 2], [3], row]` over `typealias Rows = Array with
Ints`. `Set` and `List` also take any `Array` value, under the bare name or an alias: `Set from names` builds
a set of `names`' elements, and an alias head takes an `Array` of exactly its element; any other source is **E3005**.
An `Array` or `FixedArray` alias head takes an array literal. A type that declares `implements InitableFromArrayLiteral with
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
			total = total * 10 + d as Number
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

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function get() returns T
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
- A static called on the bare generic name infers the arguments from its parameters: a parameter written
  at a type parameter, or at an instance over one (`Array with T`, or the generic itself). An overloaded
  static infers them from the overload its arguments bind. A call whose result is
  written over a parameter that no argument fixes (`Holder.make()` declared `returns T`) has no type and is
  **E3162**; inside the generic's own body the enclosing instance types it.
- One compiled body serves every instantiation. A method learns how its type arguments are laid out and
  owned from the instance it is called on, and that includes instances the body composes over its own
  parameters — `Array with (Pair with T)`, `Box with (T, Integer)`. A closure uses what the function it is
  written in received.
- A `static function` learns it from the instance its call names: the typealias it is called through
  (`IntBox.create(42)`), the instance its arguments fix on the bare generic name, or, called from a method
  of the same generic, what that method received. This covers every static, whatever it returns, that
  needs its instance's layout, its `countof(Self)` or the tables of a `where` constraint, itself or through
  what it calls. A call that names no instance, from a function holding none to hand on, is **E3162** when
  the static needs the layout (below), and **E2015** when it needs the count or the tables.
- A generic type whose methods reach an instance of itself through an ever-growing type argument
  (`Node with (Pair with T)` inside `Node`) has no finite set of instances, and is **E2015**.
- Wrong argument count is **E2056**; a bare `int` argument is **E2061**; a type argument to a non-generic
  type is **E2055**.
- A `float` type argument, bare or through a ranged float alias such as `FullRangeFloat`, is **E2062**
  (`Box with FullRangeFloat`). The element of an `Array` or a `FixedArray` is the exception, because it is stored
  inline in a buffer: `Array with FullRangeFloat` and `FixedArray with 2 FullRangeFloat` compile, and there a bare `float` is
  **E2061**.

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

	function itemDigest() returns Code
		return item.digest()
	end 'itemDigest'
end 'Tagged'
```

- Several interfaces on one parameter: `where T is Hashable and Equatable`.
- Several parameters: `where K is Hashable, V is Cloneable`.
- An instantiation whose argument does not conform is **E3017** (`Type 'X' does not satisfy constraint
  'Digest' required by type parameter 'T' of 'Tagged'`).
- A constraint can bind an associated type: `where S is Cursor with E`.
- In the body, `T.member` reaches a member through the parameter, whatever top-level type or alias shares
  its spelling. A `static` requirement dispatches on a concrete type, so calling one through `T`
  (`T.make()`) is **E3116**, and naming one without its call is **E2078**. An instance requirement called
  through `T` (`T.id()`) and an undeclared member are **E2073**: call an instance requirement on a value of
  type `T`.

### Generic Functions

A free function or a `static` method declares type parameters with `uses` after its parameter list, and
constrains them with `where` at the end of the declaration line:

```maxon
typealias Score = int(0 to 100)

function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	print("{larger(30 as Score, b: 70)} {larger("pear", b: "apple")}\n")   // 70 pear
	return 0
end 'main'
```

A call infers `T` from the arguments whose parameters are declared `T`:

- Arguments of one alias give that alias. An argument with no alias (a literal, arithmetic over literals)
  adopts the other arguments' alias. Two different aliases give their nearest common ancestor under
  [`implements`](#subtypes-with-implements), and two with none are **E3180**.
- A type parameter binds a number, `String`, `bool`, `Character`, a typealias, a record or an enum; any
  other argument is **E3181**. A type parameter no parameter is declared with is **E3179**.
- An inferred type that does not conform to a `where` interface is **E3017**.

Each distinct inferred type compiles its own copy of the function in the declaring file, and the body is
checked against that type. Every integer and float type, every alias of one, `String`, `bool` and
`Character` conform to `Equatable` and `Comparable` (see [Primitive Conformances](#primitive-conformances)),
and a record that implements `Comparable` is ordered by `<`, `>`, `<=` and `>=` through its `compare`.

A copy may call its argument type's `where` requirements — `compare`, `equals`, any method of the
constraining interface, and a method an extension of that interface declares — whatever their visibility,
because the caller granted the conformance by passing the type. The grant reaches a closure written inside
the copy too. Every other member of the type needs ordinary visibility from the declaring file. A generic
function is itself visible like any function: a private one called from another file is **E3008**.

A non-generic declaration of the same name is chosen when every argument fits its parameter exactly: a
value of the parameter's own type, or a value with no alias — a literal, a negated literal, a loop counter or
a value computed from them — that lies inside the parameter's range. A value known to leave the range is
taken by the generic, and a parameter whose alias admits every value of its type takes any of them. Since a
literal already binds the non-generic declaration, casting it to that parameter's alias is **E3010**
(`describe(5 as Score)`); a cast to another alias fixes `T` to that alias, and the generic binds. Two generic
declarations that both infer a type for one call are **E3007**.

A generic function's result may declare `returns sole T` or `returns readonly T` (see
[Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly)); a body that breaks it is **E3189**.

A generic function may reassign a parameter declared `T`, and the argument is then passed by reference, as
for any function (see [Parameter Passing](#parameter-passing)). When a call's name reaches more than one
declaration — generic or not — and they disagree about which parameters they reassign, each call passes
its arguments the way the declaration it resolves to takes them.

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

	static function create(base Score) returns Self
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

	static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns Integer
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

- `self` is the conforming value; the extension may call any requirement of the interface, including one a
  conformer implements with a file-private function.
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

The method belongs to the conformers that satisfy the clause; calling it on any other is **E4006**, which
names the unmet constraint. A conformer that declares its own method of that name calls its own, whether
its binding satisfies the clause or not.

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
	function doubled() returns Integer
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
	var t = makePair(0, b: 0)
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
- A tuple literal written where a tuple type is declared — a parameter, a field, a variable, a return, an
  element of a typed array literal, a `match` or ternary arm, or an element of an enclosing tuple — takes
  that type's element types, so a closure literal as an element takes the declared function type.
- Each element keeps its own alias, so a tuple type is its element aliases in order: `(Tally, Tally)` and
  `(Score, Score)` are two types even over one range, an element is checked against its alias's range,
  and an element of an unsigned alias stays unsigned through a parameter, a nested tuple or a destructuring.

### Destructuring Declarations

`let (a, b) = expr` and `var (a, b) = expr` bind each element to a new name. `_` discards an element:

```maxon
let (first, _) = makePair(1, b: 2)
```

### Tuple Assignment

A tuple pattern on the left of `=` assigns to existing `var`s, and may declare new names in the same pattern:

```maxon
var p = 0 as Amount
var q = 0 as Amount
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
var total = 0 as Count
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

	function opposite() returns Direction
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
- Inside a method, `self.name`, `self.ordinal` and `self.rawValue` read the case, and a
  [struct-backed](#struct-backed-enums) enum's backing field reads through `self` (`self.latency`). Any other
  `self.x`, and any assignment through `self`, is **E2015**.
- `static function` is not supported on an enum (**E2015**).

### Enum Properties

Every enum case has:

| Property | Result |
|----------|--------|
| `.name` | the case name as a `String` |
| `.ordinal` | the zero-based declaration position, an `ElementIndex` |
| `.rawValue` | the case's raw value (the ordinal when none is declared), of the backing type — a `FullRangeInt` for an integer backing |

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
- A backing field also reads directly off the value: `op.latency` is `op.rawValue.latency`. A field named
  `name`, `ordinal` or `rawValue` is reached only as `op.rawValue.name`, because the case accessors take
  those spellings.

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
typealias Integer = int(0 to 255)

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

Payloads may be integers, floats, booleans, strings, records, other unions and collections. An integer
literal passed for a `float` payload widens to the float: `Reading.celsius(21)` carries `21.0`.

### Read-Only Payloads

A payload declared `let` is read-only for the whole life of the value, like a `let` field (see
[Read-Only Values](#read-only-values)). A value stored into it must be read-only, a value type, or moved in
(**E3078** otherwise); a match binding of it is read-only, and a write through it is **E3159**. Every payload
of a read-only union value — one held by a `let` binding, a `let` field or a module-level `let` — is read-only
too, whatever the payload's own declaration, so binding a record out of it to a `var` is **E3078**. A
union whose payloads are all `let` and all value types is itself a value type.

```maxon
typealias Weight = int(0 to 100)

union Op
	call(let callee ByteArray, weight Weight)
	nop
end 'Op'

function main() returns ExitCode
	let op = Op.call(b"emit", weight: 3)

	match op 'show'
		call(callee, weight) then print("{callee.count()} {weight}\n")    // 4 3
		nop then print("nop\n")
	end 'show'

	return 0
end 'main'
```

### Pattern Matching

In a `match`, `caseName(a, b)` binds the payload for that arm; the bindings are local to the arm.

- The number of bindings matches the case's payload fields.
- Discard one binding with `_`: `failure(_, message)`.
- To ignore the whole payload, omit the parentheses: `success then …`. Writing `success(_)` with every
  binding discarded is **E3081**.
- An `or`-chain can list payload cases bare; their payloads are not accessible in that arm.
- An arm that binds a payload covers exactly one case: every case in the arm's `or`-chain must bind the
  slots the arm reads, so a binding beside a payload-less or bare case (`stay or walk(dir)`) is **E3129**.
  Give each case its own arm.
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

When the matched value is a `let`, the bindings are immutable and assigning to one is **E2013**. They are
also [read-only](#read-only-values) all the way down, whatever each payload's own declaration, so a write
through one is **E3159** and binding a record out of one to a `var` is **E3078**.

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

	function area() returns Amount
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

As with enums, a method carries its own visibility, and `static function` is not supported. Through
`self`, a method reads `self.name`, `self.ordinal`, `self.rawValue` and a
[struct-backed](#struct-backed-unions) union's backing field; a case's payload (`self.radius`) is
**E2015**, and `match self` binds it.

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
typealias Tally = int(0 to u64.max)

var x = 42 as Tally     // a Tally: the cast names the type
x = x + 5               // reassignment allowed, range-checked against Tally
// var y = 42           // E2004: A var cannot start from a bare number: write 'var name = expr as Type'.
```

A `var` holding a number names its alias where it is born, locally, at file scope and as a `static`. A bare
number, arithmetic over bare numbers (`16 * 1024`), a plain `let` constant, and an array, map or tuple literal
of bare numbers are refused as its initializer (**E2004**); cast it (`var size = (16 * 1024) as ByteCount`).
A `var` initialized from a value of some alias is that alias.

### Immutable Variables (`let`)

```maxon
let pi = 3.14159
let name = "Maxon"
// pi = 3.14            // E2013: cannot assign to immutable variable: 'pi'
```

A `let` bound to a bare number, to arithmetic over bare numbers or to a literal container of them is a
**constant**: it acts like the literal it holds, and fits any alias its value fits, checked at compile time
at each use. A `let` computed from such constants or from a loop counter between two literal bounds carries
the range its value can have, and each use is checked against it the same way, for integers and floats
alike. A `let` of a number whose range the compiler cannot know (`let w = 1 shl k`) is **E2004**; write
`let w = (1 shl k) as Width`. A literal cast (`let port = 8080 as Port`) makes the `let` a value of the
alias; when every use of it would take the plain constant the same way, the cast is **E3010** (see
[Construction](#construction)).

```maxon
typealias Port = int(0 to 65535)

function open(p Port) returns Port
	return p
end 'open'

function main() returns ExitCode
	let base = 8000
	let port = base + 80        // a constant: 8080
	print("{open(port)}\n")     // 8080
	// open(base * 10)          // E3005: Value 80000 is outside the range of 'Port' (int(0 to 65535))
	return 0
end 'main'
```

A `let` is read-only all the way down (see [Read-Only Values](#read-only-values)): calling a method that writes
its receiver (`push`, `append`, `set`, `remove`, `clear`, …) on a `let`, or writing a field of anything it
reaches, is **E3159**.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var items = TallyArray.create()
	items.push(1)                    // fine: items is a var
	// let fixed = TallyArray.create()
	// fixed.push(1)                 // E3159: cannot write through 'fixed': 'fixed' is read-only (a `let` binding)
	print("{items.count()}\n")
	return 0
end 'main'
```

So a `let` binds only a value nothing writes while it lives:

- a fresh record no other name reaches (`let x = Box.create(1)`), frozen at birth;
- an element a library `pop`, `remove`, `removeFirst` or `removeLast` moves out of a container
  (`let x = try self.items.pop() otherwise return`), which the container no longer holds;
- a read-only value, such as a module-level `let`'s record or a `let` field of a read-only holder;
- a part of a parameter (`let z = p.child`), which is as immutable as the argument the caller passed. A caller
  that passes the same record through a second, writable argument may write it while the `let` lives.

A `let` bound to state something may still write — a field of a writable `self` or `var`, an element of a `var`
container, a getter's result on a `var` holder, a parameter the function writes, a `var`'s record that a
container also holds — is **E3188** (``cannot bind `let` 'b' to a value reached through 'holder', which something
may still write; use `view` to read it, `var` to write it, or bind a clone()``). A function that returns a
`let`-bound value returns a read-only value.

### Views (`view`)

`view x = e` binds any value read-only through `x`. Unlike a `let`, a `view` promises nothing about other names: a
write made through another name, or by another coroutine across an `await`, is seen through the view.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	view v = h.box            // `let v = h.box` is E3188: h may still write it
	h.box.n = 7
	print("{v.n}\n")          // 7
	// v.n = 8                // E3159: 'v' is read-only (a `view`)
	return 0
end 'main'
```

- Writing through a view, or handing it to a parameter the callee writes, is **E3159**.
- A view lives only inside its function. Storing it, handing it to a parameter that keeps it, returning it (or a
  call's result that is still the view) and capturing it in a closure are **E3078**; sending it to a service is
  **E3138**. Use a `clone()` for any of them. A view of a number or another scalar is copied where it goes.
- A view holds its own reference to the record it names. Once a view of a `var` is declared — of the `var`
  itself, a field or an element of it, or any expression that may hand its record back (a ternary, an
  `otherwise`, a `match`, a call) — the view co-owns that record for the rest of the function, read again or
  not: sending it (**E3138**), publishing it (**E3174**) or handing it to a `sole`
  parameter (**E3078**) is refused. The rule follows source order, so inside a loop a view declared earlier in the
  body counts. Reassigning the `var` a fresh record makes it sole again, and returning the `var` is unaffected.
- A record nothing can write — a heap type whose fields are all `let` — is shared instead: it is sent, published
  or handed to a `sole` parameter as it is, and the view stays valid. An inline packed record is copied.
- `view` is a [contextual keyword](#keywords). It declares a local binding only: a `view` field, a module-level
  `view`, a `view` in an extension body or an `@heap view` is **E2077**.

### Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  A declaration takes no type annotation: `var x as Tally = 0` is **E2010** (`Expected '=' but got 'as'`).
  A cast names the type instead — `var x = 0 as Tally` declares a `Tally`, locally and at file scope.
- **A `var` is born a value of its alias, and every assignment to it is checked against it** (see
  [Range Checks](#range-checks)).
- **A `var` takes the brand of the first branded value stored into it** when its initializer has none:
  `var a = [1 as Item, 2 as Item]` followed by `a = xs` for an `Xs` makes `a` an `Xs` for its whole life,
  and a later store of a `Ys` is **E3005** (`cannot assign a value of type 'Ys' to variable 'a', which
  holds 'Xs'`). The first store in source order decides; for a module `var`, the first in the program's
  file order. See [Generic-Instance and Function-Type Aliases Are Brands](#generic-instance-and-function-type-aliases-are-brands).
- Variables are block-scoped.
- **A `var` that is never reassigned or mutated is E3077** (`variable 'x' is never reassigned; use 'let'
  instead of 'var'`) when a `let` in its place would compile: its initializer is a fresh record or a
  read-only value, and nothing stores it where something may write it and reads it again.
- **Every variable must be used (E3012).** This covers `let`/`var`, parameters, loop variables, pattern
  bindings and closure parameters. The name `_` discards: it creates no binding and may appear several
  times in one pattern (`for (_, _) in pairs`). A parameter named `_` takes its argument unnamed, so
  reading `_` in the body is **E2004**, and one signature may declare several. In a destructuring, `_`
  discards its element: `let (a, _) = t` declares `a` alone, and `(let _, let b) = t` declares `b`
  alone. Names such as `_x` are ordinary variables. A method that
  implements an interface requirement is exempt for its parameters, since the interface dictates them.
- **Records are shared, not copied.** `var b = a` for a record-typed `a` hands `b` the same record; use
  `a.clone()` for an independent copy (see
  [Reference-by-Default Assignment](#reference-by-default-assignment)).
  Numbers, `bool` and other scalar values are always independent copies.
- **A read-only value stays read-only.** `var b = a` moves `a` when `a` is a `var`; from a read-only `a` (a
  `let` included) it is **E3078**, and `a.clone()` is the mutable copy (see [Read-Only Values](#read-only-values)):

  ```maxon
  typealias Coord = int(i64.min to i64.max)

  type Point
  	export var x as Coord

  	static function create(x Coord) returns Point
  		return Point{x: x}
  	end 'create'
  end 'Point'

  function main() returns ExitCode
  	var a = Point.create(1)
  	a.x = 2
  	var b = a                 // a MOVES into b; reading a afterwards is E3102
  	b.x = 10

  	let c = Point.create(3)
  	let d = c                 // d shares c's record: both are read-only
  	// var e = c              // E3078: a read-only value bound to a `var`
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

var callCount = 0 as Tally
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
- An initializer is any expression a function body accepts, with the same meaning, the same types and the
  same diagnostics: literals, arithmetic, casts, enum and union cases, array, map and tuple literals,
  `Type from "literal"`, `ArrayAlias from [...]`, factory and function calls (`let shared = Cache.create()`,
  `let shared = makeCache()`, `lib.Cache.create()`), a function name (`let step = countdown`), and another
  top-level `let`, declared before or after it, in this file or one it can see (`let B = A`).
- A binding that holds a scalar — a number, a `bool` or an enum case — is a compile-time
  constant, inlined at every use. Its initializer must have a value known at compile time, as a body knows
  `2 * WIDTH` or `sqrt(4.0)`; a call that makes it unknown is **E2045** (`Function calls are not allowed in
  global variable initializers`, naming the call). A derived constant keeps its operands' typealias, as in a
  body: with `let a = 5 as Small`, `let s = a + a` is a `Small`. A `var` whose initializer is a bare number
  is **E2004**; state its type (`var count = 0 as Tally`).
- A call in an initializer names its function as a call in a function body does: a name nothing declares is
  **E3004**, one the file cannot see is **E3008** (**E3088** for a `module` one), and one several directories
  declare is **E3095**. That one diagnostic covers every read and assignment of the binding. A tuple
  assignment naming a module-level binding is **E2015**; assign the element to a local and store that.
- Every initializer runs **before `main`**, once, in dependency order, whether or not anything reads the
  binding. Initializers that depend on each other in a cycle are **E2012**. A `let` whose value is decided at
  compile time — a scalar, a string or byte-string literal, a constant array literal, an empty container, a
  payload-free union case, or a record built from constants — is image data, laid down in read-only memory
  with nothing to run; any other record is built by the program's `__module_init` before `main`. A static
  field follows the same rules (see [Static Fields](#static-fields)).
- A struct literal at file scope is **E3076**; call a factory instead, or declare a static field inside the
  type.
- An initializer whose value has an interface type is **E2015**: an interface value is two words, a value
  and its witness, and a global holds one. Return a concrete type from the initializer, or hold the interface
  in a function's local.
- A tuple literal is accepted in an initializer wherever a function body accepts one, with the same type.
  `let pair = (1, "one")` is a tuple of the types its elements have; a slot that states a tuple type —
  an element of `ArrayAlias from [...]` (`let steps = Steps from [(0, 1), (2, 3)]` over
  `typealias Steps = Array with Step` and `typealias Step = (Word, Word)`), a struct literal's field
  (`static var origin = Holder{step: (7, 9)}`), a factory parameter, a union payload, or an element of another
  tuple — gives each element that slot's type. A tuple literal inside extra parentheses is the same tuple, and a
  `var` born from a tuple of bare numbers is refused as it is in a body.
- A `let`'s initializer may not reach a
  module-level `var` that holds a record — a String, an array, a struct, a boxed union — through any function
  it calls (**E3165**, reported at the `var`'s use with its declaration as a note). A `let` is fixed at startup
  and may not hold what a `var` owns, and what the initializer keeps is not followed, so reading only a number
  out of the `var` is refused too. "Reaches" is the call graph after overload resolution, dispatches
  included. A scalar `var` is readable, a `var`'s initializer may reach any global, and a `let`'s may reach
  other `let`s.
- The other direction is refused too: a `var`'s initializer may not call anything whose result may be, lie
  within or hold a module-level `let`'s record (**E3078**, at the `var`'s declaration, naming the call). What
  a call hands back is followed through further calls, witness dispatches and calls through function values;
  a record built fresh from numbers read out of a `let` is legal.
- A module-level `let` is [read-only](#read-only-values) through everything it reaches, for the program's whole
  life. A write through it, or through a record a call handed back out of it, is **E3159**; binding it, a field
  of it (`var m = H.index`) or such a call result, an element accessor's included (`var p = Origins.get(0)`),
  to a `var` is **E3078**. Read it through a `let`, or bind a
  `clone()`. Storing its record anywhere something may write it — pushing it into a container, local or
  module-level, storing it into a `var` field, putting it in an array literal or a map value, or handing it to
  a function that keeps its parameter — is **E3078** too; store a `clone()`.
- A `spawn` reachable from a global initializer is **E3164**; start services in `main`, or declare a
  program-wide one with [`default`](#program-wide-defaults--default). A `Key.current()` or `Key.register()`
  reachable from a global initializer is [E3173](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3173): the
  defaults are built after every global initializer has run.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
- A [service](#services--spawn) handler, and every function it reaches, may not read or write a module-level
  `var` (**E3143**); keep service state in its fields.

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

- The first argument is positional. When the callee's first parameter has a default, the first argument
  may also be written `name: value`, naming that parameter or any later one. Labelling a first argument
  whose parameter is required is **E2052** (`the first argument cannot be named because its parameter
  has no default`). For a method the first parameter is the one after the receiver; for a service
  message, the one after `self`.
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

A default may sit on any parameter, before or after the required ones. When the first parameter has a
default, a call may omit it and name a later argument first:

```maxon
typealias Port = int(0 to 65535)
typealias Retries = int(0 to 10)

function connect(port Port = 80, retries Retries = 3)
	print("port {port}, {retries} retries\n")
end 'connect'

function main() returns ExitCode
	connect()                    // port 80, 3 retries
	connect(8080)                // port 8080, 3 retries
	connect(port: 8080)          // port 8080, 3 retries
	connect(retries: 5)          // port 80, 5 retries
	return 0
end 'main'
```

A required parameter after a defaulted first one is named: `function f(a Integer = 1, b Integer)` is
called `f(b: 2)` or `f(5, b: 2)`.

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

- Each argument ranks the overloads it fits: a parameter declared with the argument's own alias ranks
  first, a parameter whose type is exactly the argument's next, and a parameter the argument widens to
  (an int at a `float`) last. The call binds the overload whose arguments rank highest in total, so
  beside `create(n Count)` and `create(n Integer)`, `create(7 as Count)` binds `Count`.
- A call that more than one overload matches equally is **E3007** (`Ambiguous overload`) — two
  overloads each preferred at a different argument, for one — and so is a `spawn` or an `async` call
  that does. The message lists each tied overload's parameters as its declaration writes
  them (`Candidates: (b BlockId), (v VarId)`). A first argument
  is labelled only at a defaulted parameter, and overloads of one name that declare defaults must agree
  on their parameters, so two single-parameter overloads of the same type cannot be told apart.
- A literal, a negated literal, a loop counter or a value computed from them fits a ranged-alias
  parameter when it lies inside the alias's range, so beside `pick(n Small)` (`int(0 to 10)`) and
  `pick(n Wide)` (`int(0 to 1000)`), `pick(50)` binds `Wide`.
- Declaring the same overload twice, with the same parameter names and types, is a duplicate
  definition (**E3006**). Overloads with the same parameter types but different parameter names are
  separate declarations; a call whose labels cannot tell them apart is **E3007**.
- Where a diagnostic tells overloads of one name apart, it names each by its parameter types,
  `pick(String)`, adding the parameter names where only those differ:
  `slice(start Integer, length Integer)`.
- A type may declare a `static` method and an instance method with the same name and parameters:
  `Type.name()` calls the static one and `value.name()` the instance one, and a bare `name()` calls the one
  [Static Methods](#static-methods) names.

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
	var x = 10 as Tally
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
- Passing a [read-only](#read-only-values) value to a parameter the callee writes is **E3159** (`cannot pass
  'y', which is read-only (a `let` binding), to a function that writes parameter 'n'`). The receiver of a
  method counts as a parameter, so `let acc = Accumulator.create()` followed by `acc.add(10)`, where `add`
  writes a field of `acc`, is refused too. A `let` field, or anything reached through a read-only value, is
  refused for the same reason the equivalent assignment is.
- A call through a function value passes its arguments as a direct call does. A function value of arity n
  passes the argument's own storage at position i exactly when some free function of arity n writes its
  parameter i, whichever body the call reaches: a body that writes the parameter updates the variable, and
  one that only reads it reads the value held there. After `let f = bump` with `bump` assigning its
  parameter, `f(v)` updates `v` exactly as `bump(v)` does. The argument is held to the rules above. When the
  value is the function or closure a reference names, the callee is that body; a value read out of storage
  (a variable, a field, an element, a capture) or arriving through a parameter or a merge may hold any
  function used as a value whose shape agrees with its function type. A read-only value handed to a position
  the callee may write is **E3159**, reported for every such argument and once for each body that writes it,
  naming that body's parameter, also when a closure captured it; a callback that only reads accepts it. An
  expression that is not storage gives the callee a temporary. A function that forwards a parameter to such a
  callback writes that parameter when any candidate does.
- A callback parameter typed over a type parameter (`f function(T) returns T` in a generic body) holds the
  functions handed in for it, so a read-only value passed to it is refused only where a writing function is
  handed in, and the refusal names that call (`'isBig' writes its parameter 'n', but 'Array.filter' passes it
  the immutable 'item'`).
- A call through an interface follows the same rule as a call through a function value: a requirement's
  position is passed as the argument's storage when any conformer writes it, the caller's `var` is updated,
  and a read-only value there is **E3159**, once for each conformer that writes it (`'Doubler.bump' writes
  its parameter 'n', but 'bump' passes it the immutable 'x'`).
- A variable passed by reference to a parameter whose alias is wider than its own, or that names no alias,
  is range-checked when the call returns.
- Passing storage to a parameter the callee **reassigns**, while another argument of the same call reaches
  that storage, is **E3186** (see [Aliasing Between Arguments](#aliasing-between-arguments)).
- Passing a literal or another expression gives the callee a temporary; its writes have no visible effect.
- A parameter declared at a type parameter — in a method of a generic type or in a generic function — is
  passed by reference on the same terms.

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

A function value fits a function type only by variance. A read-only parameter
may be wider than the type's (the function accepts every value the type's callers pass), a parameter the
function writes by reference must be the same type, and the result may be narrower (everything the function
returns is something the type promises). A bare `int` in the type's declaration is an unranged integer, so
a function over a narrower alias does not fit it. That is checked wherever the value meets the type: a
variable, a global, a field, a tuple or container element, a union payload, an argument and a `return`
are **E3005**, and an `as` cast is **E3009**. A call through a function value is checked against the type
the value is held at, and dispatches to the function it holds.

Omit `returns` for a function type that returns nothing: `typealias Callback = function()`. A function type cannot express `throws`, so a
throwing function cannot be used as a value (**E3101**) — wrap it in a function that handles the error.
`Type.method` written without its call — a static method, an instance method read through its type, or a
static the compiler provides such as `int.fromString` — is **E2078**, and assigning to one (`Ops.twice = f`)
is **E2013**; pass a closure that calls it, `apply(function(n) gives Ops.twice(n), x: 4)`. A function type says
which promise parameters its callee takes: see [Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly).
Function-type aliases are [brands](#generic-instance-and-function-type-aliases-are-brands).

### Capability Qualifiers (`keep`, `sole`, `returns readonly`)

A call through a function value or an interface method has no body the compiler can read, so the boundary
declares what the callee does with its values. The qualifier is written before the type:

| Qualifier | Meaning |
|-----------|---------|
| `keep` on a parameter | the callee keeps the argument where something may write it |
| `sole` on a parameter | the callee consumes the argument and owns it, so it may keep it anywhere |
| `returns sole T` | the result is fresh, reached by no other name |
| `returns readonly T` | the result is read-only |

```maxon
typealias Count = int(0 to 1000)
typealias Sink = function(b keep Box)
typealias Maker = function() returns sole Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Source
	function current() returns readonly Box
end 'Source'
```

- They are written only on a parameter or result of a function-type alias or an
  [interface](#interfaces) requirement, and on the result of a [generic function](#generic-functions). Anywhere
  else — a parameter of a function with a body, a cast, a binding — a qualifier is **E2076**.
- A caller trusts the declaration. An argument to a `keep` parameter follows the rule for a function that keeps
  it, so a `let` read again afterwards is **E3078**. A `sole` parameter consumes its argument: pass a fresh value,
  or a `let` or `var` at its last use (a later use is **E3102**); a read-only value, a parameter, a field of a
  record that stays reachable, or a `var` a [view](#views-view) has seen is **E3078**. The `let` of a
  `returns sole` result is fresh, and a `let` of a `returns readonly` result is read-only.
- A promise parameter of a function type is taken by the callee when it is written `keep` or `sole`, and only
  then: a call through the type moves the promise into the callee, which stores, awaits, cancels or returns it,
  and an unqualified promise parameter stays with the caller, who still owns it after the call. A named
  function used as a value has its own type, so the call honours what the function does. A function value
  converted to an alias whose qualifier disagrees with the function — it takes a promise the alias leaves
  unqualified, or the alias declares `keep` or `sole` on a promise the function only reads — is **E3189**.
- A function value converted to the alias, a conformer's method, or a generic function's body that does more
  than its boundary declares — keeps a parameter not declared `keep` or `sole`, or returns state something may
  write where `returns sole` or `returns readonly` is declared — is **E3189**. A function value typed by one
  alias converts to another, by a `return`, a store or an argument, only when it keeps no parameter the target
  does not declare and declares at least the result capability the target declares.

### Closures

A closure is an anonymous function written `function(parameters) gives expression`:

```maxon
typealias Score = int(i64.min to i64.max)
typealias UnaryOp = function(Score) returns Score

function apply(f UnaryOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	var offset = 10 as Score
	let addOffset = function(n Score) gives n + offset
	offset = 20
	print("{apply(addOffset, x: 5)} {offset}\n")     // 15 20: the closure keeps the offset it captured
	return 0
end 'main'
```

**A closure owns what it captures.** What a capture does depends on what is captured:

| Captured | Effect |
|----------|--------|
| a scalar | copied into the closure; writes on either side do not reach the other |
| a local that holds a record, a `String`, an array or a promise | **moved** into the closure when the closure is created; reading the local afterwards is **E3102** (`its ownership moved to the closure that captures it`) |
| a parameter, `self`, a field or a borrowed value | retained: the closure holds a counted reference to the same record, so a write through the receiver is what the closure reads |
| a name an enclosing closure captured | taken from that closure's captures |

Clone a local before the closure when the function still needs it (`let mine = word.clone()`). A local
captured inside a loop is moved on the first trip, so the second is **E3102**. A capture moves its local
before the call it is an argument of runs, so that call may not also consume the local, take it by
reference or write it as its receiver (**E3102**); a call that only reads it may. Inside a closure, a captured name is the closure's own
copy, so passing it to a parameter the callee writes is **E3159**.

A closure that escapes — stored anywhere or returned — keeps every record it captures, so handing a read-only
value to a function whose parameter such a closure captures is **E3078**. A closure that stays local — it is
only called, where it is made or by a parameter that only calls it — writes what it writes of a parameter it
captures, so handing that function a read-only value is **E3159**. A record [view](#views-view) cannot be
captured (**E3078**); a view of a number can, since the closure captures a copy.

**A closure can go anywhere a value can.** A function value is one reference to a record holding the code
and the captures, so a closure may be returned, stored in a field, a module-level variable, a container or
a union payload, and passed to `async`. A plain function is a record that
lives for the whole program. Copies share: `var g = f` and `f.clone()` hold the same closure, and a function
value is exempt from **E3078**. A module-level `let` or `var` may hold a function value, including a closure
a factory call returns.

**A closure may not be stored into what it captures.** The record would hold the closure and the closure the
record, and neither would ever be released, so storing a closure — directly, or inside a value holding it —
into a record it captures or a record that record reaches is **E3183**. The store is followed through fields,
elements, payloads, tuples, literals, module variables, clones, returned values and the functions a
closure is handed to, in the order the stores run, and each cycle is reported once, at the store that closes
it. Two names for one record that only exist at run time are beyond what the compiler can follow.

- A parameter's type may be omitted when the closure is written directly as a call argument whose parameter
  is declared with a function type: the closure's parameters take that type's parameter types, in order —
  `scores.sort(function(a, b) gives b.compare(a))`. A parameter past that function type's arity is **E2003**,
  and an omitted type anywhere else is **E2015**. When overloads of the callee declare different function
  types at that argument, the closure writes its parameter types. A member called on an undefined
  function's result accepts an untyped closure argument, and the call's
  [E3004](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3004) is the error reported.
- Closure parameters must be used (**E3012**); write `_` for an unused one.
- Inside an instance method a closure may use `self`; elsewhere `self` is **E2001**.
- Assigning to a captured `let` is an error, as it is outside the closure.
- A captured value answers the same fields, methods and enum or union accessors it answers outside the
  closure.

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

var counter = 0 as Tally

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

Writes to a value the function created itself — an array it fills, a string it lowers, a record it builds and
returns — leave it pure; only writes to parameters, globals and records it was handed make it impure. A call through a
closure is judged by the closure's own body when the closure is known where the call is made: a closure
literal, or a named function, handed to a helper that only calls it. A closure the call cannot identify (one
read out of a field, say) makes the caller impure. Releasing a value that can own a green thread, a mailbox or an
operating-system handle (a promise, a file, a socket, a type parameter's value) is an effect of the function that
releases it. A call to an overloaded name is judged by the overload its arguments select.

A function that returns nothing has no result to discard. Destructuring a pure function's tuple result must
keep at least one element (`(_, _) = pure()` is **E3064**). A throwing call is judged the same way: a pure
one discarded as a bare statement inside a [`try` block](#try-blocks) or a
[`test` body](#uncaught-errors-in-tests), where it needs no `try`, is **E3064**.

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

- **The divisor is provably non-zero** — a non-zero literal (`x / 4`), a value whose ranged type excludes 0,
  or a value whose known range excludes 0: the counter of a counted `for` loop between compile-time bounds
  (`i` in `for i in 1 upto 10`), or a `let` computed from such values — and the divide compiles as-is, with
  no check. A `try` on it is **E3055**.
- **The divisor might be zero** — the divide throws, and must be written `try (a / b) otherwise …` (or
  propagated from a function that `throws`), unless a [`try` block](#try-blocks) or a
  [`test` body](#uncaught-errors-in-tests) handles it. A bare divide anywhere else is **E3057**
  (`throwing division requires try`).
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
(**E3066**). Comparing values of different types is **E3005**, two brands of one instance included.

`==` and `!=` call `equals`, and `<`, `<=`, `>` and `>=` call `compare`. A method of either name that takes
one operand besides its receiver may not write that operand or its receiver, directly or through a
function it calls (**E3187**, reported at the method). Operands are only read, so a `let`, a
`var`, a field or an expression compares alike.

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
`-0.0`. Negating an integer alias keeps the alias only where its range is closed under negation; otherwise the result is an unaliased signed value.

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

Both take a **type** and produce a compile-time `Count`.

- `sizeof(T)` is the size of a value of type `T` in bytes: `sizeof(int)` and `sizeof(float)` are `8`,
  `sizeof(bool)` is `1`, and a record type is the size of its fields — except an
  [inline packed record](#inline-packed-records), which is `8` whatever its fields sum to. Inside a
  generic body, `sizeof` of any of its type parameters is the size of that parameter's argument: in
  `type Holder uses T, U`, `sizeof(T)` and `sizeof(U)` each answer for their own argument.
- `countof(T)` is the number of elements a **fixed-size** container type holds: `countof(FixedArray with 3 Int)`
  is `3`. Inside such a container's own body, `countof(Self)` is the count of the instance a method is
  called on, or that a `static function`'s call names. A type with no fixed
  element count — a record, a primitive, a growable `Array` — is refused (**E2015**); ask an `Array` for its
  `count()` instead.
- `countof(T)` is also a constant in a [top-level initializer](#top-level-variables), alone or folded
  (`let last = countof(Vec3) - 1`), and the binding is a `Count`. Such a `let` is a compile-time bound inside a
  function: over `let slots = countof(Vec4)`, `for i in 0 upto slots` proves its counter a
  [`FixedArray` index](#fixed-arrays-and-proven-indices).

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

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
methods, and **every access is bounds-checked** — at run time, or at compile time for a
[`FixedArray` index proven inside its size](#fixed-arrays-and-proven-indices):

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
only for elements stored inline — integers, floats, `bool`, payload-free enums — whose type admits zero. For an
array of records, strings or other managed values, or of an alias whose range excludes 0 (`int(1 to 100)`),
`resize` is **E3106**; grow with `push` or `growFilled(n, value:)` and shrink
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

### Fixed Arrays and Proven Indices

A `FixedArray with N T` holds exactly `N` elements, and `N` is part of its type: `FixedArray with 3 Int`
and `FixedArray with 4 Int` are different types. The compiler therefore answers at each `get` or `set`
whether the index lies inside `0 to N-1`:

- **A proven index makes the access total.** The call cannot throw, is written without `try`, and a `try`
  on it is **E3055**. An index is proven when it is a constant inside the size, a value whose ranged type
  lies inside (`typealias Slot = int(0 to 3) implements ElementIndex` for a `FixedArray with 4`), the
  counter of a counted `for` loop whose bounds are known at compile time and lie inside (literals,
  `countof(T)`, folded constants), or a `let` computed from such values whose computed range lies inside.
- **A constant index at or past the size** is **E3005** (`Value 4 is outside the range of the indices of
  'FixedArray with 4' (int(0 to 3))`).
- **Any other index** is checked at run time and throws `ArrayError.indexOutOfBounds`, so the call needs
  `try`; a bare call is **E3057**, with the hint to give the index a ranged type within `int(0 to N-1)`.
  An `ElementIndex` or `Count` parameter, a `var` stepped by a `while` loop, a call result and a
  reassigned `var` are such indices, and so is every index inside an `extension FixedArray`, where the
  count is known only at run time.

The same known ranges prove a divisor non-zero (see [Division by Zero](#division-by-zero)).

```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Slot = int(0 to 3) implements ElementIndex

function read(v Vec4, at Slot) returns Int
	return v.get(at)                              // proven by the parameter's type
end 'read'

function main() returns ExitCode
	var v = Vec4.create()

	for i in 0 upto countof(Vec4) 'fill'
		v.set(i, value: (i * 10) as Int)          // proven by the loop bounds
	end 'fill'

	print("{read(v, at: 2)} {v.get(3)}\n")        // 20 30
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
**E3013** (`missing return statement`). A `while true` loop that no `break` targets has no end to fall
off, so a function whose `return`s are all inside such a loop is missing none. A function without `returns`
uses a bare `return`, or none.

### Variable Declaration and Assignment

```maxon
var count = 10 as Count
let limit = 20
count = count + limit
```

Assigning to a `let` is **E2013**. Assigning a variable to itself (`x = x`, `p.x = p.x`) has no effect
and is **E3067**. See [Variables](#variables) for the full rules.

### Tuple Assignment

Assign the elements of a tuple to existing `var`s, declare new names in the same pattern, or discard
elements with `_`:

```maxon
var x = 0 as Amount
var y = 0 as Amount
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
var i = 0 as Count
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

The element of a `let` array, map, tuple or `FixedArray` literal keeps the range its literal elements span, so
it fits any alias that admits all of them.

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

In a `for` header a range compiles to a counted loop with no allocation. The counter is immutable inside
the body (assigning it is **E2013**) and takes its type from the bounds: a literal bound adopts the other
bound's alias, so `for i in 0 upto xs.count()` counts in `Count`. Between two literal bounds the counter
is a value whose range is known — `[0, 10)` for `0 upto 10` — and each use is checked at compile time
against the alias it reaches, as a literal is, under the conditions that lead to that use; a `let`
computed from it carries the computed range the same way.

Anywhere else an integer range is
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
- The loop variable cannot be reassigned. It follows its source: read-only all the way down over a
  [read-only](#read-only-values) source, writable through over a `var` one. Handing it to a parameter the
  callee reassigns is **E3159**, which names it a `for` loop variable.
- The loop holds a counted reference to its source, so the body may write the collection. A loop over an
  `Array` or a `String` re-reads the live count each step: a `push` in the body extends the loop, and a
  `clear` ends it.
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
var total = 0 as Count
for i in 0 upto 3 'rows'
	for j in 0 upto 3 'cols'
		if j == 2 'skip'
			continue 'rows'
		end 'skip'

		total = total + i + j
	end 'cols'
end 'rows'
```

A label that names the construct a bare `break` or `continue` already leaves is redundant and is
**E2048**: the innermost loop, or for `break` the `match` it sits in. Use a label only to reach an outer
loop, including from inside a `match` arm (where a bare `break` leaves the `match`).

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
var result = 0 as Count
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

A function that declares `throws` must be able to throw: its body needs a `throw`, a `default throws`, an
`otherwise` that throws, or a bare `try` that passes an error on (see [Error Propagation](#error-propagation)).
A clause with none of these is [E3168](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3168), since every
caller would handle an error that cannot arrive. A handled `try … otherwise`, an `if try`, and a `throw`
caught by a [`try` block](#try-blocks) keep the error inside the function. A method implementing an interface
requirement keeps the clause the requirement fixes (**E3016**).

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

Every call to a throwing function — including a call to a throwing interface method and an `await` of a
throwing promise — is marked with `try`, except inside a [`try` block](#try-blocks) or a
[`test` body](#uncaught-errors-in-tests), whose handler takes it. Anywhere else a call without it is
**E3057** (`throwing function requires try`), naming the function as it was declared
(`'stdlib.Expect.equal'`). `try` is followed by exactly one of:

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
	var total = 0 as Amount
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
- Inside a [test](#uncaught-errors-in-tests), a bare `try` on any error type is allowed, and so is
  leaving the `try` out: an error that reaches it fails the test.

### Try Blocks

A `try` block runs several statements and sends every error to one handler. Inside the block, calls to
throwing functions, calls to throwing interface methods and `await`s of throwing promises need no `try` of
their own:

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

- The block must contain at least one operation that throws (**E3083**).
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
  block's handler. Nested try blocks compose the same way: an error goes to the innermost block around it.
- An explicit `try` covers only the operation that produces its value. A throwing argument, operand, method
  receiver or range bound inside it — `h()` in `try g(h()) otherwise 0` — goes to the block's handler.

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
	Expect.equal(2 + 2, expected: 4)
end 'adds two numbers'
```

- The name may contain any character except `'`, and the `end` label repeats it exactly (**E2008** on a
  mismatch). An empty name is **E2059**.
- A test takes no parameters and has no `returns` clause.
- `test` is a contextual keyword: it opens a declaration only at the top level and only when a quoted name
  follows, so `test` remains usable as a variable or parameter name.

### Test Files

Tests live in files whose names end in **`.maxtest`**. A `test` in any other file is **E2058**, and its
message names the file to rename it to (`pricing.maxon` becomes `pricing.maxtest`). A regular build
compiles `.maxon` files only, and `maxon test` compiles the `*.maxtest` files together with the rest of
the project and a generated entry point — the project needs no `main`.

```text
temperature/
├── temperature.maxon          # the code
└── temperature.maxtest        # its tests
```

### Assertions

Every test implicitly declares `throws TestFailure` (you cannot write the clause yourself). The `Expect`
assertions throw `TestFailure.assertion` when they fail, and a test body calls them without `try`: the test
handles every error its body raises ([Uncaught Errors in Tests](#uncaught-errors-in-tests)), and a
`TestFailure` fails it. Outside a test — in a helper function a test calls — an assertion needs `try` like
any throwing call, and a forgotten one is a compile error (**E3057**), never an assertion whose failure goes
unnoticed.

`temperature.maxon`:

```maxon
export typealias Celsius = int(-273 to 10000)
export typealias Fahrenheit = int(-460 to 18032)

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

`temperature.maxtest`:

```maxon
test 'boiling point converts'
	Expect.equal(toFahrenheit(100), expected: 212)
end 'boiling point converts'

test 'zero is freezing'
	Expect.equal(describe(0), expected: "freezing")
	Expect.startsWith(describe(5), needle: "above")
end 'zero is freezing'

test 'body temperature'
	Expect.equal(toFahrenheit(37), expected: 98, message: "rounds toward zero")
end 'body temperature'
```

```text
$ maxon test temperature --no-timing
temperature/temperature.maxtest:
  ✓ boiling point converts
  ✓ zero is freezing
  ✓ body temperature

 3 pass
 0 fail

 3 tests across 1 file.
```

The comparing matchers are [generic functions](#generic-functions): the type is inferred from the two
arguments, so a value of any alias is passed as it is. `close` takes `AssertedReal` floats. The matchers:

| Matcher | Checks |
|---------|--------|
| `Expect.equal(actual, expected:)`, `Expect.notEqual(actual, expected:)` | any `Equatable` type |
| `Expect.greaterThan(actual, than:)`, `lessThan`, `atLeast`, `atMost` | any `Comparable` type |
| `Expect.close(actual, expected:, within:)` | floats within a tolerance |
| `Expect.isTrue(actual)`, `Expect.isFalse(actual)` | booleans |
| `Expect.contains(haystack, needle:)`, `startsWith`, `endsWith`, `isEmpty` | strings |
| `Expect.fail(message)` | always fails |

Every matcher also takes an optional `message:` that is printed when it fails, and reports the line of the
assertion itself. A failing assertion prints what it expected and what it received — had the last test
expected `99`, the run would report:

```text
FAIL  temperature/temperature.maxtest > body temperature
  FAIL temperature.maxtest:11: Expect.equal
    expected: 99
    received: 98
    message: rounds toward zero
```

The full assertion reference is on the [Testing](STDLIB_REFERENCE.md#testing) page of the standard library.

### Uncaught Errors in Tests

Inside a test body, a call to a throwing function, a call to a throwing interface method and an `await` of
a throwing promise need no `try`: the test handles every error its body raises, of **any** error type — not
only `TestFailure`. Any other error fails that test and reports the error and the line of the operation that
threw it:

```maxon
test 'a missing user throws'
	let name = lookup(0)          // lookup throws LookupError
	Expect.equal(name, expected: "user")
end 'a missing user throws'
```

```text
FAIL  users/lookup.maxtest > a missing user throws
  threw LookupError.notFound
  at lookup.maxtest:2
```

The error is named as the test's file would write it, so a library error whose name the project also
declares is reported as `stdlib.ParseError.invalidFormat`.

- A bare `try` means the same thing, and an `otherwise` clause you write always takes precedence. An
  explicit `try` covers only the operation that produces its value; a throwing argument or operand inside it
  is handled by the test. A [`try` block](#try-blocks) inside the test handles its own body's errors first.
- The rule applies to the test body only. In an ordinary function a throwing call without `try` is still
  **E3057** and a bare `try` on another error type is still **E3059**, and a closure written inside a test
  is an ordinary function.
- A `panic` cannot be caught; the test is reported as crashed.

### Test Diagnostics

| Code | Cause |
|------|-------|
| E2008 | the `end` label does not repeat the test's name |
| E2058 | a `test` declaration outside a `*.maxtest` file |
| E2059 | an empty test name |
| E3057 | an assertion (or other throwing call) without `try` outside a test body — in a helper function, or in a closure written inside a test |
| E3107 | two tests in one file whose names compile to the same symbol — each character outside `A–Z`, `a–z`, `0–9` and `_` becomes `_`, so `'adds two'` and `'adds-two'` collide |

### Running Tests

`maxon test [directory]` discovers every `test` in the project's sources — the directory, or the `sources`
its `.maxproj` target lists, as `maxon build` compiles them — runs them and exits `0` when all pass,
`1` when any fails (or there are none), and `2` when the tests do not compile. `--list` prints the tests
without running them. A test binary's heap is checked for leaks like any program's, and a leaking test is
reported as such. `maxon test <file>.maxtest` runs that one file's tests, compiled with the library tiers
alone. See the [CLI reference](CLI_REFERENCE.md#maxon-test) for its flags.

---

## Namespaces

A Maxon program is every `.maxon` file in a project directory, compiled together. There are no import
statements: a file sees its own declarations, the declarations other files make visible, and the standard
library. A program that is one file also sees the scriptlib packages
([The scriptlib directory](CLI_REFERENCE.md#the-scriptlib-directory)).

### Automatic Derivation

A file's namespace is its directory path relative to the project root, joined with `.`:

| File | Namespace |
|------|-----------|
| `main.maxon` | (none) |
| `utils/helpers.maxon` | `utils` |
| `lib/fmt/integer.maxon` | `lib.fmt` |

Every file in one directory shares that directory's namespace.

A scriptlib package's namespace is `scriptlib.<package>`: `scriptlib/text/Lines.maxon` is in
`scriptlib.text`, and its subdirectories extend it the same way.

### Visibility

Top-level declarations — functions, types, enums, unions, typealiases, variables — are **private to their
file** unless marked: only that file may name them. The name of a type, enum, union or interface is still
the whole program's, so a second declaration of it anywhere is **E3006** (see
[Bare Names and Ambiguity](#bare-names-and-ambiguity)). Three modifiers widen visibility:

| Modifier | Visible to | Checked for unused exports |
|----------|------------|----------------------------|
| *(none)* | the declaring file | — |
| `module` | files in the declaring directory and its subdirectories | yes (**E3094**) |
| `export` | every file | yes (**E3092**, **E3093**) |
| `public` | every file | no |

The same modifiers apply to members inside a type. An unmarked field is private to the type: reading or
writing it anywhere else is **E3014**. An unmarked method or static member is private to the file, like a
top-level declaration: calling it from another file is **E3008**. A typealias declared inside a type body
carries its own modifier the same way: unmarked, it is private to the file, and naming it from another file —
bare or as `Holder.Inner` — is **E3008**. A typealias inside an `interface` has the interface's visibility.
At most one modifier may be written; combining two is **E2001** (`'export' and 'public' cannot be combined`).

**A type hides its members.** Where a type is not visible, nothing of it is: naming it is **E3008**
(**E3088** for a `module` type), and so is reaching a member through a value of it — a value an exported
function returns included. That covers its fields, methods, extension methods, accessors and statics, and
the calls the language makes on the author's behalf: `toString` in an interpolation, `==` and `<`, the
iteration a `for` performs, and a `match` over an enum or union value. A value held at an interface type that
is visible answers that interface's requirements. The standard library follows the same rule: a library type
or interface without `public` is hidden from a program.

**A signature may not name a type less visible than the function itself.** Whoever may call a function has to
be able to name what the call takes and gives back, so every type its parameters, its return type and its
`throws` clause name must carry at least the function's own modifier. For this rule the four tiers are ordered
*(none)* < `module` < `export` < `public`: `public` outranks `export`, so a `public` function may not name an
`export` type. The check is structural — a generic instance's base type and each of its type arguments, a
tuple's elements, and a function typealias's parameter and return types are all asked; a type parameter and a
primitive name no declaration and are asked nothing. A typealias the signature names is walked through, each
name on its right-hand side read as the alias's own file means it, and a type body's inner typealias is held
to its own modifier before its right-hand side is walked. A member is held to its own modifier, whatever its
type's, and an interface's members — its requirements' types included — are held to the interface's. A [service](#services--spawn)'s message is
held to the narrower of its own modifier and its service type's. A `module`, `export` or `public`
binding — a top-level `let` or `var`, or a `static let` or `static var` member — is held to the same rule
for its type, which is the type its initializer gives it, written or inferred, together with any alias the
initializer was spelled through (`50 as Small`, `Tallies.create()`). Naming a narrower type is
[E3167](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3167); the fix is to raise the type to the function's
or binding's tier, or to narrow the function or binding.

### `export`

```maxon
export typealias Score = int(i64.min to i64.max)

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
declaring directory it suggests `module` (**E3093**). These checks run on every program that otherwise
compiles, a one-file program included. The entry point, every target a `.maxproj` file declares and every
task a `.maxtasks` file declares are exempt, because the driver calls them by name. A type an exported or `module` signature or binding names is exempt while that function or
binding is itself referenced from another file: the signature requires the wider tier, so dropping the modifier would only
trade E3092 for [E3167](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3167). Every typealias form —
ranged, function, generic-instance and tuple — is audited, and a use credits exactly the declaration it
means, a use reached only through a called function's signature included.

### `public`

`public` gives exactly the visibility of `export` and declares the symbol **API surface**: something that
exists for callers outside this program, so "nothing here uses it" is not a finding. Library code marks its
surface `public`; the standard library does so throughout.

```maxon
public typealias Length = int(0 to 1000)

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
**E3094**; a name spelled in a generic function nothing instantiates, or in a branch of `#if` the target
excludes, counts as a use.

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

Qualification works for functions and for every kind of type name — typealiases, types, enums, unions and
interfaces (`lib.fmt.format(x)`, `50 as api.Score`, `api.Point.origin()`) — at every position a type name
is written: a declaration, a cast, a construction, a static call's base, a `spawn`'s target, an
`extension`'s target, a `throws`, `implements`, `where` or `extends` clause, and the head of a top-level
constant's initializer. Visibility holds through it: a type the referring file may not name is refused
qualified exactly as it is bare.

A qualified name resolves the same way wherever it is written — an expression, a statement, a `try`, an
`async` call, a `spawn` or a `default`. A chain names a function (`outer.inner.f()`), a type and its member
(`sub.Calc.make()`), or a member type (`lib.Type.Inner`, the typealias `Inner` that `lib`'s `Type`
declares). When a binding in scope — a local, a parameter, or a top-level binding the file sees — has the
name of the chain's head, the chain starts at that binding: with `let util = 4` in the file, `util.f()` is a
method call on `util`. A top-level binding that shares its name with a type yields to the type where the
type declares the member written after it, a static or a static function. A directory may share its name
with a type or a primitive: beside a `type Point`, `Point.helper()` calls the directory `Point`'s `helper`
and `Point.create()` the type's static, and `byte.helper()` calls `byte/`'s `helper`. Reading an
undeclared member of a directory is **E2073**. A call whose qualifier names
no binding, directory or type is [E3004](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3004), and the
message names the qualifier: for `foo.bar.baz()`, nothing declares a binding, directory or type `foo.bar`.

Three qualifiers are reserved. `export.X` names a declaration at the project root, `stdlib.X` one in the
standard library, and `scriptlib.<package>.X` one in a scriptlib package. A source directory that cannot be
written as a qualifier is refused when the program is loaded, **E3182**: one whose name is not an
identifier, such as `my-dir`, and a top-level one named `export`, `stdlib`, `runtime`, `scriptlib` or a
keyword.

### Bare Names and Ambiguity

A bare name resolves when exactly one visible declaration has it. Only a declaration the referring file may
name is a candidate: a file-private function counts only in its own file and a `module` function only
inside its subtree, and the candidate list an error prints names only visible ones. A bare call or a bare
function value takes the type of the declaration it resolves to, and a function-backed enum case's function
is resolved from the file that declares the enum, whichever file reads the case; a case that resolves to no
single declaration is reported in that file.

**A type name** — a typealias of any form, a type, an enum, a union or an interface — that reaches more than
one project declaration is ambiguous, **E3063**, in every file, a file that declares one of them included.
The message lists the spellings that resolve it, a declaration at the project root as `export.Name`:

```text
error E3063: app/main.maxon:7:11: Ambiguous type name 'Score': more than one visible declaration matches it. Qualify it as one of: api.Score, lib.Score
```

In a file whose own declaration is file-private, the message ends `, or rename this file's own declaration`:
renaming is the remedy in the declaring file, and other files qualify.

Only project declarations take part. A project declaration of a library name is what the bare name means in
every project file, so a project's `export typealias StringArray` is the one a bare
`StringArray` names, and the library's stays reachable as `stdlib.StringArray`. The standard library's own
files see only the library's declarations. The same holds for the protocols the library defines —
`Stringable`, `FormattedStringable`, `Hashable`, `Equatable`, `Comparable`, `Cloneable` and `Error`: a
project's own `interface Comparable` is what `implements Comparable` names in project files, while the
library's generics, such as `sort` and `contains`, keep their library constraints. `int`, `float`, `bool`,
`Character`, `String`, arrays and payload-free enums meet the library's protocols. A `b"…"` literal's
element is the type the file's bare `Byte` names when that is an integer alias — the file's own, else the one project alias it sees, else the
library's; a `type Byte` leaves the literal on the library's, and an ambiguous bare `Byte` refuses the
literal with **E3063**.

**A function name** that reaches several declarations resolves to one at the project root, or in an
enclosing directory, over one in a nested directory, and to a project function over a standard-library one.
Otherwise the call is ambiguous, **E3095**, in every form it takes — plain, under `try`, or spawned with
`async` (`Ambiguous bare-name call to 'describe': more than one visible declaration matches it. Qualify it
as one of: alpha.describe, beta.describe`) — worded for a function value or an enum case's backing where
the name is one. A call (`api.format(...)`), a function value (`let f = api.format`) and a function-backed
enum case (`plain = api.format`) all accept the qualified form. A call to a generic function first keeps the
declarations its arguments fit, then the nearest of those: one in the calling file's own directory, then
one at the project root, then any other — so an overload of another arity in a farther directory serves the
calls the nearer one cannot take.

**Only a declaration the file can see counts**, for every kind of name. A project type named like a
primitive (`byte`, say) and declared where a file cannot see it leaves that name the primitive in that file.
What a call knows about its callee comes from the declaration it binds, too: whether the callee never
returns (its body ends in `panic`) or can only throw, and which generic template it instantiates. A function
value (`let f = helper`) is the one declaration of its name the file can see, with that declaration's
parameters and return type, whatever other files declare under the name.

**Every candidate is nameable.** Two type declarations of one name that can both be named from outside
their files may not share a directory: two typealiases are **E3061** and a pair involving a type, enum,
union or interface is **E3006**. Two typealiases of one name in one file are **E3061** too. The declaration
kept is the earlier one in a file, and across files the one in the file whose absolute path, with `/`
separators, sorts first byte by byte; each other one is reported at its name. Declarations in different directories
coexist — a typealias in one and a type in another included — and a file-private typealias coexists with
anything, since only its own file can name it. A type, enum, union or interface name is the whole program's:
two of them sharing a name are **E3006** wherever they sit and whatever their modifiers.

Every typealias a `public` standard-library signature names is itself `public` — an inner one such as
`Array.ElementIterator` included — so a value can always be cast to the alias a library signature asks for
(`x as ElementIndex`). A standard-library typealias with no modifier
is private to its declaring file exactly as anyone's is — `Math.maxon`'s `SeriesTermLimit` is one — and
naming it from another file is **E3008**.

### Multi-Project Workspaces

Several projects can share a workspace. Each is a directory marked by its own `.maxproj` file, and the
directory you build is the one that is compiled:

```text
workspace/
├── project-a/
│   ├── project-a.maxproj    # how project A is built
│   └── main.maxon
└── project-b/
    ├── project-b.maxproj    # how project B is built
    └── main.maxon
```

The projects sit side by side: a `.maxproj` file inside another project's tree is **E2074**. See
[Build System](#build-system) and [Project Structure](CLI_REFERENCE.md#project-structure) for what a
project directory contains.

**The language server checks open files.** It checks a document together with the standard library, and
reads the rest of the project to learn which names its other files declare, so it reports what the build
reports about the names a buffer uses. A diagnostic that only the merged program raises is the build's to
report, and while a buffer is unsaved the name-dependent diagnostics are withheld until it matches disk
again. The build remains the authority.

---

## Concurrency

Maxon has two concurrency tools:

- **`async` / `await`** start a **coroutine** of the current green thread. Coroutines overlap *waiting* — for a
  timer, a socket, a child process — without creating threads.
- **`spawn`** starts a **service**: a green thread of its own, scheduled independently and able to run in
  parallel on another processor. You talk to it through messages.

Both run on the runtime's scheduler, which maps green threads onto a pool of OS threads. There are no
locks or atomics in user code: a value is only ever reachable from one green thread at a time, except
where it is shared read-only — held by a module-level `let`, or published through a
[`default`](#program-wide-defaults--default) or a [`SharedValue`](#sharedvalue--a-live-value-made-at-run-time)
(below).

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
  indirect call, an instance method, or a static that reads its generic type's layout (**E2015**): a
  coroutine is handed its written arguments only. A generic function is started at the instance its
  arguments infer, as a call would be. A [qualified](#qualified-names) callee reads as the direct call
  reads it, at any length: `async sub.Calc.seven()` and `async outer.inner.six()` start what those calls
  name.
- An argument travels as one integer word: an integer, a `bool`, or a managed value's reference. A `float`
  or a value held at an interface type is **E2015**, unless the callee reassigns that parameter — then the
  argument rides a cell the coroutine owns.
- A callee may keep an argument it is handed (move it into a record it returns, for one); a coroutine
  dropped before it runs releases what it was handed instead.
- **The arguments are shared, exactly as an ordinary call shares them.** The caller and its coroutines are
  one strand of one green thread, so the coroutine takes a plain count of its own on each managed argument
  and the caller's binding stays usable: a `var` handed to `async` is read and written after the call, a
  `let` is shared read-only, and one binding other than a promise may be handed twice in one call. A scalar
  is copied. Passing a handle is how a socket or a server is shared with a coroutine.
- **A promise argument moves.** A coroutine can still be running after the frame that started it has ended,
  so an `async` call hands a promise, or a value whose type holds one, only to a parameter the callee takes
  — one it awaits, cancels, stores or returns; for an overloaded callee, the overload the call's arguments
  bind. The coroutine then owns the thread, and the caller's binding is spent. A parameter that only reads it is **E3141**: pass the
  promise to one that takes it, or await it and pass the result. One promise at two arguments of one call is
  **E3102**.
- A parameter the callee reassigns gets storage the coroutine owns, and nothing is written back to the
  caller. A coroutine that writes a parameter is refused a [read-only](#read-only-values) argument
  (**E3159**), as an ordinary call is.
- A closure that captured a binding owns it, so handing that binding to `async` afterwards is **E3102**.
- The callee must be able to wait — call `sleep`, `await`, `Scheduler.yield()`, send a service message with a
  `sendTimeout:`, or perform file, socket or process I/O, directly or through its callees. A function that never yields is **E3073** (`function never
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

Plain `await` on a throwing promise is **E3057**, unless a [`try` block](#try-blocks) or a
[`test` body](#uncaught-errors-in-tests) handles it; `try await` on a promise that cannot throw is **E3055**.
The awaited form may be parenthesized: `try (await p) otherwise …` is `try await p otherwise …`.

### Promises in Collections and Fields

A promise can be stored in a container, a field, a tuple, a union payload or an array literal, captured by a
closure, or held by a generic type, when its type is named: `Promise with T` for a function returning `T`,
and `Promise with (T, E)` for one that also throws `E`. A store takes any promise the storing function owns:

- an `async` spawn, or a service reply received in that function;
- a promise parameter;
- a promise a call returned;
- a promise moved out of storage by `pop`, `remove` or a `match` arm, or yielded by `try … otherwise`.

The store moves the promise: the container or field now owns the thread, and the function's own name for it
is spent (**E3102**). The one condition is that the types agree, and **E3098** names what differs when they
do not: a result type other than the slot's, or an error type the slot drops, changes or adds. A promise
handed to a parameter, or to a container's method such as `Map.upsert`, is held to the same rule. A call
through a function value hands a promise over exactly where the function type says the callee takes it (see
[Capability Qualifiers](#capability-qualifiers-keep-sole-returns-readonly)).

A promise parameter the callee reassigns (`q = async work()`) writes into the caller's own variable: the
callee drops the thread the variable held, the variable takes the new one, and the caller awaits it as any
other promise name. The same holds through a function that only hands the parameter on, and at a position a
function type passes by reference. The argument is a `var` the caller owns: a `let`, a `let` field, a name a
closure captured, or a promise an array still holds is **E3159**. Inside a loop, `await x` after such a call
spends `x`, so the call on the next iteration is **E3102**.

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

	let first = naps.awaitAny()
	print("first finished: index {first}\n")     // index 1

	var sum = 0 as Tag
	for p in naps 'drain'
		sum = sum + await p
	end 'drain'

	print("sum of tags={sum}\n")                  // 6
	return 0
end 'main'
```

**Waiting for the first of several.** `promises.awaitAny()`, on an `Array with Promise with …`, parks until
any promise in the array has finished and returns its index. It consumes nothing: every promise, the winner
included, is still awaited (or dropped) afterwards. An already-finished promise is returned without parking.
`p.isComplete()` asks one promise whether it has finished, without parking or consuming it.

**Reading a promise out of storage.** A promise has one owner, so reading one out of the array or the struct
field that holds it starts a MOVE out of that slot: consuming the read empties the slot, and a read never
consumed stays the container's and is dropped with it. `get(i)`, `first()`, `for p in array`, an array
iterator's `current()` and `peek(n)`, a destructured `for (iter, p) in array.withIterator()`, and a read of a
promise-typed field all name their slot. A read that cannot name one is **E3141**: `last()`, a list's elements,
a `Map`'s values, and a `withIterator()` pair bound whole. `pop` and `remove` move the promise out, and the
caller owns it outright. A member of a type you declare, a `pop` of your own included, is judged by its
body: it moves its element out when every `return` hands back what a library `pop`/`remove` took out of a
field of `self` (directly, through a `var` local bound to one, or through another such member of a field's
type), or a parameter the declaration the call binds takes; one that returns a borrow (`self.items.last()`) is
**E3141**. A static of a generic type that hands back a promise an argument holds — an instance passed in,
or an array, map, tuple or struct reaching one — reads it out of that argument as a method reads its
receiver, so it is **E3141** too, unless the declaration the call binds takes that argument. Reading a promise out of a temporary — `try make().get(0)` — keeps that temporary
alive to the end of the enclosing scope, so the promise outlives the expression it came from.

**Consuming a promise.** `await`, `.cancel()`, a `push` or `set` into another container, a store into a field
or a union case, and passing it to a parameter the callee takes all consume it: each empties the slot the
promise was read out of, and hands the thread either back to the runtime or to its new owner. A callee takes
a promise parameter it awaits, cancels, stores or returns, or hands on to a callee that takes it; a promise
passed to a parameter the callee only reads stays with the caller. A call of `push`, `set`, `insert`,
`upsert`, `append` or `prepend` stores its bare arguments whatever the receiver is, so a function that
hands its promise parameter to such a method takes the promise even where the receiver's own method only
reads it. Where the overloads a call may bind differ about taking it, the overload the
call binds decides. An `async` call is stricter (see [Starting a Coroutine](#starting-a-coroutine)): the
overload it binds must take the promise. A store into a promise field also releases the thread that field was
holding, exactly once. Three refusals follow:

- **E3141** — the slot a read came out of has already been spent, at any of those doors. Two reads of one slot
  hold one promise, so only one of them may be consumed; where the compiler cannot see that both name one slot,
  the program aborts at run time with exit code **118** instead, and so does `p.isComplete()` on such a read.
  An `async` call that hands a promise to a parameter the callee only reads is **E3141** too, and so is a
  store of a conditional expression (`h.p = a if c else b`): store on each branch instead.
- **E3102** — a promise already moved into storage is used again afterwards, or one promise is passed at two
  arguments of one call where a parameter takes it.
- **E2015** — a promise read outside a loop is given away inside it, or inside a `while` condition. Read it
  inside the loop instead.

**Returning a promise.** A function may return a promise it owns — a spawn, a bound spawn, one taken with
`pop`/`remove`, or a promise parameter — and the caller then owns it, to await, cancel, drop or
[store](#promises-in-collections-and-fields). A `return` of
a promise no frame owns is **E3141**: one read out of a container that still holds it, a merge of two
promises (`p if c else q`), or a second name for a parameter.

### Cancellation and Dropped Promises

`promise.cancel()` consumes a promise without waiting for it. A promise that is never awaited is dropped
when it goes out of scope (or is overwritten), which has the same effect.

- A coroutine that has **not started** never runs.
- A coroutine that has **already started** is not interrupted where it stands: it resumes and runs on, and
  its result is discarded. What it may no longer do is **begin** an operation that waits on a far end. Its
  next `TcpClient.connect`, `TcpListener.accept`, `send`, `sendFrom`, `recv`, or `TcpListener.bind` to a
  host **name** throws that operation's own [`NetworkError`](STDLIB_REFERENCE.md#tcpclient) variant at once
  instead of parking for a wake-up nothing will send. `close()` still completes, and so does a `bind` to a
  numeric address. A service send it makes to a full mailbox fails `mailboxFull` at once, with or without a
  `sendTimeout:`, and a send already waiting for room fails `mailboxFull` when the promise is dropped (see
  [Mailboxes and failed sends](#mailboxes-and-failed-sends)).
- A promise held in an array or a field is dropped with its container.

### Yielding

`Scheduler.yield()` gives other runnable work a turn and then continues. It never blocks and uses no timer
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
	try h.bump(3) otherwise ignore                  // a message with no reply
	try h.bump(4) otherwise ignore
	let n = try await h.total() otherwise 0         // a message with a reply
	print("total={n}\n")                            // total=7

	h.shutdown()
	let after = try await h.total() otherwise (e) 'stopped'
		match e 'why'
			mailboxFull then print("mailbox full\n")
			serviceStopped then print("service stopped\n")
		end 'why'
		return 0
	end 'stopped'

	print("unexpected {after}\n")
	return 1
end 'main'
```

- **The same type is still a plain value.** `Calc.create()` without `spawn` is an ordinary record with
  ordinary calls, so a service's logic is unit-testable without threads.
- **Messages.** Every send is a throwing call, because it can fail at the send (see
  [Mailboxes and failed sends](#mailboxes-and-failed-sends)): write `try h.method(…)` with an `otherwise`,
  or propagate it. A send with no `try` is **E3057**, except inside a block-form `try` or a `test` body,
  which take its error.
- **Replies.** A method that returns a value, or throws, delivers a reply. `try await h.method(…)` sends it
  and awaits the reply, and its one `try` covers both: the error it binds has the send's cases
  `mailboxFull` and `serviceStopped` beside the method's own. `let p = try h.method(…)` sends it and keeps
  the reply as a promise, whose error type is the method's own alone: `Promise with (T, E)` for a method
  that throws `E`, `Promise with T` for one that throws nothing, which a plain `await p` resolves. A
  bare `try await` that propagates is refused (**E3059**) when the method throws too, because the two
  errors have no single type to propagate: handle it with `otherwise (e)` and `match e`, or split it into
  `let p = try h.method(…)` and `try await p`.
- **A reply is kept, or discarded on purpose.** A method that returns or throws, sent as a statement, is
  **E3190**. Await the reply, keep it, or discard it on purpose with `_ = try h.method(…)`.
- **Private methods are not messages.** Calling a non-exported method or a static through a handle is
  **E3136**. A service cannot send to itself.
- **A message is as visible as its service, and no more.** Its `export` makes it a message rather than
  widening it past the type, so the types its signature names need only the service type's visibility
  ([E3167](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3167)): a file-private service's messages may name
  file-private types.
- **Shutdown.** `h.shutdown()` closes the mailbox: the messages already queued still run, and then the
  service stops. Every later send fails with `serviceStopped`. It always succeeds, on a full mailbox too.
  Dropping the last handle does the same. A
  queued message always runs, so a reply always resolves.
- **The target** of `spawn` must be a static factory that returns its own type; anything else — including a
  bare `spawn f()`, a qualified chain that names no type, or a chain whose head is a binding in scope — is
  **E3134**, in a `default` as anywhere else. The type may be [qualified](#qualified-names), at any length:
  `spawn sub.Calc.create()` starts a type declared in `sub/`, `spawn outer.inner.Calc.create()` one two
  directories down, and `spawn stdlib.Range.create(1, finish: 4)` the library's `Range` in a program that
  declares its own. A factory the spawning file cannot see is **E3008** (**E3088** for a `module` one).
- **Generic services.** A generic type can be spawned; its handle type is spelled through an alias,
  `typealias StringBoxHandle = Box.handle with String`. A `spawn` on the bare generic name reads its type
  arguments off the factory's arguments; a factory that fixes no type is **E2015**.
- **Overloaded factories.** A `spawn` binds the factory overload its arguments fit, as any call does, and
  that overload must start the service (**E3134** when it returns another type). Two overloads that fit
  equally are **E3007**, and arguments that bind no single overload of a generic factory are **E2015**.
- A refusal of a `spawn`'s target or factory (**E3134**, **E3008**, **E2015**) points at the `spawn`
  keyword.
- Services that `await` each other in a cycle are **E3139**.
- `spawn` is a contextual keyword; it remains usable as a name.

**What crosses a message.** A value sent to a service must not stay reachable from the sender in a way
either side could write, because the two green threads may run at the same time on different processors:

- A local `let` or `var`, a temporary or a literal argument the sender owns is **moved** into the service;
  reading the sender's variable afterwards is **E3102**. Replies are moved too, and so is every argument a
  `spawn`'s factory keeps in the service's state. The handler owns what it was moved, so it may keep it or
  write it.
- A [read-only](#read-only-values) value the sender does not own — a module-level `let`, a `let` field, a
  `SharedValue` or registry read, or a parameter nothing in the sending function writes — is **shared**: its graph is marked shared, so every
  count on it steps atomically, and neither side may write it. Sending a parameter the sending function
  writes, or any shared value the handler writes or keeps, is **E3138**. A borrowed `String` is copied at the
  send.
- A tuple none of whose elements owns heap — `(Amount, Amount)`, say — is a value. When the sender is not its
  one owner, it is copied wherever it crosses: a send, a factory argument the factory keeps,
  `SharedValue.publish` or `Key.register`. The service gets the copy and the sender keeps its own.
- A value with an owner besides the sender that is not read-only — held in a container, read out of a field
  or an element of a `var`, or the source of an enclosing `for` loop — is **E3138**; send a `.clone()`. So is
  an element a member hands back out of a field's container (`return self.queue.pop()`): it has left the
  container, and another owner may still hold it. A
  borrowed factory argument the factory keeps is **E3138** too, a generic factory's argument at a type
  parameter included; one the sender owns but also shares is caught by the run-time check of the
  service's state below (exit code **96**). A field read off a temporary built inside the factory call's own
  argument list crosses, because the `spawn` releases that temporary.
- A `spawn` of a factory declared more than once, whose declarations keep an argument differently, is
  **E2015**; give the factories distinct names.
- A parameter type that cannot cross at all — a promise, a function value, an opaque type parameter — is
  **E3135**. A reply that is part of the service's own state is **E3137**; return a copy. For a generic
  service the reply is judged at the `spawn` that fixes `T`, and a `returns T` message that hands back the
  state is **E3137** there whenever `T` resolves to a managed type; a scalar `T` crosses.
- A value held at an interface type crosses as a message argument, in a service's state and as a reply,
  moved like any other value. A conformer sent at its own type whose graph the runtime cannot walk
  (an OS handle other than a socket, such as a machine pin) is **E3138**; once it is held at the interface type
  it is checked through its witness at the send, and such a conformer aborts with exit code **96**.
- **A socket crosses like any other record, because its box counts its owners.** A `TcpClient`, a
  `TcpListener` or a `TlsStream` that the sender holds alone is moved into a service, sent in a message or
  handed back in a reply; the receiving green thread reads and writes it, and the network poller wakes it
  wherever it runs. One with a second owner — a copy in a container, a second `TcpClient` over the same
  socket — is stopped by the walk below with exit code **96**. A module `let`,
  a `SharedValue` or a registry value that reaches one is **E3163** or **E3171**.
- Before a send, the runtime also checks the value's whole object graph. The graph may reach one record
  several times — two fields, two slots of an array — when every owner of that record is one of those
  references, and the record is walked once however many paths reach it. If some nested record has an owner
  outside the graph, the program prints `fatal error: runtime abort 96 (transferredRecordNotSole)` to
  stderr and exits with code **96** before anything is sent. A reply is checked
  after the handler's locals and the message's arguments are released, so a reply built from them crosses. A
  generic service's reply is checked at the type its `spawn` fixes: a `returns T` message that hands back a
  container or a reference-holding record from the service's own state is **E3137** at the `spawn`, and a
  reply whose graph holds a type the runtime cannot walk (an OS handle other than a socket) is **E3138** there. A generic service
  cannot be spawned over a value held at an interface type (**E2015**).

**Module-level state.** A service handler — and anything it calls — may not read or write a module-level
`var` (**E3143**). Keep a service's state in its own fields and hand results back through replies.

A module-level `let` stays readable. In a program that spawns a service, every `let` record built before
`main` is marked shared once the last global initializer has returned, so every count a handler steps on it
is atomic; a record two `let`s reach counts both as its owners. A `let` whose graph no walk can mark — one
holding an OS handle, a value held at an interface type, or a generic instance with no base layout — is
**E3163** where a message can read it, and legal where only `main` does. A `spawn` a global initializer can
reach is **E3164** (a [`default`](#program-wide-defaults--default) starts a program-wide service before
`main`), a `let` whose initializer reaches a module-level `var` holding a record is **E3165**, and
a `var` whose initializer may take a `let`'s record is **E3078** (see
[Top-Level Variables](#top-level-variables)).

**Output order.** Text printed by `main` and by a service handler may interleave in any order; sequence it
through awaited replies when order matters.

#### Mailboxes and failed sends

A mailbox holds at most its **capacity** of queued messages: `DefaultMailboxCapacity` (1024), or the
`MailboxCapacity` (`int(1 to 2^31)`) that `spawn(capacity: n) Type.factory(args)` names. The capacity is a
ceiling: the mailbox's memory grows with its backlog, up to the capacity.

A send queues the message, or fails with an error the compiler declares for each message,
`<Service>.<message>.sendError`, whose two cases carry the arguments the send was given:

- `mailboxFull(<arguments>)` — the mailbox holds its capacity;
- `serviceStopped(<arguments>)` — the service has been shut down.

**`sendTimeout:`.** Every message call takes a reserved last argument, `sendTimeout: Milliseconds`, which
says how long a send waits for room in a full mailbox. It defaults to 0: a send to a full mailbox fails
`mailboxFull` at once. It may be the call's only argument, `h.ping(sendTimeout: 5000)`. It stays with the
send: the handler receives the message's own arguments alone.

- A send given a positive timeout waits in line behind the senders already waiting on that mailbox. Each
  message the service takes frees a slot, and the first waiting sender's message goes into it, so the
  waiting sends are delivered in the order they began to wait.
- The service shutting down fails every waiting send with `serviceStopped`.
- The timeout passing fails the send with `mailboxFull`.
- `sendTimeout: ForeverMs` waits as long as it takes, untimed: the blocking send of a Go channel.
  When the mailbox stays full and every green thread is waiting, the program stops with exit code
  **92** (see [Exit Codes](#exit-codes)).
- A send in a coroutine whose promise has been dropped (see
  [Cancellation and Dropped Promises](#cancellation-and-dropped-promises)) fails `mailboxFull` at once, and so
  does a waiting one when the promise is dropped. This is the one way a send given `ForeverMs` fails
  `mailboxFull`.
- A message that declares a parameter named `sendTimeout`, and an argument after `sendTimeout:`, are
  **[E3192](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3192)**.

The arguments come back owned by the caller, so they can be sent again, to this service or another. A
read-only value the send shared (see above) comes back as a copy. One whose type has no deep copy comes back
as the shared value itself, which the caller may read and keeps for itself. A message with no parameters
has payload-free cases. Through an
[interface handle](#interface-handles) the error is `<Interface>.<message>.sendError`.

```maxon
typealias Count = int(0 to 1000000)

type Store
	var kept as Count
	var last as String

	static function create() returns Self
		return Self{kept: 0, last: ""}
	end 'create'

	export function keep(line String)
		self.kept = self.kept + 1
		self.last = line
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 64) Store.create()

	for i in 0 upto 1000 'eachLine'
		try h.keep("line {i}", sendTimeout: 50) otherwise (e) 'refused'
			match e 'why'
				mailboxFull(back) then print("dropped {back}\n")
				serviceStopped then return 1
			end 'why'
		end 'refused'
	end 'eachLine'

	return 0
end 'main'
```

- **A full mailbox is the sender's to handle.** Wait for room with `sendTimeout:`, as above, send elsewhere,
  or drop the message. A send with a finite timeout waits a bounded time, so with those the only unbounded
  wait is an `await`, and [E3139](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3139)'s cycle rule keeps the
  program deadlock-free. A `ForeverMs` send is an unbounded wait too: services that fill each other's
  mailboxes with `ForeverMs` sends can deadlock.
- ⚠ **Retrying coroutines can livelock.** A yield runs the green thread's other runnable coroutines first.
  When every coroutine of one green thread retries a send without a timeout to the same full service, at one
  processor with preemption off (`MAXON_MAX_PROCS=1`, `MAXON_PREEMPT=off`) the coroutines hand the processor
  to each other and the service waits for it forever. Give the send a `sendTimeout:`, which parks the
  coroutine until there is room.
- **A send error is propagated** by a bare `try` in a function that `throws <Service>.<message>.sendError`,
  or rethrown with `throw e` from the binding that caught it. Throwing one the function does not solely
  own — a parameter, say — is **E3191**, because its catcher takes the arguments out of it as their only
  owner.
- `otherwise ignore` drops a failed send and releases its arguments.

### Interface Handles

A service type may `implements` an interface, and `I.handle` is a handle to any running service whose type
implements `I`. Each requirement of `I` is a message: a call through an `I.handle` is sent to whichever
service the handle holds, by the same rules as the service's own handle. Its send error is
`I.<requirement>.sendError`, with the same `mailboxFull` and `serviceStopped` cases.

```maxon
typealias Count = int(0 to 1000000)

interface Tally
	function add(by Count)
	function total() returns Count
end 'Tally'

type Counter implements Tally
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Count)
		self.n = self.n + by
	end 'add'

	export function total() returns Count
		return self.n
	end 'total'
end 'Counter'

function report(t Tally.handle) returns Count
	try t.add(5) otherwise ignore
	return try await t.total() otherwise 0
end 'report'

function main() returns ExitCode
	let total = report(spawn Counter.create())
	print("total={total}\n")                       // total=5
	return 0
end 'main'
```

- A `T.handle` converts to `I.handle` wherever an `I.handle` is expected: a binding, an argument (an
  overloaded callee's included), a return, a field, an array element, a message argument, a
  `Key.register` and a call through a function value or a closure. `as I.handle` converts explicitly.
  A value that converts equally well to two overloads' interface handles is ambiguous (**E3007**).
- Every requirement must be sendable: its parameters must cross a message (**E3135**) and its reply must be
  carriable (**E3140**). One send serves every implementer, so the services a program sends to through
  `I.handle` declare each requirement with the same parameter, return and `throws` types
  ([E3177](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3177)); send through each service's own handle
  otherwise.
- A requirement sent through `I.handle` is a message of every service implementing `I`, so each declares it
  as an `export` instance method. Sending one that an implementer declares another way (a private method,
  say), or that no implementer declares, is [E3136](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3136).
- A handler that sends through an `I.handle` is checked against the implementers of `I`, so
  [E3139](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3139) finds an await cycle through any of them.

### Program-Wide Defaults — `default`

A top-level `default Key = expression` declares the program-wide default for `Key`. Any thread reads it with
`Key.current()` and replaces it with `Key.register(x)`; every later `current()` sees the replacement.

- **A service key** is an interface. The expression spawns a type that implements it
  ([E3170](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3170) otherwise), and `Key.current()` returns an
  owned `Key.handle`.
- **A value key** is a type whose graph a share walk can mark: one that reaches no OS handle, function value
  or promise ([E3171](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3171) otherwise). Its fields may be `var`,
  and may hold containers. The expression builds one, and may spawn services for its fields.
  `Key.current()` returns the published record, [read-only](#read-only-values) all the way down: a write
  through it is **E3159**, binding it to a `var` is **E3078**, and `clone()` makes a mutable copy.

```maxon
typealias Count = int(i64.min to i64.max)

interface Greeter
	function greet() returns Count
end 'Greeter'

type Polite implements Greeter
	var calls as Count

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Count
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Polite'

type Config
	export let retries as Count

	static function create(retries Count) returns Self
		return Self{retries: retries}
	end 'create'
end 'Config'

default Greeter = spawn Polite.create()
default Config = Config.create(3)

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	Config.register(Config.create(5))
	print("{answer} {Config.current().retries}\n")     // 7 5
	return 0
end 'main'
```

- **When it is built.** Defaults are built in dependency order after the last global initializer has
  returned and the module's `let`s are marked shared, and before `main`. Only a default some code reaches —
  through its key's `current()` or `register()` — is built, so a default's service runs only in a program
  that uses its key. Defaults whose expressions reach each other are
  [E3172](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3172), and a `current()` or `register()` reachable
  from a global initializer is [E3173](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3173).
- **Precedence.** A program's own `default` for a key replaces the standard library's. Two for one key on
  the same side are [E3169](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3169).
- **A service lookup is a snapshot.** A handle taken before a `register` keeps reaching the service it was
  taken from; that service runs until its last holder drops it, then drains its queue and exits.
- **A value lookup is lock-free**, at the cost of reading a module `let`. A registered value must be solely
  owned by the caller ([E3174](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3174); publish a `.clone()`)
  or a tuple of heap-free elements, which is copied ([Moves and Sharing](#moves-and-sharing)),
  and it is marked shared before any other thread can read it. A value a reader already holds stays valid
  after it is replaced.
- **`Key.register(new, replacing: old)`** publishes `new` only while `old` is still the current value, and
  returns `true` when it did. On `false`, `new` is consumed and dropped. Build the value again from a fresh
  `current()` and retry, so two threads updating one key keep both updates.
- `register` may be called from any thread, a service handler included.
- `current` and `register` belong to the registry, so a key type that declares a static member of either name
  is [E3175](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3175).
- **At exit**, the drain that runs the services' queued messages keeps the defaults live, so a handler may
  still look one up; they are released once every service is idle, and a default's service then drains and
  exits. Module and `static` `let`s are released inside the drain after the services settle, so a handler
  may read them there too.

### `SharedValue` — a Live Value Made at Run Time

`SharedValue with T` is a value key's slot created at run time: one live value that every holder of the cell
reads. `T` follows the value-key rule, or is a `String`, a service handle or a scalar
([E3171](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3171) otherwise).

```maxon
typealias Count = int(0 to 1000)

type Config
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Reader
	let cell as ConfigCell

	static function create(cell ConfigCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function read() returns Count
		return self.cell.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let reader = spawn Reader.create(cell.clone())
	let before = try await reader.read() otherwise 0
	cell.publish(Config.create(6))
	let after = try await reader.read() otherwise 0
	print("{before} {after}\n")                       // 1 6
	return 0
end 'main'
```

- `ConfigCell.create(v)` makes a cell; `current()` reads its value lock-free; `publish(v)` replaces it
  for every holder; `publish(v, replacing: old)` is the conditional form, answering as
  `Key.register(new, replacing: old)` does.
- Readers take a read-only snapshot and a writer publishes a whole new value, so a value a reader holds
  stays valid across later publishes. A value `current()` returns is read-only all the way down, as a value
  key's is. A published value must be solely owned
  ([E3174](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3174)) or a tuple of heap-free elements, which is
  copied ([Moves and Sharing](#moves-and-sharing)).
- The cell is shared by reference: `clone()` hands out another reference to the same value, and a cell
  crosses a message like a service handle.

### I/O and Waiting

Operations that wait **park** their green thread instead of blocking an OS thread: `sleep`, `await`, socket
operations (`TcpClient.connect`, `send`, `recv`, `TcpListener.accept`) and waiting on a child process. A
parked green thread holds no processor, so a server waiting in `accept()` costs nothing while idle.

File and directory operations yield to other work before each call into the operating system and are
started with `async` like any other waiting function (`try await` for the throwing ones such as
`File.readText`). The [standard library reference](STDLIB_REFERENCE.md) documents the file, network and process
APIs.

A failed operation reports the reason its own call into the operating system gave, such as the case of a file
error or the `os error N` of a `SubprocessError`, even when its green thread ran on another OS thread between
that call and the report.

### The Scheduler

- **Green threads.** Every green thread starts on a small stack that grows on demand, so thousands are
  cheap. A stack stops growing at 1 GiB; a recursion past it stops the program with `panic: stack overflow`.
- **Parallelism.** By default the scheduler creates one processor per logical CPU. The environment
  variable `MAXON_MAX_PROCS=N` sets the count exactly, for any decimal `N` from 1 to 2147483647, including a
  count above the CPU count; any other value leaves the default. `Scheduler.processorCount()` answers the resolved count. Services use
  the processors in parallel; an `async`-only program's coroutines stay on the green thread that started
  them.
- **Preemption.** A green thread that has run for 10 ms is stopped at its next function entry, `spawn`, `async` call, service send, `await` or `awaitAny`, or
  when a service takes its next message, and moved
  behind the other runnable work, so a CPU-bound loop cannot starve the rest of the program.
  `MAXON_PREEMPT=off` disables preemption; `on`, or unset, is the default, and any other value makes the
  program exit at start-up with code 116.
- **Coroutines switch only where they wait**: at `await`, `sleep`, `Scheduler.yield()`, I/O, and a service
  send given a `sendTimeout:`, while its mailbox is full.

The [CLI reference](CLI_REFERENCE.md) lists the environment variables a compiled program reads.

### Exit Codes

Each of these aborts writes `fatal error: runtime abort N (name)` to stderr, then exits with code `N`.

| Exit code | Cause |
|-----------|-------|
| 75 | a green thread was neither awaited nor dropped when the program ended |
| 90 | a service's message loop received a message its type does not declare — reachable only if a send the compiler built names no message of the service |
| 92 | deadlock: `main` is unfinished and every green thread waits for good (for example `awaitAny` on an empty array, or a `sendTimeout: ForeverMs` send to a mailbox that stays full) |
| 96 | a service send found a value with a second owner |
| 98 | a service send waiting for room broke the protocol between the sender and the party that wakes it — reachable only through a send the compiler built wrongly |
| 116 | `MAXON_PREEMPT` holds a value other than `on` or `off` |
| 120 | a deep copy reached an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled |

Exit code 70 with `fatal error: <reason>` on stderr is a broken runtime invariant: a bug in the compiler or
its runtime, worth reporting with the program that produced it. One reason marks a limit of the host:
on x64-windows, `fatal error: exception dispatched below green thread stack bottom` ends a program whose
green thread faulted with too little stack left for the operating system's exception dispatch, whose size
grows with the processor's vector state.

Exit code 2 with `fatal error: cannot open standard fds` is the start-up check described under
[Entry Point](#entry-point).

### Targets

Green threads, `async`, `sleep`, `awaitAny` and services run on every native target. `wasm32-wasi` has no
green threads, so each of them is **E3104** there, reported at the call when the language checks find no
other error in the program.

---

## Build System

A Maxon project is a directory of `.maxon` files marked by a **`<name>.maxproj`** project file at its
root. `maxon build <directory>` compiles every source file beneath a directory, and `maxon build` with no
path builds a target the project file describes.

```text
myproject/
├── myproject.maxproj    # the project file: where the project begins, and how it is built
├── myproject.maxtasks   # the tasks `maxon run` offers, if it has any
├── main.maxon           # entry point
├── lib.maxon
├── lib.maxtest          # tests: compiled only by maxon test
└── utils/
    └── math.maxon       # subdirectories are included
```

A build compiles the `.maxon` files. Each other kind of file has its own reader:

- **`.maxproj`** — the project file; `maxon build` runs its targets.
- **`.maxtasks`** — the task file; `maxon run` runs its tasks.
- **`.maxtest`** — test files; [`maxon test`](#testing) compiles them with the project's `.maxon` sources.

A directory walk also skips **any directory containing a `.maxonignore` file**, with everything beneath
it. A `.maxonignore` excludes a directory the walk *discovers*; naming a path explicitly on the command
line — a file or a directory — compiles it whatever marker sits above it or on it. A file named
`project.maxon` or `tasks.maxon` is ordinary source. See
[Project Structure](CLI_REFERENCE.md#project-structure) for the full layout rules.

### A Project File Is a Program

A `.maxproj` file is ordinary Maxon with the whole standard library available. The compiler compiles it
for the host, runs the chosen target, and performs the build that target describes. A build can therefore
**compute** what it compiles — list a directory, choose sources by host, derive a version from git — as
well as spell it out.

**Its targets are its exported (or `public`) functions of no parameters returning `ExitCode`.** A
non-zero return or a crash fails the build before anything is compiled.

```maxon
// hello.maxproj
export function build() returns ExitCode
	Build.build("src")
	return 0
end 'build'
```

A build that states no output is written to `.maxon/<name>`, `<name>` being the project file's own name —
`.maxon/hello` here. A stated output omits the extension: the compiler adds the target's (`.exe` on
Windows, `.wasm` for `wasm32-wasi`, none on Linux and macOS). Relative paths resolve against the project
file's directory.

`maxon build` with no path builds the project's only target. **A project with several targets** names
one on the command line, each `_` of the function's name written `-`:

```maxon
// tools.maxproj
export function app() returns ExitCode
	Build.build("app", output: "out/app", version: "1.2.3")
	return 0
end 'app'

export function gen_tool() returns ExitCode
	Build.build("tool", output: "out/tool", debugInfo: false)
	return 0
end 'gen_tool'
```

`maxon build` with no argument then lists `app` and `gen-tool` and compiles nothing; `maxon build app`
and `maxon build gen-tool` build one each. Every target is an entry point of the project file, so an
exported target is exempt from **E3092**.

**One project per tree.** A `.maxproj` file inside another project's directory tree is **E2074**, reported
from either project; a directory holds one `.maxproj` file at most.

### Describing the Build

`Build` (in the standard library) writes the build description the compiler reads back. A target
describes **one** build; describing a second in the same run is an error.

| Call | Meaning |
|------|---------|
| `Build.build(source, output: "", debugInfo: true, version: "", defines:, rebuildWithOutput: false)` | compile one file or directory to one output |
| `Build.buildWithConfig(config)` | build one `BuildConfig`, whose `sources` may list several files and directories compiled as one program, in order |
| `Build.delegate(directory, target: "")` | hand the whole description to a target of another directory's `.maxproj` file, run there |

- `debugInfo` controls the debug-information sidecar written beside the executable.
- `version` stamps a dotted version into the executable's metadata.
- `defines` is a `StringArray` of `"name=value"` entries, each replacing the written default of a top-level
  `String` constant — the same as `maxon build --define`. This is how a project file passes a value it
  computed, such as a version derived from git, into the program.
- `rebuildWithOutput` runs the written program with the same command line once the build succeeds, as a
  compiler building itself does; see
  [Building again with the written program](CLI_REFERENCE.md#building-again-with-the-written-program).
- `target` names one of the delegated project's targets the way `maxon build` does; empty means its only
  one.

**Several sources in one program** use a `BuildConfig`:

```maxon
export function build() returns ExitCode
	var sources = StringArray.create()
	sources.push("src")
	sources.push("vendor/thirdparty")

	Build.buildWithConfig(BuildConfig.create(sources, output: "out/myprogram"))
	return 0
end 'build'
```

Sources are compiled in exactly the order listed. An empty `sources` list is refused
(`myprogram.maxproj named no sources to compile`), so a build always names what it compiles.

### The Command Line Wins

Flags typed on the command line outrank the described build: `--output=` replaces the output path,
`--target` chooses the target (the project file itself always runs on the host), a `--define` is applied
after the described build's defines, and debug information is written only if both the described build
and the command line allow it.

The [CLI reference](CLI_REFERENCE.md) documents `maxon build` and its flags.

### Tasks

A directory may also hold a **`<name>.maxtasks`** file, and `maxon run <task>` runs one of its exported
no-parameter `ExitCode` functions with the caller's own streams and exit code. The task is named the way
a target is, each `_` written `-`, and `maxon run` alone lists them. It is the same language and the
same standard library as a project file, and the two files divide one job in two:

```maxon
// workspace.maxtasks
export function build() returns ExitCode
	Build.delegate("compiler")
	return 0
end 'build'

export function check_format() returns ExitCode
	return 0
end 'check_format'
```

**A `.maxtasks` file marks nothing**, so it can sit in any directory: what says where a project begins is
a `.maxproj` file. A task may describe a build, as `build` does above, and the compiler performs it once
the task exits 0; with no stated output it is written to `.maxon/<name>`, `<name>` being the task file's
own. Every task is an entry point, exempt from **E3092** like a target.

---

## Memory Model

Maxon manages memory with **reference counting**. There is no garbage collector and no manual `free`: a
record is released when its last reference goes away. Programs cannot:

- use freed memory, or free memory twice;
- access out of bounds;
- read null or uninitialized memory;
- invalidate an iterator;
- race on data.

Three mechanisms carry this:

- **Reference counting is the safety mechanism.** Every read that could outlive its owner is counted, so a
  record is freed only when nothing can reach it.
- **Mutable state belongs to one thread; read-only values may be shared.** `let` is deeply read-only, so a
  read-only value can be shared by any number of holders and threads without copying.
- **Raw memory is reachable only from the library tier** (`stdlib/` and `runtime/`). Programs reach it through
  safe, bounds-checked standard-library APIs.

### Safety Guarantees

| Hazard | What Maxon does |
|--------|-----------------|
| Use after free, double free | Reference counting; a read that could outlive its owner takes its own reference, including across the rest of its statement |
| Iterator invalidation | A `for` loop or cursor holds a counted reference to its source |
| Out-of-range collection index | `get`/`set` throw `ArrayError.indexOutOfBounds`, or the compiler proves a `FixedArray` index inside the size |
| Raw memory | Reachable only from the library tier (**E3185**) |
| Forged values | No integer-to-enum or integer-to-union cast, and no interface-to-interface cast (**E3009**) |
| Null | There is no null: every variable, field and element holds a value |
| Uninitialized fields | Every field must be definitely assigned before its struct literal (**E3086**) |
| Writing through read-only data | Compile error (**E3159**, **E3078**) |
| Use after move | Compile error (**E3102**) |
| Data races | Mutable state is owned by one thread, and only read-only values are shared |
| Stack overflow | Checked function prologues; **E5004** for the unchecked runtime chains; guard pages on OS stacks |
| Value outside a typealias's range | Compile error for a known value, **panic** at run time otherwise |
| Division by zero | Compile error for a constant zero; otherwise the division throws `DivisionByZero` |
| Negative shift count | Compile error for a known count, **panic** at run time otherwise |
| Integer overflow | Wraps (two's complement); a range check catches it where the value reaches a checked place |
| Leaks | Exit code **101** when `main` returns with memory still allocated (see [The Leak Checker](#the-leak-checker)) |

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

	static function create(x Coord, y Coord) returns Point
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

### Ownership and Lifetime

Every record carries a reference count and is freed when the count reaches zero. The compiler decides where
counts are needed. A mistake in that analysis can only add a count, so it costs speed, never safety.

- **Counted reads.** A value read out of a collection (`get`, `first`, a cursor) or out of a field takes its own
  reference whenever something before its last use could release the record it came from. That includes
  whatever its own statement evaluates after it: a later operand, argument or literal element, the right-hand
  side of its own write (`x.a.b = x.dropA()`), a default argument filled after the last written one, and the
  arguments of a method called on it (`x.items.push(x.reset())`). Mutating a collection while an element read
  from it is still in use is therefore safe.
- **Loops hold their source.** A `for` loop or cursor holds a counted reference to the collection it walks, so a
  write to that collection inside the loop cannot free the elements being visited.
- **Moves.** A move hands the reference over without changing the count, and consumes the source binding.
  Reading the source afterwards is **E3102**. Assigning the binding a new value makes it usable again.
- **Plain and atomic counts.** Counts are plain within one thread, and atomic only on values shared between
  threads (see [Threads and Shared Data](#threads-and-shared-data)).

### Read-Only Values

`let` makes a value **read-only all the way down**: nothing reachable through it may be written, by any holder,
for as long as it is read-only. That is what makes sharing by reference safe.

| Read-only root | Read-only through |
|----------------|-------------------|
| a `let` binding | everything it reaches |
| a [`view`](#views-view) binding | everything it reaches, through the view; other names may still write it |
| a `let` field | everything it reaches, for the record's whole life |
| a module-level `let` | everything it reaches, for the program's whole life |
| a value read from a `SharedValue` or a registry | everything it reaches |
| a stored Map key or Set element | itself, while stored |
| an element of a `readonly` element type | itself, while stored |
| a `for` variable, `if let` binding or `match` binding | the same as its source: read-only from a read-only source, writable from a writable one |

The rules:

- **No write through a read-only value.** A field store, a method that writes its receiver, or an argument the
  callee writes is **E3159**. The diagnostic names the read-only root.
- **No mutable alias of a read-only value.** Binding a read-only value to a `var` is **E3078**, and so is a module
  `var` initializer that may take a module-level `let`'s record. A module-level `let`'s record reaching a `var`
  through a ternary, a `match … gives` or a `try … otherwise` is the same **E3078**. Storing a read-only value into a `var` field, a
  mutable container element or module storage, or passing it to a parameter that keeps it where something may
  write it, is **E3078** too. A `let` bound to a fresh record is the one exception, at its last use: the store
  consumes the name, so no `let` sees what the new holder writes. Stored there and read again afterwards, it is
  refused. A last use is judged per path: a store on one arm of an `if`, a `match` or a ternary is the last use on
  that arm when the arm reads the name no more, and so is a store on a branch that then leaves the function
  (`return`, `throw`, `panic`), whatever the code after the `if` reads. A store on an arm that falls through to a
  later read is refused. When a callee both keeps and writes a read-only argument, the keep is the error reported.
  [`clone()`](#explicit-cloning) makes an independent mutable copy.
- **A tuple carries the mark of what it holds.** A tuple literal may hold read-only values, and the tuple is
  read-only wherever it goes, one a call returns included: `let (a, b) = ends(p)` keeps both names read-only.
  Binding the tuple, or one of its read-only elements, to a `var`, assigning it to existing `var` names, or storing it into a
  `var` field or a container is **E3078**, and passing it to a function that writes it is **E3159**.
- **A `let` binds only what nothing writes.** A `let` bound to state something may still write is **E3188**; see
  [Immutable Variables](#immutable-variables-let). A `let` bound from a parameter is as immutable as the caller's
  argument.
- **A `let` field has no other writer.** A value stored into a `let` field must be:
  - already read-only;
  - a value type;
  - or moved in, either as a sole-owned value or as a `var` at its last use.

  A mutable value that is still shared elsewhere is **E3078**. A sole-owned tuple or record is moved in only
  when the records it holds are unwritten too: one that holds a record a `var` of the function, or a module
  `var`, may still write is **E3078**. The same rule applies to a parameter whose callee
  stores it into a `let` field; the compiler reads that from the callee's body.
- **`let b = a`, where `a` is a `var`, moves `a`.** `b` is then the record's only holder, and read-only.
- **Keys are read-only.** A stored Map key or Set element cannot be written through its container, and a key read
  back out is read-only. Storing a key follows the `let`-field rule. Map values and Array elements follow their
  container.
- **Read-only element types.** `Array with readonly T` and `Map with K, readonly V` hold read-only values
  without copying:
  - a read-only value can be stored directly;
  - an element read out is read-only;
  - the container itself stays mutable: elements can still be pushed, replaced and removed.

  As a type argument, `readonly` appears only on a container's element or value type; bindings and fields use
  `let`. `returns readonly` on a boundary with no body is a
  [capability qualifier](#capability-qualifiers-keep-sole-returns-readonly).
- **Value types are exempt.** Scalars, `String`, `Character` and records whose fields are all `let` cannot give a
  stored value a second writer, so none of these rules apply to them. A `String` or `Character` a name does not
  own is copied when it is bound to a `var` or stored.

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	static function create(x Coord) returns Point
		return Point{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var a = Point.create(1)
	a.x = 2
	let frozen = a                       // a moves into frozen, which is read-only
	let shared = frozen                  // shared names frozen's record, read-only
	// frozen.x = 3                      // E3159: 'frozen' is read-only
	// var copy = frozen                 // E3078: 'frozen' is read-only
	var b = frozen.clone()               // an independent, mutable record
	b.x = 5

	var points = PointArray.create()
	points.push(b)                       // the array shares b's record
	var first = try points.get(0) otherwise panic("pushed")
	first.x = 9                          // an element follows its container
	print("{shared.x} {b.x} {first.x}\n")   // 2 9 9
	return 0
end 'main'
```

### Moves and Sharing

| Where a value goes | What happens |
|--------------------|--------------|
| `var b = a`, `a` a `var` | `a` moves into `b` |
| `let b = a`, `a` a `var` | `a` moves into `b`, and `b` is read-only |
| `let b = a`, `a` read-only | `b` shares the record, read-only |
| `let b = e`, `e` state something may still write | **E3188**; `view b = e` reads it, `var b = e` writes it |
| `view b = e` | `b` names the record read-only; other names may still write it |
| `var b = a`, `a` read-only (a `let` included) | **E3078**; `a.clone()` is the mutable copy |
| an ordinary call argument | shared with the callee for the call, counted where needed; a parameter the callee reassigns gets its own storage |
| an argument the callee keeps where something may write it | a fresh `let` at its last use moves; any other read-only value is **E3078** |
| an argument the callee keeps in a `let` field | must be read-only, a value type, or moved (a `var` at its last use) |
| an argument to a [`sole`](#capability-qualifiers-keep-sole-returns-readonly) parameter | moves: a fresh value, or a `let` or `var` at its last use |
| an [`async`](#starting-a-coroutine) call argument | shared like an ordinary call argument: the coroutine runs on the caller's green thread; a parameter the coroutine reassigns gets storage the coroutine owns; a promise moves to the coroutine |
| a [closure](#closures) capture | the captured local moves into the closure; reading it afterwards is **E3102** |
| a [service](#services--spawn) message | a mutable value moves to the service; a read-only value the sender does not own is shared; a tuple of heap-free elements the sender does not solely own is copied; a view, or a record a view declared earlier may reach, is **E3138** |
| a `spawn` factory argument the factory keeps | moves into the service's state; a borrowed `String` or a tuple of heap-free elements the sender does not solely own is copied, and any other borrowed value is **E3138** |
| `SharedValue.publish`, `Key.register` | the value must be solely owned (**E3174**) or a tuple of heap-free elements, which is copied; every reader sees it read-only |

### Aliasing Between Arguments

Reads are counted, so two arguments that reach the same storage are safe in most calls: `s.append(s)` is
legal. The one refused overlap is a **by-reference parameter** whose storage another argument of the same call
also reaches. A by-reference parameter is one the callee reassigns. The refusal is **E3186**; pass a `clone()` as
one of the two. A promise, or a value of an opaque type parameter, passed at two arguments whose parameters
take it moves at the first, so the second is **E3102**.

### Explicit Cloning

`clone()` returns an independent deep copy:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	static function create(x Coord) returns Point
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
cloneable. A collection's `clone()` clones each element it holds, so no record of the copy is a record of the
original, and the copy is solely owned. A generic type's instance clones per instance — a `Map`, a `Set`, a declared `Box with String` —
copying what its type arguments hold. Declare `clone()` yourself for custom behaviour, or for a type holding
an uncloneable field.

A copy of anything that is or holds a promise is **E3141**: a `clone()`, or an array's `slice` or `append`,
reaching a promise through a field, a union payload, a generic instance or an array element. A generic type
whose body copies an `Array with T` field is refused where the program instantiates it with a promise. A
promise owns a green thread exactly one owner may reclaim; `await` it and copy a value holding its result.

A field declared at an [interface](#interfaces) type is cloneable when **every** conformer of that interface
in the program is — the copy runs the conformer the value actually holds, which is not known until the
program runs, so the whole program's conformers are what the compiler checks. A conformer that owns an OS
handle, or a generic type, makes the field uncloneable and the `.clone()` is refused with **E2015**, naming
the field, the interface and the conformer. A container ELEMENT held at an interface type is a separate
matter and is never cloneable: an element slot is one machine word and a value at an interface type is two.

A type that both conforms to an interface and holds a value at that interface type cannot be declared at
all — it is a reference cycle, reported as **E4014** (see the ownership rules in `specs/ownership.md`), so no
decorator of that shape reaches the clone check.

### Threads and Shared Data

- **A green thread and its coroutines run one at a time.** The coroutines a green thread starts with `async` never
  run in parallel with it or with each other. They switch only at `await`, `sleep`, `Scheduler.yield()`, I/O
  and a service send given a `sendTimeout:` to a full mailbox, so their reference counts are plain and sharing between them needs no synchronization.
- **Services own their state.** State that more than one thread uses lives in a [service](#services--spawn) and is
  reached by message. Messages and replies move their mutable arguments. A [view](#views-view) stays on its thread:
  a send refuses a view, and a writable record a view declared earlier in the function may reach.
- **Module `var`s stay on one thread.** A module or static `var` that code running on another thread (a service,
  or anything it calls) can reach is **E3143**.
- **Read-only values are shared.** A module `let` reachable from another thread, a value published through
  `SharedValue` or a registry `default`, and a read-only parameter sent in a message are marked shared before a
  second thread can see them. A shared value uses atomic counts, and nothing can write it, because it is read-only.
- **A shared graph must be walkable.** A `SharedValue` or registry value that holds an OS handle, a function value
  or a promise is **E3171**. A module `let` read by a message that the share walk cannot mark is **E3163**.
- **The send check.** At run time, a mutable value moved to another thread must be solely owned; a value that is
  not stops the program with exit code **96**.

### Raw Memory and the Library Tier

Raw memory is reached only through `__Builtins` and `.managed` / `__ManagedMemory`, and both are available only
to the library tier: source files under `stdlib/` and `runtime/`. In any other code these are **E3185**:

- the whole `__Builtins` namespace;
- `.managed`, `__ManagedMemory` and `__ManagedMemoryCursor`, and the methods of a value of either type;
- `cstr()` and `cstring`;
- `Promise.inner`;
- the raw handle types `__ManagedFile`, `__ManagedDirectory`, `__ManagedSocket`, `__ManagedPages`,
  `__ManagedHandle` and `__ManagedPin`.

The diagnostic names the safe API that replaces the member, where there is one (`Process.id()` for
`__Builtins.currentProcessId`, `HeapStats.slab()` for the slab counters).

`__Raw`, the runtime's machine-level floor, is reachable only from `runtime/` (**E3152**).

Programs use the safe standard-library APIs built on top of these, and each is handle-based and bounds-checked:
`File`, `Directory`, the sockets, `Process`, `Scheduler`, `Clock`, `Console`, `Math`, `HeapStats`, `Debuggee` and
`Sampler`.

The rules that rule out forged and missing values:

- **Casts.** An integer cannot be cast to an enum or a union, and an interface cannot be cast to another
  interface; each is **E3009**. A plain enum value comes from `Enum.fromRawValue(raw)`, which throws
  `noSuchRawValue` for a value no case carries, and a union value from its case constructor.
- **Null.** There is none. A value that may be absent is reached with `try … otherwise`, and an operation that can
  fail `throws`.
- **Initialization.** A struct literal must give every field, and a field a factory assigns must be definitely
  assigned on every path to the literal (**E3086**).

### Stacks

- **Green-thread stacks** are pooled. Each starts small and grows on demand up to 1 GiB; past that the program stops
  with `panic: stack overflow`. Every ordinary function checks its stack in its prologue.
- **Unchecked runtime functions.** A few runtime functions have no prologue check. The build proves that every
  chain of them fits the 928-byte margin below the stack limit, and refuses recursion among them (**E5004**).
- **OS stacks** — each machine's system stack, OS threads, the main stack and the signal stack — have guard pages.
- **On arm64**, a frame large enough to skip past a guard is probed page by page.

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

	static function create(id TokenId) returns Token
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

`HeapStats.scavenge()` is the one call that changes that. It returns the number of bytes whose physical backing
was handed back, and it does two things no other road does:

- **Chunks go back to the page layer, where any size class can take them.** A size class the program has
  finished with is holding memory nothing else can use until this call releases it.
- **Whole 64 KiB granules that nothing is using are decommitted.**

```maxon
// after a burst has been allocated and released
_ = HeapStats.scavenge()            // chunks return to the page layer; nothing is decommitted yet
let released = HeapStats.scavenge() // granules still free are decommitted; `released` is the bytes
```

⚠ **The first call after a population is dropped returns 0.** A granule is released only if it was
already entirely free when the previous call looked at it, so a program that reuses its memory between two
calls never pays a decommit and a re-commit for having been busy. **A caller that wants memory back after a
burst therefore calls it twice.**

The address space is not returned and the reservation is not shrunk — only the physical backing goes. What
the operating system then reports as the process's resident set is its own answer, which is why the figure
here is bytes the allocator asked it to drop rather than bytes observed to leave.

`HeapStats.slab()` reports what the heap holds at any moment as a `SlabUsage`: live, free, cached-free,
parked and committed bytes, and the count of frees made from another thread. Its free and cached-free
figures include the backlog `scavenge()` would return.

### Attributing the Live Heap

`HeapStats.slab()` says how much is out; `HeapStats.census(mode)` says what it is. It walks every live slot once
and returns a `SlabCensus` of 2048 buckets, read back one at a time with `bucketCount(i)` and `bucketBytes(i)`.

| `mode` | Bucket `i` holds |
|--------|------------------|
| `HeapCensusMode.byTag` | the slots whose allocation tag is `i` — available only in a program built with `--debugstream`, which is what puts a tag on a box |
| `HeapCensusMode.bySizeClass` | the slots of size class `i` |

The census's `tallyBytes` is the live bytes the walk itself counted, which is an independent cross-check: the
buckets' sum travels through the table and the tally travels through nothing, so the two figures beside
`slab().live` tell a table fault from a walk fault. Bucket 2046 is the slots whose first word is not a tag — a
string's bytes, an element buffer, a green thread's stack — and bucket 2047 is a tag past the end of the table.
A heap the walk finds inconsistent ends the program with exit code **119** rather than answering.

The compiler's own build uses this through [`--census-by-tag`](CLI_REFERENCE.md#logging).

### Attributing Allocation Churn

A census says what is still held; it cannot see an allocation that was freed before it looked.
`HeapStats.allocationsTagged(i)` and `HeapStats.bytesTagged(i)` answer the other question — how many
allocations tag `i` has made since the process started, and how many bytes they asked for. Nothing subtracts a
free, so both figures only rise and a difference of two readings is the churn between them.

The table is 2048 buckets, like the census's. Bucket 0 is the allocations the compiler had no declared type to
tag, and bucket 2047 collects every tag past the end of the table.

Both are maintained only in a program built with `--debugstream`, which is what puts a tag on a box; in any other
build they answer 0. Summed over every bucket they equal `HeapStats.allocationsMade()`, which is the cross-check
to reach for. The compiler's own build uses them through
[`--allocations-by-tag`](CLI_REFERENCE.md#logging).

### The Leak Checker

Every program that uses the heap checks, when `main` returns, that every allocation was released. If any
was not, the program's exit code becomes **101** (nothing else is printed). Reference counting is managed by
the compiler, so a leak is not something ordinary code is expected to cause; `maxon test` reports a leaking
test as leaked. A green thread that was never
awaited or dropped exits with **75** instead (see [Concurrency](#exit-codes)).

### Memory Diagnostics

| Code | Meaning |
|------|---------|
| [E3009](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3009) | a cast that could forge a value: integer to an enum or a union, or interface to interface |
| [E3078](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3078) | a read-only value bound or stored where it could be written, or kept by a `let` field while another writer exists |
| [E3086](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3086) | a field not definitely assigned before its struct literal |
| [E3102](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3102) | use of a moved value |
| [E3143](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3143) | a module `var` reachable from another thread |
| [E3152](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3152) | `__Raw` used outside `runtime/` |
| [E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159) | a write through a read-only value |
| [E3163](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3163) | a module `let` read by a message that the share walk cannot mark |
| [E3171](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3171) | a shared value whose graph holds an OS handle, a function value or a promise |
| [E3174](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3174) | a published value that is not solely owned |
| [E3185](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3185) | raw memory or a raw handle used outside the library tier |
| [E3186](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3186) | a by-reference parameter that overlaps another argument |
| [E5004](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e5004) | a chain of unchecked runtime functions that can exceed the stack margin |
| exit 96 | a mutable value sent to another thread while not solely owned |
| exit 101 | memory still allocated when `main` returns |

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
	var i = 0 as Count
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

	static function create(done Percent) returns Self
		return Self{done: done}
	end 'create'

	static function parse(text String) returns Self throws PercentError
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

**A `var` started from a bare number**

```maxon
var total = 0                 // E2004: A var cannot start from a bare number: write 'var name = expr as Type'.
```

**Self-assignment**

```maxon
x = x                         // E3067: self-assignment has no effect: 'x = x'
```

**A `var` that never changes**

```maxon
var x = 10 as Count
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
items.push(1)                 // E3159: cannot write through 'items': 'items' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```

**Moving out of a `var`**

```maxon
var a = Point.create(1)
var b = a
b.x = 2
print("{a.x}\n")              // E3102: use of moved value 'a'
```

**Binding a read-only value to a `var`**

```maxon
let a = Point.create(1)
var c = a                     // E3078: cannot assign the read-only 'a' to mutable binding 'c'; use 'let' instead of 'var', or use clone()
```

**Handing a by-reference parameter storage another argument reaches**

```maxon
rebindThenRead(h, inner: h.inner)   // E3186: argument 'h.inner' overlaps argument 'h', which 'rebindThenRead' reassigns (parameter 'h') — …
```

**Reaching raw memory from a program**

```maxon
let pid = __Builtins.currentProcessId()   // E3185: call to '__Builtins.currentProcessId' from outside the library tiers: … — use 'Process.id()'
```

**Reading a local a closure captured**

```maxon
let word = "a word padded long enough to heap allocate {1}"
let f = function() gives word.byteLength() as Tally
print("{word}\n")             // E3102: use of moved value 'word': its ownership moved to the closure that captures it
```

**Storing a closure into what it captures**

```maxon
export function arm()
	self.read = function() gives self.n   // E3183: this closure captures 'self' and is stored into a record 'self' holds, …
end 'arm'
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

**A redundant loop or match label**

```maxon
while i < 3 'loop'
	break 'loop'              // E2048: 'break' with label 'loop' targets its own loop
end 'loop'
```

```maxon
match n 'pick'
	0 then break 'pick'       // E2048: 'break' with label 'pick' targets its own match
	default then fallback()
end 'pick'
```

### Run-Time Behavior

Nothing in Maxon is undefined behaviour. At run time:

| Event | Behavior |
|-------|----------|
| `panic("…")`, a failed range check, a negative shift count | the program prints `panic at <file>:<line>: <message>` and a stack trace to stderr and exits with code **1** |
| a `for` loop or an array cursor's `current()` reading a slot that was never filled | a panic: `empty slot: element <i> of the array was never filled` |
| an array cursor's `current()` past the last element | a panic, as an index out of range |
| a green-thread stack past 1 GiB, or a recursion that overflows an OS stack | a panic: `stack overflow` |
| an index past the end of a collection | `get`/`set` throw `ArrayError.indexOutOfBounds`, handled with `try` |
| division or `mod` by zero | throws `DivisionByZero`, handled with `try` |
| integer overflow | wraps around (two's complement), with no error |
| an allocation never released | exit code **101** |
| a green thread neither awaited nor dropped | exit code **75** |
| a promise consumed through a second read of one container slot or struct field | exit code **118** |
| `HeapStats.census(mode)` walking a heap it cannot describe | exit code **119** |
| a deep copy of an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled | exit code **120** |
| on x64-windows, the green-thread scheduler starting up with a thread-local slot Windows placed past the 64 slots of the thread's own block | exit code **121** |
| a second green thread calling `Scheduler.quiesce()` while another is waiting in it | exit code **123** |
| the operating system refusing a green-thread program's OS thread the event it sleeps on while it waits for a runtime lock | exit code **126** |
| a broken runtime invariant — a bug in the compiler or its runtime | the program prints `fatal error: <reason>` to stderr and exits with code **70** |
| on x64-windows, a fault on a green thread with too little stack left for the operating system's exception dispatch | the program prints `fatal error: exception dispatched below green thread stack bottom` to stderr and exits with code **70** |
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

3. **Pass the first argument positionally and name the rest.** `connect("localhost", port: 8080)`. The
   first argument may be named only when its parameter has a default.

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

13. **Keep tests in `*.maxtest` files.** A test body calls assertions without `try`:

    ```maxon
    test 'adds two numbers'
    	Expect.equal(2 + 2, expected: 4)
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
