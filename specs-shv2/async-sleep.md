---
feature: async-sleep
status: stable
keywords: [sleep, async, await, green-threads, scheduler, timer, yield, concurrency]
category: concurrency
---

# Sleep — the mid-body yield and netpoller (P1.5)

## Documentation

`sleep(ms)` suspends the **current green thread** for `ms` milliseconds and yields to the scheduler, so
other green threads run while it waits. It is the first **mid-body yield**: unlike a B1a async body, which
runs to completion in one shot, a sleeping thread parks on a timer, hands control back, and RESUMES where it
left off once its deadline has passed.

```text
function main() returns ExitCode
	sleep(10)
	return 0
end 'main'
```

`sleep` works from the main thread (`GT0`) and from a spawned `async` thread alike. When the run queue
empties, the scheduler **netpolls** — it waits on the earliest timer deadline with a real OS sleep (never a
busy-spin) — and re-enqueues each parked thread once its deadline arrives. Because the wait is on the
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

It used to be a BARE-NAME COMPILER BUILTIN instead — a name `Parser.parseCallNamed` recognized before any
registry was consulted, standing in for a stdlib the compiler could not yet load. A call-site-only name is
not a declaration, and the difference showed in two places: the name had no VALUE (`let nap = sleep` was
*"Undefined variable 'sleep'"* where the reference compiler takes the function's address), and a user file
declaring its own `function sleep` compiled with that declaration SILENTLY UNLINKED — `sleep(1)` still
reached the builtin, no diagnostic, a wrong answer. Both are gone: the name is now resolved like every
other, so it has an address, and a second declaration of it is the ordinary whole-program duplicate
(`E3006`, naming `stdlib/Sleep.maxon`).

Everything the builtin enforced by hand is now enforced by the declaration: the argument is checked against
`milliseconds Milliseconds` by the ordinary argument rule, and the absent result by the ordinary void-call
rule. Only the qualified `__Builtins.sleep(ms)` — stdlib's own floor — is still recognized by name.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** `sleep` lowers to `__gt_sleep`, which two lanes now implement — x64-windows and
arm64-macOS — and the others do not, so `SemanticCheck` refuses it with **E3104** on the rest. That is what
the `rejected-on-wasm` and `rejected-on-a-native-target` cases below PIN, and why they carry the inverse marker naming
only the target they are about.

### Deadline order is the TIMER STORE's answer, not the netpoller's

The paragraph above says threads resume in deadline order *because* the netpoll waits on the earliest
deadline. That is a true statement about the netpoller and it was, for a while, the whole of the
implementation's argument — and it is an argument about the CALLER. It says the scheduler usually hands its
timer walk exactly one due deadline at a time; it says nothing about what the walk does when it is handed
several at once.

**Several at once is reachable.** A green thread that never yields holds its M for as long as it runs, so
every deadline that elapses meanwhile is still pending when the scheduler next looks — and so is a machine
under load, a coarse OS timer, or a park that overslept. In that pass the store, not the netpoller, decides
who wakes first.

⇒ **the store is a MIN-HEAP keyed on `(deadline, arm sequence)`, and every pass pops it in that order.** The
rule is total and has no ties left in it: **an earlier deadline resumes first, and among threads whose
deadlines are equal, the one that armed its timer first resumes first.** `coalesced-pass-fires-in-deadline-order`
below is the case that holds it, and it is a RED-GATE pin: the store used to be a flat array fired in
ARMING order, and that case answered `1432` where the rule says `4321`.

## Tests

<!-- test: async-sleep.basic -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The main thread (GT0) sleeps, then returns a value: GT0 parks on the timer, the netpoll waits, and GT0
resumes with its state intact.
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Two async threads sleep for different durations; the shorter-sleep one resumes and observes FIRST. Each
records its completion order into a global (`order = order * 10 + tag`), so `21` proves the fast thread
(tag 2) completed before the slow thread (tag 1) — deadline order, not spawn order.
```maxon
var order = 0

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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
**FOUR DEADLINES DUE IN ONE PASS, ARMED IN EXACTLY THE WRONG ORDER.** The four sleepers are spawned
longest-first, so their arming order (400, 300, 200, 100) is the reverse of their deadline order, and a
fifth thread spawned after them BUSY-WAITS for 600 ms on the clock without ever yielding. Pinned to one
processor there is one M, so nothing polls the timer store while that thread runs: by the time it returns,
all four deadlines have elapsed and a single `__gt_timer_check` is handed the whole set. Each sleeper folds
its tag into a global as it resumes, so the printed number IS the firing order.

⇒ `4321` — deadline order. This is the case `interleave` above cannot reach: there the netpoll wakes on
each deadline separately, so the walk is handed one entry at a time and its own ordering is never asked.
Here it is the only thing being asked, and the flat unsorted store it replaced answered **`1432`** —
firing the 400 ms sleeper first because it had been armed first, then the rest in the order its
swap-removes happened to leave them.

The busy wait is bounded by `Clock`, not by an iteration count, so it holds the M for 600 ms of real time
on a fast machine and a slow one alike; 600 ms clears the longest deadline by 200.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Millis = int(0 to u64.max)

var order = 0

function sleeper(ms Millis, tag Integer) returns Integer
	sleep(ms)
	order = order * 10 + tag
	return tag
end 'sleeper'

function hog(ns DurationNanos) returns Integer
	let started = Clock.nowNanos()
	var spins = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Robustness: fifty spawned threads each sleep then complete, awaited in turn. Each parks (yielded, NOT
completed — its stack must NOT be recycled while parked) then resumes and completes (stack recycled onto the
free-list). The sum proves all fifty ran to completion with no crash, no use-after-free, and no leak.
```maxon
function sleeper() returns Integer
	sleep(2)
	return 1
end 'sleeper'

function main() returns ExitCode
	var i = 0
	var sum = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`sleep` is a DECLARATION, so it has an address: bound to a `let` and called indirectly, it parks the green
thread exactly as the direct call does. A call-site-only builtin name has no value at all — this program was
*"error E2004: Undefined variable 'sleep'"* while one claimed the name, though the reference compiler has
always accepted it.
```maxon
function main() returns ExitCode
	let nap = sleep
	let start = Clock.nowMs()
	nap(60)
	let elapsed = Clock.elapsedMs(start)
	if elapsed >= 40 'slept'
		return 7 as ExitCode
	end 'slept'
	return 1 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-sleep.float-arg-rejected -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`sleep` requires an integer millisecond count; a float is refused at compile time — by the ORDINARY
argument rule against `milliseconds Milliseconds`, which is why the rejection names the parameter and offers
the conversion, and is the same sentence every other narrowing site speaks. The bare-name builtin refused it
with a rule of its own (`E3005: 'sleep' requires a integer, but its argument is float`), which said neither.
```maxon
function main() returns ExitCode
	sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:3:2: argument 'milliseconds': cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: async-sleep.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The timer the park is built on reads `osReadClock` and waits with `osSleepMs`, and neither has an
arm64 or wasm lowering at this rung. A `sleep` compiled for another target is therefore refused at its
own call span with `E3104`, naming the runtime entry it lowers to — not a panic three tiers down in
the backend, which is what it was before this gate:

```text
panic at StdToWasm.maxon:1108: emitBodyOp: `osReadClock` is x64-windows only
```

The refusal is raised INSIDE `stdlib/Sleep.maxon`'s body and attributed to the crossing call, so it is
positioned at the line the user wrote and names the stdlib function they called
(`stdlib-loading.md`) — never at a path inside `stdlib/`.
```maxon
function main() returns ExitCode
	sleep(1)
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:2: this construct is x64-windows only at this rung: 'sleep' lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```

<!-- test: async-sleep.rejected-on-a-native-target -->
<!-- targets: x64-linux -->
The attribution is a property of the crossing, not of one backend: the same program compiled for a NATIVE
target with no timed park is refused at the same user span, naming the same stdlib function and the same
missing runtime entry — which is what makes the point that the wasm case cannot, wasm being the one backend
whose whole OS surface is different.

⚠ **THIS CASE HAS BEEN RE-POINTED TWICE, AND EACH TIME BECAUSE THE LANE IT NAMED GREW THE THING IT PINS.**
It named arm64-macOS until that lane grew a scheduler, then arm64-Linux until L4 gave that lane a
futex-based one; `sleep` now COMPILES AND RUNS on both. The refusal was RE-POINTED rather than deleted
both times, because the rule — a NATIVE target refuses this at the user's own span — is still true and
still needs a non-wasm witness. `x64-linux` is that witness now: a raw static image with no libc has no
`nanosleep` to call and no per-thread slot to publish a processor in, which is the same *"there is no such
primitive here"* the case was written to describe.

⇒ **THE NAME NO LONGER SAYS WHICH TARGET, AND THAT IS THE FIX RATHER THAN AN OMISSION.** It ended
`-on-arm64`, so the target was written down THREE times — the name, the `targets:` marker, and the
`maxoncstderr` text — and a rename was forced on every re-point. The marker and the diagnostic text are
the two copies the runner actually CHECKS AGAINST EACH OTHER, so they cannot silently disagree; a target
in the NAME is the third copy, the one nothing verifies. Whoever gives x64-linux a timed park re-points
the marker and the text, and the name stays true.

⚠ **It is not runnable from a macOS host and does not need to be: a `maxoncstderr` case is COMPILED and
never executed.** MEASURED at this re-point — `--target=x64-linux` emits the identical E3104 on this host
with no WSL. The prose that used to sit here rejected x64-linux for needing WSL, which was a fact about
RUNNING a binary applied to a case that runs none.
```maxon
function main() returns ExitCode
	sleep(1)
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:2: this construct is x64-windows only at this rung: 'sleep' lowers to the runtime entry '__gt_sleep', which has no x64-linux implementation
```

<!-- test: async-sleep.taken-as-a-value-rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
Taking `sleep`'s ADDRESS reaches its body exactly as a call does, so the crossing is refused at the span
where the address is taken — `2:10`, the `sleep` token, not the `f(5)` that consumes the value and not a
line inside `stdlib/`. The value route needs its own pin because it is not a call op at all: the target
gate reaches it through the `functionRef`, which is one of the four edges reachability counts.
```maxon
function main() returns ExitCode
	let f = sleep
	f(5)
	return 7
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:10: this construct is x64-windows only at this rung: 'sleep' lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
```

<!-- test: async-sleep.unreached-compiles-on-wasm -->
<!-- targets: wasm32-wasi -->
An UNREACHED `sleep` now compiles for wasm, and that is a deliberate behaviour change this rung made:
while `sleep` was a bare-name builtin the call was a `__gt_sleep` in USER code, and the target gate is
reachability-BLIND for user code, so `napper` was refused though `main` never calls it
(`builtins-sleep.rejected-on-wasm-when-unreached`, which pins the property at the spelling that still has
it). The declaration moved that runtime entry INTO stdlib source, where the gate is reachability-AWARE —
the same exemption that keeps an unused stdlib module byte-neutral
(`stdlib-loading.unreached-clock-still-compiles-on-wasm`). A `sleep` on a path from `main` is still
refused, as the case above shows.
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
