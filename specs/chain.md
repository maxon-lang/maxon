---
feature: chain
status: experimental
keywords: [chain, linked list, doubly linked, node, collection]
category: collections
---
# Chain

## Documentation

A `Chain` is a doubly linked list with O(1) insertion, removal, and node-level access. Unlike `List`, which provides index-based access, `Chain` exposes node handles (`ChainNode`) that allow direct traversal and manipulation.

Chain owns its nodes via a parent-child memory hierarchy. Nodes are accessed through `ChainNode` handles with refcount-based lifetime.

### Creating a Chain

Create an empty chain with an explicit element type:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntChain = Chain with Integer
var chain = IntChain{}
```

### Inserting Elements

Insert at the front or back, receiving a handle to the new node:

```text
var first = chain.insertFirst(10)
var last = chain.insertLast(30)
var middle = chain.insertAfter(target: first, value: 20)
```

### Traversal

Use node handles to traverse the chain:

```text
var node = try chain.head() otherwise 'err'
  // chain is empty
end 'err'
print("{node.value()}")
var next = try node.next() otherwise 'err2'
  // at end of chain
end 'err2'
```

### Methods

#### insertFirst(value Element) returns ChainNode

Insert a value at the front. Returns a handle to the new node.

#### insertLast(value Element) returns ChainNode

Insert a value at the back. Returns a handle to the new node.

#### insertAfter(target ChainNode, value Element) returns ChainNode

Insert a value after the given node. Returns a handle to the new node.

#### insertBefore(target ChainNode, value Element) returns ChainNode

Insert a value before the given node. Returns a handle to the new node.

#### head() returns ChainNode throws ChainError

Get a handle to the first node. Throws `ChainError.empty` if the chain is empty.

#### tail() returns ChainNode throws ChainError

Get a handle to the last node. Throws `ChainError.empty` if the chain is empty.

#### remove(node ChainNode) returns Element

Remove a node from the chain, returning its value and freeing the node.

#### detach(node ChainNode)

Detach a node from the chain without freeing it.

#### count() returns Count

Get the number of elements.

#### isEmpty() returns bool

Check if the chain is empty.

#### clear()

Remove all elements, freeing all nodes.

### ChainNode Methods

#### value() returns Element

Get the value stored in this node.

#### setValue(v Element)

Replace the value stored in this node.

#### next() returns ChainNode throws ChainError

Get the next node. Throws `ChainError.endOfChain` if at the end.

#### prev() returns ChainNode throws ChainError

Get the previous node. Throws `ChainError.endOfChain` if at the beginning.

## Tests

<!-- test: core.create-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  if chain.isEmpty() 'check'
    print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertFirst(42)
  print("{node.value()}\n")
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertLast(99)
  print("{node.value()}\n")
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n3 = chain.insertFirst(30)
  var n2 = chain.insertFirst(20)
  var n1 = chain.insertFirst(10)
  // Verify via node handles
  print("{n1.value()}\n")
  print("{n2.value()}\n")
  print("{n3.value()}\n")
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Verify via node handles
  print("{n1.value()}\n")
  print("{n2.value()}\n")
  print("{n3.value()}\n")
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertFirst(10)
  var n3 = chain.insertLast(30)
  // Insert 20 after n1
  var n2 = chain.insertAfter(target: n1, value: 20)
  // Verify order via traversal
  var cur = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertFirst(10)
  var n3 = chain.insertLast(30)
  // Insert 20 before n3
  var n2 = chain.insertBefore(target: n3, value: 20)
  // Verify order via traversal
  var cur = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Remove middle node
  var removed = chain.remove(node: n2)
  print("{removed}\n")
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Detach middle node
  chain.detach(node: n2)
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(1)
  var n2 = chain.insertLast(2)
  var n3 = chain.insertLast(3)
  var n4 = chain.insertLast(4)
  var n5 = chain.insertLast(5)
  print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertFirst(42)
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertFirst(10)
  print("{node.value()}\n")
  node.setValue(v: 99)
  print("{node.value()}\n")
  // Verify through chain head too
  var h = try chain.head() otherwise 'err'
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
typealias StringChain = Chain with String

function main() returns ExitCode
  var chain = StringChain{}
  var node = chain.insertFirst("hello world!!!!!!!!!!!!!!")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(1)
  var n2 = chain.insertLast(2)
  var n3 = chain.insertLast(3)
  // Verify nodes exist before clear
  print("{n1.value()}\n")
  print("{n2.value()}\n")
  print("{n3.value()}\n")
  chain.clear()
  if chain.isEmpty() 'check'
    print("{chain.count()}\n")
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  if try chain.head() 'try'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  if try chain.tail() 'try'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertFirst(42)
  // Use the node handle
  print("{node.value()}\n")
  var h = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var node = chain.insertFirst(42)
  // Use the node handle
  print("{node.value()}\n")
  var h = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Move n3 to front: 30, 10, 20
  chain.reinsertFirst(node: n3)
  var cur = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Move n1 to back: 20, 30, 10
  chain.reinsertLast(node: n1)
  var cur = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Remove head node
  var removed = chain.remove(node: n1)
  print("{removed}\n")
  print("{chain.count()}\n")
  var h = try chain.head() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var n3 = chain.insertLast(30)
  // Remove tail node
  var removed = chain.remove(node: n3)
  print("{removed}\n")
  print("{chain.count()}\n")
  var t = try chain.tail() otherwise 'err'
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
typealias IntChain = Chain with Integer

function main() returns ExitCode
  var chain = IntChain{}
  var n1 = chain.insertLast(10)
  var n2 = chain.insertLast(20)
  var removed1 = chain.remove(node: n1)
  var removed2 = chain.remove(node: n2)
  print("{removed1}\n")
  print("{removed2}\n")
  print("{chain.count()}\n")
  if chain.isEmpty() 'check'
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
