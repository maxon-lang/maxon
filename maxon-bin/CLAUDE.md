## The compiler

One compiler builds this tree and it is written in Maxon: source `maxon-bin/`, binary
`maxon-bin/.maxon/maxon`, suite `specs/`. On Windows the binary is `maxon.exe`; commands below show
the Windows form.

⭐ **TWO SOURCE TIERS ARE READ ON EVERY COMPILE, AND BOTH LIVE AT THE CHECKOUT ROOT.** `stdlib/` is the
standard library; `runtime/` beside it is the language runtime — the code every program needs before
any of its own runs. The compiler locates `stdlib/` by walking UP from its own executable and reaches
`runtime/` as its sibling, so the two travel together everywhere: a release archive, an install tree, a
Docker image, a Homebrew prefix. A tree holding one without the other compiles nothing.

⛔ **A `runtime/` FILE IS NOT COMPILER SOURCE AND NEEDS ONE SELF-COMPILE, NOT TWO.** It is input the
compiler READS, so the first build already compiles against the edited file and carries it. That is the
opposite of `Compiler/Runtime/` below, which the compiler WRITES into every program including itself.
`scripts/self-compiles-needed.sh` answers for both; it does not watch `runtime/`, deliberately.

⭐ **FIVE FAMILIES ARE IN, AND THE ROOT THAT REACHES ANY OF THEM IS A CALL THE COMPILER EMITS.**
`runtime/Clock.maxon` holds `__clock_now_unix_s` and `__uptime_ms`, `runtime/ParallelBoundary.maxon` holds
`__parallel_boundary`, `runtime/FaultProbe.maxon` holds `maxon_force_segfault`,
`runtime/CpuParallel.maxon` holds `__cpu_count`, `__sched_max_active_workers` and
`__sched_processor_count`, and `runtime/Process.maxon` holds `__proc_pid` and `__proc_bg_priority`; the
matching `__Builtins` spellings lower to calls naming them, from whatever file wrote the construct.
⚠ **TWO MORE FAMILIES ARE IN AND NEITHER IS REACHED THAT WAY** — `runtime/SlabArena.maxon` and
`runtime/SlabRuntime.maxon`, whose roots are `StdOp.call` sites an INSTALLER mints. They owe the declared
reach edge below rather than a `__Builtins` spelling.
⛔ **THE PROCESS FAMILY'S THIRD ENTRY, `__proc_exe_path`, IS NOT IN AND IS BLOCKED RATHER THAN DEFERRED.**
It is a grow-and-retry loop that allocates and builds an `__ManagedMemory` answer, so as tier source it
would CALL other runtime entries. The blocker is no longer the usage scan (see `unreachedRuntimeTier`
below) but the CALL DOOR: `__mm_alloc`, `__mm_free`, `__managed_create` and `__managed_reserve` are
DECLARED in no source file — the compiler synthesizes their bodies as `StdOp` graphs — so
`Parser.requireCalleeIsNotReservedName`'s `signatures.declaresCallee` conjunct is false and a tier body
spelling one earns E3004. The allocator's own migration is what admits it.

⛔⛔ **THE TIER'S PROTECTION IS OVER BOTH DOORS A NAME CAN BE REACHED THROUGH, AND IT TAKES TWO REFUSALS.**
`Parser.requireCalleeIsNotReservedName` admits a reserved CALLEE, and
`Parser.requireFunctionValueNameIsNotReserved` a reserved name in VALUE position, only where
`Parser.fileMayUseReservedNames` holds — `runtime/` itself, `stdlib/Builtins.maxon` and
`stdlib/Testing.maxon`, **and any file this compile WROTE part of, which is a staged `*.maxtest` under
`maxon test`** — and in both doors conjoined with `declaresCallee`. An ordinary stdlib or user file calling
one earns E3004 and naming one as a value earns E3155, which is what keeps the visibility admit-list below
sound. The value door is cured at `requireNameIsUsableAsFunctionValue`, the one site both producers of an
author-named function value ask — a bare name READ and a function-backed enum case — so the rule needs no
list of syntactic positions. ⇒ The property is *"no source file outside the tier may CALL **or NAME** a
runtime entry"*, and the staged-file provenance is the standing exception to it on BOTH doors alike: under
`maxon test` an author's own file may do either.

⛔⛔ **ONE TIER ENTRY WEARS NO PREFIX AND SO CARRIES ITS OWN RESERVATION — `maxon_force_segfault`.** Its name
is the FRAME a backtrace prints (`specs/safety.md` asserts three of them), so it cannot be moved into the
`__` band a shape test recognises. `Parser.requireUnreservedName` refuses the DECLARATION of it by name, under
E2051 and beside the `self` rule, and `MmRuntime.ReservedCalleeReason.faultProbeEntry` refuses the CALL. The
declaration half is not tidiness: a second declaration of the bare name makes it CONTESTED across directories,
`Parser.declaredMethodName` then files the tier's own declaration as `runtime.maxon_force_segfault`, and the
bare call the compiler emits binds to the user's function — which does not fault. E4015 cannot report it,
because it tells the two sides apart by one having no source file and both are now parsed.
`specs/safety.md`'s `error.force-segfault-name-is-reserved` is the case. ⇒ **THE NEXT UNRESERVED TIER ENTRY
OWES THE SAME TWO DOORS.**

A runtime name is therefore never
classified `LibraryFacts.unreachable`, and the exemption is by PROVENANCE rather than by reachability: no
source call edge can earn a tier body, so a walk over them holds no evidence about whether one was BUILT
(`StdlibSource.classifyLibraryReach`). **Whether the program REACHES one is a different question and the same
walk does answer it** — `LibraryFacts.unreachedRuntimeTier`, below — and every pre-elimination pass reads
the two answers as ONE predicate, `LibraryFacts.bodyIsSwept`: an unreached tier body is built but never
lowered, guarded, scanned or call-checked, exactly like an unreachable stdlib body, and dead-function
elimination sweeps it. ⛔ **THAT IS WHAT KEEPS ITS `.rdata` OUT OF A PROGRAM THAT CANNOT REACH IT** — DFE
prunes functions and never `.rdata`, so a lowered-then-swept body would still leave its panic strings ahead
of the program's own literals (`specs/stdlib-loading.md`'s
`a-stdlib-modules-literals-cannot-reach-the-rdata-image` is the gate; the arena's five range-check strings
and its shift-count panic are what it caught).

⚠ **THE TWO arm64-macos force-segfault GOLDENS OWE A RE-MINT ON A MAC, AND THE DRIFT IS EXPECTED.** A
Mach-O runs only on macOS, so an x64 host reports `force-segfault-macos` and that lane's
`force-segfault-on-a-green-thread` NOT RUN and the harness correctly mints nothing for them; their committed
text still renders `func @maxon_force_segfault`, which the printer withholds now that the name is in
`libraryFunctions`. The drift is exactly that block and nothing else. It is the FIRST tier migration whose
entry was previously rendered in goldens — the clock's and the checkpoint's never were — so neither earlier
commit set a precedent for reading it.

⚠ **A `RuntimeUsage` BIT NO LONGER GATES A SOURCED BODY'S INSTALLATION, AND STILL GATES EVERYTHING ELSE.**
DFE decides whether the body survives, so `usesWallClock` and `usesUptimeClock` install nothing — but the
per-target hand-assembled `osReadWallClock` chunk, the POSIX clock floor and the Windows optional import
band are all still theirs. Retire a bit when its LAST consumer is gone, not when the body moves.
`usesParallelBoundary`, `usesFaultProbe` and `usesProcessId` are the three that qualified and all are
GONE: no body of theirs declares any dependency — no heap, no scheduler, no import, no `.data` word — so
the install guard was the only reader of each. `usesBackgroundPriority` is the near miss that stays: its
body declares nothing either, but the Windows OPTIONAL IMPORT band still reads it through
`IrModule.usesBackgroundPriority`.

