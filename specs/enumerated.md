---
feature: enumerated
status: stable
keywords: [enumerated, index, iterator, for-in, array]
category: stdlib
---

# Enumerated

## Documentation

### Overview

The `enumerated()` method returns an iterator that yields `(Index, Element)` tuples, providing both the index and value during iteration. This is useful when you need to know the position of each element while iterating.

### Usage

Call `enumerated()` on an array, then destructure the tuple in a `for` loop:

```text
var names = ["Alice", "Bob", "Charlie"]
for (index, name) in names.enumerated() 'loop'
  print("{index}: {name}\n")
end 'loop'
// 0: Alice
// 1: Bob
// 2: Charlie
```

### Available On

All `Iterable` types (Array, String, Map, Set, List, etc.) via the `Iterable` extension.

## Tests

<!-- test: enumerated.basic-int -->
```maxon
function main() returns ExitCode
  var arr = [10, 20, 30]
  for (index, value) in arr.enumerated() 'loop'
    print("{index}:{value}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:20
2:30
```

<!-- test: enumerated.basic-string -->
```maxon
function main() returns ExitCode
  var arr = ["hello", "world"]
  for (i, s) in arr.enumerated() 'loop'
    print("{i}={s}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0=hello
1=world
```

<!-- test: enumerated.empty-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
  var arr = IntArray{}
  var count = 0
  for (index, value) in arr.enumerated() 'loop'
    count = count + 1
  end 'loop'
  print("{count}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: enumerated.single-element -->
```maxon
function main() returns ExitCode
  var arr = [42]
  for (i, v) in arr.enumerated() 'loop'
    print("{i}:{v}\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0:42
```

<!-- test: enumerated.use-index -->
```maxon
function main() returns ExitCode
  var arr = [100, 200, 300, 400]
  var total = 0
  for (i, v) in arr.enumerated() 'loop'
    total = total + i * v
  end 'loop'
  print("{total}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2000
```

<!-- test: enumerated.with-struct -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
  export var x Integer
  export var y Integer
end 'Point'

function main() returns ExitCode
  var points = [Point{x: 1, y: 2}, Point{x: 3, y: 4}]
  for (i, p) in points.enumerated() 'loop'
    print("{i}:({p.x},{p.y})\n")
  end 'loop'
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0:(1,2)
1:(3,4)
```
