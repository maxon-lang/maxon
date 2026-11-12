---
feature: returned-and-local-holders
keywords: [holder, record, literal, return, parameter, capability, iterator, frozen, read-only, M3]
category: semantics
---

# A Record Built From a Parameter's Data and Returned Does Not Pin the Parameter

## Documentation

A function that returns a record literal (`return Self{field: parameter}`) holds the parameter's data in the
record it returns. It does not write the parameter and does not need it mutable: the result follows the arguments
the record holds, so a frozen argument gives a read-only record and a mutable argument gives a writable one. A
function that only builds and returns such a record takes a frozen argument; a write through the result needs a
mutable one.

A function that builds a record from a parameter takes no read-only argument, which is data that an open `modify`
block pauses or a read-only result, because the record it builds is new data that would hold it.

The same holds wherever the builder hides. A function value, an interface requirement and a generic method that
calls a function value are judged as if they build a record from each unmarked argument they cannot see into
(E3207), a cycle of functions learns the fact from its members, and a closure that returns a record holding its
parameter refuses read-only data at its call. A wrapper that lends its parameter to such a callee is refused at
its own caller.

A closure that writes what it captured, or builds a record from it, is a `mutates captures` closure. It is accepted
under a `mutates captures` function type and called on mutable data, and it is refused under a plain function type
(E3210). A `mutates captures` function value cannot be called through read-only data: inside an open `modify` block
that holds the data it was returned from, was read out of or was stored in, and on frozen data anywhere (E3211).
A closure called inside the block that built or wrote data outside it is refused for the closure itself (E3207).

A record whose iterator holds the collection it walks, or a box the collection reads, does not write its receiver, so
the collection is `Iterable`. A holder built from a callee's result holds the arguments that callee held, so writing
through it writes them; a `clone()` of the parameter is a separate record and pins nothing.

## Tests

<!-- test: returned-holder-built-from-a-frozen-argument-is-read -->
`Holder.make` returns a record holding its parameter, so the frozen `Box` is accepted and `readSize` only reads through the record.

```maxon
type Box
	var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function size() returns Count
		return self.count
	end 'size'
end 'Box'

type Holder
	var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'

	function size() returns Count
		return box.size()
	end 'size'
end 'Holder'

function readSize(box Box) returns Count
	let h = Holder.make(box)
	return h.size()
end 'readSize'

function main() returns ExitCode
	let b = freeze Box.create()
	print("{readSize(b)}\n")
	return 0
end 'main'
```
```stdout
0
```

<!-- test: returned-holder-built-from-a-mutable-argument-writes-through -->
The same factory over a mutable `Box` hands back a record whose referent is writable, and the write is seen through the argument.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'
end 'Holder'

function main() returns ExitCode
	let b = Box.create()
	let h = Holder.make(b)
	h.box.count = 5
	print("{b.count}\n")
	return 0
end 'main'
```
```stdout
5
```

<!-- test: error.write-through-a-returned-holder-built-from-a-frozen-argument-is-refused -->
The record `make` returns holds the frozen `Box`, so writing the box through it is refused.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'
end 'Holder'

function main() returns ExitCode
	let b = freeze Box.create()
	let h = Holder.make(b)
	h.box.count = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:21:2: cannot write 'count' through 'h.box', which is read-only
```

<!-- test: error.a-returned-holder-whose-parameter-is-also-written-needs-a-mutable-argument -->
`reset` writes the box it also holds in the record it returns, so the frozen argument is refused.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function reset(box Box) returns Self
		box.count = 0
		return Self{box: box}
	end 'reset'
end 'Holder'

function main() returns ExitCode
	let b = freeze Box.create()
	let h = Holder.reset(b)
	return h.box.count as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:23: 'reset' writes 'box' (line 14: `box.count = 0`); 'b' is immutable
```

<!-- test: user-iterable-whose-iterator-is-built-from-the-collection-iterates -->
`ItemCursor.create` returns a record holding the array, so `ItemSeq.createIterator` does not write its receiver and the collection conforms to `Iterable`.

```maxon
type Item
	export var n = 0 as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

