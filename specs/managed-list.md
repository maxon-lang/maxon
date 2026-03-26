---
feature: managed-list
status: experimental
keywords: [managed-list, linked list, doubly linked, node, collection]
category: collections
---
# ManagedList

## Documentation

A `__ManagedList` is a doubly linked list with O(1) insertion, removal, and node-level access. Unlike `List`, which provides index-based access, `__ManagedList` exposes node handles (`__ManagedListNode`) that allow direct traversal and manipulation.

__ManagedList owns its nodes via reference counting. Nodes are accessed through `__ManagedListNode` handles with refcount-based lifetime.

### Creating a ManagedList

Create an empty list with an explicit element type:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer
var list = IntManagedList.create()
```

### Inserting Elements

Insert at the front or back, receiving a handle to the new node:

```text
var first = list.insertFirst(10)
var last = list.insertLast(30)
var middle = list.insertAfter(target: first, value: 20)
```

### Traversal

Use node handles to traverse the list:

```text
var node = try list.head() otherwise 'err'
  // list is empty
end 'err'
print("{node.value()}")
var next = try node.next() otherwise 'err2'
  // at end of list
end 'err2'
```

### Methods

#### insertFirst(value Element) returns __ManagedListNode

Insert a value at the front. Returns a handle to the new node.

#### insertLast(value Element) returns __ManagedListNode

Insert a value at the back. Returns a handle to the new node.

#### insertAfter(target __ManagedListNode, value Element) returns __ManagedListNode

Insert a value after the given node. Returns a handle to the new node.

#### insertBefore(target __ManagedListNode, value Element) returns __ManagedListNode

Insert a value before the given node. Returns a handle to the new node.

#### head() returns __ManagedListNode throws __ManagedListError

Get a handle to the first node. Throws `__ManagedListError.empty` if the list is empty.

#### tail() returns __ManagedListNode throws __ManagedListError

Get a handle to the last node. Throws `__ManagedListError.empty` if the list is empty.

#### remove(node __ManagedListNode) returns Element

Remove a node from the list, returning its value and freeing the node.

#### detach(node __ManagedListNode)

Detach a node from the list without freeing it.

#### count() returns Count

Get the number of elements.

#### isEmpty() returns bool

Check if the list is empty.

#### clear()

Remove all elements, freeing all nodes.

### __ManagedListNode Methods

#### value() returns Element

Get the value stored in this node.

#### setValue(v Element)

Replace the value stored in this node.

#### next() returns __ManagedListNode throws __ManagedListError

Get the next node. Throws `__ManagedListError.endOfList` if at the end.

#### prev() returns __ManagedListNode throws __ManagedListError

Get the previous node. Throws `__ManagedListError.endOfList` if at the beginning.

## Tests

<!-- test: core.create-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	if list.isEmpty() 'check'
		print("{list.count()}\n")
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: core.insert-first -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertFirst(42)
	print("{node.value()}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
1
```

