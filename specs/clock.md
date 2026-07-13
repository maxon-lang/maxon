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
returned after as little as ~16 ms: the scheduler computed its wake deadline from
the COARSE ~15.6 ms tick (`GetTickCount64`), so the deadline could expire a full
tick before the requested duration had actually elapsed. The deadline is now
anchored to the monotonic nanosecond clock, so the real bound holds.

```maxon
function main() returns ExitCode
		let requestedNanos = 30000000
		let start = Clock.nowNanos()
		sleep(30)
		let elapsed = Clock.elapsedNanos(start)
		var score = 0
		if elapsed >= requestedNanos 'neverEarly'
				score = score + 1
		end 'neverEarly'
		if elapsed < 10000000000 'bounded'
				score = score + 1
		end 'bounded'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```
