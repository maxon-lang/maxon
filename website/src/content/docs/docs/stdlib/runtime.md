---
title: Runtime & Math
description: Clocks, the runtime, math functions, and extensions on the primitive types.
sidebar:
  order: 7
---

## Clock

`Clock` is a monotonic stopwatch: only the difference between two readings means anything. For the date
and time of day use `WallClock`.

| Method | Returns | Description |
|--------|---------|-------------|
| `Clock.nowNanos()` | `InstantNanos` | A high-resolution reading in nanoseconds. |
| `Clock.elapsedNanos(since InstantNanos)` | `DurationNanos` | Nanoseconds since a `nowNanos()` reading; `0` if the clock appears to go backwards. |
| `Clock.nowMs()` | `InstantMs` | The same clock in milliseconds. |
| `Clock.elapsedMs(since InstantMs)` | `DurationMs` | Milliseconds since a `nowMs()` reading. |

`since` is the first argument, so it is written without a label: `Clock.elapsedNanos(start)`.

| Target | Source | Period |
|--------|--------|--------|
| `x64-windows` | `QueryPerformanceCounter` | 100 ns |
| `arm64-macos`, `arm64-linux`, `x64-linux` | the kernel's monotonic clock | 1 ns |
| `wasm32-wasi` | refused at compile time (E3104) | |

Readings are always in nanoseconds, but two back-to-back readings can be equal when the period is coarser.

`InstantMs`, `DurationMs`, `InstantNanos` and `DurationNanos` are all `int(0 to u64.max)`.

### WallClock

| Method | Returns | Description |
|--------|---------|-------------|
| `WallClock.nowUnixSeconds()` | `UnixSeconds` | Whole seconds since 1970-01-01 00:00:00 UTC. No time zone is applied. |

`UnixSeconds` is `int(0 to u64.max)`. The wall clock can step backwards (an NTP correction, a resumed
virtual machine), so never measure a duration with it.

```maxon
function main() returns ExitCode
	let start = Clock.nowNanos()
	sleep(20)
	let took = Clock.elapsedNanos(start)
	print("{took >= 20000000} {WallClock.nowUnixSeconds() > 1700000000}\n")
	return 0
end 'main'
```

Output: `true true`.

## Runtime

`Runtime` holds controls over the green-thread scheduler. It is a namespace with no fields.

| Method | Description |
|--------|-------------|
| `Runtime.yield()` | Let the next runnable green thread run. The caller resumes behind everything that was already runnable. When nothing else is runnable it returns promptly, so a loop that yields is a busy wait that lets others progress. It uses no timer, unlike `sleep(0)`, and is safe in a program that never starts a green thread. |

Refused on `wasm32-wasi` (E3104). Green threads, `async` and `await` are described under Concurrency in
[LANGUAGE_REFERENCE.md](/docs/language/overview/).

```maxon
function worker() returns ExitCode
	Runtime.yield()
	print("worker\n")
	return 0
end 'worker'

function main() returns ExitCode
	let p = async worker()
	Runtime.yield()
	_ = await p
	print("main\n")
	return 0
end 'main'
```

Output: `worker` then `main`.

## Math

`Math` provides the elementary functions over `Real`, which is `float(f64.min to f64.max)`. The rounding and
`sqrt`/`abs`/`min`/`max` builtins are in [Core Functions](/docs/stdlib/#core-functions).

| Method | Returns | Description |
|--------|---------|-------------|
| `Math.sin(x Real)` | `Real` | Sine of radians. |
| `Math.cos(x Real)` | `Real` | Cosine of radians. |
| `Math.tan(x Real)` | `Real` | Tangent of radians. |
| `Math.atan(z Real)` | `Real` | Arc tangent. |
| `Math.atan2(y Real, x Real)` | `Real` | The angle of `(x, y)`, in `[-π, π]`. |
| `Math.exp(x Real)` | `Real` | e raised to `x`. |
| `Math.log(x Real)` | `Real` | Natural logarithm. |
| `Math.log2(x Real)` | `Real` | Base-2 logarithm. |
| `Math.log10(x Real)` | `Real` | Base-10 logarithm, computed as `log(x) / ln 10`, so exact powers of ten may be off in the last digit. |
| `Math.pow(base Real, exponent Real)` | `Real` | `base` raised to `exponent`, following IEEE 754's special cases (`pow(0.0, -1.0)` is infinity; `pow(x, 0)` and `pow(1, y)` are 1 even for NaN). |
| `Math.hasNegativeSignBit(z Real)` | `bool` | True when the sign bit is set, including `-0.0`, which no comparison can distinguish from `0.0`. |

These are implemented in Maxon from series expansions, so results can differ from a C library in the last
bit.

```maxon
function main() returns ExitCode
	print("{Math.sin(0.0)} {Math.exp(0.0)} {Math.log2(8.0)} {Math.pow(2.0, exponent: 10.0)}\n")
	print("{Math.atan2(1.0, x: 1.0)} {Math.hasNegativeSignBit(-0.0)}\n")
	return 0
end 'main'
```

Output: `0.0 1.0 3.0 1024.0` and `0.7853981633974483 true`.

## Primitive Extensions

`int`, `float` and `bool` conform to the core interfaces, so they work as `Map` keys, `Set` elements, in
`sort()` and in generic code.

| Type | Conforms to | Notes |
|------|-------------|-------|
| `int` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `hash()` is the low 32 bits of the value. |
| `float` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `compare` is a total order: NaN equals NaN and sorts below every other value. `-0.0` and `0.0` hash alike. |
| `bool` | `Comparable`, `Stringable`, `Cloneable` | `false` sorts before `true`. |

| Method | Returns | Description |
|--------|---------|-------------|
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other Self)` | `bool` | Through `Equatable`. |
| `compare(other Self)` | `Ordering` | Through `Comparable`. |
| `toString()` | `String` | The same text as interpolation. |
| `clone()` | `Self` | The value itself. |
| `int.fromString(input String)` | `int` | Throws `ParseError.invalidFormat`; likewise `float.fromString`, `bool.fromString`, `byte.fromString`. |

```maxon
function main() returns ExitCode
	let five = 5
	let half = 2.5
	let yes = true
	print("{five.compare(3)} {five.hash()} {half.compare(2.5)} {yes.compare(false)} {five.toString()}\n")
	return 0
end 'main'
```

Output: `greaterThan 5 equalTo greaterThan 5`.
