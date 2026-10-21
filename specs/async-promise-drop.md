---
feature: async-promise-drop
status: stable
keywords: [async, await, promise, green-threads, drop, cancel, ownership, leak, E3100]
category: concurrency
---

# Async / Await — un-awaited Promise drop-reclaim + cancel

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
thread that is neither awaited nor dropped leaks silently — so the runtime keeps a green-thread live count (one up
per spawn, one down when the promise's owner releases it, by an await or a drop) and the one OS-exit leak gate
asserts it is zero.
A leak (or an over-reclaim) reports `RuntimeAbort.greenThreadLeak` (75), distinct from the heap gate's 101.

`__gt_promise_drop` branches on the thread's state: a `completed` thread (which may have run while its owner
was parked on a DIFFERENT await) has finished with its stack, which its runner returned to the stack pool when
it is not the current seed size and which the reclaimed record keeps for the next spawn when it is; a `ready`
(never-run) thread is renounced where it sits in its strand's queue, and whoever pops it reclaims it, seed
stack and struct together, instead of running it; a `waiting` (parked) thread is taken off whatever would have woken it —
its timer entry, or the poll descriptor a socket or a child made it a waiter on — and READIED, so it resumes,
runs its body out and is reclaimed by its runner like a queued thread. A Windows overlapped read the kernel is
still serving is not taken off the poller: the drop cancels the operation with `CancelIoEx`, and the thread is
readied by that operation's own completion.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Dropping a promise reaps a green-thread struct and its stack, which come from
`osAllocPages`/`VirtualAlloc` and go back to `osFreePages`/`VirtualFree`, which every native lane provides; a WASI component is refused by E3104,
which is why the cases here carry no `unsupported-targets` marker for it. A case marked for one lane
family is marked because its CHILD COMMAND is a shell's or `cmd`'s, never because the drop is.

A case observes a thread without consuming its promise through `p.isComplete()`. The cases that park on a
child spawned by `__Builtins.runProcess` compile in the library tier (`<!-- tier: stdlib -->`): that
intrinsic is callable only from a source file under `stdlib/` or `runtime/` (E3185). They use it because it is
the child wait that parks on a poll source a drop takes the thread off. A coroutine parked in `Subprocess.run`
waits in `__gt_subp_wait_collect`, which drains the child's pipes while it waits: dropping its promise waits the
child out (the program lives as long as the child does), so it exercises another road than the one these cases
pin.

A promise handed to a call moves into a parameter the callee takes (one it awaits, cancels, stores or returns,
or hands on to a callee that takes it) and stays with the caller at a parameter that only reads it; where the overloads a call may bind differ, the
overload it binds decides. An `async` call moves a promise only into a parameter every overload takes: the
coroutine can outlive the frame that started it, so a promise, or a value whose type holds one, at a parameter
that only reads it is E3141. A coroutine dropped before it runs reclaims the promise it was handed. One promise
at two arguments of one call where a parameter takes it is E3102, reported at the second argument and naming
the binding written there. `p.isComplete()` on a read of a slot another read already emptied aborts with 118.

## Tests

<!-- test: async-promise-drop.never-ran-drop-no-leak -->
A spawned green thread that is never awaited is DROPPED at scope exit: it is renounced where it sits in its
strand's queue, so it is reclaimed — seed stack and struct — rather than run, and the green-thread live count balances to
zero — so the program exits with `main`'s own code (0), not the GT-leak abort (75).
```maxon

function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	_ = async trivial()
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.completed-sibling-drop-no-leak -->
A sibling promise that COMPLETES while another is awaited, and is then dropped — its struct is reclaimed
WITHOUT freeing the stack twice (the stack travels with the record, which is reclaimed once). While `main` is parked on `await b` its
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
	let done = 1 if a.isComplete() else 0
	return (r + done) as ExitCode
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
var flag = 0 as Integer

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
The leak shape: a loop that spawns a promise every iteration and never awaits it. Each iteration's
promise is dropped at the loop body's scope exit — renounced in its strand's queue (so it is reclaimed rather
than run when the queue reaches it) and its struct recycled onto the free-list, which the next spawn reuses.
Memory stays bounded across 1000 iterations and the live count balances to zero (exit 0). Neither the struct
nor the seed stack is a box, so both are invisible to the heap leak gate's tracked live column, and
The green-thread live count is the gate that catches them.
```maxon

function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	var i = 0 as Integer
	while i < 1000 'loop'
		_ = async trivial()
		i = i + 1
	end 'loop'
	return 0
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
	let done = 1 if s.isComplete() else 0
	return (r + done) as ExitCode
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
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The CHILD twin of the parked-timer case, and the same program one op at a time. `slowProc` spawns a child
that runs for ~2 s and parks on the poll source that child became; `fast` completes immediately, so `await q`
returns 42 while `slowProc` is still parked on its child — the `isComplete()` peek adds `0`, which is that
thread saying so. Scope exit drops `s` — the `waiting` arm takes it off that source and RENOUNCES it, so the
thread resumes, gives the source back, closes the child's handle (abandon the WAIT, do not kill the child) and
unwinds its own frame. The abandoned child runs to completion independently; the program exits promptly with 42
and the live count balances to zero.

⚠ **`s` MUST BE BOUND.** `_ = async slowProc()` discards the promise at its own statement, before `main`
ever parks: `__gt_spawn` → `__gt_ready` → `__gt_promise_drop` run back to back, so `slowProc` never runs,
never spawns a child, and the drop takes the QUEUED arm — the shape `parked-timer-drop-cancel`'s own warning
names. **That spelling answers 42 by testing nothing.**

