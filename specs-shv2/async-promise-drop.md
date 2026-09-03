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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A spawned green thread that is never awaited is DROPPED at scope exit: it is unlinked from the run queue, its
seed stack freed and its struct reclaimed, and `__gt_live_count` balances to zero — so the program exits with
`main`'s own code (0), not the GT-leak abort (75). Before #88 this spawn leaked its struct + stack silently.
```maxon

function trivial() returns Integer
	Runtime.yield()
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A sibling promise that COMPLETES as a side effect of driving another await is dropped through the `completed`
arm — its struct is reclaimed WITHOUT double-freeing the stack (`__gt_drive_until` already freed it at
completion). `await b` drives the FIFO run queue and runs `a` to completion; `return (await b)` then drops the
completed-but-un-awaited `a`. The awaited sibling's result (20) is intact, and the live count balances to zero.
```maxon

function ten() returns Integer
	Runtime.yield()
	return 10
end 'ten'

function twenty() returns Integer
	Runtime.yield()
	return 20
end 'twenty'

function main() returns ExitCode
	_ = async ten()
	let b = async twenty()
	return (await b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
20
```

<!-- test: async-promise-drop.never-ran-drop-not-run -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Pins the divergence from a fire-and-forget-run model: a dropped thread's body NEVER RUNS. `incFlag` would set
the global to 1 if it ran, but `p` is never awaited (nothing drives the scheduler), so `incFlag` is never
scheduled and the global stays 0. The drop cancels the never-run thread; it does not run it.
```maxon
var flag = 0

function incFlag() returns Integer
	Runtime.yield()
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The real #88 leak shape: a loop that spawns a promise every iteration and never awaits it. Each iteration's
promise is dropped at the loop body's scope exit — unlinked from the run queue (so it cannot be run by a later
drive) and its struct recycled onto the free-list, which the next spawn reuses. Memory stays bounded across
1000 iterations and the live count balances to zero (exit 0). Before #88 each iteration bump-leaked its struct
and seed stack, invisible to `__mm_alloc_count`.
```maxon

function trivial() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.parked-timer-drop-cancel -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A promise PARKED on a timer is dropped-cancelled at scope exit. `slow` sleeps 200 ms (parks on the timer);
`fast` completes immediately. `await q` drives the scheduler: `slow` runs first, parks on its timer and yields;
`fast` then runs to completion, so `await q` returns 42 while `slow` is still parked. `return r` drops `slow` —
the `waiting` arm removes it from the timer store, frees its stack and reclaims its struct — with NO hang (the
200 ms timer is never waited on) and NO use-after-free (the netpoller never touches the freed `slow`). The live
count balances to zero.
```maxon

function sleeper() returns Integer
	sleep(200)
	return 99
end 'sleeper'

function fast() returns Integer
	Runtime.yield()
	return 42
end 'fast'

function main() returns ExitCode
	_ = async sleeper()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.parked-timer-drop-through-a-rearm -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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

function sleeper() returns Integer
	sleep(200)
	return 99
end 'sleeper'

function fast() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.branch-await-one-drop-other -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The path-sensitive case: a promise `await`ed on ONE branch and DROPPED on the other, reconciled at the merge.
`pick` spawns `p`, then on `x > 0` awaits it (the runtime reclaims the struct at the await) and on the else
path lets it drop at scope exit (the `ready` arm cancels the never-run thread). Compiling ONE body with both
fates — the awaited binding is `movedFrom`, the else path is live — must drop `p` on exactly the else path and
NOT double-drop the awaited one. `pick(1)` returns 7 (awaited); `pick(0)` returns 0 (dropped, never ran); their
sum is 7, and the live count balances to zero across both calls (no GT-leak abort 75).
```maxon

function compute() returns Integer
	Runtime.yield()
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The process-store twin of the parked-timer case. `slowProc` spawns a child that runs for ~2 s and parks on the
process store; `fast` completes immediately, so `await q` returns 42 while `slowProc` is still parked on its
child. `return r` drops `slowProc` — the `waiting` arm scans the process store, `CloseHandle`s the child (abandon
the WAIT, do not kill the child), swaps the entry out and frees the stack. The abandoned child runs to completion
independently; the program exits promptly with 42 and the live count balances to zero.
```maxon

