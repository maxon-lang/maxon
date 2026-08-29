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

⚠ **Almost every REFUSAL in this file is unmarked, and that is the same rule read from the other end.** A
verdict reached before a backend — a token shape, a declaration, a transferability rule, a move — is
target-neutral by construction, so a marker on one would hide a green lane rather than describe a red one.
`spawn-is-not-a-keyword` and `service-error-is-declared-and-nameable` are unmarked for the neighbouring
reason: they RUN, but they start no service and reach no scheduler at all.

⚠ **THE TWO E3104 CASES ARE THE EXCEPTION, AND THEY ARE MARKED WITH THE TARGET THEY REFUSE.** A refusal
whose whole subject is *"this target has no substrate for it"* is the one verdict in this file that is not
target-neutral, so it can only be pinned by compiling FOR that target — exactly as
`subprocess-builtins.rejected-on-wasm` is. They exist because the gate did not: MEASURED at review on all
three non-host lanes, a `spawn` reached the backend and PANICKED the compiler
(`StdToArm64Conversion.maxon:652`, `StdToWasm.maxon:1748`, `StdToX64Conversion.maxon:3382`) where the same
substrate reached by `sleep` has always answered E3104.

## Tests

<!-- test: companions-resolve-as-types -->
<!-- targets: x64-windows -->
A `spawn` makes the type a service, and both synthesized companions are then nameable TYPES — in a
signature written by a function that spawns nothing.

⭐ **IT CAN NOW FAIL, AND WAVE 2's NOTE THAT IT COULD NOT IS SPENT.** While the wave-3 E2015 stood at the
`spawn`, the file's parse ended before an unresolved `Calc.handle` was ever reported, so this case passed
with the whole-program spawn walk disabled and only the two CROSS-FILE cases could see the companions. That
throw is gone: the program parses to completion and runs.

