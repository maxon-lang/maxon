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
variable by hand (`scripts/multicore-stress/pin-matrix.sh`). ⇒ **the code was written, built and shipped, and the
default build never executed it.** Flipping the default is what puts it under the program that runs.

⭐ **THE DEFAULT IS NO LONGER A CONSTANT, WHICH IS WHY IT COULD NOT BE A CONSTANT SUBSTITUTION.** A
processor count is a fact about the machine, so `emitResolveMaxProcs` reads `StdOp.osCpuCount` at scheduler
bring-up — floored at `SchedRuntime.MinimumProcessorCount`, since the OS read can answer 0 or -1 — and
that reading is the default.

### `MAXON_MAX_PROCS` is GOMAXPROCS — it LOWERS as well as raises

The variable sets the count **exactly**, as Go's `schedinit` takes any positive `GOMAXPROCS`: a request
below the machine's count lowers it, and a request above the machine's count raises it.

**A value that is not a processor count never errors.** A processor count is a decimal that fits a positive
32-bit signed integer, the test Go's `schedinit` makes with `strconv.ParseInt(…, 10, 32)` and `n > 0`;
anything else is ignored and the machine's count applies. On a 16-processor host:

| `MAXON_MAX_PROCS` | `schedProcessorCount()` | why |
|---|---|---|
| unset | 16 | `osEnvRead` answers 0 characters |
| `1` | 1 | taken exactly — a lowering |
| `2` | 2 | taken exactly |
| `16` | 16 | taken exactly |
| `64` | 64 | taken exactly — a raising above the machine |
| `0` | 16 | not positive ⇒ ignored |
| `abc` | 16 | not a decimal ⇒ ignored |
| `12abc` | 16 | not a decimal ⇒ ignored |
| `2147483648` | 16 | above 2147483647 ⇒ ignored |
| `18446744073709551621` | 16 | above 2147483647 ⇒ ignored; it is `2^64 + 5`, so a walk that wraps in 64 bits would read 5 |
| 39 digits | 16 | above 2147483647 ⇒ ignored |

### `<!-- procs: N -->` — the marker that makes the count a property of the CASE

⛔ **`specs/sched-runqueue.md` states the restriction this marker lifts, in as many words:** *"A SPEC
CASE CANNOT SET `MAXON_MAX_PROCS`, SO NOTHING BELOW EXERCISES TWO PROCESSORS — the harness gives a case
`Args:` and no environment."* That is why the whole green-thread substrate was gated by a shell script
rather than by the suite, and why a scheduler bug reachable only at N ≥ 2 could sit behind a wholly green
`spec-test` run.

`<!-- procs: N -->` sets `MAXON_MAX_PROCS=N` in the environment the harness gives that one case, beside
`<!-- unsupported-targets: … -->` and `<!-- Args: … -->` on the same kind of comment line
(`maxon-bin/Testing/SpecParser.maxon`). A case that carries none gets the process default.

⭐ **THE CASE THAT ASSERTS THE DEFAULT ITSELF — `the-default-is-every-processor` — IS THE FIRST ONE
BELOW.** Its whole subject is the ABSENCE of a marker: it sets no count and asserts that the scheduler
resolved the machine's own.

⇒ **`the-procs-marker-pins-one-processor` IS THE GATE THAT THE OVERRIDE HAPPENS.** The harness refuses a
comment line no directive reads, but a `procs:` marker that is READ and then not applied would still run the
case at the default. The default is the machine's count, so its `procs=1` is reachable ONLY through the
marker. **MEASURED: the
same program with the marker removed prints `procs=16` on this host.** A `procs:` marker that is parsed but
dropped turns it red.

**`the-procs-marker-raises-the-processor-count` is the same gate pointing the other way**, and it is last
below. It names a count ABOVE one and asserts the scheduler RESOLVED it, so an unread marker leaves the
case at the machine's count and it prints that count instead of `procs=4` on any host without exactly four
processors.

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
`MAXON_MAX_PROCS` settles `__sched_num_procs` to the requested count in `emitResolveMaxProcs`,
once, inside `__sched_init_procs`, before a single green thread runs and without consulting the workload.
How many worker Ms get built out of those Ps is a consequence of the WORK — the scheduler's business, not
the marker's promise. `schedMaxActiveWorkers()` is unchanged and still honest about what it is, a
measurement of an outcome; it belongs in cases that can tolerate one, like
`builtins-cpu-parallel.md`'s `sched-max-active-workers-is-one-under-async`, whose program builds no second M
unless the system monitor starts one, and reads the monitor's counters to say which.

