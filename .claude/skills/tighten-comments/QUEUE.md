# tighten-comments — work queue

**Seeded by ENUMERATION, not by selection.** Every `.maxon` file under `maxon-shv2/Compiler/` has a
row, generated once from `find maxon-shv2/Compiler -name '*.maxon' | sort`. Membership is never
decided by a predicate, so no file can be missed by one. Temporary — deleted with the skill.

⚠ **The parent process owns this file. A subagent never edits it.** Five agents writing one table is
a lost-update race, and a silently dropped row is the failure this ledger exists to prevent.

`status`: `todo` · `in-progress` · `done` · `no-change` (examined, already conforming) ·
`refused: generated`.

`score` is lines matching `⭐|⚠|⛔|**|MEASURED|2026-|used to|no longer|(W#|(A#|(SV#`. It **orders**
the todo rows; it never decides membership, and it is never zeroed for its own sake — a `⚠` survivor
is expected, since the rules permit one per block.

## Before every batch: reconcile the ledger against the filesystem

```
find maxon-shv2/Compiler -name '*.maxon' | tr -d '\r' | sort              > temp/tc-disk
grep -oE 'maxon-shv2/Compiler/[A-Za-z0-9_/.-]+\.maxon' QUEUE.md | sort -u > temp/tc-queue
diff temp/tc-disk temp/tc-queue     # MUST be empty — a new or renamed file shows up here
```

The ledger records what a predicate cannot express (`no-change`, partial ranges, refusals, the
landing commit); this derivation proves the ledger still covers everything. Neither is trusted alone.

## Batches

| # | Scope | Files | Lines |
|---|---|---|---|
| 1 | Pilot (`Queries.maxon`) + the three worst-ratio files | 4 | 12,025 |
| 2 | The bulk | 164 | 170,713 |
| 3 | `Project.maxon`, `SemanticCheck.maxon` | 2 | 14,557 |
| 4 | `SignatureIndex.maxon`, `Parser.maxon` — ranged passes; decide on batch 2's measured cost | 2 | 111,416 |
| — | Generated, refused | 2 | 7,676 |

Totals: **176 files, 306,347 lines, 169,086 comment lines (55%).**

## Queue

