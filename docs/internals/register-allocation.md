---
title: Register Allocation
description: How the x64 and arm64 back ends assign SSA values to machine registers and stack slots.
sidebar:
  order: 1
---

The x64 and arm64 back ends assign every virtual register to a machine register or a stack slot with an
SSA-based allocator. There are no live intervals and no explicit interference graph. The wasm32 back end
allocates no registers: every SSA value is a wasm local (`maxon-bin/Compiler/Targets/Wasm/StdToWasm.maxon`).
All files named below are under `maxon-bin/Compiler/Targets/Shared/`.

## Pipeline

`BackendPool.lowerAndAllocateOnAWorkerPool` (`BackendPool.maxon`) computes the target's register facts once
per module and hands each function to a pool worker (`MAXON_MAX_PROCS=1` gives a pool of one, not a separate
serial path). The worker selects instructions, splits critical edges so every phi copy has an edge block to
land in, and runs `allocateFunctionCore` (`RegisterAllocator.maxon`):

1. lay the blocks out in reverse postorder, so layout order is a dominance order;
2. [liveness](#liveness);
3. [splitting](#splitting), which either relieves every over-pool point or refuses with E5001;
4. [colouring](#colouring);
5. fix the frame size — after colouring, because a colouring repair can take more spill slots;
6. [SSA destruction and rewrite](#ssa-destruction-and-rewrite).

These are also the `RegAllocPhase` rows compile timings are reported under. Back on the main thread, the back
end cleans up branches and inserts the prologue and epilogue.

## The contract

**E5001 is reported only when the program genuinely does not fit the machine.** A loop whose own working
set needs more registers than the target has is refused with
[E5001](../../maxon-bin/Compiler/ErrorCodeRegistry.maxon), naming the values in the way. That asks the
author to restructure the loop, so a *false* E5001, on a program that fits, is the worst defect the allocator
can have: the author is told to change code that is not wrong, and cannot converge. Everything else —
pressure in straight-line code, values idle across a loop, clobbers at calls — is relieved silently with
spill code. `PruneDeadBlockArgs` and `ElimTrivialBlockArgs` serve the contract from the Std tier: they delete
phis that nothing reads or that merely rename one value, which would otherwise count as live across a loop.

E5001 is always a full-pool overflow, measured against the full pool of the peak's own register file (on
x64, 14 general-purpose or 16 float registers, never the two together). `PressurePeak.witness` carries that
pool, so the message cannot read a reduced one off the peak op and ask the author to remove values the
program does not need to lose.

The message ranks the values to remove by how often the loop reads them, and that count excludes
branch-edge arguments. A loop-carried `var` the loop only assigns therefore reads 0 times yet is a genuine
part of the working set: a 0 means "look at the IR", not that a pass produced a surplus value.

## The rules

**Rule 1 — SSA.** Every value has exactly one def, which dominates its uses. Live ranges are therefore
dominance-closed and the interference graph is chordal, so greedy colouring in a dominance order uses exactly
`maxPressure` registers when nothing is forbidden. The colourer asserts the consequence at every op: each
value it uses is already coloured. Splitting preserves the rule, because a reload defines a fresh value.

**Biased colouring is correctness, not optimization.** Copy-related values hold the same value: a phi and
its incoming values, and a two-address (reuse) def and its input. Colouring them together keeps a loop's
back-edge copy out of the loop and stops the allocator counting one value as two, which would otherwise
produce a false E5001.

**Rule 2 — spill placement.** Spill code for a value a loop uses never lands on that loop's hot path. A
**cold** placement puts every store and reload at loop depth 0 or in a cold block (`blockIsOffTheHotPath`),
and `assertPlacementLegal` panics if one lands elsewhere, because a hot spill should have been E5001. It
relieves a full-pool overflow cheaply: the store runs once before the loop, the reload after it or inside the
cold arm that reads the value.

A **forced** placement may land at any depth, and is never E5001. It is used where memory is the only home
left:

- at a [confined](#effective-pools) point, where the value cannot survive in a register across the op. The
  load and store are cheap against the call they bracket, and refusing would only make the author write the
  same memory traffic by hand;
- in the colourer's eviction repair, where the alternative is a panic on a program that fits;
- at a full-pool peak in a function the compiler emitted, where there is no author to refuse.

**Rule 3 — no compiler-introduced value blocks an allocation.** Every value in an E5001 blocking set must
resolve to the author's source, because the author cannot delete a value they never wrote.
`RegisterPressureDiagnostic.defRangeOf` resolves a parameter to its declaration, an op-defined value to its
op, a phi to its earliest incoming value, and a reload, rematerialized constant or Std-pass stand-in to the
value it derives from. A value with no user ancestry is a compiler defect, and the diagnostic panics naming
it. A generic's dictionary parameter, for instance, is relieved by a forced bracket around the call instead.

## Registers

`assertRegisterFacts` checks, once per compile, that the masks below agree with each other, with every call
op's clobber set (`implicitDefs`), and with the fixed scratch registers the encoders use.

| | x64 | arm64 |
| --- | --- | --- |
| Allocatable | 14 GPRs (all but `rsp`, `rbp`), `xmm0`–`xmm15` | `x0`–`x14`, `x19`–`x28`, `d0`–`d31` |
| Caller-saved | `rax`, `rcx`, `rdx`, `rsi`, `rdi`, `r8`–`r11`, `xmm0`–`xmm5` | `x0`–`x14`, `d0`–`d7`, `d16`–`d31` |
| Callee-saved | `rbx`, `r12`–`r15`, `xmm6`–`xmm15` | `x19`–`x28`, `d8`–`d15` |
| Never allocated | `rsp`, `rbp` | `x15` (preemption resume), `x16`/`x17` (scratch), `x18` (platform), `x29`, `x30`, `sp` |
| Integer arguments | `rcx`, `rdx`, `rax`, `r9`, `rsi`, `rdi` | `x0`–`x7` |
| Float arguments | `xmm0`–`xmm5` | `d0`–`d7` |
| Result, error flag | `r8` or `xmm0`; `r10` | `x0` or `d0`; `x9` |

The x64 convention between Maxon functions is the compiler's own and is the same on Windows and Linux; only
calls into the host use the Win64 order. General-purpose and floating-point registers share one register
file (`RegBits.maxon`); a value's class masks the pool, so a value is only ever coloured within its class.

## Effective pools

`maxPressure ≤ pool` is necessary but not sufficient. A value's *effective pool* is its class pool minus its
`forbidden` mask: a value live across a call may use only callee-saved registers, one live across an x64
`idiv` may not use `rax` or `rdx`, and one live across an argument move may not use that argument's register.
Two values at one point can then need registers from different, differently sized sets, so counting them
against one number cannot decide whether they fit. `HallCondition.hallVerdictAt` (`HallCondition.maxon`)
decides it exactly, as a bipartite matching of values to registers. A point that fits by count but fails
that matching is *confined*; the splitter relieves it with a forced placement.

Even where every point fits, a greedy colouring need not find a solution: forbidden masks make this list
colouring, which is NP-hard. [Colouring](#colouring) repairs the values it cannot place.

## Liveness

`TargetLiveness.maxon` computes per-block live-in and live-out sets with no dataflow fixpoint: for each SSA
value it walks backward from every use to the def (`solveLivenessSsa`). Because the walk is per value, a split
re-marks only the victim and the values it created, over the blocks the split touched.

Alongside the live sets it records a `UseIndex` (each value's def and using ops, with edge uses to phis
recorded at the branch), per-op and peak pressure per class, each value's `forbidden` mask (the clobbers of
every op it is live across), and the call runs inside cold blocks. Ops are ordered by `OpSeq`, a
block-relative sequence number, so inserting an op renumbers only its block.

## Splitting

`SplitDriver.relievePressure` (`SplitLiveRanges.maxon`) repeats until no point exceeds its pool: find the
worst point, the *peak*; split one value live across it; repair the analysis over the region the split
touched. A `PeakTree` over blocks and a `PressureIndex` segment tree over ops keep the peak current.

**The peak.** A full-pool overflow ranks above a confined one, then higher pressure wins, then a
fixed-register op. Confined points are searched only once no full-pool overflow is left.

**The victim.** Candidates are the values of the overflowing class live across the peak that the peak op does
not read. The tiers are tried in order; within a tier the value whose next use is farthest away wins
(Belady), ties going to the lowest value id:

1. a constant dead across the peak: rematerialize it after the peak;
2. a value not yet spilled, dead across the peak, that can be spilled cold: split it after the peak;
3. under a forced placement, a constant: rematerialize it at every use;
4. a value already in a slot, or, when forced, one whose store fits the pool at its def;
5. any spillable value not yet spilled that has a store anchor.

With no victim at a full-pool peak, the driver retries the same op as a confined overflow, then with a forced
placement if the function has no author or the peak reaches a cold run. Failing that, it throws
`RegAllocRefusal.hotOverflow`, which becomes E5001.

**The split.** The store goes right after the def, or at the top of the block for a phi: the def is the only
point guaranteed to dominate every reload, and it runs once rather than once per iteration. Each run of uses
after the peak gets one reload, a fresh value, placed before the run's first use; uses before the peak keep
the original value, which is therefore dead across the peak. Every reload dominates the uses it serves, so no
phi is ever inserted. A run of uses is cut at block boundaries, at the peak, and wherever an op between two
uses leaves the class no register at all (on x64, a float value across a call).

**Rematerialization.** An integer constant (`movRegImm`, `movRegImm32` on x64; `arm64MovImm` on arm64) is
never spilled: it is re-emitted before each use, and its original def is removed once no use is left.

The driver panics if the loop runs past `splitRunawayBound`, and on exit asserts that no class exceeds its
pool.

## Colouring

`colorFunction` (`RegisterAllocator.maxon`) sweeps the blocks in layout order. At each block it seeds the
in-use registers from the live-ins, colours the phis, works out where each value dies, and colours each op's
defs as it passes; a register is free again at the op where its value dies.

`chooseRegister` takes the first of:

1. the value's fixed-register hint, such as the result register a return moves it into;
2. the register of its already-coloured copy partner;
3. the fixed register of a copy partner not yet coloured;
4. the lowest free register, preferring caller-saved, first avoiding registers the partner is forbidden.

A hint is taken only if it is free and either in the preferred set — the class's caller-saved registers while
any is free, otherwise the whole class — or being vacated at this op. Hints come from phi edges, reuse pairs
and moves into fixed registers (`computeHints`).

**Two-address ops.** On x64, `sub`, `imul` and `neg` overwrite their first input. The colourer gives the def
its input's register and records a copy (`ReuseCopyPlan`) only if the input is still live after the op.

**Cold runs.** Inside a cold block a call does not confine the values live across it; the colourer saves the
registers it holds before the run and reloads them after (`ColdCallBracketPlan`).

**Repair.** When no register is left for a value, `reportExhaustion` runs Hall's condition at that point. An
overflow verdict means the splitter left a point over its pool, and panics. A feasible verdict records the
value, and `repairStuckValues` splits it across the clobber that confines it or, failing that, evicts an
occupant of a register it may use. All stuck values of a pass are repaired together and the function is
coloured again, up to `splitRunawayBound` rounds.

## SSA destruction and rewrite

`buildSsaDestructionPlan` (`SsaDestruction.maxon`) turns each edge's phi arguments into a parallel copy in the
edge's block, sequenced so no copy overwrites a source another still reads. `applyAllocation` splices in
those copies, the two-address copies and the cold-run brackets, replaces every virtual operand with its
physical register, and drops moves whose source and destination are the same register.

Spill slots are 8 bytes. Above them the frame holds a save slot for each callee-saved register the colouring
used (`ColouredRegisters.maxon`), which each target's prologue pass saves and restores.

## Scaling

Every structure is sized per function and reused: scratch buffers are allocated once and reused across calls,
candidates are read off one live row, the peak is maintained in trees, and liveness is repaired over the
region a split touched. Two costs remain per split and proportional to that region: the backward pressure
sweep that rebuilds `forbidden` masks, and the confined (Hall) sweep. They are ordered, not redundant — the
second reads effective pools the first finishes writing — and cannot be fused. In a single function that
keeps growing the region grows with it, so `regalloc:splitting` is the phase to expect to bend on a scale
ladder built that way.

## Verification

There is no allocation verifier pass. A wrong allocation computes a wrong answer, which running the spec
suite catches; a worse one — an extra spill, a lost coalesce — shows in the Target IR, which the
`TargetIr:<lane>` pins in `ir-specs/` hold block by block for the cases that carry them. The allocator asserts its invariants as it runs, and
two compile-time constants, off in shipping builds, add exhaustive self-checks: `VerifyIncrementalSplit`
compares every incremental repair with a full rebuild, and `VerifyOpVariantFacts` checks the per-op fact
cache.

To watch it work: `--emit-ir` (or the MCP `dump_ir` tool) shows the target IR after allocation;
`--log=codegen:debug` prints copies, hints taken and missed, values split, reloads, rematerializations, forced
spills and repairs; `--metrics=<path>` writes a `regalloc` row per phase; and the `regalloc.functionAllocated`
trace event carries each function's peak pressure. The specs named `register-*` and `regalloc*`, with
`cold-call-spilling`, `float-register-pressure`, `wide-spill-frames` and `two-address-regression`, cover it.
