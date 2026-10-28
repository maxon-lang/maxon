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

	static function create(value T) returns Self
		return Self{value: value, tag: 0}
	end 'create'

	function getTag() returns Idx
		return self.tag
	end 'getTag'

	function withTag(t Idx) returns Self
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

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
		return self.tag
	end 'getTag'

	function setTag(t Idx)
		self.tag = t
	end 'setTag'
end 'Wrapper'

typealias WrapperA = Wrapper with Integer
typealias WrapperB = Wrapper with Integer

function main() returns ExitCode
	let a = WrapperA.create(1, tag: 5)
	var b = WrapperB.create(2, tag: 0)
	let aTag = a.getTag()
	b.setTag(aTag)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/per-instance-typealias/wrong-instance-error.maxon:30:4: argument type mismatch for 't': expected 'WrapperB.Idx', got 'WrapperA.Idx'
```

### Literal in range is accepted

<!-- test: literal-accepted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
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

	static function create(value T, tag Idx) returns Self
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

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
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

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
		return self.tag
	end 'getTag'

	function setTag(t Idx)
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

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
		return self.tag
	end 'getTag'

	function setTag(t Idx)
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
error E3005: specs/per-instance-typealias/error.cast-does-not-launder-source.maxon:32:4: argument type mismatch for 't': expected 'WB.Idx', got 'WA.Idx'
```

### Per-instance Idx decays to plain int on return

A per-instance `Idx` is a nominal wrapper over a SCALAR int. `getTag()` returns `IW.Idx`, and `ExitCode` is
its own type, so returning it from an `ExitCode` function takes the one door between two names: `as`.
The cast is guard-free — the range fits — and yields the value.

<!-- test: return-decays-to-plain -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to 200)

	export var value as T
	export var tag as Idx

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
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

### A per-instance Idx does not take another alias's var

A per-instance `Idx` keeps its own identity at a store, as it does at a return and at a call: it does not
decay into a variable of another alias.

<!-- test: error.reassign-is-refused-by-the-vars-own-alias -->
Every `var` names its alias, so an instance's member alias does not decay into a plain binding: an
`IW.Idx` stored into an `Integer` var is refused at the store.
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

typealias IW = Wrapper with Integer

function main() returns ExitCode
	let w = IW.create(1, tag: 7)
	var n = 0 as Integer
	n = w.getTag()
	if n == 7 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:2: cannot assign a value of type 'IW.Idx' to variable 'n', which holds 'Integer'
```

## A nested typealias is a MEMBER of its type, and every `extension` over that type may spell it

A nested `typealias` is keyed `<Type>.<member>` (`Parser.qualifiedInnerGenericAlias`), which is a
statement about WHOSE member it is and not about which file it was written in. So an `extension Foo`
in one file may spell a member `type Foo` — or another `extension Foo` — declared somewhere else,
exactly as it may call a method declared somewhere else.

`stdlib/helpers/sort/` is the case this exists for. Six files each write `public extension Array`;
`insertionSort.maxon` declares `SortComparator` and `mergeSort.maxon` declares `MergeScratchArray`, and the
others spell BARE from their own bodies whichever of the two they need, without regard to which file
wrote the declaration.

⛔ **A FLAT, PROGRAM-WIDE NAME CANNOT DIAGNOSE TWO TYPES DECLARING ONE MEMBER NAME, AND the compiler
DELIBERATELY DOES NOT USE ONE.** Here the widening is the ENCLOSING TYPE's
own members and nothing else: a member of `Holder` reaches an `extension Holder` in any file, and it
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
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

extension Holder
	export typealias Idx = int(0 to 100)

	export function fromA(i Idx) returns Num
		return self.v + (i as Num)
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Holder
	export function fromB(i Idx) returns Num
		return self.v + (i as Num)
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
`MergeScratchArray = Array with Element` is `stdlib/helpers/sort/mergeSort.maxon:8`'s shape, spelled
from sibling files. Here `Bag = Array with Num` is declared in `a.maxon`'s extension body and
named from `b.maxon`'s — a different registry from the ranged case above
(`genericAliases`, not `innerAliases`), which is why it is its own case.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

extension Holder
	export typealias Bag = Array with Num

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
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num
end 'Holder'

export type Other
	export var w as Num
end 'Other'

extension Holder
	export typealias Idx = int(0 to 100)

	export function fromA(i Idx) returns Num
		return self.v + (i as Num)
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Other
	export function fromB(i Idx) returns Num
		return self.w + (i as Num)
	end 'fromB'
end 'Other'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:23:26: Unknown type 'Idx'
```

### A member declared in the TYPE's own body is spelled from an extension in another file

<!-- test: extension-spells-a-member-the-type-body-declares -->
The other direction of one rule: `stdlib/Map.maxon` declares `typealias Entry` on its first body line,
and an `extension Map` elsewhere may name it. This direction is order-independent for a different
reason — a `type` body's nested aliases are folded with their own file, before any extension is read —
and it is pinned so the two directions cannot come apart.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type Holder
	export typealias Idx = int(0 to 100)

	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

// --- file: b.maxon
extension Holder
	export function fromB(i Idx) returns Num
		return self.v + (i as Num)
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

