---
feature: inner-alias-construction
status: experimental
keywords: [typealias, struct-literal, construction, generics, container, extension]
category: type-system
---

# Constructing an inner `typealias`

## Documentation

`E3076` restricts a struct literal to the body of the type it constructs — `Self{…}` / `Point{…}` inside
`type Point`, and nowhere else. A `typealias` a type body DECLARES is inside that body too, so
`<InnerAlias>{}` written there is legal: the rule is about WHERE THE ALIAS IS DECLARED, not about
genericity, and not about the type the alias resolves to.

What it BUILDS is the alias's EMPTY CONTAINER — the same value `<InnerAlias>.create()` builds, from the
same producer. `stdlib/Set.maxon` writes exactly that (`var elems = ElementArray{}` then `elems.resize(cap)`),
and so do `stdlib/Map.maxon` and `stdlib/Interfaces.maxon`.

A TOP-LEVEL alias is unaffected and stays E3076: it is declared in no body, so no body may construct it.

## Tests

### An inner array alias is constructible in the non-generic body that declares it

`Plain` has no type parameters at all, and its own `typealias Nums = Array with ExitCode` is constructible
inside `Plain`. This is the case that settles that the rule is about the alias's DECLARATION SITE and not
about genericity.

<!-- test: nongeneric-inner-array-alias -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Plain
	typealias Nums = Array with ExitCode

	static function make() returns Count
		var n = Nums{}
		n.resize(4)
		return n.count()
	end 'make'
end 'Plain'

function main() returns ExitCode
	let c = Plain.make()
	if c == 4 'ok'
		return 4
	end 'ok'
	return 1
end 'main'
```
```exitcode
4
```

### An inner array alias over the type's own parameter is constructible in its body

The generic form `stdlib/Set.maxon` and `stdlib/Map.maxon` are written in: `typealias ElementArray = Array
with Element` inside `type Holder uses Element`, constructed by `Holder`'s own `create`. The value is the
OPAQUE-element array `ElementArray.create()` builds, so it resizes and counts exactly as that one does.

<!-- test: generic-inner-array-alias -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		var n = ElementArray{}
		n.resize(5)
		return Self{ items: n }
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'
end 'Holder'

typealias IntHolder = Holder with ExitCode

function main() returns ExitCode
	var h = IntHolder.create()
	let c = h.count()
	if c == 5 'ok'
		return 5
	end 'ok'
	return 1
end 'main'
```
```exitcode
5
```

### A managed-element inner array alias owns and frees its elements

The array `Names{}` builds is OWNED exactly as `Names.create()`'s is — enrolled as the statement's temporary
and then moved into the struct's field, whose destructor frees the String once. A literal that produced an
UNOWNED record would leak the String (exit 101); one that produced two owners would double-free.

<!-- test: managed-element-inner-array-alias -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Bag
	typealias Names = Array with String

	export var names as Names

	export static function create() returns Self
		var n = Names{}
		n.push("a string long enough to force a heap allocation")
		return Self{names: n}
	end 'create'

	export function count() returns Count
		return self.names.count()
	end 'count'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	let c = b.count()
	if c == 1 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### An inner `Set` alias's `{}` is E3076 — because `Set` is a DECLARED generic now

⭐⭐ **THIS CASE ASSERTED `exitcode 8` UNTIL W90, AND THE MOVE IS THE RETIREMENT AND NOT A LOST CAPABILITY.**
Its prose read *"every builtin container answers `{}` the same way … a `Set` inner alias is that roster's
second member"* — and with `stdlib/Set.maxon` listed, `Set` has left that roster. It is an ordinary declared
generic whose fields are unexported, so `Nums{}` from another type's body takes the ordinary refusal, which is
**exactly** `user-generic-inner-alias-keeps-e3076` below with `Pair` replaced by `Set`.

⭐ **AND THE CURE THAT DIAGNOSTIC PRESCRIBES IS ALREADY PINNED GREEN, ONE CASE DOWN**
(`inner-set-alias-create`, `exitcode 8`) — which is this file's own standing rule that *"a diagnostic that
names a cure owes a case that takes it"*. So the surface is not lost: it moved one spelling over, to the one
the message tells the author to write.

