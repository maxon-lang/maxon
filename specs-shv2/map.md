---
feature: map
status: stable
keywords: map, dictionary, hash, key-value, contains, insert, remove, get
category: collections
---
# Map

## Documentation

A `map` is a collection that stores key-value pairs. It provides fast lookup, insertion, and removal using hash-based indexing.

## Creating a Map

Use map literal syntax with key-value pairs:

```text
var scores = [1: 100, 2: 85, 3: 92]                 // map<int,int>
var lookup = [10: 1000, 20: 2000, 30: 3000]         // map<int,int>
```

The key and value types are automatically inferred from the literal values.

You can also create an empty map with a type alias:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntIntMap = Map with (Integer, Integer)
var m = IntIntMap.create()
```

## Methods

### insert(key, value) throws MapError

Add a key-value pair to the map. Throws `MapError.keyAlreadyExists` if the key is already present.

```text
var m = [1: 100, 2: 200]
try m.insert(3, value: 300) otherwise ignore    // Map now has {1: 100, 2: 200, 3: 300}
try m.insert(1, value: 150) otherwise ignore    // Throws MapError.keyAlreadyExists
```

### upsert(key, value)

Insert or update a key-value pair. If the key already exists, updates the value.

```text
var m = [1: 100, 2: 200]
m.upsert(3, value: 300)    // Map now has {1: 100, 2: 200, 3: 300}
m.upsert(1, value: 150)    // Updates key 1 to 150
```

### get(key) returns Value throws MapError

Get the value for a key. Throws `MapError.keyNotFound` if the key is not in the map.

```text
var m = [10: 5, 20: 3]
var v = try m.get(10) otherwise 0    // 5
var w = try m.get(30) otherwise 0    // 0 (key not found, fallback used)
```

### contains(key) returns bool

Check if a key exists in the map. Returns `true` if found, `false` otherwise.

```text
var m = [1: 10, 2: 20]
m.contains(1)      // true
m.contains(3)      // false
```

### remove(key) returns bool

Remove a key-value pair from the map. Returns `true` if the key was present and removed, `false` if it wasn't in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.remove(2)        // Returns true, map is now {1: 10, 3: 30}
m.remove(9)        // Returns false, key wasn't present
```

### count() returns int

Get the number of key-value pairs in the map.

```text
var m = [1: 10, 2: 20, 3: 30]
m.count()          // 3
```

### capacity() returns int

Get the current capacity (number of slots) of the internal hash table.

```text
var m = [1: 100]
m.capacity()       // 16 (initial capacity)
```

## Automatic Growth

The map automatically grows when the load factor (count/capacity) exceeds 75%. When this happens, the capacity doubles and all entries are rehashed.

## Tests

<!-- test: literal.basic -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: literal.int-keys -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
20
```

<!-- test: contains.true -->
```maxon
function main() returns ExitCode
	let m = [10: 100, 20: 200, 30: 300]
	if m.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: contains.false -->
```maxon
function main() returns ExitCode
	let m = [10: 100, 20: 200, 30: 300]
	if m.contains(40) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: get.existing -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
20
```

<!-- test: get.missing -->
```maxon
function main() returns ExitCode
	let m = [1: 10, 2: 20]
	let result = try m.get(0) otherwise 0
	return result
end 'main'
```
```exitcode
0
```

<!-- test: insert.new -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	try m.insert(3, value: 30) otherwise ignore
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: upsert.update -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: insert.then-contains -->
```maxon
function main() returns ExitCode
	var m = [10: 1]
	try m.insert(20, value: 2) otherwise ignore
	if m.contains(20) 'check'
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
	var m = [1: 10, 2: 20, 3: 30]
	let removed = m.remove(2)
	if removed 'check'
		return m.count()
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
	var m = [1: 10, 2: 20]
	let removed = m.remove(99)
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
	var m = [1: 10, 2: 20, 3: 30]
	_ = m.remove(2)
	if m.contains(2) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: empty-map.from-syntax -->
```maxon
function main() returns ExitCode
	var m = [0: 0]
	_ = m.remove(0)
	try m.insert(1, value: 100) otherwise ignore
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: single-entry -->
```maxon
function main() returns ExitCode
	let m = [42: 99]
	let result = try m.get(42) otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: negative-keys -->
```maxon
function main() returns ExitCode
	let m = [-5: 50, -3: 30, -1: 10]
	let result = try m.get(-3) otherwise 0
	return result
end 'main'
```
```exitcode
30
```

<!-- test: remove-reinsert -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20, 3: 30]
	_ = m.remove(2)
	try m.insert(2, value: 99) otherwise ignore
	let result = try m.get(2) otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: map-type-in-field -->
```maxon
typealias StrMap = Map with (String, String)

