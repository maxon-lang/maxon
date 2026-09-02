---
name: maxon-coder
description: Write or modify Maxon (.maxon) code. Use this skill whenever you need to create, edit, or review Maxon source files. Ensures correct syntax and avoids common mistakes.
---

Read `docs/WRITING_MAXON_CODE.md` before writing any Maxon code. It contains mandatory syntax rules, common mistakes, and the correct patterns. Refer to `docs/LANGUAGE_REFERENCE.md` for the full specification and `docs/QUICK_REFERENCE.md` for API reference.

## Building and testing

After writing or modifying Maxon code, verify it compiles and passes tests. Prefer the `maxon-dev` MCP
tools — they are faster than shelling out and return structured output. See CLAUDE.md for the full tool
mapping. **In a worktree, pass `repoRoot` (your worktree's absolute path) to EVERY tool call**, or you
will drive the main checkout and be told `success: true` about a tree containing none of your work.

**TWO compilers are driveable, and every tool naming one takes `compiler` (or `target`):**

**C# bootstrap** (after modifying `maxon-sharp/` or `stdlib/`):
- Build: `mcp__maxon-dev__build` with `target: "csharp"`.
- Spec tests: `mcp__maxon-dev__run_spec_test` (default compiler is `csharp`, suite `specs/`).

**shv2** — the active development line (after modifying `maxon-shv2/`):
- Build: `mcp__maxon-dev__build` with `target: "shv2"`. It is built BY the bootstrap, so build `csharp`
  first if `maxon-sharp/` is newer than `bin/`. One compiler per call — there is no `"both"`.
- Spec tests: `mcp__maxon-dev__run_spec_test` with `compiler: "shv2"` (suite `specs-shv2/`).

> ⚠ **The v1 self-hosted compiler (`maxon-selfhosted`) is DEPRECATED and unreachable from the MCP** — no
> `selfhosted` token, no `run_self_hosted_test` tool, no `"both"` build. It also no longer BUILDS, and
> that is accepted. **Read its 191,487 lines as a reference** — every hard mechanism already exists there,
> debugged — but drive it by hand (CLAUDE.md) if you truly must run it.

**Quick experiments / verification:**
- Run a snippet or file end-to-end: `mcp__maxon-dev__run_program` (`compiler` is required: `"csharp"` or
  `"shv2"`).
- Inspect lowered IR: `mcp__maxon-dev__dump_ir` (`compiler` required; `dumpStages: true` for per-stage
  artifacts is **csharp only** — shv2 REJECTS it with an error naming the gap, rather than dropping it).
- Format Maxon source: `mcp__maxon-dev__fmt` — the bootstrap's `maxon fmt`; **shv2 has no formatter**.
  ⚠ Give it the **file** you mean: with no path it formats the whole current directory.
- Look up a 4-digit error code: `mcp__maxon-dev__lookup_error_code`. Never reference a code by its bare
  number in source — use the generated `ErrorCode` member.
- Debug memory leaks: `mcp__maxon-dev__mm_trace_analyze` (`mmTrace` is csharp-only on `run_spec_test`;
  shv2 rejects it). **Exit code 101 = a leak was detected.**

⚠ **`bin/maxon.exe` and the shv2 binary are BOTH gitignored and nothing rebuilds them**, so a stale one
lies in both directions — build before you trust a run.

## Syntax that DOES NOT EXIST (most common mistakes)

```
WRONG                          CORRECT
─────────────────────────────  ─────────────────────────────
let x: int = 5                 let x = 5            (type always inferred)
var y: String = "hi"           var y = "hi"
x += 1                         x = x + 1
x++                            x = x + 1
x % 5                          x mod 5
!condition                     not condition
a && b                         a and b
a || b                         a or b
a & b                          a and b              (bitwise on int)
a | b                          a or b               (bitwise on int)
a ^ b                          a xor b              (bitwise on int)
a << 4                         a shl 4
a >> 4                         a shr 4
if (x > 0) { ... }             if x > 0 'label' ... end 'label'
} else {                       end 'label' else 'other'
"hello " + name                "hello {name}"       (interpolation)
null / nil / None              (does not exist — use try...otherwise)
;                              (no semicolons — newline-delimited)
func(a, b, c)                  func(a, b: b, c: c)  (first positional, rest named)
param int                      param SomeTypealias  (bare int/float/byte forbidden)
returns int                    returns SomeTypealias
cond ? a : b                   a if cond else b
```

## Mandatory rules

- **Every block MUST have a label + matching `end`**: `if`, `else`, `while`, `for`, `match`, `try...otherwise`, `function`, `type`, `enum`, `union`, `interface`, `extension`.
- **`else` MUST be on the same line as its `end`**: `end 'check' else 'other'` or `end 'check' else if x == 0 'zero'`.
- **NEVER use bare `int`, `float`, or `byte`** in type positions (params, returns, fields). ALWAYS use a typealias with a range. `bool` is the exception.
- **Variable declarations NEVER have type annotations** — `let x = 5`, not `let x: int = 5` (E2010).
- **First argument is positional, all others MUST be named**: `connect("localhost", port: 8080, timeout: 5000)`. Naming the first one is **E2052** — `arr.remove(0)`, never `arr.remove(at: 0)`, even though the parameter is called `at`.
- **`main` MUST return `ExitCode` and MUST NOT throw**.
- **Collection `.get()` ALWAYS requires `try...otherwise`** — it throws.
- **Throwing functions MUST be called with `try`** (E3057).
- **Match arms MUST use bare case names** (`red`, not `Color.red`) (E3075).
- **Enum/union match MUST be exhaustive**. Plain `default` on enum is forbidden (E2046) — use `default throws` or `default panic("msg")`.
- **Union values CANNOT be compared with `==`** (E3066) — use `match`.
- **Indentation uses tabs** (not spaces).
- **Strings use `{expr}` interpolation** — there is NO string concatenation operator.
- **Comments use `//`** (or `/* ... */` for block comments). **Concise and minimal — the default is NO
  comment.** Explain the **why** (a constraint, an invariant, a non-obvious reason), never the **how**;
  the code is the how. **Present state only — no history**: no "used to", "changed from", or old names;
  git holds that. **A comment you edit is rewritten to conform, not patched.** (Code Quality checklist,
  `.claude/CLAUDE.md`.)
- **Blocks MUST NOT be empty** (E3082) — no comment-only blocks.
- **Struct FIELDS use `as`**: `export var x as Coord`, not `export var x Coord` (E2010). Parameters and
  returns do NOT — `function f(x Coord) returns Coord`.
- **`Type{...}` ONLY inside that type's own methods** (E3076). External callers go through a
  `static function create(...)`. This includes ranged typealiases: `Port{8080}` at a call site is
  E3076 — write the bare literal at a `Port`-typed destination, or `8080 as Port`.
- **Every field must be initialized** in a struct literal (E3086) unless it has a declaration default
  (`export var value = 0`) or is proven definitely assigned before a `Self{}` in a static factory.
- **All variables must be used** (E3012). Use `_` to discard.
- **A typealias declared and never used is an error** (E3062) — "used" means the NAME appears in a type
  position in its OWN file; `export` and `module` both exempt it (the check only asks about aliases
  nothing outside the file can see), and inference from a bare `[...]` literal is not a use.
- **`var` that is never reassigned is an error** (E3077) — use `let`.
- **Cannot assign immutable `let` ref-type to `var`** (E3078) — use `let` or `.clone()`.
- **Self-assignment is an error** (E3067): `x = x`, `p.x = p.x`.

## Stdlib type aliases to use

Cross-cutting: `ExitCode`, `HashValue`, `Codepoint`, `Byte`, `ByteArray`, `FileSize`, `Timestamp`, `NetworkPort`, `Character`.

⚠ **`Integer` is NOT one of them** — nothing in the stdlib exports it, and a bare `Integer` in a type
position is E2003 ("Unknown type"). Examples that use it declare
`typealias Integer = int(i64.min to i64.max)` in the same file — in real code, prefer a purpose-named
alias.

For per-domain quantities (counts, indices, byte offsets, math values), declare a typealias local to your file with a name that describes the *purpose* — e.g. `Tally`, `BytePos`, `Coord`. Don't reach for a generic `Count` or `Index`; the stdlib doesn't export them anymore.

Wide ranges like `int(0 to u64.max)` or `int(i64.min to i64.max)` are fine when no concrete bound exists. Use tight ranges only for true domain limits (e.g., `Port = int(0 to 65535)`).

## Quick syntax reference

```maxon
// Function
function name(param1 Type1, param2 Type2) returns ReturnType
	// body
end 'name'

// Throwing function
function load(path FilePath) returns Config throws FileError
	// ...
end 'load'

// Void function (no returns clause)
function printStatus()
	print("OK\n")
end 'printStatus'

// Default parameters
function connect(host String, port Port = 8080) returns Connection
	// ...
end 'connect'

// Variables (type inferred, NEVER annotated)
let x = 42          // immutable
var y = 10          // mutable (must be reassigned or error E3077)
_ = sideEffect()    // discard (RHS MUST be a function call)

// Domain-specific typealiases declared next to the type that uses them
typealias Coord = float(f64.min to f64.max)
typealias VisitCount = int(0 to u64.max)
typealias StatusCode = int(0 to 599)

// Struct type — fields take 'as'; a literal is legal only inside the type's own methods
export type Point
	export var x as Coord            // public mutable
	export var y as Coord
	var visits as VisitCount = 0     // private, declaration default

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'

	function magnitude() returns Coord
		return sqrt(self.x * self.x + self.y * self.y)
	end 'magnitude'

	function magnitudeSquared() returns Coord
		return magnitude() * magnitude()   // sibling call — no explicit receiver
	end 'magnitudeSquared'

	static function origin() returns Point
		return Point{x: 0.0, y: 0.0}
	end 'origin'
end 'Point'

var p = Point.create(1.5, y: 2.5)   // E3076 if written Point{x: 1.5, y: 2.5} out here
var o = Point.origin()

// Enum (auto Equatable/Hashable; supports ==/!=)
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

// Enum properties: .rawValue, .name, .ordinal, .allCases, .allCaseNames
// Methods: fromRawValue(), fromName() (throw — use try)

// Union (for associated values; NO ==/!=, use match)
union Result
	success(value VisitCount)
	failure(code StatusCode, message String)
	pending
end 'Result'

var r = Result.success(42)
var f = Result.failure(404, message: "Not found")

// Error enum (only enums/unions can implement Error)
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'

// Interface
interface Describable
	function describe() returns String
end 'Describable'

interface Container uses Element
	function get(index ContainerIndex) returns Element throws ArrayError
end 'Container'

// Extension (interface methods synthesized for all conformers)
extension Array where Element is Equatable
	export function contains(element Element) returns bool
		// ...
	end 'contains'
end 'Array'

// Typealias (with range)
export typealias Score = int(0 to 100)
export typealias ScoreArray = Array with Score
export typealias ScoreMap = Map with (String, Score)

// Ranged type values: a bare literal at a Port-typed destination, or an 'as' cast.
// Port{8080} is E3076 outside the type's own methods.
let port = 8080 as Port
connect(host, port: 443)    // literal takes the parameter's declared type

// If / else if / else
if x > 0 'positive'
	print("positive\n")
end 'positive' else if x == 0 'zero'
	print("zero\n")
end 'zero' else 'negative'
	print("negative\n")
end 'negative'

// While
while count < 10 'loop'
	count = count + 1
end 'loop'

// For
for i in 1 to 5 'loop' ... end 'loop'              // inclusive: 1,2,3,4,5
for i in 0 upto n 'loop' ... end 'loop'            // exclusive: 0..n-1
for item in array 'each' ... end 'each'            // collection
for (iter, item) in array.withIterator() 'e' ... end 'e'  // iter.index() etc.
for color in Color.allCases 'c' ... end 'c'        // enum cases
for c in "hello" 'ch' ... end 'ch'                 // grapheme clusters
for _ in 0 upto 10 'r' ... end 'r'                 // discard variable

// Break / continue
break
break 'outerLoop'
continue
continue 'outer'

// Match statement
match value 'label'
	1 then doOne()
	2 or 3 then doTwoOrThree()
	4 to 10 then doRange()
	default then doDefault()
end 'label'

// Match expression (use 'gives')
let label = match status 'map'
	ok gives "Success"
	notFound gives "Not Found"
	serverError gives "Error"
end 'map'

// Enum match (exhaustive; bare case names; 'default throws' or 'default panic')
match status 'handle'
	ok then doOk()
	default throws StatusError.unhandled
end 'handle'

// Union destructuring
match result 'handle'
	success(value) then print("{value}")
	failure(code, msg) then print("{code}: {msg}")
	pending then print("waiting")
end 'handle'

// Conditional (ternary) expression — lowest precedence
let abs = x if x > 0 else -x
let tier = "gold" if s > 90 else "silver" if s > 70 else "bronze"
print("Mode: {"fast" if turbo else "normal"}")

// Error handling
let content = try readFile(path) otherwise ""         // default value
try readFile(path) otherwise 'err' ... end 'err'      // block handler
try readFile(path) otherwise (e) 'err'                // error binding
	match e 'handle'
		notFound then print("Not found\n")
		permissionDenied then print("Denied\n")
	end 'handle'
end 'err'
try cleanup() otherwise ignore                        // discard error
let slot = try xs.get(i) otherwise panic("unreachable")   // panic on error
let v = try mayFail() otherwise return -1             // single-statement: return/break/continue/throw
let content = try readFile(path)                      // propagate (inside throwing fn)

// if-try
if let value = try mayFail() 'ok'
	print("{value}")
end 'ok' else (e) 'err'
	print("Error\n")
end 'err'

panic("invariant violated: {details}")                // unrecoverable

// Closures (capture by reference) — a closure literal STARTS with `function`
let double = function(n VisitCount) gives n * 2
items.sort(function(a, b) gives a.priority - b.priority)
let always42 = function(_ VisitCount) gives 42

// Tuples
var t = (10, 20)                  // 2+ elements required
t.0 = 30                           // mutable field access
var (x, y) = makePair(10, b: 32)  // destructure (new bindings)
(x, y) = makePair(1, b: 2)        // assign to existing vars
(x, var z) = makePair(1, b: 2)    // mixed: existing + new
(_, status) = fetch()             // discard element

for (key, value) in m 'loop' ... end 'loop'   // destructure in for

// Async / await
var promise = async someFunction(arg1, arg2)
var result = await promise
var r = try await p otherwise 0    // throwing async
p.cancel()

// Visibility (file-private by default; use 'export' for cross-file)
export function publicFunc() returns Tally ...
export type PublicType ...
export typealias PublicAlias = int(0 to 100)
export enum PublicEnum ...
export union PublicUnion ...
export var sharedState = 0

// Conditional compilation
#if os(Windows)
	let sep = "\\"
#else
	let sep = "/"
#endif
// Conditions: os(Windows|Linux|Macos), arch(x64|arm64), testing(true|false)
// Operators: and, or, not
```

## Strings

```maxon
// Interpolation (no concatenation operator)
var msg = "Hello, {name}!"
print("{n:04x}")      // zero-padded hex
print("{f:.2}")       // 2 decimal places
print("{n:8.2}")      // width 8, 2 decimal places
print("Use \{expr\}") // escape literal braces

// Efficient append (grows buffer in place; no temporary)
var s = ""
s.append("hello")
s.append(" {name}!")

// Self-append pattern is auto-optimized to in-place append
var s = ""
while cond 'loop'
	s = "{s}{v},"         // optimized
end 'loop'

// Trimming
"  hi  ".trim()                                     // "hi"
"123abc".trim(CharacterSet.decimalDigits())         // "abc"

// Iteration
for c in s 'chars' ... end 'chars'                  // grapheme clusters
for b in s.bytes() 'bytes' ... end 'bytes'          // raw bytes
for cp in s.codepoints() 'cp' ... end 'cp'          // codepoints

// Byte string literal (creates ByteArray)
let bytes = b"hello"
let raw = b"\xFF\x00"
```

## Collections

Every method that can fail THROWS — `get`, `set`, `pop`, `remove`, `Map.insert`, `List.insert`,
`List.first` — so it needs `try` (E3057). `push`, `Array.insert`, `upsert`, `clear`, `reserve`,
`resize`, `prepend` and `append` do not. A returned value must be used (E3064/E3065).

```maxon
// Arrays — a container needs a typealias before use (E3003)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
var arr = TallyArray.create()
var literal = [1, 2, 3]                        // literal infers its type; NOT a use of the alias (E3062)
arr.push(42)
let n = arr.count()
let val = try arr.get(0) otherwise 0           // throws — ALWAYS try
try arr.set(0, value: 100) otherwise ignore    // throws
arr.insert(0, value: 99)                       // first arg positional, rest named
let last = try arr.pop() otherwise 0           // throws
let gone = try arr.remove(0) otherwise 0       // throws, returns the removed element
arr.reserve(100)
arr.resize(50)
arr.clear()

// Maps
typealias StringTallyMap = Map with (String, Tally)
var m = StringTallyMap.create()
try m.insert("hello", value: 42) otherwise ignore   // throws MapError.keyAlreadyExists
m.upsert("hello", value: 43)                        // insert-or-update, never throws
let hit = try m.get("hello") otherwise 0            // throws
let has = m.contains("hello")                       // NOT containsKey
let dropped = m.remove("hello")                     // returns bool
let size = m.count()

// List (doubly linked, O(1) at ends)
typealias TallyList = List with Tally
var list = TallyList.create()
list.prepend(1)
list.append(2)
let head = try list.first() otherwise 0         // throws
let popped = try list.removeFirst() otherwise 0 // throws
```

## Builtins

```maxon
// Intrinsics (accept float or int-promoted-to-float)
abs(x)  sqrt(x)  floor(x)  ceil(x)  round(x)  trunc(x)     // trunc returns int
min(a, b: b)
max(a, b: b)
sizeof(TypeName)                                             // compile-time
countof(TypeName)                                            // elements a Vector with N T holds

// Stdlib I/O
print("hello\n")
printError("fail\n")
panic("message")
sleep(100)                                                   // milliseconds

// Math library
Math.sin(x)  Math.cos(x)  Math.tan(x)  Math.atan(z)
Math.atan2(y, x: x)
Math.exp(x)  Math.log(x)  Math.log2(x)  Math.log10(x)
Math.pow(base, exponent: e)
```

## Operators (high to low precedence)

`.` `()` > `as` > `-` `not` (unary) > `*` `/` `mod` > `+` `-` > `shl` `shr` > `==` `!=` `<` `>` `<=` `>=` `is` `is not` > `and` > `xor` > `or` > `... if ... else ...`

Type casts (widening only): `int → float`, `byte → int`, `byte → float`, `int` literal 0–255 → `byte`.
Float→int requires `trunc()`, `round()`, `floor()`, or `ceil()`.
`is` / `is not` compare reference identity on struct types (not primitives).

## Memory model

- Primitives: copied by value.
- Structs: assigned by reference (alias). Use `.clone()` for an independent copy (auto-generated when all fields are Cloneable).
- Reference counting with automatic scope cleanup.
- Stack promotion: small all-primitive structs that don't escape are stack-allocated.
- `@heap var p = Point.create(0.0, y: 0.0)` forces heap allocation.
- Borrow checker (NLL): CANNOT mutate a collection while a `.get()` borrow is live (E3070).
- Pass-by-reference is automatic for parameters that are assigned to in the callee.
- Passing a `let` var to a mutating parameter is E3019.

## Purity & discards

- **Pure functions** (no side effects): result MUST be used; cannot discard with `_ =` (E3064).
- **Impure functions**: result MUST be explicitly used or discarded with `_ =` (E3065).
- **Chainable methods** (take `self`, return same type): result can be dropped freely.

## File I/O & paths

```maxon
let fp = FilePath from "data.txt"
let content = try File.readText(fp) otherwise ""
try File.writeText(fp, content: "hello")
File.exists(fp)
let info = try File.info(fp) otherwise ...
// info.size, info.modifiedTime, info.isDirectory, info.isReadOnly, ...
```

## Prefer iterators over indexed access

When walking a collection, prefer `for`-`in` and iterators over manual index loops with `.get()`. Indexed access forces a `try ... otherwise` on every step, triggers borrow-checker conflicts (E3070) if the collection is mutated, and is typically slower than direct iteration.

```maxon
// AVOID — indexed loop with per-step try
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

// PREFER — explicit iterator for navigation (advance/retreat/seek/peek)
var it = try arr.createIterator() otherwise return 0
while it.hasNext() 'loop'
	let v = it.current()
	it.advance(1)
	process(v)
end 'loop'
```

Use `.withIterator()` to combine element access with iterator methods (`index()`, `advance()`, `retreat()`, `advanceBy(n)`, `retreatBy(n)`, `seek(idx)`, `peek(ahead)`). It works on every iterable (Array, String, Map, Set, List) and is a lazy wrapper — no intermediate collection is allocated.

Reach for `.get(i)` only when the access pattern is genuinely random (e.g., binary search, lookup table), not sequential.

## Additional guidance

- Add blank lines to improve readability around control flow and between logical sections.
- Use `let` by default; reach for `var` only when mutation is required.
- Use `panic`/`default panic` for truly unreachable branches; use `throw`/`default throws` for recoverable errors.
