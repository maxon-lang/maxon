---
feature: regions-inferred
status: experimental
keywords: [capability, inferred, parameter, receiver, immutable self, may yield, may wait, modify]
category: semantics
---

# Inferred parameter capabilities and the may-wait and may-yield facts

## Documentation

An unmarked parameter, or an omitted receiver, takes one of three classes from how the body uses it: any
capability when the body only reads it, mutable only when it writes through it, stores it or passes it to a
mutable-only parameter, and immutable only when it sends it or passes it to an `immutable` parameter. A body
that needs both is refused. A result reached from a parameter takes that argument's capability when every
return path fits it. An argument whose capability the class refuses is refused at the call, naming the use.

Two more facts are inferred per function, with no marker: whether it may wait on another party (a message
reply, an `acquire`), and whether it may yield (it awaits a coroutine, or calls something that may yield or
wait). A call to either is refused inside a `modify` block, because another coroutine could run while the
region is open.

## Tests

<!-- test: read-only-param-takes-mutable-and-frozen -->
`describe` only reads its table and takes a mutable one and a frozen one; `stamp` writes its table and takes the mutable one.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

function describe(table Table) returns String
	return "{table.count()} rows"
end 'describe'

function stamp(table Table)
	table.add(9)
end 'stamp'

function main() returns ExitCode
	var live = Table.create()
	live.add(1)
	let frozen = freeze Table.create()
	print("{describe(live)} {describe(frozen)}\n")
	stamp(live)
	print("{live.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 rows 0 rows
2
```

<!-- test: writer-on-frozen-receiver-refused -->
`add` writes its receiver, which the call site's frozen table cannot allow.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

function main() returns ExitCode
	var frozen = freeze Table.create()
	frozen.add(1)
	print("{frozen.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:2: 'add' writes its receiver (line 17: `self.rows.push(n)`); 'frozen' is immutable
```

<!-- test: transitive-writer-refused -->
`stamp` writes `table` only by calling `add`, and the refusal still names the chain.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

function stamp(table Table)
	table.add(9)
end 'stamp'

function main() returns ExitCode
	var frozen = freeze Table.create()
	stamp(frozen)
	print("{frozen.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:8: 'stamp' writes 'table' (it calls 'add', which writes its receiver); 'frozen' is immutable
```

<!-- test: immutable-only-param-given-mutable -->
`relay` sends `t` to a service whose message takes `immutable Table`, so `t` is immutable only and a mutable argument is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

type Auditor
	static function create() returns Self
		return Self{}
	end 'create'

	export function check(table immutable Table)
		print("audited {table.count()} rows\n")
	end 'check'
end 'Auditor'

function relay(t Table, auditor Auditor.handle)
	try auditor.check(t) otherwise ignore
end 'relay'

function main() returns ExitCode
	let a = spawn Auditor.create()
	var live = Table.create()
	live.add(1)
	relay(live, auditor: a)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:39:8: 'relay' needs 't' frozen (line 32: `try auditor.check(t)`); 'live' is mutable. Freeze it first
```

<!-- test: param-needed-both-refused -->
`both` writes `t` and also hands it to a parameter declared `immutable`, so no single capability fits the parameter.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Table
	var rows as TallyArray

	static function create() returns Self
		return Self{rows: TallyArray.create(0)}
	end 'create'

	function count() returns Count
		return self.rows.count()
	end 'count'

	function add(n Tally)
		self.rows.push(n)
	end 'add'
end 'Table'

function audit(table immutable Table)
	print("audited {table.count()} rows\n")
end 'audit'

function both(t Table)
	t.add(1)
	audit(t)
end 'both'

function main() returns ExitCode
	let frozen = freeze Table.create()
	audit(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3206: <fragment>:25:17: parameter 't' is used as mutable (it calls 'add', which writes its receiver) and as immutable at line 27; a parameter cannot be both
```

<!-- test: pick-classes-disagree -->
`pick` returns `a` or `b`, so a call must give both the same capability, and `mine` is mutable while `frozen` is not.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

function pick(a Row, b Row, first bool) returns Row
	return a if first else b
end 'pick'

function main() returns ExitCode
	var mine = Row.create("m")
	mine.text = "n"
	let frozen = freeze Row.create("f")
	let chosen = pick(mine, b: frozen, first: true)
	print("{chosen.text}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:18:15: 'pick' returns 'a' or 'b'; they must both be mutable or both immutable here
```

<!-- test: result-path-disagrees -->
The declared result is `immutable Row`; the first return gives a fresh mutable row, so that path does not fit it.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

function settle(text String, fresh bool) returns immutable Row
	if fresh 'fresh'
		return Row.create(text)
	end 'fresh'

	return freeze Row.create("none")
end 'settle'

function main() returns ExitCode
	let settled = settle("a", fresh: false)
	print("{settled.text}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:12:10: a return does not fit the declared result: 'Row.create(text)' is mutable, the result is immutable; freeze it
```

<!-- test: immutable-self-on-mutable-refused -->
`reported` is declared `immutable self`, so it exists only on frozen values and `live` is mutable.

```maxon
typealias Tally = int(0 to u64.max)

type Scale
	var reading as Tally

	static function create(reading Tally) returns Self
		return Self{reading: reading}
	end 'create'

	function bump(by Tally)
		self.reading = self.reading + by
	end 'bump'

	function reported(immutable self) returns Tally
		return self.reading
	end 'reported'
end 'Scale'

function main() returns ExitCode
	var live = Scale.create(4)
	live.bump(1)
	let n = live.reported()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:10: 'reported' is declared `immutable self`; 'live' is mutable. Freeze it first
```

<!-- test: may-yield-call-inside-modify-refused -->
`settle` awaits a coroutine, so it may yield, and a yield inside the block could run a sibling that reaches `batch`.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

function work() returns Tally
	sleep(1)
	return 5
end 'work'

function settle() returns Tally
	let pending = async work()
	return await pending
end 'settle'

function main() returns ExitCode
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = settle()
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:19:11: cannot call 'settle' inside 'fill'; it may yield (it awaits a coroutine at line 12). Call it before or after the block
```

<!-- test: may-wait-call-inside-modify-refused -->
`ask` awaits a message reply, so it may wait on another party, which no call inside the block may do.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

type Oracle
	static function create() returns Self
		return Self{}
	end 'create'

	export function answer() returns Tally
		return 42
	end 'answer'
end 'Oracle'

function ask(oracle Oracle.handle) returns Tally
	return try await oracle.answer() otherwise 0
end 'ask'

function main() returns ExitCode
	let oracle = spawn Oracle.create()
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = ask(oracle)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:24:11: cannot call 'ask' inside 'fill'; it may wait (it awaits a reply from 'Oracle' at line 16). Call it before or after the block
```

<!-- test: result-of-a-call-returning-its-parameter-keeps-the-argument-capability -->
`same` returns its parameter, so `c` is the frozen `b` and the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function same(b Box) returns Box
	return b
end 'same'

function poke(b immutable Box)
	var c = same(b)
	c.n = 5
end 'poke'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	poke(pinned)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:2: cannot write 'n' through 'c', which is immutable
```

<!-- test: element-read-of-a-frozen-array-is-frozen -->
`rows.get(0)` hands back an element of the frozen array, so `first` is frozen and the write through it is refused.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

typealias RowArray = Array with Row

function rewrite(rows immutable RowArray)
	var first = try rows.get(0) otherwise panic("rewrite: called with a row")
	first.text = "x"
end 'rewrite'

function main() returns ExitCode
	var frozen = freeze RowArray.create(0)
	rewrite(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:14:2: cannot write 'text' through 'first', which is immutable
```

<!-- test: writer-through-an-element-read-needs-a-mutable-argument -->
`rewrite` writes the element it read from `rows`, so `rows` is mutable only and the frozen array is refused at the call.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

typealias RowArray = Array with Row

function rewrite(rows RowArray)
	var first = try rows.get(0) otherwise panic("rewrite: called with a row")
	first.text = "x"
end 'rewrite'

function main() returns ExitCode
	var frozen = freeze RowArray.create(0)
	rewrite(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:10: 'rewrite' writes 'rows'; 'frozen' is immutable
```

<!-- test: writer-through-a-returned-parameter-needs-a-mutable-argument -->
`edit` writes the record `same` handed back, which is its own parameter `b`, so `b` is mutable only and the frozen box is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function same(b Box) returns Box
	return b
end 'same'

function edit(b Box)
	var c = same(b)
	c.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:7: 'edit' writes 'b'; 'pinned' is immutable
```

<!-- test: call-that-only-spawns-a-waiting-factory-reports-the-chain -->
`start` waits only through the factory it spawns, and no call that may wait runs inside the block.

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

function start() returns Relay.handle
	return spawn Relay.create()
end 'start'

function main() returns ExitCode
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		_ = start()
		batch.push("started")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:37:7: cannot call 'start' inside 'fill'; it may wait (it spawns 'create', which it awaits a reply from 'Oracle' at line 20). Call it before or after the block
```

<!-- test: write-through-a-returned-parameter-of-a-frozen-local-is-refused -->
`same` returns its parameter, so `c` is the frozen box `pinned` and the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function same(b Box) returns Box
	return b
end 'same'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var c = same(pinned)
	c.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:2: cannot write 'n' through 'c', which is immutable
```

<!-- test: write-through-an-element-of-a-frozen-local-is-refused -->
`frozen.get(0)` hands back an element of the frozen array, so `first` is frozen and the write through it is refused.

```maxon
type Row
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Row'

typealias RowArray = Array with Row

function main() returns ExitCode
	var frozen = freeze RowArray.create(0)
	var first = try frozen.get(0) otherwise panic("main: the array is empty")
	first.text = "x"
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:15:2: cannot write 'text' through 'first', which is immutable
```

<!-- test: try-otherwise-joining-an-element-of-a-frozen-array-with-a-fresh-value-is-read-only -->
`frozen.get(0)` is data of a frozen array and the `otherwise` value is a fresh mutable box, so the merge is read-only and the write through it is refused.

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
	var frozen = freeze BoxArray.create(0)
	var e = try frozen.get(0) otherwise Box.create(0)
	e.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:16:2: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: ternary-joining-frozen-and-fresh-data-is-read-only -->
`pinned if flag else Box.create(0)` joins frozen data with a fresh mutable box, so the merge is read-only and the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, flag bool)
	var e = pinned if flag else Box.create(0)
	e.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:14:2: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: match-joining-frozen-and-fresh-data-is-read-only -->
A `match` giving frozen data in one arm and a fresh mutable box in another is read-only afterwards, and the write through its result is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, k Tally)
	var e = match k 'pick'
		0 gives pinned
		default gives Box.create(0)
	end 'pick'

	e.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, k: 0)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:18:2: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: ternary-joining-two-frozen-values-stays-immutable -->
Both arms of the ternary are frozen, so the merge is frozen and the write through it is refused as a write through immutable data.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(first immutable Box, second immutable Box, flag bool)
	var e = first if flag else second
	e.n = 5
end 'edit'

function main() returns ExitCode
	var one = freeze Box.create(1)
	var two = freeze Box.create(2)
	edit(one, second: two, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:14:2: cannot write 'n' through 'e', which is immutable
```

<!-- test: var-assigned-frozen-data-in-one-branch-is-read-only-after-the-merge -->
`e` holds a fresh box and one branch assigns it frozen data, so after the merge `e` is read-only and the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, flag bool)
	var e = Box.create(0)

	if flag 'branch'
		e = pinned
	end 'branch'

	e.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:2: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: var-reassigned-frozen-data-is-immutable -->
`e = pinned` rebinds `e` to frozen data, so the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box)
	var e = Box.create(0)
	e = pinned
	e.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:15:2: cannot write 'n' through 'e', which is immutable
```

<!-- test: ternary-joining-isolated-data-with-mutable-data-is-refused -->
`a` is isolated and `b` is mutable data of this region, so a ternary over them has no one capability.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, flag bool) returns Tally
	let b = Box.create(2)
	let c = a if flag else b
	return c.n
end 'pick'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	print("{pick(x, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:14:12: a join of an isolated value with data that is not isolated is refused: every arm must be isolated, or a fresh value
```

<!-- test: ternary-joining-two-isolated-values-is-isolated -->
Both arms are isolated, so the ternary is isolated and its count is read through a one-statement open.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, b isolated Box, flag bool) returns Tally
	let c = a if flag else b
	return c.n
end 'pick'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	let y = Box.create(2) as isolated Box
	print("{pick(x, b: y, flag: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: ternary-over-two-isolated-locals-yields-one-isolated-result -->
`a` and `b` are isolated locals, so the ternary is isolated and `c.n` is read through a one-statement open; the arm not taken is released with the function.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	let a = Box.create(1) as isolated Box
	let b = Box.create(2) as isolated Box
	let c = a if flag else b
	return c.n
end 'pick'

function main() returns ExitCode
	print("{pick(true)} {pick(false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: ternary-over-isolated-locals-in-a-loop-moves-the-selected-one-and-leaks-nothing -->
Inside a loop the ternary moves its isolated local into the result on every trip, taking the arm it selects, and the program ends with no allocation held.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	let a = Box.create(1) as isolated Box
	let c = a if flag else Box.create(2)
	return c.n
end 'pick'

function loopy() returns Tally
	var total = 0 as Tally

	for round in 0 upto 3 'each'
		let a = Box.create(round) as isolated Box
		let b = Box.create(10) as isolated Box
		let c = a if round == 1 else b
		total = total + c.n
	end 'each'

	return total
end 'loopy'

function main() returns ExitCode
	print("{pick(true)} {pick(false)} {loopy()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 21
```

<!-- test: ternary-over-two-isolated-locals-consumes-the-true-arm -->
`let c = a if flag else b` moves both isolated locals into the result, exactly as `let c = a` moves `a`, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	let a = Box.create(1) as isolated Box
	let b = Box.create(2) as isolated Box
	let c = a if flag else b
	let again = a.n
	return c.n + again
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: ternary-over-two-isolated-locals-consumes-the-false-arm -->
The arm that is not selected is moved as well, so the read of `b` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	let a = Box.create(1) as isolated Box
	let b = Box.create(2) as isolated Box
	let c = a if flag else b
	let again = b.n
	return c.n + again
end 'pick'

function main() returns ExitCode
	print("{pick(false)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:14: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: match-over-two-isolated-locals-consumes-both -->
A `match` expression whose arms give isolated locals moves each of them into the result, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	let a = Box.create(1) as isolated Box
	let b = Box.create(2) as isolated Box

	let c = match flag 'which'
		true gives a
		default gives b
	end 'which'

	let again = a.n
	return c.n + again
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: loop-body-write-through-a-variable-the-back-edge-assigns-frozen-data -->
The first trip writes through the fresh box, but `e = pinned` hands the next trip frozen data, so at the loop header `e` joins immutable and mutable data and the write through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		e.n = 1
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:17:3: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: for-body-write-through-a-variable-the-back-edge-assigns-frozen-data -->
The same refusal in a `for` loop: the counter advances through the step block, and the back edge still carries `e = pinned` to the header.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)

	for round in 0 upto rounds 'each'
		e.n = round
		e = pinned
	end 'each'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:16:3: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: loop-write-through-a-field-path-of-a-variable-the-back-edge-assigns-frozen-data -->
The write goes through `h.box`, a field read of the carried `h`, so it is judged against the header join of `h` and not the fresh holder the first trip sees.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(n Tally) returns Self
		return Self{box: Box.create(n)}
	end 'create'
end 'Holder'

function edit(pinned immutable Holder, rounds Tally)
	var h = Holder.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		h.box.n = 1
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
error E3197: <fragment>:25:3: cannot write 'n' through 'h.box', which is read-only, as it joins immutable and mutable data
```

<!-- test: nested-loop-write-through-a-variable-the-outer-back-edge-assigns-frozen-data -->
The inner loop assigns nothing, but the outer loop's back edge carries `e = pinned` to the outer header, which the inner header inherits.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'outer'
		var j = 0 as Tally

		while j < rounds 'inner'
			e.n = 1
			j = j + 1
		end 'inner'

		e = pinned
		i = i + 1
	end 'outer'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:20:4: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: loop-body-merge-of-a-variable-the-back-edge-assigns-frozen-data -->
`e` is replaced by a fresh box on the first trip only, so after the `if` it joins that box with the carried `e`, which the back edge makes read-only.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		if i == 0 'first'
			e = Box.create(5)
		end 'first'

		e.n = 1
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:21:3: cannot write 'n' through 'e', which is read-only, as it joins immutable and mutable data
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data -->
`h.box = e` stores the carried `e` into a mutable field, and the next trip stores frozen data there, so the store is judged against the header join of `e`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function create(n Tally) returns Self
		return Self{box: Box.create(n)}
	end 'create'
end 'Holder'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var h = Holder.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		h.box = e
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:26:11: cannot store a read-only 'Box' into 'box', as it joins immutable and mutable data; store a copy
```

<!-- test: loop-call-of-a-writer-on-a-variable-the-back-edge-assigns-frozen-data -->
`e.bump()` writes its receiver, and the next trip's receiver is the frozen box, so the call is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		e.bump()
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:3: 'bump' writes its receiver (line 12: `self.n = self.n + 1`); 'e' is read-only
```

<!-- test: loop-write-through-a-variable-reassigned-fresh-data-stays-writable -->
`e` is assigned a fresh box on every back edge, so the header joins mutable with mutable and the write through it is accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	var e = Box.create(0)
	var i = 0 as Tally

	while i < 3 'again'
		e.n = e.n + i
		e = Box.create(e.n)
		i = i + 1
	end 'again'

	print("{e.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: loop-read-of-a-variable-the-back-edge-assigns-frozen-data-runs -->
The body only reads `e` before `e = pinned` hands the next trip frozen data, which a read-only join allows.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function total(pinned immutable Box, rounds Tally) returns Tally
	var e = Box.create(5)
	var sum = 0 as Tally
	var i = 0 as Tally

	while i < rounds 'again'
		sum = sum + e.n
		e = pinned
		i = i + 1
	end 'again'

	return sum
end 'total'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	print("{total(pinned, rounds: 3)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: isolated-local-passed-to-an-isolated-parameter-is-moved -->
`consume` declares `box` isolated, so the call takes the region from `a` and the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(box isolated Box) returns Tally
	return box.n
end 'consume'

function passed() returns Tally
	let a = Box.create(1) as isolated Box
	let taken = consume(a)
	let again = a.n
	return taken + again
end 'passed'

function main() returns ExitCode
	print("{passed()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-parameter-owns-its-region-and-drops-it-once -->
The callee owns an `isolated` parameter and releases it exactly once: an isolated local, a fresh call result, a forwarded parameter and a returned parameter each reach one owner, so the program ends with no allocation held.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(box isolated Box) returns Tally
	return box.n
end 'consume'

function forward(box isolated Box) returns Tally
	return consume(box)
end 'forward'

function identity(box isolated Box) returns isolated Box
	return box
end 'identity'

function main() returns ExitCode
	let first = Box.create(1) as isolated Box
	let second = Box.create(10) as isolated Box
	let third = Box.create(100) as isolated Box
	let handed = identity(third)
	let total = consume(first) + forward(second) + consume(Box.create(1000)) + forward(handed)
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1111
```

<!-- test: isolated-parameter-never-read-is-still-dropped-once -->
A parameter named `_` is never read, yet the callee still owns the region it was handed and releases it, so nothing leaks.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function discard(_ isolated Box) returns Tally
	return 7
end 'discard'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	print("{discard(a)} {discard(Box.create(2))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 7
```

<!-- test: mutable-isolated-parameter-leaves-the-callers-variable-usable -->
A `mutable isolated` parameter is by reference and is re-isolated after the call, so the caller keeps its region and reads it after both calls.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(box mutable isolated Box)
	modify box 'edit'
		box.n = box.n + 10
	end 'edit'
end 'bump'

function main() returns ExitCode
	var a = Box.create(1) as isolated Box
	bump(a)
	bump(a)
	print("{a.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21
```

<!-- test: isolated-local-passed-twice-in-one-call-is-refused -->
The first position takes the region out of `a`, so naming `a` again in the same call is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function both(first isolated Box, second isolated Box) returns Tally
	return first.n + second.n
end 'both'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	print("{both(a, second: a)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:18: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-local-stored-by-a-factory-parameter-is-moved -->
`Holder.create` keeps its isolated parameter in a field, so the call takes the region from `a`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var item as isolated Box

	static function create(item isolated Box) returns Self
		return Self{item: item}
	end 'create'

	function size() returns Tally
		return self.item.n
	end 'size'
end 'Holder'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	let holder = Holder.create(a)
	let again = a.n
	print("{holder.size()} {again}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-local-stored-by-a-factory-parameter-runs -->
The holder owns the region it was handed and releases it with the holder, once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var item as isolated Box

	static function create(item isolated Box) returns Self
		return Self{item: item}
	end 'create'

	function size() returns Tally
		return self.item.n
	end 'size'
end 'Holder'

function main() returns ExitCode
	let a = Box.create(3) as isolated Box
	let holder = Holder.create(a)
	let other = Holder.create(Box.create(4))
	print("{holder.size()} {other.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4
```

<!-- test: isolated-local-passed-through-a-function-type-is-moved -->
`Take` declares its parameter isolated, so calling it with `a` takes the region from `a` whichever function it names.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Take = function(isolated Box) returns Tally

function consume(box isolated Box) returns Tally
	return box.n
end 'consume'

function feed(take Take) returns Tally
	var a = Box.create(4) as isolated Box
	let taken = take(a)
	let again = a.n
	return taken + again
end 'feed'

function main() returns ExitCode
	print("{feed(consume)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-argument-through-a-function-type-is-dropped-once -->
Whichever declared function the function value names takes the isolated region it is called with, from a local and from a fresh call result alike, and releases it once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Take = function(isolated Box) returns Tally

function consume(box isolated Box) returns Tally
	return box.n
end 'consume'

function bumped(box isolated Box) returns Tally
	return box.n + 1
end 'bumped'

function feed(take Take, n Tally) returns Tally
	var a = Box.create(n) as isolated Box
	return take(a) + take(Box.create(n + 1))
end 'feed'

function main() returns ExitCode
	print("{feed(consume, n: 4)} {feed(bumped, n: 10)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9 23
```

<!-- test: let-of-an-isolated-parameter-moves-it -->
An `isolated` parameter is owned by the function, so `let b = a` hands its region to `b` and the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function moveOnly(a isolated Box) returns Tally
	let b = a
	let reread = a.n
	return b.n + reread
end 'moveOnly'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	print("{moveOnly(x)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:15: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: freeze-of-an-isolated-parameter-moves-it -->
`freeze a` consumes the parameter exactly as it consumes a local, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function useParam(a isolated Box) returns Tally
	let frozen = freeze a
	let reread = a.n
	return frozen.n + reread
end 'useParam'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	print("{useParam(x)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:15: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: ternary-over-two-isolated-parameters-consumes-the-false-arm -->
Both parameters are owned isolated regions, so the ternary moves each of them into its result and the read of `b` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, b isolated Box, flag bool) returns Tally
	let c = a if flag else b
	let again = b.n
	return c.n + again
end 'pick'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	let y = Box.create(2) as isolated Box
	print("{pick(x, b: y, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:14: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: match-over-two-isolated-parameters-consumes-both -->
A `match` expression whose arms give isolated parameters moves each of them into the result, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, b isolated Box, flag bool) returns Tally
	let c = match flag 'which'
		true gives a
		default gives b
	end 'which'

	let again = a.n
	return c.n + again
end 'pick'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	let y = Box.create(2) as isolated Box
	print("{pick(x, b: y, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-parameter-moved-in-one-branch-is-live-in-its-sibling -->
The branch that hands the parameter to `b` moves it and the other branch still reads it, so neither branch is refused and each releases the region once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function route(a isolated Box, flag bool) returns Tally
	let seen = a.n

	if flag 'moving'
		let b = a
		return b.n + seen
	end 'moving'

	return a.n + seen
end 'route'

function settle(a isolated Box, flag bool) returns Tally
	var total = 0 as Tally

	if flag 'moving'
		let b = a
		total = b.n
	end 'moving' else 'keeping'
		total = a.n + 100
	end 'keeping'

	return total
end 'settle'

function main() returns ExitCode
	print("{route(Box.create(1), flag: true)} {route(Box.create(2), flag: false)}\n")
	print("{settle(Box.create(3), flag: true)} {settle(Box.create(4), flag: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 4
3 104
```

<!-- test: try-otherwise-an-isolated-local-moves-it -->
The `otherwise` fallback names `spare`, an isolated local, so the result takes the region from `spare` and the read of `spare` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum BuildError implements Error
	refused
end 'BuildError'

function build(n Tally) returns Box throws BuildError
	if n == 0 'refuse'
		throw BuildError.refused
	end 'refuse'

	return Box.create(n)
end 'build'

function pick(n Tally) returns Tally
	let spare = Box.create(7) as isolated Box
	let chosen = try build(n) otherwise spare
	let again = spare.n
	return chosen.n + again
end 'pick'

function main() returns ExitCode
	print("{pick(0)} {pick(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:14: use of moved value 'spare': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: try-otherwise-an-isolated-local-runs-and-leaks-nothing -->
The fallback region is moved into the result when the call fails and released with the function when it succeeds, so both paths end with no allocation held.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum BuildError implements Error
	refused
end 'BuildError'

function build(n Tally) returns Box throws BuildError
	if n == 0 'refuse'
		throw BuildError.refused
	end 'refuse'

	return Box.create(n)
end 'build'

function pick(n Tally) returns Tally
	let spare = Box.create(7) as isolated Box
	let chosen = try build(n) otherwise spare
	return chosen.n
end 'pick'

function main() returns ExitCode
	print("{pick(0)} {pick(2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 2
```

<!-- test: loop-merge-of-an-isolated-variable-the-back-edge-assigns-data -->
The first trip merges the isolated region out of `e`, but `e = fresh` assigns mutable data of this region to a variable that holds an isolated region, which is refused where it is assigned.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function size(box Box) returns Tally
	return box.n
end 'size'

function drain(rounds Tally) returns Tally
	var e = Box.create(0) as isolated Box
	var sum = 0 as Tally
	var i = 0 as Tally

	while i < rounds 'again'
		sum = sum + size(merge e)
		var fresh = Box.create(i)
		e = fresh
		i = i + 1
	end 'again'

	return sum
end 'drain'

function main() returns ExitCode
	print("{drain(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:24:7: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: loop-freeze-of-an-isolated-variable-the-back-edge-assigns-data -->
`freeze e` needs an isolated region on every trip, but `e = fresh` assigns mutable data of this region to a variable that holds an isolated region, which is refused where it is assigned.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function read(box immutable Box) returns Tally
	return box.n
end 'read'

function drain(rounds Tally) returns Tally
	var e = Box.create(0) as isolated Box
	var sum = 0 as Tally
	var i = 0 as Tally

	while i < rounds 'again'
		sum = sum + read(freeze e)
		var fresh = Box.create(i)
		e = fresh
		i = i + 1
	end 'again'

	return sum
end 'drain'

function main() returns ExitCode
	print("{drain(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:24:7: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: loop-modify-of-an-isolated-variable-the-back-edge-assigns-data -->
`modify e` opens an isolated region on every trip, but `e = fresh` assigns mutable data of this region to a variable that holds an isolated region, which is refused where it is assigned.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function drain(rounds Tally) returns Tally
	var e = Box.create(0) as isolated Box
	var i = 0 as Tally

	while i < rounds 'again'
		modify e 'edit'
			e.n = e.n + 1
		end 'edit'

		var fresh = Box.create(i)
		e = fresh
		i = i + 1
	end 'again'

	return i
end 'drain'

function main() returns ExitCode
	print("{drain(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:22:7: 'fresh' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: branch-assigning-data-to-an-isolated-variable-is-refused-at-the-assignment -->
`var e = a` holds an isolated region, and one branch assigns mutable data to it, which is refused where it is assigned.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(flag bool) returns Tally
	var a = Box.create(1) as isolated Box
	var spare = Box.create(5)
	var e = a

	if flag 'swap'
		e = spare
	end 'swap'

	return e.n
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:18:7: 'spare' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: loop-reassigning-an-isolated-variable-a-fresh-call-stays-isolated -->
`e = Box.create(i + 1)` hands the next trip a clean call result, which an isolated variable takes, so the loop is accepted and releases every region it makes once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function total(rounds Tally) returns Tally
	var e = Box.create(0) as isolated Box
	var sum = 0 as Tally
	var i = 0 as Tally

	while i < rounds 'again'
		sum = sum + e.n
		e = Box.create(i + 1)
		i = i + 1
	end 'again'

	return sum + e.n
end 'total'

function main() returns ExitCode
	print("{total(3)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: closure-capture-of-an-isolated-local-moves-it -->
The closure owns the region it captures, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	let f = function() gives a.n
	let k = a.n
	print("{f()} {k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:10: use of moved value 'a': its ownership moved to the closure that captures it
```

<!-- test: returned-isolated-local-runs-and-leaks-nothing -->
`return a` hands the region to the caller, which owns and releases it, and nothing in the callee releases it again.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function make() returns isolated Box
	let a = Box.create(1) as isolated Box
	return a
end 'make'

function main() returns ExitCode
	let r = make()
	print("{r.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: returning-an-isolated-parameter-as-a-plain-result-is-refused -->
`pass` declares no `isolated` result, so `return box` would hand a closed region to the caller as ordinary mutable data; it is merged where it is returned.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pass(box isolated Box) returns Box
	return box
end 'pass'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	var plain = pass(a)
	plain.n = 4
	print("{plain.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:13:9: a return does not fit the declared result: 'box' is isolated, the result is not `isolated`; return `merge box`, or declare the result `isolated`
```

<!-- test: returning-an-isolated-local-as-a-plain-result-is-refused -->
The same for an `isolated` local: the result is ordinary data of the caller's region, so the region is merged first.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function make() returns Box
	let a = Box.create(1) as isolated Box
	return a
end 'make'

function main() returns ExitCode
	var plain = make()
	plain.n = 4
	print("{plain.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:14:9: a return does not fit the declared result: 'a' is isolated, the result is not `isolated`; return `merge a`, or declare the result `isolated`
```

<!-- test: returning-a-merged-isolated-parameter-as-a-plain-result-runs -->
`return merge box` moves the region into the caller's, where the result is ordinary mutable data.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pass(box isolated Box) returns Box
	return merge box
end 'pass'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	var plain = pass(a)
	plain.n = 4
	print("{plain.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: isolated-local-passed-through-an-interface-requirement-is-moved -->
`Sink.take` declares its parameter isolated, so a call through the requirement takes the region from `a` whichever conformer is behind it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Sink
	function take(mutable self, box isolated Box)
end 'Sink'

type Counter implements Sink
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function take(box isolated Box)
		let added = box.n
		self.total = self.total + added
	end 'take'

	function size() returns Tally
		return self.total
	end 'size'
end 'Counter'

type Feeder
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function feed() returns Tally
		var a = Box.create(3) as isolated Box
		self.sink.take(a)
		return a.n
	end 'feed'
end 'Feeder'

function main() returns ExitCode
	var counter = Counter.create()
	var feeder = Feeder.create(counter)
	print("{feeder.feed()} {counter.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:43:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-argument-through-an-interface-requirement-is-dropped-once -->
A conformer takes the isolated region it is called with, from a local and from a fresh call result alike, whether the call names the interface-typed parameter or a stored interface value, and releases it once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Sink
	function take(mutable self, box isolated Box)
end 'Sink'

type Counter implements Sink
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function take(box isolated Box)
		let added = box.n
		self.total = self.total + added
	end 'take'

	function size() returns Tally
		return self.total
	end 'size'
end 'Counter'

type Feeder
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function feed()
		var a = Box.create(3) as isolated Box
		self.sink.take(a)
		self.sink.take(Box.create(4))
	end 'feed'
end 'Feeder'

function feedDirect(sink Sink)
	var a = Box.create(30) as isolated Box
	sink.take(a)
	sink.take(Box.create(40))
end 'feedDirect'

function main() returns ExitCode
	var counter = Counter.create()
	var feeder = Feeder.create(counter)
	feeder.feed()
	feedDirect(counter)
	print("{counter.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
77
```

<!-- test: isolated-local-passed-to-an-async-call-is-moved -->
`work` declares its parameter isolated, so spawning it with `a` takes the region from `a` and the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function work(box isolated Box) returns Tally
	sleep(1)
	return box.n
end 'work'

function main() returns ExitCode
	let a = Box.create(7) as isolated Box
	let pending = async work(a)
	let again = a.n
	let got = await pending
	print("{got} {again}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: isolated-argument-of-an-async-call-is-dropped-once -->
The spawned coroutine owns the isolated region it was handed, from a local and from a fresh call result alike, and releases it once when it finishes.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function work(box isolated Box) returns Tally
	sleep(1)
	return box.n
end 'work'

function main() returns ExitCode
	let a = Box.create(7) as isolated Box
	let first = async work(a)
	let second = async work(Box.create(8))
	let one = await first
	let two = await second
	print("{one} {two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 8
```

<!-- test: isolated-parameter-of-a-throwing-callee-is-dropped-once -->
The callee owns the isolated region it was handed whether it returns or throws, so both calls end with no allocation held.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

enum LimitError implements Error
	exceeded
end 'LimitError'

function check(box isolated Box, limit Tally) returns Tally throws LimitError
	if box.n > limit 'tooBig'
		throw LimitError.exceeded
	end 'tooBig'

	return box.n
end 'check'

function main() returns ExitCode
	let small = Box.create(5) as isolated Box
	let large = Box.create(50) as isolated Box
	let one = try check(small, limit: 10) otherwise 0
	let two = try check(large, limit: 10) otherwise 99
	print("{one} {two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 99
```

<!-- test: isolated-local-made-in-each-loop-trip-is-moved-and-dropped-once -->
Each trip makes its own isolated region and hands it to `consume`, which releases it, so the loop ends with no allocation held.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function consume(box isolated Box) returns Tally
	return box.n
end 'consume'

function main() returns ExitCode
	var total = 0 as Tally

	for i in 1 to 3 'eachTrip'
		let made = Box.create(i) as isolated Box
		total = total + consume(made)
	end 'eachTrip'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: closure-capture-of-an-isolated-parameter-moves-it -->
The closure owns the region it captures from the parameter, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function capture(a isolated Box) returns Tally
	let f = function() gives a.n
	let k = a.n
	return f() + k
end 'capture'

function main() returns ExitCode
	let x = Box.create(1) as isolated Box
	print("{capture(x)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:10: use of moved value 'a': its ownership moved to the closure that captures it
```

<!-- test: mutable-isolated-requirement-parameter-leaves-the-callers-variable-usable -->
`Sink.grow` declares its parameter `mutable isolated`, so a call through the requirement borrows the caller's region exactly as a call to a function does, and the caller reads it afterwards.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

interface Sink
	function grow(mutable self, box mutable isolated Box)
end 'Sink'

type Counter implements Sink
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	function grow(box mutable isolated Box)
		self.calls = self.calls + 1

		modify box 'edit'
			box.n = box.n + 10
		end 'edit'
	end 'grow'

	function size() returns Tally
		return self.calls
	end 'size'
end 'Counter'

function feed(sink Sink) returns Tally
	var a = Box.create(1) as isolated Box
	sink.grow(a)
	sink.grow(a)
	return a.n
end 'feed'

function main() returns ExitCode
	var counter = Counter.create()
	print("{feed(counter)} {counter.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21 2
```

<!-- test: isolated-argument-of-a-message-is-moved-and-dropped-once -->
A message handler takes its `isolated` parameter, so the sender gives up the region it sends and the service releases it once.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Sink
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(box isolated Box)
		let added = box.n
		self.total = self.total + added
	end 'take'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let a = Box.create(3) as isolated Box
	try sink.take(a) otherwise panic("an open mailbox with room takes the send")
	let total = try await sink.size() otherwise 0
	sink.shutdown()
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: isolated-local-sent-in-a-message-is-moved -->
The handler's `isolated` parameter takes the region out of `a`, so reading `a` after the send is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Sink
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(box isolated Box)
		let added = box.n
		self.total = self.total + added
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let a = Box.create(3) as isolated Box
	try sink.take(a) otherwise panic("an open mailbox with room takes the send")
	let k = a.n
	sink.shutdown()
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:29:10: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: mutable-isolated-function-type-parameter-leaves-the-callers-variable-usable -->
`Grow` declares its parameter `mutable isolated`, so a call through the function value borrows the caller's region exactly as a call to the function does, and the caller reads it afterwards.

```maxon
typealias Tally = int(0 to u64.max)
typealias Grow = function(box mutable isolated Box)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(box mutable isolated Box)
	modify box 'edit'
		box.n = box.n + 10
	end 'edit'
end 'bump'

function feed(grow Grow) returns Tally
	var a = Box.create(1) as isolated Box
	grow(a)
	grow(a)
	return a.n
end 'feed'

function main() returns ExitCode
	print("{feed(bump)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21
```

<!-- test: frozen-local-handed-to-a-factory-parameter-that-stores-it-runs -->
`Holder.build` declares `item` `immutable` and keeps it in a frozen field. The factory takes its own reference to what it keeps, and that reference is the frozen data `pinned` names, so a frozen local is accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as immutable Box

	static function build(item immutable Box) returns Self
		return Self{held: item}
	end 'build'
end 'Holder'

function main() returns ExitCode
	let pinned = freeze Box.create(1)
	let h = Holder.build(pinned)
	print("{h.held.n} {pinned.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: immutable-parameter-stored-into-a-mutable-field-is-refused -->
`create` declares `held` `immutable`, but the field is an ordinary mutable `Box`, so storing it would let `h.held.n = 7` write the frozen record `pinned` names.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as Box

	static function create(held immutable Box) returns Self
		return Self{held: held}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var h = Holder.create(pinned)
	h.held.n = 7
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:16:21: cannot store an immutable 'Box' into 'held', which holds a mutable 'Box'; store a copy
```

<!-- test: frozen-argument-to-a-factory-parameter-that-stores-it-in-a-mutable-field-is-refused -->
`Holder.build` keeps `item` in a mutable field, so `item` is mutable only: it needs no marker, and a frozen record cannot be handed to it, because `h.held.n = 7` would write the record `pinned` names.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'
end 'Holder'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var h = Holder.build(pinned)
	h.held.n = 7
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:22:23: 'build' writes 'item' (line 16: `return Self{held: item}`); 'pinned' is immutable
```

<!-- test: frozen-argument-to-a-method-parameter-assigned-to-a-mutable-field-is-refused -->
`adopt` assigns `item` to a mutable field of its receiver, which makes `item` mutable only.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as Box

	static function empty() returns Self
		return Self{held: Box.create(0)}
	end 'empty'

	function adopt(item Box)
		self.held = item
	end 'adopt'
end 'Holder'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var h = Holder.empty()
	h.adopt(pinned)
	h.held.n = 7
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:10: 'adopt' writes 'item' (line 20: `self.held = item`); 'pinned' is immutable
```

<!-- test: frozen-argument-to-a-parameter-that-a-relay-hands-to-a-storing-parameter-is-refused -->
`relay` stores `thing` only by handing it to `Holder.build`, and the refusal names the chain.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'
end 'Holder'

function relay(thing Box) returns Holder
	return Holder.build(thing)
end 'relay'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var h = relay(pinned)
	h.held.n = 7
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:16: 'relay' writes 'thing' (it calls 'build', which writes 'item'); 'pinned' is immutable
```

<!-- test: frozen-argument-to-a-parameter-pushed-into-an-array-is-refused -->
`stash` pushes `item` into an array of mutable records, so `item` is mutable only.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function stash(items BoxArray, item Box)
	items.push(item)
end 'stash'

function main() returns ExitCode
	var items = BoxArray.create(0)
	var pinned = freeze Box.create(1)
	stash(items, item: pinned)
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:15: 'stash' writes 'item' (line 14: `items.push(item)`); 'pinned' is immutable
```

<!-- test: mutable-argument-to-a-parameter-stored-in-an-immutable-field-is-refused -->
`Shelf.build` keeps `item` in an `immutable` field, which makes `item` immutable only: a mutable record another name may still write cannot be handed to it.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export var held as immutable Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'
end 'Shelf'

function main() returns ExitCode
	var loose = Box.create(1)
	var s = Shelf.build(loose)
	loose.n = 9
	print("{s.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:22:22: 'build' needs 'item' frozen (line 16: `return Self{held: item}`); 'loose' is mutable. Freeze it first
```

<!-- test: frozen-argument-to-a-parameter-stored-in-an-immutable-field-runs -->
Both stores of `item` into an `immutable` field, the literal and the assignment, make it immutable only, and a frozen record meets that.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export var held as immutable Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'

	function put(item Box)
		self.held = item
	end 'put'
end 'Shelf'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var s = Shelf.build(pinned)
	s.put(pinned)
	print("{s.held.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: mutable-arguments-to-parameters-that-store-them-run -->
A parameter the body stores is mutable only, so the mutable records `loose` and `other` are accepted, the relay passes the class on, and the stores alias them.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var held as Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'

	function adopt(item Box)
		self.held = item
	end 'adopt'
end 'Holder'

function relay(thing Box) returns Holder
	return Holder.build(thing)
end 'relay'

function main() returns ExitCode
	var loose = Box.create(1)
	var other = Box.create(2)
	let frozen = freeze Box.create(3)
	var h = relay(loose)
	h.adopt(other)
	h.held.n = 7
	other.n = other.n + 1
	print("{loose.n} {other.n} {h.held.n} {frozen.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 8 8 3
```

<!-- test: frozen-argument-to-an-async-call-of-a-writer-is-refused -->
The coroutine takes a reference of its own to what it is handed, and that reference is the frozen record `pinned` names, so `bump` writing it is refused as it is at a direct call.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(item Box) returns Tally
	sleep(1)
	item.n = item.n + 1
	return item.n
end 'bump'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	let p = async bump(pinned)
	let got = await p
	print("{got} {pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:21: 'bump' writes 'item' (line 14: `item.n = item.n + 1`); 'pinned' is immutable
```

<!-- test: struct-literal-storing-a-mutable-value-into-an-immutable-field-is-refused -->
A struct literal judges each field against its slot as an assignment does: `local` is mutable and `held` is `immutable`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrong
	export var held as immutable Box

	static function build() returns Self
		var local = Box.create(1)
		local.n = 2
		return Self{held: local}
	end 'build'
end 'Wrong'

function main() returns ExitCode
	let w = Wrong.build()
	print("{w.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:21: cannot store a mutable 'Box' into 'held', which holds an immutable 'Box'; freeze it, or store a copy
```

<!-- test: struct-literal-storing-a-parameter-into-an-isolated-field-is-refused -->
A parameter is outer data, which an `isolated` field takes only as a value, immutable data or a moved `isolated` one.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Vault
	export var held as isolated Box

	static function build(item Box) returns Self
		return Self{held: item}
	end 'build'
end 'Vault'

function main() returns ExitCode
	var shared = Box.create(1)
	var v = Vault.build(shared)
	v.held.n = v.held.n + 1
	print("{shared.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:16:21: 'item' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Box`, or store a copy
```

<!-- test: frozen-value-stored-in-a-union-payload-is-refused -->
A union payload is an ordinary mutable slot, so a frozen record cannot be built into one.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	filled(box Box)
	empty
end 'Slot'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var s = Slot.filled(pinned)
	match s 'm'
		filled(b) then b.n = 7
		empty then print("empty\n")
	end 'm'
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:19:22: cannot store an immutable 'Box' into 'Slot.filled.box', which holds a mutable 'Box'; store a copy
```

<!-- test: frozen-argument-to-a-parameter-built-into-a-union-payload-is-refused -->
`wrap` keeps `item` in a union payload, which makes `item` mutable only.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

union Slot
	filled(box Box)
	empty
end 'Slot'

function wrap(item Box) returns Slot
	return Slot.filled(item)
end 'wrap'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var s = wrap(pinned)
	match s 'm'
		filled(b) then b.n = 7
		empty then print("empty\n")
	end 'm'
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:15: 'wrap' writes 'item' (line 18: `return Slot.filled(item)`); 'pinned' is immutable
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data-into-a-mutable-array -->
`items.push(e)` stores the carried `e` into a mutable-element array, and the next trip stores frozen data there, so the store is judged against the header join of `e`.

```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var items = Boxes.create(0)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		items.push(e)
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:19:14: cannot store a read-only 'Box' into 'an element of the container push writes', as it joins immutable and mutable data; store a copy
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data-into-a-struct-literal -->
`Self{box: e}` stores the carried `e` into a mutable field, and the next trip stores frozen data there.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var box as Box

	static function build(pinned immutable Box, rounds Tally) returns Self
		var e = Box.create(0)
		var h = Self{box: e}
		var i = 0 as Tally

		while i < rounds 'again'
			h = Self{box: e}
			e = pinned
			i = i + 1
		end 'again'

		return h
	end 'build'
end 'Holder'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	let h = Holder.build(pinned, rounds: 2)
	print("{h.box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:21:18: cannot store a read-only 'Box' into 'box', as it joins immutable and mutable data; store a copy
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data-into-a-tuple-literal -->
The tuple `(e, Box.create(1))` types its first item from the first trip's `e`, and the next trip's frozen `e` would sit in a mutable item.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var pair = (e, Box.create(1))
	var i = 0 as Tally

	while i < rounds 'again'
		pair = (e, Box.create(1))
		e = pinned
		i = i + 1
	end 'again'

	pair.0.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:11: cannot store a read-only 'Box' into 'an item of the tuple', as it joins immutable and mutable data; store a copy
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data-into-a-map-literal -->
`["k": e]` stores the carried `e` as a map value, and the next trip stores frozen data there.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(pinned immutable Box, rounds Tally)
	var e = Box.create(0)
	var m = ["k": e]
	var i = 0 as Tally

	while i < rounds 'again'
		m = ["k": e]
		e = pinned
		i = i + 1
	end 'again'

	var found = try m.get("k") otherwise panic("missing")
	found.n = 5
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:18:13: cannot store a read-only 'Box' into 'a value of the map literal', as it joins immutable and mutable data; store a copy
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-frozen-data-into-a-declared-generic -->
`stack.push(e)` is a store into the `T` slot of a mutable instance, and the next trip pushes frozen data.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Stack uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(item T)
		self.items.push(item)
	end 'push'

	function size() returns Tally
		return self.items.count()
	end 'size'
end 'Stack'

typealias BoxStack = Stack with Box

function edit(pinned immutable Box, rounds Tally)
	var stack = BoxStack.create()
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		stack.push(e)
		e = pinned
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:38:14: cannot store a read-only 'Box' into 'Stack.push.item', as it joins immutable and mutable data; store a copy
```

<!-- test: a-parameter-joined-with-local-data-and-stored-is-mutable-only -->
`c` is `p` on one path and a fresh box on the other, so storing `c` into the mutable field needs `p` writable and the frozen argument is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var item as Box

	static function create() returns Self
		return Self{item: Box.create(0)}
	end 'create'
end 'Holder'

function bump(p Box, flag bool)
	var h = Holder.create()
	var c = Box.create(1)

	if flag 'useParameter'
		c = p
	end 'useParameter'

	h.item = c
end 'bump'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	bump(frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:33:7: 'bump' writes 'p'; 'frozen' is immutable
```

<!-- test: a-parameter-joined-with-local-data-and-pushed-is-mutable-only -->
The same join pushed into a mutable-element array pins `p` mutable-only.

```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(p Box, flag bool)
	var items = Boxes.create(0)
	var c = Box.create(1)

	if flag 'useParameter'
		c = p
	end 'useParameter'

	items.push(c)
end 'bump'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	bump(frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:7: 'bump' writes 'p'; 'frozen' is immutable
```

<!-- test: loop-store-of-a-variable-the-back-edge-assigns-a-parameter-pins-it-mutable-only -->
The back edge hands `p` to the carried `e`, which `items.push(e)` stores into a mutable-element array on the next trip, so `p` is inferred mutable-only and the frozen argument is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function edit(p Box, rounds Tally)
	var items = Boxes.create(0)
	var e = Box.create(0)
	var i = 0 as Tally

	while i < rounds 'again'
		items.push(e)
		e = p
		i = i + 1
	end 'again'
end 'edit'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	edit(pinned, rounds: 2)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:7: 'edit' writes 'p'; 'pinned' is immutable
```

<!-- test: a-parameter-returned-on-one-path-and-fresh-data-on-another-accepts-a-frozen-argument-for-reading -->
`pick` hands back its parameter on one path and a fresh box on the other, so `p` stays any-capability: a frozen argument is accepted for reading.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	if flag 'useParameter'
		return p
	end 'useParameter'

	return Box.create(0)
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	let r = pick(frozen, flag: true)
	print("{r.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: write-through-a-mixed-return-of-a-frozen-argument-is-refused -->
The result of `pick` may be `frozen` itself, so writing through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	if flag 'useParameter'
		return p
	end 'useParameter'

	return Box.create(0)
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	var r = pick(frozen, flag: true)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:23:2: cannot write 'n' through 'r', which is read-only
```

<!-- test: writer-through-a-mixed-return-needs-a-mutable-argument -->
`edit` writes the record `pick` may hand back, which may be its own parameter `b`, so `b` is mutable only and the frozen box is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	if flag 'useParameter'
		return p
	end 'useParameter'

	return Box.create(0)
end 'pick'

function edit(b Box)
	var c = pick(b, flag: true)
	c.n = 5
end 'edit'

function main() returns ExitCode
	var frozen = freeze Box.create(7)
	edit(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:27:7: 'edit' writes 'b'; 'frozen' is immutable
```

<!-- test: a-parameter-joined-with-local-data-and-only-read-accepts-a-frozen-argument -->
`c` is `p` on one path and a fresh box on the other, and `total` only reads it, so `p` stays any-capability and a frozen argument is accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function total(p Box, flag bool) returns Tally
	var c = Box.create(1)

	if flag 'useParameter'
		c = p
	end 'useParameter'

	return c.n
end 'total'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	print("{total(frozen, flag: true)} {total(frozen, flag: false)}\n")
	return 0
end 'main'
```
```stdout
7 1
```
```exitcode
0
```

<!-- test: a-conditional-of-a-parameter-and-fresh-data-only-read-accepts-a-frozen-argument -->
The same join spelled as a conditional expression.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function total(p Box, flag bool) returns Tally
	let c = p if flag else Box.create(1)

	return c.n
end 'total'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	print("{total(frozen, flag: true)} {total(frozen, flag: false)}\n")
	return 0
end 'main'
```
```stdout
7 1
```
```exitcode
0
```

<!-- test: a-try-otherwise-of-local-data-and-a-parameter-returned-accepts-a-frozen-argument-for-reading -->
`pick` hands back an element of a local array or, when it is empty, its parameter, so a frozen argument is accepted and the result is read-only.

```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, spare Boxes) returns Box
	return try spare.first() otherwise p
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	let r = pick(frozen, spare: Boxes.create(0))
	print("{r.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-joined-parameter-returned-is-written-through-only-for-a-mutable-argument -->
The result of `pick` may be the argument itself, so a mutable argument gives a writable result.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	return p if flag else Box.create(0)
end 'pick'

function main() returns ExitCode
	var mine = Box.create(1)
	var r = pick(mine, flag: true)
	r.n = 5
	print("{mine.n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-joined-parameter-returned-is-read-only-for-a-frozen-argument -->
The same result, given a frozen argument, may be that frozen box, so writing through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	return p if flag else Box.create(0)
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	var r = pick(frozen, flag: true)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:2: cannot write 'n' through 'r', which is read-only
```

<!-- test: a-parameter-joined-with-local-data-and-written-through-the-join-is-mutable-only -->
Writing through the join writes `p` on the path that took it, so `p` is mutable only and the frozen argument is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(p Box, flag bool)
	var c = Box.create(1)

	if flag 'useParameter'
		c = p
	end 'useParameter'

	c.n = 5
end 'bump'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	bump(frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:24:7: 'bump' writes 'p'; 'frozen' is immutable
```

<!-- test: a-parameter-joined-with-local-data-and-handed-to-a-writer-is-mutable-only -->
`grow` writes the record `c` names, which may be `p`, so `p` is mutable only and the frozen argument is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function grow(b Box)
	b.n = b.n + 1
end 'grow'

function bump(p Box, flag bool)
	var c = Box.create(1)

	if flag 'useParameter'
		c = p
	end 'useParameter'

	grow(c)
end 'bump'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	bump(frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:28:7: 'bump' writes 'p' (it calls 'grow', which writes 'b'); 'frozen' is immutable
```

<!-- test: a-conditional-of-a-parameter-and-fresh-data-handed-to-a-writer-is-mutable-only -->
The same hand-off with the join spelled as a conditional expression.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function grow(b Box)
	b.n = b.n + 1
end 'grow'

function bump(p Box, flag bool)
	grow(p if flag else Box.create(1))
end 'bump'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	bump(frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:22:7: 'bump' writes 'p' (it calls 'grow', which writes 'b'); 'frozen' is immutable
```

<!-- test: a-parameter-joined-with-local-data-and-returned-then-written-by-the-caller-is-mutable-only -->
`edit` writes the record `pick` may hand back, which may be its own parameter `b`, so `b` is mutable only and the frozen box is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, flag bool) returns Box
	return p if flag else Box.create(0)
end 'pick'

function edit(b Box)
	var c = pick(b, flag: true)
	c.n = 5
end 'edit'

function main() returns ExitCode
	var frozen = freeze Box.create(7)
	edit(frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:7: 'edit' writes 'b'; 'frozen' is immutable
```

<!-- test: a-join-of-two-parameters-returned-is-read-only-for-frozen-arguments -->
`c` is `a` or `b`, so the result of `pick` may be either frozen box and writing through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c
end 'pick'

function main() returns ExitCode
	let first = freeze Box.create(1)
	let second = freeze Box.create(2)
	var r = pick(first, b: second, flag: true)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:26:2: cannot write 'n' through 'r', which is immutable
```

<!-- test: a-join-of-two-parameters-returned-is-written-through-for-mutable-arguments -->
The same result, given mutable arguments, is writable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c
end 'pick'

function main() returns ExitCode
	var first = Box.create(1)
	var second = Box.create(2)
	var r = pick(first, b: second, flag: true)
	r.n = 5
	print("{first.n} {second.n}\n")
	return 0
end 'main'
```
```stdout
1 5
```
```exitcode
0
```

<!-- test: a-join-of-two-parameters-returned-and-written-by-the-caller-is-mutable-only -->
`edit` writes the record `pick` may hand back, which may be `x` or `y`, so both are mutable only and the frozen box is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c
end 'pick'

function edit(x Box, y Box)
	var c = pick(x, b: y, flag: true)
	c.n = 5
end 'edit'

function main() returns ExitCode
	var mine = Box.create(1)
	var frozen = freeze Box.create(7)
	edit(mine, y: frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:13: 'edit' writes 'y'; 'frozen' is immutable
```

<!-- test: a-join-of-two-parameters-written-through-the-join-is-mutable-only -->
Writing through the join writes `a` or `b`, so both are mutable only and the frozen argument is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function bump(a Box, b Box, flag bool)
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	c.n = 5
end 'bump'

function main() returns ExitCode
	var mine = Box.create(1)
	var frozen = freeze Box.create(7)
	bump(mine, b: frozen, flag: true)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:25:13: 'bump' writes 'b'; 'frozen' is immutable
```

<!-- test: a-loop-join-of-two-parameters-returned-is-read-only-for-frozen-arguments -->
The back edge hands `b` to the carried `c`, so the result may be either frozen box.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, rounds Tally) returns Box
	var c = a
	var i = 0 as Tally

	while i < rounds 'again'
		c = b
		i = i + 1
	end 'again'

	return c
end 'pick'

function main() returns ExitCode
	let first = freeze Box.create(1)
	let second = freeze Box.create(2)
	var r = pick(first, b: second, rounds: 1)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:28:2: cannot write 'n' through 'r', which is immutable
```

<!-- test: a-join-of-two-parameters-only-read-accepts-frozen-arguments -->
`total` only reads the join, so both parameters stay any-capability and frozen arguments are accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function total(a Box, b Box, flag bool) returns Tally
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c.n
end 'total'

function main() returns ExitCode
	let first = freeze Box.create(1)
	let second = freeze Box.create(2)
	print("{total(first, b: second, flag: true)} {total(first, b: second, flag: false)}\n")
	return 0
end 'main'
```
```stdout
2 1
```
```exitcode
0
```

<!-- test: a-join-of-two-parameters-returned-given-one-frozen-argument-is-not-written-through -->
`pick` may hand back either box, so one frozen argument makes the result unwritable, whichever argument is mutable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c
end 'pick'

function main() returns ExitCode
	var mine = Box.create(1)
	let frozen = freeze Box.create(7)
	var r = pick(mine, b: frozen, flag: false)
	r.n = 5
	print("{mine.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:25:10: 'pick' returns 'a' or 'b'; they must both be mutable or both immutable here
error E3197: <fragment>:26:2: cannot write 'n' through 'r', which is immutable
```

<!-- test: a-conditional-of-two-parameters-returned-given-one-frozen-argument-is-not-written-through -->
The same result spelled as a conditional expression.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	return a if flag else b
end 'pick'

function main() returns ExitCode
	var mine = Box.create(1)
	let frozen = freeze Box.create(7)
	var r = pick(mine, b: frozen, flag: false)
	r.n = 5
	print("{mine.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:19:10: 'pick' returns 'a' or 'b'; they must both be mutable or both immutable here
error E3197: <fragment>:20:2: cannot write 'n' through 'r', which is immutable
```

<!-- test: a-join-of-two-parameters-returned-and-handed-to-a-writer-by-the-caller-is-mutable-only -->
`grow` writes the record `pick` may hand back, so both arguments must be mutable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, flag bool) returns Box
	var c = a

	if flag 'useSecond'
		c = b
	end 'useSecond'

	return c
end 'pick'

function grow(box Box)
	box.n = box.n + 1
end 'grow'

function edit(x Box, y Box)
	grow(pick(x, b: y, flag: true))
end 'edit'

function main() returns ExitCode
	var mine = Box.create(1)
	var frozen = freeze Box.create(7)
	edit(mine, y: frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:33:13: 'edit' writes 'y' (it calls 'grow', which writes 'box'); 'frozen' is immutable
```

<!-- test: a-mixed-join-joined-with-immutable-data-is-read -->
`own` is `p` or a fresh box, and `choose` hands back either `own` or its `immutable` parameter `fixed`, so the result is only ever read.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function choose(p Box, fixed immutable Box, flag bool) returns Box
	let own = p if flag else Box.create(1)

	return own if flag else fixed
end 'choose'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	let other = freeze Box.create(9)
	print("{choose(frozen, fixed: other, flag: true).n} {choose(frozen, fixed: other, flag: false).n}\n")
	return 0
end 'main'
```
```stdout
7 9
```
```exitcode
0
```

<!-- test: a-mixed-join-joined-with-immutable-data-is-not-written-through -->
The result of `choose` may be `fixed`, so a mutable `p` does not make it writable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function choose(p Box, fixed immutable Box, flag bool) returns Box
	let own = p if flag else Box.create(1)

	return own if flag else fixed
end 'choose'

function main() returns ExitCode
	var mine = Box.create(1)
	let other = freeze Box.create(9)
	var r = choose(mine, fixed: other, flag: true)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:21:10: 'choose' returns 'p' or 'fixed'; they must both be mutable or both immutable here
error E3197: <fragment>:22:2: cannot write 'n' through 'r', which is immutable
```

<!-- test: a-mixed-join-joined-with-immutable-data-is-read-only-for-frozen-arguments -->
Given frozen arguments the result of `choose` may be either frozen box, so writing through it is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function choose(p Box, fixed immutable Box, flag bool) returns Box
	let own = p if flag else Box.create(1)

	return own if flag else fixed
end 'choose'

function main() returns ExitCode
	let frozen = freeze Box.create(7)
	let other = freeze Box.create(9)
	var r = choose(frozen, fixed: other, flag: true)
	r.n = 5
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:22:2: cannot write 'n' through 'r', which is read-only
```

<!-- test: a-holder-method-writing-through-an-element-read-is-refused-for-a-frozen-array -->
`bump` writes `.n` through an element of the array `holder` holds, so the frozen array must not be given to the holder.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Holder
	var items as Boxes

	static function over(items Boxes) returns Self
		return Self{items: items}
	end 'over'

	function bump()
		for box in self.items 'each'
			box.n = 5
		end 'each'
	end 'bump'
end 'Holder'

function oneBox() returns Boxes
	var raw = Boxes.create(0)
	raw.push(Box.create(1))

	return raw
end 'oneBox'

function main() returns ExitCode
	let frozen = freeze oneBox()
	var holder = Holder.over(frozen)
	holder.bump()
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:37:27: 'over' writes 'items' (line 18: `return Self{items: items}`); 'frozen' is immutable
```

<!-- test: a-holder-method-reaching-through-a-wrapper-is-refused-for-a-frozen-array -->
The same write one method deeper: `run` hands the holder to `bump`, which writes through an element.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Holder
	var items as Boxes

	static function over(items Boxes) returns Self
		return Self{items: items}
	end 'over'

	function bump()
		for box in self.items 'each'
			box.n = 5
		end 'each'
	end 'bump'

	function run()
		self.bump()
	end 'run'
end 'Holder'

function oneBox() returns Boxes
	var raw = Boxes.create(0)
	raw.push(Box.create(1))

	return raw
end 'oneBox'

function main() returns ExitCode
	let frozen = freeze oneBox()
	var holder = Holder.over(frozen)
	holder.run()
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:41:27: 'over' writes 'items' (line 18: `return Self{items: items}`); 'frozen' is immutable
```

<!-- test: a-generic-call-that-may-yield-inside-modify-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
`settle` is generic and awaits a coroutine, so the call is to an instance that may yield, and no call inside the block may.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

function work() returns Tally
	sleep(1)

	return 5
end 'work'

function settle(_ T) uses T returns Tally
	let pending = async work()
	return await pending
end 'settle'

function main() returns ExitCode
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'fill'
		let n = settle(1)
		batch.push("{n}")
	end 'fill'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:20:11: cannot call 'settle' inside 'fill'; it may yield (it awaits a coroutine at line 13). Call it before or after the block
```
