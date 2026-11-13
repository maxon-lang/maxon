---
feature: acquire-states
status: experimental
keywords: [acquire, state, window, modify, one-statement open, view, closure, isolated, service]
category: semantics
---

# Acquired states: one open at a time

## Documentation

Inside `acquire s, t 'held' … end 'held'` each name is the state of its service, an `isolated` value that is not open. Reading a
field of it, or calling a method on it, opens it for that one statement, and what the statement hands back may outlive it only as
a value, `immutable` data or a fresh `isolated` value (E3202): `let held = s.cell` is refused, `let n = s.cell.n` and
`let copy = s.cell.clone()` are not. A call that hands back mutable data of the state it opens is refused outside `modify` (E3202).

Two names of one `acquire` may be one service, so two states are never open at once. A statement reaches at most one acquired state
(E3201); a read of a second state is a statement of its own. Inside `modify s`, or `modify s.spare`, every other state is read-only:
it is read for one statement at a time, its data is never written, called with a writing method, passed to a writing function,
opened with `modify` or captured by a closure. A `for` head over a state keeps that state open for the whole loop.

## Tests

<!-- test: a-record-field-of-a-closed-state-cannot-outlive-its-statement -->
`s.cell` read into a `let` is a view of the state's data that a later write through `s` would invalidate, so it is refused; the value needed is read instead.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let held = s.cell
		s.cell.n = 5
		return held.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:47:14: 's.cell' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: a-method-result-that-views-a-closed-state-cannot-outlive-its-statement -->
`s.peek()` hands back the state's own `cell`, so binding the result is refused the way binding `s.cell` is.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let held = s.peek()
		s.cell.n = 5
		return held.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:47:14: 's.peek()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: a-value-read-from-a-closed-state-outlives-its-statement -->
A value read out of the state (`s.cell.n`) may be kept: a number holds nothing of the state.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let before = s.cell.n
		s.cell.n = 5
		return before + s.cell.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: a-clone-of-a-closed-state-field-outlives-its-statement -->
`s.cell.clone()` is a fresh record of its own, so the binding may outlive the statement.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let copy = s.cell.clone()
		s.cell.n = 5
		return copy.n + s.cell.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: a-field-read-from-a-second-state-cannot-be-bound -->
The second state may be the first one under another name, so a view of its data is refused the same way.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		let held = t.cell
		s.cell.n = 5
		return held.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:47:14: 't.cell' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: inside-a-window-another-state-is-read-as-a-one-statement-open -->
Inside `modify s`, `t.cell` is read for one statement, and its result may not outlive that statement.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			let held = t.cell
			s.cell.n = 5
			s.total = held.n
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:48:15: 't.cell' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: inside-a-window-a-method-view-of-another-state-cannot-be-bound -->
`t.peek()` inside `modify s` hands back mutable data of `t`; the result may not be bound.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			let held = t.peek()
			s.cell.n = 5
			s.total = held.n
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:48:15: 't.peek()' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: inside-a-window-another-state-cannot-be-written -->
`t` is read-only while `modify s` is open: a store into one of its fields is refused.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			t.total = 1
			s.total = 2
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:48:4: cannot write 'total' through 't', which is read-only while 'edit' is open
```

<!-- test: inside-a-window-another-state-cannot-be-written-through-a-record -->
A write through `t.cell` is a write to `t`, which is read-only while `modify s` is open.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			t.cell.n = 1
			s.total = 2
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:48:4: cannot write 'n' through 't.cell', which is read-only while 'the isolated value' is open
```

<!-- test: inside-a-window-a-writing-method-of-another-state-is-refused -->
`t.raise()` writes its receiver, and `t` is read-only while `modify s` is open.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			t.raise()
			s.total = 2
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:48:4: 'raise' needs a writable receiver at line 37; 't' is read-only while 'edit' is open
```

<!-- test: inside-a-window-a-writing-function-cannot-take-data-of-another-state -->
`poke(t.cell)` writes its argument, and `t.cell` is read-only while `modify s` is open.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function poke(cell Cell)
	cell.n = 9
end 'poke'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			poke(t.cell)
			s.total = 2
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:52:9: 'poke' needs a writable parameter 'cell' at line 46; 't' is read-only while 'the isolated value' is open
```

<!-- test: inside-a-window-another-state-cannot-be-opened-by-modify -->
`modify t` inside `modify s` would make two states writable at once.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			modify t 'other'
				t.total = 1
			end 'other'
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:48:11: 't' cannot be opened: the value that holds it is read-only while 'edit' is open
```

<!-- test: inside-a-window-an-isolated-field-of-another-state-cannot-be-opened -->
`modify t.spare` inside `modify s` opens a region of `t` while `s` is open.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			modify t.spare 'other'
				t.spare.n = 1
			end 'other'
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:48:11: 't.spare' cannot be opened: the value that holds it is read-only while 'the isolated value' is open
```

