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

They sum the two layers where the six are per-layer, because each is one green thread's share of a
process-wide total below, and `PhaseProbe` brackets a pool worker with these and a main-thread phase with
those — the two must be one unit. They count frees rather than a live figure, because a live count per green
thread would mean nothing — a thread does not own the boxes it allocated and may exit with them alive. A
program with no green threads answers all three from the process-wide totals below, which is not a fallback
but the same fact: single-threaded, *"what did this thread allocate"* and *"what did this process allocate"*
are one question.

⭐⭐ **AND THREE THAT ASK IT OF THE WHOLE PROCESS, AS TOTALS THAT ONLY RISE:**

| Intrinsic | Meaning |
|---|---|
| `__Builtins.processAllocTotal()` | cumulative allocations by every thread, BOTH layers |
| `__Builtins.processFreeTotal()` | cumulative frees by every thread, both layers |
| `__Builtins.processAllocBytes()` | cumulative bytes requested from the slab by every thread, a box's header included |

Each is ONE raw column summed over the allocator's rows, with no subtraction anywhere: the raw columns count
every slab request, boxes included, so each already covers both layers. Every word summed only rises, so a
reading never stands below an earlier one whatever other threads are doing — which is the one property a
bracket needs, because the difference of two readings is then never negative.

⚠ **THE SIX ABOVE CANNOT GIVE A BRACKET THAT PROPERTY.** Four of them are differences — a live figure is a
total less its frees, and a raw figure is the raw column less the tracked one, which a box steps at two
different instants. Read while another thread allocates, a sum or difference of them can stand below an
earlier reading. MEASURED while `PhaseProbe` bracketed a main-thread phase with the six, deriving its frees
as *(total − live)* over four such walks: a `spec-test --target=wasm32-wasi` worker died compiling
`register-allocator/int-six-vars-alive` with `Range check failed: value outside typealias 'AllocCount'` in
`PhaseProbe.elapsedInto` — the phase's closing reading stood below its opening one while other threads
allocated — while the same file passed 60/60 alone.

⚠ **THE BYTE TOTAL COUNTS A BOX's HEADER WITH ITS PAYLOAD**, which is what `threadAllocBytes()` counts too,
so the process-wide and per-thread byte figures measure one quantity. The header-free volume
(`mmAllocBytes() + mmRawAllocBytes()`) is not a total that only rises: a box credits its whole request to the
raw layer first and subtracts its header back out at the tracked step, so a reading between the two stands
above one taken after.

All twelve take no arguments and answer an `int`. `maxon-bin/Compiler/PhaseProbe.maxon` reads the three
process-wide totals for a main-thread phase and the three per-thread columns inside a pool worker — so
`scale-test`, and the rows it appends to `docs/optimization-log.md`, bottom out on those six. Its byte
column is therefore the slab's request volume, each box's header included, on both sources alike.

### Why the six carry a LIVE column

The per-layer six are (cumulative, live) in each layer plus the two byte volumes, because a live count is
what a question about leaks asks, and only the RAW live count can say whether a header-free buffer leaked.
In a program where one thread runs, `freed = Δtotal − Δlive` over any interval, and every figure is
bit-for-bit reproducible on the same input — which is what lets a suite gate on memory where it cannot gate
on wall time. A free path that walked a cumulative counter back would report a phase that allocated and
released a million boxes as having done nothing, so no free touches a total.

### The two LAYERS

The allocator has two layers because they answer different questions, and only the RAW live count
can say whether a header-free BUFFER leaked:

- **TRACKED.** An element buffer here is an ordinary `__mm_alloc` box with a header and a
  destructor, so this layer covers the header-carrying records (a struct, a String record, an
  array's outer handle) and the header-free buffers behind them alike.
- **RAW.** Beneath the boxes sits `__slab_alloc`, whose direct callers are the scheduler's tables (its
  machines, processors, lock and park stores), the service mailboxes and their message envelopes, the
  subprocess scratch buffers and the DebugStream ring. A green thread is not among them: its STACK comes
  from `osAllocPages`, and its RECORD from the scheduler's own record arena, which is carved from
  `osAllocPages` chunks — scaffolding like the stack, invisible to both layers. The slab's callers are
  genuinely header-free and no `__mm_alloc` counter can see them, so they are what the RAW columns report.

⇒ **A case that reads ONE layer is reading a runtime-internal fact; a case that reads the SUM is
reading the contract.** `bytes-scale-with-the-request` below is written on the sum for exactly this
reason, and it is not a stylistic choice: WHERE a payload lands is the allocator's private business,
and only the sum is stable across a change to it.

