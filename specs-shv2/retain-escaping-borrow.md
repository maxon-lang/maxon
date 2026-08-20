---
feature: retain-escaping-borrow
status: stable
keywords: [generics, ownership, retain, co-ownership, borrow, escape, type-parameters]
category: type-system
---

# Retain an escaping struct type-argument borrow

## Documentation

A TRIVIAL struct or instance argument stored into a generic instance's type-parameter field
(`type Box uses T`; `Self{ item: item }`) is BORROWED at the call site — the caller keeps it usable
(`PointBox.create(p); … p.getX()`). But the container it is stored into may OUTLIVE the borrowed
source: when the container binding lives in an outer scope and its element's source lives in an inner
scope (`var b = …; { let pInner = Point.create(…); b = PointBox.create(pInner) }; …`), a pure borrow
would dangle — the element is freed at the inner scope's exit while the container still points at it.

The fix (user ruling, 2026-07-23) generalizes the OPEN #40 `__mm_retain` co-ownership thesis: a
trivial struct/instance stored into a generic field is RETAINED at the constructor feed, so the box
holds a REAL second reference. The box's destructor decrefs that reference, and the caller's own drop
decrefs the caller's reference — freed EXACTLY once on every path, whether the source is a temporary,
a named inner binding, an outer binding, or loop-carried. The consume boundary is unchanged (the
caller keeps `p`), matching the value oracle (the bootstrap refcounts every heap struct).

This is a BOUNDED refcount exception for a co-owned trivial struct, not a general refcounting scheme.
A SHARED-body reassignment of a co-owned trivial field (`self.saved = next`) cannot drop the old
co-owned value without a trivial-struct single-value drop (a later slice), so it is REJECTED rather
than leaked. (The true E3070 NLL borrow-liveness pass — `arr.get(0); arr.push(x)` — is a separate
future rung.)

## Tests

<!-- test: cross-scope-temporary -->
The RED: a container `b` in the OUTER scope is reassigned inside an inner block to hold a TEMPORARY
element (`b = PointBox.create(Point.create(3, y: 4))`). The temporary is retained (co-owned) by the
box, so a later witness dispatch on `b`'s element reads a LIVE element rather than freed memory, and
the element is freed exactly once at the box's own scope exit.
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
	var b = PointBox.create(Point.create(1, y: 1))
	if true 'reassign'
		b = PointBox.create(Point.create(3, y: 4))
	end 'reassign'
	return b.itemDigest()
end 'main'
```
```exitcode
97
```

<!-- test: named-inner-binding -->
The same escape through a NAMED inner binding (`let pInner = Point.create(3, y: 4); b =
PointBox.create(pInner)`), which the earlier scope-local materialization never covered. `pInner` is
retained, so the inner scope's drop lowers its count without freeing it, and `b`'s later read is live.
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
	var b = PointBox.create(Point.create(1, y: 1))
	if true 'reassign'
		let pInner = Point.create(3, y: 4)
		b = PointBox.create(pInner)
	end 'reassign'
	return b.itemDigest()
end 'main'
```
```exitcode
97
```

<!-- test: loop-carried-reassignment -->
The container is reassigned each iteration to a fresh inner-scope element. Every iteration retains the
new element and the loop-carried reassignment drops the previous container's co-owned element exactly
once — no leak accumulates across iterations, and the final read is live.
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
	var b = PointBox.create(Point.create(0, y: 0))
	var i = 0
	while i < 5 'loop'
		let pIn = Point.create(3, y: 4)
		b = PointBox.create(pIn)
		i = i + 1
	end 'loop'
	return b.itemDigest()
end 'main'
```
```exitcode
97
```

<!-- test: cross-scope-leak-free -->
A plain generic (no witness) exercises the refcount balance on every native target: a temporary
container is reassigned across scopes to hold a named inner element, and the whole thing frees exactly
once under `__mm_free` poisoning — the old container's co-owned element, the inner element, and the
final container all drop once. Leak-free (a double-free faults, an under-release exits 101).
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
	export function getX() returns Integer
		return self.x
	end 'getX'
end 'Point'
type Box uses T
	export var item as T
	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var b = PointBox.create(Point.create(0))
	if true 'inner'
		let pInner = Point.create(41)
		b = PointBox.create(pInner)
	end 'inner'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: source-stays-usable -->
The co-ownership does NOT consume the source: after `PointBox.create(p)` the caller's `p` is still
live and readable, and both the box's co-owned reference and `p`'s own reference free the element
exactly once at scope exit.
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
	export function getX() returns Integer
		return self.x
	end 'getX'
end 'Point'
type Box uses T
	export var item as T
	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let p = Point.create(7)
	let b = PointBox.create(p)
	print("{b.item.x}")
	return p.getX() - 7
end 'main'
```
```exitcode
0
```
```stdout
7

```

<!-- test: trivial-instance-arg-co-owned -->
The retained argument may itself be a trivial generic INSTANCE (`Box with (Box with Integer)`): the
outer box co-owns the inner instance box across scopes, drops it once at its own destruction, and the
inner box's own scope drop frees it once. Leak-free under poisoning.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var item as T
	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
typealias BoxBox = Box with (Box with Integer)
function main() returns ExitCode
	var outer = BoxBox.create(IntBox.create(0))
	if true 'inner'
		let innerBox = IntBox.create(5)
		outer = BoxBox.create(innerBox)
	end 'inner'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.reassign-co-owned-trivial-in-shared-body -->
A shared-body reassignment of a type-parameter field that a trivial-struct instantiation co-owns is
rejected: the box retains and drops the co-owned trivial struct, but a shared-body reassignment cannot
drop the old co-owned value without the trivial-struct single-value drop (a later slice). Rejected
cleanly rather than leaked.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var b = PointBox.create(Point.create(7))
	b.replace(Point.create(9))
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:8: Unsupported: reassigning the type-parameter field 'saved' of 'Box' in a shared generic body, where a trivial-struct instantiation co-owns the field — the box retains a co-owned trivial struct at construction and drops it once at destruction, but a shared-body reassignment cannot drop the old co-owned value (the descriptor-gated single-value drop for a trivial struct is a later slice); reassign the field on a concrete instance, or use a managed element type
```
