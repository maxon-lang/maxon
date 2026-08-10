---
feature: per-instance-typealias
status: experimental
keywords: [typealias, generics, type-safety, nominal-types, per-instance]
category: type-system
---

# Per-Instance Ranged Typealiases in Generics

## Documentation

### Overview

When a ranged typealias is declared inside a generic type, each concrete instantiation gets a nominally distinct copy. This prevents accidentally mixing values from different instances (e.g., passing an index from one pool to a different pool).

### Syntax

Declare a ranged typealias inside a generic type body:

```text
type Container uses T
	export typealias Idx = int(0 to u64.max)

	export function push(item T) returns Idx
		// ...
	end 'push'

	export function get(index Idx) returns T
		// ...
	end 'get'
end 'Container'
```

When instantiated:

```text
typealias FooContainer = Container with Foo
typealias BarContainer = Container with Bar

// FooContainer.Idx and BarContainer.Idx are distinct types
var fooIdx = fooContainer.push(myFoo)   // returns FooContainer.Idx
fooContainer.get(fooIdx)                // OK
barContainer.get(fooIdx)                // ERROR: type mismatch
```

### Explicit Conversion

Use `as` to convert between compatible per-instance aliases (same base type and range):

```text
var barIdx = fooIdx as BarContainer.Idx
barContainer.get(barIdx)  // OK after explicit conversion
```

### Construction

Cast a value into the per-instance type with `as`:

```text
var idx = 0 as FooContainer.Idx
```

## Tests

### Basic per-instance typealias: return type is tracked

<!-- test: basic-return-type -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T) returns Self
		return Self{value: value, tag: 0}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function withTag(t Idx) returns Self
		return Self{value: self.value, tag: t}
	end 'withTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	var w = IntWrapper.create(42)
	w = w.withTag(7)
	let t = w.getTag()
	if t == 7 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Wrong instance tag is rejected

<!-- test: wrong-instance-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WrapperA = Wrapper with Integer
typealias WrapperB = Wrapper with Integer

function main() returns ExitCode
	let a = WrapperA.create(1, tag: 5)
	let b = WrapperB.create(2, tag: 0)
	let aTag = a.getTag()
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/per-instance-typealias/wrong-instance-error.test:30:4: argument type mismatch for 't': expected 'WrapperB.Idx', got 'WrapperA.Idx'
```

### Literal in range is accepted

<!-- test: literal-accepted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	let w = IntWrapper.create(42, tag: 5)
	let t = w.getTag()
	if t == 5 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Explicit conversion with 'as'

<!-- test: as-conversion -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer
typealias StrWrapper = Wrapper with String

function takeStrTag(t StrWrapper.Idx) returns StrWrapper.Idx
	return t
end 'takeStrTag'

function main() returns ExitCode
	let iw = IntWrapper.create(1, tag: 7)
	let intTag = iw.tag
	let strTag = intTag as StrWrapper.Idx
	let result = takeStrTag(strTag)
	if result == 7 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Dot-syntax construction

<!-- test: dot-construction -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IntWrapper = Wrapper with Integer

function main() returns ExitCode
	let w = IntWrapper.create(99, tag: 42)
	let t = w.getTag()
	if t == 42 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Cast preserves the source's identity

An `as` between per-instance aliases is a CONVERSION: it yields a distinct value of the target
type and leaves the SOURCE unchanged. So after `let bTag = aTag as WB.Idx`, `aTag` is still
`WA.Idx` and remains usable everywhere `WA.Idx` is expected — the cast did not mutate it.

<!-- test: cast-preserves-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WA = Wrapper with Integer
typealias WB = Wrapper with Integer

function main() returns ExitCode
	let a = WA.create(1, tag: 5)
	let aTag = a.getTag()
	let bTag = aTag as WB.Idx
	var a2 = WA.create(9, tag: 0)
	a2.setTag(aTag)
	let check = a2.getTag()
	if bTag == 5 'converted'
		if check == 5 'preserved'
			return 0
		end 'preserved'
	end 'converted'
	return 1
end 'main'
```
```exitcode
0
```

### Cast does not launder the source's instance

The `as` produces `bTag` (a `WB.Idx`), but `aTag` is STILL a `WA.Idx` — the cast is not an in-place
retag. So `bTag` is accepted where `WB.Idx` is expected, while the SOURCE `aTag` passed to the same
slot is the genuine cross-instance mismatch.

<!-- test: error.cast-does-not-launder-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'

	export function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WA = Wrapper with Integer
typealias WB = Wrapper with Integer

