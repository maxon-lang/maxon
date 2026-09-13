---
feature: runtime-source-tier
status: stable
keywords: [runtime, raw-intrinsics, compiler-internals, source-tier, reserved-identifier]
category: diagnostics
---

# The Runtime Source Tier

## Documentation

A `runtime/` directory sits at the checkout root beside `stdlib/`, and its `.maxon` files are a
PRIVILEGED SOURCE TIER. They load as ordinary sources — parsed, type-checked and lowered like any
other file — but the rules that hold the reserved space shut for user code are lifted inside that
cone, and two rules that hold nowhere else are imposed on it.

The tier exists so that the runtime the compiler emits into every program becomes INPUT the compiler
READS FROM THE TREE rather than compiler code that BUILDS IR. A runtime written as IR-building code
lives inside the compiler, so changing it needs the two self-compiles that `maxon-bin/CLAUDE.md`
spells out: the first build fixes the emitter, the second gives the compiler its own new runtime. A
runtime written as SOURCE the compiler reads is just another input — the compiler that reads it is
already correct — so a runtime change needs ONE self-compile, and the runtime becomes readable,
formattable and diffable as Maxon instead of as string-valued IR.

The privileges are what a runtime needs and nothing else:

- **It may DECLARE a `__`-prefixed name.** The reserved prefix is the runtime's own name space, so the
  E2051 reservation that stops user code from declaring into it is lifted across the whole cone.
  ⚠ One tier entry wears no prefix and cannot take one, because its name is a FRAME a backtrace prints:
  `maxon_force_segfault` (`specs/safety.md`). It is reserved from user declarations by NAME under the same
  E2051, and that reservation is what keeps a second declaration of it from making the name contested
  across directories and renaming the tier's own symbol.
- **It may CALL a `__Raw.*` intrinsic.** `__Raw` is the closed table of raw machine and OS operations
  — the floor a runtime is written on top of, and the one surface below which there is no Maxon.

⭐ **THE BODIES ARE COMPILED, AND THE CASE THAT SAYS SO IS `builtins-clock.md`'s.** Every test below
stops at the parser: each asserts what the tier ADMITS or REFUSES, and every one would still pass if the
compiler threw the body away immediately afterwards. What proves otherwise is a REAL family — the wall
clock, `runtime/Clock.maxon`'s `__clock_now_unix_s` — reached from a user program through
`__Builtins.currentUnixTimeSeconds()` and pinned there under a ```RequiredRuntime block. No source file
outside the tier may CALL a runtime entry, so a call the compiler emits is the only root a program can
deliberately reach one through, and a family that has one is the only honest way to watch the far end.

⛔ **THE PROTECTION TAKES TWO REFUSALS, BECAUSE A NAME CAN BE REACHED THROUGH TWO DOORS.** A user file
CALLING a runtime entry earns E3004 and one NAMING it in value position — `let f = __parallel_boundary`,
which would otherwise resolve to the tier's declaration and reach it through a synthesized `__fnref_` thunk
— earns E3155. Both are conjoined with the same tier test, so the property is *"no source file outside the
tier may CALL or NAME a runtime entry"* rather than a rule about call syntax.

The restrictions are what a runtime cannot have:

- **No managed value may be admitted.** The reference-counting pass emits calls into the very runtime
  this tier DEFINES, so a managed local here is a runtime function that calls itself into existence.
  The rule is asked of the TYPE a runtime file spells — a field, a return, a parameter, a cast target —
  and of every VALUE the file builds, whatever names it or leaves it unnamed. Asking it of the value is
  what reaches a `for` element, a caught error, a `match` payload and a temporary no binding form sees; a
  CAPTURING closure is asked separately, because its heap is an environment block rather than the type of
  any value. A non-capturing closure, and a caught error whose enum owns nothing, are both admitted.
- **Nothing wider than `module`.** `runtime/` is loaded into every program, so a declaration visible
  outside the tier contests names with the programs it is linked into, and the compiler compiling
  itself is one of them. `module` is the widest visibility the tier's own file-to-file sharing needs.

⛔⛔ **AND ONE RESTRICTION IS NOT A RULE OF THE TIER AT ALL BUT A PROPERTY OF WHAT A BUILDER CAN DO THAT
SOURCE CANNOT — AND IT IS THE BOUNDARY: A FAMILY'S BODIES CAN BE TIER SOURCE IFF THE BUILDER THAT EMITS THEM
IS A CONSTANT FUNCTION OF `RuntimeUsage`.** A builder can emit a lock acquire only where the program has a
second thread, or a per-P TLS read only where there are processors to shard by. Tier source is compiled once
and reads no usage record, so such a body would have to spell every arm unconditionally — which is a
different body, not a harder one.

⚠ **A LITERAL A BUILDER'S CALLER PASSES IS NOT SUCH AN ARGUMENT.** `zeroed` never reaches `RuntimeUsage`:
`SlabRuntime.installSlabRuntime` passes a literal to each of the three allocation doors, and an argument
that is fixed per ENTRY POINT is one tier source spells as a `bool` parameter on one helper. That is a cost;
`sharded` (`usesGt`) and `countRaw` (`usesMmCounters`) are the blockers.

⇒ **A FAMILY'S PARTITION IS DECIDED BY ITS BUILD-TIME ARGUMENTS BEFORE ITS CALL GRAPH IS EVEN CONSULTED, AND
THE SECOND-SPELLING TEST IS ASKED AFTER BOTH.** Nine of the object layer's fourteen entry points are blocked
by the argument; three more by the second-spelling rule (`__slab_drain_remote`, `__slab_rounded_size` and
`__slab_span_destroy` each share a walk with a body that stays); and the five that move take neither.

## Tests

<!-- test: runtime-file-may-declare-a-reserved-name -->
**P1 — THE DECLARATION PRIVILEGE, WIDENED FROM TWO NAMED FILES TO A CONE.** Before the tier, exactly
one file could declare into the reserved space (`<stdlibDir>/Builtins.maxon`, by IDENTITY — see
`reserved-double-underscore.md`, which pins both halves of that exemption). The tier is the same
permission keyed on a DIRECTORY instead of on a file name, and this is the positive control on it:
the declaration compiles rather than raising E2051.

`main` deliberately does not call `__probe_answer`. What is under test is the DECLARATION door, and a
call would drag the call door (case two) into the same case; two doors pinned by one case is a case
that cannot say which one moved.
```maxon
// --- runtime-file: Probe.maxon
function __probe_answer() returns ExitCode
	return 7
