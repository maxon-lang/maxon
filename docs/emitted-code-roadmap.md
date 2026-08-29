# The emitted-code roadmap — what shv2's codegen does not do yet

**Opened 2026-08-28.** Ranked plan for the **quality of the machine code shv2 emits** — not for the
compiler's own speed, which is `docs/optimization-log.md`'s axis and a different instrument.

## Why this document exists at all

`maxon-shv2/PLAN.md`'s **`EC` rows are the claim registry** and stay that way: a rung is taken by
editing a row there and pushing. What the board has never held is the **ranking and the readings
behind it**. `EC5`'s row cites *"the plan: coordinator's `temp/ec5-plan.md`"* and *"R6 of the
emitted-code plan"* — both are gone, `temp/` is scratch, and the R1…Rn list they refer to exists
nowhere in the tree. So the ranking below was re-derived from scratch to write this file. **That is
the failure this document fixes:** the measurements live here, committed; a row is filed onto the
board when it is taken, and cites this file rather than restating it.

## The baseline, and why the motivation is not what `EC1` said

⚠ **READ `W222` FIRST.** Measured 2026-08-27, `scripts/self-host-ab.sh` run twice on one box:
**shv2-emitted code runs ~35% FASTER than bootstrap-emitted code** (CPU ×0.60–0.68 across the
ladder) **while allocating 69% more** (allocs ×1.57 at rung 0 rising to ×1.69 at rung 5). `EC1`'s
original ×1.78 CPU deficit is dead.

⇒ **Nothing below may be justified by "the bootstrap is faster."** It is not. The live deficits are
the **allocation column** — which still grows with program size, the signature of a per-element cost
— and the **absolute instruction quality** measured directly below, which is a deficit against good
codegen rather than against either reference compiler.

## ⚠⚠ THE COUNTER'S UNIT CHANGED ON 2026-08-29 — NUMBERS DO NOT COMPARE ACROSS IT

Upstream's *"a fragment renders the PROGRAM, and the LIBRARY is not the program"* (X11) made
`--emit-ir` write only the **user's** functions. Before it, the stdlib and runtime bodies came too.

**Same tree, same compiler, nbody reads 8,587 ops before and 837 after.** That is the instrument's
unit changing, not codegen improving. ⇒ **Every `emitted-code-count.py` figure in this file dated
before 2026-08-29, and every one quoted in the `EC11`–`EC18` commit messages, is on the OLD unit.**
They remain valid *against each other*; they are not comparable to anything measured after.

Post-change baseline at `3231f15162`, all eight rows in:

```
program                ops  jmp  jmp->next  jmponly  imul-imm  imul-pow2  idiv  call-direct  mgd-call  im-blocks  mov
nbody.maxon            837   20          0        0         0          0     0           66        14         28  120
fannkuch-redux.maxon  1698   64          0        0         1          1     2          110        48        180  212
arr.maxon               86    1          0        0         0          0     0           13         5          1   19
cse2.maxon              60    0          0        0         0          0     0            8         0          0   14
cse3.maxon              22    1          0        0         1          0     0            0         0          0    2
probe.maxon             57    0          0        0         1          0     0            4         0          0    8
leaf.maxon              56    1          0        0         0          0     0            5         4          1    9
fmt.maxon               73    1          0        0         0          0     0            5         0          0   10
TOTAL                 2889   88          0        0         3          1     2          211        71        210  394
```

⚠ **AND THE CORPUS CAN NO LONGER SEE A ROW WHOSE SUBJECT IS IN THE STDLIB.** Library bodies are
opt-in by name (`--emit-ir-runtime=<a>,<b>`), so `EC2` — refcount traffic inside container methods —
must be sized on the **self-compile**, not here. This is the fourth time in this workstream that a
corpus could not express a row's shape; the other three are recorded in the END-TO-END section.

⭐ **THE TIMED A/B RESULTS BELOW ARE UNAFFECTED.** They measure wall time on compiled programs and
never went through `--emit-ir`.

## ⭐⭐ END-TO-END: WHAT THE WORKSTREAM ACTUALLY BOUGHT — FINAL, MEASURED 2026-08-29

All eight rows in (`EC11` `EC12` `EC13` `EC16` `EC15` `EC14` `EC17` `EC19` `EC18`), A/B'd against a
compiler built from `01eaf6ea4e` — the commit before the first row. **Both compilers built in one
session, runs interleaved, same box, three runs each. All three programs print byte-identical
output.**

| program | pre (ms) | post (ms) | time | code size |
|---|---|---|---|---|
| `fannkuch-redux` — array indexing | 19,209 / 18,678 / 18,678 | **10,267 / 9,835 / 9,841** | **−47.3% (×1.90)** | 37,073 → 28,881 B (**−22.1%**) |
| `fmt` — float formatting | 4,875 / 4,747 / 4,769 | **3,286 / 3,188 / 3,187** | **−33.2% (×1.50)** | 81,077 → 71,793 B (**−11.5%**) |
| `nbody` — float arithmetic | 11,271 / 10,799 / 10,750 | **10,602 / 10,144 / 10,168** | **−5.8%** | 87,402 → 76,582 B (**−12.4%**) |

Every `post` run beats every `pre` run on all three; fannkuch's and fmt's arms are separated by more
than 4× their own spread.

**The compiler compiling itself**, which is the one program here that is not a benchmark:
stage-1 (bootstrap-emitted) self-compiles in **162 s**, stage-2 (shv2-emitted) in **77 s** —
**×0.475**, where `W222` read that ratio at **×0.62** on 2026-08-27 before any of these rows. Same
logic in both binaries, so the whole difference is codegen. The fixpoint holds byte-identically
(9,033,004 B) and **the whole suite passes under stage-2** (6,834 / 0).

⚠⚠ **THE SPREAD ACROSS PROGRAMS IS THE POINT, AND IT IS WHY AN INSTRUCTION COUNT IS ONLY A
HYPOTHESIS.** The same compiler change is ×1.90 on one program and −5.8% on another. Each row's win
lives in a *shape*: `EC16`+`EC15` deleted a runtime branch and a multiply from every array access,
which fannkuch pays on every element and nbody barely touches; `EC18` deleted four `idiv` per
formatted number, which only `fmt` pays; `EC14` deleted two L1 loads from a loop an out-of-order core
was already absorbing them in, and measured **zero**. ⇒ **Attribute the shape before predicting the
win, and settle it with a timed A/B on a program that exercises it.**

⚠ And the corpus must be able to SEE the shape. Three times in this workstream it could not:
`scale-test` read a delta of exactly **0** for `EC14` because `ScaleCorpus` has no loop over an
array; `cse2.maxon` stopped being a CSE probe when `EC12` folded it away; and an int-formatting
probe read `idiv 0` because that path is hand-emitted runtime. Each was fixed by building a probe
that carries the shape — `cse3.maxon`, `fmt.maxon` — not by trusting the zero.

## The three-way inventory — the answer to "what do the other two have that shv2 lacks?"

Pipelines compared: `maxon-shv2/Compiler/IR/PassPipeline.maxon:398-413` ·
`maxon-sharp/Compiler/3-MlirPipeline.cs:62-173` · `maxon-selfhosted/Compiler/IR/PassPipeline.maxon:778-898`.

| Optimization | shv2 | bootstrap | v1 self-hosted |
|---|---|---|---|
| Whole-program dead-function elim | ✅ `DeadFunctionElimination.maxon` | ✅ `DeadFunctionElimination.cs` | ✅ ×2 (Maxon + Std tier) |
| Inlining | ◑ `InlineLeaves` — leaf-only, one round, ≤24 ops, scheduled AFTER the managed rewrite (EC17) | ❌ none | ✅ `Passes/Inliner.maxon` (3,067 lines) |
| Managed-primitive inlining | ✅ `InlineManagedPrimitives` (EC1) | ❌ n/a | ❌ n/a |
| Const **operand** → immediate | ✅ `FoldConstOperands` | ◑ imm8/imm32 encodings only | ✅ `Canonicalize` |
| Algebraic identity (`x+0`, `x*1`) | ✅ `FoldConstOperands` move 3 | ✅ `TryAlgebraicIdentity` | ✅ `Canonicalize` |
| **Constant folding (`const ⊕ const`)** | ✅ `FoldConstants` (EC12) | ❌ | ✅ `Canonicalize` |
| **CSE / GVN** | ✅ `CommonSubexpressionElimination` (EC13) — dominator-scoped, arith band, call-barriered | ❌ | ✅ `CommonSubexpressionElimination.maxon` (494) |
| **LICM** | ✅ `LoopInvariantCodeMotion` (EC14) — pure ops AND invariant loads, two stated speculation rules | ◑ refcount pairs only | ✅ `LoopInvariantCodeMotion.maxon` (596) — pure ops only |
| **General DCE (dead pure values)** | ❌ *(2 op kinds only, by design)* | ✅ `DeadStoreEliminationPass` sub-pass 3 | ✅ `DeadCodeElimination.maxon` (728) |
| **Block merging / branch simplification** | ◑ `X64BranchCleanup` (EC11) — elision, inversion, threading, unreachable; **no reordering** | ◑ cond-br then-edge only | ✅ `CfgAnalysis` + `Canonicalize` |
| **Strength reduction** (magic div, shift div) | ✅ `StrengthReduceDivision` (EC18) — x64 only; the `mul`→`shl` half is moot since EC16 | ❌ | ❌ |
| **Scaled-index addressing** (`[base+idx*8]`) | ✅ `loadRegBaseIndexScale` etc. (EC16) — x64 full, arm64 the `ADD` half | ❌ | ❌ |
| **Static specialization of the inlined managed guards** | ✅ `Project.stdOpElementStrides` + `strideDispatchPlanForStamp` (EC15) | ❌ n/a | ❌ n/a |
| Store-forwarding / dead-store elim | ❌ | ✅ `StoreForwardingPass` + `DeadStoreEliminationPass` | ✅ via `Mem2Reg` |
| mem2reg / SROA | **n/a — see below** | ❌ | ✅ `Mem2Reg.maxon` (2,331) |
| Escape analysis → stack | ✅ `PromoteStackRecords` | ✅ `StackPromotionAnalysisPass` | ◑ |
| Monomorphization | ⛔ **ruled against** (user, EC1) | ✅ `MonomorphizationPass` | ✅ |
| Refcount pair elimination | ⛔ different model — static ownership | ✅ `RefcountOptimizationPass` (2,143) | ✅ `InsertRefcounts` (7,755) |
| Register allocator | ✅ linear SSA-chordal + Hall condition | ◑ on-the-fly LRU, 8 caller-saved regs | ✅ SSA graph colouring (3,479) |
| **Coalescing-driven operand commute** | ❌ | ❌ | ✅ `Mir/CommuteForCoalescing.maxon` (355) |
| **Instruction scheduling** | ❌ | ❌ | ✅ `InstructionScheduler.maxon` (847) — pressure-aware list scheduler |
| Jump-table dispatch | ◑ dense arms only | ✅ linear / table / **binary search** over intervals | ✅ |
| Short-jump (rel8) relaxation | ❌ always rel32 | ❌ always rel32 | — |
| Target-tier peephole | ❌ **no peephole pass at all** | ◑ `PeepholePass.cs` — 2 patterns, x64 only | ◑ |

**The headline:** on the classical-optimizer axis, the bootstrap is *not* the reference — it has no
constant folding, no CSE, no inlining, no LICM either, and shv2 already beats it. **v1 is the
reference**, and shv2 has now taken THREE of its six classical passes (branch simplification `EC11`,
constant folding `EC12`, CSE `EC13`) — each written against shv2's own IR rather than ported, and each
citing what it took from v1's version and what it deliberately did not.

⭐ **`EC16` is the row NEITHER reference could have been read for**: no compiler in this tree emits a
scaled-index address, so the addressing-mode row is the first where shv2 is ahead of both.

### Three things NOT to port, each with its reason

- **`Mem2Reg`** — v1 needed it because v1's lowering created memref slots for locals. **shv2's Std
  tier has no such slot**: its only memory ops are `loadIndirect`/`storeIndirect` on real addresses
  (`StdDialect.maxon:906,910`), and locals are parser-minted `ValueId`s that are already SSA. The
  pass would have nothing to promote. *(Confirm before building anything that assumes otherwise.)*
  ⇒ The bootstrap's `StoreForwardingPass`/`DeadStoreEliminationPass` are likewise mostly moot for
  **locals** — but **not** for the `loadIndirect` traffic on struct fields and array headers, which
  is what `EC14` is about.
- **`MonomorphizationPass`** — ⚖ user ruling 2026-08-25 (`EC1`): keep ONE shared generic body. The
  answer to the generic-`Array` cost is inlining the primitives' fast paths, which `EC1` did.
- **`RefcountOptimizationPass`** — the bootstrap cancels `incref`/`decref` pairs a *later* analysis
  proves redundant. shv2 decides ownership statically in the parser, so the equivalent work is
  **not emitting the pair** — which is the open row `EC2`, not a dominator-based canceller.

⚠ **Stale, do not mine them:** `docs/microarch-optimization-plan.md` and
`docs/simd-optimization-opportunities.md` both target `maxon-bin/`, a C++ tree that no longer exists.
`docs/optimization-architecture.md` is an aspirational redesign nothing implements.
`docs/refcount-optimization-roadmap.md` **is** live, but it is the *bootstrap's* roadmap.
And `PLAN.md`'s "Measured debt" entry citing `JumpTableFormationPass.cs:12,55,59` is **stale** — no
such file exists; the bootstrap's dispatch is now `MaxonToStandardConversion.SwitchDispatch.cs` and
it *does* handle range arms, via intervals. That entry's premise needs re-measuring before it is used
to argue anything.

## THE ANCHOR — one 3-line loop, and every missing pass visible at once

`for v in a` over `Array with Integer`, summing (`temp/codegen-probe/arr.maxon`). **Re-measured after
`EC11`, `EC12`, `EC13`, `EC16`, `EC15` and `EC14`**: the executed hot path is **6 instructions per
element**, down from 15.

```
  entry:      load rdx,[rcx+8]      ← length      — EC14 hoisted it OUT of the loop
              load rcx,[rcx+0]      ← buffer base — EC14 hoisted it too (its Rule 2b)
  forhdr:     cmp rax,rdx ; jcc greaterEqual,forexit
  loop:       load rsi,[rcx+rax*8]  ← EC16 folded the `imul` AND the `lea` into this operand
  __im_cont:  lea  r8,r8,rsi        ← EC11 elided this block's `jmp forstep`
  forstep:    lea  rax,rax,1
              jmp  forhdr           ← the real back edge
```

An ideal x64 body is **5**: `mov rax,[rbx+r13*8]` · `add r12,rax` · `inc r13` · `cmp r13,len` · `jl`.
**15 → 6**, and the ONE that remains is the back edge's `jmp`, which loop ROTATION (not `EC11`'s
elision) would remove — the ideal's `jl` IS the rotated form's single branch.

⛔⛔ **AND THE 8 → 6 BOUGHT NO MEASURABLE TIME. READ `EC14`'s ROW BEFORE RANKING ANYTHING BY THIS
SECTION.** A microbenchmark of this exact loop over 4.1×10⁸ elements runs in **294 ms whether the
compiler hoists the two loads or not**, and `nbody` is 10.02 s either way — the loop is bound by its
taken branch and its loop-carried dependency at ~2.3 cycles/iteration, and an out-of-order core absorbs
two L1-hitting loads for free. **This section counts INSTRUCTIONS, which is a proxy for the quality of
the emitted code and not a measurement of time**, and `EC14` is where that proxy was first measured
against the thing it stands for and found not to track it. Instruction count is still the right axis
for a codegen inventory — it is exact, reproducible and attributable, where a benchmark is none of the
three — but a row that claims a SPEEDUP owes its own measurement.

⭐⭐ **`EC15` TOOK FOUR OF THE TWELVE AND THE FUNCTION'S WHOLE FRAME WITH THEM.** What stood between
`loop:` and `__im_word:` was a `loadIndirect` of `element_size@24`, a `cmp`/`jcc` against 8, a second
`cmp`/`jcc` against 1 and a `__il_slow` arm that CALLED `__managed_get_unchecked` — a runtime dispatch
on a type whose stride is 8 at compile time, plus the call it protected. `@sum` is now **call-free**
and no longer saves `rbx`/`r12`/`r13` or opens a frame at all, because the values that needed to live
across that call are gone.

⚠ Note what this is NOT: the `__im_*` arms were `EC1`'s inlined fast path, and `EC1` was a large
measured win (a checked read went 176 → 26 instructions). The defect `EC15` closed is that the inlined
arm was **not specialized by an element type the compiler already knows** — not that it was inlined.

