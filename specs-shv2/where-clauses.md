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

<!-- test: where-clauses.witness-exitcode-return-high -->
<!-- targets: x64-windows -->
⚠ **A WINDOWS-LANE READING SINCE BATCH27, WHICH IS A REAL LOSS ON THE ONE LANE THIS CASE WAS ABOUT.**
`return 4000000000` is E3005 on every other target — `ExitCode` is `int(0 to 255)` there — so those lanes
cannot express this program, which is what the `targets:` restriction says. It cannot be re-pinned on
wasm through any other type, and the reason it cannot (plus the array-element route that looks like a
substitute and measurably is not) is stated once, in `exit-code-range.md`'s *"What the narrowing costs
the other lanes"*.

⭐⭐ **AN `ExitCode` RETURNED THROUGH A WITNESS, ABOVE 2^31 (W1 review).** `ExitCode` is the only builtin
type NAME whose tag carries a sub-64 width — a **u32** (`valueTagToStdType`) — and an interface stores its
method return type as a rendered source STRING, so a witness dispatch RE-DERIVES that width from the name.
`where-clauses.witness-multi-arg-ranged-return` above already pins that a compiler-owned name survives the
round trip; what it cannot pin is the VALUE, because every case in this file returns a small number, and a
small number is the same under either extension rule.

On wasm the recovered u32 lives in an `i32` and must be widened back to the `i64` world every Maxon value
inhabits. MEASURED, before the fix: the host printed `4000000000` and wasm printed **-294967296** — the
widen was `i64.extend_i32_s`, reading a u32's top bit as a sign. Silent on every register target, where the
value never leaves its 64-bit GPR, which is why it needed a case that reads the number back rather than
returning it as a process status (the OS truncates an exit code, so `exitcode` alone could never have
caught this).
```maxon
typealias Num = int(0 to 10)

interface Coded
	function code() returns ExitCode
end 'Coded'

type Widget implements Coded
	export var n as Num
	export static function create(n Num) returns Self
		return Self{ n: n }
	end 'create'
	export function code() returns ExitCode
		return 4000000000
	end 'code'
end 'Widget'

type Box uses T where T is Coded
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function show() returns ExitCode
		let v = self.item.code()
		print("witness={v}\n")
		return 0
	end 'show'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	let b = WidgetBox.create(Widget.create(1))
	return b.show()
end 'main'
```
```exitcode
0
```
```stdout
witness=4000000000
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
error E3017: <fragment>:22:11: Type 'Plain' does not satisfy constraint 'Digest' required by type parameter 'T' of 'Box'
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

<!-- test: where-clauses.error.operator-witness-wrong-formal-type -->
A constraint whose method has the protocol's NAME, the protocol's RESULT and the protocol's ARITY but
declares its formal as something other than `Self` is not the protocol either — the FOURTH hole in the same
wall, and the twin of `operator-witness-wrong-arity` above. An operator's one operand is a value of `T`, so
what the formal is TYPED is as much a part of "is this `Equatable`?" as how many there are.
`requireGenuineSelfArgs` cannot cover it: it validates the actuals sitting at `Self` formals, and an
interface like this one declares none. ⚠ **MEASURED before the check existed: this program compiled CLEAN
and `self.a == self.b` handed the RECEIVER'S `T` POINTER to a formal the impl reads as an `int`, so
`equals` compared a pointer against `7` and answered `false` for two equal values** — a silent wrong
answer, on `main` as well as on the branch that added the arity half. The `float` twin is worse (the
witness float-widening `cvtsi2sd`s that pointer) and a `String` formal is an outright type confusion, an
unmanaged struct pointer read through a managed header. Same E3005, because the author's cure is the same
sentence. Target-independent.
```maxon
typealias Integer = int(0 to u32.max)

interface WeirdEq
	function equals(other Integer) returns bool
end 'WeirdEq'

type Thing implements WeirdEq
	export var v as Integer
	export static function create(v Integer) returns Self
		return Self{ v: v }
	end 'create'
	export function equals(other Integer) returns bool
		return other == 7
	end 'equals'
end 'Thing'

type Pair uses T where T is WeirdEq
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

<!-- test: where-clauses.error.operator-witness-wrong-formal-type-comparable -->
The `Comparable` half of `operator-witness-wrong-formal-type`, so the check is pinned for BOTH protocols
rather than only the one it was found through — `<`/`>`/`<=`/`>=` read the same formals off the same
synthesized interface, and a `compare` taking anything but `Self` is no more `Comparable` than a
non-`Self` `equals` is `Equatable`. Target-independent.
```maxon
typealias Integer = int(0 to u32.max)

interface WeirdOrd
	function compare(other Integer) returns Ordering
end 'WeirdOrd'

type Thing implements WeirdOrd
	export var v as Integer
	export static function create(v Integer) returns Self
		return Self{ v: v }
	end 'create'
	export function compare(other Integer) returns Ordering
		return Ordering.lessThan
	end 'compare'
end 'Thing'

type Pair uses T where T is WeirdOrd
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

<!-- disabled-test: witness-mutation-of-let-argument-refused -->
<!-- P1.7a-witness-mutation: E3019 is never ASKED at a witness dispatch. `parseWitnessMethodOnValue`
     fills `argImmutableNames` and discards it, because the mutation summary is keyed by CALLEE and a
     witness dispatch has no single callee to ask — under dictionary-passing any conformer could be the
     impl. Answering it needs a design ruling (mutability declared on the interface method, or the union
     over every registered conformer), so it is a rung of its own, not a fix smuggled into 2b-vi.
     MEASURED on `main` @153d04620 AND on this branch, identically: the program below compiles with NO
     diagnostic and exits 9 — the `let` array really is grown, and the caller sees the grown array with
     its count and elements intact. So this is an OVER-ACCEPTANCE, not a miscompile and not memory-
     unsafe; that is why it could be deferred at all. The blame position below is the one the transitive
     rule produces for the concrete case (`specs-shv2/parameter-mutation.md` transitive-let-array-error)
     and is PROJECTED, not observed — no compiler emits it yet. Re-derive it when the rung lands. -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Grower
	function grow(dest IntArray)
end 'Grower'

type Pusher implements Grower
	export var n as Integer
	export static function create(n Integer) returns Self
		return Self{ n: n }
	end 'create'
	export function grow(dest IntArray)
		dest.push(9)
	end 'grow'
end 'Pusher'

type Box uses T where T is Grower
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function run(dest IntArray)
		self.item.grow(dest)
	end 'run'
end 'Box'

typealias PusherBox = Box with Pusher

function main() returns ExitCode
	let a = IntArray.create()
	let b = PusherBox.create(Pusher.create(1))
	b.run(a)
	return try a.get(0) otherwise 55
end 'main'
```
```maxoncstderr
error E3019: <fragment>:33:2: cannot pass 'a' to function that mutates parameter 'dest' (in main)
```

