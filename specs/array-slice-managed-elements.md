---
feature: array-slice-managed-elements
status: stable
keywords: [array, slice, managed, refcount, use-after-free]
category: memory
---
# Array Slice Must Incref Managed Elements

## Documentation

When `Array.slice` copies elements via `managed.slice()`, managed elements (structs, enums)
must have their reference counts incremented. The current implementation uses a raw `memcpy`
which copies heap pointers without adjusting refcounts. When the source array is later freed,
its destructor decrements each element's refcount — potentially freeing elements that the
slice still references.

## Tests

<!-- test: slice-struct-source-freed -->
### Slice of struct array, source freed before access
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
		export var name String
		export var value Integer

		static function create(name String, value Integer) returns Self
			return Self{name: name, value: value}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

function makeSlice() returns ItemArray throws ArrayError
		var src = ItemArray.create()
		src.push(Item.create("first item long enough for heap allocation", value: 10))
		src.push(Item.create("second item long enough for heap allocation", value: 20))
		src.push(Item.create("third item long enough for heap allocation", value: 30))
		return try src.slice(0, endIndex: 2)
		// src is freed when this function returns
end 'makeSlice'

function main() returns ExitCode
		let sliced = try makeSlice() otherwise return 98

		if sliced.count() != 2 'badCount'
				return 99
		end 'badCount'

		let item = try sliced.get(0) otherwise Item.create("", value: 0)
		return item.value
end 'main'
```
```exitcode
10
```

<!-- test: slice-enum-source-freed -->
### Slice of enum array, source freed before access
```maxon
typealias Integer = int(i64.min to i64.max)

union Op
		add(value Integer)
		sub(value Integer)
		nop
end 'Op'

typealias OpArray = Array with Op

function makeSlice() returns OpArray throws ArrayError
		var src = OpArray.create()
		src.push(Op.add(10))
		src.push(Op.sub(20))
		src.push(Op.add(30))
		return try src.slice(1, endIndex: 3)
end 'makeSlice'

function main() returns ExitCode
		let sliced = try makeSlice() otherwise return 96

		if sliced.count() != 2 'badCount'
				return 99
		end 'badCount'

		let op = try sliced.get(0) otherwise Op.nop
		match op 'check'
				sub(v) then return v
				add then return 98
				nop then return 97
		end 'check'
end 'main'
```
```exitcode
20
```
