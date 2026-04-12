# Maxon Language Rules for AI Agents

ALWAYS read this document before writing or modifying Maxon code.
For full specification see [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md), [BNF_SYNTAX.md](BNF_SYNTAX.md), [QUICK_REFERENCE.md](QUICK_REFERENCE.md).

---

## Syntax that DOES NOT EXIST in Maxon

These are the most common mistakes. NEVER use any of these:

```
WRONG                          CORRECT
─────────────────────────────  ─────────────────────────────
let x: int = 5                 let x = 5
var y: String = "hi"           var y = "hi"
x += 1                         x = x + 1
x++                            x = x + 1
x % 5                          x mod 5
!condition                     not condition
a && b                         a and b
a || b                         a or b
a & b                          a and b
a | b                          a or b
a ^ b                          a xor b
a << 4                         a shl 4
a >> 4                         a shr 4
if (x > 0) { ... }            if x > 0 'label' ... end 'label'
} else {                       end 'label' else 'label2'
"hello " + name                "hello {name}"
null / nil / None              (does not exist — use try...otherwise)
;                              (no semicolons — newline-delimited)
func(a, b, c)                  func(a, b: b, c: c)
param int                      param SomeTypealias
returns int                    returns SomeTypealias
cond ? a : b                   a if cond else b
```

---

## Mandatory Rules

### 1. Every block MUST have a label and matching `end`

```maxon
// WRONG — no labels
if x > 0
	print("yes")
end

// CORRECT
if x > 0 'positive'
	print("yes")
end 'positive'
```

This applies to: `if`, `else`, `while`, `for`, `match`, `try...otherwise` blocks, `function`, `type`, `enum`, `union`, `interface`, `extension`.

### 2. `else` MUST appear on the same line as its `end`

```maxon
// WRONG
end 'check'
else 'other'

// CORRECT
end 'check' else 'other'
	// ...
end 'other'

// else-if:
end 'check' else if x == 0 'zero'
	// ...
end 'zero' else 'other'
	// ...
end 'other'
```

### 3. NEVER use bare `int`, `float`, or `byte` as types

All numeric types in type positions (parameters, return types, fields) MUST use a typealias with range constraints.

```maxon
// WRONG
function add(a int, b int) returns int

// CORRECT
typealias Offset = int(i64.min to i64.max)
function add(a Offset, b Offset) returns Offset
```

Use stdlib aliases when appropriate: `ExitCode`, `Count`, `Index`, `Offset`, `HashValue`, `Codepoint`, `MathValue`.

Wide ranges like `int(0 to u64.max)` are fine when no concrete upper bound exists (line numbers, array indices, etc.). Use tight ranges only for concrete domain limits (`Port = int(0 to 65535)`).

### 4. `bool` is the exception — use it directly

`bool` does NOT require a typealias. Use it directly in parameters, return types, and fields.

### 5. Variable declarations NEVER have type annotations

```maxon
// WRONG — colon syntax does not exist (E2010)
let x: int = 5
var name: String = "hi"

// CORRECT — type is always inferred
let x = 5
var name = "hi"
```

### 6. First argument is positional, all others MUST be named

```maxon
// WRONG
connect("localhost", 8080, 5000)

// CORRECT
connect("localhost", port: 8080, timeout: 5000)
```

### 7. `main` MUST return `ExitCode` and MUST NOT throw

```maxon
function main() returns ExitCode
	return 0
end 'main'
```

### 8. Collection access ALWAYS requires `try...otherwise`

`.get()` throws. NEVER call it without `try`.

```maxon
// WRONG
let val = arr.get(0)

// CORRECT
let val = try arr.get(0) otherwise 0
```

### 9. Throwing functions MUST be called with `try`

```maxon
// WRONG (E3057)
let content = readFile(path)

// CORRECT
let content = try readFile(path) otherwise ""
```

### 10. Match arms MUST use bare case names

```maxon
// WRONG (E3075)
match color 'c'
	Color.red then doRed()
end 'c'

// CORRECT
match color 'c'
	red then doRed()
end 'c'
```

### 11. Enum and union match MUST be exhaustive

