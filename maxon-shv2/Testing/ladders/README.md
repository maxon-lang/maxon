# Hand-built ladders — for the costs `scale-test` structurally cannot see

`scale-test` is the standing instrument and the thing whose trend `docs/optimization-log.md`
records. **These generators are not a replacement for it.** They exist because a scale corpus is
a list of shapes someone thought to generate, and every optimizer pass so far has had to build a
throwaway ladder to answer a question the corpus could not express — then thrown it away, so the
next pass built it again and the number in the log could never be re-run by anyone.

That last part is not hypothetical. The Wave 2 rung reported a headline of
`404,409 → 2,218,086` base bytes and recorded **no ladder**; when the optimizer rebuilt one, the
base column came out **~60× larger**. The rung had measured a different program, and nobody could
tell, because there was nothing to re-run. **A headline nobody can re-run is not a result.**

## The generators

Each writes one self-contained `.maxon` program to `<outfile>`. All but `genclosure.sh` call an
opaque `scaleOpaque(a int)` the optimizer cannot see through, so the call is a real clobber point;
`genclosure.sh` gets the same opacity from the closure call itself.

| script | shape | the question it answers |
|---|---|---|
| `gen.sh <N> <float\|int> <out>` | N units, **one** value live across one call | the Wave 2 headline: is a cross-call float force-spilled? The `int` form is the **control** — it must stay bit-identical across a change that only touches the float path. |
| `gen12.sh <N> <floatsPer> <out>` | N units, `floatsPer` floats live across one call | overflow **past** the callee-saved XMM half. With `floatsPer > 10` the spill is forced, so the splitter really runs and `growValueSpace` mints ids on every split. |
| `gen12i.sh <N> <intsPer> <out>` | the same, with ints | its control, and the one that shows the single-basic-block splitting quadratic is **file-agnostic**. |
| `genloop.sh <loops> <floatsPer> <out>` | the exact shape `ScaleCorpus.floatSpillSource` generates | ties a hand-ladder reading back to the corpus's own shape — the check that a pathological ladder is not being mistaken for the realistic case. |
| `genmutchain.sh <chains> <depth> <params> <out>` | `chains` chains of `depth` functions passing `params` arrays along; the last link writes them all | the parameter-mutation fixpoint (`SemanticCheck.buildParamMutationSummary`) and the label→position slotting. **Its three knobs are INDEPENDENT**, which is its whole point — see below. |
| `genemit.sh <funcs> <stmts> <ifs> <out>` | `funcs` functions of `stmts` straight-line statements and `ifs` two-way branches each | the ENCODE phase's shape. **Its three knobs are INDEPENDENT**: function COUNT, ops per function, and BLOCKS per function. This is the ladder that found the `chunkLabelOffsets` quadratic — funcs-doubling and stmts-doubling both read a flat ×2.00 while ifs-doubling read ×3.17, which located the term in blocks-per-function and nowhere else. |
| `genblocks.sh <n> <blocks\|funcs\|straight> <out>` | the same block count as ONE big function, as `n` small ones, or as one straight-line block | separates **blocks per FUNCTION** from **functions per module** — two axes `ScaleCorpus` doubles together and so cannot tell apart. It reached the SAME `phase:encode` quadratic as `genemit.sh` from the other side (×3.21 ×3.37 ×3.69 on `blocks`, ×2.0 flat on both controls), which is why both survive: one varies the branch count that mints blocks, the other holds a block count fixed and RESHAPES where those blocks live. |
| `genclosure.sh <closures> <captures> <reads> <ranged\|plain> <out>` | `closures` functions, each with one closure capturing `captures` bindings read `reads` times each | **the only way to measure the capture path at all** — `ScaleCorpus` states outright that it generates no closures, so every `scale-test` column reads Δ0 for a change to it. Its three knobs are independent for `genmutchain.sh`'s reason; see below. |
| `geninstances.sh <instances> <chain> <plain\|contested\|control> <out>` | `instances` generic instantiations, each with its own type argument; optionally a `chain` of `__`-prefixed declarations planted against instance 0 | the COMPILED-NAME path — `ProgramSignatures.mangleGenericInstance` and the `reservedIfDeclared` re-probe. Unlike closures the corpus *does* generate generics, so `scale-test` sees the per-instance cost; what it cannot express is a compiled name a DECLARATION also claims, or a re-probe deeper than one. See below. |
| `genawait.sh <funcs> <ifs> <out>` | `funcs` await-bearing functions of `ifs` two-way branches each, the promise spawned before the thicket and awaited after it | the AWAIT-LINEARITY walk (`SemanticCheck.checkLinearAwaitInFunction`) and its per-function block table. `ScaleCorpus` lists async under **NOT GENERATED**, so this is the only way to measure it — see below. |

