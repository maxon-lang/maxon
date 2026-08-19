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
re-interned per enclosing instantiation, so `Bag with String` is what mints `Box with String`. The clone
inside `Bag`'s shared body therefore has to reach the SAME per-instance cloner the top-level case does.

The clone and the source are both live at the read, so a cloner that blitted the element POINTER — rather
than allocating a fresh box per element — would hand two owners one box, and the second `__mm_decref` would
free what the first already freed. ⚠ **WHAT THIS CASE DOES *NOT* PIN IS THE PAYLOAD'S OWNERSHIP, AND AN
EARLIER DRAFT OF THIS PARAGRAPH CLAIMED IT DID.** It named `__destruct_Box_String` as the refcount check that
would catch the mistake; this program emits no such symbol. MEASURED with `--emit-ir-runtime`: the element
array is `__managed_create(8, __mm_decref)`, `Box.create` allocates each box with a ZERO destructor, and the
cloner it reaches is `__clone_Box_T<hash>` — `__mm_alloc` + blit, no incref. The box identity IS pinned here
(a shared box would double-free); the `String` inside it is neither retained nor released on either side, and
its literal lives in `.rdata`, so no exit code here can distinguish an owned payload from a borrowed one.
See `MmRuntime.synthesizeGenericInstanceCloner`'s header for that measurement and for the pre-existing
borrow hole it belongs to.

⚠ The element's `String` is read only through the box's scalar `tag`, not through `e.v`. A shared body's
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

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.add("a boxed string held by a generic bag", tag: 4)

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
