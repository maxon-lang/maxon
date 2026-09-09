---
title: Async & Concurrency
description: Green threads, awaiting results, parallel execution, and cancellation.
sidebar:
  order: 13
---

Maxon supports concurrency via `async` and `await` with green threads scheduled across multiple OS worker threads. Each `async` call spawns a lightweight green thread with a growable stack (starting at 4KB). The runtime uses a GMP (Goroutine-Machine-Processor) scheduler with per-worker local queues, work stealing, and IOCP-based overlapped I/O.

### Spawning Green Threads

Use `async` before a function call to spawn a green thread:

```maxon
var promise = async someFunction(arg1, arg2)
```

The `async` expression returns a promise value that can be awaited later.

### Awaiting Results

Use `await` to wait for a green thread to complete and retrieve its result:

```maxon
var result = await promise
```

If the green thread has already completed, `await` returns immediately. Otherwise, the current thread yields until the result is ready.

### Parallel Execution

Multiple green threads can run concurrently:

```maxon
var p1 = async taskA()
var p2 = async taskB()
var r1 = await p1
var r2 = await p2
```

### Void Functions

Functions that return no value can also be spawned as green threads:

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

Cancelling a green thread stops it at its next yield point. The green thread's stack is freed.

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
- `async` can only target functions that yield (contain I/O operations or `await` points)

### Key Properties

- **Multi-threaded** -- green threads are distributed across OS worker threads (one per CPU core)
- **Work stealing** -- idle workers steal from busy workers' local queues for load balancing
- **Cooperative scheduling** -- context switches at `await` points and I/O operations
- **Growable stacks** -- 4KB initial, doubles when needed
- **Thread-safe memory** -- atomic reference counting and lock-protected shared state
- **Fire-and-forget safe** -- unawaited green threads are drained at program exit

---
