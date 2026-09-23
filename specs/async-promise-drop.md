---
feature: async-promise-drop
status: stable
keywords: [async, await, promise, green-threads, drop, cancel, ownership, leak, E3100]
category: concurrency
---

# Async / Await — un-awaited Promise drop-reclaim + cancel (P1.5-B2 #88)

## Documentation

An `async` spawn's `Promise` is an **owned value**: it owns the green thread it names. `await p` **consumes**
that thread (the runtime reclaims its struct at the await). A promise that reaches scope exit — or a re-arm —
**without** being awaited is instead **DROPPED**: the compiler emits `__gt_promise_drop(p)`, which reclaims
the green thread and renounces its result. This is the ownership dual of the linear-await rule (E3100):
`await` is the consuming move, and any path with no consuming await drops the thread exactly once.

Dropping is not an exit-time drain: a thread that has not STARTED is **cancelled** and its body **never
runs**, while a thread that has started carries on and only its RESULT is renounced — except that it may
no longer BEGIN an operation that waits on a far end, which answers that operation's own failure variant
at once rather than parking for a wake-up the spent drop will never send. A started
thread parked on a wait (a `sleep` timer, a `runProcess` child) has that wait ended early — it is readied,
resumes, unwinds its own frame, and its strand's runner reclaims it — because nothing can unwind a suspended
frame from outside, and freeing its stack would strand every heap value its locals own. Because
a green thread's struct and stack are slab/OS allocations invisible to the `__mm` heap leak gate, a spawned
thread that is neither awaited nor dropped leaks silently — so the runtime keeps a `__gt_live_count` (one up
per spawn, one down per await-reclaim AND per drop-reclaim) and the one OS-exit leak gate asserts it is zero.
A leak (or an over-reclaim) reports `RuntimeAbort.greenThreadLeak` (75), distinct from the heap gate's 101.

`__gt_promise_drop` branches on the thread's state: a `completed` thread (which may have run while its owner
was parked on a DIFFERENT await) has already had its stack freed, so only its struct is reclaimed; a `ready`
(never-run) thread is renounced where it sits in its strand's queue, and whoever pops it reclaims it and frees
its seed stack instead of running it; a `waiting` (parked) thread is taken off whatever would have woken it —
its timer entry, or the poll descriptor a socket or a child made it a waiter on — and READIED, so it resumes,
runs its body out and is reclaimed by its runner like a queued thread. A Windows overlapped read the kernel is
still serving is not taken off the poller: the drop cancels the operation with `CancelIoEx`, and the thread is
readied by that operation's own completion.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Dropping a promise reaps a green-thread struct and releases its stack through
`osFreePages`/`VirtualFree`, which every native lane provides; a WASI component is refused by E3104,
which is why the cases here carry no `unsupported-targets` marker for it. A case marked for one lane
family is marked because its CHILD COMMAND is a shell's or `cmd`'s, never because the drop is.

## Tests

<!-- test: async-promise-drop.never-ran-drop-no-leak -->
A spawned green thread that is never awaited is DROPPED at scope exit: it is renounced where it sits in its
strand's queue, so it is reclaimed — seed stack and struct — rather than run, and `__gt_live_count` balances to
zero — so the program exits with `main`'s own code (0), not the GT-leak abort (75). Before #88 this spawn leaked
its struct + stack silently.
```maxon

function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	_ = async trivial()
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.completed-sibling-drop-no-leak -->
A sibling promise that COMPLETES while another is awaited, and is then dropped — its struct is reclaimed
WITHOUT double-freeing the stack (the runner freed it at completion). While `main` is parked on `await b` its
strand runs `a` and `b` FIFO, so `a` has completed by the time `main` resumes; the peek answers `1` for it, and
`a` is dropped at scope exit. `20 + 1 = 21`: the awaited sibling's result is intact, and the live count balances
to zero.

⚠ **`a` MUST BE BOUND.** `_ = async ten()` discards the promise at its own statement, before `main` parks — so
`ten` never runs and the drop reclaims a never-run thread, which is `never-ran-drop-no-leak` again. The peek
pins that `a` really had completed when it was dropped: a thread that has not run reads `0`.
```maxon

function ten() returns Integer
	Scheduler.yield()
	return 10
end 'ten'

function twenty() returns Integer
	Scheduler.yield()
	return 20
end 'twenty'

