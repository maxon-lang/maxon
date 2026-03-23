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

Standard library aliases: `Count`, `Index`, `ExitCode`, `Offset`, `HashValue`, `Codepoint`, `MathValue`, `NetworkPort`

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
"Line1\nLine2"      // escape sequences: \n \t \r \0 \\ \" \{ \} \xNN \uXXXX
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

// Lazy static fields (inside types)
// Complex initializers (function calls, struct/array literals) run on first access
static var _ws = CharacterSet.whitespacesAndNewlines()   // lazy, cached after first use
static var origin = Point{x: 0, y: 0}       // lazy struct literal
static var _data = [10, 20, 30]              // lazy array literal
static let MAX = 100                         // constant, evaluated at compile time

// Reference-by-default for structs
var a = Point{x: 1, y: 2}
var b = a               // reference -- b is an alias for a (same object)
b.x = 99               // a.x is now 99
b = Point{x: 5, y: 6}  // rebinds b -- a is unaffected

// Explicit clone for independent copy (requires Cloneable)
var c = a.clone()       // deep copy -- c is independent
c.x = 42               // a.x is still 99
```

All variables must be used (E3012). The exact name `_` is a discard identifier -- it creates no binding and is exempt from unused checks. Names like `_x` are regular variables and must be used. Self-assignment (`x = x`) is an error (E3067). `let _ =` requires a function call on the right-hand side (`let _ = 42` is an error).

**Assignment semantics:** For struct types, `var b = a` creates a reference (alias to the same heap object). Field mutation through the alias affects the original. Reassignment (`b = Point{...}`) rebinds without affecting the original. Use `var b = a.clone()` for an independent deep copy (the type must be `Cloneable`). Primitives are always copied by value.

**Auto-conformance:** The compiler auto-generates `Cloneable` and `Equatable` conformance for structs whose fields are all Cloneable/Equatable. Primitives, `String`, and `Array` are built-in Cloneable and Equatable types. Use `.clone()` to create independent copies.

**Scope cleanup:** Struct variables are automatically freed when they go out of scope (reference-counted). Returned structs are not freed at scope exit — the caller takes responsibility for their lifetime. Structs with all-primitive fields that don't escape scope are automatically stack-promoted (no heap allocation or refcounting). Use `@heap var p = Point{...}` to force heap allocation.

**Borrow checking:** You cannot mutate a collection while a variable borrows from it (e.g., a reference obtained via `.get()`). Borrows expire at the last use of the borrowing variable (non-lexical lifetimes). Error E3070.

Function return values must be used. Pure functions (no side effects) cannot have their results discarded at all. Impure functions can have results explicitly discarded with `let _ = func()`. Chainable methods (returning own type) may be freely discarded.

## Tuples

```maxon
// Tuple literal
var t = (10, 20)
t.0   // 10
t.1   // 20

// Tuple as function return type
function minMax(a int, b int) returns (int, int)
    return (a, b)
end 'minMax'

// Destructuring declaration (creates new variables)
var (lo, hi) = minMax(3, 7)

// Tuple assignment (assigns to existing variables)
var x = 0
var y = 0
(x, y) = minMax(3, 7)    // x = 3, y = 7

// Discard individual elements with _
(x, _) = minMax(3, 7)    // x = 3, second element discarded
```

**Tuple assignment rules:**
- All named targets must already exist as `var` (mutable) variables
- `let` variables cannot be targets (E2013)
- Name count must match tuple element count (E3005)
- `_` discards the element without binding

## Functions

```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function greet(name String, title String = "Mr.")  // string default
    print("Hello, {title} {name}")
end 'greet'

function process(items IntArray = [10, 20, 12]) returns Integer  // array default
    return items.count()
end 'process'

// Any literal expression is supported as a default value:
// integers, floats, bools, strings, arrays, enum cases,
// struct construction, character literals, byte string literals

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

Closure parameters are checked for unused (E3012). Use `_` to discard: `(_ int) gives 42`

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

## Conditional Compilation
```maxon
#if os(Windows)
  let sep = "\\"
#else
  let sep = "/"
#endif

#if arch(x86_64)
  // x86-specific code
#endif
```
Conditions: `os(Windows)`, `os(Linux)`, `os(Macos)`, `arch(x86_64)`, `arch(aarch64)`, `testing(true)`, `testing(false)`. Boolean operators: `not`, `and`, `or` (precedence: `or` < `and` < `not`). Can appear at top-level, inside functions, and inside type bodies. Nested `#if` blocks are supported.

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

// Enumerated (index + value)
for (i, item) in array.enumerated() 'loop' ... end 'loop'

// Skip: advance loop by n positions and continue
skip 2             // skip 2 elements in innermost for loop

// Discard loop variable when only side effects matter
for _ in array 'loop' ... end 'loop'
for (key, _) in pairs 'loop' ... end 'loop'   // discard value, keep key
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