⚠ **AN `extension` BODY'S MEMBER IS CHECKED THE SAME WAY, IN THE SAME FILE AND ACROSS FILES.** The
per-instance argument check reads `ProgramSignatures.methodInnerAliasParams`, recorded by
`recordScannedSignature`. Asking the BODY WALK's live alias set whether to look at all and whether a given
parameter names a member would record nothing for a method in an `extension` body other than the one
that declared the alias, and nothing at all across files. Both doors ask `namesInnerAliasHere`, the one
home of "does this written name denote an inner alias here", so the check and the DENOTATION cannot
disagree.

<!-- test: error.an-extension-bodys-inner-alias-is-per-instance -->
The alias and its user are in ONE file, in two `extension` bodies.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export type Wrapper uses T
	export var value as T
	export var tag as Integer

	export static function create(value T, tag Integer) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

extension Wrapper
	export typealias Idx = int(0 to 100)

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
export typealias Integer = int(i64.min to i64.max)

export type Wrapper uses T
	export var value as T
	export var tag as Integer

	export static function create(value T, tag Integer) returns Self
		return Self{value: value, tag: tag}
	end 'create'
end 'Wrapper'

extension Wrapper
	export typealias Idx = int(0 to 100)

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

## A type has ONE member of each name, however many `extension` bodies write it

An `extension` body's nested `typealias` is a member of every type the extension reaches, keyed
`<Type>.<member>` for the whole program. A second `extension` declaring the same member for the same type
therefore declares a second meaning for ONE key, and nothing could tell the two apart at a reader: the
fold keeps whichever site it reached last, and every signature built from the other site's spelling
silently means the survivor's instance. Two declarations of one name are ambiguous, whether the instances
differ or not, so the later one is refused at its name. The earlier one is the one whose file sorts first,
byte by byte, and then the one written first in the file.

### Two extensions in two files declare different instances of one member

<!-- test: error.two-extensions-declare-different-instances-of-one-member -->
`Box implements Seq`, and `a.maxon`'s and `b.maxon`'s `extension Seq` each declare `Items` — an `Array`
and a `List`. `Box.Items` has no single meaning, and a reader of it was handed whichever site folded last.
```maxon
// --- file: seq.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: box.maxon
export type Box implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

// --- file: a.maxon
export extension Seq
	export typealias Items = Array with Item

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Seq'

// --- file: b.maxon
export extension Seq
	export typealias Items = List with Item

	export function listed() returns Items
		return Items.create()
	end 'listed'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	let all = box.all()
	let listed = box.listed()
	return (all.count() + listed.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:33:19: Duplicate typealias 'Items': `Box.Items` is already declared by an `extension` in a.maxon
```

### Two extensions in two files declare the SAME instance of one member

<!-- test: error.two-extensions-declare-the-same-instance-of-one-member -->
The same program with both sites declaring `Array with Item`. The two spellings denote one instance, but
each declaration is its own, and a type has one member of each name: the duplicate is refused exactly as
a different instance is.
```maxon
// --- file: seq.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: box.maxon
export type Box implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

// --- file: a.maxon
export extension Seq
	export typealias Items = Array with Item

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Seq'

// --- file: b.maxon
export extension Seq
	export typealias Items = Array with Item

	export function again() returns Items
		return Items.create()
	end 'again'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	let all = box.all()
	let again = box.again()
	return (all.count() + again.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:33:19: Duplicate typealias 'Items': `Box.Items` is already declared by an `extension` in a.maxon
```

### Two extensions in ONE file declare one member

<!-- test: error.two-extensions-in-one-file-declare-one-member -->
The refusal does not depend on the sites being in different files: the later `extension Seq` is the
duplicate, and the message names the file the earlier one is in, which is this one.
```maxon
// --- file: seq.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: box.maxon
export type Box implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

// --- file: ext.maxon
export extension Seq
	export typealias Items = Array with Item

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Seq'

export extension Seq
	export typealias Items = List with Item

	export function listed() returns Items
		return Items.create()
	end 'listed'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	let all = box.all()
	let listed = box.listed()
	return (all.count() + listed.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:32:19: Duplicate typealias 'Items': `Box.Items` is already declared by an `extension` in ext.maxon
```

### One extension body declares a member twice

<!-- test: error.one-extension-body-declares-a-member-twice -->
```maxon
// --- file: seq.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: box.maxon
export type Box implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

// --- file: ext.maxon
export extension Seq
	export typealias Items = Array with Item
	export typealias Items = List with Item

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	let all = box.all()
	return all.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:25:19: Duplicate typealias 'Items': `Box.Items` is already declared by an `extension` in ext.maxon
```

### The refusal covers the ranged, function and tuple forms too

<!-- test: error.two-extensions-declare-a-ranged-member -->
A ranged `typealias Idx` is a different registry from the generic-instance forms above, and a
type extension (`extension Holder`) reaches one type rather than every conformer of an interface.
```maxon
// --- file: holder.maxon
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

// --- file: a.maxon
extension Holder
	export typealias Idx = int(0 to 100)

	export function fromA(i Idx) returns Num
		return self.v + (i as Num)
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Holder
	export typealias Idx = int(0 to 50)

	export function fromB(i Idx) returns Num
		return self.v + (i as Num)
	end 'fromB'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(1)
	return (h.fromA(3) + h.fromB(30)) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:24:19: Duplicate typealias 'Idx': `Holder.Idx` is already declared by an `extension` in a.maxon
```

