---
feature: builtins-clock
status: stable
keywords: [builtins, __Builtins, clock, time, monotonic, wall-clock, cpu-time, intrinsics]
category: system
---

# `__Builtins` clock intrinsics

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static
methods are INTRINSICS rather than functions any file declares. `stdlib/Clock.maxon` is written
against all four (the fourth through `Clock.threadCpuTicks()`). All four
are what this spec pins:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.currentTimeNanos()` | monotonic nanoseconds, from the platform's high-resolution counter |
| `__Builtins.currentTimeMs()` | the same reading, in whole milliseconds |
| `__Builtins.currentUnixTimeSeconds()` | WALL-clock seconds since 1970-01-01 UTC |
| `__Builtins.threadCpuTicks()` | CPU time consumed by the CALLING THREAD, in a platform-defined unit |

The first two are a STOPWATCH (monotonic; only a difference between two readings means anything);
the third is a CALENDAR (it can step backwards when NTP corrects a drift, so it must never be used
to measure a duration). They are `int`-valued and take no arguments.

### The fourth is the only one that is NOT a clock

The three above all measure WALL time, so a duration taken with any of them counts every OTHER
process on the box: a compiler phase timed on a busy machine reports the machine, and on a
`scale-test` DOUBLING ladder that preemption reads as ratios that fit no curve of any shape.

`threadCpuTicks` advances only while the CALLING THREAD is scheduled on a core, so it cannot see
preemption and it cannot see any other process. That is what makes a per-phase cost survive a
loaded host, and it is why `Compiler/PhaseProbe.maxon` brackets it beside the wall clock and
`docs/optimization-log.md` carries a `## CPU` table.

A busy loop advances it about three orders of magnitude further than a `sleep` of comparable WALL
length: a thread that is not running accrues almost nothing.

⚠ **ITS UNIT IS PLATFORM-DEFINED AND NOTHING CONVERTS IT**: TSC ticks on Windows
(`QueryThreadCycleTime`), nanoseconds under POSIX (`clock_gettime(CLOCK_THREAD_CPUTIME_ID)`).
`QueryPerformanceFrequency` is the PERFORMANCE COUNTER's rate and not the TSC's, so a normalization
would be a guess wearing a unit's name. Compare RATIOS, which are unit-free, or absolutes within
one platform. It is also NOT a retired-instruction count and is not reproducible to the digit — it
still moves with turbo, thermal throttling and cache pressure from other cores, by a few percent.
Against the only question a doubling ladder asks (×2 is linear, ×4 is quadratic) that band has a
100% margin; against a claimed 3% constant-factor win it is worth nothing, and the allocation
columns are what answer that.

⚠ **IT IS NOT CLAMPED, unlike `__Builtins.cpuCount()`.** A processor count is a DIVISOR for its
callers, so a 0 escaping there would be a division by zero; a tick count is only ever subtracted
from a later one, and a floor would make the first bracket of a phase report a cost it did not
have.

### `currentTimeMs` is the NANOSECOND clock scaled, not the coarse tick

Both monotonic builtins read the one runtime entry `__gt_now_ns`: `QueryPerformanceCounter` scaled
to nanoseconds on Windows, `clock_gettime(CLOCK_MONOTONIC)` on Linux and
`clock_gettime(CLOCK_UPTIME_RAW)` on macOS and the WASI monotonic clock on wasm32-wasi.
`currentTimeNanos()` returns that reading, and `currentTimeMs()` divides it by 1,000,000 inline.

So a millisecond reading resolves to one millisecond on every native lane, never to a scheduler
tick. A coarse tick source such as `GetTickCount64` (~15.6 ms period) would still be monotonic and
still be milliseconds, but a caller measuring a short interval would read 0 or 16;
`ms-resolves-sub-tick` below is the test that catches a downgrade to one.

### An unrecognized `__` callee is a diagnostic, not a panic

`__` is reserved for compiler internals: no file may DECLARE such a name (`E2051`), and the
compiler is the only party that may emit a call to one. A `__`-prefixed callee written in SOURCE
is therefore either one of the intrinsics the table above lists, or a name that does not exist —
and the second is refused at the call site with `E3004`, the same code a plain undefined function
gets.