All matches must be exhaustive. For non-enum/union matches (int, float, string, char), a `default` arm is required. Enum and union matches must cover all cases explicitly. Enums support range patterns: `Priority.low to Priority.high`. Unions with associated values support range patterns on bare case names: `caseName1 to caseName2` (inclusive) or `caseName1 upto caseName2` (exclusive upper bound). A range arm cannot extract bindings, but can cover cases that have associated values. Use `default throws` or `default panic("message")` for non-exhaustive matching (see below).

Pattern bindings are checked for unused (E3012). Use `_` to discard: `success(_)` or `pair(_, second)`. To discard all associated values, omit parentheses entirely: `success then ...`

### Match Expression
```maxon
let result = match value 'label'
    1 gives "one"
    2 gives "two"
    default gives "other"
end 'label'
```

### Default Throws / Default Panic (non-exhaustive enum/union match)
```maxon
// Statement form: throws an error for unmatched cases
// Enclosing function must declare 'throws ErrorType'
match shape 'draw'
    circle(r) then drawCircle(r)
    square(s) then drawSquare(s)
    default throws ShapeError.unsupported
end 'draw'

// Statement form: terminates with an error message
match shape 'draw'
    circle(r) then drawCircle(r)
    square(s) then drawSquare(s)
    default panic("unsupported shape")
end 'draw'

// Expression form: also throws for unmatched cases
let desc = match shape 'describe'
    circle(r) gives "circle"
    square(s) gives "square"
    default throws ShapeError.unsupported
end 'describe'
```

`default throws` and `default panic("message")` are the only forms of `default` allowed on enum and union matches (E2046). For non-enum/union matches, `default` with arbitrary code is still valid.

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

Enums are like unions but simpler: no methods, no associated values. Direct `==` and `!=` comparison is allowed. Enum matches require exhaustive coverage (all cases or range patterns). Enums support `.rawValue`, `.name`, `.ordinal`, `.allCases`, `fromRawValue()`, and `fromName()`.

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
    ok gives 1
    notFound gives 2
    serverError gives 3
end 'handle'

// rawValue, name, ordinal, allCases, fromRawValue, fromName
var code = s.rawValue      // 404
var name = s.name          // "notFound"
var pos = s.ordinal        // 1 (declaration position, not raw value)
for status in HttpStatus.allCases 'loop'  // iterate all cases
    print("{status.name}\n")
end 'loop'
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

// Panic (unrecoverable error — terminates with stack trace)
panic("invariant violated")
panic("expected {a}, got {b}")      // interpolated strings supported
```

## Async/Await

```maxon
// Spawn a green thread
var promise = async someFunction(arg1, arg2)

// Wait for the result
var result = await promise

// Parallel work
var p1 = async taskA()
var p2 = async taskB()
var r1 = await p1
var r2 = await p2

// Void functions
var p = async doWork()
await p

// Throwing async functions
var p = async mayFail(true)
var result = try await p otherwise 0

// Cancellation
var p = async longRunning()
p.cancel()
```

- Green threads are distributed across OS worker threads (one per CPU core)
- Context switches at `await` points and I/O operations
- Growable stacks (4KB initial, doubles as needed)
- Throwing async functions require `try await` (not plain `await`)
- `async` target must yield (contain I/O or `await` points)
- Unawaited green threads are drained at program exit

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
s.split(",")                           // split by delimiter
s.slice(startIdx, endIndex: endIdx)

// Trimming
s.trim()                                     // remove whitespace from both ends
s.trimStart()                                // remove whitespace from start
s.trimEnd()                                  // remove whitespace from end
s.trim(CharacterSet.decimalDigits())     // remove matching chars from both ends
s.trimStart(CharacterSet.from(CharSet from ['x']))     // remove matching chars from start
s.trimEnd(CharacterSet.punctuation())   // remove matching chars from end

// Iteration
for c in s 'chars' ... end 'chars'           // grapheme clusters
for b in s.bytes() 'bytes' ... end 'bytes'   // bytes
for cp in s.codepoints() 'cp' ... end 'cp'   // codepoints
```

## Type Conversions

```maxon
// Implicit: int -> float in arithmetic
// Implicit: 'A' -> 65 (char literal to codepoint when used with int)

// Explicit with 'as'
var f = 5 as float
var by = 255 as byte

// Float to int (no direct cast)
trunc(x)   // toward zero
round(x)   // nearest
floor(x)   // down
ceil(x)    // up
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
| `Iterable uses E` | `createIterator()`, `next() -> E throws IterationError` |
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
| `--filter=PATTERN` | Run tests matching pattern |
| `--verbose` | Show detailed failure messages for failing tests |
| `--update-required` | Regenerate and update RequiredMLIR + MmTrace stderr |
| `--workers=N` | Set number of parallel test workers |

## The self hosted compiler (currently in development)
- The source is in maxon-bin-selfhosted
- To build it run `maxon build` in /maxon-bin-selfhosted
