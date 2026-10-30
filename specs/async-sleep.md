---
feature: async-sleep
status: stable
keywords: [sleep, async, await, green-threads, scheduler, timer, yield, concurrency]
category: concurrency
---

# Sleep — the mid-body yield and netpoller

## Documentation

`sleep(ms)` suspends the **current green thread** for `ms` milliseconds and yields to the scheduler, so
other green threads run while it waits. It is a **mid-body yield**: unlike an async body that never yields, which
runs to completion in one shot, a sleeping thread parks on a timer, hands control back, and RESUMES where it
left off once its deadline has passed.

```text
function main() returns ExitCode
	sleep(10)
	return 0
end 'main'
```

`sleep` works from `main` and from an `async` coroutine alike — `main` is a green thread like any other.
When a machine has nothing to run, it **netpolls**: it parks until the earliest timer deadline with a real OS
wait (never a busy-spin), and readies each parked thread once its deadline arrives. Because the wait is on the
EARLIEST deadline, threads resume in deadline order: a shorter sleep resumes before a longer one regardless
of spawn order.

The argument is an integer count of milliseconds; a `float`/`String`/`bool` is refused. `sleep` returns
nothing, so its result may not be used in value position.

### `sleep` is an ORDINARY FUNCTION — `stdlib/Sleep.maxon`'s, not a name the compiler claims

`sleep` is declared in `stdlib/Sleep.maxon`, which the stdlib loader brings into every compile, and its
one-line body is written against the `__Builtins.sleep` intrinsic (`builtins-sleep.md`):

```text
typealias Milliseconds = int(0 to u64.max)

export function sleep(milliseconds Milliseconds)
	__Builtins.sleep(milliseconds)
end 'sleep'
```

