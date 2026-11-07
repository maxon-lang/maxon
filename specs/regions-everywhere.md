---
feature: regions-everywhere
status: experimental
keywords: [region tracking, function type, interface, conformer, may wait, thrown payload, self-referential alias, let]
category: semantics
---

# The region checker runs on every program

## Documentation

The region checker judges every program, whether or not the source spells a region word. A function value or a
conformer converts to a function type or an interface requirement only if it declares what it does: a parameter
it writes, keeps or returns is marked, and a function type never waits or yields. A program that spells no
`modify`, `isolated`, `immutable`, `freeze` or `async` is held to the same conversion rules as one that does.

## Tests

<!-- test: a-function-value-keeping-its-parameter-in-a-container-does-not-convert-to-a-plain-function-type -->
`stash` pushes `thing` into the shelf it was handed, so it keeps `thing`. `Stasher` declares nothing about its
second parameter, so a caller holding that argument read-only would have it written through.

```maxon
typealias Tally = int(0 to u64.max)

type Thing
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Thing'

typealias ThingArray = Array with Thing

type Shelf
	export var things as ThingArray

	static function create() returns Self
		return Self{things: ThingArray.create(0)}
	end 'create'

	function count() returns Count
		return self.things.count()
	end 'count'
end 'Shelf'

typealias Stasher = function(mutable Shelf, Thing)

function stash(shelf mutable Shelf, thing Thing)
	shelf.things.push(thing)
end 'stash'

function apply(stasher Stasher, shelf mutable Shelf, thing Thing)
	stasher(shelf, thing)
end 'apply'

function main() returns ExitCode
	var shelf = Shelf.create()
	apply(stash, shelf: shelf, thing: Thing.create(1))
	return shelf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3210: <fragment>:38:8: function value for 'Stasher' writes its parameter 'thing', but 'Stasher' does not declare it `mutable`
```

<!-- test: a-function-value-keeping-its-parameter-in-a-container-converts-to-a-function-type-that-declares-it -->
The accepted twin: `Stasher` declares the second parameter `mutable` and `stash` declares the same marker.

```maxon
typealias Tally = int(0 to u64.max)

type Thing
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Thing'

typealias ThingArray = Array with Thing

type Shelf
	export var things as ThingArray

	static function create() returns Self
		return Self{things: ThingArray.create(0)}
	end 'create'

	function count() returns Count
		return self.things.count()
	end 'count'
end 'Shelf'

typealias Stasher = function(mutable Shelf, mutable Thing)

function stash(shelf mutable Shelf, thing mutable Thing)
	shelf.things.push(thing)
end 'stash'

function apply(stasher Stasher, shelf mutable Shelf, thing mutable Thing)
	stasher(shelf, thing)
end 'apply'

function main() returns ExitCode
	var shelf = Shelf.create()
	var thing = Thing.create(1)
	apply(stash, shelf: shelf, thing: thing)
	return shelf.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-conformer-whose-result-holds-its-receiver-data-needs-mutable-self -->
`Bag.open` returns a `Cursor` that holds the bag's `items`, so it writes through its receiver, and `Cursors.open`
declares a read-only one.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Cursor
	var items as TallyArray

	static function create(items TallyArray) returns Self
		return Self{items: items}
	end 'create'

	function size() returns Count
		return self.items.count()
	end 'size'
end 'Cursor'

interface Cursors
	function open() returns Cursor
end 'Cursors'

type Bag implements Cursors
	var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'

	function open() returns Cursor
		return Cursor.create(self.items)
	end 'open'
end 'Bag'

function first(source Cursors) returns Cursor
	return source.open()
end 'first'

function main() returns ExitCode
	let bag = Bag.create()
	return first(bag).size() as ExitCode
end 'main'
```
```maxoncstderr
error E3210: <fragment>:28:11: 'Bag.open' writes its receiver, but 'Cursors.open' does not declare `mutable self`
```

