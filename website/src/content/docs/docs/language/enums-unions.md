---
title: Enums & Unions
description: Simple and struct-backed enums, raw-value enums, and tagged unions with pattern matching.
sidebar:
  order: 5
---

Enums define a fixed set of named constants with optional raw values (int, float, string, char, struct). Enums auto-implement `Equatable` and `Hashable`, and support `==`/`!=` comparison. Enums do NOT support associated values -- use `union` for that.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
		north
		south
		east
		west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
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

### Enum as Function Parameter

Enums can be used as function parameters and return types:

```maxon
enum Status
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

### Creating Enums from Names (`fromName`)

The `fromName` static method creates an enum value from a string name. It throws `EnumError.invalidName` if the name doesn't match any case:

```maxon
enum Direction
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

**Notes:**
- Returns `throws EnumError`, use with `try...otherwise` or `try...catch`
- Compile-time literal names are validated at compile time

### Struct-Backed Enums

Enums can be backed by a struct type, associating compile-time constant metadata with each case. Access the backing struct via `.rawValue`:

```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
end 'OpMeta'

enum Instruction
	add = OpMeta{latency: 1, isMemory: false}
	load = OpMeta{latency: 4, isMemory: true}
	store = OpMeta{latency: 3, isMemory: true}
end 'Instruction'

let op = Instruction.load
let lat = op.rawValue.latency     // 4
let mem = op.rawValue.isMemory    // true
```

**Notes:**
- All cases must use the same struct type
- Every case must provide a backing value (no bare cases)
- Struct field values must be compile-time constants (integers, floats, booleans, enum member references like `Priority.high`, or top-level constants)
- At runtime, the enum is stored as an ordinal; `.rawValue` constructs the backing struct

### Enum Interface Conformance

Enums can conform to interfaces using the `implements` keyword, similar to types:

```maxon
enum FileError implements Error
		notFound
		permissionDenied
		alreadyExists
end 'FileError'

enum HttpError int implements Error
		badRequest = 400
		notFound = 404
		serverError = 500
end 'HttpError'
```

**Notes:**
- The `implements Interface` clause comes after the optional backing type
- Multiple interfaces can be specified: `enum Foo implements A, B`
- The `Error` interface can only be implemented by enums or unions (not types/structs)

---

## Unions

Unions define a type with a fixed set of named cases that can carry optional associated values. Unions do NOT implement `Equatable` or `Hashable`, do not support `==`/`!=` comparison. Use `match` to inspect union values.