type Container
	export var data as StrMap

	static function create(data StrMap) returns Self
		return Self{data: data}
	end 'create'
end 'Container'

function main() returns ExitCode
	var m = StrMap.create()
	try m.insert("key", value: "val") otherwise ignore
	let c = Container.create(m)
	let result = try c.data.get("key") otherwise ""
	if result == "val" 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```
<!-- test: string-keys-basic -->
```maxon
function main() returns ExitCode
	let m = ["a": 1, "b": 2]
	let result = try m.get("a") otherwise 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: string-keys-get-multiple -->
```maxon
function main() returns ExitCode
	let m = ["hello": 10, "world": 20, "foo": 30]
	let a = try m.get("hello") otherwise 0
	let b = try m.get("world") otherwise 0
	return a + b
end 'main'
```
```exitcode
30
```

<!-- test: string-keys-contains -->
```maxon
function main() returns ExitCode
	let m = ["key1": 100, "key2": 200]
	if m.contains("key1") 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: string-keys-upsert-update -->
```maxon
function main() returns ExitCode
	var m = ["x": 10]
	m.upsert("x", value: 99)
	let result = try m.get("x") otherwise 0
	return result
end 'main'
```
```exitcode
99
```

<!-- test: string-keys-remove -->
```maxon
function main() returns ExitCode
	var m = ["alpha": 1, "beta": 2, "gamma": 3]
	_ = m.remove("beta")
	if m.contains("beta") 'check'
		return 1
	end 'check'
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: string-keys-early-return -->
```maxon
function main() returns ExitCode
	let m = ["test": 42]
	let v = try m.get("test") otherwise 0
	return v
end 'main'
```
```exitcode
42
```

<!-- test: multiline-map-literal -->
```maxon
function main() returns ExitCode
	let m = [
		1: 100,
		2: 200,
		3: 300
	]
	return m.count()
end 'main'
```
```exitcode
3
```

<!-- test: map-literal-with-enum-values -->
```maxon
enum TokenKind
	Function
	Var
end 'TokenKind'

function main() returns ExitCode
	let keywords = ["function": TokenKind.Function, "var": TokenKind.Var]
	let kind = try keywords.get("function") otherwise TokenKind.Var
	match kind 'match'
		Function then return 1
		Var then return 2
	end 'match'
end 'main'
```
```exitcode
1
```

<!-- test: insert.duplicate-throws -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	try m.insert(1, value: 99) otherwise 'err'
		return 42
	end 'err'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: insert.duplicate-error-binding -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	try m.insert(1, value: 99) otherwise (e) 'err'
		match e 'check'
			keyAlreadyExists then return 1
			keyNotFound then return 2
		end 'check'
	end 'err'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: insert.duplicate-does-not-update -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	try m.insert(1, value: 99) otherwise ignore
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
10
```

<!-- test: upsert.new-key -->
```maxon
function main() returns ExitCode
	var m = [1: 10]
	m.upsert(2, value: 20)
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: upsert.existing-key -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	let result = try m.get(1) otherwise 0
	return result
end 'main'
```
```exitcode
100
```

<!-- test: upsert.then-contains -->
```maxon
function main() returns ExitCode
	var m = [10: 1]
	m.upsert(20, value: 2)
	if m.contains(20) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: upsert.preserves-count -->
```maxon
function main() returns ExitCode
	var m = [1: 10, 2: 20]
	m.upsert(1, value: 100)
	return m.count()
end 'main'
```
```exitcode
2
```

<!-- test: function-valued.dispatch -->
A `Map<String, FunctionType>` lets the caller dispatch by string key.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
typealias HandlerMap = Map with String, UnaryOp

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	var m = HandlerMap.create()
	m.upsert("d", value: double)
	m.upsert("t", value: triple)
	let f = try m.get("d") otherwise panic("missing 'd'")
	let g = try m.get("t") otherwise panic("missing 't'")
	return f(7) + g(7)
