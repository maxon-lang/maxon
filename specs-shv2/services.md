---
feature: services
status: experimental
keywords: [spawn, services, actors, mailbox, concurrency, green-threads]
category: concurrency
---

# Services — `spawn`, and the two types the compiler synthesizes beside one

## Documentation

A **service** is an ordinary `type`. There is no `service` keyword and no member keyword; the only new
syntax in the whole feature is `spawn`, a call-site prefix like `async`:

```text
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)                 // a MESSAGE: export + instance
		self.count = self.count + by
	end 'bump'

	function record(v int) returns int           // private: NOT on the handle
		return v
	end 'record'
end 'Calc'

let c = Calc.create()          // a plain value — direct calls, fully testable
let h = spawn Calc.create()    // a `Calc.handle` — the same methods, as messages
```

### The export boundary IS the isolation boundary

`spawn` yields `Calc.handle`, whose method surface is **exactly `Calc`'s `export`/`public` INSTANCE
surface**. Three properties fall out:

1. **No method ever changes meaning.** Dispatch is decided by the receiver's TYPE — a `Calc` value takes
   direct calls, a `Calc.handle` takes messages. Same spelling; that is location transparency.
2. **Services are synchronously unit-testable.** Construct one directly and call its methods with no
   runtime, no mailbox and no green thread.
3. **Self-send deadlock is structurally impossible.** A private helper is not on the handle, so
   `self.record(…)` inside a message can only ever be a direct call. There is no way to spell a self-send,
   so it needs no diagnostic.

`static function` is excluded from the roster structurally: it has no `self`, and `spawn Calc.create()`
calls it DIRECTLY rather than through a handle.

### `spawn` is for services only

⚖ There is no bare `spawn f()` green thread. Every `spawn` names a **static factory of a declared type
returning that type**, and everything else is **E3134**. The unit of concurrency is a service, whose
message surface the compiler can check — a fan-out over an unstructured green thread would be a second
primitive with none of that.

### What the compiler synthesizes

Two companion types, under the DOTTED names an author writes, so a type reference to either needs no new
grammar:

| name | what it is |
|---|---|
| `Calc.request` | a union — `__shutdown` at variant 0, then one variant per message in declaration order. A message's payload is its parameters, plus an integer `__reply` slot when it has a reply to deliver. |
| `Calc.handle` | a one-field struct holding the mailbox pointer. A real 8-byte box, so moves, E3102, struct fields, arrays and the drop cascade all reach it by construction. |

`__shutdown` is variant **0** so that adding a message never renumbers the synthesized one.

Whether a type is a service is a **whole-program** property: a `spawn` anywhere makes the type a service
and subjects all its export methods to the service rules. That is why every service diagnostic fires **at
the `spawn`** rather than at the method it names — the method may be in a different file entirely.

### Sending MOVES, and that is load-bearing

A message's arguments are **moved** into the service, which becomes their one owner. This is not
ergonomics: a refcount step in this compiler is a plain load/add/store, because the language guarantees
one green thread per box. A send that let two green threads hold one box would not be slow, it would
corrupt the heap. So a parameter whose value cannot have exactly one owner on the far side is **E3135** —
a `Promise` (a handle its awaiter owns), a function value (whose captured environment is shared), a value
held at an interface type (a fat pointer released through a witness), an opaque type parameter (no layout
at the send).

### `ServiceError`

A service can be gone: `h.shutdown()` enqueues a poison pill behind everything queued, and dropping the last
handle closes the mailbox, which drains it the same way. So `stdlib/Builtins.maxon` declares

```text
public enum ServiceError implements Error
	stopped
end 'ServiceError'
```

which a reply's error type will be merged with. It carries no `__` prefix precisely so a user can write
`match e … stopped …`.

## Targets

⚠ **A `<!-- targets: x64-windows -->` MARKS EXACTLY THE CASES THAT START A SERVICE, AND NOTHING ELSE.** A
running service reaches a green thread's context switch, which is hand-assembled x64 — so those cases are
restricted for the reason `specs-shv2/async-scheduler.md`'s own Targets section gives, and every one of
them carries the marker.