⚠ **IT SPAWNS THROUGH `cmd /c`, SO IT CARRIES AN `unsupported-targets` MARKER**: on the three POSIX lanes
`/bin/sh -c "cmd /c ping …"` fails to exec and returns at once — nothing to park on.
`posix-parked-subprocess-drop-cancel` is this case's sibling on those lanes.
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
	let done = 1 if s.isComplete() else 0
	return (r + done) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.posix-parked-subprocess-drop-cancel -->
<!-- tier: stdlib -->
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
	let done = 1 if s.isComplete() else 0
	return (r + done) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.parked-subprocess-drop-reclaims-the-frames-heap -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐ **THE DROPPED COROUTINE UNWINDS ITS OWN FRAME, SO THE HEAP ITS LOCALS OWN GOES BACK.** `slowProc` holds
one interpolated `String` across a park on its child, and `main` drops the promise while it is still parked.
Renouncing resumes the thread on the source it was waiting on and lets it unwind, which releases that
`String`; freeing the parked stack outright strands it, and the leak gate reports that as **exit 101** —
not a wrong answer, which is why the symptom is an exit code no arithmetic in the program can produce.

⚠ **`parked-subprocess-drop-cancel` DOES NOT COVER THIS, AND THE ONE INTERPOLATED `String` IS THE WHOLE
DIFFERENCE.** Its coroutine holds nothing, so a drop that frees the parked stack and a drop that unwinds it
answer 42 alike — there is nothing on that stack to strand.
`posix-parked-subprocess-drop-reclaims-the-frames-heap` is this case on the POSIX lane.
```maxon

function slowProc(tag Integer) returns Integer
	let held = "held-{tag}"
	let code = try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99
	return (code as Integer) + (held.byteLength() as Integer)
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let p = async slowProc(1)
	let q = async fast()
	let r = await q
	let done = 1 if p.isComplete() else 0
	return (r + done) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.posix-parked-subprocess-drop-reclaims-the-frames-heap -->
<!-- tier: stdlib -->
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
	return (code as Integer) + (held.byteLength() as Integer)
end 'slowProc'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let p = async slowProc(1)
	let q = async fast()
	let r = await q
	let done = 1 if p.isComplete() else 0
	return (r + done) as ExitCode
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
`isComplete()` peek adds `0`, which is that thread saying it was still parked when `dropWhileParked`
returned and dropped it.
```maxon
function reader(h FullRangeInt, tag Integer) returns Integer
	let held = "held-{tag}"
	let line = subpReadLine(h)
	return line.byteLength() + held.byteLength()
end 'reader'

function dropWhileParked(h FullRangeInt) returns Integer
	let p = async reader(h, tag: 1)
	sleep(200)
	return 1 if p.isComplete() else 0
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
	let done = 1 if p.isComplete() else 0
	return 7 + done
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
Re-arming a promise `var` INSIDE A LOOP is not phi-blind: the loop-header phi
carries the promise mark, so each iteration DROPS the previous thread (cancelling it) and the last one drops at
scope exit — the live count balances to zero (exit 0). An unmarked phi would give the re-arm no live thread to
drop and route the scope-exit drop to `__mm_decref` on a GT pointer, corrupting the heap count (exit 101).
```maxon
function trivial() returns Integer
	Scheduler.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	var p = async trivial()
	var i = 0 as Integer
	while i < 8 'loop'
		p = async trivial()
		i = i + 1
	end 'loop'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.rearm-var-across-branch -->
Re-arming a promise `var` in ONE ARM OF A BRANCH is likewise not phi-blind: the if-continuation phi
merges the re-armed thread and the untouched one, both marked promises, so the scope-exit drop cancels whichever
the taken path holds — balanced on every path (exit 0).
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
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.await-rearmed-var -->
AWAITING a promise `var` re-armed across a branch works and yields the re-armed thread's result: the
merge phi is recognised as a promise, and its awaited result type is recovered by tracing the phi's incoming
`async` calls. `positive()` is true, so `p` holds the re-armed thread; `await p` returns 7 and both threads are
accounted for (the original dropped at the re-arm, the re-armed one awaited).
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
AWAITING a promise `var` re-armed INSIDE A LOOP, from AFTER the loop — the loop-EXIT phi, a block-arg-carried
promise. `p` is re-armed each
iteration (dropping the previous thread); after the loop `await p` targets the loop-exit phi, whose promise mark
and awaited result type are recovered by tracing the phi's incoming `async` calls. It returns the LAST spawn's
result (5), every intermediate thread dropped and the live count balanced.
```maxon
function five() returns Integer
	Scheduler.yield()
	return 5
end 'five'

function main() returns ExitCode
	var p = async five()
	var i = 0 as Integer
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
Re-arming a promise `var` whose thread is still named by a LIVE ALIAS is refused: `let q = p` gives
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
⛔ **A `var` DOES NOT CHANGE TYPE, AND A PROMISE BINDING IS NO EXCEPTION.** The spawn is typed, so `p`
holds a `Promise with Integer` for its whole life and assigning an `int` to it is the same refusal assigning
an `int` to any other declared type earns.

⚠ **AN AWAITED PROMISE'S BINDING OWNS NOTHING**, so nothing is dropped at scope exit and no use-after-move is
reported. That is what `rearm-var-across-loop`, `rearm-var-across-branch` and `await-rearmed-var` test, with a
re-armed PROMISE rather than a scalar, which is the only re-arm the type admits.
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
	if slot == (slots.count() as Integer) 'firstUse'
		slots.push(p)
	end 'firstUse' else 'reuse'
		try slots.set(slot as ElementIndex, value: p) otherwise panic("the slot exists — this arm is only taken when it does")
	end 'reuse'
