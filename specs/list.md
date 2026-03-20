---
feature: list
status: experimental
keywords: [list, linked list, doubly linked, collection, prepend, append]
category: collections
---
# List

## Documentation

A `List` is a doubly linked list collection. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

Internally, List is backed by a `__ManagedList` — a doubly linked list of nodes that supports O(1) insertion and removal at both ends.

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

Elements are iterated front to back. Iteration uses a cached cursor on the underlying managed list for O(n) total traversal (O(1) per element).

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
  try list.insert(at: 0, value: 10) otherwise 'err'
    return 1
  end 'err'
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
  try list.insert(at: 1, value: 20) otherwise 'err'
    return 1
  end 'err'
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
  try list.insert(at: 2, value: 30) otherwise 'err'
    return 1
  end 'err'
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
sl_init
  os_alloc size=67108864
mm_alloc __ManagedList_String #1 size=32 [list.testRemove]
  sl_alloc __ManagedList_String #1 size=64 class=5
mm_alloc StringList #2 size=16 [list.testRemove]
  sl_alloc StringList #2 size=48 class=4
mm_incref __ManagedList_String #1 rc=1 [list.testRemove]
mm_incref StringList #2 rc=1 [list.testRemove]
mm_alloc String #3 size=16 [list.testRemove]
  sl_alloc String #3 size=48 class=4
mm_alloc __ManagedMemory #4 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [list.testRemove]
mm_incref String #3 rc=1 [list.testRemove]
mm_alloc __ManagedListNode #5 size=32 [StringList.append]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.append]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_alloc String #6 size=16 [list.testRemove]
  sl_alloc String #6 size=48 class=4
mm_alloc __ManagedMemory #7 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #7 size=64 class=5
mm_incref __ManagedMemory #7 rc=1 [list.testRemove]
mm_incref String #6 rc=1 [list.testRemove]
mm_alloc __ManagedListNode #8 size=32 [StringList.append]
  sl_alloc __ManagedListNode #8 size=64 class=5
mm_incref String #6 rc=2 [StringList.append]
mm_incref __ManagedListNode #8 rc=1 [managed_list_insert]
mm_incref __ManagedListNode #5 rc=2 [StringList.removeFirst]
mm_incref __ManagedListNode #5 rc=3 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=2 [StringList.removeFirst]
mm_decref String #3 rc=1 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=1 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=0 [StringList.removeFirst]
  mm_free __ManagedListNode #5
    sl_free __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.removeFirst]
mm_transfer String #3 rc=2 [StringList.removeFirst]
mm_incref String #3 rc=3 [list.testRemove]
mm_alloc String #9 size=16 [list.testRemove]
  sl_alloc String #9 size=48 class=4
mm_alloc __ManagedMemory #10 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #10 size=64 class=5
mm_raw_alloc #R1 size=27 [interp.buf [list.testRemove]]
  sl_alloc size=27 class=3
mm_incref __ManagedMemory #10 rc=1 [list.testRemove]
mm_incref String #9 rc=1 [list.testRemove]
mm_raw_alloc #R2 size=21 [toStr.buf [list.testRemove]]
  sl_alloc size=21 class=2
mm_alloc String #11 size=16 [list.testRemove]
  sl_alloc String #11 size=48 class=4
mm_alloc __ManagedMemory #12 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #12 size=64 class=5
mm_raw_alloc #R3 size=3 [interp.buf [list.testRemove]]
  sl_alloc size=3 class=0
mm_raw_free #R2
  sl_free size=24 class=2
mm_incref __ManagedMemory #12 rc=1 [list.testRemove]
mm_incref String #11 rc=1 [list.testRemove]
mm_decref String #11 rc=0 [list.testRemove]
  mm_decref __ManagedMemory #12 rc=0 [~String]
    mm_raw_free #R3
      sl_free size=8 class=0
    mm_free __ManagedMemory #12
      sl_free __ManagedMemory #12 size=64 class=5
  mm_free String #11
    sl_free String #11 size=48 class=4
