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
the green thread and **cancels** it. This is the ownership dual of the linear-await rule (E3100): `await` is
the consuming move, and any path with no consuming await drops the thread exactly once.

Dropping is not an exit-time drain and not a fire-and-forget run: a never-scheduled thread's body **never
runs**, and a parked thread's wait (a `sleep` timer, a `runProcess` child) is **cancelled** in place. Because
a green thread's struct and stack are slab/OS allocations invisible to the `__mm` heap leak gate, a spawned
thread that is neither awaited nor dropped leaks silently — so the runtime keeps a `__gt_live_count` (one up
per spawn, one down per await-reclaim AND per drop-reclaim) and the one OS-exit leak gate asserts it is zero.
A leak (or an over-reclaim) reports `RuntimeAbort.greenThreadLeak` (75), distinct from the heap gate's 101.

`__gt_promise_drop` branches on the thread's state: a `completed` thread (which may have run as a side effect
of driving a DIFFERENT await) has already had its stack freed, so only its struct is reclaimed; a `ready`
(never-run) thread is unlinked from the run queue and its seed stack freed; a `waiting` (parked) thread is
removed from the timer / process stores — closing a parked child's handle to abandon the wait — and its stack
freed.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** Dropping a promise reaps a green-thread struct and releases its stack through
`osFreePages`/`VirtualFree`, which exists only on x64-windows at this rung.

## Tests

<!-- test: async-promise-drop.never-ran-drop-no-leak -->
<!-- targets: x64-windows -->
A spawned green thread that is never awaited is DROPPED at scope exit: it is unlinked from the run queue, its
seed stack freed and its struct reclaimed, and `__gt_live_count` balances to zero — so the program exits with
`main`'s own code (0), not the GT-leak abort (75). Before #88 this spawn leaked its struct + stack silently.
```maxon

function trivial() returns int
	Runtime.yield()
	return 0
end 'trivial'

function main() returns ExitCode
	_ = async trivial()
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.completed-sibling-drop-no-leak -->
<!-- targets: x64-windows -->
A sibling promise that COMPLETES as a side effect of driving another await is dropped through the `completed`
arm — its struct is reclaimed WITHOUT double-freeing the stack (`__gt_drive_until` already freed it at
completion). `await b` drives the FIFO run queue and runs `a` to completion; `return (await b)` then drops the
completed-but-un-awaited `a`. The awaited sibling's result (20) is intact, and the live count balances to zero.
```maxon

function ten() returns int
	Runtime.yield()
	return 10
end 'ten'

function twenty() returns int
	Runtime.yield()
	return 20
end 'twenty'

function main() returns ExitCode
	_ = async ten()
	let b = async twenty()
	return (await b) as ExitCode
end 'main'
```
```exitcode
20
```

<!-- test: async-promise-drop.never-ran-drop-not-run -->
<!-- targets: x64-windows -->
Pins the divergence from a fire-and-forget-run model: a dropped thread's body NEVER RUNS. `incFlag` would set
the global to 1 if it ran, but `p` is never awaited (nothing drives the scheduler), so `incFlag` is never
scheduled and the global stays 0. The drop cancels the never-run thread; it does not run it.
```maxon
var flag = 0

function incFlag() returns int
	Runtime.yield()
	flag = 1
	return 1
end 'incFlag'

function main() returns ExitCode
	_ = async incFlag()
	return flag as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: async-promise-drop.spawn-drop-loop-bounded -->
<!-- targets: x64-windows -->
The real #88 leak shape: a loop that spawns a promise every iteration and never awaits it. Each iteration's
promise is dropped at the loop body's scope exit — unlinked from the run queue (so it cannot be run by a later
drive) and its struct recycled onto the free-list, which the next spawn reuses. Memory stays bounded across
1000 iterations and the live count balances to zero (exit 0). Before #88 each iteration bump-leaked its struct
and seed stack, invisible to `__mm_alloc_count`.
```maxon

function trivial() returns int
	Runtime.yield()
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
```
```exitcode
0
```

