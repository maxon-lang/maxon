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

	export var items as ElementArray
	export var name as String

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

	export var items as ElementArray

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

### Returning a ternary over an opaque element

A ternary's arms merge into ONE result temp, and a `return` of that temp transfers the temp's owned
reference to the caller — so scope cleanup must skip it. Under a type-parameter return type the merged
temp is read back through a plain variable load rather than a struct load, which is the shape the
transfer has to recognize; without it the frame frees and nulls the slot before the return reads it and
the caller increfs a null pointer.

Both arms are exercised: the two-element container returns its FRONT and the three-element one its BACK,
each compared against the element it must hand back.

<!-- test: ternary-return-of-opaque-element -->
```maxon
typealias ExitCode = int(0 to 125)

type Container uses Element
	typealias ElementArray = Array with Element

	export var items as ElementArray

	export static function create() returns Self
		return Self{ items: ElementArray.create() }
	end 'create'

	export function push(item Element)
		self.items.push(item)
	end 'push'

	export function peekEnds() returns Element throws ArrayError
		let front = try self.items.first()
		let back = try self.items.last()
		return front if self.items.count() == 2 else back
	end 'peekEnds'
end 'Container'

typealias StringContainer = Container with String

function main() returns ExitCode
	var pair = StringContainer.create()
	pair.push("front element, long enough to force a heap allocation")
	pair.push("back element, also long enough to force a heap allocation")
	let fromBorrowedArm = try pair.peekEnds() otherwise return 1

	var trio = StringContainer.create()
	trio.push("front element, long enough to force a heap allocation")
	trio.push("middle element, long enough to force a heap allocation")
	trio.push("back element, also long enough to force a heap allocation")
	let fromOwnedArm = try trio.peekEnds() otherwise return 2

	if not fromBorrowedArm.equals("front element, long enough to force a heap allocation") 'wrongFront'
		return 3
	end 'wrongFront'

	if not fromOwnedArm.equals("back element, also long enough to force a heap allocation") 'wrongBack'
		return 4
	end 'wrongBack'

	return 0
end 'main'
```
```exitcode
0
```
