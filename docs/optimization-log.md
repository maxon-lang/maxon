# Optimization log

What the compiler's cost has actually done, change by change. **This is `scale-test`'s deliverable.**

**Read the tables downwards.** Each row is one recorded change, dated. A column *is* its history, so
the progression is the thing you see rather than something you have to reconstruct.

`scale-test` renders **no verdict**. There is nothing to pass, no golden to accept, and no light to
turn green — it is an instrument, and this document is what it is an instrument *for*. The last row
of each table below is the previous datapoint: every run reads it and reports the delta from it, so
you still see exactly what moved, without anything failing.

⚠ **When a row's headline was measured on a HAND-BUILT ladder rather than the corpus, the ladder must
be committed, not described.** `scale-test`'s corpus is a list of shapes someone thought to generate,
so most optimizer passes end up building one to answer a question the corpus cannot express — and
every one that threw it away left a number nobody could re-run. The Wave 2 rung reported a base
column that came out **~60× different** when someone rebuilt the ladder: it had measured a different
program, and there was nothing to check that against. **The generators live in
`tests/ladders/` — add yours there and name it in your row.**

⚠ **THE LADDER CORPUS MOVED TO `tests/ladders/` ON 2026-09-02, AND THE ROWS BELOW WERE NOT REWRITTEN.**
Every row dated before then names its generator under the former path `maxon-bin/Testing/ladders/`,
because that is where the script lived on the day the number was taken, and a dated record that says
otherwise is asserting something false. **The generators themselves are unchanged** — same file, same
name, same output — so every measurement below still stands and every script it names is still
findable BY NAME under `tests/ladders/`.

## How rows get here

Rows are **appended by the tool, not by hand**. Running

```
maxon-the compiler scale-test --note="<what changed, and why the numbers moved>"
```

records this run as a new dated row in both tables. **A run without `--note` records nothing** —
scale-test is run dozens of times a day against dirty trees and abandoned experiments, and a log of
all of them would be a log nobody reads.

**`--note` is an instruction, not a request: if you pass one, the row is written.** Even if the
numbers are identical to the row above it. The run will tell you plainly that nothing moved, and then
record it anyway, because you are the one who knows whether it is a datapoint and the instrument is
not.

The `--note` is not a formality. The instrument can see exactly *what* moved and can never see *why*,
and six months from now a number with no reason attached is worth almost nothing. So the reason is
demanded at the one moment it is still known: from you, now. See
[`maxon-bin/Testing/ScaleHistory.maxon`](../maxon-bin/Testing/ScaleHistory.maxon).

⚠ **IF YOU EDIT A ROW BY HAND, EDIT IT IN ALL THREE TABLES.** One run writes the last row of
**Allocations**, **Bytes** and **CPU** together, so those three rows must agree cell for cell on
`date`, `minted at` and `change` — and the next run **refuses to read this file** rather than
difference itself against two different pasts. Touching the note of one row and not the other two is
the way that happens, and it has. The refusal names the cell that differs and prints a window of both
values around the character they part at, so what it tells you to fix is the edit you actually made.

## `minted at` — why a row records where it was measured, and what it costs you

**A row's BYTE columns are only comparable with another row minted in the same place.** The `minted at`
cell names the two paths that reach a compile's byte volume — the absolute path of the compiler that
produced the row, and the working directory it compiled the corpus from — and `scale-test` compares the
pair before it subtracts anything.

**A compile allocates the path strings it works with**, so its byte volume carries a term proportional to
the **length** of each. There are two, and they arrive by different routes:

1. **The compiler's own executable path.** `locateStdlibDir` walks up from it and every stdlib source
   path is derived from what it finds.
2. **The working directory.** The generated corpus is deliberately handed a *relative* path (see
   `CorpusRelativeRoot`) — and that does **not** keep the checkout out of the byte column, because the
   compiler resolves each source path against `Directory.currentPath()` itself, per file, per compile
   (`Queries.isStdlibSourceFile`). That comment claimed otherwise until it was measured.

**MEASURED**, one binary, six rungs, each row varying exactly one of the two:

| | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| bytes, two checkouts 44 characters apart | +92,576 | +102,740 | +123,068 | +163,724 | +245,036 | +407,660 |
| bytes, one exe, two working directories 44 apart | +34,515 | +39,960 | +50,850 | +72,630 | +116,190 | +203,310 |
| allocations, either experiment | 0 | 0 | 0 | 0 | 0 | 0 |

⚠ **That `0` row is real and its old reading was not.** Both experiments varied a path's **length**, and
the allocation column does not see that. It sees something else. **MEASURED 2026-08-02** (row `A3p`), one
binary — built twice, in two worktrees of one commit, and confirmed byte-identical by hash — run at four
real checkout paths:

| checkout path | rung 0 allocs | … | rung 5 allocs |
| --- | ---: | :-: | ---: |
| 45 characters, 7 path components (a worktree, **and** a synthetic path of the same length and depth) | 778,291 | … | 18,109,206 |
| 46 characters, 5 path components | 778,147 | … | 18,109,062 |
| 45 characters, 3 path components | 778,018 | … | 18,108,933 |
| 14 characters, 3 path components | 778,018 | … | 18,108,933 |

**+273 at every one of six rungs between two paths of the same LENGTH; 0 at every one between paths 31
characters apart.** Every delta lands in `phase:parse` alone. So the two columns are **not the same kind
of number** across checkouts, **both** are spoiled by a mismatched pair, and the rule is the difference in
*how*:

- **Allocations carry a CONSTANT** — the same number at every rung, which is what a startup cost looks
  like. So the **shape** of a cross-checkout allocation A/B survives (a flat offset cancels out of every
  rung-over-rung ratio and out of any delta that *grows* per rung) and its **constant term does not**: a
  flat per-rung delta between two checkouts is unattributable and must not be filed as a cost. It has
  nearly been, twice. `scale-test` still prints the delta, under a caveat saying exactly this.
- **Bytes carry a term that grows with the program.** It sits in `phase:load` (flat), `phase:signatures`
  and `phase:parse` (both rising with it), so it is **not a constant offset a reader could subtract** —
  it ran from ~2,100 bytes per character at rung 0 to ~9,300 at rung 5. There is nothing left of that
  A/B, so when the pair differs `scale-test` prints `withheld` in that column and says why. **Nothing is
  normalised**: the counts stay exactly what was measured, and the one subtraction that cannot be honest
  is not performed.

⚠ **So "equalise the path length" is NOT the rule** — two 45-character paths differ by 273. The remedy is
the same for both columns and needs no theory of which part of a path multiplies: **run both binaries
from ONE directory, swapped into one location.** Equal paths allocate equally, full stop.

⚠ **Every row below dated 2026-07-31 or earlier reads `*(unrecorded)*`** — they predate the column, so
their own minting paths are lost. A **byte** delta against one of them is withheld rather than guessed,
and an **allocation** delta against one of them is exactly as good as any other cross-checkout pair's —
shape yes, constant term no — because an unknown path cannot be shown to match this run's. The absolutes
remain readable and the trend is unbroken: what is gone is only the licence to subtract two byte cells
minted who-knows-where, and to read a flat allocation offset as a cost.

⚠ **A symlink or junction is not a different path.** Windows resolves one before a process sees its own
executable path, so an A/B run through two junctions of different lengths pointing at one tree measures
**nothing** and reads as proof of path-independence. Every reading above comes from real directories.

*(This cost four sightings by two agents in one session before it was measured — once as a sign
reversal, and once written down with the allocation column wrongly blamed alongside the byte one.)*

## Reading the numbers

The rungs are generated programs, each **double** the last, so rung 5 is 32× rung 0. Rung 0 shows
constant-factor wins; only rung 5 shows whether a change bent the curve.

**And because the ladder doubles, the RATIO between two rungs IS the growth.** Divide a rung by the
one before it: **2× is linear, 4× is quadratic**. That is the whole method — there is nothing fitted,
no exponent, no residual, and no threshold for anyone to argue about. `scale-test` prints that ratio
beside every phase; these tables carry the raw counts it is taken from.

| what | how much to trust it |
| --- | --- |
| **allocations** (per rung) | **Exact, bit-for-bit reproducible, and INDEPENDENT of where the checkout lives.** The same source through the same compiler makes exactly the same allocations, on an idle machine or a loaded one, in the primary checkout or in a worktree. A number here that moved, moved for a *reason*. |
| **bytes** (per rung) | **Exact and bit-for-bit reproducible for ONE checkout — and NOT comparable across two.** See the `minted at` section below: a compile allocates the compiler's own path and derives every stdlib source path from it, so this column carries a term proportional to that path's *length*. Measured at **+98,888 → +435,455 bytes** across two checkouts 47 characters apart, against **exactly 0** allocations. Within one path a movement is as real as an allocation's. |
| **the rung-over-rung ratio** | **Exact**, for the same reason: it is a division of two numbers that cannot move. And it is the one reading the path term barely touches: across those same two checkouts the top rung's byte ratio read **×2.0355 and ×2.0353**. |
| **CPU ticks** (per rung) | **MEASURED: median 1.7%, worst 6.3%, across three idle runs — for phases above 50M ticks, which are the ones a ratio is read from.** Small phases are much noisier (`phase:runtimeAugment` at ~100K ticks swung 28.6%); do not read a growth ratio off one. **A movement inside the band is NOT a datapoint.** Against the question the ladder asks — ×2 linear vs ×4 quadratic — 6% has a 100% margin. Rows are the MINIMUM of `--repeat` compiles. |
| **the CPU rung-over-rung RATIO** | **The durable part, and it survives a busy box.** Absolute ticks do NOT: a fully CPU-loaded machine inflated every rung by **+49% to +52%** (cache and SMT contention — this counter ignores preemption, not contention). But it inflated them *uniformly*, so the growth reading came through nearly intact: **loaded ×1.90/×1.98 against idle ×1.86/×2.00.** Compare ratios across dates and boxes; compare absolutes only within one machine's own quiet runs. |
| **wall time** | **Not measured, and never will be here.** It is machine-dependent in the way that matters: it counts every *other* process too, so a dated column of it would compare a loaded box in July against an idle one in August. Measured: one run read `phase:parse` at ×5.03 then ×1.78 across a doubling ladder — preemption, not a curve. For the milliseconds of one compile, use the compiler's own `--log=compiler:debug`, which now prints a `cpu%` column beside the wall `%`; a phase where the two disagree spent its wall time not running. |

