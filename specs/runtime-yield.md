---
feature: runtime-yield
status: stable
keywords: [runtime, yield, green-threads, scheduler, concurrency, async, cooperative]
category: concurrency
---

# `Runtime.yield()` — the cooperative yield

## Documentation

`Runtime.yield()` hands the CPU to the next runnable green thread and returns when the caller's turn comes
round again. It is the explicit form of the thing `async`/`await` and `sleep` do implicitly: a point at which
the calling green thread stops occupying the scheduler so that other work can progress.

```text
function main() returns ExitCode
	Runtime.yield()
	return 0
end 'main'
```

It takes no arguments, returns nothing, and never throws. `Runtime` is a namespace rather than a value — it
has no fields and is never constructed, so every member is `static`.

### What it promises

The next runnable green thread takes over, and the caller resumes exactly where it left off once its turn
comes round again — **behind** everything that was already runnable, never ahead of it. A yield that returned
the caller to the front of the queue would be no yield at all: it would hand the processor straight back to
the thread that just gave it up. **If nothing else is runnable it returns promptly and the caller simply
continues** — a yield never blocks waiting for work to appear.

`main` is an ordinary green thread, so a yield taken there takes the same road as one taken anywhere else —
which is why `Runtime.yield()` needs no separate spelling for it.

That makes `Runtime.yield()` the right primitive for a wait loop that has something to re-check:

```text
while not ready 'spin'
	Runtime.yield()
end 'spin'
```

Such a loop is a busy wait that **makes progress**: each yield lets other green threads run and lets the
runtime notice work that has become due, so whatever the loop is waiting for can actually happen. It is not a
substitute for `await` or `sleep`, which are the *blocking* waits and which cost no CPU while they wait.

### It is not `sleep(0)`

`sleep(ms)` parks the calling green thread on a timer and resumes it once the deadline passes, so even
`sleep(0)` occupies a timer entry for the round trip. `Runtime.yield()` occupies none: it only re-queues the
caller instead. A program may therefore yield as often as it likes without competing for the
timer store, which matters precisely when many green threads are yielding at once.

### It is safe outside any async context

Calling it from a program that has never spawned anything is well defined: the scheduler is up before `main`
runs, `main` is its only green thread, and a yield with nothing else runnable comes straight back. It goes
behind nothing on the global queue and pays the wake any global put does (Go's `goschedImpl`), so above one
processor that wake may start an idle machine and `main` may resume on another OS thread. This is what lets
library code yield without first asking whether its caller happens to be concurrent.

### It counts as yielding

A function that calls `Runtime.yield()` **yields**, so `async` over it is accepted. `async` requires a
callee that can actually give up the scheduler — a function that only computes is refused with **E3073**
(see `async-await.md`'s `error.no-yield`, which pins that message). `Runtime.yield()` satisfies that check on
the same footing as `sleep`, which is likewise a scheduler park rather than an I/O wait.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** `Runtime.yield()` lowers to `__gt_resched`, the scheduler's yield park, so it
serves the lanes that have a green-thread substrate and is refused with **E3104** on the ones that do not.

## Tests

<!-- test: runtime-yield.sibling-runs -->
The discriminating case: a yield really does hand the processor to a SIBLING that would not otherwise run
first. `spinner` is created FIRST, so it is the member of `main`'s strand that runs first once `main` parks on
its await; it yields a thousand times and then reports whether `setter` ever ran. `setter` sets the flag as its
first act, so the only question the exit code answers is whether `spinner`'s yields let it in. `3` (`1` seen +
`2` acknowledged) means they did; `2` means a thousand yields went by and the sibling never got a turn.

**Creation order is what makes it a test rather than a coincidence.** A strand runs its members in the order
they were readied, so a spinner created AFTER the setter would find the flag already set whether or not its
yields did anything, and the case would pass with the yield removed. Checked the only way this claim can be:
with `__gt_resched` made to return at once, this case reads `2`.

Both spawned functions yield, which is also required rather than decorative: `async` demands a callee that
can give up the scheduler, so a `setter` without one would be refused by E3073 — the very rule this document
states above.
```maxon
typealias Integer = int(i64.min to i64.max)

var flag = 0

function setter() returns Integer
	flag = 1
	Runtime.yield()
	return 2
end 'setter'

function spinner() returns Integer
	for _ in 0 upto 1000 'spin'
		Runtime.yield()
	end 'spin'
	return flag
end 'spinner'

function main() returns ExitCode
	let p1 = async spinner()
	let p2 = async setter()
	let seen = await p1
	let ack = await p2
	return (seen + ack) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: runtime-yield.nothing-runnable -->
Yielding with nobody to yield to PROCEEDS rather than blocking. The coroutine is the only runnable member of
`main`'s strand — `main` is parked on its await — so each of its yields finds nothing else to run and comes
straight back; the thread runs to completion and its value is awaited normally.
```maxon
typealias Integer = int(i64.min to i64.max)

function lonely() returns Integer
	var i = 0
	while i < 100 'l'
		Runtime.yield()
		i = i + 1
	end 'l'
	return 7
end 'lonely'

function main() returns ExitCode
	let p = async lonely()
	return await p as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: runtime-yield.spin-wait-from-main -->
**A spin-wait on the MAIN thread makes progress.** `main` is a green thread like any other, so a yield taken
there puts it behind its strand's runnable members exactly as a yield from a coroutine does, and `worker`
gets its turn. This is the case that makes `while not ready { Runtime.yield() }` usable
from `main`, and the exit code distinguishes the two outcomes exactly: `8` means the worker ran during the
spin, `7` means a thousand yields went by without it running once.
```maxon
typealias Integer = int(i64.min to i64.max)

var progressed = 0

function worker() returns Integer
	progressed = 1
	Runtime.yield()
	return 7
end 'worker'

function main() returns ExitCode
	let p = async worker()
	for _ in 0 upto 1000 'spin'
		Runtime.yield()
	end 'spin'
	return (progressed + await p) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: runtime-yield.outside-async-context -->
A yield from a program that never spawned anything is INERT — not a crash, and not a hang. This is the case
library code depends on: it may yield without knowing whether its caller is concurrent.
```maxon
function main() returns ExitCode
	Runtime.yield()
	Runtime.yield()
	Runtime.yield()
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: runtime-yield.satisfies-async -->
`Runtime.yield()` is a yield point, so `async` over a function whose only concession to the scheduler is a
yield is ACCEPTED — where the same function without it would be refused with E3073 (`async-await.md`'s
`error.no-yield` pins that refusal). The value still comes back through `await` unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

function computeThenYield(n Integer) returns Integer
	let squared = n * n
	Runtime.yield()
	return squared
end 'computeThenYield'

function main() returns ExitCode
	let p = async computeThenYield(6)
	return await p as ExitCode
end 'main'
```
```exitcode
36
```

<!-- test: runtime-yield.managed-across-yield -->
A managed value held ACROSS a yield survives it and is released exactly once. The green thread suspends and
resumes in the middle of the String's lifetime, so a yield that lost or double-counted the frame's ownership
would show up here as a wrong answer or as a leak (exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)

function held() returns Integer
	let label = "green"
	var i = 0
	while i < 50 'l'
		Runtime.yield()
		i = i + 1
	end 'l'
	return label.count()
end 'held'

function main() returns ExitCode
	let p = async held()
	let n = await p
	let echo = "green"
	Runtime.yield()
	return (n + (echo.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
10
```
