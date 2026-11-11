---
feature: regions-runtime
status: experimental
keywords: [strand guard, freeze, shared counts, wasm, hold, may yield, modify, sleep]
category: runtime
---

# Run-time behaviour of regions: the strand guard, `freeze` and holds

## Documentation

While a `modify` block is open the runtime does not switch to a sibling coroutine of the same green thread:
a wait that parks the thread, such as `sleep` or a read, blocks the strand for the block instead of letting
another coroutine run. `freeze` walks the region it freezes and marks every box in it shared, so the frozen
graph can be read from any thread with atomic counts. On `wasm32-wasi`, which has no green threads, the
guard is not emitted and `freeze` works the same. A value read from a field and passed as an operand is held
across every later operand that may yield, so a coroutine that replaces the field meanwhile cannot free it.

## Tests

<!-- test: strand-guard-holds-sibling-during-park -->
`editor` parks in `sleep(150)` inside `modify`, and `sibling` sleeps only 10 ms; without the guard the scheduler would run `sibling` during the park and print it between `before` and `inside`. The pinned order proves no sibling ran while the block was open.

```maxon
typealias LineArray = Array with String

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	print("before\n")
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		batch.push("x")
		sleep(150)
		print("inside {batch.count()}\n")
	end 'fill'

	print("after\n")
	return 2
end 'editor'

function main() returns ExitCode
	let ed = async editor()
	let sib = async sibling()
	_ = await ed
	_ = await sib
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
inside 1
after
sibling
```

<!-- test: frozen-graph-shared-counts -->
<!-- procs: 4 -->
Two services read one frozen forest at the same time, three hundred passes each, so every read of a leaf and its label steps the counts of boxes both threads hold; a plain count would lose steps and either leak at exit or free a live box.

```maxon
typealias Tally = int(0 to u64.max)
typealias LeafArray = Array with Leaf

type Leaf
	export var label as String
	export var weight as Tally

	static function create(label String, weight Tally) returns Self
		return Self{label: label, weight: weight}
	end 'create'
end 'Leaf'

type Forest
	export var leaves as LeafArray

	static function create(size Tally) returns Self
		let leaves = LeafArray.create(0)

		for n in 1 to size 'build'
			leaves.push(Leaf.create("leaf {n}", weight: n))
		end 'build'

		return Self{leaves: leaves}
	end 'create'

	function total() returns Tally
		var sum = 0 as Tally

		for leaf in self.leaves 'eachLeaf'
			sum = sum + leaf.weight + (leaf.label.byteLength() as Tally)
		end 'eachLeaf'

		return sum
	end 'total'
end 'Forest'

type Reader
	static function create() returns Self
		return Self{}
	end 'create'

	export function passes(forest immutable Forest, rounds Tally) returns Tally
		var sum = 0 as Tally

		for _ in 1 to rounds 'eachRound'
			sum = sum + forest.total()
		end 'eachRound'

		return sum
	end 'passes'
end 'Reader'

function main() returns ExitCode
	let forest = freeze Forest.create(50)
	let one = spawn Reader.create()
	let two = spawn Reader.create()
	let first = try one.passes(forest, rounds: 300) otherwise panic("main: a fresh reader accepts its only request")
	let second = try two.passes(forest, rounds: 300) otherwise panic("main: a fresh reader accepts its only request")
	let a = await first
	let b = await second
	one.shutdown()
	two.shutdown()
	print("{a} {b} {forest.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
484800 484800 1616
```

<!-- test: freeze-on-wasm -->
`freeze` walks a nested graph, an array of records holding strings, and the frozen result reads back whole. The case carries no marker, so it runs on `wasm32-wasi` too.

```maxon
typealias Tally = int(0 to u64.max)
typealias LeafArray = Array with Leaf

type Leaf
	export var label as String
	export var weight as Tally

	static function create(label String, weight Tally) returns Self
		return Self{label: label, weight: weight}
	end 'create'
end 'Leaf'

type Forest
	export var leaves as LeafArray

	static function create(size Tally) returns Self
		let leaves = LeafArray.create(0)

		for n in 1 to size 'build'
			leaves.push(Leaf.create("leaf {n}", weight: n))
		end 'build'

		return Self{leaves: leaves}
	end 'create'

	function total() returns Tally
		var sum = 0 as Tally

		for leaf in self.leaves 'eachLeaf'
			sum = sum + leaf.weight + (leaf.label.byteLength() as Tally)
		end 'eachLeaf'

		return sum
	end 'total'
end 'Forest'

function main() returns ExitCode
	let forest = freeze Forest.create(10)
	print("{forest.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
116
```

