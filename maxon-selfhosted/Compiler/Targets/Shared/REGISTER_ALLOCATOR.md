# Register Allocator

The self-hosted Maxon compiler ships an LLVM-Greedy-style register
allocator that lives under `Compiler/Targets/Shared/`. The core is
target-independent; per-target adapters (X64, Arm64) plug in via the
`RegAllocTarget` and `TargetOpQuery` interfaces. WASM bypasses the
allocator entirely — it uses a virtual register stack.

## Pipeline

The per-function pipeline runs in seven phases, orchestrated by
`FunctionRegAllocator.run()` in [RegisterAllocator.maxon](RegisterAllocator.maxon):

```
Phase 0  CFG analysis             (RPO, dominators, loop depth, dom-tree preorder)
Phase 1  Liveness                 (LivenessAnalysis.maxon: dataflow fixed-point)
Phase 2  Live-range construction  (LiveRangeBuilder.maxon: per-vreg intervals)
Phase 3  Phi-merge splitting      (LiveRangeSplitter.maxon: per-anchor sub-ranges)
Phase 4  Coloring                 (SSAColoring.maxon: chordal greedy + eviction + last-chance)
Phase 5  Spill/color loop         (RegisterAllocator.runSpillColorLoop: capped at 10 iterations)
Phase 6  Apply coloring           (ApplyColoring.maxon: virtual -> physical rewrites)
Phase 7  SSA destruction          (RegisterAllocator.destroySSA: parallel-copy materialization)
```

Phases 1-4 rebuild from scratch on every spill-loop iteration. Phase 5
drives the iteration; on each round it tries call-boundary splitting,
rematerialization, and then conventional spilling, in that order, for
any range the colorer couldn't place.

## Pre-allocation: Pressure-Aware Scheduling

`InstructionScheduler.maxon` runs before regalloc as a per-function MIR
pass (wired in [PassPipeline.maxon](../../IR/PassPipeline.maxon)). It's a
bottom-up list scheduler with a pressure-vs-critical-path heuristic.

For each ready op, it computes
`delta = defs_created - uses_going_dead`:

- A pure-use op like `cmp r1, r2` where both r1 and r2 die has delta -2.
- A pure-def like `movRegImm` has delta +1.
- A two-address op like `addRegReg(dest, src)` where src dies has delta 0.

Selection rule:

- **At high pressure** (`currentPressure >= latencyTable.pressureThreshold`):
  pick the op with the most-negative delta (relieves pressure most).
  Tiebreak by critical-path-length descending.
- **At low pressure**: pick by critical-path-length descending (latency hiding).

Per-target thresholds (in `X64Latency.maxon` / `Arm64Latency.maxon`):

| Target | gprCount | pressureThreshold |
|---|---|---|
| x64   | 12 | 9  |
| arm64 | 26 | 22 |

This pre-RA shaping reduces peak live count for many functions, which
keeps the allocator out of high-pressure regions in the first place.

## Live Ranges

`LiveRangeBuilder.maxon` produces a `LiveRange` per virtual register:

```maxon
type LiveRange
    valueId       ValueId       // SSA vreg id
    defBlock      BlockId       // block where the def lives
    defPosition   OpPos         // 1-indexed op position within defBlock
    intervals     IntervalArray // disjoint (block, startPos, endPos) segments
    spillWeight   RegInt        // sum(uses * loopWeight) / totalLength
    regClass      RegClass      // gpr | fp
    fixedReg      RegOrdinal    // != RM_NONE for physical pseudo-ranges
    assignedReg   RegOrdinal    // populated by coloring
    spillSlot     RegInt        // -1 if unspilled
    isRematerializable bool     // true for movRegImm/movRegImm32 defs
    constantValue RegInt        // the integer constant when rematerializable
    hint          RegOrdinal    // preferred physical register
    copyHintValueId ValueId     // copy-link for hint propagation
    copyEdges     CopyEdgeArray // per-anchor copy-boundary suppression
    crossesCall   bool          // live across at least one call op
    stage         RegInt        // call-boundary split stage (RS_NEW..RS_DONE)
    cascade       RegInt        // eviction cascade count (Phase 4 termination)
end
```

