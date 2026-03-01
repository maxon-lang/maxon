---
feature: stdlib-array
status: stable
keywords: [stdlib, Array, generic, collection, push, pop, get, set, count, capacity, String, ownership]
category: stdlib
---

# Stdlib Array Type

## Documentation

### Array Type

The `Array` type is a generic, dynamically-sized collection that can hold elements of any type.

### Creating Arrays

Define a concrete type alias for your array:
```text
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int
```

Create an empty array:
```text
var arr = IntArray{}
```

Create an array from a literal:
```text
var arr = [1, 2, 3]
```

### Adding Elements

Use `push` to add elements to the end:
```text
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

var arr = IntArray{}
arr.push(10)
arr.push(20)
arr.push(30)
```

Use `insert` to add at a specific index:
```text
arr.insert(1, 15)  // Insert 15 at index 1
```

### Accessing Elements

Use `get` to access elements (throws on out-of-bounds):
```text
var val = try arr.get(0) otherwise 0  // Returns 0 if out of bounds
```

Use `first` and `last` for convenience:
```text
var first = try arr.first() otherwise 0  // First element or default
var last = try arr.last() otherwise 0    // Last element or default
```

### Modifying Elements

Use `set` to modify an element:
```text
arr.set(0, 100)  // Set first element to 100
```

### Removing Elements

Use `pop` to remove and return the last element:
```text
var val = try arr.pop() otherwise 0  // Pop or use default if empty
```

Use `remove` to remove at a specific index:
```text
arr.remove(1)  // Remove element at index 1
```

Use `clear` to remove all elements:
```text
arr.clear()
```

### Size and Capacity

```text
var size = arr.count()       // Number of elements
var cap = arr.capacity()     // Current capacity
var empty = arr.isEmpty()    // true if count() == 0
arr.reserve(100)             // Ensure capacity >= 100
```

### Iteration

Use `for-in` to iterate over elements:
```text
for item in arr 'loop'
  // use item
end 'loop'
```

## Tests

<!-- test: empty-array -->
Create an empty array and verify it starts empty.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  if arr.count() != 0 'check'
    return 1
  end 'check'
  if arr.isEmpty() == false 'empty'
    return 2
  end 'empty'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-single -->
Push a single element and retrieve it.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(42)
  var val = try arr.get(0) otherwise -1
  if val != 42 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-multiple -->
Push multiple elements and verify count.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)
  if arr.count() != 3 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: get-all-elements -->
Push multiple elements and get each one.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  var v0 = try arr.get(0) otherwise -1
  if v0 != 10 'c0'
    return 1
  end 'c0'

  var v1 = try arr.get(1) otherwise -1
  if v1 != 20 'c1'
    return 2
  end 'c1'

  var v2 = try arr.get(2) otherwise -1
  if v2 != 30 'c2'
    return 3
  end 'c2'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: set-element -->
Set an element and verify the change.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  arr.set(1, value: 99)

  var val = try arr.get(1) otherwise -1
  if val != 99 'check'
    return 1
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: first-last -->
Test first() and last() methods.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  var f = try arr.first() otherwise -1
  if f != 10 'fc'
    return 1
  end 'fc'

  var l = try arr.last() otherwise -1
  if l != 30 'lc'
    return 3
  end 'lc'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: pop-element -->
Pop elements and verify they are removed.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  var val = try arr.pop() otherwise -1
  if val != 30 'check'
    return 1
  end 'check'

  if arr.count() != 2 'cnt'
    return 3
  end 'cnt'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: array-literal -->
Initialize from array literal.