<!-- test: error.inner-set-alias-literal-keeps-e3076 -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Plain
	typealias Nums = Set with ExitCode

	static function make() returns Count
		var s = Nums{}
		s.insert(3)
		s.insert(3)
		return s.count()
	end 'make'
end 'Plain'

function main() returns ExitCode
	let c = Plain.make()
	if c == 1 'ok'
		return 8
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3076: <fragment>:9:16: type 'Nums' can only be constructed from within its own methods; use a static factory method instead
```

### An inner `Set` alias's `create()` builds the same container the literal does

The `.create()` spelling of the very same alias. It is pinned beside the literal because the two MUST agree:
a roster that admitted one and not the other is what let a `Set` inner alias's `create()` yield a value of
type `unknown` instead of a set.

<!-- test: inner-set-alias-create -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Plain
	typealias Nums = Set with ExitCode

	static function make() returns Count
		var s = Nums.create()
		s.insert(3)
		s.insert(3)
		return s.count()
	end 'make'
end 'Plain'

function main() returns ExitCode
	let c = Plain.make()
	if c == 1 'ok'
		return 8
	end 'ok'
	return 1
end 'main'
```
```exitcode
8
```

### `Self{}` in a body that also declares an inner alias is unchanged

The inner-alias route must not claim the enclosing type's own name. `Holder` declares an inner alias AND
constructs itself with `Self{}`, whose every field is defaulted.

<!-- test: self-literal-beside-an-inner-alias -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder
	typealias Nums = Array with ExitCode

	export var seed = 0

	export static function create() returns Self
		return Self{}
	end 'create'

	export function count() returns Count
		var n = Nums{}
		n.resize(self.seed)
		return n.count()
	end 'count'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	let c = h.count()
	if c == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### An inner alias may SHADOW its own type's name, and the type still wins

A written name resolves to the enclosing type before it resolves to an inner alias, so `type Holder`
declaring `typealias Holder = …` still means the TYPE at `returns Holder` — and `Self{…}` (which arrives
as `Holder`) must build the same thing, or one declaration would denote two types in one body.

<!-- test: an-inner-alias-shadowing-its-own-type -->
```maxon
typealias ExitCode = int(0 to 125)

type Holder
	typealias Holder = Array with ExitCode

	export var seed as ExitCode

	export static function create() returns Holder
		return Self{seed: 4}
	end 'create'

	export function seedValue() returns ExitCode
		return self.seed
	end 'seedValue'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.seedValue()
end 'main'
```
```exitcode
4
```

### A TOP-LEVEL alias is still restricted

The half both reference compilers already agree on. A top-level `typealias` is declared in no body, so no
body is its own — E3076 stands.

<!-- test: top-level-alias-is-still-restricted -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Nums = Array with ExitCode

function main() returns ExitCode
	var n = Nums{}
	n.resize(3)
	let c = n.count()
	if c == 3 'ok'
		return 3
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3076: <fragment>:6:15: type 'Nums' can only be constructed from within its own methods; use a static factory method instead
```

### Another type's inner alias is still restricted

The widening is about the body that DECLARES the alias. `Other` did not declare `Nums`, so `Other` may not
construct it.

<!-- test: another-types-inner-alias-is-restricted -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Owner
	typealias Nums = Array with ExitCode

	static function own() returns Count
		var n = Nums{}
		return n.count()
	end 'own'
end 'Owner'

type Other
	static function make() returns Count
		var n = Nums{}
		return n.count()
	end 'make'
end 'Other'

function main() returns ExitCode
	let a = Owner.own()
	let b = Other.make()
	if a == b 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3076: <fragment>:16:16: type 'Nums' can only be constructed from within its own methods; use a static factory method instead
```

### A free function may not construct another type's inner alias

The same rule from outside every type body.

<!-- test: free-function-inner-alias-is-restricted -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Owner
	typealias Nums = Array with ExitCode

	static function own() returns Count
		var n = Nums{}
		return n.count()
	end 'own'
