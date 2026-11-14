---
feature: regions-tied
status: experimental
keywords: [async, promise, tied, await, drain, mutable isolated, for, iterator]
category: semantics
---

# Tied promises and the B-era rulings no stage named

## Documentation

An `async` call that takes mutable data produces a tied promise: it lives in a local, is awaited
unconditionally before the function returns, and is never returned, held in a field or container, captured,
sent, or passed to a parameter the callee keeps. A `mutable isolated` parameter may be opened, swapped or
replaced in the callee but never moved out or aliased. The array a `for` drains is read-only in the loop body.

## Tests

<!-- test: a-tied-promise-returned-is-refused -->
`bump` takes the mutable `row`, so the promise `async bump(row)` is tied to this call and cannot be returned.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function start(row Row) returns TallyPromise
	return async bump(row)
end 'start'

function main() returns ExitCode
	var row = Row.create(1)
	let later = start(row)
	let n = await later
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:20:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-tied-promise-never-awaited-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The promise is tied to `row`, so the function must await it before it returns.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	let row = Row.create(1)
	let later = async bump(row)
	print("started\n")
	_ = later
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:23:2: this exit leaves 'later' neither awaited nor cancelled, and the coroutine of the `async` call at 20:20 still holds the data it was given: `await later` or call `later.cancel()` before this exit
```

<!-- test: a-tied-promise-held-in-a-field-is-refused -->
A tied promise lives only in a local, so storing it in a field is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Pending
	var later as TallyPromise

	static function of(later TallyPromise) returns Self
		return Self{later: later}
	end 'of'
end 'Pending'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	var row = Row.create(1)
	let held = Pending.of(async bump(row))
	_ = held
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:29:21: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-mutable-isolated-parameter-moved-out-of-the-callee-is-refused -->
A `mutable isolated` parameter is by reference, so the callee may not move it out: the caller's variable would name a region another owner holds.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function takeOut(box mutable isolated Box) returns isolated Box
	return box
end 'takeOut'

function main() returns ExitCode
	var box = Box.create(1) as isolated Box
	let taken = takeOut(box)
	_ = taken
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:13:2: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: a-for-loop-over-a-plain-array-may-push-to-it -->
A `for` over a plain array that the loop pushes to is accepted; the iterator holds its buffer and re-checks the count.

```maxon
typealias LineArray = Array with String