### The layers NEST in the allocator and are made DISJOINT at the reader

A caller asking about both layers SUMS them, and `__mm_alloc` is itself a `__slab_alloc` caller, so the
slab's RAW columns count every box as well as every scratch buffer. The public raw readers therefore answer
**raw − tracked**: `mmRawAllocTotal()` is the slab's request count less the box count, `mmRawAllocLive()`
the same for live, and `mmRawAllocBytes()` the slab's byte volume less the tracked payload volume less the
box header per box (`MmRuntime.buildMmCounterAccessors`). Counted naively — a reader that forgot the
subtraction — every box would be reported in both columns and the sum would read exactly double, which is
what `the-two-layers-are-disjoint` below catches. Its opposite,
`raw-total-is-never-below-tracked-total-after-boxes`, catches the two ways the subtraction goes too far:
answering NEGATIVE, and standing on a raw column nothing credits.

⭐ **The layer split is a runtime's private business; the SUM is the contract**, and while one thread runs it
is exactly what the process-wide totals answer (`process-totals-are-the-six-summed-while-one-thread-runs`).

### `mmRawAllocLive` and `mmRawAllocTotal` are TWO numbers

They read two columns. `mmRawAllocTotal` is cumulative and only ever rises; `mmRawAllocLive` rises with
each slab request and FALLS at `__slab_free`. A `__slab_alloc` caller that frees its region moves the
live column back: the subprocess runner's and the read probe's per-call scratch, and a service's mailbox
envelopes, each freed the moment its message is delivered. A green-thread record is never credited at
all: it comes from the scheduler's record arena, not the slab, and is recycled through the scheduler's
own free lists (`runtime-scratch-reclaim.md`'s `spawn-await-loop-is-bounded`). `__mm_alloc`'s boxes move
both raw columns and both tracked columns by the same amount, so the reader's subtraction leaves the
public raw figures untouched by them. `raw-live-falls-below-raw-total` below pins the two-number shape.

### They are maintained in EVERY heap program, per row

The columns live in the slab's state region, one row of six per mcache row
(`SlabRuntime.SlabTrafficColumn`). A P steps its own row with a plain add; every P-less thread and every
clamped P steps the shared raw row with an atomic add once a scheduler exists, and the lone main thread
steps it plainly before one does. No `.data` word carries a counter, so a heap program's `data {` table
does not change with what it reads, and no shared word is stepped by every allocation in the language. A
reader sums the rows, which is why the readers are cold and the writers are not.

### They are refused NOWHERE

Each sums one column over the rows of the allocator's own state region, which reaches no OS and which every
target the compiler emits can do, and the allocator whose state they read runs on all of them. That is the ACCEPTANCE half of the target pair whose refusal half is
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
lowering that summed the wrong column — or answered a constant — fails first.
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
**THE CASE THAT PROVES THE TWO LAYERS' SUM DOES NOT DOUBLE-COUNT.** the compiler's `__mm_alloc` obtains its
box from `__slab_alloc`, so the obvious implementation credits every allocation to BOTH columns and the
sum reads exactly double. Here a program allocates 512 array elements and nothing else:
the TRACKED column moves and the RAW column does not, because the raw reader subtracts the boxes the
slab counted on `__mm_alloc`'s behalf.

✅ **SABOTAGE-VERIFIED, and it is the only case in this file that catches it.** With the raw readers
answering the raw column without the tracked subtraction, this case goes RED (exit **2** against the
pinned 7 — the `tracked > 0` half holds and `raw == 0` does not) while `total-is-monotonic-and-moves`,
`live-returns-to-its-floor`, `bytes-scale-with-the-request` and `total-is-never-below-live` stay
GREEN. A suite without this case would report a compiler whose layer sum reads exactly double as
fully passing.
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

<!-- test: builtins-mm-counters.raw-total-is-never-below-tracked-total-after-boxes -->
**THE NESTING INVARIANT, READ ACROSS A WINDOW, WHERE A RAW COLUMN NOTHING CREDITS WOULD SHOW.** Every box
is one slab request, so `raw + boxes` is the slab's OWN request count whatever the reader's subtraction
does, and a window that boxes N times must move it by at least N. The two halves that carry the case fail
for opposite reasons and neither is implied by the other: a reader that subtracted the tracked column from
a raw one nothing credited answers NEGATIVE, and an allocator that stopped crediting the raw column on the
box path moves the sum by 0 while the box column moves by N. ⚠ A bare `raw + boxes >= boxes` would be
neither — for the signed `int` these intrinsics answer it is `raw >= 0` rewritten, so the two halves could
not disagree.

⭐ The window is what makes it a claim about THIS program rather than about the process: the readings
bracket the loop, so the pre-`main` scaffolding of a lane that has a scheduler cancels out of both deltas.
`the-two-layers-are-disjoint` pins the other edge, `raw == 0`, for a whole program.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let rawBefore = __Builtins.mmRawAllocTotal()
	let boxesBefore = __Builtins.mmAllocTotal()
	var buf = ByteArray.create()
	for _ in 0 upto 256 'push'
		buf.push(3)
	end 'push'
	let raw = __Builtins.mmRawAllocTotal()
	let boxes = __Builtins.mmAllocTotal()
	var score = 0
	if boxes > boxesBefore 'theWindowBoxedSomething'
		score = score + 1
	end 'theWindowBoxedSomething'
	if raw >= 0 'theSubtractionStaysPositive'
		score = score + 2
	end 'theSubtractionStaysPositive'
	if raw + boxes - rawBefore - boxesBefore >= boxes - boxesBefore 'everyBoxIsASlabRequest'
		score = score + 4
	end 'everyBoxIsASlabRequest'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.raw-columns-count-the-scheduler-scaffolding -->
**THE CASE THAT SAYS THE RAW COLUMNS ARE MAINTAINED AT ALL**, which is the hazard a column reading
a plausible `0` hides. `__gt_init` takes the scheduler's tables from `__slab_alloc` before `main` runs, and
`main` itself runs on a green thread, so by its first line the columns already count that scaffolding and
are not 0. The window then spawns a service and awaits one message: `__svc_spawn` takes the service's
MAILBOX (64 bytes) and `__mbox_send` takes the message's ENVELOPE (16 bytes), both straight from
`__slab_alloc` (`MailboxRuntime.maxon`) with no box header, so the raw columns rise and the tracked ones
do not. The third assertion separates the byte column
from the count column: every region the window takes is many bytes wide, so the two deltas cannot be equal
unless one of the two intrinsics is wired to the other's slot.

⚠ **THE WITNESS IS A SERVICE AND NOT AN `async` SPAWN**, because a green thread moves neither column: its
record comes from the scheduler's record arena and its stack from `osAllocPages` (see *The two LAYERS*
above). An `async` spawn and await in this window would read `after == before`.

