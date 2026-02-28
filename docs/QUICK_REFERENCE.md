# Maxon Quick Reference

## Types

| Type | Description | Example |
|------|-------------|---------|
| `int` | 64-bit signed integer | `42`, `-17` |
| `float` | Floating-point | `3.14`, `-2.5` |
| `bool` | Boolean | `true`, `false` |
| `byte` | 0-255 | `255 as byte` |
| `character literal` | Grapheme cluster | `'A'`, `'é'` |
| `string literal` | UTF-8 string | `"hello"` |
| `byte string literal` | ByteArray from string | `b"hello"` |

## Ranged Type Aliases

All numeric types in type positions require a `typealias` with range constraints (`bool` is exempt):

```maxon
typealias Age = int(0 to 150)       // inclusive upper bound
typealias Idx = int(0 upto 100)     // exclusive upper bound (0-99)
typealias Pct = float(0.0 to 100.0)
typealias FullInt = int(i64.min to i64.max)  // type.min/type.max for full range
typealias Handle = int(0 to u32.max)         // full u32 range
```

Construction and range checks:
```maxon
var a = Age{25}                     // construct with TypeName{value}
var x = Age{200}                    // compile error: out of range
var y = Age{someExpression}         // runtime range check (panics on violation)
```

Storage in arrays and globals uses the smallest fitting integer width (u8/i8, u16/i16, u32/i32, or i64). All arithmetic uses 64-bit operations regardless of storage type.

Standard library aliases: `Count`, `Index`, `ExitCode`, `Offset`, `HashValue`, `Codepoint`, `MathValue`

## Literals

```maxon
// Integers
42          // decimal
0xFF        // hex
0b1010      // binary
0o777       // octal
1_000_000   // with separators

// Floats (must have decimal point)
3.14

// Characters
'A'  '\n'  '\t'  '\\'  '\''

// Strings
"Hello, {name}!"    // interpolation with {}
"{n:04}"            // format specifier: zero-pad to width 4
"{f:.2}"            // format specifier: 2 decimal places
"Line1\nLine2"      // escape sequences: \n \t \r \0 \\ \" \{ \} \xNN
```

## Operators

| Precedence | Operators |
|------------|-----------|
| Highest | `.` `as` `()` |
| Unary | `-` `not` |
| Multiplicative | `*` `/` `mod` |
| Additive | `+` `-` |
| Shift | `shl` `shr` |
| Comparison | `==` `!=` `<` `>` `<=` `>=` `is` `is not` |
| AND | `and` |
| XOR | `xor` |
| Lowest | `or` |

`and`, `or`, `xor`, `not` are context-dependent: logical on `bool`, bitwise on `int`.
`==` on struct types requires the type to implement `Equatable` (error E3078 if not).
`is`, `is not` compare reference identity (same heap object) for struct types.
`shl`, `shr` work on integers only.

## Variables

```maxon
let x = 42          // immutable (type inferred)
var y = 10          // mutable (type inferred)
let _ = sideEffect()  // discard: no binding, no unused check

// Top-level variables (outside functions)
var globalCounter = 0   // mutable, accessible from any function
let MAX_SIZE = 1024     // immutable constant

// Reference-by-default for structs
var a = Point{x: 1, y: 2}
var b = a               // reference -- b is an alias for a (same object)
b.x = 99               // a.x is now 99
b = Point{x: 5, y: 6}  // rebinds b -- a is unaffected

// Explicit clone for independent copy (requires Cloneable)
var c = a.clone()       // deep copy -- c is independent
c.x = 42               // a.x is still 99
```

All variables must be used (E3012). The exact name `_` is a discard identifier -- it creates no binding and is exempt from unused checks. Names like `_x` are regular variables and must be used. Self-assignment (`x = x`) is an error (E3067). `let _ =` can only discard function call results, not literals or other expressions.

