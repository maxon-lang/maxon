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

A set is constructed with `Set from [1, 2, 3]` — an array literal whose element type is inferred from its
first member — with `Set from values` over any `Array`, or with `IntSet.create()` for an empty typed set. The witness tables ride the funcAbs64
relocation, which every target fills: a `.text` VA on the fixed-base x64 and arm64 writers, a funcref-table index on wasm, and a dyld
chained-fixup REBASE on the always-PIE arm64-macOS. So these tests carry no target marker.

## Tests

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
	var allPresent = 1 as FullRangeInt
	var i = 1 as FullRangeInt
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

<!-- test: grow.insert-loop -->
Growing a `.create()`d set past the 75% load factor of the initial 16-slot table by repeated `insert`
forces a grow-and-rehash; every element survives, the count is exact, and a never-inserted key is absent.

```maxon
typealias Int = int(i64.min to i64.max)
typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	var i = 0 as Int
	while i < 20 'fill'
		s.insert(i)
		i = i + 1
	end 'fill'
	if s.count() != 20 'count'
		return 1
	end 'count'
	var j = 0 as Int
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

	function add(v Int)
		seen.insert(v)
	end 'add'

	function size() returns Int
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

## A set under insert/remove churn stays flat

⛔⛔ **A REMOVED MEMBER LEAVES A TOMBSTONE, AND A PROBE STOPS ONLY AT AN EMPTY SLOT** — `Map`'s defect in
the same table, and the same cure: the load factor counts tombstones as occupancy, so a table that is
inserted into and removed from in turn rehashes to clear them instead of spending its empty slots until
every miss probes the whole capacity. `specs/map.md`'s churn case states it in full.

<!-- test: churn.insert-and-remove-stays-flat -->
```maxon
typealias Tally = int(0 to u64.max)
typealias TallySet = Set with Tally

let Live = 2000 as Tally
let Rounds = 2000 as Tally

function main() returns ExitCode
	var s = TallySet.create()

	for i in 0 upto Live 'seed'
		s.insert(i)
	end 'seed'

	for round in 1 to Rounds 'eachRound'
		for i in 0 upto Live 'eachKey'
			s.insert(round * Live + i)
			_ = s.remove((round - 1) * Live + i)
		end 'eachKey'
	end 'eachRound'

	return 7 if s.count() == (Live as MemberCount) and s.contains(Rounds * Live + 1) else 1
end 'main'
```
```exitcode
7
```

<!-- test: error.a-set-from-head-another-file-keeps-private-is-refused -->
A `Set` instance alias at the head of `from […]` is a type name, and another file's file-private one is refused.
```maxon
// --- file: probe.maxon
typealias Counts = Set with ExitCode

export function probeCounts() returns ExitCode
	let s = Counts from [1, 2]
	print("{s.count()}")
	return 0
end 'probeCounts'

// --- file: main.maxon
function main() returns ExitCode
	let s = Counts from [3]
	print("{s.count()}")
	return probeCounts()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:13:10: typealias 'Counts' is not exported
```

<!-- test: from-an-array-variable -->
An alias head takes any array of its element type as the source of `from`, not only a literal.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias CodeSet = Set with Code

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(3)
	codes.push(7)
	codes.push(11)
	let s = CodeSet from codes
	print("{s.count()} {s.contains(7)} {s.contains(8)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 true false
```

<!-- test: from-a-call-result-infers-the-element -->
A bare `Set` head takes its element type from the array a call returns.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code

function makeCodes() returns Codes
	var codes = Codes.create()
	codes.push(40)
	codes.push(2)
	return codes
end 'makeCodes'

