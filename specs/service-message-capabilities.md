---
feature: service-message-capabilities
status: experimental
keywords: [spawn, services, isolated, immutable, capability, message, reply, factory, E3217]
category: concurrency
---

# Service messages carry a capability word on their data

## Documentation

A message hands its arguments to another green thread and its reply to the thread that awaits it, so data that
crosses must be owned by one side or read-only on both. A message parameter, a message reply or a factory
parameter whose type is a record, a union carrying data, an array or an interface value names which of the two it
is: `isolated` gives the value away, `immutable` shares it frozen. A type with no data of its own to share needs
no word: numbers, `bool`, `String`, `Character`, enums and unions that hold no payload, service handles and
`SharedValue` cells cross as they are. A type that holds a `unique` value moves on a send and needs none either.

The roster is checked at the `spawn` that makes the type a service, once for the type's messages and once for the
factory that `spawn` names. A message parameter that is a promise, a function value or an opaque type parameter is refused at the same `spawn`, also as E3217.

## Tests

<!-- test: error.an-unmarked-record-parameter-is-refused -->
`plot` takes a `Point` with no capability word.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function plot(point Point)
		self.total = self.total + point.x + point.y
	end 'plot'
end 'Plotter'

function main() returns ExitCode
	let plotter = spawn Plotter.create()
	plotter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:26:16: parameter `point` of the message `Plotter.plot` is a `Point` with no capability word, and this `spawn` makes `Plotter` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `plot(point isolated …)` or `plot(point immutable …)`
```

<!-- test: error.an-unmarked-record-reply-is-refused -->
`origin` returns a `Point` with no capability word.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function origin() returns Point
		return Point.create(self.total, y: 0)
	end 'origin'
end 'Plotter'

function main() returns ExitCode
	let plotter = spawn Plotter.create()
	plotter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:26:16: the message `Plotter.origin` returns a `Point` with no capability word, and this `spawn` makes `Plotter` a service — whose reply is handed to the green thread that awaits it, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `returns isolated …` or `returns immutable …`
```

<!-- test: error.an-unmarked-union-reply-is-refused -->
A union that carries a `ByteArray` is data of the region like a record.

```maxon
typealias Integer = int(i64.min to i64.max)

union Outcome
	found(name ByteArray)
	missing
end 'Outcome'

type Finder
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function find(key Integer) returns Outcome
		self.calls = self.calls + key
		return Outcome.missing
	end 'find'
end 'Finder'

function main() returns ExitCode
	let finder = spawn Finder.create()
	finder.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:23:15: the message `Finder.find` returns a `Outcome` with no capability word, and this `spawn` makes `Finder` a service — whose reply is handed to the green thread that awaits it, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `returns isolated …` or `returns immutable …`
```

<!-- test: error.an-unmarked-union-parameter-is-refused -->
The same union as a parameter.

```maxon
typealias Integer = int(i64.min to i64.max)

union Outcome
	found(name ByteArray)
	missing
end 'Outcome'

type Finder
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function record(outcome Outcome)
		let weight = match outcome 'weight'
			found(name) gives name.count() as Integer
			missing gives 0 as Integer
		end 'weight'

		self.calls = self.calls + weight
	end 'record'
end 'Finder'

function main() returns ExitCode
	let finder = spawn Finder.create()
	finder.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:27:15: parameter `outcome` of the message `Finder.record` is a `Outcome` with no capability word, and this `spawn` makes `Finder` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `record(outcome isolated …)` or `record(outcome immutable …)`
```

<!-- test: error.an-unmarked-array-of-records-parameter-is-refused -->
An array is data of the region whatever its element.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias PointArray = Array with Point

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function plotAll(points PointArray)
		self.total = self.total + (points.count() as Integer)
	end 'plotAll'
end 'Plotter'

