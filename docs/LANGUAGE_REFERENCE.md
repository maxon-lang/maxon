# Maxon Language Reference

**Version**: 1.0  
**Target Audience**: AI Coding Agents and Developers

This reference provides complete syntax and semantics for the Maxon programming language.

---

## Table of Contents

1. [Program Structure](#program-structure)
   - [Conditional Compilation](#conditional-compilation)
2. [Lexical Elements](#lexical-elements)
3. [Types](#types)
   - [Type Conversions](#type-conversions)
   - [Ranged Type Aliases](#ranged-type-aliases)
4. [Types (Composite)](#types-composite)
   - [Interface Extensions](#interface-extensions)
   - [Conditional Extensions](#conditional-extensions)
   - [Conditional Interface Conformance](#conditional-interface-conformance)
5. [Tuples](#tuples)
6. [Enums](#enums)
7. [Unions](#unions)
8. [Variables](#variables)
9. [Functions](#functions)
   - [Parameter Passing](#parameter-passing)
   - [Function Types and Function-Typed Values](#function-types-and-function-typed-values)
   - [Closures](#closures)
   - [Function Purity and Discarded Results](#function-purity-and-discarded-results)
10. [Expressions](#expressions)
    - [Conditional (Ternary) Expression](#conditional-ternary-expression)
11. [Statements](#statements)
12. [Error Handling](#error-handling)
13. [Namespaces](#namespaces)
14. [Async/Await (Concurrency)](#asyncawait-concurrency)
15. [Standard Library](#standard-library)
    - [FilePath](#filepath)
    - [URL](#url)
    - [CharacterSet](#characterset)
    - [Unicode](#unicode)
    - [String Trimming](#string-trimming)
    - [List](#list)
    - [Networking (TcpClient)](#networking-tcpclient)
    - [Builtin Managed Types](#builtin-managed-types)
16. [Build System](#build-system)
17. [Memory Model](#memory-model)
    - [Reference-by-Default Assignment](#reference-by-default-assignment)
    - [Explicit Cloning](#explicit-cloning)
    - [Cloneable Interface](#cloneable-interface)
    - [Auto-Equatable](#auto-equatable)
    - [Scope Cleanup](#scope-cleanup)

---

## Program Structure

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
- `os(Windows)`, `os(Linux)`, `os(Macos)` — match the target operating system
- `arch(x64)`, `arch(arm64)` — match the target CPU architecture
- `testing(true)`, `testing(false)` — match whether the code is compiled in test mode

**Boolean operators** (precedence: `or` < `and` < `not`):
```maxon
#if not os(Windows)
		// runs on Linux and macOS
#endif

#if os(Linux) or os(Macos)
		// runs on Linux and macOS
#endif

#if os(Linux) and arch(arm64)
		// runs on ARM Linux only
#endif
```

Conditional compilation directives can appear at:
- Top level (around functions, types, variables)
- Inside function bodies (around statements)
- Inside type bodies (around fields and methods)

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

```
identifier = [a-zA-Z_][a-zA-Z0-9_]*
```

### Keywords
```
and, as, bool, break, byte, continue, default, else, end, enum, export,
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

**Byte Values** (use `as byte` cast)
```maxon
42 as byte
0xff as byte
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

## Types

### Type Conversions

**Implicit Conversions**
- `int` → `float` (in mixed arithmetic)
- `character` literal → `int` (in binary operations with an integer operand, coerced to Unicode codepoint value)

**Explicit Conversions** (using `as` operator)

Only safe (widening) casts are allowed. The compiler rejects casts that could lose data:

```maxon
var b = 42 as byte         // int literal 0-255 to byte (OK)
var i = b as int           // byte to int (OK)
var f = b as float         // byte to float (OK)
var g = 100 as float       // int to float (OK)
```

Supported casts:
- `byte` → `int` (widening)
- `byte` → `float` (widening)
- `int` → `float` (widening)
- `int` literal 0-255 → `byte` (compile-time range-checked)

Casts to or from `bool` are not allowed. Narrowing casts (`int` variable → `byte`, `float` → `int`, `float` → `byte`) are not allowed. Attempting an unsupported cast produces error **E3009**.

**Converting floats to integers:**
The `float → int` cast is not supported because it silently truncates. Use explicit functions instead to make your intent clear:
- `trunc(x)` - Truncate toward zero (removes fractional part)
- `round(x)` - Round to nearest integer
- `floor(x)` - Round down to nearest integer
- `ceil(x)` - Round up to nearest integer

Example:
```maxon
var f = 3.7
var i1 = trunc(f)  // 3 (toward zero)
var i2 = round(f)  // 4 (nearest)
var i3 = floor(f)  // 3 (down)
var i4 = ceil(f)   // 4 (up)
```

### Ranged Type Aliases

Every use of `int`, `float`, and `byte` in type positions must go through a `typealias` with mandatory range constraints. This creates a stronger type system where every numeric value has a documented domain. `bool` is exempt from this requirement.

**Restriction in `with` clauses:** Bare primitive types (`int`, `float`, `byte`) cannot be used as type arguments in `with` clauses on `typealias` or `type` declarations. You must create a ranged typealias first. `bool`, `String`, and other struct types are not affected.

```maxon
// INVALID — bare primitives in with clauses
typealias IntArray = Array with int          // ERROR
type IntBox implements Container with int    // ERROR

// VALID — use a ranged typealias
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer      // OK
type IntBox implements Container with Integer // OK
```

**Declaration:**

```maxon
typealias Port = int(0 to 65535)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = int(0 to u8.max)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive:

```maxon
typealias Score = int(0 upto 100)   // 0 to 99
```

**Type-qualified bounds:**

Use `type.min` and `type.max` to reference bounds of specific numeric types:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
```

Supported types: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`.

When both bounds use type qualifiers, they must reference the same type (e.g., `i64.min to i64.max`, not `i8.min to i32.max`). A type-qualified bound paired with a literal must form a natural range — `0 to u32.max` is valid, but `0 to i64.max` is an error (use `i64.min to i64.max` or `0 to u64.max` instead). A negative-literal lower paired with `u64.max` upper (e.g., `int(-1 to u64.max)`) is also rejected — no single 64-bit type can represent both ends; use `i64.min to i64.max` or `0 to u64.max`. Byte ranges must have bounds within 0 to u8.max.

**Range identifiers as expressions:**

`type.min` and `type.max` can also be used as expressions anywhere an integer literal is valid — in variable assignments, comparisons, arithmetic, function arguments, etc.:

```maxon
var x = u16.max            // 65535
if value == i32.max 'check'
	// ...
end 'check'
var y = u8.max + 1         // 256
```

**Construction:**

Cast a value into a ranged type with `as`:

```maxon
typealias Port = int(0 to 65535)
var p = 8080 as Port
```

In most cases the cast is unnecessary — when a literal flows into a slot whose type is already a ranged alias (a parameter, a struct field, a function return), the literal is checked against that target type directly. Use `as` when the target type needs to be visible at the use site, or when narrowing a wider value to a smaller range.

**Compile-time range checks:**

Literal values are checked at compile time. This is a compile error:

```maxon
typealias SmallInt = int(0 to 10)
var x = 15 as SmallInt   // error: Value 15 is outside the range of 'SmallInt'
```

**Runtime range checks:**

When the value is a computed expression, a runtime range check is emitted that panics on violation:

```maxon
typealias Port = int(0 to 65535)
typealias RawValue = int(i64.min to i64.max)
function makePort(n RawValue) returns RawValue
	var p = n as Port   // runtime check: panics if n < 0 or n > 65535
	return p
end 'makePort'
```

**Return value range checks:**

Functions with a ranged return type have their return values checked:
- Returning a literal outside the range is a compile error
- Returning a computed expression emits a runtime range check
- Types whose range covers the full representation (e.g., `ExitCode`) are exempt

```maxon
typealias Score = int(0 to 100)

function half(s Score) returns Score
	return s / 2    // runtime range check on return value
end 'half'
```

**Arithmetic:**

Ranged types support standard arithmetic. The result of arithmetic between ranged values is the underlying primitive type:

```maxon
typealias Score = int(0 to 100)
var a = 30 as Score
var b = 12 as Score
var sum = a + b    // result is int
```

All arithmetic on ranged integer types uses 64-bit operations regardless of storage type.

**Storage:**

The compiler automatically selects the smallest x86-optimal integer width that can represent the declared range for storage in arrays and global variables. All arithmetic still uses 64-bit operations.

| Range fits in | Storage used |
|---------------|-------------|
| 0 to u8.max | u8 (1 byte) |
| -128 to 127 | i8 (1 byte) |
| 0 to 65535 | u16 (2 bytes) |
| -32768 to 32767 | i16 (2 bytes) |
| 0 to 4294967295 | u32 (4 bytes) |
| -2147483648 to 2147483647 | i32 (4 bytes) |
| anything wider | i64 (8 bytes) |

```maxon
typealias Pixel = int(0 to 65535)        // stored as u16 in arrays and globals
typealias Delta = int(-32768 to 32767)   // stored as i16 in arrays and globals
typealias Percent = int(0 to 100)        // stored as u8 in arrays and globals
```

Local variables always use 64-bit registers regardless of the ranged type's storage class.

**Standard library aliases:**

The standard library exports a small set of cross-cutting aliases that don't belong to any one domain:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | platform-dependent | Process exit codes |
| `HashValue` | `u32` | Hash function results |
| `Codepoint` | `int(0 to 1114111)` | Unicode codepoints |

Domain-specific quantities (counts, indices, byte offsets, math values) are declared as typealiases inside the module they belong to — for example `String` exports `ByteCount` and `GraphemeCount`, `Math` exports `Real`, and `Array` keeps `ElementCount`/`ElementIndex` private. Application code should follow the same pattern: declare a typealias that names the *purpose* (e.g. `Tally`, `BytePos`, `Coord`) rather than reaching for a generic `Count`/`Index`.

**Assignment and rebinding:**

Assigning one ranged integer variable to another initially creates an alias — both variables refer to the same underlying value. However, reassigning with arithmetic produces a **new value** and rebinds the variable without affecting the original:

```maxon
typealias Pos = int(0 to i64.max)
var startPos = Pos{10}
var pos = startPos      // pos and startPos initially share the same value

pos = pos + 1           // rebinds pos to a new value (11) -- startPos is unaffected
print("{startPos}")     // 10 -- startPos is unchanged
print("{pos}")          // 11
```

This behavior means that using a ranged integer as a loop cursor is safe — advancing `pos` never mutates `startPos`:

```maxon
function skipSpaces(src ByteArray, startPos Pos) returns Pos
		var pos = startPos          // pos starts at the same value as startPos
		while pos < src.length 'loop'
				if src[pos] != b' ' 'notSpace'
						break 'loop'
				end 'notSpace'
				pos = pos + 1           // advances pos; startPos is unaffected
		end 'loop'
		return pos
end 'skipSpaces'
```

This is in contrast to struct assignment, where field mutations through an alias affect the original. See [Reference-by-Default Assignment](#reference-by-default-assignment).

---

## Types (Composite)

### Declaration
Types are user-defined composite types containing named fields. Use `var` for mutable fields and `let` for immutable fields:

```maxon
type Point
		var x int
		var y int
end 'Point'
```

### Type Literals
Create type instances using field initializers:

```maxon
var p = Point{x: 10, y: 20}
var origin = Point{x: 0, y: 0}
```

### Required Field Initialization

Every field of a type must be initialized when the type is constructed. A field counts as initialized if any of the following is true:

1. **The declaration supplies a default value**: `var count = 0`. Two forms are accepted:
   - **Shorthand** — `var name = literal`. The type is inferred from the literal, which must be an integer, float, `true`/`false`, or an enum case (`Priority.low`).
   - **Full form** — `var name Type = expression`. The type annotation is required whenever the default is something other than a literal. The expression can be any valid expression and is re-evaluated at every struct literal that omits the field: `var items IntArray = IntArray.create()`, `var origin Point = Point.create(0, y: 0)`.
2. **The literal provides the field**: `Counter{count: 5}`. A value provided here always wins over a declared default (the default expression is not evaluated when the field is provided).
3. **The literal is the direct return expression of a `static` factory** whose return type is the enclosing type, and the field is assigned via `self.field = expr` on every control-flow path that reaches the literal. In that case the field can be omitted from the literal. Prefer rule 1 (field default) when the value doesn't depend on factory parameters; reach for rule 3 when the default needs access to `create`'s arguments.

A `Self{}` literal is only legal when every field has a default or is supplied via rule 3. Otherwise the compiler emits **E3086 `SemanticFieldNotInitialized`** listing the uninitialized fields.

```maxon
type Counter
	export var value Integer
	export var version = 0           // default

	export static function create(initial Integer) returns Self
		self.value = initial          // proof of initialization (rule 3)
		return Self{}                 // OK: value proven by self-assign; version defaulted
	end 'create'
end 'Counter'
```

The self-assignment form requires **definite assignment**: the write must reach the return on every control-flow path. A write in only one branch of an `if/else`, or only inside a loop body (which may execute zero times), is not sufficient and triggers E3086.

### Field Access
Access fields using dot notation:

```maxon
var p = Point{x: 10, y: 20}
var xVal = p.x           // Read field
p.x = 15                 // Write field (if var, not let)
```

### Methods

Methods are defined **inside the type body** and can access fields directly (implicit `self`):

```maxon
type Point
		var x int
		var y int

		function add(other Point) returns Point
				return Point{x: x + other.x, y: y + other.y}
		end 'add'

		export function magnitude() returns float
				return sqrt((x * x + y * y) as float)
		end 'magnitude'
end 'Point'
```

**Method Syntax Rules:**
- Methods must be declared inside the type body
- Methods access type fields directly without explicit `self`
- Use `export` keyword before `function` to export individual methods
- Methods are called using dot notation: `instance.method(args)`
- A local declaration inside an instance method (`let`/`var`, parameter, match-pattern binding, tuple destructure, for-in loop variable, try/otherwise error binding) MUST NOT collide with a self-field name. Such a shadow is rejected at parse time with **E3006** because reads and writes of the local would silently route through `self.field` and produce type confusion when the local's type differs from the field's type.
- `self` is a reserved identifier and cannot be bound by user code in any declaration (parameter, `let`/`var`, `for`-in variable, function name, type name, etc.). Lexer-strict positions reject it with **E2010**; positions that accept keyword-shaped names reject it with **E2051**. This prevents accidental shadowing of the implicit receiver.

### Calling Methods

Methods are called using dot notation on instances. The receiver (`self`) is implicit:

```maxon
var p1 = Point{x: 10, y: 20}
var p2 = Point{x: 5, y: 10}
var p3 = p1.add(p2)
var mag = p1.magnitude()
```

#### Sibling Method Calls

Inside a type body, instance methods can call other instance methods on `self` using a bare name (no explicit receiver):

```maxon
type Calculator
	var base Integer

	function double() returns Integer
		return base * 2
	end 'double'

	function quadruple() returns Integer
		return double() * 2    // bare call — implicitly calls self.double()
	end 'quadruple'
end 'Calculator'
```

The compiler detects that `double` is an instance method of the current type and automatically prepends `self` as the receiver.

### Static Methods

Static methods belong to a type but don't have access to instance data. They are declared with the `static` keyword and called using `TypeName.method()` syntax:

```maxon
type Point
		var x int
		var y int

		static function origin() returns Point
				return Point{x: 0, y: 0}
		end 'origin'

		static function create(x int, y int) returns Point
				return Point{x: x, y: y}
		end 'create'

		function magnitude() returns float
				return sqrt((x * x + y * y) as float)
		end 'magnitude'
end 'Point'
```

**Calling Static Methods:**
```maxon
var p1 = Point.origin()           // Static method call
var p2 = Point.create(10, y: 20)  // First positional, second named
var mag = p2.magnitude()          // Instance method call
```

**Static Method Rules:**
- Declared with `static function` inside a type body
- No implicit `self` parameter - cannot access instance fields
- Called on the type name, not on instances: `TypeName.method()`
- Can be exported with `export static function`
- Commonly used for factory methods and utility functions

**Differences from Instance Methods:**

| Feature | Instance Method | Static Method |
|---------|----------------|---------------|
| Has `self` | Yes (implicit) | No |
| Can access fields | Yes | No |
| Call syntax | `instance.method()` | `Type.method()` |
| Declaration | `function name()` | `static function name()` |

### Static Fields

Static fields are shared across all instances of a type. They are declared using `static var` (mutable) or `static let` (immutable):

```maxon
type Counter
		static var count = 0
		static let MAX_COUNT = 1000

		var id int

		static function create() returns Counter
				Counter.count = Counter.count + 1
				return Counter{id: Counter.count}
		end 'create'
end 'Counter'
```

**Accessing Static Fields:**
```maxon
var c1 = Counter.create()    // Counter.count becomes 1
var c2 = Counter.create()    // Counter.count becomes 2
print(Counter.count)         // Prints: 2
print(Counter.MAX_COUNT)     // Prints: 1000
```

**Static Field Rules:**
- Declared with `static var` or `static let` inside a type body
- Must have an initializer value (no uninitialized static fields)
- Accessed using `TypeName.fieldName` syntax (not instance syntax)
- `static var` fields can be reassigned; `static let` fields are immutable

**Initialization behavior** depends on the initializer expression:
- **Constant initializers** (integer, float, bool literals) are evaluated at compile time
- **Complex initializers** (function calls, struct literals, array literals) are evaluated **lazily on first access** -- see [Lazy Static Initializers](#lazy-static-initializers) below

**Differences from Instance Fields:**

| Feature | Instance Field | Static Field |
|---------|---------------|--------------|
| Storage | Per instance | One per type |
| Access | `instance.field` | `Type.field` |
| Declaration | `var field type` | `static var field = value` |
| Requires initializer | No (can use type default) | Yes |

### Lazy Static Initializers

Static fields initialized with complex expressions -- function calls, struct literals, or array literals -- are evaluated lazily. The initializer runs the first time the field is accessed, and the result is cached for all subsequent accesses.

```maxon
typealias Tally = int(0 to u64.max)

type Config
		static var instance = Config.create()

		static function _create() returns Config
				return Config{value: 42}
		end '_create'

		export var value Tally

		export static function instance() returns Config
				return Config.instance   // initializer runs on first call only
		end 'instance'
end 'Config'
```

**Lazy initialization guarantees:**
- The initializer executes exactly once, on the first access to the static field
- Subsequent accesses return the cached value without re-evaluating the initializer
- `static var` fields can be reassigned after initialization; the initializer does not run again
- `static let` fields are immutable after initialization (planned)
- Constant initializers (integer, float, bool literals) remain compile-time constants and are not lazy

**Common patterns:**

Caching expensive computations:
```maxon
type WSCache
		static var ws = CharacterSet.whitespacesAndNewlines()

		export static function isWhitespace(c Character) returns bool
				return WSCache.ws.contains(c)
		end 'isWhitespace'
end 'WSCache'
```

Struct literal defaults:
```maxon
typealias Coord = int(0 to u64.max)

type Point
		export var x Coord
		export var y Coord
end 'Point'

type Defaults
		static var origin = Point{x: 0, y: 0}

		export static function getOrigin() returns Point
				return Defaults.origin
		end 'getOrigin'
end 'Defaults'
```

Array literal initialization:
```maxon
typealias Integer = int(i64.min to i64.max)

type Lookup
		static var values = [10, 20, 30]

		export static function get(index Integer) returns Integer
				return try Lookup.values.get(index) otherwise -1
		end 'get'
end 'Lookup'
```

Multiple lazy statics in the same type are each initialized independently on first access:
```maxon
typealias Tally = int(0 to u64.max)

type Cache
		static var a = Cache.buildA()
		static var b = Cache.buildB()
		export var n Tally

		static function _buildA() returns Cache
				return Cache{n: 10}
		end '_buildA'

		static function _buildB() returns Cache
				return Cache{n: 20}
		end '_buildB'
end 'Cache'
```

### Interfaces

Interfaces define a set of method signatures that types can implement:

```maxon
interface Hashable
		function hash() returns int
end 'Hashable'
```

Structs declare conformance using the `implements` keyword:

```maxon
type Point implements Hashable
		var x int
		var y int

		function hash() returns int
				return x + y * 31
		end 'hash'
end 'Point'
```

**Static Interface Methods**

Interfaces can declare static methods using the `static` keyword. Static interface methods don't receive an implicit `self` parameter and are typically used for factory methods:

**Interface Notes:**
- `Self` in interface method parameters/returns refers to the conforming type
- A type can conform to multiple interfaces: `type Foo implements A, B`
- Methods implementing interface requirements follow the same syntax as regular methods
- Static interface methods use `static function method()` syntax in implementations
- One interface can extend another with `interface Derived extends Base`. A type that lists `implements Derived` must declare every method from `Derived` and every method transitively inherited from `Base`; missing methods inherited via `extends` are reported with a `(from BaseName)` suffix in the diagnostic

**Interface-Typed Parameters**

Functions can use interface types directly as parameter types. Any concrete type that implements the interface can be passed as an argument:

```maxon
interface Drawable
	function draw() returns Integer
end 'Drawable'

function render(item Drawable) returns Integer
	return item.draw()
end 'render'
```

The compiler monomorphizes the function at compile time, creating specialized copies for each concrete type used at call sites. This provides static dispatch with no runtime overhead. If the argument's type does not implement the required interface, a compile error is reported. Interface inheritance is respected: a type implementing a derived interface also satisfies parameters typed with the base interface.

**Interface-Typed Return Values**

Functions can declare an interface as their return type. When every `return` in the body yields the same concrete implementing type, the compiler statically infers that type at the call site so chained method dispatch on the result resolves without runtime overhead:

```maxon
interface Producer
	function produce() returns Integer
end 'Producer'

type Widget implements Producer
	let value Integer
	function produce() returns Integer
		return value
	end 'produce'
end 'Widget'

function makeProducer() returns Producer
	return Widget{value: 42}
end 'makeProducer'
```

**Interface-Typed Fields**

Struct fields can declare an interface as their type. The field stores any value of a type that conforms to the interface, and methods invoked on the field dispatch to the implementing type. When the compiler can trace the concrete type stored into the field at construction, dispatch is resolved statically with no runtime overhead:

```maxon
interface Tagged
	function tag() returns Integer
end 'Tagged'

type Holder
	export let t Tagged

	static function create(t Tagged) returns Self
		return Self{t: t}
	end 'create'
end 'Holder'
```

### Where Clauses (Type Parameter Constraints)

The `where` clause constrains type parameters to require specific interface conformance. This enables the compiler to verify method calls on type parameters and to reject concrete types that don't satisfy the constraints.

```maxon
type Map uses Key, Value implements BuiltinDictionaryLiteral where Key is Hashable
		// Key is guaranteed to have hash() method
end 'Map'
```

Multiple interfaces on the same parameter use `and`:

```maxon
type Container uses T where T is Hashable and Equatable
```

Multiple constrained parameters use comma separation:

```maxon
type Pair uses A, B where A is Hashable, B is Cloneable
```

When creating a type alias, the compiler checks that concrete types satisfy the constraints:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringMap = Map with (String, Integer)  // OK: String implements Hashable
```

### Per-Instance Typealiases

A ranged typealias declared inside a generic type body produces a nominally distinct type for each concrete instantiation. This prevents accidentally mixing values between different instances of the same generic type.

```maxon
type Pool uses T
	export typealias Idx = int(0 to u64.max)

	export function push(item T) returns Idx
		// ...
	end 'push'

	export function get(index Idx) returns T
		// ...
	end 'get'
end 'Pool'

typealias Integer = int(i64.min to i64.max)
typealias PoolA = Pool with Integer
typealias PoolB = Pool with Integer
```

`PoolA.Idx` and `PoolB.Idx` are distinct types — passing one where the other is expected produces a compile error. Literal integers that fit the range are still accepted. To explicitly convert between compatible per-instance aliases, use `as`:

```text
let bIdx = aIdx as PoolB.Idx
```

Dot-syntax `as` casts are also supported:

```text
let idx = 0 as PoolA.Idx
```

### Interface Extensions

Extensions add methods to interfaces that are automatically available on all types conforming to that interface. Unlike regular interface methods that each conforming type must implement, extension methods have a single implementation that works for all conformers.

**Declaration:**

```maxon
extension Iterable
	function count() returns int
		var n = 0
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'count'
end 'Iterable'
```

**How Extensions Work:**
- The method becomes available on all types that conform to the interface
- The `self` keyword refers to the concrete type instance
- Extension methods can call any method required by the interface
- Associated types from the interface are resolved to the concrete type's bindings

**Using Associated Types:**

Extensions can use the interface's associated types. These are automatically substituted with the concrete type's associated type bindings:

```maxon
interface Container uses Element
	function get(index int) returns Element
end 'Container'

extension Container
	function first() returns Element
		return self.get(0)
	end 'first'
end 'Container'
```

When called on a type like `IntArray implements Container with Integer` (where `Integer` is a ranged typealias for `int`), the return type `Element` becomes `Integer`.

**Extension Method Synthesis:**

When a type conforms to an interface that has extensions, the compiler synthesizes concrete methods for that type. For example, if `IntArray` conforms to `Iterable`, calling `myArray.count()` invokes a method specialized for `IntArray`.

**Extension Rules:**
- Declared with `extension InterfaceName ... end 'InterfaceName'`
- Methods use `self` to access the conforming type instance
- Associated types resolve to the concrete type's bindings
- Extensions from parent interfaces are applied transitively

### Conditional Extensions

Extensions can include a `where` clause to restrict which conforming types receive the extension methods. Only types whose associated type bindings satisfy the constraints will have the methods synthesized.

**Syntax:**

```maxon
extension Iterable where Element is Equatable
	function contains(element Element) returns bool
		for item in self 'loop'
			if item == element 'found'
				return true
			end 'found'
		end 'loop'
		return false
	end 'contains'
end 'Iterable'
```

The `where` clause follows the same syntax as type-level where clauses: `where TypeParam is Interface`, with `and` for multiple interfaces on the same parameter.

**Behavior:**

When a type conforms to the extended interface, the compiler checks whether the type's associated type bindings satisfy the `where` constraints:
- If they do, the extension methods are synthesized for that type
- If they don't, the methods are silently skipped (no error)

For example, `Array with Integer` (where `Integer` is a ranged typealias for `int`) conforms to `Iterable`. Since `Integer` implements `Equatable`, the `contains` method is available on `Array with Integer`. A hypothetical `Array with SomeNonEquatableType` would not receive the `contains` method.

**Multiple Constraints:**

Multiple constraints on the same parameter use `and`:

```maxon
extension Container where Key is Hashable and Equatable
	// Methods available only when Key is both Hashable and Equatable
end 'Container'
```

**Mixing Unconditional and Conditional Extensions:**

An interface can have both unconditional extensions and conditional extensions. Types that don't satisfy the `where` clause still receive the unconditional extension methods:

```maxon
extension Seq
	function countItems() returns int
		var n = 0
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'countItems'
end 'Seq'

extension Seq where Element is Equatable
	function includes(target Element) returns bool
		for item in self 'loop'
			if item == target 'yes'
				return true
			end 'yes'
		end 'loop'
		return false
	end 'includes'
end 'Seq'
```

In this example, all types conforming to `Seq` get `countItems()`, but only those whose `Element` implements `Equatable` get `includes()`.

### Conditional Interface Conformance

Extensions can add interface conformance conditionally using both `implements` and `where` clauses. When a concrete type alias satisfies the `where` constraints, the type gains the declared interface conformance.

**Syntax:**

```maxon
extension Array implements Hashable, Equatable where Element is Hashable and Equatable
	function hash() returns HashValue
		// ...
	end 'hash'

	function equals(other Self) returns bool
		// ...
	end 'equals'
end 'Array'
```

**Behavior:**

When a concrete type alias is created (e.g., `typealias IntArr = Array with Integer`), the compiler checks whether the element type satisfies the `where` constraints. If `Integer` implements both `Hashable` and `Equatable`, then `IntArr` automatically conforms to `Hashable` and `Equatable`, enabling it to be used as a `Map` key or `Set` element.

This applies both to explicit `typealias` declarations and to auto-generated type aliases created during monomorphization.

---

## Tuples

Tuples are fixed-size, ordered collections of values with potentially different types. They use parenthesized syntax for both type annotations and literals.

### Tuple Literals

Create tuples using parenthesized, comma-separated expressions:

```maxon
var t = (10, 20)              // 2-element int tuple
var mixed = (42, "hello")     // int and String
var triple = (1, 2, 39)       // 3-element tuple
```

**Note:** A single parenthesized expression `(expr)` is NOT a tuple -- it is a parenthesized expression. Tuples require at least two elements.

### Element Access

Access tuple elements using positional dot syntax `.0`, `.1`, `.2`, etc.:

```maxon
var t = (10, 20)
t.0   // 10
t.1   // 20
```

### Field Assignment

Tuple fields are mutable and can be assigned individually:

```maxon
var t = (0, 0)
t.0 = 20
t.1 = 22
// t is now (20, 22)
```

### Tuple Types

In function parameters and return types, tuple types use parenthesized type lists:

```maxon
typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'sum'

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'
```

### Destructuring Declarations

Tuples can be destructured into new variables using `var` or `let`:

```maxon
var (x, y) = makePair(10, b: 32)   // x = 10, y = 32
let (a, b) = (10, 20)              // immutable bindings
```

Use `_` to discard individual elements:

```maxon
var (result, _) = compute()    // discard second element
var (_, status) = fetch()      // discard first element
```

If the function is pure, at least one element must be used:

```maxon
(_, _) = pureFunc()        // Error E3064: all elements discarded for pure function
```

### Tuple Assignment

Assign tuple values to **existing** mutable variables:

```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)  // x = 10, y = 32
```

**Mixed declaration and assignment** -- combine existing variables with new declarations:

```maxon
var x = 0
(x, var y) = makePair(10, b: 32)     // x existing, y newly declared
(var a, var b) = makePair(10, b: 32)  // both newly declared
(x, let z) = makePair(3, b: 4)       // x existing, z immutable
```

**Discard elements:**

```maxon
(x, _) = makePair(42, b: 99)    // discard second element
```

**Rules:**
- All named variables (without `var`/`let`) must already be declared with `var`
- Immutable (`let`) variables cannot be reassignment targets (error E2013)
- The number of names must exactly match the tuple's element count (error E3005)
- Use `_` to discard individual elements
- If all elements are discarded and the function is pure, error E3064 is raised

### Destructuring in For Loops

Tuple destructuring works in `for` loops when the iterator yields tuples:

```maxon
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
	print("{key}: {value}\n")
end 'loop'
```

Use `_` to discard loop variables:

```maxon
for (_, value) in m 'loop'
	sum = sum + value
end 'loop'
```

### Memory Semantics

- Tuples are heap-allocated structs with reference counting
- Each field occupies 8 bytes (primitives and pointers)
- Tuples containing managed types (strings, structs) have their reference counts managed automatically
- Tuples are assigned by reference (like structs). Use `.clone()` for an independent copy

---

## Enums

Enums define a fixed set of named constants with optional raw values (int, float, string, char, struct). Enums auto-implement `Equatable` and `Hashable`, and support `==`/`!=` comparison. Enums do NOT support associated values -- use `union` for that.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
		north
		south
		east
		west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
		north
		south

		function opposite() returns Direction
				return match self 'check'
						north gives Direction.south
						south gives Direction.north
				end 'check'
		end 'opposite'
end 'Direction'
```

Call methods using type-qualified syntax:

```maxon
var dir = Direction.north
var opp = Direction.opposite(self: dir)  // Direction.south
```

### Enum as Function Parameter

Enums can be used as function parameters and return types:

```maxon
enum Status
		on
		off
end 'Status'

function isOn(s Status) returns bool
		return match s 'check'
				on gives true
				off gives false
		end 'check'
end 'isOn'

function toggle(s Status) returns Status
		return match s 'check'
				on gives Status.off
				off gives Status.on
		end 'check'
end 'toggle'
```

### Creating Enums from Names (`fromName`)

The `fromName` static method creates an enum value from a string name. It throws `EnumError.invalidName` if the name doesn't match any case:

```maxon
enum Direction
		north
		south
		east
		west
end 'Direction'

// Compile-time known name
var dir = try Direction.fromName("north") otherwise Direction.south

// Runtime string
function getDirection(name String) returns Direction
		return try Direction.fromName(name) otherwise Direction.north
end 'getDirection'
```

**Notes:**
- Returns `throws EnumError`, use with `try...otherwise` or `try...catch`
- Compile-time literal names are validated at compile time

### Struct-Backed Enums

Enums can be backed by a struct type, associating compile-time constant metadata with each case. Access the backing struct via `.rawValue`:

```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency Latency
	export let isMemory bool
end 'OpMeta'

enum Instruction
	add = OpMeta{latency: 1, isMemory: false}
	load = OpMeta{latency: 4, isMemory: true}
	store = OpMeta{latency: 3, isMemory: true}
end 'Instruction'

let op = Instruction.load
let lat = op.rawValue.latency     // 4
let mem = op.rawValue.isMemory    // true
```

**Notes:**
- All cases must use the same struct type
- Every case must provide a backing value (no bare cases)
- Struct field values must be compile-time constants (integers, floats, booleans, enum member references like `Priority.high`, or top-level constants)
- At runtime, the enum is stored as an ordinal; `.rawValue` constructs the backing struct

### Enum Interface Conformance

Enums can conform to interfaces using the `implements` keyword, similar to types:

```maxon
enum FileError implements Error
		notFound
		permissionDenied
		alreadyExists
end 'FileError'

enum HttpError int implements Error
		badRequest = 400
		notFound = 404
		serverError = 500
end 'HttpError'
```

**Notes:**
- The `implements Interface` clause comes after the optional backing type
- Multiple interfaces can be specified: `enum Foo implements A, B`
- The `Error` interface can only be implemented by enums or unions (not types/structs)

---

## Unions

Unions define a type with a fixed set of named cases that can carry optional associated values. Unions do NOT implement `Equatable` or `Hashable`, do not support `==`/`!=` comparison, and do not have raw values. Use `match` to inspect union values.

Unions support `.name` (returns the case name as a `String`) and `.ordinal` (returns the zero-based declaration position). Unions also have a static `.allCaseNames` property returning an `Array with String` of the case names in declaration order. `.allCases` is not available on unions because cases may carry associated values; use `.unionCases` to access the discriminant as a first-class enum (see [Union Cases](#union-cases-discriminant-as-an-enum) below).

### Simple Unions

The simplest form of union defines named cases with no additional data:

```maxon
union Option
		some(value int)
		none
end 'Option'
```

### Associated Values

Cases can carry additional data called associated values:

```maxon
union Result
		success(value int)
		failure(code int, message String)
		pending
end 'Result'
```

Construct cases with associated values:

```maxon
var r1 = Result.success(42)                    // Single param is positional
var r2 = Result.failure(404, message: "Not found")  // First positional, second named
var r3 = Result.pending
```

### Pattern Matching with Value Extraction

Use `match` statements to extract associated values from union cases. Each binding name becomes a local variable within the case body:

```maxon
match result 'handle'
		success(value) then return value
		failure(code, msg) then print(msg)
		pending then print("waiting...")
end 'handle'
```

Match expressions also support value extraction using `gives`:

```maxon
var extracted = match container 'get'
		none gives 0
		some(n) gives n * 2
end 'get'
```

You can mix cases with and without bindings:

```maxon
match result 'check'
		success(v) then return v    // Extracts value
		pending then return 0       // No extraction needed
end 'check'
```

**Discarding associated values:** When you don't need the associated value, omit the parentheses entirely:

```maxon
match container 'check'
		some then return 1        // omit parentheses to ignore associated value
		none then return 0
end 'check'
```

**Notes:**
- Binding names must match the number of associated values in the case definition
- Bindings are only in scope within the case body
- Cases without associated values don't need parentheses

### Mutable Match Bindings

When a union variable is declared with `var`, match bindings on its associated values are mutable. Assigning to a binding writes the new value back to the union in-place:

```maxon
var box = Box.full(10)
match box 'update'
		full(value) then value = 42    // Writes 42 back into box
		empty then return
end 'update'
// box is now Box.full(42)
```

When the union variable is declared with `let`, bindings are immutable (read-only copies).

### Comparing Union Values

Union values cannot be compared using `==` or `!=` (error E3066). The only way to inspect a union value is through `match`. This restriction exists to prevent a class of bugs that happen when a new case is added that is unaccounted for and code that handles the union either falls through or uses a default value that is wrong.

```maxon
// ERROR E3066: Cannot compare union values with ==
// if r1 == r2 'check' ... end 'check'

// Use match instead
match result 'check'
		success(v) then handleSuccess(v)
		failure(c, msg) then handleFailure(c, msg: msg)
		pending then handlePending()
end 'check'
```

### Creating Unions from Names (`fromName`)

The `fromName` static method creates a union value from a string name. It throws `EnumError.invalidName` if the name doesn't match any case:

For unions with associated values, pass the values as additional arguments when the name is a compile-time literal:

```maxon
union Container
		empty
		value(n int)
end 'Container'

// With associated values (name must be compile-time literal)
var c = try Container.fromName("value", 42) otherwise Container.empty

// Cases without associated values work with runtime strings
function getContainer(name String) returns Container
		return try Container.fromName(name) otherwise Container.empty
end 'getContainer'
```

**Notes:**
- Returns `throws EnumError`, use with `try...otherwise` or `try...catch`
- Compile-time literal names are validated at compile time
- Associated value types are validated at compile time
- Runtime strings only support cases without associated values

### Union Methods

Unions can have methods, similar to structs:

```maxon
union Direction
		north
		south

		function opposite() returns Direction
				return match self 'check'
						north gives Direction.south
						south gives Direction.north
				end 'check'
		end 'opposite'
end 'Direction'
```

### Union Interface Conformance

Unions can conform to interfaces using the `implements` keyword:

```maxon
union FileError implements Error
		notFound
		permissionDenied(path String)
end 'FileError'
```

**Notes:**
- The `Error` interface can be implemented by enums or unions (not types/structs)

### Union Cases (Discriminant as an Enum)

Every `union` has a compiler-synthesized companion type `U.unionCases` — a simple integer-backed enum with one bare case per variant of `U`, in declaration order. It is the union's discriminant exposed as a first-class enum value.

```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

// Shape.unionCases is conceptually:
//   enum Shape.unionCases
//     circle    // rawValue 0
//     square    // rawValue 1
//     point     // rawValue 2
//   end
```

Because `U.unionCases` is a regular enum it inherits all of the standard enum machinery: `.allCases`, `.allCaseNames`, `.rawValue`, `.fromRawValue`, `.fromName`, `.name`, and `.ordinal`. Match arms over a `U.unionCases` value are exhaustiveness-checked, just like match arms over the union itself.

The intended use is symmetric (de)serialization: write the variant's `rawValue` to a buffer alongside its payload; on read, lift the raw integer back to a `U.unionCases` via `fromRawValue` and match on it to dispatch the payload reader. Match arms are single-statement, so multi-step writers and readers extract per-variant helpers:

```maxon
function writeShapeCircle(buf ByteArray, radius Integer)
	writeDword(buf, value: Shape.unionCases.circle.rawValue)
	writeQword(buf, value: radius)
end 'writeShapeCircle'

function writeShapeSquare(buf ByteArray, side Integer)
	writeDword(buf, value: Shape.unionCases.square.rawValue)
	writeQword(buf, value: side)
end 'writeShapeSquare'

function writeShape(buf ByteArray, value Shape)
	match value 'tag'
		circle(r) then writeShapeCircle(buf, radius: r)
		square(s) then writeShapeSquare(buf, side: s)
		point then writeDword(buf, value: Shape.unionCases.point.rawValue)
	end 'tag'
end 'writeShape'

function readShapeCircle(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let radius = readQword(buf, offset: offset)
	return (Shape.circle(radius), offset + 8)
end 'readShapeCircle'

function readShapeSquare(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let side = readQword(buf, offset: offset)
	return (Shape.square(side), offset + 8)
end 'readShapeSquare'

function readShape(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let raw = readDword(buf, offset: offset)
	let pos = offset + 4
	let kase = try Shape.unionCases.fromRawValue(raw) otherwise panic("corrupt cache: unknown Shape tag {raw}")
	match kase 'tag'
		circle then return readShapeCircle(buf, offset: pos)
		square then return readShapeSquare(buf, offset: pos)
		point then return (Shape.point, pos)
	end 'tag'
end 'readShape'
```

Both `match` statements are exhaustiveness-checked: the writer matches over a `Shape` value, the reader matches over a `Shape.unionCases` value. Adding a new variant to `Shape` automatically extends `Shape.unionCases`, which produces non-exhaustive-match errors in *both* the writer and reader. There is no path to a compiling-but-broken codec.

**Notes:**
- `.unionCases` raw values are declaration ordinals (0, 1, 2, ...). Reordering variants of `U` changes the on-disk format if `rawValue` is being persisted; treat serialized unions as append-only.
- Plain `enum` types do not need `.unionCases` — they already are their own discriminant and expose `.allCases` / `.fromRawValue` directly.

---

## Variables

### Mutable Variables (`var`)
```maxon
var x = 42              // Type inferred
x = x + 5               // Reassignment allowed
```

### Immutable Variables (`let`)
```maxon
let pi = 3.14159        // Cannot be reassigned
let name = "Maxon"
// pi = 3.14            // ERROR: Cannot assign to immutable variable
```

Calling a mutating method on a `let` variable is a compile error. Mutating methods include `push`, `pop`, `set`, `remove`, `clear`, `append`, `insert`, `resize`, `reserve`, `setLength`, `grow`, `upsert`, and similar methods that modify the receiver's state. Use `var` for variables that need mutation:

```maxon
let items = Array with int{}
// items.push(1)        // ERROR: cannot call mutating method 'push' on immutable variable
var items2 = Array with int{}
items2.push(1)           // OK — items2 is var
```

**Rules:**
- All variables must be initialized at declaration
- Type is always inferred from the initializer
- Scope is block-scoped
- Primitives are stack-allocated; structs with all-primitive fields that don't escape scope are stack-promoted automatically; `var` arrays use heap buffers (with automatic cleanup)
- Variables declared with `var` that are never reassigned produce an error (E3077). Use `let` instead if the variable is not mutated.
- For struct-typed variables, `var b = a` creates a reference (alias to the same object); use `var b = a.clone()` for an independent copy (see [Reference-by-Default Assignment](#reference-by-default-assignment))
- Assigning an immutable (`let`) reference-type variable to a mutable (`var`) binding is an error (E3078). Value types (int, float, bool, byte) are always independent copies and are allowed. Use `let` instead of `var`, or call `.clone()` to create an independent mutable copy:
  ```maxon
  let a = Point.create(x: 1, y: 2)
  // var b = a              // ERROR E3078: cannot assign immutable variable 'a' to mutable binding 'b'
  let b = a                 // OK — b is immutable
  var c = a.clone()         // OK — c is an independent mutable copy
  ```
- All variables must be used; unused variables cause a compile error (E3012). This applies to `let`/`var` declarations, function parameters, for-loop variables, match pattern bindings, and closure parameters.
- The variable name `_` is a special discard identifier: it creates no binding and is exempt from unused variable checks. Only the exact name `_` is a discard -- names like `_x` are regular variables subject to normal unused checks. Multiple `_` discards are allowed in tuple destructuring (e.g., `for (_, _) in pairs`). In match patterns, `_` can discard individual bindings (e.g., `pair(_, second)`) but discarding all bindings is an error (E3081) — omit the parentheses instead: `pair then ...`.
- **Interface method exception**: methods that implement an interface contract are exempt from the unused-parameter check on their parameters. The implementer is forced to declare every parameter the interface names, even when a particular implementation does not need one of them. The check still applies to local `let`/`var` bindings inside the method, and to non-interface methods on the same type.

### Top-Level Variables

Variables can be declared at the top level of a module (outside any function):

```maxon
var globalCounter = 0
let MAX_SIZE = 1024

function main() returns ExitCode
		globalCounter = globalCounter + 1
		return globalCounter
end 'main'
```

**Top-Level Variable Rules:**
- `var` declares a mutable top-level variable (can be reassigned from any function)
- `let` declares an immutable top-level constant (compile-time evaluated when possible)
- Most initializers must be constant expressions (integer, float, bool, string literal, or enum member reference like `Color.red`)
- `Type from "literal"` expressions (e.g., `FilePath from "path"`) are also allowed as top-level `let` initializers; these are runtime-initialized before `main()` executes
- Static factory calls (e.g., `let shared = Counter.create()`) and array literals (e.g., `var items = [1, 2, 3]`) are also allowed; their initializers run in a per-file `__module_init` function before `main()`
- Initialized before `main()` executes
- Accessible from any function in the same file (use `export` for cross-file visibility)

**Use Cases:**
- Configuration constants (`let MAX_BUFFER_SIZE = 4096`)
- Counters and state shared across function calls (`var callCount = 0`)
- Program-wide settings

---

## Enums (Raw-Value Enums)

Enums without associated values define a named group of typed constant values. They support direct `==` and `!=` comparison and provide `.rawValue`, `.name`, `.ordinal`, `.allCases`, `.allCaseNames`, `fromRawValue()`, and `fromName()`.

### Declaration

```maxon
enum HttpStatus
		ok = 200
		notFound = 404
		serverError = 500
end 'HttpStatus'
```

Cases without explicit values auto-increment from 0 (or from the previous explicit value + 1):

```maxon
enum Color
		red       // 0
		green     // 1
		blue      // 2
end 'Color'

enum Priority
		low         // 0
		medium      // 1
		high = 10
		critical    // 11
end 'Priority'
```

### Backing Types

Enums support integer, float, String, Character, struct, and function backing types.

```maxon
enum Threshold
		low = 0.1
		medium = 0.5
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

Struct backing attaches compile-time constant metadata to each case. Field values must be compile-time constants (integers, floats, booleans) or nested struct literals:

```maxon
type OpInfo
		export let latency int(0 to 100)
		export let throughput int(0 to 10)
end 'OpInfo'

enum Instruction
		add = OpInfo{latency: 1, throughput: 2}
		mul = OpInfo{latency: 3, throughput: 1}
		div = OpInfo{latency: 40, throughput: 1}
end 'Instruction'

let lat = Instruction.div.rawValue.latency  // 40
```

At runtime, struct-backed enums are stored as ordinals. The struct is reconstructed on `.rawValue` access. `fromRawValue()` is not available for struct-backed enums.

Function backing attaches a top-level function reference to each case. All cases must share the same function signature, which becomes the enum's backing type. The function may be declared later in the same file or in a different file; the binding is resolved after every file's top-level declarations have been pre-scanned.

```maxon
typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
		return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
		return x * 3
end 'tripleFn'

enum Op
		doubleOp = doubleFn
		tripleOp = tripleFn
end 'Op'

let f = Op.doubleOp.rawValue
let r = f(21)   // 42
```

At runtime, function-backed enums are stored as ordinals; `.rawValue` lowers to a select chain that recovers the function pointer for the live case. `fromRawValue()` is not available for function-backed enums.

Auto-increment (bare case names with no explicit value) is only valid for integer-backed enums. Mixing bare names with non-integer explicit values is a compile error.

Negative integer values are supported:

```maxon
enum Temperature
		freezing = 0
		cold = -10
		warm = 25
end 'Temperature'
```

### Comparison

Enums without associated values allow direct `==` and `!=` comparison:

```maxon
var s = HttpStatus.notFound
if s == HttpStatus.notFound 'check'
		// ...
end 'check'
if s != HttpStatus.ok 'check2'
		// ...
end 'check2'
```

### Match

Enum matches require exhaustive case coverage — all cases must be matched by explicit patterns or range patterns. Match arms use bare case names (unqualified); using qualified `Type.case` syntax in a match arm is a compile error (E3075). Plain `default` is not allowed; use `default throws` or `default panic("message")` if you want a catch-all:

```maxon
// Exhaustive: all cases listed
var result = match s 'handle'
		ok gives 1
		notFound gives 2
		serverError gives 3
end 'handle'
```

Range patterns use bare case names as bounds, based on ordinal values. `to` is inclusive, `upto` excludes the upper bound. Qualified `Type.case` syntax in match arms is a compile error (E3075):

```text
match p 'check'
    low to medium then print("not urgent")
    high to critical then print("urgent")
end 'check'
```

Overlapping patterns (ranges that cover the same case, or an explicit case within a range) are reported as errors.

### Raw Value Access

All enums support `.rawValue`. Simple enums return the case ordinal (0, 1, 2…); backed enums return the explicit value:

```maxon
var c = Color.green
var ordinal = c.rawValue   // 1

var s = HttpStatus.notFound
var code = s.rawValue      // 404
```

### Name Access

All enums have a `.name` property returning the case name as a `String`. For backed enums, `.name` always returns the case name, not the raw value:

```maxon
var s = HttpStatus.notFound
var code = s.rawValue   // 404
var n = s.name          // "notFound"
```

### Ordinal Access

All enums have an `.ordinal` property returning the zero-based declaration position as an `int`. For simple enums, `.ordinal` is identical to `.rawValue`. For backed enums, `.ordinal` is the position in declaration order, not the backing value:

```maxon
var c = Color.green
var pos = c.ordinal    // 1 (same as .rawValue for simple enums)

var s = HttpStatus.notFound
s.ordinal              // 1 (second case in declaration order)
s.rawValue             // 404 (backing value)
s.name                 // "notFound"
```

`.ordinal` is available on all enum backing types (int, float, string, char).

### All Cases (`allCases`)

All enums have a static `.allCases` property that returns an `Array` containing all cases in declaration order:

```maxon
for color in Color.allCases 'loop'
	print("{color.name}\n")
end 'loop'
// Prints: red, green, blue

var count = Color.allCases.count()  // 3
```

`.allCases` works with all backing types (simple, int, float, string, char).

### All Case Names (`allCaseNames`)

All enums and unions have a static `.allCaseNames` property returning an `Array with String` of the case names in declaration order:

```maxon
for name in Color.allCaseNames 'loop'
	print("{name}\n")
end 'loop'
// Prints: red, green, blue

var count = Color.allCaseNames.count()  // 3
```

Unlike `.allCases`, `.allCaseNames` is available on unions too — even unions whose cases carry associated values — because only the case name strings are returned.

### Converting from Raw Value (`fromRawValue`)

The `fromRawValue` static method converts a raw value to an enum case. It throws `EnumError.invalidRawValue` if no case matches:

```maxon
var s = try HttpStatus.fromRawValue(404) otherwise HttpStatus.ok  // HttpStatus.notFound
```

### Converting from Name (`fromName`)

The `fromName` static method converts a string name to an enum case. It throws `EnumError.invalidName` if no case matches:

```maxon
var s = try HttpStatus.fromName("notFound") otherwise HttpStatus.ok  // HttpStatus.notFound

// Runtime string
function getStatus(name String) returns HttpStatus
		return try HttpStatus.fromName(name) otherwise HttpStatus.ok
end 'getStatus'
```

**Notes:**
- Both `fromRawValue` and `fromName` throw; use `try...otherwise` or `try...catch`
- Compile-time literal arguments are validated at compile time

### As Function Parameters and Return Types

```maxon
function isSuccess(s HttpStatus) returns bool
		if s == HttpStatus.ok 'check'
				return true
		end 'check'
		return false
end 'isSuccess'

function getDefault() returns HttpStatus
		return HttpStatus.ok
end 'getDefault'
```

### Keywords as Case Names

Keywords can be used as enum case names:

```maxon
enum TokenKind
		function
		return
		end
		if
end 'TokenKind'
```

### Export

```maxon
export enum Permission
		none = 0
		read = 1
		write = 2
end 'Permission'
```

### Error Conditions

- **E3030**: Duplicate case name within the same enum block
- **E3031**: Duplicate explicit value within the same enum block
- **E3032**: Mixing backing types (e.g., int and String values in the same block)
- **E3034**: Accessing an unknown case (`Color.purple` when `purple` is not defined)

---

## Functions

### Declaration Syntax
```maxon
// Function with return value
function name(param type [= default], ...) returns returnType
		// statements
		return value
end 'name'

// Function with no return value (implicit void)
function name(param type [= default], ...)
		// statements
end 'name'
```

**Block Identifier**: The string after `end` must match the function name.

**Return Type**: Functions that return a value must specify `returns` followed by the type. Functions that don't return a value should omit the `returns` clause entirely.

**Discarding Parameters**: Use `_` as the parameter name to discard an unused parameter and suppress the unused-variable error:

```maxon
function onClick(_ MouseEvent)
	// the MouseEvent argument is intentionally unused
end 'onClick'
```

### Named Arguments

Maxon uses a **first-positional, rest-named** rule for function and method calls:
- **First argument**: Always positional (no name)
- **Subsequent arguments**: Must use `name: value` syntax
- Named arguments (after the first) can appear in any order
- Parameters with default values can be omitted

**Examples:**

```maxon
function add(a int, b int) returns int
		return a + b
end 'add'

add(3, b: 4)      // First positional, second named

function connect(host String, port int) returns bool
		// ...
end 'connect'

connect("localhost", port: 8080)  // First positional, second named

// Single parameter functions
function greet(name String)
		print("Hello, " + name)
end 'greet'

greet("Alice")    // Single param is positional
```

### Default Values

Parameters can have default values. Parameters with defaults can be omitted at the call site. Any literal expression is supported as a default value, including integers, floats, booleans, strings, arrays, enum cases, struct construction, character literals, and byte string literals.

```maxon
function greet(name String, title String = "Mr.")
		print("Hello, {title} {name}")
end 'greet'

greet("Smith")                    // Uses default title
greet("Smith", title: "Dr.")      // Override default

// String default
function connect(host String = "localhost") returns ExitCode
		// ...
end 'connect'

// Array default
function process(items IntArray = [10, 20, 12]) returns Integer
		// ...
end 'process'

// Integer default
function retry(attempts AttemptCount = 3) returns ExitCode
		// ...
end 'retry'

// Float default
function scale(factor ScaleFactor = 1.0) returns ScaleFactor
		// ...
end 'scale'

// Bool default
function run(verbose bool = false) returns ExitCode
		// ...
end 'run'

// Enum default
function setLevel(level Priority = Priority.medium) returns ExitCode
		// ...
end 'setLevel'

// Struct default
function draw(origin Point = Point{x: 0, y: 0}) returns ExitCode
		// ...
end 'draw'

// Character default
function setSeparator(sep Character = '/') returns ExitCode
		// ...
end 'setSeparator'

// Byte string default
function send(header ByteArray = b"HTTP/1.1") returns ExitCode
		// ...
end 'send'
```

**Rules:**
- Parameters with defaults must come after required parameters
- Default values are evaluated at call site
- Arguments may be omitted if they have defaults
- Any literal expression is supported as a default value

### Function Overloads

Maxon supports function overloading — multiple functions with the same name but different signatures.

#### Disambiguation by Parameter Types

When overloads differ in their parameter types, the compiler automatically selects the correct overload based on the argument types at the call site:

```maxon
function process(value int) returns int
		return value * 2
end 'process'

function process(value String) returns int
		return value.count()
end 'process'

process(42)        // calls process(value int)
process("hello")   // calls process(value String)
```

#### Disambiguation by Parameter Names

When overloads have different parameter names, the caller uses named arguments to select the correct overload:

```maxon
function create(name String) returns String
		return name
end 'create'

function create(label String) returns String
		return label
end 'create'

create(name: "foo")    // calls first overload
create(label: "bar")   // calls second overload
```

#### Ambiguous Calls

If the compiler cannot determine which overload to call based on argument types alone, it requires named arguments. Calling an ambiguous overload without named arguments produces error **E3007**.

### Examples

**No Parameters**
```maxon
function getAnswer() returns int
		return 42
end 'getAnswer'
```

**Void Return Type**
```maxon
function greet(name String)
		print("Hello, " + name)
end 'greet'
```

**Multiple Parameters**
```maxon
function add(a int, b int) returns int
		return a + b
end 'add'

var result = add(3, b: 4)
```

**Named Arguments for Clarity**
```maxon
function divide(dividend int, divisor int) returns int
		return dividend / divisor
end 'divide'

var result = divide(dividend: 10, divisor: 2)
```

**Array Parameters**
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(numbers IntArray) returns int
		var total = 0
		for num in numbers 'loop'
				total = total + num
		end 'loop'
		return total
end 'sum'
```

### Calling Functions

**First Positional, Rest Named:**
```maxon
var result = add(3, b: 4)         // First positional, second named
var answer = getAnswer()          // No parameters
greet("Alice")                    // Single param is positional
divide(100, divisor: 5)           // First positional, second named
```

### Parameter Passing

Maxon uses **automatic pass-by-reference** for parameters that are assigned to inside the function body.

**By-value (read-only parameters):** If a function only reads a parameter, the value is passed directly — no indirection overhead.

**By-reference (mutated parameters):** If a function assigns to a parameter (directly or through a field or element), the compiler passes a pointer to the caller's storage. This allows the called function to mutate the caller's variable.

```maxon
function increment(n int)
		n = n + 1       // assigns to n — passed by reference
end 'increment'

function main() returns ExitCode
		var x = 10
		increment(x)    // x is now 11
		return x
end 'main'
```

**Mutability rules:**

- If the caller passes a `var` variable, the parameter is mutable and assignments propagate back to the caller.
- If the caller passes a `let` variable to a function that mutates its parameter, the compiler raises error **E3063**.
- If the caller passes a literal or expression (not a named variable), the compiler creates a temporary immutable stack slot. Mutations inside the function do not propagate anywhere.

```maxon
function double(n int)
		n = n * 2
end 'double'

function main() returns ExitCode
		var x = 5
		double(x)       // OK — x is var; x becomes 10

		let y = 5
		double(y)       // ERROR E3063: cannot pass let variable to mutating parameter

		double(5)       // OK — literal creates a temporary; mutation has no visible effect
		return x
end 'main'
```

### Function Types and Function-Typed Values

Functions in Maxon are first-class values: they can be stored in variables, passed as arguments, and returned from other functions. A *function type* describes a callable signature:

```maxon
(Score) returns Score             // takes one Score, returns Score
(Score, Score) returns bool       // takes two Scores, returns bool
()                                // takes nothing, returns void
```

The `returns` clause is omitted for a void-returning function type. To use a function type repeatedly, name it with `typealias`. A function-type alias resolves to the same `IrFunctionType` as the inline form and is interchangeable at every use site — function parameter, return type, struct field, or generic argument:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = (Integer) returns Integer
typealias HandlerMap = Map with(String, UnaryOp)

function apply(f UnaryOp, x Integer) returns Integer
		return f(x)
end 'apply'

function pickDouble() returns UnaryOp
		return double                  // function reference, no parens
end 'pickDouble'

function main() returns ExitCode
		let f = pickDouble()           // f has type UnaryOp
		return f(21)                   // 42
end 'main'
```

A bare function name (no parens) evaluates to a function reference. Closures (see below) and function references are both valid where a function-typed value is expected.

### Closures

Closures are anonymous functions expressed inline using `gives` syntax:

```maxon
(param) gives expression
(param1, param2) gives expression
() gives expression
```

**Capture by reference:** Closures capture variables from the enclosing scope by reference, not by value. This means changes to a captured variable after the closure is created are visible inside the closure when it executes.

```maxon
function main() returns ExitCode
		var x = 10
		let addX = (n int) gives n + x   // captures x by reference
		x = 20
		var result = addX(5)             // evaluates with x == 20, result is 25
		return result
end 'main'
```

**Notes:**
- Closure parameters may optionally omit the type annotation when the type can be inferred from context.
- Closures can only appear where a function-type value is expected.
- Captured variables follow the same mutability rules as parameters: a closure that assigns to a captured `let` variable produces a compile error.
- Closure parameters are checked for unused (E3012). Use `_` to discard an unused parameter: `(_ int) gives 42`
- A closure declared inside an instance method may reference `self` (and therefore `self.field` and `self.method(...)`); the receiver is captured like any other local. A closure inside a free function or static method that mentions `self` is rejected with **E2001**.

### Function Purity and Discarded Results

Maxon requires function return values to be used. The compiler infers whether each function is **pure** or **impure** and enforces different rules for discarding results.

#### Pure vs Impure Functions

A function is **pure** if it has no side effects: it does not write to stdout/stderr, does not modify global state, does not mutate parameters, and only calls other pure functions. Purity is inferred automatically by the compiler -- there is no annotation.

A function is **impure** if it performs any side effect, either directly or by calling another impure function. Examples of impure operations include:
- Writing to stdout or stderr (e.g., `print`)
- Modifying global or static variables
- Mutating parameters
- Calling runtime functions
- Calling other impure functions (transitively)

Functions with no return type are always considered impure (their result cannot be discarded because there is no result).

#### Discarding Pure Function Results

Pure function results **must** be used -- they cannot be discarded, even with `_ =`. Since a pure function has no side effects, calling it without using the result is always a mistake.

```maxon
function double(x int) returns int
		return x * 2
end 'double'

double(5)               // Error E3064: result of pure function 'double' must be used
_ = double(5)       // Error E3064: result of pure function 'double' must be used
let result = double(5)  // OK: result is used
```

#### Discarding Impure Function Results

Impure function results **must** be explicitly acknowledged. A bare statement-level call that ignores the result is an error. To intentionally discard the result, use `_ =`:

```maxon
var counter = 0
function incrementAndGet() returns int
		counter = counter + 1
		return counter
end 'incrementAndGet'

incrementAndGet()               // Error E3065: result of 'incrementAndGet' is not used
_ = incrementAndGet()       // OK: explicitly discarded
let count = incrementAndGet()   // OK: result is used
```

#### Chainable Methods

Methods that take `self` as their first parameter and return the same type are **chainable**. Their results can be freely discarded without `_ =`, since the common pattern is to call them for their side effect on the receiver:

```maxon
type Counter
		var value int

		function increment() returns Counter
				value = value + 1
				return self
		end 'increment'
end 'Counter'

var c = Counter{value: 0}
c.increment()  // OK: chainable method, result can be discarded
```

#### Discarding Tuple Elements

When destructuring a tuple, individual elements can be discarded with `_`. If the function is pure, at least one element must be used:

```maxon
var (result, _) = pureFunc()   // OK: one element used
(_, _) = pureFunc()        // Error E3064: all elements discarded for pure function
```

#### Error Codes

| Code | Meaning |
|------|---------|
| E3064 | Result of a pure function must be used (cannot be discarded) |
| E3065 | Result of an impure function is not used (use `_ = expr` to discard) |

### Extern Functions

Declare external functions (Windows API, C libraries):
```maxon
extern function GetStdHandle(nStdHandle int) returns int
extern function ExitProcess(uExitCode int) returns int
```

**Notes:**
- No function body or `end` statement
- No name mangling
- Assumes C calling convention
- Must exist at link time

---

## Expressions

### Operator Precedence (highest to lowest)

1. **Postfix**: `.` (member access), `as` (cast), function call `()`
2. **Unary**: `-` (negation), `not` (logical/bitwise not)
3. **Multiplicative**: `*` `/` `mod`
4. **Additive**: `+` `-`
5. **Shift**: `shl` `shr`
6. **Comparison**: `==` `!=` `<` `>` `<=` `>=` `is` `is not`
7. **AND**: `and`
8. **XOR**: `xor`
9. **OR**: `or`
10. **Conditional**: `<true_value> if <condition> else <false_value>` (lowest precedence)

### Arithmetic Operators

| Operator | Description | Types | Example |
|----------|-------------|-------|---------|
| `+` | Addition | int, float | `a + b` |
| `-` | Subtraction | int, float | `a - b` |
| `*` | Multiplication | int, float | `a * b` |
| `/` | Division | int, float | `a / b` |
| `mod` | Modulo | int only | `a mod b` |

**Notes:**
- Mixed int/float operations promote int to float

### Comparison Operators

| Operator | Description | Result Type |
|----------|-------------|-------------|
| `==` | Equal to | bool |
| `!=` | Not equal to | bool |
| `<` | Less than | bool |
| `>` | Greater than | bool |
| `<=` | Less than or equal | bool |
| `>=` | Greater than or equal | bool |

Using `==` or `!=` on struct types requires the type to implement the `Equatable` interface (error E3069 if it does not). Primitives, `String`, and `Array` support `==` and `!=` without restriction. For reference identity comparison (same heap object), use `is` and `is not` instead.

### Reference Identity Operators

| Operator | Description | Result Type |
|----------|-------------|-------------|
| `is` | Same reference (same heap object) | bool |
| `is not` | Different references | bool |

`is` and `is not` compare whether two struct-typed variables refer to the same heap object. They cannot be used on primitive types (`int`, `float`, `bool`, `byte`) — using them produces error E3068.

```maxon
function areSame(a Point, b Point) returns bool
	return a is b
end 'areSame'
```

### Logical / Bitwise Operators

The keyword operators `and`, `or`, `xor`, and `not` are context-dependent: they perform logical operations on `bool` operands and bitwise operations on `int` operands.

| Operator | On `bool` | On `int` | Example |
|----------|-----------|----------|---------|
| `and` | Logical AND | Bitwise AND | `a > 0 and b < 10` / `flags and 0xff` |
| `or` | Logical OR | Bitwise OR | `x == 1 or x == 2` / `flags or 0x01` |
| `xor` | Logical XOR | Bitwise XOR | `a xor b` / `value xor mask` |
| `not` | Logical NOT (unary) | Bitwise NOT (unary) | `not done` / `not mask` |

`and` and `or` short-circuit when both operands are `bool`: the right-hand side
is evaluated only if the left-hand side does not already determine the result
(`false and _` skips the right; `true or _` skips the right). This lets a
left-hand guard make the right-hand side safe to evaluate, e.g.
`i < arr.count() and (try arr.get(i) otherwise default) > 0`. Integer `and`/`or`
remain bitwise and always evaluate both sides.

### Shift Operators

Shift operators work on integers only.

| Operator | Description | Example |
|----------|-------------|---------|
| `shl` | Shift left | `1 shl 4` (result: 16) |
| `shr` | Shift right | `256 shr 4` (result: 16) |

### Unary Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `-` | Negation | `-x` |
| `not` | Logical NOT / Bitwise NOT | `not condition` / `not mask` |

The `-` operator cannot be chained directly. Use parentheses for nested negation:
```maxon
var y = -(-x)      // OK: parenthesized
var z = -(x + 1)   // OK: subexpression
// var w = --x      // Error: consecutive negation operators
```

The `not` operator can be applied repeatedly:
```maxon
var a = not not x  // OK: double bitwise NOT (identity for integers)
```

### Parentheses
Override precedence:
```maxon
(2 + 3) * 5    // 25, not 17
```

### Conditional (Ternary) Expression

The conditional expression evaluates one of two values based on a boolean condition:

```text
<true_value> if <condition> else <false_value>
```

The condition must be `bool`. Both arms must produce the same type. The conditional expression binds looser than all binary operators, so operands are evaluated naturally without extra parentheses:

```maxon
let x = a + b if flag else c * d    // (a + b) if flag else (c * d)
let abs = x if x > 0 else -x
let label = "yes" if enabled else "no"
```

Conditional expressions can be chained. They associate to the right:

```maxon
let tier = "gold" if score > 90 else "silver" if score > 70 else "bronze"
// equivalent to: "gold" if score > 90 else ("silver" if score > 70 else "bronze")
```

Conditional expressions work inside string interpolation, including with nested string literals:

```maxon
print("Status: {"on" if flag else "off"}")
```

### Array Access

Array elements are accessed using the `.get()` method, which throws `ArrayError.indexOutOfBounds` if the index is out of range, or `ArrayError.emptySlot` if the slot at that index is empty (null pointer, e.g. after `resize()` without filling every slot):
```maxon
var arr = [1, 2, 3, 4, 5]
var first = try arr.get(0) otherwise 0
var last = try arr.get(arr.count() - 1) otherwise 0
```

Array elements are modified using the `.set()` method:
```maxon
var arr = [1, 2, 3]
arr.set(0, value: 100)  // First positional, second named
```

### Creating Empty Arrays

Create an empty typed array using a type alias:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var numbers = IntArray.create()         // Empty array
numbers.push(42)                 // Add elements with push
```

To preallocate with a specific length (elements zero-initialized):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.resize(100)               // Length is now 100
buffer.set(0, value: 42)         // Can set any index 0-99
```

To preallocate capacity without changing length (for performance):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.reserve(100)              // Capacity is 100, length is 0
buffer.push(42)                  // Now length is 1
```

---

## Statements

### Expression Statement
Any expression followed by newline:
```maxon
print(x)           // Single param is positional
add(3, b: 4)       // First positional, rest named
x = x + 1
```

### Return Statement
```maxon
return expression
```
Must appear in every code path of non-void functions.

### Variable Declaration
```maxon
var x = 10
let y = 20
```

### Assignment
```maxon
variable = expression
```

**Note:** Cannot assign to `let` variables (immutable).

Assigning a variable to itself is a compile error (E3067), since it has no effect:
```maxon
x = x         // ERROR: self-assignment has no effect
p.x = p.x     // ERROR: self-assignment has no effect
```

### Tuple Assignment

Assign multiple values from a tuple expression (or function returning a tuple) to existing mutable variables in a single statement:

```maxon
(variable1, variable2) = expression
```

**Notes:**
- All named variables must already be declared with `var`
- Immutable (`let`) variables cannot be targets (error E2013)
- The number of names must exactly match the tuple's element count (error E3005 on mismatch)
- Use `_` to discard individual elements
- If all elements are discarded (`(_, _) = ...`) and the function is pure, error E3064 is raised

**Example:**
```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)  // x = 10, y = 32
```

**Discard individual elements:**
```maxon
(result, _) = compute()   // discard second element
(_, status) = fetch()     // discard first element
```

### If Statement

**Syntax**
```maxon
if condition 'label'
		statements
end 'label'
```

**With Else**
```maxon
if condition 'then'
		statements
end 'then' else 'else'
		statements
end 'else'
```

**Notes:**
- Block identifier required after `if` condition
- Block identifier must match on `else` and `end` keywords
- Condition must be `bool` type
- Can nest arbitrarily
- Empty blocks are a compile error (E3082) — every `if`, `else`, `while`, `for`, and `try...otherwise` block must contain at least one statement

### While Loop
```maxon
while condition 'label'
		statements
end 'label'
```

**Example:**
```maxon
var i = 0
while i < 10 'loop'
		print("{i}")
		i = i + 1
end 'loop'
```

### For Loop
```maxon
for variable in iterable 'label'
		statements
end 'label'
```

**Iterating over collections:**
```maxon
var numbers = [1, 2, 3, 4, 5]
for num in numbers 'loop'
		print("{num}")
end 'loop'
```

**Iterating over ranges:**

Ranges are created using `to` (inclusive) or `upto` (exclusive) expressions:

```maxon
// Inclusive range: 1, 2, 3, 4, 5
for i in 1 to 5 'loop'
		print("{i}")
end 'loop'

// Exclusive range: 1, 2, 3, 4
for i in 1 upto 5 'loop'
		print("{i}")
end 'loop'

// Character ranges
for c in 'a' to 'z' 'loop'
		print("{c}")
end 'loop'
```

Range expressions are supported for `int` and `Character`.

**Ranges as first-class values:**

Outside a `for-in` header, an integer range expression evaluates to a `Range` (`to`, inclusive) or `OpenRange` (`upto`, exclusive) value from the standard library. Both implement `Iterable`, so they can be bound to a variable, passed as an argument, or chained with `.createIterator()` / `.withIterator()`. Inside a `for-in` header the same syntax desugars directly to a counted loop with no allocation.

```maxon
let r = 1 upto 5                           // OpenRange value
for x in r 'loop' ... end 'loop'           // iterates 1, 2, 3, 4

let it = try (1 to 4).createIterator() otherwise return 0
for v in it 'loop' ... end 'loop'          // iterates 1, 2, 3, 4
```

Character ranges and ranges over user-defined types remain `for-in`-only.

**Iterating with the underlying iterator:**

Append `.withIterator()` to any iterable to get an `(Iterator, Element)` tuple — the iterator exposes navigation methods like `index()`, `advance()`, `retreat()`, `advanceBy(n)`, `retreatBy(n)`, `seek(index)`, and `peek(ahead)`:

```maxon
var names = ["Alice", "Bob", "Charlie"]
for (iter, name) in names.withIterator() 'loop'
		print("{iter.index()}: {name}\n")
end 'loop'
// 0: Alice
// 1: Bob
// 2: Charlie
```

This works on all iterable types (Array, String, Map, Set, List, etc.). The `WithIterIterator` is a lazy wrapper — no intermediate collection is created.

**Notes:**
- Loop variable is immutable (like `let`)
- Ranges use `to` for inclusive end and `upto` for exclusive end
- Desugars to a loop over the `Iterator` protocol: `advance(1)` (throws `IterationError.exhausted` at end) followed by `current()` (infallible read of the element in view)
- The compiler calls `createIterator()` before each loop to obtain a fresh iterator, enabling safe re-iteration and nested loops over the same collection
- Loop variables are checked for unused (E3012). Use `_` as the loop variable when the value is not needed: `for _ in array 'loop'`. In tuple destructuring, each element can be discarded independently: `for (key, _) in pairs 'loop'`

### Match Statement

Match statements provide pattern matching on values, executing different code based on the matched pattern. Each case is a single line with exactly one statement.

**Syntax**
```maxon
match expression 'label'
		pattern then statement
		pattern1 or pattern2 then statement
		pattern then statement and fallthrough
		pattern then break
		default then statement
end 'label'
```

**Example:**
```maxon
var x = 2
match x 'check'
		1 then return 10
		2 or 3 then return 20
		default then return 0
end 'check'
```

**With Fallthrough:**
```maxon
var result = 0
match x 'cascade'
		1 then result = result + 10 and fallthrough
		2 then result = result + 20
		default then result = 100
end 'cascade'
```

When `x = 1`, the first case matches, adds 10, then falls through to case 2 (adds 20), giving a total of 30.

**With Break:**

Use `break` in a match arm to exit the match without executing any code for that arm. An unlabeled `break` exits the innermost match. A labeled `break` can target any enclosing match or loop:

```maxon
while running 'loop'
		match state 'check'
				0 then break              // exits match, continues loop
				1 then break 'loop'       // exits loop
				default then process()
		end 'check'
end 'loop'
```

`break` is not allowed in match expressions (with `gives`), since every arm must produce a value.

**Enum Case Pattern Matching (Associated Values):**

For enums with associated values, use `CaseName(bindings)` syntax to extract values:

```maxon
enum Result
		success(value int)
		failure(code int)
end 'Result'

var r = Result.success(42)
match r 'handle'
		success(v) then return v      // v binds to 42
		failure(c) then return c
end 'handle'
```

**Notes:**
- Block identifier required after `match expression` and on `end`
- Each case is a single line with one statement
- Block-opening statements (`if`, `while`, `for`, nested `match`, and the multi-line `try ... end` / `try ... otherwise 'label' ... end` block forms) are rejected in match arms with **E2049**. All single-statement `try` forms are allowed: bare propagation (`try call()`), `try call() otherwise panic("...")`, `try call() otherwise ignore`, `try call() otherwise return/break/continue/throw ...`, and `try call() otherwise <expr>`.
- Multiple patterns can be combined with `or`
- `break` exits the match statement (or a labeled enclosing loop/match)
- `and fallthrough` continues to the next case (skipping its pattern check)
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered (error E2026) — plain `default` is not allowed (error E2046). This is a deliberate design choice: when a new case is added to an enum, a plain `default` arm would silently swallow it, hiding bugs that can be subtle and difficult to track down. By requiring exhaustive coverage, the compiler forces every match site to be reviewed when cases change, ensuring new variants are handled intentionally. To cover cases you don't need to handle individually, use range patterns with `break` (see [Enum Match Range Patterns](#enum-match-range-patterns) below), or use `default throws` / `default panic("message")` to signal that unhandled cases are errors (see [Default Throws / Default Panic in Match](#default-throws--default-panic-in-match) below). Enums use bare case names in match arms — qualified `Type.case` syntax is a compile error (E3075). Range patterns use bare case names as bounds (`case1 to case2`).
- Overlapping patterns are reported as errors (error E2027).
- All matches must be exhaustive. For non-enum matches (int, float, string, char), a `default` arm is required.
- `default` matches any non-enum value not matched by previous patterns
- `default` must be the last case if present
- Enum case patterns: `CaseName(binding1, binding2)` extracts associated values
- Pattern bindings are checked for unused (E3012). Use `_` to discard individual bindings: `pair(_, second)`
- To discard all associated values, omit the parentheses entirely: `success then ...` — using `success(_)` when all bindings are discarded is an error (E3081)

**Enum Match Range Patterns:**

Enums with associated values support range patterns on bare case names using `to` (inclusive) and `upto` (exclusive upper bound). This allows matching a contiguous range of cases by their ordinal (declaration order) without listing each one individually.

```maxon
enum IrOp
		maxon(op MaxonOp)
		arith(op ArithOp)
		cf(op CfOp)
		func(op FuncOp)
end 'IrOp'

match op 'dispatch'
		maxon(hlOp) then lowerMaxonOp(hlOp, dstBlock: dstBlock)
		arith to func then dstBlock.ops.push(op)
end 'dispatch'
```

In this example, `arith to func` matches `arith`, `cf`, and `func` (inclusive). Using `arith upto func` would match `arith` and `cf` but not `func`. Cases with associated values can be covered by a range — their payloads are simply inaccessible in that arm.

**Rules:**
- A range arm cannot extract bindings. To extract associated values from a specific case, match it individually with binding syntax.
- Range bounds are based on ordinal order (the order cases are declared in the enum).
- Range patterns participate in exhaustiveness checking — they count toward full case coverage.
- Overlapping patterns (a range that covers a case also matched explicitly, or two overlapping ranges) are reported as errors (E2027).
- A range pattern that covers exactly one value is also rejected as E2027 — `red to red` and `red upto green` (when `green` is the case immediately after `red`) are mistakes; use the bare case name `red` instead.
- Range patterns can be combined with `or` and with explicit case patterns in the same match.

**Range Patterns:**

Range patterns match numeric values within a range using Rust-style syntax:

| Syntax | Meaning | Example |
|--------|---------|---------|
| `a..=b` | Inclusive range (a ≤ x ≤ b) | `1..=5` matches 1, 2, 3, 4, 5 |
| `a..<b` | Exclusive upper (a ≤ x < b) | `1..<5` matches 1, 2, 3, 4 |
| `a..` | Open upper bound (x ≥ a) | `100..` matches 100 and above |
| `..=b` | Open lower, inclusive (x ≤ b) | `..=0` matches 0 and below |
| `..<b` | Open lower, exclusive (x < b) | `..<0` matches negative numbers |
| `..` | Wildcard (matches any value) | `..` equivalent to `default` |

```maxon
function classify(n int) returns int
		match n 'check'
				1..=5 then return 1      // 1 to 5 inclusive
				6..<10 then return 2     // 6 to 9 (exclusive of 10)
				10.. then return 3       // 10 and above
				default then return 0    // negative numbers
		end 'check'
end 'classify'
```

Range patterns work with integers, floats, and any type implementing the `Comparable` interface (like `Character`):

```maxon
function charType(c Character) returns int
		match c 'classify'
				'a'..='z' then return 1  // lowercase letters
				'A'..='Z' then return 2  // uppercase letters
				'0'..='9' then return 3  // digits
				default then return 0    // other
		end 'classify'
end 'charType'
```

A range pattern that covers exactly one value is rejected as E2027 — `5 to 5`, `5 upto 6`, `'a' to 'a'`, and `'a' upto 'b'` (adjacent codepoints) are mistakes; use the bare value (`5`, `'a'`) instead.

Range patterns can be combined with `or`:

```maxon
match score 'grade'
		90..=100 or 85..=89 then return "A"
		70..=84 then return "B"
		default then return "C"
end 'grade'
```

### Match Expression

Match expressions return a value and can be assigned to variables. Use `gives` instead of `then`:

**Syntax**
```maxon
let result = match expression 'label'
		pattern1 gives value1
		pattern2 or pattern3 gives value2
		default gives defaultValue
end 'label'
```

**Example:**
```maxon
var grade = "B"
let points = match grade 'convert'
		"A" gives 4
		"B" gives 3
		"C" gives 2
		default gives 0
end 'convert'
```

**Enum Case Extraction:**
```maxon
enum Container
		empty
		value(n int)
end 'Container'

var c = Container.value(10)
var result = match c 'get'
		empty gives 0
		value(n) gives n * 2    // result = 20
end 'get'
```

**Notes:**
- All cases must return the same type
- `and fallthrough` is NOT allowed in match expressions
- Block identifier required
- Enum bindings work the same as in match statements

#### Per-Arm `panic` and `throws`

Because match expressions don't allow arbitrary statements in arm bodies, but you may still need a specific case to signal an unrecoverable error or throw a recoverable one, individual arms may use `panic("message")` or `throws ErrorType.case` in place of `gives <expr>`. The arm terminates instead of producing a value, so the match expression's result type is inferred only from the `gives` arms.

```maxon
let n = match c 'check'
		red panic("red not allowed here")
		green throws ColorError.unsupported
		blue gives 42
		default gives 0
end 'check'
```

- `panic("...")` arms accept either a string literal or an interpolated string, just like the `panic` statement and `default panic`.
- `throws ErrorType.case` arms require the enclosing function to declare `throws ErrorType` (the same rule as the `throw` statement and `default throws`).
- A diverging arm covers its pattern for exhaustiveness purposes exactly like a `gives` arm.
- This applies only to match *expressions* — match statements already accept `panic`/`throw` via the normal `then <statement>` form.

### Default Throws / Default Panic in Match

When matching on an enum, all cases must normally be covered explicitly (exhaustive matching). Plain `default` is forbidden because it defeats the purpose of exhaustiveness: if a new case is added to the enum later, the `default` arm would silently handle it, often with incorrect behavior. This class of bug — adding a new variant and forgetting to update match sites — is a common source of subtle, hard-to-diagnose errors in languages that allow catch-all defaults on sum types.

To handle only a subset of cases, you have two options:

1. **Range patterns with `break`**: When the unhandled cases are not errors — you simply don't need to act on them — cover them with a range pattern and `break`. This still participates in exhaustiveness checking, so new cases outside the range will be flagged by the compiler.

```maxon
match level 'filter'
		error then handleError()
		fatal then handleFatal()
		trace to warning then break
end 'filter'
```

2. **`default throws` or `default panic("message")`**: When unhandled cases represent genuine errors that should not occur silently:

- **`default throws`** throws the specified error when no other case matches. The enclosing function must declare `throws ErrorType` to use this feature. The error is catchable by the caller.
- **`default panic("message")`** terminates the program with an error message when no other case matches. This is not catchable and should be used for cases that represent programming errors.

Both forms work in all match types (enum and primitive types).

**Statement Form:**

```maxon
function handleShape(shape Shape) throws ShapeError
		match shape 'draw'
				circle(r) then drawCircle(r)
				square(s) then drawSquare(s)
				default throws ShapeError.unsupported
		end 'draw'
end 'handleShape'
```

If `shape` is `triangle`, the function throws `ShapeError.unsupported`, which the caller must handle with `try`.

**Statement Form (panic):**

```maxon
function handleShape(shape Shape)
		match shape 'draw'
				circle(r) then drawCircle(r)
				square(s) then drawSquare(s)
				default panic("unsupported shape")
		end 'draw'
end 'handleShape'
```

If `shape` is `triangle`, the program terminates with the message "unsupported shape".

**Expression Form:**

```maxon
function describeShape(shape Shape) returns String throws ShapeError
		let desc = match shape 'describe'
				circle(r) gives "circle with radius {r}"
				square(s) gives "square with side {s}"
				default throws ShapeError.unsupported
		end 'describe'
		return desc
end 'describeShape'
```

**Example:**

```maxon
enum Shape
		circle(radius float)
		square(side float)
		triangle(base float, height float)
end 'Shape'

enum ShapeError implements Error
		unsupported
end 'ShapeError'

function getArea(shape Shape) returns float throws ShapeError
		return match shape 'calc'
				circle(r) gives 3.14159 * r * r
				square(s) gives s * s
				default throws ShapeError.unsupported
		end 'calc'
end 'getArea'

function main() returns ExitCode
		var shape = Shape.circle(5.0)
		let area = try getArea(shape) otherwise 0.0
		print("{area}")
		return 0
end 'main'
```

**Notes:**
- `default throws` and `default panic("message")` are the only forms of `default` allowed in enum matches -- `default` with arbitrary code on enums is forbidden (error E2046)
- For `default throws`: the error value must be a valid enum case of an `Error`-conforming type, the enclosing function must declare `throws` with a matching error type, and callers must handle the thrown error using `try ... otherwise` or `try` propagation
- For `default panic("message")`: the program terminates immediately with the given message. No `throws` declaration is required.
- Supports all the same features as regular match: associated value extraction, `and fallthrough`, `break`, etc.
- For non-enum matches (int, float, string, char), `default` with arbitrary code remains valid as before

### Break Statement
```maxon
break           // Break from innermost loop
break 'label'   // Break from loop with specified label
```
Exits the innermost loop (while or for), or breaks to a specific labeled loop.

**Example:**
```maxon
while true 'outer'
		while true 'inner'
				break 'outer'  // Breaks out of outer loop
		end 'inner'
end 'outer'
```

### Continue Statement
```maxon
continue           // Continue innermost loop
continue 'label'   // Continue loop with specified label
```
Skips to next iteration of the innermost loop, or continues to a specific labeled loop.

---

## Error Handling

Maxon uses a unified error handling system based on typed errors. Functions either return a value or throw an error—there are no optional types or null values. Error types must be enums conforming to the `Error` interface.

### Defining Error Types

Error types are enums that conform to the `Error` interface:

```maxon
// Simple enum error
enum FileError implements Error
		notFound
		permissionDenied
		alreadyExists
end 'FileError'

// Int-backed enum error (for error codes)
enum HttpError int implements Error
		badRequest = 400
		notFound = 404
		serverError = 500
end 'HttpError'

// String-backed enum error (for messages)
enum ValidationError String implements Error
		emptyField = "Field cannot be empty"
		invalidFormat = "Invalid format"
end 'ValidationError'
```

**Note:** Only enums can conform to `Error`. Attempting to make a type (struct) conform to `Error` produces a compile error (E023).

### Throwing Functions

Functions that can throw errors declare the error type with `throws`:

```maxon
function readFile(path String) returns String throws FileError
		if not exists(path) 'check'
				throw FileError.notFound
		end 'check'
		return contents
end 'readFile'

// Void function that throws
function resetConfig() throws FileError
		if not exists("config.json") 'check'
				throw FileError.notFound
		end 'check'
		// reset logic...
end 'resetConfig'
```

**Syntax:**
```maxon
function name(params) returns ReturnType throws ErrorType
function name(params) throws ErrorType  // void function that throws
```

### Throw Statement

Use `throw` to throw an error value:

```maxon
throw FileError.notFound
throw HttpError.serverError
```

**Rules:**
- `throw` is only valid inside functions with a `throws` declaration
- The thrown value must match the declared error type

### Panic Statement

The `panic` statement immediately terminates the program with an error message and stack trace. It is used to signal unrecoverable errors — situations that represent bugs in the program rather than expected error conditions.

```maxon
panic("something went wrong")
```

The argument can be a plain string literal or an interpolated string. The program prints a panic message to stderr including the source file and line number, followed by a stack trace, then exits with code 1.

```maxon
function processValue(x int) returns int
		if x < 0 'negative'
				panic("processValue: negative input, got {x}")
		end 'negative'
		return x * 2
end 'processValue'
```

Output when called with a negative value:
```text
panic at example.maxon:3: processValue: negative input not allowed
Stack trace:
  in example.processValue
  in example.main
  in _start
```

Use `panic` for invariant violations and unreachable code paths. For expected error conditions (invalid user input, missing files, etc.), use `throw`/`try`/`otherwise` instead.

### Calling Throwing Functions

When calling a function that throws, you must use `try`:

```maxon
// Compile error - must use try
let contents = readFile("config.json")  // ERROR

// Correct - use try with otherwise
let contents = try readFile("config.json") otherwise ""
```

The `try` keyword is always required when calling throwing functions, even when using `otherwise`.

### Handling Errors with `otherwise`

The `otherwise` keyword provides unified error handling for throwing expressions. There are six forms:

#### Default Value Form

Provide a default value when an error occurs:

```maxon
let value = try mayFail() otherwise 42
```

If `mayFail()` throws, `value` is assigned `42`. The default expression must match the return type.

```maxon
function readConfig() returns String
		// If readFile throws, use empty string as default
		let contents = try readFile("config.json") otherwise ""
		return contents
end 'readConfig'
```

#### Ignore Form

Discard errors when you don't need the result:

```maxon
try mayFail() otherwise ignore
```

This silently ignores any thrown error. Use sparingly—typically for cleanup operations where errors can be safely ignored.

```maxon
function cleanup()
		// Best-effort cleanup, ignore failures
		try deleteFile("temp.txt") otherwise ignore
end 'cleanup'
```

#### Panic Form

Crash immediately if an error occurs. Use for unreachable error paths where a failure indicates a bug:

```maxon
let slot = try slots.get(idx) otherwise panic("unreachable: index was validated")
```

This is preferred over a silent default value when the error path should never execute. If it does, the program terminates with a stack trace rather than silently miscompiling or producing wrong results.

#### Single-Statement Form

Run a single statement on the error path. Supported statements are `return`, `break`, `continue`, and `throw`:

```maxon
let value = try mayFail() otherwise return -1
```

Each of these statements terminates the error path, so the success value still flows out of the `try` expression normally. Use single-statement form when the error handler is a single early exit — for anything more complex, use the block form.

```maxon
// Early return on error
function runIt() returns int
		let value = try mayFail() otherwise return -1
		return value
end 'runIt'

// Bail out of a loop on error
while true 'loop'
		let v = try next() otherwise break
		total = total + v
end 'loop'

// Skip failed items
for item in items 'items'
		let parsed = try parse(item) otherwise continue
		results.append(parsed)
end 'items'

// Re-throw as a different error type
function outer() returns int throws OuterError
		let v = try inner() otherwise throw OuterError.failed
		return v
end 'outer'
```

Each statement has the same requirements it normally has: `break`/`continue` must be inside a loop, `throw` requires the enclosing function to declare `throws`, and `return`'s value must match the enclosing function's return type.

#### Block Handler Form

Execute a block of code when an error occurs:

```maxon
try readFile("config.json") otherwise 'handler'
		print("File not found, using defaults")
		useDefaults()
end 'handler'
```

The block executes only if an error is thrown.

```maxon
function loadData() returns int
		var result = 0
		try parseFile("data.txt") otherwise 'err'
				result = -1  // Mark as failed
				logError("Parse failed")
		end 'err'
		return result
end 'loadData'
```

#### Block with Error Binding

Capture the error as a typed enum for inspection:

```maxon
try readFile("config.json") otherwise (e) 'handler'
		match e 'check'
				notFound then print("File not found")
				permissionDenied then print("Permission denied")
				alreadyExists then print("Already exists")
		end 'check'
end 'handler'
```

The error is bound to the variable `e` as a typed enum value, allowing you to match on specific error cases. For error enums with associated values, you can extract the payload in the match arm.

```maxon
function processFile(path String)
		try readFile(path) otherwise (err) 'handler'
				// err contains the FileError value
				print("Failed to read file")
		end 'handler'
end 'processFile'
```

### Error Propagation

Use `try` without `otherwise` to propagate errors to the caller. This is only valid inside functions declared with `throws`:

```maxon
function loadConfig() returns Config throws FileError
		// If readFile throws, the error propagates to our caller
		let contents = try readFile("config.json")
		return parse(contents)
end 'loadConfig'
```

**Rules:**
- `try` without `otherwise` is only valid in functions with `throws`
- The propagated error type must be the same type as the enclosing function's declared error type. Propagation re-throws the callee's error value through the caller's error flag, so a type mismatch would cause the caller to decode bits of one enum as tags of another. If the types differ, add an `otherwise` clause to convert.
- Using `try` without `otherwise` in a non-throwing function is a compile error

### Try Block (Multi-Call Error Handling)

The `try 'label' ... end 'label' otherwise (e) 'handler' ... end 'handler'` construct wraps a sequence of statements so that every throwing call inside funnels its error to a single shared handler. Inside the body, bare calls to throwing functions do **not** require the `try` keyword — the compiler implicitly promotes them.

```maxon
try 'reading'
		let raw = readFile("config.json")
		let parsed = parseJson(raw)
		let port = parsed.get("port")
		print("{port}")
end 'reading'
otherwise (e) 'handler'
		match e 'kind'
				FileError.notFound        then print("missing")
				FileError.permissionDenied then print("denied")
				ParseError.unexpectedToken then print("bad json")
				MapError.missingKey       then print("no port")
		end 'kind'
end 'handler'
```

**Rules:**

- The `try` body MUST contain at least one bare call to a throwing function (E3083).
- The `otherwise` clause takes one of three forms:
	- **Block handler** — `otherwise (e) 'handler' ... end 'handler'` MUST contain a `match` on the binding (E3084).
	- **Terminal panic** — `otherwise [(e)] panic("message")` halts the program when the body throws.
	- **Terminal throws** — `otherwise [(e)] throws ErrorType.case` re-throws a fixed error to the caller. The enclosing function must declare `throws ErrorType`.
- The `(e)` binding is optional for the terminal forms. When supplied it has the same type as in the block-handler form (single enum or synthesized error union) and may be referenced inside the panic message's interpolation or inside the throw expression — for example, `otherwise (e) throws AppError.wrap(e)` to wrap the original error as a payload of the new error case. A binding declared but never read is rejected by the standard unused-variable check (E3012).
- If the body throws calls of a single error enum type, `e` has that enum type and match patterns use bare case names (e.g. `notFound`).
- If the body throws calls of two or more distinct error enum types, `e` has a synthesized error-union type. Each match arm targets a specific `EnumName.caseName` pair:
	- Fully-qualified form `EnumName.caseName` is always accepted.
	- Bare `caseName` is accepted only when the case name is unique across the union members. Shared names (e.g. two enums both with `notFound`) are rejected with E3085.
- The match must be exhaustive across every `(EnumName, caseName)` pair unless a `default` arm is provided.
- An explicit `try expr otherwise ...` inside the body still works for any single call — its error is consumed by its own handler and does not contribute to the synthesized union.
- Nested try blocks compose: the inner block absorbs its own throws; the outer block sees only what its own bare calls throw. A terminal `otherwise throws E.x` inside an inner try block routes through the outer block's shared error sink, just like a bare `throw`.

#### Terminal Form Examples

```maxon
// Panic when the body throws — useful for unreachable error paths.
try 'reading'
		parseFile("data.json")
end 'reading'
otherwise panic("unreachable: data.json is bundled with the binary")

// Re-throw a fixed error to the caller.
function compute() returns int throws AppError
		try 'work'
				doStuff()
		end 'work'
		otherwise throws AppError.failed
		return 0
end 'compute'

// Bind the original error and wrap it as the payload of the new error.
function compute2() returns int throws AppError
		try 'work'
				doStuff()
		end 'work'
		otherwise (e) throws AppError.wrap(e)
		return 0
end 'compute2'
```

### Conditional Try (if try)

The `if try` construct provides conditional execution based on whether a throwing expression succeeds.

#### Boolean Form

Check if an expression succeeds without binding the result:

```maxon
if try mayFail() 'check'
		print("Success!")
end 'check'
```

The if-block executes only if the expression succeeds (doesn't throw).

#### Binding Form

Unwrap and bind the success value:

```maxon
if let value = try mayFail() 'check'
		print("Got: {value}")
end 'check'
```

If successful, the unwrapped value is bound to `value` and available within the if-block.

#### Mutable Binding Form

Use `var` instead of `let` to make the bound name reassignable inside the then-block:

```maxon
if var value = try mayFail() 'check'
		value = value + 10
		return value
end 'check'
```

The binding is scoped to the then-block; mutations do not propagate back to the source expression.

#### With Else Clause

Handle the error case:

```maxon
if try mayFail() 'check'
		print("Success!")
end 'check' else 'err'
		print("Failed!")
end 'err'
```

#### With Error Binding

Capture the error value in the else block:

```maxon
if let value = try mayFail() 'check'
		print("Got: {value}")
end 'check' else (e) 'err'
		print("Error occurred")
end 'err'
```

The error is bound to `e` and available within the else-block.

### Standard Library Error Types

The standard library provides error types for built-in operations:

```maxon
// Array access errors
enum ArrayError implements Error
		indexOutOfBounds  // index >= length
		emptySlot         // slot pointer is null (e.g. after resize() without push())
end 'ArrayError'

// Map operations
enum MapError implements Error
		keyNotFound
		keyAlreadyExists
end 'MapError'

// Iterator exhaustion
enum IterationError implements Error
		exhausted
end 'IterationError'

// File metadata errors
enum FileInfoError implements Error
		notFound              // file does not exist
end 'FileInfoError'
```

Array and Map access methods throw these errors:

```maxon
var arr = [1, 2, 3]
let val = try arr.get(5) otherwise 0  // Returns 0 on out of bounds

var map = ["key": 42]
let result = try map.get("missing") otherwise -1  // Returns -1 if key not found
```

### Error Enum Types

Functions with `throws` return an error type internally:

```maxon
// This function returns "String or FileError" internally
function readFile(path String) returns String throws FileError
```

**Memory Layout:**
```
+--------+--------------------------------+
| tag(8) | value OR error ordinal         |
+--------+--------------------------------+
   0=ok    success value
   1=err   enum ordinal (8 bytes)
```

### Complete Example

```maxon
enum ParseError implements Error
		invalidSyntax
		unexpectedEnd
end 'ParseError'

function parseNumber(s String) returns int throws ParseError
		if s.isEmpty() 'empty'
				throw ParseError.unexpectedEnd
		end 'empty'
		// parsing logic...
		return result
end 'parseNumber'

function main() returns ExitCode
		// Use default value on error
		let num1 = try parseNumber("42") otherwise 0
		
		// Handle error in block
		var num2 = 0
		try parseNumber("invalid") otherwise 'err'
				num2 = -1
		end 'err'
		
		// Handle with error binding
		try parseNumber("") otherwise (e) 'handler'
				print("Parse error occurred")
		end 'handler'
		
		return num1
end 'main'
```

---

## Namespaces

### Automatic Derivation
Namespaces are derived from file paths:

| File Path | Namespace |
|-----------|-----------|
| `math.maxon` | (global) |
| `utils/helpers.maxon` | `utils` |
| `stdlib/fmt/integer.maxon` | `stdlib.fmt` |

### Export Keyword

Functions, types, enums, typealiases, and top-level variables are file-scoped by default. Use the `export` keyword to make them visible to other files:

```maxon
typealias Score = int(i64.min to i64.max)

export function publicAdd(a Score, b Score) returns Score
		return a + b
end 'publicAdd'

function privateHelper(x Score) returns Score
		return x * 2
end 'privateHelper'
```

Only `publicAdd` can be called from other files.

**Exporting types and enums:**

```maxon
typealias Coord = int(i64.min to i64.max)

export type Point
	export var x Coord
	export var y Coord
end 'Point'

export enum Color
	red
	green
	blue
end 'Color'
```

Without `export`, types and enums are only usable within the file where they are declared.

**Exporting typealiases:**

```maxon
export typealias Score = int(0 to 100)
```

Non-exported typealiases are only visible within their file.

**Exporting top-level variables:**

```maxon
export var sharedCounter = 0
export let MAX_CONNECTIONS = 100
```

Exported variables can be read and (for `var`) modified from other files in the same project.

**Exporting methods within types:**

Individual methods can be exported independently of the type itself:

```maxon
typealias Amount = int(i64.min to i64.max)

export type Calculator
	var result Amount

	export function add(n Amount)
		result = result + n
	end 'add'

	function internalReset()
		result = 0
	end 'internalReset'
end 'Calculator'
```

### Module Keyword (directory-scoped visibility)

`module` is a third visibility tier between file-scoped (the default) and `export`. A `module`-marked declaration is visible to every file in the **same directory** as the declaring file AND every file in **any subdirectory** of that directory — but not to files outside that subtree.

```maxon
// project/feature/internal.maxon
module function helper() returns Integer
	return 42
end 'helper'

// project/feature/main.maxon — same directory, can call helper()
function caller() returns Integer
	return helper()
end 'caller'

// project/feature/sub/deep.maxon — subdirectory, can also call helper()
function deepCaller() returns Integer
	return helper()
end 'deepCaller'

// project/other.maxon — outside feature/, CANNOT call helper()
```

`module` and `export` are mutually exclusive — combining them is a parse error. The keyword applies in every position where `export` does: top-level functions, types, enums, unions, typealiases, top-level vars/lets, and per-method or per-field modifiers inside types. A code outside the declarer's directory subtree that tries to use a `module` symbol gets error `E3088: function 'X' is module-scoped and not visible from this directory`.

In Maxon, "module" in this context means a directory subtree — useful for sharing helpers across a feature folder without leaking them to the rest of the program.

### Qualified Names
Call functions with full namespace:
```maxon
var result = stdlib.fmt.format_int(42)
```

### Suffix Matching
If unambiguous, use short name:
```maxon
var result = format_int(42)   // Finds stdlib.fmt.format_int
```

---

## Async/Await (Concurrency)

Maxon supports concurrency via `async` and `await` with green threads scheduled across multiple OS worker threads. Each `async` call spawns a lightweight green thread with a growable stack (starting at 4KB). The runtime uses a GMP (Goroutine-Machine-Processor) scheduler with per-worker local queues, work stealing, and IOCP-based overlapped I/O.

### Spawning Green Threads

Use `async` before a function call to spawn a green thread:

```maxon
var promise = async someFunction(arg1, arg2)
```

The `async` expression returns a promise value that can be awaited later.

### Awaiting Results

Use `await` to wait for a green thread to complete and retrieve its result:

```maxon
var result = await promise
```

If the green thread has already completed, `await` returns immediately. Otherwise, the current thread yields until the result is ready.

### Parallel Execution

Multiple green threads can run concurrently:

```maxon
var p1 = async taskA()
var p2 = async taskB()
var r1 = await p1
var r2 = await p2
```

### Void Functions

Functions that return no value can also be spawned as green threads:

```maxon
var p = async doWork()
await p
```

### Throwing Async Functions

Async functions that throw require `try await` instead of plain `await`:

```maxon
var p = async mayFail(true)
var result = try await p otherwise 0
```

The `try await` syntax supports the same `otherwise` clauses as `try` on synchronous calls:
- `try await p otherwise <default>` -- use a default value on error
- `try await p otherwise panic("msg")` -- panic on error
- `try await p otherwise ignore` -- for void throwing functions
- `try await p otherwise return -1` (or `break`/`continue`/`throw ...`) -- run a single statement on error
- `try await p` -- propagate the error (inside a throwing function)

### Cancellation

A promise can be cancelled via the `.cancel()` method:

```maxon
var p = async longRunning()
p.cancel()
```

Cancelling a green thread stops it at its next yield point. The green thread's stack is freed.

### Typed promises in collections

Promises can be stored in collections and struct fields by declaring an explicit `Promise with T` type. The compiler boxes the green-thread handle into a `Promise<T>` struct at the storage site and unboxes it at the matching `await`. This pattern lets you fan out N tasks and join them in a second loop:

```maxon
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

var arr = IntPromiseArray.create()
for i in 0 upto n 'spawn'
	arr.push(async compute(i))
end 'spawn'
var total = 0
for p in arr 'join'
	total = total + await p
end 'join'
```

### Restrictions

- `async` can only be used on direct function calls (not closures or indirect calls)
- `async` can only target functions that yield (contain I/O operations or `await` points)

### Key Properties

- **Multi-threaded** -- green threads are distributed across OS worker threads (one per CPU core)
- **Work stealing** -- idle workers steal from busy workers' local queues for load balancing
- **Cooperative scheduling** -- context switches at `await` points and I/O operations
- **Growable stacks** -- 4KB initial, doubles when needed
- **Thread-safe memory** -- atomic reference counting and lock-protected shared state
- **Fire-and-forget safe** -- unawaited green threads are drained at program exit

---

## Standard Library

### Core Functions

**I/O Functions**
```maxon
print(value String)                     // Print string to stdout
```

**Math Functions**
```maxon
abs(x float) float              // Absolute value (int auto-promoted to float)
sqrt(x float) float             // Square root
floor(x float) float            // Round toward negative infinity
ceil(x float) float             // Round toward positive infinity
round(x float) float            // Round to nearest (banker's rounding)
trunc(x float) int              // Truncate toward zero
min(a float, b float) float     // Minimum of two values
max(a float, b float) float     // Maximum of two values

// Math library (stdlib) — called as Math.sin(x), Math.cos(x), etc.
Math.sin(x float) float         // Sine (radians)
Math.cos(x float) float         // Cosine (radians)
Math.tan(x float) float         // Tangent (radians)
Math.atan(z float) float        // Arc tangent
Math.atan2(y float, x float) float // Two-argument arc tangent
Math.exp(x float) float         // e^x
Math.log(x float) float         // Natural logarithm
Math.log2(x float) float        // Base-2 logarithm
Math.log10(x float) float       // Base-10 logarithm
Math.pow(base float, exponent float) float // Power
```

**Compile-Time Functions**
```maxon
sizeof(TypeName) int            // Size of a type in bytes (compile-time constant)
```

`sizeof` accepts a type name (not a variable) and returns its storage size in bytes as a compile-time integer constant. Primitive sizes: `int` (8), `float` (8), `bool` (1), `byte` (1). Struct types use 8 bytes per field (minimum 8). Enum types use 8 bytes. Ranged type aliases use the optimal storage width for their range.

**Formatting Functions**
```maxon
format_int(value int) String    // Format int as string
format_float(value float) String // Format float as string
```

### FilePath

`FilePath` is a type-safe wrapper around `String` for filesystem paths. It normalizes path separators to the platform-native format on construction and provides methods for path manipulation.

**Construction:**
```maxon
var p = FilePath from "C:\\Users\\test.txt"              // From string literal (panics on invalid chars)
var q = try FilePath.from("hello.maxon") otherwise ...   // From string (throws FilePathError)
var r = FilePath from "file:///C:/Users/test.txt"        // file:// URLs are converted to paths
var s = try FilePath.from("file:///home/user/f.txt") otherwise ...  // Also works with from()
```

Both `init()` and `from()` transparently accept `file://` URLs, parsing them with `URL.parse()` and extracting the filesystem path. On Windows, the leading `/` before drive letters is stripped (e.g. `/C:/path` becomes `C:\path`). Non-file URL schemes (e.g. `https://`) cause a panic in `init()` or throw `FilePathError.notFileURL` in `from()`.

**Component Extraction:**
```maxon
p.filename()         // "test.txt"
p.fileExtension()    // ".txt"
p.stem()             // "test"
try p.parent()       // FilePath("C:\\Users") — throws FilePathError.noParent if no parent
```

**Path Manipulation:**
```maxon
p.join("docs")                  // Append component with platform separator
p.join(otherFilePath)           // Join with another FilePath
p.changeExtension(".exe")       // Replace file extension
p.normalize()                   // Returns self (normalized on construction)
```

**Query Methods:**
```maxon
p.isEmpty()          // true if path is empty string
p.isAbsolute()       // true for drive paths (C:\) or UNC paths (\\server)
p.isRelative()       // opposite of isAbsolute
```

**Resolution:**
```maxon
p.resolve(base)      // resolve relative path against base; absolute paths returned unchanged
```

**Static Methods:**
```maxon
FilePath.separator()   // Platform-native separator ("\" on Windows, "/" on Linux)
```

`FilePath` implements `Equatable`, `Hashable`, `Stringable`, and `InitableFromStringLiteral`.

All `File` and `Directory` methods accept `FilePath` parameters:
```maxon
let fp = FilePath from "data.txt"
let content = try File.readText(fp) otherwise ...
try File.writeText(fp, content: "hello")
let files = try Directory.list(FilePath from "./") otherwise ...
```

### File

`File` provides static methods for reading, writing, deleting, and querying files. All methods accept `FilePath` parameters. It is defined in `stdlib/File.maxon`.

**Reading Files:**
```maxon
let text = try File.readText(path) otherwise ""           // Read as UTF-8 string (throws FileReadError)
let bytes = try File.readBinary(path) otherwise empty     // Read as ByteArray (throws FileReadError)
```

**Writing Files:**
```maxon
try File.writeText(path, content: "hello") otherwise ...           // Write string (throws FileWriteError)
try File.writeBinary(path, content: data) otherwise ...            // Write bytes (throws FileWriteError)
try File.writeText(path, content: "#!/bin/sh", mode: FilePermission.executable)  // Write with permissions
```

**Query and Delete:**
```maxon
File.exists(path)                                         // Check if file exists (returns bool)
try File.delete(path) otherwise ...                       // Delete file (throws FileDeleteError)
```

**File Metadata:**
```maxon
let info = try File.info(path) otherwise ...              // Get file metadata (throws FileInfoError)
info.size                                                 // FileSize — file size in bytes
info.modifiedTime                                         // Timestamp — last modification (Unix epoch seconds)
info.createdTime                                          // Timestamp — creation time (Unix epoch seconds)
info.accessedTime                                         // Timestamp — last access (Unix epoch seconds)
info.isDirectory                                          // bool — true if path is a directory
info.isReadOnly                                           // bool — true if file is read-only
```

`File.info` retrieves all metadata from a single OS call. Throws `FileInfoError.notFound` when the file does not exist.

**Type Aliases:**
```maxon
typealias FileSize = int(0 to u64.max)     // File size in bytes
typealias Timestamp = int(0 to u64.max)    // Unix epoch seconds
```

**FileInfo Type:**
```maxon
type FileInfo
	export let size FileSize
	export let modifiedTime Timestamp
	export let createdTime Timestamp
	export let accessedTime Timestamp
	export let isDirectory bool
	export let isReadOnly bool
end 'FileInfo'
```

**Error Types:**

| Error | Description |
|-------|-------------|
| `FileReadError.notFound` | File not found when reading |
| `FileWriteError.failed` | Write operation failed |
| `FileDeleteError.notFound` | File not found when deleting |
| `FileInfoError.notFound` | File not found when querying metadata |

**FilePermission Enum:**

| Case | Description |
|------|-------------|
| `normal` | Standard file permissions (0666) |
| `executable` | Executable permissions (0755, Unix) |

### URL

`URL` provides RFC 3986 compliant URI parsing, serialization, and reference resolution. It is defined in `stdlib/URL.maxon`.

**Parsing:**
```maxon
var url = try URL.parse("https://example.com:8080/path?q=1#top") otherwise 'err'
	// handle error
end 'err'
```

**Always-available accessors:**
```maxon
url.scheme()     // "https" (empty string for relative references)
url.path()       // "/path" (always present, may be empty)
```

**Throwing accessors** (throw `URLError.fieldNotPresent` if not set):
```maxon
var host = try url.host() otherwise "default"       // "example.com"
var port = try url.port() otherwise 443             // 8080
var ui = try url.userinfo() otherwise ""            // userinfo before @
var query = try url.query() otherwise ""            // "q=1"
var frag = try url.fragment() otherwise ""          // "top"
```

**Serialization:**
```maxon
url.toString()   // "https://example.com:8080/path?q=1#top"
```

**Reference Resolution** (RFC 3986 Section 5):
```maxon
var base = try URL.parse("http://a/b/c/d?q") otherwise ...
var resolved = try URL.resolve(base, reference: "../g") otherwise ...
resolved.toString()  // "http://a/b/g"
```

**Error Types:**

| Error | Description |
|-------|-------------|
| `URLError.emptyInput` | Input is empty or whitespace-only |
| `URLError.invalidScheme` | Scheme starts with non-alpha or contains invalid characters |
| `URLError.invalidHost` | Malformed host (e.g., unclosed IPv6 bracket) |
| `URLError.invalidPort` | Port is non-numeric or exceeds 65535 |
| `URLError.invalidEncoding` | Malformed percent-encoding (e.g., `%GG`, `%2`) |
| `URLError.relativeWithoutBase` | `resolve()` called with a base URL that has no scheme |
| `URLError.fieldNotPresent` | Accessor called for a component not present in the URL |

`URL` implements `Equatable` and `Stringable`.

### CharacterSet

`CharacterSet` represents a set of characters for use with string trimming and character classification. It is defined in `stdlib/CharacterSet.maxon`.

**Static Factory Methods**

Create a `CharacterSet` using one of the built-in factory methods:

```maxon
var ws = CharacterSet.whitespacesAndNewlines()  // All Unicode whitespace including newlines
var spaces = CharacterSet.whitespaces()         // Spaces and tabs only (no newlines)
var nl = CharacterSet.newlines()               // Newline characters only (LF, CR, CRLF, etc.)
var digits = CharacterSet.decimalDigits()       // Unicode decimal digits (Nd category)
var letters = CharacterSet.letters()            // Unicode letters and marks (L*, M* categories)
var alnum = CharacterSet.alphanumerics()        // Unicode letters, marks, and numbers
var punct = CharacterSet.punctuation()          // Unicode punctuation (P* categories)
var custom = CharacterSet.from(CharSet from ['a', 'e', 'i', 'o', 'u'])  // Custom set
```

**Instance Methods**

```maxon
ws.contains('A')    // false
ws.contains(' ')    // true
```

| Method | Returns | Description |
|--------|---------|-------------|
| `contains(c Character)` | `bool` | Check if the character is in the set |

### Unicode

`Unicode` provides Unicode character classification utilities. It is defined in `stdlib/Unicode.maxon`.

**Static Methods**

```maxon
Unicode.isWhitespace(32)    // true (space)
Unicode.isWhitespace(65)    // false ('A')
```

| Method | Returns | Description |
|--------|---------|-------------|
| `isWhitespace(cp Codepoint)` | `bool` | Check if a codepoint is Unicode whitespace |

### String Trimming

The `String` type provides methods for removing characters from the start and end of a string. Each method has two forms: one that accepts a `CharacterSet` parameter, and a convenience overload that trims whitespace by default.

**Trimming with CharacterSet**

```maxon
"123hello456".trim(CharacterSet.decimalDigits())     // "hello"
"...hello!!!".trim(CharacterSet.punctuation())       // "hello"
"xxxhelloxxx".trimStart(CharacterSet.from(CharSet from ['x']))     // "helloxxx"
"xxxhelloxxx".trimEnd(CharacterSet.from(CharSet from ['x']))       // "xxxhello"
```

**Trimming Whitespace (convenience)**

```maxon
"  hello  ".trim()          // "hello"
"  hello  ".trimStart()     // "hello  "
"  hello  ".trimEnd()       // "  hello"
```

| Method | Returns | Description |
|--------|---------|-------------|
| `trim(in CharacterSet)` | `String` | Remove matching characters from both ends |
| `trimStart(in CharacterSet)` | `String` | Remove matching characters from the start |
| `trimEnd(in CharacterSet)` | `String` | Remove matching characters from the end |
| `trim()` | `String` | Remove whitespace from both ends |
| `trimStart()` | `String` | Remove whitespace from the start |
| `trimEnd()` | `String` | Remove whitespace from the end |

The no-argument convenience methods are equivalent to calling the `CharacterSet` variants with `CharacterSet.whitespacesAndNewlines()`.

### String Append

`String.append` grows a string's buffer in place, avoiding the allocation of a new string. When called with an interpolated string argument, the interpolation parts are written directly into the buffer without materializing a temporary string. Uses a 2x growth strategy for amortized O(1) append.

```maxon
var s = "Hello"
s.append(" World")       // s is now "Hello World"

var name = "World"
s.append(" {name}!")      // interpolation written directly into buffer
```

The compiler also automatically optimizes the pattern `s = "{s}..."` into an in-place append when it detects the string being reassigned to itself with additional content. This means loops like:

```maxon
var s = ""
while condition 'loop'
	s = "{s}{value},"     // automatically optimized to in-place append
end 'loop'
```

are efficient without requiring explicit `append` calls.

| Method | Description |
|--------|-------------|
| `append(other String)` | Append another string's content in place |

### List

`List` is a generic doubly linked list backed by `__ManagedList` (a builtin compiler-synthesized type, like `Array` and `String`) for efficient node management with automatic memory cleanup. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

**Creating a List**

Create a concrete List type with `typealias`, then initialize with `{}`:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

var list = IntList.create()             // Empty list
```

**Adding Elements**
```maxon
list.prepend(1)                  // Add to front — O(1)
list.append(2)                   // Add to back — O(1)
list.insert(1, value: 99)       // Insert at index — O(n)
```

**Accessing Elements**
```maxon
var first = try list.first() otherwise 0   // First element (throws ArrayError)
var last = try list.last() otherwise 0     // Last element (throws ArrayError)
var elem = try list.get(1) otherwise 0     // Element at index (throws ArrayError)
```

**Removing Elements**
```maxon
var removed = try list.removeFirst() otherwise 0  // Remove front — O(1)
var popped = try list.removeLast() otherwise 0    // Remove back — O(1)
var at2 = try list.remove(at: 2) otherwise 0      // Remove at index — O(n)
list.clear()                                       // Remove all elements
```

**Query**
```maxon
list.count()                     // Number of elements
list.isEmpty()                   // true if empty
```

**Iteration**

`List` implements `Iterable`, so it supports `for`-`in` loops:
```maxon
for item in list 'loop'
		print("{item}")
end 'loop'
```

**Complexity Summary**

| Operation | Time |
|-----------|------|
| `prepend` | O(1) |
| `removeFirst` | O(1) |
| `append` | O(1) |
| `removeLast` | O(1) |
| `get`, `insert`, `remove(at:)` | O(n) |
| `first`, `last`, `count`, `isEmpty` | O(1) |
| iteration (for-in) | O(n) total |

### Networking (TcpClient)

`TcpClient` provides TCP client networking with automatic resource cleanup. It is defined in `stdlib/TcpClient.maxon`. The socket is backed by `__ManagedSocket`, a builtin type whose destructor closes the file descriptor when the last reference goes out of scope.

**NetworkPort Alias**

The `NetworkPort` type alias constrains port numbers to the valid TCP range:
```maxon
typealias NetworkPort = int(1 to 65535)
```

**NetworkError**

All networking operations throw `NetworkError`, an enum conforming to `Error`:
```maxon
enum NetworkError implements Error
		resolveFailed       // DNS lookup failed
		connectFailed       // TCP connection refused or timed out
		sendFailed          // OS-level send error
		recvFailed          // OS-level recv error
		connectionClosed    // peer closed the connection
end 'NetworkError'
```

**Connecting**

`TcpClient.connect` resolves the hostname, creates a TCP socket, and connects:
```maxon
let client = try TcpClient.connect("example.com", port: 4242)
```

**Sending Data**

`send` transmits all bytes of a string, looping internally to handle partial sends. It returns the total number of bytes sent:
```maxon
let bytesSent = try client.send("Hello\n")
```

**Receiving Data**

`recv` reads up to `bufferSize` bytes from the connection and returns them as a `String`:
```maxon
let response = try client.recv(1024)
```

**Closing**

`close` is idempotent and safe to call multiple times. The socket also closes automatically when the `TcpClient` goes out of scope:
```maxon
client.close()
```

**API Summary**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Connect to a TCP server |
| `send(data String)` | `ByteCount` | `NetworkError` | Send all bytes of a string |
| `recv(bufferSize ByteCount)` | `String` | `NetworkError` | Receive up to bufferSize bytes |
| `close()` | — | — | Close the connection (idempotent) |

**Example: Simple TCP Client**
```maxon
function main() returns ExitCode
		let client = try TcpClient.connect("localhost", port: 8080) otherwise 'err'
				print("connection failed")
				return 1
		end 'err'
		_ = try client.send("GET / HTTP/1.0\r\n\r\n") otherwise 'err'
				print("send failed")
				return 1
		end 'err'
		let response = try client.recv(4096) otherwise 'err'
				print("recv failed")
				return 1
		end 'err'
		print(response)
		client.close()
		return 0
end 'main'
```

### Builtin Managed Types

The compiler provides several builtin managed types that wrap OS-level resources (file handles, sockets, directory search handles). These types use RAII via destructors: when the last reference to a managed object goes out of scope, the compiler automatically calls the destructor to release the underlying OS resource.

Managed types are not used directly by application code. Instead, stdlib wrapper types (`File`, `Directory`, `TcpClient`) provide the public API. The managed types are documented here for completeness and for stdlib authors.

#### `__ManagedSocket`

Wraps an OS socket file descriptor. Used internally by `TcpClient`. See [Networking (TcpClient)](#networking-tcpclient) for details.

All `__ManagedSocket` methods that can fail at the OS layer throw `__ManagedSocketError` instead of returning sentinel values; callers must wrap them in `try`. `close` stays non-throwing (it is idempotent).

**Static Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `tcpConnect(managed, port)` | `__ManagedSocket` | `__ManagedSocketError` | Resolve hostname and connect a TCP socket. Throws `resolveFailed` when DNS fails, `connectFailed` when the connection is refused. |

**Instance Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `sendFrom(managed, offset, length)` | `int` | `__ManagedSocketError` | Send `length` bytes from the managed buffer at `offset`. Throws `bufferOutOfBounds` if `offset + length > capacity`, `sendFailed` on OS error. |
| `recv(managed)` | `int` | `__ManagedSocketError` | Receive up to `managed.capacity` bytes. Returns `0` when the peer closed gracefully. Throws `recvFailed` on OS error. |
| `close()` | -- | -- | Close the socket handle. Idempotent; also called automatically by the destructor. |

#### `__ManagedFile`

Wraps an OS file handle (Windows `HANDLE` or Linux file descriptor). Used internally by `File`.

All `__ManagedFile` methods that can fail at the OS layer throw `__ManagedFileError` instead of returning sentinel values; callers must wrap them in `try`. `exists` and `close` stay non-throwing (a missing file is a valid answer; close is idempotent).

**Static Methods:**

| Method | Returns | Description |
|--------|---------|-------------|
| `openRead(managed)` | `__ManagedFile` throws `__ManagedFileError` | Open a file for reading. |
| `openWrite(managed)` | `__ManagedFile` throws `__ManagedFileError` | Open a file for writing (creates or truncates). |
| `openWriteExecutable(managed)` | `__ManagedFile` throws `__ManagedFileError` | As `openWrite`, with executable permission bits on Unix. |
| `exists(managed)` | `int` | Check if a file exists. Returns 1 if it does, 0 otherwise. |
| `delete(managed)` | -- throws `__ManagedFileError` | Delete a file. |
| `stat(managed)` | `int` throws `__ManagedFileError` | Return a raw stat-buffer pointer; release with `statFree`. |
| `statField(buffer, index)` | `int` | Read field `index` (0..5) from a stat buffer. Stdlib invariant — panics on null buffer or OOB index. |
| `statFree(buffer)` | -- | Free a stat buffer. Stdlib invariant — panics on null buffer. |

**Instance Methods:**

| Method | Returns | Description |
|--------|---------|-------------|
| `size()` | `int` throws `__ManagedFileError` | Get the file size in bytes. |
| `read(managed, size)` | `int` throws `__ManagedFileError` | Read up to `size` bytes into the managed buffer. Throws `readFailed` if `size > managed.capacity` or on I/O error. |
| `write(managed)` | `int` throws `__ManagedFileError` | Write the managed buffer. Returns bytes written. |
| `close()` | -- | Close the file handle. Idempotent; also called automatically by the destructor. |

The `managed` parameters refer to `__ManagedMemory` buffers (the internal backing store of `String` and `ByteArray`).

#### `__ManagedDirectory`

Wraps an OS directory search handle (Windows `FindFirstFile`/`FindNextFile` or Linux `opendir`/`readdir`). Used internally by `Directory`.

**Static Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `openSearch(managed)` | `__ManagedDirectory` | `__ManagedDirectoryError` | Open a directory search with a glob pattern. Throws `openSearchFailed` if the path does not exist or access is denied. |
| `exists(managed)` | `bool` | -- | Check if a path exists and is a directory. |
| `create(managed)` | -- | `__ManagedDirectoryError` | Create a directory. Throws `createFailed` on failure. |
| `currentPath()` | `__ManagedMemory` | `__ManagedDirectoryError` | Get the current working directory as a managed string. Throws `currentPathFailed` on OS failure. |

**Instance Methods:**

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `filename()` | `__ManagedMemory` | -- | Get the filename of the current search result. Panics on a closed iterator. |
| `next()` | `int` | `__ManagedDirectoryError` | Advance to the next search result. Returns non-zero if found, `0` when no more entries. Throws `nextFailed` on OS error. |
| `close()` | -- | -- | Close the search handle. Idempotent; also called automatically by the destructor. |

---

## Build System

Maxon uses a `build.maxon` file as a script file with exported functions that can be run via `maxon run`. This file serves as both a project root marker and a task runner.

### Project Structure

A Maxon project is defined by the presence of a `build.maxon` file:

```
myproject/
├── build.maxon          # Script file with exported functions
├── main.maxon           # Entry point
├── lib.maxon            # Project file
└── utils/
    └── math.maxon       # Files in subdirectories are included
```

### build.maxon

The `build.maxon` file contains exported functions that serve as runnable commands. Each exported function must return `ExitCode` and must not throw. Private helper functions (without `export`) are not listed or runnable.

Each exported function may be preceded by `///` doc-comment lines; those lines are rendered next to the command name in the `maxon run` listing. Plain `//` comments are treated as in-source notes and are not surfaced to the CLI.

```maxon
/// Build the project.
export function build() returns ExitCode
	let exe = try FilePath.from("maxon") otherwise return 2
	var argv = StringArray.create()
	argv.push("build")
	argv.push(".")
	let result = try Subprocess.run(.path(exe), arguments: argv, workingDirectory: Directory.currentPath(), timeoutMs: 60000) otherwise return 1
	if not result.succeeded() 'failed'
		return 1
	end 'failed'
	return 0
end 'build'

/// Compile the self-hosted compiler and run its spec tests.
export function spec_test_selfhosted() returns ExitCode
	print("Compiling...\n")
	let exe = try FilePath.from("bin/maxon.exe") otherwise return 2
	var argv = StringArray.create()
	argv.push("build")
	argv.push("maxon-selfhosted")
	let result = try Subprocess.run(.path(exe), arguments: argv, workingDirectory: Directory.currentPath(), timeoutMs: 120000) otherwise return 1
	if not result.succeeded() 'failed'
		return 1
	end 'failed'
	return 0
end 'spec_test_selfhosted'

// Private helper - not listed or runnable via maxon run
function log(msg String)
	print(msg)
end 'log'
```

This automatically:
- Sets output to `myapp.exe` in the project root
- Discovers all `.maxon` files in the project directory (recursively)
- Skips directories containing a `.maxonignore` flag file
- Uses default compilation settings

Use `maxon run` to execute exported functions from `build.maxon`:

```bash
# List available commands (names shown with dashes)
maxon run

# Run a specific function (dashes are translated to underscores)
maxon run spec-test-selfhosted

# maxon build is shorthand for maxon run build
maxon build
```

> **Note:** The CLI translates dashes to underscores, so `maxon run spec-test-selfhosted` runs the function `spec_test_selfhosted`. The listing displays function names with dashes for convenience.

### Multi-Project Workspaces

Multiple projects can coexist in a workspace. Each project is isolated by its `build.maxon`:

```
workspace/
├── project-a/
│   ├── build.maxon      # Project A root
│   └── main.maxon
└── project-b/
    ├── build.maxon      # Project B root
    └── main.maxon
```

The LSP automatically detects project boundaries and provides:
- Project-scoped symbol completion
- Cross-file go-to-definition within a project
- Isolated diagnostics per project

---

## Memory Model

### Reference-by-Default Assignment

Assigning a struct-typed variable to another variable copies the **heap pointer**, creating a reference (alias) to the same object:

```maxon
type Point
	export var x int
	export var y int
end 'Point'

var a = Point{x: 1, y: 2}
var b = a               // b is an alias for a -- both point to the same object
b.x = 99
print("{a.x}")          // 99 -- a and b share the same object
```

Field mutation through an alias affects the original, because both variables point to the same heap-allocated object. Reassignment, however, rebinds the variable to a new object without affecting the original:

```maxon
var a = Point{x: 1, y: 2}
var b = a               // alias
b = Point{x: 5, y: 6}  // rebinds b to a new object -- a is unaffected
print("{a.x}")          // 1 -- a still points to the original
```

All types in Maxon use reference semantics on assignment — the variable is rebound to a new value only when reassigned with an expression. The practical distinction is that struct field mutations are visible through aliases, whereas arithmetic on ranged integers and primitives always produces a new value and rebinds the variable, leaving the original unchanged.

### Stack Promotion

The compiler performs escape analysis to identify struct literals that can be safely stack-allocated instead of heap-allocated. A struct is promoted to the stack when all of the following are true:

- All fields are primitive types (no heap-allocated field types)
- Neither the variable nor any alias escapes the function (not returned, stored into a heap field, captured by a closure, or passed to a function that escapes it)
- The `@heap` directive is not used

Stack-promoted structs are freed automatically when the stack frame is reclaimed — no reference counting overhead is incurred. This optimization is transparent and preserves the same semantics as heap allocation.

The `@heap` annotation forces a struct to be heap-allocated, bypassing stack promotion:

```maxon
@heap var p = Point{x: 1, y: 2}  // always heap-allocated
```

`@heap` is only valid on `var` or `let` declarations with struct literal initializers.

### Explicit Cloning

To create an independent deep copy of a struct, use the `.clone()` method:

```maxon
var a = Point{x: 1, y: 2}
var b = a.clone()       // deep copy -- b is independent of a
b.x = 99
print("{a.x}")          // 1 -- a is unchanged
```

The type must implement the `Cloneable` interface to use `.clone()`. See [Cloneable Interface](#cloneable-interface) below.

### Cloneable Interface

The `Cloneable` interface is defined in the standard library:

```maxon
interface Cloneable
	function clone() returns Self
end 'Cloneable'
```

**Auto-conformance:** The compiler automatically generates `Cloneable` conformance for any struct whose fields are all Cloneable types. You do not need to write the conformance manually unless you need custom clone behavior.

**Built-in Cloneable types:**
- All primitives (`int`, `float`, `bool`, `byte`)
- `String`
- `Array` (when the element type is Cloneable)

**When auto-conformance fails:** If a struct contains a field whose type is not Cloneable (such as an enum with associated values), the compiler will not auto-generate conformance. You must implement `clone()` manually or restructure the type.

### Auto-Equatable

The compiler also auto-generates `Equatable` conformance for structs whose fields all implement `Equatable`. The synthesized `equals()` method compares each field using `==` (for primitives) or `.equals()` (for nested structs).

```maxon
type Point
	export var x int
	export var y int
end 'Point'

// Point auto-conforms to Equatable (all fields are primitive)
var a = Point{x: 1, y: 2}
var b = Point{x: 1, y: 2}
if a == b 'equal'           // true -- content equality
	print("equal")
end 'equal'
```

If a struct contains a field that doesn't implement `Equatable` (such as a function type), using `==` produces error E3069.

To compare reference identity (whether two variables point to the same heap object), use the `is` operator:

```maxon
var a = Point{x: 1, y: 2}
var b = a.clone()           // deep copy
var c = a                   // alias (reference to same object)
a is b                      // false -- different objects
a is c                      // true -- same object
```

### Scope Cleanup

When a struct variable goes out of scope, the compiler automatically releases its heap allocation. The runtime uses reference counting: each heap allocation has a refcount header. When a reference is created (via assignment), the refcount is incremented. When a variable goes out of scope, the runtime decrements the refcount and frees the memory if it reaches zero.

```maxon
function compute() returns int
	var a = Point{x: 10, y: 20}  // allocated on heap, refcount = 1
	var b = Point{x: 30, y: 40}  // allocated on heap, refcount = 1
	return a.x + b.y              // a and b released here (refcount -> 0 -> freed)
end 'compute'
```

**Return values:** When a struct is returned from a function, the returned variable is not released at scope exit — the caller takes responsibility for its lifetime.

```maxon
function makePoint() returns Point
	var p = Point{x: 1, y: 2}
	return p                      // p is NOT freed; caller is responsible
end 'makePoint'
```

**Container cleanup:** Containers with heap-allocated elements (e.g., `List with MyStruct`) perform deep cleanup when freed. Each element's refcount is decremented, and elements whose refcount reaches zero are freed recursively. For `List`, the compiler walks all managed list nodes and decrefs their stored values before freeing the managed list itself.

```maxon
typealias TokenList = List with Token

function example() returns int
	var list = TokenList.create()
	list.append(Token{id: 1})   // Token incref'd by the managed list node
	list.append(Token{id: 2})   // Token incref'd by the managed list node
	return 0                     // list freed: each Token decref'd (rc→0→freed),
															 // then managed list nodes freed, then managed list freed
end 'example'
```

### Stack Allocation
- Local variables (`var`, `let`)
- Function parameters
- Automatic lifetime (scope-based)

### Heap Allocation
- Arrays (all array types)
- Strings (when dynamically allocated)
- Automatically freed at end of scope
- No manual `free` or garbage collector needed

### Borrow Checking

The borrow checker prevents mutation of a collection while references to its elements are alive. When you obtain a reference from a mutable source (e.g., `var s = try arr.get(0) otherwise ""`), that source cannot be mutated until the reference is no longer used.

Borrows use non-lexical lifetimes (NLL): a borrow ends at the last use of the borrowing variable, not at the end of its scope.

```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
arr.push("world")    // ERROR E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
```

```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
print("{s}\n")        // last use of s — borrow expires here
arr.push("world")    // OK: borrow has expired
```

The borrow checker also detects indirect mutation through helper functions:

```maxon
function clearList(list StringList)
	list.clear()
end 'clearList'

var val = try list.first() otherwise "none"
clearList(list)       // ERROR E3070: cannot mutate 'list' via 'clearList' while it is borrowed by 'val'
```

### Safety
- No bounds checking on arrays
- No null checks
- Use-after-move prevented at compile time (see Ownership System above)
- Mutation of borrowed collections prevented at compile time (see Borrow Checking above)

### Calling Convention
- Parameters that are only read are passed by value (simple types in registers, arrays as pointer)
- Parameters that are assigned to inside the callee are passed by reference (pointer to caller's storage)
- Return values passed by value (in register or stack)

---

## Code Generation

### Native x86-64 Backend
- Maxon uses a custom x86-64 backend (no LLVM dependency)
- Generates native Windows PE executables directly

### Optimizations
- Constant folding
- Dead code elimination

### Runtime Library
- Located in `maxon-runtime/runtime-windows.obj`
- Provides implementations for intrinsic functions
- Auto-linked with all programs
- No C runtime dependency

---

## Common Patterns

### Main Function Template
```maxon
function main() returns ExitCode
		// program logic
		return 0
end 'main'
```

### Loops with Break
```maxon
var i = 0
while true 'forever'
		if i >= 10 'done'
				break
		end 'done'
		print("{i}")
		i = i + 1
end 'forever'
```

### Array Iteration
```maxon
var numbers = [1, 2, 3, 4, 5]
for i in numbers 'iter'
		var num = try numbers.get(i) otherwise 0
		print("{num}")
end 'iter'
```


### Factorial Example
```maxon
function factorial(n int) returns int
		if n <= 1 'base'
				return 1
		end 'base'
		return n * factorial(n: n - 1)
end 'factorial'

function main() returns ExitCode
		var result = factorial(n: 5)
		print("{result}")  // 120
		return 0
end 'main'
```

---

## Compilation Commands

```bash
# Compile a single file
maxon build hello.maxon

# Build current directory (uses build.maxon if present)
maxon build

# Build a project directory
maxon build myproject/

# Emit IR alongside executable
maxon build app.maxon --emit-ir

# Write IR at each pipeline stage
maxon build app.maxon --dump-stages

# List available commands from build.maxon
maxon run

# Run an exported function from build.maxon (dashes translate to underscores)
maxon run spec-test-selfhosted

# Run spec fragment tests
maxon spec-test

# Run tests matching a pattern
maxon spec-test --filter=array

# Run tests with verbose output
maxon spec-test --verbose
```

---

## Error Handling

### Compile-Time Errors

**Type Errors**
```maxon
var x = 5 + "string"    // ERROR: Type mismatch
```

**Missing Return**
```maxon
function test() returns int
		var x = 5
		// ERROR: Missing return statement
end 'test'
```

**Immutable Assignment**
```maxon
let x = 5
x = 10                  // ERROR: Cannot assign to immutable variable
```

**Self-Assignment**
```maxon
var x = 5
x = x                   // ERROR: self-assignment has no effect
```

**Borrow Conflict**
```maxon
var arr = ["hello"]
var s = try arr.get(0) otherwise ""
arr.push("world")       // ERROR E3070: cannot mutate 'arr' while borrowed by 's'
```

**Var Never Reassigned**
```maxon
var x = 10
return x                // ERROR E3077: variable 'x' is never reassigned; use 'let' instead of 'var'
```

**Var From Immutable**
```maxon
let a = Point.create(x: 1, y: 2)
var b = a               // ERROR E3078: cannot assign immutable variable 'a' to mutable binding 'b'
```

**Useless Discard**
```maxon
_ = 42              // ERROR: discarding a non-call expression has no effect
```

**Unknown Keyword**
```maxon
functon test()          // ERROR: Unknown keyword 'functon'
```

**Mismatched Block Identifiers**
```maxon
if x > 0 'check'
		print("{x}")
end 'wrong'             // ERROR: Expected 'check', got 'wrong'
```

**Empty Block**
```maxon
if x > 0 'check'
end 'check'             // ERROR E3082: empty block
```
Every `if`, `else`, `while`, `for`, and `try...otherwise` block must contain at least one statement. Comment-only blocks are also considered empty since comments are not statements.

### Runtime Behavior

**Caught at runtime (clean panic, exit 1)**

CPU faults — division (or modulo) by zero, null pointer dereference, and stack overflow — are caught by the runtime safety handler, which writes a `panic: ...` line to stderr and exits with status 1.

**Undefined Behavior (no error)**
- Array out-of-bounds access
- Wrap-around on regular signed/unsigned integer arithmetic

---

## Best Practices for AI Agents

1. **Always match block identifiers**: `if x > 0 'check'` must end with `end 'check'`

2. **Initialize all variables**: No uninitialized variables allowed

3. **Use clear initializers**: Type is always inferred from the value
   ```maxon
	 var count = 0    // Type inferred as int
   ```

4. **Return from all code paths**:
   ```maxon
	 function test(x int) returns int
			 if x > 0 'pos'
					 return 1
			 end 'pos'
			 return -1        // Don't forget this
	 end 'test'
   ```

5. **Remember int/float distinction**:
   ```maxon
	 var x = 5           // int
	 var y = 5.0         // float (note decimal point)
   ```

7. **Prefer `let` for immutability**:
   ```maxon
	 let pi = 3.14159    // Prevents accidental modification
   ```

8. **Export only necessary functions**:
   ```maxon
	 export returns int    // Public API
	 returns int      // Private helper
   ```

9. **Handle array access errors**:
   ```maxon
	 // Use otherwise for safe access with default
	 var val = try arr.get(index) otherwise 0

	 // Or check bounds first
	 if index < arr.count() 'safe'
			 var val = try arr.get(index) otherwise 0
	 end 'safe'
   ```

10. **Use meaningful block identifiers**:
    ```maxon
		while not done 'process'     // 'process' describes the loop
				// ...
		end 'process'
    ```

11. **First argument positional, rest named**:
    ```maxon
		greet("Alice")                        // Single param is positional
		connect("localhost", port: 8080)      // First positional, rest named
		move(start, end: end)                 // First positional, rest named
    ```

12. **Parameters with defaults can be omitted**:
    ```maxon
		function greet(name String, title String = "Mr.")
		greet("Smith")                // Uses default
		greet("Smith", title: "Dr.")  // Override default
    ```

13. **Use `try otherwise` for error handling**:
    ```maxon
		let value = try mayFail() otherwise 42  // Default value on error
		try cleanup() otherwise ignore          // Ignore errors in cleanup

		// Use block handler for complex error handling
		try loadData() otherwise 'err'
				logError("Failed to load data")
				useDefaults()
		end 'err'
    ```

14. **Propagate errors with `try` in throwing functions**:
    ```maxon
		function process() returns Result throws WorkflowError
				let data = try loadData()  // Propagates error to caller
				return transform(data)
		end 'process'
    ```

---

**End of Reference**