end '__probe_answer'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-call-a-raw-intrinsic -->
**P2 — THE CALL PRIVILEGE, AND IT IS A SEPARATE DOOR FROM P1.** Declaring a `__` name and CALLING one
are decided at different sites (`Parser.requireUnreservedName` and
`Parser.requireCalleeIsNotReservedName`), which is exactly the asymmetry the older stdlib exemption
shipped with: the declaration door opened first and the call door stayed shut, so the one file that
could declare `__int_fromString` could not then call it.

`__Raw.osTickCountMs` is the intrinsic under the privilege here and it takes no argument, so nothing
about the case turns on argument lowering.
```maxon
// --- runtime-file: Probe.maxon
function probeTicks() returns MachineWord
	return __Raw.osTickCountMs()
end 'probeTicks'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-keep-its-own-frame -->
**⭐⭐ A RUNTIME BODY WHOSE CONTRACT IS THE CALL ITSELF, AND THE ROW THAT SAYS SO.** Every other
`__Raw` row is an operation; `ownFrame` is the one that is an INSTRUCTION TO THE COMPILER, and it earns
that exception by being the only thing a runtime body cannot otherwise state. A tier body flows through
the ordinary pipeline, so `InlineLeaves` splices a small one into each of its call sites and dead-function
elimination then drops it — which is correct for a body that computes an answer and destroys a body whose
whole product is a FRAME: a checkpoint a profile attaches to, a stack-walk entry a backtrace prints.

⚠ It appends no Std op and costs no instruction. What it does is set a fact about the enclosing function
(`IrFunction.keepsItsOwnFrame`), which `InlineLeaves.functionShape` refuses to splice for the reason it
already refuses a green-thread stack guard: the frame is load-bearing.

Under test here is the DECLARATION door alone — that the row exists and a runtime file may spell it. That
the frame then survives is measured where a compiler-emitted call reaches one, in
`builtins-parallel-boundary.md`.
```maxon
// --- runtime-file: Probe.maxon
function __probe_marked()
	__Raw.ownFrame()
end '__probe_marked'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-ask-the-machine-for-its-cpu-count -->
**AN OS ROW WHOSE FACILITY ONLY SOME LANES PROVIDE.** `osCpuCount` is the raw floor's spelling of the one
question `__cpu_count` exists to answer, and it is the row that carries `HostFacility.cpuCount` — so a lane
without the read has nothing to lower it onto.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and the
case runs on every lane. What is under test is the TABLE: the row exists, the tier may spell it, and its
result is a machine word.
```maxon
// --- runtime-file: Probe.maxon
function probeCpuCount() returns MachineWord
	return __Raw.osCpuCount()
end 'probeCpuCount'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-read-a-compiler-owned-global -->
⭐⭐ **A `.data` WORD THE COMPILER LAID OUT, ADDRESSED BY A ROW AND READ BY ANOTHER.** A runtime body that
answers a scheduler counter needs the word's ADDRESS, and an address is not something the tier can compute:
the label is minted by the compiler and resolved by the linker. So each readable word is its own row,
answering an address, and the existing `loadWord` row turns that address into the value — the same two-step
`__Raw.scratch` and `loadWord` already make over a frame slot.

⛔ **WHICH WORDS ARE READABLE IS THE TABLE'S QUESTION AND NOT THE `.data` SECTION'S.** Every global the
emitted runtime lays out would otherwise be in reach of a tier body by NAME, which is a wider privilege than
anything else on this floor and wider than any family needs. A row per readable word keeps the roster closed
by construction: an address row that does not exist cannot be spelled, and the lowering's exhaustive match
means a row that exists names a LABEL. That the label is LAID OUT is a separate argument the family owes:
the `.data` slot rides a `RuntimeUsage` bit the call site sets, and that same call site is the edge dead
function elimination keeps the body alive for.

⚠ **THAT LAST ARGUMENT IS THE SCHEDULER PAIR'S AND NOT EVERY FAMILY'S.** The slab arena's two words
(`slabArenaListAddr`, `slabArenaMapL1Addr`) are read by tier bodies whose callers are `StdOp.call` sites an
INSTALLER mints, so there is no call site in any Maxon body to set a bit from. Their slot rides a DECLARED
bit instead (`RuntimeUsage.closeSlabNeeds`), and what makes that sound is that the only minter of a call
into the family — `SlabRuntime.installSlabRuntime` — reads the same `usesHeap` the declaration does.
⇒ **a family whose entry points the compiler reaches by minting a call owes that declaration, not this
paragraph's coincidence.**
```maxon
// --- runtime-file: Probe.maxon
function probeProcessorCount() returns MachineWord
	return __Raw.loadWord(__Raw.schedNumProcsAddr(), offset: 0)
