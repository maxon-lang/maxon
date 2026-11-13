---
feature: service-handle-cycles
status: experimental
keywords: [service, handle, cycle, E4014, refcount, leak]
category: memory-safety
---

# A service's state never reaches a handle to its own type

## Documentation

A service handle is counted by whoever holds it, and the service is freed when its last handle is dropped. A service whose
state holds a handle to itself, directly, through the handles of other services, or through an interface handle whose
conformers include it, therefore holds the count that would free it: the service is never freed and the leak gate reports
it. The check is **E4014**, the same one that refuses a type reaching itself through fields, and its type graph has an edge
from every service handle to the service it addresses and from every interface handle to each service that conforms to the
interface. The message names the cycle. The cure is to pass the handle with each message (`start(me Asker.handle)`), or to
use distinct types.

A function value holds what its closure captured, and a function type does not say what that is. A service state that
holds a function value, in a field, a union payload or a container element, therefore reaches the handle of every service
that some closure in the program captures, directly or through a type that holds one, and a cycle through it is the same
E4014. The rule is conservative: it does not trace which closure is stored where, so a service with a function-typed field
cannot share a program with a closure that captures a handle reaching that service, even when the two never meet. Pass the
handle as an argument to the call that needs it.

A handle that a message carries is not state: it lives only until the message returns. A chain of distinct services, each
holding the next one's handle, is not a cycle.

## Tests

<!-- test: a-service-holding-a-handle-to-its-own-type-is-refused -->
`Echo` keeps a handle to itself in a field.
```maxon
type Echo
	var count as Tally
	var me as Echo.handle

	static function create(me Echo.handle) returns Self
		return Self{count: 0, me: me}
	end 'create'

	export function bump(by Tally)
		self.count = self.count + by
	end 'bump'
end 'Echo'

function launch(peer Echo.handle) returns Echo.handle
	return spawn Echo.create(peer)
end 'launch'

function main() returns ExitCode
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E4014: <fragment>:2:6: type 'Echo' reaches a service handle that reaches it again (via Echo → me: Echo.handle → Echo); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: a-service-holding-its-own-handle-in-an-array-is-refused -->
A handle kept in a container is still held.
```maxon
typealias Tally = int(0 to u64.max)
typealias EchoHandleArray = Array with Echo.handle

type Echo
	var count as Tally
	var receivers as EchoHandleArray

	static function create() returns Self
		return Self{count: 0, receivers: EchoHandleArray.create(0)}
	end 'create'

	export function attach(receiver Echo.handle)
		self.receivers.push(receiver)
	end 'attach'
end 'Echo'

function main() returns ExitCode
	let echo = spawn Echo.create()
	echo.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:5:6: type 'Echo' reaches a service handle that reaches it again (via Echo → receivers: EchoHandleArray → Echo.handle → Echo); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: two-services-holding-each-others-handles-are-refused -->
`Ping` holds `Pong`'s handle and `Pong` holds `Ping`'s.
```maxon
typealias PongHandleArray = Array with Pong.handle
typealias PingHandleArray = Array with Ping.handle

type Ping
	var peers as PongHandleArray

	static function create() returns Self
		return Self{peers: PongHandleArray.create(0)}
	end 'create'

	export function meet(peer Pong.handle)
		self.peers.push(peer)
	end 'meet'
end 'Ping'

type Pong
	var peers as PingHandleArray

	static function create() returns Self
		return Self{peers: PingHandleArray.create(0)}
	end 'create'

	export function meet(peer Ping.handle)
		self.peers.push(peer)
	end 'meet'
end 'Pong'

function main() returns ExitCode
	let ping = spawn Ping.create()
	let pong = spawn Pong.create()
	ping.shutdown()
	pong.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:5:6: type 'Ping' reaches a service handle that reaches it again (via Ping → peers: PongHandleArray → Pong.handle → Pong → peers: PingHandleArray → Ping.handle → Ping); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: a-service-holding-an-interface-handle-it-conforms-to-is-refused -->
`Hub` conforms to `Listener` and keeps `Listener` handles, which may address `Hub` itself.
```maxon
typealias Tally = int(0 to u64.max)
typealias ListenerHandleArray = Array with Listener.handle

interface Listener
	function note(mutable self, by Tally)
end 'Listener'

type Hub implements Listener
	var count as Tally
	var listeners as ListenerHandleArray

	static function create() returns Self
		return Self{count: 0, listeners: ListenerHandleArray.create(0)}
	end 'create'

	export function note(by Tally)
		self.count = self.count + by
	end 'note'

	export function subscribe(listener Listener.handle)
		self.listeners.push(listener)
	end 'subscribe'
end 'Hub'

