---
feature: list
status: experimental
keywords: [list, linked list, doubly linked, collection, prepend, append]
category: collections
---
# List

## Documentation

A `List` is a doubly linked list collection. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

Internally, List is backed by a `__Chain` — a doubly linked chain of nodes that supports O(1) insertion and removal at both ends.

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

Elements are iterated front to back. Iteration uses a cached cursor on the underlying chain for O(n) total traversal (O(1) per element).

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

<!-- test: iteration.large-list -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
  var list = IntList{}
  for i in 1 to 100 'build'
    list.append(i)
  end 'build'
  var sum = 0
  for item in list 'loop'
    sum = sum + item
  end 'loop'
  return sum - 5050
end 'main'
```
```exitcode
0
```

<!-- test: iteration.two-loops -->
```maxon
function main() returns ExitCode
  var list = List from [1, 2, 3]
  var sum1 = 0
  for item in list 'loop1'
    sum1 = sum1 + item
  end 'loop1'
  var sum2 = 0
  for item in list 'loop2'
    sum2 = sum2 + item
  end 'loop2'
  if sum1 == sum2 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
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
alloc EChain #1 rc=0 [list.testRemove]
alloc StringList #2 rc=0 [list.testRemove]
incref EChain #1 rc=1 [list.testRemove]
incref StringList #2 rc=1 [list.testRemove]
alloc String #3 rc=0 [list.testRemove]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.testRemove]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
alloc String #6 rc=0 [list.testRemove]
alloc_in __ManagedMemory
incref String #6 rc=1 [list.testRemove]
alloc_in __ChainNode
incref String #6 rc=2 [StringList.append]
incref __ChainNode #5 rc=1 [StringList.removeFirst]
  free __ChainNode #5
alloc String #9 rc=0 [list.testRemove]
alloc_in __ManagedMemory
alloc_in Buffer
incref String #9 rc=1 [list.testRemove]
alloc ToStringBuf #12 rc=0 [list.testRemove]
alloc String #13 rc=0 [list.testRemove]
alloc_in __ManagedMemory
alloc_in Buffer
  free ToStringBuf #12
incref String #13 rc=1 [list.testRemove]
decref StringList #2 rc=0 [list.testRemove]
decref EChain #1 rc=0 [list.testRemove]
decref String #6 rc=1
free EChain #1 [list.testRemove]
free StringList #2 [list.testRemove]
decref String #3 rc=1 [list.testRemove]
decref String #3 rc=0 [list.testRemove]
free String #3 [list.testRemove]
decref String #6 rc=0 [list.testRemove]
free String #6 [list.testRemove]
decref String #9 rc=0 [list.testRemove]
free String #9 [list.testRemove]
decref String #13 rc=0 [list.testRemove]
free String #13 [list.testRemove]
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
alloc EChain #1 rc=0 [list.testRemove]
alloc StringList #2 rc=0 [list.testRemove]
incref EChain #1 rc=1 [list.testRemove]
incref StringList #2 rc=1 [list.testRemove]
alloc String #3 rc=0 [list.testRemove]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.testRemove]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
alloc String #6 rc=0 [list.testRemove]
alloc_in __ManagedMemory
incref String #6 rc=1 [list.testRemove]
alloc_in __ChainNode
incref String #6 rc=2 [StringList.append]
incref __ChainNode #5 rc=1 [StringList.removeFirst]
  free __ChainNode #5
decref StringList #2 rc=0 [list.testRemove]
decref EChain #1 rc=0 [list.testRemove]
decref String #6 rc=1
free EChain #1 [list.testRemove]
free StringList #2 [list.testRemove]
decref String #3 rc=1 [list.testRemove]
decref String #6 rc=0 [list.testRemove]
free String #6 [list.testRemove]
transfer String #3 rc=1 [list.testRemove]
alloc String #9 rc=0 [list.main]
alloc_in __ManagedMemory
alloc_in Buffer
incref String #9 rc=1 [list.main]
decref String #3 rc=0 [list.main]
free String #3 [list.main]
decref String #9 rc=0 [list.main]
free String #9 [list.main]
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
alloc EChain #1 rc=0 [list.testClear]
alloc StringList #2 rc=0 [list.testClear]
incref EChain #1 rc=1 [list.testClear]
incref StringList #2 rc=1 [list.testClear]
alloc String #3 rc=0 [list.testClear]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.testClear]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
alloc String #6 rc=0 [list.testClear]
alloc_in __ManagedMemory
incref String #6 rc=1 [list.testClear]
alloc_in __ChainNode
incref String #6 rc=2 [StringList.append]
alloc String #9 rc=0 [list.testClear]
alloc_in __ManagedMemory
incref String #9 rc=1 [list.testClear]
alloc_in __ChainNode
incref String #9 rc=2 [StringList.append]
decref String #3 rc=1
  free __ChainNode #5