end 'probeProcessorCount'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-ask-the-os-for-its-process-id -->
**AN OS ROW WHOSE ANSWER IS AN IDENTITY THE KERNEL ALREADY HOLDS.** `osGetPid` is the raw floor's spelling
of the read behind `__proc_pid`, and it carries `HostFacility.processInfo` — so a lane without the read has
nothing to lower it onto.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and the
case runs on every lane. What is under test is the TABLE: the row exists, the tier may spell it, and its
result is a machine word.
```maxon
// --- runtime-file: Probe.maxon
function probePid() returns MachineWord
	return __Raw.osGetPid()
end 'probePid'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-enter-background-priority -->
⭐ **THE ONE OS ROW ON THIS FLOOR THAT CHANGES THE PROCESS RATHER THAN REPORTING ON IT**, and it still
answers a machine word: the op sets the priority and then READS IT BACK, so what a tier body receives is a
second reading rather than an echo of the value written (`StdOp.osEnterBackgroundPriority`). Its facility is
`HostFacility.processPriority`, which is its own row and not `processInfo`'s — a lane can serve the identity
reads long before it can serve this write.

⚠ The probe is uncalled, so nothing here changes the priority of the process running the suite: the body is
dropped before instruction selection. What is under test is the TABLE.
```maxon
// --- runtime-file: Probe.maxon
function probeBackgroundPriority() returns MachineWord
	return __Raw.osEnterBackgroundPriority()
end 'probeBackgroundPriority'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-address-a-single-byte -->
**THE WORD ROWS HAVE A BYTE-WIDE TWIN, AND A BYTE IS NOT A NARROW WORD.** `loadWord`/`storeWord` move
the machine's own unit; an allocator's bitmaps, headers and poison bytes are addressed one byte at a
time, and a tier body cannot get at one by masking — a `storeWord` of a masked value writes the seven
neighbours too. So the floor carries `loadByte`/`storeByte`, which lower to the same `loadIndirect`
/`storeIndirect` the word rows do under `StdType.u8`.

The probe reaches a byte through the frame address `__Raw.scratch` answers, which is the only address
a tier body can obtain without asking the OS for one.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and
the case runs on every lane. What is under test is the TABLE: the two rows exist, the tier may spell
them, and a load answers a machine word while a store answers nothing.
```maxon
// --- runtime-file: Probe.maxon
let ProbeScratchBytes = 8
let ProbeByte = 255 as MachineWord

module function probeBytes() returns MachineWord
	let addr = __Raw.scratch(ProbeScratchBytes)
	__Raw.storeByte(addr, offset: 0, value: ProbeByte)

	return __Raw.loadByte(addr, offset: 0)
end 'probeBytes'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-update-a-word-atomically -->
⭐⭐ **THE TWO ATOMICS, AND THEY ANSWER IN OPPOSITE CONVENTIONS ON PURPOSE.** `atomicAddWord` answers the
PRIOR value, which is what both ISAs' read-modify-write instructions leave behind and what a caller
wanting the new one recovers with a single add; `atomicCas` answers 1 on success and 0 on failure, which
is what both ISAs leave in their flags and what every compare-and-swap loop tests. A caller written for
one convention against an op implementing the other loops for ever on a legitimately failing swap, so the
two rows are read here together.

⛔ **NEITHER TAKES AN OFFSET, AND THAT IS THE STD OPS' RULE RATHER THAN AN OMISSION** — an atomic names
ONE word and nothing else, so a caller addressing a field computes `addr + k` itself. That is why their
shapes are `twoWords` and `threeWords` where the byte and word accessors carry a constant displacement.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and the
case runs on every lane. What is under test is the TABLE.
```maxon
// --- runtime-file: Probe.maxon
let ProbeAtomicBytes = 8
let ProbeDelta = 1 as MachineWord
let ProbeExpected = 0 as MachineWord
let ProbeReplacement = 7 as MachineWord

