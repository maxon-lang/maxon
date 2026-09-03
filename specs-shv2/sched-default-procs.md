---
feature: sched-default-procs
status: experimental
keywords: [scheduler, green-threads, MAXON_MAX_PROCS, processors, procs, multi-M, work-stealing, spawn, services, determinism]
category: system
---

# The default processor count, and the answer that must not depend on it

## Documentation

⚖ **`DefaultMaxProcs` HAS BECOME `osCpuCount`, AND THE CONSTANT IS GONE.** Until this rung a Maxon program
that set no `MAXON_MAX_PROCS` ran on **one** P and therefore one worker M, and every multi-processor
property the scheduler has — the ring's head CAS under contention, the Dekker fence on its publish, the four
stealing rounds, the handoff of a P off a blocked M — was reachable only by a driver script that set the
variable by hand (`maxon-shv2/track0/pin-matrix.sh`). ⇒ **the code was written, built and shipped, and the
default build never executed it.** Flipping the default is what puts it under the program that runs.

⭐ **THE DEFAULT IS NO LONGER A CONSTANT, WHICH IS WHY IT COULD NOT BE A CONSTANT SUBSTITUTION.** A
processor count is a fact about the machine, so `emitResolveMaxProcs` reads `StdOp.osCpuCount` at scheduler
bring-up — floored at `CpuParallelRuntime.MinimumProcessorCount`, since the OS read can answer 0 or -1 — and
that ONE reading is both the default and the ceiling. Two readings could have disagreed; one cannot.

### `MAXON_MAX_PROCS` is GOMAXPROCS now — it LOWERS as well as raises

The variable sets the count **exactly**, clamped to `[MinimumProcessorCount, osCpuCount]`. It used to be
able only to RAISE, and the reason was the default it was raising from: with a default of 1 a variable that
could only lower would have had nothing to lower to. That argument died with the constant, and the rule is
now the bootstrap's own (`X86CodeEmitter.Runtime.cs`'s `EmitReadMaxProcsEnvOverride`), which matters because
one harness drives both compilers through this one variable.

**A value that is not a processor count never errors**, and the ways of not being one land on three tests,
MEASURED on this tree (16-processor host):

| `MAXON_MAX_PROCS` | `schedProcessorCount()` | why |
|---|---|---|
| unset | 16 | `osEnvRead` answers 0 characters |
| `1` | 1 | in band, taken exactly — the LOWERING the flip bought |
| `2` | 2 | in band, taken exactly |
| `16` | 16 | in band, and the band's top |
| `999` | 16 | above the machine ⇒ the ceiling |
| `0` | 16 | below the floor ⇒ not a count, so: the default |
| `abc` | 16 | the digit walk answers 0, which is the same predicate as `0` |
| `12abc` | 12 | the walk stops at the first non-digit, so a suffix is ignored rather than rejected |
| 39 digits | 16 | longer than the 32-byte buffer ⇒ declined, not retried |
| `18446744073709551621` | **5** | ⚠ the decimal walk WRAPS in i64, so a 20-digit value can land in band |

⚠ **THE LAST ROW IS WHY THIS SECTION NO LONGER CLAIMS "resolves to the machine's count".** A value that
overflows i64 wraps to whatever the low 64 bits say, and if that happens to be in `[1, cpuCount]` it is
taken *exactly* — `18446744073709551621` is `2^64 + 5`, so it reads as 5. Nothing is unsafe: the result is
still clamped into the band, so no `P*` array is mis-sized and nothing wild-writes. But it is not the
machine's count, and the earlier wording promised it was. **This is pre-flip behaviour, unchanged** — the
old code clamped the same 5 to `min(5, 16)` — so it is a documentation correction, not a regression.
Rejecting an overflowing value would need the walk to detect the wrap, which is a real change to
`emitParseUnsignedDecimal` and is not this one.

### `<!-- procs: N -->` — the marker that makes the count a property of the CASE