Intervals are half-open `[startPos, endPos)`. Position scheme: each op
at `opIdx N` has position `N + 1`; block-arg defs at position 0; the
parallel-copy slot for a block-arg lives at `predBlockLen - 1` of each
predecessor.

The builder also emits **pseudo live ranges** for physical-register
constraints (call clobbers, idiv RAX/RDX, shift CL, calling-convention
parameter registers). These have `fixedReg != RM_NONE` and short
intervals; they participate in the interference graph the same way
virtual ranges do.

`spillWeight` follows the LLVM convention: `SPILL_WEIGHT_INFINITE` is
the unspillable marker for reload vregs (so the spill picker never
re-spills them, guaranteeing termination).

## Phi-Merge Splitting

`LiveRangeSplitter.maxon` runs after live-range construction but before
coloring. A multi-predecessor block-arg ("phi merge") accumulates a
1-position interval at each predecessor's parallel-copy slot. With
3+ predecessors interfering with each other at the merge block, the
chordal greedy allocator can panic.

The splitter rewrites each multi-anchor merge `R_main` into:

- `R_main` keeps its def-block interval only.
- One fresh sub-range per predecessor copy slot, with a 1-position
  interval at `(predId, predBlockLen-1)`. Each is marked
  `SPILL_WEIGHT_INFINITE` and stage `RS_DONE` (further splitting is
  pointless — def and only use coincide). Each is hint-linked to
  `R_main` so the chordal greedy prefers the same color, collapsing
  parallel-copy moves to identities in the no-pressure case.

The source vreg's `CopyEdge` that targeted `R_main` at the predecessor
copy slot is redirected to the sub-range, keeping `isCopyBoundaryOverlap`
suppression correct. The MIR is unchanged (block-args still reference
`R_main`); the sub-ranges exist only in the `ranges` array and the
`SplitMergeInfo` sidetable, consumed by `destroySSA` to dual-emit
`(src → sub → merge)` moves on each edge.

When a sub-range fails to color, `runSpillColorLoop`'s recovery
demotes the parent `R_main` to a memory-only stack slot (LLVM Greedy's
"memory-only phi" pattern).

## Coloring

`SSAColoring.maxon` colors a class (GPR or FP) in three layers:

### Layer 1: Chordal Greedy First-Fit

Pre-existing. Visit ranges in dominance-tree preorder; for each, pick
the first physical register not conflicting with already-colored
neighbors. SSA + chordal interference + dom-tree-preorder is provably
optimal for first-fit when no eviction is needed, so this layer alone
handles the bulk of every function.

The picker honors:

- Hard constraint: `fixedReg` (physical pseudo-ranges).
- Soft constraint: `range.hint` (e.g. calling-convention preferences).
- Copy-link hint: if a copy partner is already colored, prefer its color.
- Tier order: caller-saved vs. callee-saved by `crossesCall`.

### Layer 2: Greedy Eviction with Cascade

When first-fit leaves a range uncolored, `runEvictionFixup` retries via
a priority-queue + eviction picker (LLVM Greedy / regalloc2 style):

1. PQ ordered by `(spillWeight DESC, cascade ASC)`. Highest-weight,
   lowest-cascade ranges pop first.
2. For the popped range R: try the standard picker (Layer 1). If it
   finds a register, commit.
3. If not and `R.cascade < MAX_CASCADE` (8): find the lowest-weight
   colored neighbor whose `(weight, cascade)` is strictly less than
   R's. Evict it (clear its color, bump its cascade, push it back to
   the PQ). Assign its register to R.
4. If no eligible evictee or cascade exhausted: leave R uncolored —
   the spill loop will handle it.

