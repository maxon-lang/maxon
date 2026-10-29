- claude skills (submit issue, etc)
- libraries
- A typealias (or any named type) used in a function's SIGNATURE must have visibility >= that
  function's. `stdlib/Array.maxon:168` is the case that found it: `public function count() returns
  ElementIndex`, where `ElementIndex` (Array.maxon:17) is a bare `typealias` — file-visible — so a
  public signature hands back a type no caller can name. MEASURED 2026-09-05 by a textual scan of
  stdlib/ + maxon-bin/: ~235 leaked signatures across 66 files (Array 22, Subprocess 19, Builtins 13,
  Parser 13, Testing 12); `String.from(bytes ByteArray)` is the sharpest, `ByteArray` being file-private
  to String.maxon:26. Treat the count as an order of magnitude — the scan does not resolve file scope.
  ⚠ The compiler already polices the OPPOSITE direction and only that one: E3092 (an `export` nothing
  uses) and E3093 (an `export` that could be `module`). It objects to visibility that is too broad and
  says nothing about visibility that leaks.
  Needs deciding before building: whether the rule covers all named types or only typealiases, whether
  it reaches struct fields and generic arguments, and a new error code in the 3xxx band.
- auto-update the install
- `tests/ladders/` generators are in the state `genrangesites.sh` documents for itself: several emit
  programs that no longer compile. Measured 2026-09-07 against BOTH this tree and origin HEAD, so
  none of it is new — `genshareddag` (E3012 unused variable `base`), `genclosure` in both `ranged`
  (E3005 `Integer` + `Word`) and `plain` (E3062 unused typealias `Word`) modes, and `genfsprobe`
  fails to generate at all. `gennest`, `genemit` and `genrangesites` are the ones that work.
- safeffi
- use code generation for generics to remove monomorphization/witness
- look into making optimizations into compile error (ie hoisting a static value out of a loop)
- emit-asm (for compiler explorer)
- investigate changing Hasher from FNV-1 to SipHash-1-3
- remove @category from stdlib
- ensure static/const unions/enums exist in rdata not the heap (like strings)
- tokenkind should be a type
- multiline string literals using multiple quotes
- One sweep drop is reported by two internal codes: a signature `recordAttributableDrop` drops
  (`tupleElementRejected`) gets E9003 at its declaration (`Parser.requireSweptDeclaration`) and E9002 at
  each Array-member use (Parser.maxon ~:73985). One should own it, or E9003 should carry the drop's
  reason. No known program reaches a drop. Found by reading, 2026-09-24.
- `ProgramSignatures.methodInnerAliasParams` is filed under the swept METHOD name as written, not the
  registration key, so a static and an instance member of the same name in one generic type share one
  entry (last wins). No wrong answer reached. Found by reading, 2026-09-24.
- `extensionMethodConstraints` / `unconstrainedExtensionMethods` (`noteTypeExtensionWitnessConstraints`,
  SignatureIndex ~:21794) are keyed `T.m` whatever the member's kind, so a static/instance pair in
  constrained type extensions overwrite each other, and the static is later looked up at `T.m#__static`.
  The cross-file extension contest maps (`extensionMethodDeclFiles`, `contestedExtensionMethods`,
  `stdlibPublishedExtensionMethods`, `stdlibExtensionMethodsBeatenByUser`, ~:13496-13530) are name-keyed
  too, so a static `m` in one file's extension and an instance `m` in another's read as a contest. Found by
  reading, 2026-09-24; no wrong answer reached yet.
- `SemanticCheck.validateCall` (~:3913) reports E3004 from the real parse's `project.funcSignatures`
  (post-overload-resolution names), while the parser's deferral and SemanticCheck's trace ask
  `ProgramSignatures.definesCallee` (the sweep). E9003 enforces that the sweep holds every declaration the
  parse registers; nothing states the converse as one fact. Found by reading, 2026-09-24.

## TODO
- code coverage during spec tests
- test for __chkstk
- advent of compiler optimization
- 2 types of Stringable, formatted and not formatted
- // Use prevCp to avoid unused parameter warning (reserved for future Extended_Pictographic checks)
- warnings as errors in release mode
- Extra Inhabitants to optimize memory layout
- toLower/toUpper need to be unicode aware, maybe other string functions too
- add "implement interface" code action
- code actions should be directly linked to the errors that made them needed
- oh god locales
- optimize stack arrays (simd, bitmask filtering)
- dedup struct literals with COW ie = OpMeta{latency: 40}
- check for missing fields in struct literals
- tests for Process.executablePath longer than 1024 bytes
- add tests for compiler will all kinds of malformed inputs
- @embedFile from zig for multiline strings
- add "repl" to maxon
- add "lint" to maxon
- add "docs" to maxon

