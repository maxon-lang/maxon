---
feature: generic-instance-clone
status: stable
keywords: [generics, clone, deep-clone, monomorphization, managed]
category: memory
---
# Deep Clone of a Non-`Array` Generic Instance

## Documentation

A monomorphized generic instance (`Box with String`) is a base-struct box whose SUBSTITUTED fields own
managed heap, so it needs a per-instance deep cloner `__clone_<mangled>` exactly as it needs the
per-instance destructor cascade `__destruct_<mangled>`. It is the clone twin of that cascade: allocate a
fresh box, blit it, then replace each blit-copied shallow pointer with an independent copy through the
substituted field's own clone strategy.

Without it an `Array with (Box with String)` cannot be copied at all — `slice`/`clone`/`append` need a
single `(box) -> newBox` cloner for the element to bake into the layout descriptor's `copyFunc@32`, and a
non-`Array` instance had none. The refusal is raised inside `stdlib/Array.maxon`'s shared body and blamed
at the instantiation the user wrote.

## Tests

<!-- test: clone-array-of-generic-instances -->
### An array of generic instances clones deeply, and the clone outlives the source
The helper's `src` (and every `Box with String` in it) is freed when the helper returns, so only a deep,
independent clone can still be read. A cloner that blitted the element pointer would leave the clone
pointing at a freed box.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

typealias StrBox = Box with String
typealias BoxArray = Array with StrBox

function makeClone() returns BoxArray
	var src = BoxArray.create()
	src.push(StrBox.create("first boxed string, long enough to live on the heap", tag: 10))
	src.push(StrBox.create("second boxed string, long enough to live on the heap", tag: 20))
	return src.clone()
end 'makeClone'

function main() returns ExitCode
	let copy = makeClone()

	if copy.count() != 2 'badCount'
		return 91
	end 'badCount'

	let b = try copy.get(1) otherwise return 92

	if not b.v.equals("second boxed string, long enough to live on the heap") 'badString'
		return 93
	end 'badString'

	return b.tag
end 'main'
```
```exitcode
20
```

<!-- test: clone-is-independent-of-a-mutated-source -->
### Replacing the source's element does not change the clone
Both arrays are alive at once and the source's slot 0 is REPLACED after the clone was taken, which
releases the box the source held there. The clone still reads its own box, with its own `String`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

typealias StrBox = Box with String
typealias BoxArray = Array with StrBox

function main() returns ExitCode
	var src = BoxArray.create()
	src.push(StrBox.create("original boxed string, long enough to live on the heap", tag: 7))

	let copy = src.clone()

	try src.set(0, value: StrBox.create("replaced", tag: 99)) otherwise return 91

	let theirs = try src.get(0) otherwise return 92
	if theirs.tag != 99 'sourceNotReplaced'
		return 93
	end 'sourceNotReplaced'
	if not theirs.v.equals("replaced") 'sourceStringNotReplaced'
		return 94
	end 'sourceStringNotReplaced'

	let mine = try copy.get(0) otherwise return 95
	if not mine.v.equals("original boxed string, long enough to live on the heap") 'cloneFollowedTheSource'
		return 96
	end 'cloneFollowedTheSource'

	return mine.tag
end 'main'
```
```exitcode
7
```

<!-- test: clone-of-an-instance-over-the-enclosing-type-parameter -->
### The element instance is written over the ENCLOSING generic's own type parameter
`Bag`'s inner `typealias EBox = Box with Element` names no concrete instance in the shared body; it is
re-interned per enclosing instantiation, so `Bag with Integer` is what mints `Box with Integer`. The clone
inside `Bag`'s shared body therefore has to reach the SAME per-instance cloner the top-level case does.

The clone and the source are both live at the read, so a cloner that blitted the element POINTER — rather
than allocating a fresh box per element — would hand two owners one box, and the second `__mm_decref` would
free what the first already freed. That box IDENTITY is what this case pins, and it pins it whatever the
payload is.