module function probeAtomics() returns MachineWord
	let addr = __Raw.scratch(ProbeAtomicBytes)
	let prior = __Raw.atomicAddWord(addr, delta: ProbeDelta)
	let swapped = __Raw.atomicCas(addr, expected: ProbeExpected, replacement: ProbeReplacement)

	return prior + swapped
end 'probeAtomics'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-fill-a-range-with-one-byte -->
⭐ **THE FILL IS A ROW BECAUSE THE THING IT BECOMES IS A SIZE LADDER, AND A TIER BODY CANNOT WRITE ONE.**
Spelled out of `storeByte`, zeroing a recycled slot is one store, one add, one compare and one branch per
byte; the backend picks overlapping stores, a block loop or `rep stosq` by SIZE, and choosing an
instruction sequence by size is not something this floor has the vocabulary to say.

⛔ **THE FILL BYTE IS A COMPILE-TIME CONSTANT AND THE ROW'S SHAPE SAYS SO.** Every producer of a fill
knows its pattern at compile time — zero to restore the zeroing contract, a poison byte to mark a dead
payload — and a constant is what lets the instruction selector broadcast the byte across a 64-bit
immediate for free. A value operand would buy a generality no caller asks for and charge every fill a
runtime multiply.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and the
case runs on every lane. What is under test is the TABLE.
```maxon
// --- runtime-file: Probe.maxon
let ProbeFillBytes = 16
let ProbeFillByte = 0

module function probeFill()
	let addr = __Raw.scratch(ProbeFillBytes)
	__Raw.memFill(addr, byteCount: ProbeFillBytes as MachineWord, value: ProbeFillByte)
end 'probeFill'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-take-pages-from-the-os -->
⭐⭐ **FIVE PAGE ROWS, AND THEY ARE FIVE BECAUSE RESERVING ADDRESS SPACE AND BACKING IT ARE DIFFERENT
QUESTIONS.** `osAllocPages` does both at once, which charges an allocator for every byte of the arena it
wants to OWN on the day it maps it. `osReservePages` takes address space with no backing and
`osCommitPages` backs a range inside it, so an arena can be sized for the address space it wants while a
hello-world commits a fraction of it. `osDecommitPages` gives the backing back and KEEPS the reservation,
so the base stays valid; `osFreePages` gives both back and the base does not.

⛔ **`osCommitPages` DESTROYS THE CONTENTS OF THE RANGE IT COMMITS ON THE THREE POSIX LANES.** POSIX has
no commit call — the mapping IS the commitment, so a commit is a `MAP_FIXED` mapping that REPLACES
whatever was there with fresh zeroed pages, where Windows keeps every byte. A caller may therefore commit
only a range it holds no data in.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection: no
page is taken, none is leaked, and the case runs on every lane. What is under test is the TABLE.
```maxon
// --- runtime-file: Probe.maxon
let ProbePageBytes = 65536 as MachineWord

module function probePages() returns MachineWord
	let reserved = __Raw.osReservePages(ProbePageBytes)
	let committed = __Raw.osCommitPages(reserved, size: ProbePageBytes)
	__Raw.osDecommitPages(committed, size: ProbePageBytes)
	__Raw.osFreePages(reserved, size: ProbePageBytes)

	let whole = __Raw.osAllocPages(ProbePageBytes)
	__Raw.osFreePages(whole, size: ProbePageBytes)

	return committed
end 'probePages'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-guard-a-word-with-an-os-lock -->
**THE THREE LOCK ROWS, OVER A REGION THE CALLER OWNS RATHER THAN A HANDLE THE OS HANDS BACK.** Each takes
the ADDRESS of a region the caller has set aside and nothing else: the OS objects behind them differ in
size and shape per platform, so the region is the lock and the row carries no result to store. A tier
body's own frame is a region it owns, which is what `__Raw.scratch` answers here.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection — no
lock is initialised and none is entered — and the case runs on every lane. What is under test is the
TABLE.
```maxon
// --- runtime-file: Probe.maxon
let ProbeLockBytes = 64

module function probeLock()
	let lock = __Raw.scratch(ProbeLockBytes)
	__Raw.osLockInit(lock)
	__Raw.osLockEnter(lock)
	__Raw.osLockLeave(lock)
end 'probeLock'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unreached-runtime-body-costs-the-program-nothing -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The bit this measures is set by a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⛔⛔ **A TIER BODY THE PROGRAM CANNOT REACH MUST NOT SPEND ITS BUDGET.** A runtime name is never
`unreachable` — the tier's bodies are built unconditionally, because no source call edge earns them — and
`scanRuntimeUsage`'s only skip reads that same set. So a CALL inside a tier body is credited to EVERY
program the tier is linked into, including the ones dead-function elimination sweeps the body out of.

Here `probeWorkers` asks for the worker mark and `main` asks for nothing. The body is swept, so the
program cannot observe the answer — but the call sets `usesSchedMaxActiveWorkers`, which is what lays the
`.data` word out (`SchedRuntime.schedRuntimeGlobals`), so the word ships in an image that can never read
it. The `.data` roster is the channel that shows it.

⚠ The probe calls a RUNTIME ENTRY and not a `__Raw` row, and that is the whole point: a `rawIntrinsic`
names no callee and records nothing, which is why every tier family before this one left the question
untouched.
⚠ **THE PIN STATES THE WHOLE ROSTER, NOT A LEADING SLOT, AND IT HAS TO.** `RequiredData` is a PREFIX
compare and a program's own globals are laid out AHEAD of the runtime's, so a word the program did not
earn lands BEHIND `used` where a one-line pin cannot see it. Spelling the trailing console probes is what
makes an inserted word a mismatch rather than a longer tail.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
i8 0
i8 0
```