Without this rule the reserved prefix would route such a call PAST every unknown-callee check
(`SemanticCheck.validateCall` returns early on a `__` callee, on the premise that nothing but the
compiler can produce one), and it would reach the linker, where the failure has neither a file nor a
line:

```text
panic at X64Backend.maxon:1781: resolveCallFixups: call to unknown function '__Builtins.currentTimeMs'
```

### The substrate exists on the lanes that have written it, and nowhere else

All four read the host, and a lane serves them only once a backend has written that read out. A program
that reaches one on a lane whose `targetProvidesFacility` row denies the facility is refused with `E3104`
at the call site rather than panicking three tiers down in the backend.

⭐ **wasm32-wasi SERVES THE THREE CLOCKS, NOT THE CPU READ.** The monotonic reading is WASI's
`monotonic-clock` and the calendar is its `wall-clock`. WASI has no per-thread CPU clock and no
time-since-boot clock, so `threadCpuTicks` and the uptime read `tickCountMs` are refused there.

⭐ **arm64-macOS SERVES ALL FOUR, WHICH IS WHY THE READING CASES BELOW NAME IT.** The calendar and the
thread-CPU read belong to the Darwin host surface; the MONOTONIC one is `__gt_now_ns`, a scheduler
function that rides `usesGt`. All three are one libSystem
import asked with three different `clockid_t`s (`clock_gettime_nsec_np`), which is why one lane's
arithmetic is exact where Windows's is not: `Arm64DarwinRuntime` reports the monotonic frequency as 1e9
against a reading already in nanoseconds, making the ticks-to-nanos scaling the identity.

⭐ **THE TWO LINUX LANES SERVE ALL FOUR TOO**, each as `clock_gettime` with its own `clockid_t`: the
calendar, the thread-CPU read, and — through `__gt_now_ns`, a SCHEDULER entry that rides the
green-thread floor — `CLOCK_MONOTONIC`, the Linux id whose semantics match Darwin's `CLOCK_UPTIME_RAW`.

For `threadCpuTicks` the refusal is stronger than *"not yet"*, and it is the SHAPE argument the
machine query makes one family over: `QueryThreadCycleTime` answers TSC ticks through a `ULONG64*`
while `clock_gettime(CLOCK_THREAD_CPUTIME_ID)` answers nanoseconds through a `timespec`, so each lane
writes its own read rather than re-spelling another's — and WASI exposes no per-thread CPU clock at all.
A lowering there could only fabricate a cost, which is a silent wrong answer rather than a missing
feature. The POSIX lanes answer in nanoseconds, and nothing converts between their unit and
Windows's: `__thread_cpu_ticks` promises only that two readings may be SUBTRACTED.

⚠ **ITS BAND IS `__thread_`, DELIBERATELY NOT `__clock_`**, and the split is about COST rather than
about targets: the two reach different kernel32 imports, and the compiler emits the optional trailing import
band per producer, so one shared bit would make a calendar-reading program import
`QueryThreadCycleTime` and a phase-timing program import `GetSystemTimeAsFileTime`. The ACCEPTANCE
half of the refusal below lives in `builtins-mm-counters.md` (`all-six-run-on-wasm`): without it,
`thread-cpu-ticks-rejected-on-wasm` would pass just as happily against a compiler that had stopped
serving the whole instrumentation family on that lane.

⚠ **Which cases that gates, exactly**: on wasm32-wasi, only the ones that REACH `__thread_cpu_ticks` or
`__uptime_ms`. `arity-checked` and `unknown-intrinsic` are refused in the front end, are target-neutral, and carry NO
marker. See `async-scheduler.md`'s *Targets* section for the one statement of the substrate gate.

Every `__Builtins` member is library-tier: only a source file under `stdlib/` or `runtime/` may call one,
and a call from anywhere else is E3185. The cases below call the intrinsics themselves, so they compile in
the library tier (`<!-- tier: stdlib -->`). A program reads these clocks through `Clock.nowNanos()`,
`Clock.nowMs()`, `WallClock.nowUnixSeconds()` and `Clock.threadCpuTicks()`, and the machine's uptime through
`Clock.osTickMs()`; the last two cases read them that way.

## Tests

