---
feature: witness-closure-inheritance
status: stable
keywords: [where, constraints, type-parameters, generics, interfaces, witness, closure, capture, environment]
category: type-system
---

# Closure-Env Witness Inheritance

## Documentation

A closure written inside a `where`-constrained generic body may dispatch through the enclosing
declaration's constraints: `function(x Element, y Element) gives x.compare(y)` inside
`extension Array where Element is Comparable` calls `Comparable.compare` through the same witness table
the enclosing method holds.

The table reaches the lifted body as an ordinary CAPTURE. It cannot reach it any other way: a closure is
called through the uniform `(userargs, env)` indirect shape, which has exactly one hidden slot and it is
the environment, and the call site (`cmp(a, b)`, deep inside a sort helper) knows nothing of the closure's
origin, let alone its constraints. So the witness occupies one environment slot — one machine word,
stored where the closure is BUILT, inside the constrained method that holds the table — and each dispatch
in the body reads it back with the same single `loadIndirect` every other capture is read with.

Because the table is a VALUE rather than a folded address, one lifted body serves every instantiation:
two `Array with …` (or `Box with …`) alias of one generic type call the same lifted function and each
supplies its own table.

Closures NEST by chaining, one hop per level: a closure inside a closure captures the witness from its own
immediate enclosing closure's environment, which in turn captured it from the declaring function. This is
why a nested closure may inherit a witness even though it may not capture an ordinary variable from a
non-immediate frame — every hop here is an immediate one.

A `static function` (and a synthesized field-default helper) reserves no witness parameters, so there is
no table anywhere in the chain for a closure written inside one to inherit. That is refused with a
position, the same shape and for the same reason a static may not FORWARD its witnesses to a call on a
value of its own type.

## Tests

<!-- test: witness-closure-inheritance.closure-dispatches-type-parameter-witness -->
The base case: a closure inside an instance method of `type Box uses T where T is Digest` dispatches
`Digest.digest` on its own `T`-typed parameter. Before closure-env inheritance the parser had no witness
to name here and aborted the compiler.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		let f = function(v T) gives v.digest()
		return f(self.item)
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let p = Point.create(3)
	let b = PointBox.create(p)
	print("d={b.itemDigest()}")
	return 0
end 'main'
```
```stdout
d=93
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.closure-witness-in-type-extension -->
The constraint written on a TYPE EXTENSION rather than on the declaration — `stdlib/Array.maxon`'s
`extension Array where Element is Comparable` shape exactly. The extension body's methods reserve the
witness parameters, so the closure inherits from the extension's clause.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Box uses T
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
end 'Box'

extension Box where T is Digest
	export function itemDigest() returns Code
		let f = function(v T) gives v.digest()
		return f(self.item)
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	print("d={b.itemDigest()}")
	return 0
end 'main'
```
```stdout
d=93
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.closure-witness-in-interface-extension -->
An INTERFACE extension's body, which shv2 parses once per conformer with the CONFORMER's own `uses`
parameters and `where` clause installed (`Parser.openExtensionBodyScope`). A closure there inherits the
conformer's witness exactly as one in the type's own body does. `93` from the dispatch plus `1` from the
ordinary method call beside it.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

interface Peek
	function base() returns Code
end 'Peek'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Box uses T implements Peek where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function base() returns Code
		return 1
	end 'base'
end 'Box'

extension Peek
	export function viaClosure() returns Code
		let f = function(v T) gives v.digest()
		return f(self.item) + self.base()
	end 'viaClosure'
end 'Peek'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	print("d={b.viaClosure()}")
	return 0
end 'main'
```
```stdout
d=94
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.two-instantiations-through-one-lifted-body -->
⭐ The witness must be DYNAMIC, not folded. `Box.itemDigest` is one shared body under dictionary-passing,
so its closure lifts to ONE function — and both instantiations call it. `Times31` gives 93 and `Times2`
gives 6 from the same lifted code, which a table baked into the lift could not produce.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Times31 implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Times31'

type Times2 implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 2
	end 'digest'
end 'Times2'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		let f = function(v T) gives v.digest()
		return f(self.item)
	end 'itemDigest'
end 'Box'

typealias BoxA = Box with Times31
typealias BoxB = Box with Times2

function main() returns ExitCode
	let a = BoxA.create(Times31.create(3))
	let b = BoxB.create(Times2.create(3))
	print("a={a.itemDigest()} b={b.itemDigest()}")
	return 0
end 'main'
```
```stdout
a=93 b=6
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.nested-closure-inherits-through-two-hops -->
⭐ A closure inside a closure. The inner lift captures the witness from the OUTER lift's environment, and
the outer captured it from the method's hidden parameter — two hops, each of them a capture from the
immediate enclosing frame, which is the only kind the environment machinery builds.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	typealias TFn = function(T) returns Code
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export static function applyStatic(f TFn, z T) returns Code
		return f(z)
	end 'applyStatic'
	export function itemDigest() returns Code
		let outer = function(v T) gives Self.applyStatic(function(w T) gives w.digest(), z: v)
		return outer(self.item)
	end 'itemDigest'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	print("d={b.itemDigest()}")
	return 0