<!-- test: where-clauses.constraint-interface-declared-below -->
A `where` constraint's interface is resolved WHOLE-PROGRAM (R8), so writing the `interface` BELOW the
generic type it constrains is legal — the identical program with the interface written above compiles
and returns the identical answer. Before R8 this was `E2015 … not declared before its constrained use`:
the resolution walked THIS FILE's interfaces as the linear parse had recorded them so far.
```maxon
typealias Code = int(0 to u32.max)
typealias Coord = int(0 to 1000)

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

interface Digest
	function digest() returns Code
end 'Digest'

function main() returns ExitCode
	let p = Point.create(3, y: 4)
	let b = PointBox.create(p)
	if b.itemDigest() == 97 'ok'
		return 42
	end 'ok'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: where-clauses.constraint-interface-in-a-later-file -->
The same fact across FILES, which is the commoner shape: the constrained type is in the
earlier-sorting file and the `interface` it names is in the later one, so no declaration order within
a file can rescue it. It resolves because the interface index is built from EVERY file's tokens
before ANY file is parsed.
```maxon
// --- file: a.maxon
typealias Code = int(0 to u32.max)
typealias Coord = int(0 to 1000)

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
		return 42
	end 'ok'
	return 1
end 'main'

// --- file: z.maxon
export interface Digest
	function digest() returns Code
end 'Digest'
```
```exitcode
42
```

⚠⚠ **THE PLACEMENT GUARD (R8 review), AND IT PINS A LIMITATION ON PURPOSE.** This program SHOULD compile and
return 42; it does not, and the E3011 below is a PRE-EXISTING false reject that R8 neither introduced nor
widens (`Parser.interfaceReturnMaxonType` has no generic-instance arm — see the note under the code). It is
pinned as an ENABLED error case anyway, because **the diagnostic is the only thing in the whole suite that
can see WHERE `Queries.foldInterfaceDeclarations` runs**, and that placement is this rung's entire
correctness thesis.

MEASURED THREE WAYS on this one program, each leg a separate build of `maxon-shv2` in this worktree:

| compiler | result |
|---|---|
| pre-R8 (`04b7330c5^`), reading the interface out of the REAL PARSE's `artifact.interfaces` | `E3011 … Unknown type 'Array_Integer'` |
| R8 as shipped — the read after `allFilesFolded`, before `deriveInstanceNames` | `E3011 … Unknown type 'Array_Integer'`, byte-identical |
| R8 with the read moved INTO the per-file sweep | **COMPILES, exit 42** |

The third row is the trap, and it is why this case exists: the sabotage looks like an improvement and is
not. An incomplete index makes `signatures.isGenericAlias` answer "no", so the requirement's return type
renders as the RAW ALIAS `IntArray` — which happens to resolve — while the real parse renders the same type
as the canonical `Array_Integer`. Two spellings of one type inside one index is the false-ACCEPT shape the
R5 review measured, and the accept it buys here is an accident, not a fix.

⚠ **AND THE REST OF THE SUITE CANNOT SEE THAT — MEASURED.** With the recording moved into the per-file
sweep the full suite ran **2837 passed / 0 failed, exit 0, no leak**, `constraint-interface-generic-alias-formal`
included: a FORMAL's rendered string is only ever COMPARED (against `Self`, against `float`), never
RESOLVED, so no formal-position case can go red. Only the RETURN position resolves the string, so this is
the one shape where the two renderings are observable at all. Without this case the placement is guarded by
nothing.

⇒ **THE DAY `interfaceReturnMaxonType` GROWS ITS GENERIC-INSTANCE ARM, THIS CASE FLIPS TO `exitcode 42` —
IT DOES NOT GET DELETED.** It has two jobs and only the first one retires.

<!-- test: where-clauses.error.witness-return-generic-instance -->
<!-- interface-return-generic-instance: WHAT THE E3011 ACTUALLY IS, and what unblocks it. An interface
     requirement's types are stored as rendered source strings and `renderDeclaredTypeName` spells a generic
     instance as its CANONICAL INSTANCE NAME, but `Parser.interfaceReturnMaxonType` — which turns that string
     back into a type at the dispatch — has no generic-instance arm: its `named` fallback interns
     `Array_Integer` and the resolver has never heard of it.
     ⚠ NO ORACLE: the bootstrap refuses this program in every declaration order with `E4006 Primitive type
     'int' has no method named 'make'`, because it has no parse-time witness dispatch at all (it
     monomorphizes). Unblocking it needs a canonical-instance-NAME -> `GenericInstanceId` door that
     `ProgramSignatures` does not have, plus a ruling on how a witness dispatch adopts a MANAGED
     generic-instance result — its own rung, not a line here. -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Maker
	function make() returns IntArray
end 'Maker'

type Builder implements Maker
	export var seed as Integer
	export static function create(seed Integer) returns Self
		return Self{ seed: seed }
	end 'create'
	export function make() returns IntArray
		var xs = IntArray.create()
		xs.push(self.seed)
		return xs
	end 'make'
end 'Builder'

type Box uses T where T is Maker
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function first() returns Integer
		let xs = self.item.make()
		return try xs.get(0) otherwise 0
	end 'first'
end 'Box'

typealias BuilderBox = Box with Builder

function main() returns ExitCode
	return BuilderBox.create(Builder.create(42)).first()
end 'main'
```
```maxoncstderr
error E3011: specs/fragments/where-clauses/where-clauses.error.witness-return-generic-instance.test:28:17: Unknown type 'Array_Integer'
```

