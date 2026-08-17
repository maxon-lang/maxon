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
| `gen12i.sh <N> <intsPer> <out>` | the same, with ints | its control, and the one that shows the single-basic-block splitting quadratic is **file-agnostic**. ⭐⭐ **AND IT IS THE ONLY LADDER THAT REACHES A LARGE BLOCK IN *CONFINED* MODE — use `intsPer = 8`.** At `intsPer ≤ 10` **every split is confined** (`valuesSplit` == `forced at fixed-reg points`: 159/159, 319/319, 639/639 at N = 32/64/128); at 12 it starts mixing in full-pool splits (214 splits, 191 forced). That window matters because the two ladders either side of it each miss the shape by ONE AXIS: `genwidelive` is a big block but sits in FULL-POOL mode for ~386 of its 396 splits, so Hall barely runs; the scale corpus IS confined from its first analysis but its blocks are small, so the O(block) confined walk is tiny. **Both read LINEAR and both are blind.** On this knob the confined path measures allocations **×3.88 then ×3.94** (CPU ×4.11 then ×4.42) — quadratic in the exact column, and A/B'd PRE-EXISTING against `d28583442` (×3.98 / ×4.14). Observe confinement directly with `--log=codegen:debug` and read `forced at fixed-reg points`. |
| `genwidelive.sh <N> <sum\|dead> <out>` | N call results **all live at once** in ONE straight-line block | the shape `specs-shv2/x64-large-frame-arg7.md` is the N=800 rung of, and the one where the splitter's **two Θ(N) terms multiply**: liveness is Θ(N) at the last call, so the SPLIT COUNT is Θ(N) too and each split re-derives a Θ(N) block. **Its two knobs are the SHAPE and its CONTROL**, not two axes: `sum` feeds all N values into one N-term sum at the end (Θ(N) splits — `valuesSplit` is 96 / 196 / 396 at N = 100 / 200 / 400); `dead` folds each result into an accumulator on the next statement, so at most two values are live and **nothing is ever split**. They differ ONLY in where the results are consumed, so parse/lowering/isel cost the same and the DIFFERENCE between their `regalloc:splitting` columns is the splitting cost alone. Both modes return **7** at every N, so a rung that miscompiles shows up as a wrong exit code and not merely as a time. |
| `genrespace.sh <N> <out>` (`WIDE=` env, default 12) | N ALTERNATING pressure humps in one block, so each valley carries one hump's reloads and the next hump's stores | the only shape that exhausts a gap of the splitter's positional index and fires `PressureIndex.makeRoom`. `genwidelive.sh` cannot: its single hump puts every reload after the peak and every store before it, so the two never share a gap, and it measures ZERO re-spaces at every N. This one puts THREE ops in a gap sized for two and so re-spaces at the shipping `SlotGap = 4` — which is how the crash of 2026-07-27 was found (a re-space moved ops whose slots `SplitEdits` had already recorded, and nothing repaired them). Returns **7** at every N. ⚠ `WIDE` has a NARROW window and must be EVEN (the generator rejects odd). Measured at N=24: 8 fires zero re-spaces (it still splits 141 values — it is the GAP it never exhausts), 10 fires 23, 12 fires 46, 14 fires 63, 16 is a legitimate E5001. The upper edge FALLS as N rises, since every hump's results stay live to the end. Re-check both edges against any change to the register file size. |
| `genloop.sh <loops> <floatsPer> <out>` | the exact shape `ScaleCorpus.floatSpillSource` generates | ties a hand-ladder reading back to the corpus's own shape — the check that a pathological ladder is not being mistaken for the realistic case. |
| `genmutchain.sh <chains> <depth> <params> <out>` | `chains` chains of `depth` functions passing `params` arrays along; the last link writes them all | the parameter-mutation fixpoint (`SemanticCheck.buildParamMutationSummary`) and the label→position slotting. **Its three knobs are INDEPENDENT**, which is its whole point — see below. |
| `genemit.sh <funcs> <stmts> <ifs> <out>` | `funcs` functions of `stmts` straight-line statements and `ifs` two-way branches each | the ENCODE phase's shape. **Its three knobs are INDEPENDENT**: function COUNT, ops per function, and BLOCKS per function. This is the ladder that found the `chunkLabelOffsets` quadratic — funcs-doubling and stmts-doubling both read a flat ×2.00 while ifs-doubling read ×3.17, which located the term in blocks-per-function and nowhere else. |
| `genblocks.sh <n> <blocks\|funcs\|straight> <out>` | the same block count as ONE big function, as `n` small ones, or as one straight-line block | separates **blocks per FUNCTION** from **functions per module** — two axes `ScaleCorpus` doubles together and so cannot tell apart. It reached the SAME `phase:encode` quadratic as `genemit.sh` from the other side (×3.21 ×3.37 ×3.69 on `blocks`, ×2.0 flat on both controls), which is why both survive: one varies the branch count that mints blocks, the other holds a block count fixed and RESHAPES where those blocks live. |
| `gennest.sh <depth> <stmtsPerLevel> <outfile>` | one function, `depth` NESTED `while` loops with `stmtsPerLevel` assignments in each — and the two knobs are held so `depth × stmtsPerLevel` can be kept FIXED (`4 128` and `8 64` are the same size) | `Parser.assignedBindingsIn`, which scans every token between a loop's header and its `end` once per loop — so a loop nested inside another has its tokens re-scanned by every enclosing loop, Θ(depth × tokens) in TIME while allocating one small set per loop. `ScaleCorpus`'s `loopNest` knob predicted exactly this in writing and predicted the memory-only instrument would read it linear anyway; this is the ladder that separates the depth term from the size term, which a ladder doubling both cannot. ⭐ **Every level guards on the SAME `total`**, deliberately: a per-level counter is one more value live across every enclosing loop and the allocator REFUSES rather than spills inside a loop (E5001) at depth ~14 — which is a fact about register pressure, not about the token scan. The loops trip ZERO times (`while total < 0`), so the program is cheap to run; the ladder is about COMPILE cost. |
| `genclosure.sh <closures> <captures> <reads> <ranged\|plain> <out>` | `closures` functions, each with one closure capturing `captures` bindings read `reads` times each | **the only way to measure the capture path at all** — `ScaleCorpus` states outright that it generates no closures, so every `scale-test` column reads Δ0 for a change to it. Its three knobs are independent for `genmutchain.sh`'s reason; see below. |
| `geninstances.sh <instances> <chain> <plain\|contested\|control> <out>` | `instances` generic instantiations, each with its own type argument; optionally a `chain` of `__`-prefixed declarations planted against instance 0 | the COMPILED-NAME path — `ProgramSignatures.mangleGenericInstance` and the `reservedIfDeclared` re-probe. Unlike closures the corpus *does* generate generics, so `scale-test` sees the per-instance cost; what it cannot express is a compiled name a DECLARATION also claims, or a re-probe deeper than one. See below. |
| `genawait.sh <funcs> <ifs> <out>` | `funcs` await-bearing functions of `ifs` two-way branches each, the promise spawned before the thicket and awaited after it | the AWAIT-LINEARITY walk (`SemanticCheck.checkLinearAwaitInFunction`) and its per-function block table. `ScaleCorpus` lists async under **NOT GENERATED**, so this is the only way to measure it — see below. |
| `genwitness.sh <conformers> <methods> <dispatch\|inert> <out>` | `conformers` types against a `methods`-method interface, every one dispatched through ONE shared `where T is Digest` generic body | the WITNESS-TABLE RELOCATION path — every `.rdata` slot that names a function, and the walks over `GlobalDataTable.pendingRdataRelocs` that bake and check them (`StdToWasm.bakeFuncTableIndexRelocs`, `StdToWasm.requireIndirectlyReachableParamsAreMachineWords`, `CodeResult.bakeFuncAbs64Relocs`). `ScaleCorpus` generates **no interface and no witness dispatch**, so that list is EMPTY at every rung of the standing ladder and every column reads a Δ0 that means *unreached*. See below. |
| `genwitnessargs.sh <conformers> <methods> <args> <decl\|reverse> <out>` | the same shape as `genwitness.sh`, but every interface method takes `args` LABELLED parameters | the ARGUMENT LIST behind a witness call's parentheses (P1.7a slice 2b-vi) — `parseWitnessMethodOnValue` → `parseCallArgs` → `slottedWitnessArgs` → `slotCallArgs` → `argSlotPosition`. `genwitness.sh` holds that arity at ONE, so it cannot see this axis at all; `ScaleCorpus` has **zero `where`** in it and cannot see either. `decl`/`reverse` is the LABEL-ORDER control. See below. |
| `genfsprobe.sh <iterations> <out>` | ⚠ **the odd one out: a program to RUN, not one to compile** | what one `File.delete` / `File.exists` / `FilePath.changeExtension` COSTS, in nanoseconds and in allocations — so a per-compile cost paid in SYSCALLS can be priced at all. See below. |
| `genfor.sh <loops> <depth> <accesses> <array\|range\|noloop> <out>` | `loops/depth` functions, each one NEST of `depth` `for` loops with `accesses` binding accesses per level | `for … in` (P1.8 slice A) and the four doors of its ITERATION LOCK. `ScaleCorpus` generates **no `for` at all** — the construct did not parse until that commit — so every column of a default run reads a flat Δ0 for it. **Its knobs are independent** (program size is `loops × accesses`, so `depth` moves alone) and `noloop` is the CONTROL: the same accesses through the same doors with not one `for` in the program. See below. |
| `genborrow.sh <K> <pending\|subjects\|dead\|noborrow> <out>` | `K` borrowed array elements in ONE function body — as unclaimed PENDING borrows, as K distinct SUBJECTS, as K borrows against K writing SITES, or (`noborrow`) the same access count through the same doors with not one borrow in the program | E3070 BORROW LIVENESS and the four per-function structures behind it. ⚠ **The corpus is not blind here — it is NARROW, which is worse, because it reads as coverage**: `v_marray.maxon` mints one borrow per managed-Array group, so `BorrowCheck` runs and reports a healthy non-zero at every rung, but every subject carries exactly ONE borrow and every accessor is bound to a name, so the two product terms are structurally unreachable. ✅ **Two quadratics found and CLOSED here**: `pending` (nothing dropped a `PendingBorrow` until the function's `end`, and both `retargetPendingBorrow` and `attachPendingBorrows` re-walked the list — ×4.2 ×3.8 ×3.1, cured with a per-statement watermark) and `dead` (`reportFirstLiveBorrow` walked every borrow of the subject a site writes — ×3.8 ×3.8 ×4.1 ×4.0, the cleanest of the four, cured with a per-subject cursor). ◑ `subjects` MEASURED AND LEFT (`borrowSubjectIdOf` is a linear scan of `BorrowFacts.subjects`: fitted `aK + bK²` gives a = 15,697 and b = 36.2 ticks, so the quadratic does not equal the linear term below K ≈ 434 distinct subjects in ONE body). `noborrow` is THE CONTROL and the reading that matters most — allocations IDENTICAL TO THE DIGIT at every K, parse CPU +5.5%…+7.0% spread over constant factors no per-function attribution can separate. **Everything goes in ONE function** deliberately: every term is per-function, so widening the function count would divide K by the very thing it doubles. |
| `genstring.sh <n> <sites-*\|data-*> <out>` | ⚠ **TWO families, and `<n>` means a different thing in each**: `sites-*` is a COMPILE ladder (`<n>` = method CALL SITES, four per function); `data-*` is a RUN ladder (`<n>` = DOUBLINGS of the subject string, the program timing only the operation and printing a CSV line) | the seven byte/ASCII `String` methods (P1.8 Slice C). `ScaleCorpus` emits **not one** of `startsWith`/`endsWith`/`contains`/`toLower`/`toUpper`/`replace`/`split` at any rung — dump it and the complete method-call inventory is `create push count get append scaleBy probe byteLength slice reserve clone firstVal` — so the family is structurally invisible to a default run. ⚠ **It is NOT blind to `String`**, which is the trap: 756 `==` sites plus `append`/`byteLength`/interpolation across the six rungs mean a Slice C change to the SHARED `__str_eq` loop DOES read, and that non-zero looks like coverage of a rung it does not cover. `sites-control` is the CONTROL (the P1.2 surface only, no Slice C method), and `data-appendloop` measures the P1.2 `append` this generator has to route around. See below. |
| `genstring-grapheme.sh <n> <sites-*\|data-*> <out>` | ⚠ **TWO families, `genstring.sh`'s twin in shape**: `sites-iter\|count\|charlit\|bytes\|control` is a COMPILE ladder (`<n>` = CALL SITES); `data-iter-*`, `data-count-*`, `data-countloop`, `data-bytes` are RUN ladders (`<n>` = DOUBLINGS of the subject string, the program bracketing only the operation and printing a CSV line) | `Character`, UAX#29 GRAPHEME SEGMENTATION, `String.count()` and `for c in s` (P1.8 slice B). ⛔ **Every last piece is invisible to `ScaleCorpus`, measured not assumed**: at rung 5 its 1,024 `.count()` sites are ALL `Array`/`Set` receivers and not one is a `String`, `.bytes()` is zero, `for … in` is zero in any form, and all 53,766 single-quoted tokens are BLOCK LABELS — so the `charLiteral` LEXER path is hammered while the literal-directed TYPING path (`integerizedOperand`) is never entered. ⚠ **And the converse**: the corpus is NOT blind to `String`, so a reading that moves can still be real — it just cannot be about the grapheme surface. `sites-control` is the CONTROL that prices the rung against a program with no `Character` in it (`recordGraphemeUsage` runs eight `ByteArray.equals` on EVERY call op in the module, and `parseBinary` asks `integerizedOperand` about both operands of EVERY binary operator). READ: compile LINEAR in every mode (×1.85…×1.99); run, per cluster at the top rung, `for c in s` 57.5 ns ASCII / 88.6 ns 2-byte / 255.6 ns ZWJ family, `count()` 3.4 / 32.4 / 172.0 ns, every ladder ×2 per doubling. ⛔⛔ **It also found a BOOTSTRAP defect: the C# compiler miscompiles the SECOND `s.append(s)` after the buffer grows** (content replaced by fill; shv2 is right) — which applies to `genstring.sh` and `genstringviews.sh` too, since all three build their subject that way. |
| `genfnval.sh <aliases> <sites> <arity> <indirect\|direct> <out>` | `aliases` function typealiases with a matching callee each, called through a function VALUE `sites` times per alias, plus a `return` door and an argument door per alias | FUNCTION VALUES and their DECLARED TYPES — `resolveFunctionAliasShapes`, `declaredFunctionShapeOf`, `checkIndirectCall`, `checkFunctionTypeDoors` and `indirectCalleeParamTypes`. `--emit-corpus` finds **ZERO `typealias = function(…)` and ZERO function references** in all 465 generated files, so every column of a default run reads a flat Δ0 for the whole mechanism. `direct` is the CONTROL: the same declarations, bodies and CALL COUNT, called by NAME. See below. |
| `gentrim.sh <n> <sites-*\|data-*\|edge-*\|loop-*> <out>` | ⚠ **FOUR families, and `<n>` means a different thing in each**: `sites-*` is a COMPILE ladder (`<n>` = CALL SITES); `data-*` is a RUN ladder on the STRING LENGTH (`<n>` = DOUBLINGS of the subject); `edge-*` is a RUN ladder on the MATCHED RUN (`<n>` = doublings of a trimmed pad against a FIXED 8-byte body); `loop-*` is a RUN ladder on the TRIP COUNT | `CharacterSet`, the UCD `General_Category` table and the three `String` trims (P1.8 Slice D). `ScaleCorpus` emits **zero** of `trim`/`CharacterSet`/`CharSet`/`Set with Character`/`\uXXXX` at any rung — measured by `--emit-corpus`, and this is the SIXTH consecutive rung with that property — so the whole surface is structurally invisible to a default run. **It is the ladder that found `trimStart` walking the entire string to answer a question it settled at the first kept cluster** (56.2 ms and 45 MB to trim nothing off 512 KB). `sites-control` is the CONTROL and gave the rung's cost to a program that never uses it: **+14 allocations FLAT** over a 16× span. ⚠ `SITES_PER_FN` is an ENV OVERRIDE, and it is what separates a per-FUNCTION term from a per-SITE one — hold `<n>` and move it. ⚠ `data-trim-supp` is the ONLY mode that reaches `__ucd_cat`'s 806-entry binary search; the other seeds all take the direct BMP byte load. See below. |
| `genstringviews.sh <n> <sites-*\|data-*\|loop-*> <out>` | ⚠ **THREE families, `gentrim.sh`'s twin in shape**: `sites-*` is a COMPILE ladder (`<n>` = CALL SITES); `data-*` is a RUN ladder on the STRING LENGTH; `loop-*` is a RUN ladder on the TRIP COUNT at a fixed tiny subject | `toByteArray` / `clone` / `codepoints` / `utf16` / `isEmpty` / `replaceFirst` / `String.from(bytes)` and the nine whitelisted `utf16*` free functions (P1.8 slice E). ⛔ **`ScaleCorpus` emits ZERO of each — the SEVENTH consecutive rung with that property** — and `.clone()` is THE TRAP IN REVERSE: the corpus has 256 of them and **every receiver is an `Array`**, so a moved number cannot be about this rung. ⭐ **`data-*` settles a real O(n)-vs-O(n²) question**: all three views are PUSH-ONLY loops into an array the parser created with NO reserve, so the whole cost rests on `__managed_grown_cap` being geometric — if a push ever reallocated to the exact required length every one would read ×4 per doubling, exactly as `__str_append` used to. ⚠ **`SEED` is a REAL KNOB for `codepoints`/`utf16` and INERT for `toByteArray`/`clone`**, and the asymmetry is the point: a byte copy cannot see an encoding, while `SEED=supp` is the ONLY seed that reaches `utf16`'s SURROGATE-PAIR arm (two units per codepoint) — a ladder without it has not measured that arm. `loop-*` prices the four new per-trip owned allocations against `loop-control`; unlike slice D's `loop-trim-shared` there is nothing to HOIST, because a materialized view IS the per-call allocation. |
| `genwhitelist.sh <n> <synth\|real> <outdir>` (`MODULES=`, `DECLS_PER_MODULE=`, `FUNCS_PER_FILE=` env) | `n` corpus functions spread over files, plus `MODULES` extra modules shaped like `stdlib/helpers/string/utf16.maxon` and NEVER CALLED — `synth` generates them, `real` copies the actual stdlib file with every name suffixed | does `phase:parse` cost O(user corpus × modules loaded), or O(user corpus) + O(modules)? ⚠ **The shared ladder cannot ask it, and not for the usual reason**: the corpus DOES exercise the cost (every rung loads the same 3 whitelisted modules) but it can only ever move ONE axis, and a product term and a sum term are indistinguishable when one factor never varies. ⭐ **It matters far more than today's number** — `StdlibLoader`'s own header says the whitelist "IS DESIGNED TO GROW … until it names every file there" (49 files, 3 listed), so a product term would turn a 0.005% tax into a genuine superlinearity, and the cheapest moment to find that is while the list is short. ⭐⭐ **VERDICT: A SUM, and the reading is EXACT rather than approximate** — extra `phase:parse` allocations vs `MODULES=0` came out IDENTICAL TO THE ALLOCATION at every corpus size: +1,914 / +7,656 / +30,632 / +122,512 at `MODULES` 1/4/16/64, the same at C=128, 256 and 512 AND on the real shared corpus at rung 0 and rung 5 (a 32× span). The REAL whitelist entry behaves identically, measured three-binary (`head − nowl` = **+1,654 parse allocations at every one of the six rungs**, FLAT). ⚠ The documented `O(files × types)` hazard was tested separately with modules carrying array literals and is also absent — the only cross term found has the WRONG SIGN (an extra module gets marginally cheaper as the program grows). |
| `genscope.sh <constructs> <locals> <if\|ifelse\|while\|match\|straight> <out>` | ONE function with `locals` scope-filler `var`s in scope and `constructs` merging statements after them, each assigning a BOUNDED two of six accumulators | THE PARSER'S SCOPE DIMENSION — mutable bindings IN SCOPE (V) against the CONSTRUCTS that merge them (C), which `ScaleCorpus` doubles TOGETHER (`LocalsPerFunctionBase` and `LongIfsBase`/`DeepBlocksBase`) and therefore cannot tell apart. It is what separated the `phase:parse` bend of 2026-07-28: **linear in C alone, linear in V alone, quadratic in the two together** — an O(V×C) term, which the `straight` CONTROL (same V, same statement count, no merging construct) then pinned to the construct rather than to program size. See below. |
| `genfloathash.sh <sites> <hash\|equals\|inthash\|control> <out>` | `sites` direct builtin-conformance call sites on a FLOAT receiver, eight per function so doubling `<sites>` doubles the FUNCTION COUNT and leaves each function's register pressure fixed | P1.7a's last slice (`float` is `Hashable`) and the parse-time lookup that routes `<float>.<method>()` — `builtinConformerMethod` -> `requireBuiltinInterface`. `ScaleCorpus` emits **not one** `.hash()`/`.equals()`/`.compare()` on a float receiver at any rung, so the install gate never fires, `buildFloatHash` is never built and `movqGprXmm` is never encoded: every column of a default run reads a Δ0 that means UNREACHED. ⭐⭐ **`equals` and `inthash` compile under the PRE-rung compiler too, so this is one of the few ladders that supports a true A/B on a byte-identical file** — and `equals` is the mode that found the +10 allocations/site this rung costs a surface it never touched. `inthash` and `control` are the controls. See below. |
| `gendirectives.sh <n> <regions\|plain\|dead\|nest\|flat\|cond\|parens\|files\|filesplain> <out>` | `n` `#if` regions, or `n` files, or one region nested `n` deep, or one condition of `n` atoms — one axis per mode, in four CONTROL PAIRS | `#if`/`#else`/`#endif` CONDITIONAL COMPILATION (D5) and the token-tier filter that resolves it — `filterConditionalTokens`, its frame stack, and the `queryActiveTokens` memo both the declaration sweep and the real parse read. ⛔⛔ **`ScaleCorpus` emits ZERO directives** — measured, `--emit-corpus` wrote 465 files across rungs 0-5 and greps for all three directive spellings return NOTHING — so `phase:directives` on a default run is EXACTLY `files + 3` (30/35/45/65/105/185 files against 33/38/48/68/108/188 allocations) and **not one token was ever filtered while that column was taken.** A flat row there means UNREACHED, not free. **Four control pairs, and each answers a different question the others cannot:** `plain` is the directive-free control whose SURVIVING PROGRAM IS IDENTICAL to `regions`', and its directives phase is **FLAT AT 5** — that is what prices a directive-free file at nothing. `nest`/`flat` is the product-axis pair and reads **4,130 vs 4,119, indistinguishable**, which is how the frame stack was shown O(1) — depth is a top READ, not a walk. `files`/`filesplain` separates per-file from per-region (10 allocations/file with a region, 1 without). `dead` against `regions` prices SKIPPING at ×1.99 with 139,616 B against `regions`' 2,207,504 B — skipping is proportional to what is skipped, and skipped tokens cost ~1/16th of parsed ones. `cond` and `parens` both read **FLAT at 14** allocations: the evaluator deliberately does NOT short-circuit (so the cursor lands deterministically whatever the result), and this is the measurement showing that choice costs exactly its atoms and nothing per atom. ⚠ `parens` stack-overflows between depth 1024 and 2048 — **the CONTROL fails identically** (an ordinary expression with 2048 nested parens overflows `Parser.current` the same way), so that is the compiler's uniform recursive-descent ceiling and NOT a directive defect. A region costs ~2 allocations whatever it contains (one `ConditionCursor`, one `DirectiveFrame`). |
| `genmm.sh <n> <buffer\|array\|trythrows\|trynone> <out>` | `n` functions either on the `__ManagedMemory` BUFFER surface or on a plain `IntArray` with the same call and `try` counts — two CONTROL PAIRS | THE BUFFER SURFACE (R4.4) — `Parser.bufferSurfaceValues`, the `.managed` dispatch RE-ENTRY, `arrayManagedFieldAt`, the four buffer-only members — **and** the `ArrayError` throws clause `GtRuntime.runtimeThrowsClause` builds per `try` on a throwing array accessor. `ScaleCorpus` emits **no `__ManagedMemory` and no `.managed`**, so the surface is structurally invisible to a default run; it is **not** blind to the throws clause, and that is the trap — the corpus is full of `try arr.get(i)`, so that arm reads a real non-zero which looks like coverage of a rung it does not cover. ⚠ **DO NOT SUBTRACT `trynone` FROM `trythrows`** — the call shapes behind the `try` differ by ~90,000 allocations at n=2048, 2.5× the term and pointing the wrong way. **Price the clause by A/B-ing ONE mode across TWO COMPILERS**; `array`, `trythrows` and `trynone` all compile under the pre-R4.4 compiler, which is what makes that possible, and `buffer` deliberately does not. That A/B gave `trynone` **−11 FLAT** over a ×8 span (the feature-free ZERO — `arrayManagedFieldAt` and `valueIsBufferSurface` are allocation-free to the digit) against `trythrows` **+4,597 / +9,205 / +18,421 / +36,853** (×2.003 ×2.001 ×2.000), i.e. **exactly `3 × (accessor trys) − 11`**. At six accessor `try`s per function that is +0.81% of the compile; on the standing corpus, +0.013%. The term is FILED as measured debt — see the R4.4 row of `docs/optimization-log.md` for the reason and the trigger. |
| `genrangesites.sh <n> <onefunc\|spread> <outdir>` | `n` field stores of a NON-CONSTANT value through a narrow ranged alias, all in ONE function (`onefunc`) or one per function (`spread`) | the ALIAS-NAMED range-check sites of P1.9 — `guardSiteAt`, the guard CHAIN and the split that carves it. ⛔⛔ **THIS ROW USED TO SAY `ScaleCorpus` REACHED A GUARD WITH NONE OF ITS ALIASES AND THAT "the rung0 and rung5 binaries each contain the SAME single range-check panic blob". THAT WAS FALSE, AND FALSE WHEN WRITTEN** — re-measured 2026-07-31 by the same method (`grep -c 'Range check failed' <rung>.out.exe`): **33 at rung 0 and 1,025 at rung 5**, of which 32 and 1,024 are `ScaleDivisor`, the A1 divide knob's `((acc and 7) + 1) as ScaleDivisor`. That is a narrow alias-named cast over an unfoldable value, one per divide group, ALL IN ONE FUNCTION and doubling with the knob — i.e. **this ladder's `onefunc` shape at n = 32…1,024**, on the standing instrument since A1. The old survey enumerated alias DECLARATIONS correctly and was simply never re-run after the knob that mints this one landed; the fix is that the blob count is now written down as a COMMAND, not a number. ⇒ **What this ladder is still for**: the site count moves with nothing else moving (the corpus doubles every knob it has at once, so a bend there names a phase, not a term), it drives n past what the corpus reaches, and it carries `spread`. ⚠ A2a added the OTHER door — a doubling ranged-`return` count (8 blobs at rung 0, 256 at rung 5) — which resolves through `retBlockOf`/`splitBlockInPlace` and exercises neither `splitChainEnd` nor `materializeChainTails`. `spread` is the CONTROL: same site count, same guards, different per-function concentration, so a bend on `onefunc` alone is the sites×blocks product term. |
| `genretsites.sh <n> <onefunc\|spread> <outdir>` | `n` guarded `return` sites, each behind its own `if`, all in ONE function and all returning the SAME parameter (`onefunc`) or one per function (`spread`) | the ranged-`return` sites — `retBlockOf` and `splitBlockInPlace`. **The other half of `genrangesites.sh`'s axis**: no store or cast makes a function RETURN a narrow alias, so neither ladder can stand in for the other. ⚠ **`ScaleCorpus` had not one non-full ranged RETURN type until A2a**, so this whole path read Δ0 at every rung of the standing instrument; the corpus's `p_rreturn` knob now doubles the site count from 8 to 256 across the ladder and reads LINEAR there in all three columns (isolated per-rung `phase:insertRangeChecks`: allocations ×1.885 ×1.925 ×1.950 ×1.969 ×1.982, CPU min-of-3 ×1.68 ×1.70 ×1.86 ×1.94 ×1.95). This ladder is still the one that can drive the axis to n = 400 in ONE function and hold everything else still. ⭐⭐ **It is the ladder that found `findRetBlock`'s per-site scan, and it found it in the CPU COLUMN ALONE**: x2.21 x2.53 x3.20 per doubling at n = 50/100/200/400 — a RISING ratio — while allocations and bytes read a dead-flat x1.99 / x2.03 over the same span, because a scan allocates nothing. Read the CPU column FIRST here. Cured (A1h, corrected by its review) to x1.96 x1.94 x2.03. Every rung's binary must EXIT 7, so a miscompile shows as a wrong exit code and not only as a time. `spread` is the CONTROL, for `genrangesites.sh`'s reason. |
| `gentuples.sh <n> <types\|sites\|arity\|access\|files\|fileset\|nest> <outdir>` | ⚠ **SEVEN modes in three CONTROL PAIRS plus one unpaired**, and no mode is meant to be read alone: `types`/`sites` (n literal sites, all DISTINCT types vs all ONE type), `arity`/`access` (n element accesses over an arity-n tuple vs over an arity-2 one), `files`/`fileset` (n files each with an array literal, distinct tuple type per file vs one shared), `nest` (nesting depth n) | a tuple is a SYNTHESIZED STRUCT under a mangled name (`__Tuple2.int.int`) — is minting one O(1), is reading one back O(1), and does the number of DISTINCT tuple types cost more than the number of SITES? ⛔ **The blunt kind of blindness, measured**: `ScaleCorpus` emits NO tuple at all — destructuring 0, `.N` access 0, tuple literal 0, tuple return type 0, tuple typealias 0, the word "tuple" 0, across 465 files and 279,453 lines — and `ScaleCorpus.maxon` itself contains "tuple" zero times. ⇒ **the tuple rung's reported `+20 allocations, FLAT at every rung` is a true reading of two empty maps being constructed once per compile and of nothing else**; not one line of `internTupleType`, `canonicalTupleName`, `registerTupleLayout`, `parseTupleLiteral` or `parseDestructuringBinding` ran while it was taken. ⭐ **`files`/`fileset` FOUND ONE** — `phase:signatures` allocations ×2.10 ×2.24 ×2.45 ×2.75 against the control's ×1.95 ×1.97 ×1.99 ×1.99, the files × types walk in `internArrayLiteralAggregateInstances` — and the pair is kept because it is the only thing that can show it has not come back to the per-file position it was hoisted out of. `arity`/`access` is the other product axis (`StructLayout.indexOfField` is a linear scan by field-name string); `nest` is unpaired and its own control is the ratio, since a mangled name EMBEDS its element names and is O(depth) bytes. |
| `genshareddag.sh <depth> <doubling\|control> <out>` | `depth` chained typealiases over a two-parameter generic; `doubling` gives both arguments the SAME previous alias (a shared DAG), `control` gives the second a leaf (a chain) | is a generic instance's structural compiled name O(nodes) or O(paths)? ⛔ **It is O(paths), and the ladder measures an EXPONENTIAL in LINES OF SOURCE**: `phase:signatures` bytes **36,156,421 → 139,977,003 → 555,220,169** at depth 18/20/22 — **×2 per added line** — against `control`'s **1,557,391 → 1,584,775, DEAD FLAT** at the same depths. The two modes emit the same declaration count, the same instance count and **byte-identical allocation COUNTS (31,441 / 23,514)**; only the byte column moves, which is what identifies the cost as name LENGTH over a shared DAG rather than as a walk visiting each node. At depth 22 that is 97.4% of the compile on a 31-line file; depth 25 is ~4.4 GB and past the memory budget. ⚠ **PRE-EXISTING** — A/B'd against `69e655d38` built like-for-like in one directory, same exponent, W41 flat at +~250 KB independent of depth. **Named cure:** the digest tuple names already use (`__Tuple2.Tfcd8090be60770aa.…`, W14). ⚠ Export the deepest alias or the ladder measures an E3062 error path; the generator does this. |
| `genopaquefields.sh <units> <out>` | `units` × (a plain struct + a generic holding an opaque `T` field AND a struct-typed field + an instantiation + a construction and a field read in `main`) | the W41 paths the corpus structurally cannot reach: `substituteInstanceArgsThrough`'s per-argument interner probe (a `structRef` return used to short-circuit and now does `nameOf` + `isTupleTypeName` first), and the per-instance registration in `closeDestructorNeeds` / `rootManagedOpaqueArrayElementDrops`. ⭐ **Both LINEAR, measured**: `phase:deriveRuntimeNeeds` allocations **6,119 / 7,899 / 11,415 / 18,447 / 32,473** at 25/50/100/200/400 units — per-unit delta **71.2, 70.3, 70.3, 70.1, dead flat**, so the destructor-needs closure is a constant per node and NOT a product with the instance count (the claim that it is O(cascades × instances) describes the pre-`CascadeWorklist` shape and is stale). `phase:semanticCheck` ×1.60 ×1.75 ×1.75 ×2.12. ⚠ **Every unit lands in ONE `main`, so `regalloc` dominates and bends** (501.0 → 1,569.2 ms at 200 → 400, of which `splitting` is 408.2 → 1,351.8) — that is the KNOWN, BUDGETED `SplitLiveRanges` term (`ARCHITECTURE.md:1336-1345`) triggered by the single growing function, not a finding of this ladder. Read the front-end columns. |

### `genscope.sh` — two knobs the standing corpus can only move together

**A cost of the form O(V × C) is invisible on a ladder that doubles V and C at once**, and not because
the ladder is blind to it — it shows up perfectly, as a bend. It is invisible because the bend is
unattributable: `ScaleCorpus` doubles EVERY knob it has per rung, so *everything* quadruples against
*everything*, and `phase:parse` climbing ×2.02 → ×3.33 names a phase and nothing inside it. Separating
the two axes is the whole job, and it takes three columns, not one:

| ladder | `phase:parse` CPU per doubling | reading |
| --- | --- | --- |
| C alone (V = 256) | ×1.72 ×1.86 ×1.89 | linear in constructs |
| V alone (C = 400) | ×1.46 ×1.57 ×1.83 | linear in scope size |
| **both** | **×2.21 ×2.45 ×2.83** | **the product term** |
| `straight`, both | ×1.80 ×1.90 ×1.89 | the control: it is the CONSTRUCT, not the size |

⚠ **THE CONTROL IS NOT OPTIONAL AND IT IS NOT THE `if` SHAPE WITH A SMALLER NUMBER.** `straight` emits
the same V declarations and the same statement COUNT, and merges nothing — so a cost that is really
per-statement, per-token or per-`var` reads the same on both and is ruled out in one run. Without it,
"doubling both bends" is equally consistent with a cost that is simply quadratic in program size, and
this ladder would have named a phase exactly as the corpus already did.

⭐ **THE SCOPE-FILLER TRICK IS LOAD-BEARING AND IS COPIED FROM `ScaleCorpus.fillerLocalsDecl`.** Each
filler is folded into a working accumulator on the very next line, so it is IN SCOPE for every later
construct but LIVE for one instruction. Declaring V locals and reading them at the end instead makes
all V live at once and trips E5001 above ~13 — which is to say the obvious way to write this ladder
cannot climb the axis it exists to climb.

### `genstring.sh` — the two questions, and the ONE that was not about Slice C at all

**The `sites-*` family answers the mandate's question and answers it dully**: predicates, case, replace and
split all read **×1.90–×1.99 per doubling of the call-site count out to 2,048 sites**, in allocations, bytes
and CPU alike, converging on ×2.00 from below the way a linear term under a fixed per-compile constant does.
Nothing in the parser dispatch, the eight runtime graphs or the `RuntimeUsage` closure is superlinear in the
number of call sites, and `sites-control` A/B'd against the parent compiler is **+0 allocations on a program
with no `String` in it at all** — the family levies no per-call-op tax on programs that never use it.

**The `data-*` family is where the readings are.** All four realistic shapes are linear — `split` **×2.04**
across a 16× span of SEGMENTS (so its search genuinely advances rather than restarting at 0 per segment, and
`__managed_push`'s growth is amortized), `replace` **×1.96** across 16× MATCHES (the two-pass count really is
sizing one allocation), `toUpper` **×2.00** and a realistic `contains` **×2.04** in haystack bytes. Two are not:

- **`data-findquad` — `__str_find` IS A NAIVE SEARCH and has a genuine O(hay × needle) term.** Measured
  **×3.92 ×4.15 ×3.95 ×4.14** on the worst case that defeats the first-byte fast reject: an all-`a` haystack
  against an all-`a` needle with one trailing `b`, at HALF the haystack's length. ⚠ **It needs a needle whose
  LENGTH SCALES WITH THE HAYSTACK** — a fixed needle makes this a constant factor and no curve appears, which
  is exactly why `data-find` (a needle absent from the haystack) reads a clean ×2 and is not evidence about
  this. Filed, not fixed: a real fix is a two-way/KMP search, and `stdlib/String.maxon`'s `findIn` — the
  reference both other compilers run — has the same shape.
- **`data-appendloop` — NOT A SLICE C SHAPE, and it was the biggest thing here.** `__str_append` used to
  reallocate to the EXACT required length, so `var s = ""` plus a loop of appends re-copied everything at
  every step: **×3.94 ×4.07 ×3.82 ×3.88**, and **5.0 seconds to build a 288 KB string** from 32,768 chunks.
  Growth is geometric (`2 * requiredLen`) as of the same commit that added this generator, and the same ladder
  now reads **×1.82 ×2.21 ×1.77 ×1.94** with the top rung at **0.98 ms** — a 5,118× fall on the shape, for a
  flat +103 allocations per compile.

⚠ **The `data-*` setup builds its subject with SELF-append (`s.append(s)`), not a chunk loop**, and that is
load-bearing rather than cute: it doubles the length per step, so the setup is O(final length) whatever the
append policy is. Written against a chunk loop, every one of the readings above would have carried the
`appendloop` quadratic inside it.

### `genwitness.sh` — two knobs onto ONE list, and a control that turned out to be a different control

**`relocs = conformers × methods`, and the two knobs reach that product from different sides.**
`<conformers>` moves the number of witness TABLES and the program's type and function count with it;
`<methods>` moves the SLOTS PER TABLE at a nearly fixed everything-else, which is the axis that
separates *linear in relocations* from *linear in program size*. Both are needed: a ladder that only
doubles types cannot tell the two apart, because it doubles them together.

**The methods axis is where the reading is clean.** At a fixed 128 conformers, `wasm32-wasi`,
`phase:encode` (which `buildWasmBackend` bills the WHOLE wasm backend to), relocations 256 → 512 →
1024 → 2048, minimum of 3:

| relocs | allocations | bytes | CPU ticks |
|---:|---:|---:|---:|
| 256 | 31,291 | 1,938,808 | 35,305,117 |
| 512 | 44,924 | 2,609,176 | 44,251,973 |
| 1,024 | 72,141 | 3,629,960 | 61,243,944 |
| 2,048 | 126,586 | 6,050,501 | 99,285,247 |

Read the ratios alone and every one is *below* ×2.00 and rising — which is a doubling term under a
large fixed constant, not a sub-linear anything. Fit the line the two endpoints give and the constant
comes out: **allocations = 17,677 + 53.18 × relocs, with residuals of +19 and +7 at the two interior
points (0.04% and 0.01%)**. CPU fits the same shape to within 2.4%. A straight line in the reloc count,
with the 128 types' own cost sitting in the intercept.

⚠ **`inert` IS NOT THE CONTROL IT WAS WRITTEN TO BE, and the probe said so.** It was meant to build the
witness tables without dispatching through them, so the two per-reloc walks could be priced with the
per-call-site emitters held out. It cannot: **a witness table does not survive a program that never
dispatches through it.** Change one `other int` to `other bool` and build for wasm — under `dispatch`
the compile is REFUSED naming `__witness_P000000.Digest'+24`, under `inert` it compiles clean, and that
silence is the reloc list reading empty. So `inert` is a whole-path control instead: byte-identical
program size (30,298 bytes at 64×2 either way), zero tables, zero relocations, zero call sites, so
subtracting it from `dispatch` is the entire witness cost with parse, lowering and register allocation
of an equally large program cancelled out. To move relocations without moving call sites, turn
`<methods>`. *(It also means neither walk can ever meet a slot whose callee DCE removed — which is the
invariant both of their panics assert.)*

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

### `genwitnessargs.sh` — the arity axis, and a control that is the same program TO THE BYTE

**`genwitness.sh` holds argument count at ONE**, so every cost behind a witness call's parentheses is
invisible to it — and `ScaleCorpus` cannot stand in, because `--emit-corpus` over all 465 generated
files holds 276 `interface`, 276 `implements`, 270 `uses` and **zero `where`**. A witness dispatch needs
`where T is <Interface>`, so `parseWitnessMethodOnValue` runs **exactly zero times** in the standing
ladder and every column reads a Δ0 that means *unreached*. This generator adds the missing knob.

The three size knobs are separable, and they must be, because they reach different costs:

- `<conformers>` moves witness TABLES and instantiations — **not** call sites. Under dictionary-passing
  there is ONE shared body however many instantiations exist, so `phase:parse` does not move on this
  knob. Turning it and watching parse stay flat is the cheapest proof that dictionary-passing is what
  is being measured.
- `<methods>` moves slots per table AND dispatch CALL SITES, so it is the axis the per-call-site
  constant rides.
- `<args>` is `P` in `slotCallArgs` — the length of the `paramNames` list each call's labels are
  slotted against. The axis slice 2b-vi created.

⭐⭐ **`<decl|reverse>` IS THE LABEL-ORDER CONTROL, AND THE TWO ARE THE SAME PROGRAM TO THE BYTE.** Every
argument is written `aNNNN: 1` — fixed-width label, fixed literal — so reversing them permutes
equal-length chunks and changes nothing about size, tokens, IR or codegen. The only difference is
whether `argSlotPosition`'s O(P) fast path (test `paramNames[argIndex]` before scanning) HITS or MISSES,
so **subtracting `decl` from `reverse` is the label SCAN and nothing else.**

⚠ **The scan ALLOCATES NOTHING**, so the allocation columns read flat however quadratic it is — read the
CPU column, which exists for exactly this. The allocation columns still earn their place: they price the
per-call-site buffers, which the CPU column cannot separate from the scan.

⚠⚠ **`<args>` is capped at 63 by the LANGUAGE, not by this script** — `<args>` = 64 is refused with
E2015 (the 64th slot is the dispatch's own receiver). That cap is the finding, not a usage note: `P` is
bounded by 64 for every program the compiler accepts, so `reverse`'s P²/2 term cannot exceed ~2,048
comparisons per call. **A quadratic in a variable the type system caps at 64 is a constant**, and this
is the ladder that establishes which one.

### `genfnval.sh` — a mechanism the corpus contains ZERO of, and a control that is NOT byte-identical

**The corpus cannot express this one at all**, and that is checked rather than assumed: `--emit-corpus`
over all 465 generated files finds **no `typealias … = function(…)` and no function reference, at every
rung**, so `resolveFunctionAliasShapes`, `declaredFunctionShapeOf`, `checkIndirectCall`,
`checkFunctionTypeDoors` and `indirectCalleeParamTypes` each run **exactly zero times** in a default
`scale-test`. A Δ0 there measures the instrument, not the compiler.

**The three knobs are separable, and the quadratic they exist to refute needs two of them to show.**
`<aliases>` moves the size of `functionAliasShapes`, the length of `resolveFunctionAliasShapes`' one
loop, and the DOOR count (each alias contributes a `return` door and an argument door) — and, since it
also moves the function count, it is the "whole program doubles" knob. `<sites>` moves indirect CALL
SITES at a fixed alias count. `<arity>` is `P` in `functionShapesAgree`, `paramFloatMask` and
`widenIntArgsToFloatParams`. **A per-call-site scan over the alias registry would cost `sites × aliases`
and so read ×4.00 when `<aliases>` doubles** (which doubles the sites with it); it reads ×2.00, because
`declaredFunctionShapeOf` is two hash-map gets.

⚠ **`direct` IS A SHAPE CONTROL, NOT A BYTE-IDENTICAL ONE, and it cannot be** — unlike
`genwitnessargs.sh`'s label-order control. A DIRECT call's second and later arguments MUST be labelled
(E2053); an INDIRECT call's are positional and CANNOT be, because a function TYPE has no parameter names
for them to name. So `direct` carries `aNNNN: ` per argument past the first. Read the subtraction as an
upper bound and read RATIOS within a mode, which the label bytes cannot touch.

⚠ **The function typealiases are DECLARED in both modes**, so `resolveFunctionAliasShapes` runs in both
and the declaration cost cancels in the subtraction — leaving the CALL and DOOR paths alone. To price the
declaration itself, compare `direct` against a run with `<aliases>` halved. (Measured 2026-07-28: the
`phase:resolveTypes` delta is **identical to the digit in both modes**, +609/+1,193/+2,353/+4,665 across
aliases 64→512, which is that cancellation working.)

⚠⚠ **`<arity>` HAS A CEILING OF 13 AND IT IS A COMPILER DEFECT, NOT A DESIGN LIMIT.** An indirect call of
arity ≥ 14 panics the register allocator (`RegisterAllocator.maxon:1173`, `chooseRegister: no free
register`). Measured on `main` @9fa71cc79 as well as on the rung under test — arity 13 compiles and 14
panics on both — and the same arity 16 through a DIRECT call compiles clean, so it is specific to the
indirect path and is PRE-EXISTING. Reported to the coordinator, not worked around.

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

### `genfsprobe.sh` — pricing a cost that is paid in SYSCALLS

⚠ **Build this one with the BOOTSTRAP** (`./bin/maxon.exe build <out>`), not with shv2 — shv2 cannot
yet compile `stdlib/File.maxon`. That is not a weaker measurement: the shv2 compiler *binary* is
emitted by the bootstrap, from this same `stdlib/File.maxon` and this same emitted runtime, so a
bootstrap-built program calling `File.delete` runs the identical machine code shv2's own
`Compiler.discardPreviousOutput` runs.

**Why it cannot be a compile ladder.** `scale-test`'s memory columns cannot price a syscall: a
`File.delete` allocates **exactly one 40-byte record whatever it costs in time**, so the compile that
does four of them reads +4 allocations and tells you nothing about the microseconds. And that 40 bytes
is not the path — it is the green-thread runtime's `SyncRequest`. `__io_submit_sync`
(`X86CodeEmitter.Runtime.cs`) `mm_raw_alloc`s one, enqueues it, `SetEvent`s a **sync worker thread**
and parks the caller until the worker completes it. **Every `__ManagedFile` call is a cross-thread
round trip**, which is why one costs tens of MICROseconds on a box where the kernel call itself is a
couple. Measured here at 20,000 iterations: a failing `File.delete` **52.8 µs**, `File.exists` **48.2 µs**
absent / **62.9 µs** present, `File.writeBinary` of 64 bytes **449 µs**.

**⭐ The reading it exists to make re-runnable: `changeExtension` charges by what the String is a
PRODUCT OF, not by what it says.** `change_extension` and `change_extension_forward_spelt` build the
same final path text, both the way `Compiler.resolveOutputPath` does — by interpolating the platform
extension onto the raw `-o` value — and differ only in the separator that went in. On Windows
`FilePath.create` normalizes `/` to `\` through `String.replace`, so the forward-spelt one arrives as
a `replace()` result and the native-spelt one arrives as the interpolation's own buffer:
**8 allocations / 343 bytes against 10 / 368**, for two `FilePath`s that compare EQUAL. Build them
from literals instead and the difference vanishes, which is how you can tell it is the buffer and not
the bytes. ⚠ **This makes a per-compile allocation figure depend on how the DRIVER spelt its paths**:
`scale-test` spells them with `FilePath.join` (`\`), so its numbers carry the 10-allocation form,
while the same compile driven by hand with `/` arguments reads 2 allocations and 26 bytes lighter.

**`discard_plus_require`** is the composite — one `changeExtension`, two failing `File.delete`, two
`File.exists` — i.e. exactly what `Compiler.discardPreviousOutput` + `Compiler.requireOutputWritten`
add to a compile whose output is absent. **`guarded_delete_absent`** is the standing question about
them: is `if exists then delete` cheaper than a bare failing `delete`? Measured, it is not enough to
matter — 48.2 µs against 52.8 µs when the file is absent, and a whole extra 62.9 µs `exists` on top of
the delete when it is present.

### `genfor.sh` — a construct the corpus contains ZERO of, and a lock read on every binding access

`ScaleCorpus` generates **no `for` loop at all**, and could not: the construct did not parse until
P1.8 slice A. So a default `scale-test` run is blind to the whole rung in **every** column, CPU
included — the lowering, `__managed_get_unchecked`, and the four doors that read
`Parser.iterationLockedBindings` produce a flat Δ0 that measures the instrument.

**Its knobs are independent**, for `genmutchain.sh`'s reason. Program size is `loops × accesses`,
so `depth` moves **alone**: `genfor.sh D D <512/D> array` puts the whole nest in ONE function and
holds the access count at exactly 512 while the nesting doubles (1,043 → 1,057 lines across depth
1 → 8). That is the only recipe that can separate a depth term from a size term here, and it is
the one below.

⚠ **`noloop` IS THE READING THAT MATTERS.** The four doors sit on ordinary binding-access paths, so
the question is not what a loop costs but what the lock costs a program that has no loops. Measured
head against the parent rebuilt in the same worktree, 64 → 1,024 accesses:
**`phase:parse`, `phase:lex`, `phase:signatures`, `phase:merge`, `phase:lowerMaxonToStd` and
`phase:regalloc` are IDENTICAL TO THE DIGIT at all five rungs.** The entire delta is **+142
allocations, FLAT** — `+108 unattributed` (building the `__managed_get_unchecked` Std graph) and
`+28 phase:isel` (selecting it), once per compile. **Nothing per access, per binding, per statement
or per function**, which is the claim the `count() == 0` guard in front of the walk exists to
support. `scale-test` reads the same +142 flat across its own 32× ladder, on a completely different
program.

**And nothing here is superlinear.** `loops` 64 → 1,024 reads `phase:parse` allocations ×2.00 ×2.00
×2.00 ×2.00 (CPU ×2.04 ×2.02 ×1.99 ×1.99); `range` mode, whose literal bounds leave the lock stack
empty, reads ×2.00 in every phase in both columns. On the constant-size **depth** ladder
`phase:parse` allocations go 6,541 / 6,674 / 6,940 / 7,469 — an excess over depth 1 of
**133 / 399 / 928, i.e. 133 × (1, 3, 7)**, exactly ∝ (D−1). That is `assignedBindingsIn`'s documented
Θ(tokens × depth) scan, **linear in depth**, and the lock walk adds nothing on top of it.
`phase:regalloc` jumps ×7.21 from depth 1 to 2 and then reads ×1.68 ×1.95 — a MODE CHANGE (the
splitter engages at all once two loops nest), not a curve, and the identical shape appears on
`while` nesting built against the parent binary.

⚠ **DEPTH IS HARD-CAPPED NEAR 10, AND THE CAP IS THE ALLOCATOR'S, NOT THE PARSER'S.** Every `for`
mints an index phi live across every loop nested inside it, so depth D holds D values at once and
E5001 refuses: measured, depth 10 compiles and depth 11 is *"needs 1 more register(s) than are
available"*. `gennest.sh` evades exactly this by giving every `while` level the SAME guard variable
— a trick a `for` cannot use, because the counter is minted by the lowering and not by the program.
**So the lock stack can never be deeper than ~10 in a program that compiles at all**, which bounds
its linear scan by a constant the compiler itself enforces. Move the door count with `accesses` and
`noloop` instead of reaching for depth.

### `gentrim.sh` — four families, and the one that found a whole-string walk for a one-cluster answer

`ScaleCorpus` contains **zero** `trim`, `CharacterSet`, `CharSet`, `Set with Character` and `\uXXXX`
(measured, `--emit-corpus` at rung 5). That is the **sixth consecutive rung** the shared instrument
cannot express, so every column of a default run reads a Δ0 that means *unreached*.

**The four families answer four different questions**, and collapsing any two of them loses the finding:

- **`data-*` doubles the UNMATCHED body** — the pure scan. All seeds ×2.00 per doubling in time and
  peak RSS: **`__str_trim` is O(n), not O(n²)**. Per CLUSTER at 524,288 bytes: ASCII 102 ns, 2-byte BMP
  150 ns, supplementary 190 ns — so `__gr_end`'s general scan is ~48 ns over its ASCII fast path and the
  **806-entry binary search is ~40 ns over a direct BMP byte load**. Bounded constants. ⚠ Measured against
  the synthesized `__str_trim`/`__gr_end`/`__ucd_cat`, all three since retired onto the corpus (W49 waves
  3–6, W129); the shapes are the same and the CONSTANTS have not been re-measured at the corpus site.
- **`edge-*` doubles the MATCHED pad against a fixed body** — the cost of *cutting* rather than of
  *scanning past*. Also ×2.00, and at the same per-cluster cost, because both paths mint a `Character`
  and probe the set.
- **`loop-*` doubles the TRIP COUNT at a fixed 9-byte subject** — the slab trigger. ⭐ **`loop-trim` vs
  `loop-trim-shared` is the A/B that isolates the per-call `CharacterSet`**: the same program but for
  whether the set is built inside the loop or hoisted out, giving **~1,068 ns and ~1,155 bytes of
  never-reclaimed slab per call**.
- **`sites-*` doubles CALL SITES** — the compiler's own linearity. All modes ×1.9…×2.0.

⭐⭐ **What it found.** `data-trimstart-clean` read the same curve *and the same absolute nanoseconds*
as `data-trim-clean`, which is the tell: `trimStart` ran the scan to the end collecting a `keptEnd`
that `emitTrimResult` discards (an untrimmed end takes `length`). Isolated to one call,
`"abcdefgh"×65,536`.trimStart() — **nothing to cut** — cost **56.2 ms and 45.0 MB** to answer "byte 0".
Fixed by exiting at the first kept cluster when `fromEnd` is clear; after, **0.46 ms and 4.8 MB**, and
the remaining 0.46 ms is the result COPY, which is the floor. `trim`/`trimEnd` unchanged to the KB.

⚠ **Two knobs exist because one reading was ambiguous without them.** `SITES_PER_FN` (env) moves the
function count while `<n>` holds the site count, which is what proved the control-mode delta was
**+16 bytes per FUNCTION** and not per site — at 256 sites, `SITES_PER_FN` 1/2/4/8 read 4,920 / 2,872 /
1,848 / 1,336 bytes, tracking functions and ignoring sites. And `data-trim-supp` is the only mode whose
seed reaches the supplementary-plane search at all; a ladder of ASCII and 2-byte seeds measures the
direct table load twice and cannot claim to have measured the category lookup (it lived in the compiler's
own `__ucd_cat` when this was written and lives in `stdlib/helpers/string/unicodeCategory.maxon` since W129 —
the two plane arms, and this argument, are unchanged).

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

⚠ **And a ladder that does not move a quantity cannot bound it.** `genwidelive.sh <N> sum` splits
Θ(N) values and mints exactly **one** fresh reload id per split, so it reads flat over anything whose
cost is per-fresh-id — which is how a Θ(fresh ids) walk at every clobber op survived it. The count is
one per **RUN** of the victim's uses (a run breaks at every block boundary), so it is the *use-block*
count that moves it, and no generator here moves that: measured **8 / 16 / 32 / 64** fresh ids in ONE
split for a value read in 8 / 16 / 32 / 64 separate blocks, against 1 on this whole ladder. **Read
which quantity a ladder actually varies before believing its flat column.**

### `genfloathash.sh` — and the mode that found a cost in a surface the rung never touched

The rung this was written for (P1.7a's last slice: `float` is `Hashable`) adds an IR opcode, a `TargetOp`
on two backends and a synthesized `buildFloatHash`. **None of it is reachable from `ScaleCorpus`** — the
corpus emits no `.hash()`, `.equals()` or `.compare()` on a float receiver at any rung, and the install
gate is REFERENCED-but-undefined ⟺ INSTALLED, so nothing is ever built and every column reads Δ0. That is
the ordinary reason a private ladder exists here, and it is the *less* interesting one.

**The interesting reason is that the mode which measured a regression is the mode for a surface the rung
did not modify at all.** `builtinConformerMethod` walks `builtinConformableProtocolNames()` in the fixed
order Hashable, Equatable, Comparable; it `continue`s past a protocol the receiver does not conform to,
and it RETURNS at the first protocol whose declaration carries the method. Admitting `float` to `Hashable`
turns that leading `continue` into a `requireBuiltinInterface(Hashable)` — a whole `IrInterface`
synthesized, searched for `equals`, missed, discarded — on **every float `.equals()` and `.compare()` call
site in the program**. Nothing in `float.equals`'s own path changed; its position in a search order did.

Measured, same file compiled by the pre-rung and post-rung binaries at a matched checkout-path length:

| mode | 64 sites | 128 | 256 | 512 | reading |
| --- | --- | --- | --- | --- | --- |
| `equals` | +648 | +1,288 | +2,568 | +5,128 | **exactly `10 × sites + 8` allocations**, and `344 × sites + 510` bytes |
| `inthash` | +8 | +8 | +8 | +8 | the control: FLAT |
| `control` | +8 | +8 | +8 | +8 | the control: FLAT |

**+10 allocations and +344 bytes per call site — LINEAR in sites, not superlinear**, so it is a constant
factor and it is filed rather than fixed. The flat `+8 / +510` both controls show is the whole rest of the
rung: one more candidate in `installBuiltinConformanceRuntime`'s roster, paid once per compile whatever the
program's size.

⚠ **`inthash` IS NOT A SMALLER `hash`, AND IT IS THE ROW THAT MAKES THE ATTRIBUTION STICK.** `int` has
conformed to all three protocols since slice 2b-ii, so its conformance row is already the width this rung
gave `float`, and this rung cannot have moved it. Its flat `+8` says the cost is in *float's row* and not
in the walk — and, because those programs carry thousands of `TargetOp`s, the same flat `+8` is also the
measurement that the new union variants did **not** widen `TargetOp`'s widest payload. A union that had
grown would have taxed every op allocation and the delta would climb with program size. It does not.

⚠ **`hash` CANNOT PRODUCE A DELTA AND MUST NOT BE READ FOR ONE** — the pre-rung compiler refuses the file
(`E2015: 'float' has no method named 'hash' — its builtin conformances supply equals, compare`). That
refusal is the ladder's proof of REACH, and it is the strongest kind available: a generator whose output
the parent compiler cannot compile at all is unambiguously exercising the rung. Read `hash` as a SLOPE
within one binary instead — measured **595.4 / 594.1 / 593.8** allocations per site against `control`'s
**595.7 / 594.6 / 594.1**, i.e. compiling a `f.hash()` call site costs what compiling a float comparison
costs, flat across an 8× span.
