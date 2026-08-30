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
- a `Map` KEY or a `Set` ELEMENT, where the same verdict decides whether the array reduces to a conformer
  name the `where` clause can discharge. Since `Map` (W41) and `Set` (W90) stopped being synthesized there
  is no container-specific key gate left: the refusal is the ordinary **E3017**, at the instantiation, and it
  names the constraint the argument failed rather than a roster it is missing from.

A copy of the walk at any one of those doors is a silent wrong answer in either direction: `a == b` refused
while the same array is still stamped with `__witness_Array.*` and admitted as a key, or the reverse.
Neither is a compile error and neither shows up as a failing test, which is why the cases below hold all
three doors to one clause.

⛔ **AND THE CLAUSE IS NOT THE WHOLE STORY AT A MANAGED ELEMENT.** `Array with String` SATISFIES this clause
and is still not a usable hash key on this compiler, because one shared `__witness_Array.*` table serves
every element type and `Array.hash`/`Array.equals` therefore compare the buffer's RAW BYTES
(`array-hashable.md`'s own Documentation says so; E3128's registry entry states the premise). The
measurement, and what currently stands in that program's way instead, are in
`error.a-map-key-array-is-refused-for-its-element` below.

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

<!-- test: the-cloneable-witness-route-is-opened-by-the-instance-dictionary -->
### An existential `Cloneable` reaches `Array.clone`'s body with no concretely-typed call site

⭐⭐ **THIS CASE PINNED THE OPAQUE COPY GATE FOR TWO RUNGS AND WAS NEVER THE GATE'S TO PIN.** It stood in the
`Map` case's shelving note as a shell transcript, and a transcript is the one form of evidence nothing
re-runs. The program calls `.clone()` at NO concretely-typed site: it reaches `Array.clone`'s shared body
purely through the `Cloneable` witness that `type Array … implements … Cloneable` promises unconditionally.
The gate spoke first, so the gate got the credit.

⛔⛔ **W90 MEASURED THAT IT WAS NOT THE GATE — with `requireOpaqueArrayCopyable` probe-disabled, this program
was STILL REFUSED, by E3128 at `stdlib/Array.maxon:143:18`.** A conforming generic type shared ONE witness
table across every instantiation, so a dispatch through that table had no instantiation to take a layout
descriptor from, and an `Array` therefore could not serve `Cloneable` at ANY element.

⭐⭐ **AND G19 OPENED IT, WHICH IS WHY THIS CASE NOW RUNS RATHER THAN REFUSING.** A witness table minted for a
CONCRETE instance of a dictionary-carrying conformer is keyed by that instance — `__witness_Array_StrArr.Cloneable`
— and its slot points at an adapter that calls the one shared `Array.clone` with THAT instance's own layout
descriptor. The dispatch has an instantiation to take the dictionary from because the TABLE has one. E3128
survives for the residue it is still true of: a table demanded where no concrete instance exists, which is
what `stdlib/Array.maxon`'s `ArrayIterator with Element` inside `Array.withIterator()` still is.

⛔⛔ **THE OWNERSHIP WORD HAD TO MOVE WITH THE LABEL, AND IT WAS A MEASURED LEAK BEFORE IT DID (G19).** A
table's `destroyFunc@8` used to be read off the CONFORMER NAME (`existentialDestroyCallee("Array")`), which
names a drop that does not know the element. Widening this same `Array with (Array with String)` into a
`Cloneable` and dropping it exited **101**; the identical program over `Array with String` exited 0, because
a String LITERAL element is immortal `.rdata` and hid the missing walk. A per-instance table names the
instance, so it now names the instance's own drop (`ProgramSignatures.instanceDropCallee`) — which is why
this case's element is a heap `Array with String` and not a literal.

⭐⭐ **G18 IS WHERE THE ORDER FINALLY CHANGED, AND NOTHING WAS WEAKENED TO MAKE IT.** A managed-element
container now has a per-instance one-argument cloner thunk, so `Array with (Array with String)` is
deep-cloneable and the copy gate has nothing to say about it. What is left speaking is the diagnostic that
was holding this door shut all along.