### `geninstances.sh` — the two things the corpus cannot claim a Δ0 about

`ScaleCorpus` generates generics (its instance count is `3 + 12×2^rung`), so a per-instance cost is
visible on the standing ladder and a Δ0 there means something. **Two neighbouring costs on the same
path are still structurally invisible to it, and always will be:**

- **A CONTEST** — an instance whose compiled name is also a declared `type`/`enum`/`union`, the only
  thing that makes `reservedIfDeclared` mint anything. `contested` declares `type Box_A<i>`, exactly the
  string instance `i` compiles to; `control` declares `type Zed_A<i>`, the same bytes claiming nothing.
  **Their difference is the contest and nothing else** — the extra declarations' own sweep cost cancels.
  Measured 2026-07-26, 64…2048 instances: `phase:signatures` **+4 allocations and +198 bytes per
  contested instance**, flat per instance across the whole 32× span, both columns ×1.98.
- **A RE-PROBE DEEPER THAN ONE**, which needs a declaration whose own name starts with `__` — E2051. So
  `<chain>` above 0 emits a program that **cannot compile**, and that is the point rather than a defect:
  it is the only shape in which the loop iterates more than twice, which is what makes *"at most two
  probes on anything that compiles"* a property and not a hope. `--metrics` is not written for a failed
  build, so time `contested` against the byte-identical `control` at the same `<chain>` with a wall clock
  (min of 5) and read the DIFFERENCE. Measured at chain 128…2048 (40 KB…8.5 MB of source): **−1 / 0 /
  +4 / +14 / +64 ms**, i.e. ~7×10⁻⁶ ms per source byte and FLAT — the re-probe is linear in program size
  even on the shape built to break it, because each extra probe costs a declaration that is itself two
  characters longer.

### `genclosure.sh` — why the capture path needs its own ladder

`ScaleCorpus`'s manifest lists closures under **NOT GENERATED**, so a Δ0 from `scale-test` on a
capture-path change is the instrument's blind spot and not a result. The three knobs separate the
three costs that live there, which a single doubling ladder would sum into one column:

- `<closures>` alone is program size — `genclosure.sh 128…1024 4 4 ranged` reads **×1.98 ×1.99
  ×1.99** in parse allocations, which is what "the capture path is linear" means.
- `<captures>` at **constant read sites** (`64 <c> $((4096/(64*c))) ranged`) isolates
  `Parser.captureSlotFor`, which scans `captureNames` linearly on every read, so its work is
  `sites × captures/2` while the program size stays put.
- `<reads>` at fixed `<captures>` moves the number of `emitCaptureRead` sites, i.e. how often
  `fieldStorageType` resolves a slot's width through the whole-program alias registry.

⚠ `<captures>` is bounded by the language, not by taste: a function may declare at most **64
argument slots** (E2015), so a ladder past `genclosure.sh 1 64 …` does not compile.

### `genawait.sh` — why the await walk needs its own ladder, and why two knobs

`ScaleCorpus`'s manifest lists async under **NOT GENERATED**, so a Δ0 from `scale-test` on the
await-linearity path is the instrument's blind spot and not a result. The two knobs separate the two
costs the analysis has, which one doubling ladder would sum into a single column:

- `<funcs>` at fixed `<ifs>` is the **control** — it moves only how often the `functionHasAwait` gate
  opens, i.e. the per-function term.
