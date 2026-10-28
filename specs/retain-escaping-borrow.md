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

`__mm_retain` co-ownership closes it: a
trivial struct/instance stored into a generic field is RETAINED at the constructor feed, so the box
holds a REAL second reference. The box's destructor decrefs that reference, and the caller's own drop
decrefs the caller's reference — freed EXACTLY once on every path, whether the source is a temporary,
a named inner binding, an outer binding, or loop-carried. The consume boundary stays where it is (the
caller keeps `p`): every heap struct is refcounted.

This is a BOUNDED refcount exception for a co-owned trivial struct, not a general refcounting scheme.
A SHARED-body reassignment of a co-owned trivial field (`self.saved = next`) releases the displaced
value through the instance descriptor and takes the new value's own reference, so the old element is
freed exactly once and the new one is shared with the caller.

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
	static function create(x Coord, y Coord) returns Self
		return Self{ x: x, y: y }
	end 'create'
	function digest() returns Code
		return self.x * 31 + self.y
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemDigest() returns Code
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
	static function create(x Coord, y Coord) returns Self
		return Self{ x: x, y: y }
	end 'create'
	function digest() returns Code
		return self.x * 31 + self.y
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemDigest() returns Code
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
	static function create(x Coord, y Coord) returns Self
		return Self{ x: x, y: y }
	end 'create'
	function digest() returns Code
		return self.x * 31 + self.y
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	function itemDigest() returns Code
		return self.item.digest()
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	var b = PointBox.create(Point.create(0, y: 0))
	var i = 0 as FullRangeInt
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
	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
	function getX() returns Integer
		return self.x
	end 'getX'
end 'Point'
type Box uses T
	export var item as T
	static function create(v T) returns Self
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
	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
	function getX() returns Integer
		return self.x
	end 'getX'
end 'Point'
type Box uses T
	export var item as T
	static function create(v T) returns Self
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
	static function create(v T) returns Self
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

