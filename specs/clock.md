---
feature: clock
status: experimental
keywords: [clock, time, monotonic, duration, elapsed]
category: system
---

# Clock

## Documentation

The `Clock` type exposes two monotonic clocks. Both return a reading whose
absolute value is platform-defined and only meaningful when two readings are
subtracted; they differ in the hardware source they read, and therefore in
resolution.

`Clock.nowMs()` reads the platform's COARSE tick counter (`GetTickCount64` on
Windows, `wasi:clocks/monotonic-clock.now` on WASI).

```text
let start = Clock.nowMs()
// ... work ...
let elapsed = Clock.elapsedMs(start)  // milliseconds since `start`
```

`Clock.nowNanos()` reads the platform's HIGH-RESOLUTION counter
(`QueryPerformanceCounter` on Windows, `clock_gettime(CLOCK_MONOTONIC)` on
Linux/macOS, `wasi:clocks/monotonic-clock.now` on WASI) and reports nanoseconds.

```text
let start = Clock.nowNanos()
// ... work ...
let elapsed = Clock.elapsedNanos(start)  // nanoseconds since `start`
```

Prefer `nowNanos()` for anything you intend to measure. `GetTickCount64`'s period
is ~15.6 ms, so `nowMs()` cannot resolve a duration shorter than a scheduler
tick: every reading is a multiple of the tick, and a sub-tick operation measures
as either 0 ms or 16 ms depending on where the tick happened to fall. `nowMs()`
remains the cheaper read and is the right choice for coarse timeouts and
deadlines.

Note that `nowNanos()`'s UNIT is nanoseconds but its PERIOD is platform-defined —
100 ns on a typical Windows machine, 1 ns elsewhere — so two back-to-back
readings can legitimately compare equal.

`Clock.elapsedMs(since)` / `Clock.elapsedNanos(since)` return the time elapsed
since a prior reading from the matching clock, clamping to 0 if the source
somehow moves backwards (it never does on a monotonic clock, but the guard
protects against bugs).

### WallClock — the calendar

`Clock` is a stopwatch. `WallClock` is a calendar, and they are different
instruments.

Every reading `Clock` gives you is monotonic: its absolute value is meaningless
(milliseconds since boot, performance-counter ticks) and only the DIFFERENCE
between two readings means anything. That makes it exactly right for "how long
did this take" and useless for "what is today's date" — no arithmetic turns an
uptime into a calendar day.

`WallClock.nowUnixSeconds()` returns whole seconds since the Unix epoch
(1970-01-01 UTC), read from `GetSystemTimeAsFileTime` on Windows and
`clock_gettime(CLOCK_REALTIME)` on macOS/Linux.

```text
let now = WallClock.nowUnixSeconds()  // e.g. 1783990842
```

It must never be used to measure a duration. A wall clock can step BACKWARDS —
NTP corrects a drift, a user changes the timezone, a VM resumes from a snapshot —
and a duration measured across such a step comes out negative or absurd. That is
precisely the bug `Clock`'s monotonic guarantee exists to prevent.

    duration → Clock.elapsedNanos / Clock.elapsedMs
    date     → WallClock.nowUnixSeconds

No timezone is applied. The caller converts if it wants local time, because the
offset is a policy decision the stdlib has no business guessing.

## Tests

<!-- test: clock.now-monotonic -->
A monotonic clock never moves backwards: each successive reading is `>=` the
previous one. The epoch is target-dependent (boot-relative on native targets,
process-relative under WASI, where a fast-starting program's first reading can
legitimately be 0), so only ordering is asserted, never a particular magnitude.

```maxon
function main() returns ExitCode
		let a = Clock.nowMs()
		let b = Clock.nowMs()
		let c = Clock.nowMs()
		var score = 0
		if b >= a 'nondecreasing1'
				score = score + 1
		end 'nondecreasing1'
		if c >= b 'nondecreasing2'
				score = score + 1
		end 'nondecreasing2'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```

<!-- test: clock.elapsed-after-sleep -->
After sleeping ~30 ms the elapsed time is non-zero and within a generous upper
bound, proving the clock measures real wall time rather than returning a
constant.

```maxon
function main() returns ExitCode
		let start = Clock.nowMs()
		sleep(30)
		let elapsed = Clock.elapsedMs(start)
		var score = 0
		if elapsed > 0 'advanced'
				score = score + 1
		end 'advanced'
		if elapsed < 10000 'bounded'
				score = score + 1
		end 'bounded'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```

<!-- test: clock.elapsed-clamps-on-equal -->
Two back-to-back readings with no work between them elapse 0 ms (or a tiny
positive amount); `elapsedMs` never returns a negative value.

```maxon
function main() returns ExitCode
		let start = Clock.nowMs()
		let elapsed = Clock.elapsedMs(start)
		var ok = 0
		if elapsed < 10000 'bounded'
				ok = 1
		end 'bounded'
		print("ok={ok}\n")
		return 0
end 'main'
```
```stdout
ok=1
```

<!-- test: clock.now-nanos-monotonic -->
The high-resolution clock is monotonic too: each successive reading is `>=` the
previous one. As with `nowMs`, the epoch is target-dependent, so only ordering is
asserted — never a particular magnitude.

```maxon
function main() returns ExitCode
		let a = Clock.nowNanos()
		let b = Clock.nowNanos()
		let c = Clock.nowNanos()
		var score = 0
		if b >= a 'nondecreasing1'
				score = score + 1
		end 'nondecreasing1'
		if c >= b 'nondecreasing2'
				score = score + 1
		end 'nondecreasing2'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```

<!-- test: clock.nanos-resolves-sub-millisecond -->
The whole point of `nowNanos`: it must actually resolve durations shorter than the
coarse clock's tick. This walks a tight loop, records the SMALLEST non-zero delta
between two successive readings — which is precisely the counter's period — and
asserts it is under 1 ms.

