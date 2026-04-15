---
feature: generic-type-nested-array-typealias
status: experimental
keywords: [generics, typealias, array, nested, uses, with, monomorphization]
category: type-system
---

# Generic Type with Nested Array Typealias

## Documentation

When a generic type declares a typealias that references its type parameter (e.g., `typealias ElementArray = Array with Element`), monomorphization must correctly resolve the element size for array allocation.

## Tests

### Basic generic type with nested array typealias

<!-- test: basic-nested-array -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)
typealias SmallInt = int(0 to 100)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items ElementArray
	export var name String

	export static function create(name String) returns Self
		return Self{
			items: ElementArray.create(),
			name: name
		}
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function push(item Element)
		self.items.push(item)
	end 'push'
end 'Container'

typealias IntContainer = Container with SmallInt

function main() returns ExitCode
	var ic = IntContainer.create("numbers")
	ic.push(10)
	ic.push(20)
	let c = ic.count()
	if c == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Generic type with string element

<!-- test: string-element -->
```maxon
typealias ExitCode = int(0 to 125)
typealias Count = int(0 to u64.max)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items ElementArray

	export static function create() returns Self
		return Self{
			items: ElementArray.create()
		}
	end 'create'

	export function count() returns Count
		return self.items.count()
	end 'count'

	export function push(item Element)
		self.items.push(item)
	end 'push'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var sc = StringContainer.create()
	sc.push("hello")
	sc.push("world")
	let c = sc.count()
	if c == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
