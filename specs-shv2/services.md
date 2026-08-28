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

A service can be gone: `shutdown()` enqueues a poison pill behind everything queued, and dropping the last
handle does the same. So `stdlib/Builtins.maxon` declares

```text
public enum ServiceError implements Error
	stopped
end 'ServiceError'
```

which a reply's error type will be merged with. It carries no `__` prefix precisely so a user can write
`match e … stopped …`.

## Targets

⚠ **No case in this file carries a `<!-- targets: -->` marker, and none should.** Fourteen of them are
decided BEFORE lowering — a token shape, a declaration, a transferability rule — and a verdict reached
before a backend is target-neutral by construction. The two that RUN (`spawn-is-not-a-keyword`,
`service-error-is-declared-and-nameable`) reach no green-thread runtime at all: one is an ordinary program
about an identifier and the other throws an ordinary enum. That is the rule
`specs-shv2/async-scheduler.md`'s own Targets section states — **a marker on a case that passes everywhere
hides a green lane rather than describing a red one**, which is what eleven removed markers were doing.

⚠ **The gate arrives with the cases that need it.** Every `disabled-test` below that actually STARTS a
service is gated on the mailbox and the dispatch loop, and each will carry the green-thread
`<!-- targets: x64-windows -->` when it is enabled — because from that moment the case reaches a context
switch written in hand-assembled x64.

## Tests

<!-- test: companions-resolve-as-types -->
A `spawn` makes the type a service, and both synthesized companions are then nameable TYPES — in a
signature written by a function that spawns nothing.

⚠ **THIS CASE CANNOT FAIL ON ITS OWN CLAIM, AND THAT IS MEASURED RATHER THAN SUSPECTED.** With the
whole-program spawn walk disabled — companions minted for nothing — it still PASSES: the wave-3 E2015 is a
throw, so the file's parse ends before an unresolved `Calc.handle` is ever reported, and the `E3011 Unknown
type` that would expose the absence never runs. It is kept because it pins the SHAPE (naming both
companions in ordinary signatures adds no diagnostic of its own), and because it goes red the moment wave 3
deletes the E2015 with anything wrong. **The gate on the companions actually existing is
`companions-come-from-a-spawn-in-another-file`**, where the declaring file parses to completion.
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
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:31:10: Unsupported: services are not lowered yet — SV1 wave 3
```

<!-- test: companions-come-from-a-spawn-in-another-file -->
⭐ Whether a type is a service is a WHOLE-PROGRAM property. The file that declares `Calc` and names
`Calc.handle` writes no `spawn` at all; the file that spawns declares nothing. The companions exist
because the two are compiled together, and the only diagnostic is the wave-3 refusal — the `Unknown type`
a per-file decision would report is what this case is against.

⭐ **IT IS THE ONE CASE IN THIS FILE THAT CAN FAIL ON THE COMPANIONS, AND IT WAS SEEN RED.** SABOTAGE
MEASURED: with the whole-program spawn probe removed from the pre-fold token walk, this case reports the
wave-3 E2015 **and** `error E3011: Unknown type 'Calc.handle'`, while every other case in this file stays
green. `calc.maxon` parses to completion — its `spawn` is in the other file — so the unresolved companion
is actually reached, which is precisely what a single-file case cannot arrange.
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
```maxoncstderr
error E2015: <fragment>:21:10: Unsupported: services are not lowered yet — SV1 wave 3
```

<!-- test: a-spawn-inside-a-method-body-is-found -->
⭐ The walk that decides which types are services reads EVERY token of every file, and this is the case
that says why it cannot ride the declaration sweep: that sweep consumes a `type` declaration whole and
resumes past its `end`, so a `spawn` in a METHOD BODY would be invisible to an arm written inside it. The
only `spawn` in this program is inside `Runner.start`.

⚠ **IT IS SPLIT ACROSS TWO FILES ON PURPOSE, for `companions-come-from-a-spawn-in-another-file`'s measured
reason.** Written as one file it would pass with the walk disabled — the wave-3 E2015 is a throw and ends
the parse before an unresolved companion is reported — so the claim would be untestable. Here `calc.maxon`
parses to completion, and a walk that skipped method bodies would leave its `Calc.handle` unresolved.
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
```maxoncstderr
error E2015: <fragment>:28:11: Unsupported: services are not lowered yet — SV1 wave 3
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

<!-- disabled-test: send-and-await-a-reply -->
<!-- SV1 wave 3: mailbox + dispatch loop; SV2: the reply cell -->
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

<!-- disabled-test: fire-and-forget-send-returns-nothing -->
<!-- SV1 wave 3: mailbox + dispatch loop -->
A message that returns nothing is a non-blocking send.
```maxon
type Counter
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function value() returns int
		return self.n
	end 'value'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.tick()
	h.tick()
	let n = try await h.value() otherwise 0
	return n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- disabled-test: messages-are-serialized-in-fifo-order -->