✅ **SABOTAGE-VERIFIED.** With the raw columns' maintenance removed from `__slab_alloc`, this case exits
**2** against 8 — both delta halves go RED, and so does the first, because a column nothing maintains
reads 0 at `main`'s first line — and `raw-live-falls-below-raw-total` exits **6** against 7, while every
tracked-layer case stays GREEN.
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
	let before = __Builtins.mmRawAllocTotal()
	let bytesBefore = __Builtins.mmRawAllocBytes()
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0
	let after = __Builtins.mmRawAllocTotal()
	let bytesAfter = __Builtins.mmRawAllocBytes()
	var score = a
	if before > 0 'theSchedulerIsInstalledBeforeMain'
		score = score + 1
	end 'theSchedulerIsInstalledBeforeMain'
	if after > before 'theServiceAllocatedRaw'
		score = score + 2
	end 'theServiceAllocatedRaw'
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
`total`.** The caller is a service's MAILBOX. `__mbox_send` cuts one envelope per message from
`__slab_alloc`, and `__mbox_recv` hands it back through `__slab_free` the moment it pops the message,
before the handler runs — so once the reply has been awaited, the one envelope this program sent is
credited to `total` and debited from `live`. Every other raw region the program touches — the
scheduler's tables, the P and M structs, the mailbox itself — is still held when the counters are read,
so the envelope is the only debit, and a runtime whose free door did not debit the live column would read
`live == total`. Mailboxes run on every lane with green threads,
so the case needs no Windows-only scratch.

It is asserted only where there is something to compare — after the send, so the count is non-zero —
because `0 < 0` is false and `0 == 0` was true, and neither says anything about a runtime that
maintains no raw counter at all.