<!-- test: where-clauses.constraint-interface-generic-alias-formal -->
⚠ THE RENDERING GUARD (R8). An interface requirement whose parameter type is a GENERIC-ALIAS instance
(`IntArray = Array with Integer`) is the one shape where the two renderings of a declared type can
disagree: `renderDeclaredTypeName` spells a `genericInstance` as its CANONICAL instance name, and a
reader asked before the whole-program alias table is complete would see a plain `named` type and spell
the raw alias instead. Two spellings of one type are a false ACCEPT on the conformance and a wrong
`paramTypeNames` at the dispatch, so the recording pass runs only once every file has folded — and
this case is what pins it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Summer
	function total(xs IntArray) returns Integer
end 'Summer'

type Adder implements Summer
	export var base as Integer
	export static function create(base Integer) returns Self
		return Self{ base: base }
	end 'create'
	export function total(xs IntArray) returns Integer
		var sum = self.base
		for x in xs 'each'
			sum = sum + x
		end 'each'
		return sum
	end 'total'
end 'Adder'

type Box uses T where T is Summer
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function run(xs IntArray) returns Integer
		return self.item.total(xs)
	end 'run'
end 'Box'

typealias AdderBox = Box with Adder

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(10)
	xs.push(30)
	let b = AdderBox.create(Adder.create(2))
	return b.run(xs)
end 'main'
```
```exitcode
42
```

⚠ **WHAT R8 PAID FOR DELETING R7's SEPARATE ARITY STORE, PINNED (R8 review).** R7 read an interface's
HEADER during the token sweep, so a broken BODY still recorded the name and its `uses` arity. R8 reads the
WHOLE declaration through `Parser.readInterfaceDeclaration` — the one builder, which is the point — and that
read is TOLERANT: an `interface` whose body will not parse records NOTHING, so its name resolves to nothing
at every parse-time door.

The consequence is a DIAGNOSTIC one and only that. Every program that lands here is a program the real
parse refuses on its own line, so no wrong program is ever accepted — but the reject below is raised at the
DISPATCH, in a file that parses before the broken `interface` is reached, and the file's parse stops there.
MEASURED: it is the ONLY diagnostic this program produces, which is why the sentence may not claim the
interface is undeclared (it is declared, eight lines down). It names both causes instead.

The same shape with the broken `interface` in a LATER FILE reports both errors — the dispatch's reject from
`a.maxon` and the real `E2010` from the later file — because each file's parse is independent. It is the
same-file ordering that hides the cause, so that is the one pinned.

<!-- test: where-clauses.error.constraint-interface-unreadable -->
```maxon
typealias Code = int(0 to u32.max)

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function go() returns Code
		return self.item.digest()
	end 'go'
end 'Box'

typealias PointBox = Box with Point

interface Digest
	function digest() returns Code
	function bogus(,) returns Code
end 'Digest'

function main() returns ExitCode
	return PointBox.create(Point.create(42)).go()
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/where-clauses/where-clauses.error.constraint-interface-unreadable.test:20:20: Unsupported: the `where` constraint interface 'Digest' does not resolve — no file in this program declares a readable `interface` of that name, so it is either misspelled or an `interface` whose own declaration fails to parse
```

### An `extends`-inherited requirement is dispatchable through the constraint that inherits it

A `where` constraint names ONE interface, but the requirements it supplies are that interface's TRANSITIVE
set — its own and its `extends` parents'. `interfaceWitnessSlots` computes that list once, and both the
witness-table BUILDER and the dispatch RESOLVER read it, so the slot a call compiles is the slot the blob
fills. ⚠ **Before R10c only the CONFORMANCE CHECK walked the chain**: a conformer was required to supply
`Base.label()` and there was no slot in any table to put it in, so calling it was refused outright with
`E2015 … no `where` constraint on this type parameter declares a method 'label'` — a refusal of a legal
program, on a constraint that does declare it.

⚠ **THE TWO RESULTS ARE COMBINED POSITIONALLY (`* 10 +`) AND NOT SUMMED, WHICH IS THE WHOLE ASSERTION.**
`7 + 1` is `8` whichever slot each call reads, so a summed expectation would be satisfied by a compiler that
resolved both requirements and numbered their slots the wrong way round — the silent wrong-function-pointer
this rung exists to make unrepresentable, passing green. `71` is reached only by the correct pairing; the
swap reads `17`. Target-independent.

<!-- test: where-clauses.inherited-requirement-dispatch -->
```maxon
typealias Code = int(0 to u32.max)

interface Base
	function label() returns Code
end 'Base'

interface Derived extends Base
	function extra() returns Code
end 'Derived'

type Widget implements Derived
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function label() returns Code
		return self.seed
	end 'label'

	export function extra() returns Code
		return 1
	end 'extra'
end 'Widget'

type Box uses T where T is Derived
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function callInherited() returns Code
		return self.item.label()
	end 'callInherited'

	export function callOwn() returns Code
		return self.item.extra()
	end 'callOwn'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	let w = Widget.create(7)
	let b = WidgetBox.create(w)
	return (b.callInherited() * 10 + b.callOwn()) as ExitCode