end 'main'
```
```stdout
d=93
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.managed-element-through-an-inherited-witness -->
A MANAGED element: `Tag` owns a `String`, so the boxed conformer is refcounted and every trip through the
closure borrows it. The exit code is the gate — a retain the environment never balanced would leave the
run at 101 rather than 0.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Tag implements Digest
	export var name as String
	export var code as Code
	export static function create(name String, code Code) returns Self
		return Self{ name: name, code: code }
	end 'create'
	export function digest() returns Code
		return self.code * 31
	end 'digest'
end 'Tag'

type Box uses T where T is Digest
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function itemDigest() returns Code
		let f = function(v T) gives v.digest()
		return f(self.item)
	end 'itemDigest'
end 'Box'

typealias TagBox = Box with Tag

function main() returns ExitCode
	let t = Tag.create("hello", code: 3)
	let b = TagBox.create(t)
	print("d={b.itemDigest()}")
	return 0
end 'main'
```
```stdout
d=93
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.two-constraints-keep-their-own-slots -->
⭐ Two constraints on one parameter, dispatched through BOTH inside one closure. Each constraint takes its
own environment slot, and the answer distinguishes them: `tag()` is `x + 7` and `digest()` is `x * 31`, so
a slot mix-up prints a different number rather than failing. `10093 = 10 * 1000 + 93`.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

interface Tagged
	function tag() returns Code
end 'Tagged'

type Point implements Digest, Tagged
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
	export function tag() returns Code
		return self.x + 7
	end 'tag'
end 'Point'

type Box uses T where T is Digest and Tagged
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function both() returns Code
		let f = function(v T) gives v.tag() * 1000 + v.digest()
		return f(self.item)
	end 'both'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	print("r={b.both()}")
	return 0
end 'main'
```
```stdout
r=10093
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.witness-and-ordinary-capture-share-one-environment -->
The inherited witnesses sit in the SAME environment block as ordinary captures — here two constraint
tables and the enclosing method's `bias` parameter, four reads in one body over three slots. `10098` is
the case above plus the captured `5`.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

interface Tagged
	function tag() returns Code
end 'Tagged'

type Point implements Digest, Tagged
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
	export function tag() returns Code
		return self.x + 7
	end 'tag'
end 'Point'

type Box uses T where T is Digest and Tagged
	export var item as T
	export static function create(item T) returns Self
		return Self{ item: item }
	end 'create'
	export function both(bias Code) returns Code
		let f = function(v T) gives v.tag() * 1000 + v.digest() + bias
		return f(self.item)
	end 'both'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	let b = PointBox.create(Point.create(3))
	print("r={b.both(5)}")
	return 0
end 'main'
```
```stdout
r=10098
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.one-constraint-dispatched-twice-takes-one-slot -->
A constraint dispatched twice in one body occupies ONE environment slot and is stored once — the same
dedup an ordinary name read twice gets. Two `Point`s so the two reads are distinguishable: `93 + 62`.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Pair uses T where T is Digest
	export var a as T
	export var b as T
	export static function create(a T, b T) returns Self
		return Self{ a: a, b: b }
	end 'create'
	export function sum() returns Code
		let f = function(p T, q T) gives p.digest() + q.digest()
		return f(self.a, self.b)
	end 'sum'
end 'Pair'

typealias PointPair = Pair with Point

function main() returns ExitCode
	let p = PointPair.create(Point.create(3), b: Point.create(2))
	print("s={p.sum()}")
	return 0
end 'main'
```
```stdout
s=155
```
```exitcode
0
```

<!-- test: witness-closure-inheritance.static-function-has-no-witness-to-lend -->
A `static function` carries no witness tables, so a closure written inside one has nothing to inherit and
is refused with a position rather than aborting the compiler. The refusal is the closure twin of the
forwarding refusal a static gets when it calls a dispatching method on a value of its own type.
```maxon
typealias Code = int(0 to u32.max)

interface Digest
	function digest() returns Code
end 'Digest'

type Point implements Digest
	export var x as Code
	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'
	export function digest() returns Code
		return self.x * 31
	end 'digest'
end 'Point'

type Box uses T where T is Digest
	export var item as T
	export static function digestOf(v T) returns Code
		let f = function(w T) gives w.digest()
		return f(v)
	end 'digestOf'
end 'Box'

typealias PointBox = Box with Point

function main() returns ExitCode
	print("d={PointBox.digestOf(Point.create(3))}")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:33: Unsupported: a closure that dispatches through the type's `where` constraints, written inside a `static function` (or a field-default helper) that carries no witness tables of its own — the closure inherits its table through its environment from the function that built it, and a static has no receiver to source one from. Write the closure in an instance method, or take the operation as a function parameter
```
