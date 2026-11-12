---
feature: regions-modify
status: experimental
keywords: [modify, isolated, swap, region, window, one-call open, freeze]
category: semantics
---

# `modify` and the window rule

## Documentation

`modify x 'label' … end 'label'` opens an `isolated` local, parameter or field for several statements. Inside
the block `x` keeps its name and is writable; everything outside is read-only except block locals, objects
created inside the block and outer `var`s holding values. A call on an `isolated` value, and a statement that
reads or writes through one, opens it for that one call or statement under the same rules.

The window is checked locally. Only the region's contents are writable, so the slot holding `x` cannot be
reassigned inside its own block. An outer `var` holding a record cannot be reassigned there, and a mutable
value cannot leave the block by `return`, by `throw` or by an outer variable. Nothing inside waits on another party:
`await` is refused. `swap path with value` takes a region out of an `isolated` slot, and `freeze` turns a
filled region into frozen data.

## Tests

<!-- test: build-then-freeze-runs -->
The index fills a region of its own inside `modify`, is frozen on return, and `lineOf` reads the frozen result.

```maxon
typealias LineNumber = int(1 to u32.max)
typealias DeclarationMap = Map with (String, Declaration)
typealias DeclarationArray = Array with Declaration

type Declaration
	export var name as String
	export var line as LineNumber

	static function create(name String, line LineNumber) returns Self
		return Self{name: name, line: line}
	end 'create'
end 'Declaration'

type IndexBuilder
	var byName as DeclarationMap

	static function create() returns Self
		return Self{byName: DeclarationMap.create()}
	end 'create'

	function add(declaration Declaration)
		self.byName.upsert(declaration.name, value: declaration)
	end 'add'

	function lineOf(name String) returns LineNumber
		return (try self.byName.get(name) otherwise panic("lineOf: every name was added")).line
	end 'lineOf'
end 'IndexBuilder'

typealias SettledIndex = immutable IndexBuilder

function buildIndex(declarations DeclarationArray) returns SettledIndex
	let builder = IndexBuilder.create() as isolated IndexBuilder

	modify builder 'fill'
		for declaration in declarations 'eachDeclaration'
			builder.add(declaration.clone())
		end 'eachDeclaration'
	end 'fill'

	return freeze builder
end 'buildIndex'

function main() returns ExitCode
	let declarations = DeclarationArray.create(0)
	declarations.push(Declaration.create("alpha", line: 3))
	declarations.push(Declaration.create("beta", line: 9))
	let index = buildIndex(declarations)
	let alpha = index.lineOf("alpha")
	let beta = index.lineOf("beta")
	print("{alpha} {beta}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 9
```

<!-- test: a-capability-typealias-over-a-generic-alias-resolves-through-the-alias -->
A capability word written over the name of a generic alias denotes that alias's instance, so a parameter of the capability alias reads like the instance itself.

```maxon
typealias Tally = int(0 to u64.max)

type Node
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Node'

typealias NodeArray = Array with Node
typealias FrozenNodes = immutable NodeArray

function total(items FrozenNodes) returns Count
	return items.count()
end 'total'

function main() returns ExitCode
	let nodes = NodeArray.create(0) as isolated NodeArray
	modify nodes 'fill'
		nodes.push(Node.create(1))
		nodes.push(Node.create(2))
	end 'fill'
	print("{total(freeze nodes)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: freeze-of-a-call-that-returns-its-temporary-argument-runs -->
The temporary the argument was built into is released before the freeze walks the result, so the result is the record's only owner.

```maxon
function pick(bytes ByteArray) returns ByteArray
	return bytes
end 'pick'