end 'main'
```
```exitcode
71
```

### A child interface OVERLOADING an inherited name: two requirements, two slots, two impls

`Base.label()` and `Derived.label(width)` are DISTINCT requirements and take DISTINCT witness slots, so the
argument COUNT is what tells a dispatch which one it means. ⚠ **A resolver that matched by NAME alone bound
both calls to whichever requirement it reached first and then reported the OTHER one's arity against the
call: `E3036 'Derived.label' expects 1 argument(s) but 0 were provided`** — a sentence that is false about an
interface inheriting a zero-argument `label()`. The two calls below must reach DIFFERENT impls, which is what
`7` and `21` prove: a single slot serving both would return one of them twice. Target-independent.

<!-- test: where-clauses.inherited-overload-dispatch -->
```maxon
typealias Code = int(0 to u32.max)

interface Base
	function label() returns Code
end 'Base'

interface Derived extends Base
	function label(width Code) returns Code
end 'Derived'

type Widget implements Derived
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function label() returns Code
		return self.seed
	end 'label'

	export function label(width Code) returns Code
		return self.seed * width
	end 'label'
end 'Widget'

type Box uses T where T is Derived
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function zeroArg() returns Code
		return self.item.label()
	end 'zeroArg'

	export function oneArg() returns Code
		return self.item.label(3)
	end 'oneArg'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	let b = WidgetBox.create(Widget.create(7))
	return (b.zeroArg() + b.oneArg()) as ExitCode
end 'main'
```
```exitcode
28
```

### A DIAMOND gives the shared ancestor ONE slot, not two

`Both extends Left, Right` where `Left` and `Right` each extend `Root` reaches `Root` twice. The walk's
`visited` set — keyed by interface NAME, the same key the registry is — makes the second arrival contribute
nothing, so `Root.base()` occupies exactly one slot and the table has four rather than five. Two slots for
one requirement would not be a wasted word: every slot after the duplicate shifts, and the resolver and the
builder would have to agree on the same duplication to stay in step.

⚠ **THE FOUR RESULTS ARE A BASE-5 NUMERAL, NOT A SUM, AND THAT IS THE ASSERTION.** Any sum of the four is
invariant under all 24 permutations of the slots, so it pins only that every requirement RESOLVES — never
which slot each call reads, which is the failure this rung exists to prevent. As digits `1 2 3 4` of a
positional numeral the answer is `194` for the correct numbering and a different value for every one of the
other 23. Target-independent.

<!-- test: where-clauses.diamond-inherited-requirement -->
```maxon
typealias Code = int(0 to u32.max)

let SlotRadix = 5

interface Root
	function base() returns Code
end 'Root'

interface Left extends Root
	function left() returns Code
end 'Left'

interface Right extends Root
	function right() returns Code
end 'Right'

interface Both extends Left, Right
	function both() returns Code
end 'Both'

type Widget implements Both
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function base() returns Code
		return 1
	end 'base'

	export function left() returns Code
		return 2
	end 'left'

	export function right() returns Code
		return 3
	end 'right'

	export function both() returns Code
		return 4
	end 'both'
end 'Widget'

type Box uses T where T is Both
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function digits() returns Code
		return ((self.item.base() * SlotRadix + self.item.left()) * SlotRadix + self.item.right()) * SlotRadix + self.item.both()
	end 'digits'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	let b = WidgetBox.create(Widget.create(0))
	return b.digits()
end 'main'
```
```exitcode
194
```

### The diamond split across TWO CONSTRAINTS: one requirement reached twice is still one requirement

The case above closes the diamond inside ONE interface, where `interfaceWitnessSlots`' `visited` set sees
both arrivals. `where T is Left and Right` splits the same diamond across two CONSTRAINTS, and no `visited`
set spans them — the name search walks each constraint's own slot list and collects `Root.base()` once from
each. ⚠ **Read as two claimants that was E3114 on a legal program, and the message named the requirement as
its own rival: `'base' … is provided by both Root.base() returns Code and Root.base() returns Code`.** The
two entries are two ROUTES to one requirement: `ConformanceCheck` files the accepted member under
(conformer, DECLARING interface, method name), and both `__witness_Widget.Left` and `__witness_Widget.Right`
stamp their `base` slot from that one filing — so either route binds the same function pointer, and the
second entry is dropped rather than counted (`witnessCandidateAlreadyCollected`).

⚠ **THE THREE RESULTS ARE POSITIONAL, NOT SUMMED.** `1 + 2 + 3` is `6` under every permutation, so a sum
would pass on a compiler that merely stopped refusing — including one that bound `base` to `Left`'s slot 0
and returned `left`'s answer for it. `123` is reached only when the inherited requirement AND both
constraints' own requirements each read their own slot. Target-independent.

<!-- test: where-clauses.shared-ancestor-through-two-constraints -->
```maxon
typealias Code = int(0 to u32.max)

interface Root
	function base() returns Code
end 'Root'

interface Left extends Root
	function left() returns Code
end 'Left'

interface Right extends Root
	function right() returns Code
end 'Right'

type Widget implements Left, Right
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function base() returns Code
		return 1
	end 'base'

	export function left() returns Code
		return 2
	end 'left'

	export function right() returns Code
		return 3
	end 'right'
end 'Widget'

type Box uses T where T is Left and Right
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.base() * 100 + self.item.left() * 10 + self.item.right()
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(0)).go()
end 'main'
```
```exitcode
123
```

### The OPERATOR path: two constraints that each `extends Equatable` supply ONE `equals`

The same shape on the operator path, which reaches it through a different filter — `witnessTargetIsProtocol`
keeps every candidate shaped like `Equatable.equals`, and both routes to the inherited requirement are.
⚠ **`Equatable.equals … and Equatable.equals` was the E3114 this produced**, one sentence naming one
requirement twice. It must not be confused with `where-clauses.error.ambiguous-operator-witness` below,
which is two DISTINCT declaring interfaces (`Equatable` and `AlsoEquatable`) and stays refused: that pair
files under two impl keys and can hold two different members. Target-independent.

<!-- test: where-clauses.shared-equatable-through-two-constraints -->
```maxon
typealias Code = int(0 to u32.max)

interface EqL extends Equatable
	function l() returns Code
end 'EqL'

interface EqR extends Equatable
	function r() returns Code
