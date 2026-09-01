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
verdict the PARSE reaches — a token shape, a declaration, a transferability rule, a move, E3137, E3140 — is
target-neutral by construction, so a marker on one would hide a green lane rather than describe a red one.
`spawn-is-not-a-keyword` and `service-error-is-declared-and-nameable` are unmarked for the neighbouring
reason: they RUN, but they start no service and reach no scheduler at all.

⛔⛔ **THE DISCRIMINATOR IS *WHO REACHES THE VERDICT FIRST*, NOT *IS THE RULE TARGET-NEUTRAL* — AND FOUR
CASES WERE MARKED WRONG BY READING IT THE SECOND WAY (SV2 review).** A parse refusal THROWS and the compile
stops, so the fragment's only diagnostic is the one the case pins. A verdict from a whole-program
`SemanticCheck` pass — **E3139**'s cycle graph and **E3100**'s await linearity — does not: `checkCalls` has
already recorded an **E3104** for every `spawn`, every send and every reply cell in the program, and the
case's own program contains all three. MEASURED at review on `--target=wasm32-wasi`:
`error.two-services-that-await-each-other-are-refused`, `error.double-await-of-a-reply`,
`cycle-through-a-free-function-is-refused` and `cycle-same-type-self-edge-is-refused` printed five E3104
lines ahead of the diagnostic they pin and the lane went **RED, 4 failed**. They carry the marker now — the
rule they pin is target-neutral and the x64 lane pins it; what is not target-neutral is the SCAFFOLDING they
need to reach it, which is the shape `project_w96_e3104_masks_the_case_subject` records. ⇒ **a case whose
refusal is not a parse throw needs the marker, however target-neutral the rule.**

⚠ **THE TWO E3104 CASES ARE THE EXCEPTION, AND THEY ARE MARKED WITH THE TARGET THEY REFUSE.** A refusal
whose whole subject is *"this target has no substrate for it"* is the one verdict in this file that is not
target-neutral, so it can only be pinned by compiling FOR that target — exactly as
`subprocess-builtins.rejected-on-wasm` is. They exist because the gate did not: MEASURED at review on all
three non-host lanes, a `spawn` reached the backend and PANICKED the compiler
(`StdToArm64Conversion.maxon:652`, `StdToWasm.maxon:1748`, `StdToX64Conversion.maxon:3382`) where the same
substrate reached by `sleep` has always answered E3104.

## Tests

<!-- test: companions-resolve-as-types -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'

	export function total() returns Integer
		return self.count
	end 'total'

	function record(v Integer) returns Integer
		return v
	end 'record'
end 'Calc'

function serve(_ Calc.handle) returns Integer
	return 1
end 'serve'

function describe(_ Calc.request) returns Integer
	return 2
end 'describe'

function main() returns ExitCode
	let h = spawn Calc.create()
	return serve(h) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: companions-come-from-a-spawn-in-another-file -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var count as Integer

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(_ Calc.handle) returns Integer
	return 1
end 'serve'

typealias Integer = int(i64.min to i64.max)
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var count as Integer

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export function serve(_ Calc.handle) returns Integer
	return 1
end 'serve'

typealias Integer = int(i64.min to i64.max)
// --- file: runner.maxon
type Runner
	var started as Integer

	static function create() returns Self
		return Self{started: 0}
	end 'create'

	function start() returns Integer
		let h = spawn Calc.create()
		return serve(h)
	end 'start'
end 'Runner'

function main() returns ExitCode
	return Runner.create().start() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: error.spawn-of-a-bare-function-refused -->
⚖ There is no unstructured green thread in this language. `spawn work()` names no type, so it starts no
service, and the refusal teaches the one form rather than reporting a syntax error.
```maxon
function work() returns Integer
	return 1
end 'work'

function main() returns ExitCode
	let h = spawn work()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
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
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.bump()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3134: <fragment>:15:10: `spawn Calc.bump(…)` does not start a service: `Calc.bump` is an INSTANCE method, and a `spawn` calls its factory directly — there is no service yet for a message to reach. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-of-a-static-that-returns-something-else-refused -->
A static that does not hand back the type has produced no state for the message loop to own.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function version() returns Integer
		return 3
	end 'version'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.version()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3134: <fragment>:19:10: `spawn Calc.version(…)` does not start a service: `Calc.version` does not hand back a `Calc` the service can own — a service's state is the BOX of a declared `type`, and this factory's recorded return type is not one. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-self-dot-refused -->
The whole-program walk that decides which types are services reads tokens with no type scope, so `Self`
resolves to nothing there. It is refused in its own words rather than dying as an unexpected token.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function start() returns Integer
		let h = spawn Self.create()
		return 0
	end 'start'
end 'Calc'

function main() returns ExitCode
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
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
	var count as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3135: <fragment>:17:10: parameter `p` of the message `Calc.hold` is a Promise, which is a green-thread handle its awaiter owns, and this `spawn` makes `Calc` a service — whose messages MOVE their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-function-value-not-transferable -->
A function value reaches a captured environment block, which is a box with a second referent by
construction.
```maxon
typealias IntOp = function(n Integer) returns Integer

type Calc
	var count as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

type Calc
	var count as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3135: <fragment>:27:10: parameter `s` of the message `Calc.measure` is a value held at an interface type, which is a fat pointer released through a witness the request union cannot carry, and this `spawn` makes `Calc` a service — whose messages MOVE their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.overloaded-message-refused -->
A message becomes ONE variant of the request union, and one variant carries one payload shape.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(n Integer)
		self.count = self.count + n
	end 'add'

	export function add(a Integer, b Integer)
		self.count = self.count + a + b
	end 'add'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Only `export`/`public` INSTANCE methods are messages. `record` is file-private and `version` is a static,
so neither is subject to the transferability rule — a `Promise` parameter on either is perfectly legal on
a type that is spawned, which is what this case pins. Compare
`error.message-param-promise-not-transferable`, whose only difference is the `export`.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias IntPromise = Promise with Whole

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	static function fromPromise(_ IntPromise) returns Self
		return Self{count: 0}
	end 'fromPromise'

	function record(_ IntPromise) returns Integer
		return 1
	end 'record'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	spawn Calc.create()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: a-factory-may-be-spelled-with-a-keyword -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var n as Integer

	static function from(path Integer) returns Self
		return Self{n: path}
	end 'from'

	export function read() returns Integer
		return self.n
	end 'read'
end 'Reader'

function serve(_ Reader.handle) returns Integer
	return 1
end 'serve'

function main() returns ExitCode
	spawn Reader.from(3)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
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
	var spawn as Integer

	static function spawn(n Integer) returns Self
		return Self{spawn: n}
	end 'spawn'

	function tally() returns Integer
		return self.spawn
	end 'tally'
end 'Job'

function run(spawn Integer) returns Integer
	return spawn + 1
end 'run'