end 'Owner'

function main() returns ExitCode
	var n = Nums{}
	let c = n.count()
	let d = Owner.own()
	if c == d 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3076: <fragment>:15:15: type 'Nums' can only be constructed from within its own methods; use a static factory method instead
```

### An inner alias naming a USER type's generic instance keeps E3076

`IntPair` denotes `Pair with ExitCode`, and `Pair`'s invariants are `Pair`'s own to establish — so writing
its literal inside `Plain` is exactly what E3076 restricts, and the cure it prescribes — a static factory,
here `IntPair.create(…)` — is real. The widening does not reach it: only the containers this compiler OWNS have an empty value a body
outside the declaring type may mint.

<!-- test: user-generic-inner-alias-keeps-e3076 -->
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Plain
	typealias IntPair = Pair with ExitCode

	static function make() returns ExitCode
		let p = IntPair{a: 9}
		return p.a
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```maxoncstderr
error E3076: <fragment>:16:19: type 'IntPair' can only be constructed from within its own methods; use a static factory method instead
```

### ⭐ …and the CURE THAT DIAGNOSTIC PRESCRIBES ACTUALLY WORKS — the half that was missing (W7)

The SAME program as `user-generic-inner-alias-keeps-e3076`, written the way its message tells the author to
write it. Until W7 this answered `E2015 … a field access on 'p', which is declared 'unknown'` — the cure
E3076 names produced a value of no type, so the refusal above sent every author into a second refusal. The
bootstrap compiles and runs it (**exit 9**, measured), so the divergence was shv2's alone.

⚠ **A DIAGNOSTIC THAT NAMES A CURE OWES A CASE THAT TAKES IT.** The refusal above was specced and the cure
was not, which is precisely why a defect this reachable survived: `Parser.parseInnerAliasLiteral`'s own
comment recorded it in prose (*"whose cure (`{alias}.create()`) fails one door over with a value of type
`unknown`"*) two arms above another comment calling the cure *"real"*. Two sentences in one function
disagreeing, with nothing running either.

<!-- test: user-generic-inner-alias-create-is-the-cure -->
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Plain
	typealias IntPair = Pair with ExitCode

	static function make() returns ExitCode
		let p = IntPair.create(9)
		return p.a
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```exitcode
9
```

### …and the alias's argument may be the ENCLOSING TYPE'S OWN PARAMETER

The shape v1 resolves in `TypeResolution.resolveGenericAliasArgName` — the alias's argument is a name in the
declaring type's `uses` list, so `Wrap.ItemHolder` is `Holder with typeParameter(0)` and the instantiation
`Wrap with ExitCode` decides what that is. The declaration sweep already resolved the argument that way (it
is what makes `Array with T` work); what was missing was the STATIC CALL door reading the alias back under
its whole-program key. Bootstrap: **exit 7**, measured.

<!-- test: generic-body-user-generic-inner-alias-create -->
```maxon
typealias ExitCode = int(0 to 125)

type Holder uses Held
	let value as Held

	export static function create(value Held) returns Self
		return Self{value: value}
	end 'create'

	export function get() returns Held
		return value
	end 'get'
end 'Holder'

type Wrap uses Item
	typealias ItemHolder = Holder with Item

	var seed as Item

	export static function create(seed Item) returns Self
		return Self{seed: seed}
	end 'create'

	export function boxed() returns ItemHolder
		return ItemHolder.create(seed)
	end 'boxed'
end 'Wrap'

typealias IntWrap = Wrap with ExitCode

function main() returns ExitCode
	let w = IntWrap.create(7)
	return w.boxed().get()
