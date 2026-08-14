---
feature: interface-extensions
status: stable
keywords: [extension, interface, method]
category: type-system
---

# Interface Extensions

## Documentation

Extensions allow you to add methods to interfaces that are automatically available on all types conforming to that interface. Unlike regular interface methods that must be implemented by each conforming type, extension methods have a single implementation that works for all conformers.

### Extension Declaration

Extensions are declared with the `extension` keyword followed by an interface name:

```maxon
typealias Score = int(i64.min to i64.max)

extension Iterable
	function count() returns Score
		var n = 0
		for _ in self 'loop'
			n = n + 1
		end 'loop'
		return n
	end 'count'
end 'Iterable'
```

### How Extensions Work

When you define an extension method:
1. The method becomes available on all types that conform to the interface
2. The `self` keyword refers to the concrete type instance
3. Extension methods can call any method required by the interface
4. Associated types from the interface are resolved to the concrete type's bindings

### Using Associated Types

Extensions can use the interface's associated types. These are automatically substituted with the concrete type's associated type bindings:

```maxon
typealias Score = int(i64.min to i64.max)

interface Container uses Element
	function get(index Score) returns Element
end 'Container'

extension Container
	function first() returns Element
		return self.get(0)
	end 'first'
end 'Container'
```

When called on a type like `IntArray implements Container with int`, the return type `Element` becomes `int`.

### Extension Method Synthesis

When a type conforms to an interface that has extensions, the compiler synthesizes concrete methods for that type. For example, if `type IntArray implements Array with Int` conforms to `Iterable`, calling `myArray.count()` invokes a method specialized for `IntArray`.

### Transitive Extensions

Extensions from interfaces are also applied transitively. If interface `B` extends interface `A`, extensions on `A` are available on types conforming to `B`.

### Example: Map on Iterable

```maxon
extension Iterable
	typealias ElementArray = Array with Element

typealias FnTypeAlias1 = function(Element) returns Element
	function map(transform FnTypeAlias1) returns ElementArray
		var result = ElementArray.create()
		for item in self 'loop'
			result.push(transform(item))
		end 'loop'
		return result
	end 'map'
end 'Iterable'
```

This `map` extension works on any `Iterable` type (Array, Set, Map, etc.) and returns a new array with transformed elements.

## Tests

<!-- test: basic-extension-on-array -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Countable
	function value() returns Integer
end 'Countable'

extension Countable
	function count() returns Integer
		return 42
	end 'count'
end 'Countable'

type IntList implements Countable
	let data as Integer

	function value() returns Integer
		return data
	end 'value'

	static function create(data Integer) returns Self
		return Self{data: data}
	end 'create'
end 'IntList'

function main() returns ExitCode
	let list = IntList.create(5)
	return list.count()
end 'main'
```
```exitcode
42
```


<!-- test: extension-with-self -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Summable
	function value() returns Integer
end 'Summable'

extension Summable
	function doubled() returns Integer
		return self.value() * 2
	end 'doubled'
end 'Summable'

type Number implements Summable
	let n as Integer

	function value() returns Integer
		return n
	end 'value'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Number'

function main() returns ExitCode
	let num = Number.create(21)
	return num.doubled()
end 'main'
```
```exitcode
42
```


<!-- test: extension-multiple-types -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Valued
	function val() returns Integer
end 'Valued'

extension Valued
	function valPlusTen() returns Integer
		return self.val() + 10
	end 'valPlusTen'
end 'Valued'

type TypeA implements Valued
	let a as Integer
	function val() returns Integer
		return a
	end 'val'

	static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'TypeA'

type TypeB implements Valued
	var b as Integer
	function val() returns Integer
		return b * 2
	end 'val'

	static function create(b Integer) returns Self
		return Self{b: b}
	end 'create'
end 'TypeB'

function main() returns ExitCode
	let ta = TypeA.create(5)
	let tb = TypeB.create(10)
	return ta.valPlusTen() + tb.valPlusTen()
end 'main'
```
```exitcode
45
```


<!-- test: extension-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Scalable
	function base() returns Integer
end 'Scalable'

extension Scalable
	function scale(factor Integer) returns Integer
		return self.base() * factor
	end 'scale'
end 'Scalable'

type Amount implements Scalable
	let amount as Integer

	function base() returns Integer
		return amount
	end 'base'

	static function create(amount Integer) returns Self
		return Self{amount: amount}
	end 'create'
end 'Amount'

function main() returns ExitCode
	let a = Amount.create(7)
	return a.scale(6)
end 'main'
```
```exitcode
42
```