⭐ **AND SINCE `EC17` THERE IS NO `@sum` LEFT TO LOOK AT — THE WHOLE FUNCTION IS A LEAF.** `EC15` made a
known-stride `for v in a` call-free, and `EC17` runs `inlineLeaves` after the pass that does it, so `sum`
now holds no call, fits the 24-op budget, and is spliced into `@main` — its frame, its `call`, its
argument move and its `ret` all gone with it (`arr.maxon`: 100 → 93 ops). The six-instruction body above is
unchanged; it is simply inside `main` now, between an `__il_body` and an `__il_cont`.

⭐ **THE JUMP THAT WAS THE ARGUMENT FOR BLOCK REORDERING IS GONE, AND NOT BY REORDERING.** This
section used to read *"the one jump left in the loop is the clearest argument for block reordering"* —
it was `__im_word`'s `jmp __il_cont`, unelidable because `__im_stride` was laid between them. `EC15`
deleted `__im_stride`, so the continuation IS physically next and `EC11`'s existing elision took the
jump. The reordering row stands on its own merits elsewhere; this loop is no longer its exhibit.

## The ranking

Ordered by (measured size of the win) ÷ (design risk). Every row is independent; the Tier 1 three
compound with each other and with every inlining win that lands after them.

### Tier 1 — measured on this tree, bounded, no design question open

#### `EC11` · Branch simplification + block layout

A Std→Std (or late Target) pass that (a) orders blocks so a branch's target is its physical
successor and deletes the resulting `jmp`, (b) merges a block whose only op is an unconditional
branch into its predecessors, (c) threads a branch to a branch.

**MEASURED** on `examples/nbody.maxon`: shv2 emits **10,389 ops with 1,016 `jmp`s, of which 410 (40%)
jump to the physically next block** — 3.9% of the whole instruction stream, every one a taken branch
— and **112 blocks whose only op is a `jmp`**. The bootstrap emits **312 `jmp`s and 0 jump-to-next**
on the same file. Attribution of the 410 by block-name family:

| family | count | source |
|---|---|---|
| `__im_*` | 168 | `inlineManagedPrimitives` (EC1) |
| `__il_*` | 60 | `inlineLeaves` (EC5) |
| `scmerge` | 45 | short-circuit merge |
| `ifcont` | 35 | `if` continuation |
| `whilehdr` | 29 | `while` header |
| `critsplit` | 26 | critical-edge splitting |
| `__rc_chk` | 18 | range checks (P1.9) |
| `forhdr` / `forstep` | 10 + 10 | `for` header and step |
| `matchcont` | 9 | `match` continuation |

⇒ **56% of it is debris shv2's own two inline passes create**, so it grows with every future
inlining win rather than shrinking.

Reference for the conditional half: `StandardToX86Conversion.cs:953` ("THE FALLTHROUGH INVARIANT"),
whose comment also records the two x64-only **silent miscompiles** that came from assuming the
invariant instead of enforcing it.

⛔ **THIS ROW'S STATED RISK WAS FALSE, AND IT NAMED THE WRONG COMPILER.** It read *"shv2 already
depends on a fallthrough contract on x64 (`StdCondBrOp` emits one `jcc` to Else)"*, citing
`[[project_condbr_fallthrough_is_an_x64_only_contract]]`. **MEASURED 2026-08-28: shv2 does not.**
`StdToX64Conversion.lowerCondBranch` (`:3724`) emits **BOTH** edges — a `jcc` body op naming the then
target, then a `jmp` TERMINATOR naming the else target — and its own comment says why: *"An
unconditional `jmp` to the else target is emitted for EVERY layout so correctness never depends on
block ordering (a later pass may elide a fall-through)."* The contract that memory names is the **C#
bootstrap's** `StdCondBrOp` (`maxon-sharp`, the `G24` row on `PLAN.md`), a different compiler. Block
order is semantically free at the shv2 Target tier — which is what makes this rung a DELETION rather
than a reordering, and is the whole of why it is low risk.

✅ **LANDED 2026-08-28** as `maxon-shv2/Compiler/Targets/X64/X64BranchCleanup.maxon`, scheduled in
`buildX64Backend` between `allocateRegisters` and `insertPrologueEpilogue` — after `applyAllocation`,
which is the whole correctness argument for the threading half (before SSA destruction has placed its
edge copies, a "jmp-only" block is an edge ABOUT to receive moves). Four transforms landed: jump
elision, conditional inversion, jump threading, unreachable-block elimination. **Block REORDERING did
NOT land and is still open** — it is what would CREATE new fall-through opportunities, where this
rung collects the ones already there. `scripts/emitted-code-count.py`, committed corpus, before
→ after:

| | ops | jmp | jmp→next | jmponly-blocks |
|---|---|---|---|---|
| nbody | 10,389 → **9,504** | 1,016 → **131** | 410 → **0** | 112 → **0** |
| fannkuch-redux | 3,014 → **2,567** | 520 → **73** | 286 → **0** | 34 → **0** |
| TOTAL | 13,687 → **12,339** (−9.8%) | 1,555 → **207** (−87%) | 709 → **0** | 147 → **0** |

⚠ The `jmp` column falls by far more than the `jmp→next` count alone, and the reason is worth
knowing: threading deletes whole forwarding BLOCKS, so each one's own `jmp` goes with it and the
branches that named it now reach the real target directly. Elision alone would have removed 709.

**ACCEPTANCE**: nbody's jump-to-next count 410 → 0; total emitted ops down ≥3%; byte-identical
self-host fixpoint; suite green on x64-windows and wasm; `--emit-ir` diff audited on the anchor loop.

#### `EC12` · `foldConstants` — evaluate `const ⊕ const`

shv2's own design named this pass and **deferred it** (`FoldConstOperands.maxon:5-7`,
`DEVLOG.md:348`) for one stated reason: *"it would collapse the test programs to `mov r8,k` and erase
the codegen the fragments exist to show."*

⭐ **That reason expired on 2026-08-27**, when the user ruled goldens informational and `1152bbc566`
removed golden drift from the whole rung process.

**MEASURED**: `2 + 3 * 4` emits `mov rcx,3 ; imul rcx,rcx,4 ; lea rcx,[rcx+2]` — three instructions
for the constant 14. After inlining, `x*31+y` with `x=3, y=4` emits the full six-instruction sequence
**three times over** rather than one `mov rax,97`.

⚠ **The rung's real work is the specs, not the pass.** A fragment that demonstrates `imul` codegen
using two literal operands is a fragment that would stay green if the compiler folded it *wrongly*.
Rewrite those to take a runtime input — strictly better testing, and the
[[green-case-proved-nothing-sabotage]] shape. Reference: `maxon-selfhosted/Compiler/IR/Std/Canonicalize.maxon`.

⚠ **A trap already paid for once in this tree**: `TypeRules.foldIntDivision` refused `i64.min mod -1`
for *both* operators — the constant folder disagreeing with the language it folds for. Every fold
must agree with the emitted instruction on every edge case; `i64.min / -1` and division by zero
**trap** (`isPure: false`) and must not be folded away.

✅ **LANDED 2026-08-28** as `maxon-shv2/Compiler/IR/Std/FoldConstants.maxon`, scheduled in
`buildLoweringPasses` between `elimTrivialBlockArgs` and `foldConstOperands` — after BOTH inliners,
which is what makes the post-inline constants visible, and before the operand rewrite so a constant it
declines still becomes an immediate. `scripts/emitted-code-count.py`, committed corpus, before → after:

| | ops | imul-imm | imul-pow2 | idiv |
|---|---|---|---|---|
| nbody | 9,504 → **9,481** | 45 → **40** | 41 → **37** | 8 → 8 |
| fannkuch-redux | 2,567 → **2,458** | 33 → **24** | 33 → **24** | 3 → 3 |
| cse2 | 75 → **67** | 3 → **0** | 0 → 0 | 0 → 0 |
| probe | 67 → **59** | 2 → **0** | 2 → **0** | 3 → 3 |
| TOTAL | 12,339 → **12,191** (−1.2%) | 84 → **65** | 77 → **62** | 14 → **14** |

`idiv` is byte-identical, as it must be: a division is not a `binOp` at all (`StdOp.div`/`mod` carry
`isPure: false` because `idiv` traps), so no fold can reach one and no prune can delete one.

⭐⭐ **THE DUPLICATE-EVALUATOR QUESTION WAS ANSWERED BY SHARING.** `TypeRules.foldIntBinOp`,
`TypeRules.evalShift` and `TypeRules.foldIntCompare` already decide what `const ⊕ const` evaluates to,
for `Parser.recordBinOpFold`; the pass CALLS them rather than carrying its own arithmetic, bridging
`StdBinOpcode`/`StdCmpPred` back to `MaxonBinOp`/`MaxonCmpOp` through CHECKED INVERSES of the
lowering's own maps (`maxonBinOpOfStdOpcode`, `maxonCmpOpOfStdPred`) that panic if the two directions
ever diverge. It is sound because every integer `binOp`/`cmp` in shv2 computes at the MACHINE WORD on
all three targets and every comparison is signed — which is exactly the arithmetic a `ParsedInt` is.

⚠ **THE ROW'S PLAN SAID DEAD-BLOCK REMOVAL COULD BE LEFT TO `EC11`. IT CANNOT.** MEASURED: `match x`
over a constant subject panicked the register allocator (`seedInUse: value 18 is live-in to block 11
but was never colored`). An unreachable block is harmless; an unreachable block that FEEDS A PHI is
not, and folding a `condBranch` makes one — the arm not taken keeps its edge into the merge block, and
`reorderFuncBlocksRpo` then appends it AFTER the block that reads it. The pass drops the orphaned arm
at the Std tier itself.

**Left open**: a `switch` on a constant subject (a jump-table fold, not a branch fold); UNARY folds
(`neg`/`bitNot` over a constant), whose absence leaves the guarded-shift mask cascade half-folded; and
a def-before-use walk order — `func.blockRefs` records when a block was MINTED, so a range check
emitted before inlining is walked before the constant inlining created. That last one is MEASURED at
2 ops out of 12,191 on this corpus, which is why the pass stays one linear forward walk; `EC13`'s CSE
needs a dominance order anyway and is the rung to build it in.

#### `EC13` · Common subexpression elimination

**MEASURED**: three occurrences of `x * 31 + y` in one function emit `imul`/`lea` **three times into
three registers**, then combine — six instructions and three live values where two and one would do.

Register pressure is shv2's known sore point (E5001 is a *diagnostic* the compiler raises when it
cannot allocate), so CSE pays twice: fewer ops, and a narrower live set for the chordal allocator.

Reference: `maxon-selfhosted/Compiler/IR/Std/CommonSubexpressionElimination.maxon` (494 lines) — v1
schedules it `mem2reg → canonicalize → cse → licm → dce`. ⚠ shv2 must key on `isPure` (`StdOpMeta`):
a `div` is not pure (it traps) and a `call` is not (effects). ⚠ And it must run **after**
`inlineLeaves`, which is what makes identical expressions co-resident in one block at all.

✅ **LANDED 2026-08-28** as `maxon-shv2/Compiler/IR/Std/CommonSubexpressionElimination.maxon`, scheduled
in `buildLoweringPasses` between `foldConstOperands` and `promoteStackRecords`.

⭐⭐ **"AFTER `foldConstants`" WAS NOT ENOUGH — IT HAD TO BE AFTER `foldConstOperands`, AND THAT IS THE
ROW'S FIRST FINDING.** The lowering mints a SEPARATE `const` op per literal occurrence (there is no
constant interning), so before the operand rewrite the three copies of `x * 31 + y` are three `binOp`s
over three DIFFERENT value ids and no expression comparison can call them equal. `foldConstOperands` is
the canonicalization that makes them one shape — it moves the literal INTO the op as an `imm` and puts
a commutative op's constant on the rhs. Scheduled one pass earlier, this rung finds nothing.

**Scope, and the three measurements that set it:**

- **DOMINATOR-SCOPED**, not intra-block. `StdDominatorTree.maxon` is new and is what `EC14` needs too:
  successors via the dialect's `collectStdBlockSuccessorIds` (moved into `StdDialect` beside the other
  two exhaustive `StdOp` rosters), three CSR adjacencies via the existing `buildCsr`, a DFS reverse
  postorder and the Cooper–Harvey–Kennedy iterative solve. ⚠ **An intra-block-only first cut measured
  8 hits in `cse3.maxon` and ZERO in nbody and fannkuch** — shv2's own two inliners fragment every
  array access and every leaf call into a diamond, so a program's repeated arithmetic lands in
  different blocks. (An unsound function-wide table, run only to bound the answer, found 79 and 47.)
- **A CALL IS A BARRIER TO REUSE**, and that rule is the row's second finding. Without it,
  `generic-hash-table-regalloc/…witness-dispatch-inside-a-pressured-loop` — a spec built to sit exactly
  at the register pool with nineteen values live across a call — went RED with `E5001 … needs 1 more
  register`, **for ONE reused expression**. shv2 REFUSES rather than spills, so a CSE that lengthens a
  live range is a compile error rather than slower code, and the ranges that cost most are the ones
  crossing a call (confined to five callee-saved registers). ⚠ The count follows the DOMINATOR path, so
  a call on a side path is not seen — permissive, never unsound.
- **MEMORY AND `div`/`mod` ARE OUT, BY THE DIALECT'S OWN FLAG.** This pass is the FIRST reader
  `StdOpMeta.isPure` has ever had (its field comment said so and now records the change). `loadIndirect`
  and `div`/`mod` are `isPure: false`, so neither is in the shared arith roster nor past the purity
  gate, and the pass re-asks the flag of every shape the roster admits and PANICS on a disagreement.
  Hoisting loads is `EC14`'s row. **Floats are IN** — CSE changes no value, so there is no NaN or
  signed-zero argument to make; only operand REORDERING is float-excluded.

