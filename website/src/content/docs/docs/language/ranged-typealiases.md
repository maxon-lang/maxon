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
- A typealias nothing uses is **E3062**; any other file-private top-level declaration nothing uses is
  **E3219** (see [Visibility](/docs/language/namespaces/#visibility)).

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
arm, a struct field, a union payload, a generic argument, an array element (`push`, `set`, `insert`,
`from [..]`) — through the cast you write:

```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function yearOfAge(a Age) returns Year
	// return takesYear(a)            // E3005: argument type mismatch for 'y': expected 'Year', got 'Age' — cast the argument with 'as Year'
	return takesYear(a as Year)       // the cast converts
end 'yearOfAge'

function main() returns ExitCode
	print("{yearOfAge(30)}\n")        // 30
	return 0
end 'main'
```

- `as` converts in both directions. A widening cast (the source range fits the target) emits no check;
  a narrowing cast keeps a run-time check.
- Casting a value to its own alias is **E3010** (`unneeded cast: 'Age' already fits in 'Age'`).
- A value with **no** alias fits any alias of its kind whose range admits it, checked at compile time at
  each use: a literal, a plain `let` constant, a loop counter between two literal bounds, and a value
  computed from them (see [Variables](/docs/language/variables/)). A named value also fits an unnamed slot. Two
  *different* names conflict unless one implements the other (next section).
- An **E3005** between two integer-range types (a ranged integer alias or `ExitCode`) ends with the cast
  that fixes it: `— cast the argument with 'as Year'` at an argument, a builtin's included, and at an
  array element stored by `push`, `set` or `from [..]`, and
  `— cast the value with 'as Year'` at a store (a variable, global, field, tuple element, struct literal
  or union payload). The hint ends only a diagnostic whose expected type carries no `(declared in <file>)`
  note.
- A value keeps its alias wherever it is held: a local or global initialized by a cast has the cast's alias,
  and each tuple element keeps its own, so `(Tally, Tally)` and `(Score, Score)` are two types. Float
  aliases follow these rules exactly as integer aliases do — through locals, parameters, fields, captures
  and array elements — and a `float` alias value at an `int` alias place is **E3005**, as `Age` at a `Year`
  place is.
- Two declarations of one alias name, in two directories, are two types too, even over the same range. An
  operator, an argument, a store or a join that mixes them is **E3005** (**E2028** for a join) until one side
  is cast; `as` between them converts, and `return` converts as it does for any two aliases. The rule holds
  for every alias form — ranged, function, generic-instance and tuple — and for the elements of a container
  instance: `Array with Score` over two `Score` declarations is two types.
- An `Array` is indexed by the standard library's `ElementIndex`: `get`, `set`, `resize` and the capacity of `create(n)` take one.
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
	let rows = RowArray.create(0)
	rows.push(4)
	rows.push(9)
	let head = 1 as LoopHead
	let r = try rows.get(head) otherwise 0    // a LoopHead is an ElementIndex
	let other = 3 as VarId
	print("{r} {takesBlock(head)} {other}\n")  // 9 1 3
	// takesBlock(other)  // E3005: argument type mismatch for 'b': expected 'BlockId', got 'VarId' — cast the argument with 'as BlockId'
	return 0
end 'main'
```

- A subtype goes wherever an ancestor is declared, with no cast. Casting it to an ancestor retypes the value
  as the ancestor: `let hi = xs.count() as ElementIndex` is an `ElementIndex`.
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
A cast to a different alias of the same name converts and is legal: this directory's `Item = int(0 to 200)` at a
parameter declared with another directory's `Item = int(0 to 255)`, or an alias nested in a type or extension
body at a place declared with a file-scope one.

A literal cast in an unannotated `let` (`let p = 8080 as Port`) is **E3010** when every use of the `let`
takes the plain constant the same way (`unneeded cast: the literal 8080 already fits every use of 'p'`):
`let p = 8080` already fits each use. A `public`, `export` or `module` `let` keeps its cast, and so does one a closure captures.

A literal cast is how a type is fixed where nothing declares one, and there it is legal: an unannotated
`var` (`var p = 8080 as Port`), a `let` some use of which needs the alias, a field default whose type the
cast gives (`var port = 8080 as Port`), an array-literal element, an argument or field of a generic type parameter, an operator operand, and a value
joined by `if`/`else`, `gives` or `otherwise`.

Write `value as Alias` to convert a value of another alias.

## Arithmetic

Arithmetic keeps the alias of its operands. Two operands of one alias give that alias; an unnamed operand
(a literal, a loop counter) adopts the named one, and so does an ancestor (`block + offset` over a
`BlockId` and an `ElementIndex` is a `BlockId`); two aliases with no common ancestor are **E3005** until
one side is cast. The same rule governs comparisons, and a [top-level initializer](/docs/language/variables/#top-level-bindings)
refuses the same pairs. A shift takes the alias of its left operand.

Two subtypes of one parent make a *mixed* expression. It lands in any destination one of its operands'
aliases satisfies — a parameter, a field, an `otherwise` or `match` merge — computed into a temporary of the
destination's alias, so the result is range-checked there. Anywhere else its type is the operands' nearest
common ancestor, and a diagnostic names that ancestor.
Unary `-`, `not`, `or` and `xor` keep the operand's alias only where its range is closed under the operator: a full-width unsigned alias (`int(0 to u64.max)`) keeps it under `not`, `or` and `xor`, a `bits(64)` pattern keeps it under all four and wraps modulo 2^64, and a range symmetric about zero keeps it under `-`. Unary `-` over a full-width unsigned alias is **E3218**: the negation of any value above 0 is outside `int(0 to u64.max)`, so write the wrapping negation over a `bits(64)` alias, or convert the value to a signed alias first with `as`. Over any other alias the result is an unnamed signed value carrying the range it computes (`-b` over `int(0 to 100)` is `[-100, 0]`), and it needs `as` to reach an alias slot. `and` always keeps the alias.

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
	print("{sum} {bumped} {mixed} {m}\n")
	return 0
end 'main'
```

The alias is a name, not a proof: `a + b` over `Score` is a `Score` that may hold 130. Range checks apply
where the value lands (next section). All integer arithmetic is 64-bit and wraps on overflow, except `+`,
`-` and `*` over a full-width unsigned alias, which panic when the result leaves the range.

## Range Checks

A value is checked where it reaches a place **declared** with the alias: a call argument, a `return`, a
`var`'s initializer and every assignment to a ranged `var` (local, top-level, `static` or field), a
struct-literal field, a field store, a field's declared default, an array element, a union case's payload,
or an explicit `as`. A `var` therefore always holds a value of its alias: `var t = a + b` over two `Score`s
checks the sum where `t` is born.
The rules below hold for every ranged alias, integer or float, including one declared inside a type or
extension body. A field is checked against the alias its declaring file names, and a literal passed through
an interface is checked at compile time against the interface's own parameter type.

- A value the compiler can compute — a literal, a constant expression — that is out of range is a
  compile error, **E3005** (`Value 101 is outside the range of 'Percent' (int(0 to 100))`).
- Any other value gets a run-time check where needed. A check is omitted when the value's own range
  provably fits.
- A full-unsigned quantity (`int(0 to u64.max)`) is checked at its door only for a negative from a
  signed source. A value from an unsigned alias, `u64.max`, a literal above `i64.max`, or `and or xor shl
  shr` and unsigned `/` `mod` over unsigned operands passes its door unchecked; `shl` drops the bits
  shifted past bit 63 over every alias, and bit work is written over a `bits(64)` alias.
- `+`, `-` and `*` over a full-unsigned alias are checked where they are computed, because every 64-bit
  pattern is a value of the alias and a wrapped result looks like a real one at any later door. A sum that
  carries past `u64.max`, a difference that borrows (the left operand is smaller than the right) and a
  product past `u64.max` each panic at the operation, in a `let`, a loop bound or an argument alike. The
  check is omitted where the compiler proves the result fits: an identity operand (`x + 0`, `x - 0`,
  `x * 0`, `x * 1`), constants that do not wrap, a counted loop's interval, or a dominating comparison (a
  difference `a - b` where `b <= a` holds on every path to it, a sum of a value and a constant where the
  comparison bounds the value below the point of wrapping). A constant expression that
  wraps is a compile error, **E3005** (`The result of 2 - 3 is outside the range of 'Count'
  (int(0 to 18446744073709551615))`). Arithmetic over a narrower alias is checked where its result
  reaches a door.
- A full-unsigned parameter is checked at the call: a direct call, a call through a function value, an
  interface method call, an `async` call and a service send each check the argument they pass, so a
  negative is refused there and a value above `i64.max` passes. Its function checks it on entry as well
  when some call that reaches it carries no such check.
- A failed run-time check is a **panic**, not a recoverable error: the program prints
  `panic at <file>:<line>: Range check failed: value outside typealias '<Name>'` and a stack trace, and exits
  with code 1. No `try` is involved.

**Reassigning a ranged `var` is a checked place**, so the panic names the assignment that left the range:

```maxon
typealias Score = int(0 to 100)

function bump(start Score) returns Score
	var s = start
	s = s + 200       // bump(10) panics here: Range check failed: value outside typealias 'Score'
	return s
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
| `Codepoint` | `int(0 to 1114111)` | Unicode codepoints (see [Character Literals](/docs/language/overview/#character-literals) for the surrogate block) |
| `Count` | `int(0 to u64.max) implements ElementIndex` | a collection's `count()` |
| `SourceLineNumber` | `int(1 to i32.max)` | caller line numbers (`__line__`) |
| `FullRangeFloat` | `float(f64.min to f64.max)` | general floating-point values |

Because every alias is its own type, a quantity crossing from one module's alias to another's is cast at
the crossing.

Where a standard-library alias fits, use it; where none fits, give your own alias a name of its own,
distinct from the library's. A program's declaration of a library name is
what the bare name means in every author file — the library's stays reachable as `stdlib.Name` — so the two
are two types and every value crossing between them needs a cast.

A directory declares a ranged alias once, exported or not: two directories may each declare one name over
different ranges, or one over `int` and the other over `float`, and a bare name means the reading file's own
directory's declaration. A bare name that reaches several directories' declarations, none nearer, is
ambiguous (see [Bare Names and Ambiguity](/docs/language/namespaces/#bare-names-and-ambiguity)).

## Generic-Instance and Function-Type Aliases Are Brands

An alias over a generic instance or a function type follows the same rule. `typealias Xs = Array with
Integer` and `typealias Ys = Array with Integer` are one instance (one layout, one method set) under two
**brands**: an `Xs` does not flow into a `Ys` slot unless you write `xs as Ys`, which re-brands the value at
no cost, and `return` re-brands implicitly.

- A `[...]` literal carries no brand and fits either, with or without an `Alias from` head.
- An element stored into a branded slot must carry the slot's brand, in a literal as in `push`: with
  `typealias Rows = Array with Xs`, both `Rows from [ys]` and `rows.push(ys)` for a `Ys` value are
  **E3005**.
- A brand travels with the value: through a tuple element (`(Xs, Item)` and `(Ys, Item)` are two types),
  a destructuring, a merge and a read of a `var`. The arms of a merge holding two brands are refused.
- An unbranded `var` takes the brand of the first branded value stored into it, and keeps it: a later
  store of another brand is **E3005** (see [Variables](/docs/language/variables/#rules)).
- Comparing values of two brands (`xs == ys`) is **E3005**: a `Self` parameter, as `equals`' operand is,
  takes the receiver's brand. Inside a generic type, a parameter spelled with the bare base (`other Box`)
  fits every brand of the instance.
- A closure literal or a declared function carries no brand and fits any function alias of its shape.
- A function alias declared **inside a type or extension body** carries no brand either, in both
  directions: `Array.SortComparator` spells the shared body's signature over the type's own parameters,
  and a brand is a file-scope name an author chose to distinguish two shapes. A value of any other alias
  of that shape flows into it, and a value of it flows into any other alias of that shape.
- Two **file-scope** function aliases of one shape still refuse each other — including two
  directory-qualified ones, `api.Score` and `legacy.Score`, which are file-scope declarations wearing
  their directory and can be written in a type position.
- A function alias is declared once per directory, so two directories declaring one name have two
  brands and two types, whatever their shapes: each directory's declarations mean its own, and a value of
  one reaches a door declared with the other only through `as`. Two declarations of one generic-instance alias over the
  same instance are two brands in the same way.
- A call's result wears the alias its callee's `returns` clause names, bare or directory-qualified, as the
  file that declares the callee resolves it: a caller's own declaration of that name is another brand.
- A field read and a `match` payload binding wear the alias the field or payload was declared with, as its declaring file resolves it: with `held(v Xs)`, the
  binding `v` in `held(v) then …` is an `Xs`.
- A bare reference that reaches two `export`ed declarations, with none in the reading file's directory or
  the root, is ambiguous: **E3063**, resolved by qualifying with the directory namespace.
- A diagnostic names each type as source would qualify it: the standard library's as `stdlib.Name`,
  another directory's as `dir.Name`, the root's bare. Where the two sides would still print alike, it adds
  a parenthesised note — the shape where two function shapes differ, otherwise `(declared in <file>)`. A
  view of a buffer prints as `__ManagedMemory with <element>`, noted `(the view of 'Name')` where two views
  read alike.
- The base of a generic-instance alias is read as [Generic Types](/docs/language/composite-types/#generic-types) says: a directory-qualified
  base (`typealias Good = lib.Pair with (Integer, Integer)`) names that directory's type, and a bare base that a
  `typealias` wins for the reading file is **E2055**.
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

A type has one member of each name. When two `extension` bodies of the program declare the same nested
typealias for one type, in one file or two, over one instance or two, and for each conformer of an
[interface extension](/docs/language/composite-types/#interface-extensions) that both reach, the later declaration is
[E3061](/docs/cli/error-codes/#e3061--semanticduplicatetypealias), naming the file of the one kept: the
declaration in the file whose absolute path, with `/` separators, sorts first byte by byte, and within a file
the one written first. A conditional extension declares the member only for the conformers its `where`
clause admits, and only the program's own extensions are compared.

A `type` or `interface` body declares each nested typealias once: a second declaration of the name in the
same body, whatever its form (generic instance, ranged, function or tuple), is
[E3061](/docs/cli/error-codes/#e3061--semanticduplicatetypealias) at its name.

A nested typealias follows the rule a method does (see [Static Methods](/docs/language/composite-types/#static-methods)). An `extension` of a
type that declares a nested typealias the type's own body declares is
[E3061](/docs/cli/error-codes/#e3061--semanticduplicatetypealias) at the extension's declaration, in any file and
for a standard-library type too. An [interface extension](/docs/language/composite-types/#interface-extensions)'s nested typealias is a
default: a conforming type whose body declares the same member keeps its own, and the extension's methods,
compiled for that type, read it.

A nested typealias that an `extension` of the program declares under the name of one a library `extension` of
the same type also declares (`Array.ElementArray`, `Array.SortComparator`, `Iterable.ElementTransform`) is a
separate member. The program's extension bodies, in every file, spell it with the bare name, and callers of
the program's methods see it; the library's bodies and methods keep the library's member. A member without
a visibility modifier is spelled from its own file only: another file's extension body naming it is
[E3008](/docs/cli/error-codes/#e3008--symbolnotexported).

A per-instance **function** alias is unbranded: it spells the shared body's signature over the type's own
parameters. `Array.sortBy` takes an `Array.SortComparator`, so a field declared with your own `typealias RowComparator =
function(Row, Row) returns Ordering` passes straight through: `rows.sortBy(self.compare)` compiles with no
cast and no wrapping closure.
