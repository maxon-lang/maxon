---
feature: extension-overload-set
status: stable
keywords: [extension, overload, cross-file, method, duplicate]
category: type-system
---

# One Method Name, Two Extensions

## Documentation

A method name declared by **two `extension` declarations onto one conformer** is an **overload set**,
exactly as two declarations of one name inside a single `type` body are. Which one a call means is
decided by its arguments, at the call.

```maxon
extension Iterable
	function contains(predicate ElementPredicate) returns bool
end 'Iterable'

extension Array where Element is Equatable
	function contains(element Element) returns bool
	function contains(sequence ElementArray) returns bool
end 'Array'
```

All three are callable on an `Array`: `arr.contains(3)`, `arr.contains(other)` and
`arr.contains(function(x) gives x > 3)` each pick a different one.

### Why the FILE boundary cannot decide this

A declaration's **registration name** is minted where the declaration is parsed, and a parser is a pure
function of its own file. So a later overload of a name the same file already claimed registers as
`pick#bool`, while a later overload of a name **another** file claimed has no way to know it is later
at all — a per-file rule would register both under the bare name and collide them at the merge (`E3006`).

The whole-program extension fold is what closes it: it walks every `extension` declaration in the
program before any file is parsed, so it — and only it — can say *"this `<Conformer>.<method>` is
declared by extensions in more than one file"*. It records that, and each file's parse reads the answer
back rather than re-deriving it.

### When the name is contested, NOBODY keeps the bare spelling

An uncontested declaration registers under its own name. A **contested** one registers
under its parameter-type suffix — and so does the first of them, which is the whole point:

- two declarations whose parameters differ mint **different** suffixes and are two live overloads;
- two declarations whose parameters are the **same** mint the **same** suffix and collide, which is the
  `E3006` a genuine redeclaration has always earned.

The refusal is therefore established by construction rather than by a second check that could disagree
with the first. It is the same construction a free function contested across directories takes, where
neither claimant keeps the bare name either.

### What a conformer's own body still shadows

None of this changes the rule that a **conformer's own declaration** replaces an extension's same-named
method (an interface extension supplies DEFAULT implementations, and a conformer that declares the name
has said what it means there). That method is never published, so it never joins an overload set and
never contests one.

## Tests

<!-- test: two-extensions-in-one-file-are-one-overload-set -->
The control: one file, two `extension` declarations onto one interface, one method name, two parameter
types. This already worked — the file's own overload registry can see both — and it must keep working
byte for byte, because the contest rule is deliberately scoped to the boundary that registry cannot see.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

extension Tagged
	function pick(n Integer) returns Integer
		return tag() + n
	end 'pick'
end 'Tagged'

extension Tagged
	function pick(flag bool) returns Integer
		return tag() + (100 if flag else 0)
	end 'pick'
end 'Tagged'

type Five implements Tagged
	static function make() returns Self
		return Self{}
	end 'make'

	function tag() returns Integer
		return 5
	end 'tag'
end 'Five'

function main() returns ExitCode
	let f = Five.make()
	return f.pick(2) + f.pick(true)
end 'main'
```
```exitcode
112
```

<!-- test: two-extensions-in-two-files-are-one-overload-set -->
The headline case. `Five.pick` is declared by an `extension Tagged` in each file, and the two
declarations are not duplicates of each other. Both are live and the argument picks between them:
`7 + 105`.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Tagged
	function tag() returns Integer
end 'Tagged'

export extension Tagged
	export function pick(n Integer) returns Integer
		return tag() + n
	end 'pick'
end 'Tagged'

export type Five implements Tagged
	export static function make() returns Self
		return Self{}
	end 'make'

	export function tag() returns Integer
		return 5
	end 'tag'
end 'Five'

// --- file: b.maxon
export typealias Count = int(i64.min to i64.max)

export extension Tagged
	export function pick(flag bool) returns Count
		return tag() + (100 if flag else 0)
	end 'pick'
end 'Tagged'

function main() returns ExitCode
	let f = Five.make()
	return (f.pick(2) as Count) + f.pick(true)
end 'main'
```
```exitcode
112
```

