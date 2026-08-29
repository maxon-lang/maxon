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

## The three-way inventory — the answer to "what do the other two have that shv2 lacks?"

Pipelines compared: `maxon-shv2/Compiler/IR/PassPipeline.maxon:398-413` ·
`maxon-sharp/Compiler/3-MlirPipeline.cs:62-173` · `maxon-selfhosted/Compiler/IR/PassPipeline.maxon:778-898`.

| Optimization | shv2 | bootstrap | v1 self-hosted |
|---|---|---|---|
| Whole-program dead-function elim | ✅ `DeadFunctionElimination.maxon` | ✅ `DeadFunctionElimination.cs` | ✅ ×2 (Maxon + Std tier) |
| Inlining | ◑ `InlineLeaves` — leaf-only, one round, ≤24 ops | ❌ none | ✅ `Passes/Inliner.maxon` (3,067 lines) |
| Managed-primitive inlining | ✅ `InlineManagedPrimitives` (EC1) | ❌ n/a | ❌ n/a |
| Const **operand** → immediate | ✅ `FoldConstOperands` | ◑ imm8/imm32 encodings only | ✅ `Canonicalize` |
| Algebraic identity (`x+0`, `x*1`) | ✅ `FoldConstOperands` move 3 | ✅ `TryAlgebraicIdentity` | ✅ `Canonicalize` |
| **Constant folding (`const ⊕ const`)** | ✅ `FoldConstants` (EC12) | ❌ | ✅ `Canonicalize` |
| **CSE / GVN** | ✅ `CommonSubexpressionElimination` (EC13) — dominator-scoped, arith band, call-barriered | ❌ | ✅ `CommonSubexpressionElimination.maxon` (494) |
| **LICM** | ❌ | ◑ refcount pairs only | ✅ `LoopInvariantCodeMotion.maxon` (596) |
| **General DCE (dead pure values)** | ❌ *(2 op kinds only, by design)* | ✅ `DeadStoreEliminationPass` sub-pass 3 | ✅ `DeadCodeElimination.maxon` (728) |
| **Block merging / branch simplification** | ◑ `X64BranchCleanup` (EC11) — elision, inversion, threading, unreachable; **no reordering** | ◑ cond-br then-edge only | ✅ `CfgAnalysis` + `Canonicalize` |
| **Strength reduction** (`mul`→`shl`, magic div) | ❌ | ❌ | ❌ |
| **Scaled-index addressing** (`[base+idx*8]`) | ✅ `loadRegBaseIndexScale` etc. (EC16) — x64 full, arm64 the `ADD` half | ❌ | ❌ |
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
`EC11`, `EC12`, `EC13` and `EC16`**: the executed hot path is **12 instructions per element**, down
from 15 — and what remains is a list of the rows still open.

```
  forhdr:     load rax,[rbx+8]      ← length, RELOADED every trip          (EC14 LICM)
              cmp r13,rax ; jcc greaterEqual,forexit
  loop:       load rax,[rbx+24]     ← element_size, RELOADED every trip    (EC14 LICM)
              cmp rax,8 ; jcc notEqual,__im_stride   ← a RUNTIME stride dispatch on a
                                                       type whose stride is 8 at COMPILE TIME (EC15)
  __im_word:  load rax,[rbx+0]      ← buffer base, RELOADED every trip     (EC14 LICM)
              load rax,[rax+r13*8]  ← EC16 folded the `imul` AND the `lea` into this operand
              jmp  __il_cont        ← __im_stride is physically next, so EC11 cannot elide this.
                                      BLOCK REORDERING would (filed, not taken)
  __il_cont:  lea  r12,r12,rax      ← EC11 elided this block's `jmp forstep`
  forstep:    lea  r13,r13,1
              jmp  forhdr           ← the real back edge
```

An ideal x64 body is **5**: `mov rax,[rbx+r13*8]` · `add r12,rax` · `inc r13` · `cmp r13,len` · `jl`.
**12 → 5**, and the element read itself is now the ideal instruction — every one of the seven that
remain is a row still open. They are independent of each other.

⚠ Note what this is NOT: the `__im_*` arms are `EC1`'s inlined fast path, and `EC1` was a large
measured win (a checked read went 176 → 26 instructions). The defect is that the inlined arm is
**not specialized by an element type the compiler already knows** — not that it was inlined.

⭐ The one jump left in the loop is the clearest argument for **block reordering**, which `EC11`
deliberately did not do: `EC11` collects the fall-throughs that already exist, it does not create
new ones. Laying `__il_cont` after `__im_word` would remove it and cost nothing.

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

The anchor loop reloads **three** invariants per element (length, element_size, buffer base).
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

#### `EC15` · Static stride specialization for the inlined managed primitives

The `cmp element_size,8` / `jcc` dispatch and the dynamically-unreachable `__il_slow` call arm are
emitted in every array access, including where the element type fixes the stride at compile time
(`Array with Integer` is always 8). `EC1`'s pass
emits its guards ordered by discrimination; this rung makes it **skip** the ones a known
`GenericInstanceId` already answers. Closes the largest per-element residue the anchor shows, and
shrinks what `EC11` then has to lay out.

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

#### `EC2` *(already on the board, ⬜ FREE)*

The managed field read retained and released across a call in the same statement. Front half of the
surviving allocation gap. ⚠ Re-measure first: `EC1`'s `__managed_fill` removed the per-element
instance the row was sized on, and `W222` falsified its stated CPU motivation.

#### `EC7` *(already on the board, ⬜ FREE)*

A `for x in a upto b` counter with constant bounds denotes no range, so every ranged parameter it
reaches is guarded at runtime — and after `EC5` that guard is *copied into the caller*.

#### `EC17` · A second inline round, or swap the two inliners

Filed by `EC5` and never taken: `Array.isEmpty` (209 sites), `Parser.advance` (106),
`String.byteLength` (92) stay calls because their one body call is `__managed_count`, which
`inlineManagedPrimitives` rewrites **one pass later**. **407 sites**, for either a second round or a
reorder. Cheapest row on this list.

#### `EC18` · Strength reduction

`mul` by a power of two → `shl`/`lea`; `div`/`mod` by a constant → the magic-number multiply.
**Neither** the bootstrap nor v1 has this, so there is no reference to read — but `idiv` is 20–40
cycles and nbody carries 8 of them plus 41 power-of-two multiplies. ⚠ Sequence it **after** `EC16`,
which removes most of the power-of-two multiplies by folding them into an addressing mode; measure
what is left before building the `shl` half.

#### `EC19` · `commuteForCoalescing`

v1's `Mir/CommuteForCoalescing.maxon` (355 lines) commutes a commutative op's operands so the
register coalescer can eliminate the copy. shv2's allocator already coalesces (biased colouring,
`RegisterAllocator.maxon:1830-1853`) — this makes it succeed more often, for 355 lines and no new
analysis. Small, well-understood, low risk.

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
attribution, `--suite` runs the whole suite under stage-2). ⛔ `git status specs-shv2/fragments/`
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
