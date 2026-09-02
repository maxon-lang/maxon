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

### How a Float is Spelled

A float interpolates as the **shortest decimal that reads back as the same double** — the fewest digits
that identify the value uniquely, as Python 3, JavaScript, Rust, Go, Swift, Java and .NET Core all
print it. Three consequences the goldens across this corpus depend on:

- **Always a fractional part, and never an exponent.** `100.0`, not `100`; the least subnormal spells
  out in full rather than as `4.9e-324`. A printed float is always text Maxon can read back as a float.
- **`-0.0` keeps its sign**, because it is a distinct double from `0.0`.
- **Every digit is significant.** A value that is not exactly 2 can never print as `2.0`, and a value
  that IS 2 can never print as `1.999999`. (The fixed-six-decimal printer this replaced did both:
  `log10(100.0)` is `1.9999999999999996` and it printed `1.999999`, while `f64.max` saturated an i64
  and printed `9223372036854775807.999999`.)

An infinity prints `inf` / `-inf` and a NaN prints `nan`, unsigned — none of the three has a literal
that could read back, so the spelling is only about being unmistakable.

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

### Union Types

A union interpolates by exactly the same rule as an enum. The rule is stated once, over the whole
enum family, and it turns on how the value is **represented**:

- A union with **no payload-carrying case** is represented as a bare discriminant, exactly like an
  enum. It interpolates to its **raw value** if its cases were given explicit scalar raw values, and
  to its **case name** otherwise.
- A union with **any** payload-carrying case is heap-boxed, and interpolates to the **name of its
  live case**. This holds for every case of such a union, payload-carrying or not, and it holds even
  if some case was given an explicit raw value — a heap-boxed union prints case names throughout.
  It also covers struct-backed unions, whose backing is compile-time metadata rather than a scalar.

Representation is the deciding fact rather than "does this case have a raw value", because a union
may mix the two: `union { a = 1001, b(s String) }` is accepted, and `a` prints `a`, not `1001`.

```maxon
typealias Coord = int(i64.min to i64.max)

union Shape
	empty
	point(x Coord)
end 'Shape'

var a = Shape.empty
print("{a}\n")  // "empty"

var b = Shape.point(7)
print("{b}\n")  // "point"

union Code            // all cases bare, so scalar backing is available
	lexer = 1001
	parser = 2001
end 'Code'

print("{Code.parser}\n")  // "2001" — the raw value, as for an int-backed enum
```

The payload is deliberately **not** rendered, for two reasons:

- A union's discriminant is total — every value has exactly one live case name — while its payloads
  are per-case and heterogeneous. The case name is the only thing every union value has, so it is
  the only rule that stays well-defined as cases are added.
- Rendering payloads would make a union interpolable only when *every* payload type of *every* case
  is itself renderable. Adding one case with an unrenderable payload would then break every existing
  `"{u}"` site for that union — action at a distance from an unrelated edit.

The case name is also stable under the edit that adds the *first* payload to a union. A union whose
cases are all payload-less is stored as a bare discriminant, while one with any payload is stored as
a heap record; case-name rendering is the same rule across that representation change, so adding a
payload to one case does not silently alter how the *other* cases print.

To render a payload, match on the union and interpolate the binding:

```maxon
let text = match b 'm'
	point(x) gives "point({x})"
	empty gives "empty"
end 'm'
```

As with enums, a `toString()` method declared on the union is **not** consulted by interpolation —
the case name is used regardless. Use `match` when you need a custom rendering.

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

### Extreme Magnitudes

`f64.max` and the least subnormal are the two ends of the shortest-round-trip rule, and they are the
two the fixed-six-decimal printer this replaced could not express at all: it converted through an i64
integer part, so `f64.max` saturated and printed `9223372036854775807.999999`, and the subnormal
underflowed to `0.0`. Fixed notation costs some zeros and buys a value that can always be read back.

<!-- test: float-extreme-magnitudes -->
```maxon
function main() returns ExitCode
	print("{1.7976931348623157e308}\n")
	print("{4.9e-324}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
179769313486231570000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.0
0.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005
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
		return "{value}"
	end 'toString'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(42)
	print("{c}\n")
	print("{c:verbose}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
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

### Union Interpolation - Payload-less Case

A union with any payload-carrying case is stored as a heap record (`tag@0`, payload slots from 8).
The payload-less cases of such a union interpolate to their case name, exactly as an enum does.

Regression: interpolation dispatched on the lowered *representation* (a heap pointer) before the
*value kind* (an enum), so a union reached the struct path, which reads offsets 0 and 8 as a
`__ManagedMemory`'s buffer and length. For `empty` that read tag 0 as the buffer and the zeroed
payload slot as the length, copying 0 bytes and rendering the empty string.

<!-- test: union-payloadless-interpolation -->
```maxon
typealias Coord = int(i64.min to i64.max)

union Shape
	empty
	point(x Coord)
end 'Shape'

