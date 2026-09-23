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

### A send MOVES or LENDS, and that is load-bearing

A `spawn`'s factory arguments, a reply, and a message argument that is a `var`, a temporary or a literal are
**moved** across: the far side becomes the value's one owner. A message argument that is a `let` LOCAL
owning its graph is **lent**: the sender's binding stays readable, and the service reads the same graph,
whose counts are stepped atomically while two green threads hold it. This is not ergonomics: a refcount
step on a box one green thread holds is a plain load/add/store, so a send that put one box into two green
threads' hands without saying so would not be slow, it would corrupt the heap. Two refusals are static. A
ROOT this frame does not solely own — a value a closure or a container also holds, or a borrowed parameter
— is **E3138**, and a value that cannot have exactly one owner on the far side at all is **E3135**: a
`Promise` (a handle its awaiter owns), a function value (whose captured environment is shared), an opaque
type parameter (no layout at the send). Every other aggregate — a container, a record with a managed field,
a union with a managed payload, a value held at an interface type — is admitted, moved or lent, after a
runtime WALK over its graph at the send (an interface-typed value through its witness, whose table names the
conformer's walk): no reachable refcounted record may have an owner outside the graph (a record the graph
reaches twice is legal when both references are its owners; an immortal literal has no count and passes), a
shared copy-on-write buffer is detached, and a RECORD with an owner outside the graph aborts the process with
exit **96** rather than hand one box to two green threads.

A lent graph is **frozen** from the send on (**E3160**): neither the binding nor any value read out of it
may reach storage through which it could be written — a `var`, a field or union payload, a container, a
`return`, a callee parameter that keeps it, a closure capture. Reads, `let` bindings, interpolation,
parameters that neither write nor keep, and a second send are allowed. A value whose type graph holds
nothing a statement can write — a service handle, a struct whose fields are all `let` over such types — is
exempt. The receiving side answers to the same rule, whole-program: a handler whose parameter escapes
through one of those doors refuses every send that lends to it (E3160 at the send, naming the escape), and
a handler that writes its parameter refuses it with E3019.

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

⚠ **NO CASE HERE IS MARKED FOR STARTING A SERVICE, BECAUSE THE COMPILER SAYS SO LOUDER.** A running
service reaches a green thread's context switch; all four native lanes have one, and wasm32-wasi is
refused by `SemanticCheck.requireTargetSupportsServiceEntry`, which the harness reports as a counted SKIP
naming the case. A marker would restate that and take the case out of the run in silence — see
`specs/async-scheduler.md`'s Targets section for the one statement of it.

⛔ **THE FIVE REFUSALS THAT DO EXCLUDE wasm32-wasi ARE EXCLUDED FOR THEIR DIAGNOSTIC, NOT FOR THEIR
RULE.** Each is target-neutral and would be reached on every lane, but on wasm the same program earns
E3104 for `__svc_spawn` / `__mbox_send` FIRST, so the stderr they pin is not what the compiler emits
there. MEASURED with the exclusions lifted. They expire when wasm grows a service substrate, not when the
rule moves — which is why the reason is written here rather than left to the marker.

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
(`StdToArm64Conversion.maxon:651`, `StdToWasm.maxon:1738`, `StdToX64Conversion.maxon:3368`) where the same
substrate reached by `sleep` has always answered E3104.

## Tests

<!-- test: companions-resolve-as-types -->
A `spawn` makes the type a service, and both synthesized companions are then nameable TYPES — in a
signature written by a function that spawns nothing.

⭐ **IT CAN NOW FAIL, AND WAVE 2's NOTE THAT IT COULD NOT IS SPENT.** While the wave-3 E2015 stood at the
`spawn`, the file's parse ended before an unresolved `Calc.handle` was ever reported, so this case passed
with the whole-program spawn walk disabled and only the two CROSS-FILE cases could see the companions. That
throw is gone: the program parses to completion and runs.

