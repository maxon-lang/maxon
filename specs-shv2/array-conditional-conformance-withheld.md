---
feature: array-conditional-conformance-withheld
status: experimental
keywords: [array, hashable, equatable, conditional, extension, where, map, set, diagnostics]
category: type-system
---

# `Array`'s `Hashable`/`Equatable` is CONDITIONAL — what a withheld one is refused with

## Documentation

`Array` conforms to `Hashable` and `Equatable` only `where Element is Hashable and Equatable`
(`stdlib/Array.maxon`, and `specs-shv2/array-hashable.md` for what the conformance DOES). An array whose
element does not satisfy that clause has no `hash`, no `equals` and no `==`/`!=`, and cannot be a `Map` or
`Set` key.

This file is the WITHHELD half. It lives here rather than beside the positive cases because
`array-hashable.md` is ported byte-identical from `/specs` and may not gain an shv2-authored case — the
same reason `builtin-member-rosters.md` gives for `Character`'s roster.

**One clause, one walk, three doors.** The element's conformance is decided by a single predicate
(`ProgramSignatures.arrayElementConstraintCheck`) under a single list (`Array`'s own intrinsic conformance
row), and three doors consume its verdict:

- the METHOD surface — `arr.hash()` / `a.equals(b)` — refused with **E4006**, the sentence a user's own
  conditional extension is withheld with, naming the unmet interface and the element that failed it;
- the `==` / `!=` OPERATOR, which IS `Array.equals`, so it is refused with the same sentence at the
  operator's own span;
- the hash-table KEY gate, which admits `Array` as a key type and must therefore not report a withheld
  array as a key type it does not serve yet. Its refusal names the clause and the element instead.

A copy of the walk at any one of those doors is a silent wrong answer in either direction: `a == b` refused
while the same array is still stamped with `__witness_Array.*` and admitted as a key, or the reverse.
Neither is a compile error and neither shows up as a failing test, which is why the cases below hold all
three doors to one clause.

## Tests

<!-- test: error.hash-is-withheld-for-a-non-conforming-element -->
### `hash()` on an array of a non-conforming element names the unmet interface
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque

function main() returns ExitCode
	var a = OpaqueArr.create()
	a.push(Opaque.create(1))
	let h = a.hash()
	return h
end 'main'
```
```maxoncstderr
error E4006: <fragment>:17:12: Type 'Array' has no field named 'hash' ('hash' is available as a conditional extension where Element is Hashable and Equatable, but 'Opaque' does not implement 'Hashable')
```

<!-- test: error.equality-operator-is-withheld-for-a-non-conforming-element -->
### `==` on two such arrays is refused as the `equals` it dispatches to
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque

function main() returns ExitCode
	var a = OpaqueArr.create()
	a.push(Opaque.create(1))
	var b = OpaqueArr.create()
	b.push(Opaque.create(1))
	if a == b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:19:7: Type 'Array' has no field named 'equals' ('equals' is available as a conditional extension where Element is Hashable and Equatable, but 'Opaque' does not implement 'Hashable')
```

<!-- disabled-test: error.a-map-key-array-is-refused-for-its-element -->
<!-- the refusal is CORRECT but lands at `stdlib/Array.maxon:145` with the ABSOLUTE checkout path, and the runner has no `<stdlib>` normalization token, so no portable `maxoncstderr` can be written for it. Needs BOTH: (a) a stdlib-path normalization in `Testing/SpecTestRunner.maxon` (see `FragmentPathMapping` — today only STAGED fragment paths are rewritten), and (b) the blame edge that would re-point a stdlib-body diagnostic at the user construct that forced it, which does not exist (no re-anchoring on any reporting path; `GenericInstantiationSite` records where the `typealias` was WRITTEN, so for a stdlib alias the site IS the stdlib). Not an `Array` gap — the deep-clone slice it names is a real documented one -->
### A `Map` key array is refused for its ELEMENT, not as an unserved key type

⛔⛔ **SHELVED BY `land-the-listing`, AND THE REASON IS NOT THAT THE COMPILER GOT WORSE — READ THIS BEFORE
RE-ENABLING IT.** With `stdlib/Array.maxon` listed, this program is refused by the OPAQUE COPY GATE
(`Parser.requireOpaqueArrayCopyable`) before the constraint check ever runs, and the sentence it prints names
`stdlib/Array.maxon:145:32` — `Array.clone`'s own `managed.slice(0, len)`, a line no user wrote. The two
E3017s below are what the user's mistake actually IS and they never speak, because a `ParseError` stops the
file before the pipeline and `checkWhereConstraints` short-circuits on `projectHasErrors`.

⭐⭐ **THE CANDIDATE CURE WAS EVALUATED AND MUST NOT BE TAKEN — THERE IS A RUN THAT REFUTES IT.** The standing
proposal was to drop `Array`'s own opaque gate, on the ground that *"`Array`'s body is the one opaque body
whose every call site is already concretely gated (`requireArrayElementCopyable`)"*. That is false, and the
counterexample needs no `Map` at all:

```
typealias Nested = Array with (Array with String)
function copyIt(c Cloneable) returns Cloneable
	return c.clone()
end 'copyIt'
… copyIt(Nested.create())
```

