# Maxon Best Practices

A practical guide to writing robust, idiomatic, and maintainable Maxon code. This complements the [Style Guide](STYLE_GUIDE.md) (which covers formatting) by focusing on design patterns, safety, and effective use of language features.

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
typealias Age = int(0 to 150)
typealias Percentage = float(0.0 to 100.0)
typealias Port = int(0 to 65535)

' Avoid: max-range aliases that provide no safety
typealias Age = int(i64.min to i64.max)    ' defeats the purpose
```

When a value genuinely has no meaningful range constraint (like a general-purpose counter), use the standard library aliases:

```maxon
' Standard library aliases for common unconstrained uses
typealias count = Count      ' non-negative: int(0 to i64.max)
typealias offset = Offset    ' signed: i64
```

### Create Type Aliases for Collections Early

Define collection type aliases alongside the element type. This keeps type declarations together and avoids repeating `Array with MyType` throughout the code.

```maxon
typealias TaskPriority = int(0 to 10)

type Task
	export var name String
	export var priority TaskPriority
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

' The compiler rejects ambiguous casts
var x = 3.7 as int          ' ERROR: narrowing cast not allowed
```

---

## Error Handling

### Prefer `try...otherwise` with a Sensible Default

When you can provide a reasonable default value, use the inline form. This keeps error handling concise and local.

```maxon
let value = try config.get("timeout") otherwise 30
let name = try readLine() otherwise ""
```

### Use Block Handlers for Complex Recovery

When recovery requires multiple statements, use the block form with a descriptive label.

```maxon
try loadDatabase(path) otherwise 'dbFail'
	logError("Database load failed, rebuilding index")
	rebuildIndex()
	useEmptyDatabase()
end 'dbFail'
```

### Use Error Binding When You Need to Inspect the Error

Bind the error to inspect which specific failure occurred.

```maxon
try parseConfig(data) otherwise (e) 'parseErr'
	match e 'handle'
		invalidSyntax then logError("Bad syntax in config")
		unexpectedEnd then logError("Config file truncated")
	end 'handle'
	useDefaults()
end 'parseErr'
```

### Use `otherwise ignore` Sparingly

Only ignore errors for best-effort operations where failure is truly acceptable, like optional cleanup.

```maxon
' Acceptable: cleanup that may fail harmlessly
try deleteTempFile(path) otherwise ignore

' Bad: silently swallowing errors you should handle
try saveUserData(data) otherwise ignore
```

### Propagate Errors When You Cannot Handle Them Locally

If your function cannot meaningfully recover from an error, propagate it to the caller with `try` (no `otherwise`).

```maxon
function loadConfig(path String) returns Config throws FileError
	let contents = try readFile(path)    ' propagates FileError
	return parseContents(contents)
end 'loadConfig'
```

### Use `panic` for Programming Errors, `throw` for Expected Failures

`panic` terminates the program immediately. Use it only for invariant violations and unreachable states. Use `throw` for conditions the caller might reasonably want to handle.

```maxon
' panic: this should never happen, indicates a bug
function getElement(index Index) returns Element
	if index >= self.length 'overflow'
		panic("getElement: index {index} >= length {self.length}")
	end 'overflow'
	return self.data.get(index)
end 'getElement'

' throw: the caller can decide what to do
function findUser(name String) returns User throws LookupError
	if not users.containsKey(name) 'missing'
		throw LookupError.notFound
	end 'missing'
	return try users.get(name) otherwise User{}
end 'findUser'
```

### Define Domain-Specific Error Types

Create error unions that describe failures in your domain. This produces clearer error messages and enables precise error handling.

```maxon
union ConfigError implements Error
	fileNotFound
	invalidSyntax
	missingRequiredField
	valuOutOfRange
end 'ConfigError'
```

---

## Variables and Mutability

### Default to `let` for Immutability

Use `let` unless you need to reassign the variable. This communicates intent and prevents accidental mutation.

```maxon
let maxRetries = 3               ' constant, never changes
let userName = getUserName()     ' assigned once from a function

var retryCount = 0               ' needs to be incremented
var buffer = ByteArray{}         ' needs push/set operations
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

When you do not need a value (loop variable, tuple element), use `_` to signal this clearly. In match arms, prefer omitting the binding entirely over using `(_)` when you don't care about the associated value.

```maxon
' Discard the index when only the element matters
for (_, name) in entries.enumerated() 'loop'
	print("{name}\n")
end 'loop'

' Discard an impure function's result
let _ = incrementCounter()

' In matches, omit the binding entirely if you don't need the associated value
match result 'check'
	success then return true
	failure then return false
end 'check'

' Use (_) only when you want to emphasize that a value is being ignored
match result 'check'
	success(_) then return true
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
items.sort((a, b) gives a.priority - b.priority)
let names = users.map((u) gives u.name)
```

### Handle Pure vs Impure Return Values Correctly

The compiler tracks purity. Pure function results must always be used. Impure function results must be explicitly discarded with `let _ =` if unused.

```maxon
' Pure: result must be used
let doubled = double(5)

' Impure: explicitly discard if unused
let _ = incrementAndLog()

' Chainable methods: result can be discarded freely
builder.addField("name")
```

---

## Pattern Matching

### Prefer `match` Over Chained `if` for Unions and Enums

`match` guarantees exhaustiveness. If a new case is added to a union, the compiler forces you to handle it.

```maxon
' Good: exhaustive, compiler-checked
match direction 'navigate'
	north then moveUp()
	south then moveDown()
	east then moveRight()
	west then moveLeft()
end 'navigate'

' Avoid for unions: error-prone, not exhaustive
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

When you intentionally handle only a subset of cases, use `default throws` (for recoverable situations) or `default panic` (for bugs).

```maxon
' Recoverable: caller decides what to do
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

This is preferable to `default throws` or `default panic` because the unhandled cases are not errors — they are simply not relevant. The range pattern still participates in exhaustiveness checking, so if a new case is added to the union, the compiler will tell you whether it falls inside or outside the range.

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
var results = ResultArray{}
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
var scores = ScoreArray{}

' Map: fast lookup by key
var userCache = UserMap{}

' Set: track unique items
var visited = CitySet{}

' List: queue with O(1) push/pop at both ends
var taskQueue = TaskList{}
```

### Iterate Collections Directly

Use `for...in` loops. Use `.enumerated()` when you need the index alongside the element.

```maxon
' Direct iteration
for name in names 'greet'
	print("Hello, {name}\n")
end 'greet'

' With index
for (i, name) in names.enumerated() 'list'
	print("{i + 1}. {name}\n")
end 'list'
```

---

## Types and Interfaces

### Use `export` Selectively

Export only the public API. Keep internal helpers, fields, and utility methods private. This reduces coupling and makes refactoring safer.

```maxon
export type Parser
	var source String           ' private field
	export var position Count   ' public field

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
	var host String
	var port Port
	var timeout Milliseconds

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
		return Classifier._whitespace.contains(c)
	end 'isWhitespace'
end 'Classifier'
```

### Implement Standard Interfaces

Implement `Hashable`, `Equatable`, `Cloneable`, `Stringable`, and `Comparable` where appropriate. This enables your types to work with collections, comparisons, string interpolation, and sorting.

```maxon
type Coordinate implements Hashable, Equatable, Stringable, Cloneable
	export var lat MathValue
	export var lon MathValue

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

for (i, field) in fields.enumerated() 'validateFields'
	if not isValid(field) 'invalid'
		throw ValidationError.invalidField
	end 'invalid'
end 'validateFields'
```
