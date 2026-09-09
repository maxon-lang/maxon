---
title: Best Practices
description: Recommended practices for type safety, error handling, mutability, memory, functions, and program structure.
sidebar:
  order: 2
---

A practical guide to writing robust, idiomatic, and maintainable Maxon code. This complements the style guide (which covers formatting) by focusing on design patterns, safety, and effective use of language features.

---

## Table of Contents

1. [Type Safety](#type-safety)
2. [Error Handling](#error-handling)
3. [Variables and Mutability](#variables-and-mutability)
4. [Memory and Ownership](#memory-and-ownership)
5. [Functions](#functions)
6. [Pattern Matching](#pattern-matching)
7. [Collections](#collections)
8. [Types and Interfaces](#types-and-interfaces)
9. [Program Structure](#program-structure)

---

## Type Safety

### Use Meaningful Ranged Type Aliases

Every numeric type must go through a `typealias` with range constraints. Use this as a design tool, not just a requirement to satisfy the compiler. Choose ranges that reflect the actual domain of your values.

```maxon
' Good: ranges communicate intent and catch bugs at compile time
typealias Port = int(0 to 65535)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = int(0 to 255)

' Wide ranges are fine when no concrete upper bound exists
typealias LineNumber = int(0 to u64.max)   ' no inherent max
```

When a value genuinely has no meaningful range constraint (like a general-purpose counter), declare a typealias whose name describes the *purpose* and use a wide range:

```maxon
typealias VisitCount = int(0 to u64.max)        ' non-negative
typealias FrameDelta = int(i64.min to i64.max)  ' signed
```

Avoid generic names like `Count` or `Index` — they tell a reader nothing the field name doesn't already say. Pick names that carry domain information.

### Create Type Aliases for Collections Early

Define collection type aliases alongside the element type. This keeps type declarations together and avoids repeating `Array with MyType` throughout the code.

```maxon
typealias TaskPriority = int(0 to 10)

type Task
	export var name as String
	export var priority as TaskPriority
end 'Task'

typealias TaskArray = Array with Task
typealias TaskList = List with Task
```

### Use Explicit Conversions

Maxon only allows widening casts. For narrowing conversions, use explicit functions that make your intent clear.

```maxon
' Good: intent is obvious
var rounded = round(3.7)    ' 4
var truncated = trunc(3.7)  ' 3

' The compiler also rejects bare primitive cast targets — cast through a
' named ranged typealias instead (e.g. `var x = 3.7 as Tally`).
```

---

## Error Handling

Maxon's error-handling vocabulary is expressive: single-statement `otherwise` (value, panic, return, throw, break, continue, ignore), multi-line handler blocks with optional error binding, and union error types that can carry associated values. The conventions below describe when to reach for each form.

### Prefer Single-Statement `otherwise`

Use a block handler only when the recovery genuinely needs multiple statements, needs the error value, or needs to inspect its variants. A handler whose body is a single `return`, `continue`, `break`, `throw`, `panic`, or literal default should be a single-statement `otherwise`.

```maxon
' Good: single-statement forms
let value = try config.get("timeout") otherwise 30
let ch = try bytes.get(i) otherwise panic("bounds proven above")
try stack.pop() otherwise break
try project.types.insert(name, value: t) otherwise continue
let content = try readFile(path) otherwise throw ConfigError.readError(path)

' Bad: wrapping a single statement in a block
try readFile(path) otherwise 'fail'
	return 1
end 'fail'

' Good: block form earns its weight when it branches on the error
try readFile(path) otherwise (e) 'fail'
	match e 'report'
		notFound(p) then printError("missing: {p}")
		permissionDenied(p) then printError("denied: {p}")
	end 'report'
	return 1
end 'fail'
```

### Use a Literal Default Only for True Sentinels

A literal default (`otherwise 0`, `otherwise ""`, `otherwise false`) is appropriate only when the default value is semantically meaningful in its own right — for example, a lookup table that legitimately returns 0 for "no entry" to end a parse loop. Add a one-line comment naming *why* the default is correct, so future readers don't mistake the sentinel for a silent fallback.

```maxon
' Sentinel: infixBindingPower throws for non-operator tokens; 0 signals "stop the Pratt loop".
let bp = try infixBindingPower(opToken.kind) otherwise 0
```

### Promote Invariant-Hiders to Panic

If the `otherwise` path is provably unreachable because of a bounds check, loop guard, or just-executed push, use `otherwise panic("...")` — not a literal default. A silent default turns a latent bug into data corruption; a panic surfaces it immediately with a stack trace. The panic message should name the invariant that must have broken.

```maxon
' Just-proven bounds check — panic makes the invariant explicit.
while i < len 'scan'
	let b = try bytes.get(i) otherwise panic("scan: i < len just succeeded but get({i}) failed")
	' ...
end 'scan'

' Just-grown array — the entry is known to exist.
while table.count() <= valueId 'grow'
	table.push(0)
end 'grow'
let current = try table.get(valueId) otherwise panic("incrementUse: table grown to include valueId but get failed")
```

### Use `otherwise ignore` for Best-Effort Operations

Only ignore errors when failure genuinely doesn't matter — cleanup after the real work is done, idempotent registrations, best-effort logging.

```maxon
' Acceptable: cleanup that may fail harmlessly
try deleteTempFile(path) otherwise ignore
try registry.insert(name, value: info) otherwise ignore   ' idempotent

' Bad: silently swallowing a real failure
try saveUserData(data) otherwise ignore
```

### Propagate Errors With Bare `try`

When a throwing function cannot meaningfully recover from an inner error, propagate with bare `try` (no `otherwise`).

```maxon
function loadConfig(path FilePath) returns Config throws ConfigError
	let contents = try readFile(path)    ' propagates
	return try parseContents(contents)   ' propagates
end 'loadConfig'
```

### `panic` for Invariants, `throw` for Expected Failures

`panic` terminates the program with a stack trace. Use it for invariant violations — cases that cannot legitimately occur and which a caller cannot reasonably handle. Use `throw` for failures the caller might want to recover from.

```maxon
' panic: invariant; this is a bug if it fires.
function getCurrentDef(stack ValueIdArray) returns ValueId
	if stack.count() == 0 'empty'
		return 0   ' undefined use — a separate case, not an invariant violation
	end 'empty'
	return try stack.get(stack.count() - 1) otherwise panic("getCurrentDef: non-empty stack check just succeeded but get(count-1) failed")
end 'getCurrentDef'

' throw: the caller can decide what to do.
function findUser(name String) returns User throws LookupError
	let info = try users.get(name) otherwise throw LookupError.notFound(name)
	return info
end 'findUser'
```

### Error Types: Use a Union With Associated Values When Throw Sites Have Useful Context

Define error types as unions that carry the context callers need. If every throw site has a path, token, or position in scope, the error type should carry it. Reserve bare enums for error types whose variants genuinely have no useful payload.

```maxon
' Good: throw sites have paths in scope, so the error carries them.
union CompileError implements Error
	fileNotFound(path FilePath)
	directoryNotFound(path FilePath)
	readError(path FilePath)
	compileError
end 'CompileError'

' Good: argument parsing failure carries the offending argument.
union ArgError implements Error
	invalidOption(arg String)
end 'ArgError'

' OK: signal-only error type with no useful payload.
enum BlockRefLookupError implements Error
	notInFunction
end 'BlockRefLookupError'
```

### Avoid Sentinel-Return APIs

A function that returns `-1`, `""`, or another magic value on "not found"/"error" forces every caller to pattern-check before using the result. Prefer `throws` (with a descriptive union error type) unless the sentinel is part of the normal data model (e.g. `parseBuildName` returning `""` for "no build name configured" is meaningful data, not an error).

```maxon
' Good: caller uses if-let and branches structurally.
function lookup(name String) returns VarSlot throws VarRegistryLookupError
	let info = try vars.get(name) otherwise throw VarRegistryLookupError.notFound(name)
	return info.slot
end 'lookup'

' Caller:
if let slot = try vars.lookup(name) 'found'
	useSlot(slot)
end 'found'

' Avoid: sentinel return forces every caller to branch on >= 0 or similar.
function lookup(name String) returns VarSlot
	let info = try vars.get(name) otherwise return -1   ' sentinel
	return info.slot
end 'lookup'
```

### Exhaustive `match` Over Enums and Unions

A `match` on a closed set of variants should cover every case explicitly. When you want a catch-all, use `default panic("...")` for unreachable cases or `default throws ...` to turn unmatched variants into an error — never `default` with a placeholder value that silently propagates.

```maxon
' Good: explicit, exhaustive.
match status 'check'
	ok gives 0
	notFound gives 1
	serverError gives 2
end 'check'

' Good: catch-all that crashes with context if a new variant is added.
match tokenKind 'classify'
	intLiteral then handleInt()
	stringLiteral then handleString()
	default panic("classify: unhandled token kind {tokenKind}")
end 'classify'
```

---

## Variables and Mutability

### Default to `let` for Immutability

Use `let` unless you need to reassign the variable. This communicates intent and prevents accidental mutation.

```maxon
let maxRetries = 3               ' constant, never changes
let userName = getUserName()     ' assigned once from a function

var retryCount = 0               ' needs to be incremented
var buffer = ByteArray.create()         ' needs push/set operations
```

### Keep Variable Scope Narrow

Declare variables as close to their first use as possible. This improves readability and ensures borrow lifetimes are short.

```maxon
' Good: variable declared where it is needed
for item in items 'process'
	let score = computeScore(item)
	if score > threshold 'above'
		results.push(item)
	end 'above'
end 'process'

' Avoid: declaring everything at the top of the function
var score = 0
var temp = ""
var found = false
' ... 30 lines later ...
score = computeScore(item)
```

### Use `_` to Discard Intentionally

When you do not need a value (loop variable, tuple element), use `_` to signal this clearly. In match arms, omit the parentheses entirely when you don't care about the associated value — using `caseName(_)` when all bindings are discarded is an error (E3081).

```maxon
' Iterate directly when only the element matters (don't use withIterator())
for name in entries 'loop'
	print("{name}\n")
end 'loop'

' Discard an impure function's result
_ = incrementCounter()

' In matches, omit the binding entirely if you don't need the associated value
match result 'check'
	success then return true
	failure then return false
end 'check'
```

---

## Memory and Ownership

### Understand Reference Semantics for Structs

Struct assignment creates a reference (alias), not a copy. Mutating through one variable affects all aliases.

```maxon
var a = Point{x: 1, y: 2}
var b = a          ' b is an alias for a
b.x = 99          ' a.x is now 99 too

' Use clone for an independent copy
var c = a.clone()
c.x = 50          ' a.x is still 99
```

### Clone Explicitly When You Need Independence

If you need to mutate a value without affecting the original, clone it. Maxon makes copies explicit so there are no hidden allocations.

```maxon
function sortedCopy(items ItemArray) returns ItemArray
	var copy = items.clone()
	sortInPlace(copy)
	return copy
end 'sortedCopy'
```

### Respect Borrow Lifetimes

When you take a reference from a collection (via `get`), do not mutate the collection until you are done with the reference. Use the reference first, then mutate.

```maxon
' Good: use the reference before mutating
var first = try list.get(0) otherwise ""
print("{first}\n")        ' last use of first, borrow expires
list.push("new item")     ' OK: borrow has expired

' Bad: mutation while borrow is live
var first = try list.get(0) otherwise ""
list.push("new item")     ' ERROR E3070: list borrowed by first
print("{first}\n")
```

---

## Functions

### Use the First-Positional, Rest-Named Convention

The first argument is always positional. Subsequent arguments use `name: value` syntax. This reads naturally at call sites.

```maxon
print("hello")
connect("localhost", port: 8080)
array.set(0, value: 42)
createUser("alice", role: "admin", active: true)
```

### Use Default Parameters for Optional Configuration

Default parameters avoid the need for multiple overloads and make the common case simple.

```maxon
function connect(host String, port Port = 8080, timeout Milliseconds = 5000)
	' ...
end 'connect'

connect("localhost")                               ' uses defaults
connect("example.com", port: 443, timeout: 10000)  ' override both
```

### Use Closures for Short Callbacks

Closures work well for sort comparators, map transforms, and filter predicates. Keep them short.

```maxon
items.sort(function(a, b) gives a.priority - b.priority)
let names = users.map(function(u) gives u.name)
```

### Handle Pure vs Impure Return Values Correctly

The compiler tracks purity. Pure function results must always be used. Impure function results must be explicitly discarded with `_ =` if unused.

```maxon
' Pure: result must be used
let doubled = double(5)

' Impure: explicitly discard if unused
_ = incrementAndLog()

' Chainable methods: result can be discarded freely
builder.addField("name")
```

---

## Pattern Matching

### Prefer `match` Over Chained `if` for Enums and Unions

`match` guarantees exhaustiveness. If a new case is added to an enum or union, the compiler forces you to handle it. For unions, `match` is the only way to inspect values since `==`/`!=` are not available.

```maxon
' Good: exhaustive, compiler-checked
match direction 'navigate'
	north then moveUp()
	south then moveDown()
	east then moveRight()
	west then moveLeft()
end 'navigate'

' Avoid: error-prone, not exhaustive
if dirString == "north" 'n'
	moveUp()
end 'n' else 'other'
	if dirString == "south" 's'
		moveDown()
	end 's'
	' easy to forget cases...
end 'other'
```

### Use `default throws` or `default panic` for Partial Matching

When you intentionally handle only a subset of enum or union cases, use `default throws` (for recoverable situations) or `default panic` (for bugs).

```maxon
' Recoverable: caller decides what to do (Shape is a union with associated values)
function areaOf(shape Shape) returns float throws ShapeError
	return match shape 'calc'
		circle(r) gives 3.14159 * r * r
		square(s) gives s * s
		default throws ShapeError.unsupported
	end 'calc'
end 'areaOf'

' Unrecoverable: should never happen
match token 'lex'
	plus then emitAdd()
	minus then emitSub()
	default panic("unexpected token in expression")
end 'lex'
```

### Use Match Expressions for Value Mapping

When every branch produces a value, use `gives` instead of `then` to make the match an expression.

```maxon
let label = match status 'label'
	HttpStatus.ok gives "Success"
	HttpStatus.notFound gives "Not Found"
	HttpStatus.serverError gives "Server Error"
end 'label'
```

### Use Range Patterns for Numeric Classification

Range patterns keep numeric classification concise and correct.

```maxon
let category = match codepoint 'classify'
	0..=127 gives "ASCII"
	128..=2047 gives "2-byte UTF-8"
	2048..=65535 gives "3-byte UTF-8"
	65536.. gives "4-byte UTF-8"
	default gives "invalid"
end 'classify'
```

### Use Range Patterns with `break` to Skip Unhandled Cases

When matching an enum or union where you only care about a few cases, you cannot use `default` (it must be `default throws` or `default panic`). Instead, use a range pattern covering the remaining cases with `break` to skip them silently.

```maxon
union Instruction
	add(dst Register, src Register)
	sub(dst Register, src Register)
	load(dst Register, addr Address)
	store(src Register, addr Address)
	nop
	halt
end 'Instruction'

' Only optimize add instructions, skip everything else
match instruction 'optimize'
	add(dst, src) then optimizeAdd(dst, src: src)
	sub to halt then break
end 'optimize'
```

This is preferable to `default throws` or `default panic` because the unhandled cases are not errors — they are simply not relevant. The range pattern still participates in exhaustiveness checking, so if a new case is added to the enum, the compiler will tell you whether it falls inside or outside the range.

For enums, the same pattern works with enum-qualified range bounds:

```maxon
enum LogLevel
	trace
	debug
	info
	warning
	error
	fatal
end 'LogLevel'

' Only act on serious levels
match level 'filter'
	LogLevel.error then handleError()
	LogLevel.fatal then handleFatal()
	LogLevel.trace to LogLevel.warning then break
end 'filter'
```

### Extract Associated Values with Clear Binding Names

Use descriptive names for match bindings so the code reads clearly.

```maxon
match result 'handle'
	success(user) then processUser(user)
	failure(errorCode, message) then logError("{errorCode}: {message}")
	pending then retry()
end 'handle'
```

---

## Collections

### Use `reserve` for Known Sizes

When you know how many elements you will add, reserve capacity upfront to avoid repeated reallocations.

```maxon
var results = ResultArray.create()
results.reserve(inputCount)
for item in inputs 'process'
	results.push(transform(item))
end 'process'
```

### Use `try...otherwise` for All Collection Access

Array `get`, Map `get`, and List access methods all throw. Always wrap them with `try`.

```maxon
let value = try map.get(key) otherwise defaultValue
let element = try array.get(index) otherwise fallback
let first = try list.first() otherwise emptyItem
```

### Choose the Right Collection

- **Array**: Random access, contiguous memory. Best for most use cases.
- **List**: Efficient insertion/removal at both ends. Use when you need a queue or deque.
- **Map**: Key-value lookup. Keys must implement `Hashable`.
- **Set**: Unique elements. Elements must implement `Hashable`.

```maxon
' Array: ordered data with index access
var scores = ScoreArray.create()

' Map: fast lookup by key
var userCache = UserMap.create()

' Set: track unique items
var visited = CitySet.create()

' List: queue with O(1) push/pop at both ends
var taskQueue = TaskList.create()
```

### Iterate Collections Directly

Use `for...in` loops. Use `.withIterator()` when you need the index (via `iter.index()`) or navigation alongside the element.

```maxon
' Direct iteration
for name in names 'greet'
	print("Hello, {name}\n")
end 'greet'

' With index
for (iter, name) in names.withIterator() 'list'
	print("{iter.index() + 1}. {name}\n")
end 'list'
```

---

## Types and Interfaces

### Use `export` Selectively

Export only the public API. Keep internal helpers, fields, and utility methods private. This reduces coupling and makes refactoring safer.

```maxon
export type Parser
	var source String              ' private field
	export var position BytePos    ' public field

	export function parse() returns AST
		' public API
	end 'parse'

	function skipWhitespace()
		' internal helper, not exported
	end 'skipWhitespace'
end 'Parser'
```

### Use Static Methods for Construction

Static methods provide named constructors that are clearer than struct literals for complex initialization.

```maxon
type Connection
	var host as String
	var port as Port
	var timeout as Milliseconds

	export static function createDefault(host String) returns Connection
		return Connection{host: host, port: 8080, timeout: 5000}
	end 'createDefault'

	export static function createSecure(host String) returns Connection
		return Connection{host: host, port: 443, timeout: 10000}
	end 'createSecure'
end 'Connection'

' Clear at the call site
var conn = Connection.createSecure("example.com")
```

### Use Lazy Static Fields for Expensive Initialization

Static fields with complex initializers are evaluated lazily on first access and cached. Use this for lookup tables, precomputed data, and singleton-like patterns.

```maxon
type Classifier
	static let _whitespace = CharacterSet.whitespacesAndNewlines()
	static let _digits = CharacterSet.decimalDigits()

	export static function isWhitespace(c Character) returns bool
		return Classifier.whitespace.contains(c)
	end 'isWhitespace'
end 'Classifier'
```

### Implement Standard Interfaces

Implement `Hashable`, `Equatable`, `Cloneable`, `Stringable`, and `Comparable` where appropriate. This enables your types to work with collections, comparisons, string interpolation, and sorting.

```maxon
typealias Degrees = float(f64.min to f64.max)

type Coordinate implements Hashable, Equatable, Stringable, Cloneable
	export var lat as Degrees
	export var lon as Degrees

	function hash() returns HashValue
		return (trunc(lat * 1000.0) xor trunc(lon * 1000.0)) as HashValue
	end 'hash'

	function toString() returns String
		return "({lat:.4}, {lon:.4})"
	end 'toString'
end 'Coordinate'
```

### Use `where` Clauses to Constrain Generic Types

Constrain type parameters to document requirements and catch misuse at compile time.

```maxon
type SortedList uses T where T is Comparable and Equatable
	var items Array with T

	function insert(item T)
		' T is guaranteed to support comparison
	end 'insert'
end 'SortedList'
```

---

## Program Structure

### Use Block Labels as Documentation

Block labels serve as inline documentation. Choose labels that describe the purpose of the block.

```maxon
if buffer.count() >= MAX_BUFFER_SIZE 'flushWhenFull'
	flush(buffer)
end 'flushWhenFull'

while hasMoreTokens() 'tokenize'
	let token = nextToken()
	tokens.push(token)
end 'tokenize'

for (iter, field) in fields.withIterator() 'validateFields'
	if not isValid(field) 'invalid'
		throw ValidationError.invalidField
	end 'invalid'
end 'validateFields'
```
