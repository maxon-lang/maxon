---
feature: green-thread-globals
status: experimental
keywords: [spawn, services, globals, module-level, data-race, green-threads, handler, concurrency, MAXON_MAX_PROCS]
category: concurrency
---

# A service handler may not touch a module-level `var`

## Documentation

⚖ **A MODULE-LEVEL `var` IS ONE WORD, AND A SERVICE HANDLER IS A GREEN THREAD THE SCHEDULER MAY PUT ON
ANY OS THREAD.** Those two facts cannot both hold in a program that compiles, so one of them has to be
refused, and it is the ACCESS: **a message handler — and anything reachable from one — may neither WRITE
nor READ a module-level `var`. A module-level `let` stays fully legal**, and it is the whole of the escape
hatch: a word written once before any green thread exists has no second writer for anybody to race.

⚠ **THE RULE ARRIVED IN TWO RULINGS AND IS ONE RULE.** The write half was taken first, on the lost-update
measurement below; the read half was taken after it, on the measurement below that. They share a
reachability question, a cure and an error code — only the diagnostic's subject clause differs — because
splitting them would be one rule written down twice.

**THE MEASUREMENT THAT MOTIVATED IT, AND IT IS AN ARITHMETIC FAILURE RATHER THAN A CRASH.** A service
whose `export` handler did `done = done + 1` on a module-level `var`, driven to a fixed total of 1200
sends, run ten times at `MAXON_MAX_PROCS=16`:

| run | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| `done` | 1200 | 1200 | 1199 | 1199 | 1198 | 1199 | 1199 | 1200 | 1199 | 1200 |

**Five of ten runs lost an update, and every one of them exited 0.** `done = done + 1` is a load, an add
and a store; two Ms that interleave those six steps write each other's stale value back. Nothing
crashes, nothing leaks, no gate anywhere goes red — the program simply answers a number that is too
small, sometimes. ⇒ **the cure cannot be a runtime check, because there is no moment at which the runtime
could notice.** It has to be a compile error, and the write is the only place the compiler can stand.

**AND THE READ IS THE HALF WITH TEETH — A USE-AFTER-FREE, NOT A LOST UPDATE.** A write to a module `var`
RELEASES the record the slot was holding before it stores the new one, so a green thread that has already
loaded the pointer reads on into freed memory. Twelve services spinning on `label.count()` while `main` ran
`label = "bravo-{r}"` four thousand times — **interpolated**, so every store frees a real heap record rather
than an immortal `.rdata` one:

| processors | outcome |
|---|---|
| `MAXON_MAX_PROCS=1` | **8 of 8 clean**, `worst=0` |
| `MAXON_MAX_PROCS=16` | **20 of 20 exit 139**, several panicking inside `String.count` → `utf8DecodeAt` with *"Range check failed: value outside typealias 'Codepoint'"* — decoding a record freed under them |

⛔ **A CHEAPER PROBE ANSWERS CLEANLY, AND KNOWING THAT IS WHAT KEEPS THE MEASUREMENT HONEST.** A handler
that reads the global ONCE, with `main`'s single store on either side of it, exits normally every time — it
never holds the pointer across a store. The hazard needs the reader to be INSIDE the load while the writer
is freeing, which is exactly what the spin and the four thousand stores buy. A rule argued from the cheap
probe would have concluded there was nothing here.

⚠ **AND THE SAME PROGRAM IS PERFECTLY CORRECT AT ONE PROCESSOR**, which is why this rule arrives beside
the `DefaultMaxProcs` flip and not before it. Every green thread ran on the one M, so the six steps could
not interleave; `specs/sched-runqueue.md`'s ring-overflow and index-wrap cases said so in as many
words — *"a global counter is the only channel a service has in SV1, and it is sound here for a stated
reason"*. **That reason expires the moment the default is more than one P.** The refusal is what makes the
expiry visible at the source rather than in a tally that is occasionally short: all four of those cases
now tally in `self` and report through an awaited reply, and that file's own preamble records the
withdrawal.

### What is refused, and what is not

The rule is about **who writes**, not about what is written, and not about globals as such:

| shape | verdict |
|---|---|
| a handler assigns to a module-level `var` | **refused** — the lost update above |
| a handler READS a module-level `var` | **refused** — the use-after-free above; the writer need not be another handler, `main` will do |
| a function a handler CALLS touches a module-level `var` | **refused** — transitive through the call graph, exactly as a service's blocking edge is (`services.md`'s `cycle-through-a-free-function-is-refused`) |
| a handler writes `self.<field>` | **legal** — a service's fields are the service's, reached by one green thread, which is the whole point of putting state there |
| a handler READS a module-level `let` | **legal** — an immortal, never-written word has no second writer to race, and this is the escape hatch every configuration constant takes. A `let` record `__module_init` has to BUILD is marked shared before `main`, so the counts every handler steps on it are atomic |
| a handler — or a function one reaches — reads a `let` whose graph no share walk can mark (an OS handle) | **refused** (E3163) — nothing could make the counts the handlers step on it atomic |
| the same `let`, read only by functions no handler reaches | **legal** — only `main` counts it, so it stays unmarked |
| a global initializer reaches a `spawn` | **refused** (E3164) — the `let`s are marked only once the last initializer returns, so no other green thread may exist before then |
| a `let` initializer reaches a module-level `var` holding a record | **refused** (E3165), spawn or no spawn — a record the `var` owns has an owner no `let` holds, so no mark could make it shared |
| a `var` initializer may be handed a `let`'s record, or one holding it | **refused** (E3166), spawn or no spawn — the `var` would be that owner |
| a plain function, not reachable from any handler, touches a module-level `var` | **legal** — the program spawning a service somewhere else does not make `main`'s own bookkeeping concurrent |
| a handler CALLS A CLOSURE VALUE, and a function whose ADDRESS IS TAKEN assigns to a module-level `var` | **refused** — the target is chosen at run time, so no edge can be followed and every function the call could land on is treated as reachable |
| a handler reaches a WITNESS dispatch, and a function wearing the dispatched requirement's name assigns to a module-level `var` | **refused**, for the same reason — a witness table holds the members of a conformance, so every member wearing that name is treated as reachable |
| the same, but the assigning function's address is taken nowhere and it wears no name a dispatch the handler reaches is for | **legal** — no dispatch can reach it. Marking it anyway refused `stdlib/Log.maxon` in every service program that used an interface, and a closure in `main` in every service program that keeps a `Map` |

⛔⛔ **THAT LAST ROW IS THIS RULE'S REFUSING DIRECTION, AND IT IS THE EXACT OPPOSITE OF `services.md`'s
DEADLOCK RULE ON THE SAME EDGES.** `ServiceCallCycleCheck`'s header says an unknown callee *"contributes no
edge … that is the ACCEPTING direction, deliberately"*, and it is right to: a missed cycle costs a guarantee
for a program shape nobody writes, while a false refusal is unworkaroundable. Here a missed edge costs a
**silently wrong answer at run time** — the failure with no gate anywhere — so the same silence resolves the
other way. The diagnostic says which road it took rather than claiming a call path the author cannot find:
*"the message `X` dispatches through a closure or a witness whose target this compiler cannot name, so it may
land here"*.

⚠ **An unresolvable NAMED callee is not one of those.** A name no function in the program declares is a
runtime entry point — `__mm_retain`, `__write_stdout`, the `__mf_*` band — with no body that could touch a
module slot and no route back into user code. Counting those as unknowns would mark every handler that prints
or allocates as blind and refuse essentially every service program there is.

⚠ **AND EVERY REFUSAL CARRIES THE `spawn` AS A NOTE**, on `specs/services.md`'s standing requirement for a
whole-program service rule and for its reason: whether `Counter` is a service is decided by a `spawn` that may
be in another file entirely, so a diagnostic that named only the write would leave the reader with no way to
find out why an ordinary-looking assignment became illegal. The primary line is the WRITE — the one place the
program can be repaired — and the note answers the question that line raises.

⇒ the cure for a refused program is the same in both directions and is always available: **keep the value
in `self` and hand it back through a reply.** A reply is a `Promise` the awaiter owns, so the sum is accumulated by
one green thread from values each computed by one green thread, and the answer no longer depends on how
many processors serviced the work. `specs/sched-default-procs.md` is that shape run at three
processor counts for exactly one answer.

⚠ **NO CASE HERE IS RESTRICTED FOR NEEDING A `spawn`.** All four native lanes have the green thread's
context switch, and wasm32-wasi is refused with E3104 — which the harness REPORTS as a counted SKIP
naming the case, where a marker removes it from the run in silence.

⛔ **THE FOUR `error.*` CASES DO EXCLUDE wasm32-wasi, AND FOR A REASON THAT IS ABOUT THE DIAGNOSTIC AND
NOT ABOUT THE RULE.** E3143 is target-neutral and would be reached everywhere, but on wasm the same
program earns E3104 for `__svc_spawn` and `__mbox_send` FIRST, so the pinned stderr is not what the
compiler emits there. MEASURED with the exclusion lifted: `actual` was the E3104 pair, `expected` the
E3143 text. The exclusion expires the day wasm grows a service substrate — not the day the rule changes.

## Tests