<!-- test: a-conformer-whose-result-holds-its-receiver-data-conforms-to-a-mutable-self-requirement -->
The accepted twin: the requirement declares `mutable self`.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Cursor
	var items as TallyArray

	static function create(items TallyArray) returns Self
		return Self{items: items}
	end 'create'

	function size() returns Count
		return self.items.count()
	end 'size'
end 'Cursor'

interface Cursors
	function open(mutable self) returns Cursor
end 'Cursors'

type Bag implements Cursors
	var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'

	function open() returns Cursor
		return Cursor.create(self.items)
	end 'open'
end 'Bag'

function first(source Cursors) returns Cursor
	return source.open()
end 'first'

function main() returns ExitCode
	var bag = Bag.create()
	return first(bag).size() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-function-value-returning-its-read-only-parameter-does-not-convert-to-a-plain-function-type -->
`same` returns data reached from its read-only parameter, and a function type promises nothing about what its
result shares with its argument.

```maxon
type Key
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function length() returns Count
		return self.text.count()
	end 'length'
end 'Key'

typealias KeyOf = function(Key) returns Key

function same(key Key) returns Key
	return key
end 'same'

function apply(keyOf KeyOf, key Key) returns Key
	return keyOf(key)
end 'apply'

function main() returns ExitCode
	let key = Key.create("abc")
	return apply(same, key: key).length() as ExitCode
end 'main'
```
```maxoncstderr
error E3210: <fragment>:26:15: function value for 'KeyOf' returns data reached from its read-only parameter 'key', which 'KeyOf' does not allow
```

<!-- test: a-function-value-returning-a-clone-of-its-parameter-converts-to-a-plain-function-type -->
The `clone()` cure: the result shares nothing with the argument.

```maxon
type Key
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function length() returns Count
		return self.text.count()
	end 'length'
end 'Key'

typealias KeyOf = function(Key) returns Key

function same(key Key) returns Key
	return key.clone()
end 'same'

function apply(keyOf KeyOf, key Key) returns Key
	return keyOf(key)
end 'apply'

function main() returns ExitCode
	let key = Key.create("abc")
	return apply(same, key: key).length() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-function-value-throwing-its-parameter-in-a-payload-does-not-convert-to-a-plain-function-type -->
A thrown payload stores its argument, so `find` keeps `key`, and `lookup`, which hands `key` to it, keeps `key` too.

```maxon
type Key
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Key'

union Missing implements Error
	absent(key Key)
end 'Missing'

typealias Lookup = function(Key) returns Count

function find(key Key) returns Count throws Missing
	if key.text.count() == 0 'empty'
		throw Missing.absent(key)
	end 'empty'
	return key.text.count()
end 'find'

function lookup(key Key) returns Count
	return try find(key) otherwise 0
end 'lookup'

function apply(look Lookup, key Key) returns Count
	return look(key)
end 'apply'

function main() returns ExitCode
	let key = Key.create("abc")
	return apply(lookup, key: key) as ExitCode
end 'main'
```
```maxoncstderr
error E3210: <fragment>:33:15: function value for 'Lookup' writes its parameter 'key', but 'Lookup' does not declare it `mutable`
```

<!-- test: a-function-value-throwing-a-clone-of-its-parameter-converts-to-a-plain-function-type -->
The `clone()` cure: the payload holds a copy.

```maxon
type Key
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Key'

union Missing implements Error
	absent(key Key)
end 'Missing'

typealias Lookup = function(Key) returns Count

function find(key Key) returns Count throws Missing
	if key.text.count() == 0 'empty'
		throw Missing.absent(key.clone())
	end 'empty'
	return key.text.count()
end 'find'

function lookup(key Key) returns Count
	return try find(key) otherwise 0
end 'lookup'

function apply(look Lookup, key Key) returns Count
	return look(key)
end 'apply'

function main() returns ExitCode
	let key = Key.create("abc")
	return apply(lookup, key: key) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-function-value-that-awaits-a-service-does-not-convert-to-a-plain-function-type -->
`viaService` calls `ask`, which awaits a reply, so it may wait; a function type never waits or yields.

