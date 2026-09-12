---
feature: builtins-cpu-parallel
status: stable
keywords: [builtins, __Builtins, cpuCount, schedMaxActiveWorkers, intrinsics, parallel, scheduler, green-threads]
category: system
---

# `__Builtins.cpuCount()` and `__Builtins.schedMaxActiveWorkers()`

## Documentation

`__Builtins` is the compiler's builtin TYPE, whose static methods are INTRINSICS rather than
functions any file declares. This spec pins the two QUERIES of the CPU-parallel family — the third
member, `__Builtins.parallelBoundary()`, is a marker rather than a query and has its own spec:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.cpuCount()` | how many logical CPUs the OS reports for this MACHINE, as an `int`, never below 1 |
| `__Builtins.schedMaxActiveWorkers()` | the high-water mark of concurrently-active green-thread worker Ms this PROCESS has had, as an `int`, never below 1 |

Both take no arguments. Their one caller in this tree is `scripts/multicore-stress/alloc-torture.maxon`, the
multi-core validation harness, which prints both so its driver can sweep the core-count clamp
(`cpucount=`) and see that more than one core actually ran the work (`workers=`).

They read as a pair and they are NOT the same kind of question, which is the whole reason this file
documents them together:

- `cpuCount` asks the MACHINE how parallel a run COULD be. It is an OS call.
- `schedMaxActiveWorkers` asks this compiler's own SCHEDULER how parallel a run WAS. It is a
  property of the emitted runtime and reaches no OS.

### `schedMaxActiveWorkers` for an `async` program is 1 UNTIL ITS GREEN THREAD REACHES THE SCHEDULER

The compiler HAS a worker M — `SchedRuntime.buildSchedWorkerLoop` — and a high-water counter that loop
raises on every entry, which is what this intrinsic reads. ⚖ **An `async` call creates a COROUTINE of the
calling green thread** (user ruling, 2026-08-27), published only to its owner's queue, so a coroutine never
gives a worker M anything to take. A second machine comes from the system monitor, or from the program's own
green thread — `main`'s, in an `async`-only program — asking to be put behind everyone:

- **Preemption.** The monitor preempts a green thread that has held its processor for 10 ms, at its next
  function prologue, and puts it on the global queue behind every other runnable green thread
  (`sched-preempt.md`). It may resume on another OS thread. `__Builtins.schedPreemptCount()` counts these.
- **A retake.** A kernel call long enough for the monitor to retake its processor hands that processor to a
  new M when no processor is idle and no M is looking for work (Go's `handoffp`, `sched-syscall-handoff.md`).
  `__Builtins.schedRetakeCount()` counts these.
- **An overdue timer.** The monitor starts a machine on an idle processor for a timer no running machine will
  fire in time. `__Builtins.schedTimerStartCount()` counts these.
- **A `Runtime.yield()` with no sibling coroutine to run** puts the green thread on the global queue the way a
  preemption does (`runtime-yield.md`).

Each global put pays a wake — Go's `wakep` — and a wake starts a worker M when a processor is idle. MEASURED
on this tree: an `async` program whose one coroutine spins for 100 ms reads `workers=1` at
`MAXON_MAX_PROCS=1` and `workers=2` at 2, at 12 and at the default, with six preemptions in each run.
⇒ **An `async`-only program that never yields alone reads 1 exactly when the monitor's three counters read 0**,
and that conditional — not a bare `workers == 1` a descheduled run can break — is what the case below
asserts.

⛔ **IT IS NOT 1 IN EVERY PROGRAM, AND THIS SECTION USED TO SAY IT WAS.** The old sentence — *"the
high-water mark of a population that never exceeds one is 1, in every program"* — rested on there being no
producer of a green thread "until a `spawn` primitive lands". `spawn` has landed. A program that spawns
services publishes real green threads to a P ring, wakes worker Ms and reads this intrinsic above 1;
`multicore-stress/pin-matrix.sh` asserts exactly that, per family. The cases below are `async` programs and their
subject is the `async` half.

⚠ **`__Builtins.schedStealCount()` needs a second M for the same reason**: a steal takes a green thread out of
another P's ring, and no `async` frame ever enters a ring — only a green thread readied onto one can be
stolen, and only by a worker M, which a run that never reaches the scheduler does not start.
`sched-runqueue.md` carries that half.

⚠ **THAT IS A READING, NOT A PLACEHOLDER — AND IT USED TO BE A `ret 1`.** The body was a constant
return for as long as the runtime had no worker loop to raise anything; it is now a `.data` load, and
the slot is SEEDED TO 1 rather than written by an initializer, so a program that never installs the
scheduler still reads the truth (one M: its own) with no code at all. ⚠ **The `workers=2, 7, 11-12`
this used to report under `MAXON_MAX_PROCS ∈ {2, 7, 12}` was measured before EC10 pinned `async`, when
a spawn published a green thread to the scheduler.** `multicore-stress/pin-matrix.sh` asserts `workers=1` for
its coroutine family and `workers >= 2` for the spawn family; the coroutine half is asserted only for a row whose
`monitor=` reading — the three counters' sum — is 0.

⚠ **THE IOCP COMPLETION THREAD IS STILL NOT A WORKER M, AND IT IS THE ONE THING THAT COULD MAKE THIS
LOOK WRONG.** The OS thread `__io_init` creates drains completions and re-readies parked green
threads; it never RUNS one and it never adopts a P.

### What the compiler answers for `alloc-torture.maxon`

`scripts/multicore-stress/alloc-torture.maxon` is the one program in this tree that calls both intrinsics.
MEASURED on a 12-logical-CPU Windows host: `aggregate=205500`, `workers=1`, `cpucount=12`, exit 42.
The `aggregate=` row is the harness's own determinism signal.

⛔ **`workers=1` IS NOT A CONSEQUENCE OF THE PROCESSOR COUNT.** The compiler defaults to the machine's count,
so this program builds 12 Ps. **The `workers=` row is 1 anyway**, because this program's work is
`async` and an `async` frame is a coroutine of its caller — the P count is not what decides whether a
worker M starts, the WORK is — and because its run is too short for the monitor to preempt its green thread.
MEASURED on this tree: `workers=1` at `MAXON_MAX_PROCS ∈ {1, 2, 12}`, each run about 40 ms.

⛔ **THE NOTE HERE WAS STALE TWICE OVER AND BOTH CORRECTIONS ARE MEASURED.** It said
*"`MAXON_MAX_PROCS>1` really does give the compiler worker Ms"* and that this program *"dies with exit 86
(`slabSpanExhaustedPastItsEnd`) at `MAXON_MAX_PROCS=2`"* because the allocator was one unsharded shard.
**S5 sharded the allocator per P**, so the second half was already false; and **EC10 pinned `async`**,
so the first half is false too — this program's tasks are coroutines and no worker M is created at any
value. MEASURED with `multicore-stress/pin-matrix.sh` on this tree: `aggregate=205500`, `workers=1`, exit 42 at
`MAXON_MAX_PROCS ∈ {1, 2, 7, 12}` and at the default. See `sched-processor.md`, which carries the same
correction and the cost that comes with it (the cross-P allocator paths are correct and, for THIS program,
still UNREACHED — `spawn`'s service programs are what reach them).

### The two land on OPPOSITE sides of the target line, and that is the pair's sharpest property

`cpuCount` is an OS query with a different API on every platform — `GetActiveProcessorCount` on
Windows, `sysconf(_SC_NPROCESSORS_ONLN)` under POSIX, and on WASI **nothing at all**: a component
has no OS-thread concept and no primitive that reports one. The compiler lowers it wherever a backend has
landed that read — x64-windows, arm64-macOS and arm64-linux — and every other target refuses at the
call's own span with **E3104** (by the `__cpu_` PREFIX, so a second entry point in that band is gated
by construction rather than by memory). A fabricated `1` on a lane that cannot ask the OS would be a
silent wrong answer, which is strictly worse than a refusal.

⚠ The POSIX half is not a re-spelling of the Windows one, which is why it is a rung and not a
lowering: the two APIs fail differently (`0` against `-1`), and the `_SC_` parameter is itself
numbered per OS. ⭐ **AND THE LINUX LANE INHERITED NOTHING FROM THE macOS ONE, EXACTLY AS THAT SAID:**
`sysconf` is a libc FUNCTION rather than a syscall, so a static image with no libc cannot call it at
all — the read there is `sched_getaffinity` plus a popcount of the returned mask, which answers the
processors this PROCESS may use rather than the ones the machine has, and so agrees with `nproc`
rather than with a machine-wide count. The authority on which target provides it is
`TargetFacilities.targetProvidesFacility`; this paragraph names the lanes only to say
that there is more than one, and the per-case `unsupported-targets:` markers below are what actually gate.

`schedMaxActiveWorkers` is refused NOWHERE, and for a reason of its own rather than by omission: its
whole body is one `.data` load and a `ret`, which lowers on every target the compiler emits — the same
argument `__parallel_boundary`'s empty body makes for its own `__parallel_` band, and a load reaches
no more OS than the constant return this used to be. It therefore wears the
`__sched_` band rather than `__cpu_`, because the two bands answer the question *"may this target
run it"* differently and a prefix test can only give one answer per band.

⇒ The two `on-wasm` cases below are a PAIR and are half the proof each: the refusal case alone
cannot tell a live gate from a compiler that refuses everything, and the acceptance case alone
cannot tell a live gate from one that has stopped refusing.

### `cpuCount` returns the count of ACTIVE logical processors, ALL groups

The compiler reads `GetActiveProcessorCount(ALL_PROCESSOR_GROUPS)` rather than
`GetSystemInfo().dwNumberOfProcessors`. The two agree on every machine with a single processor group
— i.e. every machine with at most 64 logical processors, which is what the difference is about:
`dwNumberOfProcessors` reports the CALLING THREAD's group, so on a 128-processor host it answers 64
while the count of the machine is 128. The compiler takes the direct-return API because its Std tier has no way to hand a backend a 48-byte
`SYSTEM_INFO` scratch buffer without either allocating on the heap for a 4-byte read or putting a
Windows struct layout into the target-neutral tier; the more-correct answer on a multi-group host is
the second reason, not the first.

The clamp to at least 1 lives in builder-built Std rather than in the backend:
`GetActiveProcessorCount` answers 0 on failure and `sysconf` answers -1, so one signed `< 1` test
serves both and a future POSIX lane supplies only the read.

⚠ **THE CLAMP IS REACHABLE AND WAS MEASURED THERE.** Pointed at an invalid processor group,
`GetActiveProcessorCount` really does answer `0`, and the Std guard really does turn that into `1`:
with the group number sabotaged, `cpu-count-is-at-least-one` and `cpu-count-is-in-range` stayed green
(the clamp held) while `cpu-count-agrees-with-a-child-environment` went red; with the guard ALSO
removed, the first two went red as well. The arm is not decoration.

### The count is MACHINE-specific, so the cases assert PROPERTIES

There is no literal to compare against — the answer differs on every host — so the cases below
assert what cannot vary:

- it is at least 1, which is the clamp's own contract;
- it is at most 4096, the ceiling Windows can report at all (64 processor groups of 64), so a
  captured wrong register, a sign-extension or a stale high half shows up as an out-of-range number
  rather than as a plausible-looking count nobody would question;
- it is STABLE across calls in one process — the machine does not grow cores mid-run;
- and it AGREES WITH THE ENVIRONMENT a child process was started with.

⚠ **THE LAST ONE IS THE ONLY DISCRIMINATOR AGAINST A CONSTANT, AND IT IS WHY IT SPAWNS A CHILD.**
Bounds and stability are all satisfied by a lowering that returns a fixed `1` and never calls the OS
at all — the failure mode a green suite would otherwise hide. Windows populates
`NUMBER_OF_PROCESSORS` in a new process's environment block from the same fact the API reports, and
that is a second reading through a mechanism that shares no code with the intrinsic. It is asserted
as a DISCRIMINATION rather than an equality (*the child says "1" ⟺ we say 1*) so that it stays true
on a multi-group host, where the environment reports one group's count and the intrinsic reports the
machine's. `process-id.md` reaches for a child process for the identical reason and accepts the
identical dependency.

✅ **SABOTAGE-VERIFIED, and it is the only case that catches this one.** With `__cpu_count`'s body
replaced by a bare `return 1` that never calls the OS, `cpu-count-is-at-least-one`,
`cpu-count-is-in-range` and `cpu-count-is-stable-across-calls` all stayed GREEN and this case went
RED (exit 2 against the pinned 7) — measured, on the 12-CPU host. A suite without it would have
reported a compiler that had stopped asking the machine anything as fully passing.

⚠ On a genuinely single-processor host with `NUMBER_OF_PROCESSORS` unset the child prints the
literal `%NUMBER_OF_PROCESSORS%` and this case fails. Windows sets that variable for every process;
the case is written to be honest about what it leans on rather than to be immune to a Windows that
stops doing so.

## Tests

<!-- test: builtins-cpu-parallel.cpu-count-is-at-least-one -->
The clamp's own contract, and the property a missing lowering fails first: the call answers a number
at least 1 rather than whatever the result register happened to hold.
```maxon
function main() returns ExitCode
	let cpus = __Builtins.cpuCount()
	if cpus >= 1 'atLeastOne'
		return 3
	end 'atLeastOne'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: builtins-cpu-parallel.cpu-count-is-in-range -->