<!-- test: builtins-clock.nanos-monotonic -->
<!-- tier: stdlib -->
A monotonic clock never moves backwards: each successive reading is `>=` the previous one. The
epoch is platform-defined, so only ordering is asserted, never a magnitude.
```maxon
function main() returns ExitCode
	let a = __Builtins.currentTimeNanos()
	let b = __Builtins.currentTimeNanos()
	let c = __Builtins.currentTimeNanos()
	var score = 0 as Count
	if b >= a 'nondecreasing1'
		score = score + 1
	end 'nondecreasing1'
	if c >= b 'nondecreasing2'
		score = score + 1
	end 'nondecreasing2'
	return score as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: builtins-clock.nanos-advances -->
<!-- tier: stdlib -->
The clock is LIVE, not a constant folded in at compile time: busy-work between two readings shows
up as a positive delta, and the delta is in the units the intrinsic claims (a millisecond of
spinning is at least 1,000 nanoseconds by a wide margin, which raw QPC ticks — ~10,000 for a
millisecond at a 10 MHz counter — would also clear, so the LOWER bound alone does not pin the
scale; `ms-tracks-nanos` does).
```maxon
function main() returns ExitCode
	let start = __Builtins.currentTimeNanos()
	var spins = 0 as Count
	while spins < 200000 'burn'
		spins = spins + 1
	end 'burn'
	let elapsed = __Builtins.currentTimeNanos() - start
	if elapsed > 0 'advanced'
		return 7
	end 'advanced'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: builtins-clock.ms-tracks-nanos -->
<!-- tier: stdlib -->
`currentTimeMs()` IS `currentTimeNanos()` scaled: two readings taken back to back must agree once
the nanosecond one is divided by 1,000,000. The window is generous (a scheduler preemption between
the two reads is legal) but tiny compared with any unit error — a millisecond reading that was
really microseconds or raw ticks would miss by three or four orders of magnitude, not by 50.
```maxon
function main() returns ExitCode
	let ns = __Builtins.currentTimeNanos()
	let ms = __Builtins.currentTimeMs()
	let nsAsMs = (ns / 1000000) as InstantMs
	var score = 0 as Count
	if ms >= nsAsMs 'notBehind'
		score = score + 1
	end 'notBehind'
	if ms - nsAsMs < 50 'notAhead'
		score = score + 1
	end 'notAhead'
	return score as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: builtins-clock.ms-resolves-sub-tick -->
<!-- tier: stdlib -->
**The test that pins the millisecond resolution above.** It walks a tight loop, records the SMALLEST non-zero delta
between two successive `currentTimeMs()` readings — which is precisely the source counter's period,
expressed in milliseconds — and asserts it is 1.

A clock backed by `GetTickCount64` fails this: that counter advances in ~15.6 ms steps, so its
smallest observable non-zero delta is 15 or 16. Passing therefore proves the reading is derived
from the performance counter and not from the coarse tick, which is exactly the regression a future
"simplification" of this lowering would introduce.
```maxon
function main() returns ExitCode
	var smallest = 0 as DurationMs
	var prev = __Builtins.currentTimeMs()
	var i = 0 as Count
	while i < 200000 'sample'
		let now = __Builtins.currentTimeMs()
		let delta = (now - prev) as DurationMs
		if delta > 0 'advanced'
			if smallest == 0 or delta < smallest 'newMin'
				smallest = delta
			end 'newMin'
			prev = now
		end 'advanced'
		i = i + 1
	end 'sample'
	if smallest > 0 and smallest < 5 'subTick'
		return 9
	end 'subTick'
	return 1
end 'main'
```
```exitcode
9
```

<!-- test: builtins-clock.unix-seconds-is-a-calendar-time -->
<!-- tier: stdlib -->
`currentUnixTimeSeconds()` returns a real calendar time, and the bounds are the whole test.

The LOWER bound catches the regression it exists for: a wall clock silently wired to the monotonic
source. A monotonic reading is time since BOOT, at most a few million seconds on any real machine —
a host would have to have been powered on continuously since 1970 to reach 1735689600. Any
monotonic source fails this by four orders of magnitude.

The UPPER bound catches the other half, a mis-scaled unit: a reading in milliseconds (~1.78e12) or
nanoseconds (~1.78e18) sails past 2100.
```maxon
function main() returns ExitCode
	let now = __Builtins.currentUnixTimeSeconds()
	var score = 0 as Count
	if now > 1735689600 'afterKnownPast'
		score = score + 1
	end 'afterKnownPast'
	if now < 4102444800 'beforeFarFuture'
		score = score + 1
	end 'beforeFarFuture'
	return score as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: builtins-clock.unix-seconds-advances -->