<!-- test: extension-returns-struct -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Pointlike
	function getX() returns Integer
	function getY() returns Integer
end 'Pointlike'

type SimplePoint
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'SimplePoint'

extension Pointlike
	function asSimple() returns SimplePoint
		return SimplePoint.create(self.getX(), y: self.getY())
	end 'asSimple'
end 'Pointlike'

type Coord implements Pointlike
	let cx as Integer
	let cy as Integer

	function getX() returns Integer
		return cx
	end 'getX'

	function getY() returns Integer
		return cy
	end 'getY'

	static function create(cx Integer, cy Integer) returns Self
		return Self{cx: cx, cy: cy}
	end 'create'
end 'Coord'

function main() returns ExitCode
	let c = Coord.create(10, cy: 32)
	let p = c.asSimple()
	return p.x + p.y
end 'main'
```
```exitcode
42
```


<!-- test: stdlib-map-on-array -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let nums = [1, 2, 3, 4, 5]
	let doubled = nums.map(function(x Integer) gives x * 2)

	var sum = 0
	for n in doubled 'loop'
		sum = sum + n
	end 'loop'

	// 2 + 4 + 6 + 8 + 10 = 30
	return sum
end 'main'
```
```exitcode
30
```


<!-- test: stdlib-map-on-set -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let s = Set from [10, 20, 30]
	let mapped = s.map(function(x Integer) gives x + 1)

	var sum = 0
	for n in mapped 'loop'
		sum = sum + n
	end 'loop'

	// 11 + 21 + 31 = 63 (order may vary but sum is same)
	return sum
end 'main'
```
```exitcode
63
```

<!-- test: stdlib-map-on-map -->
```maxon
function main() returns ExitCode
	let m = ["a": 1, "b": 2, "c": 3]

	var sum = 0
	for pair in m 'loop'
		sum = sum + pair.1 * 10
	end 'loop'

	// 10 + 20 + 30 = 60
	return sum
end 'main'
```
```exitcode
60
```

<!-- test: stdlib-map-on-map-with-function -->
```maxon
function main() returns ExitCode
	let m = ["a": 1, "b": 2, "c": 3]
	let mapped = m.map(function(p) gives p)

	var sum = 0
	for pair in mapped 'loop'
		sum = sum + pair.1
	end 'loop'

	// 1 + 2 + 3 = 6
	return sum
end 'main'
```
```exitcode
6
```


<!-- test: extension-with-internal-closure -->
A closure declared inside an interface-extension body. When `t.bump()` is
called on a concrete `Three implements Tagger`, MonomorphizeExtensions
clones `Tagger.bump` as `Three.bump` and walks the cloned body's ops with
the `Self → Three` substitution. The walk encounters a `closureCreate` op
for the inline `function(x Integer) gives x + one` literal, which without
per-substitution closure specialization panics with "closure '_$closure_N'
referenced from a monomorphized extension body". The closure body's
`x + one` is a plain `Integer` binop so the closure itself needs no
type-parameter dispatch; only the outer `tag()` call exercises the
substitution. Compiling at all confirms the panic doesn't fire.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagger
	function tag() returns Integer
end 'Tagger'

extension Tagger
	export function bump() returns Integer
		let one = 1 as Integer
		let inc = function(x Integer) gives x + one
		return inc(tag())
	end 'bump'
end 'Tagger'

type Three implements Tagger
	export static function make() returns Self
		return Self{}
	end 'make'
	export function tag() returns Integer
		return 3
	end 'tag'
end 'Three'

function main() returns ExitCode
	let t = Three.make()
	print("{t.bump()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```


