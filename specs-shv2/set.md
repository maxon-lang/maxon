---
feature: set
status: stable
keywords: [set, collection, hash, unique, contains, insert, remove]
category: collections
---

# Set

## Documentation

A `Set` is a collection of unique elements backed by an open-addressing hash table. This slice provides
the hash-table CORE for INT scalar keys, constructed with `IntSet.create()`: `insert`, `contains`,
`remove`, `count`, and automatic growth (double + rehash) at a 75% load factor. The key's `hash()` /
`equals()` are dispatched through the key type's `Hashable` / `Equatable` witness tables
(dictionary-passing), so the runtime is key-type-agnostic.

`Set from [...]` construction syntax, managed/String keys, and iteration are later slices. The witness
tables are x64-only (funcAbs64 relocations), so these tests are gated to the x64 targets.

## Tests

<!-- test: empty-set -->
<!-- targets: x64-windows, x64-linux -->
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

<!-- test: insert.new-element -->
<!-- targets: x64-windows, x64-linux -->
Inserting three distinct elements grows the count to three.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(10)
	s.insert(20)
	s.insert(30)
	if s.count() != 3 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert.duplicate -->
<!-- targets: x64-windows, x64-linux -->
Inserting the same element repeatedly is a no-op — the count stays one.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(10)
	s.insert(10)
	s.insert(10)
	if s.count() != 1 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: insert.then-contains -->
<!-- targets: x64-windows, x64-linux -->
An inserted element is reported present by `contains`.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(42)
	if not s.contains(42) 'present'
		return 1
	end 'present'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: contains.absent -->
<!-- targets: x64-windows, x64-linux -->
`contains` reports an element that was never inserted as absent.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(42)
	if s.contains(99) 'absent'
		return 1
	end 'absent'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.existing -->
<!-- targets: x64-windows, x64-linux -->
Removing a present element returns true and decrements the count.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(1)
	s.insert(2)
	s.insert(3)
	if not s.remove(2) 'removed'
		return 1
	end 'removed'
	if s.count() != 2 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.nonexistent -->
<!-- targets: x64-windows, x64-linux -->
Removing an absent element returns false and leaves the count unchanged.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(1)
	if s.remove(99) 'notPresent'
		return 1
	end 'notPresent'
	if s.count() != 1 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove.then-contains -->
<!-- targets: x64-windows, x64-linux -->
A removed element is reported absent by `contains` afterward.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(5)
	let removed = s.remove(5)
	if not removed 'r'
		return 1
	end 'r'
	if s.contains(5) 'stillThere'
		return 2
	end 'stillThere'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove-reinsert -->
<!-- targets: x64-windows, x64-linux -->
Removing then re-inserting the same key reuses the tombstoned slot — the count is
restored and both elements remain present.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(7)
	s.insert(8)
	if not s.remove(7) 'r'
		return 1
	end 'r'
	if s.count() != 1 'c1'
		return 2
	end 'c1'
	s.insert(7)
	if s.count() != 2 'c2'
		return 3
	end 'c2'
	if not s.contains(7) 'has7'
		return 4
	end 'has7'
	if not s.contains(8) 'has8'
		return 5
	end 'has8'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow.preserves-elements -->
<!-- targets: x64-windows, x64-linux -->
Inserting twenty elements crosses the 75% load factor of the initial 16-slot table and
forces a grow-and-rehash; every element survives and the count is exact.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	var i = 0
	while i < 20 'fill'
		s.insert(i)
		i = i + 1
	end 'fill'
	if s.count() != 20 'count'
		return 1
	end 'count'
	var j = 0
	while j < 20 'check'
		if not s.contains(j) 'missing'
			return 2
		end 'missing'
		j = j + 1
	end 'check'
	if s.contains(20) 'phantom'
		return 3
	end 'phantom'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: negative-values -->
<!-- targets: x64-windows, x64-linux -->
Negative keys hash by the low-32 mask of their two's-complement representation and behave
consistently across insert / contains / remove.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(-5)
	s.insert(-100)
	s.insert(-5)
	if s.count() != 2 'count'
		return 1
	end 'count'
	if not s.contains(-5) 'hasNeg5'
		return 2
	end 'hasNeg5'
	if not s.contains(-100) 'hasNeg100'
		return 3
	end 'hasNeg100'
	if s.contains(-7) 'noNeg7'
		return 4
	end 'noNeg7'
	if not s.remove(-5) 'rmNeg5'
		return 5
	end 'rmNeg5'
	if s.count() != 1 'count2'
		return 6
	end 'count2'
	if s.contains(-5) 'goneNeg5'
		return 7
	end 'goneNeg5'
	return 0
end 'main'
```
```exitcode
0
```
