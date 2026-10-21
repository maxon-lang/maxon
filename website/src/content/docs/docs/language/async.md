---
title: Async & Concurrency
description: Coroutines, awaiting results, overlapping waits, and cancellation.
sidebar:
  order: 13
---

Maxon has two concurrency tools:

- **`async` / `await`** start a **coroutine** of the current green thread. Coroutines overlap *waiting* — for a
  timer, a socket, a child process — without creating threads.
- **`spawn`** starts a **service**: a green thread of its own, scheduled independently and able to run in
  parallel on another processor. You talk to it through messages.

Both run on the runtime's scheduler, which maps green threads onto a pool of OS threads. There are no
locks or atomics in user code: a value is only ever reachable from one green thread at a time, except
where it is shared read-only — held by a module-level `let`, or published through a
[`default`](#program-wide-defaults--default) or a [`SharedValue`](#sharedvalue--a-live-value-made-at-run-time)
(below).

## Starting a Coroutine

`async` before a call starts the callee as a coroutine and returns a **promise**:

```maxon
typealias Score = int(0 to 1000)

function slowDouble(n Score, delay Milliseconds) returns Score
	sleep(delay)
	print("finished {n} after {delay} ms\n")
	return n * 2
end 'slowDouble'

function main() returns ExitCode
	let a = async slowDouble(10, delay: 60)
	let b = async slowDouble(20, delay: 20)
	let ra = await a
	let rb = await b
	print("sum={ra + rb}\n")
	return 0
end 'main'
```

```text
finished 20 after 20 ms
finished 10 after 60 ms
sum=60
```

Both coroutines start before either is awaited, so their sleeps overlap and the program takes about 60 ms,
not 80.

- `sleep(milliseconds)` parks the current green thread; it takes a `Milliseconds` value.
- `async` applies to a direct call of a function or a static method. It cannot start a closure, an
  indirect call or an instance method (**E2015**). A generic function is started at the instance its
  arguments infer, as a call would be. A [qualified](/docs/language/namespaces/#qualified-names) callee reads as the direct call
  reads it, at any length: `async sub.Calc.seven()` and `async outer.inner.six()` start what those calls
  name.
- An argument travels as one integer word: an integer, a `bool`, or a managed value's reference. A `float`
  or a value held at an interface type is **E2015**, unless the callee reassigns that parameter — then the
  argument rides a cell the coroutine owns.
- A callee may keep an argument it is handed (move it into a record it returns, for one); a coroutine
  dropped before it runs releases what it was handed instead.
- **The arguments are shared, exactly as an ordinary call shares them.** The caller and its coroutines are
  one strand of one green thread, so the coroutine takes a plain count of its own on each managed argument
  and the caller's binding stays usable: a `var` handed to `async` is read and written after the call, a
  `let` is shared read-only, and one binding other than a promise may be handed twice in one call. A scalar
  is copied. Passing a handle is how a socket or a server is shared with a coroutine.
- **A promise argument moves.** A coroutine can still be running after the frame that started it has ended,
  so an `async` call hands a promise, or a value whose type holds one, only to a parameter that every
  overload the call may bind takes — one the callee awaits, cancels, stores or returns. The coroutine then
  owns the thread, and the caller's binding is spent. A parameter that only reads it is **E3141**: pass the
  promise to one that takes it, or await it and pass the result. One promise at two arguments of one call is
  **E3102**.
- A parameter the callee reassigns gets storage the coroutine owns, and nothing is written back to the
  caller. A coroutine that writes a parameter is refused a [read-only](/docs/language/memory-model/#read-only-values) argument
  (**E3159**), as an ordinary call is.
- A closure that captured a binding owns it, so handing that binding to `async` afterwards is **E3102**.
- The callee must be able to wait — call `sleep`, `await`, `Scheduler.yield()`, send a service message with a
  `sendTimeout:`, or perform file, socket or process I/O, directly or through its callees. A function that never yields is **E3073** (`function never
  yields; 'async' is for I/O-concurrent work only`): there is nothing to overlap.

## Awaiting Results

`await promise` returns the coroutine's result. If it has finished, `await` returns at once; otherwise the
awaiting green thread **parks** and the scheduler runs other work until the result is ready. A coroutine
that returns nothing is awaited as a statement: `await p`.

A promise is consumed exactly once. Awaiting it twice, or using it after `.cancel()`, is **E3142**; awaiting
inside a loop a promise that was started outside the loop is **E3100**.

## Throwing Async Functions

A promise from a throwing function is awaited with `try await`, which accepts every `otherwise` form a
synchronous `try` does:

```maxon
enum FetchError implements Error
	notFound
end 'FetchError'

function lookup(key String) returns String throws FetchError
	sleep(5)
	if key == "missing" 'absent'
		throw FetchError.notFound
	end 'absent'

	return "value-of-{key}"
end 'lookup'

function main() returns ExitCode
	let good = async lookup("x")
	let bad = async lookup("missing")
	let g = try await good otherwise "none"
	let m = try await bad otherwise (e) 'failed'
		match e 'why'
			notFound then print("lookup failed: notFound\n")
		end 'why'
		return 0
	end 'failed'

	print("{g} {m}\n")
	return 1
end 'main'
```

Plain `await` on a throwing promise is **E3057**, unless a [`try` block](/docs/language/error-handling/#try-blocks) or a
[`test` body](/docs/language/testing/#uncaught-errors-in-tests) handles it; `try await` on a promise that cannot throw is **E3055**.
The awaited form may be parenthesized: `try (await p) otherwise …` is `try await p otherwise …`.

## Promises in Collections and Fields

A promise can be stored in a container, a field, a tuple, a union payload or an array literal, captured by a
closure, or held by a generic type, when its type is named: `Promise with T` for a function returning `T`,
and `Promise with (T, E)` for one that also throws `E`. A store takes any promise the storing function owns:

- an `async` spawn, or a service reply received in that function;
- a promise parameter;
- a promise a call returned;
- a promise moved out of storage by `pop`, `remove` or a `match` arm, or yielded by `try … otherwise`.

The store moves the promise: the container or field now owns the thread, and the function's own name for it
is spent (**E3102**). The one condition is that the types agree, and **E3098** names what differs when they
do not: a result type other than the slot's, or an error type the slot drops, changes or adds. A promise
handed to a parameter, or to a container's method such as `Map.upsert`, is held to the same rule. A call
through a function value hands a promise over exactly where the function type says the callee takes it (see
[Capability Qualifiers](/docs/language/functions/#capability-qualifiers-keep-sole-returns-readonly)).

A promise parameter the callee reassigns (`q = async work()`) writes into the caller's own variable: the
callee drops the thread the variable held, the variable takes the new one, and the caller awaits it as any
other promise name. The same holds through a function that only hands the parameter on, and at a position a
function type passes by reference. The argument is a `var` the caller owns: a `let`, a `let` field, a name a
closure captured, or a promise an array still holds is **E3159**. Inside a loop, `await x` after such a call
spends `x`, so the call on the next iteration is **E3102**.

```maxon
typealias Tag = int(0 to 100)
typealias TagPromise = Promise with Tag
typealias TagPromiseArray = Array with TagPromise

function nap(delay Milliseconds, tag Tag) returns Tag
	sleep(delay)
	return tag
end 'nap'

function main() returns ExitCode
	var naps = TagPromiseArray.create()
	naps.push(async nap(200, tag: 1))
	naps.push(async nap(20, tag: 2))
	naps.push(async nap(100, tag: 3))

	let first = naps.awaitAny()
	print("first finished: index {first}\n")     // index 1

	var sum = 0 as Tag
	for p in naps 'drain'
		sum = sum + await p
	end 'drain'

	print("sum of tags={sum}\n")                  // 6
	return 0
end 'main'
```

**Waiting for the first of several.** `promises.awaitAny()`, on an `Array with Promise with …`, parks until
any promise in the array has finished and returns its index. It consumes nothing: every promise, the winner
included, is still awaited (or dropped) afterwards. An already-finished promise is returned without parking.
`p.isComplete()` asks one promise whether it has finished, without parking or consuming it.

**Reading a promise out of storage.** A promise has one owner, so reading one out of the array or the struct
field that holds it starts a MOVE out of that slot: consuming the read empties the slot, and a read never
consumed stays the container's and is dropped with it. `get(i)`, `first()`, `for p in array`, an array
iterator's `current()` and `peek(n)`, a destructured `for (iter, p) in array.withIterator()`, and a read of a
promise-typed field all name their slot. A read that cannot name one is **E3141**: `last()`, a list's elements,
a `Map`'s values, and a `withIterator()` pair bound whole. `pop` and `remove` move the promise out, and the
caller owns it outright. Reading a promise out of a temporary — `try make().get(0)` — keeps that temporary
alive to the end of the enclosing scope, so the promise outlives the expression it came from.

**Consuming a promise.** `await`, `.cancel()`, a `push` or `set` into another container, a store into a field
or a union case, and passing it to a parameter the callee takes all consume it: each empties the slot the
promise was read out of, and hands the thread either back to the runtime or to its new owner. A callee takes
a promise parameter it awaits, cancels, stores or returns, or hands on to a callee that takes it; a promise
passed to a parameter the callee only reads stays with the caller. A call of `push`, `set`, `insert`,
`upsert`, `append` or `prepend` stores its bare arguments whatever the receiver is, so a function that
hands its promise parameter to such a method takes the promise even where the receiver's own method only
reads it. Where the overloads a call may bind differ about taking it, the overload the
call binds decides. An `async` call is stricter (see [Starting a Coroutine](#starting-a-coroutine)): every
overload must take the promise. A store into a promise field also releases the thread that field was
holding, exactly once. Three refusals follow:

- **E3141** — the slot a read came out of has already been spent, at any of those doors. Two reads of one slot
  hold one promise, so only one of them may be consumed; where the compiler cannot see that both name one slot,
  the program aborts at run time with exit code **118** instead, and so does `p.isComplete()` on such a read.
  An `async` call that hands a promise to a parameter the callee only reads is **E3141** too, and so is a
  store of a conditional expression (`h.p = a if c else b`): store on each branch instead.
- **E3102** — a promise already moved into storage is used again afterwards, or one promise is passed at two
  arguments of one call where a parameter takes it.
- **E2015** — a promise read outside a loop is given away inside it, or inside a `while` condition. Read it
  inside the loop instead.

**Returning a promise.** A function may return a promise it owns — a spawn, a bound spawn, one taken with
`pop`/`remove`, or a promise parameter — and the caller then owns it, to await, cancel, drop or
[store](#promises-in-collections-and-fields). A `return` of
a promise no frame owns is **E3141**: one read out of a container that still holds it, a merge of two
promises (`p if c else q`), or a second name for a parameter.

## Cancellation and Dropped Promises

`promise.cancel()` consumes a promise without waiting for it. A promise that is never awaited is dropped
when it goes out of scope (or is overwritten), which has the same effect.

- A coroutine that has **not started** never runs.
- A coroutine that has **already started** is not interrupted where it stands: it resumes and runs on, and
  its result is discarded. What it may no longer do is **begin** an operation that waits on a far end. Its
  next `TcpClient.connect`, `TcpListener.accept`, `send`, `sendFrom`, `recv`, or `TcpListener.bind` to a
  host **name** throws that operation's own [`NetworkError`](/docs/stdlib/network/#tcpclient) variant at once
  instead of parking for a wake-up nothing will send. `close()` still completes, and so does a `bind` to a
  numeric address. A service send it makes to a full mailbox fails `mailboxFull` at once, with or without a
  `sendTimeout:`, and a send already waiting for room fails `mailboxFull` when the promise is dropped (see
  [Mailboxes and failed sends](#mailboxes-and-failed-sends)).
- A promise held in an array or a field is dropped with its container.

## Yielding

`Scheduler.yield()` gives other runnable work a turn and then continues. It never blocks and uses no timer
(unlike `sleep(0)`); when nothing else is runnable it returns promptly. Use it in a loop that polls for a
condition; use `await` or `sleep` to actually wait.

## Services — `spawn`

A **service** is an ordinary `type` started with `spawn`. `spawn Type.factory(args)` calls a static
factory of the type and runs the result on a new green thread, returning a **handle**. The handle's methods
are exactly the type's `export` and `public` instance methods; each call on the handle is a **message** the
service handles one at a time, in order.

```maxon
typealias Count = int(0 to 1000000)

type Calc
	var count as Count

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function bump(by Count)
		self.count = self.count + by
	end 'bump'

	export function total() returns Count
		return self.count
	end 'total'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	try h.bump(3) otherwise ignore                  // a message with no reply
	try h.bump(4) otherwise ignore
	let n = try await h.total() otherwise 0         // a message with a reply
	print("total={n}\n")                            // total=7

	h.shutdown()
	let after = try await h.total() otherwise (e) 'stopped'
		match e 'why'
			mailboxFull then print("mailbox full\n")
			serviceStopped then print("service stopped\n")
		end 'why'
		return 0
	end 'stopped'

	print("unexpected {after}\n")
	return 1
end 'main'
```

- **The same type is still a plain value.** `Calc.create()` without `spawn` is an ordinary record with
  ordinary calls, so a service's logic is unit-testable without threads.
- **Messages.** Every send is a throwing call, because it can fail at the send (see
  [Mailboxes and failed sends](#mailboxes-and-failed-sends)): write `try h.method(…)` with an `otherwise`,
  or propagate it. A send with no `try` is **E3057**, except inside a block-form `try` or a `test` body,
  which take its error.
- **Replies.** A method that returns a value, or throws, delivers a reply. `try await h.method(…)` sends it
  and awaits the reply, and its one `try` covers both: the error it binds has the send's cases
  `mailboxFull` and `serviceStopped` beside the method's own. `let p = try h.method(…)` sends it and keeps
  the reply as a promise, whose error type is the method's own alone: `Promise with (T, E)` for a method
  that throws `E`, `Promise with T` for one that throws nothing, which a plain `await p` resolves. A
  bare `try await` that propagates is refused (**E3059**) when the method throws too, because the two
  errors have no single type to propagate: handle it with `otherwise (e)` and `match e`, or split it into
  `let p = try h.method(…)` and `try await p`.
- **A reply is kept, or discarded on purpose.** A method that returns or throws, sent as a statement, is
  **E3190**. Await the reply, keep it, or discard it on purpose with `_ = try h.method(…)`.
- **Private methods are not messages.** Calling a non-exported method or a static through a handle is
  **E3136**. A service cannot send to itself.
- **A message is as visible as its service, and no more.** Its `export` makes it a message rather than
  widening it past the type, so the types its signature names need only the service type's visibility
  ([E3167](/docs/cli/error-codes/#e3167--semanticsignaturetypelessvisiblethanfunction)): a file-private service's messages may name
  file-private types.
- **Shutdown.** `h.shutdown()` closes the mailbox: the messages already queued still run, and then the
  service stops. Every later send fails with `serviceStopped`. It always succeeds, on a full mailbox too.
  Dropping the last handle does the same. A
  queued message always runs, so a reply always resolves.
- **The target** of `spawn` must be a static factory that returns its own type; anything else — including a
  bare `spawn f()`, a qualified chain that names no type, or a chain whose head is a binding in scope — is
  **E3134**, in a `default` as anywhere else. The type may be [qualified](/docs/language/namespaces/#qualified-names), at any length:
  `spawn sub.Calc.create()` starts a type declared in `sub/`, `spawn outer.inner.Calc.create()` one two
  directories down, and `spawn stdlib.Range.create(1, finish: 4)` the library's `Range` in a program that
  declares its own. A factory the spawning file cannot see is **E3008** (**E3088** for a `module` one).
- **Generic services.** A generic type can be spawned; its handle type is spelled through an alias,
  `typealias StringBoxHandle = Box.handle with String`.
- Services that `await` each other in a cycle are **E3139**.
- `spawn` is a contextual keyword; it remains usable as a name.

**What crosses a message.** A value sent to a service must not stay reachable from the sender in a way
either side could write, because the two green threads may run at the same time on different processors:

- A local `let` or `var`, a temporary or a literal argument the sender owns is **moved** into the service;
  reading the sender's variable afterwards is **E3102**. Replies are moved too, and so is every argument a
  `spawn`'s factory keeps in the service's state. The handler owns what it was moved, so it may keep it or
  write it.
- A [read-only](/docs/language/memory-model/#read-only-values) value the sender does not own — a module-level `let`, a `let` field, a
  `SharedValue` or registry read, or a parameter nothing in the sending function writes — is **shared**: its graph is marked shared, so every
  count on it steps atomically, and neither side may write it. Sending a parameter the sending function
  writes, or any shared value the handler writes or keeps, is **E3138**. A borrowed `String` is copied at the
  send.
- A tuple none of whose elements owns heap — `(Amount, Amount)`, say — is a value. When the sender is not its
  one owner, it is copied wherever it crosses: a send, a factory argument the factory keeps,
  `SharedValue.publish` or `Key.register`. The service gets the copy and the sender keeps its own.
- A value with an owner besides the sender that is not read-only — held in a container, read out of a field
  or an element of a `var`, or the source of an enclosing `for` loop — is **E3138**; send a `.clone()`. A
  borrowed factory argument the factory keeps is **E3138** too, a generic factory's argument at a type
  parameter included; one the sender owns but also shares is caught by the run-time check of the
  service's state below (exit code **96**). A field read off a temporary built inside the factory call's own
  argument list crosses, because the `spawn` releases that temporary.
- A `spawn` of a factory declared more than once, whose declarations keep an argument differently, is
  **E2015**; give the factories distinct names.
- A parameter type that cannot cross at all — a promise, a function value, an opaque type parameter — is
  **E3135**. A reply that is part of the service's own state is **E3137**; return a copy. For a generic
  service the reply is judged at the `spawn` that fixes `T`, and a `returns T` message that hands back the
  state is **E3137** there whenever `T` resolves to a managed type; a scalar `T` crosses.
- A value held at an interface type crosses as a message argument, in a service's state and as a reply,
  moved like any other value. A conformer sent at its own type whose graph the runtime cannot walk
  (an OS handle) is **E3138**; once it is held at the interface type it is checked through its witness at
  the send, and such a conformer aborts with exit code **96**.
- Before a send, the runtime also checks the value's whole object graph. The graph may reach one record
  several times — two fields, two slots of an array — when every owner of that record is one of those
  references, and the record is walked once however many paths reach it. If some nested record has an owner
  outside the graph, the program prints `fatal error: runtime abort 96 (transferredRecordNotSole)` to
  stderr and exits with code **96** before anything is sent. A reply is checked
  after the handler's locals and the message's arguments are released, so a reply built from them crosses. A
  generic service's reply is checked at the type its `spawn` fixes: a `returns T` message that hands back a
  container or a reference-holding record from the service's own state is **E3137** at the `spawn`, and a
  reply whose graph holds a type the runtime cannot walk (an OS handle) is **E3138** there. A generic service
  cannot be spawned over a value held at an interface type (**E2015**).

**Module-level state.** A service handler — and anything it calls — may not read or write a module-level
`var` (**E3143**). Keep a service's state in its own fields and hand results back through replies.

A module-level `let` stays readable. In a program that spawns a service, every `let` record built before
`main` is marked shared once the last global initializer has returned, so every count a handler steps on it
is atomic; a record two `let`s reach counts both as its owners. A `let` whose graph no walk can mark — one
holding an OS handle, a value held at an interface type, or a generic instance with no base layout — is
**E3163** where a message can read it, and legal where only `main` does. A `spawn` a global initializer can
reach is **E3164** (a [`default`](#program-wide-defaults--default) starts a program-wide service before
`main`), a `let` whose initializer reaches a module-level `var` holding a record is **E3165**, and
a `var` whose initializer may take a `let`'s record is **E3078** (see
[Top-Level Variables](/docs/language/variables/#top-level-variables)).

**Output order.** Text printed by `main` and by a service handler may interleave in any order; sequence it
through awaited replies when order matters.

### Mailboxes and failed sends

A mailbox holds at most its **capacity** of queued messages: `DefaultMailboxCapacity` (1024), or the
`MailboxCapacity` (`int(1 to 2^31)`) that `spawn(capacity: n) Type.factory(args)` names. The capacity is a
ceiling: the mailbox's memory grows with its backlog, up to the capacity.

A send queues the message, or fails with an error the compiler declares for each message,
`<Service>.<message>.sendError`, whose two cases carry the arguments the send was given:

- `mailboxFull(<arguments>)` — the mailbox holds its capacity;
- `serviceStopped(<arguments>)` — the service has been shut down.

**`sendTimeout:`.** Every message call takes a reserved last argument, `sendTimeout: Milliseconds`, which
says how long a send waits for room in a full mailbox. It defaults to 0: a send to a full mailbox fails
`mailboxFull` at once. It may be the call's only argument, `h.ping(sendTimeout: 5000)`. It stays with the
send: the handler receives the message's own arguments alone.

- A send given a positive timeout waits in line behind the senders already waiting on that mailbox. Each
  message the service takes frees a slot, and the first waiting sender's message goes into it, so the
  waiting sends are delivered in the order they began to wait.
- The service shutting down fails every waiting send with `serviceStopped`.
- The timeout passing fails the send with `mailboxFull`.
- `sendTimeout: ForeverMs` waits as long as it takes, untimed: the blocking send of a Go channel.
  When the mailbox stays full and every green thread is waiting, the program stops with exit code
  **92** (see [Exit Codes](#exit-codes)).
- A send in a coroutine whose promise has been dropped (see
  [Cancellation and Dropped Promises](#cancellation-and-dropped-promises)) fails `mailboxFull` at once, and so
  does a waiting one when the promise is dropped. This is the one way a send given `ForeverMs` fails
  `mailboxFull`.
- A message that declares a parameter named `sendTimeout`, and an argument after `sendTimeout:`, are
  **[E3192](/docs/cli/error-codes/#e3192--semanticservicesendtimeoutreserved)**.

The arguments come back owned by the caller, so they can be sent again, to this service or another. A
read-only value the send shared (see above) comes back as a copy. One whose type has no deep copy comes back
as the shared value itself, which the caller may read and keeps for itself. A message with no parameters
has payload-free cases. Through an
[interface handle](#interface-handles) the error is `<Interface>.<message>.sendError`.

```maxon
typealias Count = int(0 to 1000000)

type Store
	var kept as Count
	var last as String

	static function create() returns Self
		return Self{kept: 0, last: ""}
	end 'create'

	export function keep(line String)
		self.kept = self.kept + 1
		self.last = line
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 64) Store.create()

	for i in 0 upto 1000 'eachLine'
		try h.keep("line {i}", sendTimeout: 50) otherwise (e) 'refused'
			match e 'why'
				mailboxFull(back) then print("dropped {back}\n")
				serviceStopped then return 1
			end 'why'
		end 'refused'
	end 'eachLine'

	return 0
end 'main'
```

- **A full mailbox is the sender's to handle.** Wait for room with `sendTimeout:`, as above, send elsewhere,
  or drop the message. A send with a finite timeout waits a bounded time, so with those the only unbounded
  wait is an `await`, and [E3139](/docs/cli/error-codes/#e3139--semanticservicecallcycle)'s cycle rule keeps the
  program deadlock-free. A `ForeverMs` send is an unbounded wait too: services that fill each other's
  mailboxes with `ForeverMs` sends can deadlock.
- ⚠ **Retrying coroutines can livelock.** A yield runs the green thread's other runnable coroutines first.
  When every coroutine of one green thread retries a send without a timeout to the same full service, at one
  processor with preemption off (`MAXON_MAX_PROCS=1`, `MAXON_PREEMPT=off`) the coroutines hand the processor
  to each other and the service waits for it forever. Give the send a `sendTimeout:`, which parks the
  coroutine until there is room.
- **A send error is propagated** by a bare `try` in a function that `throws <Service>.<message>.sendError`,
  or rethrown with `throw e` from the binding that caught it. Throwing one the function does not solely
  own — a parameter, say — is **E3191**, because its catcher takes the arguments out of it as their only
  owner.
- `otherwise ignore` drops a failed send and releases its arguments.

## Interface Handles

A service type may `implements` an interface, and `I.handle` is a handle to any running service whose type
implements `I`. Each requirement of `I` is a message: a call through an `I.handle` is sent to whichever
service the handle holds, by the same rules as the service's own handle. Its send error is
`I.<requirement>.sendError`, with the same `mailboxFull` and `serviceStopped` cases.

```maxon
typealias Count = int(0 to 1000000)

interface Tally
	function add(by Count)
	function total() returns Count
end 'Tally'

type Counter implements Tally
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function add(by Count)
		self.n = self.n + by
	end 'add'

	export function total() returns Count
		return self.n
	end 'total'
end 'Counter'

function report(t Tally.handle) returns Count
	try t.add(5) otherwise ignore
	return try await t.total() otherwise 0
end 'report'

function main() returns ExitCode
	let total = report(spawn Counter.create())
	print("total={total}\n")                       // total=5
	return 0
end 'main'
```

- A `T.handle` converts to `I.handle` wherever an `I.handle` is expected: a binding, an argument (an
  overloaded callee's included), a return, a field, an array element, a message argument, a
  `Key.register` and a call through a function value or a closure. `as I.handle` converts explicitly.
  A value that converts equally well to two overloads' interface handles is ambiguous (**E3007**).
- Every requirement must be sendable: its parameters must cross a message (**E3135**) and its reply must be
  carriable (**E3140**). One send serves every implementer, so the services a program sends to through
  `I.handle` declare each requirement with the same parameter, return and `throws` types
  ([E3177](/docs/cli/error-codes/#e3177--interfacehandleimplementersdisagree)); send through each service's own handle
  otherwise.
- A requirement sent through `I.handle` is a message of every service implementing `I`, so each declares it
  as an `export` instance method. Sending one that an implementer declares another way (a private method,
  say), or that no implementer declares, is [E3136](/docs/cli/error-codes/#e3136--semanticservicenotamessage).
- A handler that sends through an `I.handle` is checked against the implementers of `I`, so
  [E3139](/docs/cli/error-codes/#e3139--semanticservicecallcycle) finds an await cycle through any of them.

## Program-Wide Defaults — `default`

A top-level `default Key = expression` declares the program-wide default for `Key`. Any thread reads it with
`Key.current()` and replaces it with `Key.register(x)`; every later `current()` sees the replacement.

- **A service key** is an interface. The expression spawns a type that implements it
  ([E3170](/docs/cli/error-codes/#e3170--registrydefaultservicedoesnotimplementkey) otherwise), and `Key.current()` returns an
  owned `Key.handle`.
- **A value key** is a type whose graph a share walk can mark: one that reaches no OS handle, function value
  or promise ([E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable) otherwise). Its fields may be `var`,
  and may hold containers. The expression builds one, and may spawn services for its fields.
  `Key.current()` returns the published record, [read-only](/docs/language/memory-model/#read-only-values) all the way down: a write
  through it is **E3159**, binding it to a `var` is **E3078**, and `clone()` makes a mutable copy.

```maxon
typealias Count = int(i64.min to i64.max)

interface Greeter
	function greet() returns Count
end 'Greeter'

type Polite implements Greeter
	var calls as Count

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function greet() returns Count
		self.calls = self.calls + 1
		return 7
	end 'greet'
end 'Polite'

type Config
	export let retries as Count

	static function create(retries Count) returns Self
		return Self{retries: retries}
	end 'create'
end 'Config'

default Greeter = spawn Polite.create()
default Config = Config.create(3)

function main() returns ExitCode
	let answer = try await Greeter.current().greet() otherwise 0
	Config.register(Config.create(5))
	print("{answer} {Config.current().retries}\n")     // 7 5
	return 0
end 'main'
```

- **When it is built.** Defaults are built in dependency order after the last global initializer has
  returned and the module's `let`s are marked shared, and before `main`. Only a default some code reaches —
  through its key's `current()` or `register()` — is built, so a default's service runs only in a program
  that uses its key. Defaults whose expressions reach each other are
  [E3172](/docs/cli/error-codes/#e3172--registrydefaultsreacheachother), and a `current()` or `register()` reachable
  from a global initializer is [E3173](/docs/cli/error-codes/#e3173--registryreachedfromglobalinitializer).
- **Precedence.** A program's own `default` for a key replaces the standard library's. Two for one key on
  the same side are [E3169](/docs/cli/error-codes/#e3169--registryduplicatedefault).
- **A service lookup is a snapshot.** A handle taken before a `register` keeps reaching the service it was
  taken from; that service runs until its last holder drops it, then drains its queue and exits.
- **A value lookup is lock-free**, at the cost of reading a module `let`. A registered value must be solely
  owned by the caller ([E3174](/docs/cli/error-codes/#e3174--registrypublishedvaluenotsolelyowned); publish a `.clone()`)
  or a tuple of heap-free elements, which is copied ([Moves and Sharing](/docs/language/memory-model/#moves-and-sharing)),
  and it is marked shared before any other thread can read it. A value a reader already holds stays valid
  after it is replaced.
- **`Key.register(new, replacing: old)`** publishes `new` only while `old` is still the current value, and
  returns `true` when it did. On `false`, `new` is consumed and dropped. Build the value again from a fresh
  `current()` and retry, so two threads updating one key keep both updates.
- `register` may be called from any thread, a service handler included.
- `current` and `register` belong to the registry, so a key type that declares a static member of either name
  is [E3175](/docs/cli/error-codes/#e3175--registrykeydeclaresaregistrymember).
- **At exit**, the drain that runs the services' queued messages keeps the defaults live, so a handler may
  still look one up; they are released once every service is idle, and a default's service then drains and
  exits. Module and `static` `let`s are released inside the drain after the services settle, so a handler
  may read them there too.

## `SharedValue` — a Live Value Made at Run Time

`SharedValue with T` is a value key's slot created at run time: one live value that every holder of the cell
reads. `T` follows the value-key rule, or is a `String`, a service handle or a scalar
([E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable) otherwise).

```maxon
typealias Count = int(0 to 1000)

type Config
	export let n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

type Reader
	let cell as ConfigCell

	static function create(cell ConfigCell) returns Self
		return Self{cell: cell}
	end 'create'

	export function read() returns Count
		return self.cell.current().n
	end 'read'
end 'Reader'

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let reader = spawn Reader.create(cell.clone())
	let before = try await reader.read() otherwise 0
	cell.publish(Config.create(6))
	let after = try await reader.read() otherwise 0
	print("{before} {after}\n")                       // 1 6
	return 0
end 'main'
```

- `ConfigCell.create(v)` makes a cell; `current()` reads its value lock-free; `publish(v)` replaces it
  for every holder; `publish(v, replacing: old)` is the conditional form, answering as
  `Key.register(new, replacing: old)` does.
- Readers take a read-only snapshot and a writer publishes a whole new value, so a value a reader holds
  stays valid across later publishes. A value `current()` returns is read-only all the way down, as a value
  key's is. A published value must be solely owned
  ([E3174](/docs/cli/error-codes/#e3174--registrypublishedvaluenotsolelyowned)) or a tuple of heap-free elements, which is
  copied ([Moves and Sharing](/docs/language/memory-model/#moves-and-sharing)).
- The cell is shared by reference: `clone()` hands out another reference to the same value, and a cell
  crosses a message like a service handle.

## I/O and Waiting

Operations that wait **park** their green thread instead of blocking an OS thread: `sleep`, `await`, socket
operations (`TcpClient.connect`, `send`, `recv`, `TcpListener.accept`) and waiting on a child process. A
parked green thread holds no processor, so a server waiting in `accept()` costs nothing while idle.

File and directory operations yield to other work before each call into the operating system and are
started with `async` like any other waiting function (`try await` for the throwing ones such as
`File.readText`). The [standard library reference](/docs/stdlib/) documents the file, network and process
APIs.

A failed operation reports the reason its own call into the operating system gave, such as the case of a file
error or the `os error N` of a `SubprocessError`, even when its green thread ran on another OS thread between
that call and the report.

## The Scheduler

- **Green threads.** Every green thread starts on a small stack that grows on demand, so thousands are
  cheap. A stack stops growing at 1 GiB; a recursion past it stops the program with `panic: stack overflow`.
- **Parallelism.** By default the scheduler creates one processor per logical CPU. The environment
  variable `MAXON_MAX_PROCS=N` sets the count exactly, for any decimal `N` from 1 to 2147483647, including a
  count above the CPU count; any other value leaves the default. `Scheduler.processorCount()` answers the resolved count. Services use
  the processors in parallel; an `async`-only program's coroutines stay on the green thread that started
  them.
- **Preemption.** A green thread that has run for 10 ms is stopped at its next function entry, `spawn`, `async` call, service send, `await` or `awaitAny`, or
  when a service takes its next message, and moved
  behind the other runnable work, so a CPU-bound loop cannot starve the rest of the program.
  `MAXON_PREEMPT=off` disables preemption; `on`, or unset, is the default, and any other value makes the
  program exit at start-up with code 116.
- **Coroutines switch only where they wait**: at `await`, `sleep`, `Scheduler.yield()`, I/O, and a service
  send given a `sendTimeout:`, while its mailbox is full.

The [CLI reference](/docs/cli/) lists the environment variables a compiled program reads.

## Exit Codes

Each of these aborts writes `fatal error: runtime abort N (name)` to stderr, then exits with code `N`.

| Exit code | Cause |
|-----------|-------|
| 75 | a green thread was neither awaited nor dropped when the program ended |
| 90 | a service's message loop received a message its type does not declare — reachable only if a send the compiler built names no message of the service |
| 92 | deadlock: `main` is unfinished and every green thread waits for good (for example `awaitAny` on an empty array, or a `sendTimeout: ForeverMs` send to a mailbox that stays full) |
| 96 | a service send found a value with a second owner |
| 98 | a service send waiting for room broke the protocol between the sender and the party that wakes it — reachable only through a send the compiler built wrongly |
| 116 | `MAXON_PREEMPT` holds a value other than `on` or `off` |
| 120 | a deep copy reached an interface-typed field whose conformer cannot be duplicated — reachable only if a `.clone()` the front end should have refused was compiled |

Exit code 70 with `fatal error: <reason>` on stderr is a broken runtime invariant: a bug in the compiler or
its runtime, worth reporting with the program that produced it. One reason marks a limit of the host:
on x64-windows, `fatal error: exception dispatched below green thread stack bottom` ends a program whose
green thread faulted with too little stack left for the operating system's exception dispatch, whose size
grows with the processor's vector state.

Exit code 2 with `fatal error: cannot open standard fds` is the start-up check described under
[Entry Point](/docs/language/overview/#entry-point).

## Targets

Green threads, `async`, `sleep`, `awaitAny` and services run on every native target. `wasm32-wasi` has no
green threads, so each of them is **E3104** there, reported at the call when the language checks find no
other error in the program.