Unions support `.name` (returns the case name as a `String`) and `.ordinal` (returns the zero-based declaration position). Unions also have a static `.allCaseNames` property returning an `Array with String` of the case names in declaration order. `.allCases` is not available on unions because cases may carry associated values; use `.unionCases` to access the discriminant as a first-class enum (see [Union Cases](#union-cases-discriminant-as-an-enum) below).

Unions can additionally have a per-variant struct backing — see [Struct-Backed Unions](#struct-backed-unions) below.

### Simple Unions

The simplest form of union defines named cases with no additional data:

```maxon
union Option
		some(value int)
		none
end 'Option'
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
		none gives 0
		some(n) gives n * 2
end 'get'
```

You can mix cases with and without bindings:

```maxon
match result 'check'
		success(v) then return v    // Extracts value
		pending then return 0       // No extraction needed
end 'check'
```

**Discarding associated values:** When you don't need the associated value, omit the parentheses entirely:

```maxon
match container 'check'
		some then return 1        // omit parentheses to ignore associated value
		none then return 0
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

Union values cannot be compared using `==` or `!=` (error E3066). The only way to inspect a union value is through `match`. This restriction exists to prevent a class of bugs that happen when a new case is added that is unaccounted for and code that handles the union either falls through or uses a default value that is wrong.

```maxon
// ERROR E3066: Cannot compare union values with ==
// if r1 == r2 'check' ... end 'check'

// Use match instead
match result 'check'
		success(v) then handleSuccess(v)
		failure(c, msg) then handleFailure(c, msg: msg)
		pending then handlePending()
end 'check'
```

### Creating Unions from Names (`fromName`)

The `fromName` static method creates a union value from a string name. It throws `EnumError.invalidName` if the name doesn't match any case:

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
- Returns `throws EnumError`, use with `try...otherwise` or `try...catch`
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

### Union Interface Conformance

Unions can conform to interfaces using the `implements` keyword:

```maxon
union FileError implements Error
		notFound
		permissionDenied(path String)
end 'FileError'
```

**Notes:**
- The `Error` interface can be implemented by enums or unions (not types/structs)

### Struct-Backed Unions

Each union variant can be tagged with a compile-time struct value, the same shape as struct-backed enums. Use `.rawValue` on a union value to read the variant's backing struct. The variant's associated values are independent of the backing struct — they coexist, with the payload accessed by `match` and the metadata accessed by `.rawValue`.

```maxon
typealias Latency = int(0 to 50)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
end 'OpMeta'

union MirOp
	movImm(dest VarSlot, value MachineWord) = OpMeta{latency: 1, isMemory: false}
	load(dest VarSlot, addr VarSlot)        = OpMeta{latency: 4, isMemory: true}
	store(addr VarSlot, src VarSlot)        = OpMeta{latency: 3, isMemory: true}
end 'MirOp'

let op = MirOp.load(dest: d, addr: a)
let lat = op.rawValue.latency     // 4
let mem = op.rawValue.isMemory    // true
```

This is the union analogue of [Struct-Backed Enums](#struct-backed-enums). Use it to carry per-variant compile-time metadata (memory/store/call flags, instruction latency, scheduling hints, inliner policy bits) without writing exhaustive match expressions in every consumer — readers query the backing struct directly via `.rawValue.field`.

**Notes:**
- All variants must use the same backing struct type
- Every variant must provide a backing value (no bare-tagged variants in a backed union)
- Backing struct field values must be compile-time constants (integers, floats, booleans, enum member references like `OpPattern.passThrough`, or top-level constants)
- At runtime, the union discriminant is stored as an ordinal; `.rawValue` constructs the backing struct on demand

### Union Cases (Discriminant as an Enum)

Every `union` has a compiler-synthesized companion type `U.unionCases` — a simple integer-backed enum with one bare case per variant of `U`, in declaration order. It is the union's discriminant exposed as a first-class enum value.

```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(radius Integer)
	square(side Integer)
	point
end 'Shape'

// Shape.unionCases is conceptually:
//   enum Shape.unionCases
//     circle    // rawValue 0
//     square    // rawValue 1
//     point     // rawValue 2
//   end
```

Because `U.unionCases` is a regular enum it inherits all of the standard enum machinery: `.allCases`, `.allCaseNames`, `.rawValue`, `.fromRawValue`, `.fromName`, `.name`, and `.ordinal`. Match arms over a `U.unionCases` value are exhaustiveness-checked, just like match arms over the union itself.

The intended use is symmetric (de)serialization: write the variant's `rawValue` to a buffer alongside its payload; on read, lift the raw integer back to a `U.unionCases` via `fromRawValue` and match on it to dispatch the payload reader. Match arms are single-statement, so multi-step writers and readers extract per-variant helpers:

```maxon
function writeShapeCircle(buf ByteArray, radius Integer)
	writeDword(buf, value: Shape.unionCases.circle.rawValue)
	writeQword(buf, value: radius)
end 'writeShapeCircle'

function writeShapeSquare(buf ByteArray, side Integer)
	writeDword(buf, value: Shape.unionCases.square.rawValue)
	writeQword(buf, value: side)
end 'writeShapeSquare'

function writeShape(buf ByteArray, value Shape)
	match value 'tag'
		circle(r) then writeShapeCircle(buf, radius: r)
		square(s) then writeShapeSquare(buf, side: s)
		point then writeDword(buf, value: Shape.unionCases.point.rawValue)
	end 'tag'
end 'writeShape'

function readShapeCircle(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let radius = readQword(buf, offset: offset)
	return (Shape.circle(radius), offset + 8)
end 'readShapeCircle'

function readShapeSquare(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let side = readQword(buf, offset: offset)
	return (Shape.square(side), offset + 8)
end 'readShapeSquare'

function readShape(buf ByteArray, offset ByteOffset) returns (Shape, ByteOffset)
	let raw = readDword(buf, offset: offset)
	let pos = offset + 4
	let kase = try Shape.unionCases.fromRawValue(raw) otherwise panic("corrupt cache: unknown Shape tag {raw}")
	match kase 'tag'
		circle then return readShapeCircle(buf, offset: pos)
		square then return readShapeSquare(buf, offset: pos)
		point then return (Shape.point, pos)
	end 'tag'
end 'readShape'
```

Both `match` statements are exhaustiveness-checked: the writer matches over a `Shape` value, the reader matches over a `Shape.unionCases` value. Adding a new variant to `Shape` automatically extends `Shape.unionCases`, which produces non-exhaustive-match errors in *both* the writer and reader. There is no path to a compiling-but-broken codec.

**Notes:**
- `.unionCases` raw values are declaration ordinals (0, 1, 2, ...). Reordering variants of `U` changes the on-disk format if `rawValue` is being persisted; treat serialized unions as append-only.
- Plain `enum` types do not need `.unionCases` — they already are their own discriminant and expose `.allCases` / `.fromRawValue` directly.

---

## Enums (Raw-Value Enums)

Enums without associated values define a named group of typed constant values. They support direct `==` and `!=` comparison and provide `.rawValue`, `.name`, `.ordinal`, `.allCases`, `.allCaseNames`, `fromRawValue()`, and `fromName()`.

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

Enums support integer, float, String, Character, struct, and function backing types.

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

Struct backing attaches compile-time constant metadata to each case. Field values must be compile-time constants (integers, floats, booleans) or nested struct literals:

```maxon
type OpInfo
		export let latency as int(0 to 100)
		export let throughput as int(0 to 10)
end 'OpInfo'

enum Instruction
		add = OpInfo{latency: 1, throughput: 2}
		mul = OpInfo{latency: 3, throughput: 1}
		div = OpInfo{latency: 40, throughput: 1}
end 'Instruction'

let lat = Instruction.div.rawValue.latency  // 40
```

At runtime, struct-backed enums are stored as ordinals. The struct is reconstructed on `.rawValue` access. `fromRawValue()` is not available for struct-backed enums.

Function backing attaches a top-level function reference to each case. All cases must share the same function signature, which becomes the enum's backing type. The function may be declared later in the same file or in a different file; the binding is resolved after every file's top-level declarations have been pre-scanned.

```maxon
typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
		return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
		return x * 3
end 'tripleFn'

enum Op
		doubleOp = doubleFn
		tripleOp = tripleFn
end 'Op'

let f = Op.doubleOp.rawValue
let r = f(21)   // 42
```

At runtime, function-backed enums are stored as ordinals; `.rawValue` lowers to a select chain that recovers the function pointer for the live case. `fromRawValue()` is not available for function-backed enums.

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

Enums without associated values allow direct `==` and `!=` comparison:

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

Enum matches require exhaustive case coverage — all cases must be matched by explicit patterns or range patterns. Match arms use bare case names (unqualified); using qualified `Type.case` syntax in a match arm is a compile error (E3075). Plain `default` is not allowed; use `default throws` or `default panic("message")` if you want a catch-all:

```maxon
// Exhaustive: all cases listed
var result = match s 'handle'
		ok gives 1
		notFound gives 2
		serverError gives 3
end 'handle'
```

Range patterns use bare case names as bounds, based on ordinal values. `to` is inclusive, `upto` excludes the upper bound. Qualified `Type.case` syntax in match arms is a compile error (E3075):

```maxon
match p 'check'
    low to medium then print("not urgent")
    high to critical then print("urgent")
end 'check'
```

Overlapping patterns (ranges that cover the same case, or an explicit case within a range) are reported as errors.

### Raw Value Access

All enums support `.rawValue`. Simple enums return the case ordinal (0, 1, 2…); backed enums return the explicit value, typed by the backing kind — `int` for int-backed, `float` for float-backed, `String` for string-backed, and `Character` for char-backed:

```maxon
var c = Color.green
var ordinal = c.rawValue   // 1

var s = HttpStatus.notFound
var code = s.rawValue      // 404 (int)
```

For string- and char-backed enums, `.rawValue` returns the declared backing literal as a `String` / `Character` (the value is reconstructed from the ordinal at runtime):

```maxon
enum Status
	active = "ACTIVE"
	closed = "CLOSED"
end 'Status'

var st = Status.active
var raw = st.rawValue          // "ACTIVE" (String)
if st.rawValue == "ACTIVE" 'check'   // String/char-backed enums compare against their scalar peer
	// ...
end 'check'
```

A string-backed enum value compares with `==` / `!=` against a `String` (and a char-backed value against a `Character`) by comparing the declared backing literal; both sides may also be the same enum type.

### Name Access

All enums have a `.name` property returning the case name as a `String`. For backed enums, `.name` always returns the case name, not the raw value:

```maxon
var s = HttpStatus.notFound
var code = s.rawValue   // 404
var n = s.name          // "notFound"
```

### Ordinal Access

All enums have an `.ordinal` property returning the zero-based declaration position as an `int`. For simple enums, `.ordinal` is identical to `.rawValue`. For backed enums, `.ordinal` is the position in declaration order, not the backing value:

```maxon
var c = Color.green
var pos = c.ordinal    // 1 (same as .rawValue for simple enums)

var s = HttpStatus.notFound
s.ordinal              // 1 (second case in declaration order)
s.rawValue             // 404 (backing value)
s.name                 // "notFound"
```

`.ordinal` is available on all enum backing types (int, float, string, char).

### All Cases (`allCases`)

All enums have a static `.allCases` property that returns an `Array` containing all cases in declaration order:

```maxon
for color in Color.allCases 'loop'
	print("{color.name}\n")
end 'loop'
// Prints: red, green, blue

var count = Color.allCases.count()  // 3
```

`.allCases` works with all backing types (simple, int, float, string, char).

### All Case Names (`allCaseNames`)

All enums and unions have a static `.allCaseNames` property returning an `Array with String` of the case names in declaration order:

```maxon
for name in Color.allCaseNames 'loop'
	print("{name}\n")
end 'loop'
// Prints: red, green, blue

var count = Color.allCaseNames.count()  // 3
```

Unlike `.allCases`, `.allCaseNames` is available on unions too — even unions whose cases carry associated values — because only the case name strings are returned.

### Converting from Raw Value (`fromRawValue`)

The `fromRawValue` static method converts a raw value to an enum case. It throws `EnumError.invalidRawValue` if no case matches:

```maxon
var s = try HttpStatus.fromRawValue(404) otherwise HttpStatus.ok  // HttpStatus.notFound
```

For string- and char-backed enums, `fromRawValue` takes the backing literal type (`String` / `Character`) and matches against each case's declared backing literal:

```maxon
var st = try Status.fromRawValue("CLOSED") otherwise Status.active  // Status.closed
```

`fromRawValue` is not available for struct-backed or function-backed enums (their raw value can't be reconstructed from a literal lookup).

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
- Both `fromRawValue` and `fromName` throw; use `try...otherwise` or `try...catch`
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

Keywords can be used as enum case names:

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