⛔ **BUT NOT BY THE E3011 THIS PARAGRAPH USED TO CLAIM — SABOTAGE MEASURED AT THE SV1 REVIEW.** It said *"an
absent companion is `E3011 Unknown type 'Calc.handle'` at `serve`'s own signature"*. With the handle mint
withheld (`ServiceCompanions.synthesizeServiceCompanions`'s pass 1 skipped) this case IS red — but as
`panic at Parser.maxon:45435: serviceHandleLayout: nothing declares 'type Calc.handle'`, raised at the
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

export typealias Integer = int(i64.min to i64.max)
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

export typealias Integer = int(i64.min to i64.max)
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
error E3135: <fragment>:17:10: parameter `p` of the message `Calc.hold` is a Promise, which is a green-thread handle its awaiter owns, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
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
error E3135: <fragment>:17:10: parameter `op` of the message `Calc.apply` is a function value, whose captured environment is a box a second thread would share, and this `spawn` makes `Calc` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
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

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.measure(Square.create(3))
	h.measure(Square.create(2))
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
error E3135: <fragment>:15:10: parameter `x` of the message `Box.put` is an opaque type parameter, whose layout is not known at the send, and this `spawn` makes `Box` a service — whose messages hand their arguments to another green thread. Send a `.clone()`, send the scalar it is derived from, or drop the parameter from the message
```

<!-- test: error.a-constrained-generic-service-is-refused -->
Lifting the generic gate did not lift it for a CONSTRAINED generic, and the refusal is a mechanism rather
than caution: an instance method of a constrained generic takes one hidden witness pointer per constraint,
and `Box.__loop` is synthesized with no `self` to source one from. Compare
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
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:31:10: Unsupported: `spawn Box.create(…)` — `type Box` constrains its type parameters, and an instance method of a constrained generic takes one hidden witness pointer per constraint, which the synthesized `Box.__loop` has no `self` to source. Spawn an UNCONSTRAINED generic or a plain type, or move the constrained work behind a message that takes concrete arguments
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
error E2015: <fragment>:15:20: Unsupported: `spawn Box.create(…)` fixes nothing for the type parameter `T` of `type Box` — a `spawn` names the type outright, so its type arguments are read off the factory's own arguments and `create` declares no parameter typed at `T`. Give the factory a parameter of that type, or spawn a type that fixes it
```

<!-- test: error.a-spawn-inside-a-generic-body-is-refused -->
The same rule one level out: a `spawn` inside another generic's body fixes the service's type arguments to
the ENCLOSING declaration's type parameters, which name no layout. `Box.__loop` is synthesized per
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
error E2015: <fragment>:22:21: Unsupported: `spawn Box.create(…)` fixes `type Box`'s type arguments to the enclosing declaration's own type parameters, which name no layout — and `Box.__loop` is synthesized per instantiation, so it has no dictionary to read one from. Spawn the service from a body that knows the concrete type
```

<!-- test: a-private-method-is-not-a-message -->
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
declarations in this tree depend on it (`specs/associated-types.md` declares
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
Two spawns of one type are two services with two states.

⚠ **THE ORDER OF THE TWO PRINTS IS FORCED BY CAUSALITY AND NOT BY LUCK.** `b` prints its own count and then
sends `report` to `a`, so `a`'s line cannot be written until `b`'s handler has already written its own — two
services printing on two green threads with nothing between them would be ordered by the scheduler. Lending
`a`'s handle to `b`'s handler is what buys that ordering; `borrow.a-lent-handle-may-be-kept-by-the-handler`
pins a lent handle on its own.
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
⭐⭐ **THE CASE THAT REACHES THE ABANDON PATH, AND IT REACHES IT BY EITHER OF TWO ROADS.** The second `keep`
is sent after the poison pill, so the loop never runs its handler — and which road drops its `String` is a
race this case deliberately does not resolve: if the send wins, the envelope is queued behind the pill and
the loop's exit drain abandons it; if the loop wins, the mailbox is already closed and `__mbox_send` abandons
it inline. **Both roads must print exactly the same thing and leak exactly nothing**, which is the property
worth pinning — the drop of a sent payload nobody will ever handle.
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
<!-- procs: 1 -->
<!-- preempt: off -->
⚠ **THE TWO PINS ARE THIS CASE'S OWN STATED PREMISE, WRITTEN DOWN WHERE THE RUNNER CAN READ THEM.** The
paragraph below says *"nothing runs on a service's green thread until the main thread stops running"*, and
that takes BOTH: one processor, because there is no `MAXON_MAX_PROCS=1` default — the count is the machine's,
and with a second M the two services run concurrently and either order is correct (MEASURED 2/5 red on
arm64-macOS and 3/3 on arm64-linux before the `procs` pin) — and a monitor that leaves `main` its processor,
because the monitor asks any strand that has held one for 10 ms of WALL time to yield, a preempted `main`
goes to the global tail and its machine takes `runnext`, so a `main` held off a core between its two spawns
prints `beep 2` before `beep 1` (MEASURED on the x64-linux runner at 456ad242). The ORDER follows from the
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
		sink.say("from the worker")
	end 'run'
end 'Worker'

function main() returns ExitCode
	var logger = spawn Logger.create()
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

<!-- test: a-handle-read-out-of-an-array-survives-a-second-send -->
⭐⭐ **A HANDLE READ *OUT OF* A CONTAINER, WHICH `handles-in-an-array` NEVER DOES — IT ONLY PUSHES AND
DROPS.** Binding an element to a local promotes a BORROW to an owned name, and the language's two retain
doors disagreed about what that costs for a handle: the void one asked
`SignatureIndex.managedNameRetainCallee` and got the mailbox's `handles`/`refs` pair, the RETURNING one
(`Parser.retainBorrowedAggregate`, the door every `arr.get(i)` binding takes) spelled the plain box incref
directly. So the local's drop stepped a pair its retain never stepped, the mailbox was CLOSED AND FREED while
the array's handle still named it — and the SECOND `get` sent into freed memory.

⚠ **ONE SEND WAS CLEAN, WHICH IS WHY NOTHING CAUGHT IT.** The first message is already queued when the
mailbox dies, so it is still handled and still answered; the loop only stops afterwards. **MEASURED at
`MAXON_MAX_PROCS` 1 and 16 alike: two sends is exit 92 (`RuntimeAbort.schedulerDeadlock`) and a `.clone()` of
the borrowed element is exit 89 (`slabFreeOfParkedSpan`)** — a heap corruption, not a diagnostic. This case
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
typealias BumpReplyArray = Array with Promise with (Integer, ServiceError)

function main() returns ExitCode
	var hs = CounterHandleArray.create()
	hs.push(spawn Counter.create())

	var replies = BumpReplyArray.create()
	for step in 1 to 3 'eachSend'
		var h = try hs.get(0) otherwise panic("hs.get(0)")
		replies.push(h.bump(step))
	end 'eachSend'

	var total = 0
	for r in replies 'eachReply'
		total = try await r otherwise 0
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
A `var` argument is MOVED: the sending frame hands over the reference it holds and the service becomes the
box's one owner. Nothing is increfed at the send and nothing is dropped by the sender, which is what keeps
the plain refcount correct across a green thread. The bare literal is an immortal `.rdata` record, promoted
to a fresh owned copy at the send; a `let` local would be LENT instead
(`borrow.a-let-string-stays-readable-after-the-send`).
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
	var a = "one-{1}"
	h.keep(a)
	var b = "two-{2}"
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

export typealias Integer = int(i64.min to i64.max)
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
A `var` argument MOVES: the send consumes it, the source is poisoned and a read is E3102. A `let` local is
LENT instead and stays readable — see the `borrow.*` cases.

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
error E3138: <fragment>:19:9: argument `s` of the message `Store.keep` cannot be proven to have exactly one owner (`buf`): this frame has either taken a SECOND reference to it — a container push, a closure capture, a consuming call — or received it across a frame boundary whose far side may still hold one (a parameter, or a call whose callee the compiler cannot prove returns a fresh record). A send moves this value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands (only a `let` local that solely owns its graph is lent instead). Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
```

<!-- test: error.a-borrowed-parameter-may-not-be-sent -->
Send-uniqueness does not survive a function boundary: `p` arrived as a BORROWED struct parameter and the
caller still holds it. ⭐ **THE RULE IS UNIFORM OVER BORROWS AND USED NOT TO BE.** A borrowed `String` was
PROMOTED to a copy here rather than refused, so one send site answered one question two ways — deciding by
whether the value's type happened to have a cheap owning copy — and the copy was an allocation the author
never wrote. `error.a-value-sent-through-a-parameter-is-refused` below is that shape, refused now.

⚠ What is still promoted is a value with NO owner at all: a STRING LITERAL this parse minted, whose record
is immortal `.rdata`. Nobody owns it, so nothing can be a second owner, and there is nothing to move — see
`a-string-argument-moves-into-the-service`, whose third send is a bare literal.
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
error E3138: <fragment>:23:9: argument `p` of the message `Store.keep` is BORROWED — read out of a field, an element or a parameter — so this frame does not own it. A send moves this value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands (only a `let` local that solely owns its graph is lent instead). Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
```

<!-- test: error.a-service-is-rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⭐ A service's whole substrate is x64-windows only, so on any other target BOTH ops are refused at their own
source span with `E3104`, naming the runtime entry that has no lowering there — never a panic from inside a
backend, which is what this family did before the gate existed.

⚠ **THE TWO OPS NEED TWO ARMS, AND ONE WOULD HAVE PASSED A HALF-BUILT GATE.** A `spawn` and a send mint
different entries, and a program can contain either without the other — `an-idle-service-that-is-never-sent-to-still-exits-zero`
is exactly a spawn with no send.
⚠ **THE NATIVE TWIN THIS CASE ONCE HAD IS RETIRED.** `error.a-service-is-rejected-on-a-native-target`
pinned the same two refusals on a native lane with no green-thread floor — arm64-macOS until MAC3 supplied
the scheduler primitive, then x64-Linux (arm64-Linux was considered and rejected as answering identically).
x64-Linux's floor left no native lane refusing, and the twin's program and both expected diagnostics were
byte-identical to this one's but for the target name, so it was deleted rather than duplicated here.
`async-sleep.md`'s surviving wasm case states the convention this follows.

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
error E3104: <fragment>:15:10: 'spawn' lowers to the runtime entry '__svc_spawn', which has no wasm32-wasi implementation
error E3104: <fragment>:16:2: a message send lowers to the runtime entry '__mbox_send', which has no wasm32-wasi implementation
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
		var cells = CellArray.create()
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{tag: "literal", label: "heap {k}", cells: cells}
	end 'create'
end 'Holder'

type Svc
	var held as Holder

	static function create(held Holder) returns Self
		return Self{held: held}
	end 'create'

	export function report()
		print("{self.held.tag} {self.held.label} {self.held.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(Holder.create(7))
	h.report()
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

	export function values() returns IntegerArray
		var xs = IntegerArray.create()
		xs.push(1)
		xs.push(2)
		xs.push(3)
		return xs
	end 'values'
end 'Source'

function main() returns ExitCode
	let h = spawn Source.create()
	let xs = try await h.values() otherwise IntegerArray.create()
	var sum = 0
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

	export function write(k Integer) returns Note
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

<!-- test: deepmove.abort.a-reply-holding-a-shared-record-aborts -->
The refusal on the reply road. The reply's root is a `CellArray` the handler mints in its own frame, so the
static E3137 rule admits it; what the walk finds inside is the `Cell` the service's state still owns —
`push` increfs the borrowed element, so that one record has two owners, the state and the reply. Handing it
across would put one box in two green threads' hands with a plain reference count between them, so the move
aborts with `RuntimeAbort` exit **96** and nothing reaches the awaiter.
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
		var cells = CellArray.create()
		cells.push(Cell.create())
		cells.push(Cell.create())
		return Self{cells: cells}
	end 'create'

	export function snapshot() returns CellArray
		var out = CellArray.create()
		let first = try self.cells.get(0) otherwise Cell.create()
		out.push(first)
		return out
	end 'snapshot'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let got = try await h.snapshot() otherwise CellArray.create()
	return got.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
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

	export function take(cs CellArray)
		var sum = 0
		for c in cs 'each'
			sum = sum + c.n
		end 'each'
		print("svc {cs.count()}\n")
		print("sum {sum}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cs = CellArray.create()
	cs.push(Cell.create())
	cs.push(Cell.create())
	let h = spawn Svc.create()
	h.take(cs)
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

<!-- test: deepmove.abort.a-container-holding-a-shared-record-aborts-at-the-send -->
The refusal the walk exists to make. `cell` is owned by `main`'s binding and by `cs` at once — `push`
increfs — and `main` reads it back after the send, so handing the array across would leave one box with a
plain refcount on two green threads. The root `cs` is this frame's alone, so no static arm can see the
second owner; the walk finds the `Cell` at a count of 2 and aborts the process before anything is enqueued —
`RuntimeAbort` exit **96** on the sender's own green thread. No processor count is
involved: nothing has crossed yet.
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
```exitcode
96
```

<!-- test: deepmove.a-cloned-array-detaches-its-shared-buffer-at-the-send -->
A shared BUFFER is not a shared record. `a.clone()` yields a second array RECORD viewing `a`'s buffer until
one of them writes. `b` is a `let`, so the send LENDS it, and the send's walk still detaches a shared buffer
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

	export function take(xs IntegerArray) returns Integer
		for x in xs 'each'
			print("svc {x}\n")
		end 'each'
		return xs.count()
	end 'take'
end 'Svc'

function main() returns ExitCode
	var a = IntegerArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	let b = a.clone()
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
A slice is a temporary too: each `slice` overload hands back the fresh record `sliceBytes` builds, a view onto
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
	let n = try await h.keep(src.slice(src.startIndex(), length: 7)) otherwise 0
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

<!-- test: deepmove.abort.a-string-whose-bytes-the-sender-still-views-aborts -->
An owned `String` keeps its bytes inline, so a `toByteArray()` view counts the String's own RECORD. That owner
is invisible to the static soleness question — `s` is a `var` nothing else names — so only the walk can see
it: moving `s` would leave the view on this green thread stepping a plain count the service steps too. The
walk finds the record at a count of 2 and aborts before anything is enqueued, exactly as the lend of the same
graph does (`borrow.abort.a-let-string-whose-bytes-the-sender-still-views-aborts`).
```maxon
type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(s String)
		print("svc {s}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var s = "payload {42}"
	let bytes = s.toByteArray()
	h.take(s)
	return bytes.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: deepmove.a-grown-string-whose-bytes-the-sender-still-views-detaches-at-the-send -->
<!-- procs: 16 -->
A String that has been APPENDED to owns a separate buffer, so a `toByteArray()` view counts that buffer and
not the record. A shared buffer is what copy-on-write is for: the move's walk detaches `s` onto a private copy,
and the view is left the old buffer's one owner on this green thread. Without the detach the view's release
here and the String's release on the service step one plain count from two processors — a lost update leaks
the buffer (exit 101) or frees it early.
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
	var readers = ReaderHandles.create()
	for _ in 1 to serviceCount 'spawnEach'
		readers.push(spawn Reader.create())
	end 'spawnEach'

	var kept = 0
	for i in 1 to rounds 'round'
		let r = try readers.get(i mod serviceCount) otherwise panic("readers.get")
		var s = ""
		s.append("payload {i mod 10}")
		let bytes = s.toByteArray()
		r.take(s)
		kept = kept + bytes.count()
	end 'round'

	var total = 0
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

	export function take(xs Texts)
		for x in xs 'each'
			print("svc {x}\n")
		end 'each'
	end 'take'
end 'Svc'

function main() returns ExitCode
	var xs = Texts.create()
	xs.append("a {1}")
	xs.append("b {2}")
	let h = spawn Svc.create()
	h.take(xs)
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

<!-- test: deepmove.abort.a-chain-holding-a-shared-record-aborts -->
The chain's refusal, and the element half of the walk is what finds it: `append` increfs the `Cell` it is
handed, `main` reads it back afterwards, and the chain's own record and nodes are all sole — so only a walk
that descends into the NODE's element sees the second owner. Exit **96** on the sender's own green thread.
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

	export function take(xs Cells)
		print("svc {xs.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var xs = Cells.create()
	var cell = Cell.create()
	xs.append(cell)
	let h = spawn Svc.create()
	h.take(xs)
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
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

	export function take(m Names)
		print("svc {m.count()}\n")
		let v = try m.get("a 1") otherwise "missing"
		print("got {v}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Names.create()
	m.upsert("a {1}", value: "x {2}")
	let h = spawn Svc.create()
	h.take(m)
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

<!-- test: deepmove.abort.a-map-holding-a-shared-value-aborts -->
The same refusal two containers down: `upsert` increfs the `Cell` into the value column, `main` reads it
back, and the walk finds that one record at two owners. Exit **96**.
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

	export function take(m Cells)
		print("svc {m.count()}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Cells.create()
	var cell = Cell.create()
	m.upsert("k {1}", value: cell)
	let h = spawn Svc.create()
	h.take(m)
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: deepmove.a-map-releases-a-removed-entry-so-the-map-crosses -->
`remove` releases the key and the value it takes out, so the map no longer owns them. `main` still holds both
records, but they are not in the moved graph: the walk reaches the tombstoned slot and finds it empty, and the
map crosses. A tombstone that kept its occupants would reach `key` and `text` inside the graph while `main`
owns them outside it, and the walk would abort with exit 96.
```maxon
typealias Texts = Map with (String, String)

type Svc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function take(m Texts) returns Integer
		return m.count() as Integer
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Texts.create()
	let key = "gone {1}"
	let text = "value {7}"
	m.upsert(key, value: text)
	m.upsert("kept {2}", value: "value {8}")
	let removed = m.remove(key)
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

	export function take(s Cells) returns Integer
		return s.count() as Integer
	end 'take'
end 'Svc'

function main() returns ExitCode
	var s = Cells.create()
	var cell = Cell.create(1)
	s.insert(cell)
	let removed = s.remove(cell)
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

	export function take(m Cells) returns Integer
		let kept = try m.get("kept 2") otherwise panic("the kept entry crossed with the map")
		return (m.count() as Integer) + kept.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Cells.create()
	let key = "gone {1}"
	var cell = Cell.create(7)
	m.upsert(key, value: cell)
	m.upsert("kept {2}", value: Cell.create(8))
	let removed = m.remove(key)
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

	export function take(s Names) returns Integer
		if s.contains("kept 2") 'kept'
			return s.count() as Integer
		end 'kept'

		return 0
	end 'take'
end 'Svc'

function main() returns ExitCode
	var s = Names.create()
	let gone = "gone {1}"
	s.insert(gone)
	s.insert("kept {2}")
	let removed = s.remove(gone)
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

	export function take(m Cells) returns Integer
		let kept = try m.get("kept 2") otherwise panic("the kept entry crossed with the map")
		return kept.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	var m = Cells.create()
	m.upsert("kept {2}", value: Cell.create())
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

	export function take(s Shape)
		match s 'k'
			empty then print("empty\n")
			labelled(t) then print("labelled {t}\n")
		end 'k'
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	h.take(Shape.labelled("heap {1 + 1}"))
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

<!-- test: deepmove.abort.a-union-payload-with-a-second-owner-aborts -->
The tag-guarded half of that walk, refusing: `Shape.held(cell)` increfs into the payload slot and `main`
reads `cell` back, so the live case's payload has two owners. Exit **96**.
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

	export function take(s Shape)
		match s 'k'
			empty then print("empty\n")
			held(c) then print("held {c.n}\n")
		end 'k'
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cell = Cell.create()
	let s = Shape.held(cell)
	let h = spawn Svc.create()
	h.take(s)
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: deepmove.a-deep-graph-crosses-and-is-released-on-the-service -->
⭐ **THE GRAPH IS BUILT ON `main` AND RELEASED ON THE SERVICE, AND A GREEN THREAD'S STACK STARTS AT 2 KB.**
The handler reads one scalar and returns, so the only frames that release the chain are the per-type
cascade's own — `__destruct_S63` calling `__destruct_S62`, once per level. A cascade recurses to the depth
of the PROGRAM's types, which is why it carries the green-thread stack guard that grows and relocates the
stack rather than the exemption the compiler's fixed-depth runtime primitives take. `report` is a SECOND
message so that the line it prints cannot grow the stack ahead of the release: with the cascade exempt this
faults on the stack, with no output, on every run at this depth (MEASURED with the cascade exempted:
`0xC0000005` on x64-windows).
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

	export function take(root S63)
		self.seen = self.seen + root.n
	end 'take'

	export function report()
		print("released {self.seen}\n")
	end 'report'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	h.take(S63.create())
	h.report()
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
		var cells = CellArray.create()
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

	static function create(held Pair) returns Self
		return Self{held: held}
	end 'create'

	export function report()
		self.held.left.n = 5
		print("{self.held.right.n} {self.held.right.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(Pair.create())
	h.report()
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
	var cs = CellArray.create()
	let c = Cell.create()
	cs.push(c)
	cs.push(c)
	return cs
end 'oneCellTwice'

type Svc
	var cells as CellArray

	static function create(cells CellArray) returns Self
		return Self{cells: cells}
	end 'create'

	export function report()
		var first = try self.cells.get(0) otherwise panic("two cells were pushed")
		first.n = 7
		let second = try self.cells.get(1) otherwise panic("two cells were pushed")
		print("{second.n}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellTwice())
	h.report()
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

	static function create(frame Frame) returns Self
		return Self{frame: frame}
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
	h.report()
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
		var cells = CellArray.create()
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

	export function make() returns Pair
		return Pair.create()
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create()
	var p = try await h.make() otherwise Pair.create()
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

<!-- test: deepmove.dag.an-outside-owner-of-a-record-reached-twice-aborts -->
Reaching a record twice is admitted only when every owner is inside the graph. `Pair.around(bag)` puts `bag`
in both fields, so the record has three owners and `main`'s binding, which reads it after the send, is not
one the walk meets: the move aborts with `RuntimeAbort` exit **96** before anything is enqueued.
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
		var cells = CellArray.create()
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

	export function take(p Pair)
		print("svc {p.right.n}\n")
	end 'take'
end 'Svc'

function main() returns ExitCode
	let bag = Bag.create()
	let h = spawn Svc.create()
	h.take(Pair.around(bag))
	return bag.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
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

	export function read(h Holder) returns Integer
		return h.text.byteLength() + h.bytes.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	var holder = Holder.around("payload {42}")
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
	var m = Cells.create()
	let c = Cell.create()
	m.upsert("a", value: c)
	m.upsert("b", value: c)
	return m
end 'oneCellUnderTwoKeys'

type Svc
	var cells as Cells

	static function create(cells Cells) returns Self
		return Self{cells: cells}
	end 'create'

	export function report()
		var a = try self.cells.get("a") otherwise panic("a was inserted")
		a.n = 7
		let b = try self.cells.get("b") otherwise panic("b was inserted")
		print("{b.n}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellUnderTwoKeys())
	h.report()
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
	var xs = Cells.create()
	let c = Cell.create()
	xs.append(c)
	xs.append(c)
	return xs
end 'oneCellTwice'

type Svc
	var cells as Cells

	static function create(cells Cells) returns Self
		return Self{cells: cells}
	end 'create'

	export function report()
		var first = try self.cells.first() otherwise panic("two cells were appended")
		first.n = 7

		for c in self.cells 'each'
			print("{c.n}\n")
		end 'each'
	end 'report'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create(oneCellTwice())
	h.report()
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
	var cs = CellArray.create()

	for _ in 1 to pairs 'each'
		let c = Cell.create()
		cs.push(c)
		cs.push(c)
	end 'each'

	return cs
end 'pairedCells'

type Svc
	var cells as CellArray

	static function create(cells CellArray) returns Self
		return Self{cells: cells}
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
	h.report()
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

<!-- test: deepmove.dag.an-outside-owner-among-many-records-reached-twice-aborts -->
Among forty records each met twice, one has a third owner: `main`'s `kept`, which the walk never meets. The
move aborts with exit **96** before anything is enqueued.
```maxon
type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Cell'

typealias CellArray = Array with Cell

function pairedCellsAround(kept Cell, pairs Integer) returns CellArray
	var cs = CellArray.create()

	for _ in 1 to pairs 'each'
		let c = Cell.create()
		cs.push(c)
		cs.push(c)
	end 'each'

	cs.push(kept)
	cs.push(kept)
	return cs
end 'pairedCellsAround'

type Svc
	var cells as CellArray

	static function create(cells CellArray) returns Self
		return Self{cells: cells}
	end 'create'

	export function report()
		print("svc {self.cells.count()}\n")
	end 'report'
end 'Svc'

function main() returns ExitCode
	let kept = Cell.create()
	let h = spawn Svc.create(pairedCellsAround(kept, pairs: 40))
	h.report()
	return kept.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: borrow.a-let-string-stays-readable-after-the-send -->
A `let` local is LENT, not moved: the service reads the sender's own record and the sender's binding stays
readable after the send. The awaited reply orders the service's read before `main`'s.
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
```exitcode
0
```
```stdout
service read 7 characters
sender still reads hello 1
```

<!-- test: borrow.a-cloned-let-string-is-lent-while-the-sender-keeps-its-source -->
A clone bound to a `let` is LENT, and its walk marks a record of its own: the source it was copied from is not
part of the graph, so `main` may go on writing it after the send.
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
	print("service read {n} bytes of {part}, sender wrote {src}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service read 10 bytes of payload 42, sender wrote payload 42!
```

<!-- test: borrow.abort.a-let-string-whose-bytes-the-sender-still-views-aborts -->
A `toByteArray()` view of an owned `String` counts the String's own record, because its bytes are inline. The
share walk finds that owner outside the lent graph and aborts, as the move of the same graph does
(`deepmove.abort.a-string-whose-bytes-the-sender-still-views-aborts`).
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
	return (n + bytes.count()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: borrow.two-services-and-the-sender-read-one-graph -->
One `let` graph — a record holding a `String`, an array of records and a union with a managed payload — is
lent to two services at once, and all three green threads read it while both replies are outstanding.
`digest` and `noteLength` neither write nor keep their parameters, so handing them the lent graph, or a
value read out of it, is allowed on both sides of the send.
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
		var tags = Tags.create()
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

	export function read(d Doc) returns Integer
		return self.id * 1000 + digest(d)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let one = spawn Reader.create(1)
	let two = spawn Reader.create(2)
	let doc = Doc.create(7)
	let first = one.read(doc)
	let second = two.read(doc)
	let mine = digest(doc)
	let a = try await first otherwise 0
	let b = try await second otherwise 0
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

<!-- test: borrow.one-graph-sent-many-times-to-one-service -->
The same `let` graph lent 200 times to one service: every queued message holds a share of it, and the
mailbox's FIFO puts `report` behind the last of them. `main` reads the graph again once the tallies are in.
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
		var leaves = Leaves.create()
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

	export function take(t Tree)
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
	for _ in 1 to 200 'lend'
		h.take(tree)
	end 'lend'
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
`lendAndReturn` lends its graph and returns while the message is still queued, so the envelope's share is
the graph's last owner and the service's release is the one that frees it — a release that never comes is
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
		var leaves = Leaves.create()
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

	export function take(t Tree)
		let first = try t.leaves.get(0) otherwise panic("a tree has leaves")
		self.seen = "{t.name} starting with {first.text}"
	end 'take'

	export function report() returns String
		return "service read {self.seen}"
	end 'report'
end 'Keeper'

function lendAndReturn(h Keeper.handle)
	let tree = Tree.create(3)
	h.take(tree)
	print("sender read {tree.name}\n")
end 'lendAndReturn'

function main() returns ExitCode
	let h = spawn Keeper.create()
	lendAndReturn(h)
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

<!-- test: borrow.a-graph-with-a-second-owner-aborts-at-the-send -->
A lent graph is walked at the send exactly as a moved one is. `Shape.held(cell)` increfs into the payload
slot, so the live case's payload has two owners — `cell` and `s` — and the walk aborts with exit **96**
before anything is enqueued; the read of `s` after the send never runs.
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

	export function take(s Shape)
		match s 'k'
			empty then print("empty\n")
			held(c) then print("held {c.n}\n")
		end 'k'
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cell = Cell.create()
	let s = Shape.held(cell)
	let h = spawn Svc.create()
	h.take(s)
	match s 'mine'
		empty then print("main empty\n")
		held(c) then print("main held {c.n}\n")
	end 'mine'
	return cell.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
96
```

<!-- test: borrow.a-handler-may-write-a-copy-of-a-record-inside-its-lent-parameter-through-a-self-writing-method -->
The control for the self-writing-method refusals: `copy` is the handler's own record, so `bump` writes
nothing the sender lent and the send is legal.
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

	export function poke(target Holder)
		var copy = target.inner.clone()
		copy.bump()
		print("{copy.n}\n")
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.poke(b)
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

<!-- test: borrow.dag.a-graph-reaching-one-record-twice-is-lent -->
A lent graph may reach one record through two fields: the second owner is inside the graph the service
reads, not outside it, so the send's walk admits it and the sender reads the record back after the reply.
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
		var cells = CellArray.create()
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

	export function look(target Pair) returns Integer
		print("service reads {target.right.n}\n")
		return target.right.cells.count() as Integer
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create()
	let cells = try await h.look(p) otherwise 0
	print("sender reads {p.left.n} with {cells} cells\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service reads 3
sender reads 3 with 2 cells
```

<!-- test: borrow.dag.a-graph-reaching-one-record-twice-is-lent-twice -->
The second lend of that graph finds it already marked, and marking is closed, so it crosses again with
nothing left to count.
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
		var cells = CellArray.create()
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

	export function look(target Pair) returns Integer
		print("service reads {target.right.n}\n")
		return target.right.cells.count() as Integer
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create()
	let first = try await h.look(p) otherwise 0
	let second = try await h.look(p) otherwise 0
	print("sender reads {p.left.n} with {first + second} cells\n")
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
sender reads 3 with 4 cells
```

<!-- test: borrow.dag.an-array-holding-one-record-twice-is-lent -->
The container's share walk meets the one `Cell` in both slots, marks it once and counts the second slot
against its owners.
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
		var cells = CellArray.create()
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

	export function look(shelf Shelf) returns Integer
		let second = try shelf.cells.get(1) otherwise panic("two cells were pushed")
		return second.n
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let shelf = Shelf.create()
	let n = try await h.look(shelf) otherwise 0
	let first = try shelf.cells.get(0) otherwise panic("two cells were pushed")
	print("service read {n}, sender reads {first.n} of {shelf.cells.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
service read 1, sender reads 1 of 2
```

<!-- test: borrow.dag.an-outside-owner-of-a-record-reached-twice-aborts -->
A lend admits a record reached twice only when every owner is inside the lent graph. `bag` is a third owner
the walk never meets, so the send aborts with exit **96** before anything is enqueued.
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
		var cells = CellArray.create()
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

	export function look(target Pair) returns Integer
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
```exitcode
96
```

<!-- test: borrow.dag.a-string-and-its-bytes-are-lent-together -->
An owned `String` keeps its bytes inline, so a `toByteArray()` view holds a reference to the String's own record.
`Holder` owns `text` and a view of it, so the record has two owners and both are inside the lent graph: the walk
meets the record through `text` and again through the view's reference, and the graph is lent.
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

	export function read(h Holder) returns Integer
		return h.text.byteLength() + h.bytes.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	let n = try await r.read(holder) otherwise 0
	print("n={n} {holder.text}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=20 payload 42
```

<!-- test: borrow.dag.bytes-and-their-string-are-lent-together -->
The same graph with the view declared first, so the walk meets the record through the view before it meets it
through `text`. Either order counts both references.
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

	export function read(h Holder) returns Integer
		return h.text.byteLength() + h.bytes.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	let n = try await r.read(holder) otherwise 0
	print("n={n} {holder.text}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
n=20 payload 42
```

<!-- test: borrow.dag.an-outside-owner-of-a-viewed-string-aborts -->
`kept` takes a third owner of the String's record outside the lent graph, so the send aborts with exit **96**.
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

	export function read(h Holder) returns Integer
		return h.text.byteLength() + h.bytes.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	var kept = TextArray.create()
	kept.push(holder.text)
	let n = try await r.read(holder) otherwise 0
	print("n={n} kept={kept.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias TextArray = Array with String
```
```exitcode
96
```

<!-- test: borrow.dag.an-outside-owner-of-a-string-its-bytes-reach-first-aborts -->
The same outside owner with the view declared first. The view's meeting is the record's first, so it is entered
in the visit table rather than only marked, and the meeting through `text` then finds the owner `kept` holds
unmet: exit **96**.
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

	export function read(h Holder) returns Integer
		return h.text.byteLength() + h.bytes.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let r = spawn Reader.create()
	let holder = Holder.around("payload {42}")
	var kept = TextArray.create()
	kept.push(holder.text)
	let n = try await r.read(holder) otherwise 0
	print("n={n} kept={kept.count()}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias TextArray = Array with String
```
```exitcode
96
```

<!-- test: borrow.a-lent-handle-may-be-kept-by-the-handler -->
A service handle holds nothing a statement can write, so a lent handle is exempt from the freeze: `Relay`
keeps `counter` in its state and sends through it in a later message, while `main` goes on sending through
its own binding. Each reply is awaited before the next line prints.
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
		return Self{peers: CounterHandles.create()}
	end 'create'

	export function adopt(peer Counter.handle)
		self.peers.push(peer)
	end 'adopt'

	export function forward(by Integer) returns Integer
		var total = 0
		for peer in self.peers 'each'
			total = total + (try await peer.bump(by) otherwise 0)
		end 'each'
		return total
	end 'forward'
end 'Relay'

function main() returns ExitCode
	let counter = spawn Counter.create()
	let relay = spawn Relay.create()
	relay.adopt(counter)
	let viaRelay = try await relay.forward(5) otherwise 0
	print("relay {viaRelay}\n")
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
relay 5
direct 7
```

<!-- test: borrow.many-services-count-one-graph-on-sixteen-processors -->
<!-- procs: 16 -->
⭐ **THE COUNT TORTURE.** Sixteen services walk one lent graph on sixteen processors, and every `get` binding
in `walk` retains and then releases a `Leaf` record the other fifteen walkers are stepping at the same time,
while `main` steps the root's count with every lend and each loop steps it back down with every release. A
lent graph's counts are stepped atomically; one lost update frees a record under a reader (a crash) or
never frees it (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

let serviceCount = 16
let rounds = 50
let leafCount = 64

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

	static function create() returns Self
		var leaves = Leaves.create()
		for i in 1 to leafCount 'grow'
			leaves.push(Leaf.create("leaf {i}"))
		end 'grow'
		return Self{name: "tree {leafCount}", leaves: leaves}
	end 'create'
end 'Tree'

type Checker
	var sum as Integer

	static function create() returns Self
		return Self{sum: 0}
	end 'create'

	export function walk(t Tree)
		for i in 0 upto t.leaves.count() 'each'
			let leaf = try t.leaves.get(i) otherwise panic("walk: leaf {i} is missing")
			self.sum = self.sum + (leaf.text.count() as Integer)
		end 'each'
	end 'walk'

	export function report() returns Integer
		return self.sum
	end 'report'
end 'Checker'

typealias CheckerHandles = Array with Checker.handle

function main() returns ExitCode
	var checkers = CheckerHandles.create()
	for _ in 1 to serviceCount 'spawnEach'
		checkers.push(spawn Checker.create())
	end 'spawnEach'

	let tree = Tree.create()
	for _ in 1 to rounds 'round'
		for k in 0 upto serviceCount 'lend'
			let c = try checkers.get(k) otherwise panic("checkers.get({k})")
			c.walk(tree)
		end 'lend'
	end 'round'

	var total = 0
	for k in 0 upto serviceCount 'collect'
		let c = try checkers.get(k) otherwise panic("checkers.get({k})")
		total = total + (try await c.report() otherwise 0)
	end 'collect'

	print("total {total}\n")
	print("sender still reads {tree.name} with {tree.leaves.count()} leaves\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total 351200
sender still reads tree 64 with 64 leaves
```

<!-- test: borrow.a-door-on-a-sibling-arm-does-not-follow-the-send -->
A door on the OTHER arm of the `match` whose arm lent the graph does not follow the send: no path runs both, so
`route` may hand `b` to `stash`, which keeps it, on the arm that did not lend it. A door follows a send where the
function's control flow reaches it from the send, not where the text places it later.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'

	export function total() returns Integer
		return self.seen
	end 'total'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function route(h Svc.handle, xs Boxes, n Integer, way Route)
	let b = Box.create(n)
	match way 'send'
		toService then h.take(b)
		toArray then stash(xs, item: b)
	end 'send'
end 'route'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
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

<!-- test: borrow.error.a-door-after-the-branch-that-lent-follows-the-send -->
<!-- unsupported-targets: wasm32-wasi -->
A door after the `match` follows the send on the arm that lent: a path runs the send and then `stash`, which
keeps `b`. Which parameters a function keeps is a whole-program fact, so on wasm32-wasi the send's E3104 is
reported first, and the case is pinned on the native lanes (§ Targets).
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function route(h Svc.handle, xs Boxes, n Integer, way Route)
	let b = Box.create(n)
	match way 'send'
		toService then h.take(b)
		toArray then print("kept, not sent\n")
	end 'send'
	stash(xs, item: b)
end 'route'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
	route(h, xs: xs, n: 2, way: Route.toService)
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:39:18: `b` was lent to another green thread at <fragment>:36:25, so what it holds is frozen: passing it to `stash`, which keeps it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-door-on-a-sibling-arm-inside-a-loop-follows-the-send -->
<!-- unsupported-targets: wasm32-wasi -->
Inside a loop a door on the sibling arm of the send follows it, though the text places it first: the next trip
takes the other arm while `b`, bound before the loop, is still the graph the service reads. The same
whole-program door as above, pinned on the native lanes.
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

	export function take(b Box)
		self.seen = self.seen + b.n
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
	var xs = Boxes.create()
	let b = Box.create(3)
	var trip = 0
	while trip < 2 'twice'
		match routeFor(trip) 'send'
			toArray then stash(xs, item: b)
			toService then h.take(b)
		end 'send'
		trip = trip + 1
	end 'twice'
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:44:33: `b` was lent to another green thread at <fragment>:45:26, so what it holds is frozen: passing it to `stash`, which keeps it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.a-let-rebound-each-trip-is-lent-afresh -->
The loop case above with `b` bound INSIDE the loop: the next trip binds a fresh record, so the door the back edge
reaches is not the graph the send lent and `stash` may keep it. What the freeze follows from a send stops where
the binding is bound again.
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

	export function take(b Box)
		self.seen = self.seen + b.n
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
	var xs = Boxes.create()
	var trip = 0
	while trip < 4 'eachTrip'
		let b = Box.create(trip + 1)
		match routeFor(trip) 'send'
			toArray then stash(xs, item: b)
			toService then h.take(b)
		end 'send'
		trip = trip + 1
	end 'eachTrip'

	let seen = try await h.total() otherwise 0
	var kept = 0
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

<!-- test: borrow.error.a-lent-let-may-not-be-bound-to-a-var -->
From the send on, a lent `let` is frozen: binding it to a `var` would give the service's graph a name that
can write it.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	var m = b
	m.n = 2
	return m.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:26:10: `b` was lent to another green thread at <fragment>:25:9, so what it holds is frozen: binding it to a `var` would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-assigned-to-a-var -->
Assigning a lent `let` to an existing `var` is the same door as binding one.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var m = Box.create()
	let b = Box.create()
	h.take(b)
	m = b
	m.n = 2
	return m.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:27:6: `b` was lent to another green thread at <fragment>:26:9, so what it holds is frozen: assigning it to a `var` would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-value-read-out-of-a-lent-let-may-not-be-bound-to-a-var -->
The freeze covers what is READ OUT of a lent binding: `p.inner` is part of the graph the service reads, so it
is frozen with `p`, and the diagnostic names the root.
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

	export function take(p Pair)
		self.seen = self.seen + p.inner.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let p = Pair.create(Box.create())
	h.take(p)
	var y = p.inner
	y.n = 2
	return y.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:34:10: `p` was lent to another green thread at <fragment>:33:9, so what it holds is frozen: binding it to a `var` would let it be written. Send a `.clone()` instead, or bind `p` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-stored-in-a-field -->
Storing a lent value in a field of a mutable record would let it be written through that record.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var holder = Holder.create()
	let b = Box.create()
	h.take(b)
	holder.item = b
	return holder.item.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:35:16: `b` was lent to another green thread at <fragment>:34:9, so what it holds is frozen: storing it in a field or payload would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-wrapped-in-a-new-record -->
Constructing a record around a lent value stores it exactly as a field assignment does. The record here is a
union payload, because a struct literal is legal only inside its own type.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	let w = Wrap.held(b)
	print("{w.name}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:31:20: `b` was lent to another green thread at <fragment>:30:9, so what it holds is frozen: storing it in a field or payload would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-stored-in-a-container -->
Pushing a lent value into a container would let it be written through the container.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
	let b = Box.create()
	h.take(b)
	xs.push(b)
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:29:10: `b` was lent to another green thread at <fragment>:28:9, so what it holds is frozen: storing it in a container would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-returned -->
Returning a lent value hands it to a caller, which may bind it to a `var`.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function lend() returns Box
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	return b
end 'lend'

function main() returns ExitCode
	let b = lend()
	return b.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:26:9: `b` was lent to another green thread at <fragment>:25:9, so what it holds is frozen: returning it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-parameter-that-keeps-it -->
<!-- unsupported-targets: wasm32-wasi -->
A callee parameter that KEEPS what it is handed is a door too: `stash` pushes `item` into an array it was
passed, and which parameters a function keeps is a whole-program fact — so on wasm32-wasi the send's E3104
is reported first, and the case is pinned on the native lanes (§ Targets).
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box)
	xs.push(item)
end 'stash'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
	let b = Box.create()
	h.take(b)
	stash(xs, item: b)
	return xs.count() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:33:18: `b` was lent to another green thread at <fragment>:32:9, so what it holds is frozen: passing it to `stash`, which keeps it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-be-captured-by-a-closure -->
A closure that captures a lent value keeps it for as long as the closure lives.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	let peek = function() gives b.n
	return peek() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:26:30: `b` was lent to another green thread at <fragment>:25:9, so what it holds is frozen: capturing it in a closure would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-method-that-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
A method that writes its own receiver is legal on a `let` (`parameter-mutation.md`), and after a lend it
would write the graph the service is reading. Which methods write their receiver is a whole-program fact,
so this is pinned on the native lanes.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	b.bump()
	return b.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:30:2: `b` was lent to another green thread at <fragment>:29:9, so what it holds is frozen: calling `bump`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-parameter-that-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
A `let` may not be handed to a parameter whose FIELD the callee writes: `poke` writing `p.n` writes the
caller's record, so the call earns E3019 (`SemanticCheck.checkImmutableArgToMutatingParam`) on its own, and
the lend adds the freeze on top — both are reported, because the second says why this write would also cross
a green thread. Which parameters a function writes is a whole-program fact, so this is pinned on the native
lanes.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function poke(p Box)
	p.n = 99
end 'poke'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	poke(b)
	return b.n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:30:2: cannot pass 'b' to function that mutates parameter 'p' (in main)
error E3160: <fragment>:30:7: `b` was lent to another green thread at <fragment>:29:9, so what it holds is frozen: passing it to `poke`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-an-async-callee-that-keeps-it -->
<!-- unsupported-targets: wasm32-wasi -->
An `async` call is the same door as a direct one: the coroutine runs on this green thread, but what it KEEPS
outlives the await, and the freeze asks about storage rather than about who runs. Whole-program, so pinned on
the native lanes.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box) returns Integer
	Scheduler.yield()
	xs.push(item)
	return xs.count() as Integer
end 'stash'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
	let b = Box.create()
	h.take(b)
	let pending = async stash(xs, item: b)
	let kept = await pending
	return kept as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:35:38: `b` was lent to another green thread at <fragment>:34:9, so what it holds is frozen: passing it to `stash`, which keeps it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-an-async-callee-that-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
The write half of the same door, one `async` out: `poke` writes its parameter's field, which a `let` may not
fill at all (E3019), and the lend freezes it on top of that. Whole-program, so pinned on the native lanes.
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

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 9
	return p.n
end 'poke'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.take(b)
	let pending = async poke(b)
	let poked = await pending
	return poked as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:32:22: cannot pass 'b' to function that mutates parameter 'p' (in main)
error E3160: <fragment>:32:27: `b` was lent to another green thread at <fragment>:31:9, so what it holds is frozen: passing it to `poke`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-callee-that-writes-it-through-async -->
<!-- unsupported-targets: wasm32-wasi -->
A callee that writes what it was handed refuses a `let` however deep the write is: `relay` writes nothing
itself and spawns `replace`, which does. The summary closes over the spawn, so `relay` writes its parameter —
E3019 at the call — and the lend freezes it on top of that. Whole-program, so pinned on the native lanes.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function replace(p Box) returns Integer
	Scheduler.yield()
	p.n = 9
	return p.n
end 'replace'

function relay(q Box) returns Integer
	let pending = async replace(q)
	return await pending
end 'relay'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create(1)
	h.take(b)
	return relay(b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:37:9: cannot pass 'b' to function that mutates parameter 'q' (in main)
error E3160: <fragment>:37:15: `b` was lent to another green thread at <fragment>:36:9, so what it holds is frozen: passing it to `relay`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-an-async-keeper-one-frame-down -->
<!-- unsupported-targets: wasm32-wasi -->
The KEEPS twin of the write door one frame down: `relay` keeps nothing itself and spawns `stash`, which pushes
what it is handed into a container that outlives the await. Whole-program, so pinned on the native lanes.
```maxon
type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

typealias Boxes = Array with Box

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(b Box)
		self.seen = self.seen + b.n
	end 'take'
end 'Svc'

function stash(xs Boxes, item Box) returns Integer
	Scheduler.yield()
	xs.push(item)
	return xs.count() as Integer
end 'stash'

function relay(xs Boxes, q Box) returns Integer
	let pending = async stash(xs, item: q)
	return await pending
end 'relay'

function main() returns ExitCode
	let h = spawn Svc.create()
	var xs = Boxes.create()
	let b = Box.create(1)
	h.take(b)
	return relay(xs, q: b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:40:22: `b` was lent to another green thread at <fragment>:39:9, so what it holds is frozen: passing it to `relay`, which keeps it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-handler-may-not-keep-a-lent-parameter -->
<!-- unsupported-targets: wasm32-wasi -->
The receiving side is held to the same freeze, whole-program: `keep` pushes its parameter into the service's
state, so every send that LENDS to it is refused at the send, naming the handler's escape. `main` never reads
`b` again and the send still lends — every `let` local does. Whole-program, so pinned on the native lanes.
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
		return Self{kept: Boxes.create()}
	end 'create'

	export function keep(given Box)
		self.kept.push(given)
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.keep(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:27:9: `b` is lent to `Svc.keep` here, so what it holds is frozen, but the handler's parameter `given` escapes at <fragment>:20:18: storing it in a container would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-handler-may-not-keep-what-it-reads-out-of-a-lent-parameter -->
<!-- unsupported-targets: wasm32-wasi -->
What a handler reads OUT of a lent parameter is frozen with it: `p.inner` escapes into the state array, and
the refusal names `main`'s lent binding and the handler's parameter. Whole-program, so pinned on the native
lanes.
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
		return Self{kept: Boxes.create()}
	end 'create'

	export function keep(p Pair)
		self.kept.push(p.inner)
	end 'keep'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let pair = Pair.create(Box.create())
	h.keep(pair)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:35:9: `pair` is lent to `Svc.keep` here, so what it holds is frozen, but the handler's parameter `p` escapes at <fragment>:28:18: storing it in a container would let it be written. Send a `.clone()` instead, or bind `pair` with `var` so the send moves it
```

<!-- test: borrow.error.a-let-lent-to-a-handler-that-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
A handler that WRITES its parameter would write the sender's graph from another green thread, so a lending
send to it is E3019, as passing a `let` to a writing parameter is. The write is a FIELD write, which a
direct call accepts for a `let` (`SemanticCheck.checkImmutableArgToMutatingParam`) and a lending send may
not. E3019 is decided whole-program, so this is pinned on the native lanes.
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

	export function bump(target Box)
		target.n = target.n + 1
	end 'bump'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.bump(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:25:2: cannot pass 'b' to function that mutates parameter 'target' (in main)
```

<!-- test: borrow.error.a-handler-may-not-write-its-lent-parameter-through-a-self-writing-method -->
<!-- unsupported-targets: wasm32-wasi -->
A method that writes its own receiver writes the record it is called on, so a handler calling `bump` on its
parameter writes the sender's graph as surely as `target.n = …` does, and the lending send is E3019. Which
methods write their receiver is a whole-program fact, so this is pinned on the native lanes.
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

	export function poke(target Box)
		target.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Box.create()
	h.poke(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:29:2: cannot pass 'b' to function that mutates parameter 'target' (in main)
```

<!-- test: borrow.error.a-handler-may-not-write-a-record-inside-its-lent-parameter-through-a-self-writing-method -->
<!-- unsupported-targets: wasm32-wasi -->
The lent graph is everything the parameter reaches, so a self-writing method called on `target.inner` writes
the sender's graph one record down, and the lending send is E3019. Whole-program, so pinned on the native
lanes.
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

	export function poke(target Holder)
		target.inner.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.poke(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:37:2: cannot pass 'b' to function that mutates parameter 'target' (in main)
```

<!-- test: borrow.error.a-handler-may-not-write-a-record-inside-its-lent-parameter-one-frame-down-through-a-self-writing-method -->
<!-- unsupported-targets: wasm32-wasi -->
The handler writes nothing itself: `nudge` calls the self-writing method on what it was handed, and the
summary that says `nudge` writes its parameter reaches the handler, so the lending send is E3019.
Whole-program, so pinned on the native lanes.
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

	export function poke(target Holder)
		nudge(target)
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.poke(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:41:2: cannot pass 'b' to function that mutates parameter 'target' (in main)
```

<!-- test: borrow.error.a-lent-let-may-not-have-a-record-inside-it-written-through-a-self-writing-method -->
<!-- unsupported-targets: wasm32-wasi -->
The sender's side of the same door: `b.inner` is part of the graph the service reads, so calling a
self-writing method on it after the lend writes that graph, and the diagnostic names the root. Whole-program,
so pinned on the native lanes.
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

	export function look(target Holder)
		print("{target.inner.n}\n")
	end 'look'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.look(b)
	b.inner.bump()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:38:9: `b` was lent to another green thread at <fragment>:37:9, so what it holds is frozen: calling `bump`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-handler-may-not-push-into-a-container-a-call-hands-back-from-inside-its-lent-parameter -->
<!-- unsupported-targets: wasm32-wasi -->
`pick` hands back the array `target` holds, so pushing into it writes the sender's graph one record down, and
the lending send is E3019. The container is written by a built-in method rather than a declared one, which is
the receiver write a value that may lie within a parameter must carry. Whole-program, so pinned on the native
lanes.
```maxon
typealias Counts = Array with Integer

type Holder
	export var items as Counts

	static function create() returns Self
		return Self{items: Counts.create()}
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

	export function poke(target Holder)
		pick(target).push(1)
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.poke(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3019: <fragment>:31:2: cannot pass 'b' to function that mutates parameter 'target' (in main)
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-callee-that-writes-inside-it-through-a-witness -->
<!-- unsupported-targets: wasm32-wasi -->
`kick` dispatches `bump` through the `Bumpable` witness on each element the shelf holds, so `poke(s)` writes
the lent graph one record down. The element lies within the receiver, and the dispatch carries that edge to
every implementation. Whole-program, so pinned on the native lanes.
```maxon
interface Bumpable
	function bump()
end 'Bumpable'

type Box implements Bumpable
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Shelf uses T where T is Bumpable
	typealias Items = Array with T

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function count() returns Integer
		return self.items.count()
	end 'count'

	function kick()
		for item in self.items 'eachItem'
			item.bump()
		end 'eachItem'
	end 'kick'
end 'Shelf'

typealias BoxShelf = Shelf with Box

type Svc
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(target BoxShelf)
		self.seen = self.seen + target.count()
	end 'look'
end 'Svc'

function poke(s BoxShelf)
	s.kick()
end 'poke'

function main() returns ExitCode
	let h = spawn Svc.create()
	let s = BoxShelf.create()
	h.look(s)
	poke(s)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:60:7: `s` was lent to another green thread at <fragment>:59:9, so what it holds is frozen: passing it to `poke`, which writes it would let it be written. Send a `.clone()` instead, or bind `s` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-callee-that-spawns-a-write-inside-it -->
<!-- unsupported-targets: wasm32-wasi -->
`poke` hands `t.inner` to an `async` call whose target writes it, so the lent graph is written one record down
on the spawned green thread. The argument lies within the parameter, and the spawn carries that edge. Whole-
program, so pinned on the native lanes.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
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

	export function look(target Holder)
		self.seen = self.seen + target.inner.n
	end 'look'
end 'Svc'

function bumpIt(b Box) returns Integer
	Scheduler.yield()
	b.n = b.n + 1
	return b.n
end 'bumpIt'

function poke(t Holder) returns Integer
	let pending = async bumpIt(t.inner)
	return await pending
end 'poke'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.look(b)
	return poke(b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:45:14: `b` was lent to another green thread at <fragment>:44:9, so what it holds is frozen: passing it to `poke`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
```

<!-- test: borrow.error.a-lent-let-may-not-reach-a-callee-that-writes-what-a-call-hands-back-from-inside-it -->
<!-- unsupported-targets: wasm32-wasi -->
`pick` hands back the record `t` holds, and `poke` calls a self-writing method on that result, so the lent
graph is written one record down. Whole-program, so pinned on the native lanes.
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

	export function look(target Holder)
		self.seen = self.seen + target.inner.n
	end 'look'
end 'Svc'

function pick(t Holder) returns Box
	return t.inner
end 'pick'

function poke(t Holder)
	let c = pick(t)
	c.bump()
end 'poke'

function main() returns ExitCode
	let h = spawn Svc.create()
	let b = Holder.create()
	h.look(b)
	poke(b)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3160: <fragment>:47:7: `b` was lent to another green thread at <fragment>:46:9, so what it holds is frozen: passing it to `poke`, which writes it would let it be written. Send a `.clone()` instead, or bind `b` with `var` so the send moves it
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
error E3137: <fragment>:10:3: `Store.itself` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Store` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
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
error E3137: <fragment>:10:3: `Store.echo` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Store` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:15:10: the `spawn` that makes `Store` a service
```

<!-- test: error.two-services-that-await-each-other-are-refused -->
<!-- unsupported-targets: wasm32-wasi -->
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
⭐ **THE SPELLING THE PREVIOUS CASE'S MISMATCH IMPLIES** (§"`ServiceError`"). The fused pair is a
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
⭐⭐ **THE FIRST CROSS-GREEN-THREAD WAKE IN THE LANGUAGE.** `Outer`'s message awaits a reply from `Inner`, so a
green thread — not the main one — is the awaiter: the await parks `Outer`, `Inner` runs on its own stack, and
the reply that completes the cell readies `Outer`. The graph `Outer → Inner` is acyclic, which is what makes
this legal.
⚠ The peer's handle crosses as a message ARGUMENT rather than as `Outer`'s state, so the subject stays the
cross-green-thread wake and not the walk `deepmove.a-record-with-managed-fields-crosses` pins.
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- procs: 4 -->
⛔⛔ **THE HALF OF THE DEADLOCK RULE THAT IS A RUNTIME PROPERTY, BECAUSE E3139 CANNOT REACH IT.**
`ServiceCallCycleCheck` walks a graph of NAMED callees, so every case above it can be refused at compile
time. This program's `A → B` edge goes through a **closure value** — `callIndirect` calls whatever
function it was handed, and what it was handed is decided at the call site rather than at the callee's
name — so the roster records `B → A` alone, which is not a ring, and the program **compiles clean**. The
ring is real all the same: `main` awaits `A.kick`, `A.kick` awaits `B.work` through the closure, and
`B.work` awaits `A.ack` — which `A` cannot serve, because `A` is inside `kick`.

⇒ **the refusal a static check cannot make, the runtime must.** Every green thread in the program is
parked and none of them can ever become ready — the scheduler's `checkdead` (Go's, at `mput`):
`RuntimeAbort.schedulerDeadlock`, **exit 92**, silent on both streams. The answer is a diagnosis rather
than a hang: a wedged process tells you nothing and costs a 120 s harness timeout; a 92 names the condition.

⭐⭐ **THE DETECTOR DECIDES UNDER `__sched_lock`, SO THE ANSWER IS THE SAME AT EVERY PROCESSOR COUNT.** Every
blocked party in this program is a PARKED green thread — an await parks its caller, it does not run anything
on the caller's stack — so once the last runnable thread has parked, every machine goes idle. The last
machine to join the idle list runs `checkdead` in the same hold of the lock: no machine running, no timer,
child or read pending, and `main` not finished is exit 92 at once. `procs: 4` is what makes this case take
that decision on the fourth idle machine rather than the only one.

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
live reply cell — `W217`, exit 75; see `specs/await-any.md`.
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

<!-- test: a-reply-over-a-declared-type-stores-in-a-promise-naming-it -->
⭐ **A REPLY'S RESULT MAY BE A DECLARED TYPE, AND THE STORAGE TYPE THAT NAMES ONE IS THE SAME INSTANCE THE
SEND MINTS.** `Promise with (T, E)` is interned on its base and its ARGUMENTS, and a declared type reached
the two roads under two different tags — `structRef` off the message's `returns` clause, `named` off the
type argument the author wrote — which mangle to one symbol. Interned as two, a program was refused as a
duplicate definition of its own promise. Every `Promise with (Integer, …)` case above is blind to it: a
scalar argument carries no name, so it carries no second tag.
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

	export function bumped(by Integer) returns Tally
		self.count = self.count + by
		return Tally.create(self.count)
	end 'bumped'
end 'Counter'

typealias TallyReply = Promise with (Tally, ServiceError)
typealias TallyReplyArray = Array with TallyReply

function main() returns ExitCode
	let h = spawn Counter.create()
	var ps = TallyReplyArray.create()
	ps.push(h.bumped(4))
	let p = try ps.remove(0) otherwise panic("the reply was pushed into slot 0")
	let tally = try await p otherwise return 70 as ExitCode
	return tally.n as ExitCode
end 'main'
```
```exitcode
4
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
	let v = try await h.peek() otherwise 0
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

<!-- test: error.a-generic-reply-that-is-the-services-own-string-state-is-refused-at-the-spawn -->
The reply IS the state, at a type the body cannot see: one body serves every instantiation, so the `spawn`
that fixes `T` is where the reply is judged. With `T` a `String`, the caller and the service's own green
thread would hold one string record between them.
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
```maxoncstderr
error E3137: <fragment>:15:10: `Box.peek` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Box` a service over a `String`, which is a value it would then share with the caller on another green thread. Return a `.clone()`, or return the scalars the caller needs
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

	static function create(first T) returns Self
		var held = Items.create()
		held.append(first)
		return Self{seen: held.count() as Count}
	end 'create'

	export function all() returns Items
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
error E2015: <fragment>:3:26: Unsupported: a typealias over 'identifier' (only `int(low to high)`, `float(low to high)`, `bits(n)` and `function(...)` are parsed; generic and bare-sized aliases arrive with the milestones that give them meaning)
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
A handle in a FIELD, which co-owns it. ⭐ **This case caught a wrong answer during its own implementation**:
the retain router read every handle as `structRef`-tagged after its companion's name, and a generic one is
`genericInstance`-tagged, so a co-owning store fell through to `__mm_incref` — which steps the box while the
release still steps a `handles` share, a `refs` share AND the box. The mailbox handle count reached zero
under a live handle, the service drained, and the next send answered `stopped`. **Exit 0 is the failure
here**, not a crash.
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
	var hs = HandleArray.create()
	hs.push(spawn Box.create(6 as Small))
	let h = try hs.get(0) otherwise return 1 as ExitCode
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
	let s = try await h.peek() otherwise ""
	return s.byteLength() as Whole
end 'readIt'

function main() returns ExitCode
	let h = spawn Box.create("ok")
	return readIt(h) as ExitCode
end 'main'
typealias Whole = int(i64.min to i64.max)
```
```maxoncstderr
error E3145: <fragment>:17:22: `Box.peek` replies at `Box`'s own type parameter `T`, and this handle stands at the bare `Box.handle` — the ONE handle companion every instantiation of `Box` shares — so it names no instantiation and the reply has no type. SPELL the instantiation: `Box.handle with <the type argument>` is a generic instance like any other, and a `typealias` over it carries the reply's type through a parameter, a struct field or an array element. Or send `peek` where the `spawn Box.create(…)` binding is, which carries the instantiation already
```

<!-- test: a-generic-handle-is-an-opaque-element-of-another-generic -->
⭐⭐ **THE RED GATE FOR THE FOURTH DECIDER.** A handle reaches a shared generic body as an OPAQUE element
here, so its retain is chosen from the layout descriptor's `retainFunc` rather than from the value's tag — a
fourth road, and one only the handle's `with` spelling opens. MEASURED with the protocol classifier blind to
a service handle: the element retained through `__mm_retain`, which steps the box, while the release still
stepped a `handles` share, a `refs` share AND the box; the mailbox handle count reached zero under a live
handle, the service drained, and the SECOND ask answered `stopped`. **This case answers 6 and answered 3**,
with a concrete-typed field unchanged in both directions as the control.
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
above pins the generic retain router; the monomorphic road into the same co-owning store is this case's.
`__mbox_handle_retain_box` increfs the handle box
inside its own Std body, which the Maxon-module usage scan cannot see, so a program whose ONLY incref comes
from a handle retain died at `resolveCallFixups: call to unknown function '__mm_incref'`. Nothing here
allocates but the handle, which is what makes the case discriminating.
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
MESSAGE gets one rule over (`error.void-message-sent-for-its-value`), and it must be a refusal because the
alternative measured is a COMPILER PANIC: `emitServiceShutdown` mints a reply cell for the `__shutdown` pill
under the name `<T>.shutdown`, and `serviceTypeOfMessage` then looks that name up on the message roster,
where the compiler's own pill is not and never was.
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
error E2015: <fragment>:18:14: Unsupported: the value of `Calc.shutdown()` — `shutdown` is the HANDLE's own method and not a message of `Calc`, so no declaration says what it hands back and an expression has nothing to bind. Write `<handle>.shutdown()` as a statement: the pill goes in behind everything already queued, and dropping the last handle starts the same drain
```

<!-- test: a-spawn-inside-a-generic-body-over-an-inferred-instance -->
⭐ **THE OTHER HALF OF `error.a-spawn-inside-a-generic-body-is-refused`, AND THE HALF THAT MUST WORK.** That
case refuses a `spawn` whose type arguments fix to the enclosing declaration's own type parameters, which
name no layout. Here they fix to a CONCRETE type, so there is nothing to refuse and the service is ordinary.
⚠ **The instantiation is INFERRED** (`Outer.create(1 as Whole)`) rather than spelled by a `typealias`, and
that is the whole discrimination: the same program with `typealias WholeOuter = Outer with Whole` compiles,
and this one PANICKED at `forwardCallerLayout: caller 'main' has no layout descriptor to forward to
'Outer.go'` — whose own sentence says the parser's transitive reservation should make it unreachable.
Removing the `spawn` also compiles, so it takes both to reach it.
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
The third corner of the enclosing-generic family, and the one that PANICKED. `error.a-spawn-inside-a-generic-body-is-refused`
covers arguments fixed to the enclosing declaration's type parameters;
`a-spawn-inside-a-generic-body-over-an-inferred-instance` covers arguments fixed to a concrete type. Here
the enclosing factory fixes **nothing at all**, so `Outer` stands at its bare base and `main` has no
descriptor to forward — which the lowering is right to refuse and wrong to reach. The cure is that the
enclosing body no longer RESERVES a descriptor slot no call site could ever fill.
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

<!-- test: error.a-spawn-of-an-overloaded-generic-factory-is-refused -->
A `spawn` reads its type arguments off the factory's own argument list, and an OVERLOADED factory has no
single list to read: which overload the arguments resolve to is settled after the point where the
instantiation must already be known. Refused rather than guessed — reading the first declaration would fix
`T` from a parameter the resolved overload does not have, and the instance, its layout and its destructor
would all be the wrong ones with no diagnostic.
```maxon
typealias Whole = int(i64.min to i64.max)

type Box uses T
	export var n as Whole

	static function create(x T) returns Self
		return Self{n: 1}
	end 'create'

	static function create(a Whole, x T) returns Self
		return Self{n: 2}
	end 'create'

	export function peek() returns Whole
		return self.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	let h = spawn Box.create(7 as Whole, x: 9 as Whole)
	return try await h.peek() otherwise 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:20: Unsupported: `spawn Box.create(…)` — `Box` is generic and `create` is declared 2 times, so its type arguments cannot be read off the call: which overload the arguments resolve to is settled after this point, and the parameter list they must be matched against is that overload's. Give the factories distinct names, or spawn a type whose factory is declared once
```

<!-- test: error.a-value-sent-through-a-parameter-is-refused -->
⭐⭐ **THE `String` HALF OF `error.a-borrowed-parameter-may-not-be-sent`, AND THE CASE THAT MADE THE RULE
UNIFORM.** `buf` arrived as a borrowed parameter and the caller still holds it — the identical fact the
struct case is refused for, at the identical position, in the identical sentence. It compiled here until
2026-09-04, promoting `buf` to a fresh copy: sound, and an allocation the author never wrote, chosen by
whether the value's TYPE had a cheap clone rather than by whether a second owner exists.

⚠ **THE CURE THE DIAGNOSTIC TEACHES IS THE COPY IT USED TO MAKE.** `h.keep(buf.clone())` compiles and does
exactly what the promotion did — at a site the reader can see, which is what an explicit transfer rule owes
them.

⚠ A bare LITERAL is not in this class and is still promoted: it has no owner to be a second of. That is
`a-string-argument-moves-into-the-service`'s third send, and it is why the test here is PROVENANCE and not
type.
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
error E3138: <fragment>:16:9: argument `s` of the message `Store.keep` is BORROWED — read out of a field, an element or a parameter — so this frame does not own it. A send moves this value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands (only a `let` local that solely owns its graph is lent instead). Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
```

<!-- test: a-borrowed-parameter-may-be-sent-as-a-clone -->
⚠ **NOTHING IN THIS CASE MAY REST ON WHICH GREEN THREAD REACHES `stdout` FIRST.** Its subject is OWNERSHIP,
so `main` is the only writer and the awaited `report` is what puts the service's `keep` before the line that
reads it back — a CAUSAL order rather than a timed one.

⭐⭐ **THE CURE THE REFUSALS NAME FIRST, AND IT DID NOT WORK UNTIL THE FRESH-RETURN CLAIM CLOSED OVER A
HOP.** `String.clone`'s body is `return sliceBytes(…)` — a call to a function that IS fresh by
`Parser.noteFreshReturnShape`'s record-literal criterion — so the claim died one frame short and
`returnsFreshValue` answered `false`. MEASURED before `ProgramSignatures.closeFreshReturnForwards`:
`h.keep(buf.clone())` and `let c = buf.clone()` + `h.keep(c)` were BOTH E3138, on a program whose only fault
was following the diagnostic's own advice.

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
	h.keep(buf.clone())
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

	export function keep(p Payload)
		self.n = self.n + p.count()
	end 'keep'

	export function total() returns Integer
		return n
	end 'total'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	h.keep(Payload.create())
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

⛔⛔ **`.clone()` DID NOT WORK AT ANY SEND UNTIL 2026-09-04, FOR ANY SHAPE — the refusal named a spelling
the next diagnostic also refused.** `returnsFreshValue` answered `false` for `String.clone`, whose body is
`return sliceBytes(…)`: a call to a function that IS fresh by `noteFreshReturnShape`'s criterion, ONE HOP
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
	h.keep("{buf}")
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- procs: 4 -->
⭐⭐ **W226's SHAPE, COMMITTED — A SERVICE SHUT DOWN WHILE A COROUTINE ITS HANDLER STARTED IS PARKED, AND THE
PROOF THAT THE LEAK W226 PREDICTED IS NOT THERE.** The `SV1` review that opened `W226` could not measure
whether a coroutine owned by a service green thread is STRANDED when its owner is reclaimed: a coroutine runs
only on its owner's strand, so the prediction was that if `<T>.__loop` exits with one still in flight, nothing
runs it and nothing reclaims it — and a stranded record never reaches the exit gate, which is why the row says
the shape *"is invisible to every gate the suite has"*.

`fire` builds the shape in three steps, and each one is load-bearing:

- **`Scheduler.yield()` after the `async`** hands the strand to the coroutine, which runs until its `sleep` parks
  it on a timer. The handler then reads `probe.started` and a peek of `0`, so `parked=8` says every one of the
  eight coroutines had started and not finished when its handler returned.
- **The promise is kept in the service's state**, so the one thing that drops it is the shutdown's state drop
  in `<T>.__loop`'s teardown, with the coroutine still parked: its ten-second timer is far longer than the
  program runs, so the coroutine cannot finish before the shutdown reaches it.
- **The coroutine carries a managed argument**, the `Probe`, which only the coroutine's reclaim releases.

⛔ **A PROMISE DISCARDED AT ITS OWN STATEMENT BUILDS NONE OF THIS.** `let p = async slowWork(v)` followed by
`_ = p` renounces the coroutine before it ever runs (`async-promise-drop.parked-timer-drop-cancel`'s ⚠), so
the drop takes the queued arm and the shutdown finds nothing in flight. MEASURED with a `Probe` in that
shape: `started=0` at one processor and at four, with `main` sleeping 50 ms before asking — and that shape
stays GREEN under the sabotage below.

✅ **SABOTAGE-VERIFIED, AND THE `Probe` IS WHAT LETS THIS CASE SEE IT.** With the runner's half removed from
`__gt_promise_drop`'s parked arm, the coroutine is never reclaimed and neither is its service's green thread,
whose strand reference it still holds; this case exits **101** at one, four and sixteen processors, because the
`Probe` is released only by that reclaim. The exit gate proper cannot see the loss — the drop's consumer half
has already debited `__gt_live_count` — so the same program without the `Probe` argument exits 42 under the
sabotage, with 18 records carved at one processor where a sound runtime carves 5.

⭐ **SO `W226` WAS MEASURED AGAINST THE SCHEDULER'S RECORD-CARVE COUNT, AND THE ANSWER IS NO.**
`scripts/multicore-stress/service-async-strand-torture.maxon` runs this shape in batches of twenty services.
A runtime that reclaims every record carves no more than two batches' records plus fewer than 64 on each
other processor's free list, however many rounds run, and the torture runs until one record lost per round
would carry `__Builtins.schedGtRecordsCarved()` to twice that bound. Every round reaches the shape (`parked=`
equals the rounds) and the count stays under the bound, three runs each: **61 records carved over 260 rounds
against a bound of 121 at `MAXON_MAX_PROCS=1`, 208–214 over 620 against 310 at 4, and 621–731 over 2,140
against 1,066 at 16** — where the sabotaged runtime above carves 621, 1,475 and 4,838 and exits 101. The
CONTROL, the same batches with the handler `await`ing its coroutine so nothing is in flight at the shutdown,
stays under the bound too.

What reclaims the coroutine is the drop, not a sweep of the strand: the shutdown's state drop reaches the
parked coroutine's promise, `__gt_promise_drop` takes it out of the timer store and performs both halves of
its teardown. That runner half is also what releases the coroutine's strand reference (`GtOffStrandRefs`);
without it the owner's record outlives `<T>.__loop` for ever, which is the second record the sabotage loses
per round.

⇒ **the case stays as the proof.** It pins that a service may be shut down with `async` work in flight and
the process still terminates cleanly — no 101, no 75, no hang — which is the property `W226` was really
asking about.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

// Far longer than the program runs: the shutdown's drop cancels the timer, so nothing ever waits it out.
let WorkSleepMs = 10000

type Probe
	export var started as bool

	static function create() returns Self
		return Self{started: false}
	end 'create'
end 'Probe'

function slowWork(n Integer, probe Probe) returns Integer
	probe.started = true
	sleep(WorkSleepMs)
	return n * 2
end 'slowWork'

type Worker
	var pending as IntPromiseArray
	var probe as Probe

	static function create() returns Self
		return Self{pending: IntPromiseArray.create(), probe: Probe.create()}
	end 'create'

	// The yield hands the strand to the coroutine, which runs until its sleep parks it. The promise then lives
	// in the service's state, so the shutdown's state drop is the one thing that can reach it.
	export function fire(v Integer) returns Integer
		let p = async slowWork(v, probe: self.probe)
		Scheduler.yield()
		let parked = self.probe.started and __Builtins.gtIsComplete(p.inner) == 0
		self.pending.push(p)
		return 1 if parked else 0
	end 'fire'
end 'Worker'

function main() returns ExitCode
	var parked = 0

	for round in 1 to 8 'rounds'
		let w = spawn Worker.create()
		parked = parked + (try await w.fire(round) otherwise 0)
	end 'rounds'

	print("parked={parked}\n")
	return 42 as ExitCode
end 'main'
```
```exitcode
42
```
```stdout
parked=8
```

<!-- test: a-service-that-keeps-a-coroutine-across-a-reply -->
<!-- procs: 1 -->
⭐⭐ **A SERVICE THAT OWNS AN UNFINISHED COROUTINE PARKS ON ITS MAILBOX LIKE ANY OTHER SERVICE.** `start`
leaves `async work()` in the service's own state and replies, so between that reply and the next message the
service waits on its mailbox still owning a coroutine that has not run. `main` then awaits `finish`.

⛔ **A WAIT PARKS, AND THIS SHAPE IS WHY IT MAY DO NOTHING ELSE.** A mailbox wait that ran other green
threads nested on the waiter's stack would run `main` on top of the service — and `main`, awaiting the
`finish` reply only that service can send, would be waiting on the frame it is standing on: MEASURED on such
a runtime, **exit 92** (`schedulerDeadlock`) at one processor and at four. The service parks onto its
machine's scheduler context instead, `main` runs on its own stack, and the program answers `1 + 1`.
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
		return Self{pending: IntPromiseArray.create()}
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
```exitcode
2
```

<!-- test: a-service-that-keeps-a-coroutine-across-a-reply.on-four-processors -->
<!-- procs: 4 -->
The same program on four processors, where the service and `main` may run on different machines — so the
nesting cannot be read as an artifact of one machine running everything. It deadlocked here too.
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
		return Self{pending: IntPromiseArray.create()}
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
```exitcode
2
```

<!-- test: main-is-never-run-on-top-of-a-service-it-awaits -->
<!-- procs: 1 -->
⛔⛔ **NOTHING RUNS ON TOP OF A PARKED GREEN THREAD, SO `main` MAY WAKE WHILE THE SERVICE IT AWAITS IS
ITSELF WAITING.** `Middle.go` awaits `Slow.get`, which parks `Middle`. `main`'s `sleep(1)` ends while
`Middle` is parked waiting out `Slow`'s `sleep(20)`, and `main` is readied like any other green thread and
runs on its own stack, switched in from a machine's scheduler context — never on `Middle`'s. An await that
ran whatever it took off a run queue on the awaiter's own stack would run `main` on top of `Middle`, and
`main`'s `await r1` — a reply only `Middle` can send — would wait on the frame it stands on: MEASURED on such
a runtime, **exit 92** (`schedulerDeadlock`) on one processor. Here both replies arrive.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function get() returns Integer
		sleep(20)
		return 7
	end 'get'
end 'Slow'

type Middle
	var s as Slow.handle

	static function create() returns Self
		return Self{s: spawn Slow.create()}
	end 'create'

	export function go() returns Integer
		let v = try await self.s.get() otherwise 0
		return v
	end 'go'
end 'Middle'

function main() returns ExitCode
	let m = spawn Middle.create()
	let r1 = m.go()
	sleep(1)
	let r2 = m.go()
	let a = try await r1 otherwise 0
	let b = try await r2 otherwise 0
	print("a={a} b={b}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
a=7 b=7
```

<!-- test: an-unsent-message-that-calls-the-library-still-compiles -->
**A MESSAGE NOBODY SENDS IS STILL REACHABLE, BECAUSE THE SERVICE LOOP DISPATCHES IT.** `spawn` makes the
synthesized `Reader.__loop` live, and that loop's switch names every message the type exports, so `read` is
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
	return 7 as ExitCode
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
	return 7 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-message-that-spawns-a-service-keeps-that-services-unsent-messages-alive -->
The edge is transitive. `Outer.start` is sent, and its body spawns `Inner`, so `Inner.__loop` is live and every
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

	export function measure(n Tally) returns Report
		return build(n)
	end 'measure'

	function build(n Tally) returns Report
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

	export function measure(n Tally) returns Report
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

	export function measure() returns Report
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
error E3137: <fragment>:20:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:29:15: the `spawn` that makes `Worker` a service
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

	export function measure() returns Report
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
error E3137: <fragment>:24:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:29:15: the `spawn` that makes `Worker` a service
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
		return Self{history: ReportArray.create()}
	end 'create'

	export function measure(n Tally) returns Report
		return build(n)
	end 'measure'

	function build(n Tally) returns Report
		let report = Report.create(n * 2)
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
error E3137: <fragment>:21:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:32:15: the `spawn` that makes `Worker` a service
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

	export function measure(n Tally) returns Report
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
error E3137: <fragment>:24:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:35:15: the `spawn` that makes `Worker` a service
```

<!-- test: error.spawn-factory-delegating-to-a-factory-that-returns-its-parameter-refused -->
`create` delegates to `adopt`, which builds nothing: it returns the `Worker` it was handed, which `main`
still holds.
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

	static function create(seed Worker) returns Self
		return adopt(seed)
	end 'create'

	export function measure(n Tally) returns Tally
		return n * self.scale
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let seed = Worker.make(2)
	let worker = spawn Worker.create(seed)
	let total = try await worker.measure(21) otherwise panic("the worker is running")
	print("{total} {seed.scale}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:26:28: the state `spawn Worker.create(…)` would start the service with cannot be proven to have exactly one owner: this frame has either taken a SECOND reference to it — a container push, a closure capture, a consuming call — or received it across a frame boundary whose far side may still hold one (a parameter, or a call whose callee the compiler cannot prove returns a fresh record). A send moves this value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands (only a `let` local that solely owns its graph is lent instead). Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
```

<!-- test: error.reply-forwarded-through-a-local-named-like-a-type-refused -->
Inside `build`, `Report` is a local holding the service's own record, so `Report.echo(1)` calls the INSTANCE
`echo` on it — which returns that record — and not the static `echo` that builds one.
```maxon
typealias Tally = int(0 to u64.max)

type Report
	export var total as Tally

	static function echo(amount Tally) returns Self
		return Self{total: amount}
	end 'echo'

	function echo(amount Tally) returns Report
		self.total = self.total + amount
		return self
	end 'echo'
end 'Report'

type Worker
	var report as Report

	static function create() returns Self
		return Self{report: Report.echo(41)}
	end 'create'

	export function measure() returns Report
		return build()
	end 'measure'

	function build() returns Report
		let Report = self.report
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
error E3137: <fragment>:25:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:35:15: the `spawn` that makes `Worker` a service
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

	export function measure(n Tally) returns Clock
		return build(n)
	end 'measure'

	function build(n Tally) returns Clock
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

<!-- test: services.an-or-arm-matches-a-reply-error-merged-with-stopped -->
A reply's error is the message's own error set merged with `ServiceError.stopped`, and a `match` over it takes
the same `or` arms a match over any error enum does.
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
				stopped then print("no answer\n")
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

	static function create(flags Flags) returns Self
		return Self{flags: flags}
	end 'create'
end 'Cell'

type Worker
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function report() returns Cell
		self.calls = self.calls + 1
		var flags = Flags.create()
		flags.push(false)
		flags.push(true)
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
A reply may hold a message parameter: the request box releases its reference to the argument before the
reply is handed to the caller, so the caller becomes the only owner of everything the reply reaches.
```maxon
typealias Flags = Array with bool
typealias Tally = int(0 to 1000)

type Cell
	export var flags as Flags

	static function create(flags Flags) returns Self
		return Self{flags: flags}
	end 'create'
end 'Cell'

type Worker
	var calls as Tally

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function wrap(flags Flags) returns Cell
		self.calls = self.calls + 1
		return Cell.create(flags)
	end 'wrap'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	var flags = Flags.create()
	flags.push(true)
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
	var flags = Flags.create()
	flags.push(true)
	let h = spawn Box.create(flags)
	let got = try await h.peek() otherwise panic("the box is running")
	print("{got.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3137: <fragment>:19:10: `Box.peek` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Box` a service over a `Flags`, which is a value it would then share with the caller on another green thread. Return a `.clone()`, or return the scalars the caller needs
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
	var items as Holders

	static function create(side Integer) returns Self
		var items = Holders.create()
		items.append(Holder.create(Square.create(side)))
		return Self{items: items}
	end 'create'

	export function drain() returns Holders
		var out = Holders.create()

		if let item = try self.items.removeFirst() 'held'
			out.append(item)
		end 'held'

		return out
	end 'drain'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create(2)
	let items = try await h.drain() otherwise panic("the maker is running")
	let holder = try items.first() otherwise panic("the maker held one item")
	print("{items.count()} {holder.area()}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
1 4
```

<!-- test: error.a-generic-reply-whose-instantiation-reaches-an-os-handle-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A reply typed `Array with T` is fresh in the handler's generic body and walkable there. The instantiation makes
its elements `TcpListener`s, whose socket handle no per-type walk can prove sole — so the `spawn` that fixes
`T` refuses it (E3138), rather than leaving a reply the loop cannot walk. On wasm32-wasi the socket's E3104 is
reported first, so the case is pinned on the native lanes.
```maxon
typealias Integer = int(i64.min to i64.max)

type Maker uses T
	export typealias Items = Array with T
	var seen as Integer

	static function create(seed T) returns Self
		var first = Items.create()
		first.push(seed)
		return Self{seen: first.count()}
	end 'create'

	export function make() returns Items
		self.seen = self.seen + 1
		return Items.create()
	end 'make'
end 'Maker'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let h = spawn Maker.create(listener)
	let items = try await h.make() otherwise panic("the maker is running")
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:22:10: the reply of the message `Maker.make` is a `Array_TcpListener` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance. A send hands the record over WHOLE, and this compiler walks the graph below it at run time, immediately before the send — but it can only walk a graph whose every type has a per-type cascade, and this one does not. Send the scalars the value is built from, or keep it on this side and send what the service needs of it
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

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let report = Report.create(n * self.scale)
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
		return Self{total: 0, lines: LineArray.create()}
	end 'create'
end 'Report'

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		var report = Report.create()
		report.lines.push("scaled")
		report.total = n * self.scale
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
		return Self{history: ReportArray.create()}
	end 'create'

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let report = Report.create(n * 2)
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
error E3137: <fragment>:21:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:32:15: the `spawn` that makes `Worker` a service
```

<!-- test: error.spawn-factory-returning-a-local-a-function-stored-refused -->
`create` builds its state in a local and hands it to `remember`, which keeps it in a module-level registry,
so the state the `spawn` would move has a second owner.
```maxon
typealias Tally = int(0 to u64.max)
typealias WorkerArray = Array with Worker

var registry = WorkerArray.create()

function remember(worker Worker)
	registry.push(worker)
end 'remember'

type Worker
	var scale as Tally

	static function create(scale Tally) returns Self
		let worker = Self{scale: scale}
		remember(worker)
		return worker
	end 'create'

	export function measure(n Tally) returns Tally
		return n * self.scale
	end 'measure'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create(2)
	let total = try await worker.measure(21) otherwise panic("the worker is running")
	print("{total} {registry.count()}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:26:28: the state `spawn Worker.create(…)` would start the service with cannot be proven to have exactly one owner: this frame has either taken a SECOND reference to it — a container push, a closure capture, a consuming call — or received it across a frame boundary whose far side may still hold one (a parameter, or a call whose callee the compiler cannot prove returns a fresh record). A send moves this value: the service becomes its one owner and this frame gives up the reference it held, and a box one green thread holds is counted plainly — so a value with a second owner would put one box into two green threads' hands (only a `let` local that solely owns its graph is lent instead). Send a `.clone()`, or build the value at the send: an INTERPOLATION over it is a record nothing else can name
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

	export function measure(n Tally) returns Report
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
error E3137: <fragment>:20:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:32:15: the `spawn` that makes `Worker` a service
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
		return Self{history: ReportArray.create()}
	end 'create'

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let report = Report.create(n * 2)
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
error E3137: <fragment>:25:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:36:15: the `spawn` that makes `Worker` a service
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

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let first = Report.create(n)
		let report = Report.create(n * 2)
		self.ranks = [first: 1, report: 2]
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
error E3137: <fragment>:29:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:41:15: the `spawn` that makes `Worker` a service
```

<!-- test: error.reply-a-sibling-returns-a-global-a-match-arm-local-shadows-refused -->
The `let report` in the first arm is that arm's alone, so the `return report` in the `default` arm hands back
the module-level `report` — a record the program still holds.
```maxon
typealias Tally = int(0 to 1000000)

type Report
	export var total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'
end 'Report'

let report = Report.create(7)

type Worker
	var scale as Tally

	static function create() returns Self
		return Self{scale: 2}
	end 'create'

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		match n 'pick'
			0 then let report = Report.create(n)
			default then return report
		end 'pick'

		return Report.create(n * self.scale)
	end 'summarize'
end 'Worker'

function main() returns ExitCode
	let worker = spawn Worker.create()
	let got = try await worker.measure(21) otherwise panic("the worker is running")
	print("{got.total}\n")
	worker.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3137: <fragment>:22:3: `Worker.measure` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Worker` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:36:15: the `spawn` that makes `Worker` a service
```

<!-- test: services.a-reply-whose-local-shares-a-field-with-the-state-aborts -->
A field read through the returned local is admitted: only the local's own record is proved at compile time.
`summarize` stores `report.inner` in the service's `kept`, so the record below the reply has a second owner, and
the walk at the publish aborts with exit **96** before the caller can read it.
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

	export function measure(n Tally) returns Report
		return summarize(n)
	end 'measure'

	function summarize(n Tally) returns Report
		let report = Report.create(n)
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
```exitcode
96
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

	export function make(side Tally) returns Shape
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

<!-- test: services.a-lent-interface-argument-stays-readable-by-the-sender -->
A `let` binding held at an interface type is LENT, exactly as a `let` record is: the send walks it through its
witness and marks what the conformer reaches shared, and the sender keeps dispatching through it.
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

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Tally
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let shape = squareOf(3)
	h.measure(shape)
	let total = try await h.total() otherwise panic("the service is running")
	print("{total} {shape.area()}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
9 9
```

<!-- test: services.a-second-owner-inside-a-conformer-aborts -->
A value held at an interface type is walked through the conformer it holds, so a record it reaches that
another owner still names is found at the send: the list's cell is co-owned by `main`, and the move aborts
with exit **96** before the service can see it.
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

	export function take(s Shape)
		self.seen = s.area()
	end 'take'
end 'Svc'

function main() returns ExitCode
	var cells = Cells.create()
	var cell = Cell.create()
	cells.append(cell)
	let h = spawn Svc.create()
	var shape = crateOf(cells)
	h.take(shape)
	return cell.n as ExitCode
end 'main'
```
```exitcode
96
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

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'

	export function total() returns Tally
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	var shape = labelOf(1234)
	h.measure(shape)
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

	export function make(width Tally) returns Shape
		self.made = self.made + 1
		return Label.create("w{width}")
	end 'make'

	export function count() returns Tally
		return self.made
	end 'count'
end 'Factory'

function main() returns ExitCode
	let factory = spawn Factory.create()
	factory.make(1234)
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
<!-- unsupported-targets: wasm32-wasi -->
A message argument at an interface-typed parameter widens as a call argument does, so a value whose type does
not conform is refused. On wasm32-wasi the send's E3104 is reported first, so the case is pinned on the
native lanes.
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

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	h.measure(7 as Tally)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:12: argument type mismatch for 's' of the message 'measure': type 'Tally' does not implement interface 'Shape'
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
error E2015: <fragment>:37:20: Unsupported: a type argument inferred from a factory argument declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a slot standing at a type parameter is one machine word. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

<!-- test: services.an-interface-reply-from-a-stopped-service-answers-stopped -->
A reply held at an interface type from a service that has stopped answers `ServiceError.stopped` like any
other reply, and its error edge releases nothing. An interface-typed argument the stopped mailbox abandons
is released through its witness, so the conformer's heap `String` does not leak (exit 101).
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

	export function make(width Tally) returns Shape
		self.made = self.made + 1
		return Label.create("w{width}")
	end 'make'

	export function inspect(s Shape)
		self.made = self.made + s.area()
	end 'inspect'
end 'Factory'

function labelOf(width Tally) returns Shape
	return Label.create("w{width}")
end 'labelOf'

function main() returns ExitCode
	let factory = spawn Factory.create()
	factory.shutdown()
	factory.make(7)
	factory.inspect(labelOf(5))
	let shape = try await factory.make(1234) otherwise (e) 'gone'
		match e 'why'
			stopped then return 9 as ExitCode
		end 'why'
	end 'gone'
	return shape.area() as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: services.interface-arguments-each-take-their-own-payload-slot -->
Each interface-typed parameter of a message takes its witness half in the payload slot after its own, so a
scalar parameter between two of them still reads its own slot, and a lent argument and a moved one cross in
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

	export function combine(a Shape, scale Tally, b Shape) returns Tally
		self.calls = self.calls + 1
		return a.area() * scale + b.area()
	end 'combine'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	let lent = squareOf(2)
	let n = try await h.combine(lent, scale: 10, b: labelOf(123)) otherwise panic("the calc is running")
	print("{n} {lent.area()}\n")
	h.shutdown()
	return 0
end 'main'
```
```stdout
44 4
```

<!-- test: borrow.error.a-lent-interface-argument-to-a-handler-that-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
A handler that writes a lent interface-typed parameter through its witness would write the sender's graph
from another green thread, exactly as a handler writing a lent record would.
```maxon
typealias Tally = int(0 to u64.max)

interface Counter
	function bump()
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

	export function poke(c Counter)
		c.bump()
	end 'poke'
end 'Svc'

function main() returns ExitCode
	let h = spawn Svc.create()
	let c = counterOf()
	h.poke(c)
	return c.value() as ExitCode
end 'main'
```
```maxoncstderr
error E3019: <fragment>:44:2: cannot pass 'c' to function that mutates parameter 'c' (in main)
```

<!-- test: borrow.error.a-lent-interface-argument-may-not-be-kept-by-the-handler -->
<!-- unsupported-targets: wasm32-wasi -->
A handler that stores a lent interface-typed parameter in its state keeps the sender's graph, which the lend
freezes.
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

	export function keep(s Shape)
		self.last = s
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let h = spawn Keeper.create()
	let s = squareOf(2)
	h.keep(s)
	return s.area() as ExitCode
end 'main'
```
```maxoncstderr
error E3160: <fragment>:39:9: `s` is lent to `Keeper.keep` here, so what it holds is frozen, but the handler's parameter `s` escapes at <fragment>:32:15: storing it in a field or payload would let it be written. Send a `.clone()` instead, or bind `s` with `var` so the send moves it
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

<!-- test: services.a-conformer-no-walk-reaches-aborts-when-sent-at-its-interface-type -->
<!-- unsupported-targets: wasm32-wasi -->
A value held at an interface type is walked through whatever conformer it holds at run time, so a conformer
whose graph reaches an OS handle cannot be refused where it is sent: the send aborts with exit **96** before
the service can see it.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	var listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Port'

function portOf(listener TcpListener) returns Shape
	return Port.create(listener)
end 'portOf'

type Calc
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	var listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let h = spawn Calc.create()
	var shape = portOf(listener)
	h.measure(shape)
	return 0
end 'main'
```
```exitcode
96
```

<!-- test: error.a-conformer-no-walk-reaches-sent-at-its-own-type-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Where the conformer is known at the send, the refusal is made at compile time: the argument widens to the
interface at the request payload, and it is walked as the record it is.
```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	var listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
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

	export function measure(s Shape)
		self.count = self.count + s.area()
	end 'measure'
end 'Calc'

function main() returns ExitCode
	var listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let h = spawn Calc.create()
	h.measure(Port.create(listener))
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:35:12: argument `s` of the message `Calc.measure` is a `Port` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance. A send hands the record over WHOLE, and this compiler walks the graph below it at run time, immediately before the send — but it can only walk a graph whose every type has a per-type cascade, and this one does not. Send the scalars the value is built from, or keep it on this side and send what the service needs of it
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

	export function peek() returns Shape
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
error E3137: <fragment>:28:3: `Surveyor.peek` returns a value this frame does not solely own — `self`, something reached through it, or a message PARAMETER, which the request box still holds — and this `spawn` makes `Surveyor` a service. The caller would then hold a second reference to that box, on another green thread. Return a `.clone()`, or return the scalars the caller needs
note: <fragment>:33:17: the `spawn` that makes `Surveyor` a service
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

	static function create(settings Settings) returns Self
		return prepare(settings)
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

	export function keep(holder Holder) returns Tally
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

	static function create(settings Settings, label String) returns Self
		return try prepare(settings, label: label) otherwise panic("prepare refused")
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
`Holder` the argument was borrowed from is released before the state is walked, so the `Settings` it held
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

	static function create(settings Settings) returns Self
		return Self{settings: settings}
	end 'create'

	export function measure(n Tally) returns Tally
		return n * 2 if self.settings.doubled else n
	end 'measure'
end 'Surveyor'

function main() returns ExitCode
	let surveyor = spawn Surveyor.create(Holder.create(Settings.create(true)).settings)
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
field was borrowed from is released before the argument's graph is walked.
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

	export function keep(holder Holder) returns Tally
		self.kept = self.kept + 1
		return 42 if holder.settings.doubled else 0
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let total = try await keeper.keep(Holder.create(Holder.create(Settings.create(true)).settings)) otherwise panic("the keeper is running")
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
The release at a send reaches only what its own argument list built. A borrow read before the send keeps
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

	export function keep(holder Holder, label String) returns Tally
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

	static function create(settings Settings) returns Self
		return Self{settings: settings}
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
	let total = measureWith(spawn Surveyor.create(Holder.create(Settings.create(true)).settings), extra: Holder.create(Holder.create(Settings.create(false)).settings))
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