⭐⭐ **AND THE ROUTE THE GATE GENUINELY DID HOLD UP IS NOW CORRECT RATHER THAN MERELY REFUSED.** Same W90
probe, the CONCRETE spelling — `typealias Nested = Array with (Array with String)` followed by a plain
`n.clone()` — **COMPILED and then SEGFAULTED (139)** on the double free: the element's `copyFunc@32` was 0,
so the copy byte-blitted a managed pointer and both records cascaded over the same elements. G18 answers
that with the missing cloner instead of with the refusal, and
`array-clone-managed-elements.clone-of-an-array-of-string-arrays-is-deep` is that exact program, running.
⇒ *"the library body's whole-program refusal is the only thing standing there"* is retired, and so is the
over-refusal it excused: a program that COPIES NOTHING (`for … in` over a nested array, an `isEmpty()`, a
`clear()`) used to be refused too, because the refusal fires where `stdlib/Array.maxon:145`'s
`managed.slice(0, len)` IS and that body is compiled once for the whole program.

⚠ **A stdlib-body refusal is still BLAMED AT THE USER'S OWN CONSTRUCT, and the library line survives as a
NOTE** — the arrangement the remaining opaque-gate cases print. It is RAISED at `stdlib/Array.maxon:165:32`,
a line no user wrote, and REPORTED at the `typealias` whose instantiation made the element uncopyable. The
library location reads REPO-RELATIVE because the runner rewrites the compiler's absolute `stdlib/` root the
way it already rewrites a staged fragment's path (`SpecTestRunner.rewriteStdlibPaths`). Only the NOTE carries
the library's line number, so an edit above `stdlib/Array.maxon:145` still moves those expectations — a real
cost, and the same one the four `/specs` cases pinning `Array.maxon:413`'s panic already pay. The cases that
still print it are `array-clone-managed-elements.error.clone-of-a-struct-holding-a-compiler-owned-handle-is-refused`,
`generic-instance-clone.error.a-container-of-opaque-element-containers-is-refused`,
`generic-type-nested-array-typealias.opaque-copy-uncopyable-instantiation-rejected` and
`typealias-file-scope.error.contested-generic-alias-at-the-opaque-copy-gate` — each now over an element
NOTHING can clone (an OS handle), because that is the only thing the gate still refuses.

⚠ **TWO CASES HAVE LEFT THAT LIST BY BEING FIXED RATHER THAN MOVED, AND BOTH ARE WORTH THE LINE.**
`generic-type-substitution.error.bare-generic-name-nesting-is-not-deep-cloneable` went at W162, which gave a
non-`Array` generic instance a per-instance cloner; this one goes at G18, which gave the two element-bearing
records theirs. The sentence has narrowed once per rung, in the same direction each time: what it names is
now a compiler-owned aggregate, a base-struct-less generic instance with no runtime copy of its own, an
existential, or a type owning one of those — and nothing that merely needed a cloner nobody had written yet.
```maxon
typealias StrArr = Array with String
typealias Nested = Array with StrArr

function copyIt(c Cloneable) returns Cloneable
	return c.clone()
end 'copyIt'

function main() returns ExitCode
	var src = Nested.create()
	var inner = StrArr.create()
	inner.push("ab")
	src.push(inner)
	_ = copyIt(src)
	print("cloned\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
cloned
```

<!-- test: error.the-suppression-may-not-read-a-weaker-index-than-the-report -->
### A `where` clause satisfied by an EXTENSION still refuses the copy gate

⛔⛔ **W90's SUPPRESSION AND E3017's REPORT READ TWO DIFFERENT CONFORMANCE INDEXES, AND THE WEAKER ONE
BELONGS TO THE SUPPRESSION — WHICH IS THE DIRECTION THAT FAILS SILENTLY.** The case above is the copy gate
speaking when it should; this is the copy gate STAYING SILENT when it should not, and it is the same
narrowing seen from the side its header got backwards.

`instantiationViolatesItsOwnWhereClause` withholds the refusal whenever the instantiation's own `where`
clause is unmet, on the ground that E3017 will speak instead. That is sound only while the suppression's
index is a SUPERSET of the report's. It was a strict SUBSET: the suppression reads
`ProgramSignatures.sweptConformanceIndex`, which is `StructLayout.conformsTo` — the `type` declaration's own
`implements` clause and nothing else — while E3017 reads `project.conformances`, which ALSO holds every
`extension <T> implements <I>` the real parse records (`Parser.recordExtensionConformance`). A weaker index
yields MORE `unmet` verdicts, not fewer, so a conformance only an extension declares made the suppression
fire on an instantiation E3017 then found perfectly satisfied — and nothing spoke at all.

⇒ MEASURED at review, on this program over its original `Array with String` element: it compiled with **no
diagnostic** and **ACCESS-VIOLATED (0xC0000005)** on the byte-blitted managed pointer.