end 'EqR'

type Widget implements EqL, EqR
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function l() returns Code
		return 1
	end 'l'

	export function r() returns Code
		return 2
	end 'r'

	export function equals(other Self) returns bool
		return self.seed == other.seed
	end 'equals'
end 'Widget'

type Pair uses T where T is EqL and EqR
	export var a as T
	export var b as T

	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'

	export function go() returns Code
		return (1 if self.a == self.b else 0) * 100 + self.a.l() * 10 + self.b.r()
	end 'go'
end 'Pair'

typealias WidgetPair = Pair with Widget

function main() returns ExitCode
	return WidgetPair.create(Widget.create(3), b: Widget.create(3)).go()
end 'main'
```
```exitcode
112
```

### The NEGATIVE control: a name no interface in the chain declares is still refused

The transitive walk widens what a constraint provides; it must not make the refusal vacuous. `Derived`
inherits `label` from `Base` and declares `extra` itself, and neither is `missing` — so the call is refused,
and the sentence is TRUE of every interface in the chain. Without this the two cases above would be
satisfied by a resolver that had simply stopped refusing anything. Target-independent.

<!-- test: where-clauses.error.no-constraint-declares-method -->
```maxon
typealias Code = int(0 to u32.max)

interface Base
	function label() returns Code
end 'Base'

interface Derived extends Base
	function extra() returns Code
end 'Derived'

type Widget implements Derived
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function label() returns Code
		return self.seed
	end 'label'

	export function extra() returns Code
		return 1
	end 'extra'
end 'Widget'

type Box uses T where T is Derived
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.missing()
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(7)).go() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:36:20: Unsupported: no requirement named 'missing' is provided by the constraints on type parameter 'T' — a method call on a value whose type is an interface, or on a constrained type parameter, dispatches through a witness table, so the method has to be one that interface declares
```

### Two requirements of one name and one arity are AMBIGUOUS, not a first-wins pick

`Derived extends Base` where BOTH declare `label()` leaves two requirements the call could equally mean, in
two distinct slots. They resolve to the same member here, but the slots are still two and a dispatch
compiles exactly one offset — so there is nothing to choose by, and choosing the first is a silent wrong
function pointer the moment the two slots differ. E3114 names both claimants by their DECLARING interface,
which is the interface the author has to edit, and spells each through `assembleMethodSignature` — the same
`name(p t) returns r` the conformance diagnostics already print a requirement with. Target-independent.

<!-- test: where-clauses.error.ambiguous-inherited-requirement -->
```maxon
typealias Code = int(0 to u32.max)

interface Base
	function label() returns Code
end 'Base'

interface Derived extends Base
	function label() returns Code
end 'Derived'

type Widget implements Derived
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function label() returns Code
		return self.seed
	end 'label'
end 'Widget'

type Box uses T where T is Derived
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.label()
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(7)).go() as ExitCode
end 'main'
```
```maxoncstderr
error E3114: <fragment>:32:20: 'label' taking 0 argument(s) is provided by both Derived.label() returns Code and Base.label() returns Code through the constraints on type parameter 'T' — a witness dispatch binds ONE table slot, and these are two, so there is nothing to choose by. Rename one requirement, or drop one of the constraints
```

### Several requirements of one name and NONE of the call's arity

E3036 names ONE callee and ONE expected count, which cannot be written when several requirements of the name
exist and the call matches none: blaming an arbitrary one prints a true sentence about the wrong
requirement, which is exactly what the pre-R10c resolver did. E3115 lists them all instead. ⚠ With a SINGLE
requirement of the name there is nothing to select between and the arity is still E3036's to report — that
is what `where-clauses.error.witness-too-few-args` above pins, and it is unmoved. Target-independent.

<!-- test: where-clauses.error.no-witness-requirement-of-that-arity -->
```maxon
typealias Code = int(0 to u32.max)

interface Base
	function label() returns Code
end 'Base'

interface Derived extends Base
	function label(a Code, b Code) returns Code
end 'Derived'

type Widget implements Derived
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function label() returns Code
		return self.seed
	end 'label'

	export function label(a Code, b Code) returns Code
		return a + b
	end 'label'
end 'Widget'

type Box uses T where T is Derived
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.label(1)
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(7)).go() as ExitCode
end 'main'
```
```maxoncstderr
error E3115: <fragment>:36:20: no requirement named 'label' provided by the constraints on type parameter 'T' takes 1 argument(s); they provide Derived.label(a Code, b Code) returns Code, Base.label() returns Code
```

### The OPERATOR path refuses an ambiguity too, and for the same reason

`==` searches the constraints for `equals` and then requires the hit to BE `Equatable` — result, `throws`
and formals. That test is now a FILTER over every candidate rather than a verdict on the first name match,
which is what lets a look-alike constraint stop MASKING a real one. Two constraints that are both genuinely
protocol-shaped are the other half of the same change: they are two distinct witness slots holding two
distinct conformers' impls, so "both are `Equatable`" does not make them interchangeable, and the operator
would silently take whichever was written first. Same E3114 as the `.method()` form. Target-independent.

<!-- test: where-clauses.error.ambiguous-operator-witness -->
```maxon
typealias Code = int(0 to u32.max)

interface AlsoEquatable
	function equals(other Self) returns bool
end 'AlsoEquatable'

type Widget implements Equatable, AlsoEquatable
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export function equals(other Self) returns bool
		return self.seed == other.seed
	end 'equals'
end 'Widget'

type Pair uses T where T is Equatable and AlsoEquatable
	export var a as T
	export var b as T

	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'

	export function same() returns bool
		return self.a == self.b
	end 'same'
end 'Pair'

