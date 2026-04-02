---
feature: managed-memory-methods
status: experimental
keywords: [managed-memory, methods, builtin, buffer]
category: dev
---

# __ManagedMemory Methods

## Documentation

`__ManagedMemory` is a compiler builtin type providing heap-backed buffer storage. It has instance methods for element access, mutation, and buffer management, as well as static methods for creation. All element-access and mutation methods perform runtime bounds checking and panic on invalid access.

### Instance Methods

- `length()` returns int
- `capacity()` returns int
- `elementSize()` returns int
- `setLength(n)` — set element count (panics if n > capacity)
- `get(index)` returns Element (panics if index >= length)
- `set(index, value)` (panics if index >= capacity)
- `grow(newCapacity)` (panics if newCapacity < current capacity)
- `shiftRight(index, count)` (panics if index or index+count >= capacity)
- `shiftLeft(index, count)` (panics if index or index+count >= capacity)
- `byteAt(index)` returns int (panics if index >= length * elementSize)
- `setByte(index, value)` (panics if index >= length * elementSize)
- `concat(other)` returns __ManagedMemory
- `slice(start, end)` returns __ManagedMemory (panics if end > length or start > end)
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
	var arr = IntArray.create()
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
	var arr = IntArray.create()
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
	var arr = IntArray.create()
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
	var arr = IntArray.create()
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
	var arr = IntArray.create()
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

### Bounds checking

<!-- test: bounds-get-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	var v = arr.managed.get(10)
	return v
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: index out of bounds
Stack trace:
  in main
  in mrt_start
```

<!-- test: bounds-set-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	arr.managed.set(10, 99)
	return 0
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: index out of bounds
Stack trace:
  in main
  in mrt_start
```

<!-- test: bounds-setlength-exceeds-capacity -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.managed.setLength(100)
	return 0
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: setLength exceeds capacity
Stack trace:
  in main
  in mrt_start
```

<!-- test: bounds-byte-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	var b = arr.managed.byteAt(100)
	return b
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: byte index out of bounds
Stack trace:
  in main
  in mrt_start
```

<!-- test: bounds-slice-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let sliced = arr.managed.slice(0, 10)
	return sliced.length()
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: slice out of bounds
Stack trace:
  in main
  in mrt_start
```

<!-- test: bounds-valid-operations -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.managed.grow(8)
	arr.managed.setLength(4)
	arr.managed.set(0, 10)
	arr.managed.set(1, 20)
	arr.managed.set(2, 30)
	arr.managed.set(3, 40)
	var sum = arr.managed.get(0) + arr.managed.get(1) + arr.managed.get(2) + arr.managed.get(3)
	return sum
end 'main'
```
```exitcode
100
```

<!-- test: bounds-negative-index -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	var v = arr.managed.get(-1)
	return v
end 'main'
```
```exitcode
1
```
```stderr
__ManagedMemory: index out of bounds
Stack trace:
  in main
  in mrt_start
```
