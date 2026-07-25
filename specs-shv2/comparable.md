---
feature: comparable
keywords: [comparable, ordering, compare, witness, where, generics, enum, match]
category: type-system
---

# Comparable and Ordering

## Documentation

`Ordering` is a compiler-known three-case enum — `lessThan`, `equalTo`, `greaterThan`, in that
declaration order, with the auto-increment tags `0`, `1`, `2`. shv2 reads no stdlib, so it SYNTHESIZES
the declaration (transcribed from `stdlib/Interfaces.maxon`) exactly as it synthesizes the `Hashable` /
`Equatable` / `Comparable` protocol interfaces and the `String` / `Array` / `Set` types. Being an
ordinary enum, its cases are referenced as qualified values (`Ordering.lessThan`), it names a parameter
or return type (`function rank(o Ordering)`), and a `match` over it is a case-name match with the usual
exhaustiveness rule — nothing about it is a special form.

The primitive `int` conforms to `Comparable` WITHOUT a user `implements` clause: the compiler
synthesizes `int.compare` natively, the type-side twin of the synthesized `Comparable` interface (as it
does for `int.hash` / `int.equals`). `int.compare(other)` is `lessThan` when `self < other`,
`greaterThan` when `self > other`, and `equalTo` otherwise. A ranged int alias conforms through its
primitive, so `Pair with Integer where T is Comparable` is legal.

`String` does NOT conform to `Comparable` — a byte record has no ordering, and neither reference
compiler gives it one.

Inside a generic body the concrete type is unknown, so both forms of ordering on a constrained type
parameter dispatch through the runtime WITNESS TABLE (dictionary-passing):

- `self.a.compare(self.b)` calls `Comparable`'s only method through witness slot 0, yielding an
  `Ordering` a `match` can dispatch on.
- the OPERATORS `<` `>` `<=` `>=` on a `T`-typed value lower to that SAME `compare` dispatch followed
  by an ordinal test on its result: `<` is `lessThan`, `>` is `greaterThan`, `<=` is *not*
  `greaterThan`, `>=` is *not* `lessThan`. There is no second comparison path — one witness call, one
  tag test, so the operator and the method can never disagree.

Using `<` `>` `<=` `>=` on a type parameter that is NOT constrained with `where T is Comparable` is a
compile error (E3005): there is no witness to dispatch through, and a raw word compare would be a
POINTER compare for a struct type argument — a silent wrong answer. Comparing a `T` against a concrete
value (`self.item < 42`) is the same E3005 the `==` form gives: `Comparable` proves ordering against
another `Self` (= `T`) only, and marshalling a literal into that `Self` slot would fault.

`Self` means the RECEIVER'S OWN type parameter, not "some type parameter". In a generic with two
constrained parameters (`type Mix uses A, B where A is Comparable, B is Comparable`), `self.a < self.b`
is the same E3005 — the dispatch goes through `A`'s witness table, so a `B` handed to `compare`'s `Self`
formal would be read as an `A` (for a struct `A` that is a dereference of whatever word `B` held). A
genuine cross-parameter comparison needs a second witness and a conversion, and is not this rung. The
`.compare()` method form is refused at the same shared check.

A constraint is only `Comparable` if it declares `compare` with `Comparable`'s own RESULT. The witness
search matches by method NAME (it is shared with ordinary `.method()` dispatch), so a user
`interface Weird` declaring `function compare(other Self) returns Integer` would otherwise be accepted
and its integer result read as an `Ordering` TAG — `a < b` would mean `a.compare(b) == 0`, i.e. "less
than" whenever the author's `compare` said EQUAL. Such a constraint gets the same "constrain it with
`where T is Comparable`" E3005 as no constraint at all. `Equatable`/`==` is guarded identically.

A `typeParameter`-tagged value that is NOT one of an ENCLOSING type's parameters — a concrete
generic-method result read in a non-generic caller (`let v = intBox.get()`) — has a known concrete type
and keeps its ordinary scalar comparison.

A user type may also conform explicitly, declaring `implements Comparable` and supplying
`compare(other Self) returns Ordering`; a missing or mismatched `compare` is E3016, and instantiating a
`where T is Comparable` generic with a non-conforming argument is E3017.

