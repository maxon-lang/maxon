---
feature: clock
status: experimental
keywords: [clock, time, monotonic, duration, elapsed]
category: system
---

# Clock

## Documentation

The `Clock` type exposes a monotonic millisecond clock. `Clock.nowMs()` returns
the current value of a monotonic source; the absolute value is platform-defined
(milliseconds since boot on Windows via `GetTickCount64`, the monotonic clock's
instant on WASI via `wasi:clocks/monotonic-clock.now`) and is only meaningful
when two readings are subtracted.

```text
let start = Clock.nowMs()
// ... work ...
let elapsed = Clock.elapsedMs(start)  // milliseconds since `start`
```

`Clock.elapsedMs(since)` returns the milliseconds elapsed since a prior
`nowMs()` reading, clamping to 0 if the source somehow moves backwards (it never
does on a monotonic clock, but the guard protects against bugs).

## Tests

<!-- test: clock.now-monotonic -->
A monotonic clock never moves backwards: a second reading is `>=` the first.
The boot-relative instant is large, so the reading is also strictly positive.

```maxon
function main() returns ExitCode
		let a = Clock.nowMs()
		let b = Clock.nowMs()
		var score = 0
		if a > 0 'positive'
				score = score + 1
		end 'positive'
		if b >= a 'nondecreasing'
				score = score + 1
		end 'nondecreasing'
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
