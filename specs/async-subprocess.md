---
feature: async-subprocess
status: stable
keywords: [subprocess, process, spawn, async, await, runProcess, green-threads, scheduler, netpoll, yield, concurrency, throws, try]
category: concurrency
---

# Subprocess — spawn a child and yield while it runs (P1.5)

## Documentation

`__Builtins.runProcess(cmd)` spawns a Windows child process for the command line `cmd`, suspends the **current green
thread** while the child runs, and returns the child's integer exit code once it finishes. It is a **yielding**
wait: the thread parks on the child, hands control back to the scheduler, and RESUMES with its exit code once the
child has exited — so other green threads run while a child is pending.

⭐ **IT IS SPELLED `__Builtins.runProcess`, AND THE BARE NAME `runProcess` IS AN ORDINARY NAME.** It used to be a
bare-name builtin, recognized before any registry was consulted, so a program declaring its own `runProcess`
found that declaration silently unreachable and its calls checked against the builtin's arity — which is
exactly what `maxon-bin/Testing/SpecTestRunner.maxon`'s own four-parameter `runProcess` hit (`E3036`, G17
defect 1). The entry moved into the reserved `__Builtins.` space, which `E2051` bars every declaration from,
so no user name can contest it again. `the-bare-name-is-an-ordinary-declaration` below is the door that
opened.

⚠ **UNLIKE `sleep`, IT DID NOT MOVE INTO STDLIB SOURCE, AND THAT IS WHY ITS TARGET ANSWER DID NOT MOVE
EITHER.** `sleep`'s retirement gave the entry to `stdlib/Sleep.maxon`, where the target gate is
reachability-AWARE, so an unreached `sleep` began compiling for wasm (`async-sleep.unreached-compiles-on-wasm`).
There is no stdlib declaration to give this one to: `stdlib/Subprocess.maxon`'s `Subprocess.run` is a different
mechanism (a poll-and-`__gt_sleep` drain over `__Builtins.subprocess*`), and reconciling the two is the
deferred full-Subprocess-API rung. `__Builtins.runProcess` therefore still emits `__gt_process_run` in USER
code, where the gate is reachability-BLIND — pinned by `rejected-on-wasm-when-unreached` below.