function slowProc() returns Integer
	return try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99
end 'slowProc'

function fast() returns Integer
	Runtime.yield()
	return 42
end 'fast'

function main() returns ExitCode
	_ = async slowProc()
	let q = async fast()
	let r = await q
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: async-promise-drop.rearm-var-across-loop -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Re-arming a promise `var` INSIDE A LOOP is not phi-blind (P1.5-B2 #88, review Finding 1): the loop-header phi
carries the promise mark, so each iteration DROPS the previous thread (cancelling it) and the last one drops at
scope exit — the live count balances to zero (exit 0). Before the fix the loop body emitted no drop (the phi was
unmarked, so the re-arm saw no live thread to drop) and the scope-exit drop misrouted to `__mm_decref` on a GT
pointer, corrupting the heap count (exit 101).
```maxon
function trivial() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.rearm-var-across-branch -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Re-arming a promise `var` in ONE ARM OF A BRANCH is likewise not phi-blind (Finding 1): the if-continuation phi
merges the re-armed thread and the untouched one, both marked promises, so the scope-exit drop cancels whichever
the taken path holds — balanced on every path (exit 0). Before the fix this exited 101.
```maxon
function trivial() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: async-promise-drop.await-rearmed-var -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
AWAITING a promise `var` re-armed across a branch works and yields the re-armed thread's result (Finding 1): the
merge phi is recognised as a promise, and its awaited result type is recovered by tracing the phi's incoming
`async` calls. `positive()` is true, so `p` holds the re-armed thread; `await p` returns 7 and both threads are
accounted for (the original dropped at the re-arm, the re-armed one awaited). Before the fix `await p` on the
phi was rejected E2015 ("not a promise").
```maxon
function seven() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: async-promise-drop.await-rearmed-loop-var -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
AWAITING a promise `var` re-armed INSIDE A LOOP, from AFTER the loop — the loop-EXIT phi (P1.5-B2 #88; this
closes residual #89's E2015 over-rejection of an `await` on a block-arg-carried promise). `p` is re-armed each
iteration (dropping the previous thread); after the loop `await p` targets the loop-exit phi, whose promise mark
and awaited result type are recovered by tracing the phi's incoming `async` calls. It returns the LAST spawn's
result (5), every intermediate thread dropped and the live count balanced. Before #88 the phi-carried promise
was rejected E2015 ("not a promise") at the `await`.
```maxon
function five() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'int' to variable 'p', which holds 'struct'
```

<!-- test: async-promise-drop.branch-store-into-a-container-on-both-arms -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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

<!-- test: async-promise-drop.a-container-drops-its-un-awaited-elements -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
error E3141: <fragment>:14:17: a promise cannot be borrowed through 'last': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. Read it through a door that names its slot (`get(i)`, `first()`, or `for … in`), or move it out with `pop`/`remove`
```

<!-- test: async-promise-drop.error.a-cursor-over-a-container-of-promises -->
A cursor's `current()`/`peek()` hand back an element without naming an index into the container, so a
promise read through one has no slot to empty when it is consumed — and, unrefused, the `await` and the
array's own element walk each reclaim the same green thread (measured: exit 75).

⚠ **THE REFUSAL IS DECIDED ON THE VALUE THE READ PRODUCED, NOT ON THE CONTAINER IT WAS ASKED OF.** A cursor
accessor is dispatched on the CURSOR's instance rather than the array's, so a rule keyed on the receiver's
element type answers `false` about its own subject; an early draft did exactly that and let this program
through. Asking what came BACK cannot be dodged that way.
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
```maxoncstderr
error E3141: <fragment>:15:13: a promise cannot be borrowed through 'current': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. Read it through a door that names its slot (`get(i)`, `first()`, or `for … in`), or move it out with `pop`/`remove`
```

<!-- test: async-promise-drop.a-move-out-hands-the-thread-over -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