⚠ **Every REFUSAL in this file is unmarked, and that is the same rule read from the other end.** A verdict
reached before a backend — a token shape, a declaration, a transferability rule, a move — is target-neutral
by construction, so a marker on one would hide a green lane rather than describe a red one. `spawn-is-not-a-keyword`
and `service-error-is-declared-and-nameable` are unmarked for the neighbouring reason: they RUN, but they
start no service and reach no scheduler at all.

## Tests

<!-- test: companions-resolve-as-types -->
<!-- targets: x64-windows -->
A `spawn` makes the type a service, and both synthesized companions are then nameable TYPES — in a
signature written by a function that spawns nothing.

⭐ **IT CAN NOW FAIL ON ITS OWN CLAIM, AND WAVE 2's NOTE THAT IT COULD NOT IS SPENT.** While the wave-3
E2015 stood at the `spawn`, the file's parse ended before an unresolved `Calc.handle` was ever reported, so
this case passed with the whole-program spawn walk disabled and only the two CROSS-FILE cases could see the
companions. That throw is gone: the program now parses to completion, runs, and an absent companion is
`E3011 Unknown type 'Calc.handle'` at `serve`'s own signature.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'

	export function total() returns int
		return self.count
	end 'total'

	function record(v int) returns int
		return v
	end 'record'
end 'Calc'

function serve(h Calc.handle) returns int
	return 1
end 'serve'

function describe(r Calc.request) returns int
	return 2
end 'describe'

function main() returns ExitCode
	let h = spawn Calc.create()
	return serve(h) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: companions-come-from-a-spawn-in-another-file -->
<!-- targets: x64-windows -->
⭐ Whether a type is a service is a WHOLE-PROGRAM property. The file that declares `Calc` and names
`Calc.handle` writes no `spawn` at all; the file that spawns declares nothing. The companions exist
because the two are compiled together — the `Unknown type` a per-file decision would report is what this
case is against.

⭐ **IT WAS THE ONE CASE IN THIS FILE THAT COULD FAIL ON THE COMPANIONS, AND IT WAS SEEN RED.** SABOTAGE
MEASURED at wave 2: with the whole-program spawn probe removed from the pre-fold token walk, this case
reported `error E3011: Unknown type 'Calc.handle'` while every other case in the file stayed green.
`calc.maxon` parses to completion — its `spawn` is in the other file — so the unresolved companion is
actually reached, which is precisely what a single-file case could not arrange while a throw stood at the
`spawn`.
```maxon
// --- file: calc.maxon
export type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(h Calc.handle) returns int
	return 1
end 'serve'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	return serve(h) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-spawn-inside-a-method-body-is-found -->
<!-- targets: x64-windows -->
⭐ The walk that decides which types are services reads EVERY token of every file, and this is the case
that says why it cannot ride the declaration sweep: that sweep consumes a `type` declaration whole and
resumes past its `end`, so a `spawn` in a METHOD BODY would be invisible to an arm written inside it. The
only `spawn` in this program is inside `Runner.start`.

⚠ **IT IS SPLIT ACROSS TWO FILES ON PURPOSE, for `companions-come-from-a-spawn-in-another-file`'s measured
reason.** `Runner.start`'s `spawn` is the only one in the program, and `calc.maxon` — which parses to
completion and names `Calc.handle` in a signature — is where a walk that skipped method bodies leaves the
companion unresolved.
```maxon
// --- file: calc.maxon
export type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(h Calc.handle) returns int
	return 1
end 'serve'

// --- file: runner.maxon
type Runner
	var started as int

	static function create() returns Self
		return Self{started: 0}
	end 'create'

	function start() returns int
		let h = spawn Calc.create()
		return 1
	end 'start'
end 'Runner'

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.spawn-of-a-bare-function-refused -->
⚖ There is no unstructured green thread in this language. `spawn work()` names no type, so it starts no
service, and the refusal teaches the one form rather than reporting a syntax error.
```maxon
function work() returns int
	return 1
end 'work'

function main() returns ExitCode
	let h = spawn work()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:7:10: `spawn work…` does not start a service: `spawn` is followed by a STATIC CALL on a type, and there is no bare `spawn f()` green thread — the unit of concurrency is a service, whose message surface the compiler can check. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-of-an-undeclared-type-refused -->
Nothing declares `type Calc`, so there is no type to be a service.
```maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:3:10: `spawn Calc.create(…)` does not start a service: nothing declares `type Calc`. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-of-an-instance-method-refused -->
`bump` is a message, not a factory. A `spawn` calls its factory DIRECTLY — there is no service yet for a
message to reach — so the target must be a `static`.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.bump()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:15:10: `spawn Calc.bump(…)` does not start a service: `Calc.bump` is an INSTANCE method, and a `spawn` calls its factory directly — there is no service yet for a message to reach. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-of-a-static-that-returns-something-else-refused -->
A static that does not hand back the type has produced no state for the message loop to own.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function version() returns int
		return 3
	end 'version'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.version()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:19:10: `spawn Calc.version(…)` does not start a service: `Calc.version` does not hand back a `Calc` the service can own — a service's state is the BOX of a declared `type`, and this factory's recorded return type is not one. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-self-dot-refused -->