Windows can report at most 64 processor groups of 64 logical processors each, so `[1, 4096]` is the
whole space of answers the API has. A capture of the wrong register, a sign-extension of a `DWORD`
or a high half left over from an earlier call lands outside it; a real count cannot. ⚠ The BOUND is
Windows-derived and the CASE is not: `sysconf(_SC_NPROCESSORS_ONLN)` answers a small positive `long`
on Darwin and its failure answer is `-1`, which the clamp turns into 1 — so every honest answer on
that lane sits inside the same window, and the register-capture defects this case exists to catch
(a clobbered x0, an unpatched GOT slot, a high half left over) land outside it there too. The local
`windowsCeiling` names where the number came from, not which lane may run the case.
```maxon
function main() returns ExitCode
	let cpus = __Builtins.cpuCount()
	let windowsCeiling = 4096
	var score = 0
	if cpus >= 1 'atLeastOne'
		score = score + 1
	end 'atLeastOne'
	if cpus <= windowsCeiling 'belowTheCeiling'
		score = score + 2
	end 'belowTheCeiling'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-cpu-parallel.cpu-count-is-stable-across-calls -->
Three independent calls in one process agree. A machine does not grow cores mid-run, so this is what
a lowering that read a fresh counter, or that captured a register the call had clobbered, would
fail.
```maxon
function main() returns ExitCode
	let first = __Builtins.cpuCount()
	let second = __Builtins.cpuCount()
	let third = __Builtins.cpuCount()
	var score = 0
	if first == second 'firstPair'
		score = score + 1
	end 'firstPair'
	if second == third 'secondPair'
		score = score + 3
	end 'secondPair'
	return score as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: builtins-cpu-parallel.cpu-count-agrees-with-a-child-environment -->