function main() returns ExitCode
	let j = Job.spawn(3)
	let spawn = 4
	return (j.tally() + run(spawn) + spawn) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
12
```

<!-- test: service-error-is-declared-and-nameable -->
`ServiceError` is declared by `stdlib/Builtins.maxon` and carries no `__` prefix, so a user may throw it
and name its case in a `match`. The runtime that produces it lands with the mailbox; the declaration is
what a reply's synthesized error union will be built from.
```maxon
function risky(n Integer) returns Integer throws ServiceError
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: spawn-send-and-the-service-runs -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=2
```

<!-- test: messages-are-serialized-in-fifo-order -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Handlers run one at a time, in send order — so three digits pushed in order read back as one number.
```maxon
type Log
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d Integer)
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
acc=123
```

<!-- test: two-instances-are-independent -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Two spawns of one type are two services with two states.

⚠ **THE ORDER OF THE TWO PRINTS IS FORCED BY CAUSALITY AND NOT BY LUCK.** `b` prints its own count and then
sends `report` to `a`, so `a`'s line cannot be written until `b`'s handler has already written its own — two
services printing on two green threads with nothing between them would be ordered by the scheduler. Moving
`a`'s handle INTO `b`'s mailbox is what buys that ordering, and it is the same transfer
`a-handle-moved-into-another-service` pins on its own.
```maxon
type Counter
	var id as Integer
	var n as Integer

	static function create(id Integer) returns Self
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
2=1
1=2
```

<!-- test: the-same-type-is-used-directly-and-spawned -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ The location-transparency property, and the test a dedicated `service` declaration would have made
impossible: one type, one method, reached both ways in one program. The direct calls run before the `spawn`
exists, so the two lines are ordered by the program rather than by the scheduler.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
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
typealias Integer = int(i64.min to i64.max)
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
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function record(v Integer)
		self.count = v
	end 'record'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.record(1)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3136: <fragment>:16:4: `record` is declared on `type Calc` but is not a message: only its `export` INSTANCE methods are on `Calc.handle`. That is the isolation boundary, and it is what makes a self-send unspellable — a private helper can only ever be reached by a DIRECT call, from inside a message body or from a `Calc` value. Export it to make it a message, or call it on a `Calc` value
```

<!-- test: shutdown-drains-what-is-queued -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`shutdown()` is a graceful drain, not a kill: the poison pill goes in BEHIND everything already queued, so
every message sent before it still runs.

⚠ `shutdown` is the handle's own method and is not a message of `Log`. It follows `clone`'s precedent
exactly (`Parser.structCloneIsSynthesized`): a service that declares an `export function shutdown()` of its
own WINS, and the compiler's pill is then unspellable for it — which leaves dropping the last handle, the
other road to the same drain.
```maxon
type Log
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function push(d Integer)
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
acc=3
```

<!-- test: a-send-after-shutdown-is-dropped-cleanly -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **THE CASE THAT REACHES THE ABANDON PATH, AND IT REACHES IT BY EITHER OF TWO ROADS.** The second `keep`
is sent after the poison pill, so the loop never runs its handler — and which road drops its `String` is a
race this case deliberately does not resolve: if the send wins, the envelope is queued behind the pill and
the loop's exit drain abandons it; if the loop wins, the mailbox is already closed and `__mbox_send` abandons
it inline. **Both roads must print exactly the same thing and leak exactly nothing**, which is the property
worth pinning — the drop of a moved-in payload nobody will ever handle.
```maxon
type Store
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept a1
```

<!-- test: dropping-the-last-handle-shuts-the-service-down -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
⚠ **THE `procs: 1` PIN IS THIS CASE'S OWN STATED PREMISE, WRITTEN DOWN WHERE THE RUNNER CAN READ IT.** The
paragraph below has always said *"nothing runs on a service's green thread until the main thread stops
running"*, and named the default `MAXON_MAX_PROCS=1` as why. `e05b883518` deleted that default — the count
is the machine's now — so the premise stopped holding while the sentence asserting it stayed. The ORDER
`beep 2` then `beep 1` follows from the premise and not from the feature: with a second M the two services
run concurrently and either order is correct, MEASURED 2/5 red on arm64-macOS and 3/3 on arm64-linux. So
the expectation is unchanged and the CONDITION is pinned — the same repair `e05b883518` itself applied to
five `sched-runqueue` cases in the commit that removed the default.

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
	var tag as Integer

	static function create(tag Integer) returns Self
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
beep 2
beep 1
```

<!-- test: a-cloned-handle-keeps-the-service-alive -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A handle is an ordinary box, so `.clone()` reaches it — and on a handle the clone is a second HANDLE to the
SAME service, not a second service. Dropping one leaves the mailbox open; the last one to go closes it.
```maxon
type Counter
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=2
```

<!-- test: a-handle-moved-into-another-service -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A handle is a transferable message payload: `Worker` is handed the `Logger`'s handle, sends to it, and then
DROPS it — the un-consumed payload drop the loop owes for every message it runs. That drop is the `Logger`'s
last handle, so the logger shuts down without `main` ever naming it again.
```maxon
type Logger
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String)
		print("log: {s}\n")
	end 'say'
end 'Logger'

type Worker
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
log: from the worker
```

<!-- test: handles-in-an-array -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Handles are ordinary boxes and live in containers — which means the array's element drop is the handle drop,
and two services shut down when the array does.
```maxon
type Counter
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
2
```

<!-- test: a-handle-payload-beside-a-consumed-string-payload -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var bytes as Integer

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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
bytes=15
```

<!-- test: a-string-argument-moves-into-the-service -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A managed argument is MOVED: the sending frame hands over the reference it holds and the service becomes the
box's one owner. Nothing is increfed at the send and nothing is dropped by the sender, which is what keeps
the plain refcount correct across a green thread.
```maxon
type Store
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept a1
```

<!-- test: unioncases-tags-the-request-variants -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The synthesized request union is an ordinary union, so its `.unionCases` companion exists — and `__shutdown`
holds variant 0, so the first message an author declares is variant 1.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function main() returns ExitCode
	spawn Calc.create()
	return Calc.request.unionCases.bump.rawValue as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: an-idle-service-that-is-never-sent-to-still-exits-zero -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Process exit must not hang on a service parked in `recv`, which is its steady state. Here the mailbox is
closed by the handle's own drop — at the end of the `spawn` STATEMENT, since nothing binds it — well before
`main` returns, so the loop's `recv` answers 0 the first time it is asked and the exit drain has one
already-finished thread to reap.