The whole-program walk that decides which types are services reads tokens with no type scope, so `Self`
resolves to nothing there. It is refused in its own words rather than dying as an unexpected token.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function start() returns int
		let h = spawn Self.create()
		return 0
	end 'start'
end 'Calc'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:11: Unsupported: `spawn Self.…` — the whole-program walk that decides which types are services reads tokens with no type scope, so `Self` names nothing there. Write the type outright (`spawn Calc.create()`)
```

<!-- test: error.message-param-promise-not-transferable -->
A send MOVES its arguments to another green thread, and a `Promise` is a handle its awaiter owns. The
diagnostic fires at the `spawn`, because that is what made `Calc` a service.
```maxon
typealias IntPromise = Promise with int

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function hold(p IntPromise)
		self.count = self.count + 1
	end 'hold'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3135: <fragment>:17:10: parameter `p` of the message `Calc.hold` is a Promise, which is a green-thread handle its awaiter owns, and this `spawn` makes `Calc` a service — whose messages MOVE their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-function-value-not-transferable -->
A function value reaches a captured environment block, which is a box with a second referent by
construction.
```maxon
typealias IntOp = function(n int) returns int

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function apply(op IntOp)
		self.count = op(self.count)
	end 'apply'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3135: <fragment>:17:10: parameter `op` of the message `Calc.apply` is a function value, whose captured environment is a box a second thread would share, and this `spawn` makes `Calc` a service — whose messages MOVE their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-interface-value-not-transferable -->
⭐ A value held at an interface type is a fat pointer: a witness half in read-only data and a value half
released through `__drop_existential`. The declaration sweep leaves such a parameter spelled as a bare
name — it re-tags a struct FIELD and a RETURN type and never a parameter — so this case is what stops the
rule from being silently blind to the one shape whose two halves the request union cannot carry.
```maxon
interface Shape
	function area() returns int
end 'Shape'

type Square implements Shape
	var side as int

	function area() returns int
		return self.side * self.side
	end 'area'
end 'Square'

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3135: <fragment>:27:10: parameter `s` of the message `Calc.measure` is a value held at an interface type, which is a fat pointer released through a witness the request union cannot carry, and this `spawn` makes `Calc` a service — whose messages MOVE their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.overloaded-message-refused -->
A message becomes ONE variant of the request union, and one variant carries one payload shape.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(n int)
		self.count = self.count + n
	end 'add'

	export function add(a int, b int)
		self.count = self.count + a + b
	end 'add'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:10: Unsupported: `Calc.add` is a message of the service `Calc` and is declared 2 times. A message becomes ONE variant of the synthesized `Calc.request` union, and one variant carries one payload shape — so an overloaded message has no single shape to become. Give the overloads distinct names
```

<!-- test: error.generic-service-refused -->
The companions are monomorphic by construction — one union and one handle struct per service, not one
pair per instantiation.
```maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function peek() returns T
		return self.item
	end 'peek'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(1)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: `spawn Box.create(…)` — `type Box` is generic, and a service's companions (`Box.request`, `Box.handle`) are monomorphic: ONE union and ONE handle struct per service, not one pair per instantiation. Spawn a non-generic type, or declare a concrete wrapper around the generic one
```

<!-- test: a-private-method-is-not-a-message -->
Only `export`/`public` INSTANCE methods are messages. `record` is file-private and `version` is a static,
so neither is subject to the transferability rule — a `Promise` parameter on either is perfectly legal on
a type that is spawned, which is what this case pins. Compare
`error.message-param-promise-not-transferable`, whose only difference is the `export`.
```maxon
typealias IntPromise = Promise with int

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function fromPromise(p IntPromise) returns Self
		return Self{count: 0}
	end 'fromPromise'

	function record(p IntPromise) returns int
		return 1
	end 'record'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:25:10: Unsupported: services are not lowered yet — SV1 wave 3