mm_decref String #9 rc=0 [list.testRemove]
  mm_decref __ManagedMemory #10 rc=0 [~String]
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory #10
      sl_free __ManagedMemory #10 size=64 class=5
  mm_free String #9
    sl_free String #9 size=48 class=4
mm_decref String #3 rc=2 [list.testRemove]
mm_decref String #6 rc=1 [list.testRemove]
mm_decref String #3 rc=1 [list.testRemove]
mm_decref String #3 rc=0 [list.testRemove]
  mm_decref __ManagedMemory #4 rc=0 [~String]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free String #3
    sl_free String #3 size=48 class=4
mm_decref StringList #2 rc=0 [list.testRemove]
  mm_decref __ManagedList_String #1 rc=0 [~StringList]
    mm_decref String #6 rc=0 [managed_list_clear]
      mm_decref __ManagedMemory #7 rc=0 [~String]
        mm_free __ManagedMemory #7
          sl_free __ManagedMemory #7 size=64 class=5
      mm_free String #6
        sl_free String #6 size=48 class=4
    mm_decref __ManagedListNode #8 rc=0 [managed_list_clear]
      mm_free __ManagedListNode #8
        sl_free __ManagedListNode #8 size=64 class=5
    mm_free __ManagedList_String #1
      sl_free __ManagedList_String #1 size=64 class=5
  mm_free StringList #2
    sl_free StringList #2 size=48 class=4
mm_raw_alloc #R4 size=40
  sl_alloc size=40 class=4
mm_raw_free #R4
  sl_free size=48 class=4
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
sl_init
  os_alloc size=67108864
mm_alloc __ManagedList_String #1 size=32 [list.testRemove]
  sl_alloc __ManagedList_String #1 size=64 class=5
mm_alloc StringList #2 size=16 [list.testRemove]
  sl_alloc StringList #2 size=48 class=4
mm_incref __ManagedList_String #1 rc=1 [list.testRemove]
mm_incref StringList #2 rc=1 [list.testRemove]
mm_alloc String #3 size=16 [list.testRemove]
  sl_alloc String #3 size=48 class=4
mm_alloc __ManagedMemory #4 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [list.testRemove]
mm_incref String #3 rc=1 [list.testRemove]
mm_alloc __ManagedListNode #5 size=32 [StringList.append]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.append]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_alloc String #6 size=16 [list.testRemove]
  sl_alloc String #6 size=48 class=4
mm_alloc __ManagedMemory #7 size=32 [list.testRemove]
  sl_alloc __ManagedMemory #7 size=64 class=5
mm_incref __ManagedMemory #7 rc=1 [list.testRemove]
mm_incref String #6 rc=1 [list.testRemove]
mm_alloc __ManagedListNode #8 size=32 [StringList.append]
  sl_alloc __ManagedListNode #8 size=64 class=5
mm_incref String #6 rc=2 [StringList.append]
mm_incref __ManagedListNode #8 rc=1 [managed_list_insert]
mm_incref __ManagedListNode #5 rc=2 [StringList.removeFirst]
mm_incref __ManagedListNode #5 rc=3 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=2 [StringList.removeFirst]
mm_decref String #3 rc=1 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=1 [StringList.removeFirst]
mm_decref __ManagedListNode #5 rc=0 [StringList.removeFirst]
  mm_free __ManagedListNode #5
    sl_free __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.removeFirst]
mm_transfer String #3 rc=2 [StringList.removeFirst]
mm_transfer String #3 rc=2 [list.testRemove]
mm_decref String #6 rc=1 [list.testRemove]
mm_decref String #3 rc=1 [list.testRemove]
mm_decref StringList #2 rc=0 [list.testRemove]
  mm_decref __ManagedList_String #1 rc=0 [~StringList]
    mm_decref String #6 rc=0 [managed_list_clear]
      mm_decref __ManagedMemory #7 rc=0 [~String]
        mm_free __ManagedMemory #7
          sl_free __ManagedMemory #7 size=64 class=5
      mm_free String #6
        sl_free String #6 size=48 class=4
    mm_decref __ManagedListNode #8 rc=0 [managed_list_clear]
      mm_free __ManagedListNode #8
        sl_free __ManagedListNode #8 size=64 class=5
    mm_free __ManagedList_String #1
      sl_free __ManagedList_String #1 size=64 class=5
  mm_free StringList #2
    sl_free StringList #2 size=48 class=4