end 'main'
```
```exitcode
35
```

<!-- test: for-in.nested -->
Nested for-in loops on the same map must not corrupt each other's iteration state.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

function innerSum(m IntMap) returns Integer
	var total = 0
	for (_, v) in m 'inner'
		total = total + v
	end 'inner'
	return total
end 'innerSum'

function main() returns ExitCode
	let m = [1: 1, 2: 2, 3: 3]
	var outerTotal = 0
	for (_, v) in m 'outer'
		outerTotal = outerTotal + v + innerSum(m)
	end 'outer'
	// Each outer iteration: v + innerSum = v + 6
	// (1+6) + (2+6) + (3+6) = 7+8+9 = 24
	if outerTotal == 24 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-user-type-can-be-a-map-key -->
⭐⭐ **THE CAPABILITY THE RETIREMENT BOUGHT (W41), and it is the reason the retirement was worth
doing rather than a like-for-like swap.** While `Map` was SYNTHESIZED its keys were a fixed roster of
four — `int`, `String`, `Character`, `Array` — and anything else was refused outright with *"a
'<Type>' key is a later slice"*. `Map` is `stdlib/Map.maxon` now, declared
`where Key is Hashable and Equatable`, so **the roster is not a list any more: it is the constraint**,
and a user type that declares both conformances is a key like any other.

Measured against the C# bootstrap on this exact program: **both compilers exit 42.**

⛔⛔ **THE UPSERTS ARE IN A LOOP, AND THAT IS THE WHOLE POINT OF THE SHAPE (W41-trivial).** This case
was written with two STRAIGHT-LINE `upsert`s, and it passed for a reason that had nothing to do with
the capability it advertised: a straight-line `Point.create(…)` temporary is promoted to a binding of
`main`'s OWN frame (`giveTemporaryScopeLifetime`), so it happens to outlive every read that follows.
Put the identical inserts in a loop and the promotion scopes them to the **loop body**, which frees
each key at the end of its iteration while the map still points at it — **`total 0` where the oracle
prints `total 15`, exit 0, no diagnostic**. The map's `count()` was right the whole time; every key in
it was dangling. A capability that evaporates the moment its subject is written in a loop was not a
capability, and a case that cannot tell the difference was not testing one.

⚠ The `hash()` return type must be `HashValue` and not merely some `int` alias of the same span. The
oracle refuses `returns Val` with `E3016: Partial interface implementation … expected hash() returns
HashValue`; shv2 accepts it, because its signature match erases a ranged alias to its underlying
primitive where the bootstrap compares the alias NAME. That divergence is PRE-EXISTING and is not
this case's subject — it is recorded here because this is the program that surfaces it, and a reader
who writes `returns Val` will otherwise get a green shv2 and a red oracle with no idea why.
```maxon
typealias Val = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	export var x as Val
	export var y as Val

	export static function create(x Val, y Val) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

typealias PointMap = Map with (Point, Val)

function main() returns ExitCode
	var m = PointMap.create()
	for i in 1 upto 4 'fill'
		m.upsert(Point.create(i, y: i * 2), value: i * 7)
	end 'fill'
	return (try m.get(Point.create(1, y: 2)) otherwise 0) + (try m.get(Point.create(2, y: 4)) otherwise 0) + (try m.get(Point.create(3, y: 6)) otherwise 0)
end 'main'
```
```exitcode
42
```

### A TRIVIAL key column is co-owned, and survives every rehash its load factor triggers

⭐⭐ **THE TRIVIAL-KEY TWIN OF THE TWO MANAGED-COLUMN CASES BELOW, AND IT WAS THE ONE THAT WAS
MISSING (W41-trivial).** A `String` key and a `String`-owning-struct key both reach the map by being
**CONSUMED** — `typeArgIsOwned` says they own heap, so the call site MOVES them in and the column's
own element walk frees them. An all-scalar struct key answers that question `false` and was therefore
**BORROWED**, with a scope-lifetime extension as its entire protection; the container outlives the
scope in every loop, so the column held dangling keys and every `get` missed.

The cure is that a trivial aggregate key is **CO-OWNED**, exactly as a trivial `Box with Point`
constructor field already was: the call site takes a real `__mm_retain`, and the column's element walk
releases it. Both ends read `typeIsManaged`, so the descriptor's `retainFunc@64` and its
`destroyFunc@40` are non-zero together — they used to read two DIFFERENT questions, which is the same
defect stated at the descriptor.

The three sizes are not decoration. **5** is below the first `grow()`, **50** crosses it three times
(16 → 32 → 64 → 128) and **500** eight; a rehash re-inserts every key through the shared body's
BORROWED path, so a fix that landed only on the concrete call site would be green at 5 and red at 50.

<!-- test: trivial-key-column-survives-rehash -->
```maxon
typealias Val = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	export var x as Val
	export var y as Val

	export static function create(x Val, y Val) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

typealias PointMap = Map with (Point, Val)

function build(n Val) returns Val
	var m = PointMap.create()
	for i in 0 upto n 'fill'
		m.upsert(Point.create(i, y: i * 3), value: i)
	end 'fill'
	var seen = 0 as Val
	for i in 0 upto n 'read'
		seen = seen + (try m.get(Point.create(i, y: i * 3)) otherwise -1)
	end 'read'
	if seen != (n * (n - 1)) / 2 'sum'
		return -1
	end 'sum'
	return m.count()
end 'build'

function main() returns ExitCode
	print("5 {build(5)} 50 {build(50)} 500 {build(500)}")
	return 0