A clock backed by the Windows tick counter would fail this: `GetTickCount64`
advances in ~15.6 ms steps, so its smallest observable non-zero delta is
~15,600,000 ns. Passing therefore proves the reading comes from the performance
counter and not from a coarse fallback — the regression this test exists to catch
is exactly a silent downgrade to the tick source.

```maxon
function main() returns ExitCode
		var smallest = 0
		var prev = Clock.nowNanos()
		var i = 0
		while i < 200000 'sample'
				let now = Clock.nowNanos()
				let delta = now - prev
				if delta > 0 'advanced'
						if smallest == 0 or delta < smallest 'newMin'
								smallest = delta
						end 'newMin'
						prev = now
				end 'advanced'
				i = i + 1
		end 'sample'
		var ok = 0
		if smallest > 0 and smallest < 1000000 'subMillisecond'
				ok = 1
		end 'subMillisecond'
		print("subMs={ok}\n")
		return 0
end 'main'
```
```stdout
subMs=1
```

<!-- test: clock.elapsed-nanos-after-sleep -->
`sleep(N)` NEVER returns early: after `sleep(30)` the nanosecond clock must report
at least 30 ms of elapsed real time. This is the sleep's actual contract — a
duration is a floor, not an estimate — and asserting it here also proves the clock
measures real wall time in the UNITS it claims.

The lower bound doubles as a scale check. Had the reading been raw
`QueryPerformanceCounter` ticks (~300,000 for 30 ms at a 10 MHz counter),
microseconds (~30,000), or milliseconds (~30), every one of those falls far short
of 30,000,000 and the test fails.

Only the LOWER bound is tight, because only the lower bound is a correctness
property. A loaded machine can make any sleep run arbitrarily long — the scheduler
just doesn't get to the timer promptly — so a tight upper bound would be a flaky
assertion about the host, not about the compiler. The 10 s ceiling exists solely to
catch a grossly mis-scaled counter, not to police wake latency.

This test previously asserted a 5 ms lower bound, because `sleep(30)` genuinely
returned after as little as ~17 ms: the scheduler computed its wake deadline from
the COARSE ~15.6 ms tick (`GetTickCount64`), so the deadline could expire a full
tick before the requested duration had actually elapsed. The deadline is now
anchored to the monotonic nanosecond clock, so the real bound holds.

**The sleep is PHASE-SWEPT, and that is what gives this test teeth.** A tick-derived
deadline only expires early when the call lands LATE within a tick — the deadline is
anchored to the tick edge already behind you, so the later in the tick you call, the
more of the requested duration is eaten. Sleeps naturally synchronise to the tick edge
and then stay in phase, so a single `sleep(30)`, or a fixed-cadence loop of them,
samples essentially ONE phase: measured against the buggy runtime, a lone `sleep(30)`
returned early only about two times in three, and a 40-iteration fixed-cadence loop
came back fully green on its second run. A test that passes a third of the time on a
broken compiler is not a gate. Busy-spinning a growing amount before each sleep walks
the call site across the whole tick period, and the buggy runtime then fails 44 of 64.

```maxon
function main() returns ExitCode
		let requestedNanos = 30000000
		let reps = 16
		var early = 0
		var slowest = 0
		var spins = 0

		for i in 0 upto reps 'sweep'
				let spinStart = Clock.nowNanos()

				while Clock.elapsedNanos(spinStart) < i * 1000000 'burn'
						spins = spins + 1
				end 'burn'

				let start = Clock.nowNanos()
				sleep(30)
				let elapsed = Clock.elapsedNanos(start)

				if elapsed < requestedNanos 'tooSoon'
						early = early + 1
				end 'tooSoon'

				if elapsed > slowest 'newSlowest'
						slowest = elapsed
				end 'newSlowest'
		end 'sweep'

		var score = 0
		if early == 0 'neverEarly'
				score = score + 1
		end 'neverEarly'
		if slowest < 10000000000 'bounded'
				score = score + 1
		end 'bounded'
		if spins > 0 'sweptTheTick'
				score = score + 1
		end 'sweptTheTick'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=3
```

<!-- test: clock.wall-clock-is-a-calendar-time -->
`WallClock.nowUnixSeconds()` returns a real calendar time, and the bounds are the
whole test.

The LOWER bound is what catches the regression this exists for: a wall clock
silently wired to the monotonic source. `GetTickCount64` reports milliseconds
since BOOT, so seconds-since-boot on any real machine is at most a few million —
a host would have to have been powered on continuously since 1970 to reach
1735689600. Any monotonic source fails this by four orders of magnitude.

The UPPER bound catches the other half: a mis-scaled unit. Had the reading come
back in milliseconds (~1.78e12) or nanoseconds (~1.78e18) rather than seconds, it
would sail past 2100 and fail.

```maxon
function main() returns ExitCode
		let now = WallClock.nowUnixSeconds()
		var score = 0
		if now > 1735689600 'afterKnownPast'
				score = score + 1
		end 'afterKnownPast'
		if now < 4102444800 'beforeFarFuture'
				score = score + 1
		end 'beforeFarFuture'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```

<!-- test: clock.wall-clock-advances -->
The clock is live, not a constant folded in at compile time. Sleeping 1.1 s must
cross at least one whole-second boundary no matter where in the current second the
first reading landed.

```maxon
function main() returns ExitCode
		let before = WallClock.nowUnixSeconds()
		sleep(1100)
		let after = WallClock.nowUnixSeconds()
		var ok = 0
		if after >= before + 1 'advanced'
				ok = 1
		end 'advanced'
		print("ok={ok}\n")
		return 0
end 'main'
```
```stdout
ok=1
```