### What the four cases below share, and why they can share it

One program, four processor counts — the machine's, one, four, and four again. It spawns 8 services, sends
each a chunk of index-derived integer work, and collects the eight partial sums through **awaited replies**:

- **`procs=`** is `__Builtins.schedProcessorCount()` — a direct read of `__sched_num_procs`, the word the
  marker decides. It is not an inference and it is not a race: that word is written once at scheduler
  bring-up and never again. Asking installs the scheduler, so no program reads the word's `.data` seed of
  **0** — `Scheduler.processorCount()` is the public spelling of the same read (`scheduler-processor-count.md`).
- **`aggregate=`** is an order-independent sum of eight index-derived partial sums, so it is the SAME
  number however many processors serviced the work. That invariance is the property the entire flip must
  preserve, and it is the reason a wrong answer here is a wrong answer rather than a scheduling artefact.

⛔⛔ **THE AGGREGATE IS ACCUMULATED IN `self` AND SUMMED THROUGH REPLIES, AND IT MAY NOT BE A GLOBAL.**
The obvious way to write this program — one module-level `var total` that every handler adds to — is the
shape `specs/green-thread-globals.md` refuses, and its opening measurement is what happens if you
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

⚠ **NO CASE HERE CARRIES A LANE RESTRICTION.** A `spawn` runs on all four native lanes and is refused on
wasm32-wasi by `SemanticCheck.requireTargetSupportsServiceEntry`; the first case's `cpuCount()` is an OS
call refused there with E3104. Both refusals are REPORTED as counted SKIPs naming the case, so a marker
would state what the run already says and hide the case while doing it. `schedProcessorCount()` is refused
there with E3104 too, as a query about a scheduler that lane does not have.

## Tests

<!-- test: the-default-is-every-processor -->
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
	return 0
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
<!-- procs: 1 -->
**THE MARKER'S OWN GATE.** The same program pinned to one processor, which the scheduler takes exactly on
any machine. The aggregate is unchanged, because it is unchangeable.

⭐ **THIS CASE IS LOAD-BEARING.** The default is the machine's processor count, so `procs=1` is reachable
ONLY through the marker — **MEASURED: the identical program with the marker removed prints `procs=16` on
this host.** A `procs:` marker that is parsed but dropped turns this case red, which is exactly what it
is for.
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
	return 0
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
<!-- procs: 4 -->
⭐⭐ **THE INVARIANCE, WHICH IS THE ONE PROPERTY THE WHOLE FLIP MUST PRESERVE.** Four processors, and the
program must answer the number its two siblings answer at one and at the machine's count. Nothing else in
this file would notice a chunk of work that ran twice, or a reply that resolved from a stale field, or an
`self.acc` two Ms both stepped — a count reading answers what the scheduler was given and would go on
answering it through all three.

⚠ **IT PRINTS THE AGGREGATE ALONE, AND THE OMISSION IS THE POINT.** A `procs=` reading is
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
	return 0
end 'main'
```
```stdout
aggregate=479997
```
```exitcode
0
```

<!-- test: the-procs-marker-raises-the-processor-count -->
<!-- procs: 4 -->
⭐⭐ **THE MARKER'S OWN GATE IN THE OTHER DIRECTION.** It names a count above one and asserts the scheduler
RESOLVED it. An unread marker leaves the case at the machine's count, which prints some count other than
`procs=4` on every host that does not have exactly four processors.

⚠ **IT ASSERTS A BARE NUMBER, AND THAT IS MACHINE-INDEPENDENT.** `emitResolveMaxProcs` takes a requested
count exactly, above the machine's count as well as below it, so `procs=4` holds on a one-, two- or
sixteen-processor host alike.

⚠ **IT NAMES ITS OWN COUNT, WHICH IS WHY IT COULD LAND BEFORE THE FLIP.** The case that reads whatever the
host has — `the-default-is-every-processor`, first in this file — belongs to the flip and arrived with it.
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
	return 0
end 'main'
```
```stdout
procs=4
aggregate=479997
```
```exitcode
0
```