end 'main'
```
```stdout
5 5 50 50 500 500
```
```exitcode
0
```

### A trivial KEY beside a managed VALUE — the two columns take different protocols in one map

The pair that proves the descriptor is read **per type parameter** and not once per instance: the key
column co-owns a trivial aggregate by `__mm_retain`, the value column consumes a `String` outright, and
the two blocks sit at `layoutBlockOffsetFor(0)` and `(1)` of one `__layout_Map_Point_String`. Stamping
one column's protocol into the other's block is the wild free `managedOpaqueArrayElementOf` already
carries the measurement for (W43b), so a map whose two arguments DISAGREE about their protocol is the
program that would find it again. The value is read back and compared, so a rehash that merely
survived without faulting would still fail here.

<!-- test: trivial-key-with-managed-value-column -->
```maxon
typealias Val = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	export var x as Val
	export var y as Val

	export static function create(x Val, y Val) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

typealias PointStrMap = Map with (Point, String)

function main() returns ExitCode
	var m = PointStrMap.create()
	for i in 0 upto 40 'fill'
		m.upsert(Point.create(i, y: i * 3), value: "value number {i}, long enough to escape any small-string envelope")
	end 'fill'
	var seen = 0
	for i in 0 upto 40 'read'
		if (try m.get(Point.create(i, y: i * 3)) otherwise "").equals("value number {i}, long enough to escape any small-string envelope") 'hit'
			seen = seen + 1
		end 'hit'
	end 'read'
	print("hits {seen} count {m.count()}")
	return 0
end 'main'
```
```stdout
hits 40 count 40
```
```exitcode
0
```

### A managed KEY column survives the rehash its load factor triggers

⛔ **The rehash double-freed every entry, and the suite was 4769/0 over it because no case had ever
built a managed-column map past its load factor.** `Map.grow()` reads a BORROWED key out of the old
column and hands it to `insertAtSlot`, whose parameter is enrolled OWNED and moved into the new one — so
the new array's element walk and the old array's each destroyed the same record. `Map with (String, …)`
printed the right answer for 12 entries and **segfaulted at 13**, which is exactly `trunc(16 * 3/4) + 1`:
the first insert that calls `grow()`. The oracle prints `count 200` on the identical program.

The guard is the ENTRY COUNT and nothing else, so this case crosses the threshold three times over
(16 → 32 → 64 → 128 → 256): a case that stopped at 12 would be green on the defect.

<!-- test: managed-key-column-survives-rehash -->
```maxon
typealias Count = int(i64.min to i64.max)
typealias StrMap = Map with (String, Count)

function build(n Count) returns Count
	var m = StrMap.create()
	for i in 0 upto n 'fill'
		m.upsert("key number {i}, long enough to escape any small-string envelope", value: i)
	end 'fill'
	var seen = 0 as Count
	for i in 0 upto n 'read'
		seen = seen + (try m.get("key number {i}, long enough to escape any small-string envelope") otherwise 0)
	end 'read'
	if seen != (n * (n - 1)) / 2 'sum'
		return -1
	end 'sum'
	return m.count()
end 'build'

function main() returns ExitCode
	print("count {build(200)}")
	return 0
end 'main'
```
```stdout
count 200
```
```exitcode
0
```

### A managed AGGREGATE column survives the rehash too — the retain arm, not the clone arm

The same store, one ownership protocol along: a `String` column takes its reference by COPYING
(`__str_clone`, because an immortal `.rdata` record admits no incref) and an aggregate takes it with a
real `__mm_retain`. Both words are read out of the same layout descriptor, so a fix landing on only one
of them would leave this red — and it was red identically (`0xC0000005` at the first `grow()`) where the
oracle prints `total 21190`. The value is read back and summed, so a rehash that merely survived without
faulting would still fail here.

<!-- test: managed-aggregate-column-survives-rehash -->
```maxon
typealias Count = int(i64.min to i64.max)

type Tagged
	var label as String
	var n as Count

	export static function create(label String, n Count) returns Self
		return Self{label: label, n: n}
	end 'create'

	export function score() returns Count
		return n + (label.byteLength() as Count)
	end 'score'
end 'Tagged'

typealias TaggedMap = Map with (Count, Tagged)

function build(n Count) returns Count
	var m = TaggedMap.create()
	for i in 0 upto n 'fill'
		m.upsert(i, value: Tagged.create("tag {i}", n: i))
	end 'fill'
	var total = 0 as Count
	for i in 0 upto n 'read'
		let t = try m.get(i) otherwise panic("Map lost an entry across its rehash")
		total = total + t.score()
	end 'read'
	return total
end 'build'

function main() returns ExitCode
	print("total {build(200)}")
	return 0