The cascade bound is the only thing preventing eviction cycles. Each
eviction strictly increases the evictee's `(weight, cascade)` lex key;
`MAX_CASCADE = 8` bounds the depth, so total PQ work is
`O(numRanges * MAX_CASCADE)`.

When eviction isn't needed, Layer 2 produces identical coloring to
Layer 1 (verified by guarding the re-color with `anyEvicted`).

### Layer 3: Last-Chance Recoloring

When Layer 2 can't find an evictable victim for a range R (e.g. all
interferers at R's slot are higher-priority infinite-weight reloads),
`tryLastChanceRecolor(R, depth, fixed)` recursively reassigns
interferers to free a slot. Mirrors LLVM Greedy's `tryLastChanceRecoloring`:

For each register r in pool order:

1. Collect interferers currently colored to r. Bail if any is fixed-reg
   or in the `fixed` set (currently pinned by an outer recursive frame).
2. Snapshot interferers' colors, tentatively clear them.
3. Tentatively assign r to R; add R to `fixed`.
4. For each interferer, try the standard picker → eviction →
   recursive `tryLastChanceRecolor(..., depth-1)`. Stop on first
   failure.
5. On success: commit (snapshots discarded), return true.
6. On failure: restore all snapshots, unassign R, remove from `fixed`,
   try the next register.

Depth cap: 3 (matches LLVM). Worst-case work is exponential in fanout
but bounded by depth; most calls return quickly when the standard
picker succeeds for the interferers' replacement slots.

This layer is the formal backstop. In practice every test in the
current spec suite is colored by Layers 1+2; Layer 3 is structural
completeness for the day a pathological case appears.

## Spill Loop

`runSpillColorLoop` in [RegisterAllocator.maxon](RegisterAllocator.maxon)
drives spill/color iteration when the colorer leaves ranges uncolored.

```
for iteration in 0..10:
    let uncolored = findUncoloredRanges(...)
    if uncolored is empty: break

    Pass 1: for each uncolored range, classify into:
       (a) Phi-merge sub-range with parent (SPILL_WEIGHT_INFINITE):
           recover by spilling the parent merge ("memory-only phi").
       (b) Real reload that can't be respilled:
           PANIC with rich diagnostic. Genuinely unrecoverable —
           Phases 1-5 plus last-chance recoloring all failed.
       (c) Rematerializable (constant): route to remat sidetable
           (no slot allocated, no store emitted).
       (d) Normal weight: push to spillDec.spilledRanges.

    Pass 2: insertSpillCode rewrites MIR. For each spilled value:
       - Emit store after each def.
       - For each use, try memory-operand folding first (x64 only).
         If unfoldable, emit a reload before the use.
       - For each rematerializable use, emit movRegImm at the use.

    Rebuild liveness, ranges, coloring. Continue.
```

The iteration cap of 10 is a safety belt — termination is guaranteed
by the `SPILL_WEIGHT_INFINITE + RS_DONE` markers on reload vregs and
the `RS_SPLIT2` and `MAX_CASCADE` stage progressions. In practice
realistic functions converge in 1-3 iterations.

## Spill Code Insertion

`SpillCodeInsertion.maxon` rewrites the MIR for each spilled value.
Three rewrites available per use:

### 1. Memory-Operand Folding (x64 only)

When the using op has a `*Mem` variant in the dialect, rewrite the op
to read directly from the spill slot. No reload vreg is created.

The target's `tryFoldReadFromSlot(module, packed, valueId, stackOffset)`
returns the rewritten op's index, or -1 if not foldable. The X64 impl
handles:

| Original | Folded form |
|---|---|
| `addRegReg(dest, src_spilled)` | `addRegMem(dest, rbp, stackOffset)` |
| `subRegReg(dest, src_spilled)` | `subRegMem(dest, rbp, stackOffset)` |
| `andRegReg(dest, src_spilled)` | `andRegMem(dest, rbp, stackOffset)` |
| `orRegReg`, `xorRegReg`, `imulRegReg` | similar |
| `cmpRegReg(lhs, rhs_spilled)` | `cmpRegMem(lhs, rbp, stackOffset)` |
| `movRegReg(dest, src_spilled)` | `reloadFromStack(dest, stackOffset)` |