**THE ONE DISCRIMINATOR AGAINST A CONSTANT.** Spawn `cmd /c echo %NUMBER_OF_PROCESSORS%` and read
the environment Windows gave the child — a second reading of the same fact through a mechanism that
shares no code with the intrinsic. `echo` appends `\r\n`, so a child that reports a single-digit `1`
prints exactly three bytes; anything else is a count above 9, a multi-digit count, or (on a
multi-group host) one group's share of a bigger machine. Either way the two readings must AGREE
about whether this machine has one processor, which a fixed `1` cannot do on any host that has more.
The child is waited on and both structs released, so the case is leak-clean.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function appendToken(out ByteArray, token String)
	let bytes = token.toByteArray()
	let n = bytes.count()
	for i in 0 upto n 'byteLoop'
		out.push(try bytes.get(i) otherwise panic("appendToken: get is in range"))
	end 'byteLoop'
	out.push(0)
end 'appendToken'

function main() returns ExitCode
	var argv = ByteArray.create()
	appendToken(argv, token: "cmd")
	appendToken(argv, token: "/c")
	appendToken(argv, token: "echo")
	appendToken(argv, token: "%NUMBER_OF_PROCESSORS%")
	let empty = ""
	let env = try __ManagedMemory.create(1, 1) otherwise panic("create(1, 1) cannot fail")
	let h = __Builtins.subprocessSpawn(argv, 4, empty.cstr(), env, 1, 0, empty.cstr(), 2, empty.cstr(), 0, 2, empty.cstr(), 0, 0)
	let r = __Builtins.subprocessWaitCollect(h, 0)
	let out = String.init(__Builtins.subprocessResultStdout(r))
	let one = "1"
	let oneLineBytes = 3
	let childSaysOne = out.byteLength() == oneLineBytes and out.startsWith(one)
	let cpus = __Builtins.cpuCount()
	__Builtins.subprocessResultRelease(r)
	__Builtins.subprocessReleaseHandle(h)
	var score = 0
	if childSaysOne == (cpus == 1) 'readingsAgreeAboutOne'
		score = score + 5
	end 'readingsAgreeAboutOne'
	if cpus >= 1 'clamped'
		score = score + 2
	end 'clamped'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-cpu-parallel.cpu-count-arity-checked -->