end 'main'
```
```stdout
total 21190
```
```exitcode
0
```

## The `[k: v]` LITERAL is the same map — the door the retirement missed (W41-lit)

⭐⭐ **EVERY `Map` DOOR WAS GATED BY THE RETIREMENT SWITCH EXCEPT THE LITERAL.** `Map` is
`stdlib/Map.maxon` now, and at the time this was written `ProgramSignatures.isMapBaseName` answering
false for a declared `Map` was what retired the synthesized record at every door that asked it. **W105
then deleted the synthesized record outright, and the switch with it** — the retirement is unconditional
today and there is no predicate left to ask, so read the paragraph below as the history of how the
literal got here rather than as a control that still exists. `Parser.parseMapLiteralBody` asked
none of them for its COLUMN RULES: it called `requireMapColumnTypes` — the *builtin's* rule —
directly, and it moved each column value in under the *builtin's* ownership protocol. So a `[k: v]`
literal and the `create()` + `upsert` spelling of the identical map were two different containers,
which is the one thing that function's own header has always promised they are not.

The four cases below are the two halves of that, each with its regression pin. Every one is measured
against the C# bootstrap on the exact program.

### A user `Hashable` key is a literal's key too

⭐ **THE GATE HALF.** `map.md`'s `a-user-type-can-be-a-map-key` pins a user `Point` reaching a map
through `PointMap.create()` + `upsert`; the byte-identical key written in a LITERAL was
**`error E2015: … a key must be one of int, String, Character, Array — a 'Point' key is a later
slice`** — the retired builtin's own roster sentence, quoted by the one door that never learned the
roster was gone. Both compilers exit **42** on this program.

<!-- test: literal.user-type-key -->
```maxon
typealias Val = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	export var x as Val
	export var y as Val

	export static function create(x Val, y Val) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

function main() returns ExitCode
	let m = [Point.create(1, y: 2): 7, Point.create(3, y: 4): 11, Point.create(5, y: 6): 24]
	var total = 0 as Val
	for i in 1 upto 4 'read'
		total = total + (try m.get(Point.create(i * 2 - 1, y: i * 2)) otherwise panic("map literal lost a user-type key"))
	end 'read'
	print("total {total} count {m.count()}")
	return total as ExitCode
end 'main'
```
```stdout
total 42 count 3
```
```exitcode
42
```

### A literal's key column outlives the frame that built it

⚠ **THE KEY HALF OF THE OWNERSHIP QUESTION, WHICH THE STRAIGHT-LINE CASE ABOVE CANNOT ASK.** An
all-scalar `Point` is `typeArgIsOwned` FALSE, so `Map.upsert` BORROWS it and the call site takes the
second reference (`coOwnConcreteAggregateFeed`) — the arrangement `trivial-key-column-survives-rehash`
pins for the written spelling. Reading it back inside the builder's own frame passes whether or not
that reference was ever taken, because the builder's temporaries are still alive there. Returning the
map is what makes the reference load-bearing: the keys' own frame is gone by the time `main` reads
them.

Both compilers print `total 42 count 3` and exit **42**.

<!-- test: literal.user-type-key-escapes-its-builder -->
```maxon
typealias Val = int(i64.min to i64.max)

type Point implements Hashable, Equatable
	export var x as Val
	export var y as Val

	export static function create(x Val, y Val) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

typealias PointMap = Map with (Point, Val)

function build() returns PointMap
	return [Point.create(1, y: 2): 7, Point.create(3, y: 4): 11, Point.create(5, y: 6): 24]
end 'build'

function main() returns ExitCode
	let m = build()
	var total = 0 as Val
	for i in 1 upto 4 'read'
		total = total + (try m.get(Point.create(i * 2 - 1, y: i * 2)) otherwise panic("map literal lost a user-type key"))
	end 'read'
	print("total {total} count {m.count()}")
	return total as ExitCode
end 'main'
```
```stdout
total 42 count 3
```
```exitcode
42
```

### An AGGREGATE value column in a literal leaks nothing

⛔⛔ **THE OWNERSHIP HALF, AND IT WAS AN OUTRIGHT LEAK: exit 101 where the oracle exits 42.** The
literal desugars to `Map.create()` plus one `Map.upsert(map, key, value:)` per pair — an ORDINARY
call, whose arguments the ordinary machinery transfers or co-owns (`applyCallerConsume`). The literal
ALSO ran the synthesized record's move-in (`moveColumnValueIntoTable`), which drains the value from
the statement's pending drops because `__map_upsert` is a runtime call with no signature to read. Two
protocols on one value: the map took its reference and the statement no longer released its own.

⚠ **ONLY AN AGGREGATE COLUMN SHOWED IT, WHICH IS WHY THE SUITE WAS GREEN OVER IT.** The two
protocols AGREE for every column the suite had a literal for: an `int` column owns no record and
moves nothing, and a `String` column is `typeArgIsOwned` TRUE, so the ordinary machinery MOVES it —
exactly what the literal had already done. An all-scalar struct is the one class the call site
BORROWS, and there the drained temporary is a reference nobody releases.

<!-- test: literal.aggregate-value-column -->
```maxon
typealias Val = int(i64.min to i64.max)

