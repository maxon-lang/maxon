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

**Every typealias is its own type**, even when two aliases spell the same range. A value of one alias
never flows into a place declared with another — a parameter, an assignment, an `otherwise` fallback, a
`match` arm, a struct field, a union payload, a generic argument — unless you write the cast:

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
  unnamed slot. Only two *different* names conflict.
- The same alias name declared over the same range in two files is one type.

**`return` converts.** `return x` in a function declared `returns T` behaves as `return x as T` — the one
implicit conversion between aliases. A widening return emits no check, a narrowing one keeps its check,
and `main` may return any integer alias without spelling `ExitCode`. A different struct, a union where a
scalar is declared, or a lossy float where an integer is declared is still refused.

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

Write `value as Alias` when the alias should be visible at the use site, or to convert a value of another
alias.

## Arithmetic

Arithmetic keeps the alias of its operands. Two operands of one alias give that alias; an unnamed operand
(a literal, a loop counter) adopts the named one; two different aliases are **E3005** until one side is
cast. The same rule governs comparisons. A shift takes the alias of its left operand.
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

- A value the compiler can compute — a literal, a constant expression — that is out of range is a
  compile error, **E3005** (`Value 101 is outside the range of 'Percent' (int(0 to 100))`).
- Any other value gets a run-time check where needed. A check is omitted when the value's own range
  provably fits.
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
computed value outside it panics at the `return`.

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

Name an alias for its **purpose** — `Tally`, `BytePos`, `Coord`, `Milliseconds` — rather than reaching for
a generic `Count` or `Index`, and declare it in the module it belongs to. The standard library follows the
same pattern and exports a small set of cross-cutting aliases:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | target-dependent (see above) | process exit codes |
| `Byte` | `int(0 to u8.max)` | one byte; `ByteArray` is `Array with Byte` |
| `HashValue` | `int(0 to u32.max)` | `Hashable.hash()` results |
| `Codepoint` | `int(0 to 1114111)` | Unicode scalar values |
| `SourceLineNumber` | `int(1 to i32.max)` | caller line numbers (`__line__`) |
| `Real` | `float(f64.min to f64.max)` | general floating-point values |

Because every alias is its own type, a quantity crossing from one module's alias to another's is cast at
the crossing.

## Generic-Instance and Function-Type Aliases Are Brands

An alias over a generic instance or a function type follows the same rule. `typealias Xs = Array with
Integer` and `typealias Ys = Array with Integer` are one instance (one layout, one method set) under two
**brands**: an `Xs` does not flow into a `Ys` slot unless you write `xs as Ys`, which re-brands the value at
no cost, and `return` re-brands implicitly.

- A `[...]` literal carries no brand and fits either.
- A closure literal or a declared function carries no brand and fits any function alias of its shape.
- A function alias declared **inside a type or extension body** carries no brand either, in both
  directions: no source outside the type can write `Array.SortComparator`, so it is never the name an
  author chose to distinguish two shapes. A value of any other alias of that shape flows into it, and a
  value of it flows into any other alias of that shape.
- Two **file-scope** function aliases of one shape still refuse each other — including two
  directory-qualified ones, `api.Score` and `legacy.Score`, which are file-scope declarations wearing
  their directory and can be written in a type position.
- A non-exported function alias is **private to its file**, so two files declaring one name over
  **different** shapes have two brands and two types: each file's declarations mean its own, and a value
  of one does not fit a door declared with the other. What a slot's spelling *means* in the declaring
  file decides — two files that write the identical `function(Tally) returns Tally` over two different
  `Tally` ranges still have two shapes. Two files that **agree** about the shape share one brand, exactly
  as two files declaring one ranged alias over one range share one type.
- When both declarations are `export`ed, a bare reference from a third file is ambiguous rather than
  split: **E3063**, resolved by qualifying with the directory namespace. This holds whether or not the
  two shapes agree.
- Where a refusal would print the same bare name on both sides, the message adds a parenthesised note —
  the shape where the two shapes differ, otherwise the declaring files. The alias itself is always quoted
  by the name its author wrote.
- Casting to a **different** instance (`Array with Byte` to `Array with Integer`) is **E3131**: the
  elements have different layouts, so build a new container instead.

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

A per-instance **function** alias is not a brand, because no source outside the type can write its name.
`Array.sort` takes an `Array.SortComparator`, so a field declared with your own `typealias RowComparator =
function(Row, Row) returns Ordering` passes straight through: `rows.sort(self.compare)` compiles with no
cast and no wrapping closure.