mm_alloc String #9 size=16 [list.main]
  sl_alloc String #9 size=48 class=4
mm_alloc __ManagedMemory #10 size=32 [list.main]
  sl_alloc __ManagedMemory #10 size=64 class=5
mm_raw_alloc #R1 size=27 [interp.buf [list.main]]
  sl_alloc size=27 class=3
mm_incref __ManagedMemory #10 rc=1 [list.main]
mm_incref String #9 rc=1 [list.main]
mm_decref String #9 rc=0 [list.main]
  mm_decref __ManagedMemory #10 rc=0 [~String]
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory #10
      sl_free __ManagedMemory #10 size=64 class=5
  mm_free String #9
    sl_free String #9 size=48 class=4
mm_decref String #3 rc=0 [list.main]
  mm_decref __ManagedMemory #4 rc=0 [~String]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free String #3
    sl_free String #3 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
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
sl_init
  os_alloc size=67108864
mm_alloc __ManagedList_String #1 size=32 [list.testClear]
  sl_alloc __ManagedList_String #1 size=64 class=5
mm_alloc StringList #2 size=16 [list.testClear]
  sl_alloc StringList #2 size=48 class=4
mm_incref __ManagedList_String #1 rc=1 [list.testClear]
mm_incref StringList #2 rc=1 [list.testClear]
mm_alloc String #3 size=16 [list.testClear]
  sl_alloc String #3 size=48 class=4
mm_alloc __ManagedMemory #4 size=32 [list.testClear]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [list.testClear]
mm_incref String #3 rc=1 [list.testClear]
mm_alloc __ManagedListNode #5 size=32 [StringList.append]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.append]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_alloc String #6 size=16 [list.testClear]
  sl_alloc String #6 size=48 class=4
mm_alloc __ManagedMemory #7 size=32 [list.testClear]
  sl_alloc __ManagedMemory #7 size=64 class=5
mm_incref __ManagedMemory #7 rc=1 [list.testClear]
mm_incref String #6 rc=1 [list.testClear]
mm_alloc __ManagedListNode #8 size=32 [StringList.append]
  sl_alloc __ManagedListNode #8 size=64 class=5
mm_incref String #6 rc=2 [StringList.append]
mm_incref __ManagedListNode #8 rc=1 [managed_list_insert]
mm_alloc String #9 size=16 [list.testClear]
  sl_alloc String #9 size=48 class=4
mm_alloc __ManagedMemory #10 size=32 [list.testClear]
  sl_alloc __ManagedMemory #10 size=64 class=5
mm_incref __ManagedMemory #10 rc=1 [list.testClear]
mm_incref String #9 rc=1 [list.testClear]
mm_alloc __ManagedListNode #11 size=32 [StringList.append]
  sl_alloc __ManagedListNode #11 size=64 class=5
mm_incref String #9 rc=2 [StringList.append]
mm_incref __ManagedListNode #11 rc=1 [managed_list_insert]
mm_decref String #3 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #5 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #5
    sl_free __ManagedListNode #5 size=64 class=5
mm_decref String #6 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #8 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #8
    sl_free __ManagedListNode #8 size=64 class=5
mm_decref String #9 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #11 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #11
    sl_free __ManagedListNode #11 size=64 class=5
mm_raw_alloc #R1 size=21 [toStr.buf [list.testClear]]
  sl_alloc size=21 class=2
mm_alloc String #12 size=16 [list.testClear]
  sl_alloc String #12 size=48 class=4
mm_alloc __ManagedMemory #13 size=32 [list.testClear]
  sl_alloc __ManagedMemory #13 size=64 class=5
mm_raw_alloc #R2 size=3 [interp.buf [list.testClear]]
  sl_alloc size=3 class=0