**Assignment semantics:** For struct types, `var b = a` creates a reference (alias to the same heap object). Field mutation through the alias affects the original. Reassignment (`b = Point{...}`) rebinds without affecting the original. Use `var b = a.clone()` for an independent deep copy (the type must be `Cloneable`). Primitives are always copied by value.

**Auto-conformance:** The compiler auto-generates `Cloneable` and `Equatable` conformance for structs whose fields are all Cloneable/Equatable. Primitives, `String`, and `Array` are built-in Cloneable and Equatable types. Use `.clone()` to create independent copies.

**Scope cleanup:** Struct variables are automatically freed when they go out of scope (reference-counted). Returned structs transfer ownership to the caller and are not freed at scope exit.

Function return values must be used. Pure functions (no side effects) cannot have their results discarded at all. Impure functions can have results explicitly discarded with `let _ = func()`. Chainable methods (returning own type) may be freely discarded.

## Functions

```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function greet(name String, title String = "Mr.")  // default value
    print("Hello, {title} {name}")
end 'greet'

// Calling: first arg positional, rest named
greet("Smith", title: "Dr.")
```

**Parameter passing:** Parameters are passed by value when only read. Parameters that are assigned to inside the function body are passed by reference -- mutations propagate back to the caller's `var` variable. Passing a `let` variable to a mutating parameter is a compile error (E3063). Literals and expressions create a temporary stack slot; their mutations are not visible to the caller.

**Purity and discarded results:** The compiler infers function purity (no side effects). Pure function results must always be used (E3064). Impure function results require `let _ =` to explicitly discard (E3065). Chainable methods (returning own type via `self`) can be freely discarded.

**Function overloads:** Multiple functions can share the same name if they differ by parameter types or parameter names. The compiler auto-selects by argument types when unambiguous. When signatures are identical, named arguments are required (E3007).

## Closures

```maxon
let addX = (n int) gives n + x   // single expression body
let double = (n int) gives n * 2
```

Closures capture variables from the enclosing scope **by reference**. Changes to a captured variable after the closure is created are visible inside the closure when it runs.

## Visibility

All declarations are file-scoped by default. Use `export` for cross-file visibility:

```maxon
typealias Score = int(i64.min to i64.max)
export function publicFunc() returns Score     // visible to other files
function privateFunc() returns Score           // only this file

export type Point                               // visible to other files
export union Color                              // visible to other files
export typealias Score = int(0 to 100)          // visible to other files
export var sharedCounter = 0                    // visible to other files
```

## Control Flow

### If
```maxon
if condition 'label'
    // ...
end 'label' else if other 'label2'
    // ...
end 'label2' else 'label3'
    // ...
end 'label3'
```

### While
```maxon
while condition 'loop'
    if done 'exit' break end 'exit'
    if skip 'next' continue end 'next'
end 'loop'

break 'loop'      // labeled break
continue 'loop'   // labeled continue
```

### For (iterator)
```maxon
for item in array 'loop' ... end 'loop'
for char in string 'loop' ... end 'loop'

// Ranges
for i in 1 to 5 'loop' ... end 'loop'       // inclusive: 1, 2, 3, 4, 5
for i in 1 upto 5 'loop' ... end 'loop'     // exclusive: 1, 2, 3, 4
for c in 'a' to 'z' 'loop' ... end 'loop'   // character range
```

### Match Statement
```maxon
match value 'label'
    1 then doSomething()
    2 or 3 then doOther()
    1..=10 then inRange()          // range pattern: 1 to 10 inclusive
    11..<20 then nearRange()       // range pattern: 11 to 19 (exclusive upper)
    pattern then action() and fallthrough
    42 then break                  // exit match early
    default then fallback()
end 'label'

break            // exits innermost match
break 'label'    // exits match (or loop) with that label
```

Range patterns: `a..=b` (inclusive), `a..<b` (exclusive upper), `a..` (open upper), `..=b`/`..<b` (open lower), `..` (wildcard).

Union matches must be exhaustive -- all cases must be listed explicitly. Use `default throws` for non-exhaustive union matching (see below).