`cpuCount` takes no arguments. An intrinsic has no signature for the ordinary arity check to read,
so it is refused by the same `builtinArity` check `currentProcessId`/`commandLineCount` use. This
case is front-end only and target-neutral, so it carries no marker.
```maxon
function main() returns ExitCode
	return __Builtins.cpuCount(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.cpuCount' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-cpu-parallel.cpu-count-rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
WASI has no processor-count primitive at all, so this lane is a refusal of the permanent kind rather
than of the not-yet kind. The call is refused at its source span with `E3104`, naming the runtime
entry that has no lowering there — never a panic from inside the wasm backend, and never a
fabricated count. (The diagnostic's own wording still opens "x64-windows only"; that is the pinned
message text, not a claim this spec makes about which lanes lower the op.)
```maxon
function main() returns ExitCode
	let cpus = __Builtins.cpuCount()
	return cpus as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:24: this construct lowers to the runtime entry '__cpu_count', which has no wasm32-wasi implementation
```

<!-- test: builtins-cpu-parallel.cpu-count-rejected-on-wasm-when-unreached -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The gate is REACHABILITY-BLIND for user code: `probe` is never called, yet its intrinsic is still
refused. `SemanticCheck` visits every function and dead-function elimination runs two tiers later,
so this is the same rule that reports a type error in an unreached function — the half
`builtins-sleep.rejected-on-wasm-when-unreached` keeps for the sleep intrinsic.
```maxon
function probe() returns ExitCode
	return __Builtins.cpuCount() as ExitCode