<!-- test: extension-with-substitution-dependent-closure -->
A closure declared inside an interface-extension body whose body IS
substitution-dependent — it calls `tag()`, which resolves through the
concrete `implements Tagger` receiver type. With multiple conformers,
MonomorphizeExtensions must produce DISTINCT closure bodies per
substitution: `_$closure_0$Three`'s body must call `Three.tag` while
`_$closure_0$Seven`'s body must call `Seven.tag`. Without per-
substitution closure cloning (mirroring C# bootstrap's
`FunctionCloner.SpecializeClosureName` + `ClosureSpecializations`
drain) both monomorphized `callTag` methods would share a single
closure body that resolves `tag()` to the first-cloned receiver,
silently miscompiling: `Seven.callTag` would compute `3*2` instead of
`7*2`. The expected result of `a.callTag() + b.callTag()` is
`3*2 + 7*2 = 20`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagger
	function tag() returns Integer
end 'Tagger'

extension Tagger
	export function callTag() returns Integer
		let inner = function() gives tag() * 2
		return inner()
	end 'callTag'
end 'Tagger'

type Three implements Tagger
	export static function make() returns Self
		return Self{}
	end 'make'
	export function tag() returns Integer
		return 3
	end 'tag'
end 'Three'

type Seven implements Tagger
	export static function make() returns Self
		return Self{}
	end 'make'
	export function tag() returns Integer
		return 7
	end 'tag'
end 'Seven'

function main() returns ExitCode
	let a = Three.make()
	let b = Seven.make()
	let sum = a.callTag() + b.callTag()
	return sum
end 'main'
```
```exitcode
20
```

### An extension NO type implements leaks nothing into file scope

<!-- test: error.an-unconformed-extension-does-not-leak-its-associated-type-names -->
⛔⛔ **AN `extension <Interface>` WHOSE INTERFACE NO TYPE IMPLEMENTS MUST NOT MAKE THAT INTERFACE'S
ASSOCIATED-TYPE NAMES SPELLABLE AT FILE SCOPE (W14 review).** `Parser.readExtensionHeader` installs the
extended declaration's parameter names in `enclosingTypeParams` so `readWhereClause` can resolve a
constrained name against the right list. The conformer loop that follows re-enters a real declaration
scope per conformer (`enterTypeScope`) — but an interface **nothing implements has no conformers**, so
that loop never ran and the names stayed live for the rest of the file's parse.

⛔ **MEASURED on the merge base: `function f(x Element)` below COMPILED CLEAN**, `Element` resolving to
a type parameter of a declaration `f` is not inside. It names no declared type, so the only correct
answer is E3011 — which is what the compiler now gives, because the window is opened around the `where`
clause read alone and closed at its end.

⚠ **THE SECOND CASE BELOW IS THE SAME BUG ONE DOOR DEEPER, AND IT WAS A COMPILER PANIC.** Since W14 a
`typeParameter`'s payload is a digest of `(declaring type, parameter name)` and
`ProgramSignatures.opaqueTypeParamPosition` recovers the position from the owner ledger that
`recordStruct` / `recordInterfaceDeclaration` fill. A token minted under an `enclosingType` that
declared nothing has no owner, so the leak reached that door as
*"type-parameter token … has no owner"* rather than as a diagnostic. The pair is kept because the two
cases fail at two different passes: this one at type RESOLUTION, the next at `SemanticCheck`.
```maxon
typealias Num = int(0 to 1000)

interface Seq uses Element
	function firstOne() returns Element
end 'Seq'

extension Seq
	export function twice() returns Element
		return self.firstOne()
	end 'twice'
end 'Seq'

function f(x Element) returns Num
	_ = x
	return 1
end 'f'

function main() returns ExitCode
	return f(3)
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Element'
```

<!-- test: error.an-unconformed-extension-leak-is-not-a-compiler-panic -->
The deeper half of the case above: `f` returns a GENERIC INSTANCE, which is what routes its call through
`SemanticCheck.checkTypeParameterArgs` — the pass that asks `opaqueTypeParamPosition` for an opaque
formal's position in the declaring type's `uses` list. Against the W14 tip before this fix that ask
PANICKED the compiler; against the merge base the program compiled and ran (exit 7). One refusal is the
right answer to both, and it is the same sentence the case above pins, because the fault is the same
undeclared name.
```maxon
typealias Num = int(0 to 1000)

type Box uses T
	export var v as T

	export static function of(x T) returns Box
		return Box{v: x}
	end 'of'

	export function get() returns T
		return self.v
	end 'get'
end 'Box'

typealias BoxN = Box with Num

interface Seq uses Element
	function firstOne() returns Element
end 'Seq'