function main() returns ExitCode
	let hub = spawn Hub.create()
	hub.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:9:6: type 'Hub' reaches a service handle that reaches it again (via Hub → listeners: ListenerHandleArray → Listener.handle → Hub); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: a-generic-that-expands-with-every-instantiation-is-refused-in-a-services-state-without-walking-it -->
`Rec` holds a `Rec` over `Rec with T`, so every instantiation names a larger one. A service whose state holds `Rec with Tally`
makes the compiler read that state's graph for a promise; a walk over an instantiated type reads a field whose declaration
grows without bound (`fieldTypeGrowsItsOwner`, the test E3184 itself applies) as its declared type instead of expanding it, so
the refusal of the declaration (E3184) arrives at once whatever order the files are read in.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyRec = Rec with Tally

type Rec uses T
	typealias Wrapped = Rec with (Rec with T)

	var x as Wrapped
	var y as T

	static function create(y T) returns Self
		return Self{x: Wrapped.create(Rec.create(y), y: Rec.create(y)), y: y}
	end 'create'
end 'Rec'

type Sv
	var r as TallyRec

	static function create() returns Self
		return Self{r: TallyRec.create(0)}
	end 'create'

	export function touch()
		self.r = TallyRec.create(1)
	end 'touch'
end 'Sv'

function main() returns ExitCode
	let sv = spawn Sv.create()
	sv.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3184: <fragment>:8:11: field 'x' of generic type 'Rec' is declared 'Rec.Wrapped', which holds 'Rec' again over a type built from its own type parameters: instantiating it would nest without end. Declare the field over the type's own parameters, or remove the recursion
```

<!-- test: a-service-keeping-a-closure-that-captures-its-own-handle-is-refused -->
A function value holds what its closure captured, and the type of a function says nothing of that. A service state that holds a function value therefore reaches the handle of every service some closure in the program captures: here `attach` stores a closure that captures `Echo`'s own handle.
```maxon
typealias Tally = int(0 to u64.max)
typealias Notifier = function(Tally) returns Tally

function quiet(by Tally) returns Tally
	return by
end 'quiet'

function forward(target Echo.handle, by Tally) returns Tally
	try target.bump(by, sendTimeout: 0) otherwise ignore
	return by
end 'forward'

type Echo
	var count as Tally
	var notify as Notifier

	static function create() returns Self
		return Self{count: 0, notify: quiet}
	end 'create'

	export function bump(by Tally)
		self.count = self.count + by
	end 'bump'

	export function attach(me Echo.handle)
		self.notify = function(by Tally) gives forward(me, by: by)
	end 'attach'
end 'Echo'

function main() returns ExitCode
	let echo = spawn Echo.create()
	echo.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:14:6: type 'Echo' reaches a service handle that reaches it again (via Echo → notify: function value → Echo.handle → Echo); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: two-services-keeping-closures-that-capture-each-others-handles-are-refused -->
`Ping` keeps a closure over `Pong`'s handle and `Pong` keeps one over `Ping`'s. Each service's function-typed field reaches both captured handles, so the cycle is found whichever service is read first.
```maxon
typealias Tally = int(0 to u64.max)
typealias Notifier = function(Tally) returns Tally

function quiet(by Tally) returns Tally
	return by
end 'quiet'

function nudgePong(target Pong.handle, by Tally) returns Tally
	try target.poke(by, sendTimeout: 0) otherwise ignore
	return by
end 'nudgePong'

function nudgePing(target Ping.handle, by Tally) returns Tally
	try target.poke(by, sendTimeout: 0) otherwise ignore
	return by
end 'nudgePing'

type Ping
	var hook as Notifier

	static function create() returns Self
		return Self{hook: quiet}
	end 'create'

	export function poke(by Tally)
		_ = self.hook(by)
	end 'poke'

	export function meet(peer Pong.handle)
		self.hook = function(by Tally) gives nudgePong(peer, by: by)
	end 'meet'
end 'Ping'

type Pong
	var hook as Notifier

	static function create() returns Self
		return Self{hook: quiet}
	end 'create'

	export function poke(by Tally)
		_ = self.hook(by)
	end 'poke'

	export function meet(peer Ping.handle)
		self.hook = function(by Tally) gives nudgePing(peer, by: by)
	end 'meet'
end 'Pong'

function main() returns ExitCode
	let ping = spawn Ping.create()
	let pong = spawn Pong.create()
	ping.shutdown()
	pong.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:19:6: type 'Ping' reaches a service handle that reaches it again (via Ping → hook: function value → Ping.handle → Ping); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: a-service-holding-an-interface-implemented-by-a-generic-over-its-own-handle-is-refused -->
`Echo` holds a `Sink`, and `Box with Echo.handle` implements `Sink`: the interface field may hold a `Box` whose type argument is `Echo`'s handle, so the walk substitutes the argument of the generic conformer.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
end 'Sink'

type Box uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'
end 'Box'

typealias EchoBox = Box with Echo.handle

type Echo
	var sink as Sink

	static function create(me Echo.handle) returns Self
		return Self{sink: EchoBox.create(me)}
	end 'create'

	export function ping()
		self.sink.take(1)
	end 'ping'
end 'Echo'

function launch(peer Echo.handle) returns Echo.handle
	return spawn Echo.create(peer)
end 'launch'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E4014: <fragment>:23:6: type 'Echo' reaches a service handle that reaches it again (via Echo → sink: Sink → EchoBox → Echo.handle → Echo); a service's handle is counted by its holder, so the cycle is never freed. Pass the handle with each message, or use distinct types
```

