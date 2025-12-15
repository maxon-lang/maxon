---
feature: string-interpolation
status: stable
keywords: [string, interpolation, formatting, toString]
category: strings
---

# String Interpolation

## Developer Notes

String interpolation allows embedding expressions within string literals using `{expr}` syntax. The compiler converts each embedded expression to a string and concatenates all parts into the final result.

### Implementation

**Lexer** (`lexer.cpp`):
- `readStringLiteral()` detects unescaped `{` in strings
- Returns `STRING_INTERP_START`, `STRING_INTERP_MIDDLE`, or `STRING_INTERP_END` tokens
- Tracks nesting depth for braces, parens, brackets to handle complex expressions
- Format specifiers parsed after `:` at depth 0

**AST** (`ast.h`):
- `InterpolatedStringPart` struct with `isExpression`, `literalValue`, `expr`, `formatSpec`
- `InterpolatedStringExprAST` contains vector of parts

**Parser** (`parser_expr.cpp`):
- `parseInterpolatedString()` builds AST from token sequence
- Handles alternating literal and expression parts

**Semantic Analyzer** (`semantic_analyzer_expr.cpp`):
- Type-checks each embedded expression
- Verifies expressions implement `Stringable` interface or are built-in types
- String `+` concatenation disabled with helpful error message

**Code Generation** (`codegen_mir_expr_string.cpp`):
- `generateInterpolatedString()` converts expressions via `toString()`
- Built-in type intrinsics: `__int_toString`, `__float_toString`, `__bool_toString`
- When no format spec provided, passes `nil` to `toString()` (not empty string)
- When format spec is provided (e.g., `{value:fmt}`), passes format string to `toString()`
- Concatenates all parts into single heap-allocated result
- Intermediate results tracked for cleanup at scope exit

### Memory Management

- Each interpolation creates a heap-allocated string result
- Intermediate concat results are tracked in `scopeStack.heapAllocatedStrings`
- Mutable string variables are tracked in `scopeStack.stringVariables` for cleanup
- At scope exit, cleanup reads the *current* buffer pointer from the variable (handles reassignment)

### Escape Sequences

- `\{` - Literal open brace (stored internally as `\x01{`)
- `\}` - Literal close brace (stored internally as `\x01}`)
- Standard escape sequences (`\n`, `\t`, `\\`, `\"`) work normally

## Documentation

String interpolation allows embedding expressions directly within string literals, automatically converting values to their string representation.

### Basic Syntax

Use curly braces `{expr}` to embed any expression in a string:

```maxon
var name = "World"
print("Hello, {name}!")  // "Hello, World!"

var x = 42
print("The answer is {x}")  // "The answer is 42"
```

### Expression Interpolation

Any valid expression can be embedded:

```maxon
var a = 5
var b = 3
print("{a} + {b} = {a + b}")  // "5 + 3 = 8"

print("Double: {a * 2}")  // "Double: 10"
```

### Built-in Type Support

All built-in types are automatically convertible to strings:

```maxon
// Integers
print("Count: {42}")  // "Count: 42"

// Floats
print("Pi: {3.14159}")  // "Pi: 3.14159"

// Booleans
print("Active: {true}")  // "Active: true"
```

### Negative Numbers

Unary operators work inside interpolation:

```maxon
print("Temp: {-10} degrees")  // "Temp: -10 degrees"
print("Value: {-3.5}")  // "Value: -3.5"
```

### Escape Sequences

To include literal braces, escape them with backslash:

```maxon
print("Use \{expr\} syntax")  // "Use {expr} syntax"
```

### Custom Types

Custom types can be interpolated by implementing the `Stringable` interface:

```maxon
type Point is Stringable
    var x int
    var y int

    function Stringable.toString(format string or nil) returns string
        return "({self.x}, {self.y})"
    end 'toString'
end 'Point'

var p = Point{x: 1, y: 2}
print("Location: {p}")  // "Location: (1, 2)"
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
    print("Value: {x}")
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
    print("Answer: {42}")
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
    print("Negative: {x}")
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
    print("Value: {0-10}")
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
    print("Pi: {pi}")
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
    print("Value: {2.5}")
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
    print("Temp: {temp}")
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
    print("Active: {flag}")
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
    print("Active: {flag}")
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
    print("Yes: {true}, No: {false}")
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
    print("{a} + {b} = {a + b}")
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
    print("Double: {x * 2}, Triple: {x * 3}")
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
    print("Result: {(a + b) * 2}")
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
    print("{x}")
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
    print("{a}{b}")
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
    print("{a}{b}{c}")
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
    print("Value \{x\} is {x}")
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
        print("Count: {i}")
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
    print("Double of 5: {double(5)}")
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
    print("Length: {s.count()}")
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
    print("a > b: {a > b}")
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
    print("x and y: {x and y}")
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
    print("Area: {3.14159 * r * r}")
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
    print("Name: {name}, Count: {count}, Active: {active}")
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
    print("Max int: {big}")
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
    print("Int: {i}, Float: {f}")
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
    print("{a}\t{b}")
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
type Pair is Stringable
    var first int
    var second int

    function Stringable.toString(_ string or nil) returns string
        return "[{first}, {second}]"
    end 'toString'
end 'Pair'

function main() returns int
    var p = Pair{first: 1, second: 2}
    print("{p}")
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

    function Stringable.toString(format string or nil) returns string
        if let fmt = format 'unwrap'
            if fmt == "verbose" 'verbose'
                return "Counter(value={value})"
            end 'verbose'
        end 'unwrap'
        return "{value}"
    end 'toString'
end 'Counter'

function main() returns int
    var c = Counter{value: 42}
    print("{c}")
    print("{c:verbose}")
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
    var first string
    var last string

    function Stringable.toString(_ string or nil) returns string
        return "{first} {last}"
    end 'toString'
end 'Name'

type Age is Stringable
    var years int

    function Stringable.toString(_ string or nil) returns string
        return "{years} years old"
    end 'toString'
end 'Age'

function main() returns int
    var name = Name{first: "John", last: "Doe"}
    var age = Age{years: 30}
    print("{name}, {age}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
John Doe, 30 years old
```