⚠ The handle is deliberately NOT bound: an unread `let` is `E3012 unused variable`, which is the language's
rule for every binding and not a service question (`reportUnusedBindings` follows both references). A case
that wants a handle BOUND has to read it, which `dropping-the-last-handle-shuts-the-service-down` does.
```maxon
type Idler
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: fire-and-forget-cycle-is-legal -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ The case that pins "only blocking edges count" — without it a later tightening would silently ban correct
programs. `A` names `B`'s handle in a message and `B` names `A`'s, which is a cycle in the type graph and no
cycle at all in the blocking one, because neither send waits.
```maxon
type A
	var n as Integer

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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: a-fire-and-forget-send-is-not-a-blocking-edge -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function kick(peer B.handle, mine A.handle) returns Integer
		peer.work(mine.clone())
		return 1
	end 'kick'

	export function ack() returns Integer
		return 7
	end 'ack'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function work(back A.handle)
		let v = try await back.ack() otherwise 0
		print("acked {v}\n")
	end 'work'

	export function drain() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```
```stdout
acked 7
```

<!-- test: an-export-reached-only-by-message-is-not-unused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
An export method reachable only as a MESSAGE must count as used, or the unused-export check would refuse
every service whose handle is the only caller. It is credited by the send op naming `Calc.bump` — the same
`maxonOpCalleeKind` road an ordinary call is credited by, which is why this needs no arm of its own.
```maxon
// --- file: calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

typealias Integer = int(i64.min to i64.max)
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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:18:10: use of moved value 'buf': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-co-owned-value-may-not-be-sent -->
A value a closure captured has a second owner on this green thread, which is exactly what the plain refcount
forbids across two. The send is refused rather than silently increfed, and `.clone()` is the fix.
```maxon
type Store
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Payload'

type Store
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(x Integer)
		self.n = self.n + x
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(1)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3104: <fragment>:15:10: this construct is x64-windows only at this rung: 'spawn' lowers to the runtime entry '__svc_spawn', which has no wasm32-wasi implementation
error E3104: <fragment>:16:2: this construct is x64-windows only at this rung: a message send lowers to the runtime entry '__mbox_send', which has no wasm32-wasi implementation
```

<!-- test: error.a-service-is-rejected-on-a-native-target -->
<!-- targets: x64-linux -->
The same gate on a second NON-wasm lane, which is what makes this a TARGET rule rather than a wasm one.

⚠⚠ **THIS CASE NAMED `arm64-macos` UNTIL THE arm64-macOS GREEN-THREAD FLOOR LANDED, AND ITS OWN PROSE IS
WHY IT MOVED HERE RATHER THAN BEING DELETED.** It recorded the measured reason for the refusal as
*"`lowerTlsAlloc: TlsAlloc is x64-windows only` before the gate, E3104 after"* — a SCHEDULER primitive, not
a service one. That primitive is exactly what MAC3 supplied, so a service now compiles and runs on
arm64-macOS and this case went red saying *"expected a compile error but compilation succeeded"*. The
subject — a service refused at its own span on a native lane with no green-thread floor — is unchanged and
still true here; the same prose already recorded that `x64-linux` was MEASURED to answer identically, which
is what makes this the honest re-point rather than a lane picked to keep a case green.

⚠ **THE LANE IS `x64-linux` NOW, AND THE ARGUMENT THAT ONCE PICKED arm64-linux OVER IT WAS MEASURING THE
WRONG THING.** Both refuse identically — the same two `E3104`s with only the target name differing — so
the refusal never picked between them. What used to pick was reachability: *"`x64-linux` needs WSL and so
is unreachable from a macOS host, while `arm64-linux` runs here through OrbStack."* **That is a fact about
RUNNING a binary, and this case runs none** — a `maxoncstderr` case is compiled and its diagnostics are
compared. MEASURED at this re-point: `--target=x64-linux` emits both of these E3104s on a macOS host with
no WSL anywhere. The lane held to be the one that "cannot go red" could go red the whole time.

⇒ **AND THE NAME NO LONGER CARRIES A TARGET AT ALL.** Ending it `-on-arm64` made the target a THIRD copy
beside the marker and the diagnostic text, and forced a rename on each of the two re-points. The marker
and the text are the pair the runner checks against each other; the name was the copy nothing verified —
the project's own signature bug, one fact written down twice with the copies free to disagree, sitting in
a test name. Its siblings `async-sleep.rejected-on-a-native-target` and
`subprocess-builtins.streaming-rejected-on-a-native-target` moved the same way in this same change.
```maxon
type Plot
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(x Integer)
		self.n = self.n + x
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	h.at(1)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3104: <fragment>:15:10: this construct is x64-windows only at this rung: 'spawn' lowers to the runtime entry '__svc_spawn', which has no x64-linux implementation
error E3104: <fragment>:16:2: this construct is x64-windows only at this rung: a message send lowers to the runtime entry '__mbox_send', which has no x64-linux implementation
```

<!-- test: a-scalar-only-record-crosses-whole -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ The POSITIVE CONTROL for the three cases below, and the shape the transfer rule admits: a record whose
every slot is a machine word holds no reference to anything, so moving it moves the whole of it. Built at the
send, so this frame gives up the only reference there was.
```maxon
type Point
	export var x as Integer
	export var y as Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plot
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	export var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	var bytes as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	export var n as Integer

	export static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Svc
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3138: <fragment>:29:9: argument `cs` of the message `Svc.take` is a container, whose elements a push increfs rather than moves — so this frame may still own what is in it (`cs`). This frame owns that record alone — but a record it POINTS AT may have a second owner, because every co-owning store takes a reference where a move would give one up, and soleness is not transitive. A send MOVES the record WHOLE, so the second owner would be left on this green thread with a plain refcount racing the service's. What may cross today is a scalar, a `String`, a service HANDLE, or a record whose every slot is a SCALAR — a `String` FIELD does not make a record crossable even though a `String` ARGUMENT crosses, because the store that put it there took a reference where a move would have given one up. Proving the rest needs a record's whole graph tracked through the co-owning stores, which is a whole-program fact this compiler does not yet compute. Send the scalars the record is built from, or keep the record on this side and send what the service needs of it
```

<!-- test: error.a-promise-may-not-be-sent -->
A `Promise` is a green-thread handle its awaiter owns, and a message MOVES its arguments to another green
thread — so sending one would leave a second thread holding a thread this one is still waiting on.

⛔ **THE DIAGNOSTIC MOVED FROM E3135 TO E3005 WHEN PROMISES BECAME TYPED (`W230`), AND THAT IS THE HONEST
FAULT.** The old sentence here read *"its value is a bare integer — so a message declaring an `int`
parameter would take one without a word if the send site did not ask"*, and the rule existed precisely
because the type system could not object. It can now: `keep(value Integer)` does not accept a
`Promise with Integer`, so the ordinary argument check refuses this send before the service rule is
consulted. The E3135 arm is KEPT as the structural backstop for the case the type check cannot reach — a
message that DECLARES a promise parameter, which typechecks and must still be refused.
```maxon
typealias Integer = int(i64.min to i64.max)

function work() returns Integer
	return 5
end 'work'

type Store
	var n as Integer

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
error E3005: <fragment>:23:9: argument type mismatch for 'value': expected 'Integer', got 'Promise with int(-9223372036854775808 to 9223372036854775807)'
```