function main() returns ExitCode
	let frozen = freeze pick(ByteArray.create(3))
	print("{frozen.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: modify-of-non-isolated-refused -->
Without `as isolated Counter` the local is ordinary mutable data of this region, and `modify` opens only an isolated value.

```maxon
typealias Tally = int(0 to u64.max)

type Counter
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function value() returns Tally
		return self.total
	end 'value'
end 'Counter'

function main() returns ExitCode
	var counter = Counter.create()

	modify counter 'fill'
		counter.add(1)
	end 'fill'

	print("{counter.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:9: 'counter' is not isolated; 'modify' opens an isolated value. Declare it `as isolated Counter`
```

<!-- test: isolated-field-modify-one-call-swap-runs -->
An `isolated` field is filled inside `modify`, written by a one-call open, and taken out whole with `swap`; the replacement region is empty and `files` counts the one `modify`.

```maxon
typealias Tally = int(0 to u64.max)
typealias WordCounts = Map with (String, Tally)
typealias WordArray = Array with String

type WordIndex
	var counts as WordCounts

	static function create() returns Self
		return Self{counts: WordCounts.create()}
	end 'create'

	function add(word String)
		let seen = try self.counts.get(word) otherwise 0
		self.counts.upsert(word, value: seen + 1)
	end 'add'

	function seen(word String) returns Tally
		return try self.counts.get(word) otherwise 0
	end 'seen'
end 'WordIndex'

type Indexer
	var index as isolated WordIndex
	var filesSeen = 0 as Tally

	static function create() returns Self
		return Self{index: WordIndex.create()}
	end 'create'

	function addWords(words WordArray)
		modify self.index 'fill'
			for word in words 'eachWord'
				self.index.add(word)
			end 'eachWord'
		end 'fill'

		self.filesSeen = self.filesSeen + 1
	end 'addWords'

	function addWord(word String)
		self.index.add(word)
	end 'addWord'

	function takeIndex() returns isolated WordIndex
		return swap self.index with WordIndex.create()
	end 'takeIndex'

	function files() returns Tally
		return self.filesSeen
	end 'files'
end 'Indexer'

function main() returns ExitCode
	let words = WordArray.create(0)
	words.push("a")
	words.push("b")
	words.push("a")

	let indexer = Indexer.create()
	indexer.addWords(words)
	indexer.addWord("a")

	let taken = indexer.takeIndex()
	let again = indexer.takeIndex()
	let a = taken.seen("a")
	let b = taken.seen("b")
	let fresh = again.seen("a")
	print("{a} {b} {fresh} {indexer.files()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 1 0 1
```

<!-- test: call-into-isolated-with-mutable-argument-refused -->
`self.index.absorb(spare)` opens `index` for the call, and `spare` is a mutable `WordIndex` of this region, so the region would point out.

```maxon
typealias Tally = int(0 to u64.max)
typealias WordCounts = Map with (String, Tally)

type WordIndex
	var counts as WordCounts

	static function create() returns Self
		return Self{counts: WordCounts.create()}
	end 'create'

	function add(word String)
		let seen = try self.counts.get(word) otherwise 0
		self.counts.upsert(word, value: seen + 1)
	end 'add'

	function absorb(other WordIndex)
		for (word, seen) in other.counts 'eachWord'
			let mine = try self.counts.get(word) otherwise 0
			self.counts.upsert(word, value: mine + seen)
		end 'eachWord'
	end 'absorb'
end 'WordIndex'

type Indexer
	var index as isolated WordIndex

	static function create() returns Self
		return Self{index: WordIndex.create()}
	end 'create'

	function absorbSpare()
		var spare = WordIndex.create()
		spare.add("x")
		self.index.absorb(spare)
	end 'absorbSpare'
end 'Indexer'

function main() returns ExitCode
	var indexer = Indexer.create()
	indexer.absorbSpare()
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:35:21: 'spare' is mutable outside 'index'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: writable-receiver-needed-inside-window -->
Inside `'fill'` the rest of `self` is read-only, so a method that writes `self` cannot be called there.

```maxon
typealias Tally = int(0 to u64.max)
typealias WordCounts = Map with (String, Tally)
typealias WordArray = Array with String

type WordIndex
	var counts as WordCounts

	static function create() returns Self
		return Self{counts: WordCounts.create()}
	end 'create'

	function add(word String)
		let seen = try self.counts.get(word) otherwise 0
		self.counts.upsert(word, value: seen + 1)
	end 'add'
end 'WordIndex'

type Indexer
	var index as isolated WordIndex
	var filesSeen = 0 as Tally

	static function create() returns Self
		return Self{index: WordIndex.create()}
	end 'create'

	function noteFile()
		self.filesSeen = self.filesSeen + 1
	end 'noteFile'

	function addWords(words WordArray)
		modify self.index 'fill'
			for word in words 'eachWord'
				self.index.add(word)
			end 'eachWord'

			self.noteFile()
		end 'fill'
	end 'addWords'

	function files() returns Tally
		return self.filesSeen
	end 'files'
end 'Indexer'

function main() returns ExitCode
	var words = WordArray.create(0)
	words.push("a")

	var indexer = Indexer.create()
	indexer.addWords(words)
	print("{indexer.files()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:4: 'noteFile' needs a writable receiver at line 28; 'self' is read-only while 'fill' is open
```

<!-- test: mutable-value-leaves-window-refused -->
The row `get` returns is mutable data inside the region, so returning it from the block would let it outlive the window.

```maxon
typealias RowArray = Array with Row

type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

function firstRow(rows isolated RowArray) returns Row
	modify rows 'read'
		return try rows.get(0) otherwise panic("firstRow: rows is never empty")
	end 'read'
end 'firstRow'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:14:10: 'rows.get(0)' is mutable and cannot leave 'read'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: returned-closure-literal-leaving-window-is-named-by-its-site -->
A closure literal returned from the block captures the region's contents, so it cannot leave the window either. It is
named as every other closure is, by where it is written.

```maxon
typealias Tally = int(0 to u64.max)
typealias LineArray = Array with String
typealias Thunk = function() returns Tally

function make() returns Thunk
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		batch.push("a")
		return function() gives batch.count()
	end 'fill'

	return function() gives 0
end 'make'

function main() returns ExitCode
	let t = make()
	return t() as ExitCode
end 'main'
```
```maxoncstderr
error E3202: <fragment>:11:10: the closure at <fragment>:11:10 is mutable and cannot leave 'fill'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: thrown-region-contents-leaving-window-refused -->
An error carries its payload out of the function, so a payload that holds the region's array leaves the block exactly as a returned view does.

```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

type Bag
	export var names as NameArray
	export var count as Tally

	static function create(first String) returns Self
		let names = NameArray.create(0)
		names.push(first)

		return Self{names: names, count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Bag'

union BagFault implements Error
	named(names NameArray)
end 'BagFault'

function run(bag isolated Bag) throws BagFault
	modify bag 'open'
		bag.bump()

		throw BagFault.named(bag.names)
	end 'open'
end 'run'

function main() returns ExitCode
	let bag = Bag.create("alpha") as isolated Bag

	try run(bag) otherwise (fault) 'failed'
		match fault 'which'
			named(names) then print("{names.count()}\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:29:9: 'BagFault.named(bag.names)' is mutable and cannot leave 'open'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: thrown-record-read-inside-window-refused -->
A record an element read bound inside the block is region data too, and naming it in the payload does not make it a value.

```maxon
typealias Tally = int(0 to u64.max)
typealias RowArray = Array with Row

type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

type Table
	export var rows as RowArray
	export var count as Tally

	static function create(first String) returns Self
		let rows = RowArray.create(0)
		rows.push(Row.create(first))

		return Self{rows: rows, count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Table'

union RowFault implements Error
	named(row Row)
	empty
end 'RowFault'

function run(table isolated Table) throws RowFault
	modify table 'open'
		table.bump()

		let first = try table.rows.get(0) otherwise throw RowFault.empty

		throw RowFault.named(first)
	end 'open'
end 'run'

function main() returns ExitCode
	let table = Table.create("alpha") as isolated Table

	try run(table) otherwise (fault) 'failed'
		match fault 'which'
			named(row) then print("{row.text}\n")
			empty then print("empty\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:40:9: 'RowFault.named(first)' is mutable and cannot leave 'open'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: thrown-values-read-inside-window-run -->
A `String` is a value, so a payload built from the block's text, or from a string an element read produced, may leave through the error.

```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

type Bag
	export var name as String
	export var names as NameArray
	export var count as Tally

	static function create(name String) returns Self
		let names = NameArray.create(0)
		names.push(name)

		return Self{name: name, names: names, count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Bag'

union BagFault implements Error
	named(text String)
	empty
end 'BagFault'

function fromName(bag isolated Bag) throws BagFault
	modify bag 'open'
		bag.bump()

		throw BagFault.named(bag.name)
	end 'open'
end 'fromName'

function fromElement(bag isolated Bag) throws BagFault
	modify bag 'open'
		bag.bump()

		let first = try bag.names.get(0) otherwise throw BagFault.empty

		throw BagFault.named(first)
	end 'open'
end 'fromElement'

function main() returns ExitCode
	let first = Bag.create("alpha") as isolated Bag
	let second = Bag.create("alpha") as isolated Bag

	try fromName(first) otherwise (fault) 'nameFailed'
		match fault 'nameWhich'
			named(text) then print("{text}\n")
			empty then print("empty\n")
		end 'nameWhich'
	end 'nameFailed'

	try fromElement(second) otherwise (fault) 'elementFailed'
		match fault 'elementWhich'
			named(text) then print("{text}\n")
			empty then print("empty\n")
		end 'elementWhich'
	end 'elementFailed'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha
alpha
```

<!-- test: thrown-region-contents-leaving-an-enclosing-try-refused -->
A block-form `try` around the `modify` carries the thrown payload out of the window to its handler.

```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

type Bag
	export var names as NameArray
	export var count as Tally

	static function create(first String) returns Self
		let names = NameArray.create(0)
		names.push(first)

		return Self{names: names, count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Bag'

union BagFault implements Error
	named(names NameArray)
end 'BagFault'

function main() returns ExitCode
	let bag = Bag.create("alpha") as isolated Bag

	try 'guard'
		modify bag 'open'
			bag.bump()

			throw BagFault.named(bag.names)
		end 'open'
	end 'guard'
	otherwise (fault) 'failed'
		match fault 'which'
			named(names) then print("{names.count()}\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:32:10: 'BagFault.named(bag.names)' is mutable and cannot leave 'open'; a value leaving a modify block must be a value, immutable or a fresh isolated
```

<!-- test: thrown-region-contents-caught-inside-the-window-runs -->
A block-form `try` written inside the `modify` hands the payload to a handler that is still inside the window, so nothing leaves it.

```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

type Bag
	export var names as NameArray
	export var count as Tally

	static function create(first String) returns Self
		let names = NameArray.create(0)
		names.push(first)

		return Self{names: names, count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Bag'

union BagFault implements Error
	named(names NameArray)
end 'BagFault'

function run(bag isolated Bag)
	modify bag 'open'
		bag.bump()

		try 'guard'
			throw BagFault.named(bag.names)
		end 'guard'
		otherwise (fault) 'failed'
			match fault 'which'
				named(names) then print("{names.count()}\n")
			end 'which'
		end 'failed'
	end 'open'
end 'run'

function main() returns ExitCode
	let bag = Bag.create("alpha") as isolated Bag
	run(bag)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: thrown-region-contents-leaving-the-statement-that-opens-refused -->
A payload built from the one-statement open's region data leaves with the error just as a returned one does.

```maxon
typealias LineArray = Array with String

type Page
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Page'

type Holder
	export var page as isolated Page

	static function create() returns Self
		return Self{page: Page.create()}
	end 'create'
end 'Holder'

union PageFault implements Error
	named(lines LineArray)
end 'PageFault'

function check(holder Holder) throws PageFault
	throw PageFault.named(holder.page.lines)
end 'check'

function main() returns ExitCode
	let holder = Holder.create()

	try check(holder) otherwise (fault) 'failed'
		match fault 'which'
			named(lines) then print("{lines.count()}\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:25:8: 'PageFault.named(holder.page.lines)' is mutable and cannot leave the statement that opens 'page'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: opened-slot-reassigned-inside-window -->
Only the region's contents are writable inside its block, so the variable holding the opened region cannot be reassigned there.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function main() returns ExitCode
	var slot = Bag.create() as isolated Bag

	modify slot 'edit'
		slot.add(1)
		slot = Bag.create()
	end 'edit'

	let n = slot.size()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:25:3: 'slot' is open in 'edit' and cannot be reassigned there
```

<!-- test: outer-record-var-assigned-inside-window -->
An outer `var` that holds a record is read-only inside the window, so assigning it a new record there is refused.

```maxon
typealias LineArray = Array with String

type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

function main() returns ExitCode
	var keep = Row.create("a")
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		batch.push("x")
		keep = Row.create("b")
	end 'fill'

	print("{keep.text}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3204: <fragment>:18:3: 'keep' holds a record from outside 'fill' and cannot be reassigned while 'fill' is open
```

<!-- test: isolated-part-read-outside-block-refused -->
`self.part` is an `isolated` field, and binding it with `let` is neither a move, an open nor a `swap`.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

type Holder
	var part as isolated Bag

	static function create() returns Self
		return Self{part: Bag.create()}
	end 'create'

	function peek() returns Tally
		let alias = self.part
		return alias.size()
	end 'peek'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	let n = holder.peek()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:24:15: 'self.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: write-through-frozen-refused -->
A frozen record is never written again, so the write through `pinned` is refused.

```maxon
typealias FieldNumber = int(0 to 1024)

type Settings
	export var field as FieldNumber
	export var numeric as bool

	static function create(field FieldNumber, numeric bool) returns Self
		return Self{field: field, numeric: numeric}
	end 'create'
end 'Settings'

function main() returns ExitCode
	var pinned = freeze Settings.create(7, numeric: false)
	pinned.numeric = true
	print("{pinned.field}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:15:2: cannot write 'numeric' through 'pinned', which is immutable
```

<!-- test: capability-mismatch-at-store -->
Storing needs equal capabilities: `row` is mutable data of this region, and `pinned` holds frozen data.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

type Holder
	var pinned as immutable Row

	static function create() returns Self
		return Self{pinned: freeze Row.create("a")}
	end 'create'

	function replace()
		var row = Row.create("b")
		row.text = "c"
		self.pinned = row
	end 'replace'

	function text() returns String
		return self.pinned.text
	end 'text'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.replace()
	print("{holder.text()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:17: cannot store a mutable 'Row' into 'pinned', which holds an immutable 'Row'; freeze it
```

<!-- test: await-inside-modify-refused -->
A coroutine starts running at its caller's next wait, so an `await` inside the block could run it while `batch` is open.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	sleep(1)
	return 4
end 'slow'

function main() returns ExitCode
	let batch = LineArray.create(0) as isolated LineArray
	let later = async slow()

	modify batch 'edit'
		let n = await later
		batch.push("{n}")
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:15:11: cannot wait inside 'edit'; another coroutine could run while 'batch' is open. Await before or after the block
```

<!-- test: break-leaves-window-lets-sibling-run -->
`editor` leaves the block by `break`, then sleeps 150 ms; `sibling` sleeps 10 ms. The window's strand guard is released on the `break` path, so `sibling` runs during the sleep and prints between `left` and `done`.

```maxon
typealias LineArray = Array with String

function sibling() returns Count
	sleep(10)
	print("sibling\n")
	return 1
end 'sibling'

function editor() returns Count
	let batch = LineArray.create(0) as isolated LineArray

	for round in 0 upto 4 'eachRound'
		modify batch 'fill'
			batch.push("round {round}")

			if batch.count() == 2 'enough'
				break
			end 'enough'
		end 'fill'
	end 'eachRound'

	print("left\n")
	sleep(150)
	print("done\n")
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
left
sibling
done
```

<!-- test: declared-isolated-slot-is-checked-in-a-file-with-no-region-word -->
`b.maxon` spells no region word, but `a.maxon` declares `part` isolated, so the store of a mutable local into it is refused there too.

```maxon
// --- file: a.maxon
export type Bag
	export var text as String

	export static function create() returns Self
		return Self{text: "bag"}
	end 'create'
end 'Bag'

export type Holder
	export var part as isolated Bag

	export static function create() returns Self
		return Self{part: Bag.create()}
	end 'create'
end 'Holder'

// --- file: b.maxon
function main() returns ExitCode
	var holder = Holder.create()
	var spare = Bag.create()
	holder.part = spare
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:23:16: 'spare' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Bag`, or store a copy
```

<!-- test: declared-immutable-result-is-frozen-in-a-file-with-no-region-word -->
`frozenBag` declares `returns immutable Bag`, so its result passes to the `immutable` parameter of `describe` from a file that spells no region word.

```maxon
// --- file: a.maxon
export type Bag
	export var text as String

	export static function create() returns Self
		return Self{text: "bag"}
	end 'create'
end 'Bag'

export function frozenBag() returns immutable Bag
	return freeze Bag.create()
end 'frozenBag'

export function describe(bag immutable Bag) returns String
	return bag.text
end 'describe'

// --- file: b.maxon
function main() returns ExitCode
	let pinned = frozenBag()
	print("{describe(pinned)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bag
```

<!-- test: one-statement-open-refuses-outer-mutable-data-stored-into-the-region -->
`holder.page.lines = outer` writes through the isolated `page`, which a statement opens for itself, and `outer` is mutable data of this region.

```maxon
typealias LineArray = Array with String

type Page
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Page'

type Holder
	export var page as isolated Page

	static function create() returns Self
		return Self{page: Page.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	var outer = LineArray.create(0)
	outer.push("a")
	holder.page.lines = outer
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:24:22: 'outer' is mutable outside 'holder.page'; a store into an isolated region takes values, immutable or isolated
```

<!-- test: one-statement-open-refuses-region-data-bound-outside-the-statement -->
`holder.page.lines` is mutable data of the isolated region, and a `var` binds outside the statement that opened it.

```maxon
typealias LineArray = Array with String

type Page
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Page'

type Holder
	export var page as isolated Page

	static function create() returns Self
		return Self{page: Page.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	var escaped = holder.page.lines
	escaped.push("a")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:22:16: 'holder.page.lines' is mutable and cannot leave the statement that opens 'page'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: one-statement-open-takes-values-and-fresh-data -->
Inside the one-statement open a store takes a fresh call's result, a call on the opened path takes values, and a count leaves as a value.

```maxon
typealias LineArray = Array with String

type Page
	export var lines as LineArray

	static function create() returns Self
		return Self{lines: LineArray.create(0)}
	end 'create'
end 'Page'

type Holder
	export var page as isolated Page

	static function create() returns Self
		return Self{page: Page.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	holder.page.lines = LineArray.create(0)
	holder.page.lines.push("a")
	holder.page.lines.push("b")
	let n = holder.page.lines.count()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: witness-call-on-outer-data-inside-modify-needs-a-writable-receiver -->
`g` is outer data, read-only while `'fill'` is open, and `Grows.grow` is declared `mutable self`.

```maxon
typealias Tally = int(0 to u64.max)
typealias LineArray = Array with String

interface Grows
	function grow(mutable self, by Tally)
end 'Grows'

type Bag implements Grows
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function grow(by Tally)
		self.total = self.total + by
	end 'grow'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function stretch(g Grows)
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		g.grow(5)
		batch.push("x")
	end 'fill'
end 'stretch'

function main() returns ExitCode
	var bag = Bag.create()
	stretch(bag)
	print("{bag.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:29:5: 'Grows.grow' needs a writable receiver; 'g' is read-only while 'fill' is open
```

<!-- test: function-value-call-on-outer-data-inside-modify-needs-a-writable-parameter -->
`row` is outer data, read-only while `'fill'` is open, and `Edit` declares its parameter `mutable`.

```maxon
typealias LineArray = Array with String
typealias Edit = function(mutable Row)

type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

function rename(row mutable Row)
	row.text = "renamed"
end 'rename'

function run(edit Edit)
	var row = Row.create("a")
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		edit(row)
		batch.push("x")
	end 'fill'
end 'run'

function main() returns ExitCode
	run(rename)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:22:7: 'Edit' needs a writable parameter; 'row' is read-only while 'fill' is open
```

<!-- test: closure-written-inside-modify-sees-outer-data-as-read-only -->
The closure is built inside `'fill'` and writes `row`, outer data that is read-only while the block is open.

```maxon
typealias Tally = int(0 to u64.max)
typealias LineArray = Array with String
typealias Rename = function() returns Tally mutates captures

type Row
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function rename() returns Tally
		self.text = "renamed"
		return 1
	end 'rename'
end 'Row'

function consume(rename Rename) returns Tally
	return rename()
end 'consume'

function main() returns ExitCode
	var row = Row.create("a")
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = consume(function() gives row.rename())
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:28:11: the closure at <fragment>:28:19 writes data it captured, and that data is read-only while 'fill' is open; write it outside the closure or take the data as a parameter
```

<!-- test: one-statement-open-refuses-two-isolated-values-of-different-holders -->
`a.part.n + b.part.n` reads through the isolated `part` of two holders, and one statement opens one isolated value, even though only numbers leave.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create()
	let b = Pair.create()
	let total = a.part.n + b.part.n
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:23:25: one statement reaches through two isolated values, 'part' and 'part'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: one-statement-open-refuses-two-isolated-fields-of-one-holder -->
`a.left.n + a.right.n` reads through two isolated fields of one holder, which are two regions.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Part'

type Pair
	export var left as isolated Part
	export var right as isolated Part

	static function create() returns Self
		return Self{left: Part.create(), right: Part.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create()
	let total = a.left.n + a.right.n
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:23:25: one statement reaches through two isolated values, 'left' and 'right'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: one-statement-open-refuses-a-one-call-open-beside-a-read-through-another-region -->
`a.part.twice()` opens `a.part` for the call, and `b.part.n` reads through a second region in the same statement.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function twice() returns Tally
		return self.n + self.n
	end 'twice'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create()
	let b = Pair.create()
	let total = a.part.twice() + b.part.n
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:31: one statement reaches through two isolated values, 'part' and 'part'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: one-statement-open-refuses-a-read-through-a-region-beside-a-one-call-open-of-another -->
`a.part.n` reads through a region, and `b.part.twice()` opens a second one for the call in the same statement.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function twice() returns Tally
		return self.n + self.n
	end 'twice'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create()
	let b = Pair.create()
	let total = a.part.n + b.part.twice()
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:31: one statement reaches through two isolated values, 'part' and 'part'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: one-statement-takes-two-one-call-opens-and-one-region-twice -->
Two one-call opens of different regions in one statement are accepted when only values leave, and one region read twice in a statement is one open.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function twice() returns Tally
		return self.n + self.n
	end 'twice'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create()
	let b = Pair.create()
	let calls = a.part.twice() + b.part.twice()
	let reads = a.part.n + a.part.n
	let mixed = a.part.twice() + a.part.n
	print("{calls} {reads} {mixed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 2 3
```

<!-- test: call-with-an-isolated-argument-refuses-mutable-data-beside-it -->
`link(iso, b: outer)` passes the isolated `iso` in an unmarked position, which opens it for the call, so the mutable `outer` of this region may not ride along.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bag'

function link(a Bag, b Bag)
	a.n = b.n
end 'link'

function main() returns ExitCode
	var iso = Bag.create(1) as isolated Bag
	var outer = Bag.create(2)
	link(iso, b: outer)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:19:15: 'outer' is mutable outside 'iso'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: call-with-two-isolated-arguments-is-refused -->
Both arguments of `link` are isolated and sit in unmarked positions, so the one call would open two regions.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bag'

function link(a Bag, b Bag)
	a.n = b.n
end 'link'

function main() returns ExitCode
	var left = Bag.create(1) as isolated Bag
	var right = Bag.create(2) as isolated Bag
	link(left, b: right)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:19:16: one statement reaches through two isolated values, 'left' and 'right'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: call-with-an-isolated-argument-takes-values-and-answers-a-value-runs -->
`weigh(iso, factor: 3)` opens `iso` for the call, takes a value beside it and answers a value, and `iso` is still there for the next call.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bag'

function weigh(bag Bag, factor Tally) returns Tally
	return bag.n * factor
end 'weigh'

function main() returns ExitCode
	let iso = Bag.create(7) as isolated Bag
	let first = weigh(iso, factor: 3)
	let second = weigh(iso, factor: 2)
	print("{first} {second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21 14
```

<!-- test: call-with-an-isolated-field-as-an-argument-takes-values-and-answers-a-value-runs -->
`count(h.part)` opens the isolated field `part` for the call, takes nothing beside it and answers a value.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create(n Tally) returns Self
		return Self{part: Part.create(n)}
	end 'create'
end 'Holder'

function count(part Part) returns Tally
	return part.n
end 'count'

function main() returns ExitCode
	let h = Holder.create(4)
	let k = count(h.part)
	print("{k}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: call-with-two-isolated-fields-as-arguments-is-refused -->
`link(h.part, second: g.part)` opens the isolated field of two holders in one call.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create(n Tally) returns Self
		return Self{part: Part.create(n)}
	end 'create'
end 'Holder'

function link(first Part, second Part)
	first.n = second.n
end 'link'

function main() returns ExitCode
	var h = Holder.create(4)
	var g = Holder.create(5)
	link(h.part, second: g.part)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:23: one statement reaches through two isolated values, 'part' and 'part'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: loop-swap-out-of-a-holder-the-back-edge-assigns-frozen-data -->
The first trip swaps the region out of a fresh holder, but `h = pinned` hands the next trip a frozen holder, so at the loop header `h` is read-only and the swap is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create(n Tally) returns Self
		return Self{part: Part.create(n)}
	end 'create'
end 'Holder'

function edit(pinned immutable Holder, rounds Tally)
	var h = Holder.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		let old = swap h.part with Part.create(1)
		h = pinned
		i = i + old.n
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Holder.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:25:13: 'h.part' cannot be swapped: the value that holds it is read-only, as it joins immutable and mutable data
```

<!-- test: loop-modify-of-a-holder-field-the-back-edge-assigns-frozen-data -->
`modify h.part` opens a field of the carried `h`, and the next trip's `h` is the frozen holder, so the block is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create(n Tally) returns Self
		return Self{part: Part.create(n)}
	end 'create'
end 'Holder'

function edit(pinned immutable Holder, rounds Tally)
	var h = Holder.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		modify h.part 'fill'
			h.part.n = 3
		end 'fill'

		h = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Holder.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:25:10: 'h.part' cannot be opened: the value that holds it is read-only, as it joins immutable and mutable data
```

<!-- test: async-call-with-an-isolated-argument-in-an-unmarked-position-is-refused -->
An `async` call keeps its arguments alive while another coroutine runs, so it takes an isolated value only in an `isolated` parameter, never as a one-call open.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bag'

function count(bag Bag) returns Tally
	sleep(1)
	return bag.n
end 'count'

function main() returns ExitCode
	let iso = Bag.create(7) as isolated Bag
	let pending = async count(iso)
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:28: 'count' is spawned with `async`, and 'iso' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: call-with-an-isolated-argument-refuses-a-record-result -->
`pick(iso)` opens `iso` for the call and answers a `Bag` that may be the region's own, so the result may not leave the call.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Bag'

function pick(bag Bag) returns Bag
	return bag
end 'pick'

function main() returns ExitCode
	let iso = Bag.create(1) as isolated Bag
	let same = pick(iso)
	print("{same.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:18:13: 'pick' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: assignment-of-mutable-data-to-an-isolated-variable-is-refused -->
`e` took `a`'s region, so it is an isolated variable for good, and `spare` is mutable data of this region.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	var spare = Box.create(2)
	spare.n = 3
	var e = a
	e = spare
	print("{e.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:17:6: 'spare' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: assignment-of-isolated-data-to-a-mutable-variable-is-refused -->
`e` holds mutable data of this region, so an isolated region is merged into it, never assigned over it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	var e = Box.create(0)
	e.n = 2
	e = a
	print("{e.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:6: cannot store an isolated 'Box' into 'e', which holds a mutable 'Box'; `merge` it first
```

<!-- test: container-store-of-frozen-data-into-a-mutable-array-is-refused -->
`BoxArray` holds mutable `Box` elements, so frozen data stored by `push` would be written through the element.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var boxes = BoxArray.create(0)
	boxes.push(pinned)
	print("{boxes.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:13: cannot store an immutable 'Box' into 'an element of the container push writes', which holds a mutable 'Box'; declare the element `immutable Box`
```

<!-- test: container-store-of-isolated-data-into-a-mutable-array-is-refused -->
`BoxArray` holds mutable `Box` elements, so an isolated region stored by `push` would be shared with the array while `a` still names it.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	var boxes = BoxArray.create(0)
	boxes.push(a)
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:13: cannot store an isolated 'Box' into 'an element of the container push writes', which holds a mutable 'Box'; `merge` it first
```

<!-- test: array-literal-element-of-isolated-data-is-refused -->
The literal's elements are mutable `Box` slots of the new array, so an isolated region written into one would be shared with `a`.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	var boxes = BoxArray from [a]
	print("{a.n} {boxes.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:15:29: cannot store an isolated 'Box' into 'an element of the array literal', which holds a mutable 'Box'; `merge` it first
```

<!-- test: spawn-of-a-waiting-factory-inside-modify-refused -->
`spawn Relay.create()` runs the factory, which awaits a reply, while `batch` is open.

```maxon
typealias Tally = int(0 to u64.max)
typealias LineArray = Array with String

type Oracle
	static function create() returns Self
		return Self{}
	end 'create'

	export function answer() returns Tally
		return 42
	end 'answer'
end 'Oracle'

type Relay
	var seen as Tally

	static function create() returns Self
		let oracle = spawn Oracle.create()
		let n = try await oracle.answer() otherwise 0
		return Self{seen: n}
	end 'create'

	export function seenSoFar() returns Tally
		return self.seen
	end 'seenSoFar'
end 'Relay'

function main() returns ExitCode
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		_ = spawn Relay.create()
		batch.push("started")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:33:7: cannot spawn 'create' inside 'fill'; it may wait (it awaits a reply from 'Oracle' at line 20). Spawn it before or after the block
```

<!-- test: a-call-that-sends-with-a-timeout-inside-modify-refused -->
`push` sends with `sendTimeout:`, which waits for room in a full mailbox, so it may wait while `batch` is open.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

type Store
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function fill()
		self.n = self.n + 1
	end 'fill'
end 'Store'

function push(h Store.handle)
	try h.fill(sendTimeout: ForeverMs) otherwise panic("a send that waits without end returns only when it is delivered or the service stops")
end 'push'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'edit'
		push(h)
		batch.push("sent")
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:26:3: cannot call 'push' inside 'edit'; it may wait (it waits on another party). Call it before or after the block
```

<!-- test: a-one-call-open-of-an-isolated-argument-to-a-generic-callee-counts-as-a-region -->
`count` is generic, so `count(1, part: a.part)` opens `a.part` for the call, and `b.part.n` reads through a second region in the same statement.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function count(_ T, part Part) uses T returns Tally
	return part.n
end 'count'

function main() returns ExitCode
	let a = Pair.create()
	let b = Pair.create()
	let total = count(1, part: a.part) + b.part.n
	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:39: one statement reaches through two isolated values, 'part' and 'part'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: an-async-call-to-a-generic-callee-with-an-isolated-argument-in-an-unmarked-position-is-refused -->
`take` is generic, so the `async` call is to an instance of it, and the isolated `a.part` is refused in its unmarked `part` position as it is for a plain callee.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Part'

type Pair
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create()}
	end 'create'
end 'Pair'

function take(_ T, part Part) uses T returns Tally
	sleep(1)
	return part.n
end 'take'

function main() returns ExitCode
	let a = Pair.create()
	let pending = async take(1, part: a.part)
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:36: 'take' is spawned with `async`, and 'a' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: a-swap-through-a-two-source-call-result-holder-is-judged-once-the-call-settles -->
`either` may hand back `a` or `b`, so `swap` through its result is judged by what the call settles: both arguments are mutable locals, so the swap is accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export var part as isolated Box

	static function create() returns Self
		return Self{part: Box.create(0)}
	end 'create'
end 'Pair'

function either(a Pair, b Pair, flag bool) returns Pair
	return a if flag else b
end 'either'

function main() returns ExitCode
	let x = Pair.create()
	let y = Pair.create()
	let old = swap either(x, b: y, flag: true).part with Box.create(1)
	print("{old.n} {x.part.n}\n")
	return 0
end 'main'
```
```stdout
0 1
```
```exitcode
0
```

<!-- test: a-modify-through-a-two-source-call-result-holder-is-judged-once-the-call-settles -->
`modify either(x, b: y, flag: true).part` opens the isolated field of a call result whose sources are both mutable locals, which the settled call allows.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export var part as isolated Box

	static function create() returns Self
		return Self{part: Box.create(0)}
	end 'create'
end 'Pair'

function either(a Pair, b Pair, flag bool) returns Pair
	return a if flag else b
end 'either'

function main() returns ExitCode
	let x = Pair.create()
	let y = Pair.create()

	modify either(x, b: y, flag: true).part 'window'
		print("opened\n")
	end 'window'

	print("{x.part.n}\n")
	return 0
end 'main'
```
```stdout
opened
0
```
```exitcode
0
```

<!-- test: a-swap-through-a-two-source-call-result-holder-refuses-a-frozen-source -->
The same `swap` with a frozen second argument settles the call result to a mix of mutable and immutable data, and is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export var part as isolated Box

	static function create() returns Self
		return Self{part: Box.create(0)}
	end 'create'
end 'Pair'

function either(a Pair, b Pair, flag bool) returns Pair
	return a if flag else b
end 'either'

function main() returns ExitCode
	var x = Pair.create()
	let frozen = freeze Pair.create()
	let old = swap either(x, b: frozen, flag: true).part with Box.create(1)
	print("{old.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:27:12: cannot write 'part' through 'either(x, b: frozen, flag: true).part', which is immutable
error E3208: <fragment>:27:17: 'either' returns 'a' or 'b'; they must both be mutable or both immutable here
```

<!-- test: a-swap-through-a-call-result-holder-of-frozen-data-is-refused -->
`holder(frozen)` is the frozen local itself, so `swap` through its result is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	export var part as isolated Box

	static function create() returns Self
		return Self{part: Box.create(0)}
	end 'create'
end 'Pair'

function either(a Pair, b Pair, flag bool) returns Pair
	return a if flag else b
end 'either'

function holder(h Pair) returns Pair
	return h
end 'holder'

function main() returns ExitCode
	let frozen = freeze Pair.create()
	let old = swap holder(frozen).part with Box.create(1)
	print("{old.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:30:12: cannot write 'part' through 'holder(frozen).part', which is immutable
```
