---
title: Writing Maxon Code
description: "The rules and idioms for writing correct Maxon: syntax that does not exist, mandatory rules, and core constructs."
sidebar:
  order: 1
---

ALWAYS read this document, `WRITING_MAXON_CODE.md`, before writing or modifying Maxon code. An install
holds it in `docs/`, beside `stdlib/`, with the documents it links.
For the full specification see [LANGUAGE_REFERENCE.md](/docs/language/overview/) and [BNF_SYNTAX.md](/docs/spec/bnf-syntax/);
for the standard library's API see [STDLIB_REFERENCE.md](/docs/stdlib/).

When a build or test run reports breaking changes since the project was last built, call the MCP
`breaking_changes` tool and apply each change's fix. See
[Breaking changes since the last build](/docs/cli/#breaking-changes-since-the-last-build).

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
;                              (no semicolons — newline-delimited; a line break inside ( [ { ends nothing)
func(a, b, c)                  func(a, b: b, c: c)
param int                      param SomeTypealias
returns int                    returns SomeTypealias
cond ? a : b                   a if cond else b
(x) gives x + 1                function(x) gives x + 1
param (T) returns U            typealias F = function(T) returns U; param F
import / include               (does not exist — a project compiles every .maxon under its directory)
defer / lazy                   (do not exist)
module var / static var        (do not exist — a local `var` passed to a `mutable` parameter, a field, a `SharedValue`, or a service's fields)
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

Use stdlib aliases when appropriate: `ExitCode`, `HashValue`, `Codepoint`, `Byte`, `ByteArray`, `Count`,
`FileSize`, `Timestamp`, `NetworkPort`, `FullRangeFloat` — that is the whole list, so an example that uses
`Integer` declares it in the same file. `Count` is what a collection's `count()` returns. For per-domain quantities (tallies, indices, byte offsets, math values), declare a typealias local to your file with a name that describes the *purpose* (`Tally`, `BytePos`, `Coord`).

An alias of your own that shares a stdlib alias's name is a second type, and every value crossing between the two needs a cast: use the stdlib alias where it fits, and otherwise give yours a different name. Every typealias declaration is its own type, so two files declaring one name over one range declare two types too.

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

The first argument carries a label only when its parameter has a default — labeling a required first parameter is rejected as **E2052 "the first argument cannot be named because its parameter has no default"**. Every argument after the first MUST use `name: value`.

```maxon
// WRONG — second arg missing label
connect("localhost", 8080, 5000)

// WRONG — first arg cannot be named (E2052)
connect(host: "localhost", port: 8080, timeout: 5000)

// CORRECT
connect("localhost", port: 8080, timeout: 5000)
```

Against `function open(path String = "config.toml", mode Mode = Mode.read)`, `open(mode: Mode.write)`
names the first argument and is legal, because `path` has a default.

A [builtin](#builtin-functions) such as `min` or `max` is the exception: it has no declaration to name a
parameter, so all of its arguments are positional and naming one is **E2067**.

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
1. has a default on its declaration, re-evaluated at every literal that omits the field. The default
   gives the field its type, so it takes one of three shapes:
   - a `bool` or string literal: `var enabled = true`, `var name = "anon"`;
   - `Type.member` or `Type.member(...)`, typed `Type`: `var items = IntArray.create(0)`, `var level = Priority.low`;
   - an expression cast with `as`, typed by the cast: `var count = 0 as Tally`.

   A bare number default (`var count = 0`) is E2004 — cast it. `var count as Tally = 0` is E2010.
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

// CORRECT — declaration default (a cast gives the type)
type Counter
	export var value = 0 as Integer

	export static function create() returns Self
		return Self{}           // OK — value defaults to 0
	end 'create'
end 'Counter'

// CORRECT — declaration default (a factory call gives the type)
type Bag
	export var items = IntArray.create(0)

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
- A file-private top-level `function`, `let`, `type`, `enum`, `union` or `interface` that nothing live in its
  file names is **E3219**: delete it. Declarations that only name each other, or a function that only calls
  itself, are dead together.
- A `var` that is neither reassigned nor mutated (`push`, `append`, a field write…) is **E3077** — use `let`,
  whatever the `var` was bound from.
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
	// ... throws FileError on some path
end 'load'

// Void (no returns clause):
function printStatus()
	print("OK\n")
end 'printStatus'

// Default parameters:
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

**`let` means the name is never reassigned; `var` means it may be.** Writing through a `let` is legal: a field
store, `push` and a callee that writes its parameter reach the record a `let` names. A number, `bool`, enum or
`String` has nothing behind its name, so changing one is reassigning it, and it needs a `var`. `view` is
another spelling of `let`.

```maxon
let x = 42          // never reassigned (E2013: 'x' is a let and cannot be reassigned)
var y = 10 as Count // may be reassigned: a var names its alias (E2004 without the cast)
_ = sideEffect()     // discard (RHS MUST be a function call)
```

A `let` of a bare number, literal arithmetic or a literal container is a constant that fits any alias its
value fits, checked at each use. Cast the literal of a `let` (`let limit = 10 as Count`) only where some use
needs the alias: when every use would take the plain constant the same way, the cast is **E3010**. A `var`
— a local — starts from a value of an alias: cast the number (`var total = 0 as Count`, `var size = (16 * 1024) as ByteCount`) or give an array literal a
typed head (`TallyArray from [1, 2]`). A `for` counter takes the alias of its non-literal bound
(`for i in 0 upto xs.count()` counts in `Count`) and is a `let` in the body.

A variable you call mutating methods on (`push`, `set`, `remove`, `clear`, `append`, etc.) can be a `let`,
because the call writes the array and not the name; use `var` for a name that is reassigned:
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
let items = IntArray.create(0)   // a let: push writes the array, not the name
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
	var visits = 0 as VisitCount    // private to the type, declaration default

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
exported signature or field may not name a less visible type (**E3167**) — see [Visibility](#visibility).

**A struct literal is legal only inside the type's own methods (E3076).** `Point{x: 1.5, y: 2.5}` written
anywhere else is an error, however public the fields are — external callers go through a static factory, so
invariants have a single construction point. The same restriction covers ranged typealiases: `Port{8080}` at
a call site is E3076; write the bare literal at a `Port`-typed destination (a cast there is **E3010**), and
`8080 as Port` where nothing declares the type, such as an unannotated `var` or a `let` some use of which
needs the alias.

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

A requirement is read-only: a conformer whose method writes the receiver needs the requirement written
`function grow(mutable self, by Tally)` (**E3210** otherwise); the conformer's own method needs no marker on
its receiver. The standard library's `Iterator.advance`, `BidirectionalIterator.retreat` and
`LogHandler.handle` are written that way. `HttpHandler.handle` takes `exchange mutable HttpExchange`, and a
conformer spells the same marker on its parameter.

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
let p = 8080 as Port        // a Port; E3010 when every use of p would take the bare 8080
let bad = 70000 as Port     // E3005: the literal is outside the range
connect(host, port: 443)    // a bare literal takes the parameter's declared type
// connect(host, port: 443 as Port)   // E3010: the parameter already declares 'Port'
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

Labeling `break` / `continue` with the label of the construct it already
leaves — the innermost enclosing loop, or for `break` the `match` it sits
in — is redundant and rejected as E2048. Use unlabeled `break` /
`continue` for that case; a label reaches an outer loop (for `break`,
across an intervening `match`).

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

// Throwing function with no result. A `throws` clause the body can never throw through is E3168.
function remove(path FilePath) throws FileError
	if not path.fileExists() 'missing'
		throw FileError.notFound
	end 'missing'
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
`prepend` and `append` do not, and neither does a `FixedArray` `get`/`set` whose index is proven inside the
size — a `try` there is E3055 (see
[Fixed Arrays and Proven Indices](/docs/language/expressions/#fixed-arrays-and-proven-indices)).

### Arrays

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var arr = [1, 2, 3]
var empty = IntArray.create(0)

arr.push(42)                              // append
let n = arr.count()                       // length
let val = try arr.get(0) otherwise 0      // access (ALWAYS use try)
try arr.set(0, value: 100) otherwise ignore  // modify (throws)
var sized = IntArray.create(50)           // 50 zero elements, one allocation up to 256 bytes
var roomy = IntArray.create(100, zeroed: false)  // empty, room for 100, one allocation up to 256 bytes
arr.reserve(100)                          // pre-allocate an existing array
arr.resize(50)                            // set length
let last = try arr.pop() otherwise 0      // remove last (throws)
arr.insert(0, value: 99)                  // insert at index
let gone = try arr.remove(0) otherwise 0  // remove at index (throws)
arr.clear()                               // remove all
arr.sort()                                // in-place stable sort (Element is Comparable)
arr.sortUnstable()                        // in-place unstable sort (Element is Comparable)
arr.sortBy(cmp)                           // sort with comparator: function(Element, Element) returns Ordering
arr.sortUnstableBy(cmp)                   // unstable sort with comparator
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
step and is slower.

```maxon
// AVOID — indexed loop with a per-step try
var i = 0 as ElementIndex
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

// Two-argument (both positional: naming a builtin's argument is E2067)
min(a, b)      // minimum of two values
max(a, b)      // maximum of two values

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

All accept and return `FullRangeFloat` (full-range float). Implemented in the standard library.

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
typealias Tally = int(0 to u64.max)
typealias Octet = int(0 to u8.max)

5 as Real            // integer to a ranged float typealias
42 as Octet          // a literal is range-checked at compile time (256 as Octet is E3005)
o as Tally           // widening: no run-time check
t as Octet           // narrowing: checked at run time, panics when out of range
// Float to int — use: trunc(), round(), floor(), ceil() (a cast is E3009)
```

A bare `int` or `float` cast target is rejected (**E3005**) — every primitive cast travels through a named
ranged typealias. `bool` is unranged and stays bare.

A cast to the type the value already has (`x as Integer` when `x` is already an `Integer`, `flag as bool`,
a struct, enum, union or `String` to itself) is **E3010 "unneeded cast"**: it converts nothing. So is a literal cast to the alias its destination already declares —
a call argument, a `return`, a struct-literal field or a field store (`open(8080 as Port)` for a `Port`
parameter, `return 0 as ExitCode` from `main`): write the bare literal there. So is a literal cast in an
unannotated `let` every use of which takes the plain constant the same way (`let limit = 10 as Count`, read
only where a `Count` is declared): write `let limit = 10`. A `public`, `export` or `module` `let` keeps its
cast, and so does one a closure captures.

A cast whose operand is a cast (`x as A as B`, `(x as A) as B`) is **E2079**: cast directly to `B`. When
both steps do work — a range check, then a reinterpretation as `bits(n)` — bind the first:
`let a = x as A`, then `a as B`.

## Other Features

### Closures

Closure literals start with the `function` keyword:

```maxon
typealias Integer = int(i64.min to i64.max)

let double = function(n Integer) gives n * 2
items.sortBy(function(a, b) gives a.priority.compare(b.priority))
let always42 = function(_ Integer) gives 42
```

A closure owns what it captures: a scalar is copied, a local holding a record or a `String` is moved into
the closure (reading it afterwards is E3102), and a parameter, `self` or a field is shared. A closure passed where the parameter is declared with a function type (like
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

A function value is a bare free-function name or a closure literal. A method in value position, static
or instance, `apply(Ops.twice, x: 3)`, is **E2078**; pass a closure that calls it —
`apply(function(n) gives Ops.twice(n), x: 3)` — or a free function that does.

A function type and an interface requirement are read-only and never wait or yield. Mark what the callee
does: `function(mutable Counter)` for a parameter it writes, `mutable self` on a requirement whose conformers
write the receiver, and a trailing `mutates captures` for a closure that writes what it captured
(`typealias Tallier = function(Tally) returns Tally mutates captures`). A function value or conformer that
does more is **E3210**.

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
var x = 0 as Integer
var y = 0 as Integer
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

Every promise held in a local is awaited, cancelled, stored or returned on every path out of its scope; a
dropped, overwritten or `_ =`-discarded one is **E3213**. An `async` call given your mutable data (a record, an
array, a variable at a `mutable` parameter) makes a tied promise: await it or `.cancel()` it before every
`return`, `throw` and propagating `try`, and never return, store or pass it on (**E3212**); pass a `.clone()`
to untie the call. A variable handed to a `mutable` parameter of an `async` callee is lent to the coroutine
until the promise is awaited or cancelled, and reading it before then is **E3102**.

### Visibility

All declarations are file-private by default. Four tiers exist:

- **default** — visible only within the declaring file.
- **`module`** — visible to every file in the same directory and any subdirectory of that directory.
- **`export`** — visible everywhere in the compilation.
- **`public`** — visible everywhere, and declared API surface, so it is exempt from the unused-export checks.

No surface a declaration exposes may name a type less visible than the declaration: a function's signature
and `where` clause, a top-level or `static` `let`'s type, a marked field's type, a union case's
payloads, a typealias's target, an `implements`, `where` or `extends` clause, and an enum's backing record.
For that rule the tiers are ordered default < `module` < `export` < `public`, and naming a narrower type is
E3167.

A top-level name is declared once per directory, whatever its visibility and kind: a `type`, `enum`,
`union`, `interface`, `typealias`, top-level `let`, and `function` share one namespace, and a second
declaration in the directory is **E3006**. Two directories may each declare the name; a bare name means the
reader's own directory's declaration, else the root's, else the one visible declaration, and
**E3063**/**E3095** ask for `dir.name` where several are equally near. A function name is declared once per
directory and a method name once per type: Maxon has no overloading.

At most one modifier may be written. `module` is a contextual keyword: it is recognised only immediately before a declaration token (`function`, `type`, `enum`, `let`, etc.), so user code can still use `module` as a parameter or local variable name.

```maxon
export function publicFunc() returns Tally ...
module function packageFunc() returns Tally ...   // visible to this directory subtree
export type PublicType ...
export typealias PublicAlias = int(0 to 100)
export enum PublicEnum ...
export union PublicUnion ...
export let sharedLimit = 0
module let featureLimit = 0                       // visible to this directory subtree
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
- Structs: shared by reference when stored or passed. Use `.clone()` for independent copy
- Reference counting: automatic scope cleanup. A read that could outlive its owner is counted, across the rest
  of its statement too (`x.a.b = x.dropA()`, `x.items.push(x.reset())`), and a `for` loop holds its source, so
  mutating a collection while an element read from it is in use is safe
- `@heap let p = Point.create(0.0, y: 0.0)` forces heap allocation
- **`let` means the name is never reassigned; `var` means it may be.** A `let` binding, `let` field and `let`
  payload can be written through (a field store, `push`, a callee that writes its parameter), and `view` is
  another spelling of `let`. Reassigning one is **E2013** (`'x' is a let and cannot be reassigned`, field form
  `'f' is a let field and cannot be reassigned`), and so is passing a `let` to a `mutable` parameter (`bind it
  to a var`).
- Data nothing may write is separate from `let`: a constant (a `let` bound to a literal, a `b"…"` literal, a
  constant record or array literal), a `freeze` result, a module-level `let` built by a call, a `SharedValue` or
  registry read, a stored `Map` key or `Set` element, and a `readonly` element. A write through constant or
  frozen data is **E3207**, through the others **E3159**; binding the others to a `var` or storing them where
  something may write it is **E3078**.
- Constant data is `immutable`: a holder says so. A field, array element or union payload that holds a
  `b"…"` literal or a constant `let` is declared `immutable ByteArray` (**E3196** names it otherwise); a result
  that returns one is declared `immutable` (**E3209**); a `Map` key or `Set` element is an `immutable` slot, so a
  key built at run time is `freeze`d where it is inserted. A mutable byte array comes from `ByteArray.create`
  and `push`, or `"text".toByteArray()`; a `var` bound to a literal and then written is E3207.
- `var b = a` and `let b = a` from a `var` `a` **move** it: reading `a` afterwards is **E3102**. From a `let` `a`,
  `b` names the same record and `a` stays usable; `a.clone()` is the independent copy.
- A parameter is a `let`: reassigning it is **E2013**. A value-type parameter the function reassigns is marked
  `mutable` and is passed by reference; passing a `let` to it is **E2013** (bind the value to a `var`), and
  passing other read-only data, a literal or an expression is **E3159**. The variable's declared type must be
  the parameter's type, alias included (**E3005**).
  Reading a local in a statement that also hands it to a `mutable` parameter the callee writes, with the read
  running before the write, is **E3186**: copy it into a `let` in an earlier statement. `mutable` before a
  message or factory parameter's type is a parse error (**E2010**).
- There is no module-level mutable state: a file-scope or `static` `var` is a syntax error. State that more
  than one thread uses is a service's fields (reached by message) or a `SharedValue`. A service's state holds no
  promise (**E3216**): a message or factory awaits or cancels the coroutines it starts before it returns.

### Regions and capabilities

Ordinary records need no marker: they belong to the current region and alias freely inside it. Write a
capability word only where a value must change owners or be shared without a writer:

- **Build, then freeze.** Fill a structure inside `modify`, then `freeze` it, and read the frozen result
  from anywhere. `var b = Index.create() as isolated Index`, `modify b 'fill' … end 'fill'`,
  `return freeze b` with `returns immutable Index` (or a typealias of it).
- **A field that owns a region** is `var index as isolated WordIndex`. Fill it in `modify self.index 'fill'`;
  take it out whole with `swap self.index with WordIndex.create()`; `Self{index: WordIndex.create()}` builds it.
- **Frozen data** is declared `immutable T`. A parameter needs no marker to accept it: an unmarked parameter
  that only reads takes a mutable or a frozen argument, and one that writes takes a mutable argument.
  Constant data (a `b"…"` literal, a `let` bound to a constant) is already `immutable`, and `Map` keys and `Set`
  elements are `immutable` slots: a key built at run time is `freeze`d where it is inserted.
- `merge x` turns an `isolated` value back into ordinary mutable data (`return merge box`).
- `type T is unique` for a value that must never be copied or aliased; `let b = a` moves it.
- `where T is sendable` or `where T is isolated` on a generic **type** bounds its argument.
- On an interface requirement or function type, write `mutable self`, `mutable T` or `mutates captures`
  where the callee writes; unmarked ones are read-only and never wait or yield. This holds in every program,
  whether or not it spells a region word. A function value that hands back its read-only parameter returns
  `x.clone()`. A closure that writes what it captures, or builds a record from it, is a `mutates captures`
  closure without being marked, so the function type it is stored under declares `mutates captures`; a conformer
  whose result holds data from its receiver needs `mutable self` on the requirement.
- A function whose return paths mix frozen and writable data returns a read-only result: declare it
  `returns immutable` when every path is frozen, or return a `.clone()`. A record that is an immutable
  aggregate (every field a `let` of a value type) is a value: it needs no `freeze`, and `freeze` of it is E3200.
- A service message parameter, message reply or factory parameter whose type is a record, a union with a
  payload, an array or an interface value is written `isolated T` (the sender builds it for this one send) or
  `immutable T` (the sender passes `freeze x.clone()`). Numbers, `bool`, `String`, `Character`, enums, handles
  and `SharedValue` cells need no word.

Common refusals and their fixes:

| Refusal | Fix |
|---------|-----|
| **E3207** `'f' writes 'x'; 'frozen' is immutable` | pass a mutable value (`frozen.clone()`), or do not freeze it |
| **E3207** `'f' needs 'x' frozen; 'live' is mutable. Freeze it first` | `freeze` it first (`freeze live.clone()` keeps `live`) |
| **E3206** parameter used as mutable and as immutable | make one of the two uses work on a `.clone()` |
| **E3197** write through frozen or read-only data | write to a `.clone()`, or keep the data mutable |
| **E3196** mutable value stored into an `immutable` slot | `freeze` it, or store a `.clone()` |
| **E3196** ``cannot store an immutable 'ByteArray' into 'Tok.value', which holds a mutable 'ByteArray'; declare the field `immutable ByteArray` `` | declare the field, element or payload `immutable ByteArray` (a `b"…"` literal and a constant `let` are `immutable`) |
| **E3207** `'push' needs a writable receiver; 'name' is immutable` | build a mutable array (`ByteArray.create` and `push`, `"text".toByteArray()`, or `.clone()`) instead of writing a literal or constant |
| **E3209** ``'Name' is immutable, the result is not `immutable`; declare the result `immutable` `` | declare the result `immutable ByteArray` |
| **E3197** a write to an element a `for` hands out over a frozen or module-level `List`, or to a mixed-source result | iterate a `.clone()`, or write through a mutable list; return a `.clone()` of the frozen path |
| **E3198** `'x' is not isolated; 'modify' opens an isolated value` | declare it `as isolated T` |
| **E3198** an element of an `isolated` container bound to a `let` or `var` outside a block | open the container with `modify` |
| **E3200** `'x' is not isolated: only a clean call, a swap or an isolated local …` | declare it `as isolated T`, or store a `.clone()` |
| **E3201** one statement reaches two isolated values, or mutable data beside one | open one with `modify`, or split the statement |
| **E3202** a mutable value leaves a `modify` block | let a value, `immutable` data or a fresh `isolated` value leave instead |
| **E3205** `await` or a may-wait or may-yield call inside `modify` | await before the block and pass the result in |
| **E3208** `try … otherwise <param>` or a call result joining a frozen and a fresh argument | pass arguments of one capability, `freeze` or `.clone()` one |
| **E3210** a function value or conformer writes, waits or yields beyond its boundary | declare `mutable self`, `mutable T` or `mutates captures` on the boundary, or change the body |
| **E3212** a tied promise left unconsumed on an exit, returned, stored or passed on | `await p` or `p.cancel()` before every exit; `.clone()` the arguments to untie the call |
| **E3213** a promise dropped, overwritten or discarded | await it, cancel it, store it or return it; `_ = try h.m(x)` discards a service reply |
| **E3214** a unique value cloned, frozen, or bound from a field or element | move it with `pop` or `remove`, or `swap` it out of an `isolated` field |
| **E3215** a type argument breaks a `where T is sendable` or `is isolated` bound | `immutable Job` or a value for `sendable`; a value or unique type for `isolated` |
| **E3216** a service's state field can hold a promise | `await` or `.cancel()` the promise before the message or factory returns, and keep its result in the field |
| **E3217** a service message or factory parameter, or a reply, is a record, payload union, array or interface value with no `isolated` or `immutable` | write `isolated T` and send a fresh value, or `immutable T` and send `freeze x.clone()` |
| **E3186** a local read before a by-reference call writes it | copy it into a `let` in an earlier statement and read the copy |
| **E3005** a `Narrow` variable at a `mutable Wide` parameter | declare the variable at the parameter's type |

The full rules are in [Regions and Capabilities](/docs/language/regions/).

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
	Expect.equal(discounted(400, percent: 25), expected: 300)
end 'a quarter off'

test 'too large a discount is caught'
	let price = try discounted(400, percent: 150) otherwise 0
	Expect.equal(price, expected: 0)
end 'too large a discount is caught'
```

- The name is a quoted phrase, repeated exactly by `end` (E2008).
- A test body is an implied `try`: a throwing call, a throwing interface call or an `await` needs no `try`,
  and any error fails the test. A helper function a test calls, or a closure written in one, still needs
  `try` (E3057).
- `equal` and `notEqual` take any `Equatable` type and the ordering matchers any `Comparable` one, inferred
  from the two arguments, so a value of any alias needs no cast. `close` takes `AssertedReal` floats.
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

## Scripts and scriptlib

Put a helper in a **scriptlib** package only when two or more single-file scripts need it. Keep out of
scriptlib:

- Code only one script uses: keep it in that script.
- Code for a directory project: put it in the project's own files.
- Anything meant to ship to everyone: that belongs in the standard library (`stdlib/`).

Lay out, write and call a package as [The scriptlib directory](/docs/cli/#the-scriptlib-directory)
says, and run [`maxon check-scriptlib`](/docs/cli/#maxon-check-scriptlib) after editing one.
