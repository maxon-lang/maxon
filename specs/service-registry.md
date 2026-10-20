---
feature: service-registry
status: experimental
keywords: [default, registry, service, interface, handle, current, register, SharedValue, concurrency]
category: concurrency
---

# Service registry — replaceable program-wide defaults, and `SharedValue with T`

## Documentation

A service type that `implements I` is reached through `I.handle`, and a `T.handle` converts to `I.handle` at
every door. A top-level `default Key = <expression>` declares the program-wide default for `Key`: a spawn
of a service implementing the interface `Key`, or a value of the type `Key`. `Key.current()`
looks it up from any thread, and `Key.register(x)` replaces it for every later lookup. A default is built
before `main` only when something reaches its key, and the program's own default wins over the stdlib's.
`SharedValue with T` is the same live slot made at run time: `current()`, `publish(v)`, and the conditional
`publish(v, replacing: old)`.

A registry value and a `SharedValue`'s value are read by every thread at once. Their type may hold `var`
fields, `Array`s and `Map`s, and every read — `Key.current()`, `cell.current()`, or a function returning
either — hands back a deep read-only view of the published value, the same one a module-level `let`'s
record gets. Every write through it is refused: binding it, or anything reached through it, to a `var`
(E3078); calling a method that writes its receiver on it or on anything reached through it, or passing
either to a parameter the callee writes or reassigns (E3159, naming the root binding the view was read
into); storing it, or anything reached through it, into a `var` field (E3078), since the function that
later writes the field cannot see where its value came from; and a field store through a record that may
hold it (E3159). A store written on the read itself, `cell.current().inner.x = 9`, is not a statement at
all: an assignment's target begins at a binding (E2001), so every write reaches the view through one. The
view is the published record itself, so a read copies nothing; `.clone()` makes a value of one's own to
change.

A type whose graph a share walk cannot mark — one reaching an OS handle, a function value or a promise —
cannot be a registry value or a `SharedValue` (E3171). A value handed to `register` or `publish` must be
solely owned (E3174).

## Tests

<!-- test: an-interface-handle-sends-and-awaits -->
A handle at interface type sends a requirement that has no reply and awaits a returning one, exactly as the
service's own handle would.
```maxon
interface Tally
	function add(by Integer)
	function total() returns Integer
end 'Tally'

type Counter implements Tally
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		self.n = self.n + by
	end 'add'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = (spawn Counter.create()) as Tally.handle
	try h.add(4) otherwise panic("send add 4")
	try h.add(5) otherwise panic("send add 5")
	let total = try await h.total() otherwise 0
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
9
```

<!-- test: a-handle-converts-to-its-interface-at-every-door -->
A `Fixed.handle` becomes a `Source.handle` at an argument, a return, a field initializer, an array element and
a message argument; each path ends in an awaited reply, and the replies are distinct powers of two.
```maxon
interface Source
	function value() returns Integer
end 'Source'

type Fixed implements Source
	let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Fixed'

type Holder
	export let source as Source.handle

	static function create(concrete Fixed.handle) returns Self
		return Self{source: concrete}
	end 'create'
end 'Holder'

type Relay
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function ask(source Source.handle) returns Integer
		self.asked = self.asked + 1
		return try await source.value() otherwise 0
	end 'ask'
end 'Relay'

typealias SourceArray = Array with Source.handle

function viaArgument(source Source.handle) returns Integer
	return try await source.value() otherwise 0
end 'viaArgument'

function viaReturn() returns Source.handle
	return spawn Fixed.create(2)
end 'viaReturn'

function main() returns ExitCode
	let fromArgument = viaArgument(spawn Fixed.create(1))

	let returned = viaReturn()
	let fromReturn = try await returned.value() otherwise 0

	let holder = Holder.create(spawn Fixed.create(4))
	let fromField = try await holder.source.value() otherwise 0

	var sources = SourceArray.create()
	sources.push(spawn Fixed.create(8))
	view element = try sources.get(0) otherwise panic("sources.get(0)")
	let fromElement = try await element.value() otherwise 0

	let relay = spawn Relay.create()
	let fromMessage = try await relay.ask(spawn Fixed.create(16)) otherwise 0

	return (fromArgument + fromReturn + fromField + fromElement + fromMessage) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
31
```

<!-- test: a-returning-message-may-be-sent-unawaited -->
A requirement that returns a value may be sent and its reply discarded: the message still runs, in FIFO order
before the awaited read that follows it.
```maxon
interface Bumper
	function bump(by Integer) returns Integer
	function read() returns Integer
end 'Bumper'

type Counter implements Bumper
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump(by Integer) returns Integer
		self.n = self.n + by
		return self.n
	end 'bump'

	export function read() returns Integer
		return self.n
	end 'read'
end 'Counter'

function drive(b Bumper.handle) returns Integer
	_ = try b.bump(5) otherwise panic("send bump 5")
	_ = try b.bump(2) otherwise panic("send bump 2")
	return try await b.read() otherwise 0
end 'drive'

function main() returns ExitCode
	return drive(spawn Counter.create()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-service-default-is-started-before-main-and-looked-up -->
```maxon
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
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-value-default-is-built-before-main-and-looked-up -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(7)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-handler-reaches-the-registered-service-without-a-handle -->
Each worker's handler sends to the registered `Ledger` before it replies, so the `total` `main` sends after
both replies is queued behind both adds.
```maxon
interface Ledger
	function add(by Integer)
	function total() returns Integer
end 'Ledger'

type Tally implements Ledger
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		self.n = self.n + by
	end 'add'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Tally'

default Ledger = spawn Tally.create()

type Worker
	let k as Integer

	static function create(k Integer) returns Self
		return Self{k: k}
	end 'create'

	export function run() returns Integer
		try Ledger.current().add(self.k) otherwise panic("send add")
		return self.k
	end 'run'
end 'Worker'

function main() returns ExitCode
	let a = spawn Worker.create(3)
	let b = spawn Worker.create(4)
	let fromA = try await a.run() otherwise 0
	let fromB = try await b.run() otherwise 0

	if fromA + fromB != 7 'workers'
		return 1
	end 'workers'

	let total = try await Ledger.current().total() otherwise 0
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-handler-may-register -->
```maxon
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

type Rude implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 9
	end 'greet'
end 'Rude'

default Greeter = spawn Polite.create()

type Installer
	var installs as Integer

	static function create() returns Self
		return Self{installs: 0}
	end 'create'

	export function install() returns Integer
		Greeter.register(spawn Rude.create())
		self.installs = self.installs + 1
		return self.installs
	end 'install'
end 'Installer'

function main() returns ExitCode
	let installer = spawn Installer.create()
	let installs = try await installer.install() otherwise 0
	let answer = try await Greeter.current().greet() otherwise 0
	return (answer * 10 + installs) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
91
```

<!-- test: register-replaces-the-service-for-later-lookups -->
```maxon
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

type Rude implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 9
	end 'greet'
end 'Rude'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	let first = try await Greeter.current().greet() otherwise 0
	Greeter.register(spawn Rude.create())
	let second = try await Greeter.current().greet() otherwise 0
	return (first * 10 + second) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
79
```

<!-- test: a-lookup-taken-before-register-keeps-the-old-service -->
A lookup is a snapshot: the handle taken before the register still reaches the old service, which keeps
running until its last holder drops it.
```maxon
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