| batch | file | lines | comments before → after | score | status | ranges done | commit |
|---|---|---|---|---|---|---|---|
| 4 | `maxon-shv2/Compiler/Parser.maxon` | 82550 | 50522 → | 9137 | todo | | |
| 4 | `maxon-shv2/Compiler/SignatureIndex.maxon` | 28866 | 17857 → | 3349 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/SchedRuntime.maxon` | 6069 | 3839 → | 1029 | todo | | |
| 3 | `maxon-shv2/Compiler/Project.maxon` | 7909 | 5259 → | 959 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/GtRuntime.maxon` | 9045 | 4418 → | 807 | todo | | |
| 3 | `maxon-shv2/Compiler/SemanticCheck.maxon` | 6648 | 3625 → | 623 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ManagedMemoryRuntime.maxon` | 5348 | 2627 → | 500 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/LowerMaxonToStd.maxon` | 5176 | 2918 → | 423 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/MmRuntime.maxon` | 4398 | 2259 → | 411 | todo | | |
| 1 | `maxon-shv2/Compiler/IR/Std/StdDialect.maxon` | 4389 | 3015 → | 388 | todo | | |
| 1 | `maxon-shv2/Compiler/IR/Maxon/TypeRules.maxon` | 3214 | 2242 → | 321 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/IrInterface.maxon` | 2425 | 1519 → | 288 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/StdToX64Conversion.maxon` | 4673 | 2374 → | 235 | todo | | |
| 2 | `maxon-shv2/Compiler/StdlibSource.maxon` | 1677 | 1039 → | 233 | todo | | |
| 1 | `maxon-shv2/Compiler/Queries.maxon` | 1843 | 1482 → 925 | 232 | done | | |
| 2 | `maxon-shv2/Compiler/ConformanceCheck.maxon` | 2478 | 1378 → | 231 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/SlabRuntime.maxon` | 2179 | 1016 → | 221 | todo | | |
| 2 | `maxon-shv2/Compiler/Compiler.maxon` | 1939 | 1335 → | 206 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/StdToArm64Conversion.maxon` | 2833 | 1462 → | 204 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ListRuntime.maxon` | 1952 | 850 → | 187 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/X64LinuxRuntime.maxon` | 3620 | 1126 → | 186 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/StringRuntime.maxon` | 1658 | 925 → | 176 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/MaxonDialect.maxon` | 1782 | 1395 → | 176 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/SlabArena.maxon` | 1809 | 872 → | 168 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64LinuxRuntime.maxon` | 2719 | 1028 → | 167 | todo | | |
| 2 | `maxon-shv2/Compiler/TypeResolution.maxon` | 1411 | 963 → | 160 | todo | | |
| 1 | `maxon-shv2/Compiler/IR/Target/TargetDialect.maxon` | 2012 | 1584 → | 158 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Wasm/StdToWasm.maxon` | 3293 | 1553 → | 153 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/SplitLiveRanges.maxon` | 6488 | 3125 → | 153 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/InsertRangeChecks.maxon` | 1611 | 964 → | 149 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ManagedSocketRuntime.maxon` | 2019 | 654 → | 128 | todo | | |
| 2 | `maxon-shv2/Compiler/ServiceCompanions.maxon` | 1094 | 558 → | 127 | todo | | |
| 2 | `maxon-shv2/Compiler/TargetFacilities.maxon` | 840 | 648 → | 123 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/X64GtRuntime.maxon` | 1660 | 875 → | 120 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64DarwinRuntime.maxon` | 2074 | 841 → | 120 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/ModuleInit.maxon` | 1041 | 589 → | 111 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/DebugStreamRuntime.maxon` | 1376 | 673 → | 111 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/SubprocessRuntime.maxon` | 2467 | 641 → | 111 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/BuiltinConformanceRuntime.maxon` | 1033 | 608 → | 108 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ManagedFileRuntime.maxon` | 1321 | 563 → | 101 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/BufferOwnership.maxon` | 693 | 532 → | 101 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/RuntimeUsage.maxon` | 1245 | 825 → | 99 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/StdLoweringShared.maxon` | 1659 | 879 → | 98 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/InlineManagedPrimitives.maxon` | 965 | 512 → | 97 | todo | | |
| 2 | `maxon-shv2/Compiler/ParseStaging.maxon` | 1590 | 875 → | 95 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/TargetLiveness.maxon` | 3961 | 1860 → | 93 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/IrFunction.maxon` | 850 | 589 → | 93 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/InlineLeaves.maxon` | 1360 | 506 → | 92 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/LinuxRuntime.maxon` | 703 | 476 → | 86 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/FoldConstants.maxon` | 813 | 467 → | 84 | todo | | |
| 2 | `maxon-shv2/Compiler/BorrowCheck.maxon` | 615 | 396 → | 82 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64Backend.maxon` | 2696 | 1203 → | 81 | todo | | |
| 2 | `maxon-shv2/Compiler/UnusedExportCheck.maxon` | 961 | 405 → | 79 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/RegisterAllocator.maxon` | 2583 | 1239 → | 77 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/GlobalDataTable.maxon` | 854 | 528 → | 75 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ManagedDirectoryRuntime.maxon` | 931 | 416 → | 75 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/LayoutDescriptor.maxon` | 745 | 432 → | 74 | todo | | |
| 2 | `maxon-shv2/Compiler/StdlibLoader.maxon` | 397 | 350 → | 72 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/RuntimeAbort.maxon` | 431 | 361 → | 70 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Windows/PeWriter.maxon` | 1302 | 578 → | 69 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/X64Backend.maxon` | 2467 | 1342 → | 69 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/LoopInvariantCodeMotion.maxon` | 944 | 468 → | 66 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/PosixRuntime.maxon` | 541 | 340 → | 65 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/MailboxRuntime.maxon` | 699 | 293 → | 64 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64PosixRuntime.maxon` | 992 | 344 → | 62 | todo | | |
| 2 | `maxon-shv2/Compiler/QueryEngine.maxon` | 435 | 308 → | 61 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/StrengthReduceDivision.maxon` | 772 | 364 → | 58 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Target/TargetPrinter.maxon` | 1228 | 587 → | 57 | todo | | |
| 2 | `maxon-shv2/Compiler/Lexer.maxon` | 1931 | 677 → | 57 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ServiceLoop.maxon` | 555 | 284 → | 57 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/AsyncReleaser.maxon` | 604 | 310 → | 57 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64GtRuntime.maxon` | 875 | 402 → | 56 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/DeadFunctionElimination.maxon` | 522 | 355 → | 51 | todo | | |
| 2 | `maxon-shv2/Compiler/ServiceGlobalAccessCheck.maxon` | 395 | 200 → | 47 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/PromoteStackRecords.maxon` | 1163 | 413 → | 45 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/GraphemeRuntime.maxon` | 536 | 280 → | 45 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/CommonSubexpressionElimination.maxon` | 672 | 319 → | 43 | todo | | |
| 2 | `maxon-shv2/Compiler/PromiseType.maxon` | 213 | 155 → | 41 | todo | | |
| - | `maxon-shv2/Compiler/ErrorCodeRegistry.maxon` | 1723 | 1550 → | 40 | refused: generated | | |
| 2 | `maxon-shv2/Compiler/Formatter.maxon` | 1545 | 447 → | 38 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/CheckedDivisionRuntime.maxon` | 351 | 221 → | 37 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/PassPipeline.maxon` | 510 | 342 → | 37 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/FoldConstOperands.maxon` | 679 | 376 → | 37 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/CommandLineRuntime.maxon` | 811 | 234 → | 37 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/IrModule.maxon` | 800 | 406 → | 36 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/DeadGlobalElimination.maxon` | 219 | 169 → | 34 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ProcessRuntime.maxon` | 252 | 157 → | 34 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/EnumLookupRuntime.maxon` | 453 | 208 → | 33 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/FunctionNameIndex.maxon` | 235 | 165 → | 32 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/OsImportSlot.maxon` | 284 | 208 → | 32 | todo | | |
| 2 | `maxon-shv2/Compiler/LiteralArgPromotion.maxon` | 514 | 236 → | 32 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/CpuParallelRuntime.maxon` | 232 | 166 → | 31 | todo | | |
| 2 | `maxon-shv2/Compiler/ServiceCallCycleCheck.maxon` | 369 | 168 → | 31 | todo | | |
| 2 | `maxon-shv2/Compiler/PhaseProbe.maxon` | 517 | 279 → | 31 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/TerminalRuntime.maxon` | 254 | 138 → | 30 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspHover.maxon` | 1173 | 245 → | 28 | todo | | |
| 2 | `maxon-shv2/Compiler/TreeLock.maxon` | 424 | 228 → | 27 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/RegAllocUnit.maxon` | 686 | 208 → | 27 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64Runtime.maxon` | 293 | 171 → | 26 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/BackendDispatch.maxon` | 593 | 357 → | 26 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/UnicodeCategoryRuntime.maxon` | 229 | 132 → | 25 | todo | | |
| 2 | `maxon-shv2/Compiler/ConditionalCompilation.maxon` | 562 | 254 → | 25 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Wasm/WasmBinary.maxon` | 1390 | 435 → | 24 | todo | | |
| 2 | `maxon-shv2/Compiler/ServiceProgramSurvey.maxon` | 295 | 102 → | 24 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/RegAllocPool.maxon` | 317 | 143 → | 24 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/RegisterPressureDiagnostic.maxon` | 626 | 279 → | 24 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/X64PrologueEpilogue.maxon` | 634 | 343 → | 24 | todo | | |
| 2 | `maxon-shv2/Compiler/Diagnostics.maxon` | 334 | 194 → | 23 | todo | | |
| 2 | `maxon-shv2/Compiler/TypeCycleCheck.maxon` | 408 | 154 → | 21 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/ValueOrigin.maxon` | 522 | 227 → | 20 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/HallCondition.maxon` | 766 | 390 → | 20 | todo | | |
| 2 | `maxon-shv2/Compiler/VerifyRecheck.maxon` | 284 | 126 → | 19 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ConsoleRuntime.maxon` | 150 | 87 → | 18 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspDiagnostics.maxon` | 187 | 90 → | 18 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/RegBits.maxon` | 487 | 273 → | 18 | todo | | |
| 2 | `maxon-shv2/Compiler/VerifyWarmRebuild.maxon` | 518 | 237 → | 18 | todo | | |
| 2 | `maxon-shv2/Compiler/LspPosition.maxon` | 278 | 118 → | 17 | todo | | |
| 2 | `maxon-shv2/Compiler/QueryDatabase.maxon` | 315 | 146 → | 16 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64RuntimeAsm.maxon` | 396 | 154 → | 16 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Macos/MachOWriter.maxon` | 1054 | 297 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/X64/X64Runtime.maxon` | 1235 | 514 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/BlockKeyedTable.maxon` | 161 | 117 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ClockRuntime.maxon` | 182 | 111 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/SlabClassTable.maxon` | 283 | 97 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspServer.maxon` | 327 | 82 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/Scope.maxon` | 417 | 240 → | 14 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspSymbols.maxon` | 522 | 124 → | 13 | todo | | |
| 2 | `maxon-shv2/Compiler/CompileTimings.maxon` | 686 | 414 → | 13 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/NaturalLoops.maxon` | 236 | 92 → | 12 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/FnRefThunk.maxon` | 277 | 138 → | 12 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspDefinition.maxon` | 318 | 66 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/NumberParsing.maxon` | 319 | 122 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/StdDominatorTree.maxon` | 380 | 123 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64PrologueEpilogue.maxon` | 416 | 200 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/CodeResult.maxon` | 488 | 214 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/ParallelBoundaryRuntime.maxon` | 86 | 62 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/SsaDestruction.maxon` | 959 | 267 → | 11 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/CsrGraph.maxon` | 392 | 114 → | 10 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Linux/ElfWriter.maxon` | 491 | 206 → | 10 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspCompletion.maxon` | 517 | 91 → | 10 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspMethod.maxon` | 180 | 59 → | 9 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Wasm/WasmComponent.maxon` | 232 | 88 → | 9 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspSemanticTokens.maxon` | 255 | 56 → | 9 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/BranchCleanup.maxon` | 423 | 174 → | 9 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspEdits.maxon` | 283 | 62 → | 8 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Arm64/Arm64PanicRuntime.maxon` | 309 | 129 → | 8 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/ElimTrivialBlockArgs.maxon` | 484 | 231 → | 8 | todo | | |
| 2 | `maxon-shv2/Compiler/TestDiscovery.maxon` | 79 | 54 → | 8 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/IrBlock.maxon` | 373 | 195 → | 7 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspCursor.maxon` | 46 | 26 → | 7 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Target/TargetOperands.maxon` | 588 | 272 → | 7 | todo | | |
| 2 | `maxon-shv2/Compiler/Runtime/CascadeNeeds.maxon` | 161 | 82 → | 6 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/IrValueId.maxon` | 163 | 91 → | 6 | todo | | |
| - | `maxon-shv2/Compiler/Runtime/SlabClasses.maxon` | 201 | 36 → | 6 | refused: generated | | |
| 2 | `maxon-shv2/Compiler/TypeNameInterner.maxon` | 242 | 121 → | 6 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/PosixMmap.maxon` | 51 | 41 → | 6 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/StdSpliceWiring.maxon` | 81 | 46 → | 6 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspDocumentProject.maxon` | 83 | 36 → | 6 | todo | | |
| 2 | `maxon-shv2/Compiler/Target.maxon` | 112 | 34 → | 5 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Maxon/SourceRange.maxon` | 142 | 70 → | 5 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/PanicExitCode.maxon` | 35 | 34 → | 4 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/PruneDeadBlockArgs.maxon` | 351 | 148 → | 4 | todo | | |
| 2 | `maxon-shv2/Compiler/Logger.maxon` | 177 | 56 → | 3 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspMessage.maxon` | 206 | 49 → | 3 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspTransport.maxon` | 138 | 32 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/StackAlignment.maxon` | 17 | 16 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/MetricsEmit.maxon` | 170 | 104 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/BacktraceFormat.maxon` | 171 | 100 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/BinaryHelpers.maxon` | 306 | 95 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/Lsp/LspFormatting.maxon` | 70 | 21 → | 2 | todo | | |
| 2 | `maxon-shv2/Compiler/DebugTrace.maxon` | 109 | 51 → | 1 | todo | | |
| 2 | `maxon-shv2/Compiler/ContentHash.maxon` | 20 | 16 → | 1 | todo | | |
| 2 | `maxon-shv2/Compiler/CompileMemory.maxon` | 271 | 76 → | 1 | todo | | |
| 2 | `maxon-shv2/Compiler/Targets/Shared/BlockOffsetTable.maxon` | 19 | 18 → | 0 | todo | | |
| 2 | `maxon-shv2/Compiler/IR/Std/StdModule.maxon` | 3 | 2 → | 0 | todo | | |
| 2 | `maxon-shv2/Compiler/Coverage/CovSiteTable.maxon` | 41 | 20 → | 0 | todo | | |