⛔ **`specs-shv2/sched-runqueue.md` states the restriction this marker lifts, in as many words:** *"A SPEC
CASE CANNOT SET `MAXON_MAX_PROCS`, SO NOTHING BELOW EXERCISES TWO PROCESSORS — the harness gives a case
`Args:` and no environment."* That is why the whole green-thread substrate was gated by a shell script
rather than by the suite, and why a scheduler bug reachable only at N ≥ 2 could sit behind a wholly green
`spec-test` run.

`<!-- procs: N -->` sets `MAXON_MAX_PROCS=N` in the environment the harness gives that one case, beside
`<!-- targets: … -->` and `<!-- Args: … -->` on the same kind of comment line
(`maxon-shv2/Testing/SpecParser.maxon`). A case that carries none gets the process default.

⭐ **THE CASE THAT ASSERTS THE DEFAULT ITSELF — `the-default-is-every-processor` — IS THE FIRST ONE BELOW,
AND IT ARRIVED WITH THE FLIP EXACTLY AS THIS PARAGRAPH SAID IT WOULD.** Its whole subject is the ABSENCE of
a marker: it sets no count and asserts that the scheduler resolved the machine's own. It could not have
landed a commit earlier — it would have read 1 against the host's count — which is why it was named here
before it existed rather than written and shelved.

⚠⚠ **A MARKER WITH NO ARM IN `parseTestBlocks` IS SILENTLY IGNORED, AND THIS HAS ALREADY HAPPENED HERE.**
The marker loop (`SpecParser.maxon:1209-1270`) asks one `openingsCarry` question per known family and a
line matching none of them falls through to `i = i + 1` — the file's own comment on the `targets:` arm
says what that costs: *"without this it would fall through to `i = i + 1` and be silently ignored (which
is exactly how `safety.md`'s four markers went unread until this rung)"*. ⇒ a `procs:` marker written
before its arm exists reads as **prose**: the case runs at the default and passes or fails for a reason
that has nothing to do with the number it names.

⇒ **`the-procs-marker-pins-one-processor` IS THE GATE FOR THAT, AND THE FLIP IS WHAT ARMED IT.** Before the
flip its `procs: 1` and the default agreed, so it could not see whether the override happened at all; now
the default is the machine's count and its `procs=1` is reachable ONLY through the marker. **MEASURED: the
same program with the marker removed prints `procs=16` on this host.** A `procs:` marker that is parsed but
dropped, or misspelled into `SpecParser`'s silently-ignored bucket, turns it red.

**`the-procs-marker-raises-the-processor-count` is the same gate pointing the other way**, and it is last
below. It names a count ABOVE one and asserts the scheduler RESOLVED it, so an unread marker on the
PRE-flip default left it at one P and its `clamped=` reading went to 0 — MEASURED then, by running the very
same program with no marker at all: `procs=1 cpus=16 clamped=0`, against `procs=4 clamped=1` with the
marker. ⚠ **After the flip that particular reading no longer reproduces on a host with more than four
processors** — an unread marker now leaves the case at the machine's count, and `clamped=0` because 16 is
not the `min(4, 16)` it expects. The case still goes red on an ignored marker, which is what it is for; what
changed is the number it prints when it does.

### ⛔⛔ THESE CASES ASSERT THE PROCESSOR COUNT, AND THEY USED TO ASSERT THE WORKER COUNT — WHICH READ EITHER WAY DEPENDING ON MACHINE LOAD

Two of the three below asserted `multi=1`, where `multi` was `1` iff
`__Builtins.schedMaxActiveWorkers() > 1` — the scheduler's own high-water mark of concurrently-active
worker Ms. **MEASURED 2026-09-01: `the-procs-marker-raises-the-processor-count` PASSED under
`--filter=sched-default-procs` and FAILED in the full suite**, same binary, same box, minutes apart, with
nothing wrong with the scheduler. Under a full run — 12 spec workers competing for 16 cores — a short
spawn-driven program can drain its own ring and finish before `__sched_wake_or_spawn` ever needs a second
M, so at four processors the mark legitimately stays 1. **A case that reads either way depending on what
else the machine is doing is worse than no case**: it teaches every later reader to re-run the suite rather
than believe it.