type Rude implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 9
	end 'greet'
end 'Rude'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	let old = Greeter.current()
	Greeter.register(spawn Rude.create())
	let fromOld = try await old.greet() otherwise 0
	let fromNew = try await Greeter.current().greet() otherwise 0
	return (fromOld * 10 + fromNew) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
79
```

<!-- test: a-registered-handle-is-a-box-only-the-registry-owns -->
<!-- procs: 1 -->
The registry slot holds a handle box of its own, with its own share of the mailbox, so the registering
frame's drop of its reference and any thread's replace or release of the slot step different counts.
<!-- MmTrace -->
```maxon
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

type Rude implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 9
	end 'greet'
end 'Rude'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	let rude = spawn Rude.create()
	Greeter.register(rude)
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
9
```
```mm-trace
mm_alloc Polite #1 size=8
mm_alloc Polite.handle #2 size=8
mm_alloc tag=0 #3 size=16
mm_decref Polite.handle #2 rc=0
mm_free Polite.handle #2
mm_alloc Rude #4 size=8
mm_alloc Rude.handle #5 size=8
mm_alloc tag=0 #6 size=16
mm_alloc tag=0 #7 size=16
mm_decref tag=0 #3 rc=0
mm_free tag=0 #3
mm_decref tag=0 #6 rc=0
mm_free tag=0 #6
mm_alloc tag=0 #8 size=16
mm_decref tag=0 #8 rc=0
mm_free tag=0 #8
mm_decref Rude.handle #5 rc=0
mm_free Rude.handle #5
mm_decref Polite #1 rc=0
mm_free Polite #1
mm_decref tag=0 #7 rc=0
mm_free tag=0 #7
mm_decref Rude #4 rc=0
mm_free Rude #4
```

<!-- test: a-conditional-register-fails-when-the-value-moved -->
`register(new, replacing: old)` publishes only while `old` is still current: after `a` was replaced by the
value 3 it answers `false` and 3 stays; replacing the current value answers `true`.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	let a = Config.current()
	Config.register(Config.create(3))
	let stale = Config.register(Config.create(2), replacing: a)
	let afterStale = Config.current().n

	let c = Config.current()
	let fresh = Config.register(Config.create(5), replacing: c)

	if stale or not fresh 'outcomes'
		return 1
	end 'outcomes'

	return (afterStale * 10 + Config.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
35
```

<!-- test: a-registered-value-is-seen-live-by-every-thread -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)

type Reader
	var reads as Integer

	static function create() returns Self
		return Self{reads: 0}
	end 'create'

	export function read() returns Integer
		self.reads = self.reads + 1
		return Config.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let before = try await reader.read() otherwise 0
	Config.register(Config.create(6))
	let after = try await reader.read() otherwise 0
	return (before * 10 + after) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```

<!-- test: an-unreached-default-is-never-started -->
Nothing reaches `Greeter` or `Config`, so neither factory runs: no service is spawned and nothing prints.
```maxon
interface Greeter
	function greet() returns Integer
end 'Greeter'

type Loud implements Greeter
	var calls as Integer

	static function create() returns Self
		print("started\n")
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 9
	end 'greet'
end 'Loud'

type Config
	export let n as Integer

	static function create(n Integer) returns Self
		print("built\n")
		return Self{n: n}
	end 'create'
end 'Config'

default Greeter = spawn Loud.create()
default Config = Config.create(1)

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
```

<!-- test: a-program-default-wins-over-a-stdlib-default -->
```maxon
// --- stdlib-overlay: Builtins.maxon
public typealias TonePitch = int(i64.min to i64.max)

public interface Tone
	function pitch() returns TonePitch
end 'Tone'

public type Soft implements Tone
	var calls as TonePitch

	public static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 3
	end 'pitch'
end 'Soft'

default Tone = spawn Soft.create()
// --- file: main.maxon
type Loud implements Tone
	var calls as TonePitch

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 9
	end 'pitch'
end 'Loud'

default Tone = spawn Loud.create()

function main() returns ExitCode
	let pitch = try await Tone.current().pitch() otherwise 0
	return pitch as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: the-registered-service-drains-at-exit -->
`main` sends and returns at once; the exit drain runs the registered service's queue out, in FIFO order.
```maxon
interface Journal
	function write(line Integer)
end 'Journal'

type Console implements Journal
	var written as Integer

	static function create() returns Self
		return Self{written: 0}
	end 'create'

	export function write(line Integer)
		self.written = self.written + 1
		print("line {line} ({self.written})\n")
	end 'write'
end 'Console'

default Journal = spawn Console.create()

function main() returns ExitCode
	try Journal.current().write(1) otherwise panic("send write 1")
	try Journal.current().write(2) otherwise panic("send write 2")
	try Journal.current().write(3) otherwise panic("send write 3")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
line 1 (1)
line 2 (2)
line 3 (3)
```

<!-- test: a-module-var-beside-a-handler-that-sends-through-an-interface-handle -->
A module-level `var` no message reaches is legal in a program that spawns (`green-thread-globals.md`, E3143):
`Caller.call` sends through `Greeter.handle` and never reaches `bookKeeping`, so `ledger` is `main`'s alone.
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
	function greet() returns Integer
	function note(n Integer)
end 'Greeter'

type Polite implements Greeter
	var noted as Integer

	static function create() returns Self
		return Self{noted: 0}
	end 'create'

	export function greet() returns Integer
		return 7 + self.noted
	end 'greet'

	export function note(n Integer)
		self.noted = self.noted + n
	end 'note'
end 'Polite'

type Caller
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function call(g Greeter.handle) returns Integer
		self.asked = self.asked + 1
		try g.note(self.asked) otherwise panic("send note")
		return try await g.greet() otherwise 0
	end 'call'
end 'Caller'

function main() returns ExitCode
	let kept = callIndirect(bookKeeping, n: 1)
	let caller = spawn Caller.create()
	let answer = try await caller.call(spawn Polite.create()) otherwise 0

	if answer != 8 or kept != 1 'wrong'
		return 1
	end 'wrong'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: a-value-default-may-hold-a-spawned-handle -->
```maxon
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

type Route
	export let greeter as Greeter.handle

	static function create(greeter Greeter.handle) returns Self
		return Self{greeter: greeter}
	end 'create'
end 'Route'

default Route = Route.create(spawn Polite.create())

function main() returns ExitCode
	let answer = try await Route.current().greeter.greet() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-value-default-of-a-record-with-a-narrow-int-field -->
A field of a narrow integer alias is a scalar like any other: the share walk that marks the value before it
is published has nothing to follow there.
```maxon
typealias Count = int(0 to 1000)

type Config
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(3)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-value-default-of-a-record-with-a-narrow-int-field-in-a-nested-record -->
The same narrow field one record down: the walk descends into `inner` and finds only a scalar there.
```maxon
typealias Count = int(0 to 1000)

type Inner
	export let count as Count

	static function create(count Count) returns Self
		return Self{count: count}
	end 'create'
end 'Inner'

type Config
	export let inner as Inner

	static function create(count Count) returns Self
		return Self{inner: Inner.create(count)}
	end 'create'
end 'Config'

default Config = Config.create(4)

function main() returns ExitCode
	return Config.current().inner.count as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-shared-value-is-read-and-published-across-threads -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Reader
	let cell as ConfigCell

	static function create(cell ConfigCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function read() returns Integer
		return self.cell.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let reader = spawn Reader.create(cell.clone())
	let before = try await reader.read() otherwise 0
	cell.publish(Config.create(6))
	let after = try await reader.read() otherwise 0
	return (before * 10 + after) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```