end 'arm'

function completesUnderTheDrive(slots IntPromiseArray) returns bool
	var spins = 0 as Integer
	while spins < MaxSpins 'drive'
		Scheduler.yield()
		let p = try slots.get(0) otherwise panic("slot 0 was armed before the drive")
		if p.isComplete() 'complete'
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
		return 1
	end 'pushedThreadCancelled'
	let first = try slots.get(0) otherwise panic("slot 0 was just armed")
	var sum = await first

	arm(slots, slot: 0, n: 31)
	if not completesUnderTheDrive(slots) 'setThreadCancelled'
		return 2
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
The mirror of the case above, and a second case rather than a second assertion because what it pins is
ORDER-DEPENDENT: every store door consumes the thread it stores, whichever door the parser reads first. So the
arms are written the other way round here, `set` first and `push` second, which makes `push` the door that is
read second and taken FIRST at run time. Same two threads, same 42.
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
	if slot < (slots.count() as Integer) 'reuse'
		try slots.set(slot as ElementIndex, value: p) otherwise panic("the slot exists — this arm is only taken when it does")
	end 'reuse' else 'firstUse'
		slots.push(p)
	end 'firstUse'
end 'arm'

function completesUnderTheDrive(slots IntPromiseArray) returns bool
	var spins = 0 as Integer
	while spins < MaxSpins 'drive'
		Scheduler.yield()
		let p = try slots.get(0) otherwise panic("slot 0 was armed before the drive")
		if p.isComplete() 'complete'
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
		return 1
	end 'pushedThreadCancelled'
	let first = try slots.get(0) otherwise panic("slot 0 was just armed")
	var sum = await first

	arm(slots, slot: 0, n: 31)
	if not completesUnderTheDrive(slots) 'setThreadCancelled'
		return 2
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
destructor the array record stamps at `element_destroy@40`. A record stamped `0` there would let the array
die taking its elements' threads with it, unreclaimed: the green-thread live count would stay at 1 and the program
would exit **75**.
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
		return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.an-element-moved-out-by-pop-belongs-to-the-caller -->
<!-- procs: 1 -->
`pop` MOVES the element out: the array no longer holds it and the caller's binding does, so the binding
drops it at scope exit like any other owned promise. `isComplete()` peeks at the thread without consuming it, which
is what lets this case observe the promise at all without awaiting it. One processor keeps the answer fixed: `main`
has not parked, so the popped thread has not run yet.
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
		print("popped a thread complete={p.isComplete()}, array now {s.count()}")
		return 0
end 'main'
```
```stdout
popped a thread complete=false, array now 0
```
```exitcode
0
```

<!-- test: async-promise-drop.a-struct-field-drops-the-promise-it-holds -->
A struct FIELD is an owner too, and the struct's synthesized destructor drops it. A spawn is typed
`Promise with …`, so `Holder.of(async plain())` stores it into the field; stamping the field destructor is
what makes that position safe.

⚠ **IT AWAITS THE FIELD RATHER THAN PEEKING IT, AND THE DIFFERENCE IS THE WHOLE CASE.** A peek
`h.p.isComplete()` answers for the wrong reason. The spawn is a statement-scoped pending temporary, so storing it
into a field without MOVING it would leave the drain to cancel the thread at the end of that statement; the
field would then hold a dead handle, and a peek reads a thread that is already gone. `await h.p`
is what tells the two apart: against a cancelled thread it aborts (**exit 92**), and only a field that
really owns a live promise answers 7.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

type Holder
		public var p as IntPromise

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

<!-- test: async-promise-drop.a-let-field-takes-a-fresh-spawn-and-a-last-use-promise -->
A `let` field takes a promise it is the only holder of: a spawn written at the call, and a `var` promise at its last use. Both are moved in, and the holders' destructors drop them without a leak.
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
	var pr = async plain()
	let h = Holder.of(pr)
	let again = Holder.of(async plain())
	_ = h.p.isComplete()
	_ = again.p.isComplete()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.error.awaiting-a-promise-held-in-a-let-field-is-refused -->
`await` consumes the promise: it empties the slot it was read from. A `let` field is read-only, so nothing reachable through it may be written, and the await is refused. A `var` field (`a-struct-field-drops-the-promise-it-holds`) is accepted.
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
```maxoncstderr
error E3159: <fragment>:19:10: cannot consume with 'await' a promise held in a `let` field: consuming a promise empties the slot it was read from, and nothing reachable through a read-only value may be written; hold it in a `var` field
```

<!-- test: async-promise-drop.error.cancelling-a-promise-held-in-a-let-field-is-refused -->
`cancel` consumes the promise the same way, so it is refused through a `let` field for the same reason.
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
	h.p.cancel()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:19:6: cannot consume with 'cancel' a promise held in a `let` field: consuming a promise empties the slot it was read from, and nothing reachable through a read-only value may be written; hold it in a `var` field
```

<!-- test: async-promise-drop.error.a-promise-element-read-through-a-borrow-door -->
⭐ **A CORPUS MEMBER MAY HAND A PROMISE ELEMENT OVER ONLY IF IT STOPPED HOLDING IT.** `last()` reads the
element and leaves it in the array. For a refcounted element that is sound — the read RETAINS, so both
holders own a reference — and for a green thread it is impossible: there is no second reference to take,
so the array and the caller would each reclaim the same thread.