### Match Expression
```maxon
let result = match value 'label'
    1 gives "one"
    2 gives "two"
    default gives "other"
end 'label'
```

### Default Throws (non-exhaustive union match)
```maxon
// Statement form: throws an error for unmatched union cases
// Enclosing function must declare 'throws ErrorType'
match shape 'draw'
    circle(r) then drawCircle(r)
    square(s) then drawSquare(s)
    default throws ShapeError.unsupported
end 'draw'

// Expression form: also throws for unmatched cases
let desc = match shape 'describe'
    circle(r) gives "circle"
    square(s) gives "square"
    default throws ShapeError.unsupported
end 'describe'
```

`default throws` is the only form of `default` allowed on union matches (E2046). For non-union matches, `default` with arbitrary code is still valid.

## Types 

```maxon
type Point implements Hashable, Describable   // interface conformance
    export var x int                   // public mutable field
    export let name = "point"   // public immutable with default
    var internal int                   // private field

    static var count = 0               // static mutable field
    static let MAX = 100               // static immutable constant

    function hash() returns int   // interface method
        return x * 31 + y
    end 'hash'

    function magnitude() returns float     // regular method
        return sqrt((x * x + y * y) as float)
    end 'magnitude'

    static function origin() returns Point  // static method
        return Point{x: 0, y: 0}
    end 'origin'
end 'Point'

// Instantiation
var p = Point{x: 10, y: 20}
var o = Point.origin()

// Static field access
Point.count = Point.count + 1
print(Point.MAX)
```

## Interfaces

```maxon
interface Hashable
    function hash() returns int
end 'Hashable'

interface Container uses Element       // associated type
    function get(index int) returns Element
end 'Container'

typealias Integer = int(i64.min to i64.max)
type IntBox implements Container with Integer  // specify associated type
    function get(index int) returns Integer
        // ...
    end 'get'
end 'IntBox'

// Where clauses: constrain type parameters to require interface conformance
type Map uses Key, Value where Key is Hashable           // single constraint
type Pair uses A, B where A is Hashable and Equatable    // multiple interfaces with 'and'
type Multi uses A, B where A is Hashable, B is Cloneable // multiple params with ','

// Interface extensions: add methods to all conforming types
extension Container
  function first() returns Element
    return self.get(0)
  end 'first'
end 'Container'

// Conditional extensions: restrict by associated type constraints
extension Iterable where Element is Equatable
  function contains(element Element) returns bool
    // only available when Element implements Equatable
  end 'contains'
end 'Iterable'

// Conditional conformance: add interface conformance when constraints are met
extension Array implements Hashable, Equatable where Element is Hashable and Equatable
  function hash() returns HashValue ... end 'hash'
  function equals(other Self) returns bool ... end 'equals'
end 'Array'
```

## Unions

```maxon
// Simple
union Direction
    north
    south
end 'Direction'

// Associated values
union Result
    success(value int)
    failure(code int, message String)
    pending
end 'Result'
var r = Result.success(42)
var r2 = Result.failure(404, message: "Not found")

// Pattern matching
match result 'handle'
    success(v) then print("{v}")
    failure(c, msg) then print("{c}: {msg}")
    pending then print("waiting")
end 'handle'

// Mutable match bindings (var union = write-back, let union = read-only)
var box = Result.success(10)
match box 'update'
    success(v) then v = 42       // writes back to box in-place
    failure(c, msg) then return
    pending then return
end 'update'

// Create from name (throws UnionError.invalidName on unknown name)
var dir = try Direction.fromName("north") otherwise Direction.south
var c = try Result.fromName("success", 42) otherwise Result.pending

// Methods
union Direction
    north
    south
    function opposite() returns Direction
        return match self 'c'
            north gives Direction.south
            south gives Direction.north
        end 'c'
    end 'opposite'
end 'Direction'
```

Union values cannot be compared with `==` or `!=` (error E3066). Use `match` to inspect unions. This prevents bugs when new cases are added to a union. Unions auto-conform to `Hashable` internally for Map/Set usage.