mm_raw_free #R1
  sl_free size=24 class=2
mm_incref __ManagedMemory #13 rc=1 [list.testClear]
mm_incref String #12 rc=1 [list.testClear]
mm_decref String #12 rc=0 [list.testClear]
  mm_decref __ManagedMemory #13 rc=0 [~String]
    mm_raw_free #R2
      sl_free size=8 class=0
    mm_free __ManagedMemory #13
      sl_free __ManagedMemory #13 size=64 class=5
  mm_free String #12
    sl_free String #12 size=48 class=4
mm_decref String #9 rc=0 [list.testClear]
  mm_decref __ManagedMemory #10 rc=0 [~String]
    mm_free __ManagedMemory #10
      sl_free __ManagedMemory #10 size=64 class=5
  mm_free String #9
    sl_free String #9 size=48 class=4
mm_decref String #6 rc=0 [list.testClear]
  mm_decref __ManagedMemory #7 rc=0 [~String]
    mm_free __ManagedMemory #7
      sl_free __ManagedMemory #7 size=64 class=5
  mm_free String #6
    sl_free String #6 size=48 class=4
mm_decref String #3 rc=0 [list.testClear]
  mm_decref __ManagedMemory #4 rc=0 [~String]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free String #3
    sl_free String #3 size=48 class=4
mm_decref StringList #2 rc=0 [list.testClear]
  mm_decref __ManagedList_String #1 rc=0 [~StringList]
    mm_free __ManagedList_String #1
      sl_free __ManagedList_String #1 size=64 class=5
  mm_free StringList #2
    sl_free StringList #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
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
sl_init
  os_alloc size=67108864
mm_alloc __ManagedList_String #1 size=32 [list.main]
  sl_alloc __ManagedList_String #1 size=64 class=5
mm_alloc StringList #2 size=16 [list.main]
  sl_alloc StringList #2 size=48 class=4
mm_incref __ManagedList_String #1 rc=1 [list.main]
mm_incref StringList #2 rc=1 [list.main]
mm_alloc String #3 size=16 [list.main]
  sl_alloc String #3 size=48 class=4
mm_alloc __ManagedMemory #4 size=32 [list.main]
  sl_alloc __ManagedMemory #4 size=64 class=5
mm_incref __ManagedMemory #4 rc=1 [list.main]
mm_incref String #3 rc=1 [list.main]
mm_alloc __ManagedListNode #5 size=32 [StringList.append]
  sl_alloc __ManagedListNode #5 size=64 class=5
mm_incref String #3 rc=2 [StringList.append]
mm_incref __ManagedListNode #5 rc=1 [managed_list_insert]
mm_alloc String #6 size=16 [list.main]
  sl_alloc String #6 size=48 class=4
mm_alloc __ManagedMemory #7 size=32 [list.main]
  sl_alloc __ManagedMemory #7 size=64 class=5
mm_incref __ManagedMemory #7 rc=1 [list.main]
mm_incref String #6 rc=1 [list.main]
mm_alloc __ManagedListNode #8 size=32 [StringList.append]
  sl_alloc __ManagedListNode #8 size=64 class=5
mm_incref String #6 rc=2 [StringList.append]
mm_incref __ManagedListNode #8 rc=1 [managed_list_insert]
mm_alloc String #9 size=16 [list.main]
  sl_alloc String #9 size=48 class=4
mm_alloc __ManagedMemory #10 size=32 [list.main]
  sl_alloc __ManagedMemory #10 size=64 class=5
mm_incref __ManagedMemory #10 rc=1 [list.main]
mm_incref String #9 rc=1 [list.main]
mm_alloc __ManagedListNode #11 size=32 [StringList.append]
  sl_alloc __ManagedListNode #11 size=64 class=5
mm_incref String #9 rc=2 [StringList.append]
mm_incref __ManagedListNode #11 rc=1 [managed_list_insert]
mm_decref String #3 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #5 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #5
    sl_free __ManagedListNode #5 size=64 class=5
