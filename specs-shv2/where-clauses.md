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

A witness dispatch's ARGUMENTS obey the same `name:` label grammar and the same label→position slotting as
any other call (see `parameter-labels.md`), with one documented exemption: because the receiver is an
interface / type-parameter value rather than a concrete struct, the FIRST argument's label is optional
(`self.item.add(1, b: 2)` and `self.item.add(a: 1, b: 2)` are both legal), while arguments 2 and later must
still carry one. The labels bind against the INTERFACE's declared parameter names, not the conformer's:
under dictionary-passing the shared body has no concrete callee to ask, and conformance compares arity and
types only — several conformers may spell their parameters differently, so the interface's names are the
only authoritative vocabulary.

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
		return self.item.combine(1, k: 3, b: 5)
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

<!-- test: where-clauses.witness-multi-arg-labelled -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The headline unlock: a witness dispatch of a TWO-parameter interface method, spelled the way every other
Maxon call is spelled — first argument positional, second labelled. Before this rung the dispatch parsed its
argument list as a bare comma loop with no label grammar at all, so `b: 2` was read as an expression and
reported `E2004: Undefined variable 'b'`. `7 + 1 + 2` reads 10.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1, b: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.witness-multi-arg-out-of-order -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Labels REORDER the arguments against the interface's declaration, exactly as they do at a direct call:
`combine(1, b: 5, k: 3)` must bind `k = 3` and `b = 5` even though `b` appears first in source.
`1*100 + 3*10 + 5` reads 135, and every wrong slotting reads something else (source order would give 153).
⚠ This is the case the C# bootstrap gets WRONG on its own interface path — it strips the labels and binds
by source order — so there is no oracle to differential against here; shv2 is deliberately correct.
```maxon
typealias Code = int(0 to u32.max)

interface Mixer
	function combine(a Code, k Code, b Code) returns Code
end 'Mixer'

type Point implements Mixer
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function combine(a Code, k Code, b Code) returns Code
		return self.x + a * 100 + k * 10 + b
	end 'combine'
end 'Point'

type Box uses T where T is Mixer
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.combine(1, b: 5, k: 3)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let bx = PointBox.create(Point.create(0))
	if bx.go() == 135 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.witness-multi-arg-first-arg-labelled -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A LABELLED FIRST ARGUMENT is accepted at an interface / type-parameter receiver — the one documented
exemption from the rule `parameter-labels.md` states, because the receiver's concrete type is unknown in the
shared body and the label binds against the interface. A concrete-struct receiver still rejects it with
E2052 (`parameter-labels.md`'s `error-method-first-arg-named`), and that rule is unchanged. Same call, same
answer as `witness-multi-arg-labelled`: 10.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(a: 1, b: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: where-clauses.witness-multi-arg-ranged-return -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Regression pin for the garbage-read this rung closed, in the shape that made it LOUD rather than silent.
With a NARROW ranged return (`int(0 to 1000)`) the only spelling the old parser accepted for a two-parameter
method was the under-supplied `self.item.add(1)` — whose missing second actual left an uninitialised
register, so `7 + 1 + <garbage>` left the alias's range and the correct program died in the impl with
`panic: Range check failed`. The labelled call is exact by construction: `7 + 1 + 2` reads 10, in range.
```maxon
typealias Small = int(0 to 1000)

interface Adder
	function add(a Small, b Small) returns Small
end 'Adder'

type Point implements Adder
	export var x as Small
	export static function create(x Small) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Small, b Small) returns Small
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Small
		return self.item.add(1, b: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
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

<!-- test: where-clauses.error.witness-arg-missing-label -->
The label grammar is the SAME rule at a witness dispatch as anywhere else: arguments 2 and later must carry
a `name:` label. `parseWitnessMethodOnValue` used to parse its arguments with a bare comma loop that consulted
no label rule at all, so this call was silently accepted. E2053 is raised by `consumeArgLabel` — the one copy
of the syntactic rule — and anchored on the offending argument, exactly as a direct call's is.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1, 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2053: <fragment>:24:27: the second and later arguments must be named ('name: value')
```

<!-- test: where-clauses.error.witness-too-few-args -->
An UNDER-SUPPLIED witness dispatch is E3036, from the same `slotCallArgs` a direct call is checked by.
Before this rung the dispatch performed no arity check at all: the missing actual left an uninitialised
register in the argument slot and the call returned it, so the program compiled clean and exited on garbage.
The arity error anchors on the call itself (the method name), matching `SemanticCheck.validateCall`.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3036: <fragment>:24:20: 'Adder.add' expects 2 argument(s) but 1 were provided
```

<!-- test: where-clauses.error.witness-too-many-args -->
The other direction of the same check: one declared formal, two actuals. Arity is tested BEFORE any label is
resolved (`slotCallArgs`'s first act), so the surplus argument is reported as an arity error and not as an
unknown label — the same order a direct call is checked in.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code) returns Code
		return self.x + a
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1, b: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3036: <fragment>:24:20: 'Adder.add' expects 1 argument(s) but 2 were provided
```

<!-- test: where-clauses.error.witness-unknown-label -->
A label naming no parameter of the INTERFACE method is E3037. The vocabulary is the interface's, not the
conformer's — under dictionary-passing the shared body has no concrete callee to ask, and conformance
compares arity and types only, so several conformers may spell their parameters differently.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1, zzz: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3037: <fragment>:24:27: 'Adder.add' has no parameter named 'zzz'
```

<!-- test: where-clauses.error.witness-duplicate-label -->
Two actuals targeting ONE formal is E3038: the positional first argument fills parameter `a`, and the label
`a:` names the position it already holds. Arity matches, so only the slotting can see it.
```maxon
typealias Code = int(0 to u32.max)

interface Adder
	function add(a Code, b Code) returns Code
end 'Adder'

type Point implements Adder
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function add(a Code, b Code) returns Code
		return self.x + a + b
	end 'add'
end 'Point'

type Box uses T where T is Adder
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.add(1, a: 2)
	end 'go'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(7))
	if b.go() == 10 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3038: <fragment>:24:27: duplicate argument for parameter 'a' of 'Adder.add'
```

<!-- test: where-clauses.error.operator-witness-wrong-arity -->
A constraint whose method has the protocol's NAME and the protocol's RESULT but the wrong ARITY is not the
protocol, and `==` may not dispatch it. An operator has exactly one operand to give, so the formal count is
as much a part of "is this `Equatable`?" as the result type and the (absent) `throws` clause already were —
the third hole in the same wall, and the only one left open once the `.method()` form began checking its
arity against the interface. ⚠ **MEASURED before the check existed: this program compiled CLEAN and the
impl read its unsupplied second `Self` formal out of an uninitialised argument register, dereferencing it**
(exit 7 from `b.x == 3` on a formal that was never passed) — a silent wrong answer one page fault from a
crash. It reports the same E3005 as no constraint at all, because the author's cure is the same sentence.
Target-independent.
```maxon
typealias Integer = int(0 to u32.max)

interface Weird
	function equals(a Self, b Self) returns bool
end 'Weird'

type Thing implements Weird
	export var v as Integer
	export static function create(v Integer) returns Self
		return Self{ v: v }
	end 'create'
	export function equals(a Thing, b Thing) returns bool
		return a.v == b.v
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