<!-- test: error.a-handler-may-not-write-a-module-global -->
<!-- unsupported-targets: wasm32-wasi -->
**THE DIRECT SHAPE — the measurement above, reduced to the one statement that makes it possible.**
`Counter.add` is an `export` instance method of a type this program `spawn`s, so it is a MESSAGE, so it
runs on a green thread; `total = total + by` is the load/add/store that two Ms can interleave. The
refusal is anchored at the WRITE, which is where the program can be repaired; the `spawn` appears only as
the NOTE, because it is what makes the type a service and yet there is nothing to change there.

⚠ **THIS PROGRAM USED TO COMPILE AND EXIT 0**, which was not a passing case but the defect itself: the only
reason it looked harmless is that `DefaultMaxProcs` was still 1.
```maxon
var total = 0

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		total = total + by
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(5)
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:12:3: the message `Counter.add` writes the module-level `total` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. So `total = …` is a load, an add and a store that two of them can interleave, each writing the other's stale value back. Nothing traps when they do; the program answers a number that is too small. Keep it in a field of `self` and hand it back through a reply, or make `total` a `let`
note: <fragment>:17:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-function-called-by-a-handler-may-not-write-a-module-global -->
<!-- unsupported-targets: wasm32-wasi -->
**THE SAME REFUSAL ONE HOP OUT, AND IT IS THE ONE THAT DECIDES WHETHER THE RULE IS WORTH ANYTHING.** A
rule keyed on writes SYNTACTICALLY INSIDE a handler body refuses only the shape an author would have
spotted anyway; the race does not care which frame the store instruction sits in. `Counter.add` names no
global at all — it calls `record`, and `record` is what stores. The edge is transitive through the call
graph, exactly as a service's BLOCKING edge is in `services.md`'s
`cycle-through-a-free-function-is-refused`.

⚠ The refusal is anchored at `record`'s write, and the message is named as the route that reaches it —
`record` on its own is an ordinary function and an ordinary function may store to a global all it likes
(`a-plain-function-may-write-a-module-global` below is that case, run). It is the REACHABILITY that
refuses, so the site and the reason live at different lines and the message has to carry both.
```maxon
var total = 0

function record(by Integer)
	total = total + by
end 'record'

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Integer)
		record(by)
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(5)
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:5:2: `record` writes the module-level `total`, and the message `Counter.add` reaches it — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. So `total = …` is a load, an add and a store that two of them can interleave, each writing the other's stale value back. Nothing traps when they do; the program answers a number that is too small. Keep it in a field of `self` and hand it back through a reply, or make `total` a `let`
note: <fragment>:21:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-dispatch-the-compiler-cannot-follow-widens-the-rule -->
<!-- unsupported-targets: wasm32-wasi -->
⛔⛔ **THE REFUSING DIRECTION, RUN — AND IT IS THE ROW ABOVE THAT NOTHING ELSE IN THIS FILE CAN SEE.**
`a-plain-function-may-write-a-module-global` below is this program with ONE difference: there, `Counter.add`
calls nothing the walk cannot follow, and `bookKeeping`'s write is legal. Here `Counter.add` calls
`callIndirect`, which calls a **closure value** — a target chosen at the call site rather than named at the
callee — so the call graph records no edge out of it and no walk can say where control lands. Whether
`bookKeeping` is reachable from a message is therefore genuinely unknown, and this rule answers unknown with
**refused**.

⚠ **AND `bookKeeping`'s ADDRESS IS TAKEN, WHICH IS WHAT MAKES THE UNKNOWN GENUINE RATHER THAN ASSUMED.**
`main` hands it to `callIndirect` as a `Step` instead of calling it by name, so `bookKeeping` really is one
of the bodies a `Step` in this program can denote — and the indirect call in the handler's cone really could
be the one that runs it. The widening is narrowed to exactly that set: a function whose address is taken
nowhere and which satisfies no witness slot has no address in the image at all, so no dispatch can land on
it, and marking it anyway was a REFUSAL OF THE STDLIB (see `an-interface-in-a-handlers-cone-does-not-implicate-the-stdlib`).

⚠ **AND THE SENTENCE SAYS SO RATHER THAN CLAIMING A CALL PATH.** *"the message `Counter.add` dispatches
through a closure or a witness whose target this compiler cannot name, so it may land here"* — an author
sent looking for a call from `Counter.add` to `bookKeeping` would not find one, because there is none. It is
the reachability that is unknown, not established.