Because `Ordering` is a declaration the COMPILER owns and shv2 has no namespacing to tell a user's
`Ordering` from the builtin, a declaration that would give that name a NOMINAL identity — a `type`, an
`enum`/`union`, or a `function`/`Base with Args` typealias — is refused (E2015) rather than silently
half-shadowing it. The same rule covers the other compiler-owned type names, `HashValue` and `ExitCode`.
A RANGED typealias over one of those names stays legal: it erases to `int`/`float`, which is what the
builtin resolves to as well, so there is no second meaning for the name to acquire.

The witness dispatch rides the x64 rdata function-pointer relocation, so the witness cases are x64-only
(as the `primitive-conformance`, `string-conformance` and `where-clauses` witness cases are). The
compile-error cases and the pure-`Ordering` value cases are target-independent.

Direct `i.compare(j)` on a concrete primitive value, float/bool `Comparable`, `Character` ordering, and
sorting are separate future mechanisms and are NOT covered here.

## Tests

<!-- test: comparable.compare-via-witness -->
<!-- targets: x64-windows, x64-linux -->
The headline shape: `self.a.compare(self.b)` inside `where T is Comparable` dispatches `Comparable`'s
`compare` through witness slot 0 to the synthesized `int.compare`, and the `Ordering` it returns is
matched by case name. Three pairs cover all three verdicts — `3` vs `9` is `lessThan` (1), `7` vs `7`
is `equalTo` (2), `9` vs `3` is `greaterThan` (3) — so the exit code reads `123`.
```maxon
typealias Integer = int(0 to u32.max)

type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function order() returns Integer
		let o = self.a.compare(self.b)
		let r = match o 'check'
			lessThan gives 1
			equalTo gives 2
			greaterThan gives 3
		end 'check'
		return r
	end 'order'
end 'Pair'

typealias IntPair = Pair with Integer

function main() returns ExitCode
	let less = IntPair.create(3, b: 9)
	let same = IntPair.create(7, b: 7)
	let more = IntPair.create(9, b: 3)

	return less.order() * 100 + same.order() * 10 + more.order()
end 'main'
```
```exitcode
123
```

<!-- test: comparable.ordering-qualified-values -->
`Ordering.lessThan` / `.equalTo` / `.greaterThan` are ordinary qualified enum-case value expressions,
and `Ordering` names an ordinary parameter type. No witness table is involved, so this is
target-independent.
```maxon
typealias Integer = int(0 to u32.max)

function rank(o Ordering) returns Integer
	let r = match o 'check'
		lessThan gives 1
		equalTo gives 2
		greaterThan gives 3
	end 'check'
	return r
end 'rank'

function main() returns ExitCode
	return rank(Ordering.lessThan) * 100 + rank(Ordering.equalTo) * 10 + rank(Ordering.greaterThan)
end 'main'
```
```exitcode
123
```