Cover all cases. If using `default` on enum or union match, it MUST be `default throws` or `default panic`:

```maxon
// WRONG (E2046)
match status 'handle'
	ok then doOk()
	default then doDefault()
end 'handle'

// CORRECT
match status 'handle'
	ok then doOk()
	notFound then doNotFound()
	serverError then doError()
end 'handle'

// ALSO CORRECT (partial match with throw)
match status 'handle'
	ok then doOk()
	default throws StatusError.unhandled
end 'handle'

// ALSO CORRECT (panic for unreachable cases)
match status 'handle'
	ok then doOk()
	default panic("unexpected status")
end 'handle'
```

### 12. Union values CANNOT be compared with `==`

```maxon
// WRONG (E3066) — unions do not support ==
if result1 == result2 'cmp' ... end 'cmp'

// CORRECT — use match
match result 'check'
	success(v) then handleSuccess(v)
	failure(c, msg) then handleFailure(c, msg: msg)
end 'check'
```

### 13. Indentation uses tabs

NEVER use spaces for indentation.

### 14. Strings use `{expr}` interpolation

```maxon
// WRONG — no string concatenation operator
var msg = "hello " + name

// CORRECT
var msg = "Hello, {name}!"

// Format specifiers after colon
print("{n:04x}")    // zero-padded hex
print("{f:.2}")     // 2 decimal places
```

Escape literal braces: `\{` and `\}`.

To build a string incrementally, use `append`:
```maxon
var s = ""
s.append("hello")
s.append(" {name}!")    // interpolation written directly into buffer
```

### 15. Comments use `//`

```maxon
// This is a comment
```

---

## Declaration Reference

### Functions

```maxon
function name(param1 Type1, param2 Type2) returns ReturnType
	// body
end 'name'

// Throwing:
function load(path FilePath) returns Config throws FileError
	// ...
end 'load'

// Void (no returns clause):
function printStatus()
	print("OK\n")
end 'printStatus'

// Default parameters:
function connect(host String, port Port = 8080) returns Connection
	// ...
end 'connect'

// Static method:
export static function create() returns MyType
	return MyType{field: 0}
end 'create'
```

### Variables

```maxon
let x = 42          // immutable
var y = 10          // mutable
_ = sideEffect()     // discard (RHS MUST be a function call)
```

Use `var` for any variable you call mutating methods on (`push`, `set`, `remove`, `clear`, `append`, etc.):
```maxon
var items = Array with int{}   // var because we call push
items.push(1)
```

### Struct types

```maxon
export type Point
	export var x MathValue   // public mutable
	export let name String   // public immutable
	var internal Count       // private

	function magnitude() returns MathValue
		return sqrt((self.x * self.x + self.y * self.y) as float)
	end 'magnitude'

	static function origin() returns Point
		return Point{x: 0.0, y: 0.0}
	end 'origin'
end 'Point'

var p = Point{x: 1.5, y: 2.5}
var o = Point.origin()
```

### Enums

Enums define named constants with optional raw values. They auto-implement `Equatable` and `Hashable`. Enums do NOT support associated values -- use `union` for that.

```maxon
enum Color
	red       // 0
	green     // 1
	blue      // 2
end 'Color'

enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'
```

Properties: `.rawValue`, `.name`, `.ordinal`, `.allCases`, `.allCaseNames`.
Methods: `fromRawValue()`, `fromName()` (throw -- use with `try`).
`==` and `!=` work on enums.

### Unions

Unions define named cases with optional associated values. They do NOT implement `Equatable` or `Hashable`, do not support `==`/`!=`, and do not have raw values. Use `match` to inspect union values. Unions support `.name`, `.ordinal`, and the static `.allCaseNames` (an `Array with String` of case names). Unions do not support `.allCases`.

```maxon
union Result
	success(value Integer)
	failure(code Integer, message String)
	pending
end 'Result'

var r = Result.success(42)
var f = Result.failure(404, message: "Not found")
```

`==` does NOT work on unions. Use `match`.

### Error Enums

```maxon
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'
```

### Interfaces

```maxon
interface Describable
	function describe() returns String
end 'Describable'

interface Container uses Element
	function get(index Index) returns Element throws ArrayError
end 'Container'
```