⭐ **THE PAIR IS THE EVIDENCE.** This case and `a-plain-function-may-write-a-module-global` differ by one
indirect call and by nothing else, and they answer opposite verdicts — which is what makes the widening a
measured behaviour rather than a claim in a comment. `services.md`'s
`a-blocking-cycle-through-an-indirect-call-aborts` is the same silence read the OTHER way one rule over: a
cycle through a closure is ACCEPTED at compile time (and aborts at run time), because a missed refusal there
costs a guarantee and here it costs a wrong answer nothing can notice.
```maxon
var ledger = 0

typealias Step = function(Integer) returns Integer

function bookKeeping(by Integer) returns Integer
	ledger = ledger + by
	return ledger
end 'bookKeeping'

// The indirection. It calls the function it was HANDED, so no walk keyed on a callee's NAME can say which
// body runs here — `services.md`'s `callIndirect`, doing the same job for the deadlock rule.
function callIndirect(f Step, n Integer) returns Integer
	return f(n)
end 'callIndirect'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		return callIndirect(function(n Integer) gives n * 2, n: by)
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	let l = callIndirect(bookKeeping, n: 1)
	print("n={n} l={l}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:7:2: `bookKeeping` writes the module-level `ledger`, and the message `Counter.add` dispatches through a closure or a witness whose target this compiler cannot name, so it may land here — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. So `ledger = …` is a load, an add and a store that two of them can interleave, each writing the other's stale value back. Nothing traps when they do; the program answers a number that is too small. Keep it in a field of `self` and hand it back through a reply, or make `ledger` a `let`
note: <fragment>:30:10: the `spawn` that makes `Counter` a service
```

<!-- test: error.a-handler-may-not-read-a-module-global -->
<!-- unsupported-targets: wasm32-wasi -->
⛔⛔ **THE READ HALF, AND THIS IS THE PROGRAM THE MEASUREMENT WAS TAKEN ON — VERBATIM.** `Reader.size`
names no global on the left of anything; it only *reads* `label`, four thousand times, while `main`
reassigns it four thousand times from another green thread. Under the write rule alone this compiled and
ran, and at `MAXON_MAX_PROCS=16` it **exited 139 twenty times out of twenty**, several of those panicking
inside `String.count` → `utf8DecodeAt` with *"Range check failed: value outside typealias 'Codepoint'"* —
`String.count` walking a record `main`'s store had already released. At one processor the identical binary
was **clean 8 times out of 8**.

⚠ **THE THREE THINGS THAT MAKE IT BITE ARE ALL DELIBERATE, AND A "SIMPLER" VERSION OF THIS CASE MEASURES
NOTHING.** The value is **interpolated** (`"bravo-{r}"`), so every store frees a real heap record — a bare
literal is immortal `.rdata` and frees nothing at all. The handler **spins**, so it is inside the load when
the store lands. And there are **twelve** readers against four thousand stores, so the window is hit rather
than merely existing. A handler that read the global once, between two stores, exits normally every time.

⚠ The anchor is the READ, exactly as the write cases anchor at the write: it is the line the author has to
change. The `spawn` rides as the note, because whether `Reader` is a service is decided elsewhere.
```maxon
// Does a handler READING a module-level managed `var` while `main` reassigns it produce a
// use-after-free, or merely a stale word? The store decrefs the record the slot held; an
// interpolated value is a fresh HEAP record each time (a bare literal would be immortal
// .rdata and free nothing, which would make this probe vacuous).
typealias ReaderHandleArray = Array with Reader.handle
typealias IntPromise = Promise with (Integer, ServiceError)
typealias IntPromiseArray = Array with IntPromise

var label = "alpha"

type Reader
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function size() returns Integer
		var odd = 0
		var i = 0
		while i < 4000 'spin'
			let c = label.count()
			if c < 5 or c > 40 'implausible'
				odd = c
			end 'implausible'
			i = i + 1
		end 'spin'
		return odd
	end 'size'
end 'Reader'

function main() returns ExitCode
	var hs = ReaderHandleArray.create()
	var i = 0
	while i < 12 'sp'
		hs.push(spawn Reader.create())
		i = i + 1
	end 'sp'

	var ps = IntPromiseArray.create()
	var k = 0
	while k < 12 'send'
		let h = try hs.get(k) otherwise panic("get")
		ps.push(h.size())
		k = k + 1
	end 'send'

	// Reassign while the readers are already running on worker Ms.
	var r = 0
	while r < 4000 'churn'
		label = "bravo-{r}"
		r = r + 1
	end 'churn'

	var worst = 0
	var a = 0
	while a < 12 'collect'
		let p = try ps.get(a) otherwise panic("get p")
		let v = try await p otherwise 0
		if v != 0 'sawOdd'
			worst = v
		end 'sawOdd'
		a = a + 1
	end 'collect'

	print("worst={worst}\n")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:23:12: the message `Reader.size` reads the module-level `label` — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. A reader gets whichever side of a concurrent write the scheduler happened to interleave — and when `label` holds a String, an array or a struct that is not a stale word but a FREED ONE, because the write RELEASES the record the slot was holding while the reader is still following the pointer it already loaded. Nothing traps at the store. Keep it in a field of `self` and hand it back through a reply, or make `label` a `let`
note: <fragment>:37:11: the `spawn` that makes `Reader` a service
```