end 'main'
```
```exitcode
7
```

### …and an `extension` body's inner alias over a USER type reaches its static too

`extension-body-inner-alias-builds-the-container` pinned the BUILTIN half of this; a user type's generic
instance took the same fall-through as `IntPair` did, because the arm that recognises an inner alias here
served only the containers this compiler owns. It is one door, so it is one fix.

⚠ **THE ORACLE REFUSES THIS STANDALONE PROGRAM (`E3003: 'WrappedTag' is a type and cannot be used directly
as a value`, measured) WHILE ACCEPTING THE IDENTICAL SHAPE IN ITS OWN `stdlib/`.** `stdlib/Interfaces.maxon`
declares `typealias WithIterSelf = WithIterIterator with Iter, Element` inside `extension Iterable` and
returns `WithIterSelf.create(iter)` from `:223` — over `WithIterIterator`, a user `type` — and the bootstrap
compiles and runs a program driving it (`arr.withIterator()`, measured: `0:10 1:20 2:30`). So the refusal is
a limitation of the bootstrap's standalone path and not a ruling about the shape; the stdlib is the witness
for what the language means here, and shv2 answers it uniformly at every declaration site.

<!-- test: extension-body-user-generic-inner-alias-create -->
```maxon
typealias ExitCode = int(0 to 125)

type Wrapped uses T
	let value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export function get() returns T
		return value
	end 'get'
end 'Wrapped'

interface Tagged uses Tag
	function tag() returns Tag
end 'Tagged'

export extension Tagged
	typealias WrappedTag = Wrapped with Tag

	export function wrapped() returns WrappedTag
		return WrappedTag.create(self.tag())
	end 'wrapped'
end 'Tagged'

type Box implements Tagged with ExitCode
	export var v as ExitCode

	export static function create() returns Self
		return Self{v: 5}
	end 'create'

	export function tag() returns ExitCode
		return self.v
	end 'tag'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.wrapped().get()
end 'main'
```
```exitcode
5
```

### …and the factory's PER-INSTANCE provenance survives the qualified key

The composition risk in reading an inner alias under its base-qualified key: `retypeGenericAliasConstructorResult`
records that key as the value's instance-alias provenance, and `Pair.Idx` results are re-qualified against it
(`retypeInstanceMethodResult`). `splitQualifiedName` cuts at the LAST separator, so `Plain.IntPair.Idx`
splits to the prefix `Plain.IntPair` — a real `genericAliases` key — whose base is `Pair`, and the inner
alias `Pair.Idx` is found. Both compilers answer **4**.

<!-- test: user-generic-inner-alias-create-carries-per-instance-identity -->
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	typealias Idx = int(0 to 100)

	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'

	export function firstIndex() returns Idx
		return 3
	end 'firstIndex'

	export function offsetBy(i Idx) returns Idx
		return i + 1
	end 'offsetBy'
end 'Pair'

type Plain
	typealias IntPair = Pair with ExitCode

	static function make() returns ExitCode
		let p = IntPair.create(9)
		let i = p.firstIndex()
		return p.offsetBy(i)
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```exitcode
4
```

### ⚖ An inner alias SHADOWING ANOTHER type's name wins at the STATIC door too, as it already did at the TYPE door

The precedence W7 moved, and it moved to remove a disagreement rather than to introduce one.
`namesInnerAliasHere`'s header states the rule the three doors share: the enclosing type's own name and its
type parameters outrank an inner alias, and nothing else does. `parseTypeReference` has always obeyed it —
MEASURED on this program's parameter form, `p Other` inside `Plain` is `Plain.Other` and a bare `Pair`
argument is refused *"expected 'Plain.Other', got 'Pair'"*. The STATIC door alone read the bare member, found
no alias registration under it, and silently fell through to the unrelated top-level `type Other`'s
`create`, so one written name meant two types in one body — which is exactly what that header forbids.

⚠ **THE ORACLE IS MEASURABLY BROKEN ON THIS PROGRAM AND IS NOT THE MODEL HERE**: it lets `Plain`'s inner
alias shadow the top-level `type Other` GLOBALLY, refusing `type Other`'s own constructor with
`E3018: Type 'Other' has no field 'b'` at `:15`, inside the declaration the alias is not even in scope for.

<!-- test: inner-alias-shadowing-another-type-wins-at-the-static-door -->
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Other
	export var b as ExitCode

	export static function create(v ExitCode) returns Self
		return Self{b: v}
	end 'create'
end 'Other'

type Plain
	typealias Other = Pair with ExitCode

	static function make() returns ExitCode
		let p = Other.create(9)
		return p.a
	end 'make'
end 'Plain'

function main() returns ExitCode
	return Plain.make()
end 'main'
```
```exitcode
9
```

