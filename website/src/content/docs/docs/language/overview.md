---
title: Overview
description: Program structure, conditional compilation, and the lexical elements of Maxon source.
sidebar:
  order: 1
---

This reference describes the syntax and semantics of the Maxon language. The grammar is in
[BNF_SYNTAX.md](/docs/spec/bnf-syntax/), the standard library in the [standard library reference](/docs/stdlib/), and
the `maxon` command in the [CLI reference](/docs/cli/).

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
and a computed one panics at the `return`. A project run with [`maxon test`](/docs/language/testing/)
needs no `main`; a program compiled with `maxon build` or `maxon execute` without one is **E3001**.

### Files and Projects

- A program is one `.maxon` file or a directory of them, compiled together. There are no `import`
  statements.
- A file contains top-level declarations: functions, types, enums, unions, interfaces, extensions,
  typealiases, variables and — in `*.maxtest` files — tests. There are no top-level statements.
- A file's namespace comes from its directory (see [Namespaces](/docs/language/namespaces/)).
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
Running such a file is [`maxon execute`](/docs/cli/).

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
| `test` | at the top level, followed by a quoted name (see [Testing](/docs/language/testing/)) |
| `spawn` | followed by a type's static factory call (see [Services](/docs/language/async/#services--spawn)) |
| `bits` | followed by `(` on the right-hand side of a typealias |

`__line__` and `__file__` are valid only as parameter defaults (see
[Caller-Location Defaults](/docs/language/functions/#caller-location-defaults-__line__-__file__)).

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
[Initializing From Literals](/docs/language/composite-types/#initializing-from-literals) for literals of
other collection types.
