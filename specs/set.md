---
feature: set
status: stable
keywords: set, collection, hash, unique, contains, insert, remove
category: collections
---
# Set

## Documentation

A `Set` is a collection of unique elements. It provides fast membership testing, insertion, and removal using hash-based lookup.

## Creating a Set

Use the `Set from` syntax with an array literal:

```maxon
var s = Set from [1, 2, 3]           // Set<int> with elements 1, 2, 3
var names = Set from ["alice", "bob"] // Set<string>
```

The element type is automatically inferred from the array values.

## Methods

### insert(element)

Add an element to the set. If the element already exists, this is a no-op.

```maxon
var s = Set from [1, 2]
s.insert(3)    // Set now contains {1, 2, 3}
s.insert(2)    // No change - 2 already exists
```

### contains(element) returns bool

Check if an element exists in the set. Returns `true` if found, `false` otherwise.

```maxon
var s = Set from [1, 2, 3]
s.contains(2)  // true
s.contains(5)  // false
```

### remove(element) returns bool

Remove an element from the set. Returns `true` if the element was present and removed, `false` if it wasn't in the set.

```maxon
var s = Set from [1, 2, 3]
s.remove(2)    // Returns true, set is now {1, 3}
s.remove(5)    // Returns false, element wasn't present
```

### count() returns int

Get the number of elements in the set.

```maxon
var s = Set from [1, 2, 3]
s.count()      // 3
```

## Capacity

The set's capacity (number of slots in the internal hash table) is accessible via the `capacity` field, not a method.

## Automatic Growth

The set automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all elements are rehashed.

## Tests

<!-- test: basic.creation -->
```maxon
function main() returns ExitCode
	let s = Set from [1, 2, 3]
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: basic.contains-true -->
```maxon
function main() returns ExitCode
	let s = Set from [10, 20, 30]
	if s.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: basic.contains-false -->
```maxon
function main() returns ExitCode
	let s = Set from [10, 20, 30]
	if s.contains(99) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert.new-element -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	s.insert(4)
	return s.count()
end 'main'
```
```exitcode
4
```

<!-- test: insert.duplicate -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	s.insert(2)
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: insert.then-contains -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	s.insert(5)
	if s.contains(5) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: remove.existing -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	let removed = s.remove(2)
	if removed 'check'
		return s.count()
	end 'check'
	return 1
end 'main'
```
```exitcode
2
```

<!-- test: remove.nonexistent -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	let removed = s.remove(99)
	if removed 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.then-contains -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	_ = s.remove(2)
	if s.contains(2) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow.preserves-elements -->
```maxon
function main() returns ExitCode
	let s = Set from [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]
	var allPresent = 1
	var i = 1
	while i <= 15 'check'
		if not s.contains(i) 'missing'
			allPresent = 0
		end 'missing'
		i = i + 1
	end 'check'
	return allPresent
end 'main'
```
```exitcode
1
```

<!-- test: empty.single-element -->
```maxon
function main() returns ExitCode
	let s = Set from [42]
	return s.count()
end 'main'
```
```exitcode
1
```

<!-- test: remove-reinsert -->
```maxon
function main() returns ExitCode
	var s = Set from [1, 2, 3]
	_ = s.remove(2)
	s.insert(2)
	if s.contains(2) 'check'
		return s.count()
	end 'check'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: negative-values -->
```maxon
function main() returns ExitCode
	let s = Set from [-5, -3, -1, 0, 1, 3, 5]
	if s.contains(-3) 'check'
		return s.count()
	end 'check'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: empty-set -->
Create an empty set and verify it starts empty.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	if s.count() != 0 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
