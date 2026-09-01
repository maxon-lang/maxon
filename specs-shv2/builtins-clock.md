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
against three of them; a fourth has no stdlib surface and is read by the compiler itself. All four
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
process on the box: a compiler phase timed on a busy machine reports the machine. A single
`scale-test` run once read its parse phase at ×5.03 and then ×1.78 across a DOUBLING ladder, which
is not a curve of any shape — it is preemption.

`threadCpuTicks` advances only while the CALLING THREAD is scheduled on a core, so it cannot see
preemption and it cannot see any other process. That is what makes a per-phase cost survive a
loaded host, and it is why `Compiler/PhaseProbe.maxon` brackets it beside the wall clock and
`docs/optimization-log.md` carries a `## CPU` table.

MEASURED on this host, in one program: a busy loop advanced it **137,272,601** ticks while a
200 ms `sleep` advanced it **101,505** — a ratio of **1352** between a thread that was running and
one that was not, across two intervals of comparable WALL length. The bootstrap answers the same
shape (126,231,142 against 459,968).

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

`stdlib/Clock.maxon`'s doc-comment says `nowMs()` reads the platform's COARSE tick counter
(`GetTickCount64` on Windows, ~15.6 ms period). shv2 derives it from the same
`QueryPerformanceCounter` reading `currentTimeNanos()` uses, divided by 1,000,000.

That is a strictly STRONGER contract, not a weaker one, and it is why the stdlib was left alone:
every promise the doc-comment makes — monotonic, milliseconds, absolute value platform-defined —
still holds, and the resolution is finer than the one it warns about. A caller who reads the
comment and avoids sub-tick measurements is still correct; a caller who does not is no longer
wrong. Deriving it the other way round — a coarse tick where a fine one was available — is the
only direction that could break a caller, and `ms-resolves-sub-tick` below is the test that would
catch a silent downgrade to it.

### An unrecognized `__` callee is a diagnostic, not a panic

`__` is reserved for compiler internals: no file may DECLARE such a name (`E2051`), and the
compiler is the only party that may emit a call to one. A `__`-prefixed callee written in SOURCE
is therefore either one of the intrinsics the table above lists, or a name that does not exist —
and the second is refused at the call site with `E3004`, the same code a plain undefined function
gets.

Before this rule the reserved prefix routed such a call PAST every unknown-callee check
(`SemanticCheck.validateCall` returns early on a `__` callee, on the premise that nothing but the
compiler can produce one), and it reached the linker, where the failure had neither a file nor a
line:

```text
panic at X64Backend.maxon:1794: resolveCallFixups: call to unknown function '__Builtins.currentTimeMs'
```

### The substrate exists on the lanes that have written it, and nowhere else

All four read the host, and a lane serves them only once a backend has written that read out. A program
that reaches one on a lane whose `targetProvidesFacility` row denies the facility is refused with `E3104`
at the call site rather than panicking three tiers down in the backend.

⭐ **arm64-macOS SERVES ALL FOUR, WHICH IS WHY THE READING CASES BELOW NAME IT.** The calendar and the
thread-CPU read landed with the Darwin host surface; the MONOTONIC one landed with the green-thread
scheduler, because `__gt_now_ns` is a scheduler function and rides `usesGt`. All three are one libSystem
import asked with three different `clockid_t`s (`clock_gettime_nsec_np`), which is why one lane's
arithmetic is exact where Windows's is not: `Arm64DarwinRuntime` reports the monotonic frequency as 1e9
against a reading already in nanoseconds, making the ticks-to-nanos scaling the identity.

⭐ **arm64-LINUX SERVES TWO OF THE FOUR, AND WHICH TWO IS DECIDED BY WHO OWNS THE ENTRY RATHER THAN
BY WHICH CLOCK IT IS.** The calendar and the thread-CPU read are `clock_gettime` with two `clockid_t`s
and lower there; the two MONOTONIC readers do not, because both reach `__gt_now_ns`, a SCHEDULER entry
that lands with the green-thread floor. So a case that reads only the calendar or only the thread cost
names that lane below, and a case that reads the monotonic clock — or that sleeps — does not.

