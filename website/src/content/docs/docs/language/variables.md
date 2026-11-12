---
title: Variables
description: Mutable (var) and immutable (let) bindings, including top-level variables.
sidebar:
  order: 6
---

A `let` is a name that is never reassigned; a `var` is a name that may be. The keyword says nothing about the
data behind the name: a field store, a `push` and a callee that writes its parameter reach through a `let`
to the record it names. A number, `bool`, enum or `String` has no record behind its name, so changing one is
reassigning it, and it needs a `var`.

## Mutable Variables (`var`)

```maxon
typealias Tally = int(0 to u64.max)

var x = 42 as Tally     // a Tally: the cast names the type
x = x + 5               // reassignment allowed, range-checked against Tally
// var y = 42           // E2004: A var cannot start from a bare number: write 'var name = expr as Type'.
```

A `var` holding a number names its alias where it is born, locally, at file scope and as a `static`. A bare
number, arithmetic over bare numbers (`16 * 1024`), a plain `let` constant, and an array, map or tuple literal
of bare numbers are refused as its initializer (**E2004**); cast it (`var size = (16 * 1024) as ByteCount`).
A `var` initialized from a value of some alias is that alias.

## Fixed Variables (`let`)

```maxon
let pi = 3.14159
let name = "Maxon"
// pi = 3.14            // E2013: 'pi' is a let and cannot be reassigned
```

