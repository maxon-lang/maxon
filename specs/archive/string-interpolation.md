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

Enum values can be interpolated directly. For int-backed or simple enums, the numeric value is shown. For string-backed enums, the raw string value is displayed:

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
type Point is Stringable
  var x int
  var y int

  function toString(format String) returns String
    return {self.y})"
  end 'toString'
end 'Point'

var p = Point{x: 1, y: 2}
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
function main() returns int
  var name = "World"
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
function main() returns int
  var first = "Hello"
  var second = "World"
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
function main() returns int
  var x = 42
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
function main() returns int
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
function main() returns int
  var x = 0 - 5
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
function main() returns int
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
function main() returns int
  var pi = 3.14159
  print("Pi: {pi}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Pi: 3.141589
```

### Float Literal Interpolation

<!-- test: float-literal -->
```maxon
function main() returns int
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
function main() returns int
  var temp = 0.0 - 3.5
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
function main() returns int
  var flag = true
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
function main() returns int
  var flag = false
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
function main() returns int
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
function main() returns int
  var a = 5
  var b = 3
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
function main() returns int
  var x = 10
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
function main() returns int
  var a = 2
  var b = 3
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
function main() returns int
  var x = 42
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
function main() returns int
  var a = "Hello"
  var b = "World"
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
function main() returns int
  var a = "A"
  var b = "B"
  var c = "C"
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
function main() returns int
  var greeting = "Hello"
  var target = "World"
  var msg = "{greeting}, {target}!"
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
function main() returns int
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
function main() returns int
  var x = 42
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
function main() returns int
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
function double(x int) returns int
  return x * 2
end 'double'

function main() returns int
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
function main() returns int
  var s = "hello"
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
function main() returns int
  var a = 5
  var b = 3
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
function main() returns int
  var x = true
  var y = false
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
function main() returns int
  var r = 2.0
  print("Area: {3.14159 * r * r}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Area: 12.566359
```

### Mixed Types

<!-- test: mixed-types -->
```maxon
function main() returns int
  var name = "test"
  var count = 5
  var active = true
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
function main() returns int
  var big = 2147483647
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
function main() returns int
  var i = 0
  var f = 0.0
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
function main() returns int
  var x = 42
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
function main() returns int
  var a = 1
  var b = 2
  print("{a}\t{b}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

### Custom Type with Stringable

<!-- test: custom-stringable -->
```maxon
type Pair is Stringable
  var first int
  var second int

  function toString(_ String) returns String
    return "[{first}, {second}]"
  end 'toString'
end 'Pair'

function main() returns int
  var p = Pair{first: 1, second: 2}
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
type Counter is Stringable
  var value int

  function toString(format String) returns String
    if format == "verbose" 'verbose'
      return "Counter(value={value})"
    end 'verbose'
    return "{value}"
  end 'toString'
end 'Counter'

function main() returns int
  var c = Counter{value: 42}
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
type Name is Stringable
  var first String
  var last String

  function toString(_ String) returns String
    return "{first} {last}"
  end 'toString'
end 'Name'

type Age is Stringable
  var years int

  function toString(_ String) returns String
    return "{years} years old"
  end 'toString'
end 'Age'

function main() returns int
  var name = Name{first: "John", last: "Doe"}
  var age = Age{years: 30}
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

function main() returns int
  var c = Color.green
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

function main() returns int
  var d = Direction.east
  print("Direction: {d}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
Direction: 2
```

### String-Backed Enum Interpolation

<!-- test: string-enum-interpolation -->
```maxon
enum Status
  active = "Active"
  inactive = "Inactive"
  pending = "Pending"
end 'Status'

function main() returns int
  var s = Status.active
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

function main() returns int
  var p1 = Priority.low
  var p2 = Priority.high
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
