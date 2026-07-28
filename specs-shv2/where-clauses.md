---
feature: where-clauses
status: stable
keywords: [where, constraints, type-parameters, generics, interfaces, witness]
category: type-system
---

# Where Clauses

## Documentation

A `where` clause constrains a generic type parameter to require interface conformance:
`type Box uses T where T is Digest`. Inside the shared generic body the concrete type is unknown, so a
method call on the constrained parameter (`self.item.digest()`) dispatches through a runtime WITNESS TABLE
(dictionary-passing) rather than a direct call. At each instantiation (`typealias PointBox = Box with Point`)
the compiler checks that the concrete argument conforms to the constrained interface, reporting E3017 if not.

Multiple interfaces on one parameter chain with `and` (`where T is Digest and Tagged`); multiple constrained
parameters separate with `,` (`where T is Digest, U is Tagged`).

## Tests

<!-- test: where-clauses.witness-user-dispatch -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Dispatch a USER interface method through the witness of a constrained type parameter — the concrete type is
unknown in `Box.itemDigest`'s shared body, so `self.item.digest()` goes through the `(Point, Digest)` witness.
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
	let p = Point.create(3, y: 4)
	let b = PointBox.create(p)
	if b.itemDigest() == 97 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.witness-dispatch-in-loop -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The same witness dispatch driven in a loop — the fused witness call and its element borrow must be leak-free
across iterations.
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
		return self.x + self.y
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
	let p = Point.create(2, y: 3)
	let b = PointBox.create(p)
	var total = b.itemDigest()
	var i = 0
	while i < 10 'loop'
		total = b.itemDigest()
		i = i + 1
	end 'loop'
	if total == 5 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.two-constraints -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Two constraints on one parameter (`where T is Digest and Tagged`) reserve two witness slots; a dispatch of
each interface's method reads its own slot.
```maxon
typealias Code = int(0 to u32.max)
typealias Coord = int(0 to 1000)

interface Digest
	function digest() returns Code
end 'Digest'

interface Tagged
	function tag() returns Code
end 'Tagged'

type Point implements Digest, Tagged
	export var x as Coord
	export var y as Coord
	export static function create(x Coord, y Coord) returns Self
		return Self{ x: x, y: y }
	end 'create'
	export function digest() returns Code
		return self.x + self.y
	end 'digest'
	export function tag() returns Code
		return self.x * self.y
	end 'tag'
end 'Point'

type Box uses T where T is Digest and Tagged
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function combined() returns Code
		return self.item.digest() + self.item.tag()
	end 'combined'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(3, y: 4)
	let b = PointBox.create(p)
	if b.combined() == 19 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.forward-through-sibling -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A generic method that dispatches on `T` reached through a sibling self-call — the caller forwards its own
witness slot to the sibling (no reload), so both drive the same witness.
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
		return self.x + self.y
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function inner() returns Code
		return self.item.digest()
	end 'inner'
	export function outer() returns Code
		return self.inner()
	end 'outer'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(3, y: 4)
	let b = PointBox.create(p)
	if b.outer() == 7 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.witness-temporary-arg -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The element passed to the container is a TEMPORARY (an rvalue constructor result, not a named binding).
Its borrow escapes into the container's field, so it is RETAINED (co-owned) at the constructor feed — the
box holds a real second reference that outlives the temporary's statement, the later witness dispatch reads
a live element, and the element is freed exactly once: the box's destructor decrefs its co-owned reference,
then the temporary's own drop releases the last one.
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
	if b.itemDigest() == 97 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.int-actual-for-float-formal -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
An INTEGRAL actual supplied for a `float` interface FORMAL is widened at the witness dispatch, exactly as a
direct call's argument is (`LowerMaxonToStd.widenIntArgsToFloatParams`). A witness dispatch has no callee
signature to look the parameter up in, so nothing used to widen it and the fused `witnessCall`'s
`argFloatMask` — read off the already-typed argument VALUES — declared the argument a GPR/i64 against an
impl declaring XMM/f64. **x64 compiled clean and answered WRONG** (`movRegImm32 rdx, 2` ahead of
`callMem [rbx + 24]`, so `k * 2.0` multiplied whatever was left in xmm0) and **wasm trapped `indirect call
type mismatch`**. `scale(2)` must therefore read `4.0`, and the second dispatch pins that a genuine `float`
actual is still passed through untouched (`promoteToFloat` is idempotent).
```maxon
typealias Coord = int(0 to 1000)

interface Scaled
	function scale(k float) returns float
end 'Scaled'

type Point implements Scaled
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function scale(k float) returns float
		return k * 2.0
	end 'scale'
end 'Point'

type Box uses T where T is Scaled
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function fromInt() returns float
		return self.item.scale(2)
	end 'fromInt'
	export function fromFloat() returns float
		return self.item.scale(1.5)
	end 'fromFloat'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.fromInt() == 4.0 and b.fromFloat() == 3.0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.float-formal-among-int-formals -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The formal→actual MAPPING under the widening above, which a one-off would silently break: the receiver is
`args[0]` and interface formal `j` is `args[j+1]`, so in `combine(a int, k float, b int)` exactly the MIDDLE
actual is widened and its two integral neighbours are passed through. Widening the wrong position puts a
double where the impl declares an `int` (and leaves the `int` where it declares a double), which is a wrong
answer on x64 and an `indirect call type mismatch` on wasm — neither of which a single-float-formal test can
see. `1 + 3 + 5` reads `9.0`.
```maxon
typealias Coord = int(0 to 1000)

interface Mixed
	function combine(a int, k float, b int) returns float
end 'Mixed'

type Point implements Mixed
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function combine(a int, k float, b int) returns float
		return k + (a as float) + (b as float)
	end 'combine'
end 'Point'

type Box uses T where T is Mixed
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns float
		return self.item.combine(1, 3, 5)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 9.0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.error.instantiate-nonconforming -->
A concrete argument that does not implement the constrained interface is rejected at the instantiation (E3017).
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Plain
	export var v as Code
	export static function create(v Code) returns Self
		return Self{ v: v }
	end 'create'
end 'Plain'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
end 'Box'

typealias PlainBox = Box with Plain

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:22:11: type 'Plain' does not implement 'Digest', which the `where` clause on generic type 'Box' requires of its type parameter
```