```maxon
typealias Tally = int(0 to u64.max)

type Doubler
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function twice(n Tally) returns Tally
		self.calls = self.calls + 1
		return n * 2
	end 'twice'
end 'Doubler'

function ask(doubler Doubler.handle, n Tally) returns Tally
	return try await doubler.twice(n) otherwise 0
end 'ask'

function viaService(doubler Doubler.handle) returns Tally
	return ask(doubler, n: 3)
end 'viaService'

typealias Step = function(Doubler.handle) returns Tally

function apply(step Step, doubler Doubler.handle) returns Tally
	return step(doubler)
end 'apply'

function main() returns ExitCode
	let doubler = spawn Doubler.create()
	return apply(viaService, doubler: doubler) as ExitCode
end 'main'
```
```maxoncstderr
error E3210: <fragment>:33:15: function value for 'Step' may wait (it calls 'ask', which it awaits a reply from 'Doubler' at line 18), but a function type never waits or yields
```

<!-- test: error.self-referential-tuple-alias-in-a-file-that-spells-async -->
The termination case for a tuple alias that names itself, in a file the region checker has always tracked: the
walk over the alias's items ends and the program is refused with the same sentence as without `async`.

```maxon
typealias Int = int(i64.min to i64.max)
typealias P = (P, Int)

function make() returns P
	return ((1, 2), 3)
end 'make'

function work() returns Int
	sleep(1)
	return 7
end 'work'

function later() returns Int
	let pending = async work()
	return await pending
end 'later'

function main() returns ExitCode
	let t = make()
	return (t.1 + later()) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return '((int, Int), Int)' from function declared to return 'P'
```

<!-- test: error.function-value-call-keeps-its-argument-in-a-let-field-through-a-mutable-parameter -->
The conversion rule is met (`Maker` and `shelve` both declare the parameter `mutable`), so the call through
`build` is judged by the older door: `shelve` keeps `v` in a `let` field and `v` is read again afterwards.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Shelf
	export let items as IntArray

	static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'
end 'Shelf'

typealias Maker = function(mutable IntArray) returns Shelf

function shelve(d mutable IntArray) returns Shelf
	return Shelf.create(d)
end 'shelve'

function build(m Maker, d mutable IntArray) returns Shelf
	return m(d)
end 'build'

function main() returns ExitCode
	var v = IntArray.create(0)
	v.push(1)
	let shelf = build(shelve, d: v)
	v.push(2)
	return shelf.items.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:26:14: cannot pass the `var` 'v', which is read again, to parameter 'd' of 'build', which stores it into a `let` field: a `let` field has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-write-through-a-let-bound-to-ordinary-data-is-refused-by-the-let-door -->
A `let` is deep-immutable in the older analyses, and that door speaks for a write through it, in a program that spells
no region word.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function grow(d mutable IntArray)
	d.push(9)
end 'grow'

function main() returns ExitCode
	let a = IntArray.create(0)
	grow(a)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:2: cannot pass 'a', which is read-only (a `let` binding), to a function that writes parameter 'd' (in main); pass a clone()
```

<!-- test: a-var-written-through-and-passed-to-a-reading-function-is-accepted -->
A `var` is written through, then handed to a function whose parameter is only read, and both the older and the
region analyses accept it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function total(d IntArray) returns Count
	return d.count()
end 'total'

function main() returns ExitCode
	var a = IntArray.create(0)
	a.push(1)
	a.push(2)
	return total(a) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-merged-isolated-parameter-fills-a-let-field -->
A `let` field takes `merge` of an isolated parameter as it takes the parameter itself: the merge moves the region in, so
no other name may still write it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Row
	export let cells as IntArray
	export let width as Integer

	static function create(width Integer, cells isolated IntArray) returns Row
		return Row{width: width, cells: merge cells}
	end 'create'
end 'Row'

function main() returns ExitCode
	var cells = IntArray.create(0) as isolated IntArray

	modify cells 'fill'
		cells.push(7)
		cells.push(9)
	end 'fill'

	let row = Row.create(2, cells: cells)
	return row.cells.count() as ExitCode
end 'main'
```
```exitcode
2
```
