---
title: Writing Maxon Code
description: "The rules and idioms for writing correct Maxon: syntax that does not exist, mandatory rules, and core constructs."
sidebar:
  order: 1
---

ALWAYS read this document before writing or modifying Maxon code.
For the full specification see [LANGUAGE_REFERENCE.md](/docs/language/overview/) and [BNF_SYNTAX.md](/docs/spec/bnf-syntax/);
for the standard library's API see [STDLIB_REFERENCE.md](/docs/stdlib/).

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
a / b   (b may be zero)        try (a / b) otherwise ...   (`/` `mod` throw DivisionByZero)
;                              (no semicolons — newline-delimited)
func(a, b, c)                  func(a, b: b, c: c)
param int                      param SomeTypealias
returns int                    returns SomeTypealias
cond ? a : b                   a if cond else b
(x) gives x + 1                function(x) gives x + 1
param (T) returns U            typealias F = function(T) returns U; param F
import / include               (does not exist — a project compiles every .maxon under its directory)
defer / lazy                   (do not exist)
```

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

This applies to: `if`, `else`, `while`, `for`, `match`, `try` and `try...otherwise` blocks, `function`, `type`, `enum`, `union`, `interface`, `extension`, `test`.

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

### 3. NEVER use bare `int` or `float` as types

All numeric types in type positions (parameters, return types, fields) MUST use a typealias with range
constraints (E3005). A byte is an `int` alias whose range fits in 8 bits.

```maxon
// WRONG
function add(a int, b int) returns int

// CORRECT
typealias Integer = int(i64.min to i64.max)
function add(a Integer, b Integer) returns Integer
```

Use stdlib aliases when appropriate: `ExitCode`, `HashValue`, `Codepoint`, `Byte`, `ByteArray`,
`FileSize`, `Timestamp`, `NetworkPort`, `Real` — that is the whole list, so an example that uses
`Integer` declares it in the same file. For per-domain quantities (counts, indices, byte offsets, math values), declare a typealias local to your file with a name that describes the *purpose* (`Tally`, `BytePos`, `Coord`) rather than reusing a generic `Count`/`Index`.

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

The first argument NEVER carries a label — labeling it is rejected as **E2052 "first arg cannot be named"**. Every argument after the first MUST use `name: value`.

```maxon
// WRONG — second arg missing label
connect("localhost", 8080, 5000)

// WRONG — first arg cannot be named (E2052)
connect(host: "localhost", port: 8080, timeout: 5000)

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