### Extensions

```maxon
extension Array where Element is Equatable
	export function contains(element Element) returns bool
		// ...
	end 'contains'
end 'Array'
```

### Type aliases

```maxon
export typealias Score = int(0 to 100)
export typealias ScoreArray = Array with Score
export typealias ScoreMap = Map with(String, Score)
```

### Ranged type construction

```maxon
typealias Port = int(0 to 65535)
var p = Port{8080}          // construct with TypeName{value}
var bad = Port{70000}       // compile error: out of range
```

---

## Control Flow

### if / else if / else

```maxon
if x > 0 'positive'
	print("positive\n")
end 'positive' else if x == 0 'zero'
	print("zero\n")
end 'zero' else 'negative'
	print("negative\n")
end 'negative'
```

### while

```maxon
while count < 10 'loop'
	count = count + 1
end 'loop'
```

### for

```maxon
for i in 1 to 5 'loop' ... end 'loop'           // inclusive: 1,2,3,4,5
for i in 0 upto n 'loop' ... end 'loop'          // exclusive: 0..n-1
for item in array 'each' ... end 'each'           // collection
for (i, item) in array.enumerated() 'e' ... end 'e'  // with index
for color in Color.allCases 'c' ... end 'c'       // enum cases
for c in "hello" 'ch' ... end 'ch'                // string chars
for _ in 0 upto 10 'r' ... end 'r'                // discard variable
```

### break / continue

```maxon
break              // exit innermost loop
break 'outerLoop'  // exit labeled loop
continue           // skip to next iteration
continue 'outer'   // labeled continue
```

### match statement

```maxon
match value 'label'
	1 then doOne()
	2 or 3 then doTwoOrThree()
	4 to 10 then doRange()
	default then doDefault()
end 'label'
```

Each arm is ONE statement. `default` MUST be last. Fallthrough: `then action() and fallthrough`.
Use `default panic("message")` when unhandled cases are programming errors.

### match expression

```maxon
let label = match status 'map'
	ok gives "Success"
	notFound gives "Not Found"
	serverError gives "Error"
end 'map'
```

Use `gives` (not `then`) for expressions.

### Conditional expression

```maxon
let label = "yes" if enabled else "no"
let abs = x if x > 0 else -x

// Binds looser than all binary operators
let result = a + b if flag else c * d    // (a + b) if flag else (c * d)

// Chaining (right-associative)
let tier = "gold" if s > 90 else "silver" if s > 70 else "bronze"

// Inside string interpolation
print("Mode: {"fast" if turbo else "normal"}")
```

Condition must be `bool`. Both arms must produce the same type.

### Union destructuring

```maxon
match result 'handle'
	success(value) then print("{value}")
	failure(code, msg) then print("{code}: {msg}")
	pending then print("waiting")
end 'handle'
```

---

## Error Handling

```maxon
// Define error type
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'

// Throwing function
function readFile(path FilePath) returns String throws FileError
	if not path.fileExists() 'missing'
		throw FileError.notFound
	end 'missing'
	return content
end 'readFile'

// Default value
let content = try readFile(path) otherwise ""

// Handler block
try readFile(path) otherwise 'err'
	print("Failed\n")
	return 1
end 'err'

// Error binding
try readFile(path) otherwise (e) 'err'
	match e 'handle'
		notFound then print("Not found\n")
		permissionDenied then print("Denied\n")
	end 'handle'
end 'err'

// Ignore
try cleanup() otherwise ignore

// Panic on failure (for unreachable error paths)
let slot = try slots.get(idx) otherwise panic("unreachable: index validated")

// Propagate (only in throwing functions)
let content = try readFile(path)

// if-try
if let value = try mayFail() 'ok'
	print("{value}")
end 'ok' else (e) 'err'
	print("Error\n")
end 'err'

// Panic (unrecoverable)
panic("invariant violated: {details}")
```

---

## Collections

### Arrays