The X64 dialect adds these `*Mem` variants (`OpPattern.baseUseUseDest`)
and the corresponding immediate-form ALU (`andRegImm`, `orRegImm`,
`xorRegImm`, `imulRegImm`) to support folding. Folding refuses when
source == dest (alias) or when the spilled operand is the dest of a
two-address op (memory-dest folding is a separate dialect that hasn't
been needed).

ARM64 is a load-store architecture; `tryFoldReadFromSlot` returns -1.
The capability flag `TargetRegAlloc.hasMemOperandAlu` lets the spill
loop short-circuit the probe.

### 2. Rematerialization

When `range.isRematerializable` (set during live-range construction for
`movRegImm` / `movRegImm32` defs), emit `movRegImm freshVreg, constantValue`
at each use site — no stack slot, no store, no reload from memory.
The constant is re-computed every use.

The minted remat vregs propagate the remat property (via a sidetable)
so chained rematerializations across spill iterations remain free.

### 3. Conventional Reload

The fallback. Emit `reloadFromStack freshVreg, slotOffset` before the
use, remap the using op to read `freshVreg`. The fresh reload vreg is
marked `SPILL_WEIGHT_INFINITE` + `RS_DONE` on the next liveness rebuild
so the spill loop can't re-spill it.

### Branch-Edge Reload Deduplication

When a predecessor block has multiple successor edges that pass the same
spilled value, the per-block `reloadCache` shares one reload vreg
across all edges instead of emitting one per edge. This was the
load-bearing optimization for sha256's compression-round body: 8 spilled
values × 2 successor edges (for the conditional loop exit) = 16
simultaneous reload vregs without dedup, vs 8 with.

## Call-Boundary Splitting

`CallBoundarySplitter.maxon` implements LLVM Greedy's `tryLocalSplit`
equivalent: when a range L lives across a call instruction at position
P, break L into a pre-call piece (still named L) and a post-call piece
(a fresh vreg `L_post`). The boundary is a `movRegReg L_post, L` op
inserted just before the call. Each piece is colored independently —
the colorer can put them in different physical registers, freeing the
pre-call piece to use caller-saved registers.

Termination uses LLVM's `RS_Split2` progress rule, encoded via
`LiveRange.stage`:

- `RS_NEW` → `RS_SPLIT1` → `RS_SPLIT2` → no further splitting.
- A range that has been split twice (`RS_SPLIT2`) falls through to the
  normal spill path.

The splitter's `trySplitAtCall(range, ...)` is currently dormant in
`runSpillColorLoop`. The infrastructure is sound and in place but the
splitter has known SSA-soundness bugs (redef rewrite leaves stale uses
+ double-defines L_post) that surface in some IR shapes. Re-enabling
it requires reconciling the SSA rebuild path with the splitter's
rewrite invariants.

## Stage and Cascade Sidetables

`LiveRange` is rebuilt from scratch on every spill iteration, so any
per-range allocator state would be lost across rebuilds. Two sidetables
in `FunctionRegAllocator` solve this:

- `stageByValueId : RegIntArray` — the call-boundary split stage.
- `cascadeByValueId : RegIntArray` — the eviction cascade count.
- `reloadIds : BoolArray` — cumulative bitmap of every reload vreg
  minted across iterations. Read by `buildLiveRanges` to mark each
  reload range `SPILL_WEIGHT_INFINITE` + `stage = RS_DONE`.

After every `buildLiveRanges`, each newly-built `LiveRange` has its
`stage`, `cascade`, and remat-flag fields populated from these
sidetables. Writes from the splitter/evictor/spill-code-insertion go
to the sidetables, not the throwaway `LiveRange` objects.

## Apply Coloring