end 'probe'

function main() returns ExitCode
	return 4
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__cpu_count', which has no wasm32-wasi implementation
```

<!-- test: builtins-cpu-parallel.sched-max-active-workers-is-one -->
A program that never spawns anything runs on one M — its own — whatever processor count the scheduler
resolved, so the high-water mark of concurrently-active worker Ms is 1 (MEASURED: `workers=1`). The
`>= 1` half is the contract's floor; the `== 1` half is this runtime's reading of it. ⚠ **THE CLAIM IS ABOUT THIS PROGRAM AND NOT ABOUT THE DEFAULT**, which used
to be one processor and is now the machine's: a worker M is started by WORK reaching
`__sched_wake_or_spawn`, and this program produces none — see the next case, which produces `async` work
and reads the same 1.

⭐ **AND THIS PROGRAM INSTALLS NO SCHEDULER AT ALL, WHICH IS WHAT MAKES IT DISCRIMINATING NOW THAT
THE ANSWER IS A `.data` LOAD.** Nothing here ever runs `__gt_init`, so nothing ever writes the
counter; a mark that were SEEDED BY AN INITIALIZER instead of by `.data` would read 0 here and this
case would go red. That ordering hazard is the reason the slot carries its 1 from the image.
```maxon
function main() returns ExitCode
	let workers = __Builtins.schedMaxActiveWorkers()
	var score = 0
	if workers >= 1 'contractFloor'
		score = score + 1
	end 'contractFloor'
	if workers == 1 'singleM'
		score = score + 2
	end 'singleM'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-cpu-parallel.sched-max-active-workers-is-one-under-async -->
