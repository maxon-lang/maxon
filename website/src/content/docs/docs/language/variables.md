---
title: Variables
description: Mutable (var) and immutable (let) bindings, including top-level variables.
sidebar:
  order: 6
---

## Mutable Variables (`var`)

```maxon
var x = 42              // type inferred from the initializer
x = x + 5               // reassignment allowed
```

## Immutable Variables (`let`)

```maxon
let pi = 3.14159
let name = "Maxon"
// pi = 3.14            // E2013: cannot assign to immutable variable: 'pi'
```

A `let` cannot be mutated through a method either: calling a method that writes its receiver (`push`,
`append`, `set`, `remove`, `clear`, …) on a `let` is **E3019**.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var items = TallyArray.create()
	items.push(1)                    // fine: items is a var
	// let fixed = TallyArray.create()
	// fixed.push(1)                 // E3019: cannot pass 'fixed' to function that mutates parameter 'self'
	print("{items.count()}\n")
	return 0
end 'main'
```

## Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  Local declarations take no type annotation.
- Variables are block-scoped.
- **A `var` that is never reassigned or mutated is E3077** (`variable 'x' is never reassigned; use 'let'
  instead of 'var'`).
- **Every variable must be used (E3012).** This covers `let`/`var`, parameters, loop variables, pattern
  bindings and closure parameters. The name `_` discards: it creates no binding and may appear several
  times in one pattern (`for (_, _) in pairs`). Names such as `_x` are ordinary variables. A method that
  implements an interface requirement is exempt for its parameters, since the interface dictates them.
- **Records are shared, not copied.** `var b = a` for a record-typed `a` hands `b` the same record; use
  `a.clone()` for an independent copy (see
  [Reference-by-Default Assignment](/docs/language/memory-model/#reference-by-default-assignment)).
  Numbers, `bool` and other scalar values are always independent copies.
- **A mutable name takes an immutable name's record only by moving it** (see
  [Immutable XOR Mutable](/docs/language/memory-model/#immutable-xor-mutable)):

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
  	var b = a                 // a MOVES into b; reading a afterwards is E3102
  	b.x = 10

  	let c = Point.create(3)
  	let d = c                 // d aliases c: two immutable names
  	// var e = c              // E3078: cannot assign immutable variable 'c' to mutable binding 'e'
  	var f = c.clone()         // an independent record
  	f.x = 30
  	print("{b.x} {c.x} {d.x} {f.x}\n")   // 10 3 3 30
  	return 0
  end 'main'
  ```

## Top-Level Variables

Variables can be declared at file scope, outside any function:

```maxon
typealias Tally = int(0 to u64.max)

var callCount = 0
let MAX_SIZE = 1024
var names = ["ada", "alan"]
let dataFile = FilePath from "data.txt"

function bump() returns Tally
	callCount = callCount + 1
	return callCount
end 'bump'

function main() returns ExitCode
	_ = bump()
	print("{callCount} {MAX_SIZE} {names.count()} {dataFile.filename()}\n")   // 1 1024 2 data.txt
	return 0
end 'main'
```

- `var` declares module state any function in the file can reassign; `let` declares a constant.
- An initializer is a literal, a constant expression, an enum case, an array or dictionary literal,
  `Type from "literal"`, or a static factory call (`let shared = Cache.create()`). Any other function call is
  **E2045** (`Function calls are not allowed in global variable initializers`).
- Every initializer runs **before `main`**, once, in dependency order. Initializers that depend on each
  other in a cycle are **E2012**.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
- A [service](/docs/language/async/#services--spawn) handler may not read or write a module-level `var`
  (**E3143**); keep service state in its fields.