⚠ **THE CONTROL IS ONE WORD.** The same program with `where Element is Cloneable` — an interface
`type Array … implements … Cloneable` declares on the TYPE, so the swept index HAS it — is refused on the
merge base and on this tree alike. Only the extension-declared clause moved, so only the extension clause
can be the cause.

⭐ The cure is `ProgramSignatures.extensionDeclaredConformances`, filed by the extension fold BEFORE the
per-conformer `where` verdict: an over-grant there can only withhold a suppression and restore a loud
over-refusal, where an under-grant is this silent accept.

⛔⛔ **THE CONFORMER MOVED FROM `Array` TO A USER STRUCT AT G18, AND IT HAD TO — THE SUBJECT DID NOT MOVE,
THE SCAFFOLDING DID.** This case needs an element the copy gate genuinely refuses, so that a WITHHELD
refusal is observable, AND it needs the offending instance to be one the program never WROTE (`Array with
Handle`, minted by `Container`'s inner `typealias ElementArray = Array with Element`) so that the refusal is
blamed at the `NestedContainer` line the `where` clause hangs off. Its original element was
`Array with String`, and BOTH halves of that stopped working at once: a managed-element container is
deep-cloneable now, and any nested spelling that is still uncopyable is uncopyable at a level the author
spelled out loud — `typealias HandleArray = Array with __ManagedFile` is refused AT ITS OWN LINE, which
blames the alias and never reaches `Container`'s clause at all (measured, both spellings). An OS handle
behind ONE user struct restores both: `Array with Handle` is uncopyable, is minted by the substitution, and
`Handle` takes the extension-declared `Sizer` the suppression is about.

⚠ **WHAT THAT COSTS IS THE CORPUS PROVENANCE, AND IT IS WORTH SAYING RATHER THAN LOSING.** The old shape was
`extension Array implements Sizer`, which is exactly how `Array`'s own `Hashable`/`Equatable` are declared
(`stdlib/Array.maxon:668`) — so the arrangement under test was the corpus's own and not a contrivance.
`extension Handle implements Sizer` is the same MECHANISM (a conformance in `project.conformances` and not in
`StructLayout.conformsTo`) on a type the corpus does not happen to declare. `Sizer` rather than `Hashable`
for the original reason: `Hashable` is ALSO granted intrinsically to an array
(`isIntrinsicBuiltinConformance`'s row), which would have hidden the divergence behind an answer both
indexes agree on.
```maxon
typealias ExitCode = int(0 to 125)
typealias Integer = int(i64.min to i64.max)

type Handle
	export var f as __ManagedFile

	export static function create(f __ManagedFile) returns Self
		return Self{f: f}
	end 'create'
end 'Handle'

interface Sizer
	function size() returns Integer
end 'Sizer'

extension Handle implements Sizer
	function size() returns Integer
		return 3
	end 'size'
end 'Handle'

type Container uses Element where Element is Sizer
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function duplicate() returns Self
		return Self{ items: self.items.clone() }
	end 'duplicate'
end 'Container'

typealias NestedContainer = Container with Handle

function main() returns ExitCode
	var nc = NestedContainer.create()
	nc.push(Handle.create(try __ManagedFile.openRead(b"DATA.BIN".managed) otherwise return 3))
	let dup = nc.duplicate()
	let n = dup.items.count()
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:41:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate or a base-struct-less generic instance with no runtime copy of its own (`__ManagedFile`, a `Vector`), a value held at an interface type, or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported (P1.7 slice 3b-vi-b, W162, W173, G18).
note: stdlib/Array.maxon:165:32: raised inside the library, on behalf of the construct above
```

<!-- test: error.a-map-key-array-is-refused-for-its-element -->
### A `Map` key array is refused for its ELEMENT, not as an unserved key type

⭐⭐ **UN-SHELVED BY W90, AND WHAT MOVED WAS *WHICH REGISTRY ROWS A REFUSAL MAY READ* — NOT THE COPY GATE'S
SUBJECT.** This case spent two rungs disabled because `Parser.requireOpaqueArrayCopyable` spoke first and
its refusal is a `ParseError`: the file stopped, `checkWhereConstraints` short-circuited on
`projectHasErrors`, and the two E3017s below — the user's actual mistake — never spoke at all. A consequence
did not merely out-word its cause here, it SILENCED it.

The gate's offender was `Array with (Array with Opaque)`, an instance no author wrote: `Map`'s own
`typealias KeyArray = Array with Key` substituted with this key. And the instantiation that minted it —
`Map with (OpaqueArr, Val)` — is the very one E3017 owns. An instantiation the constraint check refuses
describes a program that will not exist, so the inner aliases it substitutes are not facts about the
program, and a REFUSAL may not read them (`ProgramSignatures.kindIsARefusal`, whose other bullet is A4q's
speculative rows — same consequence, unrelated origin). The copy gate itself is unchanged and still refuses
everything it refused before, at the same lines: `error.the-opaque-copy-gate-is-reached-through-the-cloneable-witness`
above and the four cases it lists are all byte-identical across that change.

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

⛔⛔ **AND THE SISTER PROGRAM THIS BLOCK USED TO OFFER AS THE CURE'S ACCEPTANCE IS STILL REFUSED, ON PURPOSE
— ITS PREMISE WAS MEASURED FALSE.** The standing note read: *"`typealias StrArrMap = Map with (Array with
String, Val)` — a key type that satisfies `Hashable` and `Equatable` perfectly well, and which the bootstrap
oracle compiles — takes the SAME refusal … so the gate refuses valid ones."* Its instantiation IS
constraint-satisfying, so W90's narrowing deliberately leaves it refused, and admitting it would be a WORSE
answer than the one it replaces. MEASURED, with the copy gate probe-disabled on this tree: the program
compiles, and then two arrays each holding `"a"` answer `equals` **false** where the oracle answers **true**,
so `m.contains(probe)` misses a key the map holds. That is `array-hashable.md`'s documented design working as
designed — one shared `__witness_Array.*` table serves every element type, so `Array.hash`/`Array.equals`
compare the buffer's RAW BYTES, which for a managed element are heap pointers (E3128's own doc states the
premise). ⇒ shv2's `Array` satisfies the `Hashable` CONSTRAINT and not the `Hashable` SEMANTICS at a managed
element, and `Map with (Array with String, …)` is unserved until that is cured. The copy gate is standing in
the right doorway for the wrong reason; removing it without curing the conformance would trade a compile
error for a `Map` whose keys silently never match.
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
### A `Set` key array is refused by the ORDINARY `where`-constraint rule, at the instantiation

⭐ **THE SENTENCE MOVED WHEN `Set` STOPPED BEING SYNTHESIZED (W90), EXACTLY AS IT DID FOR `Map` AT W41, AND
THE RULE IT NOW QUOTES IS THE GENERAL ONE.** This used to be a bespoke `E2015` the builtin set's own key gate
wrote at the `create()`. `Set` is `stdlib/Set.maxon` now, declared `where Element is Hashable and Equatable`
— so the refusal is the ordinary `where`-constraint refusal every generic gets, at the INSTANTIATION. That is
a better answer twice over: it NAMES the constraint that failed, and it is one rule instead of a
container-specific copy of one.

⚠ **BOTH constraints are reported, and that is the clause count rather than a duplicate.** `Element` is
constrained twice and this key discharges neither, so `checkOneInstantiation` reports each. The
`Array`-element reasoning this case is named for has not gone anywhere — it is why an array of `Opaque`
reduces to a conformer name nothing claims (`instanceConformerName`'s conditional `Array` arm), and the
sibling `E4006` cases above still state it in full.

⛔⛔ **`Array_Opaque` IS THE COMPILER'S MINT, NOT A SPELLING ANY AUTHOR WROTE, AND IT IS PINNED HERE ONLY
BECAUSE IT IS WHAT THE COMPILER SAYS.** The key is spelled INLINE, so no `typealias` names the instance and
`ProgramSignatures.instanceDisplayName` falls back to the canonical mint — where the `Map` twin above, whose
key is aliased, correctly prints `OpaqueArr`. It is NOT an `Array` fault and not W90's: MEASURED on the same
tree with no array anywhere, `typealias S = Set with (Box with Opaque)` over a user `type Box uses T` answers
*"Type 'Box_Opaque' does not satisfy constraint 'Hashable'"*. The display map is filled from `typealias`
declarations and an inline `Base with Args` is a spelling the author DID write, so the miss is a gap rather
than the documented "compiler minted it, there is nothing to quote" answer.
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
error E3017: <fragment>:12:11: Type 'Array_Opaque' does not satisfy constraint 'Hashable' required by type parameter 'Element' of 'Set'
error E3017: <fragment>:12:11: Type 'Array_Opaque' does not satisfy constraint 'Equatable' required by type parameter 'Element' of 'Set'
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
