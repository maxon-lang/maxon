---
feature: struct-array-get-refcount
status: experimental
keywords: [array, struct, get, refcount, memory]
category: memory-safety
---

# Struct Array Get Refcount

## Documentation

When retrieving struct elements from an array via `get()`, the returned struct pointer must be reference-counted correctly. The array retains its reference to the element, and the caller receives a borrowed reference that must be incref'd to prevent premature deallocation.

A `try arr.get(i) otherwise <fresh owned value>` merges two edges with *different* ownership: the success edge yields a **borrow** the array owns, the error edge a **fresh owned** box. They flow into one binding, so the binding needs one uniform drop discipline. The compiler takes a second owner of the borrowed element on the success edge (`__mm_incref`) and drops the merged binding exactly once at its scope exit. The error-edge fallback is therefore **not** released on the error edge — doing so would free it before the binding is read, a use-after-free.

⚠ **The `try-otherwise-error-fallback-outlives-read` test below is a regression guard for that use-after-free, and it PASSES WITH OR WITHOUT THE FIX under the shipped allocator** — a bump allocator that never reclaims a freed box leaves the freed bytes intact, so a premature free is invisible to a value check. It was caught only by a poisoning `__mm_free` that overwrites a freed payload; it is pinned here so the behaviour is not silently re-broken, not because it fails today on the shipped allocator.

## Tests

<!-- test: struct-array-get-survives-scope -->
Struct elements retrieved from an array in a loop inside a function must survive after the function returns.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
		export var value as Integer
		export var next as Integer

		static function create(value Integer, next Integer) returns Self
			return Self{value: value, next: next}
		end 'create'
end 'Node'

typealias NodeArray = Array with Node

type List
		export var nodes as NodeArray
		export var head as Integer

		function pushFront(value Integer)
				let node = Node.create(value, next: self.head)
				self.nodes.push(node)
				self.head = self.nodes.count() - 1
		end 'pushFront'

		function walk()
				var current = self.head
				while current != -1 'w'
						let node = try self.nodes.get(current) otherwise Node.create(0, next: -1)
						current = node.next
				end 'w'
		end 'walk'

		static function create(nodes NodeArray, head Integer) returns Self
			return Self{nodes: nodes, head: head}
		end 'create'
end 'List'

function main() returns ExitCode
		var list = List.create(NodeArray.create(), head: -1)
		list.pushFront(10)
		list.pushFront(20)
		list.walk()
		list.pushFront(30)
		let n1 = try list.nodes.get(1) otherwise Node.create(0, next: -1)
		return n1.value
end 'main'
```
```exitcode
20
```

<!-- disabled-test: struct-array-get-loop-function -->
<!-- P1.8 for-in -->
Struct elements in array survive after being read in a loop inside a standalone function.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
		export var a as Integer
		export var b as Integer

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
		pairs.push(Pair.create(1, b: 2))
		pairs.push(Pair.create(3, b: 4))
		pairs.push(Pair.create(5, b: 6))
		let sum = sumAll(pairs)
		// After sumAll, elements should still be valid
		let p1 = try pairs.get(1) otherwise Pair.create(0, b: 0)
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
		export var id as Integer

		static function create(id Integer) returns Self
			return Self{id: id}
		end 'create'
end 'Item'

typealias ItemArray = Array with Item

function readTwice(items ItemArray) returns Integer
		let a = try items.get(0) otherwise Item.create(0)
		let b = try items.get(0) otherwise Item.create(0)
		return a.id + b.id
end 'readTwice'

function main() returns ExitCode
		var items = ItemArray.create()
		items.push(Item.create(21))
		let result = readTwice(items)
		let check = try items.get(0) otherwise Item.create(0)
		return check.id + result
end 'main'
```
```exitcode
63
```

<!-- test: try-otherwise-error-fallback-outlives-read -->
The `otherwise` fallback of a `try` over a managed-borrow accessor is a fresh owned box that must live until the merged binding is READ, not be freed on the error edge. Here index 1 is past the end (only slot 0 was pushed), so the error edge runs and the binding is the fallback `Slot.create(-7)`; `result.value` must read `-7`. If the fallback were freed on the error edge before the read, the field read would see a dead box. What matters is that `get` takes its ERROR edge, not which failure sent it there — the array was formerly widened with `resize(3)` to make slot 1 empty instead, which E3106 now refuses on a struct element. (See the ⚠ note above: this passes on the shipped allocator regardless — it is a poison-only regression guard.)
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(10))
	let result = try arr.get(1) otherwise Slot.create(-7)
	return result.value + 8
end 'main'
```
```exitcode
1
```

<!-- test: otherwise-value-expression-allocates -->
The `otherwise <value>` fallback is an expression, so it may build its OWN owned temporary that it does not hand back — `[1, 2].count()` builds an array and yields a scalar. That temporary runs on the error edge only and must die there, not at the shared statement tail (which the ok edge reaches too — releasing it there is a release of a value that edge never built, a register-allocator "use dominates its def" panic on a valid program). This pins the fallback expression as a fork region alongside a ternary/match arm. Unlike the borrow guard above, this DOES fail without the fix — it is a compiler crash, not a silent free — so it is a genuine red-before-green.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	let v = try arr.get(9) otherwise [1, 2].count()
	return v
end 'main'
```
```exitcode
2
```

<!-- test: otherwise-live-binding-not-double-freed -->
When the `otherwise` fallback is a live owned BINDING that is read again after the `try`, the error edge ALIASES it rather than moving it — so the merge binding `r` and the source binding `fallback` name the SAME box. If the phi is made owned by moving (the temporary rule), both `r` and `fallback` decref that one box at scope exit — a double-free the leak count catches as exit 101. The cure is the error-edge twin of the ok-edge borrow incref: incref the aliased binding so `r` owns its own reference and `fallback` keeps its own. Unlike the poison-only guards above, this is a genuine red-before-green — it exits **101** without the fix — and it needs no poison, because a double-free drives the leak count NEGATIVE. `fallback` must still read `99` after the try.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
	var a = NodeArray.create()
	a.push(Node.create(10))
	let fallback = Node.create(99)
	let r = try a.get(9) otherwise fallback
	return r.value + fallback.value
end 'main'
```
```exitcode
198
```

<!-- test: otherwise-live-binding-owned-result -->
The same aliased-binding double-free reached through the OTHER arm — an OWNED try-result (`pop` moves the element out) merged with a live-binding fallback. It also exits **101** without the incref, and it exercises a program that would compile with no array-slice site, so it also pins that `__mm_incref` installs whenever the parser emits it (not only under the array runtime). `fallback` reads `99` after the try; `pop` on an empty array takes the error edge.
```maxon
typealias Integer = int(i64.min to i64.max)

type Node
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node

function main() returns ExitCode
	var a = NodeArray.create()
	let fallback = Node.create(99)
	let r = try a.pop() otherwise fallback
	return r.value + fallback.value
end 'main'
```
```exitcode
198
```