<!-- test: a-reached-runtime-entry-still-earns-its-word -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The bit this measures is set by a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⭐⭐ **THE CONTROL ON THE CASE ABOVE, AND WITHOUT IT THE RULE COULD BE *"CREDIT NO TIER BODY, EVER"*.** That
answer passes the unreached case and is the dangerous direction: a bit left UNSET while the body survives
leaves `runtime/CpuParallel.maxon`'s query loading a `.data` word the image never laid out. So the same
`Probe.maxon` stands here unchanged and `main` asks for the worker mark itself — a call the compiler mints
is the only root a program can reach a tier entry through — and both halves of the bit's job are pinned: the
word is in `.data`, and the body that reads it is in the image.

⚠ **WHAT ACTUALLY FIRES ON THE BAD ANSWER IS A PANIC, NOT A MISMATCH.** Uncredit this entry and it enters
`LibraryFacts.unreachedRuntimeTier` while `main` still calls it, which is the disagreement
`DeadFunctionElimination.requireUnreachableLibraryStayedDead` exists to refuse — so this case reddens on an
abort with the name in it rather than on a shorter `.data` roster.

⚠ It also puts the DERIVATION's other path under a case. The unreached program above names no library
function at all, so the reach split is decided by `deriveLibraryFacts`' short-circuit; this one crosses into
library source at `__sched_max_active_workers`, so the precise from-`main` walk decides it.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42

function main() returns ExitCode
	let workers = __Builtins.schedMaxActiveWorkers()
	return used - 41 - workers
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i64 1
i64 1
i8 0
i8 0
i8 0
```
```RequiredRuntime
__sched_max_active_workers
```

<!-- test: reaching-one-family-does-not-credit-another-tier-body -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
⚠ **THE PROPERTY IS TARGET-NEUTRAL AND THE CHANNEL IS NOT.** `RequiredData` is a PREFIX compare, so it can only catch a word inserted where something still TRAILS it — and the only globals laid out after a program's own are the x64-windows console probes. On a lane without them the pinned tail runs past the end of the section, and a shorter `.data` is all the gate can say. The bit this measures is set by a target-neutral walk; what is missing elsewhere is an anchor, not the behaviour.
⭐ **REACHED IS PER ENTRY POINT, NOT PER TIER.** `main` reaches the process family and nothing else, so the
precise walk runs and files `probeWorkers` unreached — and the worker counters stay out of `.data` even
though a tier body, compiled into this very image, calls the query that lays them.

⚠ **THE SECOND FAMILY IS `__proc_pid` BECAUSE IT IS THE ONE THAT PINS HONESTLY HERE.** The other bits a tier
body could set on this lane — `usesBackgroundPriority`, `usesCpuCount`, `usesWallClock` — reach only the PE
writer's optional import band and the non-Windows host chunks, which no spec channel renders; the pid read
lays no word of its own, so its presence leaves the `.data` roster exactly as the unreached case's and the
two sched words are the whole difference between the three programs in this group.
```maxon
// --- runtime-file: Probe.maxon
module function probeWorkers() returns MachineWord
	return __sched_max_active_workers()
end 'probeWorkers'
// --- file: main.maxon
var used = 42

function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	if pid > 0 'aRealProcess'
		return used - 42
	end 'aRealProcess'

	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
i8 0
i8 0
```

<!-- test: runtime-file-may-not-bind-a-managed-value-in-a-loop -->
⛔⛔ **R1's VALUE HALF IS ASKED OF THE VALUE, NOT OF THE BINDING FORM — AND A LOOP IS WHY IT HAS TO BE.**
A `for` element is bound by neither `declareInitializedBinding` nor `bindParameters`, so a rule stated at
those two doors admits the element of a `String` walk in a file whose whole premise is that the
reference-counting pass must never reach it. The refusal is raised off the parser's value type columns
instead — `mintValue` and `retypeValue`, which every value passes whatever binds it or leaves it unbound.

⚠ The anchor is the STRING, not the element name. A `String` walk builds the managed value at its
iterable and the `Character` element out of it, and the first one the file builds is the one reported: it
is the construct the author has to remove, and the element goes with it.
```maxon
// --- runtime-file: Probe.maxon
module function probeWalk() returns MachineWord
	var seen = 0

	for c in "abc" 'eachByte'
		seen = seen + (c.byteLength() as MachineWord)
	end 'eachByte'

	return seen as MachineWord