**THE CASE THAT SAYS THE COUNTER IS MAINTAINED, AND READS 1 UNLESS THE MONITOR ACTED.** A program that
spawns two coroutines, runs them and awaits them observes one worker M, or the system monitor stepped in: a
coroutine is never published where a worker M could take it, and this program never yields, so the only roads
to a second machine are the monitor's three and each has a counter. The case therefore asserts `workers == 1`
OR one of them counted, and cannot go red because the OS descheduled the process long enough for a
preemption. What turns it red is a second worker M with all three at 0 — a coroutine that reached the
scheduler. The workers are read first: every counter is stepped before the machine it may start.
⚠ It is a DEFAULT-`MAXON_MAX_PROCS` reading, and that is now a CHOICE rather than a limitation. ⛔ This
sentence used to give the reason as *"a spec case cannot set that variable"*, which stopped being true when
the per-case processor marker landed — `specs/sched-default-procs.md` owns it, and three other files
already retracted this same claim. The case stays unpinned deliberately: its subject is that an `async`-only
program reaches no worker M the monitor did not start **at whatever count the machine happens to have**, so pinning one would narrow
it to a count nobody runs. The sweep over `{1, 2, 7, 12}` is still `multicore-stress/pin-matrix.sh`'s, because
comparing counts is what that instrument is for and a spec case runs at exactly one.

