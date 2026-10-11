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

A `let` is read-only all the way down (see [Read-Only Values](/docs/language/memory-model/#read-only-values)): calling a method that writes
its receiver (`push`, `append`, `set`, `remove`, `clear`, …) on a `let`, or writing a field of anything it
reaches, is **E3159**.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var items = TallyArray.create()
	items.push(1)                    // fine: items is a var
	// let fixed = TallyArray.create()
	// fixed.push(1)                 // E3159: cannot write through 'fixed': 'fixed' is read-only (a `let` binding)
	print("{items.count()}\n")
	return 0
end 'main'
```

## Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  A declaration takes no type annotation: `var x as Tally = 0` is **E2010** (`Expected '=' but got 'as'`).
  A cast names the type instead — `var x = 0 as Tally` declares a `Tally`, locally and at file scope.
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
- **A read-only value stays read-only.** `var b = a` moves `a` when `a` is a `var`; from a read-only `a` (a
  `let` included) it is **E3078**, and `a.clone()` is the mutable copy (see [Read-Only Values](/docs/language/memory-model/#read-only-values)):

  ```maxon
  typealias Coord = int(i64.min to i64.max)

  type Point
  	export var x as Coord

  	static function create(x Coord) returns Point
  		return Point{x: x}
  	end 'create'
  end 'Point'

  function main() returns ExitCode
  	var a = Point.create(1)
  	a.x = 2
  	var b = a                 // a MOVES into b; reading a afterwards is E3102
  	b.x = 10

  	let c = Point.create(3)
  	let d = c                 // d shares c's record: both are read-only
  	// var e = c              // E3078: a read-only value bound to a `var`
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
  `Type from "literal"` or `ArrayAlias from [...]`, a static factory call (`let shared = Cache.create()`), a
  free function call that returns a record or a function value (`let shared = makeCache()`), or a function
  name (`let step = countdown`). The type at the head may be qualified (`lib.Cache.create()`). A free function
  call returning a scalar, and any other call, is **E2045** (`Function calls are not allowed in global
  variable initializers`).
- Every initializer runs **before `main`**, once, in dependency order, whether or not anything reads the
  binding. Initializers that depend on each other in a cycle are **E2012**. A `let` whose value is decided at
  compile time is image data, laid down in read-only memory with nothing to run; anything else runs in the
  program's `__module_init` before `main`. A static field follows the same rules
  (see [Static Fields](/docs/language/composite-types/#static-fields)).
- A field read off another global (`let n = shared.count`) is **E2015**, and a struct literal at file scope
  is **E3076**; call a factory instead, or declare a static field inside the type.
- An initializer cannot name another global, but what it calls may. A `let`'s initializer may not reach a
  module-level `var` that holds a record — a String, an array, a struct, a boxed union — through any function
  it calls (**E3165**, reported at the `var`'s use with its declaration as a note). A `let` is fixed at startup
  and may not hold what a `var` owns, and what the initializer keeps is not followed, so reading only a number
  out of the `var` is refused too. "Reaches" is the call graph after overload resolution, dispatches
  included. A scalar `var` is readable, a `var`'s initializer may reach any global, and a `let`'s may reach
  other `let`s.
- The other direction is refused too: a `var`'s initializer may not call anything whose result may be, lie
  within or hold a module-level `let`'s record (**E3078**, at the `var`'s declaration, naming the call). What
  a call hands back is followed through further calls, witness dispatches and calls through function values;
  a record built fresh from numbers read out of a `let` is legal.
- A module-level `let` is [read-only](/docs/language/memory-model/#read-only-values) through everything it reaches, for the program's whole
  life. A write through it, or through a record a call handed back out of it, is **E3159**; binding it, a field
  of it (`var m = H.index`) or such a call result to a `var` is **E3078**. Read it through a `let`, or bind a
  `clone()`.
- A `spawn` reachable from a global initializer is **E3164**; start services in `main`, or declare a
  program-wide one with [`default`](/docs/language/async/#program-wide-defaults--default). A `Key.current()` or `Key.register()`
  reachable from a global initializer is [E3173](/docs/cli/error-codes/#e3173--registryreachedfromglobalinitializer): the
  defaults are built after every global initializer has run.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
- A [service](/docs/language/async/#services--spawn) handler, and every function it reaches, may not read or write a module-level
  `var` (**E3143**); keep service state in its fields.