⛔⛔ **IT IS INSTANTIATED AT A SCALAR, AND THAT IS A PROPERTY OF THE LANGUAGE RATHER THAN OF THIS CASE'S
AUTHOR (BATCH41).** The managed spelling of this program — `Bag with String` — is now REFUSED, and its
refusal is `generic-opaque-value-store`'s
`error.a-container-of-records-over-the-enclosing-parameter-is-refused`, which carries the two measurements:
with a heap payload the identical program was a **use-after-free** (`0xC0000005`), and with the store side
taking a reference through `retainFunc@64` it exited **101** instead. The cause is one fact this case can
now state instead of apologising for: **`Bag.create` stamps its element array
`__managed_create(8, __mm_decref)`**, because the element `Box with Element` reads its own bare `T` field
through `typeIsManaged` and is told the field owns nothing — so the box is freed and the field it holds is
not. `__destruct_Box_String` exists, but a shared body can name only the DECLARATION VIEW's destructor.

⇒ **When a layout-descriptor slot carries a nested instance's per-instantiation destructor, this case gets
its managed spelling back and the refusal becomes a runtime program.** Until then a MANAGED payload here
would pin nothing that an exit code can see — an earlier draft of this paragraph believed a `.rdata` literal
would do, and it did not: MEASURED with `--emit-ir-runtime`, `Box.create` allocates each box with a ZERO
destructor and the cloner it reaches is `__clone_Box_T<hash>` — `__mm_alloc` + blit, no incref — so no exit
code could distinguish an owned payload from a borrowed one. See
`MmRuntime.synthesizeGenericInstanceCloner`'s header for that measurement.

⚠ The element's payload is read only through the box's own `tag`, not through `e.v`. A shared body's
method returning an inner alias over a NESTED instance (`Array with (Box with Element)`) hands the caller
`Bag.EBoxArray` unsubstituted, so `e.v` resolves to the bare type parameter and a member call on it
panics `enclosingTypeParamName` — a substitution gap of its own, unrelated to the cloner and reproducible
with `return self.items` and no clone at all.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias EBoxArray = Array with EBox

	var items as EBoxArray

	export static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	export function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	export function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'
end 'Bag'

typealias IntBag = Bag with Integer

function main() returns ExitCode
	var b = IntBag.create()
	b.add(91, tag: 4)

	let c = b.copy()

	if c.count() != 1 'badCount'
		return 91
	end 'badCount'

	let e = try c.get(0) otherwise return 92

	return e.tag
end 'main'
```
```exitcode
4
```

<!-- test: a-struct-owning-a-set-instance-clones -->
### The SOURCE-LEVEL `p.clone()` gate admits a struct owning a declared generic's instance
`requireStructCloneSupported` refused `clone` on any struct owning a `Set`/`Map` field, because a
non-`Array` generic instance had no per-instance cloner to route the field through. `Set` is a DECLARED
generic (`stdlib/Set.maxon`), so `__clone_Set_String` is now a field cascade over its substituted columns —
`elements` through `__managed_clone_managed` + `__str_clone`, `states` and `hashes` through
`__managed_clone` — and a hash table copied column-for-column is a valid independent set.

The source `Bucket` and its Set die inside the helper, so the clone is read alone: a column shared rather
than copied would leave every member pointing at a freed record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringSet = Set with String

type Bucket
	export var members as StringSet
	export var tag as Integer

	export static function create(tag Integer) returns Self
		return Self{members: StringSet.create(), tag: tag}
	end 'create'
end 'Bucket'

function detached() returns Bucket
	var b = Bucket.create(5)
	b.members.insert("a string long enough to force a heap allocation")
	b.members.insert("a second string long enough to force a heap allocation")
	return b.clone()
	// b, its Set, its three columns and both Strings are freed when this function returns
end 'detached'

function main() returns ExitCode
	let c = detached()

	if c.members.count() != 2 'badCount'
		return 91
	end 'badCount'
	if not c.members.contains("a second string long enough to force a heap allocation") 'lostAMember'
		return 92
	end 'lostAMember'

	return c.tag
end 'main'
```
```exitcode
5
```