<!-- test: error.a-handle-of-another-service-is-refused -->
Two services' handles are two nominal types, so handing one where the other is expected is the ordinary
struct-identity mismatch and needs no rule of its own.
```maxon
type Calc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'
end 'Calc'

type Logger
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	var count as Integer

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
typealias Integer = int(i64.min to i64.max)
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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:16:2: Unsupported: `try` on the message `Calc.bump` — a SEND is not the reply. It enqueues and returns, so it can fail at nothing; what carries an error is the reply, and awaiting it is what makes that error this frame's. Write `try await <handle>.<message>(…)`, or drop the `try` on a message that returns nothing and throws nothing
```

<!-- test: send-and-await-a-reply -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A value-returning message is awaitable RPC.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.bump(3)
	let n = try await h.total() otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: a-message-throws-and-the-error-merges-with-serviceerror -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The merge is always two-way — transport plus one handler.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws MathError
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
71
```

<!-- test: a-call-after-shutdown-answers-stopped -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A stopped service resolves its pending replies rather than hanging their awaiters.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function total() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
9
```

<!-- test: error.a-reply-may-not-alias-service-state -->
A handler must not return a value this frame does not solely own, or the caller ends up aliasing a box the
service still names — two green threads naming one box, with a plain refcount between them. `return self` is
the shortest way to say it and the only one a service STATE can currently spell: a state record may hold
nothing but scalars (`error.a-record-with-a-managed-field-may-not-cross`), so a FIELD read has nothing managed
to hand back yet. The other population is a message PARAMETER — see the case below it.

⚠ The blame names the RETURN, and the note names the **`spawn`** that made the type a service — whether a
type is a service is a whole-program property, and the `spawn` deciding it may be in another file entirely
from the method the rule fires on.
```maxon
type Store
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3137: <fragment>:10:3: `Store.itself` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, whose one reference the request box still holds — and this `spawn` makes `Store` a service. The caller would then hold a second reference to that box, on another green thread, with a plain reference count between them. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:15:10: the `spawn` that makes `Store` a service
```

<!-- test: error.a-reply-may-not-return-a-message-parameter -->
⭐⭐ **THE SECOND POPULATION E3137 REFUSES, AND THE SENTENCE SAID NOTHING ABOUT IT UNTIL SV2's REVIEW.** `s`
is reachable from nothing — it is a message PARAMETER, which arrives BORROWED out of the request box the loop
still owns and releases (`ServiceLoop.dropUnconsumedPayloads`). Handing it back would give the awaiter a
second reference to that box, which is the same two-green-threads-one-refcount picture `return self` draws.

⚠ **THE CURE IS THE SAME `.clone()`**, which is why the old wording still helped the author who hit this —
but it told them their value was reachable from `self` when it was not, and a refusal's noun is what a reader
takes away.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function echo(s String) returns String
		return s
	end 'echo'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let r = try await h.echo("hi") otherwise ""
	print("{r}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3137: <fragment>:10:3: `Store.echo` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, whose one reference the request box still holds — and this `spawn` makes `Store` a service. The caller would then hold a second reference to that box, on another green thread, with a plain reference count between them. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:15:10: the `spawn` that makes `Store` a service
```

<!-- test: error.two-services-that-await-each-other-are-refused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Mutual reentrancy is made unrepresentable rather than diagnosed at run time.
```maxon
type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle) returns Integer
		return try await b.pong() otherwise 0
	end 'ping'

	export function ack() returns Integer
		return 1
	end 'ack'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function pong() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3139: <fragment>:10:14: service call cycle — these messages can deadlock waiting on each other:
    `A.ping` (<fragment>:10:14) awaits a reply from `B`
    `B.pong` (<fragment>:26:14) awaits a reply from `A`
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
```

<!-- test: a-reply-error-type-with-one-member-is-nameable -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **A MESSAGE THAT THROWS NOTHING HAS A ONE-MEMBER REPLY ERROR TYPE, AND ONE MEMBER IS A NAME.** The reply of
`total()` can fail only in transport, so its error type is `ServiceError` itself — an ordinary declared enum a
`throws` clause can spell, which is what lets a bare `try` PROPAGATE it out of an intermediate function. Only a
message that throws needs the two-member union, and that one is spelled `<Service>.<method>.errors` — a name the
compiler synthesizes rather than one an author declares (see `a-declared-throws-clause-names-a-replys-errors`).
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function fetch(h Calc.handle) returns Integer throws ServiceError
	return try await h.total()
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try fetch(h) otherwise 1
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: error.bare-try-propagation-of-a-two-member-reply-is-refused -->
A message that THROWS has a two-member reply error type, and a `throws` clause CAN name it — the pair is
synthesized as `Calc.divide.errors`, which is what `a-declared-throws-clause-names-a-replys-errors` propagates
through. What THIS case pins is the MISMATCH: `fetch` declares `throws MathError`, the handler half alone,
which is not the reply's error type — so the bare `try` that would re-publish the fused flag under that
narrower name earns the same E3059 an ordinary type mismatch does. Either name the reply's own error type or
CATCH it here, which is what `otherwise (e)` is for.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return try (n / by) otherwise 0
	end 'divide'
end 'Calc'

function fetch(h Calc.handle) returns Integer throws MathError
	return try await h.divide(10, by: 2)
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3059: <fragment>:22:9: try propagates 'Calc.divide.errors' but enclosing function throws 'MathError' — add 'otherwise' to convert
```

<!-- test: a-declared-throws-clause-names-a-replys-errors -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **THE SPELLING THE PREVIOUS CASE'S MISMATCH IMPLIES** (`SERVICES_DESIGN.md:583`). The fused pair is a
nominal enum registered under `<Service>.<method>.errors`, so a `throws` clause can name it — and once it
can, a bare `try` inside `fetch` re-publishes the flag VERBATIM to `fetch`'s own caller instead of being
refused. The two members survive the hop: `main` catches at the call and still selects between transport and
handler.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return try (n / by) otherwise 0
	end 'divide'
end 'Calc'

function fetch(h Calc.handle) returns Integer throws Calc.divide.errors
	return try await h.divide(10, by: 0)
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try fetch(h) otherwise (e) 'oops'
		match e 'why'
			stopped then return 70 as ExitCode
			divideByZero then return 71 as ExitCode
		end 'why'
	end 'oops'
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
71
```

<!-- test: error.an-ambiguous-bare-arm-over-a-fused-reply-error-is-refused -->
Fusing two members into one dispatch table can collide on a bare case name: `Halt` declares a `stopped` of its
own, so `stopped` in the `match` names both `ServiceError.stopped` and `Halt.stopped` and there is no rule
that picks one. The qualified spelling disambiguates; the bare one is REFUSED rather than silently selecting a
member, which is the same E3085 an ordinary two-member `try` union earns.
```maxon
enum Halt implements Error
	stopped
	halted
end 'Halt'