⛔ **A CONTAINER OF PROMISES REFUSES THE READ.** Admitted, `try s.last() … ; await l` would have the
array's element destructor and the caller's `await` each reclaim the same thread — a double free.

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
		view c = try s.cursor() otherwise panic("has one")
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
⭐⭐ **`cancel` IS A CONSUME, SO IT ASKS THE SAME OWNERSHIP QUESTION `await` ASKS.**
`requireConsumedPromiseIsOwned` guards both doors, so this program and its `await` twin — the SAME program
with one word changed — are refused with the same E3141, each naming its own door. A reclaim the compiler
cannot account for is not less wrong for being spelled `cancel`.

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
parameter, and the sweep counts an `await` of the parameter as a take. A sweep that saw only stores into
durable storage would enroll nobody, and the `await` would meet `requireConsumedPromiseIsOwned`'s E3141 at a
program that is perfectly legal. `promise-peek.md`'s
`a-peek-through-a-function-leaves-the-promise-alone` pins the other side — a callee that only READS
`p.isComplete()` consumes nothing and leaves the promise with the caller.
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
callee cancels a thread it owns, the green-thread live count balances to zero and no leak abort fires. The two doors
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
	return 0
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
than by the program. The sweep therefore reads every shape's parameters
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
	view c = try s.cursor() otherwise panic("has two")
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
	var total = 0 as Integer
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
	var total = 0 as Integer
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
	view c = try s.cursor() otherwise panic("has one")
	let a = c.current()
	let b = c.current()
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
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
	view c = try s.cursor() otherwise panic("has one")
	let b = c.current()
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
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
	let a = try s.get(zero() as ElementIndex) otherwise panic("has one")
	let b = try s.get(zero() as ElementIndex) otherwise panic("has one")
	return ((await a) + (await b)) as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
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
	public var p as IntPromise

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
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
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
	public var p as IntPromise

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
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
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
A store inside a loop runs on every trip, so a slot read taken OUTSIDE the loop empties `a`'s slot on the
first trip and would meet an already-empty one on the second, while `b` collected two entries naming one
thread. The second trip reads a value the first moved, which is E3102; the cure is to read the element
inside the loop body.
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
	var i = 0 as Integer

	while i < 2 'loop'
		b.push(p)
		i = i + 1
	end 'loop'

	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:10: use of moved value 'p': it was moved in an earlier iteration of this loop
```

<!-- test: async-promise-drop.error.a-slot-read-awaited-inside-a-loop-it-was-read-outside -->
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

	var acc = 0 as Integer
	var i = 0 as Integer

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
moves it on every trip — passing a promise to a callee that awaits it is a move, and the second trip would
hand over a thread the first already gave up. That second trip is E3102.
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
error E3102: <fragment>:22:14: use of moved value 'p': it was moved in an earlier iteration of this loop
```

<!-- test: async-promise-drop.error.a-slot-read-awaited-inside-a-nested-loop-it-was-read-outside -->
A slot read taken in the OUTER loop's body and awaited in the INNER loop is spent by the inner loop's first
trip, so the inner loop's back edge is the refusal, exactly as for a read taken outside every loop.
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
	a.push(async plain(8))

	var acc = 0 as Integer
	var i = 0 as Integer

	while i < 2 'outer'
		let p = try a.get(i as ElementIndex) otherwise panic("in range")
		var j = 0 as Integer

		while j < 2 'inner'
			acc = acc + (await p)
			j = j + 1
		end 'inner'

		i = i + 1
	end 'outer'

	return acc as ExitCode
end 'main'
```
```maxoncstderr
error E3100: <fragment>:24:17: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-promise-drop.error.a-slot-read-stored-inside-a-nested-loop-it-was-read-outside -->
The store twin of the case above: the inner loop stores the outer body's slot read on every trip, so its
second trip reads a value the first moved.
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
	a.push(async plain(8))

	var b = IntPromiseArray.create()
	var i = 0 as Integer

	while i < 2 'outer'
		let p = try a.get(i as ElementIndex) otherwise panic("in range")
		var j = 0 as Integer

		while j < 2 'inner'
			b.push(p)
			j = j + 1
		end 'inner'

		i = i + 1
	end 'outer'

	return b.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:11: use of moved value 'p': it was moved in an earlier iteration of this loop
```

<!-- test: async-promise-drop.a-get-awaited-at-one-index-every-iteration-aborts -->
A `get(0)` INSIDE the loop is a fresh read on every trip, so the compiler has no name that was moved twice; the
first trip's await empties the slot and the second trip's read finds it empty at run time, which aborts with
**118** (`RuntimeAbort.promiseSlotConsumedTwice`) before anything is awaited, reclaimed or leaked.
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

	var acc = 0 as Integer
	var i = 0 as Integer

	while i < 2 'loop'
		let p = try a.get(0) otherwise panic("has one")
		acc = acc + (await p)
		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
```

<!-- test: async-promise-drop.a-get-stored-at-one-index-every-iteration-aborts -->
The store twin of the case above: the first trip moves the thread into `b` and empties `a`'s slot, so the second
trip's read of it aborts with **118** rather than storing a second reference to one thread.
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
	var i = 0 as Integer

	while i < 2 'loop'
		b.push(try a.get(0) otherwise panic("has one"))
		i = i + 1
	end 'loop'

	return b.count() as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
```

<!-- test: async-promise-drop.a-field-awaited-every-iteration-aborts -->
A field read inside the loop is a fresh read of the same slot on every trip, so the second trip's await finds the
field emptied by the first and aborts with **118**.
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
	let h = Holder.of(async plain(7))

	var acc = 0 as Integer
	var i = 0 as Integer

	while i < 2 'loop'
		acc = acc + (await h.p)
		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
```