⛔ **BUT NOT BY THE E3011 THIS PARAGRAPH USED TO CLAIM — SABOTAGE MEASURED AT THE SV1 REVIEW.** It said *"an
absent companion is `E3011 Unknown type 'Calc.handle'` at `serve`'s own signature"*. With the handle mint
withheld (`ServiceCompanions.synthesizeServiceCompanions`'s pass 1 skipped) this case IS red — but as
`panic at Parser.maxon:45750: serviceHandleLayout: nothing declares 'type Calc.handle'`, raised at the
`spawn` on line 14, which is reached long before `serve`'s signature and which kills the worker and fails
every case in this file with it. The E3011 road is real but belongs to
`companions-come-from-a-spawn-in-another-file`, whose declaring file writes no `spawn` and so has no panic
standing in front of the unresolved name — which is exactly the sabotage its own header records. ⇒ **This
case's gate is "the companions exist and the program runs"; the DIAGNOSTIC for an absent one is the
cross-file case's, and one claim may not be filed under both.**
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

function serve(_ Calc.handle) returns int
	return 1
end 'serve'

function describe(_ Calc.request) returns int
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

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(_ Calc.handle) returns int
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

⚠ `main` REACHES `start`, and `start` calls `serve`, because neither is decoration: a body no path from
`main` reaches is not parsed at all (`skipUnreachedFunctionBody`), and an `export` nothing outside its file
references is E3092. Both would have made this case pass without ever building the spawn it is about.
```maxon
// --- file: calc.maxon
export type Calc
	var count as int

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(_ Calc.handle) returns int
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
		return serve(h)
	end 'start'
end 'Runner'

function main() returns ExitCode
	return Runner.create().start() as ExitCode
end 'main'
```
```exitcode
1
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
<!-- targets: x64-windows -->
Only `export`/`public` INSTANCE methods are messages. `record` is file-private and `version` is a static,
so neither is subject to the transferability rule — a `Promise` parameter on either is perfectly legal on
a type that is spawned, which is what this case pins. Compare
`error.message-param-promise-not-transferable`, whose only difference is the `export`.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias IntPromise = Promise with Whole

type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function fromPromise(_ IntPromise) returns Self
		return Self{count: 0}
	end 'fromPromise'

	function record(_ IntPromise) returns int
		return 1
	end 'record'

	export function bump(by int)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	spawn Calc.create()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-factory-may-be-spelled-with-a-keyword -->
<!-- targets: x64-windows -->
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

function serve(_ Reader.handle) returns int
	return 1
end 'serve'

function main() returns ExitCode
	spawn Reader.from(3)
	return 0
end 'main'
```
```exitcode
0
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
error E3136: <fragment>:16:4: `record` is declared on `type Calc` but is not a message: only its `export` INSTANCE methods are on `Calc.handle`. That is the isolation boundary, and it is what makes a self-send unspellable — a private helper can only ever be reached by a DIRECT call, from inside a message body or from a `Calc` value. Export it to make it a message, or call it on a `Calc` value
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
what closes the mailbox. `inner` is dropped at the end of the labelled block and `outer` at `main`'s return,
and BOTH services' queued work runs — which is the property under test.

⚠⚠ **THE ORDER OF THE TWO LINES IS THE EXIT DRAIN'S AND NOT CAUSALITY, AND THIS CASE SAYS SO RATHER THAN
IMPLYING OTHERWISE.** At the default `MAXON_MAX_PROCS=1` nothing runs on a service's green thread until the
main thread stops running — an early handle drop closes the mailbox and publishes the parked receiver, but
nobody drives it — so both handlers run at the exit drain, in the order the drain reaps them, which is
spawn order. MEASURED stable across five runs at N=1 and three at N=4.
`two-instances-are-independent` is the case whose order IS forced, by a handle transfer.

⚠⚠ **THE `stdout` BLOCK PINS THAT ORDER EXACTLY, SO THIS CASE IS A TRIPWIRE ON THE DRAIN AND NOT ONLY ON
THE SHUTDOWN.** This paragraph used to end *"a scheduler change may legitimately move this line; what may
NOT move is that both lines appear and the process exits 0"* — which is a permission the GOLDEN does not
grant, and the two disagreeing is the shape this project keeps naming (SV1 review). The golden wins, and
that is the useful arrangement rather than a defect to weaken away: a drain-order change is exactly the
kind of scheduler edit whose blast radius someone should have to look at. ⇒ **When this line moves, the
answer is to re-baseline it DELIBERATELY, with the reason in the commit** — never to loosen the block, and
never to read the red as a shutdown bug. What would be a shutdown bug is a line MISSING, or a non-zero exit.
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
```stdout
beep 2
beep 1
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

<!-- test: a-handle-payload-beside-a-consumed-string-payload -->
<!-- targets: x64-windows -->
⭐⭐ **TWO SERVICES, TWO PAYLOAD KINDS, AND THE CROSSING BETWEEN TWO NAME TABLES.** `Logger.say` takes a
`String` and CALLS A METHOD on it; `Worker.run` takes a `Logger.handle`. Neither is remarkable alone — the
cases above pin each — and together they are the first program in this file whose signature-index and project
name tables have DIVERGED at the id a payload carries. `<T>.__loop` resolves that id, and it has to resolve it
against the table it came out of.

⛔ **IT WAS A CLEAN REFUSAL OF A CORRECT PROGRAM:** `E3005 argument type mismatch for 'sink': expected
'Logger.handle', got 'ExitCode'` — with no file and no line, because the check was walking the synthesized
loop. `ExitCode` is simply what the OTHER table holds at that number. See
`Runtime/ServiceLoop.maxon`'s header for the crossing and `ModuleInit.projectScopedNameId` for the carrier's
two-sided contract.

⚠ **WHY IT TOOK TWO SERVICES AND A METHOD CALL.** The two tables agree at every id until enough types are
declared to push them apart, so a smaller program cannot show it: `Logger.say` printing its parameter instead
of calling `byteLength()` on it interns one name fewer and the program compiles. That is the same property the
carrier's own header records the lexer's keyword map having — *"a read that is right only while two
independent insertion orders agree is a wrong answer waiting"*.
```maxon
type Logger
	var bytes as int

	static function create() returns Self
		return Self{bytes: 0}
	end 'create'

	export function say(s String)
		self.bytes = self.bytes + s.byteLength()
	end 'say'

	export function report()
		print("bytes={self.bytes}\n")
	end 'report'
end 'Logger'

type Worker
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function run(sink Logger.handle)
		sink.say("from the worker")
		sink.report()
	end 'run'
end 'Worker'

function main() returns ExitCode
	let logger = spawn Logger.create()
	let worker = spawn Worker.create()
	worker.run(logger.clone())
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bytes=15
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

<!-- test: a-throwing-message-is-sent-fire-and-forget -->
<!-- targets: x64-windows -->
⭐⭐ **A `throws` CLAUSE IS WHAT MAKES A MESSAGE REPLY-BEARING, AND SV1 SENDS IT ANYWAY.** The reply slot is
filled with 0, the handler runs, and its error has nowhere to go — which is the fire-and-forget half of the
design, not a gap in it. What must NOT happen is the thing that did: the second `keep` throws before it
reads `s`, so the `String` it was handed is still the loop's to drop, and the request box is still the loop's
to release.

⛔ **MEASURED RED AT SV1 wave 3: exit 101 on exactly this shape.** A `tryCall` opens an error-edge diamond,
so the payload drop and the shell decref land in that diamond's MERGE — and `buildServiceArm` terminated the
arm's own block instead, overwriting the diamond's branch and skipping both. Correct answers printed, every
throwing message's box and payload stranded.
```maxon
enum StoreError
	full
end 'StoreError'

type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String) throws StoreError
		if self.n > 0 'alreadyHoldsOne'
			throw StoreError.full
		end 'alreadyHoldsOne'
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	h.keep("a{1}")
	h.keep("b{2}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
kept a1
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
	spawn Calc.create()
	return Calc.request.unionCases.bump.rawValue as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: an-idle-service-that-is-never-sent-to-still-exits-zero -->
<!-- targets: x64-windows -->
Process exit must not hang on a service parked in `recv`, which is its steady state. Here the mailbox is
closed by the handle's own drop — at the end of the `spawn` STATEMENT, since nothing binds it — well before
`main` returns, so the loop's `recv` answers 0 the first time it is asked and the exit drain has one
already-finished thread to reap.

⚠ The handle is deliberately NOT bound: an unread `let` is `E3012 unused variable`, which is the language's
rule for every binding and not a service question (`reportUnusedBindings` follows both references). A case
that wants a handle BOUND has to read it, which `dropping-the-last-handle-shuts-the-service-down` does.
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
	spawn Idler.create()
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

<!-- test: a-fire-and-forget-send-is-not-a-blocking-edge -->
<!-- targets: x64-windows -->
⭐⭐ **THE CASE THAT CAN ACTUALLY SEE "only blocking edges count", AND `fire-and-forget-cycle-is-legal` ABOVE
CANNOT.** That one has no `await` anywhere, so `checkServiceCallCycles` short-circuits on an empty seed set
before it ever consults the rule — it pins the SV1 property (a type-graph ring compiles and runs) and is
silent about the SV2 one. MEASURED: with a `serviceSend` treated as a call-graph edge, it stayed GREEN.

This program is the shape that fails under that sabotage. `B.work` really does await a reply from `A`, so the
blocking graph holds `B → A` — and `A.kick` merely POSTS to `B` and returns, which is what keeps the graph
acyclic. Count the send as an edge and `A.kick` inherits `B.work`'s blocking, giving `A → A`: a self-edge, and
a refusal of a program that cannot deadlock. It cannot deadlock because `A.kick` never waits — it answers
immediately, so `A` is free to serve `B`'s `ack` when it arrives.
```maxon
type A
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function kick(peer B.handle, mine A.handle) returns int
		peer.work(mine.clone())
		return 1
	end 'kick'

	export function ack() returns int
		return 7
	end 'ack'
end 'A'

type B
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function work(back A.handle)
		let v = try await back.ack() otherwise 0
		print("acked {v}\n")
	end 'work'

	export function drain() returns int
		return 5
	end 'drain'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	let v = try await a.kick(b.clone(), mine: a.clone()) otherwise 0
	let done = try await b.drain() otherwise 0
	return (v + done) as ExitCode
end 'main'
```
```exitcode
6
```
```stdout
acked 7
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

	export static function create() returns Self
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
error E3102: <fragment>:18:10: use of moved value 'buf': its ownership moved to another binding at an earlier bind or assignment
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
		print("kept {s}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello {1}"
	let peek = function() gives buf
	h.keep(buf)
	return peek().byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3138: <fragment>:19:9: argument `s` of the message `Store.keep` cannot be proven to have exactly one owner (`buf`): this frame has either taken a SECOND reference to it — a container push, a closure capture, a consuming call — or received it across a frame boundary whose far side may still hold one (a parameter, or a call whose callee the compiler cannot prove returns a fresh record). A send MOVES: the service becomes the value's one owner and this frame gives up the reference it held. That is what keeps reference counting PLAIN rather than atomic — the language guarantees one green thread per box — so a value with a second owner would put one box into two green threads' hands. Send a `.clone()`, or build the value at the send
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
error E3138: <fragment>:23:9: argument `p` of the message `Store.keep` is BORROWED — read out of a field, an element or a parameter — so this frame does not own it, and its type has no owning copy a send could take instead. A send MOVES: the service becomes the value's one owner and this frame gives up the reference it held. That is what keeps reference counting PLAIN rather than atomic — the language guarantees one green thread per box — so a value with a second owner would put one box into two green threads' hands. Send a `.clone()`, or build the value at the send
```

<!-- test: error.a-service-is-rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
⭐ A service's whole substrate is x64-windows only, so on any other target BOTH ops are refused at their own
source span with `E3104`, naming the runtime entry that has no lowering there — never a panic from inside a
backend, which is what this family did before the gate existed.

⚠ **THE TWO OPS NEED TWO ARMS, AND ONE WOULD HAVE PASSED A HALF-BUILT GATE.** A `spawn` and a send mint
different entries, and a program can contain either without the other — `an-idle-service-that-is-never-sent-to-still-exits-zero`
is exactly a spawn with no send.
```maxon
type Plot
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(x int)
		self.n = self.n + x
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(1)
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:15:10: this construct is x64-windows only at this rung: 'spawn' lowers to the runtime entry '__svc_spawn', which has no wasm32-wasi implementation
error E3104: <fragment>:16:2: this construct is x64-windows only at this rung: a message send lowers to the runtime entry '__mbox_send', which has no wasm32-wasi implementation
```

<!-- test: error.a-service-is-rejected-on-arm64 -->
<!-- targets: arm64-macos -->
The same gate on the other local lane, which is the one that makes this a TARGET rule rather than a wasm
one. ⚠ `x64-linux` was measured to answer identically (`lowerTlsAlloc: TlsAlloc is x64-windows only` before
the gate, E3104 after) and is not pinned here only because two lanes already part the target from the
construct.
```maxon
type Plot
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(x int)
		self.n = self.n + x
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(1)
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:15:10: this construct is x64-windows only at this rung: 'spawn' lowers to the runtime entry '__svc_spawn', which has no arm64-macos implementation
error E3104: <fragment>:16:2: this construct is x64-windows only at this rung: a message send lowers to the runtime entry '__mbox_send', which has no arm64-macos implementation
```

<!-- test: a-scalar-only-record-crosses-whole -->
<!-- targets: x64-windows -->
⭐ The POSITIVE CONTROL for the three cases below, and the shape the transfer rule admits: a record whose
every slot is a machine word holds no reference to anything, so moving it moves the whole of it. Built at the
send, so this frame gives up the only reference there was.
```maxon
type Point
	export var x as int
	export var y as int

	export static function create(x int, y int) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plot
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(p Point)
		self.n = self.n + 1
		print("at {p.x},{p.y} ({self.n})\n")
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(Point.create(1, y: 2))
	h.at(Point.create(3, y: 4))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
at 1,2 (1)
at 3,4 (2)
```

<!-- test: error.a-record-with-a-managed-field-may-not-cross -->
⭐⭐ **SOLENESS IS NOT TRANSITIVE, AND THIS IS THE CASE THAT SAYS SO.** `main` owns `cell` and owns the
`Calc` the factory handed back — each of them ALONE — and the two facts together still do not say that
`main` and the service will not both own the `Cell`. The factory's `Self{c: alias}` increfs rather than
moves, so the record has two owners across two green threads, whose plain refcount steps then race.

⚠ **THE RETAIN IS THE FACTORY'S, WHICH IS WHY THE RULE IS ABOUT THE TYPE.** `let alias = src` puts the store
out of reach of the swept consume scan, so `main` passes a BORROW and emits no retain of its own: nothing
observable at the SEND distinguishes this from the safe program. Before the rule existed this compiled clean
and exited 0.
```maxon
type Cell
	export var n as int

	export static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

type Calc
	var c as Cell

	static function create(src Cell) returns Self
		let alias = src
		return Self{c: alias}
	end 'create'

	export function bump()
		self.c.n = self.c.n + 100
	end 'bump'
end 'Calc'

function main() returns ExitCode
	var cell = Cell.create()
	let h = spawn Calc.create(cell)
	h.bump()
	return cell.n as ExitCode
end 'main'
```
```maxoncstderr
error E3138: <fragment>:25:21: the state `spawn Calc.create(…)` would start the service with is a `type Calc` with a managed field — a reference the sending frame may still hold. This frame owns that record alone — but a record it POINTS AT may have a second owner, because every co-owning store takes a reference where a move would give one up, and soleness is not transitive. A send MOVES the record WHOLE, so the second owner would be left on this green thread with a plain refcount racing the service's. What may cross today is a scalar, a `String`, a service HANDLE, or a record whose every slot is a SCALAR — a `String` FIELD does not make a record crossable even though a `String` ARGUMENT crosses, because the store that put it there took a reference where a move would have given one up. Proving the rest needs a record's whole graph tracked through the co-owning stores, which is a whole-program fact this compiler does not yet compute. Send the scalars the record is built from, or keep the record on this side and send what the service needs of it
```

<!-- test: error.a-record-with-a-string-field-may-not-cross-either -->
⭐⭐ **A `String` ARGUMENT CROSSES AND A `String` FIELD DOES NOT, AND THE DIAGNOSTIC USED TO SAY OTHERWISE.**
A `String` VALUE is closed — its record's slots hold a byte buffer and a length, never a reference to a second
refcounted record — which is exactly why a `String` message argument is allowed. A record that HOLDS one is a
different question: `Self{held: s}` stores a reference where a move would have given one up, so the `String`
has two owners the instant the record has one. That is the same non-transitivity one level down, and it is
what makes a scalar-only record the shape that crosses.

⚠ **THIS CASE EXISTS BECAUSE THE MESSAGE OVER-PROMISED.** It read *"a record whose every slot is one of
those"* — of *"a scalar, a `String`, a service HANDLE"* — which names this program as legal. The compiler
refuses it, and always did; the sentence was the part that was wrong. A refusal an author cannot act on is
worse than one they can, and this is the shape they would have written next.
```maxon
type Holder
	var held as String
	var bytes as int

	static function create() returns Self
		return Self{held: "", bytes: 0}
	end 'create'

	export function keep(s String)
		self.bytes = self.bytes + s.byteLength()
	end 'keep'
end 'Holder'

function main() returns ExitCode
	let h = spawn Holder.create()
	h.keep("payload")
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:16:23: the state `spawn Holder.create(…)` would start the service with is a `type Holder` with a managed field — a reference the sending frame may still hold. This frame owns that record alone — but a record it POINTS AT may have a second owner, because every co-owning store takes a reference where a move would give one up, and soleness is not transitive. A send MOVES the record WHOLE, so the second owner would be left on this green thread with a plain refcount racing the service's. What may cross today is a scalar, a `String`, a service HANDLE, or a record whose every slot is a SCALAR — a `String` FIELD does not make a record crossable even though a `String` ARGUMENT crosses, because the store that put it there took a reference where a move would have given one up. Proving the rest needs a record's whole graph tracked through the co-owning stores, which is a whole-program fact this compiler does not yet compute. Send the scalars the record is built from, or keep the record on this side and send what the service needs of it
```

<!-- test: error.a-container-may-not-cross -->
A container is unclosed whatever its element type is, and `Cell` is the proof that the ELEMENT's own
scalar-ness is not the question: `push` INCREFS what it is handed, so `cell` is owned by `main` and by `cs`
at once. Sending `cs` would hand the second owner's record to another green thread.
```maxon
type Cell
	export var n as int

	export static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Svc
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(cs CellArray)
		print("svc {cs.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cell = Cell.create()
	var cs = CellArray.create()
	cs.push(cell)
	let h = spawn Svc.create()
	h.take(cs)
	return cell.n as ExitCode
end 'main'
```
```maxoncstderr
error E3138: <fragment>:29:9: argument `cs` of the message `Svc.take` is a container, whose elements a push increfs rather than moves — so this frame may still own what is in it (`cs`). This frame owns that record alone — but a record it POINTS AT may have a second owner, because every co-owning store takes a reference where a move would give one up, and soleness is not transitive. A send MOVES the record WHOLE, so the second owner would be left on this green thread with a plain refcount racing the service's. What may cross today is a scalar, a `String`, a service HANDLE, or a record whose every slot is a SCALAR — a `String` FIELD does not make a record crossable even though a `String` ARGUMENT crosses, because the store that put it there took a reference where a move would have given one up. Proving the rest needs a record's whole graph tracked through the co-owning stores, which is a whole-program fact this compiler does not yet compute. Send the scalars the record is built from, or keep the record on this side and send what the service needs of it
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

	export function keep(value Integer)
		self.n = value
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = async work()
	h.keep(p)
	let r = await p
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3135: <fragment>:23:9: argument `value` of the message `Store.keep` is a Promise, which is a green-thread handle its awaiter owns, and a message MOVES its arguments to another green thread — which would leave a second green thread holding a thread this one is still waiting on. Send the scalar it is derived from, `await` it and send the RESULT, or drop the parameter from the message
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
error E3005: <fragment>:29:8: argument type mismatch for 'peer': expected 'Calc.handle', got 'Logger.handle'
```

<!-- test: error.a-message-that-returns-nothing-and-throws-nothing-has-no-value -->
⭐ **A FIRE-AND-FORGET SEND IS STILL A STATEMENT, AND THE REFUSAL NOW POINTS AT THE DECLARATION.** A message
that returns nothing and throws nothing carries no `__reply` slot at all, so the send mints no cell and there
is no promise to bind — and the cure is on `bump` rather than at the call. Every OTHER message is awaitable,
which is why this sentence names what would give this one a reply rather than naming a rung.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump()
		self.count = self.count + 1
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let n = h.bump()
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:16:12: Unsupported: the value of `Calc.bump` sent as a MESSAGE — it returns nothing and throws nothing, so it carries no reply slot and a send of it delivers no value. Give it a `returns` clause or a `throws` clause and `try await <handle>.bump(…)` resolves through a reply cell; otherwise send it as a statement
```

<!-- test: error.a-send-may-not-be-tried -->
A `try` on a SEND has nothing to catch: the send enqueues and returns, and it is the REPLY that carries an
error. The refusal names the awaitable form rather than reporting a syntax fault — and for `bump`, which
returns nothing and throws nothing, it names the other cure too: there is no reply at all, so drop the `try`.
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
error E2015: <fragment>:16:2: Unsupported: `try` on the message `Calc.bump` — a SEND is not the reply. It enqueues and returns, so it can fail at nothing; what carries an error is the reply, and awaiting it is what makes that error this frame's. Write `try await <handle>.<message>(…)`, or drop the `try` on a message that returns nothing and throws nothing
```

<!-- test: send-and-await-a-reply -->
<!-- targets: x64-windows -->
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

<!-- test: a-message-throws-and-the-error-merges-with-serviceerror -->
<!-- targets: x64-windows -->
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
		return try (n / by) otherwise 0
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

<!-- test: a-call-after-shutdown-answers-stopped -->
<!-- targets: x64-windows -->
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

<!-- test: error.a-reply-may-not-alias-service-state -->
A handler must not return a value reachable from `self`, or the caller ends up aliasing service state — two
green threads naming one box, with a plain refcount between them. `return self` is the shortest way to say it
and the only one a service state can currently spell: a state record may hold nothing but scalars
(`error.a-record-with-a-managed-field-may-not-cross`), so a FIELD read has nothing managed to hand back yet.

⚠ The blame names the RETURN, and the note names the **`spawn`** that made the type a service — whether a
type is a service is a whole-program property, and the `spawn` deciding it may be in another file entirely
from the method the rule fires on.
```maxon
type Store
	var n as int

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function itself() returns Store
		return self
	end 'itself'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	return 0
end 'main'
```
```maxoncstderr
error E3137: <fragment>:10:3: `Store.itself` returns a value reachable from `self`, and this `spawn` makes `Store` a service — so the caller would hold a second reference to the service's own state, on another green thread, with a plain reference count between them. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:15:10: the `spawn` that makes `Store` a service
```

<!-- test: error.two-services-that-await-each-other-are-refused -->
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

	export function pong() returns int
		return try await spawnA().ack() otherwise 0
	end 'pong'
end 'B'

function spawnA() returns A.handle
	return spawn A.create()
end 'spawnA'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	return 0
end 'main'
```
```maxoncstderr
error E3139: <fragment>:10:14: service call cycle — these messages can deadlock waiting on each other:
    `A.ping` (<fragment>:10:14) awaits a reply from `B`
    `B.pong` (<fragment>:26:14) awaits a reply from `A`
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
```

<!-- test: a-reply-error-type-with-one-member-is-nameable -->
<!-- targets: x64-windows -->
⭐ **A MESSAGE THAT THROWS NOTHING HAS A ONE-MEMBER REPLY ERROR TYPE, AND ONE MEMBER IS A NAME.** The reply of
`total()` can fail only in transport, so its error type is `ServiceError` itself — an ordinary declared enum a
`throws` clause can spell, which is what lets a bare `try` PROPAGATE it out of an intermediate function. Only a
message that throws needs the two-member union, and that one has no spelling (see the case below).
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns int
		return self.count
	end 'total'
end 'Calc'

function fetch(h Calc.handle) returns int throws ServiceError
	return try await h.total()
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try fetch(h) otherwise 1
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: error.bare-try-propagation-of-a-two-member-reply-is-refused -->
A message that THROWS has a two-member reply error type, and no `throws` clause can name it — so a bare `try`
that would re-publish the flag to this function's caller is refused with the same E3059 an ordinary type
mismatch earns. The cure is to CATCH the reply here, which is what `otherwise (e)` is for.
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
		return try (n / by) otherwise 0
	end 'divide'
end 'Calc'

function fetch(h Calc.handle) returns int throws MathError
	return try await h.divide(10, by: 2)
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3059: <fragment>:22:9: try propagates 'Calc.divide.errors' but enclosing function throws 'MathError' — add 'otherwise' to convert
```

<!-- test: shutdown-resolves-pending-replies -->
<!-- targets: x64-windows -->
⭐ **THE LIVENESS OBLIGATION, ON BOTH OF ITS ROADS.** A message that dies unprocessed must not hang its awaiter.
The FIRST send is queued behind the poison pill and is abandoned by the loop's own drain; the SECOND arrives
after the mailbox is already closed and is abandoned by the send. Both answer `ServiceError.stopped`, and the
case passes by TERMINATING at all.
```maxon
type Slow
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns int
		return self.n
	end 'value'
end 'Slow'

function main() returns ExitCode
	var stops = 0
	let h = spawn Slow.create()
	h.shutdown()
	try await h.value() otherwise (e) 'first'
		match e 'w1'
			stopped then stops = stops + 1
		end 'w1'
	end 'first'
	try await h.value() otherwise (e) 'second'
		match e 'w2'
			stopped then stops = stops + 1
		end 'w2'
	end 'second'
	return stops as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-string-moves-in-and-a-fresh-string-comes-back -->
<!-- targets: x64-windows -->
The reply carries a MANAGED value across green threads. It is sound for the reason the send's move is: the
handler's result is freshly minted in the handler's own frame, so the service gives up its only reference and
the awaiter takes it — one owner throughout, which is what a plain refcount requires.
```maxon
type Echo
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	let out = try await h.say("hi") otherwise (e) 'gone'
		return 9 as ExitCode
	end 'gone'
	print("{out}\n")
	return 0
end 'main'
```
```stdout
[hi]
```

<!-- test: reply-discarded-is-dropped-clean -->
<!-- targets: x64-windows -->
⭐ A reply-bearing message sent in STATEMENT position mints a cell nobody binds, so the promise is dropped at
statement end while the cell is still PENDING. The dropper may not free it — the replier is about to write into
it — so it adds the consumer ticket only, and the reply's own completion supplies the runner's half. The managed
result nobody took is released by whichever arrives second.
```maxon
type Echo
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	h.say("a")
	h.say("b")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: pending-reply-dropped-then-answered -->
<!-- targets: x64-windows -->
The dropped cell of the first send is still pending when the SECOND send's await drives the service, so the
replier writes into a cell its awaiter has already renounced. That is the ordering the teardown rendezvous
exists for, and freeing the cell at the drop is a clobbered green thread rather than a leak.
```maxon
type Echo
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'

	export function count() returns int
		return 5
	end 'count'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	h.say("a")
	let n = try await h.count() otherwise 0
	return n as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: completed-reply-dropped-is-reclaimed -->
<!-- targets: x64-windows -->
The other order: `p`'s first cell COMPLETES while the middle await drives the service (its message is ahead in
FIFO order), and only THEN is it dropped — by the RE-ARM on the next line. The drop finds the runner ticket
already there and reclaims; an arm that took a completed cell as merely queued would strand the struct
invisibly, because the green-thread count the exit gate reads has already been debited.
```maxon
type Echo
	var n as int

	static function create() returns Self
		return Self{n: 4}
	end 'create'

	export function count() returns int
		return self.n
	end 'count'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	var p = h.count()
	let n = try await h.count() otherwise 0
	p = h.count()
	let m = try await p otherwise 0
	return (n + m) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: rpc-from-inside-a-service -->
<!-- targets: x64-windows -->
⭐⭐ **THE FIRST CROSS-GREEN-THREAD WAKE IN THE LANGUAGE.** `Outer`'s message awaits a reply from `Inner`, so a
green thread — not the main one — is the awaiter, and the drive that completes its cell runs `Inner` from
`Outer`'s own stack. The graph `Outer → Inner` is acyclic, which is what makes this legal.
⚠ The peer's handle crosses as a message ARGUMENT and not as `Outer`'s state: a state record may hold nothing
managed at all (`error.a-record-with-a-managed-field-may-not-cross`), which is a live limit of SV1's transfer
rule rather than anything this rung changes.
```maxon
type Inner
	var n as int

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	export function value() returns int
		return self.n
	end 'value'
end 'Inner'

type Outer
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function doubled(inner Inner.handle) returns int
		let v = try await inner.value() otherwise 0
		return v * 2
	end 'doubled'
end 'Outer'

function main() returns ExitCode
	let i = spawn Inner.create()
	let o = spawn Outer.create()
	let v = try await o.doubled(i) otherwise 0
	return v as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: error.double-await-of-a-reply -->
Per-await linearity composes for free, because it keys on the promise's own identity rather than on what
produced it — a reply cell is a `Promise` like any other.
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
	let p = h.total()
	let a = try await p otherwise 0
	let b = try await p otherwise 0
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:18:14: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: error.plain-await-of-a-reply -->
A reply ALWAYS throws `ServiceError`, whatever the message itself declares — so a plain `await` of one is
E3057 by construction and there is no reply in the language that can be awaited without `try`.
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
	let n = await h.total()
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3057: <fragment>:16:10: throwing function requires try: 'await' on a promise from a function that throws 'ServiceError' drops the error and leaks its payload — use 'try await'
```

<!-- test: error.a-message-that-throws-a-payload-carrying-union-is-refused -->
⚠ **THE ONE ERROR SHAPE A REPLY CANNOT CARRY, REFUSED RATHER THAN MISCOMPILED.** The reply's error word is a
single word carrying the FUSED ordinal of a two-member dispatch line, which is what lets `match e` tell
`ServiceError` apart from the handler's own error; a payload-carrying union's flag is a heap POINTER instead,
so the two facts would need two words and the box would cross green threads with no soleness rule to say it
may.

⚠ **IT FIRES AT THE `spawn` AND NOT AT A SEND, AND THAT IS FORCED RATHER THAN CONVENTIONAL.** `<T>.__loop` is
synthesized from the DECLARATION and completes a reply for every reply-bearing message of the type, so a
message the cell cannot carry would put a wrong-width store into a body the service really runs — whether or
not the program ever sends that message. There is no `spawn`-free program here to check it against.
```maxon
union Trouble implements Error
	detail(text String)
end 'Trouble'

type Store
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function wipe() returns int throws Trouble
		throw Trouble.detail("no")
	end 'wipe'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	return 0
end 'main'
```
```maxoncstderr
error E3140: <fragment>:19:10: the message `Store.wipe` declares a reply that throws `Trouble`, a PAYLOAD-CARRYING union whose error flag is a heap box pointer — a reply's one error word already carries the fused ordinal that tells `ServiceError` apart from the message's own error, and a pointer is not an ordinal, and this `spawn` makes `Store` a service — whose reply-bearing messages resolve through a CELL, a green thread that never runs, carrying one value word and one error word. Return an integer, a `String`, a struct or a service handle, and throw a payload-free `enum`; or drop the `returns` and `throws` clauses, which makes the message fire-and-forget and gives it no reply to carry
```

<!-- test: cycle-through-a-free-function-is-refused -->
An edge is transitive through ordinary functions: `A.ping` calls `relay`, which awaits a `B.handle`, so the
edge `A → B` exists even though `A`'s own body names no `B` message.

⚠ The hop is anchored at `relay`'s `await` rather than at `A.ping` — that IS where the thread stops, and a
message that blocks only through a helper has no await of its own to point at.
```maxon
function relay(b B.handle, peer A.handle) returns int
	return try await b.pong(peer.clone()) otherwise 0
end 'relay'

type A
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle, peer A.handle) returns int
		return relay(b, peer: peer)
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

	export function pong(peer A.handle) returns int
		return try await peer.ack() otherwise 0
	end 'pong'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	return 0
end 'main'
```
```maxoncstderr
error E3139: <fragment>:3:13: service call cycle — these messages can deadlock waiting on each other:
    `A.ping` (<fragment>:3:13) awaits a reply from `B`
    `B.pong` (<fragment>:30:14) awaits a reply from `A`
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
```

<!-- test: cycle-same-type-self-edge-is-refused -->
⚠ **A SELF-EDGE IS A CYCLE, AND THIS IS THE ONE USERS WILL HIT.** Two instances of the same service could not
actually deadlock, but edges are by TYPE — which is what makes them statically knowable at all — so the
analysis cannot tell the instances apart and must be conservative. The message says the workaround.
```maxon
type Worker
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask(peer Worker.handle) returns int
		return try await peer.answer() otherwise 0
	end 'ask'

	export function answer() returns int
		return 1
	end 'answer'
end 'Worker'

function main() returns ExitCode
	let w = spawn Worker.create()
	w.ask(w.clone())
	return 0
end 'main'
```
```maxoncstderr
error E3139: <fragment>:10:14: service call cycle — these messages can deadlock waiting on each other:
    `Worker.ask` (<fragment>:10:14) awaits a reply from `Worker`
    Two distinct instances of `Worker` would not deadlock — but edges are by TYPE, which is what makes them statically knowable at all, so the analysis cannot tell the instances apart and must be conservative. Make the peer call fire-and-forget (send it as a statement and have the peer reply with a separate message), or split the role into two types
```

<!-- test: deep-acyclic-chain-runs -->
<!-- targets: x64-windows -->
Three services in a chain, each awaiting the next. An acyclic graph has a topological order, so the service
lowest in it awaits nobody and always makes progress — which is the induction the whole rule rests on, run.

⚠ `A.value` forwards `last` with a `.clone()`: a message PARAMETER arrives borrowed, and a send MOVES — so the
handle it forwards has to be one this frame owns. The original is dropped by the loop as an un-consumed
payload, which is what shuts `C` down once the chain has answered.
```maxon
type C
	var n as int

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function value() returns int
		return self.n
	end 'value'
end 'C'

type B
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next C.handle) returns int
		let v = try await next.value() otherwise 0
		return v + 10
	end 'value'
end 'B'

type A
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next B.handle, last C.handle) returns int
		let v = try await next.value(last.clone()) otherwise 0
		return v + 20
	end 'value'
end 'A'

function main() returns ExitCode
	let c = spawn C.create()
	let b = spawn B.create()
	let a = spawn A.create()
	let v = try await a.value(b, last: c) otherwise 0
	return v as ExitCode
end 'main'
```
```exitcode
31
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
		print("kept {s}
")
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