type Pair
	export var a as Val
	export var b as Val

	export static function create(a Val, b Val) returns Self
		return Self{a: a, b: b}
	end 'create'

	export function sum() returns Val
		return a + b
	end 'sum'
end 'Pair'

function main() returns ExitCode
	let m = [1: Pair.create(3, b: 4), 2: Pair.create(10, b: 25)]
	let first = try m.get(1) otherwise panic("map literal lost an aggregate value")
	let second = try m.get(2) otherwise panic("map literal lost an aggregate value")
	let total = first.sum() + second.sum()
	print("total {total} count {m.count()}")
	return total as ExitCode
end 'main'
```
```stdout
total 42 count 2
```
```exitcode
42
```

### A MANAGED column pair through a literal — the regression pin the two fixes must not move

⚠ **THE COLUMNS THAT WERE ALREADY RIGHT, PINNED SO THAT MAKING THE AGGREGATE ONE RIGHT CANNOT BREAK
THEM.** A `String` key and a `String` value are `typeArgIsOwned` TRUE and therefore CONSUMED at the
call, which is the arm where the literal's own move-in and the ordinary call machinery happened to
agree — so this program was green before either fix and its whole job is to still be green after.
Read back and printed rather than merely counted: a lost reference here is a use-after-free, not a
missing entry, and `count()` cannot see one.

⚠ **ITS GOLDEN IS THE ONE FRAGMENT IN THE WHOLE SUITE THAT MOVED, and the movement is ORDER and not
content.** Measured against the merge base built the same way: 1014 fragments drift before, 1015
after, and the set difference is exactly this case. The emitted call sequence is identical —
`Map.create`, three `Map.upsert`, the same six `__mm_alloc` + `__str_copy` literal promotions, the
same `__destruct_Map_String_String` — because the promotion of a borrowed `.rdata` String is the same
act wherever it is emitted. What moved is WHEN: it used to happen inside the literal parse and now
happens inside `emitCall`, which runs after `Map.create` rather than before it. More values are
therefore live across that call and the allocator spills four more slots (`prologue 152` → `184`).
That is the price of the literal and the written `upsert` sharing ONE ownership protocol, and it is
paid only on this path.

<!-- test: literal.managed-column-pair -->
```maxon
function main() returns ExitCode
	let m = ["alpha": "one", "beta": "two", "gamma": "three"]
	let a = try m.get("alpha") otherwise panic("map literal lost a String key")
	let g = try m.get("gamma") otherwise panic("map literal lost a String key")
	print("{a}/{g} count {m.count()}")
	return m.count() as ExitCode
end 'main'
```
```stdout
one/three count 3
```
```exitcode
3
```

### A literal key that conforms to NEITHER is still refused, at the literal

⛔ **THE REFUSAL THE GATE FIX MUST NOT LOSE.** A literal is the one door a map can be born through
with no `with (K, V)` annotation to anchor an E3017 on, so dropping the builtin's roster without
putting anything in its place would admit a key nothing can hash. It is `Map`'s OWN declared
`where Key is Hashable and Equatable` that refuses it now — the same sentence, code and shape a
written `typealias OpaqueMap = Map with (Opaque, Val)` gets (`array-conditional-conformance-withheld`'s
`error.a-key-type-nothing-conforms-for-still-reads-as-a-later-slice`) — anchored on the literal's
first key, which is the only position the program offers.

<!-- test: error.literal-key-conforming-to-neither -->
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	export static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

function main() returns ExitCode
	let m = [Opaque.create(1): 5]
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3017: <fragment>:13:11: Type 'Opaque' does not satisfy constraint 'Hashable' required by type parameter 'Key' of 'Map'
error E3017: <fragment>:13:11: Type 'Opaque' does not satisfy constraint 'Equatable' required by type parameter 'Key' of 'Map'
```

### The corpus declaration is what serves a `Map` — pinned by members the synthesized record never had

⭐⭐ **THE SYNTHESIZED `Map` IS RETIRED, AND THESE ARE THE CASES THAT CAN ONLY PASS IF IT IS.** Every
case above this section passes under EITHER regime — `count`, `contains`, `get`, `upsert`, `insert`,
`remove` and `map` are the seven names `Parser.mapSurfaceMemberNames` served, so a green suite over
them says nothing about which `Map` answered. `stdlib/Map.maxon` declares two members that roster
never had — `getCapacity()` and `createIterator()` — and a program calling one is refused outright
(`E2015 … that list IS the surface`) the moment the builtin is the thing serving the type.

