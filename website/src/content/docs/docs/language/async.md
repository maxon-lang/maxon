---
title: Async & Concurrency
description: Coroutines, awaiting results, overlapping waits, and cancellation.
sidebar:
  order: 13
---

Maxon supports concurrency via `async` and `await`. An `async` call creates **no thread of any kind**: it starts the callee as a **coroutine of the green thread that called it**, on a growable stack (2KB to begin with, 8KB on x64-Windows) that doubles until the frame asking fits. So `async` overlaps **waiting**, not execution. The runtime underneath is a GMP (Goroutine-Machine-Processor) scheduler — per-processor run queues, work stealing, and a poller carrying timers, children, pipes and sockets — and `spawn`, which starts a **service**, is what publishes a green thread to it.

### Starting a Coroutine

Use `async` before a function call to start it as a coroutine of the current green thread:

```maxon
var promise = async someFunction(arg1, arg2)
```

The `async` expression returns a promise value that can be awaited later.

### Awaiting Results

Use `await` to wait for a coroutine to complete and retrieve its result:

```maxon
var result = await promise
```

If the coroutine has already completed, `await` returns immediately. Otherwise the awaiting green thread **parks**: it hands its machine back to the scheduler, which runs whatever else is runnable, and the completing coroutine readies the waiter. No wait ever runs another green thread on the waiter's stack.

### Overlapping Waits

Several coroutines can be in flight at once, and their WAITING overlaps:

```maxon
var p1 = async taskA()
var p2 = async taskB()
var r1 = await p1
var r2 = await p2
```

### Void Functions

Functions that return no value can also be started with `async`:

```maxon
var p = async doWork()
await p
```

### Throwing Async Functions

Async functions that throw require `try await` instead of plain `await`:

```maxon
var p = async mayFail(true)
var result = try await p otherwise 0
```

The `try await` syntax supports the same `otherwise` clauses as `try` on synchronous calls:
- `try await p otherwise <default>` -- use a default value on error
- `try await p otherwise panic("msg")` -- panic on error
- `try await p otherwise ignore` -- for void throwing functions
- `try await p otherwise return -1` (or `break`/`continue`/`throw ...`) -- run a single statement on error
- `try await p` -- propagate the error (inside a throwing function)

### Cancellation

A promise can be cancelled via the `.cancel()` method:

```maxon
var p = async longRunning()
p.cancel()
```

`.cancel()` **consumes** the promise and takes the same road an unawaited promise takes at scope exit: the coroutine is reclaimed and its stack freed. One that has not started never runs; a parked one is taken off whatever would have woken it — its `sleep` timer, its child, its poll descriptor. A coroutine already running is renounced rather than interrupted, so it runs its body out with nothing left to take its result. The promise is spent either way, so a later use of it is a compile error.

### Typed promises in collections

Promises can be stored in collections and struct fields by declaring an explicit `Promise with T` type. The compiler boxes the green-thread handle into a `Promise<T>` struct at the storage site and unboxes it at the matching `await`. This pattern lets you fan out N tasks and join them in a second loop:

```maxon
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

var arr = IntPromiseArray.create()
for i in 0 upto n 'spawn'
	arr.push(async compute(i))
end 'spawn'
var total = 0
for p in arr 'join'
	total = total + await p
end 'join'
```

### Restrictions

- `async` can only be used on direct function calls (not closures or indirect calls)
- `async` can only target functions that yield (contain I/O operations, `sleep`, or `await` points)

### Key Properties

- **One owner** -- a coroutine belongs to the green thread that created it and runs only where that green thread's work runs: a green thread and its coroutines are one **strand**, and at most one machine runs a strand's members at a time
- **Every wait parks** -- the waiter hands its machine back to the scheduler and whoever completes the wait readies it; a coroutine hands over at `await` points, `sleep` calls, `Runtime.yield()` and I/O, never in between
- **Green threads are preempted** -- one that has held its processor for 10 ms is stopped at its next function entry and put behind every other runnable green thread, and may resume on another OS thread
- **Growable stacks** -- 2KB initial (8KB on x64-Windows), doubling until the frame asking fits, up to 1GB
- **Plain reference counting** -- every refcount step on a box happens on the machine running that box's strand, one machine at a time, so it needs no atomic. The runtime's own shared counters and queues are protected separately
- **An unawaited promise is dropped, not drained** -- at scope exit the coroutine is reclaimed: one that never started never runs, and a parked one's wait is cancelled in place

---
