---
feature: string-interpolation
status: stable
keywords: [string, interpolation, formatting, toString]
category: strings
---

# String Interpolation

## Documentation

String interpolation allows embedding expressions directly within string literals, automatically converting values to their string representation.

### Basic Syntax

Use curly braces `{expr}` to embed any expression in a string:

```maxon
var name = "World"
print("Hello, {name}!")  // "Hello, World!"

var x = 42
print("The answer is {x}\n")  // "The answer is 42"
```

### Expression Interpolation

Any valid expression can be embedded:

```maxon
var a = 5
var b = 3
print("{a} + {b} = {a + b}\n")  // "5 + 3 = 8"

print("Double: {a * 2}\n")  // "Double: 10"
```

### Built-in Type Support

All built-in types are automatically convertible to strings:

```maxon
// Integers
print("Count: {42}\n")  // "Count: 42"

// Floats
print("Pi: {3.14159}\n")  // "Pi: 3.14159"

// Booleans
print("Active: {true}\n")  // "Active: true"
```

### Negative Numbers

Unary operators work inside interpolation:

```maxon
print("Temp: {-10} degrees")  // "Temp: -10 degrees"
print("Value: {-3.5}\n")  // "Value: -3.5"
```

### Escape Sequences

To include literal braces, escape them with backslash:

```maxon
print("Use \{expr\} syntax")  // "Use {expr} syntax"
```

### Enum Types

Enum values can be interpolated directly. For int-backed enums, the numeric value is shown. For simple enums, the case name is shown. For string-backed enums, the raw string value is displayed:

```maxon
// Int-backed enum (type inferred from values)
enum Color
	red = 1
	green = 2
	blue = 3
end 'Color'

var c = Color.green
print("Color value: {c}\n")  // "Color value: 2"

// String-backed enum (type inferred from values)
enum Status
	active = "Active"
	inactive = "Inactive"
end 'Status'

var s = Status.active
print("Status: {s}\n")  // "Status: Active"
```

### Custom Types

Custom types can be interpolated by implementing the `Stringable` interface:

```maxon
typealias Score = int(i64.min to i64.max)

type Point implements Stringable
	var x as Score
	var y as Score

	function toString() returns String
		return "({self.x}, {self.y})"
	end 'toString'

	static function create(x Score, y Score) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

var p = Point.create(1, y: 2)
print("Location: {p}\n")  // "Location: (1, 2)"
```

### Migration from Concatenation

String concatenation with `+` is not supported. Use interpolation instead:

```maxon
// Before (not supported):
// var msg = "Hello, " + name + "!"

// After (use interpolation):
var msg = "Hello, {name}!"
```

## Tests

### Basic Variable Interpolation

<!-- test: basic-variable -->
```maxon
function main() returns ExitCode
	let name = "World"
	print("Hello, {name}!")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

### Multiple Variables

<!-- test: multiple-variables -->
```maxon
function main() returns ExitCode
	let first = "Hello"
	let second = "World"
	print("{first}, {second}!")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

### Integer Interpolation

<!-- test: integer-interpolation -->
```maxon
function main() returns ExitCode
	let x = 42
	print("Value: {x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Value: 42
```

### Integer Literal Interpolation

<!-- test: integer-literal -->
```maxon
function main() returns ExitCode
	print("Answer: {42}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Answer: 42
```

### Negative Integer

<!-- test: negative-integer -->
```maxon
function main() returns ExitCode
	let x = -5
	print("Negative: {x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Negative: -5
```

### Negative Unary Expression

<!-- test: negative-unary -->
```maxon
function main() returns ExitCode
	print("Value: {0-10}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Value: -10
```

### Float Interpolation

<!-- test: float-interpolation -->
```maxon
function main() returns ExitCode
	let pi = 3.14159
	print("Pi: {pi}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Pi: 3.14159
```

### Float Literal Interpolation

<!-- test: float-literal -->
```maxon
function main() returns ExitCode
	print("Value: {2.5}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Value: 2.5
```

### Negative Float

<!-- test: negative-float -->
```maxon
function main() returns ExitCode
	let temp = -3.5
	print("Temp: {temp}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Temp: -3.5
```

### Signed Zero and Full Significance

`-0.0` is a different double from `0.0` and prints with its sign. A value that needs all 17
significant digits gets all 17: `0.1 + 0.2` is not `0.3`, and printing it as `0.3` would say it was.

<!-- test: float-signed-zero-and-significance -->
```maxon
function main() returns ExitCode
	let negativeZero = -0.0
	print("{negativeZero}\n")
	print("{0.0}\n")
	print("{0.1 + 0.2}\n")
	print("{1.0 / 3.0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-0.0
0.0
0.30000000000000004
0.3333333333333333
```

