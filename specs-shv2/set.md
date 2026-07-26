---
feature: set
status: stable
keywords: [set, collection, hash, unique, contains, insert, remove]
category: collections
---

# Set

## Documentation

A `Set` is a collection of unique elements backed by an open-addressing hash table. It provides the
hash-table CORE for INT scalar keys: `insert`, `contains`, `remove`, `count`, and automatic growth
(double + rehash) at a 75% load factor. The key's `hash()` / `equals()` are dispatched through the key
type's `Hashable` / `Equatable` witness tables (dictionary-passing), so the runtime is key-type-agnostic.

A set is constructed either with `Set from [1, 2, 3]` — an array literal whose element type (int) is
inferred — or with `IntSet.create()` for an empty typed set. `Set from` with managed/String keys and
iteration are later slices. The witness tables are x64-only (funcAbs64 relocations), so these tests are
gated to the x64 targets.

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

<!-- test: basic.creation -->
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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

<!-- test: grow.insert-loop -->
<!-- targets: x64-windows, x64-linux -->
Growing a `.create()`d set past the 75% load factor of the initial 16-slot table by repeated `insert`
forces a grow-and-rehash; every element survives, the count is exact, and a never-inserted key is absent.

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

<!-- test: insert.alias-typed-key -->
<!-- targets: x64-windows, x64-linux -->
A key whose static type is a ranged ALIAS is an int key. It carries the `named` tag until TypeResolution
collapses it — which is every parameter and every field read of `typealias Int = int(…)` — so a key check
spelled `== integer` rather than "is integral" rejected it as *"`Set with int` requires an int key — got a
'int' value"*, a sentence that argues against itself.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function add(s IntSet, v Int)
	s.insert(v)
end 'add'

function main() returns ExitCode
	var s = IntSet.create()
	add(s, v: 3)
	add(s, v: 4)
	add(s, v: 3)
	return s.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: insert.self-field-set -->
<!-- targets: x64-windows, x64-linux -->
A `Set` held in a `var` FIELD, inserted into and counted through the bare field name. The alias holds no
SSA value (`boundValue` is 0, and 0 is `self`'s own id), so the receiver has to be materialized before the
dispatch — taking it directly handed `__set_count` the enclosing struct's box.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

type Reg
	export var seen as IntSet

	static function create() returns Reg
		return Self{seen: IntSet.create()}
	end 'create'

	export function add(v Int)
		seen.insert(v)
	end 'add'

	export function size() returns Int
		return seen.count()
	end 'size'
end 'Reg'

function main() returns ExitCode
	var r = Reg.create()
	r.add(3)
	r.add(4)
	r.add(3)
	return r.size() as ExitCode
end 'main'
```
```exitcode
2
```
