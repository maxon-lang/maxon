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
	var x Score
	var y Score

	function toString() returns String
		return "({self.x}, {self.y})"
	end 'toString'

	static function create(x Score, y Score) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

var p = Point.create(x: 1, y: 2)
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
	var first Integer
	var second Integer

	function toString() returns String
		return "[{first}, {second}]"
	end 'toString'

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(first: 1, second: 2)
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
	var value Integer

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
	let c = Counter.create(value: 42)
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
	var first String
	var last String

	function toString() returns String
		return "{first} {last}"
	end 'toString'

	static function create(first String, last String) returns Self
		return Self{first: first, last: last}
	end 'create'
end 'Name'

type Age implements Stringable
	var years Integer

	function toString() returns String
		return "{years} years old"
	end 'toString'

	static function create(years Integer) returns Self
		return Self{years: years}
	end 'create'
end 'Age'

function main() returns ExitCode
	let name = Name.create(first: "John", last: "Doe")
	let age = Age.create(years: 30)
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
