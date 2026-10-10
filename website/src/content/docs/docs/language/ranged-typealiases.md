---
title: Ranged Type Aliases
description: Named numeric subranges that move domain constraints into the type system.
sidebar:
  order: 3
---

A `typealias` gives a type a name. Over `int` and `float` it also gives the type a **range**, which moves a
domain rule — a port is 0 to 65535, a percentage is 0 to 100 — into the type system, where the compiler
checks it.

## Declaration

```maxon
typealias Port = int(0 to 65535)
typealias Percentage = float(0.0 to 100.0)
typealias Temperature = int(-273 to 1000)
typealias Score = int(0 upto 100)        // 0 to 99
```

`to` makes the upper bound inclusive and `upto` makes it exclusive.

**Type-qualified bounds.** `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32` and `f64` each
have `.min` and `.max`:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
typealias Offset = int(0 to i64.max)
```

- When both bounds are type-qualified they name the same type: `i8.min to i32.max` is **E3005**
  (`Mismatched type bounds`). A qualified bound may pair with a literal (`0 to u32.max`).
- A range cannot reach both below zero and above `i64.max`: `int(-1 to u64.max)` is refused. Use
  `i64.min to i64.max` or `0 to u64.max`.
- A lower bound above `i64.max` (`int(9223372036854775808 to u64.max)`) is **E3005**, and so is an empty
  range whose lower bound is non-negative and whose upper bound is negative (`int(0 to -1)`).
- An upper bound above `i64.max` makes the range unsigned (see [Integers](/docs/language/types/#integers)).
- `typealias X = i64` is **E2003**; the sized names exist only as bounds.
- A typealias nothing uses is **E3062**.

`.min` and `.max` are also integer expressions anywhere a literal is valid: `let limit = u16.max`.

**Primitives in `with` clauses.** A generic type argument must be an alias, never a bare `int` or
`float` (**E2061**):

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally      // not Array with int
```

## Aliases Are Distinct Types

**Every typealias declaration is its own type**, even when two aliases spell the same range. A value of one
alias reaches a place declared with another — a parameter, an assignment, an `otherwise` fallback, a `match`
arm, a struct field, a union payload, a generic argument — through the cast you write:

```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function main() returns ExitCode
	let a = 30 as Age
	// takesYear(a)                   // E3005: argument type mismatch for 'y': expected 'Year', got 'Age'
	print("{takesYear(a as Year)}\n") // the cast converts
	return 0
end 'main'
```

- `as` converts in both directions. A widening cast (the source range fits the target) emits no check;
  a narrowing cast keeps a run-time check.
- Casting a value to its own alias is **E3010** (`unneeded cast: 'Age' already fits in 'Age'`).
- A value with **no** alias fits any alias of its kind: a literal, a counted-loop counter, a `var`
  initialized from a literal, the raw value of a payload-free enum case. A named value also fits an
  unnamed slot. Two *different* names conflict unless one implements the other (next section).
- A value keeps its alias wherever it is held: a local or global initialized by a cast has the cast's alias,
  and each tuple element keeps its own, so `(Tally, Tally)` and `(Score, Score)` are two types. Float
  aliases follow these rules exactly as integer aliases do — through locals, parameters, fields, captures
  and array elements.
- Two declarations of one alias name, in two files, are two types too, even over the same range. An
  operator, an argument, a store or a join that mixes them is **E3005** (**E2028** for a join) until one side
  is cast; `as` between them converts, and `return` converts as it does for any two aliases. The rule holds
  for every alias form — ranged, function, generic-instance and tuple — and for the elements of a container
  instance: `Array with Score` over two `Score` declarations is two types.
- An `Array` is indexed by the standard library's `ElementIndex`: `get`, `set` and `resize` take one.
  `count()` returns a `Count`, which implements `ElementIndex`, so a count goes wherever an index is taken
  and an `ElementIndex` goes where a `Count` is declared only by cast. The index must be an `ElementIndex`
  or an alias that implements it; any other alias, or a non-integer such as a `String`, is **E3005** —
  cast it, or declare it `implements ElementIndex`.
  A program's own array record that declares `get`, `set` or `resize` with a first parameter that is not an
  integer alias has an ordinary method: the call is served from that declaration, not the array surface.