type Guard
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function risky() returns Integer throws Halt
		if self.count == 0 'halt'
			throw Halt.halted
		end 'halt'
		return self.count
	end 'risky'
end 'Guard'

function main() returns ExitCode
	let h = spawn Guard.create()
	try await h.risky() otherwise (e) 'oops'
		match e 'why'
			stopped then return 70 as ExitCode
			halted then return 71 as ExitCode
		end 'why'
	end 'oops'
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3085: <fragment>:26:4: case 'stopped' is shared by multiple union members; qualify with 'EnumName.stopped'
```

<!-- test: shutdown-resolves-pending-replies -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **THE LIVENESS OBLIGATION, ON BOTH OF ITS ROADS.** A message that dies unprocessed must not hang its awaiter.
The FIRST send is queued behind the poison pill and is abandoned by the loop's own drain; the SECOND arrives
after the mailbox is already closed and is abandoned by the send. Both answer `ServiceError.stopped`, and the
case passes by TERMINATING at all.
```maxon
type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
2
```

<!-- test: a-string-moves-in-and-a-fresh-string-comes-back -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The reply carries a MANAGED value across green threads. It is sound for the reason the send's move is: the
handler's result is freshly minted in the handler's own frame, so the service gives up its only reference and
the awaiter takes it — one owner throughout, which is what a plain refcount requires.
```maxon
type Echo
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```stdout
[hi]
```

<!-- test: reply-discarded-is-dropped-clean -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ A reply-bearing message sent in STATEMENT position mints a cell nobody binds, so the promise is dropped at
statement end while the cell is still PENDING. The dropper may not free it — the replier is about to write into
it — so it adds the consumer ticket only, and the reply's own completion supplies the runner's half. The managed
result nobody took is released by whichever arrives second.
```maxon
type Echo
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: pending-reply-dropped-then-answered -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The dropped cell of the first send is still pending when the SECOND send's await drives the service, so the
replier writes into a cell its awaiter has already renounced. That is the ordering the teardown rendezvous
exists for, and freeing the cell at the drop is a clobbered green thread rather than a leak.
```maxon
type Echo
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'

	export function count() returns Integer
		return 5
	end 'count'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	h.say("a")
	let n = try await h.count() otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: completed-reply-dropped-is-reclaimed -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The other order: `p`'s first cell COMPLETES while the middle await drives the service (its message is ahead in
FIFO order), and only THEN is it dropped — by the RE-ARM on the next line. The drop finds the runner ticket
already there and reclaims; an arm that took a completed cell as merely queued would strand the struct
invisibly, because the green-thread count the exit gate reads has already been debited.
```maxon
type Echo
	var n as Integer

	static function create() returns Self
		return Self{n: 4}
	end 'create'

	export function count() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```

<!-- test: rpc-from-inside-a-service -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **THE FIRST CROSS-GREEN-THREAD WAKE IN THE LANGUAGE.** `Outer`'s message awaits a reply from `Inner`, so a
green thread — not the main one — is the awaiter, and the drive that completes its cell runs `Inner` from
`Outer`'s own stack. The graph `Outer → Inner` is acyclic, which is what makes this legal.
⚠ The peer's handle crosses as a message ARGUMENT and not as `Outer`'s state: a state record may hold nothing
managed at all (`error.a-record-with-a-managed-field-may-not-cross`), which is a live limit of SV1's transfer
rule rather than anything this rung changes.
```maxon
type Inner
	var n as Integer

	static function create() returns Self
		return Self{n: 3}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Inner'

type Outer
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function doubled(inner Inner.handle) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: error.double-await-of-a-reply -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Per-await linearity composes for free, because it keys on the promise's own identity rather than on what
produced it — a reply cell is a `Promise` like any other. Which is also why the refusal is the parser's
E3142 and not the linearity pass's E3100: the second `await` READS a name whose thread the first one
already handed back, and that is a use of a spent promise before it is a second await. See
`async-linearity.md`'s *Documentation* for the one statement of how the three codes divide this family.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function total() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3142: <fragment>:18:20: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: error.plain-await-of-a-reply -->
A reply ALWAYS throws `ServiceError`, whatever the message itself declares — so a plain `await` of one is
E3057 by construction and there is no reply in the language that can be awaited without `try`.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let n = await h.total()
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
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
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function wipe() returns Integer throws Trouble
		throw Trouble.detail("no")
	end 'wipe'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3140: <fragment>:19:10: the message `Store.wipe` declares a reply that throws `Trouble`, a PAYLOAD-CARRYING union whose error flag is a heap box pointer — a reply's one error word already carries the fused ordinal that tells `ServiceError` apart from the message's own error, and a pointer is not an ordinal, and this `spawn` makes `Store` a service — whose reply-bearing messages resolve through a CELL, a green thread that never runs, carrying one value word and one error word. Return an integer, a `String`, a struct or a service handle, and throw a payload-free `enum`; or drop the `returns` and `throws` clauses, which makes the message fire-and-forget and gives it no reply to carry
```

<!-- test: cycle-through-a-free-function-is-refused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
An edge is transitive through ordinary functions: `A.ping` calls `relay`, which awaits a `B.handle`, so the
edge `A → B` exists even though `A`'s own body names no `B` message.

⚠ The hop is anchored at `relay`'s `await` rather than at `A.ping` — that IS where the thread stops, and a
message that blocks only through a helper has no await of its own to point at.
```maxon
function relay(b B.handle, peer A.handle) returns Integer
	return try await b.pong(peer.clone()) otherwise 0
end 'relay'

type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle, peer A.handle) returns Integer
		return relay(b, peer: peer)
	end 'ping'

	export function ack() returns Integer
		return 1
	end 'ack'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function pong(peer A.handle) returns Integer
		return try await peer.ack() otherwise 0
	end 'pong'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3139: <fragment>:3:13: service call cycle — these messages can deadlock waiting on each other:
    `A.ping` (<fragment>:3:13) awaits a reply from `B`
    `B.pong` (<fragment>:30:14) awaits a reply from `A`
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
```

<!-- test: cycle-same-type-self-edge-is-refused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⚠ **A SELF-EDGE IS A CYCLE, AND THIS IS THE ONE USERS WILL HIT.** Two instances of the same service could not
actually deadlock, but edges are by TYPE — which is what makes them statically knowable at all — so the
analysis cannot tell the instances apart and must be conservative. The message says the workaround.
```maxon
type Worker
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask(peer Worker.handle) returns Integer
		return try await peer.answer() otherwise 0
	end 'ask'

	export function answer() returns Integer
		return 1
	end 'answer'
end 'Worker'

function main() returns ExitCode
	let w = spawn Worker.create()
	w.ask(w.clone())
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3139: <fragment>:10:14: service call cycle — these messages can deadlock waiting on each other:
    `Worker.ask` (<fragment>:10:14) awaits a reply from `Worker`
    Two distinct instances of `Worker` would not deadlock — but edges are by TYPE, which is what makes them statically knowable at all, so the analysis cannot tell the instances apart and must be conservative. Make the peer call fire-and-forget (send it as a statement and have the peer reply with a separate message), or split the role into two types
```

