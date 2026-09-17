---
title: Enums & Unions
description: Simple and struct-backed enums, raw-value enums, and tagged unions with pattern matching.
sidebar:
  order: 5
---

## Enums

An `enum` declares a fixed set of named cases. Enum cases carry no associated data — a type whose cases
carry data is a [union](#unions). A case may have a **raw value** (see
[Raw-Value Enums](#raw-value-enums)).

### Simple Enums

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	let dir = Direction.north
	if dir == Direction.north 'up'
		print("{dir.name}\n")      // north
	end 'up'

	return 0
end 'main'
```

A case is written `Type.case`. Enum values compare with `==` and `!=`, and every payload-free enum is
automatically `Equatable` and `Hashable`, so it can be a `Map` key or `Set` element. Inside `match` arms,
cases are written bare (see [Match Statement](/docs/language/statements/#match-statement)).

### Enum Methods

An enum can declare instance methods after its cases. Inside one, `self` is the case value; inspect it with
`match self`. `Self.case` names a case of the enclosing enum.

```maxon
enum Direction
	north
	south

	export function opposite() returns Direction
		return match self 'flip'
			north gives Self.south
			south gives Self.north
		end 'flip'
	end 'opposite'
end 'Direction'

function main() returns ExitCode
	print("{Direction.north.opposite().name}\n")    // south
	return 0
end 'main'
```

- A method carries its own visibility — `export`, `module` or `public` — and is file-private without one,
  whatever the enum's own visibility. Calling a private method from another file is **E3008**.
- An enum declares no fields, so `self.something` inside a method is **E2015**; use `match self`.
- `static function` is not supported on an enum (**E2015**).

### Enum Properties

Every enum case has:

| Property | Result |
|----------|--------|
| `.name` | the case name as a `String` |
| `.ordinal` | the zero-based declaration position |
| `.rawValue` | the case's raw value (the ordinal when none is declared) |

and every enum type has:

| Member | Result |
|--------|--------|
| `Type.allCases` | an `Array` of every case, in declaration order |
| `Type.allCaseNames` | an `Array with String` of every case name |
| `Type.fromName(name)` | the case with that name; throws `noSuchCaseName` when none matches |
| `Type.fromRawValue(raw)` | the case with that raw value; throws `noSuchRawValue` when none matches |

```maxon
enum Color
	red
	green
	blue
end 'Color'

function lookup(name String) returns Color
	return try Color.fromName(name) otherwise Color.red
end 'lookup'

function main() returns ExitCode
	for color in Color.allCases 'each'
		print("{color.name}={color.ordinal} ")      // red=0 green=1 blue=2
	end 'each'

	print("\n{lookup("blue").name} {lookup("purple").name}\n")   // blue red
	return 0
end 'main'
```

A literal name or raw value that matches no case is caught at compile time (**E3034**). Both lookups throw,
so they need `try`.

### Struct-Backed Enums

A case's raw value can be a record of compile-time constants, which attaches metadata to each case. Read
it through `.rawValue`:

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

function main() returns ExitCode
	let op = Instruction.load
	print("{op.rawValue.latency} {op.rawValue.isMemory}\n")    // 4 true
	return 0
end 'main'
```

- Every case uses the same struct type and provides a value.
- Field values are compile-time constants: numbers, booleans, enum cases, top-level constants.
- The enum is stored as its ordinal; `.rawValue` builds the record on demand. `fromRawValue` is not
  available for a struct-backed enum.

### Enum Interface Conformance

An enum can declare conformances after its name:

```maxon
enum FileError implements Error
	notFound
	permissionDenied
end 'FileError'

enum HttpError implements Error
	badRequest = 400
	notFound = 404
end 'HttpError'
```

The header is only `enum Name` and an optional `implements` clause; the backing type is inferred from the
raw values, never written. Anything else on the header line — `enum Colour int` — is **E2001**
(`unexpected token: 'int'`), and the same holds for a `union` header.

## Raw-Value Enums

A raw value gives each case a constant: an integer, a float, a string, a character, a record, or a
function.

### Declaration

```maxon
enum HttpStatus
	ok = 200
	notFound = 404
	serverError = 500
end 'HttpStatus'
```

Cases without a value count up from 0, or from the previous explicit integer value plus one. Negative values
are allowed:

```maxon
enum Priority
	low          // 0
	medium       // 1
	high = 10
	critical     // 11
end 'Priority'

enum Temperature
	cold = -10
	freezing = 0
	warm = 25
end 'Temperature'
```

Counting up applies only to integer raw values; a case without a value beside non-integer values is an
error.

### Backing Types

The raw values decide the backing type:

```maxon
enum Threshold
	low = 0.1
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

`.rawValue` has the backing type — `ContentType.json.rawValue` is the `String` `"application/json"`.
[Struct-backed enums](#struct-backed-enums) attach a record per case.

**Function backing** attaches a function to each case; all cases share one signature, and `.rawValue` is
the function value:

```maxon
typealias Operand = int(i64.min to i64.max)

function doubleFn(x Operand) returns Operand
	return x * 2
end 'doubleFn'

function tripleFn(x Operand) returns Operand
	return x * 3
end 'tripleFn'

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function main() returns ExitCode
	let f = Op.tripleOp.rawValue
	print("{f(14)}\n")     // 42
	return 0
end 'main'
```

`fromRawValue` is available for integer, float, string and character backings, not for struct or function
backings.

### Comparison

Cases compare with `==` and `!=`. A string-backed case also compares directly with a `String`, and a
character-backed case with a `Character`, by its raw value:

```maxon
enum ContentType
	json = "application/json"
	html = "text/html"
end 'ContentType'

function isJson(c ContentType) returns bool
	return c == "application/json"
end 'isJson'
```

Comparing `.rawValue` itself with `==` is **E3097** — compare the case (`value == Type.case`) instead.

### Match

A match on an enum names every case, bare (**E2026** lists any that are missing). `Type.case` in an arm is
**E3075**, a plain `default` arm is **E2046**, and covering a case twice is **E2027**. An arm covering
several cases lists them with `or`, one per line:

```maxon
enum Priority
	low
	medium
	high = 10
	critical
end 'Priority'

function urgency(p Priority) returns String
	return match p 'check'
		low or
			medium gives "not urgent"
		high or
			critical gives "urgent"
	end 'check'
end 'urgency'
```

### Implicit Coercion to the Backing Primitive

A case of a simple, integer-backed or float-backed enum is used as its raw value wherever a number is
expected — a function argument, a collection element, a comparison, a `return`, a struct field — with no
`.rawValue`:

```maxon
enum JsonByte
	lBracket = 0x5B
	space = 0x20
end 'JsonByte'

function main() returns ExitCode
	var out = ByteArray.create()
	out.push(JsonByte.lBracket)                     // pushes 0x5B
	out.push(JsonByte.space)
	let first = try out.get(0) otherwise 0
	print("{first == JsonByte.lBracket}\n")         // true
	return 0
end 'main'
```

String-, character-, struct- and function-backed enums do not coerce; use `.rawValue`.

### Keywords as Case Names

A keyword can be a case name, since cases are always written qualified (`TokenKind.end`) or bare inside a
match arm:

```maxon
enum TokenKind
	function
	return
	end
	if
end 'TokenKind'
```

### Error Conditions

| Code | Cause |
|------|-------|
| E3030 | duplicate case name |
| E3031 | duplicate raw value |
| E3032 | raw values of different backing types in one enum |
| E3034 | an unknown case (`Color.purple`), or a literal `fromName`/`fromRawValue` argument that matches no case |
| E3097 | comparing `.rawValue` with `==` |

## Unions

A `union` declares a fixed set of cases, each of which may carry **associated values**. A value of a union
is exactly one case with its payload, and `match` is how you read it.

```maxon
typealias Amount = int(i64.min to i64.max)

union Outcome
	success(value Amount)
	failure(code Amount, message String)
	pending
end 'Outcome'

function describe(r Outcome) returns String
	return match r 'show'
		success(v) gives "ok {v}"
		failure(code, message) gives "failed {code}: {message}"
		pending gives "waiting"
	end 'show'
end 'describe'

function main() returns ExitCode
	print("{describe(Outcome.success(42))}\n")
	print("{describe(Outcome.failure(404, message: "not found"))}\n")
	print("{describe(Outcome.pending)}\n")
	return 0
end 'main'
```

### Constructing Cases

A case with a payload is constructed like a call — first argument positional, the rest named after the
payload fields: `Outcome.failure(404, message: "not found")`. A case without a payload is written like an
enum case: `Outcome.pending`.

Payloads may be integers, booleans, strings, records, other unions and collections. A `float` payload is
not supported yet (**E2015**).

### Pattern Matching

In a `match`, `caseName(a, b)` binds the payload for that arm; the bindings are local to the arm.

- The number of bindings matches the case's payload fields.
- Discard one binding with `_`: `failure(_, message)`.
- To ignore the whole payload, omit the parentheses: `success then …`. Writing `success(_)` with every
  binding discarded is **E3081**.
- An `or`-chain can list payload cases bare; their payloads are not accessible in that arm.
- A match on a union must cover every case (**E2026**); use `default throws` or `default panic(…)` for a
  deliberate catch-all (see [Statements](/docs/language/statements/#default-throws-and-default-panic)).

### Mutable Match Bindings

When the matched value is a `var` (or a union parameter the function may write), assigning to a binding
writes back into the union:

```maxon
typealias Amount = int(i64.min to i64.max)

union Box
	empty
	full(value Amount)
end 'Box'

function main() returns ExitCode
	var b = Box.full(10)
	match b 'update'
		full(value) then value = 42
		empty then return 1
	end 'update'

	match b 'read'
		full(value) then print("{value}\n")    // 42
		empty then print("empty\n")
	end 'read'

	return 0
end 'main'
```

When the matched value is a `let`, the bindings are immutable and assigning to one is **E2013**.

### Comparing Union Values

Unions have no `==` or `!=` (**E3066** `cannot compare union values using '==', use 'match' instead`).
Inspecting a union through `match` means that adding a case later flags every place that must decide what
to do with it. For the same reason unions are not automatically `Equatable` or `Hashable`; a union may
still declare `implements Equatable` with its own `equals` method and call it explicitly.

### Union Properties

A union value has `.name`, `.ordinal` and `.rawValue`, and a union type has `Type.allCaseNames` and
`Type.fromName(name)`. `fromName` takes the payload as extra arguments when the name is a literal
(`Container.fromName("value", 42)`); with a run-time string it only produces cases without a payload.
There is no `.allCases`, because a payload case has no single value — use `.unionCases` below.

A case can declare an explicit integer tag, which becomes its `.rawValue` (the `.ordinal` is still the
declaration position):

```maxon
typealias Id = int(0 to 255)

union Instr
	add(dest Id, src Id) = 5
	neg(dest Id) = 9
end 'Instr'
```

### Union Methods

A union can declare instance methods; inspect `self` with `match`:

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	point

	export function area() returns Amount
		return match self 'calc'
			circle(r) gives 3 * r * r
			square(s) gives s * s
			point gives 0
		end 'calc'
	end 'area'
end 'Shape'

function main() returns ExitCode
	print("{Shape.square(4).area()}\n")    // 16
	return 0
end 'main'
```

As with enums, a method carries its own visibility, and `static function` is not supported.

### Union Interface Conformance

```maxon
typealias HttpCode = int(100 to 599)

union FetchError implements Error
	notFound
	status(code HttpCode)
end 'FetchError'
```

Unions and enums are the types that can be thrown (see
[Error Handling](/docs/language/error-handling/#defining-error-types)).

### Struct-Backed Unions

Each case can also carry a compile-time record, exactly like a
[struct-backed enum](#struct-backed-enums). The payload and the record are
independent: `match` reads the payload and `.rawValue` reads the record.

```maxon
typealias Latency = int(0 to 50)
typealias Slot = int(0 to 255)

type OpMeta
	export let latency as Latency
	export let isMemory as bool
end 'OpMeta'

union MachineOp
	movImm(dest Slot, value Slot) = OpMeta{latency: 1, isMemory: false}
	load(dest Slot, addr Slot) = OpMeta{latency: 4, isMemory: true}
	store(addr Slot, src Slot) = OpMeta{latency: 3, isMemory: true}
end 'MachineOp'

function main() returns ExitCode
	let op = MachineOp.load(1, addr: 2)
	print("{op.rawValue.latency} {op.rawValue.isMemory}\n")    // 4 true
	return 0
end 'main'
```

Every case uses the same record type and provides a value of compile-time constants.

### Union Cases (Discriminant as an Enum)

Every union `U` has a companion enum `U.unionCases` with one bare case per union case, in declaration
order. It is an ordinary enum — `.allCases`, `.allCaseNames`, `.fromRawValue`, `.fromName`, `.name`,
`.ordinal`, `.rawValue` — and a `match` over it is exhaustiveness-checked.

That makes serialization safe to extend: write a case's `rawValue` next to its payload, and on reading, lift
the stored tag back with `fromRawValue` and `match` on it. Adding a case to `U` adds it to `U.unionCases`,
so both the writer's and the reader's `match` stop compiling until they handle it.

```maxon
typealias Amount = int(i64.min to i64.max)

union Shape
	circle(radius Amount)
	square(side Amount)
	point
end 'Shape'

function kindOf(tag Amount) returns String
	let kind = try Shape.unionCases.fromRawValue(tag) otherwise panic("unknown Shape tag {tag}")
	return match kind 'kind'
		circle gives "circle"
		square gives "square"
		point gives "point"
	end 'kind'
end 'kindOf'

function main() returns ExitCode
	let s = Shape.square(3)
	print("{kindOf(s.rawValue)}\n")    // square
	return 0
end 'main'
```

The tags are declaration positions unless a case declares its own, so reordering cases changes stored
tags; treat a persisted union as append-only.