⛔ **THE MARKER IS NAMED HERE AND NOT SPELLED, AND THAT IS DELIBERATE.** Writing it out verbatim inside a
case's marker region makes the parser read the prose AS a marker — `parseProcsValue` panics on the `N`,
and the whole FILE stops parsing. (Measured: this paragraph did exactly that.) `sched-default-procs.md`
can spell it because its copy sits in the Documentation section, above `## Tests`, which nothing scans for
markers.
```maxon
function work(n ExitCode) returns ExitCode
	__Builtins.parallelBoundary()
	return n + 1
end 'work'

function main() returns ExitCode
	let first = async work(1)
	let second = async work(2)
	let a = await first
	let b = await second
	let workers = __Builtins.schedMaxActiveWorkers()
	let monitorActions = __Builtins.schedPreemptCount() + __Builtins.schedRetakeCount() + __Builtins.schedTimerStartCount()
	var score = a + b
	if workers == 1 or monitorActions > 0 'singleMUnlessTheMonitorActed'
		score = score + 2
	end 'singleMUnlessTheMonitorActed'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-cpu-parallel.sched-max-active-workers-arity-checked -->
`schedMaxActiveWorkers` takes no arguments, and earns the same `builtinArity` refusal its sibling
does. Front-end only and target-neutral, so it carries no marker.
```maxon
function main() returns ExitCode
	return __Builtins.schedMaxActiveWorkers(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.schedMaxActiveWorkers' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-cpu-parallel.sched-max-active-workers-runs-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
**THE OTHER HALF OF THE TARGET PAIR, AND IT IS AN ACCEPTANCE.** The scheduler query reaches no OS —
its whole body is one `.data` load and a `ret` — so it lowers on every target the compiler emits and is
refused nowhere. ⭐ It is also the one case that proves the counter's `.data` slot is laid out for a
program that installs NO scheduler: on a lane with no green threads at all, the load still has a slot
to read, because `schedRuntimeGlobals` gates the two worker counters on the QUERY as well as on
`usesGt`. Without this case, `cpu-count-rejected-on-wasm` would pass just as happily against a
compiler that refused the entire family on wasm; with it, the gate has to be the narrow one.
```maxon
function main() returns ExitCode
	let workers = __Builtins.schedMaxActiveWorkers()
	if workers == 1 'singleM'
		return 3
	end 'singleM'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: builtins-cpu-parallel.error.the-per-processor-counters-are-refused-off-their-substrate -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⛔⛔ **THESE THREE PANICKED THE COMPILER INSTEAD OF REFUSING, AND THE SUITE COULD NOT SEE IT BECAUSE NO CASE
CALLED ONE OF THEM OFF x64-windows.** MEASURED 2026-09-02, a four-line scalar program per builtin:
`wasm32-wasi` died at `SchedRuntime.tlsSlotArrayBase` (*"wasi has no per-OS-thread slot this scheduler"*) and
`x64-linux` at `StdToX64Conversion.lowerTlsSetValue`. **A panic is the worst answer a compiler can give**:
no span, no code, and it names a runtime emitter rather than the call the user wrote.

⇒ The three per-P counter sums now sit in `TargetFacilities.calleeHostFacility` under
`HostFacility.greenThreads`, exactly as `__gt_await_any` and a service's two ops already do, so the refusal
lands on the call's own span. ⚠ **They are named individually and NOT by prefix** — one of the three is a
`__slab_` entry, and `schedMaxActiveWorkers`/`schedProcessorCount` set no `usesGt` and **must keep working
here**, which the sibling case `sched-max-active-workers-runs-on-wasm` in this file is what proves.

⚠ **ONE PROGRAM NAMES EVERY MEMBER ON PURPOSE.** The band is a roster in the compiler
(`SchedRuntime.isSchedSubstrateQueryCallee`), so the thing worth pinning is that every scheduler-state query
is on it; a per-builtin case would pass while a sibling was quietly dropped from the list. A query added to
the roster is added here too.

⛔ **A SECOND DEFECT RODE WITH THE FIRST, AND IT REACHED x64-windows.** All three set `usage.usesGt` BY HAND
instead of calling `recordGtUsage`, so `usesHeap` stayed off and the scheduler linked against a
`__slab_alloc` that dead-function elimination had pruned — `resolveCallFixups: call to unknown function`, on
**every** lane. It hid because the obvious probe prints its answer, and `print` turns `usesHeap` on by
itself; only a program that discards the value shows it. That is why the program below **returns** the
counter rather than printing it.
⚠ **THE NATIVE TWIN THIS CASE ONCE HAD IS RETIRED, AND IT WAS PINNING SOMETHING THIS CASE DOES NOT.**
`…refused-on-a-native-target` existed because both cases reach `E3104` through the same arm of
`TargetFacilities.calleeHostFacility` but replaced panics in DIFFERENT backend code — wasm died in
`SchedRuntime.tlsSlotArrayBase`, x64-Linux in `StdToX64Conversion.lowerTlsSetValue`, the same x64 backend
that serves the lane where these builtins work. Now that x64-Linux provides the facility,
`lowerTlsSetValue` has a real arm and no native lane refuses these counters, so the twin has nothing left
to witness. ⚠ **What it covered is therefore NOT covered here**: this case proves the facility arm fires,
not that the x64 backend stopped panicking — the suite's 7079 passing x64-linux cases are what prove that
now. `async-sleep.md`'s surviving wasm case states the convention this follows.

```maxon
function main() returns ExitCode
	let c0 = __Builtins.schedStealCount()
	let c1 = __Builtins.schedRetakeCount()
	let c2 = __Builtins.slabRemoteFreeCount()
	let c3 = __Builtins.schedGtRecycleCount()
	let c4 = __Builtins.schedPreemptCount()
	let c5 = __Builtins.schedSliceCount()
	let c6 = __Builtins.schedGlobalPushCount()
	let c7 = __Builtins.schedTimerStartCount()
	let c8 = __Builtins.schedIdleProcessorCount()
	let c9 = __Builtins.schedGtRecordsCarved()
	let c10 = __Builtins.schedParkWakeCount()
	let c11 = __Builtins.gtStackBytes()

	return (c0 + c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9 + c10 + c11) as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:22: this construct lowers to the runtime entry '__sched_steal_count', which has no wasm32-wasi implementation
error E3104: <fragment>:4:22: this construct lowers to the runtime entry '__sched_retake_count', which has no wasm32-wasi implementation
error E3104: <fragment>:5:22: this construct lowers to the runtime entry '__slab_remote_free_count', which has no wasm32-wasi implementation
error E3104: <fragment>:6:22: this construct lowers to the runtime entry '__sched_gt_recycle_count', which has no wasm32-wasi implementation
error E3104: <fragment>:7:22: this construct lowers to the runtime entry '__sched_preempt_count', which has no wasm32-wasi implementation
error E3104: <fragment>:8:22: this construct lowers to the runtime entry '__sched_slice_count', which has no wasm32-wasi implementation
error E3104: <fragment>:9:22: this construct lowers to the runtime entry '__sched_global_push_count', which has no wasm32-wasi implementation
error E3104: <fragment>:10:22: this construct lowers to the runtime entry '__sched_timer_start_count', which has no wasm32-wasi implementation
error E3104: <fragment>:11:22: this construct lowers to the runtime entry '__sched_idle_processor_count', which has no wasm32-wasi implementation
error E3104: <fragment>:12:22: this construct lowers to the runtime entry '__sched_gt_records_carved', which has no wasm32-wasi implementation
error E3104: <fragment>:13:23: this construct lowers to the runtime entry '__sched_park_wake_count', which has no wasm32-wasi implementation
error E3104: <fragment>:14:23: this construct lowers to the runtime entry '__sched_gt_stack_bytes', which has no wasm32-wasi implementation
```