## Ideas
- codelens to show the complexity/cost of a function
- reorganize structs to improve cache locality
- have a command line options stdlib that supplies all the common CLI features (flags, parameters, validation)
  and you just get a type back with everything filled in
- live process monitor (memory allocations, etc)

- how to have the language prevent users doing this
The Trap: If you make an O(n) operation look like a property (s.count), a user might innocently write for i in 0..s.count, inadvertently creating an O(n²) loop because the language recalculates the count on every iteration.


### AI Assistance

1. Provide an llms.txt File
This is an emerging standard (used by projects like Svelte) specifically for AI consumption. While humans like formatted HTML, AI agents perform better with a linear, high-density markdown file.

What it is: A file located at /llms.txt on your docs site.

Why it works: It acts as a curated "brain dump" that agents can ingest in one go, stripping away UI noise and navigation, and focusing purely on syntax, API signatures, and rules.

2. Build a Language Server Protocol (LSP)
Since you are already deep into compiler architecture (IR stages, register allocation, etc.), building an LSP is the "gold standard" for AI effectiveness.

The AI Connection: When an agent (like Cursor or GitHub Copilot) interacts with a codebase, it uses the LSP to understand the symbol graph.

Why it's better than docs: An LSP provides real-time semantic validation. If an AI suggests code that violates your memory management rules, the LSP will flag the error immediately. This allows the agent to "self-correct" before it ever presents the code to you.

3. Create a "Synthetic Golden Dataset"
AI agents struggle with a new language because they lack "intuition" for common patterns. You can bridge this by generating a synthetic dataset of Prompt + Correct Code + Explanation triplets.

Seed Examples: Write 50–100 high-quality "idiomatic" examples covering everything from basic loops to your specific string handling and iterators.

Chain-of-Thought (CoT) Guides: For complex features (like your custom memory management), provide examples that include the "internal monologue" of how to solve a problem in your language.

Example: "To process this list, I must first initialize the iterator because in [Your Language], iterators are stateful..."

4. Grammar-Based Constraints (BNF/Tree-sitter)
If you provide an agent with your language's formal grammar (like a Tree-sitter parser or a BNF file), it can use that to ensure the code it generates is syntactically valid.

Many advanced AI agents can use these files to "constrain" their output, preventing them from hallucinating keywords or syntax from other languages like C# or Rust.

