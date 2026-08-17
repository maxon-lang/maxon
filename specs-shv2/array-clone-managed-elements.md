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
error E2015: <fragment>:8:11: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned as a single-function element — a managed-element array (`Array with (Array with String)`) or a non-Array generic instance (`Box with String`, whose per-instance cloner is a later slice). String / struct / boxed-union / trivial-element-array / trivial instantiations ARE supported (P1.7 slice 3b-vi-b).
note: stdlib/Array.maxon:145:32: raised inside the library, on behalf of the construct above
```