⭐⭐ **THE EQUALITY PREDICATE ALREADY EXISTED IN PART, AND THE ANSWER WAS TO WIDEN IT RATHER THAN WRITE
A FOURTH ROSTER.** `classifyArithOperands`/`StdArithOperands` (was `classifyBinaryOperands`) is the
dialect's one answer to "what does this op compute, out of what?", shared with `foldConstOperands` and
`foldConstants`; it gained the three shapes it used to decline (`unary`, `binaryArithImm`,
`comparisonImm`) — their exclusion reasons were about what a CONST FOLD can do with them, never about
what they compute — plus `computesSameValueAs`. The substitution column's three operations
(`recordValueSubstitution`, `remappedValue`, `substituteValuesInFunc`) and the block rebuild
(`removeOpsDefining`, now shared with `foldConstOperands`' const DCE) are likewise the dialect's.

`scripts/emitted-code-count.py`, committed corpus, before → after:

| | ops | imul-imm |
|---|---|---|
| nbody | 9,481 → **9,477** | 40 → 40 |
| fannkuch-redux | 2,458 → **2,456** | 24 → 24 |
| cse3 | 26 → **22** | 3 → **1** |
| TOTAL | 12,217 → **12,207** | 66 → 66 |

⚠ **THAT CORPUS UNDERSTATES IT AND THE SCALE LADDER DOES NOT.** With a CONTROL taken the same session
(the same binary with the one `passes.push` line removed), `scale-test`'s generated corpus reads
**emitted code bytes 2,888,699 → 2,842,895 at rung 5, −1.59%, and −1.1%…−1.6% at every rung**, for
**+0.96%…+1.19% of compile allocations** (flat across the ladder) and +2.0% of compile CPU. The two
corpora disagree because they contain different amounts of repeated arithmetic, not because either is
wrong: nbody's repeats are mostly the array-index scaling `EC15`/`EC16` remove, which lives in sibling
diamond arms that dominate nothing.

⛔⛔ **THE FIRST CUT WAS A QUADRATIC THE MEMORY COLUMNS COULD NOT SEE, and the corpus caught it.** The
expression table bucketed on the FIRST OPERAND — dense, no arithmetic, and wrong: `lhs` is a dense index
for a VALUE, not for an EXPRESSION. `scale-test`'s own `c_long` knob (`a + 1`, `a + 2`, … `a + N`) is N
distinct expressions sharing one operand, so every probe walked one long chain: **CPU ×1.77 ×2.07 ×2.28
×2.79 ×3.12 across a DOUBLING ladder while allocations stayed linear at ×1.89.** Mixing both halves of
the expression into the key fixed it with no allocation change and no change to the emitted code
anywhere (6,801 goldens, 0 differing), taking the phase from 5.38e9 to 1.48e9 ticks at rung 5.

**Left open**: `const` unification (v1's Stage A, whose own header records the ABI-constrained-use
hazard — MEASURED here: 369 of nbody's 524 `movRegImm32` are duplicates within one function, and most
are call arguments a merge would drag across a call); the three pure ADDRESS ops (`globalAddr`,
`rdataAddr`, `funcAddr` — 33 duplicate `leaRegRdata` within a function on nbody), which need a second
bucketing scheme because they have no value operand; and a strict per-block call summary, which would
let the barrier see a call on a side path.

### Tier 2 — real wins, one design question each

#### `EC14` · Loop-invariant code motion

The anchor loop reloads **two** invariants per element (length and buffer base — `EC15` removed the
third, `element_size`, along with the fork that read it, and made the loop body CALL-FREE, which is what
makes this row tractable at all: `EC13` measured that a live range crossing a call is confined to the
callee-saved registers and that shv2 REFUSES rather than spills).
⭐ **The safety fact shv2 needs is already proven by its own frontend**: the loop element is a
BORROW of the subject and the borrow is *lexical*, so a call that takes the subject mutably while the
loop is live is refused by the borrow checker (`Parser.maxon:37545-37560` — the ⚖ 2026-08-26 ruling
`EC3` landed, whose comment says outright *"Do not 'fix' this by narrowing the borrow, the lock or
`iterationSubjectNameAt` — the refusal IS the ruling"*; the four round-trip cases are
`specs-shv2/borrow-liveness.md`). ⇒ **the array header cannot change under the loop, and the compiler
already knows it.** Reference: `LoopInvariantCodeMotion.maxon` (596) + `CfgAnalysis.maxon` (592 —
natural loops + dominators). ⭐ **The dominance half already exists**: `EC13` landed
`maxon-shv2/Compiler/IR/Std/StdDominatorTree.maxon` (successors, reverse postorder, immediate
dominators, dominator-tree children), so this rung owes natural-loop detection — back edges are
`u → v where v dominates u` — and not a dominator solve. ⚠ Hoisting raises live ranges; measure the
register-pressure diagnostic and the spill count, not only the op count. `EC13` MEASURED what that
costs on this compiler: one reused expression across a call took a knife-edge pressure spec red with
`E5001`, which is why its own reuse rule stops at a call. LICM's hoists cross the loop, which is
strictly worse for pressure, so the trade has to be made explicitly rather than discovered.

✅ **LANDED 2026-08-28** as `maxon-shv2/Compiler/IR/Std/LoopInvariantCodeMotion.maxon`, scheduled in
`buildLoweringPasses` between `commonSubexpressionElimination` and `promoteStackRecords` — v1's own
order (`canonicalize → cse → licm → dce`), and for a reason of this compiler's: CSE has already
collapsed a repeated invariant expression to ONE op, so this pass moves one instruction and lengthens
one live range instead of three.

⭐⭐ **THE ANCHOR LOOP IS 6, WHICH IS THE ROW'S STATED TARGET AND THE END OF THE 15 → 6 SEQUENCE.**

```
  entry:      load rdx,[rcx+8]      ← length      — HOISTED
              load rcx,[rcx+0]      ← buffer base — HOISTED
  forhdr:     cmp rax,rdx ; jcc greaterEqual,forexit
  loop:       load rsi,[rcx+rax*8]
  __im_cont:  lea r8,r8,rsi
  forstep:    lea rax,rax,1 ; jmp forhdr
```

Both of `EC14`'s reloads are gone. What is left against the roadmap's "ideal 5" is the back edge's
`jmp`, which loop ROTATION would remove and this rung does not attempt.

**TWO PHASES, TWO SAFETY ARGUMENTS, and the whole content of the row is the second.**

- **PURE COMPUTATION** needs no argument beyond `StdOpMeta.isPure` ("can be duplicated, reordered, or
  dropped"), and is hoisted from ANY block of the loop — out of a whole NEST in one run, because loops
  are processed innermost-first and each loop's hoists are applied before the next is analysed. v1's
  LICM is exactly this phase and states the same reason. `div`/`mod` never reach it (they trap, and the
  arithmetic roster answers `neither` for them); `const` and the pure ADDRESS ops are deliberately left
  alone, for `EC13`'s recorded reason — rematerializing beats a live range spanning the loop.
- **INVARIANT LOADS** are `isPure: false` and get **two rules, each stated as a rule**:
  - **Rule 1 — invariance.** A loop none of whose ops is `isStore` or `isCall` cannot change any
    location. That is a conservative rule, not an alias analysis, and it is exactly sufficient because
    `EC15` made the anchor loop call-free. MEASURED against the union: **20 of the 99 variants declare
    neither flag**, and every one is pure arithmetic, a pure address, a branch, a RETURN, a trapping
    division or a read. ⛔ An earlier spelling of this census named only the seven IMPURE ones and
    called them "the only variants" — wrong, and wrong in the direction that matters, because it told a
    reader that a loop containing a `return` was refused here. It is not: a block ending in `ret` has no
    successors, so `collectNaturalLoop` never admits it, so its predecessor is an EXIT and Rule 2a is
    what covers the return path. The pass's own header now carries that argument; this was one fact
    written down twice and wrong in both.
  - **Rule 2 — speculation.** A hoisted load executes even when the loop body never runs, and the
    anchor makes that concrete: the length is read in the HEADER (runs whenever the preheader does) and
    the buffer base in the block the guard branches to, which **for an empty array never runs**. So:
    **2a** the block dominates every exit AND every latch, or **2b** the loop is already hoisting,
    under 2a, a load through the SAME address value with an equal-or-greater `offset + width`. A
    `loadIndirect`'s `(addrId, offset)` names field `offset` of the object at `addrId` and `ByteOffset`
    is non-negative by type, so a load the program performs unconditionally at a further offset proves
    the nearer field is inside the same object — **2b is what takes the anchor from 7 to 6.**

⚠⚠ **THE `ops` COLUMN CANNOT SEE THIS ROW, AND THE FINAL READING IS `TOTAL 10,558 → 10,558` — EVERY
COLUMN BYTE-IDENTICAL, INCLUDING `arr.maxon`'s OWN 100.** LICM MOVES ops rather than deleting them, so a
static count of a whole program is flat by construction even where the pass fires: `arr.maxon`'s two
loads left its loop and stayed in its function. Before Rule 3 this column read **+6**, which was
register-pressure churn where a hoisted value in a call-heavy loop lengthened a range; Rule 3 removed
the churn along with the hoists that caused it. ⇒ **This instrument reports nothing at all for this row,
in either direction.** The reading that matters is the anchor's executed body, 8 → 6 per element, which
only a look at ONE function shows.

⛔⛔ **AND THE TIME DID NOT FOLLOW THE INSTRUCTION COUNT. MEASURED, AND IT IS THE MOST IMPORTANT NUMBER
IN THIS ROW.** A microbenchmark of *exactly* the anchor loop — 4.1×10⁸ element iterations, one program
compiled by the two binaries — runs in **294 ms either way** (min of 7 alternating runs; 871 vs 874 ms
at 3× the work; an L2-resident 160 KB array and an L1-resident 32 KB array agree). `nbody` is likewise
**10.02 s either way**. ⇒ The loop is bound by its taken branch and its loop-carried dependency at
~2.3 cycles/iteration, and an out-of-order core absorbs the two L1-hitting header loads for free.
**The "ideal 5 instructions" axis this document ranks by is a PROXY, and on this shape it is measurably
not tracking time.** That does not make the row wrong — fewer instructions is less I-cache and fewer
load-port µops in a loop that is not alone, and the Std-tier loop machinery is what later rows need —
but no row below may be justified by "instructions are time" without measuring it.

⚠ **`scale-test` CANNOT SIZE THIS ROW EITHER, AND THE REASON IS THE CORPUS, NOT THE ROW.** With a
control taken the same session (the same binary with the one `passes.push` line removed), emitted
`codeBytes` is **BYTE-IDENTICAL at every rung — a delta of exactly 0**. `ScaleCorpus`'s array knob emits
`create`/`push`/`get`/`count`/`slice` straight-line with **no loop over an array at all**, and every loop
the ladder does generate holds a call, which Rule 3 refuses. So the ladder cannot express the construct
this row optimizes and can see none of the win. That is the instrument's blind spot, exactly as
`.claude/CLAUDE.md` warns; it is a corpus gap, and it is filed rather than fixed. (Before Rule 3 the
same control read **+64 bytes at every rung** — a constant, i.e. one stdlib function, and one that Rule 3
then declined.)

**What the control DOES size is the COST**, and that column is real: **+0.14% of compile allocations,
+0.70% of bytes and +0.60% of CPU at rung 5**, falling with rung size from +0.40% / +0.55% at rung 0.
`phase:loopInvariantCodeMotion` is 0.14% / 0.70% / 0.58% of the rung-5 compile. A trend row is logged.

⛔⛔ **THE FIRST CUT WAS SUPERLINEAR IN BOTH COLUMNS, AND THE LADDER CAUGHT IT: bytes ×2.49 and CPU
×2.69 per DOUBLING.** Three causes, all one shape — **a per-LOOP cost proportional to the FUNCTION**,
on a corpus whose pressure knob is one function with N loops where N grows with the rung:

| | rung-5 allocs | rung-5 bytes | rung-5 CPU | bytes ×/doubling | CPU ×/doubling |
|---|---|---|---|---|---|
| first cut | 158,106 | 81,919,720 | 1.182×10⁹ | ×2.49 | ×2.69 |
| landed | **62,980** | **30,896,818** | **0.540×10⁹** | ×2.30 | ×2.55 |

1. `naturalLoopMembers` returned membership as a `BoolArray` over ALL blocks, so **all three** of its
   consumers answered "which blocks are in this loop?" with `for b in 0 upto numBlocks`, once per loop
   — and each loop also allocated its own block-wide column. It now fills a caller-owned
   `LoopMembership` carrying a member LIST, which fixes the Target tier's `computeLoopDepth` and
   `enclosingLoopBlocks` at the same time.
2. `StdValueUseSet.clear()` keeps capacity and the next `insert` re-extends it — one pass over the
   function's value space **per loop**. Replaced by a GENERATION STAMP, whose reset is one increment.
3. The dominator tree was built for **every** function before asking whether it had a back edge at all.
   `StdDominatorTree.buildFromSuccessors` splits the successor graph (all the back-edge DFS needs) from
   the predecessor graph, the reverse postorder, the solve and the child adjacency, which only a
   function with a loop ever uses.

⚠ **A RESIDUAL BEND REMAINS AND IS NOT EXPLAINED AWAY: ×2.30 bytes / ×2.55 CPU.** It sits beside
`regalloc`'s own ×2.33 on the same ladder, on a phase that is 0.6% of the compile. Open.

⭐⭐ **A BOUND WAS IMPOSED, AND IT WAS MEASURED INTO EXISTENCE RATHER THAN ASSUMED — `RULE 3`: A LOOP
HOLDING A CALL HOISTS NOTHING, ARITHMETIC INCLUDED.** The first cut bounded only LOADS that way (Rule 1
already refuses them), and the minted goldens are what caught the rest: **48 `map` fragments gained +96
`loadRegSlot` and +48 `storeSlotReg`**, and every one of their frames grew, because a hoisted value in a
call-heavy loop is COLD-SPILLED rather than kept. In `Map.grow` the entire trade was one
`leaRegRegImm32` replaced by one `loadRegSlot` — an ALU op for a memory op, which is a loss at equal
instruction count. `EC13` had measured the other end of the same fact (shv2 REFUSES rather than spills,
so one reused expression across a call was an `E5001`); this is what the same hazard looks like when the
allocator does not refuse.

⇒ It is a structural rule, not a budget with a number to justify. It costs the anchor nothing (`EC15`
made that loop call-free) and `regalloc/many-call-crossing` nothing (nine invariant computations still
leave its loop — that loop holds no call), and both were checked before it was adopted. **419 of the 466
moved goldens went back** when it landed, and the `scripts/emitted-code-count.py` corpus returned to
byte-identical.

**PRESSURE**: zero `E5001` in a 6,818-case run, and `EC13`'s red case
`witness-dispatch-inside-a-pressured-loop` stays green. It is not free even so —
`regalloc/many-call-crossing`'s fragment gained one `pushReg r13`, the price of nine values hoisted out
of a call-free loop. ⚠ Rule 3 sees MEMBER blocks only, so a call on a loop-EXIT path is not counted: a
gap in the heuristic, not in the safety argument, and the same one `EC13`'s barrier note records.

**GATES**: x64-windows **6,818 passed, 0 failed, exit 0** (6,812 + 6 new) and wasm32-wasi **6,358
passed, 0 failed, exit 0** (6,352 + 6); **47** goldens re-minted from ONE unfiltered run and re-verified
at zero drift, 6 added. ⭐ That number was **466 before Rule 3**, which gave 419 of them back — the
clearest single statement of what the bound is worth: nine tenths of this pass's effect on the committed
corpus was spill churn in call-heavy loops. New `specs-shv2/loop-invariant-code-motion.md`, 6 cases including **two
sabotage-verified controls**: disabling Rule 1 turns `a-loop-that-writes-keeps-its-loads-inside` red
with a WRONG ANSWER (exit 1), and disabling Rule 2a moves exactly the two fragments whose pins it is.

⚠⚠ **TWO EARLIER SPELLINGS OF THE RULE-1 CONTROL PASSED UNDER ITS OWN SABOTAGE**, and the reason is
worth carrying: a load through a module-level `var` is refused for having a loop-defined ADDRESS
(`globalAddr` is minted inside the loop), and a field load in the loop's BODY is refused by Rule 2a —
so neither ever reached Rule 1. **In shv2's loop shapes Rule 2a admits essentially only the loop
HEADER's own loads**, plus whatever 2b carries with them, so a Rule-1 control has to put the load in the
loop's CONDITION through an address computed before the loop.

**Left open**: `const` and the three pure ADDRESS ops (a hoist with no instruction-count argument —
`EC13` filed the same three); CREATING a preheader where none exists (a loop entered from two places, or
through a conditional branch, is skipped whole); hoisting a load out of a NEST past one level, which
Rule 2a refuses at the outer loop's exit; the residual bend above; and the corpus gap — `ScaleCorpus`
has no loop over an array, so no future row touching element access can be sized on that ladder either.


#### `EC15` · Static stride specialization for the inlined managed primitives

The `cmp element_size,8` / `jcc` dispatch and the dynamically-unreachable `__il_slow` call arm are
emitted in every array access, including where the element type fixes the stride at compile time
(`Array with Integer` is always 8). `EC1`'s pass
emits its guards ordered by discrimination; this rung makes it **skip** the ones a known
`GenericInstanceId` already answers. Closes the largest per-element residue the anchor shows, and
shrinks what `EC11` then has to lay out.

✅ **LANDED 2026-08-28.** `scripts/emitted-code-count.py`, committed corpus, before → after — **the
largest single row of this workstream so far, by a factor of four**:

| | ops | im-blocks | mgd-call |
|---|---|---|---|
| nbody | 9,407 → **8,602** | 302 → **140** | 96 → 96 |
| fannkuch-redux | 2,408 → **1,708** (−29%) | 327 → **180** | 48 → 48 |
| arr | 124 → **100** | 3 → **1** | 6 → **5** |
| TOTAL | 12,087 → **10,558** (−12.7%) | 632 → **321** (−49%) | 150 → **149** |

`jmp` 207 → 181; `jmp→next`, `jmponly-blocks`, `imul-imm`, `imul-pow2` and `idiv` are byte-identical,
as they must be — this row deletes blocks and their guards, it selects no instruction differently.

⭐ **TWO COLUMNS WERE ADDED TO THE INSTRUMENT, because the defect this row removes had none.** `mgd-call`
counts `callDirect __managed_*` (the slow arms plus every site the pass declines) and `im-blocks` counts
blocks labelled `__im_*` (the scaffolding one inlined element access costs). The baselines above are
measured on `3b519b230a` with the new columns, not carried forward. ⚠ **`mgd-call` moved by ONE and that
is not a weak result, it is the wrong corpus for it**: nbody and fannkuch's `__managed_*` calls are
`push`/`create`/`decref` and the slow arms of `get`/`set`, which keep their other guards. Its −1 is
`arr`'s `__managed_get_unchecked`, and that one call is the whole "is the anchor loop call-free" question.

**WHERE THE FACT COMES FROM, AND WHY IT IS NOT ON THE OP.** `StdOp.call` carries `callee` and `args` and
NO type — the Std tier is deliberately type-free — so the tier that still HAS the container's type is
`LowerMaxonToStd`, which writes the stamp against the **Std op index** of the call it is appending
(`Project.stdOpElementStrides`). A `(blockId, stdOpPos)` table like `RangeCheckSite`'s could not survive:
`insertRangeChecks` and `inlineLeaves` both split blocks. The op index does, and for a stated reason —
`IrModule.ops` allocates indices by `push`, and **a leaf holds no call by rule** (`LeafOpRole.calling`
refuses the callee outright), so `inlineLeaves` can neither clone nor re-issue a managed primitive.
⇒ every recorded index still names the very call the lowering appended.

⭐ The recorder is **callee-agnostic** — it asks about the ARGUMENT (`isArrayInstanceAt`, the one home of
that two-part test) and not about a roster of primitive names, so it cannot drift from
`InlineManagedPrimitives`' own dispatch. An entry recorded for a call the pass never expands is never read.

⛔⛔ **THE STATIC STAMP IS NOT ALWAYS THE RECORD'S, AND THE FIRST CUT WAS A WRONG ANSWER.** MEASURED: a
`Bag with Byte` whose `Array with Element` field is read from OUTSIDE the shared body printed
`stamp=8 v=0` where 7 is correct. `Parser.emitOpaqueArrayCreateOp` stamps `LAYOUT_TYPE_PARAM_SLOT_BYTES`
— one machine word, because the ONE compiled body moves elements as words whatever the instantiation —
while `Parser.slotTypeThroughReceiver` hands the same record back typed `Array with Byte`, stride 1.
What is provable is `actual ∈ {static stamp, MachineWordBytes}`, and that leaves exactly three answers:
**word** is safe (both possibilities are the word); **byte** is NOT (1 and 8 are two different arms, so
the fork is exactly the question that has to be asked); **anything else** keeps the CALL, which reads the
real stamp and is right for both. The sabotage is recorded: restore the byte arm and
`static-stride-specialization/a-substituted-container-field-is-word-strided-however-it-is-typed` goes red
with exit 3 while the whole rest of the suite stays green.

⚠ **THE BYTE ARM IS THEREFORE LEFT ON THE TABLE.** A byte-stamped site could keep BOTH single-op arms and
route `emitStrideDispatch`'s third edge to the WORD arm rather than to the call — sound by the same
reading, and enough to make a `for v in b` over a `ByteArray` call-free the way the word case now is. It
is a third emission shape and this row did not take it. The corpus above cannot see the loss (it holds no
byte arrays in a hot path); `Map.findSlot`'s `__managed_get` can, and EC3 measured that at 2% of a
stage-2 self-compile.

⭐ **WHAT WAS REUSED RATHER THAN ADDED**: `isArrayInstanceAt` (the tag+id gate, unchanged),
`containerElementIsOpaque` (the W57 refusal), `arrayElementSize` (the stamp's sole producer),
`Project`'s three existing `OpIndex`-keyed side tables as the precedent, and `SingleOpStride` —
`emitStrideDispatch`'s two constants now come out of `singleOpStrideBytes` too, so the two stamps are
written ONCE and the compile-time answer cannot disagree with the runtime compares it replaces.

⛔ **A LABEL COLLISION WAS FOUND AND THE UNDERLYING DEFECT IS NOT FIXED.** `InlineManagedPrimitives` and
`InlineLeaves` each declared a file-level `let InlineContLabel` / `InlineSlowLabel`, and shv2 resolved
both files to ONE of them: this pass had been emitting `__il_cont` / `__il_slow` — measured on a program
whose log reads `inlineLeaves: 0 site(s) inlined`. Renamed here; **the compiler silently unifying two
same-named non-`export` file-level `let`s in different files is a separate rung.**

**Left open**: the byte arm above; block REORDERING, whose clearest exhibit this loop was and no longer is;
and the type/layout incoherence itself — a container declared over a type parameter genuinely has a
different layout from its substituted type's, and every compile-time reader of `arrayElementSize` on such
a value shares the hazard this row's byte arm ran into.

#### `EC16` · Scaled-index addressing

shv2's x64 dialect had exactly `loadRegBaseDisp(base, disp)` and `leaRegRegReg(base, index)` at
**scale 1** — there was no `[base + index*scale + disp]`. So every element access paid a separate
`imul`/`lea` to scale the index. **MEASURED** on nbody: **40 `imul`-by-immediate, 37 of them by a power
of two and 36 of those `×8`** — element-index scaling, one per array access. This is the hottest shape
in the language: `EC1`'s `inlineManagedPrimitives` puts an inline element access at every array read
and write in every program.

✅ **LANDED 2026-08-28.** Four new `TargetOp`s appended at the union tail — x64's
`leaRegBaseIndexScale` / `loadRegBaseIndexScale` / `storeBaseIndexScaleReg` and arm64's `arm64AddLsl`
— plus `StdLoweringShared.ScaledIndexFolds`, the instruction-selection analysis that matches
`binOpImm(mul, index, 2^k)` → `binOp(add, buffer, offset)` → `loadIndirect`/`storeIndirect` and hands
the whole address to the memory op. `scripts/emitted-code-count.py`, committed corpus, before → after:

| | ops | imul-imm | imul-pow2 |
|---|---|---|---|
| nbody | 9,477 → **9,407** | 40 → **5** | 37 → **2** |
| fannkuch-redux | 2,456 → **2,408** | 24 → **0** | 24 → **0** |
| arr | 126 → **124** | 1 → **0** | 1 → **0** |
| TOTAL | 12,207 → **12,087** (−1.0%) | 66 → **6** | 62 → **2** |

**−2 ops per folded chain, not −1**: 60 `imul`s went and `ops` fell by 120, so every one of them took
its `lea` with it — the full 3 → 1 collapse. `jmp`, `jmp→next`, `jmp-only blocks` and `idiv` are
byte-identical, as they must be.

⭐ **THE COMPILER ALLOCATES LESS FOR IT, WHICH THE ROW DID NOT PREDICT.** Against a real control (the
same tree with the change stashed, both binaries built and laddered in one session): emitted **code
bytes −0.39%…−0.46% at every rung**, for **−1.59% of compile allocations** at rung 5 — `phase:regalloc`
alone is **−4.97%** (and its CPU −4.6%), because the two intermediates the fold deletes are two fewer
values to colour. The price is `phase:isel`: +0.12% allocations and **+17.3% CPU**, which is the
analysis's two O(ops) walks over every function whether or not it indexes anything — 2.3% of the
compile, so +0.4% overall, and linear on the ladder (×1.64…×1.93 per doubling). The scale corpus
contains little array indexing, so −0.4% of emitted code is a floor rather than the win on real code.

⭐⭐ **NO PARALLEL ENCODER WAS WRITTEN, WHICH WAS THE STANDING INSTRUCTION AFTER EC12 AND EC13.** The
SIB index became an OPTIONAL PARAMETER of `X64Backend`'s existing `[base + disp]` assembler
(`emitBaseDispModRmBits`), so `[base + disp]` and `[base + index*scale + disp]` are one address
operand with one home for the rsp-index and rbp/r13-disp8 traps — and `encodeLeaRegRegReg` and the
jump table's `encodeMovsxdRegBaseIndexScale4`, which each hand-spelled their own SIB, now route
through it. `emitRexRXB` was deleted into `emitRex` (it differed in the REX.X bit and in nothing
else), and `classifyArithOperands` — the dialect's one answer to "what does this op compute?" — is
what the new analysis matches on, gaining only an `isBinaryArithImm()` predicate.

⚠ **THAT +17.3% WAS +4.8% UNTIL REVIEW, AND THE DIFFERENCE IS A DELIBERATE TRADE.** The first cut
skipped the use walk for functions with no scale multiply, through a SECOND `blockRefs` → `opRefs`
descent — and two descents over the same ops fail here in the PERMISSIVE direction and in silence: a
pre-scan that came to visit fewer ops than the record walk would stop the fold for that whole
function, and the goldens would simply be MINTED showing the unfolded three-instruction chain with
nothing red anywhere. One descent, 0.4% of compile CPU.

⚠ **THE FIRST CUT COST +47% OF `phase:encode`'s ALLOCATIONS, AND ONLY THE CONTROL SAW IT.** The index
was a RECORD with a `none()` default, and a record-typed default is CONSTRUCTED at every call — of an
encoder that runs once per emitted instruction: 372,734 → 548,168 allocations at rung 5, with the
emitted code byte-identical either way. It is two scalars now (`index` + `indexScale`), where "no
index" is spelled `X64Register.rsp` — not an invented sentinel but x64's own encoding, since SIB index
field 100 with REX.X clear IS "no index" and 100 is rsp's number, which is exactly why rsp can never
be a real index. `StdArithOperands`' header records the same trap one tier up.

**Scope, and the three conditions that set it** (`specs-shv2/scaled-index-addressing.md` is the pin):

- **The multiplier must be 1, 2, 4 or 8**, stated once as `MemoryIndexScale`'s case list because it is
  what x64's two SIB scale bits and AArch64's `LSL` amount both hold.
- **The intermediate must have NO OTHER READER**, or it must be materialised anyway and the fold ADDS
  an instruction. `StdValueUseSet` gained an OPT-IN repeat column for it — the same walk, one more bit
  of resolution — and `collectFunctionValueUses` (its whole-function wrapper, now shared with
  `FoldConstOperands`) is what makes the count complete: op operands, terminators, phi branch-edge
  args, and a value read TWICE by one op, which is how `t + t` is refused with no special case.
- **The chain must be in ONE BLOCK.** Cross-block is SOUND (the add's def dominates the memory op, so
  its operands do too) but stretches `base` and `index` across a block boundary in place of one
  value's, and shv2 REFUSES rather than spills — EC13 measured exactly that as an `E5001`. **Measured
  here: no E5001 anywhere in a 6,806-case run**, and `generic-hash-table-regalloc/…witness-dispatch-inside-a-pressured-loop`
  — EC13's red case — stays green.

**arm64 took the ADDRESS half and NOT the memory half, and the asymmetry is the row's one correction
to its own premise.** `ADD Xd, Xn, Xm, LSL #k` is exactly `lea [base + index*2^k]` and arm64 gains
MORE from it than x64 does — AArch64 has no multiply-immediate at all, so `lowerMulImm` was
materialising the stride into the IP scratch and following with a register `MUL`, three instructions
where this is one. But `LDR Xt, [Xn, Xm, LSL #3]` is **NOT** `loadRegBaseIndexScale`'s equivalent: its
`S` bit selects a shift of 0 or exactly log2(access size) — there is no `LSL #1`/`#2` for a 64-bit
access — and the register-offset form carries **no displacement field at all**, over three more
encodings (LDR Xt / LDR Dt / LDRB Wt and their stores). ⇒ the isel asks for `foldsIntoMemoryOps:
false` on that lane. ⚠ **UNVERIFIED: this host cannot run the arm64 suite, and that lane's goldens
will move when one does.**

**Left open**: the arm64 memory fold above; a CROSS-BLOCK chain (bounded by the pressure argument, not
by soundness); an ADDRESS with two memory readers, where folding into both would delete the `add`
outright; a non-zero DISPLACEMENT, which the op carries and nothing currently produces (every element
access loads at `+0`); and the BYTE-width indexed forms, which the shared width dispatcher gives for
free and which no lowering can currently reach — a one-byte element has stride 1, so `index * 1` is
folded to `index` by `foldConstOperands`' identity rule and there is no multiply left to absorb.

#### `EC2` · The managed field read retained and released across a call in the same statement

⛔⛔ **THE ROW'S STATED CAUSE WAS FALSIFIED BEFORE ANY CODE WAS WRITTEN, AND RE-MEASURING IS WHAT FOUND
THE REAL ONE.** The row named W41's mark — *"`markRebindableSlotRead` marks every managed field read, and
`declareInitializedBinding` / **the argument door** then PROMOTE it"*. **MEASURED 2026-08-29: there is no
argument door.** `rebindableSlotReads` has exactly ONE reader in the whole compiler
(`Parser.declareInitializedBinding`), and `emitFieldLoad`'s own header already states the rule the row was
asking for — *"a receiver load, an argument, a chain hop are all transient and owe nothing"*. A program's
`items.push(v)` / `items.clear()` on a struct's own managed field emits **one `loadRegBaseDisp` and the
call**, with no retain and no release. The row's optimisation, at the door the row named, was already the
behaviour.

⚠ **AND ITS ACCEPTANCE MEASURES SOMETHING ELSE.** *"`__mm_retain` + `__managed_decref` < 2% of stage-2's
samples"* reads **1.39% + 2.54% = 3.93%** today (the row cited 3.2% + 4.0%), and neither symbol is this
row's subject: `__managed_decref` is the array DESTRUCTOR, **11,070 static sites**, one per scope-exit drop,
and the 886 `__mm_retain` sites are `handleEscapingBorrowFeed`'s and `promoteBorrowedToOwned`'s real
co-ownership. **The threshold is unreachable by any correct change** and was not adopted as the gate.

⭐⭐ **BUT THE ROW'S OWN EXHIBIT WAS ALIVE, EXACTLY AS WRITTEN — `Array.clear` UNDER shv2 REALLY IS
`__mm_retain → __managed_clear → __managed_decref`** — and the cause is a different door:
`Parser.fusedManagedMemberTakesTheRecord`, a **six-name whitelist** deciding whether a bare
`managed.<member>(…)` inside a fused wrapper's own body (`String`, `Character`, `Array`, `Vector`) hands the
member's runtime entry the RECEIVER'S OWN RECORD, or goes through the `managed`-as-a-VALUE door, which since
W157 pays an `__mm_retain`/`__managed_decref` pair around the call. It listed `length`, `byteAt`, `setByte`,
`append`, `toCString`, `makeCharFromBytes`. The **eleven** others a container body spells — `set`, `get`,
`setLength`, `capacity`, `grow`, `slice`, `remove`, `clear`, `fill`, `shiftRight`, `elementSize`, **39 call
sites across `stdlib/`** — paid the pair. The whitelist's own header had already filed the fix and priced
it: *"What listing `slice` here would still save is one `__mm_incref`/`__managed_decref` pair per call, which
is a measurement, not a correctness argument, and belongs to whoever takes it."*

✅ **LANDED 2026-08-29 AS A DELETION.** `fusedManagedMemberTakesTheRecord` is gone and the door's condition
is `binding.isSelfField and self.memberCallFollows(self.pos + 1)` — **which is EC2's rule, stated at the door
that actually has it**: the only consumer is a call in the same statement, and the receiver is `self`, a
parameter the CALLER owns for the whole frame, so nothing a buffer entry does can free it.

**The three arguments the list still carried are each closed somewhere else, and the first is decisive:**

- **THE ENTRY RECEIVES THE SAME POINTER EITHER WAY**, so the list never decided which record an entry got —
  only whether a refcount pair was paid on the way to it. An `Array`'s buffer IS the `Array` (W57), and W157
  made `__str_bytes_view` an `__mm_incref` that hands the receiver's own record straight back (MEASURED on
  this tree: its whole body is a `capacity@16 == -4` test, an incref, and `mov r8, rcx`).
- **THE INSTANCE** — `append`'s and `setByte`'s parse-time admission read the receiver's `giid`, and the door
  used to hand every receiver `internSynthesizedByteInstance()`. W194 replaced that with
  `bufferSurfaceInstanceOf`, which is the value door's OWN answer, so the two cannot disagree.
- **THE SEVENTH SLOT** — `makeCharFromBytes` reads `@48` and must never be handed a plain 48-byte buffer
  record. `dispatchArrayMethod` refuses that on the RECEIVER's TAG rather than on the route it came by
  (`tagIsByteRecord`); the deleted header already recorded that as structural rather than reachability.

**MEASURED — A REAL CONTROL, both compilers built in ONE session and the SAME source compiled by both, so
the only difference is the codegen (`EC19`'s trap: a before/after across a source change is not an A/B):**

| | control | with the row | |
|---|---|---|---|
| stage-2 self-compile, 4 runs interleaved A B A B | 79,625 / 79,738 / 79,512 / 79,648 ms | **78,050 / 78,007 / 77,912 / 77,688 ms** | **−2.16%** |
| emitted compiler binary | 9,281,678 B | **9,281,166 B** | −512 B |
| `__mm_retain`, stage-2 sample profile | 1.39% | **1.02%** | |
| `__managed_decref`, same profile | 2.54% | **1.96%** | |
| `Array.clear` — the row's exhibit | 14 emitted ops | **7** | no frame, no saved register |

Every experiment run beats every control run by ≥ 1,462 ms, and the two arms are separated by about 4× their
own spread. **And the third stage settles the correctness question harder than a fixpoint does**: the
experiment-emitted compiler compiles the whole compiler to a file BYTE-IDENTICAL to the control-emitted
one's output — 9.28 MB, `cmp` clean.

⚠⚠ **`scripts/emitted-code-count.py` AND THE `--emit-ir` CENSUS CAN SEE ALMOST NONE OF THIS, AND THAT IS
THE FOURTH TIME THIS WORKSTREAM'S INSTRUMENTS COULD NOT EXPRESS A ROW.** The subject is 39 call sites in
`stdlib/`, and since X11 `--emit-ir` writes only the user's functions: the self-compile dump moves by
**−6 `__managed_decref` and −41 `movRegReg` out of 1,287,634 ops**, because the compiler's own source spells
only six of them. The binary size and the timed A/B are the instruments that can see it.

**GATES**: x64-windows **6,931 passed, 0 failed, exit 0** (6,929 + 2 new) and wasm32-wasi **6,415 passed, 0
failed, exit 0** (6,413 + 2); **21 goldens re-minted** from ONE unfiltered run and re-verified at zero drift,
2 added. **Self-host fixpoint: stage-2 == stage-3, BYTE-IDENTICAL (9,280,084 bytes).** E3070 was re-probed
on the changed route (an element borrowed out of the buffer, then `managed.clear()` under it) and gives the
identical diagnostic at the identical line and column before and after.

**Cases added** to `specs-shv2/array-declared-record.md`, 2, both sabotage-verified against the committed
fragments — the sabotage is **restoring the six-name whitelist**:

| case | what the sabotage does |
|---|---|
| `a-buffer-member-off-the-old-six-takes-the-record` (the gate) | **10 of the spec's 37 fragments move, this one among them** — its frame grows back the callee-saved register the `__mm_retain`'s result had to live in across `__managed_clear`. A `String` element and a pinned exit code, so a lost reference is a double free and a gained one is 101. |
| `the-value-spelling-of-the-same-field-keeps-its-reference` (the control) | **byte-identical** — `return managed` has no consumer in its own statement, so `bufferSurfaceOf` still co-owns it. This is what says the two doors were separated, not merged. |

⭐⭐ **WHAT THE RE-MEASUREMENT FOUND AND THIS ROW DID *NOT* TAKE — THE BIGGER SHAPE, AND IT IS A DIFFERENT
DOOR AGAIN.** A census of the self-compile's emitted x64 (1,287,634 ops, 150,851 `callDirect`, **32,725 of
them refcount = 21.7%**, independently confirming `EC19`'s 21.2%) finds **825 exact EC2-shaped brackets** —
an acquire, exactly ONE other call, then a release of the SAME register:

```
loadRegBaseDisp rbx, [rbx + 8]   ; a union PAYLOAD, bound by a match arm
mov  rcx, rbx ; call __mm_incref
mov  rcx, r12 ; mov rdx, rbx ; call pushDef
mov  rcx, rbx ; call __mm_decref
```

**801 of the 825 acquire with `__mm_incref`, and the producer is `Parser.retainBorrowedPayload` (D1b)** — a
managed payload bound out of a BORROWED union in a `match` arm, retained unconditionally and dropped at the
arm's own exit. **197 of them are in `targetOpOperands` alone**, the function `EC19` names as 46% copies.
That function's header states the design outright and gives its reason: shv2's release is STRUCTURAL, so
*"an unconditional acquire is unconditionally balanced and needs no analysis at all"*, where v1 pays a
whole-function dataflow pass for the same question. ⇒ **The remaining EC2 shape is a PAYLOAD-BINDING rule,
not a field-read rule**, worth 4 ops × 825 sites plus whatever `EC19`'s induced-copy argument adds on top.
Filed here, not taken.

#### `EC7` *(already on the board, ⬜ FREE)*

A `for x in a upto b` counter with constant bounds denotes no range, so every ranged parameter it
reaches is guarded at runtime — and after `EC5` that guard is *copied into the caller*.

#### `EC17` · A second inline round, or swap the two inliners

Filed by `EC5` and never taken: `Array.isEmpty` (209 sites), `Parser.advance` (106),
`String.byteLength` (92) stay calls because their one body call is `__managed_count`, which
`inlineManagedPrimitives` rewrites **one pass later**. **407 sites**, for either a second round or a
reorder. Cheapest row on this list.

✅ **LANDED 2026-08-29 AS THE REORDER.** `buildLoweringPasses` now pushes `inlineManagedPrimitives`
before `inlineLeaves`, and **that is the entire change**: no pass added, no rule weakened, no budget
moved, one round still one round. The 407 sites are **0**.

⭐⭐ **WHY THE REORDER AND NOT A SECOND ROUND — THEY ARE NOT ALTERNATIVES OF EQUAL REACH.** One round
*after* the managed rewrite sees everything a second round would see **except the cascade**, and the
cascade is the part this row did not ask for. What a second round would additionally do, each needing
its own suppression machinery:

- **It re-expands its own slow arms.** `inlineLeaves`' `__il_slow` block holds *the very call the splice
  moved* — that is how the panic rule keeps a callee's frame — so a later round inlines it again: one
  more copy of a panicking leaf's body at every such site, and the innermost arm still calls. Pure growth,
  no call removed. (`InlineLeaves.run`'s header already names this: *"the SLOW arm holds the very call
  this splice moved, which re-inlining would loop on"*; within one round the snapshot bound prevents it,
  across two rounds nothing does.)
- **It opens a cascade level.** Eligibility recomputed on an already-spliced module admits a caller that
  became call-free BY being inlined into — exactly what `LeafPlan`'s *"ONE ROUND, NO CASCADE"* rule
  refuses on purpose, and what `inline-leaves.a-leaf-called-from-a-leaf-is-not-cascaded` pins.

⭐ **AND THE REORDER CANNOT COST `inlineLeaves` A SINGLE CALLEE — A PROOF, NOT A MEASUREMENT.**
`inlineManagedPrimitives` rewrites only bodies that hold a `__managed_*` CALL; a leaf holds **no call by
rule** (`LeafOpRole.calling` refuses the callee outright). So no body it can reach is one the leaf inliner
would have accepted, and the eligible set can only grow. MEASURED anyway, because a proof about two
passes is worth checking: eligible callees **469 → 530**, sites inlined **4,138 → 4,921**, panic-rule
refusals unchanged at **7 sites / 6 callees**, self-calls unchanged at **14** — and
`inlineManagedPrimitives`' own count is unchanged **to the digit at 6,032 sites across 9,494 functions**,
which is the other half of the same proof (leaf splicing never duplicated a managed primitive, in either
order).

⚠⚠ **`EC15`'s DURABILITY ARGUMENT WAS CHECKED AND IT GOT SHORTER, NOT LONGER.**
`Project.stdOpElementStrides` keys on the append-only `module.ops` INDEX, justified by *"a leaf holds no
call BY RULE, so `inlineLeaves` can neither clone nor re-issue a managed primitive."* The reorder removes
`inlineLeaves` from between the WRITE (`LowerMaxonToStd`) and the READ (`inlineManagedPrimitives`)
altogether — `insertRangeChecks` is now the ONLY pass in between, and it appends ops and moves `opRefs`,
never ops. The leaf rule is therefore no longer that key's argument at all; it is now the argument for the
REORDER, in the other direction. Both comments were rewritten to say which.

⚠ And the second half of the question checks out too: `EC1`/`EC15`'s expansion **does** leave the original
call on a slow arm, so a body carrying one is a body holding a CALL — which is exactly why `inlineLeaves`
still refuses it, and why nothing had to be added to make it. That is pinned by a control that is the SAME
SOURCE with one type changed: `a-loop-whose-element-access-keeps-a-slow-arm-is-not-a-leaf` iterates an
`Array with Byte` (stamp 1 ⇒ `runtimeFork` ⇒ both width arms AND a slow arm holding
`__managed_get_unchecked`), and `totalBytes` is **still called**, where the `Array with Integer` twin is
spliced whole.

**THE CENSUS — the self-compile, every `x64.callDirect` in 10,517 functions:**

| callee | before | after |
|---|---|---|
| `Array.isEmpty` | 209 | **0** |
| `Parser.advance` | 106 | **0** |
| `String.byteLength` | 92 | **0** |
| `IrModule.funcCount` | 38 | **0** |
| `StructLayout.sizeBytes` | 14 | **0** |
| `projectHasErrors` | 13 | **0** |
| `IrModule.opCount` | 12 | **0** |
| ~100 further `count` / `size` / predicate accessors | 157 | **0** |
| **TOTAL `callDirect`** | **149,271** | **148,630 (−641)** |

`EC5`'s three are 407 of it; the other **234** are the same shape it never enumerated — a body whose one
call is `__managed_count`. **Every mover goes to ZERO and not one callee gained a site**, which is what a
reorder should look like.

**`scripts/emitted-code-count.py`**, committed corpus, before → after. ⭐ **A `call-direct` COLUMN WAS ADDED
FOR THIS ROW** — the two inlining passes are graded on calls discharged and no column counted them — and
`temp/codegen-probe/leaf.maxon` joined the corpus as the probe that exhibits the defect, so the totals
below are re-baselined against both:

| | ops | call-direct |
|---|---|---|
| nbody | 8,602 → **8,586** | 785 → **777** |
| fannkuch-redux | 1,708 → 1,708 | 111 → 111 |
| arr | 100 → **93** | 15 → **14** |
| cse2 / cse3 / probe | unchanged | unchanged |
| leaf | 69 → **56** | 6 → **5** |
| TOTAL | 10,627 → **10,591** | 931 → **921** |

`jmp`, `jmp→next`, `jmponly-blocks`, `imul-imm`, `imul-pow2`, `idiv`, `mgd-call` and `im-blocks` are
byte-identical, as they must be: this row selects no instruction differently and expands no element
access. Which calls went: nbody −5 `Array.isEmpty` and −3 `String.byteLength`; `leaf` −1 `Array.isEmpty`;
`arr` −1 **`sum`** — the whole loop function, per the anchor note above; fannkuch **nothing at all**, and
its compiled binary is BYTE-IDENTICAL under both compilers.

⚠ **`ops` FELL ON THIS CORPUS AND THAT IS NOT THE GENERAL CASE.** Splicing a two-op leaf and deleting a
call, a frame, an argument move and a `ret` is a net LOSS of instructions wherever the callee then dies
entirely. Where it does not — the self-compile, where most inlined callees keep other callers — the same
change reads **+1,190 static x64 ops (+0.09%)** and **+5,606 binary bytes (+0.06%)**. Inlining ADDS static
ops while removing calls, exactly as the row's brief warned; the corpus simply happens to be on the other
side of it.

⭐⭐ **THE TIMED A/B — SMALL, CONSISTENT, AND EXACTLY ZERO ON BOTH BENCHMARK PROGRAMS.** Two stage-2
compilers built from IDENTICAL sources by the pre-swap and post-swap compilers (so the LOGIC is the same
in both binaries and only the emitted code differs), each self-compiling `maxon-shv2`, interleaved
A B A B A B on one box:

| | run 1 | run 2 | run 3 | min |
|---|---|---|---|---|
| pre | 77.88 s | 77.83 s | 77.78 s | 77.78 |
| post | **77.46 s** | **77.70 s** | **77.15 s** | **77.15** |

**−0.5%**, and the two arms' ranges do not overlap — post's SLOWEST run (77.70) beats pre's FASTEST
(77.78). It is about the size the static census predicts: 641 of 149,271 emitted calls is 0.43%.

⛔ **On the workstream's two benchmark programs it is ZERO, and that is the honest reading.**
`fannkuch-redux`'s binary is **BYTE-IDENTICAL** under both compilers — it holds none of these accessors,
so there is nothing to time. `nbody` runs **10.30 / 10.13 / 10.12 s** before and **10.33 / 10.15 / 10.13 s**
after, printing the same answer: no difference, because its 8 removed calls are in setup and printing and
not in its integration loop. ⇒ **this row's win lives in programs that call small accessors inside hot
code, and the compiler is the one such program in the tree.** `EC14` reported the first zero of this
workstream; this is the second, and the END-TO-END section's rule holds — an instruction count is a
hypothesis, and only a program that exercises the shape can settle it.

⭐ **WHAT IT COSTS THE COMPILER, ATTRIBUTED — AND IT IS NEGATIVE.** `--metrics` on both compilers
self-compiling `maxon-shv2` (the allocation columns are exact and depend only on the LOGIC, so they read
the reorder cleanly; the CPU column mixes in the emitted-code win and is not used here):
`phase:inlineLeaves` allocations **+21.6%** (677,058 → 823,371 — proportional to the 19% more splices),
`elimTrivialBlockArgs` +11.0%, `pruneDeadBlockArgs` +3.7%, `ssaDestruction` +3.0%; paid back by
`regalloc` **−0.65%** and `regalloc:splitting` **−0.84%** (the calls that bounded live ranges are gone)
and `inlineManagedPrimitives` **−1.0%** (it now walks unspliced blocks). **Whole-compile allocations
−0.30%**: the compiler allocates LESS overall while its inliner does a fifth more work.

⚠ **NO STASHED `scale-test` CONTROL WAS TAKEN AND THE LOGGED ROW SAYS SO.** The stale-binary guard
refuses to run `scale-test` on a deliberately-old control binary — correctly, and it is not a guard to
work around — so `docs/optimization-log.md`'s EC17 row is a TREND STEP against EC14's and not an
attributed A/B. The `--metrics` reading above is the attribution, on a far realer input than the ladder.

**GATES**: x64-windows **6,822 passed, 0 failed, exit 0** (6,818 + 4 new) and wasm32-wasi **6,361
passed, 0 failed, exit 0** (6,358 + 3 — the panic-trace case is x64-only by its own `targets:` marker);
**zero `E5001` in either run**, and `EC13`'s knife-edge case
`generic-hash-table-regalloc/…witness-dispatch-inside-a-pressured-loop` is not merely green: **its
golden is BYTE-IDENTICAL** — this row put no extra pressure on the case built to sit exactly at the
register pool. (Its sibling `…rehash-loop-forwards-hidden-parameters` did move, and is one of the
1,008.) **1,008 goldens re-minted** from ONE unfiltered run and
re-verified at zero drift, 4 added. **Self-host fixpoint: stage-2 == stage-3, BYTE-IDENTICAL**
(9,004,190 bytes). The **89 `Stack trace:` blocks across 19 spec files are byte-identical**:
the panic rule is untouched by the reorder, and a moved trace would have been a FAILURE rather than a
golden note.

**Cases added** to `specs-shv2/inline-leaves.md`, 4:
`an-accessor-that-becomes-a-leaf-after-the-managed-rewrite-is-inlined` (the gate — `Array.isEmpty` is
spliced, and the fragment holds no `callDirect Array.isEmpty`);
`a-whole-loop-over-an-array-becomes-a-leaf` (`total` is spliced into `main` and does not survive
dead-function elimination at all);
`the-panic-rule-holds-when-the-argument-is-an-inlined-element` (the guard tests a value
`inlineManagedPrimitives` produced, and the trace still reads `in clampPct / in main / in mrt_start`);
and the byte-array control above.

**Left open**: the CASCADE a second round would buy (a caller that becomes call-free by being inlined
into) — unbought, and now the only thing a second round would add; the **24-op budget**, unchanged per this
row's scope and now refusing **529 sites / 60 callees** where it refused 520 / 53, of which 378 sites /
30 callees a budget of 32 would admit (the population it is applied to grew, so the budget's price grew
with it — lowering or raising it still owes its own CPU A/B); and `EC15`'s **byte arm**, which would make
the `ByteArray` control's loop call-free too and turn that refusal into an inline.

#### `EC18` · Strength reduction

`mul` by a power of two → `shl`/`lea`; `div`/`mod` by a constant → the magic-number multiply.
**Neither** the bootstrap nor v1 has this, so there is no reference to read.

⛔ **THE `mul` HALF WAS ALREADY GONE WHEN THIS ROW WAS TAKEN, AND THE ROW'S OWN WARNING IS WHY IT WAS
CHECKED.** `EC16` folded the element-index scaling into an addressing mode, and `imul`-by-immediate
fell **66 → 6 corpus-wide**; 36 of nbody's 40 had been `×8` element scaling. So this row is
**DIVISION**, and the `shl` half is left where `EC16` left it — six multiplies in the whole corpus,
with no site shown to pay.

⚠⚠ **AND NEITHER BENCHMARK CAN SEE THE DIVISION HALF EITHER.** Every `idiv` in nbody was attributed
before the row was designed: `grownCapacity`'s `/4` (array growth, amortized), two in
`__bigDivModSmall` whose divisor is a REGISTER and is not reducible at all, and the rest in the float
formatting stdlib. The `idiv`-by-constant sites a program actually pays are **`__decimalWidthOf` and
`__appendDecimalGroup` (four per number formatted)**, **`__int_to_string` (three per integer
printed)** and **`graphemeBreakProperty`'s `mod 28` (one per grapheme)**. `temp/codegen-probe/fmt.maxon`
— 300,000 floats formatted — was committed as the probe for exactly that reason and is reproduced at
the foot of this document.

✅ **LANDED 2026-08-29** as `maxon-shv2/Compiler/IR/Std/StrengthReduceDivision.maxon`, scheduled in
`buildLoweringPasses` between `foldConstants` and `foldConstOperands`, and run a SECOND time by
`Compiler.compileToCodeResult` over the appended runtime band.

⭐⭐ **IT IS A Std PASS AND NOT AN ISEL, AND THAT IS FORCED RATHER THAN CHOSEN — WHICH MAKES IT THE
FIRST ROW OF THIS WORKSTREAM THAT COULD NOT GO WHERE `EC16` AND `EC19` WENT.** The sequence needs four
or five intermediate values and **the x64 isel cannot mint a vreg**: every `TargetVReg.virtual(id)`
names a Std `ValueId`, the parser owns that value space, and the register-class column is derived from
the **Std** module — so a backend-minted vreg would be filled `gpr` by accident rather than by
decision. `TargetDialect.absF64RegReg` and `StdToX64Conversion.materializeFloatEquality` had both
already recorded that wall from the other side; this row is the first to be stopped by it.

**WHAT WAS ADDED, AND IT IS ONE OPCODE AND ONE INSTRUCTION.** `StdBinOpcode.mulHighSigned` (the high
64 bits of the 128-bit signed product) and `TargetOp.imulHighReg` (x64's ONE-operand group-3
`imul r/m64`, `REX.W F7 /5`), whose register model is `divideReg`'s to the digit — implicit RAX in,
implicit RAX+RDX out, one explicit operand — so `lowerMulHighSigned` is `lowerDivMod`'s three-op shape
and the twenty-odd exhaustive `TargetOp` matches each gained one arm.

⚠ **THE TARGET GATE IS THE ROW'S ONE PIECE OF SCOPE, AND IT IS THE HARDWARE'S ON ONE LANE AND THIS
COMPILER'S ON THE OTHER.** `strengthReduceDivision` asks `targetLowersMulHighSigned` and rewrites
NOTHING where the answer is `false`: **wasm32 has no `i64.mul_high_s` at all** (four 32×32 products
and their carries, ~20 instructions against the one `i64.div_s` it would replace), and **arm64 HAS the
instruction — `SMULH Xd, Xn, Xm`, a plain three-address form strictly nicer than x64's — and has no
`TargetOp` for it.** So no arm64 or wasm golden moves for this row, and adding that op is what turns
that lane on. Filed, not taken: this is the most arithmetically dangerous row of the workstream and it
was not worth doubling on a lane this host cannot execute.

**THE SEQUENCES, AND THE SIGN CORRECTION IS THE WHOLE OF WHY THEY ARE NOT ONE-LINERS.** Signed
division TRUNCATES TOWARD ZERO and neither a shift nor a magic multiply does:

```
x / 2^k   sar sgn,x,63 ; shr bias,sgn,64-k ; lea t,[x+bias] ; sar q,t,k      (4 ops)
x /u 2^k  shr q,x,k                                                          (1 op)
x modu 2^k  and r,x,2^k-1                                                    (1 op)
x / K     mov rax,M ; imul x ; [lea h,[rdx+x]] ; [sar h,s] ; shr s,h,63 ; lea q,[h+s]
x mod K   <the quotient> ; imul p,q,|K| ; sub r,x,p
```

⇒ **THE `ops` COLUMN GOES UP AND THAT IS THE ROW WORKING.** A five-op sequence replaces a four-op
`idiv` that costs 20–40 cycles; the instrument counts instructions and cannot see cycles. Corpus,
before → after — and note the `idiv` column was WIDENED in the same commit, because it had only ever
counted the SIGNED `idivReg` and missed all ten of the corpus's unsigned `divReg`s:

| | ops | imul-imm | imul-pow2 | idiv | mov |
|---|---|---|---|---|---|
| nbody | 8,582 → **8,587** | 5 → **7** | 2 → 2 | 13 → **2** | 1,464 → **1,463** |
| fannkuch-redux | 1,700 → **1,705** | 0 → **1** | 0 → **1** | 3 → **2** | 211 → **213** |
| fmt (the probe) | 7,811 → **7,816** | 5 → **7** | 2 → 2 | 13 → **2** | 1,353 → **1,352** |
| probe | 59 → **64** | 0 → **1** | 0 → 0 | 3 → **0** | 11 → **9** |
| TOTAL | 18,390 → **18,410** | 11 → **17** | 4 → **5** | **32 → 6** | 3,085 → **3,083** |

**Every divide left in the corpus has a REGISTER divisor and is not reducible at all**: two in
`__bigDivModSmall` (nbody and fmt each) and two in fannkuch's `getPermutation`.

⭐⭐ **HOW THE MAGIC NUMBERS ARE DERIVED, AND HOW A READER CHECKS THEM — because a pasted table of
constants would be a table nobody can check.** `deriveSignedMagic` is Granlund & Montgomery's
algorithm (PLDI 1994; Hacker's Delight fig. 10-1), widened to 64 bits. It seeds `floor(2^63/d)` and
`floor(2^63/anc)` and then RAISES `p` one bit at a time — one doubling with a remainder correction,
no division after the seeds — until the loop's own exit condition holds. **That condition IS the proof
obligation**, so the first `p` that satisfies it gives the smallest exact multiplier; there is nothing
to trust beyond the transcription.

⚠⚠ **ONE VALUE IS ALLOWED TO WRAP AND EVERY OTHER ONE MUST NOT, AND THAT IS WHAT MAKES A u64
ALGORITHM EXPRESSIBLE IN A LANGUAGE WITH NO u64 ARITHMETIC.** shv2's `ParsedInt` is signed 64-bit and
its comparisons are signed, so a value above `2^63` would compare as negative and the loop would take
the wrong branch. `anc`, `r1`, `r2` and `delta` are remainders below `2^63` and are exact — their
doublings are written `r >= m - r` / `r - (m - r)` so no intermediate leaves the range. `q1` must be
exact because the exit condition COMPARES it, and it is tested against `i64.max/2` before each
doubling: MEASURED, it first exceeds i64 for divisors around **2^62.5** and for none below **2^61**,
so that bound is a FIFTH refusal that costs nothing real. `q2` is the only term allowed to wrap, and
its wrapped value IS the answer — a 64-bit multiplier whose top bit may legitimately be set, which is
what the `+ dividend` fixup exists for.

⭐ **AND THE DIVISOR IS ALWAYS TAKEN POSITIVE, WHICH IS A DELIBERATE DEPARTURE FROM THE PUBLISHED
ALGORITHM.** It handles `d < 0` by biasing `anc` and negating `M`; working in `|K|` and negating the
QUOTIENT is exact (truncating division is odd, and `|x / |K||` is at most `2^62` so the negation
cannot overflow) and removes the one case where `anc` reaches `2^63` and stops fitting — a negative
divisor whose magnitude divides `2^63 + 1`, of which `x / -3` is the smallest.

**A reader checks it two ways**: against that exit condition, and by running the spec — which is a
DIFFERENTIAL test, not a table.

⭐⭐ **`specs-shv2/strength-reduction.md` USES THE HARDWARE `idiv` AS ITS ORACLE.** Each case divides
by the LITERAL (reduced) and by a **runtime value of the same magnitude** (not reduced — its ranged
type `int(2 to 1000000)` proves it non-zero, so `Parser.emitDivOrMod` emits a bare `idiv` with no
guard and no `try`), and asserts the two agree. Twelve edge dividends — `i64.min`, `i64.max`, `0`,
`±1`, `±2`, `±7`, `-2^62`, `±10^9+7` — against eleven divisors chosen so every arm of both sequences
is taken. A wrong magic is a wrong exit code, not a moved golden.

⚠ **THE REFERENCE HAD TO BE A REAL CALL, AND THAT IS THE [[green-case-proved-nothing-sabotage]]
SHAPE.** A reference small enough for `inlineLeaves` to splice with its literal argument would have
its divisor folded to a constant and be REDUCED TOO — the case would then compare a reduction against
itself and pass however wrong both were. The ranged-parameter guard keeps the references out of the
budget; the minted fragments show a `callDirect` at every one of the gate's seventeen reference sites
and an `idivReg` inside each of its four reference bodies.

⭐⭐ **THE SABOTAGES WERE RUN, NOT DESCRIBED — seven of them, and the two that stayed GREEN are the
ones worth reading.**

| sabotage | what happened |
|---|---|
| drop the power-of-two sign bias | **RED, exit 1** on the gate AND on the truncation case — a wrong answer for every negative dividend |
| drop the `+ dividend` fixup for a top-bit-set magic | **RED, exit 10** — the `/15` arm, which is the arm added for it |
| make the floor→truncate correction a `mul` instead of an `add` | **RED, exit 5**, and the range-check control flips too |
| shift the unsigned quotient ARITHMETICALLY | **RED, exit 1** on the unsigned case only |
| HALVE `anc` in `deriveSignedMagic` | **RED, exit 5** — but ONLY on the seventh case, which exists because of this |
| invert the do-while sense, or set `anc` to `i64.max` | **GREEN, and correctly so**: both make the refinement stop LATER, and a later `p` is still an EXACT multiplier (the loop finds the smallest, not the only one), so either the magic merely changes or `q1` trips its bound and the site declines. Only an EARLY exit is a wrong answer. |

⛔⛔ **AND THE SEVENTH CASE EXISTS BECAUSE THE FIRST SIX COULD NOT SEE A MIS-DERIVED MAGIC AT ALL.**
With `anc` halved — a provably too-coarse reciprocal — the gate's twelve dividends against eleven
divisors were **six cases, 0 failed**. A fixed-point reciprocal that is slightly wrong is right for
almost every dividend and wrong only near `anc` itself, which is the value the derivation's exit
condition is stated in terms of and which no fixed dividend list contains. The seventh case COMPUTES
`anc` per divisor from a runtime `mod` the compiler cannot fold, and reddens at exit 5.
⇒ **a differential test against the hardware is only as good as its dividends, and the dividend that
matters for a magic number is not an edge of the TYPE — it is an edge of the DIVISOR.**

⛔⛔ **AND REVIEW FOUND A LIVE WRONG ANSWER THAT NO GATE COULD SEE.** `magnitude` is `|K|` read as a
SIGNED number, and for an UNSIGNED divide a `K` with its top bit set is not a small negative number
but a value of at least `2^63`. `x /u 18446744073709551600` is `0` for every dividend below it; its
signed magnitude is `16`, a power of two, which the first cut answered with `shrLogical 4`. MEASURED
on a program that reaches it from source — a `let` typed `int(0 to u64.max)` whose folded value has
wrapped past `i64.max` — **`9223372036854776807 / 18446744073709551600` printed `576460752303423550`
where `0` is correct.** Refused now, and pinned by
`an-unsigned-divisor-above-the-signed-range-is-refused`. ⇒ **the sign of a `ParsedInt` divisor means
two different things to the two signednesses, and it is the ONE refusal here that prevents a wrong
answer rather than a deleted trap.**

⭐⭐ **THE TIMED A/B — AND IT IS THE SECOND NON-ZERO RESULT THIS WORKSTREAM HAS HAD, ON THE PROGRAM
THE ROW WAS DESIGNED AROUND.** Two compilers from ONE tree differing in the one line that turns the
pass off (`targetLowersMulHighSigned`'s x64 arm), built in one session, each compiling the same three
programs, runs interleaved on one box. **All three print byte-identical answers and all three binaries
genuinely differ** (60,497 / 45,837 / 10,930 bytes apart — checked, because "same size" is not "same
code" and the PE file size is 512-byte aligned):

| program | control | with the row | |
|---|---|---|---|
| `temp/codegen-probe/fmt.maxon` (the probe) | 3,561 / 3,557 / 3,566 ms | **3,167 / 3,194 / 3,171 ms** | **−11.0%** |
| `examples/fannkuch-redux` | 10,042 / 10,024 / 10,040 ms | **9,787 / 9,848 / 9,797 ms** | **−2.3%** |
| `examples/nbody` | 10,168 / 10,114 / 10,133 ms | 10,116 / 10,122 / 10,101 ms | **0** |

Both non-zero arms are non-overlapping (fmt's worst run beats the control's best by 363 ms;
fannkuch's by 176 ms). **nbody's zero was PREDICTED and is reported as measured** — its divisions are
`grownCapacity`'s amortized `/4` and two `__bigDivModSmall` divides whose divisor is a register.

⭐ **AND FANNKUCH'S −2.3% IS ATTRIBUTED RATHER THAN CREDITED.** It has no division in `getPermutation`
that this row can touch — its two survive — and the one site that WAS reduced is `main`'s `mod 2`, the
per-permutation parity test that decides the sign of the checksum. That is a ~30-cycle `idiv` per
permutation replaced by four ALU ops, in the outer loop of the benchmark, which is why a program with
"no division in its hot loop" moved at all.

⚠ **THE SCALE LADDER AGREES WITH ALL THREE, WHICH IS WORTH SAYING BECAUSE `EC19`'s DID NOT.** Against
the same kind of control, its binary swapped in and the ladder re-run: emitted **`codeBytes` is
+112 BYTES AT EVERY RUNG** — a CONSTANT, i.e. one program's worth of reduced stdlib sequences, so it
reads +0.084% at rung 0 and **+0.004% at rung 5** as the corpus grows around it. The compile costs
**+0.19% → +0.15% of allocations** and +0.16% of bytes (also falling), and CPU sits between −0.45% and
+0.81%, inside the noise band. `phase:strengthReduceDivision` + `phase:strengthReduceEmittedDivision`
together are **0.148% / 0.157% / 0.162%** of the rung-5 compile and are **LINEAR** — ×1.90 allocs,
×2.02 bytes, ×2.07 CPU across the last doubling, converging on ×2 from below as their constant term
washes out. **No bend, in any column.** ⇒ this row does not compound with `EC19`'s open ladder
disagreement: that one is `regalloc:splitting` reacting to a colouring change, and this row's ladder
delta is a flat constant that never enters the allocator's pressure at all.

**GATES**: x64-windows **6,834 passed, 0 failed, exit 0** (6,827 + 7 new) and wasm32-wasi **6,372
passed, 0 failed, exit 0** (6,366 + 6 — the range-check control is `targets: x64-windows, x64-linux`);
**zero `E5001` in either run**. **772 goldens re-minted** from ONE unfiltered run and re-verified at
zero drift (6,834 compared, 0 differ), 7 added — and **every one of the 772 is on the x64-windows
lane**, which is the target gate reading itself back: not one arm64 or wasm32 fragment moved.
**Self-host fixpoint: stage-2 == stage-3, BYTE-IDENTICAL (9,033,004 bytes)** — the gate `EC17` found
the suite structurally cannot give, and the one that matters most for a row that rewrites arithmetic:
the compiler compiles itself with its own reduced divisions and lands on the same bytes.

**Cases added** to the new `specs-shv2/strength-reduction.md`, 7, every one of them a DIFFERENTIAL
test against the hardware `idiv` rather than a table of expected quotients:

| case | what it pins |
|---|---|
| `a-constant-divisor-gives-the-same-answer-as-a-runtime-one` | THE GATE — 12 edge dividends × 11 divisors, each answered by the reduced literal and by a real `idiv` on a value the compiler cannot fold |
| `the-hardest-dividend-for-a-divisor-is-the-one-the-derivation-is-ABOUT` | the DERIVATION, at `anc` and its neighbours, computed per divisor at run time |
| `a-signed-power-of-two-truncates-toward-zero` | the bias that makes `-7 / 2` be `-3` and not `-4` |
| `an-unsigned-power-of-two-reads-the-whole-bit-pattern` | the logical shift, over three dividends past `i64.max` |
| `an-unsigned-divisor-above-the-signed-range-is-refused` | the live wrong answer review found |
| `the-refused-divisors-keep-their-answers` | `i64.min mod -1`, `x / 1`, `x mod 1`, `x / i64.min` |
| `a-reduced-division-still-meets-its-range-check` | that the rewrite keeps the division's RESULT VALUE ID, which the guard `insertRangeChecks` emitted names |

**Left open**: the `mul` → `shl` half (`EC16` left six multiplies in the whole corpus and no site has
been shown to pay); the **UNSIGNED magic sequence**, which needs a 65-bit multiplier and an
"add-indicator" fixup — the unsigned power-of-two cases are taken and are the cheapest reductions
here, but `x /u 10` still divides; the **arm64 `SMULH` op**, which is the whole of what that lane
needs; a divisor at or above **2^62.5**, where the derivation leaves i64 range and declines;
`|K| == 1`, an identity `foldConstOperands` cannot reach because `div`/`mod` have no `binOpImm` form;
and — measured rather than assumed — **`const` UNIFICATION, which would buy this row a second time**.
`(n / 8) + (n mod 8)` emits its four-op quotient chain ONCE because CSE merges the two; the same
program over `10` emits it TWICE, because there is no constant interning and the two sites mint two
`const` ops for one 64-bit magic, so no expression comparison can call the two `mulHighSigned`s equal.
That is `EC13`'s own filed item, and this is the second row to be worth a measurement to it.

#### `EC19` · Register-to-register copies — the census, and `commuteForCoalescing`

v1's `Mir/CommuteForCoalescing.maxon` (355 lines) commutes a commutative op's operands so the
register coalescer can eliminate the copy. shv2's allocator already coalesces (biased colouring,
`RegisterAllocator.maxon:1830-1853`); the row was filed to make it succeed more often. Its SUBJECT,
though, is the wider question — **why are 17% of nbody's emitted ops copies, and which of them need
not be** — and that is what it answers.

⭐⭐ **THE CENSUS IS THIS ROW'S PRODUCT, AND IT SAYS THE COPIES ARE NOT A COALESCING PROBLEM.** None
is a self-move (the biased colouring already deletes those, `SsaDestruction.maxon:481`), so every one
is a real copy between two different registers. Measured 2026-08-29 on **the self-compile**, the
realest program in the tree — 8,554 functions, **1,275,734 emitted x64 ops, 248,215 `movRegReg`
(19.5%)** — each attributed by structural rules over the `--emit-ir` dump:

| bucket | count | share | who emits it |
|---|---|---|---|
| **ABI: call argument setup** | 169,037 | **68.1%** | `emitArgMovesByFloatMask` |
| **ABI: call result capture** | 28,955 | 11.7% | `lowerCall`, out of R8 / XMM0 / R10 |
| **ABI: parameter capture at entry** | 15,564 | 6.3% | `lowerParam` |
| **ABI: a result straight into the next call's argument register** | 9,192 | 3.7% | both of the above at once |
| SSA-destruction edge copies | 11,379 | 4.6% | `SsaDestruction` (5,752 in `critsplit` blocks) |
| frame `mov rbp, rsp` | 8,554 | 3.4% | one per function, `X64PrologueEpilogue` |
| **ABI: return-value move** | 4,368 | 1.8% | `emitPrimaryReturnMove` |
| **two-address reuse copies** | **969** | **0.39%** | `RegisterAllocator.allocateReuseDef` |
| fixed-register (`idiv` operands, a shift count in CL) | 57 | 0.02% | `lowerDivMod` / `lowerShiftCl` |
| unclassified | 132 | 0.05% | — |

**91.6% of the copies are the ABI, and they are FORCED rather than missed.** Every argument register
in shv2's custom convention — `rcx, rdx, rax, r9, rsi, rdi` and `xmm0–5` — is CALLER-saved, so a value
that has to survive the call cannot live in the register the call wants it in. Measured: **93.0% of
the 178,339 argument-setup copies read a CALLEE-SAVED register**, and **95.7% of those provably cross
a call** (the source is read again after this call, or a call lies between its definition and here).
Two readings say the same thing from the other side: **a call-free function is 5.5% copies against
19.5% for a function that calls** (121 vs 8,433 functions), and the self-compile emits **1.67
`movRegReg` per emitted call**.

⇒ **THE LEVER ON THIS COLUMN IS FEWER CALLS, NOT BETTER COALESCING — and specifically `EC2`.**
**21.2% of the self-compile's 148,623 calls are refcount primitives** (`__mm_incref` / `__mm_decref` /
`__managed_decref` / …) and **9.7% of all copies feed one directly**. The INDUCED cost is larger than
the direct one: a managed field read bracketed by `incref`/`decref` is a value LIVE ACROSS A CALL,
therefore callee-saved, therefore copied again at every later use as an argument. `targetOpOperands`
in shv2's own source is 46% copies for exactly that reason:

```
loadRegBaseDisp r13, [rbx + 8]   ; the union payload
mov  rcx, r13                    ; ─┐ the retain's argument
call __mm_incref                 ;  │
mov  rcx, r12                    ;  │ r13 must now be CALLEE-saved, so the REAL call
mov  rdx, r13                    ;  │ has to copy it again
call pushCopyOperands            ;  │
mov  rcx, r13                    ;  │
call __mm_decref                 ; ─┘
```

**AND TWO THIRDS OF THE REUSE COPIES COULD NEVER HAVE BEEN COMMUTED AT ALL:**

| shape | count | why |
|---|---|---|
| an IMMEDIATE or CL-count form (`andRegImm32`, `xorRegImm32`, `sarRegImm8`, `shrRegCl`, …) | 514 | ONE register operand — nothing to swap |
| **a commutative binary — the row's target** | **314** | `imul` / `and` / `or` / `xor` |
| non-commutative binary (`sub`, `divsd`) | 71 | the order IS the answer |
| unary (`neg`, `not`) | 70 | one operand |

✅ **`commuteForCoalescing` LANDED 2026-08-29 ANYWAY, AND IT IS NOT A PASS.** The two-address
constraint is x64's, so the decision is x64's: `StdToX64Conversion.commutesForCoalescing`, at
instruction selection. Swapping two USE operands of ONE op is free of every ordering question — both
are read at the same point, so no live range moves and nothing downstream is invalidated — and **no
descent was added**: both liveness facts come off the ONE `blockRefs` → `opRefs` walk the isel already
makes for `EC16` (`ScaledIndexFolds`).

**The rule**: the opcode is commutative (`binOpcodeIsCommutative`, the dialect's one answer — already
right that `min`/`max` are NOT), the x64 instruction is destructive (`imul`/`and`/`or`/`xor`; integer
`+` is the three-operand `lea` and is excluded), the RIGHT operand PROVABLY DIES at the op, and the
LEFT one has a second reader.

⭐ **THE DEPARTURE FROM v1 IS THE WHOLE OF THE DESIGN, AND v1's RULES WOULD HAVE MISSED THE ONE SHAPE
THAT PAYS.** v1 classifies each operand by its DEFINING OP — fixed-register-bound, immediate-foldable,
or flexible — and swaps only to move a fixed-bound operand off the left; it never asks liveness. The
shape that actually costs shv2 a copy is `EC1`'s inlined bounds guard,
`bitOr(setcc(i < 0), setcc(i >= len))`, whose operands are BOTH "flexible" — v1's rules answer *no
swap*. shv2 asks the question that decides the copy instead. v1's immediate-foldable rule has no shv2
analogue either: `foldConstOperands` has already turned a constant operand into an `imm` FORM
(`andRegImm32`), which is not a two-operand op at all.

⚠⚠ **"READ EXACTLY ONCE" IS NOT "DIES HERE", AND THE FIRST CUT GOT THAT WRONG.** It tested only
`valueHasASecondReader(rhs)`, on the argument that a use is dominated by its def. Dominance does not
carry it: **a value defined OUTSIDE a loop and read ONCE inside it has one reader and is live across
the back edge.** Combined with the LEFT half's acknowledged imprecision (a second reader that comes
BEFORE the op leaves `lhs` dying here anyway) that is not merely a missed win — it swaps a copy INTO
existence, 0 becomes 1 per iteration, and shv2 REFUSES rather than spills, so the price is an `E5001`
on a program that compiled before. **Found by the independent review, not by any gate**: the suite was
6,827 green with zero `E5001` either way. `valueDiesAtItsOnlyReader` now asks "read exactly once" AND
"defined in the same block", which closes the range inside one block and is exact; the LEFT half stays
necessary-but-not-sufficient, and with the RIGHT half exact its imprecision can only cost **a golden
that moved for nothing**.

`scripts/emitted-code-count.py`, committed corpus, before → after. Every other column is
byte-identical, as it must be — this row selects no instruction differently:

| | ops | mov |
|---|---|---|
| nbody | 8,586 → **8,582** | 1,468 → **1,464** |
| fannkuch-redux | 1,708 → **1,700** | 219 → **211** |
| TOTAL | 10,591 → **10,579** | 1,744 → **1,732** |

**AGAINST A REAL CONTROL** — the same tree with the one `let swap = …` line replaced by
`let swap = false`, both compilers built in one session and the SAME source compiled by both:

| | control | with the row | |
|---|---|---|---|
| emitted x64 ops | 1,276,158 | **1,275,980** | **−178** |
| `movRegReg` | 248,286 | **248,111** | **−175** |
| two-address reuse copies | 969 | **800** | |
| … of which commutable | 314 | **156** | |
| emitted binary | 9,007,487 B | **9,006,975** B | **−512 B** |

⭐⭐ **AND THE TIMED A/B IS NOT ZERO — fannkuch is −4.7%, WHICH IS THE FIRST NON-ZERO TIMED RESULT
THIS WORKSTREAM HAS HAD FROM REMOVING A COPY.** Two compilers from one tree differing only in that
line, each compiling `examples/`, runs interleaved A B A B A B on one box, both printing identical
answers:

| program | control | with the row | |
|---|---|---|---|
| `fannkuch-redux` | 10,707 / 10,589 / 10,605 ms | **10,095 / 10,144 / 10,143 ms** | **−4.7%** |
| `nbody` | 10,340 / 10,226 / 10,256 ms | 10,220 / 10,220 / 10,249 ms | **0** |

**Eight instructions, and all eight are in fannkuch's innermost loops**: every one is `EC1`'s bounds
guard, which the program pays on every element access — the same reason `EC16` bought ×1.78 there and
`EC14` bought nothing. nbody's four are in its float-printing stdlib, which runs a handful of times.
⚠ The two binaries differ in 16 KB, not 8 instructions — a different colour for one value moves others
— so the honest attribution is *to the change*, not to the eight `mov`s alone. ⭐ What that churn came
to is worth stating, because it could have gone either way: across the whole self-compile the ONLY op
kinds whose count moved at all are **`movRegReg` −175, `loadRegSlot` −2 and `storeSlotReg` −1**.
Nothing was ADDED anywhere — the change removed 175 copies and, incidentally, three spill/reload ops.

⚠ **A FALSE READING THAT ONLY THE CONTROL CAUGHT, worth carrying because it is the shape of the
mistake.** Comparing the self-compile BEFORE the change against the self-compile AFTER it read the
binary **114 bytes BIGGER** while emitting 116 fewer instructions — which invites a story about REX
prefixes, since a register above `r7` costs one byte the low eight do not. It is an ARTEFACT: the two
runs compiled DIFFERENT SOURCE, this row's own added lines included, so the delta held the new code as
well as its effect. **A before/after across a source change is not an A/B.** The same trap ate the
first fixpoint check, where stage-2 and stage-3 differed in 175 bytes that turned out to be the LINE
NUMBERS inside `panic at StdToX64Conversion.maxon:1747` — exactly the 12 lines of comment added
between the two stages.

⚠⚠ **AND THE SCALE LADDER DISAGREES WITH ALL THREE REAL PROGRAMS — THE ROW'S ONE UNEXPLAINED READING.**
Against the same control, its binary swapped in and the ladder re-run: emitted **`codeBytes` +0.75% at
rung 0 rising to +1.45% at rung 5** — the LADDER's emitted code gets BIGGER — for **+1.20% of compile
allocations, +3.81% of bytes and +5.8% of CPU**. ATTRIBUTED, and the attribution is the useful half:
**`phase:isel` allocations are IDENTICAL TO THE DIGIT (2,855,745 both ways)**, so the two dense columns
this row adds cost nothing at all — they are reset per function and reuse their capacity — and the
entire effect is **`regalloc:splitting` +7.59% allocations / +12.20% CPU**. The swapped operand order
changes a colour, this corpus then SPLITS more, and the extra code bytes are spill/reload.

⚠ **The three real programs say the opposite** — nbody's and fannkuch's binaries are the same size to
the byte, the self-compile is 512 bytes SMALLER with `loadRegSlot`/`storeSlotReg` DOWN, and fannkuch
runs 4.7% faster — and there is a standing reason to distrust the ladder on exactly this axis:
`ScaleCorpus`'s pressure knob is sized to sit AT the register pool (`calleeSavedMask` is what
`floatsLivePerSpillLoop` reads), so ANY colouring perturbation tips it into splitting. That is the
corpus being a knife-edge, not necessarily the change being bad — but it is a reading, not an excuse,
and **which of the two generalises is OPEN.** A row that later touches the allocator should re-measure
it. The trend row in `docs/optimization-log.md` carries the same numbers.

**GATES**: x64-windows **6,827 passed, 0 failed, exit 0** (6,822 + 5 new) and wasm32-wasi **6,366 passed, 0 failed, exit 0** (6,361 + 5);
**zero `E5001` in either run**, and `EC13`'s knife-edge case
`generic-hash-table-regalloc/…witness-dispatch-inside-a-pressured-loop` stays green. **497
goldens re-minted** from ONE unfiltered run and re-verified at zero drift, 5 added. **Self-host
fixpoint: stage-2 == stage-3, BYTE-IDENTICAL (9,006,975 bytes).**

**Cases added** to the new `specs-shv2/commute-for-coalescing.md`, 5, four of them CONTROLS and every
one sabotage-verified against the committed fragments:

| case | sabotage | what moved |
|---|---|---|
| `a-commutative-op-with-a-dying-right-operand-needs-no-copy` (the gate) | `let swap = false` | EXACTLY this one; the four controls byte-identical |
| `a-commutative-op-whose-both-operands-survive-keeps-its-copy` | invert the dying-`rhs` test | this one and the gate; the other three byte-identical |
| `a-non-commutative-op-keeps-its-copy` | drop `binOpcodeIsCommutative` | **RED, exit 1** — a wrong answer, not a golden |
| `a-float-multiply-keeps-its-copy` | call the rule from `lowerFloatBinOp` | EXACTLY this one |
| `an-integer-add-is-not-commuted` | `add gives true` in the roster | EXACTLY this one |

⚠⚠ **TWO OF THOSE FOUR CONTROLS FIRST PASSED FOR THE WRONG REASON, AND BOTH WERE CAUGHT BY RUNNING
THE SABOTAGE RATHER THAN BY READING THE CASE.** The subtraction's first spelling was `a - b`, and the
`add`'s was `total + step` — in both, after inlining, the right operand IS the loop counter, which has
half a dozen readers, so the dying-`rhs` condition refused first and the guard under test was never
reached: the sabotage left every fragment byte-identical and every case green. Giving each a right
operand of its own (`b * 3`, `step * 5`) fixed it — and the `add`'s SECOND spelling failed differently
again, because `step * 3` against a `total` of `i * 3` is the SAME VALUE after CSE and the `x ⊕ x`
guard refused it. **A control is not a control until its sabotage has been run.**

**Left open**: the whole ABI column above, which is `EC2`'s and the calling convention's, not this
row's; the **frame pointer** (8,554 copies, one per function, plus `push`/`pop` — omitting it frees a
register and ~25,000 ops, and breaks backtraces, the GT runtime and stack-parameter addressing, so it
is its own row); the **`critsplit` edge copies** (5,752), where a phi is biased toward the register
its SLOW arm's call returns in and the two FAST arms then each pay a copy — a phi-colouring heuristic
with no profile to read, and rematerializing a CONSTANT phi input on the edge instead of copying it is
the bounded half of it; the LEFT-operand imprecision, whose cure is a use-POSITION column; and
`integerBinOpIsTwoAddress`, which is a SECOND statement of a fact `lowerBinOp`'s own match owns — the
structural cure is a commutativity bit on `TargetOpMeta` read off the operand model, which would let
`allocateReuseDef` make this decision with EXACT liveness and no census at all.

⚠ **AND ONE THING THE CENSUS FOUND THAT IS NOT ABOUT COPIES AT ALL**: `EC1`'s bounds guard emits
`cmp`/`setcc` twice, `or`, `cmp $0`, `jcc` — SIX instructions in the hottest loop of every
array-indexing program — where x64's idiom is TWO: `cmp idx, len` / `jae slow`, because an UNSIGNED
compare catches `idx < 0` and `idx >= len` at once. Filed here rather than taken.

### ⭐⭐ RE-PRIORITIZED 2026-08-29, AFTER ALL TEN ROWS LANDED

The original Tier 2/3 ordering is superseded. Three things re-ranked it, all measured:
`EC2`'s profile of the stage-2 self-compile, `EC19`'s copy census, and two probes taken today.

**`A1` · `EC7` — a proven-ranged counter still pays its callee's guard. THE TOP ROW, and it is the
one that fell off the end.** `EC7` has sat ⬜ FREE since 2026-08-26. Its own text claims
`regMaskContains`'s four hottest call sites are in `augmentValue`/`tightRegisterSet` at **37% of the
shv2-emitted profile** — and `EC2`'s profile, taken two rows later for an unrelated purpose,
independently measures those two functions at **35.6%** (`augmentValue` 25.7% + `tightRegisterSet`
9.9%). ⇒ **the largest measured concentration in the compiler's own emitted code, confirmed twice.**
Probe (`temp/codegen-probe/ec7.maxon`, a `RegNum`-parameter callee reached from `for r in 0 upto 64`):

```
  forhdr:    cmp r13,64 ; jcc ge,forexit
  __il_body: cmp r13,0  ; jcc less,__il_slow   ← DEAD: the counter is provably 0..63
  __rc_chk:  cmp r13,63 ; jcc le,__rc_ok       ← DEAD
  __il_slow: … callDirect weigh                ← and the slow arm RE-ISSUES THE CALL,
  __rc_ok:   lea rax,rbx,r13 ; mov r8,rax        so EC5's inlining is defeated on this shape
```
**4 of the loop's 11 instructions are a provably-dead guard**, and the fast arm it guards is 2.

**`A2` · The bounds guard is 7 instructions where x64's idiom is 2.** `EC19`'s census named this and
under-counted it. Measured today (`temp/codegen-probe/guard.maxon`): `cmp`/`setcc`/`cmp`/`setcc`/
`or`/`cmp`/`jcc`, where `cmp idx,len` + `jae` does it in two — an UNSIGNED compare catches `idx < 0`
and `idx >= len` together, because a negative index wraps above any length. 24 sites in
`fannkuch-redux` ≈ **7% of its user-code ops**; 6 in nbody. Every checked array access in every
program, and the `or` it produces is what feeds two of the `critsplit` blocks below.

**`A3` · `retainBorrowedPayload` — the rest of `EC2`, and a different rule.** `EC2`'s census finds
**825 remaining refcount brackets; 801 acquire through `__mm_incref` from
`Parser.retainBorrowedPayload`** — a managed payload bound out of a *borrowed union in a `match`
arm*, retained unconditionally and dropped at the arm's exit. **197 in `targetOpOperands` alone**,
the function `EC19` independently measures at 46% copies. A payload-binding rule, not a field-read
rule.

**`B1` · Block reordering.** `EC11` collected the fall-throughs that already existed and explicitly
did not create new ones. `EC15` then showed what reordering is worth by accident — deleting one block
made a continuation physically next and `EC11`'s elision took its jump for free.

**`B2` · `critsplit` edge copies** — 5,752 in the self-compile, 72 in fannkuch, 108 in nbody. `EC19`:
the phi is biased to the register the *slow* arm's call returns in, so both *fast* arms pay.
Rematerializing a constant phi input on the edge is the bounded half.

**`B3` · `const` unification** — filed by `EC13`, then measured by `EC18` to *"buy this row a second
time"*: two sites mint two `const` ops for one 64-bit magic, so `(n/10)+(n mod 10)` emits its
quotient chain twice where `/8` emits it once.

**`B4` · The frame pointer** — 8,554 copies plus `push`/`pop`, ~25,000 ops and a whole register. Big,
and risky: it breaks backtraces, the GT runtime and stack-param addressing.

**`C` · `EC20` instruction scheduling** — better motivated than when it was filed (the 35.6% above),
but `A1` targets the same functions far more cheaply. Do `A1` first and re-measure.

**⛔ MEASURED AND NOT WORTH BUILDING**, unless something changes: `EC22` (rel8 relaxation — code size
only, ~1.8 KB after `EC11` deleted 40% of the jumps); `EC23` (general DCE — the frontend refuses dead
source, so its producers are only the passes); `EC21` (interval match dispatch — its premise cites
`JumpTableFormationPass.cs`, **which does not exist**; re-measure before quoting it).

### ⚠ OWED, AND NOT OPTIMIZATION WORK

- **A latent correctness bug, filed by `EC11`**: a block with no terminator op falls through
  PHYSICALLY, but `collectBlockSuccessorIds` / `buildFuncTopology` model it as having **no
  successors**. `X64BranchCleanup` closes the hole locally; liveness, loop depth and critical-edge
  classification would all be wrong if such a block ever became reachable. **Rank this by risk, not
  by win.**
- **arm64** owes a golden mint and carries real `EC16`/`EC18` codegen **never executed on that lane**.
  `EC18`'s `SMULH` is all that lane needs for its half of strength reduction.
- **`EC19`'s ladder disagreement is still OPEN** — +1.45% emitted bytes at rung 5, attributed to
  `regalloc:splitting`, unexplained.
- `W219` (a file-private `let` unified across files), and `E3092` cannot see a default argument's
  names — both reproduced by rows here, neither theirs to fix.

### Tier 3 — measure before committing

#### `EC20` · Instruction scheduling

v1's `Targets/Shared/InstructionScheduler.maxon` (847) is a register-pressure-aware bottom-up list
scheduler with a per-target latency table. shv2 has no equivalent and **no MIR tier to put one in**
(3 tiers by design), so it would sit between SSA destruction and register allocation. ⚠ v1's
register allocator was ~74% of its self-compile time — port the *lesson*, not the code, per
`CLAUDE.md`'s standing warning.

#### `EC21` · Interval and binary-search match dispatch

shv2 forms a jump table for **dense** arms only; the bootstrap chooses among linear chain / table /
binary search over *intervals*, with four named thresholds
(`MaxonToStandardConversion.SwitchDispatch.cs:28,34,56`). ⚠ **Re-measure PLAN.md's "tree-wide
tension" entry first** — it cites a file that no longer exists, so its claim that CLAUDE.md's
"consolidate redundant match arms" rule demotes jump tables may already be false.

#### `EC22` · Short-jump (rel8) relaxation

shv2 emits `jmp`/`jcc` at rel32 unconditionally (`X64Backend.maxon:157,1125,1149`); so does the
bootstrap. **Code size only** — nbody is 64,296 bytes of code with 1,016 jumps, so the whole
rel32→rel8 saving is at most ~3 KB (4.7%), and after `EC11` deletes 410 of those jumps it is ~1.8 KB
(2.8%). Lowest priority here; listed for completeness.

#### `EC23` · General DCE for dead pure values

`FoldConstOperands` retires exactly two op kinds and its header says explicitly it *"MUST NOT GROW
INTO ONE."* ⚠ **The frontend refuses dead source** (E3012 unused-variable), so unlike a C compiler
shv2 has little to collect — the real producers are inlining, identity folds and proven-redundant
range checks. **Measure the corpus first**; this may be a row with nothing behind it.

## What is deliberately absent from this roadmap

- **arm64-macos and wasm32-wasi as first-class rows.** Design and measure on x64-windows (the host
  lane); each row states what it owes the other lanes. `EC16` was filed as the exception — "the arm64
  instruction is the same instruction" — and that held for the ADDRESS and NOT for the ACCESS; see the
  row for what AArch64's register-offset load actually encodes. It landed on both lanes anyway, at
  different depths, and owes arm64 a golden mint on a Mac.
- **Compiler-speed work.** `scale-test` and `docs/optimization-log.md` are a different axis; a row
  here is judged by `self-host-ab.sh` and `--emit-ir`, not by the ladder.

## How a row gets taken

1. **Re-measure.** ⛔ Every number above is dated **2026-08-28** against `f8380ebcaa`. Rows rot
   ([[rung-rows-rot-measure-before-planning]]), and `W222` is the cautionary case in this very
   workstream: three rows sat for two days quoting a headline that landing rungs had already
   falsified, because nothing was asked to re-measure it.
2. **File the row** onto `maxon-shv2/PLAN.md`'s slice board with its lane, citing this file rather
   than restating it, and push — the push is the lock.
3. `/rung EC<n>`.

**The instrument is `scripts/self-host-ab.sh`** (~15 min; `--profile` adds function-level
attribution, `--suite` runs the whole suite under stage-2).

⛔⛔ **AND IT WAS BROKEN AT `2cdfab05d5` — THE COMPILER COULD NOT COMPILE ITSELF, WHICH MEANS THIS
WORKSTREAM'S ONE INSTRUMENT COULD NOT RUN AT ALL.** Found 2026-08-29 while re-measuring `EC17`; three
defects, all landed by rows in this document, none of them visible to the spec suite (the bootstrap
builds shv2, and the bootstrap has neither the borrow checker's E3070 nor `checkUnusedExports`):

- `EC12` left an **E3070** in `FoldConstants.foldConstantBranches` — a `module.ops.get(...)` read inline
  borrows `module` while the fold in the next breath mutates it. Read through a function, which is the
  discipline `LeafInliner.opAtIndex` and `PrimitiveInliner.opAt` already state for themselves.
- `EC16` left **three E3092s**: `MemoryIndexScaleShift` and `MemoryIndexScaleFactor` are named nowhere
  outside `TargetDialect.maxon` and simply lost their `export`; `X64SibNoIndex` is a **false positive**
  and is now `public`. ⚠ **That third one is a hole in E3092 worth its own row**: a DEFAULT ARGUMENT is
  evaluated in the CALLER's scope, so `X64GtRuntime`/`X64Runtime` resolve that name in their own files
  while writing nothing — drop the visibility and the build stops with 20 × `E2004 Undefined variable`.
  `checkUnusedExports` counts the names a file WRITES, so it cannot see such a use.

⇒ **A row that changes a pass owes a self-compile, not only a suite run.** All three are repaired and
the fixpoint holds byte-identically; this is filed here rather than in a row because it is about the
instrument. ⛔ `git status specs-shv2/fragments/`
measures nothing, and since 2026-08-27 neither does golden drift. To show what a rung did to emitted
code, **disassemble or use `--emit-ir` / `--emit-ir-runtime`** and count.

### Reproducing this document's measurements

⚠ Both compilers name the `.ir` after the SOURCE, not after `-o`, so run them into separate
directories or rename between runs — a bootstrap build silently overwrites the shv2 `.ir`.

```
mkdir -p temp/codegen-probe && cp examples/nbody.maxon temp/codegen-probe/
cd temp/codegen-probe
../../maxon-shv2/.maxon/maxon-shv2.exe build nbody.maxon --emit-ir -o nbody_shv2
mv nbody.ir nbody_shv2.ir
../../bin/maxon.exe build nbody.maxon --emit-ir       # no -o; writes nbody.ir + nbody.exe
mv nbody.ir nbody_cs.ir
```

Then count `x64.jmp <t>` lines whose next non-blank line is `<t>:` (shv2) or `x64.label <t>`
(bootstrap). ⚠ A bootstrap `spec-test` run deletes every `*.exe` under `temp/` recursively
(`TestRunner.CleanupExecutables`), so do not park a staged binary here.

The three probes are reproduced in full below so they survive `temp/` being scratch. Maxon needs
tabs for block indentation, `end '<name>'` to close a block, `mod` (not `%`) for remainder, and
named arguments from the second onwards.

**`arr.maxon`** — the anchor loop (§THE ANCHOR):

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(a IntArray) returns Integer
	var t = 0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'sum'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	print("s={sum(a)}")
	return 0
end 'main'
```

**`leaf.maxon`** — the `EC17` probe. `Array.isEmpty`'s body is one `__managed_count` call, so before the
reorder `main` held a `callDirect Array.isEmpty` and `inlineLeaves` reported *"0 site(s) inlined"* on this
program. It now reports 1, and `total`'s loop goes with it:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function total(a IntArray) returns Integer
	if a.isEmpty() 'empty'
		return 0
	end 'empty'
	var t = 0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(5)
	if total(a) != 5 'bad'
		return 1
	end 'bad'
	return 0
end 'main'
```

**`cse2.maxon`** — the CSE probe (`EC13`) and, with `x` a literal, the constant-folding probe
(`EC12`). shv2 emits `imul`/`lea` three times; the ideal is one `mov rax,97`:

```maxon
typealias Integer = int(i64.min to i64.max)

function work(x Integer, y Integer) returns Integer
	let a = x * 31 + y
	let b = x * 31 + y
	let c = x * 31 + y
	return a + b + c
end 'work'

function main() returns ExitCode
	print("r={work(3, y: 4)}")
	return 0
end 'main'
```

**`ec7.maxon`** — the probe for `A1`/`EC7`, the top remaining row. A callee taking a `RegNum`
(`int(0 to 63)`) parameter, reached from `for r in 0 upto 64` — so the counter is PROVABLY in range
and every guard it pays is dead. Measured at `beddff81f1`: **4 of the loop's 11 instructions are the
dead guard**, and the `__il_slow` arm re-issues the CALL, defeating `EC5`'s inlining on this shape:

```maxon
typealias RegNum = int(0 to 63)
typealias Word = int(i64.min to i64.max)

function weigh(table Word, regNum RegNum) returns Word
	return table + regNum
end 'weigh'

function scan(table Word) returns Word
	var n = 0
	for r in 0 upto 64 'scan'
		n = n + weigh(table, regNum: r)
	end 'scan'
	return n
end 'scan'

function main() returns ExitCode
	if scan(0) != 2016 'bad'
		return 1
	end 'bad'
	return 0
end 'main'
```

**`guard.maxon`** — the probe for `A2`, the bounds guard. An explicit `a.get(i)` in a loop (the
`for`-in form uses `__managed_get_unchecked` and has no guard at all). Emits
`cmp`/`setcc`/`cmp`/`setcc`/`or`/`cmp`/`jcc` — **7 instructions where `cmp idx,len` + `jae` is 2**.

**`fmt.maxon`** — the DIVISION probe (`EC18`). ⚠ Neither benchmark's hot loop contains a division:
nbody and fannkuch hold 11 `idiv` between them and every one is in cold or amortized code. The
`idiv`-by-constant sites that a program actually pays are in shv2's **float formatting stdlib**
(`__decimalWidthOf` and `__appendDecimalGroup`, four `idiv` by 10 per number) and in
`graphemeBreakProperty` (`idiv` by 28 per grapheme). ⚠ The INT path is hand-emitted runtime
(`__int_to_string`) and has no Maxon-level division at all, so an int-formatting probe measures
nothing — this one formats floats deliberately. 300,000 numbers, **3,586 / 3,554 / 3,559 ms** at
`24dfb88ed1`, carrying 8 of the corpus's 22 `idiv`:

```maxon
typealias Real = float(f64.min to f64.max)

function main() returns ExitCode
	var sink = 0
	var x = 1.0 as Real
	for _ in 0 upto 300000 'fmt'
		let s = "{x}"
		sink = sink + s.byteLength()
		x = x + 1.5
	end 'fmt'
	if sink < 0 'guard'
		return 1
	end 'guard'
	return 0
end 'main'
```

**`cse3.maxon`** — the CSE probe (`EC13`). ⚠ `cse2.maxon` STOPPED being a CSE probe when `EC12`
landed: its operands are constant after inlining, so `foldConstants` now evaluates all three copies
away and its `imul-imm` reads 0. `cse3` feeds the same expression a value the compiler cannot see
through, so the three copies survive folding and only CSE can remove them. Before `EC13` shv2 emitted
`imul`/`lea` three times into three simultaneously-live registers; it now emits the pair ONCE
(`imul-imm` 3 → 1, ops 26 → 22), which is what the row was opened for:

```maxon
typealias Integer = int(i64.min to i64.max)

// The operand is a RUNTIME value the compiler cannot see through, so foldConstants
// cannot collapse this. Three identical subexpressions remain three computations.
function work(x Integer, y Integer) returns Integer
	let a = x * 31 + y
	let b = x * 31 + y
	let c = x * 31 + y
	return a + b + c
end 'work'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 3 'feed'
		seed = seed + work(i, y: i + 1)
	end 'feed'
	if seed < 0 'guard'
		return 1
	end 'guard'
	return 0
end 'main'
```

**`probe.maxon`** — one function per classical opportunity (`EC12`, `EC18`). Both compilers emit
`imul` for `x * 8` and `cqo`+`idiv` for `/ 8` and `/ 10`; both fold `x + 0`; neither folds
`2 + 3 * 4`; neither CSEs `(a+b)*(a+b)`. shv2 additionally inlines every one of them into `main`
and dead-function-eliminates the bodies, which the bootstrap does not:

```maxon
typealias Integer = int(i64.min to i64.max)

function mulPow2(x Integer) returns Integer
	return x * 8
end 'mulPow2'

function divPow2(x Integer) returns Integer
	return x / 8
end 'divPow2'

function divConst(x Integer) returns Integer
	return x / 10
end 'divConst'

function modConst(x Integer) returns Integer
	return x mod 10
end 'modConst'

function addZero(x Integer) returns Integer
	return x + 0
end 'addZero'

function foldConst() returns Integer
	return 2 + 3 * 4
end 'foldConst'

function cse(a Integer, b Integer) returns Integer
	return (a + b) * (a + b)
end 'cse'

function main() returns ExitCode
	var t = 0
	t = t + mulPow2(3)
	t = t + divPow2(64)
	t = t + divConst(100)
	t = t + modConst(7)
	t = t + addZero(1)
	t = t + foldConst()
	t = t + cse(2, b: 3)
	print("t=\{t}")   // \{ is a LITERAL brace: this probe is about arithmetic, not interpolation
	return 0
end 'main'
```

⚠ A dead-value probe cannot be written in source: the frontend refuses an unused binding
(E3012), which is why `EC23` says its producers are the passes, not the programmer.