type ItemCursor implements Iterator with Item
	var items as ItemArray
	var pos = 0 as ElementIndex

	static function create(items ItemArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'

		return Self{items: items, pos: 0}
	end 'create'

	function current() returns Item
		return try self.items.get(self.pos) otherwise panic("oob")
	end 'current'

	function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atEnd'
			throw IterationError.exhausted
		end 'atEnd'

		self.pos = self.pos + 1
	end 'advance'
end 'ItemCursor'

type ItemSeq implements Iterable with (Item, ItemCursor)
	var items as ItemArray

	static function create(items ItemArray) returns Self
		return Self{items: items}
	end 'create'

	function createIterator() returns ItemCursor throws IterationError
		return try ItemCursor.create(self.items)
	end 'createIterator'
end 'ItemSeq'

function main() returns ExitCode
	let a = ItemArray.create(0)
	a.push(Item.create(10))
	a.push(Item.create(20))
	let s = ItemSeq.create(a)
	var sum = 0 as Count

	for v in s 'each'
		sum = sum + v.n
	end 'each'

	print("{sum}\n")
	return 0
end 'main'
```
```stdout
30
```

<!-- test: user-iterable-whose-iterator-holds-a-box-iterates -->
`LstIter.create` returns a record holding the `Box` its collection reads, so the collection conforms to `Iterable`.

```maxon
typealias StrArray = Array with String

type Box
	var data as StrArray

	static function create() returns Self
		return Self{data: StrArray.create(0)}
	end 'create'

	function add(text String)
		self.data.push(text)
	end 'add'

	function size() returns Count
		return self.data.count()
	end 'size'

	function at(index ElementIndex) returns String
		return try self.data.get(index) otherwise panic("bad")
	end 'at'
end 'Box'

type Lst implements Iterable with (String, LstIter)
	var box as Box

	static function create() returns Self
		return Self{box: Box.create()}
	end 'create'

	function add(text String)
		self.box.add(text)
	end 'add'

	function createIterator() returns LstIter throws IterationError
		return try LstIter.create(box)
	end 'createIterator'
end 'Lst'

type LstIter implements Iterator with String
	var owner as Box
	var pos = 0 as ElementIndex

	static function create(box Box) returns Self throws IterationError
		if box.size() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'

		return Self{owner: box, pos: 0}
	end 'create'

	function current() returns String
		return owner.at(pos)
	end 'current'

	function advance() throws IterationError
		if pos + 1 >= owner.size() 'last'
			throw IterationError.exhausted
		end 'last'

		pos = pos + 1
	end 'advance'
end 'LstIter'

function total(l Lst) returns Count
	var n = 0 as Count

	for s in l 'each'
		n = n + s.byteLength()
	end 'each'

	return n
end 'total'

function main() returns ExitCode
	let l = Lst.create()
	l.add("ab")
	l.add("cde")
	print("{total(l)}\n")
	return 0
end 'main'
```
```stdout
5
```

<!-- test: error.read-only-data-into-a-function-that-builds-a-record-from-it-is-refused -->
`Holder.make` builds a record that holds `box`, and `row` is outer data that `'fill'` pauses, so passing it inside the block is refused.

```maxon
typealias LineArray = Array with String

type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

type Holder
	export var row as Row

	static function make(row Row) returns Self
		return Self{row: row}
	end 'make'
end 'Holder'

function main() returns ExitCode
	let row = Row.create("a")
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let h = Holder.make(row)
		batch.push(h.row.text)
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:25:23: 'make' builds a record that holds 'row', so it takes no read-only data; 'row' is read-only
```

<!-- test: error.read-only-data-into-a-function-value-inside-modify-is-refused -->
Red team Q1: the oracle sees a record built inside the block holding outer data, a non-entry edge between regions. `Probe` is a function value whose body cannot be seen, so it may build a record from `row`, and `row` is read-only while `'fill'` is open.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

typealias Probe = function(Row) returns Count

function run(probe Probe)
	let row = Row.create(1)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = probe(row)
		batch.push("{n}")
	end 'fill'
end 'run'

function main() returns ExitCode
	run(function(row) gives Reader.over(row).size())
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:16: 'Probe' needs a parameter it may build a record from, since the function behind it is not visible here; 'row' is read-only while 'fill' is open
```

<!-- test: error.read-only-data-into-an-interface-requirement-inside-modify-is-refused -->
Red team Q1: the same through an interface requirement. `scan` is dispatched to a conformer that builds a record from its argument, and the call site cannot see it.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

interface Scanner
	function scan(row Row) returns Count
end 'Scanner'

type Scan implements Scanner
	var calls = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function scan(row Row) returns Count
		return Reader.over(row).size()
	end 'scan'
end 'Scan'

function run(scanner Scanner)
	let row = Row.create(1)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = scanner.scan(row)
		batch.push("{n}")
	end 'fill'
end 'run'

function main() returns ExitCode
	run(Scan.create())
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:45:19: 'Scanner.scan' needs a parameter it may build a record from, since the function behind it is not visible here; 'scanner' is read-only while 'fill' is open
```

<!-- test: error.a-ring-of-three-functions-building-a-record-refuses-read-only-data-inside-modify -->
Red team Q2: a record built three calls down a cycle. The fixpoint must learn that `f0`'s `row` builds one, so the call inside the block is refused.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

function f0(row Row, j Count) returns Count
	return f1(row, j: j)
end 'f0'

function f1(row Row, j Count) returns Count
	return f2(row, j: j)
end 'f1'

function f2(row Row, j Count) returns Count
	let reader = Reader.over(row)

	if j > 0 'again'
		return f0(row, j: 0)
	end 'again'

	return reader.size()
end 'f2'

function main() returns ExitCode
	let row = Row.create(7)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		batch.push("{f0(row, j: 0)}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:47:19: 'f0' builds a record that holds 'row', so it takes no read-only data; 'row' is read-only
```

<!-- test: error.a-closure-returning-a-record-built-from-its-parameter-refuses-read-only-data-inside-modify -->
Red team Q3: a closure returns a record holding its parameter. The result is reached from the parameter, so the call inside the block that hands it read-only data is refused.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

typealias Maker = function(Row) returns Reader

function run(make Maker)
	let row = Row.create(1)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let reader = make(row)
		batch.push("{reader.size()}")
	end 'fill'
end 'run'

function main() returns ExitCode
	run(function(row) gives Reader.over(row))
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:20: 'Maker' needs a parameter it may build a record from, since the function behind it is not visible here; 'row' is read-only while 'fill' is open
error E3210: <fragment>:37:6: function value for 'Maker' returns data reached from its read-only parameter 'row', which 'Maker' does not allow
```

<!-- test: error.a-closure-building-a-record-from-its-capture-is-refused-when-called-inside-modify -->
Red team R1: `g` builds a `Reader` from the `row` it captured. `row` is outer data that `'fill'` pauses, and a closure's captures are not arguments, so the call `g()` inside the block is refused for the closure itself.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

function main() returns ExitCode
	let row = Row.create(1)
	let g = function() gives Reader.over(row).size()
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = g()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:12: the function value builds a record from data it captured, and that data is read-only while 'fill' is open; build the record outside the closure or take the data as a parameter
```

<!-- test: error.a-closure-building-a-record-from-its-capture-is-refused-when-handed-to-a-callee-inside-modify -->
Red team R1: the same closure handed to `runIt`, which invokes it. The callee is judged by its parameter, which says nothing about the closure's captures, so the closure operand is judged for what it builds.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

typealias Probe = function() returns Count

function runIt(g Probe) returns Count
	return g()
end 'runIt'

function main() returns ExitCode
	let row = Row.create(1)
	let g = function() gives Reader.over(row).size()
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = runIt(g)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:32:10: function value for 'Probe' builds a record from data it captured, but 'Probe' does not declare `mutates captures`
error E3207: <fragment>:36:11: 'g' builds a record from data it captured, and that data is read-only while 'fill' is open; build the record outside the closure or take the data as a parameter
```

<!-- test: error.a-wrapper-lending-read-only-data-to-a-generic-method-that-calls-a-function-value-is-refused-inside-modify -->
Red team R2: `App.run` hands its `T` argument to a function value whose body is not visible, and `h` lends its parameter `p` to `run`. `x` is a `T` parameter of `App with Row`, whose element slot is writable, so `h` needs `p` writable and the read-only `row` is refused at the call to `h`.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

type App uses T
	typealias Step = function(T) returns Count

	var f as Step

	static function make(f Step) returns Self
		return Self{f: f}
	end 'make'

	function run(x T) returns Count
		return self.f(x)
	end 'run'
end 'App'

typealias RowApp = App with Row

function h(a RowApp, p Row) returns Count
	return a.run(p)
end 'h'

function main() returns ExitCode
	let row = Row.create(1)
	let a = RowApp.make(function(r Row) gives Reader.over(r).size())
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = h(a, p: row)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:50:16: 'h' needs a writable parameter 'p' (line 41: 'App.run' keeps a value read from it in a writable slot (its parameter 'x')); 'row' is read-only while 'fill' is open
```

<!-- test: error.a-wrapper-lending-read-only-data-to-a-generic-method-whose-parameter-is-not-a-type-parameter-is-refused-inside-modify -->
Red team R2: `App.run` takes a `Row`, not a `T`, so no element slot is involved. It hands the `Row` to a function value whose body is not visible, and `h` lends its parameter `p` to `run`. The function value may build a record from `p`, so `h` takes no read-only data and the read-only `row` is refused at the call to `h`.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

type App uses T
	typealias Step = function(Row) returns Count

	var f as Step
	var tag as T

	static function make(f Step, tag T) returns Self
		return Self{f: f, tag: tag}
	end 'make'

	function run(x Row) returns Count
		return self.f(x)
	end 'run'
end 'App'

typealias RowApp = App with Row

function h(a RowApp, p Row) returns Count
	return a.run(p)
end 'h'

function main() returns ExitCode
	let row = Row.create(1)
	let a = RowApp.make(function(r Row) gives Reader.over(r).size(), tag: Row.create(2))
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = h(a, p: row)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:51:16: 'h' builds a record that holds 'p', so it takes no read-only data; 'row' is read-only
```

<!-- test: error.a-generic-method-calling-a-function-value-with-its-own-item-refuses-a-paused-receiver-inside-modify -->
Red team R2: no `T` argument at all. `go` calls the function value with `self.item`, so the receiver `a` carries the data the closure builds from, and `h2` passes its paused receiver two wrappers down.

```maxon
typealias LineArray = Array with String

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

type App uses T
	typealias Step = function(T) returns Count

	var f as Step
	var item as T

	static function make(f Step, item T) returns Self
		return Self{f: f, item: item}
	end 'make'

	function go() returns Count
		return self.f(self.item)
	end 'go'
end 'App'

typealias RowApp = App with Row

function h(a RowApp) returns Count
	return a.go()
end 'h'

function h2(a RowApp) returns Count
	return h(a)
end 'h2'

function main() returns ExitCode
	let row = Row.create(1)
	let a = RowApp.make(function(r Row) gives Reader.over(r).size(), item: row)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = h2(a)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:55:14: 'h2' builds a record that holds 'a', so it takes no read-only data; 'a' is read-only
```

<!-- test: returned-holder-built-from-a-callee-result-holds-the-frozen-argument -->
`Outer.make` returns a record holding what `Inner.make` built from its parameter, so the frozen `Box` is accepted and `readSize` only reads through the result.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function size() returns Count
		return self.count
	end 'size'
end 'Box'

type Inner
	var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'

	function size() returns Count
		return box.size()
	end 'size'
end 'Inner'

type Outer
	var inner as Inner

	static function make(box Box) returns Self
		return Self{inner: Inner.make(box)}
	end 'make'

	function size() returns Count
		return inner.size()
	end 'size'
end 'Outer'

function readSize(box Box) returns Count
	let outer = Outer.make(box)
	return outer.size()
end 'readSize'

function main() returns ExitCode
	let b = freeze Box.create()
	print("{readSize(b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: error.write-through-a-returned-holder-built-from-a-callee-result-is-refused -->
The `Outer` holds the frozen `Box` through the `Inner` it was built from, so a method that writes the box through it is refused.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

type Inner
	var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'

	function bump()
		self.box.count = 5
	end 'bump'
end 'Inner'

type Outer
	var inner as Inner

	static function make(box Box) returns Self
		return Self{inner: Inner.make(box)}
	end 'make'

	function bump()
		self.inner.bump()
	end 'bump'
end 'Outer'

function main() returns ExitCode
	let frozen = freeze Box.create()
	var outer = Outer.make(frozen)
	outer.bump()
	print("{frozen.count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:2: 'bump' writes its receiver (it calls 'bump', which writes its receiver); 'outer' is read-only
```

<!-- test: error.a-local-holder-built-from-a-callee-result-pins-the-parameter-it-writes -->
A holder built from a callee result and written through in the same function writes the parameter, so the frozen argument is refused.

```maxon
type Box
	export var count = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

type Inner
	var box as Box

	static function make(box Box) returns Self
		return Self{box: box}
	end 'make'

	function bump()
		self.box.count = 5
	end 'bump'
end 'Inner'

type Outer
	var inner as Inner

	static function make(box Box) returns Self
		return Self{inner: Inner.make(box)}
	end 'make'

	function bump()
		self.inner.bump()
	end 'bump'
end 'Outer'

function localUse(box Box)
	var outer = Outer.make(box)
	outer.bump()
end 'localUse'

function main() returns ExitCode
	let frozen = freeze Box.create()
	localUse(frozen)
	print("{frozen.count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:41:11: 'localUse' writes 'box' (it calls 'bump', which calls 'bump', which writes its receiver); 'frozen' is immutable
```

<!-- test: a-clone-of-a-parameter-held-by-a-returned-record-is-not-the-parameter -->
`signature.clone()` is a copy of the parameter, so the record `record` builds holds its own data: writing it pins nothing and the frozen argument is accepted.

```maxon
typealias NameArray = Array with String

type Sig
	var names as NameArray

	static function create() returns Self
		return Self{names: NameArray.create(0)}
	end 'create'

	function add()
		self.names.push("x")
	end 'add'

	function size() returns Count
		return self.names.count()
	end 'size'
end 'Sig'

type Entry
	let tag as Count
	export var signature as Sig

	static function create(tag Count, signature Sig) returns Self
		return Self{tag: tag, signature: signature}
	end 'create'
end 'Entry'

function record(tag Count, signature Sig) returns Count
	let made = Entry.create(tag, signature: signature.clone())
	made.signature.add()
	return made.signature.size()
end 'record'

function main() returns ExitCode
	let frozen = freeze Sig.create()
	print("{record(1, signature: frozen)} {frozen.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 0
```

<!-- test: error.a-parameter-held-by-a-record-and-written-through-it-is-refused -->
Without the `clone()` the record holds the parameter itself, so writing through it writes the frozen argument.

```maxon
typealias NameArray = Array with String

type Sig
	var names as NameArray

	static function create() returns Self
		return Self{names: NameArray.create(0)}
	end 'create'

	function add()
		self.names.push("x")
	end 'add'

	function size() returns Count
		return self.names.count()
	end 'size'
end 'Sig'

type Entry
	let tag as Count
	export var signature as Sig

	static function create(tag Count, signature Sig) returns Self
		return Self{tag: tag, signature: signature}
	end 'create'
end 'Entry'

function record(tag Count, signature Sig) returns Count
	let made = Entry.create(tag, signature: signature)
	made.signature.add()
	return made.signature.size()
end 'record'

function main() returns ExitCode
	let frozen = freeze Sig.create()
	print("{record(1, signature: frozen)} {frozen.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:20: 'record' writes 'signature' (it calls 'add', which writes its receiver); 'frozen' is immutable
```


<!-- test: a-closure-building-a-record-from-its-capture-runs-on-mutable-data-outside-a-window -->
M3V: a closure that builds a record from data it captured is a `mutates captures` closure. Called on mutable data outside any window, held in a local and under a `mutates captures` field type, it is accepted.

```maxon
typealias Probe = function() returns Count mutates captures

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

type Holder
	export var probe as Probe

	static function over(probe Probe) returns Self
		return Self{probe: probe}
	end 'over'
end 'Holder'

function main() returns ExitCode
	let first = Row.create(3)
	let second = Row.create(4)
	let g = function() gives Reader.over(first).size()
	let holder = Holder.over(function() gives Reader.over(second).size())
	print("{g()} {holder.probe()}\n")
	return 0
end 'main'
```
```stdout
3 4
```

<!-- test: error.a-closure-building-a-record-from-its-capture-is-refused-under-a-plain-function-type -->
M3V: the same closure converted to a plain function type is refused where it is converted, outside any window. A plain function type promises no capture writes, and a closure that builds a record from its captures needs a mutable `self`.

```maxon
typealias Probe = function() returns Count

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

function runIt(g Probe) returns Count
	return g()
end 'runIt'

function main() returns ExitCode
	let row = Row.create(1)
	let g = function() gives Reader.over(row).size()
	print("{runIt(g)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3210: <fragment>:30:10: function value for 'Probe' builds a record from data it captured, but 'Probe' does not declare `mutates captures`
```

<!-- test: error.a-mutable-function-returned-from-a-function-is-refused-when-called-inside-modify -->
M3V: `makeProbe` returns the closure under a `mutates captures` type. The caller's `g` holds data from outside `'fill'`, which the block pauses, so calling it inside the block is refused.

```maxon
typealias LineArray = Array with String
typealias Probe = function() returns Count mutates captures

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

function makeProbe(row Row) returns Probe
	return function() gives Reader.over(row).size()
end 'makeProbe'

function main() returns ExitCode
	let row = Row.create(1)
	let g = makeProbe(row)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = g()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:35:12: 'Probe' mutates captures and cannot be called through read-only data while 'fill' is open
```

<!-- test: error.a-mutable-function-held-in-a-field-is-refused-when-called-inside-modify -->
M3V: the closure is held in a field of an outer record under a `mutates captures` type. The record is paused inside `'fill'`, so calling the function value read out of it is refused.

```maxon
typealias LineArray = Array with String
typealias Probe = function() returns Count mutates captures

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

type Holder
	export var probe as Probe

	static function over(probe Probe) returns Self
		return Self{probe: probe}
	end 'over'
end 'Holder'

function main() returns ExitCode
	let row = Row.create(1)
	let holder = Holder.over(function() gives Reader.over(row).size())
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = holder.probe()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:39:23: 'Probe' mutates captures and cannot be called through read-only data
```

<!-- test: error.a-mutable-function-held-in-an-array-is-refused-when-called-inside-modify -->
M3V: the closure is an element of an outer array whose element type is a `mutates captures` function type. The element read inside `'fill'` is paused data, so calling it is refused.

```maxon
typealias LineArray = Array with String
typealias Probe = function() returns Count mutates captures
typealias ProbeArray = Array with Probe

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Reader
	var row as Row

	static function over(row Row) returns Self
		return Self{row: row}
	end 'over'

	function size() returns Count
		return row.n
	end 'size'
end 'Reader'

function main() returns ExitCode
	let row = Row.create(1)
	let probes = ProbeArray.create(0)
	probes.push(function() gives Reader.over(row).size())
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let g = try probes.get(0) otherwise panic("no probe")
		let n = g()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:34:12: 'Probe' mutates captures and cannot be called through read-only data
```

<!-- test: error.a-mutable-function-held-in-a-frozen-holder-is-refused-when-called -->
M3V: a `mutates captures` function value read out of frozen data cannot be called, whatever the closure does.

```maxon
typealias Probe = function() returns Count mutates captures

type Holder
	export var probe as Probe

	static function over(probe Probe) returns Self
		return Self{probe: probe}
	end 'over'
end 'Holder'

function run(holder immutable Holder) returns Count
	return holder.probe()
end 'run'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:13:21: 'Probe' mutates captures and cannot be called through immutable data
```

<!-- test: error.a-closure-writing-its-capture-is-refused-when-called-inside-modify -->
M3V: a closure that writes data it captured needs a mutable `self` as well. `g` writes `counter`, which `'fill'` pauses, so the call inside the block is refused for the closure itself.

```maxon
typealias LineArray = Array with String

type Counter
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function bump(counter Counter) returns Count
	counter.n = counter.n + 1
	return counter.n
end 'bump'

function main() returns ExitCode
	let counter = Counter.create()
	let g = function() gives bump(counter)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = g()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:12: the function value writes data it captured, and that data is read-only while 'fill' is open; write it outside the closure or take the data as a parameter
```

<!-- test: a-closure-writing-its-capture-runs-under-a-mutates-captures-type-on-mutable-data -->
M3V: a closure that writes data it captured is inferred `mutates captures`, like one that builds a record from it. `drive` takes mutable data, so `runIt` calls the closure twice and the caller sees both writes.

```maxon
typealias Bump = function() returns Count mutates captures

type Counter
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function bump(counter Counter) returns Count
	counter.n = counter.n + 1
	return counter.n
end 'bump'

function runIt(g Bump) returns Count
	return g()
end 'runIt'

function drive(counter Counter) returns Count
	let g = function() gives bump(counter)
	let first = runIt(g)
	return first + runIt(g)
end 'drive'

function main() returns ExitCode
	let counter = Counter.create()
	print("{drive(counter)} {counter.n}\n")
	return 0
end 'main'
```
```stdout
3 2
```

<!-- test: error.a-closure-writing-its-capture-returned-under-a-mutates-captures-type-is-refused-when-called-inside-modify -->
M3V: `makeBump` returns the capture-writing closure under a `mutates captures` type. `g` holds data from outside `'fill'`, which the block pauses, so calling it inside the block is refused.

```maxon
typealias LineArray = Array with String
typealias Bump = function() returns Count mutates captures

type Counter
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Counter'

function bump(counter Counter) returns Count
	counter.n = counter.n + 1
	return counter.n
end 'bump'

function makeBump(counter Counter) returns Bump
	return function() gives bump(counter)
end 'makeBump'

function main() returns ExitCode
	let counter = Counter.create()
	let g = makeBump(counter)
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = g()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3211: <fragment>:28:12: 'Bump' mutates captures and cannot be called through read-only data while 'fill' is open
```