For `threadCpuTicks` the refusal is stronger than *"not yet"*, and it is the SHAPE argument the
machine query makes one family over: `QueryThreadCycleTime` answers TSC ticks through a `ULONG64*`
while `clock_gettime(CLOCK_THREAD_CPUTIME_ID)` answers nanoseconds through a `timespec`, so a POSIX
lane is a rung rather than a lowering — and WASI exposes no per-thread CPU clock at all. A lowering
there could only fabricate a cost, which is a silent wrong answer rather than a missing feature. Both
POSIX lanes have now paid that rung, in nanoseconds, and nothing converts between their unit and
Windows's: `__thread_cpu_ticks` promises only that two readings may be SUBTRACTED.

⚠ **ITS BAND IS `__thread_`, DELIBERATELY NOT `__clock_`**, and the split is about COST rather than
about targets: the two reach different kernel32 imports, and shv2 emits the optional trailing import
band per producer, so one shared bit would make a calendar-reading program import
`QueryThreadCycleTime` and a phase-timing program import `GetSystemTimeAsFileTime`. The ACCEPTANCE
half of the refusal below lives in `builtins-mm-counters.md` (`all-six-run-on-wasm`): without it,
`thread-cpu-ticks-rejected-on-wasm` would pass just as happily against a compiler that had stopped
serving the whole instrumentation family on that lane.

⚠ **Which cases that gates, exactly**: only the ones that REACH `__gt_now_ns`/`__clock_now_unix_s`.
`arity-checked` and `unknown-intrinsic` are refused in the front end, are target-neutral, and carry NO
marker — `arity-checked` wore one until the 2026-07-28 targets audit measured it green on x64-linux and
wasm32-wasi. See `async-scheduler.md`'s *Targets* section for the one statement of the substrate gate.

## Tests

<!-- test: builtins-clock.nanos-monotonic -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
A monotonic clock never moves backwards: each successive reading is `>=` the previous one. The
epoch is platform-defined, so only ordering is asserted, never a magnitude.
```maxon
function main() returns ExitCode
	let a = __Builtins.currentTimeNanos()
	let b = __Builtins.currentTimeNanos()
	let c = __Builtins.currentTimeNanos()
	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The clock is LIVE, not a constant folded in at compile time: busy-work between two readings shows
up as a positive delta, and the delta is in the units the intrinsic claims (a millisecond of
spinning is at least 1,000 nanoseconds by a wide margin, which raw QPC ticks — ~10,000 for a
millisecond at a 10 MHz counter — would also clear, so the LOWER bound alone does not pin the
scale; `ms-tracks-nanos` does).
```maxon
function main() returns ExitCode
	let start = __Builtins.currentTimeNanos()
	var spins = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
`currentTimeMs()` IS `currentTimeNanos()` scaled: two readings taken back to back must agree once
the nanosecond one is divided by 1,000,000. The window is generous (a scheduler preemption between
the two reads is legal) but tiny compared with any unit error — a millisecond reading that was
really microseconds or raw ticks would miss by three or four orders of magnitude, not by 50.
```maxon
function main() returns ExitCode
	let ns = __Builtins.currentTimeNanos()
	let ms = __Builtins.currentTimeMs()
	let nsAsMs = ns / 1000000
	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
**The test that pins the ruling above.** It walks a tight loop, records the SMALLEST non-zero delta
between two successive `currentTimeMs()` readings — which is precisely the source counter's period,
expressed in milliseconds — and asserts it is 1.

A clock backed by `GetTickCount64` fails this: that counter advances in ~15.6 ms steps, so its
smallest observable non-zero delta is 15 or 16. Passing therefore proves the reading is derived
from the performance counter and not from the coarse tick, which is exactly the regression a future
"simplification" of this lowering would introduce.
```maxon
function main() returns ExitCode
	var smallest = 0
	var prev = __Builtins.currentTimeMs()
	var i = 0
	while i < 200000 'sample'
		let now = __Builtins.currentTimeMs()
		let delta = now - prev
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
The two clocks are DIFFERENT instruments, and this is what says so: seconds-since-1970 is four
orders of magnitude larger than seconds-since-boot on any machine that has not been running since
1970, so a wall clock wired to the monotonic source collapses the gap.
```maxon
function main() returns ExitCode
	let uptimeSeconds = __Builtins.currentTimeNanos() / 1000000000
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
A clock intrinsic takes no arguments; one given an argument is refused by the same builtin-arity
check `trunc`/`sleep` use, because a builtin has no signature for the ordinary arity check to read.
```maxon
function main() returns ExitCode
	return __Builtins.currentTimeMs(5) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.currentTimeMs' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-clock.unknown-intrinsic -->
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
The same rejection in STATEMENT position, where the call's result is discarded — the path that
reached `resolveCallFixups` and panicked.
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