<!-- test: two-extensions-on-a-user-type-in-two-files -->
The same rule on a **type** extension rather than an interface extension. The two targets reach
`foldExtensionDeclarationInto` through different arms of `ExtensionTarget`, so both are pinned: a rule
that held for only one of them would be a boundary nobody wrote down.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export type Box
	export var v as Integer

	export static function of(v Integer) returns Self
		return Self{v: v}
	end 'of'
end 'Box'

export extension Box
	export function widen(n Integer) returns Integer
		return self.v * n
	end 'widen'
end 'Box'

// --- file: b.maxon
export typealias Amount = int(i64.min to i64.max)

export extension Box
	export function widen(flag bool) returns Amount
		return self.v + (1 if flag else 0)
	end 'widen'
end 'Box'

function main() returns ExitCode
	let b = Box.of(10)
	return (b.widen(4) as Amount) + b.widen(true)
end 'main'
```
```exitcode
51
```

<!-- test: three-declarations-across-two-files-are-one-set -->
`stdlib/Array.maxon`'s own shape: **two** overloads of `contains` in one file's extension and a **third**
in `stdlib/Interfaces.maxon`'s `extension Iterable`. It is the case a per-file overload set cannot
express — each file knows only the members it declared — so the set has to be assembled where every
file's members meet. `5 + 11 + 7`.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Counted
	function base() returns Integer
end 'Counted'

export extension Counted
	export function score(n Integer) returns Integer
		return base() + n
	end 'score'

	export function score(flag bool) returns Integer
		return base() + (10 if flag else 0)
	end 'score'
end 'Counted'

export type Unit implements Counted
	export static function make() returns Self
		return Self{}
	end 'make'

	export function base() returns Integer
		return 1
	end 'base'
end 'Unit'

// --- file: b.maxon
export typealias Amount = int(i64.min to i64.max)

public type Weight
	export var w as Amount

	export static function of(w Amount) returns Self
		return Self{w: w}
	end 'of'
end 'Weight'

export extension Counted
	export function score(x Weight) returns Amount
		return (base() as Amount) * x.w
	end 'score'
end 'Counted'

function main() returns ExitCode
	let u = Unit.make()
	return (u.score(4) as Amount) + (u.score(true) as Amount) + u.score(Weight.of(7))
end 'main'
```
```exitcode
23
```

<!-- test: a-conformers-own-declaration-still-shadows-an-extensions -->
The boundary of the rule above. `Bag` declares its own `has`, so `extension Holder`'s `has` is never
published onto it — it is not a member of any set and it does not contest one. The conformer's answer
is the only one, which is what "an extension supplies a DEFAULT implementation" means.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Holder
	function only() returns Integer
end 'Holder'

extension Holder
	function has() returns Integer
		return 1
	end 'has'
end 'Holder'

type Bag implements Holder
	static function make() returns Self
		return Self{}
	end 'make'

	function only() returns Integer
		return 3
	end 'only'

	function has() returns Integer
		return 40
	end 'has'
end 'Bag'

function main() returns ExitCode
	let b = Bag.make()
	return b.has() + b.only()
end 'main'
```
```exitcode
43
```

<!-- test: error.a-call-matching-no-member-of-a-contested-set-names-the-argument -->
⛔ **A CALL THAT FITS NO MEMBER MUST STILL BE REPORTED AGAINST AN ARGUMENT, NOT AS A CALL TO SOMETHING
UNDEFINED.** `SemanticCheck.resolveOverloadedCalls` deliberately leaves such a call alone so that
`checkCalls` reports the per-argument mismatch — a rule whose stated premise is *"the bare name is the first
member's registration name"*. A CONTESTED set has no member under the bare name, so that premise fails
exactly here, and the call was reported as **`E3004: call to undefined function 'Five.pick'`** — about a
method that is declared twice over.

⚠ The same program with both extensions in ONE file has always given the sentence below, which is what
makes this a difference between two spellings of one program rather than a diagnostic anyone chose.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Tagged
	function tag() returns Integer
end 'Tagged'

export extension Tagged
	export function pick(n Integer) returns Integer
		return tag() + n
	end 'pick'
end 'Tagged'

export type Five implements Tagged
	export static function make() returns Self
		return Self{}
	end 'make'

	export function tag() returns Integer
		return 5
	end 'tag'
end 'Five'

// --- file: b.maxon
export typealias Count = int(i64.min to i64.max)

export extension Tagged
	export function pick(flag bool) returns Count
		return tag() + (1 if flag else 0)
	end 'pick'
end 'Tagged'

function main() returns ExitCode
	let f = Five.make()
	return f.pick("nope")
end 'main'
```
```maxoncstderr
error E3005: <fragment>:36:11: argument type mismatch for 'n': expected 'Integer', got 'String'
```

