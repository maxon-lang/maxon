---
feature: let-reassignment-only
status: experimental
keywords: [let, view, reassign, write through, alias, E2013, E3159, E3077, E3102, E3207]
category: semantics
---

# `let` forbids reassignment, not writing

## Documentation

A `let` is a name that is never reassigned. It does not freeze what the name holds: a field store, a mutating
method such as `push` and a callee that writes its parameter all reach through a `let` to the record or
container it names. `view` is another spelling of `let`.

What a `let` still refuses is rebinding. Assigning to it is **E2013**, and so is handing it to a `mutable`
parameter, because that parameter reassigns the caller's variable.

A `let` bound to a constant (a literal, a `b"…"` byte string, a constant array or record literal, or another
constant `let`, at module scope or not) is `immutable` image data, so writing through it is **E3207**, and so is
writing a `var` bound to such a literal. A `Map` key and a `Set` element are `immutable` slots too: their storage is
shared or hashed. A record whose fields are all `let` is an immutable aggregate only when every field is a value or
another immutable aggregate.

Binding a `var` from a `let` local moves the record, and reading the
`let` afterwards is **E3102**; a `var` bound from a field of a `let` names that field's record.

## Tests

<!-- test: a-let-field-array-grows-through-a-let-local -->
The plan's first sample: `columns` is a `let` field and `report` a `let` local, and both are written through.

```maxon
typealias Tally = int(0 to u64.max)
typealias Column = function(Counts) returns Tally
typealias ColumnArray = Array with Column

type Counts
	export var lines = 0 as Tally
	export var words = 0 as Tally

	static function create(lines Tally, words Tally) returns Self
		return Self{lines: lines, words: words}
	end 'create'
end 'Counts'

type Report
	let columns as ColumnArray

	static function create() returns Self
		return Self{columns: ColumnArray.create(0)}
	end 'create'

	function addColumn(column Column)
		self.columns.push(column)
	end 'addColumn'

	function render(counts Counts) returns String
		let row = StringBuilder.create()

		for column in self.columns 'eachColumn'
			row.append("[{column(counts)}]")
		end 'eachColumn'

		return row.build()
	end 'render'
end 'Report'

function main() returns ExitCode
	let report = Report.create()
	report.addColumn(function(counts Counts) gives counts.lines)
	report.addColumn(function(counts Counts) gives counts.words)
	print("{report.render(Counts.create(3, words: 12))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[3][12]
```