function main() returns ExitCode
	let s = Set from makeCodes()
	print("{s.count()} {s.contains(40)} {s.contains(2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 true true
```

<!-- test: from-an-array-collapses-duplicates -->
A source array holding an element twice yields a set holding it once.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias CodeSet = Set with Code

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(5)
	codes.push(9)
	codes.push(5)
	codes.push(9)
	codes.push(5)
	let s = CodeSet from codes
	print("{s.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: from-an-empty-array-is-empty -->
An empty array VALUE is a valid source, though an empty literal `[]` is not.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias CodeSet = Set with Code

function main() returns ExitCode
	let codes = Codes.create()
	let s = CodeSet from codes
	print("{s.count()} {s.contains(0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 false
```

<!-- test: error.from-a-source-that-is-not-an-array-is-a-type-mismatch -->
The source of `from` must be an array; any other value is a type mismatch naming the array the head expects.
```maxon
typealias Code = int(0 to 100)
typealias CodeSet = Set with Code

function main() returns ExitCode
	let s = CodeSet from 5
	return s.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:23: argument type mismatch for 'source': expected 'Array with Code', got 'int'
```

<!-- test: error.from-an-array-of-another-element-is-a-type-mismatch -->
An alias head takes only an array of its own element type.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias Names = Set with String

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(1)
	let s = Names from codes
	return s.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:21: argument type mismatch for 'source': expected 'Array with String', got 'Codes'
```

<!-- test: from-a-function-declared-later -->
A source call to a function declared further down the file is a valid source.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code

function main() returns ExitCode
	let s = Set from laterCodes()
	print("{s.count()} {s.contains(6)}\n")
	return 0
end 'main'

function laterCodes() returns Codes
	var codes = Codes.create()
	codes.push(6)
	codes.push(6)
	codes.push(1)
	return codes
end 'laterCodes'
```
```exitcode
0
```
```stdout
2 true
```

<!-- test: from-a-struct-array-infers-the-key -->
A bare `Set` head over an array of a user `Hashable` struct builds a set of that struct.
```maxon
typealias Coord = int(0 to 1000)

type Point implements Hashable, Equatable
	let x as Coord
	let y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'

	function hash() returns HashValue
		return self.x * 1000 + self.y
	end 'hash'

	function equals(other Point) returns bool
		return self.x == other.x and self.y == other.y
	end 'equals'
end 'Point'

typealias Points = Array with Point

function main() returns ExitCode
	var points = Points.create()
	points.push(Point.create(1, y: 2))
	points.push(Point.create(3, y: 4))
	points.push(Point.create(1, y: 2))
	let s = Set from points
	print("{s.count()} {s.contains(Point.create(3, y: 4))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 true
```

<!-- test: error.from-an-array-of-an-unhashable-element -->
A bare `Set` head over an array whose element is not `Hashable` and `Equatable` is refused by `Set`'s `where` clause, at the head.
```maxon
typealias Coord = int(0 to 1000)

type Spot
	let x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'
end 'Spot'

typealias Spots = Array with Spot

function main() returns ExitCode
	var spots = Spots.create()
	spots.push(Spot.create(1))
	let s = Set from spots
	return s.count()
end 'main'
```
```maxoncstderr
error E3017: <fragment>:17:10: Type 'Spot' does not satisfy constraint 'Hashable' required by type parameter 'Element' of 'Set'
error E3017: <fragment>:17:10: Type 'Spot' does not satisfy constraint 'Equatable' required by type parameter 'Element' of 'Set'
```

<!-- test: two-files-each-build-the-same-inferred-instance -->
Two files that each build a bare `Set from` over the same element mint one instance between them.
```maxon
// --- file: a.maxon
module function countA(codes Codes) returns Count
	let s = Set from codes
	return s.count()
end 'countA'

// --- file: main.maxon
module typealias Code = int(0 to 100)
module typealias Codes = Array with Code

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(2)
	codes.push(2)
	codes.push(9)
	let s = Set from codes
	print("{s.count()} {countA(codes)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2
```

<!-- test: from-a-character-array-under-an-alias-or-a-bare-head -->
A `Character` array is a source like any other: an alias head of `Set with Character` and a bare `Set` head both take one.
```maxon
typealias Chars = Array with Character
typealias CharSet = Set with Character

function main() returns ExitCode
	var chars = Chars.create()
	chars.push('a')
	chars.push('b')
	chars.push('a')
	let named = CharSet from chars
	let bare = Set from chars
	print("{named.count()} {named.contains('b')} {bare.count()} {bare.contains('c')}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 true 2 false
```

<!-- test: from-a-struct-array-under-an-alias -->
An alias head over a user `Hashable` struct takes an array of that struct.
```maxon
typealias Coord = int(0 to 1000)

type Cell implements Hashable, Equatable
	let x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'

	function hash() returns HashValue
		return self.x
	end 'hash'

	function equals(other Cell) returns bool
		return self.x == other.x
	end 'equals'
end 'Cell'

typealias Cells = Array with Cell
typealias CellSet = Set with Cell

function main() returns ExitCode
	var cells = Cells.create()
	cells.push(Cell.create(4))
	cells.push(Cell.create(4))
	cells.push(Cell.create(5))
	let s = CellSet from cells
	print("{s.count()} {s.contains(Cell.create(5))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 true
```

<!-- test: an-alias-head-literal-builds-the-alias-instance -->
An alias head states the set's key type for a literal source as it does for an array value, so both build a value of the alias's type.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias CodeSet = Set with Code

function total(s CodeSet) returns Count
	return s.count()
end 'total'

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(4)
	print("{total(CodeSet from [1, 2])} {total(CodeSet from codes)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: a-bare-head-literal-of-struct-members-infers-the-key -->
A bare `Set` head over a literal of user `Hashable` struct members infers the key from the first member, as it does over an array of them.
```maxon
typealias Coord = int(0 to 1000)

type Tile implements Hashable, Equatable
	let x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'

	function hash() returns HashValue
		return self.x
	end 'hash'

	function equals(other Tile) returns bool
		return self.x == other.x
	end 'equals'
end 'Tile'

function main() returns ExitCode
	let s = Set from [Tile.create(1), Tile.create(1), Tile.create(2)]
	print("{s.count()} {s.contains(Tile.create(2))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 true
```

<!-- test: error.an-alias-head-literal-member-of-another-type -->
Every member of an alias-headed literal is held to the alias's key type.
```maxon
typealias Code = int(0 to 100)
typealias CodeSet = Set with Code

function main() returns ExitCode
	let s = CodeSet from [1, "two"]
	return s.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:10: argument type mismatch for 'element': expected 'Code', got 'String'
```
