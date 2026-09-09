---
title: Overview
description: Program structure, conditional compilation, and the lexical elements of Maxon source.
sidebar:
  order: 1
---

### Entry Point
Every Maxon program must have a `main()` function that returns `ExitCode`:

```maxon
function main() returns ExitCode
		return 0
end 'main'
```

The return value becomes the program's exit code (0-255 on Windows).

### File Structure
- One or more function, type, enum, or typealias declarations
- Namespace derived from file path
- Use `export` keyword for cross-file visibility (applies to functions, types, enums, and typealiases)
- Use `module` keyword for directory-scoped visibility (visible to files in the same directory and subdirectories)

### Conditional Compilation

Maxon supports `#if`, `#else`, and `#endif` directives for platform-conditional code. These are evaluated at parse time based on the compilation target.

**Target OS:**
```maxon
#if os(Windows)
	let separator = "\\"
#else
	let separator = "/"
#endif
```

**Target Architecture:**
```maxon
#if arch(x64)
	let pointerSize = 8
#else
	let pointerSize = 8
#endif
```

Supported conditions:
- `os(Windows)`, `os(Linux)`, `os(Macos)`, `os(Wasi)` — match the target operating system
- `arch(x64)`, `arch(arm64)`, `arch(wasm32)` — match the target CPU architecture
- `testing(true)`, `testing(false)` — match whether the code is compiled in test mode

**Boolean operators** (precedence: `or` < `and` < `not`), plus parentheses for grouping:
```maxon
#if not os(Windows)
		// runs on non-Windows targets
#endif

#if os(Linux) or os(Macos)
		// runs on Linux and macOS
#endif

#if os(Linux) and arch(arm64)
		// runs on ARM Linux only
#endif

#if (os(Linux) or os(Macos)) and arch(x64)
		// runs on Linux or macOS, but only on x64
#endif
```

Conditional compilation directives can appear at:
- Top level (around functions, types, variables)
- Inside function bodies (around statements)
- Inside `type`, `enum`, `union`, `interface`, or `extension` bodies (around members)

Nested `#if` blocks are supported.

---

## Lexical Elements

### Comments
```maxon
// Line comment

/* Block comment */

/*
  Multi-line
  block comment
*/
```

### Identifiers

Identifiers name variables, functions, types, and other declarations.

- **Starts with a letter or underscore**: Cannot start with a digit, since the compiler uses the first character to distinguish names from number literals.
- **Alphanumeric content**: After the first character, letters (`a-z`, `A-Z`), digits (`0-9`), and underscores (`_`) are allowed.
- **Case-sensitive**: `myVar` and `MyVar` are different identifiers.
- **Cannot be keywords**: Reserved words like `if`, `for`, `return` cannot be used as identifiers.

```text
identifier = [a-zA-Z_][a-zA-Z0-9_]*
```

### Keywords
```text
and, as, bool, break, byte, continue, cstring, default, else, end, enum, export,
extends, extension, fallthrough, false, float, for, from, function, gives, if,
ignore, implements, in, int, interface, is, let, match, not, of, or, otherwise,
return, returns, self, Self, shl, shr, static, then, throw, throws, to,
true, try, type, typealias, upto, uses, var, where, while, with, xor
```

`module` is a **contextual keyword** — it is recognised as a visibility modifier only when it appears immediately before a declaration token (`function`, `type`, `enum`, `var`, `let`, etc.). In any other position it is a regular identifier, so user code can still use `module` as a parameter or local variable name.

### Literals

**Integer Literals**

Integer literals are 64-bit signed values (range: -9,223,372,036,854,775,808 to 9,223,372,036,854,775,807).

Decimal:
```maxon
42
-17
0
9223372036854775807    // INT64_MAX
```

Hexadecimal (prefix `0x`):
```maxon
0xff
0x1a2b
0x0000000140000000     // Values above 32-bit range
```

Binary (prefix `0b`):
```maxon
0b1010
0b11111111
```

Octal (prefix `0o`):
```maxon
0o777
0o52
```

Underscore separators for readability:
```maxon
1_000_000
0xff_ff
0b1111_0000
0x0000_0001_4000_0000  // Large hex with separators
```

**Byte Values** (cast through a ranged `byte` typealias)
```maxon
typealias Octet = byte(0 to u8.max)
42 as Octet
0xff as Octet
```

