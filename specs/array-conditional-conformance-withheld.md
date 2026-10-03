---
feature: array-conditional-conformance-withheld
status: experimental
keywords: [array, hashable, equatable, conditional, extension, where, map, set, diagnostics]
category: type-system
---

# `Array`'s `Hashable`/`Equatable` is CONDITIONAL — what a withheld one is refused with

## Documentation

`Array` conforms to `Hashable` and `Equatable` only `where Element is Hashable and Equatable`
(`stdlib/Array.maxon`, and `specs/array-hashable.md` for what the conformance DOES). An array whose
element does not satisfy that clause has no `hash`, no `equals` and no `==`/`!=`, and cannot be a `Map` or
`Set` key.

This file is the WITHHELD half; the positive cases are `array-hashable.md`'s.

**One clause, one walk, three doors.** The element's conformance is decided by a single predicate
(`ProgramSignatures.arrayElementConstraintCheck`) under a single list (`Array`'s own intrinsic conformance
row), and three doors consume its verdict:

- the METHOD surface — `arr.hash()` / `a.equals(b)` — refused with **E4006**, the sentence a user's own
  conditional extension is withheld with, naming the unmet interface and the element that failed it;
- the `==` / `!=` OPERATOR, which IS `Array.equals`, so it is refused with the same sentence at the
  operator's own span;
- a `Map` KEY or a `Set` ELEMENT, where the same verdict decides whether the array reduces to a conformer
  name the `where` clause can discharge. `Map` and `Set` are ordinary generics (`stdlib/Map.maxon`,
  `stdlib/Set.maxon`) with no container-specific key gate: the refusal is the ordinary **E3017**, at the
  instantiation, and it names the constraint the argument failed.

A copy of the walk at any one of those doors is a silent wrong answer in either direction: `a == b` refused
while the same array is still stamped with `__witness_Array.*` and admitted as a key, or the reverse.
Neither is a compile error and neither shows up as a failing test, which is why the cases below hold all
three doors to one clause.

⛔ **AND THE CLAUSE IS NOT THE WHOLE STORY AT A MANAGED ELEMENT.** `Array with String` SATISFIES this clause
and is still not a usable hash key on this compiler, because one shared `__witness_Array.*` table serves
every element type and `Array.hash`/`Array.equals` therefore compare the buffer's RAW BYTES
(`array-hashable.md`'s own Documentation says so; E3128's registry entry states the premise). What stands
in that program's way is in `error.a-map-key-array-is-refused-for-its-element` below.

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

⭐⭐ **THIS CASE IS NOT THE OPAQUE COPY GATE'S TO PIN.** The program calls `.clone()` at NO concretely-typed
site: it reaches `Array.clone`'s shared body purely through the `Cloneable` witness that
`type Array … implements … Cloneable` promises unconditionally.

⛔⛔ **A WITNESS TABLE SHARED ACROSS EVERY INSTANTIATION HAS NO INSTANTIATION TO TAKE A LAYOUT DESCRIPTOR
FROM**, so a dispatch through one cannot serve `Cloneable` at any element, and E3128 refuses such a table.

⭐⭐ **THIS CASE RUNS RATHER THAN REFUSING BECAUSE ITS TABLE IS PER-INSTANCE.** A witness table minted for a
CONCRETE instance of a dictionary-carrying conformer is keyed by that instance — `__witness_Array_StrArr.Cloneable`
— and its slot points at an adapter that calls the one shared `Array.clone` with THAT instance's own layout
descriptor. The dispatch has an instantiation to take the dictionary from because the TABLE has one. E3128
refuses the residue: a table demanded where no concrete instance exists, which is what
`stdlib/Array.maxon`'s `ArrayIterator with Element` inside `Array.withIterator()` is.

⛔⛔ **THE OWNERSHIP WORD FOLLOWS THE LABEL.** A per-instance table's `destroyFunc@8` names the instance's own
drop (`ProgramSignatures.instanceDropCallee`); a drop named off the CONFORMER (`existentialDestroyCallee("Array")`)
does not know the element and leaks it. This case's element is a heap `Array with String` and not a literal
because a String LITERAL element is immortal `.rdata` and would hide a missing walk.

⭐⭐ **THE COPY GATE HAS NOTHING TO SAY HERE.** A managed-element container has a per-instance one-argument
cloner thunk, so `Array with (Array with String)` is deep-cloneable.

⭐⭐ **THE CONCRETE ROUTE IS CORRECT RATHER THAN REFUSED.** The CONCRETE spelling — `typealias Nested = Array
with (Array with String)` followed by a plain `n.clone()` — copies each element through that cloner rather
than byte-blitting a managed pointer, and
`array-clone-managed-elements.clone-of-an-array-of-string-arrays-is-deep` is that exact program, running. A
program that COPIES NOTHING (`for … in` over a nested array, an `isEmpty()`, a `clear()`) is accepted too,
although `stdlib/Array.maxon:147`'s `managed.slice(0, len)` is in a body compiled once for the whole program.