`ApplyColoring.maxon` walks each block and calls
`regTarget.substituteOp(module, packed, coloring, opRefs)` to convert
each virtual operand to its assigned physical register. Identity moves
(virtual1 → physical1, virtual2 → physical1 where both got the same
color) are elided by the per-target `substituteOp` implementation.

A parallel buffered walk recognises call-arg setup runs (consecutive
`mov physArg, virtual(srcId)` / `reloadFromStack(physArg, off)` /
`store [sp+off], src` ops preceding a `callDirect` or `callIndirect`)
via `regTarget.classifyCallArgSetup`. The buffer is handed to
`sequentializeCallArgSetup` (CopyResolution.maxon), which emits stack
writes first, then runs the standard topological-order +
cycle-break sequencer on the GPR / FP reg-to-reg moves, interleaving
spill reloads as their destinations become unblocked. Without this,
chains like "arg0 lives in rcx, arg1's destination is rcx" miscompile
to a read-after-clobber.

## SSA Destruction

`destroySSA` in `RegisterAllocator.maxon` runs after coloring. It
materializes parallel-copy moves at CFG edges for block-arg passing:

1. For each predecessor → successor edge, compute the parallel copy
   that must execute between the predecessor's last op and the
   successor's first op.
2. Resolve copy cycles via the cycle-break register
   (`desc.gprCycleBreakOrd` = r11 on x64).
3. For conditional branches, the else-edge's copies live in a
   trampoline block to keep the cond-jump terminator pure.
4. The phi-merge splitter's `SplitMergeInfo` sidetable is consulted to
   dual-emit `(src → sub_range → merge)` moves where a multi-anchor
   merge was split.

## Target Adapters

`RegAllocTarget` and `TargetOpQuery` are the interfaces the
target-independent allocator calls into:

```
RegAllocTarget:
    emitSpill / emitReload / emitSpillStore / emitReloadOp
    emitMovImm / emitMovReg / emitVirtualMov / emitRematConst
    emitSlotToSlotMove
    countOpReads / noteOpDefs                  -- liveness inputs
    remapOp                                    -- substitute fresh value-IDs
    substituteOp                               -- final virtual->physical rewrite
    tryFoldReadFromSlot                        -- Phase 3 fold probe
    getImplicitGprDefs / getImplicitGprReads   -- call clobbers, idiv RAX/RDX
    getCallConvGprOrd                          -- calling convention
    classifyCallArgSetup                       -- call-arg parallel-copy sequencing
    isCallOp / isReturnTerminator / ...        -- op classification

TargetOpQuery:
    isCallOp / isCondJumpTerminator / isReturnTerminator
    getOpPatternOrdinal                        -- OpPattern dispatch
    decodeReturnMovProbe                       -- copy-link probe for return mov
    extractCopyHints                           -- copy-edge population
    terminatorIsConditionalBranch
    ...
```

Each target (`X64RegisterAlloc.maxon`, `Arm64RegisterAlloc.maxon`)
implements both interfaces. The allocator monomorphizes calls per
target — no runtime dispatch.

The WASM backend bypasses the allocator entirely:
`allocateRegisters` matches on `target.cpu` and breaks out for
`wasm32`.

## Determinism and Termination

The allocator is fully deterministic (same input → same output) and
provably terminating:

- Iteration cap of 10 on `runSpillColorLoop` — safety belt only;
  realistic functions converge in 1-3 iterations.
- `SPILL_WEIGHT_INFINITE` on reload vregs prevents re-spilling.
- `RS_SPLIT2` caps call-boundary splits per range at 2.
- `MAX_CASCADE = 8` caps eviction depth.
- Last-chance recoloring depth cap of 3.

If all backstops fail, `panicUnresolvableReload` emits a structured
diagnostic naming the failing vreg, function, block, position, and
the full live set with each interferer's spill weight. This signals
a true structural impossibility (e.g. more concurrent fixed-reg
constraints than the pool size) rather than an allocator bug.

## File Map

