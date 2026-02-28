---
feature: managed-memory-methods
status: experimental
keywords: [managed-memory, methods, builtin, buffer]
category: dev
---

# __ManagedMemory Methods

## Documentation

`__ManagedMemory` is a compiler builtin type providing heap-backed buffer storage. It has instance methods for element access, mutation, and buffer management, as well as static methods for creation.

### Instance Methods

- `length()` returns int
- `capacity()` returns int
- `elementSize()` returns int
- `setLength(n)` — set element count
- `get(index)` returns Element
- `set(index, value)`
- `grow(newCapacity)`
- `shiftRight(index, count)`
- `shiftLeft(index, count)`
- `byteAt(index)` returns int
- `setByte(index, value)`
- `concat(other)` returns __ManagedMemory
- `slice(start, end)` returns __ManagedMemory
- `toCString()` returns int
- `makeCharFromBytes(pos, len)` returns int

### Static Methods

- `__ManagedMemory.create(capacity, elementSize)` returns __ManagedMemory
- `__ManagedMemory.fromCString(ptr)` returns __ManagedMemory

## Tests

<!-- test: array-via-methods -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)
  arr.push(40)
  arr.push(50)
  return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: array-get-set -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)
  arr.set(1, value: 99)
  var v = try arr.get(1) otherwise 'err'
    return 0
  end 'err'
  return v
end 'main'
```
```exitcode
99
```

<!-- test: array-slice -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(20)
  arr.push(30)
  arr.push(40)
  arr.push(50)
  var sliced = arr.slice(1, endIndex: 4)
  return sliced.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-insert-remove -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  arr.push(10)
  arr.push(30)
  arr.insert(1, value: 20)
  var removed = try arr.remove(0) otherwise 'err'
    return 99
  end 'err'
  return removed + arr.count()
end 'main'
```
```exitcode
12
```

<!-- test: string-operations -->
```maxon
function main() returns ExitCode
  var s = "hello world"
  return s.byteLength()
end 'main'
```
```exitcode
11
```

<!-- test: string-concat -->
```maxon
function main() returns ExitCode
  var a = "hello"
  var b = " world"
  var c = a.concat(b)
  return c.byteLength()
end 'main'
```
```exitcode
11
```

<!-- test: array-literal -->
```maxon
function main() returns ExitCode
  var arr = [10, 20, 30, 40]
  var v = try arr.get(2) otherwise 'err'
    return 0
  end 'err'
  return v
end 'main'
```
```exitcode
30
```

<!-- test: array-growth -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
  var arr = IntArray{}
  var i = 0
  while i < 100 'fill'
    arr.push(i)
    i = i + 1
  end 'fill'
  return arr.count()
end 'main'
```
```exitcode
100
```
