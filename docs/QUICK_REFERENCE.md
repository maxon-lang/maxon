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
"Line1\nLine2"      // escape sequences: \n \t \\ \" \{ \}
```

## Operators

| Precedence | Operators |
|------------|-----------|
| Highest | `.` `as` `()` |
| Unary | `-` `not` |
| Multiplicative | `*` `/` `mod` |
| Additive | `+` `-` |
| Comparison | `==` `!=` `<` `>` `<=` `>=` |
| Logical | `and` `or` |

## Variables

```maxon
let x = 42          // immutable (type inferred)
var y = 10          // mutable (type inferred)
```

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
for i in range(0, end: 10) 'loop'
    print("{i}")
end 'loop'

for item in array 'loop' ... end 'loop'
for char in string 'loop' ... end 'loop'
```

### Match Statement
```maxon
match value 'label'
    1 then doSomething()
    2 or 3 then doOther()
    pattern then action() and fallthrough
    default then fallback()
end 'label'
```

### Match Expression
```maxon
let result = match value 'label'
    1 gives "one"
    2 gives "two"
    default gives "other"
end 'label'
```

## Types 

```maxon
type Point is Hashable, Describable   // interface conformance
    export var x int                   // public mutable field
    export let name = "point"   // public immutable with default
    var internal int                   // private field

    function Hashable.hash() returns int   // interface method
        return x * 31 + y
    end 'hash'

    function magnitude() returns float     // regular method
        return sqrt((x * x + y * y) as float)
    end 'magnitude'

    static function origin() returns Point  // static method
        return {x: 0, y: 0}
    end 'origin'
end 'Point'

// Instantiation
var p = Point{x: 10, y: 20}
var o = Point.origin()
```

## Interfaces

```maxon
interface Hashable
    function hash() returns int
end 'Hashable'

interface Container uses Element       // associated type
    function get(index int) returns Element
end 'Container'

type IntBox is Container with int      // specify associated type
    function Container.get(index int) returns int
        // ...
    end 'get'
end 'IntBox'
```

## Enums

```maxon
// Simple
enum Direction
    north
    south
end 'Direction'

// Raw values
enum HttpStatus
    ok = 200
    notFound = 404
end 'HttpStatus'
var code = HttpStatus.ok.rawValue  // 200
var n = HttpStatus.ok.name         // "ok"

// String-backed
enum Planet
    earth = "Earth"
    mars = "Mars"
end 'Planet'
var p = Planet.mars
var raw = p.rawValue   // "Mars" (backing value)
var name = p.name      // "mars" (case name)

// Associated values
enum Result
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

// Create from name (throws EnumError.invalidName on unknown name)
var dir = try Direction.fromName("north") otherwise Direction.south
var c = try Result.fromName("success", 42) otherwise Result.pending

// Methods
enum Direction
    north
    function opposite() returns Direction
        if self == Direction.north 'c' return Direction.south end 'c'
        return Direction.north
    end 'opposite'
end 'Direction'
```

## Error Handling

```maxon
// Define error type (must be enum conforming to Error)
enum FileError is Error
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
s.contains("needle")
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
var c = 65 as character    // 'A'
var b = 1 as bool          // true
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
| `Comparable` | `compare(other) -> int` |
| `Cloneable` | `clone() -> Self` |
| `Stringable` | `toString(format) -> String` |
| `Iterable uses E` | `next() -> E throws IterationError` |
| `Sized` | `count() -> int` |
| `Indexed uses E` | `get(i) -> E throws ArrayError`, `set(i, value)` |
| `Error` | (marker for throwable enums) |

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
| `-v` | Verbose/debug output |
| `--emit-ir` | Output IR to `<source>.ir` |
| `--emit-asm` | Output assembly to `<source>.asm` |
| `--track-memory` | Runtime memory tracking (allocs, moves, refcounts) |
| `--track-registers` | Compile-time register/stack allocation debug info |

### Test Options (spec-test)
| Option | Description |
|--------|-------------|
| `--filter <pattern>` | Run tests matching pattern |
| `--verbose` | Detailed test output |