<!-- tier: stdlib -->
The wall clock is live, not a constant read once at startup. Sleeping 1.1 s must cross at least one
whole-second boundary no matter where in the current second the first reading landed.
```maxon
function main() returns ExitCode
	let before = __Builtins.currentUnixTimeSeconds()
	sleep(1100)
	let after = __Builtins.currentUnixTimeSeconds()
	if after >= before + 1 'advanced'
		return 4
	end 'advanced'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: builtins-clock.wall-clock-is-not-the-monotonic-clock -->
<!-- tier: stdlib -->
The two clocks are DIFFERENT instruments, and this is what says so: seconds-since-1970 is four
orders of magnitude larger than seconds-since-boot on any machine that has not been running since
1970, so a wall clock wired to the monotonic source collapses the gap.
```maxon
function main() returns ExitCode
	let uptimeSeconds = (__Builtins.currentTimeNanos() / 1000000000) as UnixSeconds
	let wallSeconds = __Builtins.currentUnixTimeSeconds()
	if wallSeconds > uptimeSeconds + 1000000000 'differentEpochs'
		return 5
	end 'differentEpochs'
	return 1
end 'main'
```
```exitcode
5
```

<!-- test: builtins-clock.arity-checked -->
<!-- tier: stdlib -->
A clock intrinsic takes no arguments; one given an argument is refused by the same builtin-arity
check `trunc`/`sleep` use, because a builtin has no signature for the ordinary arity check to read.
```maxon
function main() returns ExitCode
	return __Builtins.currentTimeMs(5) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.currentTimeMs' takes exactly 0 arguments, but 1 was given
```

<!-- test: builtins-clock.unknown-intrinsic -->
<!-- tier: stdlib -->
`__Builtins.nope()` names no intrinsic. It is refused at the call site with `E3004` — the code a
plain undefined function gets — instead of reaching the linker and panicking without a file or a
line.
```maxon
function main() returns ExitCode
	return __Builtins.nope() as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:20: call to undefined function '__Builtins.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-clock.unknown-internal-callee -->
Every `__`-prefixed callee is covered, not only the `__Builtins` ones: `__whatever()` is a name no
file may declare and no intrinsic provides.
```maxon
function main() returns ExitCode
	return __whatever() as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function '__whatever': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-clock.unknown-internal-callee-statement -->
The same rejection in STATEMENT position, where the call's result is discarded — the path on which an
unrejected call would reach `resolveCallFixups` and panic.
```maxon
function main() returns ExitCode
	__whatever()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:2: call to undefined function '__whatever': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-clock.unknown-internal-callee-async -->
And in `async` position, which reached a DIFFERENT panic — `lowerAsyncCall`'s, whose own message
said an unknown callee must have been rejected before lowering.
```maxon
function main() returns ExitCode
	let p = async __whatever()
	return await p as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function '__whatever': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: builtins-clock.monotonic-clock-runs-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
On wasm32-wasi the monotonic reading is WASI's `monotonic-clock`: a running program reads a positive
value, and a second reading does not move backwards.
```maxon
function main() returns ExitCode
	let first = __Builtins.currentTimeNanos()
	let second = __Builtins.currentTimeNanos()
	if first > 0 and second >= first 'readsTheClock'
		return 4
	end 'readsTheClock'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: builtins-clock.wall-clock-runs-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
On wasm32-wasi the calendar is WASI's `wall-clock`, so it reads a date after November 2023
(Unix second 1,700,000,000), not zero.
```maxon
function main() returns ExitCode
	if __Builtins.currentUnixTimeSeconds() > 1700000000 'readsTheCalendar'
		return 4
	end 'readsTheCalendar'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: builtins-clock.thread-cpu-ticks-monotonic -->
<!-- tier: stdlib -->
Three reads in one program never go backwards. A thread's consumed CPU time cannot decrease, so
this is what a capture of the wrong register, a sign-extension of a 32-bit half or a stale value
left in the out-param word would fail.
```maxon
function main() returns ExitCode
	let first = __Builtins.threadCpuTicks()
	let second = __Builtins.threadCpuTicks()
	let third = __Builtins.threadCpuTicks()
	var score = 0 as Count
	if second >= first 'firstPair'
		score = score + 1
	end 'firstPair'
	if third >= second 'secondPair'
		score = score + 2
	end 'secondPair'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-clock.thread-cpu-ticks-advances-under-work -->
