---
feature: dead-function-elimination
status: stable
keywords: [dce, dfe, reachability, unreachable, function-value, witness, descriptor]
category: codegen
---

# Dead Function Elimination

## Documentation

A function that cannot be reached from the program's roots is not emitted. The prune runs once, at
the target-neutral backend entry, so x64, arm64 and wasm all inherit it; everything after it — instruction
selection, register allocation, frames, encoding, linking — is work the compiler simply does not do for
code that can never execute.

**Reachability, not a use count.** A function is live iff a root reaches it through a chain of static
edges. Two dead functions that call each other therefore both go, even though each has a caller.

**The roots** are `main` plus every reference no Std op in the module spells: the two functions a
hand-assembled runtime chunk calls by name, and every function named by an `.rdata` slot (a layout
descriptor's `destroyFunc@40`/`copyFunc@48`, a witness table's method slots).

**The edges** are the direct call graph (`call`, `tryCall`) plus `funcAddr` — taking a function's ADDRESS
is a static edge even though the call that consumes it is indirect. That is what keeps a function used
only as a first-class value alive, and it is the arm whose absence is a silent miscompile rather than a
missed optimization.

Only CODE is pruned. `.rdata` is left whole, which is why every function an `.rdata` slot names is rooted
unconditionally: the slot survives, so its target must be there to fill it.

## Tests

<!-- test: unreachable-function-pruned -->
A function nothing calls is not emitted. The committed fragment is the assertion: it contains `main` and
nothing else.
```maxon
typealias Integer = int(i64.min to i64.max)

function unusedHelper(x Integer) returns Integer
	return x * 3
end 'unusedHelper'

function main() returns ExitCode
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: unreachable-cone-pruned -->
The whole cone goes, not just its entry: `deadOuter` is unreachable, so `deadInner` — which only
`deadOuter` calls — is unreachable too.
```maxon
typealias Integer = int(i64.min to i64.max)

function deadInner(x Integer) returns Integer
	return x + 1
end 'deadInner'

function deadOuter(x Integer) returns Integer
	return deadInner(x) * 2
end 'deadOuter'

function main() returns ExitCode
	return 5
end 'main'
```
```exitcode
5
```

<!-- test: mutually-recursive-dead-pair-pruned -->
Liveness is REACHABILITY, not a use count. `pingDead` and `pongDead` each have a caller — each other —
so a "does anything call this?" test would keep both. Neither is reachable from `main`, so both go.
```maxon
typealias Integer = int(i64.min to i64.max)

function pingDead(n Integer) returns Integer
	if n <= 0 'base'
		return 0
	end 'base'
	return pongDead(n - 1)
end 'pingDead'

function pongDead(n Integer) returns Integer
	return pingDead(n - 1)
end 'pongDead'

function main() returns ExitCode
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: function-value-target-kept -->
`triple` is never CALLED by name — its only reference is the function value `let f = triple`, which
lowers to the address of a `__fnref_` thunk that forwards to it. Taking a function's address is a static
reachability edge; pruning `triple` here would be a link failure, and pruning the thunk a wrong call.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function triple(n Integer) returns Integer
	return n * 3
end 'triple'

function main() returns ExitCode
	let f = triple
	return f(14)
end 'main'
```
```exitcode
42
```

<!-- test: witness-method-kept -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
`Point.digest` is reached only through the `.rdata` witness table `Box`'s constrained `T` dispatches on —
there is no `call` naming it anywhere in the module. It is rooted because the table's method slot names
it.
```maxon
typealias Code = int(0 to u32.max)
typealias Coord = int(0 to 1000)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Coord
	export var y as Coord
	export static function create(x Coord, y Coord) returns Self
		return Self{ x: x, y: y }
	end 'create'
	export function digest() returns Code
		return self.x * 31 + self.y
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		return self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3, y: 4))
	return b.itemDigest()
end 'main'
```
```exitcode
97
```

<!-- test: descriptor-destructor-kept -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A `Container with String`'s element destructor is named by ONE thing in the whole program: the
`destroyFunc@40` slot of the instance's `.rdata` layout descriptor, which the shared opaque `Array` body
reads back at runtime and calls indirectly. Pruning it leaves that slot pointing at freed code — so the
exit code alone would not catch it, but the leak gate would (the elements are never released). Proved
alongside a prune: `unusedInThisProgram` in the same file is dropped.
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function push(item Element)
		self.items.push(item)
	end 'push'
end 'Container'

typealias StringContainer = Container with String

function unusedInThisProgram(x Integer) returns Integer
	return x * 9
end 'unusedInThisProgram'

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("a string long enough to need the heap")
	sc.push("and a second one, likewise heap-allocated")
	if sc.count() == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
