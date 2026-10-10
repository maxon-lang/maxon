# The runtime source tier

`runtime/` holds the language runtime written as Maxon source: the code a program needs that the compiler
supplies rather than the author. Every compile reads it, beside `stdlib/`. The compiler locates `stdlib/` by
walking up from its own executable and derives `runtime/` as that directory's sibling
(`StdlibSource.runtimeDirBeside`) rather than walking for it separately, so the two tiers a compile reads
always come from one tree. A release archive, an install tree or an image that carries one without the
other compiles nothing (`CompileError.runtimeNotFound`). A `runtime/` file is not a program of its own, and
building one as a project root is refused (`CompileError.runtimeTierIsNotAProgram`).

A tier file is input the compiler reads, so an edit to one needs one self-compile: the first build already
compiles against the edited file. `maxon run build` builds a second time only when `Compiler/Runtime/` or a
per-target `Compiler/Targets/*/*Runtime*.maxon` file changed (`maxon-bin/maxon.maxproj`,
`emittedRuntimePathspecs`); a `runtime/` edit is outside that check. The rest of the runtime is still
compiler code under `Compiler/Runtime/` that builds Std IR (a "builder"), and changing that needs the two
self-compiles `maxon-bin/AGENTS.md` describes.

`specs/runtime-source-tier.md` is the specification of what the tier admits and refuses.

## The files

| File | Entry points | Reached through |
|---|---|---|
| `Clock.maxon` | `__clock_now_unix_s`, `__clock_now_unix_ns`, `__clock_now_filetime`, `__uptime_ms` | `__Builtins.currentUnixTimeSeconds`, `currentUnixTimeNanos`, `profileNowFileTime`, `tickCountMs` |
| `CpuParallel.maxon` | `__cpu_count`, `__sched_max_active_workers`, `__sched_processor_count` | `__Builtins.cpuCount`, `schedMaxActiveWorkers`, `schedProcessorCount` |
| `FaultProbe.maxon` | `maxon_force_segfault` | `__Builtins.forceSegfault` |
| `ParallelBoundary.maxon` | `__parallel_boundary` | `__Builtins.parallelBoundary` |
| `Process.maxon` | `__proc_pid`, `__proc_bg_priority`, `__proc_alive` | `__Builtins.currentProcessId`, `enterBackgroundPriority`, `processIsAlive` |
| `ManagedGeometry.maxon` | `__mg_element_bits`, `__mg_bits_to_bytes`, `__mg_element_byte_len` | calls the managed-memory, file and buffer builders mint (`ManagedMemoryRuntime.emitElementBits`, `emitBitsToBytes`, `emitElementByteLen`) — compiler-called roster |
| `SlabArena.maxon` | `__slab_assert_os_alloc`, `__slab_arena_new`, `__slab_arena_alloc_chunks`, `__slab_arena_free_chunks`, `__slab_arena_of`, `__slab_arena_scavenge`, `__slab_arena_committed_bytes`, `__slab_arena_map_ensure`, `__slab_arena_map_set` | calls `installSlabRuntime`'s builders mint, and calls from the two slab tier files — compiler-called roster |
| `SlabRuntime.maxon` | `__slab_os_direct_alloc`, `__slab_os_direct_free`, `__slab_state_base`, `__slab_meta_alloc`, `__slab_meta_free`, `__slab_census`, `__slab_census_tally_walk`, `__slab_census_bucket_count`, `__slab_census_bucket_bytes` | calls `installSlabRuntime`'s builders mint (the census entries from the locked `__slab_live_bytes`-family wrappers) — compiler-called roster |
| `WideText.maxon` | `__wt_widen`, `__wt_narrow`, `__wt_narrow_ascii`, `__wt_text_length`, `__wt_block_extent` (Windows only) | calls `HostTextRuntime`'s builders mint at every UTF-8⇄UTF-16 boundary of x64-windows — compiler-called roster |
| `Word.maxon` | none: the shared `module typealias`es `MachineWord`, `BufferCoordinate`, `ElementStride`, `RefCountDelta` | — |

