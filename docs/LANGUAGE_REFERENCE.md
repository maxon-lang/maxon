# Maxon Language Reference

**Version**: 1.0  
**Target Audience**: AI Coding Agents and Developers

This reference provides complete syntax and semantics for the Maxon programming language.

---

## Table of Contents

1. [Program Structure](#program-structure)
2. [Lexical Elements](#lexical-elements)
3. [Types](#types)
   - [Type Conversions](#type-conversions)
4. [Types (Composite)](#types-composite)
   - [Interface Extensions](#interface-extensions)
   - [Conditional Extensions](#conditional-extensions)
5. [Enums](#enums)
6. [Variables](#variables)
7. [Functions](#functions)
8. [Expressions](#expressions)
9. [Statements](#statements)
10. [Error Handling](#error-handling)
11. [Namespaces](#namespaces)
12. [Standard Library](#standard-library)
13. [Build System](#build-system)
14. [Memory Model](#memory-model)
    - [Ownership System](#ownership-system)

---

## Program Structure

### Entry Point
Every Maxon program must have a `main()` function that returns `int`:

```maxon
function main() returns int
    return 0
end 'main'
```

The return value becomes the program's exit code (0-255 on Windows).

### File Structure
- One or more function declarations
- Namespace derived from file path
- Export functions with `export` keyword for cross-file visibility

---

## Lexical Elements

### Comments
Single-line comments only:
```maxon
// This is a comment
```

### Identifiers
- Start with letter or underscore: `[a-zA-Z_]`
- Followed by letters, digits, or underscores: `[a-zA-Z0-9_]*`
- Case-sensitive
- Cannot be keywords

### Keywords
```
and, as, bool, break, continue, default, else, end, enum, export, extern,
fallthrough, false, float, for, function, gives, if, ignore, implements, in, int, interface, let, match,
mod, not, or, otherwise, return, shl, shr, static, then, throw, throws, true, try, type,
typealias, var, where, while, xor
```

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
```

Character literals create a `character` type value, which represents an Extended Grapheme Cluster (EGC).
The `character` type may contain multiple UTF-8 bytes.

**String Literals** (double-quoted, null-terminated)
```maxon
"Hello, World!"
"Line1\nLine2"
"Tab\there"
"Quote: \"text\""
```

Escape sequences: `\n` `\t` `\\` `\"` `\{` `\}`

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

Casts to or from `bool` are not allowed. Narrowing casts (`int` variable → `byte`, `float` → `int`, `float` → `byte`) are not allowed.

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

### Calling Methods

Methods are called using dot notation on instances. The receiver (`self`) is implicit:

```maxon
var p1 = Point{x: 10, y: 20}
var p2 = Point{x: 5, y: 10}
var p3 = p1.add(p2)
var mag = p1.magnitude()
```

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
- Initialized at program startup, before `main()` executes

**Differences from Instance Fields:**

| Feature | Instance Field | Static Field |
|---------|---------------|--------------|
| Storage | Per instance | One per type |
| Access | `instance.field` | `Type.field` |
| Declaration | `var field type` | `static var field = value` |
| Requires initializer | No (can use type default) | Yes |

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
typealias StringMap = Map with (String, int)  // OK: String implements Hashable
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

When called on a type like `IntArray implements Container with int`, the return type `Element` becomes `int`.

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

For example, `Array with int` conforms to `Iterable`. Since `int` implements `Equatable`, the `contains` method is available on `Array with int`. A hypothetical `Array with SomeNonEquatableType` would not receive the `contains` method.

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

---

## Enums

Enums define a type with a fixed set of named variants called cases. Maxon supports three kinds of enums: simple enums, raw value enums, and enums with associated values.

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

### Raw Value Enums

Enums can have an underlying raw value type (`int` or `String`):

```maxon
enum HttpStatus int
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

enum Planet String
    earth = "Earth"
    mars = "Mars"
end 'Planet'
```

Access the raw value with `.rawValue`:

```maxon
var status = HttpStatus.ok
var code = status.rawValue    // 200
```

Access the case name with `.name`:

```maxon
var status = HttpStatus.notFound
var n = status.name       // "notFound"
var code = status.rawValue    // 404
```

For string-backed enums, `.rawValue` returns the backing value while `.name` returns the case name:

```maxon
var p = Planet.mars
var rawName = p.rawValue  // "Mars" (backing value)
var caseName = p.name     // "mars" (case name)
```

### Associated Values

Cases can carry additional data called associated values:

```maxon
enum Result
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

Use `match` statements to extract associated values from enum cases. Each binding name becomes a local variable within the case body:

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
    empty gives 0
    value(n) gives n * 2
end 'get'
```

You can mix cases with and without bindings:

```maxon
match result 'check'
    success(v) then return v    // Extracts value
    pending then return 0       // No extraction needed
end 'check'
```

**Notes:**
- Binding names must match the number of associated values in the case definition
- Bindings are only in scope within the case body
- Cases without associated values don't need parentheses

### Comparing Enum Values

Enum values can be compared for equality using `==` and `!=`:

```maxon
var dir = Direction.north
if dir == Direction.north 'check'
    print("Going north!")
end 'check'
```

For enums with associated values, `==` compares both the case and the associated values.

### Creating Enums from Names (`fromName`)

The `fromName` static method creates an enum value from a string name. It returns an error union that throws `EnumError.invalidName` if the name doesn't match any case:

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

For enums with associated values, pass the values as additional arguments when the name is a compile-time literal:

```maxon
enum Container
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

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
    north
    south

    function opposite() returns Direction
        if self == Direction.north 'check'
            return Direction.south
        end 'check'
        return Direction.north
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
    if s == Status.on 'check'
        return true
    end 'check'
    return false
end 'isOn'

function toggle(s Status) returns Status
    if s == Status.on 'check'
        return Status.off
    end 'check'
    return Status.on
end 'toggle'
```

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
- The `Error` interface can only be implemented by enums (not types/structs)

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

**Rules:**
- All variables must be initialized at declaration
- Type is always inferred from the initializer
- Scope is block-scoped
- Primitives are stack-allocated; `var` arrays use heap buffers (with automatic cleanup)

### Top-Level Variables

Variables can be declared at the top level of a module (outside any function):

```maxon
var globalCounter = 0
let MAX_SIZE = 1024

function main() returns int
    globalCounter = globalCounter + 1
    return globalCounter
end 'main'
```

**Top-Level Variable Rules:**
- `var` declares a mutable top-level variable (can be reassigned from any function)
- `let` declares an immutable top-level constant (compile-time evaluated)
- Must have an initializer with a constant expression (integer, float, bool, or string literal)
- Initialized before `main()` executes
- Accessible from any function in the same module

**Use Cases:**
- Configuration constants (`let MAX_BUFFER_SIZE = 4096`)
- Counters and state shared across function calls (`var callCount = 0`)
- Program-wide settings

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

Parameters can have default values. Parameters with defaults can be omitted at the call site:

```maxon
function greet(name String, title String = "Mr.")
    print("Hello, " + title + " " + name)
end 'greet'

greet("Smith")                    // Uses default title
greet("Smith", title: "Dr.")      // Override default
```

**Rules:**
- Parameters with defaults must come after required parameters
- Default values are evaluated at call site
- Arguments may be omitted if they have defaults

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
type IntArray is Array with int

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
6. **Comparison**: `==` `!=` `<` `>` `<=` `>=`
7. **AND**: `and`
8. **XOR**: `xor`
9. **OR**: `or`

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

### Logical / Bitwise Operators

The keyword operators `and`, `or`, `xor`, and `not` are context-dependent: they perform logical operations on `bool` operands and bitwise operations on `int` operands.

| Operator | On `bool` | On `int` | Example |
|----------|-----------|----------|---------|
| `and` | Logical AND | Bitwise AND | `a > 0 and b < 10` / `flags and 0xff` |
| `or` | Logical OR | Bitwise OR | `x == 1 or x == 2` / `flags or 0x01` |
| `xor` | Logical XOR | Bitwise XOR | `a xor b` / `value xor mask` |
| `not` | Logical NOT (unary) | Bitwise NOT (unary) | `not done` / `not mask` |

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

### Parentheses
Override precedence:
```maxon
(2 + 3) * 5    // 25, not 17
```

### Array Access

Array elements are accessed using the `.get()` method, which throws `ArrayError.indexOutOfBounds` if the index is invalid:
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
typealias IntArray = Array with int

var numbers = IntArray{}         // Empty array
numbers.push(42)                 // Add elements with push
```

To preallocate with a specific length (elements zero-initialized):
```maxon
typealias IntArray = Array with int

var buffer = IntArray{}
buffer.resize(100)               // Length is now 100
buffer.set(0, value: 42)         // Can set any index 0-99
```

To preallocate capacity without changing length (for performance):
```maxon
typealias IntArray = Array with int

var buffer = IntArray{}
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

**Notes:**
- Loop variable is immutable (like `let`)
- Currently supports ranges (`start..end`)
- Desugars to while loop with iterator interface

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

**Enum Case Pattern Matching:**

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
- Multiple patterns can be combined with `or`
- `break` exits the match statement (or a labeled enclosing loop/match)
- `and fallthrough` continues to the next case (skipping its pattern check)
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered explicitly — `default` is not allowed
- `default` matches any non-enum value not matched by previous patterns
- `default` must be the last case if present
- Enum case patterns: `CaseName(binding1, binding2)` extracts associated values

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

The `otherwise` keyword provides unified error handling for throwing expressions. There are four forms:

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
        FileError.notFound then print("File not found")
        FileError.permissionDenied then print("Permission denied")
        FileError.alreadyExists then print("Already exists")
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
- The error type must match or be compatible with the function's declared error type
- Using `try` without `otherwise` in a non-throwing function is a compile error

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
// Array bounds checking
enum ArrayError implements Error
    indexOutOfBounds
end 'ArrayError'

// Map key lookup
enum MapError implements Error
    keyNotFound
end 'MapError'

// Iterator exhaustion
enum IterationError implements Error
    exhausted
end 'IterationError'
```

Array and Map access methods throw these errors:

```maxon
var arr = [1, 2, 3]
let val = try arr.get(5) otherwise 0  // Returns 0 on out of bounds

var map = ["key": 42]
let result = try map.get("missing") otherwise -1  // Returns -1 if key not found
```

### Error Union Types

Functions with `throws` return an error union type internally:

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

function main() returns int
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
Make functions visible outside the file:

```maxon
export function public_add(a int, b int) returns int
    return a + b
end 'public_add'

function private_helper(x int) returns int
    return x * 2
end 'private_helper'
```

Only `public_add` can be called from other files.

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

## Standard Library

### Core Functions

**I/O Functions**
```maxon
print(value String)                     // Print string to stdout
```

**Math Functions**
```maxon
abs(x int) int                  // Absolute value
abs(x float) float              // Absolute value (overloaded)
sqrt(x float) float             // Square root
pow(base float, exp float) float // Power
sin(x float) float              // Sine
cos(x float) float              // Cosine
tan(x float) float              // Tangent
exp(x float) float              // e^x
log(x float) float              // Natural logarithm
log2(x float) float             // Base-2 logarithm
log10(x float) float            // Base-10 logarithm
floor(x float) int              // Round down
ceil(x float) int               // Round up
round(x float) int              // Round to nearest
trunc(x float) int              // Truncate toward zero
```

**Formatting Functions**
```maxon
format_int(value int) String    // Format int as string
format_float(value float) String // Format float as string
```

---

## Build System

Maxon uses a `build.maxon` file to define project structure and build configuration. This file marks the project root and contains executable Maxon code that outputs build configuration.

### Project Structure

A Maxon project is defined by the presence of a `build.maxon` file:

```
myproject/
├── build.maxon          # Project root marker and build config
├── main.maxon           # Entry point
├── lib.maxon            # Project file
└── utils/
    └── math.maxon       # Files in subdirectories are included
```

### Simple Build Configuration

For most projects, a single line suffices:

```maxon
function main()
    build("myapp")  // Executable name is required
end 'main'
```

This automatically:
- Sets output to `bin/myapp.exe`
- Discovers all `.maxon` files in the project directory (recursively)
- Uses default compilation settings

### Custom Build Configuration

For more control, use `BuildConfig`:

```maxon
function main()
    var config = BuildConfig{
        name: "myapp",
        output: "dist/myapp.exe",
        sources: ["main.maxon", "lib.maxon"],
        optimize: true,
        debug_info: false
    }
    buildWithConfig(config)
end 'main'
```

### BuildConfig Type

```maxon
type StringArray is Array with String

type BuildConfig
    var name String           // Executable name
    var output String         // Output path (e.g., "bin/app.exe")
    var sources StringArray   // Source files (empty = auto-discover)
    var optimize bool         // Enable optimizations
    var debug_info bool       // Include debug symbols
end 'BuildConfig'
```

### Build Functions

| Function | Description |
|----------|-------------|
| `build(name String)` | Simple build with defaults |
| `buildWithConfig(config BuildConfig)` | Custom build with full control |

### Creating a New Project

Initialize a new project with:

```bash
maxon init myproject
```

This creates a `build.maxon` file with the project name pre-filled.

### Building a Project

From the project directory:

```bash
maxon build
```

The compiler:
1. Finds `build.maxon` in the current or parent directory
2. Compiles and executes `build.maxon`
3. Parses the JSON output for build configuration
4. Compiles the project with those settings

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

### Ownership System

Maxon implements a compile-time ownership system that tracks value ownership and prevents use-after-move errors without runtime overhead.

Every variable in Maxon has ownership of its value. When a variable is passed to a function:

- **Borrow**: If the function only reads the parameter, ownership stays with the caller
- **Move**: If the function mutates the parameter, ownership transfers to the callee

After a move, the original variable cannot be used or reassigned - this is a compile-time error.

**Example:**
```maxon
function main() returns int
    var a = 42              // a owns the value 42

    var b = foo(a)          // borrow - foo only reads z
    a = a + 1               // OK - a still owns its value

    bar(a)                  // move - bar mutates z, ownership transfers

    // a = a + 1            // ERROR: Cannot assign after ownership transferred
    // return a             // ERROR: Cannot use after ownership transferred

    return b                // OK - b is still owned
end 'main'

function foo(z int) returns int
    return z + 4            // only reads z - borrows
end 'foo'

function bar(z int)
    z = z + 1               // mutates z - takes ownership
end 'bar'
```

**How Ownership Works:**

The compiler runs a mutation analysis pass that scans each function to determine which parameters it mutates:
1. Direct assignment to a parameter 
2. Array element assignment 
3. Member assignment 
4. Passing to another function that mutates that parameter position

During semantic analysis, each variable has an ownership state:
- `Owned` - Variable owns its value and can be used
- `Moved` - Ownership has been transferred; any use is an error

**Error Messages:**
```
Semantic Error: example.maxon:10:4
Cannot assign to variable 'a' after ownership was transferred
  Ownership transferred to function 'bar' at line 8
  Note: Once ownership is transferred, the variable cannot be used or reassigned
```

**Control Flow:**

If a variable is moved in any branch of a conditional, it's considered moved after the conditional:
```maxon
var a = 42
if condition 'c'
    bar(a)          // moves a in this branch
end 'c'
// a is now moved - might have been moved depending on condition
return a            // ERROR
```

**Design Principles:**
- All types have ownership (including primitives like `int`, `float`, `bool`)
- Automatic mutation detection (no explicit `&mut` annotations needed)
- Compile-time only - zero runtime overhead

### Stack Allocation
- Local variables (`var`, `let`)
- Function parameters
- Automatic lifetime (scope-based)

### Heap Allocation
- Arrays (all array types)
- Strings (when dynamically allocated)
- Allocated with Windows `HeapAlloc`
- Automatically freed at end of scope
- No manual `free` or garbage collector needed

### Safety
- No bounds checking on arrays
- No null checks
- Use-after-move prevented at compile time (see Ownership System above)

### Calling Convention
- Simple types (int, float, bool, character) passed by value
- Arrays passed as pointer
- Return values passed by value (in register or stack)

---

## Code Generation

### LLVM Backend
- Maxon compiles to LLVM IR
- LLVM optimizations applied
- Native executable generated

### Optimizations
- Constant folding
- Dead code elimination
- LLVM optimization passes (inline, DCE, etc.)

### Runtime Library
- Located in `maxon-runtime/runtime-windows.obj` or `maxon-runtime/runtime-linux.o`
- Provides implementations for LLVM intrinsics
- Auto-linked with all programs
- No C runtime dependency

---

## Common Patterns

### Main Function Template
```maxon
function main() returns int
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

function main() returns int
    var result = factorial(n: 5)
    print("{result}")  // 120
    return 0
end 'main'
```

---

## Compilation Commands

```bash
# Compile and run in one step
maxon program.maxon

# Compile to executable
maxon compile program.maxon -o program.exe

# Emit IR alongside executable
maxon compile program.maxon --emit-ir

# Emit assembly alongside executable
maxon compile program.maxon --emit-asm

# Enable verbose output
maxon compile program.maxon -v

# Run language tests
maxon test

# Run tests with verbose output
maxon test --verbose
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

### Runtime Behavior

**Undefined Behavior (No Error)**
- Array out-of-bounds access
- Null pointer dereference
- Integer overflow (wraps around)
- Division by zero

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
    function process() returns Result throws ProcessError
        let data = try loadData()  // Propagates error to caller
        return transform(data)
    end 'process'
    ```

---

**End of Reference**