<!-- test: cycle-behind-a-second-await-is-still-refused -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⛔⛔ **ONE MESSAGE OWES AN EDGE PER SERVICE IT AWAITS, NOT ONE EDGE.** `A.ping` awaits `B` and then `C`; the
`A → B` half is what closes the ring `A.ping → B.pong → A.ack`, and the `A → C` half is innocent. This case
is `error.two-services-that-await-each-other-are-refused` with **one extra, unrelated `await` appended**, and
it COMPILED CLEAN — exit 0 — while the graph carried one site per FUNCTION and kept whichever the op walk saw
LAST (SV2 review; see `ServiceCallCycleCheck.ServiceAwaitRoster`). A dropped edge is a MISSED refusal, which
is the deadlock this rule exists to make unrepresentable. **Delete the `C` await and the case still refuses —
which is the point: it must refuse WITH it.**
```maxon
type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle, c C.handle) returns Integer
		let x = try await b.pong() otherwise 0
		let y = try await c.tick() otherwise 0
		return x + y
	end 'ping'

	export function ack() returns Integer
		return 1
	end 'ack'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function pong() returns Integer
		return try await spawnA().ack() otherwise 0
	end 'pong'
end 'B'

type C
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick() returns Integer
		return 2
	end 'tick'
end 'C'

function spawnA() returns A.handle
	return spawn A.create()
end 'spawnA'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	let c = spawn C.create()
	a.ping(b, c: c)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3139: <fragment>:10:15: service call cycle — these messages can deadlock waiting on each other:
    `A.ping` (<fragment>:10:15) awaits a reply from `B`
    `B.pong` (<fragment>:28:14) awaits a reply from `A`
    A message may not await a reply from a service that can await back. Break the ring by making one of these calls fire-and-forget — drop its `returns` and `throws` clauses, or send it as a statement and do not await it — because a non-blocking send is not part of the graph
```

<!-- test: a-blocking-cycle-through-an-indirect-call-aborts -->
<!-- targets: x64-windows, arm64-macos -->
<!-- procs: 4 -->
⛔⛔ **THE HALF OF THE DEADLOCK RULE THAT IS A RUNTIME PROPERTY, BECAUSE E3139 CANNOT REACH IT.**
`ServiceCallCycleCheck` walks a graph of NAMED callees, so every case above it can be refused at compile
time. This program's `A → B` edge goes through a **closure value** — `callIndirect` calls whatever
function it was handed, and what it was handed is decided at the call site rather than at the callee's
name — so the roster records `B → A` alone, which is not a ring, and the program **compiles clean**. The
ring is real all the same: `main` awaits `A.kick`, `A.kick` awaits `B.work` through the closure, and
`B.work` awaits `A.ack` — which `A` cannot serve, because `A` is inside `kick`.

⇒ **the refusal a static check cannot make, the runtime must.** Every green thread in the program is
parked and none of them can ever become ready, which is exactly the `nothingLeft` arm of
`__sched_find_runnable` — `RuntimeAbort.schedulerDeadlock`, **exit 92**, silent on both streams. The
answer is a diagnosis rather than a hang: a wedged process tells you nothing and costs a 120 s harness
timeout; a 92 names the condition.

⭐⭐ **THE EXIT CODE WAS TAKEN EMPIRICALLY, AND THIS CASE WAS THE ACCEPTANCE FOR A DEFECT THAT IS NOW
CLOSED (A1).** At `MAXON_MAX_PROCS=1` it has always aborted promptly — one M, so the moment it finds both
queues empty it is provably alone. **Above one processor it was BIMODAL**, measured on one box, one binary:

| | exit 92 | hung |
|---|---|---|
| `MAXON_MAX_PROCS=1` | 5 of 5, <320 ms | — |
| `MAXON_MAX_PROCS=4` | 6 of 11, 87-313 ms | **5 of 11** — still running at a 10 s cap |

⇒ **it was not "slower to notice", it was a coin flip between noticing in 90 ms and never noticing** — the
flip being purely whether a worker M happened to be spawned at all. The `aloneHere` test that guarded the arm
read `__sched_active_workers`, a count of Ms that have ENTERED the worker loop and not yet left it, which
never falls while the program runs. **A deadlock detector that only fires when there is one processor stops
being a detector on the day the default stops being one processor**, which is the same day this case's
`procs: 4` starts being honoured.

✅ **CLOSED: the arm now asks whether any M is EXECUTING, not whether any M is ALIVE.** `POffQuiesced` is
published by every M that has established there is nothing runnable anywhere, `__sched_progress` is bumped by
every publish so a quiet M that has not yet had its look still counts as live, and the state must be
confirmed `DeadlockConfirmPolls` times before the abort fires. **MEASURED after the fix: exit 92 on 20 of 20
direct runs at `procs: 4`, 327-530 ms**, and 92 at 1, 2, 8 and 16 processors. `SchedRuntime.POffQuiesced`,
`POffQuiescedGen` and `POffQuietPolls` carry the three terms and the false abort each one closes.

⚠ The closure clones the handle it forwards (`m.clone()`): a message parameter arrives BORROWED and a
send MOVES, so the handle crossing into `B.work` has to be one this frame owns — E3138 otherwise, which
is `error.a-borrowed-parameter-may-not-be-sent`'s subject and has nothing to do with this one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias WorkFn = function(B.handle, A.handle) returns Integer

// The indirection. It calls the function it was HANDED, so no roster keyed on a callee's name can say
// which body runs here — which is what keeps `A.ping`'s edge to `B` out of the cycle graph.
function callIndirect(f WorkFn, peer B.handle, mine A.handle) returns Integer
	return f(peer, mine)
end 'callIndirect'

type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function kick(peer B.handle, mine A.handle) returns Integer
		return callIndirect(function(p B.handle, m A.handle) gives (try await p.work(m.clone()) otherwise 0), peer: peer, mine: mine)
	end 'kick'

	export function ack() returns Integer
		return 7
	end 'ack'
end 'A'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function work(back A.handle) returns Integer
		return try await back.ack() otherwise 0
	end 'work'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	let v = try await a.kick(b.clone(), mine: a.clone()) otherwise 0
	return v as ExitCode
end 'main'
```
```exitcode
92
```

<!-- test: deep-acyclic-chain-runs -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Three services in a chain, each awaiting the next. An acyclic graph has a topological order, so the service
lowest in it awaits nobody and always makes progress — which is the induction the whole rule rests on, run.

⚠ `A.value` forwards `last` with a `.clone()`: a message PARAMETER arrives borrowed, and a send MOVES — so the
handle it forwards has to be one this frame owns. The original is dropped by the loop as an un-consumed
payload, which is what shuts `C` down once the chain has answered.
```maxon
type C
	var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'C'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next C.handle) returns Integer
		let v = try await next.value() otherwise 0
		return v + 10
	end 'value'