```

<!-- test: a-factory-may-be-spelled-with-a-keyword -->
⭐ A keyword may be a DECLARED NAME (D8), and `stdlib/FilePath.maxon:34` proves the shape is live corpus:
`public static function from (path String) returns FilePath`. So `spawn Reader.from(3)` must be recognized
as a spawn — both halves of `<Type>.<factory>` go through the same name reader every other declaration
position uses.

⚠ Found by probing this rung's own mechanism, and it WAS red: with an `identifier`-only test the program
earned the SHAPE refusal (*"`spawn` is followed by a STATIC CALL on a type"*), which is a sentence about a
program the author did not write. The whole-program discovery walk had the identical narrowing, and the two
must widen together or one accepts a spawn the other minted no companions for.
```maxon
type Reader
	var n as int

	static function from(path int) returns Self
		return Self{n: path}
	end 'from'

	export function read() returns int
		return self.n
	end 'read'
end 'Reader'

function serve(h Reader.handle) returns int
	return 1
end 'serve'

function main() returns ExitCode
	let h = spawn Reader.from(3)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:10: Unsupported: services are not lowered yet — SV1 wave 3
```

<!-- test: spawn-is-not-a-keyword -->
⭐ `spawn` is a CONTEXTUAL keyword and must stay an ordinary identifier everywhere else: a declared
static named `spawn`, a call to it, a parameter, a local, and a field all keep their meaning. Two live
declarations in this tree depend on it (`specs-shv2/associated-types.md` declares
`static function spawn() returns Self`; `stdlib/Subprocess.maxon` declares `Subprocess.spawn`), so a
`TokenKind` would have retokenized both.
```maxon
type Job
	var spawn as int

	static function spawn(n int) returns Self
		return Self{spawn: n}
	end 'spawn'

	function tally() returns int
		return self.spawn
	end 'tally'
end 'Job'

function run(spawn int) returns int
	return spawn + 1
end 'run'

function main() returns ExitCode
	let j = Job.spawn(3)
	let spawn = 4
	return (j.tally() + run(spawn) + spawn) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: service-error-is-declared-and-nameable -->
`ServiceError` is declared by `stdlib/Builtins.maxon` and carries no `__` prefix, so a user may throw it
and name its case in a `match`. The runtime that produces it lands with the mailbox; the declaration is
what a reply's synthesized error union will be built from.
```maxon
function risky(n int) returns int throws ServiceError
	if n == 0 'gone'
		throw ServiceError.stopped
	end 'gone'
	return n
end 'risky'

function main() returns ExitCode
	let v = try risky(0) otherwise (e) 'failed'
		match e 'why'
			stopped then return 7 as ExitCode
		end 'why'
	end 'failed'
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: spawn-send-and-the-service-runs -->
<!-- targets: x64-windows -->
⭐ The first case in this file that starts a real green thread. `spawn` hands back a `Counter.handle`; a call
on that handle is a MESSAGE, enqueued and returned from at once; the service's own green thread runs the
handler. Dropping the handle at the end of `main` closes the mailbox, the loop's `recv` answers 0 and the
service exits — which the exit drain runs out before the leak gate reads its counters.

⚠ **`main` PRINTS NOTHING, AND THAT IS THE RULE EVERY RUNNING CASE IN THIS FILE FOLLOWS.** A service runs on
its own green thread and, above one processor, on another OS thread — so `main`'s output and a handler's are
ordered by nothing. Every expectation below is therefore built out of prints made by ONE service at a time,
whose order is its own mailbox's FIFO.
```maxon
type Counter
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function report()
		print("n={self.n}\n")
	end 'report'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.tick()
	h.tick()
	h.report()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=2
```

<!-- test: messages-are-serialized-in-fifo-order -->
<!-- targets: x64-windows -->
Handlers run one at a time, in send order — so three digits pushed in order read back as one number.
```maxon
type Log
	var acc as int

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d int)
		self.acc = self.acc * 10 + d
	end 'push'

	export function read()
		print("acc={self.acc}\n")
	end 'read'
end 'Log'