function main() returns ExitCode
	let a = async ten()
	let b = async twenty()
	let r = await b
	return (r + __Builtins.gtIsComplete(a.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: async-promise-drop.never-ran-drop-not-run -->
Pins the divergence from a fire-and-forget-run model: a dropped thread's body NEVER RUNS. `incFlag` would set
the global to 1 if it ran, but `p` is never awaited and `main` never parks or yields, so its strand never
reaches `incFlag` and the global stays 0. The drop cancels the never-run thread; it does not run it.
```maxon
var flag = 0

function incFlag() returns Integer
	Scheduler.yield()
	flag = 1
	return 1
end 'incFlag'

function main() returns ExitCode
	_ = async incFlag()
	return flag as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.spawn-drop-loop-bounded -->
The real #88 leak shape: a loop that spawns a promise every iteration and never awaits it. Each iteration's
promise is dropped at the loop body's scope exit — renounced in its strand's queue (so it is reclaimed rather
than run when the queue reaches it) and its struct recycled onto the free-list, which the next spawn reuses.
Memory stays bounded across 1000 iterations and the live count balances to zero (exit 0). Before #88 each
iteration bump-leaked its struct and seed stack — neither of them a box, so both are invisible to the heap
leak gate's tracked live column, and `__gt_live_count` is the gate that catches them.
```maxon

function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	var i = 0
	while i < 1000 'loop'
		_ = async trivial()
		i = i + 1
	end 'loop'
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.parked-timer-drop-cancel -->
A promise PARKED on a timer is dropped at scope exit. `sleeper` sleeps 200 ms; `fast` completes
immediately. `await q` parks `main`, and its strand runs its other members FIFO: `sleeper` runs first and parks
on its timer; `fast` then runs to completion, so `await q` returns 42 while `sleeper` is still parked — the peek
adds `0`, where a completed thread would add `1`. Scope exit drops `s` — the `waiting` arm removes it from the
timer store and readies it, so its `sleep` ends early, it runs its body out and its runner reclaims it — with NO
hang (the 200 ms deadline is never waited on) and NO use-after-free (no timer fire touches the reclaimed
thread). The live count balances to zero.

⚠ **`s` MUST BE BOUND.** `_ = async sleeper()` discards the promise at its own statement, before `main` parks:
`sleeper` never runs, never arms its timer, and the drop takes the QUEUED arm — a program that exercises
nothing the `waiting` arm does, and still exits 42.
```maxon

function sleeper() returns Integer
	sleep(200)
	return 99
end 'sleeper'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let s = async sleeper()
	let q = async fast()
	let r = await q
	return (r + __Builtins.gtIsComplete(s.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.parked-timer-drop-through-a-rearm -->
The same deregistration reached through the OTHER door that drops a promise: a RE-ARM rather than scope exit.
`await q` parks `main`, so the strand runs `sleeper` up to its `sleep(200)` and parks it on the timer; only
THEN does `p = async fast()` renounce it. The `waiting` arm removes it from the store and readies it, so it
resumes on the stack it is suspended on, runs to completion and is reclaimed by its runner — with no hang (the
200 ms deadline is never waited on) and no use-after-free.

⭐ Its own RED reading: point `__gt_promise_drop`'s park-kind refusal at `GtParkKindTimer` instead of
`GtParkKindMailbox` and this exits **94** where it exits 42.
```maxon

function sleeper() returns Integer
	sleep(200)
	return 99
end 'sleeper'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	var p = async sleeper()
	let q = async fast()
	let r = await q
	p = async fast()
	let s = await p
	return (r + s - 42) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.branch-await-one-drop-other -->
The path-sensitive case: a promise `await`ed on ONE branch and DROPPED on the other, reconciled at the merge.
`pick` spawns `p`, then on `x > 0` awaits it (the runtime reclaims the struct at the await) and on the else
path lets it drop at scope exit (the `ready` arm cancels the never-run thread). Compiling ONE body with both
fates — the awaited binding is `movedFrom`, the else path is live — must drop `p` on exactly the else path and
NOT double-drop the awaited one. `pick(1)` returns 7 (awaited); `pick(0)` returns 0 (dropped, never ran); their
sum is 7, and the live count balances to zero across both calls (no GT-leak abort 75).
```maxon

function compute() returns Integer
	Scheduler.yield()
	return 7
end 'compute'

function pick(x Integer) returns Integer
	let p = async compute()
	if x > 0 'branch'
		return (await p)
	end 'branch'
	return 0
end 'pick'

function main() returns ExitCode
	let a = pick(1)
	let b = pick(0)
	return (a + b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: async-promise-drop.parked-subprocess-drop-cancel -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The CHILD twin of the parked-timer case, and the same program one op at a time. `slowProc` spawns a child
that runs for ~2 s and parks on the poll source that child became; `fast` completes immediately, so `await q`
returns 42 while `slowProc` is still parked on its child — the `gtIsComplete` peek adds `0`, which is that
thread saying so. Scope exit drops `s` — the `waiting` arm takes it off that source and RENOUNCES it, so the
thread resumes, gives the source back, closes the child's handle (abandon the WAIT, do not kill the child) and
unwinds its own frame. The abandoned child runs to completion independently; the program exits promptly with 42
and the live count balances to zero.

⚠ **`s` MUST BE BOUND, AND THIS CASE READ `_ = async slowProc()` WHILE DESCRIBING THE `waiting` ARM.** That
spelling discards the promise at its own statement, before `main` ever parks: the committed fragments showed
`__gt_spawn` → `__gt_ready` → `__gt_promise_drop` back to back, so `slowProc` never ran, never spawned a
child, and the drop took the QUEUED arm — the identical defect `parked-timer-drop-cancel`'s own warning
records, in the case written against it. **It answered 42 by testing nothing**, which is why the expectation
here is unchanged and the program is not.

⚠ **AND IT CARRIED NO `unsupported-targets` MARKER WHILE SPAWNING THROUGH `cmd /c`**, so on the three POSIX
lanes `/bin/sh -c "cmd /c ping …"` failed to exec and returned at once — nothing to park on even had the
promise been bound. `posix-parked-subprocess-drop-cancel` is this case's real sibling on those lanes.
```maxon

function slowProc() returns Integer
	return try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let s = async slowProc()
	let q = async fast()
	let r = await q
	return (r + __Builtins.gtIsComplete(s.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.posix-parked-subprocess-drop-cancel -->
<!-- unsupported-targets: x64-windows -->
`parked-subprocess-drop-cancel` on the POSIX lanes, where `/bin/sh -c "sleep 2"` is a child that really does
outlive the `await`. The road is the same one: a child park is a poll source here too, so the `waiting` arm
renounces rather than freeing the stack under a suspended thread.
```maxon

function slowProc() returns Integer
	return try __Builtins.runProcess("sleep 2") otherwise 99
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let s = async slowProc()
	let q = async fast()
	let r = await q
	return (r + __Builtins.gtIsComplete(s.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.parked-subprocess-drop-reclaims-the-frames-heap -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐ **THE DROPPED COROUTINE UNWINDS ITS OWN FRAME, SO THE HEAP ITS LOCALS OWN GOES BACK.** `slowProc` holds
one interpolated `String` across a park on its child, and `main` drops the promise while it is still parked.
Renouncing resumes the thread on the source it was waiting on and lets it unwind, which releases that
`String`; freeing the parked stack outright strands it, and the leak gate reports that as **exit 101** —
not a wrong answer, which is why the symptom is an exit code no arithmetic in the program can produce.

⚠ **`parked-subprocess-drop-cancel` DOES NOT COVER THIS, AND THE ONE INTERPOLATED `String` IS THE WHOLE
DIFFERENCE.** Its coroutine holds nothing, so a drop that frees the parked stack and a drop that unwinds it
answer 42 alike — there is nothing on that stack to strand. It also drops at the `_ =` site, before the
coroutine has run; binding the promise to `p` and dropping it at scope exit is what puts the drop AFTER the
park. `posix-parked-subprocess-drop-reclaims-the-frames-heap` is this case on the POSIX lane and carries the
measurement.
```maxon

function slowProc(tag Integer) returns Integer
	let held = "held-{tag}"
	let code = try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99
	return code + held.byteLength()
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let p = async slowProc(1)
	let q = async fast()
	let r = await q
	return (r + __Builtins.gtIsComplete(p.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.posix-parked-subprocess-drop-reclaims-the-frames-heap -->
<!-- unsupported-targets: x64-windows -->
The same shape on the POSIX lane, where `/bin/sh -c "sleep 2"` is a child that really does outlive the
`await`. ⚠ Its sibling above spawns through `cmd /c`, which on this lane fails to exec and returns before
anything can park — so this is the case that actually holds a coroutine parked on a child here.

A child park is a poll source on this lane too, so the `waiting` arm renounces the thread and it unwinds its
own frame, releasing the interpolated `String`. A drop that freed the stack the suspended thread sits on would
strand that `String`, and the leak gate would report **exit 101**.
```maxon

function slowProc(tag Integer) returns Integer
	let held = "held-{tag}"
	let code = try __Builtins.runProcess("sleep 2") otherwise 99
	return code + held.byteLength()
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let p = async slowProc(1)
	let q = async fast()
	let r = await q
	return (r + __Builtins.gtIsComplete(p.inner)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.windows-parked-pipe-read-drop-reclaims-the-frames-heap -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐ **A PIPE READ IS THE PARK KIND WHOSE WAIT THE RUNTIME CANNOT END ITSELF.** Its siblings above park on a
CHILD, which is a poll source the drop takes the thread off. A streaming read parks on an overlapped read the
kernel is still serving, so the drop cancels that operation with `CancelIoEx` and leaves the park standing:
the cancelled operation's completion readies the thread, it answers a failed read, unwinds its own frame and
its runner reclaims it. `reader` holds one interpolated `String` across the read; a drop that freed the parked
stack instead would strand it, and the leak gate would report **exit 101**, an exit code no arithmetic in the
program can produce.

⚠ **THE PROMISE MUST BE BOUND AND THE SLEEP IS WHAT PUTS THE DROP AFTER THE PARK.** `_ = async reader(h)`
discards at its own statement, before the coroutine has run, and takes the never-ran arm instead. The
`gtIsComplete` peek adds `0`, which is that thread saying it was still parked when `dropWhileParked`
returned and dropped it.
```maxon
function reader(h Integer, tag Integer) returns Integer
	let held = "held-{tag}"
	let line = subpReadLine(h)
	return line.byteLength() + held.byteLength()
end 'reader'

function dropWhileParked(h Integer) returns Integer
	let p = async reader(h, tag: 1)
	sleep(200)
	return __Builtins.gtIsComplete(p.inner)
end 'dropWhileParked'

function main() returns ExitCode
	let h = subpSpawn("cmd /c ping -n 3 127.0.0.1 >nul & echo hi")
	let parked = dropWhileParked(h)
	_ = subpWait(h)
	subpRelease(h)
	return (42 + parked) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.a-dropped-coroutine-still-running-holds-the-exit -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
⭐ **THE EXIT DRAIN READS QUEUES, STORES AND PROMISES — AND A COROUTINE A MACHINE HAS ALREADY DEQUEUED IS
IN NONE OF THEM.** A drop renounces the coroutine and debits the live count at once, so the promise term is
already zero; from the moment a machine takes the readied token until that thread parks again or completes,
it sits on no queue and in no wait store. `victim` holds one interpolated `String` and spends that window
inside a child spawn — `SyscallClass.blocking`, tens of milliseconds in the kernel — and only its own unwind
can release the `String`. A drain that leaves during the window joins the machine out from under it and the
leak gate reports **exit 101**, an exit code no arithmetic here can produce.

⚠ **THE DROP HAPPENS INSIDE A COROUTINE, AND THAT IS WHAT MAKES IT DETERMINISTIC RATHER THAN A 4% RACE.**
What decides the outcome is which machine takes the readied token: the main machine taking it is inside the
strand runner and never reaches the exit test at all. Dropping from `dropper` readies the victim into a
WORKER's ring while the main machine is only being woken, so the vulnerable road is the one taken every
time. Dropping from `main` instead reads green roughly nineteen runs in twenty, which is a gate that cannot
be trusted rather than a gate that cannot fail.
```maxon
function victim(tag Integer) returns Integer
	let held = "held-{tag}"
	sleep(5000)
	_ = spawnReadLine("cmd /c echo bye")
	return held.byteLength()
end 'victim'

function dropper() returns Integer
	let p = async victim(1)
	sleep(200)
	return 7 + __Builtins.gtIsComplete(p.inner)
end 'dropper'

function main() returns ExitCode
	_ = spawnReadLine("cmd /c echo hello")
	let d = async dropper()
	let n = await d
	return (35 + n) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.rearm-var-across-loop -->
Re-arming a promise `var` INSIDE A LOOP is not phi-blind (P1.5-B2 #88, review Finding 1): the loop-header phi
carries the promise mark, so each iteration DROPS the previous thread (cancelling it) and the last one drops at
scope exit — the live count balances to zero (exit 0). Before the fix the loop body emitted no drop (the phi was
unmarked, so the re-arm saw no live thread to drop) and the scope-exit drop misrouted to `__mm_decref` on a GT
pointer, corrupting the heap count (exit 101).
```maxon
function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	var p = async trivial()
	var i = 0
	while i < 8 'loop'
		p = async trivial()
		i = i + 1
	end 'loop'
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.rearm-var-across-branch -->
Re-arming a promise `var` in ONE ARM OF A BRANCH is likewise not phi-blind (Finding 1): the if-continuation phi
merges the re-armed thread and the untouched one, both marked promises, so the scope-exit drop cancels whichever
the taken path holds — balanced on every path (exit 0). Before the fix this exited 101.
```maxon
function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function positive() returns bool
	return true
end 'positive'

function main() returns ExitCode
	var p = async trivial()
	if positive() 'b'
		p = async trivial()
	end 'b'
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.await-rearmed-var -->
AWAITING a promise `var` re-armed across a branch works and yields the re-armed thread's result (Finding 1): the
merge phi is recognised as a promise, and its awaited result type is recovered by tracing the phi's incoming
`async` calls. `positive()` is true, so `p` holds the re-armed thread; `await p` returns 7 and both threads are
accounted for (the original dropped at the re-arm, the re-armed one awaited). Before the fix `await p` on the
phi was rejected E2015 ("not a promise").
```maxon
function seven() returns Integer
	Scheduler.yield()
	return 7
end 'seven'

function positive() returns bool
	return true
end 'positive'

function main() returns ExitCode
	var p = async seven()
	if positive() 'b'
		p = async seven()
	end 'b'
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: async-promise-drop.await-rearmed-loop-var -->
AWAITING a promise `var` re-armed INSIDE A LOOP, from AFTER the loop — the loop-EXIT phi (P1.5-B2 #88; this
closes residual #89's E2015 over-rejection of an `await` on a block-arg-carried promise). `p` is re-armed each
iteration (dropping the previous thread); after the loop `await p` targets the loop-exit phi, whose promise mark
and awaited result type are recovered by tracing the phi's incoming `async` calls. It returns the LAST spawn's
result (5), every intermediate thread dropped and the live count balanced. Before #88 the phi-carried promise
was rejected E2015 ("not a promise") at the `await`.
```maxon
function five() returns Integer
	Scheduler.yield()
	return 5
end 'five'

function main() returns ExitCode
	var p = async five()
	var i = 0
	while i < 3 'loop'
		p = async five()
		i = i + 1
	end 'loop'
	return (await p) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: async-promise-drop.error.rearm-aliased-thread -->
Re-arming a promise `var` whose thread is still named by a LIVE ALIAS is refused (Finding 2): `let q = p` gives
the thread a second name, so re-arming `p` would drop it while `q` still names it, and `await q` would then
reclaim freed memory — a use-after-free E3100 cannot catch (it is not a double await). A compile error, not a
miscompile. (`double-await-alias-outlives-rebind` stays E3100 because there the thread is AWAITED before the
re-arm, so the re-arm drops nothing.)
```maxon
function compute() returns Integer
	return 7
end 'compute'

function main() returns ExitCode
	var p = async compute()
	let q = p
	p = async compute()
	let r = await q
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2015: <fragment>:9:2: Unsupported: cannot re-arm the promise binding ('p'): its green thread is still named by an alias, so dropping it here would leave that alias dangling — `await` through one name before re-arming
```

<!-- test: async-promise-drop.error.await-then-reassign-nonpromise -->
⛔ **A `var` DOES NOT CHANGE TYPE, AND A PROMISE BINDING IS NO EXCEPTION.** This case used to RUN, and it
ran only because a promise had no type of its own: the binding was declared from `async nine()`, which minted
a bare machine word, so `p` was an `int` binding and `p = 5` was an ordinary scalar reassignment. Once the
spawn is typed (`W230`), `p` holds a `Promise with Integer` for its whole life and assigning an `int` to it is
the same refusal assigning an `int` to any other declared type earns.

⚠ **THE OWNERSHIP FACT IT USED TO PIN IS UNCHANGED AND IS STILL PINNED** — that an AWAITED promise's binding
owns nothing, so nothing is dropped at scope exit and no use-after-move is reported. That is what
`rearm-var-across-loop`, `rearm-var-across-branch` and `await-rearmed-var` test, with a re-armed PROMISE
rather than a scalar, which is the only re-arm the type now admits. What is gone is a spelling, not a rule.
```maxon
function nine() returns Integer
	Scheduler.yield()
	return 9
end 'nine'

function main() returns ExitCode
	var p = async nine()
	let r = await p
	print("{r}")
	p = 5
	return p as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'int' to variable 'p', which holds 'struct'
```

<!-- test: async-promise-drop.branch-store-into-a-container-on-both-arms -->
⭐ **STORING THE SPAWN IS A CONSUMING MOVE ON *EVERY* ARM THAT DOES IT, INCLUDING THE SECOND ONE THE PARSER
READS.** A dispatcher arms a slot through two doors — `push` the first time a slot exists, `set` every time
after — so ONE `async` spawn reaches a merge having been given to the container on both paths, and the merge
must therefore reconcile NOTHING. If either door fails to record the move, `reconcileMovesAtMerge` sees the
binding live on that edge, emits `__gt_promise_drop` there, and the container is left holding a **cancelled**
thread: it never completes, so a non-blocking poller waits on it forever. `arm` is called twice, taking a
different door each time; each stored thread must run to completion while the bounded yield loop
(`completesUnderTheDrive`) gives it turns, and be awaitable for its value, so the sum is 42. The bound is what
makes a cancelled thread a VERDICT (exit 2) instead of a hang.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

let MaxSpins = 200

function value(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'value'

function arm(slots IntPromiseArray, slot Integer, n Integer)
	let p = async value(n)
	if slot == slots.count() 'firstUse'
		slots.push(p)
	end 'firstUse' else 'reuse'
		try slots.set(slot, value: p) otherwise panic("the slot exists — this arm is only taken when it does")
	end 'reuse'
end 'arm'

function completesUnderTheDrive(slots IntPromiseArray) returns bool
	var spins = 0
	while spins < MaxSpins 'drive'
		Scheduler.yield()
		let p = try slots.get(0) otherwise panic("slot 0 was armed before the drive")
		if __Builtins.gtIsComplete(p.inner) != 0 'complete'
			return true
		end 'complete'
		spins = spins + 1
	end 'drive'
	return false
end 'completesUnderTheDrive'

function main() returns ExitCode
	var slots = IntPromiseArray.create()

	arm(slots, slot: 0, n: 11)
	if not completesUnderTheDrive(slots) 'pushedThreadCancelled'
		return 1 as ExitCode
	end 'pushedThreadCancelled'
	let first = try slots.get(0) otherwise panic("slot 0 was just armed")
	var sum = await first

	arm(slots, slot: 0, n: 31)
	if not completesUnderTheDrive(slots) 'setThreadCancelled'
		return 2 as ExitCode
	end 'setThreadCancelled'
	let second = try slots.get(0) otherwise panic("slot 0 was just re-armed")
	sum = sum + (await second)

	return sum as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-promise-drop.branch-store-into-a-container-reads-the-doors-in-either-order -->
The mirror of the case above, and the reason it is a second case rather than a second assertion: the defect it
pins was ORDER-DEPENDENT — the first store door the parser read retyped the spawn's value to the storage
instance, and the SECOND one then mistook it for a promise already read back out of a container and skipped
the move. So the arms are written the other way round here, `set` first and `push` second, which makes `push`
the door that is read second and taken FIRST at run time. Same two threads, same 42: whichever door the parser
happens to read second must still consume the thread it stores.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

let MaxSpins = 200

function value(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'value'

function arm(slots IntPromiseArray, slot Integer, n Integer)
	let p = async value(n)
	if slot < slots.count() 'reuse'
		try slots.set(slot, value: p) otherwise panic("the slot exists — this arm is only taken when it does")
	end 'reuse' else 'firstUse'
		slots.push(p)
	end 'firstUse'
end 'arm'

function completesUnderTheDrive(slots IntPromiseArray) returns bool
	var spins = 0
	while spins < MaxSpins 'drive'
		Scheduler.yield()
		let p = try slots.get(0) otherwise panic("slot 0 was armed before the drive")
		if __Builtins.gtIsComplete(p.inner) != 0 'complete'
			return true
		end 'complete'
		spins = spins + 1
	end 'drive'
	return false
end 'completesUnderTheDrive'

function main() returns ExitCode
	var slots = IntPromiseArray.create()

	arm(slots, slot: 0, n: 11)
	if not completesUnderTheDrive(slots) 'pushedThreadCancelled'
		return 1 as ExitCode
	end 'pushedThreadCancelled'
	let first = try slots.get(0) otherwise panic("slot 0 was just armed")
	var sum = await first

	arm(slots, slot: 0, n: 31)
	if not completesUnderTheDrive(slots) 'setThreadCancelled'
		return 2 as ExitCode
	end 'setThreadCancelled'
	let second = try slots.get(0) otherwise panic("slot 0 was just re-armed")
	sum = sum + (await second)

	return sum as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-promise-drop.a-container-drops-its-un-awaited-elements -->
⭐ The container is an OWNER. An array of promises that reaches scope exit holding un-awaited elements
drops each one — the same `__gt_promise_drop` a bare binding gets, reached through the element
destructor the array record stamps. Before this slice the record stamped `element_destroy@40 = 0`,
because the compiler classed a promise as owing nothing, and the array died taking its elements'
threads with it, unreclaimed: `__gt_live_count` stayed at 1 and the program exited **75**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		return 0 as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.an-element-moved-out-by-pop-belongs-to-the-caller -->
`pop` MOVES the element out: the array no longer holds it and the caller's binding does, so the binding
drops it at scope exit like any other owned promise. Before this slice a popped promise was owned by
NOBODY — the array had already forgotten it and the binding never adopted it — so both the promise's
box and its green thread leaked. The heap gate is checked first, so the symptom was **101**, with the
green-thread leak (75) hiding behind it. `.inner` peeks at the handle without consuming it, which is
what lets this case observe the promise at all without awaiting it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		let p = try s.pop() otherwise panic("just pushed one")
		print("popped a thread {p.inner > 0}, array now {s.count()}")
		return 0 as ExitCode
end 'main'
```
```stdout
popped a thread true, array now 0
```
```exitcode
0
```

<!-- test: async-promise-drop.a-struct-field-drops-the-promise-it-holds -->
A struct FIELD is an owner too, and the struct's synthesized destructor drops it. This case could not
even be WRITTEN before promises were typed: `Holder.of(async plain())` was refused (`expected
'IntPromise', got 'int'`) because the spawn was a bare machine word, so the only way a promise ever
reached a field was through a container read — and once it did, nothing dropped it. Typing the promise
opens the position; stamping the field destructor is what makes opening it safe.

⚠ **IT AWAITS THE FIELD RATHER THAN PEEKING IT, AND THE DIFFERENCE IS THE WHOLE CASE.** An earlier draft
observed the promise with `h.p.inner > 0` and PASSED the moment the spawn was typed — for the wrong
reason. The spawn is a statement-scoped pending temporary, so storing it into a field without MOVING it
leaves the drain to cancel the thread at the end of that statement; the field then holds a dead handle,
and a peek reads a dangling pointer that is still non-zero. `await h.p` is what tells the two apart:
against the cancelled thread it aborts (**exit 92**), and only a field that really owns a live promise
answers 7.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

type Holder
		public let p as IntPromise

		static function of(p IntPromise) returns Holder
				return Holder{p: p}
		end 'of'
end 'Holder'

function main() returns ExitCode
		let h = Holder.of(async plain())
		return (await h.p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.error.a-promise-element-read-through-a-borrow-door -->
⭐ **A CORPUS MEMBER MAY HAND A PROMISE ELEMENT OVER ONLY IF IT STOPPED HOLDING IT.** `last()` reads the
element and leaves it in the array. For a refcounted element that is sound — the read RETAINS, so both
holders own a reference — and for a green thread it is impossible: there is no second reference to take,
so the array and the caller would each reclaim the same thread.

⛔ **THIS WAS THE ONE CASE THE ELEMENT STAMP MADE WORSE BEFORE IT WAS REFUSED, WHICH IS WHY THE REFUSAL IS
PART OF THE SAME CHANGE.** Measured: `try s.last() … ; await l` exited **7** while an `Array with Promise`
dropped nothing, and **75** once it dropped — a leak turning into a double free. A container of promises
now refuses the read instead.

⚠ **THE RULE IS DECIDED AT THE CALL SITE AND IT HAS TO BE.** `stdlib/Array.maxon` is compiled ONCE over an
opaque `Element`, so inside `last()` the element is a type parameter and nothing about green threads is
true of it yet; the caller is the only place the element type is concrete.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		let l = try s.last() otherwise panic("has one")
		return (await l) as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:14:17: a promise cannot be borrowed through 'last': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. Read it through a door that names its slot (`get(i)`, `first()`, `for … in`, or an array's cursor), or move it out with `pop`/`remove`
```

<!-- test: async-promise-drop.a-cursor-over-a-container-of-promises-names-its-slot -->
A cursor over an array stands at an index, so a promise read through its `current()` has a slot exactly as
one read through `get(i)` does: awaiting it empties that slot, and the array's own element walk has nothing
left to reclaim.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		let c = try s.cursor() otherwise panic("has one")
		let p = c.current()
		return (await p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.a-move-out-hands-the-thread-over -->
The other side of the rule above: `pop` MOVES the element out, so the container has stopped naming it and
the caller owns it outright — the read is legal and the awaited value arrives intact. This is what keeps
the refusal a statement about BORROWING rather than a ban on getting a promise out of a container.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		let p = try s.pop() otherwise panic("has one")
		return (await p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.error.cancel-a-promise-no-frame-owns -->
⭐⭐ **`cancel` IS A CONSUME, SO IT ASKS THE SAME OWNERSHIP QUESTION `await` ASKS — AND FOR TWO WEEKS IT
DID NOT.** `requireConsumedPromiseIsOwned` had exactly one caller, `emitAwaitOp`, so this program and
its `await` twin — the SAME program with one word changed — disagreed: the twin was refused with the
E3141 below, and this one COMPILED and aborted **75** at run time. A reclaim the compiler cannot
account for is not less wrong for being spelled `cancel`.

⚠ **THE SHAPE IS A MERGE, and that is why no frame owns the promise.** A phi is minted with the slot
columns seeded NOT-SET — there is no single slot to empty, because the two edges name different ones —
so the move out of the container can never be finished. `requireConsumedPromiseIsOwned`'s own header
describes this program; the only thing new here is that the cancel road reaches it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 21
end 'plain'

function pick() returns bool
		return true
end 'pick'

function main() returns ExitCode
		var s = IntPromiseArray.create()
		s.push(async plain())
		s.push(async plain())
		var p = try s.get(0) otherwise panic("has two")
		if pick() 'branch'
				p = try s.get(1) otherwise panic("has two")
		end 'branch'
		p.cancel()
		return 5 as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:23:5: a promise cannot be borrowed through 'cancel': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.a-callee-awaits-the-promise-it-is-handed -->
⭐⭐ **A CALLEE THAT AWAITS ITS PROMISE PARAMETER OWNS IT, so the caller MOVES the promise in.** A green
thread has exactly one owner; `await` hands that thread back to the runtime, so the frame that spells the
`await` must be the frame that owns it. The transfer is the ordinary consumed-argument road — the caller
vacates its binding at the call and the callee is enrolled the promise's owner at its parameter.

⛔⛔ **THE OWNERSHIP IS DECIDED BEFORE THE CALLEE'S BODY IS PARSED, WHICH IS WHY THE DECLARATION SWEEP HAS
TO SEE THE DOOR.** `bindParameters` asks the swept consume set whether the callee takes its promise
parameter, and that sweep recognised stores into durable storage and nothing else — so `await p` on a
parameter enrolled nobody, and the `await` met `requireConsumedPromiseIsOwned`'s E3141 at a program that
is perfectly legal. That refusal is the SECOND answer this shape got: before it existed the callee awaited
a thread it did not own and the program aborted **75**, which is the stamp-without-vacate abort
`error.cancel-a-promise-no-frame-owns` records one case up. `promise-peek.md`'s
`a-peek-through-a-function-leaves-the-promise-alone` pins the other side — a callee that only READS
`p.inner` consumes nothing and leaves the promise with the caller.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	return await p
end 'finish'

function main() returns ExitCode
	let p = async work(6)
	return finish(p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.a-callee-cancels-the-promise-it-is-handed -->
`cancel` is the other door onto the same reclaim, so it transfers ownership exactly as `await` does — the
callee cancels a thread it owns, `__gt_live_count` balances to zero and no leak abort fires. The two doors
are one list (`PromiseConsume`), which is what stops the sweep learning one and not the other.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	Scheduler.yield()
	return 1
end 'work'

function abandon(p IntPromise)
	p.cancel()
end 'abandon'

function main() returns ExitCode
	let p = async work()
	abandon(p)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.error.a-promise-handed-to-an-awaiting-callee-is-consumed -->
The negative of the two above: handing the promise over is a MOVE, so the caller has stopped naming the
thread and a later `await` of its binding is refused at that second use — at the CALLER's line, which is
where the mistake is. The refusal the caller earns is the ordinary use-after-move: the thread is alive and
the callee has it, which is exactly what E3102 says.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	return await p
end 'finish'

function main() returns ExitCode
	let p = async work(6)
	let handed = finish(p)
	return (handed + (await p)) as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:26: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-promise-drop.an-instance-method-awaits-the-promise-it-is-handed -->
⭐⭐ **AN INSTANCE METHOD OWNS THE PROMISE IT AWAITS, EXACTLY AS THE FREE FUNCTION TWO CASES UP DOES.**
A receiver decides nothing about who holds a green thread: `await` hands the thread back to the runtime,
so the frame that spells it is the owner and the caller moves the promise in at the call.

⛔ **WHICH IS A CLAIM ABOUT THE DECLARATION SWEEP, because ownership is settled before the body is
parsed.** `bindParameters` asks the swept consume set, so a sweep reading only receiver-less declarations
leaves nobody enrolled and the method's own `await` meets E3141 — a refusal earned by the receiver rather
than by the program, which is what it answered until the sweep read every shape's parameters
(`Parser.sweptParamNamesFor`). `ServiceLoop.dropUnconsumedPayloads` reads the same widened set and still
gets `false` for every message, by a checked reason rather than by that skip: a promise never crosses a
send (E3135 refuses a `Promise` payload at the `spawn`, reading the handler's declared slot type), and
neither of the other two consume doors can fire in a service either.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

type Runner
	static function create() returns Runner
		return Runner{}
	end 'create'

	function finish(p IntPromise) returns Integer
		return await p
	end 'finish'
end 'Runner'

function main() returns ExitCode
	let r = Runner.create()
	let p = async work(6)
	return r.finish(p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.a-local-that-rebinds-a-promise-parameters-name -->
⛔⛔ **A `let p = …` INSIDE THE BODY TAKES THE NAME, SO THE `await` PAST IT CONSUMES THE LOCAL AND NOT THE
PARAMETER.** The declaration sweep matches a consume door's operand against the parameter list BY NAME, so
a rebinding is where the parameter's claim on its own name ends. Attributing the `await` below to the
parameter has the caller move its promise in against a callee that never reclaims it — the green thread
outlives the program and the run aborts **101** with no diagnostic anywhere. The parameter stays the
CALLER's, and `main`'s own scope exit is what drops it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	let p = async work(5)
	return await p
end 'finish'

function main() returns ExitCode
	let outer = async work(1)
	return finish(outer) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: async-promise-drop.a-closure-parameters-shadow-ends-at-its-line -->
⭐ **A CLOSURE'S PARAMETERS BIND FOR THE ONE EXPRESSION AFTER `gives`, so the shadow is gone at the next
newline.** `p` on the last line below is the promise parameter again, and its `await` is what enrols this
frame the promise's owner. A shadow that outlived its line would leave nothing enrolled and
`requireConsumedPromiseIsOwned` refuses the body at that very `await` (E3141) — which is the loud half of
what a line column that never cleared would cost; the quiet half is a promise nobody reclaims.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	let bump = function(p Integer) gives p + 1
	let extra = bump(4)
	return (await p) + extra
end 'finish'

function main() returns ExitCode
	let outer = async work(1)
	return finish(outer) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.a-cursor-peek-over-a-container-of-promises-names-its-slot -->
A cursor's `peek(n)` reads the slot `n` past where it stands, and that is the slot the read names: awaiting it
empties slot 1, `get(0)` still names slot 0, and each thread is reclaimed exactly once. Returns 5 + 37.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var s = IntPromiseArray.create()
	s.push(async plain(5))
	s.push(async plain(37))
	let c = try s.cursor() otherwise panic("has two")
	let ahead = try c.peek(1) otherwise panic("has two")
	let first = try s.get(0) otherwise panic("has two")
	return ((await ahead) + (await first)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-promise-drop.error.a-whole-pair-from-withIterator-over-promises -->
A `withIterator` pair bound WHOLE carries the promise its array still holds, and a pair can be passed anywhere,
so no slot follows it. Only the destructuring spelling moves the promise back to its array slot; this one is
refused where the pair is read.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var s = IntPromiseArray.create()
	s.push(async plain(7))
	var total = 0
	for pair in s.withIterator() 'each'
		total = total + (await pair.1)
	end 'each'
	return total as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:15:6: a promise cannot be borrowed through 'current': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. A pair keeps no slot for the promise it carries: destructure an array's `withIterator()` pair in the loop header (`for (it, p) in a.withIterator()`), read the array with `get(i)`, or move the promise out with `pop`/`remove`
```

<!-- test: async-promise-drop.error.a-map-iterator-over-promise-values -->
A `Map`'s iterator hands each entry back as a `(key, value)` pair while the map keeps the value, and a map has
no index a slot could name — so a promise value read through it is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseMap = Map with (String, IntPromise)

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var m = IntPromiseMap.create()
	m.upsert("a", value: async plain(9))
	var total = 0
	for (_, v) in m 'each'
		total = total + (await v)
	end 'each'
	return total as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:15:6: a promise cannot be borrowed through 'current': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. A pair keeps no slot for the promise it carries: destructure an array's `withIterator()` pair in the loop header (`for (it, p) in a.withIterator()`), read the array with `get(i)`, or move the promise out with `pop`/`remove`
```

<!-- test: async-promise-drop.a-cursor-read-twice-without-moving-aborts -->
Two `current()` reads through a cursor that does not move between them name one slot, so both values name one
green thread. The compiler cannot prove a cursor's position unchanged, so the first await empties the slot and
the second finds it empty at run time and aborts with **118** (`RuntimeAbort.promiseSlotConsumedTwice`) rather
than reclaiming the thread a second time.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var s = IntPromiseArray.create()
	s.push(async plain(4))
	let c = try s.cursor() otherwise panic("has one")
	let a = c.current()
	let b = c.current()
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```

<!-- test: async-promise-drop.a-get-and-a-cursor-read-of-one-slot-abort -->
`get(0)` and a cursor standing at 0 name the same slot through two different doors, so the two reads name one
green thread and the second await aborts with **118**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var s = IntPromiseArray.create()
	s.push(async plain(4))
	let a = try s.get(0) otherwise panic("has one")
	let c = try s.cursor() otherwise panic("has one")
	let b = c.current()
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```

<!-- test: async-promise-drop.a-get-read-twice-at-one-runtime-index-aborts -->
Two `get(i)` reads whose index is one value at run time but two to the compiler name one slot, so the second
await aborts with **118**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function zero() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 0
end 'zero'

function main() returns ExitCode
	var s = IntPromiseArray.create()
	s.push(async plain(4))
	let a = try s.get(zero()) otherwise panic("has one")
	let b = try s.get(zero()) otherwise panic("has one")
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```

<!-- test: async-promise-drop.a-field-awaited-twice-aborts -->
The first await empties the field, so the second `h.p` is a fresh read of a slot that is already zero: it
names no thread, and its vacate aborts with **118** (`RuntimeAbort.promiseSlotConsumedTwice`) rather than
touching a null handle.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

type Holder
	public let p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function main() returns ExitCode
	let h = Holder.of(async plain(7))
	let a = await h.p
	let b = await h.p
	return (a + b) as ExitCode
end 'main'
```
```exitcode
118
```

<!-- test: async-promise-drop.a-field-cancelled-then-awaited-aborts -->
`cancel` is the other consume door, and it empties the field the same way — so the await behind it finds
an empty slot and aborts with **118**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

type Holder
	public let p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function main() returns ExitCode
	let h = Holder.of(async plain(7))
	h.p.cancel()
	return (await h.p) as ExitCode
end 'main'
```
```exitcode
118
```

<!-- test: async-promise-drop.a-field-rearmed-between-awaits-answers-both -->
**CONTROL.** A field written again between two consumes holds a NEW thread, and the occupant check accepts
what it finds rather than refusing the second await on the strength of the first. The field is `var` here
because that is what a rearm needs; the two cases above keep `let`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

type Holder
	public var p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function main() returns ExitCode
	var h = Holder.of(async plain(4))
	let a = await h.p
	h.p = async plain(7)
	let b = await h.p
	return (a + b) as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: async-promise-drop.a-promise-read-out-of-one-container-and-stored-into-another-moves -->
Reading a promise out of one container and storing it into another is a MOVE between two slots: the store
empties the source slot, so exactly one container owns the thread and exactly one holder may reclaim it.
The await through the destination answers, and the source's element walk finds an empty slot.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	var b = IntPromiseArray.create()
	b.push(try a.get(0) otherwise panic("has one"))

	let p = try b.get(0) otherwise panic("has one")
	return (await p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.error.a-slot-read-stored-twice -->
The store above emptied `a`'s slot and handed the thread to `b`, so the name that read it out has stopped
denoting a thread this frame can give away a second time. The refusal is the ordinary use-after-move, at the
second store and naming the binding — the same sentence the spawn spelling of this mistake already earns.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	var b = IntPromiseArray.create()
	b.push(p)

	var c = IntPromiseArray.create()
	c.push(p)

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-promise-drop.error.a-retired-slot-read-stored -->
Two reads of one slot hold one thread, and the first store empties the slot both of them name — so `q` names
a thread no frame owns any longer. Nothing moved `q` itself, which is why the refusal names the door it was
carried through rather than a move: the sentence an author needs here is that the slot was already consumed
by another read of it. Without the retire's half of the rule this program would compile and leave `b` and `c`
naming one green thread.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")
	let q = try a.get(0) otherwise panic("has one")

	var b = IntPromiseArray.create()
	b.push(p)

	var c = IntPromiseArray.create()
	c.push(q)

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:22:4: a promise cannot be borrowed through 'push': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.a-slot-read-stored-in-exclusive-arms-is-owned-once -->
**CONTROL.** Two stores of one slot read in MUTUALLY EXCLUSIVE arms are each the only store on their own
path, so exactly one container ends up owning the thread and `a`'s slot is vacated whichever way the branch
goes. The move state must therefore be rewound at the arm boundary rather than carried into the sibling
path: a poison that outlives its own edge refuses this program.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function pick() returns bool
	return File.exists(FilePath from "definitely-not-here.txt")
end 'pick'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	var b = IntPromiseArray.create()
	var c = IntPromiseArray.create()

	if pick() 'x'
		b.push(p)
	end 'x' else 'y'
		c.push(p)
	end 'y'

	if b.count() == 1 'heldByB'
		let q = try b.get(0) otherwise panic("holds it")
		return (await q) as ExitCode
	end 'heldByB'

	let r = try c.get(0) otherwise panic("holds it")
	return (await r) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.error.a-slot-read-stored-in-one-arm-is-spent-past-the-join -->
The other side of the control above: a thread given away on ONE reaching edge is spent past the join, so the
store after the `if` is a use of a name that no longer denotes a thread on every path. That is
`reconcileMovesAtMerge`'s own rule, and the refusal is the ordinary use-after-move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function pick() returns bool
	return File.exists(FilePath from "definitely-not-here.txt")
end 'pick'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	var b = IntPromiseArray.create()
	var c = IntPromiseArray.create()

	if pick() 'x'
		b.push(p)
	end 'x'

	c.push(p)

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-promise-drop.error.a-slot-read-stored-inside-a-loop-it-was-read-outside -->
A store inside a loop is parsed ONCE and runs on every trip, so a slot read taken OUTSIDE the loop would
empty `a`'s slot on the first trip and meet an already-empty one on the second, while `b` collected two
entries naming one thread. That is the loop-escaping move an owned binding is already refused for, on the
road that enrols no owned binding — so it earns the same refusal, and the cure is the same: read the
element inside the loop body.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	var b = IntPromiseArray.create()
	var i = 0

	while i < 2 'loop'
		b.push(p)
		i = i + 1
	end 'loop'

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:5: Unsupported: moving a value declared outside this loop from inside the loop body — its drop on the loop's other exit paths (the back edge would re-move it next iteration; a `break` leaves it live on the normal exit) needs path-sensitive elaboration across the loop boundary, which arrives with a later wave. Move the value into the loop body, or restructure so the move does not cross the loop boundary
```

<!-- test: async-promise-drop.error.a-slot-read-awaited-inside-a-loop-it-was-read-outside -->
<!-- unsupported-targets: wasm32-wasi -->
**CONTROL for the refusal above.** The await twin of the same mistake is caught by a DIFFERENT road and must
go on being caught by it: linearity is decided on the IR, where the await is reachable from itself across the
back edge without re-passing an `async` that would re-arm the thread. Its sentence names the rule the author
broke, so the store door's loop check must not reach this program and displace it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	var acc = 0
	var i = 0

	while i < 2 'loop'
		let s = await p
		acc = acc + s
		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:21:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-promise-drop.error.a-retired-slot-read-awaited -->
The await spelling of the case above, and the one that fixes its sentence: the first await empties the slot
both reads name, so the second names a thread no frame owns. It is not a move — nothing moved `q` — and the
refusal must say what actually happened, which is that the slot was consumed by another read of it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")
	let q = try a.get(0) otherwise panic("has one")

	let first = await p
	let second = await q

	return (first + second) as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:19:15: a promise cannot be borrowed through 'await': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.error.a-slot-read-given-away-in-a-while-condition -->
A `while` CONDITION runs on every trip exactly as its body does, so giving a slot read away inside one
escapes the loop the same way — passing a promise to a callee that awaits it is a move, and the second trip
would hand over a thread the first already gave up. The loop's own context is not pushed until the condition
has parsed, so the depth the body is measured against cannot see this; the refusal is the one an owned
binding in this position already earns.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain(n Integer) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return n
end 'plain'

function takes(p IntPromise) returns bool
	let v = await p
	return v > 0
end 'takes'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async plain(7))

	let p = try a.get(0) otherwise panic("has one")

	while takes(p) 'loop'
		_ = File.exists(FilePath from "noyield.txt")
	end 'loop'

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:22:2: Unsupported: moving a value declared outside this loop from inside the loop body — its drop on the loop's other exit paths (the back edge would re-move it next iteration; a `break` leaves it live on the normal exit) needs path-sensitive elaboration across the loop boundary, which arrives with a later wave. Move the value into the loop body, or restructure so the move does not cross the loop boundary
```
