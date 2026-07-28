---
feature: set-string
keywords: [set, collection, hash, string, managed, ownership, witness, drop]
category: collections
---

# Set with String keys (managed-element drop)

## Documentation

A `Set with String` OWNS its heap-`String` keys. The hash-table core is the same one INT keys use — open
addressing, `hash()`/`equals()` dispatched through the key type's `Hashable`/`Equatable` witness tables
(dictionary-passing) — but a `String` key is a heap box the set must DROP: on teardown it frees every live
key, a duplicate insert drops the key it did not keep, a `remove` drops the stored key, and a
`Set from ["…"]` literal MOVES each key out of the source array (nulling the array slot so the array's own
drop does not double-free). The mechanism mirrors `Array with String`: the element drop function
(`__str_decref`) is stored in the set record at creation and read back at each slot-vacating op, so the
runtime stays key-type-agnostic while owning the keys correctly.

`String.hash()` is djb2 over the bytes and `String.equals` is content equality (`string-conformance`), so a
`String`-keyed set deduplicates by VALUE — two distinct `"a"` allocations are one member. The witness
dispatch rides the x64 rdata function-pointer relocation, so these tests are x64-only (as the
`string-conformance` and `set` witness cases are).

`Set with ByteArray` / struct keys (per-gid thunks), iteration, and `Map` are separate future slices and are
NOT covered here.

## Tests

<!-- test: create-insert-count -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Three distinct `String` keys inserted into a `.create()`d set; the count is 3 and every key is owned and
dropped at scope exit (the leak gate stays green).
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	s.insert("carol")
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: insert-duplicate-no-leak -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A duplicate `String` insert is a no-op on the count — the set keeps its existing copy and DROPS the new key,
so the run neither leaks (the new key never freed) nor double-frees (the new key freed AND the stored key
freed at scope exit are two distinct allocations). Count stays 3.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	s.insert("carol")
	s.insert("bob")
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: contains-true -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
`contains` compares by String content (the `Equatable` witness), and its argument is BORROWED — a fresh
`"bob"` allocation that drops at statement end while the stored `"bob"` stays owned by the set.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	if s.contains("bob") 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: contains-false -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A key never inserted is absent; the borrowed argument still drops cleanly.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	if s.contains("dave") 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove-drops-stored-key -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
`remove` DROPS the stored key (its argument stays the caller's borrow for the compare), tombstones the slot,
and decrements the count. Three inserts, one remove → count 2. The removed key is freed exactly once (by
remove), the borrowed argument once (at statement end), and the two survivors once each (at scope exit).
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	s.insert("carol")
	let removed = s.remove("alice")
	if removed 'ok'
		return s.count()
	end 'ok'
	return 1
end 'main'
```
```exitcode
2
```

<!-- test: remove-then-contains -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A removed key is no longer present — the tombstoned slot is skipped by a later probe.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	s.insert("bob")
	_ = s.remove("bob")
	if s.contains("bob") 'still'
		return 1
	end 'still'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: remove-nonexistent -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Removing an absent key returns false and drops nothing stored — only the borrowed argument.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	s.insert("alice")
	let removed = s.remove("zzz")
	if removed 'ok'
		return 1
	end 'ok'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: grow-with-managed-elements -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Twenty distinct heap-`String` keys (interpolated, so each is a real allocation) force a grow-and-rehash past
the 75% load factor of the initial 16-slot table. Relocation moves the key POINTERS into the new buffers and
drops NOTHING; every key survives, the count is exact, and each `contains` argument is a borrowed temp that
drops at statement end. All twenty present and no leak.
```maxon
typealias StrSet = Set with String

function main() returns ExitCode
	var s = StrSet.create()
	var i = 0
	while i < 20 'fill'
		s.insert("k{i}")
		i = i + 1
	end 'fill'
	if s.count() != 20 'count'
		return 1
	end 'count'
	var j = 0
	while j < 20 'check'
		if not s.contains("k{j}") 'missing'
			return 2
		end 'missing'
		j = j + 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: from-literal-distinct -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
`Set from ["…"]` builds a set by MOVING each key out of the source array literal — the array is the caller's
owned temp, borrowed by `__set_from`, and each moved-out slot is nulled so the array's own drop double-frees
nothing. Three distinct keys → count 3.
```maxon
function main() returns ExitCode
	let s = Set from ["x", "y", "z"]
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: from-literal-dedup -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The oracle dedup case: `Set from ["a", "b", "a", "c"]` — the second `"a"` is a distinct allocation that
duplicates the first by content, so it is dropped by the insert dup-path (and its source array slot nulled).
Three unique members; no double-free between the set's key drops and the array's element drop.
```maxon
function main() returns ExitCode
	let s = Set from ["a", "b", "a", "c"]
	return s.count()
end 'main'
```
```exitcode
3
```

<!-- test: from-literal-contains -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A `Set from ["…"]` value answers `contains` by content and owns its keys to scope exit.
```maxon
function main() returns ExitCode
	let s = Set from ["apple", "banana", "cherry"]
	if not s.contains("banana") 'miss'
		return 1
	end 'miss'
	if s.contains("durian") 'phantom'
		return 2
	end 'phantom'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: construct-drop-loop-no-leak -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The standing leak/double-free probe: a `String`-keyed set built with two heap keys, checked, and DROPPED
every iteration of a 100-iteration loop. Each iteration allocates two keys, the set owns them, and the
scope-exit drop frees both through the record's stored element destructor. `acc` reaches 100 and the leak
gate stays green — if the decref walk missed a key the run would leak; if it dropped one twice the freed
payload would fault.
```maxon
typealias StrSet = Set with String
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		var s = StrSet.create()
		s.insert("a{i}")
		s.insert("b{i}")
		if s.count() == 2 'ok'
			acc = acc + 1
		end 'ok'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: duplicate-heap-key-loop-no-leak -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The dup-drop probe under real allocations: a key inserted TWICE per iteration for 100 iterations. Each
`insert` promotes the literal to a fresh owned copy; the first is stored, the second duplicates it and is
dropped by the insert dup-path. Two allocations and two frees per iteration — a leak (the dup never freed) or
a double-free (the dup freed by insert AND again at scope exit) both fail the gate. Count is 1; `acc` reaches
100.
```maxon
typealias StrSet = Set with String
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		var s = StrSet.create()
		s.insert("dup")
		s.insert("dup")
		if s.count() == 1 'ok'
			acc = acc + 1
		end 'ok'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: remove-loop-no-leak -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The remove-drop probe under real allocations: a heap key inserted then removed every iteration for 100
iterations, exercising the remove drop site. Each iteration allocates the stored key (freed by remove) and
the borrowed remove argument (freed at statement end); the set is then dropped empty. Neither a leak nor a
double-free; `acc` reaches 100.
```maxon
typealias StrSet = Set with String
typealias Counter = int(0 to 1000)

function main() returns ExitCode
	var i = 0 as Counter
	var acc = 0 as Counter
	while i < 100 'loop'
		var s = StrSet.create()
		s.insert("gone{i}")
		let removed = s.remove("gone{i}")
		if removed 'ok'
			if s.count() == 0 'empty'
				acc = acc + 1
			end 'empty'
		end 'ok'
		i = i + 1
	end 'loop'
	if acc == 100 'all'
		return 0
	end 'all'
	return 1
end 'main'
```
```exitcode
0
```