end 'probeWalk'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:6:11: a managed value of type 'String' is built in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-not-build-an-unnamed-managed-temporary -->
⭐⭐ **THE DOOR NO BINDING FORM CAN EVER REACH: A MANAGED VALUE NOTHING NAMES.** Here every name in the
file is a machine word and every type it spells is one — the `String` exists only as the receiver of a
method call, for the length of one expression. A rule asked at bindings sees nothing to ask about, and a
rule asked at spelled types sees nothing written down; the value is still allocated, still refcounted, and
still emits `__mm_decref` into the tier that defines it.

⚠ This is the case that makes the value columns the RIGHT site rather than a convenient one. There is no
name to hang a diagnostic on and no type to point at, so the only thing that can be refused is the value —
which is exactly what the columns hold.
```maxon
// --- runtime-file: Probe.maxon
module function probeLength() returns MachineWord
	return "abc".count() as MachineWord
end 'probeLength'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:9: a managed value of type 'String' is built in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-not-build-an-interpolated-string -->
**A SECOND PRODUCER OF THE SAME REFUSAL, AND IT IS THE ONE THAT ALLOCATES.** A string LITERAL can be an
immortal `.rdata` record; an interpolation is a fresh heap record built at run time by the very allocator
this tier is being written to define. So the two are not one case with two spellings — the refusal has to
hold for a value the compiler mints rather than one it lays out, and this is the half where a hole would
cost a real `__mm_alloc`.
```maxon
// --- runtime-file: Probe.maxon
module function probeInterpolated() returns MachineWord
	return "a{1}b".count() as MachineWord
end 'probeInterpolated'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:9: a managed value of type 'String' is built in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-walk-raw-words-in-a-loop -->
⭐⭐ **THE CONTROL, AND WITHOUT IT THE RULE COULD BE *"REFUSE EVERY LOOP IN `runtime/`"*.** That answer
passes all three refusals above and is the dangerous direction: the allocator is the next family to
migrate and it is the first tier code written out of counted loops, bit-run walks and copy ranges. A rule
that refused a `for` — rather than refusing a MANAGED VALUE that a `for` can happen to build — would make
that port impossible while every case above stayed green.

Both loop forms are here because they bind their induction variables differently: a `for` element is bound
by the loop's own door and a `while` condition binds nothing at all. Every value the body builds is a
machine word, so the file is legal and compiles.
```maxon
// --- runtime-file: Probe.maxon
module function probeWords() returns MachineWord
	var total = 0 as MachineWord

	for i in 0 upto 4 'eachIndex'
		total = total + (i as MachineWord)
	end 'eachIndex'

	var bits = total
	var runs = 0 as MachineWord

	while bits > 0 'eachRun'
		runs = runs + (bits and 1)
		bits = bits shr 1
	end 'eachRun'

	return runs
end 'probeWords'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-not-capture-a-mutable-local-in-a-closure -->
⛔⛔ **THE ONE MANAGED THING THE VALUE TYPE COLUMNS CANNOT SEE, AND IT IS NOT A `String`.** A closure is
`function`-tagged and `valueIsManagedHeap` declines that tag — rightly, because a NON-capturing closure is
a bare code address that owns nothing. A CAPTURING one allocates a refcounted ENVIRONMENT block, and the
cell a captured-and-reassigned `var` is promoted into lives inside it, so the tier acquires exactly the
refcount traffic R1 exists to forbid by a route the type of no value records.

⚠ The refusal is therefore asked at `markCapturingClosure`, the one door that records the capture, rather
than at the mint. The anchor is the closure literal, because the env and every cell under it exist only
because of it.
```maxon
// --- runtime-file: Probe.maxon
module function probeClosure() returns MachineWord
	var total = 0 as MachineWord
	let bump = function(x MachineWord) gives total + x
	total = bump(1 as MachineWord)

	return total
end 'probeClosure'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:5:13: a managed value of type 'function' is built in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-catch-an-unmanaged-error -->
⭐⭐ **THE SECOND CONTROL, OVER THE TWO DOORS A REFUSAL KEYED ON SYNTAX WOULD HAVE SHUT NEXT.** A caught
error and a `match` payload are the other two bindings no spelled type and no `let` reaches, and the rule
must admit both whenever what they bind owns no heap. The divide's error enum carries no payload and is
not boxed, so `e` is a tag in a register and the tier may catch it, discriminate it and act on it.

⚠ **THE DIVISOR IS A HOST READING BECAUSE A CONSTANT ONE NEVER REACHES THE HANDLER.** A literal zero is
E3103 at compile time and a literal non-zero elides the check, so neither would put the caught-error
binding under a case at all.