<!-- test: async-promise-drop.parked-timer-drop-cancel -->
<!-- targets: x64-windows -->
A promise PARKED on a timer is dropped-cancelled at scope exit. `slow` sleeps 200 ms (parks on the timer);
`fast` completes immediately. `await q` drives the scheduler: `slow` runs first, parks on its timer and yields;
`fast` then runs to completion, so `await q` returns 42 while `slow` is still parked. `return r` drops `slow` —
the `waiting` arm removes it from the timer store, frees its stack and reclaims its struct — with NO hang (the
200 ms timer is never waited on) and NO use-after-free (the netpoller never touches the freed `slow`). The live
count balances to zero.
```maxon

function sleeper() returns int
	sleep(200)
	return 99
end 'sleeper'

function fast() returns int
	Runtime.yield()
	return 42
end 'fast'

function main() returns ExitCode
	_ = async sleeper()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-promise-drop.parked-timer-drop-through-a-rearm -->
<!-- targets: x64-windows -->
⛔⛔ **THE CASE ABOVE DOES NOT REACH THE `waiting` ARM, AND ITS OWN DESCRIPTION SAYS IT DOES.** It reads *"`return
r` drops `slow` — the `waiting` arm removes it from the timer store"*, and `_ = async sleeper()` DISCARDS the
promise at its own statement, before `await q` has driven anything: `sleeper` has never run, its status is the
slab's `ready`, and the drop takes the QUEUED arm. So the timer-store deregistration had no committed case at
all. **MEASURED at SV2, by a sabotage that should have turned that case red and did not.**

This one really does drop a TIMER-PARKED promise, and the RE-ARM is what makes it so: `await q` drives, which
runs `sleeper` up to its `sleep(200)` and parks it on the timer; only THEN does `p = async fast()` renounce it.
The `waiting` arm removes it from the store, frees the stack a parked coroutine is suspended on and reclaims
the struct — with no hang (the 200 ms deadline is never waited on) and no use-after-free.

⭐ Its own RED reading: point `__gt_promise_drop`'s park-kind refusal at `GtParkKindTimer` instead of
`GtParkKindMailbox` and this exits **94** where it exits 42.
```maxon

function sleeper() returns int
	sleep(200)
	return 99
end 'sleeper'

function fast() returns int
	Runtime.yield()
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
```
```exitcode
42
```

<!-- test: async-promise-drop.branch-await-one-drop-other -->
<!-- targets: x64-windows -->
The path-sensitive case: a promise `await`ed on ONE branch and DROPPED on the other, reconciled at the merge.
`pick` spawns `p`, then on `x > 0` awaits it (the runtime reclaims the struct at the await) and on the else
path lets it drop at scope exit (the `ready` arm cancels the never-run thread). Compiling ONE body with both
fates — the awaited binding is `movedFrom`, the else path is live — must drop `p` on exactly the else path and
NOT double-drop the awaited one. `pick(1)` returns 7 (awaited); `pick(0)` returns 0 (dropped, never ran); their
sum is 7, and the live count balances to zero across both calls (no GT-leak abort 75).
```maxon

function compute() returns int
	Runtime.yield()
	return 7
end 'compute'

function pick(x int) returns int
	let p = async compute()
	if x > 0 'branch'
		return (await p) as int
	end 'branch'
	return 0
end 'pick'

function main() returns ExitCode
	let a = pick(1)
	let b = pick(0)
	return (a + b) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-promise-drop.parked-subprocess-drop-cancel -->