⇒ **The marker's contract is the PROCESSOR COUNT, so that is what a case about the marker asserts.**
`MAXON_MAX_PROCS` settles `__sched_num_procs` to `min(requested, osCpuCount)` in `emitResolveMaxProcs`,
once, inside `__sched_init_procs`, before a single green thread runs and without consulting the workload.
How many worker Ms get built out of those Ps is a consequence of the WORK — the scheduler's business, not
the marker's promise. `schedMaxActiveWorkers()` is unchanged and still honest about what it is, a
measurement of an outcome; it belongs in cases that can tolerate one, like
`builtins-cpu-parallel.md`'s `sched-max-active-workers-is-one-under-async`, whose subject is a program that
provably builds no second M at all.

### What the four cases below share, and why they can share it

One program, four processor counts — the machine's, one, four, and four again. It spawns 8 services, sends
each a chunk of index-derived integer work, and collects the eight partial sums through **awaited replies**:

- **`procs=`** is `__Builtins.schedProcessorCount()` — a direct read of `__sched_num_procs`, the word the
  marker decides. It is not an inference and it is not a race: that word is written once at scheduler
  bring-up and never again. A program that has started no scheduler reads **0** (MEASURED), the truthful
  *"no P has been built"*, which is why the query costs one `.data` word and no green-thread runtime.
- **`clamped=`** is `1` when the count the scheduler resolved is exactly `min(requested, cpuCount())` —
  the marker's contract stated as a comparison the program can make on ANY machine, from two independent
  readings: an OS call and a scheduler word. A bare `procs=4` would have been a claim about this box.
- **`aggregate=`** is an order-independent sum of eight index-derived partial sums, so it is the SAME
  number however many processors serviced the work. That invariance is the property the entire flip must
  preserve, and it is the reason a wrong answer here is a wrong answer rather than a scheduling artefact.

⛔⛔ **THE AGGREGATE IS ACCUMULATED IN `self` AND SUMMED THROUGH REPLIES, AND IT MAY NOT BE A GLOBAL.**
The obvious way to write this program — one module-level `var total` that every handler adds to — is the
shape `specs-shv2/green-thread-globals.md` refuses, and its opening measurement is what happens if you
write it anyway: **five of ten runs at `MAXON_MAX_PROCS=16` lost an update, and all ten exited 0.** A
scaling case whose own tally races is an instrument that cannot fail honestly. Each service accumulates
into its own field; `main` sums the eight replies on the one green thread that awaited them.

⚠ **THE WORK PER SERVICE IS 20,000 ITERATIONS, AND WHAT THAT NUMBER BUYS HAS CHANGED.** It was chosen to
stabilize the worker-mark reading: at 400 that reading was **flaky at N=2** — 3 of 8 runs read `multi=1`
and 5 read `multi=0`, because `main` drained its own ring and finished before the worker M it woke had got
going. **20,000 was not enough either, and the full suite is what proved it** (see the box above): more
backlog only moves the odds, it does not remove the race, which is why these cases now assert the
processor count instead. The number stays because it is what the aggregate `479997` is the sum OF, and
because a real fan-out across eight services is what makes that invariance worth asserting; it is no
longer load-bearing against flakiness, and nothing here depends on how long the work takes.