MEASURED: this program calls `.clone()` at NO concretely-typed site, so `requireArrayElementCopyable` never
fires; it reaches `Array.clone`'s shared body purely through the `Cloneable` witness `type Array …
implements … Cloneable` promises unconditionally. It is refused TODAY by exactly the gate the cure would
delete, with exactly the message quoted below. Remove the gate and that program compiles and byte-blits a
managed pointer through `copyFunc@32`. ⇒ **the existential-dispatch hole the previous rung could not close is
real, and closing it is what a cure must do first — not something to route around.**

⚠ **AND THE COST IS WIDER THAN THIS CASE, WHICH IS THE PART WORTH CARRYING FORWARD.** MEASURED: `typealias
StrArrMap = Map with (Array with String, Val)` — a key type that satisfies `Hashable` and `Equatable`
perfectly well, and which the bootstrap oracle compiles — takes the SAME refusal at the SAME stdlib line. So
the gate is not merely misplaced on an already-invalid program; it refuses valid ones. The underlying gap
(`Array with (Array with String)` has no single `copyFunc`) is a documented later slice, not something this
rung invents.

⚠ Its SUBJECT is not lost while it is shelved: `error.a-set-key-array-spelled-inline-is-refused-the-same-way`
directly below refuses the identical mistake for a `Set`, at the USER's line, naming the element by name —
which is also a fair statement of what a good answer here would look like.

⭐ **THE SENTENCE MOVED WHEN `Map` STOPPED BEING SYNTHESIZED (W41), AND THE RULE IT NOW QUOTES IS THE
GENERAL ONE.** This used to be a bespoke `E2015` the builtin map's own key gate wrote. `Map` is
`stdlib/Map.maxon` now, declared `where Key is Hashable and Equatable` — so the refusal is the
ordinary `where`-constraint refusal every generic gets, at the instantiation rather than at the
`create()`. That is a better answer twice over: it NAMES the constraint that failed, and it is one
rule instead of a container-specific copy of one.

⚠ **BOTH constraints are reported, and that is the clause count rather than a duplicate.** `Key` is
constrained twice; `OpaqueArr` discharges neither, so `checkOneInstantiation` reports each. The
`Array`-element reasoning this case is named for has not gone anywhere — it is why `OpaqueArr`
reduces to a conformer name nothing claims (`instanceConformerName`'s conditional `Array` arm), and
the sibling `E4006` cases above still state it in full.
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque
typealias OpaqueArrMap = Map with (OpaqueArr, Val)

function main() returns ExitCode
	var m = OpaqueArrMap.create()
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:13:11: Type 'OpaqueArr' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
error E3017: <fragment>:13:11: Type 'OpaqueArr' does not satisfy constraint 'Equatable' required by type parameter 'Key' of 'Map'
```

<!-- test: error.a-set-key-array-spelled-inline-is-refused-the-same-way -->
### The same refusal reaches a `Set` and an inline `Array with E` spelling
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArrSet = Set with (Array with Opaque)

function main() returns ExitCode
	var s = OpaqueArrSet.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: `Set` hashes and compares its keys through their `Hashable`/`Equatable` witnesses, and `Array` supplies those only as a conditional extension (where Element is Hashable and Equatable) — this key's element 'Opaque' does not implement 'Hashable'
```

<!-- test: error.a-key-type-nothing-conforms-for-still-reads-as-a-later-slice -->
### A key that is not an array at all is refused by its CONSTRAINT

⚠⚠ **THIS CASE'S ID IS HISTORICAL AND ITS SECOND HALF IS NOW FALSE — read the heading, not the
name.** It was minted when `Map` was synthesized and its keys were a fixed roster of four
(`int`, `String`, `Character`, `Array`), so anything else "is a later slice". W41 listed
`stdlib/Map.maxon` and retired the builtin: **the roster is gone, and any `Hashable + Equatable` type
is a key** — `map.md`'s `a-user-type-can-be-a-map-key` pins a user `Point` doing exactly that, agreed
with the oracle. What is left to refuse here is a type that implements NEITHER, which the ordinary
`where`-constraint check reports against `Map`'s own declared clause.

The id is kept rather than corrected because it names two COMMITTED golden fragments, one of them
`x64-linux`, which cannot be regenerated on this host — orphaning a cross-target golden to fix a
name would trade a stale word for a lost measurement. The note is the cheaper honest fix; see
`spec-marker-can-be-a-sentence` for why the wording is called out at all rather than left to be
believed.
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueMap = Map with (Opaque, Val)

function main() returns ExitCode
	var m = OpaqueMap.create()
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:12:11: Type 'Opaque' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
error E3017: <fragment>:12:11: Type 'Opaque' does not satisfy constraint 'Equatable' required by type parameter 'Key' of 'Map'
```

<!-- test: error.an-array-of-non-conforming-arrays-names-the-inner-array -->
### A nested array whose INNER array is withheld reports the inner spelling
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque
typealias OpaqueArrArr = Array with OpaqueArr

function main() returns ExitCode
	var outer = OpaqueArrArr.create()
	let h = outer.hash()
	return h
end 'main'
```
```maxoncstderr
error E4006: <fragment>:17:16: Type 'Array' has no field named 'hash' ('hash' is available as a conditional extension where Element is Hashable and Equatable, but 'OpaqueArr' does not implement 'Hashable')
```

<!-- test: an-array-of-arrays-decides-the-inner-conformance-first -->
### A nested array's conformance is decided element-first
```maxon
typealias Val = int(i64.min to i64.max)
typealias IntArr = Array with Val
typealias IntArrArr = Array with IntArr

function main() returns ExitCode
	var outer = IntArrArr.create()
	var inner = IntArr.create()
	inner.push(3)
	outer.push(inner)
	var other = IntArrArr.create()
	var innerOther = IntArr.create()
	innerOther.push(3)
	other.push(innerOther)
	let h = outer.hash()
	if h == 0 'hashIsZero'
		return 1
	end 'hashIsZero'
	return 42
end 'main'
```
```exitcode
42
```