function main() returns ExitCode
	let s = Shape.empty
	print("Shape: [{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Shape: [empty]
```

### Union Interpolation - Payload-carrying Case

The same struct-path misdispatch read the tag of a payload-carrying case as a buffer address: for
`point` the tag is 1, so the copy read from address 0x1 and the program died with a nil-pointer
panic before printing anything.

<!-- test: union-payload-interpolation -->
```maxon
typealias Coord = int(i64.min to i64.max)

union Shape
	empty
	point(x Coord)
end 'Shape'

function main() returns ExitCode
	let s = Shape.point(7)
	print("Shape: [{s}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Shape: [point]
```

### Union Interpolation - Payload Kinds Do Not Affect Rendering

Only the discriminant is read, so a managed payload (`String`) and a multi-slot payload render
exactly like a scalar one. A `String` payload is the case that would fault most readily under the
old struct path, since its slot holds a real heap pointer that would be copied from as text.

<!-- test: union-payload-kinds -->
```maxon
typealias Coord = int(i64.min to i64.max)

union Node
	leaf
	named(label String)
	pair(a Coord, b Coord)
end 'Node'

function main() returns ExitCode
	let a = Node.leaf
	let b = Node.named("hello")
	let c = Node.pair(1, b: 2)
	print("{a} {b} {c}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
leaf named pair
```

### Union Interpolation - Error Bound by `otherwise`

Unions are the idiomatic error type when throw sites carry context, so the handler binding is the
main road for this path, not a corner. The binding `e` is the union value itself.

<!-- test: union-error-otherwise-binding -->
```maxon
typealias Code = int(i64.min to i64.max)

union OpError implements Error
	notFound
	badCode(code Code)
end 'OpError'

function risky() returns Code throws OpError
	throw OpError.badCode(42)
end 'risky'

function main() returns ExitCode
	try risky() otherwise (e) 'h'
		print("caught: [{e}]\n")
	end 'h'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
caught: [badCode]
```

### Union Interpolation - All Cases Payload-less

A union with no payload at all is stored as a bare discriminant rather than a heap record. It
already rendered its case name, and must keep doing so: the fix must not move this shape onto the
heap-record path.

<!-- test: union-all-payloadless-interpolation -->
```maxon
union Flat
	alpha
	beta
end 'Flat'

function main() returns ExitCode
	let a = Flat.alpha
	let b = Flat.beta
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha beta
```

### Union Interpolation - Every Route a Union Value Arrives By

A heap-boxed union reaches interpolation through several distinct lowerings, each of which builds
the heap pointer at a different place: a locally constructed value, a function parameter, a call
return, a payload unpacked from an enclosing union, and a struct field read through `self`. All of
them must find the discriminant in the same slot, so all of them are exercised here rather than
just the local-construction path the other tests use.

<!-- test: union-interpolation-value-routes -->
```maxon
typealias Wide = int(i64.min to i64.max)

union Inner
	zero
	one(v Wide)
end 'Inner'

union Outer
	nothing
	wrap(i Inner)
end 'Outer'

type Holder
	export var cur as Inner

	function show() returns Wide
		print("field=[{self.cur}]\n")
		return 0
	end 'show'

	static function create(cur Inner) returns Self
		return Self{cur: cur}
	end 'create'
end 'Holder'

function viaParam(s Inner) returns Wide
	print("param=[{s}]\n")
	return 0
end 'viaParam'

function viaReturn() returns Inner
	return Inner.one(5)
end 'viaReturn'

function main() returns ExitCode
	let p = Inner.one(3)
	print("paramrc={viaParam(p)}\n")
	print("ret=[{viaReturn()}]\n")
	let o = Outer.wrap(Inner.zero)
	print("outer=[{o}]\n")
	let nested = match o 'm'
		wrap(inner) gives "nested=[{inner}]"
		nothing gives "none"
	end 'm'
	print("{nested}\n")
	let h = Holder.create(Inner.one(9))
	print("showrc={h.show()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
param=[one]
paramrc=0
ret=[one]
outer=[wrap]
nested=[zero]
field=[one]
showrc=0
```

### Union Interpolation - Struct-backed Union with Payloads

A struct-backed union carries compile-time metadata as its backing *and* runtime payloads. Its
backing is not a scalar, so there is no raw value to print and the case name is used — the same
answer the struct-backed *enum* path already gives. Its discriminant is still the ordinal, so this
shape faulted identically before the fix (case `add` is ordinal 0, so the copy read address 0x0).

<!-- test: union-struct-backed-interpolation -->
```maxon
typealias Latency = int(0 to 50)
typealias ID = int(i64.min to i64.max)

type OpMeta
	export let latency as Latency
end 'OpMeta'

union Instruction
	add(dest ID, src ID) = OpMeta{latency: 1}
	nop = OpMeta{latency: 0}
end 'Instruction'

function main() returns ExitCode
	let op = Instruction.add(1, src: 2)
	print("{op}\n")
	let n = Instruction.nop
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
add
nop
```

### Union Interpolation - Scalar-backed Union Renders Its Raw Value

A union whose cases are all payload-less may give each case an explicit scalar raw value, which
makes it representationally identical to an int-backed enum — and it renders like one. This pins the
boundary of the case-name rule: the fix must route only *heap-boxed* unions to case names, leaving
the scalar-backed shape reading its raw value.

<!-- test: union-scalar-backed-interpolation -->
```maxon
union Code
	lexer = 1001
	parser = 2001
end 'Code'

function main() returns ExitCode
	let c = Code.parser
	print("{c}\n")
	let l = Code.lexer
	print("{l}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2001
1001
```

### Union Interpolation - Explicit Raw Value Mixed With a Payload Case

A union may give one case an explicit scalar raw value *and* give another a payload; the parser
accepts it. Such a union is heap-boxed, so the case-name rule governs the whole type — `a` renders
`a`, not `1001`. This is why the rule above is stated over the representation rather than over "does
this case have a raw value": the two answers differ precisely here.

Note the discriminant is not the list index in this shape. Auto-increment resumes from the explicit
value, so `b` is stored as 1002 while its position is 1 — anything that dispatches on a union's tag
has to compare against the stored discriminant, not the case's ordinal position.

<!-- test: union-mixed-rawvalue-and-payload -->
```maxon
union U
	a = 1001
	b(s String)
end 'U'

function main() returns ExitCode
	let x = U.a
	print("{x}\n")
	let y = U.b("hello")
	print("{y}\n")
	print("{y.rawValue}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
1002
```

### Union Interpolation - Alongside Enums in One String

Enum rendering must not regress when a union shares the string: the two kinds are dispatched by the
same code, and an int-backed enum still renders its raw value while a union renders its case name.

<!-- test: union-and-enum-interpolation -->
```maxon
typealias Coord = int(i64.min to i64.max)

enum Level
	low = 10
	high = 20
end 'Level'

enum Bare
	first
	second
end 'Bare'

union Shape
	empty
	point(x Coord)
end 'Shape'

function main() returns ExitCode
	let lvl = Level.high
	let bare = Bare.second
	let shape = Shape.point(3)
	print("{lvl} {bare} {shape}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20 second point
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
	let neg = 0 - 42
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
	let allBits = 0 - 1
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

### Error: operator '+' on String produces a semantic error, not a compiler crash

Maxon's `String` doesn't overload `+`; string concatenation is done through interpolation
(`"{a}{b}"`). Applying `+` to two strings must produce a clear semantic error instead
of crashing in the binop constructor.

<!-- test: error.plus-on-string -->
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

### Error: An interpolation expression must consume everything up to its closing brace

`{...}` holds ONE expression. Anything the expression does not consume is an error, exactly as
it would be outside a string — `let x = 1 zzz` has never been legal, and an interpolation is not
a place where trailing tokens become invisible.

<!-- test: error.interp-trailing-tokens -->
```maxon
function main() returns ExitCode
	print("{7 zzz}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/string-interpolation/error.interp-trailing-tokens.test:3:12: Expected 'interpolation end' but got 'zzz'
```

### Error: An exponent without a decimal point is not a float literal

A float literal must contain a decimal point, so `1e100` lexes as the integer `1` followed by the
identifier `e100` — a fact `lexer-edge-cases`' `float-exponent-eof` already pins. Outside a string
that identifier is E2001; inside one it used to be DROPPED, and `print("{1e100}")` printed `1` — a
number a hundred orders of magnitude wrong, with no diagnostic. Write `1.0e100`.

<!-- test: error.interp-exponent-without-point -->
```maxon
function main() returns ExitCode
	print("{1e100}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/string-interpolation/error.interp-exponent-without-point.test:3:11: Expected 'interpolation end' but got 'e100'
```

### Error: A backslash-escaped quote inside a hole is refused AT THE BACKSLASH

Inside `{...}` the hole is expression context, so a string argument is written with bare quotes —
`"{shout("hi")}"` compiles and runs. A backslash there is not an escape, it is an unexpected token,
and the refusal must say so at the backslash. It is reported that way when one candidate is in scope;
with an overload set the same input reported `E3007 Ambiguous overload` at the CALL instead, blaming
overload resolution for a lexical fault and naming candidates that were never the problem.

<!-- test: error.interp-escaped-quote-with-overloads -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(count Integer) returns String
	return "int: {count}"
end 'process'

function process(text String) returns String
	return "str: {text}"
end 'process'

function main() returns ExitCode
	print("{process(\"hello\")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/string-interpolation/error.interp-escaped-quote-with-overloads.test:13:18: Expected expression but got '\'
```

### The expression boundary is exact in both directions

The refusal above must not cost any ordinary interpolation. A bare name, a format specifier, a method
call, a call with named arguments, an operator expression, escaped braces, surrounding whitespace, a
`toString` struct and a brace-nested struct literal all still parse — and the last of those is why the
format specifier's `:` is found at brace depth zero as well as paren depth zero: the `:` inside
`Pt{x: 7}` belongs to the struct literal, not to a format spec.

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
	print("[{a}][{a:8}][{p.px()}][{add(1, b: 2)}][{a + b}][\{lit\}][{ a }][{p}]\n")
	Pt.printNested()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[3][       3][1][3][7][{lit}][3][Pt(1)]
[Pt(7)]
```