extension Seq
	export function twice() returns Element
		return self.firstOne()
	end 'twice'
end 'Seq'

function f(x Element) returns BoxN
	_ = x
	return BoxN.of(7)
end 'f'

function main() returns ExitCode
	return f(3).get()
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Element'
```

### The ORDER of an `implements` clause does not decide which extensions arrive

<!-- test: conformance-clause-order-does-not-change-extensions -->
⭐⭐ **`implements Tagged with Integer, Held with Integer` AND `implements Held with Integer, Tagged
with Integer` ARE THE SAME CLAUSE, so they must publish the same extension methods (W95).** The
declaration sweep is what decides which types an `extension <Interface>` lands on, and it used to STOP
its interface list at the first `with` — it could not tell `A with X, B with X` (two interfaces) from
`Pair with X, Y` (one interface, two arguments) without an arity only the complete index holds. So the
interface named after a `with` silently received no extensions, and moving it ahead of the `with` cured
it: clause ORDER changed semantics.

⛔ **MEASURED before the fix:** `MarkerFirst` below reported
*"error E3004: call to undefined function 'MarkerFirst.heldPlusOne'"*, while `HeldFirst` — the identical
clause, reordered — compiled and ran. The whole-program re-read (`Queries.foldConformanceClauses`) is
what makes the two spellings one program; the golden beside this case is where both
`MarkerFirst.heldPlusOne` and `HeldFirst.heldPlusOne` are shown emitted.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagged uses Tag
	function tag() returns Tag
end 'Tagged'

interface Held uses Item
	function held() returns Item
end 'Held'

extension Held
	export function heldPlusOne() returns Item
		return self.held() + 1
	end 'heldPlusOne'
end 'Held'

type MarkerFirst implements Tagged with Integer, Held with Integer
	export var n as Integer

	export static function of(n Integer) returns Self
		return MarkerFirst{n: n}
	end 'of'

	export function tag() returns Integer
		return self.n
	end 'tag'

	export function held() returns Integer
		return self.n
	end 'held'
end 'MarkerFirst'

type HeldFirst implements Held with Integer, Tagged with Integer
	export var n as Integer

	export static function of(n Integer) returns Self
		return HeldFirst{n: n}
	end 'of'

	export function tag() returns Integer
		return self.n
	end 'tag'

	export function held() returns Integer
		return self.n
	end 'held'
end 'HeldFirst'

function main() returns ExitCode
	let a = MarkerFirst.of(20)
	let b = HeldFirst.of(21)
	return a.heldPlusOne() + b.heldPlusOne()
end 'main'
```
```exitcode
43
```

<!-- test: conformance-clause-order-reaches-an-enum-too -->
⭐ **THE SAME CLAUSE, ON THE OTHER DECLARATION KIND.** An `enum` records no `with` bindings — it has no
conditional extension to evaluate — but a LOST NAME is the same loss, and on an error enum the name that
gets lost is the one `throws Error` is narrowed against (`EnumLayout.conformsTo`). `Slow` writes
`implements Tagged with Code, Error`, so before the whole-program re-read the sweep recorded `Tagged`
alone, the abstract-requirement narrowing could not see an `Error` conformance, and `Point.digest`
throwing `Slow` was refused — while `implements Error, Tagged with Code`, the same clause reordered,
compiled. Both edges ride one exit code, as `interface-conformance.md`'s
`throws-narrower-than-abstract-requirement` does: the success edge carries 20 through the witness, the
error edge takes the handler's 55.
```maxon
typealias Code = int(0 to u32.max)

interface Tagged uses Tag
	function tag() returns Tag
end 'Tagged'

enum Slow implements Tagged with Code, Error
	tooSmall

	export function tag() returns Code
		return 1
	end 'tag'
end 'Slow'

interface Digest
	function digest() returns Code throws Error
end 'Digest'

type Point implements Digest
	export var x as Code

	export static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	export function digest() returns Code throws Slow
		if self.x < 10 'small'
			throw Slow.tooSmall
		end 'small'
		return self.x
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function itemDigest() returns Code
		return try self.item.digest() otherwise 55
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let good = PointBox.create(Point.create(20))
	let bad = PointBox.create(Point.create(3))
	return (good.itemDigest() + bad.itemDigest()) as ExitCode
end 'main'
```
```exitcode
75
```
