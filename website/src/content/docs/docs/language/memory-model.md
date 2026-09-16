---
title: Memory Model
description: Reference-by-default semantics, cloning, auto-equatable, scope cleanup, and code generation.
sidebar:
  order: 15
---

## Memory Model

Maxon manages memory with **reference counting** decided at compile time. There is no garbage collector and
no manual `free`: a record is released when its last reference goes away, and the compiler proves at compile
time that no reference is used after that.

### Values and Records

- **Scalars** — integers, floats, `bool`, payload-free enum values — are plain values. Assigning one copies
  it.
- **Records** — values of a `type`, tuples, unions with payloads, strings, arrays and other collections —
  live on the heap. A variable holds a **reference** to its record.

### Reference-by-Default Assignment

Storing a record somewhere else shares it rather than copying it, so a field write through one holder is
visible through every other:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var a = Point.create(1, y: 2)
	var points = PointArray.create()
	points.push(a)                                        // the array shares a's record
	var b = try points.get(0) otherwise panic("pushed")
	b.x = 99
	print("{a.x}\n")                                      // 99: a and b are one record

	b = Point.create(5, y: 6)                             // rebinding b leaves a alone
	a = Point.create(7, y: 8)
	points.push(a)
	print("{points.count()}\n")                           // 2
	return 0
end 'main'
```

Reassigning a variable rebinds it to another record without touching the old one. Use
[`clone()`](#explicit-cloning) when you need an independent copy.

### Immutable XOR Mutable

A record is either **written through mutable holders** or **read through immutable names** — never both at
once. The compiler enforces this, which is what makes sharing by reference safe.

**Immutable names** are `let` bindings (including `for` loop variables and `if let` bindings), module-level
`let` constants, and parameters the function does not write. **Mutable holders** are `var` bindings, fields,
container elements and parameters the function writes. Several holders may share a record: storing it into a
field or container, or passing it to a callee that keeps it, adds a reference.

The checks are by **liveness**: an immutable name matters only while some later statement still reads it.

| Situation | Result |
|-----------|--------|
| `var b = a` where `a` is a `let` that is its record's only reference | `a` **moves** into `b`; reading `a` afterwards is **E3102** |
| `var b = a` where the record is also named by another live `let`, or `a` is a field of an immutable name (`var i = o.inner`) | **E3078** |
| a `var` bound to a value that may be a live `let`'s record — a call result, a container element — and then written | **E3078** |
| a field write, or a method that writes its receiver, on a record a live `let` may still read | **E3159** |
| a `let`, or a value that may be a live `let`'s record, passed to a parameter the callee writes | **E3019** |
| mutating a collection while a `let` borrowed from it is live | **E3070** (see [Borrow Checking](#borrow-checking)) |

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord
	export var y as Coord

	export static function create(x Coord, y Coord) returns Point
		return Point{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	var arr = PointArray.create()
	arr.push(a)                     // arr co-owns a's record
	var b = a                       // a moves into b
	b.x = 5
	// print("{a.x}\n")             // E3102: use of moved value 'a'

	let first = try arr.get(0) otherwise panic("pushed")
	// var e = try arr.get(0) otherwise panic("pushed")
	// e.x = 9                      // E3078: 'get' may share storage with immutable variable 'first'
	var e = (try arr.get(0) otherwise panic("pushed")).clone()
	e.x = 9                         // fine: an independent record
	print("{first.x} {b.x} {e.x}\n")   // 5 5 9
	return 0
end 'main'
```

Some consequences:

- A `var` that is only rebound and read — walking a chain with `cur = try cur.next()` — writes nothing and is
  never refused.
- A method writing a field of its **own** receiver is not a parameter write: `let acc = Accumulator.create()`
  then `acc.add(10)` is legal.