<!-- test: comparable.user-type-direct-compare -->
<!-- targets: x64-windows, x64-linux -->
A user type conforming explicitly: `implements Comparable` plus `compare(other Self) returns Ordering`
type-checks (the interface's `Self` substitutes `Point`), and the method is callable by CONCRETE
dispatch — no witness table — with its `Ordering` result matched by case name.
```maxon
typealias Coord = int(0 to 1000)

type Point implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function compare(other Point) returns Ordering
		if self.x < other.x 'lt'
			return Ordering.lessThan
		end 'lt'
		if self.x > other.x 'gt'
			return Ordering.greaterThan
		end 'gt'
		return Ordering.equalTo
	end 'compare'
end 'Point'

function main() returns ExitCode
	let a = Point.create(3)
	let b = Point.create(9)
	let o = a.compare(b)
	let r = match o 'check'
		lessThan gives 1
		equalTo gives 2
		greaterThan gives 3
	end 'check'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: comparable.ordering-operators-via-witness -->
<!-- targets: x64-windows, x64-linux -->
All four ordering OPERATORS on a `Comparable`-constrained type parameter, each lowered to the `compare`
witness dispatch plus an ordinal test on its `Ordering`. `mask()` packs the four verdicts as
`lt|le<<1|gt<<2|ge<<3`, so `3 < 9` reads `3` (lt+le), `7` vs `7` reads `10` (le+ge) — the case that
proves `<=`/`>=` accept `equalTo` — and `9` vs `3` reads `12` (gt+ge).
```maxon
typealias Integer = int(0 to u32.max)

type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function lt() returns bool
		return self.a < self.b
	end 'lt'
	export function le() returns bool
		return self.a <= self.b
	end 'le'
	export function gt() returns bool
		return self.a > self.b
	end 'gt'
	export function ge() returns bool
		return self.a >= self.b
	end 'ge'
	export function mask() returns Integer
		var m = 0 as Integer
		if self.lt() 'a1'
			m = m + 1
		end 'a1'
		if self.le() 'a2'
			m = m + 2
		end 'a2'
		if self.gt() 'a3'
			m = m + 4
		end 'a3'
		if self.ge() 'a4'
			m = m + 8
		end 'a4'
		return m
	end 'mask'
end 'Pair'

typealias IntPair = Pair with Integer

function main() returns ExitCode
	let low = IntPair.create(3, b: 9)
	if low.mask() != 3 'p1'
		return 1
	end 'p1'
	let same = IntPair.create(7, b: 7)
	if same.mask() != 10 'p2'
		return 2
	end 'p2'
	let high = IntPair.create(9, b: 3)
	if high.mask() != 12 'p3'
		return 3
	end 'p3'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: comparable.ordering-reversed-operands -->
<!-- targets: x64-windows, x64-linux -->
The dispatch is ASYMMETRIC — the LEFT operand is the witness receiver — so `self.b < self.a` is a
different question from `self.a < self.b`. On `(3, 9)` the forward form is true and the reverse false,
so only the forward branch fires and the exit code is `1`.
```maxon
typealias Integer = int(0 to u32.max)

type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function forwardLess() returns bool
		return self.a < self.b
	end 'forwardLess'
	export function reverseLess() returns bool
		return self.b < self.a
	end 'reverseLess'
end 'Pair'

typealias IntPair = Pair with Integer

function main() returns ExitCode
	let p = IntPair.create(3, b: 9)
	var r = 0 as Integer
	if p.forwardLess() 'f'
		r = r + 1
	end 'f'
	if p.reverseLess() 'g'
		r = r + 10
	end 'g'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: comparable.concrete-typeparam-result-stays-scalar -->
<!-- targets: x64-windows, x64-linux -->
A generic-method result read in a NON-generic caller carries the `typeParameter` tag but has a known
concrete type, so `v < 9` stays an ordinary scalar comparison — it must NOT be rerouted to a witness
(there is none: `Box` declares no `where` constraint at all). `7 < 9` holds, so the exit code is `1`.
```maxon
typealias Integer = int(0 to u32.max)

type Box uses T
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function get() returns T
		return self.item
	end 'get'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(7)
	let v = b.get()
	if v < 9 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: comparable.compare-in-loop-no-leak -->
<!-- targets: x64-windows, x64-linux -->
A generic instance constructed, compared through BOTH witness forms (the `compare` method and the `<`
operator), and dropped every iteration of a 100-iteration loop — the standing leak/double-free probe.
If the witness receiver were consumed rather than borrowed, the per-iteration drop would free an
already-freed record; if the box failed to drop, the run would leak. `acc` reaches 200 and the leak
gate stays green.
```maxon
typealias Counter = int(0 to 1000)

type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function isLess() returns bool
		let o = self.a.compare(self.b)
		let r = match o 'check'
			lessThan gives true
			equalTo gives false
			greaterThan gives false
		end 'check'
		return r
	end 'isLess'
	export function opLess() returns bool
		return self.a < self.b
	end 'opLess'
end 'Pair'

typealias CountPair = Pair with Counter

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		let p = CountPair.create(3, b: 9)
		if p.isLess() 'ok'
			acc = acc + 1
		end 'ok'
		if p.opLess() 'ok2'
			acc = acc + 1
		end 'ok2'
		i = i + 1
	end 'loop'
	if acc == 200 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: comparable.error.missing-compare -->
A type declaring `implements Comparable` without supplying `compare` is E3016, positioned at the type
name. The expected signature prints the interface's own spelling, `Self` and `Ordering` included.
```maxon
typealias Coord = int(0 to 1000)

type BadPoint implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
end 'BadPoint'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:4:6: Partial interface implementation: type 'BadPoint' is missing 1 method(s):
  - compare(other Self) returns Ordering
```

<!-- test: comparable.error.wrong-compare-signature -->
A `compare` that returns something other than `Ordering` is E3016 — `Ordering` is a nominal enum, not
an alias for an integer, so a `returns Integer` implementation does not satisfy the interface.
```maxon
typealias Coord = int(0 to 1000)
typealias Integer = int(0 to u32.max)

type WrongPoint implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function compare(other WrongPoint) returns Integer
		return self.x
	end 'compare'
end 'WrongPoint'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:5:6: Partial interface implementation: type 'WrongPoint' has 1 method(s) with wrong signature:
  - compare(other WrongPoint) returns Integer (expected compare(other Self) returns Ordering)
```

<!-- test: comparable.error.instantiate-nonconforming -->
Instantiating a `where T is Comparable` generic with a type that does not implement `Comparable` is
E3017, anchored on the instantiation.
```maxon
typealias Coord = int(0 to 1000)

type Plain
	export var v as Coord
	export static function create(v Coord) returns Self
		return Self{ v: v }
	end 'create'
end 'Plain'

type Pair uses T where T is Comparable
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
end 'Pair'

typealias PlainPair = Pair with Plain

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:19:11: type 'Plain' does not implement 'Comparable', which the `where` clause on generic type 'Pair' requires of its type parameter
```

<!-- test: comparable.error.ordering-requires-comparable -->
`<` on a type parameter that is NOT constrained with `where T is Comparable` is E3005 — there is no
`compare` witness to dispatch through, and a raw word compare would be a pointer compare for a struct
type argument. Target-independent.
```maxon
type Box uses T
	var item as T

	export function lt(other T) returns bool
		return item < other
	end 'lt'
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:15: Operator '<' requires type parameter 'T' to be constrained with 'where T is Comparable'
```

<!-- test: comparable.error.ordering-concrete-self-arg -->
`T < <literal>` inside `where T is Comparable` is E3005, at the SAME shared witness-dispatch argument
check the `==` form uses: `Comparable` only proves ordering against another `Self` (= `T`), so a
concrete literal is not a valid operand, and marshalling it into the `Self` slot would FAULT for a
struct `T` (the callee's `compare(other Point)` would dereference the literal as its `other` pointer).
Target-independent.
```maxon
typealias Coord = int(0 to 1000)

type Point implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function compare(other Point) returns Ordering
		if self.x < other.x 'lt'
			return Ordering.lessThan
		end 'lt'
		if self.x > other.x 'gt'
			return Ordering.greaterThan
		end 'gt'
		return Ordering.equalTo
	end 'compare'
end 'Point'

type Box uses T where T is Comparable
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function belowLiteral() returns bool
		return self.item < 42
	end 'belowLiteral'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:26:20: '<' on type parameter 'T' requires an argument of type 'T' (the `Self` its constraint provides), not a concrete value
```

<!-- test: comparable.error.declare-ordering-enum -->
`Ordering` is a declaration the COMPILER owns — shv2 synthesizes it instead of reading `stdlib/`, and
has no namespace to tell a user `Ordering` from the builtin one. A user declaration binding the name is
therefore refused (E2015) rather than half-shadowing it: `Comparable.compare` would still return the
builtin's tags while a `match` on the result offered the user's cases.
```maxon
enum Ordering
	alpha
	beta
end 'Ordering'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:6: Unsupported: a declaration of the type name 'Ordering', which the compiler owns — shv2 synthesizes that declaration rather than reading it from the stdlib, and has no namespace to tell a user declaration of the name apart from the builtin one
```

<!-- test: comparable.ranged-alias-over-owned-name-is-legal -->
<!-- targets: x64-windows, x64-linux -->
The other side of that line, so the reject cannot creep: a RANGED typealias over a compiler-owned name is
ACCEPTED. It mints no identity — it erases to `int`, the same thing `HashValue` resolves to — so the
`Hashable` witness keeps working through it and `b.itemHash()` still reads `97`.
```maxon
typealias HashValue = int(0 to u32.max)
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

<!-- test: comparable.error.declare-hashvalue-type -->
The same rule closes a MEASURED fault on the sibling compiler-owned name: a user `type HashValue` made
`Hashable.hash()`'s witness result resolve as a struct box (the parser reads the struct registry; type
resolution claims `HashValue` before it), which was then dropped as one — an access violation. One
reject, stated once, for every compiler-owned type name.
```maxon
typealias Small = int(0 to 100)

type HashValue
	export var v as Small
	export static function create(v Small) returns Self
		return Self{ v: v }
	end 'create'
end 'HashValue'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:6: Unsupported: a declaration of the type name 'HashValue', which the compiler owns — shv2 synthesizes that declaration rather than reading it from the stdlib, and has no namespace to tell a user declaration of the name apart from the builtin one
```