<!-- tier: stdlib -->
Two million additions move it. The direct statement of the contract, and the property a lowering
that never called the OS — answering the `.data` scratch word's zero initializer forever — fails
first.
```maxon
function main() returns ExitCode
	let before = __Builtins.threadCpuTicks()
	var sum = 0 as Count
	for i in 0 upto 2000000 'spin'
		sum = sum + i
	end 'spin'
	let after = __Builtins.threadCpuTicks()
	var score = 0 as Count
	if sum > 0 'workHappened'
		score = score + 1
	end 'workHappened'
	if after > before 'cpuAdvanced'
		score = score + 3
	end 'cpuAdvanced'
	return score as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: builtins-clock.thread-cpu-ticks-is-not-wall-time -->
<!-- tier: stdlib -->
**THE DISCRIMINATOR, AND THE WHOLE REASON THIS INTRINSIC EXISTS.** The program measures its own CPU
across two intervals of comparable WALL length — one spent RUNNING, one spent asleep — and requires
the running one to cost multiples more. A wall clock, which is the wrong lowering somebody would
plausibly write, reports the two intervals as roughly equal and fails; so does a constant.

It is written as a RATIO between two readings of the same instrument rather than as a comparison
against the wall clock, and deliberately: the two are in different, unconvertible units, so any
absolute threshold across them would be a normalization this spec's Documentation refuses. A ratio
is unit-free.

The case asks for a factor of 4. Under a wall-clock lowering both readings become the interval's own duration,
and the busy interval is the SHORTER of the two.
```maxon
typealias SpinCount = int(0 to 100000000)
typealias SpinSum = int(0 to i64.max)

function spin(n SpinCount) returns SpinSum
	var sum = 0 as SpinSum
	for i in 0 upto n 'each'
		sum = sum + (i as SpinSum)
	end 'each'
	return sum
end 'spin'

function main() returns ExitCode
	let busyCpuBefore = __Builtins.threadCpuTicks()
	let worked = spin(20000000)
	let busyCpuAfter = __Builtins.threadCpuTicks()

	let sleepWallBefore = __Builtins.currentTimeNanos()
	let sleepCpuBefore = __Builtins.threadCpuTicks()
	__Builtins.sleep(200)
	let sleepCpuAfter = __Builtins.threadCpuTicks()
	let sleepWallAfter = __Builtins.currentTimeNanos()

	let busyCpu = busyCpuAfter - busyCpuBefore
	let sleepCpu = sleepCpuAfter - sleepCpuBefore
	let sleepWall = sleepWallAfter - sleepWallBefore
	var score = 0 as Count
	if worked > 0 'workReallyRan'
		score = score + 1
	end 'workReallyRan'
	if sleepWall >= 200000000 'sleptTheWholeTime'
		score = score + 2
	end 'sleptTheWholeTime'
	if busyCpu > sleepCpu * 4 'cpuFollowsTheThreadNotTheClock'
		score = score + 4
	end 'cpuFollowsTheThreadNotTheClock'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-clock.thread-cpu-ticks-arity-checked -->
<!-- tier: stdlib -->
The CPU clock takes no arguments, and earns the same `builtinArity` refusal its three wall siblings
do. Front-end only and target-neutral, so it carries no marker.
```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks(5) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.threadCpuTicks' takes exactly 0 arguments, but 1 was given
```

<!-- test: builtins-clock.thread-cpu-ticks-rejected-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The CPU clock is refused on wasm32-wasi while the other three run there, naming its OWN runtime entry —
which is what says the gate is keyed on the `__thread_` band rather than on the clock band it shares a
file with. WASI exposes no per-thread CPU clock, so a lowering there could only fabricate a cost.

Its ACCEPTANCE half is `builtins-mm-counters.md`'s `all-six-run-on-wasm`, which proves the same
compiler still serves the rest of the instrumentation family on this lane.
```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__thread_cpu_ticks', which has no wasm32-wasi implementation
```