function main() returns ExitCode
	let a = WA.create(1, tag: 5)
	let aTag = a.getTag()
	let bTag = aTag as WB.Idx
	var b = WB.create(2, tag: 0)
	b.setTag(bTag)
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/per-instance-typealias/error.cast-does-not-launder-source.test:32:4: argument type mismatch for 't': expected 'WB.Idx', got 'WA.Idx'
```

### Per-instance Idx decays to plain int on return

A per-instance `Idx` is a nominal wrapper over a SCALAR int, so it DECAYS to plain int wherever a
non-per-instance numeric is expected — a `return` included. `getTag()` returns `IW.Idx`, and returning
it from an `ExitCode` function is accepted (no narrowing: the range fits), yielding the value.

<!-- test: return-decays-to-plain -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to 200)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IW = Wrapper with Integer

function main() returns ExitCode
	let w = IW.create(1, tag: 42)
	return w.getTag()
end 'main'
```
```exitcode
42
```

### Per-instance Idx decays when reassigned into a plain int var

The decay is not special to `return` — a per-instance `Idx` assigned into a plain `int` variable decays
just the same, as it does when passed to a plain-int parameter.

<!-- test: reassign-decays-to-plain -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IW = Wrapper with Integer

function main() returns ExitCode
	let w = IW.create(1, tag: 7)
	var n = 0
	n = w.getTag()
	if n == 7 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

## A nested typealias is a MEMBER of its type, and every `extension` over that type may spell it

A nested `typealias` is keyed `<Type>.<member>` (`Parser.qualifiedInnerGenericAlias`), which is a
statement about WHOSE member it is and not about which file it was written in. So an `extension Foo`
in one file may spell a member `type Foo` — or another `extension Foo` — declared somewhere else,
exactly as it may call a method declared somewhere else.

`stdlib/helpers/sort/` is the case this exists for. Six files each write `export extension Array`;
`insertionSort.maxon:14-15` declares `SortIndex` and `SortComparator`, `mergeSort.maxon:25` declares
`MergeScratchArray`, and the other five spell all three BARE from their own bodies.

⛔ **BOTH REFERENCE COMPILERS ANSWER THIS BY MAKING THE NAME FLAT AND PROGRAM-WIDE, AND shv2
DELIBERATELY DOES NOT.** The bootstrap writes an extension's `typealias` into `module.TypeDefs` under
its bare name (`0-Compiler.cs:1199`), keeping an `OwnerTypeName` it never puts in any key; v1 stores it
on `Array.innerAliases` but keeps three bare-name global fallbacks over it, one of whose comments
records the assumption outright — *"inner-alias names are unique across the stdlib … no ambiguity"*.
Neither can diagnose two types declaring one member name. Here the widening is the ENCLOSING TYPE's
own members and nothing else: a member of `Holder` reaches an `extension Holder` in any file, and it
reaches nothing else anywhere. The third case below is the half that says so.

⚠ **THE TYPE'S OWN BODY IS NOT GIVEN THIS.** A `type` body's signatures are read during the per-file
declaration sweep, which runs before any `extension` has been folded, so granting it would make the
sweep and the real parse record one written name under two spellings. An `extension` body is read only
by passes that both run after every extension's members are known
(`Queries.foldExtensionDeclarations` makes two, and the first one exists for this).

### A ranged member declared in another file's extension body

<!-- test: cross-file-extension-declares-the-ranged-member -->
`a.maxon`'s `extension Holder` declares `Idx`; `b.maxon`'s `extension Holder` names it bare in a
parameter. Before this rule the second file answered `E3011 Unknown type 'Idx'`.
```maxon
// --- file: a.maxon
typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

extension Holder
	typealias Idx = int(0 to 100)

	export function fromA(i Idx) returns Num
		return self.v + i
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Holder
	export function fromB(i Idx) returns Num
		return self.v + i
	end 'fromB'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(1)
	let total = h.fromA(3) + h.fromB(30)
	if total == 35 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### A generic-instance member declared in another file's extension body

<!-- test: cross-file-extension-declares-the-generic-instance-member -->
`MergeScratchArray = Array with Element` is `stdlib/helpers/sort/mergeSort.maxon:25`'s shape, spelled
from five sibling files. Here `Bag = Array with Num` is declared in `a.maxon`'s extension body and
named from `b.maxon`'s — a different registry from the ranged case above
(`genericAliases`, not `innerAliases`), which is why it is its own case.
```maxon
// --- file: a.maxon
typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

extension Holder
	typealias Bag = Array with Num

	export function fill() returns Bag
		var b = Bag.create()
		b.push(self.v)
		b.push(self.v)
		return b
	end 'fill'
end 'Holder'

// --- file: b.maxon
extension Holder
	export function firstOf(b Bag) returns Num
		return try b.get(0) otherwise 0
	end 'firstOf'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(20)
	let b = h.fill()
	if h.firstOf(b) == 20 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### The member is the ENCLOSING TYPE's, and an extension over ANOTHER type does not see it

