---
feature: builtins-mm-counters
status: stable
keywords: [builtins, __Builtins, memory, allocator, counters, instrumentation, scale-test, PhaseProbe]
category: system
---

# The `__Builtins.mm*()` memory-traffic counters

## Documentation

`__Builtins` is the compiler's builtin TYPE, whose static methods are INTRINSICS rather than
functions any file declares. This spec pins the six that read the ALLOCATOR's own counters:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.mmAllocTotal()` | cumulative count of TRACKED allocations since process start |
| `__Builtins.mmAllocLive()` | TRACKED allocations currently live |
| `__Builtins.mmAllocBytes()` | cumulative USER bytes handed out by TRACKED allocations |
| `__Builtins.mmRawAllocTotal()` | cumulative count of RAW (header-free) allocations |
| `__Builtins.mmRawAllocLive()` | RAW allocations currently live |
| `__Builtins.mmRawAllocBytes()` | cumulative bytes handed out by RAW allocations |

⭐ **AND THREE MORE THAT ASK THE SAME QUESTION OF ONE GREEN THREAD**, because the six above are
process-wide and a POOL cannot be measured with a process-wide column — a bracket opened inside a worker
counts every other worker's traffic:

| Intrinsic | Meaning |
|---|---|
| `__Builtins.threadAllocTotal()` | cumulative allocations by the CALLING green thread, BOTH layers |
| `__Builtins.threadFreeTotal()` | cumulative frees by the calling green thread, both layers |
| `__Builtins.threadAllocBytes()` | cumulative bytes handed to the calling green thread, both layers |

They sum the two layers where the six are per-layer, because `PhaseProbe` adds them before it reports any
figure. `frees` is COUNTED here and DERIVED there: `Δtotal − Δlive` needs a LIVE column, and a live count per
green thread would mean nothing — a thread does not own the boxes it allocated and may exit with them alive.
A program with no green threads answers all three from the process-wide words, which is not a fallback but
the same fact: single-threaded, *"what did this thread allocate"* and *"what did this process allocate"* are
one question.

All six take no arguments and answer an `int`. Their caller in this tree is
`maxon-bin/Compiler/PhaseProbe.maxon`, which sums the two layers into `totalAllocs()`,
`liveAllocs()` and `totalAllocBytes()` — so `scale-test`, and every row of
`docs/optimization-log.md`, bottoms out here.

### Why SIX: `frees` is DERIVED, not counted

`freed = Δtotal − Δlive` over any interval, which needs a cumulative AND a live counter IN EACH
LAYER. That is four of the six; the remaining two are the byte volumes, which have no live form —
a free path that walked a cumulative counter back would report a phase that allocated and released
a million boxes as having done nothing.

The four cumulative counters are therefore MONOTONIC by construction — the two LIVE columns are the
exception, and being non-monotonic is what they are for — so they delta exactly across a phase
boundary and are bit-for-bit reproducible on the same input. That is what lets a suite gate on
memory where it cannot gate on wall time.

### The two LAYERS

The allocator has two layers because they answer different questions, and only the RAW live count
can say whether a header-free BUFFER leaked:

- **TRACKED.** An element buffer here is an ordinary `__mm_alloc` box with a header and a
  destructor, so this layer covers the header-carrying records (a struct, a String record, an
  array's outer handle) and the header-free buffers behind them alike.
- **RAW.** Beneath the boxes sits `__slab_alloc`, whose direct callers are the green-thread structs
  and tables, the service mailboxes and their message envelopes, the subprocess scratch buffers and the
  DebugStream ring. A green thread's STACK is not among them: it comes from `osAllocPages`. Those are genuinely
  header-free and no `__mm_alloc` counter can see them, so they are what the RAW columns report.

⇒ **A case that reads ONE layer is reading a runtime-internal fact; a case that reads the SUM is
reading the contract.** `bytes-scale-with-the-request` below is written on the sum for exactly this
reason, and it is not a stylistic choice: WHERE a payload lands is the allocator's private business,
and only the sum is stable across a change to it.

### The layers must be DISJOINT, and in the compiler that took work

`PhaseProbe` SUMS them, and in the compiler `__mm_alloc` is itself a `__slab_alloc` caller. Counted
naively, every box would be credited to both columns and `totalAllocs()` would read exactly double.
`MmRuntime.buildSlabAlloc` therefore emits an UNCOUNTED twin (`__slab_alloc_box`) for `__mm_alloc`
to call in a build that reads the counters. `the-two-layers-are-disjoint` below is what says so.

⭐ **The layer split is a runtime's private business; the SUM is the number `PhaseProbe` reads.**

### `mmRawAllocLive` and `mmRawAllocTotal` are TWO numbers

They read two `.data` words. `mmRawAllocTotal` is cumulative and only ever rises; `mmRawAllocLive`
rises with each credited allocation and FALLS when a counted `__slab_alloc` caller hands its region
back through the counted free door, `__mm_raw_free`. Several callers do: the subprocess runner's and
the read probe's per-call scratch, and a service's mailbox envelopes, each freed the moment its
message is delivered. A GT struct is credited once and never handed back — the scheduler recycles it
through its own free lists (`runtime-scratch-reclaim.md`'s `spawn-await-loop-is-bounded`).
`__mm_alloc`'s boxes move neither column: they come from the UNCOUNTED twin `__slab_alloc_box` and
are reported by the tracked layer instead, because the two layers must stay disjoint (see *The
layers must be DISJOINT* above). `raw-live-falls-below-raw-total` below pins the two-number shape.

### They are maintained only in a build that READS them

Their maintenance is not a `.data` word — it is loads, adds and stores on the path EVERY allocation
in the language takes. The bootstrap pays that price unconditionally and had to shard the counters
per-P to afford it (a shared locked add measured +3%, and a first cut that also touched the free
path +9%). The compiler gates it on `RuntimeUsage.usesMmCounters` instead, so a program that never asks how
much it allocated carries no counter word, no `.data` slot and not one extra instruction on the
allocation path. That is the same rule `--debugstream`'s box prefix follows: an instrument may not
tax a program that is not being measured.

MEASURED against a control built from the parent commit — an ordinary heap program that reads no
counter: the SAME 13,163-byte image, an identical `.data` image, an identical import table, and 35
differing bytes, all of them one `mov eax, 1` moved ahead of a `lea` inside `__mm_alloc` and
`__mm_free`. Those are the rung's DE-DUPLICATION of the counter-update sequence rather than the
counters, they are behaviour-identical, and no golden renders either body.

### They are refused NOWHERE

Each lowers to a `.data` load, which every target the compiler emits can do, and the allocator whose state
they read runs on all of them. That is the ACCEPTANCE half of the target pair whose refusal half is
`builtins-clock.md`'s `thread-cpu-ticks-rejected-on-wasm`: without a case proving these six run on
wasm, that refusal would pass just as happily against a compiler that had stopped serving the whole
instrumentation family there.

### The counters are LIVE, so the cases assert PROPERTIES

There is no literal to compare against — every number depends on what the program and the runtime
have done — so the cases below assert what cannot vary: monotonicity, an invariant between two
columns, a DELTA across a known allocation, and a return to a floor across a scope exit.

## Tests

<!-- test: builtins-mm-counters.total-is-monotonic-and-moves -->
The cumulative counter never decreases, and a real allocation moves it. The second half is what a
lowering that read the wrong `.data` word — or answered a constant — fails first.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let before = __Builtins.mmAllocTotal()
	var buf = ByteArray.create()
	buf.push(1)
	let after = __Builtins.mmAllocTotal()
	var score = 0
	if after >= before 'monotonic'
		score = score + 1
	end 'monotonic'
	if after > before 'moved'
		score = score + 2
	end 'moved'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-mm-counters.live-returns-to-its-floor -->
**THE CASE THAT SEPARATES THE TWO TRACKED COLUMNS.** An array built and dropped inside a called
function leaves the LIVE count exactly where it was, while the CUMULATIVE count is strictly higher.
It is the leak gate's own invariant read at a finer granularity, and it fails three different ways:
a live counter the free path never decrements, a cumulative counter the free path DOES decrement,
and either intrinsic wired to the other's slot.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function fill()
	var buf = ByteArray.create()
	for _ in 0 upto 64 'push'
		buf.push(3)
	end 'push'
end 'fill'

function main() returns ExitCode
	let liveBefore = __Builtins.mmAllocLive()
	let totalBefore = __Builtins.mmAllocTotal()
	fill()
	let liveAfter = __Builtins.mmAllocLive()
	let totalAfter = __Builtins.mmAllocTotal()
	var score = 0
	if liveAfter == liveBefore 'liveCameBack'
		score = score + 3
	end 'liveCameBack'
	if totalAfter > totalBefore 'totalDidNotComeBack'
		score = score + 4
	end 'totalDidNotComeBack'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.bytes-scale-with-the-request -->
**THE CASE THAT SAYS A BYTE COLUMN IS BYTES AND NOT A COUNT**, and it is written on the SUM of the
two layers because that is the contract — see *The two LAYERS* above. Filling 1024 elements moves
the volume by
more than filling 16 does, and by at least the payload asked for; a counter that ticked once per
allocation would move by roughly the same small number both times.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias PushCount = int(0 to 65536)

function volume() returns BytePos
	return (__Builtins.mmAllocBytes() + __Builtins.mmRawAllocBytes()) as BytePos
end 'volume'

function fill(n PushCount)
	var buf = ByteArray.create()
	for _ in 0 upto n 'push'
		buf.push(3)
	end 'push'
end 'fill'

function main() returns ExitCode
	let before = volume()
	fill(16)
	let afterSmall = volume()
	fill(1024)
	let afterLarge = volume()
	let small = afterSmall - before
	let large = afterLarge - afterSmall
	var score = 0
	if large > small 'bytesScaleWithTheRequest'
		score = score + 2
	end 'bytesScaleWithTheRequest'
	if large >= 1024 'atLeastThePayloadAsked'
		score = score + 3
	end 'atLeastThePayloadAsked'
	return score as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: builtins-mm-counters.total-is-never-below-live -->
The invariant that must hold at every observation point in both layers: an allocation must be
counted before it can be live. It is what a cumulative counter that had been wired to the free path
— or a live counter that double-counted — breaks.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var buf = ByteArray.create()
	buf.push(1)
	let total = __Builtins.mmAllocTotal()
	let live = __Builtins.mmAllocLive()
	let rawTotal = __Builtins.mmRawAllocTotal()
	let rawLive = __Builtins.mmRawAllocLive()
	var score = 0
	if total >= live 'trackedInvariant'
		score = score + 1
	end 'trackedInvariant'
	if rawTotal >= rawLive 'rawInvariant'
		score = score + 2
	end 'rawInvariant'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-mm-counters.the-two-layers-are-disjoint -->
**THE CASE THAT PROVES `PhaseProbe`'s SUM DOES NOT DOUBLE-COUNT.** the compiler's `__mm_alloc` obtains its
box from `__slab_alloc`, so the obvious implementation credits every allocation to BOTH columns and
`totalAllocs()` reads exactly double. Here a program allocates 512 array elements and nothing else:
the TRACKED column moves and the RAW column does not, because `__mm_alloc` goes through the
uncounted `__slab_alloc_box` twin.

✅ **SABOTAGE-VERIFIED, and it is the only case in this file that catches it.** With `__mm_alloc`
pointed back at the COUNTED `__slab_alloc`, this case went RED (exit **2** against the pinned 7 —
the `tracked > 0` half held and `raw == 0` did not) while all twelve other cases in this file stayed
GREEN, `total-is-monotonic-and-moves`, `live-returns-to-its-floor`, `bytes-scale-with-the-request`
and `total-is-never-below-live` among them. A suite without this case would have reported a
compiler whose `totalAllocs()` reads exactly double as fully passing.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var buf = ByteArray.create()
	for _ in 0 upto 512 'push'
		buf.push(3)
	end 'push'
	let tracked = __Builtins.mmAllocTotal()
	let raw = __Builtins.mmRawAllocTotal()
	var score = 0
	if tracked > 0 'trackedMoved'
		score = score + 2
	end 'trackedMoved'
	if raw == 0 'rawDidNot'
		score = score + 5
	end 'rawDidNot'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.raw-columns-count-the-scheduler-scaffolding -->
**THE CASE THAT SAYS THE RAW COLUMNS ARE MAINTAINED AT ALL**, which is the hazard a column reading
a plausible `0` hides. the compiler's header-free layer is reached by the green-thread runtime: `__gt_init`
and `__gt_spawn` take the scheduler's tables and GT structs from `__slab_alloc`. `main` itself runs on a
green thread, so by its first line the columns already count that scaffolding and are not 0; the `async`
spawn then takes another GT struct, so they rise. The third assertion separates the byte column from the
count column: a GT struct alone is hundreds of BYTES, so the two deltas cannot be equal unless one of the
two intrinsics is wired to the other's slot.

✅ **SABOTAGE-VERIFIED.** With the raw columns' maintenance removed from `__slab_alloc`, both delta
halves of this case go RED, and so does `raw-live-falls-below-raw-total` (exit **6** against 7), while
every tracked-layer case stays GREEN. The first half fails under that sabotage too: a column nothing
maintains reads 0 at `main`'s first line.
```maxon
function work(n ExitCode) returns ExitCode
	__Builtins.parallelBoundary()
	return n + 1
end 'work'

function main() returns ExitCode
	let before = __Builtins.mmRawAllocTotal()
	let bytesBefore = __Builtins.mmRawAllocBytes()
	let p = async work(1)
	let a = await p
	let after = __Builtins.mmRawAllocTotal()
	let bytesAfter = __Builtins.mmRawAllocBytes()
	var score = a
	if before > 0 'theSchedulerIsInstalledBeforeMain'
		score = score + 1
	end 'theSchedulerIsInstalledBeforeMain'
	if after > before 'theSchedulerAllocatedRaw'
		score = score + 2
	end 'theSchedulerAllocatedRaw'
	if bytesAfter - bytesBefore > after - before 'bytesAreBytesNotACount'
		score = score + 3
	end 'bytesAreBytesNotACount'
	return score as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: builtins-mm-counters.raw-live-falls-below-raw-total -->
**THE CASE THAT PINS THE RAW COLUMNS AS TWO NUMBERS: A COUNTED CALLER FREES, SO `live` FALLS BELOW
`total`.** The caller is a service's MAILBOX. `__mbox_send` cuts one envelope per message from the
counted `__slab_alloc`, and `__mbox_recv` hands it back through `__mm_raw_free` the moment it pops the
message, before the handler runs — so once the reply has been awaited, the one envelope this program
sent is credited to `total` and debited from `live`. Every other raw region the program touches — the
scheduler's tables, the P and M structs, the GT structs, the mailbox itself — is still held when the
counters are read, so the envelope is the only debit, and a runtime whose envelope did not come back
through the counted door would read `live == total`. Mailboxes run on every lane with green threads,
so the case needs no Windows-only scratch.

It is asserted only where there is something to compare — after the send, so the count is non-zero —
because `0 < 0` is false and `0 == 0` was true, and neither says anything about a runtime that
maintains no raw counter at all.

✅ **RED BEFORE GREEN, MEASURED.** With the envelope freed through the plain `__slab_free` — a door the
raw columns never see — this program reads `live == total` (30 and 30 at a 16-processor default)
and answers **3**. Through
`__mm_raw_free` it reads `live == total - 1` at one, two and four processors and at the machine's
count. ⚠ Under the sabotage that removes the raw columns' maintenance entirely, the `total > 0` half
fails instead, so the two halves fail for opposite reasons and neither can carry the case alone.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0
	let total = __Builtins.mmRawAllocTotal()
	let live = __Builtins.mmRawAllocLive()
	var score = a
	if total > 0 'somethingToCompare'
		score = score + 1
	end 'somethingToCompare'
	if live < total 'theDeliveredEnvelopeWentBack'
		score = score + 4
	end 'theDeliveredEnvelopeWentBack'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.all-six-run-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
**THE ACCEPTANCE HALF OF THE TARGET PAIR.** The six counters reach no OS — each is a `.data` load —
so they lower on every target the compiler emits and are refused nowhere. Pinned on the lane that REFUSES
`__Builtins.threadCpuTicks()` (`builtins-clock.md`'s `thread-cpu-ticks-rejected-on-wasm`), because
that refusal alone cannot tell a narrow gate from a compiler that has stopped serving the whole
instrumentation family there.

It is also the one case that names all six in one program, and so the only place `mmRawAllocBytes`
is exercised outside the x64-windows scheduler cases.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var buf = ByteArray.create()
	buf.push(1)
	var score = 0
	if __Builtins.mmAllocTotal() > 0 'total'
		score = score + 1
	end 'total'
	if __Builtins.mmAllocLive() > 0 'live'
		score = score + 1
	end 'live'
	if __Builtins.mmAllocBytes() > 0 'bytes'
		score = score + 1
	end 'bytes'
	if __Builtins.mmRawAllocTotal() == 0 'rawTotal'
		score = score + 1
	end 'rawTotal'
	if __Builtins.mmRawAllocLive() == 0 'rawLive'
		score = score + 1
	end 'rawLive'
	if __Builtins.mmRawAllocBytes() == 0 'rawBytes'
		score = score + 1
	end 'rawBytes'
	return score as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: builtins-mm-counters.a-thread-is-billed-only-its-own-allocations -->
⭐⭐⭐ **THE CASE THE POOL NEEDS, AND THE ONE THE SIX ABOVE STRUCTURALLY CANNOT BE.** Every column above is
a process-wide `.data` word, which is exact while ONE thread allocates and worthless the moment several do:
a bracket opened inside a worker counts every other worker's traffic for the whole of its span. MEASURED on
a stage-2 self-compile, `regalloc:splitting` reported **1,207,232,853** allocations at sixteen processors
against **77,890,562** at one — the identical compile, and the sub-phase rows went into `--metrics`,
`--log=compiler:debug`, `scale-test` and `docs/optimization-log.md` saying so.

The three per-thread columns answer the same question of the CALLING GREEN THREAD. Both halves below are
necessary and neither alone is enough:

* **SERIAL, they must AGREE TO THE DIGIT.** A per-thread column that merely moved could be anything; what
  makes it a memory figure is that it equals the process's own when the process is one thread. This is also
  what keeps a main-thread phase row and the run total commensurable.
* **CONCURRENT, they must DIVERGE.** The service allocates tens of thousands of times inside an interval in
  which `main` is parked on `await` and allocates almost nothing. A per-thread column that was secretly the
  process-wide one passes the first half and fails here.

⚠ The spawn comes FIRST so both readings are taken with a scheduler already installed — it comes up lazily,
and before it there is no green thread for a per-thread column to be about.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Tally = int(0 to u64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Churn
	var rounds as Integer

	static function create() returns Self
		return Self{rounds: 0}
	end 'create'

	export function churn(n Integer) returns Integer
		var buf = Bytes.create()
		for _ in 0 upto n 'push'
			buf.push(3)
		end 'push'
		self.rounds = self.rounds + 1
		return buf.count() as Integer
	end 'churn'
end 'Churn'

function processWide() returns Tally
	return (__Builtins.mmAllocTotal() + __Builtins.mmRawAllocTotal()) as Tally
end 'processWide'

function main() returns ExitCode
	let h = spawn Churn.create()

	let serialThreadBefore = __Builtins.threadAllocTotal()
	let serialProcessBefore = processWide()
	var warm = Bytes.create()
	for _ in 0 upto 4096 'warm'
		warm.push(1)
	end 'warm'
	let serialThread = __Builtins.threadAllocTotal() - serialThreadBefore
	let serialProcess = processWide() - serialProcessBefore

	let busyThreadBefore = __Builtins.threadAllocTotal()
	let busyProcessBefore = processWide()
	let reply = h.churn(60000)
	_ = try await reply otherwise 0
	let busyThread = __Builtins.threadAllocTotal() - busyThreadBefore
	let busyProcess = processWide() - busyProcessBefore

	var score = 0
	if serialThread > 0 and serialThread == serialProcess 'serialSourcesAgree'
		score = score + 1
	end 'serialSourcesAgree'
	if busyProcess > busyThread 'theServiceIsNotBilledToMain'
		score = score + 2
	end 'theServiceIsNotBilledToMain'
	if __Builtins.threadFreeTotal() > 0 and __Builtins.threadAllocBytes() > 0 'theOtherTwoColumnsMove'
		score = score + 4
	end 'theOtherTwoColumnsMove'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.alloc-total-arity-checked -->
Every one of the six takes no arguments. An intrinsic has no signature for the ordinary arity check
to read, so each is refused by the same `builtinArity` check `currentProcessId`/`cpuCount` use —
and each names ITSELF, which is what a copy-pasted dispatch arm carrying its neighbour's name would
fail. These six cases are front-end only and target-neutral, so they carry no marker.
```maxon
function main() returns ExitCode
	return __Builtins.mmAllocTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmAllocTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.alloc-live-arity-checked -->
The live tracked counter, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmAllocLive(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmAllocLive' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.alloc-bytes-arity-checked -->
The tracked byte volume, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmAllocBytes(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmAllocBytes' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.raw-alloc-total-arity-checked -->
The cumulative raw counter, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmRawAllocTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmRawAllocTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.raw-alloc-live-arity-checked -->
The live raw counter, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmRawAllocLive(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmRawAllocLive' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.raw-alloc-bytes-arity-checked -->
The raw byte volume, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmRawAllocBytes(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmRawAllocBytes' takes exactly 0 argument, but 1 were given
```