**`return` converts.** `return x` in a function declared `returns T` behaves as `return x as T` — the one
implicit conversion between aliases. A widening return emits no check, a narrowing one keeps its check,
and `main` may return any integer alias without spelling `ExitCode`. A different struct, a union where a
scalar is declared, or a lossy float where an integer is declared is still refused.

## Subtypes With `implements`

A ranged alias may name a parent alias after its range. It is then a **subtype** of the parent, and of every
alias the parent implements:

```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias LoopHead = int(0 to u64.max) implements BlockId
typealias VarId = int(0 to u64.max) implements ElementIndex
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	rows.push(9)
	let head = 1 as LoopHead
	let r = try rows.get(head) otherwise 0    // a LoopHead is an ElementIndex
	print("{r} {takesBlock(head)}\n")          // 9 1
	// takesBlock(3 as VarId)                 // E3005: expected 'BlockId', got 'VarId'
	return 0
end 'main'
```

- A subtype goes wherever an ancestor is declared, with no cast. Casting it to an ancestor is **E3010**.
- The relation is one way: an ancestor value needs a cast to become the subtype, and that cast keeps its
  run-time range check. Two subtypes of one parent are two different types.
- The subtype's range lies inside the parent's, over the same primitive and signedness (**E3178**). Its own
  range is what its values are checked against.
- A parent is a ranged typealias (**E2003** otherwise), and may be qualified (`typealias Id = int(0 to 9)
  implements shapes.Index`). A chain that returns to itself is **E3091**.
- `implements` applies to `int` and `float` ranges. An alias of an alias, `typealias X = Y`, is **E2015**.

**Joins.** The arms of a `match` or a ternary that give two different aliases have the type of their nearest
common ancestor; arms with no common ancestor are refused.

## Construction

A literal needs no cast when it flows into a place already declared with an alias — the literal is
checked against that alias directly:

```maxon
typealias Port = int(0 to 65535)

function open(p Port) returns Port
	return p
end 'open'

function main() returns ExitCode
	print("{open(8080)}\n")
	// open(70000)          // E3005: Value 70000 is outside the range of 'Port' (int(0 to 65535))
	return 0
end 'main'
```

A cast of that literal to the alias the destination declares converts nothing and is **E3010**
(`unneeded cast: the literal 8080 already fits in 'Port'`). The destinations are a call argument (direct or
through an interface), a `return` (a parameter default included), a struct-literal field and a store to a
declared field — `open(8080 as Port)`, `return 0 as ExitCode` from `main`, `Config{port: 8080 as Port}`.
A cast to a different alias of the same name converts and is legal: this file's `Item = int(0 to 200)` at a
parameter declared with another file's `Item = int(0 to 255)`, or an alias nested in a type or extension
body at a place declared with a file-scope one.

A literal cast is how a type is fixed where nothing declares one, and there it is legal: an unannotated
`let`/`var` (`let p = 8080 as Port`), a field default whose type the cast gives (`var port = 8080 as Port`),
an array-literal element, an argument or field of a generic type parameter, an operator operand, and a value
joined by `if`/`else`, `gives` or `otherwise`. At an overloaded
call, the unneeded literal casts of one call are reported together, and only when removing all of them
still selects the same overload — a cast that decides which overload runs is needed.

Write `value as Alias` to convert a value of another alias.

## Arithmetic

Arithmetic keeps the alias of its operands. Two operands of one alias give that alias; an unnamed operand
(a literal, a loop counter) adopts the named one, and so does an ancestor (`block + offset` over a
`BlockId` and an `ElementIndex` is a `BlockId`); two aliases with no common ancestor are **E3005** until
one side is cast. The same rule governs comparisons. A shift takes the alias of its left operand.

Two subtypes of one parent make a *mixed* expression. It lands in any destination one of its operands'
aliases satisfies — a parameter, a field, an `otherwise` or `match` merge — computed into a temporary of the
destination's alias, so the result is range-checked there. Anywhere else its type is the operands' nearest
common ancestor, and a diagnostic names that ancestor. At an overloaded call each operand's alias counts:
`show(block + varId)` against `show(b BlockId)` and `show(v VarId)` is **E3007**, and the candidate list
names the aliases (`Candidates: (b BlockId), (v VarId)`).
Negating a signed alias keeps the alias; negating an unsigned one gives an unnamed value.