typealias WidgetPair = Pair with Widget

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3114: <fragment>:29:17: 'equals' taking 1 argument(s) is provided by both Equatable.equals(other Self) returns bool and AlsoEquatable.equals(other Self) returns bool through the constraints on type parameter 'T' — a witness dispatch binds ONE table slot, and these are two, so there is nothing to choose by. Rename one requirement, or drop one of the constraints
```

### A `static` requirement is NOT reachable through a type parameter (A2w)

A `static` member has no receiver, so a value of `T` cannot dispatch one — and until this code existed the
resolver matched a requirement by NAME ALONE and bound the static's slot anyway, prepending a receiver the
callee has no parameter for. **MEASURED on the program below: x64-windows compiled clean and returned 7 (the
right answer, by ABI luck — the spurious receiver landed in an argument register the zero-parameter callee
never read), while `--target=wasm32-wasi` compiled clean and then trapped `indirect call type mismatch` at
runtime.** `call_indirect` checks the declared functype against the target's own, so wasm caught what x64's
registers let slide: the slot's signature genuinely disagrees with the callee's.

Neither existing message could be reused. E3036's is about an argument COUNT (it is what the same call shape
gets on a CONCRETE receiver — `'Widget.origin' expects 0 argument(s) but 1 were provided` — counting the
receiver as an argument), and E2015's `no `where` constraint … declares a method 'origin'` would be FALSE:
the constraint declares it, as a static. Target-independent.

<!-- test: where-clauses.error.static-requirement-through-type-param -->
```maxon
typealias Code = int(0 to u32.max)

interface Origin
	static function origin() returns Code
	function digest() returns Code
end 'Origin'

type Widget implements Origin
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export static function origin() returns Code
		return 7
	end 'origin'

	export function digest() returns Code
		return self.seed
	end 'digest'
end 'Widget'

type Box uses T where T is Origin
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.origin()
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(7)).go() as ExitCode
end 'main'
```
```maxoncstderr
error E3116: <fragment>:33:20: 'Origin.origin' is a `static` requirement, and a `static` member has no receiver to dispatch on — so a value of type parameter 'T' cannot reach it. Call a `static` member on a concrete type
```

### The ESCAPE HATCH: a `static` member is reached on the CONCRETE type, and still is

The refusal above is only safe because the capability has a route, so the route is pinned beside it. `Widget`
is named outright, which is the one thing a `where` constraint does not tell you — and the same body still
dispatches an INSTANCE requirement through the witness, so the two forms are proved to coexist. Returns
`2 + 7`.

<!-- test: where-clauses.concrete-static-beside-witness-dispatch -->
```maxon
typealias Code = int(0 to u32.max)

interface Origin
	static function origin() returns Code
	function digest() returns Code
end 'Origin'

type Widget implements Origin
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export static function origin() returns Code
		return 7
	end 'origin'

	export function digest() returns Code
		return self.seed
	end 'digest'
end 'Widget'

type Box uses T where T is Origin
	export var item as T

	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'

	export function go() returns Code
		return self.item.digest() + Widget.origin()
	end 'go'
end 'Box'

typealias WidgetBox = Box with Widget

function main() returns ExitCode
	return WidgetBox.create(Widget.create(2)).go()
end 'main'
```
```exitcode
9
```

### The OPERATOR path had the SAME hole, one door over

`witnessTargetIsProtocol` decides whether a constraint's `equals` IS `Equatable` by comparing the result
type, the `throws` clause and the formals — and it did not compare the RECEIVER KIND, so a `static equals`
passed a shape test the protocol's own instance method defines. **MEASURED on the program below with the
comparison reached (`a.seed` 7, `b.seed` 1, so the answer is `false`): x64-windows compiled clean and
returned `true` — the static impl read the RECEIVER as its `other`, not the right operand, a SILENT WRONG
ANSWER — and wasm trapped `indirect call type mismatch`.** A static look-alike is no more `Equatable` than a
throwing one or a wrong-arity one, so it takes the same E3005 those already take: the author's cure is the
same sentence. Target-independent.

<!-- test: where-clauses.error.static-operator-witness -->
```maxon
typealias Code = int(0 to u32.max)

interface StaticEq
	static function equals(other Self) returns bool
end 'StaticEq'

type Widget implements StaticEq
	export var seed as Code

	export static function create(seed Code) returns Self
		return Self{ seed: seed }
	end 'create'

	export static function equals(other Self) returns bool
		return other.seed == 7
	end 'equals'
end 'Widget'

type Pair uses T where T is StaticEq
	export var a as T
	export var b as T

	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'

	export function same() returns bool
		return self.a == self.b
	end 'same'
end 'Pair'

typealias WidgetPair = Pair with Widget

function main() returns ExitCode
	return 1 if WidgetPair.create(Widget.create(7), b: Widget.create(1)).same() else 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:29:17: Operator '==' requires type parameter 'T' to be constrained with 'where T is Equatable'
```

### The refusal must be the message the author GETS — three shapes that used to answer something else

The refusal above first shipped as a RECORDED diagnostic that left the parse running and handed the rejected
`static` candidates back to the arity/ambiguity selection. It looked right on the one program that has a
single static requirement, a matching argument count and nothing else wrong in the file — and on nothing
else. **MEASURED, all three:** a wrong-arity call printed E3036's argument COUNT alone; two same-named
statics printed E3114's *"Rename one requirement, or drop one of the constraints"* alone; and an unrelated
later error anywhere in the file printed alone, because an aborted parse salvages the type names and drops
the recorded diagnostics. Each of those is the sentence E3116's own registry entry says must **not** be the
answer, and following any of them does not fix the program. The reject therefore THROWS, and these pin it.

The count is what tells the two apart from a real arity error: the requirement is unreachable whatever the
call supplies, so the receiver kind is reported and the argument count never comes up. Target-independent.

<!-- test: where-clauses.error.static-requirement-wrong-arity-at-call -->
```maxon
typealias Code = int(0 to u32.max)

interface Origin
	static function origin(seed Code) returns Code
end 'Origin'

