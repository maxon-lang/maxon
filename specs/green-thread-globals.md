---
feature: green-thread-globals
status: experimental
keywords: [spawn, services, globals, module-level, data-race, green-threads, handler, concurrency, MAXON_MAX_PROCS]
category: concurrency
---

# A service's messages touch no module-level or `static` `var`

## Documentation

⚖ **A MODULE-LEVEL `var` IS ONE WORD, AND A SERVICE MESSAGE RUNS ON A GREEN THREAD THE SCHEDULER MAY PUT ON
ANY OS THREAD.** So code a message can run — the message itself, and every function it calls, through a
closure value or a witness dispatch included — neither reads nor writes a module-level `var` or a `static var`
(E3143). The refusal is anchored at the ACCESS, names the message that reaches it, and carries a note at the
`spawn` that makes its type a service. **A module-level `let` and a `static let` stay legal**, and they are the
escape hatch: a word written once before any green thread exists has no second writer to race.

```text
error E3143: <fragment>:12:3: the message `Counter.add` uses the module-level `var total` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `total` a `let`
note: <fragment>:17:10: the `spawn` that makes `Counter` a service
```

A `static var` is named with its type: ``the static `var Counter.count` ``.

**The rule asks what a message can run, not whether the program spawns.** A `var` that only `main` and the
functions `main` calls touch is legal in a program that spawns services — `Log`'s sink included — because
`main` and its own coroutines are one strand. A function a message reaches by name, one a closure call in the
message could land on, and one a witness dispatch could land on are all refused alike, because a `var` any
of them touches is a `var` another OS thread can be using.

### What makes a type a service

| shape | a service |
|---|---|
| `spawn T.create()` anywhere in the program | **yes** |
| a registry `default Key = spawn …` the program reaches | **yes** |
| a call into `Log` — `default LogSink` spawns the log handler | **yes** |
| `async f()` with no service anywhere | **no** — a coroutine shares its spawner's strand |

### The two hazards

**A WRITE LOSES UPDATES.** A service whose `export` handler does `done = done + 1` on a module-level `var`,
driven to a fixed total of 1200 sends, run ten times at `MAXON_MAX_PROCS=16`:

| run | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| `done` | 1200 | 1200 | 1199 | 1199 | 1198 | 1199 | 1199 | 1200 | 1199 | 1200 |

Five of ten runs lose an update, and every one of them exits 0: `done = done + 1` is a load, an add and a
store, and two Ms that interleave those six steps write each other's stale value back. There is no moment at
which a runtime check could notice, so the cure is a compile error.

**A READ FOLLOWS A FREED RECORD.** A write to a module `var` releases the record the slot held before it
stores the new one, so a green thread that has already loaded the pointer reads on into freed memory. Twelve
services spinning on `label.count()` while `main` runs `label = "bravo-{r}"` four thousand times:

| processors | outcome |
|---|---|
| `MAXON_MAX_PROCS=1` | **8 of 8 clean** |
| `MAXON_MAX_PROCS=16` | **20 of 20 exit 139**, several panicking inside `String.count` → `utf8DecodeAt` on a record freed under them |

At one processor every green thread runs on the one M and the steps cannot interleave; `DefaultMaxProcs` is
more than one.

⇒ **The cure is always available: keep the value in a field of the service that owns it and hand it back
through a reply, pass it as an argument, or make it a `let`.** A reply is a `Promise` the awaiter owns, so
the answer does not depend on how many processors serviced the work (`specs/sched-default-procs.md`).

### What a service program may hold

| shape | verdict |
|---|---|
| a handler writes `self.<field>` | **legal** — a service's fields are reached by one green thread |
| a handler reads a module-level `let` | **legal** — a `let` record `__module_init` builds is marked shared before `main`, so the counts every handler steps on it are atomic |
| a handler — or a function one reaches — reads a `let` whose graph no share walk can mark (an OS handle) | **refused** (E3163) — nothing could make the counts the handlers step on it atomic |
| the same `let`, read only by functions no handler reaches | **legal** — only `main` counts it, so it stays unmarked |
| a global initializer reaches a `spawn` | **refused** (E3164) — the `let`s are marked only once the last initializer returns, so no other green thread may exist before then |
| a `let` initializer reaches a module-level `var` holding a record | **refused** (E3165, `static-variables.md`), spawn or no spawn |
| a `var` initializer may be handed a `let`'s record, or one holding it | **refused** (E3078, `static-variables.md`), spawn or no spawn |
| a `var` only `main` and the functions it calls touch, in a program that spawns | **legal** — no other thread can reach it |
| the same `var` read or written by a handler, or by a function a handler reaches | **refused** (E3143) |

