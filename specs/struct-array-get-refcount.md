---
feature: struct-array-get-refcount
status: experimental
keywords: [array, struct, get, refcount, memory]
category: memory-safety
---

# Struct Array Get Refcount

## Documentation

When retrieving struct elements from an array via `get()`, the returned struct pointer must be reference-counted correctly. The array retains its reference to the element, and the caller receives a borrowed reference that must be incref'd to prevent premature deallocation.

## Tests

<!-- test: struct-array-get-survives-scope -->
Struct elements retrieved from an array in a loop inside a function must survive after the function returns.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
		export var value Integer
		export var next Integer

		static function create(value Integer, next Integer) returns Self
			return Self{value: value, next: next}
		end 'create'
end 'Node'

typealias NodeArray = Array with Node

type List
		export var nodes NodeArray
		export var head Integer

		function pushFront(value Integer)
				let node = Node.create(value: value, next: self.head)
				self.nodes.push(node)
				self.head = self.nodes.count() - 1
		end 'pushFront'

		function walk()
				var current = self.head
				while current != -1 'w'
						let node = try self.nodes.get(current) otherwise Node.create(value: 0, next: -1)
						current = node.next
				end 'w'
		end 'walk'

		static function create(nodes NodeArray, head Integer) returns Self
			return Self{nodes: nodes, head: head}
		end 'create'
end 'List'

function main() returns ExitCode
		var list = List.create(nodes: NodeArray.create(), head: -1)
		list.pushFront(10)
		list.pushFront(20)
		list.walk()
		list.pushFront(30)
		let n1 = try list.nodes.get(1) otherwise Node.create(value: 0, next: -1)
		return n1.value
end 'main'
```
```exitcode
20
```

<!-- test: struct-array-get-loop-function -->
Struct elements in array survive after being read in a loop inside a standalone function.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
		export var a Integer
		export var b Integer

		static function create(a Integer, b Integer) returns Self
			return Self{a: a, b: b}
		end 'create'
end 'Pair'

typealias PairArray = Array with Pair

function sumAll(pairs PairArray) returns Integer
		var total = 0
		for pair in pairs 'loop'
				total = total + pair.a + pair.b
		end 'loop'
		return total
end 'sumAll'

function main() returns ExitCode
		var pairs = PairArray.create()
		pairs.push(Pair.create(a: 1, b: 2))
		pairs.push(Pair.create(a: 3, b: 4))
		pairs.push(Pair.create(a: 5, b: 6))
		let sum = sumAll(pairs)
		// After sumAll, elements should still be valid
		let p1 = try pairs.get(1) otherwise Pair.create(a: 0, b: 0)
		if sum == 21 'ok'
				return p1.a + p1.b
		end 'ok'
		return 0
end 'main'
```
```exitcode
7
```

<!-- test: struct-array-get-multiple-reads -->
Multiple reads of the same struct array element in a function don't corrupt data.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
		export var id Integer

		static function create(id Integer) returns Self
			return Self{id: id}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

function readTwice(items ItemArray) returns Integer
		let a = try items.get(0) otherwise Item.create(id: 0)
		let b = try items.get(0) otherwise Item.create(id: 0)
		return a.id + b.id
end 'readTwice'

function main() returns ExitCode
		var items = ItemArray.create()
		items.push(Item.create(id: 21))
		let result = readTwice(items)
		let check = try items.get(0) otherwise Item.create(id: 0)
		return check.id + result
end 'main'
```
```exitcode
63
```