The exceptions are a block-form `try 'label' … end 'label'`, whose body sends every throwing call to its
handler, and a `test` body, where a throwing call needs no `try` and an error fails the test (see
[Tests](#tests)).

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

⛔ **Write no comments while you write the code.** Code changes shape while a change is being written,
so a comment written before the code settles is written and rewritten several times and most of that
prose never survives to the commit. Only the final shape of the code is worth commenting, and it does
not exist until the change is finished. **Every comment is added at the end, in one pass, by
a dedicated documentation step.**

⚠ **Writing them last is not a licence to write more.** Comments are **concise and minimal**: the
default is no comment, and one earns its place only where the code cannot carry the point by itself.
Comment the **why** — the constraint, the invariant, the reason a bound or an order is the correct one
— never the **how**, which is the code. **Describe the code as it is now:** no "used to", no "changed
from", no reference to a previous name or shape; git holds the history. **A comment you edit gets
rewritten to conform**, not patched. See the Comments entry of the Code Quality checklist in
the compiler repository's own style guide.

### 16. Blocks MUST NOT be empty (E3082)

Every `if`, `else`, `while`, `for`, and `try...otherwise` block must contain at least one statement. Comment-only blocks are also empty since comments are not statements.

```maxon
// WRONG — empty block
if x > 0 'check'
end 'check'

// WRONG — comment-only block is still empty
if x > 0 'check'
	// do something later
end 'check'

// CORRECT
if x > 0 'check'
	print("positive\n")
end 'check'
```

### 17. Every struct field MUST be initialized (E3086)

A struct literal must supply a value for every field, unless the field:
1. has a default on its declaration — two forms:
   - shorthand: `var count = 0` (literal only: int/float/bool/enum case), OR
   - full form: `var items as IntArray = IntArray.create()` (type annotation + arbitrary expression, re-evaluated at every literal that omits the field)
2. is assigned via `self.field = expr` on every control-flow path of a
   `static` factory whose return type is the enclosing type, and the literal
   is the direct `return` expression.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

// WRONG — missing field
type P
	export var x as Integer
	export var y as Integer

	static function create(x Integer) returns Self
		return Self{x: x}       // E3086: 'y' not initialized
	end 'create'
end 'P'

// CORRECT — declaration default (shorthand)
type Counter
	export var value = 0

	export static function create() returns Self
		return Self{}           // OK — value defaults to 0
	end 'create'
end 'Counter'

// CORRECT — declaration default (full form with expression)
type Bag
	export var items as IntArray = IntArray.create()

	export static function create() returns Self
		return Self{}           // OK — items gets a fresh empty array per construction
	end 'create'
end 'Bag'

// CORRECT — self-assignment in static factory
type Thing
	export var value as Integer

	static function make(v Integer) returns Self
		self.value = v          // proof of initialization
		return Self{}           // OK: value deferred to self-assign
	end 'make'
end 'Thing'
```

Single-branch or loop-only `self.field` writes are NOT definite assignment and also trigger E3086.

### 18. Struct fields take `as`; parameters and returns do not

```maxon
// WRONG (E2010)
export var x Coord

// CORRECT
export var x as Coord
function f(x Coord) returns Coord
```

### 19. Nothing may be declared and left unused

- A variable **or parameter** that is never read is **E3012**. Bind a value you do not need to `_`.
- A typealias never named in a type position of its own file is **E3062**, unless it is visible outside the
  file (`module`, `export` or `public`). A bare `[...]` literal inferring the type is not a use.
- A `var` that is neither reassigned nor mutated (`push`, `append`, a field write…) is **E3077** — use `let`.
- Assigning a value to itself (`x = x`, `p.x = p.x`) is **E3067**.
- A pure function's result must be used, and an impure one's used or discarded with `_ =` — see
  [Purity and discarded results](#purity-and-discarded-results).

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

// Default parameters (not on an overloaded name whose declarations differ — E2015):
function connect(host String, port Port = 8080) returns Connection
	// ...
end 'connect'

// Static method — a struct literal is legal only inside its own type:
type MyType
	export var field as FieldValue

	export static function create() returns Self
		return Self{field: 0}
	end 'create'
end 'MyType'
```

### Variables

```maxon
let x = 42          // immutable
var y = 10          // mutable
_ = sideEffect()     // discard (RHS MUST be a function call)
```

Use `var` for any variable you call mutating methods on (`push`, `set`, `remove`, `clear`, `append`, etc.):
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
var items = IntArray.create()   // var because we call push
items.push(1)
```
`Array with Integer{}` at a use site is E2004 — a generic container needs a typealias before use.

### Struct types

```maxon
typealias Coord = float(f64.min to f64.max)
typealias VisitCount = int(0 to u64.max)

type Point
	export var x as Coord           // readable and writable outside the type
	export var y as Coord
	var visits as VisitCount = 0    // private to the type, declaration default

	static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'

	function magnitude() returns Coord
		return sqrt(self.x * self.x + self.y * self.y)
	end 'magnitude'

	function magnitudeSquared() returns Coord
		return magnitude() * magnitude()   // sibling call — no explicit receiver needed
	end 'magnitudeSquared'

	function visit() returns VisitCount
		self.visits = self.visits + 1
		return self.visits
	end 'visit'

	static function origin() returns Point
		return Point{x: 0.0, y: 0.0}
	end 'origin'
end 'Point'

var p = Point.create(1.5, y: 2.5)
p.x = 3.0
let o = Point.origin()
```

Instance methods can call sibling instance methods by bare name — the compiler implicitly prepends `self` as the receiver.

**Member visibility differs for fields and methods.** An unmarked field is private to the type — `p.visits`
outside `Point` is **E3014**. An unmarked method or static member is private to the *file*, like a top-level
declaration, so `main` above may call `Point.create` — but another file may not (**E3008**). Mark a member
`export` (or `public`) when another file uses it; an `export` nothing outside its file uses is **E3092**, and an
exported signature may not name a less visible typealias (**E3167**) — see [Visibility](#visibility).

**A struct literal is legal only inside the type's own methods (E3076).** `Point{x: 1.5, y: 2.5}` written
anywhere else is an error, however public the fields are — external callers go through a static factory, so
invariants have a single construction point. The same restriction covers ranged typealiases: `Port{8080}` at
a call site is E3076; write the bare literal at a `Port`-typed destination, or `8080 as Port`.

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
typealias Integer = int(i64.min to i64.max)

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
	function get(index ContainerIndex) returns Element throws ArrayError
end 'Container'
```

Interface types can be used directly as function parameter types. The compiler monomorphizes the function for each concrete type:

```maxon
function render(item Describable) returns String
	return item.describe()
end 'render'
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
export typealias ScoreMap = Map with (String, Score)
```

### Ranged type construction

```maxon
typealias Port = int(0 to 65535)
let p = 8080 as Port        // cast a value into the ranged type
let bad = 70000 as Port     // E3005: the literal is outside the range
connect(host, port: 443)    // a bare literal takes the parameter's declared type
```

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
for (iter, item) in array.withIterator() 'e' ... end 'e'  // iter.index() gives position
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

Labeling `break` / `continue` with the innermost enclosing loop's own
label is redundant and rejected as E2048 — use unlabeled `break` /
`continue` for that case. A label is only meaningful when targeting an
outer loop (or, for `break`, jumping out across an intervening `match`).

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

Block-opening statements (`if`, `while`, `for`, nested `match`, multi-line `try ... end` /
`try ... otherwise 'label' ... end`) are rejected in match arms (E2049). Single-statement
`try` is fine: `try call()`, `try call() otherwise panic("...")`, `try call() otherwise ignore`,
`try call() otherwise return/break/continue/throw ...`, and `try call() otherwise <expr>` when the call
returns a value.

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

## Error Handling

```maxon
// Define error type
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'

// Throwing function with no result
function remove(path FilePath) throws FileError
	// ...
end 'remove'

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

// A call that returns nothing has no value to fall back to: `try remove(path) otherwise print("…")`
// is E3059. Use `otherwise ignore`, a handler block, or `otherwise return/throw/break/continue`.

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

// try block — multi-call form. Inside, bare throwing calls don't need `try`; all
// errors route to the shared `otherwise` handler. The handler body MUST match on
// the binding. `e` is either the single thrown enum type or a synthesized
// error-union when multiple enums are thrown.
try 'work'
	let raw = readFile("config.json")
	let parsed = parseJson(raw)
end 'work'
otherwise (e) 'h'
	match e 'k'
		FileError.notFound then print("missing\n")
		ParseError.syntax then print("bad json\n")
	end 'k'
end 'h'

// Panic (unrecoverable)
panic("invariant violated: {details}")
```

## Collections

A container type needs a typealias before use (`Array with Integer{}` at a use site is **E2004**). Every
method that can fail THROWS — `get`, `set`, `pop`, `remove` on an Array, `Map.insert`, `List.insert`,
`List.first` — so it needs `try` (E3057). `push`, `Array.insert`, `upsert`, `clear`, `reserve`, `resize`,
`prepend` and `append` do not.

### Arrays

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var arr = [1, 2, 3]
var empty = IntArray.create()

arr.push(42)                              // append
let n = arr.count()                       // length
let val = try arr.get(0) otherwise 0      // access (ALWAYS use try)
try arr.set(0, value: 100) otherwise ignore  // modify (throws)
arr.reserve(100)                          // pre-allocate
arr.resize(50)                            // set length
let last = try arr.pop() otherwise 0      // remove last (throws)
arr.insert(0, value: 99)                  // insert at index
let gone = try arr.remove(0) otherwise 0  // remove at index (throws)
arr.clear()                               // remove all
arr.sort()                                // in-place stable sort (Element is Comparable)
arr.sortUnstable()                        // in-place unstable sort (Element is Comparable)
arr.sort(cmp)                             // sort with comparator: function(Element, Element) returns Ordering
arr.sortUnstable(cmp)                     // unstable sort with comparator
```

### Maps

```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringIntMap = Map with (String, Integer)

var m = ["hello": 42]
var empty = StringIntMap.create()

let val = try m.get("hello") otherwise 0  // access (ALWAYS use try)
m.upsert("world", value: 99)              // insert or replace
try m.insert("fresh", value: 7) otherwise ignore   // insert; throws if the key exists
let has = m.contains("hello")             // membership
let dropped = m.remove("hello")           // returns bool; does not throw
let n = m.count()
```

### Lists

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

var list = IntList.create()
list.prepend(1)                                  // O(1) at either end
list.append(2)
let head = try list.first() otherwise 0          // throws
let popped = try list.removeFirst() otherwise 0  // throws
```

### Prefer iterators over indexed access

Walk a collection with `for`-`in`, not an index loop over `.get()`. Indexed access needs a `try` on every
step, conflicts with the borrow checker (E3070) when the collection is mutated, and is slower.

```maxon
// AVOID — indexed loop with a per-step try
var i = 0
while i < arr.count() 'loop'
	let v = try arr.get(i) otherwise 0
	process(v)
	i = i + 1
end 'loop'

// PREFER — for-in
for v in arr 'loop'
	process(v)
end 'loop'

// PREFER — .withIterator() when you need the position
for (iter, v) in arr.withIterator() 'loop'
	print("{iter.index()}: {v}\n")
end 'loop'

// An explicit iterator, when you steer it yourself
var it = try arr.createIterator() otherwise return 0   // throws on an empty collection
while true 'walk'
	process(it.current())
	try it.advance() otherwise break                   // throws past the last element
end 'walk'
```

An iterator always points at an element: `createIterator()` throws `IterationError.exhausted` on an empty
collection, `current()` always answers, and `advance()` throws on the last element. `.withIterator()`
works on every iterable (Array, String, Map, Set, List) without allocating, and its iterator also offers
`index()`, `advanceBy(n)`, `retreat()` and `retreatBy(n)`. Reach for `.get(i)` only when access is genuinely
random — a binary search, a lookup table.

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

All accept and return `Math.Real` (full-range float). Implemented in the standard library.

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

## Operators (precedence high to low)

| Precedence | Operators | Notes |
|------------|-----------|-------|
| Highest | `.` `()` | Member access, function call |
| | `-` `not` | Unary negation, NOT |
| | `as` | Type cast — `-x as Small` is `(-x) as Small` |
| | `*` `/` `mod` | Multiplication, division, modulo |
| | `+` `-` | Addition, subtraction |
| | `shl` `shr` | Bit shift (int only) |
| | `==` `!=` `<` `>` `<=` `>=` `is` `is not` | Comparison |
| | `and` | Logical/bitwise AND |
| | `xor` | Logical/bitwise XOR |
| | `or` | Logical/bitwise OR |
| | `to` `upto` | Range |
| Lowest | `if`...`else` | Conditional (ternary) expression |

`and` and `or` short-circuit on `bool` operands (the right-hand side is skipped when the left already determines the result). On `int` operands they are bitwise and always evaluate both sides.

Type casting:
```maxon
typealias Real = float(f64.min to f64.max)
typealias Tally = int(0 to i64.max)
typealias Octet = int(0 to u8.max)

5 as Real            // integer to a ranged float typealias
42 as Octet          // a literal is range-checked at compile time (256 as Octet is E3005)
o as Tally           // widening: no run-time check
t as Octet           // narrowing: checked at run time, panics when out of range
true as bool         // bool stays bare; no range to declare
// Float to int — use: trunc(), round(), floor(), ceil() (a cast is E3009)
```

A bare `int` or `float` cast target is rejected (**E3005**) — every primitive cast travels through a named
ranged typealias. `bool` is unranged and stays bare.

A cast to the value's own alias (`x as Integer` when `x` is already an `Integer`) is **E3010
"unneeded cast"**: it converts nothing.

## Other Features

### Closures

Closure literals start with the `function` keyword:

```maxon
typealias Integer = int(i64.min to i64.max)

let double = function(n Integer) gives n * 2
items.sort(function(a, b) gives a.priority.compare(b.priority))
let always42 = function(_ Integer) gives 42
```

Closures capture by reference. A closure passed where the parameter is declared with a function type (like
`sort`'s comparator) may omit its parameter types; everywhere else they are written.

### Function Types

Function types are written `function(ParamType, ...) returns ReturnType` (the
`returns` clause is omitted for a void-returning function type). The literal
`function(...)` form is legal only as the right-hand side of a `typealias` —
everywhere else (parameter types, return types, struct fields, generic
arguments) you reference the alias by name:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'
```

A function value is a bare free-function name or a closure literal. A static method in value position,
`apply(Ops.twice, x: 3)`, is a parse error (**E2010**); pass a closure that calls it —
`apply(function(n) gives Ops.twice(n), x: 3)` — or a free function that does.

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

All declarations are file-private by default. Four tiers exist:

- **default** — visible only within the declaring file.
- **`module`** — visible to every file in the same directory and any subdirectory of that directory.
- **`export`** — visible everywhere in the compilation.
- **`public`** — visible everywhere, and declared API surface, so it is exempt from the unused-export checks.

A signature may not name a type less visible than the function itself; for that rule the tiers are ordered
default < `module` < `export` < `public`, and naming a narrower type is E3167.

At most one modifier may be written. `module` is a contextual keyword: it is recognised only immediately before a declaration token (`function`, `type`, `enum`, `var`, `let`, etc.), so user code can still use `module` as a parameter or local variable name.

```maxon
export function publicFunc() returns Tally ...
module function packageFunc() returns Tally ...   // visible to this directory subtree
export type PublicType ...
export typealias PublicAlias = int(0 to 100)
export enum PublicEnum ...
export union PublicUnion ...
export var sharedState = 0
module var featureState = 0                       // visible to this directory subtree
```

### Conditional Compilation

```maxon
#if os(Windows)
	let sep = "\\"
#else
	let sep = "/"
#endif
```

Conditions: `os(Windows)`, `os(Linux)`, `os(Macos)`, `os(Wasi)`, `arch(x64)`, `arch(arm64)`, `arch(wasm32)`,
and `debugstream(true|false)`, which follows the `--debugstream` flag. `testing(...)`, `rcSanitize(...)`
and `leakReport(...)` name flags that are always off, so `testing(false)` holds in every build, `maxon test`
included. An unknown condition is **E2064**.
Operators: `and`, `or`, `not`, plus parentheses for grouping.

### Memory Model

- Primitives: copied by value
- Structs: assigned by reference (alias). Use `.clone()` for independent copy
- Reference counting: automatic scope cleanup
- Borrow checking: CANNOT mutate a collection while a `.get()` borrow is live (E3070)
- `@heap let p = Point.create(0.0, y: 0.0)` forces heap allocation
- A record is written through mutable holders or read through immutable names, never both at once.
  `var b = a`, where the `let` `a` is its record's only reference, **moves** it: reading `a` afterwards is
  **E3102**. Where another live `let` still names the record, `var b = a` is **E3078** — use `let` or
  `.clone()`. Writing a field of a record a live `let` may still read is **E3159**.
- A parameter the function assigns to is passed by reference; passing a `let` to it is **E3019**.

### Purity and discarded results

A function's result must be used. The compiler infers whether a function is pure:

| Callee | Bare call statement | `_ = call()` |
|--------|---------------------|--------------|
| pure function (no output, no writes to globals or parameters) | **E3064** | **E3064** |
| impure function | **E3065** | allowed |
| chainable method (returns its own receiver type) | allowed | allowed |

### Files and paths

```maxon
let fp = FilePath from "data.txt"
try File.writeText(fp, content: "hello") otherwise return 1
let content = try File.readText(fp) otherwise ""
let there = File.exists(fp)
let info = try File.info(fp) otherwise return 1   // info.size, info.modifiedTime, info.isDirectory, …
```

### Tests

Tests live in `*.maxtest` files beside the code they test; a `test` anywhere else is **E2058**. `maxon test`
compiles them with the rest of the project, so the project needs no `main`.

```maxon
test 'a quarter off'
	Expect.equal(discounted(400, percent: 25) as AssertedInt, expected: 300)
end 'a quarter off'

test 'too large a discount is caught'
	let price = try discounted(400, percent: 150) otherwise 0
	Expect.equal(price as AssertedInt, expected: 0)
end 'too large a discount is caught'
```

- The name is a quoted phrase, repeated exactly by `end` (E2008).
- A test body is an implied `try`: a throwing call, a throwing interface call or an `await` needs no `try`,
  and any error fails the test. A helper function a test calls, or a closure written in one, still needs
  `try` (E3057).
- Integer assertions take `AssertedInt` (`int(i64.min to i64.max)`) and float ones `AssertedReal`; a value
  of an alias with another range needs a cast (`x as AssertedInt`), or the call is E3005.
- The matchers, with their labels: `equal(actual, expected:)`, `notEqual(actual, expected:)`,
  `greaterThan` / `lessThan` / `atLeast` / `atMost(actual, than:)`, `close(actual, expected:, within:)`,
  `isTrue(actual)`, `isFalse(actual)`, `contains` / `startsWith` / `endsWith(haystack, needle:)`,
  `isEmpty(haystack)`, `fail(message)`. Each takes an optional `message:`.

The full rules are in the language reference's [Testing](/docs/language/testing/) section.

### Style

- Blank lines around control flow and between logical sections.
- `let` by default; `var` only where the value is reassigned or mutated.
- `panic` / `default panic` for a branch that is a programming error; `throw` / `default throws` for a
  recoverable one.
