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
4. [Structs](#structs)
5. [Variables](#variables)
6. [Functions](#functions)
7. [Expressions](#expressions)
8. [Statements](#statements)
9. [Namespaces](#namespaces)
10. [Standard Library](#standard-library)
11. [Memory Model](#memory-model)

---

## Program Structure

### Entry Point
Every Maxon program must have a `main()` function that returns `int`:

```maxon
function main() int
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
and, as, bool, break, continue, else, end, export, extern,
false, float, for, function, if, in, int, interface, is, let, nil, not, or,
return, struct, then, true, var, while
```

### Literals

**Integer Literals**
```maxon
42
-17
0
```

**Byte Literals** (with `b` suffix, range 0-255)
```maxon
42b
0b
255b
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

Character literals create a `character` struct value, which represents an Extended Grapheme Cluster (EGC).
The `character` type may contain multiple UTF-8 bytes.

**String Literals** (double-quoted, null-terminated)
```maxon
"Hello, World!"
"Line1\nLine2"
"Tab\there"
"Quote: \"text\""
```

Escape sequences: `\n` `\t` `\\` `\"`

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
| `int` | 32-bit | Signed integer | `i32` |
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
var greeting = s + " world"  // Concatenation
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

**Fixed-Size Arrays**
```maxon
var numbers = [10]int        // Array of 10 integers
var values = [1, 2, 3]       // Value-initialized, size 3
```

**Unsized Arrays** (function parameters only)
```maxon
function process(data []int, size int) int
    return data[0]
end 'process'
```

**Array Properties**
- Zero-based indexing: `array[0]`, `array[1]`, ...
- `.length` property returns array size
- Heap-allocated with automatic scope-based cleanup
- No bounds checking (undefined behavior for out-of-bounds access)

### Map Type

Maps are hash-based key-value collections with O(1) average lookup time.

**Declaration**
```maxon
var m = map from KeyType to ValueType
```

**Examples**
```maxon
var scores = map from int to int
var names = map from string to string
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
var m = map from int to int

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
var m = map from int to int
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
function safeDivide(a int, b int) int or nil
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
- Function return types: `function foo() int or nil`
- Function parameters: `function bar(x int or nil)`
- Struct fields: `struct Person { var age int or nil }`
- Local variables: `var result int or nil`

**If-Let Unwrapping**

Safely unwrap optional values with `if let`:

```maxon
var result = safeDivide(10, 2)

if let val = result 'check'
    // val is unwrapped int here
    print_int(val + 5)  // 10
else 'check'
    // result was nil
    print("Cannot divide by zero")
end 'check'
```

**Else-Unwrap with Default**

Provide a default value when the optional is nil:

```maxon
var result = safeDivide(10, 0) else 'default'
    result = 1  // Must assign default value
end 'default'

// result is guaranteed to be int (non-optional) here
print_int(result)  // 1
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
function greet(name string or nil) void
    if let n = name 'check'
        print("Hello, " + n)
    else 'check'
        print("Hello, stranger")
    end 'check'
end 'greet'

greet("Alice")  // Implicitly wraps "Alice" as Some
greet(nil)      // Passes nil
```

**Optional Struct Fields**

Structs can have optional fields:

```maxon
struct Person
    var name string
    var age int or nil  // Optional age
end 'Person'

var p1 = Person{name: "Bob", age: nil}
var p2 = Person{name: "Alice", age: 30}  // Implicitly wraps 30

if let age = p2.age 'check'
    print_int(age)  // 30
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

## Structs

### Declaration
Structs are user-defined composite types containing named fields. Use `var` for mutable fields and `let` for immutable fields:

```maxon
struct Point
    var x int
    var y int
end 'Point'
```

### Struct Literals
Create struct instances using field initializers:

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

Methods are defined **inside the struct body** with an explicit `self` parameter:

```maxon
struct Point
    var x int
    var y int

    function add(self Point, other Point) Point
        return Point{x: self.x + other.x, y: self.y + other.y}
    end 'add'

    export function magnitude(self Point) float
        return sqrt((self.x * self.x + self.y * self.y) as float)
    end 'magnitude'
end 'Point'
```

**Method Syntax Rules:**
- Methods must be declared inside the struct body
- The first parameter must be named `self` with the struct's type
- Use `export` keyword before `function` to export individual methods
- Methods are called with `Type.method(instance, args)` syntax

### Calling Methods

Methods are called using explicit type qualification:

```maxon
var p1 = Point{x: 10, y: 20}
var p2 = Point{x: 5, y: 10}
var p3 = Point.add(p1, p2)      // p3 = {x: 15, y: 30}
var mag = Point.magnitude(p1)
```

### Interfaces

Interfaces define a set of method signatures that structs can implement:

```maxon
interface Hashable
    function hash(self Self) int
end 'Hashable'
```

Structs declare conformance using the `is` keyword:

```maxon
struct Point is Hashable
    var x int
    var y int

    function hash(self Point) int
        return self.x + self.y * 31
    end 'hash'
end 'Point'
```

**Interface Notes:**
- `Self` in interface signatures refers to the conforming type
- A struct can conform to multiple interfaces: `struct Foo is A, B`
- Methods implementing interface requirements follow the same syntax as regular methods

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
- Variables are stack-allocated (except arrays, which are heap-allocated)

---

## Functions

### Declaration Syntax
```maxon
function name(param1 type1, param2 type2) returnType
    // statements
    return value
end 'name'
```

**Block Identifier**: The string after `end` must match the function name.

### Examples

**No Parameters**
```maxon
function getAnswer() int
    return 42
end 'getAnswer'
```

**Multiple Parameters**
```maxon
function add(a int, b int) int
    return a + b
end 'add'
```

**Array Parameters**
```maxon
function sum(numbers []int, count int) int
    var total = 0
    for i in range(0, count) 'loop'
        total = total + numbers[i]
    end 'loop'
    return total
end 'sum'
```

### Calling Functions
```maxon
var result = add(3, 4)
var answer = getAnswer()
```

### Extern Functions

Declare external functions (Windows API, C libraries):
```maxon
extern function GetStdHandle(nStdHandle int) int
extern function ExitProcess(uExitCode int) int
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
5. **Comparison**: `=` `!=` `<` `>` `<=` `>=`
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
var last = arr[arr.length - 1]
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
if condition 'label'
    statements
else 'label'
    statements
end 'label'
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
if let binding = optional_expression 'label'
    // binding is unwrapped value (non-optional) here
    statements
else 'label'
    // optional was nil
    statements
end 'label'
```

**Example**
```maxon
function safeDivide(a int, b int) int or nil
    if b == 0 'check'
        return nil
    end 'check'
    return a / b
end 'safeDivide'

var result = safeDivide(10, 2)
if let val = result 'unwrap'
    print_int(val + 5)  // val is unwrapped int
else 'unwrap'
    print("Division by zero")
end 'unwrap'
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

print_int(result)  // result is int, not int or nil
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
export function public_add(a int, b int) int
    return a + b
end 'public_add'

function private_helper(x int) int
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
print_int(value int)                    // Print integer to stdout with newline
print_float(value float, precision int) // Print float with specified decimal places
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
function main() int
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
    print_int(i)
    i = i + 1
end 'forever'
```

### Array Iteration
```maxon
var numbers = [1, 2, 3, 4, 5]
for i in range(0, numbers.length) 'iter'
    print_int(numbers[i])
end 'iter'
```


### Factorial Example
```maxon
function factorial(n int) int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() int
    var result = factorial(5)
    print_int(result)  // 120
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
function test() int
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
    print_int(x)
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
   function test(x int) int
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
   export function api() int    // Public API
   function internal() int      // Private helper
   ```

9. **Check `.length` before array access**:
   ```maxon
   if index < arr.length 'safe'
       var val = arr[index]
   end 'safe'
   ```

10. **Use meaningful block identifiers**:
    ```maxon
    while not done 'process'     // 'process' describes the loop
        // ...
    end 'process'
    ```

---

## Syntax Quick Reference

```maxon
// Variables
var mutable = 42
let immutable = 100

// Functions
function name(param type) returnType
    return value
end 'name'

// Control Flow
if condition 'id' statements end 'id'
if condition 'id' statements else 'id' statements end 'id'
while condition 'id' statements end 'id'
for var in iterable 'id' statements end 'id'
break
continue

// Arrays
var arr = [10]int
var vals = [1, 2, 3]
var elem = arr[0]
var size = arr.length

// Types
int float bool character
[N]type    // fixed-size array
[]type     // unsized array (parameters)

// Operators
+ - * / mod                  // arithmetic
= != < > <= >=               // comparison
and or not                   // logical
as                           // type cast

// Literals
42                           // int
3.14                         // float
'A'                          // character
"text"                       // string
true false                   // bool
```

---

**End of Reference**