⛔⛔ **A BIT NEVER LAYS OUT A `.data` WORD A STD BODY READS — THE WORD FOLLOWS THE BODY.**
`GlobalDataTable.layOut` runs in `buildBackend` AFTER dead-function elimination, and a runtime word pushed
as `DataReach.walked` is kept only if a SURVIVING function names it in a `globalAddr` — the label set the
prune collects in the same op walk that decides survival (`DeadFunctionSurvivors`). So
`__sched_max_active_workers` and `__sched_num_procs` are laid out exactly where
`runtime/CpuParallel.maxon`'s queries survive, and a query only a dead function asks lays out nothing
(`specs/runtime-source-tier.md`'s `a-word-only-a-dead-user-function-reads-is-not-laid-out`). A word read by a
hand-assembled chunk or the entry stub is `DataReach.declared` on that reader's own gate instead, because no
Std walk sees the reader; `GlobalDataTable.requireLaidOutCoversReferences` panics by label on a surviving
`globalAddr` the layout does not hold. `usesCpuCount` keeps the ordinary kind of consumer: the Windows
optional import band and the two hand-assembled `mrt_host_cpu_count` chunks.
⚠ **THE `.data` ORDER IS TWO SEGMENTS, EACH SORTED BY SIZE CLASS WITHIN ITSELF**: the user's globals first,
then the runtime's, in `BackendDispatch.dataSectionRoster` order (`GlobalDataTable.layOut`). Filtering never
reorders, so a program that reaches every word it names lays each out at the offset a gate would have given
it — but the roster order alone is NOT the `.data` order, because `layOut` sorts each segment largest slot
first. The segment split is what keeps a runtime word added anywhere from moving every user global of a
narrower size class.

The fault probe's family predicate `isFaultProbeRuntimeCallee` outlived its bit, because
`MmRuntime.reservedCalleeReasonOf` still routes the call refusal through it; it moved there with the two
constants and `Compiler/Runtime/FaultProbeRuntime.maxon` is deleted, having nothing left to build.

⛔⛔⛔ **THREE PREDICATES ASKED "IS THIS THE RUNTIME'S?" THROUGH A PROXY, AND THE TIER FALSIFIED ALL THREE.
`LibraryFacts.runtimeTier` IS THE PROVENANCE FACT, AND A FOURTH SITE MUST READ IT RATHER THAN INVENT A
PROXY OF ITS OWN.** Where a family's body is WRITTEN is not something a program can observe, so moving one
must move no verdict. Two proxies said *"the module does not declare it"* and one said *"it was not parsed
from source"*; a migrated entry point falsifies each:

- `SemanticCheck.calleeYields` — answers "yields" for a tier callee and records NO edge. E3073's
  unknown-callee fallback is what admits a CPU-bound `async` target; with the edge recorded the closure
  hands the caller the tier body's own non-yielding answer and refuses a legal spawn.
- `SemanticCheck.calleeShowsAnEffect` — answers "an effect", and records no edge. Its roster is the whole
  positive side, so an unlisted runtime entry MUST read as effectful; otherwise a pure `module function`
  helper in a tier file propagates purity into a user caller and a discarded call earns E3064 on a legal
  program.
- `TargetPrinter.isRuntimeFunction` — tier membership is a DISJUNCT that stands on its own, because a name
  band exists to identify a body nothing declared and a tier member has a path AND membership to argue from.
  Keyed on the band as well, `maxon_force_segfault` — which can never take a prefix — would carry a
  green-thread stack guard and be marked `FunctionCodeChunk.asyncPreemptible` (which is `mightGuard`, NOT
  `emitGuard`, so a 0-byte frame does not escape it; arm64 has no frame-size exemption at all). It also drives
  `InlineLeaves.splicingWouldWidenTheSafePoint`.
  ⚠ **THE STANDING LIMIT THIS BUYS: EVERY DECLARATION IN A `runtime/` FILE IS EXEMPT FROM THE GREEN-THREAD
  STACK GUARD, an unreserved `module function` helper included.** That is intended — they are compiler-authored
  frames inside the margin `GtRuntime.gtStackGuardMargin` is sized for — but a tier helper that RECURSES on
  program-sized data would be the `__probeDeep` SIGSEGV with a compiler author instead of a user. A tier body
  that recurses needs a guard this predicate will not give it.

⚠ **EACH ARM SITS AFTER ITS OWN WALK'S ROSTER**, which still decides a name it lists. ⛔ And widening
`isRuntimeFunction` does NOT reopen the `__probeDeep` SIGSEGV its header records: that was a USER function
in `stdlib/Builtins.maxon` reaching the scheduler's exemption, and `runtime/` is a compiler-loaded cone no
user file can declare or even name into. The set is closed; the name test is untouched.

⛔ **AND NOTHING IN THE SUITE CAN SEE THE LAST OF THE THREE.** The guard is emitted at byte level rather
than as a TargetOp (`X64Backend`), and `asyncPreemptible` reaches only `__symtable`, whose sole reader is
`__gt_preempt_safe` — a scheduler-internal decision with no program-visible result. `schedPreemptCount()`
counts preemptions HONOURED, so a zero is indistinguishable from a monitor that never asked, and no program
can arrange to be stopped inside a four-instruction runtime body. ⇒ **a golden CANNOT catch a regression
here; read the predicate.** Closing this needs a channel that renders per-symbol safe-point provenance.

⚠ **A COMPILER-EMITTED CALL INTO THE TIER IS EXEMPT FROM MODULE VISIBILITY, AND THE EXEMPTION IS AN
ADMIT-LIST.** `SemanticCheck.calleeVisibleFrom` admits a callee that is RESERVED and is declared under
`runtime/`. Reserved is `MmRuntime.isCompilerInternalCallee`, which is the `__` band OR a name the
declaration door reserves outright — today `maxon_force_segfault`, and it belongs in the admit-list for the
band's own reason: no author can have written it either. ⇒ **WHAT KEEPS THIS SOUND FOR AN UNPREFIXED ENTRY
IS THE DECLARATION DOOR, so a name admitted here owes a refusal in
`Parser.requireFreeFunctionNameIsNotTheFaultProbe` or its successor.** A runtime file's OTHER unreserved
declarations stay module-scoped, which
`specs/runtime-source-tier.md`'s `runtime-file-unreserved-declaration-is-still-module-scoped` measures.

⭐⭐ **BUILT AND REACHED ARE TWO QUESTIONS FOR THE TIER, AND EVERY PRE-ELIMINATION PASS ASKS THE SECOND.**
Every tier body is built, in every program; the scan credits a body's calls only where some root can reach it
(`LibraryFacts.unreachedRuntimeTier`, filed by `StdlibSource.classifyLibraryReach` out of the SAME
from-`main` walk that files `unreachable` — one derivation, two answers, so they cannot disagree). Without
that split, a CALL inside a tier body sets its family's bit in every program DFE sweeps the body out of,
which is rule 1 ("vocabulary does not ship ahead of its consumer") failing open.
`specs/runtime-source-tier.md`'s three-case group is the channel: the unreached program, the control that
reaches the entry, and the program that reaches a DIFFERENT family.

⛔⛔ **OVER-APPROXIMATING IS SAFE AND THE OTHER DIRECTION IS A LINK FAILURE.** A bit set for a swept body
costs BYTES and nothing else — never a wrong answer and never a refusal, because
`SemanticCheck.blameTargetErrorsIn` skips the target gate inside library code and usage-installs /
DFE-removes is a one-way composition. A bit UNSET while the body ships is a body calling into a runtime
floor, a host chunk or an import band nothing installed. So a name enters the set only on the
walk's positive evidence, every unmodelled edge resolves to "reached", and
`DeadFunctionElimination.requireUnreachableLibraryStayedDead` panics on the unsafe direction.

⚠ The reach answer is CLOSED over runtime→runtime calls for free: a tier body is in the merged Maxon
module with real ops, and `markMaxonCalleeEdge` filters no callee by provenance. `runtime/SlabArena.maxon`
is the family that exercises it — `__slab_arena_alloc_chunks` calls `__slab_arena_new` calls
`__slab_assert_os_alloc`, and its module-scoped helpers are reached the same way.

⛔⛔ **AND IT IS CLOSED ONLY OVER CALLS SOME ROOT CAN REACH, WHICH THE SLAB ARENA'S CANNOT BE.** Every other
family is kept alive by a call the PARSER mints from user source (`__Builtins.cpuCount()` ⇒ `__cpu_count`),
which is an ordinary edge this walk finds. The arena's callers are `StdOp.call` sites an INSTALLER mints
into the appended band, long after the walk, into a module that is not the Maxon one — so the walk holds no
evidence at all and would file every arena entry `unreachedRuntimeTier` while its body ships, which
`DeadFunctionElimination.requireUnreachableLibraryStayedDead` panics on. The edge is therefore DECLARED:
`StdlibSource.runtimeTierFileIsCompilerCalled` names the tier FILES the compiler itself calls into, out of
the same one-walk derivation, and `classifyLibraryReach` reads it. **BY FILE rather than by NAME**, because
an installer's call lands on an entry point whose body then reaches that file's module-scoped helpers — and
the short-circuit path hands `classifyLibraryReach` an EMPTY reached set, so there is no walk there to close
a name roster's difference. **UNCONDITIONAL AT THE WALK**, because the minter's gate — `usesHeap`, which
`installSlabRuntime` returns without — is not final until after `scanRuntimeUsage`, and a name filed
unreached while its body ships is a link failure. **SETTLED ONCE THE GATE IS**:
`LibraryFacts.withCompilerCalledTierUnreached`, called from `compileToCodeResult` before lowering, files
every name of those files `unreachedRuntimeTier` in a program the installer will mint no call for, so
their bodies lower nothing there (`bodyIsSwept`). ⇒ **A NEW TIER FAMILY THE COMPILER REACHES BY MINTING A
CALL OWES A LINE IN THAT ROSTER, AND ITS MINTER MUST RIDE THE SAME GATE**; the panic above names the missing
body either way.
⭐ **`runtime/SlabRuntime.maxon`'s THIRD, FOURTH AND FIFTH ENTRY POINTS NEEDED NO NEW LINE**, because the
roster is keyed by FILE and that file was already on it — which is the whole reason it is keyed that way.

⛔⛔ **`runtime/DebugAgent.maxon` IS THE FIRST ALWAYS-REACHED TIER FAMILY, SO EVERY BYTE IT COSTS IS
CHARGED TO EVERY PROGRAM.** It is rooted by the call the entry stub mints (`DeadFunctionElimination`'s
`debugAgentRoots`, its own list because `compilerOwnedRoots` refuses a name a declaration carries and these
four are declared), emitted by default on x64-windows, and dark at run time unless `MAXON_DEBUG` names it a
control segment. ⇒ **NO GUARDED CONSTRUCT MAY APPEAR IN IT.** A ranged cast or a variable shift mints a
panic string, and a panic string in this family lands in the `.rdata` of every program the compiler builds
— which is how it first tripped the stdlib-loading rdata gate. Test a flag by mask, walk bytes over the
value, and build a needed literal with `storeWord`s into scratch rather than spelling it. `--no-debug-agent`
is the opt-out, and it is the one debugging flag that changes the executable (`--no-debug-info`'s byte
identity stands, gated by `tests/debug/byte-identical-debug-info`).

⚠ **ITS GEOMETRY IS WRITTEN TWICE AND PINNED, ON THE SLAB RUNTIME'S TERMS.**
`Compiler/Debug/DebugControlLayout.maxon` owns the control segment's layout, the tier restates every figure,
and `checkDebugAgentGeometry` reads the tier's own constants back out and compares them. So **a pinned
figure, or a `__Raw` row's arity, takes a STAGED build** — tier file back to the current emitter's values →
build C1 → restore → build C2 with C1 — exactly as the slab state region does above.

⛔ **TWO x64-windows FACTS THE AGENT RESTS ON, both silent when wrong.** Windows hands a breakpoint trap a
`CONTEXT.Rip` already backed onto the `int3`, the opposite of the POSIX convention, so the delivered pc IS
the breakpoint's address. And the trap thunk saves `rsi`/`rdi` by hand: Maxon's own convention treats them
as volatile (`X64PrologueEpilogue.calleeSavedOrder` is rbx and r12–r15) while Win64 requires a handler to
preserve them, and nothing in a run reports the difference.

⛔⛔ **THE AGENT IS A SECOND SUSPENDER OF THREADS, AND `__sched_preempt_ext_lock` IS WHAT MAKES THAT
SOUND.** `__sysmon` is no longer the only party that calls `SuspendThread`: every stop suspends every
other machine, and the agent holds that window across the whole park, so a suspend and `__gt_exit_process`
still exclude each other. The order is the window, then `__sched_lock`, then the suspends — never the
other way round, which is what keeps it free of a cycle with `__gt_preempt_m`. ⇒ **A THIRD SUSPENDER, OR
A ROAD THAT BLOCKS WHILE HOLDING `__sched_lock`, BREAKS BOTH ARGUMENTS AT ONCE.**

⚠ **FIVE SCHEDULER `.data` WORDS ARE LAID OUT IN EVERY x64-windows PROGRAM BECAUSE THE AGENT READS
THEM** — `__sched_lock`, `__sched_tls_teb_offset`, `__sched_allm`, `__sched_preempt_ext_lock` and
`__ds_base` — so their gates in `schedRuntimeGlobals` carry `usesDebugAgent` beside `usesGt`. A zero in
the TEB-offset word is how the agent recognises a program with no scheduler, and `__ds_base` is read at
the stop rather than at attach because `__dbg_init` runs ahead of `__ds_init`.

⛔⛔ **E3153 IS COMPLETE ON BOTH SIDES, AND THE VALUE SIDE IS COMPLETE BECAUSE IT IS ASKED OF THE VALUE
RATHER THAN OF THE BINDING FORM.** `parseTypeReference` catches every type a runtime file WRITES.
Everything else is caught off the parser's value type columns: `Parser.noteManagedValueInRuntimeSource` is
asked at `mintValue` and `retypeValue` — the only two writers of those columns, so a `for` element, a
`match` payload, a caught error and an UNNAMED temporary all pass one of them — and the first offence is
held on the parser and refused at the end of `parseModule`, where every function of the file including its
lifted closures and default helpers has been built. ⚠ **IT IS RECORDED AND REFUSED LATER BECAUSE NEITHER
WRITER MAY THROW**: both are called from expression emitters that are not `throws`, and the position is
the DEFINING OP's span, which does not exist yet at the mint and is resolved from
`valueOrigins`/`opRanges` at the refusal. `declareInitializedBinding` and `bindParameters` still ask
first, for a sharper sentence naming the binding — never for cover.
⭐ **THE ONE MANAGED THING THE TYPE COLUMNS CANNOT SEE IS A CAPTURING CLOSURE**, because
`valueIsManagedHeap` declines a `function` tag — correctly, a non-capturing closure owns nothing — while a
capturing one carries a refcounted env block and the PBR-2 cells live inside it. That is its own fact with
its own single door, `markCapturingClosure`, and the refusal is asked there.
⇒ **A NEW WAY FOR A VALUE TO ACQUIRE HEAP THAT IS NOT A TYPE-COLUMN WRITE OWES THE SAME TREATMENT**: one
door that records the fact, and a note on it.

Four doors are still standing open rather than shut:

- **`osThreadCpuTicks` is the one `__Raw` row no spec case spells at all**, as are `reserveRawScratchSlot`'s refusals.
  `osCpuCount`, `schedNumProcsAddr` and `schedMaxActiveWorkersAddr` are the cpu-parallel family's, and
  `builtins-cpu-parallel.md`'s two `-body-is-runtime-source` cases render the bodies they lower to;
  `osGetPid` and `osEnterBackgroundPriority` are the process family's, rendered by
  `process-id.md`'s `pid-body-is-runtime-source` and `process-background-priority.md`'s
  `priority-body-is-runtime-source`.
  `osTickCountMs`, `osReadWallClock`, `scratch` and `loadWord` are the clock family's, and
  `builtins-clock.md`'s `wall-clock-body-is-runtime-source` renders the emitted body the last three lower to;
  `storeWord` is the fault probe's, and no golden renders that body — what measures it is a LIVE fault, in
  `specs/safety.md`'s three backtrace cases. `osThreadCpuTicks` waits on `__thread_cpu_ticks`, which cannot
  move until a `__Raw` row names the current-GT read its green-thread arm makes.
  ⭐⭐ **THE SLAB ARENA'S PAGE LAYER IS THE FIRST FAMILY WHOSE VOCABULARY LANDED AHEAD OF IT, AND THE
  CONSUMER HAS NOW ARRIVED.** `loadByte`, `storeByte`, `atomicAddWord`, `atomicCas`, `memFill`, the five
  page rows (`osAllocPages`, `osReservePages`, `osCommitPages`, `osDecommitPages`, `osFreePages`), the three
  `osLock*` rows and `osExit` landed WITHOUT a family, because once the tier supplies allocation a bad tier
  file breaks `C1` — the compiler `C2` is built with. `runtime/SlabArena.maxon` is that consumer: EIGHT of
  the family's nine entry points (`__slab_assert_os_alloc`, `__slab_arena_new`,
  `__slab_arena_alloc_chunks`, `__slab_arena_free_chunks`, `__slab_arena_of`, `__slab_arena_scavenge`,
  `__slab_arena_map_ensure`, `__slab_arena_map_set`), so the reserve/commit/decommit rows, `memFill`,
  `osExit` and the two address rows below are now MEASURED through a reached body rather than at what a
  probe proves.
  ⭐⭐⭐ **THE OBJECT LAYER IS WHERE THE TIER'S BOUNDARY WAS FINALLY STATED, AND IT IS ONE SENTENCE:
  A FAMILY'S BODIES CAN BE TIER SOURCE IFF THE BUILDER THAT EMITS THEM IS A CONSTANT FUNCTION OF
  `RuntimeUsage`.** Tier source is compiled once and reads no usage record, so a builder whose OUTPUT varies
  with the record has no tier spelling at all — not a harder one. It explains every outcome so far:
  `SlabArena` moved because `installSlabArena` reads no usage; the OS-direct pair moved because it takes no
  build-time argument; `__slab_state_base` was one `if` away and was therefore a LOWERING rung rather than a
  port; `__mm_alloc` is permanently a builder because its ARITY forks on `--debugstream`; and
  `ManagedMemoryRuntime` is permanently a builder because its function LIST is a function of the program's
  types.
  ⇒ **THE COROLLARY, AND IT IS THE PERMANENT SHAPE OF THE SPLIT: the allocator's MECHANISM is constant and
  portable, and its INSTRUMENTATION and SPECIALISATION are a function of the program and a builder's job
  forever.**
  ⛔ **`zeroed` IS NOT ON THAT LIST AND NEVER WAS — IT NEVER TOUCHES `RuntimeUsage`.**
  `installSlabRuntime` passes a LITERAL to each of the two allocation doors (`__slab_alloc` zeroes,
  `__slab_alloc_raw` does not), and a build-time argument that is a literal per ENTRY POINT is one tier
  source simply spells — as one Maxon helper with a `bool` parameter, which is a cost and not a blocker.
  ⭐ **THE ALLOCATOR READS NO USAGE BIT AT ALL.** Its sharding, its lock and its traffic columns are
  compiled into every heap program, and whether a second thread exists is a RUN-TIME word in the slab's
  state head (`SlabRuntime.SlabStateSchedulerOffset`). The one build-time fork is the TARGET's
  `TargetFacilities.machineModel`: on wasm the walk to "which processor am I" is a constant because the
  backend has no `tlsSlotLoad`. That fork is what still keeps the doors out of the tier.

  `runtime/SlabRuntime.maxon` holds FIVE of `SlabRuntime.maxon`'s fourteen entry points: the OS-direct pair
  `__slab_os_direct_alloc`/`__slab_os_direct_free`, the whole of the above-32 KiB road, plus
  `__slab_state_base` and the metadata slab's `__slab_meta_alloc`/`__slab_meta_free`. NINE of the rest are
  emitted DIFFERENTLY per TARGET off the machine model — the TLS read that answers *"which P am I"* is a
  constant on wasm. `__slab_rounded_size` is blocked a different way: it shares `emitSlabClassIndex` with
  `__slab_alloc`, so porting it alone would be a second spelling of one walk.
  ⛔⛔ **AND `__slab_span_destroy` IS BLOCKED THAT SAME WAY, WHICH IS NOT WHERE ITS BLOCKER WAS PREDICTED.**
  It reads no usage record and every callee it names is now tier source, so the closure argument really does
  reach it — but it and `__slab_refill` compute a span's chunk run from the same packed class geometry, and
  the refill stays a builder. `emitSpanChunkCount`'s own header is the rule: the CUT and the DESTRUCTION must
  agree TO THE CHUNK, or a span is released one chunk short and a chunk stays claimed forever. ⇒ **THE
  SECOND-SPELLING TEST IS A SEPARATE GATE FROM THE BUILD-TIME-ARGUMENT ONE AND IS ASKED AFTER IT.**
  ⚠ **THE STATE REGION'S GEOMETRY IS WRITTEN TWICE AND PINNED AT EVERY SEED.** The tier file cannot read the
  class ladder, so it restates the head layout, the class count and the shard count and derives the region's
  size itself; `checkSlabRuntimeGeometry` reads each of those constants BACK OUT OF THE TIER SOURCE — by name,
  through `ProgramSignatures.integerConstantIn`, folded by the compile that is compiling it — and compares it
  against the emitter's, on every compile of every allocating program. `checkSlabArenaGeometry` does the same
  for the page layer, on every compile of every program. Reading the tier's own value is what makes the
  comparison a statement about the two TREES; a check over an expected value declared in the emitter's own
  file is a pin against itself, and a stale tier file changes nothing it can see. Without it a regenerated
  ladder is a request one chunk too small — an mcache running off the end of its run, silently.
  **A RESTATED DERIVATION OWES A PIN.**
  ⚠ **AND CHANGING A PINNED FIGURE TAKES A STAGED BUILD — NO SINGLE BUILD CAN MOVE ONE.** The pin compares
  the BUILDING compiler's emitter constant with the tier file on disk, so a tree where the two differ is
  refused by the compiler you would use to build it. Set the tier file back to the current emitter's
  values → build C1 → restore the tier file → build C2 with C1. Growing the state region (adding a table,
  widening one) is this, every time.
  ⛔ **TIER SOURCE MUST NOT DISCARD A DECLARED CALLEE'S RESULT.** A `_ = f(…)` on a callee the tier
  DECLARES makes every compile of every program build the whole-program effect-free summary, to decide
  whether the discard is legal. Consume the value — fold it into the figure being returned, or test it —
  rather than throwing it away.
  ⛔ **THE REVERSE MAP'S READ SIDE IS NOT AN ENTRY POINT AT ALL, AND THAT IS NOT DEBT**: `__slab_free`
  splices the walk INLINE (`SlabArena.emitSlabArenaMapGet`), because a frame around four loads and three
  tests is overhead on the path of every free in the language. It had a behind-a-frame twin for as long as
  something replayed a queue of slots whose spans it did not know; each slot now carries its span, so the
  inline splice is the only spelling and porting it would be a SECOND one. The atomics are still spelled only by UNREACHED probe cases, and the caveat below is
  theirs: **WHAT A PROBE CASE PROVES STOPS AT THE PARSER AND THE Maxon→Std LOWERING** — the probe is
  uncalled, so dead-function elimination drops the body before instruction selection and no lane's isel is
  consulted.
  ⭐⭐ **`osLockInit` IS OUT OF THAT CLASS ON ALL FIVE LANES.** `__slab_state_base` spells it
  UNCONDITIONALLY and every allocating program reaches it, so the row is now MEASURED through a reached tier
  body: `InitializeCriticalSection`, `mrt_host_lock_init`'s futex build on the two Linux lanes, a RECURSIVE
  `pthread_mutex_init` on arm64-macOS, and nothing at all on wasm32-wasi. `osLockEnter`/`osLockLeave` are
  still spelled from tier source by no reached body — the BUILDERS emit them, which is a different road — so their isel evidence is the scheduler's and not the tier's.
  ⭐⭐ **AND THE ARENA'S TWO `.data` WORDS ARE ADDRESSED BY ROWS OF THEIR OWN** —
  `slabArenaListAddr` and `slabArenaMapL1Addr`, lowering to a `globalAddr` on
  `SlabArena.SlabArenaListLabel`/`SlabArenaMapL1Label`, and laid out as `DataReach.walked` words exactly
  where a surviving arena body names them. **THE FAMILY CARRIES NO USAGE BIT AND NO INSTALLER AT ALL**: every
  `__slab_arena_*` entry is tier source, so there is no builder-built body for a bit to gate and nothing for
  an installer to mint. What remains at the install site is the geometry check, which therefore runs on every
  compile rather than only where a program allocates — a check gated on an install is a check that reports a
  breakage to whoever comes next.
  ⛔⛔ **THE FAMILY HAS NO DISCOVERED ARM, AND A PREFIX ARM WOULD BE WORSE THAN REDUNDANT.** Its
  entry points are tier SOURCE, so its bodies are in the walked Maxon module and call each other; no file
  outside the tier may name one, so a `__slab_arena_*` callee the walk can SEE is always the family talking
  to itself. ⇒ **once a family's bodies move into the tier, its prefix arm in `recordCallUsage` stops being
  evidence about the PROGRAM** — the clock's, the cpu-parallel pair's and the process family's are the
  precedent to re-read, not to copy.
  ⭐⭐ **`osExit` IS THE TIER'S ONLY WAY OUT, AND IT IS AN EXIT RATHER THAN A `panic` BECAUSE A PANIC
  ALLOCATES.** Building a message and walking a stack are both heap work, which the allocator cannot do
  while reporting that allocation has failed — so a tier body that cannot continue names a code and ends
  the process, the shape `RuntimeAbort.emitRuntimeAbort` emits one tier down. Its facility is `none` for a
  reason no other row's is: every supported lane lowers `StdOp.osExit` unconditionally, because it is the
  floor under every `panic` and every range check, so there is no lane to withhold it. It is also the one
  row MEASURED on all five through a REACHED tier body rather than left at what a probe proves —
  `ExitProcess` on x64-windows, `_exit` on arm64-macos, `syscall 231` / `svc 94` on the two Linux lanes,
  and `exit-with-code` on wasm32-wasi, where the program really did end at 93.
  ⛔ **`memcpy` IS NOT A ROW AND MUST NOT BECOME ONE.** The tree has no `memcpy` Std op; bulk copy is a
  hand-built loop over word and byte chunks, which in tier source is ordinary Maxon over the accessors.
