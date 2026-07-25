---
feature: primitive-conformance
keywords: [hashable, equatable, primitives, int, witness, where, generics]
category: type-system
---

# Primitive Interface Conformance

## Documentation

The primitive `int` conforms to the `Hashable` and `Equatable` protocol interfaces WITHOUT a user
`implements` clause — the compiler synthesizes its `int.hash` / `int.equals` implementations natively, the
type-side twin of the synthesized `Hashable`/`Equatable` interfaces. A ranged int alias (`Integer =
int(0 to u32.max)`) conforms through its primitive, so `Box with Integer where T is Hashable` is legal.

- `int.hash()` returns the low 32 bits of the value (`self and 0xFFFFFFFF`, type `HashValue = int(0 to
  u32.max)`) — identity for a value that already fits `u32`. This is exactly what a hash-based `Set`/`Map`
  needs of its keys.
- `int.equals(other)` returns `self == other`.
- `byte` (`int(0 to u8.max)`) rides `int`'s conformance, since it IS a ranged int.

Inside a generic body the concrete type is unknown, so a call on a constrained parameter — `self.item.hash()`,
`self.item.equals(other)`, or the `==`/`!=` OPERATOR on a `T`-typed value — dispatches through the runtime
WITNESS TABLE (dictionary-passing), whose slots for an `int` argument point at the synthesized
`int.hash`/`int.equals`. Using `==`/`!=` on a type parameter that is NOT constrained with `where T is
Equatable` is a compile error (E3005): there is no witness to dispatch through.

Two further E3005 rejects keep that dispatch honest, and both are shared verbatim with the ordering
operators (see `comparable.md`):

- `equals`'s formal is `Self`, which means the RECEIVER'S OWN type parameter — not "some type parameter".
  A concrete literal is refused, and so is a value of a DIFFERENT parameter of the same generic type
  (`type Mix uses A, B where A is Equatable, B is Equatable`, then `self.a == self.b`): the dispatch goes
  through `A`'s witness table, so a struct `A`'s `equals` would dereference whatever word `B` held.
- a constraint is only `Equatable` if it declares `equals` with `Equatable`'s own `bool` RESULT. The
  witness search matches by method NAME (shared with ordinary `.method()` dispatch), so a user
  `interface Weird` with `function equals(other Self) returns Integer` would otherwise be accepted and
  `a == b` would evaluate to whatever integer the author's `equals` returned.

The witness dispatch rides the x64 rdata function-pointer relocation, so these cases are x64-only (as the
`where-clauses` witness cases are); the E3005 reject is a compile error and is target-independent.

Float `hash`, `Comparable`/`Ordering`, and direct `i.hash()` dispatch on a concrete `int` value are separate
future mechanisms and are NOT covered here.

## Tests

<!-- test: primitive-conformance.hash-identity -->
<!-- targets: x64-windows, x64-linux -->
An `int` argument's `Hashable` witness dispatches `element.hash()` to the synthesized `int.hash` — identity
for a value inside `u32`.
```maxon
typealias Integer = int(0 to u32.max)

type Box uses T where T is Hashable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(97)
	if b.itemHash() == 97 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: primitive-conformance.hash-low32-mask -->
<!-- targets: x64-windows, x64-linux -->
`int.hash()` masks to the low 32 bits: a value above `u32.max` folds down, and a negative value's two's
complement folds to its low 32 bits (`-1` -> `4294967295`).
```maxon
typealias Wide = int(i64.min to i64.max)

type Box uses T where T is Hashable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemHash() returns HashValue
		return self.item.hash()
	end 'itemHash'
end 'Box'

typealias WideBox = Box with Wide

function main() returns ExitCode
	let a = WideBox.create(42)
	if a.itemHash() != 42 'p1'
		return 1
	end 'p1'
	let b = WideBox.create(4294967303)
	if b.itemHash() != 7 'p2'
		return 2
	end 'p2'
	let c = WideBox.create(-1)
	if c.itemHash() != 4294967295 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: primitive-conformance.equals-via-constraint -->
<!-- targets: x64-windows, x64-linux -->
An `int` argument's `Equatable` witness dispatches `element.equals(other)` to the synthesized `int.equals`.
```maxon
typealias Integer = int(0 to u32.max)

type Box uses T where T is Equatable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function sameAs(other T) returns bool
		return self.item.equals(other)
	end 'sameAs'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(5)
	if not b.sameAs(5) 'eq'
		return 1
	end 'eq'
	if b.sameAs(9) 'ne'
		return 2
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: primitive-conformance.hashable-and-equatable -->
<!-- targets: x64-windows, x64-linux -->
The `Set` key shape — a parameter constrained with `where T is Hashable and Equatable` dispatches BOTH
witnesses on an `int` argument, each to its synthesized impl.
```maxon
typealias Integer = int(0 to u32.max)

type Key uses T where T is Hashable and Equatable
	export var value as T
	export static function create(value T) returns Self
		return Self{ value: value }
	end 'create'
	export function digest() returns HashValue
		return self.value.hash()
	end 'digest'
	export function matches(other T) returns bool
		return self.value.equals(other)
	end 'matches'
end 'Key'

typealias IntKey = Key with Integer