```maxon
function main() returns ExitCode
  var arr = [1, 2, 3]
  
  if arr.count() != 3 'cnt'
    return 1
  end 'cnt'
  
  var v = try arr.get(1) otherwise -1
  if v != 2 'check'
    return 2
  end 'check'
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: for-in-iteration -->
Iterate over array using for-in loop.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  var sum = 0
  for item in arr 'loop'
    sum = sum + item
  end 'loop'

  if sum != 60 'check'
    return 1
  end 'check'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: for-in-double-iteration -->
Iterating the same array twice produces the same results.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  var sum1 = 0
  for item in arr 'loop1'
    sum1 = sum1 + item
  end 'loop1'

  var sum2 = 0
  for item in arr 'loop2'
    sum2 = sum2 + item
  end 'loop2'

  if sum1 == 60 and sum2 == 60 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: isEmpty-transitions -->
Verify isEmpty changes with push/pop.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}

  if arr.isEmpty() == false 'e1'
    return 1
  end 'e1'

  arr.push(42)

  if arr.isEmpty() == true 'e2'
    return 2
  end 'e2'

  var _ = try arr.pop() otherwise 0

  if arr.isEmpty() == false 'e3'
    return 3
  end 'e3'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: clear-array -->
Clear an array and verify it's empty.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)

  arr.clear()

  if arr.count() != 0 'cnt'
    return 1
  end 'cnt'

  if arr.isEmpty() == false 'empty'
    return 2
  end 'empty'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: get-out-of-bounds -->
Get throws error for out of bounds index.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)

  var val = try arr.get(5) otherwise -1
  if val == -1 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: get-empty-array -->
Get on an empty array throws error and is caught by otherwise.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}

  var val = try arr.get(0) otherwise -1
  if val == -1 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: get-empty-module-level-array -->
Get on an empty module-level array throws error and is caught by otherwise.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int
var arr = IntArray{}

function main() returns ExitCode
  var val = try arr.get(0) otherwise -1
  if val == -1 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: reserve-capacity -->
Reserve capacity for efficiency.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.reserve(100)

  if arr.capacity() < 100 'cap'
    return 1
  end 'cap'

  if arr.count() != 0 'cnt'
    return 2
  end 'cnt'

  return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-string-literals -->
Push string literals into an array and retrieve them.

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  arr.push("hello")
  arr.push("world")

  if arr.count() != 2 'cnt'
    return 1
  end 'cnt'

  var s0 = try arr.get(0) otherwise ""
  var s1 = try arr.get(1) otherwise ""
  print(s0)
  print(s1)
  return 0
end 'main'
```
```stdout
helloworld
```
```exitcode
0
```

<!-- test: push-string-literals-long -->
Push longer string literals (heap-allocated) into an array.

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  arr.push("hello this is a longer string")
  arr.push("world this is also a longer string")

  if arr.count() != 2 'cnt'
    return 1
  end 'cnt'

  var s0 = try arr.get(0) otherwise ""
  var s1 = try arr.get(1) otherwise ""
  print(s0)
  print(s1)
  return 0
end 'main'
```
```stdout
hello this is a longer stringworld this is also a longer string
```
```exitcode
0
```

<!-- test: push-string-variables -->
Push string variables into an array.

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  var s1 = "first"
  var s2 = "second"
  arr.push(s1)
  arr.push(s2)

  var v0 = try arr.get(0) otherwise ""
  var v1 = try arr.get(1) otherwise ""
  print(v0)
  print(v1)
  return 0
end 'main'
```
```stdout
firstsecond
```
```exitcode
0
```

<!-- test: string-array-iteration -->
Iterate over an array of strings.

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  arr.push("a")
  arr.push("b")
  arr.push("c")

  for item in arr 'loop'
    print(item)
  end 'loop'
  return 0
end 'main'
```
```stdout
abc
```
```exitcode
0
```

<!-- test: string-array-get -->
Get strings from array using get method.

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  arr.push("one")
  arr.push("two")
  arr.push("three")

  var val = try arr.get(1) otherwise ""
  print(val)
  return 0
end 'main'
```
```stdout
two
```
```exitcode
0
```

<!-- test: string-array-memory -->
Verify string array memory is properly managed (no leaks).

```maxon
typealias StringArray = Array with String

function main() returns ExitCode
  var arr = StringArray{}
  arr.push("test string one")
  arr.push("test string two")
  arr.push("test string three")
  return 0
end 'main'
```
```exitcode
0
```

<!-- test: push-self-assignment -->
Test that `arr = arr.push(value)` pattern works correctly.
This pattern was previously broken due to incorrect refcount handling when the
same variable appears on both sides of an assignment with a mutating method call.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr = arr.push(1)
  arr = arr.push(2)
  arr = arr.push(3)

  var sum = 0
  for n in arr 'loop'
    sum = sum + n
  end 'loop'

  // 1 + 2 + 3 = 6
  return sum
end 'main'
```
```exitcode
6
```