type Box uses T where T is Origin
	export var item as T

	export function go() returns Code
		return self.item.origin()
	end 'go'
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3116: <fragment>:12:20: 'Origin.origin' is a `static` requirement, and a `static` member has no receiver to dispatch on — so a value of type parameter 'T' cannot reach it. Call a `static` member on a concrete type
```

### TWO `static` requirements of one name are the same fact twice, not a choice

Two constraints supplying one `static` name are not an AMBIGUITY — there is nothing to disambiguate,
because neither of them was ever reachable. Naming the first is the whole answer. Target-independent.

<!-- test: where-clauses.error.two-static-requirements-through-type-param -->
```maxon
typealias Code = int(0 to u32.max)

interface OriginA
	static function origin() returns Code
end 'OriginA'

interface OriginB
	static function origin() returns Code
end 'OriginB'

type Box uses T where T is OriginA, T is OriginB
	export var item as T

	export function go() returns Code
		return self.item.origin()
	end 'go'
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3116: <fragment>:16:20: 'OriginA.origin' is a `static` requirement, and a `static` member has no receiver to dispatch on — so a value of type parameter 'T' cannot reach it. Call a `static` member on a concrete type
```

### A same-named INSTANCE requirement still binds — the filter removes statics, it does not refuse the name

The escape hatch above pins a static and an instance requirement of DIFFERENT names coexisting. This pins
the harder half: one NAME, supplied `static` by one constraint and as an INSTANCE requirement by another, so
the filter has to keep the one the call could have meant instead of refusing the name outright. It is a
generic BODY rather than an instantiation because no conforming type is representable — a type may not
declare both `static origin` and `origin` (E3006) — which is exactly why the shape needs pinning here: the
only thing that decides it is the parse.

<!-- test: where-clauses.static-and-instance-requirement-of-one-name -->
```maxon
typealias Code = int(0 to u32.max)

interface StaticOrigin
	static function origin() returns Code
end 'StaticOrigin'

interface InstanceOrigin
	function origin() returns Code
end 'InstanceOrigin'

type Box uses T where T is StaticOrigin, T is InstanceOrigin
	export var item as T

	export function go() returns Code
		return self.item.origin()
	end 'go'
end 'Box'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

### Basic where clause with Map

Map requires `Key is Hashable`. String implements Hashable, so this should work:

<!-- test: where-clauses.map-basic -->

```maxon
function main() returns ExitCode
		let m = ["hello": 42]
		return try m.get("hello") otherwise 0
end 'main'
```
```exitcode
42
```

### Custom Hashable type as Map key

A user-defined type that implements Hashable can be used as a Map key:

<!-- test: where-clauses.custom-hashable-key -->

```maxon

typealias Integer = int(i64.min to i64.max)

type MyKey implements Hashable, Equatable
		var value as Integer

		function hash() returns HashValue
				return self.value * 31
		end 'hash'

		function equals(other MyKey) returns bool
				return self.value == other.value
		end 'equals'

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'MyKey'

typealias MyKeyMap = Map with (MyKey, Integer)

function main() returns ExitCode
		var m = MyKeyMap.create()
		try m.insert(MyKey.create(1), value: 42) otherwise ignore
		return m.count()
end 'main'
```
```exitcode
1
```

### Where clause constraint violation

Using a type that doesn't implement Hashable as a Map key should produce a compile error:

<!-- disabled-test: where-clauses.constraint-violation -->
<!-- P1.x-Map: `Map`'s ASSOCIATED TYPES. MEASURED: `error E2055: <fragment>:9:20: Type 'Map' has no
     associated types` — shv2 synthesizes no `Map`, so `Map with (NotHashable, Integer)` is refused
     before any constraint is checked and the expected E3017 never gets the chance to fire. R8 does not
     touch which types exist. -->

```maxon

typealias Integer = int(i64.min to i64.max)

type NotHashable
		var x as Integer
end 'NotHashable'

typealias BadMap = Map with (NotHashable, Integer)

function main() returns ExitCode
		return 0
end 'main'
```
```maxoncstderr
error E3017: specs/fragments/where-clauses/where-clauses.constraint-violation.test:9:11: Type 'NotHashable' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
```

### User-defined type with where clause

A user-defined generic type can use where clauses:

<!-- test: where-clauses.user-defined -->
<!-- ⚠ ENABLED BY A5o, BUT NOT FIXED BY IT — the disable was STALE. Its note recorded
     `a member access 'value' on a 'int' value` for `h.item.value()`, i.e. the missing per-instance
     field type at an instantiation site. A4i landed exactly that (`declaredSlotType` at
     `instanceSubstitutedType`) and nobody re-ran the case. MEASURED both ways at A5o: exit 10 on the
     PRE-A5o binary as well as the post one, so the credit belongs to the field-read retype, not to the
     call-result one. This is what a disabled case costs — it pins nothing while it waits. -->

```maxon

typealias Integer = int(i64.min to i64.max)

interface Valuable
		function value() returns Integer
end 'Valuable'

type Wrapper implements Valuable
		let n as Integer

		function value() returns Integer
				return self.n
		end 'value'

		static function create(n Integer) returns Self
			return Self{n: n}
		end 'create'
end 'Wrapper'

type Holder uses T where T is Valuable
		export var item as T

		static function create(item T) returns Self
			return Self{item: item}
		end 'create'
end 'Holder'

typealias WrapperHolder = Holder with Wrapper

function main() returns ExitCode
		let w = Wrapper.create(10)
		let h = WrapperHolder.create(w)
		return h.item.value()
end 'main'
```
```exitcode
10
```

### Where clause with multiple interfaces using and

A type parameter can require multiple interface conformance:

<!-- test: where-clauses.multiple-interfaces -->
<!-- ⚠ The same stale disable as `where-clauses.user-defined`, one line further out, and re-measured
     with it: exit 30 on the PRE-A5o binary too. -->

```maxon

typealias Integer = int(i64.min to i64.max)

interface HasName
		function name() returns Integer
end 'HasName'

interface HasAge
		function age() returns Integer
end 'HasAge'