function main() returns ExitCode
	let k = IntKey.create(7)
	if k.digest() != 7 'h'
		return 1
	end 'h'
	if not k.matches(7) 'e1'
		return 2
	end 'e1'
	if k.matches(9) 'e2'
		return 3
	end 'e2'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: primitive-conformance.eq-with-equatable -->
<!-- targets: x64-windows, x64-linux -->
The `==` OPERATOR on an Equatable-constrained type parameter lowers to the Equatable witness dispatch — for
an `int` argument that is the synthesized `int.equals`, so `b.eq(42)` is true and the `if` returns 1.
```maxon
type Box uses T where T is Equatable
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function eq(other T) returns bool
		return item == other
	end 'eq'
end 'Box'

typealias Int = int(i64.min to i64.max)
typealias IntBox = Box with Int

function main() returns ExitCode
	let b = IntBox.create(42)
	if b.eq(42) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: primitive-conformance.error.eq-requires-equatable -->
Using `==` on a type parameter that is NOT constrained with `where T is Equatable` is E3005 — there is no
Equatable witness to dispatch through, and a raw scalar compare of two opaque words would be a silent wrong
answer for a struct type argument. Target-independent: a compile error, no witness table.
```maxon
type Box uses T
	var item as T

	export function eq(other T) returns bool
		return item == other
	end 'eq'
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:15: Operator '==' requires type parameter 'T' to be constrained with 'where T is Equatable'
```

<!-- test: primitive-conformance.error.eq-concrete-self-arg -->
`T == <literal>` inside `where T is Equatable` is E3005: `Equatable` only proves equality against another
`Self` (= `T`), so a concrete literal is not a valid operand. Marshalling it into the `Self` pointer would
FAULT for a struct `T` (the callee's `equals(other Point)` dereferences the literal as its `other` pointer) —
so this is rejected at compile time, not miscompiled. Target-independent.
```maxon
typealias Coord = int(0 to 1000)

type Point implements Equatable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function equals(other Point) returns bool
		return self.x == other.x
	end 'equals'
end 'Point'

type Box uses T where T is Equatable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function matchesLiteral() returns bool
		return self.item == 42
	end 'matchesLiteral'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:20: '==' on type parameter 'T' requires an argument of type 'T' (the `Self` its constraint provides), not a concrete value
```

<!-- test: primitive-conformance.error.equals-concrete-self-arg -->
The method-call twin: `T.equals(<literal>)` inside `where T is Equatable` is the SAME E3005 reject, at the
SAME shared witness-dispatch arg site — a concrete literal is not the `Self` the constraint provides, and
would fault for a struct `T`. Target-independent.
```maxon
typealias Coord = int(0 to 1000)

type Point implements Equatable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function equals(other Point) returns bool
		return self.x == other.x
	end 'equals'
end 'Point'

type Box uses T where T is Equatable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function matchesLiteral() returns bool
		return self.item.equals(42)
	end 'matchesLiteral'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:20: 'equals' on type parameter 'T' requires an argument of type 'T' (the `Self` its constraint provides), not a concrete value
```

<!-- test: primitive-conformance.error.eq-cross-type-param -->
`Self` is the RECEIVER'S OWN type parameter, so `self.a == self.b` over two DIFFERENT constrained
parameters is the same E3005 a concrete literal gets: the dispatch goes through `A`'s witness table, and
a struct `A`'s `equals(other Point)` would dereference the integer `B` held as its `other` pointer. The
message names the parameter the offending operand actually has. Target-independent.
```maxon
typealias Coord = int(0 to 1000)
typealias Integer = int(0 to u32.max)

type Point implements Equatable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function equals(other Point) returns bool
		return self.x == other.x
	end 'equals'
end 'Point'

type Mix uses A, B where A is Equatable, B is Equatable
	export var a as A
	export var b as B
	export static function create(a A, b B) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function cross() returns bool
		return self.a == self.b
	end 'cross'
end 'Mix'

typealias PointIntMix = Mix with (Point, Integer)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:17: '==' on type parameter 'A' requires an argument of type 'A' (the `Self` its constraint provides), not a value of type parameter 'B'
```

<!-- test: primitive-conformance.error.non-protocol-equals-result -->
A constraint that declares a method NAMED `equals` is not `Equatable` unless it returns `bool`. The
witness search matches by method name (shared with ordinary `.method()` dispatch), so without this check
`Weird`'s `Integer` result would BE the value of `a == b` — a comparison evaluating to `7`. It reports
the same E3005 as no constraint at all, because the cure is the same sentence. Target-independent.
```maxon
typealias Integer = int(0 to u32.max)

interface Weird
	function equals(other Self) returns Integer
end 'Weird'

type Thing implements Weird
	export var v as Integer
	export static function create(v Integer) returns Self
		return Self{ v: v }
	end 'create'
	export function equals(other Thing) returns Integer
		return 7
	end 'equals'
end 'Thing'

type Pair uses T where T is Weird
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function eq() returns bool
		return self.a == self.b
	end 'eq'
end 'Pair'

typealias ThingPair = Pair with Thing

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:17: Operator '==' requires type parameter 'T' to be constrained with 'where T is Equatable'
```
