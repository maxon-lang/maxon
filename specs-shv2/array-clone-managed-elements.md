---
feature: array-clone-managed-elements
status: stable
keywords: [array, clone, managed, deep-clone, use-after-free]
category: memory
---
# Array Clone Deep-Copies Managed Elements

## Documentation

`Array.clone` on a MANAGED-element array (String / struct / boxed union) produces an INDEPENDENT copy by
deep-cloning each element (`__arr_clone_managed` + the element's `__clone_<E>`), not a COW view. shv2 is
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