<!-- test: error.a-nested-member-does-not-leak-to-another-types-extension -->
The whole point of the `<Type>.<member>` key, and the control the widening above owes: `Idx` is a
member of `Holder`, so an `extension Other` naming it bare is still refused. Under either reference
compiler's flat bare-name table this program compiles.
```maxon
// --- file: a.maxon
typealias Num = int(0 to 200)

export type Holder
	export var v as Num
end 'Holder'

export type Other
	export var w as Num
end 'Other'

extension Holder
	typealias Idx = int(0 to 100)

	export function fromA(i Idx) returns Num
		return self.v + i
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Other
	export function fromB(i Idx) returns Num
		return self.w + i
	end 'fromB'
end 'Other'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: Unknown type 'Idx'
```

### A member declared in the TYPE's own body is spelled from an extension in another file

<!-- test: extension-spells-a-member-the-type-body-declares -->
The other direction of one rule: `stdlib/Map.maxon` declares `typealias Entry` on its first body line,
and an `extension Map` elsewhere may name it. This direction is order-independent for a different
reason — a `type` body's nested aliases are folded with their own file, before any extension is read —
and it is pinned so the two directions cannot come apart.
```maxon
// --- file: a.maxon
typealias Num = int(0 to 200)

export type Holder
	typealias Idx = int(0 to 100)

	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

// --- file: b.maxon
extension Holder
	export function fromB(i Idx) returns Num
		return self.v + i
	end 'fromB'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(4)
	if h.fromB(3) == 7 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

## An extension body's inner alias is PER-INSTANCE too

Everything above is about which bodies may SPELL a member. This is the other half: what the name then
MEANS. A nested `typealias` of a generic type is per-instance — `WrapperA.Idx` and `WrapperB.Idx` are
distinct types, which is what `wrong-instance-error` pins for a member declared in the `type` body.

⚠ **AN `extension` BODY'S MEMBER WAS NOT, AND THAT WAS TRUE OF THE SAME-FILE FORM BEFORE IT WAS TRUE OF
THE CROSS-FILE ONE.** The per-instance argument check reads `ProgramSignatures.methodInnerAliasParams`,
recorded by `recordScannedSignature` — which asked the BODY WALK's live alias set both for whether to
look at all and for whether a given parameter names a member. The same-file case therefore recorded
nothing whenever the method sat in an `extension` body other than the one that declared the alias, and
the cross-file case recorded nothing at all. Both doors now ask `namesInnerAliasHere`, the one home of
"does this written name denote an inner alias here", so the check and the DENOTATION cannot disagree.

<!-- test: error.an-extension-bodys-inner-alias-is-per-instance -->
The alias and its user are in ONE file, in two `extension` bodies. This program compiled and returned
1 before the two doors were made one.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export type Wrapper uses T
	export var value as T
	export var tag as Integer

	export static function create(value T, tag Integer) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

extension Wrapper
	typealias Idx = int(0 to 100)

	export function getTag() returns Idx
		return 1
	end 'getTag'
end 'Wrapper'

extension Wrapper
	export function useTag(t Idx) returns Integer
		return t
	end 'useTag'
end 'Wrapper'

// --- file: main.maxon
typealias Num = int(0 to 200)
typealias WA = Wrapper with Integer
typealias WB = Wrapper with Num

function main() returns ExitCode
	let a = WA.create(1, tag: 1)
	let b = WB.create(2, tag: 2)
	let fromA = a.getTag()
	return b.useTag(fromA) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:37:11: argument type mismatch for 't': expected 'WB.Idx', got 'WA.Idx'
```

<!-- test: error.a-cross-file-extension-inner-alias-is-per-instance -->
The same program with the second `extension Wrapper` moved to its own file — the shape
`stdlib/helpers/sort/` has six times over. It is the construct the cross-file rule above admits, so it
owes the same refusal: admitting the SPELLING must not quietly admit the wrong instance's value with it.
```maxon
// --- file: a.maxon
typealias Integer = int(i64.min to i64.max)

export type Wrapper uses T
	export var value as T
	export var tag as Integer

	export static function create(value T, tag Integer) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

extension Wrapper
	typealias Idx = int(0 to 100)

	export function getTag() returns Idx
		return 1
	end 'getTag'
end 'Wrapper'

// --- file: b.maxon
extension Wrapper
	export function useTag(t Idx) returns Integer
		return t
	end 'useTag'
end 'Wrapper'

// --- file: main.maxon
typealias Num = int(0 to 200)
typealias WA = Wrapper with Integer
typealias WB = Wrapper with Num

function main() returns ExitCode
	let a = WA.create(1, tag: 1)
	let b = WB.create(2, tag: 2)
	let fromA = a.getTag()
	return b.useTag(fromA) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:38:11: argument type mismatch for 't': expected 'WB.Idx', got 'WA.Idx'
```