### Boolean True Interpolation

<!-- test: bool-true -->
```maxon
function main() returns ExitCode
	let flag = true
	print("Active: {flag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Active: true
```

### Boolean False Interpolation

<!-- test: bool-false -->
```maxon
function main() returns ExitCode
	let flag = false
	print("Active: {flag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Active: false
```

### Boolean Literal Interpolation

<!-- test: bool-literal -->
```maxon
function main() returns ExitCode
	print("Yes: {true}, No: {false}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Yes: true, No: false
```

### Expression Interpolation

<!-- test: expression-interpolation -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	print("{a} + {b} = {a + b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 + 3 = 8
```

### Complex Expression

<!-- test: complex-expression -->
```maxon
function main() returns ExitCode
	let x = 10
	print("Double: {x * 2}, Triple: {x * 3}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Double: 20, Triple: 30
```

### Parenthesized Expression

<!-- test: parenthesized-expression -->
```maxon
function main() returns ExitCode
	let a = 2
	let b = 3
	print("Result: {(a + b) * 2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Result: 10
```

### Empty String Parts

<!-- test: empty-parts -->
```maxon
function main() returns ExitCode
	let x = 42
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

### Adjacent Interpolations

<!-- test: adjacent-interpolations -->
```maxon
function main() returns ExitCode
	let a = "Hello"
	let b = "World"
	print("{a}{b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
HelloWorld
```

### Three Adjacent Interpolations

<!-- test: three-adjacent -->
```maxon
function main() returns ExitCode
	let a = "A"
	let b = "B"
	let c = "C"
	print("{a}{b}{c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ABC
```

### String Variable Interpolation

<!-- test: string-variable -->
```maxon
function main() returns ExitCode
	let greeting = "Hello"
	let target = "World"
	let msg = "{greeting}, {target}!"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

### Escaped Braces

<!-- test: escaped-braces -->
```maxon
function main() returns ExitCode
	print("Use \{expr\} for interpolation")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Use {expr} for interpolation
```

### Mixed Escaped and Interpolation

<!-- test: mixed-escaped -->
```maxon
function main() returns ExitCode
	let x = 42
	print("Value \{x\} is {x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Value {x} is 42
```

### Interpolation in Loop

<!-- test: interpolation-loop -->
```maxon
function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		print("Count: {i}\n")
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Count: 0
Count: 1
Count: 2
```

### Function Call in Interpolation

<!-- test: function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	print("Double of 5: {double(5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Double of 5: 10
```

### Method Call in Interpolation

<!-- test: method-call -->
```maxon
function main() returns ExitCode
	let s = "hello"
	print("Length: {s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Length: 5
```

### Comparison in Interpolation

<!-- test: comparison-interpolation -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	print("a > b: {a > b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a > b: true
```

### Logical Expression

<!-- test: logical-expression -->
```maxon
function main() returns ExitCode
	let x = true
	let y = false
	print("x and y: {x and y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x and y: false
```

### Float Arithmetic

<!-- test: float-arithmetic -->
```maxon
function main() returns ExitCode
	let r = 2.0
	print("Area: {3.14159 * r * r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Area: 12.56636
```

### Mixed Types

<!-- test: mixed-types -->
```maxon
function main() returns ExitCode
	let name = "test"
	let count = 5
	let active = true
	print("Name: {name}, Count: {count}, Active: {active}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Name: test, Count: 5, Active: true
```

### Large Integer

<!-- test: large-integer -->
```maxon
function main() returns ExitCode
	let big = 2147483647
	print("Max int: {big}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Max int: 2147483647
```

### Zero Values

<!-- test: zero-values -->
```maxon
function main() returns ExitCode
	let i = 0
	let f = 0.0
	print("Int: {i}, Float: {f}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Int: 0, Float: 0.0
```

### Newline in String with Interpolation

<!-- test: newline-interpolation -->
```maxon
function main() returns ExitCode
	let x = 42
	print("Line1: {x}\nLine2: done")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Line1: 42
Line2: done
```

### Tab in String with Interpolation

<!-- test: tab-interpolation -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	print("{a}\t{b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1	2
```

### Custom Type with Stringable

<!-- test: custom-stringable -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair implements Stringable
	var first as Integer
	var second as Integer

	function toString() returns String
		return "[{first}, {second}]"
	end 'toString'

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(1, second: 2)
	print("{p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[1, 2]
```

### Stringable with Format Specifier

⭐ **THESE TWO CASES WERE ONE DISABLED CASE, AND SPLITTING THEM IS THE POINT (R10d).** It bundled a half
that works today with a half the parser cannot even read, so the whole thing was disabled and *this spec*
carried no live case for the working half — *"in the corpus but never executed"*, which reads as coverage in
a listing and provides none. The halves have different blockers, so they are different cases.

<!-- test: stringable-and-formatted-interp-selects-the-zero-arg-overload -->
Plain `{c}` on a type implementing **both** protocols must select `toString()`, not `toString(format)`.

⚠ **THE FORMATTED OVERLOAD IS DECLARED FIRST, AND THAT ORDER IS THE ASSERTION (R10d review).** The first
declaration of a name keeps the BARE registration key (`Parser.overloadRegistrationNameFor`), and a call
whose overload resolution finds nothing is left holding that bare key — `SemanticCheck.selectOverload`'s
`noMatch` arm says so in as many words. So with `toString()` written first this program prints `42` whether
the resolver selected it or never ran at all, and the case could not tell a SELECTION from an ABSENCE of one.
With `toString(format String)` holding the bare key, only an actual retarget by
`SemanticCheck.resolveOverloadedCalls` reaches the zero-argument body, and the two-argument fallback returns
a different string so a mis-selection is visible rather than coincidentally correct.

⚠ **The behaviour itself is pinned a second time, deliberately and NOT redundantly**, by
`specs-shv2/interface-conformance.md`'s `overloaded-tostring-satisfies-stringable-and-formatted` (R10) —
that case asks it of the CONFORMANCE checker with a constant-returning fixture, this one asks it of the
INTERPOLATION materializer with a body that itself interpolates a field. Two cases, one fact: **move one and
the other must move with it.**
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter implements Stringable, FormattedStringable
	var value as Integer

	function toString(format String) returns String
		if format == "verbose" 'verbose'
			return "Counter(value={value})"
		end 'verbose'

		return "two-arg:{value}"
	end 'toString'

	function toString() returns String
		return "{value}"
	end 'toString'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(42)

	print("{c}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: stringable-format-spec -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter implements Stringable, FormattedStringable
	var value as Integer

	function toString() returns String
		return "{value}"
	end 'toString'

	function toString(format String) returns String
		if format == "verbose" 'verbose'
			return "Counter(value={value})"
		end 'verbose'

		return "two-arg:{value}"
	end 'toString'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(42)

	print("{c:verbose}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
Counter(value=42)
```

### Multiple Stringable Types

<!-- test: multiple-stringable -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Name implements Stringable
	var first as String
	var last as String

	function toString() returns String
		return "{first} {last}"
	end 'toString'

	static function create(first String, last String) returns Self
		return Self{first: first, last: last}
	end 'create'
end 'Name'

type Age implements Stringable
	var years as Integer

	function toString() returns String
		return "{years} years old"
	end 'toString'

	static function create(years Integer) returns Self
		return Self{years: years}
	end 'create'
end 'Age'

function main() returns ExitCode
	let name = Name.create("John", last: "Doe")
	let age = Age.create(30)
	print("{name}, {age}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
John Doe, 30 years old
```

### Struct Interpolation Without toString

A user struct interpolated with `"{s}"` must have a `toString` method (by name); a struct without one
is E3016, positioned at the interpolated expression.

<!-- test: error.interp-struct-without-tostring -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Plain
	var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Plain'

function main() returns ExitCode
	let p = Plain.create(5)
	print("{p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:15:10: Type 'Plain' used in string interpolation must have a toString method
```

### Int-Backed Enum Interpolation

<!-- test: int-enum-interpolation -->
```maxon
enum Color
	red = 1
	green = 2
	blue = 3
end 'Color'

function main() returns ExitCode
	let c = Color.green
	print("Color value: {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Color value: 2
```

### Simple Enum Interpolation

<!-- test: simple-enum-interpolation -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let d = Direction.east
	print("Direction: {d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Direction: east
```

### String-Backed Enum Interpolation

<!-- test: string-enum-interpolation -->
```maxon
enum Status
	active = "Active"
	inactive = "Inactive"
	pending = "Pending"
end 'Status'

function main() returns ExitCode
	let s = Status.active
	print("Status: {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Status: Active
```

### Multiple Enum Interpolations

<!-- test: multiple-enum-interpolation -->
```maxon
enum Priority
	low = 1
	medium = 2
	high = 3
end 'Priority'

function main() returns ExitCode
	let p1 = Priority.low
	let p2 = Priority.high
	print("Priorities: {p1} and {p2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Priorities: 1 and 3
```

### Float-Backed Enum Interpolation

A float-backed enum's tag IS the f64's IEEE-754 bit pattern (`Project.EnumLayout`), so the hole must
DECODE it rather than hand the integer renderer an encoding. Before A4o this printed
`4612811918334230528` — the bits of 2.5 read as a decimal integer — where the oracle printed `2.5`.

<!-- test: float-enum-interpolation -->
```maxon
enum Weight
	light = 2.5
	heavy = 4.0
end 'Weight'

function main() returns ExitCode
	let w = Weight.light
	print("Weight: {w}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Weight: 2.5
```

### Float-Backed Enum Interpolation - Negative and Signed Zero

The spellings where reading the encoding is most obviously wrong. Before A4o `down` printed
`-4611235658464650854` — a NEGATIVE integer, because a negative double's sign bit is the i64's — and
`zero` printed `0`, which is the one value the two readings agree on and therefore the one that could
never have found this.

<!-- test: float-enum-interpolation-extremes -->
```maxon
enum Scale
	huge = 1.0e300
	down = -2.2
	zero = 0.0
end 'Scale'

function main() returns ExitCode
	print("{Scale.down} {Scale.zero}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-2.2 0.0
```

### An Int-Backed and a Float-Backed Enum in One String

⭐ THE NEGATIVE CONTROL for the decode above: the two backings must reach two DIFFERENT renderers in one
interpolation. A fix that routed every enum to the float renderer would print `200.0` here, and a
compiler that never decodes would print the bits of 2.5 — so neither reading passes this case.

<!-- test: int-and-float-enum-interpolation -->
```maxon
enum Code
	ok = 200
	missing = 404
end 'Code'

enum Weight
	light = 2.5
end 'Weight'

function main() returns ExitCode
	print("{Code.ok} {Weight.light} {Code.missing}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200 2.5 404
```

### Cross-File Float-Backed Enum Interpolation

The backing is a fact about the DECLARATION, so a file that only USES the enum must reach the same
renderer as the file that declares it — the whole-program enum registry is what carries it, and nothing
local to the declaring parse survives to answer here.

<!-- test: cross-file-float-enum-interpolation -->
```maxon
// --- file: weights.maxon
export enum Weight
	light = 2.5
	heavy = 4.0
end 'Weight'

// --- file: main.maxon
function main() returns ExitCode
	print("{Weight.heavy}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4.0
```

### Integer Format Specifier - Zero Padding

<!-- test: int-format-zero-pad -->
```maxon
function main() returns ExitCode
	let n = 42
	print("{n:04}\n")
	let m = 7
	print("{m:04}\n")
	let big = 12345
	print("{big:04}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0042
0007
12345
```

### Integer Format Specifier - Hex

<!-- test: int-format-hex -->
```maxon
function main() returns ExitCode
	let n = 255
	print("{n:x}\n")
	print("{n:X}\n")
	let m = 0
	print("{m:x}\n")
	let big = 65535
	print("{big:x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ff
FF
0
ffff
```

### Integer Format Specifier - Zero-padded unsigned bases

Zero-padding shifts the converted digits right and fills the gap, and it starts the fill
after a `-` sign when there is one. An unsigned base never writes a sign, so it must say
so: arm64 left its `is_negative` slot holding the hex letter base ('a'), the fill read
that as "negative", started at index 1, and overwrote the digit it had just shifted —
`{255:08x}` printed `f000000f`. Width alone did not catch it (`{high:016x}` is already 16
chars wide, so it never pads); only a value SHORTER than its field reaches the fill loop.

<!-- test: int-format-zero-padded-unsigned -->
```maxon
function main() returns ExitCode
	let n = 255
	print("{n:08x}\n")
	print("{n:08X}\n")
	print("{n:06o}\n")
	print("{n:012b}\n")
	// Space fill and the signed path share the same shift-and-fill code.
	print("{n:8x}\n")
	let neg = -42
	print("{neg:08d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
000000ff
000000FF
000377
000011111111
      ff
-0000042
```

### Integer Format Specifier - Hex High Bit (unsigned bases)

<!-- test: int-format-high-bit-unsigned -->
```maxon
function main() returns ExitCode
	// Values with bit 63 set exceed i64.max (negative as signed i64). The
	// hex/octal/binary formatter must treat the value as unsigned bits and emit
	// every digit — a signed `<= 0` loop guard or signed div/rem would bail out
	// early and print only zero-padding.
	let high = 1 shl 63
	print("{high:016x}\n")
	let allBits = -1
	print("{allBits:x}\n")
	let upper = 11 shl 60
	print("{upper:X}\n")
	let mixed = 15 shl 60
	print("{mixed:o}\n")
	print("{high:b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8000000000000000
ffffffffffffffff
B000000000000000
1700000000000000000000
1000000000000000000000000000000000000000000000000000000000000000
```

### Integer Format Specifier - Width

<!-- test: int-format-width -->
```maxon
function main() returns ExitCode
	let n = 42
	print("[{n:6}]\n")
	print("[{n:2}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[    42]
[42]
```

### Integer Format Specifier - Negative Zero Padding

<!-- test: int-format-neg-zero-pad -->
```maxon
function main() returns ExitCode
	let n = -42
	print("{n:06}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-00042
```

### Integer Format Specifier - A Field Wider Than Any Buffer

⭐ **A WIDTH IS A NUMBER THE PROGRAM ASKED FOR, NOT A BUDGET THE RUNTIME SET.** The formatted
integer converter used to write into a fixed 72-byte scratch and take the field width from the
spec with nothing checking it against that size, so `"{n:200}"` wrote 200 bytes into 72. It
neither crashed nor reported anything: the NEXT part's block was carved inside the overrun and
the first part then read those bytes back as its own padding, so `"A<{a:200}>B<{b:X}>"` printed
`DEADBEEF` in the middle of the spaces and lost every character after it. A field is built in
`stdlib` over a String that grows, so a width has no ceiling left to breach.

<!-- test: int-format-wide-field -->
```maxon
function main() returns ExitCode
	let a = 7
	let b = 3735928559
	print("A<{a:200}>B<{b:X}>\n")
	print("{a:120}|\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
A<                                                                                                                                                                                                       7>B<DEADBEEF>
                                                                                                                       7|
```

### Integer Format Specifier - Int-Backed Enum

A bare `"{e}"` on an enum with explicit integer backing values renders that value, so a format
specifier on one has exactly the meaning it has on any other integer. It used to be dropped in
silence — `"{code:08}"` printed `404` rather than `00000404`, and `"{code:x}"` printed `404`
rather than `194` — because the enum arm of interpolation took no format specifier at all.

<!-- test: int-format-enum-backing -->
```maxon
enum ErrorCode
	ok = 0
	notFound = 404
end 'ErrorCode'

function main() returns ExitCode
	let code = ErrorCode.notFound
	print("[{code}]\n")
	print("[{code:08}]\n")
	print("[{code:x}]\n")
	print("[{code:6}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[404]
[00000404]
[194]
[   404]
```

### A specifier on a NAME- or TEXT-rendering enum is ignored

⭐ **A FORMAT SPECIFIER IS A NUMERIC ONE, SO IT REACHES ONLY THE ARM THAT RENDERS A NUMBER.**
`int-format-enum-backing` above pins the raw-number arm, where the specifier APPLIES. These are the
other two arms, and they are the ones that break SILENTLY: an enum declaring no raw values renders
its CASE NAME and a string-backed one renders its DECLARED TEXT, and neither is a number to pad or
to re-base. Nothing pinned them before — `enum-rawvalue-format` writes `.rawValue` explicitly, so it
never reaches the enum arm at all — which is exactly how a shared integer-rendering path can start
padding a case name without a single test noticing. Oracle-agreed on this source.

<!-- test: format-spec-on-a-text-rendering-enum-is-ignored -->
```maxon
enum Plain
	red
	green
end 'Plain'

enum Text
	active = "Active"
	idle = "Idle"
end 'Text'

function main() returns ExitCode
	let p = Plain.green
	let t = Text.idle
	print("[{p}] [{p:6}] [{p:08}] [{p:x}]\n")
	print("[{t}] [{t:6}] [{t:08}] [{t:x}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[green] [green] [green] [green]
[Idle] [Idle] [Idle] [Idle]
```


### Integer Format Specifier - Unsigned Decimal

⭐ **A FORMAT SPECIFIER NEVER CHANGES WHICH NUMBER IS BEING PRINTED.** An `int(0 to u64.max)`
value with bit 63 set is a large positive number, and `"{u}"`, `"{u:d}"` and `"{u:25}"` must all
say so. The formatted converter used to re-derive signedness from the sign BIT instead of taking
it from the type the compiler already knew, so `"{u:d}"` read `u64.max` as `-1` — the same value
the unformatted spelling printed as `18446744073709551615`. Signedness is decided once, by the
compiler, and handed to the renderer.

<!-- test: int-format-unsigned-decimal -->
```maxon
typealias Wide = int(0 to u64.max)

function show(u Wide)
	print("[{u}] [{u:d}] [{u:25}] [{u:x}]\n")
end 'show'

function main() returns ExitCode
	show(u64.max)
	// Bit 63 alone — the value a SIGNED reading calls i64.min. Written as a shift because a bare
	// literal is an `int`, and 9223372036854775808 is past the end of one (E2011).
	show(1 shl 63)
	show(42)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[18446744073709551615] [18446744073709551615] [     18446744073709551615] [ffffffffffffffff]
[9223372036854775808] [9223372036854775808] [      9223372036854775808] [8000000000000000]
[42] [42] [                       42] [2a]
```

### Unsigned Decimal Interpolation - the specifier-free half

The three specifier-bearing thirds of the case above arrive with the format-specifier rung; this is
its first third, which needs no specifier and therefore runs today. It is the rule that case states,
asked of the ONE rendering shv2 has: **signedness is a property of the value's DECLARED type, read
once by the compiler and handed to the renderer**, so a value whose type admits no negative is
rendered by a converter that does no sign handling at all.

`1 shl 63` is not decoration. It is the value a signed reading calls `i64.min`, and it is the second
way this can be wrong: a renderer that negates instead of dividing unsigned would overflow on it
where `u64.max` merely comes out as `-1`.

<!-- test: unsigned-decimal-interpolation -->
```maxon
typealias Wide = int(0 to u64.max)

function show(u Wide)
	print("[{u}]\n")
end 'show'

function main() returns ExitCode
	show(u64.max)
	show(1 shl 63)
	show(42)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[18446744073709551615]
[9223372036854775808]
[42]
```

### Unsigned Decimal via toString

`x.toString()` on a builtin-typed receiver IS the interpolation path — one `materializeInterpExpr`
chooses the converter for both spellings — so a second selection site would show up here and nowhere
else. This is what pins that there is only one.

<!-- test: unsigned-decimal-tostring -->
```maxon
typealias Wide = int(0 to u64.max)

function show(u Wide)
	print("[{u.toString()}]\n")
end 'show'

function main() returns ExitCode
	show(u64.max)
	show(1 shl 63)
	show(42)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[18446744073709551615]
[9223372036854775808]
[42]
```

### A value that admits a negative still renders signed

The control the rule above cannot do without: a rung that only proves the new answer cannot see that
it broke the old one. Neither of these values DECLARES a non-negative range — one has no ranged type
at all (which means the whole of `i64`, not "no constraint"), and one declares a range that spans
zero — so both keep the signed renderer and both still print `-1`.

<!-- test: signed-value-still-renders-signed -->
```maxon
typealias Signed = int(i64.min to i64.max)

function show(s Signed)
	print("[{s}]\n")
end 'show'

function main() returns ExitCode
	let n = -1
	print("[{n}]\n")
	show(-1)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[-1]
[-1]
```

### A folded non-negative constant still renders signed

A constant that happens to be `>= 0` **fits** an unsigned rendering and does not **ask** for one —
the same distinction that keeps `7 / 2` a signed divide. Both renderings agree on every value a
non-negative constant can hold, so this case's stdout cannot tell them apart and its committed
fragment is what records which converter was emitted. It is here so that a future widening of the
rule from "declares it" to "fits it" moves a golden that somebody has to explain.

<!-- test: folded-non-negative-constant-renders-signed -->
```maxon
function main() returns ExitCode
	let n = 42
	print("[{n}] [{7 + 1}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[42] [8]
```

### A declared non-negative range below i64.max prints what it always printed

The unsigned renderer reaches every declared non-negative type, not only the ones a signed reading
gets wrong — that uniformity is what makes the rule one rule. So these two must print exactly what
they printed before: a narrow `int(0 to 255)` alias and the builtin `ExitCode`, whose values fit an
`i64` with room to spare and therefore render identically under either converter.

<!-- test: narrow-non-negative-alias-renders-unchanged -->
```maxon
typealias Small = int(0 to 255)

function show(s Small, code ExitCode)
	print("[{s}] [{code}]\n")
end 'show'

function main() returns ExitCode
	show(200, code: 7)
	show(0, code: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[200] [7]
[0] [0]
```

### Float Format Specifier - Precision

<!-- test: float-format-precision -->
```maxon
function main() returns ExitCode
	let f = 3.14159
	print("{f:.2}\n")
	print("{f:.4}\n")
	let g = 2.0
	print("{g:.3}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.14
3.1416
2.000
```

### Float Format Specifier - Width and Precision

<!-- test: float-format-width-precision -->
```maxon
function main() returns ExitCode
	let f = 3.14
	print("[{f:8.2}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[    3.14]
```

### Enum Raw Value Format Specifier

<!-- test: enum-rawvalue-format -->
```maxon
enum ErrorCode
	ok = 0
	notFound = 404
	serverError = 500
end 'ErrorCode'

function main() returns ExitCode
	let code = ErrorCode.notFound
	print("E{code.rawValue:04}\n")
	let ok = ErrorCode.ok
	print("E{ok.rawValue:04}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
E0404
E0000
```

### Multiple control-flow-opening interpolations in one string

Regression: each `{...}` fragment is parsed into the block control flow
currently lands in, not the string's original entry block. A fragment whose
expression opens blocks (a `try ... otherwise` here) leaves control in its
merge block; the next fragment and the trailing literal parts must chain off
that merge block. Resetting to the entry block per fragment orphaned the first
fragment's `try` merge block — leaving it without a terminator, which tripped
`assertAllBlocksTerminated` (a parser-internal panic) before any user
diagnostic could be reported.

<!-- test: multiple-try-fragments -->
```maxon
typealias Idx = int(i64.min to i64.max)
typealias IdxList = List with Idx

function main() returns ExitCode
	var a = IdxList.create()
	a.append(10)
	a.append(20)
	print("a={try a.get(0) otherwise -1} b={try a.get(1) otherwise -1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=10 b=20
```

### Error: Unescaped Brace in String

An unescaped `{` in a string literal that is not part of an interpolation expression produces a clear error. Use `\{` for literal braces.

<!-- test: error.unescaped-brace -->
```maxon
function main() returns ExitCode
	print("Expected '{' here")
	return 0
end 'main'
```
```maxoncstderr
error E1006: specs/fragments/string-interpolation/error.unescaped-brace.test:3:19: Unescaped '{' in string literal — use '\{' for a literal brace
```

### Error: An unclosed format specifier stops at the line, and does not eat the next statement

⛔⛔ **A MISSING `}` AFTER A FORMAT SPECIFIER SILENTLY DELETED THE FOLLOWING STATEMENT.** shv2-authored
regression, and the worst answer a compiler can give: this exact program **compiled clean, exited 0 and
printed `a1`** — line 5's entire `print` was gone, with no diagnostic from the lexer or the parser. The
specifier's scanner had no newline bound while every other quoted body in the lexer has one, so it ate
`")`, the newline and `print("b{y`, stopped at the NEXT line's `}`, and the `"` after that closed the
string tidily. The garbage in between then read as a legal specifier (width 0, decimal), because nothing
in it is a digit or a base letter.

⚠ **The control is the same program with the `:` removed**, which has always been refused at the same
code, position and message (it reaches `scanInterp`'s own newline handling instead). The two must agree:
a format specifier is not a place where a missing brace becomes invisible.

<!-- test: error.unclosed-format-spec -->
```maxon
function main() returns ExitCode
	let x = 1
	let y = 2
	print("a{x:")
	print("b{y}")
	return 0
end 'main'
```
```maxoncstderr
error E1006: <fragment>:5:10: Unescaped '{' in string literal — use '\{' for a literal brace
```

### Error: operator '+' on String produces a semantic error, not a compiler crash

Maxon's `String` doesn't overload `+`; string concatenation is done through interpolation
(`"{a}{b}"`). Applying `+` to two strings must produce a clear semantic error instead
of crashing in the binop constructor.

<!-- disabled-test: error.plus-on-string -->
<!-- MEASURED 2026-09-04: `E2004: Cannot operate on String and String` where the pin is `E3005: operator '+' is not
     defined for type 'String'`. Same verdict, and shv2's code and noun are both the wrong ones: this is a TYPE
     rule, not an undefined name. -->
```maxon
function main() returns ExitCode
	let a = "foo"
	let b = "bar"
	let c = a + b
	print("{c}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/string-interpolation/error.plus-on-string.test:5:12: operator '+' is not defined for type 'String'
```

### Interpolation temporary is dropped per loop iteration

An unbound interpolation result (`print("{i}")`) is an owned heap String owned by the STATEMENT that
produced it. In a loop body each iteration allocates a fresh one, and the statement-scoped drop must free
it inside the body, every iteration — not once at scope exit. A single scope-exit drop would leak every
iteration but the last; a wrong drop of an already-freed value would be worse. This authored regression
runs the loop long enough that any per-iteration leak drives `__mm_alloc_count` above zero and the leak
gate reports exit 101 instead of 0.

<!-- test: interp-temporary-dropped-per-loop-iteration -->
```maxon
function main() returns ExitCode
	var i = 0
	while i < 8 'loop'
		print("iter {i}\n")
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
iter 0
iter 1
iter 2
iter 3
iter 4
iter 5
iter 6
iter 7
```

### Interpolation temporary in a nested block is dropped at statement end

A temporary owned interpolation result created inside an `if` block is dropped at the end of its own
statement — which is INSIDE the block — so it is leak-free even though the block emits no scope-exit drop.
This is the case the deferred `closeBlock` gap does NOT cover: a nested-block temporary works (the drop
rides the statement), where a nested-block BOUND value would leak (its drop would have to ride the block's
`end`, which is P1.4). Authored to pin that distinction.

<!-- test: interp-temporary-in-nested-block -->
```maxon
function main() returns ExitCode
	let n = 7
	if n > 0 'inside'
		print("val={n}\n")
		print("twice {n}{n}\n")
	end 'inside'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
val=7
twice 77
```

<!-- test: float-ranged-alias-positions -->
A float that reaches `"{x}"` through a ranged ALIAS — as a parameter, as a field, as a return —
interpolates as a float.

The question is not idle: `tagIsIntegral` answers `true` for a `named` tag on purpose, so a float
still carrying one would be claimed by the integer arm of the interpolation lowering and printed as
its IEEE bit pattern in decimal. Nothing reaches that arm, because a ranged float alias is resolved
to a `float` tag before the lowering sees it. This pins the three positions where an alias survives
longest, so the day one of them stops resolving it is a failing case rather than a wrong number.
```maxon
typealias Percent = float(0.0 to 100.0)

type Reading
	export var pct as Percent

	static function create(pct Percent) returns Reading
		return Self{pct: pct}
	end 'create'
end 'Reading'

function pick(r Reading) returns Percent
	return r.pct
end 'pick'

function show(p Percent)
	print("param={p}")
	print("\n")
end 'show'

function main() returns ExitCode
	let r = Reading.create(12.5)
	show(r.pct)
	print("field={r.pct}")
	print("\n")
	print("ret={pick(r)}")
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
param=12.5
field=12.5
ret=12.5
```

### Error: An interpolation expression must consume everything up to its closing brace

`{...}` holds ONE expression. Anything the expression does not consume is an error, exactly as
it would be outside a string — `let x = 1 zzz` has never been legal, and an interpolation is not
a place where trailing tokens become invisible. This is the one pin that keeps the two compilers
agreeing about what such a program MEANS: the bootstrap dropped the leftovers and printed `7`.

<!-- test: error.interp-trailing-tokens -->
```maxon
function main() returns ExitCode
	print("{7 zzz}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:12: Expected 'interpolation end' but got 'zzz'
```

### Error: An exponent without a decimal point is not a float literal

A float literal must contain a decimal point, so `1e100` lexes as the integer `1` followed by the
identifier `e100`. Outside a string that identifier is E2001; inside one the bootstrap used to DROP
it, and `print("{1e100}")` printed `1` — a number a hundred orders of magnitude wrong, with no
diagnostic. Write `1.0e100`.

<!-- test: error.interp-exponent-without-point -->
```maxon
function main() returns ExitCode
	print("{1e100}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:11: Expected 'interpolation end' but got 'e100'
```

### The expression boundary is exact in both directions

The refusal above must not cost any ordinary interpolation. A bare name, a method call, a call with
named arguments, an operator expression, escaped braces, surrounding whitespace, a `toString` struct
and a brace-nested struct literal all still parse.

<!-- test: interp-expression-boundary-forms -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pt
	var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'

	// The nested struct literal has to be written from inside the type — E3076 forbids it
	// anywhere else — so the brace-nested interpolation lives here rather than in main.
	static function printNested()
		print("[{Pt{x: 7}}]\n")
	end 'printNested'

	function px() returns Integer
		return self.x
	end 'px'

	function toString() returns String
		return "Pt({self.x})"
	end 'toString'
end 'Pt'

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	let a = 3
	let b = 4
	let p = Pt.create(1)
	print("[{a}][{p.px()}][{add(1, b: 2)}][{a + b}][\{lit\}][{ a }][{p}]\n")
	Pt.printNested()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[3][1][3][7][{lit}][3][Pt(1)]
[Pt(7)]
```

### The expression boundary is exact around a format specifier

The `:` that opens a format specifier is the one place a token legitimately follows the expression,
so the boundary rule and the specifier split are the same decision read twice. In shv2 they are
literally one decision: `Lexer.scanInterp` finds that `:` at brace depth zero AND group depth zero,
and the specifier is everything from it to the closing `}`.

⚠ It is split out of `interp-expression-boundary-forms` above, where canonical writes `[{a:8}]` as one
more form in that case's single `print`. The split dates from when this half could not parse; the two
cases together assert exactly what canonical's one asserts.

<!-- test: interp-expression-boundary-format-spec -->
```maxon
function main() returns ExitCode
	let a = 3
	print("[{a:8}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[       3]
```