<!-- test: inside-a-window-a-call-into-an-isolated-field-of-another-state-is-refused -->
`t.spare.bump()` writes through `t.spare`, which is read-only while `modify s` is open.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			t.spare.bump()
			s.total = 2
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:48:11: 'bump' needs a writable receiver at line 13; the argument is read-only while 'the isolated value' is open
```

<!-- test: inside-a-window-a-value-of-another-state-cannot-be-stored-in-one-statement -->
`s.total = t.total` reads `t` and writes `s` in one statement; the read opens `t`, so `s` is read-only in it.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			s.total = t.total
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:48:4: cannot write 'total' through 's', which is read-only while 'the isolated value' is open
```

<!-- test: inside-a-window-another-state-cannot-be-captured -->
A closure that names `t` would keep a view of its state, so capturing the state is refused.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			let read = function() gives t.total
			s.total = read()
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:48:32: 't' is the state of an acquired service and cannot be captured by a closure: the service keeps its state
```

<!-- test: a-closed-state-cannot-be-captured -->
A closure that names `s` would keep a view of its state, so capturing the state is refused.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		let read = function() gives s.total
		s.cell.n = 5
		return read()
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:47:31: 's' is the state of an acquired service and cannot be captured by a closure: the service keeps its state
```

<!-- test: a-for-head-over-another-state-holds-a-window-for-its-whole-body -->
The `for` head reads `t.cells`, which keeps `t` open for the whole loop, so the body may not write `s`.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		for cell in t.cells 'each'
			s.total = cell.n
		end 'each'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:48:4: cannot write 'total' through 's', which is read-only while 'the isolated value' is open
```

<!-- test: one-statement-reading-two-acquired-states-is-refused -->
`add(s.total, t.total)` reads two states in one statement, and the two names may be one service.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return add(s.total, second: t.total)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:31: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-reading-two-acquired-states-is-refused-in-either-order -->
`add(t.total, s.total)` is refused the way `add(s.total, t.total)` is.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return add(t.total, second: s.total)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:31: one statement reaches through two acquired states, 't' and 's', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-handing-a-writer-and-a-view-of-two-states-to-a-call-is-refused -->
`copyOver(s.cell, source: t.cell)` writes `s.cell` while it reads `t.cell`.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function copyOver(target Cell, source Cell) returns Tally
	target.n = source.n
	return target.n
end 'copyOver'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return copyOver(s.cell, source: t.cell)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:52:35: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-handing-a-view-first-and-a-writer-second-is-refused -->
`copyOver(t.cell, source: s.cell)` writes `t.cell`; the operand order does not change the verdict.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function copyOver(target Cell, source Cell) returns Tally
	target.n = source.n
	return target.n
end 'copyOver'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return copyOver(t.cell, source: s.cell)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:52:35: one statement reaches through two acquired states, 't' and 's', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-reading-two-states-through-a-receiver-and-an-argument-is-refused -->
`t.cell.give(s.cell)` takes its receiver from one state and writes an argument from another.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return t.cell.give(s.cell)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:47:22: one statement reaches through two acquired states, 't' and 's', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-reading-two-states-through-methods-is-refused -->
A call on each state's own method in one statement reaches two states.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return add(s.size(), second: t.size())
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:32: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-reading-two-states-through-a-function-that-may-return-its-argument-is-refused -->
`pick` may hand back `t.cell` itself, so `copyOver(s.cell, source: pick(t.cell, fresh: false))` still reads two states.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function copyOver(target Cell, source Cell) returns Tally
	target.n = source.n
	return target.n
end 'copyOver'

function pick(cell Cell, fresh bool) returns Cell
	if fresh 'newCell'
		return Cell.create(1)
	end 'newCell'

	return cell
end 'pick'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		return copyOver(s.cell, source: pick(t.cell, fresh: false))
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:60:40: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-statement-reading-three-acquired-states-is-refused -->
A statement that reaches a third state is refused at the second.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle, u Ledger.handle) returns Tally
	acquire s, t, u 'held'
		return add(add(s.total, second: t.total), second: u.total)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	let c = spawn Ledger.create(30)
	print("{work(a, t: b, u: c)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:35: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-condition-reading-two-acquired-states-is-refused -->
An `if` condition is one statement for the purpose.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		if add(s.total, second: t.total) > 0 'positive'
			return 1
		end 'positive'
		return 0
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:51:27: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: one-interpolated-string-reading-two-acquired-states-is-refused -->
The holes of an interpolated string belong to its statement.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		let text = "{s.total} {t.total}"
		return text.count() as Tally
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:47:26: one statement reaches through two acquired states, 's' and 't', which may be one service; bind a value read from one of them in its own statement first
```