- `<ifs>` at fixed `<funcs>` holds the function count still and moves BLOCKS PER FUNCTION, which is
  what loads the per-function block table (`IrModule.blockIndexById`) without touching anything else.

That separation is what read the table conversion of 2026-07-26. On the `<ifs>` axis at `funcs=24`,
`phase:semanticCheck` allocations went **1,864 → 2,056 → 2,248** (ifs 100 → 200 → 400) before the
change — **+192 per doubling of the block count, which is 8 per function**, the `Map`'s rehash chain
(one `grow()` per capacity doubling, and a `grow` builds four fresh columns). After it they are
**FLAT at 712** across all three, because a pre-sized dense table is two allocations per function
whatever the block count. The `<funcs>` control stayed linear on both sides (600 / 1,148 / 2,248
before, 216 / 380 / 712 after).

⚠ **Compile it with `--target=x64-windows` on a non-x64 host.** The green-thread substrate an `async`
lowers to is x64-windows-gated at this rung, so an arm64/wasm build panics in the BACKEND — after the
front-end phase this ladder measures, but with no `--metrics` file written.

### `genmutchain.sh` — why three knobs and not one

A ladder that doubles functions, call sites and depth **together** cannot tell a depth term from a
size term: both read ×2. Hold `chains × depth` **fixed** and vary `depth` and the program size barely
moves while the fixpoint's iteration depth changes by 1000×, so anything that bends is depth. Vary
`params` alone and you move the number of **bits** a mask gains — how many times a function can
re-enter the worklist. The three readings that matter:

- `for d in 2 4 … 2048; do genmutchain.sh $((2048/d)) $d 1 out.maxon; done` — depth at constant size.
- `genmutchain.sh <N> 2 1` — size and call sites at constant depth.
- `genmutchain.sh 1 256 <P>` — mask width. **This is the one that found the `argSlotPosition`
  quadratic**: every argument is labelled, so it is also the ladder for the label→position mapping.

### `genemit.sh` — and why the CONSTANT-SIZE reading is the decisive one

`genemit.sh 64 0 32` … `genemit.sh 2 0 1024` holds `funcs × ifs` FIXED, so the program size, the total
block count and the total op count are all constant and only the SPLIT moves. A cost that is quadratic
in one function's block count then reads as a straight RISE across that series while the memory columns
sit still — which is exactly what `phase:encode` did (29.2M → 76.0M CPU ticks at a constant ~96 KB of
source), and what it stopped doing once the per-function `BlockId → offset` map became a dense array.
A ladder that grows the program cannot make that distinction: everything doubles there, including the
thing you are trying to hold still.

## Reading one

```
<compiler> build out.maxon -o out --metrics=m.txt
awk -F'\t' '$1=="regalloc" && $2=="splitting"' m.txt      # nanos, allocs, frees, bytes
```

Compare a **base** binary against **head** at equal-length checkout paths — path length is a real
constant in the byte column (two runs of identical content once differed by exactly 546 bytes
because one worktree path was 14 characters longer).

## What a reading means

**The ladder DOUBLES, so the ratio between consecutive rungs IS the growth**: ×2.00 linear,
×4.00 quadratic, read straight off. Do not fit an exponent — the doubling already gives you the
answer, and a fit is interpretation dressed as measurement.

⚠ **Allocations and bytes are bit-for-bit reproducible; time is not.** Time is worth reading
interactively (a quadratic in *time* with linear allocations is exactly the shape `scale-test` is
blind to, and two have been found that way), but never trend it — a loaded box in July against an
idle one in August compares nothing.

⚠ **These ladders are pathological on purpose, and that cuts both ways.** A curve that bends here
is a real cost the compiler can be made to pay, but it is not automatically a cost anyone pays:
the single-basic-block splitting quadratic filed against P1.7 bends at ×3.99 on `gen12i.sh` while
`genloop.sh` — the corpus's own shape — reads ×2.02 out to 256 loops. **Report both**, and say
which one a real program looks like.