- A record whose type declares every field `let` can never be written, so none of these checks apply to it.
- `String` and `Character` values a name does not own are copied when bound to a `var` or stored.
- A `let` lent to a [service](/docs/language/async/#services--spawn) is frozen from the send onwards
  (**E3160**).
- The analysis tracks records made and used within one function. Two fields of a record received from
  elsewhere — a parameter, module storage — are assumed to hold different records.

### Explicit Cloning

`clone()` returns an independent deep copy:

```maxon
typealias Coord = int(i64.min to i64.max)

type Point
	export var x as Coord

	export static function create(x Coord) returns Point
		return Point{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1)
	var b = a.clone()
	b.x = 99
	print("{a.x} {b.x} {a is b}\n")    // 1 99 false
	return 0
end 'main'
```

`clone()` comes from the `Cloneable` interface (`function clone() returns Self`). The compiler generates it
for any type whose fields are all cloneable; primitives, `String`, and collections of cloneable elements are
cloneable. Declare `clone()` yourself for custom behaviour, or when a field's type is not cloneable.

### Borrow Checking

A `let` bound to an element read out of a mutable collection **borrows** from that collection. While the
borrow is live, the collection cannot be mutated — directly or through a function that mutates it:

```maxon
function main() returns ExitCode
	var arr = ["hello"]
	let s = try arr.get(0) otherwise ""
	// arr.push("world")      // E3070: cannot mutate 'arr' via 'push' while it is borrowed by 's'
	print("{s}\n")            // the borrow ends at its last use...
	arr.push("world")         // ...so this is fine
	print("{arr.count()}\n")
	return 0
end 'main'
```

Borrows end at the borrowing variable's **last use**, not at the end of its scope.

### Stack Promotion

A record that never escapes its function — never returned, stored in a field or container, captured by a
closure, or passed to a callee that keeps it — and whose fields are all scalars is allocated on the stack
instead of the heap, with no reference counting. This is automatic and does not change behaviour.
`@heap let p = Point.create(1)` forces heap allocation for one declaration.

### Scope Cleanup

When a reference goes out of scope the record's count drops, and a record whose count reaches zero is freed
along with everything it holds — a container releases each element, a record each field. A returned record
is handed to the caller rather than freed.

```maxon
typealias TokenId = int(0 to 1000)

type Token
	export let id as TokenId

	export static function create(id TokenId) returns Token
		return Token{id: id}
	end 'create'
end 'Token'

typealias TokenList = List with Token

function countTokens() returns TokenId
	var list = TokenList.create()
	list.append(Token.create(1))
	list.append(Token.create(2))
	return list.count()             // list, then both tokens, are freed on return
end 'countTokens'
```

A type cannot contain itself (**E4014**), so reference cycles between records cannot be built and reference
counting reclaims everything.

### The Leak Checker

Every program that uses the heap checks, when `main` returns, that every allocation was released. If any
was not, the program's exit code becomes **101** (nothing else is printed). Reference counting is managed by
the compiler, so a leak is not something ordinary code is expected to cause; `maxon test` reports a leaking
test as leaked. A green thread that was never
awaited or dropped exits with **75** instead (see [Concurrency](/docs/language/async/#exit-codes)).

### Safety Summary

| Hazard | What Maxon does |
|--------|-----------------|
| Out-of-range collection index | `get`/`set` throw `ArrayError.indexOutOfBounds`; there is no unchecked access |
| Value outside a typealias's range | compile error for a known value, **panic** at run time otherwise |
| Division by zero | compile error for a constant zero; otherwise the division throws `DivisionByZero` |
| Negative shift count | compile error for a known count, **panic** at run time otherwise |
| Integer overflow | wraps (two's complement); a range check catches it where the value reaches a checked place |
| Null | there is no null: every variable, field and element holds a value |
| Use after move, or writing a record an immutable name reads | compile error |
| Mutating a collection while an element is borrowed | compile error (**E3070**) |
| Use after free, double free | prevented by compile-time reference counting |

## Code Generation

### Native Backend

- Maxon has its own code generator: no LLVM, no external assembler and no linker step.
- It writes each target's executable format directly, producing a standalone executable.
- The supported targets are listed under `--target` in the [CLI reference](/docs/cli/).

### Optimizations

The compiler's optimization passes include:

- constant folding and simplification of constant operands
- inlining of small functions and of functions called once
- common subexpression elimination and loop-invariant code motion
- loop unswitching and jump threading
- value-range analysis, which removes range and bounds checks that provably cannot fail
- strength reduction of division and `mod` by constants
- stack promotion of records that do not escape
- dead function and dead global elimination
- hot/cold code layout and jump tables for `match`
- register allocation

Optimizations never change a program's behaviour, including its panics and range checks.

### Runtime Library

- The runtime — memory management, the scheduler, string and arithmetic helpers — is written in Maxon and
  compiled into each program.
- There is no C runtime dependency and nothing to ship beside the executable.