function main() returns ExitCode
	let h = spawn Log.create()
	h.push(1)
	h.push(2)
	h.push(3)
	h.read()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
acc=123
```

<!-- test: two-instances-are-independent -->
<!-- targets: x64-windows -->
Two spawns of one type are two services with two states.

⚠ **THE ORDER OF THE TWO PRINTS IS FORCED BY CAUSALITY AND NOT BY LUCK.** `b` prints its own count and then
sends `report` to `a`, so `a`'s line cannot be written until `b`'s handler has already written its own — two
services printing on two green threads with nothing between them would be ordered by the scheduler. Moving
`a`'s handle INTO `b`'s mailbox is what buys that ordering, and it is the same transfer
`a-handle-moved-into-another-service` pins on its own.
```maxon
type Counter
	var id as int
	var n as int

	static function create(id int) returns Self
		return Self{id: id, n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function report()
		print("{self.id}={self.n}\n")
	end 'report'

	export function reportThen(peer Counter.handle)
		print("{self.id}={self.n}\n")
		peer.report()
	end 'reportThen'
end 'Counter'

function main() returns ExitCode
	let a = spawn Counter.create(1)
	let b = spawn Counter.create(2)
	a.tick()
	a.tick()
	b.tick()
	b.reportThen(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2=1
1=2
```

<!-- test: the-same-type-is-used-directly-and-spawned -->
<!-- targets: x64-windows -->
⭐ The location-transparency property, and the test a dedicated `service` declaration would have made
impossible: one type, one method, reached both ways in one program. The direct calls run before the `spawn`
exists, so the two lines are ordered by the program rather than by the scheduler.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'

	export function total()
		print("total={self.count}\n")
	end 'total'
end 'Calc'

function main() returns ExitCode
	var direct = Calc.create()
	direct.bump(4)
	direct.total()

	let h = spawn Calc.create()
	h.bump(3)
	h.total()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total=4
total=3
```

<!-- test: error.a-private-method-is-absent-from-the-handle -->
A private helper is not on the handle, which is what makes a self-send unspellable.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function record(v int)
		self.count = v
	end 'record'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.record(1)
	return 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:15:4: Type 'Calc.handle' has no member named 'record'
```

<!-- test: shutdown-drains-what-is-queued -->
<!-- targets: x64-windows -->
`shutdown()` is a graceful drain, not a kill: the poison pill goes in BEHIND everything already queued, so
every message sent before it still runs.

⚠ `shutdown` is the handle's own method and is not a message of `Log`. It follows `clone`'s precedent
exactly (`Parser.structCloneIsSynthesized`): a service that declares an `export function shutdown()` of its
own WINS, and the compiler's pill is then unspellable for it — which leaves dropping the last handle, the
other road to the same drain.
```maxon
type Log
	var acc as int

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d int)
		self.acc = self.acc + d
	end 'push'

	export function read()
		print("acc={self.acc}\n")
	end 'read'
end 'Log'

function main() returns ExitCode
	let h = spawn Log.create()
	h.push(1)
	h.push(2)
	h.read()
	h.shutdown()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
acc=3
```

<!-- test: a-send-after-shutdown-is-dropped-cleanly -->
<!-- targets: x64-windows -->
⭐⭐ **THE CASE THAT REACHES THE ABANDON PATH, AND IT REACHES IT BY EITHER OF TWO ROADS.** The second `keep`
is sent after the poison pill, so the loop never runs its handler — and which road drops its `String` is a
race this case deliberately does not resolve: if the send wins, the envelope is queued behind the pill and
the loop's exit drain abandons it; if the loop wins, the mailbox is already closed and `__mbox_send` abandons
it inline. **Both roads must print exactly the same thing and leak exactly nothing**, which is the property
worth pinning — the drop of a moved-in payload nobody will ever handle.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let first = "a{1}"
	h.keep(first)
	h.shutdown()
	let second = "b{2}"
	h.keep(second)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
kept a1
```

<!-- test: dropping-the-last-handle-shuts-the-service-down -->
<!-- targets: x64-windows -->
An ordinary program needs no shutdown boilerplate: the handle is an owned box, and its scope-exit drop is
what closes the mailbox. `inner` is dropped at the end of the labelled block, well before `main` returns, so
the service that prints `early` is finished while the second one is still being fed.
```maxon
type Beeper
	var tag as int

	static function create(tag int) returns Self
		return Self{tag: tag}
	end 'create'

	export function beep()
		print("beep {self.tag}\n")
	end 'beep'
end 'Beeper'

function main() returns ExitCode
	let outer = spawn Beeper.create(2)
	if true 'early'
		let inner = spawn Beeper.create(1)
		inner.beep()
	end 'early'
	outer.beep()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-cloned-handle-keeps-the-service-alive -->
<!-- targets: x64-windows -->
A handle is an ordinary box, so `.clone()` reaches it — and on a handle the clone is a second HANDLE to the
SAME service, not a second service. Dropping one leaves the mailbox open; the last one to go closes it.
```maxon
type Counter
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function report()
		print("n={self.n}\n")
	end 'report'
end 'Counter'

function main() returns ExitCode
	let a = spawn Counter.create()
	let b = a.clone()
	a.tick()
	b.tick()
	b.report()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=2
```

<!-- test: a-handle-moved-into-another-service -->
<!-- targets: x64-windows -->
A handle is a transferable message payload: `Worker` is handed the `Logger`'s handle, sends to it, and then
DROPS it — the un-consumed payload drop the loop owes for every message it runs. That drop is the `Logger`'s
last handle, so the logger shuts down without `main` ever naming it again.
```maxon
type Logger
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String)
		print("log: {s}\n")
	end 'say'