function main() returns ExitCode
	let plotter = spawn Plotter.create()
	plotter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:26:16: parameter `points` of the message `Plotter.plotAll` is a `PointArray` with no capability word, and this `spawn` makes `Plotter` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `plotAll(points isolated …)` or `plotAll(points immutable …)`
```

<!-- test: error.an-unmarked-array-of-numbers-parameter-is-refused -->
An array of numbers is still an array: the elements are values, the array is not.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

type Summer
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(numbers IntegerArray)
		self.total = self.total + (numbers.count() as Integer)
	end 'add'
end 'Summer'

function main() returns ExitCode
	let summer = spawn Summer.create()
	summer.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:18:15: parameter `numbers` of the message `Summer.add` is a `IntegerArray` with no capability word, and this `spawn` makes `Summer` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `add(numbers isolated …)` or `add(numbers immutable …)`
```

<!-- test: error.an-unmarked-interface-value-parameter-is-refused -->
A value held at an interface type is a record behind a witness.

```maxon
public typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	export var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	export function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

type Painter
	var painted as Integer

	static function create() returns Self
		return Self{painted: 0}
	end 'create'

	export function paint(shape Shape)
		self.painted = self.painted + shape.area()
	end 'paint'
end 'Painter'

function main() returns ExitCode
	let painter = spawn Painter.create()
	painter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:33:16: parameter `shape` of the message `Painter.paint` is a `Shape` with no capability word, and this `spawn` makes `Painter` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `paint(shape isolated …)` or `paint(shape immutable …)`
```

<!-- test: error.an-unmarked-factory-parameter-is-refused -->
`create` takes a `Point` with no capability word, and the `spawn` names `create`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create(origin Point) returns Self
		return Self{total: origin.x}
	end 'create'

	export function read() returns Integer
		return self.total
	end 'read'
end 'Plotter'

function main() returns ExitCode
	let plotter = spawn Plotter.create(Point.create(1))
	plotter.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:25:16: parameter `origin` of the factory `Plotter.create` is a `Point` with no capability word, and this `spawn` moves its arguments into the state of `Plotter`, a service — whose state is run by whichever green thread takes its next message, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `create(origin isolated …)` or `create(origin immutable …)`
```

<!-- test: only-the-factory-the-spawn-names-is-judged -->
`fromPoint` takes an unmarked record but nothing spawns through it, so the roster of `Plotter` is as acceptable as its `create` is.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'

	static function fromPoint(origin Point) returns Self
		return Self{total: origin.x}
	end 'fromPoint'

	export function read() returns Integer
		return self.total
	end 'read'
end 'Plotter'

function main() returns ExitCode
	let local = Plotter.fromPoint(Point.create(4))
	let plotter = spawn Plotter.create(local.read())
	let answer = try await plotter.read() otherwise panic("main: a fresh plotter answers its first message")
	plotter.shutdown()
	return answer as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.an-unmarked-record-parameter-of-a-default-service-is-refused -->
A `default` service is spawned by the declaration, and the declaration is where the refusal lands.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

interface Greeter
	function greet(mutable self, point Point) returns Integer
end 'Greeter'

type Polite implements Greeter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet(point Point) returns Integer
		self.calls = self.calls + 1
		return point.x
	end 'greet'
end 'Polite'

default Greeter = spawn Polite.create()

function main() returns ExitCode
	let answer = try await Greeter.current().greet(Point.create(3)) otherwise 0
	return answer as ExitCode
end 'main'
```
```maxoncstderr
error E3217: <fragment>:29:19: parameter `point` of the message `Polite.greet` is a `Point` with no capability word, and this `spawn` makes `Polite` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `greet(point isolated …)` or `greet(point immutable …)`
```