<!-- test: error.a-function-a-handler-reaches-may-not-read-a-module-global -->
<!-- unsupported-targets: wasm32-wasi -->
**THE READ HALF TWO HOPS OUT, AND IT IS THE SHAPE A COPY-ON-WRITE ANCHOR TAKES.** `Counter.count` names no
global; it calls `entryCount`, which calls `holdsNothing`, and only that asks whether a column IS the shared
empty `noEntries` — an identity test, which READS the slot. A work function run from a handler is refused for
exactly this, however deep in its call graph the test sits: the anchor has to belong to the work, not to the
module, which is why the compiler's own parser holds its anchors per parser (`ParserAnchors`).
```maxon
var noEntries = IntArray.create()

function holdsNothing(column IntArray) returns bool
	return column is noEntries
end 'holdsNothing'

function entryCount(column IntArray) returns Integer
	if holdsNothing(column) 'shared'
		return 0
	end 'shared'

	return column.count()
end 'entryCount'

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function count(by Integer) returns Integer
		var column = IntArray.create()
		column.push(by)
		return entryCount(column)
	end 'count'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.count(5) otherwise 0
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
```
```maxoncstderr
error E3143: <fragment>:5:19: `holdsNothing` reads the module-level `noEntries`, and the message `Counter.count` reaches it — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. A reader gets whichever side of a concurrent write the scheduler happened to interleave — and when `noEntries` holds a String, an array or a struct that is not a stale word but a FREED ONE, because the write RELEASES the record the slot was holding while the reader is still following the pointer it already loaded. Nothing traps at the store. Keep it in a field of `self` and hand it back through a reply, or make `noEntries` a `let`
note: <fragment>:31:10: the `spawn` that makes `Counter` a service
```

<!-- test: a-handler-may-write-its-own-field -->
**THE OVER-REFUSAL GUARD THAT MATTERS MOST — the cure the two refusals above prescribe must itself
compile.** `self.count = self.count + by` is a store to a word owned by ONE green thread: the service's
own. There is no second writer at any processor count, which is the language guarantee the whole
plain-refcount design already rests on, so nothing here is refused and nothing needs to be.

The write is observed through an AWAITED REPLY rather than through a print inside the handler, deliberately:
a reply is what carries a value back to a party that can order it, and it is the one channel that stays
correct when the default is every processor. Two sends, then one await — so the exit code is the sum and a
lost send would read 3 or 4 rather than 7.
```maxon
type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + by
	end 'add'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	h.add(3)
	h.add(4)
	let n = try await h.total() otherwise 0
	print("count={n}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
count=7
```
```exitcode
7
```

<!-- test: a-handler-may-read-an-immutable-global -->
**THE RULE IS ABOUT THE WRITE, AND A `let` HAS NONE.** A module-level `let` is written once before any
green thread exists and never again, so every M that reads it reads the same word and no interleaving can
produce a different answer. A rule that refused module-level state as such — rather than the store to it —
would refuse this, and would take every configuration constant in every service program with it.
```maxon
let stride = 7

type Scaler
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function scale(by Integer) returns Integer
		return by * stride
	end 'scale'
end 'Scaler'

function main() returns ExitCode
	let h = spawn Scaler.create()
	let n = try await h.scale(6) otherwise 0
	print("scaled={n}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
scaled=42
```
```exitcode
42
```

<!-- test: every-handler-may-co-own-a-global-built-at-run-time -->
**A `let` WHOSE RECORD `__module_init` BUILDS IS READ BY EVERY THREAD TOO, AND ITS COUNT IS ONE WORD THEY ALL
STEP.** A `let` no image can hold — here a factory with a loop in it — is a counted record, and a handler that
puts it somewhere that outlives a statement takes an owner on it and gives the owner back. Eight handlers
doing that at once on different OS threads step one refcount, so the record is marked shared once it is built,
exactly as a lent graph is, and every count on it from then on is atomic. Unmarked, a lost increment frees the
roster under a reader and a lost decrement leaks it.
```maxon
type Roster
	export var names as NameArray

	static function create(count Integer) returns Self
		var names = NameArray.create()

		for i in 0 upto count 'fill'
			names.push("member {i}")
		end 'fill'

		return Self{names: names}
	end 'create'
end 'Roster'

let roster = Roster.create(4)

type Churner
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function churn(rounds Integer) returns Integer
		var held = 0

		for _ in 0 upto rounds 'eachRound'
			var kept = HeldNamesArray.create()
			kept.push(roster.names)
			held = held + kept.count()
		end 'eachRound'

		return held
	end 'churn'
end 'Churner'

function main() returns ExitCode
	var replies = ChurnReplyArray.create()

	for _ in 0 upto ChurnerCount 'eachChurner'
		let h = spawn Churner.create()
		replies.push(h.churn(RoundsPerChurner))
	end 'eachChurner'

	var held = 0

	for r in replies 'eachReply'
		held = held + (try await r otherwise 0)
	end 'eachReply'

	print("held={held} names={roster.names.count()}\n")
	return 0
end 'main'

let ChurnerCount = 8
let RoundsPerChurner = 200000

typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
typealias HeldNamesArray = Array with NameArray
typealias ChurnReplyArray = Array with Promise with (Integer, ServiceError)
```
```stdout
held=1600000 names=4
```
```exitcode
0
```