<!-- test: modify-of-an-isolated-field-makes-another-state-read-only -->
`modify s.spare` opens `s`, so a value of `t` cannot be stored into `s.spare` in the statement that reads it.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s.spare 'edit'
			s.spare.n = t.total
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:48:4: cannot write 'n' through 's.spare', which is read-only while 'the isolated value' is open
```

<!-- test: modify-of-an-isolated-field-reads-another-state-for-one-statement -->
Inside `modify s.spare`, a view of `t.cell` may not be bound.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s.spare 'edit'
			let held = t.cell
			s.spare.n = held.n
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:48:15: 't.cell' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: modify-of-an-isolated-field-refuses-opening-an-isolated-field-of-another-state -->
`modify t.spare` inside `modify s.spare` would make two states writable at once.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s.spare 'edit'
			modify t.spare 'other'
				t.spare.n = 1
			end 'other'
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'

	return 0
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:48:11: 't.spare' cannot be opened: the value that holds it is read-only while 'the isolated value' is open
```

<!-- test: a-value-of-one-state-is-bound-before-the-other-is-written -->
Read the value in its own statement, then write the other state.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		let n = t.cell.n
		s.cell.n = n
		return s.cell.n
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-clone-of-one-state-is-handed-to-a-write-of-the-other -->
`t.cell.clone()` is fresh data, so the call that writes `s.cell` takes no view of `t`.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function copyOver(target Cell, source Cell) returns Tally
	target.n = source.n
	return target.n
end 'copyOver'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		let copy = t.cell.clone()
		return copyOver(s.cell, source: copy)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: two-states-are-read-in-two-statements -->
Each state is read in its own statement and the values are combined afterwards.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		let first = s.total
		let second = t.total
		return add(first, second: second)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30
```

<!-- test: one-state-is-read-twice-in-one-statement -->
One state named twice in a statement is one state.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		return add(s.total, second: s.cell.n)
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: inside-a-window-another-state-is-read-one-statement-at-a-time -->
Inside `modify s`, `t` is read in statements of its own and the values are stored into `s`.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			let first = t.total
			let second = t.cell.n
			s.total = first + second
		end 'edit'
		return s.total
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21
```

<!-- test: inside-a-window-reads-of-two-states-that-write-nothing-are-one-statement -->
A statement inside `modify s` that only reads values of `t` may name `t` twice.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function add(first Tally, second Tally) returns Tally
	return first + second
end 'add'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			s.total = 0
			let sum = add(t.total, second: t.size())
			s.total = sum
		end 'edit'
		return s.total
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
40
```

<!-- test: inside-a-window-the-opened-states-own-data-may-be-viewed -->
The state the window opens may be viewed and written inside it.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle) returns Tally
	acquire s 'held'
		modify s 'edit'
			let held = s.cell
			s.cell.n = 5
			s.total = held.n
		end 'edit'
		return s.total
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	print("{work(a)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: conditions-of-an-else-if-chain-may-read-different-states -->
Each condition of the chain is its own statement.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		if s.total > 100 'first'
			return 1
		end 'first' else if t.total > 15 'second'
			return 2
		end 'second' else 'last'
			return 3
		end 'last'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-return-inside-modify-inside-acquire-releases-both -->
A `return` from inside `modify` leaves the `acquire` block too, so the next `acquire` of the same service is granted.

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

	function give(target Cell) returns Tally
		target.n = self.n
		return self.n
	end 'give'
end 'Cell'

type Ledger
	export var cell as Cell
	export var total as Tally
	export var spare as isolated Cell
	export var cells as CellArray

	static function create(total Tally) returns Self
		return Self{cell: Cell.create(1), total: total, spare: Cell.create(2), cells: CellArray.create(0)}
	end 'create'

	function peek() returns Cell
		return self.cell
	end 'peek'

	function raise()
		self.total = self.total + 1
	end 'raise'

	export function size() returns Tally
		return self.total
	end 'size'
end 'Ledger'

function work(s Ledger.handle, t Ledger.handle) returns Tally
	acquire s, t 'held'
		modify s 'edit'
			s.total = s.total + 1
			return s.total
		end 'edit'
	end 'held' otherwise 'stopped'
		return 0
	end 'stopped'
end 'work'

function main() returns ExitCode
	let a = spawn Ledger.create(10)
	let b = spawn Ledger.create(20)
	print("{work(a, t: b)}\n")
	print("{work(a, t: b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11
12
```

<!-- test: a-modify-block-that-returns-on-every-path-leaves-a-dead-tail -->
What follows a `modify` block that returns on every path is unreachable, as after an `if` whose branches all return.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function first() returns Tally
	let box = Box.create(7) as isolated Box

	modify box 'edit'
		return box.n
	end 'edit'

	return 0
end 'first'

function main() returns ExitCode
	print("{first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