```maxon
typealias IntArray = Array with Offset

var arr = [1, 2, 3]
var empty = IntArray.create()

arr.push(42)                              // append
arr.count()                               // length
let val = try arr.get(0) otherwise 0      // access (ALWAYS use try)
arr.set(0, value: 100)                    // modify
arr.reserve(100)                          // pre-allocate
arr.resize(50)                            // set length
arr.pop()                                 // remove last (throws)
arr.insert(0, value: 99)                  // insert at index
arr.remove(at: 0)                         // remove at index (throws)
arr.clear()                               // remove all
```

### Maps

```maxon
typealias StringIntMap = Map with(String, Offset)

var m = ["hello": 42]
let val = try m.get("hello") otherwise 0  // ALWAYS use try
m.set("world", value: 99)
m.containsKey("hello")
m.remove("hello")
m.count()
```

### Strings

```maxon
s.count()                    // grapheme count
s.byteLength()               // byte count
s.isEmpty()
s.startsWith("prefix")
s.endsWith("suffix")
s.contains("text")
try s.find("needle") otherwise -1
s.toLower()
s.toUpper()
s.replace("old", "new")
s.split(",")
s.trim()
```

NO string concatenation. Use interpolation: `"Hello, {name}!"`.

Iteration:
```maxon
for c in s 'chars' ... end 'chars'          // grapheme clusters
for b in s.bytes() 'bytes' ... end 'bytes'  // bytes
for cp in s.codepoints() 'cp' ... end 'cp'  // codepoints
```

---

## Builtin Functions

### Compiler Intrinsics

These are lowered directly to hardware instructions. They accept `float` (or `int`, which is auto-promoted to `float`). All return `float` except `trunc` which returns `int`.

```maxon
// Single-argument
abs(x)       // absolute value
sqrt(x)      // square root
floor(x)     // round toward negative infinity
ceil(x)      // round toward positive infinity
round(x)     // round to nearest (banker's rounding)
trunc(x)     // truncate toward zero, returns int

// Two-argument (second arg is named)
min(a, b: b)   // minimum of two values
max(a, b: b)   // maximum of two values

// Compile-time
sizeof(TypeName)   // size of a type in bytes (compile-time constant)
```

### Standard Library Functions

```maxon
print("hello\n")             // print to stdout
printError("fail\n")         // print to stderr
panic("invariant violated")  // terminate with stack trace (unrecoverable)
sleep(100)                   // sleep current green thread (milliseconds)
```

### Math Library (`Math.*`)

All accept and return `MathValue` (float). Implemented in the standard library.

```maxon
Math.sin(x)                  // sine (radians)
Math.cos(x)                  // cosine (radians)
Math.tan(x)                  // tangent (radians)
Math.atan(z)                 // arc tangent
Math.atan2(y, x: x)         // two-argument arc tangent
Math.exp(x)                  // e^x
Math.log(x)                  // natural logarithm (ln)
Math.log2(x)                 // base-2 logarithm
Math.log10(x)                // base-10 logarithm
Math.pow(base, exponent: e)  // base raised to exponent
```

---

## Operators (precedence high to low)

| Precedence | Operators | Notes |
|------------|-----------|-------|
| Highest | `.` `()` | Member access, function call |
| | `as` | Type cast (widening only) |
| | `-` `not` | Unary negation, NOT |
| | `*` `/` `mod` | Multiplication, division, modulo |
| | `+` `-` | Addition, subtraction |
| | `shl` `shr` | Bit shift (int only) |
| | `==` `!=` `<` `>` `<=` `>=` `is` `is not` | Comparison |
| | `and` | Logical/bitwise AND |
| | `xor` | Logical/bitwise XOR |
| | `or` | Logical/bitwise OR |
| Lowest | `if`...`else` | Conditional (ternary) expression |

Type casting:
```maxon
5 as float     // widening OK
42 as byte     // int literal 0-255 OK
b as int       // byte to int OK
// Float to int — use: trunc(), round(), floor(), ceil()
```

---

## Other Features

### Closures

```maxon
let double = (n Offset) gives n * 2
items.sort((a, b) gives a.priority - b.priority)
let always42 = (_ Offset) gives 42
```

Closures capture by reference.

### Tuples

Tuples are fixed-size, ordered collections of values with potentially different types.

