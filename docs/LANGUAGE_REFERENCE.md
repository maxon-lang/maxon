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
   - [Ranged Type Aliases](#ranged-type-aliases)
4. [Types (Composite)](#types-composite)
   - [Interface Extensions](#interface-extensions)
   - [Conditional Extensions](#conditional-extensions)
   - [Conditional Interface Conformance](#conditional-interface-conformance)
5. [Unions](#unions)
6. [Variables](#variables)
7. [Functions](#functions)
   - [Parameter Passing](#parameter-passing)
   - [Closures](#closures)
   - [Function Purity and Discarded Results](#function-purity-and-discarded-results)
8. [Expressions](#expressions)
9. [Statements](#statements)
10. [Error Handling](#error-handling)
11. [Namespaces](#namespaces)
12. [Standard Library](#standard-library)
13. [Build System](#build-system)
14. [Memory Model](#memory-model)
    - [Reference-by-Default Assignment](#reference-by-default-assignment)
    - [Explicit Cloning](#explicit-cloning)
    - [Cloneable Interface](#cloneable-interface)
    - [Auto-Equatable](#auto-equatable)
    - [Scope Cleanup](#scope-cleanup)
    - [Ownership System](#ownership-system)

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
- One or more function, type, union, enum, or typealias declarations
- Namespace derived from file path
- Use `export` keyword for cross-file visibility (applies to functions, types, unions, enums, and typealiases)

---

## Lexical Elements

### Comments
Single-line comments only:
```maxon
// This is a comment
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
return, returns, self, Self, shl, shr, skip, static, then, throw, throws, to,
true, try, type, typealias, union, upto, uses, var, where, while, with, xor
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
'\x41'        // Hex escape (character 'A')
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
```

Escape sequences: `\n` `\t` `\r` `\0` `\\` `\"` `\{` `\}` `\xNN`

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

Byte string literals use the `b"..."` prefix to create a `ByteArray` (`Array with Byte`) directly from a string. They support the same escape sequences as regular string literals, including `\xNN` hex escapes for arbitrary byte values (0x00-0xFF). This is useful when working with raw bytes or APIs that expect byte arrays.

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
typealias Age = int(0 to 150)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = byte(0 to u8.max)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive:

```maxon
typealias Index = int(0 upto 100)   // 0 to 99
```

**Type-qualified bounds:**

Use `type.min` and `type.max` to reference bounds of specific numeric types:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
```

Supported types: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`.

When both bounds use type qualifiers, they must reference the same type (e.g., `i64.min to i64.max`, not `i8.min to i32.max`). A type-qualified bound paired with a literal must form a natural range — `0 to u32.max` is valid, but `0 to i64.max` is an error (use `i64.min to i64.max` or `0 to u64.max` instead). Byte ranges must have bounds within 0 to u8.max.

**Construction:**

Create values using `TypeName{value}` syntax:

```maxon
typealias Age = int(0 to 150)
var myAge = Age{25}
```

**Compile-time range checks:**

Literal values are checked at compile time. This is a compile error:

```maxon
typealias SmallInt = int(0 to 10)
var x = SmallInt{15}   // error: Value 15 is outside the range of 'SmallInt'
```

**Runtime range checks:**

When the value is a computed expression, a runtime range check is emitted that panics on violation:

```maxon
typealias Age = int(0 to 150)
typealias Year = int(i64.min to i64.max)
function makeAge(n Year) returns Year
  var a = Age{n}   // runtime check: panics if n < 0 or n > 150
  return a
end 'makeAge'
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
var a = Score{30}
var b = Score{12}
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
typealias Pixel = int(0 to 65535)    // stored as u16 in arrays and globals
typealias Offset = int(-32768 to 32767)  // stored as i16 in arrays and globals
typealias Age = int(0 to 150)        // stored as u8 in arrays and globals
```

Local variables always use 64-bit registers regardless of the ranged type's storage class.

**Standard library aliases:**

The standard library provides purpose-specific aliases:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `Count` | `int(0 to i64.max)` | Non-negative counts |
| `Index` | `int(0 to i64.max)` | Array indices |
| `ExitCode` | `u32` | Process exit codes |
| `Offset` | `i64` | Signed offsets |
| `HashValue` | `u32` | Hash function results |
| `Codepoint` | `int(0 to 1114111)` | Unicode codepoints |
| `MathValue` | `f64` | Math function results |

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
typealias Integer = int(i64.min to i64.max)
typealias StringMap = Map with (String, Integer)  // OK: String implements Hashable
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

## Unions

Unions define a type with a fixed set of named variants called cases. Maxon supports two kinds of unions: simple unions and unions with associated values.

### Simple Unions

The simplest form of union defines named cases with no additional data:

```maxon
union Direction
    north
    south
    east
    west
end 'Direction'
```

Create union values using dot notation:

```maxon
var dir = Direction.north
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

Union values cannot be compared using `==` or `!=` (error E3066). The only way to inspect a union value is through `match`. This restriction exists to prevent a class of bugs that happen when a new value is added to a union that is unaccounted for and code that handles the union either falls through or uses a default value that is wrong.

```maxon
var dir = Direction.north
if dir == Direction.north 'check'    // ERROR E3066: Cannot compare union values
    print("Going north!")
end 'check'

// Use match instead
match dir 'check'
    north then print("Going north!")
    south then print("Going south!")
    east then print("Going east!")
    west then print("Going west!")
end 'check'
```

Unions still auto-conform to `Hashable` internally, so they can be used as `Map` keys and `Set` elements. However, users cannot call `==` or `!=` on union values directly.

### Creating Unions from Names (`fromName`)

The `fromName` static method creates a union value from a string name. It returns an error union that throws `UnionError.invalidName` if the name doesn't match any case:

```maxon
union Direction
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
- Returns `throws UnionError`, use with `try...otherwise` or `try...catch`
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

Call methods using type-qualified syntax:

```maxon
var dir = Direction.north
var opp = Direction.opposite(self: dir)  // Direction.south
```

### Union as Function Parameter

Unions can be used as function parameters and return types:

```maxon
union Status
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

### Union Interface Conformance

Unions can conform to interfaces using the `implements` keyword, similar to types:

```maxon
union FileError implements Error
    notFound
    permissionDenied
    alreadyExists
end 'FileError'

union HttpError int implements Error
    badRequest = 400
    notFound = 404
    serverError = 500
end 'HttpError'
```

**Notes:**
- The `implements Interface` clause comes after the optional backing type
- Multiple interfaces can be specified: `union Foo implements A, B`
- The `Error` interface can only be implemented by unions (not types/structs)

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
- For struct-typed variables, `var b = a` creates a reference (alias to the same object); use `var b = a.clone()` for an independent copy (see [Reference-by-Default Assignment](#reference-by-default-assignment))
- All variables must be used; unused variables cause a compile error (E3012). This applies to `let`/`var` declarations, function parameters, for-loop variables, match pattern bindings, and closure parameters.
- The variable name `_` is a special discard identifier: it creates no binding and is exempt from unused variable checks. Only the exact name `_` is a discard -- names like `_x` are regular variables subject to normal unused checks. Multiple `_` discards are allowed in tuple destructuring and match patterns (e.g., `for (_, _) in pairs` or `case pair(_, _)`).

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
- `let` declares an immutable top-level constant (compile-time evaluated)
- Must have an initializer with a constant expression (integer, float, bool, or string literal)
- Initialized before `main()` executes
- Accessible from any function in the same file (use `export` for cross-file visibility)

**Use Cases:**
- Configuration constants (`let MAX_BUFFER_SIZE = 4096`)
- Counters and state shared across function calls (`var callCount = 0`)
- Program-wide settings

---

## Enums

Enums define a named group of typed constant values. They support direct `==` and `!=` comparison and provide `.rawValue`, `.name`, `fromRawValue()`, and `fromName()`. Unlike unions, enums have no methods and no associated values.

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

Enums support the same backing types as unions: integer, float, String, and Character.

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

Unlike unions, enums allow direct `==` and `!=` comparison:

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

Use `match` with a `default` arm. Exhaustiveness is not checked for enums since the set of valid values is not fully enumerable at compile time:

```maxon
var result = match s 'handle'
    HttpStatus.ok gives 1
    HttpStatus.notFound gives 2
    HttpStatus.serverError gives 3
    default gives 0
end 'handle'
```

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

### Converting from Raw Value (`fromRawValue`)

The `fromRawValue` static method converts a raw value to an enum case. It is only available on enums (not unions). It throws `EnumError.invalidRawValue` if no case matches:

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
- Both `fromRawValue` and `fromName` return an error union; use `try...otherwise` or `try...catch`
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

Keywords can be used as case names (same as unions):

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

**Ownership interaction:** When a `var` variable is passed to a mutating parameter, ownership transfers to the callee for the duration of the call. After the call returns, the caller's variable reflects the updated value and ownership is restored. The variable cannot be used again in a branch where the call may not return (e.g., after a `throw`).

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

Pure function results **must** be used -- they cannot be discarded, even with `let _ =`. Since a pure function has no side effects, calling it without using the result is always a mistake.

```maxon
function double(x int) returns int
    return x * 2
end 'double'

double(5)               // Error E3064: result of pure function 'double' must be used
let _ = double(5)       // Error E3064: result of pure function 'double' must be used
let result = double(5)  // OK: result is used
```

#### Discarding Impure Function Results

Impure function results **must** be explicitly acknowledged. A bare statement-level call that ignores the result is an error. To intentionally discard the result, use `let _ =`:

```maxon
var counter = 0
function incrementAndGet() returns int
    counter = counter + 1
    return counter
end 'incrementAndGet'

incrementAndGet()               // Error E3065: result of 'incrementAndGet' is not used
let _ = incrementAndGet()       // OK: explicitly discarded
let count = incrementAndGet()   // OK: result is used
```

#### Chainable Methods

Methods that take `self` as their first parameter and return the same type are **chainable**. Their results can be freely discarded without `let _ =`, since the common pattern is to call them for their side effect on the receiver:

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
var (_, _) = pureFunc()        // Error E3064: all elements discarded for pure function
```

#### Error Codes

| Code | Meaning |
|------|---------|
| E3064 | Result of a pure function must be used (cannot be discarded) |
| E3065 | Result of an impure function is not used (assign to `_` to discard) |

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

Using `==` or `!=` on struct types requires the type to implement the `Equatable` interface (error E3078 if it does not). Primitives, `String`, and `Array` support `==` and `!=` without restriction. For reference identity comparison (same heap object), use `is` and `is not` instead.

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
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var numbers = IntArray{}         // Empty array
numbers.push(42)                 // Add elements with push
```

To preallocate with a specific length (elements zero-initialized):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray{}
buffer.resize(100)               // Length is now 100
buffer.set(0, value: 42)         // Can set any index 0-99
```

To preallocate capacity without changing length (for performance):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

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

Assigning a variable to itself is a compile error (E3067), since it has no effect:
```maxon
x = x         // ERROR: self-assignment has no effect
p.x = p.x     // ERROR: self-assignment has no effect
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

Ranges work with any type implementing the `Strideable` interface:

```maxon
interface Strideable
    function advancedBy(n int) returns Self
end 'Strideable'
```

The standard library provides `Strideable` conformance for `int` and `Character`.

**Iterating with an index:**

Append `.enumerated()` to any iterable to get a zero-based index alongside each element:

```maxon
var names = ["Alice", "Bob", "Charlie"]
for (i, name) in names.enumerated() 'loop'
    print("{i}: {name}\n")
end 'loop'
// 0: Alice
// 1: Bob
// 2: Charlie
```

This works on all iterable types (Array, String, Map, Set, List, etc.). The `EnumeratedIterator` is a lazy wrapper that tracks the index — no intermediate collection is created.

**Notes:**
- Loop variable is immutable (like `let`)
- Ranges use `to` for inclusive end and `upto` for exclusive end
- Desugars to while loop with iterator interface
- The compiler calls `createIterator()` before each loop to reset iteration state, enabling safe re-iteration of the same collection
- Loop variables are checked for unused (E3012). Use `_` as the loop variable when the value is not needed: `for _ in array 'loop'`. In tuple destructuring, each element is checked independently: `for (_, name) in pairs.enumerated() 'loop'`

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

**Union Case Pattern Matching:**

For unions with associated values, use `CaseName(bindings)` syntax to extract values:

```maxon
union Result
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
- For unions, all cases must be covered explicitly (error E2026) — `default` with arbitrary code is not allowed (error E2046). Use `default throws` for non-exhaustive union matching (see below).
- `default` matches any non-union value not matched by previous patterns
- `default` must be the last case if present
- Union case patterns: `CaseName(binding1, binding2)` extracts associated values
- Pattern bindings are checked for unused (E3012). Use `_` to discard individual bindings: `case success(_)` or `case pair(_, second)`

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

**Union Case Extraction:**
```maxon
union Container
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
- Union bindings work the same as in match statements

### Default Throws in Union Match

When matching on a union, all cases must normally be covered explicitly (exhaustive matching). To handle only a subset of cases, use `default throws` to specify an error to throw for unmatched cases. This makes non-exhaustive matching explicit and produces a catchable error rather than a silent panic.

The `default throws` clause throws the specified error when no other case matches. The enclosing function must declare `throws ErrorType` to use this feature.

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
union Shape
    circle(radius float)
    square(side float)
    triangle(base float, height float)
end 'Shape'

union ShapeError implements Error
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
- `default throws` is the only form of `default` allowed in union matches -- `default` with arbitrary code on unions is forbidden (error E2046)
- The error value must be a valid union case of an `Error`-conforming type
- The enclosing function must declare `throws` with a matching error type
- Callers must handle the thrown error using `try ... otherwise` or `try` propagation
- Supports all the same features as regular match: associated value extraction, `and fallthrough`, `break`, etc.
- For non-union matches, `default` with arbitrary code remains valid as before

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

### Skip Statement
```maxon
skip n            // Skip n elements in innermost for loop
```
Advances the loop by `n` positions and continues to the next iteration. Valid inside iterator-based and range-based `for` loops (not `while` loops). Like `continue`, `skip` abandons the rest of the current iteration body before advancing.

- `skip 0` is equivalent to `continue`
- If skipping past the end, the loop exits normally
- `n` can be any non-negative integer expression

---

## Error Handling

Maxon uses a unified error handling system based on typed errors. Functions either return a value or throw an error—there are no optional types or null values. Error types must be unions conforming to the `Error` interface.

### Defining Error Types

Error types are unions that conform to the `Error` interface:

```maxon
// Simple union error
union FileError implements Error
    notFound
    permissionDenied
    alreadyExists
end 'FileError'

// Int-backed union error (for error codes)
union HttpError int implements Error
    badRequest = 400
    notFound = 404
    serverError = 500
end 'HttpError'

// String-backed union error (for messages)
union ValidationError String implements Error
    emptyField = "Field cannot be empty"
    invalidFormat = "Invalid format"
end 'ValidationError'
```

**Note:** Only unions can conform to `Error`. Attempting to make a type (struct) conform to `Error` produces a compile error (E023).

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

Capture the error as a typed union for inspection:

```maxon
try readFile("config.json") otherwise (e) 'handler'
    match e 'check'
        FileError.notFound then print("File not found")
        FileError.permissionDenied then print("Permission denied")
        FileError.alreadyExists then print("Already exists")
    end 'check'
end 'handler'
```

The error is bound to the variable `e` as a typed union value, allowing you to match on specific error cases. For error unions with associated values, you can extract the payload in the match arm.

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
union ArrayError implements Error
    indexOutOfBounds
end 'ArrayError'

// Map key lookup
union MapError implements Error
    keyNotFound
end 'MapError'

// Iterator exhaustion
union IterationError implements Error
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
   1=err   union ordinal (8 bytes)
```

### Complete Example

```maxon
union ParseError implements Error
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

Functions, types, unions, enums, typealiases, and top-level variables are file-scoped by default. Use the `export` keyword to make them visible to other files:

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

**Exporting types and unions:**

```maxon
typealias Coord = int(i64.min to i64.max)

export type Point
  export var x Coord
  export var y Coord
end 'Point'

export union Color
  red
  green
  blue
end 'Color'
```

Without `export`, types and unions are only usable within the file where they are declared.

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

### List

`List` is a generic doubly linked list backed by `__Chain` (a builtin compiler-synthesized type, like `Array` and `String`) for efficient node management with automatic memory cleanup. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

**Creating a List**

Create a concrete List type with `typealias`, then initialize with `{}`:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

var list = IntList{}             // Empty list
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

`List` implements `Iterable with Element`, so it supports `for`-`in` loops:
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

Primitives (`int`, `float`, `bool`, `byte`) are value types and are always copied on assignment. Only heap-allocated types (structs, strings, arrays) use reference semantics.

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

**When auto-conformance fails:** If a struct contains a field whose type is not Cloneable (such as a union with associated values), the compiler will not auto-generate conformance. You must implement `clone()` manually or restructure the type.

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

If a struct contains a field that doesn't implement `Equatable` (such as a function type), using `==` produces error E3078.

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

**Return values transfer ownership:** When a struct is returned from a function, its ownership transfers to the caller. The returned variable is not released at scope exit.

```maxon
function makePoint() returns Point
  var p = Point{x: 1, y: 2}
  return p                      // ownership transfers to caller, p is NOT freed
end 'makePoint'
```

**Container cleanup:** Containers with heap-allocated elements (e.g., `List with MyStruct`) perform deep cleanup when freed. Each element's refcount is decremented, and elements whose refcount reaches zero are freed recursively. For `List`, the compiler walks all chain nodes and decrefs their stored values before freeing the chain itself.

```maxon
typealias TokenList = List with Token

function example() returns int
  var list = TokenList{}
  list.append(Token{id: 1})   // Token incref'd by the chain node
  list.append(Token{id: 2})   // Token incref'd by the chain node
  return 0                     // list freed: each Token decref'd (rc→0→freed),
                               // then chain nodes freed, then chain freed
end 'example'
```

### Ownership System

Maxon implements a compile-time ownership system that tracks value ownership and prevents use-after-move errors without runtime overhead.

Every variable in Maxon has ownership of its value. When a variable is passed to a function:

- **Borrow**: If the function only reads the parameter, ownership stays with the caller
- **Move**: If the function mutates the parameter, ownership transfers to the callee

After a move, the original variable cannot be used or reassigned - this is a compile-time error.

**Example:**
```maxon
function main() returns ExitCode
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
- Parameters that are only read are passed by value (simple types in registers, arrays as pointer)
- Parameters that are assigned to inside the callee are passed by reference (pointer to caller's storage)
- Return values passed by value (in register or stack)

---

## Code Generation

### Native x86-64 Backend
- Maxon uses a custom x86-64 backend (no LLVM dependency)
- Compiles through an MLIR-inspired multi-stage pipeline
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

**Self-Assignment**
```maxon
var x = 5
x = x                   // ERROR: self-assignment has no effect
```

**Useless Discard**
```maxon
let _ = 42              // ERROR: discarding a non-call expression has no effect
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
