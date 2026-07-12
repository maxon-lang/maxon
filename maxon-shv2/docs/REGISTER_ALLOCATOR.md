<!--
================================================================================
ADOPTED 2026-07-11 — this is the CANONICAL maxon-shv2 register-allocator design.
It supersedes the two competing proposals ("Design A" SSA linear-scan + greedy
fallback, and the original form of this doc). Chosen after an adversarial
5-lens + 3-skeptic evaluation of both designs against the real shv2/v1 source.
Decision: PURE Design B — build the mechanism AND commit to the E5001 contract
(refuse to emit hot spill code; the author, an AI agent, restructures). Rationale:
Maxon is written by AI agents (a deterministic, one-round-trip E5001 is a feature,
not friction — see memory project_maxon_target_ai_authors), and dogfooding E5001
on the compiler's own code forces it to stay within the Stage-5 self-compile budget
rather than hiding slow spills.

CORRECTIONS to the body below — read these FIRST, they override the text:

 1. ABI = the EXISTING custom ABI, not a new one (user: "use the existing callee
    saved set and we will see how it goes"). Callee-saved = {rbx, r12, r13, r14, r15}
    (FIVE); return value in R8; rsi/rdi are CALLER-saved. Therefore every "7 registers
    survive a call" in the body (Phase-7 example, Known-limits 2, the E5001 message) is
    WRONG for this ABI — the across-a-call budget is the 5 callee-saved. The allocatable
    pool is the existing set (all GPRs except rsp/rbp and the reserved scratch: r10 =
    indirect-call/IAT scratch, r11 = parallel-copy cycle-break). The body's "pool = 14"
    (reclaiming r10/r11) is a FUTURE tuning, not now. THE SPECIFIC COUNT IS A TUNABLE
    KNOB, NOT THE DESIGN'S CRUX (user: "the specific number of registers isn't relevant")
    — the E5001 contract exists at any count because x64 has ~14 GPRs and some loop
    always overflows.

 2. **MAKE-OR-BREAK — eliminating FALSE E5001 is the critical correctness obligation.**
    A false E5001 is the worst bug this compiler can have (it sends an agent to "fix"
    correct code and can break its convergence loop). A skeptic CONFIRMED that this
    design AS WRITTEN over-counts: Phase 4.1's "values USED inside the loop" cardinality
    gate counts each loop-carried value AND its back-edge/phi copy as two live values
    (every accumulator loop has these), firing E5001 on ordinary loops. FIX: the E5001
    decision MUST be made on the TRUE per-program-point maxlive (χ = ω, the exact chordal
    quantity — Phase 4.6's `assert maxlive <= pool`) computed AFTER biased coloring
    (Phase 5) has collapsed copy-related values — NOT on the Phase-4.1 cardinality gate.
    Biased coloring is therefore a CORRECTNESS obligation, not an optimization, and the
    AllocChecker (Phase 6) + the exact chordal theory are what verify no false positive
    escapes. This is where the care goes.

 3. Retire M4b's `EliminatePhis` Std pass (it eagerly appends copies at predecessor ends
    and clears blockArgs/branchEdges before the backend). The allocator consumes
    blockArgs/branchEdges DIRECTLY and does SSA destruction AFTER coloring — Phase 0's
    "mem2reg must NOT lower block args to edge copies" requirement, now concrete.

 4. E5001 is a REAL hard error by default (pure Design B), NOT a warning and NOT gated
    behind a flag for normal code. No routine escape hatch (Rule 3). We can revisit if a
    genuinely-unrestructurable non-compiler case ever appears (user: "we can change it
    later if we need to").

 5. Sequencing: M5's own programs have maxlive <= 3-4, so E5001 CANNOT fire yet. Build the
    spine (operand model -> liveness -> colorer -> AllocChecker) first and PROVE the
    false-positive elimination (correction 2) before E5001 can bite. The cold-spill
    splitter and E5001 diagnostic are the last M5 pieces.

 6. The IrBlock housekeeping in Phase 8 is verified: `IrBlock.clone` has ZERO callers
    (delete it); the `BranchEdge`/`Terminator`/`ApplyColoring` doc comments are stale.
================================================================================
-->

# Register allocator for maxon-shv2 — SSA chordal coloring + gap splitting

## Context

`maxon-shv2`'s allocator today is `MinimalColorer` ([RegisterAllocator.maxon](maxon-shv2/Compiler/Targets/Shared/RegisterAllocator.maxon)) — a 139-line M1 placeholder with **no liveness at all**: every distinct `ValueId` gets a fresh register from a 6-register pool, forever, never reused. It panics at 7 distinct values; `return 10 + 5 * 2 + 3` already trips it.

### The premise

**A human restructuring a hot loop will beat any spiller.** A spiller can only shuffle values between registers and stack slots; a human can change the *data structure* — hoist the working set into an array, split the loop, reorder the computation. So when a loop genuinely doesn't fit, the right move is not for the compiler to quietly emit worse code. It is to **stop and say so precisely**, and let the person who can actually fix it, fix it.

That inverts the usual obligations, and everything below follows from it:

- **A false `E5001` is the worst bug this compiler can have.** It sends someone to restructure code that was fine. Trust in the diagnostic is the whole product; one cry of wolf and the model collapses.
- **The compiler may therefore never waste a register**, because a wasted register *is* a false positive. This is what promotes 3-operand `lea`/`imul`, immediate operands, constant folding, rematerialization, biased coloring, and copy-free ISel from "optimizations" to **contract obligations**.
- **The diagnostic is the feature, not the error path.** It gets the care usually spent on codegen: which values, where defined, how many must go, and which are cheapest to move.
- **We can afford to be exact.** Because SSA interference is chordal, `maxlive` *is* the minimum register count for the program as lowered — not an estimate. So `E5001` fires **iff** the loop truly doesn't fit, and the only way to be wrong is to have wasted a register upstream.

**And the programmer is an AI agent.** Maxon is meant to be written by agents, which changes the economics: the cost of an `E5001` is one compile round-trip, not a frustrated human. That makes erroring-instead-of-degrading straightforwardly the right trade — and it adds two requirements a human-facing compiler could get away with ignoring:

- **The error must be deterministic — a property of the program, not of a search.** An agent loop needs `same program → same error`, or it cannot converge. This is the strongest argument for the design we chose over regalloc2's backtracking: "your loop needs 16 registers and has 14" is a *theorem*, stable across compiler versions; "my evict/split search gave up" can flip when a heuristic is tuned, and a rewrite loop chasing it may never terminate.
- **The error must be actionable in ONE step, not by bisection.** It states the exact deficit ("remove 2 values"), lists the candidates ranked by how cheap each is to move to memory, and names the transformation. An agent that has to guess-and-recompile N times is a design failure, not a user-experience one.

This also promotes [RULE 3](#rule-3) from fairness to **loop termination**: if a compiler-introduced value (a witness table, a layout descriptor) ever lands in a blocking set, the agent is told to delete a value **that does not appear in its source**. It cannot converge. It hangs. That is not an annoyance — it is the worst failure mode in the system, and it is why rematerialization is load-bearing.

The mechanism: **spilling is allowed exactly where it is cold, and is a compile error where it would be hot.** The programmer, not a heuristic, resolves genuine register pressure in a loop.

- **Cold spilling is free and automatic.** A value with a gap in its uses gets its live range *split*: it lives in memory across the gap and in a register where it is used. Around a loop it doesn't touch, that's a store in the preheader and a reload in the continuation — **zero instructions added to the loop body**.
- **Hot spilling is `E5001`.** If a value that a loop *uses* would have to be evicted and reloaded inside that loop, the compiler stops and reports it. No backtracking, no eviction tournament, no spill-cost model.
- **One shot.** liveness → split/spill → color. No reactive spill/color iteration. v1's allocator was **~74% of self-compile wall time** (~418 s of 561 s) not because backtracking is inherently slow — regalloc2 backtracks and is fast — but because v1 *rebuilt the interference graph from scratch on every spill iteration*. We rebuild nothing.
- **The ISA carries its weight.** Since the programmer absorbs whatever the compiler can't fit, every register the *compiler* wastes is stolen from them. 3-operand `lea`/`imul` remove the two-address copy; immediate operands stop literals occupying registers at all.

### What we take from Cranelift's regalloc2, and what we don't

**Taken — the operand model.** Each op declares its operands as `(vreg, kind: Def|Use, position: Early|Late, constraint: Any|Reg|FixedReg(p)|Reuse(input_i))`. This is not cosmetic:

- **`Reuse(i)` deletes the two-address problem at the root.** ISel emits **one** op — `sub(dest ⟸ lhs, rhs)`, "dest reuses input 0" — and the *allocator* inserts the copy only when `lhs` actually outlives it. Every op has exactly one def, so the Target tier **is** SSA, single-live-range-start is trivially true rather than an invariant to defend, and no identity `mov rax, rax` is ever emitted to be elided later.
- **`FixedReg`** gives M5's `idiv` (RDX:RAX) and the call ABI a declarative home instead of pre-colored physical vregs.
- **`Early`/`Late`** makes "may this def reuse a dying use's register?" a *declared fact per operand* rather than an emergent property of sweep ordering.

**Taken — the checker.** A symbolic verifier that abstractly interprets the allocated program and asserts every use reads the register actually holding its value. ~200 lines, runs under `spec-test`. This is the single highest-value safety net available here: `SpecTestRunner` treats fragments as *outputs*, not gates, so **the suite will otherwise go green on a wrong-but-self-consistent allocator.**

**Taken — the data layout.** See the next section. regalloc2 is fast substantially *because of* its representation, not despite it, and this is the easiest thing in the whole plan to get wrong by default.

**Not taken — the algorithm.** Live bundles, spill weights, eviction, the priority queue, iterative re-splitting. regalloc2 is engineered so allocation *always succeeds by spilling*; our contract is to fail loudly instead. And a heuristic-dependent failure ("my evict/split search gave up") is unactionable for a user told to rewrite their code, and can flip between compiler versions. Ours is a property of the program.

---

## Data representation — the part that decides whether this is fast

Not a detail, and not deferrable: register allocation was **74% of v1's self-compile**, and the acceptance budget is ≤30 s for the whole thing. regalloc2 is fast substantially because of *how it stores things* — dense `u32` index arenas instead of pointers, `Operand` packed into one word, `ProgPoint` as a single integer so live ranges are integer intervals, sorted vectors + binary search instead of hash maps, chunked bitsets for liveness, scratch buffers reused across functions. No hashing and no allocation in any hot loop.

**In Maxon this matters more than in Rust, and the codebase already knows why.** A `type` has *reference* semantics — so `Array with LiveRange` is an array of **pointers to heap-boxed, refcounted** LiveRanges: one live heap object per range. That is precisely the trap [`SourceRange.maxon`](maxon-shv2/Compiler/IR/Maxon/SourceRange.maxon) documents and dodges by storing four dense scalar columns instead of an `Array with SourceRange`. The allocator inherits that discipline wholesale.

`ValueId`s are **dense, function-local integers** — which makes them perfect array indices, and makes every map below a lie we don't have to tell:

| Concern | Wrong (and what today's code does) | Required |
|---|---|---|
| Value → color | `Map with (ValueId, X64Register)` — `MinimalColorer`'s `VRegColorMap`, a **hash map** | flat `Array with int` indexed by `ValueId`. The hashing disappears entirely. |
| Value → next-use / `forbiddenPhys` / def point | a map each | one dense column each, same indexing |
| Live-in / live-out per block | `Set` per block — one heap object per block | **dense bitset over `ValueId`** (`Array with int` as 64-bit words); all blocks in ONE flat `blocks × words` matrix, so the fixpoint's union/diff are word-parallel |
| Program position | op indices scattered across blocks | **`ProgPoint`** — one dense integer per live op in layout order. Live ranges become integer intervals; next-use distance is a subtraction; *"is this point inside loop L"* is an interval containment test — **but only if a loop's blocks are contiguous in layout order.** True for the parser's structured loops; **assert it** after preheader synthesis and critical-edge splitting append new blocks, or fall back to a per-block `loopDepth` column. Do not let this stay an unchecked assumption. |
| Per-op operands | `Array with Operand` returned per op — an allocation **and** a heap box per operand | `Operand` **packed into one int**; `targetOpOperands` fills a **reusable scratch buffer** |
| Kill records | a set per op | 2 bits per operand slot, packed into one int per `ProgPoint` |
| `BlockId` → `blockRef` | `Map` (and `getBlockByIdIn` is O(blocks) — calling it inside a fixpoint is O(B² × iters), v1's trap in miniature) | flat array — `BlockId`s are dense too |
| Register sets | any collection | `u16` bitmask; picking a register is `lowestClearBit(...)` |
| Scratch buffers | allocated per function | allocated once, reused across functions (per-worker at M5's fan-out) |

**No `Map` and no hashing anywhere in the allocator's hot path.** The `fixpointIterations` and `maxLiveSetSize` counters below exist to tell us when this stops being true.

*(We do not take regalloc2's bundle/spillset arenas or its priority queue — we have neither bundles nor a queue.)*

---

## Phase 0 — What this needs from M4b (in flight; not built here)

Almost nothing, and that is deliberate — the splitter derives its own structure:

- **Loop nesting comes from back-edge detection on the CFG**, not from the parser. The splitter **synthesizes its own preheaders** (splitting the loop's entry edge) and **splits its own critical edges**. No `RegionTable`, no preheader contract, no single-continuation contract.
- **The one requirement: mem2reg must NOT lower block args to edge copies.** Copies are inserted by SSA destruction, which runs **after** coloring. Eager edge copies give each phi target multiple non-dominating defs, destroying the SSA property the whole design rests on. `IrBlock.blockArgs` / `branchEdges` already exist for exactly this.

---

## The two rules that define the allocator

> **RULE 1 (SSA / single live-range start).** Every `ValueId` has exactly one def, and it dominates every point at which the value is live. The `Reuse` operand model is what makes this true at the Target tier — without it, `mov dest,lhs; add dest,rhs` writes `dest` twice and the tier is not SSA.
>
> This is what makes live ranges dominance-closed subtrees, hence the interference graph **chordal**, hence dominance-order greedy coloring **exact**: two values interfere iff one is live at the other's def, so each edge is enforced once at its later endpoint, and layout order is a perfect elimination order. After splitting, `maxlive ≤ pool` everywhere by construction, so **the colorer cannot fail** — a coloring failure is a compiler bug and asserts as one.

> **RULE 2 (spill placement).** A store or reload may be inserted at a point `q` only if, for every loop containing `q`, the value has **no use or def inside that loop**. Consequently spill code is never added to a loop body for a value that loop uses. Straight-line (depth-0) code has no such loop, so Belady eviction there is unrestricted — the reload executes once.
>
> If, after splitting out every value idle across loop `L`, the pressure inside `L` still exceeds the pool → **E5001**.

> <a name="rule-3"></a>**RULE 3 (the user always has a move).** There is **no escape hatch** — no attribute, no flag. `E5001` is final, and the code gets rewritten. That is only defensible if every value in a blocking set is one the user can actually *see and remove*, which makes the following an invariant rather than a nicety:
>
> **No compiler-introduced value may ever appear in an E5001 blocking set.** Witness tables, layout descriptors (M14's dictionary-passing generics), and refcount temporaries (M6) are pressure the user cannot see, did not write, and cannot delete. They are all either loop-invariant constant addresses (→ **rematerialize**, Phase 4.3) or tiny-lived temporaries. **If one ever blocks an allocation, that is a compiler bug, not user error** — the diagnostic must say so, name it, and be treated as a defect to fix rather than a message to tune.

**Ops emitted *after* coloring — `pushReg`, `popReg`, `xchgRegReg`, SSA-destruction copies — carry physical registers only and are invisible to Rule 1.** (`xchgRegReg` has two defs and would otherwise violate it; it is legal precisely because the allocator never sees it.)

---

## Phase 1 — Operand model + ISel

- **New** `Compiler/IR/Target/TargetOperands.maxon` — `Operand{vreg, kind, position, constraint}` and `targetOpOperands(op)`: **one exhaustive match, no `default`**, so a new `TargetOp` is a compile error rather than a silent miscompile. This replaces the ad-hoc "which fields are registers, and is this one read-modify-write?" knowledge that would otherwise be smeared across liveness, the splitter, and the colorer.
- **`TargetOpMeta`** grows `implicitUses` / `implicitDefs` `u16` masks (`ret` implicitly uses `{r8}`; `callDirect`/`iatCall` implicitly def the volatile set) — the extension point its own header already promises.
- **[`StdToX64Conversion.maxon`](maxon-shv2/Compiler/Targets/X64/StdToX64Conversion.maxon) stops pre-emitting the two-address `mov`.** It emits 3-operand ops with reuse constraints; the allocator materializes the copy iff the input outlives the op. Its current comments justify correctness with *"the M1 minimal colorer gives result, lhs, and rhs three DISTINCT registers"* — an argument that becomes **false** under register reuse, so it must be replaced, not tweaked.

## Phase 2 — The instruction set

Band placement follows the **band-append invariant** (append at the END of a band — a range arm silently swallows a mid-band insert).

| Op | Encoding | Operands | Why |
|---|---|---|---|
| `condJmp(cond, then, else)` | expands at emit to `jcc rel32` + `jmp rel32` — byte-identical | — | **Replaces `jcc`.** See below. |
| `leaRegRegReg(dest, base, index)` | REX.W `8D /r` + SIB | def `dest`; use `base`, `index` | **3-operand ADD** — `+` needs no copy and no reuse constraint at all. Sets no flags. |
| `leaRegRegImm32(dest, base, imm)` | REX.W `8D /r`, mod=01/10 | def `dest`; use `base` | `a + imm` / `a - imm` (negative disp) — **no register for the literal** |
| `imulRegRegImm32(dest, src, imm)` | REX.W `69 /r id` | def `dest`; use `src` | **3-operand IMUL** — no literal register, no copy |
| `subRegReg(dest, lhs, rhs)` | REX.W `29 /r` | def `dest` **reuses** `lhs`; use `rhs` | genuinely two-address on x64 |
| `imulRegReg(dest, lhs, rhs)` | REX.W `0F AF /r` | def `dest` **reuses** `lhs`; use `rhs` | two-address |
| `negReg(dest, src)` | REX.W `F7 /3` | def `dest` **reuses** `src` | two-address |
| `addRegImm32` / `subRegImm32` | REX.W `81 /0` / `81 /5` `id` | def **reuses** use | RMW fallback |
| `cmpRegImm32(reg, imm)` | REX.W `81 /7 id` | use `reg` | compare to a literal with no register |
| `storeSlotReg(slot, srcReg)` | REX.W `89 /r`, mod=10, SIB base=rsp | use `srcReg` | spill |
| `loadRegSlot(destReg, slot)` | REX.W `8B /r`, mod=10, SIB base=rsp | def `destReg` | reload |
| `pushReg` / `popReg` | `50+r` / `58+r` (+REX.B) | physical, post-regalloc | callee-saved → unlocks `rbx`, `r12`–`r15` |
| `xchgRegReg(a, b)` | REX.W `87 /r` | physical, post-coloring | breaks SSA-destruction copy cycles **with no scratch register** |

Group-1 immediate forms reuse the existing `emitModRmExt` primitive. `jcc` is **removed** from `TargetOp`: today `lowerCondBranch` puts it in `block.opRefs` as a **body** op with `jmp` as the terminator, so a two-successor block has **one append point** — after the `jcc`, on the else path only. Any edge copy placed there silently skips the then-edge, which is verbatim the v1 miscompile ARCHITECTURE names (*"a phi-copy trampoline that only one edge of a two-jump condition routed through"*). With `condJmp` every block has one terminator and `opRefs` is a body that is always safe to append to.

> **x64 encoding trap — exactly the class of silent miscompile v1 shipped.** In a SIB byte, `mod=00` with base low-3-bits `= 101` means *"disp32, no base register"*. So `lea dest, [base + index]` with base in **RBP or R13** must be emitted as `mod=01, disp8=0`. **R13 is in the allocatable pool**, so this fires in real code. Likewise index `= 100` means *"no index"*, so RSP can never be an index — assert it rather than trust it.

**Std tier** grows `binOpImm(resultId, lhs, imm, opcode)` and `cmpImm(resultId, lhs, imm, pred)` (arith band, appended). Std is also the machine-level tier — there is no MIR, and wasm reads it at M17 — so immediates belong there rather than being invented during x64 lowering. Two Std→Std passes feed them:
- **`foldConstOperands`** — canonicalize commutative ops so a constant lands on the rhs; rewrite const-rhs `binOp`/`cmp` to the imm forms (for a const *lhs*, swap and flip the predicate); DCE the now-unused `const` ops.
- **`foldConstants`** — evaluate `const ⊕ const`. **Land this AFTER the allocator's fragment review** (below).

**Pool (x64-windows): 14** — every GPR except `rsp`/`rbp`. `r8` is admitted *because* the implicit-mask modelling makes it safe (nothing is live across the `mov r8, retval` return move — `forbiddenPhys` proves it rather than assuming it). `rbp` is free too (no frame pointer) but held back for debuggability. The pool sits behind `allocatableRegisters(target)` and is **a property of the target**; arm64 declares its own (~27) when its backend lands.

## Phase 3 — Liveness + loop structure

**New `Compiler/Targets/Shared/TargetLiveness.maxon`.** Built for speed from the first commit — this is the pass that decides whether the allocator is fast.

- `FuncCfg` — `BlockId → blockRef` map, successors, predecessors, **built once per function**. (`IrModule.getBlockByIdIn` is O(blocks); calling it inside a fixpoint is O(B² × iters) — v1's trap in miniature.)
- **Loop nesting from back-edge detection** (reducible CFG → DFS back edges → natural loops → depth per block). Also **synthesizes preheaders** and **splits critical edges**.
- Live-in/live-out **iterated to a fixpoint** — not a single reverse pass, which suffices for a DAG and is *wrong* for M4b's back edges. Count and log the iteration count.
- A backward sweep retaining **only a fixed-arity kill record per op** plus running `maxPressure` — **never a materialized `liveOut` set per op** (O(ops × maxlive) memory, an allocation per op). It also accumulates `forbiddenPhys(v)` and **next-use distances** (which Belady needs).
- Asserts: no `Terminator.unset`; no `Terminator.fallthrough` (**zero producers** — panic, don't guess a next-block edge); no `ret` inside `opRefs`. `Terminator.dead` blocks (the parser emits them after a fully-terminated `if`/`else`) have no successors but **still hold ops that get emitted** — treated as ordinary blocks with empty live-out.

## Phase 4 — The splitter — highest risk, build and test this first

**New `Compiler/Targets/Shared/SplitLiveRanges.maxon`.** A Braun–Hack MIN sweep with a loop-aware working-set init, plus Rule 2's gate.

**Retire v1's failure first.** ARCHITECTURE records that v1 tried a one-shot MIN spiller and failed, because *"cross-block call-crossers cannot be relieved by splitting inside the def-block"* and *"spilling a crosser rather than splitting it re-creates a re-crossing reload cluster"* — concluding it needs a SplitKit-class cross-block splitter. The hard part of that is **SSA reconstruction**: a value reloaded on some paths but not others needs a phi at the merge.

> **We avoid it entirely with one restriction: every reload must DOMINATE the uses it serves.** Concretely, a reload is placed either (a) in the use's own block, or (b) in the preheader of the outermost loop containing its uses — both of which dominate what they serve. Then no merge ever sees two definitions, **no phi is ever needed, and there is no SSA reconstruction.** The price is occasional duplicate reloads (one per use-block rather than one per path) — and those are provably **cold**, because a reload inside a loop is precisely what Rule 2 errors on. **Prove this out on a cross-block spill before building anything else.**

Per function, in reverse postorder:

1. **At a loop header**: the entry working set is seeded with the values *used inside the loop*. If those alone exceed the pool → **E5001** immediately; that loop cannot be allocated without hot spill code. Values live *through* the loop but unused in it are kept only if there is room, and otherwise split out — store in the preheader, reload at their next use after the loop. **Hoist each to the outermost loop across which it is also idle** (walk the loop-nest parent chain), so a value idle across an outer loop is stored once outside it rather than once per outer iteration.
2. **At depth 0**: when pressure exceeds the pool, evict by **Belady/MIN — farthest next use** (the next-use distances come from Phase 3). Store, and reload per Rule 2's placement.
3. **Rematerialize instead of spilling — and this is load-bearing, not an optimization.** A value is *rematerializable* if it can be recomputed at a use in one instruction with no inputs: an integer constant (re-emit the immediate) or a **constant address** — which is what M14's dictionary-passing witness tables and layout descriptors are (`lea` from rdata). A rematerializable value **never holds a register across a gap and never touches memory**; it is simply re-emitted at each use, giving it a live range of zero.

   The reason this cannot be deferred is [RULE 3](#rule-3) below: there is no escape hatch, so any compiler-introduced value that occupies a register in a loop is pressure the **user cannot see and cannot remove**. Remat is what guarantees they always have a move.
4. **Each reload defines a fresh `ValueId`** and its uses are rewritten to it. This is what keeps the program in SSA and Rule 1 intact through spilling, so the colorer needs no special handling.
5. Allocate slots; write `func.stackSize` (16-byte aligned — `insertPrologueEpilogue` already emits `sub rsp, stackSize` when non-zero). Slot displacement is `slotIndex * 8` from `rsp` after the prologue's `sub`; `outgoingArgSize` is 0 today and is the one M5 seam.
6. Recompute liveness. **Assert `maxlive ≤ pool`.**

## Phase 5 — The colorer

**Rewrite** [`RegisterAllocator.maxon`](maxon-shv2/Compiler/Targets/Shared/RegisterAllocator.maxon) — delete `MinimalColorer`/`PoolIndex`/`VRegColorMap`/`callerSavedPool`. Per function: a **forward walk over a `u16` in-use bitmask**, seeded per block from `liveIn`. At each op, free the dying operands' bits first, then allocate for the def. Picking a register is `lowestClearBit(inUseMask | forbiddenPhys(v) | ~poolMask)` — one operation, no set iteration, no per-neighbour lookup, **no allocation in the inner loop**, no interference graph at any point.

- Honour `Reuse`: give the def the input's register, or insert the copy if the input outlives the op.
- Honour `FixedReg`.
- **Biased coloring (register hints) — a contract obligation, not an optimization.** Two copy-related values (a block arg and the value the back edge passes it; a `Reuse` def and its input) hold the *same* value but are counted as two live values. Left unhinted they get different registers, which (a) inflates pressure — a **false `E5001`** — and (b) makes SSA destruction emit a `mov` on the back edge, i.e. **a copy inside the loop**, which is exactly the hot-code cost this design exists to prevent. The fix is one line in the allocation step: at a def, try the hinted register *first*, fall back to `lowestClearBit` if it's taken. Hints come from block-arg/branch-arg pairs and from `Reuse` inputs — no separate coalescing pass, no extra data structure. Count `hintsHonoured` / `hintsMissed`.
- **Assert dominance order** — every use must already be colored. Today that holds only as an emergent property of the structured parser; the assert turns a future block-reordering pass from a silent miscompile into a panic.
- **Coloring cannot fail** (Phase 4 guarantees `maxlive ≤ pool`); a failure asserts as a compiler bug.
- **SSA destruction** runs after coloring: parallel copies on edges, cycles broken with `xchgRegReg`.

## Phase 6 — The checker

**New `Compiler/Targets/Shared/AllocChecker.maxon`.** Abstractly interpret the allocated function: track, per program point, `preg → vreg` and `slot → vreg`; at every use of `v` in `p`, assert the abstract state says `p` holds `v`; join states at merges. Run it under `spec-test` on every function. This catches clobbers, bad edge moves, bad spill/reload pairing, and reused-register aliasing — the class of bug that produces a plausible fragment and a wrong answer, which is exactly what the fragment diff will *not* catch.

## Phase 7 — The diagnostic

`E5001` must point at source, but spans die at the Maxon tier by design (`SourceRangeTable` is keyed by *Maxon* op index; those don't survive `lowerMaxonToStd`'s fresh module). `ValueId`s **do** survive verbatim Maxon → Std → Target, and function indices are 1:1 across all three tiers ([LowerMaxonToStd.maxon:130-138](maxon-shv2/Compiler/IR/Maxon/LowerMaxonToStd.maxon#L130-L138), `prepareSkeleton`). The missing link is `(funcIndex, ValueId) → Maxon OpIndex`:

- **New** `Compiler/IR/Maxon/ValueOrigin.maxon` — `ValueOriginTable`, **three dense scalar `int` columns**, following [`SourceRange.maxon`](maxon-shv2/Compiler/IR/Maxon/SourceRange.maxon)'s discipline for its stated reason (an `Array with` a `type` is an array of heap boxes; scalar columns are zero heap objects per value). Materializes a `SourceRange` only on the cold path.
- `maxonOpResultValue(op)` — exhaustive, returning a `MaxonOpResult` union (`none` | `value(id)`), **not** a sentinel `ValueId`.
- Recorded inside the **existing `emitOp`/`emitTerminator` choke points** in [`ParseStaging.maxon`](maxon-shv2/Compiler/ParseStaging.maxon), so a value cannot be minted without an origin — the same lockstep argument that protects `SourceRangeTable`. `mergeArtifact` folds it with the op/function offsets.
- `ErrorCode.registerPressure = "E5001"` — the `5xxx` "code emitter" band that v1's [`ErrorCode.maxon`](maxon-selfhosted/Compiler/ErrorCode.maxon) documents and no code yet occupies. `render()` needs no change; a multi-line message flows through.
- `buildBackend` gains a `project` handle and `throws CompileError`; [`Compiler.maxon`](maxon-shv2/Compiler/Compiler.maxon) prints diagnostics and throws a **fresh** `CompileError.compileError` (per the documented re-throw gotcha), mirroring `PassPipeline.run`'s gate.

There is **no escape hatch**, and the consumer is an **agent in a rewrite loop** — so this message *is* the remedy, and it has to let the agent converge in **one** iteration rather than by bisection:

- **State the exact deficit.** "Remove 2 values", not "you have too many". The agent should never have to guess-and-recompile to find the number.
- **Rank the candidates by cost to move.** The cheapest value to hoist into an array is the one with the **fewest uses inside the loop** (fewest loads after the rewrite). We already have use counts; sorting by them turns the message into a work list.
- **Report the *constrained* register count, not the nominal one.** A value live across a call can only sit in a callee-saved register — **7 on Win64, not 14**. "10 live, 14 available" is actively misleading to something that will act on it literally.
- **Name the transformation**, not just the diagnosis.
- **Assert no compiler-introduced value is in the blocking set** ([Rule 3](#rule-3)). If one is, this is a **compiler defect** and must be reported as one — an agent told to delete a value that is not in its source cannot converge.
- **Deterministic byte-for-byte.** Same program → same message, so the loop is stable. This is what the `maxoncstderr` spec block gates.

```
error E5001: the loop at hot.maxon:12 needs 2 more registers than exist
  16 values are live across the call at hot.maxon:15, and only 7 registers
  survive a call on x64-windows (the other 7 are clobbered by the callee)
  4 values idle across the loop were already spilled around it, at no cost
  inside the loop; spilling any of the remaining 16 would put a reload in
  the loop body.

  remove 2 of these from the loop, cheapest first (by uses inside the loop):
    hot.maxon:8:6    let scratch    1 use
    hot.maxon:9:6    let carry      2 uses
    hot.maxon:11:6   let sum        7 uses
    ...

  to fix: hold them in an array and index it inside the loop — array elements
  are never promoted back into registers, so the spill stays spilled.
```

A structured emission mode (`--diagnostics=json`) is a small, obvious follow-on for agent tooling; the text form above is the contract for now.

M6's ownership checker needs exactly this table to say *"moved here, used there"* — it is not regalloc-only infrastructure.

## Phase 8 — Specs, fragments, docs

- **New** `specs-shv2/register-pressure.md` — the exact `E5001` text (via a ` ```maxoncstderr ` block); a loop rescued *only* by idle-value splitting, whose fragment must show **nothing added to the loop body**; a loop that cannot be rescued; a straight-line function with many idle values that compiles via depth-0 Belady with reloads at the uses.
- **A two-address regression test** — `-` or `*` over **loop-carried, non-constant** values (a constant operand folds away). Under the `Reuse` operand model this should be structurally impossible; the test proves it. **Verify it fails against a deliberately-broken colorer**, so we know it has teeth.
- **Review the fragment diff, then land `foldConstants`.** Every current spec program is a constant expression, so full folding collapses them to `mov r8, <k>` and destroys the only artifact that shows what the allocator did.
- **Stats from the first commit.** ARCHITECTURE is emphatic that v1's "74%" stood for months with *zero sub-phase attribution* inside it. Counters matter more than the clock: `fixpointIterations` (total + max), `maxPressure` and where, `valuesSplit`, `reloadsInserted`, `rematerialized`, `registersActuallyUsed`, `copiesInserted`. Wall-clock via `Clock.elapsedMs` per backend stage — shv2 has **no timing infrastructure at all** today.
- **ARCHITECTURE.md's "Register allocator" section** is a full rewrite (it describes `MinimalColorer` and predicts a spilling allocator). Lead with Rules 1 and 2 and the regalloc2 lineage. Note that v1's MIN-spiller failure is retired by the dominating-reload restriction, not ignored.
- **[`IrBlock.maxon`](maxon-shv2/Compiler/IR/IrBlock.maxon) is three-quarters fiction** — `IrBlock.clone`'s doc describes *"the register allocator's spill-everywhere fallback"* (which will never exist; the function has **zero callers** — delete it); `BranchEdge`'s doc claims it is *"populated by lowerStdToX64"* and *"consumed by CopyResolution in the register allocator"* (both false — mem2reg populates it; SSA destruction consumes it *after* coloring); the `Terminator` doc references *"ApplyColoring identity-move elimination"*, which does not exist.

---

## Known limits — state these in ARCHITECTURE

1. **The only source of false positives is a wasted register, and copy-related values are the one that bites.** `maxlive` is *exact* for the program as lowered (chordal ⇒ `χ = ω = maxlive`), and liveness is per-program-point, so values live on disjoint paths correctly do **not** interfere. But two values that are *copies of each other* — a block arg and what the back edge passes it — hold the same value and are still counted twice. **Biased coloring (Phase 5) is what collapses them**, and without it a loop can be told it needs one more register than it does. Every other "waste a register" path is likewise a contract bug, not a limitation: a literal in a register (→ immediates, const folding), a witness table in a register (→ remat), a redundant two-address copy (→ `lea`, `Reuse`). Treat any of them showing up in a blocking set as a defect.
2. **Fixed-register points reduce the effective pool locally, and the pressure count doesn't see it.** A value live across a call is forbidden every caller-saved register, so its effective pool is the **callee-saved subset** (5 under shv2's ABI — see the corrections header; the body's "7" is standard-Win64), not the full pool. The **`idiv` (M5.4, BUILT)** is the milder in-family case: values live across it are forbidden **{RAX, RDX}**, so their effective pool is `pool ∖ {RAX,RDX}`. The splitter must treat each such point as a one-op pressure region against its *reduced* pool (idle values split out — same machinery), and `E5001` must report the reduced count, or the deficit is actively misleading. `maxPressure ≤ pool` is therefore NECESSARY but NOT SUFFICIENT at these points — the exact-deficit computation (M5.7) is per-point against the reduced pool, not the nominal one.

3. **There is always a rewrite — but sometimes it is a real restructuring.** Register pressure is always reducible by hand-spilling into memory: twenty accumulators become one array, and pressure collapses to `{base, index, temp}`. The floor is set by the most demanding single operation, which on x64 is 3–4 registers — far below 14. And mem2reg only promotes scalar `VarSlot`s, never array elements, so a hand-spill *stays* spilled. But the honest version of this is that the fix for a genuinely hot loop (a SHA-256 compression round wants ~24 live values) is the same restructuring a real implementation would already do — not a one-line tweak. **Say this in the docs.** A user who believes the compiler is being arbitrary will not go looking for the array.
4. **The allocator's real test surface doesn't exist until M4b.** Every program in `specs-shv2` today is a constant expression; module-wide maxlive is **3**, and after `foldConstants` it will be **1**. Non-constant values first appear as M4b's loop-carried phis, then M5's parameters.

---

## Verification

1. `bin/maxon.exe build maxon-shv2` — clean, no warnings.
2. `maxon-shv2/.maxon/maxon-shv2.exe spec-test` — run **from the repo root** (the spec dir resolves against cwd). All specs green, plus `register-pressure.md` and the two-address regression. **The checker (Phase 6) runs on every function in every test.**
3. `verify-warm-rebuild examples/basic.maxon` — exits 0. This matters more than usual: a liveness fixpoint or a working set iterating a hash-ordered collection is a classic source of run-to-run drift, and this gate is what catches it.
4. **Read the fragment diff line by line.** `SpecTestRunner` treats fragments as *outputs*, not gates. Confirm `+` emits a single `lea`, literals no longer occupy registers, and every register change is *reuse of a dead value's register* rather than aliasing of a live one.
5. **Run the binaries.** Fragments prove what was *emitted*, not that it *runs*. A loop that accumulates must produce the right sum.
6. **Exercise `lea` with an `r13` base** — the SIB `mod=00`/`base=101` trap. Force it with enough pressure to push a base operand into `r13`, and check the bytes with `llvm-objdump` (LLVM tools are at `llvm-project/bin/`).
7. **The pressure cliff by hand.** A loop at exactly `pool` live values compiles and runs. One value more, idle across the loop → compiles, with the store in the preheader, the reload in the continuation, and **nothing added to the loop body**. One value more that the loop *uses* → `E5001`, naming the loop, the count, and every def site.
8. `/code-review` before committing. Commit regenerated fragments *with* the compiler change.