end 'Logger'

type Worker
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function run(sink Logger.handle)
		sink.say("from the worker")
	end 'run'
end 'Worker'

function main() returns ExitCode
	let logger = spawn Logger.create()
	let worker = spawn Worker.create()
	worker.run(logger)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
log: from the worker
```

<!-- test: handles-in-an-array -->
<!-- targets: x64-windows -->
Handles are ordinary boxes and live in containers — which means the array's element drop is the handle drop,
and two services shut down when the array does.
```maxon
type Counter
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'
end 'Counter'

typealias CounterHandleArray = Array with Counter.handle

function main() returns ExitCode
	var hs = CounterHandleArray.create()
	hs.push(spawn Counter.create())
	hs.push(spawn Counter.create())
	return hs.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-string-argument-moves-into-the-service -->
<!-- targets: x64-windows -->
A managed argument is MOVED: the sending frame hands over the reference it holds and the service becomes the
box's one owner. Nothing is increfed at the send and nothing is dropped by the sender, which is what keeps
the plain refcount correct across a green thread.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s} ({self.n})\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let a = "one-{1}"
	h.keep(a)
	let b = "two-{2}"
	h.keep(b)
	h.keep("a literal")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
kept one-1 (1)
kept two-2 (2)
kept a literal (3)
```

<!-- test: unioncases-tags-the-request-variants -->
<!-- targets: x64-windows -->
The synthesized request union is an ordinary union, so its `.unionCases` companion exists — and `__shutdown`
holds variant 0, so the first message an author declares is variant 1.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return Calc.request.unionCases.bump.rawValue as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: an-idle-service-that-is-never-sent-to-still-exits-zero -->
<!-- targets: x64-windows -->
Process exit must not hang on a service parked in `recv`, which is its steady state. Here the mailbox is
closed by the handle's own scope-exit drop before `main` returns, so the loop's `recv` answers 0 the first
time it is asked and the exit drain has one already-finished thread to reap.
```maxon
type Idler
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'
end 'Idler'

function main() returns ExitCode
	let h = spawn Idler.create()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: fire-and-forget-cycle-is-legal -->
<!-- targets: x64-windows -->
⭐ The case that pins "only blocking edges count" — without it a later tightening would silently ban correct
programs. `A` names `B`'s handle in a message and `B` names `A`'s, which is a cycle in the type graph and no
cycle at all in the blocking one, because neither send waits.
```maxon
type A
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle)
		b.pong()
	end 'ping'

	export function ack()
		self.n = self.n + 1
	end 'ack'
end 'A'

type B
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function pong()
		self.n = self.n + 1
	end 'pong'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	a.ping(b)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-export-reached-only-by-message-is-not-unused -->
<!-- targets: x64-windows -->
An export method reachable only as a MESSAGE must count as used, or the unused-export check would refuse
every service whose handle is the only caller. It is credited by the send op naming `Calc.bump` — the same
`maxonOpCalleeKind` road an ordinary call is credited by, which is why this needs no arm of its own.
```maxon
// --- file: calc.maxon
export type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	h.bump(1)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-sent-value-is-moved-and-reading-it-back-is-refused -->
A send consumes its argument; the source is poisoned and a read is E3102.