<!-- test: error.two-extensions-declare-a-function-member -->
```maxon
// --- file: holder.maxon
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

// --- file: a.maxon
extension Holder
	export typealias Step = function(Num) returns Num

	export function fromA(step Step) returns Num
		return step(self.v)
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Holder
	export typealias Step = function(Num, Num) returns Num

	export function fromB(step Step) returns Num
		return step(self.v, 1)
	end 'fromB'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(1)
	return h.fromA(function(n) gives n) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:24:19: Duplicate typealias 'Step': `Holder.Step` is already declared by an `extension` in a.maxon
```

<!-- test: error.two-extensions-declare-a-tuple-member -->
```maxon
// --- file: holder.maxon
export typealias Num = int(0 to 200)

export type Holder
	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'
end 'Holder'

// --- file: a.maxon
extension Holder
	export typealias Pair = (Num, Num)

	export function fromA() returns Pair
		return (self.v, self.v)
	end 'fromA'
end 'Holder'

// --- file: b.maxon
extension Holder
	export typealias Pair = (Num, String)

	export function fromB() returns Pair
		return (self.v, "b")
	end 'fromB'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let h = Holder.create(1)
	let pair = h.fromA()
	return pair.0 as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:24:19: Duplicate typealias 'Pair': `Holder.Pair` is already declared by an `extension` in a.maxon
```

### What the refusal leaves alone

<!-- test: extensions-declare-members-of-different-names-and-conformers-keep-their-own -->
Two `extension Seq` bodies declaring DIFFERENT members, and two conformers each receiving the same member
through ONE extension: the key is `<Type>.<member>`, so neither is a duplicate.
```maxon
// --- file: seq.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: box.maxon
export type Box implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

export type Crate implements Seq with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Crate'

// --- file: a.maxon
export extension Seq
	export typealias Items = Array with Item

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Seq'

// --- file: b.maxon
export extension Seq
	export typealias Chain = List with Item

	export function chained() returns Chain
		return Chain.create()
	end 'chained'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	let crate = Crate.create(4)
	var all = box.all()
	all.push(box.first())
	var more = crate.all()
	more.push(crate.first())
	var chain = crate.chained()
	chain.append(crate.first())
	return (all.count() + more.count() + chain.count()) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: conditional-extensions-withheld-from-a-conformer-declare-one-member -->
Two CONDITIONAL extensions declaring `Items`, each withheld from the conformer the other reaches: a
withheld extension gives its conformer no members, so `Flagged.Items` and `Ranked.Items` each have ONE
declaration.
```maxon
// --- file: seq.maxon
export interface Seq uses Item
	function first() returns Item
end 'Seq'

// --- file: items.maxon
export enum Flag
	up
	down
end 'Flag'

export type Rank implements Comparable
	export var level as Level

	export static function create(level Level) returns Self
		return Self{level: level}
	end 'create'

	export function compare(other Rank) returns Ordering
		return self.level.compare(other.level)
	end 'compare'
end 'Rank'

export typealias Level = int(0 to 9)

export type Flagged implements Seq with Flag
	export var flag as Flag

	export static function create(flag Flag) returns Self
		return Self{flag: flag}
	end 'create'

	export function first() returns Flag
		return self.flag
	end 'first'
end 'Flagged'

export type Ranked implements Seq with Rank
	export var rank as Rank

	export static function create(rank Rank) returns Self
		return Self{rank: rank}
	end 'create'

	export function first() returns Rank
		return self.rank
	end 'first'
end 'Ranked'

// --- file: a.maxon
export extension Seq where Item is Hashable
	export typealias Items = Array with Item

	export function hashedItems() returns Items
		return Items.create()
	end 'hashedItems'
end 'Seq'

// --- file: b.maxon
export extension Seq where Item is Comparable
	export typealias Items = List with Item

	export function rankedItems() returns Items
		return Items.create()
	end 'rankedItems'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let flagged = Flagged.create(Flag.up)
	let ranked = Ranked.create(Rank.create(2))
	var hashed = flagged.hashedItems()
	hashed.push(flagged.first())
	var listed = ranked.rankedItems()
	listed.append(ranked.first())
	return (hashed.count() + listed.count()) as ExitCode
end 'main'
```
```exitcode
2
```

## A body declares each nested typealias ONCE

A `type` or `interface` body is one member list, so a nested `typealias` name written twice in it is a
duplicate whatever the two declarations denote: the compiler would otherwise keep whichever it read last and
give the first declaration's own signatures that instance without a word. The later declaration is
[E3061](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3061) at its name, and the rule covers the generic-instance,
ranged, function and tuple forms alike.

### One type body declares a member twice

<!-- test: error.a-type-body-declares-a-generic-member-twice -->
```maxon
typealias Digit = int(0 to 9)

type Box
	typealias Items = List with Digit
	typealias Items = Array with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function items() returns Items
		return Items.create()
	end 'items'
end 'Box'