A `let` bound to a bare number, to arithmetic over bare numbers or to a literal container of them is a
**constant**: it acts like the literal it holds, and fits any alias its value fits, checked at compile time
at each use. A `let` computed from such constants or from a loop counter between two literal bounds carries
the range its value can have, and each use is checked against it the same way, for integers and floats
alike. A `let` of a number whose range the compiler cannot know (`let w = 1 shl k`) is **E2004**; write
`let w = (1 shl k) as Width`. A literal cast (`let port = 8080 as Port`) makes the `let` a value of the
alias; when every use of it would take the plain constant the same way, the cast is **E3010** (see
[Construction](/docs/language/ranged-typealiases/#construction)).

```maxon
typealias Port = int(0 to 65535)

function open(p Port) returns Port
	return p
end 'open'

function main() returns ExitCode
	let base = 8000
	let port = base + 80        // a constant: 8080
	print("{open(port)}\n")     // 8080
	// open(base * 10)          // E3005: Value 80000 is outside the range of 'Port' (int(0 to 65535))
	return 0
end 'main'
```

A `let` fixes which record the name holds, not what is in the record. A field store, a method that writes its
receiver (`push`, `append`, `set`, `remove`, `clear`, …) and a callee that writes its parameter all reach
through a `let`:

```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let p = Box.create(1)
	p.n = 2                          // a field store through a let
	let xs = Counts.create(0)
	xs.push(p.n)                     // so is a method that writes its receiver
	// p = Box.create(3)             // E2013: 'p' is a let and cannot be reassigned
	print("{p.n} {xs.count()}\n")    // 2 1
	return 0
end 'main'
```

What a `let` refuses is reassignment:

- Assigning to it is **E2013** (`'p' is a let and cannot be reassigned`). The same wording covers a `for`
  variable, a `match` or `if let` binding and a tuple element bound by `let`. A `let` field is `'version' is a
  let field and cannot be reassigned`.
- Passing it to a `mutable` parameter is **E2013** too, because that parameter reassigns the caller's variable:
  ``'n' is a let and cannot be passed to the `mutable` parameter 'x'; bind it to a `var` ``.
- A number, `bool`, enum or `String` held by a `let` cannot change at all, since changing one is reassigning
  it. Declare it `var` when it must.

A name bound from a `let` names the same record, so a write through either name is seen through the other. A
`let` bound from state that something else writes sees the writes:

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let r = Box.create(5)
	let c = r
	c.n = 7                   // r names the same record
	let h = Holder.create()
	let b = h.box
	h.box.n = 8               // b sees it
	print("{r.n} {b.n}\n")    // 7 8
	return 0
end 'main'
```

Data that nothing may write is a separate notion from the keyword: constant data, frozen data, a stored `Map`
key and a `SharedValue` read stay read-only wherever they are bound (see [Read-Only Values](/docs/language/memory-model/#read-only-values)
and [Constant Data](/docs/language/regions/#constant-data)).

## Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  A declaration takes no type annotation: `var x as Tally = 0` is **E2010** (`Expected '=' but got 'as'`).
  A cast names the type instead — `var x = 0 as Tally` declares a `Tally`; a file-scope `let` takes the same cast (`let limit = 5 as Tally`).
- **A `var` is born a value of its alias, and every assignment to it is checked against it** (see
  [Range Checks](/docs/language/ranged-typealiases/#range-checks)).
- **A `var` takes the brand of the first branded value stored into it** when its initializer has none:
  `var a = [1 as Item, 2 as Item]` followed by `a = xs` for an `Xs` makes `a` an `Xs` for its whole life,
  and a later store of a `Ys` is **E3005** (`cannot assign a value of type 'Ys' to variable 'a', which
  holds 'Xs'`). The first store in source order decides. See [Generic-Instance and Function-Type Aliases Are Brands](/docs/language/ranged-typealiases/#generic-instance-and-function-type-aliases-are-brands).
- Variables are block-scoped.
- **A `var` that is never reassigned is E3077** (`variable 'x' is never reassigned; use 'let' instead of
  'var'`), whatever else is done with it. Reassigning the name is what keeps a `var`: an assignment, passing
  it to a `mutable` parameter, or calling a method declared `mutable self` on a value of a value type
  (`text.append("!")` over a `String`). A field store, a `push`, a call whose callee writes the parameter and
  moving it into another binding leave the name unreassigned, so each is written with `let`. A `var` bound to
  an array, map or tuple literal that is written through is written `let items = [..].clone()`: a plain `let`
  of a literal is constant data, and writing through it is **E3207**.
- **Every variable must be used (E3012).** This covers `let`/`var`, parameters, loop variables, pattern
  bindings and closure parameters. The name `_` discards: it creates no binding and may appear several
  times in one pattern (`for (_, _) in pairs`). A parameter named `_` takes its argument unnamed, so
  reading `_` in the body is **E2004**, and one signature may declare several. In a destructuring, `_`
  discards its element: `let (a, _) = t` declares `a` alone, and `(let _, let b) = t` declares `b`
  alone. Names such as `_x` are ordinary variables. A method that
  implements an interface requirement is exempt for its parameters, since the interface dictates them.
- **Records are shared, not copied.** `let b = a` for a record-typed `a` hands `b` the same record; use
  `a.clone()` for an independent copy (see
  [Reference-by-Default Assignment](/docs/language/memory-model/#reference-by-default-assignment)).
  Numbers, `bool` and other scalar values are always independent copies.
- **`let b = a` moves `a` when `a` is a `var`.** When `a` is a `let`, `b` names the same record
  and `a` stays usable. `a.clone()` is the independent copy (see [Moves and Sharing](/docs/language/memory-model/#moves-and-sharing)):

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
  	let b = a                 // a MOVES into b; reading a afterwards is E3102
  	b.x = 10
  	a = Point.create(2)       // assigning a makes it usable again

  	let c = Point.create(3)
  	let d = c                 // d names c's record
  	let e = c                 // so does e, and c stays usable
  	e.x = 30
  	let f = c.clone()         // an independent record
  	f.x = 40
  	print("{a.x} {b.x} {c.x} {d.x} {f.x}\n")   // 2 10 30 30 40
  	return 0
  end 'main'
  ```

## Top-Level Bindings

A `let` can be declared at file scope, outside any function:

```maxon
typealias Tally = int(0 to u64.max)

let START = 10 as Tally
let MAX_SIZE = 1024
let names = ["ada", "alan"]
let dataFile = FilePath from "data.txt"

function main() returns ExitCode
	print("{START} {MAX_SIZE} {names.count()} {dataFile.filename()}\n")   // 10 1024 2 data.txt
	return 0
end 'main'
```

- `let` declares a constant any function in the file can read. A file-scope `var`, with or without `module`
  or `export` before it, is a syntax error: state a function updates is a local `var` passed to a `mutable`
  parameter, a field of a record, a [`SharedValue`](/docs/language/async/#sharedvalue--a-live-value-made-at-run-time), or the
  fields of a [service](/docs/language/async/#services--spawn).
- A binding shares its directory's one namespace with every other top-level name (see
  [Bare Names and Ambiguity](/docs/language/namespaces/#bare-names-and-ambiguity)), and another directory's binding is read through
  its directory: `alpha.LIMIT`.
- An initializer is a value the compiler knows before the program runs, and it **calls nothing**: literals,
  arithmetic, casts, enum and union cases, array, map and tuple literals, `Type from "literal"`,
  `ArrayAlias from [...]`, a function name (`let step = countdown`), and another top-level `let`, declared
  before or after it, in this file or one it can see (`let B = A`). A call is **E3220** (`the initializer of
  'shared' calls 'Cache.create': a module-level or static `let` initializer calls nothing, so write a literal,
  or build the value where it is used`), and a call in the initializer of a number, `bool` or enum binding is
  **E2045** (`Function calls are not allowed in global variable initializers`). A closure literal's body is not
  part of the initializer: `let total = function(n int) gives helper(n)` builds a function value, and `helper`
  runs when `total` is called. A call that receives the closure, `wrap(function(n int) gives helper(n))`, is
  still a call the initializer makes.
- An initializer is outside every function, so a `return` in it is **E2080**, naming the binding.
- A binding that holds a scalar — a number, a `bool` or an enum case — is a compile-time
  constant, inlined at every use. Its initializer must have a value known at compile time, as a body knows
  `2 * WIDTH` or `sqrt(4.0)`. A derived constant keeps its operands' typealias, as in a
  body: with `let a = 5 as Small`, `let s = a + a` is a `Small`.
- A name in an initializer resolves as it does in a function body: a name nothing declares is
  **E3004**, one the file cannot see is **E3008** (**E3088** for a `module` one), and one several directories
  declare is **E3095**. That one diagnostic covers every read and assignment of the binding. A tuple
  assignment naming a module-level binding is **E2015**; assign the element to a local and store that.
- Nothing runs before `main` on behalf of a top-level `let`. Initializers that depend on each other in a
  cycle are **E2012**, and their order in the source does not matter. A `let`'s value is image data, laid down
  in read-only memory as one copy that every function and thread reads, and it is `immutable` (see
  [Constant Data](/docs/language/regions/#constant-data)); `.clone()` gives a mutable copy where one is needed. A static field
  follows the same rules (see [Static Fields](/docs/language/composite-types/#static-fields)).
- A struct literal at file scope is **E3076**; declare a `static let` holding the literal inside the type
  (`static let origin = Self{x: 0, y: 0}`), or build the record where it is used.
- An initializer whose value has an interface type is **E2015**: an interface value is two words, a value
  and its witness, and a global holds one. Return a concrete type from the initializer, or hold the interface
  in a function's local.
- A tuple literal is accepted in an initializer wherever a function body accepts one, with the same type.
  `let pair = (1, "one")` is a tuple of the types its elements have; a slot that states a tuple type —
  an element of `ArrayAlias from [...]` (`let steps = Steps from [(0, 1), (2, 3)]` over
  `typealias Steps = Array with Step` and `typealias Step = (Word, Word)`), a struct literal's field
  (`static let origin = Holder{step: (7, 9)}`), a factory parameter, a union payload, or an element of another
  tuple — gives each element that slot's type. A tuple literal inside extra parentheses is the same tuple, and a
  `var` born from a tuple of bare numbers is refused as it is in a body.
- A module-level `let` is `immutable` image data (see [Constant Data](/docs/language/regions/#constant-data)): a write through it is
  **E3207**, and storing it where something may write it is **E3196**.
- A `spawn` in a global initializer is **E3220**, like any call; start services in `main`, or declare a
  program-wide one with [`default`](/docs/language/async/#program-wide-defaults--default). A `Key.current()` or `Key.register()`
  in a global initializer is [E3173](/docs/cli/error-codes/#e3173--registryreachedfromglobalinitializer): the
  defaults are built after every global initializer has run.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