end 'B'

type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next B.handle, last C.handle) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
31
```

<!-- test: awaitany-returns-the-completed-index -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **ONE WAITING PRIMITIVE COVERS SERVICE REPLIES, FILE IO AND SUBPROCESS DRAINS, AND THIS IS THE HALF THAT
MAKES IT TRUE (SV3).** A reply is an ordinary `Promise`, so it goes into an `Array with Promise with …` and
`__Builtins.awaitAny` selects over it exactly as it does over `async` spawns — no separate "channel select"
and no second waiting mechanism.

⚠ **THE STORAGE MUST NAME THE REPLY'S OWN ERROR TYPE.** A reply ALWAYS carries `ServiceError` (the service can
be gone, whatever the message declares), so `Promise with Integer` alone would erase it; and a message that
DOES throw has a two-member reply error type the storage must spell by its fused name, which is
`a-throwing-reply-stores-in-a-promise-naming-its-errors` below. `Slow.value` throws nothing, so `ServiceError`
is the whole of its reply error type.

⚠ The reply is awaited afterwards. `awaitAny` retires nothing, so the array would otherwise die holding a
live reply cell — `W217`, exit 75; see `specs-shv2/await-any.md`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
		return 5
	end 'value'
end 'Slow'

function main() returns ExitCode
	let h = spawn Slow.create()
	var ps = ReplyPromiseArray.create()
	ps.push(h.value())
	let ready = __Builtins.awaitAny(ps)
	let p = try ps.get(ready) otherwise panic("awaitAny named a slot that is in range")
	let v = try await p otherwise 0
	return (ready + v) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-throwing-reply-stores-in-a-promise-naming-its-errors -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐ **THE FUSED ERROR TYPE HAS A NAME, SO THE ONE REPLY THAT COULD NOT BE STORED NOW CAN BE.** A message that
throws has a reply error type of `{ServiceError, <what the message throws>}`, and that pair is synthesized as
a nominal enum under `<Service>.<method>.errors` — an ordinary declared type a `Promise with (T, E)` can put
in its second argument. The storage road and the direct road then describe the SAME two members, so `e` binds
the same fused flag either way and the bare arms select through one dispatch table.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

typealias Integer = int(i64.min to i64.max)
typealias DivideReply = Promise with (Integer, Calc.divide.errors)
typealias DivideReplyArray = Array with DivideReply

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return try (n / by) otherwise 0
	end 'divide'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	var ps = DivideReplyArray.create()
	ps.push(h.divide(10, by: 0))
	let p = try ps.get(0) otherwise panic("the reply was pushed into slot 0")
	try await p otherwise (e) 'oops'
		match e 'why'
			stopped then return 70 as ExitCode
			divideByZero then return 71 as ExitCode
		end 'why'
	end 'oops'
	return 0 as ExitCode
end 'main'
```
```exitcode
71
```

<!-- test: error.a-stored-reply-that-names-the-wrong-errors-is-refused -->
The two-member type now HAS a name, so the refusal is no longer *"nothing can name it"* — it is an ordinary
name mismatch. `ReplyPromise` names `ServiceError`, which is only the TRANSPORT half; storing a reply from a
throwing message under it would erase the handler member, and the awaiter would then decode a fused
two-member flag as a single enum: a silent wrong `match` arm rather than a diagnostic. So the refusal stays at
the STORE, where both the declared name and the message's own are still known.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function divide(n Integer, by Integer) returns Integer throws MathError
		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'
		return try (n / by) otherwise 0
	end 'divide'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	var ps = ReplyPromiseArray.create()
	ps.push(h.divide(10, by: 2))
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:28:5: 'ReplyPromise' names the error type 'ServiceError', but a reply from 'Calc.divide' throws 'Calc.divide.errors'
```

<!-- test: a-stored-reply-decodes-serviceerror-through-the-storage-road -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **THE CASE THAT SAYS THE TWO ROADS DECODE THE SAME WORD.** A reply awaited DIRECTLY is described by its
message (`TryTarget.serviceReply`); one awaited out of an array is described by its STORAGE TYPE
(`TryTarget.promise`) — two different roads through `caughtErrorFormFor`, reading one error word that the
service wrote already fused. If they disagreed the failure would be silent: a `match e` arm selected by a
table the writer did not build, never a diagnostic.

They agree because a message that throws NOTHING has exactly one reply error member, so both roads answer
`CaughtErrorForm.singleCall` and `e` binds a plain `ServiceError`. This shuts the service down first, so the
send is abandoned at the send and the reply really does carry `stopped`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Slow'

function main() returns ExitCode
	var stops = 0
	let h = spawn Slow.create()
	h.shutdown()
	var ps = ReplyPromiseArray.create()
	ps.push(h.value())
	let ready = __Builtins.awaitAny(ps)
	let p = try ps.get(ready) otherwise panic("awaitAny named a slot that is in range")
	try await p otherwise (e) 'gone'
		match e 'w'
			stopped then stops = stops + 1
		end 'w'
	end 'gone'
	return stops as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-reply-stored-without-its-error-type-is-refused -->
A reply ALWAYS carries `ServiceError` — the service can be gone, whatever the message declares — so a
`Promise with T` storage would erase it and leave a `try await` with nothing to bind `e` at. The refusal
names the MESSAGE rather than *"this promise's function"*: `Slow.value` throws nothing, its REPLY does, and
a sentence blaming the handler would send the author to add a `throws` clause that changes none of this.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BarePromise = Promise with Integer
typealias BarePromiseArray = Array with BarePromise

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Slow'

function main() returns ExitCode
	let h = spawn Slow.create()
	var ps = BarePromiseArray.create()
	ps.push(h.value())
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:21:5: a reply from 'Slow.value' always carries 'ServiceError' — the service can be gone, whatever the message declares — so 'BarePromise' would erase it; declare the storage as 'Promise with (T, ServiceError)' so 'try await' can bind it
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
	var n as Integer

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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3138: <fragment>:14:8: 'buf' arrived as a parameter and cannot be proven unique at the send
```

<!-- test: error.use-after-await-of-a-reply -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **A REPLY IS A PROMISE, SO IT IS MOVE-ONLY TOO — AND IT WAS THE ONE ROAD `W230` DID NOT REACH.** A
spawn's promise is minted at the interned `Promise with (T[, E])`; a reply cell was minted
`ValueTypeTag.integer`, so it stayed the bare machine word W230 exists to abolish. Two consequences,
both measured on this program before the cure: `requireBindingLive`'s scalar early-return swallowed the
consume poison entirely, and the raw handle flowed straight into an INTEGER parameter position with no
`.inner` to unwrap it. It COMPILED and exited **8** — `gtIsComplete` answered 1 off a reclaimed cell.