### ⚠ THE FALSE-ACCEPT GUARD: an inner alias's static must NOT outrank the enclosing type's own name

`namesInnerAliasHere` is the one predicate that keeps the three doors agreeing, and this is the case that
holds the static door to it: `type Holder` declaring `typealias Holder = Pair with ExitCode` means the TYPE
at `Holder.create()`, so the call reaches `Holder`'s own static and not `Pair`'s. Its literal twin is
`an-inner-alias-shadowing-its-own-type` one heading up.

<!-- test: an-inner-alias-shadowing-its-own-types-static -->
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Holder
	typealias Holder = Pair with ExitCode

	export var seed as ExitCode

	export static function create() returns Holder
		return Self{seed: 4}
	end 'create'

	export function seedValue() returns ExitCode
		return self.seed
	end 'seedValue'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.seedValue()
end 'main'
```
```exitcode
4
```

<!-- test: an-inner-alias-shadowing-its-own-type-loses-IN-BODY-too -->
The companion above calls from `main`, where `enclosingInnerAliases` is EMPTY — so the inner-alias door
cannot fire there and the case cannot distinguish "the type won" from "the alias was never consulted".
This one calls from INSIDE `Holder`'s own body, where the alias IS in scope, and is built so the two
readings disagree LOUDLY: the enclosing type's `create()` takes no arguments while the alias's
(`Pair.create`) takes one, so if the alias won this program would be an arity error rather than a
different number. It answers 4, so the enclosing TYPE wins at both call sites and the precedence is one
rule rather than two. (No oracle spelling: the bootstrap cannot compile a type that shadows its own name
— measured, it fails this program.)
```maxon
typealias ExitCode = int(0 to 125)

type Pair uses T
	export var a as T

	export static function create(a T) returns Self
		return Self{a: a}
	end 'create'
end 'Pair'

type Holder
	typealias Holder = Pair with ExitCode

	export var seed as ExitCode

	export static function create() returns Holder
		return Self{seed: 4}
	end 'create'

	export static function probe() returns ExitCode
		let h = Holder.create()
		return h.seed
	end 'probe'
end 'Holder'

function main() returns ExitCode
	return Holder.probe()
end 'main'
```
```exitcode
4
```

### A RANGED inner alias has nothing to construct

`{}` on an inner alias means the alias's empty CONTAINER, and a number is not one. It is refused by what it
is rather than by E3076, whose cure (`Idx.create(…)`) does not exist for a numeric alias. (Both reference
compilers answer this program with an internal crash — the bootstrap with an unhandled
`IrRangedPrimitiveType`→`IrStructType` cast reported as `E9001` — so there is no oracle spelling to match
and the message names the cure instead.)

<!-- test: ranged-inner-alias-has-nothing-to-construct -->
```maxon
typealias ExitCode = int(0 to 125)

type Plain
	typealias Idx = int(0 to 10)

	static function make() returns Idx
		let n = Idx{}
		return n
	end 'make'
end 'Plain'

function main() returns ExitCode
	let c = Plain.make()
	if c == 0 'ok'
		return 7
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:11: Unsupported: a struct literal naming `Idx`, a ranged numeric typealias declared by `Plain` — a number declares no fields to write, so write the number itself. `{}` on an inner alias builds the EMPTY CONTAINER a container alias denotes, and a numeric alias denotes no container
```

### A written field is refused — the literal is the EMPTY container

`Nums{}` is exactly `Nums.create()`, and a builtin container has no user-writable fields, so a field written
between the braces is refused where it stands rather than checked against a layout that does not exist.

<!-- test: written-field-on-a-container-alias-is-refused -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Plain
	typealias Nums = Array with ExitCode

	static function make() returns Count
		var n = Nums{count: 3}
		return n.count()
	end 'make'
end 'Plain'

function main() returns ExitCode
	let c = Plain.make()
	if c == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:16: Unsupported: a field written inside `Nums{…}` — the literal builds the EMPTY container `Nums.create()` builds, and a builtin container has no user-writable fields; fill it after creating it
```

