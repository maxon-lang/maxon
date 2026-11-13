---
feature: acquire-state-doors
status: stable
keywords: [acquire, state, window, view, try, handler, isolated]
category: semantics
---
# Acquired states: the doors a state could leave through

## Documentation

A call that opens an isolated value or an acquired state and answers mutable data of it is judged where its result leaves the statement, not at the call: a number read out of the result, or the result handed to a writer inside the statement, is accepted, and binding, returning or storing the result is E3202. A `let` whose `try` has an `otherwise` handler is judged as the same statement as one without. An acquired state never leaves its block: binding it, returning it, storing it, moving it into an isolated parameter or a spawn, or sending it through `async` is refused (E3202, E3205), and `acquire s, s` is one acquire.

## Tests

<!-- test: a-value-read-from-an-opening-calls-result-outlives-its-statement -->
`t.peekR()` opens `t` and answers its own record, and `.b` reads a number out of it: the statement hands back a value, so it may be kept.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var b as Tally

	static function create(b Tally) returns Self
		return Self{b: b}
	end 'create'

	function me() returns Cell
		return self
	end 'me'
end 'Cell'

type Ledger
	export var r as Cell
	export var i as isolated Cell
	export var total as Tally

	static function create() returns Self
		return Self{r: Cell.create(4), i: Cell.create(5), total: 0}
	end 'create'

	function peekR() returns Cell
		return self.r
	end 'peekR'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function bump(writer Cell, by Tally)
	writer.b = writer.b + by
end 'bump'
function work(t Ledger.handle) returns Tally
	acquire t 'held'
		let n = t.peekR().b
		bump(Cell.create(0), by: n)

		return n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