<!-- test: comparable.error.cross-type-param-ordering -->
`Self` is the RECEIVER'S OWN type parameter. With two constrained parameters, `self.a < self.b`
dispatches `A`'s witness table — `Point.compare` — and would hand it a `B`, so the callee would
dereference the integer `9` as its `other Point`. Rejected at compile time as the same E3005 a concrete
literal gets, naming the parameter the offending operand actually has. Target-independent.
```maxon
typealias Coord = int(0 to 1000)
typealias Integer = int(0 to u32.max)

type Point implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function compare(other Point) returns Ordering
		if self.x < other.x 'lt'
			return Ordering.lessThan
		end 'lt'
		return Ordering.greaterThan
	end 'compare'
end 'Point'

type Mix uses A, B where A is Comparable, B is Comparable
	export var a as A
	export var b as B
	export static function create(a A, b B) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function cross() returns bool
		return self.a < self.b
	end 'cross'
end 'Mix'

typealias PointIntMix = Mix with (Point, Integer)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:17: '<' on type parameter 'A' requires an argument of type 'A' (the `Self` its constraint provides), not a value of type parameter 'B'
```

<!-- test: comparable.error.cross-type-param-compare-method -->
The method-call twin, at the SAME shared witness-dispatch argument check: `self.a.compare(self.b)` over
two different type parameters is E3005. Reversed here (`A` is the int, `B` the struct) to show the
direction that does not fault but silently compares an int against a heap pointer — equally refused.
Target-independent.
```maxon
typealias Coord = int(0 to 1000)
typealias Integer = int(0 to u32.max)

type Point implements Comparable
	export var x as Coord
	export static function create(x Coord) returns Self
		return Self{ x: x }
	end 'create'
	export function compare(other Point) returns Ordering
		if self.x < other.x 'lt'
			return Ordering.lessThan
		end 'lt'
		return Ordering.greaterThan
	end 'compare'
end 'Point'

type Mix uses A, B where A is Comparable, B is Comparable
	export var a as A
	export var b as B
	export static function create(a A, b B) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function crossMethod() returns Ordering
		return self.a.compare(self.b)
	end 'crossMethod'
end 'Mix'

typealias IntPointMix = Mix with (Integer, Point)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:17: 'compare' on type parameter 'A' requires an argument of type 'A' (the `Self` its constraint provides), not a value of type parameter 'B'
```

<!-- test: comparable.error.non-protocol-compare-result -->
A constraint that declares a method NAMED `compare` is not `Comparable` unless it returns `Ordering`.
The witness search matches by method name (shared with ordinary `.method()` dispatch), so without this
check `Weird`'s `Integer` result would be read as an `Ordering` tag and `a < b` would mean
`a.compare(b) == 0` — "less than" whenever the author's `compare` said EQUAL. It reports the same E3005
as no constraint at all, because the cure is the same sentence. Target-independent.
```maxon
typealias Integer = int(0 to u32.max)

interface Weird
	function compare(other Self) returns Integer
end 'Weird'

type Thing implements Weird
	export var v as Integer
	export static function create(v Integer) returns Self
		return Self{ v: v }
	end 'create'
	export function compare(other Thing) returns Integer
		return 0
	end 'compare'
end 'Thing'

type Pair uses T where T is Weird
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function lt() returns bool
		return self.a < self.b
	end 'lt'
end 'Pair'

typealias ThingPair = Pair with Thing

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:17: Operator '<' requires type parameter 'T' to be constrained with 'where T is Comparable'
```