⚠ **THE `error.*` CASES EXCLUDE wasm32-wasi FOR A REASON ABOUT THE DIAGNOSTIC, NOT THE RULE.** On wasm the
same program earns E3104 for `__svc_spawn` and `__mbox_send` first, so the pinned stderr is not what the
compiler emits there. Every accepted case that spawns is left unmarked: wasm32-wasi answers it with E3104,
which the harness counts as a SKIP naming the case.

## Tests

<!-- test: error.a-module-var-a-handler-writes -->
<!-- unsupported-targets: wasm32-wasi -->
**THE WRITE HAZARD'S PROGRAM.** `Counter.add` is a message, and `total = total + by` is the load/add/store
two Ms can interleave. The refusal is anchored at the ACCESS — the line the cure changes — and the
`spawn` rides as the note, because it is what makes `Counter` a service.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var total = 0 as Integer

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		total = total + by
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(5)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:12:11: the message `Counter.add` uses the module-level `var total` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `total` a `let`
note: <fragment>:17:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-module-var-a-function-called-by-a-handler-writes -->
<!-- unsupported-targets: wasm32-wasi -->
`Counter.add` names no global; it calls `record`, and `record` stores. The refusal names the message that
reaches the access: the race does not care which frame the store sits in.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var total = 0 as Integer

function record(by Integer)
	total = total + by
end 'record'

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		record(by)
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(5)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:5:10: `record` uses the module-level `var total`, and the message `Counter.add` reaches it — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `total` a `let`
note: <fragment>:21:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-module-var-only-a-closure-call-in-a-handler-could-reach -->
<!-- unsupported-targets: wasm32-wasi -->
`Counter.add` calls a closure value through `callIndirect`, and `bookKeeping`, whose address `main` takes,
writes `ledger`. No named call runs from the message to the write, and the program is refused all the same:
a target chosen at run time may land on any function whose address the program takes, so the rule reads the
message as reaching all of them.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var ledger = 0 as Integer

typealias Step = function(Integer) returns Integer

function bookKeeping(by Integer) returns Integer
	ledger = ledger + by
	return ledger
end 'bookKeeping'

function callIndirect(f Step, n Integer) returns Integer
	return f(n)