A declaration is what a call-site-only name is not: the name is resolved like every other, so it has a
VALUE (`let nap = sleep` takes the function's address), and a user file declaring its own `function sleep`
is the ordinary whole-program duplicate (`E3006`, naming `stdlib/Sleep.maxon`) rather than a declaration
silently left unlinked.

The declaration enforces the call's rules: the argument is checked against `milliseconds Milliseconds` by
the ordinary argument rule, and the absent result by the ordinary void-call rule. Only the qualified
`__Builtins.sleep(ms)` — stdlib's own floor — is recognized by name.

**Targets.** `sleep` lowers to `__gt_sleep`, which every lane implements. A native lane parks the green
thread on a timer; `wasm32-wasi` has no green threads, so there a sleep is a blocking wait on a WASI
`monotonic-clock` pollable. The `on-wasm` cases below pin that lowering, and carry the inverse marker
naming only the target they are about.

### Deadline order is the TIMER STORE's answer, not the netpoller's

The paragraph above says threads resume in deadline order *because* the netpoll waits on the earliest
deadline. That is a true statement about the netpoller, and it is an argument about the CALLER. It says the scheduler usually hands its
timer walk exactly one due deadline at a time; it says nothing about what the walk does when it is handed
several at once.

**Several at once is reachable.** A green thread that never yields holds its M for as long as it runs, so
every deadline that elapses meanwhile is still pending when the scheduler next looks — and so is a machine
under load, a coarse OS timer, or a park that overslept. In that pass the store, not the netpoller, decides
who wakes first.

⇒ **the store is a MIN-HEAP keyed on `(deadline, arm sequence)`, and every pass pops it in that order.** The
rule is total and has no ties left in it: **an earlier deadline resumes first, and among threads whose
deadlines are equal, the one that armed its timer first resumes first.** `coalesced-pass-fires-in-deadline-order`
below is the case that holds it, and it is a RED-GATE pin: a flat array fired in ARMING order answers
`1432` there, where the rule says `4321`.

## Tests

<!-- test: async-sleep.basic -->
`main` sleeps, then returns a value: its green thread parks on the timer, the machine waits out the deadline,
and `main` resumes with its state intact.
```maxon
function main() returns ExitCode
	sleep(50)
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: async-sleep.resume-state -->
A spawned green thread's frame survives the mid-body yield: a value live across the `sleep` (the parameter
`base`) is intact after the context switch back into the thread, so `base + 2` is correct.
```maxon

function worker(base Integer) returns Integer
	sleep(30)
	return base + 2
end 'worker'

function main() returns ExitCode
	let p = async worker(40)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-sleep.interleave -->
Two async threads sleep for different durations; the shorter-sleep one resumes and observes FIRST. Each
records its completion order into a global (`order = order * 10 + tag`), so `21` proves the fast thread
(tag 2) completed before the slow thread (tag 1) — deadline order, not spawn order.
```maxon
var order = 0 as Integer

function slow() returns Integer
	sleep(100)
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns Integer
	sleep(10)
	order = order * 10 + 2
	return 2
end 'fast'

function main() returns ExitCode
	let p1 = async slow()
	let p2 = async fast()
	_ = await p1
	_ = await p2
	return order as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: async-sleep.coalesced-pass-fires-in-deadline-order -->
<!-- procs: 1 -->
**FOUR DEADLINES DUE IN ONE PASS, ARMED IN EXACTLY THE WRONG ORDER.** The four sleepers are spawned
longest-first, so their arming order (400, 300, 200, 100) is the reverse of their deadline order, and a
fifth thread spawned after them BUSY-WAITS for 600 ms on the clock without ever yielding. Pinned to one
processor there is one M, so nothing polls the timer store while that thread runs: by the time it returns,
all four deadlines have elapsed and a single `__gt_timer_check` is handed the whole set. Each sleeper folds
its tag into a global as it resumes, so the printed number IS the firing order.

⇒ `4321` — deadline order. This is the case `interleave` above cannot reach: there the netpoll wakes on
each deadline separately, so the walk is handed one entry at a time and its own ordering is never asked.
Here it is the only thing being asked, and a flat unsorted store fired in arming order answers
**`1432`** — firing the 400 ms sleeper first because it was armed first, then the rest in the order its
swap-removes happen to leave them.

The busy wait is bounded by `Clock`, not by an iteration count, so it holds the M for 600 ms of real time
on a fast machine and a slow one alike; 600 ms clears the longest deadline by 200.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Millis = int(0 to u64.max)

var order = 0 as Integer

function sleeper(ms Millis, tag Integer) returns Integer
	sleep(ms as Milliseconds)
	order = order * 10 + tag
	return tag
end 'sleeper'

function hog(ns DurationNanos) returns Integer
	let started = Clock.nowNanos()
	var spins = 0 as Integer
	while Clock.elapsedNanos(started) < ns 'spin'
		spins = spins + 1
	end 'spin'
	return spins
end 'hog'

function main() returns ExitCode
	let p1 = async sleeper(400, tag: 1)
	let p2 = async sleeper(300, tag: 2)
	let p3 = async sleeper(200, tag: 3)
	let p4 = async sleeper(100, tag: 4)
	let busy = async hog(600000000)
	_ = await p1
	_ = await p2
	_ = await p3
	_ = await p4
	_ = await busy
	print("order={order}")
	return 0
end 'main'
```
```stdout
order=4321
```
```exitcode
0
```

<!-- test: async-sleep.zero -->
`sleep(0)` parks on a deadline of "now" and resumes promptly (the netpoll fires it on the first poll).
```maxon
function main() returns ExitCode
	sleep(0)
	return 5
end 'main'
```
```exitcode
5
```

<!-- test: async-sleep.spawn-loop -->
Robustness: fifty spawned threads each sleep then complete, awaited in turn. Each parks (yielded, NOT
completed — its stack must NOT be recycled while parked) then resumes and completes (stack recycled onto the
free-list). The sum proves all fifty ran to completion with no crash, no use-after-free, and no leak.
```maxon
function sleeper() returns Integer
	sleep(2)
	return 1
end 'sleeper'

function main() returns ExitCode
	var i = 0 as Integer
	var sum = 0 as Integer
	while i < 50 'l'
		let p = async sleeper()
		let r = await p
		sum = sum + r
		i = i + 1
	end 'l'
	return sum as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
50
```

<!-- test: async-sleep.taken-as-a-value -->
`sleep` is a DECLARATION, so it has an address: bound to a `let` and called indirectly, it parks the green
thread exactly as the direct call does. A call-site-only builtin name would have no value at all.
```maxon
function main() returns ExitCode
	let nap = sleep
	let start = Clock.nowMs()
	nap(60)
	let elapsed = Clock.elapsedMs(start)
	if elapsed >= 40 'slept'
		return 7
	end 'slept'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: async-sleep.float-arg-rejected -->
`sleep` requires an integer millisecond count; a float is refused at compile time — by the ORDINARY
argument rule against `milliseconds Milliseconds`, which is why the rejection names the parameter and offers
the conversion, and is the same sentence every other narrowing site speaks.
```maxon
function main() returns ExitCode
	sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:3:2: argument 'milliseconds': cannot implicitly convert 'float' to 'Milliseconds': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: async-sleep.runs-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
On wasm32-wasi `sleep` is a blocking wait on a WASI `monotonic-clock` pollable, so the monotonic clock
has moved at least the requested 20 ms when it returns.
```maxon
function main() returns ExitCode
	let start = Clock.nowNanos()
	sleep(20)
	if Clock.nowNanos() - start >= 20000000 'sleptTheWholeTime'
		return 4
	end 'sleptTheWholeTime'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: async-sleep.taken-as-a-value-runs-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
Taking `sleep`'s ADDRESS reaches its body exactly as a call does, through the `functionRef` rather than a
call op, and on wasm32-wasi the function it takes runs: the program sleeps through the value and exits.
```maxon
function main() returns ExitCode
	let f = sleep
	f(5)
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: async-sleep.unnamed-sleep-compiles-on-wasm -->
A program that never names `sleep` compiles for wasm with `stdlib/Sleep.maxon` loaded: a stdlib body no
path from `main` reaches is not in the program, the same exemption that keeps an unused stdlib module
byte-neutral (`stdlib-loading.unreached-host-sibling-still-compiles-on-wasm`).
```maxon
function main() returns ExitCode
	print("awake\n")
	return 4
end 'main'
```
```exitcode
4
```
```stdout
awake
```

<!-- test: async-sleep.unreached-call-compiles-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A `sleep(1)` in a function nothing calls compiles for wasm32-wasi: the target gate visits the program's
own calls whether or not `main` reaches them, and `__gt_sleep` has a wasm lowering, so it has nothing to
refuse.
```maxon
function napper()
	sleep(1)
end 'napper'

function main() returns ExitCode
	return 4
end 'main'
```
```exitcode
4
```