⚠ **A stdlib-body refusal is BLAMED AT THE USER'S OWN CONSTRUCT, and the library line is kept as a
NOTE** — the arrangement the opaque-gate cases print. It is RAISED at `stdlib/Array.maxon:79:32`,
a line no user wrote, and REPORTED at the `typealias` whose instantiation made the element uncopyable. The
library location reads REPO-RELATIVE because the runner rewrites the compiler's absolute `stdlib/` root the
way it rewrites a staged fragment's path (`SpecTestRunner.rewriteSourceTierPaths`). Only the NOTE carries
the library's line number, so an edit above `stdlib/Array.maxon:147` moves those expectations — a real
cost, and the same one the four cases pinning `Array.maxon:407`'s panic pay. The cases that print it are
`array-clone-managed-elements.error.clone-of-a-struct-holding-a-compiler-owned-handle-is-refused`,
`generic-instance-clone.error.a-container-of-opaque-element-containers-is-refused`,
`generic-type-nested-array-typealias.opaque-copy-uncopyable-instantiation-rejected` and
`typealias-file-scope.error.contested-generic-alias-at-the-opaque-copy-gate` — each over an element
NOTHING can clone (an OS handle).

⚠ **WHAT THE GATE REFUSES** is a compiler-owned aggregate, a base-struct-less generic instance with no
runtime copy of its own, an existential, or a type owning one of those — and nothing else.
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

⛔⛔ **THE SUPPRESSION MAY NOT READ A WEAKER CONFORMANCE INDEX THAN E3017's REPORT — THAT IS THE DIRECTION
THAT FAILS SILENTLY.** The case above is the copy gate speaking when it should; this one pins it speaking
where a withheld refusal would leave nothing said.

`instantiationViolatesItsOwnWhereClause` withholds the refusal whenever the instantiation's own `where`
clause is unmet, on the ground that E3017 will speak instead. That is sound only while the suppression's
index is a SUPERSET of the report's. E3017 reads `project.conformances`, which holds every
`extension <T> implements <I>` the real parse records (`Parser.recordExtensionConformance`) as well as each
`type` declaration's own `implements` clause; `ProgramSignatures.sweptConformanceIndex` —
`StructLayout.conformsTo` — holds only the latter. A weaker index yields MORE `unmet` verdicts, not fewer, so
a suppression reading it alone fires on an instantiation E3017 then finds perfectly satisfied, and the
program compiles with no diagnostic over a byte-blitted managed pointer.

⚠ **THE CONTROL IS ONE WORD.** The same program with `where Element is Cloneable` — an interface
`type Array … implements … Cloneable` declares on the TYPE, so the swept index HAS it — is refused whichever
index is read, so only the extension-declared clause exercises the divergence.

⭐ The suppression's index includes `ProgramSignatures.extensionDeclaredConformances`, filed by the extension
fold BEFORE the per-conformer `where` verdict: an over-grant there can only withhold a suppression and
restore a loud over-refusal, where an under-grant is a silent accept.

⛔⛔ **THE CONFORMER IS A USER STRUCT, NOT `Array`, AND IT HAS TO BE.** This case needs an element the copy
gate genuinely refuses, so that a WITHHELD refusal is observable, AND it needs the offending instance to be
one the program never WROTE (`Array with Handle`, minted by `Container`'s inner
`typealias ElementArray = Array with Element`) so that the refusal is blamed at the `NestedContainer` line
the `where` clause hangs off. A managed-element `Array` is deep-cloneable, and any nested spelling that is
uncopyable is uncopyable at a level the author spelled out loud — `typealias HandleArray = Array with
__ManagedFile` is refused AT ITS OWN LINE, which blames the alias and never reaches `Container`'s clause at
all. An OS handle behind ONE user struct gives both: `Array with Handle` is uncopyable, is minted by the
substitution, and `Handle` takes the extension-declared `Sizer` the suppression is about.