<!-- test: error.a-message-may-not-read-a-let-no-share-walk-can-mark -->
<!-- unsupported-targets: wasm32-wasi -->
**AND A `let` THE MARK CANNOT REACH IS REFUSED WHERE A MESSAGE READS IT.** An OS handle has a destructor and no
walk, so nothing can make the counts on a record holding one atomic. The refusal sits at the read the message
reaches, and the note is the declaration.
```maxon
type Listening
	export var listener as TcpListener

	static function open() returns Self
		let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("bind")
		return Self{listener: listener}
	end 'open'
end 'Listening'

let listening = Listening.open()

type Porter
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function port() returns Integer
		self.asked = self.asked + 1
		return listening.listener.port()
	end 'port'
end 'Porter'

function main() returns ExitCode
	let h = spawn Porter.create()
	let p = try await h.port() otherwise 0
	return (0 if p > 0 else 1) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3163: <fragment>:22:10: the message `Porter.port` reads the module-level `listening`, and it holds a `Listening` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance. A message runs on a green thread the scheduler may put on any OS thread, so the counts every message steps on `listening` must be atomic, and no share walk can mark it so. Keep it in a field of the service that reads it
note: <fragment>:11:5: `listening` is declared here
```

<!-- test: a-let-no-message-reads-may-hold-an-os-handle-beside-a-service -->
**THE SAME `let`, READ ONLY BY `main`, IS LEGAL.** No message reaches `listening`, so nothing but `main`'s green
thread counts it and it needs no mark.
```maxon
type Listening
	export var listener as TcpListener

	static function open() returns Self
		let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("bind")
		return Self{listener: listener}
	end 'open'
end 'Listening'

let listening = Listening.open()

type Doubler
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function double(n Integer) returns Integer
		self.asked = self.asked + 1
		return n * 2
	end 'double'
end 'Doubler'

function main() returns ExitCode
	let h = spawn Doubler.create()
	let d = try await h.double(21) otherwise 0
	let open = listening.listener.port() > 0
	print("d={d} open={open}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
d=42 open=true
```
```exitcode
0
```

<!-- test: lets-reaching-one-record-are-marked-through-one-visit-table -->
**ONE VISIT TABLE SERVES EVERY `let`.** `pair` holds `bag` twice and `bag`'s own slot holds it too, so the
record has three owners, all of them `let`s. Marked `let` by `let`, the second and third meetings would read
as owners no walk met; one table across every `let` counts all three and the program starts.
```maxon
type Bag
	export var n as Integer
	export var names as NameArray

	static function create() returns Self
		var names = NameArray.create()

		for i in 0 upto 3 'fill'
			names.push("name {i}")
		end 'fill'

		return Self{n: 7, names: names}
	end 'create'
end 'Bag'

type Pair
	export var left as Bag
	export var right as Bag

	static function around(bag Bag) returns Self
		return Self{left: bag, right: bag}
	end 'around'
end 'Pair'

let bag = Bag.create()
let pair = aroundBag()

function aroundBag() returns Pair
	return Pair.around(bag)
end 'aroundBag'

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return pair.right.n + bag.names.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
```
```stdout
n=10
```
```exitcode
0
```

