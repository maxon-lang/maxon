---
feature: deep-let-door-agreement
status: experimental
keywords: [let, read-only, field, call argument, store, owner, orphaned array, key column, sole]
category: type-system
---

# Every door a `let` field is filled through asks the same question

## Documentation

A `let` field has no other writer, so what it takes must be fresh, moved or already read-only. Two doors
fill one: a call to a function that stores its parameter into the field, and a store written in the
function that owns the field's type. They ask one question, and a value is accepted only when nothing else
can still write it:

- a value this function made and names only here, or hands over at its last use, is accepted;
- a value that was also placed inside another container, and is read again after, is not;
- a value read out of a record this function made is accepted only when that record is dead afterwards: reading
  the same field again, passing the record on, returning it or branching on it keeps the record alive, and the
  value read out of it is still named through it;
- an element read out of an array whose slot was rewritten is not fresh, because another holder may still name
  the array or the element. The one array whose elements are always read-only is the key column of a `Map` or a
  `Set`, which only holds keys stored read-only.

## Tests

<!-- test: error.an-element-of-an-array-another-field-also-names-is-not-fresh -->
`orphan` swaps `a` out and reads an element of the old array. `seed` made `b` another name for the same array,
so the element is still reachable through `b` and the `let` field refuses it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

typealias CounterArray = Array with Counter

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

type Hub
	var a as CounterArray
	var b as CounterArray

	static function create() returns Self
		return Self{a: CounterArray.create(), b: CounterArray.create()}
	end 'create'

	function seed(counter Counter)
		self.a.push(counter)
		self.b = self.a
	end 'seed'

	function orphan() returns Keeper
		view old = self.a
		self.a = CounterArray.create()
		return Keeper.create(try old.get(0) otherwise panic("seeded before"))
	end 'orphan'
end 'Hub'

function main() returns ExitCode
	var hub = Hub.create()
	hub.seed(Counter.create(4))
	let kept = hub.orphan()
	return kept.inner.v as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:38:17: cannot pass a value other names may still write to parameter 'inner' of 'Keeper.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-value-made-here-and-stored-once-is-stored-into-a-let-field -->
The accepted twin: the record is made here, named once and stored, so nothing else can write it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Keeper
	export let inner as Counter

	static function made() returns Self
		let counter = Counter.create(1)
		return Self{inner: counter}
	end 'made'
end 'Keeper'

function main() returns ExitCode
	let kept = Keeper.made()
	return kept.inner.v as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-record-read-out-of-an-owner-that-is-handed-out-afterwards-is-not-fresh -->
`owner` is made in `build`, its `inner` goes into the `let` field, and `owner` itself is handed out in the
result. The caller can bind it to a `var` and write `owner.inner`, which is the record the `let` field holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Box
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create(0)}
	end 'create'
end 'Box'

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

function build() returns (Keeper, Box)
	let owner = Box.create()
	let keeper = Keeper.create(owner.inner)
	return (keeper, owner)
end 'build'

function main() returns ExitCode
	let built = build()
	return built.0.inner.v as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:30:22: cannot pass a value other names may still write to parameter 'inner' of 'Keeper.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-record-read-out-of-an-owner-nothing-names-afterwards-is-stored-into-a-let-field -->
The accepted twin: `owner` is not named after the read, so the `let` field holds the record alone.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Box
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create(0)}
	end 'create'
end 'Box'

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

function build() returns Keeper
	let owner = Box.create()
	return Keeper.create(owner.inner)
end 'build'

function main() returns ExitCode
	let built = build()
	return built.inner.v as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-map-that-grows-keeps-every-key -->
A `Map` rehashes into larger arrays when it fills, moving each key out of the old key column into the new one. The
key column holds only read-only keys, so the move is accepted and no key is lost.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias NameMap = Map with (String, Integer)