### A genuine redeclaration is still refused

<!-- test: error.one-signature-declared-by-two-extensions-in-two-files -->
⛔ **TWO EXTENSIONS DECLARING ONE NAME WITH THE SAME PARAMETER TYPES ARE NOT AN OVERLOAD SET, AND
MUST NOT BE SILENTLY ACCEPTED.** They render the same parameter-type suffix, so they claim one
registration name and collide at the merge — the refusal falls out of the mint rather than out of a
second check written beside it.

⚠ A contested name is registered under its parameter types, never bare. The message names the member
in source form, `'Five.pick(bool)'`, and earns the same extra sentence a free function contested across
directories does, saying where the collision comes from.

⚠ Both declarations return `bool`, and that is not incidental: an overload set whose members disagree on
their return type is a SEPARATE, pre-existing boundary (`SemanticCheck.reportOverloadReturnDisagreement`),
and a disagreement here is reported before the merge ever runs — it would mask the refusal this case is
about.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Tagged
	function tag() returns Integer
end 'Tagged'

export extension Tagged
	export function pick(flag bool) returns bool
		return flag and tag() > 0
	end 'pick'
end 'Tagged'

export type Five implements Tagged
	export static function make() returns Self
		return Self{}
	end 'make'

	export function tag() returns Integer
		return 5
	end 'tag'
end 'Five'

// --- file: b.maxon
export extension Tagged
	export function pick(flag bool) returns bool
		return not flag
	end 'pick'
end 'Tagged'

function main() returns ExitCode
	let f = Five.make()
	if f.pick(true) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:27:18: duplicate definition of function 'Five.pick(bool)' — 'Five.pick' is declared by an `extension` in more than one FILE, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: error.one-signature-declared-by-two-extensions-in-the-OTHER-fold-order -->
⭐ **THE SAME PROGRAM WITH THE TWO EXTENSIONS SWAPPED BETWEEN THE FILES — the refusal must not depend on
which one folds first.** The extension fold walks the program's `extension` declarations in source-path
order, so the file each body sits in decides the order they are published in; only a pair of cases can
say whether the verdict does.

⚠ **WHAT MAKES IT ORDER-INDEPENDENT IS ONE EXCLUSION, and its absence is a SILENT ACCEPTANCE rather than
a different diagnostic.** By the time the second declaration folds, the first has already put
`Five.pick` in the whole-program index — so "a signature already exists under this name" cannot tell the
conformer's own declaration from an earlier extension's. Read as the conformer's, the second declaration
is judged SHADOWED, withheld without a word, and the program compiles and returns an answer. The verdict
therefore asks only about declarations an extension did NOT publish.
```maxon
// --- file: a.maxon
export typealias Integer = int(i64.min to i64.max)

export interface Tagged
	function tag() returns Integer
end 'Tagged'

export extension Tagged
	export function pick(flag bool) returns bool
		return not flag
	end 'pick'
end 'Tagged'

export type Five implements Tagged
	export static function make() returns Self
		return Self{}
	end 'make'

	export function tag() returns Integer
		return 5
	end 'tag'
end 'Five'

// --- file: b.maxon
export extension Tagged
	export function pick(flag bool) returns bool
		return flag and tag() > 0
	end 'pick'
end 'Tagged'

function main() returns ExitCode
	let f = Five.make()
	if f.pick(true) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:27:18: duplicate definition of function 'Five.pick(bool)' — 'Five.pick' is declared by an `extension` in more than one FILE, so every one of those declarations is registered under its parameter-type spelling, and two of them spell the same parameters. Give the overloads distinct parameter types, or distinct names
```