### An opaque inner array alias inside a CLOSURE is refused, not a panic

An `Array with <type parameter>` reads its element destructor from the enclosing instance's layout
descriptor at run time, and only a generic type's own METHOD reserves the parameter that carries it — a
closure literal is lifted to a top-level function whose uniform `(userargs, env)` ABI has no slot for one.
Both spellings used to abort the compiler here (`appendOpaqueArrayCreate`'s panic, naming
`Holder.sized$closure_0`, with no source position at all); the literal is pinned first and its `create()`
twin next, because the refusal lives in the ONE producer they share.

<!-- test: opaque-inner-alias-in-a-closure-is-refused -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function sized() returns Count
		let make = function() gives ElementArray{}.count()
		return make()
	end 'sized'
end 'Holder'

typealias IntHolder = Holder with ExitCode

function main() returns ExitCode
	var h = IntHolder.create()
	let c = h.sized()
	if c == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:31: Unsupported: constructing an opaque type-parameter `Array` inside a CLOSURE literal — the create reads its element destructor from the enclosing instance's layout descriptor at run time, and only a generic type's own METHOD reserves the parameter that carries it: a closure is lifted to a top-level function whose uniform `(userargs, env)` ABI has no slot for one. Build the array in the method and capture it, or move the construction out of the closure. Threading a layout descriptor into a lifted closure is a later slice
```

### The `create()` spelling in a closure is refused identically

<!-- test: opaque-inner-alias-create-in-a-closure-is-refused -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Holder uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function sized() returns Count
		let make = function() gives ElementArray.create().count()
		return make()
	end 'sized'
end 'Holder'

typealias IntHolder = Holder with ExitCode

function main() returns ExitCode
	var h = IntHolder.create()
	let c = h.sized()
	if c == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:31: Unsupported: constructing an opaque type-parameter `Array` inside a CLOSURE literal — the create reads its element destructor from the enclosing instance's layout descriptor at run time, and only a generic type's own METHOD reserves the parameter that carries it: a closure is lifted to a top-level function whose uniform `(userargs, env)` ABI has no slot for one. Build the array in the method and capture it, or move the construction out of the closure. Threading a layout descriptor into a lifted closure is a later slice
```

### An `extension` body's inner alias builds its container, keyed under the CONFORMER

⭐ **W3 BUILT THE MECHANISM THIS CASE USED TO RECORD AS ABSENT, AND THE CASE MOVED FROM A REFUSAL TO AN
ANSWER.** It read: *"an `extension`'s members are consumed whole by the declaration sweep, so a `typealias`
declared in one is never keyed whole-program … the oracle compiles this program; shv2 refuses it by name."*
The sweep now reads an extension body's nested typealiases and keys each one under the CONFORMER it is
expanded onto (`Parser.foldExtensionDeclarationInto`), so `TagArray` inside `extension Tagged` is
`Box.TagArray` and the literal has an interned instance to build. Both compilers answer **6**.

⚠ It is the same measurement in the other direction: the refusal was pinned because shv2 diverged from the
runnable oracle, and the pin is what makes the divergence's END observable. `stdlib/Interfaces.maxon`'s
`extension Iterable` is the program that forced it — `map`/`filter` open with `var result = ElementArray{}`.

<!-- test: extension-body-inner-alias-builds-the-container -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

interface Tagged
	function tag() returns ExitCode
end 'Tagged'

export extension Tagged
	typealias TagArray = Array with ExitCode

	export function tags() returns Count
		var result = TagArray{}
		result.push(self.tag())
		return result.count()
	end 'tags'
end 'Tagged'

type Box implements Tagged
	export var v as ExitCode

	export static function create() returns Self
		return Self{v: 2}
	end 'create'

	export function tag() returns ExitCode
		return self.v
	end 'tag'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	let c = b.tags()
	if c == 1 'ok'
		return 6
	end 'ok'
	return 1
end 'main'
```
```exitcode
6
```