end 'callIndirect'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		return callIndirect(function(n Integer) gives n * 2, n: by)
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	let l = callIndirect(bookKeeping, n: 1)
	print("n={n} l={l}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:7:11: `bookKeeping` uses the module-level `var ledger`, and the message `Counter.add` dispatches through a closure or a witness whose target this compiler cannot name, so it may land here — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `ledger` a `let`
note: <fragment>:28:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-module-var-a-handler-reads -->
<!-- unsupported-targets: wasm32-wasi -->
**THE READ HAZARD'S PROGRAM.** Twelve `Reader.size` messages spin on `label.count()` while `main` stores
four thousand interpolated strings into `label`; at `MAXON_MAX_PROCS=16` the program would exit 139 reading
records the stores freed. A READ is refused like a write.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
typealias ReaderHandleArray = Array with Reader.handle
typealias IntPromise = Promise with (Integer, ServiceError)
typealias IntPromiseArray = Array with IntPromise

var label = "alpha"

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function size() returns Integer
		var odd = 0 as GraphemeIndex
		var i = 0 as Count
		while i < 4000 'spin'
			let c = label.count()
			if c < 5 or c > 40 'implausible'
				odd = c
			end 'implausible'
			i = i + 1
		end 'spin'
		return odd
	end 'size'
end 'Reader'

function main() returns ExitCode
	var hs = ReaderHandleArray.create()
	var i = 0 as Count
	while i < 12 'sp'
		hs.push(spawn Reader.create())
		i = i + 1
	end 'sp'

	var ps = IntPromiseArray.create()
	var k = 0 as Count
	while k < 12 'send'
		view h = try hs.get(k) otherwise panic("get")
		ps.push(h.size())
		k = k + 1
	end 'send'

	// Reassign while the readers are already running on worker Ms.
	var r = 0 as Count
	while r < 4000 'churn'
		label = "bravo-{r}"
		r = r + 1
	end 'churn'

	var worst = 0 as Integer
	var a = 0 as Count
	while a < 12 'collect'
		let p = try ps.get(a) otherwise panic("get p")
		let v = try await p otherwise 0
		if v != 0 'sawOdd'
			worst = v
		end 'sawOdd'
		a = a + 1
	end 'collect'

	print("worst={worst}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:19:12: the message `Reader.size` uses the module-level `var label` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `label` a `let`
note: <fragment>:33:11: the `spawn` that makes `Reader` a service
```

<!-- test: error.a-module-var-used-as-a-shared-empty-anchor -->
<!-- unsupported-targets: wasm32-wasi -->
**THE SHAPE A COPY-ON-WRITE ANCHOR TAKES.** `holdsNothing` asks whether a column IS the shared empty
`noEntries` — an identity test, which reads the slot — two calls below a message. In a service program the
anchor belongs to the work rather than the module, which is why the compiler's own parser holds its anchors
per parser (`ParserAnchors`).
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var noEntries = IntArray.create()

function holdsNothing(column IntArray) returns bool
	return column is noEntries
end 'holdsNothing'

function entryCount(column IntArray) returns Integer
	if holdsNothing(column) 'shared'
		return 0
	end 'shared'

	return column.count()
end 'entryCount'

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function count(by Integer) returns Integer
		var column = IntArray.create()
		column.push(by)
		return entryCount(column)
	end 'count'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.count(5) otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
```
```maxoncstderr
error E3143: <fragment>:5:19: `holdsNothing` uses the module-level `var noEntries`, and the message `Counter.count` reaches it — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `noEntries` a `let`
note: <fragment>:31:10: the `spawn` that makes `Counter` a service
```

<!-- test: a-handler-may-write-its-own-field -->
**THE OVER-REFUSAL GUARD THAT MATTERS MOST — the cure the two refusals above prescribe must itself
compile.** `self.count = self.count + by` is a store to a word owned by ONE green thread: the service's
own. There is no second writer at any processor count, which is the language guarantee the whole
plain-refcount design already rests on, so nothing here is refused and nothing needs to be.

The write is observed through an AWAITED REPLY rather than through a print inside the handler, deliberately:
a reply is what carries a value back to a party that can order it, and it is the one channel that stays
correct when the default is every processor. Two sends, then one await — so the exit code is the sum and a
lost send would read 3 or 4 rather than 7.
```maxon
type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + by
	end 'add'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(3)
	h.add(4)
	let n = try await h.total() otherwise 0
	print("count={n}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
count=7
```
```exitcode
7
```

<!-- test: a-handler-may-sort-its-own-field -->
**THE STANDARD LIBRARY'S LARGEST ALGORITHM RUNS ON A MESSAGE.** `Array.sort` reaches no module storage, so
sorting a field of `self` is the cure this rule prescribes rather than something it refuses. All four entries
run — both `Comparable` overloads, which pass a comparator closure through the extension's witness slot, and
both explicit-comparator ones — over forty elements, which is past the small-sort short-circuit, so driftsort's
run stack and pdqsort's pivot machinery are both on the message's green thread.

The sort is observed through an awaited reply: the lowest and highest elements summed, so an ordering that
came back wrong at either end changes the exit code.
```maxon
type Sorter
	var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	export function add(n Integer)
		self.items.push(n)
	end 'add'

	export function arrange() returns Integer
		self.items.sortUnstable()
		self.items.sort()
		self.items.sortUnstable(function(a, b) gives b.compare(a))
		self.items.sort(function(a, b) gives a.compare(b))
		let low = try self.items.get(0) otherwise 0
		let high = try self.items.get(39) otherwise 0
		return low + high
	end 'arrange'
end 'Sorter'

function main() returns ExitCode
	let h = spawn Sorter.create()
	var n = 40 as Integer

	while n > 0 'fill'
		h.add(n)
		n = n - 1
	end 'fill'

	let span = try await h.arrange() otherwise 0
	print("span={span}\n")
	return span as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
```
```stdout
span=41
```
```exitcode
41
```

<!-- test: a-handler-may-read-an-immutable-global -->
**THE RULE IS ABOUT THE WRITE, AND A `let` HAS NONE.** A module-level `let` is written once before any
green thread exists and never again, so every M that reads it reads the same word and no interleaving can
produce a different answer. A rule that refused module-level state as such — rather than the store to it —
would refuse this, and would take every configuration constant in every service program with it.
```maxon
let stride = 7

type Scaler
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function scale(by Integer) returns Integer
		return by * stride
	end 'scale'
end 'Scaler'

function main() returns ExitCode
	let h = spawn Scaler.create()
	let n = try await h.scale(6) otherwise 0
	print("scaled={n}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
scaled=42
```
```exitcode
42
```

<!-- test: every-handler-may-co-own-a-global-built-at-run-time -->
**A `let` WHOSE RECORD `__module_init` BUILDS IS READ BY EVERY THREAD TOO, AND ITS COUNT IS ONE WORD THEY ALL
STEP.** A `let` no image can hold — here a factory with a loop in it — is a counted record, and a handler that
puts it somewhere that outlives a statement takes an owner on it and gives the owner back. Eight handlers
doing that at once on different OS threads step one refcount, so the record is marked shared once it is built,
exactly as a published value is, and every count on it from then on is atomic. Unmarked, a lost increment frees the
roster under a reader and a lost decrement leaks it.
```maxon
type Roster
	export var names as NameArray

	static function create(count Integer) returns Self
		var names = NameArray.create()

		for i in 0 upto count 'fill'
			names.push("member {i}")
		end 'fill'

		return Self{names: names}
	end 'create'
end 'Roster'

let roster = Roster.create(4)

type Churner
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function churn(rounds Integer) returns Integer
		var held = 0 as Count

		for _ in 0 upto rounds 'eachRound'
			var kept = HeldNamesArray.create()
			kept.push(roster.names)
			held = held + kept.count()
		end 'eachRound'

		return held as Integer
	end 'churn'
end 'Churner'

function main() returns ExitCode
	var replies = ChurnReplyArray.create()

	for _ in 0 upto ChurnerCount 'eachChurner'
		let h = spawn Churner.create()
		replies.push(h.churn(RoundsPerChurner))
	end 'eachChurner'

	var held = 0 as Integer

	for r in replies 'eachReply'
		held = held + (try await r otherwise 0)
	end 'eachReply'

	print("held={held} names={roster.names.count()}\n")
	return 0
end 'main'

let ChurnerCount = 8
let RoundsPerChurner = 200000

typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
typealias HeldNamesArray = Array with readonly NameArray
typealias ChurnReplyArray = Array with Promise with (Integer, ServiceError)
```
```stdout
held=1600000 names=4
```
```exitcode
0
```

<!-- test: error.a-message-may-not-read-a-let-no-share-walk-can-mark -->
<!-- unsupported-targets: wasm32-wasi -->
**AND A `let` THE MARK CANNOT REACH IS REFUSED WHERE A MESSAGE READS IT.** An OS handle has a destructor and no
walk, so nothing can make the counts on a record holding one atomic. The refusal sits at the read the message
reaches, and the note is the declaration.
```maxon
type Listening
	export var listener as TcpListener

	static function open() returns Self
		let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("bind")
		return Self{listener: listener}
	end 'open'
end 'Listening'

let listening = Listening.open()

type Porter
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function port() returns Integer
		self.asked = self.asked + 1
		return listening.listener.port()
	end 'port'
end 'Porter'

function main() returns ExitCode
	let h = spawn Porter.create()
	let p = try await h.port() otherwise 0
	return (0 if p > 0 else 1) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3163: <fragment>:22:10: the message `Porter.port` reads the module-level `listening`, and it holds a `Listening` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance. A message runs on a green thread the scheduler may put on any OS thread, so the counts every message steps on `listening` must be atomic, and no share walk can mark it so. Keep it in a field of the service that reads it
note: <fragment>:11:5: `listening` is declared here
```

<!-- test: a-let-no-message-reads-may-hold-an-os-handle-beside-a-service -->
**THE SAME `let`, READ ONLY BY `main`, IS LEGAL.** No message reaches `listening`, so nothing but `main`'s green
thread counts it and it needs no mark.
```maxon
type Listening
	export var listener as TcpListener

	static function open() returns Self
		let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("bind")
		return Self{listener: listener}
	end 'open'
end 'Listening'

let listening = Listening.open()

type Doubler
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function double(n Integer) returns Integer
		self.asked = self.asked + 1
		return n * 2
	end 'double'
end 'Doubler'

function main() returns ExitCode
	let h = spawn Doubler.create()
	let d = try await h.double(21) otherwise 0
	let open = listening.listener.port() > 0
	print("d={d} open={open}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
d=42 open=true
```
```exitcode
0
```

<!-- test: lets-reaching-one-record-are-marked-through-one-visit-table -->
**ONE VISIT TABLE SERVES EVERY `let`.** `pair` holds `bag` twice and `bag`'s own slot holds it too, so the
record has three owners, all of them `let`s. Marked `let` by `let`, the second and third meetings would read
as owners no walk met; one table across every `let` counts all three and the program starts.
```maxon
type Bag
	export var n as Integer
	export var names as NameArray

	static function create() returns Self
		var names = NameArray.create()

		for i in 0 upto 3 'fill'
			names.push("name {i}")
		end 'fill'

		return Self{n: 7, names: names}
	end 'create'
end 'Bag'

type Pair
	export let left as Bag
	export let right as Bag

	static function around(bag Bag) returns Self
		return Self{left: bag, right: bag}
	end 'around'
end 'Pair'

let bag = Bag.create()
let pair = aroundBag()

function aroundBag() returns Pair
	return Pair.around(bag)
end 'aroundBag'

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return pair.right.n + (bag.names.count() as Integer)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
```
```stdout
n=10
```
```exitcode
0
```

<!-- test: error.a-spawn-a-global-initializer-reaches-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
**A GLOBAL INITIALIZER MAY NOT START A SERVICE.** `Box.start` spawns `Reader` while `box` is being built, before
`roster` is marked shared, so `Reader.read` could step `roster`'s plain count from another OS thread.
```maxon
type Roster
	export var names as NameArray

	static function create(count Integer) returns Self
		var names = NameArray.create()

		for i in 0 upto count 'fill'
			names.push("member {i}")
		end 'fill'

		return Self{names: names}
	end 'create'
end 'Roster'

let roster = Roster.create(4)

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return roster.names.count()
	end 'read'
end 'Reader'

type Box
	export var h as Reader.handle

	static function start() returns Self
		let h = spawn Reader.create()
		_ = h.read()
		return Self{h: h}
	end 'start'
end 'Box'

let box = Box.start()

function main() returns ExitCode
	let n = try await box.h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
```
```maxoncstderr
error E3164: <fragment>:35:11: `Box.start` spawns `Reader`, and a module-level global's initializer reaches it — initializers run before `main`, and the `let`s every message may read are marked shared only once the last one returns, so a service started here could step a plain count on one concurrently with the mark or read a global not yet built. Start the service in `main`, or in a function `main` calls
note: <fragment>:41:5: the initializer of `box`, which reaches it
```

<!-- test: a-module-var-only-main-touches-in-a-service-program -->
**NO MESSAGE TOUCHES `ledger`, SO IT IS LEGAL.** `bookKeeping` is reached from `main` alone, and `main` runs on
the program's own strand: no other OS thread can reach the word, whether or not the program spawns a service.
The twin below reads the same `ledger` from a handler and is refused.
```maxon
var ledger = 0 as Integer

function bookKeeping(by Integer)
	ledger = ledger + by
end 'bookKeeping'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + by
	end 'add'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	bookKeeping(10)
	h.add(5)
	let n = try await h.total() otherwise 0
	bookKeeping(n)
	print("ledger={ledger} count={n}\n")
	return ledger as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
ledger=15 count=5
```
```exitcode
15
```

<!-- test: error.the-same-module-var-read-by-a-handler-in-a-service-program -->
<!-- unsupported-targets: wasm32-wasi -->
**THE REFUSED TWIN OF THE CASE ABOVE.** The program differs only in `total`, which now reads `ledger`: a message
runs on a green thread the scheduler may put on any OS thread, while `main` writes the same word.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var ledger = 0 as Integer

function bookKeeping(by Integer)
	ledger = ledger + by
end 'bookKeeping'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + by
	end 'add'

	export function total() returns Integer
		return self.count + ledger
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	bookKeeping(10)
	h.add(5)
	let n = try await h.total() otherwise 0
	bookKeeping(n)
	print("ledger={ledger} count={n}\n")
	return ledger as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:20:23: the message `Counter.total` uses the module-level `var ledger` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `ledger` a `let`
note: <fragment>:25:10: the `spawn` that makes `Counter` a service
```

<!-- test: a-service-program-may-use-the-stdlibs-trace-capture -->
⭐⭐ **THE RULE COUNTS THE STANDARD LIBRARY'S `var`s TOO, SO THE LIBRARY CODE A MESSAGE RUNS TOUCHES NONE.**
`TraceCapture` keeps its state in a service of its own and a `SharedValue`, and this program — which spawns
`Counter` and starts and stops a capture from `main` — compiles and runs. A module `var` a message of
`TraceCapture` reached would refuse it, naming a file the author does not own.

⭐ **AND AN INTERFACE IN THE HANDLER'S CONE DOES NOT IMPLICATE ANYTHING BUT THE REQUIREMENT'S MEMBERS.** The
handler dispatches `greet` through `Greeter`, so the dispatch can land on `Polite.greet` and on no other function;
`bookKeeping` writes a module `var` and has its address taken in `main`, but it is no member of the requirement, so
no dispatch reaches it and the `var` is legal.
```maxon
var ledger = 0 as Integer

typealias Step = function(Integer) returns Integer

function bookKeeping(by Integer) returns Integer
	ledger = ledger + by
	return ledger
end 'bookKeeping'

function callIndirect(f Step, n Integer) returns Integer
	return f(n)
end 'callIndirect'

interface Greeter
	function greet() returns String
end 'Greeter'

type Polite implements Greeter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function greet() returns String
		return "hello"
	end 'greet'
end 'Polite'

function describe(g Greeter) returns String
	return g.greet()
end 'describe'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		let p = Polite.create()
		self.count = self.count + by + (describe(p).count() as Integer)
		return self.count
	end 'add'
end 'Counter'

function main() returns ExitCode
	TraceCapture.startCapture()
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	let booked = callIndirect(bookKeeping, n: n)
	let keys = TraceCapture.stopCapture()
	print("n={n} booked={booked} keys={keys.count()}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
n=10 booked=10 keys=0
```
```exitcode
10
```

<!-- test: a-module-var-beside-a-handler-that-keeps-a-map -->
The handler hashes and compares its keys through a `Map`'s witness slots and calls no closure; `bookKeeping`,
whose address `main` takes, writes `ledger`. Nothing a message runs can land on `bookKeeping`, so the `var` is
legal.
```maxon
var ledger = 0 as Integer

typealias Step = function(Integer) returns Integer

function bookKeeping(by Integer) returns Integer
	ledger = ledger + by
	return ledger
end 'bookKeeping'

function callIndirect(f Step, n Integer) returns Integer
	return f(n)
end 'callIndirect'

typealias Tally = Map with (String, Integer)

type Counter
	var counts as Tally

	static function create() returns Self
		return Self{counts: Tally.create()}
	end 'create'

	export function add(key String, by Integer) returns Integer
		let before = try self.counts.get(key) otherwise 0
		self.counts.upsert(key, value: before + by)
		return before + by
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add("apples", by: 5) otherwise 0
	let l = callIndirect(bookKeeping, n: 1)
	print("n={n} l={l}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
n=5 l=1
```
```exitcode
5
```

<!-- test: error.a-module-var-a-witness-member-writes -->
<!-- unsupported-targets: wasm32-wasi -->
The handler dispatches `greet` through an interface value, and `Loud.greet` writes `greetings`. The refusal
names the member the dispatch could land on, as for every other road to the `var`.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var greetings = 0 as Integer

interface Greeter
	function greet() returns String
end 'Greeter'

type Loud implements Greeter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function greet() returns String
		greetings = greetings + 1
		return "HELLO"
	end 'greet'
end 'Loud'

function describe(g Greeter) returns String
	return g.greet()
end 'describe'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		let loud = Loud.create()
		self.count = self.count + by + (describe(loud).count() as Integer)
		return self.count
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	print("n={n} greetings={greetings}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:16:15: `Loud.greet` uses the module-level `var greetings`, and the message `Counter.add` dispatches through a closure or a witness whose target this compiler cannot name, so it may land here — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `greetings` a `let`
note: <fragment>:40:10: the `spawn` that makes `Counter` a service
```

<!-- test: a-handler-reads-a-module-let-during-the-exit-drain -->
**THE EXIT DRAIN RUNS HANDLERS, SO EVERY MODULE `let` A HANDLER READS MUST OUTLIVE IT.** `main` sends three
messages and returns without awaiting any; the drain that runs them comes after `main`, and each handler reads
a `let` whose record `__module_init` built at run time. The globals are released only once the drain has
finished.
```maxon
type Label
	export let text as String
	export let weight as Integer

	static function create(weight Integer) returns Self
		return Self{text: "a label built at run time, weight {weight}", weight: weight}
	end 'create'
end 'Label'

let label = Label.create(40)

type Printer
	var printed as Integer

	static function create() returns Self
		return Self{printed: 0}
	end 'create'

	export function show(n Integer)
		self.printed = self.printed + 1
		print("{n}: {label.text} ({label.weight + n})\n")
	end 'show'
end 'Printer'

function main() returns ExitCode
	let printer = spawn Printer.create()
	printer.show(1)
	printer.show(2)
	printer.show(3)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
1: a label built at run time, weight 40 (41)
2: a label built at run time, weight 40 (42)
3: a label built at run time, weight 40 (43)
```
```exitcode
0
```

<!-- test: a-handler-reads-a-static-let-during-the-exit-drain -->
The same for a type's `static let`: it is a global too, and the drain's handlers read it after `main` has
returned.
```maxon
type Label
	export let text as String
	export let weight as Integer

	static let standard = Label.create(40)

	static function create(weight Integer) returns Self
		return Self{text: "a label built at run time, weight {weight}", weight: weight}
	end 'create'
end 'Label'

type Printer
	var printed as Integer

	static function create() returns Self
		return Self{printed: 0}
	end 'create'

	export function show(n Integer)
		self.printed = self.printed + 1
		print("{n}: {Label.standard.text} ({Label.standard.weight + n})\n")
	end 'show'
end 'Printer'

function main() returns ExitCode
	let printer = spawn Printer.create()
	printer.show(1)
	printer.show(2)
	printer.show(3)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
1: a label built at run time, weight 40 (41)
2: a label built at run time, weight 40 (42)
3: a label built at run time, weight 40 (43)
```
```exitcode
0
```

<!-- test: a-program-with-only-async-keeps-its-module-var -->
**`async` ALONE DOES NOT MAKE A SERVICE PROGRAM.** `triple` runs on a green thread of its own twice, and
`main` folds each awaited answer into `total`. Nothing is spawned, so `total` is a legal declaration.
```maxon
var total = 0 as Integer

function triple(n Integer) returns Integer
	sleep(1)
	return n * 3
end 'triple'

function main() returns ExitCode
	let first = async triple(5)
	let a = await first
	total = total + a
	let second = async triple(4)
	let b = await second
	total = total + b
	print("total={total}\n")
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
total=27
```
```exitcode
27
```

<!-- test: a-module-var-only-main-touches-beside-a-log-sink -->
**A PROGRAM THAT LOGS SPAWNS A SERVICE, AND ITS `var` IS STILL `main`'S.** The log handler is a service, so
`Log.info` runs on a green thread of its own, but nothing it runs touches `calls`: `main` alone counts it.
```maxon
var calls = 0 as Count

function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.info))
	Log.setDefault(LogSink.create(spawn TextLogHandler.create(options), levels: levels))
	calls = calls + 1
	Log.info("started {calls}")
	return calls as ExitCode
end 'main'
```
```stdout
level=INFO msg="started 1"
```
```exitcode
1
```

<!-- test: a-module-var-only-main-touches-beside-a-registry-default-that-spawns -->
A `default` whose value is a `spawn` starts its service before `main` once anything reaches its key, so the
program has a service. `lookups` is touched by `main` alone, so it is legal.
```maxon
var lookups = 0 as Integer

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Polite implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Polite'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	lookups = lookups + 1
	let answer = try await Greeter.current().greet() otherwise 0
	return (answer + lookups) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```

<!-- test: a-static-var-only-main-touches-in-a-program-that-spawns-a-service -->
**A `static var` IS A MODULE `var` WITH A TYPE'S NAME ON IT.** `spawn` calls the factory on the spawner's own
strand, so `Counter.created` is `main`'s and the program is legal.
```maxon
type Counter
	static var created = 0 as Integer

	var n as Integer

	static function create() returns Self
		Counter.created = Counter.created + 1
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		self.n = self.n + by
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(5)
	return Counter.created as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: error.a-static-var-a-handler-reads -->
<!-- unsupported-targets: wasm32-wasi -->
The refused twin: a handler reads the `static var`, so it is one word another OS thread uses, and it is named
with its type.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
type Counter
	static var created = 0 as Integer

	var n as Integer

	static function create() returns Self
		Counter.created = Counter.created + 1
		return Self{n: 0}
	end 'create'

	export function add(by Integer) returns Integer
		self.n = self.n + by + Counter.created
		return self.n
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:13:26: the message `Counter.add` uses the static `var Counter.created` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `Counter.created` a `let`
note: <fragment>:19:10: the `spawn` that makes `Counter` a service
```

<!-- test: a-static-let-in-a-program-that-spawns-a-service -->
The `let` twin of the case above: a `static let` is written once before any green thread exists, so a
message may read it.
```maxon
type Counter
	static let step = 6

	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(times Integer) returns Integer
		self.n = self.n + times * Counter.step
		return self.n
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(7) otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: error.a-registry-default-that-spawns-is-refused-when-its-message-touches-the-var -->
<!-- unsupported-targets: wasm32-wasi -->
A `default` whose value is a `spawn` starts its service before `main`, so a module `var` the service's own message
touches is shared by two threads. The note points at the `spawn` inside the `default`.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var lookups = 0 as Integer

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Polite implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		lookups = lookups + 1
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Polite'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	return (answer + lookups) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:16:13: the message `Polite.greet` uses the module-level `var lookups` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `lookups` a `let`
note: <fragment>:22:19: the `spawn` that makes `Polite` a service
```

<!-- test: error.a-log-sink-default-that-spawns-is-refused-when-its-handler-touches-the-var -->
<!-- unsupported-targets: wasm32-wasi -->
A program that logs spawns the handler the `LogSink` `default` names. A module `var` that handler's own message
touches is shared by two threads, and the note points at the `spawn` inside the `default`.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var calls = 0 as Integer

type CountingHandler implements LogHandler
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function handle(record LogRecord) returns bool
		calls = calls + 1
		self.seen = self.seen + 1
		return true
	end 'handle'
end 'CountingHandler'

default LogSink = LogSink.create(spawn CountingHandler.create(), levels: LogLevelVar.create(LogLevels.minimum(LogLevel.info)))

function main() returns ExitCode
	Log.info("started")
	return calls as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:12:11: the message `CountingHandler.handle` uses the module-level `var calls` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `calls` a `let`
note: <fragment>:18:34: the `spawn` that makes `CountingHandler` a service
```

<!-- test: error.a-coroutine-a-handler-starts-shares-the-handlers-strand -->
<!-- unsupported-targets: wasm32-wasi -->
A handler that runs `async` work starts a coroutine on the handler's own strand, so a module `var` the coroutine
touches is reached by a message and is refused.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
```maxon
var ledger = 0 as Integer

function book(by Integer) returns Integer
	sleep(1)
	ledger = ledger + by
	return ledger
end 'book'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		let pending = async book(by)
		let booked = await pending
		self.count = self.count + booked
		return self.count
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:6:11: `book` uses the module-level `var ledger`, and the message `Counter.add` reaches it — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares: a write can lose an update, and a read can follow a record a concurrent write has just freed. Keep it in a field of `self` and hand it back through a reply, or make `ledger` a `let`
note: <fragment>:26:10: the `spawn` that makes `Counter` a service
```
