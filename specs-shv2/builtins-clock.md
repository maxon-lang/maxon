---
feature: builtins-clock
status: stable
keywords: [builtins, __Builtins, clock, time, monotonic, wall-clock, intrinsics]
category: system
---

# `__Builtins` clock intrinsics

## Documentation

`__Builtins` is the compiler's builtin TYPE: a name reserved for the compiler, whose static
methods are INTRINSICS rather than functions any file declares. `stdlib/Clock.maxon` is written
against three of them, and they are what this spec pins:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.currentTimeNanos()` | monotonic nanoseconds, from the platform's high-resolution counter |
| `__Builtins.currentTimeMs()` | the same reading, in whole milliseconds |
| `__Builtins.currentUnixTimeSeconds()` | WALL-clock seconds since 1970-01-01 UTC |

The first two are a STOPWATCH (monotonic; only a difference between two readings means anything);
the third is a CALENDAR (it can step backwards when NTP corrects a drift, so it must never be used
to measure a duration). They are `int`-valued and take no arguments.

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

### The substrate is x64-windows only at this rung

All three read the host clock through Win32 imports, and the green-thread runtime that hosts the
monotonic one has no arm64/wasm lowering. A program that reaches any of them on another target is
refused with `E3104` at the call site rather than panicking three tiers down in the wasm/arm64
backend.

⚠ **Which cases that gates, exactly**: only the ones that REACH `__gt_now_ns`/`__clock_now_unix_s`.
`arity-checked` and `unknown-intrinsic` are refused in the front end, are target-neutral, and carry NO
marker — `arity-checked` wore one until the 2026-07-28 targets audit measured it green on x64-linux and
wasm32-wasi. See `async-scheduler.md`'s *Targets* section for the one statement of the substrate gate.

## Tests

<!-- test: builtins-clock.nanos-monotonic -->
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
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
