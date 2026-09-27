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
- ⛔ `scale-test --repeat=N` (N>=2) REPORTS THE COMPILER NONDETERMINISTIC, and it is the REPEAT that is
  nondeterministic rather than the compiler. Measured 2026-09-07 on BOTH this tree and a build of
  origin HEAD, so it is not new: `scale-test --repeat=3` fails as a BROKEN RUN at whichever rung it
  reaches first, always with the same signature — the later compile of one rung reports exactly +4
  allocs, +4 frees and +4,453 bytes (e.g. rung 3: 17,098,076/13,355,416/1,366,115,712 then
  17,098,080/13,355,420/1,366,120,165). ⭐ THREE SEPARATE PROCESSES AGREE BIT FOR BIT
  (`scale-test --rungs=4 --result-json` x3, identical), so the difference is state carried from one
  compile to the NEXT INSIDE ONE PROCESS, and it grows rather than shrinks — not a lazily-built cache
  the first compile pays for. Still failing 2026-09-24 with a different signature, at rung 0:
  7,719,592 / 7,242,211 / 771,993,049 then 7,719,580 / 7,242,199 / 771,996,347 (allocs / frees /
  bytes). Consequence: no two-compiler `scale-test` ratio table can be built, and a CPU A/B has to take
  its samples as separate processes.
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
- tests/build-manifest's rebuild-with-output cases inherit the caller's environment, so a caller that already carries `MAXON_SECOND_STAGE` (e.g. running the corpus from inside a second-stage build) fails them; the stdlib `Environment` API cannot remove a variable from a child's environment. Seen reviewing the automatic second self-compile.
- The soleness proof keeps a value's fresh-return mark through an immutable rebind (`let a = fresh`), so `valueIsSolelyOwnedHere` can answer "sole" while a second name still reads the record; the registry/SharedValue publish door patches over it with a live-name count (maxon-bin/Compiler/Parser.maxon `requirePublishedValueNamedOnce`). A message-door case would show whether a move can leave a readable alias: `var v = X.create(); let a = v; h.send(v); print(a.n)`. Seen reviewing the service registry.
- The parser and the checker each score overload candidates (maxon-bin/Compiler/Parser.maxon `sweptOverloadAmong`/`sweptOverloadArgFit` and maxon-bin/Compiler/SemanticCheck.maxon `overloadArgTypeFit`); both carry the `T.handle` to `I.handle` widening. A divergence costs only a false E3005, never a wrong binary. Seen reviewing the service registry.
- A `SharedValue` box steps both its own refcount and the cell's refs; any path that retains it with a plain `__mm_incref` instead of `__shv_box_retain` would drop the cell early. No failing program found; the same two-count shape service handles use. Seen reviewing the service registry.
- A static-call statement now parses through `parsePostfix`, so `Foo.make().field` alone on a line is accepted, as receiver chains already are; a dead-expression statement check would refuse both. Seen reviewing the service registry.
- tests/warm-rebuild times out 2/2 at its 10 s limit on x64-windows, identically with a compiler built from 1d3610cc8. Seen during the log-as-a-service optimization pass.
- The out-of-process debugger's per-thread bookkeeping is O(n^2) in the debuggee's OS-thread count: `WindowsBackend.handleOf` scans the `threads` array and `forgetThread` rebuilds it whole (maxon-bin/Debug/Backend/WindowsBackend.maxon:398,426). n is the debuggee's M count, which the scheduler bounds by the processor count, so it is measured-linear for any real subject; the trigger that would make it bend is a debuggee churning thousands of OS threads. Seen in the independent review of the out-of-process debugger change, and agreed as debt rather than defect.
- `DebugRuntimeContract.figureIn` and `globalAt` are hand-rolled linear scans over parallel name/value columns, one scan per lookup (maxon-bin/Debug/Backend/DebugRuntimeContract.maxon). ~40 rows and a per-command call rate, so it does not show; a map would remove the pattern the optimize skill names. Seen in the same review.
- `DebugEngine.greenThreadRecordOf` calls both `machineRunning` and `machineOfTheStoppedThread`, each an O(M) walk of `__sched_allm`, once per green thread in `listGreenThreads` — O(N*M) twice per roster. Fine at real N and M, and both walks are now bounded, but one walk answering both questions would do. Seen in the same review.
- `emitLoadGlobalWord` emits `leaRegGlobal` + `loadRegBaseDisp` where x64 has a single RIP-relative `mov`, so every runtime global read in every program pays two instructions instead of one (maxon-bin/Compiler/Targets/Shared, the global-word load helper). Pre-existing and reaching far beyond any one change; folding it is an isel change next to the register allocator's repair cliff. Seen in the optimization pass over the out-of-process debugger change.
- `SidecarProvenance.originOf` resolves the same file's provenance twice per function chunk, once through `isRuntimeSourceFile` and again through `isLibrarySourceFile` (maxon-bin/Compiler/Debug/MxdbgEmit.maxon:689). Memoized, so ~2 allocations per function and about half of a measured +64k at scale rung 5 (0.03% of the compile); collapsing it wants one exported `fileProvenanceOf` in Queries.maxon with both bits read from it, rather than the library rule restated at the call site. Seen in the same optimization pass.
- `WindowsBackend.waitForStop` does not filter debug events by process id: it records `pendingProcessId` and never compares it with `self.processId` (maxon-bin/Debug/Backend/WindowsBackend.maxon). `WaitForDebugEvent` is per debugging thread, so two debuggees on one thread cross their events — the one producer found (MCP launching a replacement before reaping the live session) was removed, leaving no red case, but stage 2's `--pid` attach would reintroduce the shape. Delve demultiplexes on pid and this backend should too. Seen in the independent review.
- The debugger driver cannot read a named runtime global out of a debuggee, so the `__gt_hold_epoch` pin-count invariant is unwitnessed. The pieces are nearly all there: `__gt_hold_epoch` is in `debuggerReadableGlobals` and so is published in the sidecar's globals table with an address, and a staged `*.maxtest` may legally spell `__Builtins.pinToMachine()` / `__Builtins.unpinFromMachine()` under the `Parser.fileMayUseReservedNames` exception. What is missing is a driver-side read of a named global (a `gt`/`print` over `debuggerReadableGlobals`); with it, a fixture that pins once and unpins twice gives a deterministic gate asserting the epoch is 0 rather than -1, instead of the flaky race a steal-observing case would be. Seen in the independent review of the out-of-process debugger change, which fixed the underflow but could not gate it.
- A parked green thread's pending-preemption state is not readable by the debugger: `GtOffStackGuard` doubles as the preemption request (`GtStackGuardPreempt`), both figures are published in the sidecar's layout block, but the engine's green-thread walk consults only `GtOffTibStackBase`, so a driver cannot tell "a preemption is pending on this parked thread" from a real guard value. The two rows are published and their name constants are file-private, which is honest only while `tests/debug/a-sidecar-names-the-globals-and-layout-tables.maxtest` pins them by literal string; narrow that case and the rows become unwitnessed payload. Seen in the same review.
- `maxon-bin/Debug/Debugger/DebugEngine.maxon:1232`'s `or self.walkHoldsTheByteAt(address)` disjunct is subsumed by the `self.returnPointAt(address)` beside it: both reduce to `point.address == address`, because `plantReturnPoint` sets `offset = point.address - self.textBase` at both pushes, `textBase` is written once at construction and never reassigned, and the three rebuild loops carry `offset` through unchanged. So the second disjunct is `returnPointAt(address) and holdsTheWalksHold()`, strictly weaker than the first, which has already fired. That makes `holdsTheWalksHold`, `walkHoldsTheByteAt` and the whole `EngineReturnPoint.heldOverACondition` payload — with `withoutTheHoldAt`, `heldIfAt`, `unheldPointAt`, `unheldReturnPointAt`, `returnPointHeldOver` and `forgetTheHoldAt` behind them — reachable but unable to affect any answer. It is dead computation, not a wrong answer (`conditionMissed: true` is returned either way). ⚠ Decide one thing first: whether the INTENT was that a held byte should stop the walk in a case a plain return point would not. If so the guard is not redundant but WRONG, and the fix is to correct the address test rather than delete the machinery; no reading of `plantReturnPoint` was found under which the two addresses differ. Seen in the independent review of the out-of-process debugger change, which judged the cascade (about half the return-point machinery) too large to ride along on the last pass before a battery.
- `EngineReturnPoint` and `EngineWalk` in `maxon-bin/Debug/Debugger/DebugEngine.maxon` are both one-case unions. `EngineReturnPoint` lost its `doubted` case when the out-of-process backend stopped answering `unacknowledged`; it was deliberately left a union rather than collapsed to a `type`, because five of its methods are whole-value transformers whose `Self{…}` rebuilds would name every field anyway (so the ceremony moves rather than shrinks), and because a stage-2 `--pid` attach can hand the driver a return point whose frame it cannot confirm, which is structurally the case that just died. If stage 2 adds no second case to either, collapse both to `type`s. Seen in the same review.