⚠ **MEASURED RED — by the A/B available at the time, removing `stdlib/Map.maxon`'s
`listWhitelistedModule` line from the loader's then-whitelist and rebuilding** (that method is gone with
the filter; the reading stands): all
three cases in this section fail to COMPILE against the synthesized record and pass against the
declaration —

```
error E2015: Unsupported: `Map` member 'getCapacity' — shv2 provides count/contains/get/upsert/insert/remove/map
error E2015: Unsupported: `Map` member 'createIterator' — shv2 provides count/contains/get/upsert/insert/remove/map
error E4016: 'MapError' is the error enum the Map runtime (MapError) throws, and this compile declares no enum of that name
```

— which is the control this file owed and did not have. `ProgramSignatures.isMapBaseName` answered
FALSE once a `Map` was declared, so the whole retirement was ONE predicate — and a predicate with no case
behind it is a switch nothing would notice being flipped back, which is exactly why **W105 deleted it
along with the `__map_*` runtime its true arm selected.** The refusal above is now unconditional.

⚠⚠ **AND THE THIRD LINE IS THE ONE WORTH READING: THE SYNTHESIZED RECORD IS NOT A WORKING FALLBACK.**
It throws `MapError`, whose ordinals it can only get from a *declared* enum of that name — and the
only file that declares one is `stdlib/Map.maxon` itself. So the regime selected by that module's
absence cannot survive its absence: it is unreachable in every shipped compile, not merely unused.

<!-- test: corpus.get-capacity -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias ValMap = Map with (Val, Val)

function main() returns ExitCode
	var m = ValMap.create()
	let empty = m.getCapacity()
	m.upsert(1, value: 1)
	let one = m.getCapacity()

	for i in 0 upto 40 'fill'
		m.upsert(i, value: i * 10)
	end 'fill'

	print("capacity {empty} {one} {m.getCapacity()} count {m.count()}")
	return m.count() as ExitCode
end 'main'
```
```stdout
capacity 0 16 64 count 40
```
```exitcode
40
```

The three capacities are the corpus's own schedule and not a shape any other reader could supply: a
map created empty holds no buffers at all (`capacity = 0`), the first `upsert` allocates sixteen
slots, and `ensureCapacity` doubles at a load factor of 75% — 16 ⇒ 32 at the twelfth entry, 32 ⇒ 64
at the twenty-fourth, and forty entries therefore rest at 64 rather than growing again.

<!-- test: corpus.create-iterator -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias ValMap = Map with (Val, Val)

function main() returns ExitCode
	var m = ValMap.create()
	m.upsert(1, value: 10)
	m.upsert(2, value: 20)
	m.upsert(3, value: 30)

	var it = try m.createIterator() otherwise panic("a three-entry map has an occupied first slot")
	var total = 0
	var more = true
	while more 'walk'
		let entry = it.current()
		total = total + entry.0 * entry.1

		try it.advance() otherwise 'exhausted'
			more = false
		end 'exhausted'
	end 'walk'

	print("total {total} count {m.count()}")
	return total as ExitCode
end 'main'
```
```stdout
total 140 count 3
```
```exitcode
140
```

⚠ **DRIVEN BY HAND RATHER THAN THROUGH `for … in`, which is the point.** A `for (k, v) in m` reaches
`MapIterator` too, but it reaches it through the cursor-protocol rewrite that `Range` and `views`
also take — so it would still compile if `Map` were served by something else that happened to publish
a cursor. Naming `createIterator()`, `current()` and `advance()` at the call site pins the declared
protocol itself: the `(Key, Value)` tuple `current()` returns, and `advance()`'s
`throws IterationError` as the loop's own termination.

### An empty map iterates ZERO times, however it became empty

⚠ **THE EDGE THE CURSOR FORM MAKES NON-OBVIOUS, AND THE FILE HAD NO CASE FOR IT.** `for … in` over a
cursor is a DO-WHILE — its first test is licensed by the protocol's invariant that a live cursor is
already positioned on an element — so an empty source has to be refused by the FACTORY rather than by
the loop, and `Map.createIterator()` is the throwing factory that does it
(`IterationError.exhausted`). A map that never held an entry has `capacity == 0`; one emptied by
`remove` has a full slot array of tombstones and a non-zero capacity, and `findNextOccupied` has to
walk all of it to reach the same answer. Both must run the body zero times, and the body is written to
say so loudly if either does not: it adds `1 + v`, so one spurious trip moves `trips` off zero whatever
the slot it read held.

<!-- test: corpus.empty-map-iterates-zero-trips -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias ValMap = Map with (Val, Val)

function main() returns ExitCode
	let never = ValMap.create()
	var trips = 0
	for (_, v) in never 'neverPopulated'
		trips = trips + 1 + v
	end 'neverPopulated'

	var emptied = ValMap.create()
	emptied.upsert(1, value: 10)
	emptied.upsert(2, value: 20)
	_ = emptied.remove(1)
	_ = emptied.remove(2)

	for (_, v) in emptied 'allRemoved'
		trips = trips + 1 + v
	end 'allRemoved'

	print("trips {trips} counts {never.count()} {emptied.count()}")
	return trips as ExitCode