`__Builtins.runProcess` is a **throwing** builtin (P1.5 #93): a spawn failure THROWS rather than aborting, so it
must be called under `try`, exactly as a throwing array accessor is. It rides the same dual-register error ABI
(`errorReturn`) an ordinary throwing call uses — the exit code in R8, the error flag in R10 — so
`try __Builtins.runProcess(cmd) otherwise <handler>` catches the failure that used to abort the process, and a
program can recover from it instead of dying. Recovery today is by VALUE — any error routes to the `otherwise`
handler; binding `otherwise (e)` to a specific case is a deferred P1.7 feature, as for `ArrayError`.

```text
function runChild() returns int
	return try __Builtins.runProcess("cmd /c exit 3") otherwise 99
end 'runChild'

function main() returns ExitCode
	let p = async runChild()
	let code = await p
	return code as ExitCode
end 'main'
```

When no machine has anything to run and a child is parked, the idle machine blocks in the POLLER, and the
child's exit arrives there as an event of its own — never a poll period and never a busy-spin. A parked child is
a SOURCE on the poller exactly as a socket is: a `pidfd` on Linux, a kqueue carrying the child's exit on macOS,
a registered wait posting a packet on Windows. The same wait ends at every due timer, so a thread that is merely
sleeping still wakes on time while another thread's child is still running.

`__Builtins.runProcess` works from `main` and from an `async` coroutine alike. Its argument is a
`String` command line — borrowed, not consumed; a `float`/`int`/`bool` is refused at compile time. Its result is
an integer (the exit code), so — unlike `sleep` — it may be used in value position (under `try`).

If the command names no runnable executable (`CreateProcessA` fails outright), it throws its
**spawn-failure** error rather than parking on a non-existent child — a deterministic error the caller
catches, never a hang. How many children may be parked at once is the poller's property and carries no
ceiling of its own: `sixty-five-concurrent-children-all-complete` holds sixty-five waits open together, one
past what a single `WaitForMultipleObjects` array could ever have held.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** A parked child is a source on the scheduler's poller, so these cases need the substrate.
⚠ The two `error.` cases are front-end refusals (`E3005`, `E3057`), are target-neutral, and carry NO
marker.

### ⭐ arm64-macOS RUNS `__Builtins.runProcess`, AND ITS WAIT REALLY DOES YIELD THERE

`TargetFacilities` answers `subprocess gives true` for arm64-macOS, so this builtin compiles and runs
on that lane. The cases above cannot be widened — every one of them spawns `cmd /c …` — so the
subjects this lane can express carry `posix-…` siblings marked `arm64-macos`, following
`process-background-priority.md`'s pattern. The command LINE reaches `/bin/sh -c` there, which is
what "run this command line" means under POSIX and the same interpreter `system()` names, so
`"exit 3"` is the whole program where the Windows sibling writes `cmd /c exit 3`.

⛔ **ONE SUBJECT HAS NO SIBLING, FOR A REASON IN THE PLATFORM RATHER THAN IN THE PORT.**
`spawn-failure-caught` and `spawn-failure-recover-continue` turn on `CreateProcessA` REFUSING a
command that names no runnable executable; under the shell shape the spawn of `/bin/sh` always
succeeds and a missing command is the SHELL's exit **127**, so no command line on this lane can
reach the spawn-failure throw those cases exist to catch (measured: a bad name answers 127, not the
`otherwise` handler). That subject would need a different program asserting a different fact, which
is a rung and not a marker.

## Tests

<!-- test: async-subprocess.exit-code -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
A spawned green thread runs a child that exits 3; the thread parks on the child (yielding), resumes once the child
exits, and returns the exit code, which becomes the program's exit code. The spawn succeeds, so the
`otherwise` fallback is never taken.
```maxon
function runChild() returns Integer
	return try __Builtins.runProcess("cmd /c exit 3") otherwise 99
end 'runChild'

function main() returns ExitCode
	let p = async runChild()
	let code = await p
	return code as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: async-subprocess.posix-exit-code -->
<!-- unsupported-targets: x64-windows -->
`exit-code`'s subject on the POSIX lane, and the case that proves `__Builtins.runProcess` runs a child here
at all: a spawned green thread runs a child that exits 3, parks on it, resumes once it exits, and returns
the exit code, which becomes the program's exit code. The spawn succeeds, so the `otherwise` fallback is
never taken.

⚠ **THE COMMAND LINE GOES THROUGH `/bin/sh -c`, AND THAT IS THE CORRECT READING OF THIS SURFACE RATHER THAN
A SHORTCUT.** `__Builtins.runProcess` takes a whole command LINE a user wrote, and on a lane whose spawn
primitive takes a vector, "run this command line" MEANS handing it to the interpreter `system()` names —
which is why `"exit 3"` is a complete program here where the Windows sibling writes `cmd /c exit 3`. The
ARGV-taking intrinsics in `subprocess-builtins.md` reach no shell at all, and
`posix-argv-reaches-the-child-verbatim` there is what holds that half apart from this one.
```maxon
function runChild() returns Integer
	return try __Builtins.runProcess("exit 3") otherwise 99
end 'runChild'

function main() returns ExitCode
	let p = async runChild()
	let code = await p
	return code as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
3
```

<!-- test: async-subprocess.sequence -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Two children run in sequence (spawn, await, spawn, await) with distinct exit codes; each thread's exit code is
read back independently and combined, proving no cross-talk between the two parked-then-resumed threads.
```maxon
function childA() returns Integer
	return try __Builtins.runProcess("cmd /c exit 4") otherwise 99
end 'childA'

function childB() returns Integer
	return try __Builtins.runProcess("cmd /c exit 5") otherwise 99
end 'childB'

function main() returns ExitCode
	let pa = async childA()
	let a = await pa
	let pb = async childB()
	let b = await pb
	return (a * 10 + b) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
45
```

<!-- test: async-subprocess.multi-concurrent -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Three children are spawned BEFORE any await, so all three park on their processes SIMULTANEOUSLY — the poller
holds THREE child sources at once and its readiness walk wakes each waiter separately (the path `sequence`,
`spawn-loop` and `interleave` never reach, each parking ≤1 child at a time). Each exit code is read back into
its own digit, so `123` proves all three resumed independently with the right handle-to-thread mapping and no
cross-talk.
```maxon
function c1() returns Integer
	return try __Builtins.runProcess("cmd /c exit 1") otherwise 99
end 'c1'

function c2() returns Integer
	return try __Builtins.runProcess("cmd /c exit 2") otherwise 99
end 'c2'

function c3() returns Integer
	return try __Builtins.runProcess("cmd /c exit 3") otherwise 99
end 'c3'

function main() returns ExitCode
	let p1 = async c1()
	let p2 = async c2()
	let p3 = async c3()
	let r1 = await p1
	let r2 = await p2
	let r3 = await p3
	return (r1 * 100 + r2 * 10 + r3) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
123
```

<!-- test: async-subprocess.posix-a-child-that-has-already-exited -->
<!-- unsupported-targets: x64-windows -->
⭐⭐ **A CHILD CAN FINISH BEFORE ITS PARENT FINISHES ARMING, AND THAT IS AN ANSWER RATHER THAN A REFUSAL.**
Between the spawn and the registration the kernel may run the child to completion, and a source for a task
that is gone cannot be opened: `pidfd_open` answers `ESRCH` for a reaped pid and an `EVFILT_PROC`
registration answers it for a task that has exited. The exit STATUS is still there to read, so the wait is
already over and the runtime takes the status — it does not abort.

⛔⛔ **IT IS A RACE, WHICH IS WHY THE LOOP IS FIFTY AND WHY NOTHING PINNED IT BEFORE.** `exit 7` through
`/bin/sh` is about as short-lived as a child gets, so each lap is a fresh chance to lose it; a machine that
loses it once fails the whole case. MEASURED: as `RuntimeAbort.netpollFailed` (103) it killed ten spec
workers on a 3-vCPU `macos-15` runner while every developer machine won the race every time and stayed
green — a shape no existing case could see, because every other subprocess case spawns a child that outlives
its own registration.

⚠ **THE COUNT IS THE ASSERTION, NOT THE EXIT CODE.** Each lap that answers 7 steps `done`, so `50` says
every child was spawned, waited for and reaped with the right status; an abort answers 103 and a lap that
silently lost its child answers less.
```maxon
function once() returns Integer
	return try __Builtins.runProcess("exit 7") otherwise 99
end 'once'

function main() returns ExitCode
	var done = 0

	for _ in 0 upto 50 'spawns'
		if once() == 7 'reaped'
			done = done + 1
		end 'reaped'
	end 'spawns'

	return done as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
50
```

<!-- test: async-subprocess.posix-an-already-exited-child-gets-no-record -->
<!-- unsupported-targets: x64-windows -->
⛔⛔ **`-ESRCH` IS AN ANSWER, NOT A DESCRIPTOR, AND IT MUST NEVER REACH THE RECORD TABLE.** Its neighbour
above pins that an already-exited child is reaped rather than refused; this one pins the road the refusal
travels. `__np_child_open` answers the negative errno, and the block that took it fell through into
`__np_pd_adopt` — so **-3 was used as the table index**. The bound is a signed compare, so `-3 < capacity`
reads as *inside the table*, and `table + 8*(-3)` is the word 24 bytes BEFORE it: either a wild value
readied as a green thread, or a fresh record's address written over eight bytes belonging to something
else. The open now RETURNS on that edge; callers see `-ESRCH` and take their own road exactly as before.

⚠ **ONE LAP, AND THE ONE IS THE WHOLE POINT.** `posix-a-child-that-has-already-exited` runs fifty and
CANNOT catch this — MEASURED at 30,000 spawns with 0 failures against 16 in 20,000 here. From the second
lap the record table exists, so `table - 24` lands in live heap and the damage is silent; on the FIRST
`runProcess` of a process the table is unborn, the load is from a fixed wild address, and losing the race
is a hard SIGSEGV naming `__np_pd_adopt`. So `main` spawns exactly once and does nothing before it.

⚠ **IT IS PROBABILISTIC IN THE SAME WAY ITS NEIGHBOUR IS, AND LESS LIKELY TO FIRE.** 16 of 20,000 under
192-way concurrency on a loaded 3-vCPU machine is the measured rate; on an idle developer box it is far
rarer. What makes it worth keeping is that its failure is a crash naming the function, not a wrong number.
```maxon
function once() returns Integer
	return try __Builtins.runProcess("exit 7") otherwise 99
end 'once'

function main() returns ExitCode
	return once() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
```RequiredRuntime
__np_child_open
```

<!-- test: async-subprocess.posix-multi-concurrent -->
<!-- unsupported-targets: x64-windows -->
⭐ **THE CONCURRENCY CASE — SEVERAL CHILDREN THROUGH THE NETPOLL AT ONCE.** Three children are spawned
BEFORE any await, so all three park on their processes SIMULTANEOUSLY and the poller holds three child
sources at once — the path `posix-exit-code` and `posix-interleave-with-sleep` never reach, each parking one
child at a time. Each exit code is read back into its own digit, so `123` proves all three resumed
independently with the right child-to-thread mapping and no cross-talk; two children swapped, or one thread
resumed with another's status, gives a different three-digit number rather than a near miss.

⚠ On this lane each child's exit reaches the poller through a source the kernel mints for it — a `pidfd` on
Linux, a kqueue carrying `NOTE_EXIT` on macOS — where the Windows lane's arrives as a completion packet. How
many may be parked together is the poller's property on either, and
`posix-sixty-five-concurrent-children-all-complete` is where that is asserted rather than here.
```maxon
function c1() returns Integer
	return try __Builtins.runProcess("exit 1") otherwise 99
end 'c1'

function c2() returns Integer
	return try __Builtins.runProcess("exit 2") otherwise 99
end 'c2'

function c3() returns Integer
	return try __Builtins.runProcess("exit 3") otherwise 99
end 'c3'

function main() returns ExitCode
	let p1 = async c1()
	let p2 = async c2()
	let p3 = async c3()
	let r1 = await p1
	let r2 = await p2
	let r3 = await p3
	return (r1 * 100 + r2 * 10 + r3) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
123
```

<!-- test: async-subprocess.interleave-with-sleep -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
A slow child (a ~1 s `ping` delay) and a short (50 ms) sleeper run concurrently. The sleeper's timer fires WHILE
the child is still running, so the sleeper resumes FIRST — proving the process wait YIELDS (it is bounded by the
earliest timer, not a blocking wait on the child). Each records its completion order into a global
(`order = order * 10 + tag`), so `21` proves the sleeper (tag 2) completed before the child thread (tag 1). A wait
that blocked the single thread on the child would instead produce `12`.
```maxon
var order = 0

function slow() returns Integer
	_ = try __Builtins.runProcess("cmd /c ping -n 2 127.0.0.1 >nul") otherwise 99
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns Integer
	sleep(50)
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

<!-- test: async-subprocess.posix-interleave-with-sleep -->
<!-- unsupported-targets: x64-windows -->
⭐ **THE CASE THAT PROVES THE PROCESS WAIT YIELDS ON THIS LANE.** A slow child (a one-second `sleep`) and a
short 50 ms sleeper run concurrently. The sleeper's timer fires WHILE the child is still running, so the
sleeper resumes FIRST — the wait is bounded by the earliest timer rather than blocking the single M on the
child. Each records its completion order into a global (`order = order * 10 + tag`), so `21` proves the
sleeper (tag 2) completed before the child thread (tag 1); a wait that blocked would produce `12`.

⭐ It is worth having here rather than being taken on trust from Windows, because the very same measurement
on the STREAMING reader answers `12` on this lane: the process wait parks and a pipe read does not
(`streaming-subprocess.posix-echo-read` records that split, and `TargetFacilities`'s MAC8 row is where it
comes from). So on arm64-macOS this case and its streaming counterpart genuinely disagree, and only a
measurement can say which way each falls.
```maxon
var order = 0

function slow() returns Integer
	_ = try __Builtins.runProcess("sleep 1") otherwise 99
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns Integer
	sleep(50)
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

<!-- test: async-subprocess.a-parked-child-costs-no-poll -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
⭐ **A SECOND OF PARKED CHILD COSTS THE POLLER ONE WAIT, NOT A THOUSAND.** A child is a SOURCE on the
network poller, exactly as a socket is: the green thread awaiting it is suspended there, and the poller's
wait ends when the child's exit lands rather than at a poll period of the poller's own. Two facts say it.
`quiet=` bounds how many times a parked machine's wait returned across the second the child runs — a
poller that caps its wait so it can re-poll every parked child returns about a thousand times in that
second, so the bound is a small constant and anything near it is the cap still there. `onpoller=` says the
park was on the POLLER at all: a child parked on a store nothing blocks on steps that counter zero times,
however promptly it is reaped.

⚠ **NEITHER FACT STANDS ALONE.** A quiet second is satisfied by a machine that simply blocked on the
child and polled nothing; a poller park is satisfied by a poller that parks and then wakes on a period
anyway. Together they say the wait is ON the poller AND is not bounded by a period of its own.

⚠ **`quietWakes` IS THE BOUND THIS CASE ASSERTS, NOT A NUMBER READ OFF A CONTROL.** It is chosen the way
`netpoll-idle.an-idle-sleep-wakes-no-machine` chooses its own: small enough that a per-millisecond poll
cannot fit under it by two orders of magnitude, loose enough to cover the handful of wakes the spawn, the
exit and the await themselves cost.
```maxon
// The whole second the child runs. A poller that blocks until the child's exit lands returns a handful of
// times; one that caps its wait to re-poll every parked child returns about a thousand.
let quietWakes = 8

// A child wait is a park on the poller — at least the one this program makes.
let pollerParks = 1

function slow() returns Integer
	return try __Builtins.runProcess("cmd /c ping -n 2 127.0.0.1 >nul") otherwise 99
end 'slow'

function main() returns ExitCode
	sleep(1)
	let beforeWakes = __Builtins.schedParkWakeCount()
	let beforeParks = __Builtins.schedNetpollBlockCount()
	let p = async slow()
	_ = await p
	let wakes = __Builtins.schedParkWakeCount() - beforeWakes
	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	print("quiet={wakes <= quietWakes} onpoller={parks >= pollerParks}\n")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
quiet=true onpoller=true
```
```exitcode
0
```

<!-- test: async-subprocess.posix-a-parked-child-costs-no-poll -->
<!-- unsupported-targets: x64-windows -->
<!-- procs: 1 -->
`a-parked-child-costs-no-poll`'s subject on the POSIX lane, where the child joins the poller as a source of
its own rather than as a handle in a wait array. The same two facts: the second the child runs costs the
poller a handful of wait returns rather than one per millisecond, and the awaiting green thread is counted
as suspended ON the poller.

⚠ **THE EXIT CODE IS FIXED AT 0 ON PURPOSE.** What a child answers is `posix-exit-code`'s subject; this
case is about where its waiter sat, so the two printed facts carry the whole claim and the exit code
carries none of it.
```maxon
// The whole second the child runs. A poller that blocks until the child's exit lands returns a handful of
// times; one that caps its wait to re-poll every parked child returns about a thousand.
let quietWakes = 8

// A child wait is a park on the poller — at least the one this program makes.
let pollerParks = 1

function slow() returns Integer
	return try __Builtins.runProcess("sleep 1") otherwise 99
end 'slow'

function main() returns ExitCode
	sleep(1)
	let beforeWakes = __Builtins.schedParkWakeCount()
	let beforeParks = __Builtins.schedNetpollBlockCount()
	let p = async slow()
	_ = await p
	let wakes = __Builtins.schedParkWakeCount() - beforeWakes
	let parks = __Builtins.schedNetpollBlockCount() - beforeParks
	print("quiet={wakes <= quietWakes} onpoller={parks >= pollerParks}\n")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
quiet=true onpoller=true
```
```exitcode
0
```

<!-- test: async-subprocess.spawn-loop -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Robustness: twenty-five spawned threads each run a child that exits 1, awaited in turn. Each parks on its child
(waiting, NOT completed — its stack must NOT be recycled while parked) then resumes and completes (stack recycled
onto the free-list). The sum proves all twenty-five ran to completion with no crash, no use-after-free, and no leak.
```maxon
function child() returns Integer
	return try __Builtins.runProcess("cmd /c exit 1") otherwise 99
end 'child'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 25 'l'
		let p = async child()
		let r = await p
		sum = sum + r
		i = i + 1
	end 'l'
	return sum as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
25
```

<!-- test: async-subprocess.scratch-reuse-loop -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Fifty children each exit 1, awaited in turn, summing to 50. The value is that `__gt_process_run`'s OS scratch —
STARTUPINFOA, PROCESS_INFORMATION, the mutable cmdline copy and the exit-code slot — is REUSED across all fifty
calls (P1.5-B1c #92): the three fixed buffers are one-time `__gt_init` allocations and the cmdline copy is a
grow-on-demand global that allocates once for a constant command length, so the loop stays bounded rather than
bump-leaking ~150 bytes per call. Reuse is only correct because PROCESS_INFORMATION's `hProcess` is re-zeroed
before each spawn (the failure sentinel) and the exit-code slot is re-zeroed before each `GetExitCodeProcess`
(a clean i64 read); the `__gt_live_count` gate stays clean, so a clean exit proves the reuse leaked nothing.
```maxon
function child() returns Integer
	return try __Builtins.runProcess("cmd /c exit 1") otherwise 99
end 'child'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 50 'l'
		let p = async child()
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

<!-- test: async-subprocess.spawn-failure-caught -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
A command that names no runnable executable makes `CreateProcessA` fail outright, leaving a null child handle. The
runtime now THROWS its spawn-failure error (P1.5 #93) rather than aborting the process — so the direct
`try __Builtins.runProcess(bad) otherwise 42` in `main` catches it and returns the fallback 42. Before #93 this aborted with exit
1; now the program runs to a normal return, proving the spawn failure is recoverable, not fatal.
```maxon
function main() returns ExitCode
	let code = try __Builtins.runProcess("nonexistentprogram_xyz_12345") otherwise 42
	return code as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-subprocess.spawn-failure-recover-continue -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
Recovery leaves the scheduler CONSISTENT: after a caught `spawnFailed` (a command that cannot start), a SECOND
A VALID command still spawns, parks and returns its exit code. `bad` catches the spawn failure and
falls back to 0; `good` runs `cmd /c exit 9` normally. `0 + 9 = 9` proves the caught error did not leave the process
store, the netpoller or the current GT in a broken state.
```maxon
function main() returns ExitCode
	let bad = try __Builtins.runProcess("nonexistentprogram_xyz_67890") otherwise 0
	let good = try __Builtins.runProcess("cmd /c exit 9") otherwise 0
	return (bad + good) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: async-subprocess.sixty-five-concurrent-children-all-complete -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⭐ **SIXTY-FIVE CHILDREN PARKED AT ONCE, AND NOTHING IS FULL.** How many children a program may wait on
concurrently is a property of the POLLER, which carries as many registered sources as the process has
handles — not of a fixed-size store with a ceiling of its own. Sixty-five is one past the sixty-four a
`WaitForMultipleObjects` array can hold, so a store reappearing anywhere on this path shows up as the
sixty-fifth child failing rather than as a slower run. Each child is started as a live promise before any
await (a discarded promise is drop-cancelled, so each needs its own binding) and every one that exits 0
steps `done`, so `65` is the only answer all sixty-five completing produces — one child lost gives a
different number rather than a near miss.

⚠ **EVERY CHILD MUST OUTLIVE THE BURST OF SIXTY-FIVE SPAWNS, AND THAT IS WHY IT SLEEPS.** A spawn is a
kernel call, and while one runs the scheduler may hand the processor to another machine, whose idle poll
reaps every child that has already exited. A child that exited at once would be gone before the
sixty-fifth parks: the program would never hold sixty-five waits open together, would pass without
exercising the property at all, and would say nothing about the ceiling it exists to deny. A two-second
child is still running when the last spawn returns, however the spawns interleave with that machine.
```maxon
var done = 0

function child() returns Integer
	let code = try __Builtins.runProcess("cmd /c ping -n 3 127.0.0.1 >nul") otherwise 99

	if code == 0 'completed'
		done = done + 1
	end 'completed'

	return code
end 'child'

function main() returns ExitCode
	let p00 = async child()
	let p01 = async child()
	let p02 = async child()
	let p03 = async child()
	let p04 = async child()
	let p05 = async child()
	let p06 = async child()
	let p07 = async child()
	let p08 = async child()
	let p09 = async child()
	let p10 = async child()
	let p11 = async child()
	let p12 = async child()
	let p13 = async child()
	let p14 = async child()
	let p15 = async child()
	let p16 = async child()
	let p17 = async child()
	let p18 = async child()
	let p19 = async child()
	let p20 = async child()
	let p21 = async child()
	let p22 = async child()
	let p23 = async child()
	let p24 = async child()
	let p25 = async child()
	let p26 = async child()
	let p27 = async child()
	let p28 = async child()
	let p29 = async child()
	let p30 = async child()
	let p31 = async child()
	let p32 = async child()
	let p33 = async child()
	let p34 = async child()
	let p35 = async child()
	let p36 = async child()
	let p37 = async child()
	let p38 = async child()
	let p39 = async child()
	let p40 = async child()
	let p41 = async child()
	let p42 = async child()
	let p43 = async child()
	let p44 = async child()
	let p45 = async child()
	let p46 = async child()
	let p47 = async child()
	let p48 = async child()
	let p49 = async child()
	let p50 = async child()
	let p51 = async child()
	let p52 = async child()
	let p53 = async child()
	let p54 = async child()
	let p55 = async child()
	let p56 = async child()
	let p57 = async child()
	let p58 = async child()
	let p59 = async child()
	let p60 = async child()
	let p61 = async child()
	let p62 = async child()
	let p63 = async child()
	let p64 = async child()
	_ = await p00
	_ = await p01
	_ = await p02
	_ = await p03
	_ = await p04
	_ = await p05
	_ = await p06
	_ = await p07
	_ = await p08
	_ = await p09
	_ = await p10
	_ = await p11
	_ = await p12
	_ = await p13
	_ = await p14
	_ = await p15
	_ = await p16
	_ = await p17
	_ = await p18
	_ = await p19
	_ = await p20
	_ = await p21
	_ = await p22
	_ = await p23
	_ = await p24
	_ = await p25
	_ = await p26
	_ = await p27
	_ = await p28
	_ = await p29
	_ = await p30
	_ = await p31
	_ = await p32
	_ = await p33
	_ = await p34
	_ = await p35
	_ = await p36
	_ = await p37
	_ = await p38
	_ = await p39
	_ = await p40
	_ = await p41
	_ = await p42
	_ = await p43
	_ = await p44
	_ = await p45
	_ = await p46
	_ = await p47
	_ = await p48
	_ = await p49
	_ = await p50
	_ = await p51
	_ = await p52
	_ = await p53
	_ = await p54
	_ = await p55
	_ = await p56
	_ = await p57
	_ = await p58
	_ = await p59
	_ = await p60
	_ = await p61
	_ = await p62
	_ = await p63
	_ = await p64
	return done as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
65
```

<!-- test: async-subprocess.posix-sixty-five-concurrent-children-all-complete -->
<!-- unsupported-targets: x64-windows -->
`sixty-five-concurrent-children-all-complete`'s subject on the POSIX lanes: sixty-five children parked
together, each a source on the poller rather than a slot in a store, and `65` only if every one of them
completed. `sleep 2` outlives the burst of spawns for the same reason as the Windows sibling's two-second
child — a child that exited at once would be reaped by another machine's idle poll before the sixty-fifth
parked, and the program would never hold sixty-five waits open at the same time.
```maxon
var done = 0

function child() returns Integer
	let code = try __Builtins.runProcess("sleep 2") otherwise 99

	if code == 0 'completed'
		done = done + 1
	end 'completed'

	return code
end 'child'

function main() returns ExitCode
	let p00 = async child()
	let p01 = async child()
	let p02 = async child()
	let p03 = async child()
	let p04 = async child()
	let p05 = async child()
	let p06 = async child()
	let p07 = async child()
	let p08 = async child()
	let p09 = async child()
	let p10 = async child()
	let p11 = async child()
	let p12 = async child()
	let p13 = async child()
	let p14 = async child()
	let p15 = async child()
	let p16 = async child()
	let p17 = async child()
	let p18 = async child()
	let p19 = async child()
	let p20 = async child()
	let p21 = async child()
	let p22 = async child()
	let p23 = async child()
	let p24 = async child()
	let p25 = async child()
	let p26 = async child()
	let p27 = async child()
	let p28 = async child()
	let p29 = async child()
	let p30 = async child()
	let p31 = async child()
	let p32 = async child()
	let p33 = async child()
	let p34 = async child()
	let p35 = async child()
	let p36 = async child()
	let p37 = async child()
	let p38 = async child()
	let p39 = async child()
	let p40 = async child()
	let p41 = async child()
	let p42 = async child()
	let p43 = async child()
	let p44 = async child()
	let p45 = async child()
	let p46 = async child()
	let p47 = async child()
	let p48 = async child()
	let p49 = async child()
	let p50 = async child()
	let p51 = async child()
	let p52 = async child()
	let p53 = async child()
	let p54 = async child()
	let p55 = async child()
	let p56 = async child()
	let p57 = async child()
	let p58 = async child()
	let p59 = async child()
	let p60 = async child()
	let p61 = async child()
	let p62 = async child()
	let p63 = async child()
	let p64 = async child()
	_ = await p00
	_ = await p01
	_ = await p02
	_ = await p03
	_ = await p04
	_ = await p05
	_ = await p06
	_ = await p07
	_ = await p08
	_ = await p09
	_ = await p10
	_ = await p11
	_ = await p12
	_ = await p13
	_ = await p14
	_ = await p15
	_ = await p16
	_ = await p17
	_ = await p18
	_ = await p19
	_ = await p20
	_ = await p21
	_ = await p22
	_ = await p23
	_ = await p24
	_ = await p25
	_ = await p26
	_ = await p27
	_ = await p28
	_ = await p29
	_ = await p30
	_ = await p31
	_ = await p32
	_ = await p33
	_ = await p34
	_ = await p35
	_ = await p36
	_ = await p37
	_ = await p38
	_ = await p39
	_ = await p40
	_ = await p41
	_ = await p42
	_ = await p43
	_ = await p44
	_ = await p45
	_ = await p46
	_ = await p47
	_ = await p48
	_ = await p49
	_ = await p50
	_ = await p51
	_ = await p52
	_ = await p53
	_ = await p54
	_ = await p55
	_ = await p56
	_ = await p57
	_ = await p58
	_ = await p59
	_ = await p60
	_ = await p61
	_ = await p62
	_ = await p63
	_ = await p64
	return done as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
65
```

<!-- test: async-subprocess.error.non-string-arg-rejected -->
`__Builtins.runProcess` requires a `String` command line; a non-String argument is refused at compile time.
```maxon
function main() returns ExitCode
	__Builtins.runProcess(42)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: '__Builtins.runProcess' requires a String, but its argument is int
```

<!-- test: async-subprocess.error.bare-call-requires-try -->
<!-- unsupported-targets: wasm32-wasi -->
`__Builtins.runProcess` is a throwing builtin (P1.5 #93), so a bare call that drops its error flag is refused
(E3057) — the exact mirror of the throwing-array-accessor rule. A bare call would read only the exit code (R8)
and silently drop the spawn-failure flag (R10), so the compiler forces a `try`.

⚠ **THE RULE IS TARGET-NEUTRAL AND THE CASE IS NOT, WHICH IS A CONSEQUENCE OF THE SUBSTRATE GATE.** Since
this entry joined `SemanticCheck.calleeNeedsWin32Substrate`, this program is refused on every other target
FIRST, with E3104 naming `__gt_process_run` — a correct refusal about a different property, and one no
single pinned text can express alongside this one. The twin below pins that half.
```maxon
function main() returns ExitCode
	let code = __Builtins.runProcess("cmd /c exit 1")
	return code as ExitCode
end 'main'
```
```maxoncstderr
error E3057: <fragment>:3:24: throwing subprocess call requires try: wrap `__Builtins.runProcess(…)` as `try __Builtins.runProcess(…) otherwise …` — a bare call drops the spawn-failure error
```

<!-- test: async-subprocess.error.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The other half: it spawns a Windows child through `__gt_process_run`, so a program that reaches it
on any other target is refused at the call's own span with **E3104**. ⚠ It was OUTSIDE that gate until the
subprocess rung, and `SemanticCheck.calleeNeedsWin32Substrate`'s header recorded what that cost: *"on another
target they still die as a BACKEND PANIC rather than a diagnostic — MEASURED"*.
```maxon
function main() returns ExitCode
	let code = try __Builtins.runProcess("cmd /c exit 1") otherwise return 9
	return code as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:28: this construct lowers to the runtime entry '__gt_process_run', which has no wasm32-wasi implementation
```

<!-- test: async-subprocess.rejected-on-wasm-when-unreached -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
**The retirement did NOT move this program, and that is the half worth pinning.** `spawner` is never called,
yet its intrinsic is still refused: `__Builtins.runProcess` emits `__gt_process_run` in USER code, where
`SemanticCheck.requireTargetSupportsCallee` is reachability-BLIND — it visits every function, and
dead-function elimination runs two tiers later. The twin `async-sleep.unreached-compiles-on-wasm` shows the
opposite outcome for the retirement that DID hand its entry to stdlib source, where the gate is
reachability-AWARE; this entry has no stdlib declaration to move to (see the *Documentation* section), so it
keeps the property at the spelling that still has it, exactly as `builtins-sleep.rejected-on-wasm-when-unreached`
does for `__Builtins.sleep`.
```maxon
function spawner() returns Integer
	return try __Builtins.runProcess("cmd /c exit 1") otherwise 9
end 'spawner'

function main() returns ExitCode
	return 4
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3104: <fragment>:3:24: this construct lowers to the runtime entry '__gt_process_run', which has no wasm32-wasi implementation
```

<!-- test: async-subprocess.the-bare-name-is-an-ordinary-declaration -->
**THE DOOR THE RETIREMENT OPENED, and the defect it closed.** `runProcess` is no longer claimed by the parser,
so a program may declare one and it is REACHED — with its own arity, its own `name:` labels and its own return
type, none of which the one-argument builtin could express. Before the retirement this program was
`E3036: 'runProcess' takes exactly 1 argument, but 3 were given`, reported against the call while the
declaration sat there unreachable and undiagnosed — the shape `maxon-bin/Testing/SpecTestRunner.maxon`'s own
four-parameter `runProcess` hit, and G17 defect 1. Target-neutral: nothing here reaches a runtime entry.
The three arguments carry 1, 2 and 4 so their SUM names exactly which of them arrived: a dropped or
transposed label changes the total to a different, distinguishable number rather than to another 7.
```maxon
function runProcess(exe Integer, argv Integer, workingDirectory Integer) returns Integer
	return exe + argv + workingDirectory
end 'runProcess'

function main() returns ExitCode
	let n = runProcess(1, argv: 2, workingDirectory: 4)
	return n as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
