---
title: Functions
description: "Declaring and calling functions: named arguments, defaults, overloads, closures, and purity."
sidebar:
  order: 7
---

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
- **First argument**: Always positional. Labelling the first argument is rejected as **E2052 "first arg cannot be named"**.
- **Subsequent arguments**: Must use `name: value` syntax (omitting the label triggers **E3005**)
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
- If the caller passes a `let` variable to a function that mutates its parameter, the compiler raises error **E3019**.
- If the caller passes a literal or expression (not a named variable), the compiler creates a temporary immutable stack slot. Mutations inside the function do not propagate anywhere.

```maxon
function double(n int)
		n = n * 2
end 'double'

function main() returns ExitCode
		var x = 5
		double(x)       // OK — x is var; x becomes 10

		let y = 5
		double(y)       // ERROR E3019: cannot pass let variable to mutating parameter

		double(5)       // OK — literal creates a temporary; mutation has no visible effect
		return x
end 'main'
```

### Function Types and Function-Typed Values

Functions in Maxon are first-class values: they can be stored in variables, passed as arguments, and returned from other functions. A *function type* is written with the `function` keyword and must be named via `typealias` — the literal `function(...) returns T` form is legal only as the right-hand side of a `typealias` declaration. Anywhere else (parameters, return types, struct fields, variable annotations, generic arguments), reference the alias by name.

```maxon
typealias Score = int(i64.min to i64.max)

typealias UnaryOp = function(Score) returns Score    // takes one Score, returns Score
typealias Compare = function(Score, Score) returns bool  // two Scores, returns bool
typealias Callback = function()                       // takes nothing, returns void
```

The `returns` clause is omitted for a void-returning function type. Once defined, the alias can be used at every use site — function parameter, return type, struct field, or generic argument:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
typealias HandlerMap = Map with (String, UnaryOp)

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
		var value as int

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
