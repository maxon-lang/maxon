---
feature: sched-processor
status: stable
keywords: [scheduler, green-threads, processor, worker, multi-M, GMP, async, MAXON_MAX_PROCS]
category: system
---

# The scheduler's PROCESSOR (P) and its worker M

## Documentation

the compiler's green-thread runtime is split across two files, and the split is the subject of this spec.
`Compiler/Runtime/GtRuntime.maxon` owns the GREEN THREAD (G) — its struct, its stack, the global FIFO
run queue, the timer store. `Compiler/Runtime/SchedRuntime.maxon` owns the two things a
green thread alone cannot express:

| | |
|---|---|
| **M** | the OS thread that is running a green thread right now |
| **P** | the per-processor state that OS thread HOLDS while it does |

Go's model.

### The M and the P are TWO STRUCTS, and TLS points at the M

⭐⭐ **THE RULE THAT SEPARATES THEM IS ONE QUESTION ASKED OF EVERY FIELD:** ***if this thread's P were
taken away while it is blocked in a kernel call, would this field still be true of it?***

| on the **M** | on the **P** |
|---|---|
| `currentP` — the processor it holds, **or 0** | `id`, the shard index and the run-queue ring |
| `currentGt` — the green thread it is executing | the steal count |
| `systemStackSP` — its own 64 KB syscall stack | the remote-free queue |
| its inline scheduler green thread (Go's `g0`) | `status`, and its link on the idle-P list |
| **its park event, and its place on the idle-M list** | |
| **the spinning bit** | |
| **its OS thread handle, and its link on the roster** | |

⚠ **`M->currentP` IS THE FIELD THE SPLIT EXISTS TO CREATE.** *"This thread holds no processor"* is one load
and one compare, and it can be written down only because TLS points at the machine rather than the
processor.

⛔⛔ **A `currentP` OF 0 HAS A PRODUCER — THE THREE ROWS IN BOLD ARE ITS.** **A worker M that finds nothing
to run RELEASES its processor onto the idle-P list and parks on its OWN event with `currentP == 0`**, and a
waker takes a processor and a machine off the two lists and binds them. So an idle machine does not occupy
a processor, and a processor can sit idle with no machine.

⚠ **A MACHINE BUSY INSIDE A BLOCKING KERNEL CALL IS THE OTHER HALF**, and `sched-syscall-handoff.md` owns
it: `entersyscall`/`handoffp`/`sysmon`, where `handoffp`'s last step — *park the P, not the M* — is this
same idle-P list.

⚠ **THE HOT READS ARE TWO LOADS.** The syscall shim and the prologue stack guard want `currentGt` and
the syscall stack, and both are the M's, so they are two loads through the TLS slot. Only a reader that wants the PROCESSOR pays the extra field load — the allocator's shard read, and
the scheduler's own per-schedule walks.

### Everything `async` creates runs on one machine at a time — and `spawn` is what makes a second worth starting

⚖ **AN `async f(…)` CALL DOES NOT CREATE A GREEN THREAD** (user ruling). It creates a COROUTINE
of the green thread that called it, which joins that green thread's strand and runs only on the machine
holding the strand — `sched-runqueue.md` is where that is stated in full. **A coroutine is never a token on
a run queue**, so an `async`-only program publishes one token, `main`'s, and a program whose only green
thread is `main` needs a second M only when the system monitor steps in — a preemption, a retake from a
kernel call, or an overdue timer (`builtins-cpu-parallel.md` pins that exactly).

⛔ **THAT IS A STATEMENT ABOUT `async`, NOT ABOUT THE PROCESS.** **`spawn` creates real green threads**,
which is exactly what a ring, a steal and a worker loop schedule, and **the default is the machine's
processor count** (`sched-default-procs.md`). An ordinary build therefore has one P per processor, and
whether a second M starts is decided by the WORK — a service program runs on several, an `async`-only
program runs on one.

⭐ `scripts/multicore-stress/pin-matrix.sh` drives the `multicore-stress` programs across
`MAXON_MAX_PROCS ∈ {1, 2, 7, 12}` and at the default. `steal-torture` — which is `async` — reads
`workers=1 steals=0` at every one; the two SPAWN-driven programs read the other way, which is the same
script's other family.

⚠ **WHAT AN `async`-ONLY PROGRAM EXERCISES IS STILL LESS OF THE STRUCTURE.** `main` is a green thread in
every green-thread program, so one machine's whole road runs in it and is covered by the cases below: the
**TLS indirection** (every green thread reaches `currentGt` through the M its TLS slot names, which is every
async case in the corpus), the **per-M syscall stack** (`a-green-thread-kernel-call-round-trips-its-processor-stack`),
`main`'s token going through its processor's ring, the scheduler loop's search, and the machine's park when
it has nothing to run. What such a program never reaches is a SECOND machine: no worker loop runs and nothing
is stolen. A program that spawns reaches all of it. ⚠⚠ **NOTHING UNREACHED IS DELETED, AND EVERY UNREACHED
PIECE SAYS SO AT ITS OWN DECLARATION.**

⛔ **THE ALLOCATOR IS SHARDED PER P, AND THE REFCOUNT READ-MODIFY-WRITE IS PLAIN, unconditionally**,
because pinning `async` removed the second party an atomic would serialise against
(`MmRuntime.emitAdjustRefcount` carries the argument and the committed control).

⚠ **`alloc-torture` AND `remote-free-torture` DO NOT REACH THE CROSS-M PATHS.** With no worker M, they run
entirely on one M and do not touch the per-P mcache handoff, the remote-free MPSC queue or the span
ownership gate. A green run of either does not cover them. ⭐ **A DIFFERENT PROGRAM DOES:** `service-torture` and `service-fanin-torture` move 4,800 heap
`String`s each ACROSS Ms, so a record allocated on one M is released on another — which is the remote-free
push. `scripts/multicore-stress/README.md` states which rows cover what, once, where the programs are.

⛔ **A SPEC CASE SETS THE KNOB WITH A MARKER.** `<!-- procs: N -->` sets it
(`sched-default-procs.md` owns the marker), and a case that carries NO marker runs at the machine's count. ⇒ **every case below runs multi-M**, and **none of them
carries a `procs:` marker**, which is a claim about their SUBJECT rather than an oversight: every case here
measures a coroutine, and an `async` coroutine is published to its owner green thread's own queue and is
never stolen, so its answer cannot depend on how many processors exist. The marker lives where a case's
subject really is a count — `sched-default-procs.md` owns it, and the five ring/global-queue ordering cases
in `sched-runqueue.md` carry `procs: 1`. `pin-matrix.sh` is the instrument for the counts a spec
case has no reason to name.

### What the M and the P replaced, and why a `.data` word could not stay

Two facts cannot be `.data` globals. **Both belong to the M rather than to the P** — they are properties
of the thread that is EXECUTING, and the argument for each is the same argument one level up:

- **The running green thread.** Two Ms run two different green threads at the
  same instant, so a single word answers whichever wrote last. It is `M->currentGt`, read through
  two loads and no call: `mov reg, [__sched_tls_teb_offset]` then `mov reg, gs:[reg]`, which IS
  `TlsGetValue` for a slot in the TEB's inline 64, and then one field load. The thread executing inside a
  kernel call belongs to the M blocked there, not to the processor a handoff can take away — and the
  syscall shim reads this word again AFTER the call, to restore the TIB.
- **The 64 KB scratch stack a green thread's Win32 calls run on.** One global region is safe only while
  one M runs at a time: two Ms inside kernel calls would set RSP to the same top and write the same
  region, as silent interleaved corruption rather than a fault. It is `M->systemStackSP` — one region per
  OS thread that runs Maxon code, committed as that thread becomes an M. ⛔ **A per-P region would make
  the invariant *"an M can only reach its own P"*, and that is exactly the sentence P migration
  falsifies**: a P handed to a second M while the first is still
  inside a kernel call puts two Ms back on one region, with the `.data` global's failure shape restored
  in full. *"One OS thread runs one thing at a time"* is true unconditionally, which is why the region is
  the M's.

### A spawned green thread is PUBLISHED LAST, and that ordering is load-bearing

`__gt_spawn` creates a green thread and returns it UNQUEUED; the lowering fills its inline argument
slots and its releaser, and only then calls `__gt_ready`, which puts it on its strand queue and — when no
machine holds that strand — publishes the strand's token and wakes an idle M.

⚠ **THE SPLIT IS WHAT KEEPS A WORKER FROM RUNNING A THREAD BEFORE ITS ARGUMENTS EXIST.** A thread
enqueued inside `__gt_spawn` can be dequeued and run by a worker M while the spawning thread is still
storing its arguments, and the callee then reads the allocator's zeros — a wrong sum, not a crash.
Publishing last removes that window rather than narrowing it.

## Tests

<!-- test: sched-processor.spawn-carries-every-argument -->
**THE FULL INLINE ARGUMENT REGION, AND THE SUBJECT OF THE PUBLISH-LAST ORDERING.** `MaxAsyncArgs` is
six, so this is the widest spawn the language admits: every slot of the GT struct's inline region is
written by the lowering and read back by the trampoline. A slot missed, mis-strided or read before it
was written shows up as a wrong sum rather than as a crash — which is exactly how a publish
before the arguments are filled presents.
```maxon
function widest(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	__Builtins.parallelBoundary()
	return a + b * 2 + c * 4 + d * 8 + e * 16 + f * 32
end 'widest'

function main() returns ExitCode
	let p = async widest(1, b: 1, c: 1, d: 1, e: 1, f: 1)
	return await p as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
63
```

<!-- test: sched-processor.many-green-threads-through-one-processor -->
Thirty-two coroutines spawned before any is awaited, so all thirty-two are on `main`'s strand queue at
once and one machine runs every one of them: the scheduler's `currentGt` is written and restored around
each switch, and an M that lost track of which thread it was running would return one thread's result
for another. The sum is index-derived, so any mis-pairing lands on a different number.
```maxon
function step(n Counted) returns Counted
	__Builtins.parallelBoundary()
	return n * 3
end 'step'

typealias Counted = int(i64.min to i64.max)
typealias CountedPromise = Promise with Counted
typealias CountedPromiseArray = Array with CountedPromise

function main() returns ExitCode
	var promises = CountedPromiseArray.create()

	for i in 0 upto 32 'spawn'
		promises.push(async step(i))
	end 'spawn'

	var total = 0

	for p in promises 'await'
		total = total + await p
	end 'await'

	if total == 1488 'expected'
		return 7
	end 'expected'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: sched-processor.a-green-thread-kernel-call-round-trips-its-processor-stack -->
**THE PER-M SYSCALL STACK, END TO END.** A green thread's `sleep` is a Win32 call, which the syscall
shim runs on a scratch stack instead of the thread's own 2 KB one — and that scratch stack is reached
through a struct field rather than a global, `M->systemStackSP`. ⚠ The case's NAME says *processor* while
the stack is the M's; what it exercises is the shim's whole chain. The shim must find this thread's M, switch RSP to its stack, repoint the TIB,
call, restore the TIB from the green thread and switch back; a wrong offset anywhere in that chain
corrupts the return path rather than producing a wrong number, so the assertion is that the thread
returns AT ALL with its own value intact.
```maxon
function napper(n Integer) returns Integer
	__Builtins.sleep(2)
	return n + 5
end 'napper'

function main() returns ExitCode
	let first = async napper(1)
	let second = async napper(2)
	let a = await first
	let b = await second
	return (a + b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
13
```

<!-- test: sched-processor.a-scalar-program-still-runs-with-no-processor-at-all -->
The negative control, and it is target-neutral on purpose: a program with no green thread installs no
scheduler, so it has no M, no P, no TLS slot and no `__sched_*` `.data` word — and the prologue stack
guard every function in a green-thread image carries is not emitted for it either.

⚠ **ITS FAILURE MODE IS A COMPILE-TIME PANIC, NOT A RUNTIME FAULT**, and that is worth stating because
it changes what the case is for: a `globalAddr __sched_tls_teb_offset` in an image whose `.data` never
laid that slot out is a BACKEND RELOCATION PANIC (the trap `DebugStreamRuntime` records for `__ds_base`
in an untraced build). So this asserts that the M/P indirection stayed behind `usesGt` — a property every
other scalar spec in the suite also happens to assert, which is why this one is a control rather than a
discovery.
```maxon
function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function main() returns ExitCode
	return twice(21) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: sched-processor.idle-processor-count-before-any-work -->
<!-- procs: 4 -->
**THE IDLE-PROCESSOR COUNT, GO'S `sched.npidle`.** Nothing spawned: the main thread holds one processor and
every other one sits on the idle list. `__Builtins.schedIdleProcessorCount()` reads the count the scheduler
keeps beside that list. Go reads the same number on every wake decision — whether to start a spinning machine
when a processor is handed off, and how many spinners are worth having — which is why it is a maintained word
rather than a walk of the list.

⚠ **IT ASSERTS AN AGREEMENT AND NOT A NUMBER.** The scheduler takes `procs: 4` exactly on every host, and
the case checks the idle count against `schedProcessorCount() - 1`, the count the scheduler itself resolved,
so the two readings of one scheduler must agree. The idle count is read first, because it is the query that
brings the scheduler up.
```maxon
function main() returns ExitCode
	let idle = __Builtins.schedIdleProcessorCount()
	print("everyButMain={idle == __Builtins.schedProcessorCount() - 1}\n")
	return 0
end 'main'
```
```stdout
everyButMain=true
```
```exitcode
0
```

<!-- test: sched-processor.idle-processor-count-returns-after-a-burst -->
<!-- procs: 4 -->
**THE COUNT FOLLOWS THE PROCESSORS BACK TO THE IDLE LIST.** Four services each do a share of work on
whatever machines the scheduler starts for them, and every reply is awaited. Once the last worker has found
nothing to run it releases its processor, so the count settles back at every processor but the main thread's
— an agreement with `schedProcessorCount()`, for the case above's reason. The settling is waited for on a
bounded budget rather than sampled once: a worker that has just finished a handler is still searching for a
moment before it parks, and a single read taken in that moment would report a machine that is about to be idle
as busy.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ShareHandleArray = Array with Share.handle
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

let shares = 4
let settleTurns = 3000

type Share
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function work(rounds Integer) returns Integer
		var acc = 0
		var i = 0
		while i < rounds 'spin'
			acc = (acc + i * self.id) mod 1000003
			i = i + 1
		end 'spin'
		return acc
	end 'work'
end 'Share'

function main() returns ExitCode
	var hs = ShareHandleArray.create()
	var i = 0
	while i < shares 'spawnEach'
		hs.push(spawn Share.create(i + 1))
		i = i + 1
	end 'spawnEach'
	var replies = ReplyPromiseArray.create()
	var k = 0
	while k < shares 'sendEach'
		let h = try hs.get(k) otherwise panic("hs.get OOB at {k} — bounded by the pushes above")
		replies.push(h.work(200000))
		k = k + 1
	end 'sendEach'
	var total = 0
	var n = 0
	while n < shares 'collect'
		let p = try replies.get(n) otherwise panic("replies.get OOB at {n} — bounded by the pushes above")
		total = total + (try await p otherwise 0)
		n = n + 1
	end 'collect'
	let everyButMain = __Builtins.schedProcessorCount() - 1
	var turn = 0
	while turn < settleTurns and __Builtins.schedIdleProcessorCount() != everyButMain 'settle'
		sleep(1)
		turn = turn + 1
	end 'settle'
	let settled = __Builtins.schedIdleProcessorCount() == everyButMain
	print("total={total} settled={settled}\n")
	return 0
end 'main'
```
```stdout
total=2400012 settled=true
```
```exitcode
0
```

<!-- test: sched-processor.error.idle-processor-count-arity-checked -->
The query takes no argument, like every member of its family.
```maxon
function main() returns ExitCode
	return __Builtins.schedIdleProcessorCount(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.schedIdleProcessorCount' takes exactly 0 argument, but 1 were given
```

<!-- test: sched-processor.error.idle-processor-count-is-refused-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The count is a fact about processors, and a WASI component has none to count, so the query is refused where
the green-thread substrate is: at the call's own span, with E3104.
```maxon
function main() returns ExitCode
	return __Builtins.schedIdleProcessorCount() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__sched_idle_processor_count', which has no wasm32-wasi implementation
```

<!-- test: sched-processor.error.gt-records-carved-arity-checked -->
The record-carve count takes no argument, like every member of its family.
```maxon
function main() returns ExitCode
	return __Builtins.schedGtRecordsCarved(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.schedGtRecordsCarved' takes exactly 0 argument, but 1 were given
```

<!-- test: sched-processor.error.gt-records-carved-is-refused-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The count is a fact about the scheduler's record arena, and a WASI component has no green threads to carve
records for, so the query is refused where the green-thread substrate is: at the call's own span, with E3104.
```maxon
function main() returns ExitCode
	return __Builtins.schedGtRecordsCarved() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__sched_gt_records_carved', which has no wasm32-wasi implementation
```