```maxon
// Creating tuples
var t = (10, 20)
var mixed = (42, "hello")
var triple = (1, 2, 39)

// Element access with positional dot syntax
t.0   // 10
t.1   // 20

// Field assignment (tuples are mutable)
t.0 = 30
t.1 = 40
```

#### Tuples as function parameters and return types

```maxon
typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'sum'

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'
```

#### Destructuring declarations

```maxon
var (x, y) = makePair(10, b: 32)   // creates new variables
let (a, b) = (10, 20)              // immutable bindings

// Discard elements with _
var (result, _) = compute()
```

#### Tuple assignment (to existing variables)

```maxon
var x = 0
var y = 0
(x, y) = makePair(10, b: 32)       // assigns to existing variables

// Mixed: existing + new declarations
(x, var z) = makePair(1, b: 2)     // x existing, z newly declared
(x, let w) = makePair(3, b: 4)     // x existing, w immutable

// Discard elements
(x, _) = makePair(42, b: 99)
```

#### Destructuring in for loops

```maxon
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
	print("{key}: {value}\n")
end 'loop'
```

### Async/Await

```maxon
var promise = async someFunction(arg1, arg2)
var result = await promise
var r = try await p otherwise 0    // throwing async
p.cancel()                         // cancellation
```

### Visibility

All declarations are file-private by default. Use `export` for cross-file visibility:

```maxon
export function publicFunc() returns Count ...
export type PublicType ...
export typealias PublicAlias = int(0 to 100)
export enum PublicEnum ...
export union PublicUnion ...
export var sharedState = 0
```

### Conditional Compilation

```maxon
#if os(Windows)
	let sep = "\\"
#else
	let sep = "/"
#endif
```

Conditions: `os(Windows)`, `os(Linux)`, `os(Macos)`, `arch(x64)`, `arch(arm64)`, `testing(true)`, `testing(false)`.
Operators: `and`, `or`, `not`.

### Memory Model

- Primitives: copied by value
- Structs: assigned by reference (alias). Use `.clone()` for independent copy
- Reference counting: automatic scope cleanup
- Borrow checking: CANNOT mutate a collection while a `.get()` borrow is live (E3070)
- `@heap var p = Point{x: 0, y: 0}` forces heap allocation

---

## Compiling and Running Tests

### Two compilers

| | C# compiler | Self-hosted compiler |
|---|---|---|
| Location | `maxon-sharp/` | `maxon-selfhosted/` |
| Executable | `bin/maxon.exe` | `maxon-selfhosted/.maxon/maxon-selfhosted.exe` |
| Commands | build, run, fmt, spec-test, lsp-server | build, spec-test, test-incremental |
| Build | pre-built (NEVER use `dotnet run`) | `maxon.exe build` from `maxon-selfhosted/` |

### Compiling

```bash
maxon.exe build hello.maxon          # single file
maxon.exe build                        # multi-file project (from project dir)
maxon.exe run hello.maxon              # compile and run
maxon.exe build hello.maxon --emit-ir       # emit IR
maxon.exe build hello.maxon --dump-stages   # IR at each stage
```

### Spec tests (C# compiler)

```bash
maxon.exe spec-test                            # all tests
maxon.exe spec-test --filter=arithmetic        # filter
maxon.exe spec-test --filter=arrays --verbose  # verbose failures
maxon.exe spec-test --update-required          # regenerate RequiredIR
maxon.exe spec-test --target=x64-linux      # cross-compile
```

### Spec tests (self-hosted compiler)

```bash
cd maxon-selfhosted
./maxon-selfhosted.exe spec-test                           # all tests
./maxon-selfhosted.exe spec-test --filter=arithmetic       # filter
./maxon-selfhosted.exe spec-test --verbose                 # verbose failures
./maxon-selfhosted.exe spec-test --target=x64-linux     # cross-compile
```

### Debugging

```bash
maxon.exe build foo.maxon --log=trace              # all logging
maxon.exe build foo.maxon --log=parser:debug       # category-specific
maxon.exe build foo.maxon --log=codegen:trace
maxon.exe build foo.maxon --mm-trace               # memory manager trace
maxon.exe build foo.maxon --mm-debug               # memory debug checks
```