These are the compiler's own allocation counts and byte volumes while compiling that rung — **not**
peak resident memory, which nothing currently measures.

> ### ⚠ THREE READING RULES. The first two are the 2026-07-14 Go-growth / raw-counter change; the third is about the CORPUS:
>
> **1. ×2.1 — not ×2.00 — is the byte column's LINEAR reading.** `Array` grows by Go's `nextslicecap`
> (double below 256 elements, then ease toward 1.25×), and that ratio is a *function of the buffer's
> size*, so the bytes a buffer allocates over its life are a *rising* multiple of its final size (2.0 at
> the threshold, up to 5.0 as the ratio approaches 1.25). The ladder doubles the program each rung, so
> that multiple climbs each rung, and **a phase whose bytes are perfectly linear reads above ×2.00 while
> it climbs.** It saturates at ×5 — a transient bend, not a superlinearity. So a byte reading around
> ×2.1 is this policy; well past it is still a quadratic. See `grownCapacity` in `stdlib/Array.maxon`.
>
> **2. The `allocs` column counts the RAW allocator layer, and older rows do not.** Every row on or below
> the "three changes rebased onto…" row counts raw+tracked allocations; every row above counts only
> tracked (structs, handles, Strings), so array/string *buffers* were invisible to it. The two are not
> comparable — the one-time step up (~1.06M at rung 5) is the instrument gaining sight of allocations
> that were always there, not the compiler doing more.
>
> **3. THE CORPUS IS VERSIONED TOO, AND A ROW THAT CHANGED IT IS AN EPOCH BOUNDARY, NOT A DATAPOINT.**
> Every number here is *this compiler compiling that corpus*, and `ScaleCorpus.maxon` is a source file
> like any other. When it gains a knob or retunes one the generated programs get bigger, so **every
> column moves for a reason that has nothing to do with the compiler**: the delta on that row is
> meaningless, and nothing above it is comparable with anything below it. The `change` column is the
> **only** mark there is — the tool cannot know a knob moved — so such rows say so themselves, in
> words like "CORPUS CHANGED", "CORPUS INSTRUMENT CHANGE", "CORPUS BLIND SPOTS CLOSED", "CORPUS
> RETUNE". ⚠ **The most recent boundary is the easiest to miss**: `2026-07-25` *"CORPUS RETUNE, not a
> compiler change: FloatsLivePerSpillLoop 4 -> 12"* opens with prose instead of the usual banner, yet
> it is a full boundary — rung 5 goes 16,020,150 → 16,223,032 across two rows whose compiler is
> byte-identical.

Frees are measured and reported but not tabulated here: they track allocations almost exactly, so a
third table would be a near-duplicate paid for in width. They are in `--result-json` for anyone who
wants them.

Dates are UTC. The columns assume a six-rung ladder; changing the ladder's length changes what the
numbers mean, so the tool refuses to append a row of a different width rather than make the tables
ragged.

## Allocations

