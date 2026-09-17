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
where a service send lends it read-only (below).

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
  indirect call or an instance method (**E2015**).
- The callee must be able to wait — call `sleep`, `await`, `Runtime.yield()`, or perform file, socket or
  process I/O, directly or through its callees. A function that never yields is **E3073** (`function never
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

Plain `await` on a throwing promise is **E3057**; `try await` on a promise that cannot throw is **E3055**.

## Promises in Collections and Fields

A promise can be stored when its type is named: `Promise with T` for a function returning `T`, and
`Promise with (T, E)` for one that also throws `E`. Storing a throwing promise under a type that omits the
error is **E3098**.

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

	let first = __Builtins.awaitAny(naps)
	print("first finished: index {first}\n")     // index 1

	var sum = 0
	for p in naps 'drain'
		sum = sum + await p
	end 'drain'

	print("sum of tags={sum}\n")                  // 6
	return 0
end 'main'
```

**Waiting for the first of several.** `__Builtins.awaitAny(array)` parks until any promise in the array has
finished and returns its index. It consumes nothing: every promise, the winner included, is still awaited
(or dropped) afterwards. An already-finished promise is returned without parking.

**Reading a promise out of an array.** A promise has one owner, so reading one out of the array that holds it
borrows the array's slot: awaiting or cancelling the read empties that slot, and a read never consumed stays the
array's and is dropped with it. `get(i)`, `first()`, `for p in array`, an array iterator's `current()` and
`peek(n)`, and a destructured `for (iter, p) in array.withIterator()` all name their slot. A read that cannot
name one is **E3141**: `last()`, a list's elements, a `Map`'s values, and a `withIterator()` pair bound whole.
`pop` and `remove` move the promise out, and the caller owns it outright. Two reads of one slot hold one
promise, so only one of them may be consumed; consuming the second is **E3141** where the compiler can see both
name the slot, and otherwise aborts the program with exit code **118**.

## Cancellation and Dropped Promises

`promise.cancel()` consumes a promise without waiting for it. A promise that is never awaited is dropped
when it goes out of scope (or is overwritten), which has the same effect.

- A coroutine that has **not started** never runs.
- A coroutine that has **already started** is not interrupted: it runs to the end of its body, and its
  result is discarded.
- A promise held in an array or a field is dropped with its container.

## Yielding

`Runtime.yield()` gives other runnable work a turn and then continues. It never blocks and uses no timer
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
	h.bump(3)                                       // fire-and-forget message
	h.bump(4)
	let n = try await h.total() otherwise 0         // a message with a reply
	print("total={n}\n")                            // total=7

	h.shutdown()
	let after = try await h.total() otherwise (e) 'stopped'
		match e 'why'
			stopped then print("service stopped\n")
		end 'why'
		return 0
	end 'stopped'

	print("unexpected {after}\n")
	return 1
end 'main'
```

- **The same type is still a plain value.** `Calc.create()` without `spawn` is an ordinary record with
  ordinary calls, so a service's logic is unit-testable without threads.
- **Messages.** A method that returns nothing and throws nothing is sent and forgotten. A method that returns
  a value, or throws, is awaited: `try await h.method(…)`. The reply can always fail with
  `ServiceError.stopped`, so plain `await` is **E3057**; the method's own error type merges with it in the
  handler's `match`.
- **Private methods are not messages.** Calling a non-exported method or a static through a handle is
  **E3136**. A service cannot send to itself.
- **Shutdown.** `h.shutdown()` stops the service after the messages already queued. Dropping the last
  handle does the same. Replies requested afterwards fail with `ServiceError.stopped`.
- **The target** of `spawn` must be a static factory that returns its own type; anything else — including a
  bare `spawn f()` — is **E3134**.
- **Generic services.** A generic type can be spawned; its handle type is spelled through an alias,
  `typealias StringBoxHandle = Box.handle with String`.
- Services that `await` each other in a cycle are **E3139**.
- `spawn` is a contextual keyword; it remains usable as a name.

**What crosses a message.** A value sent to a service must not stay reachable from the sender in a way
either side could write, because the two green threads may run at the same time on different processors:

- A `var`, a temporary or a literal argument is **moved** into the service; reading the sender's variable
  afterwards is **E3102**. Factory arguments and replies are moved too.
- A `let` argument that owns its value outright is **lent**: the sender keeps reading it, and from the send
  onwards neither side may store it anywhere writable, return it, capture it or pass it to anything that
  writes it (**E3160**). A handler that writes its parameter's graph at any depth — a method that writes its
  own receiver, called on a record within the parameter, included — refuses every send that lends to it
  (**E3019**).
- A value the sender does not solely own — captured by a closure, held in a container, borrowed from a
  parameter — is **E3138**; send a `.clone()`.
- A parameter type that cannot cross at all — a promise, a function value, an opaque type parameter — is
  **E3135**. A reply that is part of the service's own state is **E3137**; return a copy.
- A value held at an interface type crosses as a message argument, in a service's state and as a reply,
  moved or lent like any other value. A conformer sent at its own type whose graph the runtime cannot walk
  (an OS handle) is **E3138**; once it is held at the interface type it is checked through its witness at
  the send, and such a conformer aborts with exit code **96**.
- Before a send, the runtime also checks the value's whole object graph. The graph may reach one record
  several times — two fields, two slots of an array — when every owner of that record is one of those
  references, and the record is walked once however many paths reach it. If some nested record has an owner
  outside the graph, the program aborts with exit code **96** before anything is sent. A reply is checked
  after the handler's locals and the message's arguments are released, so a reply built from them crosses. A
  generic service's reply is checked at the type its `spawn` fixes: a `returns T` message that hands back a
  container or a reference-holding record from the service's own state aborts with **96**, and a reply whose
  graph holds a type the runtime cannot walk (an OS handle) is **E3138** at the `spawn`. A generic service
  cannot be spawned over a value held at an interface type (**E2015**).

**Module-level state.** A service handler — and anything it calls — may not read or write a module-level
`var` (**E3143**). Keep a service's state in its own fields and hand results back through replies.

A module-level `let` stays readable. In a program that spawns a service, every `let` record built before
`main` is marked shared once the last global initializer has returned, so every count a handler steps on it
is atomic; a record two `let`s reach counts both as its owners. A `let` whose graph no walk can mark — one
holding an OS handle, a value held at an interface type, or a generic instance with no base layout — is
**E3163** where a message can read it, and legal where only `main` does. A `spawn` a global initializer can
reach is **E3164**, a `let` whose initializer reaches a module-level `var` holding a record is **E3165**, and
a `var` whose initializer may take a `let`'s record is **E3166** (see
[Top-Level Variables](/docs/language/variables/#top-level-variables)).

**Output order.** Text printed by `main` and by a service handler may interleave in any order; sequence it
through awaited replies when order matters.

## I/O and Waiting

Operations that wait **park** their green thread instead of blocking an OS thread: `sleep`, `await`, socket
operations (`TcpClient.connect`, `send`, `recv`, `TcpListener.accept`) and waiting on a child process. A
parked green thread holds no processor, so a server waiting in `accept()` costs nothing while idle.

File and directory operations yield to other work before each call into the operating system and are
started with `async` like any other waiting function (`try await` for the throwing ones such as
`File.readText`). The [standard library reference](/docs/stdlib/) documents the file, network and process
APIs.

## The Scheduler

- **Green threads.** Every green thread starts on a small stack that grows on demand, so thousands are
  cheap. A stack stops growing at 1 GiB; a recursion past it stops the program with `panic: stack overflow`.
- **Parallelism.** By default the scheduler creates one processor per logical CPU. The environment
  variable `MAXON_MAX_PROCS=N` sets the count, clamped to between 1 and the CPU count; a value that is not a
  positive number leaves the default. `Runtime.processorCount()` answers the resolved count. Services use
  the processors in parallel; an `async`-only program's coroutines stay on the green thread that started
  them.
- **Preemption.** A green thread that has run for 10 ms is stopped at its next function entry and moved
  behind the other runnable work, so a CPU-bound loop cannot starve the rest of the program.
  `MAXON_PREEMPT=off` disables preemption; `on`, or unset, is the default, and any other value makes the
  program exit at start-up with code 116.
- **Coroutines switch only where they wait**: at `await`, `sleep`, `Runtime.yield()` and I/O.

The [CLI reference](/docs/cli/) lists the environment variables a compiled program reads.

## Exit Codes

| Exit code | Cause |
|-----------|-------|
| 75 | a green thread was neither awaited nor dropped when the program ended |
| 92 | deadlock: `main` has not finished and nothing can ever run again (for example `awaitAny` on an empty array) |
| 96 | a service send found a value with a second owner |
| 116 | `MAXON_PREEMPT` holds a value other than `on` or `off` |

## Targets

Green threads, `async`, `sleep`, `awaitAny` and services run on every native target. `wasm32-wasi` has no
green threads, so each of them is **E3104** there, reported at the call.
