---
feature: green-thread-globals
status: experimental
keywords: [spawn, services, globals, module-level, let, green-threads, handler, concurrency, MAXON_MAX_PROCS]
category: concurrency
---

# A service's messages read only `let` globals

## Documentation

⚖ **A SERVICE MESSAGE RUNS ON A GREEN THREAD THE SCHEDULER MAY PUT ON ANY OS THREAD, SO THE ONLY MODULE-LEVEL
STATE IT CAN REACH IS A `let`.** A module-level `var` and a `static var` do not exist
(`specs/module-var-removed.md`), and a module-level `let` and a `static let` are the shared words: a record
`__module_init` builds is marked shared before `main`, so the counts every handler steps on it are atomic, and a
word written once before any green thread exists has no second writer to race.

**The rule asks what a message can run, not whether the program spawns.** A `let` whose graph no share walk can
mark (an OS handle) is refused when the message itself, a function it reaches by name, a closure call in the
message could land on, or a witness dispatch could land on reads it (E3163); the same `let` read only by
functions no message reaches stays unmarked, because only `main` counts it.

### What makes a type a service

| shape | a service |
|---|---|
| `spawn T.create()` anywhere in the program | **yes** |
| a registry `default Key = spawn …` the program reaches | **yes** |
| a call into `Log` — `default LogSink` spawns the log handler | **yes** |
| `async f()` with no service anywhere | **no** — a coroutine shares its spawner's strand |

### What a service program may hold

| shape | verdict |
|---|---|
| a handler writes `self.<field>` | **legal** — a service's fields are reached by one green thread |
| a handler reads a module-level `let` | **legal** — a `let` record `__module_init` builds is marked shared before `main`, so the counts every handler steps on it are atomic |
| a handler — or a function one reaches — reads a `let` whose graph no share walk can mark (an OS handle) | **refused** (E3163) — nothing could make the counts the handlers step on it atomic |
| the same `let`, read only by functions no handler reaches | **legal** — only `main` counts it, so it stays unmarked |
| a global initializer reaches a `spawn` | **refused** (E3164) — the `let`s are marked only once the last initializer returns, so no other green thread may exist before then |

⇒ **Mutable state a service shares with its callers is a field of the service, reached through a reply, an
argument, or a `SharedValue`.** A reply is a `Promise` the awaiter owns, so the answer does not depend on how
many processors serviced the work (`specs/sched-default-procs.md`).

⚠ **NO CASE HERE IS MARKED FOR wasm32-wasi.** E3104 for `__cown_spawn` and `__cown_send` is reported only when
the program has no other diagnostic, so an `error.*` case prints its pinned stderr alone on wasm as on every
native lane. Every accepted case that spawns is answered with E3104, which the harness counts as a SKIP naming
the case.

## Tests

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
	try h.add(3) otherwise panic("add 3")
	try h.add(4) otherwise panic("add 4")
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
run — both `Comparable` forms, which pass a comparator closure through the extension's witness slot, and
both explicit-comparator ones — over forty elements, which is past the small-sort short-circuit, so driftsort's
run stack and pdqsort's pivot machinery are both on the message's green thread.

The sort is observed through an awaited reply: the lowest and highest elements summed, so an ordering that
came back wrong at either end changes the exit code.
```maxon
type Sorter
	var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create(0)}
	end 'create'

	export function add(n Integer)
		self.items.push(n)
	end 'add'

	export function arrange() returns Integer
		self.items.sortUnstable()
		self.items.sort()
		self.items.sortUnstableBy(function(a, b) gives b.compare(a))
		self.items.sortBy(function(a, b) gives a.compare(b))
		let low = try self.items.get(0) otherwise 0
		let high = try self.items.get(39) otherwise 0
		return low + high
	end 'arrange'
end 'Sorter'

function main() returns ExitCode
	let h = spawn Sorter.create()
	var n = 40 as Integer

	while n > 0 'fill'
		try h.add(n) otherwise panic("add")
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
		var names = NameArray.create(0)

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
			var kept = HeldNamesArray.create(0)
			kept.push(roster.names)
			held = held + kept.count()
		end 'eachRound'

		return held as Integer
	end 'churn'
end 'Churner'

function main() returns ExitCode
	var replies = ChurnReplyArray.create(0)

	for _ in 0 upto ChurnerCount 'eachChurner'
		let h = spawn Churner.create()
		replies.push(try h.churn(RoundsPerChurner) otherwise panic("churn"))
	end 'eachChurner'

	var held = 0 as Integer

	for r in replies 'eachReply'
		held = held + (await r)
	end 'eachReply'

	print("held={held} names={roster.names.count()}\n")
	return 0
end 'main'

let ChurnerCount = 8
let RoundsPerChurner = 200000

typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
typealias HeldNamesArray = Array with readonly NameArray
typealias ChurnReplyArray = Array with Promise with Integer
```
```stdout
held=1600000 names=4
```
```exitcode
0
```

<!-- test: error.a-message-may-not-read-a-let-no-share-walk-can-mark -->
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
		var names = NameArray.create(0)

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
**A GLOBAL INITIALIZER MAY NOT START A SERVICE.** `Box.start` spawns `Reader` while `box` is being built, before
`roster` is marked shared, so `Reader.read` could step `roster`'s plain count from another OS thread.
```maxon
type Roster
	export var names as NameArray

	static function create(count Integer) returns Self
		var names = NameArray.create(0)

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
		_ = try h.read() otherwise panic("read")
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

<!-- test: a-service-program-may-use-the-stdlibs-trace-capture -->
⭐⭐ **THE LIBRARY CODE A MESSAGE RUNS KEEPS ITS STATE IN A SERVICE AND A `SharedValue`.** `TraceCapture` holds
its state in a service of its own and a `SharedValue`, and this program — which spawns `Counter` and starts
and stops a capture from `main` — compiles and runs.
⭐ **AN INTERFACE IN THE HANDLER'S CONE IS DISPATCHED THROUGH ITS WITNESS.** The handler dispatches `greet`
through `Greeter`, so the dispatch lands on `Polite.greet`.
```maxon
interface Greeter
	function greet(mutable self) returns String
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
	let keys = TraceCapture.stopCapture()
	print("n={n} keys={keys.count()}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
n=10 keys=0
```
```exitcode
10
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
	try printer.show(1) otherwise panic("show 1")
	try printer.show(2) otherwise panic("show 2")
	try printer.show(3) otherwise panic("show 3")
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
	try printer.show(1) otherwise panic("show 1")
	try printer.show(2) otherwise panic("show 2")
	try printer.show(3) otherwise panic("show 3")
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