| date | minted at | change | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-09-10 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | OpVariantFacts sized to the whole TargetOp union at creation: one memo lives per regalloc worker, and which functions a worker gets is timing-dependent, so its lazy growth made regalloc memory differ between two compiles of one rung and every ladder since the pool landed read BROKEN RUN. Now identical across repeats. The module-level memo only refuseForRegisterPressure read is gone, and dead allocateOnThisThread/allocateFunction removed. First readable ladder after 6c6ac5c82d (spawn-aware stdlib reachability; this corpus spawns no service, so that change reads 0 here). Allocation totals scale with the host's processor count (one regalloc worker per processor): compare rows from same-core-count hosts only. | 4,051,353 | 6,065,963 | 10,091,698 | 18,146,909 | 34,264,189 | 66,540,997 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | unswitchInvariantGuards (the new Std loop-versioning pass after LICM) made LINEAR in loop count. Its first cut walked the FUNCTION per admitted loop three times over: the exit-phi substitution rebuilt every op of every block per loop, and two function-sized columns (the hoist numbering's canonical map, the clone's value/block substitution columns) were built per loop - x3.70 x3.84 x3.91 allocations across 50/100/200/400 versionable loops in ONE function on tests/ladders/genunswitch.sh, and +400,000 allocations for +4,000 pad ops at a fixed 100 loops. Now an escaping value's reads come off a per-function use index (op sites and edge sites, two CSRs built once), one identity column pair per function is restored after each rewrite in O(what it mapped), and the canonical map is a stamped per-loop map: x2.01 x1.97 x2.00, and flat under pad. THE CORPUS IS BLIND TO THE PASS: no rung holds an element store inside a loop, so it versions 0 loops at rung 0 and rung 5 alike (identical refusal tally, the runtime's own loops). This row's phase:unswitchInvariantGuards moves only by the per-function gather on that constant set of functions: +228 allocations and +29,480 bytes, the same at every rung (the second CSR and the two site columns per gathered function, less the liveOpIndices walk a second nextFreeValueId used to pay). | 3,948,444 | 5,881,226 | 9,742,633 | 17,468,550 | 32,925,679 | 63,878,946 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | refineVersionedRanges (the second refineValueRanges run, after unswitchInvariantGuards) is SCOPED to the functions that pass rewrote: unswitchInvariantGuards answers a per-function column (RewrittenFunctions) that the pipeline hands the second run as its RangeRunScope, so a function versioning left alone - the shape the first run already read - pays nothing. Before, the second run rebuilt the dominator tree, stamps, shape reader and eight value columns for every function holding a word compare: 19,442 / 23,011 / 29,822 / 42,946 / 68,740 / 119,705 allocations (x1.74) and 3.8M to 122M bytes, CPU 45M to 1.2G ticks; now 46 allocations and 2,212 bytes at every rung (the scratch record and its empty columns). The corpus versions 0 loops so the phase visits 0 functions here; on the self-compile it visits 61 of 12,692 and decides the same 44 compares in the same 23 functions as the unscoped run, and 13 hand probes (N straight-line checked accesses at N = 100..800, N versionable loops at 50..400 from tests/ladders/genunswitch.sh, and a for-i-in-0-upto-a.count() twin with 0/2000/4000 pad calls before the loops) emit byte-identical executables before and after. Costs of the contract: the first run pays +1 allocation per module for the scope union, unswitchInvariantGuards +4 allocations and one byte per function for the column. unswitchInvariantGuards also reads the preheader BACKWARDS in preheaderLoadOf (one scan that stops at the nearest load of the field or the first header write, where it was two full scans per hoisted load): the same answer on every probe, and the iterator each withIterator() scan allocated is gone - about 20 allocations per versioned loop on the probes (cl-50 unswitchInvariantGuards 35,739 to 34,731, us-100 76,250 to 74,638), invisible on this corpus, which versions 0 loops. Both runs were already LINEAR before this row - first run x1.81 allocations and x2.09 bytes here, second run x1.74 and x2.09 - and on the acc probes the first run bytes grow x2.07 / x2.28 / x2.15 / x1.96 / x2.16 across 100..3200 accesses on a HEAD control binary as well, the alternating ratio being capacity rounding shared with threadConstantBranches and CSE rather than a term of this change. | 3,954,490 | 5,888,462 | 9,752,182 | 17,482,522 | 32,948,412 | 63,918,958 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | Cold-call spilling (round 6): a call in a cold block (BlockHeat.cold — an inlined access's slow arm, a range check's panic, an otherwise-panic handler) no longer confines the values live across it; the colorer plans a save/reload bracket per caller-saved register held across it (ColdCallBracketPlan) and the rewrite splices storeSlotReg/loadRegSlot around the call. The optimize pass then removed two heap boxes from the rewrite's per-op path: ColdCallBracketPlan.takeAt answered a payload-bearing union (ColdCallBracketLookup.none, boxed once per body op) and ReuseCopyPlan.at answered ReuseCopyLookup.none the same way — both answer a bool now (a probe shows Lookup.none calling __mm_alloc exactly as the payload case does) — and the plan's per-register slot tables are sized on a pass's first mint instead of at beginPass. regalloc:rewrite vs the last row: 107,668 to 86,607 allocations at rung 0 and 2,447,890 to 1,973,144 at rung 5, x1.97 unchanged; the ladder taken before the union removal read 135,089 / 3,061,519, so the bracket ops themselves cost about 3,180 allocations at rung 0 and the remainder was the box. regalloc:liveness +66 at every rung is the isCall column of OpVariantFacts, grown per learned variant and not per op; regalloc:coloring +10 to +30 (+4 per rung) is the same column grown where a variant is first learned inside the coloring probe; regalloc:ssaDestruction -40 / -80 / ... / -1280 (halving downward, so per guarded site) is fewer phi-edge moves once the corpus's checked accesses keep their values in caller-saved registers across the cold call; phase:regalloc carries about +1,880 allocations at rung 0 outside every sub-phase probe, ColdCallBracketPlan.create() per function beside ReuseCopyPlan.create(). fannkuch-redux compiles to byte-identical IR and exe with and without the union removal: 49 bracketed cold calls, 230 saves and 230 reloads, every one in an __im_slow arm, six registers the mode. | 3,935,329 | 5,854,971 | 9,690,044 | 17,363,104 | 32,714,359 | 63,455,645 |
<!-- scale-history:allocations -->

## Bytes

| date | minted at | change | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-09-10 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | OpVariantFacts sized to the whole TargetOp union at creation: one memo lives per regalloc worker, and which functions a worker gets is timing-dependent, so its lazy growth made regalloc memory differ between two compiles of one rung and every ladder since the pool landed read BROKEN RUN. Now identical across repeats. The module-level memo only refuseForRegisterPressure read is gone, and dead allocateOnThisThread/allocateFunction removed. First readable ladder after 6c6ac5c82d (spawn-aware stdlib reachability; this corpus spawns no service, so that change reads 0 here). Allocation totals scale with the host's processor count (one regalloc worker per processor): compare rows from same-core-count hosts only. | 286,905,620 | 457,666,956 | 814,808,692 | 1,534,223,990 | 3,011,957,699 | 5,932,257,299 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | unswitchInvariantGuards (the new Std loop-versioning pass after LICM) made LINEAR in loop count. Its first cut walked the FUNCTION per admitted loop three times over: the exit-phi substitution rebuilt every op of every block per loop, and two function-sized columns (the hoist numbering's canonical map, the clone's value/block substitution columns) were built per loop - x3.70 x3.84 x3.91 allocations across 50/100/200/400 versionable loops in ONE function on tests/ladders/genunswitch.sh, and +400,000 allocations for +4,000 pad ops at a fixed 100 loops. Now an escaping value's reads come off a per-function use index (op sites and edge sites, two CSRs built once), one identity column pair per function is restored after each rewrite in O(what it mapped), and the canonical map is a stamped per-loop map: x2.01 x1.97 x2.00, and flat under pad. THE CORPUS IS BLIND TO THE PASS: no rung holds an element store inside a loop, so it versions 0 loops at rung 0 and rung 5 alike (identical refusal tally, the runtime's own loops). This row's phase:unswitchInvariantGuards moves only by the per-function gather on that constant set of functions: +228 allocations and +29,480 bytes, the same at every rung (the second CSR and the two site columns per gathered function, less the liveOpIndices walk a second nextFreeValueId used to pay). | 285,895,575 | 457,089,459 | 813,455,497 | 1,533,503,113 | 3,010,366,983 | 5,934,016,277 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | refineVersionedRanges (the second refineValueRanges run, after unswitchInvariantGuards) is SCOPED to the functions that pass rewrote: unswitchInvariantGuards answers a per-function column (RewrittenFunctions) that the pipeline hands the second run as its RangeRunScope, so a function versioning left alone - the shape the first run already read - pays nothing. Before, the second run rebuilt the dominator tree, stamps, shape reader and eight value columns for every function holding a word compare: 19,442 / 23,011 / 29,822 / 42,946 / 68,740 / 119,705 allocations (x1.74) and 3.8M to 122M bytes, CPU 45M to 1.2G ticks; now 46 allocations and 2,212 bytes at every rung (the scratch record and its empty columns). The corpus versions 0 loops so the phase visits 0 functions here; on the self-compile it visits 61 of 12,692 and decides the same 44 compares in the same 23 functions as the unscoped run, and 13 hand probes (N straight-line checked accesses at N = 100..800, N versionable loops at 50..400 from tests/ladders/genunswitch.sh, and a for-i-in-0-upto-a.count() twin with 0/2000/4000 pad calls before the loops) emit byte-identical executables before and after. Costs of the contract: the first run pays +1 allocation per module for the scope union, unswitchInvariantGuards +4 allocations and one byte per function for the column. unswitchInvariantGuards also reads the preheader BACKWARDS in preheaderLoadOf (one scan that stops at the nearest load of the field or the first header write, where it was two full scans per hoisted load): the same answer on every probe, and the iterator each withIterator() scan allocated is gone - about 20 allocations per versioned loop on the probes (cl-50 unswitchInvariantGuards 35,739 to 34,731, us-100 76,250 to 74,638), invisible on this corpus, which versions 0 loops. Both runs were already LINEAR before this row - first run x1.81 allocations and x2.09 bytes here, second run x1.74 and x2.09 - and on the acc probes the first run bytes grow x2.07 / x2.28 / x2.15 / x1.96 / x2.16 across 100..3200 accesses on a HEAD control binary as well, the alternating ratio being capacity rounding shared with threadConstantBranches and CSE rather than a term of this change. | 287,387,049 | 459,843,382 | 819,175,799 | 1,545,266,863 | 3,035,674,070 | 5,986,222,964 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | Cold-call spilling (round 6): a call in a cold block (BlockHeat.cold — an inlined access's slow arm, a range check's panic, an otherwise-panic handler) no longer confines the values live across it; the colorer plans a save/reload bracket per caller-saved register held across it (ColdCallBracketPlan) and the rewrite splices storeSlotReg/loadRegSlot around the call. The optimize pass then removed two heap boxes from the rewrite's per-op path: ColdCallBracketPlan.takeAt answered a payload-bearing union (ColdCallBracketLookup.none, boxed once per body op) and ReuseCopyPlan.at answered ReuseCopyLookup.none the same way — both answer a bool now (a probe shows Lookup.none calling __mm_alloc exactly as the payload case does) — and the plan's per-register slot tables are sized on a pass's first mint instead of at beginPass. regalloc:rewrite vs the last row: 107,668 to 86,607 allocations at rung 0 and 2,447,890 to 1,973,144 at rung 5, x1.97 unchanged; the ladder taken before the union removal read 135,089 / 3,061,519, so the bracket ops themselves cost about 3,180 allocations at rung 0 and the remainder was the box. regalloc:liveness +66 at every rung is the isCall column of OpVariantFacts, grown per learned variant and not per op; regalloc:coloring +10 to +30 (+4 per rung) is the same column grown where a variant is first learned inside the coloring probe; regalloc:ssaDestruction -40 / -80 / ... / -1280 (halving downward, so per guarded site) is fewer phi-edge moves once the corpus's checked accesses keep their values in caller-saved registers across the cold call; phase:regalloc carries about +1,880 allocations at rung 0 outside every sub-phase probe, ColdCallBracketPlan.create() per function beside ReuseCopyPlan.create(). fannkuch-redux compiles to byte-identical IR and exe with and without the union removal: 49 bracketed cold calls, 230 saves and 230 reloads, every one in an __im_slow arm, six registers the mode. | 287,364,587 | 459,688,064 | 818,755,751 | 1,544,349,767 | 3,033,800,776 | 5,982,377,871 |
<!-- scale-history:bytes -->

## CPU

**CPU ticks consumed by the compiling thread, per rung.** Added 2026-07-25. This is the only column
here that can see a cost which **allocates nothing** — the class of defect the two tables above are
structurally blind to, and the class that every time-only quadratic in `maxon-bin/PLAN.md` belongs
to (`requireInterfaceForParse` was measured with allocations *identical to the digit*, 1,417,523 both
ways, against a +24.15 ms parse delta).

**It is not wall time, and the distinction is the whole reason it can be here.** Wall time counts
every other process on the box; this counts only the thread doing the compiling, so preemption is
invisible to it. A run whose parse phase reads ×5.03 then ×1.78 on a doubling ladder — which happened
— was measuring the machine.

> ### ⚠ HOW TO READ THIS TABLE, AND IT IS NOT LIKE THE OTHER TWO
>
> **Allocations and bytes are exact: any movement is real. This column has a noise band, and a
> movement inside it is NOT a datapoint.**
>
> **The band, as measured rather than assumed** (three idle runs of `--rungs=3 --repeat=3`, plus one
> against a machine loaded to every core):
>
> | | reading |
> | --- | --- |
> | phases > 50M ticks, idle | **median 1.7%, worst 6.3%** |
> | small phases (~100K ticks), idle | up to **31%** — never read a ratio off one |
> | per-rung total, idle | 6.7% / 2.5% / 0.3% (larger rungs are quieter) |
> | per-rung total, **fully loaded box** | **+49% … +52%** |
> | rung-over-rung RATIO, loaded vs idle | **×1.90/×1.98 vs ×1.86/×2.00** — essentially unchanged |
>
> ⚠ **So "it is immune to a busy machine" is FALSE, and the true statement is better.** This counter
> ignores *preemption*, not *contention*: other cores fighting for cache and SMT siblings make the
> same work take half again as many ticks. What survives is the **ratio**, because the inflation is
> uniform — which is exactly the number the doubling ladder is read for. Trend ratios; treat absolutes
> as a fact about one machine on one day.
>
> Against a claimed few-percent constant-factor win this column is worth nothing — use the allocation
> columns, which are exact. **Memory was bit-identical across all four runs, including the loaded
> one**, which is both the reason those columns are trusted and a live check that the compiler is
> deterministic (`--repeat` asserts it on every run).
>
> **The unit is platform-defined** — TSC ticks on Windows, nanoseconds on POSIX — and there is no
> honest conversion between them (`QueryPerformanceFrequency` is the performance counter's rate, not
> the TSC's). **So compare RATIOS between rungs, which are unit-free, and absolutes only against rows
> measured on the same platform.**

| date | minted at | change | rung 0 | rung 1 | rung 2 | rung 3 | rung 4 | rung 5 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2026-09-10 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | OpVariantFacts sized to the whole TargetOp union at creation: one memo lives per regalloc worker, and which functions a worker gets is timing-dependent, so its lazy growth made regalloc memory differ between two compiles of one rung and every ladder since the pool landed read BROKEN RUN. Now identical across repeats. The module-level memo only refuseForRegisterPressure read is gone, and dead allocateOnThisThread/allocateFunction removed. First readable ladder after 6c6ac5c82d (spawn-aware stdlib reachability; this corpus spawns no service, so that change reads 0 here). Allocation totals scale with the host's processor count (one regalloc worker per processor): compare rows from same-core-count hosts only. | 2,225,010,200 | 3,157,755,920 | 5,287,627,420 | 9,733,381,180 | 19,226,164,980 | 38,825,518,080 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | unswitchInvariantGuards (the new Std loop-versioning pass after LICM) made LINEAR in loop count. Its first cut walked the FUNCTION per admitted loop three times over: the exit-phi substitution rebuilt every op of every block per loop, and two function-sized columns (the hoist numbering's canonical map, the clone's value/block substitution columns) were built per loop - x3.70 x3.84 x3.91 allocations across 50/100/200/400 versionable loops in ONE function on tests/ladders/genunswitch.sh, and +400,000 allocations for +4,000 pad ops at a fixed 100 loops. Now an escaping value's reads come off a per-function use index (op sites and edge sites, two CSRs built once), one identity column pair per function is restored after each rewrite in O(what it mapped), and the canonical map is a stamped per-loop map: x2.01 x1.97 x2.00, and flat under pad. THE CORPUS IS BLIND TO THE PASS: no rung holds an element store inside a loop, so it versions 0 loops at rung 0 and rung 5 alike (identical refusal tally, the runtime's own loops). This row's phase:unswitchInvariantGuards moves only by the per-function gather on that constant set of functions: +228 allocations and +29,480 bytes, the same at every rung (the second CSR and the two site columns per gathered function, less the liveOpIndices walk a second nextFreeValueId used to pay). | 2,529,361,216 | 3,593,149,487 | 6,119,176,635 | 11,149,241,386 | 22,121,325,078 | 43,933,707,044 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | refineVersionedRanges (the second refineValueRanges run, after unswitchInvariantGuards) is SCOPED to the functions that pass rewrote: unswitchInvariantGuards answers a per-function column (RewrittenFunctions) that the pipeline hands the second run as its RangeRunScope, so a function versioning left alone - the shape the first run already read - pays nothing. Before, the second run rebuilt the dominator tree, stamps, shape reader and eight value columns for every function holding a word compare: 19,442 / 23,011 / 29,822 / 42,946 / 68,740 / 119,705 allocations (x1.74) and 3.8M to 122M bytes, CPU 45M to 1.2G ticks; now 46 allocations and 2,212 bytes at every rung (the scratch record and its empty columns). The corpus versions 0 loops so the phase visits 0 functions here; on the self-compile it visits 61 of 12,692 and decides the same 44 compares in the same 23 functions as the unscoped run, and 13 hand probes (N straight-line checked accesses at N = 100..800, N versionable loops at 50..400 from tests/ladders/genunswitch.sh, and a for-i-in-0-upto-a.count() twin with 0/2000/4000 pad calls before the loops) emit byte-identical executables before and after. Costs of the contract: the first run pays +1 allocation per module for the scope union, unswitchInvariantGuards +4 allocations and one byte per function for the column. unswitchInvariantGuards also reads the preheader BACKWARDS in preheaderLoadOf (one scan that stops at the nearest load of the field or the first header write, where it was two full scans per hoisted load): the same answer on every probe, and the iterator each withIterator() scan allocated is gone - about 20 allocations per versioned loop on the probes (cl-50 unswitchInvariantGuards 35,739 to 34,731, us-100 76,250 to 74,638), invisible on this corpus, which versions 0 loops. Both runs were already LINEAR before this row - first run x1.81 allocations and x2.09 bytes here, second run x1.74 and x2.09 - and on the acc probes the first run bytes grow x2.07 / x2.28 / x2.15 / x1.96 / x2.16 across 100..3200 accesses on a HEAD control binary as well, the alternating ratio being capacity rounding shared with threadConstantBranches and CSE rather than a term of this change. | 2,533,282,593 | 3,614,129,889 | 6,295,155,499 | 10,960,330,175 | 21,972,866,899 | 43,869,224,812 |
| 2026-09-09 | C:\Users\Eric\Dev\maxon\maxon-bin\.maxon\maxon.exe (corpus C:\Users\Eric\Dev\maxon\.scale-tmp) | Cold-call spilling (round 6): a call in a cold block (BlockHeat.cold — an inlined access's slow arm, a range check's panic, an otherwise-panic handler) no longer confines the values live across it; the colorer plans a save/reload bracket per caller-saved register held across it (ColdCallBracketPlan) and the rewrite splices storeSlotReg/loadRegSlot around the call. The optimize pass then removed two heap boxes from the rewrite's per-op path: ColdCallBracketPlan.takeAt answered a payload-bearing union (ColdCallBracketLookup.none, boxed once per body op) and ReuseCopyPlan.at answered ReuseCopyLookup.none the same way — both answer a bool now (a probe shows Lookup.none calling __mm_alloc exactly as the payload case does) — and the plan's per-register slot tables are sized on a pass's first mint instead of at beginPass. regalloc:rewrite vs the last row: 107,668 to 86,607 allocations at rung 0 and 2,447,890 to 1,973,144 at rung 5, x1.97 unchanged; the ladder taken before the union removal read 135,089 / 3,061,519, so the bracket ops themselves cost about 3,180 allocations at rung 0 and the remainder was the box. regalloc:liveness +66 at every rung is the isCall column of OpVariantFacts, grown per learned variant and not per op; regalloc:coloring +10 to +30 (+4 per rung) is the same column grown where a variant is first learned inside the coloring probe; regalloc:ssaDestruction -40 / -80 / ... / -1280 (halving downward, so per guarded site) is fewer phi-edge moves once the corpus's checked accesses keep their values in caller-saved registers across the cold call; phase:regalloc carries about +1,880 allocations at rung 0 outside every sub-phase probe, ColdCallBracketPlan.create() per function beside ReuseCopyPlan.create(). fannkuch-redux compiles to byte-identical IR and exe with and without the union removal: 49 bracketed cold calls, 230 saves and 230 reloads, every one in an __im_slow arm, six registers the mode. | 2,548,189,260 | 3,617,481,151 | 6,109,423,817 | 11,533,955,880 | 22,179,006,134 | 44,769,649,410 |
<!-- scale-history:cpu -->

Since the suite was introduced, rung 5 has gone **36,897,948 → 14,509,321 allocations** (−61%) and
**2.86 GB → 1.52 GB** (−47%).

## Exponents — CLOSED 2026-07-14. Kept as history; no longer written.

**The tool no longer fits exponents, and no longer records time at all.** The row below is the only
one that was ever machine-written, and it is left exactly as it was measured. Deleting honestly-taken
numbers to tidy up a format change would be the worst of both worlds; they are simply not extended.

Two reasons it went, and they are the same reason twice:

**The ladder DOUBLES, so the ratio between two rungs already IS the growth.** x2.00 is linear, x4.00
is quadratic — read straight off the allocation counts in the tables above, no fit required. An
exponent could tell you nothing those numbers had not already told you, and it dragged in a residual,
which dragged in a NOISY verdict, which got an exemption written for it so it would stop complaining.
That is optimizing the gauge instead of the engine.

**And time can never be trended.** It is machine-dependent, so a dated column of it compares a loaded
box in July against an idle one in August. The `time /` half of every cell below is a fact about the
machine that ran it. (The compiler still times itself — `--log=compiler:debug` and `--metrics=<path>`
report per-phase milliseconds for one compile, which is where a clock is worth reading.)

What replaced it: `scale-test` prints per-phase **allocations and bytes at every rung**, with the
rung-over-rung ratio beside them. `regalloc:liveness` reads **x2.80** and `regalloc:splitting`
**x2.52** where every other phase sits on **x1.98** — the known-superlinear splitter, visible without
a single fitted number.

| date | change | phase:load | phase:lex | phase:parse | phase:merge | phase:resolveTypes | phase:semanticCheck | phase:lowerMaxonToStd | phase:pruneDeadBlockArgs | phase:elimTrivialBlockArgs | phase:foldConstOperands | phase:sugarGate | phase:isel | phase:regalloc | phase:prologueEpilogue | phase:runtimeAugment | phase:encode | phase:link | phase:writeExe | phase:emitIr | regalloc:criticalEdges | regalloc:blockOrder | regalloc:splitting | regalloc:liveness | regalloc:coloring | regalloc:ssaDestruction | regalloc:rewrite | aggregate |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |

## Notes on the changes

The four earliest rows predate the automated log; their numbers are reconstructed from the diffs in
git, so they are accurate but were not written by the tool. They also predate the exponent table,
which is why it starts empty.

**2026-09-01 — W219: THE DRIVER'S TIMED WAITS BECAME INTERRUPTIBLE. A RUNTIME row, and the FIRST one here
whose subject is LATENCY rather than throughput — read MC1's row below first, because this one is its
mirror image and the two pull in opposite directions.** MC1 removed wakeup traffic; this adds a wakeup
back, on the one path where its absence was a WRONG ANSWER. `scale-test` has nothing to say about either.

**What it is.** `__gt_drive_until`'s netpoll has two arms where the driver has nothing runnable and nothing
to WAIT ON — `awaitOtherM` (another M is executing, no timer) and `sleepwait` (a timer deadline). Both were
`osSleepMs`, which waits on no object and therefore cannot be ended early. They are now one shared park:
publish this driver's owner on its P, step `__sched_parked_drivers` (the `lock xadd` is the Dekker pair's
StoreLoad barrier), RE-TEST the exit condition, then `osWaitHandle` on the P's own wake event with the old
interval as the TIMEOUT. `__gt_runner_done` — the one door every finished green thread and every answered
reply passes through — calls `__sched_wake_parked_drivers(owner)`, which is gated to one load when nobody is
parked.

**The defect it closes is a WRONG ANSWER**, and the reproducer
(`scripts/multicore-stress/awaitany-index-torture.maxon` + `awaitany-index-race.sh`) is committed with it.
⚠ **THE NUMBERS ARE IN `scripts/multicore-stress/README.md` UNDER "W219's READINGS" AND ARE NOT REPEATED HERE** —
the reproducer's rate before and after, the 80 ms variant's latency, the wake-removed control and the
send-and-await wall times. That section exists because the first cut of this change scattered five copies of
those readings and two of them disagreed about which configuration they were quoting.

The shape, without the numbers: unfixed, the driver slept through the reply, its own deadline fired during
that sleep, and `awaitAny` then answered the promise that finished SECOND; fixed, the select is clean at
every processor count. Removing only the WAKE — keeping the interruptible wait — puts the reproducer back in
the red, which is what says the signal rather than the wait primitive is the fix. And the throughput effect
is the opposite sign from what an added wakeup suggests, by a factor of sixty: `Sleep(1)` returns on
Windows' scheduler tick, so every await in a send-and-await loop paid one, with process CPU near zero in
both arms — the before arm's wall time was spent NOT RUNNING.

**CPU: free at this resolution, and that is TWO interleaved sessions plus a null control each time.**
`service-torture` at 480,000 messages (`rounds = 40000`), the same `GetProcessTimes` harness MC1's row
describes, medians of 5 and of 7 at `MAXON_MAX_PROCS=16`: session 1 reads **before 812 → after 891** and
session 2 **before 969 → after 844** — the sign FLIPS, which is what a reading inside the band looks like.
The null controls (same bytes in both arms) read **1016 / 938** and **906 / 938** in those same sessions, so
the band on this box is ±8% and both real readings sit inside it. N=1 is **172 both ways** — the control,
since one processor never parks a driver against another M — N=4 reads 750 → 734, wall is 270-275
throughout, `steals` 139k, and `aggregate=5226680` is byte-identical across all 48 runs at exit 42.
⚠ **`service-torture` DOES NOT REACH THE REPLY PATH** (SV1 sends are fire-and-forget, so no reply cell is
ever completed), which is exactly why the second corpus above exists and why its numbers are the ones that
bound the added cost.

**What it costs, stated as code rather than as a number.** One `.data` word (`__sched_parked_drivers`,
`atomicStep`), one P field (`POffParkedDriverOwner`), a load-compare-branch at the tail of
`__gt_runner_done`, and — only when a driver is actually parked — a walk of `__sched_procs` comparing one
word per P. The `.data` word moves the `data {` section of every green-thread golden in the corpus: **331
fragments re-minted across 51 filtered batches**, all of them one label at one offset, and a full run
afterwards reports none still differing.

⛔ **ONE BLIND SLEEP IS KEPT ON PURPOSE, AND IT IS THE DEADLOCK DETECTOR'S.** `confirmQuiet`'s not-proved
edge (`GtRuntime`'s `confirmWait`) still calls `osSleepMs`, because `DeadlockConfirmPolls` was bisected
against that call's real ~35 ms TICK and an interruptible wait there could let sixteen consecutive quiet
polls elapse in microseconds — exit 92 on a live program. It also buys nothing: that edge is reached only
when no M is executing and there is no timer, no parked child and no read in flight, so no party exists that
could signal it. The constant's *"re-bisect it if the poll changes"* is discharged by not changing the poll.

⚠ **arm64-macOS IS CROSS-COMPILED AND DISASSEMBLED, NOT RUN** (Windows host), and it is the lane where the
ordering matters most. The park reads `str x24, [x22, #0xc0]` (arm the slot) → `ldaxr`/`add`/**`stlxr`** (the
gate step, whose STORE-RELEASE is the barrier x86 gets from the `lock` prefix) → `ldr x0, [x19, #0x10]` (the
exit re-test) → `_mrt_darwin_wait_one` on `#0x38` → the atomic decrement → `str xzr, [x22, #0xc0]`.
`__sched_wake_parked_drivers` lowers to the gate load, the bound hoisted out of the loop, `#0xc0` compared
per P and `_mrt_darwin_event_set` on `#0x38`. A census of the emitted image finds **exactly one
`sleep_ms` per drive loop** — the three `confirmWait`s — and none anywhere else in the driver.

**2026-09-01 — MC1: SPINNING-M ACCOUNTING. A RUNTIME row, not a compiler one — no `scale-test` numbers
exist for it and none should be looked for.** `scale-test` measures the COMPILER compiling; this change is
in the scheduler the compiler EMITS, and its subject is a program with twelve services and half a million
messages. The instrument is `scripts/multicore-stress/service-torture.maxon` scaled to 480,000 sends
(`rounds = 40000`), driven by a PowerShell harness that reads **process-wide CPU off `GetProcessTimes`**
(`TotalProcessorTime` / `UserProcessorTime` / `PrivilegedProcessorTime`) rather than a wall clock, with the
two binaries **interleaved rep by rep in one session**. Medians of 3 reps, all times in ms:

| `MAXON_MAX_PROCS` | | CPU | user | kernel | wall | steals |
|---|---|---:|---:|---:|---:|---:|
| 1 | before | 172 | 125 | 47 | 176 | 0 |
| 1 | **after** | **156** | 125 | 31 | **168** | 0 |
| 4 | before | 1516 | 766 | 781 | 604 | 443,053 |
| 4 | **after** | **797** | 406 | **297** | **251** | **120,801** |
| 16 | before | 3172 | 1172 | 2000 | 1244 | 812,867 |
| 16 | **after** | **859** | 531 | **281** | **269** | **143,229** |

**At sixteen processors: −73% CPU, −86% KERNEL, −78% wall, −82% steals, and the aggregate byte-identical
at every count.** The wall gap against one processor closes from ×7.1 to ×1.60. ⚠ **THE NOISE BAND ON THIS
BOX IS ±5-10% ON CPU AND 25-30% ON WALL**, which is why wall is reported but never argued from; every
number above is outside the CPU band by an order of magnitude, and the N=1 row — unchanged, as it must be,
since one processor reaches none of this code — is the control.

**What it is.** Go's `sched.nmspinning` (`proc.go:3228`), ported with its *"delicate dance"*
(`proc.go:3638-3673`): a publisher that sees an M already looking for work skips the wakeup entirely, and
an M going non-spinning decrements FIRST and re-checks every source afterwards. Before it, every publish
`SetEvent`ed an idle M and the woken M found the queue already drained as often as not — one kernel
round trip per message, on work finer-grained than the round trip.

**What it is NOT: the lock — and that reading is INHERITED, not MC1's.** The measure-first pass that
preceded this change built an ablation with `emitSchedLockEnter`/`Leave` emptied — no scheduler lock at
all, the ceiling any lock work could reach — and read **−5.7%** of total CPU at N=16 with the kernel column
barely moved (1953 → 1844), that build exiting 101/139 at N≥2 as the positive control that the lock is
load-bearing. MC1 did not re-take it and the ablation is not committed; it is written down here because it
is the reason the lock was not what got changed.

**Sabotaged, on the column that discriminates.** With the reservation's give-back deleted (one line), the
count leaks on the first exhausted scan and no publish ever wakes anybody again: `steals` reads **12 at
`MAXON_MAX_PROCS=2` and 21-30 at 4, against ~79,000 and ~120,000** with it, the workers sitting out their
park timeouts. ⚠ It is invisible at N=16, and `pin-matrix.sh` does not catch it either — `steals=12`
satisfies its `steals > 0` family assertion. The steal COUNT is the instrument, not the family.

**Two things were measured and deliberately NOT built, and the numbers are the reason.**

- **Go's `pidleget`/`pidleput` idle-P list (`proc.go:7425`/`7454`).** An instrumented build counted every
  `idleFlag` load in the wake scan: **108,322 for the whole 480,000-message run at N=16** — FEWER than the
  **298,552** at N=2, because more processors means the first idle P is found sooner and the gate
  suppresses more calls outright. Against 797-953 ms of CPU an O(1) list can save single-digit
  milliseconds, and it would have to buy them with a lock-free stack whose ABA argument is harder than the
  `idleFlag` protocol it replaces — on the one path that deliberately runs with the run-queue lock
  RELEASED, because that release is the StoreLoad fence.
- **`runnext` (`POffRunnext`, reserved since W212).** Whether the compiler's cooperative model makes Go's
  no-`sysmon` objection inapplicable was settled by a probe rather than by argument:
  `scripts/multicore-stress/runnext-starvation-probe.maxon`, one program, two compilers, `MAXON_MAX_PROCS=1` —
  shipped compiler runs the bystander FIRST on 3 of 3, an experimental `runnext` runs it LAST on 3 of 3,
  after all 4,000 bounces. The 61-schedule fairness check does not rescue it: it consults the GLOBAL
  queue, and the starved thread is in a P's LOCAL RING. The slot stays reserved for W213.

**RE-MEASURED AT MC1's REVIEW, WHICH FOUND A STRANDED CREDIT AND CLOSED IT — AND THE NUMBERS DID NOT
MOVE.** An M that parks publishes `idleFlag = 1` before its re-search; a waker can win that flag and hand
the P a credit in the gap, and if the M's OWN re-search then succeeds it used to run a green thread with
that credit standing on its P — so the gate above wakes nobody for the whole duration of that thread while
nobody is searching (self-healing at the M's next search, or at `ParkTimeoutIdleMs`). Go calls
`resetspinning` before `execute` and does not have the window; `wredeq` now releases through the same CAS,
which steps by zero where no waker intervened. **It was MEASURED, not argued**: a probe build counting the
reclaim reads **5-16 occurrences per 4,800-message run of `service-torture` at N ∈ {2, 4, 12, 16}**, and a
second probe that only READS the word in the unfixed build agrees to within a few counts, so the fix fires
on essentially every occurrence the old code stranded.

The A/B above was then re-taken against the fixed binary, same harness, two interleaved sessions on one
box — medians of 3 and of 5. At N=16 it reads **CPU 891 → 812 (3 reps) and 953 → 922 (5 reps)**, at N=4
**703 → 766 then 750 → 656** — the N=4 sign FLIPS between sessions, which is what a reading inside the
±5-10% band looks like — and N=1 is 172 both ways, the control. `aggregate=5226680` is byte-identical
across all 32 runs and the exit code is 42 throughout. ⇒ **the fix is free at this resolution**, which is
what one `lock cmpxchg` on a path taken a few thousand times in half a million messages should be.

**What still costs, and it is not what an idle-P list would fix.** The residual at N=16 is ~640 ms of CPU
over the one-processor run, and it grows only mildly with the count (734 → 953 ms from N=4 to N=16 while
`numProcs` goes ×4). The work-conservation hand-off — an M that finds work waking another, Go's
`resetspinning` → `wakep` — is a third of it: deleting it reads **625 ms against 922 at an identical wall
and MORE steals**, and it is kept anyway, because what it buys is bounded by `ParkTimeoutIdleMs` and a
bursty producer that goes quiet would otherwise wait 100 ms for its second M. That trade is the next
thing to measure here, and it needs a LATENCY corpus `multicore-stress/` does not have.

**2026-08-11 — CORPUS CHANGE, NO ROW: the scaling corpus grew one statement per SCOPE-FILLER LOCAL, so
rows above this line are not comparable term for term with rows below it.** The `var-should-be-let` spec
port made E3077 (a `var` that is never reassigned) a compile error, and `ScaleCorpus.fillerLocalsDecl`
emitted exactly that shape — the whole ladder stopped compiling, reported as a BROKEN RUN. The filler MUST
stay a `var` or the V dimension it exists to grow disappears (a `let` leaves the parser's mutable-var set,
which IS the O(V) cost), so each filler is now declared empty and assigned (`var lN = 0` / `lN = a + N`)
instead of initialized in one statement. Values, the witness, the knob and the mutable-var count are all
unchanged; the SOURCE and the statement count are not.

No row was appended, deliberately: a row minted across a corpus change measures the instrument and the
compiler at once and can be attributed to neither. The last rows against the one-statement filler are
ARR1/ARRR/ARRG/ARR3a-c (2026-08-11); the next row minted after this note is the new baseline, and a reader
comparing across this line is comparing two different ladders. The tip was read at the port (6 rungs,
`--repeat=1`) and every phase is x1.9-2.0 across a doubling ladder — `phase:parse` x1.94 allocations and
x1.89 CPU — so nothing about the new rule bends a curve; the numbers simply start from a different place.

**2026-08-07 — W41 optimization pass. NO CHANGE SHIPPED, and this note is the result.** No row was
appended because nothing moved the corpus ladder: the pass measured the three constant factors W41's
implementers flagged in words, plus the paths the generated corpus structurally cannot reach, and
every one of them is already linear. It also found two PRE-EXISTING defects, both confirmed against a
pre-W41 build made like-for-like in the same directory. Recorded here because the measurements exist
and would otherwise have to be re-taken.

*The three flagged constant factors — all measured, none worth changing:*

- **The inert `__retain_type_param` on a trivial instantiation costs NOTHING MEASURABLE, so the
  whole-program prune pass that would remove it should not be built.** A/B of the same program written
  generically (`Holder with Integer`, whose `entryAt` builds a `(Element, Integer)` tuple from a
  borrowed opaque, and whose emitted IR was confirmed to contain `x64.callDirect
  __retain_type_param`) against its concrete twin, **20,000,000 executions of that site**: generic
  1147 / 1166 / 1162 ms, concrete 1175 / 1147 / 1179 ms. The generic is not slower — the difference is
  inside noise and the sign favours the generic. Code size 6,012 vs 5,876 bytes, and that +136 is an
  UPPER bound because it also carries every other generic-path difference, not just the call. A
  descriptor word of 0 makes the callee a load, a test and a return, and 20M of them do not show.
- **`substituteInstanceArgsThrough`'s new per-argument interner probe is two O(1) non-allocating
  operations.** `TypeNameInterner.nameOf` is `names.get(id)` — an array index handing back the interned
  `ByteArray`, no copy — and `isTupleTypeName` is a 7-byte prefix compare. Both run only for a
  `structRef`-tagged argument. On `genopaquefields.sh` (25→400 units) `phase:semanticCheck` is
  1.0 / 1.6 / 2.8 / 4.9 / 10.4 ms, ratios ×1.60 ×1.75 ×1.75 ×2.12 — linear.
- **`closeDestructorNeeds` is NOT O(referenced cascades × instances); that premise is stale by one
  refactor.** The shared `CascadeWorklist` (`Compiler/Runtime/CascadeNeeds.maxon`) registers each node
  once and expands it at most once, so the closure is O(types + instances + fields), and it early-outs
  entirely when nothing drops. Measured on `genopaquefields.sh`: `phase:deriveRuntimeNeeds`
  allocations 6,119 / 7,899 / 11,415 / 18,447 / 32,473 at 25 / 50 / 100 / 200 / 400 units — a per-unit
  delta of **71.2, 70.3, 70.3, 70.1**, dead flat. Listing `Map.maxon` added a constant number of nodes
  to a linear registration; it multiplied nothing.

*What the corpus cannot see, measured directly (`stdlib/Map.maxon`'s own algorithms are now in a user
program's hot path for the first time):* `Map with (Count, Count)` built, looked up and iterated at
25k → 400k entries runs 91 / 118 / 159 / 266 / 500 ms wall; net of the ~60 ms fixed process cost that
is ×1.87 ×1.71 ×2.08 ×2.14 per doubling — **linear**. `findSlot`'s probe, `grow()`'s amortized rehash
and `ensureCapacity`'s 75% load factor all behave; final capacity is within 2× of entries, which is
the doubling table's expected slack and not a growth term.

### ⛔⛔ Measured debt found by this pass — PRE-EXISTING, both confirmed against a pre-W41 build

- **A BORROWED OPAQUE `T` HANDED TO A SELF-SIBLING'S PARAMETER ESCAPES THE ARRAY MOVE-IN DOOR
  ENTIRELY, AND `stdlib/Map.maxon` DOES EXACTLY THAT.** The refusal is intra-procedural: it demands the
  value at the store be owned, a parameter answers `valueIsOwnedHeap` true, and nothing checks the
  caller. `Map.grow()` → `insertAtSlot()` is one such hop, so **a `Map` with any managed column double
  frees every entry the moment it crosses its load factor** — `Map with (String, Count)` prints the
  right answer at 12 entries and exits 139 at 13. Reproduced with a 45-line generic holding no `Map` at
  all, on BOTH binaries, so the compiler defect predates W41 — but the same program is **exit 0 at
  128,000 entries** on a pre-W41 build, whose synthesized `Map` had no such body, so **W41 regressed the
  reachable set**. Suite 4769/0 over it: no spec case builds a managed-column map past the load factor.
  Full diagnosis, the reproducer and the cure W41 itself already built (`retainFunc@64` +
  `coOwnBorrowedOpaqueForConsume`, which the field-store door takes and this door does not) are in
  `Parser.referenceBorrowedOpaqueElement`'s header (that door was `requireOwnedOpaqueElement` when this row
  was written; W60 renamed it when the borrowed store stopped being a refusal). **This is a live miscompile,
  not a perf note.**
- **AN INSTANCE ARGUMENT TREE THAT IS A SHARED DAG MAKES `phase:signatures` EXPONENTIAL IN LINES OF
  SOURCE** (`genshareddag.sh`). `typealias P<k> = Pair with (P<k-1>, P<k-1>)` gives the deepest alias a
  structural mangled name of 2^k characters. `phase:signatures` bytes at depth 18 / 20 / 22:
  **36,156,421 → 139,977,003 → 555,220,169**, i.e. **×2 per ADDED LINE**, and CPU 180.4 → 620.9 →
  2410.8 ms (97.4% of the compile at depth 22, on a 31-line file). The `control` mode — same
  declaration count, same instance count, byte-identical allocation counts (31,441 / 23,514), second
  argument a leaf instead of the shared alias — is **1,557,391 → 1,584,775 bytes, dead flat**, which is
  what isolates it to name LENGTH over a shared DAG rather than to a walk. Allocation COUNT is flat in
  both modes; only bytes move. Depth 25 is ~4.4 GB, past the 1.7 GB budget. W41 is exactly flat against
  it (+~250 KB at every depth, independent of depth — the listed module's own constant). **Re-measure
  trigger:** any machine-generated or deeply-composed generic type, or a program that hits the memory
  budget in `phase:signatures`. **Named cure:** the digest that tuple names already use
  (`__Tuple2.Tfcd8090be60770aa.…`, W14) applied to generic-instance mangling.

### ⛔⛔ Measured debt found by the spec-port optimization pass (2026-09-04) — THREE terms. The first two predate this change; the third is a PRE-EXISTING gate whose cost this change newly EXPOSES, because the union `.clone()` door added here routes it through an allocating classifier.

Found by hand-built ladders written to fill the blind spots the 2026-09-04 row above states, on the tree
binary at `maxon-bin/.maxon/maxon`. Every ladder DOUBLES, so the ratio between consecutive MARGINAL
costs (the delta a rung adds over the one below) is the growth: ×2 linear, ×4 quadratic. Every ratio below
is that marginal ratio unless it says otherwise. Allocation counts reproduce byte-for-byte across runs; CPU
is a single sample and moves a few percent.

- ✅ **FIRST, WHAT IS NOT DEBT — the two blind spots that row states are now MEASURED, and both are
  LINEAR.** A ladder of `t = t + Helper.f(3)` statements (N = 1k/2k/4k/8k) puts `phase:parse` marginal
  allocations at **+101,074 → +202,074 → +404,075** — ×1.999, ×2.000, i.e. a flat **101 allocations per
  added statement**, of which **81** is the qualified-static-call's own premium over the same statement
  written `t = t + 3` (a free call's premium is 49; the no-call floor is 316,865). A ladder of
  `let p = async H.bump(1); t = await p` against the identical ladder written `async bump(1)` puts the
  QUALIFIED-spawn premium at **+9,808 / +19,558 / +39,058 / +78,058 allocations — exactly 39 per site,
  flat at every rung**. ⇒ The two boxed unions the resolution split mints per qualified call
  (`CompilerOwnedStaticSurface`, `QualifiedStaticCallee` — a payload-free arm of a boxed union still
  heap-allocates, which is what `ArgLabelRule`'s header measures) are **2 of 81, and constant**. A ladder
  of `let ops = [Op.add(k), …]` (N = 250…2000) puts `phase:signatures` marginal allocations at
  **+13,002 → +26,004 → +52,004**, ×2.0000 twice.

- **`async`/`await` PARSE IS QUADRATIC IN SPAWN SITES PER FUNCTION.** Ladder: N × (`let p = async
  bump(1)` + `t = await p`) in one `main`, N = 250…4000. `phase:parse` allocations **924,089 →
  1,856,987 → 5,416,231 → 19,300,169 → 74,107,828**, marginal **×3.82, ×3.90, ×3.95** — converging on
  4.00 from below, so quadratic with no ambiguity. `phase:parse` CPU **52.9M → 102.1M → 438.6M →
  2,857M → 21,895M** grows FASTER still, marginal **×6.8, ×7.2, ×7.9** (≈ N^2.95); whether that third
  power is a second nested walk or the memory pressure of 74M allocations is not established here. At
  N = 4000 an **8,000-line file takes 22.8 s to compile, 21.9 s of it in parse** — against ~170 s for
  the whole 200k-line self-compile. **TWO HYPOTHESES RULED OUT BY CONTROLS on the same ladder shape:**
  it is not the LOCAL COUNT (N plain `let p = bump(1)` bindings is ×2.00, ×2.00 — dead linear) and it
  is not the OWNED-BINDING POPULATION (spawning N times into ONE reused `var p` gives ×2.92, ×3.57
  total — the identical curve), so the cost is per SPAWN/AWAIT SITE and not per live value. NOT
  attributed to a line: `recordAsyncSpawnSite`, `markValuePromise`, `pendingTempDrops` and
  `GenericInstanceRegistry.intern` are each O(1) per site, so the walk is elsewhere in the async
  emission. **Re-measure trigger:** any function with more than a few dozen spawn sites — a generated
  fan-out, a service dispatcher, a parallel driver.

- **N OWNED MANAGED BINDINGS IN ONE FUNCTION MAKE `phase:parse` CPU QUADRATIC** while allocations stay
  linear — the allocation-free cost class the CPU column exists for. Ladder: N × (`let s = mk()` at
  `String` + a use), N = 250…2000. Allocations **378,604 → 471,106 → 664,078 → 1,070,608** (marginal
  ×2.09, ×2.11) against CPU **28.9M → 38.1M → 72.0M → 198.5M** (marginal ×3.68, ×3.73). Reproduced on a
  second run to ±7% CPU with byte-identical allocations. ~18× smaller than the async term at equal N.

- **THE `.clone()` GATE IS A FULL TYPE-GRAPH WALK PER SITE, AND THE NEW UNION DOOR MAKES IT VISIBLE IN
  ALLOCATIONS.** `ProgramSignatures.aggregateSupportsDeepClone` builds a fresh `ClonerSet` and walks the
  whole reachable graph at EVERY source `.clone()`, so the cost is O(sites × graph). On the STRUCT side
  the per-field probes allocate nothing, so the memory columns read linear (marginal ×2.05, ×2.05 for
  N fields × N clone sites) while parse CPU bends (marginal ×3.68 at C = 100→200). On the UNION side
  both walks that run per site — `unionPayloadsSupportDeepClone` and the new
  `Parser.requireUnionHoldsNoGreenThread` — call `classifyUnionPayload` once per payload, which returns
  a BOXED union, so the same product is quadratic in ALLOCATIONS: marginal **×2.96, ×3.30, ×3.58, ×3.76**
  over C = 25…800 cases × C clone sites. **Filed rather than fixed because the population is bounded to
  the point of being empty:** the whole compiler + stdlib holds 122 `.clone()` sites, and the union
  clone door is new, so it has ZERO sites outside the new specs. **Named cure:** memoize
  `aggregateSupportsDeepClone` by aggregate name at its FRESH-`visited` entry ONLY — never in
  `aggregateNameSupportsDeepClone`, whose answer is computed under the cycle-break assumption that a
  name already in flight is clonable and is therefore not a stand-alone truth. The green-thread walk
  needs an answer of its own, because it also carries the float-payload refusal and must keep throwing
  at every site.

- ⚠ **AND THE STANDING INSTRUMENT CANNOT SEE ANY OF THE THREE, FOR ONE STRUCTURAL REASON: `ScaleCorpus`
  GROWS THE NUMBER OF FUNCTIONS, NOT THE SIZE OF ONE.** Every term above is quadratic in a per-FUNCTION-
  BODY population — spawn sites, owned bindings, clone sites — so a ladder that doubles the function
  count at fixed body size reads Δ0 through all of them, in every column it has. That is the
  `floatSpill` knob = 4 lesson in a second dimension: there the knob was too small, here the ladder
  grows the wrong axis. A DEPTH knob — one function whose BODY doubles with the rung — is what would
  make these measurable without a hand-built ladder.

**A name is a slice of the source, not a heap String.** By far the largest win so far.
Token text stopped being a heap `String` and became a zero-copy `ByteArray` slice into the source
bytes already in memory. Note the *shape*: rung 0 fell 14% but rung 5 fell 43%. That did not shave a
constant factor, it bent the curve.

**A byte string literal IS a ByteArray, and may be a global.** A bootstrap fix, not a
compiler optimization. `b"..."` at module scope resolved to an auto-created `__Array_i8` rather than
the stdlib `ByteArray`, which forced byte constants to be rebuilt per occurrence inside accessor
functions. The measurable win is small; the point was making the lexer's keyword table a byte-literal
map at all.

**For-in over an Array is an index counter.** `for x in arr` no longer allocates an
iterator object (a heap `ArrayIterator` wrapping a heap cursor). It lowers to an integer counter over
the backing buffer, in the parser, where the loop is built — replacing a post-monomorphization pass
that pattern-matched the CFG back into a loop and bailed on exactly the loops a compiler actually
runs (arrays of ops, blocks, tokens, strings).

**`EnumDummy` — a `try_call` on an associated-value enum stops allocating a placeholder.** The
biggest single win in the table: −24.6% of *all* allocations at rung 5. The bootstrap lowered a
throwing call returning a union into a heap-allocated dummy enum, a `select` between it and the real
result, and a decref of whichever lost — because a `try_call` returns null on the error path and the
lowering believed scope cleanup needed a real rc=1 allocation to decref. It never did: scope-end
cleanup already emits a null-GUARDED decref, and every managed slot is zeroed on function entry, so a
null slot is already the well-defined "nothing to release" case. On the success path the dummy was
allocated, increffed, decreffed and freed **without ever being read**. Null is what absent means, and
storing it is now the whole lowering.

The dummy was also *leaking* on the error path (see below), so this is a correctness fix wearing an
optimization's clothes.

**2026-07-16 — bootstrap constant resolver, not the compiler: no table row moved, and none could.** This
change is in the **C# bootstrap's** cross-file top-level constant resolver, which `scale-test` does
not measure — the instrument compiles generated programs with **The compiler**, and the compiler was rebuilt
byte-for-byte identically by the patched bootstrap (sha256 `1062b0d…`, forward *and*
`MAXON_SOURCE_ORDER=reverse`). The subject of every row above is unchanged, so there is nothing for
the tool to record. (On this arm64-macOS host the instrument cannot run at all: the compiler's M1 backend
allocates for x64-windows only, so `RegisterAllocator.maxon` panics on the host arch — orthogonal to
this change.)

The regression it fixes was real and superlinear. The rung made cross-file
constant resolution order-independent by folding every reference against a **whole-program-visible**
list of constant declarations; `EvaluateConstant` looked a name up in that list with a linear
`FirstOrDefault`, once per reference — **O(references × constants)**, quadratic in a constant-heavy
project. Measured on a generated corpus of N cross-file constants with `--timing` (the bootstrap's
own per-phase instrument, since `scale-test` measures the wrong compiler here), the `preScan` phase
grew **×4.2 per doubling** at N = 2000→4000 (39 → 164 ms) — a clean quadratic. Replacing the scan
with a name→decl dictionary built once per file (first-wins, so a file's own declaration still
shadows a foreign one of the same name) makes each lookup O(1): that same step is now ×2.4 (15 → 36
ms) and the dominant term is gone. The O(files × constants) that remains is the *same* class as the
pre-existing `SeedFromModule` seed this diff never touched — sub-millisecond at the ~600-constant
scale of a real self-build, and quadratic before this rung too.

**2026-07-17 — the perspective-fold's cache was PER-PARSER, so a foreign perspective was rebuilt in
every parser that read it. No table row moved.** The follow-up folds each
top-level constant in its DECLARER's perspective, resolving that file's private aliases; a foreign
perspective is built by `ConstantDeclSetFor(filePath)`, which scans the whole-program declaration list
once and memoizes the result. The memo was an **instance field** (`_constantDeclSetCache`), and a fresh
`Parser` is created for every file in every pass — so the SAME declarer's perspective was rebuilt once
per parser that folded one of its constants, across both the PreScan and Parse passes. On a deep
cross-file constant chain folded in a seeding-defeating order (which is exactly what
`MAXON_SOURCE_ORDER=reverse` exercises) the cascade re-derives each perspective at every level of every
parser: total perspective-build work grew **super-quadratically**, above the pre-existing and accepted
O(files × constants) baseline.

Measured with a temporary deterministic counter (perspective builds, and decls scanned across them —
exact, load-independent) on a generated chain of N single-constant files, `MAXON_SOURCE_ORDER=reverse`:
builds grew **×2.5–3 per doubling (~N¹·³)** and total decls scanned **×~4.5 (~N²·¹⁶)** — F, the number
of distinct perspective demands, was *larger than the file count* because the cache did not dedupe
across parsers. The fix threads ONE cache through every parser of a compilation
(`Compiler.CompileSources` owns it; scoped to the call, not static, so a reused module — the LSP's
cached stdlib module — never serves a stale set). After it: builds are **exactly linear (×2.00 per
doubling, one build per file)** and decls scanned is **×~4 = O(N²)**, the baseline class restored. At
N = 3200 that is 71.1M → 10.4M decls scanned (**6.85×** less), and reverse-order `preScan` wall-clock
went from **4.56× the parent at N = 3200 to 1.97×** — a bounded constant factor over the parent's own
O(files × constants), where before the gap *widened* with N.

It was a **latent** superlinearity, not a live regression: on realistic input the whole path is inert —
the stdlib (~600 constants) does **0** perspective builds and a real `maxon-the compiler` self-build does **2**
(816 decls scanned), because real cross-file constant references are shallow and mostly fold from a
value already seeded by their declarer. Behaviour is byte-identical: **3041/3041** C# spec tests pass
forward AND `MAXON_SOURCE_ORDER=reverse`, `git status specs/ specs/` empty (codegen unchanged),
the compiler self-build is bit-for-bit `1062b0d…` in both orders, and the three perspective/collision
reopen behaviours hold both ways (`cross-file-exported-reads-own-private` → 42,
`cross-file-exported-cast-to-own-private-alias` → 42, `cross-file-private-constant-name-collision` →
**122**, not 42). No run exited 101. (`scale-test` measures the compiler and cannot run on this arm64-macOS
host anyway — the bootstrap's `--timing` plus the temporary build counter are the right instruments
here, as with the entry above.)

**2026-07-18 — the arm64 encoder stopped building a `RegisterFile` per register operand.**
`scale-test` could not measure this: its corpus is heap/struct/global-heavy and the arm64 M1 backend
panics at `osAllocPages` on rung 0, so the ladder never produces arm64 data on this host. Measured
instead with `--metrics` on a synthetic 200-function scalar module (~36,800 ALU statements, which the
arm64 backend *can* emit), before vs after:

| phase | allocs before | allocs after | bytes before | bytes after |
| --- | ---: | ---: | ---: | ---: |
| encode | 123,130 | 10,913 | 2,636,989 | 841,517 |
| total | 1,527,795 | 1,415,376 | 115,908,333 | 114,109,634 |

**−112,217 allocations in the encode phase (−91%), and the total delta (−112,419) is the same number**
— so essentially every allocation removed was in the encoder. The cause: `arm64RegNumIsGpr` (the class
test every colored operand goes through, via `arm64GprField`/`arm64FpField`) read the GPR/SIMD boundary
as `arm64RegisterFile().firstVector`, **constructing a throwaway `RegisterFile` heap object to read the
compile-time constant 31** — once per register operand of every op, i.e. O(ops) pure-waste heap
objects. The x64 side never had this: `regClassOf` reads the `FirstXmmRegisterNumber` constant directly
and its comment says it does so precisely to avoid building a `RegisterFile` on the encoder's
per-register path. The fix names the arm64 twin (`Arm64FirstVectorRegister = 31`), so `arm64RegisterFile()`
and the encoder share one home for "31" and the hot path allocates nothing (the two non-hot per-function
readers, `arm64CallerSavedMask` and `arm64UsedCalleeSaved`, read the constant too). It is a constant-
factor win, not a bent curve — the count is still linear in ops — but the constant was ~110k objects a
scalar module. Codegen is byte-identical (the module emits the same 154,464 code bytes before and after;
`git status specs/fragments/` empty, so both the x64-windows and arm64-macos goldens are
untouched); the arm64 suite is **489/0**, exit 0 (no leak). x64 is provably neutral — nothing on the x64
path was touched and `arm64RegisterFile()` still returns the same value.

**2026-07-26 — the last two `BlockId`-keyed `Map`s became the dense table the other one already was
(PLAN #123 closed), and the ladder can see only a sliver of it.** `SemanticCheck.buildBlockByIdMap`
and `StdToWasm.BlockDispatchMap` are gone; both now read one `BlockIndexById` built by
`IrModule.blockIndexById` — the inverse of `func.blockRefs` — which is an instantiation of a new
generic `BlockKeyedTable`, and `BlockOffsetTable` is the other. Three hand-rolled copies of
array-pair-plus-presence-column would otherwise have existed; there is now one.

⚠ **`scale-test` structurally cannot see either converted site**, and the Δ0 it reports for them is the
corpus blind spot, not a result: `ScaleCorpus` generates no `async` (so `functionHasAwait` never opens
the gate) and `compileRung` spawns `build` with no `--target` (so the wasm backend never runs). Both
were measured directly with `--metrics`:

| ladder | phase | allocs before | allocs after | bytes before | bytes after |
| --- | --- | ---: | ---: | ---: | ---: |
| `Testing/ladders/genawait.sh 24 400` (x64) | semanticCheck | 2,248 | **712** | 3,191,376 | **258,744** |
| `scale-test --emit-corpus` rung3, `--target=wasm32-wasi` | encode | 123,468 | **116,604** | 24,043,224 | **21,799,335** |

**−1,536 allocations (−68%) and −92% of the bytes** on the await ladder; **−6,864 allocations** on the
wasm rung, and in BOTH compiles the whole-module total moved by exactly that phase's delta and every
other phase read Δ0 — so the attribution is complete, not inferred. The await ladder's `<ifs>` knob is
what names the mechanism: at a fixed 24 functions, allocations used to climb **1,864 → 2,056 → 2,248**
as blocks per function doubled — **+8 per function per doubling**, which is the `Map`'s rehash chain
(one `grow()` per capacity doubling, four fresh columns each) — and are now **FLAT at 712**, because a
pre-sized dense table is two allocations per function however many blocks it has.

**What the ladder DID move is `bytes:encode`, and only that**: −2,328 / −3,608 / −6,168 / −11,288 /
−21,528 / −42,008 down the rungs, with **allocation counts unchanged in every phase**. Divided by
`sizeof(CodeOffset)` those are 291 / 451 / 771 / 1,411 / 2,691 / 5,251 slots — exactly `131 + 160·2^k`,
a constant stdlib component plus a corpus component that doubles precisely with the rung, which is what
a per-chunk constant looks like. The cause is one line: `forBlockCount` now `reserve`s the value column
(`managed.grow(n)`, the exact ask) where it used to `growFilled` it, and `growFilled` routes through
`grownCapacity` → `atLeastMinimum`, which floors at `Array.MinimumCapacity` = 4. So a chunk of fewer
than four blocks no longer rounds its offset column up to four slots. `phase:encode`'s byte curve is
x2.03 before and after — the constant moved, the exponent did not.

⭐ **And the measurement worth keeping is the one about `spreadHash`, because it runs the other way.**
Replaying the real key sequences of a 2,808-function corpus through `Map`'s exact algorithm, these two
maps' keys arrive **perfectly ascending in 2,808/2,808 and 24/24 instances** — Std-tier and Maxon-tier
`blockRefs` layout order IS id order, because the interleaving run comes from the register allocator's
critical-edge split, which neither site sees. That is the case an identity hash gets *perfect*: **1.000
probes/insert**. Under today's `spreadHash` the same keys cost **3.088** and **3.062**. The class-level
mixer is right — it is what makes the trap unreachable for any key order — but on a dense id run it
*costs* about 3× the probes it insures against, which is why the per-site "tidy O(n)" #123 always
preferred was still worth taking after the mixer landed, and not instead of it. Codegen-neutral:
`specs/fragments/` empty after full runs on arm64-macos AND arm64-linux; The compiler **1591/0**
arm64-macos, **1586/0** wasm32-wasi, **1589/0** arm64-linux, C# bootstrap **3104/3104**, all exit 0;
`--workers=1` byte-identical to `--workers=12`.

## Bugs this uncovered

Removing the dummy exposed a leak it had been half-hiding, and the leak in turn exposed a hole in the
test suite. Both are fixed; both are worth knowing about.

**A routed try-block call leaked one reference per call.** A bare throwing call inside a
`try 'blk' … end 'blk' otherwise (e)` block has its result hoisted into a `__try_block_result_N` temp
that receives the callee's *transferred* reference — so that temp owns it and must release it.
`VarRegistry.KeysSince` excluded exactly those temps from scope-end cleanup, on the theory that a
downstream `let x = <call>` aliased the same slot without increfing. That was true for a struct
return, which was separately handed a `CallReturn` `__call_tmp_` that turned the alias into a *move*,
and false for an associated-value union, which was handed nothing and so increfed like any other
alias. Both special cases are gone: the try-block form now works the way every single-statement `try`
form already did, and the temp that receives the reference is the temp that releases it.

**80% of the spec suite could not see a memory leak.** `mm_leak_check` overrides the process exit code
with 101 when an allocation is still live at exit. But 2311 of the 2886 tests are compiled together
into one batched binary whose per-test verdicts are parsed from stdout markers carrying each test's
own `main` return value — and `TestRunner` discarded the batched binary's process exit code entirely.
The leak checker ran, printed, and had its verdict thrown away. A leaking batched test still emitted a
full set of passing markers. The runner now reads that exit code; because the leak counter is
process-global and cannot say *which* test leaked, a non-zero batch exit invalidates the batch and
re-runs its tests individually, where each one's own leak check attributes it.

## What the profile says is left

From `scale-test --per-type`, which attributes every allocation to the TYPE allocated *and* the SCOPE
that allocated it. **The scope column is the one that finds things** — a `String` row can never tell
you that 150 of them came from `emitFixedToken`.

| what | share | note |
| --- | ---: | --- |
| `OpIndexList` / `BlockRefList` | — | **DONE, and this row is kept only so the claim is not re-derived.** It said `IrBlock.opRefs` and `IrFunction.blockRefs` were *linked lists of integers* costing a heap node per op reference plus a `ListIterator` per traversal, and called `Array` the biggest single win available. They ARE `Array`s now (`IrBlock.maxon:147`, `IrFunction.maxon:89`); the win was taken. Spotted 2026-07-26 by the encode-quadratic pass, which went looking for this ~17% and found it already gone. |
| `stepBackwardOverOp` et al. | ~7% | A superlinear cluster inside the register allocator (fitted exponent ~1.47), not a constant factor. |

`ArrayIterator` (once the single biggest allocating scope, at 102,845) and `EnumDummy` (once ~20% of
all allocations) no longer appear.