function main() returns ExitCode
	let a = spawn Ledger.create()
	print("{work(a)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: a-writer-takes-the-view-a-call-opens-an-isolated-field-for -->
`t.i.me()` opens the isolated field and answers a view of it; the writer takes the view inside the statement and nothing keeps it.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var b as Tally

	static function create(b Tally) returns Self
		return Self{b: b}
	end 'create'

	function me() returns Cell
		return self
	end 'me'
end 'Cell'

type Ledger
	export var r as Cell
	export var i as isolated Cell
	export var total as Tally

	static function create() returns Self
		return Self{r: Cell.create(4), i: Cell.create(5), total: 0}
	end 'create'

	function peekR() returns Cell
		return self.r
	end 'peekR'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function bump(writer Cell, by Tally)
	writer.b = writer.b + by
end 'bump'
function work(t Ledger.handle) returns Tally
	acquire t 'held'
		bump(t.i.me(), by: 2)
		let n = t.i.b

		return n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
function main() returns ExitCode
	let a = spawn Ledger.create()
	print("{work(a)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.an-opening-calls-result-bound-to-a-let-is-refused-at-the-binding -->
`t.peekR()` answers the state's own record, so a `let` that keeps it would name data a later write through `t` invalidates.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var b as Tally

	static function create(b Tally) returns Self
		return Self{b: b}
	end 'create'

	function me() returns Cell
		return self
	end 'me'
end 'Cell'

type Ledger
	export var r as Cell
	export var i as isolated Cell
	export var total as Tally

	static function create() returns Self
		return Self{r: Cell.create(4), i: Cell.create(5), total: 0}
	end 'create'

	function peekR() returns Cell
		return self.r
	end 'peekR'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function bump(writer Cell, by Tally)
	writer.b = writer.b + by
end 'bump'
function work(t Ledger.handle) returns Tally
	acquire t 'held'
		let held = t.peekR()

		return held.b
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
function main() returns ExitCode
	let a = spawn Ledger.create()
	print("{work(a)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:39:14: 't.peekR()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: error.an-opening-calls-result-returned-from-an-acquire-is-refused -->
A result that views the state's record cannot leave the acquire block by `return`.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var b as Tally

	static function create(b Tally) returns Self
		return Self{b: b}
	end 'create'

	function me() returns Cell
		return self
	end 'me'
end 'Cell'

type Ledger
	export var r as Cell
	export var i as isolated Cell
	export var total as Tally

	static function create() returns Self
		return Self{r: Cell.create(4), i: Cell.create(5), total: 0}
	end 'create'

	function peekR() returns Cell
		return self.r
	end 'peekR'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function bump(writer Cell, by Tally)
	writer.b = writer.b + by
end 'bump'
function work(t Ledger.handle) returns Cell
	acquire t 'held'
		return t.peekR()
	end 'held' otherwise 'stopped'
		return Cell.create(0)
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create()
	print("{work(a).b}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:39:10: 't.peekR()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: error.a-view-bound-through-a-try-with-a-handler-is-refused-at-the-binding -->
The handler's statements are parsed inside the `let`, and the `let` is still judged as the statement that opened the isolated value.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Holder
	export var items as NumArray

	static function create() returns Self
		let items = NumArray.create(0)
		items.push(1)
		return Self{items: items}
	end 'create'
end 'Holder'

function viaHandler() returns Num
	let j = Holder.create() as isolated Holder
	let it = try j.items.createIterator() otherwise return 0

	return it.current()
end 'viaHandler'

function main() returns ExitCode
	print("{viaHandler()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:17:11: 'try j.items.createIterator() otherwise return 0' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: error.a-throwing-view-bound-through-a-try-with-a-handler-is-refused -->
A user view wrapper that can throw is judged the same way with or without a handler.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

enum ViewError implements Error
	empty
end 'ViewError'

type View
	export var items as NumArray

	static function over(items NumArray) returns Self throws ViewError
		if items.isEmpty() 'none'
			throw ViewError.empty
		end 'none'

		return Self{items: items}
	end 'over'

	function first() returns Num
		return try self.items.get(0) otherwise 0
	end 'first'
end 'View'

type Holder
	export var items as NumArray

	static function create() returns Self
		let items = NumArray.create(0)
		items.push(1)
		return Self{items: items}
	end 'create'
end 'Holder'

function work() returns Num
	let j = Holder.create() as isolated Holder
	let v = try View.over(j.items) otherwise return 0

	return v.first()
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:37:10: 'try View.over(j.items) otherwise return 0' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: a-value-bound-through-a-try-with-a-handler-is-accepted -->
A number read through the open leaves the statement, handler or not.

```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type Holder
	export var items as NumArray

	static function create() returns Self
		let items = NumArray.create(0)
		items.push(1)
		return Self{items: items}
	end 'create'
end 'Holder'

function work() returns Num
	let j = Holder.create() as isolated Holder
	let n = try j.items.get(0) otherwise return 0

	return n
end 'work'

function main() returns ExitCode
	print("{work()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-state-bound-to-a-local-is-refused -->
`let again = s` would give the state a second name.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let again = s
		return 1
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```maxoncstderr
error E3202: <fragment>:64:15: 's' is the state of an acquired service and cannot be moved: the service keeps its state
```

<!-- test: error.a-state-passed-to-an-isolated-parameter-is-refused -->
An isolated parameter takes its argument by move, and the service keeps its state.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function pass(l isolated Ledger) returns isolated Ledger
	return l
end 'pass'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let again = pass(s)
		return again.total
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```maxoncstderr
error E3202: <fragment>:68:20: 's' is the state of an acquired service and cannot be moved: the service keeps its state
```

<!-- test: error.a-state-sent-through-async-is-refused -->
An `async` coroutine would wait while the block holds the service.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let p = async touch(s)
		return await p
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```maxoncstderr
error E3205: <fragment>:64:11: cannot wait inside 'held'; an acquire block holds its services and may not wait or acquire again. Await before or after the block
```

<!-- test: error.a-state-handed-to-a-spawn-is-refused -->
A spawn factory takes an isolated parameter by move.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let k = spawn Keeper.create(s)
		return 1
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```maxoncstderr
error E3202: <fragment>:64:31: 's' is the state of an acquired service and cannot be moved: the service keeps its state
```

<!-- test: error.a-state-returned-from-its-acquire-is-refused -->
`return s` would move the state out of the service.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function pick(s Ledger.handle) returns Ledger
	acquire s 'held'
		return s
	end 'held' otherwise 'stopped'
		return Ledger.create(0)
	end 'stopped'
end 'pick'

function work(s Ledger.handle) returns Tally
	return pick(s).total
end 'work'
```
```maxoncstderr
error E3202: <fragment>:64:10: 's' is the state of an acquired service and cannot be returned: the service keeps its state
```

<!-- test: error.a-state-pushed-into-a-container-is-refused -->
`push` stores its argument in the array.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

typealias LedgerArray = Array with Ledger

function work(s Ledger.handle) returns Tally
	let all = LedgerArray.create(0)

	acquire s 'held'
		all.push(s)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return all.count()
end 'work'
```
```maxoncstderr
error E3202: <fragment>:68:12: 's' is the state of an acquired service and cannot be stored into 'an element of the container push writes': the service keeps its state
```

<!-- test: error.a-state-in-a-tuple-literal-is-refused -->
A tuple literal stores the state as an item.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let pair = (s, 1)
		return 1
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```maxoncstderr
error E3202: <fragment>:64:15: 's' is the state of an acquired service and cannot be stored into 'an item of the tuple': the service keeps its state
```

<!-- test: error.a-state-written-into-a-record-field-outside-the-block-is-refused -->
The record outside the block is read-only while the block is open, so the store is refused before the state is judged.

```maxon
typealias Tally = int(0 to u64.max)
typealias CellArray = Array with Cell

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

type Box
	export var held as Ledger

	static function create() returns Self
		return Self{held: Ledger.create(0)}
	end 'create'
end 'Box'

function touch(l Ledger) returns Tally
	return l.total
end 'touch'

type Keeper
	var kept as isolated Ledger

	static function create(kept isolated Ledger) returns Self
		return Self{kept: kept}
	end 'create'

	export function ping()
		print("ping\n")
	end 'ping'
end 'Keeper'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'

function work(s Ledger.handle) returns Tally
	let kept = Box.create()

	acquire s 'held'
		kept.held = s
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return kept.held.total
end 'work'
```
```maxoncstderr
error E3197: <fragment>:66:3: cannot write 'held' through 'kept', which is read-only while 'held' is open
```

<!-- test: acquire-of-one-service-twice-is-one-acquire -->
`acquire s, s` names one service twice; the two names are one state, so it is as accepted as `acquire s`.

```maxon
typealias Tally = int(0 to u64.max)

type Ledger
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")

	return 0
end 'main'
function work(s Ledger.handle) returns Tally
	acquire s, s 'held'
		return s.total
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'
```
```exitcode
0
```
```stdout
10
```