- **`ownFrame` and `splicedAtEverySite` are the two rows that are DIRECTIVES rather than operations, and
  they are opposites.** Neither appends a Std op; each sets a fact about the function that spells it, and
  `Parser.recordFrameDirective` is the one writer of both — which is also where spelling BOTH earns
  **E3157**, because there is no body they can both be true of.
  - `ownFrame` sets `IrFunction.keepsItsOwnFrame`, which `InlineLeaves.functionShape` refuses to splice.
    Without it a tier body small enough to inline is spliced into every call site and then swept, and a
    runtime entry whose whole product is a FRAME ceases to exist. It is the checkpoint's, and
    `builtins-parallel-boundary.md`'s `checkpoint-body-is-runtime-source` renders the body it protects —
    though a ```RequiredRuntime block pins a body never-inline for its own compile, so what actually guards
    the rule is the unchanged `call __parallel_boundary` in every other golden.
  - `splicedAtEverySite` sets `IrFunction.mustBeSplicedAtEverySite`: the inliner must splice the body into
    every call site whatever its size, so a family a BUILDER used to splice inline can live in tier source
    and still be emitted the way the builder emitted it.
    ⭐⭐ **IT OVERRIDES THE COMPILER'S COST RULES AND MAY NEVER OVERRIDE A CORRECTNESS RULE.** Waived:
    `InlineLeaves.MaxInlinedLeafOps`, the called-once pressure budget, the inline-frame-record rule (a
    builder's spliced code never had a frame record either), and the leaf rule's *"no call of any kind"*
    for ONE edge — a call into another body that declares the row, spliced out callees-first before this
    body is copied anywhere. Still refusing: `splicingWouldWidenTheSafePoint`, `reassignedParamMask != 0`,
    `needsGreenThreadStackGuard`, `keepsItsOwnFrame`, the `isUnsupportedInInlineBody` roster, and a golden
    request for the body itself.
    ⛔⛔ **AND A REFUSAL IS NEVER SILENT.** `InlineLeaves.requireAlwaysSplicedBodiesAreGone` reads the
    SURVIVING module in `BackendDispatch.buildBackend` — beside `assertCallsMatchCalleeArity`, after the
    last splice round and after the prune — and reports **E3158** at the body's declaration, naming the
    reference that survived and the rule that refused. A CYCLE of declared rows is one of those reasons,
    detected exactly rather than capped. Asked there because a check inside the pass can only see the sites
    the pass reached, and the next narrowing of an optimization would take it away.
    ⇒ **PORTING A FAMILY ON THE STRENGTH OF THIS ROW MEANS ASKING WHO CALLS IT.** A tier body spliced into
    code the compiler does NOT own is `splicingWouldWidenTheSafePoint`'s refusal, so a primitive
    `InlineManagedPrimitives` expands into USER functions — `emitSlotAddr` and the four fast arms around it
    — has no tier spelling at all. `emitElementBits` / `emitBitsToBytes` / `emitElementByteLen` are reached
    only from builder-emitted `__`-band bodies and do not hit that wall.
- **`__Raw.scratch` is the one door that materializes a frame ADDRESS in a register in a GT program, and
  the gate that protects the other such door does not cover it.** `PromoteStackRecords` promotes NOTHING in
  a program running green threads, because `__gt_stack_relocate` frees the old pages and a promoted address
  would dangle; `__Raw.scratch` reaches `TargetOp.leaRegSlot` by a different route and no gate asks. It
  cannot fire today — the guard precedes the `lea`, an async stop cannot relocate (`GtRuntime`), and the
  shim window is not a safe point — but a tier body holding a `scratch` address across a guarded call is
  the bug it becomes.
  ⛔ **IT ALSO HAS NO wasm LOWERING AND THE TABLE CANNOT SAY SO.** `StdOp.stackRecordAddr` panics in
  `StdToWasm.emitBodyOp` — a wasm local has no address — and no `HostFacility` row names an addressable
  frame, so the row answers `none` and a REACHED tier body spelling it dies in that backend. MEASURED. Every
  probe that spells it is unreached, which is why the suite is green there.
- **A `__Raw` row's host facility reaches no refusable site.** `maxonOpCalleeKind` answers `noCallee`,
  so `LibraryFacts.substrateEntries` never sees one, and a lane without the op reaches instruction
  selection instead of E3104. A substrate-entry row per op is what closes it.
  ⚠ **FOUR ROWS NAME A FACILITY A SUPPORTED LANE DOES NOT PROVIDE** — `osCpuCount`, `osGetPid`,
  `osEnterBackgroundPriority` and `osThreadCpuTicks`. For the first three what refuses the wasm program is
  the CALLEE route (`TargetFacilities.calleeHostFacility`: the `__cpu_` band, the `__proc_` band, and
  `ProcessBackgroundPriorityName` by name), not this one. The fourth is spelled only by an UNREACHED probe
  body, which dead-function elimination removes before instruction selection, so nothing exercises its route
  at all; a REACHED tier body spelling it reaches instruction selection with nothing said.
  ⭐⭐ **THE LOCK ROWS' GAP WAS TWO GAPS AND BOTH ARE SHUT: `HostFacility.hostMutex` IS `true` ON ALL FIVE
  LANES.** On wasm32-wasi each of the three has its own arm in `StdToWasm.emitBodyOp` emitting NOTHING — a
  component has one thread, so exclusion is already total and the empty lowering is the honest translation,
  the same shape `osDecommitPages` gets there. That is `pageMemory`'s standard of proof (*"every one of the
  ops has an arm, which is what a `true` promises"*) and not `terminalDetection`'s; a threaded wasm lane
  falsifies the premise and owes three real bodies before the row may stay.
  ⛔ On the three POSIX lanes the `mrt_host_lock_*` chunks ride `PosixRuntime.posixUsesHostLock`, a gate
  SPLIT OFF `posixUsesHostObjects` rather than a widening of it: the lock's producers are the scheduler, the
  DebugStream ring and — widest — every program that allocates, and putting that on the host-object gate
  would drag `mrt_host_proc_reap` (a `wait4` child reaper) and `mrt_host_wait_close` into every
  single-threaded POSIX program that allocates one box. `posixReadsEnvironment` is the precedent: a union
  over the producers of ONE op, split off for exactly this reason. ⇒ **A GATE NARROWER THAN ITS OP'S
  PRODUCER SET IS A LINK FAILURE RATHER THAN A DIAGNOSTIC** (MEASURED one facility over as
  `arm64ResolveCallFixups: bl to unknown function 'mrt_host_env_read'`), **and a `hostMutex` `true` is only
  as good as that union.**
  ⚠ **`HostFacility.pageMemory` IS `true` ON ALL FIVE LANES, MEASURED RATHER THAN ASSUMED** — including
  wasm32-wasi, where `memory.grow` is the whole page API, a reserve IS an alloc, and a decommit or a free
  emits nothing. `targetProvidesFacility` spells wasm32-wasi as its OWN BLOCK rather than reaching the
  fallthrough `false`, which is what lets that row be stated at all.

- **Build it:** `./maxon-bin/.maxon/maxon run build` at the repo root. The root `maxon.maxtasks`'s
  `build` task delegates to `maxon-bin/maxon.maxproj`, whose `build` target is where the git-derived
  version comes from; `cd maxon-bin && maxon build` is the same build said from inside. ⛔ **`maxon build
  maxon-bin` from the root is a PATH build** — the root holds no `.maxproj`, so the word is a path: it
  compiles the same sources and stamps NO version, which is what the seed rule below relies on and what
  you keep away from the slot.
- **Get a compiler to build it WITH:** `scripts/fetch-seed.sh` places the latest release's binary at
  `.bootstrap/maxon.exe`, which you run directly when the slot is empty. **Re-run it after every
  release:** the stdlib may call a `__Builtins` intrinsic once a published release has it, so an older
  seed fails the first build with E3004. Maxon compiles Maxon, so there is no second
  implementation here — a previous build of this compiler is the only thing that can build it.
  ⛔ **A DECLARATION NO PUBLISHED RELEASE KNOWS IS REFUSED BY EVERY SEED THERE IS** — E2015 on a
  `__Managed*` entry this tree adds, which no re-fetch can cure. `scripts/build-from-seed.sh` stages
  `scripts/seed-shim/` over the first build for exactly that; `docs/RELEASING.md` owns the rule.
  ⛔ **NAME THE OUTPUT WHEN YOU BUILD WITH THE SEED, ALWAYS:**
  ```
  ./.bootstrap/maxon.exe build maxon-bin --output=maxon-bin/.maxon/maxon
  ```
  **A SEED OLDER THAN NAMED MANIFEST TARGETS READS `maxon-bin` AS A PATH, NOT A TARGET**, and a path
  build picks its own output name. MEASURED with the v0.1.0 release as the seed: it wrote
  `maxon-bin/Compiler/BorrowCheck.exe`, left the slot EMPTY, and **exited 0** — so the next command is
  a bare `127` about a compiler that was never written. `scripts/build-from-seed.sh` passes `--output=` for this reason;
  `CONTRIBUTING.md` spells it too.
- **Run the suite:** `./maxon-bin/.maxon/maxon.exe spec-test`.
- Exit code **101** means a memory leak was detected.
- There is **no `maxon clean`**.

> ### ⭐ A SELF-REBUILD RENAMES ITS RUNNING IMAGE OUT OF THE SLOT, THEN WRITES INTO THE EMPTY SLOT
>
> A compiler cannot overwrite its own running image (**E6002**), and a half-written slot is a
> compiler that answers as though it were whole. So a compiler rebuilding its own slot RENAMES its
> running image to `maxon-bin/.maxon/maxon.previous` — an OS will not let a running executable be
> deleted, but will let one be renamed — and its `.mxdbg` travels with it. ⭐ **THE RENAME HAPPENS ONLY
> AFTER THE COMPILE SUCCEEDS**, so a FAILED build (a compile error included) leaves the running compiler
> in the slot, able to build the fix; a write that fails after the rename moves it back. An older
> `.previous` that another process is still running (an `mcp-server` or `lsp-server` started before the
> last rebuild) is renamed aside to `maxon.retired-<stamp>`, and every self-rebuild deletes the retired
> images nothing holds any longer.
>
>
> ⛔ **A CHANGE TO EMITTED RUNTIME — `Compiler/Runtime/` OR `Compiler/Targets/*/*Runtime*.maxon` — NEEDS
> *TWO* SELF-COMPILES BEFORE THE COMPILER ITSELF BEHAVES THAT WAY.** The compiler EMITS the runtime
> into every program it builds — including into itself — so
> with `C0` the old compiler and `S` the fixed sources:
>
> - `C0` builds `S` → `C1`. `C1`'s emitter logic is fixed, so **programs C1 builds get the new
>   runtime** — but `C1`'s OWN embedded runtime was emitted by `C0`, and is old.
> - `C1` builds `S` → `C2`. Now the compiler's own runtime is new too.
>
> ⇒ **It bites hardest where the compiler is the program under test**: `spec-test`'s worker IS the
> compiler, so a runtime fix to subprocess, the scheduler or memory management does not change what the
> HARNESS does until the second build. MEASURED: a delayed-stdin fix looked like a Windows-only lane bug
> for exactly this reason — the case failed 3/3 against `C1` and passed 3/3 against `C2`.
>
> ⚠ **`fixpoint.sh` DOES NOT CATCH THIS.** It builds two stages under `temp/fixpoint/` and compares
> them — both are past the convergence point, so they agree while the SLOT still holds `C1`.
>
> ⭐⭐ **DO NOT GUESS WHICH CASE YOU ARE IN — ASK, BEFORE YOU BUILD:**
> ```
> scripts/self-compiles-needed.sh     # prints `once` or `twice`, and why
> ```
> The compiler stamps the commit it was built from, so *"has any runtime file changed since the slot
> binary was built"* is a `git diff` rather than a judgement — and it counts uncommitted changes too.
> ⛔ **THIS RULE IS ONLY ABOUT EMITTED RUNTIME. EVERY OTHER CHANGE NEEDS ONE BUILD**, and a needless
> self-compile is a minute off every task that touches the compiler. The script's own header says why
> the per-target half counts.
> ⛔ **THE COMPILER THAT BUILDS THIS TREE MUST LIVE INSIDE IT.** `stdlib/` and its sibling `runtime/` are
> found by walking up from the EXECUTABLE, so an installed `maxon` on PATH compiles this repository
> against the RELEASE's sources — MEASURED: it succeeds and exits 0, having built a compiler from a
> library that is not this tree's. Run `.bootstrap/maxon` or the slot binary, never a PATH one.
>
> ⛔ **`.bootstrap/` HOLDS THE BINARY AND NOTHING ELSE.** A release archive ships its own `stdlib/` and
> `runtime/`, and the compiler resolves both by walking UP from its own executable — so an archive
> unpacked whole would leave a RELEASED stdlib and runtime one directory above the compiler and the
> tree's own would never be reached. The build would succeed and compile the wrong sources, silently.

## maxon MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`**)

**The server IS the compiler**: `maxon mcp-server --dev`, implemented under `maxon-bin/Compiler/Mcp/`.
There is no separate project and nothing to build but the compiler itself, so a rebuild of the slot is
a rebuild of the server. Prefer these tools over raw Bash invocations: faster (no shell startup),
structured results. Use Bash only where no tool covers the case.

⚠ **A RUNNING SERVER IS THE COMPILER YOU BUILT IT FROM.** Rebuilding the slot renames the running
image to `maxon.previous` and writes a new one; the live process keeps serving from the vacated image
until the host restarts it. So after a build, a tool answer still comes from the PREVIOUS compiler —
restart the MCP server when you need the new one to answer.

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> ONE stdio server process is shared by every agent in every worktree, and its default root is the
> main checkout (derived from the SERVER's own binary path). **Say nothing and you are told
> `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree root — to EVERY tool call
> that acts in a tree**: `build`, `run_spec_test`, `run_scale_test`, `spec_test_outcome`, and `execute`,
> `test` and `fmt` when you mean YOUR tree's compiler to answer. Those last three default to the
> host's working directory and name no tree, which is right for a path you wrote yourself and wrong
> for a worktree whose compiler you want exercised. `check`, `dump_ir`, `lookup_error_code` and `info`
> take no `repoRoot` at all: the first two compile INSIDE the server, so they always answer about the
> server's own compiler — read the `executable` and `stdlibRoot` fields they carry before believing a
> verdict about your tree.
>
> ```
> build(repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> - **Every result echoes the `repoRoot` it actually used**, in the payload's `repoRoot` field —
>   answers and refusals alike. **READ IT BACK.** A tool that named no tree leaves the field out
>   rather than reporting an empty one, so an absent `repoRoot` means "the host's directory".
> - ⭐ **THE TREE'S OWN COMPILER RUNS, NOT THE SERVER'S.** A tool acting on `repoRoot` spawns
>   `<repoRoot>/maxon-bin/.maxon/maxon`, because `stdlib/` and `runtime/` are resolved by walking UP
>   from the EXECUTABLE — the server's binary would compile your worktree against the MAIN repo's
>   sources. A tree whose slot is empty is REFUSED, naming the `build` tool.
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`), never quietly swapped
>   for the main repo. Relative paths are refused too — they would resolve against the *server's* cwd.
>   A checkout is any tree holding `stdlib/`, `runtime/` and `maxon-bin/`, so a brand-new worktree qualifies
>   before anything is built in it.
>
> ⚠ These tools **EDIT** the tree they are pointed at: `run_spec_test` with `updateRequired: true`
> rewrites that tree's committed goldens, `run_scale_test` with `note:` writes a row into its
> `docs/optimization-log.md`, and `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build the compiler | `build` — `path: "maxon-bin"`; `from:` names the compiler to build WITH |
| Run the spec suite | `run_spec_test` |
| Per-test PASS/FAIL detail | `spec_test_outcome` (requires `filter`) |
| MEASURE per-phase memory + CPU scaling — an instrument, **no verdict** | `run_scale_test` |
| Run an inline snippet or a file | `execute` — `source:` for a snippet, `path:` for a file |
| Dump IR | `dump_ir` |
| Format a file or snippet | `fmt` — `source:` returns the formatted text; `path:` rewrites in place |
| Look up a 4-digit error code | `lookup_error_code` — number, `"E3014"`, or the case name |

⭐ **AN ARGUMENT NO TOOL DECLARES IS REFUSED** (`invalidParams`), never dropped: a `mmTrace: true` run
that never traced, reported back as a clean success, is indistinguishable from a leak-free run. The
refusal is by ARRIVAL against the tool roster, so it covers `mmTrace` and `dumpStages` and every other
argument nobody thought to reject. A contributor argument sent to a server started WITHOUT `--dev` is
refused the same way.

Always pair `updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite.

⛔ **`build`'s `target:` IS A TRIPLE WHEN IT PARSES AS ONE OF THE FIVE, AND A PROJECT TARGET OTHERWISE.**
`wasm32-wasi` becomes `--target=wasm32-wasi`; any other value becomes a positional naming a target of
the `.maxproj` file, each `_` written `-` (`my-app` is `my_app`). A word naming no target is a SOURCE
PATH to `maxon build`, so a triple has to travel as `--target=`.

**A `spec-test` pattern is a CASE-SENSITIVE substring** of the `<spec>/<test>` label, and `--filter=`
is repeatable: the run takes the union, as N jobs across the pool. **Select a set spanning several spec
files in ONE run** — one `--filter=<spec>/` per file (MCP: a `filter` array) — never one run per file,
which pays the startup per file and runs each on one worker. A comma is part of a pattern
(`--filter=static-methods,enums` selects nothing and is refused). A pattern that selects no case refuses
the whole run, naming it, so a typo'd member cannot run nowhere while the rest reads green. (`maxon test`
lowercases its patterns and also splits a value on commas.)

The runner has no `--verbose` (it always prints a line per test), no `--no-batch` (its batching is
`RunStrategy`, chosen by target and host) and no `--debug-info`; it does have `--network`.

### Common flags

- `--filter=PATTERN` (repeatable), `--update-required`, `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`),
  `--workers=<n>`, `--target=ARCH-OS`, `--network`.
- ⛔ **There is no `--mm-trace` on any command.** The driver refuses an unimplemented flag loudly
  (`Main.MaxonArgs.parse`, which cites this very spelling as the reason it must), so a leak is read off
  the RUN: the runtime's leak gate is **exit 101**, and a case pinning `exitcode 0` reddens on one.
- **`--workers=1` is a DEBUGGING TOOL, not a gate.** It is the same pool with one worker in it, and
  the parent buffers results and reports in fixed order — **ordering cannot vary with pool size**.
  The default pool is `defaultWorkerCount()`, this machine's CPU count, and that is the only count these
  processes run the suite at.

### Targets

The compiler emits `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` and **`wasm32-wasi`** (a
WASI Preview2 component).

> #### ⛔⛔ `--target=` CROSS-COMPILES THE PROGRAMS. IT NEVER HOSTS THE COMPILER.
>
> `spec-test --target=x64-linux` on a Windows host builds and runs the TEST binaries for Linux while
> the compiler stays a Windows process. So that lane never runs the compiler's own `main` **as** a
> Linux program — on a Linux green thread, through the Linux runtime, with that lane's frame layout —
> and it is not evidence about anything that only happens there. **CI hosts every target on its own
> architecture**: seed → `C1` → `C2` → the suite under `C2`, which is a whole class of defect a cross
> lane cannot reach.
>
> MEASURED: the x64 large-frame page walk touched-then-compared, so its last store landed below the
> frame base (`ca4abf52b8`). Harmless on an OS-grown stack; a SIGSEGV on a green thread's exact mmap'd
> one — and reachable only once the called-once inliner carried the compiler's own `main` past one
> page. **Three consecutive pushes to `main` died at exit 139 in `build-from-seed.sh` before the suite
> could start, while the cross lane read 7706/0 on the same tree.**
>
> ⇒ **TO HOST x64-linux LOCALLY, CROSS-BUILD ONCE AND THEN STAY INSIDE WSL** — the recipe is in the
> `compiler-workflow` skill.

`run_spec_test` takes `target: "wasm32-wasi"` and runs the output under the
vendored wasmtime. By hand, for ONE program:

```
./maxon-bin/.maxon/maxon build f.maxon --output=out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

⛔ **`vendor/` IS GITIGNORED EXCEPT `vendor/wasi-wit/`, SO A CLONE HAS NONE OF THE TOOL BINARIES** —
`scripts/fetch-vendor.sh` stages them; see the `compiler-workflow` skill.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY".** Heap, `String`, `print`, structs, arrays, closures,
interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there; the two lanes
run within a few hundred cases of each other. The families it does not run carry an explicit
`<!-- unsupported-targets: … -->` exclusion: **async / green threads, the clock builtins, argv**, plus
the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names), which are about x64's
output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on that lane, not
an out-of-slice case to refuse.**

⭐ **THE MARKER NAMES THE LANES THAT CANNOT SERVE A CASE, NEVER THE ONES THAT CAN**, so a backend that
lands inherits every unmarked case instead of being excluded from all of them at once. The harness
REFUSES a key naming no supported target, refuses a marker that excludes every one of them (a case that
runs nowhere is fixed or deleted), and refuses the retired `<!-- targets: -->` spelling.

⛔⛔ **DO NOT MARK A CASE THE COMPILER ALREADY REFUSES.** A lane with no substrate answers **E3104**, and
the harness reports that as a counted **SKIP** naming the case; a marker removes the case from selection
with nothing said anywhere. The two are not two spellings of one fact — one is the fact and the other is
the fact made invisible. So async, the clock, argv, file and directory IO and console stdin carry NO
marker on wasm: every one of those skips is a case a reader can count. A marker is for a case that would
otherwise go RED — an ISA-specific codegen reading, a POSIX/Windows shell spelling, a diagnostic
displaced by E3104 — and it states its reason.

⚠ **SUBPROCESS IS THE EXCEPTION, AND NEEDS THE MARKER.** A target that can never host a child process
answers **E3074** ahead of E3104 (`requireTargetSupportsCallee`), and the harness counts only E3104 as a
SKIP — so an unmarked subprocess case goes RED on wasm. Mark it `wasm32-wasi`.

## Measuring and self-checking — the `compiler-workflow` skill

Two instruments live in that skill, with their reading guides: `run_scale_test`, the per-phase memory/CPU
doubling ladder to run after any change to a pass, the IR, or a data structure the compiler indexes by;
and `scripts/fixpoint.sh`, which answers whether the compiler reproduces itself byte for byte — a
difference there is a MISCOMPILE, and a green suite cannot see it. Load the skill before acting on either.

## `tests/` — fixture corpora for the DRIVER COMMANDS

**`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
(user ruling), and could not be gated there anyway: a spec case is a Maxon PROGRAM the harness
compiles and runs, so it can reach `stdlib/` and `runtime/` and nothing else. Driver commands are
gated by spawning the compiler at a fixture project and asserting what it reports.

**`tests/README.md` is the authority** — it lists every corpus, the constant each is reached through,
and the rules that keep the corpora honest. Read it before touching anything under `tests/`. The three
facts worth knowing before you get there:

- ⭐ **RUN EACH CORPUS AND READ ITS PASS/FAIL COUNT.** A runner broken badly enough to report green
  having run nothing cannot detect itself, so the count is what closes the circularity.
  ```
  ./maxon-bin/.maxon/maxon.exe test tests/test-command
  ```
  ⚠ **CI runs four of them** — `tests/lsp`, `tests/fmt`, `tests/spec-harness` and `tests/ladders` — and
  `/land`'s battery runs the last three beside the suite and the self-compile. Every other corpus runs
  only when someone names it.
- ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — e.g. `python
  tests/fmt/generate-expectations.py` runs the compiler and records its real answers, so a corpus
  pins what the tool DOES rather than what its author expected. Re-run the generator after changing
  any input, and read the diff: a generated expectation cannot tell you an answer is wrong.
  **`tests/examples/` is the one exception**: it checks the example programs against answers that
  exist outside this compiler (the Benchmarks Game's published output, an example's documented
  result), because there a generated expectation would only record whatever the compiler said.
- ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`** unless its corpus's row says so.
  Names are `<x>.fixture` and `dot-git/`, mapped back at staging time — git refuses to commit a path
  with a `.git` component, and a real `.maxon` under `tests/` is walked by `maxon fmt`, which is the
  tool under test rewriting its own oracle.

One test per file is structural, not tidiness: a file is what ONE process runs and that process has a
5 s default deadline, so twelve compiler-spawning tests in one file report a spurious `TIMED OUT`.

⚠ **`tests/debug` IS 64 CASES AND MOST OF THEM DEBUG A RUNNING PROGRAM**, which is a compile plus a
debugged run inside one file's deadline. Run it as `maxon test tests/debug --timeout=60000`; the default
deadline reports the corpus as timed out rather than failed.

## `maxon fmt`

`maxon fmt [<file|directory>]` — gated byte-for-byte by `tests/fmt/`.
⚠ **With NO PATH it formats the whole current directory** — that is its documented
default. `fmt <file>` formats only that file, `fmt <dir>` that directory; `fmt --check` and `fmt a b`
are REJECTED, exit 1, nothing written. The walk prunes any directory holding `.git`, so it cannot
descend into a nested checkout or an agent worktree.

⛔ **A SUBTREE THAT IS NOT A CHECKOUT NEEDS A `.maxonignore`, AND `website/` IS THE ONE THAT DOES.**
The `.git` rule protects a sibling repository, not a directory of this one — so without the marker a
root `fmt` rewrites `website/src/examples/*.maxon` in place and walks every directory under
`node_modules/`. MEASURED: remove `website/.maxonignore`, mis-format one of those files, run `fmt`,
and it is silently reformatted. The marker is a FLAG whose contents are never read, and both walks
honour it — `fmt`'s and the compiler's own `collectMaxonSources`.

⚠ **THE FORMATTER ENGINE'S GATE IS `tests/fmt/engine-cases.maxtest`, NOT `spec-test`.** It formats
the 28 sources in `tests/fmt/engine-cases/` (4 of them unlexable) with the real `maxon fmt`, twice, and
goes red if an answer differs from its `.expected`, a comment is lost or duplicated, a lexer-error
sentinel is written into a file, or a second run moves anything. `UrlInPlainString`,
`NoMultilineLiteral` and `PlainStringNoInterpolation` are negative controls that must stay GREEN.
Three separate silent source-corrupting defects reached the tree before the formatter had a gate; a
preservation check phrased as *presence* passes duplication, so it asserts **multiplicity**. Run it with
`maxon test tests/fmt`.

## ⚠ Running a suite by hand: REDIRECT IT TO A FILE. Never pipe through `head`/`tail`/`grep`.

```
mkdir -p temp
./maxon-bin/.maxon/maxon.exe spec-test > temp/spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/spec.log
```

Then **read the file** at each hit for the full reason. **A pipe decides what to keep before you know
what failed**, so when the run goes red the detail is already gone and the only way back is running
the whole suite again. Grep alone is not enough either: a failed compile embeds the compiler's entire
stderr, so the marker line is a headline, not the evidence.

Do not assume the console is small: **the runner prints one line per test (~1,500) and only then
the summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines
while the reason sits thousands of lines above. `temp/` is gitignored. The MCP tools need none of this.

⚠ **THE SUITE RUNS EVERY TEST BINARY WITH CWD `temp/`**, so that directory is shared with anything
else you put there. Stage a binary you must keep somewhere else.

## Error codes — ONE registry, and it is the enum itself

**`maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry.** It carries the number, the canonical
name and the doc text for every code, and it is AUTHORED — edit it directly.

**To add a diagnostic:** take the next free number in the right band, add a case, write the code that
emits it.

⛔ **Not its doc comment.** That block is a documentation surface, not a code comment —
`Mcp/McpErrorCodes.maxon` re-parses this file at runtime to serve `lookup_error_code`, and the site's
Error Codes page is generated from it — so the `documenter` skill writes it, with every other comment
the change gets, just before the commit. ⚠ `website/scripts/sync-docs.mjs` fails closed on a case with
no doc comment, which is how it finds them.

- **A duplicate NAME does not compile**, and `ErrorCode.Foo` does not compile unless the enum declares
  it — both are structural, so neither needs a checker.
- **A duplicate NUMBER is neither**: two cases may carry one `"E3099"` and the program is well-formed.
  `website/scripts/sync-docs.mjs` refuses one while it reads the registry, naming BOTH lines —
  `website.yml` runs it on every change to the registry, and `/land`'s battery runs it too. `spec-test`
  does not check this.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written
  down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the registry's member
  (`ErrorCode.semanticUnneededCast`, plus `.rawValue` for the `"E3010"` spelling). A literal `"E3010"`
  in a source file is a second copy of the number space: renumber the code and every gate stays green
  while the code that matched it silently stops matching anything.

## Spec files

- **Golden drift needs no attention.** The goldens under `specs/fragments/<target>/` are reference, not
  a gate. A run MINTS a golden a case has none of, on the host where that case passed; it never rewrites
  an existing one — a golden whose bytes differ from this run's compile is REPORTED as drift (`N drifted`
  beside `0 failed`, the differences in a note on stderr) and the case keeps the verdict its assertions
  earned. Only `--update-required --filter=<spec>` rewrites a committed golden. A golden that only
  another host can mint (the arm64 lanes) is minted there: CI fails a lane that leaves untracked goldens
  and uploads them as an artifact to unpack at the repository root. Whatever a run minted, or an
  `--update-required` rewrote, is committed with the change (`git add -A specs/`) — never measured,
  investigated, explained or reverted. (A `RequiredIR` block in a spec file is not drift: it is a test
  input, and a mismatch is a failing test.)
- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests using RequiredIR fail, regenerate with `--update-required` **plus a `filter`**.
- `--update-required` regenerates RequiredIR but **not** `maxoncstderr` blocks — an error-code
  renumber moves those by hand.

## ⚠ A running MCP server keeps answering from the compiler it was started as

The server is the compiler, so editing `maxon-bin/Compiler/Mcp/` and rebuilding the slot leaves the
LIVE process running the old code: a build renames the running image to `maxon.previous` rather than
overwriting it (an OS will not let a running executable be deleted, but will let one be renamed), and
the process serves on from the vacated image. **RESTART THE MCP SERVER after a build whose result you
want the tools to reflect.**

`tests/mcp/rebuild.maxtest` pins the surviving half of that: the process keeps answering across the
replacement rather than dying mid-session.