## Enums

Enums are like unions but simpler: no methods, no associated values. Direct `==` and `!=` comparison is allowed. Use `match` with a `default` arm. Enums support `.rawValue`, `.name`, `fromRawValue()`, and `fromName()`.

```maxon
// Integer (auto-increment from 0)
enum Color
    red       // 0
    green     // 1
    blue      // 2
end 'Color'

// Explicit integer values (mixed with auto-increment)
enum HttpStatus
    ok = 200
    notFound = 404
    serverError = 500
end 'HttpStatus'

// Float-backed
enum Threshold
    low = 0.1
    medium = 0.5
    high = 0.9
end 'Threshold'

// String-backed
enum ContentType
    json = "application/json"
    html = "text/html"
end 'ContentType'

var s = HttpStatus.notFound
if s == HttpStatus.notFound 'check'
    // direct comparison allowed
end 'check'

var result = match s 'handle'
    HttpStatus.ok gives 1
    HttpStatus.notFound gives 2
    HttpStatus.serverError gives 3
    default gives 0
end 'handle'

// rawValue, name, fromRawValue, fromName
var code = s.rawValue      // 404
var name = s.name          // "notFound"
var s2 = try HttpStatus.fromRawValue(200) otherwise HttpStatus.ok    // HttpStatus.ok
var s3 = try HttpStatus.fromName("notFound") otherwise HttpStatus.ok // HttpStatus.notFound

export enum Permission
    none = 0
    read = 1
    write = 2
end 'Permission'
```

## Error Handling

```maxon
// Define error type (must be union conforming to Error)
union FileError implements Error
    notFound
    permissionDenied
end 'FileError'

// Throwing function
function readFile(path String) returns String throws FileError
    if not exists(path) 'c' throw FileError.notFound end 'c'
    return contents
end 'readFile'

// Handle with default value
let content = try readFile("x") otherwise ""

// Ignore error
try mayFail() otherwise ignore

// Block handler
try mayFail() otherwise 'handler'
    print("Failed")
end 'handler'

// Block with error binding
try mayFail() otherwise (e) 'handler'
    print("Error: {e}")
end 'handler'

// Propagate (only in throwing functions)
let content = try readFile("x")   // propagates to caller

// Conditional try
if let value = try mayFail() 'ok'
    print("{value}")
end 'ok' else (e) 'err'
    print("Error")
end 'err'
```

## Arrays

```maxon
var arr = [1, 2, 3]                    // array literal
var empty = IntArray{}                 // typed empty array

arr.count()                            // length
try arr.get(0) otherwise 0             // access (throws ArrayError)
arr.set(0, value: 100)                 // modify
arr.push(42)                           // append
arr.pop()                              // remove last (throws)
arr.insert(0, value: 99)               // insert at index
arr.remove(at: 0)                      // remove at index (throws)
arr.reserve(100)                       // allocate capacity
arr.resize(50)                         // set length
arr.clear()                            // remove all
arr.first()                            // first element (throws)
arr.last()                             // last element (throws)
```

## Strings

There is no string concatenation, only interpolation.

```maxon
s.count()                              // grapheme count
s.byteLength()                         // byte count
s.isEmpty()                            // check empty

s.startsWith("prefix")
s.endsWith("suffix")
s.contains("text")
s.contains('x')
try s.find("needle") otherwise -1      // find index

s.toLower()
s.toUpper()
s.replace("old", "new")
s.replaceFirst("old", "new")
s.slice(startIdx, endIndex: endIdx)

// Iteration
for c in s 'chars' ... end 'chars'           // grapheme clusters
for b in s.bytes() 'bytes' ... end 'bytes'   // bytes
for cp in s.codepoints() 'cp' ... end 'cp'   // codepoints
```

## Type Conversions

```maxon
// Implicit: int -> float in arithmetic

// Explicit with 'as'
var f = 5 as float
var by = 255 as byte

// Float to int (no direct cast)
trunc(x)   // toward zero
round(x)   // nearest
floor(x)   // down
ceil(x)    // up
```