end 'main'
```
```stdout
trips 0 counts 0 0
```
```exitcode
0
```

## A map literal at FILE SCOPE

A `[k: v]` written as a top-level `let`/`var` initializer is the same desugar a body's literal is —
`Map.create()` plus one `Map.upsert(k, value: v)` per pair — emitted by `__module_init` before `main`
instead of into the function that wrote it. The first pair fixes both columns; every later pair is held
to them.

⛔⛔ **BOTH HALVES OF EVERY PAIR ARE KEPT BY THE TABLE, AND `__module_init` MUST THEREFORE RELEASE
NEITHER.** `Map.upsert` pushes its `key` and its `value` into `Array with Key` / `Array with Value`, which
is a type-parameter FEED and not a consume bit — so the synthesis' ordinary "the callee only borrowed it,
drop it after the call" rule is a use-after-free here. MEASURED with that rule applied: the first case
below printed `0 0` (both lookups missing, the keys having been freed the instant they were stored) and
then **segfaulted**, against the reference compiler's `1 2`. A body's call site takes a real reference at
the same position and drops the temp at statement end; `__module_init` has no statement, so the two net to
the same one reference by this frame taking none and releasing none.

<!-- test: top-level-literal.managed-value-column -->
Both columns managed, and the map is read only AFTER `main` has allocated over whatever the init freed —
so a released key or value is a wrong answer here rather than a lucky one.
```maxon
var m = [b"alpha": "one", b"beta": "two"]

function main() returns ExitCode
	var churn = ByteArray.create()
	var i = 0
	while i < 64 'allocate'
		churn.push(65)
		i = i + 1
	end 'allocate'

	let a = try m.get(b"alpha") otherwise panic("the file-scope literal lost a key")
	let b = try m.get(b"beta") otherwise panic("the file-scope literal lost a key")
	print("{a}/{b} count {m.count()} churn {churn.count()}")
	return 0
end 'main'
```
```stdout
one/two count 2 churn 64
```
```exitcode
0
```

<!-- test: top-level-literal.struct-value-column -->
A value built by a static FACTORY, which is what makes the literal an arena node rather than a fold: the
record is a call `__module_init` makes, and the `String` it holds is released once, by the map's teardown.
```maxon
typealias Integer = int(i64.min to i64.max)

type Info
	export var help as String
	export var n as Integer

	export static function create(help String, n Integer) returns Info
		return Self{help: help, n: n}
	end 'create'
end 'Info'

var table = [b"if": Info.create("conditional", n: 1), b"else": Info.create("alternative", n: 2)]

function main() returns ExitCode
	let e = try table.get(b"else") otherwise panic("the file-scope literal lost a key")
	print("{e.help} {e.n} count {table.count()}")
	return 0
end 'main'
```
```stdout
alternative 2 count 2
```
```exitcode
0
```

<!-- test: error.top-level-literal-mixed-value-column -->
The first pair fixes both columns. A later pair whose half has another type is refused where it is
written — the file-scope twin of the body's `parseHashTableColumnArg` refusal, which cannot serve here
because it reads a `ValueId` and a top-level initializer mints none.
```maxon
var m = [b"a": 1, b"b": "two"]

function main() returns ExitCode
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:25: Unsupported: this `Map`'s value is 'int' — got a 'String' value. A top-level map literal's FIRST pair fixes both columns, and every later pair must match them
```

<!-- test: error.top-level-literal-enum-column -->
⛔ **AN ENUM CASE IS REFUSED AS A FILE-SCOPE COLUMN, AND THE LIMIT IS THE INTERNER.** A payload-free case
folds to a `named`-tagged scalar carrying its enum's name as BYTES — the constant evaluator is a throwaway
parser, so an id minted during the fold names nothing where it is read — and a `Map with (K, V)` needs a
type argument the whole program can name. Admitting it either drops the name (and `m.get(…)` then hands
back a bare `int` for a program whose every arm is the enum) or keeps this index's id (and
`SemanticCheck.aggregateNameFor` panics on it). The same literal inside a function is correct today, which
is what the message points at.
```maxon
enum Kind
	one
	two
end 'Kind'

var m = [b"a": Kind.one, b"b": Kind.two]

function main() returns ExitCode
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:16: Unsupported: a `Kind` case as a top-level map literal's value — a `Map`'s column type has to be nameable from the whole-program index, and an enum case folded at file scope carries its enum's name as BYTES rather than as an id this tier can put in `Map with (…)`. Build the map inside a function, where the literal's instance is interned from the file's own parse artifact
```