<!-- test: error.a-spawn-a-global-initializer-reaches-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
**A GLOBAL INITIALIZER MAY NOT START A SERVICE.** `Box.start` spawns `Reader` while `box` is being built, before
`roster` is marked shared, so `Reader.read` could step `roster`'s plain count from another OS thread.
```maxon
type Roster
	export var names as NameArray

	static function create(count Integer) returns Self
		var names = NameArray.create()

		for i in 0 upto count 'fill'
			names.push("member {i}")
		end 'fill'

		return Self{names: names}
	end 'create'
end 'Roster'

let roster = Roster.create(4)

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return roster.names.count()
	end 'read'
end 'Reader'

type Box
	export var h as Reader.handle

	static function start() returns Self
		let h = spawn Reader.create()
		_ = h.read()
		return Self{h: h}
	end 'start'
end 'Box'

let box = Box.start()

function main() returns ExitCode
	let n = try await box.h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias NameArray = Array with String
```
```maxoncstderr
error E3164: <fragment>:35:11: `Box.start` spawns `Reader`, and a module-level global's initializer reaches it — initializers run before `main`, and the `let`s every message may read are marked shared only once the last one returns, so a service started here could step a plain count on one concurrently with the mark or read a global not yet built. Start the service in `main`, or in a function `main` calls
note: <fragment>:41:5: the initializer of `box`, which reaches it
```

<!-- test: error.a-let-initializer-may-not-read-a-managed-var-beside-a-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
**A `let` THAT HOLDS WHAT A `var` OWNS CANNOT BE MARKED SHARED.** `l` is the record `g` owns, so marking `l`'s
graph before `main` would meet an owner no `let` holds. The program is refused at compile time (E3165, owned
by `static-variables.md`) rather than stopping before `main` runs.
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return g
	end 'current'
end 'Box'

var g = Box.create()
let l = Box.current()

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return l.n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3165: <fragment>:10:10: the initializer of the `let` `l` reaches the module-level `var` `g` here — a `let` is fixed at startup and may not hold what a `var` owns, and the compiler does not follow what the initializer keeps of `g`. Make `l` a `var`, or build its value without reading `g`
note: <fragment>:14:5: `g` is declared here
```

<!-- test: error.a-var-initializer-may-not-take-a-lets-record-beside-a-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
**AND A `var` MAY NOT TAKE WHAT A `let` HOLDS.** `v` would be an owner of `a`'s record that no `let` is, so marking
`a` shared before `main` would meet it. Refused at compile time (E3166, owned by `static-variables.md`).
```maxon
type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function current() returns Self
		return a
	end 'current'
end 'Box'

let a = Box.create()
var v = Box.current()

type Reader
	var asked as Integer

	static function create() returns Self
		return Self{asked: 0}
	end 'create'

	export function read() returns Integer
		self.asked = self.asked + 1
		return a.n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let h = spawn Reader.create()
	let n = try await h.read() otherwise 0
	print("n={n} v={v.n}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3166: <fragment>:15:5: the initializer of the module-level `var` `v` may hand it a record a module-level `let` holds, through `Box.current` — a `let` is fixed at startup and a `var` may write what it holds, so the two may not share a record. Build the value without taking the `let`'s record, or make `v` a `let`
```

<!-- test: a-plain-function-may-write-a-module-global -->
**THE SCOPE GUARD — a `spawn` anywhere does not make the whole program concurrent.** Whether a type is a
service is a WHOLE-PROGRAM property (`services.md`, *"a `spawn` anywhere makes the type a service"*), and
the tempting cheap implementation of this rule inherits that shape: refuse every global write in any
program that spawns. This program spawns one, and `bookKeeping` is still reached from `main` alone — one
green thread, before and after an await that is fully ordered. Its two writes are as safe as
they are in a program with no `spawn` in it at all.

⚠ **THE SECOND CALL IS PAST THE `await` ON PURPOSE.** A reachability walk that stopped at the first
suspension point, or that treated everything after an `await` as "concurrent", would refuse the second
`bookKeeping(n)` and not the first — so the case puts one on each side of it and pins the sum, `10 + 5`.
```maxon
var ledger = 0

function bookKeeping(by Integer)
	ledger = ledger + by
end 'bookKeeping'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer)
		self.count = self.count + by
	end 'add'

	export function total() returns Integer
		return self.count
	end 'total'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	bookKeeping(10)
	h.add(5)
	let n = try await h.total() otherwise 0
	bookKeeping(n)
	print("ledger={ledger} count={n}\n")
	return ledger as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