```maxon
typealias Score = int(0 to 100)
typealias Meters = int(0 to 1000)

function main() returns ExitCode
	let a = 30 as Score
	let b = 12 as Score
	let m = 5 as Meters
	let sum = a + b              // Score
	let bumped = a + 1           // Score: the literal adopts the alias
	let mixed = a + (m as Score) // cast one side
	// let bad = a + m           // E3005: 'Score' and 'Meters' are different typealiases
	print("{sum} {bumped} {mixed}\n")
	return 0
end 'main'
```

The alias is a name, not a proof: `a + b` over `Score` is a `Score` that may hold 130. Range checks apply
where the value lands (next section). All integer arithmetic is 64-bit and wraps on overflow.

## Range Checks

A value is checked where it reaches a place **declared** with the alias: a call argument, a `return`, a
struct-literal field, a field store, a field's declared default, an array element, or an explicit `as`.
The rules below hold for every ranged alias, integer or float, including one declared inside a type or
extension body. A field is checked against the alias its declaring file names, and a literal passed through
an interface is checked at compile time against the interface's own parameter type.

- A value the compiler can compute — a literal, a constant expression — that is out of range is a
  compile error, **E3005** (`Value 101 is outside the range of 'Percent' (int(0 to 100))`).
- Any other value gets a run-time check where needed. A check is omitted when the value's own range
  provably fits.
- A full-unsigned quantity (`int(0 to u64.max)`) is checked only for a negative from a signed source.
  A value from an unsigned alias, `u64.max`, a literal above `i64.max`, or `+ * and or xor shl shr` and
  unsigned `/` `mod` over unsigned operands passes unchecked. An unsigned subtraction panics when it
  borrows (the left operand is smaller than the right); its difference passed through a merge or further
  arithmetic is tested for a negative. A constant expression folds with the same two's-complement
  wrapping as the run time and is judged by that same rule.
- A full-unsigned parameter is checked at the call: a direct call, a call through a function value, an
  interface method call, an `async` call and a service send each check the argument they pass, so a
  negative is refused there and a value above `i64.max` passes. Its function checks it on entry as well
  when some call that reaches it carries no such check.
- A failed run-time check is a **panic**, not a recoverable error: the program prints
  `panic at <file>:<line>: Range check failed: value outside typealias '<Name>'` and a stack trace, and exits
  with code 1. No `try` is involved.

**Reassigning a local is not a checked place**, so a local may hold an out-of-range value until it escapes:

```maxon
typealias Score = int(0 to 100)

function bump(start Score) returns Score
	var s = start
	s = s + 200       // no check: a local rebind
	print("{s}\n")    // prints 210 for bump(10)
	return s          // panics: Range check failed: value outside typealias 'Score'
end 'bump'
```

**`ExitCode`** is the stdlib alias `main` returns. Its range follows the target: `int(0 to u32.max)` on
Windows and `int(0 to 255)` on Linux, macOS and WASI. A literal outside it is a compile error, and a
computed value outside it panics at the `return`. A program's own `typealias ExitCode` may be `main`'s
declared return type when it is an integer alias whose range lies within the builtin's; any other is
**E3002**.

Integers are plain values: `var pos = start` copies, and advancing `pos` never changes `start`, so a ranged
alias makes a safe loop cursor:

```maxon
typealias Pos = int(0 to i64.max)

function skipSpaces(src ByteArray, startPos Pos) returns Pos
	var pos = startPos
	while pos < src.count() 'scan'
		let b = try src.get(pos) otherwise panic("in range")
		if b != 32 'notSpace'
			break
		end 'notSpace'

		pos = pos + 1
	end 'scan'

	return pos
end 'skipSpaces'
```

## Naming Aliases

Name an alias for its **purpose** — `Tally`, `BytePos`, `Coord`, `Milliseconds` — and declare it in the
module it belongs to. The standard library follows the
same pattern and exports a small set of cross-cutting aliases:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | target-dependent (see above) | process exit codes |
| `Byte` | `int(0 to u8.max)` | one byte; `ByteArray` is `Array with Byte` |
| `HashValue` | `int(0 to u32.max)` | `Hashable.hash()` results |
| `Codepoint` | `int(0 to 1114111)` | Unicode scalar values |
| `Count` | `int(0 to u64.max) implements ElementIndex` | a collection's `count()` |
| `SourceLineNumber` | `int(1 to i32.max)` | caller line numbers (`__line__`) |
| `Real` | `float(f64.min to f64.max)` | general floating-point values |

Because every alias is its own type, a quantity crossing from one module's alias to another's is cast at
the crossing.