⚠ **ITS SPAWN TWIN WAS ALREADY E3102**, which is what made this a hole rather than a design: the same
five lines over `async makeValue()` were refused, and over `h.total()` they were not. Linearity (E3100)
cannot stand in for it — a bare rebind consumes nothing, so there is no second consume for that pass to
find.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let p = h.total()
	let a = try await p otherwise 0
	let b = p
	let done = __Builtins.gtIsComplete(b)
	return (a + done) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3142: <fragment>:18:10: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: error.return-a-reply-as-its-result-type -->
A reply is not its result. `grab` is declared `returns Integer` and returns `h.total()`, which is the reply —
the value that will eventually produce an `Integer`, not an `Integer`. Before replies were typed the cell was
minted `ValueTypeTag.integer`, so this COMPILED and printed the green thread's raw cell address (a different
number on every run), which is the wrong answer this pins.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function grab(h Calc.handle) returns Integer
	return h.total()
end 'grab'

function main() returns ExitCode
	let h = spawn Calc.create()
	print("grabbed {grab(h)}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:15:2: Cannot return 'struct' from function declared to return 'int'
```

<!-- test: error.arithmetic-on-a-reply -->
A reply is not a number, so it has no arithmetic. `p + 1` used to be pointer arithmetic on a green-thread cell
address that happened to compile.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let p = h.total()
	let bumped = p + 1
	print("bumped {bumped}")
	return (try await p otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2004: <fragment>:17:17: Cannot operate on struct and int
```

<!-- test: error.a-reply-in-an-integer-parameter -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
An `Integer` parameter does not take a reply. The cure is to `await` it and pass the RESULT — which is also
the only spelling that keeps the cell's one owner intact. A reply always carries an error member, so it
renders as a two-argument `Promise` instance.

⚠ **THIS IS THE ONE REPLY-TYPING REFUSAL THAT CARRIES THE MARKER, AND THE REASON IS *WHO REACHES THE
VERDICT FIRST* — the discriminator this file's Targets section states.** Its four siblings (return,
arithmetic, `clone`, storage) are PARSE throws: the compile stops, so the fragment's only diagnostic is the
one they pin, and they are unmarked and green on every lane. An ARGUMENT type mismatch is not — it is a
whole-program `SemanticCheck` verdict (`argTypeMismatchSentence`), and by the time it is reached
`checkCalls` has already recorded an **E3104** for this program's `spawn`, its `__gt_cell_alloc` and its
`__mbox_send`. MEASURED on `--target=wasm32-wasi`: the E3005 is produced, correctly and last, behind three
E3104 lines. The rule is target-neutral and the x64 lane pins it; what is not target-neutral is the
SCAFFOLDING needed to reach it.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function takesInt(n Integer) returns Integer
	return n
end 'takesInt'

function main() returns ExitCode
	let h = spawn Calc.create()
	return takesInt(h.total()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:20:9: argument type mismatch for 'n': expected 'Integer', got 'Promise with (int(-9223372036854775808 to 9223372036854775807), ServiceError)'
```

<!-- test: error.clone-a-reply -->
⭐ The one that was a latent double-reclaim rather than merely a wrong type. A reply cell is the two-party
teardown rendezvous between the awaiter and the service loop, and exactly one owner may arrive at it;
`p.clone()` used to hand back a second copy of the cell word, with nothing to say which of the two owned it.
`Promise` declares no `clone`, and synthesizing one is refused at the receiver.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let p = h.total()
	let copy = p.clone()
	print("copied {copy.inner > 0}")
	return (try await p otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:17:15: Unsupported: `clone` on `Promise`, which is a GENERIC type — a clone must be minted per INSTANCE (a `Promise with String` and a `Promise with int` copy different things), and this compiler mints one per declared type only, so the copy would alias the type parameter's value instead of cloning it. Write a `clone` method on `Promise` that rebuilds it.
```

<!-- test: a-reply-inner-is-the-one-unwrap -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The sanctioned reply → `int` conversion, and the twin of `promise-typing.inner-is-the-one-unwrap`. `.inner`
peeks at the cell word without consuming the reply, so the `await` that follows still reclaims it and the
program still balances to zero.
```maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let p = h.total()
	let named = p.inner > 0
	print("names a thread {named}")
	return (try await p otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```stdout
names a thread true
```

<!-- test: a-service-shut-down-with-async-work-in-flight -->
<!-- targets: x64-windows, arm64-macos -->
<!-- procs: 4 -->
⭐⭐ **W226's SHAPE, COMMITTED — AND IT IS THE PROOF THAT THE LEAK IT PREDICTED IS NOT THERE.** No case in
this file had a handler use `async` at all, and the `SV1` review that opened `W226` could not measure whether
a coroutine owned by a service green thread is STRANDED when its owner is reclaimed: since `EC10` a coroutine
is published only to its owner's queue and driven only by that owner's chain of drivers, so if `<T>.__loop`
exits with work still on its `coroHead`, nothing drives it and nothing frees it — and a stranded slab
allocation never reaches the exit gate, which is why the row says the shape *"is invisible to every gate the
suite has"*.

⭐ **SO IT WAS MEASURED AGAINST `__Builtins.mmRawAllocLive()` RATHER THAN THE EXIT CODE, AND THE ANSWER IS
NO.** 100 rounds of *spawn a service, send one message whose handler `async`s a 30 ms sleep and NEVER awaits
it, drop the handle* grows the live-allocation count by **400 at `MAXON_MAX_PROCS=1`, 247 at 4 and 204 at
8** — and the CONTROL, the same program whose handler `await`s that coroutine so nothing can be stranded,
grows by **400 at 1 and 491 at 4**. **The two are the same number.** What the counter is reading is a
service's own teardown lagging its handle drop until the exit drain, not a coroutine anybody lost; the
un-awaited coroutine costs nothing the awaited one does not. The exit gate is clean at every processor count,
which is the second half of the same answer.

⇒ **the case stays as the proof.** It pins that a service may be shut down with `async` work in flight and
the process still terminates cleanly — no 101, no 75, no hang — which is the property `W226` was really
asking about, and it is the case that will go red if a later rung gives the loop's exit block a coroutine
sweep it gets wrong.
```maxon
typealias Integer = int(i64.min to i64.max)

function slowWork(n Integer) returns Integer
	sleep(30)
	return n * 2
end 'slowWork'

type Worker
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	// Starts a coroutine on the SERVICE's own green thread and never awaits it. The promise dies at handler
	// scope exit, and the service is shut down by the handle drop below while the sleep is still in flight.
	export function fire(v Integer) returns Integer
		let p = async slowWork(v)
		_ = p
		self.n = self.n + 1
		return self.n
	end 'fire'
end 'Worker'

function main() returns ExitCode
	for round in 1 to 8 'rounds'
		let w = spawn Worker.create()
		w.fire(round)
	end 'rounds'

	print("survived\n")
	return 42 as ExitCode
end 'main'
```
```exitcode
42
```
```stdout
survived
```