<!-- test: marked-parameters-replies-and-factories-run -->
`isolated` gives the argument away and `immutable` shares it; the same two words on a reply and on the factory's parameter.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plotter
	var total as Integer

	static function create(origin immutable Point) returns Self
		return Self{total: origin.x}
	end 'create'

	export function plot(point isolated Point) returns Integer
		let added = point.x + point.y

		self.total = self.total + added

		return self.total
	end 'plot'

	export function shifted(delta immutable Point) returns Integer
		return self.total + delta.x + delta.y
	end 'shifted'

	export function next() returns isolated Point
		return Point.create(self.total, y: 1)
	end 'next'

	export function pinned() returns immutable Point
		return freeze Point.create(self.total, y: 2)
	end 'pinned'
end 'Plotter'

function main() returns ExitCode
	let origin = freeze Point.create(10, y: 0)
	let plotter = spawn Plotter.create(origin)
	let shape = Point.create(3, y: 4) as isolated Point
	let delta = freeze Point.create(100, y: 200)
	let total = try await plotter.plot(shape) otherwise panic("main: a fresh plotter answers plot")
	let moved = try await plotter.shifted(delta) otherwise panic("main: a fresh plotter answers shifted")
	let made = try await plotter.next() otherwise panic("main: a fresh plotter answers next")
	let kept = try await plotter.pinned() otherwise panic("main: a fresh plotter answers pinned")
	plotter.shutdown()
	print("{total} {moved} {made.x} {made.y} {kept.x} {kept.y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
17 317 17 1 17 2
```

<!-- test: marked-union-parameters-replies-and-factories-run -->
A union that carries a `ByteArray` takes the same two words on a message parameter, a reply and the factory's parameter.

```maxon
typealias Integer = int(i64.min to i64.max)

union Outcome
	found(name ByteArray)
	missing
end 'Outcome'

type Finder
	var weight as Integer

	static function create(first isolated Outcome) returns Self
		return Self{weight: Self.weighed(first)}
	end 'create'

	static function weighed(outcome Outcome) returns Integer
		return match outcome 'weight'
			found(name) gives name.count() as Integer
			missing gives 0 as Integer
		end 'weight'
	end 'weighed'

	export function record(outcome isolated Outcome) returns Integer
		self.weight = self.weight + Self.weighed(outcome)

		return self.weight
	end 'record'

	export function compare(outcome immutable Outcome) returns Integer
		return self.weight + Self.weighed(outcome)
	end 'compare'

	export function latest() returns isolated Outcome
		return Outcome.found("latest".toByteArray())
	end 'latest'

	export function pinned() returns immutable Outcome
		return freeze Outcome.found("pinned".toByteArray())
	end 'pinned'
end 'Finder'

function main() returns ExitCode
	let first = Outcome.found("ab".toByteArray()) as isolated Outcome
	let finder = spawn Finder.create(first)
	let second = Outcome.found("cde".toByteArray()) as isolated Outcome
	let shared = freeze Outcome.found("fghij".toByteArray())
	let total = try await finder.record(second) otherwise panic("main: a fresh finder answers record")
	let compared = try await finder.compare(shared) otherwise panic("main: a fresh finder answers compare")
	let newest = try await finder.latest() otherwise panic("main: a fresh finder answers latest")
	let kept = try await finder.pinned() otherwise panic("main: a fresh finder answers pinned")
	finder.shutdown()

	let newestWeight = match newest 'newest'
		found(name) gives name.count()
		missing gives 0
	end 'newest'

	let keptWeight = match kept 'kept'
		found(name) gives name.count()
		missing gives 0
	end 'kept'

	print("{total} {compared} {newestWeight} {keptWeight}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 10 6 6
```

<!-- test: error.a-payload-free-union-takes-no-word -->
A union whose cases carry nothing is a number to the checker, so it has no region for `isolated` to qualify.

```maxon
typealias Integer = int(i64.min to i64.max)

union Signal
	stop
	go
end 'Signal'

type Relay
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function tell(signal isolated Signal)
		self.seen = self.seen + 1
	end 'tell'
end 'Relay'

function main() returns ExitCode
	let relay = spawn Relay.create()
	relay.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:16:30: Expected 'a type' but got 'isolated'
```

<!-- test: values-and-handles-need-no-word -->
Numbers, `bool`, `String`, `Character`, an enum, a service handle and a `SharedValue` cell cross on parameter, reply and factory with no capability word.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Counter = SharedValue with Integer

enum Mode
	fast
	slow
end 'Mode'

type Echo
	var calls as Integer

	static function create(start Integer, label String, mode Mode) returns Self
		let bonus = match mode 'bonus'
			fast gives 0 as Integer
			slow gives 100 as Integer
		end 'bonus'

		return Self{calls: start + (label.byteLength() as Integer) + bonus}
	end 'create'

	export function name(text String) returns String
		self.calls = self.calls + 1
		return text.clone()
	end 'name'

	export function twice(n Integer) returns Integer
		return n * 2
	end 'twice'

	export function pick(mode Mode) returns Mode
		return mode
	end 'pick'

	export function flip(on bool) returns bool
		return not on
	end 'flip'

	export function letter(c Character) returns Character
		return c.clone()
	end 'letter'

	export function bump(cell Counter)
		cell.publish(cell.current() + 1)
	end 'bump'

	export function read() returns Integer
		return self.calls
	end 'read'
end 'Echo'

type Relay
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function note(other Echo.handle) returns Echo.handle
		self.seen = self.seen + 1
		return other.clone()
	end 'note'
end 'Relay'

function main() returns ExitCode
	let echo = spawn Echo.create(1, label: "ab", mode: Mode.fast)
	let relay = spawn Relay.create()
	let cell = Counter.create(0 as Integer)
	let text = try await echo.name("hello") otherwise panic("main: echo answers name")
	let doubled = try await echo.twice(21) otherwise panic("main: echo answers twice")
	let mode = try await echo.pick(Mode.slow) otherwise panic("main: echo answers pick")
	let flipped = try await echo.flip(true) otherwise panic("main: echo answers flip")
	let letter = try await echo.letter('q') otherwise panic("main: echo answers letter")
	try echo.bump(cell.clone()) otherwise panic("main: echo takes bump")
	let calls = try await echo.read() otherwise panic("main: echo answers read")
	let again = try await relay.note(echo.clone()) otherwise panic("main: relay answers note")
	let doubledAgain = try await again.twice(4) otherwise panic("main: the handed-back handle still reaches echo")
	relay.shutdown()
	echo.shutdown()
	print("{text} {doubled} {mode == Mode.slow} {flipped} {letter} {cell.current()} {calls} {doubledAgain}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello 42 true false q 1 4 8
```

<!-- test: a-unique-record-needs-no-word -->
A `unique` record moves on a send, so its parameter carries no capability word.

```maxon
typealias Integer = int(i64.min to i64.max)

type Token is unique
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Token'

type Vault
	var held as Integer

	static function create() returns Self
		return Self{held: 0}
	end 'create'

	export function deposit(token Token)
		self.held = self.held + (token.label.byteLength() as Integer)
	end 'deposit'

	export function read() returns Integer
		return self.held
	end 'read'
end 'Vault'

function main() returns ExitCode
	let vault = spawn Vault.create()
	let token = Token.create("secret")
	try vault.deposit(token) otherwise panic("main: a fresh vault takes its first message")
	let held = try await vault.read() otherwise panic("main: a fresh vault answers read")
	vault.shutdown()
	print("{held}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: an-os-handle-needs-no-word-on-a-factory-or-a-reply -->
<!-- procs: 4 -->
A `TcpListener` and a `TcpClient` each own an OS socket, so each is `unique`: the listener crosses into the factory and the accepted connection crosses back as a reply, with no capability word on either.
```maxon
typealias Tally = int(0 to 1000000)

type Acceptor
	var listener as TcpListener
	var accepted as Tally

	static function create(listener TcpListener) returns Self
		return Self{listener: listener, accepted: 0}
	end 'create'

	export function next() returns TcpClient throws NetworkError
		self.accepted = self.accepted + 1

		return try self.listener.accept()
	end 'next'
end 'Acceptor'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()
	let acceptor = spawn Acceptor.create(listener)
	let pending = try acceptor.next() otherwise return 2
	let out = try TcpClient.connect("127.0.0.1", port: port) otherwise 'refused3x23'
		pending.cancel()
		return 3
	end 'refused3x23'
	let served = try await pending otherwise return 4

	_ = try out.send("handle") otherwise return 5

	let heard = try served.recv(64) otherwise return 6

	print("{heard}\n")

	return 0
end 'main'
```
```stdout
handle
```
```exitcode
0
```

<!-- test: a-record-holding-an-os-handle-needs-no-word -->
<!-- procs: 4 -->
A record with a field that is an OS handle is `unique` by that field, so it moves on a send and its parameter carries no capability word.
```maxon
type Door
	var listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	export function port() returns NetworkPort
		return self.listener.port()
	end 'port'
end 'Door'

type Gatehouse
	var visits as NetworkPort

	static function create() returns Self
		return Self{visits: 0}
	end 'create'

	export function portOf(door Door) returns NetworkPort
		return door.port()
	end 'portOf'
end 'Gatehouse'

function main() returns ExitCode
	let door = Door.create(try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1)
	let port = door.port()
	let gatehouse = spawn Gatehouse.create()
	let seen = try await gatehouse.portOf(door) otherwise return 2

	gatehouse.shutdown()
	print("{seen == port}\n")

	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.a-record-holding-no-handle-keeps-its-word-beside-a-handle -->
A `TcpListener` parameter is accepted unmarked, and the plain `Point` beside it is still refused.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Watcher
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function watch(listener TcpListener, point Point)
		self.total = self.total + point.x + (listener.port() as Integer)
	end 'watch'
end 'Watcher'

function main() returns ExitCode
	let watcher = spawn Watcher.create()
	watcher.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:25:16: parameter `point` of the message `Watcher.watch` is a `Point` with no capability word, and this `spawn` makes `Watcher` a service — whose messages hand their arguments to another green thread, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `watch(point isolated …)` or `watch(point immutable …)`
```

<!-- test: error.a-promise-parameter-keeps-e3217-after-an-unmarked-record -->
The `Promise` parameter is refused first, by the rule that has always refused it, though the unmarked `Point` is declared before it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function plot(point Point)
		self.count = self.count + point.x
	end 'plot'

	export function hold(p IntPromise)
		self.count = self.count + 1
	end 'hold'
end 'Calc'

function main() returns ExitCode
	let calc = spawn Calc.create()
	calc.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:30:13: parameter `p` of the message `Calc.hold` is a Promise, which is a green-thread handle its awaiter owns, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.a-reply-the-cell-cannot-carry-keeps-e3140-after-an-unmarked-record -->
`wipe` throws a payload-carrying union, refused first by the rule that has always refused it, though `plot` declares an unmarked `Point` before it.

```maxon
typealias Integer = int(i64.min to i64.max)

union Trouble implements Error
	detail(text String)
end 'Trouble'

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function plot(point Point)
		self.n = self.n + point.x
	end 'plot'

	export function wipe() returns Integer throws Trouble
		throw Trouble.detail("no")
	end 'wipe'
end 'Store'

function main() returns ExitCode
	let store = spawn Store.create()
	store.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3140: <fragment>:33:14: the message `Store.wipe` declares a reply that throws `Trouble`, a PAYLOAD-CARRYING union whose error flag is a heap box pointer — a reply's one error word holds an `ordinal + bias`, and a pointer is not an ordinal, and this `spawn` makes `Store` a service — whose reply-bearing messages resolve through a CELL, a green thread that never runs, carrying one value word and one error word. Return an integer, a `String`, a struct or a service handle, and throw a payload-free `enum`; or drop the `returns` and `throws` clauses, which makes the message fire-and-forget and gives it no reply to carry
```

<!-- test: error.a-state-field-holding-a-promise-keeps-e3216-beside-an-unmarked-record -->
The state's promise is refused before the message's unmarked `Point` is looked at.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromiseArray = Array with IntPromise
typealias IntPromise = Promise with Integer

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Svc
	var pending as IntPromiseArray

	static function create() returns Self
		return Self{pending: IntPromiseArray.create(0)}
	end 'create'

	export function plot(point Point) returns Integer
		return point.x
	end 'plot'
end 'Svc'

function main() returns ExitCode
	let svc = spawn Svc.create()
	svc.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3216: <fragment>:27:12: `Svc.pending` is a `IntPromiseArray` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Svc` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: error.a-type-parameter-keeps-e3217-beside-an-unmarked-record -->
`put` takes a bare `T` and `plot` an unmarked `Point`; the opaque parameter is refused first.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Box uses T
	var item as T
	var count as Integer

	static function create(item T) returns Self
		return Self{item: item, count: 0}
	end 'create'

	export function plot(point Point)
		self.count = self.count + point.x
	end 'plot'

	export function put(x T)
		self.item = x
	end 'put'
end 'Box'

function main() returns ExitCode
	let box = spawn Box.create(1)
	box.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:30:12: parameter `x` of the message `Box.put` is an opaque type parameter, whose layout is not known at the send, and this `spawn` makes `Box` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: an-isolated-interface-value-is-merged-into-an-interface-field -->
`merge` of an isolated interface-typed parameter carries its witness into the field it fills, in a factory and in a
message.

```maxon
typealias Side = int(0 to 99)

interface Shape
	function sides() returns Side
end 'Shape'

type Square implements Shape
	export var edge as Side

	static function create(edge Side) returns Self
		return Self{edge: edge}
	end 'create'

	function sides() returns Side
		return 4
	end 'sides'
end 'Square'

type Triangle implements Shape
	export var edge as Side

	static function create(edge Side) returns Self
		return Self{edge: edge}
	end 'create'

	function sides() returns Side
		return 3
	end 'sides'
end 'Triangle'

type Keeper
	var last as Shape

	static function create(first isolated Shape) returns Self
		return Self{last: merge first}
	end 'create'

	export function keep(shape isolated Shape)
		self.last = merge shape
	end 'keep'

	export function sides() returns Side
		return self.last.sides()
	end 'sides'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create(Square.create(1))
	let first = try await keeper.sides() otherwise panic("main: a fresh keeper answers")
	try keeper.keep(Triangle.create(2)) otherwise panic("main: a running keeper takes a shape")
	let second = try await keeper.sides() otherwise panic("main: a fresh keeper answers")
	keeper.shutdown()
	return (first * 10 + second) as ExitCode
end 'main'
```
```exitcode
43
```

<!-- test: an-isolated-parameter-is-sent-on-as-an-isolated-argument -->
A function that receives an `isolated` value hands it to a message's `isolated` parameter, which moves it: the value
is the function's to give away, and no other name reaches it afterwards.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Chunk
	export var cells as IntArray

	static function create() returns Self
		return Self{cells: IntArray.create(0)}
	end 'create'
end 'Chunk'

type Sink
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(chunk isolated Chunk)
		let added = chunk.cells.count() as Integer
		self.total = self.total + added
	end 'add'

	export function read() returns Integer
		return self.total
	end 'read'
end 'Sink'

function forward(sink Sink.handle, chunk isolated Chunk)
	try sink.add(chunk) otherwise panic("forward: a running sink takes a chunk")
end 'forward'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let chunk = Chunk.create() as isolated Chunk

	modify chunk 'fill'
		chunk.cells.push(1)
		chunk.cells.push(2)
		chunk.cells.push(3)
	end 'fill'

	forward(sink, chunk: chunk)
	let total = try await sink.read() otherwise panic("main: a fresh sink answers")
	sink.shutdown()
	return total as ExitCode
end 'main'
```
```exitcode
3
```
