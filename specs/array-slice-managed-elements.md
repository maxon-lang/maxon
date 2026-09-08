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
		export var name as String
		export var value as Integer

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

<!-- test: slice-managed-then-write-original -->
### Slice is independent of later writes to the original
A deep-cloned slice must not alias the source's buffer: mutating the ORIGINAL after slicing (a push that
grows it) leaves the slice untouched — it owns its own independent element clones.
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

function main() returns ExitCode
		var src = ItemArray.create()
		src.push(Item.create("alpha item long enough for heap alloc", value: 10))
		src.push(Item.create("beta item long enough for heap alloc", value: 20))
		src.push(Item.create("gamma item long enough for heap alloc", value: 30))

		let sub = try src.slice(0, endIndex: 2) otherwise return 98

		// Mutate the ORIGINAL after slicing — an independent deep clone is unaffected.
		src.push(Item.create("delta item long enough for heap alloc", value: 40))

		if sub.count() != 2 'badSubCount'
				return 97
		end 'badSubCount'
		if src.count() != 4 'badSrcCount'
				return 96
		end 'badSrcCount'

		let item = try sub.get(1) otherwise Item.create("", value: 0)
		return item.value
end 'main'
```
```exitcode
20
```

<!-- test: slice-union-string-payload -->
### Slice of a union array whose live case owns a String payload, source freed
Exercises the union cloner's MANAGED-payload path: `__clone_<Union>` blits the box, then behind a tag guard
deep-clones the live case's String payload. A shallow copy would dangle when the source frees.
```maxon
typealias Integer = int(i64.min to i64.max)

union Node
		leaf(name String)
		branch(value Integer)
		empty
end 'Node'

typealias NodeArray = Array with Node

function checkLeaf(n String) returns ExitCode
		if n == "first leaf node name long enough for heap" 'correct'
				return 0
		end 'correct'
		return 1
end 'checkLeaf'

function makeSlice() returns NodeArray throws ArrayError
		var src = NodeArray.create()
		src.push(Node.leaf("first leaf node name long enough for heap"))
		src.push(Node.branch(42))
		src.push(Node.leaf("third leaf node name long enough for heap"))
		return try src.slice(0, endIndex: 2)
		// src is freed when this function returns
end 'makeSlice'

function main() returns ExitCode
		let sliced = try makeSlice() otherwise return 96

		if sliced.count() != 2 'badCount'
				return 99
		end 'badCount'

		let node = try sliced.get(0) otherwise Node.empty
		match node 'check'
				leaf(n) then return checkLeaf(n)
				branch then return 98
				empty then return 97
		end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: trivial-slice-still-cow-byte-identical -->
### Trivial-element slice still takes the O(1) COW path
A slice of a TRIVIAL-element array is unchanged by the managed deep-clone rung — it stays the O(1)
zero-copy view (`__managed_slice`). This is the contrast case pinning that only MANAGED elements route to the
deep-clone path.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
		var src = IntArray.create()
		src.push(10)
		src.push(20)
		src.push(30)

		let sub = try src.slice(1, endIndex: 3) otherwise return 98

		if sub.count() != 2 'badCount'
				return 99
		end 'badCount'

		return try sub.get(0) otherwise 97
end 'main'
```
```exitcode
20
```