<!-- test: builtins-clock.thread-cpu-ticks-rejected-on-wasm-when-unreached -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The gate is REACHABILITY-BLIND for the program's own source — a library-tier case's files included, because a project rooted under `stdlib/` keeps its own files as the program's rather than recording them as library source (`StdlibLoader.setAsideTheUsersOwnLibraryFiles`): `napper` is never called, yet its intrinsic is still refused.
`SemanticCheck` visits every function and dead-function elimination runs two tiers later, so this is the
same rule that reports a type error in an unreached function. Pinned because the stdlib loader's own
exemption (`checkCalls` skips an unreached stdlib body) points the other way: it is the unreached stdlib
BODY that is exempt, not an unreached function of the program's own.
```maxon
function napper() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'napper'

function main() returns ExitCode
	return 4
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__thread_cpu_ticks', which has no wasm32-wasi implementation
```

<!-- test: builtins-clock.uptime-clock-rejected-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The machine's uptime is refused on wasm32-wasi, naming its own runtime entry: WASI has no time-since-boot
clock, and the monotonic clock's origin is arbitrary, so it cannot stand in for one.
```maxon
function main() returns ExitCode
	return __Builtins.tickCountMs() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct lowers to the runtime entry '__uptime_ms', which has no wasm32-wasi implementation
```

<!-- test: builtins-clock.the-public-cpu-clock-follows-the-thread-not-the-wall -->
`thread-cpu-ticks-is-not-wall-time`'s discriminator, asked through the surface a program outside the library
tiers reads: `Clock.threadCpuTicks()` across a busy interval must cost multiples of what it costs across a
`sleep` of comparable wall length, timed with `Clock.nowNanos()`. wasm32-wasi has no per-thread CPU clock.
```maxon
typealias SpinCount = int(0 to 100000000)
typealias SpinSum = int(0 to i64.max)

function spin(n SpinCount) returns SpinSum
	var sum = 0 as SpinSum
	for i in 0 upto n 'each'
		sum = sum + (i as SpinSum)
	end 'each'
	return sum
end 'spin'

function main() returns ExitCode
	let busyCpuBefore = Clock.threadCpuTicks()
	let worked = spin(20000000)
	let busyCpuAfter = Clock.threadCpuTicks()

	let sleepWallBefore = Clock.nowNanos()
	let sleepCpuBefore = Clock.threadCpuTicks()
	sleep(200)
	let sleepCpuAfter = Clock.threadCpuTicks()
	let sleepWallAfter = Clock.nowNanos()

	let busyCpu = busyCpuAfter - busyCpuBefore
	let sleepCpu = sleepCpuAfter - sleepCpuBefore
	let sleepWall = sleepWallAfter - sleepWallBefore
	var score = 0 as ExitCode
	if worked > 0 'workReallyRan'
		score = score + 1
	end 'workReallyRan'
	if sleepWall >= 200000000 'sleptTheWholeTime'
		score = score + 2
	end 'sleptTheWholeTime'
	if busyCpu > sleepCpu * 4 'cpuFollowsTheThreadNotTheClock'
		score = score + 4
	end 'cpuFollowsTheThreadNotTheClock'
	return score
end 'main'
```
```exitcode
7
```

<!-- test: builtins-clock.the-os-tick-clock-counts-the-machines-uptime -->
`Clock.osTickMs()` is how long the MACHINE has been up, in milliseconds, so it is above zero in any program
that runs, never moves backwards, and covers a 100 ms `sleep` — less a host tick's granularity, which is at
most 16 ms on x64-windows. wasm32-wasi has no uptime clock.
```maxon
let SleepMs = 100

let TickGranularityMs = 16

function main() returns ExitCode
	let before = Clock.osTickMs()
	sleep(SleepMs)
	let after = Clock.osTickMs()
	var score = 0 as ExitCode
	if before > 0 'theMachineIsUp'
		score = score + 1
	end 'theMachineIsUp'
	if after >= before 'neverBackwards'
		score = score + 2
	end 'neverBackwards'
	if after - before >= SleepMs - TickGranularityMs 'theSleepWasCounted'
		score = score + 4
	end 'theSleepWasCounted'
	return score
end 'main'
```
```exitcode
7
```