<!-- test: a-field-store-through-a-let-local -->
```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let p = Box.create(1)
	p.n = 2
	print("{p.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-let-array-is-pushed-set-and-read-in-loops -->
The `for` that reads `xs` holds an iterator over it; the writes before it are unaffected.

```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function main() returns ExitCode
	let xs = Counts.create(0)

	for i in 0 upto 3 'fill'
		xs.push(i)
	end 'fill'

	try xs.set(1, value: 5) otherwise return 1
	var total = 0 as Count

	for x in xs 'sum'
		total = total + x
	end 'sum'

	print("{xs.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 7
```

<!-- test: a-for-variable-over-a-let-array-is-written-through -->
```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let boxes = Boxes.create(0)
	boxes.push(Box.create(1))
	boxes.push(Box.create(2))

	for b in boxes 'bump'
		b.n = b.n + 10
	end 'bump'

	var total = 0 as Count

	for b in boxes 'sum'
		total = total + b.n
	end 'sum'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
23
```

<!-- test: a-match-binding-of-a-let-payload-is-written-through -->
```maxon
union Holder
	held(items ByteArray)
	empty
end 'Holder'

function grow(holder Holder) returns ExitCode
	match holder 'write'
		held(items) then items.push(1)
		empty then return 1
	end 'write'

	return 0
end 'grow'

function size(holder Holder) returns ExitCode
	return match holder 'size'
		held(items) gives items.count() as ExitCode
		empty gives 99
	end 'size'
end 'size'

function main() returns ExitCode
	let holder = Holder.held(ByteArray.create(0))
	let status = grow(holder)
	print("{status} {size(holder)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 1
```

<!-- test: a-var-bound-from-a-field-of-a-let-aliases-it -->
`c` names the record the `let` holder's field names, so the write through `c` is seen through `h`.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(5)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	var c = h.box
	c.n = 7
	print("{h.box.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-let-bound-from-a-field-of-a-var-sees-a-later-write -->
```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	let b = h.box
	h.box.n = 7
	print("{b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-let-alias-of-a-parameter-writes-the-callers-record -->
`a` names the record `p` names, so `retarget` writes its parameter through the alias.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function retarget(p Box)
	let a = p
	a.n = 3
end 'retarget'

function main() returns ExitCode
	var b = Box.create(1)
	retarget(b)
	print("{b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-frozen-argument-is-refused-where-the-parameter-is-written-through-a-let-alias -->
The write through the alias counts as a write of `p`, so a frozen record cannot be passed.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function retarget(p Box)
	let a = p
	a.n = 3
end 'retarget'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	retarget(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:11: 'retarget' writes 'p' (line 14: `a.n = 3`); 'frozen' is immutable
```

<!-- test: a-let-is-passed-to-a-parameter-the-callee-writes -->
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function fill(xs Counts)
	xs.push(1)
	xs.push(2)
end 'fill'

function main() returns ExitCode
	let xs = Counts.create(0)
	fill(xs)
	print("{xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-let-is-stored-into-let-and-var-fields-and-an-array-and-written-after -->
`a`, `b` and `c` are stored into a `let` field, a `var` field and an array, and each is read and written again after the store.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export let kept as Box
	export var spare as Box
	export var all as Boxes

	static function create(kept Box, spare Box) returns Self
		return Self{kept: kept, spare: spare, all: Boxes.create(0)}
	end 'create'

	function stock(item Box)
		self.all.push(item)
	end 'stock'
end 'Shelf'

function main() returns ExitCode
	let a = Box.create(1)
	let b = Box.create(2)
	let c = Box.create(3)
	let shelf = Shelf.create(a, spare: b)
	shelf.stock(c)
	a.n = 10
	b.n = 20
	c.n = 30
	let third = try shelf.all.get(0) otherwise return 1
	print("{shelf.kept.n} {shelf.spare.n} {third.n} {a.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 20 30 10
```

<!-- test: a-let-is-stored-into-an-array-literal -->
```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1)
	let b = Box.create(2)
	let pair = Boxes from [a, b]
	let first = try pair.get(0) otherwise return 1
	first.n = first.n + 1
	print("{first.n} {pair.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2
```

<!-- test: error.reassigning-a-let-local -->
Reassigning is what a `let` refuses; "immutable" would now say too much.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let report = Box.create(1)
	report = Box.create(2)
	print("{report.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:14:2: 'report' is a let and cannot be reassigned
```

<!-- test: error.reassigning-a-for-variable -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]

	for item in arr 'loop'
		item = 99
	end 'loop'

	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:6:3: 'item' is a let and cannot be reassigned
```

<!-- test: error.reassigning-a-tuple-element-of-a-let -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let x = 0
	let y = 0
	(x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```maxoncstderr
error E2013: <fragment>:11:3: 'x' is a let and cannot be reassigned
```

<!-- test: error.reassigning-a-let-field-through-self -->
```maxon
typealias Count = int(0 to 1000)

type Config
	let version as Count

	static function create(version Count) returns Self
		return Self{version: version}
	end 'create'

	function bump()
		self.version = 2
	end 'bump'
end 'Config'

function main() returns ExitCode
	var c = Config.create(1)
	c.bump()
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:12:8: 'version' is a let field and cannot be reassigned
```

<!-- test: error.reassigning-a-let-field-through-a-variable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
	export let version as Integer
	export var count as Integer

	static function create(version Integer, count Integer) returns Self
		return Self{version: version, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(1, count: 0)
	c.version = 2
	return c.version
end 'main'
```
```maxoncstderr
error E2013: <fragment>:15:2: 'version' is a let field and cannot be reassigned
```

<!-- test: error.a-let-is-passed-to-a-mutable-parameter -->
`setTo99` reassigns the caller's variable, and a `let` has none to give.

```maxon
typealias Integer = int(i64.min to i64.max)

function setTo99(x mutable Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	let n = 1 as Integer
	setTo99(n)
	return n
end 'main'
```
```maxoncstderr
error E2013: <fragment>:10:2: 'n' is a let and cannot be passed to the `mutable` parameter 'x'; bind it to a `var`
```

<!-- test: error.a-module-let-array-is-still-read-only -->
```maxon
let fixed = [4, 5, 6]

function main() returns ExitCode
	fixed.push(9)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:5:8: 'push' needs a writable receiver; 'fixed' is immutable
```

<!-- test: error.a-stored-map-key-is-still-read-only -->
```maxon
typealias Count = int(0 to 1000)
typealias Counted = Map with (String, Count)

function main() returns ExitCode
	var m = Counted.create()
	m.upsert("a", value: 1)

	for (k, _) in m 'each'
		k.append("x")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:5: cannot pass 'k', which is read-only (a `for` element of a read-only source), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: a-view-is-written-through-like-a-let -->
```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	view v = h.box
	v.n = 9
	print("{h.box.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: error.reassigning-a-view -->
```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	view v = Box.create(1)
	v = Box.create(2)
	print("{v.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:14:2: 'v' is a let and cannot be reassigned
```

<!-- test: a-frozen-let-is-sent-to-a-service-twice -->
Sharing a frozen value rests on `freeze`, not on the `let` that names it.

```maxon
typealias Count = int(0 to 1000)

type Cfg
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Cfg'

type Sink
	var total as Count

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(cfg immutable Cfg) returns Count
		self.total = self.total + cfg.n
		return self.total
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let cfg = freeze Cfg.create(3)
	let first = try await sink.take(cfg) otherwise 0
	let second = try await sink.take(cfg) otherwise 0
	sink.shutdown()
	print("{first} {second} {cfg.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 6 3
```

<!-- test: a-let-bound-from-a-field-keeps-its-record-when-the-holder-is-reassigned -->
```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(n Count) returns Self
		return Self{box: Box.create(n)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(1)
	let x = h.box
	h = Holder.create(2)
	print("{x.n} {h.box.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: error.an-all-let-record-holding-an-array-is-not-immutable -->
Every field of `Bag` is a `let`, but `items` is an array: the record is not frozen data, so it is not sent as `immutable` without `freeze`.

```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Bag
	let items as Counts

	static function create() returns Self
		return Self{items: Counts.create(0)}
	end 'create'
end 'Bag'

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(bag immutable Bag) returns Count
		return 0
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let bag = Bag.create()
	let n = try await sink.take(bag) otherwise 1
	sink.shutdown()
	return n
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:30: 'take' takes `immutable` 'bag'; 'bag' is mutable. Freeze it first
```

<!-- test: an-all-let-record-of-values-is-still-immutable -->
```maxon
typealias Count = int(0 to 1000)

type Point
	export let x as Count
	export let y as Count

	static function create(x Count, y Count) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Sink
	static function create() returns Self
		return Self{}
	end 'create'

	export function take(point immutable Point) returns Count
		return point.x + point.y
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let point = Point.create(1, y: 2)
	let n = try await sink.take(point) otherwise 0
	sink.shutdown()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: error.a-var-never-reassigned-that-a-let-could-not-bind-before -->
`node` names state `h` still writes. Nothing is written through `node` itself, so it is a `let`.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create() returns Self
		return Self{box: Box.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	var node = h.box
	h.box.n = 4
	print("{node.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3077: <fragment>:22:6: variable 'node' is never reassigned; use 'let' instead of 'var'
```

<!-- test: error.a-var-bound-from-a-let-moves-it -->
`var b = a` moves the record out of the `let` `a`, so reading `a` afterwards is refused.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create()
	var b = a
	b.n = 5
	print("{a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```