**Float Literals** (must contain decimal point)
```maxon
3.14
-2.5
0.0
1.0
```

**Character Literals** (grapheme clusters in single quotes)
```maxon
'A'           // ASCII character (1 byte)
'é'           // Latin with accent (2 bytes)
'中'          // CJK character (3 bytes)
'🎉'          // Emoji (4 bytes)
'\n'          // Escape sequence (newline)
'\t'          // Escape sequence (tab)
'\\'          // Escape sequence (backslash)
'\''          // Escape sequence (single quote)
'\x41'        // Hex escape (character 'A')
'\u00A0'      // Unicode escape (NBSP)
'\u03A3'      // Unicode escape (Greek sigma Σ)
```

Character literals create a `character` type value, which represents an Extended Grapheme Cluster (EGC).
The `character` type may contain multiple UTF-8 bytes.

When a single-codepoint character literal appears in a binary operation with an integer operand, the compiler automatically converts it to its Unicode codepoint value:
```maxon
var cp = 45
if cp == '-' 'check'    // '-' is coerced to 45
	var digit = cp - '0'  // '0' is coerced to 48
end 'check'
```

**String Literals** (double-quoted, null-terminated)
```maxon
"Hello, World!"
"Line1\nLine2"
"Tab\there"
"Quote: \"text\""
"\x48\x69"          // Hex escape ("Hi")
"hello\u0021"       // Unicode escape ("hello!")
```

Escape sequences: `\n` `\t` `\r` `\0` `\\` `\"` `\{` `\}` `\xNN` `\uXXXX`

**String Interpolation** (embed expressions in strings)
```maxon
var name = "World"
print("Hello, {name}!")        // "Hello, World!"

var x = 5
print("{x} * 2 = {x * 2}")     // "5 * 2 = 10"

print("Pi: {3.14159}")         // "Pi: 3.14159"
print("Active: {true}")        // "Active: true"

// Escape braces with backslash
print("Use \{expr\} syntax")   // "Use {expr} syntax"
```

Any expression can be embedded. Built-in types (`int`, `float`, `bool`) are automatically converted to strings. Custom types must implement the `Stringable` interface.

**Format Specifiers** (control output formatting)

Append a format specifier after a colon inside the interpolation braces: `{expr:spec}`

*Integer format specifiers:* `[0][width][type]`
- `0` — pad with zeros instead of spaces
- `width` — minimum output width (right-aligned)
- `type` — `d` decimal (default), `x` lowercase hex, `X` uppercase hex, `b` binary, `o` octal

```maxon
var n = 42
print("{n:04}")      // "0042"  — zero-pad to width 4
print("{n:6}")       // "    42" — space-pad to width 6
print("{n:x}")       // "2a"    — lowercase hex
print("{n:04X}")     // "002A"  — zero-padded uppercase hex

var neg = -42
print("{neg:06}")    // "-00042" — sign comes before padding
```

*Float format specifiers:* `[0][width][.precision]`
- `0` — pad with zeros instead of spaces
- `width` — minimum total output width (right-aligned)
- `.precision` — number of decimal places (max 20)

```maxon
var f = 3.14159
print("{f:.2}")      // "3.14"     — 2 decimal places
print("{f:.4}")      // "3.1416"   — 4 decimal places (rounded)
print("{f:8.2}")     // "    3.14" — width 8, 2 decimal places
```

Custom types can implement `FormattedStringable` to support format specifiers:

```maxon
interface FormattedStringable
		function toString(format String) returns String
end 'FormattedStringable'
```

**Byte String Literals** (create a ByteArray from a string)
```maxon
let bytes = b"hello"           // ByteArray containing [104, 101, 108, 108, 111]
let empty = b""                // Empty ByteArray
let escaped = b"line\n"        // Supports escape sequences
let raw = b"\xFF\x00"          // Hex escape: raw bytes [255, 0]
```

Byte string literals use the `b"..."` prefix to create a `ByteArray` (`Array with Byte`) directly from a string. They support the same escape sequences as regular string literals, including `\xNN` hex escapes for arbitrary byte values (0x00-0xFF) and `\uXXXX` Unicode escapes. This is useful when working with raw bytes or APIs that expect byte arrays.

**Boolean Literals**
```maxon
true
false
```

---
