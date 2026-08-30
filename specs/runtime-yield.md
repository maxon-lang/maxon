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

`main` is the one caller that is not itself a queued green thread — it is the thread the scheduler runs
everything else from — so a yield taken there is not a trip through the queue: it gives the queued work a
turn and comes back. The promise is the same either way, which is why `Runtime.yield()` needs no separate
spelling for it.

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
`sleep(0)` occupies a timer entry for the round trip. `Runtime.yield()` occupies none: it moves the caller
through the run queue instead. A program may therefore yield as often as it likes without competing for the
timer store, which matters precisely when many green threads are yielding at once.

### It is safe outside any async context

Calling it from a program that has never spawned a green thread — including before anything has initialised
the scheduler — is well defined and inert: the call returns and the program continues. This is what lets
library code yield without first asking whether its caller happens to be concurrent.

### It counts as yielding

A function that calls `Runtime.yield()` **yields**, so `async` over it is accepted. `async` requires a
callee that can actually give up the scheduler — a function that only computes is refused with **E3073**
(see `async-await.md`'s `error.no-yield`, which pins that message). `Runtime.yield()` satisfies that check on
the same footing as `sleep`, which is likewise a scheduler park rather than an I/O wait.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** `Runtime.yield()` lowers to the green-thread scheduler, which has no non-Windows
implementation, so `SemanticCheck` refuses it with **E3104** everywhere else.

## Tests

<!-- test: runtime-yield.sibling-runs -->
<!-- targets: x64-windows -->
The discriminating case: a yield really does hand the processor to a SIBLING green thread. `spinner` yields a
thousand times and then reports whether `setter` ever ran; `setter` sets the flag as its first act, so the
only question the exit code answers is whether `spinner`'s yields let it run at all. `3` (`1` seen + `2`
acknowledged) means they did; `2` means a thousand yields went by and the sibling never got a turn.

**Spawn order is what makes it a test rather than a coincidence, and it is deliberately the awkward way
round.** `spinner` is spawned LAST, so it is the thread the scheduler reaches first — a yield that did
nothing would therefore leave `spinner` running until it finished, with `setter` still unstarted and `flag`
still `0`. Spawning the spinner first would prove nothing: `setter` would already have run before the
spinning began, and the case would pass with the yield removed entirely.

That is not hypothetical. This case previously spawned two threads that each appended a digit to a global,
and claimed a particular interleaving proved the handover — but the interleaving followed from spawn order
alone, and the case passed verbatim against a `Runtime.yield()` compiled to `return`. It caught nothing for
as long as it existed. The shape below was checked the only way this claim can be: by making the primitive
inert and confirming the case goes red.

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
	let p1 = async setter()
	let p2 = async spinner()
	let seen = await p2
	let ack = await p1
	return (seen + ack) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: runtime-yield.nothing-runnable -->
<!-- targets: x64-windows -->
Yielding with nobody to yield to PROCEEDS rather than blocking. The spawned thread is the only runnable one,
so each of its yields finds an empty run queue and returns straight away; the thread runs to completion and
its value is awaited normally.
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
<!-- targets: x64-windows -->
**A spin-wait on the MAIN thread makes progress.** `main` is not itself a spawned thread — it is the one the
scheduler runs everything else from — so a yield taken there has to give queued green threads a turn just as a
yield from a spawned thread does. This is the case that makes `while not ready { Runtime.yield() }` usable
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
	return (n + echo.count()) as ExitCode
end 'main'
```
```exitcode
10
```