A `__Builtins` row is lowered by the parser to a call naming the entry, from whatever file wrote the
construct (`stdlib/Clock.maxon`'s `nowUnixSeconds` is one such caller). That call is an ordinary edge in an
ordinary body, so the reachability walk finds it. A compiler-called entry is reached only by a
`StdOp.call` a builder mints into the appended band after the walk has run, so its reach is declared
instead; see [Reach](#built-and-reached).

A tier file's other functions (`clockTicksSince1970`, the `slabArena*` and `slabCensus*` helpers) are
reached only through its entry points.

## Rules a tier file obeys

- **No managed value (E3153).** The reference-counting pass emits calls into the runtime this tier
  defines, so a managed value here would be bookkeeping that calls the function being lowered. The rule is
  asked at three places. `Parser.parseTypeReference` refuses any managed type a tier file spells (a field,
  a return, a parameter, a cast target). `Parser.noteManagedValueInRuntimeSource` is asked at `mintValue`
  and `retypeValue`, the only two writers of the parser's value type columns, so every value passes it
  whatever bound it — a `for` element, a `match` payload, a caught error, an unnamed temporary. Neither
  writer may throw, so the first offence is recorded and refused at the end of `parseModule`
  (`requireNoManagedValueWasBuiltInRuntimeSource`), positioned at the defining op's span. A capturing
  closure owns heap that no type column shows; `noteClosureRecordInRuntimeSource`, asked where
  `buildClosureEnv` builds the record, refuses it. `declareInitializedBinding` and `bindParameters` ask
  first only for a sharper sentence that names the binding. A new way for a value to acquire heap that is
  not a type-column write owes its own recording door.
- **Nothing wider than `module` (E3154).** The tier is loaded into every program, the compiler among
  them, so an `export` or `public` declaration would contest names with the program it is linked into.
- **`__Raw.*` is callable only from a tier file (E3152).** `__Raw` is the closed table of machine and OS
  operations in `Compiler/RawIntrinsics.maxon`; an unlisted `__Raw.` member is E3004. See
  [`__Raw` rows](#__raw-rows-and-host-facilities).
- **A tier file may declare `__` names.** The E2051 reservation is lifted for `runtime/`
  (`Queries.declaresCompilerInternalNames`).
- **Do not discard a declared callee's result.** `_ = f(…)` on a function the program declares makes the
  compile build the whole-program effect-free summary (`SemanticCheck.effectFreeSummaryIfConsulted`) to
  judge the discard, and tier bodies are built in every program. Consume the value instead. A discarded
  `__Raw` result does not ask that question.
- **No `panic`.** A panic builds a message and walks a stack, both of which allocate, and the allocator
  cannot do that while reporting its own failure. A tier body that cannot continue calls
  `__Raw.osExit(code)` and writes a `return` under it for the block's terminator, the shape
  `RuntimeAbort.emitRuntimeAbort` emits.
- **Target forks are `#if os(…)` / `#if arch(…)`** (`Process.maxon`, `SlabArena.maxon`). Tier source
  reads no `RuntimeUsage`.
- **No name crosses the tier boundary**, so a constant the compiler also uses is restated in the tier
  file and pinned; see [Restated geometry](#restated-geometry-and-its-pins).

## Reserved names and their two doors

A tier entry can be reached by name through two doors, and each is refused outside the files that may use
reserved names:

- **Call.** `Parser.requireCalleeIsNotReservedName` refuses a reserved callee with E3004.
- **Value.** `Parser.requireFunctionValueNameIsNotReserved` refuses a reserved name in value position
  (`let f = __parallel_boundary`) with E3155. It is asked from `requireNameIsUsableAsFunctionValue`, which
  every producer of an author-named function value goes through — a bare or directory-qualified name read
  (`emitNamedFunctionRef`) and a function-backed enum case (`caseFunctionName`) — so no list of syntactic
  positions is needed.

Both doors admit a reserved name only where `Parser.fileMayUseReservedNames` holds and
`ProgramSignatures.declaresCallee` says some file declares it. `fileMayUseReservedNames` is true for a
`runtime/` file, for `stdlib/Builtins.maxon` and `stdlib/Testing.maxon`, and for a file this compile wrote
part of — which includes the author's staged `*.maxtest` file under `maxon test`, so on both doors that
file is the standing exception to "no file outside the tier calls or names a runtime entry".

Reserved means `MmRuntime.reservedCalleeReasonOf` answers other than `notReserved`: the `__` prefix, a
synthesized builtin-conformance impl, or `maxon_force_segfault`.

### `maxon_force_segfault`, the unprefixed entry

Its name is the frame a backtrace prints (`specs/safety.md`'s `force-segfault`, `force-segfault-macos` and
`force-segfault-on-a-green-thread` assert it), so it cannot take the `__` prefix. It carries its own
reservation on both sides:

- **Declaration.** `Parser.requireFreeFunctionNameIsNotTheFaultProbe` refuses a free function of that name
  outside the files that declare compiler-internal names (E2051). A local, parameter, field, case or method
  may use the word; none enters the free-function name space. The cases are `specs/safety.md`'s
  `error.force-segfault-name-is-reserved` and `force-segfault-name-is-free-outside-the-function-space`.
- **Call and value.** `ReservedCalleeReason.faultProbeEntry` makes it reserved at both doors above.

⚠ The declaration refusal is what prevents a silent wrong answer: a second free function of that name
makes it contested across directories, `Parser.declaredMethodName` then files the tier's declaration as
`runtime.maxon_force_segfault`, and the bare call the compiler emits binds to the program's function, which
does not fault. `FunctionNameIndex.refuseDuplicateFunctionName` cannot report it, because both sides are
parsed source. A new unprefixed tier entry owes the same declaration refusal and a `ReservedCalleeReason`
arm.

### Module visibility

A compiler-emitted call into the tier comes from whatever file wrote the construct, so
`SemanticCheck.calleeVisibleFrom` admits, after the ordinary rule refuses, a callee that is reserved
(`MmRuntime.isCompilerInternalCallee`) and declared under `runtime/`. This admit-list is sound only because
no author can write a reserved name; for `maxon_force_segfault` that rests on the declaration refusal
above. A tier file's unreserved declarations stay module-scoped
(`specs/runtime-source-tier.md`'s `runtime-file-unreserved-declaration-is-still-module-scoped`).

## Provenance: `LibraryFacts.runtimeTier`

`LibraryFacts.runtimeTier` (filed by `StdlibSource.collectOneLibraryFile`) is the set of function names
declared in `runtime/`. Where a family's body is written is not observable by a program, so every
predicate asking "is this callee the runtime's?" reads this set rather than a proxy such as "declared in no
source" or "not parsed from source":

- `SemanticCheck.calleeYields` answers "yields" for a tier callee and records no edge. Recording the edge
  would hand the caller the tier body's own non-yielding answer and refuse a legal `async` spawn (E3073).
- `SemanticCheck.calleeShowsAnEffect` answers "an effect" for a tier callee not on the effect-free roster.
  Otherwise a pure tier helper would make a user caller pure and earn E3064 on a legal discard.
- `TargetPrinter.isRuntimeFunction` treats tier membership as a disjunct of its own, beside the
  `__`/`mrt_` band of compiler-built bodies. It decides the green-thread stack guard
  (`skipsGreenThreadStackGuard`, read by both native backends' function emitters),
  `FunctionCodeChunk.asyncPreemptible`, `compilerScaffolding`, and
  `InlineLeaves.splicingWouldWidenTheSafePoint`. Keyed on the band alone,
  `maxon_force_segfault` would carry a stack guard and be marked preemptible.
- `InlineLeaves.roundAdmitsCallee` admits only tier bodies into the band round that splices them into
  builder-emitted callers.
- `StdlibSource.classifyLibraryReach`, below.

A new predicate of that kind reads `runtimeTier`.

⚠ Every declaration in a tier file skips the green-thread stack guard, unreserved `module function`
helpers included, and tier source has no way to ask for one (`IrFunction.needsGreenThreadStackGuard` is set
only by builders). They run inside the margin `GtRuntime.gtStackGuardMargin` is sized for, so a tier body
must not recurse on program-sized data.

The `asyncPreemptible` consequence is not observable by any spec case: the guard is emitted at byte level
rather than as a `TargetOp`, the bit reaches only the symbol table's safe-point bits
(`BacktraceFormat.symbolSafeFromByte`), and `schedPreemptCount()` counts preemptions honoured. A pin
cannot catch a regression in `isRuntimeFunction`; read the predicate.

## Built and reached

Every tier body is built in every program (`Queries.buildsBodiesFor`), because no source call edge can
earn one its body. So a tier name is never filed `LibraryFacts.unreachable`; the exemption is by
provenance. Whether the program reaches a tier body is a separate question, answered by the same
from-`main` walk: `classifyLibraryReach` files an unreached tier name under
`LibraryFacts.unreachedRuntimeTier`. Every pre-elimination pass reads the two sets as one predicate,
`LibraryFacts.bodyIsSwept`: a swept body is not lowered, guarded, scanned by `scanRuntimeUsage` or
call-checked, and dead-function elimination removes it. Elimination prunes functions and never `.rdata`,
so lowering a body it later sweeps would leave that body's strings in the image ahead of the program's
own (`specs/stdlib-loading.md`'s `a-stdlib-modules-literals-cannot-reach-the-rdata-image`).
`specs/runtime-source-tier.md`'s `an-unreached-runtime-body-costs-the-program-nothing`,
`a-reached-runtime-entry-still-earns-its-word` and `reaching-one-family-does-not-credit-another-tier-body`
are the gate.

The answer is closed over tier-to-tier calls, because tier bodies are in the merged Maxon module and
`markMaxonCalleeEdge` filters no callee by provenance.

⚠ The two errors are not alike. A tier name credited reached that is then swept costs bytes: its calls set
`RuntimeUsage` bits for code the program does not contain, and no diagnostic results because
`SemanticCheck.blameTargetErrorsIn` is false inside library code. A name filed unreached whose body ships
calls into a floor, a host chunk or an import nothing installed — a link failure. So a name is filed
unreached only on the walk's positive evidence, every unmodelled edge resolves to reached, and
`DeadFunctionElimination.requireUnreachableLibraryStayedDead` panics by name if elimination keeps a body
filed unreached.

### The compiler-called roster

`StdlibSource.runtimeTierFileIsCompilerCalled` names the tier files the compiler calls into through minted
`StdOp.call`s: `SlabArena.maxon`, `SlabRuntime.maxon`, `ManagedGeometry.maxon` and `WideText.maxon`. No walk over source sees
those calls, so every name in those files is declared reached (`LibraryFunctionRoster.compilerReached`).

- **By file, not by name**, because a minted call lands on an entry point whose body reaches the file's
  helpers, and the short-circuit path hands `classifyLibraryReach` an empty reached set with no walk to
  close a name roster. A new entry point in a listed file needs no new line.
- **Unconditional at the walk**, because `usesHeap` is not final until after `scanRuntimeUsage`.
- **Settled once the gate is final.** `Compiler.compileToCodeResult` calls
  `LibraryFacts.withCompilerCalledTierUnreached(not usage.usesHeap)` after every producer of `usesHeap` has
  run, which files every name of the listed files unreached in a program with no allocator. Every minter
  rides `usesHeap`: `installSlabRuntime` returns without it, and the `__mg_*` callers are installed only
  under `usesManagedMemory`, `usesManagedFile` or `usesManagedSocket`, each of which implies it.

A new tier family the compiler reaches by minting a call owes a line in that roster, and its minter must
ride `usesHeap`: a minter that can run in a program without `usesHeap` calls bodies filed unreached, which
is a link failure. `requireUnreachableLibraryStayedDead` names the body either way.

⚠ A listed file is credited reached, and therefore walked by `scanRuntimeUsage`, in every program until the
gate is settled. The band its entries wear is a usage decision: a `recordCallUsage` arm that claims the
band sets its bit in every program. That is why the geometry family is `__mg_` and not `__mm_` —
`MmRuntime.isRuntimeCallee` reads `__mm_` as `usesHeap`. A listed family must wear a band no
`recordCallUsage` arm claims unless it needs what that arm installs.

No tier body is reached by every program.

### What a `RuntimeUsage` bit still gates

Dead-function elimination decides whether a tier body survives, so a family's usage bit installs no tier
body. The bits a tier call sets still gate what the body depends on outside the tier: `usesWallClock` the
per-target wall-clock chunks; `usesUptimeClock` the POSIX monotonic clock (`posixReadsMonotonicClock`) and
the Windows optional import band; `usesCpuCount` the Windows optional imports and the two Linux
`mrt_host_cpu_count` chunks; `usesBackgroundPriority` and `usesProcessLiveness` the Windows optional
imports, and `usesProcessLiveness` the two arm64 POSIX lanes' liveness chunks. A bit is removed when its
last consumer is, not when a body becomes tier source. `__sched_max_active_workers` sets no bit; `__sched_processor_count`
installs the scheduler (`SchedRuntime.isSchedSubstrateQueryCallee`).

## `.data` words

A runtime `.data` word a tier body reads follows the body. `GlobalDataTable.layOut` runs in
`BackendDispatch.buildBackend` after dead-function elimination, and a word pushed with `DataReach.walked`
is laid out only if a surviving function names it in a `globalAddr` (the label set elimination collects in
the walk that decides survival). `__sched_num_procs` and `__sched_max_active_workers` are laid out exactly
where `CpuParallel.maxon`'s queries survive, and a query only a dead function asks lays out nothing
(`specs/runtime-source-tier.md`'s `a-word-only-a-dead-user-function-reads-is-not-laid-out`). The arena's
`__slab_arena_list`/`__slab_arena_map_l1` and the allocator's state word are `walked` the same way. A word
a hand-assembled chunk or the entry stub reads is `DataReach.declared` on that reader's gate, because no Std
walk sees the reader; a word read by both is `declared`. `GlobalDataTable.requireLaidOutCoversReferences`
panics by label on a surviving `globalAddr` the layout does not hold.

A tier body names a word through a `__Raw` address row, one row per word; it cannot form an arbitrary
label.

Layout order: reserved `.data` runs first in registration order, then the user's globals, then the
runtime's in `BackendDispatch.dataSectionRoster` order. Each of the two scalar segments is sorted largest
slot first, stably, so the roster order is not the `.data` order. Dropping an unreached `walked` word keeps
the survivors' relative order, and the segment split keeps a runtime word added anywhere from moving a user
global.

⚠ Every slot is naturally aligned (`naturallyAlignedSlotOffset`), so a user segment ending on a 1-, 2- or
4-byte slot is followed by zero padding before the first runtime word. arm64's exclusive and acquire/release
loads and stores fault on a misaligned address and x64 does not, so a layout that drops the alignment
crashes only the arm64 lanes. `specs/static-variables.md`'s
`data-section-runtime-word-after-a-bool-is-aligned` pins the pad.

## What may be tier source

A family's bodies can be tier source when the builder that emits them is a constant function of
`RuntimeUsage`. Tier source is compiled once and reads no usage record, so a body whose emitted code varies
with the record has no tier spelling. Two consequences:

- `__mm_alloc` stays a builder: its arity changes with `--debugstream`. `ManagedMemoryRuntime` stays a
  builder: its function list is a function of the program's types.
- A literal a builder's caller passes is not such an argument. `installSlabRuntime` passes `zeroed: true`
  to `__slab_alloc` and `false` to `__slab_alloc_raw`; tier source would spell that as a `bool` parameter.

A second gate is asked after that one: a walk two bodies share must have one spelling. A body that shares
a walk with a builder stays a builder:

- `__slab_rounded_size` shares `SlabRuntime.emitSlabClassIndex` with `__slab_alloc`.
- `__slab_span_destroy` shares the span's chunk-run computation with `__slab_refill`
  (`SlabRuntime.emitSpanChunkCount`). ⚠ The cut and the destruction must agree to the chunk, or a span is
  released one chunk short and a chunk stays claimed forever.
- The reverse map's read (`__slab_arena_map_get`) is spliced inline into `__slab_free` by
  `SlabArena.emitSlabArenaMapGet`, so it is not an entry point and has no tier spelling.

The slab object layer's remaining entries are builders in `installSlabRuntime`: `__slab_collect_remote`,
`__slab_rounded_size`, `__slab_refill`, `__slab_alloc`, `__slab_alloc_raw`, `__slab_span_destroy`,
`__slab_free_to_full`, `__slab_free_to_raw`, `__slab_scavenge`, `__slab_free_to_span`, `__slab_free`, and
the eight census wrappers that take `__slab_lock` around a tier census walk. The refill, both allocation
doors, the scavenger, the free and the census wrappers take `TargetFacilities.machineModel(target)`: on
wasm32-wasi (`singleMachine`) the "which processor am I" walk is a constant, elsewhere a TLS read. The
allocator reads no usage bit; whether a second thread exists is a run-time word in the state head
(`SlabStateSchedulerOffset`).

`__proc_exe_path` stays a builder (`ProcessRuntime`): its body allocates and returns a managed buffer
(E3153), and the allocator entries it would call (`__mm_alloc`, `__mm_free`, `__managed_create`,
`__managed_reserve`) are builder-built and declared in no source, so a tier call to one fails
`declaresCallee` and earns E3004. `__thread_cpu_ticks` stays a builder (`ClockRuntime`): its green-thread
arm reads the current green thread's clock offset, which no `__Raw` row names.

A tier body spliced into code the compiler does not own is refused by `splicingWouldWidenTheSafePoint`, so
a primitive `InlineManagedPrimitives` expands into user functions — `emitSlotAddr` — has no tier spelling.
The `__mg_*` bodies are called only from builder-emitted `__` bodies.

Bulk copy has no `__Raw` row and no Std op; tier source writes it as a loop over the word and byte
accessors.

## Restated geometry and its pins

No name crosses the tier boundary, so a tier file restates the constants it shares with an emitter, and
each restatement is pinned:

- `SlabRuntime.checkSlabRuntimeGeometry` and `SlabArena.checkSlabArenaGeometry` read each restated
  constant back out of the tier file by name (`TierGeometryPins.requireTierGeometryAgrees`, through
  `ProgramSignatures.integerConstantIn`, folded by the compile reading it) and panic if it differs from the
  emitter's. Reading the tier's own value is what makes the check compare the two files; an expected value
  declared on the emitter's side would be a pin against itself. The arena check runs on every compile; the
  object-layer check on every compile of a program that allocates. Without them a regenerated class ladder
  becomes a request one chunk too small — an mcache running off the end of its run, silently.
- `ManagedMemoryRuntime.checkManagedGeometry` compares the owners (`LayoutDescriptor.BitsPerByte`,
  `MachineWordBytes`, `BitPosToByteShift`) against literal copies of what `ManagedGeometry.maxon` derives,
  on every compile. It does not read the tier file, so it catches the owner moving and not the tier file
  changing; an edit to the tier's constants is caught by the suite as wrong array answers.

A restated derivation owes a pin.

⚠ Changing a figure the read-back pins cover takes a staged build. The pin compares the building
compiler's emitter constant with the tier file on disk, so a tree where both have moved is refused by the
compiler you would build it with. Set the tier file back to the current emitter's values, build C1, restore
the tier file, then build C2 with C1. Growing the allocator's state region is this every time.

## Frame directives: `ownFrame` and `splicedAtEverySite`

Two `__Raw` rows are directives rather than operations. Neither emits a Std op; each sets a fact on the
function that spells it, and `Parser.recordFrameDirective` is the one writer of both. Spelling both in one
body is E3157.

- **`__Raw.ownFrame()`** sets `IrFunction.keepsItsOwnFrame`, which `InlineLeaves.functionShape` refuses to
  splice. Without it a tier body small enough to inline is spliced into its callers and swept, and an entry
  whose product is its frame disappears. `__parallel_boundary` and `maxon_force_segfault` declare it.
  `ir-specs/builtins-parallel-boundary.md`'s `checkpoint-body-is-runtime-source` renders the checkpoint body;
  a ```` ```RequiredRuntime ```` block keeps a body un-inlined for its own compile, so what guards the rule
  is the `call __parallel_boundary` in every other `TargetIr` pin that reaches it.
- **`__Raw.splicedAtEverySite()`** sets `IrFunction.mustBeSplicedAtEverySite`: the inliner splices the body
  into every call site whatever its size, so a family a builder emitted inline can be tier source and still
  be emitted inline. `ManagedGeometry.maxon`'s three entries declare it. It overrides the inliner's cost
  rules only: `MaxInlinedLeafOps`, the called-once pressure budget, the inline-frame-record rule, and the
  leaf rule's "no call" for a call into another body declaring the row (spliced callees-first). The
  correctness refusals still apply: `splicingWouldWidenTheSafePoint`, a by-reference parameter
  (`reassignedParamMask`), `needsGreenThreadStackGuard`, `keepsItsOwnFrame`, the
  `isUnsupportedInInlineBody` ops, and a `RequiredRuntime` request for the body.

  A refusal is never silent. `InlineLeaves.requireAlwaysSplicedBodiesAreGone` reads the surviving module in
  `BackendDispatch.buildBackend`, after the last splice round and elimination and beside
  `assertCallsMatchCalleeArity`, and reports E3158 at the body's declaration, naming the reference that
  survived (a call, a function value, an `.rdata` slot) and the rule that refused. A cycle of bodies
  declaring the row is detected and reported the same way. Before relying on the row for a family, check
  who calls it: a body spliced into code the compiler does not own is the safe-point refusal.

## `__Raw` rows and host facilities

`Compiler/RawIntrinsics.maxon` is the table. Each row has an argument shape, a result kind and a host
facility (`rawHostFacilityOf`); `RawHostFacility.none` means no host facility, not every target.

A row naming a facility is carried out to user code by the substrate fixpoint:
`StdlibSource.noteSubstrateRawIntrinsic` seeds it with the row's spelling, and
`SemanticCheck.requireTargetSupportsCallee` reports E3104 at the first user call into the library body that
reaches the op. Where a callee band also answers (`TargetFacilities.calleeHostFacility`: the `__clock_`,
`__uptime_`, `__cpu_` and `__proc_` bands, and `__proc_bg_priority`/`__proc_alive` by name), that refusal
ranks first (`SemanticCheck.substrateEntryRank`).

⚠ A tier body whose only callers are installer-minted `StdOp.call`s has no user-code crossing to report at.
The slab files are in that position; what keeps a lane from meeting an op it cannot lower there is the
installer's own fork on `machineModel`. A row spelled from such a body owes that fork.

Notes on particular rows:

- **`osExit`** names no facility: every lane lowers `StdOp.osExit`, the floor under every panic and range
  check.
- **`osLockInit`/`osLockEnter`/`osLockLeave`** need `HostFacility.hostMutex`, true on all five lanes. On
  wasm32-wasi each lowers to nothing, because a component has one thread; a threaded wasm lane would need
  real bodies. On the three POSIX lanes the `mrt_host_lock_*` chunks ride
  `PosixRuntime.posixUsesHostLock` (`usesGt`, `usesDebugStream` or `usesHeap`), kept apart from
  `posixUsesHostObjects` so a single-threaded program that allocates does not carry the child reaper.
  ⚠ A chunk gate narrower than the producers of its op is a link failure, not a diagnostic.
- **The page rows** need `HostFacility.pageMemory`, true on all five lanes. On wasm32-wasi `memory.grow`
  is the whole API: a reserve is an allocation and a decommit or free emits nothing.
  `targetProvidesFacility` states wasm32-wasi as its own block. `osCommitPages` destroys the range's
  contents on the POSIX lanes (a `MAP_FIXED` mapping), so commit only a range holding no data.
- **`scratch`** reserves frame slots (`Parser.reserveRawScratchSlot`; the size must be a positive multiple
  of 8 bytes) and lowers to `StdOp.stackRecordAddr`, a frame address in a register. In a green-thread
  program a frame can be relocated (`__gt_stack_relocate`), which is why `PromoteStackRecords` promotes
  nothing there; `scratch` reaches `TargetOp.leaRegSlot` by its own route with no such gate, so a tier body
  must not hold a `scratch` address across a call that can relocate the stack. ⚠ It has no wasm lowering
  (`StdToWasm.emitBodyOp` panics on `stackRecordAddr`) and no `HostFacility` names an addressable frame, so
  the row answers `none` and a reached tier body spelling it dies in that backend. Its only tier user,
  `__clock_now_filetime`, is refused on wasm by the `__clock_` band first.
- **The atomics** take no offset; `atomicAddWord` answers the prior value and `atomicCas` answers 1/0.

## Open gaps

- No spec case and no tier file spells `osThreadCpuTicks`, `osThreadCreate`, `schedTlsTebOffsetAddr`,
  `schedAllMachinesAddr`, `schedPreemptExtLockAddr` or `schedLockAddr`, so nothing exercises their parsing,
  lowering or facility route.
- No spec case covers `reserveRawScratchSlot`'s refusals.
- `osLockEnter`, `osLockLeave` and `atomicCas` are spelled by no reached tier body; spec probe bodies that
  spell them are unreached, so dead-function elimination drops them before instruction selection and no
  lane's isel is exercised from the tier. A probe case proves only the parser and the Maxon-to-Std lowering.
- `scratch` has no wasm lowering and no facility row to refuse it (above).
