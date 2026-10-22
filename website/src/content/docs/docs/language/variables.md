---
title: Variables
description: Mutable (var) and immutable (let) bindings, including top-level variables.
sidebar:
  order: 6
---

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

## Immutable Variables (`let`)

```maxon
let pi = 3.14159
let name = "Maxon"
// pi = 3.14            // E2013: cannot assign to immutable variable: 'pi'
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

So a `let` binds only a value nothing writes while it lives:

- a fresh record no other name reaches (`let x = Box.create(1)`), frozen at birth;
- an element a library `pop`, `remove`, `removeFirst` or `removeLast` moves out of a container
  (`let x = try self.items.pop() otherwise return`), which the container no longer holds;
- a read-only value, such as a module-level `let`'s record or a `let` field of a read-only holder;
- a part of a parameter (`let z = p.child`), which is as immutable as the argument the caller passed. A caller
  that passes the same record through a second, writable argument may write it while the `let` lives.

A `let` bound to state something may still write — a field of a writable `self` or `var`, an element of a `var`
container, a getter's result on a `var` holder, a parameter the function writes, a `var`'s record that a
container also holds — is **E3188** (``cannot bind `let` 'b' to a value reached through 'holder', which something
may still write; use `view` to read it, `var` to write it, or bind a clone()``). A function that returns a
`let`-bound value returns a read-only value.

## Views (`view`)

`view x = e` binds any value read-only through `x`. Unlike a `let`, a `view` promises nothing about other names: a
write made through another name, or by another coroutine across an `await`, is seen through the view.

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
	var h = Holder.create()
	view v = h.box            // `let v = h.box` is E3188: h may still write it
	h.box.n = 7
	print("{v.n}\n")          // 7
	// v.n = 8                // E3159: 'v' is read-only (a `view`)
	return 0
end 'main'
```

- Writing through a view, or handing it to a parameter the callee writes, is **E3159**.
- A view lives only inside its function. Storing it, handing it to a parameter that keeps it, returning it (or a
  call's result that is still the view) and capturing it in a closure are **E3078**; sending it to a service is
  **E3138**. Use a `clone()` for any of them. A view of a number or another scalar is copied where it goes.
- A view holds its own reference to the record it names. Once a view of a `var` is declared — of the `var`
  itself, a field or an element of it, or any expression that may hand its record back (a ternary, an
  `otherwise`, a `match`, a call) — the view co-owns that record for the rest of the function, read again or
  not: sending it (**E3138**), publishing it (**E3174**) or handing it to a `sole`
  parameter (**E3078**) is refused. The rule follows source order, so inside a loop a view declared earlier in the
  body counts. Reassigning the `var` a fresh record makes it sole again, and returning the `var` is unaffected.
- A record nothing can write — a heap type whose fields are all `let` — is shared instead: it is sent, published
  or handed to a `sole` parameter as it is, and the view stays valid. An inline packed record is copied.
- `view` is a [contextual keyword](/docs/language/overview/#keywords). It declares a local binding only: a `view` field, a module-level
  `view`, a `view` in an extension body or an `@heap view` is **E2077**.

## Rules

- Every variable is initialized where it is declared, and its type is inferred from the initializer.
  A declaration takes no type annotation: `var x as Tally = 0` is **E2010** (`Expected '=' but got 'as'`).
  A cast names the type instead — `var x = 0 as Tally` declares a `Tally`, locally and at file scope.
- **A `var` is born a value of its alias, and every assignment to it is checked against it** (see
  [Range Checks](/docs/language/ranged-typealiases/#range-checks)).
- **A `var` takes the brand of the first branded value stored into it** when its initializer has none:
  `var a = [1 as Item, 2 as Item]` followed by `a = xs` for an `Xs` makes `a` an `Xs` for its whole life,
  and a later store of a `Ys` is **E3005** (`cannot assign a value of type 'Ys' to variable 'a', which
  holds 'Xs'`). The first store in source order decides; for a module `var`, the first in the program's
  file order. See [Generic-Instance and Function-Type Aliases Are Brands](/docs/language/ranged-typealiases/#generic-instance-and-function-type-aliases-are-brands).
- Variables are block-scoped.
- **A `var` that is never reassigned or mutated is E3077** (`variable 'x' is never reassigned; use 'let'
  instead of 'var'`) when a `let` in its place would compile: its initializer is a fresh record or a
  read-only value, and nothing stores it where something may write it and reads it again.
- **Every variable must be used (E3012).** This covers `let`/`var`, parameters, loop variables, pattern
  bindings and closure parameters. The name `_` discards: it creates no binding and may appear several
  times in one pattern (`for (_, _) in pairs`). A parameter named `_` takes its argument unnamed, so
  reading `_` in the body is **E2004**, and one signature may declare several. In a destructuring, `_`
  discards its element: `let (a, _) = t` declares `a` alone, and `(let _, let b) = t` declares `b`
  alone. Names such as `_x` are ordinary variables. A method that
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

var callCount = 0 as Tally
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
- A call in an initializer names its function as a call in a function body does: a name nothing declares is
  **E3004**, one the file cannot see is **E3008** (**E3088** for a `module` one), and one several directories
  declare is **E3095**. That one diagnostic covers every read and assignment of the binding; a tuple
  assignment naming the binding is **E2015** as well.
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
  of it (`var m = H.index`) or such a call result, an element accessor's included (`var p = Origins.get(0)`),
  to a `var` is **E3078**. Read it through a `let`, or bind a
  `clone()`. Storing its record anywhere something may write it — pushing it into a container, local or
  module-level, storing it into a `var` field, putting it in an array literal or a map value, or handing it to
  a function that keeps its parameter — is **E3078** too; store a `clone()`.
- A `spawn` reachable from a global initializer is **E3164**; start services in `main`, or declare a
  program-wide one with [`default`](/docs/language/async/#program-wide-defaults--default). A `Key.current()` or `Key.register()`
  reachable from a global initializer is [E3173](/docs/cli/error-codes/#e3173--registryreachedfromglobalinitializer): the
  defaults are built after every global initializer has run.
- A top-level declaration is private to its file unless marked `export`, `module` or `public`.
- A [service](/docs/language/async/#services--spawn) handler, and every function it reaches, may not read or write a module-level
  `var` (**E3143**); keep service state in its fields.
