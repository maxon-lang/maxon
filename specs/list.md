---
feature: list
status: experimental
keywords: [list, linked list, doubly linked, collection, prepend, append]
category: collections
---
# List

## Documentation

A `List` is a doubly linked list collection. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

Internally, List is backed by a `Chain` — a doubly linked chain of nodes that supports O(1) insertion and removal at both ends.

### Creating a List

Use the `List from` syntax with an array literal:

```maxon
var list = List from [1, 2, 3]
```

Or create an empty list with an explicit type:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer
var empty = IntList{}
```

### Methods

#### count() returns Count

Get the number of elements.

#### isEmpty() returns bool

Check if the list is empty.

#### first() returns Element throws ArrayError

Get the first element. Throws if empty.

#### last() returns Element throws ArrayError

Get the last element. Throws if empty.

#### get(index Index) returns Element throws ArrayError

Get element at index (0-based, front to back). Throws if out of bounds.

#### prepend(value Element)

Add an element to the front of the list. O(1).

#### append(value Element)

Add an element to the back of the list. O(1).

#### insert(at Index, value Element)

Insert an element at the given index. O(n).

#### removeFirst() returns Element throws ArrayError

Remove and return the first element. Throws if empty. O(1).

#### removeLast() returns Element throws ArrayError

Remove and return the last element. Throws if empty. O(1).

#### remove(at Index) returns Element throws ArrayError

Remove and return the element at the given index. Throws if out of bounds. O(n).

#### clear()

Remove all elements from the list.

### Iteration

List implements `Iterable`, so it can be used with for-in loops:

```text
for item in list 'loop'
    print("{item}\n")
end 'loop'
```

Elements are iterated front to back.

## Tests

<!-- test: basic.creation -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  return list.count()
end 'main'
```
```exitcode
3
```