<!-- test: an-overload-set-on-a-generic-types-extension-resolves-at-the-instance -->
The control above is a NON-generic conformer. A shared generic body spells its receiver at the base
`Holder with <T>`, so the overload scorer cannot read the raw signature: scoring `h.rank(40)` on a
`Holder with Integer` would compare `Holder_T<gid>` against `Holder_Integer` at position 0 and call EVERY
candidate incompatible. The scorer resolves each parameter through the call's instance, as `checkArgTypes`
does one pass later.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder uses Element
	export var value as Element

	static function create(v Element) returns Self
		return Self{value: v}
	end 'create'
end 'Holder'

export extension Holder
	function rank() returns Integer
		return 1
	end 'rank'
end 'Holder'

export extension Holder
	function rank(bonus Integer) returns Integer
		return 1 + bonus
	end 'rank'
end 'Holder'

typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let h = IntHolder.create(7)
	return h.rank() + h.rank(40)
end 'main'
```
```exitcode
42
```

<!-- test: an-unconstrained-overload-of-a-conditional-extension-method-is-reachable -->
The two extensions declaring one name need not agree about their `where` clause, and `stdlib/Array.maxon` is
built that way: `extension Array where Element is Comparable` declares `sort()` and a second, UNCONSTRAINED
`extension Array` declares `sort(cmp)`.

⛔ **The conditional-extension gate is keyed on the NAME, and a call is dispatched before its arguments are
read** — so deciding at parse time would refuse the UNCONSTRAINED overload with the constrained one's
clause. The parse-time gate DEFERS when an unconditional declaration of the name exists, and
`SemanticCheck.checkResolvedExtensionConstraints` decides on the member overload resolution picked — which is
the earliest point at which the question has an answer. Both readings are exercised here: the CONFORMING
instance still reaches the conditional overload, and the non-conforming one reaches the unconstrained one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Opaque
	static function create() returns Self
		return Self{}
	end 'create'
end 'Opaque'

type Holder uses Element
	export var value as Element

	static function create(v Element) returns Self
		return Self{value: v}
	end 'create'
end 'Holder'

export extension Holder where Element is Comparable
	function beats(other Element) returns bool
		return self.value.compare(other) == Ordering.greaterThan
	end 'beats'
end 'Holder'

export extension Holder
	function beats(assumed bool) returns bool
		return assumed
	end 'beats'
end 'Holder'

typealias IntHolder = Holder with Integer
typealias OpaqueHolder = Holder with Opaque

function main() returns ExitCode
	let h = IntHolder.create(7)
	let o = OpaqueHolder.create(Opaque.create())
	var n = 0
	if h.beats(3) 'cmp'
		n = n + 1
	end 'cmp'
	if o.beats(true) 'fb'
		n = n + 10
	end 'fb'
	return n
end 'main'
```
```exitcode
11
```

<!-- test: error.the-conditional-overload-of-a-contested-name-is-still-refused -->
The other half of that deferral, and the half that keeps it SOUND. The same two declarations, and a call
whose ARGUMENT can only mean the CONDITIONAL one — on an instance whose element does not conform. Nothing
would refuse it if the deferral were the end of the story, and the program would reach
`LowerMaxonToStd.ensureWitnessTable` for a conformance nothing validated, which PANICS rather than failing.
The post-resolution decider asks the same predicate the parse-time gate asks
(`ConformanceCheck.receiverMeetsExtensionConstraints`) and quotes the same sentence
(`conditionalExtensionWithheldMessage`), so the refusal a reader sees does not depend on which of the two
answered.
```maxon
type Opaque
	static function create() returns Self
		return Self{}
	end 'create'
end 'Opaque'

type Holder uses Element
	export var value as Element

	static function create(v Element) returns Self
		return Self{value: v}
	end 'create'
end 'Holder'

export extension Holder where Element is Comparable
	function beats(other Element) returns bool
		return self.value.compare(other) == Ordering.greaterThan
	end 'beats'
end 'Holder'

export extension Holder
	function beats(assumed bool) returns bool
		return assumed
	end 'beats'
end 'Holder'

typealias OpaqueHolder = Holder with Opaque

function main() returns ExitCode
	let o = OpaqueHolder.create(Opaque.create())
	if o.beats(Opaque.create()) 'cmp'
		return 1
	end 'cmp'
	return 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:32:7: Type 'Holder' has no field named 'beats' ('beats' is available as a conditional extension where Element is Comparable, but 'Opaque' does not implement 'Comparable')
```