function main() returns ExitCode
	let box = Box.create(3)
	var xs = box.items()
	xs.push(box.n)
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:6:12: Duplicate typealias 'Items': `Box.Items` is already declared earlier in the body of `Box`
```

<!-- test: error.a-type-body-declares-a-ranged-member-twice -->
```maxon
type Box
	typealias Idx = int(0 to 9)
	typealias Idx = int(0 to 99)
	export var n as Idx

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	return box.n as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:4:12: Duplicate typealias 'Idx': `Box.Idx` is already declared earlier in the body of `Box`
```

<!-- test: error.a-type-body-declares-a-function-member-twice -->
```maxon
typealias Number = int(0 to 9)

type Box
	typealias Op = function(Number) returns Number
	typealias Op = function(Number, Number) returns Number
	export var n as Number

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function apply(op Op) returns Number
		return op(self.n, 2)
	end 'apply'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	return box.apply(function(a, b) gives a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:6:12: Duplicate typealias 'Op': `Box.Op` is already declared earlier in the body of `Box`
```

<!-- test: error.a-type-body-declares-a-tuple-member-twice -->
```maxon
typealias Number = int(0 to 9)

type Box
	typealias Pair = (Number, Number)
	typealias Pair = (Number, Number, Number)
	export var n as Number

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function pair() returns Pair
		return (self.n, self.n, self.n)
	end 'pair'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	let pair = box.pair()
	return pair.0 as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:6:12: Duplicate typealias 'Pair': `Box.Pair` is already declared earlier in the body of `Box`
```

<!-- test: error.an-interface-body-declares-a-member-twice -->
```maxon
typealias Digit = int(0 to 9)

interface Seq uses Item
	typealias Items = Array with Item
	typealias Items = List with Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Digit
		return self.n
	end 'first'
end 'Box'

function main() returns ExitCode
	let box = Box.create(3)
	return box.first() as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:6:12: Duplicate typealias 'Items': `Seq.Items` is already declared earlier in the body of `Seq`
```

### What the refusal leaves alone

<!-- test: two-types-each-declare-a-member-of-one-name -->
The key is `<Type>.<member>`, so two types declaring `Items` once each are not duplicates of each other,
and neither is a type body beside an `extension` of another type declaring the same name.
```maxon
typealias Digit = int(0 to 9)

type Box
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function items() returns Items
		return Items.create()
	end 'items'
end 'Box'

type Crate
	typealias Items = Array with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function items() returns Items
		return Items.create()
	end 'items'
end 'Crate'

function main() returns ExitCode
	let box = Box.create(3)
	let crate = Crate.create(4)
	var listed = box.items()
	listed.prepend(box.n)
	var arrayed = crate.items()
	arrayed.push(crate.n)
	return (listed.count() + arrayed.count()) as ExitCode
end 'main'
```
```exitcode
2
```

## A type's own nested typealias and an extension's of the same name follow the method rule

A nested typealias is a member of its type exactly as a method is, and a name written by the type's body and
by an extension is resolved the way two methods of that name are (`same-name-methods.md`'s
`error.an-extension-instance-method-colliding-with-the-types-own` and `extension-overload-set.md`'s
`a-conformers-own-declaration-still-shadows-an-extensions`):

- **A type extension** (`extension Box`) that declares a member the type's own body declares would hide it,
  so it is [E3061](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3061) at the extension's declaration, the
  collision being found across files and for a standard-library type's members too.
- **An interface extension** supplies a DEFAULT: a conformer whose own body declares the member keeps its own,
  and the extension's methods, compiled for that conformer, read the conformer's declaration.

### A type extension may not declare a member the type's body declares

<!-- test: error.a-type-extension-declares-a-generic-member-the-types-body-declares -->
```maxon
typealias Digit = int(0 to 9)

type Box
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

extension Box
	typealias Items = Array with Digit

	function all() returns Items
		return Items.create()
	end 'all'
end 'Box'

function main() returns ExitCode
	let box = Box.create(3)
	var xs = box.all()
	xs.push(box.n)
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:14:12: Duplicate typealias 'Items': `Box.Items` is already declared in the body of `Box`
```

<!-- test: error.a-type-extension-declares-a-ranged-member-the-types-body-declares -->
```maxon
type Box
	typealias Idx = int(0 to 9)
	export var n as Idx

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

extension Box
	typealias Idx = int(0 to 99)

	function wide(i Idx) returns Idx
		return i
	end 'wide'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	return box.wide(5) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:12:12: Duplicate typealias 'Idx': `Box.Idx` is already declared in the body of `Box`
```

<!-- test: error.a-type-extension-declares-a-function-member-the-types-body-declares -->
```maxon
typealias Number = int(0 to 9)

type Box
	typealias Op = function(Number) returns Number
	export var n as Number

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

extension Box
	typealias Op = function(Number, Number) returns Number

	function apply(op Op) returns Number
		return op(self.n, 2)
	end 'apply'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	return box.apply(function(a, b) gives a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:14:12: Duplicate typealias 'Op': `Box.Op` is already declared in the body of `Box`
```

<!-- test: error.a-type-extension-declares-a-tuple-member-the-types-body-declares -->
```maxon
typealias Number = int(0 to 9)

type Box
	typealias Pair = (Number, Number)
	export var n as Number

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

extension Box
	typealias Pair = (Number, Number, Number)

	function triple() returns Pair
		return (self.n, self.n, self.n)
	end 'triple'
end 'Box'

function main() returns ExitCode
	let box = Box.create()
	let triple = box.triple()
	return triple.0 as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:14:12: Duplicate typealias 'Pair': `Box.Pair` is already declared in the body of `Box`
```

<!-- test: error.a-type-extension-in-another-file-declares-a-member-the-types-body-declares -->
```maxon
// --- file: box.maxon
export typealias Digit = int(0 to 9)

export type Box
	export typealias Items = List with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

// --- file: ext.maxon
export extension Box
	export typealias Items = Array with Digit

	export function all() returns Items
		return Items.create()
	end 'all'
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	var xs = box.all()
	xs.push(box.n)
	return xs.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3061: <fragment>:16:19: Duplicate typealias 'Items': `Box.Items` is already declared in the body of `Box`
```

<!-- test: error.a-program-extension-of-a-library-type-declares-a-member-the-library-types-body-declares -->
`stdlib/List.maxon`'s body declares `ElementArray`, so a program's `extension List` writing the name is held
to the same rule as its extension of `FilePath` writing `stem`.
```maxon
extension List
	typealias ElementArray = List with Element

	function chain() returns ElementArray
		return ElementArray.create()
	end 'chain'
end 'List'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3061: <fragment>:3:12: Duplicate typealias 'ElementArray': `List.ElementArray` is already declared in the body of `List`
```

### An interface extension's member is a default the conformer's own declaration overrides

<!-- test: an-interface-extension-generic-member-yields-to-the-conformers-own -->
`Box` declares `Items` over a `List`, and `extension Seq` declares it over an `Array`. The extension's
`items`, compiled for `Box`, returns `Box`'s `List`.
```maxon
typealias Digit = int(0 to 9)

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Digit
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Digit
		return self.n
	end 'first'
end 'Box'

extension Seq
	typealias Items = Array with Item

	function items() returns Items
		return Items.create()
	end 'items'
end 'Seq'

function main() returns ExitCode
	let box = Box.create(3)
	var xs = box.items()
	xs.prepend(box.first())
	return xs.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: an-interface-extension-generic-member-yields-to-the-conformers-own-in-any-file-order -->
The extension sits in a file that sorts before the type's. A second extension declares a different member,
which the conformer still receives.
```maxon
// --- file: a.maxon
export typealias Digit = int(0 to 9)

export interface Seq uses Item
	function first() returns Item
end 'Seq'

export extension Seq
	export typealias Items = Array with Item

	export function items() returns Items
		return Items.create()
	end 'items'
end 'Seq'

// --- file: b.maxon
export type Box implements Seq with Digit
	export typealias Items = List with Digit
	export var n as Digit

	export static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	export function first() returns Digit
		return self.n
	end 'first'
end 'Box'

// --- file: c.maxon
export extension Seq
	export typealias Wrapped = Array with Item

	export function wrapped() returns Wrapped
		return Wrapped.create()
	end 'wrapped'
end 'Seq'

// --- file: main.maxon
function main() returns ExitCode
	let box = Box.create(3)
	var xs = box.items()
	xs.prepend(box.first())
	var ys = box.wrapped()
	ys.push(box.n)
	return (xs.count() + ys.count()) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: an-interface-extension-function-member-yields-to-the-conformers-own -->
```maxon
typealias Number = int(0 to 9)

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Number
	typealias Op = function(Number) returns Number
	export var n as Number

	static function create(n Number) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Number
		return self.n
	end 'first'
end 'Box'

extension Seq
	typealias Op = function(Item, Item) returns Item

	function apply(op Op) returns Item
		return op(first())
	end 'apply'
end 'Seq'

function main() returns ExitCode
	let box = Box.create(4)
	return box.apply(function(a) gives a + 1) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: an-interface-extension-tuple-member-yields-to-the-conformers-own -->
```maxon
typealias Number = int(0 to 9)

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Number
	typealias Pair = (Number, Number)
	export var n as Number

	static function create(n Number) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Number
		return self.n
	end 'first'
end 'Box'

extension Seq
	typealias Pair = (Item, Item, Item)

	function pair() returns Pair
		return (first(), first())
	end 'pair'
end 'Seq'

function main() returns ExitCode
	let box = Box.create(4)
	let pair = box.pair()
	return (pair.0 + pair.1) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: error.an-interface-extension-ranged-member-yields-to-the-conformers-own -->
`Box.Idx` is `int(0 to 9)`, so the extension's `widen`, compiled for `Box`, refuses the `50` that the
extension's own `int(0 to 99)` would take.
```maxon
typealias Number = int(0 to 9)

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Number
	typealias Idx = int(0 to 9)
	export var n as Number

	static function create(n Number) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Number
		return self.n
	end 'first'
end 'Box'

extension Seq
	typealias Idx = int(0 to 99)

	function widen(i Idx) returns Idx
		return i
	end 'widen'
end 'Seq'

function main() returns ExitCode
	let box = Box.create(4)
	return box.widen(50) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:31:13: Value 50 is outside the range of 'Idx' (int(0 to 9))
```

<!-- test: a-conformer-without-its-own-member-receives-the-interface-extensions -->
The control: `Plain` declares no `Items`, so it receives the extension's `Array`.
```maxon
typealias Digit = int(0 to 9)

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Box implements Seq with Digit
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Digit
		return self.n
	end 'first'
end 'Box'

type Plain implements Seq with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Digit
		return self.n
	end 'first'
end 'Plain'

extension Seq
	typealias Items = Array with Item

	function items() returns Items
		return Items.create()
	end 'items'
end 'Seq'

function main() returns ExitCode
	let box = Box.create(3)
	let plain = Plain.create(4)
	var listed = box.items()
	listed.prepend(box.first())
	var arrayed = plain.items()
	arrayed.push(plain.first())
	return (listed.count() + arrayed.count()) as ExitCode
end 'main'
```
```exitcode
2
```

## A program's extension member and a library extension's of the same name are two members

The library declares nested typealiases in its `extension` bodies (`Array.ElementArray`, `Array.SortComparator`,
`Array.NodePower`, `Iterable.ElementTransform`). A program's own `extension` may declare a member of one of
those names, and the library never sees the program's declaration: the program's member is a separate member
of the type, spelled with the same name inside the program's extension bodies.

- **Inside a program's `extension` bodies of the type, in any file**, the bare name means the program's
  member, for the declaring body and for a sibling body in another file alike.
- **A caller of the program's method** sees the program's member in its signature.
- **A library body, and a caller of a library method, keeps the library's member**: `xs.map(…)` still returns
  an `Array`, and `xs.sort(…)` still takes a two-parameter comparator.
- **A file-private member** is spelled from its own file only; another file's extension body naming it is
  [E3008](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3008), as for any member declared elsewhere.

The rule holds for the generic-instance, function, ranged and tuple forms and for the members an interface
extension (`extension Iterable`) publishes to each conformer.

<!-- test: an-extension-member-of-the-programs-wins-over-the-librarys-generic-member -->
`Array.ElementArray` is `Array with Element` in the library. The program's `extension Array` declares it over
a `List`: `firstTwo` returns a `List`, the sibling extension in `second.maxon` spells the same member, and the
library's `map` result is still an `Array`.
```maxon
// --- file: ext.maxon
export extension Array
	export typealias ElementArray = List with Element

	export function firstTwo() returns ElementArray
		return ElementArray.create()
	end 'firstTwo'
end 'Array'

// --- file: second.maxon
export extension Array
	export function spare() returns ElementArray
		return ElementArray.create()
	end 'spare'
end 'Array'

// --- file: main.maxon
typealias Number = int(0 to 99)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers.create()
	xs.push(3)
	xs.push(1)

	var mapped = xs.map(function(n) gives n)
	mapped.push(5)

	var ys = xs.firstTwo()
	ys.prepend(2)

	var zs = xs.spare()
	zs.prepend(4)

	xs.sort(function(a, b) gives a.compare(b))
	print("{mapped.count()} {ys.count()} {zs.count()} {xs.count()}\n")
	return 0
end 'main'
```
```stdout
3 1 1 2
```
```exitcode
0
```

<!-- test: an-extension-member-of-the-programs-wins-over-the-librarys-function-member -->
`Array.SortComparator` is `function(Element, Element) returns Ordering` in the library. The program's member
takes one parameter, and `sort` still takes the library's two.
```maxon
// --- file: ext.maxon
export extension Array
	export typealias SortComparator = function(Element) returns Ordering

	export function rank(key SortComparator) returns Ordering throws ArrayError
		return key(try self.get(0))
	end 'rank'
end 'Array'

// --- file: main.maxon
typealias Number = int(0 to 9)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers from [3, 1, 2]
	xs.sort(function(a, b) gives a.compare(b))
	let first = try xs.rank(function(_) gives Ordering.equalTo) otherwise Ordering.lessThan
	print("{xs.count()} {first.rawValue}\n")
	return 0
end 'main'
```
```stdout
3 1
```
```exitcode
0
```

<!-- test: an-extension-member-of-the-programs-wins-over-the-librarys-ranged-member -->
`Array.NodePower` is `int(0 to 64)` in the library's sort helpers. The program's member is `int(0 to 9)`, and
sorting a hundred elements still runs the library's code over the library's range. The program's own `Byte`
declares a name the library declares, so the library is folded after the program.
```maxon
// --- file: ext.maxon
export extension Array
	export typealias NodePower = int(0 to 9)

	export function power(n NodePower) returns NodePower
		return n
	end 'power'
end 'Array'

// --- file: main.maxon
typealias Byte = int(0 to 255)
typealias Number = int(0 to 99)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers.create()
	let marker = 7 as Byte

	for i in 0 upto 100 'fill'
		xs.push(((i * 7) mod 100) as Number)
	end 'fill'

	xs.sort(function(a, b) gives a.compare(b))
	print("{xs.count()} {xs.power(5)} {marker}\n")
	return 0
end 'main'
```
```stdout
100 5 7
```
```exitcode
0
```

<!-- test: error.a-ranged-member-of-the-programs-is-held-to-its-own-range -->
The program's `NodePower` is `int(0 to 9)`, so the `50` that a sibling extension's `narrow` is handed is held to
the program's range and not to the library's `int(0 to 64)`.
```maxon
// --- file: ext.maxon
export extension Array
	export typealias NodePower = int(0 to 9)

	export function power(n NodePower) returns NodePower
		return n
	end 'power'
end 'Array'

// --- file: second.maxon
export extension Array
	export function narrow(n NodePower) returns NodePower
		return n
	end 'narrow'
end 'Array'

// --- file: main.maxon
typealias Number = int(0 to 99)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers.create()
	xs.push(1)
	return xs.narrow(50) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:12: Value 50 is outside the range of 'NodePower' (int(0 to 9))
```

<!-- test: an-interface-extension-member-of-the-programs-wins-over-the-librarys -->
`extension Iterable` declares `ElementTransform` in the library. The program's `extension Iterable` declares it
with two parameters: `combine` takes the program's, and the library's `map` keeps its own.
```maxon
// --- file: ext.maxon
export extension Iterable
	export typealias ElementTransform = function(Element, Element) returns Element

	export function combine(transform ElementTransform, seed Element) returns Element
		var total = seed

		for item in self 'each'
			total = transform(total, item)
		end 'each'

		return total
	end 'combine'
end 'Iterable'

// --- file: main.maxon
typealias Number = int(0 to 99)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers.create()
	xs.push(3)
	xs.push(1)

	var mapped = xs.map(function(n) gives n)
	mapped.push(5)

	let total = xs.combine(function(a, b) gives (a + b) as Number, seed: 0)
	print("{mapped.count()} {total}\n")
	return 0
end 'main'
```
```stdout
3 4
```
```exitcode
0
```

<!-- test: an-extension-that-declares-no-member-reads-the-librarys -->
The control: no program declaration of `ElementArray`, so the program's `extension Array` spells the
library's, an `Array`.
```maxon
extension Array
	function spare() returns ElementArray
		return ElementArray.create()
	end 'spare'
end 'Array'

typealias Number = int(0 to 99)
typealias Numbers = Array with Number

function main() returns ExitCode
	var xs = Numbers.create()
	var zs = xs.spare()
	zs.push(4)
	return zs.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.another-files-extension-naming-a-private-member-of-the-programs -->
`ext.maxon` declares `ElementArray` without a visibility modifier, so it is spelled from that file only; the
sibling extension in `second.maxon` naming it is refused rather than reading the library's.
```maxon
// --- file: ext.maxon
extension Array
	typealias ElementArray = List with Element

	function firstTwo() returns ElementArray
		return ElementArray.create()
	end 'firstTwo'
end 'Array'

// --- file: second.maxon
extension Array
	function spare() returns ElementArray
		return ElementArray.create()
	end 'spare'
end 'Array'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:13:27: typealias 'ElementArray' is not exported
```

## A NON-GENERIC type's member has a FILE-SCOPE name; a generic type's member does not

Everything above is about which BODY may spell a member — the declaring type's own, or an `extension`
over it. This is the third reader, and it is the one with no enclosing type open at all: **file scope**.

A `type` that takes no parameters has exactly one instance, so a `typealias` written in its body
denotes one type for the whole program. Without a name that can be written from outside, the member
would be unspellable and so would every signature built on it: no other file could declare a variable, a
parameter or a return of a type a method of that type hands back. A GENERIC type's member is the opposite case: `Bag = Array with T` means a different type
per instantiation (`wrong-instance-error`), so it has no single meaning a file-scope name could carry,
and it keeps only its `<Type>.<member>` key.

⚠ **The FUNCTION form and the generic-instance form both have a file-scope name.** A nested
`typealias Fn = function(…)` and a nested `typealias Bag = Array with Num` are each filed under BOTH its
bare name and `<Type>.<member>`. Filed under the qualified name ALONE, `Bag.create()` at file scope would
type its result `unknown` and the program would be refused one door later for a member access on it,
and `returns Bag` would be `E3011`.

⚠ **The RANGED and TUPLE forms have no file-scope name, and that is a boundary rather than an
oversight.** A ranged `typealias` is FILE-SCOPED by its declaration form (`type-name-collision.md`'s
table of the three registries), so "a bare whole-program key" is not the shape its answer takes at all
and handing it one would settle a different question than this rule does. Nothing needs it:
what `Directory.list` returns is a generic instance, and so is every member the compiler's own harness
names.

### A plain type's member generic alias is a STATIC-CALL base at file scope

<!-- test: plain-type-member-generic-alias-at-file-scope -->
`Bag.create()` written by a free function in another file. Before this rule the result typed `unknown`
and the `push` one line down was `E2015`.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type Holder
	export typealias Bag = Array with Num

	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'

	export function fill() returns Bag
		var b = Bag.create()
		b.push(self.v)
		return b
	end 'fill'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	var b = Bag.create()
	b.push(20)
	b.push(22)
	let h = Holder.create(1)
	let ownBag = h.fill()
	if b.count() == 2 and ownBag.count() == 1 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### The same member is a DECLARED TYPE at file scope

<!-- test: plain-type-member-generic-alias-as-a-declared-type -->
The other door, and it is a different one: a written type annotation is resolved by
`parseTypeReference`, a static call base by `parseQualifiedCall`. A free function's PARAMETER, its
RETURN and a plain struct's FIELD all name the member. Before this rule each was
`E3011 Unknown type 'Bag'`.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type Holder
	export typealias Bag = Array with Num

	export var v as Num

	export static function create(v Num) returns Holder
		return Holder{v: v}
	end 'create'

	export function fill() returns Bag
		var b = Bag.create()
		b.push(self.v)
		return b
	end 'fill'
end 'Holder'

// --- file: main.maxon
type Keeper
	var held as Bag

	static function create(held Bag) returns Keeper
		return Keeper{held: held}
	end 'create'

	function total() returns Num
		return sumOf(self.held)
	end 'total'
end 'Keeper'

function makeBag() returns Bag
	var b = Bag.create()
	b.push(20)
	b.push(22)
	return b
end 'makeBag'

function sumOf(b Bag) returns Num
	var total = 0 as Num
	for n in b 'each'
		total = total + n
	end 'each'
	return total
end 'sumOf'

function main() returns ExitCode
	let k = Keeper.create(makeBag())
	let h = Holder.create(3)
	if k.total() == 42 and sumOf(h.fill()) == 3 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

### `stdlib`'s own case — `Directory.list`'s return, named bare

<!-- test: stdlib-directory-member-alias-at-file-scope -->
`FilePathArray` is `stdlib/Directory.maxon`'s `public` alias for `Directory.list`'s return, one type for the
whole program, so a user file writes it bare. The compiler's own spec harness spells it exactly this way.
```maxon
function main() returns ExitCode
	var paths = FilePathArray.create()
	paths.push(try FilePath.from("a") otherwise panic("unreachable: 'a' is a valid path"))
	paths.push(try FilePath.from("b") otherwise panic("unreachable: 'b' is a valid path"))
	return paths.count() as ExitCode
end 'main'
```
```exitcode
2
```

### A GENERIC type's member generic alias is per-instance and has NO file-scope name

<!-- test: error.generic-type-member-generic-alias-has-no-file-scope-name -->
The control the widening owes, and the reason the rule is the enclosing type's PARAMETER LIST rather
than "a nested alias is a top-level declaration written indented" — which is what both reference
compilers do. `Bag = Array with T` is a different array for every `Holder with …`, so there is no one
type for the bare name to denote and it stays refused.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type Holder uses T
	export typealias Bag = Array with T

	export var v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function fill() returns Bag
		var b = Bag.create()
		b.push(self.v)
		return b
	end 'fill'
end 'Holder'

export typealias NumHolder = Holder with Num

// --- file: main.maxon
function firstOf(b Bag) returns Num
	return try b.get(0) otherwise 0
end 'firstOf'

function main() returns ExitCode
	let h = NumHolder.create(3)
	return firstOf(h.fill()) as ExitCode
end 'main'
```
```maxoncstderr
error E3011: <fragment>:24:20: Unknown type 'Bag'
```

### Two plain types in two files may declare one member name, exactly as two file-scope aliases may

<!-- test: crossfile-plain-type-member-aliases-of-one-name-stay-file-scoped -->
`crossfile-generic-alias-same-name-still-legal`'s rule, reached through the nested form: the bare key a
member files is the same registry a file-scope `typealias` files, so a name two files disagree
about is settled the same way — each file reads its own declaration.
```maxon
// --- file: a.maxon
export typealias Num = int(0 to 200)

export type HolderA
	typealias Bag = Array with Num

	export var v as Num

	export static function create(v Num) returns HolderA
		return HolderA{v: v}
	end 'create'
end 'HolderA'

export function fromA() returns Num
	var b = Bag.create()
	b.push(20)
	return try b.get(0) otherwise 0
end 'fromA'

// --- file: b.maxon
export type HolderB
	typealias Bag = Array with String

	export var w as String

	export static function create(w String) returns HolderB
		return HolderB{w: w}
	end 'create'
end 'HolderB'

export function fromB() returns Num
	var b = Bag.create()
	b.push("xx")
	let first = try b.get(0) otherwise ""
	return first.count() as Num
end 'fromB'

// --- file: main.maxon
function main() returns ExitCode
	let a = HolderA.create(1)
	let b = HolderB.create("y")
	if a.v == 1 and b.w.count() == 1 'constructed'
		return (fromA() + fromB()) as ExitCode
	end 'constructed'
	return 1
end 'main'
```
```exitcode
22
```

<!-- test: a-nested-pattern-alias-compares-and-prints-unsigned -->
```maxon
type Holder
	typealias Word = bits(64)

	var unused as bool

	static function create() returns Holder
		return Holder{unused: false}
	end 'create'

	function top() returns bool
		let twoTo62 = 4611686018427387904 as Word
		let top = twoTo62 * 2
		print("{top} {top > 0}\n")
		return top > 0
	end 'top'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()

	if holder.top() 'positive'
		return 0
	end 'positive'

	return 1
end 'main'
```
```exitcode
0
```
```stdout
9223372036854775808 true
```

<!-- test: a-cast-to-a-nested-ranged-alias-is-range-checked -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	typealias Small = int(0 to 10)

	var unused as bool

	static function create() returns Holder
		return Holder{unused: false}
	end 'create'

	function narrow(n Integer) returns Integer
		let small = n as Small
		return small
	end 'narrow'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	print("{holder.narrow(50)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-cast-to-a-nested-ranged-alias-is-range-checked.test:14: Range check failed: value outside typealias 'Small'
Stack trace:
  in Holder.narrow
  in main
  in mrt_start
```
