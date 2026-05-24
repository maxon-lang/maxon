---
feature: generic-module-merge-crash
status: experimental
keywords: [generics, typealias, inner-alias, interface, merge, uses, with]
category: type-system
---

# Generic Module with Interface Conformance and Merge

## Tests

### Generic module with merge extension

<!-- test: generic-module-merge -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

interface Mergeable uses Func
	function itemCount() returns Count
	function getItem(index Count) returns Func
	function pushItem(item Func)
	function opsCount() returns Count
	function appendOps(source Self)
	function cloneItem(src Func) returns Func
end 'Mergeable'

extension Mergeable
	export function merge(source Self)
		self.appendOps(source)
		for i in 0 upto source.itemCount() 'eachItem'
			let src = source.getItem(i)
			let copy = self.cloneItem(src)
			self.pushItem(copy)
		end 'eachItem'
	end 'merge'
end 'Mergeable'

type Wrapper
	export var value as Integer
	export static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

type Container uses Op implements Mergeable with Wrapper
	typealias OpArray = Array with Op

	export var ops as OpArray
	export var items as WrapperArray

	export static function create() returns Self
		return Self{
			ops: OpArray.create(),
			items: WrapperArray.create()
		}
	end 'create'

	export function opsCount() returns Count
		return self.ops.count()
	end 'opsCount'

	export function appendOps(source Self)
		self.ops.append(source.ops)
	end 'appendOps'

	export function itemCount() returns Count
		return self.items.count()
	end 'itemCount'

	export function getItem(index Count) returns Wrapper
		return try self.items.get(index) otherwise 'unreachable'
			panic("getItem: index out of bounds")
		end 'unreachable'
	end 'getItem'

	export function pushItem(item Wrapper)
		self.items.push(item)
	end 'pushItem'

	export function cloneItem(src Wrapper) returns Wrapper
		return Wrapper.create(src.value)
	end 'cloneItem'

	export function pushOp(op Op)
		self.ops.push(op)
	end 'pushOp'
end 'Container'

typealias IntContainer = Container with Integer

typealias ExitCode = int(0 to 125)

function main() returns ExitCode
	var a = IntContainer.create()
	a.pushOp(10)
	a.pushOp(20)
	a.pushItem(Wrapper.create(99))

	var b = IntContainer.create()
	b.merge(a)

	if b.opsCount() == 2 and b.itemCount() == 1 'check'
		let item = b.getItem(0)
		if item.value == 99 'valueCheck'
			return 0
		end 'valueCheck'
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