⚠ **`extension Handle implements Sizer` IS THE MECHANISM `Array`'s OWN `Hashable`/`Equatable` ARE DECLARED
WITH** (`stdlib/Array.maxon:668`): a conformance in `project.conformances` and not in
`StructLayout.conformsTo`. `Sizer` rather than `Hashable` because `Hashable` is ALSO granted intrinsically
to an array (`isIntrinsicBuiltinConformance`'s row), which would hide the divergence behind an answer both
indexes agree on.
```maxon
typealias ExitCode = int(0 to 125)
typealias Integer = int(i64.min to i64.max)

type Handle
	export var f as __ManagedFile

	static function create(f __ManagedFile) returns Self
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

	static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	function push(item Element)
		self.items.push(item)
	end 'push'

	function duplicate() returns Self
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
error E2015: <fragment>:41:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate (`__ManagedFile`), a base-struct-less generic instance with no runtime copy of its own, an ELEMENT held at an interface type (an element slot is one machine word and a fat pointer is two), or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, a record holding an interface-typed FIELD, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported.
note: stdlib/Array.maxon:79:32: raised inside the library, on behalf of the construct above
```

<!-- test: error.a-map-key-array-is-refused-for-its-element -->
### A `Map` key array is refused for its ELEMENT, not as an unserved key type

⭐⭐ **WHAT DECIDES THIS CASE IS *WHICH REGISTRY ROWS A REFUSAL MAY READ* — NOT THE COPY GATE'S
SUBJECT.** Were `Parser.requireOpaqueArrayCopyable` to speak first, its refusal is a `ParseError`: the file
would stop, `checkWhereConstraints` would short-circuit on `projectHasErrors`, and the two E3017s below — the
user's actual mistake — would never speak. A consequence would not merely out-word its cause, it would
SILENCE it.

The gate's offender is `Array with (Array with Opaque)`, an instance no author wrote: `Map`'s own
`typealias KeyArray = Array with Key` substituted with this key. And the instantiation that mints it —
`Map with (OpaqueArr, Val)` — is the very one E3017 owns. An instantiation the constraint check refuses
describes a program that will not exist, so the inner aliases it substitutes are not facts about the
program, and a REFUSAL may not read them (`ProgramSignatures.kindIsARefusal`, whose other bullet is the
speculative rows — same consequence, unrelated origin).

⭐ **THE RULE THIS QUOTES IS THE GENERAL ONE.** `Map` is `stdlib/Map.maxon`, declared
`where Key is Hashable and Equatable` — so the refusal is the ordinary `where`-constraint refusal every
generic gets, at the instantiation. It NAMES the constraint that failed, and it is one rule rather than a
container-specific copy of one.

⚠ **BOTH constraints are reported, and that is the clause count rather than a duplicate.** `Key` is
constrained twice; `OpaqueArr` discharges neither, so `checkOneInstantiation` reports each. The
`Array`-element reasoning this case is named for is why `OpaqueArr`
reduces to a conformer name nothing claims (`instanceConformerName`'s conditional `Array` arm), and
the sibling `E4006` cases above state it in full.

⛔⛔ **A KEY THAT SATISFIES THE CLAUSE AT A MANAGED ELEMENT IS STILL REFUSED, ON PURPOSE.**
`typealias StrArrMap = Map with (Array with String, Val)` IS constraint-satisfying, and the copy gate leaves
it refused, because admitting it would be a WRONG answer: without the gate the program compiles, and then two
arrays each holding `"a"` answer `equals` **false**, so `m.contains(probe)` misses a key the map holds. That
is `array-hashable.md`'s documented design working as
designed — one shared `__witness_Array.*` table serves every element type, so `Array.hash`/`Array.equals`
compare the buffer's RAW BYTES, which for a managed element are heap pointers (E3128's own doc states the
premise). ⇒ the compiler's `Array` satisfies the `Hashable` CONSTRAINT and not the `Hashable` SEMANTICS at a managed
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

⭐ **THE RULE THIS QUOTES IS THE GENERAL ONE, EXACTLY AS FOR `Map`.** `Set` is `stdlib/Set.maxon`, declared
`where Element is Hashable and Equatable` — so the refusal is the ordinary `where`-constraint refusal every
generic gets, at the INSTANTIATION. It NAMES the constraint that failed, and it is one rule rather than a
container-specific copy of one.

⚠ **BOTH constraints are reported, and that is the clause count rather than a duplicate.** `Element` is
constrained twice and this key discharges neither, so `checkOneInstantiation` reports each. The
`Array`-element reasoning this case is named for is why an array of `Opaque`
reduces to a conformer name nothing claims (`instanceConformerName`'s conditional `Array` arm), and the
sibling `E4006` cases above state it in full.

⛔⛔ **`Array_Opaque` IS THE COMPILER'S MINT, NOT A SPELLING ANY AUTHOR WROTE, AND IT IS PINNED HERE ONLY
BECAUSE IT IS WHAT THE COMPILER SAYS.** The key is spelled INLINE, so no `typealias` names the instance and
`ProgramSignatures.instanceDisplayName` falls back to the canonical mint — where the `Map` twin above, whose
key is aliased, correctly prints `OpaqueArr`. It is NOT an `Array` fault: with no array anywhere,
`typealias S = Set with (Box with Opaque)` over a user `type Box uses T` answers
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

⚠⚠ **THIS CASE'S ID IS FALSE IN ITS SECOND HALF — read the heading, not the name.** No key type is "a
later slice": **any `Hashable + Equatable` type is a `Map` key** — `map.md`'s `a-user-type-can-be-a-map-key`
pins a user `Point` doing exactly that. What is refused here is a type that implements NEITHER, which the
ordinary `where`-constraint check reports against `Map`'s own declared clause.

The id stands rather than being corrected because it names two COMMITTED golden fragments, one of them
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