function main() returns ExitCode
	var names = NameMap.create()

	for i in 0 upto 200 'fill'
		names.upsert("key number {i} padded out long enough to heap allocate", value: i as Integer)
	end 'fill'

	var total = 0 as Integer

	for i in 0 upto 200 'sum'
		total = total + (try names.get("key number {i} padded out long enough to heap allocate") otherwise 0)
	end 'sum'

	print("{names.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200 19900
```

<!-- test: error.a-let-kept-by-a-let-field-cannot-be-moved-into-a-var -->
`wrap` keeps the record `cell` names in a `let` field and is read again, so moving `cell` into a `var` would give a
writer to a record the field reads.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function main() returns ExitCode
	let cell = Cell.create()
	let wrap = Wrap.create(cell)
	var mine = cell
	mine.n = 7
	return wrap.cell.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:23:6: cannot assign the read-only 'cell' to mutable binding 'mine'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-let-kept-by-a-let-field-and-also-returned-is-refused -->
The function hands the record to the caller beside the `Wrap` that keeps it, so the `Wrap`'s `let` field would not be the
only name the record is reachable through.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function register() returns (Wrap, Cell)
	let cell = Cell.create()
	let wrap = Wrap.create(cell)
	return (wrap, cell)
end 'register'

function main() returns ExitCode
	let (wrap, cell) = register()
	return (wrap.cell.n + cell.n) as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:22:18: cannot pass a value this function also hands out to parameter 'cell' of 'Wrap.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-byte-array-kept-by-a-let-field-and-also-returned-is-refused -->
A `ByteArray` is an array like any other: the name a `Layout` keeps in its `let` field may not also be returned.

```maxon
type Layout
	export let name as ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Layout'

function register() returns (Layout, ByteArray)
	let name = ByteArray.create()
	let layout = Layout.create(name)
	return (layout, name)
end 'register'

function main() returns ExitCode
	let (layout, name) = register()
	return (layout.name.count() + name.count()) as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:12:22: cannot pass a value this function also hands out to parameter 'name' of 'Layout.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-let-kept-by-a-let-field-is-read-through-the-field -->
The accepted twin: the record is kept by the field, the caller reads it through the field, and nothing else names it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function register() returns Wrap
	let cell = Cell.create(7)
	return Wrap.create(cell)
end 'register'

function main() returns ExitCode
	let wrap = register()
	let cell = wrap.cell
	return cell.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-var-moved-into-a-let-field-at-its-last-use-is-accepted -->
The other accepted twin: the `var` is the record's only name, and the call that keeps it is its last use.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function main() returns ExitCode
	var cell = Cell.create(1)
	cell.n = 9
	let wrap = Wrap.create(cell)
	return wrap.cell.n as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: a-cell-resident-var-moved-into-a-let-field-at-its-last-use-is-accepted -->
`stored` lives in a cell because `refill` reassigns its parameter. `refill` stores only a record it built, so when the
call that keeps the record is the variable's last use nothing else can name it, exactly as for a variable that is not a
cell.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function refill(dest Words, first String)
	dest = Words.create()
	dest.push(first)
end 'refill'

function main() returns ExitCode
	var stored = Words.create()
	refill(stored, first: "first padded out long enough to heap allocate")
	let shelf = Shelf.create(stored)
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-cell-resident-var-a-callee-filled-from-a-variable-still-named-is-refused -->
`adopt` stores its second parameter into the first, so the cell of `stored` holds the record `other` still names, and a
write through `other` would reach the `let` field.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function adopt(dest Words, src Words)
	dest = src
end 'adopt'

function main() returns ExitCode
	var other = Words.create()
	other.push("first padded out long enough to heap allocate")
	var stored = Words.create()
	adopt(stored, src: other)
	let shelf = Shelf.create(stored)
	other.push("second padded out long enough to heap allocate")
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:20: cannot pass a value other names may still write to parameter 'words' of 'Shelf.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-cell-resident-var-a-callee-also-registered-is-refused -->
`refillAndRemember` pushes the record it stores into the cell onto `registry` as well, so the `let` field would hold a
record the `var` `registry` still reaches.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String
typealias WordsList = Array with Words

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function refillAndRemember(dest Words, registry WordsList)
	dest = Words.create()
	dest.push("padded out long enough to heap allocate")
	registry.push(dest)
end 'refillAndRemember'

function main() returns ExitCode
	var registry = WordsList.create()
	var stored = Words.create()
	refillAndRemember(stored, registry: registry)
	let shelf = Shelf.create(stored)
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:20: cannot pass a value other names may still write to parameter 'words' of 'Shelf.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-cell-resident-var-kept-by-a-let-field-and-also-returned-is-refused -->
The function hands the record to the caller beside the `Shelf` that keeps it, so the `let` field would not be the only
name the record is reachable through; `stored` being a cell changes nothing.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function refill(dest Words, first String)
	dest = Words.create()
	dest.push(first)
end 'refill'

function register() returns (Shelf, Words)
	var stored = Words.create()
	refill(stored, first: "first padded out long enough to heap allocate")
	let shelf = Shelf.create(stored)
	return (shelf, stored)
end 'register'

function main() returns ExitCode
	let (shelf, stored) = register()
	print("{shelf.count()} {stored.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:25:20: cannot pass the `var` 'stored', which is read again, to parameter 'words' of 'Shelf.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-cell-resident-var-read-and-iterated-before-the-let-field-keeps-it-is-accepted -->
Reading the record a cell holds, pushing to it and walking it all leave the variable the record's only name, so the call
that keeps it at the variable's last use is accepted.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function refill(dest Words, first String)
	dest = Words.create()
	dest.push(first)
end 'refill'

function main() returns ExitCode
	var stored = Words.create()
	refill(stored, first: "first padded out long enough to heap allocate")
	stored.push("second padded out long enough to heap allocate")
	var total = 0 as Whole

	for word in stored 'each'
		total = total + (word.byteLength() as Whole)
	end 'each'

	let shelf = Shelf.create(stored)
	print("{shelf.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 91
```

<!-- test: a-cell-resident-var-filled-through-a-forwarding-callee-is-accepted -->
`outer` hands the cell on to `inner`, which stores a record it built there; no record but that one can land in the cell,
so the variable is still the only name for what it holds.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function inner(dest Words, first String)
	dest = Words.create()
	dest.push(first)
end 'inner'

function outer(dest Words, first String)
	inner(dest, first: first)
end 'outer'

function main() returns ExitCode
	var stored = Words.create()
	outer(stored, first: "first padded out long enough to heap allocate")
	let shelf = Shelf.create(stored)
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-cell-resident-var-pushed-onto-a-container-before-the-let-field-keeps-it-is-refused -->
The container `registry` holds the record the cell holds, so a `let` field taking it would share it with a `var`.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String
typealias WordsList = Array with Words

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function refill(dest Words, first String)
	dest = Words.create()
	dest.push(first)
end 'refill'

function main() returns ExitCode
	var registry = WordsList.create()
	var stored = Words.create()
	refill(stored, first: "first padded out long enough to heap allocate")
	registry.push(stored)
	let shelf = Shelf.create(stored)
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:28:20: cannot pass a value other names may still write to parameter 'words' of 'Shelf.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-cell-resident-var-a-callee-handed-back-is-refused -->
`fillAndHandBack` returns the record it stored into the cell, so `kept` and the cell name one record and a write through
`kept` would reach the `let` field.

```maxon
typealias Whole = int(i64.min to i64.max)
typealias Words = Array with String

type Shelf
	export let words as Words

	static function create(words Words) returns Self
		return Self{words: words}
	end 'create'

	function count() returns Whole
		return self.words.count() as Whole
	end 'count'
end 'Shelf'

function fillAndHandBack(dest Words) returns Words
	dest = Words.create()
	dest.push("padded out long enough to heap allocate")
	return dest
end 'fillAndHandBack'

function main() returns ExitCode
	var stored = Words.create()
	var kept = fillAndHandBack(stored)
	let shelf = Shelf.create(stored)
	kept.push("another padded out long enough to heap allocate")
	print("{shelf.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:20: cannot pass a value other names may still write to parameter 'words' of 'Shelf.create', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```