ledger=15 count=5
```
```exitcode
15
```

<!-- test: an-interface-in-a-handlers-cone-does-not-implicate-the-stdlib -->
⭐⭐ **THE OVER-REFUSAL THAT MADE THIS RULE UNUSABLE, PINNED — one ordinary interface method in a handler's
cone used to refuse every program that so much as MENTIONED `Log`.**

A `witnessDispatch` chooses its target at run time, so it triggers the same widening the case above pins.
The widening used to mark EVERY function in the program, and `stdlib/Log.maxon` holds a module-level `var
capturing` that `Log.startCapture` writes — so the compiler refused this program with *"`Log.startCapture`
writes the module-level `capturing` … make `capturing` a `let`"*. **The cure names a file the author does not
own**, `stdlib/Testing.maxon` has the same shape, and interfaces are not exotic: essentially every service
program that used one was refused.

⭐ **THE NARROWING, AND WHY IT IS SOUND.** A function value in this language comes from exactly two places —
a `functionRef` (a closure literal, or a named function used as a value) and a witness slot, whose accepted
member the conformance check files by name. A function that is in neither has no address anywhere in the
emitted image, so no `indirectCall` and no `witnessDispatch` can reach it. `Log.startCapture` is neither, so
it is not implicated here; `Polite.greet` IS a witness member and remains in the widened set, which is what
keeps the refusing direction intact for the target the dispatch can genuinely land on.

⚠ **`Log` IS NAMED FROM `main` AND NOT FROM THE HANDLER, and that distinction is the whole case.** A handler
that CALLS `Log.trace` is still refused, correctly and by a NAMED call path — `Log.trace` reads `capturing`,
and a message reaches it. What this case pins is that a handler which calls neither is left alone.
```maxon
interface Greeter
	function greet() returns String
end 'Greeter'

type Polite implements Greeter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function greet() returns String
		return "hello"
	end 'greet'
end 'Polite'

function describe(g Greeter) returns String
	return g.greet()
end 'describe'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		let p = Polite.create()
		self.count = self.count + by + (describe(p).count() as Integer)
		return self.count
	end 'add'
end 'Counter'

function main() returns ExitCode
	Log.startCapture()
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	let keys = Log.stopCapture()
	print("n={n} keys={keys.count()}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
n=10 keys=0
```
```exitcode
10
```

<!-- test: a-witness-dispatch-in-a-handlers-cone-does-not-implicate-a-closure -->
⭐⭐ **A WITNESS DISPATCH LANDS ON A MEMBER WEARING ITS REQUIREMENT'S NAME, AND NEVER ON A CLOSURE.** The
handler looks a key up in a `Map`, and a `Map` hashes and compares its key through a witness slot — so every
handler that keeps a map is a handler whose cone holds a run-time dispatch. `bookKeeping` writes a module
global and its address is taken, exactly as in `error.a-dispatch-the-compiler-cannot-follow-widens-the-rule`;
what differs is that no closure is CALLED anywhere a message can run. A witness table holds a conformance's
members and nothing else, so the dispatch can reach every `hash` and every `equals` the program declares, and
cannot reach `bookKeeping`. Widened to every address-taken function, this program was refused, and so was
every service a compiler's own pool runs, whose handlers index maps throughout.
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

typealias Tally = Map with (String, Integer)

type Counter
	var counts as Tally

	static function create() returns Self
		return Self{counts: Tally.create()}
	end 'create'

	export function add(key String, by Integer) returns Integer
		let before = try self.counts.get(key) otherwise 0
		self.counts.upsert(key, value: before + by)
		return before + by
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add("apples", by: 5) otherwise 0
	let l = callIndirect(bookKeeping, n: 1)
	print("n={n} l={l}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
n=5 l=1
```
```exitcode
5
```

<!-- test: error.a-member-a-witness-dispatch-can-land-on-may-not-write-a-module-global -->
<!-- unsupported-targets: wasm32-wasi -->
The refusing twin of the case above, and of `an-interface-in-a-handlers-cone-does-not-implicate-the-stdlib`:
the handler dispatches `greet` through an interface value, and the one member wearing that requirement's
name writes a module global. No named call runs from `Counter.add` to `Loud.greet`, so the sentence names
the dispatch rather than a call path.
```maxon
var greetings = 0

interface Greeter
	function greet() returns String
end 'Greeter'

type Loud implements Greeter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function greet() returns String
		greetings = greetings + 1
		return "HELLO"
	end 'greet'
end 'Loud'

function describe(g Greeter) returns String
	return g.greet()
end 'describe'

type Counter
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function add(by Integer) returns Integer
		let loud = Loud.create()
		self.count = self.count + by + (describe(loud).count() as Integer)
		return self.count
	end 'add'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let n = try await h.add(5) otherwise 0
	print("n={n} greetings={greetings}\n")
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3143: <fragment>:16:3: `Loud.greet` writes the module-level `greetings`, and the message `Counter.add` dispatches through a closure or a witness whose target this compiler cannot name, so it may land here — a message runs on a green thread the scheduler may put on any OS thread, and a module `var` is one word every one of them shares. So `greetings = …` is a load, an add and a store that two of them can interleave, each writing the other's stale value back. Nothing traps when they do; the program answers a number that is too small. Keep it in a field of `self` and hand it back through a reply, or make `greetings` a `let`
note: <fragment>:40:10: the `spawn` that makes `Counter` a service
```
