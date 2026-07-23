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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