<!-- test: basic.empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  if list.isEmpty() 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: basic.first-last -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  var f = try list.first() otherwise 0
  var l = try list.last() otherwise 0
  print("{f}\n")
  print("{l}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
10
30
```

<!-- test: basic.first-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  var f = try list.first() otherwise 99
  return f
end 'main'
```
```exitcode
99
```

<!-- test: basic.last-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  var l = try list.last() otherwise 99
  return l
end 'main'
```
```exitcode
99
```

<!-- test: prepend.single -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  list.prepend(42)
  var f = try list.first() otherwise 0
  return f
end 'main'
```
```exitcode
42
```

<!-- test: prepend.multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  list.prepend(3)
  list.prepend(2)
  list.prepend(1)
  var f = try list.first() otherwise 0
  var l = try list.last() otherwise 0
  print("{f}\n")
  print("{l}\n")
  print("{list.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
3
```

<!-- test: append.single -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  list.append(42)
  var f = try list.first() otherwise 0
  return f
end 'main'
```
```exitcode
42
```

<!-- test: append.multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  list.append(1)
  list.append(2)
  list.append(3)
  var f = try list.first() otherwise 0
  var l = try list.last() otherwise 0
  print("{f}\n")
  print("{l}\n")
  print("{list.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
3
```

<!-- test: get.valid -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30, 40]
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  var v2 = try list.get(2) otherwise 0
  var v3 = try list.get(3) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  print("{v2}\n")
  print("{v3}\n")
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
40
```

<!-- test: get.out-of-bounds -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  var v = try list.get(5) otherwise 99
  return v
end 'main'
```
```exitcode
99
```

<!-- test: insert.at-beginning -->
```maxon
function main() returns ExitCode
  var list = List from [20, 30]
  list.insert(at: 0, value: 10)
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  var v2 = try list.get(2) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  print("{v2}\n")
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
```

<!-- test: insert.at-middle -->
```maxon
function main() returns ExitCode
  var list = List from [10, 30]
  list.insert(at: 1, value: 20)
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  var v2 = try list.get(2) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  print("{v2}\n")
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
```

<!-- test: insert.at-end -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20]
  list.insert(at: 2, value: 30)
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  var v2 = try list.get(2) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  print("{v2}\n")
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
```

<!-- test: remove-first -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  var removed = try list.removeFirst() otherwise 0
  print("{removed}\n")
  print("{list.count()}\n")
  var f = try list.first() otherwise 0
  print("{f}\n")
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
```

<!-- test: remove-last -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  var removed = try list.removeLast() otherwise 0
  print("{removed}\n")
  print("{list.count()}\n")
  var l = try list.last() otherwise 0
  print("{l}\n")
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
```

<!-- test: remove.at-index -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30, 40]
  var removed = try list.remove(at: 1) otherwise 0
  print("{removed}\n")
  print("{list.count()}\n")
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  var v2 = try list.get(2) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  print("{v2}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
20
3
10
30
40
```

<!-- test: iteration.for-in -->
```maxon
function main() returns ExitCode
  var list = List from [10, 20, 30]
  for item in list 'loop'
    print("{item}\n")
  end 'loop'
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
```

<!-- test: mixed-operations -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  list.append(2)
  list.append(3)
  list.prepend(1)
  list.append(4)
  var removed1 = try list.removeFirst() otherwise 0
  var removed2 = try list.removeLast() otherwise 0
  print("{removed1}\n")
  print("{removed2}\n")
  print("{list.count()}\n")
  var v0 = try list.get(0) otherwise 0
  var v1 = try list.get(1) otherwise 0
  print("{v0}\n")
  print("{v1}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
2
2
3
```

<!-- test: strings -->
```maxon
typealias StringList = List with String

function main() returns ExitCode
  var list = StringList{}
  list.append("hello")
  list.append("world")
  var f = try list.first() otherwise "none"
  var l = try list.last() otherwise "none"
  print("{f}\n")
  print("{l}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
world
```

<!-- test: clear -->
```maxon
function main() returns ExitCode
  var list = List from [1, 2, 3]
  list.clear()
  if list.isEmpty() 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: memory.remove-frees-strings -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function testRemove()
  var list = StringList{}
  list.append("hello world!!!!!!!!!!!!!!")
  list.append("goodbye world!!!!!!!!!!!!!")
  var removed = try list.removeFirst() otherwise "none"
  print("{removed}\n")
  print("{list.count()}\n")
end 'testRemove'

function main() returns ExitCode
  testRemove()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world!!!!!!!!!!!!!!
1
```
```stderr
  scope_enter list.testRemove (depth=1)
  alloc EChain rc=0
  alloc StringList rc=0
  move EChain
  incref StringList rc=1
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
    scope_enter stdlib.List.removeFirst (depth=2)
    incref ChainNode rc=0
    move String
    free ChainNode
    incref String rc=1
    move String
    scope_exit stdlib.List.removeFirst (0 owned)
  incref String rc=2
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  alloc ToStringBuf rc=0
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  free ToStringBuf
  decref StringList rc=0
  decref String rc=1
  decref String rc=0
  scope_exit list.testRemove (4 owned)
  free __ManagedMemory
  free String
  free ChainNode
  free EChain
  free StringList
  free __ManagedMemory
  free String
  free Buffer
  free __ManagedMemory
  free String
  free Buffer
  free __ManagedMemory
  free String
```

<!-- test: memory.remove-returns-string -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function testRemove() returns String
  var list = StringList{}
  list.append("hello world!!!!!!!!!!!!!!")
  list.append("goodbye world!!!!!!!!!!!!!")
  return try list.removeFirst() otherwise "none"
end 'testRemove'

function main() returns ExitCode
  var removed = testRemove()
  print("{removed}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world!!!!!!!!!!!!!!
```
```stderr
  scope_enter list.main (depth=1)
    scope_enter list.testRemove (depth=2)
    alloc EChain rc=0
    alloc StringList rc=0
    move EChain
    incref StringList rc=1
    alloc String rc=0
    alloc_in __ManagedMemory
    alloc_in ChainNode
    move String
    alloc String rc=0
    alloc_in __ManagedMemory
    alloc_in ChainNode
    move String
      scope_enter stdlib.List.removeFirst (depth=3)
      incref ChainNode rc=0
      move String
      free ChainNode
      incref String rc=1
      move String
      scope_exit stdlib.List.removeFirst (0 owned)
    incref String rc=2
    move String
    decref StringList rc=0
    decref String rc=1
    scope_exit list.testRemove (1 owned)
    free __ManagedMemory
    free String
    free ChainNode
    free EChain
    free StringList
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  decref String rc=0
  scope_exit list.main (2 owned)
  free __ManagedMemory
  free String
  free Buffer
  free __ManagedMemory
  free String
```

<!-- test: memory.clear-frees-strings -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function testClear()
  var list = StringList{}
  list.append("alpha string!!!!!!!!!!!!!!!")
  list.append("beta string!!!!!!!!!!!!!!!!")
  list.append("gamma string!!!!!!!!!!!!!!!")
  list.clear()
  print("{list.count()}\n")
end 'testClear'

function main() returns ExitCode
  testClear()
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```
```stderr
  scope_enter list.testClear (depth=1)
  alloc EChain rc=0
  alloc StringList rc=0
  move EChain
  incref StringList rc=1
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
    scope_enter stdlib.List.clear (depth=2)
    free __ManagedMemory
    free String
    free ChainNode
    free __ManagedMemory
    free String
    free ChainNode
    free __ManagedMemory
    free String
    free ChainNode
    scope_exit stdlib.List.clear (0 owned)
  alloc ToStringBuf rc=0
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  free ToStringBuf
  decref StringList rc=0
  scope_exit list.testClear (2 owned)
  free EChain
  free StringList
  free Buffer
  free __ManagedMemory
  free String
```

<!-- test: memory.clear-passed-list -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function clearList(list StringList)
  list.clear()
end 'clearList'

function main() returns ExitCode
  var list = StringList{}
  list.append("alpha string!!!!!!!!!!!!!!!")
  list.append("beta string!!!!!!!!!!!!!!!!")
  list.append("gamma string!!!!!!!!!!!!!!!")
  clearList(list)
  print("{list.count()}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```
```stderr
  scope_enter list.main (depth=1)
  alloc EChain rc=0
  alloc StringList rc=0
  move EChain
  incref StringList rc=1
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in ChainNode
  move String
    scope_enter stdlib.List.clear (depth=2)
    free __ManagedMemory
    free String
    free ChainNode
    free __ManagedMemory
    free String
    free ChainNode
    free __ManagedMemory
    free String
    free ChainNode
    scope_exit stdlib.List.clear (0 owned)
  alloc ToStringBuf rc=0
  alloc String rc=0
  alloc_in __ManagedMemory
  alloc_in Buffer
  free ToStringBuf
  decref StringList rc=0
  scope_exit list.main (2 owned)
  free EChain
  free StringList
  free Buffer
  free __ManagedMemory
  free String
```