⚠ **THE VALUE IS AN INTERPOLATION AND NOT A BARE LITERAL, AND THAT IS THE SUBJECT RATHER THAN A DETAIL.** A
`"hello"` binding is a BORROWED `.rdata` record, and a borrowed byte record is PROMOTED to a fresh owned copy
at the send (`promoteToOwnedString`) — which leaves the source readable, correctly, because the service was
given a copy and never the author's record. Only an OWNED String is moved, so only an owned one can be read
back too soon.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello {1}"
	h.keep(buf)
	print("{buf}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:10: 'buf' was moved and cannot be read
```

<!-- test: error.a-co-owned-value-may-not-be-sent -->
A value a closure captured has a second owner on this green thread, which is exactly what the plain refcount
forbids across two. The send is refused rather than silently increfed, and `.clone()` is the fix.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello {1}"
	let peek = function() gives buf
	h.keep(buf)
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:18:8: argument 's' of the message 'Store.keep' cannot be sent: 'buf' is an owned value this frame has already taken a second reference to, and a message MOVES its arguments to another green thread — where a second owner on this one would make the plain refcount wrong. Send `buf.clone()` instead
```

<!-- test: error.a-borrowed-parameter-may-not-be-sent -->
Send-uniqueness does not survive a function boundary for a value with no owning copy: `p` arrived as a
BORROWED struct parameter, the caller still holds it, and a struct has no static clone for the send site to
take. (A borrowed `String` is not in this class — it has a cheap owning copy and is promoted.)
```maxon
type Payload
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Payload'

type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(p Payload)
		self.n = self.n + 1
	end 'keep'
end 'Store'

function forward(h Store.handle, p Payload)
	h.keep(p)
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = Payload.create()
	forward(h, p: p)
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:23:8: argument 'p' of the message 'Store.keep' cannot be sent: 'p' is a borrowed value this frame does not own — a message MOVES its arguments to another green thread, and the frame this borrow belongs to would still be holding it. Send a `.clone()` of it instead
```

<!-- test: error.a-promise-may-not-be-sent -->
A `Promise` is a green-thread handle its awaiter owns, and its value is a bare integer — so a message
declaring an `int` parameter would take one without a word if the send site did not ask. It asks.
```maxon
typealias Integer = int(i64.min to i64.max)

function work() returns Integer
	return 5
end 'work'

type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(n Integer)
		self.n = n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = async work()
	h.keep(p)
	let r = await p
	return 0
end 'main'
```
```maxoncstderr
error E3135: <fragment>:22:8: argument 'n' of the message 'Store.keep' cannot be sent: it is a Promise, which is a green-thread handle its awaiter owns and drives — a second green thread holding one would drop a thread this one is still waiting on
```

<!-- test: error.a-handle-of-another-service-is-refused -->
Two services' handles are two nominal types, so handing one where the other is expected is the ordinary
struct-identity mismatch and needs no rule of its own.
```maxon
type Calc
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Calc'

type Logger
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(peer Calc.handle)
		peer.bump()
	end 'say'
end 'Logger'

function main() returns ExitCode
	let c = spawn Calc.create()
	let l = spawn Logger.create()
	l.say(l)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:28:8: argument type mismatch for 'peer': expected 'Calc.handle', got 'Logger.handle'
```

<!-- test: error.a-send-may-not-be-tried -->
A `try` on a send has nothing to catch in SV1: a fire-and-forget message delivers no reply and therefore no
error. The refusal names the form that WILL carry one rather than reporting a syntax fault.
```maxon
type Calc
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.bump()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:2: Unsupported: `try h.bump()` — a fire-and-forget message carries no reply and so can deliver no error. The awaitable form `try await h.bump()`, which resolves through a reply cell and merges the handler's error union with `ServiceError`, is SV2
```

<!-- disabled-test: send-and-await-a-reply -->
<!-- SV2: the reply cell -->
A value-returning message is awaitable RPC.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'

	export function total() returns int
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.bump(3)
	let n = try await h.total() otherwise 0
	return n as ExitCode
end 'main'
```
```exitcode
3
```

<!-- disabled-test: a-message-throws-and-the-error-merges-with-serviceerror -->
<!-- SV2: the reply cell and the two-member synthesized error union -->
The merge is always two-way — transport plus one handler.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n int, by int) returns int throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return n / by
	end 'divide'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try await h.divide(10, by: 0) otherwise (e) 'oops'
		match e 'why'
			stopped then return 70 as ExitCode
			divideByZero then return 71 as ExitCode
		end 'why'
	end 'oops'
	return v as ExitCode
end 'main'
```
```exitcode
71
```

<!-- disabled-test: a-call-after-shutdown-answers-stopped -->
<!-- SV2: the reply cell resolves pending replies with ServiceError.stopped -->
A stopped service resolves its pending replies rather than hanging their awaiters.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function total() returns int
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.shutdown()
	let v = try await h.total() otherwise (e) 'gone'
		match e 'why'
			stopped then return 9 as ExitCode
		end 'why'
	end 'gone'
	return v as ExitCode
end 'main'
```
```exitcode
9
```

<!-- disabled-test: error.a-reply-may-not-alias-service-state -->
<!-- SV2: the reply-escape rule at parseCheckedValueReturn -->
A handler must not return a value reachable from `self`, or the caller ends up aliasing service state.
```maxon
type Store
	var buf as String

	static function create() returns Self
		return Self{buf: "x"}
	end 'create'

	export function read() returns String
		return self.buf
	end 'read'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	return 0
end 'main'
```
```maxoncstderr
error E3136: <fragment>:14:10: 'Store.read' returns a value reachable from 'self'
```

<!-- disabled-test: error.two-services-that-await-each-other-are-refused -->
<!-- SV2: the acyclic blocking graph (SemanticServiceCallCycle) -->
Mutual reentrancy is made unrepresentable rather than diagnosed at run time.
```maxon
type A
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle) returns int
		return try await b.pong() otherwise 0
	end 'ping'

	export function ack() returns int
		return 1
	end 'ack'