function main() returns ExitCode
	let items = LineArray.create(0)
	items.push("a")

	for item in items 'each'
		if items.count() < 3 'grow'
			items.push(item)
		end 'grow'
	end 'each'

	print("{items.count()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: the-array-a-for-loop-drains-is-read-only-in-the-body -->
`for` drains `pending`, moving each promise out, so a push to `pending` inside the body writes the array the loop is draining.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias PromiseArray = Array with TallyPromise

function tick() returns Tally
	sleep(1)
	return 1
end 'tick'

function main() returns ExitCode
	var pending = PromiseArray.create(0)
	pending.push(async tick())

	for p in pending 'drain'
		pending.push(async tick())
		let n = await p
		print("{n}\n")
	end 'drain'

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:16:11: cannot write through a read-only value while 'drain' drains it
```

<!-- test: a-tied-promise-awaited-before-the-function-returns-is-accepted -->
The promise is tied to `row`, held in a local and awaited unconditionally, which is the one shape a tied promise may take.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	let row = Row.create(1)
	let later = async bump(row)
	let n = await later
	print("{n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-tied-promise-passed-to-a-function-is-refused -->
A promise argument always moves, so a tied promise passed to any parameter is kept by the callee and refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function finish(later TallyPromise) returns Tally
	return await later
end 'finish'

function main() returns ExitCode
	var row = Row.create(1)
	let later = async bump(row)
	let n = finish(later)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:26:10: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-tied-promise-captured-by-a-closure-is-refused -->
A closure that captures a tied promise would carry the coroutine out of the function, so the capture is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	var row = Row.create(1)
	let later = async bump(row)
	let wait = function() gives await later
	print("{wait()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:21:30: a promise cannot be borrowed through 'await': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. This one is a closure's capture: the closure owns it for as long as the closure lives, and its body may run any number of times, so no run of it may give the promise away. Await it in the function that spawned it, or hand the closure what it needs as a parameter
```

<!-- test: a-tied-promise-pushed-into-an-array-is-refused -->
A container holds its promises past the statement, so a tied promise cannot be pushed into one.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias PromiseArray = Array with TallyPromise

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	var row = Row.create(1)
	var pending = PromiseArray.create(0)
	pending.push(async bump(row))
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:23:10: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-conditional-await-does-not-drain-a-tied-promise -->
<!-- unsupported-targets: wasm32-wasi -->
The `await` runs in one `cond` arm only, so on the other path the function returns with the coroutine still holding `row`.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	let row = Row.create(1)
	let later = async bump(row)

	if row.n > 5 'sometimes'
		let n = await later
		print("{n}\n")
	end 'sometimes'

	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:22:2: this exit leaves the tied promise of the `async` call at 20:20 neither awaited nor cancelled: `await` it or call `.cancel()` on it before this exit
```

<!-- test: a-function-that-writes-the-drained-array-is-refused -->
`grow` pushes to the array `drain` is draining, so the call inside the body is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias PromiseArray = Array with TallyPromise

function tick() returns Tally
	sleep(1)
	return 1
end 'tick'

function grow(items PromiseArray)
	items.push(async tick())
end 'grow'

function main() returns ExitCode
	var pending = PromiseArray.create(0)
	pending.push(async tick())

	for p in pending 'drain'
		grow(pending)
		let n = await p
		print("{n}\n")
	end 'drain'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:8: 'grow' needs a writable parameter 'items' at line 12; 'pending' is read-only while 'drain' is open
```

<!-- test: clearing-the-drained-array-is-refused -->
The body clears the array it drains, which is a write the loop does not allow.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally
typealias PromiseArray = Array with TallyPromise

function tick() returns Tally
	sleep(1)
	return 1
end 'tick'

function main() returns ExitCode
	var pending = PromiseArray.create(0)
	pending.push(async tick())

	for p in pending 'drain'
		pending.clear()
		let n = await p
		print("{n}\n")
	end 'drain'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:16:3: 'clear' needs a writable receiver at line 306; 'pending' is read-only while 'drain' is open
```

<!-- test: a-mutable-isolated-parameter-aliased-by-a-let-is-refused -->
A `mutable isolated` parameter is by reference, so binding it to a second name aliases a region the caller still owns.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function alias(box mutable isolated Box) returns Tally
	let other = box
	return other.n
end 'alias'

function main() returns ExitCode
	var box = Box.create(1) as isolated Box
	print("{alias(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:13:14: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: a-mutable-isolated-parameter-pushed-into-an-array-is-refused -->
Pushing the `mutable isolated` parameter into an array moves it out of the caller's variable.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function stash(box mutable isolated Box, all BoxArray)
	all.push(box)
end 'stash'

function main() returns ExitCode
	var box = Box.create(1) as isolated Box
	var all = BoxArray.create(0)
	stash(box, all: all)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:15:11: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: a-mutable-isolated-parameter-passed-to-a-keeping-parameter-is-refused -->
`adopt` takes its `isolated` parameter by move, so handing it the caller's by-reference region would give two owners.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function adopt(box isolated Box) returns Tally
	return box.n
end 'adopt'

function relay(box mutable isolated Box) returns Tally
	return adopt(box)
end 'relay'

function main() returns ExitCode
	var box = Box.create(1) as isolated Box
	print("{relay(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:17:15: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: a-mutable-isolated-parameter-stored-in-a-field-is-refused -->
Storing the `mutable isolated` parameter in a field leaves the caller's variable naming a region the field now owns.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	export var held as isolated Box

	static function create() returns Self
		return Self{held: Box.create(0)}
	end 'create'

	function keep(box mutable isolated Box)
		self.held = box
	end 'keep'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	var box = Box.create(1) as isolated Box
	shelf.keep(box)
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:20:15: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: the-array-a-for-loop-drains-is-read-only-for-unique-elements -->
`for` moves each `Ticket` out of `tickets`, so a push to `tickets` inside the body writes the array being drained.

```maxon
typealias Serial = int(0 to u64.max)

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

typealias TicketArray = Array with Ticket

function main() returns ExitCode
	var tickets = TicketArray.create(0)
	tickets.push(Ticket.create(1))

	for ticket in tickets 'drain'
		tickets.push(Ticket.create(2))
		print("{ticket.serial}\n")
	end 'drain'

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:19:11: cannot write through a read-only value while 'drain' drains it
```

<!-- test: a-mutable-isolated-parameter-relayed-to-a-mutable-isolated-parameter-is-accepted -->
Passing the by-reference region on to another `mutable isolated` parameter lends it again; nothing is moved out, aliased or kept.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function peek(box mutable isolated Box) returns Tally
	return box.n
end 'peek'

function relay(box mutable isolated Box) returns Tally
	return peek(box)
end 'relay'

function main() returns ExitCode
	var box = Box.create(1) as isolated Box
	print("{relay(box)}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-mutable-isolated-method-parameter-relayed-to-the-same-method-is-accepted -->
A method's `mutable isolated` parameter lends the caller's region again to the next call, including its own recursion; nothing is moved out, aliased or kept.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Ledger
	var floor as Tally

	static function create(floor Tally) returns Self
		return Self{floor: floor}
	end 'create'

	function record(rows mutable isolated TallyArray, depth Tally)
		rows.push(self.floor + depth)

		if depth > 0 'deeper'
			self.record(rows, depth: depth - 1)
		end 'deeper'
	end 'record'
end 'Ledger'

function main() returns ExitCode
	let ledger = Ledger.create(10)
	var rows = TallyArray.create(0) as isolated TallyArray
	ledger.record(rows, depth: 2)
	let frozen = freeze rows
	print("{frozen.count()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-mutable-isolated-method-parameter-passed-to-an-isolated-method-parameter-is-refused -->
A method parameter that is not by reference takes the region, so the caller's variable would then name a region another owner holds.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Desk
	var floor as Tally

	static function create(floor Tally) returns Self
		return Self{floor: floor}
	end 'create'

	function adopt(box isolated Box) returns Tally
		return box.n + self.floor
	end 'adopt'

	function relay(box mutable isolated Box) returns Tally
		return self.adopt(box)
	end 'relay'
end 'Desk'

function main() returns ExitCode
	let desk = Desk.create(1)
	var box = Box.create(1) as isolated Box
	print("{desk.relay(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:24:21: a `mutable isolated` parameter is by reference: the callee may open, swap or replace it but never move it out, alias it, store it or pass it on, because the caller's variable would then name a region another owner holds
```

<!-- test: a-tied-promise-may-live-in-a-local-while-the-caller-keeps-using-the-shared-data -->
Sharing is legal in locals: the caller keeps using `row`, cancels the promise before it returns early on one path, and awaits it on the other.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	let row = Row.create(1)
	let later = async bump(row)
	let before = row.n

	if before > 100 'tooBig'
		later.cancel()
		return 3
	end 'tooBig'

	let n = await later
	print("{n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-local-whose-last-use-is-the-async-argument-is-still-tied -->
A plain record local has no move: `row` stays readable and other names may alias it, so handing it to `async` as its last use does not untie the promise, and returning that promise is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function start() returns TallyPromise
	var row = Row.create(1)
	return async bump(row)
end 'start'

function main() returns ExitCode
	let later = start()
	print("{await later}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:21:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: an-await-in-one-match-arm-does-not-drain-a-tied-promise -->
<!-- unsupported-targets: wasm32-wasi -->
The `await` is the whole body of the `loud` arm, which opens no scope of its own, so only the control flow shows that the `quiet` path leaves the function with the coroutine still holding `row`.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

union Mode
	quiet
	loud
end 'Mode'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function main() returns ExitCode
	let row = Row.create(1)
	let later = async bump(row)
	let mode = Mode.loud

	match mode 'which'
		quiet then print("quiet\n")
		loud then _ = await later
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:28:2: this exit leaves the tied promise of the `async` call at 25:20 neither awaited nor cancelled: `await` it or call `.cancel()` on it before this exit
```

<!-- test: an-early-return-without-an-await-or-cancel-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The `return 0` leaves `leave` while the coroutine still holds `row`, so it must `await` or `.cancel()` the promise first.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function leave(row Row, early bool) returns Tally
	let later = async bump(row)

	if early 'leavingEarly'
		return 0
	end 'leavingEarly'

	return await later
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	_ = leave(row, early: true)
	print("{row.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:22:3: this exit leaves 'later' neither awaited nor cancelled, and the coroutine of the `async` call at 19:20 still holds the data it was given: `await later` or call `later.cancel()` before this exit
```

<!-- test: a-propagating-try-between-the-async-and-the-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The failed `try` leaves `leave` with the coroutine still holding `row`, so the exit must `await` or `.cancel()` the promise first.

```maxon
typealias Tally = int(0 to u64.max)

enum Refusal implements Error
	refused
end 'Refusal'

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function check(fail bool) returns Tally throws Refusal
	if fail 'failing'
		throw Refusal.refused
	end 'failing'

	return 1
end 'check'

function leave(row Row, fail bool) returns Tally throws Refusal
	let later = async bump(row)
	let extra = try check(fail)
	return extra + await later
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	let total = try leave(row, fail: true) otherwise 0
	print("{total} {row.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:32:14: this exit leaves 'later' neither awaited nor cancelled, and the coroutine of the `async` call at 31:20 still holds the data it was given: `await later` or call `later.cancel()` before this exit
```

<!-- test: an-early-return-cancels-the-tied-coroutine-and-waits-for-it -->
`leave` starts `bump`, lets it begin, then `.cancel()`s it before returning early. The cancel ends `bump`'s sleep and waits for it to finish, so `main`, the caller's caller, reads the completed write.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function leave(row Row, early bool) returns Tally
	let later = async bump(row)
	Scheduler.yield()

	if early 'leavingEarly'
		later.cancel()
		return 0
	end 'leavingEarly'

	return await later
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	_ = leave(row, early: true)
	print("{row.n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: a-throw-exit-cancels-the-tied-coroutine-and-waits-for-it -->
`leave` cancels the started coroutine before it throws, so the error reaches `main` only after `bump` finished its write.

```maxon
typealias Tally = int(0 to u64.max)

enum Refusal implements Error
	refused
end 'Refusal'

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function leave(row Row, fail bool) returns Tally throws Refusal
	let later = async bump(row)
	Scheduler.yield()

	if fail 'failing'
		later.cancel()
		throw Refusal.refused
	end 'failing'

	return await later
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	let attempt = try leave(row, fail: true) otherwise 0
	print("{attempt} {row.n}\n")
	return 0
end 'main'
```
```stdout
0 2
```
```exitcode
0
```

<!-- test: cancelling-a-tied-coroutine-that-never-started-leaves-the-data-unwritten -->
The coroutine is queued and `later.cancel()` runs before it was ever switched in, so it never runs: the write is not made then, and not after the cancel returned either, however long the caller waits.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function leave(row Row)
	let later = async bump(row)
	later.cancel()
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	leave(row)
	print("{row.n}\n")
	sleep(20)
	Scheduler.yield()
	print("{row.n}\n")
	return 0
end 'main'
```
```stdout
1
1
```
```exitcode
0
```

<!-- test: cancelling-a-tied-coroutine-that-yielded-waits-for-it-to-finish -->
The coroutine ran up to its own `Scheduler.yield()` and is ready again when `later.cancel()` runs. The cancel waits for it to run out, so the write is complete when the cancel returns and nothing is written afterwards.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	Scheduler.yield()
	row.n = row.n + 1
	return row.n
end 'bump'

function leave(row Row)
	let later = async bump(row)
	Scheduler.yield()
	later.cancel()
end 'leave'

function main() returns ExitCode
	let row = Row.create(1)
	leave(row)
	print("{row.n}\n")
	sleep(20)
	Scheduler.yield()
	print("{row.n}\n")
	return 0
end 'main'
```
```stdout
2
2
```
```exitcode
0
```

<!-- test: many-tied-coroutines-cancelled-before-and-after-they-start-never-write-late -->
Alternate laps cancel the coroutine before it starts and after it ran once and yielded. After every cancel returns the counter holds exactly the writes of the laps that let it start, and a final wait adds nothing.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	Scheduler.yield()
	row.n = row.n + 1
	return row.n
end 'bump'

function lap(row Row, started bool)
	let later = async bump(row)

	if started 'letItStart'
		Scheduler.yield()
	end 'letItStart'

	later.cancel()
end 'lap'

function main() returns ExitCode
	let row = Row.create(0)
	var expected = 0 as Tally
	var started = false

	for i in 0 upto 200 'laps'
		started = not started
		lap(row, started: started)

		if started 'wrote'
			expected = expected + 1
		end 'wrote'

		if row.n != expected 'late'
			print("late write at lap {i}: {row.n} != {expected}\n")
			return 1
		end 'late'
	end 'laps'

	sleep(20)
	Scheduler.yield()
	print("{row.n} {expected}\n")
	return 0
end 'main'
```
```stdout
100 100
```
```exitcode
0
```

<!-- test: an-async-over-a-construction-that-embeds-the-callers-mutable-local-is-tied -->
`Handler.create` stores the caller's `server`, and `main` reads `server` again after the coroutine ran, so the coroutine holds data the caller still uses: the promise is tied and may not be stored in `wakes`.

```maxon
typealias Tally = int(0 to 1024)
typealias TallyPromise = Promise with Tally
typealias TallyPromises = Array with TallyPromise

type Server
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Server'

type Handler
	var server as Server
	let port as Tally

	static function create(port Tally, server Server) returns Handler
		return Self{server: server, port: port}
	end 'create'

	static function serve(handler Handler) returns Tally
		sleep(1)
		handler.server.n = handler.server.n + 1
		return handler.server.n
	end 'serve'
end 'Handler'

function main() returns ExitCode
	var server = Server.create(1)
	var wakes = TallyPromises.create(0)
	wakes.push(async Handler.serve(Handler.create(3, server: server)))
	let ready = wakes.awaitAny()
	let woken = try wakes.remove(ready) otherwise panic("main: awaitAny answered a slot of this array")
	let n = await woken
	print("{n} {server.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:32:8: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: an-async-over-a-shared-value-clone-is-untied -->
A `SharedValue` is the designed way to hand a coroutine a cell both sides read, so a second holder passed to `async` does not tie the promise and the promise may be stored.

```maxon
typealias Tally = int(0 to 1024)
typealias TallyPromise = Promise with Tally
typealias TallyPromises = Array with TallyPromise
typealias Cell = SharedValue with Tally
typealias Cells = Array with Cell

function drain(spoke Cell) returns Tally
	sleep(1)
	return spoke.current()
end 'drain'

function main() returns ExitCode
	let cells = Cells.create(0)
	let five = 5 as Tally
	cells.push(Cell.create(five))
	let wakes = TallyPromises.create(0)
	wakes.push(async drain((try cells.get(0) otherwise panic("main: one cell was pushed")).clone()))
	let woken = try wakes.pop() otherwise panic("main: one promise was pushed")
	let n = await woken
	print("{n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: an-async-over-a-reader-taken-from-a-field-is-tied -->
`take` is a method of `slot.child`, which the caller can still reach and write, so the `Reader` it builds is not a clean input: the promise is tied and may not be stored in `wakes`.

```maxon
typealias Tally = int(0 to 1024)
typealias TallyPromise = Promise with Tally
typealias TallyPromises = Array with TallyPromise

type Reader
	var h as Tally

	static function over(h Tally) returns Reader
		return Self{h: h}
	end 'over'

	function next() returns Tally
		return self.h
	end 'next'
end 'Reader'

type Child
	export var handle as Tally

	static function create() returns Child
		return Self{handle: 4}
	end 'create'

	function take() returns Reader
		return Reader.over(self.handle)
	end 'take'
end 'Child'

type Slot
	export var child as Child

	static function create() returns Slot
		return Self{child: Child.create()}
	end 'create'
end 'Slot'

function drain(reader Reader) returns Tally
	sleep(1)
	return reader.next()
end 'drain'

function main() returns ExitCode
	var slot = Slot.create()
	var wakes = TallyPromises.create(0)
	wakes.push(async drain(slot.child.take()))
	let woken = try wakes.pop() otherwise panic("main: one promise was pushed")
	let n = await woken
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:46:8: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: cancelling-a-joined-tied-promise-reports-the-borrow-alone -->
<!-- unsupported-targets: wasm32-wasi -->
`settle` cancels the join of two tied promises, which is refused as a borrow of a promise no frame owns. The refused body is unknown, so no wait is reported about the call to `settle` inside the window.

```maxon
typealias Tally = int(0 to 1024)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Holder
	export var row as isolated Row

	static function create() returns Self
		return Self{row: Row.create(0)}
	end 'create'
end 'Holder'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1

	return row.n
end 'bump'

function settle(row Row, flag bool)
	let first = async bump(row)
	let second = async bump(row)
	let chosen = first if flag else second
	chosen.cancel()
end 'settle'

function main() returns ExitCode
	var holder = Holder.create()

	modify holder.row 'window'
		settle(holder.row, flag: true)
	end 'window'

	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:31:9: a promise cannot be borrowed through 'cancel': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: a-function-with-an-abandoned-body-called-inside-a-modify-window-reports-only-the-syntax-error -->
`settle` is abandoned at its syntax error, so whether it waits is unknown: the call to it inside the window is not
reported as one that may wait. `other` is an independent error and stays reported.

```maxon
typealias Tally = int(0 to 1024)
typealias LineArray = Array with String

function settle(n Tally) returns Tally
	let total = n + )
	return total
end 'settle'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function main() returns ExitCode
	let batch = LineArray.create(0) as isolated LineArray

	modify batch 'window'
		let n = settle(3)
		batch.push("{n}")
	end 'window'

	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:6:18: Expected expression but got ')'
error E2004: <fragment>:11:11: Cannot operate on Tally and String
```

<!-- test: a-generic-callee-given-to-async-ties-its-promise -->
<!-- unsupported-targets: wasm32-wasi -->
`work` is generic, so the call is to an instance of it, and the promise of `async work(1, row: row)` is tied to `row` as the promise of a plain callee is.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function work(_ T, row Row) uses T returns Tally
	sleep(1)

	return row.n
end 'work'

function start(row Row) returns TallyPromise
	return async work(1, row: row)
end 'start'

function main() returns ExitCode
	var row = Row.create(1)
	let later = start(row)
	let n = await later
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:20:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: an-isolated-two-field-reader-handed-to-a-coroutine-stays-untied -->
<!-- unsupported-targets: wasm32-wasi -->
`Reader.create` declares `returns isolated` and `drain` takes it as an `isolated` parameter, which moves it, so the coroutine holds nothing the caller can write whatever the record's layout, and the promise may be stored.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Reader
	export let handle as Tally
	export let tag as Tally

	static function create(handle Tally, tag Tally) returns isolated Reader
		return Self{handle: handle, tag: tag}
	end 'create'
end 'Reader'

typealias PromiseArray = Array with TallyPromise

function drain(reader isolated Reader) returns Tally
	sleep(1)

	return reader.handle + reader.tag
end 'drain'

function dispatch(drains mutable PromiseArray)
	let reader = Reader.create(1, tag: 2)
	let drained = async drain(reader)
	drains.push(drained)
end 'dispatch'

function main() returns ExitCode
	let drains = PromiseArray.create(0)
	dispatch(drains)

	for drained in drains 'each'
		print("{await drained}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: overwriting-a-tied-promise-in-a-loop-without-an-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The assignment drops the promise the loop header carries, so every overwrite of an unawaited tied promise is refused as an exit is.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(5)
	n = n + 1

	return k
end 'bump'

function run(a mutable Tally, b mutable Tally) returns Tally
	var promise = async bump(a, k: 1)
	var lap = 0 as Tally

	while lap < 2 'laps'
		promise = async bump(b, k: 2)
		lap = lap + 1
	end 'laps'

	let k = await promise

	return k
end 'run'

function main() returns ExitCode
	var a = 5 as Tally
	var b = 6 as Tally
	let r = run(a, b: b)
	print("{a} {b} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:16:3: this exit leaves the tied promise of the `async` call at 12:22 neither awaited nor cancelled: `await` it or call `.cancel()` on it before this exit
```

<!-- test: a-tied-promise-reassigned-through-a-branch-is-awaited-after-the-join -->
<!-- unsupported-targets: wasm32-wasi -->
The promise `await` reads is the join of the two the branch chose, and it is the one the scope's drop reaches too.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(5)
	n = n + 1

	return k
end 'bump'

function run(flag bool, a mutable Tally, b mutable Tally) returns Tally
	var promise = async bump(a, k: 1)

	if flag 'second'
		promise.cancel()
		promise = async bump(b, k: 2)
	end 'second'

	let k = await promise

	return k
end 'run'

function main() returns ExitCode
	var a = 5 as Tally
	var b = 6 as Tally
	let r = run(true, a: a, b: b)
	print("{a} {b} {r}\n")
	return 0
end 'main'
```
```stdout
5 7 2
```
```exitcode
0
```
