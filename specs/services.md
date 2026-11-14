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
3. **A private helper is never a message.** It is not on the handle, so `self.record(…)` inside a message
   can only ever be a direct call. A message reaches its own service as a handle only through a handle it was
   given.

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
| `Calc.request` | a union — one variant per message in declaration order, the first message at variant 0. A message's payload is its parameters, plus an integer `__reply` slot when it has a reply to deliver. |
| `Calc.handle` | a one-field struct holding the pointer to the service's cown, the record that holds its state, its mailbox and its gate. A real 8-byte box, so moves, E3102, struct fields, arrays and the drop cascade all reach it by construction. |

Whether a type is a service is a **whole-program** property: a `spawn` anywhere makes the type a service
and subjects all its export methods to the service rules. That is why every service diagnostic fires **at
the `spawn`** rather than at the method it names — the method may be in a different file entirely.

### A send MOVES, and that is load-bearing

A `spawn`'s factory arguments, a reply, and every message argument — a `let` or `var` local, a temporary or a
literal — are **moved** across: the far side becomes the value's one owner, and the sending binding is consumed,
so a later read of it is **E3102**, as is handing one binding twice in one send. A scalar is copied. The
handler owns what it was sent: it may keep it or write it, and nothing is written back to the sender. This is not ergonomics: a refcount step on a box one
green thread holds is a plain load/add/store, so a send that put one box into two green threads' hands without
saying so would not be slow, it would corrupt the heap. Every record that crosses is `isolated` or `immutable`, and the static rules decide who owns it: an `isolated` value comes only from clean inputs (E3200), and a binding handed to a send is consumed (E3102). A value that cannot cross at all is refused at the `spawn` as E3217: a `Promise` or any type that can hold one (a handle its awaiter owns, whose coroutine belongs to the strand that started it), a function value (a closure record its copies share), an opaque type parameter (no layout at the send), and a thread-bound handle. A `String`, a number, an enum and a service handle own nothing a second thread could write, so they cross as they are.

A message parameter, a reply or a factory parameter whose type is a record, a union that carries data, a container
or an interface value names how it crosses (**E3217**, specified in `service-message-capabilities`): `isolated` moves a
value the sender built for this one send, so a graph with an owner outside it is refused at the argument, and
`immutable` shares a frozen value that both threads only read.

### A send can fail

A service can be gone and a mailbox can be full, so every send is a throwing call. It fails AT the send, with
the message's own `<Svc>.<msg>.sendError` — `mailboxFull(…)` or `serviceStopped(…)` — which hands the send's
arguments back to the sender. `h.shutdown()` closes the mailbox once it has drained, takes no slot and cannot
fail, and dropping the last handle closes the mailbox the same way; a message that was queued always runs.
A reply's error type is the handler's own and nothing else, so no reply can answer `stopped`.

### A service runs only while it has messages

A service owns no green thread while its queue is empty. A message that reaches an idle service starts a runner, which takes
the queued messages in order, at most 64 per turn, and yields its processor between turns so that every other runnable
service runs before it continues; when the queue is empty the runner ends. A handler that waits (`await`, `sleep`, I/O)
keeps the service held while it waits, and messages queue behind it up to the mailbox's capacity.

### A service's state holds no promise

A field of a service type, after alias resolution and generic instantiation, is not a `Promise`, an array of promises, or a
record or union that holds one: **E3216**, at the `spawn` that makes the type a service. Each message may run on a different
green thread, and a coroutine belongs to the strand that started it, so a promise kept in the state could be awaited or
dropped by a strand that does not own its coroutine. A message or the factory `await`s (or `.cancel()`s) the coroutines it
starts before it returns, and keeps their result in the field.

## Targets

⚠ **NO CASE HERE IS MARKED FOR STARTING A SERVICE, BECAUSE THE COMPILER SAYS SO LOUDER.** A running
service reaches a green thread's context switch; all four native lanes have one, and wasm32-wasi is
refused by `SemanticCheck.requireTargetSupportsServiceEntry`, which the harness reports as a counted SKIP
naming the case. A marker would restate that and take the case out of the run in silence — see
`specs/async-scheduler.md`'s Targets section for the one statement of it.

⛔ **THE REFUSALS THAT DO EXCLUDE wasm32-wasi ARE EXCLUDED FOR THEIR DIAGNOSTIC, NOT FOR THEIR
RULE.** Each is target-neutral and would be reached on every lane, but it is decided after the Maxon tier,
and on wasm the same program is stopped there by E3104 for `__cown_spawn` / `__cown_send`, so the stderr they
pin is not what the compiler emits there. They expire when wasm grows a service substrate, not when the
rule moves — which is why the reason is written here rather than left to the marker.

⚠ **Almost every REFUSAL in this file is unmarked, and that is the same rule read from the other end.** A
verdict the PARSE reaches — a token shape, a declaration, a transferability rule, a move, E3140 — is
target-neutral by construction, so a marker on one would hide a green lane rather than describe a red one.
`spawn-is-not-a-keyword` is unmarked for the neighbouring reason: it RUNS, but it starts no service and
reaches no scheduler at all.

⛔⛔ **THE DISCRIMINATOR IS *WHICH STAGE REACHES THE VERDICT*, NOT *IS THE RULE TARGET-NEUTRAL*.** E3104 is
reported only for a program the Maxon tier otherwise accepts: `checkCalls` files one for every `spawn`, every
send and every reply cell, and they are printed only when no other diagnostic was reported. A verdict from the
parse or from a whole-program `SemanticCheck` pass — **E3221**'s may-wait verdict, **E3100**'s await linearity,
**E3217**'s transferability verdict — therefore prints alone on `--target=wasm32-wasi`, exactly as it does on a native
lane, and its case is unmarked. A ranged-literal **E3005** the parser defers to `InsertRangeChecks` is
decided after lowering, which the E3104s stop, so on wasm it never prints at all. ⇒ **a case whose
refusal is decided after the Maxon tier needs the marker, however target-neutral the rule.**

⚠ **THE TWO E3104 CASES ARE THE EXCEPTION, AND THEY ARE MARKED WITH THE TARGET THEY REFUSE.** A refusal
whose whole subject is *"this target has no substrate for it"* is the one verdict in this file that is not
target-neutral, so it can only be pinned by compiling FOR that target — exactly as
`subprocess-builtins.rejected-on-wasm` is. Without the gate a `spawn` on a lane with no substrate reaches
the backend and PANICS the compiler, where the same substrate reached by `sleep` answers E3104.

## Tests

<!-- test: companions-resolve-as-types -->
A `spawn` makes the type a service, and both synthesized companions are then nameable TYPES — in a
signature written by a function that spawns nothing.

⭐ **IT CAN FAIL.** The program parses to completion and runs, so nothing at the `spawn` stops the parse
before a missing companion is reached.

⛔ **BUT NOT BY AN E3011 AT `serve`'s OWN SIGNATURE.** With the handle mint withheld
(`ServiceCompanions.synthesizeServiceCompanions`'s pass 1 skipped) this case IS red — but as a panic,
`serviceHandleLayout: nothing declares 'type Calc.handle'`, raised at the `spawn` on line 14, which is reached long before `serve`'s signature and which kills the worker and fails
every case in this file with it. The E3011 road is real but belongs to
`companions-come-from-a-spawn-in-another-file`, whose declaring file writes no `spawn` and so has no panic
standing in front of the unresolved name. ⇒ **This
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
	let _ = describe
	let h = spawn Calc.create()
	return serve(h) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: companions-come-from-a-spawn-in-another-file -->
⭐ Whether a type is a service is a WHOLE-PROGRAM property. The file that declares `Calc` and names
`Calc.handle` writes no `spawn` at all; the file that spawns declares nothing. The companions exist
because the two are compiled together — the `Unknown type` a per-file decision would report is what this
case is against.

⭐ **IT IS THE CASE THAT REPORTS AN UNRESOLVED COMPANION.** With the whole-program spawn probe removed from
the pre-fold token walk, this case reports `error E3011: Unknown type 'Calc.handle'`. `calc.maxon` parses
to completion — its `spawn` is in the other file — so the unresolved companion is actually reached.
```maxon
// --- file: calc.maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function serve(_ Calc.handle) returns Integer
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
⭐ The walk that decides which types are services reads EVERY token of every file, and this is the case
that says why it cannot ride the declaration sweep: that sweep consumes a `type` declaration whole and
resumes past its `end`, so a `spawn` in a METHOD BODY would be invisible to an arm written inside it. The
only `spawn` in this program is inside `Runner.start`.

⚠ **IT IS SPLIT ACROSS TWO FILES ON PURPOSE, for `companions-come-from-a-spawn-in-another-file`'s
reason.** `Runner.start`'s `spawn` is the only one in the program, and `calc.maxon` — which parses to
completion and names `Calc.handle` in a signature — is where a walk that skipped method bodies leaves the
companion unresolved.

⚠ `main` REACHES `start`, and `start` calls `serve`, because neither is decoration: a body no path from
`main` reaches is not parsed at all (`skipUnreachedFunctionBody`), and an `export` nothing outside its file
references is E3092. Both would have made this case pass without ever building the spawn it is about.
```maxon
// --- file: calc.maxon
type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

function serve(_ Calc.handle) returns Integer
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
```
```exitcode
1
```

<!-- test: a-spawn-of-a-type-in-another-directory-by-its-qualified-name -->
`Calc` is declared in `sub/`, so `main` names it `sub.Calc`, the spelling a direct call and an `async` accept,
and the spawn starts that type as a service.
```maxon
// --- file: sub/calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn sub.Calc.create()
	let total = try await h.total() otherwise 0
	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-spawn-of-a-nested-directory-type-by-its-qualified-name -->
`Calc` is declared two directories down, in `outer/inner/`, so `main` names it `outer.inner.Calc`, and the
spawn starts that type as a service exactly as it does for a one-segment qualifier.
```maxon
// --- file: outer/inner/calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn outer.inner.Calc.create()
	let total = try await h.total() otherwise 0
	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-default-spawn-of-a-directory-type-by-its-qualified-name -->
The `default` for `Greeter` spawns `Polite`, declared in `sub/`, by its qualified name `sub.Polite`, and
the registry hands `main` that service.
```maxon
// --- file: sub/polite.maxon
export type Polite implements Greeter
	var calls as Integer

	export static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Polite'
// --- file: main.maxon
export interface Greeter
	function greet(mutable self) returns Integer
end 'Greeter'

default Greeter = spawn sub.Polite.create()

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'

export typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-spawn-reaches-a-service-in-a-directory-named-like-a-type -->
`sub/` declares a type `Calc`, and `sub/Calc/` is a directory of its own holding the service `Inner`.
`Calc` declares no member `Inner`, so `sub.Calc.Inner` is the directory chain and the spawn starts
`Inner`, while `sub.Calc.make()` still names the type (8 + 1).
```maxon
// --- file: sub/calc.maxon
export type Calc
	export let v as Integer

	export static function make() returns Calc
		return Calc{v: 1}
	end 'make'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: sub/Calc/inner.maxon
export type Inner
	var count as sub.Integer

	export static function create() returns Self
		return Self{count: 8}
	end 'create'

	export function total() returns sub.Integer
		return self.count
	end 'total'
end 'Inner'
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn sub.Calc.Inner.create()
	let total = try await h.total() otherwise 0
	return (total + sub.Calc.make().v) as ExitCode
end 'main'
```
```exitcode
9
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

<!-- test: error.a-spawn-whose-head-is-a-binding-is-refused-as-a-value -->
A chain whose head is a binding in scope is a value expression, so `spawn sub.Calc.create()` names no
type even though a directory `sub` declares one: the refusal names the value, and no service is
started for the directory's `Calc`.
```maxon
// --- file: sub/calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 7}
	end 'create'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let sub = "a value"
	let h = spawn sub.Calc.create()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:19:10: `spawn sub.Calc.create(…)` does not start a service: `sub` is a value in scope, so `sub.Calc` names no type. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.spawn-of-a-chain-naming-no-type-refused -->
Nothing declares `foo`, so the qualified chain `foo.bar` names no type to be a service.
```maxon
function main() returns ExitCode
	let h = spawn foo.bar.baz()
	return 0
end 'main'
```
```maxoncstderr
error E3134: <fragment>:3:10: `spawn foo.bar.baz(…)` does not start a service: nothing declares a binding, directory or type `foo.bar`. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.a-default-spawn-of-a-chain-naming-nothing-is-refused -->
A `default`'s spawn is read by the same resolver as any other: nothing declares `foo`, so the chain names
no type, and the refusal is the spawn's own.
```maxon
interface Greeter
	function greet() returns Integer
end 'Greeter'

default Greeter = spawn foo.bar.baz()

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3134: <fragment>:6:19: `spawn foo.bar.baz(…)` does not start a service: nothing declares a binding, directory or type `foo.bar`. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.a-default-spawn-whose-head-is-a-top-level-binding-is-refused-as-a-value -->
A top-level `let sub` is in scope at the file's `default`, so `spawn sub.Calc.create()` there is a value
expression and names no type, though the directory `sub` declares a `Calc` that conforms to `Greeter`.
```maxon
// --- file: sub/calc.maxon
export type Calc implements Greeter
	var calls as Integer

	export static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Integer
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Calc'
// --- file: main.maxon
export interface Greeter
	function greet(mutable self) returns Integer
end 'Greeter'

let sub = "a value"

default Greeter = spawn sub.Calc.create()

function main() returns ExitCode
	print(sub)
	let answer = try await Greeter.current().greet() otherwise 0
	return answer as ExitCode
end 'main'

export typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3134: <fragment>:22:19: `spawn sub.Calc.create(…)` does not start a service: `sub` is a value in scope, so `sub.Calc` names no type. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
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
public typealias Integer = int(i64.min to i64.max)
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
public typealias Integer = int(i64.min to i64.max)
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
typealias IntPromise = Promise with Integer

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
public typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3217: <fragment>:17:10: parameter `p` of the message `Calc.hold` is a Promise, which is a green-thread handle its awaiter owns, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-function-value-not-transferable -->
A function value is a reference-counted closure record that every copy of it shares, so a message
parameter of a function type is refused.
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
error E3217: <fragment>:17:10: parameter `op` of the message `Calc.apply` is a function value, a closure record that owns what it captures and does not cross to another thread, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-array-of-promises-not-transferable -->
A promise belongs to the strand that started its coroutine, so no type that can hold one crosses to a service: an
array of promises is refused as a message parameter, however many it holds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseArray = Array with IntPromise

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(ps isolated PromiseArray)
		self.n = self.n + (1 if ps.isEmpty() else 2)
	end 'take'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	var ps = PromiseArray.create(0)
	ps.push(async forever())
	try h.take(ps) otherwise panic("an open mailbox with room takes the send")
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:24:10: parameter `ps` of the message `Sink.take` is a `PromiseArray` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` makes `Sink` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-empty-array-of-promises-not-transferable -->
The type decides, not the value: the same message is refused when the array sent is empty.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseArray = Array with IntPromise

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(ps isolated PromiseArray)
		self.n = self.n + (1 if ps.isEmpty() else 2)
	end 'take'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	let ps = PromiseArray.create(0)
	try h.take(ps) otherwise panic("an open mailbox with room takes the send")
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:19:10: parameter `ps` of the message `Sink.take` is a `PromiseArray` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` makes `Sink` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-param-union-carrying-a-promise-not-transferable -->
A union that has a case carrying a promise is refused as a message parameter even when the value sent is the
case that carries none.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Slot
	idle
	held(p IntPromise)
end 'Slot'

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s isolated Slot)
		match s 'slot'
			idle then self.n = self.n + 1
			held then self.n = self.n + 2
		end 'slot'
	end 'take'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	try h.take(Slot.idle) otherwise panic("an open mailbox with room takes the send")
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:26:10: parameter `s` of the message `Sink.take` is a `Slot` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` makes `Sink` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.message-reply-record-holding-a-promise-not-transferable -->
A reply is handed to the strand that awaits it, so a reply whose type holds a promise is refused as a parameter is.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Holder
	export var held as IntPromise

	static function create() returns Self
		return Self{held: async forever()}
	end 'create'
end 'Holder'

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function make() returns isolated Holder
		return Holder.create()
	end 'make'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:31:10: the message `Sink.make` returns a `Holder` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` makes `Sink` a service — whose reply is handed to the green thread that awaits it. `await` a promise in the handler and return the result it yields, return a value whose graph holds no function value or thread-bound handle, or drop the `returns` clause
```

<!-- test: error.spawn-factory-argument-promise-not-transferable -->
A `spawn`'s factory runs on the service's strand, so a promise handed to it would cross to a strand that does not
own its coroutine.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Sink
	var n as Integer

	static function create(p IntPromise) returns Self
		p.cancel()
		return Self{n: 1}
	end 'create'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create(async forever())
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:24:10: parameter `p` of the factory `Sink.create` is a `IntPromise` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` moves its arguments into the state of `Sink`, a service — which another green thread runs. Pass a value whose graph holds no function value, promise or thread-bound handle, or drop the parameter from the factory
```

<!-- test: error.spawn-factory-argument-record-holding-a-promise-not-transferable -->
A record that holds a promise is refused as a factory argument as the promise itself is.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Holder
	export var held as IntPromise

	static function create() returns Self
		return Self{held: async forever()}
	end 'create'
end 'Holder'

type Sink
	var n as Integer

	static function create(h isolated Holder) returns Self
		let done = h.held.isComplete()
		return Self{n: 1 if done else 0}
	end 'create'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create(Holder.create())
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:32:10: parameter `h` of the factory `Sink.create` is a `Holder` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it, and this `spawn` moves its arguments into the state of `Sink`, a service — which another green thread runs. Pass a value whose graph holds no function value, promise or thread-bound handle, or drop the parameter from the factory
```

<!-- test: services.a-message-may-carry-a-value-at-an-interface-type -->
A value held at an interface type crosses a message whole: its witness names the conformer's own ownership
walk and release, so the service becomes the one owner of the conformer and dispatches through it.
```maxon
interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

type Calc
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.measure(Square.create(3)) otherwise panic("an open mailbox with room takes the send")
	try h.measure(Square.create(2)) otherwise panic("an open mailbox with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("{total}\n")
	h.shutdown()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
13
```

<!-- test: error.message-param-type-parameter-not-transferable -->
A generic service is spawnable, and a message PARAMETER typed at the type parameter is still refused: a send
MOVES its argument, and the send site cannot know from an opaque `T` whether what it is moving is managed at
all. The RETURN road is the asymmetry that makes this a rule rather than a gap — `peek() returns T` is legal
in `a-generic-service-is-supported`, because the value is produced where the descriptor is.
```maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function put(x T)
		self.item = x
	end 'put'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(1)
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:15:10: parameter `x` of the message `Box.put` is an opaque type parameter, whose layout is not known at the send, and this `spawn` makes `Box` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.a-constrained-generic-service-is-refused -->
A CONSTRAINED generic service is refused, and the refusal is a mechanism rather than caution: an instance method of a constrained generic takes one hidden witness pointer per constraint,
and `Box.__behave` is synthesized with no `self` to source one from. Compare
`a-generic-service-is-supported`, whose only difference is the `where`.
```maxon
interface Peekable
	function tag() returns Whole
end 'Peekable'

type Box uses T where T is Peekable
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function peek() returns Whole
		return self.item.tag()
	end 'peek'
end 'Box'

type Coin
	var n as Whole

	static function create(n Whole) returns Self
		return Self{n: n}
	end 'create'

	public function tag() returns Whole
		return self.n
	end 'tag'
end 'Coin'

function main() returns ExitCode
	let h = spawn Box.create(Coin.create(3))
	return 0
end 'main'
public typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:31:10: Unsupported: `spawn Box.create(…)` — `type Box` constrains its type parameters, and an instance method of a constrained generic takes one hidden witness pointer per constraint, which the synthesized `Box.__behave` has no `self` to source. Spawn an UNCONSTRAINED generic or a plain type, or move the constrained work behind a message that takes concrete arguments
```

<!-- test: error.a-generic-factory-that-fixes-no-type-argument-is-refused -->
A `spawn` names its type outright and cannot spell a type argument — the discovery walk reads raw tokens — so
the instantiation is read off the FACTORY'S OWN ARGUMENTS. A factory taking none fixes nothing, and there is
no instance to synthesize a loop against.
```maxon
type Box uses T
	var count as Whole

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function peek() returns Whole
		return self.count
	end 'peek'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create()
	return 0
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: `spawn Box.create(…)` fixes nothing for the type parameter `T` of `type Box` — a `spawn` names the type outright, so its type arguments are read off the factory's own arguments and `create` declares no parameter typed at `T`. Give the factory a parameter of that type, or spawn a type that fixes it
```

<!-- test: error.a-spawn-inside-a-generic-body-is-refused -->
The same rule one level out: a `spawn` inside another generic's body fixes the service's type arguments to
the ENCLOSING declaration's type parameters, which name no layout. `Box.__behave` is synthesized per
instantiation and has no dictionary to read one from.
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

type Outer uses U
	var seed as U

	static function create(seed U) returns Self
		return Self{seed: seed}
	end 'create'

	function go() returns Whole
		let h = spawn Box.create(self.seed)
		return 0
	end 'go'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(1 as Whole)
	return 0
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:22:11: Unsupported: `spawn Box.create(…)` fixes `type Box`'s type arguments to the enclosing declaration's own type parameters, which name no layout — and `Box.__behave` is synthesized per instantiation, so it has no dictionary to read one from. Spawn the service from a body that knows the concrete type
```

<!-- test: a-private-method-is-not-a-message -->
Only `export`/`public` INSTANCE methods are messages. `record` is unmarked and `version` is a static,
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
⭐ A keyword may be a DECLARED NAME, and `FilePath.from` in `stdlib/FilePath.maxon` proves the shape is live corpus:
`public static function from(path String) returns FilePath`. So `spawn Reader.from(3)` must be recognized
as a spawn — both halves of `<Type>.<factory>` go through the same name reader every other declaration
position uses.

⚠ With an `identifier`-only test the program would earn the SHAPE refusal (*"`spawn` is followed by a
STATIC CALL on a type"*), which is a sentence about a program the author did not write. The whole-program
discovery walk reads the same two names, and the two must widen together or one accepts a spawn the other minted no companions for.
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
declarations in this tree depend on it (`specs/associated-types.md` declares
`static function spawn() returns Self`; `stdlib/Subprocess.maxon` declares `Subprocess.spawn`), so a
`TokenKind` would have retokenized both.
```maxon
type Job
	var spawn as Integer

	static function create(n Integer) returns Self
		return Self{spawn: n}
	end 'create'

	function tally() returns Integer
		return self.spawn
	end 'tally'
end 'Job'

type Launcher
	static function spawn(n Integer) returns Job
		return Job.create(n)
	end 'spawn'
end 'Launcher'

function run(spawn Integer) returns Integer
	return spawn + 1
end 'run'

function main() returns ExitCode
	let j = Launcher.spawn(3)
	let spawn = 4
	return (j.tally() + run(spawn) + spawn) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
12
```

<!-- test: a-send-error-is-declared-and-nameable -->
A message's send error is a declared type with a name an author can write: `Store.ping.sendError` in a
`throws` clause, and its cases in a `match`. The mailbox runtime produces it — here a send after `shutdown()`
— and `ping` takes no parameters, so `mailboxFull` and `serviceStopped` carry nothing.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping()
		self.n = self.n + 1
	end 'ping'
end 'Store'

function poke(h Store.handle) throws Store.ping.sendError
	try h.ping()
end 'poke'

function main() returns ExitCode
	let h = spawn Store.create()
	h.shutdown()

	try poke(h) otherwise (e) 'failed'
		match e 'why'
			mailboxFull then return 6
			serviceStopped then return 7
		end 'why'
	end 'failed'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-service-whose-state-is-one-small-immutable-field-shuts-down-cleanly -->
A service whose whole state is one small immutable field answers and then shuts down cleanly.
```maxon
typealias Count = int(0 to 1000)

type A
	let n as Count

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function ping() returns Count
		return self.n
	end 'ping'
end 'A'

function main() returns ExitCode
	let h = spawn A.create()
	let v = try await h.ping() otherwise 0
	print("{v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: spawn-send-and-the-service-runs -->
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
	try h.tick() otherwise panic("an open mailbox with room takes the send")
	try h.tick() otherwise panic("an open mailbox with room takes the send")
	try h.report() otherwise panic("an open mailbox with room takes the send")
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
	try h.push(1) otherwise panic("an open mailbox with room takes the send")
	try h.push(2) otherwise panic("an open mailbox with room takes the send")
	try h.push(3) otherwise panic("an open mailbox with room takes the send")
	try h.read() otherwise panic("an open mailbox with room takes the send")
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

<!-- test: a-long-backlog-of-string-messages-keeps-fifo-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A BACKLOG LONGER THAN ONE TURN'S BATCH, EVERY MESSAGE CARRYING A HEAP `String`.** At one processor with preemption off the
service does not run until `main` parks at the `await`, so all 100 `note` messages are queued at once, more than the 64
a service takes per turn. The handler checks every message against the index FIFO order says it must carry, and the reply
reports the count, any message that arrived out of place, and the first and last three. Exit 0 also says no payload
leaked across the turn boundary (exit 101).
```maxon
typealias Tally = int(0 to 1000)

let backlog = 100 as Tally
let edge = 3

type FifoTape
	var seen as Tally
	var misplaced as Tally
	var trail as String

	static function create() returns Self
		return Self{seen: 0, misplaced: 0, trail: ""}
	end 'create'

	export function note(s String)
		if s != "m{self.seen}" 'outOfPlace'
			self.misplaced = self.misplaced + 1
		end 'outOfPlace'

		if self.seen < edge or self.seen >= backlog - edge 'atAnEdge'
			self.trail.append(" {s}")
		end 'atAnEdge'

		self.seen = self.seen + 1
	end 'note'

	export function report() returns String
		return "seen={self.seen} misplaced={self.misplaced}{self.trail}"
	end 'report'
end 'FifoTape'

function main() returns ExitCode
	let h = spawn FifoTape.create()

	for i in 0 upto backlog 'fill'
		try h.note("m{i}") otherwise panic("an open mailbox with room takes the send")
	end 'fill'

	let summary = try await h.report() otherwise 'gone'
		return 9
	end 'gone'

	print("{summary}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seen=100 misplaced=0 m0 m1 m2 m97 m98 m99
```

<!-- test: the-first-message-to-a-service-that-has-never-run-and-the-queue-behind-it-keep-fifo-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **THE FIRST MESSAGE TO A SERVICE THAT HAS NEVER RUN, AND EVERY MESSAGE BEHIND IT.** At one processor with preemption off
the service has not started when `main` sends `m0` and the 18 messages after it, so all 19 are queued behind the service's
start. The service must answer `m0` first and then the rest in send order. Every message carries a heap `String`, and the
handler checks each against the index FIFO order says it must carry; a first message answered out of turn, answered twice or
dropped shows as `misplaced` or as a short `seen`. Exit 0 also says the first message's `String` was released exactly once
(exit 101).
```maxon
typealias Tally = int(0 to 1000)

let backlog = 19 as Tally
let edge = 2

type InboxTape
	var seen as Tally
	var misplaced as Tally
	var trail as String

	static function create() returns Self
		return Self{seen: 0, misplaced: 0, trail: ""}
	end 'create'

	export function note(s String)
		if s != "m{self.seen}" 'outOfPlace'
			self.misplaced = self.misplaced + 1
		end 'outOfPlace'

		if self.seen < edge or self.seen >= backlog - edge 'atAnEdge'
			self.trail.append(" {s}")
		end 'atAnEdge'

		self.seen = self.seen + 1
	end 'note'

	export function report() returns String
		return "seen={self.seen} misplaced={self.misplaced}{self.trail}"
	end 'report'
end 'InboxTape'

function main() returns ExitCode
	let h = spawn InboxTape.create()

	for i in 0 upto backlog 'fill'
		try h.note("m{i}") otherwise panic("an open mailbox with room takes the send")
	end 'fill'

	let summary = try await h.report() otherwise 'gone'
		return 9
	end 'gone'

	print("{summary}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seen=19 misplaced=0 m0 m1 m17 m18
```

<!-- test: a-send-to-a-shut-down-service-hands-every-argument-back -->
⭐ **A BACKLOG SENT AFTER `shutdown()`, EVERY MESSAGE CARRYING A HEAP `String`.** `shutdown()` closes the
mailbox, so each of 100 `keep` sends is refused with `serviceStopped` and hands its `String` back, in send
order: the handler never runs, no `kept` line prints, and `misplaced` counts any returned `String` that is not
the one that send was given. After every 25th a reply-bearing `value` is awaited, and the one `try` of
`try await` catches the same refusal. Exit 0 says every refused `String` was released (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Tally = int(0 to 1000)

let backlog = 100 as Tally
let replyEvery = 25 as Tally

type Backlog
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Backlog'

function countMisplaced(s String, index Tally) returns Tally
	if s == "s{index}" 'inPlace'
		return 0
	end 'inPlace'

	return 1
end 'countMisplaced'

function main() returns ExitCode
	let h = spawn Backlog.create()
	h.shutdown()

	var refused = 0 as Tally
	var misplaced = 0 as Tally
	var valueRefused = 0 as Tally

	for i in 0 upto backlog 'fill'
		try h.keep("s{i}") otherwise (e) 'keepRefused'
			match e 'keepWhy'
				mailboxFull(s) then panic("a mailbox with room is never full: {s}")
				serviceStopped(s) then misplaced = misplaced + countMisplaced(s, index: i)
			end 'keepWhy'

			refused = refused + 1
		end 'keepRefused'

		if (i + 1) mod replyEvery == 0 'replyHere'
			let v = try await h.value() otherwise (e) 'valueGone'
				match e 'valueWhy'
					mailboxFull then panic("a mailbox with room is never full")
					serviceStopped then valueRefused = valueRefused + 1
				end 'valueWhy'

				continue
			end 'valueGone'

			print("answered {v}\n")
		end 'replyHere'
	end 'fill'

	print("refused={refused} misplaced={misplaced} valueRefused={valueRefused}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
refused=100 misplaced=0 valueRefused=4
```

<!-- test: a-queue-refilled-after-it-drained-keeps-fifo-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A QUEUE THAT DRAINED COMPLETELY AND FILLS AGAIN KEEPS SEND ORDER.** At one processor with preemption off the service runs
only when `main` parks at an `await`. Six `note` messages and the awaited `sync` go through the mailbox first and the service
takes them all, which leaves its queue empty. Eighteen more `note` messages follow, and the order must continue from where
the first batch stopped. Every message carries a heap `String`, and the handler checks each against the index FIFO order says
it must carry; a message lost, repeated or reordered across the drain shows as `misplaced` and a payload dropped or doubled
as a leak or a double free (exit 101).
```maxon
typealias Tally = int(0 to 1000)

let firstBatch = 6 as Tally
let secondBatch = 18 as Tally

type WrapTape
	var seen as Tally
	var misplaced as Tally

	static function create() returns Self
		return Self{seen: 0, misplaced: 0}
	end 'create'

	export function note(s String)
		if s != "m{self.seen}" 'outOfPlace'
			self.misplaced = self.misplaced + 1
		end 'outOfPlace'

		self.seen = self.seen + 1
	end 'note'

	export function sync() returns Tally
		return self.seen
	end 'sync'

	export function report() returns String
		return "seen={self.seen} misplaced={self.misplaced}"
	end 'report'
end 'WrapTape'

function main() returns ExitCode
	let h = spawn WrapTape.create()
	var next = 0 as Tally

	for _ in 0 upto firstBatch 'first'
		try h.note("m{next}") otherwise panic("an open mailbox with room takes the send")
		next = next + 1
	end 'first'

	let drained = try await h.sync() otherwise 'syncFailed'
		return 9
	end 'syncFailed'

	for _ in 0 upto secondBatch 'second'
		try h.note("m{next}") otherwise panic("an open mailbox with room takes the send")
		next = next + 1
	end 'second'

	let summary = try await h.report() otherwise 'reportFailed'
		return 9
	end 'reportFailed'

	print("drained={drained}\n")
	print("{summary}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
drained=6
seen=24 misplaced=0
```

<!-- test: a-long-queue-refilled-after-it-drained-keeps-fifo-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A LONG QUEUE THAT DRAINED COMPLETELY AND FILLS AGAIN KEEPS SEND ORDER.** At one processor with preemption off the service
runs only when `main` parks at an `await`. Forty `note` messages queue first, and the awaited `sync` behind them makes the
service take every one. Forty more follow, longer than one turn's batch of 64 once the first batch is counted again. Each
message carries a heap `String` and the handler checks it against the index FIFO order says it must carry, so an order that
breaks across the drain, or a heap that does (exit 101), shows here.
```maxon
typealias Tally = int(0 to 1000)

let burst = 40 as Tally

type ShrinkTape
	var seen as Tally
	var misplaced as Tally

	static function create() returns Self
		return Self{seen: 0, misplaced: 0}
	end 'create'

	export function note(s String)
		if s != "m{self.seen}" 'outOfPlace'
			self.misplaced = self.misplaced + 1
		end 'outOfPlace'

		self.seen = self.seen + 1
	end 'note'

	export function sync() returns Tally
		return self.seen
	end 'sync'

	export function report() returns String
		return "seen={self.seen} misplaced={self.misplaced}"
	end 'report'
end 'ShrinkTape'

function main() returns ExitCode
	let h = spawn ShrinkTape.create()
	var next = 0 as Tally

	for _ in 0 upto burst 'grow'
		try h.note("m{next}") otherwise panic("an open mailbox with room takes the send")
		next = next + 1
	end 'grow'

	let drained = try await h.sync() otherwise 'syncFailed'
		return 9
	end 'syncFailed'

	for _ in 0 upto burst 'again'
		try h.note("m{next}") otherwise panic("an open mailbox with room takes the send")
		next = next + 1
	end 'again'

	let summary = try await h.report() otherwise 'reportFailed'
		return 9
	end 'reportFailed'

	print("drained={drained}\n")
	print("{summary}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
drained=40
seen=80 misplaced=0
```

<!-- test: two-instances-are-independent -->
Two spawns of one type are two services with two states.

⚠ **THE ORDER OF THE TWO PRINTS IS FORCED BY CAUSALITY AND NOT BY LUCK.** `b` prints its own count and then
sends `report` to `a`, so `a`'s line cannot be written until `b`'s handler has already written its own — two
services printing on two green threads with nothing between them would be ordered by the scheduler. Sending
`a`'s handle to `b`'s handler is what buys that ordering; `borrow.a-sent-handle-may-be-kept-by-the-handler`
pins a sent handle on its own.
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
		try peer.report() otherwise panic("an open mailbox with room takes the send")
	end 'reportThen'
end 'Counter'

function main() returns ExitCode
	let a = spawn Counter.create(1)
	let b = spawn Counter.create(2)
	try a.tick() otherwise panic("an open mailbox with room takes the send")
	try a.tick() otherwise panic("an open mailbox with room takes the send")
	try b.tick() otherwise panic("an open mailbox with room takes the send")
	try b.reportThen(a) otherwise panic("an open mailbox with room takes the send")
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
	let direct = Calc.create()
	direct.bump(4)
	direct.total()

	let h = spawn Calc.create()
	try h.bump(3) otherwise panic("an open mailbox with room takes the send")
	try h.total() otherwise panic("an open mailbox with room takes the send")
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
A private helper is not on the handle, so a call to it through the handle is not a message.
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
error E3136: <fragment>:16:4: `record` is declared on `type Calc` but is not a message: only its `export` INSTANCE methods are on `Calc.handle`. A handle reaches exactly those, so a private helper is reached by a DIRECT call, from inside a message body or from a `Calc` value. Export it to make it a message, or call it on a `Calc` value
```

<!-- test: shutdown-drains-what-is-queued -->
`shutdown()` is a graceful drain, not a kill: it closes the mailbox once everything already queued has run, so
every message sent before it still runs.

⚠ `shutdown` is the handle's own method and is not a message of `Log`. It follows `clone`'s precedent
exactly (`Parser.structCloneIsSynthesized`): a service that declares an `export function shutdown()` of its
own WINS, and the compiler's close is then unspellable for it — which leaves dropping the last handle, the
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
	try h.push(1) otherwise panic("an open mailbox with room takes the send")
	try h.push(2) otherwise panic("an open mailbox with room takes the send")
	try h.read() otherwise panic("an open mailbox with room takes the send")
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

<!-- test: a-let-sent-after-shutdown-is-handed-back-and-released -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐⭐ **A `let` THE REFUSED SEND MOVED COMES BACK, AND IS RELEASED EXACTLY ONCE.** The second `keep` is sent
after `shutdown()`, so the mailbox is closed and the send is refused with `serviceStopped`, handing back the
`String` the `let` moved. The first `keep`, queued before the shutdown, still runs at the exit drain. Exit 0
says the returned `String` was released once and the queued one by the service (exit 101 otherwise).
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
	try h.keep(first) otherwise panic("an open mailbox with room takes the send")
	h.shutdown()
	let second = "b{2}"

	try h.keep(second) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("refused {s}\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
refused b2
kept a1
```

### A bounded mailbox, and a send that fails

A mailbox holds at most its capacity, `DefaultMailboxCapacity` (1024) unless `spawn(capacity: n)` names one.
Every send throws at the send when the mailbox is full or the service has stopped, with the message's own
`<Svc>.<msg>.sendError`: `mailboxFull(…)` and `serviceStopped(…)`, each carrying the arguments the send was given.

<!-- test: a-full-mailbox-refuses-a-send-and-hands-its-arguments-back -->
<!-- procs: 1 -->
<!-- preempt: off -->
At one processor with preemption off the service runs nothing until `main` returns, so the first two sends
fill a capacity of two and the third is refused with `mailboxFull`, handing back the `String` it was given. The
two queued messages still run at the exit drain, and exit 7 says the refused `String` was released.
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
	let h = spawn(capacity: 2) Store.create()

	for i in 0 upto 3 'fill'
		try h.keep("s{i}") otherwise (e) 'refused'
			match e 'why'
				mailboxFull(s) then print("full {s}\n")
				serviceStopped(s) then print("stopped {s}\n")
			end 'why'

			return 7
		end 'refused'
	end 'fill'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```stdout
full s2
kept s0
kept s1
```

<!-- test: a-handed-back-argument-can-be-resent -->
<!-- procs: 1 -->
<!-- preempt: off -->
The `String` a refused send hands back is the caller's own, so it can be sent again. A capacity of one
refuses the second send of `h`; the arm hands the returned `String` to `other`, which has room, and a
retry loop reassigns a `var` from the returned value and sends it until it lands. At one processor the
retry cannot land without `Scheduler.yield()` letting `h`'s service drain its queue, and the loop prints
once per refusal, so the output shows it took exactly one yield. Exit 0 says every `String` was released.
```maxon
type Store
	var tag as Integer

	static function create(tag Integer) returns Self
		return Self{tag: tag}
	end 'create'

	export function keep(s String)
		print("store{self.tag} kept {s}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create(1)
	let other = spawn Store.create(2)

	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	try h.keep("s{1}") otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then try other.keep(back) otherwise panic("a service with room takes the send")
			serviceStopped then return 1
		end 'why'
	end 'refused'

	var pending = "s{2}"

	while true 'untilItLands'
		try h.keep(pending) otherwise (e) 'refusedAgain'
			match e 'again'
				mailboxFull(back) then pending = back
				serviceStopped then return 2
			end 'again'

			print("full, yielding\n")
			Scheduler.yield()
			continue
		end 'refusedAgain'

		break
	end 'untilItLands'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full, yielding
store2 kept s1
store1 kept s0
store1 kept s2
```

<!-- test: a-send-after-shutdown-fails-stopped-with-its-arguments -->
<!-- procs: 1 -->
<!-- preempt: off -->
A send after `shutdown()` is refused with `serviceStopped` and gets its `String` back; the message sent before the
shutdown still runs.
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
	try h.keep("a{1}") otherwise panic("an open mailbox with room takes the send")
	h.shutdown()

	try h.keep("b{2}") otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
stopped b2
kept a1
```

<!-- test: a-reply-bearing-send-to-a-full-mailbox-fails-at-the-send -->
<!-- procs: 1 -->
<!-- preempt: off -->
A reply-bearing send fails at the send too, and the one `try` of `try await` catches it: `value` takes no
parameters, so its `mailboxFull` and `serviceStopped` carry nothing.
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

	export function value() returns Integer
		return self.n
	end 'value'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.keep("a{1}") otherwise panic("a capacity of one takes the first send")

	let v = try await h.value() otherwise (e) 'refused'
		match e 'why'
			mailboxFull then print("full\n")
			serviceStopped then print("stopped\n")
		end 'why'

		return 0
	end 'refused'

	print("value={v}\n")
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full
kept a1
```

<!-- test: a-stored-reply-send-needs-its-own-try -->
A reply that is stored rather than awaited takes its own `try` for the send, and the promise it yields
carries only the handler's errors: `Promise with (Integer, StoreError)`, matched exhaustively by `empty`.
```maxon
enum StoreError implements Error
	empty
end 'StoreError'

typealias ValuePromise = Promise with (Integer, StoreError)
typealias ValuePromiseArray = Array with ValuePromise

type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		self.n = self.n + by
	end 'add'

	export function value() returns Integer throws StoreError
		if self.n == 0 'none'
			throw StoreError.empty
		end 'none'

		return self.n
	end 'value'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let ps = ValuePromiseArray.create(0)
	ps.push(try h.value() otherwise panic("an open mailbox with room takes the send"))
	try h.add(5) otherwise panic("an open mailbox with room takes the send")
	ps.push(try h.value() otherwise panic("an open mailbox with room takes the send"))

	while ps.count() > 0 'drain'
		let p = try ps.remove(0) otherwise panic("the loop runs while a promise is left")

		let v = try await p otherwise (e) 'failed'
			match e 'why'
				empty then print("empty\n")
			end 'why'

			continue
		end 'failed'

		print("value={v}\n")
	end 'drain'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
empty
value=5
```

<!-- test: error.a-bare-send-requires-try -->
A send can fail, so a send with no `try` is the ordinary refusal of a throwing call made without one.
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
	h.keep("a{1}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3057: <fragment>:17:2: throwing function requires try: 'Store.keep'
```

<!-- test: error.a-reply-bearing-message-sent-as-a-statement-is-refused -->
A message that returns or throws sent as a statement drops its reply, and with it the value or the
handler's error. The send's own error is handled here, so the drop is the only fault: await the reply,
store it, or discard it with `_ =`.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.value() otherwise ignore
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3190: <fragment>:16:2: `Store.value` returns or throws, so a send of it delivers a reply, and as a statement the reply is dropped — and with it the value and any error the handler throws. Await it with `try await <handle>.<message>(…)`, keep it with `let reply = try <handle>.<message>(…)`, or discard it on purpose with `_ = try <handle>.<message>(…)`
```

<!-- test: shutdown-of-a-full-mailbox-runs-what-is-queued -->
<!-- procs: 1 -->
<!-- preempt: off -->
`shutdown()` takes no slot, so it succeeds on a full mailbox: both queued messages still run, and the send
after it is refused with `serviceStopped`.
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
	let h = spawn(capacity: 2) Store.create()
	try h.keep("a{1}") otherwise panic("a capacity of two takes the first send")
	try h.keep("b{2}") otherwise panic("a capacity of two takes the second send")
	h.shutdown()

	try h.keep("c{3}") otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
stopped c3
kept a1
kept b2
```

<!-- test: the-default-capacity-queues-1024-messages -->
<!-- procs: 1 -->
<!-- preempt: off -->
A bare `spawn` takes `DefaultMailboxCapacity`: 1024 sends queue before the service runs, the 1025th is
refused with `mailboxFull`, and every queued message runs at the exit drain.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Tally = int(0 to 1025)

let attempts = 1025 as Tally
let lastQueued = 1024

type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1

		if self.n == lastQueued 'last'
			print("ran {self.n} last={s}\n")
		end 'last'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var queued = 0 as Tally

	for i in 0 upto attempts 'fill'
		try h.keep("s{i}") otherwise (e) 'refused'
			match e 'why'
				mailboxFull(s) then print("queued={queued} full {s}\n")
				serviceStopped(s) then print("queued={queued} stopped {s}\n")
			end 'why'

			return 0
		end 'refused'

		queued = queued + 1
	end 'fill'

	print("queued={queued} nothing refused\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
queued=1024 full s1024
ran 1024 last=s1023
```

<!-- test: a-propagated-send-error-carries-its-arguments -->
<!-- procs: 1 -->
<!-- preempt: off -->
A bare `try` propagates the send's error through a function declared to throw `Store.keep.sendError`, and
the caller that catches it gets the `String` back.
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

function stash(h Store.handle, tag Integer) throws Store.keep.sendError
	try h.keep("s{tag}")
end 'stash'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try stash(h, tag: 1) otherwise panic("a capacity of one takes the first send")

	try stash(h, tag: 2) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full s2
kept s1
```

<!-- test: a-shared-argument-handed-back-leaves-the-callers-value-intact -->
<!-- procs: 1 -->
<!-- preempt: off -->
A read-only parameter is shared with the service rather than moved into the message, and a refused send hands
back a copy of it: the `Wrap` that `offer` receives is its own, so it is sent on to `other` as it stands. The
original, which the `main` binding `first` still reads, is intact afterwards. Exit 0 says every reference was
released, and `other` printing the text twice says the two handed-back values carried it.
```maxon
union Wrap
	held(text String)
	empty
end 'Wrap'

type Store
	var n as Integer
	var tag as Integer

	static function create(tag Integer) returns Self
		return Self{n: 0, tag: tag}
	end 'create'

	export function keep(w immutable Wrap)
		match w 'm'
			held(t) then self.take(t)
			empty then self.n = self.n + 100
		end 'm'
	end 'keep'

	function take(t String)
		self.n = self.n + 1

		if self.tag == 2 'loud'
			print("other kept {t}\n")
		end 'loud'
	end 'take'

	export function count() returns Integer
		return self.n
	end 'count'
end 'Store'

function describe(w Wrap)
	match w 'again'
		held(t) then print("still holds {t}\n")
		empty then print("empty\n")
	end 'again'
end 'describe'

function offer(h Store.handle, other Store.handle, w immutable Wrap)
	try h.keep(w) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then try other.keep(back.clone()) otherwise panic("a service with room takes the send")
			serviceStopped then panic("the service is running")
		end 'why'
	end 'refused'
end 'offer'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create(1)
	let other = spawn Store.create(2)
	let first = Wrap.held("first{1}")

	for _ in 0 upto 3 'many'
		offer(h, other: other, w: first)
	end 'many'

	let total = try await other.count() otherwise panic("a running service answers")
	print("other took {total}\n")
	describe(first)

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
other kept first1
other kept first1
other took 2
still holds first1
```

<!-- test: a-propagated-send-error-hands-a-shared-argument-back-to-be-resent -->
<!-- procs: 1 -->
<!-- preempt: off -->
`offer` shares its read-only parameter with the service and propagates the refusal with a bare `try`, through a
function declared to throw `Store.keep.sendError`. Whoever catches the error owns the arguments in it, so `main`
sends the handed-back `Wrap` to `other`, while the `first` it passed in is intact. Exit 0 says every reference
was released.
```maxon
union Wrap
	held(text String)
	empty
end 'Wrap'

type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(w immutable Wrap)
		match w 'm'
			held(t) then print("kept {t}\n")
			empty then self.n = self.n + 100
		end 'm'
	end 'keep'
end 'Store'

function describe(w Wrap)
	match w 'again'
		held(t) then print("still holds {t}\n")
		empty then print("empty\n")
	end 'again'
end 'describe'

function offer(h Store.handle, w immutable Wrap) throws Store.keep.sendError
	try h.keep(w)
end 'offer'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	let other = spawn Store.create()
	let first = Wrap.held("first{1}")

	try offer(h, w: first) otherwise panic("a capacity of one takes the first send")

	try offer(h, w: first) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(back) then try other.keep(back.clone()) otherwise panic("a service with room takes the send")
			serviceStopped then return 1
		end 'why'
	end 'refused'

	describe(first)

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
still holds first1
kept first1
kept first1
```

<!-- test: a-shared-value-that-cannot-be-cloned-is-handed-back-retained -->
<!-- procs: 1 -->
<!-- preempt: off -->
`Port` is a `Shape` that owns a listening socket, which cannot be duplicated, so no `Holder` — whose field is
held at `Shape` — can be deep-cloned, though the `Holder` this program builds holds a `Plain` and walks to the
service without trouble. A refused send therefore cannot hand back a copy of the read-only `Holder` that `offer`
shares: the send-error slot holds the very `Holder` the caller passed, and the binding in the arm retains it
rather than taking it over. The handed-back `back` is usable, and the `Holder` that `main` still names is intact.
Exit 0 says every reference was released.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Port implements Shape
	var listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'Port'

type Holder
	let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'

	function measure() returns Tally
		return self.shape.area()
	end 'measure'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(h immutable Holder)
		self.seen = self.seen + h.measure()
	end 'look'
end 'Door'

function offer(h Door.handle, held immutable Holder)
	try h.look(held) otherwise (e) 'refused'
		match e 'why'
			serviceStopped(back) then print("stopped, handed back a holder measuring {back.measure()}\n")
			mailboxFull then panic("a stopped service is not full")
		end 'why'
	end 'refused'
end 'offer'

function main() returns ExitCode
	let _ = sizeof(Port)
	let h = spawn Door.create()
	h.shutdown()

	let holder = freeze Holder.create(Plain.create())
	offer(h, held: holder)
	print("the original measures {holder.measure()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
stopped, handed back a holder measuring 1
the original measures 1
```

<!-- test: a-caught-send-error-can-be-thrown-on -->
<!-- procs: 1 -->
<!-- preempt: off -->
The binding that caught a send's error owns it alone, so `throw e` hands it on to the caller, which gets the
`String` back.
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

function relay(h Store.handle, tag Integer) throws Store.keep.sendError
	try h.keep("s{tag}") otherwise (e) 'refused'
		throw e
	end 'refused'
end 'relay'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try relay(h, tag: 1) otherwise panic("a capacity of one takes the first send")

	try relay(h, tag: 2) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full s2
kept s1
```

<!-- test: error.a-throws-clause-over-a-send-whose-error-is-handled-never-throws -->
A send's error caught with `otherwise` leaves nothing for the declared clause to carry, so the clause is
refused as one the body can never throw.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping()
		self.n = self.n + 1
	end 'ping'
end 'Store'

function poke(h Store.handle) throws Store.ping.sendError
	try h.ping() otherwise return
end 'poke'

function main() returns ExitCode
	let h = spawn Store.create()
	try poke(h) otherwise return 1
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3168: <fragment>:14:10: 'throws Store.ping.sendError' is declared but nothing in the body throws — no 'throw', no bare 'try' and no 'otherwise throw' reaches a caller. Remove the clause
```

<!-- test: a-send-error-may-be-thrown-from-a-value-the-function-was-handed -->
A send-error a function is handed may be thrown by it: the box the send failed on is counted, so the catcher and
the function each hold a counted reference to it.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + (s.byteLength() as Integer)
	end 'keep'
end 'Store'

function again(e Store.keep.sendError) throws Store.keep.sendError
	throw e
end 'again'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.keep("a") otherwise panic("a capacity of one takes the first send")

	try h.keep("b") otherwise (e) 'refused'
		try again(e) otherwise ignore
	end 'refused'

	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: error.a-capacity-out-of-range-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A capacity is a `MailboxCapacity`, `int(1 to 2^31)`, so a literal outside it is the ordinary ranged-literal
refusal.
wasm32-wasi has no services, so it answers `E3104` for the `spawn` and the sends before it reaches this refusal; the case is pinned on the native lanes.
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
	let h = spawn(capacity: 0) Store.create()
	try h.keep("a{1}") otherwise panic("a mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:16:26: Value 0 is outside the range of 'MailboxCapacity' (int(1 to 2147483648))
```

<!-- test: a-capacity-that-is-not-a-power-of-two-fills-drains-and-fills-again-in-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
A capacity of 20 is not a power of two, so 20 sends queue ahead of the service and the 21st is refused, whether or
not the service has already run before the round starts. `Scheduler.yield()` lets the service run them all, which
empties the queue, and the second round fills it again. The service logs the numbers in the order it receives them,
so the output pins FIFO across the fill, the drain and the refill.
```maxon
typealias Tally = int(0 to u64.max)

let attempts = 22 as Tally

type Ledger
	var taken as Tally
	var log as String

	static function create() returns Self
		return Self{taken: 0, log: ""}
	end 'create'

	export function take(n Tally)
		self.taken = self.taken + 1
		self.log.append(" {n}")
	end 'take'

	export function finish() returns Tally
		print("order:{self.log}\n")

		return self.taken
	end 'finish'
end 'Ledger'

function fill(h Ledger.handle, base Tally) returns Tally
	for i in 0 upto attempts 'innerFill'
		try h.take(base + i) otherwise (e) 'refused'
			match e 'why'
				mailboxFull then print("refused after {i} sends\n")
				serviceStopped then panic("the service is running")
			end 'why'

			return i
		end 'refused'
	end 'innerFill'

	panic("a capacity of 20 took 22 sends")
end 'fill'

function main() returns ExitCode
	let h = spawn(capacity: 20) Ledger.create()
	Scheduler.yield()
	let first = fill(h, base: 0)
	Scheduler.yield()
	let second = fill(h, base: first)
	Scheduler.yield()
	let taken = try await h.finish() otherwise panic("a running service answers")
	print("sent {first} then {second}, taken={taken}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
refused after 20 sends
refused after 20 sends
order: 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39
sent 20 then 20, taken=40
```

<!-- test: a-trace-capture-stop-waits-for-room-in-a-full-recorder-mailbox -->
<!-- procs: 1 -->
<!-- preempt: off -->
`TraceCapture` records through a service whose mailbox holds 1024 messages, and `stopCapture()` is a barrier
sent to it. The yield parks the recorder on its empty mailbox first, so `startCapture()`'s reset is handed
straight to it and the 1024 trace keys that follow fill the mailbox to exactly its capacity, with the recorder
not yet run. The barrier is then refused as `mailboxFull`; it yields until the recorder has drained and sends
again, rather than treating the refusal as a stopped recorder. Every key reaches the capture.
```maxon
let traces = 1024

function main() returns ExitCode
	Scheduler.yield()
	TraceCapture.startCapture()

	for _ in 0 upto traces 'emit'
		TraceCapture.trace("k")
	end 'emit'

	let keys = TraceCapture.stopCapture()
	print("keys={keys.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
keys=1024
```

<!-- test: a-send-with-a-timeout-waits-for-room -->
<!-- procs: 1 -->
<!-- preempt: off -->
A send given `sendTimeout:` waits for room instead of failing at once. The first send fills a capacity of one
before the service has run, so the second finds the mailbox full and parks `main` on the mailbox's sender queue.
At one processor with preemption off that park is the only way the service runs. Its receive takes `s0` and,
finding `main` queued, moves `main`'s `s1` into the freed slot and readies `main`, as Go's receive moves a
parked sender's element into the buffer. The send is already delivered when `main` runs again. The service
keeps the processor until it parks, so it handles `s0` and then `s1` before `main` prints.
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
	let h = spawn(capacity: 1) Store.create()
	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	try h.keep("s{1}", sendTimeout: 5000) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 1
	end 'refused'

	print("sent s1\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept s0
kept s1
sent s1
```

<!-- test: a-waiting-sender-of-a-capacity-one-mailbox-is-admitted-behind-what-was-queued -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A WAITING SENDER IS ADMITTED BEHIND WHAT WAS QUEUED WHEN THE SERVICE STARTS ITS FIRST MESSAGE.** The first send fills a
capacity of one while the service has never run, so `s0` is queued and nothing is behind it. The second send, given
`sendTimeout:`, finds the mailbox full and parks `main` on the sender queue. When the service starts `s0` it frees the slot,
which goes to `main`'s `s1`: `s1` is queued behind `s0` and `main` is readied. The service then runs `s0` and `s1` in that
order. A third send after `main` wakes, a yield that lets the service start it (a queued message holds its slot until the
handler starts) and an awaited `count` behind it show the mailbox keeps working afterwards: `s2` is run before the count
is read.
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

	export function count() returns Integer
		return self.n
	end 'count'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	try h.keep("s{1}", sendTimeout: 5000) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 1
	end 'refused'

	print("sent s1\n")
	try h.keep("s{2}") otherwise panic("the service has drained its queue, so a capacity of one takes the send")
	Scheduler.yield()

	let n = try await h.count() otherwise 'gone'
		return 9
	end 'gone'

	print("count={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept s0
kept s1
sent s1
kept s2
count=3
```

<!-- test: a-waiting-sender-of-a-capacity-seventeen-mailbox-is-admitted-in-fifo-order -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A WAITING SENDER BEHIND A FULL QUEUE IS ADMITTED LAST.** A capacity of 17 takes 17 messages while the service has not
run: `m0` to `m16` fill the mailbox exactly. The eighteenth, given `sendTimeout:`, finds the mailbox full and parks `main`.
When the service starts `m0` the slot it frees goes to the waiting `m17`, which is queued behind `m16`. The handler checks
each message against the index FIFO order says it must carry, and every message carries a heap `String`, so a sender admitted
ahead of an older message, or a message lost on the way in, shows as `misplaced` or as a heap error (exit 101).
```maxon
typealias Tally = int(0 to 1000)

let queued = 17 as Tally

type FullRingTape
	var seen as Tally
	var misplaced as Tally

	static function create() returns Self
		return Self{seen: 0, misplaced: 0}
	end 'create'

	export function note(s String)
		if s != "m{self.seen}" 'outOfPlace'
			self.misplaced = self.misplaced + 1
		end 'outOfPlace'

		self.seen = self.seen + 1
	end 'note'

	export function report() returns String
		return "seen={self.seen} misplaced={self.misplaced}"
	end 'report'
end 'FullRingTape'

function main() returns ExitCode
	let h = spawn(capacity: 17) FullRingTape.create()

	for i in 0 upto queued 'fill'
		try h.note("m{i}") otherwise panic("a capacity of 17 takes the first 17 sends")
	end 'fill'

	try h.note("m{queued}", sendTimeout: 5000) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 1
	end 'refused'

	let summary = try await h.report() otherwise 'gone'
		return 9
	end 'gone'

	print("{summary}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seen=18 misplaced=0
```

<!-- test: shutdown-before-the-service-runs-fails-a-waiting-sender-and-still-runs-the-queued-message -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐ **A WAITING SENDER BEHIND THE QUEUED MESSAGE OF A SERVICE THAT HAS NEVER RUN, AND A `shutdown()` BEFORE IT DOES.** The waiting
green thread cannot shut the service down itself, so a second service, `Closer`, does, and it is spawned after `Store` so
that it runs before it. `s0` fills `Store`'s one slot while `Store` has never run, so it is queued. `close` goes to `Closer`,
and `s1`, given `sendTimeout:`, finds the mailbox full and parks `main` on the sender queue. Its wait is what lets `Closer`
run: `Closer` shuts `Store` down while `s0` is still queued and `s1` still waits. The waiting send fails `serviceStopped`
with its `String`, and `Store`, which first runs after the shutdown, still runs `s0`.
```maxon
let longTimeoutMs = 60000

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

type Closer
	static function create() returns Self
		return Self{}
	end 'create'

	export function close(target Store.handle)
		print("closing\n")
		target.shutdown()
	end 'close'
end 'Closer'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	let closer = spawn Closer.create()
	try h.keep("s{0}") otherwise panic("the service has never run, so its one slot is free")
	try closer.close(h.clone()) otherwise panic("an open mailbox with room takes the send")

	try h.keep("s{1}", sendTimeout: longTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 0
	end 'refused'

	print("sent s1\n")
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
closing
stopped s1
kept s0
```

<!-- test: a-parameterless-message-takes-only-a-named-send-timeout -->
<!-- procs: 1 -->
<!-- preempt: off -->
`sendTimeout:` is optional, so it is named wherever it appears, even as a call's only argument: a message with
no parameters takes `h.ping(sendTimeout: 5000)`. The shape is `a-send-with-a-timeout-waits-for-room`'s: the
second ping parks until the service's receive takes the first and moves the second into the freed slot. The
service then runs both pings before `main` prints.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping()
		self.n = self.n + 1
		print("ping {self.n}\n")
	end 'ping'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.ping() otherwise panic("a capacity of one takes the first send")

	try h.ping(sendTimeout: 5000) otherwise (e) 'refused'
		match e 'why'
			mailboxFull then print("full\n")
			serviceStopped then print("stopped\n")
		end 'why'

		return 1
	end 'refused'

	print("sent the second ping\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
ping 1
ping 2
sent the second ping
```

<!-- test: a-message-with-a-defaulted-first-parameter-may-name-it -->
```maxon
type Meter
	var n as Integer

	static function create(start Integer = 0) returns Self
		return Self{n: start}
	end 'create'

	export function bump(by Integer = 1)
		self.n = self.n + by
	end 'bump'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create(start: 5)
	try h.bump() otherwise panic("an open mailbox with room takes the send")
	try h.bump(by: 2) otherwise panic("an open mailbox with room takes the send")
	try h.bump(by: 3, sendTimeout: 1000) otherwise panic("an open mailbox with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("total={total}\n")
	h.shutdown()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
total=11
```

<!-- test: a-message-with-a-defaulted-first-parameter-takes-it-positionally -->
```maxon
type Meter
	var n as Integer

	static function create(start Integer = 0) returns Self
		return Self{n: start}
	end 'create'

	export function bump(by Integer = 1)
		self.n = self.n + by
	end 'bump'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create(5)
	try h.bump(2) otherwise panic("an open mailbox with room takes the send")
	try h.bump(3, sendTimeout: 1000) otherwise panic("an open mailbox with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("total={total}\n")
	h.shutdown()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
total=10
```

<!-- test: an-awaited-send-with-a-timeout-waits-for-room -->
<!-- procs: 1 -->
<!-- preempt: off -->
`try await` takes `sendTimeout:` too. The send waits for the service to take `s0`, as in
`a-send-with-a-timeout-waits-for-room`, and the one `try` then awaits the reply.
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

	export function tally(s String) returns Integer
		self.n = self.n + 1
		print("tallied {s}\n")

		return self.n
	end 'tally'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	let n = try await h.tally("s{1}", sendTimeout: 5000) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 1
	end 'refused'

	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept s0
tallied s1
n=2
```

<!-- test: a-send-whose-timeout-expires-fails-mailbox-full -->
<!-- procs: 1 -->
<!-- preempt: off -->
A send whose timeout expires before room appears fails `mailboxFull` and hands its `String` back. The yield
lets the service take `hold`, whose handler returns only once `main` publishes the release, so nothing drains
the mailbox while `main` waits: `s0` fills the one slot and `s1` waits out its `timeoutMs`. The wait is measured
with `Clock` from below only: at least the timeout. That the handler cannot have returned is what says the timer
and not a drain ended it, with no bound on how long the timer takes to fire. `main` releases the handler after the
refusal, and both queued messages run at the exit drain.
```maxon
typealias ReleaseCell = SharedValue with bool

let timeoutMs = 20
let releasePollMs = 1

type Store
	let released as ReleaseCell
	var n as Integer

	static function create(released ReleaseCell) returns Self
		return Self{released: released, n: 0}
	end 'create'

	export function hold()
		while not self.released.current() 'untilReleased'
			sleep(releasePollMs)
		end 'untilReleased'

		print("held\n")
	end 'hold'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let h = spawn(capacity: 1) Store.create(released.clone())
	try h.hold() otherwise panic("an empty mailbox takes the first send")
	Scheduler.yield()
	try h.keep("s{0}") otherwise panic("the service took hold, so its one slot is free")
	let start = Clock.nowMs()

	try h.keep("s{1}", sendTimeout: timeoutMs) otherwise (e) 'refused'
		let waited = Clock.elapsedMs(start)

		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		print("waited out the timeout: {waited >= timeoutMs}\n")
		released.publish(true)
		return 0
	end 'refused'

	print("sent s1\n")
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full s1
waited out the timeout: true
held
kept s0
```

<!-- test: shutdown-wakes-a-sender-waiting-for-room -->
<!-- procs: 1 -->
<!-- preempt: off -->
`shutdown()` wakes a send waiting for room, and the send fails `serviceStopped` with its `String`. The waiting
green thread cannot shut the service down itself, so a second service, `Closer`, does. The yield lets `Store`
take `hold` and wait, and lets `Closer` park on its empty mailbox. `s0` then fills `Store`'s one slot,
`close` goes to `Closer`, and `s1` waits with a timeout far longer than the case runs. Its wait is what lets
`Closer` run. `hold` returns only once `main` publishes the release after the refusal, so nothing but the
shutdown can have ended the wait, however slow the host is. `s0` still runs after the shutdown, once `hold`
returns.
```maxon
typealias ReleaseCell = SharedValue with bool

let longTimeoutMs = 60000
let releasePollMs = 1

type Store
	let released as ReleaseCell
	var n as Integer

	static function create(released ReleaseCell) returns Self
		return Self{released: released, n: 0}
	end 'create'

	export function hold()
		while not self.released.current() 'untilReleased'
			sleep(releasePollMs)
		end 'untilReleased'

		print("held\n")
	end 'hold'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

type Closer
	static function create() returns Self
		return Self{}
	end 'create'

	export function close(target Store.handle)
		print("closing\n")
		target.shutdown()
	end 'close'
end 'Closer'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let h = spawn(capacity: 1) Store.create(released.clone())
	let closer = spawn Closer.create()
	try h.hold() otherwise panic("an empty mailbox takes the first send")
	Scheduler.yield()
	try h.keep("s{0}") otherwise panic("the service took hold, so its one slot is free")
	try closer.close(h.clone()) otherwise panic("an open mailbox with room takes the send")

	try h.keep("s{1}", sendTimeout: longTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		released.publish(true)
		return 0
	end 'refused'

	print("sent s1\n")
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
closing
stopped s1
held
kept s0
```

<!-- test: a-coroutine-dropped-while-waiting-for-room-gives-up-its-send -->
<!-- procs: 1 -->
<!-- preempt: off -->
A coroutine parked on a full mailbox's sender queue has its promise dropped. The drop takes it off the queue and
readies it, and its send fails `mailboxFull` with the `String` handed back, so a coroutine winding down gives up
its message rather than waiting out a timeout this long. `main` yields twice for what the case needs parked: once
so `Store` takes `hold` and waits, and once so `begForRoom` runs to its wait. The block's end drops the promise,
and `main` prints before the coroutine runs again. `hold` returns only once `main` publishes the release, which it
does after the coroutine has published that it gave up, so the room the coroutine would have been woken by cannot
appear first, however slow the host is.
```maxon
typealias ReleaseCell = SharedValue with bool

let longTimeoutMs = 60000
let pollMs = 1

type Store
	let released as ReleaseCell
	var n as Integer

	static function create(released ReleaseCell) returns Self
		return Self{released: released, n: 0}
	end 'create'

	export function hold()
		while not self.released.current() 'untilReleased'
			sleep(pollMs)
		end 'untilReleased'

		print("held\n")
	end 'hold'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function begForRoom(h Store.handle, gaveUp ReleaseCell) returns Integer
	try h.keep("s1", sendTimeout: longTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("gave up {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		gaveUp.publish(true)
		return 1
	end 'refused'

	return 0
end 'begForRoom'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let gaveUp = ReleaseCell.create(false)
	let h = spawn(capacity: 1) Store.create(released.clone())
	try h.hold() otherwise panic("an empty mailbox takes the first send")
	Scheduler.yield()
	try h.keep("s0") otherwise panic("the service took hold, so its one slot is free")

	if true 'scope'
		let waiting = async begForRoom(h.clone(), gaveUp: gaveUp.clone())
		Scheduler.yield()
		print("waiting: {not waiting.isComplete()}\n")
		waiting.cancel()
	end 'scope'

	print("dropped\n")

	while not gaveUp.current() 'untilGaveUp'
		sleep(pollMs)
	end 'untilGaveUp'

	released.publish(true)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
waiting: true
dropped
gave up s1
held
kept s0
```

<!-- test: a-renounced-coroutine-does-not-wait-for-room -->
<!-- procs: 1 -->
<!-- preempt: off -->
A coroutine whose promise was dropped while it slept may not begin a wait on a far end, and a send given
`sendTimeout:` to a full mailbox is one. The coroutine sleeps without end (`ForeverMs`), so only the drop ends
the sleep; it resumes, and its send fails `mailboxFull` at once with the `String` handed back, so `s1` is never
delivered. `hold` returns only once `main` publishes the release, after the coroutine has published that its send
failed. A coroutine that waited instead would never publish, and `main` would wait for ever and the runner report
the case TIMED OUT: no release could wake it by taking `s0`, however slow the host is.
```maxon
typealias ReleaseCell = SharedValue with bool

let longTimeoutMs = 60000
let pollMs = 1

type Store
	let released as ReleaseCell
	var n as Integer

	static function create(released ReleaseCell) returns Self
		return Self{released: released, n: 0}
	end 'create'

	export function hold()
		while not self.released.current() 'untilReleased'
			sleep(pollMs)
		end 'untilReleased'

		print("held\n")
	end 'hold'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function napThenBeg(h Store.handle, gaveUp ReleaseCell) returns Integer
	sleep(ForeverMs)

	try h.keep("s1", sendTimeout: longTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("gave up {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		gaveUp.publish(true)
		return 1
	end 'refused'

	return 0
end 'napThenBeg'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let gaveUp = ReleaseCell.create(false)
	let h = spawn(capacity: 1) Store.create(released.clone())
	try h.hold() otherwise panic("an empty mailbox takes the first send")
	Scheduler.yield()
	try h.keep("s0") otherwise panic("the service took hold, so its one slot is free")

	if true 'scope'
		let napping = async napThenBeg(h.clone(), gaveUp: gaveUp.clone())
		Scheduler.yield()
		print("napping: {not napping.isComplete()}\n")
		napping.cancel()
	end 'scope'

	print("dropped\n")

	while not gaveUp.current() 'untilGaveUp'
		sleep(pollMs)
	end 'untilGaveUp'

	released.publish(true)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
napping: true
dropped
gave up s1
held
kept s0
```

<!-- test: a-zero-send-timeout-fails-at-once -->
<!-- procs: 1 -->
<!-- preempt: off -->
`sendTimeout: 0` is the default spelled out: a full mailbox fails `mailboxFull` at once, exactly as
`a-full-mailbox-refuses-a-send-and-hands-its-arguments-back` does with no label. `main` never parks, so the
service runs nothing until the exit drain.
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
	let h = spawn(capacity: 1) Store.create()
	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	try h.keep("s{1}", sendTimeout: 0) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 0
	end 'refused'

	print("sent s1\n")
	return 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
full s1
kept s0
```

<!-- test: the-largest-send-timeout-waits-for-room-instead-of-failing-at-once -->
<!-- procs: 1 -->
<!-- preempt: off -->
A `sendTimeout:` of `Milliseconds` maximum is a wait with no practical end, not an overflowed deadline that
has already passed. The shape is `a-send-with-a-timeout-waits-for-room`'s: the second send finds the one slot
full and parks until the service has taken `s0`, then lands in the slot that take freed. The service handles
`s0` and `s1` before `main` prints.
```maxon
let longestTimeoutMs = u64.max

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
	let h = spawn(capacity: 1) Store.create()
	try h.keep("s{0}") otherwise panic("a capacity of one takes the first send")

	try h.keep("s{1}", sendTimeout: longestTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		return 1
	end 'refused'

	print("sent s1\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept s0
kept s1
sent s1
```

<!-- test: an-indefinite-send-waits-past-a-sleeping-handler-and-delivers -->
<!-- procs: 1 -->
<!-- preempt: off -->
A send whose `sendTimeout:` is so large that its deadline cannot be represented arms no timer, so nothing but a
drain, a shutdown or a cancellation ends the wait. The wait here is a real one: the yield lets the service take
`hold`, whose handler returns only once a coroutine publishes the release, and that coroutine runs only when
`main` has parked on its send. So `s0` fills the one slot and `s1` parks for the whole of `hold`, however slow
the host is. The service then takes `s0`, moves `s1` into the freed slot and readies `main`, which would have failed
`mailboxFull` had a deadline been armed and passed. The service handles `s0` and `s1` before `main` prints.
`i64.max` is the other half of `the-largest-send-timeout-waits-for-room-instead-of-failing-at-once`: it is
representable as a signed count of milliseconds and too large to turn into a deadline, so the saturation is the
clock-reading road's rather than the sign test's.
```maxon
typealias ReleaseCell = SharedValue with bool

let indefiniteTimeoutMs = i64.max
let releasePollMs = 1

type Store
	let released as ReleaseCell
	var n as Integer

	static function create(released ReleaseCell) returns Self
		return Self{released: released, n: 0}
	end 'create'

	export function hold()
		while not self.released.current() 'untilReleased'
			sleep(releasePollMs)
		end 'untilReleased'

		print("held\n")
	end 'hold'

	export function keep(s String)
		self.n = self.n + 1
		print("kept {s}\n")
	end 'keep'
end 'Store'

function publishRelease(released ReleaseCell) returns Integer
	released.publish(true)
	return 1
end 'publishRelease'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let h = spawn(capacity: 1) Store.create(released.clone())
	try h.hold() otherwise panic("an empty mailbox takes the first send")
	Scheduler.yield()
	try h.keep("s{0}") otherwise panic("the service took hold, so its one slot is free")

	let releaser = async publishRelease(released.clone())

	try h.keep("s{1}", sendTimeout: indefiniteTimeoutMs) otherwise (e) 'refused'
		match e 'why'
			mailboxFull(s) then print("full {s}\n")
			serviceStopped(s) then print("stopped {s}\n")
		end 'why'

		releaser.cancel()
		return 1
	end 'refused'

	_ = await releaser
	print("sent s1\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
held
kept s0
kept s1
sent s1
```

<!-- test: error.a-message-that-sends-without-end-is-refused -->
A send that waits without end on a mailbox nobody will ever receive from is a deadlock, as Go's blocking send on
a channel nobody reads is. A message that makes one — `stall` fills its own service's one slot with `fill` and
then sends a second `fill` to the same full mailbox with `ForeverMs`, so the service would park as a sender on
the mailbox it has to receive from — is refused, because a message may not wait on a send: it holds its own
service while it waits. Only `main` and its coroutines send with a timeout, and `main`'s own waits are pinned by
`an-indefinite-send-waits-past-a-sleeping-handler-and-delivers` and the cases around it.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function stall(me Store.handle)
		try me.fill() otherwise panic("the slot the take of `stall` freed holds the first fill")
		try me.fill(sendTimeout: ForeverMs) otherwise panic("a send that waits without end returns only when it is delivered or the service stops")
	end 'stall'

	export function fill()
		self.n = self.n + 1
	end 'fill'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	try h.stall(h.clone()) otherwise panic("an empty mailbox takes the first send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3221: <fragment>:9:18: the message 'stall' may wait (it sends with a timeout, which waits for mailbox space, at line 11) while holding its own service; send with `sendTimeout: 0`, which fails `mailboxFull` instead of waiting
```

<!-- test: error.a-message-that-sends-with-a-timeout-is-refused -->
A send with a `sendTimeout:` of anything but a literal `0` waits for room in a full mailbox, so a message that
makes one holds its own service while it waits and is refused. A `sendTimeout:` of `0` is the default spelled out:
a full mailbox fails at once and nothing waits (see `a-message-may-send-with-a-literal-zero-timeout`).
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function fill()
		self.n = self.n + 1
	end 'fill'
end 'Store'

type Pusher
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function push(target Store.handle)
		try target.fill(sendTimeout: 50) otherwise panic("room appears when the service takes the first fill")
	end 'push'
end 'Pusher'

function main() returns ExitCode
	let store = spawn(capacity: 1) Store.create()
	let pusher = spawn Pusher.create()
	try pusher.push(store.clone()) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3221: <fragment>:21:18: the message 'push' may wait (it sends with a timeout, which waits for mailbox space, at line 22) while holding its own service; send with `sendTimeout: 0`, which fails `mailboxFull` instead of waiting
```

<!-- test: error.a-message-that-awaits-a-promise-it-cannot-see-is-refused -->
A promise a message did not start itself comes from a coroutine the compiler cannot see, so awaiting it may wait on
anything and is refused like an await of a reply. An await of a coroutine the message started itself, and that
the message can see finishes without waiting on another party, stays legal.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function seven() returns Integer
	Scheduler.yield()
	return 7
end 'seven'

function begin() returns IntPromise
	return async seven()
end 'begin'

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function settle() returns Integer
		let p = begin()
		return await p
	end 'settle'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:21:18: the message 'settle' may wait (it awaits a promise whose coroutine it cannot see, at line 23) while holding its own service
```

<!-- test: error.a-message-that-sends-with-a-computed-timeout-is-refused -->
Only a literal `sendTimeout: 0` is known not to wait. A timeout the compiler cannot read as that literal may be any
duration, so a message that sends with one is refused even when every caller passes `0`.
```maxon
type Peer
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function note()
		self.n = self.n + 1
	end 'note'
end 'Peer'

type Probe
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function post(p Peer.handle, ms Milliseconds)
		try p.note(sendTimeout: ms) otherwise ignore
	end 'post'
end 'Probe'

function main() returns ExitCode
	let probe = spawn Probe.create()
	let peer = spawn Peer.create()
	try probe.post(peer.clone(), ms: 0) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3221: <fragment>:21:18: the message 'post' may wait (it sends with a timeout, which waits for mailbox space, at line 22) while holding its own service; send with `sendTimeout: 0`, which fails `mailboxFull` instead of waiting
```

<!-- test: error.a-message-that-starts-a-waiting-coroutine-is-refused -->
Starting a coroutine whose body waits is a wait: the message holds its own service while it awaits the coroutine,
and the coroutine waits on another service's reply. The chain names the coroutine's function and the `await`
inside it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Peer
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Peer'

function waitsOnPeer(p Peer.handle) returns Integer
	return try await p.ping() otherwise 0
end 'waitsOnPeer'

type Probe
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function relay(p Peer.handle) returns Integer
		let pending = async waitsOnPeer(p.clone())
		return await pending
	end 'relay'
end 'Probe'

function main() returns ExitCode
	let probe = spawn Probe.create()
	let peer = spawn Peer.create()
	_ = probe
	_ = peer
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:27:18: the message 'relay' may wait (it starts 'waitsOnPeer', which awaits a reply from 'Peer' at line 17) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: a-message-may-send-with-a-literal-zero-timeout -->
<!-- procs: 1 -->
<!-- preempt: off -->
A literal `sendTimeout: 0` is not a wait, so a message may make that send. `forward` posts three bumps to a
counter whose mailbox holds one: the first lands and the other two fail `mailboxFull` at once, which `forward`
counts, so the reply is the number of refusals. `main`'s own send of `total` waits for room, which is `main`'s
to do.
```maxon
type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump(by Integer)
		self.n = self.n + by
	end 'bump'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Counter'

type Relay
	var refused as Integer

	static function create() returns Self
		return Self{refused: 0}
	end 'create'

	export function forward(peer Counter.handle, times Integer) returns Integer
		for _ in 0 upto times 'each'
			try peer.bump(1, sendTimeout: 0) otherwise (e) 'full'
				match e 'why'
					mailboxFull then self.refused = self.refused + 1
					serviceStopped then self.refused = self.refused + 1
				end 'why'
			end 'full'
		end 'each'

		return self.refused
	end 'forward'
end 'Relay'

function main() returns ExitCode
	let counter = spawn(capacity: 1) Counter.create()
	let relay = spawn Relay.create()
	let refused = try await relay.forward(counter.clone(), times: 3) otherwise 99
	let total = try await counter.total(sendTimeout: ForeverMs) otherwise 0
	print("refused={refused} total={total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
refused=2 total=1
```
```exitcode
0
```

<!-- test: a-request-and-its-reply-are-two-messages -->
A service that needs another service's answer sends the request with its own handle and receives the answer as a
message. `Asker.start` posts `Oracle.ask` with a clone of its own handle and returns at once; `Oracle.ask` posts
`Asker.answer` back and returns; neither waits. `main` polls `Asker.result` until the answer has landed.
```maxon
type Oracle
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask(back Asker.handle, question Integer)
		try back.answer(question * 2) otherwise panic("an open mailbox with room takes the send")
	end 'ask'
end 'Oracle'

type Asker
	var got as Integer

	static function create() returns Self
		return Self{got: 0}
	end 'create'

	export function start(oracle Oracle.handle, me Asker.handle)
		try oracle.ask(me.clone(), question: 21) otherwise panic("an open mailbox with room takes the send")
	end 'start'

	export function answer(value Integer)
		self.got = value
	end 'answer'

	export function result() returns Integer
		return self.got
	end 'result'
end 'Asker'

function main() returns ExitCode
	let oracle = spawn Oracle.create()
	let asker = spawn Asker.create()
	try asker.start(oracle, me: asker.clone()) otherwise panic("an open mailbox with room takes the send")

	var answer = 0 as Integer

	while answer == 0 'untilAnswered'
		Scheduler.yield()
		answer = try await asker.result() otherwise 0
	end 'untilAnswered'

	print("answer={answer}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
answer=42
```
```exitcode
0
```

<!-- test: error.a-message-may-not-declare-a-send-timeout-parameter -->
`sendTimeout:` is reserved on every message call, so a message may not declare a parameter of that name: a
call could not tell the payload from the timeout.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String, sendTimeout Milliseconds)
		self.n = self.n + 1
		print("kept {s} {sendTimeout}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.keep("a{1}", sendTimeout: 3) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3192: <fragment>:16:10: the message `Store.keep` declares a parameter named `sendTimeout`, and this `spawn` makes `Store` a service. `sendTimeout:` is reserved on every message call: it is the milliseconds a send waits for room in a full mailbox, and it never enters the request, so a call could not tell the parameter from the timeout. Rename the parameter
```

<!-- test: dropping-the-last-handle-shuts-the-service-down -->
<!-- procs: 1 -->
<!-- preempt: off -->
⚠ **THE TWO PINS ARE THIS CASE'S OWN STATED PREMISE, WRITTEN DOWN WHERE THE RUNNER CAN READ THEM.** The
paragraph below says *"nothing runs on a service's green thread until the main thread stops running"*, and
that takes BOTH: one processor, because there is no `MAXON_MAX_PROCS=1` default — the count is the machine's,
and with a second M the two services run concurrently and either order is correct — and a monitor that leaves `main` its processor,
because the monitor asks any strand that has held one for 10 ms of WALL time to yield, a preempted `main`
goes to the global tail and its machine takes `runnext`, so a `main` held off a core between its two spawns
prints `beep 2` before `beep 1`. The ORDER follows from the
premise and not from the feature, so the expectation stands and both CONDITIONS are pinned — the same pair
four `sched-runqueue` order cases carry.

An ordinary program needs no shutdown boilerplate: the handle is an owned box, and its scope-exit drop is
what closes the mailbox. `inner` is dropped at the end of the labelled block and `outer` at `main`'s return,
and BOTH services' queued work runs — which is the property under test.

⚠⚠ **THE ORDER OF THE TWO LINES IS THE EXIT DRAIN'S AND NOT CAUSALITY, AND THIS CASE SAYS SO RATHER THAN
IMPLYING OTHERWISE.** At one processor with preemption off, nothing runs on a service's green thread until
the main thread stops running — an early handle drop closes the mailbox, but `main` never parks and nothing
takes its processor, so its machine takes nothing else off the ring — and both handlers run at the exit
drain, in the order the drain's scheduler loop takes them: the processor's `runnext` slot first, so the
service spawned LAST, then the ring in spawn order.
`two-instances-are-independent` is the case whose order IS forced, by a handle transfer.

⚠⚠ **THE `stdout` BLOCK PINS THAT ORDER EXACTLY, SO THIS CASE IS A TRIPWIRE ON THE DRAIN AND NOT ONLY ON
THE SHUTDOWN.** That is the useful arrangement rather than a defect to weaken away: a drain-order change
is exactly the kind of scheduler edit whose blast radius someone should have to look at. ⇒ **When this line moves, the
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
		try inner.beep() otherwise panic("an open mailbox with room takes the send")
	end 'early'
	try outer.beep() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
beep 1
beep 2
```

<!-- test: a-cloned-handle-keeps-the-service-alive -->
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
	try a.tick() otherwise panic("an open mailbox with room takes the send")
	try b.tick() otherwise panic("an open mailbox with room takes the send")
	try b.report() otherwise panic("an open mailbox with room takes the send")
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
A handle is a transferable message payload: `Worker` is handed the `Logger`'s handle, sends to it, and then
DROPS it — the un-consumed payload drop the loop owes for every message it runs. `logger` is a `var`, so the
send MOVES it: that drop is the `Logger`'s last handle, and the logger shuts down without `main` ever naming
it again.
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
		try sink.say("from the worker") otherwise panic("an open mailbox with room takes the send")
	end 'run'
end 'Worker'

function main() returns ExitCode
	let logger = spawn Logger.create()
	let worker = spawn Worker.create()
	try worker.run(logger) otherwise panic("an open mailbox with room takes the send")
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
	let hs = CounterHandleArray.create(0)
	hs.push(spawn Counter.create())
	hs.push(spawn Counter.create())
	return hs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
2
```

<!-- test: a-handle-read-out-of-an-array-survives-a-second-send -->
⭐⭐ **A HANDLE READ *OUT OF* A CONTAINER, WHICH `handles-in-an-array` NEVER DOES — IT ONLY PUSHES AND
DROPS.** Binding an element to a local promotes a BORROW to an owned name, and for a handle that retain
must step the mailbox's `handles`/`refs` pair (`SignatureIndex.managedNameRetainCallee`) at BOTH retain
doors — the void one and the RETURNING one (`Parser.retainBorrowedAggregate`, the door every `arr.get(i)`
binding takes). A retain that steps only the plain box count leaves the local's drop stepping a pair its
retain never stepped: the mailbox is CLOSED AND FREED while the array's handle still names it, and the
SECOND `get` sends into freed memory.

⚠ **ONE SEND IS CLEAN EVEN UNDER THAT DEFECT.** The first message is already queued when the mailbox dies,
so it is still handled and still answered; the loop only stops afterwards. **At `MAXON_MAX_PROCS` 1 and 16
alike, two sends are exit 92 (`RuntimeAbort.schedulerDeadlock`) and a `.clone()` of the element read is
exit 89 (`slabFreeOfParkedSpan`)** — a heap corruption, not a diagnostic. This case
therefore sends THREE times through three separate reads, and its answer is the accumulated total.
```maxon
type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump(by Integer) returns Integer
		self.n = self.n + by
		return self.n
	end 'bump'
end 'Counter'

typealias CounterHandleArray = Array with Counter.handle
typealias BumpReplyArray = Array with Promise with Integer

function main() returns ExitCode
	let hs = CounterHandleArray.create(0)
	hs.push(spawn Counter.create())

	let replies = BumpReplyArray.create(0)
	for step in 1 to 3 'eachSend'
		let h = try hs.get(0) otherwise panic("hs.get(0)")
		replies.push(try h.bump(step) otherwise panic("an open mailbox with room takes the send"))
	end 'eachSend'

	var total = 0 as Integer
	for r in replies 'eachReply'
		total = await r
	end 'eachReply'

	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: a-handle-payload-beside-a-consumed-string-payload -->
⭐⭐ **TWO SERVICES, TWO PAYLOAD KINDS, AND THE CROSSING BETWEEN TWO NAME TABLES.** `Logger.say` takes a
`String` and CALLS A METHOD on it; `Worker.run` takes a `Logger.handle`. Neither is remarkable alone — the
cases above pin each — and together they are the first program in this file whose signature-index and project
name tables have DIVERGED at the id a payload carries. `<T>.__behave` resolves that id, and it has to resolve it
against the table it came out of.

⛔ **RESOLVED AGAINST THE WRONG TABLE, IT IS A CLEAN REFUSAL OF A CORRECT PROGRAM:** `E3005 argument type
mismatch for 'sink': expected 'Logger.handle', got 'ExitCode'` — with no file and no line, because the check
is running the synthesized behaviour. `ExitCode` is simply what the OTHER table holds at that number. See
`Runtime/ServiceBehaviour.maxon`'s header for the crossing and `ModuleInit.projectScopedNameId` for the carrier's
two-sided contract.

⚠ **WHY IT TAKES TWO SERVICES AND A METHOD CALL.** The two tables agree at every id until enough types are
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
		self.bytes = self.bytes + (s.byteLength() as Integer)
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
		try sink.say("from the worker") otherwise panic("an open mailbox with room takes the send")
		try sink.report() otherwise panic("an open mailbox with room takes the send")
	end 'run'
end 'Worker'

function main() returns ExitCode
	let logger = spawn Logger.create()
	let worker = spawn Worker.create()
	try worker.run(logger.clone()) otherwise panic("an open mailbox with room takes the send")
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
A `var` argument is MOVED: the sending frame hands over the reference it holds and the service becomes the
box's one owner. Nothing is increfed at the send and nothing is dropped by the sender, which is what keeps
the plain refcount correct across a green thread. The bare literal is an immortal `.rdata` record, promoted
to a fresh owned copy at the send; a `let` local is moved exactly as a `var` is.
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
	try h.keep(a) otherwise panic("an open mailbox with room takes the send")
	let b = "two-{2}"
	try h.keep(b) otherwise panic("an open mailbox with room takes the send")
	try h.keep("a literal") otherwise panic("an open mailbox with room takes the send")
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
⭐⭐ **A `throws` CLAUSE IS WHAT MAKES A MESSAGE REPLY-BEARING, AND ITS REPLY CAN STILL BE DISCARDED ON
PURPOSE.** Sent as a bare statement it is E3190, because the reply would be dropped without anyone seeing the
error; `_ = try h.keep(…)` drops it deliberately. The handler runs and its error has nowhere to go. What must
NOT happen is a leak: the second `keep` throws before it reads `s`, so the `String` it was handed is still the
loop's to drop.

⛔ **A `tryCall` opens an error-edge diamond, so the payload drop lands in that diamond's MERGE.** An arm
that terminates its own block instead overwrites the diamond's branch and skips it: correct answers print,
and every throwing message's payload is stranded — exit 101.
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
	_ = try h.keep("a{1}") otherwise panic("an open mailbox with room takes the send")
	_ = try h.keep("b{2}") otherwise panic("an open mailbox with room takes the send")
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
The synthesized request union is an ordinary union, so its `.unionCases` companion exists — and the first
message an author declares is variant 0, the next variant 1.
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
	spawn Calc.create()
	print("{Calc.request.unionCases.bump.rawValue} {Calc.request.unionCases.total.rawValue}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
0 1
```

<!-- test: an-idle-service-that-is-never-sent-to-still-exits-zero -->
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
⭐ The case that pins that a post is not a wait — without it a later tightening would silently ban correct
programs. `A` names `B`'s handle in a message and `B` names `A`'s, which is a ring of services and no wait at
all, because neither send waits.
```maxon
type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping(b B.handle)
		try b.pong() otherwise panic("an open mailbox with room takes the send")
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
	try a.ping(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: a-ring-of-posts-answers-through-continuations -->
⭐⭐ **A POST IS NOT A WAIT, SO A RING OF POSTS IS A REQUEST AND ITS REPLY.** `A.kick` posts `B.work` and answers
at once; `B.work` posts `A.ack` with `B`'s own handle and returns; `A.ack` posts the answer back to `B.acked` and
returns. The services name each other in a ring, and no message waits for anything, so `A` is free to serve `B`'s
`ack` when it arrives. `main` polls `B.replies` until the answer has landed.

⚠ `B.work` sends its own handle (`me`, a clone `A.kick` made of `peer`) so that the reply can come back as a
message. A handle a message was given is borrowed and a send moves, so each forward is a `.clone()`.
```maxon
type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function kick(peer B.handle, mine A.handle) returns Integer
		try peer.work(mine.clone(), me: peer.clone()) otherwise panic("an open mailbox with room takes the send")
		return 1
	end 'kick'

	export function ack(to B.handle)
		try to.acked(7) otherwise panic("an open mailbox with room takes the send")
	end 'ack'
end 'A'

type B
	var heard as Integer

	static function create() returns Self
		return Self{heard: 0}
	end 'create'

	export function work(back A.handle, me B.handle)
		try back.ack(me.clone()) otherwise panic("an open mailbox with room takes the send")
	end 'work'

	export function acked(v Integer)
		print("acked {v}\n")
		self.heard = self.heard + 1
	end 'acked'

	export function replies() returns Integer
		return self.heard
	end 'replies'
end 'B'

function main() returns ExitCode
	let a = spawn A.create()
	let b = spawn B.create()
	let v = try await a.kick(b.clone(), mine: a.clone()) otherwise 0

	while (try await b.replies() otherwise 0) < 1 'untilAcked'
		Scheduler.yield()
	end 'untilAcked'

	return (v + 5) as ExitCode
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
An export method reachable only as a MESSAGE must count as used, or the unused-export check would refuse
every service whose handle is the only caller. It is credited by the send op naming `Calc.bump` — the same
`maxonOpCalleeKind` road an ordinary call is credited by, which is why this needs no arm of its own.
```maxon
// --- file: calc/calc.maxon
export type Calc
	var count as Integer

	export static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Integer)
		self.count = self.count + by
	end 'bump'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Calc.create()
	try h.bump(1) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.a-sent-value-is-moved-and-reading-it-back-is-refused -->
A `var` argument MOVES: the send consumes it, the source is poisoned and a read is E3102.

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
	var buf = "hello {1}"
	try h.keep(buf) otherwise panic("an open mailbox with room takes the send")
	print("{buf}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:18:10: use of moved value 'buf': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-sent-let-is-moved-and-reading-it-back-is-refused -->
A `let` argument MOVES exactly as a `var` does: the send consumes the binding and a later read is E3102.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String)
		self.n = self.n + (s.byteLength() as Integer)
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello {1}"
	try h.keep(buf) otherwise panic("an open mailbox with room takes the send")
	print("{buf}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:18:10: use of moved value 'buf': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-sent-let-is-moved-and-the-service-frees-it -->
The service becomes the sole owner of a `let` it was sent, and releases it exactly once.
```maxon
type Store
	var sum as Integer

	static function create() returns Self
		return Self{sum: 0}
	end 'create'

	export function keep(s String)
		self.sum = self.sum + (s.byteLength() as Integer)
		print("kept {s}\n")
	end 'keep'

	export function total() returns Integer
		return self.sum
	end 'total'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello padded out long enough to heap allocate {1}"
	try h.keep(buf) otherwise panic("an open mailbox with room takes the send")
	let n = try await h.total() otherwise 0
	print("{n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
kept hello padded out long enough to heap allocate 1
47
```

<!-- test: a-string-a-container-also-holds-is-sent-with-its-owners-counted -->
A String a container also holds has a second owner on this green thread, and a send of it is correct: the box
counts its owners, so the service and the sender each hold a counted reference to one String.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String) returns Integer
		self.n = self.n + 1
		return s.byteLength()
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let buf = "hello {1}"
	let held = Strings.create(0)
	held.push(buf)
	let got = try await h.keep(buf) otherwise panic("an open mailbox with room takes the send")
	print("{got} {held.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Strings = Array with String
```
```stdout
7 1
```
```exitcode
0
```

<!-- test: a-borrowed-parameter-nothing-writes-is-shared-with-the-service -->
A message argument the sender does not own is SHARED when nothing can write it: `p` arrived as a borrowed
struct parameter, `forward` never writes it, and the handler only reads it. The graph is marked shared, so every
count in it steps atomically, and both threads read the one record. `forward`'s caller still holds `p` and reads
it afterwards. The parameter is kept by the call the way a `let` field keeps one, so a caller hands it a fresh
or read-only value.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function forward(h Store.handle, p immutable Payload) returns Integer
	return (try await h.keep(p) otherwise 0) + p.n
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = freeze Payload.create(7)
	let total = forward(h, p: p)
	print("{total} {p.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
14 7
```

<!-- test: error.a-borrowed-parameter-the-sender-writes-may-not-be-sent -->
`forward` writes `p`, so the record is not read-only for the thread that sends it: the service would read it
while `forward` changes it.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function forward(h Store.handle, p Payload) returns Integer
	p.n = 8
	return try await h.keep(p) otherwise 0
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	var p = Payload.create(7)
	return forward(h, p: p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:25:26: 'p' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.a-borrowed-parameter-the-handler-writes-may-not-be-sent -->
The handler writes its parameter, so a record both threads reach would be written by the service while the sender
reads it.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(p isolated Payload) returns Integer
		modify p 'write'
			p.n = p.n + 1
		end 'write'

		self.seen = self.seen + 1
		return p.n
	end 'bump'
end 'Store'

function forward(h Store.handle, p Payload) returns Integer
	return try await h.bump(p) otherwise 0
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = Payload.create(7)
	return forward(h, p: p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:28:26: 'p' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.a-var-read-again-is-not-handed-to-a-function-that-sends-it-shared -->
`forward` sends its parameter to the service, which owns it afterwards, so a binding the caller reads again is a
moved value.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function forward(h Store.handle, p isolated Payload) returns Integer
	return try await h.keep(p) otherwise 0
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let p = Payload.create(7) as isolated Payload
	let total = forward(h, p: p)
	return (total + p.n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:31:18: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-module-let-record-is-shared-with-the-service -->
A module-level `let` is read-only for the whole program, so a message may carry it directly. The sender does not own
the record, nothing can write it, and the handler only reads it: the graph is marked shared, every count in it steps
atomically, and both threads read the one record.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let shared = freeze Payload.create(7)
	let total = (try await h.keep(shared) otherwise 0) + shared.n
	print("{total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
14
```

<!-- test: a-let-field-record-is-shared-with-the-service -->
A `let` field has no other writer for the record's whole life, so the record read out of one is a read-only root the
sender does not own, and a message shares it exactly as it shares a module-level `let`.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Holder
	export let payload as immutable Payload

	static function create(n Integer) returns Self
		return Self{payload: freeze Payload.create(n)}
	end 'create'
end 'Holder'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable Payload) returns Integer
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let holder = Holder.create(5)
	let total = (try await h.keep(holder.payload) otherwise 0) + holder.payload.n
	print("{total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
10
```

<!-- test: error.a-module-let-record-the-handler-writes-may-not-be-sent -->
The handler writes its parameter, so a record both threads reach would be written by the service while the sender
and every other reader of the module-level `let` see it. A read-only value is shared only with a handler that reads.
```maxon
type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(p immutable Payload) returns Integer
		p.n = p.n + 1
		self.seen = self.seen + 1
		return p.n
	end 'bump'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let shared = freeze Payload.create(7)
	return (try await h.bump(shared) otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3197: <fragment>:18:3: cannot write 'n' through 'p', which is immutable
```

<!-- test: a-shared-value-read-is-shared-with-the-service -->
A value `SharedValue.current()` answers is read-only for the whole program and owned by the cell, so a message may carry
it directly: the graph is shared, every count in it steps atomically, and the handler only reads it.
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(c immutable Config) returns Integer
		self.seen = self.seen + 1
		return c.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(7))
	let h = spawn Store.create()
	let shared = freeze cell.current().clone()
	let total = (try await h.keep(shared) otherwise 0) + cell.current().n
	print("{total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias ConfigCell = SharedValue with Config
```
```exitcode
0
```
```stdout
14
```

<!-- test: error.a-shared-value-read-the-handler-writes-may-not-be-sent -->
The handler writes its parameter, so the record the cell hands every reader would be written by the service while the
cell's other readers see it.
```maxon
type Config
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(c immutable Config) returns Integer
		c.n = c.n + 1
		self.seen = self.seen + 1
		return c.n
	end 'bump'
end 'Store'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(7))
	let h = spawn Store.create()
	let shared = freeze cell.current().clone()
	return (try await h.bump(shared) otherwise 0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias ConfigCell = SharedValue with Config
```
```maxoncstderr
error E3197: <fragment>:18:3: cannot write 'n' through 'c', which is immutable
```

<!-- test: error.a-service-is-rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⭐ On a target with no service substrate, BOTH ops are refused at their own source span with `E3104`,
naming the runtime entry that has no lowering there — never a panic from inside a backend.

⚠ **THE TWO OPS NEED TWO ARMS, AND ONE WOULD HAVE PASSED A HALF-BUILT GATE.** A `spawn` and a send mint
different entries, and a program can contain either without the other — `an-idle-service-that-is-never-sent-to-still-exits-zero`
is exactly a spawn with no send.
⚠ **NO NATIVE LANE REFUSES A SERVICE, SO THIS wasm CASE HAS NO NATIVE TWIN.** A native twin's program
and expected diagnostic would be byte-identical to this one's but for the target name — one fact written
down twice.

⇒ **THE CONVENTION, STATED ONCE FOR EVERY wasm-ONLY REFUSAL CASE THAT SHARES IT**
(`builtins-cpu-parallel.md`'s counter case is another): a target-refusal case needs a lane that actually
refuses. When the last native lane gains a facility, its native witness has nowhere to go — **do not
re-point it at `wasm32-wasi` beside an existing wasm sibling, and do not re-add one unless a native lane
loses the facility.** While wasm refuses the facility, the wasm case carries the rule by itself.

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
	try h.at(1) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3104: <fragment>:15:10: 'spawn' lowers to the runtime entry '__cown_spawn', which has no wasm32-wasi implementation
error E3104: <fragment>:16:6: a message send lowers to the runtime entry '__cown_send', which has no wasm32-wasi implementation
```

<!-- test: a-scalar-only-record-crosses-whole -->
⭐ The simplest shape that crosses, and the control for the `deepmove.*` cases below: a record whose every
slot is a machine word holds no reference to anything, so the walk has nothing to visit and moving it moves
the whole of it. Built at the send, so this frame gives up the only reference there was.
```maxon
type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Plot
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function at(p isolated Point)
		self.n = self.n + 1
		print("at {p.x},{p.y} ({self.n})\n")
	end 'at'
end 'Plot'

function main() returns ExitCode
	let h = spawn Plot.create()
	try h.at(Point.create(1, y: 2)) otherwise panic("an open mailbox with room takes the send")
	try h.at(Point.create(3, y: 4)) otherwise panic("an open mailbox with room takes the send")
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

<!-- test: deepmove.a-record-with-managed-fields-crosses -->
A record with managed fields crosses at the `spawn`, as the factory's argument, and its three fields are
the three answers the walk gives. The literal `String` is an immortal `.rdata` record: nothing ever steps
its count, so it has none to check and passes. The interpolated `String` is a heap record the walk visits
and finds sole. The `CellArray` is a container the walk descends, element by element. Every reachable
record has one owner, so the whole graph moves and the service reads all of it.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Holder
	export var tag as String
	export var label as String
	export var cells as CellArray

	static function create(k Integer) returns Self
		let cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{tag: "literal", label: "heap {k}", cells: cells}
	end 'create'
end 'Holder'

type Svc
	var held as Holder

	static function create(held isolated Holder) returns Self
		return Self{held: merge held}
	end 'create'

	export function report()
		print("{self.held.tag} {self.held.label} {self.held.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(Holder.create(7))
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
literal heap 7 2
```

<!-- test: deepmove.a-reply-carries-a-container-back -->
The reply road runs the same walk in the other direction, and a container whose whole graph is fresh
crosses: the handler mints the array in its own frame and every element is a scalar, so the walk finds
nothing to refuse and the awaiter takes the container whole.
```maxon
typealias IntegerArray = Array with Integer

type Source
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function values() returns isolated IntegerArray
		let xs = IntegerArray.create(0) as isolated IntegerArray

		modify xs 'fill'
			xs.push(1)
			xs.push(2)
			xs.push(3)
		end 'fill'

		return xs
	end 'values'
end 'Source'

function main() returns ExitCode
	let h = spawn Source.create()
	let xs = try await h.values() otherwise IntegerArray.create(0)
	var sum = 0 as Integer
	for x in xs 'each'
		sum = sum + x
	end 'each'
	return sum as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: deepmove.a-reply-carries-a-record-with-a-string-back -->
A record HOLDING a `String` comes back the way a bare `String` does: the record and the heap string inside
it are both minted by the handler, so the walk finds nothing to refuse and the record moves with its field.
```maxon
type Note
	export var text as String

	static function create(k Integer) returns Self
		return Self{text: "note {k}"}
	end 'create'
end 'Note'

type Author
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function write(k Integer) returns isolated Note
		return Note.create(k)
	end 'write'
end 'Author'

function main() returns ExitCode
	let h = spawn Author.create()
	let note = try await h.write(4) otherwise Note.create(0)
	print("{note.text}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
note 4
```

<!-- test: deepmove.error.a-reply-holding-a-shared-record-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Store
	var cells as CellArray

	static function create() returns Self
		var cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{cells: cells}
	end 'create'

	export function snapshot() returns isolated CellArray
		var out = CellArray.create(0)
		let first = try self.cells.get(0) otherwise Cell.create()
		out.push(first)
		return out
	end 'snapshot'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let got = try await h.snapshot() otherwise CellArray.create(0)
	return got.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:26:10: 'out' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated CellArray`, or pass a copy
```

<!-- test: deepmove.a-container-crosses-with-its-elements -->
A container crosses WHOLE. `push` increfs what it is handed, so an element's count says nothing the type can
promise about the frame that filled the array — which is why soleness is proved at the SEND, by a walk over
the value's graph, rather than declared from the type. Each `Cell` here has the array as its only owner, the
walk finds a count of 1 at every record it reaches, and the service receives the elements along with the
array.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(cs isolated CellArray)
		var sum = 0 as Integer
		for c in cs 'each'
			sum = sum + c.n
		end 'each'
		print("svc {cs.count()}\n")
		print("sum {sum}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	let cs = CellArray.create(0) as isolated CellArray

	modify cs 'fill'
		cs.push(Cell.create())
		cs.push(Cell.create())
	end 'fill'

	let h = spawn Svc.create()
	try h.take(cs) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc 2
sum 2
```

<!-- test: deepmove.error.a-container-holding-a-shared-record-is-refused-at-the-send -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(cs isolated CellArray)
		print("svc {cs.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cell = Cell.create()
	var cs = CellArray.create(0)
	cs.push(cell)
	let h = spawn Svc.create()
	try h.take(cs) otherwise panic("an open mailbox with room takes the send")
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:29:13: 'cs' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: deepmove.a-cloned-array-detaches-its-shared-buffer-at-the-send -->
A shared BUFFER is not a shared record. `a.clone()` yields a second array RECORD viewing `a`'s buffer until
one of them writes. The send moves `b`, and its walk detaches the shared buffer
as a write would — copying the elements out of `a`'s buffer into `b`'s own, exactly as `b.push(…)` would
have. Only a shared RECORD is refused; a shared buffer is what copy-on-write is for. The reply sequences the
two sides, so the service's lines land before `main` reads `a` back.
```maxon
typealias IntegerArray = Array with Integer

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(xs isolated IntegerArray) returns Integer
		for x in xs 'each'
			print("svc {x}\n")
		end 'each'
		return xs.count()
	end 'take'
end 'Svc'

function main() returns ExitCode
	let a = IntegerArray.create(0)
	a.push(1)
	a.push(2)
	a.push(3)
	let b = a.clone() as isolated IntegerArray
	let h = spawn Svc.create()
	let n = try await h.take(b) otherwise 0
	for x in a 'each'
		print("main {x}\n")
	end 'each'
	print("crossed {n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc 1
svc 2
svc 3
main 1
main 2
main 3
crossed 3
```

<!-- test: deepmove.a-cloned-string-crosses-while-the-sender-keeps-its-source -->
A `String` moved by a send is walked like every other managed argument. A clone is a fresh record of its own,
so the walk finds nothing outside the graph and the temporary crosses while `main` goes on reading and writing
the source it was copied from. The reply sequences the two sides.
```maxon
type Svc
	var last as String

	static function create() returns Self
		return Self{last: ""}
	end 'create'

	export function keep(s String) returns Integer
		self.last = s
		print("svc {self.last}\n")
		return self.last.byteLength() as Integer
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var src = "payload {42}"
	let n = try await h.keep(src.clone()) otherwise 0
	src.append("!")
	print("main {src} after the service kept {n} bytes\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc payload 42
main payload 42! after the service kept 10 bytes
```

<!-- test: deepmove.a-slice-written-at-the-send-crosses-while-the-sender-keeps-its-source -->
A slice is a temporary too: each `slice` form hands back the fresh record `sliceBytes` builds, a view onto
the source's bytes that nothing but the send can name. It moves, the walk detaches the view onto bytes of its
own, and `main` goes on reading and writing the source.
```maxon
type Svc
	var last as String

	static function create() returns Self
		return Self{last: ""}
	end 'create'

	export function keep(s String) returns Integer
		self.last = s
		print("svc {self.last}\n")
		return self.last.byteLength() as Integer
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var src = "payload {42}"
	let n = try await h.keep(src.sliceLength(src.startIndex(), length: 7)) otherwise 0
	src.append("!")
	let m = try await h.keep(src.slice(src.startIndex(), endIndex: src.endIndex())) otherwise 0
	src.append("?")
	print("main {src} after the service kept {n} and {m} bytes\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc payload
svc payload 42!
main payload 42!? after the service kept 7 and 11 bytes
```

<!-- test: deepmove.a-string-whose-bytes-the-sender-still-views-crosses-the-send -->
An owned `String` keeps its bytes inline, so a `toByteArray()` view counts the String's own RECORD. The view is the
record's second owner, so the record's count turns shared at the view and every step of it from then on is
atomic: the service drops the String on its own green thread while this one drops the view, and neither frees the
bytes early nor leaks them. The send is a plain move and nothing walks the String.
```maxon
type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s String) returns Integer
		return s.byteLength() as Integer
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var s = ""
	s = "payload {42}"
	let bytes = s.toByteArray()
	let n = try await h.take(s) otherwise 0
	print("service {n} view {bytes.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service 10 view 10
```

<!-- test: deepmove.a-grown-string-whose-bytes-the-sender-still-views-detaches-at-the-send -->
<!-- procs: 16 -->
A String that has been APPENDED to owns a separate buffer, so a `toByteArray()` view counts that buffer and
not the record. A shared buffer is what copy-on-write is for: the move's walk detaches `s` onto a private copy,
and the view is left the old buffer's one owner on this green thread. Without the detach the view's release
here and the String's release on the service step one plain count from two processors — a lost update leaks
the buffer (exit 101) or frees it early. Each reader's mailbox holds every round, so a send never finds it
full however far the readers fall behind.
```maxon
typealias Integer = int(i64.min to i64.max)

let serviceCount = 8
let rounds = 100000

type Reader
	var sum as Integer

	static function create() returns Self
		return Self{sum: 0}
	end 'create'

	export function take(s String)
		self.sum = self.sum + (s.byteLength() as Integer)
	end 'take'

	export function report() returns Integer
		return self.sum
	end 'report'
end 'Reader'

typealias ReaderHandles = Array with Reader.handle

function main() returns ExitCode
	let readers = ReaderHandles.create(0)
	for _ in 1 to serviceCount 'spawnEach'
		readers.push(spawn(capacity: rounds) Reader.create())
	end 'spawnEach'

	var kept = 0 as Count
	for i in 1 to rounds 'round'
		let r = try readers.get(i mod serviceCount) otherwise panic("readers.get")
		var s = ""
		s.append("payload {i mod 10}")
		let bytes = s.toByteArray()
		try r.take(s) otherwise panic("an open mailbox with room takes the send")
		kept = kept + bytes.count()
	end 'round'

	var total = 0 as Integer
	for k in 0 upto serviceCount 'collect'
		let r = try readers.get(k) otherwise panic("readers.get({k})")
		total = total + (try await r.report() otherwise 0)
	end 'collect'

	print("total {total} kept {kept}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total 900000 kept 900000
```

<!-- test: deepmove.a-string-viewed-by-the-sender-crosses-repeatedly -->
<!-- procs: 16 -->
A String whose bytes live inline in its record is viewed by the sender and sent, round after round, to eight
readers. The view is the record's second owner and turns its count shared, so the sender's release of the view and
the reader's release of the String step one atomic count. Without that, a lost update leaks the record (exit 101) or
frees it under the view.
```maxon
typealias Integer = int(i64.min to i64.max)

let serviceCount = 8
let rounds = 100000

type Reader
	var sum as Integer

	static function create() returns Self
		return Self{sum: 0}
	end 'create'

	export function take(s String)
		self.sum = self.sum + (s.byteLength() as Integer)
	end 'take'

	export function report() returns Integer
		return self.sum
	end 'report'
end 'Reader'

typealias ReaderHandles = Array with Reader.handle

function main() returns ExitCode
	let readers = ReaderHandles.create(0)
	for _ in 1 to serviceCount 'spawnEach'
		readers.push(spawn(capacity: rounds) Reader.create())
	end 'spawnEach'

	var kept = 0 as Count
	for i in 1 to rounds 'round'
		let r = try readers.get(i mod serviceCount) otherwise panic("readers.get")
		var s = ""
		s = "payload {i mod 10}"
		let bytes = s.toByteArray()
		try r.take(s) otherwise panic("an open mailbox with room takes the send")
		kept = kept + bytes.count()
	end 'round'

	var total = 0 as Integer
	for k in 0 upto serviceCount 'collect'
		let r = try readers.get(k) otherwise panic("readers.get({k})")
		total = total + (try await r.report() otherwise 0)
	end 'collect'

	print("total {total} kept {kept}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total 900000 kept 900000
```

<!-- test: deepmove.a-chain-crosses-with-its-elements -->
The SECOND element-bearing record crosses too, and it is a different walk: a chain owns its record, a node
per element and each node's element, so `__list_sole` proves all three where the buffer's walk proves the
record and its slots. Every `String` here is minted at the append, so each node and each element has one
owner and the whole chain moves.
```maxon
typealias Texts = List with String

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(xs isolated Texts)
		for x in xs 'each'
			print("svc {x}\n")
		end 'each'
	end 'take'
end 'Svc'

function main() returns ExitCode
	let xs = Texts.create() as isolated Texts

	modify xs 'fill'
		xs.append("a {1}")
		xs.append("b {2}")
	end 'fill'

	let h = spawn Svc.create()
	try h.take(xs) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc a 1
svc b 2
```

<!-- test: deepmove.error.a-chain-holding-a-shared-record-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias Cells = List with Cell

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(xs isolated Cells)
		print("svc {xs.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var xs = Cells.create()
	var cell = Cell.create()
	xs.append(cell)
	let h = spawn Svc.create()
	try h.take(xs) otherwise panic("an open mailbox with room takes the send")
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:29:13: 'xs' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: deepmove.a-map-crosses-with-its-keys-and-values -->
A `Map` is a declared record over two element-bearing containers, so it crosses through the ordinary field
cascade and its keys and values are proved by the containers' own element walks. Nothing here is a special
case for `Map`, which is the point: a record that holds containers is a record.
```maxon
typealias Names = Map with (String, String)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m isolated Names)
		print("svc {m.count()}\n")
		let v = try m.get("a 1") otherwise "missing"
		print("got {v}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	let m = Names.create() as isolated Names

	modify m 'fill'
		m.upsert("a {1}", value: "x {2}")
	end 'fill'

	let h = spawn Svc.create()
	try h.take(m) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
svc 1
got x 2
```

<!-- test: deepmove.error.a-map-holding-a-shared-value-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias Cells = Map with (String, Cell)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m isolated Cells)
		print("svc {m.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Cells.create()
	var cell = Cell.create()
	m.upsert("k {1}", value: cell)
	let h = spawn Svc.create()
	try h.take(m) otherwise panic("an open mailbox with room takes the send")
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:29:13: 'm' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: deepmove.a-map-releases-a-removed-entry-so-the-map-crosses -->
`remove` releases the key and the value it takes out, so the map no longer owns them. `main` still holds both
records, but the map's slot is empty, and the map crosses. A tombstone that kept its occupants would share `key`
and `text` between `main` and the service while `main` owns them outside the map, and the service's release of the
map would free them under `main`.
```maxon
typealias Texts = Map with (String, String)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m isolated Texts) returns Integer
		return m.count() as Integer
	end 'take'
end 'Svc'

function main() returns ExitCode
	let m = Texts.create() as isolated Texts
	let key = "gone {1}"
	let text = "value {7}"
	var removed = false

	modify m 'fill'
		m.upsert(key, value: text)
		m.upsert("kept {2}", value: "value {8}")
		removed = m.remove(key)
	end 'fill'

	let h = spawn Svc.create()
	let n = try await h.take(m) otherwise 0
	print("removed {removed}, the service holds {n}, main still reads {key} and {text}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
removed true, the service holds 1, main still reads gone 1 and value 7
```

<!-- test: deepmove.a-set-releases-a-removed-member-so-the-set-crosses -->
`Set.remove` releases the member it takes out, exactly as `Map.remove` does, so the set crosses while `main`
keeps reading the record it removed.
```maxon
type Cell implements Hashable, Equatable
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function hash() returns HashValue
		return n as HashValue
	end 'hash'

	function equals(other Self) returns bool
		return n == other.n
	end 'equals'
end 'Cell'

typealias Cells = Set with Cell

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s isolated Cells) returns Integer
		return s.count() as Integer
	end 'take'
end 'Svc'

function main() returns ExitCode
	let s = Cells.create() as isolated Cells
	let cell = Cell.create(1)
	var removed = false

	modify s 'fill'
		s.insert(freeze cell.clone())
		removed = s.remove(cell)
	end 'fill'

	let h = spawn Svc.create()
	let n = try await h.take(s) otherwise 0
	print("removed {removed}, the service holds {n}, main still reads {cell.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
removed true, the service holds 0, main still reads 1
```

<!-- test: deepmove.a-map-releases-a-removed-record-value-so-the-map-crosses -->
The same release for a record value: `main` keeps `key` and `cell` after `remove`, and the map crosses holding
only the entry it still owns.
```maxon
type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

typealias Cells = Map with (String, Cell)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m isolated Cells) returns Integer
		let ownedM = merge m

		let kept = try ownedM.get("kept 2") otherwise panic("the kept entry crossed with the map")
		return (ownedM.count() as Integer) + kept.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let m = Cells.create() as isolated Cells
	let key = "gone {1}"
	let cell = Cell.create(7)
	var removed = false

	modify m 'fill'
		m.upsert(key, value: cell.clone())
		m.upsert("kept {2}", value: Cell.create(8))
		removed = m.remove(key)
	end 'fill'

	let h = spawn Svc.create()
	let n = try await h.take(m) otherwise 0
	cell.n = cell.n + 1
	print("removed {removed}, the service read {n}, main still reads {key} and {cell.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
removed true, the service read 9, main still reads gone 1 and 8
```

<!-- test: deepmove.a-set-releases-one-of-two-members-so-the-set-crosses -->
A set that keeps a member after `remove` crosses with that member, and `main` keeps reading the one it removed.
```maxon
typealias Names = Set with String

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s isolated Names) returns Integer
		if s.contains("kept 2") 'kept'
			return s.count() as Integer
		end 'kept'

		return 0
	end 'take'
end 'Svc'

function main() returns ExitCode
	let s = Names.create() as isolated Names
	let gone = "gone {1}"
	var removed = false

	modify s 'fill'
		s.insert(gone)
		s.insert("kept {2}")
		removed = s.remove(gone)
	end 'fill'

	let h = spawn Svc.create()
	let n = try await h.take(s) otherwise 0
	print("removed {removed}, the service holds {n}, main still reads {gone}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
removed true, the service holds 1, main still reads gone 1
```

<!-- test: deepmove.a-map-whose-value-was-built-inside-the-upsert-crosses -->
A record built as the `value:` argument has no owner but the map, so the map crosses with it.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Cell'

typealias Cells = Map with (String, Cell)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m isolated Cells) returns Integer
		let ownedM = merge m

		let kept = try ownedM.get("kept 2") otherwise panic("the kept entry crossed with the map")
		return kept.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let m = Cells.create() as isolated Cells

	modify m 'fill'
		m.upsert("kept {2}", value: Cell.create())
	end 'fill'

	let h = spawn Svc.create()
	let n = try await h.take(m) otherwise 0
	print("crossed {n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
crossed 7
```

<!-- test: deepmove.a-union-payload-crosses-and-a-shared-one-aborts -->
A boxed union crosses under its TAG: the walk proves the box, reads the discriminant and descends only into
the LIVE case's managed payloads, so a variant that owns nothing costs one refcount test. The payload here is
the service's own to keep.
```maxon
union Shape
	empty
	labelled(text String)
end 'Shape'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s isolated Shape)
		match s 'k'
			empty then print("empty\n")
			labelled(t) then print("labelled {t}\n")
		end 'k'
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	try h.take(Shape.labelled("heap {1 + 1}")) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
labelled heap 2
```

<!-- test: deepmove.error.a-union-payload-with-a-second-owner-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

union Shape
	empty
	held(c Cell)
end 'Shape'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s isolated Shape)
		match s 'k'
			empty then print("empty\n")
			held(c) then print("held {c.n}\n")
		end 'k'
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cell = Cell.create()
	var s = Shape.held(cell)
	let h = spawn Svc.create()
	try h.take(s) otherwise panic("an open mailbox with room takes the send")
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:34:13: 's' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: deepmove.a-deep-graph-crosses-and-is-released-on-the-service -->
⭐ **THE GRAPH IS BUILT ON `main` AND RELEASED ON THE SERVICE, AND A GREEN THREAD'S STACK STARTS AT 2 KB.**
The handler reads one scalar and returns, so the only frames that release the chain are the per-type
cascade's own — `__destruct_S63` calling `__destruct_S62`, once per level. A cascade recurses to the depth
of the PROGRAM's types, which is why it carries the green-thread stack guard that grows and relocates the
stack rather than the exemption the compiler's fixed-depth runtime primitives take. `report` is a SECOND
message so that the line it prints cannot grow the stack ahead of the release: with the cascade exempt this
faults on the stack, with no output, on every run at this depth (`0xC0000005` on x64-windows).
```maxon
type S0
	export var n as Level

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'S0'

type S1
	export var n as Level
	export var inner as S0

	static function create() returns Self
		return Self{n: 1, inner: S0.create()}
	end 'create'
end 'S1'

type S2
	export var n as Level
	export var inner as S1

	static function create() returns Self
		return Self{n: 2, inner: S1.create()}
	end 'create'
end 'S2'

type S3
	export var n as Level
	export var inner as S2

	static function create() returns Self
		return Self{n: 3, inner: S2.create()}
	end 'create'
end 'S3'

type S4
	export var n as Level
	export var inner as S3

	static function create() returns Self
		return Self{n: 4, inner: S3.create()}
	end 'create'
end 'S4'

type S5
	export var n as Level
	export var inner as S4

	static function create() returns Self
		return Self{n: 5, inner: S4.create()}
	end 'create'
end 'S5'

type S6
	export var n as Level
	export var inner as S5

	static function create() returns Self
		return Self{n: 6, inner: S5.create()}
	end 'create'
end 'S6'

type S7
	export var n as Level
	export var inner as S6

	static function create() returns Self
		return Self{n: 7, inner: S6.create()}
	end 'create'
end 'S7'

type S8
	export var n as Level
	export var inner as S7

	static function create() returns Self
		return Self{n: 8, inner: S7.create()}
	end 'create'
end 'S8'

type S9
	export var n as Level
	export var inner as S8

	static function create() returns Self
		return Self{n: 9, inner: S8.create()}
	end 'create'
end 'S9'

type S10
	export var n as Level
	export var inner as S9

	static function create() returns Self
		return Self{n: 10, inner: S9.create()}
	end 'create'
end 'S10'

type S11
	export var n as Level
	export var inner as S10

	static function create() returns Self
		return Self{n: 11, inner: S10.create()}
	end 'create'
end 'S11'

type S12
	export var n as Level
	export var inner as S11

	static function create() returns Self
		return Self{n: 12, inner: S11.create()}
	end 'create'
end 'S12'

type S13
	export var n as Level
	export var inner as S12

	static function create() returns Self
		return Self{n: 13, inner: S12.create()}
	end 'create'
end 'S13'

type S14
	export var n as Level
	export var inner as S13

	static function create() returns Self
		return Self{n: 14, inner: S13.create()}
	end 'create'
end 'S14'

type S15
	export var n as Level
	export var inner as S14

	static function create() returns Self
		return Self{n: 15, inner: S14.create()}
	end 'create'
end 'S15'

type S16
	export var n as Level
	export var inner as S15

	static function create() returns Self
		return Self{n: 16, inner: S15.create()}
	end 'create'
end 'S16'

type S17
	export var n as Level
	export var inner as S16

	static function create() returns Self
		return Self{n: 17, inner: S16.create()}
	end 'create'
end 'S17'

type S18
	export var n as Level
	export var inner as S17

	static function create() returns Self
		return Self{n: 18, inner: S17.create()}
	end 'create'
end 'S18'

type S19
	export var n as Level
	export var inner as S18

	static function create() returns Self
		return Self{n: 19, inner: S18.create()}
	end 'create'
end 'S19'

type S20
	export var n as Level
	export var inner as S19

	static function create() returns Self
		return Self{n: 20, inner: S19.create()}
	end 'create'
end 'S20'

type S21
	export var n as Level
	export var inner as S20

	static function create() returns Self
		return Self{n: 21, inner: S20.create()}
	end 'create'
end 'S21'

type S22
	export var n as Level
	export var inner as S21

	static function create() returns Self
		return Self{n: 22, inner: S21.create()}
	end 'create'
end 'S22'

type S23
	export var n as Level
	export var inner as S22

	static function create() returns Self
		return Self{n: 23, inner: S22.create()}
	end 'create'
end 'S23'

type S24
	export var n as Level
	export var inner as S23

	static function create() returns Self
		return Self{n: 24, inner: S23.create()}
	end 'create'
end 'S24'

type S25
	export var n as Level
	export var inner as S24

	static function create() returns Self
		return Self{n: 25, inner: S24.create()}
	end 'create'
end 'S25'

type S26
	export var n as Level
	export var inner as S25

	static function create() returns Self
		return Self{n: 26, inner: S25.create()}
	end 'create'
end 'S26'

type S27
	export var n as Level
	export var inner as S26

	static function create() returns Self
		return Self{n: 27, inner: S26.create()}
	end 'create'
end 'S27'

type S28
	export var n as Level
	export var inner as S27

	static function create() returns Self
		return Self{n: 28, inner: S27.create()}
	end 'create'
end 'S28'

type S29
	export var n as Level
	export var inner as S28

	static function create() returns Self
		return Self{n: 29, inner: S28.create()}
	end 'create'
end 'S29'

type S30
	export var n as Level
	export var inner as S29

	static function create() returns Self
		return Self{n: 30, inner: S29.create()}
	end 'create'
end 'S30'

type S31
	export var n as Level
	export var inner as S30

	static function create() returns Self
		return Self{n: 31, inner: S30.create()}
	end 'create'
end 'S31'

type S32
	export var n as Level
	export var inner as S31

	static function create() returns Self
		return Self{n: 32, inner: S31.create()}
	end 'create'
end 'S32'

type S33
	export var n as Level
	export var inner as S32

	static function create() returns Self
		return Self{n: 33, inner: S32.create()}
	end 'create'
end 'S33'

type S34
	export var n as Level
	export var inner as S33

	static function create() returns Self
		return Self{n: 34, inner: S33.create()}
	end 'create'
end 'S34'

type S35
	export var n as Level
	export var inner as S34

	static function create() returns Self
		return Self{n: 35, inner: S34.create()}
	end 'create'
end 'S35'

type S36
	export var n as Level
	export var inner as S35

	static function create() returns Self
		return Self{n: 36, inner: S35.create()}
	end 'create'
end 'S36'

type S37
	export var n as Level
	export var inner as S36

	static function create() returns Self
		return Self{n: 37, inner: S36.create()}
	end 'create'
end 'S37'

type S38
	export var n as Level
	export var inner as S37

	static function create() returns Self
		return Self{n: 38, inner: S37.create()}
	end 'create'
end 'S38'

type S39
	export var n as Level
	export var inner as S38

	static function create() returns Self
		return Self{n: 39, inner: S38.create()}
	end 'create'
end 'S39'

type S40
	export var n as Level
	export var inner as S39

	static function create() returns Self
		return Self{n: 40, inner: S39.create()}
	end 'create'
end 'S40'

type S41
	export var n as Level
	export var inner as S40

	static function create() returns Self
		return Self{n: 41, inner: S40.create()}
	end 'create'
end 'S41'

type S42
	export var n as Level
	export var inner as S41

	static function create() returns Self
		return Self{n: 42, inner: S41.create()}
	end 'create'
end 'S42'

type S43
	export var n as Level
	export var inner as S42

	static function create() returns Self
		return Self{n: 43, inner: S42.create()}
	end 'create'
end 'S43'

type S44
	export var n as Level
	export var inner as S43

	static function create() returns Self
		return Self{n: 44, inner: S43.create()}
	end 'create'
end 'S44'

type S45
	export var n as Level
	export var inner as S44

	static function create() returns Self
		return Self{n: 45, inner: S44.create()}
	end 'create'
end 'S45'

type S46
	export var n as Level
	export var inner as S45

	static function create() returns Self
		return Self{n: 46, inner: S45.create()}
	end 'create'
end 'S46'

type S47
	export var n as Level
	export var inner as S46

	static function create() returns Self
		return Self{n: 47, inner: S46.create()}
	end 'create'
end 'S47'

type S48
	export var n as Level
	export var inner as S47

	static function create() returns Self
		return Self{n: 48, inner: S47.create()}
	end 'create'
end 'S48'

type S49
	export var n as Level
	export var inner as S48

	static function create() returns Self
		return Self{n: 49, inner: S48.create()}
	end 'create'
end 'S49'

type S50
	export var n as Level
	export var inner as S49

	static function create() returns Self
		return Self{n: 50, inner: S49.create()}
	end 'create'
end 'S50'

type S51
	export var n as Level
	export var inner as S50

	static function create() returns Self
		return Self{n: 51, inner: S50.create()}
	end 'create'
end 'S51'

type S52
	export var n as Level
	export var inner as S51

	static function create() returns Self
		return Self{n: 52, inner: S51.create()}
	end 'create'
end 'S52'

type S53
	export var n as Level
	export var inner as S52

	static function create() returns Self
		return Self{n: 53, inner: S52.create()}
	end 'create'
end 'S53'

type S54
	export var n as Level
	export var inner as S53

	static function create() returns Self
		return Self{n: 54, inner: S53.create()}
	end 'create'
end 'S54'

type S55
	export var n as Level
	export var inner as S54

	static function create() returns Self
		return Self{n: 55, inner: S54.create()}
	end 'create'
end 'S55'

type S56
	export var n as Level
	export var inner as S55

	static function create() returns Self
		return Self{n: 56, inner: S55.create()}
	end 'create'
end 'S56'

type S57
	export var n as Level
	export var inner as S56

	static function create() returns Self
		return Self{n: 57, inner: S56.create()}
	end 'create'
end 'S57'

type S58
	export var n as Level
	export var inner as S57

	static function create() returns Self
		return Self{n: 58, inner: S57.create()}
	end 'create'
end 'S58'

type S59
	export var n as Level
	export var inner as S58

	static function create() returns Self
		return Self{n: 59, inner: S58.create()}
	end 'create'
end 'S59'

type S60
	export var n as Level
	export var inner as S59

	static function create() returns Self
		return Self{n: 60, inner: S59.create()}
	end 'create'
end 'S60'

type S61
	export var n as Level
	export var inner as S60

	static function create() returns Self
		return Self{n: 61, inner: S60.create()}
	end 'create'
end 'S61'

type S62
	export var n as Level
	export var inner as S61

	static function create() returns Self
		return Self{n: 62, inner: S61.create()}
	end 'create'
end 'S62'

type S63
	export var n as Level
	export var inner as S62

	static function create() returns Self
		return Self{n: 63, inner: S62.create()}
	end 'create'
end 'S63'

type Sink
	var seen as Level

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(root isolated S63)
		let ownedRoot = merge root

		self.seen = self.seen + ownedRoot.n
	end 'take'

	export function report()
		print("released {self.seen}\n")
	end 'report'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	try h.take(S63.create()) otherwise panic("an open mailbox with room takes the send")
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Level = int(0 to 1024)
```
```exitcode
0
```
```stdout
released 63
```

<!-- test: deepmove.dag.a-record-held-by-two-fields-crosses -->
A moved graph may reach one record twice. `Pair.create` puts its one `Bag` in both fields, so the bag's count
is 2 and both owners are inside the graph: nothing outside it can write the bag once it crosses, and the walk
counts the owners it meets rather than demanding a count of 1. The bag is descended once, so its `CellArray`
is proved once however many paths reach it. The service's write through `left` is what `right` reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		let cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{n: 1, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function create() returns Self
		let bag = Bag.create()
		return Self{left: bag, right: bag}
	end 'create'
end 'Pair'

type Svc
	var held as Pair

	static function create(held isolated Pair) returns Self
		return Self{held: merge held}
	end 'create'

	export function report()
		self.held.left.n = 5
		print("{self.held.right.n} {self.held.right.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(Pair.create())
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
5 2
```

<!-- test: deepmove.dag.an-array-holding-one-record-twice-crosses -->
The same inside a container: both slots hold one `Cell`, the array is that cell's only other owner, and the
service's write through slot 0 is what slot 1 reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

function oneCellTwice() returns CellArray
	let cs = CellArray.create(0)
	let c = Cell.create()
	cs.push(c)
	cs.push(c)
	return cs
end 'oneCellTwice'

type Svc
	var cells as CellArray

	static function create(cells isolated CellArray) returns Self
		return Self{cells: merge cells}
	end 'create'

	export function report()
		let first = try self.cells.get(0) otherwise panic("two cells were pushed")
		first.n = 7
		let second = try self.cells.get(1) otherwise panic("two cells were pushed")
		print("{second.n}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellTwice())
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
7
```

<!-- test: deepmove.dag.a-union-payload-shared-with-a-sibling-field-crosses -->
A union payload and a sibling field may be one record: the walk meets the `Cell` once under the live case and
once under the field, both inside the graph, so the frame crosses and the service's write through the field
is what the payload reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

union Shape
	empty
	held(c Cell)
end 'Shape'

type Frame
	export var shape as Shape
	export var cell as Cell

	static function create() returns Self
		let cell = Cell.create()
		return Self{shape: Shape.held(cell), cell: cell}
	end 'create'
end 'Frame'

type Svc
	var frame as Frame

	static function create(frame isolated Frame) returns Self
		return Self{frame: merge frame}
	end 'create'

	export function report()
		self.frame.cell.n = 7
		match self.frame.shape 'k'
			empty then print("empty\n")
			held(c) then print("held {c.n}\n")
		end 'k'
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(Frame.create())
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
held 7
```

<!-- test: deepmove.dag.a-reply-holding-one-record-twice-crosses -->
The reply road admits the same shape: the handler mints a `Pair` whose two fields hold one `Bag`, and the
awaiter's write through `left` is what `right` reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		let cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{n: 1, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function create() returns Self
		let bag = Bag.create()
		return Self{left: bag, right: bag}
	end 'create'
end 'Pair'

type Maker
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function make() returns isolated Pair
		return Pair.create()
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create()
	let p = try await h.make() otherwise Pair.create()
	p.left.n = 5
	print("{p.right.n} {p.right.cells.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
5 2
```

<!-- test: deepmove.dag.an-outside-owner-of-a-record-reached-twice-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		var cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{n: 1, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function around(bag Bag) returns Self
		return Self{left: bag, right: bag}
	end 'around'
end 'Pair'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(p isolated Pair)
		print("svc {p.right.n}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var bag = Bag.create()
	let h = spawn Svc.create()
	try h.take(Pair.around(bag)) otherwise panic("an open mailbox with room takes the send")
	return bag.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:48:13: 'Pair.around(bag)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: deepmove.dag.a-string-and-its-bytes-cross-together -->
A move meets a view's reference to the String record its bytes live in before it detaches the view, so a moved
`Holder` owning `text` and a view of it crosses: both of the record's owners are inside the graph.
```maxon
type Holder
	export let text as String
	export let bytes as ByteArray

	static function around(text String) returns Self
		return Self{text: text, bytes: text.toByteArray()}
	end 'around'
end 'Holder'

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function read(h isolated Holder) returns Integer
		return h.text.byteLength() + (h.bytes.count() as BytePos)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}") as isolated Holder
	let n = try await r.read(holder) otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=20
```

<!-- test: deepmove.dag.a-map-holding-one-record-under-two-keys-crosses -->
A `Map`'s value column may hold one record under two keys: both owners are slots of the moved map, so it
crosses, and the service's write through one key is what the other reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias Cells = Map with (String, Cell)

function oneCellUnderTwoKeys() returns Cells
	let m = Cells.create()
	let c = Cell.create()
	m.upsert("a", value: c)
	m.upsert("b", value: c)
	return m
end 'oneCellUnderTwoKeys'

type Svc
	var cells as Cells

	static function create(cells isolated Cells) returns Self
		return Self{cells: merge cells}
	end 'create'

	export function report()
		let a = try self.cells.get("a") otherwise panic("a was inserted")
		a.n = 7
		let b = try self.cells.get("b") otherwise panic("b was inserted")
		print("{b.n}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellUnderTwoKeys())
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
7
```

<!-- test: deepmove.dag.a-chain-holding-one-record-twice-crosses -->
The chain's walk admits the same shape: two nodes hold one `Cell`, the chain is its only owner, and the
service's write through the first node is what the second reads.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias Cells = List with Cell

function oneCellTwice() returns Cells
	let xs = Cells.create()
	let c = Cell.create()
	xs.append(c)
	xs.append(c)
	return xs
end 'oneCellTwice'

type Svc
	var cells as Cells

	static function create(cells isolated Cells) returns Self
		return Self{cells: merge cells}
	end 'create'

	export function report()
		let first = try self.cells.first() otherwise panic("two cells were appended")
		first.n = 7

		for c in self.cells 'each'
			print("{c.n}\n")
		end 'each'
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellTwice())
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
7
7
```

<!-- test: deepmove.dag.many-records-each-reached-twice-cross -->
Forty records, each held by two slots of one array: every one of them is met twice, and the graph crosses.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

function pairedCells(pairs Integer) returns CellArray
	let cs = CellArray.create(0)

	for _ in 1 to pairs 'each'
		let c = Cell.create()
		cs.push(c)
		cs.push(c)
	end 'each'

	return cs
end 'pairedCells'

type Svc
	var cells as CellArray

	static function create(cells isolated CellArray) returns Self
		return Self{cells: merge cells}
	end 'create'

	export function report()
		var sum = 0 as Integer

		for c in self.cells 'each'
			sum = sum + c.n
		end 'each'

		print("{self.cells.count()} slots sum to {sum}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(pairedCells(40))
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
80 slots sum to 80
```

<!-- test: deepmove.dag.an-outside-owner-among-many-records-reached-twice-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

function pairedCellsAround(kept Cell, pairs Integer) returns CellArray
	var cs = CellArray.create(0)

	for _ in 1 to pairs 'each'
		var c = Cell.create()
		cs.push(c)
		cs.push(c)
	end 'each'

	cs.push(kept)
	cs.push(kept)
	return cs
end 'pairedCellsAround'

type Svc
	var cells as CellArray

	static function create(cells isolated CellArray) returns Self
		return Self{cells: cells}
	end 'create'

	export function report()
		print("svc {self.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	var kept = Cell.create()
	let h = spawn Svc.create(pairedCellsAround(kept, pairs: 40))
	try h.report() otherwise panic("an open mailbox with room takes the send")
	return kept.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3196: <fragment>:30:22: cannot store an isolated 'CellArray' into 'Svc.cells', which holds a mutable 'CellArray'; `merge` it first
error E3200: <fragment>:40:27: 'pairedCellsAround(kept, pairs: 40)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: borrow.error.a-let-string-read-after-its-awaited-send-is-use-after-move -->
A `let` local handed to a message is MOVED: the service becomes its owner, and the sender's binding is gone
even after the reply it awaited.
```maxon
type Meter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function measure(s String) returns Integer
		return s.count() as Integer
	end 'measure'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	let buf = "hello {1}"
	let n = try await h.measure(buf) otherwise 0
	print("service read {n} characters\n")
	print("sender still reads {buf}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:19:29: use of moved value 'buf': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.a-cloned-let-string-is-sent-while-the-sender-keeps-its-source -->
A clone bound to a `let` is moved by the send, and its walk meets a record of its own: the source it was
copied from is not part of the graph, so `main` may go on writing it after the send.
```maxon
type Meter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function measure(s String) returns Integer
		return s.byteLength() as Integer
	end 'measure'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	var src = "payload {42}"
	let part = src.clone()
	let n = try await h.measure(part) otherwise 0
	src.append("!")
	print("service read {n} bytes, sender wrote {src}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service read 10 bytes, sender wrote payload 42!
```

<!-- test: borrow.a-record-clone-bound-to-a-let-or-a-var-is-sent-while-the-sender-keeps-its-source -->
A `.clone()` of a record holding an array of records and a map, bound to a `let` or a `var` before the send,
is a graph of its own exactly as the same clone written inside the call is, so the send moves it. The second
copy's map is empty.
```maxon
type Leaf
	export let name as String
	export let weight as Integer

	static function create(name String, weight Integer) returns Self
		return Self{name: name, weight: weight}
	end 'create'
end 'Leaf'

typealias Leaves = Array with Leaf
typealias LeafIndex = Map with (String, Leaf)

type Shelf
	export let leaves as Leaves
	export let byName as LeafIndex

	static function create(leaves Leaves, byName LeafIndex) returns Self
		return Self{leaves: leaves, byName: byName}
	end 'create'

	function weight() returns Integer
		var sum = 0 as Integer

		for leaf in self.leaves 'eachLeaf'
			sum = sum + leaf.weight * (leaf.name.byteLength() as Integer)
		end 'eachLeaf'

		for (key, leaf) in self.byName 'eachEntry'
			sum = sum + leaf.weight * (key.byteLength() as Integer)
		end 'eachEntry'

		return sum
	end 'weight'
end 'Shelf'

type Keeper
	let shelf as Shelf

	static function create(shelf isolated Shelf) returns Self
		return Self{shelf: merge shelf}
	end 'create'

	export function weight() returns Integer
		return self.shelf.weight()
	end 'weight'
end 'Keeper'

function shelf(indexed bool) returns Shelf
	let leaves = Leaves.create(0)
	let byName = LeafIndex.create()
	leaves.push(Leaf.create("a", weight: 1))
	leaves.push(Leaf.create("bb", weight: 2))

	if indexed 'index'
		byName.upsert("ccc", value: Leaf.create("c", weight: 4))
	end 'index'

	return Shelf.create(leaves, byName: byName)
end 'shelf'

function main() returns ExitCode
	let original = shelf(true)
	let copy = original.clone() as isolated Shelf
	let first = spawn Keeper.create(copy)
	let firstSaw = try await first.weight() otherwise 0
	print("let copy: service {firstSaw}, sender {original.weight()}\n")

	let unindexed = shelf(false)
	let moved = unindexed.clone() as isolated Shelf
	let second = spawn Keeper.create(moved)
	let secondSaw = try await second.weight() otherwise 0
	print("var copy: service {secondSaw}, sender {unindexed.weight()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
let copy: service 17, sender 17
var copy: service 5, sender 5
```

<!-- test: borrow.a-let-string-whose-bytes-the-sender-still-views-crosses-the-send -->
A `toByteArray()` view of an owned `String` counts the String's own record, because its bytes are inline. The
send turns that count shared, so the service's release and the view's release step one atomic count
(`deepmove.a-string-whose-bytes-the-sender-still-views-crosses-the-send`).
```maxon
type Meter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function measure(s String) returns Integer
		return s.byteLength() as Integer
	end 'measure'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	let s = "payload {42}"
	let bytes = s.toByteArray()
	let n = try await h.measure(s) otherwise 0
	print("service {n} view {bytes.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service 10 view 10
```

<!-- test: borrow.two-services-and-the-sender-each-read-a-copy-of-one-graph -->
One `let` graph — a record holding a `String`, an array of records and a union with a managed payload — is
cloned into a send to each of two services, and all three green threads read their own copy while both
replies are outstanding.
```maxon
type Tag
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Tag'

typealias Tags = Array with Tag

union Note
	blank
	written(body String)
end 'Note'

type Doc
	export var title as String
	export var tags as Tags
	export var note as Note

	static function create(k Integer) returns Self
		let tags = Tags.create(0)
		tags.push(Tag.create("red {k}"))
		tags.push(Tag.create("green {k}"))
		tags.push(Tag.create("blue {k}"))
		return Self{title: "doc {k}", tags: tags, note: Note.written("body {k}")}
	end 'create'
end 'Doc'

function noteLength(n Note) returns Integer
	return match n 'length'
		blank gives 0
		written(body) gives body.count() as Integer
	end 'length'
end 'noteLength'

function digest(d Doc) returns Integer
	var total = d.title.count() as Integer
	for t in d.tags 'each'
		total = total + (t.text.count() as Integer)
	end 'each'
	return total + noteLength(d.note)
end 'digest'

type Reader
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function read(d isolated Doc) returns Integer
		return self.id * 1000 + digest(d)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let one = spawn Reader.create(1)
	let two = spawn Reader.create(2)
	let doc = Doc.create(7)
	let first = try one.read(doc.clone()) otherwise panic("an open mailbox with room takes the send")
	let second = try two.read(doc.clone()) otherwise panic("an open mailbox with room takes the send")
	let mine = digest(doc)
	let a = await first
	let b = await second
	print("first {a}\n")
	print("second {b}\n")
	print("sender {mine}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
first 1029
second 2029
sender 29
```

<!-- test: borrow.one-graph-cloned-into-many-sends-to-one-service -->
A clone of the same `let` graph sent 200 times to one service: every queued message owns its copy, and the
mailbox's FIFO puts `report` behind the last of them. `main` reads its own graph once the tallies are in.
```maxon
type Leaf
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Leaf'

typealias Leaves = Array with Leaf

type Tree
	export var name as String
	export var leaves as Leaves

	static function create(size Integer) returns Self
		let leaves = Leaves.create(0)
		for i in 1 to size 'grow'
			leaves.push(Leaf.create("leaf {i}"))
		end 'grow'
		return Self{name: "tree {size}", leaves: leaves}
	end 'create'
end 'Tree'

type Tally
	var sends as Integer
	var letters as Integer

	static function create() returns Self
		return Self{sends: 0, letters: 0}
	end 'create'

	export function take(t isolated Tree)
		var read = t.name.count() as Integer
		for leaf in t.leaves 'each'
			read = read + (leaf.text.count() as Integer)
		end 'each'
		self.sends = self.sends + 1
		self.letters = self.letters + read
	end 'take'

	export function report() returns String
		return "sends={self.sends} letters={self.letters}"
	end 'report'
end 'Tally'

function main() returns ExitCode
	let h = spawn Tally.create()
	let tree = Tree.create(4)
	for _ in 1 to 200 'send'
		try h.take(tree.clone()) otherwise panic("an open mailbox with room takes the send")
	end 'send'
	let report = try await h.report() otherwise "gone"
	print("{report}\n")
	print("sender still reads {tree.name} with {tree.leaves.count()} leaves\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
sends=200 letters=6000
sender still reads tree 4 with 4 leaves
```

<!-- test: borrow.the-sender-releases-first-and-the-service-frees-the-graph -->
<!-- procs: 1 -->
<!-- preempt: off -->
`sendAndReturn` sends its graph and returns while the message is still queued, so the queued message is the
graph's last owner and the service's release is the one that frees it — a release that never comes is
exit 101. The two pins make *still queued* the premise rather than luck: on one processor that `main` is
never preempted from, nothing runs on the service until `main` parks at its await.
```maxon
type Leaf
	export var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'
end 'Leaf'

typealias Leaves = Array with Leaf

type Tree
	export var name as String
	export var leaves as Leaves

	static function create(size Integer) returns Self
		let leaves = Leaves.create(0)
		for i in 1 to size 'grow'
			leaves.push(Leaf.create("leaf {i}"))
		end 'grow'
		return Self{name: "tree {size}", leaves: leaves}
	end 'create'
end 'Tree'

type Keeper
	var seen as String

	static function create() returns Self
		return Self{seen: "nothing"}
	end 'create'

	export function take(t isolated Tree)
		let ownedT = merge t

		let first = try ownedT.leaves.get(0) otherwise panic("a tree has leaves")
		self.seen = "{ownedT.name} starting with {first.text}"
	end 'take'

	export function report() returns String
		return "service read {self.seen}"
	end 'report'
end 'Keeper'

function sendAndReturn(h Keeper.handle)
	let tree = Tree.create(3) as isolated Tree
	print("sender read {tree.name}\n")
	try h.take(tree) otherwise panic("an open mailbox with room takes the send")
end 'sendAndReturn'

function main() returns ExitCode
	let h = spawn Keeper.create()
	sendAndReturn(h)
	let report = try await h.report() otherwise "gone"
	print("{report}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
sender read tree 3
service read tree 3 starting with leaf 1
```

<!-- test: borrow.a-handler-may-write-a-copy-of-a-record-inside-its-sent-parameter-through-a-self-writing-method -->
`copy` is the handler's own record, so `bump` writes it whatever the send moved.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function create() returns Self
		return Self{inner: Box.create()}
	end 'create'
end 'Holder'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(target isolated Holder)
		let copy = target.inner.clone()
		copy.bump()
		print("{copy.n}\n")
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create() as isolated Holder
	try h.poke(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
2
```

<!-- test: borrow.dag.a-graph-reaching-one-record-twice-is-sent -->
A sent graph may reach one record through two fields: the second owner is inside the graph the service
owns, not outside it, so the send's walk admits it.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		let cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{n: 3, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function create() returns Self
		let bag = Bag.create()
		return Self{left: bag, right: bag}
	end 'create'
end 'Pair'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function look(target isolated Pair) returns Integer
		print("service reads {target.right.n}\n")
		return target.right.cells.count() as Integer
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create() as isolated Pair
	let cells = try await h.look(p) otherwise 0
	print("service counted {cells} cells\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service reads 3
service counted 2 cells
```

<!-- test: borrow.dag.a-graph-reaching-one-record-twice-is-sent-twice -->
A clone of that graph is sent first and the graph itself second; each send's walk admits what it meets.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		let cells = CellArray.create(0)
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{n: 3, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function create() returns Self
		let bag = Bag.create()
		return Self{left: bag, right: bag}
	end 'create'
end 'Pair'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function look(target isolated Pair) returns Integer
		print("service reads {target.right.n}\n")
		return target.right.cells.count() as Integer
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create() as isolated Pair
	let first = try await h.look(p.clone()) otherwise 0
	let second = try await h.look(p) otherwise 0
	print("service counted {first + second} cells\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service reads 3
service reads 3
service counted 4 cells
```

<!-- test: borrow.dag.an-array-holding-one-record-twice-is-sent -->
The container's walk meets the one `Cell` in both slots and counts the second slot against its owners.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Shelf
	export var cells as CellArray

	static function create() returns Self
		let cells = CellArray.create(0)
		let c = Cell.create()
		cells.push(c)
		cells.push(c)
		return Self{cells: cells}
	end 'create'
end 'Shelf'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function look(shelf isolated Shelf) returns Integer
		let ownedShelf = merge shelf

		let second = try ownedShelf.cells.get(1) otherwise panic("two cells were pushed")
		return second.n
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let shelf = Shelf.create() as isolated Shelf
	let n = try await h.look(shelf) otherwise 0
	print("service read {n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service read 1
```

<!-- test: borrow.dag.an-outside-owner-of-a-record-reached-twice-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

type Bag
	export var n as Integer
	export var cells as CellArray

	static function create() returns Self
		var cells = CellArray.create(0)
		cells.push(Cell.create())
		return Self{n: 3, cells: cells}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function around(bag Bag) returns Self
		return Self{left: bag, right: bag}
	end 'around'
end 'Pair'

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function look(target isolated Pair) returns Integer
		return target.right.n
	end 'look'
end 'Svc'

function main() returns ExitCode
	let bag = Bag.create()
	let h = spawn Svc.create()
	let p = Pair.around(bag)
	let n = try await h.look(p) otherwise 0
	print("{n} {bag.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3200: <fragment>:48:27: 'p' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: borrow.dag.bytes-and-their-string-are-sent-together -->
An owned `String` keeps its bytes inline, so a `toByteArray()` view holds a reference to the String's own record.
`Holder` declares the view before `text`, so the walk meets the record through the view before it meets it
through `text`, and counts both references: both owners are inside the sent graph, and the graph crosses.
```maxon
type Holder
	export let bytes as ByteArray
	export let text as String

	static function around(text String) returns Self
		return Self{bytes: text.toByteArray(), text: text}
	end 'around'
end 'Holder'

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function read(h isolated Holder) returns Integer
		return h.text.byteLength() + (h.bytes.count() as BytePos)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}") as isolated Holder
	let n = try await r.read(holder) otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=20
```

<!-- test: borrow.dag.an-outside-owner-of-a-viewed-string-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Holder
	export let text as String
	export let bytes as ByteArray

	static function around(text String) returns Self
		return Self{text: text, bytes: text.toByteArray()}
	end 'around'
end 'Holder'

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function read(h isolated Holder) returns Integer
		return h.text.byteLength() + (h.bytes.count() as BytePos)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	var kept = TextArray.create(0)
	kept.push(holder.text)
	let n = try await r.read(holder) otherwise 0
	print("n={n} kept={kept.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias TextArray = Array with String
```
```maxoncstderr
error E3200: <fragment>:28:27: 'holder' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: borrow.dag.an-outside-owner-of-a-string-its-bytes-reach-first-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
type Holder
	export let bytes as ByteArray
	export let text as String

	static function around(text String) returns Self
		return Self{bytes: text.toByteArray(), text: text}
	end 'around'
end 'Holder'

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function read(h isolated Holder) returns Integer
		return h.text.byteLength() + (h.bytes.count() as BytePos)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	var kept = TextArray.create(0)
	kept.push(holder.text)
	let n = try await r.read(holder) otherwise 0
	print("n={n} kept={kept.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias TextArray = Array with String
```
```maxoncstderr
error E3200: <fragment>:28:27: 'holder' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: borrow.a-sent-handle-may-be-kept-by-the-handler -->
A service handle may be kept by the handler it is sent to: `Relay` keeps a clone of `counter` in its state and
sends through it in a later message, while `main` goes on sending through its own binding. `Relay` posts its
send and answers without waiting for the counter's reply, and `relay.forward` is answered before `main`'s own
send goes out, so the counter has `5` in its mailbox ahead of `main`'s `2` and `main`'s reply is `7`.
```maxon
type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump(by Integer) returns Integer
		self.n = self.n + by
		return self.n
	end 'bump'
end 'Counter'

typealias CounterHandles = Array with Counter.handle

type Relay
	var peers as CounterHandles

	static function create() returns Self
		return Self{peers: CounterHandles.create(0)}
	end 'create'

	export function adopt(peer Counter.handle)
		self.peers.push(peer)
	end 'adopt'

	export function forward(by Integer) returns Integer
		var sent = 0 as Integer
		for peer in self.peers 'each'
			_ = try peer.bump(by) otherwise panic("an open mailbox with room takes the send")
			sent = sent + 1
		end 'each'
		return sent
	end 'forward'
end 'Relay'

function main() returns ExitCode
	let counter = spawn Counter.create()
	let relay = spawn Relay.create()
	try relay.adopt(counter.clone()) otherwise panic("an open mailbox with room takes the send")
	let viaRelay = try await relay.forward(5) otherwise 0
	print("relay sent {viaRelay}\n")
	let direct = try await counter.bump(2) otherwise 0
	print("direct {direct}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
relay sent 1
direct 7
```

<!-- test: borrow.a-door-on-a-sibling-arm-does-not-follow-the-send -->
A door on the OTHER arm of the `match` whose arm sent the graph does not follow the send: no path runs both, so
`route` may hand `b` to `stash`, which keeps it, on the arm that did not send it.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

enum Route
	toService
	toArray
end 'Route'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'

	export function total() returns Integer
		return self.seen
	end 'total'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function route(h Svc.handle, xs Boxes, n Integer, way Route)
	let b = Box.create(n) as isolated Box
	match way 'send'
		toService then try h.take(b) otherwise panic("an open mailbox with room takes the send")
		toArray then stash(xs, item: merge b)
	end 'send'
end 'route'

function main() returns ExitCode
	let h = spawn Svc.create()
	let xs = Boxes.create(0)
	route(h, xs: xs, n: 2, way: Route.toService)
	route(h, xs: xs, n: 5, way: Route.toArray)
	let seen = try await h.total() otherwise 0
	let kept = try xs.get(0) otherwise panic("nothing was kept")
	print("the service saw {seen}\n")
	print("kept {xs.count()} holding {kept.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
the service saw 2
kept 1 holding 5
```

<!-- test: borrow.error.a-door-after-the-branch-that-sent-follows-the-send -->
A door after the `match` follows the send on the arm that sent: a path runs the send and then `stash`, which
reads `b` after it moved.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

enum Route
	toService
	toArray
end 'Route'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function route(h Svc.handle, xs Boxes, n Integer, way Route)
	let b = Box.create(n) as isolated Box
	match way 'send'
		toService then try h.take(b) otherwise panic("an open mailbox with room takes the send")
		toArray then print("kept, not sent\n")
	end 'send'
	stash(xs, item: merge b)
end 'route'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create(0)
	route(h, xs: xs, n: 2, way: Route.toService)
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:41:24: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-door-on-a-sibling-arm-inside-a-loop-follows-the-send -->
Inside a loop a door on the sibling arm of the send follows it, though the text places it first: the next trip
takes the other arm while `b`, bound before the loop, has already moved.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

enum Route
	toService
	toArray
end 'Route'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function routeFor(trip Integer) returns Route
	return Route.toService if trip == 0 else Route.toArray
end 'routeFor'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create(0)
	let b = Box.create(3) as isolated Box
	var trip = 0 as Integer
	while trip < 2 'twice'
		match routeFor(trip) 'send'
			toArray then stash(xs, item: merge b)
			toService then try h.take(b) otherwise panic("an open mailbox with room takes the send")
		end 'send'
		trip = trip + 1
	end 'twice'
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:46:39: use of moved value 'b': it was moved in an earlier iteration of this loop
```

<!-- test: borrow.a-let-rebound-each-trip-is-sent-afresh -->
The loop case above with `b` bound INSIDE the loop: the next trip binds a fresh record, so the door the back edge
reaches is not the binding the send moved and `stash` may keep it.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

enum Route
	toService
	toArray
end 'Route'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'

	export function total() returns Integer
		return self.seen
	end 'total'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function routeFor(trip Integer) returns Route
	return Route.toService if trip mod 2 == 0 else Route.toArray
end 'routeFor'

function main() returns ExitCode
	let h = spawn Svc.create()
	let xs = Boxes.create(0)
	var trip = 0 as Integer
	while trip < 4 'eachTrip'
		let b = Box.create(trip + 1) as isolated Box
		match routeFor(trip) 'send'
			toArray then stash(xs, item: merge b)
			toService then try h.take(b) otherwise panic("an open mailbox with room takes the send")
		end 'send'
		trip = trip + 1
	end 'eachTrip'

	let seen = try await h.total() otherwise 0
	var kept = 0 as Integer
	for box in xs 'sum'
		kept = kept + box.n
	end 'sum'

	print("the service saw {seen}\n")
	print("kept {xs.count()} totalling {kept}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
the service saw 4
kept 2 totalling 6
```

<!-- test: borrow.error.a-sent-let-may-not-be-bound-to-a-var -->
From the send on, a sent `let` is gone: binding it to a `var` reads a moved value.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	var m = b
	m.n = 2
	return m.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:28:10: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-assigned-to-a-var -->
Assigning a sent `let` to an existing `var` reads it after the move, as binding one does.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var m = Box.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	m = b
	m.n = 2
	return m.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:29:6: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-value-read-out-of-a-sent-let-is-use-after-move -->
Reading a field out of a sent binding reads the moved binding: `p.inner` names `p`, which the send took.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Pair
	export let inner as Box

	static function create(inner Box) returns Self
		return Self{inner: inner}
	end 'create'
end 'Pair'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(p isolated Pair)
		let ownedP = merge p

		self.seen = self.seen + ownedP.inner.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create(Box.create()) as isolated Pair
	try h.take(p) otherwise panic("an open mailbox with room takes the send")
	var y = p.inner
	y.n = 2
	return y.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:36:10: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-stored-in-a-field -->
Storing a sent value in a field of a mutable record reads it after the move.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Holder
	export var item as Box

	static function create() returns Self
		return Self{item: Box.create()}
	end 'create'
end 'Holder'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var holder = Holder.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	holder.item = b
	return holder.item.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:37:16: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-wrapped-in-a-new-record -->
Constructing a record around a sent value reads it after the move, exactly as a field assignment does. The
record here is a union payload, because a struct literal is legal only inside its own type.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

union Wrap
	empty
	held(b Box)
end 'Wrap'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	let w = Wrap.held(b)
	print("{w.name}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:33:20: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-stored-in-a-container -->
Pushing a sent value into a container reads it after the move.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create(0)
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	xs.push(b)
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:31:10: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-returned -->
Returning a sent value hands back a binding the send already took.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function sendAndKeep() returns Box
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	return b
end 'sendAndKeep'

function main() returns ExitCode
	let b = sendAndKeep()
	return b.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:28:9: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.error.a-sent-let-may-not-be-captured-by-a-closure -->
A closure that captures a sent value reads it after the move.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("an open mailbox with room takes the send")
	let peek = function() gives b.n
	return peek() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:28:30: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: borrow.a-handler-may-keep-a-sent-parameter -->
The service owns what a send moves to it, so `keep` may push its parameter into the service's state.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Svc
	var kept as Boxes

	static function create() returns Self
		return Self{kept: Boxes.create(0)}
	end 'create'

	export function keep(given isolated Box)
		self.kept.push(merge given)
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.keep(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-keep-what-it-reads-out-of-a-sent-parameter -->
What a handler reads out of a sent parameter is part of the graph it owns, so it may keep `p.inner`.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Pair
	export let inner as Box

	static function create(inner Box) returns Self
		return Self{inner: inner}
	end 'create'
end 'Pair'

typealias Boxes = Array with Box

type Svc
	var kept as Boxes

	static function create() returns Self
		return Self{kept: Boxes.create(0)}
	end 'create'

	export function keep(p isolated Pair)
		let ownedP = merge p

		self.kept.push(ownedP.inner)
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let pair = Pair.create(Box.create()) as isolated Pair
	try h.keep(pair) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-let-sent-to-a-handler-that-writes-it -->
A handler may write a parameter a send moved to it: the sender's binding is gone, so nothing else sees the
record.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump(target isolated Box)
		target.n = target.n + 1
	end 'bump'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.bump(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-write-its-sent-parameter-through-a-self-writing-method -->
A method that writes its own receiver may write a parameter the send moved to the handler.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(target isolated Box)
		target.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.poke(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-write-a-record-inside-its-sent-parameter-through-a-self-writing-method -->
The moved graph is everything the parameter reaches, so the handler may write `target.inner` too.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function create() returns Self
		return Self{inner: Box.create()}
	end 'create'
end 'Holder'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(target isolated Holder)
		target.inner.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create() as isolated Holder
	try h.poke(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-write-a-record-inside-its-sent-parameter-one-frame-down-through-a-self-writing-method -->
The same write one frame down: `nudge` writes what the handler was sent.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Holder
	export var inner as Box

	static function create() returns Self
		return Self{inner: Box.create()}
	end 'create'
end 'Holder'

function nudge(t Holder)
	t.inner.bump()
end 'nudge'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(target isolated Holder)
		nudge(target)
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create() as isolated Holder
	try h.poke(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-push-into-a-container-a-call-hands-back-from-inside-its-sent-parameter -->
`pick` hands back the array `target` holds, and the handler may push into it: the send moved the whole graph.
```maxon
typealias Counts = Array with Integer

type Holder
	export var items as Counts

	static function create() returns Self
		return Self{items: Counts.create(0)}
	end 'create'
end 'Holder'

function pick(t Holder) returns Counts
	return t.items
end 'pick'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(target isolated Holder)
		let ownedTarget = merge target

		pick(ownedTarget).push(1)
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create() as isolated Holder
	try h.poke(b) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: error.a-promise-may-not-be-sent -->
A `Promise` is a green-thread handle its awaiter owns, and a message MOVES its arguments to another green
thread — so sending one would leave a second thread holding a thread this one is still waiting on.

⛔ **THE DIAGNOSTIC IS E3005, BECAUSE A PROMISE IS TYPED, AND THAT IS THE HONEST FAULT.**
`keep(value Integer)` does not accept a `Promise with Integer`, so the ordinary argument check refuses this
send before the service rule is consulted. The E3217 rule is the structural backstop for the case the type check cannot reach — a
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
	try h.keep(p) otherwise panic("an open mailbox with room takes the send")
	let r = await p
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:13: argument type mismatch for 'value': expected 'Integer', got 'Promise with int'
```

<!-- test: error.a-send-argument-of-another-alias-names-the-cast -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

type Store
	var n as Wide

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(value Narrow)
		self.n = value as Wide
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let w = 5 as Wide
	try h.keep(w) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:13: type mismatch: 'expected Narrow, got Wide' — cast the value with 'as Narrow'
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
		try peer.bump() otherwise panic("an open mailbox with room takes the send")
	end 'say'
end 'Logger'

function main() returns ExitCode
	let c = spawn Calc.create()
	let l = spawn Logger.create()
	try l.say(l) otherwise panic("an open mailbox with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:29:12: argument type mismatch for 'peer': expected 'Calc.handle', got 'Logger.handle'
```

<!-- test: error.a-message-that-returns-nothing-and-throws-nothing-has-no-value -->
⭐ **A MESSAGE THAT RETURNS NOTHING IS SENT AS A STATEMENT, AND THE REFUSAL POINTS AT THE DECLARATION.** A
message that returns nothing and throws nothing carries no `__reply` slot at all, so the send mints no cell
and there is no promise to bind — and the cure is on `bump` rather than at the call. Every OTHER message is
awaitable, which is why the diagnostic names what would give this one a reply. The tried spelling
`try h.bump() otherwise …` is the one a send takes, so it is the spelling that must get this sentence rather
than the generic "does not return a value".
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
	let n = try h.bump() otherwise panic("an open mailbox with room takes the send")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:16:10: Unsupported: the value of `Calc.bump` sent as a MESSAGE — it returns nothing and throws nothing, so it carries no reply slot and a send of it delivers no value. Send it as a statement, `try <handle>.bump(…) otherwise …`, or give it a `returns` clause or a `throws` clause and `try await <handle>.bump(…)` resolves through a reply cell
```

<!-- test: send-and-await-a-reply -->
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
	try h.bump(3) otherwise panic("a running service with room takes the send")
	let n = try await h.total() otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: a-message-throws-and-the-handler-error-merges-with-the-send-errors -->
One `try` covers the send and the handler: the error it binds has the send's two arms, `mailboxFull` and
`serviceStopped`, beside the handler's own cases.
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
			mailboxFull then return 69
			serviceStopped then return 70
			divideByZero then return 71
		end 'why'
	end 'oops'
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
71
```

<!-- test: a-call-after-shutdown-fails-service-stopped -->
A call to a stopped service fails at the send with `serviceStopped`, so the awaiter never has a reply to wait for.
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
			mailboxFull then return 8
			serviceStopped then return 9
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
the shortest way to say it — the state is the one box the service is guaranteed to still name. The other
population is a message PARAMETER — see the case below it.

⚠ The blame names the RETURN, and the note names the **`spawn`** that made the type a service — whether a
type is a service is a whole-program property, and the `spawn` deciding it may be in another file entirely
from the method the rule fires on.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function itself() returns immutable Store
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
error E3209: <fragment>:10:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: a-reply-swapped-out-of-a-field-is-sole -->
The swap idiom: `let old = self.items` followed by `self.items = Items.create()` moves the record out of the
field. The replacement is fresh and every store to the field is fresh, so `old` is the record's only holder and
the reply crosses without a `.clone()`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Items = Array with Integer

type Store
	var items as isolated Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	export function put(n Integer)
		self.items.push(n)
	end 'put'

	export function take() returns isolated Items
		return swap self.items with Items.create(0)
	end 'take'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.put(4) otherwise panic("a running service with room takes the send")
	let got = try await h.take() otherwise return 2
	print("{got.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-read-of-the-field-between-the-swap-load-and-store-keeps-the-reply-aliased -->
A read of `self.items` between the load and the store may hand the record to a second holder, so the swap no
longer proves `old` is the only one and the reply is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Items = Array with Integer

type Store
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	export function put(n Integer)
		self.items.push(n)
	end 'put'

	export function take() returns immutable Items
		var old = self.items
		let seen = self.items.count()
		self.items = Items.create(0)
		return old
	end 'take'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.put(4) otherwise panic("a running service with room takes the send")
	let got = try await h.take() otherwise return 2
	print("{got.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3206: <fragment>:16:18: parameter the receiver is used as mutable at line 19 and as immutable at line 20; a parameter cannot be both
error E3209: <fragment>:20:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.a-field-another-store-fills-with-a-borrowed-record-is-not-swapped-out -->
`replace` stores a record it was handed into `items`, so the record `old` holds may be one its sender still names.
The swap in `take` does not prove `old` is the only holder, and the reply is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Items = Array with Integer

type Store
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	export function put(n Integer)
		self.items.push(n)
	end 'put'

	export function replace(given isolated Items)
		self.items = merge given
	end 'replace'

	export function take() returns immutable Items
		var old = self.items
		self.items = Items.create(0)
		return old
	end 'take'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.put(4) otherwise panic("a running service with room takes the send")
	let got = try await h.take() otherwise return 2
	print("{got.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3206: <fragment>:20:18: parameter the receiver is used as mutable at line 22 and as immutable at line 23; a parameter cannot be both
error E3209: <fragment>:23:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: a-reply-may-be-a-message-parameter -->
`s` is a message PARAMETER, which arrives borrowed out of the message the behaviour still owns. Handing it back
gives the awaiter a counted reference to the same String, which is correct because the String's box counts its
owners.
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
```stdout
hi
```
```exitcode
0
```

<!-- test: a-reply-a-method-of-the-state-builds-fresh-crosses -->
A method called on the service's state that returns a `String` it builds itself hands back a value no one else
holds, so the handler may return it.
```maxon
type Shelf
	export let count as Integer

	static function create(count Integer) returns Self
		return Self{count: count}
	end 'create'

	function describe() returns String
		return "shelf of {self.count}"
	end 'describe'
end 'Shelf'

type Keeper
	let shelf as Shelf

	static function create(shelf Shelf) returns Self
		return Self{shelf: shelf}
	end 'create'

	export function describe() returns String
		return self.shelf.describe()
	end 'describe'
end 'Keeper'

function main() returns ExitCode
	let k = spawn Keeper.create(Shelf.create(4))
	let text = try await k.describe() otherwise "gone"
	print("{text}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
shelf of 4
```

<!-- test: error.two-services-that-await-each-other-are-refused -->
Mutual reentrancy is made unrepresentable rather than diagnosed at run time: a message never waits for another
service's reply, so two services cannot wait on each other, and each message that awaits one is refused.
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
error E3221: <fragment>:9:18: the message 'ping' may wait (it awaits a reply from 'B' at line 10) while holding its own service; send the request with this service's handle and handle the reply in another message
error E3221: <fragment>:25:18: the message 'pong' may wait (it awaits a reply from 'A' at line 26) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: a-reply-error-type-with-one-member-is-nameable -->
⭐ **A MESSAGE THAT THROWS NOTHING FAILS ONLY AT THE SEND, AND THAT ERROR HAS A NAME.** The awaited `total()` can
fail only because the mailbox is full or the service has stopped, so its error type is `Calc.total.sendError` —
the per-message type a `throws` clause can spell, which is what lets a bare `try` PROPAGATE it out of an
intermediate function. A message that also throws has its handler's error beside the send's, and a bare propagating
`try await` of it is refused (see `error.bare-try-propagation-of-a-two-member-reply-is-refused`).
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

function fetch(h Calc.handle) returns Integer throws Calc.total.sendError
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
A message that THROWS has two error sources: the send, which fails with `Calc.divide.sendError`, and the
handler, which throws `MathError`. A bare `try await` of it has no single error type to propagate, so
`fetch`, which declares `throws MathError`, is refused with E3059 rather than re-publishing the handler's half
under a name that hides the send's. Either CATCH both here, which is what `otherwise (e)` is for, or split the
send from the await, which is what `a-split-send-and-await-propagates-the-handlers-error` does.
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
error E3059: <fragment>:22:9: type mismatch: '`try await` of 'Calc.divide' can fail at the send ('Calc.divide.sendError') and in the handler ('MathError'), so it has no single error type to propagate — handle it here with `otherwise (e)` and `match e`, or split it: `let p = try h.m(x)` propagates 'Calc.divide.sendError', then `try await p` propagates 'MathError''
```

<!-- test: a-split-send-and-await-propagates-the-handlers-error -->
⭐ **THE SPLIT THE PREVIOUS CASE'S REFUSAL NAMES.** The send is its own `try` and the await is another: the send
is handled where it is made, and the bare `try await` of the stored promise propagates the handler's `MathError`
alone, which `fetch` declares. `main` catches it at the call and selects the handler's case.
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
	let p = try h.divide(10, by: 0) otherwise panic("a running service with room takes the send")
	return try await p
end 'fetch'

function main() returns ExitCode
	let h = spawn Calc.create()
	let v = try fetch(h) otherwise (e) 'oops'
		match e 'why'
			divideByZero then return 71
		end 'why'
	end 'oops'
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
71
```

<!-- test: error.an-ambiguous-bare-arm-over-a-merged-reply-error-is-refused -->
Merging the send's error with the handler's into one dispatch table can collide on a bare case name: `Halt`
declares a `serviceStopped` of its own, so `serviceStopped` in the `match` names both
`Guard.risky.sendError.serviceStopped` and `Halt.serviceStopped` and there is no rule that picks one. The
qualified spelling disambiguates; the bare one is REFUSED rather than silently selecting a member, which is the
same E3085 an ordinary two-member `try` union earns.
```maxon
enum Halt implements Error
	serviceStopped
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
			serviceStopped then return 70 as ExitCode
			halted then return 71 as ExitCode
			mailboxFull then return 72 as ExitCode
		end 'why'
	end 'oops'
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3085: <fragment>:26:4: case 'serviceStopped' is shared by multiple union members; qualify with 'EnumName.serviceStopped'
```

<!-- test: shutdown-resolves-pending-replies -->
⭐ **THE LIVENESS OBLIGATION, ON BOTH OF ITS ROADS.** No awaiter hangs. A message queued before `shutdown()` still
runs, so its stored reply resolves with the handler's value; a message sent after it fails at the send with
`serviceStopped`, so there is no reply to wait for. The case passes by TERMINATING at all, and its exit code
says which road each took.
```maxon
type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 5}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Slow'

function main() returns ExitCode
	var stops = 0 as Integer
	let h = spawn Slow.create()
	let queued = try h.value() otherwise panic("a running service with room takes the send")
	h.shutdown()
	let answer = await queued
	try await h.value() otherwise (e) 'second'
		match e 'w2'
			mailboxFull then stops = stops + 100
			serviceStopped then stops = stops + 1
		end 'w2'
	end 'second'
	return (answer + stops) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: a-string-moves-in-and-a-fresh-string-comes-back -->
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
	let out = try await h.say("hi") otherwise 'gone'
		return 9
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
⭐ A reply-bearing message whose reply is discarded on purpose with `_ =` mints a cell nobody binds, so the promise
is dropped at statement end while the cell is still PENDING. The dropper may not free it — the replier is about to write into
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
	_ = try h.say("a") otherwise panic("a running service with room takes the send")
	_ = try h.say("b") otherwise panic("a running service with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: pending-reply-dropped-then-answered -->
The dropped cell of the first send is still pending when the SECOND send's await parks `main` and the
service runs, so the replier writes into a cell its awaiter has already renounced. That is the ordering the
teardown rendezvous exists for, and freeing the cell at the drop is a clobbered green thread rather than a
leak.
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
	_ = try h.say("a") otherwise panic("a running service with room takes the send")
	let n = try await h.count() otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: completed-reply-dropped-is-reclaimed -->
The other order: `p`'s first cell COMPLETES while `main` is parked on the middle await and the service runs
(its message is ahead in FIFO order), and only THEN is it dropped — by the RE-ARM on the next line. The drop
finds the runner ticket already there and reclaims; an arm that took a completed cell as merely queued would
strand the struct invisibly, because the green-thread count the exit gate reads has already been debited.
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
	var p = try h.count() otherwise panic("a running service with room takes the send")
	let n = try await h.count() otherwise 0
	p.cancel()
	p = try h.count() otherwise panic("a running service with room takes the send")
	let m = await p
	return (n + m) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```

<!-- test: a-reply-completed-by-a-service-readies-a-waiting-coroutine -->
⭐⭐ **A REPLY COMPLETED ON ANOTHER GREEN THREAD WAKES THE COROUTINE THAT AWAITS IT.** `doubled` is a coroutine of
`main` that awaits a reply from `Inner`: the await parks the coroutine, `Inner` runs on its own stack, and the
reply that completes the cell readies the awaiter. A message body never waits, so the awaiter is `main`'s
coroutine and the service stays a leaf; the answer comes back through the coroutine's promise.
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

function doubled(inner Inner.handle) returns Integer
	let v = try await inner.value() otherwise 0
	return v * 2
end 'doubled'

function main() returns ExitCode
	let i = spawn Inner.create()
	let pending = async doubled(i)
	let v = await pending
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
6
```

<!-- test: error.double-await-of-a-reply -->
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
	let p = try h.total() otherwise panic("a running service with room takes the send")
	let a = await p
	let b = await p
	return (a + b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3142: <fragment>:18:16: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: error.plain-await-of-a-reply -->
Every send throws, whatever the message itself declares — so a plain `await` of a send is E3057 by construction.
Only the promise a send returns is awaited without `try`, when the handler cannot throw.
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
error E3057: <fragment>:16:16: throwing function requires try: 'Calc.total'
```

<!-- test: error.a-message-that-throws-a-payload-carrying-union-is-refused -->
⚠ **THE ONE ERROR SHAPE A REPLY CANNOT CARRY, REFUSED RATHER THAN MISCOMPILED.** The reply's error word is a
single word holding the handler error's `ordinal + bias`; a payload-carrying union's flag is a heap POINTER
instead, so the box would cross green threads with no soleness rule to say it may.

⚠ **IT FIRES AT THE `spawn` AND NOT AT A SEND, AND THAT IS FORCED RATHER THAN CONVENTIONAL.** `<T>.__behave` is
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
error E3140: <fragment>:19:10: the message `Store.wipe` declares a reply that throws `Trouble`, a PAYLOAD-CARRYING union whose error flag is a heap box pointer — a reply's one error word holds an `ordinal + bias`, and a pointer is not an ordinal, and this `spawn` makes `Store` a service — whose reply-bearing messages resolve through a CELL, a green thread that never runs, carrying one value word and one error word. Return an integer, a `String`, a struct or a service handle, and throw a payload-free `enum`; or drop the `returns` and `throws` clauses, which makes the message fire-and-forget and gives it no reply to carry
```

<!-- test: error.a-message-that-waits-through-a-helper-is-refused -->
A wait is transitive through ordinary functions: `A.ping` calls `relay`, which awaits a `B.handle`, so `A.ping`
may wait even though its own body names no `B` message.

⚠ The refusal is anchored at `A.ping` and its chain names `relay` and the line of the `await` — a message that
waits only through a helper has no await of its own to point at.
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
error E3221: <fragment>:13:18: the message 'ping' may wait (it calls 'relay', which awaits a reply from 'B' at line 3) while holding its own service; send the request with this service's handle and handle the reply in another message
error E3221: <fragment>:29:18: the message 'pong' may wait (it awaits a reply from 'A' at line 30) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: error.a-message-that-awaits-a-peer-of-its-own-type-is-refused -->
⚠ **A MESSAGE THAT AWAITS A PEER OF ITS OWN TYPE IS REFUSED LIKE ANY OTHER AWAIT, AND THIS IS THE ONE USERS WILL
HIT.** Two instances of the same service could not actually deadlock, but the refusal is a property of the message
body — it holds its own service while it waits — and not of which instance answers. The message says the
workaround: send the request with this service's handle and handle the reply in another message.
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
	_ = try w.ask(w.clone()) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3221: <fragment>:9:18: the message 'ask' may wait (it awaits a reply from 'Worker' at line 10) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: error.a-message-with-two-awaits-is-refused-at-the-first -->
⛔⛔ **A MESSAGE WITH TWO AWAITS IS REFUSED, AND THE FIRST WAIT IS THE ONE IT NAMES.** `A.ping` awaits `B` and then
`C`. This case is `error.two-services-that-await-each-other-are-refused` with **one extra, unrelated `await`
appended**: a refusal that kept only whichever wait it saw LAST would name the innocent `C` await, and one that
looked only at the first would stop at the first. **Delete the `C` await and the case still refuses — which is
the point: it must refuse WITH it, at the earliest wait.**
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
	_ = try a.ping(b, c: c) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3221: <fragment>:9:18: the message 'ping' may wait (it awaits a reply from 'B' at line 10) while holding its own service; send the request with this service's handle and handle the reply in another message
error E3221: <fragment>:27:18: the message 'pong' may wait (it awaits a reply from 'A' at line 28) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: error.a-message-that-calls-a-waiting-closure-is-refused -->
⛔⛔ **A WAIT BEHIND A CLOSURE VALUE IS A WAIT, AND THE REFUSAL FOLLOWS THE CLOSURE'S BODY.** `A.kick` calls a
local function value, whose body is decided at the call site rather than at a callee's name; the closure awaits
`B.work`'s reply, so `kick` may wait while it holds `A`, and the chain names the closure and the `await` inside
it. `B.work` awaits `A.ack` directly and is refused too. No message of either service can wait, so a ring of
services awaiting each other is unrepresentable even behind an indirect call.
```maxon
typealias Integer = int(i64.min to i64.max)

type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function kick(peer B.handle, mine A.handle) returns Integer
		let through = function(p B.handle, m A.handle) gives (try await p.work(m.clone()) otherwise 0)
		return through(peer, mine)
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
```maxoncstderr
error E3221: <fragment>:11:18: the message 'kick' may wait (it calls the closure at line 12, which awaits a reply from 'B' at line 12) while holding its own service; send the request with this service's handle and handle the reply in another message
error E3221: <fragment>:28:18: the message 'work' may wait (it awaits a reply from 'A' at line 29) while holding its own service; send the request with this service's handle and handle the reply in another message
```

<!-- test: a-scheduler-deadlock-decides-the-same-at-four-processors -->
<!-- procs: 4 -->
⛔⛔ **THE HALF OF THE DEADLOCK RULE THAT IS A RUNTIME PROPERTY.** No message of a service waits, so the only
parked green thread a program can end up with is `main` or a coroutine of it, and a wait that nothing can end
is the scheduler's `checkdead` (Go's, at `mput`): `RuntimeAbort.schedulerDeadlock`, **exit 92**, silent on both
streams. The answer is a diagnosis rather than a hang: a wedged process tells you nothing and costs a 120 s
harness timeout; a 92 names the condition.

⭐⭐ **THE DETECTOR DECIDES UNDER `__sched_lock`, SO THE ANSWER IS THE SAME AT EVERY PROCESSOR COUNT.** `main`
parks on a select over nothing while four idle services keep every other processor free to go idle. The last
machine to join the idle list runs `checkdead` in the same hold of the lock: no machine running, no timer,
child or read pending, and `main` not finished is exit 92 at once. `procs: 4` is what makes this case take that
decision on the fourth idle machine rather than the only one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

type Idle
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function touch()
		self.n = self.n + 1
	end 'touch'
end 'Idle'

function main() returns ExitCode
	let first = spawn Idle.create()
	let second = spawn Idle.create()
	try first.touch() otherwise panic("an open mailbox with room takes the send")
	try second.touch() otherwise panic("an open mailbox with room takes the send")

	let arr = IntPromiseArray.create(0)
	let ready = arr.awaitAny()
	return ready as ExitCode
end 'main'
```
```exitcode
92
```
```stderr
fatal error: runtime abort 92 (schedulerDeadlock)
```

<!-- test: deep-chain-of-continuations-runs -->
Three services in a chain, each handing the work to the next. A message never waits for another service's reply,
so each one posts its request to the next with the handle it was given and answers at once; the replies the old
shape awaited arrive as messages to `Sink`, which `main` polls until all three have landed. Nothing in the chain
can deadlock, because no service ever waits.

⚠ `A.value` forwards `last` and `sink` with a `.clone()`: a message PARAMETER arrives borrowed, and a send MOVES —
so the handle it forwards has to be one this frame owns. The originals are dropped with the messages, which is
what shuts `C` and `Sink` down once the chain has answered.
```maxon
type Sink
	var count as Integer
	var sum as Integer

	static function create() returns Self
		return Self{count: 0, sum: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + 1
		self.sum = self.sum + by
	end 'add'

	export function arrived() returns Integer
		return self.count
	end 'arrived'

	export function total() returns Integer
		return self.sum
	end 'total'
end 'Sink'

type C
	var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function value(sink Sink.handle)
		try sink.add(self.n) otherwise panic("an open mailbox with room takes the send")
	end 'value'
end 'C'

type B
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next C.handle, sink Sink.handle)
		try next.value(sink.clone()) otherwise panic("an open mailbox with room takes the send")
		try sink.add(10) otherwise panic("an open mailbox with room takes the send")
	end 'value'
end 'B'

type A
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value(next B.handle, last C.handle, sink Sink.handle)
		try next.value(last.clone(), sink: sink.clone()) otherwise panic("an open mailbox with room takes the send")
		try sink.add(20) otherwise panic("an open mailbox with room takes the send")
	end 'value'
end 'A'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let c = spawn C.create()
	let b = spawn B.create()
	let a = spawn A.create()
	try a.value(b, last: c, sink: sink.clone()) otherwise panic("an open mailbox with room takes the send")

	while (try await sink.arrived() otherwise 0) < 3 'untilAllArrived'
		Scheduler.yield()
	end 'untilAllArrived'

	let v = try await sink.total() otherwise 0
	return v as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
31
```

<!-- test: awaitany-returns-the-completed-index -->
⭐ **ONE WAITING PRIMITIVE COVERS SERVICE REPLIES, FILE IO AND SUBPROCESS DRAINS, AND THIS IS THE HALF THAT
MAKES IT TRUE.** A reply is an ordinary `Promise`, so it goes into an `Array with Promise with …` and
its `awaitAny()` selects over it exactly as it does over `async` spawns — no separate "channel select"
and no second waiting mechanism.

⚠ **THE STORAGE NAMES THE HANDLER'S OWN ERROR TYPE, AND NOTHING MORE.** The send's failure surfaces at the send,
before any promise exists, so a reply carries only what its handler throws. `Slow.value` throws nothing, so its
storage is `Promise with Integer`; a message that DOES throw adds its error as the second argument, as
`a-throwing-reply-stores-in-a-promise-naming-its-errors` below does.

⚠ The reply is awaited afterwards. `awaitAny` retires nothing, so the array would otherwise die holding a
live reply cell — exit 75; see `specs/await-any.md`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
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
	let ps = ReplyPromiseArray.create(0)
	ps.push(try h.value() otherwise panic("a running service with room takes the send"))
	let ready = ps.awaitAny()
	let p = try ps.get(ready) otherwise panic("awaitAny named a slot that is in range")
	let v = await p
	return (ready as Integer + v) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-throwing-reply-stores-in-a-promise-naming-its-errors -->
⭐ **A THROWING MESSAGE'S REPLY IS STORED UNDER THE HANDLER'S OWN ERROR.** A message that throws replies with
`Promise with (T, <what the message throws>)`, an ordinary declared type, because the send's failure surfaces at
the send and never in the promise. The storage road therefore binds only the handler's cases, and the
direct `try await` road adds the send's two beside them.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

typealias Integer = int(i64.min to i64.max)
typealias DivideReply = Promise with (Integer, MathError)
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
	let ps = DivideReplyArray.create(0)
	ps.push(try h.divide(10, by: 0) otherwise panic("a running service with room takes the send"))
	let p = try ps.get(0) otherwise panic("the reply was pushed into slot 0")
	try await p otherwise (e) 'oops'
		match e 'why'
			divideByZero then return 71
		end 'why'
	end 'oops'
	return 0
end 'main'
```
```exitcode
71
```

<!-- test: a-reply-over-a-declared-type-stores-in-a-promise-naming-it -->
⭐ **A REPLY'S RESULT MAY BE A DECLARED TYPE, AND THE STORAGE TYPE THAT NAMES ONE IS THE SAME INSTANCE THE
SEND MINTS.** `Promise with (T, E)` is interned on its base and its ARGUMENTS, and a declared type reaches
the two roads under two different tags — `structRef` off the message's `returns` clause, `named` off the
type argument the author wrote — which mangle to one symbol. Interned as two, a program would be refused as
a duplicate definition of its own promise. Every `Promise with Integer` and `Promise with (Integer, …)` case above
is blind to it: a scalar argument carries no name, so it carries no second tag.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tally
	export var n as Integer

	static function create(n Integer) returns Tally
		return Self{n: n}
	end 'create'
end 'Tally'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bumped(by Integer) returns isolated Tally
		self.count = self.count + by
		return Tally.create(self.count)
	end 'bumped'
end 'Counter'

typealias TallyReply = Promise with Tally
typealias TallyReplyArray = Array with TallyReply

function main() returns ExitCode
	let h = spawn Counter.create()
	let ps = TallyReplyArray.create(0)
	ps.push(try h.bumped(4) otherwise panic("a running service with room takes the send"))
	let p = try ps.remove(0) otherwise panic("the reply was pushed into slot 0")
	let tally = await p
	return tally.n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.a-stored-reply-that-names-the-wrong-errors-is-refused -->
`ReplyPromise` names `OtherError`, which is not the error `Calc.divide` throws; storing the reply under it
would make the awaiter decode the handler's flag as another enum: a silent wrong `match` arm rather than a
diagnostic. So the refusal stays at the STORE, where both the declared name and the message's own are still known.
```maxon
enum MathError implements Error
	divideByZero
end 'MathError'

enum OtherError implements Error
	unrelated
end 'OtherError'

typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with (Integer, OtherError)
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
	var ps = ReplyPromiseArray.create(0)
	ps.push(try h.divide(10, by: 2) otherwise panic("a running service with room takes the send"))
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:32:5: a promise of 'Integer' throwing 'MathError' cannot be stored in 'ReplyPromise', which holds 'Integer' throwing 'OtherError'
```

<!-- test: a-stopped-service-stores-no-reply-and-its-send-fails-serviceStopped -->
⭐⭐ **A STORED REPLY NEVER CARRIES THE STOP.** A reply awaited out of an array is described by its STORAGE TYPE,
which names only what the handler throws; the failure of the send is read where the send is made. This shuts the
service down first, so the send fails with `serviceStopped` before any reply exists: the `if let` takes its
`else` road, nothing reaches the array, and the stop is counted at the send.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
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
	var stops = 0 as Integer
	let h = spawn Slow.create()
	h.shutdown()
	let ps = ReplyPromiseArray.create(0)
	if let reply = try h.value() 'sent'
		ps.push(reply)
	end 'sent' else (e) 'gone'
		match e 'w'
			mailboxFull then stops = stops + 100
			serviceStopped then stops = stops + 1
		end 'w'
	end 'gone'
	return (stops + ps.count() as Integer) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-reply-stored-without-its-error-type-is-refused -->
A reply from a message that throws carries the handler's error, so a `Promise with T` storage would erase it and
leave a `try await` with nothing to bind `e` at. The refusal names the MESSAGE rather than *"this promise's
function"*: the promise belongs to the reply of `Slow.value`, whose `throws` clause is what the storage
must spell.
```maxon
enum SlowError implements Error
	tooSlow
end 'SlowError'

typealias Integer = int(i64.min to i64.max)
typealias BarePromise = Promise with Integer
typealias BarePromiseArray = Array with BarePromise

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	export function value() returns Integer throws SlowError
		if self.n > 9 'slow'
			throw SlowError.tooSlow
		end 'slow'
		return self.n
	end 'value'
end 'Slow'

function main() returns ExitCode
	let h = spawn Slow.create()
	var ps = BarePromiseArray.create(0)
	ps.push(try h.value() otherwise panic("a running service with room takes the send"))
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:28:5: a promise of 'Integer' throwing 'SlowError' cannot be stored in 'BarePromise', which holds 'Integer'
```

<!-- test: a-generic-service-is-supported -->
A generic type may be spawned, and ONE companion pair serves every instantiation of it. A `T`-typed message
payload is an opaque 8-byte slot carrying the layout descriptor, which is the same dictionary-passing model
every generic body in the compiler already compiles under — the companions never needed to be monomorphic, only the
LAYOUTS do.

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
	let v = (try await h.peek() otherwise 0) as FullRangeInt
	return v as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: two-instantiations-of-one-generic-service-coexist -->
TWO instantiations of one generic service in one program, under one `Box.request` and one `Box.handle`.
Nothing about a companion names a type argument, so this is the case that would break first if the payload
slot were typed rather than opaque — and it is the reason a per-instantiation companion pair is not needed.
`Small` and `Large` are two TYPES over two ranges, so the two spawns are two `GenericInstanceId`s.
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

typealias Small = int(0 to 100)
typealias Large = int(0 to 100000)

function main() returns ExitCode
	let a = spawn Box.create(7 as Small)
	let b = spawn Box.create(35 as Large)
	let x = try await a.peek() otherwise 0
	let y = try await b.peek() otherwise 0
	return (x as Integer + y as Integer) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: a-generic-reply-that-is-the-services-own-string-state-crosses -->
The reply IS the state, at a type the body cannot see: one body serves every instantiation. With `T` a `String`,
the caller and the service's own green thread each hold a counted reference to one string record, which is
correct because the String's box counts its owners.
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
	let h = spawn Box.create("ok")
	let s = try await h.peek() otherwise ""
	return s.byteLength() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-generic-reply-of-a-fresh-container-over-a-managed-record-crosses -->
The control for the rule above: a container the handler MINTS is not the state, and its graph is walkable
once `T` is an `Item`. The handler's `return` defers on a type an instantiation still fixes, and the `spawn`
walks the substitution and accepts it.
```maxon
typealias Count = int(0 to 255)

type Item
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Item'

type Box uses T
	export typealias Items = List with T
	var seen as Count

	static function create(first isolated T) returns Self
		let held = Items.create()
		held.append(merge first)
		return Self{seen: held.count() as Count}
	end 'create'

	export function all() returns isolated Items
		self.seen = self.seen + 1
		return Items.create()
	end 'all'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(Item.create("a label long enough to be a heap string"))
	let items = try await h.all() otherwise panic("the box is running")
	return items.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: error.an-inner-alias-of-the-type-parameter-is-refused-at-its-declaration -->
An alias of the type parameter is refused where it is WRITTEN, which is what lets the reply rule ask about a
bare `T` and nothing else: no message can reach the state through a second spelling of `T`.
```maxon
type Box uses T
	export typealias Item = T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function peek() returns Item
		return self.item
	end 'peek'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create("a label long enough to be a heap string")
	let s = try await h.peek() otherwise panic("the box is running")
	return s.byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:26: Unsupported: a typealias over 'identifier' (a typealias names `int(low to high)`, `float(low to high)`, `bits(n)`, `function(...)`, a tuple `(A, B)` or a generic instance `Base with …`)
```

<!-- test: a-generic-service-handle-reaches-a-parameter-through-its-spelling -->
`spawn` hands back a handle at the INSTANTIATION (`Box.handle with Small`), and a `typealias` over that is
what carries it anywhere else — the companion is a generic instance like any other, so it is spelled the way
`Array with Byte` is. ⚠ There is no INLINE spelling here or anywhere: `function f(xs Array with Whole)` is
E2010 for every generic in the language, so the alias is not a service quirk.
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

typealias SmallBoxHandle = Box.handle with Small

function readIt(h SmallBoxHandle) returns Whole
	let n = try await h.peek() otherwise 0
	return n as Whole
end 'readIt'

function main() returns ExitCode
	let h = spawn Box.create(4 as Small)
	return readIt(h) as ExitCode
end 'main'
typealias Small = int(0 to 100)
typealias Whole = int(i64.min to i64.max)
```
```exitcode
4
```

<!-- test: a-generic-service-handle-lives-in-a-struct-field -->
A handle in a FIELD, which co-owns it. ⭐ **A generic handle is `genericInstance`-tagged, not
`structRef`-tagged after its companion's name**, and a retain router that reads it as the latter lets a
co-owning store fall through to `__mm_incref` — which steps the box while the release still steps a
`handles` share, a `refs` share AND the box. The mailbox handle count then reaches zero under a live handle,
the service drains, and the next send fails with `serviceStopped`. **Exit 0 is the failure here**, not a crash.
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

typealias SmallBoxHandle = Box.handle with Small

type Holder
	var h as SmallBoxHandle

	static function create(h SmallBoxHandle) returns Self
		return Self{h: h}
	end 'create'

	function ask() returns Whole
		let n = try await self.h.peek() otherwise 0
		return n as Whole
	end 'ask'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(spawn Box.create(5 as Small))
	return holder.ask() as ExitCode
end 'main'
typealias Small = int(0 to 100)
typealias Whole = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: a-generic-service-handle-lives-in-an-array -->
The third road the spelling opens, and the one that proves the element type survives a container: `Array with
SmallBoxHandle` holds a real 8-byte handle box, and `get` hands back something a `T`-replying message can
still be sent through.
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

typealias SmallBoxHandle = Box.handle with Small
typealias HandleArray = Array with SmallBoxHandle

function main() returns ExitCode
	let hs = HandleArray.create(0)
	hs.push(spawn Box.create(6 as Small))
	let h = try hs.get(0) otherwise return 1
	let s = try await h.peek() otherwise 0
	return s as ExitCode
end 'main'
typealias Small = int(0 to 100)
```
```exitcode
6
```

<!-- test: error.a-type-parameter-reply-through-a-bare-handle-slot-is-refused -->
The boundary the three cases above sit inside. A slot typed at the BARE `Box.handle` names no instantiation —
there is one handle companion and every instantiation shares it — so a message replying at `T` has no type to
reply with. The refusal names the cure the cases above use.
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

typealias StringBoxHandle = Box.handle with String

function readIt(h Box.handle) returns Whole
	let s = try await h.peek() otherwise 0
	return s as Whole
end 'readIt'

function main() returns ExitCode
	let h = spawn Box.create(1)
	return 0
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E3145: <fragment>:17:22: `Box.peek` replies at `Box`'s own type parameter `T`, and this handle stands at the bare `Box.handle` — the ONE handle companion every instantiation of `Box` shares — so it names no instantiation and the reply has no type. SPELL the instantiation: `Box.handle with <the type argument>` is a generic instance like any other, and a `typealias` over it carries the reply's type through a parameter, a struct field or an array element. Or send `peek` where the `spawn Box.create(…)` binding is, which carries the instantiation already
```

<!-- test: a-generic-handle-is-an-opaque-element-of-another-generic -->
⭐⭐ **THE RED GATE FOR THE FOURTH DECIDER.** A handle reaches a shared generic body as an OPAQUE element
here, so its retain is chosen from the layout descriptor's `retainFunc` rather than from the value's tag — a
fourth road, and one only the handle's `with` spelling opens. With the protocol classifier blind to a
service handle, the element is retained through `__mm_retain`, which steps the box, while the release still
steps a `handles` share, a `refs` share AND the box; the mailbox handle count reaches zero under a live
handle, the service drains, and the SECOND ask fails with `serviceStopped`. **This case answers 6, and 3 under that
sabotage**, with a concrete-typed field unchanged in both directions as the control.
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

typealias SmallBoxHandle = Box.handle with Small

type Cell uses E
	var slot as E

	static function create(slot E) returns Self
		return Self{slot: slot}
	end 'create'

	function grab() returns E
		return self.slot
	end 'grab'
end 'Cell'

typealias HandleCell = Cell with SmallBoxHandle

function main() returns ExitCode
	let c = HandleCell.create(spawn Box.create(3 as Small))
	let first = c.grab()
	let a = try await first.peek() otherwise 0
	let second = c.grab()
	let b = try await second.peek() otherwise 0
	return (a + b) as ExitCode
end 'main'
typealias Small = int(0 to 100)
```
```exitcode
6
```

<!-- test: a-monomorphic-handle-is-co-owned-in-a-struct-field -->
⚠ **NOT A GENERIC PROGRAM, AND THAT IS THE POINT.** `a-generic-service-handle-lives-in-a-struct-field`
above pins the generic road; the monomorphic road into the same co-owning store is this case's. The field
co-owns the handle box through a plain `__mm_incref`, so a program whose ONLY incref comes from a handle
retain must still install `__mm_incref`. Nothing here allocates but the handle, which is what makes the
case discriminating.
```maxon
type Calc
	var count as Whole

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump() returns Whole
		self.count = self.count + 1
		return self.count
	end 'bump'
end 'Calc'

type Holder
	var h as Calc.handle

	static function create(h Calc.handle) returns Self
		return Self{h: h}
	end 'create'

	function ask() returns Whole
		return try await self.h.bump() otherwise 0
	end 'ask'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(spawn Calc.create())
	let a = holder.ask()
	let b = holder.ask()
	return (a + b) as ExitCode
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: error.a-generic-reply-the-cell-cannot-carry-is-refused-at-the-instantiation -->
⭐ **THE ONLY CASE THAT PROVES THE SECOND ASK IS REACHABLE.** `classifyServiceRoster` is asked twice —
`declarationOnly` before the factory call, `atInstance` after — and every other refusal it can reach is
settled at the first ask. A reply declared at a type parameter is DEFERRED there, because judging it on the
declaration would refuse every generic service: a type parameter rides no register until an instantiation
fixes one. `float` is what it fixes here, and E3140 is the deferred verdict arriving. Without this case the
`atInstance` arm is dead code that reads as live.
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
	let h = spawn Box.create(1.5 as Real)
	return 0
end 'main'
typealias Real = float(f64.min to f64.max)
```
```maxoncstderr
error E3140: <fragment>:15:10: the message `Box.peek` declares a reply whose value is float, which does not travel in the single integer word a reply cell carries — a `float` comes back in XMM0, and an opaque type parameter is released through a companion the cell does not carry, and this `spawn` makes `Box` a service — whose reply-bearing messages resolve through a CELL, a green thread that never runs, carrying one value word and one error word. Return an integer, a `String`, a struct or a service handle, and throw a payload-free `enum`; or drop the `returns` and `throws` clauses, which makes the message fire-and-forget and gives it no reply to carry
```

<!-- test: error.awaiting-shutdown-is-refused -->
`shutdown` carries no reply, so there is nothing for a `try await` to resolve — the same answer a void
MESSAGE gets one rule over (`error.void-message-sent-for-its-value`). It is the handle's own method rather than
a message of the service, so it is refused where its value is wanted instead of being looked up on the message
roster, where it is not.
```maxon
type Calc
	var count as Whole

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump() returns Whole
		self.count = self.count + 1
		return self.count
	end 'bump'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let n = try await h.bump() otherwise 0
	try await h.shutdown() otherwise return 1 as ExitCode
	return n as ExitCode
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:18:14: Unsupported: the value of `Calc.shutdown()` — `shutdown` is the HANDLE's own method and not a message of `Calc`, so no declaration says what it hands back and an expression has nothing to bind. Write `<handle>.shutdown()` as a statement: it closes the mailbox once everything already queued has run, and dropping the last handle does the same
```

<!-- test: a-spawn-inside-a-generic-body-over-an-inferred-instance -->
⭐ **THE OTHER HALF OF `error.a-spawn-inside-a-generic-body-is-refused`, AND THE HALF THAT MUST WORK.** That
case refuses a `spawn` whose type arguments fix to the enclosing declaration's own type parameters, which
name no layout. Here they fix to a CONCRETE type, so there is nothing to refuse and the service is ordinary.
⚠ **The instantiation is INFERRED** (`Outer.create(1 as Whole)`) rather than spelled by a `typealias`, and
that is the whole discrimination: the same program with `typealias WholeOuter = Outer with Whole` takes a
different road, and so does this one without the `spawn`. It takes both to reach `forwardCallerLayout`'s
*"caller 'main' has no layout descriptor to forward to 'Outer.go'"* panic, which the parser's transitive
reservation makes unreachable.
```maxon
typealias Whole = int(i64.min to i64.max)

type Calc
	var n as Whole

	static function create(n Whole) returns Self
		return Self{n: n}
	end 'create'

	export function peek() returns Whole
		return self.n
	end 'peek'
end 'Calc'

type Outer uses U
	var seed as U

	static function create(seed U) returns Self
		return Self{seed: seed}
	end 'create'

	function go() returns Whole
		let h = spawn Calc.create(3)
		return try await h.peek() otherwise 0
	end 'go'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(1 as Whole)
	return o.go() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-spawn-inside-a-generic-body-whose-factory-fixes-nothing -->
The third corner of the enclosing-generic family. `error.a-spawn-inside-a-generic-body-is-refused`
covers arguments fixed to the enclosing declaration's type parameters;
`a-spawn-inside-a-generic-body-over-an-inferred-instance` covers arguments fixed to a concrete type. Here
the enclosing factory fixes **nothing at all**, so `Outer` stands at its bare base and `main` has no
descriptor to forward — which the lowering would be right to refuse and must not reach. So the enclosing
body RESERVES no descriptor slot no call site could ever fill.
```maxon
typealias Whole = int(i64.min to i64.max)

type Calc
	var n as Whole

	static function create(n Whole) returns Self
		return Self{n: n}
	end 'create'

	export function peek() returns Whole
		return self.n
	end 'peek'
end 'Calc'

type Outer uses U
	export var seed as Whole

	static function create() returns Self
		return Self{seed: 4}
	end 'create'

	function go() returns Whole
		let h = spawn Calc.create(3)
		return try await h.peek() otherwise 0
	end 'go'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create()
	return o.go() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: error.a-spawn-whose-factory-returns-another-type-is-refused -->
`create` returns an `Other`: it does not start a service, and the spawn is refused at the call.
```maxon
public typealias Whole = int(i64.min to i64.max)

type Other
	export var m as Whole

	static function make(m Whole) returns Self
		return Self{m: m}
	end 'make'
end 'Other'

type Counter
	export var n as Whole

	static function create(seed Whole) returns Other
		return Other.make(seed)
	end 'create'

	static function createStepped(seed Whole, step Whole) returns Self
		return Self{n: seed + step}
	end 'createStepped'

	export function peek() returns Whole
		return self.n
	end 'peek'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create(40)
	let total = try await h.peek() otherwise 0
	return total as ExitCode
end 'main'
```
```maxoncstderr
error E3134: <fragment>:29:10: `spawn Counter.create(…)` does not start a service: `Counter.create` does not hand back a `Counter` the service can own — a service's state is the BOX of a declared `type`, and this factory's recorded return type is not one. `spawn` starts a SERVICE from a static factory of a declared type that returns that type, e.g. `spawn Calc.create()`
```

<!-- test: error.a-value-popped-from-a-field-container-is-moved-out-not-solely-owned -->
A `take` that returns `self.queue.pop()` moves the element out of the queue, but the queue may have shared it: `stash` pushed the very `Job` that `current` still names. The element is not provably solely owned, so sending it is refused.
```maxon
typealias Integer = int(i64.min to i64.max)

type Job
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

typealias Jobs = Array with Job

type Holder
	var queue as Jobs
	var current as Job

	static function create() returns Self
		return Self{queue: Jobs.create(0), current: Job.create(1)}
	end 'create'

	function stash()
		self.queue.push(self.current)
	end 'stash'

	function take() returns Job
		return try self.queue.pop() otherwise panic("empty")
	end 'take'
end 'Holder'

type Worker
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function run(j isolated Job) returns Integer
		let ownedJ = merge j

		self.total = self.total + ownedJ.n
		return self.total
	end 'run'
end 'Worker'

function main() returns ExitCode
	var h = Holder.create()
	h.stash()
	let j = h.take()
	let worker = spawn Worker.create()
	let total = try await worker.run(j) otherwise panic("running")
	return total as ExitCode
end 'main'
```
```maxoncstderr
error E3200: <fragment>:51:35: 'j' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.a-value-popped-from-a-generic-stack-is-moved-out-not-solely-owned -->
The same through a generic `Stack with Job`: `pop` returns `self.items.pop()`, the pushed `Job` is still held by `current`, and the send is refused.
```maxon
typealias Integer = int(i64.min to i64.max)

type Job
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Stack uses T
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function push(v T)
		self.items.push(v)
	end 'push'

	function pop() returns T
		return try self.items.pop() otherwise panic("empty")
	end 'pop'
end 'Stack'

typealias JobStack = Stack with Job

type Worker
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function run(j isolated Job) returns Integer
		let ownedJ = merge j

		self.total = self.total + ownedJ.n
		return self.total
	end 'run'
end 'Worker'

function main() returns ExitCode
	var current = Job.create(1)
	var s = JobStack.create()
	s.push(current)
	let j = s.pop()
	let worker = spawn Worker.create()
	let total = try await worker.run(j) otherwise panic("running")
	return (total + current.n) as ExitCode
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:35: 'j' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-borrowed-string-parameter-is-copied-into-the-send -->
⭐⭐ **A BORROWED `String` PARAMETER IS COPIED INTO THE SEND.** `buf` arrived as a borrowed parameter and the
caller still holds it. A `String` is a value type, so the send hands the service a COPY and `buf` stays
readable afterwards: the service owns its own record and the caller's is untouched, with no `.clone()`
written. A borrowed struct parameter that nothing writes is shared instead
(`a-borrowed-parameter-nothing-writes-is-shared-with-the-service`).

⚠ A bare LITERAL is promoted the same way (`a-string-argument-moves-into-the-service`'s third send), and a
`String` the frame built itself is MOVED, as before; the copy is only for a `String` this frame does not own.
```maxon
type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(s String) returns Integer
		self.n = self.n + 1
		return s.byteLength() as Integer
	end 'keep'
end 'Store'

function forward(h Store.handle, buf String) returns Integer
	let kept = try await h.keep(buf) otherwise 0
	return kept + (buf.byteLength() as Integer)
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let total = forward(h, buf: "hello")
	print("{total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
10
```

<!-- test: a-borrowed-parameter-may-be-sent-as-a-clone -->
⚠ **NOTHING IN THIS CASE MAY REST ON WHICH GREEN THREAD REACHES `stdout` FIRST.** Its subject is OWNERSHIP,
so `main` is the only writer and the awaited `report` is what puts the service's `keep` before the line that
reads it back — a CAUSAL order rather than a timed one.

⭐⭐ **A `.clone()` IS A FRESH STRING, BECAUSE THE FRESH-RETURN CLAIM CLOSES OVER A HOP.** `String.clone`'s body is `return sliceBytes(…)` — a call to a function that IS fresh by
`Parser.noteFreshReturnShape`'s record-literal criterion — and `ProgramSignatures.closeFreshReturnForwards`
carries the claim that one frame out, so `h.keep(buf.clone())` and `let c = buf.clone()` + `h.keep(c)` both
send a String nothing else names.

⚠ **WHAT MAKES THE HOP SOUND IS THAT THE FORWARDING FRAME BINDS NOTHING.** `return f(…)` with the call
ending the line has no statement between the callee's hand-off and its own, so the reference it passes on is
the one it received and nobody else names it — the identical argument the record-literal criterion rests on,
one frame out. A body that BINDS what it returns has statements between the construction and the hand-off,
so it is fresh only when every use of the local is one that cannot take a second reference (the
`a-reply-a-sibling-builds-in-a-local` cases at the end of this file).
```maxon
type Store
	var n as Integer
	var last as String

	static function create() returns Self
		return Self{n: 0, last: ""}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
		self.last = s
	end 'keep'

	export function report() returns String
		return "kept {self.last} ({self.n})"
	end 'report'
end 'Store'

function forward(h Store.handle, buf String)
	try h.keep(buf.clone()) otherwise panic("a running service with room takes the send")
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let s = "hello"
	forward(h, buf: s)
	let kept = try await h.report() otherwise panic("the service holds the value and must answer for it")
	print("caller still has {s}\n")
	print("{kept}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
caller still has hello
kept hello (1)
```

<!-- test: a-static-factory-forwarding-to-its-sibling-static-sends-a-fresh-record -->
A bare call inside a type body names the type's own member first, so `create`'s `return build()` hands back the
record `build` constructs — a fresh record the send moves into the service.
```maxon
type Payload
	var n as Integer

	static function build() returns Self
		return Self{n: 7}
	end 'build'

	static function create() returns Self
		return build()
	end 'create'

	function count() returns Integer
		return n
	end 'count'
end 'Payload'

type Store
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function keep(p isolated Payload)
		self.n = self.n + p.count()
	end 'keep'

	export function total() returns Integer
		return n
	end 'total'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	try h.keep(Payload.create()) otherwise panic("a running service with room takes the send")
	let total = try await h.total() otherwise panic("the service answers")
	return total as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: a-borrowed-parameter-may-be-sent-as-a-fresh-interpolation -->
⚠ **NOTHING IN THIS CASE MAY REST ON WHICH GREEN THREAD REACHES `stdout` FIRST.** Its subject is OWNERSHIP,
so `main` is the only writer and the awaited `report` is what puts the service's `keep` before the line that
reads it back — a CAUSAL order rather than a timed one.

⭐⭐ **THE CURE THE TWO REFUSALS ABOVE NAME, AND THE CASE THAT SAYS IT IS REACHABLE.** A refusal that teaches
a spelling the next diagnostic also refuses leaves an author with nothing to write, which is what a rule
without this case would be. An interpolation ALLOCATES: the record it builds is this frame's, nothing else
can name it, and the send moves it — so `buf` stays readable in the caller and the service owns a record of
its own.

⚠ **THE OTHER CURE IS `.clone()`, AND THE TWO ARE NOT INTERCHANGEABLE**: a clone needs the type to have
one, building at the send needs nothing. `a-borrowed-parameter-may-be-sent-as-a-clone` is that program.

⛔⛔ **`.clone()` WORKS AT A SEND BECAUSE THE FRESH-RETURN CLAIM CLOSES ONE HOP.** `String.clone`'s body
is `return sliceBytes(…)`: a call to a function that IS fresh by `noteFreshReturnShape`'s criterion, ONE HOP
away. `ProgramSignatures.closeFreshReturnForwards` is that hop.
```maxon
type Store
	var n as Integer
	var last as String

	static function create() returns Self
		return Self{n: 0, last: ""}
	end 'create'

	export function keep(s String)
		self.n = self.n + 1
		self.last = s
	end 'keep'

	export function report() returns String
		return "kept {self.last} ({self.n})"
	end 'report'
end 'Store'

function forward(h Store.handle, buf String)
	try h.keep("{buf}") otherwise panic("a running service with room takes the send")
end 'forward'

function main() returns ExitCode
	let h = spawn Store.create()
	let s = "hello"
	forward(h, buf: s)
	let kept = try await h.report() otherwise panic("the service holds the value and must answer for it")
	print("caller still has {s}\n")
	print("{kept}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
caller still has hello
kept hello (1)
```

<!-- test: error.use-after-await-of-a-reply -->
⭐⭐ **A REPLY IS A PROMISE, SO IT IS MOVE-ONLY TOO.** A reply cell is minted at the interned
`Promise with (T[, E])`, as a spawn's promise is. Minted as a bare `ValueTypeTag.integer` word instead,
`requireBindingLive`'s scalar early-return would swallow the consume poison entirely: this program would
compile and exit **8** — `isComplete()` answering `true` off a reclaimed cell.

⚠ **ITS SPAWN TWIN IS E3102 TOO**: the same five lines over `async makeValue()` are refused, and over
`h.total()` they must be refused alike. Linearity (E3100)
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
	let p = try h.total() otherwise panic("a running service with room takes the send")
	let a = await p
	let b = p
	let done = 1 if b.isComplete() else 0
	return (a + done) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3142: <fragment>:18:10: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: error.return-a-reply-as-its-result-type -->
A reply is not its result. `grab` is declared `returns Integer` and returns `h.total()`, which is the reply —
the value that will eventually produce an `Integer`, not an `Integer`. An untyped cell word would let this
compile and print the green thread's raw cell address (a different number on every run), which is the wrong
answer this pins against.
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
	return 0
end 'main'
public typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:15:2: Cannot return 'Promise with int' from function declared to return 'Integer'
```

<!-- test: error.arithmetic-on-a-reply -->
A reply is not a number, so it has no arithmetic. On an untyped cell word, `p + 1` would be pointer
arithmetic on a green-thread cell address.
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
	let p = try h.total() otherwise panic("a running service with room takes the send")
	let bumped = p + 1
	print("bumped {bumped}")
	return (await p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2004: <fragment>:17:17: Cannot operate on Promise with int and int
```

<!-- test: error.a-reply-in-an-integer-parameter -->
An `Integer` parameter does not take a reply. The cure is to `await` it and pass the RESULT — which is also
the only spelling that keeps the cell's one owner intact. A reply renders as a `Promise` instance over its result.
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
	return takesInt(try h.total() otherwise panic("a running service with room takes the send")) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:20:9: argument type mismatch for 'n': expected 'Integer', got 'Promise with int'
```

<!-- test: error.clone-a-reply -->
⭐ The one that would be a double-reclaim rather than merely a wrong type. A reply cell is the two-party
teardown rendezvous between the awaiter and the service loop, and exactly one owner may arrive at it; a
`p.clone()` would hand back a second copy of the cell word, with nothing to say which of the two owned it.
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
	let p = try h.total() otherwise panic("a running service with room takes the send")
	let copy = p.clone()
	print("copied {copy.isComplete()}")
	return (await p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3141: <fragment>:17:15: a promise cannot be borrowed through 'clone': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. `Promise with int` is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: a-reply-inner-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
A reply's `.inner` is library-tier surface: `promise-typing.md:promise-typing.error.inner-is-library-tier`
is this program refused in the user tier. A library-tier source reads it as the reply → `int` conversion. It peeks at the cell word without consuming
the reply, so the `await` that follows still reclaims it and the program still balances to zero.
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
	let p = try h.total() otherwise panic("a running service with room takes the send")
	let named = p.inner > 0
	print("names a thread {named}")
	return (await p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```stdout
names a thread true
```

<!-- test: a-handler-that-awaits-its-coroutine-before-it-returns-leaves-nothing-in-flight -->
<!-- procs: 4 -->
A handler may start a coroutine and yield to it, as long as it `await`s the coroutine before it returns: the result goes
in the state and nothing in flight outlives the message. The service may be shut down with the coroutine long done, and the
process ends cleanly (exit 0 also says no coroutine record was lost).
```maxon
typealias Integer = int(i64.min to i64.max)

function work(n Integer) returns Integer
	sleep(1)
	return n * 2
end 'work'

type Worker
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(v Integer) returns Integer
		let p = async work(v)
		Scheduler.yield()
		self.total = self.total + (await p)
		return self.total
	end 'add'
end 'Worker'

function main() returns ExitCode
	let w = spawn Worker.create()
	var last = 0 as Integer

	for round in 1 to 4 'rounds'
		last = try await w.add(round) otherwise 0
	end 'rounds'

	print("total={last}\n")
	w.shutdown()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total=20
```

<!-- test: an-idle-service-holds-no-green-thread -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐⭐ **A SERVICE WITH NOTHING QUEUED OWNS NO GREEN THREAD.** A hundred services are spawned one after another, each answers
one reply, and every handle is kept alive. A service whose queue is empty is parked nowhere: the green thread that took its
last message has ended and its record is back on a free list, so the next service's turn reuses it. At one processor with
preemption off the scheduler's arena carves one fixed chunk of records, however many services exist: one service and two
hundred carve the same number. A service that kept a thread parked for its next message would carve one record each on top
of that chunk, so a hundred services would carve more than a hundred.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ProbeHandles = Array with Probe.handle

let services = 100

let carveBound = 64

type Probe
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function value() returns Integer
		return self.n
	end 'value'
end 'Probe'

function main() returns ExitCode
	let handles = ProbeHandles.create(0)
	var total = 0 as Integer

	for i in 0 upto services 'eachService'
		let h = spawn Probe.create(i)
		total = total + (try await h.value() otherwise 0)
		handles.push(h)
	end 'eachService'

	let carved = Scheduler.counters().threadRecordsCarved as Integer
	print("total={total} idleServicesHoldNoThread={carved < carveBound}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total=4950 idleServicesHoldNoThread=true
```

<!-- test: known-gap-a-promise-captured-by-a-closure-in-service-state-aborts-when-the-state-drops -->
⚠ **A KNOWN GAP: THE STATE-FIELD REFUSAL SEES A PROMISE IN A FIELD'S TYPE, NOT ONE A CLOSURE CAPTURED.** `Keeper.size` is a
function-typed field, so no `Promise` appears in the state's type and the build accepts it. The factory starts a coroutine,
keeps its promise in a local array, and stores a closure that captures the array. The coroutine belongs to the strand that ran
the factory, and the state is dropped by the runner that finishes the service, so the drop reaches a promise from a green
thread that does not hold its strand and the runtime aborts with exit **70**. The abort is the backstop that stands in for a
build refusal.
```maxon
typealias Count = int(0 to u64.max)
typealias Sizer = function() returns Count
typealias PromiseArray = Array with (Promise with Count)

function compute() returns Count
	sleep(1)
	return 3
end 'compute'

type Keeper
	var size as Sizer

	static function create() returns Self
		let ps = PromiseArray.create(0)
		ps.push(async compute())
		let w = function() gives ps.count() as Count
		return Self{size: w}
	end 'create'

	export function reading() returns Count
		return self.size()
	end 'reading'
end 'Keeper'

function main() returns ExitCode
	let h = spawn Keeper.create()
	let n = try await h.reading() otherwise 0
	print("reading={n}\n")
	return 0
end 'main'
```
```exitcode
70
```
```stdout
reading=1
```
```stderr
fatal error: promise dropped by a machine that does not hold its strand
```

<!-- test: a-cown-yields-its-processor-after-a-batch -->
<!-- procs: 1 -->
<!-- preempt: off -->
⭐⭐ **A SERVICE TAKES AT MOST 64 MESSAGES PER TURN AND THEN LETS EVERY OTHER RUNNABLE SERVICE RUN.** At one processor with
preemption off `main` queues 5000 `tick` messages to each of two services before it awaits, so both have a backlog far
longer than one batch. Each handler counts its own messages and, on its 65th, records how far the other service has
counted. A service that drained its whole backlog before yielding would reach its 65th message while the other had
counted nothing (the first service records 0); batches of 64 interleave the two, so both record at least 64. Which service
runs its first batch first is not pinned: either order leaves the second batch of the first service to see the other's
first batch.
```maxon
typealias Count = int(0 to u64.max)
typealias CountCell = SharedValue with Count

let backlog = 5000
let batchPlusOne = 65

type Ticker
	var n as Count
	let mine as CountCell
	let theirs as CountCell
	let seen as CountCell

	static function create(mine CountCell, theirs CountCell, seen CountCell) returns Self
		return Self{n: 0, mine: mine, theirs: theirs, seen: seen}
	end 'create'

	export function tick()
		self.n = self.n + 1
		self.mine.publish(self.n)

		if self.n == batchPlusOne 'recordTheOther'
			self.seen.publish(self.theirs.current())
		end 'recordTheOther'
	end 'tick'

	export function done() returns Count
		return self.n
	end 'done'
end 'Ticker'

function main() returns ExitCode
	let countA = CountCell.create(0 as Count)
	let countB = CountCell.create(0 as Count)
	let seenA = CountCell.create(0 as Count)
	let seenB = CountCell.create(0 as Count)
	let a = spawn(capacity: 8192) Ticker.create(countA.clone(), theirs: countB.clone(), seen: seenA.clone())
	let b = spawn(capacity: 8192) Ticker.create(countB.clone(), theirs: countA.clone(), seen: seenB.clone())

	for _ in 0 upto backlog 'fill'
		try a.tick() otherwise panic("a capacity of 8192 takes 5000 sends")
	end 'fill'

	for _ in 0 upto backlog 'fillB'
		try b.tick() otherwise panic("a capacity of 8192 takes 5000 sends")
	end 'fillB'

	let doneA = try await a.done() otherwise 0
	let doneB = try await b.done() otherwise 0
	print("done={doneA} {doneB} interleaved={seenA.current() >= 64 and seenB.current() >= 64}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done=5000 5000 interleaved=true
```

<!-- test: error.a-service-field-holding-a-promise-array-is-refused -->
A service's state is run by whichever green thread takes its next message, and a coroutine belongs to the strand that started it,
so a promise kept in the state could be awaited or dropped by a strand that does not own its coroutine. A field of an array of
promises is refused at the `spawn`; `await` the promise before the message returns and keep its result in the field.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	sleep(1)
	return 5
end 'work'

type Svc
	var pending as IntPromiseArray

	static function create() returns Self
		return Self{pending: IntPromiseArray.create(0)}
	end 'create'

	export function start() returns Integer
		self.pending.push(async work())
		return 1
	end 'start'

	export function finish() returns Integer
		return self.pending.count() as Integer
	end 'finish'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let a = try await h.start() otherwise 0
	let b = try await h.finish() otherwise 0
	return (a + b) as ExitCode
end 'main'
```
```maxoncstderr
error E3216: <fragment>:29:10: `Svc.pending` is a `IntPromiseArray` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Svc` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: error.a-service-field-holding-a-promise-is-refused -->
The same rule for a field whose type is a `Promise` itself.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	sleep(1)
	return 5
end 'work'

type Svc
	var held as IntPromise

	static function create() returns Self
		return Self{held: async work()}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3216: <fragment>:23:10: `Svc.held` is a `IntPromise` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Svc` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: error.a-service-field-holding-a-record-that-holds-a-promise-is-refused -->
A record that holds a promise is as much a promise in the state as the promise itself.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	sleep(1)
	return 5
end 'work'

type Holder
	export var held as IntPromise

	static function create() returns Self
		return Self{held: async work()}
	end 'create'
end 'Holder'

type Svc
	var slot as Holder

	static function create() returns Self
		return Self{slot: Holder.create()}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3216: <fragment>:31:10: `Svc.slot` is a `Holder` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Svc` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: main-is-never-run-on-top-of-a-coroutine-awaiting-a-service -->
<!-- procs: 1 -->
⛔⛔ **NOTHING RUNS ON TOP OF A PARKED GREEN THREAD, SO `main` MAY WAKE WHILE ITS OWN COROUTINES ARE WAITING ON A
SERVICE THAT IS ITSELF WAITING.** Two coroutines of `main` each await `Slow.get`, which parks them both.
`Slow.get` publishes that it has begun and then waits for `main` to publish the release; `main`'s `sleep(1)` polls
end while the coroutines are parked waiting out `Slow`, and `main` is readied like any other green thread and
runs on its own stack, switched in from a machine's scheduler context — never on a coroutine's. The release comes
only after `main` has woken on seeing `Slow` begin, so no clock decides whether `main` woke while the coroutines
were parked. An await that ran whatever it took off a run queue on the awaiter's own stack would run `main` on top
of the coroutine, and `main`'s `await r1` — a reply only that coroutine can finish — would wait on the frame it
stands on: such a runtime exits **92** (`schedulerDeadlock`) on one processor. Here both replies arrive.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias FlagCell = SharedValue with bool

let pollMs = 1

type Slow
	let begun as FlagCell
	let released as FlagCell

	static function create(begun FlagCell, released FlagCell) returns Self
		return Self{begun: begun, released: released}
	end 'create'

	export function get() returns Integer
		self.begun.publish(true)

		while not self.released.current() 'untilReleased'
			sleep(pollMs)
		end 'untilReleased'

		return 7
	end 'get'
end 'Slow'

function askSlow(s Slow.handle) returns Integer
	return try await s.get() otherwise 0
end 'askSlow'

function main() returns ExitCode
	let begun = FlagCell.create(false)
	let released = FlagCell.create(false)
	let s = spawn Slow.create(begun.clone(), released: released.clone())
	let r1 = async askSlow(s.clone())

	while not begun.current() 'untilSlowBegins'
		sleep(pollMs)
	end 'untilSlowBegins'

	let r2 = async askSlow(s.clone())
	released.publish(true)
	let a = await r1
	let b = await r2
	print("a={a} b={b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=7 b=7
```

<!-- test: an-unsent-message-that-calls-the-library-still-compiles -->
**A MESSAGE NOBODY SENDS IS STILL REACHABLE, BECAUSE THE SERVICE'S BEHAVIOUR DISPATCHES IT.** `spawn` makes the
synthesized `Reader.__behave` live, and its switch names every message the type exports, so `read` is
code the program keeps whether or not any handle ever calls it. Its call to `String.count` is therefore a live
edge into the standard library. The compiler decides which library bodies to build by one reachability walk
and which functions to keep by another; both must follow the loop's dispatch, or the second keeps a function
whose body the first never built.
```maxon
typealias Integer = int(i64.min to i64.max)

type Reader
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = "hello"
		return line.count() as Integer
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	_ = reader
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: an-unsent-message-of-a-generic-service-that-calls-the-library-still-compiles -->
The same edge through a GENERIC service. The loop dispatches a message by its base name whatever the
instantiation, so the library body its unsent message reaches must be built for every instantiation the
program spawns.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function label() returns Integer
		let text = "boxed"
		return text.count() as Integer
	end 'label'
end 'Box'

function main() returns ExitCode
	let small = spawn Box.create(1)
	let named = spawn Box.create("one")
	_ = small
	_ = named
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: a-message-that-spawns-a-service-keeps-that-services-unsent-messages-alive -->
The edge is transitive. `Outer.start` is sent, and its body spawns `Inner`, so `Inner.__behave` is live and every
message it dispatches is reached — including `Inner.read`, which nobody sends and which calls the library.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = "hello"
		return line.count() as Integer
	end 'read'
end 'Inner'

type Outer
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function start() returns Integer
		let inner = spawn Inner.create()
		_ = inner
		return 7
	end 'start'
end 'Outer'

function main() returns ExitCode
	let outer = spawn Outer.create()
	let r = try await outer.start() otherwise 0
	return r as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: services.a-reply-built-by-a-sibling-method-is-fresh -->
A message that returns what a private sibling method built returns a record nothing but the reply names:
the sibling constructs it and keeps no reference, so it crosses to the caller as its one owner.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns isolated Report
		return build(n)
	end 'measure'

	function build(n Tally) returns isolated Report
		return Report.create(n * self.scale)
	end 'build'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42
```

<!-- test: services.a-reply-built-by-a-free-function-is-fresh -->
The same through a free function: a callee that returns a record it just constructed hands back a fresh one.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

function buildReport(n Tally) returns Report
	return Report.create(n * 2)
end 'buildReport'

type Worker
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function measure(n Tally) returns isolated Report
		self.calls = self.calls + 1
		return buildReport(n)
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42
```

<!-- test: services.a-spawn-factory-may-delegate-to-another-factory -->
The factory a `spawn` names may return what another static factory of the type built, including through
`try … otherwise panic`: the delegate constructs the record and keeps nothing, so the service still
becomes its one owner.
```maxon
typealias Tally = int(0 to u64.max)

enum SetupError implements Error
	zeroScale
end 'SetupError'

type Worker
	var scale as Tally

	static function prepare(scale Tally) returns Self throws SetupError
		if scale == 0 'zero'
			throw SetupError.zeroScale
		end 'zero'

		return Self{scale: scale}
	end 'prepare'

	static function create(scale Tally) returns Self
		return try prepare(scale) otherwise panic("main checks the scale before it spawns a Worker")
	end 'create'

	export function measure(n Tally) returns Tally
		return n * self.scale
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create(2)
	let total = try await worker.measure(21) otherwise panic("the worker is running")
	print("{total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42
```

<!-- test: error.reply-forwarded-from-a-sibling-that-returns-state-refused -->
A reply forwarded from a sibling is only as fresh as what the sibling returns, and `current` returns the
service's own `report` — the caller would hold a second reference to it.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var report as Report

	static function create() returns Self
		return Self{report: Report.create(42)}
	end 'create'

	export function measure() returns immutable Report
		return current()
	end 'measure'

	function current() returns Report
		return self.report
	end 'current'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure() otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:20:3: a return does not fit the declared result: the returned value is read-only, the result is immutable
```

<!-- test: error.reply-forwarded-through-a-function-returning-its-parameter-refused -->
`pass` builds nothing: it hands back the record it was given, which is the service's own `report`.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

function pass(report Report) returns Report
	return report
end 'pass'

type Worker
	var report as Report

	static function create() returns Self
		return Self{report: Report.create(42)}
	end 'create'

	export function measure() returns immutable Report
		return pass(self.report)
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure() otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:24:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.reply-forwarded-from-a-sibling-that-keeps-the-record-refused -->
`build` constructs the record but also stores it in the service's `history`, so the record it returns has
a second owner.
```maxon
typealias Tally = int(0 to u64.max)
typealias ReportArray = Array with Report

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var history as ReportArray

	static function create() returns Self
		return Self{history: ReportArray.create(0)}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return build(n)
	end 'measure'

	function build(n Tally) returns Report
		var report = Report.create(n * 2)
		self.history.push(report)
		return report
	end 'build'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:21:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.reply-forwarded-to-a-local-closure-named-like-a-fresh-function-refused -->
The `buildReport` that `build` calls is its own local closure, which hands back the service's `report`,
not the free function of that name that builds one — so `build` is not fresh, and neither is the reply.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

function buildReport(n Tally) returns Report
	return Report.create(n * 2)
end 'buildReport'

type Worker
	var report as Report

	static function create() returns Self
		return Self{report: buildReport(21)}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return build(n)
	end 'measure'

	function build(n Tally) returns Report
		let saved = self.report
		let buildReport = function(_ Tally) gives saved
		return buildReport(n)
	end 'build'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:24:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.spawn-factory-delegating-to-a-factory-that-returns-its-parameter-refused -->
`create` delegates to `adopt`, which builds nothing: it returns the `Worker` it was handed. The `spawn` moves that
argument to the service's thread, so `main` may not read `seed` afterwards.
```maxon
typealias Tally = int(0 to u64.max)

type Worker
	var scale as Tally

	static function make(scale Tally) returns Self
		return Self{scale: scale}
	end 'make'

	static function adopt(seed Worker) returns Self
		return seed
	end 'adopt'

	static function create(seed isolated Worker) returns Self
		let ownedSeed = merge seed

		return adopt(ownedSeed)
	end 'create'

	export function measure(n Tally) returns Tally
		return n * self.scale
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let seed = Worker.make(2) as isolated Worker
	let worker = spawn Worker.create(seed)
	let total = try await worker.measure(21) otherwise panic("the worker is running")
	print("{total} {seed.scale}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:18: use of moved value 'seed': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.spawn-factory-argument-borrowed-from-a-parameter-refused -->
`start` borrows `shelf` from `main`, and the `spawn` moves its factory's arguments to the service, so `shelf`
would be shared with `main` across two green threads.
```maxon
typealias Tally = int(0 to u64.max)

type Shelf
	export var count as Tally

	static function create(count Tally) returns Self
		return Self{count: count}
	end 'create'
end 'Shelf'

type Keeper
	var shelf as Shelf

	static function create(shelf isolated Shelf) returns Self
		return Self{shelf: merge shelf}
	end 'create'

	export function count() returns Tally
		return self.shelf.count
	end 'count'
end 'Keeper'

function start(shelf Shelf) returns Tally
	let k = spawn Keeper.create(shelf)
	let n = try await k.count() otherwise panic("the keeper is running")
	k.shutdown()
	return n
end 'start'

function main() returns ExitCode
	let shelf = Shelf.create(3)
	let n = start(shelf)
	print("{n} {shelf.count}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:25:30: 'shelf' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.spawn-factory-argument-read-off-an-earlier-statements-temporary-refused -->
`settings` was read out of a temporary `Holder` one statement before the `spawn`, so that `Holder` lives to the
end of `main` and still holds `settings` while the service runs. Only a temporary built inside the factory's
own argument list is released at the `spawn`.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export let settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Surveyor
	var settings as Settings

	static function create(settings isolated Settings) returns Self
		return Self{settings: merge settings}
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let settings = Holder.create(Settings.create(true)).settings
	let surveyor = spawn Surveyor.create(settings)
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	print("{total}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:34:39: 'settings' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: a-spawn-factory-string-argument-borrowed-from-a-parameter-is-copied -->
A borrowed `String` a factory keeps in the service's state is copied, exactly as a message argument is
(`a-borrowed-string-parameter-is-copied-into-the-send`): the service owns its own record and `name` stays
readable in `start`. Every other borrowed argument is still refused
(`error.spawn-factory-argument-borrowed-from-a-parameter-refused`).
```maxon
type Greeter
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	export function greet() returns String
		return "hello {self.name}"
	end 'greet'
end 'Greeter'

function start(name String) returns String
	let g = spawn Greeter.create(name)
	let text = try await g.greet() otherwise panic("the greeter is running")
	g.shutdown()
	return text
end 'start'

function main() returns ExitCode
	print("{start("ada")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello ada
```

<!-- test: services.a-spawn-factory-argument-read-two-fields-off-a-temporary-starts-once -->
A factory argument read two fields deep out of a temporary built in the factory's own argument list: the
`Outer` is released at the `spawn`, and with it the `Holder` it held, so the `Settings` has the service as its
one owner.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export var settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Outer
	export var holder as Holder

	static function create(holder Holder) returns Self
		return Self{holder: holder}
	end 'create'
end 'Outer'

type Surveyor
	var settings as Settings

	static function create(settings isolated Settings) returns Self
		return Self{settings: merge settings}
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(Outer.create(Holder.create(Settings.create(true))).holder.settings.clone())
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	print("{total}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: error.reply-forwarded-through-a-local-named-like-a-type-refused -->
Inside `build`, `Report` is a local holding the service's own record, so `Report.echo(1)` calls the INSTANCE
`echo` on it — which returns that record — and not the static `make` that builds one.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function make(amount Tally) returns Self
		return Self{total: amount}
	end 'make'

	function echo(amount Tally) returns Report
		self.total = self.total + amount
		return self
	end 'echo'
end 'Report'

type Worker
	var report as Report

	static function create() returns Self
		return Self{report: Report.make(41)}
	end 'create'

	export function measure() returns immutable Report
		return build()
	end 'measure'

	function build() returns Report
		var Report = self.report
		return Report.echo(1)
	end 'build'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure() otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:25:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: services.a-reply-built-by-a-user-type-named-like-a-stdlib-type-is-fresh -->
A program's own `type Clock` moves the stdlib `Clock` out of its way, so `Clock.make` here is the program's
factory and nothing else: the reply `build` forwards is still one it just constructed.
```maxon
typealias Tally = int(0 to u64.max)

type Clock
	export var ticks as Tally

	static function make(ticks Tally) returns Self
		return Self{ticks: ticks}
	end 'make'
end 'Clock'

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns isolated Clock
		return build(n)
	end 'measure'

	function build(n Tally) returns isolated Clock
		return Clock.make(n * self.scale)
	end 'build'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let clock = try await worker.measure(21) otherwise panic("the worker is running")
	print("{clock.ticks}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42
```

<!-- test: services.an-or-arm-matches-a-handler-error-merged-with-the-send-errors -->
An awaited message's error is the handler's own error set merged with the send's `mailboxFull` and
`serviceStopped`, and a `match` over it takes the same `or` arms a match over any error enum does.
```maxon
typealias Tally = int(0 to u64.max)

enum MathError implements Error
	divideByZero
end 'MathError'

type Calc
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function divide(n Tally, by Tally) returns Tally throws MathError
		self.calls = self.calls + 1

		if by == 0 'zero'
			throw MathError.divideByZero
		end 'zero'

		return try (n / by) otherwise throw MathError.divideByZero
	end 'divide'
end 'Calc'

function main() returns ExitCode
	let calc = spawn Calc.create()

	let value = try await calc.divide(10, by: 0) otherwise (e) 'failed'
		match e 'why'
			divideByZero or
				serviceStopped then print("no answer\n")
			mailboxFull then print("full\n")
		end 'why'

		return 1
	end 'failed'

	print("{value}\n")
	calc.shutdown()
	return 0
end 'main'
```
```stdout
no answer
```
```exitcode
1
```

<!-- test: services.a-reply-built-from-a-local-var-crosses-once -->
A message that fills a local `var` and returns a record built around it hands the caller the only owner of
that record: the local was moved into the record, so nothing on the service's green thread still names it.
```maxon
typealias Flags = Array with bool
typealias Tally = int(0 to 1000)

type Cell
	export var flags as Flags

	static function create(flags isolated Flags) returns Self
		return Self{flags: merge flags}
	end 'create'
end 'Cell'

type Worker
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function report() returns isolated Cell
		self.calls = self.calls + 1
		let flags = Flags.create(0) as isolated Flags

		modify flags 'fill'
			flags.push(false)
			flags.push(true)
		end 'fill'

		return Cell.create(flags)
	end 'report'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let cell = try await worker.report() otherwise panic("the worker is running")
	print("{cell.flags.count()}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: services.a-reply-built-over-a-message-parameter-crosses-once -->
A reply may hold a message parameter: the loop releases the message's reference to the argument before the
reply is handed to the caller, so the caller becomes the only owner of everything the reply reaches.
```maxon
typealias Flags = Array with bool
typealias Tally = int(0 to 1000)

type Cell
	export var flags as Flags

	static function create(flags isolated Flags) returns Self
		return Self{flags: merge flags}
	end 'create'
end 'Cell'

type Worker
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function wrap(flags isolated Flags) returns isolated Cell
		self.calls = self.calls + 1
		return Cell.create(flags)
	end 'wrap'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let flags = Flags.create(0) as isolated Flags

	modify flags 'fill'
		flags.push(true)
	end 'fill'

	let cell = try await worker.wrap(flags) otherwise panic("the worker is running")
	print("{cell.flags.count()}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: error.a-generic-reply-that-is-the-services-own-state-is-refused-at-the-spawn -->
The same rule where the substitution is a container: `T` is an `Array with bool`, so the array the state
names is the array the reply hands back. The `spawn` that fixes `T` refuses it, before the service runs.
```maxon
typealias Flags = Array with bool

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
	var flags = Flags.create(0)
	flags.push(true)
	let h = spawn Box.create(flags)
	let got = try await h.peek() otherwise panic("the box is running")
	print("{got.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:19:10: the message `Box.peek` returns a `Flags` with no capability word, and this `spawn` makes `Box` a service — whose reply is handed to the green thread that awaits it, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `returns isolated …` or `returns immutable …`
```

<!-- test: services.a-reply-of-records-holding-an-interface-field-crosses -->
A reply typed `List with Holder`, whose elements are records holding a value at an interface type, crosses and
dispatches: the loop walks each element's `Shape` field through the witness beside it, and the caller reads
the conformer's answer back out of the element it now owns alone.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

type Holder
	var shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'

	function area() returns Integer
		return self.shape.area()
	end 'area'
end 'Holder'

typealias Holders = List with Holder

type Maker
	var items as isolated Holders

	static function create(side Integer) returns Self
		let items = Holders.create() as isolated Holders

		modify items 'fill'
			items.append(Holder.create(Square.create(side)))
		end 'fill'

		return Self{items: items}
	end 'create'

	export function drain() returns isolated Holders
		return swap self.items with Holders.create()
	end 'drain'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create(2)
	let items = try await h.drain() otherwise panic("the maker is running")

	modify items 'read'
		let holder = try items.first() otherwise panic("the maker held one item")
		print("{items.count()} {holder.area()}\n")
	end 'read'

	h.shutdown()
	return 0
end 'main'
```
```stdout
1 4
```

<!-- test: error.a-generic-factory-argument-that-is-an-os-handle-is-refused -->
The factory's argument fixes `T`, and it crosses to the service's thread. A `MachinePin`'s machine pin is walked by
no per-type walk that could prove it sole, so the `spawn` refuses the argument (E3217). A socket is not such a
handle: its box counts its owners, and a `TcpListener` crosses when its sender holds it alone.
```maxon
typealias Integer = int(i64.min to i64.max)

type Maker uses T
	export typealias Items = Array with T
	var seen as Integer

	static function create(seed T) returns Self
		var first = Items.create(0)
		first.push(seed)
		return Self{seen: first.count() as Integer}
	end 'create'

	export function make() returns Integer
		self.seen = self.seen + 1
		return self.seen
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create(MachinePin.acquire())
	let items = try await h.make() otherwise panic("the maker is running")
	print("{items}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:21:10: parameter `seed` of the factory `Maker.create` is a `MachinePin` whose graph reaches an OS handle other than a socket, or a base-struct-less generic instance, which cannot cross to another green thread, and this `spawn` moves its arguments into the state of `Maker`, a service — which another green thread runs. Pass a value whose graph holds no function value, promise or thread-bound handle, or drop the parameter from the factory
```

<!-- test: services.a-reply-a-sibling-builds-in-a-local-is-fresh -->
A sibling that builds its record in a local, fills it, and returns the local hands back a record nothing
else names: the local is the only reference and the `return` moves it out.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns isolated Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns isolated Report
		let report = Report.create(n * self.scale) as isolated Report

		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42
```

<!-- test: services.a-reply-a-sibling-fills-in-a-var-is-fresh -->
The same with a `var` the sibling fills before returning it — pushing into the record's own array field
and assigning a scalar field keep the record the local's alone.
```maxon
typealias Tally = int(0 to u64.max)
typealias LineArray = Array with String

type Report
	export var total as Tally
	export var lines as LineArray

	static function create() returns Self
		return Self{total: 0, lines: LineArray.create(0)}
	end 'create'
end 'Report'

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns isolated Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns isolated Report
		let scaled = n * self.scale
		let report = Report.create() as isolated Report

		modify report 'fill'
			report.lines.push("scaled")
			report.total = scaled
		end 'fill'

		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total} {report.lines.count()}\n")
	worker.shutdown()
	return 0
end 'main'
```
```stdout
42 1
```

<!-- test: error.reply-a-sibling-builds-in-a-local-it-also-keeps-refused -->
`summarize` builds its record in a local but pushes the local into the service's `history` before
returning it, so the record has a second owner.
```maxon
typealias Tally = int(0 to u64.max)
typealias ReportArray = Array with Report

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var history as ReportArray

	static function create() returns Self
		return Self{history: ReportArray.create(0)}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		var report = Report.create(n * 2)
		self.history.push(report)
		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:21:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.reply-a-sibling-returns-a-local-another-local-aliases-refused -->
`copy` names the same record as `report`, so returning `report` does not hand back the only reference.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

type Worker
	var last as Tally

	static function create() returns Self
		return Self{last: 0}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let report = Report.create(n * 2)
		let copy = report
		self.last = copy.total
		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:20:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.reply-a-sibling-returns-a-local-its-own-method-stored-refused -->
`keepIn` is an instance method, so it is handed the record itself — and it pushes `self` into the service's
`history`. A method called on the local may keep it, so the local is not fresh.
```maxon
typealias Tally = int(0 to u64.max)
typealias ReportArray = Array with Report

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	function keepIn(history ReportArray)
		history.push(self)
	end 'keepIn'
end 'Report'

type Worker
	var history as ReportArray

	static function create() returns Self
		return Self{history: ReportArray.create(0)}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		var report = Report.create(n * 2)
		report.keepIn(self.history)
		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:25:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: error.reply-a-sibling-returns-a-local-it-keys-a-map-with-refused -->
`summarize` writes its local as a KEY of the map literal it stores in the service's `ranks`. After a `,` a key
is spelled exactly like a named argument's label, but it stores the record, so the local is not fresh.
```maxon
typealias Tally = int(0 to 1000000)
typealias ReportRanks = Map with (Report, Tally)

type Report implements Hashable, Equatable
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	function hash() returns HashValue
		return total
	end 'hash'

	function equals(other Self) returns bool
		return total == other.total
	end 'equals'
end 'Report'

type Worker
	var ranks as ReportRanks

	static function create() returns Self
		return Self{ranks: ReportRanks.create()}
	end 'create'

	export function measure(n Tally) returns immutable Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		var first = Report.create(n)
		var report = Report.create(n * 2)
		self.ranks = [first: 1 as Tally, report: 2 as Tally]
		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let report = try await worker.measure(21) otherwise panic("the worker is running")
	print("{report.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:29:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: services.a-reply-whose-local-shares-a-field-with-the-state-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
typealias Tally = int(0 to 1000000)

type Inner
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Report
	export var inner as Inner

	static function create(n Tally) returns Self
		return Self{inner: Inner.create(n)}
	end 'create'
end 'Report'

type Worker
	var kept as Inner

	static function create() returns Self
		return Self{kept: Inner.create(0)}
	end 'create'

	export function measure(n Tally) returns isolated Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		var report = Report.create(n)
		self.kept = report.inner
		return report
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let got = try await worker.measure(21) otherwise panic("the worker is running")
	print("{got.inner.n}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:28:10: 'summarize(n)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: services.a-service-state-may-hold-a-value-at-an-interface-type -->
A service's own state may hold a value at an interface type: the `spawn` walks it through the conformer's
witness, so the record the factory built crosses whole and dispatches on the service's green thread.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

type Surveyor
	var shape as Shape

	static function create(side Tally) returns Self
		return Self{shape: Square.create(side)}
	end 'create'

	export function measure() returns Tally
		return self.shape.area()
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(3)
	let area = try await surveyor.measure() otherwise panic("the surveyor is running")
	print("{area}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
9
```

<!-- test: services.a-reply-may-be-a-value-at-an-interface-type -->
A reply held at an interface type is a record the handler built, handed back through its witness: the
caller owns the conformer and dispatches through it.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

type Factory
	var made as Tally

	static function create() returns Self
		return Self{made: 0}
	end 'create'

	export function make(side Tally) returns isolated Shape
		self.made = self.made + 1
		return Square.create(side)
	end 'make'
end 'Factory'

function main() returns ExitCode
	let factory = spawn Factory.create()
	let shape = try await factory.make(4) otherwise panic("the factory is running")
	print("{shape.area()}\n")
	factory.shutdown()
	return 0
end 'main'
```
```stdout
16
```

<!-- test: services.a-sent-interface-argument-crosses-through-its-witness -->
A `let` binding held at an interface type is moved by the send, exactly as a `let` record is: the send walks it
through its witness, and the service dispatches through it.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

function squareOf(side Tally) returns Shape
	return Square.create(side)
end 'squareOf'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Tally
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let shape = squareOf(3) as isolated Shape
	let mine = shape.area()
	try h.measure(shape) otherwise panic("a running service with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("{total} {mine}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
9 9
```

<!-- test: services.a-second-owner-inside-a-conformer-is-refused -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Cell
	export var n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias Cells = List with Cell

type Crate implements Shape
	var cells as Cells

	static function create(cells Cells) returns Self
		return Self{cells: cells}
	end 'create'

	function area() returns Tally
		return self.cells.count()
	end 'area'
end 'Crate'

function crateOf(cells Cells) returns Shape
	return Crate.create(cells)
end 'crateOf'

type Svc
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(s isolated Shape)
		self.seen = s.area()
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cells = Cells.create()
	var cell = Cell.create()
	cells.append(cell)
	let h = spawn Svc.create()
	var shape = crateOf(cells)
	try h.take(shape) otherwise panic("a running service with room takes the send")
	return cell.n as ExitCode
end 'main'
```
```maxoncstderr
error E3200: <fragment>:52:13: 'shape' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: services.a-conformer-holding-a-string-crosses -->
A conformer whose record holds a `String` is walked into that record as a field of it is, and crosses whole.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Label implements Shape
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function area() returns Tally
		return self.text.byteLength()
	end 'area'
end 'Label'

function labelOf(width Tally) returns Shape
	return Label.create("w{width}")
end 'labelOf'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Tally
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let shape = labelOf(1234) as isolated Shape
	try h.measure(shape) otherwise panic("a running service with room takes the send")
	let total = try await h.total() otherwise panic("the service is running")
	print("{total}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
5
```

<!-- test: services.an-interface-reply-nobody-awaits-is-released -->
A reply held at an interface type that no one awaits is released with its cell, through the witness the cell
carries beside the value: the conformer's heap `String` is freed, so the program exits clean rather than
leaking (exit 101).
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Label implements Shape
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function area() returns Tally
		return self.text.byteLength()
	end 'area'
end 'Label'

type Factory
	var made as Tally

	static function create() returns Self
		return Self{made: 0}
	end 'create'

	export function make(width Tally) returns isolated Shape
		self.made = self.made + 1
		return Label.create("w{width}")
	end 'make'

	export function count() returns Tally
		return self.made
	end 'count'
end 'Factory'

function main() returns ExitCode
	let factory = spawn Factory.create()
	_ = try factory.make(1234) otherwise panic("a running service with room takes the send")
	let made = try await factory.count() otherwise panic("the factory is running")
	print("{made}\n")
	factory.shutdown()
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: error.a-message-argument-that-does-not-implement-the-interface -->
A message argument at an interface-typed parameter widens as a call argument does, so a value whose type does
not conform is refused.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.measure(7 as Tally) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:16: argument type mismatch for 's' of the message 'measure': type 'Tally' does not implement interface 'Shape'
```

<!-- test: error.a-generic-service-spawned-over-an-interface-type-is-refused -->
A `spawn` reads a generic service's type arguments off its factory's arguments, and a value held at an interface
type cannot be one: the instance would stand a two-word fat pointer in a slot a type parameter gives one word.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

function squareOf(side Tally) returns Shape
	return Square.create(side)
end 'squareOf'

type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function size() returns Tally
		return 1
	end 'size'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(squareOf(2))
	let n = try await h.size() otherwise panic("the box is running")
	print("{n}\n")
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:37:10: Unsupported: a type argument inferred from a factory argument declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a slot standing at a type parameter is one machine word. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

<!-- test: services.an-interface-argument-sent-to-a-stopped-service-is-handed-back -->
A send to a service that has stopped fails with `serviceStopped` and hands the interface-typed argument back,
still held at its interface type: the arm calls through its witness, and when the binding dies the conformer's
heap `String` is released rather than leaked (exit 101). An awaited send to the same service fails the same way
and has no reply to release.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Label implements Shape
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function area() returns Tally
		return self.text.byteLength()
	end 'area'
end 'Label'

type Factory
	var made as Tally

	static function create() returns Self
		return Self{made: 0}
	end 'create'

	export function make(width Tally) returns isolated Shape
		self.made = self.made + 1
		return Label.create("w{width}")
	end 'make'

	export function inspect(s isolated Shape)
		self.made = self.made + s.area()
	end 'inspect'
end 'Factory'

function labelOf(width Tally) returns Shape
	return Label.create("w{width}")
end 'labelOf'

function main() returns ExitCode
	let factory = spawn Factory.create()
	var stops = 0 as Tally
	var area = 0 as Tally
	factory.shutdown()

	try factory.inspect(labelOf(5)) otherwise (e) 'refused'
		match e 'why'
			mailboxFull then stops = stops + 100
			serviceStopped(back) then area = back.area()
		end 'why'
	end 'refused'

	try await factory.make(1234) otherwise (e) 'gone'
		match e 'again'
			mailboxFull then stops = stops + 100
			serviceStopped then stops = stops + 1
		end 'again'
	end 'gone'

	return (stops * 10 + area) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: services.interface-arguments-each-take-their-own-payload-slot -->
Each interface-typed parameter of a message takes its witness half in the payload slot after its own, so a
scalar parameter between two of them still reads its own slot, and a moved binding and a moved temporary cross in
one send.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

type Label implements Shape
	var text as String

	static function create(text String) returns Self
		return Self{text: text}
	end 'create'

	function area() returns Tally
		return self.text.byteLength()
	end 'area'
end 'Label'

function squareOf(side Tally) returns Shape
	return Square.create(side)
end 'squareOf'

function labelOf(width Tally) returns Shape
	return Label.create("w{width}")
end 'labelOf'

type Calc
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function combine(a isolated Shape, scale Tally, b isolated Shape) returns Tally
		self.calls = self.calls + 1
		return a.area() * scale + b.area()
	end 'combine'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let sent = squareOf(2) as isolated Shape
	let sentArea = sent.area()
	let n = try await h.combine(sent, scale: 10, b: labelOf(123)) otherwise panic("the calc is running")
	print("{n} {sentArea}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
44 4
```

<!-- test: borrow.a-handler-may-write-a-sent-interface-argument -->
A handler may write an interface-typed parameter through its witness: the send moved the conformer to it.
```maxon
typealias Tally = int(0 to u64.max)

interface Counter
	function bump(mutable self)
	function value() returns Tally
end 'Counter'

type Tick implements Counter
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'

	function value() returns Tally
		return self.n
	end 'value'
end 'Tick'

function counterOf() returns Counter
	return Tick.create()
end 'counterOf'

type Svc
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function poke(c isolated Counter)
		c.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let c = counterOf() as isolated Counter
	try h.poke(c) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: borrow.a-handler-may-keep-a-sent-interface-argument -->
A handler may store an interface-typed parameter in its state: the send moved the conformer to it.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

function squareOf(side Tally) returns Shape
	return Square.create(side)
end 'squareOf'

type Keeper
	var last as Shape

	static function create() returns Self
		return Self{last: Square.create(1)}
	end 'create'

	export function keep(s isolated Shape)
		self.last = merge s
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let h = spawn Keeper.create()
	let s = squareOf(2) as isolated Shape
	try h.keep(s) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: services.a-generic-conformer-crosses-inside-an-interface-value -->
A value held at an interface type whose conformer is an instance of a generic type crosses and dispatches.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Wrapper uses T implements Shape
	var item as T
	var side as Tally

	static function create(item T, side Tally) returns Self
		return Self{item: item, side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Wrapper'

typealias Wrapped = Wrapper with Tally

type Surveyor
	var shape as Shape

	static function create(side Tally) returns Self
		return Self{shape: Wrapped.create(7, side: side)}
	end 'create'

	export function measure() returns Tally
		return self.shape.area()
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(3)
	let area = try await surveyor.measure() otherwise panic("the surveyor is running")
	print("{area}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
9
```

<!-- test: services.a-conformer-no-walk-reaches-is-refused-when-sent-at-its-interface-type -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	var pin as MachinePin

	static function create(pin MachinePin) returns Self
		return Self{pin: pin}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Port'

function portOf(pin MachinePin) returns Shape
	return Port.create(pin)
end 'portOf'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	var shape = portOf(MachinePin.acquire())
	try h.measure(shape) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:37:10: parameter `s` of the message `Calc.measure` is a `Shape` held at an interface type, whose conformer `Port` cannot cross to another green thread, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.a-conformer-no-walk-reaches-sent-at-its-own-type-is-refused -->
Where the conformer is known at the send, the refusal is made at compile time: the argument widens to the
interface at the request payload, and it is walked as the record it is.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	var pin as MachinePin

	static function create(pin MachinePin) returns Self
		return Self{pin: pin}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Port'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s isolated Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.measure(Port.create(MachinePin.acquire())) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:33:10: parameter `s` of the message `Calc.measure` is a `Shape` held at an interface type, whose conformer `Port` cannot cross to another green thread, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.a-reply-may-not-alias-an-interface-value-the-state-holds -->
A reply held at an interface type that the service's state still holds would hand the caller a second
reference to the conformer's box on another green thread, exactly as returning a state record would.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Square implements Shape
	var side as Tally

	static function create(side Tally) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Tally
		return self.side * self.side
	end 'area'
end 'Square'

type Surveyor
	var shape as Shape

	static function create(side Tally) returns Self
		return Self{shape: Square.create(side)}
	end 'create'

	export function peek() returns immutable Shape
		return self.shape
	end 'peek'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(3)
	let shape = try await surveyor.peek() otherwise panic("the surveyor is running")
	return shape.area() as ExitCode
end 'main'
```
```maxoncstderr
error E3209: <fragment>:28:3: a return does not fit the declared result: the returned value is mutable, the result is immutable; freeze it
```

<!-- test: services.a-spawn-factory-delegating-to-a-factory-that-stores-its-argument-starts-once -->
A factory that hands its argument to another factory, which stores it, starts the service with the one
owner of that argument: the temporary moved into `create`, `create` moved it into `prepare`, and nothing
on the spawning side still names it when the state is walked.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Surveyor
	var settings as Settings

	static function prepare(settings Settings) returns Self
		return Self{settings: settings}
	end 'prepare'

	static function create(settings isolated Settings) returns Self
		let ownedSettings = merge settings

		return prepare(ownedSettings)
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(Settings.create(true))
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	print("{total}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: services.a-message-argument-built-by-a-delegating-factory-crosses-once -->
A message argument built by a factory that hands its own argument to another factory, which stores it, has
one owner at the send: the temporary the outer call took is released before the argument's graph is walked.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export var settings as Settings

	static function prepare(settings Settings) returns Self
		return Self{settings: settings}
	end 'prepare'

	static function create(settings Settings) returns Self
		return prepare(settings)
	end 'create'
end 'Holder'

type Keeper
	var kept as Tally

	static function create() returns Self
		return Self{kept: 0}
	end 'create'

	export function keep(holder isolated Holder) returns Tally
		self.kept = self.kept + 1
		return 42 if holder.settings.doubled else 0
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let total = try await keeper.keep(Holder.create(Settings.create(true))) otherwise panic("the keeper is running")
	print("{total}\n")
	keeper.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: services.a-spawn-factory-delegating-two-stored-arguments-through-try-starts-once -->
The delegation may be a `try … otherwise panic(…)` and may store two arguments, one of them a literal: every
temporary the spawn's factory call took is released before the state is walked.
```maxon
typealias Tally = int(0 to u64.max)

enum SetupError implements Error
	refused
end 'SetupError'

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Surveyor
	var settings as Settings
	var label as String

	static function prepare(settings Settings, label String) returns Self throws SetupError
		if label.isEmpty() 'noLabel'
			throw SetupError.refused
		end 'noLabel'

		return Self{settings: settings, label: label}
	end 'prepare'

	static function create(settings isolated Settings, label String) returns Self
		let ownedSettings = merge settings

		return try prepare(ownedSettings, label: label) otherwise panic("prepare refused")
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled and self.label.byteLength() == 3 else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(Settings.create(true), label: "abc")
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	print("{total}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: services.a-spawn-factory-argument-read-off-a-temporary-starts-once -->
A factory argument read out of a temporary's field keeps that temporary alive only for the read: the
`Holder` the argument was read from is released before the state is walked, so the `Settings` it held
has the service as its one owner.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export var settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Surveyor
	var settings as Settings

	static function create(settings isolated Settings) returns Self
		return Self{settings: merge settings}
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(Holder.create(Settings.create(true)).settings.clone())
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	print("{total}\n")
	surveyor.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: services.a-message-argument-built-from-a-temporarys-field-crosses-once -->
A message argument built from a field read out of a temporary has one owner at the send: the temporary the
field was read from is released before the argument's graph is walked.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export var settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Keeper
	var kept as Tally

	static function create() returns Self
		return Self{kept: 0}
	end 'create'

	export function keep(holder isolated Holder) returns Tally
		self.kept = self.kept + 1
		return 42 if holder.settings.doubled else 0
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let total = try await keeper.keep(Holder.create(Holder.create(Settings.create(true)).settings.clone())) otherwise panic("the keeper is running")
	print("{total}\n")
	keeper.shutdown()
	return 0
end 'main'
```
```stdout
42
```
```exitcode
0
```

<!-- test: services.a-send-inside-a-larger-expression-releases-only-what-its-arguments-built -->
The release at a send reaches only what its own argument list built. A field read before the send keeps
its temporary, a `try` fork inside the arguments hands its temporaries back before the release, and an
interpolated argument crosses as the one owner of its record.
```maxon
typealias Tally = int(0 to u64.max)

enum SetupError implements Error
	refused
end 'SetupError'

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Namer
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Namer'

type Holder
	export var settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Keeper
	var kept as Tally

	static function create() returns Self
		return Self{kept: 0}
	end 'create'

	export function keep(holder isolated Holder, label String) returns Tally
		self.kept = self.kept + 1
		return 42 if holder.settings.doubled and label.byteLength() == 5 else 0
	end 'keep'
end 'Keeper'

function parseSettings(text String) returns Settings throws SetupError
	if text.byteLength() != 2 'unknown'
		throw SetupError.refused
	end 'unknown'

	return Settings.create(true)
end 'parseSettings'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let mark = Namer.create("ab").name.byteLength() as Tally
	let total = try await keeper.keep(Holder.create(try parseSettings(Namer.create("on").name) otherwise panic("the settings parse")), label: "{Namer.create("ab").name}cde") otherwise panic("the keeper is running")
	print("{mark} {total}\n")
	keeper.shutdown()
	return 0
end 'main'
```
```stdout
2 42
```
```exitcode
0
```

<!-- test: services.a-spawn-as-a-call-argument-releases-only-what-its-factory-built -->
A `spawn` written as one argument of a call releases what its factory's arguments built and nothing the
call's other arguments build after it.
```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var doubled as bool

	static function create(doubled bool) returns Self
		return Self{doubled: doubled}
	end 'create'
end 'Settings'

type Holder
	export var settings as Settings

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'
end 'Holder'

type Surveyor
	var settings as Settings

	static function create(settings isolated Settings) returns Self
		return Self{settings: merge settings}
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function measureWith(surveyor Surveyor.handle, extra Holder) returns Tally
	let total = try await surveyor.measure(21) otherwise panic("the surveyor is running")
	surveyor.shutdown()
	return total if extra.settings.doubled else total + 1
end 'measureWith'

function main() returns ExitCode
	let total = measureWith(spawn Surveyor.create(Holder.create(Settings.create(true)).settings.clone()), extra: Holder.create(Holder.create(Settings.create(false)).settings))
	print("{total}\n")
	return 0
end 'main'
```
```stdout
43
```
```exitcode
0
```

<!-- test: services.a-var-string-sent-to-a-service-is-moved -->
A send moves the reference and consumes the sender's `var`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	var word = "hello padded out long enough to heap allocate {1}"
	let loud = try await h.shout(word) otherwise "stopped"
	print("{loud}\n")
	print("{word}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:10: use of moved value 'word': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-cell-resident-var-sent-to-a-service-is-moved -->
`word` lives in a cell because `fill` reassigns its parameter; the send takes the cell's contents.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function fill(dest mutable String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	let h = spawn Echo.create()
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let loud = try await h.shout(word) otherwise "stopped"
	print("{loud}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled 2 padded out long enough to heap allocate!
```

<!-- test: services.a-cell-resident-var-read-after-a-send-is-use-after-move -->
The same cell-resident `var`, read after the send.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function fill(dest mutable String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	let h = spawn Echo.create()
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let loud = try await h.shout(word) otherwise "stopped"
	print("{loud}\n")
	print("{word}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:10: use of moved value 'word': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-var-reassigned-after-a-closure-captured-it-is-sent -->
The closure owns the value it captured, so the `var` reassigned afterwards holds a new value of its own, and that
value moves to the service while the closure goes on reading the first.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	var word = "hello padded out long enough to heap allocate {0}"
	let peek = function() gives word.byteLength()
	word = "second padded out long enough to heap allocate {1}"
	let loud = try await h.shout(word) otherwise "stopped"
	print("{loud} {peek()}\n")
	return 0
end 'main'
```
```stdout
second padded out long enough to heap allocate 1! 47
```

<!-- test: services.error.a-cell-resident-var-a-closure-only-reads-cannot-be-sent -->
A closure that only READS a cell-resident `var` takes the cell's occupant, so sending the `var` afterwards is a
use after move.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function opening() returns (String, Integer)
	return ("hello padded out long enough to heap allocate {1}", 1)
end 'opening'

function fill(dest mutable String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	let h = spawn Echo.create()
	var (word, _) = opening()
	fill(word, n: 2)
	let peek = function() gives word.byteLength()
	let loud = try await h.shout(word) otherwise "stopped"
	print("{loud} {peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:31: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: services.error.one-let-sent-twice-in-one-message-is-use-after-move -->
The first argument moves `b` into the message, so the second reads a moved value.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take2(a isolated Box, other isolated Box)
		let ownedA = merge a
		let ownedOther = merge other

		self.seen = self.seen + ownedA.n + ownedOther.n
	end 'take2'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take2(b, other: b) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:28:24: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.a-let-and-its-alias-sent-in-one-message-are-refused -->
`c` names the record `b` holds, so moving `b` into the message leaves `c` a second owner of it.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take2(a isolated Box, other isolated Box)
		let ownedA = merge a
		let ownedOther = merge other

		self.seen = self.seen + ownedA.n + ownedOther.n
	end 'take2'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	let c = b
	try h.take2(b, other: c) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:29:14: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.a-sent-let-used-as-a-method-receiver-is-use-after-move -->
A sent `let` is gone from the send on, so calling a method on it reads a moved value.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function doubled() returns Integer
		return self.n * 2
	end 'doubled'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("a running service with room takes the send")
	return b.doubled() as ExitCode
end 'main'
public typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:32:9: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.a-sent-let-passed-to-an-async-call-is-use-after-move -->
A sent `let` is gone from the send on, so handing it to a coroutine reads a moved value.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b isolated Box)
		let ownedB = merge b

		self.seen = self.seen + ownedB.n
	end 'take'
end 'Svc'

function readIt(b Box) returns Integer
	Scheduler.yield()
	return b.n
end 'readIt'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create() as isolated Box
	try h.take(b) otherwise panic("a running service with room takes the send")
	let p = async readIt(b)
	return (await p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3102: <fragment>:33:23: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-var-the-loop-iterates-is-not-sent-inside-the-loop -->
The loop holds a counted reference to the array it iterates, so the `var` `items` is no longer the record's only
owner inside the loop body, and the send, which would move it to the service while the loop still reads it, is
refused where it is written.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Words = Array with String

type Summer
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(words isolated Words) returns Integer
		for word in words 'each'
			self.total = self.total + (word.byteLength() as Integer)
		end 'each'

		return self.total
	end 'add'
end 'Summer'

function main() returns ExitCode
	let h = spawn Summer.create()
	var items = Words.create(0)
	items.push("first padded out long enough to heap allocate {1}")
	items.push("second padded out long enough to heap allocate {2}")

	for x in items 'each'
		let r = try await h.add(items) otherwise 0
		print("{r} {x}\n")
		break
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:28:27: 'items' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: services.a-var-the-loop-iterated-is-sent-after-the-loop -->
Once the loop has ended it holds nothing, so the `var` is the record's only owner again and the send moves it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Words = Array with String

type Summer
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(words isolated Words) returns Integer
		for word in words 'each'
			self.total = self.total + (word.byteLength() as Integer)
		end 'each'

		return self.total
	end 'add'
end 'Summer'

function main() returns ExitCode
	let h = spawn Summer.create()
	let items = Words.create(0) as isolated Words

	modify items 'fill'
		items.push("first padded out long enough to heap allocate {1}")
		items.push("second padded out long enough to heap allocate {2}")
	end 'fill'

	var seen = 0 as Integer

	for x in items 'each'
		seen = seen + (x.byteLength() as Integer)
	end 'each'

	let r = try await h.add(items) otherwise 0
	print("{seen} {r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
95 95
```

<!-- test: services.a-value-whose-field-a-closure-holds-is-refused-at-the-send -->
An `isolated` value is one no other name reaches, so a graph the sender still owns a part of is refused at the argument.

```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Outer'

type Holder
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(b isolated Outer) returns Integer
		let ownedB = merge b

		self.total = self.total + ownedB.inner.n
		return self.total
	end 'take'
end 'Holder'

function main() returns ExitCode
	let h = spawn Holder.create()
	let b = Outer.create() as isolated Outer
	let inner = b.inner
	let peek = function() gives inner.n
	let r = try await h.take(b) otherwise 0
	return (r + peek()) as ExitCode
end 'main'
```
```maxoncstderr
error E3202: <fragment>:38:14: 'b.inner' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: services.error.an-argument-whose-graph-holds-a-function-value-is-refused -->
A message argument whose record holds a function value cannot be walked across, and the refusal names the
function value.
```maxon
typealias Tally = int(0 to 1000)
typealias Op = function(Tally) returns Tally

type Job
	export var op as Op

	static function create(op Op) returns Self
		return Self{op: op}
	end 'create'
end 'Job'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function run(j isolated Job)
		let ownedJ = merge j

		self.count = ownedJ.op(self.count)
	end 'run'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.run(Job.create(function(n Tally) gives n + 1)) otherwise panic("a running service with room takes the send")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:28:10: parameter `j` of the message `Calc.run` is a `Job` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: services.error.a-function-valued-reply-is-refused -->
A reply is handed to another green thread too, and a function value is refused there by name.
```maxon
typealias Tally = int(0 to 1000)
typealias Op = function(Tally) returns Tally

type Maker
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function make() returns Op
		self.seen = self.seen + 1
		return function(n Tally) gives n + 1
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create()
	let op = try await h.make() otherwise panic("the maker is running")
	return op(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3217: <fragment>:19:10: the message `Maker.make` returns a function value, a closure record that owns what it captures and does not cross to another thread, and this `spawn` makes `Maker` a service — whose reply is handed to the green thread that awaits it. `await` a promise in the handler and return the result it yields, return a value whose graph holds no function value or thread-bound handle, or drop the `returns` clause
```

<!-- test: services.a-handle-bound-to-a-local-then-pushed-is-freed-once -->
A handle moved from a local into an array is owned by the array alone, so each is released once when the array goes.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias WorkerHandleArray = Array with Worker.handle

type Worker
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(n Integer) returns Integer
		self.total = self.total + n
		return self.total
	end 'add'
end 'Worker'

function main() returns ExitCode
	let workers = WorkerHandleArray.create(0)

	for _ in 1 to 4 'spawnThem'
		let w = spawn Worker.create()
		workers.push(w)
	end 'spawnThem'

	var sum = 0 as Integer

	for w in workers 'useThem'
		sum = sum + (try await w.add(1) otherwise 0)
	end 'useThem'

	if sum == 4 'allAnswered'
		return 42
	end 'allAnswered'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: services.an-array-of-handles-walks-with-its-iterator -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias WorkerHandleArray = Array with Worker.handle

type Worker
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function add(n Integer) returns Integer
		self.total = self.total + n
		return self.total
	end 'add'
end 'Worker'

function main() returns ExitCode
	let workers = WorkerHandleArray.create(0)

	for _ in 1 to 4 'spawnThem'
		workers.push(spawn Worker.create())
	end 'spawnThem'

	var sum = 0 as Integer

	for (_, w) in try workers.withIterator() otherwise panic("an array always yields an iterator") 'walk'
		sum = sum + (try await w.add(1) otherwise 0)
	end 'walk'

	if sum == 4 'allSeen'
		return 42
	end 'allSeen'
	return 1
end 'main'
```
```exitcode
42
```

<!-- test: services.a-view-of-an-immutable-record-sent-to-a-service-is-sent -->
`v` is a `let` bound to a record with only `let` fields of a value type, so it is a value and crosses to the service; a second `let` elsewhere in the body changes nothing.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Point
	export let x as Count

	static function create(x Count) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function show(p isolated Point) returns Count
		self.seen = self.seen + 1
		return p.x
	end 'show'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let b = Box.create(1)
	b.n = 2
	let w = b
	print("{w.n}\n")
	let p = Point.create(7)
	let v = p
	let r = try await h.show(v) otherwise 0
	print("{r}\n")
	return 0
end 'main'
```
```stdout
2
7
```
```exitcode
0
```

<!-- test: services.error.a-var-sent-after-it-has-been-viewed-is-refused -->
`let w = b` moves `b` into `w`, as `let w = b` does, so `b` cannot be sent afterwards, even though `w` is not read again.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.n = 2
	end 'fill'

	let w = b
	print("{w.n}\n")
	let r = try await h.keep(b) otherwise 0
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:27: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.a-var-sent-after-it-has-been-viewed-and-read-afterwards-is-refused -->
`let w = b` moves `b` into `w`, which is read after the send, so the send of `b` is refused rather than leaving `w` reading a record the service now owns and writes.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var kept as Box

	static function create() returns Self
		return Self{kept: Box.create(0)}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.kept = merge p
		self.kept.n = 9
		return 0
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.n = 2
	end 'fill'

	let w = b
	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:27: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-text-field-read-through-a-view-is-sent-as-a-value -->
A `String` crosses to the service as a value, so a field read through a `let` may be sent after the record has been viewed.

```maxon
typealias Count = int(0 to 1000)

type Person
	export var name as String
	export var age as Count

	static function create(name String) returns Self
		return Self{name: name, age: 0}
	end 'create'
end 'Person'

type Greeter
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function greet(name String) returns String
		self.seen = self.seen + 1
		return "hello {name}"
	end 'greet'
end 'Greeter'

function main() returns ExitCode
	let h = spawn Greeter.create()
	let b = Person.create("ada")
	b.age = 3
	let v = b
	let r = try await h.greet(v.name) otherwise ""
	print("{r} {v.age}\n")
	return 0
end 'main'
```
```stdout
hello ada 3
```
```exitcode
0
```

<!-- test: services.error.a-var-sent-after-a-field-of-it-has-been-viewed-is-refused -->
`w` views a field of `b`, so its reference co-owns part of `b`'s graph for the rest of the function, and `b` cannot be moved to the service.

```maxon
typealias Count = int(0 to 1000)

type Inner
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Inner'

type Box
	export var child as Inner

	static function create(n Count) returns Self
		return Self{child: Inner.create(n)}
	end 'create'
end 'Box'

type Store
	var kept as Box

	static function create() returns Self
		return Self{kept: Box.create(0)}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.kept = merge p
		return 0
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.child.n = 2
	end 'fill'

	let w = b.child
	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:41:10: 'b.child' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: services.error.a-var-a-ternary-view-may-name-sent-is-refused -->
`w` names `b` or `c` depending on the comparison, so its reference may co-own `c`'s record, and `c` cannot be moved to the service.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1) as isolated Box
	var c = Box.create(2) as isolated Box

	modify b 'fillB'
		b.n = 3
	end 'fillB'

	modify c 'fillC'
		c.n = 4
	end 'fillC'

	let w = b if b.n > c.n else c
	let r = try await h.keep(c) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:38:10: 'b if b.n > c.n else c' is mutable and cannot leave the statement that opens 'the isolated value'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: services.error.a-var-an-otherwise-view-may-name-sent-is-refused -->
`w` names `spare` when the array has no element 0, so its reference may co-own `spare`'s record.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var boxes = Boxes.create(0) as isolated Boxes

	modify boxes 'fill'
		boxes.push(Box.create(1))
	end 'fill'

	var spare = Box.create(0) as isolated Box

	modify spare 'fill'
		spare.n = 2
	end 'fill'

	let w = try boxes.get(0) otherwise spare
	let r = try await h.keep(spare) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:40:10: a join of an isolated value with data that is not isolated is refused: every arm must be isolated, or a fresh value
```

<!-- test: services.error.a-var-a-view-of-a-calls-result-may-name-sent-is-refused -->
`pick` hands back its argument, so the record `w` views is `b`'s.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function pick(x Box) returns Box
	return x
end 'pick'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.n = 2
	end 'fill'

	let w = pick(b)
	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:37:10: 'pick' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: services.error.a-var-a-view-of-a-methods-result-may-name-sent-is-refused -->
The same through a method: `c.pick(b)` hands back `b`, not something of `c`'s.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Picker
	let tag as Count

	static function create() returns Self
		return Self{tag: 0}
	end 'create'

	function pick(x Box) returns Box
		return x
	end 'pick'
end 'Picker'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let c = Picker.create()
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.n = 2
	end 'fill'

	let w = c.pick(b)
	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:46:12: 'pick' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: services.error.a-var-a-match-view-may-name-sent-is-refused -->
One arm of the `match` gives `b`, so the record `w` views may be `b`'s.

```maxon
typealias Count = int(0 to 1000)

enum Side
	left
	right
end 'Side'

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let side = Side.left
	var b = Box.create(1) as isolated Box
	var c = Box.create(2) as isolated Box

	modify b 'fillB'
		b.n = 3
	end 'fillB'

	modify c 'fillC'
		c.n = 4
	end 'fillC'

	let w = match side 'which'
		left gives b
		right gives c
	end 'which'

	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:49:27: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.an-array-an-element-view-reaches-sent-is-refused -->
`e` binds an element of the isolated `boxes` outside a `modify` block, which is an isolated read without a block, so it is refused before the array is sent to the service.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keepAll(p isolated Boxes) returns Count
		self.seen = self.seen + 1
		return p.count() as Count
	end 'keepAll'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var boxes = Boxes.create(0) as isolated Boxes

	modify boxes 'fill'
		boxes.push(Box.create(1))
	end 'fill'

	let e = try boxes.get(0) otherwise return 1
	let r = try await h.keepAll(boxes) otherwise 0
	print("{e.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:34:10: 'try boxes.get(0) otherwise return 1' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: services.an-array-an-if-view-dead-before-the-send-is-sent -->
The `if let` binder views an element of `boxes` only inside the `if`, and it is not read after the `if`, so the send after it is accepted.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keepAll(p isolated Boxes) returns Count
		self.seen = self.seen + 1
		return p.count() as Count
	end 'keepAll'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let boxes = Boxes.create(0) as isolated Boxes

	modify boxes 'fill'
		boxes.push(Box.create(1))
	end 'fill'

	if let e = try boxes.get(0) 'first'
		print("{e.n}\n")
	end 'first'

	let r = try await h.keepAll(boxes) otherwise 0
	print("{r}\n")
	return 0
end 'main'
```
```stdout
1
1
```
```exitcode
0
```

<!-- test: services.error.a-var-viewed-earlier-in-a-loop-body-sent-later-in-it-is-refused -->
Inside one trip of the loop, `let w = b` moves `b` before the send, so the send is refused on every trip.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()

	for _ in 1 to 2 'each'
		var b = Box.create(1) as isolated Box

		modify b 'fill'
			b.n = 2
		end 'fill'

		let w = b
		print("{w.n}\n")
		let r = try await h.keep(b) otherwise 0
		print("{r}\n")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:37:28: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.error.a-var-viewed-then-handed-to-spawn-as-state-is-refused -->
The state `spawn` starts the service with is moved into it, so a `var` that `let w = b` has already moved cannot be that state.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Worker
	var held as Box

	static function create(held isolated Box) returns Self
		return Self{held: merge held}
	end 'create'

	export function bump() returns Count
		self.held.n = self.held.n + 1
		return self.held.n
	end 'bump'
end 'Worker'

function main() returns ExitCode
	var b = Box.create(1) as isolated Box

	modify b 'fill'
		b.n = 2
	end 'fill'

	let w = b
	let h = spawn Worker.create(b)
	let r = try await h.bump() otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:30: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-viewed-var-of-an-immutable-record-is-shared-with-the-service -->
`Box` has only `let` fields, so nothing can write its record: the send shares it with the service instead of moving it, and `w` stays valid. The service's reply reads the record, and `w` reads it after the send.

```maxon
typealias Count = int(i64.min to i64.max)

type Box
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = Box.create(1)
	b = Box.create(2)
	let w = b
	let r = try await h.keep(b) otherwise 0
	print("{w.n} {r}\n")
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: services.error.a-ternary-arm-sent-inside-a-view-initializer-moves -->
The send inside `w`'s initializer moves whichever arm the ternary picks, exactly as it would inside a `let` initializer, so writing `a` afterwards is a use of a moved value.

```maxon
typealias Count = int(0 to 1000)

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function boxed(n Count) returns Box
	return Box.create(n)
end 'boxed'

function main() returns ExitCode
	let h = spawn Store.create()
	var a = Box.create(1) as isolated Box

	modify a 'fill'
		a.n = 2
	end 'fill'

	var other = Box.create(3) as isolated Box

	modify other 'fill'
		other.n = 4
	end 'fill'

	let flag = a.n > other.n
	let w = boxed(try await h.keep(a if flag else other) otherwise 0)
	a.n = 9
	print("{w.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:2: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-viewed-var-of-an-immutable-generic-instance-is-shared-with-the-service -->
`Cell with Count` has only a `let` field, so nothing can write its record: the send shares it with the service instead of moving it, and `w` stays valid. The service's reply reads the record, and `w` reads it after the send.

```maxon
typealias Count = int(i64.min to i64.max)

type Cell uses T
	export let v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Cell'

typealias CountCell = Cell with Count

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p immutable CountCell) returns Count
		self.seen = self.seen + 1
		return p.v
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var b = CountCell.create(1)
	b = CountCell.create(3)
	let w = b
	let r = try await h.keep(b) otherwise 0
	print("{w.v} {r}\n")
	return 0
end 'main'
```
```stdout
3 3
```
```exitcode
0
```

<!-- test: services.a-viewed-var-of-an-immutable-union-is-shared-with-the-service -->
Every payload of `Note` is a `let`, so nothing can write its value: the send shares it with the service instead of moving it, and `w` stays valid. The service's reply reads the payload, and `w` reads it after the send.

```maxon
typealias Count = int(0 to 1000)

union Note
	text(body String)
	blank
end 'Note'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(n immutable Note) returns Count
		self.seen = self.seen + 1

		return match n 'note'
			text(body) gives body.byteLength() as Count
			blank gives 0
		end 'note'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var n = Note.text("a")
	n = Note.text("bb")
	let w = n
	let r = try await h.keep(n) otherwise 0

	let mine = match w 'note'
		text(body) gives body.byteLength() as Count
		blank gives 0
	end 'note'

	print("{r} {mine}\n")
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: services.error.a-viewed-var-sent-and-read-afterwards-reports-the-send -->
`w` views the array that holds `b`'s record, so the send is refused; the read of `b` after it is not reported as a use of a moved value, because a refused send moves nothing.

```maxon
typealias Count = int(0 to 1000)
typealias Boxes = Array with Box

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var boxes = Boxes.create(0)
	var b = Box.create(1) as isolated Box
	boxes.push(b)
	let w = boxes
	let r = try await h.keep(b) otherwise 0
	print("{b.n} {r} {w.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:30:13: cannot store an isolated 'Box' into 'an element of the container push writes', which holds a mutable 'Box'; `merge` it first
```

<!-- test: services.error.an-immutable-record-no-view-reaches-is-moved-by-the-send -->
`b`'s record is solely owned, so the send moves it even though its type has only `let` fields; the `let` of `other` reaches a different record and changes nothing, so reading `b` afterwards is a use of a moved value.

```maxon
typealias Count = int(i64.min to i64.max)

type Box
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let other = Box.create(7)
	let z = other
	var b = Box.create(1)
	b = Box.create(2)
	let r = try await h.keep(b) otherwise 0
	let after = b.n
	print("{after} {r} {z.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:14: use of moved value 'b': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.an-otherwise-merge-of-two-fresh-records-is-sent -->
Both edges of the `try … otherwise` merge are records the calls just made, so `x` is solely owned and the send moves it, as it would a ternary of two fresh calls.

```maxon
typealias Count = int(0 to 1000)

enum MakeError implements Error
	refused
end 'MakeError'

type Box
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Box) returns Count
		self.seen = self.seen + 1
		return p.n
	end 'keep'
end 'Store'

function made(fail bool) returns isolated Box throws MakeError
	if fail 'refuse'
		throw MakeError.refused
	end 'refuse'

	return Box.create(1)
end 'made'

function spare() returns isolated Box
	return Box.create(2)
end 'spare'

function main() returns ExitCode
	let h = spawn Store.create()
	let x = try made(true) otherwise spare()
	let r = try await h.keep(x) otherwise 0
	print("{r}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```


<!-- test: error.a-spawn-of-a-hidden-generic-factory-is-not-exported -->
`Box.create` is unexported in `box/`, so `main.maxon`, outside it, cannot spawn through it, as it could not call it.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	let h = spawn Box.create(9 as Whole)
	return try await h.peek() otherwise 0 as ExitCode
end 'main'

// --- file: box/box.maxon
export typealias Whole = int(i64.min to i64.max)

export type Box uses T
	export var n as Whole

	static function create(x T) returns Self
		return Self{n: 1}
	end 'create'

	export function peek() returns Whole
		return self.n
	end 'peek'
end 'Box'
```
```maxoncstderr
error E3008: <fragment>:4:10: function 'Box.create' is not exported
```

<!-- test: a-message-parameter-spelled-through-its-directory-keeps-its-brand -->
<!-- process: own -->
A message parameter spelled `lib.Names`, an alias of `Array with String` declared in `lib/`, carries the same brand as the value the sender builds from `lib.Names`, so the request's payload accepts it and the reply comes back.
```maxon
// --- file: lib/names.maxon
export typealias Names = Array with String

// --- file: main.maxon
type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(names isolated lib.Names) returns Count
		let added = names.count()

		self.seen = self.seen + added
		return self.seen
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let tags = lib.Names.create(0) as isolated lib.Names

	modify tags 'fill'
		tags.push("a")
	end 'fill'

	let got = try await h.keep(tags) otherwise panic("the store is open")
	return got as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-message-parameter-spelled-through-its-directory-as-a-struct-an-enum-and-an-interface -->
<!-- process: own -->
Message parameters spelled `lib.Item`, `lib.Mode` and `lib.Named`, a struct, an enum and an interface declared in `lib/`, resolve through their declaring directory, so each message is sent, handled and answered: 3 + 10 + 5.
```maxon
// --- file: lib/types.maxon
export type Item
	export let weight as Count

	export static function create(weight Count) returns Self
		return Self{weight: weight}
	end 'create'
end 'Item'

export enum Mode
	quiet
	loud
end 'Mode'

export interface Named
	function label() returns Count
end 'Named'

export type Plate implements Named
	let code as Count

	export static function create(code Count) returns Self
		return Self{code: code}
	end 'create'

	export function label() returns Count
		return self.code
	end 'label'
end 'Plate'

// --- file: main.maxon
type Store
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function weigh(item isolated lib.Item) returns Count
		let weight = item.weight

		self.seen = self.seen + weight
		return self.seen
	end 'weigh'

	export function volume(mode lib.Mode) returns Count
		return 10 if mode == lib.Mode.loud else 1
	end 'volume'

	export function tag(plate isolated lib.Named) returns Count
		return plate.label()
	end 'tag'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let weighed = try await h.weigh(lib.Item.create(3)) otherwise panic("the store is open")
	let loudness = try await h.volume(lib.Mode.loud) otherwise panic("the store is open")
	let tagged = try await h.tag(lib.Plate.create(5)) otherwise panic("the store is open")
	return (weighed + loudness + tagged) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: a-library-spawn-outside-a-default-starts-no-service-while-no-reached-body-spawns-it -->
<!-- tier: stdlib -->
A library function whose body writes `spawn Probe.create()` makes `Probe` a service only in a program that
reaches that body. `main` never calls `startProbe`, so the program carries no scheduler and its raw
allocation count is zero, exactly as a program that declares no library spawn at all.
```maxon
// --- stdlib-overlay: Builtins.maxon
public typealias ProbeCount = int(i64.min to i64.max)

public type Probe
	var calls as ProbeCount

	public static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function ping() returns ProbeCount
		self.calls = self.calls + 1
		return self.calls
	end 'ping'
end 'Probe'

public function startProbe() returns ProbeCount
	let probe = spawn Probe.create()
	return try await probe.ping() otherwise 0
end 'startProbe'

// --- file: main.maxon
function main() returns ExitCode
	if __Builtins.mmRawAllocTotal() == 0 'noScheduler'
		return 7
	end 'noScheduler'

	return 1
end 'main'
```
```exitcode
7
```

<!-- test: a-library-spawn-outside-a-default-starts-its-service-in-a-program-that-reaches-it -->
<!-- tier: stdlib -->
The same library function, called: the reached body spawns `Probe`, so the program is a service program and
`startProbe` gets its answer from the running service.
```maxon
// --- stdlib-overlay: Builtins.maxon
public typealias ProbeCount = int(i64.min to i64.max)

public type Probe
	var calls as ProbeCount

	public static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function ping() returns ProbeCount
		self.calls = self.calls + 1
		return self.calls
	end 'ping'
end 'Probe'

public function startProbe() returns ProbeCount
	let probe = spawn Probe.create()
	return try await probe.ping() otherwise 0
end 'startProbe'

// --- file: main.maxon
function main() returns ExitCode
	return (startProbe() + 41) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.a-library-spawn-is-rejected-at-its-crossing-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library body that spawns reaches the green-thread substrate exactly as a `spawn` in the program does, so on a
target with none the call that crosses into it is refused with `E3104` at its own span, naming the entry the
spawn lowers to. The scheduler install is never reached.
```maxon
// --- stdlib-overlay: Builtins.maxon
public typealias ProbeCount = int(i64.min to i64.max)

public type Probe
	var calls as ProbeCount

	public static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function ping() returns ProbeCount
		self.calls = self.calls + 1
		return self.calls
	end 'ping'
end 'Probe'

public function startProbe() returns ProbeCount
	let probe = spawn Probe.create()
	return try await probe.ping() otherwise 0
end 'startProbe'

// --- file: main.maxon
function main() returns ExitCode
	return (startProbe() + 41) as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:25:10: 'startProbe' lowers to the runtime entry '__cown_spawn', which has no wasm32-wasi implementation
```

<!-- test: error.a-library-send-is-rejected-at-its-crossing-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library body that sends a message to a service it was handed reaches the substrate the same way, so the call
into it is refused with `E3104` naming the reply cell the awaited send mints, beside the `spawn` the program itself writes.
```maxon
// --- stdlib-overlay: Builtins.maxon
public typealias ProbeCount = int(i64.min to i64.max)

public type Probe
	var calls as ProbeCount

	public static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function ping() returns ProbeCount
		self.calls = self.calls + 1
		return self.calls
	end 'ping'
end 'Probe'

public function askProbe(probe Probe.handle) returns ProbeCount
	return try await probe.ping() otherwise 0
end 'askProbe'

// --- file: main.maxon
function main() returns ExitCode
	let probe = spawn Probe.create()
	return askProbe(probe) as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:24:14: 'spawn' lowers to the runtime entry '__cown_spawn', which has no wasm32-wasi implementation
error E3104: <fragment>:25:9: 'askProbe' lowers to the runtime entry '__gt_cell_alloc', which has no wasm32-wasi implementation
```

### A socket crosses into a service when its one owner sends it

A `TcpClient` owns an OS socket, and the socket's box counts its owners as every other record does, so the send's
walk can prove it sole: a connection the sender holds alone moves into the service, which then reads and writes it
from its own green thread. A connection some other owner still holds is refused by the same walk, before anything is
enqueued.

<!-- test: services.a-service-owns-a-moved-tcp-client-and-echoes-on-it -->
<!-- procs: 1 -->
The accepted connection is moved into `Echo.create`, so the service is its only owner. The service reads what the
peer sent on its own green thread and writes it back; the peer, still on `main`'s thread, reads the echo.
```maxon
typealias Tally = int(0 to 1000000)

type Echo
	var client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'

	export function serve() returns Tally
		let got = String.from(try client.readSome(64) otherwise return 0)
		_ = try client.send(got) otherwise return 0
		client.close()

		return got.byteLength() as Tally
	end 'serve'
end 'Echo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let out = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	let accepted = try listener.accept() otherwise return 3
	let echo = spawn Echo.create(accepted)
	let reply = try echo.serve() otherwise return 4

	_ = try out.send("hello") otherwise 'refused5x26'
		reply.cancel()
		return 5
	end 'refused5x26'

	let heard = try out.readSome(64) otherwise 'refused6x28'
		reply.cancel()
		return 6
	end 'refused6x28'
	let served = await reply

	print("{String.from(heard)} {served}\n")

	return 0
end 'main'
```
```stdout
hello 5
```
```exitcode
0
```

<!-- test: services.a-moved-tcp-client-works-on-another-processor -->
<!-- procs: 4 -->
The same hand-off with four processors, so the service's green thread can run on another OS thread than the one that
accepted the connection and registered the socket with the network poller. The poller's registration belongs to the
socket, not to a thread, so the service's `readSome` parks on it and is woken wherever it runs.
```maxon
typealias Tally = int(0 to 1000000)

type Echo
	var client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'

	export function serve() returns Tally
		var total = 0 as Tally

		for _ in 0 upto 3 'eachRound'
			let got = String.from(try client.readSome(64) otherwise return 0)
			_ = try client.send(got) otherwise return 0
			total = total + (got.byteLength() as Tally)
		end 'eachRound'

		client.close()

		return total
	end 'serve'
end 'Echo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let out = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	let accepted = try listener.accept() otherwise return 3
	let echo = spawn Echo.create(accepted)
	let reply = try echo.serve() otherwise return 4

	for word in ["one", "three", "seven"] 'eachWord'
		_ = try out.send(word) otherwise 'refused5x33'
			reply.cancel()
			return 5
		end 'refused5x33'

		let heard = try out.readSome(64) otherwise 'refused6x35'
			reply.cancel()
			return 6
		end 'refused6x35'

		print("{String.from(heard)}\n")
	end 'eachWord'

	let served = await reply

	print("{served}\n")

	return 0
end 'main'
```
```stdout
one
three
seven
13
```
```exitcode
0
```

<!-- test: deepmove.error.a-tcp-client-an-array-holds-is-refused-at-the-send -->
A `TcpClient` is a unique OS handle with one owner. `kept.push(accepted)` moves the accepted connection into the array, so handing `accepted` to the service afterwards reads a moved value: no second owner of the connection can exist to meet the service's close on another green thread.
```maxon
typealias Tally = int(0 to 1000000)
typealias Clients = Array with TcpClient

type Echo
	var client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'

	export function serve() returns Tally
		client.close()

		return 0
	end 'serve'
end 'Echo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let out = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	let accepted = try listener.accept() otherwise return 3
	let kept = Clients.create(0)

	kept.push(accepted)

	let echo = spawn Echo.create(accepted)
	let reply = try echo.serve() otherwise return 4
	let served = await reply

	out.close()
	print("{served} {kept.count()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:31: use of moved value 'accepted': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: deepmove.error.a-socket-adopted-by-two-tcp-clients-is-refused -->
<!-- tier: stdlib -->
`TcpClient.adopt` keeps the socket it is given, and the socket is a unique OS handle with one owner, so a second client over the same socket reads a moved value. Two clients, and so a socket box with several owners releasing from several green threads, cannot be built.
```maxon
typealias Tally = int(0 to 1000000)

type Echo
	var client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'

	export function serve() returns Tally
		client.close()

		return 0
	end 'serve'
end 'Echo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let socket = try __ManagedSocket.tcpConnect("127.0.0.1".addressableBytes(), port: listener.port(), milliseconds: 0) otherwise return 2
	let first = TcpClient.adopt(socket)
	let second = TcpClient.adopt(socket)
	let echo = spawn Echo.create(first)
	let reply = try echo.serve() otherwise return 4
	let served = await reply

	second.close()
	print("{served}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:31: use of moved value 'socket': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: services.a-tcp-client-held-at-an-interface-type-crosses-and-echoes -->
<!-- procs: 4 -->
A connection moves into a service whose state holds a conformer of it at an interface type, and the service reads and
writes the connection through the interface.
```maxon
typealias Tally = int(0 to 1000000)

interface Speaker
	function speak() returns Tally
end 'Speaker'

type Port implements Speaker
	var client as TcpClient

	static function create(client TcpClient) returns Self
		return Self{client: client}
	end 'create'

	function speak() returns Tally
		let got = String.from(try client.readSome(64) otherwise return 0)
		_ = try client.send(got) otherwise return 0
		client.close()

		return got.byteLength() as Tally
	end 'speak'
end 'Port'

type Stage
	var heard as Tally
	var speaker as Speaker

	static function create(client TcpClient) returns Self
		return Self{heard: 0, speaker: Port.create(client)}
	end 'create'

	export function perform() returns Tally
		self.heard = self.heard + self.speaker.speak()

		return self.heard
	end 'perform'
end 'Stage'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let out = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	let accepted = try listener.accept() otherwise return 3
	let stage = spawn Stage.create(accepted)
	let reply = try stage.perform() otherwise return 4

	_ = try out.send("interface") otherwise 'refused5x49'
		reply.cancel()
		return 5
	end 'refused5x49'

	let heard = try out.readSome(64) otherwise 'refused6x51'
		reply.cancel()
		return 6
	end 'refused6x51'
	let performed = await reply

	print("{String.from(heard)} {performed}\n")

	return 0
end 'main'
```
```stdout
interface 9
```
```exitcode
0
```

<!-- test: services.a-service-replies-with-a-connection-it-accepted -->
<!-- procs: 4 -->
A reply is walked after the handler's locals are released, so a connection the handler accepted and returns has no
other owner and crosses back to the caller, which reads and writes it on its own thread. The listener moved into the
service in the first place.
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

	_ = try out.send("accepted") otherwise return 5

	let heard = try served.readSome(64) otherwise return 6

	print("{String.from(heard)}\n")

	return 0
end 'main'
```
```stdout
accepted
```
```exitcode
0
```

<!-- test: services.a-service-replies-with-a-connection-it-dialled -->
<!-- procs: 4 -->
`TcpClient.connect` builds the client. A reply of its result is fresh, so the dialled connection has no other
owner and crosses back to the caller.
```maxon
typealias Tally = int(0 to 1000000)

type Dialer
	var port as NetworkPort
	var dialled as Tally

	static function create(port NetworkPort) returns Self
		return Self{port: port, dialled: 0}
	end 'create'

	export function next() returns TcpClient throws NetworkError
		self.dialled = self.dialled + 1

		return try TcpClient.connect("127.0.0.1", port: self.port)
	end 'next'
end 'Dialer'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let dialer = spawn Dialer.create(listener.port())
	let pending = try dialer.next() otherwise return 2
	let peer = try listener.accept() otherwise 'refused3x22'
		pending.cancel()
		return 3
	end 'refused3x22'
	let dialled = try await pending otherwise return 4

	_ = try peer.send("dialled") otherwise return 5

	let heard = try dialled.readSome(64) otherwise return 6

	print("{String.from(heard)}\n")

	return 0
end 'main'
```
```stdout
dialled
```
```exitcode
0
```

<!-- test: services.a-reply-forwarded-to-a-sibling-crosses -->
A static that forwards to a sibling static is fresh when the sibling is: `Box.make(n)` hands back
the record `Box.makeExtra(n, extra:)` builds, so the handler may return it.
```maxon
typealias Tally = int(0 to 1000000)

type Box
	export var n as Tally

	static function make(n Tally) returns Self
		return makeExtra(n, extra: 0)
	end 'make'

	static function makeExtra(n Tally, extra Tally) returns Self
		return Self{n: n + extra}
	end 'makeExtra'
end 'Box'

type Maker
	var made as Tally

	static function create() returns Self
		return Self{made: 0}
	end 'create'

	export function next() returns isolated Box
		self.made = self.made + 1

		return Box.make(self.made)
	end 'next'
end 'Maker'

function main() returns ExitCode
	let maker = spawn Maker.create()
	let box = try await maker.next() otherwise return 1

	print("{box.n}\n")

	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: services.a-send-of-a-record-forwarded-to-a-sibling-moves-in -->
A static that forwards to a sibling static is fresh when the sibling is, so the record it hands
back is the sole owner's to move into a service in a send.
```maxon
typealias Tally = int(0 to 1000000)

type Box
	export var n as Tally

	static function make(n Tally) returns Self
		return makeExtra(n, extra: 0)
	end 'make'

	static function makeExtra(n Tally, extra Tally) returns Self
		return Self{n: n + extra}
	end 'makeExtra'
end 'Box'

type Shelf
	var held as Box

	static function create() returns Self
		return Self{held: Box.make(0)}
	end 'create'

	export function keep(box isolated Box)
		self.held = merge box
	end 'keep'

	export function total() returns Tally
		return self.held.n
	end 'total'
end 'Shelf'

function main() returns ExitCode
	let shelf = spawn Shelf.create()

	try shelf.keep(Box.make(7)) otherwise return 1

	let total = try await shelf.total() otherwise return 2

	print("{total}\n")

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: error.a-factory-that-starts-a-promise-the-state-keeps-is-refused -->
The factory runs on whichever green thread takes the service's first turn, and a later message may run on another, so a
coroutine the factory leaves in the state would belong to a strand that no later message shares.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Napped = Promise with Integer

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Sleeper
	var held as Napped

	static function create() returns Self
		return Self{held: async forever()}
	end 'create'

	export function arm() returns Integer
		Scheduler.yield()
		return 1
	end 'arm'
end 'Sleeper'

function main() returns ExitCode
	let s = spawn Sleeper.create()
	let r = try s.arm() otherwise panic("an open mailbox with room takes the send")
	let v = await r
	print("v={v}\n")
	s.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3216: <fragment>:24:10: `Sleeper.held` is a `Napped` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Sleeper` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: error.a-factory-that-fills-a-promise-array-the-state-keeps-is-refused -->
Every coroutine a factory pushes into an array the state keeps is a promise in the state.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Napped = Promise with Integer
typealias NapArray = Array with Napped

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Sleepers
	var held as NapArray

	static function create(naps Count) returns Self
		var napping = NapArray.create(0)

		for _ in 0 upto naps 'eachNap'
			napping.push(async forever())
		end 'eachNap'

		return Self{held: napping}
	end 'create'

	export function napping() returns Count
		return self.held.count()
	end 'napping'
end 'Sleepers'

function main() returns ExitCode
	let s = spawn Sleepers.create(3)
	let n = try await s.napping() otherwise panic("the service is running")
	print("napping={n}\n")
	s.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3216: <fragment>:30:10: `Sleepers.held` is a `NapArray` that can hold a Promise, which is a green-thread handle whose coroutine belongs to the strand that started it. `Sleepers` is a service, and a service's state is run by whichever green thread takes its next message, so a promise kept in it could be awaited or dropped by a strand that does not own its coroutine. `await` the promise (or `.cancel()` it) before the message or factory returns and keep its result in the field
```

<!-- test: services.a-factorys-prints-follow-the-spawners-until-the-spawner-waits -->
<!-- procs: 1 -->
The factory is the service's first act, not the spawner's: on one processor the spawner keeps running past `spawn`,
and the factory's output appears only once the spawner waits for the service.
```maxon
typealias Tally = int(0 to 1000000)

type Greeter
	var greeted as Tally

	static function create() returns Self
		print("factory runs\n")
		return Self{greeted: 1}
	end 'create'

	export function total() returns Tally
		return self.greeted
	end 'total'
end 'Greeter'

function main() returns ExitCode
	let g = spawn Greeter.create()
	print("spawn returned\n")
	let n = try await g.total() otherwise return 1
	print("total={n}\n")
	g.shutdown()
	return 0
end 'main'
```
```stdout
spawn returned
factory runs
total=1
```
```exitcode
0
```

<!-- test: services.a-generic-factory-runs-on-the-service-with-its-arguments -->
A generic factory keeps a managed argument at its type parameter and borrows a scalar one; both reach the factory on the
service's thread, and the one it keeps is the service's to release.
```maxon
typealias Whole = int(i64.min to i64.max)

type Box uses T
	var item as T
	var extra as Whole

	static function create(item T, extra Whole) returns Self
		return Self{item: item, extra: extra}
	end 'create'

	export function extraOf() returns Whole
		return self.extra
	end 'extraOf'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create("a label long enough to be a heap string", extra: 7)
	let n = try await h.extraOf() otherwise return 1
	print("{n}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: services.a-spawn-starts-the-factory-it-names-on-the-service -->
The `spawn` of `create` and of `createCounted` runs the factory it names on the service's thread.
```maxon
typealias Whole = int(i64.min to i64.max)

type Sink
	var seen as Whole

	static function create(label String) returns Self
		if label.isEmpty() 'unlabelled'
			return Self{seen: 0}
		end 'unlabelled'

		return Self{seen: 1}
	end 'create'

	static function createCounted(count Whole) returns Self
		return Self{seen: count}
	end 'createCounted'

	export function total() returns Whole
		return self.seen
	end 'total'
end 'Sink'

function main() returns ExitCode
	let a = spawn Sink.create("a label long enough to be a heap string")
	let b = spawn Sink.createCounted(5)
	let first = try await a.total() otherwise return 1
	let second = try await b.total() otherwise return 2
	print("{first} {second}\n")
	a.shutdown()
	b.shutdown()
	return 0
end 'main'
```
```stdout
1 5
```
```exitcode
0
```

<!-- test: services.a-factory-with-defaults-and-labels-runs-on-the-service -->
Labels reorder the factory's arguments and defaults fill the omitted ones before the call crosses to the service's
thread, so the service starts from exactly the values an ordinary call would have bound.
```maxon
typealias Whole = int(i64.min to i64.max)

type Calc
	var value as Whole

	static function create(first Whole, scale Whole = 10, offset Whole = 1) returns Self
		return Self{value: first * scale + offset}
	end 'create'

	export function total() returns Whole
		return self.value
	end 'total'
end 'Calc'

function main() returns ExitCode
	let a = spawn Calc.create(3, offset: 5, scale: 2)
	let b = spawn Calc.create(4)
	let first = try await a.total() otherwise return 1
	let second = try await b.total() otherwise return 2
	print("{first} {second}\n")
	a.shutdown()
	b.shutdown()
	return 0
end 'main'
```
```stdout
11 41
```
```exitcode
0
```

<!-- test: services.a-factory-takes-ten-arguments-of-mixed-kinds-on-the-service -->
Ten arguments — integers, floats and a value held at an interface type — travel to the service's thread in the order and
register class the factory declares them in, and the interface value is released with its own witness.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias Area = float(f64.min to f64.max)

interface Shape
	function area() returns Area
end 'Shape'

type Square implements Shape
	var side as Area

	static function create(side Area) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Area
		return self.side * self.side
	end 'area'
end 'Square'

type Plot
	var count as Whole
	var span as Area

	static function create(a Whole, b Area, shape isolated Shape, c Whole, d Area, e Whole, f Area, g Whole, h Area, i Whole) returns Self
		return Self{count: a + c + e + g + i, span: b + d + f + h + shape.area()}
	end 'create'

	export function show()
		print("{self.count} {self.span:.1}\n")
	end 'show'
end 'Plot'

function main() returns ExitCode
	let p = spawn Plot.create(1, b: 0.5, shape: Square.create(2.0), c: 2, d: 1.5, e: 3, f: 2.5, g: 4, h: 3.5, i: 5)
	try p.show() otherwise panic("an open mailbox with room takes the send")
	p.shutdown()
	return 0
end 'main'
```
```stdout
15 12.0
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-that-succeeds-starts-the-service -->
A factory that throws is a throwing expression: the spawner parks until the factory has run on the new service's green
thread and then holds the handle. The factory's output therefore comes before anything the spawner prints after the
`spawn`.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Gate
	var open as Whole

	static function create(ok bool) returns Self throws StartError
		print("factory runs\n")

		if not ok 'closed'
			throw StartError.refused
		end 'closed'

		return Self{open: 1}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function main() returns ExitCode
	let g = try spawn Gate.create(true) otherwise return 1
	print("spawn returned\n")
	let n = try await g.state() otherwise return 1
	print("state={n}\n")
	g.shutdown()
	return 0
end 'main'
```
```stdout
factory runs
spawn returned
state=1
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-that-fails-throws-at-the-spawn -->
<!-- procs: 1 -->
A factory that throws starts no service: the spawner receives the factory's error, of the factory's own type, where it
wrote the `spawn`, and the mailbox the spawn made is gone with the handle that never came.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
	exhausted
end 'StartError'

type Gate
	var open as Whole

	static function create(code Whole) returns Self throws StartError
		print("factory runs\n")

		if code == 1 'refusing'
			throw StartError.refused
		end 'refusing' else if code == 2 'exhausting'
			throw StartError.exhausted
		end 'exhausting'

		return Self{open: code}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function main() returns ExitCode
	try spawn Gate.create(2) otherwise (e) 'failed'
		print("handling\n")

		match e 'which'
			refused then print("refused\n")
			exhausted then print("exhausted\n")
		end 'which'

		return 0
	end 'failed'

	print("started\n")
	return 1
end 'main'
```
```stdout
factory runs
handling
exhausted
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-with-a-payload-error-reports-the-payload -->
A factory that throws a case of an error union carrying a payload, a heap string or an integer, hands that payload to the
spawner's handler, which matches the case and reads it.
```maxon
typealias Whole = int(i64.min to i64.max)

union StartError implements Error
	refused(reason String)
	exhausted(limit Whole)
end 'StartError'

type Gate
	var open as Whole

	static function create(code Whole) returns Self throws StartError
		if code == 1 'refusing'
			throw StartError.refused("the gate was refused for a reason long enough to be a heap string")
		end 'refusing' else if code == 2 'exhausting'
			throw StartError.exhausted(code * 21)
		end 'exhausting'

		return Self{open: code}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function attempt(code Whole)
	try spawn Gate.create(code) otherwise (e) 'failed'
		match e 'which'
			refused(reason) then print("refused: {reason}\n")
			exhausted(limit) then print("exhausted at {limit}\n")
		end 'which'

		return
	end 'failed'

	print("started\n")
end 'attempt'

function main() returns ExitCode
	attempt(1)
	attempt(2)
	return 0
end 'main'
```
```stdout
refused: the gate was refused for a reason long enough to be a heap string
exhausted at 42
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-error-propagates-through-a-throwing-function -->
A function declared to throw the factory's error propagates it with `try spawn` like any throwing call, and its caller
handles it.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Gate
	var open as Whole

	static function create(ok bool) returns Self throws StartError
		if not ok 'closed'
			throw StartError.refused
		end 'closed'

		return Self{open: 1}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function start(ok bool) returns Gate.handle throws StartError
	return try spawn Gate.create(ok)
end 'start'

function main() returns ExitCode
	let g = try start(true) otherwise return 1
	let n = try await g.state() otherwise return 1
	print("state={n}\n")
	g.shutdown()

	let h = try start(false) otherwise (e) 'failed'
		match e 'which'
			refused then print("refused\n")
		end 'which'

		return 0
	end 'failed'

	h.shutdown()
	return 1
end 'main'
```
```stdout
state=1
refused
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-in-a-try-block-routes-to-the-block-handler -->
Inside a block-form `try`, a bare `spawn` of a throwing factory sends its error to the block's handler, as every other
throwing call there does.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Gate
	var open as Whole

	static function create(ok bool) returns Self throws StartError
		if not ok 'closed'
			throw StartError.refused
		end 'closed'

		return Self{open: 1}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function main() returns ExitCode
	try 'starting'
		let g = spawn Gate.create(true)
		print("first started\n")
		g.shutdown()
		let h = spawn Gate.create(false)
		h.shutdown()
		print("unreachable\n")
	end 'starting'
	otherwise (e) 'failed'
		match e 'which'
			refused then print("refused\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```stdout
first started
refused
```
```exitcode
0
```

<!-- test: services.a-throwing-generic-factory-starts-a-generic-service -->
A generic service's throwing factory hands back the handle of the instantiation its arguments fix, and a failing one
throws its error at the spawn.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Box uses T
	var item as T
	var extra as Whole

	static function create(item T, extra Whole) returns Self throws StartError
		if extra < 0 'negative'
			throw StartError.refused
		end 'negative'

		return Self{item: item, extra: extra}
	end 'create'

	export function extraOf() returns Whole
		return self.extra
	end 'extraOf'
end 'Box'

function main() returns ExitCode
	let h = try spawn Box.create("a label long enough to be a heap string", extra: 7) otherwise return 1
	let n = try await h.extraOf() otherwise return 1
	print("{n}\n")
	h.shutdown()

	try spawn Box.create(5, extra: -1) otherwise (e) 'failed'
		match e 'which'
			refused then print("refused\n")
		end 'which'

		return 0
	end 'failed'

	return 1
end 'main'
```
```stdout
7
refused
```
```exitcode
0
```

<!-- test: services.a-throwing-generic-factory-releases-a-managed-argument-when-the-spawn-fails -->
A failing `spawn` of a generic service's throwing factory releases the managed `T` argument it consumed: the
factory throws before it builds the service, so nothing else owns the heap string.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Box uses T
	var item as T
	var extra as Whole

	static function create(item T, extra Whole) returns Self throws StartError
		if extra < 0 'negative'
			throw StartError.refused
		end 'negative'

		return Self{item: item, extra: extra}
	end 'create'

	export function extraOf() returns Whole
		return self.extra
	end 'extraOf'
end 'Box'

function main() returns ExitCode
	try spawn Box.create("another label long enough to be a heap string", extra: -1) otherwise (e) 'failed'
		match e 'which'
			refused then print("refused\n")
		end 'which'

		return 0
	end 'failed'

	return 1
end 'main'
```
```stdout
refused
```
```exitcode
0
```

<!-- test: services.a-throwing-factory-takes-ten-arguments-of-mixed-kinds-on-the-service -->
The spawner's wait costs the start record nothing: ten arguments — integers, floats and a value held at an interface
type — reach a throwing factory in the order and register class it declares them in, and one that fails releases the
interface value with its own witness.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias Area = float(f64.min to f64.max)

enum PlotError implements Error
	empty
end 'PlotError'

interface Shape
	function area() returns Area
end 'Shape'

type Square implements Shape
	var side as Area

	static function create(side Area) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Area
		return self.side * self.side
	end 'area'
end 'Square'

type Plot
	var count as Whole
	var span as Area

	static function create(a Whole, b Area, shape isolated Shape, c Whole, d Area, e Whole, f Area, g Whole, h Area, i Whole) returns Self throws PlotError
		if a < 0 'empty'
			throw PlotError.empty
		end 'empty'

		return Self{count: a + c + e + g + i, span: b + d + f + h + shape.area()}
	end 'create'

	export function summary() returns String
		return "{self.count} {self.span:.1}"
	end 'summary'
end 'Plot'

function main() returns ExitCode
	let p = try spawn Plot.create(1, b: 0.5, shape: Square.create(2.0), c: 2, d: 1.5, e: 3, f: 2.5, g: 4, h: 3.5, i: 5) otherwise return 1
	let line = try await p.summary() otherwise return 1
	print("{line}\n")
	p.shutdown()

	try spawn Plot.create(-1, b: 0.5, shape: Square.create(2.0), c: 2, d: 1.5, e: 3, f: 2.5, g: 4, h: 3.5, i: 5) otherwise (e) 'failed'
		match e 'which'
			empty then print("empty\n")
		end 'which'
	end 'failed'

	return 0
end 'main'
```
```stdout
15 12.0
empty
```
```exitcode
0
```

<!-- test: services.error.a-message-that-spawns-a-throwing-factory-is-refused -->
A message handler that spawns a throwing factory waits on its own green thread for the factory to run, and that
wait is a blocking wait like any awaited reply: the handler would hold its own service while it waits for the
start, so the message is refused. The spawn belongs to `main` or to a coroutine of it, or the factory cannot
throw.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Worker
	var id as Whole

	static function create(ok bool) returns Self throws StartError
		if not ok 'closed'
			throw StartError.refused
		end 'closed'

		return Self{id: 7}
	end 'create'

	export function ident() returns Whole
		return self.id
	end 'ident'
end 'Worker'

type Manager
	var launched as Whole

	static function create() returns Self
		return Self{launched: 0}
	end 'create'

	export function launch(ok bool) returns Whole
		let worker = try spawn(capacity: 8) Worker.create(ok) otherwise return 0
		worker.shutdown()
		self.launched = self.launched + 1
		return self.launched
	end 'launch'
end 'Manager'

function main() returns ExitCode
	let manager = spawn Manager.create()
	let first = try await manager.launch(true) otherwise return 1
	print("{first}\n")
	manager.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:31:18: the message 'launch' may wait (it awaits the start of a service at line 32) while holding its own service
```

<!-- test: services.a-service-spawns-a-service-and-hands-back-its-handle -->
A message handler may spawn a service whose factory cannot throw: the spawn answers with the handle at once and
nothing waits. The handler keeps no promise, hands the handle back in its reply, and the caller does the awaiting
against the new service itself.
```maxon
typealias Whole = int(i64.min to i64.max)

type Worker
	var id as Whole

	static function create(id Whole) returns Self
		return Self{id: id}
	end 'create'

	export function ident() returns Whole
		return self.id
	end 'ident'
end 'Worker'

type Manager
	var launched as Whole

	static function create() returns Self
		return Self{launched: 0}
	end 'create'

	export function launch(id Whole) returns Worker.handle
		self.launched = self.launched + 1
		return spawn(capacity: 8) Worker.create(id)
	end 'launch'
end 'Manager'

function main() returns ExitCode
	let manager = spawn Manager.create()
	let first = try await manager.launch(7) otherwise return 1
	let second = try await manager.launch(9) otherwise return 1
	let a = try await first.ident() otherwise return 1
	let b = try await second.ident() otherwise return 1
	print("{a} {b}\n")
	manager.shutdown()
	return 0
end 'main'
```
```stdout
7 9
```
```exitcode
0
```

<!-- test: services.a-failed-factory-releases-its-arguments -->
A factory that throws leaves the arguments the spawn handed it where the start record held them, and the service's
thread releases them: a managed argument the factory only reads and one it keeps both end with the program's last
reference gone, and the leak gate reads zero.
```maxon
typealias Whole = int(i64.min to i64.max)
typealias WholeArray = Array with Whole

enum StartError implements Error
	refused
end 'StartError'

type Ledger
	var label as String
	var total as Whole

	static function create(label String, ids isolated WholeArray) returns Self throws StartError
		if ids.count() == 0 'empty'
			throw StartError.refused
		end 'empty'

		return Self{label: label, total: ids.count() as Whole}
	end 'create'

	export function size() returns Whole
		return (self.label.byteLength() as Whole) + self.total
	end 'size'
end 'Ledger'

function main() returns ExitCode
	let ids = WholeArray.create(0) as isolated WholeArray

	try spawn Ledger.create("a label long enough to be a heap string", ids: ids) otherwise (e) 'failed'
		match e 'which'
			refused then print("refused\n")
		end 'which'
	end 'failed'

	let more = WholeArray.create(0) as isolated WholeArray

	modify more 'fill'
		more.push(1)
		more.push(2)
		more.push(3)
	end 'fill'

	let kept = try spawn Ledger.create("a label long enough to be a heap string", ids: more) otherwise return 1
	let n = try await kept.size() otherwise return 1
	print("{n}\n")
	kept.shutdown()
	return 0
end 'main'
```
```stdout
refused
42
```
```exitcode
0
```

<!-- test: error.a-throwing-factory-spawned-without-try-is-refused -->
A throwing factory is a throwing call: a `spawn` of one that nothing catches is E3057, as an unhandled call to any
throwing function is.
```maxon
typealias Whole = int(i64.min to i64.max)

enum StartError implements Error
	refused
end 'StartError'

type Gate
	var open as Whole

	static function create(ok bool) returns Self throws StartError
		if not ok 'closed'
			throw StartError.refused
		end 'closed'

		return Self{open: 1}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function main() returns ExitCode
	let g = spawn Gate.create(true)
	g.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3057: <fragment>:25:10: throwing function requires try: 'Gate.create'
```

<!-- test: error.try-on-a-spawn-of-a-non-throwing-factory-is-refused -->
A `try` needs a call that can fail: a factory that cannot throw makes `try spawn` the same E3055 `try` on any
non-throwing call is.
```maxon
typealias Whole = int(i64.min to i64.max)

type Gate
	var open as Whole

	static function create() returns Self
		return Self{open: 1}
	end 'create'

	export function state() returns Whole
		return self.open
	end 'state'
end 'Gate'

function main() returns ExitCode
	let g = try spawn Gate.create() otherwise return 1
	g.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3055: <fragment>:17:10: try requires a throwing function: 'Gate.create' does not throw'
```

<!-- test: services.a-generic-body-spawns-a-generic-service-over-a-concrete-type-from-every-instantiation -->
Spawn sites that share one service and differ in what their spawner knows each start it from the layout of the instance
the arguments fix: two instantiations of a generic body and a concrete function all start `Worker with String`, and
every start sees its own arguments.
```maxon
typealias Whole = int(i64.min to i64.max)

type Worker uses T
	typealias Items = Array with T

	var items as Items
	var extra as Whole

	static function create(seed T, extra Whole) returns Self
		let items = Items.create(0)
		items.push(seed)
		return Self{items: items, extra: extra}
	end 'create'

	export function total() returns Whole
		return (self.items.count() as Whole) + self.extra
	end 'total'
end 'Worker'

type Pool uses T
	typealias Items = Array with T

	var jobs as Items

	static function create() returns Self
		return Self{jobs: Items.create(0)}
	end 'create'

	function add(job T)
		self.jobs.push(job)
	end 'add'

	function launch() returns Whole
		let w = spawn Worker.create("a label long enough to be a heap string", extra: self.jobs.count() as Whole)
		let n = try await w.total() otherwise panic("the worker is running")
		w.shutdown()
		return n
	end 'launch'
end 'Pool'

typealias NumberPool = Pool with Whole
typealias WordPool = Pool with String

function plain() returns Whole
	let w = spawn Worker.create("another label long enough to be a heap string", extra: 100)
	let n = try await w.total() otherwise panic("the worker is running")
	w.shutdown()
	return n
end 'plain'

function main() returns ExitCode
	let numbers = NumberPool.create()
	numbers.add(1)
	numbers.add(2)
	let words = WordPool.create()
	words.add("a label long enough to be a heap string")
	print("{plain()} {numbers.launch()} {words.launch()}\n")
	return 0
end 'main'
```
```stdout
101 3 2
```
```exitcode
0
```

<!-- test: spawn-of-a-type-two-directories-export-starts-each-directorys-own-service -->
`a/` and `b/` each export a `type Calc` and each spawns the bare `Calc` of its own directory. The whole-program
spawn probe reads raw tokens, so it records the written name and must resolve it against the spawning file's
directory: each `Calc` is a service, and each handle's `bump` runs in its own directory's service.
```maxon
// --- file: a/calc.maxon
export type Calc
	var count as ExitCode

	export static function create() returns Self
		return Self{count: 1}
	end 'create'

	export function bump(by ExitCode) returns ExitCode
		self.count = self.count + by

		return self.count
	end 'bump'
end 'Calc'

export function runA() returns ExitCode
	let h = spawn Calc.create()

	return try await h.bump(2) otherwise 99
end 'runA'

// --- file: b/calc.maxon
export type Calc
	var count as ExitCode

	export static function create() returns Self
		return Self{count: 10}
	end 'create'

	export function bump(by ExitCode) returns ExitCode
		self.count = self.count + by

		return self.count
	end 'bump'
end 'Calc'

export function runB() returns ExitCode
	let h = spawn Calc.create()

	return try await h.bump(30) otherwise 99
end 'runB'

// --- file: app/main.maxon
function main() returns ExitCode
	let direct = a.Calc.create()
	let other = b.Calc.create()

	return runA() + runB() + direct.bump(1) + other.bump(2)
end 'main'
```
```exitcode
57
```