mm_decref String #6 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #8 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #8
    sl_free __ManagedListNode #8 size=64 class=5
mm_decref String #9 rc=1 [managed_list_clear]
mm_decref __ManagedListNode #11 rc=0 [managed_list_clear]
  mm_free __ManagedListNode #11
    sl_free __ManagedListNode #11 size=64 class=5
mm_raw_alloc #R1 size=21 [toStr.buf [list.main]]
  sl_alloc size=21 class=2
mm_alloc String #12 size=16 [list.main]
  sl_alloc String #12 size=48 class=4
mm_alloc __ManagedMemory #13 size=32 [list.main]
  sl_alloc __ManagedMemory #13 size=64 class=5
mm_raw_alloc #R2 size=3 [interp.buf [list.main]]
  sl_alloc size=3 class=0
mm_raw_free #R1
  sl_free size=24 class=2
mm_incref __ManagedMemory #13 rc=1 [list.main]
mm_incref String #12 rc=1 [list.main]
mm_decref String #12 rc=0 [list.main]
  mm_decref __ManagedMemory #13 rc=0 [~String]
    mm_raw_free #R2
      sl_free size=8 class=0
    mm_free __ManagedMemory #13
      sl_free __ManagedMemory #13 size=64 class=5
  mm_free String #12
    sl_free String #12 size=48 class=4
mm_decref String #9 rc=0 [list.main]
  mm_decref __ManagedMemory #10 rc=0 [~String]
    mm_free __ManagedMemory #10
      sl_free __ManagedMemory #10 size=64 class=5
  mm_free String #9
    sl_free String #9 size=48 class=4
mm_decref String #6 rc=0 [list.main]
  mm_decref __ManagedMemory #7 rc=0 [~String]
    mm_free __ManagedMemory #7
      sl_free __ManagedMemory #7 size=64 class=5
  mm_free String #6
    sl_free String #6 size=48 class=4
mm_decref String #3 rc=0 [list.main]
  mm_decref __ManagedMemory #4 rc=0 [~String]
    mm_free __ManagedMemory #4
      sl_free __ManagedMemory #4 size=64 class=5
  mm_free String #3
    sl_free String #3 size=48 class=4
mm_decref StringList #2 rc=0 [list.main]
  mm_decref __ManagedList_String #1 rc=0 [~StringList]
    mm_free __ManagedList_String #1
      sl_free __ManagedList_String #1 size=64 class=5
  mm_free StringList #2
    sl_free StringList #2 size=48 class=4
mm_raw_alloc #R3 size=40
  sl_alloc size=40 class=4
mm_raw_free #R3
  sl_free size=48 class=4
```

<!-- test: memory.value-survives-clear-and-return -->
### Borrow conflict: first then clear in function
Getting a value from a list and then clearing it is a borrow conflict, even in a helper function.
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
```maxoncstderr
error E3070: specs/fragments/list/memory.value-survives-clear-and-return.test:6:8: cannot mutate 'list' via 'clear' while it is borrowed by 'val' (borrowed at line 5)
```

<!-- test: memory.value-survives-clear-error -->
### Borrow conflict: indirect mutation via helper function
Passing a borrowed-from list to a function that clears it is a borrow conflict.
```maxon
typealias StringList = List with String

function clearList(list StringList)
  list.clear()
end 'clearList'

function main() returns ExitCode
  var list = StringList{}
  list.append("hello world!!!!!!!!!!!!!!")
  var val = try list.first() otherwise "none"
  clearList(list)
  print("{val}\n")
  return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/list/memory.value-survives-clear-error.test:12:3: cannot mutate 'list' via 'clearList' while it is borrowed by 'val' (borrowed at line 11)
```

<!-- test: memory.value-survives-clear -->
### Borrow conflict: first then clear
Getting a value from a list and then clearing it is a borrow conflict.
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
```maxoncstderr
error E3070: specs/fragments/list/memory.value-survives-clear.test:8:8: cannot mutate 'list' via 'clear' while it is borrowed by 'val' (borrowed at line 7)
```