### A container of OPAQUE-ELEMENT containers is refused, not cloned word-for-word

`containerElementIsManaged` is `typeIsManaged` of the element, and `typeIsManaged` of a bare type parameter
is **false** — not because the element owns nothing, but because a shared body cannot say what it owns. The
two clone doors read that `false` as *"a trivial element, so the record's own words ARE the copy"*, while the
instance the program actually builds stamps `element_drop@24` from the enclosing descriptor and that word can
be live.

⚠ **The door is a container of CONTAINERS, and it is the one the filed row's "not reachable today" missed.**
The OUTER array's element is `Array with Element` — a `genericInstance`, so the outer array is not itself
opaque and takes the CONCRETE copy path rather than the descriptor-reading one. The per-element cloner that
path resolves for the inner array is `__managed_clone`, a word-for-word buffer copy that retains no element,
so the copy and the source then cascade `__str_decref` over the same records. **MEASURED before the refusal
existed: the row count printed and the program died `0xC0000005` at teardown.**

<!-- test: error.a-container-of-opaque-element-containers-is-refused -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Inner = Array with Element
	typealias Outer = Array with Inner
	var rows as Outer

	export function addRow(r Inner)
		rows.push(r)
	end 'addRow'

	export function copyRows() returns Outer
		return rows.clone()
	end 'copyRows'

	export function count() returns Idx
		return rows.count()
	end 'count'

	static function create() returns Self
		return Self{rows: Outer{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String
typealias Strs = Array with String

function main() returns ExitCode
	var b = StrBag.create()
	var r = Strs.create()
	var sb = StringBuilder.create()
	sb.append("a heap row element long enough to allocate")
	r.push(sb.build())
	b.addRow(r)
	let c = b.copyRows()
	print("{c.count()}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:12: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate or a base-struct-less generic instance with no runtime copy of its own (`__ManagedFile`, a `Vector`), a value held at an interface type, or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported (P1.7 slice 3b-vi-b, W162, W173, G18).
note: stdlib/Array.maxon:145:32: raised inside the library, on behalf of the construct above
```

### …and a TRIVIAL instantiation of the identical shape still clones

The false-reject control. The refusal is gated on some instantiation making the opaque element own a record;
where every `with` in the program makes it a scalar, the inner buffers copy correctly word-for-word with no
cloner at all, and refusing them would be a wrong answer about a program that owes nothing. This program is
the one above with `String` replaced by a ranged `int` and nothing else changed.

<!-- test: a-trivial-instantiation-of-the-same-nested-shape-still-clones -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Small = int(0 to 100)

type Bag uses Element
	typealias Inner = Array with Element
	typealias Outer = Array with Inner
	var rows as Outer

	export function addRow(r Inner)
		rows.push(r)
	end 'addRow'

	export function copyRows() returns Outer
		return rows.clone()
	end 'copyRows'

	export function count() returns Idx
		return rows.count()
	end 'count'

	static function create() returns Self
		return Self{rows: Outer{}}
	end 'create'
end 'Bag'

typealias SmallBag = Bag with Small
typealias Smalls = Array with Small

function main() returns ExitCode
	var b = SmallBag.create()
	var r = Smalls.create()
	r.push(41)
	b.addRow(r)
	let c = b.copyRows()
	print("{c.count()}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.member-call-on-an-unsubstituted-element-payload-is-diagnosed -->
### A member call on a value still held at a type parameter is REFUSED, and the refusal names the parameter
⭐ The half `clone-of-an-instance-over-the-enclosing-type-parameter` STEERS AROUND (W181): that case reads
`e.tag` — a concretely-declared `Integer` field — precisely because reading `e.v` took the compiler down, and
a check that declines to ask its own question pins nothing. `Bag.copy() returns EBoxArray` hands the caller
the DECLARATION view (`Array with (Box with Element)`), so `e.v` is still `Bag`'s own opaque `Element` in
`main` — and `main` encloses no declaration at all.

⛔⛔ **THE REFUSAL WAS RIGHT AND THE COMPILER COULD NOT SAY IT.** The phrase builder asked
`enclosingTypeParamName` which of the CALLER's parameters the token names, and at file scope
`self.enclosingType` is empty: `panic at Parser.maxon: enclosingTypeParamName: type-parameter token …
names no parameter of '', which declares 0`. A diagnostic the program had already earned, replaced by a
stack trace — and the emptiness IS the tell, because a parameter's name is a property of the TOKEN (a W14
digest of `(declaring type, parameter name)`) and never of whoever is looking at it. It is now read off
`typeParamOwnerOf`, so the sentence below can be built from any scope.

⚠ **THIS CASE PINS THE DIAGNOSTIC, NOT THE ANSWER.** The SUBSTITUTION is still missing — a correct compiler
substitutes the receiver's `Integer` into that returned type and this program RUNS — and the case that pins
that is `element-payload-through-a-shared-body-return-over-the-enclosing-parameter`, disabled directly below.
When it lands, THIS case is the one that must be re-examined: the E2015 becomes wrong, not merely stale.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias EBoxArray = Array with EBox

	var items as EBoxArray

	export static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	export function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	export function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'
end 'Bag'

typealias IntBag = Bag with Integer

function main() returns ExitCode
	var b = IntBag.create()
	b.add(91, tag: 4)

	let c = b.copy()
	let e = try c.get(0) otherwise return 90
	return 4 as ExitCode if e.v.equals(91) else 70 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:40:30: Unsupported: no requirement named 'equals' is provided by the constraints on type parameter 'Element' — a method call on a value whose type is an interface, or on a constrained type parameter, dispatches through a witness table, so the method has to be one that interface declares
```

<!-- disabled-test: element-payload-through-a-shared-body-return-over-the-enclosing-parameter -->
<!-- the rung that SUBSTITUTES a shared body's inner-GENERIC-alias return at the concrete caller -->
The same program as the case above, as it must eventually RUN. `IntBag` binds `Element` to `Integer`, so
`copy()`'s `Array with (Box with Element)` is an `Array with (Box with Integer)` at this receiver and `e.v`
is an `Integer` — `.equals(91)` is then the ordinary builtin conformance the control case pins, and the
program returns 4.

⛔ **MEASURED at W181, and the reason it is shelved rather than fixed there**: the repair is NOT in the
re-qualifying arm it looks like it should be in. `promoteBareGenericResultToCalleeScope` re-scopes a bare
generic BASE and answers nothing for an alias; the arm below it re-qualifies an INNER (ranged) alias and a
GENERIC alias lives in a different registry. Probing the actual value showed the result of `b.copy()`
arriving tagged **`struct` and named `Box`** — the ELEMENT's base, two resolution steps off the array it
declared — so the type is already wrong before any substitution door sees it, and the fix is in how a shared
body's declared return crosses to a concrete caller. That is the same seam `W178` names.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias EBoxArray = Array with EBox

	var items as EBoxArray

	export static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	export function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	export function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'
end 'Bag'

typealias IntBag = Bag with Integer

function main() returns ExitCode
	var b = IntBag.create()
	b.add(91, tag: 4)

	let c = b.copy()
	let e = try c.get(0) otherwise return 90
	return 4 as ExitCode if e.v.equals(91) else 70 as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: element-payload-through-a-concretely-instantiated-box -->
The FALSE-REJECT CONTROL for the pair above, and the reason they are a set: the identical `.v.equals(…)`
spelling on a box whose argument was concrete all along never crossed a shared body's return, so it compiled
and ran throughout. It is what makes the refusal attributable to the SUBSTITUTION rather than to the
spelling — a `.equals()` on a value of a ranged int alias is an ordinary builtin conformance dispatch, and
this case is what says so. Returns 4.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	export static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let b = IntBox.create(91, tag: 4)
	return 4 as ExitCode if b.v.equals(91) else 70 as ExitCode
end 'main'
```
```exitcode
4
```
