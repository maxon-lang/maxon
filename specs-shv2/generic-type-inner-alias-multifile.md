---
feature: generic-type-inner-alias-multifile
status: experimental
keywords: [generics, typealias, inner-alias, multi-file, uses, with]
category: type-system
---

# Generic Type with Inner Typealias Across Files

## Documentation

When a generic type declares an inner typealias using its type parameter (e.g., `typealias OpArray = Array with Op`), and also has fields with already-concrete external typealiases (e.g., `var items ItemArray`), concrete specializations must correctly resolve both field types across file boundaries.

## Tests

### Inner typealias field access in separate file

<!-- test: inner-alias-cross-file -->
```maxon
// --- file: types.maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

export type Item
	export var value as Integer
	export static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

export typealias ItemArray = Array with Item

export type GenericModule uses Op
	typealias OpArray = Array with Op

	export var ops as OpArray
	export var items as ItemArray

	export static function create() returns Self
		return Self{
			ops: OpArray.create(),
			items: ItemArray.create()
		}
	end 'create'

	export function pushOp(op Op)
		self.ops.push(op)
	end 'pushOp'

	export function pushItem(item Item)
		self.items.push(item)
	end 'pushItem'

	export function opCount() returns Count
		return self.ops.count()
	end 'opCount'

	export function itemCount() returns Count
		return self.items.count()
	end 'itemCount'
end 'GenericModule'

export typealias IntModule = GenericModule with Integer

// --- file: main.maxon
typealias ExitCode = int(0 to 125)

function main() returns ExitCode
	var m = IntModule.create()
	m.pushOp(10)
	m.pushOp(20)
	m.pushItem(Item.create(99))
	let oc = m.opCount()
	let ic = m.itemCount()
	if oc == 2 and ic == 1 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Inner typealias used in string interpolation in separate file

<!-- test: inner-alias-interpolation -->
```maxon
// --- file: types.maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

export type Item
	export var value as Integer
	export static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

export typealias ItemArray = Array with Item

export type GenericModule uses Op
	typealias OpArray = Array with Op

	export var ops as OpArray
	export var items as ItemArray

	export static function create() returns Self
		return Self{
			ops: OpArray.create(),
			items: ItemArray.create()
		}
	end 'create'

	export function pushOp(op Op)
		self.ops.push(op)
	end 'pushOp'

	export function opCount() returns Count
		return self.ops.count()
	end 'opCount'

	export function itemCount() returns Count
		return self.items.count()
	end 'itemCount'
end 'GenericModule'

export typealias IntModule = GenericModule with Integer

// --- file: main.maxon
typealias ExitCode = int(0 to 125)

function main() returns ExitCode
	var m = IntModule.create()
	m.pushOp(10)
	m.pushOp(20)
	print("ops={m.opCount()} items={m.itemCount()}\n")
	if m.opCount() == 2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
ops=2 items=0
```