<!-- test: reassign-co-owned-trivial-in-shared-body -->
A shared-body reassignment of a type-parameter field that a trivial-struct instantiation co-owns drops the
displaced element through the descriptor and takes its own reference to the new one: the old element is
freed exactly once, the new one lives in the box, and the run exits clean.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var b = PointBox.create(Point.create(7))
	b.replace(Point.create(9))
	print("{b.saved.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: reassign-co-owned-trivial-from-a-binding-and-from-the-displaced-element -->
A shared-body reassignment from a binding the caller keeps takes a second reference, so the binding stays
readable after the box lets go of the element, and a reassignment from the displaced element itself
retains it before the old reference is released.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function replace(next Element)
		self.saved = next
	end 'replace'
	function keep()
		self.replace(self.saved)
	end 'keep'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var kept = Point.create(4)
	var b = PointBox.create(Point.create(1))
	b.replace(kept)
	b.replace(Point.create(2))
	b.replace(kept)
	b.keep()
	print("{b.saved.x} {kept.x}\n")
	b.replace(Point.create(3))
	b.keep()
	print("{b.saved.x} {kept.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 4
3 4
```

<!-- test: reassign-co-owned-trivial-in-a-static-factory-and-a-loop -->
A reassignment written in a `static` function that builds `Self`, and one repeated across a loop, release
every displaced element: the run exits clean with the last element in place.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	static function rebuilt(first Element, second Element) returns Self
		var built = Self.create(first)
		built.saved = second
		return built
	end 'rebuilt'
	function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let b = PointBox.rebuilt(Point.create(1), second: Point.create(2))
	print("{b.saved.x}\n")
	var c = PointBox.create(Point.create(0))
	for round in 1 to 50 'rounds'
		c.replace(Point.create((round * 7) as Coord))
	end 'rounds'
	print("{c.saved.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
350
```

<!-- test: reassign-one-shared-body-serves-string-scalar-and-co-owned-trivial-instantiations -->
One shared reassigning body serves a managed `String`, a scalar and a co-owned trivial struct at once:
each instantiation releases exactly what it owns and the run exits clean.
```maxon
typealias Coord = int(0 to 1000)
typealias Small = int(0 to 255)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias PointBox = Box with Point
typealias TextBox = Box with String
typealias SmallBox = Box with Small
function main() returns ExitCode
	var p = PointBox.create(Point.create(1))
	var t = TextBox.create("one")
	var s = SmallBox.create(1)
	p.replace(Point.create(2))
	t.replace("two")
	s.replace(2)
	let word = "three"
	t.replace(word)
	print("{p.saved.x} {t.saved} {s.saved} {word}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 three 2 three
```

<!-- test: a-local-bound-from-an-opaque-field-survives-the-field-being-reassigned -->
A local bound from a type-parameter field holds a reference of its own, so reassigning that field afterwards
releases the box's reference and leaves the local's element alive: a rotation through a local over a
co-owned trivial struct hands each element to the other box.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function rotate(other Self)
		var held = other.saved
		other.saved = self.saved
		self.saved = held
	end 'rotate'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var b = PointBox.create(Point.create(1))
	var c = PointBox.create(Point.create(2))
	b.rotate(c)
	print("{b.saved.x} {c.saved.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: a-local-bound-from-an-opaque-field-survives-the-field-being-reassigned-for-a-string -->
The same rotation over a managed `String` instantiation.
```maxon
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function rotate(other Self)
		var held = other.saved
		other.saved = self.saved
		self.saved = held
	end 'rotate'
end 'Box'
typealias TextBox = Box with String
function main() returns ExitCode
	var b = TextBox.create("one")
	var c = TextBox.create("two")
	b.rotate(c)
	print("{b.saved} {c.saved}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
two one
```

<!-- test: a-swap-through-a-local-bound-from-the-receivers-own-field -->
A swap that binds the local from the receiver's own field and writes the receiver first.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function swapWith(other Self)
		var held = self.saved
		self.saved = other.saved
		other.saved = held
	end 'swapWith'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	var b = PointBox.create(Point.create(1))
	var c = PointBox.create(Point.create(2))
	b.swapWith(c)
	print("{b.saved.x} {c.saved.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: reassign-co-owned-trivial-instantiated-by-another-file-box-first -->
The same reassignment when the co-owning instantiation is written nowhere: `Box with Point` exists only
because a SECOND file calls a method through an `Outer with Point`. The shared body drops the displaced
element whichever file is parsed first — the instantiation is a fact about the program, not about the files
parsed before the body.
```maxon
// --- file: a_box.maxon
export type Box uses T
	export var saved as T
	export static function create(first T) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next T)
		self.saved = next
	end 'replace'
end 'Box'
export type Outer uses T
	export var inner as Box
	export static function create(first T) returns Self
		return Self{ inner: Box.create(first) }
	end 'create'
	export function swapInner(next T)
		self.inner.replace(next)
	end 'swapInner'
end 'Outer'
// --- file: b_use.maxon
type Point
	var x as Count
	static function create(x Count) returns Self
		return Self{ x: x }
	end 'create'

	function xValue() returns Count
		return self.x
	end 'xValue'
end 'Point'
typealias PointOuter = Outer with Point
function main() returns ExitCode
	var o = PointOuter.create(Point.create(7))
	o.swapInner(Point.create(9))
	print("{o.inner.saved.xValue()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: reassign-co-owned-trivial-instantiated-by-another-file-use-first -->
```maxon
// --- file: a_use.maxon
type Point
	var x as Count
	static function create(x Count) returns Self
		return Self{ x: x }
	end 'create'

	function xValue() returns Count
		return self.x
	end 'xValue'
end 'Point'
typealias PointOuter = Outer with Point
function main() returns ExitCode
	var o = PointOuter.create(Point.create(7))
	o.swapInner(Point.create(9))
	print("{o.inner.saved.xValue()}\n")
	return 0
end 'main'
// --- file: b_box.maxon
export type Box uses T
	export var saved as T
	export static function create(first T) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next T)
		self.saved = next
	end 'replace'
end 'Box'
export type Outer uses T
	export var inner as Box
	export static function create(first T) returns Self
		return Self{ inner: Box.create(first) }
	end 'create'
	export function swapInner(next T)
		self.inner.replace(next)
	end 'swapInner'
end 'Outer'
```
```exitcode
0
```
```stdout
9
```

<!-- test: an-unqualified-field-reassignment-from-a-kept-parameter-holds-its-own-reference -->
A shared body reassigns the type-parameter field by its bare name from a parameter the caller keeps, for a
struct of scalars and a `String`, in a loop.
```maxon
typealias Coord = int(0 to 1000)
type Point
	export var x as Coord
	static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'Point'
type Box uses Element
	export var saved as Element
	static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	function replace(next Element)
		saved = next
	end 'replace'
end 'Box'
typealias PointBox = Box with Point
typealias TextBox = Box with String
function main() returns ExitCode
	var points = PointBox.create(Point.create(1))
	var texts = TextBox.create("one")
	var keptPoint = Point.create(7)
	var keptText = "seven"
	for _ in 0 upto 20 'swap'
		points.replace(keptPoint)
		texts.replace(keptText)
	end 'swap'
	print("{points.saved.x} {texts.saved} {keptPoint.x} {keptText}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 seven 7 seven
```