<!-- test: async-promise-drop.a-field-stored-every-iteration-aborts -->
The store twin of the case above: the second trip's read of the emptied field aborts with **118**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

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
	let h = Holder.of(async plain(7))

	var b = IntPromiseArray.create()
	var i = 0 as Integer

	while i < 2 'loop'
		b.push(h.p)
		i = i + 1
	end 'loop'

	return b.count() as ExitCode
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
```

<!-- test: async-promise-drop.a-slot-rearmed-between-moves-in-a-loop-answers-every-trip -->
**CONTROL for the four aborts above.** A slot written again between two moves holds a NEW thread, so a loop that
awaits `a.get(0)` and an array slot, and a field, and re-arms each before the next trip, answers every trip:
(1 + 10) + (1 + 10) + (2 + 10) = 34.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

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
	var a = IntPromiseArray.create()
	a.push(async plain(1))

	var h = Holder.of(async plain(10))

	var acc = 0 as Integer
	var i = 0 as Integer

	while i < 3 'loop'
		let p = try a.get(0) otherwise panic("has one")
		acc = acc + (await p)
		try a.set(0, value: async plain(1 + i)) otherwise panic("in range")

		acc = acc + (await h.p)
		h.p = async plain(10)

		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```exitcode
34
```

<!-- test: async-promise-drop.a-distinct-slot-moved-on-every-nested-iteration-answers-every-trip -->
**CONTROL.** A nested loop whose inner body reads a DIFFERENT slot on every trip, and both awaits it and
hands another on, moves every thread exactly once: 7 + 8 + 9 + 10 awaited, four more handed to `b`.
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
	var seed = 0 as Integer

	while seed < 8 'fill'
		a.push(async plain(7 + seed))
		seed = seed + 1
	end 'fill'

	var b = IntPromiseArray.create()
	var acc = 0 as Integer
	var i = 0 as Integer

	while i < 2 'outer'
		var j = 0 as Integer

		while j < 2 'inner'
			let slot = i * 2 + j
			let p = try a.get(slot as ElementIndex) otherwise panic("in range")
			acc = acc + (await p)

			let q = try a.get((slot + 4) as ElementIndex) otherwise panic("in range")
			b.push(q)
			j = j + 1
		end 'inner'

		i = i + 1
	end 'outer'

	return (acc + (b.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
38
```

<!-- test: async-promise-drop.a-returned-spawn-is-the-callers-to-await -->
⭐⭐ **A `return` HANDS THE GREEN THREAD TO THE CALLER, SO THE CALLEE'S EXIT MUST NOT DROP IT.** The spawn is
the callee's pending temporary until the `return` moves it out, exactly as a returned heap value leaves the
frame's drop sets; the caller adopts the call's result as the thread's one owner. Without the move the callee
would cancel the thread it was handing back, and the caller's `await` would wait on a thread that never runs:
**exit 92**, `RuntimeAbort.schedulerDeadlock`, with nothing printed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function start() returns IntPromise
	return async work(1)
end 'start'

