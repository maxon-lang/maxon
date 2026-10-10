---
title: System & Math
description: Clocks, the scheduler, math functions, and extensions on the primitive types.
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
| `WallClock.nowUnixSeconds()` | `UnixSeconds` | Whole seconds since 1970-01-01 00:00:00 UTC. |
| `WallClock.nowUnixNanos()` | `UnixNanos` | Nanoseconds since 1970-01-01 00:00:00 UTC. |
| `WallClock.rfc3339Millis(nanos UnixNanos)` | `String` | `nanos` as an RFC 3339 UTC time with milliseconds: `2023-11-14T22:13:20.123Z`. |
| `WallClock.rfc3339Nanos(nanos UnixNanos)` | `String` | The same with nanoseconds: `2023-11-14T22:13:20.123456789Z`. |

Every reading and format is in UTC. `UnixSeconds` is `int(0 to u64.max)`; `UnixNanos` is signed, negative
before 1970. The wall clock can step backwards (an NTP correction, a resumed virtual machine), so measure
durations with `Clock`.

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

### CivilDate

A `CivilDate` is a date in the proleptic Gregorian calendar, converted to and from a count of days since
1970-01-01 (`UnixDays`, signed).

| Member | Returns | Description |
|--------|---------|-------------|
| `CivilDate.create(year CivilYear, month CivilMonth, day CivilDay)` | `CivilDate` | A date from its parts. Throws `CivilDateError.impossibleDay` for a day past the end of its month (February 30, or February 29 in a common year). |
| `CivilDate.fromDays(days UnixDays)` | `CivilDate` | The date `days` after 1970-01-01. |
| `days()` | `UnixDays` | The inverse of `fromDays`. |
| `year`, `month`, `day` | `CivilYear`, `CivilMonth`, `CivilDay` | The parts; `month` and `day` count from 1. |

`CivilDateError` is the error `create` throws; its one case is `impossibleDay`.

```maxon
function main() returns ExitCode
	let date = CivilDate.fromDays(20000)
	let march = try CivilDate.create(2000, month: 3, day: 1) otherwise panic("2000-03-01 is a real date")
	print("{date.year}-{date.month:02}-{date.day:02} {march.days()}\n")
	print("{WallClock.rfc3339Millis(1700000000123456789)}\n")
	return 0
end 'main'
```

Output: `2024-10-04 11017`, then `2023-11-14T22:13:20.123Z`.

## Scheduler

`Scheduler` holds controls over the green-thread scheduler. It is a namespace with no fields.

| Method | Description |
|--------|-------------|
| `Scheduler.yield()` | Let the next runnable green thread run. The caller resumes behind everything that was already runnable. When nothing else is runnable it returns promptly, so a loop that yields is a busy wait that lets others progress. It uses no timer, unlike `sleep(0)`, and is safe in a program that never starts a green thread. |
| `Scheduler.processorCount()` | The number of processors the scheduler runs services on, as a `SchedulerProcessorCount` (`int(1 to u64.max)`, a `Count`, so it compares with a collection's `count()` with no cast): the machine's logical processor count, or the count `MAXON_MAX_PROCS` sets (1 to 2147483647, above the machine's count as well as below it). The count is resolved before `main` runs, so it is the same before the first `spawn` as after it. |

Both are refused on `wasm32-wasi` (E3104). Green threads, `async` and `await` are described under Concurrency in
[LANGUAGE_REFERENCE.md](/docs/language/overview/).

```maxon
function worker() returns ExitCode
	Scheduler.yield()
	print("worker\n")
	return 0
end 'worker'

function main() returns ExitCode
	let p = async worker()
	Scheduler.yield()
	_ = await p
	print("main\n")
	return 0
end 'main'
```

Output: `worker` then `main`.

## SharedValue

`SharedValue with T` is a live cell every thread can read: a reader takes an immutable snapshot lock-free,
and a writer publishes a whole new value. Use it through an alias:
`typealias ConfigCell = SharedValue with Config`. `T` is a `String`, a scalar, a service handle, or a type
whose every field is a `let` over such types ([E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable)). It is the run-time form of a registry key's
`default` ([`SharedValue`](/docs/language/async/#sharedvalue--a-live-value-made-at-run-time)).

| Member | Returns | Description |
|--------|---------|-------------|
| `SharedValue.create(value T)` | `SharedValue with T` | A cell holding `value`. A `clone()` of the cell is the same cell, so hand one to a service and it sees every publish. |
| `current()` | `T` | The value published last. A snapshot stays valid after later publishes. |
| `publish(value T)` | — | Make `value` current for every later `current()` on every thread. |
| `publish(value T, replacing T)` | `bool` | Publish only while `replacing` — a value `current()` returned — is still current. On `true` it is published; on `false` the current value stays and `value` is dropped. |

A read-modify-write re-reads `current()` and retries `publish(…, replacing:)` until it answers `true`, so
concurrent writers each land.

```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias Integer = int(i64.min to i64.max)
typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let seen = cell.current()
	cell.publish(Config.create(3))
	let stale = cell.publish(Config.create(2), replacing: seen)
	let latest = cell.current()
	let fresh = cell.publish(Config.create(5), replacing: latest)
	print("{seen.n} {stale} {fresh} {cell.current().n}\n")
	return 0
end 'main'
```

Output: `1 false true 5`.

## Random

`Random` draws integers from the operating system's cryptographic random source, on every target —
`wasm32-wasi` included, through `wasi:random`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Random.draw()` | `RandomDraw` | `RandomError` | A uniformly distributed integer from 0 to `u64.max` — all 64 bits random. |
| `Random.below(bound RandomBound)` | `RandomDraw` | `RandomError` | A uniformly distributed integer from 0 to `bound - 1`, for any bound up to `u64.max`. |

| Name | Definition |
|------|------------|
| `RandomDraw` | `int(0 to u64.max)` |
| `RandomBound` | `int(1 to u64.max)`, implementing `RandomDraw` |

`RandomError` has one case, `unavailable`: the operating system refused to supply random bytes.

```maxon
function main() returns ExitCode
	let roll = try Random.below(6) otherwise panic("no random source")
	print("{roll + 1}\n")
	return 0
end 'main'
```

It prints a number from 1 to 6.

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
| `Math.log10(x Real)` | `Real` | Base-10 logarithm. Exact at every power of ten a double represents exactly: `log10(10^k)` is `k` for `k` in 0 to 22. |
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
`sort()` and in generic code. Every alias of `int` or `float` conforms as its primitive does.

| Type | Conforms to | Notes |
|------|-------------|-------|
| `int` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `hash()` is the low 32 bits of the value. |
| `float` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `compare` is a total order: NaN equals NaN and sorts below every other value. `hash()` folds the 64-bit IEEE-754 pattern into 32 bits, `(bits xor (bits shr 32)) and 0xFFFFFFFF`, except that `-0.0` hashes as `0.0` does (`0`). |
| `bool` | `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `false` sorts before `true`. |

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