type Person implements HasName, HasAge
		let age as Integer

		function name() returns Integer
				return 1
		end 'name'

		function age() returns Integer
				return self.age
		end 'age'

		static function create(age Integer) returns Self
			return Self{age: age}
		end 'create'
end 'Person'

type Registry uses T where T is HasName and HasAge
		export var item as T

		static function create(item T) returns Self
			return Self{item: item}
		end 'create'
end 'Registry'

typealias PersonRegistry = Registry with Person

function main() returns ExitCode
		let p = Person.create(30)
		let r = PersonRegistry.create(p)
		return r.item.age()
end 'main'
```
```exitcode
30
```

### Where clause violation with and - missing one interface

<!-- test: where-clauses.and-violation -->

```maxon

typealias Integer = int(i64.min to i64.max)

interface Foo
		function foo() returns Integer
end 'Foo'

interface Bar
		function bar() returns Integer
end 'Bar'

type OnlyFoo implements Foo
		function foo() returns Integer
				return 1
		end 'foo'
end 'OnlyFoo'

type NeedsBoth uses T where T is Foo and Bar
		var item as T
end 'NeedsBoth'

typealias Bad = NeedsBoth with OnlyFoo

function main() returns ExitCode
		return 0
end 'main'
```
```maxoncstderr
error E3017: specs/fragments/where-clauses/where-clauses.and-violation.test:23:11: Type 'OnlyFoo' does not satisfy constraint 'Bar' required by type parameter 'T' of 'NeedsBoth'
```

### Equality on unconstrained type parameter requires Equatable

Using `==` or `!=` on a type parameter that isn't constrained with `where T is Equatable` should produce a compile error:

<!-- test: where-clauses.eq-requires-equatable -->
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
error E3005: specs/fragments/where-clauses/where-clauses.eq-requires-equatable.test:6:17: Operator '==' requires type parameter 'T' to be constrained with 'where T is Equatable'
```

### Equality on Equatable-constrained type parameter compiles

When the type parameter is properly constrained, `==` should work:

<!-- test: where-clauses.eq-with-equatable -->
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

### A `Self`-returning static dispatches through the constraints on the value it builds

A `static function` has no `self`, so it used to carry no witness tables at all — and a static that
built a `Self{…}` and called a constraint-dispatching method on it aborted the compiler:
`forwardCallerWitness: caller 'Map.init' carries 0 witness parameter(s) and was asked to forward slot 0
to 'Map.upsert'`. A static that RETURNS its own type has the same witness source the layout descriptor
already used — the concrete instance it builds — so it carries the block on the same terms.
`stdlib/Map.maxon`'s `static function init(…) returns Self` is this program.

<!-- test: where-clauses.self-returning-static-dispatches-on-its-own-value -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder uses T where T is Hashable and Equatable
	var v as T

	export static function of(v T) returns Self
		var built = Self{v: v}
		if built.matches(v) 'sameValue'
			return built
		end 'sameValue'
		panic("Holder.of: the value it was built from must equal itself")
	end 'of'

	export function matches(other T) returns bool
		return v.equals(other)
	end 'matches'

	export function digest() returns HashValue
		return v.hash()
	end 'digest'
end 'Holder'

typealias H = Holder with Integer

function main() returns ExitCode
	let h = H.of(41)
	return h.digest()
end 'main'
```
```exitcode
41
```

### Error: a static that does NOT return `Self` has no witness source

The other half of the rule above, stated as a refusal rather than an abort. Threading the dictionary
from the static CALL SITE's alias — `H.digestOf(41)` knows `H = Holder with Integer` — is the same
"later slice" the layout descriptor's own static rule names.

<!-- test: where-clauses.error.static-without-self-return-cannot-dispatch -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder uses T where T is Hashable and Equatable
	var v as T

	export function digest() returns HashValue
		return v.hash()
	end 'digest'

	export static function digestOf(v T) returns HashValue
		let h = Self{v: v}
		return h.digest()
	end 'digestOf'
end 'Holder'

typealias H = Holder with Integer

function main() returns ExitCode
	return H.digestOf(41)
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/where-clauses/where-clauses.error.static-without-self-return-cannot-dispatch.test:13:12: Unsupported: calling 'digest' (which dispatches through the type's `where` constraints) on a value of the enclosing type, from a `static function` that carries no witness tables of its own to forward — a static sources them from the instance it RETURNS, so declare this one `returns Self` and call the method on the result, or make it an instance method
```

### An INTERFACE EXTENSION's method over a constrained conformer reserves that conformer's witnesses

An extension method is monomorphized for ONE conformer, so it carries that conformer's witness block
exactly as the conformer's own methods do — and the supply side always passed it, because
`witnessConstraintsOfMethod` resolves a callee by its TYPE name whatever door the method came through.
The body reserved none, so it read two argument registers it had never declared. `stdlib/Interfaces.maxon`'s
`extension Iterator`'s `advanceBy` is the reaching method.

<!-- test: where-clauses.interface-extension-over-a-constrained-conformer -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = int(0 to u64.max)

type Ticks uses T where T is Hashable and Equatable
	typealias Items = Array with T
	var items as Items
	var at = 0

	export static function create(items Items) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items}
	end 'create'

	export function current() returns T
		return try items.get(at) otherwise panic("Ticks.current: the positioning invariant holds")
	end 'current'

	export function advance() throws IterationError
		at = at + 1
		if at >= items.count() 'done'
			throw IterationError.exhausted
		end 'done'
	end 'advance'
end 'Ticks'

extension Ticks
	export function skip(n Step) throws IterationError
		var i = 0
		while i < n 'loop'
			try self.advance()
			i = i + 1
		end 'loop'
	end 'skip'
end 'Ticks'

typealias IntArray = Array with Integer
typealias IntTicks = Ticks with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(5)
	a.push(6)
	a.push(7)
	var t = try IntTicks.create(a) otherwise panic("non-empty")
	try t.skip(2) otherwise panic("three elements")
	return t.current()
end 'main'
```
```exitcode
7
```