⚠ A NON-capturing closure belongs to this control's family for the same reason and is not written here: it
would need its own file, and the refusal above already states the line between the two.
```maxon
// --- runtime-file: Probe.maxon
module function probeCaught() returns MachineWord
	let ticks = __Raw.osTickCountMs()
	var answer = 0 as MachineWord

	try (100 / ticks) otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then answer = 1 as MachineWord
		end 'kind'
	end 'handle'

	return answer
end 'probeCaught'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: runtime-file-may-abort-the-process -->
⭐⭐ **THE TIER'S ONLY WAY OUT, AND IT IS AN EXIT RATHER THAN A PANIC.** A `panic` builds a message and
walks a stack, and both allocate — which the allocator cannot do while reporting that allocation has
failed. So a runtime body that has established it cannot continue leaves by the same door the Std-tier
builders use: a code, and the process ends.

⚠ **THE ROW ANSWERS NOTHING AND THE BODY STILL NEEDS A TERMINATOR.** `osExit` does not return, but a
block without a terminator is not a block, so the `return` below it is emitted and never runs — the
shape `RuntimeAbort.emitRuntimeAbort` already has one tier down.

⚠ The code is restated here rather than named across the boundary, because no name crosses it — the
restatement `runtime/Clock.maxon` makes of the FILETIME constants, for the same reason.

⚠ The probe is uncalled, so dead-function elimination drops the body before instruction selection and
nothing exits: what is under test here is the TABLE. The LOWERING is measured instead by spelling the row
inside a reached tier body, which reaches `ExitProcess` on x64-windows, `_exit` on arm64-macos,
`syscall 231` / `svc 94` on the two Linux lanes and `exit-with-code` on wasm32-wasi.
```maxon
// --- runtime-file: Probe.maxon
let ProbeAbortCode = 93

module function __probe_abort() returns MachineWord
	__Raw.osExit(ProbeAbortCode)

	return 0
end '__probe_abort'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: raw-intrinsic-refused-outside-the-runtime-tier -->
⭐⭐ **THE NEGATIVE CONTROL ON P2, AND IT IS THE HALF THAT MATTERS.** The positive cases above prove the
privileges are not EMPTY. Neither proves they are not UNIVERSAL — a compiler that let ANY file call
`__Raw.osTickCountMs` would pass both of them, and would hand every program a direct call to the raw
machine floor with no runtime between.

⚠ **THIS CASE CARRIES NO `// --- runtime-file:` SECTION ON PURPOSE.** It is an ordinary program, so it
is the one case in this file whose red today is its OWN red rather than the harness refusing a marker
it does not yet know: the same spelling answers E3004 now (`__Raw` is simply not a name any file may
write) and must answer E3152 after the tier lands. A case that needed the new marker to be RED could
not tell a missing privilege from a missing harness.

The wording is deliberate and does not say the name is undefined — `__Raw.osTickCountMs` IS defined,
and telling this author "no such function" would send them looking for a typo instead of telling them
the intrinsic exists and their file is not allowed to reach it.
```maxon
function main() returns ExitCode
	return __Raw.osTickCountMs() as ExitCode
end 'main'
```
```maxoncstderr
error E3152: <fragment>:3:15: call to '__Raw.osTickCountMs' from outside the runtime source tier: '__Raw' names a raw runtime intrinsic, callable only from a source file under runtime/
```

<!-- test: unlisted-raw-intrinsic-refused-inside-the-runtime-tier -->
**THE ROSTER IS A CLOSED TABLE, AND THE PRIVILEGE IS TO CALL WHAT IS IN IT — NOT TO WRITE `__Raw`.**
A tier that admitted any `__Raw.<anything>` from a runtime file would turn a misspelled intrinsic into
a link-time failure or, worse, a call to a symbol the emitted runtime happens to have.

⚠ The refusal is the EXISTING machinery, not a second one: `reservedCalleeReasonOf` classifies
`__Raw.nope` as `unknownCompilerIntrinsic` and the ordinary E3004 sentence answers. A second error
code for "an intrinsic of that name does not exist" would be the same rule numbered twice, which is
the disease one level up.
```maxon
// --- runtime-file: Probe.maxon
function probeNope() returns MachineWord
	return __Raw.nope()
end 'probeNope'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:15: call to undefined function '__Raw.nope': the '__' prefix names a compiler intrinsic, and no intrinsic of that name exists
```

<!-- test: runtime-file-may-not-name-a-managed-value -->
**R1 — THE RESTRICTION THAT IS NOT A STYLE RULE.** A managed local makes the reference-counting pass
emit `__mm_incref` / `__mm_decref` calls around it — into the very runtime this tier defines. The
result is not a link error but a circularity with no fixed point: the runtime function that manages a
`String` would be lowered with managed-value bookkeeping of its own, calling the function being
lowered.

