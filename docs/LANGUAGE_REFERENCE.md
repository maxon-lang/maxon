# Maxon Language Reference

**Version**: 1.0  
**Target Audience**: AI Coding Agents and Developers

This reference provides complete syntax and semantics for the Maxon programming language.

---

## Table of Contents

1. [Program Structure](#program-structure)
2. [Lexical Elements](#lexical-elements)
3. [Types](#types)
   - [Primitive Types](#primitive-types)
   - [Character Type](#character-type)
   - [String Types](#string-types)
   - [Array Types](#array-types)
   - [Map Type](#map-type)
   - [Optional Types](#optional-types)
   - [Type Conversions](#type-conversions)
4. [Types (Composite)](#types-composite)
5. [Enums](#enums)
6. [Variables](#variables)
7. [Functions](#functions)
8. [Expressions](#expressions)
9. [Statements](#statements)
10. [Namespaces](#namespaces)
11. [Standard Library](#standard-library)
12. [Build System](#build-system)
13. [Memory Model](#memory-model)

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
and, as, associatedtype, bool, break, continue, default, else, end, enum, export, extern,
fallthrough, false, float, for, function, gives, if, in, int, interface, is, let, match,
nil, not, or, return, static, then, true, type, var, while
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

**Nil Literal**
```maxon
nil  // Represents absence of a value (for optional types)
```

---

## Types

### Primitive Types

| Type | Size | Description | MIR Type |
|------|------|-------------|----------|
| `int` | 64-bit | Signed integer | `i64` |
| `float` | 64-bit | IEEE 754 double | `f64` |
| `bool` | 1-bit | Boolean (true/false) | `i1` |
| `byte` | 8-bit | Unsigned byte | `i8` |

### Character Type

| Type | Size | Description |
|------|------|-------------|
| `character` | 16-byte | Extended Grapheme Cluster (EGC) |

The `character` type represents a user-perceived character, which may consist of multiple Unicode codepoints:
- `'A'` - ASCII character (1 byte)
- `'é'` - Latin with combining accent (2-3 bytes)
- `'🎉'` - Emoji (4 bytes)
- `'👨‍👩‍👧'` - Family emoji with ZWJ (up to 25 bytes)

**Character Methods:**
```maxon
var c = 'A'
c.bytes().count()      // Number of UTF-8 bytes (1 for ASCII)
c.codepoints().count() // Number of Unicode codepoints
c.equals(other)        // Equality comparison
c.toString()           // Convert to string
```

### String Types

| Type | Size | Description |
|------|------|-------------|
| `string` | 16-byte | UTF-8 string (owned, copy-on-write) |
| `substring` | 24-byte | String view (reference to string data) |
| `cstring` | 24-byte | FFI-friendly null-terminated string reference |

**String Characteristics:**
- UTF-8 encoded
- Small String Optimization (SSO): strings ≤15 bytes stored inline
- Large strings: heap-allocated with copy-on-write semantics
- Reference counted for automatic memory management

**String Operations:**
```maxon
var s = "hello"              // Small string (SSO)
var greeting = "{s} world"   // String interpolation
print(s)                     // Print string to stdout with newline
```

**String Properties:**
```maxon
var s = "hello"
var len = s.count()           // Grapheme count (UTF-8 aware): 5
var bytes = s.bytes().count() // Byte count: 5
var empty = s.isEmpty()       // Check if empty: false
```

**String Slicing:**
```maxon
var s = "hello world"
var sub1 = s[0..5]           // "hello" (start..end, exclusive end)
var sub2 = s[6..]            // "world" (from index to end)
var sub3 = s[..5]            // "hello" (from start to index)
```

**Substring Type:**
- Lightweight view into another string's data
- Does not own the data (keeps parent string alive)
- Immutable
- Created via `s.slice(start, end)` method
- Use `toString()` to create an owned copy

**CString Type:**
- FFI-friendly null-terminated string for interop with C APIs
- Created via `s.cstr()` method on strings
- Holds a reference to the parent string's buffer
- Automatically released when out of scope

### Array Types

**Array Type Syntax**
```maxon
Array of int              // Array of integers
Array of float            // Array of floats  
Array of Array of int     // nested array (2D)
```

**Creating Arrays**
```maxon
var numbers = Array of 10 int   // Sized Array of 10 integers (zero-initialized)
let values = [1, 2, 3]          // Immutable array from literal (stack buffer)
var items = [1, 2, 3]           // Mutable array from literal (heap buffer)
```

**Function Parameters**
```maxon
function process(data Array of int) returns int
    return data[0]
end 'process'
```

**Array Properties**
- Zero-based indexing: `array[0]`, `array[1]`, ...
- `let` arrays use stack-allocated buffers (capacity = 0)
- `var` arrays use heap-allocated buffers with automatic cleanup
- No bounds checking (undefined behavior for out-of-bounds access)

**Array Methods**

| Method | Description | Return Type |
|--------|-------------|-------------|
| `arr.count()` | Get number of elements | int |
| `arr.capacity()` | Get allocated capacity | int |
| `arr.isEmpty()` | Check if array is empty | bool |
| `arr.first()` | Get first element or nil | Element or nil |
| `arr.last()` | Get last element or nil | Element or nil |
| `arr.get(i)` | Get element at index or nil | Element or nil |
| `arr.set(i, v)` | Set element at index | Self |
| `arr.push(v)` | Append element to end | Self |
| `arr.append(other)` | Append all elements from another array | Self |
| `arr.pop()` | Remove and return last element | Element or nil |
| `arr.insert(i, v)` | Insert element at index | Self |
| `arr.remove(i)` | Remove element at index | Element or nil |
| `arr.clear()` | Remove all elements (keeps capacity) | Self |
| `arr.reserve(n)` | Ensure at least n capacity | Self |

### Map Type

Maps are hash-based key-value collections with O(1) average lookup time.

**Declaration**
```maxon
var m = Map from KeyType to ValueType
```

**Examples**
```maxon
var scores = Map from int to int
var names = Map from string to string
```

**Key Type Restrictions**
Map keys must implement the `Hashable` interface. Supported key types:
- `int`
- `string`
- `character`
- `byte`

Non-hashable types (like `float`) cannot be used as map keys and will produce a compile-time error.

**Map Methods**

| Method | Description | Return Type |
|--------|-------------|-------------|
| `m.insert(key, value)` | Insert or update a key-value pair | void |
| `m.get(key)` | Get value for key (returns zero value if not found) | ValueType |
| `m.contains(key)` | Check if key exists in map | bool |
| `m.remove(key)` | Remove key-value pair from map | void |
| `m.count()` | Return number of key-value pairs | int |
| `m.capacity()` | Return current capacity of map | int |

**Usage Examples**
```maxon
var m = Map from int to int

// Insert key-value pairs
m.insert(1, 100)
m.insert(2, 200)
m.insert(3, 300)

// Get values
var val = m.get(2)           // 200
var missing = m.get(99)      // 0 (zero value for int)

// Check existence
if m.contains(1) 'found'
    print("Key exists")
end 'found'

// Update existing key
m.insert(1, 999)             // Updates value for key 1
var updated = m.get(1)       // 999

// Remove entries
m.remove(1)
var count = m.count()        // 2

// Get capacity
var cap = m.capacity()       // 16 (default initial capacity)
```

**Implementation Details**
- Uses open addressing with linear probing for collision resolution
- Default initial capacity of 16 buckets
- Automatic resizing at 75% load factor (doubles capacity)
- Heap-allocated with automatic cleanup at end of scope
- `get()` returns the zero value of the value type if key is not found

**Automatic Resizing**
Maps automatically grow when the load factor exceeds 75%:
```maxon
var m = Map from int to int
// Initial capacity: 16, grows at 12 entries
var i = 0
while i < 20 'insert'
    m.insert(i, i * 10)  // Triggers resize around i=12
    i = i + 1
end 'insert'
// Capacity is now 32
```

### Optional Types

Optional types represent values that may or may not be present. Use `T or nil` to declare an optional type, where `T` is any type.

**Syntax**
```maxon
returns int or nil
    if b == 0 'check'
        return nil
    end 'check'
    return a / b
end 'safeDivide'
```

**Key Features**
- **Type Safety**: Cannot use optional values without unwrapping
- **Nil Literal**: Use `nil` to represent absence of value
- **If-Let Unwrapping**: Safe pattern matching to extract values
- **Else-Unwrap**: Provide default value when optional is nil
- **Implicit Wrapping**: Non-nil values automatically wrapped when needed

**Usage Contexts**

Optional types can be used in:
- Function return types: `returns int or nil`
- Function parameters: `function bar(x int or nil)`
- Struct fields: `type Person { var age int or nil }`
- Local variables: `var result int or nil`

**If-Let Unwrapping**

Safely unwrap optional values with `if let`:

```maxon
var result = safeDivide(10, 2)

if let val = result 'valid'
    // val is unwrapped int here
    print("{val + 5}")  // 10
end 'valid' else 'invalid'
    // result was nil
    print("Cannot divide by zero")
end 'invalid'
```

**Else-Unwrap with Default**

Provide a default value when the optional is nil:

```maxon
var result = safeDivide(10, 0) else 'default'
    result = 1  // Must assign default value
end 'default'

// result is guaranteed to be int (non-optional) here
print("{result}")  // 1
```

**Nil Coalescing Operator**

The nil coalescing operator `or` provides a concise way to unwrap an optional with a default value:

```maxon
var x = optionalValue or defaultValue
```

This is equivalent to if-let with a default, but more concise:

```maxon
var opt = getOptional()
var result = opt or 0  // result is unwrapped int, using 0 if opt is nil
```

The result type is always the unwrapped type (non-optional). The right operand cannot be optional (no chaining).

**Guard-Let Statement**

Guard-let provides early exit when an optional is nil, reducing nesting:

```maxon
function process(value int or nil) returns int
    let x = value or 'nil_case'
        return 0  // Must exit scope (return, break, continue)
    end 'nil_case'
    
    // x is guaranteed to be unwrapped int here
    return x * 2
end 'process'
```

The guard body must exit the current scope, ensuring the variable is always bound after the guard block.

**Nil Default Parameters**

Optional parameters can use `nil` as the default value:

```maxon
function greet(name string or nil = nil)
    let actualName = name or 'default'
        print("Hello, stranger!")
        return
    end 'default'
    print("Hello, " + actualName + "!")
end 'greet'

greet()               // Uses nil default
greet(name = "Alice") // Uses provided value
```

**Type Safety**

The compiler prevents using optional values without unwrapping:

```maxon
var x = safeDivide(10, 2)
return x + 5  // ERROR: Cannot use optional without unwrapping
```

**Optional Parameters**

Functions can accept optional parameters:

```maxon
function greet(name string or nil)
    if let n = name 'valid'
        print("Hello, " + n)
    end 'valid' else 'invalid'
        print("Hello, stranger")
    end 'invalid'
end 'greet'

greet("Alice")  // Implicitly wraps "Alice" as Some
greet(nil)      // Passes nil
```

**Optional Struct Fields**

Structs can have optional fields:

```maxon
type Person
    var name string
    var age int or nil  // Optional age
end 'Person'

var p1 = Person{name: "Bob", age: nil}
var p2 = Person{name: "Alice", age: 30}  // Implicitly wraps 30

if let age = p2.age 'check'
    print("{age}")  // 30
end 'check'
```

**Memory Layout**

Optional types use a discriminated union with an 8-bit tag:
- Tag = 0: nil (value space unused)
- Tag = 1: has value
- Size: 1 byte (tag) + sizeof(T) + padding for alignment
- Stack-allocated, no heap allocation or garbage collection

Examples:
- `int or nil`: 9 bytes (1 tag + 8 int)
- `bool or nil`: 2 bytes (1 tag + 1 bool)
- `ptr or nil`: 9 bytes (1 tag + 8 pointer)

### Type Conversions

**Implicit Conversions**
- `int` → `float` (in mixed arithmetic)

**Explicit Conversions** (using `as` operator)
```maxon
var i = 65 as character         // 'A'
var b = 1 as bool          // true
var f = 5 as float         // 5.0
```

Supported casts:
- `int` → `float` (int to float only)
- `int` ↔ `character`
- `int` ↔ `bool`
- `character` ↔ `int`

**Converting floats to integers:**
The `float → int` cast is not supported. Use explicit functions instead to make your intent clear:
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
- Method parameters support named arguments just like regular functions

### Calling Methods

Methods are called using dot notation on instances. The receiver (`self`) is implicit:

```maxon
var p1 = Point{x: 10, y: 20}
var p2 = Point{x: 5, y: 10}
var p3 = p1.add(p2)             // Positional argument
var p4 = p1.add(other = p2)     // Named argument (optional)
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
var p2 = Point.create(10, 20)     // Static method with args
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

### Interfaces

Interfaces define a set of method signatures that types can implement:

```maxon
interface Hashable
    function hash() returns int
end 'Hashable'
```

Structs declare conformance using the `is` keyword:

```maxon
type Point is Hashable
    var x int
    var y int

    function Hashable.hash() returns int
        return x + y * 31
    end 'hash'
end 'Point'
```

**Static Interface Methods**

Interfaces can declare static methods using the `static` keyword. Static interface methods don't receive an implicit `self` parameter and are typically used for factory methods:

```maxon
interface Parseable
    static function parse(input string) returns Self
end 'Parseable'

type Number is Parseable
    var value int

    static function Parseable.parse(input string) returns Number
        return Number{value: input.toInt()}
    end 'parse'
end 'Number'
```

**Interface Notes:**
- `Self` in interface method parameters/returns refers to the conforming type
- A type can conform to multiple interfaces: `type Foo is A, B`
- Methods implementing interface requirements follow the same syntax as regular methods
- Static interface methods use `static function Interface.method()` syntax in implementations

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

Enums can have an underlying raw value type (`int` or `string`):

```maxon
enum HttpStatus int
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

enum Planet string
    earth = "Earth"
    mars = "Mars"
end 'Planet'
```

Access the raw value with `.rawValue`:

```maxon
var status = HttpStatus.ok
var code = status.rawValue    // 200
```

### Associated Values

Cases can carry additional data called associated values:

```maxon
enum Result
    success(value int)
    failure(code int, message string)
    pending
end 'Result'
```

Contype cases with associated values:

```maxon
var r1 = Result.success(42)
var r2 = Result.failure(404, "Not found")
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

### If-Case Pattern Matching

For single-case matching, use `if case` syntax:

```maxon
if case success(v) = result 'check'
    print("{v}")
end 'check'

if case failure(code, msg) = result 'error'
    print(msg)
end 'error'
```

### Comparing Enum Values

Enum values can be compared for equality using `==` and `!=`:

```maxon
var dir = Direction.north
if dir == Direction.north 'check'
    print("Going north!")
end 'check'
```

For enums with associated values, `==` compares both the case and the associated values.

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
var opp = Direction.opposite(dir)  // Direction.south
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

---

## Variables

### Mutable Variables (`var`)
```maxon
var x = 42              // Type inferred
var y int = 10          // Explicit type
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
- Type can be inferred from initializer or explicitly specified
- Scope is block-scoped
- Primitives are stack-allocated; `var` arrays use heap buffers (with automatic cleanup)

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

Maxon uses named arguments:
- All parameters are **positional by default**
- Callers can **optionally name any argument** using `name = value` syntax
- Parameters with default values can **only be provided via named arguments** (not positionally)
- Positional arguments must come before named arguments
- Named arguments can appear in any order

**Examples:**

```maxon
// All parameters are positional by default
function add(a int, b int) returns int
    return a + b
end 'add'

add(3, 4)           // Positional arguments
add(a = 3, b = 4)   // Named arguments (optional)
add(b = 4, a = 3)   // Named arguments in any order

// Named arguments for clarity
function connect(host string, port int) returns bool
    // ...
end 'connect'

connect("localhost", 8080)              // Positional
connect(host = "localhost", port = 8080) // Named for clarity
connect("localhost", port = 8080)       // Mix positional and named
```

### Default Values

Parameters can have default values. Parameters with defaults can **only** be provided via named arguments (not positionally):

```maxon
function greet(name string, title string = "Mr.")
    print("Hello, " + title + " " + name)
end 'greet'

greet("Smith")                    // Uses default title
greet("Smith", title = "Dr.")     // Override default with named argument
```

**Rules:**
- Parameters with defaults must come after required parameters
- Parameters with defaults cannot be passed positionally - they must use named arguments
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
function greet(name string)
    print("Hello, " + name)
end 'greet'
```

**Multiple Parameters**
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

var result = add(3, 4)  // Positional
var result2 = add(a = 3, b = 4)  // Named (optional)
```

**Named Arguments for Clarity**
```maxon
function divide(dividend int, divisor int) returns int
    return dividend / divisor
end 'divide'

var result = divide(10, 2)  // Positional
var result2 = divide(dividend = 10, divisor = 2)  // Named for clarity
```

**Array Parameters**
```maxon
function sum(numbers Array of int) returns int
    var total = 0
    for num in numbers 'loop'
        total = total + num
    end 'loop'
    return total
end 'sum'
```

### Calling Functions

**Positional (default):**
```maxon
var result = add(3, 4)
var answer = getAnswer()
greet("Alice")
```

**Named Arguments (optional):**
```maxon
greet(name = "Alice")
divide(dividend = 100, divisor = 5)
add(b = 4, a = 3)  // Named args can be in any order
```

**Mixed (positional first, then named):**
```maxon
connect("localhost", port = 8080)
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

1. **Postfix**: `[]` (array indexing), `.` (member access), `as` (cast), function call `()`
2. **Unary**: `-` (negation), `not` (logical not)
3. **Multiplicative**: `*` `/` `mod`
4. **Additive**: `+` `-`
5. **Comparison**: `==` `!=` `<` `>` `<=` `>=`
6. **Logical AND**: `and`
7. **Logical OR**: `or`

### Arithmetic Operators

| Operator | Description | Types | Example |
|----------|-------------|-------|---------|
| `+` | Addition | int, float | `a + b` |
| `-` | Subtraction | int, float | `a - b` |
| `*` | Multiplication | int, float | `a * b` |
| `/` | Division | int, float | `a / b` |
| `mod` | Modulo | int only | `a mod b` |

**Notes:**
- Integer division truncates
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

### Logical Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `and` | Logical AND | `a > 0 and b < 10` |
| `or` | Logical OR | `x == 1 or x == 2` |
| `not` | Logical NOT | `not done` |

### Unary Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `-` | Negation | `-x` |
| `not` | Logical NOT | `not condition` |

### Parentheses
Override precedence:
```maxon
(2 + 3) * 5    // 25, not 17
```

### Array Indexing
```maxon
var arr = [1, 2, 3, 4, 5]
var first = arr[0]
var last = arr[arr.count() - 1]
```

---

## Statements

### Expression Statement
Any expression followed by newline:
```maxon
print(x)
add(3, 4)
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
array[index] = value
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

### If-Let Statement

The `if let` statement safely unwraps optional values with pattern matching.

**Syntax**
```maxon
if let binding = optional_expression 'some'
    // binding is unwrapped value (non-optional) here
    statements
end 'some' else 'none'
    // optional was nil
    statements
end 'none'
```

**Example**
```maxon
returns int or nil
    if b == 0 'check'
        return nil
    end 'check'
    return a / b
end 'safeDivide'

var result = safeDivide(10, 2)
if let val = result 'valid'
    print("{val + 5}")  // val is unwrapped int
end 'valid' else 'invalid'
    print("Division by zero")
end 'invalid'
```

**Notes:**
- `binding` is only in scope within the then-block
- Optional expression must have type `T or nil`
- The `else` block is optional
- Block identifier required and must match on all keywords

### Else-Unwrap Statement

The else-unwrap statement unwraps an optional value or provides a default.

**Syntax**
```maxon
var name = optional_expression else 'label'
    name = default_value  // Must assign default value
end 'label'
// name is guaranteed to be non-optional here
```

**Example**
```maxon
var result = safeDivide(10, 0) else 'default'
    result = 1  // Provide default when nil
end 'default'

print("{result}")  // result is int, not int or nil
```

**Notes:**
- Variable must be assigned within the else block
- The else block only executes when the optional is nil
- After the else block, the variable has the unwrapped type (non-optional)
- Block identifier required and must match

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
    print(i)
    i = i + 1
end 'loop'
```

### For Loop
```maxon
for variable in iterable 'label'
    statements
end 'label'
```

**Range Iteration:**
```maxon
for i in range(0, 10) 'loop'
    print(i)
end 'loop'
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
- `and fallthrough` continues to the next case (skipping its pattern check)
- `and fallthrough` cannot be combined with `return`
- For enums, all cases must be covered unless `default` is present
- `default` must be the last case if present
- Enum case patterns: `CaseName(binding1, binding2)` extracts associated values

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
print(value string)                     // Print string to stdout with newline
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
format_int(value int) string    // Format int as string
format_float(value float) string // Format float as string
```

**String Functions**
```maxon
// Methods
s.count()                       // Grapheme count (UTF-8 aware)
s.bytes().count()               // Byte count
s.isEmpty()                     // Returns bool

// Slicing (accessed as s[start..end])
s[start..end]                   // Substring from start to end (exclusive)
s[start..]                      // Substring from start to end of string
s[..end]                        // Substring from beginning to end (exclusive)
```

### Standard Library Modules

Located in `stdlib/` directory:
- `stdlib/fmt/` - Formatting utilities
- `stdlib/fs/` - File system operations
- `stdlib/iter/` - Iterator interface
- `stdlib/build/` - Build system utilities

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
type BuildConfig
    var name string           // Executable name
    var output string         // Output path (e.g., "bin/app.exe")
    var sources Array of string  // Source files (empty = auto-discover)
    var optimize bool         // Enable optimizations
    var debug_info bool       // Include debug symbols
end 'BuildConfig'
```

### Build Functions

| Function | Description |
|----------|-------------|
| `build(name string)` | Simple build with defaults |
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
for i in range(0, numbers.count()) 'iter'
    print("{numbers[i]}")
end 'iter'
```


### Factorial Example
```maxon
function factorial(n int) returns int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() returns int
    var result = factorial(5)
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

# Emit LLVM IR alongside executable
maxon compile program.maxon --emit-ir

# Run language tests
maxon test-fragments

# Extract tests from specs
maxon extract-specs

# Regenerate IR for test fragments
maxon regen-fragments
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

3. **Use explicit types when clarity matters**: 
   ```maxon
   var count int = 0    // Clear intent
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

6. **Use `for` loops for ranges**:
   ```maxon
   for i in range(0, 10) 'loop'
       // i is 0, 1, 2, ..., 9
   end 'loop'
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

9. **Check `.count` before array access**:
   ```maxon
   if index < arr.count() 'safe'
       var val = arr[index]
   end 'safe'
   ```

10. **Use meaningful block identifiers**:
    ```maxon
    while not done 'process'     // 'process' describes the loop
        // ...
    end 'process'
    ```

11. **Use named arguments for clarity when helpful**:
    ```maxon
    connect("localhost", port = 8080)  // Clear what 8080 means
    move(start, end)                    // Positional is fine for obvious args
    ```

12. **Remember default params require named arguments**:
    ```maxon
    function greet(name string, title string = "Mr.")
    greet("Smith")                // Uses default
    greet("Smith", title = "Dr.") // Override with named arg
    ```

13. **Use nil coalescing for concise defaults**:
    ```maxon
    var result = optionalValue or defaultValue  // Clean one-liner
    ```

14. **Use guard-let for early exits**:
    ```maxon
    function process(x int or nil) returns int
        let value = x or 'nil_case'
            return 0  // Early exit on nil
        end 'nil_case'
        return value * 2  // value is guaranteed non-nil
    end 'process'
    ```

---

**End of Reference**