<!-- SV1 wave 3: mailbox FIFO -->
Handlers run one at a time, in send order.
```maxon
type Log
	var acc as int

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d int)
		self.acc = self.acc * 10 + d
	end 'push'

	export function read() returns int
		return self.acc
	end 'read'
end 'Log'

function main() returns ExitCode
	let h = spawn Log.create()
	h.push(1)
	h.push(2)
	h.push(3)
	let n = try await h.read() otherwise 0
	return n as ExitCode
end 'main'
```
```exitcode
123
```

<!-- disabled-test: two-instances-are-independent -->
<!-- SV1 wave 3: mailbox + dispatch loop -->
Two spawns of one type are two services with two states.
```maxon
type Counter
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function value() returns int
		return self.n
	end 'value'
end 'Counter'

function main() returns ExitCode
	let a = spawn Counter.create()
	let b = spawn Counter.create()
	a.tick()
	a.tick()
	b.tick()
	let x = try await a.value() otherwise 0
	let y = try await b.value() otherwise 0
	return (x * 10 + y) as ExitCode
end 'main'
```
```exitcode
21
```

<!-- disabled-test: the-same-type-is-used-directly-and-spawned -->
<!-- SV1 wave 3: mailbox + dispatch loop -->
⭐ The location-transparency property, and the test a dedicated `service` declaration would have made
impossible: one type, one method, reached both ways in one program.
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
	var direct = Calc.create()
	direct.bump(4)

	let h = spawn Calc.create()
	h.bump(3)
	let remote = try await h.total() otherwise 0
	return (direct.total() + remote) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- disabled-test: error.a-private-method-is-absent-from-the-handle -->
<!-- SV1 wave 3: the handle's method surface -->
A private helper is not on the handle, which is what makes a self-send unspellable.
```maxon
type Calc
	var count as int

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function record(v int) returns int
		return v
	end 'record'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let n = try await h.record(1) otherwise 0
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E4006: <fragment>:15:22: Type 'Calc.handle' has no member named 'record'
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

<!-- disabled-test: shutdown-drains-what-is-queued -->
<!-- SV1 wave 3: shutdown as a poison pill behind the queue -->
`shutdown()` is a graceful drain, not a kill.
```maxon
type Log
	var acc as int

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d int)
		self.acc = self.acc + d
	end 'push'

	export function read() returns int
		return self.acc
	end 'read'
end 'Log'

function main() returns ExitCode
	let h = spawn Log.create()
	h.push(1)
	h.push(2)
	let n = try await h.read() otherwise 0
	h.shutdown()
	return n as ExitCode
end 'main'
```
```exitcode
3
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

<!-- disabled-test: an-idle-service-with-a-global-handle-still-exits-zero -->
<!-- SV1 wave 3: the exit drain — dropping the last handle enqueues __shutdown -->
Process exit must not hang on a service parked in `recv`, which is its steady state.
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

<!-- disabled-test: error.a-sent-value-is-moved-and-reading-it-back-is-refused -->
<!-- SV1 wave 3: the move door at the send site -->
A send consumes its argument; the source is poisoned and a read is E3102.
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
	let buf = "hello"
	h.keep(buf)
	print("{buf}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:10: 'buf' was moved and cannot be read
```

<!-- disabled-test: error.a-co-owned-value-may-not-be-sent -->
<!-- SV1 wave 3: the move door — the co-owner refusal over the escape/retain marks -->
A value promoted to `shared` by escape analysis has a second referent, which is exactly what the plain
refcount forbids across a green thread.
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
	let buf = "hello"
	let peek = function() gives buf
	h.keep(buf)
	return 0
end 'main'
```
```maxoncstderr
error E3135: <fragment>:17:2: 'buf' is captured by a closure and has a second owner, so it cannot be sent
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

<!-- disabled-test: fire-and-forget-cycle-is-legal -->
<!-- SV2: only AWAITED replies are edges in the blocking graph -->
⭐ The case that pins "only blocking edges count" — without it a later tightening would silently ban
correct programs. A sends to B, B sends back to A, both non-blocking.
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

	export function pong(a A.handle)
		a.ack()
	end 'pong'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	return 0
end 'main'
```
```exitcode
0
```

<!-- disabled-test: an-export-reached-only-by-message-is-not-unused -->
<!-- SV1 wave 3: the UnusedExportCheck arm — reachable only once a spawn compiles -->
An export method reachable only as a MESSAGE must count as used, or the unused-export check would refuse
every service whose handle is the only caller.
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

<!-- disabled-test: handles-in-an-array -->
<!-- W217: an `Array with Promise` never drops its elements — exit 75 -->
Handles are ordinary boxes and live in containers.
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

<!-- disabled-test: unioncases-tags-the-request-variants -->
<!-- SV1 wave 3: the request union reaches codegen -->
The synthesized request union is an ordinary union, so its `.unionCases` companion exists.
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
Send-uniqueness does not survive a function boundary, and that is the design's biggest recorded weakness.
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
error E3135: <fragment>:14:8: 'buf' arrived as a parameter and cannot be proven unique at the send
```