```
Targets/Shared/
    RegisterAllocator.maxon       Top-level orchestrator, FunctionRegAllocator, spill loop (1352 lines)
    SSAColoring.maxon             Chordal greedy + eviction + last-chance recoloring (1931 lines)
    SpillCodeInsertion.maxon      MIR rewrites for spilled values (839 lines)
    LiveRangeBuilder.maxon        Per-vreg interval construction (1064 lines)
    LiveRangeSplitter.maxon       Phi-merge multi-anchor splitting (416 lines)
    LivenessAnalysis.maxon        Dataflow fixed-point liveness (417 lines)
    CallBoundarySplitter.maxon    LLVM tryLocalSplit equivalent — dormant (337 lines)
    InstructionScheduler.maxon    Bottom-up list scheduler, pressure-aware (1137 lines)
    SpillManager.maxon            Pressure analysis, slot allocation (533 lines)
    SlotLivenessRenumber.maxon    Spill-slot recycling via interval renumbering (587 lines)
    CopyResolution.maxon          Parallel-copy resolution for destroySSA (295 lines)
    ApplyColoring.maxon           Virtual -> physical rewrites (43 lines)
    PrologueEpiloguePass.maxon    Frame setup / teardown (140 lines)
    RegAllocTarget.maxon          Target-emission interface (150 lines)
    TargetRegAlloc.maxon          Per-target descriptor (84 lines)
    TargetOpQuery.maxon           Target-op classification interface (41 lines)
    TargetCfg.maxon               CFG-helper interface (56 lines)
    StdOpHelpers.maxon            Std-op classification utilities (478 lines)
    CodeResult.maxon              Code-emission result type (274 lines)
    OsDescriptor.maxon            OS-level constants (111 lines)
    BinaryHelpers.maxon           Byte-level helpers (148 lines)

Targets/X64/
    X64RegisterAlloc.maxon        X64 RegAllocTarget impl (2196+ lines)
    X64Dialect.maxon              X64Op enum + metadata
    X64Backend.maxon              X64 encoder
    X64Latency.maxon              X64 latency table + gprCount/pressureThreshold
    X64OpQuery.maxon              X64 TargetOpQuery impl
    X64SlotProjection.maxon       X64 stack-slot rewrite
    X64PrologueEpilogue.maxon     X64 prologue/epilogue emission
    MirToX64Conversion.maxon      MIR -> X64 lowering

Targets/Arm64/
    (mirror of X64)
```

## Lineage

The allocator is the result of a focused build-out modeled on LLVM
Greedy and Cranelift regalloc2. The high-level techniques in order of
implementation:

1. Rematerialization (constants re-emitted at use, never enter the
   live set).
2. X64 + Arm64 dialect additions (memory-operand ALU, immediate-form
   ALU).
3. Memory-operand folding in the spiller (read-side, via
   `tryFoldReadFromSlot`).
4. Greedy eviction with cascade (in `SSAColoring.runEvictionFixup`).
5. Branch-edge reload deduplication (per-block reload cache in
   `SpillCodeInsertion.remapBranchEdgeArgs`).
6. Pressure-aware scheduler tightening (pressure-delta heuristic in
   `InstructionScheduler.selectBestReady`).
7. Last-chance recoloring (recursive recolor in
   `SSAColoring.tryLastChanceRecolor`).

The motivating testcase was `stdlib.sha256` — a compression-round body
with ~25 concurrent i64 values across 6 helper calls — which originally
panicked with "reload vreg cannot be respilled" at iteration 1. With
the full toolkit in place sha256 compiles cleanly and all 11 spec tests
pass.

## References

- Olesen, *Greedy Register Allocation in LLVM 3.0*,
  <https://blog.llvm.org/2011/09/greedy-register-allocation-in-llvm-30.html>
- LLVM `RegAllocGreedy.cpp`, `InlineSpiller.cpp`, `SplitKit.cpp`
- Cranelift regalloc2 (`bytecodealliance/regalloc2`):
  `doc/GENERAL.md`, `doc/ION.md`
