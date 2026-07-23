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