decref String #6 rc=1
  free __ChainNode #8
decref String #9 rc=1
  free __ChainNode #11
alloc ToStringBuf #12 rc=0 [list.testClear]
alloc String #13 rc=0 [list.testClear]
alloc_in __ManagedMemory
alloc_in Buffer
  free ToStringBuf #12
incref String #13 rc=1 [list.testClear]
decref StringList #2 rc=0 [list.testClear]
decref EChain #1 rc=0 [list.testClear]
free EChain #1 [list.testClear]
free StringList #2 [list.testClear]
decref String #3 rc=0 [list.testClear]
free String #3 [list.testClear]
decref String #6 rc=0 [list.testClear]
free String #6 [list.testClear]
decref String #9 rc=0 [list.testClear]
free String #9 [list.testClear]
decref String #13 rc=0 [list.testClear]
free String #13 [list.testClear]
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
alloc EChain #1 rc=0 [list.main]
alloc StringList #2 rc=0 [list.main]
incref EChain #1 rc=1 [list.main]
incref StringList #2 rc=1 [list.main]
alloc String #3 rc=0 [list.main]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.main]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
alloc String #6 rc=0 [list.main]
alloc_in __ManagedMemory
incref String #6 rc=1 [list.main]
alloc_in __ChainNode
incref String #6 rc=2 [StringList.append]
alloc String #9 rc=0 [list.main]
alloc_in __ManagedMemory
incref String #9 rc=1 [list.main]
alloc_in __ChainNode
incref String #9 rc=2 [StringList.append]
decref String #3 rc=1
  free __ChainNode #5
decref String #6 rc=1
  free __ChainNode #8
decref String #9 rc=1
  free __ChainNode #11
alloc ToStringBuf #12 rc=0 [list.main]
alloc String #13 rc=0 [list.main]
alloc_in __ManagedMemory
alloc_in Buffer
  free ToStringBuf #12
incref String #13 rc=1 [list.main]
decref StringList #2 rc=0 [list.main]
decref EChain #1 rc=0 [list.main]
free EChain #1 [list.main]
free StringList #2 [list.main]
decref String #3 rc=0 [list.main]
free String #3 [list.main]
decref String #6 rc=0 [list.main]
free String #6 [list.main]
decref String #9 rc=0 [list.main]
free String #9 [list.main]
decref String #13 rc=0 [list.main]
free String #13 [list.main]
```

<!-- test: memory.value-survives-clear-and-return -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function clearList(list StringList) returns String
  var val = try list.first() otherwise "none"
  list.clear()
  return val
end 'clearList'

function main() returns ExitCode
  var list = StringList{}
  list.append("hello world!!!!!!!!!!!!!!")
  var result = clearList(list)
  print("{result}\n")
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
alloc EChain #1 rc=0 [list.main]
alloc StringList #2 rc=0 [list.main]
incref EChain #1 rc=1 [list.main]
incref StringList #2 rc=1 [list.main]
alloc String #3 rc=0 [list.main]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.main]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
incref __ChainNode #5 rc=1 [StringList.first]
decref __ChainNode #5 rc=0 [StringList.first]
  free __ChainNode #5
incref String #3 rc=3 [StringList.first]
transfer String #3 rc=3 [StringList.first]
decref String #3 rc=2
mm_free called on unmanaged pointer (no AllocEntry)Stack trace:
  in StringList.clear
  in StringList.clear
  in StringList.clear
  in list.clearList
  in list.main
  in _start
```

<!-- test: memory.value-survives-clear -->
<!-- MmTrace -->
```maxon
typealias StringList = List with String

function main() returns ExitCode
  var list = StringList{}
  list.append("hello world!!!!!!!!!!!!!!")
  var val = try list.first() otherwise "none"
  list.clear()
  print("{val}\n")
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
alloc EChain #1 rc=0 [list.main]
alloc StringList #2 rc=0 [list.main]
incref EChain #1 rc=1 [list.main]
incref StringList #2 rc=1 [list.main]
alloc String #3 rc=0 [list.main]
alloc_in __ManagedMemory
incref String #3 rc=1 [list.main]
alloc_in __ChainNode
incref String #3 rc=2 [StringList.append]
incref __ChainNode #5 rc=1 [StringList.first]
decref __ChainNode #5 rc=0 [StringList.first]
  free __ChainNode #5
incref String #3 rc=3 [StringList.first]
transfer String #3 rc=3 [StringList.first]
decref String #3 rc=2
mm_free called on unmanaged pointer (no AllocEntry)Stack trace:
  in StringList.clear
  in StringList.clear
  in StringList.clear
  in list.main
  in _start
```
