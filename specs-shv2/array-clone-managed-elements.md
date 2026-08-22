---
feature: array-clone-managed-elements
status: stable
keywords: [array, clone, managed, deep-clone, use-after-free]
category: memory
---
# Array Clone Deep-Copies Managed Elements

## Documentation

`Array.clone` on a MANAGED-element array (String / struct / boxed union) produces an INDEPENDENT copy by
deep-cloning each element (`__managed_clone_managed` + the element's `__clone_<E>`), not a COW view. shv2 is
move-only, so an independent copy is a per-element deep clone rather than the reference compilers' incref.
When the source array is later freed, the clone's elements survive because they are separate allocations.

## Tests

<!-- test: clone-managed-source-freed -->
### Clone of struct array, source freed before access
The helper clones a source array of structs-with-String and returns the clone. When the helper returns,
the source array (and its original elements) are freed. Only a deep, independent clone survives.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
		export var name as String
		export var value as Integer

		static function create(name String, value Integer) returns Self
			return Self{name: name, value: value}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

function makeClone() returns ItemArray
		var src = ItemArray.create()
		src.push(Item.create("first clone item long enough for heap", value: 10))
		src.push(Item.create("second clone item long enough for heap", value: 20))
		return src.clone()
		// src is freed when this function returns
end 'makeClone'

function main() returns ExitCode
		let cloned = makeClone()

		if cloned.count() != 2 'badCount'
				return 99
		end 'badCount'

		let item = try cloned.get(1) otherwise Item.create("", value: 0)
		return item.value
end 'main'
```
```exitcode
20
```

<!-- test: clone-managed-three-level-cascade -->
### Clone and drop of a THREE-level managed nesting
`Outer` owns a `Mid` owns a `Leaf` owns a `String`, so both per-type cascades are three deep:
`__clone_Outer` → `__clone_Mid` → `__clone_Leaf` → `__str_clone`, and the same chain on the drop side.
Neither inner cascade is named anywhere the module scan can see — each is reached only THROUGH its
parent — so both needs-closures have to grow the set transitively rather than one level. The source is
freed before the clone is read, which is what makes a missed level observable: a cascade that stopped
short would leave the clone sharing the freed original's `String` rather than owning its own.
```maxon
typealias Integer = int(i64.min to i64.max)

type Leaf
		export var label as String
		export var value as Integer

		static function create(label String, value Integer) returns Self
			return Self{label: label, value: value}
		end 'create'
end 'Leaf'

type Mid
		export var leaf as Leaf

		static function create(leaf Leaf) returns Self
			return Self{leaf: leaf}
		end 'create'
end 'Mid'

type Outer
		export var mid as Mid

		static function create(mid Mid) returns Self
			return Self{mid: mid}
		end 'create'
end 'Outer'

typealias OuterArray = Array with Outer

function makeClone() returns OuterArray
		var src = OuterArray.create()
		src.push(Outer.create(Mid.create(Leaf.create("a nested label long enough to reach the heap", value: 7))))
		return src.clone()
		// src and its whole three-level element graph are freed when this function returns
end 'makeClone'

function main() returns ExitCode
		let cloned = makeClone()

		if cloned.count() != 1 'badCount'
				return 99
		end 'badCount'

		let outer = try cloned.get(0) otherwise Outer.create(Mid.create(Leaf.create("", value: 0)))
		return outer.mid.leaf.value
end 'main'
```
```exitcode
7
```

<!-- test: error.clone-of-a-struct-holding-a-compiler-owned-handle-is-refused -->
⛔⛔ **THE DEEP-CLONE GATE AND THE CLONE STRATEGY HAD TO AGREE ABOUT THE COMPILER'S OWN AGGREGATES, AND THEY
DID NOT (A4n).** `CharacterSet`, `__ManagedFile` and `__ManagedDirectory` have a REGISTERED layout — they need
a nominal identity — but they are declared in no FILE, so `installStructCloners` (which walks the project's
own declarations) synthesizes no `__clone_<T>` for them. `managedNameDropCallee` has always tested the three
names and routed each to its compiler-owned destructor; its clone twin tested none, fell through to the
struct arm, and handed back `__clone___ManagedFile`.

MEASURED on the parent commit's binary, on exactly this program — a compiler PANIC, after the front end had
accepted it:

```
panic at X64Backend.maxon:1905: resolveCallFixups: call to unknown function '__clone___ManagedFile'
```

⇒ The verdict is a REFUSAL and not a gap. An OS HANDLE cannot be deep-copied at all: duplicating one would
hand two owners a
descriptor whose `__mf_destruct` closes once. Both `typeSupportsDeepClone` and `managedNameCloneStrategy` now
ask one `compilerOwnedAggregateOf`, so the gate refuses exactly what the strategy cannot emit — which is what
the gate's own header exists to say — and the front end reports a positioned E2015 where the backend used to
die.

⚠ **THE REFUSAL IS THE LIBRARY'S SINCE ARRH STRUCK `clone` FROM THE `Array` ROSTER, AND BLAME GIVES IT
THE USER'S SPAN BACK** — `arr.clone()` is the library's own declaration now, so this program is refused by the
OPAQUE copy gate inside that body rather than by the concrete gate at the call, and the sentence printed is
the opaque one. What the refusal is POSITIONED at is the user's own instantiation, with `stdlib/Array.maxon`'s
line kept as a `note:`; `specs-shv2/array-conditional-conformance-withheld.md` explains that relocation and
the blame edge once, for all four cases ARRH touched.
```maxon
type Holder
	export var f as __ManagedFile
	export static function create(f __ManagedFile) returns Self
		return Self{f: f}
	end 'create'
end 'Holder'
typealias Holders = Array with Holder
function main() returns ExitCode
	var a = Holders.create()
	a.push(Holder.create(try __ManagedFile.openRead(b"DATA.BIN".managed) otherwise return 3))
	let b = a.clone()
	return b.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate or a base-struct-less generic instance with no runtime copy of its own (`__ManagedFile`, a `Vector`), a value held at an interface type, or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported (P1.7 slice 3b-vi-b, W162, W173, G18).
note: stdlib/Array.maxon:145:32: raised inside the library, on behalf of the construct above
```

<!-- test: clone-of-an-array-of-lists-is-deep -->
### Clone of an array of LISTS, source freed before access
⭐ **A CHAIN IS AN ELEMENT-BEARING RECORD EXACTLY AS THE MANAGED BUFFER IS (W173).** A `List with T` owns a
`__list_create` record, a chain of nodes and — for a managed `T` — each node's element; none of it is
reachable from a generic `__mm_decref` and none of it is reachable from a byte blit either. So its deep copy
is a chain WALK (`__list_clone`), the structural twin of the buffer's `__managed_clone`, and it is what makes
`Array with (List with Integer)` copyable at all.

The helper clones a source array of lists and returns the clone; the source array, its two chains and every
one of their nodes are freed when the helper returns. `__mm_free` poisons freed bytes (0x3F), so a SHALLOW
copy faults here rather than reading stale-but-plausible values.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer
typealias ListArray = Array with IntList

function makeClone() returns ListArray
	var src = ListArray.create()
	var a = IntList.create()
	a.append(10)
	a.append(20)
	src.push(a)
	var b = IntList.create()
	b.append(30)
	src.push(b)
	return src.clone()
	// src, both chains and all three nodes are freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	if cloned.count() != 2 'badCount'
		return 91
	end 'badCount'

	let first = try cloned.get(0) otherwise return 92
	let second = try cloned.get(1) otherwise return 93
	let head = try first.get(0) otherwise return 94
	let tail = try first.get(1) otherwise return 95
	let only = try second.get(0) otherwise return 96

	print("{first.count()} {second.count()} {head} {tail} {only}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 10 20 30
```

<!-- test: clone-of-a-struct-holding-a-managed-element-list -->
### Clone of a struct whose field is a list of STRINGS
The chain's own elements are managed here, so the copy cannot reuse the trivial walk: each node's `String`
is deep-cloned in turn (`__list_clone_managed` + `__str_clone`), which is the chain's spelling of
`__managed_clone_managed`. Reached one level down — through `__clone_Holder`'s field cascade — which is where
a 2-argument entry is emitted INLINE. (An array ELEMENT reaches the same call through the one-argument thunk
instead; `clone-of-an-array-of-managed-element-lists` below is the chain at that position.)
```maxon
typealias StrList = List with String

type Holder
	export var words as StrList

	export static function create(words StrList) returns Self
		return Self{words: words}
	end 'create'
end 'Holder'

typealias Holders = Array with Holder

function makeClone() returns Holders
	var src = Holders.create()
	var w = StrList.create()
	w.append("first list element, long enough to need a heap record")
	w.append("second list element, long enough to need a heap record")
	src.push(Holder.create(w))
	return src.clone()
	// src, its Holder, its chain and both String records are freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	let h = try cloned.get(0) otherwise return 91
	let first = try h.words.get(0) otherwise return 92
	let second = try h.words.get(1) otherwise return 93

	print("{h.words.count()}\n{first}\n{second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
first list element, long enough to need a heap record
second list element, long enough to need a heap record
```

<!-- test: an-array-of-string-arrays-needs-no-copy-to-compile -->
### An array of string ARRAYS compiles on a program that copies nothing
⭐⭐ **G18.** `Array.clone`'s own `managed.slice(0, len)` is answerable for EVERY instantiation of `Array`
in the program, because the corpus body is compiled once over an opaque `Element` — so any program that
reaches `stdlib/Array.maxon` at all (here, through `for … in`) used to be refused for an element it never
copies. What makes the refusal go away is not a reachability exemption but the missing CLONER: a
managed-element container now has a per-instance one-argument `__clone_<mangled>` thunk, so the element is
deep-cloneable and the gate has nothing left to refuse.
```maxon
typealias StringArrayArray = Array with StringArray

function firstOf(candidates StringArrayArray) returns String
	for segments in candidates 'each'
		for s in segments 'inner'
			return s
		end 'inner'
	end 'each'
	return "none"
end 'firstOf'

function main() returns ExitCode
	var outer = StringArrayArray.create()
	var inner = StringArray.create()
	inner.push("a hit long enough to reach the heap")
	outer.push(inner)
	print("{firstOf(outer)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a hit long enough to reach the heap
```

<!-- test: clone-of-an-array-of-string-arrays-is-deep -->
### Clone of an array of STRING ARRAYS, source freed before access
The element is itself a managed-element container, whose copy is the 2-argument
`__managed_clone_managed(box, funcAddr(__str_clone))`. An array element is invoked through
`callIndirect(fn, elem)` and can pass only one argument, so the element's `copyFunc` is a synthesized
per-instance thunk that makes that 2-argument call and returns its result. The source array, its inner
array and its Strings are all freed before the clone is read, so a shallow copy at ANY of the three levels
is a read of freed memory rather than a wrong number.
```maxon
typealias StringArrayArray = Array with StringArray

function makeClone() returns StringArrayArray
	var src = StringArrayArray.create()
	var inner = StringArray.create()
	inner.push("first inner string, long enough to need a heap record")
	inner.push("second inner string, long enough to need a heap record")
	src.push(inner)
	return src.clone()
	// src, its inner array and both String records are freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	let inner = try cloned.get(0) otherwise return 91
	let first = try inner.get(0) otherwise return 92
	let second = try inner.get(1) otherwise return 93

	print("{cloned.count()} {inner.count()}\n{first}\n{second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
first inner string, long enough to need a heap record
second inner string, long enough to need a heap record
```

<!-- test: clone-of-an-array-of-arrays-of-structs -->
### Clone of buckets — an array of arrays of STRUCTS
The shape shv2's own worker pool is built on (`Array with SpecTestResultArray`): the thunk's inner
element cloner is a per-STRUCT `__clone_<T>` rather than `__str_clone`, so the needs closure has to reach
the struct cascade THROUGH the synthesized thunk, which no module scan can see.
```maxon
typealias Integer = int(i64.min to i64.max)

type Rec
	export var name as String
	export var value as Integer

	static function create(name String, value Integer) returns Self
		return Self{name: name, value: value}
	end 'create'
end 'Rec'

typealias RecArray = Array with Rec
typealias RecBuckets = Array with RecArray

function makeClone() returns RecBuckets
	var src = RecBuckets.create()
	var bucket = RecArray.create()
	bucket.push(Rec.create("a bucketed record name long enough to reach the heap", value: 41))
	src.push(bucket)
	return src.clone()
	// src, its bucket, its Rec and that Rec's String are freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	let bucket = try cloned.get(0) otherwise return 91
	let rec = try bucket.get(0) otherwise return 92

	print("{rec.name} {rec.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a bucketed record name long enough to reach the heap 41
```

<!-- test: slice-of-an-array-of-string-arrays-is-deep -->
### Slice of an array of string arrays is an independent OWNER
`slice` reaches the same element cloner `clone` does (`__managed_slice_managed` rather than
`__managed_clone_managed`), so the window it copies out is a full owner over deep copies — which is what
lets the source be dropped while the slice is still read.
```maxon
typealias StringArrayArray = Array with StringArray

function makeSlice() returns StringArrayArray
	var src = StringArrayArray.create()
	var first = StringArray.create()
	first.push("dropped inner string, long enough to need a heap record")
	var second = StringArray.create()
	second.push("kept inner string, long enough to need a heap record")
	src.push(first)
	src.push(second)
	return try src.slice(1, endIndex: 2) otherwise StringArrayArray.create()
	// src and BOTH inner arrays are freed when this function returns
end 'makeSlice'

function main() returns ExitCode
	let kept = makeSlice()

	let inner = try kept.get(0) otherwise return 91
	let s = try inner.get(0) otherwise return 92

	print("{kept.count()} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 kept inner string, long enough to need a heap record
```

<!-- test: clone-of-a-three-level-container-nesting -->
### Clone of a THREE-level container nesting
Each level's thunk names the level below it, so the needs closure has to grow through two synthesized
bodies in a row — the container twin of `clone-managed-three-level-cascade`, which does the same through
two per-struct cascades.
```maxon
typealias StringArrayArray = Array with StringArray
typealias StringArrayArrayArray = Array with StringArrayArray

function makeClone() returns StringArrayArrayArray
	var src = StringArrayArrayArray.create()
	var mid = StringArrayArray.create()
	var leaf = StringArray.create()
	leaf.push("the deepest string, long enough to need a heap record")
	mid.push(leaf)
	src.push(mid)
	return src.clone()
	// every one of the four levels is freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	let mid = try cloned.get(0) otherwise return 91
	let leaf = try mid.get(0) otherwise return 92
	let s = try leaf.get(0) otherwise return 93

	print("{cloned.count()} {mid.count()} {leaf.count()}\n{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 1
the deepest string, long enough to need a heap record
```

<!-- test: clone-of-an-array-of-managed-element-lists -->
### Clone of an array of managed-element LISTS
⚠ **A CONTROL, NOT A NEW CAPABILITY — IT PASSES ON THE MERGE BASE TOO, AND SAYING SO IS THE POINT.** A
`List with T` is a DECLARED struct whose single field is the chain (W153), so this element clones through
its ordinary per-instance `__clone_<mangled>` field cascade — already a one-argument entry — and it never
met the arity bound G18 removes. It is here because it is the CLOSEST NEIGHBOUR to what G18 changed: the
same router (`elementRecordCloneStrategy`) decides both, and the chain's own managed-element form
(`__list_clone_managed`) is the container slot the thunk would fill if the chain record were ever spellable
as an array element directly. It is not, so that half of the router is reached by construction rather than
by a corpus program — and this case is what would go red if the declared-box route were disturbed reaching
for it.
```maxon
typealias StrList = List with String
typealias StrListArray = Array with StrList

function makeClone() returns StrListArray
	var src = StrListArray.create()
	var words = StrList.create()
	words.append("first list element, long enough to need a heap record")
	words.append("second list element, long enough to need a heap record")
	src.push(words)
	return src.clone()
	// src, its chain, both nodes and both String records are freed when this function returns
end 'makeClone'

function main() returns ExitCode
	let cloned = makeClone()

	let words = try cloned.get(0) otherwise return 91
	let first = try words.get(0) otherwise return 92
	let second = try words.get(1) otherwise return 93

	print("{words.count()}\n{first}\n{second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
first list element, long enough to need a heap record
second list element, long enough to need a heap record
```

<!-- test: append-of-an-array-of-string-arrays-deep-copies-the-source -->
### Append of one array of string arrays into another deep-copies the SOURCE
`append` is the third member of the copy gate and the only one whose source is BORROWED rather than
consumed: `__managed_append_managed` grows the destination by deep clones and leaves the source owning its
own elements. Both arrays are therefore live at teardown, so a shallow append would free every appended
inner array — and every String inside it — twice.
```maxon
typealias StringArrayArray = Array with StringArray

function main() returns ExitCode
	var dest = StringArrayArray.create()
	var first = StringArray.create()
	first.push("destination inner string, long enough to allocate")
	dest.push(first)

	var src = StringArrayArray.create()
	var second = StringArray.create()
	second.push("appended inner string, long enough to allocate")
	src.push(second)

	dest.append(src)

	let appended = try dest.get(1) otherwise return 91
	let s = try appended.get(0) otherwise return 92

	print("{dest.count()} {src.count()}\n{s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
appended inner string, long enough to allocate
```
