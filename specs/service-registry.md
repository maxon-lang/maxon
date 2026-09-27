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
of a service implementing the interface `Key`, or a value of the all-`let` type `Key`. `Key.current()`
looks it up from any thread, and `Key.register(x)` replaces it for every later lookup. A default is built
before `main` only when something reaches its key, and the program's own default wins over the stdlib's.
`SharedValue with T` is the same live slot made at run time: `current()`, `publish(v)`, and the conditional
`publish(v, replacing: old)`.

## Tests

<!-- test: an-interface-handle-sends-and-awaits -->
A handle at interface type sends a fire-and-forget requirement and awaits a returning one, exactly as the
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
	h.add(4)
	h.add(5)
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
	let element = try sources.get(0) otherwise panic("sources.get(0)")
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
	_ = b.bump(5)
	_ = b.bump(2)
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
		Ledger.current().add(self.k)
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
	Journal.current().write(1)
	Journal.current().write(2)
	Journal.current().write(3)
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

<!-- test: a-table-send-in-a-handler-does-not-widen-the-global-rule -->
A send through an interface handle reaches a bounded set of send stubs, never a closure: `bookKeeping`'s
address is taken and it writes a module `var`, yet the handler that sends through `Greeter.handle` does not
implicate it.
```maxon
var ledger = 0

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
		g.note(self.asked)
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
	printer.say(1)
	printer.say(2)
	printer.say(3)
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
		Sink.current().put(n)
	end 'work'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	worker.work(1)
	worker.work(2)
	worker.work(3)
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
	t.add(by)
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
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
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
A fire-and-forget message takes a `float` argument. A reply cell carries one integer word (E3140), so the
awaited answer is the average in hundredths.
```maxon
type Averager
	var total as Real
	var count as Integer

	static function create() returns Self
		return Self{total: 0.0, count: 0}
	end 'create'

	export function take(celsius Real)
		self.total = self.total + celsius
		self.count = self.count + 1
	end 'take'

	export function averageHundredths() returns Integer
		let average = try (self.total / (self.count as Real)) otherwise 0.0
		return trunc(average * 100.0)
	end 'averageHundredths'
end 'Averager'

function main() returns ExitCode
	let h = spawn Averager.create()
	h.take(20.25)
	h.take(21.5)
	h.take(22.75)
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
<!-- unsupported-targets: wasm32-wasi -->
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

<!-- test: error.a-default-whose-service-does-not-implement-its-key -->
<!-- unsupported-targets: wasm32-wasi -->
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

<!-- test: error.a-value-default-of-a-writable-type -->
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

default Config = Config.create(3)

function main() returns ExitCode
	return Config.current().n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3171: <fragment>:10:9: `Config` cannot be a registry value or a `SharedValue`: its field `n` is writable — every field must be a `let` of a type a share walk can mark
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

<!-- test: error.a-shared-value-of-a-writable-type -->
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(2))
	return cell.current().n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3171: <fragment>:10:24: `Config` cannot be a registry value or a `SharedValue`: its field `n` is writable — every field must be a `let` of a type a share walk can mark
```