<!-- test: builtins-clock.rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The clock substrate is x64-windows only at this rung. On any other target the call is refused at
its source span with `E3104`, naming the runtime entry that has no lowering there — never a panic
from inside the wasm backend.
```maxon
function main() returns ExitCode
	return __Builtins.currentTimeNanos() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_now_ns', which has no wasm32-wasi implementation
```

<!-- test: builtins-clock.wall-clock-rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The wall clock is refused the same way, naming its own runtime entry.
```maxon
function main() returns ExitCode
	return __Builtins.currentUnixTimeSeconds() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct is x64-windows only at this rung: it lowers to the runtime entry '__clock_now_unix_s', which has no wasm32-wasi implementation
```

<!-- test: builtins-clock.thread-cpu-ticks-monotonic -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Three reads in one program never go backwards. A thread's consumed CPU time cannot decrease, so
this is what a capture of the wrong register, a sign-extension of a 32-bit half or a stale value
left in the out-param word would fail.
```maxon
function main() returns ExitCode
	let first = __Builtins.threadCpuTicks()
	let second = __Builtins.threadCpuTicks()
	let third = __Builtins.threadCpuTicks()
	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
Two million additions move it. The direct statement of the contract, and the property a lowering
that never called the OS — answering the `.data` scratch word's zero initializer forever — fails
first.
```maxon
function main() returns ExitCode
	let before = __Builtins.threadCpuTicks()
	var sum = 0
	for i in 0 upto 2000000 'spin'
		sum = sum + i
	end 'spin'
	let after = __Builtins.threadCpuTicks()
	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
**THE DISCRIMINATOR, AND THE WHOLE REASON THIS INTRINSIC EXISTS.** The program measures its own CPU
across two intervals of comparable WALL length — one spent RUNNING, one spent asleep — and requires
the running one to cost multiples more. A wall clock, which is the wrong lowering somebody would
plausibly write, reports the two intervals as roughly equal and fails; so does a constant.

It is written as a RATIO between two readings of the same instrument rather than as a comparison
against the wall clock, and deliberately: the two are in different, unconvertible units, so any
absolute threshold across them would be a normalization this spec's Documentation refuses. A ratio
is unit-free.

MEASURED on this host: 137,272,601 ticks busy against 101,505 asleep, a factor of **1352** where
the case asks for 4. Under a wall-clock lowering both readings become the interval's own duration,
and the busy interval is the SHORTER of the two.
```maxon
typealias SpinCount = int(0 to 100000000)
typealias SpinSum = int(0 to i64.max)

function spin(n SpinCount) returns SpinSum
	var sum = 0
	for i in 0 upto n 'each'
		sum = sum + i
	end 'each'
	return sum as SpinSum
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
	var score = 0
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
The CPU clock takes no arguments, and earns the same `builtinArity` refusal its three wall siblings
do. Front-end only and target-neutral, so it carries no marker.
```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks(5) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.threadCpuTicks' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-clock.thread-cpu-ticks-rejected-on-wasm -->
<!-- targets: wasm32-wasi -->
The CPU clock is refused the same way the other three are, naming its OWN runtime entry — which is
what says the gate is keyed on the `__thread_` band rather than on the clock band it shares a file
with. WASI exposes no per-thread CPU clock, so a lowering there could only fabricate a cost.

Its ACCEPTANCE half is `builtins-mm-counters.md`'s `all-six-run-on-wasm`, which proves the same
compiler still serves the rest of the instrumentation family on this lane.
```maxon
function main() returns ExitCode
	return __Builtins.threadCpuTicks() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: this construct is x64-windows only at this rung: it lowers to the runtime entry '__thread_cpu_ticks', which has no wasm32-wasi implementation
```