end 'A'

type B
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function pong(a A.handle) returns int
		return try await a.ack() otherwise 0
	end 'pong'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	return 0
end 'main'
```
```maxoncstderr
error E3137: <fragment>:30:10: service call cycle — these messages can deadlock waiting on each other
```

<!-- disabled-test: awaitany-returns-the-completed-index -->
<!-- SV3: awaitAny over an Array with Promise; gated on W217 -->
One waiting primitive covers service replies, file I/O and subprocess drains.
```maxon
type Slow
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns int
		return 5
	end 'value'
end 'Slow'

typealias IntPromiseArray = Array with Promise with int

function main() returns ExitCode
	let h = spawn Slow.create()
	var ps = IntPromiseArray.create()
	ps.push(h.value())
	let i = try awaitAny(ps) otherwise 9
	return i as ExitCode
end 'main'
```
```exitcode
0
```

<!-- disabled-test: error.a-generic-service-is-supported -->
<!-- SV-later: per-instantiation companions -->
The monomorphic-companion limitation is a v1 limit, not a rule of the design.
```maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function peek() returns T
		return self.item
	end 'peek'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(1)
	let v = try await h.peek() otherwise 0
	return v as ExitCode
end 'main'
```
```exitcode
1
```

<!-- disabled-test: error.a-value-sent-through-a-parameter-is-refused -->
<!-- E2015-deferred: the transitive half of param-consume (PLAN.md's interprocedural fixpoint) -->
⚠ **THE SHAPE THIS CASE PINS IS A `String`, AND IT IS THE ONE THE SEND SITE CURRENTLY ACCEPTS.** A borrowed
byte record is PROMOTED to a fresh owned copy at the send, which is sound — the service gets a record of its
own — so the refusal below is what a TRANSITIVE consume analysis would let the compiler replace the copy
with. The struct shape, which has no owning copy to take, IS refused today and is pinned live by
`error.a-borrowed-parameter-may-not-be-sent`. So this case is about the COST of the missing fixpoint (one
copy per forwarded String), not about a hole in the safety rule.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
	end 'keep'
end 'Store'

function forward(h Store.handle, buf String)
	h.keep(buf)
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	forward(h, buf: "hello")
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:14:8: 'buf' arrived as a parameter and cannot be proven unique at the send
```