Where a standard-library alias fits, use it; where none fits, give your own alias a name of its own,
distinct from the library's. A program's declaration of a library name is
what the bare name means in every author file — the library's stays reachable as `stdlib.Name` — so the two
are two types and every value crossing between them needs a cast.

A non-exported ranged alias is private to its file: two files may declare one name over different ranges,
or one over `int` and the other over `float`, and each file's uses mean its own declaration. Exported
declarations of one name live in different directories, whatever their ranges, and a bare name that reaches
more than one of them is ambiguous in every file, the declaring files included (see
[Bare Names and Ambiguity](/docs/language/namespaces/#bare-names-and-ambiguity)).

## Generic-Instance and Function-Type Aliases Are Brands

An alias over a generic instance or a function type follows the same rule. `typealias Xs = Array with
Integer` and `typealias Ys = Array with Integer` are one instance (one layout, one method set) under two
**brands**: an `Xs` does not flow into a `Ys` slot unless you write `xs as Ys`, which re-brands the value at
no cost, and `return` re-brands implicitly.

- A `[...]` literal carries no brand and fits either.
- A closure literal or a declared function carries no brand and fits any function alias of its shape.
- A function alias declared **inside a type or extension body** carries no brand either, in both
  directions: `Array.SortComparator` spells the shared body's signature over the type's own parameters,
  and a brand is a file-scope name an author chose to distinguish two shapes. A value of any other alias
  of that shape flows into it, and a value of it flows into any other alias of that shape.
- Two **file-scope** function aliases of one shape still refuse each other — including two
  directory-qualified ones, `api.Score` and `legacy.Score`, which are file-scope declarations wearing
  their directory and can be written in a type position.
- A non-exported function alias is **private to its file**, so two files declaring one name have two
  brands and two types, whatever their shapes: each file's declarations mean its own, and a value of one
  reaches a door declared with the other only through `as`. Two declarations of one generic-instance alias over the
  same instance are two brands in the same way.
- A bare reference that reaches two `export`ed declarations is ambiguous in every file, a declaring file
  included: **E3063**, resolved by qualifying with the directory namespace or by renaming.
- A diagnostic names each type as source would qualify it: the standard library's as `stdlib.Name`,
  another directory's as `dir.Name`, the root's bare. Where the two sides would still print alike, it adds
  a parenthesised note — the shape where two function shapes differ, otherwise `(declared in <file>)`. A
  view of a buffer prints as `__ManagedMemory with <element>`, noted `(the view of 'Name')` where two views
  read alike.
- Casting to a **different** instance (`Array with Byte` to `Array with Integer`) is **E3131**: the
  elements have different layouts, so build a new container instead. A cast between two instances whose
  elements are same-named declarations of one layout — the same underlying type, range and domain;
  `implements` aside — re-brands the container in place.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function runHandler(f Handler) returns Integer
	return f(10)
end 'runHandler'

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function main() returns ExitCode
	let addThree = function(n Integer) gives n + 3
	print("{runHandler(addThree)} {runCallback(addThree)}\n")   // 13 23
	return 0
end 'main'
```

## Per-Instance Typealiases

A ranged alias declared inside a generic type is a distinct type for each instantiation — even for two
aliases of the same instance:

```maxon
typealias Integer = int(i64.min to i64.max)

type Pool uses T
	export typealias Idx = int(0 to u64.max)
	var items as T

	static function create(item T) returns Self
		return Self{items: item}
	end 'create'

	export function checked(at Idx) returns Idx
		return at
	end 'checked'
end 'Pool'

typealias PoolA = Pool with Integer
typealias PoolB = Pool with Integer
```

`PoolA.Idx` and `PoolB.Idx` are different types; passing one where the other is expected is **E3005**
(`expected 'PoolB.Idx', got 'PoolA.Idx'`). Convert with `a as PoolB.Idx`. Literals that fit the range are
accepted by both.

A ranged alias declared in a type or extension body is a nominal type by the same rule as a file-scope
one: a value of any other alias, over the same range or not, crosses into it with a cast.

A per-instance **function** alias is unbranded: it spells the shared body's signature over the type's own
parameters. `Array.sort` takes an `Array.SortComparator`, so a field declared with your own `typealias RowComparator =
function(Row, Row) returns Ordering` passes straight through: `rows.sort(self.compare)` compiles with no
cast and no wrapping closure.
