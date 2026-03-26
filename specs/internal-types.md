---
feature: internal-types
status: stable
keywords: [internal, opaque, __ManagedMemory, __ManagedList, __ManagedListNode, builtin]
category: type-system
---

# Builtin Types

## Documentation

### Builtin Types

Types prefixed with `__` are compiler builtin types. They are registered by the compiler and have builtin methods that are intercepted during parsing.

The builtin types are:
- `__ManagedMemory` — heap-backed buffer storage used by `Array`, `String`, and other collections
- `__ManagedList` — doubly-linked list container
- `__ManagedListNode` — node in a `__ManagedList`
- `__ManagedListError` — error type for managed list operations

### Using __ManagedMemory

`__ManagedMemory` provides low-level heap buffer operations. Most user code should use higher-level types like `Array` or `String`, but `__ManagedMemory` can be used directly for custom collection types.

```text
typealias Int = int(i64.min to i64.max)

type MyBuffer uses Element
  var managed __ManagedMemory
end 'MyBuffer'
```

### __ManagedMemory Methods

All methods that access buffer elements perform runtime bounds checking and panic on out-of-bounds access.

Instance methods:
- `length()` returns int — element count
- `capacity()` returns int — allocated capacity
- `elementSize()` returns int — bytes per element
- `setLength(n)` — set element count (panics if n > capacity)
- `get(index)` returns Element — read element at index (panics if index >= length)
- `set(index, value)` — write element at index (panics if index >= capacity)
- `grow(newCapacity)` — grow buffer via realloc (panics if newCapacity < current capacity)
- `shiftRight(index, count)` — shift elements right (panics if index or index+count >= capacity)
- `shiftLeft(index, count)` — shift elements left (panics if index or index+count >= capacity)
- `byteAt(index)` returns int — read single byte (panics if index >= length * elementSize)
- `setByte(index, value)` — write single byte (panics if index >= length * elementSize)
- `concat(other)` returns __ManagedMemory — concatenate buffers
- `slice(start, end)` returns __ManagedMemory — create slice [start, end) (panics if end > length or start > end)
- `toCString()` returns int — raw buffer pointer
- `makeCharFromBytes(pos, len)` returns int — extract character

Static methods:
- `__ManagedMemory.create(capacity, elementSize)` returns __ManagedMemory
- `__ManagedMemory.fromCString(ptr)` returns __ManagedMemory

## Tests

<!-- test: array-public-api -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray{}
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-literal-works -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	return arr.count()
end 'main'
```
```exitcode
5
```