⚠ **EVERY CASE HERE CARRIES `<!-- targets: x64-windows, arm64-macos -->`**, for the `spawn` rather than
for the scheduler: `SemanticCheck.requireTargetSupportsServiceEntry` refuses a service entry on every
other lane (`services.md`'s `error.a-service-is-rejected-on-arm64`). The last case's `cpuCount()` wants the
same two lanes and gets them for free — it is an OS call, refused elsewhere with E3104 — but that is a
second reason for a restriction the `spawn` already imposes, not a new one. `schedProcessorCount()` imposes
none: it is a `.data` load and lowers wherever shv2 emits.

## Tests

<!-- test: the-default-is-every-processor -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
⭐⭐ **THE FLIP ITSELF, AND ITS SUBJECT IS THE ABSENCE OF A MARKER.** Every other case in this file names a
count; this one names none, so it reads whatever `emitResolveMaxProcs` resolves with `MAXON_MAX_PROCS`
absent — and asserts that this is the machine's own processor count. It is the one case that goes red if
the default ever quietly returns to a constant.

⚠ **IT ASSERTS AN AGREEMENT AND NOT A NUMBER, FOR ITS SIBLINGS' REASON.** `every=1` is
`schedProcessorCount() == cpuCount()`, two independent readings — a `.data` word `__sched_init_procs`
settled, and an OS call — so it holds on a 2-core CI box exactly as it holds on this 16-way one. A bare
`procs=16` would have been a claim about one machine, and would have made every other box's green run a
lie about this one.

⚠ **AND IT IS NOT VACUOUS ON A ONE-PROCESSOR HOST, WHICH IS WHERE ITS SIBLING'S GATE GOES DARK.**
`the-procs-marker-raises-the-processor-count` cannot see an inert marker where no count can be raised; this
case has no marker to be inert, so `1 == 1` there is still the true reading of the true default.

⛔⛔ **SEEN RED, AND THE ONE THING THAT REDDENS IT IS AN OVERRIDE IN THE HARNESS'S OWN ENVIRONMENT — WHICH
IS THE CASE WORKING, NOT A FRAGILITY.** Its subject is *"no marker, no override"*, so running the WHOLE
suite under `MAXON_MAX_PROCS=1` overrides the very thing it asserts and it must move. MEASURED, this tree,
the full suite at `MAXON_MAX_PROCS=1`: **7096 passed, 1 failed — this case, and only this case**, reading
`every=0` against `every=1`. ⭐ **`aggregate=479997` was UNCHANGED in the same run**, which is the other
half worth having: the answer is processor-count-independent at one processor exactly as at sixteen, and
what moved was the count claim alone. ⇒ a reader who sets the variable globally and sees one red case here
is looking at the case doing its job; a reader who sees any OTHER case move is looking at a defect.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AdderHandleArray = Array with Adder.handle

let serviceCount = 8
let workPerService = 20000
let mixModulus = 7

type Adder
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function grind(seed Integer, work Integer)
		var j = 0
		while j < work 'spin'
			self.acc = self.acc + ((seed + j) mod mixModulus)
			j = j + 1
		end 'spin'
	end 'grind'

	export function total() returns Integer
		return self.acc
	end 'total'
end 'Adder'

function main() returns ExitCode
	var hs = AdderHandleArray.create()

	var i = 0
	while i < serviceCount 'spawnEach'
		hs.push(spawn Adder.create())
		i = i + 1
	end 'spawnEach'

	var k = 0
	while k < serviceCount 'sendEach'
		let h = try hs.get(k) otherwise panic("hs.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		h.grind(k, work: workPerService)
		k = k + 1
	end 'sendEach'

	var aggregate = 0
	var n = 0
	while n < serviceCount 'collect'
		let h = try hs.get(n) otherwise panic("hs.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await h.total() otherwise 0)
		n = n + 1
	end 'collect'

	// ⭐ The default's contract, as a comparison this program can make on any machine: with no
	// `MAXON_MAX_PROCS` in the environment, the scheduler built one P per processor the OS reports.
	var every = 0
	if __Builtins.schedProcessorCount() == __Builtins.cpuCount() 'agrees'
		every = 1
	end 'agrees'

	print("every={every}\n")
	print("aggregate={aggregate}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
every=1
aggregate=479997
```
```exitcode
0
```

<!-- test: the-procs-marker-pins-one-processor -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
<!-- procs: 1 -->
**THE MARKER'S OWN GATE, AND IT IS LOAD-BEARING ONLY AFTER THE FLIP.** The same program pinned to one
processor, and the one count here that can be asserted as a BARE NUMBER on any machine: `min(1, cpuCount)`
is 1 wherever this runs, because a machine cannot report fewer than one processor
(`CpuParallelRuntime.MinimumProcessorCount` is the floor that guarantees it). The aggregate is unchanged,
because it is unchangeable.

⭐ **THE COINCIDENCE HAS ENDED, AND THIS CASE IS NOW LOAD-BEARING.** It used to agree with the default it
was meant to override, so it could not see whether the override happened at all. The default is now the
machine's processor count, so `procs=1` is reachable ONLY through the marker — **MEASURED: the identical
program with the marker removed prints `procs=16` on this host.** A `procs:` marker that is parsed but
dropped, or misspelled into `SpecParser`'s silently-ignored bucket, turns this case red, which is exactly
what it is for.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AdderHandleArray = Array with Adder.handle

let serviceCount = 8
let workPerService = 20000
let mixModulus = 7

type Adder
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function grind(seed Integer, work Integer)
		var j = 0
		while j < work 'spin'
			self.acc = self.acc + ((seed + j) mod mixModulus)
			j = j + 1
		end 'spin'
	end 'grind'

	export function total() returns Integer
		return self.acc
	end 'total'
end 'Adder'

function main() returns ExitCode
	var hs = AdderHandleArray.create()

	var i = 0
	while i < serviceCount 'spawnEach'
		hs.push(spawn Adder.create())
		i = i + 1
	end 'spawnEach'

	var k = 0
	while k < serviceCount 'sendEach'
		let h = try hs.get(k) otherwise panic("hs.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		h.grind(k, work: workPerService)
		k = k + 1
	end 'sendEach'

	var aggregate = 0
	var n = 0
	while n < serviceCount 'collect'
		let h = try hs.get(n) otherwise panic("hs.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await h.total() otherwise 0)
		n = n + 1
	end 'collect'

	print("procs={__Builtins.schedProcessorCount()}\n")
	print("aggregate={aggregate}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
procs=1
aggregate=479997
```
```exitcode
0
```

<!-- test: the-answer-does-not-depend-on-the-processor-count -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
<!-- procs: 4 -->
⭐⭐ **THE INVARIANCE, WHICH IS THE ONE PROPERTY THE WHOLE FLIP MUST PRESERVE.** Four processors, and the
program must answer the number its two siblings answer at one and at the machine's count. Nothing else in
this file would notice a chunk of work that ran twice, or a reply that resolved from a stale field, or an
`self.acc` two Ms both stepped — a count reading answers what the scheduler was given and would go on
answering it through all three.

⚠ **IT PRINTS THE AGGREGATE ALONE, AND THE OMISSION IS THE POINT.** A `procs=` or `clamped=` reading is
about the PROCESSOR COUNT, which is the one thing this case deliberately varies; asserting it here would
pin the very axis the case exists to be indifferent to, and would turn the case red at the flip for a
reason having nothing to do with the answer. What this case claims is `479997`, three times, off three
different schedulers.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AdderHandleArray = Array with Adder.handle

let serviceCount = 8
let workPerService = 20000
let mixModulus = 7

type Adder
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function grind(seed Integer, work Integer)
		var j = 0
		while j < work 'spin'
			self.acc = self.acc + ((seed + j) mod mixModulus)
			j = j + 1
		end 'spin'
	end 'grind'

	export function total() returns Integer
		return self.acc
	end 'total'
end 'Adder'

function main() returns ExitCode
	var hs = AdderHandleArray.create()

	var i = 0
	while i < serviceCount 'spawnEach'
		hs.push(spawn Adder.create())
		i = i + 1
	end 'spawnEach'

	var k = 0
	while k < serviceCount 'sendEach'
		let h = try hs.get(k) otherwise panic("hs.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		h.grind(k, work: workPerService)
		k = k + 1
	end 'sendEach'

	var aggregate = 0
	var n = 0
	while n < serviceCount 'collect'
		let h = try hs.get(n) otherwise panic("hs.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await h.total() otherwise 0)
		n = n + 1
	end 'collect'

	print("aggregate={aggregate}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
aggregate=479997
```
```exitcode
0
```

<!-- test: the-procs-marker-raises-the-processor-count -->
<!-- targets: x64-windows, arm64-macos, arm64-linux -->
<!-- procs: 4 -->
⭐⭐ **THE MARKER'S OWN GATE IN THE OTHER DIRECTION, AND THE ONLY CASE HERE THAT COULD SEE AN INERT
`procs:` TODAY.** Its two `procs:`-marked siblings above cannot: one names the count the default already
has, and the other deliberately asserts nothing about the count. This one names a count ABOVE the
current default and asserts the scheduler RESOLVED it — so with the marker unread it runs at one P,
`schedProcessorCount()` answers 1 against an `expected` of 4, and it prints `clamped=0` against an
expectation of `clamped=1`. **MEASURED: the identical program with the marker removed prints exactly
that**, which is this case's red half seen rather than argued.

⚠ **IT ASSERTS AN AGREEMENT AND NOT A NUMBER, WHICH IS WHAT MAKES IT MACHINE-INDEPENDENT.** `procs=4`
would be a claim about a box with at least four processors; `clamped=1` is the claim
`min(requested, cpuCount())`, which is the clamp `emitResolveMaxProcs` actually applies, and it holds on a
two-processor box (where it pins 2) exactly as it holds here (where it pins 4). ⚠ On a strictly
SINGLE-processor host it would pin 1 and become vacuous — no `procs:` value can raise a count there, so
nothing could see an inert marker on such a machine and the case is honest to say so rather than red.

⚠ **IT NAMES ITS OWN COUNT, WHICH IS WHY IT COULD LAND BEFORE THE FLIP.** The case that reads whatever the
host has — `the-default-is-every-processor`, first in this file — belongs to the flip and arrived with it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AdderHandleArray = Array with Adder.handle

let serviceCount = 8
let workPerService = 20000
let mixModulus = 7

// ⚠ **THIS MUST MATCH THE `procs:` MARKER ABOVE, AND NOTHING CHECKS THAT IT DOES** — the marker is read by
// the harness and this is read by the program, so the one thing the case cannot verify from inside is that
// it was told the same number the scheduler was.
let requestedProcs = 4

type Adder
	var acc as Integer

	static function create() returns Self
		return Self{acc: 0}
	end 'create'

	export function grind(seed Integer, work Integer)
		var j = 0
		while j < work 'spin'
			self.acc = self.acc + ((seed + j) mod mixModulus)
			j = j + 1
		end 'spin'
	end 'grind'

	export function total() returns Integer
		return self.acc
	end 'total'
end 'Adder'

function main() returns ExitCode
	var hs = AdderHandleArray.create()

	var i = 0
	while i < serviceCount 'spawnEach'
		hs.push(spawn Adder.create())
		i = i + 1
	end 'spawnEach'

	var k = 0
	while k < serviceCount 'sendEach'
		let h = try hs.get(k) otherwise panic("hs.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		h.grind(k, work: workPerService)
		k = k + 1
	end 'sendEach'

	var aggregate = 0
	var n = 0
	while n < serviceCount 'collect'
		let h = try hs.get(n) otherwise panic("hs.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		aggregate = aggregate + (try await h.total() otherwise 0)
		n = n + 1
	end 'collect'

	// ⭐ The marker's contract, as a comparison this program can make on any machine: the count the
	// scheduler resolved IS the count the marker named, clamped against the processors that exist. The two
	// readings come from independent places — an OS call and a `.data` word `__sched_init_procs` settled —
	// so an agreement between them is a real one.
	let procs = __Builtins.schedProcessorCount()
	let cpus = __Builtins.cpuCount()
	let expected = requestedProcs if cpus > requestedProcs else cpus

	var clamped = 0
	if procs == expected 'clamped'
		clamped = 1
	end 'clamped'

	print("clamped={clamped}\n")
	print("aggregate={aggregate}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
clamped=1
aggregate=479997
```
```exitcode
0
```