function main() returns ExitCode
	let p = start()
	print("{await p}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-returned-binding-is-the-callers-to-await -->
The same hand-over from a BOUND spawn: the binding owes a scope-exit drop, and the `return` moves the thread
out of the frame's owned bindings rather than dropping it behind the value it returns.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function start() returns IntPromise
	let p = async work(1)
	return p
end 'start'

function main() returns ExitCode
	let p = start()
	print("{await p}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-returned-spawn-dropped-by-the-caller -->
A returned promise the caller never awaits is the CALLER's to drop: its binding's scope exit renounces the
thread, and the green-thread live count balances to zero. Were the callee to drop it too, the thread would be
reclaimed twice and the run would abort **75**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function start() returns IntPromise
	return async work(1)
end 'start'

function run(awaitIt bool) returns Integer
	let p = start()
	if awaitIt 'wait'
		return await p
	end 'wait'

	return 2
end 'run'

function main() returns ExitCode
	print("{run(false)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-returned-spawn-cancelled-by-the-caller -->
`cancel` on a returned promise reclaims the thread the caller owns, once.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function start() returns IntPromise
	return async work(1)
end 'start'

function run(awaitIt bool) returns Integer
	let p = start()
	if awaitIt 'wait'
		return await p
	end 'wait'

	p.cancel()
	return 2
end 'run'

function main() returns ExitCode
	print("{run(false)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-promise-parameter-returned-passes-through -->
⭐⭐ **A CALLEE THAT RETURNS ITS PROMISE PARAMETER OWNS IT, so the caller MOVES the promise in and adopts it
back as the call's result.** A `return` is a consume door exactly as `await` and `cancel` are: the frame that
hands the thread on must be the frame that owns it. Left borrowed, the caller kept its own spawn pending AND
adopted the result — two owners of one thread — and the drain at the end of the call's statement cancelled
the thread the caller then awaited: **exit 92**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function pass(q IntPromise) returns IntPromise
	return q
end 'pass'

function main() returns ExitCode
	let p = pass(async work(1))
	print("{await p}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-promise-parameter-returned-in-parentheses-passes-through -->
Parentheses do not change what a `return` hands back: `return (q)` is the same door as `return q`, read with
the parentheses stripped exactly as a struct-field store's value is.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function pass(q IntPromise) returns IntPromise
	return (q)
end 'pass'

function main() returns ExitCode
	let p = pass(async work(1))
	print("{await p}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-peeked-promise-argument-stays-with-the-caller -->
<!-- procs: 1 -->
⭐⭐ **A CALLER MOVES A PROMISE ARGUMENT IN EXACTLY WHERE THE CALLEE OWNS IT.** `finish` awaits `q`, so it owns
`q`; it only peeks `p`, so `p` stays the caller's, and the caller's later `await p` is legal. The caller and the
callee answer "does the callee take this one?" off one predicate over the swept facts. A caller that moved
every promise argument of a callee that consumes ANY parameter would refuse this program at `await p` with
E3102 and leak its sibling below. On one processor the peek reads `p` before `main` has parked, so it answers
`false` and `r` is 11.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise, q IntPromise) returns Integer
	let ran = 1 if p.isComplete() else 0
	return (await q) + ran
end 'finish'

function main() returns ExitCode
	let p = async work(1)
	let q = async work(10)
	let r = finish(p, q: q)
	print("{r} {await p}\n")
	return 0
end 'main'
```
```stdout
11 2
```
```exitcode
0
```

<!-- test: async-promise-drop.a-peeked-promise-argument-is-dropped-by-the-caller -->
<!-- procs: 1 -->
The same peeked argument left to the caller's scope exit: the caller still owns it, so the caller drops it.
Were it moved into a callee that did not own it, nobody would drop it and the run would abort **75**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise, q IntPromise) returns Integer
	let ran = 1 if p.isComplete() else 0
	return (await q) + ran
end 'finish'

function main() returns ExitCode
	let p = async work(1)
	let q = async work(10)
	print("{finish(p, q: q)}\n")
	return 0
end 'main'
```
```stdout
11
```
```exitcode
0
```

<!-- test: async-promise-drop.error.a-ternary-of-promise-parameters-is-returned -->
⭐⭐ **A `return` IS A CONSUME DOOR, SO IT ASKS THE OWNERSHIP QUESTION EVERY OTHER DOOR ASKS.** A ternary over
two promises is a merge, and a merge has no owner — `await` and `cancel` refuse the same value with this same
E3141. Handed back, the caller would adopt a thread its own spawn still owned: the statement's drain would
cancel it and the caller's `await` would never return, **exit 92**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function pick(a IntPromise, b IntPromise, c bool) returns IntPromise
	return a if c else b
end 'pick'

function main() returns ExitCode
	let p = pick(async work(1), b: async work(10), c: true)
	print("{await p}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:11:2: a promise cannot be borrowed through 'return': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.error.a-ternary-of-owned-promises-is-returned -->
The same merge over two spawns this frame owns: each binding keeps its own drop, so the merged value has no
owner to hand back. Returned, the frame's exit would drop both spawns — the returned one included — and the
caller would await a cancelled thread, **exit 92**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function pick(c bool) returns IntPromise
	let a = async work(1)
	let b = async work(10)
	return a if c else b
end 'pick'

function main() returns ExitCode
	let p = pick(true)
	print("{await p}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:13:2: a promise cannot be borrowed through 'return': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.error.an-alias-of-a-promise-parameter-is-returned -->
A `let` alias of a promise parameter names the parameter's thread without owning it, and the declaration sweep
reads a hand-back only off the parameter's own name — the rule `await` follows too. So returning the alias
is refused: handed back, the caller would adopt a second owner of the thread its own spawn still owns, and
its `await` would never return, **exit 92**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function pass(q IntPromise) returns IntPromise
	let r = q
	return r
end 'pass'

function main() returns ExitCode
	let p = pass(async work(1))
	print("{await p}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:12:2: a promise cannot be borrowed through 'return': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-promise-drop.an-awaited-parenthesized-parameter-is-owned -->
Parentheses do not change what an `await` consumes: `await (p)` is the same door as `await p`, and the
declaration sweep reads its operand with the parentheses stripped, by the one rule the `return` door and a
struct-field store use. Read without stripping, the parameter would not be enrolled as the callee's, and
this legal program would be refused with E3141 at its own `await`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise) returns Integer
	return await (p)
end 'finish'

function main() returns ExitCode
	let p = async work(6)
	return finish(p) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.an-async-callee-awaits-the-promise-it-is-handed -->
A promise handed to an `async` call is handed exactly as an ordinary call hands it: the coroutine's `finish`
awaits `q`, so it owns the thread and the caller has stopped naming it. The caller's scope exit must not drop
the thread the coroutine reclaimed, and the coroutine must not leave it for nobody to reclaim.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(q IntPromise) returns Integer
	return (await q) + 1
end 'finish'

function main() returns ExitCode
	let p = async work(1)
	let r = async finish(p)
	print("{await r}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: async-promise-drop.an-async-callee-dropped-before-it-runs-releases-the-promise-it-was-handed -->
<!-- procs: 1 -->
The coroutine owns the thread it was handed, so a coroutine that is dropped before it ever runs must release
that thread itself: nobody else names it. On one processor `main` never yields, so neither thread starts.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(q IntPromise) returns Integer
	return (await q) + 1
end 'finish'

function main() returns ExitCode
	let p = async work(1)
	_ = async finish(p)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.error.a-promise-handed-to-peeking-parameters-of-an-async-call -->
A coroutine can still be running after the frame that started it has ended, and that frame reclaims the threads
it owns when it ends. A promise the coroutine only reads would then name a reclaimed thread, so an `async` call
hands a promise only to a parameter the callee takes.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function watch(p IntPromise, q IntPromise, n Integer) returns Integer
	if p.isComplete() or q.isComplete() 'finished'
		Scheduler.yield()
	end 'finished'

	return n
end 'watch'

function main() returns ExitCode
	let p = async work(1)
	let q = async work(2)
	let r = async watch(p, q: q, n: 5)
	print("{await r} {await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:21:22: a promise cannot be borrowed through 'watch': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. A coroutine can still be running after the frame that started it has ended, and by then that frame has reclaimed the thread, so a coroutine that only reads this promise would read a reclaimed thread. Pass the promise to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```

<!-- test: async-promise-drop.error.one-promise-handed-to-two-awaiting-parameters-of-an-async-call -->
Both parameters would reclaim the one thread, so exactly one of them may be handed it. The second argument
names a promise the first already gave away, which is the ordinary use-after-move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise) returns Integer
	return (await q) + (await p)
end 'both'

function main() returns ExitCode
	let p = async work(1)
	let r = async both(p, q: p)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:24: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.error.one-promise-handed-to-two-awaiting-parameters-of-a-call -->
The ordinary call refuses the same program for the same reason: the callee would await the one thread twice.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise) returns Integer
	return (await q) + (await p)
end 'both'

function main() returns ExitCode
	let p = async work(1)
	let r = both(p, q: p)
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:18: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.error.one-promise-handed-to-an-awaiting-and-a-peeking-parameter-of-an-async-call -->
The awaiting parameter reclaims the thread the peeking parameter still names, so a read through the second
name can land on a reclaimed thread. The one promise may be handed to one parameter that takes it, and to no other.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function finish(p IntPromise, q IntPromise) returns Integer
	let ran = 1 if p.isComplete() else 0
	return (await q) + ran
end 'finish'

function main() returns ExitCode
	let p = async work(1)
	let r = async finish(p, q: p)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:26: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.error.one-promise-read-out-of-an-array-handed-to-two-awaiting-parameters-of-an-async-call -->
A promise read out of a container is bound to a name like any other, and handing that name to two parameters
that take it is refused at the second argument, naming the binding.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise) returns Integer
	return (await q) + (await p)
end 'both'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work(1))
	let p = try a.get(0) otherwise panic("one")
	let r = async both(p, q: p)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:24: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.error.a-coroutine-that-reads-a-promise-can-outlive-the-frame-that-owns-it -->
`launch` ends while `watch` is still running, and `main` then awaits `p`, which reclaims the thread — and a
later spawn reuses its record. `watch` would read `p` through a name for a thread that is no longer `p`'s, and
answer for another coroutine's thread.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function slow(n Integer) returns Integer
	for _ in 0 upto 20 'spin'
		Scheduler.yield()
	end 'spin'

	return n
end 'slow'

function watch(p IntPromise) returns Integer
	for _ in 0 upto 5 'spin'
		Scheduler.yield()
	end 'spin'

	let done = 1 if p.isComplete() else 0
	print("watch saw {done}\n")
	return done
end 'watch'

function launch(p IntPromise) returns bool
	let r = async watch(p)
	Scheduler.yield()
	return r.isComplete()
end 'launch'

function main() returns ExitCode
	let p = async work(1)
	_ = launch(p)
	let v = await p
	let q = async slow(7)
	print("{v} {await q}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:29:22: a promise cannot be borrowed through 'watch': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. A coroutine can still be running after the frame that started it has ended, and by then that frame has reclaimed the thread, so a coroutine that only reads this promise would read a reclaimed thread. Pass the promise to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```

<!-- test: async-promise-drop.error.a-promise-handed-to-overloads-that-disagree-about-taking-it-by-an-async-call -->
Which overload an `async` call binds is settled after the arguments are handed over, so the promise cannot be
moved into a coroutine that may or may not own it. A promise is handed to an `async` call only when every
overload the call may bind takes it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function relay(p IntPromise, n Integer) returns Integer
	return (await p) + n
end 'relay'

function relay(p IntPromise, n bool) returns Integer
	if n and p.isComplete() 'done'
		Scheduler.yield()
	end 'done'

	return 7
end 'relay'

function main() returns ExitCode
	let p = async work(1)
	let r = async relay(p, n: 1)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:24:22: a promise cannot be borrowed through 'relay': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. A coroutine can still be running after the frame that started it has ended, and by then that frame has reclaimed the thread, so a coroutine that only reads this promise would read a reclaimed thread. Pass the promise to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```

<!-- test: async-promise-drop.error.one-promise-handed-to-overloads-that-disagree-about-taking-it-twice -->
Overloads that disagree about taking a promise settle the hand-over after the call is parsed; the one promise
at two arguments is still refused at the second.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise, n Integer) returns Integer
	return (await q) + (await p) + n
end 'both'

function both(p IntPromise, q IntPromise, n bool) returns Integer
	if n and p.isComplete() and q.isComplete() 'done'
		Scheduler.yield()
	end 'done'

	return 7
end 'both'

function main() returns ExitCode
	let p = async work(1)
	let r = both(p, q: p, n: 1)
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:18: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.a-peek-of-an-emptied-slot-aborts-instead-of-reading-null -->
Another read consumed the slot, so this read names no thread. A peek is not a consume, so it never reaches the
vacate that aborts a consume of such a read: it aborts with **118** itself rather than loading through a null
handle.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work(1))
	let first = try a.get(0) otherwise panic("one")
	let v = await first
	let i = a.count() - 1
	let again = try a.get(i) otherwise panic("two")
	let done = again.isComplete()
	print("{v} {done}\n")
	return 0
end 'main'
```
```exitcode
118
```
```stderr
fatal error: runtime abort 118 (promiseSlotConsumedTwice)
```

<!-- test: async-promise-drop.error.a-record-holding-a-promise-handed-to-a-peeking-coroutine -->
A record that holds a promise is a promise's holder: the coroutine reads the field after the caller has
awaited it out of the record, and the slot is empty.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Holder
	public var p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function slow(n Integer) returns Integer
	for _ in 0 upto 20 'spin'
		Scheduler.yield()
	end 'spin'

	return n
end 'slow'

function watch(h Holder) returns Integer
	for _ in 0 upto 5 'spin'
		Scheduler.yield()
	end 'spin'

	let done = 1 if h.p.isComplete() else 0
	print("watch saw {done}\n")
	return done
end 'watch'

function launch(h Holder) returns bool
	let r = async watch(h)
	Scheduler.yield()
	return r.isComplete()
end 'launch'

function main() returns ExitCode
	var h = Holder.of(async work(1))
	_ = launch(h)
	let v = await h.p
	let q = async slow(7)
	print("{v} {await q}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:37:22: a promise cannot be borrowed through 'watch': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. This argument holds a promise, and a coroutine can still be running after the frame that started it has ended — by then the promise may have been awaited out of what holds it, or its thread reclaimed, and a coroutine that only reads it would read a thread that is gone. Pass the promise itself to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```

<!-- test: async-promise-drop.error.an-array-of-promises-handed-to-a-peeking-coroutine -->
The same hazard through an array: the caller awaits the element out of its slot while the coroutine still
holds the array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function slow(n Integer) returns Integer
	for _ in 0 upto 20 'spin'
		Scheduler.yield()
	end 'spin'

	return n
end 'slow'

function watch(a IntPromiseArray) returns Integer
	for _ in 0 upto 5 'spin'
		Scheduler.yield()
	end 'spin'

	let p = try a.get(0) otherwise panic("none")
	let done = 1 if p.isComplete() else 0
	print("watch saw {done}\n")
	return done
end 'watch'

function launch(a IntPromiseArray) returns bool
	let r = async watch(a)
	Scheduler.yield()
	return r.isComplete()
end 'launch'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work(1))
	_ = launch(a)
	let p = try a.get(0) otherwise panic("none")
	let v = await p
	let q = async slow(7)
	print("{v} {await q}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:31:22: a promise cannot be borrowed through 'watch': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. This argument holds a promise, and a coroutine can still be running after the frame that started it has ended — by then the promise may have been awaited out of what holds it, or its thread reclaimed, and a coroutine that only reads it would read a thread that is gone. Pass the promise itself to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```

<!-- test: async-promise-drop.error.an-undeclared-async-callee-is-undefined-not-a-borrowed-promise -->
A callee nothing declares has no parameters to take a promise, so the call is refused for what it is.
```maxon
typealias Integer = int(i64.min to i64.max)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function main() returns ExitCode
	let p = async work(1)
	let r = async nosuch(p)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:11:16: call to undefined function 'nosuch'
```

<!-- test: async-promise-drop.error.the-alias-written-at-the-second-argument-is-the-name-reported -->
The refusal names the binding the author wrote at the argument it points at, not another name for the same
promise.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise) returns Integer
	return (await q) + (await p)
end 'both'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work(1))
	let x = try a.get(0) otherwise panic("one")
	let z = x
	let r = async both(x, q: z)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:24: use of moved value 'z': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.error.the-original-written-at-the-second-argument-is-the-name-reported -->
The same promise under the other order: the second argument spells the original name, and that is the name
reported.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function both(p IntPromise, q IntPromise) returns Integer
	return (await q) + (await p)
end 'both'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work(1))
	let x = try a.get(0) otherwise panic("one")
	let z = x
	let r = async both(z, q: x)
	print("{await r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:24: use of moved value 'x': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-promise-drop.an-async-call-moves-a-promise-into-overloads-that-all-take-it -->
Every overload the call may bind awaits the promise, so the coroutine owns it whichever one is chosen.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function relay(p IntPromise, n Integer) returns Integer
	return (await p) + n
end 'relay'

function relay(p IntPromise, n bool) returns Integer
	if n 'done'
		Scheduler.yield()
	end 'done'

	return await p
end 'relay'

function main() returns ExitCode
	let p = async work(1)
	let r = async relay(p, n: 1)
	let v = await r
	print("{v}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: async-promise-drop.error.a-record-holding-a-promise-and-an-os-handle-handed-to-a-peeking-coroutine -->
Whether a graph reaches a promise does not depend on what else it holds: the record also holds a union whose
payload is a socket, which no copy can duplicate, and the coroutine still reads a promise the caller awaits
out of the record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Link
	none
	open(client TcpClient)
end 'Link'

type Holder
	public var p as IntPromise
	public var link as Link

	static function of(p IntPromise) returns Holder
		return Holder{p: p, link: Link.none}
	end 'of'
end 'Holder'

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function slow(n Integer) returns Integer
	for _ in 0 upto 20 'spin'
		Scheduler.yield()
	end 'spin'

	return n
end 'slow'

function watch(h Holder) returns Integer
	for _ in 0 upto 5 'spin'
		Scheduler.yield()
	end 'spin'

	let done = 1 if h.p.isComplete() else 0
	print("watch saw {done}\n")
	return done
end 'watch'

function launch(h Holder) returns bool
	let r = async watch(h)
	Scheduler.yield()
	return r.isComplete()
end 'launch'

function main() returns ExitCode
	var h = Holder.of(async work(1))
	_ = launch(h)
	let v = await h.p
	let q = async slow(7)
	print("{v} {await q}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:43:22: a promise cannot be borrowed through 'watch': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. This argument holds a promise, and a coroutine can still be running after the frame that started it has ended — by then the promise may have been awaited out of what holds it, or its thread reclaimed, and a coroutine that only reads it would read a thread that is gone. Pass the promise itself to a parameter the callee takes (`await`, `.cancel()`, a store or a `return` of it), or pass the result instead
```