<!-- targets: x64-windows -->
The process-store twin of the parked-timer case. `slowProc` spawns a child that runs for ~2 s and parks on the
process store; `fast` completes immediately, so `await q` returns 42 while `slowProc` is still parked on its
child. `return r` drops `slowProc` — the `waiting` arm scans the process store, `CloseHandle`s the child (abandon
the WAIT, do not kill the child), swaps the entry out and frees the stack. The abandoned child runs to completion
independently; the program exits promptly with 42 and the live count balances to zero.
```maxon

function slowProc() returns int
	return try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99
end 'slowProc'

function fast() returns int
	Runtime.yield()
	return 42
end 'fast'

function main() returns ExitCode
	_ = async slowProc()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-promise-drop.rearm-var-across-loop -->
<!-- targets: x64-windows -->
Re-arming a promise `var` INSIDE A LOOP is not phi-blind (P1.5-B2 #88, review Finding 1): the loop-header phi
carries the promise mark, so each iteration DROPS the previous thread (cancelling it) and the last one drops at
scope exit — the live count balances to zero (exit 0). Before the fix the loop body emitted no drop (the phi was
unmarked, so the re-arm saw no live thread to drop) and the scope-exit drop misrouted to `__mm_decref` on a GT
pointer, corrupting the heap count (exit 101).
```maxon
function trivial() returns int
	Runtime.yield()
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
```
```exitcode
0
```

<!-- test: async-promise-drop.rearm-var-across-branch -->
<!-- targets: x64-windows -->
Re-arming a promise `var` in ONE ARM OF A BRANCH is likewise not phi-blind (Finding 1): the if-continuation phi
merges the re-armed thread and the untouched one, both marked promises, so the scope-exit drop cancels whichever
the taken path holds — balanced on every path (exit 0). Before the fix this exited 101.
```maxon
function trivial() returns int
	Runtime.yield()
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
```
```exitcode
0
```

<!-- test: async-promise-drop.await-rearmed-var -->
<!-- targets: x64-windows -->
AWAITING a promise `var` re-armed across a branch works and yields the re-armed thread's result (Finding 1): the
merge phi is recognised as a promise, and its awaited result type is recovered by tracing the phi's incoming
`async` calls. `positive()` is true, so `p` holds the re-armed thread; `await p` returns 7 and both threads are
accounted for (the original dropped at the re-arm, the re-armed one awaited). Before the fix `await p` on the
phi was rejected E2015 ("not a promise").
```maxon
function seven() returns int
	Runtime.yield()
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
```
```exitcode
7
```

<!-- test: async-promise-drop.await-rearmed-loop-var -->
<!-- targets: x64-windows -->
AWAITING a promise `var` re-armed INSIDE A LOOP, from AFTER the loop — the loop-EXIT phi (P1.5-B2 #88; this
closes residual #89's E2015 over-rejection of an `await` on a block-arg-carried promise). `p` is re-armed each
iteration (dropping the previous thread); after the loop `await p` targets the loop-exit phi, whose promise mark
and awaited result type are recovered by tracing the phi's incoming `async` calls. It returns the LAST spawn's
result (5), every intermediate thread dropped and the live count balanced. Before #88 the phi-carried promise
was rejected E2015 ("not a promise") at the `await`.
```maxon
function five() returns int
	Runtime.yield()
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
function compute() returns int
	return 7
end 'compute'

function main() returns ExitCode
	var p = async compute()
	let q = p
	p = async compute()
	let r = await q
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:2: Unsupported: cannot re-arm the promise binding ('p'): its green thread is still named by an alias, so dropping it here would leave that alias dangling — `await` through one name before re-arming
```

<!-- test: async-promise-drop.await-then-reassign-nonpromise -->
<!-- targets: x64-windows -->
Once a promise's thread has been AWAITED, its binding owns nothing, so reassigning it to a plain scalar is fine
(Finding 4): `await p` reclaims the thread, then `p = 5` makes `p` an ordinary int. It is neither dropped (the
binding owes nothing) nor use-after-move (a scalar binding is always readable), so `return p` is 5. Before the
fix this was wrongly rejected E2015.
```maxon
function nine() returns int
	Runtime.yield()
	return 9
end 'nine'

function main() returns ExitCode
	var p = async nine()
	let r = await p
	print("{r}")
	p = 5
	return p as ExitCode
end 'main'
```
```exitcode
5
```
```stdout
9

```

<!-- test: async-promise-drop.branch-store-into-a-container-on-both-arms -->
<!-- targets: x64-windows -->
⭐ **STORING THE SPAWN IS A CONSUMING MOVE ON *EVERY* ARM THAT DOES IT, INCLUDING THE SECOND ONE THE PARSER
READS.** A dispatcher arms a slot through two doors — `push` the first time a slot exists, `set` every time
after — so ONE `async` spawn reaches a merge having been given to the container on both paths, and the merge
must therefore reconcile NOTHING. If either door fails to record the move, `reconcileMovesAtMerge` sees the
binding live on that edge, emits `__gt_promise_drop` there, and the container is left holding a **cancelled**
thread: it never completes, so a non-blocking poller waits on it forever. `arm` is called twice, taking a
different door each time; each stored thread must run to completion under the drive and be awaitable for its
value, so the sum is 42. The bounded drive is what makes a cancelled thread a VERDICT (exit 2) instead of a
hang.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

let MaxSpins = 200

function value(n Integer) returns Integer
	Runtime.yield()
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
		Runtime.yield()
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
<!-- targets: x64-windows -->
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
	Runtime.yield()
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
		Runtime.yield()
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