⚠ The refusal is at the NAME, which is why the case binds a local rather than passing one: it must
fire before any later stage decides whether the reference-counting pass has anything to do, or the
legality of a runtime file would turn on which optimizations happened to elide its retains.
```maxon
// --- runtime-file: Probe.maxon
function probeManaged() returns ExitCode
	let s = "hello"
	return s.count() as ExitCode
end 'probeManaged'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:6: 's' has managed type 'String': a runtime source file may not name a managed value, because the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-may-not-declare-public -->
**R2 — THERE IS NO API SURFACE TO EXPORT.** The compiler reaches a runtime entry BY NAME, from its own
emitted code; nothing in user source ever names one, and nothing could — the names carry the reserved
prefix that no ordinary file may write. So `public` on a runtime declaration states a contract with a
caller that cannot exist, and the refusal says which of the two halves is wrong.

⚠ The refusal is a CEILING on visibility rather than a banned word, so its sibling below asks the same
rule of `export`. `module` is where the ceiling sits: the tier's own files still share with each other.
```maxon
// --- runtime-file: Probe.maxon
public function probePublic() returns ExitCode
	return 7
end 'probePublic'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3154: <fragment>:3:1: 'public' is not allowed in a runtime source file: runtime/ is loaded into every program, so a declaration visible outside the tier contests names with the programs it is linked into — the compiler compiling itself among them; 'module' is the widest visibility the tier's own file-to-file sharing needs
```

<!-- test: runtime-file-may-not-export -->
**R2's SECOND HALF, AND THE ONE THAT WAS MEASURED.** `export` mints GLOBAL visibility, so an `export`
declaration in a file the compiler loads into EVERY program it builds is a name offered to every one of
them — including to the compiler compiling ITSELF, where a `runtime/` alias and the compiler's own of
that name make each other ambiguous (E3063) and the self-compile stops.

⚠ `module` and `file` stay legal and must: `runtime/Word.maxon` declares its word roster `module`, and
the tier resolves those names across its own files. The rule is a CEILING, which is why one code and one
sentence answer for both halves.
```maxon
// --- runtime-file: Probe.maxon
export function probeExported() returns ExitCode
	return 7
end 'probeExported'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3154: <fragment>:3:1: 'export' is not allowed in a runtime source file: runtime/ is loaded into every program, so a declaration visible outside the tier contests names with the programs it is linked into — the compiler compiling itself among them; 'module' is the widest visibility the tier's own file-to-file sharing needs
```

<!-- test: runtime-file-may-not-declare-a-managed-field -->
**R1 ASKED OF A TYPE NOBODY NAMES.** A `String` FIELD binds no local and names no parameter, so the
value-level refusal above never sees it — and a runtime `type` holding one is exactly the circularity
that rule exists to forbid, deferred to whatever function first constructs the box.

⚠ The anchor is the field's TYPE and not its name, because the type is what the author has to change.
The refusal is asked at the one door every type a file spells comes through (`Parser.parseTypeReference`),
so a field, a return, a parameter and a cast target are one rule rather than four.
```maxon
// --- runtime-file: Probe.maxon
type ProbeBox
	var label as String
end 'ProbeBox'
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3153: <fragment>:4:15: managed type 'String' is not allowed in a runtime source file: the reference-counting pass would emit calls into the very runtime this tier defines
```

<!-- test: runtime-file-unreserved-declaration-is-still-module-scoped -->
⭐⭐ **THE NEGATIVE CONTROL ON THE ONE RULE THAT LETS A COMPILER-EMITTED CALL REACH THE TIER.** A runtime
entry point answers to no directory's module scope: `stdlib/Clock.maxon`'s `nowUnixSeconds` becomes a call
to `runtime/Clock.maxon`'s `__clock_now_unix_s`, from a different directory, and visibility has nothing to
refuse there because no file could have NAMED it — the callee is RESERVED, which is what makes the name
unwritable (`SemanticCheck.calleeVisibleFrom`).

⛔ **BEING RESERVED IS HALF THAT TEST, AND THIS IS THE HALF THAT MEASURES IT.** Keyed on the tier alone, the
exemption would make every declaration in `runtime/` callable from every file in the program — the tier is
loaded into all of them — so an UNRESERVED runtime declaration would become a global name in everything the
compiler builds, which is the contest E3154's ceiling exists to prevent. It stays module-scoped, and the
module is `runtime/`.

⚠ **RESERVED IS NOT THE SAME AS `__`-PREFIXED, AND ONE TIER ENTRY IS THE DIFFERENCE.**
`maxon_force_segfault` wears no prefix and is admitted by that same arm, because the DECLARATION door
reserves the word in the free-function name space (`specs/safety.md`). The rule the case below measures is
therefore *"a runtime declaration the compiler has not reserved stays module-scoped"*, and `probeHelper` is
one of those.
```maxon
// --- runtime-file: Probe.maxon
module function probeHelper() returns ExitCode
	return 7
end 'probeHelper'
// --- file: main.maxon
function main() returns ExitCode
	return probeHelper()
end 'main'
```
```maxoncstderr
error E3088: <fragment>:8:9: function 'probeHelper' is module-scoped and not visible from this directory
```