<!-- test: core.insert-last -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertLast(99)
	print("{node.value()}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
1
```

<!-- test: core.insert-first-multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n3 = list.insertFirst(30)
	var n2 = list.insertFirst(20)
	var n1 = list.insertFirst(10)
	// Verify via node handles
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
3
```

<!-- test: core.insert-last-multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Verify via node handles
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
3
```

<!-- test: core.insert-after -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertFirst(10)
	var n3 = list.insertLast(30)
	// Insert 20 after n1
	var n2 = list.insertAfter(target: n1, value: 20)
	// Verify order via traversal
	var cur = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{cur.value()}\n")
	var cur2 = try cur.next() otherwise 'err2'
		return 1
	end 'err2'
	print("{cur2.value()}\n")
	var cur3 = try cur2.next() otherwise 'err3'
		return 1
	end 'err3'
	print("{cur3.value()}\n")
	// Also verify n2 and n3 are valid
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
20
30
```

<!-- test: core.insert-before -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertFirst(10)
	var n3 = list.insertLast(30)
	// Insert 20 before n3
	var n2 = list.insertBefore(target: n3, value: 20)
	// Verify order via traversal
	var cur = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{cur.value()}\n")
	var cur2 = try cur.next() otherwise 'err2'
		return 1
	end 'err2'
	print("{cur2.value()}\n")
	var cur3 = try cur2.next() otherwise 'err3'
		return 1
	end 'err3'
	print("{cur3.value()}\n")
	// Also verify n1 and n2 are valid
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
10
20
```

<!-- test: core.remove -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Remove middle node
	var removed = list.remove(node: n2)
	print("{removed}\n")
	print("{list.count()}\n")
	// Verify remaining via handles
	print("{n1.value()}\n")
	print("{n3.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
2
10
30
```

<!-- test: core.detach -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Detach middle node
	list.detach(node: n2)
	print("{list.count()}\n")
	// Verify remaining via handles
	print("{n1.value()}\n")
	print("{n3.value()}\n")
	// Detached node still holds its value
	print("{n2.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
10
30
20
```

<!-- test: core.count -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(1)
	var n2 = list.insertLast(2)
	var n3 = list.insertLast(3)
	var n4 = list.insertLast(4)
	var n5 = list.insertLast(5)
	print("{list.count()}\n")
	// Use all handles to avoid unused variable errors
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	print("{n4.value()}\n")
	print("{n5.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
1
2
3
4
5
```

<!-- test: node.value -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertFirst(42)
	print("{node.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: node.next-prev -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Forward traversal from n1
	var fwd = try n1.next() otherwise 'err'
		return 1
	end 'err'
	print("{fwd.value()}\n")
	var fwd2 = try fwd.next() otherwise 'err2'
		return 1
	end 'err2'
	print("{fwd2.value()}\n")
	// Backward traversal from n3
	var back = try n3.prev() otherwise 'err3'
		return 1
	end 'err3'
	print("{back.value()}\n")
	var back2 = try back.prev() otherwise 'err4'
		return 1
	end 'err4'
	print("{back2.value()}\n")
	// Use n2 to avoid unused warning
	print("{n2.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
30
20
10
20
```

<!-- test: node.set-value -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertFirst(10)
	print("{node.value()}\n")
	node.setValue(v: 99)
	print("{node.value()}\n")
	// Verify through list head too
	var h = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{h.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
99
99
```

<!-- test: node.set-value-old-survives -->
```maxon
typealias StringManagedList = __ManagedList with String

function main() returns ExitCode
	var list = StringManagedList.create()
	var node = list.insertFirst("hello world!!!!!!!!!!!!!!")
	var old = node.value()
	node.setValue(v: "replacement!!!!!!!!!!!!!!")
	print("{old}\n")
	print("{node.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world!!!!!!!!!!!!!!
replacement!!!!!!!!!!!!!!
```

<!-- test: core.clear -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(1)
	var n2 = list.insertLast(2)
	var n3 = list.insertLast(3)
	// Verify nodes exist before clear
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	list.clear()
	if list.isEmpty() 'check'
		print("{list.count()}\n")
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
```stdout
1
2
3
0
```

<!-- test: core.head-empty-throws -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	if try list.head() 'try'
		return 1
	end 'try' else 'err'
		print("caught\n")
		return 0
	end 'err'
end 'main'
```
```exitcode
0
```
```stdout
caught
```

<!-- test: core.tail-empty-throws -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	if try list.tail() 'try'
		return 1
	end 'try' else 'err'
		print("caught\n")
		return 0
	end 'err'
end 'main'
```
```exitcode
0
```
```stdout
caught
```

<!-- test: node.next-end-throws -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertFirst(42)
	// Use the node handle
	print("{node.value()}\n")
	var h = try list.head() otherwise 'err'
		return 1
	end 'err'
	// Try to go past end
	if try h.next() 'try'
		return 1
	end 'try' else 'err2'
		print("caught\n")
		return 0
	end 'err2'
end 'main'
```
```exitcode
0
```
```stdout
42
caught
```

<!-- test: node.prev-begin-throws -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var node = list.insertFirst(42)
	// Use the node handle
	print("{node.value()}\n")
	var h = try list.head() otherwise 'err'
		return 1
	end 'err'
	// Try to go before beginning
	if try h.prev() 'try'
		return 1
	end 'try' else 'err2'
		print("caught\n")
		return 0
	end 'err2'
end 'main'
```
```exitcode
0
```
```stdout
42
caught
```

<!-- test: core.reinsert-first -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Move n3 to front: 30, 10, 20
	list.reinsertFirst(node: n3)
	var cur = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{cur.value()}\n")
	var cur2 = try cur.next() otherwise 'err2'
		return 1
	end 'err2'
	print("{cur2.value()}\n")
	var cur3 = try cur2.next() otherwise 'err3'
		return 1
	end 'err3'
	print("{cur3.value()}\n")
	// Use n1 and n2 to avoid unused warnings
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30
10
20
10
20
```

<!-- test: core.reinsert-last -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Move n1 to back: 20, 30, 10
	list.reinsertLast(node: n1)
	var cur = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{cur.value()}\n")
	var cur2 = try cur.next() otherwise 'err2'
		return 1
	end 'err2'
	print("{cur2.value()}\n")
	var cur3 = try cur2.next() otherwise 'err3'
		return 1
	end 'err3'
	print("{cur3.value()}\n")
	// Use n2 and n3 to avoid unused warnings
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
30
10
20
30
```

<!-- test: core.remove-head -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Remove head node
	var removed = list.remove(node: n1)
	print("{removed}\n")
	print("{list.count()}\n")
	var h = try list.head() otherwise 'err'
		return 1
	end 'err'
	print("{h.value()}\n")
	// Use n2 and n3 to avoid unused warnings
	print("{n2.value()}\n")
	print("{n3.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
2
20
20
30
```

<!-- test: core.remove-tail -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var n3 = list.insertLast(30)
	// Remove tail node
	var removed = list.remove(node: n3)
	print("{removed}\n")
	print("{list.count()}\n")
	var t = try list.tail() otherwise 'err'
		return 1
	end 'err'
	print("{t.value()}\n")
	// Use n1 and n2 to avoid unused warnings
	print("{n1.value()}\n")
	print("{n2.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30
2
20
10
20
```

<!-- test: core.remove-all -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntManagedList = __ManagedList with Integer

function main() returns ExitCode
	var list = IntManagedList.create()
	var n1 = list.insertLast(10)
	var n2 = list.insertLast(20)
	var removed1 = list.remove(node: n1)
	var removed2 = list.remove(node: n2)
	print("{removed1}\n")
	print("{removed2}\n")
	print("{list.count()}\n")
	if list.isEmpty() 'check'
		print("empty\n")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
0
empty
```