✅ **RED BEFORE GREEN, MEASURED.** With `__slab_free` not debiting the live column this program reads
`live == total` (30 and 30 at a 16-processor default) and answers **3**; with the debit it reads
`live == total - 1` at one, two and four processors and at the machine's count. ⚠ Under the sabotage that removes the raw columns' maintenance entirely, the `total > 0` half
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
**THE ACCEPTANCE HALF OF THE TARGET PAIR.** The six counters reach no OS — each sums a column of the
allocator's own state region — so they lower on every target the compiler emits and are refused nowhere. Pinned on the lane that REFUSES
`__Builtins.threadCpuTicks()` (`builtins-clock.md`'s `thread-cpu-ticks-rejected-on-wasm`), because
that refusal alone cannot tell a narrow gate from a compiler that has stopped serving the whole
instrumentation family there.

It is also the one case that names all six in one program, and so the only place `mmRawAllocBytes`
is exercised outside the scheduler cases. The three raw readers must answer exactly 0: every slab request
this program makes is a box, and the reader subtracts the tracked column — the byte column less the box
header per box — from the raw one.
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
summed over every row, so each answers for the whole PROCESS — exact while ONE thread allocates and
worthless the moment several do: a bracket opened inside a worker counts every other worker's traffic for
the whole of its span. MEASURED on
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

<!-- test: builtins-mm-counters.process-totals-are-the-six-summed-while-one-thread-runs -->
⭐⭐ **THE CASE THAT SAYS WHAT EACH PROCESS-WIDE TOTAL IS.** With one thread running, the six per-layer
figures are exact, so each total must equal the figure they sum to — to the digit. The allocation total is
both layers' count; the free total is that count less both layers' live counts; and the byte total exceeds
the header-free volume by exactly one header per box, which is asserted as a remainder because the header's
width is the allocator's own business (a traced build widens it). Each half fails a reader wired to the
wrong raw column, and the byte half fails one that answered the header-free volume.

It spawns nothing, so it runs on every lane — including wasm32-wasi, which makes it the acceptance half of
the target pair for these three as `all-six-run-on-wasm` is for the six.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BoxCount = int(1 to i64.max)

function fillAndDrop()
	var buf = ByteArray.create()
	for _ in 0 upto 64 'push'
		buf.push(3)
	end 'push'
end 'fillAndDrop'

function main() returns ExitCode
	fillAndDrop()
	let allocs = __Builtins.processAllocTotal()
	let frees = __Builtins.processFreeTotal()
	let bytes = __Builtins.processAllocBytes()
	let boxes = __Builtins.mmAllocTotal() as BoxCount
	let total = boxes + __Builtins.mmRawAllocTotal()
	let live = __Builtins.mmAllocLive() + __Builtins.mmRawAllocLive()
	let volume = __Builtins.mmAllocBytes() + __Builtins.mmRawAllocBytes()
	var score = 0
	if allocs > 0 and allocs == total 'allocsAreBothLayers'
		score = score + 1
	end 'allocsAreBothLayers'
	if frees > 0 and frees == total - live 'freesAreTotalLessLive'
		score = score + 2
	end 'freesAreTotalLessLive'
	if bytes > volume and (bytes - volume) mod boxes == 0 'bytesAddOneHeaderPerBox'
		score = score + 4
	end 'bytesAddOneHeaderPerBox'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: builtins-mm-counters.process-totals-agree-with-the-calling-thread-while-it-is-the-only-one -->
**THE PROCESS-WIDE TOTALS AND THE PER-THREAD COLUMNS ARE ONE QUANTITY**, so a reading of one sits beside
a reading of the other in one table. While `main` is the only thread allocating, all three
deltas must agree to the digit — the byte pair included, because both count a box's header with its payload.
A process total that answered the header-free volume fails the byte half; one wired to a tracked column
fails the count halves.

⚠ The spawn comes FIRST so `main` runs on a green thread with columns of its own; the service is never sent
a message until the window has closed, so it is parked on its mailbox throughout.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Idle
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function ping(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'ping'
end 'Idle'

function fillAndDrop()
	var buf = Bytes.create()
	for _ in 0 upto 4096 'push'
		buf.push(1)
	end 'push'
end 'fillAndDrop'

function main() returns ExitCode
	let h = spawn Idle.create()

	let threadAllocsBefore = __Builtins.threadAllocTotal()
	let threadFreesBefore = __Builtins.threadFreeTotal()
	let threadBytesBefore = __Builtins.threadAllocBytes()
	let allocsBefore = __Builtins.processAllocTotal()
	let freesBefore = __Builtins.processFreeTotal()
	let bytesBefore = __Builtins.processAllocBytes()
	fillAndDrop()
	let allocs = __Builtins.processAllocTotal() - allocsBefore
	let frees = __Builtins.processFreeTotal() - freesBefore
	let bytes = __Builtins.processAllocBytes() - bytesBefore
	let threadAllocs = __Builtins.threadAllocTotal() - threadAllocsBefore
	let threadFrees = __Builtins.threadFreeTotal() - threadFreesBefore
	let threadBytes = __Builtins.threadAllocBytes() - threadBytesBefore

	var score = try await h.ping(0) otherwise 0
	if allocs > 0 and allocs == threadAllocs 'allocsAgree'
		score = score + 2
	end 'allocsAgree'
	if frees > 0 and frees == threadFrees 'freesAgree'
		score = score + 4
	end 'freesAgree'
	if bytes > allocs and bytes == threadBytes 'bytesAgree'
		score = score + 8
	end 'bytesAgree'
	return score as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: builtins-mm-counters.process-totals-only-rise-while-another-thread-allocates -->
⭐⭐⭐ **THE PROPERTY A BRACKET RESTS ON: NO READING STANDS BELOW AN EARLIER ONE, WHATEVER ELSE IS RUNNING.**
`main` reads all three totals over and over while a service allocates and frees a hundred thousand arrays
on another thread, and counts every reading that came in below the one before it. The count must be 0, and
the free total must have moved by at least the service's frees, so the watched traffic really was counted.

⚠ **NO RUN CAN MAKE A REGRESSION HERE GO RED ON DEMAND.** A figure derived by subtracting two walks — the
shape of the MEASURED crash under *AND THREE THAT ASK IT OF THE WHOLE PROCESS* above — goes backwards
only when an allocation lands between its walks, which is timing. This case can catch that by chance and
can never pass it by design; the deterministic definitions are pinned by the two cases above.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Churn
	var rounds as Integer

	static function create() returns Self
		return Self{rounds: 0}
	end 'create'

	export function churn(n Integer) returns Integer
		for _ in 0 upto n 'round'
			var buf = Bytes.create()
			buf.push(3)
		end 'round'
		self.rounds = self.rounds + 1

		return n
	end 'churn'
end 'Churn'

function main() returns ExitCode
	let h = spawn Churn.create()
	let freesBefore = __Builtins.processFreeTotal()
	let reply = h.churn(100000)

	var lastAllocs = __Builtins.processAllocTotal()
	var lastFrees = __Builtins.processFreeTotal()
	var lastBytes = __Builtins.processAllocBytes()
	var backwards = 0

	for _ in 0 upto 20000 'watch'
		let allocs = __Builtins.processAllocTotal()
		let frees = __Builtins.processFreeTotal()
		let bytes = __Builtins.processAllocBytes()

		if allocs < lastAllocs or frees < lastFrees or bytes < lastBytes 'wentBackwards'
			backwards = backwards + 1
		end 'wentBackwards'

		lastAllocs = allocs
		lastFrees = frees
		lastBytes = bytes
	end 'watch'

	let rounds = try await reply otherwise 0
	var score = 0
	if backwards == 0 'onlyRose'
		score = score + 1
	end 'onlyRose'
	if rounds == 100000 and __Builtins.processFreeTotal() - freesBefore >= rounds 'theServiceWasCounted'
		score = score + 2
	end 'theServiceWasCounted'
	return score as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: builtins-mm-counters.alloc-total-arity-checked -->
Every one of the twelve takes no arguments. An intrinsic has no signature for the ordinary arity check
to read, so each is refused by the same `builtinArity` check `currentProcessId`/`cpuCount` use —
and each names ITSELF, which is what a copy-pasted dispatch arm carrying its neighbour's name would
fail. These cases are front-end only and target-neutral, so they carry no marker.
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

<!-- test: builtins-mm-counters.thread-alloc-total-arity-checked -->
The calling thread's allocation column, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.threadAllocTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.threadAllocTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.thread-free-total-arity-checked -->
The calling thread's free column, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.threadFreeTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.threadFreeTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.thread-alloc-bytes-arity-checked -->
The calling thread's byte column, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.threadAllocBytes(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.threadAllocBytes' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.process-alloc-total-arity-checked -->
The process-wide allocation total, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.processAllocTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.processAllocTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.process-free-total-arity-checked -->
The process-wide free total, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.processFreeTotal(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.processFreeTotal' takes exactly 0 argument, but 1 were given
```

<!-- test: builtins-mm-counters.process-alloc-bytes-arity-checked -->
The process-wide byte total, refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.processAllocBytes(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.processAllocBytes' takes exactly 0 argument, but 1 were given
```