<!-- test: a-function-typed-field-is-accepted-when-no-closure-captures-a-handle-that-reaches-its-service -->
<!-- procs: 1 -->
<!-- preempt: off -->
`Echo` keeps a function value, and a closure elsewhere captures a handle to `Logger`, a service whose state holds no function value: the function field reaches `Logger` and `Logger` reaches nothing back.
```maxon
typealias Tally = int(0 to u64.max)
typealias Notifier = function(Tally) returns Tally

function quiet(by Tally) returns Tally
	return by
end 'quiet'

function forward(target Logger.handle, by Tally) returns Tally
	try target.add(by, sendTimeout: 0) otherwise ignore
	return by
end 'forward'

type Logger
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(by Tally)
		self.total = self.total + by
	end 'add'
end 'Logger'

type Echo
	var notify as Notifier

	static function create() returns Self
		return Self{notify: quiet}
	end 'create'

	export function ping(by Tally) returns Tally
		return self.notify(by)
	end 'ping'
end 'Echo'

function main() returns ExitCode
	let logger = spawn Logger.create()
	let relay = function(by Tally) gives forward(logger, by: by)
	let echo = spawn Echo.create()
	let got = try await echo.ping(3) otherwise 0
	let relayed = relay(2)
	echo.shutdown()
	print("{got} {relayed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 2
```

<!-- test: a-chain-of-distinct-services-holding-each-others-handles-is-accepted -->
<!-- procs: 1 -->
<!-- preempt: off -->
`Front` holds `Middle`'s handle and `Middle` holds `Back`'s: the handle graph has no cycle, so every service is freed and the run exits cleanly.
```maxon
typealias Tally = int(0 to u64.max)

type Back
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(by Tally)
		self.total = self.total + by
	end 'add'
end 'Back'

type Middle
	let back as Back.handle

	static function create(back Back.handle) returns Self
		return Self{back: back}
	end 'create'

	export function relay(by Tally)
		try self.back.add(by, sendTimeout: 0) otherwise ignore
	end 'relay'
end 'Middle'

type Front
	let middle as Middle.handle

	static function create(middle Middle.handle) returns Self
		return Self{middle: middle}
	end 'create'

	export function poke(by Tally)
		try self.middle.relay(by, sendTimeout: 0) otherwise ignore
	end 'poke'
end 'Front'

function main() returns ExitCode
	let back = spawn Back.create()
	let middle = spawn Middle.create(back.clone())
	let front = spawn Front.create(middle.clone())
	try front.poke(3, sendTimeout: ForeverMs) otherwise return 1
	front.shutdown()
	middle.shutdown()
	back.shutdown()
	print("chain built\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
chain built
```

<!-- test: passing-the-handle-with-each-message-is-accepted -->
<!-- procs: 1 -->
<!-- preempt: off -->
`Asker` and `Oracle` need each other's handle for one exchange, so each message carries the handle it needs and neither service's state keeps one.
```maxon
typealias Tally = int(0 to u64.max)

type Oracle
	var asked as Tally

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function ask(back Asker.handle, question Tally)
		self.asked = self.asked + 1
		try back.answer(question * 2, sendTimeout: 0) otherwise ignore
	end 'ask'
end 'Oracle'

type Asker
	var got as Tally

	static function create() returns Self
		return Self{got: 0}
	end 'create'

	export function start(oracle Oracle.handle, me Asker.handle)
		try oracle.ask(me.clone(), question: 21, sendTimeout: 0) otherwise ignore
	end 'start'

	export function answer(value Tally)
		self.got = value
	end 'answer'

	export function result() returns Tally
		return self.got
	end 'result'
end 'Asker'

function main() returns ExitCode
	let oracle = spawn Oracle.create()
	let asker = spawn Asker.create()
	try asker.start(oracle, me: asker.clone()) otherwise return 1
	var got = 0 as Tally

	while got == 0 'untilAnswered'
		Scheduler.yield()
		got = try await asker.result() otherwise 0
	end 'untilAnswered'

	print("answer {got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
answer 42
```