5. Specialized "System Instructions" for the Compiler
Since you're building the compiler itself, you can provide the AI with a "Mental Model of the IR." * Instead of just showing the surface syntax, explain why the compiler expects certain patterns for optimization. When an AI understands the underlying architecture (like how you handle phi nodes), it is less likely to write "clever" code that the compiler can't actually lower to machine code efficiently.
- `--dry-run` and `--dev` given to a command that does not take them are refused as "unknown option" (maxon-bin/Main.maxon, MaxonArgs.parse dryRunFlag/devFlag arms): both are known options, just not of that command, so the message names the wrong fault. Seen reading the option-refusal paths during the --filter union review.
- specs/*.md prose still narrates history in places the shelf removal did not own: milestone "slice" framing (e.g. specs/basics.md "M1 slice"), specs/enum-union-method-receiver.md:377 ("used to pin"), specs/field-declared-unknown-type.md:264, and the W90/G19 paragraphs of specs/array-conditional-conformance-withheld.md. Seen by a spec-prose pass; the no-history rule says each should state the current state only.
- On x64 the ordered pair (`StdOp.loadAcquire`/`storeRelease`) is excluded from two folds in maxon-bin/Compiler/Targets/Shared/StdLoweringShared.maxon — the memory-destination RMW fold (`recordOp`, ~L2040) and the address fusion (`recordMemoryFold`, ~L1263) — because arm64's `ldar`/`stlr` take only `[Xn]`. On x64 an ordered access is a plain `mov`, so both folds would be sound there: `__ds_commit`, the debug-stream padding publish and `__shm_read_word`/`__shm_write_word` each pay one unfused instruction on x64 since they became ordered. Seen reviewing the debug-stream ordering change; a target-aware fold for the ordered pair would restore them.
- maxon-bin/maxon.maxproj `rebuildWithOutput` decides from the building compiler's COMMIT, so while an emitted-runtime edit is uncommitted every `run build` compiles twice even when the slot already carries that exact runtime (a C2 of the current tree). Stamping a digest of the emitted-runtime sources into the compiler (beside `Compiler.CompilerCommit`) and comparing it with the tree's would make the answer exact. Seen reviewing the automatic second self-compile.
- The MCP `build` tool with `path: "maxon-bin"` is a PATH build (maxon-bin/Compiler/Mcp/McpTools.maxon ~241-280): no manifest, so no version stamp and no automatic second self-compile — yet maxon-bin/AGENTS.md's tool table and the maxon-coder and land skills name it as the way to build the compiler. Seen reviewing the automatic second self-compile.
- tests/buildManifest's rebuild-with-output cases inherit the caller's environment, so a caller that already carries `MAXON_SECOND_STAGE` (e.g. running the corpus from inside a second-stage build) fails them; the stdlib `Environment` API cannot remove a variable from a child's environment. Seen reviewing the automatic second self-compile.
- The soleness proof keeps a value's fresh-return mark through an immutable rebind (`let a = fresh`), so `valueIsSolelyOwnedHere` can answer "sole" while a second name still reads the record; the registry/SharedValue publish door patches over it with a live-name count (maxon-bin/Compiler/Parser.maxon `requirePublishedValueNamedOnce`). A message-door case would show whether a move can leave a readable alias: `var v = X.create(); let a = v; h.send(v); print(a.n)`. Seen reviewing the service registry.
- The parser and the checker each score overload candidates (maxon-bin/Compiler/Parser.maxon `sweptOverloadAmong`/`sweptOverloadArgFit` and maxon-bin/Compiler/SemanticCheck.maxon `overloadArgTypeFit`); both carry the `T.handle` to `I.handle` widening. A divergence costs only a false E3005, never a wrong binary. Seen reviewing the service registry.
- A `SharedValue` box steps both its own refcount and the cell's refs; any path that retains it with a plain `__mm_incref` instead of `__shv_box_retain` would drop the cell early. No failing program found; the same two-count shape service handles use. Seen reviewing the service registry.
- A static-call statement now parses through `parsePostfix`, so `Foo.make().field` alone on a line is accepted, as receiver chains already are; a dead-expression statement check would refuse both. Seen reviewing the service registry.
- tests/warmRebuild times out 2/2 at its 10 s limit on x64-windows, identically with a compiler built from 1d3610cc8. Seen during the log-as-a-service optimization pass.
- The out-of-process debugger's per-thread bookkeeping is O(n^2) in the debuggee's OS-thread count: `WindowsBackend.handleOf` scans the `threads` array and `forgetThread` rebuilds it whole (maxon-bin/Debug/Backend/WindowsBackend.maxon:398,426). n is the debuggee's M count, which the scheduler bounds by the processor count, so it is measured-linear for any real subject; the trigger that would make it bend is a debuggee churning thousands of OS threads. Seen in the independent review of the out-of-process debugger change, and agreed as debt rather than defect.
- `DebugRuntimeContract.figureIn` and `globalAt` are hand-rolled linear scans over parallel name/value columns, one scan per lookup (maxon-bin/Debug/Backend/DebugRuntimeContract.maxon). ~40 rows and a per-command call rate, so it does not show; a map would remove the pattern the optimize skill names. Seen in the same review.
- `DebugEngine.greenThreadRecordOf` calls both `machineRunning` and `machineOfTheStoppedThread`, each an O(M) walk of `__sched_allm`, once per green thread in `listGreenThreads` — O(N*M) twice per roster. Fine at real N and M, and both walks are now bounded, but one walk answering both questions would do. Seen in the same review.
- `emitLoadGlobalWord` emits `leaRegGlobal` + `loadRegBaseDisp` where x64 has a single RIP-relative `mov`, so every runtime global read in every program pays two instructions instead of one (maxon-bin/Compiler/Targets/Shared, the global-word load helper). Pre-existing and reaching far beyond any one change; folding it is an isel change next to the register allocator's repair cliff. Seen in the optimization pass over the out-of-process debugger change.
- `SidecarProvenance.originOf` resolves the same file's provenance twice per function chunk, once through `isRuntimeSourceFile` and again through `isLibrarySourceFile` (maxon-bin/Compiler/Debug/MxdbgEmit.maxon:689). Memoized, so ~2 allocations per function and about half of a measured +64k at scale rung 5 (0.03% of the compile); collapsing it wants one exported `fileProvenanceOf` in Queries.maxon with both bits read from it, rather than the library rule restated at the call site. Seen in the same optimization pass.
- `WindowsBackend.waitForStop` does not filter debug events by process id: it records `pendingProcessId` and never compares it with `self.processId` (maxon-bin/Debug/Backend/WindowsBackend.maxon). `WaitForDebugEvent` is per debugging thread, so two debuggees on one thread cross their events — the one producer found (MCP launching a replacement before reaping the live session) was removed, leaving no red case, but stage 2's `--pid` attach would reintroduce the shape. Delve demultiplexes on pid and this backend should too. Seen in the independent review.
- The debugger driver cannot read a named runtime global out of a debuggee, so the `__gt_hold_epoch` pin-count invariant is unwitnessed. The pieces are nearly all there: `__gt_hold_epoch` is in `debuggerReadableGlobals` and so is published in the sidecar's globals table with an address, and a staged `*.maxtest` may legally spell `__Builtins.pinToMachine()` / `__Builtins.unpinFromMachine()` under the `Parser.fileMayUseReservedNames` exception. What is missing is a driver-side read of a named global (a `gt`/`print` over `debuggerReadableGlobals`); with it, a fixture that pins once and unpins twice gives a deterministic gate asserting the epoch is 0 rather than -1, instead of the flaky race a steal-observing case would be. Seen in the independent review of the out-of-process debugger change, which fixed the underflow but could not gate it.
- A parked green thread's pending-preemption state is not readable by the debugger: `GtOffStackGuard` doubles as the preemption request (`GtStackGuardPreempt`), both figures are published in the sidecar's layout block, but the engine's green-thread walk consults only `GtOffTibStackBase`, so a driver cannot tell "a preemption is pending on this parked thread" from a real guard value. The two rows are published and their name constants are file-private, which is honest only while `tests/debug/a-sidecar-names-the-globals-and-layout-tables.maxtest` pins them by literal string; narrow that case and the rows become unwitnessed payload. Seen in the same review.
- `EngineWalk` in `maxon-bin/Debug/Debugger/DebugEngine.maxon` is a one-case union, deliberately left a union rather than collapsed to a `type` while the stage-2 `--pid` attach may still give it a second case. If stage 2 adds none, collapse it to a `type`. Seen in the same review.
- x64-windows: `safety/force-segfault-on-a-green-thread` exited 0xC0000005 (the VEH never converted the fault) instead of
  1, ONCE, in CI run 36625289916 on runner image `win25-vs2026` 20260925.250.1 — the only sample on that image; 29
  earlier runs on 20260922.246.2/20260907.229.1 passed. Not a change's doing: diag runs 36630522795 and 36631333478 built
  byte-identical exes from before and after that change and passed 60/60 direct + 30/30 spec-test each, but both
  landed on the OLD image. Needs a sample on 20260925.250.1 (the diag workflow keeps crash dumps). The VEH is the
  only converter: no `SetUnhandledExceptionFilter` exists in the tree.
- E3077 (`semanticVarShouldBeLet`) covers locals only, so a `var` field nothing writes goes unflagged — 21 such fields
  sat in `MxdbgReader.maxon` until c7a076e57. A user-approved design: extend E3077 to file-private and `module` `var`
  fields of the program's own types (`public`/`export` exempt, library tiers not audited in a user compile) with
  "field 'T.f' is never written; use 'let' instead of 'var'", every such field reported; `maxon check-stdlib`, which
  compiles every stdlib function as reached, is where stdlib's fields are audited. The test is "would `let` compile": a field needs `var` wherever a `let`
  field is refused — E2013 assignment after construction, E3159 write-through / mutating method / by-reference /
  writing parameter, E3078 a store a `let` field refuses, E3188 a read-out value reaching writable state — and that
  answer must come from `ProvenanceTracer` treating each audited field as a hypothetical read-only root, not from a
  second analysis. Converting a field can make its type an all-`let` value type and inline-packable
  (`inlineRecordShapeOf`), and `is` on a packed type is E3068, so an identity comparison is a needs-`var` door too.
  A first implementation (parallel `FieldFlow` pass) was abandoned in review for duplicating the tracer and missing
  writes through `holdValue` and through records appended into a writable container. A landing owes a
  `## Unreleased` entry (Fix: declare the field `let`), the E3077 registry doc, LANGUAGE_REFERENCE's rule, and the
  conversion of every field the rule names (in that attempt: 37 in maxon-bin, 44 in stdlib, and ~400 lines of
  spec/test/example programs whose subject was not the field).
- `maxon build stdlib/URL.maxon` (and `stdlib/Clock.maxon`) panics at `SemanticCheck.maxon` `hiddenSymbolDiagnostic`
  via `Parser.recordHiddenTypeName` — a compiler panic on a stdlib file built as a program; reproduces with the
  previous compiler too. `maxon build stdlib` refuses with E3005 (`String.maxon:112`, `managed` holding `ByteMemory`)
  and E2015 (`FixedArray.maxon:12`).
- x64-windows `ir-specs` pin `a-getter-result-kept-across-a-store-to-its-field-is-counted`
  (ir-specs/borrowed-field-getters.md) does not match what the compiler renders at HEAD; minting it on the lane fixes
  it once the render is confirmed right.
- `DebugEngine.closeLeavingItRunning` on a `detachFailed` engine asks the kernel to detach twice:
  `clearWhatCanBeClearedAndLetGo` retries once, then the fallback `close()` runs it again. And a `detachFailed`
  program that ptrace only partly detached may sit stopped with DAP watching it until `quit` (found by reading).
- Against Go 1.27.3 on arm64 (docs/concurrency-benchmark-log.md, 8028c639c rows) every workload is at or under Go
  except spawn-many at P>1 (2.21x macos, 1.15x orb, as x64's 2.1-2.7x) and peak working set of spawn-many (2-3x) and
  coroutine-churn (1.4-1.7x), the same shape as x64. The record-resident context switch (8028c639c) is unmeasured on
  x64: the next windows/wsl run should A/B it.
- Reference capabilities, stage 2: the Pony-style declared design was cancelled on 2026-10-24. The capability model
  (`C:\Users\Eric\Dev\capmodel`, local only and never pushed, up to 39e94b1) keeps three findings about Maxon's own
  runtime, which any replacement design must answer. None of the three has been checked against a HEAD compiler:
  - **Target first.** A write `x.b.n = f(…)` resolves its target chain before the right-hand side runs. If `f` swaps
    `x.b` out and sends it to a service, the write lands in an object that another thread now owns, so a
    cross-thread write results. Pony evaluates the right-hand side first. The model's repro is in
    capmodel `redteam3/rev6/REPORT.md`, H1.
  - **Write-back while a release parks.** A match-binding write-back can have its new value freed by another
    coroutine while the release of the old value parks. The cure is to count the binding before releasing the old
    value (H2).
  - **Lingering counts after a push.** A call after a push can yield, or run an awaited coroutine, that sends the
    pushed element while the local's count still lingers (H3).
- `__module_init`'s synthesized calls pass a bare value to an inferred by-reference parameter (ModuleInit
  `calledFactoryRecord` ~552, `emitMapEntryUpsert` ~462, `builtArrayLiteralRecord` ~440). `let g = Tag from "hello"`,
  where `init` reassigns its parameter, prints `!` and exits 101. Inferred by-reference also reaches stdlib
  parameters by name coincidence (`DeclNodeSpace` `pushDottedTargets`). The call sites a parse synthesizes were fixed
  in 1b9466d24b.
- Unverified: a member that writes its receiver may drop the receiver bit from its summary (StorageProvenance ~4141:
  `inside` loses the receiver when the returned value is `frozen` and the callee writes its receiver). If so,
  `let e = try self.items.get(0); self.reads = self.reads + 1; return e` on a read-only-element instance is not
  refused.
- An overloaded generic constructor skips the escaping-borrow feed (Parser ~101194, `applyCallerConsume`
  `'everyDeclarationBorrowsIt'`). When every declaration borrows a column, an escaping borrowed feed never reaches
  `handleEscapingBorrowFeed`, so a borrowed trivial struct box may be stored without a retain. The tuple spawn hand-off
  is skipped on the same road. Unverified repro: `Box uses T` with `create(item T)` beside `create(item T, tag Num)`,
  both storing `item`, called with a borrowed `Point`, both plain and as `spawn`.
- A coOwned `var` cell holding a trivial tuple is refused at a send instead of copied (`handOffTakeOf`'s
  `'aCellsOccupant'` arm runs before the copy arm). The shape has not been built yet, because a closure capture moves
  the `var` (E3102).
- Latent use-after-free: `markStoredElementResult` decides "stored element" from the return type
  (SignatureIndex ~31760 `storedElementPositionOf`). On an image `let`, every library method returning `T` is marked
  `imageRecord` and its hold is elided. That is sound for today's stdlib, but a future `f(x T) returns T` that returns
  its argument would break it. Decide per method, from the body summary or a roster.
- E3078 wording splits:
  - Permanent-view values get `viewProducer`'s sharing form (StorageProvenance ~5431, ~5749) in some places, and
    `readOnlyRootSubject("", viewRootKind)` (~6334, ~9489) in others.
  - Bindings read "the read-only 'x'" at the binding door, and "'x', which is read-only (kind)," elsewhere.
  - `rootKindOfBlame` (~7318) defaults silently to `letBinding`.
- An overloaded factory on a bare generic base leaks (exit 101): `Box.create(p)` with no typealias, where `Box` declares
  `create(item T)` and `create(item T, tag Num)`. `spawnFactoryConstructorInstance` (Parser ~75108) returns the
  uninstantiated base, so no `__destruct_Box_Point` releases the `Point`. Infer from the declarations `slotCallArgs`
  leaves, and refuse only when two or more still slot.
- The opaque-overload refusal (E2015 "resolving an overload of 'add' against an argument of opaque generic type") has
  two defects:
  - its text says "whose concrete type is managed", which is false, since there is no managed/trivial split;
  - it cascades into a false E3005, because the op stays on the first member and `checkCalls` re-checks it.

  Repro: `Holder uses T` whose `put(item T)` calls an overloaded `Bag.add` with `tag: 1` and `tag: "x"`.
- SemanticCheck's merge-incoming scan is quadratic (~570-593, `pushBlockArgumentInputs` / `pushEdgeInputs`).
  The same pairing of edge values with block arguments is walked separately by:
  - `EffectSummary.noteMergeEdges`
  - ClosureCycleCheck (~765)
  - `Parser.phiIncomingValues` (~46069)
  - `LoopEdgeIndex.noteEdge` (~14542)
  - StorageProvenance's `indexEdgePairs`

  One IR-level merge-incomings index should answer all of them.
- Possible E3077 false negative: `storeOwnedValueIntoDurableSink` (Parser ~74220) marks the owner's mutability as
  exercised even when the borrowing overload of a multi-declaration call is the one bound.
- E3078 gives two verdicts for one borrowed union payload stored into a `let` field. A payload reached through a
  parameter of the filing function is refused, while the same payload taken from a `for` element of a parameter
  array is accepted. One of the two is wrong: write both shapes as cases and decide.
- E3138 refuses a service-message argument filled from a default, even an interpolated one:
  `take(first String, second String = "{rebindCurrent()}")` called as `h.take(current.label)`.
- `async intBox.work()`, `async` of a method on a concrete generic instance, is refused although the program is
  legal. The spawn trampoline passes user arguments only, so the spawn should carry the layout descriptor
  (`settleLayoutCarriers`'s spawn loop).
- A closure that calls a witness-carrying static is refused (E2015) although the program is legal. The closure should
  capture the witness tables. It is pinned as `where-clauses` `a-closure-calling-a-witness-carrying-static-is-refused`.
- An alias-typed argument ties between `f(x T)` and `f(x Whole)` with E3007 (`Box.create(7 as Whole)`), because the
  alias stays in the type tag. A `Whole` argument to a `Whole` parameter should be exact, since aliases are nominal.
  It is pinned as E3007 by `an-overloaded-factory-called-with-an-alias-valued-argument-both-members-take-equally-is-ambiguous`.
- Module-scope factories through a generic-instance alias (`let b = IntBox.make(5)`, `IntBox.create(…)`, nested
  `Outer.create(IntBox.make(4))`), imaged as image data. A saved patch from the E2078 change has four open review
  findings:
  - the deep-clone verdict skips the field-graph check (a `Directory` field panics at MmRuntime:4185);
  - `imagedInstanceBoxes` is read before it is filled;
  - the library parse cache does not key on that set;
  - SharedValue instances are not excluded.
- `Self.wrap(x)` inside a generic body, returning a bare generic of another type, is a false E3005 ("Cannot return
  'Holder' from function declared to return 'Holder'"). A static with no receiver reaches no source in
  `instanceSourceOfCall`.
- Generic templates vanish for a name declared in two directories. Template keys stay bare (`describe`) while the
  non-generic declarations are directory-qualified (`ca.describe`), so `isGenericFunctionKey` is false.
- `paramDefaultsCollide` over-enforces: two independent file-private helpers in two files of one directory, one with a
  default, are refused.