<!-- test: hold-across-may-yield-call -->
`describe` reads `shelf.cell` as its first operand, then `settle` awaits a coroutine that replaces the field with another frozen cell; the first operand is held across that call, so it still reads `old 7` and the leak gate is clean.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Cell'

type Shelf
	export var cell as immutable Cell

	static function create(label Tally) returns Self
		return Self{cell: freeze Cell.create("old {label}")}
	end 'create'
end 'Shelf'

function replace(shelf Shelf) returns Tally
	shelf.cell = freeze Cell.create("new")
	return 1
end 'replace'

function settle(shelf Shelf) returns Tally
	let pending = async replace(shelf)
	return await pending
end 'settle'

function describe(cell Cell, done Tally) returns String
	return "{cell.text} {done}"
end 'describe'

function main() returns ExitCode
	let shelf = Shelf.create(7)
	let line = describe(shelf.cell, done: settle(shelf))
	print("{line} {shelf.cell.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
old 7 1 new
```

<!-- test: strand-guard-holds-sibling-during-a-one-call-open -->
`h.part.park()` opens the isolated `part` for that one call, and `park` sleeps 150 ms inside it while `sibling` sleeps only 10 ms; the pinned order proves no sibling ran while the region was open.

```maxon
typealias LineArray = Array with String

type Part
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'

	function park() returns Count
		sleep(150)
		print("inside\n")
		return 1
	end 'park'
end 'Part'

type Holder
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Holder'

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	print("before\n")
	let h = Holder.create()
	let n = h.part.park()
	print("after {n}\n")
	return 2
end 'editor'

function main() returns ExitCode
	let ed = async editor()
	let sib = async sibling()
	_ = await ed
	_ = await sib
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
inside
after 1
sibling
```

<!-- test: strand-guard-holds-sibling-during-a-one-statement-open -->
`h.part.lines.push(pause())` reads through the isolated `part`, which the statement opens for itself, and `pause` sleeps 150 ms in the middle of it while `sibling` sleeps only 10 ms; the pinned order proves no sibling ran while the region was open.

```maxon
typealias LineArray = Array with String

type Part
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Holder'

function pause() returns String
	sleep(150)
	print("inside\n")
	return "x"
end 'pause'

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	print("before\n")
	let h = Holder.create()
	h.part.lines.push(pause())
	print("after\n")
	return 2
end 'editor'

function main() returns ExitCode
	let ed = async editor()
	let sib = async sibling()
	_ = await ed
	_ = await sib
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
inside
after
sibling
```

<!-- test: strand-guard-is-released-when-a-one-call-open-throws -->
`h.part.refuse()` throws out of its one-call open into `otherwise 0`, so the guard it took is released on the throw edge too: `sibling` then runs during the 60 ms sleep after it, and the program does not end holding a guard.

```maxon
typealias LineArray = Array with String

enum PartError implements Error
	failed
end 'PartError'

type Part
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'

	function refuse() returns Count throws PartError
		if self.lines.count() == 0 'empty'
			throw PartError.failed
		end 'empty'

		return 1
	end 'refuse'
end 'Part'

type Holder
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Holder'

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	print("before\n")
	let h = Holder.create()
	let n = try h.part.refuse() otherwise 0
	sleep(60)
	print("after {n}\n")
	return 2
end 'editor'

function main() returns ExitCode
	let ed = async editor()
	let sib = async sibling()
	_ = await ed
	_ = await sib
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
sibling
after 0
```

<!-- test: strand-guard-holds-sibling-during-an-argument-one-call-open -->
`park(part)` passes the isolated `part` in an unmarked position, which opens it for that one call, and `park` sleeps 150 ms inside it while `sibling` sleeps only 10 ms; the pinned order proves no sibling ran while the region was open.

```maxon
typealias LineArray = Array with String

type Part
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Part'

function park(part Part) returns Count
	sleep(150)
	print("inside {part.lines.count()}\n")
	return 1
end 'park'

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	print("before\n")
	let part = Part.create() as isolated Part
	let n = park(part)
	print("after {n}\n")
	return 2
end 'editor'

function main() returns ExitCode
	let ed = async editor()
	let sib = async sibling()
	_ = await ed
	_ = await sib
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
inside 0
after 1
sibling
```
