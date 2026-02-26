---
feature: list
status: experimental
keywords: [list, linked list, doubly linked, collection, prepend, append]
category: collections
---
# List

## Documentation

A `List` is a doubly linked list collection. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

Internally, each node is represented as a discriminated union (`ListNode`) that is either `empty` or contains a value with links to the previous and next nodes.

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

<!-- disabled-test: strings -->
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