## Standard Library

### Math
```maxon
abs(x)              // int or float
sqrt(x)             // float
pow(base, exp)      // float
sin(x) cos(x) tan(x)
exp(x) log(x) log2(x) log10(x)
floor(x) ceil(x) round(x) trunc(x)  // float -> int
```

### I/O
```maxon
print(s)            // print string to stdout
```

### File
```maxon
File.exists(path)                         // bool
File.readText(path)                       // throws FileReadError
File.readBinary(path)                     // returns ByteArray, throws
File.writeText(path, content: s)          // throws FileWriteError
File.writeBinary(path, content: bytes)    // throws
File.delete(path)                         // throws FileDeleteError
```

### Directory
```maxon
Directory.exists(path)                    // bool
Directory.list(path)                      // StringArray, throws
```

### Process
```maxon
Process.execute(command, timeoutMs: 5000) // returns exit code
```

### CommandLine
```maxon
CommandLine.args()           // StringArray (excludes executable)
CommandLine.executablePath() // String
```

### Map
```maxon
var m = Map{key1: val1, key2: val2}  // literal syntax
m.insert(key, value: val)
try m.get(key)                       // throws MapError
m.contains(key)                      // bool
m.remove(key)                        // bool
m.count()                            // int
```

### Set
```maxon
var s = Set{[1, 2, 3]}               // from array literal
s.insert(element)
s.contains(element)                  // bool
s.remove(element)                    // bool
s.count()                            // int
```

### List
```maxon
typealias IntList = List with Integer
var list = IntList{}                  // empty linked list

list.prepend(1)                      // add to front — O(1)
list.append(2)                       // add to back — O(1)
list.insert(1, value: 99)           // insert at index — O(n)
try list.first() otherwise 0        // first element (throws)
try list.last() otherwise 0         // last element (throws)
try list.get(1) otherwise 0         // element at index (throws)
try list.removeFirst() otherwise 0  // remove front — O(1)
try list.removeLast() otherwise 0   // remove back — O(1)
try list.remove(at: 2) otherwise 0  // remove at index — O(n)
list.count()                         // number of elements
list.isEmpty()                       // check empty
list.clear()                         // remove all

for item in list 'loop' ... end 'loop'  // iteration (Iterable)
```

### Character
```maxon
c.byteLength()
c.bytes()                            // ByteView
c.codepoints()                       // CodepointView
try c.asciiValue()                   // throws CharacterError
```

## Standard Interfaces

| Interface | Methods |
|-----------|---------|
| `Hashable` | `hash() -> int` |
| `Equatable` | `equals(other) -> bool` |
| `Comparable` | `compare(other) -> Ordering` |
| `Cloneable` | `clone() -> Self` |
| `Stringable` | `toString() -> String` |
| `FormattedStringable` | `toString(format) -> String` |
| `Iterable uses E` | `next() -> E throws IterationError` |
| `Strideable` | `advancedBy(n) -> Self` (enables range expressions) |
| `Error` | (marker for throwable unions) |

## Command Line

### Commands
```bash
maxon compile <file.maxon>   # Compile single file → file.exe
maxon build                  # Build project in current directory
maxon spec-test              # Run spec fragment tests
maxon lsp-server             # Start LSP server for IDE integration
```

### Options (compile/build)
| Option | Description |
|--------|-------------|
| `--emit-ir` | Output IR to `<source>.ir` |
| `--dump-stages` | Write IR at each pipeline stage |
| `--mm-trace` | Enable runtime memory manager trace output (stderr) |
| `--log=LEVEL` | Set log level (none, error, info, debug, trace) |
| `--log=CAT:LEVEL` | Set log level per category |

### Test Options (spec-test)
| Option | Description |
|--------|-------------|
| `--filter <pattern>` | Run tests matching pattern |

## The self hosted compiler (currently in development)
- The source is in maxon-bin-selfhosted
- To build it run `maxon build` in /maxon-bin-selfhosted