<!-- test: a-shared-value-read-across-a-publish-stays-valid -->
A value read before two publishes stays readable, and the replaced values are freed by the time the leak
gate reads its counters (a leak would exit 101).
```maxon
type Snapshot
	export let n as Integer
	export let label as String

	static function create(n Integer, label String) returns Self
		return Self{n: n, label: label}
	end 'create'
end 'Snapshot'

typealias SnapshotCell = SharedValue with Snapshot

function main() returns ExitCode
	let cell = SnapshotCell.create(Snapshot.create(3, label: "the first snapshot, long enough to be a heap string"))
	let held = cell.current()
	cell.publish(Snapshot.create(4, label: "the second snapshot, long enough to be a heap string"))
	cell.publish(Snapshot.create(5, label: "the third snapshot, long enough to be a heap string"))

	if cell.current().n != 5 'latest'
		return 1
	end 'latest'

	print("{held.label}\n")
	return held.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```
```stdout
the first snapshot, long enough to be a heap string
```

<!-- test: a-conditional-publish-fails-when-the-value-moved -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let a = cell.current()
	cell.publish(Config.create(3))
	let stale = cell.publish(Config.create(2), replacing: a)
	let afterStale = cell.current().n

	let c = cell.current()
	let fresh = cell.publish(Config.create(5), replacing: c)

	if stale or not fresh 'outcomes'
		return 1
	end 'outcomes'

	return (afterStale * 10 + cell.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
35
```

<!-- test: a-handler-looks-up-the-registry-during-the-exit-drain -->
`main` returns with three messages still queued; the exit drain runs them, and each handler reads the value
key, which must still be live then.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(5)

type Printer
	var said as Integer

	static function create() returns Self
		return Self{said: 0}
	end 'create'

	export function say(n Integer)
		self.said = self.said + 1
		print("{n + Config.current().n}\n")
	end 'say'
end 'Printer'

function main() returns ExitCode
	let printer = spawn Printer.create()
	try printer.say(1) otherwise panic("send say 1")
	try printer.say(2) otherwise panic("send say 2")
	try printer.say(3) otherwise panic("send say 3")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
6
7
8
```

<!-- test: a-handler-sends-through-the-registry-during-the-exit-drain -->
The service-key sibling: during the exit drain the worker's handler looks up `Sink` and sends to it, so the
registered service must still be reachable and must drain too. One worker sends into one collector, so the
lines keep their order.
```maxon
interface Sink
	function put(n Integer)
end 'Sink'

type Collector implements Sink
	var received as Integer

	static function create() returns Self
		return Self{received: 0}
	end 'create'

	export function put(n Integer)
		self.received = self.received + 1
		print("put {n} ({self.received})\n")
	end 'put'
end 'Collector'

default Sink = spawn Collector.create()

type Worker
	var worked as Integer

	static function create() returns Self
		return Self{worked: 0}
	end 'create'

	export function work(n Integer)
		self.worked = self.worked + 1
		try Sink.current().put(n) otherwise panic("send put")
	end 'work'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	try worker.work(1) otherwise panic("send work 1")
	try worker.work(2) otherwise panic("send work 2")
	try worker.work(3) otherwise panic("send work 3")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
put 1 (1)
put 2 (2)
put 3 (3)
```

<!-- test: a-handle-converts-at-an-overloaded-callees-argument -->
The argument door holds for an overload set too: `probe(spawn Counter.create())` selects the `Tally.handle`
overload and converts the `Counter.handle` to it.
```maxon
interface Tally
	function total() returns Integer
end 'Tally'

type Counter implements Tally
	var n as Integer

	static function create() returns Self
		return Self{n: 6}
	end 'create'

	export function total() returns Integer
		self.n = self.n + 1
		return self.n
	end 'total'
end 'Counter'

function probe(t Tally.handle) returns Integer
	return try await t.total() otherwise 0
end 'probe'

function probe(n Integer) returns Integer
	return n * 100
end 'probe'

function main() returns ExitCode
	let fromHandle = probe(spawn Counter.create())
	let fromInteger = probe(1)
	return (fromHandle + fromInteger) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
107
```

<!-- test: a-second-implementer-with-a-different-message-order -->
Each implementer has its own send table: `Ledger` declares the requirements in the opposite order and lays
out different fields, and a `Tally.handle` holding it still reaches the right messages.
```maxon
interface Tally
	function add(by Integer)
	function total() returns Integer
end 'Tally'

type Counter implements Tally
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		self.n = self.n + by
	end 'add'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Counter'

type Ledger implements Tally
	let label as String
	var adds as Integer
	var sum as Integer

	static function create() returns Self
		return Self{label: "a ledger label long enough to be a heap string", adds: 0, sum: 10}
	end 'create'

	export function total() returns Integer
		return self.sum + self.adds * 100 + (self.label.byteLength() as Integer) * 0
	end 'total'

	export function add(by Integer)
		self.adds = self.adds + 1
		self.sum = self.sum + by
	end 'add'
end 'Ledger'

function drive(t Tally.handle, by Integer) returns Integer
	try t.add(by) otherwise panic("send add")
	return try await t.total() otherwise 0
end 'drive'

function main() returns ExitCode
	let fromCounter = drive(spawn Counter.create(), by: 3)
	let fromLedger = drive(spawn Ledger.create(), by: 4)
	return (fromCounter + fromLedger) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
117
```

<!-- test: error.an-await-cycle-through-a-second-implementer -->
<!-- unsupported-targets: wasm32-wasi -->
On wasm32-wasi this case's expected diagnostic is displaced by E3104 ('spawn' lowers to the runtime entry '__svc_spawn', which has no wasm32-wasi implementation), so the lane is excluded.

`Innocent`, the first implementer of `Echo`, awaits nothing. `Relay`, the second, awaits `Bouncer`, and
`Bouncer` awaits through an `Echo.handle`, which may hold a `Relay`, so the two can wait on each other. The
cycle is found through every implementer of the interface, not only the first.
```maxon
interface Echo
	function echo() returns Integer
end 'Echo'

type Innocent implements Echo
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function echo() returns Integer
		self.calls = self.calls + 1
		return 1
	end 'echo'
end 'Innocent'

type Relay implements Echo
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function echo() returns Integer
		self.calls = self.calls + 1
		return try await spawnBouncer().back(spawn Innocent.create()) otherwise 0
	end 'echo'
end 'Relay'

type Bouncer
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function back(e Echo.handle) returns Integer
		self.calls = self.calls + 1
		return try await e.echo() otherwise 0
	end 'back'
end 'Bouncer'

function spawnBouncer() returns Bouncer.handle
	return spawn Bouncer.create()
end 'spawnBouncer'

function main() returns ExitCode
	let relay = spawn Relay.create()
	let answer = try await relay.echo() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3139: <fragment>:41:14: service call cycle — these messages can deadlock waiting on each other:
    `Bouncer.back` (<fragment>:41:14) awaits a reply from `Relay`
    `Relay.echo` (<fragment>:28:14) awaits a reply from `Bouncer`
    A message may not await a reply from a service that can await back. Break the ring by making one of these messages return nothing and throw nothing, and send it with `try <handle>.<message>(…) otherwise …` without awaiting it — because a non-blocking send is not part of the graph
```

<!-- test: a-shared-value-of-a-string -->
A `String` is immutable, so it can be a `SharedValue`'s value: a literal at creation, an interpolated string
at publish.
```maxon
typealias Label = SharedValue with String

function main() returns ExitCode
	let label = Label.create("abc")
	let before = label.current()
	let n = 7
	label.publish("x{n}")
	let after = label.current()
	print("{before} {after}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
abc x7
```

<!-- test: error.current-reached-through-a-function-from-a-global-initializer -->
The refusal follows calls: `limit`'s initializer reaches `Config.current()` through `Limits.read`. A global
initializer may call a type's static factory but not a free function (E2045), so the road runs through one.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(7)

type Limits
	export let n as Integer

	static function read() returns Limits
		return Self{n: Config.current().n}
	end 'read'
end 'Limits'

let limit = Limits.read()

function main() returns ExitCode
	return limit.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3173: <fragment>:16:18: `Config.current()` is reached from the initializer of `limit`, which runs before any `default` is built — call it from `main`, or from a function `main` calls
```

<!-- test: error.a-key-type-declaring-its-own-current -->
A `default` key's `current` and `register` belong to the registry, so the key type may not declare its own
static `current`.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	static function current() returns Config
		return Config.create(0)
	end 'current'
end 'Config'

default Config = Config.create(7)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3175: <fragment>:9:18: `Config` is a `default` key, so `Config.current` and `Config.register` are the registry's — its own static `current` would be hidden; rename it
note: <fragment>:14:9: the `default` for `Config`
```

<!-- test: a-message-with-a-float-parameter -->
A message with no reply takes a `float` argument. A reply cell carries one integer word (E3140), so the
awaited answer is the average in hundredths.
```maxon
type Averager
	var total as FullRangeFloat
	var count as Integer

	static function create() returns Self
		return Self{total: 0.0, count: 0}
	end 'create'

	export function take(celsius FullRangeFloat)
		self.total = self.total + celsius
		self.count = self.count + 1
	end 'take'

	export function averageHundredths() returns Integer
		let average = try (self.total / (self.count as FullRangeFloat)) otherwise 0.0
		return trunc(average * 100.0)
	end 'averageHundredths'
end 'Averager'

function main() returns ExitCode
	let h = spawn Averager.create()
	try h.take(20.25) otherwise panic("send take 20.25")
	try h.take(21.5) otherwise panic("send take 21.5")
	try h.take(22.75) otherwise panic("send take 22.75")
	let hundredths = try await h.averageHundredths() otherwise 0
	print("{hundredths}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
2150
```

<!-- test: error.an-argument-that-could-convert-to-two-interface-handles -->
<!-- unsupported-targets: wasm32-wasi -->
On wasm32-wasi the expected diagnostic is followed by an extra E3104 (an awaited message send lowers to the runtime entry '__gt_cell_alloc', which has no wasm32-wasi implementation) that the pinned output does not list, so the lane is excluded.

`Counter` implements both interfaces, so its handle converts to either overload's parameter equally well; the
call is ambiguous rather than resolved by declaration order.
```maxon
interface Tally
	function total() returns Integer
end 'Tally'

interface Gauge
	function level() returns Integer
end 'Gauge'

type Counter implements Tally, Gauge
	var n as Integer

	static function create() returns Self
		return Self{n: 6}
	end 'create'

	export function total() returns Integer
		return self.n
	end 'total'

	export function level() returns Integer
		return self.n * 2
	end 'level'
end 'Counter'

function probe(t Tally.handle) returns Integer
	return try await t.total() otherwise 0
end 'probe'

function probe(g Gauge.handle) returns Integer
	return try await g.level() otherwise 0
end 'probe'

function main() returns ExitCode
	return probe(spawn Counter.create()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3007: <fragment>:35:9: Ambiguous overload for 'probe': multiple overloads match. Candidates: (t Tally.handle), (g Gauge.handle)
```

<!-- test: error.implementers-of-one-interface-that-disagree-on-a-reply -->
A send through `Worker.handle` builds one request and awaits one reply for whichever implementer the handle
holds. `Left` and `Right` both satisfy the abstract `throws Error`, but each throws its own enum, so their
replies differ and no one send can serve both.
```maxon
enum LeftError implements Error
	refused
end 'LeftError'

enum RightError implements Error
	busy
end 'RightError'

interface Worker
	function work() returns Integer throws Error
end 'Worker'

type Left implements Worker
	var runs as Integer

	static function create() returns Self
		return Self{runs: 0}
	end 'create'

	export function work() returns Integer throws LeftError
		self.runs = self.runs + 1

		if self.runs > 5 'tooMany'
			throw LeftError.refused
		end 'tooMany'

		return 1
	end 'work'
end 'Left'

type Right implements Worker
	var runs as Integer

	static function create() returns Self
		return Self{runs: 0}
	end 'create'

	export function work() returns Integer throws RightError
		self.runs = self.runs + 1

		if self.runs > 5 'tooMany'
			throw RightError.busy
		end 'tooMany'

		return 2
	end 'work'
end 'Right'

function ask(w Worker.handle) returns Integer
	return try await w.work() otherwise 0
end 'ask'

function main() returns ExitCode
	return (ask(spawn Left.create()) + ask(spawn Right.create())) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3177: <fragment>:51:21: `Worker.work` cannot be sent through `Worker.handle`: `Right` and `Left` declare it with different parameter, return or `throws` types, so one request and one reply cannot serve both — send it through each service's own handle
```

<!-- test: a-handle-converts-at-an-overload-resolved-by-a-later-argument -->
`Counter.handle` could convert to either overload's first parameter; the second argument picks the `Tally`
overload, and the handle converts to `Tally.handle` there.
```maxon
interface Tally
	function total() returns Integer
end 'Tally'

interface Gauge
	function level() returns Integer
end 'Gauge'

type Counter implements Tally, Gauge
	var n as Integer

	static function create() returns Self
		return Self{n: 6}
	end 'create'

	export function total() returns Integer
		return self.n
	end 'total'

	export function level() returns Integer
		return self.n * 2
	end 'level'
end 'Counter'

function probe(t Tally.handle, extra Integer) returns Integer
	return (try await t.total() otherwise 0) + extra
end 'probe'

function probe(g Gauge.handle, extra String) returns Integer
	return (try await g.level() otherwise 0) + (extra.byteLength() as Integer)
end 'probe'

function main() returns ExitCode
	return probe(spawn Counter.create(), extra: 1) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-handle-converts-at-a-function-value-call -->
A call through a function value is a door too: the `Fixed.handle` argument converts to the `Source.handle`
the function type declares.
```maxon
interface Source
	function value() returns Integer
end 'Source'

type Fixed implements Source
	let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Fixed'

typealias SourceReader = function(Source.handle) returns Integer

function readSource(source Source.handle) returns Integer
	return try await source.value() otherwise 0
end 'readSource'

function through(f SourceReader) returns Integer
	return f(spawn Fixed.create(5))
end 'through'

function main() returns ExitCode
	let f = readSource
	let direct = f(spawn Fixed.create(1))
	let passed = through(readSource)
	return (direct * 10 + passed) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
15
```

<!-- test: a-handle-converts-at-a-closure-literal-call -->
A closure literal whose parameter is written `Source.handle` inline takes a `Fixed.handle` argument
converted at the call.
```maxon
interface Source
	function value() returns Integer
end 'Source'

type Fixed implements Source
	let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Fixed'

function main() returns ExitCode
	let f = function(s Source.handle) gives (try await s.value() otherwise 0)
	return f(spawn Fixed.create(3)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: a-handle-converts-at-a-witness-call -->
A requirement taking a `Tally.handle`, called through a value held at its interface type, converts the
`Counter.handle` argument like any other call.
```maxon
interface Tally
	function total() returns Integer
end 'Tally'

type Counter implements Tally
	var n as Integer

	static function create() returns Self
		return Self{n: 6}
	end 'create'

	export function total() returns Integer
		self.n = self.n + 1
		return self.n
	end 'total'
end 'Counter'

interface Probe
	function read(t Tally.handle) returns Integer
end 'Probe'

type Doubling implements Probe
	let factor as Integer

	static function create(factor Integer) returns Self
		return Self{factor: factor}
	end 'create'

	function read(t Tally.handle) returns Integer
		return (try await t.total() otherwise 0) * self.factor
	end 'read'
end 'Doubling'

type Holder
	export let probe as Probe

	static function create(probe Probe) returns Self
		return Self{probe: probe}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(Doubling.create(2))
	return holder.probe.read(spawn Counter.create()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
14
```

<!-- test: error.a-spawned-service-missing-an-interface-message-is-a-diagnostic -->
A spawned service that declares an interface it does not fully implement is refused like any other partial conformer, naming the missing requirement.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function value() returns Count
		return self.n
	end 'value'
end 'B'

function main() returns ExitCode
	let h = (spawn A.create()) as Pinger.handle
	let b = spawn B.create()
	print("{try await h.ping() otherwise 0}\n")
	print("{try await b.value() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:20:6: Partial interface implementation: type 'B' is missing 1 method(s):
  - ping() returns Count
```

<!-- test: error.a-non-conforming-service-spawned-first-is-still-reported-as-partial -->
The same program with the non-conforming service spawned first: the order of the spawns does not decide which diagnostic is reported.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function value() returns Count
		return self.n
	end 'value'
end 'B'

function main() returns ExitCode
	let b = spawn B.create()
	let h = (spawn A.create()) as Pinger.handle
	print("{try await h.ping() otherwise 0}\n")
	print("{try await b.value() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:20:6: Partial interface implementation: type 'B' is missing 1 method(s):
  - ping() returns Count
```

<!-- test: error.a-non-conforming-service-as-the-handle-is-reported-as-partial -->
The non-conforming service is declared first and is the one converted to the interface handle.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function value() returns Count
		return self.n
	end 'value'
end 'B'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

function main() returns ExitCode
	let a = spawn A.create()
	let h = (spawn B.create()) as Pinger.handle
	print("{try await h.ping() otherwise 0}\n")
	print("{try await a.ping() otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Partial interface implementation: type 'B' is missing 1 method(s):
  - ping() returns Count
```

<!-- test: error.an-interface-message-no-service-implements-is-refused -->
When no service implementing the interface declares the requirement, the handle has no such message to send.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function value() returns Count
		return self.n
	end 'value'
end 'B'

function main() returns ExitCode
	let h = (spawn B.create()) as Pinger.handle
	let v = try await h.ping() otherwise 0
	print("{v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3136: <fragment>:22:22: `interface Pinger` requires `ping`, but no service implementing it declares `ping` as an `export` instance method, so `Pinger.handle` has no such message to send. Implement it on the service
```

<!-- test: error.a-withheld-interface-message-is-refused -->
A service that declares the requirement as a private method withholds it: it is not a message, so the handle cannot send it.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

function main() returns ExitCode
	let h = (spawn A.create()) as Pinger.handle
	let b = spawn B.create()
	let v = try await h.ping() otherwise 0
	let w = try await b.ping() otherwise 0
	print("{v} {w}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3136: <fragment>:35:22: `interface Pinger` requires `ping`, and `type A` implements it, but not as an `export` instance method — so it is not a message of `A`, and `Pinger.handle` cannot send it to every service behind it. Declare `ping` on `A` as an `export` instance method
```
<!-- test: a-withheld-interface-message-with-no-send-compiles -->
The same conformers compile when nothing sends the withheld requirement through the handle.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

typealias PingerHandles = Array with Pinger.handle

function main() returns ExitCode
	var handles = PingerHandles.create()
	handles.push((spawn A.create()) as Pinger.handle)
	let b = spawn B.create()
	let w = try await b.ping() otherwise 0
	print("{handles.count()} {w}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 9
```
<!-- test: an-interface-handle-held-in-an-array-shuts-down-cleanly -->
An interface handle held in an array element, beside a second service, shuts down cleanly when every conformer exports the requirement.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

typealias PingerHandles = Array with Pinger.handle

function main() returns ExitCode
	var handles = PingerHandles.create()
	handles.push((spawn A.create()) as Pinger.handle)
	let b = spawn B.create()
	let w = try await b.ping() otherwise 0
	print("{handles.count()} {w}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 9
```

<!-- test: an-interface-handle-held-in-a-binding-shuts-down-cleanly -->
An interface handle held in a binding and sent to, beside a second service, shuts down cleanly.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

function main() returns ExitCode
	let h = (spawn A.create()) as Pinger.handle
	let b = spawn B.create()
	let v = try await h.ping() otherwise 0
	let w = try await b.ping() otherwise 0
	print("{v} {w}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 9
```

<!-- test: error.a-withheld-interface-message-spawned-first-is-refused -->
The order of the spawns does not decide whether the withheld requirement is refused.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

type A implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	function ping() returns Count
		return self.n
	end 'ping'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

function main() returns ExitCode
	let b = spawn B.create()
	let h = (spawn A.create()) as Pinger.handle
	let v = try await h.ping() otherwise 0
	let w = try await b.ping() otherwise 0
	print("{v} {w}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3136: <fragment>:35:22: `interface Pinger` requires `ping`, and `type A` implements it, but not as an `export` instance method — so it is not a message of `A`, and `Pinger.handle` cannot send it to every service behind it. Declare `ping` on `A` as an `export` instance method
```
<!-- test: error.a-withheld-message-behind-a-default-is-refused-in-either-order -->
A service reached only through a `default` still withholds a private requirement from the interface handle.
```maxon
typealias TonePitch = int(0 to 100)

interface Tone
	function pitch() returns TonePitch
end 'Tone'

type Loud implements Tone
	var calls as TonePitch

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 9
	end 'pitch'
end 'Loud'

type Soft implements Tone
	var calls as TonePitch

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 3
	end 'pitch'
end 'Soft'

function main() returns ExitCode
	let h = (spawn Loud.create()) as Tone.handle
	let pitch = try await h.pitch() otherwise 0
	return pitch
end 'main'

default Tone = spawn Soft.create()
```
```maxoncstderr
error E3136: <fragment>:36:26: `interface Tone` requires `pitch`, and `type Soft` implements it, but not as an `export` instance method — so it is not a message of `Soft`, and `Tone.handle` cannot send it to every service behind it. Declare `pitch` on `Soft` as an `export` instance method
```

<!-- test: error.a-withheld-message-behind-a-default-declared-first-is-refused -->
The same program with the `default` declared before `main`.
```maxon
typealias TonePitch = int(0 to 100)

interface Tone
	function pitch() returns TonePitch
end 'Tone'

type Loud implements Tone
	var calls as TonePitch

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 9
	end 'pitch'
end 'Loud'

type Soft implements Tone
	var calls as TonePitch

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	function pitch() returns TonePitch
		self.calls = self.calls + 1
		return 3
	end 'pitch'
end 'Soft'

default Tone = spawn Soft.create()

function main() returns ExitCode
	let h = (spawn Loud.create()) as Tone.handle
	let pitch = try await h.pitch() otherwise 0
	return pitch
end 'main'
```
```maxoncstderr
error E3136: <fragment>:38:26: `interface Tone` requires `pitch`, and `type Soft` implements it, but not as an `export` instance method — so it is not a message of `Soft`, and `Tone.handle` cannot send it to every service behind it. Declare `pitch` on `Soft` as an `export` instance method
```

<!-- test: a-service-behind-a-derived-interface-widens-to-the-base-handle -->
A service implementing a derived interface converts to its base interface's handle, beside a service implementing the base directly.
```maxon
typealias Count = int(0 to 1000)

interface Pinger
	function ping() returns Count
end 'Pinger'

interface Sub extends Pinger
	function tag() returns Count
end 'Sub'

type A implements Sub
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'

	export function tag() returns Count
		return 1
	end 'tag'
end 'A'

type B implements Pinger
	let n as Count

	static function create() returns Self
		return Self{n: 9}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'B'

function main() returns ExitCode
	let b = spawn B.create()
	let h = (spawn A.create()) as Pinger.handle
	let v = try await h.ping() otherwise 0
	let w = try await b.ping() otherwise 0
	print("{v} {w}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 9
```

<!-- test: error.two-defaults-for-one-key -->
```maxon
// --- file: a.maxon
module typealias Integer = int(i64.min to i64.max)

module type Config
	export let n as Integer

	module static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)
// --- file: b.maxon
default Config = Config.create(2)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
```
```maxoncstderr
error E3169: <fragment>:15:9: two `default` declarations for `Config` in one program — delete one of them
note: <fragment>:13:9: the other `default` for `Config`
```

<!-- test: error.two-defaults-for-one-key-are-blamed-in-path-order -->
The blamed `default` is the one in the file whose path sorts later byte for byte, not the one declared later.
```maxon
// --- file: b.maxon
module typealias Integer = int(i64.min to i64.max)

module type Config
	export let n as Integer

	module static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)
// --- file: a.maxon
default Config = Config.create(2)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
```
```maxoncstderr
error E3169: <fragment>:13:9: two `default` declarations for `Config` in one program — delete one of them
note: <fragment>:15:9: the other `default` for `Config`
```

<!-- test: error.a-default-whose-service-does-not-implement-its-key -->
`Stranger` has a `greet` of the right shape, but it does not declare `implements Greeter`.
```maxon
interface Greeter
	function greet() returns Integer
end 'Greeter'

type Stranger
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Stranger'

default Greeter = spawn Stranger.create()

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3170: <fragment>:19:9: the `default` for `Greeter` spawns `Stranger`, which does not implement `Greeter` — declare `type Stranger implements Greeter`, or spawn a type that does
```

<!-- test: a-value-default-of-a-writable-type-is-read-by-a-service -->
A `var` field does not keep a type out of the registry: every reader gets a read-only view of the value.
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(3)

type Reader
	var reads as Integer

	static function create() returns Self
		return Self{reads: 0}
	end 'create'

	export function read() returns Integer
		self.reads = self.reads + 1
		return Config.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let seen = try await reader.read() otherwise 0
	print("service={seen} main={Config.current().n}\n")
	return seen as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```
```stdout
service=3 main=3
```

<!-- test: error.defaults-that-reach-each-other -->
```maxon
type Left
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Left'

type Right
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Right'

default Left = Left.create(Right.current().n + 1)
default Right = Right.create(Left.current().n + 1)

function main() returns ExitCode
	return (Left.current().n + Right.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3172: <fragment>:18:9: the `default`s for `Left` and `Right` reach each other — build one of them without looking up the other
```

<!-- test: error.current-from-a-global-initializer -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(7)

let limit = Config.current().n

function main() returns ExitCode
	return limit as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3173: <fragment>:12:13: `Config.current()` is reached from the initializer of `limit`, which runs before any `default` is built — call it from `main`, or from a function `main` calls
```

<!-- test: error.register-from-a-global-initializer -->
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(7)

let first = Config.create(1)
let swapped = Config.register(Config.create(9), replacing: first)

function main() returns ExitCode
	return 0 if swapped else 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3173: <fragment>:13:15: `Config.register()` is reached from the initializer of `swapped`, which runs before any `default` is built — call it from `main`, or from a function `main` calls
```

<!-- test: error.a-registered-value-that-is-not-solely-owned -->
`alias` still names the record, so publishing it would hand every thread a value this one can still reach.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	let fresh = Config.create(4)
	let alias = fresh
	Config.register(fresh)
	return (alias.n + Config.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3174: <fragment>:15:18: the value published to `Config` is not solely owned here — publish a `.clone()`
```

<!-- test: a-shared-value-of-a-writable-type-is-read-across-threads -->
`next` is written while `main` still owns it, then published; the spawned reader sees each published value.
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Reader
	let cell as ConfigCell

	static function create(cell ConfigCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function read() returns Integer
		return self.cell.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(2))
	let reader = spawn Reader.create(cell.clone())
	let before = try await reader.read() otherwise 0
	var next = Config.create(2)
	next.n = next.n + 5
	cell.publish(next)
	let after = try await reader.read() otherwise 0
	print("before={before} after={after}\n")
	return (before * 10 + after) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
27
```
```stdout
before=2 after=7
```

<!-- test: a-value-default-whose-let-field-holds-a-writable-type-is-read-by-a-service -->
`tally` holds a `Tally` whose `count` is a `var`; a service reads through the whole path.
```maxon
type Tally
	export var count as Integer

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'
end 'Tally'

type Config
	export let tally as Tally

	static function create(count Integer) returns Self
		return Self{tally: Tally.create(count)}
	end 'create'
end 'Config'

default Config = Config.create(3)

type Reader
	var reads as Integer

	static function create() returns Self
		return Self{reads: 0}
	end 'create'

	export function count() returns Integer
		self.reads = self.reads + 1
		return Config.current().tally.count
	end 'count'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let seen = try await reader.count() otherwise 0
	print("service={seen} main={Config.current().tally.count}\n")
	return seen as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```
```stdout
service=3 main=3
```

<!-- test: a-shared-value-of-a-type-holding-an-array-is-read-across-threads -->
An `Array` field is published like any other; the spawned reader walks it with a `for`.
```maxon
typealias Scores = Array with Integer

type Board
	export let scores as Scores

	static function create(top Integer) returns Self
		var scores = Scores.create()
		scores.push(top)
		scores.push(top * 2)
		return Self{scores: scores}
	end 'create'
end 'Board'

typealias BoardCell = SharedValue with Board

type Summer
	let cell as BoardCell

	static function create(cell BoardCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function total() returns Integer
		var sum = 0 as Integer
		for score in self.cell.current().scores 'eachScore'
			sum = sum + score
		end 'eachScore'
		return sum
	end 'total'
end 'Summer'

function main() returns ExitCode
	let cell = BoardCell.create(Board.create(1))
	let summer = spawn Summer.create(cell.clone())
	let before = try await summer.total() otherwise 0
	cell.publish(Board.create(4))
	let after = try await summer.total() otherwise 0
	print("before={before} after={after} count={cell.current().scores.count()}\n")
	return (before * 10 + after) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```
```stdout
before=3 after=12 count=2
```

<!-- test: a-shared-value-of-a-type-holding-a-map-is-read-across-threads -->
The same for a `Map`: the spawned reader looks a key up in the published value.
```maxon
typealias Ranks = Map with (String, Integer)

type Ladder
	export let ranks as Ranks

	static function create(first Integer) returns Self
		var ranks = Ranks.create()
		ranks.upsert("first", value: first)
		return Self{ranks: ranks}
	end 'create'
end 'Ladder'

typealias LadderCell = SharedValue with Ladder

type Lookup
	let cell as LadderCell

	static function create(cell LadderCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function first() returns Integer
		return try self.cell.current().ranks.get("first") otherwise 0
	end 'first'
end 'Lookup'

function main() returns ExitCode
	let cell = LadderCell.create(Ladder.create(1))
	let lookup = spawn Lookup.create(cell.clone())
	let before = try await lookup.first() otherwise 0
	cell.publish(Ladder.create(6))
	let after = try await lookup.first() otherwise 0
	print("before={before} after={after}\n")
	return (before * 10 + after) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```
```stdout
before=1 after=6
```

<!-- test: error.a-value-default-whose-graph-reaches-a-function-value -->
A function value is a closure record that owns what it captures, and no share walk can mark it.
```maxon
typealias Step = function(Integer) returns Integer

type Config
	export let step as Step

	static function create() returns Self
		return Self{step: function(n Integer) gives n + 1}
	end 'create'
end 'Config'

default Config = Config.create()

function main() returns ExitCode
	return Config.current().step(2) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3171: <fragment>:12:9: `Config` cannot be a registry value or a `SharedValue`: it is a `Config` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: error.a-shared-value-whose-graph-reaches-a-function-value -->
```maxon
typealias Step = function(Integer) returns Integer

type Config
	export let step as Step

	static function create() returns Self
		return Self{step: function(n Integer) gives n + 1}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create())
	return cell.current().step(2) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3171: <fragment>:12:24: `Config` cannot be a registry value or a `SharedValue`: it is a `Config` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: a-cell-resident-var-is-published -->
`next` lives in a cell because `refresh` reassigns its parameter; the publish takes the cell's record.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function refresh(c Config)
	c = Config.create(c.n + 1)
end 'refresh'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var next = Config.create(4)
	refresh(next)
	cell.publish(next)
	return cell.current().n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: error.a-cell-resident-var-read-after-a-publish-is-use-after-move -->
The same cell-resident `var`, read after the publish.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function refresh(c Config)
	c = Config.create(c.n + 1)
end 'refresh'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var next = Config.create(4)
	refresh(next)
	cell.publish(next)
	return next.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:21:9: use of moved value 'next': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-field-store-through-a-shared-value-read -->
**A READ IS A READ-ONLY VIEW.** `around` builds a record of its own around the `Inner` the shared value
holds, so the field store through `w.inner` would change the value under every other reader.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Wrap
	export var inner as Inner

	static function around(cell ConfigCell) returns Self
		return Self{inner: cell.current().inner}
	end 'around'
end 'Wrap'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var w = Wrap.around(cell)
	w.inner.x = 9
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:31:2: cannot write through 'w.inner', which may be the record of a `SharedValue` or registry value a call handed back; use clone()
```

<!-- test: error.a-field-store-through-a-value-default-read -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

type Wrap
	export var inner as Inner

	static function around() returns Self
		return Self{inner: Config.current().inner}
	end 'around'
end 'Wrap'

function main() returns ExitCode
	var w = Wrap.around()
	w.inner.x = 9
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:30:2: cannot write through 'w.inner', which may be the record of a `SharedValue` or registry value a call handed back; use clone()
```

<!-- test: error.a-mutating-method-through-a-shared-value-read -->
A method that writes its receiver is a write too, on anything reached through the read.
```maxon
typealias Items = Array with Integer

type Config
	export var items as Items

	static function create() returns Self
		var items = Items.create()
		items.push(1)
		return Self{items: items}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create())
	cell.current().items.push(2)
	return cell.current().items.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:18:23: cannot write through 'cell.current().items', which may be the record of a `SharedValue` or registry value a call handed back; use clone()
```

<!-- test: error.a-mutating-method-through-a-value-default-read -->
```maxon
typealias Items = Array with Integer

type Config
	export var items as Items

	static function create() returns Self
		var items = Items.create()
		items.push(1)
		return Self{items: items}
	end 'create'
end 'Config'

default Config = Config.create()

function main() returns ExitCode
	Config.current().items.push(2)
	return Config.current().items.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:17:25: cannot write through 'Config.current().items', which may be the record of a `SharedValue` or registry value a call handed back; use clone()
```

<!-- test: error.a-shared-value-read-passed-to-a-writing-parameter -->
`bump` writes its parameter, so handing it a record reached through the read is refused at the call.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function bump(i Inner)
	i.x = 9
end 'bump'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	bump(cell.current().inner)
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:26:2: cannot pass a value read out of a `SharedValue` or registry value, which is read-only, to a function that writes parameter 'i' (in main); pass a clone()
```

<!-- test: error.a-value-default-read-passed-to-a-writing-parameter -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function bump(i Inner)
	i.x = 9
end 'bump'

function main() returns ExitCode
	bump(Config.current().inner)
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:25:2: cannot pass a value read out of a `SharedValue` or registry value, which is read-only, to a function that writes parameter 'i' (in main); pass a clone()
```

<!-- test: error.a-var-bound-to-a-shared-value-read -->
A `var` may write what it holds, so binding the read to one is refused at the binding.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var w = cell.current()
	w.inner.x = 9
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:22:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'w'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-var-bound-to-a-value-default-read -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	var w = Config.current()
	w.inner.x = 9
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:21:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'w'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-var-bound-to-a-field-of-a-shared-value-read -->
A field reached through the read is the shared value's own storage too.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var m = cell.current().inner
	m.x = 9
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:22:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'm'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-var-bound-to-a-field-of-a-value-default-read -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	var m = Config.current().inner
	m.x = 9
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:21:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'm'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-var-bound-to-a-clone-of-a-shared-value-read-may-be-written -->
**THE CONTROL.** A `.clone()` of the read is a value of its own; writing it leaves the published value as it was.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var w = cell.current().clone()
	w.inner.x = 9
	print("copy={w.inner.x} shared={cell.current().inner.x}\n")
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```
```stdout
copy=9 shared=1
```

<!-- test: a-var-bound-to-a-clone-of-a-value-default-read-may-be-written -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	var w = Config.current().clone()
	w.inner.x = 9
	print("copy={w.inner.x} registry={Config.current().inner.x}\n")
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```
```stdout
copy=9 registry=1
```

<!-- test: error.a-write-through-a-shared-value-read-a-function-returned -->
The view rides the value through a function that returns it.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function read(cell ConfigCell) returns Config
	return cell.current()
end 'read'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var w = read(cell)
	w.inner.x = 9
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:26:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'w'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-write-through-a-value-default-read-a-function-returned -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function read() returns Config
	return Config.current()
end 'read'

function main() returns ExitCode
	var w = read()
	w.inner.x = 9
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:25:6: cannot assign a value, which may share storage with a `SharedValue` or registry value, to mutable binding 'w'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-write-through-a-value-default-read-a-function-passed-on -->
What the function returned is still the view, and so is anything handed on from it.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

function read() returns Config
	return Config.current()
end 'read'

function bump(i Inner)
	i.x = 9
end 'bump'

function main() returns ExitCode
	bump(read().inner)
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3159: <fragment>:29:2: cannot pass a value read out of a `SharedValue` or registry value, which is read-only, to a function that writes parameter 'i' (in main); pass a clone()
```

<!-- test: error.a-module-var-initializer-may-not-take-a-shared-value-read -->
A module-level `var` may write what it holds, so its initializer may not hand it the published record.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function seed() returns Config
	let cell = ConfigCell.create(Config.create(1))
	return cell.current()
end 'seed'

var g = seed()

function main() returns ExitCode
	g.inner.x = 9
	return g.inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:25:5: the initializer of the module-level `var` `g` may hand it a record a `SharedValue` or registry value holds, through `seed` — every thread reads that record at once and a `var` may write what it holds, so the two may not share a record. Build the value from a clone() of the read, or make `g` a `let`
```

<!-- test: error.a-module-var-initializer-may-not-take-a-shared-value-read-a-module-let-holds -->
The cell sits in a module-level `let`, but the record `current` hands back is the published value, not the `let`'s.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Holder
	export let cell as ConfigCell

	static function create() returns Self
		return Self{cell: ConfigCell.create(Config.create(1))}
	end 'create'

	static function current() returns Config
		return holder.cell.current()
	end 'current'
end 'Holder'

let holder = Holder.create()
var g = Holder.current()

function main() returns ExitCode
	g.inner.x = 9
	return holder.cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:33:5: the initializer of the module-level `var` `g` may hand it a record a `SharedValue` or registry value holds, through `Holder.current` — every thread reads that record at once and a `var` may write what it holds, so the two may not share a record. Build the value from a clone() of the read, or make `g` a `let`
```

<!-- test: error.a-module-var-initializer-may-not-take-a-shared-value-read-through-two-calls -->
`seed` names no `current`; the read comes back from `read`, one call further down.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function read(cell ConfigCell) returns Config
	return cell.current()
end 'read'

function seed() returns Config
	let cell = ConfigCell.create(Config.create(1))
	return read(cell)
end 'seed'

var g = seed()

function main() returns ExitCode
	g.inner.x = 9
	return g.inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:29:5: the initializer of the module-level `var` `g` may hand it a record a `SharedValue` or registry value holds, through `seed` — every thread reads that record at once and a `var` may write what it holds, so the two may not share a record. Build the value from a clone() of the read, or make `g` a `let`
```

<!-- test: error.a-shared-value-read-stored-into-a-var-field-is-refused-at-the-store -->
A `var` field may be written by any function the record reaches, and the function that writes it cannot
see where the field's value came from. So storing a record reached through a read into a `var` field is
refused at the store: `adopt` puts the published `Inner` into `h.inner`, and `poke`, which knows only a
`Holder`, would write it under every other reader.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Holder
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(0)}
	end 'create'
end 'Holder'

function adopt(h Holder, cell ConfigCell)
	h.inner = cell.current().inner
end 'adopt'

function poke(h Holder)
	h.inner.x = 9
end 'poke'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var h = Holder.create()
	adopt(h, cell: cell)
	poke(h)
	return cell.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot store a value, which may share storage with a `SharedValue` or registry value, into writable field 'h.inner'; use clone()
```

<!-- test: error.a-value-default-read-stored-into-a-var-field-is-refused-at-the-store -->
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export var inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

default Config = Config.create(1)

type Holder
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(0)}
	end 'create'
end 'Holder'

function adopt(h Holder)
	h.inner = Config.current().inner
end 'adopt'

function poke(h Holder)
	h.inner.x = 9
end 'poke'

function main() returns ExitCode
	var h = Holder.create()
	adopt(h)
	poke(h)
	return Config.current().inner.x as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3078: <fragment>:29:2: cannot store a value, which may share storage with a `SharedValue` or registry value, into writable field 'h.inner'; use clone()
```

<!-- test: a-clone-of-a-shared-value-read-stored-into-a-var-field-may-be-written -->
The same store of a clone: `poke` writes the copy, and the published value keeps its `x`.
```maxon
type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Config
	export let inner as Inner

	static function create(x Integer) returns Self
		return Self{inner: Inner.create(x)}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Holder
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(0)}
	end 'create'
end 'Holder'

function adopt(h Holder, cell ConfigCell)
	h.inner = cell.current().inner.clone()
end 'adopt'

function poke(h Holder)
	h.inner.x = 9
end 'poke'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	var h = Holder.create()
	adopt(h, cell: cell)
	poke(h)
	print("{cell.current().inner.x} {h.inner.x}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
1 9
```

<!-- test: error.a-shared-value-holding-an-os-handle-is-refused -->
An OS handle is closed by its one owner, and a share walk cannot mark it for every thread at once.
```maxon
type Config
	export let client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function publish(client TcpClient) returns ConfigCell
	return ConfigCell.create(Config.create(client))
end 'publish'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:10:24: `Config` cannot be a registry value or a `SharedValue`: it is a `Config` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: error.a-value-default-holding-a-promise-is-refused -->
A promise owns a green thread that exactly one owner may reclaim, so it cannot be read by every thread.
```maxon
typealias IntPromise = Promise with Integer

function work() returns Integer
	Scheduler.yield()
	return 7
end 'work'

type Config
	export let pending as IntPromise

	static function create() returns Self
		return Self{pending: async work()}
	end 'create'
end 'Config'

default Config = Config.create()

function main() returns ExitCode
	return (await Config.current().pending) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3171: <fragment>:17:9: `Config` cannot be a registry value or a `SharedValue`: it is a `Config` whose graph reaches a promise, which owns a green thread that exactly one owner may reclaim — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: a-registered-immutable-value-that-has-been-viewed-is-shared -->
`Config` has only `let` fields, so nothing can write the record `w` has viewed: publishing `fresh` shares it with every thread, and `w` stays valid.
```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	var fresh = Config.create(3)
	fresh = Config.create(4)
	view w = fresh
	Config.register(fresh)
	return (w.n + Config.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```

<!-- test: error.a-registered-writable-value-that-has-been-viewed -->
`Config` has a `var` field, so its record may be written, and `w`'s reference is a second owner for the rest of the function: publishing `fresh` would hand every thread a record this one still reaches through `w`.
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(1)

function main() returns ExitCode
	var fresh = Config.create(3)
	fresh.n = 4
	view w = fresh
	Config.register(fresh)
	return (w.n + Config.current().n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3174: <fragment>:16:18: the value published to `Config` has been viewed by 'w': a view's reference is a second owner — publish a `.clone()`
```
