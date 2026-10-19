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
| `Clock.osTickMs()` | `InstantMs` | How long the machine has been up, in milliseconds, from the operating system's tick count. It moves in host ticks, up to 16 ms apart on `x64-windows`. |
| `Clock.threadCpuTicks()` | `ThreadCpuTicks` | CPU time the calling thread has consumed. It advances only while the thread runs on a core, so a difference of two readings is that thread's cost alone, whatever else the machine runs. |

`since` is the first argument and has no default, so it is written without a label: `Clock.elapsedNanos(start)`.

The unit of `threadCpuTicks()` belongs to the platform: TSC cycles on Windows (`QueryThreadCycleTime`) and
nanoseconds elsewhere (`CLOCK_THREAD_CPUTIME_ID`). Compare two readings from one platform, or ratios of
differences. `ThreadCpuTicks` is `int(0 to u64.max)`. `osTickMs()` and `threadCpuTicks()` are refused on
`wasm32-wasi` (E3104).

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

Every reading and format is in UTC. `UnixSeconds` and `UnixNanos` are signed, negative
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
| `Scheduler.machineCpuCount()` | The machine's logical processor count, at least 1, whatever `MAXON_MAX_PROCS` sets: every active processor in every processor group on Windows, the processors this process may run on under Linux. A `SchedulerProcessorCount`. |
| `Scheduler.counters()` | The scheduler's event counters since the program started, as a `SchedulerCounters`, each read once. |
| `Scheduler.currentStackBytes()` | The calling green thread's current stack size in bytes, a `StackByteCount` (`int(0 to u64.max)`). A stack grows on demand and halves once the thread has been idle with most of it unused. |
| `Scheduler.quiesce()` | Park the caller until every machine is idle and nothing pending can run again, and answer how many other green threads are still owned — a service still running, or a promise neither awaited nor dropped — as a `GreenThreadCount` (`int(0 to u64.max)`): `0` when every one has finished, or the count left. A machine waiting for its pinned thread's next turn counts as idle, and a coroutine still unwinding after its promise was dropped keeps the wait open until it finishes. A coroutine whose promise has been dropped answers at once, with the count at that moment. The program carries on afterwards unchanged; services stay running. |
| `Scheduler.parallelBoundary()` | A compile-time marker with no run-time effect. A CPU-bound function calls it to mark a spawn of itself as deliberate, which satisfies [E3073](/docs/cli/error-codes/#e3073--asyncnonyielding) for a function that only computes. |
| `Scheduler.pinToMachine()` | Bind the calling green thread to the machine (OS thread) running it, and answer whether it did. A pinned thread that parks resumes on the same machine. |
| `Scheduler.unpinFromMachine()` | Release the calling green thread from its machine. |
| `Scheduler.currentMachine()` | The machine running the calling green thread, as a `MachineIndex` (`int(0 to u64.max)`); `0` is no machine. |

On `wasm32-wasi` `parallelBoundary()` compiles and every other member is refused (E3104). Green threads,
`async` and `await` are described under Concurrency in [LANGUAGE_REFERENCE.md](/docs/language/overview/).

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

### SchedulerCounters

Every field is a `SchedulerEventCount` (`int(0 to u64.max)`) summed over every processor. All but
`maxActiveWorkers` and `idleProcessors` are totals that only rise.

| Field | Counts |
|-------|--------|
| `steals` | Green threads a worker took from another processor's run queue. |
| `retakes` | Processors the system monitor took back from a machine stuck in a long kernel call. |
| `maxActiveWorkers` | The most worker machines ever active at once; at least 1. |
| `recycledThreads` | Green-thread records a spawn took from the free list, in place of a fresh one. |
| `timerStarts` | Machines the monitor started for a timer no running machine would fire in time. |
| `syscalls` | Kernel calls entered by a machine holding a processor, where the monitor could retake it. |
| `slices` | Time slices the scheduler started. |
| `preempts` | Preemption requests green threads honoured, after 10 ms on one processor. |
| `parkWakes` | Times a parked machine's wait returned, for any reason. An idle program keeps it near zero. |
| `netpollBlocks` | Times a green thread suspended on the network poller waiting for a socket. |
| `idleProcessors` | Processors idle with no machine right now. |
| `threadRecordsCarved` | Green-thread records and reply cells ever carved from the scheduler's arena. |
| `timerScanSteps` | Timer-heap entries inspected while cancelling deadlines. |
| `globalPushes` | Green threads ever pushed onto the global run queue. |

## HeapStats

`HeapStats` reads the allocator's own counters and levels. It is a namespace with no fields. `thread()`,
`process()`, `slab()` and `census()` answer heap records, and a record still live at a later `slab()` or
`census()` is a slot that reading counts. The scalar readings (`threadAllocations()` and its five siblings)
allocate nothing, so two of them bracket a stretch of code without counting the reads themselves.

| Method | Returns | Description |
|--------|---------|-------------|
| `HeapStats.allocationsMade()` | `HeapCount` | Tracked (managed-box) allocations since the process started. |
| `HeapStats.allocationsLive()` | `HeapCount` | Tracked allocations live now. |
| `HeapStats.bytesAllocated()` | `HeapBytes` | Bytes those tracked allocations handed out, cumulative. |
| `HeapStats.rawAllocationsMade()` | `HeapCount` | Raw (header-free) allocations since the process started: element buffers, string bytes, closure environments. |
| `HeapStats.rawAllocationsLive()` | `HeapCount` | Raw allocations live now. |
| `HeapStats.rawBytesAllocated()` | `HeapBytes` | Bytes those raw allocations handed out, cumulative. |
| `HeapStats.allocationsTagged(tag HeapBucketIndex)` | `HeapCount` | Tracked allocations whose allocation tag is `tag`, cumulative. Bucket 0 holds the untagged ones and 2047 every tag from 2047 up; the buckets sum to `allocationsMade()`. Tags exist in a `--debugstream` build; every other build answers 0. |
| `HeapStats.bytesTagged(tag HeapBucketIndex)` | `HeapBytes` | Bytes those tagged allocations asked for, cumulative. |
| `HeapStats.threadAllocations()`, `HeapStats.threadFrees()` | `HeapCount` | The calling green thread's allocations and frees, both layers together. |
| `HeapStats.threadBytes()` | `HeapBytes` | The bytes the calling green thread's allocations asked for, both layers together. |
| `HeapStats.processAllocations()`, `HeapStats.processFrees()` | `HeapCount` | Every thread's allocations and frees, both layers together. |
| `HeapStats.processBytes()` | `HeapBytes` | The bytes every thread's allocations asked for, both layers together, a box's header included. |
| `HeapStats.thread()` | `HeapActivity` | The calling green thread's allocations, frees and bytes, both layers together. |
| `HeapStats.process()` | `HeapActivity` | Every thread's allocations, frees and bytes, both layers together, a box's header included. Each figure only rises, so two readings bracket a phase while other threads allocate. |
| `HeapStats.slab()` | `SlabUsage` | The slab's levels now. |
| `HeapStats.census(mode HeapCensusMode)` | `SlabCensus` | Walk every live slot once and tally it into 2048 buckets. |
| `HeapStats.scavenge()` | `HeapBytes` | Return idle memory to the operating system, and answer the bytes handed back by this call. |

`HeapActivity` has three fields: `allocations` and `frees` (`HeapCount`) and `bytes` (`HeapBytes`). A
program with no green threads answers `thread()` from the process-wide totals.

`SlabUsage` holds bytes of slot, a size class's rounding included:

| Field | Type | Holds |
|-------|------|-------|
| `live` | `HeapBytes` | Slots handed out. |
| `free` | `HeapBytes` | Slots on some span's free list, a slot queued by a free on another processor included. `live + free` is every slot of every live span. |
| `cachedFree` | `HeapBytes` | The part of `free` a processor's cache can still reach; `free - cachedFree` is free memory stranded in spans a live slot keeps alive. |
| `parked` | `HeapBytes` | Whole spans holding no live slot: the backlog `scavenge()` reclaims. |
| `committed` | `HeapBytes` | Arena memory backed by physical pages, plus live direct OS mappings. |
| `remoteFrees` | `HeapCount` | Frees performed on a processor other than the one caching the slot's span, cumulative. |

`HeapCensusMode` chooses what a bucket of `census` holds: `byTag` (0), the slots whose allocation tag is the
bucket index (in a `--debugstream` build), or `bySizeClass` (1), the slots of that size class. In `byTag`
mode bucket 2046 holds slots with no plausible tag and 2047 tags past the table.

| `SlabCensus` member | Returns | Description |
|---------------------|---------|-------------|
| `mode` | `HeapCensusMode` | The mode it was taken in. |
| `tallyBytes` | `HeapBytes` | The live bytes the walk visited. |
| `liveBytes` | `HeapBytes` | The slab's `live` level read right after the walk, before the snapshot's own arrays exist; it equals `tallyBytes` when nothing else allocated in between. |
| `bucketCount(index HeapBucketIndex)` | `HeapCount` | Live slots in bucket `index`. |
| `bucketBytes(index HeapBucketIndex)` | `HeapBytes` | Their bytes. |

`scavenge()` destroys every span with no live slot, so its chunks serve any size class again, and hands the
operating system the physical pages of every 64 KiB granule that stayed wholly free across two calls. The
first call after a population is dropped answers 0 and the second answers the bytes; a granule reused in
between stays committed. On `wasm32-wasi` the chunks return to the arena and linear memory keeps its size.

`HeapCount` and `HeapBytes` are `int(0 to u64.max)`; `HeapBucketIndex` is `int(0 to 2047)`.

```maxon
typealias Payload = Array with Byte

function main() returns ExitCode
	let before = HeapStats.processAllocations()
	var kept = Payload.create()
	kept.push(1)
	let after = HeapStats.processAllocations()
	print("{after > before} {HeapStats.slab().live > 0} {kept.count()}\n")
	return 0
end 'main'
```

Output: `true true 1`.

## SharedValue

`SharedValue with T` is a live cell every thread can read: a reader takes an immutable snapshot lock-free,
and a writer publishes a whole new value. Use it through an alias:
`typealias ConfigCell = SharedValue with Config`. `T` is any type whose value graph reaches no OS handle,
function value or promise ([E3171](/docs/cli/error-codes/#e3171--registryvaluenotshareable)): records with `var`
fields, `Array` and `Map` publish as well as scalars, strings and service handles. It is the run-time form of
a registry key's `default` ([`SharedValue`](/docs/language/async/#sharedvalue--a-live-value-made-at-run-time)).

A value `current()` returns, like a registry `default`'s, is read-only all the way down: a write through it is
[E3159](/docs/cli/error-codes/#e3159--semanticwritethroughimmutablerecord) and binding it to a `var` is
[E3078](/docs/cli/error-codes/#e3078--semanticvarfromimmutable); `clone()` gives a mutable copy. `publish` takes a
value with no other owner ([E3174](/docs/cli/error-codes/#e3174--registrypublishedvaluenotsolelyowned)); publish a fresh value
or a `clone()`.

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

`Math` provides the elementary functions over `FullRangeFloat`, which is `float(f64.min to f64.max)`. The rounding and
`sqrt`/`abs`/`min`/`max` builtins are in [Core Functions](/docs/stdlib/#core-functions).

| Method | Returns | Description |
|--------|---------|-------------|
| `Math.sin(x FullRangeFloat)` | `FullRangeFloat` | Sine of radians. |
| `Math.cos(x FullRangeFloat)` | `FullRangeFloat` | Cosine of radians. |
| `Math.tan(x FullRangeFloat)` | `FullRangeFloat` | Tangent of radians. |
| `Math.atan(z FullRangeFloat)` | `FullRangeFloat` | Arc tangent. |
| `Math.atan2(y FullRangeFloat, x FullRangeFloat)` | `FullRangeFloat` | The angle of `(x, y)`, in `[-π, π]`. |
| `Math.exp(x FullRangeFloat)` | `FullRangeFloat` | e raised to `x`. |
| `Math.log(x FullRangeFloat)` | `FullRangeFloat` | Natural logarithm. |
| `Math.log2(x FullRangeFloat)` | `FullRangeFloat` | Base-2 logarithm. |
| `Math.log10(x FullRangeFloat)` | `FullRangeFloat` | Base-10 logarithm. Exact at every power of ten a double represents exactly: `log10(10^k)` is `k` for `k` in 0 to 22. |
| `Math.pow(base FullRangeFloat, exponent FullRangeFloat)` | `FullRangeFloat` | `base` raised to `exponent`, following IEEE 754's special cases (`pow(0.0, -1.0)` is infinity; `pow(x, 0)` and `pow(1, y)` are 1 even for NaN). |
| `Math.hasNegativeSignBit(z FullRangeFloat)` | `bool` | True when the sign bit is set, including `-0.0`, which no comparison can distinguish from `0.0`. |
| `Math.isNaN(z FullRangeFloat)` | `bool` | True for every NaN, whatever its bit pattern. |
| `Math.positiveInfinity()` | `FullRangeFloat` | +∞. |
| `Math.negativeInfinity()` | `FullRangeFloat` | −∞. |
| `Math.quietNaN()` | `FullRangeFloat` | A quiet NaN. |
| `Math.bitsOf(x FullRangeFloat)` | `FloatBits` | The 64-bit IEEE 754 pattern of `x`, as a signed word: `Math.bitsOf(1.0)` is `0x3FF0000000000000`, and every negative value, `-0.0` included, has a negative pattern. |
| `Math.fromBits(bits FloatBits)` | `FullRangeFloat` | The double whose IEEE 754 pattern is `bits`; the inverse of `bitsOf`. |

`FloatBits` is `int(i64.min to i64.max)`.

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
| `int` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `hash()` is `(self xor (wide * 0x9E3779B1)) and 0xFFFFFFFF`, where `wide = (self shr 32) - (self shr 63)`. `wide` is 0 from -2^32 up to 2^32 (exclusive), so there the hash is the low 32 bits of the value; outside it the high half reaches the hash too. |
| `float` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `compare` is a total order: NaN equals NaN and sorts below every other value. `hash()` folds the 64-bit IEEE-754 pattern into 32 bits, `(bits xor (bits shr 32)) and 0xFFFFFFFF`, except that `-0.0` hashes as `0.0` does (`0`). |
| `bool` | `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `false` sorts before `true`. |

| Method | Returns | Description |
|--------|---------|-------------|
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other Self)` | `bool` | Through `Equatable`. |
| `compare(other Self)` | `Ordering` | Through `Comparable`. |
| `toString()` | `String` | The same text as interpolation. |
| `clone()` | `Self` | The value itself. |
| `int.fromString(input String)` | `FullRangeInt` | Takes one optional leading `-` and decimal digits; throws `ParseError.invalidFormat` for anything else and for a value outside `i64.min` to `i64.max`. Likewise `float.fromString`, `bool.fromString`, `byte.fromString`. |

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
