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

## Tests

<!-- test: async-promise-drop.never-ran-drop-no-leak -->
<!-- targets: x64-windows -->
A spawned green thread that is never awaited is DROPPED at scope exit: it is unlinked from the run queue, its
seed stack freed and its struct reclaimed, and `__gt_live_count` balances to zero — so the program exits with
`main`'s own code (0), not the GT-leak abort (75). Before #88 this spawn leaked its struct + stack silently.
```maxon

function trivial() returns int
	return 0
end 'trivial'

function main() returns ExitCode
	let p = async trivial()
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
	return 10
end 'ten'

function twenty() returns int
	return 20
end 'twenty'

function main() returns ExitCode
	let a = async ten()
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
	flag = 1
	return 1
end 'incFlag'

function main() returns ExitCode
	let p = async incFlag()
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
	return 0
end 'trivial'

function main() returns ExitCode
	var i = 0
	while i < 1000 'loop'
		let p = async trivial()
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
	return 42
end 'fast'

function main() returns ExitCode
	let slow = async sleeper()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
```
```exitcode
42
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
	return runProcess("cmd /c ping -n 3 127.0.0.1 >nul")
end 'slowProc'

function fast() returns int
	return 42
end 'fast'

function main() returns ExitCode
	let slow = async slowProc()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
```
```exitcode
42
```
