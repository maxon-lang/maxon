# Open findings — maxon-shv2

**What this is:** the live backlog of *bugs and gaps*, as distinct from `PLAN.md` (which owns the
**ladder** — what to build next). Everything here was found by **running code shv2 did not write**, and
almost every one is a **wrong answer, not a crash**. Recorded 2026-07-14.

> **The through-line, and the thing to internalise:** nearly every real bug below is **one fact written
> down twice**. A promise's error type distilled into two bits, with the type discarded. A return op
> derived from the token *and* from the declared type. The spec pool's parent and worker each deciding
> which tests a spec selects. The ring's size in three places. An MCP tool described twice — which is
> what taught a *model* the wrong thing about `scale-test`, and cost the most. **When two places must
> agree and nothing makes them, they will drift, and the failure is a wrong answer.**
>
> **⭐ AND ITS OTHER FACE, added 2026-07-14: the duplicate that does not drift, it just COSTS.** The
> query spine's dependency graph was a reverse index **plus** a flat array of every edge — and nothing
> ever read the array. It could not go wrong; it went **quadratic**, because `clearDepsFor` rebuilt it by
> scanning every edge in the project and rendering each one to a `String` to compare it. **The same
> question finds both: is this fact stored twice? If yes, either they disagree (a wrong answer) or one is
> dead weight being maintained at cost.** (The error-code registry, #12, is the drifting kind; this is the
> costing kind. Both are the same question.)
>
> **⭐ AND ITS CURE, learned 2026-07-16 (#23): WHEN TWO PLACES MUST AGREE, MAKE THEM CHECK EACH OTHER.**
> The iatCall bug — `implicitDefs: 0` under a comment reading *"full call-barrier semantics"* — was fixed not
> by an instrument but by **`assertCallClobberConsistent`**, which compares the op metadata against
> `RegisterAllocator.callerSavedMask` on every `allocateRegisters` and panics naming the drift. **Verified by
> trying to put the bug back: it no longer compiles anything.** The bug is **unrepresentable, not fixed**.
> Three green gates had sat on it. ⇒ **A check between the two copies beats every gate, because a gate can be
> pointed at the wrong program and a check cannot.** (`maxon error-codes check` is the same move — see #12,
> which is closed, and which this file went on claiming was open.)
>
> **⚠ A STANDING RULE, from the same rung: a WHOLE-PROGRAM query placed UNDER a per-file one is asked
> ONCE PER FILE.** Anything O(files) inside it is therefore **O(files²)**. Two quadratics hid behind
> exactly that, and were found only because a new query made the scale-test *delta* grow **x3.4 per rung
> on a ladder that doubles** — a linear change cannot do that. Before adding work to such a query,
> multiply by the file count.

---

## 🔴 Wrong answers — a correct program compiles and returns the wrong number

### ~~1. Cross-file callee types DEFER into an unsound ACCEPT~~ ✅ **FIXED 2026-07-14** (`SignatureIndex.maxon`)
```
a.maxon:  export function isReady() returns bool
b.maxon:  let x = isReady()
          return x + 41        ⇒ shv2 COMPILED it and returned 42
                               ⇒ now: E2004: Cannot operate on bool and int  (the bootstrap's own text)
```
**The fix was UPWARD, as predicted: a whole-program signature index** — `Compiler/SignatureIndex.maxon` +
`Queries.queryProgramSignatures`. Every file's TOKENS are swept before any file is parsed, so `unresolved`
is never the answer for a callee some file declares.

**THE DEFERRAL WAS KEPT** (refusing an `unresolved` operand would reject `crossFileInt() + 1`, a *correct*
program) and `wordOpResultTag` still checks `bool` first. Both traps recorded here were real; both are now
pinned by specs that must keep **compiling**, not just failing. **`unresolved` now means exactly ONE thing:
a callee NO file declares** — which `SemanticCheck` rejects (E3004), so it can never reach codegen. That is
what makes the false tag *unreachable* rather than merely *fixed*.

**The memo re-keying was the load-bearing half, and it is now GATED.** A parse reads two inputs, so
`ParseMemo.keyHash = mix(contentHash, ProgramSignatures.hash)` — and that hash covers the **DECLARATIONS**,
not the sources they were read from, so a *body* edit re-parses one file while a *return-type* edit
re-parses all of them. (Keying on the composite SOURCE hash would have been sound and would have destroyed
incrementality.) `verify-warm-rebuild` grew a **third property, `invalidation`**, because properties 1 and 2
both prove a memo is STABLE and **neither can catch a memo that stays valid after an input CHANGED** — the
failure a cache actually kills you with. Proven by negative control: with the old key, determinism and
cache-hit still **PASS** and invalidation **FAILS**. ⚠ Point it at a **multi-file** project; on one file the
two halves coincide and it proves nothing.

**Sibling, same sentinel — ✅ ALSO FIXED.** `calleeResultType` returned `unresolved` for **both** a
cross-file callee **and a VOID one**. `ValueTypeTag.void` is now the distinct "no value" tag, and a void
call in VALUE position is rejected at the call site with the bootstrap's exact text (`E2004: Function 'noop'
does not return a value`). The grammar has exactly two call positions and only one wants a value — that is
the whole check.

> ### ⚠⚠ THE PREREQUISITE THIS ENTRY RECORDED WAS **FALSE**, AND IT WOULD HAVE COST A RUNG
> It used to say: *"the `.test` fragment format **cannot express a multi-file case**. The harness must grow
> multi-file spec support first."*
>
> **The format expressed it all along.** The convention is `// --- file: name.maxon` inside a ```maxon block
> — **650 occurrences across 236 files** in `/specs` and its committed fragments — implemented by the C#
> reference in `SpecParser.cs` (`SplitMultiFileSource` + `FileMarkerRegex`). **shv2's `SpecParser` simply
> did not read it.** It now does, **ported rather than invented** (a second, shv2-only multi-file syntax
> would have been this file's own through-line bug, in the one place both versions would "work").
>
> **The lesson is about THIS FILE:** a gap in *our* implementation was written down as a limit of *the
> format*. One `grep` would have caught it. Check the corpus before believing a claim about the corpus.

### ~~2. `a / 0` is a raw hardware trap, not a panic~~ ✅ **FIXED 2026-07-15** *(= PLAN.md's P1.0d.3)*
It escaped as `0xC0000094` with **empty stderr**. Now exit **1** and a **VEH thunk** converts the `#DE`:
```
panic: integer divide by zero at rip=0x0000000140001031 diag_base=0x0000000140001000
Stack trace:
  in main
  in mrt_start
```
`specs/safety.md`'s `divide-by-zero` + `mod-by-zero` are ported and ENABLED (suite **279 → 281**). Emitted
code for a trivial program went **38 → 1434 bytes** — shv2-compiled binaries had **no runtime at all**
before this; this is **Workstream R's first slice**.

**Three things it settled that are bigger than the divide:**
- **A VEH fault handler, not a divisor check.** shv2 is x64-only and the CPU raises `#DE` for free; a
  `cmp`/`branch` before every `idiv` would be the "cheap path that avoids a hard mechanism" the PRINCIPLE
  names. ⭐ **NO green-thread coupling:** the reference's fault path rides `gt`/`fault_redirect_*` (P1.5),
  so it prints and exits **in place** — which means the context travels as ordinary **arguments** and the
  fault globals v1 needs **do not exist here at all**. v1 needs them only because its CONTEXT redirect
  resumes at a function it cannot pass arguments to.
- ⭐ **FRAME POINTERS on every function** — see PLAN.md. **The forcing reason is P1.5's `__gt_morestack`,
  not the trace:** a relocating GT stack fixes up the **saved-RBP chain**, so shv2 needs it there anyway.
- **The harness could not pin a program's RUNTIME stderr.** ` ```maxoncstderr ` is the COMPILER's. Now
  there is a ` ```stderr ` fence (+ `stripFaultRipSuffix`, mirroring the reference's `TestRunner.cs:1427`).
  ⚠ **And PLAN.md had claimed shv2 accepted a ` ```stdout ` fence it never had** — the plan committing this
  file's own through-line bug. Corrected there.

⚠ **What is NOT covered, and the ported prose over-promises it:** `specs/safety.md`'s documentation
(lines 11–32) describes v1's runtime — integer overflow, nil-pointer, stack overflow, macOS `sigaction`,
arm64 `SIGFPE` classification. **shv2 converts `#DE` and nothing else**, as `X64Runtime.maxon:744` correctly
says. The file is kept byte-identical to `/specs` per Workstream S (marker flips are the only sanctioned
edit), and `force-segfault` is `disabled-test:` behind P1.2 — but **a reader of the ported spec would
believe more is implemented than is.**

**A real bug the fence work found, of this file's exact class:** `SpecParser.scanTestFromMarker` **returned
at the first expected block**, so a trailing ` ```stderr ` would have been walked over by the outer scan and
**silently dropped** — a spec pinning a panic would have passed **on its exit code alone**. ⚠ **Still open,
inverted:** it now scans to the next marker, so a *second* ` ```maxon ` fence in one test region **silently
overwrites** the first (last-block-wins). No spec trips it today; it wants a policy call (panic on a
duplicate fence?) rather than a ride-along fix.

### 3. Awaiting one promise TWICE double-frees its managed result
`mm_decref: refcount underflow (already zero)`. The payload is handed out as an owned **+1 per await**
though the thunk owns it once. **Not** an error-type bug — a *non-throwing* promise with a managed
result double-frees identically.
⇒ A language decision: make `await` **linear** (a promise is awaited once, the compiler rejects a
second), or incref per await. **LOAD-BEARING FOR P1.5** — decide it *before* shv2 ports `async`,
alongside `Promise with (T, E)` (see PLAN.md). A promise that carries its error type **and** has a
defined await-arity has no representable version of either bug.

### 17. ⚠ NEW — the bootstrap ALIASES two file-private `var`s of the same name into ONE `.data` slot
**Measured 2026-07-15**, three files, each `var` file-private (never exported):
```maxon
// featA/a.maxon    var counter = 7      export function getA() returns Num  → counter
// featB/b.maxon    var counter = 100    export function getB() returns Num  → counter
// app/main.maxon   return getA() + getB()
```
⇒ **expected 107. Got `200` = 100 + 100.** `getA()` silently returns **100**. Compiles clean, build exit 0,
no diagnostic. **A silent wrong answer.**

**It is this file's through-line, in the reference compiler: the NAME RESOLVER is file-scoped — a cross-file
read of a non-exported decl is a correct `E2004` — but the `.data` LABEL is global-by-name.** *"Which
variable is this?"* is answered in two places, and they disagree. The later file's initializer wins the
slot and both readers get it.

⚠ **AND THE CORPUS STRUCTURALLY CANNOT SEE IT.** `specs/top-level-let.md`'s
**`file-private-same-name-cross-file`** pins exactly this shape — two files, same bare name, different
values, *"Each file's reads resolve to its OWN constant"*, exit **92** — and it **PASSES 19/19**. It passes
because a top-level **`let` is INLINED at its use sites**, so there is no label to collide. **There is no
`var` twin of that case anywhere in `/specs`** (grep: `file-private-same-name` appears only under
`top-level-let`). *The one case that would catch this tests the one construct that cannot exhibit it.*

⇒ **LOAD-BEARING FOR P1.0d.5b (top-level `var`) — shv2 MUST NOT PORT THIS.** A file-private global needs a
**label identity that is per-file, not per-name**. Note the obvious workaround is *forbidden by the spec*:
`top-level-let.md` states two files **may** declare the same private name with different values, so
"reject duplicates" is not available. Deterministic disambiguation (suffix on collision, in source-path
order) keeps goldens stable. **Write the `var` twin of `file-private-same-name-cross-file` as the
acceptance test** — it is the case the corpus is missing, and shv2 is the compiler that should have it.

---

## ⚠ Gaps the plan never listed — all found by the corpus

### 4. Top-level `var` (GLOBALS) — **and top-level `let`** — are missing entirely  *(= PLAN.md's P1.0d.5)*
```
var counter = 0   ⇒ error E2015: Unsupported: top-level var
let BASE = 10     ⇒ error E2015: Unsupported: top-level let
```
⚠ **TOP-LEVEL `let` WAS NOT ON THE LADDER EITHER** (found 2026-07-15, probing). It joins parens, block
scoping and globals as a gap the corpus knew about and the plan did not — and it has **its own stable spec,
`specs/top-level-let.md`, 19 cases, which the bootstrap passes 19/19.** *(It was missed twice: a grep for
`global|static|module|init` does not match the filename. **Check the corpus for the feature's own spec by
NAME before believing it has none.**)*

**MEASURED corpus impact: 118 of 276 `/specs` files use a top-level decl — 99 use `var`, 37 use `let`.**
`specs/short-circuit-evaluation.md` is **0/12, and NINE of the twelve need a global** — a global is how most
spec files *observe a side effect*.

**Two of the three costs this entry used to list are RETIRED by measurement:**
- ✅ **Initialization order is VACUOUS.** `specs/static-variables.md`: *"Top-level `var` initializers must be
  constant expressions… Function calls and runtime expressions are not allowed."* Confirmed against the
  bootstrap: `var b = a + 1` where `a` is a **`var`** ⇒ **`E2004: Undefined constant 'a'`**, while
  `var derived = BASE * 2` over a `let` works (and **forward references work**, so the resolver needs an
  arena + deferred resolution, not a single forward pass). ⇒ **Every initializer is compile-time evaluable;
  the compiler emits the BYTES into `.data`. There is NO `__module_init` to build.**
- ✅ **A managed global is IMPOSSIBLE** before P1.2 — no heap ⇒ scalars only.
⇒ **What is actually left is storage**: the Std **memory band** (`StdOpCategory` declares `memory`/`system`
but `StdOp` has only `arith`/`control`/`call`), a `.data` section (PeWriter has none), and the
`dataSectionRipRelDisp32` reloc — **all three already scaffolded and named for this milestone**
(`GlobalDataTable`: *".data-section globals … OMITTED until their milestones … they grow back as additional
fields here"*; `RdataRelocKind.dataSectionRipRelDisp32` already exists — as did a `GlobalLabelClass.dataSection`, since
DELETED unused (4h): the routing turned out to be by reloc kind, never by the label's name;
`PeWriter.maxon:236` panics *"not supported until its milestone"*).

⚠ **`let` needs NO storage at all** — it is compile-time evaluated and inlines at its use sites (which is
exactly why it dodges **#17**'s aliasing bug). ⇒ **SLICE IT: `let` first** (front-end only, no IR ops, no
codegen, its own stable spec), **then `var`** (where the risk actually lives — see **#17**).

> ### ✅ **THE `let` HALF IS DONE (P1.0d.5a, 2026-07-15)** — suite **281 → 295**, zero goldens moved.
> **shv2's `let` scoping is per-FILE and correct — it does NOT have #17's bug**, proven by running it: two
> files each declaring `let SHARED` (99 / 7), `getA() - getB()` ⇒ **exit 92**. A name-keyed table gives 0.
> ⚠ **But nothing in the suite PINS it** — the upstream case needs `as` (P1.9) and is `disabled-test:`.
> **`var` (P1.0d.5b) is still open, and #17 is its landmine.**

### 4b. ⚠ Three things P1.0d.5a left open — found by the independent review/optimizer, deliberately NOT fixed
- ⭐ **The constant DFS's recursion depth is UNBOUNDED — SIGSEGV, no diagnostic, and it DEFEATS `E2012`.**
  A forward chain of depth **700 compiles; 800 SIGSEGVs (139)**. A cycle of 100 raises `E2012`; **a cycle of
  800 faults before `recordCycle` can raise it.** It is **depth, not count** (800 constants as 8 chains of
  depth 100 compile fine). **INHERITED, not a regression — and shv2 is strictly BETTER: the bootstrap
  stack-overflows at depth 700, which shv2 compiles.** ⚠ **The tempting cheap fix is a TRAP:** pre-scanning
  each initializer's identifiers to build a dependency graph is **a second reading of the initializer
  grammar — a THIRD evaluator** — exactly the duplication P1.0d.5a carefully avoided. The right fix is an
  explicit **worklist** (a design change, its own rung, in **both** compilers); a depth cap is a workaround.
- **A DUPLICATE top-level constant is silently accepted:** `let A = 1` / `let A = 2` compiles and returns
  **1**. A duplicate *function* is `E3006`. Needs a spec case.
- **Assigning to a constant reports the WRONG thing:** `A = 5` where `A` is a top-level `let` ⇒
  `E2004: Undefined variable 'A'`. Correctly rejected, but `A` is plainly *defined* — it is **immutable**.
  `E2013 ParserImmutableVariable` is the right code.

### 4c. ⚠⚠ A GATE REPORTED **PASS** WHILE STRUCTURALLY BLIND — and only a SABOTAGE found it
**`verify-warm-rebuild`'s whole battery could not see whether a top-level constant's VALUE rides the parse
memo key.** The reviewer deleted the value arm of `mixConstant` and rebuilt: **`verify-warm-rebuild` PASS
(exit 0), spec suite 295/0.** Everything green, the property gone.

**The reason is precise, and it generalises: both existing probes ADD A NAME** (a comment; then
`function __warmRebuildProbe`) — **and a name-only key moves on that.** *"Declaration edit re-parsed all 2"*
therefore proves nothing about a **value**. **Only an edit that holds the NAME fixed and moves the NUMBER
can discriminate.** ⇒ property **3(c)** added (`let __warmRebuildProbeConstant = 7` → `8`): red on the
sabotage (*"expected 0+2=2, got 1"*), green as shipped.
⚠ **The optimizer's evidence for this was WRONG though its conclusion was right** — it cited the *function*
probe. **This is `22f534e78`'s lesson ("a gate that knows it is vacuous must not report PASS") one level up:
a gate that does NOT know it is vacuous is worse. When a gate asserts a property, ask what edit would break
it — and make that edit.**

### ~~4d. ⚠ THE SCALE CORPUS IS SYSTEMATICALLY BLIND TO THE FEATURE JUST LANDED — **THIRD instance**~~ ✅ **CLOSED 2026-07-16** (`74ea57d1f`, `726a606f1`)

> **FIVE KNOBS, and the four blind rungs are now on the ladder.** Measured by emitting the corpus and
> counting, at rung1 (before → after): top-level `let` **0 → 64**, top-level `var` **0 → 64**, `type` decls
> **0 → 20**, `Self{}` **0 → 20**, `.create()` **0 → 80**, integer `/` **0 → 64**, `mod` **0 → 64**. Every
> one DOUBLES per rung. `globalConsts` · `globalVars` · `structTypes` · `structAllocs` · `intDivide`.
> **No compiler code changed** — the diff is `ScaleCorpus.maxon` + one log row, and every rung's
> `allocsDelta`/`bytesDelta` read **0** against a re-run, which is the proof.
>
> ⭐ **THE FIND, and it generalises past this rung: the corpus was ALREADY FULL OF `/` — and every one was a
> FLOAT `/`.** Float `/` is a *different Std op* (`binOp(div)`, no fixed registers) from integer `/`
> (`StdOp.div`, the RAX/RDX clobber). **Counting the OPERATOR said "covered"; only counting its TYPE found
> the hole.** A grep for a construct is not a measurement of the construct.
>
> ⭐⭐ **AND THE MANIFEST LIED BY OMISSION — the half of this rung that matters.** It printed `let / var`
> under **CONSTRUCTS GENERATED**, meaning the *body-local* kind, and named the *top-level* kind under
> **NEITHER** heading. Two rungs and 36 spec tests read as covered by a reader doing exactly what the
> manifest asks. It is now held to a stated rule: **every construct in the subset appears under EXACTLY ONE
> heading, and a PARTLY-generated construct names which part.** Structs are generated — and their two
> interior blind spots (**field WRITES**, P1.1a wave 2; **structs across a CALL**, P1.4) are named *in the
> struct entry*, where a reader looking up structs will actually find them. *An undeclared gap reads as
> coverage; that is this file's oldest lesson and the instrument now obeys it.*
>
> ⚠ **SCOPE, stated so it is not mistaken for more:** these knobs measure **the COMPILER's cost of compiling
> those constructs**. `scale-test` compiles each rung and never runs it (`ScaleTestRunner.compileRung`
> spawns `build`, reads the metrics TSV, returns), so the **emitted program's RUNTIME behaviour is measured
> by nothing here** — the implementer hit this independently (*"a struct in a block exits 101 … a stated P1.4
> gap this instrument never observes because it COMPILES these programs and never RUNS them"*). Not a defect
> of this rung; a boundary worth knowing before the next reader expects more of the columns than they hold.

**~~`scale-test` reported P1.0d.5a's cost as `2 × files + 1`, exact on all six rungs.~~** **It measured the feature
INERT: the corpus contains NO top-level `let` at all** — verified by emitting it (**0** column-0 `let`, 56
body-local) — so the arena is empty on every rung and the DFS, the lookup and `mixConstant` never run.
`2 × files + 1` is *the signature of an empty arena*: it proves the per-file SWEEP is linear and says
nothing about the evaluator.

**The prior two, which make this a pattern rather than a coincidence:** the corpus contained **`and`/`or`
NOWHERE** (#5 — so the `const`-flags fix, which removed **four instructions from every short-circuit site in
the language**, read as *zero movement on every rung*), and it generates **no `/` or `mod`** (so it
structurally cannot see the divide path P1.0d.3 was built for). ⇒ **THE CORPUS IS GROWN TO EXPOSE THE LAST
BUG, SO IT IS BLIND TO THE NEXT ONE.** *A knob built to expose a bug you already found only finds bugs you
already found* (#5). **The standing rule — user directive, #5 — is FIX THE CORPUS'S REALISM, NOT THE
PROBE**, so a rung that adds a construct should add its **ladder axis**. Until it does: **measure
off-instrument and SAY SO.** P1.0d.5a did (400→12,800 constants: **x2.00**; 8→128 files × 50 constants with
cross-file `export let` — the O(files²) hazard in its live shape: **x2.00**, where the bug reads x4.00).

### 4f. ⭐⭐ `scale-test` CANNOT SEE A TIME-ONLY QUADRATIC — by design, permanently. Say it out loud.
**P1.0d.5b found a REAL Θ(globals²) that the instrument was structurally incapable of reporting.**
`GlobalDataTable.sortBySizeDescendingStable` scanned to the first *strictly smaller* entry — and
`StdTypeInfo.storageBytes` is only **{1,2,4,8}**, so a program whose globals are all one size (**every global
an `int`** — the ordinary case, not the pathological one) **never stopped early** and rescanned the whole
placed list per entry. Fixed to **four appends-in-order**, O(n), with a count invariant so a future 16-byte
type **panics rather than silently dropping a slot**.

**Measured off-instrument, 8000 globals: 619 ms → 166 ms** whole-compile. `layOut`'s bucket:
**30.2 / 120.3 / 460.3 ms** at n=2000/4000/8000 — **x3.98, x3.83** — → **2.0 / 4.1 / 9.0 ms**.

⚠ **AND ALLOCATIONS AND BYTES WERE EXACTLY x2.00 BOTH BEFORE AND AFTER.** `scale-test` was **bit-for-bit
identical across the fix.** It collects **memory only**, deliberately — *"time is machine-dependent, so a
dated table would compare a loaded box in July against an idle one in August; memory is exact and
bit-for-bit reproducible, and it is the only column where a difference MEANS something."*
⇒ **A TIME-ONLY superlinearity is invisible to it and always will be.** **This is NOT a defect and NOT a
reason to add a time column** — it is the price of the property that makes the instrument trustworthy. But
it is a **limit to state, not rediscover**: `scale-test` answers *"did the memory profile bend?"*, never
*"is this algorithm linear?"* **For the latter, measure off-instrument and say so.**

⚠ **The comment defending the cost is the real lesson**: *"a program's top-level `var` count is a handful"* —
**the same appeal to an empirical corpus property that v1's aliasing bug rests on three files away**
(*"in practice no two file-private vars share a bare name"*). **A complexity defended by what programs
happen to look like is a bug waiting for a program.**

### 4h. ✅ MAKE THE COPIES CHECK EACH OTHER — three cures, and they are NOT the same strength
Closed 2026-07-15 (`make-copies-check`). Three instances of the signature bug — ONE FACT WRITTEN DOWN TWICE
— cured three different ways. **The ranking below is the reusable part; reach for them in this order.**

1. ⭐⭐ **DERIVE IT — the fact is written ONCE and there is nothing left to check.** `TypeRules` held
   `ShiftCountBits = 64` and `MaxUnguardedShiftCount = 63`: two literals, ONE hardware fact (the operand
   width), nothing comparing them. Drift admits a count the hardware masks — the `a shl -1 → a shl 63`
   class E2054 exists to close (#7b). **Now `(ShiftCountBits - 1)`.** ✅ **The bootstrap DOES const-fold a
   top-level `let` from another top-level `let`** (verified by running it: exit code 63). **Prefer this
   always: an assert is a consolation prize for a fact you could not derive.**
2. ⭐ **ASSERT IT — when a derivation is REFUSED.** `RegBits.FirstXmmRegisterNumber = 16` and
   `RegisterFileSize = 32` respell `X64Register.xmm0.rawValue` and the enum's case count. They **cannot** be
   derived: **`.rawValue` in a top-level `let` initializer is E2045** (verified — *"Global initializer for
   'FirstXmm' is not a constant expression"*). ⭐ **But E2045 binds CONST-EVALUATION, not a function body**,
   so the constant stays a literal and the CHECK reads the enum. `assertRegisterFileMatchesEnum` runs once
   per module. **Cost, MEASURED: +2 allocs / +296 bytes per compile, FLAT across all six rungs** (the
   `allCases` Array + buffer; 40B header + 32×8B). Same call that cost **+5.6M allocations** on the per-op
   path — *the position is the whole difference.*
3. ⭐ **MAKE IT A REQUIRED PARAMETER — the strongest of all where it fits.** `ScaleCorpus.mainSource`
   re-spelled 13 of 15 driver names. Each knob now answers a required `driver RungDriver` on
   `writeRungFile`, and `main` reads one list. **A knob that forgets its driver COMPILES NOTHING**
   (`E3036: missing argument for parameter 'driver'` — verified by sabotage). This is the `iatCall` cure's
   shape: not detected, **unrepresentable**.

⭐ **THE FOURTEENTH DRIVER WAS LEFT COUPLED BY ADJACENCY, AND THE REASON FOR LEAVING IT WAS WRONG (review
closed it).** The chain knob escaped cure 3: `callChainSource` numbered its links `0..length-1` while a
separate `chainTopName(length)` returned `chainLinkName(length - 1)` — *"the links are numbered
0..length-1"* written TWICE, once as a loop bound and once as a subtraction, with **nothing making the
copies agree**. Its own comment said so (*"nothing checks the pair… a stale `chainTopName` names a MIDDLE
link silently"*) — and a coupling documented at both ends is exactly what this file's through-line says
does not work; it is what the dead `classifyGlobalLabel` had. It was left because closing it *"would
invalidate the byte-identity proof"* — **which it does not**: the closure emits **identical text**
(verified, `--emit-corpus` before/after, 342 files, recursive diff EMPTY), so the proof is re-run, not
lost. **The cure was already IN THE FILE**: `writeManyFunctions` — the other knob minting names in a loop
— writes and registers in ONE function. `callChainSource`/`chainTopName` are now `writeCallChain`, whose
`topName` is *literally the last link the loop emitted*. ⇒ **Cure 1 (derive), not cure 2 (assert): read
the fact off the generator instead of recomputing it.** Measurement-neutral: the delta stayed **+2 allocs
/ +296 bytes flat at rungs 0-5** (the generator is not inside a measured phase).

⭐ **ASK `value == Type.case`, NOT `.rawValue != n` — AND THE REVIEW IS WHAT CAUGHT IT.** The xmm check
first compared `FirstXmmRegisterNumber` against `xmm0.rawValue`. That is **E3097**, and the first fix —
bind it to a local — was a **false account of its own code**: a bare local does **not** dodge E3097 (the
checker traces the binding, measured); only a **NARROWING cast** does, and only because the range check
hides the accessor (`as int(0 to u64.max)` is elided and E3097 returns). ⚠ **And that cast PRE-EMPTED the
check**: an xmm0 drifted above the file range-check-panics *"value outside typealias 'RegNum'"* — naming
a typealias instead of the two things that disagree. The check is now
`fromRawValue(FirstXmmRegisterNumber) != X64Register.xmm0` — the form E3097 demands, no cast, and it
reads as the property. **Bonus, verified: an out-of-enum boundary is now `E3034` at COMPILE time.**
⇒ **A line that compiles only because a cast launders it is a line nobody can safely tidy.**

⚠ **THE `63` IS STILL WRITTEN A THIRD TIME AND THE DERIVATION DOES NOT REACH IT** (review found this):
`TargetDialect.ShiftCount = int(0 to 63)`. A range bound — E2010, uncheckable, the `RegNum` dead end
again. It is the FORCED restatement and says so; TypeRules now points AT it, because *"unrepresentable"*
next to `ShiftCountBits` otherwise reads as *"there is only one copy"* and there are two.

⚠ **AND THE SABOTAGE FOUND AN ORDERING DEFECT NOTHING ELSE WOULD HAVE.** With `RegisterFileSize` broken,
the PRE-EXISTING `assertCallClobberConsistent` fired FIRST and blamed *"TargetDialect.callDirect's
implicitDefs"* — **a file with nothing wrong in it** — because `callerSavedMask` is BUILT from
`classRegisterMask`, which is built from `RegisterFileSize`. A derived check outrunning the primitive one
accuses the wrong party, which is the exact failure `assertOneCallClobber`'s own comment warns of. **The
checks are now ordered most-primitive-first.** ⇒ **When you add a check, ask what it is DOWNSTREAM of.**

### 4g. ⚠ Three comments claimed mechanisms that do not exist (found by P1.0d.5b's review)
Same family as `throws_` being **write-only for its entire life** while a comment claimed *"the runtime
branches on it"*, and `TargetOpMeta.setsFlags` (written 40× / read 0×), and the `xor reg,reg` idiom the
lowering never emitted. **Now four instances of "a comment describing a compiler nobody wrote."**
- ✅ **`classifyGlobalLabel` had ZERO CALLERS** (`CodeResult.maxon:103-119`) — dead since M1-A. Its comment
  claimed it routes globals to `.data` by the `__data_` prefix, that a label without it *"would fault on its
  first store to a read-only page"*, and that *"the two are one fact and this is its name."* **All false**,
  and **disproved by measurement**: set the prefix to `"zzz_"`, rebuild — globals still land in `.data`, still
  take stores, still return the right answer. Routing is by `RdataRelocKind.dataSectionRipRelDisp32`, filed
  unconditionally. ⚠ **And the fact WAS written twice** (`DataLabelPrefix` vs the literal at
  `CodeResult.maxon:107`) — **they could not disagree only because one was dead.**
  **DELETED 2026-07-15 (make-copies-check), along with `GlobalLabelClass`, `isRuntimeMutableGlobalLabel` and
  the three comments citing them.** ⭐ **The "leave it, it plausibly becomes live at P1.2/P1.5" prediction was
  FALSIFIED before it was read**: P1.0r shipped `__slab_cursor` and `__slab_end` — the exact globals it
  enumerates — and wired nothing to it. Same rule as `__mm_incref`, deleted at P1.0r: *it had no correct call
  site to be missing.* A future name-based classifier single-sources its prefix from `DataLabelPrefix`.
- `PeWriter.maxon:34-37` carried the same false credit.
- `TargetPrinter.maxon` claimed `RequiredData` blocks *"are compared against exactly these lines"* — **the
  rung contradicted itself**: `SpecTestRunner` and `PeSectionReader` both correctly state the opposite, and
  `compareDataSection` never touches `printDataSection`.

**FOUR MORE FOUND AND FIXED 2026-07-15 (make-copies-check). The count is now EIGHT, and the class is not
slowing down** — every one is a comment that was TRUE when written and that nothing re-read when the code
under it moved:
- ✅ `TargetLiveness.maxon` claimed `assertRegClassCountMatches` gives *"the same answer `RegisterFileSize`/
  `RegNum` already give: state the number, and ASSERT it against the thing it mirrors"* — **it asserted
  NEITHER.** It cited a discipline that did not exist. `assertRegisterFileMatchesEnum` now makes the claim
  true **for `RegisterFileSize`**; the comment no longer makes it for `RegNum`, which is the one respelled
  number **nothing can check** (a typealias range bound is readable by no expression, so there is no
  comparand — it moves by hand, and saying so is the only honest option).
- ✅ `RegisterAllocator.maxon` said ``"`RegNum` is `int(0 to 16)`"`` — **it is `int(0 to 32)`**, and the
  sentence around it REASONED FROM THE WRONG BOUND (*"is not one of the 0..15 the file holds"*; the file
  holds 0..31). Worst of the four: a reader checking the sentinel's safety would have checked it against a
  file half the real width.
- ✅ `RegisterAllocator.maxon` (*"a forward sweep over a `u16` in-use mask"*) and `TargetLiveness.maxon`
  (*"u16 register masks"*) — **stale since P1.0d.4 doubled the file to 32.** `RegMask` is
  `int(0 to u64.max)`. Both now name `RegMask` rather than respelling a width, which is the fix that cannot
  go stale again: **a number in prose is just another copy.**
- ✅ `ScaleCorpus.mainSource` claimed *"the enumeration is not written down a second time here and cannot
  drift from the names on disk"* — **true of the `funcNames` loop, FALSE of the thirteen hand-spelled
  driver names directly beneath it.** A comment contradicted by the code it sat on. Fixed structurally: see
  4h.

⭐ **AND NEW CODE SHIPPED UNTESTED BEHIND A CITED PIN THAT WAS DISABLED.** `parseTopLevelAssignment`'s `let`
arm cited `top-level-let-struct-reassign-error` as its test — **which is `disabled-test:` behind P1.1
structs**, and `assignment.md`'s E2013 cases are all locals on a different path. The property needs no
structs at all. ⇒ `top-level-let-scalar-reassign-error` added, verified to pass **and** to fail when
perturbed. **A citation is not a test. Check the pin is ENABLED.**

### 4i. 🔴 OPEN BOOTSTRAP BUG: **E3097 is fully defeated by any narrowing `as` cast** (found by make-copies-check's review)
**A safety check that a one-token edit turns off.** `E3097` (`SemanticEnumAccessorComparison`) exists so
that case-testing an enum through `.name`/`.ordinal`/`.rawValue` is refused — *"so adding a case forces
every site to handle it instead of silently slipping through"*. It is a purely syntactic check on the
COMPARISON's operands, and it sees through a plain local (verified) but **not through a cast**:

```maxon
let o = c.rawValue as Ordinal   // Ordinal = int(0 to 200)
if o == 1 'isRed'               // compiles, runs, correctly identifies `red`
```

**Verified end to end** (exit 100 = the branch fired for exactly one case). That is precisely the
case-test E3097 forbids, and **adding a case to the enum would not force that site to handle it** — the
silent-slip-through the code exists to prevent. ⚠ **A WIDE cast does NOT defeat it** (`as int(0 to
u64.max)` is elided and E3097 returns), which is the tell: what hides the accessor is the range-check
node a NARROWING cast inserts, so the check is keyed on the syntax it happens to see rather than on the
question being asked.

**NOT fixed here**: it is `maxon-sharp/` code, its gate is the full C# suite plus codegen neutrality, and
it has no business riding along on a rung about shv2's constants. ⇒ **Its own rung.** *(shv2 does not emit
E3097 — `notEmittedBy: [shv2]` — so shv2's own parser inherits nothing to fix yet, but it will need the
rule when it reaches semantic checking, and it should key it on the QUESTION, not the token shape.)*

### 4e. ⚠ Three bootstrap bugs P1.0d.5a tripped over (each cost the implementer real time)
- ⭐ **A parameter named `end` SILENTLY DESTROYS the enclosing type's member table.**
  `function tokenTextFrom(start TokenIndex, end TokenIndex)` produced **no diagnostic** — instead **68
  cascading `E4006: Type 'Parser' has no field named 'at'/'consume'/'emitLiteral'`**, pointing at innocent,
  untouched lines **hundreds of lines away**. Cost a full bisect. The codebase dodges it **by convention**
  (`endIndex`, `countEnd`), which is why nobody had hit it. *(Compare `feedback_type_param_keyword_collision`:
  `type`/`enum`/`union`/`interface` as param names were fixed in 2026-04-11 — **`end` was not.**)*
- **`throw` of a union payload matched from a CALL RESULT ⇒ `mm_incref called with NULL pointer`.**
  `match self.f() 'o' … failed(error) then throw error` crashes the compiled compiler: the bootstrap drops
  the temporary scrutinee's box while the payload is in flight. **This is the sharp end of the hazard
  `emitShift` already documents as a *leak* — here it is a null-pointer panic.** Worked around by binding
  both the scrutinee and the payload to locals. *(Same family as #14/#16 — a temporary's ownership is not
  reconciled.)*
- **`self.declAt(i).outcome = x` ⇒ `E2001: unexpected token '.'`** — no field assignment through a call
  result.

✅ **BOTH HALVES ARE NOW DONE — this entry is CLOSED.** `let` = **P1.0d.5a** (281→295); `var` = **P1.0d.5b**
(295→**317**). ⭐ **shv2 does NOT inherit #17's aliasing bug: identity is per-FILE BY MECHANISM**
(`fileScopedDeclKey(name, readerFilePath)`), label = bare name + `$1` **only on collision** (path-free, so
goldens stay stable; `$` is structurally unwritable because `isAlphaNum` is `[A-Za-z0-9_]`). **The corpus had
no `var` twin of `file-private-same-name-cross-file`, so we wrote `specs-shv2/global-file-private-same-name.md`:
shv2 returns 118 where the bootstrap returns 212.** The Std **memory band** now exists (`globalAddr` +
`loadIndirect` + `storeIndirect`) — ⚠ **`loadIndirect` MUST stay `isPure: false`**, or a global's read
**would hoist out of a loop that writes it** (a silent wrong answer). ⚠ **CORRECTED 2026-07-15: this said
"pinned by a spec", and it is NOT — `StdOpMeta.isPure` has ZERO readers, so no spec can pin it. The spec
verifies the ANSWER (a loop returns 10), which is true and worth having, but it stays green with the flag
INVERTED. See #27.**

✅ **THE WORKAROUND IS RETIRED, precisely.** `specs-shv2/short-circuit-elision.md` existed **only** because
there were no globals: with no way to observe a side effect, it proved elision by making the guarded operand
**divide by zero**, so a clean exit *was* the proof. It held **two** things, and only one was a workaround —
**5** divide-by-zero cases (**deleted**, superseded by `short-circuit-evaluation.md`'s real cases) and **9
ordinary value-checked LOWERING tests that were never a workaround** (**moved** to
`specs-shv2/short-circuit-lowering.md`; git reports their goldens as **R100 renames**, which is the proof the
codegen did not move). *A retirement is not a deletion: read what the file actually holds first.*
⚠ **It already cost coverage:** with no globals, short-circuit `and`/`or` had **no way to prove the
right operand is SKIPPED** (an eager `and` returns the *same answer* on every input, so no value test
can see it). `specs-shv2/short-circuit-elision.md` works around it — the guarded operand **divides by
zero**, so a clean exit *is* the proof. **Retire that spec when globals land.**

### 5. ✅ FIXED — the `while` half. The parser's loop phis were **O(V·B)**
`while` minted a phi per mutable var **in scope**, and a function's `var`s accumulate, so the Nth loop
minted ~N phis and burned a ValueId on each — including every one `pruneDeadBlockArgs` later deleted.
`blockArgIdBound`, and every dense column sized by it, is O(ValueIds), so phis nobody kept were paid for
in **bytes** by every pass in between. **This is why the ALLOC column stayed linear while the BYTE column
bent** — the allocations were not more numerous, they were BIGGER.

⇒ **FIX THE CORPUS'S REALISM, NOT THE PROBE** (user directive). ✅ **CORPUS DONE (`f76a145fd`)** —
`longFunction`/`deepBlocks` emitted `var acc = a` then N `if`s all mutating that **ONE** accumulator, so
V=1 and O(V·B) collapsed to O(B): the two knobs *named for growing branches* could not see the cost of
growing branches. Both now carry **six** mutable locals (fixed and realistic — real functions carry a
handful), each branch mutates a **rotating subset** (a branch that wrote *every* local would make "costs
O(vars in scope)" and "costs O(vars assigned)" the same number, and telling those apart is exactly what
decides whether a write-trail is worth building), and `deepBlocks`'s two arms write **different** locals
so the join does real work. *A knob built to expose a bug you already found only finds bugs you already
found.*

⚠ **AND IT WAS BLIND A SECOND WAY, FOUND BY ACCIDENT: the corpus contained `and`/`or` NOWHERE AT ALL.**
The `const` flags fix (#7) removed **four instructions from every short-circuit site in the language** and
this instrument reported **zero movement on every rung** — because it compiles no short circuits. Every
guard in the corpus is now a short-circuit `and`. **Two independent blind spots in one instrument, and
both were silent.** (See #6's rule, which is the same lesson in the other currency.)

**WHAT IT NOW SEES — the defect, exactly as predicted:** `phase:pruneDeadBlockArgs` **allocations** grow
x1.98 x1.99 x1.995 x1.997 x1.999 — *dead linear* — while its **BYTES** grow x2.00 x2.02 x2.04 x2.09
**x2.17**. Allocation COUNT linear while BYTES bend means **the allocations are getting BIGGER**: the
dense columns sized by `blockArgIdBound`. `phase:parse` bends the same way, x1.97 → **x2.06**.
**THE BYTE COLUMN CAUGHT WHAT THE ALLOC COLUMN COULD NOT** — for the second time (see the 2026-07-14 rows
of `docs/optimization-log.md`). *(Those are the BEFORE numbers; the fix's are below.)*

**Where the cost was, measured:** the `if` path is **already correct** — `mergeAtContinuation` mints a phi
only when the value genuinely *differs* across the two paths. **`parseWhileStatement` was the sole ID
inflater:** it minted a header phi for **every mutable var in scope**, unconditionally, *before the body
was parsed*, so it could not know which vars the body touches.

⇒ **Fixed by deciding the carried set UP FRONT, from the TOKENS** (`Parser.parseWhileStatement` →
`namesAssignedIn` / `loopBodyEndIndex`): a loop carries a phi only for the mutable vars its extent
**assigns**. `pruneDeadBlockArgs` bytes **x2.17 → x1.99**, `phase:parse` **x2.06 → x1.99**,
`elimTrivialBlockArgs` **x2.17 → x1.99**, `regalloc:splitting` **x2.34 → x2.03** (fewer dead phis ⇒ lower
maxlive ⇒ fewer forced splits). Eight sequential pressured loops minted **332** header phis of which
**260** were surplus; they now mint exactly **72**. Goldens did **not** move: `pruneDeadBlockArgs` and
`elimTrivialBlockArgs` were already deleting precisely this surplus, so the emitted code was always
identical — the whole cost was intermediate. Both passes stay, on a strictly smaller input.

> ⚠ **THE DESIGN TRAP, and it is worth remembering.** The obvious fix — mint the phi LAZILY, on the
> var's first mention inside the loop — is **UNSOUND**, and not for the reason people reach for first
> (a read before the write; that one is real, and "first *mention*" fixes it). It is unsound because
> **`parseIfStatement` snapshots the mutable-var set into local `ValueId` arrays**, and a phi minted
> lazily *after* such a snapshot retroactively changes the value that was live where it was taken. The
> snapshot is on the Maxon call stack, unreachable from the Parser, so nothing can patch it:
> ```maxon
> var x = 1
> while c 'l'
>     if d 'b'      // entry snapshot of x taken HERE — x has no phi yet, so it captures 1
>         x = 2     // a lazily-minted phi is invisible to that snapshot
>     end 'b'       // ⇒ the if's FALSE edge carries 1, resetting x every iteration. Returns 1, not 2.
> end 'l'
> ```
> **EAGER minting is what makes the snapshots true**, and once minting is eager the criterion is
> *assignments*, not mentions: a var the loop never assigns keeps its pre-loop value on every iteration,
> and that definition **dominates** the header, so a read binds it correctly with no phi at all.

**The `if` half remains** and is *not* fixed: each `if` still copies the whole mutable-var set three
times (entry/then/else). Those snapshots mint **no ValueIds**, so they inflate no dense column — the cost
is O(V) array copies per `if`, linear in the program for a fixed V. Left alone deliberately;
`mergeAtContinuation` already mints a phi only where the two paths DIFFER.

*(Confirmed NOT a multiplier: `emitShortCircuit` mints one phi and takes no snapshot — exactly 58 parse
allocations at V = 8, 32, 128 and 256.)*

---

### 9. ⚠ **STILL OPEN, AND NOW STRANGER: the `mm_incref NULL` in `__module_init` DOES NOT REPRODUCE**
A **real** miscompile of the same class was found and fixed (`bf2823bcb`) — see below — and **it is not
the reported one.** Recorded here rather than closed, because a symptom nobody can reproduce is not a
symptom that has been fixed.

**What was reported** (memory, 2026-07-13): adding a `Testing/`-only **union-typed `Array`** to shv2 made
the C# bootstrap crash `__module_init` with `mm_incref called with NULL pointer`, blaming the **Lexer's**
global `b""`-keyed keyword map — code with nothing to do with the change. Bisected; neither ingredient
alone crashed.

**What is now measured — a DIFFERENTIAL against the pre-fix bootstrap, run deliberately:**

| case | pre-fix | post-fix |
|---|---|---|
| a global `Array` over a payload-carrying union | **FAIL** — `E9001 __module_init`, `%28` undefined | PASS |
| a global map with union *values* | **FAIL** — `E9001 __module_init`, `%29` undefined | PASS |
| ⚠ **global `b""`-keyed Map + union-typed `Array` — THE REPORTED COMBINATION** | **PASS** | PASS |
| a live global surviving a dead global's pruning (control) | PASS | PASS |

**The reported combination passes even BEFORE the fix**, and a fresh union-typed `Array` dropped into
shv2's `Testing/` compiles clean on the pre-fix bootstrap too. So the fix below is real and gated — but
**it is not established to be the fix for THIS symptom**, and the `mm_incref NULL` remains unexplained.

⇒ **Do not assume it is gone.** Either something else closed it between 2026-07-13 and now, or the repro
needed conditions that were not written down. **If it recurs, this table is the starting point** — and the
lesson is the one this file keeps teaching: *the repro is the asset. Record it, not just the symptom.*

**What WAS fixed, and it is the same hole:** `DeadFunctionElimination.EliminateDeadOps` **hand-rolled its
operand list — naming FIVE op kinds out of the ~80 that carry operands** — so everything it forgot was
invisible to liveness and got deleted. `maxon.enum_construct`'s payload was one of them: the literal in
`var g = [Op.add(1)]` looked dead, was deleted, and `__module_init` was left lowering an op whose operand
nothing defined. **An op already declares what it reads; the scan re-declared it, incompletely — ONE FACT
WRITTEN DOWN TWICE.** `MaxonOp.Operands` is now **`abstract`** (the Std tier's `StandardOp.ReadValues`
always was, which is the whole difference), so a new op kind *cannot* silently reintroduce the hole.
It runs over `__module_init` **and nowhere else**, and every global's initializer lives in that **one
shared block** — which is exactly why the blast radius is another file's global, and why this looked like
spooky action at a distance.

### ~~18. ⭐⭐ P1.0d.4 (FLOATS) — the design, and FOUR contract errors caught by reading the code~~ ✅ DONE 2026-07-15, MERGED — **and the contract was wrong TEN times, not four**

> **✅ SHIPPED: `specs-shv2` 317/19 → 355/0, bootstrap 3004/0, 38 goldens added and ZERO modified.**
> The design below survived contact — **one register enum; class-agnostic OPS, class-dispatched
> ENCODERS; the class column travels FORWARD from the lowering.** What did not survive is the *count*.
>
> **SIX MORE contract errors, all mine, and they share ONE shape the four below already hinted at:**
> **I specified TYPE surfaces and not OPERATIONS.** `operandType` with no ops consuming it. A register
> enum with no instructions. A float type with no conversions — **`trunc` had 0 hits in shv2**.
> *Types are what a design looks like written down; operations are what it looks like when it RUNS.*
> A contract with only the first compiles, passes its gates, and blocks every agent at its first edit.
>
> - **#5 no SSE `TargetOp`s** — an encoder needs an op to dispatch from. Adding the band forced **15
>   exhaustive matches across 6 files**: cross-cutting by design, so it cannot be split by owner.
> - **#6 the brief contradicted the spec** on the two-jump lowering (see the ⚠ below — the spec was right).
> - **#7 `movsdRegRip` is NOT rematerializable.** I asserted it must be; `opIsConstMaterialization` asks
>   a NARROWER question — *"can `constValueOfDef` + a `movRegImm` rebuild it?"* — and `leaRegGlobal` is
>   already a priced `false` for the identical reason (a RIP relocation; `true` would PANIC).
> - **#8 no conversion band** (`siToFp`/`fpToSi`), so a float could not enter or leave the float domain.
> - **#9 `classPoolSize` returned the FILE's width (16) where the POOL's (14) was meant** — and the
>   comment I wrote directly above it says *"an int's **14** allowed registers"*. **The worst one.**
> - **#10 `ValueClassColumn` "indexed by ValueId" is only well-defined PER FUNCTION** — `IrValueId.maxon`
>   says so outright. Two agents caught it independently.
>
> ⭐⭐ **AND THE ONE THAT MATTERS MOST: `Parser.mintPhi` hardcoded `argType: i64`, and its comment on
> `main` NAMED THIS RUNG** — *"the scaffold the float (XMM) register class will need at P1.0d.4, and
> that is when it acquires **a consumer and a reason to be right**."* **The rung gave it the consumer
> and not the reason.** `var f = 0.0` in a loop crashed the emitter. Three nets missed it: the contract
> (I never read the comment addressed to me), the AUTHORED spec (`float-compare-branch.md` puts phis on
> a float compare's *edges*, but the values they carry are **ints** — a float-*typed* phi had zero
> coverage), and the instrument (blind to floats for five rungs). **Only fixing the instrument found it.**
> ⇒ **READ THE COMMENT THAT NAMES YOUR RUNG.**
>
> ⚠ **The rung also shipped `0.0 - x` float negation with ZERO coverage** — the first test anywhere is
> `unary-negation.md`, found only by porting the corpus. **Two of the rung's own features were untested
> until someone went looking.** ⇒ **Step 0 is the SPEC PORT LIST, and it is the coordinator's**
> (now in the rung skill).
>
> **Deferred, named:** Wave 2's float ABI (float param/return — **measured**: the callee cannot receive
> one); `floor`/`ceil`/`round` (SSE4.1); float const-folding (needs software IEEE-754; v1 has none);
> **`f64→f32` still coerces implicitly** — the same lossy narrowing, unresolved (see §19).

**~~IN PROGRESS on branch `p10d4-floats`~~ — MERGED.** ~~**RED is banked: 317/19**,~~
every failure `E2004: Expected expression but got 'float literal'` — **the lexer makes the token, the parser
has no arm.** `NumberParsing.parseFloatBits` already returns IEEE bits **as a `ParsedInt`**, so
`MaxonOp.literal(value, valueType)` and `StdOp.const` carry a float **with no new op**; `StdType.f64` /
`stdTypeIsFloat` / `storageBytes: 8` are live. **73 of 276 `/specs` files use a float literal.**

**Settled design:** ONE `X64Register` enum (xmm0-15 at rawValue 16-31 — v1 mints a second enum and pays with
a parallel op for every spill/reload/move); `RegMask` is **already** `int(0 to u64.max)` so bits 16-31 are
free (**the "u16" prose has drifted from the type — fix it**); the Std **arith band carries no operand type**
and must get one (a *field*, as `const`/`param`/`loadIndirect` already have — the alternative is a second type
system in the backend); cond codes are **purely additive** (the enum's backing value IS the nibble); **REX
must follow the mandatory prefix** (one optional param, not a new path); float constants → **`.rdata`**
(read-only; its reloc arm is live, its image renderer is the `.data` twin that landed 2026-07-15); **`trunc`
is a compiler BUILTIN**, so no stdlib cone. **Wave 1 = an ALL-CALLER-SAVED XMM pool** (a float across a call
**force-spills**, free via §3 below); **Wave 2 = the float ABI** — because Win64 preserves **all 128 bits** of
xmm6-15 and **`movsd` ZEROES bits 64-127**, so taking them drags in 16-byte slots + the leaf misalignment
together.

> ### ⚠⚠ FOUR TIMES THE COORDINATOR'S CONTRACT WAS WRONG, AND FOUR TIMES AN AGENT REFUTED IT FROM THE SOURCE.
> **Every one is a SILENT WRONG ANSWER, and none is visible to `scale-test`.** Recorded because the *reasons*
> are the asset — a fifth attempt that re-derives them pays twice.
>
> 1. ⭐ **"Class = a forbidden mask, no new column" — WRONG. The class needs its OWN column.**
>    `clearForbidden` sets the mask to **0** (`TargetLiveness.maxon:551`) and the splitter calls it on the
>    victim (`SplitLiveRanges.maxon:929`) and **every fresh id** (`:936`) ⇒ a class stored there is **WIPED**
>    ⇒ **a float in a GPR**. And **the wipe is CORRECT and must stay** — its own comment: *"its live range is
>    cut at the peak, so it is no longer live across the call that forbade it… A mask that only ever grows
>    would keep saying it was."* ⇒ **THE DISTINCTION: the CLASS is what the value IS — intrinsic, permanent.
>    `forbidden` is what the ops it crosses TOOK AWAY — contextual, recomputable.** This is the through-line's
>    **DUAL**: not one fact written twice, but **TWO FACTS WRITTEN IN ONE PLACE**.
> 2. ⭐ **"`witness:` becomes the class pool" — NECESSARY BUT NOT SUFFICIENT. `hallVerdictAt` must run PER
>    CLASS over a class-FILTERED live list.** `valueConfinedTo = (allowed and not witness) == 0`
>    (`HallCondition.maxon:96`) — and a float across a call has `allowed = ∅`, which §3 buys **deliberately**.
>    **∅ is a subset of EVERY set**, so that float tests CONFINED against *any* witness, including a
>    callee-saved **GPR** witness from an int-only overflow. ⚠ **And the consistency assert PASSES**:
>    6 ints + 3 floats gives `expected = popcount(witness) + (valueCount − matched) = 5 + 4 = 9 == tight`
>    (`:469-472`). Deficit 4 is right **by coincidence** (3 float + 1 int) and structurally wrong —
>    `chooseVictim` may spill 4 ints (floats never spill ⇒ colorer panics) or a float to relieve a **GPR**
>    peak (freeing an XMM no int can use ⇒ pressure does not drop ⇒ the re-pick loop `:21` warns of).
> 3. **§1 and §2 INTERLOCK — neither alone is safe.** After §1's fix an int's `|A|`=14 against
>    `fullPoolSize = popcount(pool)` = 30 ⇒ **every value reads CONSTRAINED** ⇒ permanent census over every
>    operand of every op — **a TIME regression `scale-test` cannot see.** Fix: `constrained` is
>    `size < classPoolSize(class(v))`. **But that restores `constrained == 0` for 15 live ints, so the
>    per-class full-pool pigeonhole is then the ONLY thing that catches them** (`effective > fullPoolSize`,
>    `:1251`; also `:674`, the residual guard `:803`, and `witness:` `:1252`). **Both halves, or one failure
>    survives.**
> 4. **`pickPreferredRegister`/`preferredClassMask` are a WRONG-ANSWER site, not a name clash.** Both use
>    `fullRegisterMask()` as "the whole pool" (`RegisterAllocator.maxon:864,886`) ⇒ widened, an **int** whose
>    GPRs are all blocked is handed an **XMM**. ⚠ Their own comment — *"so a HINT can be held to the same
>    class; the two must agree, or the hint path quietly bypasses the very protection the fallback provides"*
>    — uses **"class" for caller/callee-saved**, which collides with *register* class head-on. **Rename one.**

**Three more, unflagged in any contract so far:**
- ⚠ **`FoldConstOperands` rewrites a const-rhs `cmp` into `cmpImm`** (an **integer** `cmp reg, imm32`).
  **A float compare must be excluded, or `3.5 > 9.5` folds through an integer immediate compare on IEEE bit
  patterns.** ⭐ **And `float-type/float-comparison` would likely still PASS — BY LUCK**, because positive
  doubles order correctly as sign-magnitude integers. **`float-compare-branch/lt-nan-is-false` catches it.**
  Same exclusion for `binOpImm`.
- ⚠ **`binOpResultTag` (`TypeRules.maxon:530-536`) returns `integer` for ALL arithmetic** — **that IS the
  `float-promotion` bug**, and the fix is numeric promotion **there**, not in the backend.
- **Float `/` must NOT ride `StdOp.div`**: that variant is `isPure: false` because **`idiv` faults** and
  **`divsd` does not** ⇒ by the dialect's own rule it belongs as a `StdBinOpcode` on `binOp`.

**⚠ TWO SILENT-WRONG-ANSWER HAZARDS, each falsifying a design property an shv2 file STATES:**
- ⭐ **An f64 compare lowers to TWO conditional jumps** (`jp` + an ordered jump, both to the same else
  successor). **`SsaDestruction` assumes ONE per block** — `IrBlock.CondBranch`: *"the block's SECOND
  successor is named by a body op"* (**singular**). **v1 SHIPPED this as a miscompile** (rewired only one
  jump ⇒ a phi's copy was skipped), documented **in the spec itself** (`specs/float-type.md:130-135`) and in
  `project_x64_f64_compare_phi_copy_fix`. ✅ **`specs-shv2/float-compare-branch.md` is authored and RED for
  exactly this** — and its `float-cmp-materialized` case pins the **`setcc`-with-parity** path, which is a
  **second lowering**, not a free consequence of the branch one.
- **There is NO `xchg xmm, xmm`.** `SsaDestruction.maxon:7` states *"Copy CYCLES are broken with
  `xchgRegReg` — no scratch."* v1 reserves xmm15. **Decide: a reserved scratch, or 3 moves through a slot.**

⭐ **Do NOT inherit v1's FP warts:** `createX64CallerSavedFps()` takes **no `os` param** while its GPR twin
does (*"Windows differs from SysV"*) — **a SysV table applied to Win64**, declaring xmm6/7 caller-saved when
they are **not** and never saving them; its FP callee-saved half is **literally ZERO** (so every float across
a call spills — which surfaces as v1's own **disabled tests**, `specs/float-type.md:157`); and
`emitRematConst` **panics for FP** (*"not needed for any current case"*) — **shv2's splitter DOES
rematerialize**, so a float const must remat to `movsd reg,[rip+const]`.

---

### 19. ⭐ `/specs` CONTRADICTED ITSELF on lossy narrowing — half fixed, half OPEN

**The through-line — ONE FACT WRITTEN DOWN TWICE — is in the CORPUS, not just a compiler.** Found
2026-07-15 at P1.0d.4, by the **user**, against my own (wrong) conclusion:

| Spec | Said |
|---|---|
| `specs/type-casting.md` | *"**Lossy conversions are not allowed.**"* `5.0 as int` ⇒ **E3009 — "use trunc/round/floor/ceil instead"** |
| `specs/implicit-type-conversion.md` | a doc TABLE: `float → int: **Truncate toward zero**`. `takeInt(3.7)` ⇒ **exitcode 3** |

⇒ Maxon **rejected an explicit `5.0 as int`, telling you to write `trunc()` — then silently performed
that exact truncation implicitly.** The explicit path demands you say what you mean; the implicit path
did it behind your back. **`trunc()` already existing is the argument**: silent truncation gives the
language two ways to narrow, one invisible, and makes the explicit one redundant exactly where the
silent one fires.

**✅ FIXED (float→int only), all three: spec + shv2 + bootstrap.** Rule drawn **WHOLE** — a coercion
site is anywhere a value meets a declared type, so `return f` too, **and that mattered: `return f`
panicked the x64 emitter**, one site over from the call a narrower fix would have covered. **E3009
reused**, not a new code. **`int→float` is untouched** — widening, lossless, and `type-casting.md`
blesses it.

**⭐ The bootstrap DISAGREED WITH ITSELF, which is what proves it was a bug and never a design:**
`CoerceValueToExpectedKind` (return + assignment) had **always** rejected float→int via
`IsWideningCastSafe`; `ConvertArgToParamType` carried its own table that truncated. **One conversion,
two rules, one compiler — only the call path was wrong.**

**⚠ STILL OPEN — `f64 → f32` coerces implicitly in BOTH compilers.** Also lossy, also silent, also
absent from `type-casting.md`'s E3009 list. It is float→**float**, so it sat outside the ruling and was
flagged rather than swept in. **It is the same bug wearing a different pair of types.**

**⚠ ALSO OPEN — three rules now answer three questions about one pair of tags** (`typesAgree` = may
they meet · `checkDeclaredType` = may a value meet a declared type · `comparableOperands` = may they be
compared). **Deliberately NOT unified — merging any two collapses them into each other's bugs**
(1+3 kills `5 + 2.0`; 2+3 re-legalises `f > i`). Verified distinct by the review. **Do not "simplify".**

⚠ **METHOD, and it is the reusable part:** I concluded "canonical, implement it" from **the test's NAME**
(`float-to-int-param-truncates`) plus *"the bootstrap passes it"* — **before reading the expectation**.
**A compiler passing a spec proves only that it implements what the spec says, contradiction included.**
**A test NAME is the thing most likely to be a lie once behaviour changes.** ⇒ **Read the EXPECTATION,
and when two specs could cover one rule, GREP FOR THE OTHER ONE.** The user said *"I'm pretty sure the
spec tests are clear about this"* and was right — the clarity was in a file I had never opened.

### 20. ⚠ arm64 / wasm fragments cannot be regenerated on a Windows host — STRUCTURAL

`spec-test --target=arm64-macos` **crashes** (`Win32Exception 193: not a valid application for this OS
platform`): the runner cross-compiles and then tries to **EXECUTE**. Reproduced identically on
`while-loops`, so it is **not** float-specific and not P1.0d.4's.

⇒ **A compile-ERROR test still emits its fragment** (never executed), which is why
`float-to-int-param-rejected` has arm64/wasm fragments while the executing
`float-to-int-param-explicit-trunc` does not. P1.0d.4 left `specs/fragments-{arm64-macos,wasm32-wasi}/`
short by exactly that one file, and **hand-writing it was refused** — it needs an on-target run.
Same class as [[project_arm64_pending_verification]].

## 🔧 Instrument & tooling

### ~~7. The corpus is blind to the feature just landed~~ ✅ FIXED for floats at P1.0d.4 — after **FIVE** rungs, and it was hiding a compiler crash

**`ScaleCorpus` printed its own blind spot** — *"NOT GENERATED — a codegen change to any of these is
INVISIBLE here (structural blind spot): … **float arithmetic**; …"* — so a float rung measured
**byte-identical**, and that zero was **not a result**: it was the integer path not regressing, and no
evidence whatever about floats.

**The consequence nobody had drawn: the rung process GATES THE OPTIMIZER on `scale-test`.** An
optimizer told to hunt superlinear algorithms *"gated objectively by scale-test"* had **no instrument
for the code the rung just wrote**. ⇒ **Extending the corpus is a PRECONDITION of the optimize step.**

⚠ **This is NOT "touching the instrument to make a number look better"** (the forbidden thing —
`regalloc:liveness` was once exempted from a check to stop it complaining). **Making the instrument SEE
is the opposite of silencing one it has.** The distinction is the whole entry.

**✅ P1.0d.4 grew `floatOps` + `floatSpill` knobs that DOUBLE like every other** (so the rung-over-rung
ratio IS the growth), covering float arith, both compare forms, float phis, promotion, `trunc`, and **4
floats live across a call** (`allowed = ∅` ⇒ Wave 1's forced spill). Int knobs byte-identical, so the
log's columns stay readable downwards. **The FIRST thing it saw was a compiler crash** (`var f = 0.0` in
a loop — see §18's `mintPhi`). Floats are now **22.9%** of allocations at rung 5; nothing superlinear
(≤32.01× across a 32× ladder).

⚠ **The pattern was logged at 3× and reached 5× before anyone fixed the tool** — because everyone who
hit it treated it as a footnote about *their* measurement rather than a defect in *the instrument*.
**The corpus is grown to expose the LAST bug, so it is blind to the NEXT one.** The remaining blind
spots are still printed in its manifest: **read them before trusting a zero.**

### 6. The scale instrument keeps silently dying on its own leftovers — **TWICE now**
- `compileRung` wrote `metrics.tsv` / `rung.exe` **into the directory it was measuring** ⇒ cold and warm
  runs disagreed by **+26 allocs/rung**. *(Fixed.)*
- `--per-type` copied sources to a scratch dir and **only ever wrote, never deleted** ⇒ after three files
  were deleted from the tree, any stale `.scale-tmp` kept compiling them, the trace build died on
  `Unknown type: ScaleVerdict`, and **the pass SKIPPED WITH EXIT 0**. The by-SCOPE table — the one column
  that names the *function* that allocated — worked only on a fresh checkout. *(Fixed.)*

⇒ **THE RULE, so there is no third time:** every directory the instrument generates must be
**reconstructed from scratch on every run**, and anything it did not generate this run must be **refused
or removed — never inherited**. *A measurement that depends on what a previous run left behind is not a
measurement.* Both failures were **silent**.

### ~~8. The DebugStream can hand the monitor an unwritten payload~~ ✅ FIXED — and the fix had a hole, now also fixed (`b896a70bd`)
The torn-read race itself was already closed on `main` (`4cfbba70d`: *an entry is VISIBLE when reserved,
READABLE when committed*), verified by a stress harness that **fails against the unfixed compiler**.

⚠ **But adding the commit bit was a BREAKING WIRE CHANGE THAT NEVER BUMPED `DsVersion`** — and the version
was written by the monitor and **read by nobody**. The flags byte used to be always-zero filler; it now
distinguishes *"payload written"* from *"not yet"*. So a **pre-`4cfbba70d` binary under today's monitor**
never sets the bit, the monitor waits for a commit that is not coming, the ring fills until the producer
**drops 98% of its events**, and it then steps over every entry as *"abandoned (producer died mid-entry)"*.
**Measured, with a real v1-schema producer: `0 events decoded, 283221 dropped, 5290 abandoned` — and the
producer had exited CLEANLY with code 42. Every number in that summary is a fiction.**
⇒ `DsVersion = 2`, and a **two-way** handshake (the monitor announces its version; the producer announces
its own at `DsOffProducerVersion` **before it decides whether to speak at all**, so a producer that refuses
an incompatible monitor is *silent*, never *anonymous*). Mismatch ⇒ loud refusal, exit 3, nothing decoded.
**An instrument that lies is worse than no instrument** — and this is the instrument the *parallel*
compiler is meant to be debugged with.
⭐ **shv2's backend must emit this protocol** (Workstream R1). It is a wire format two compilers must
agree on: the commit bit and the version live in **one** place each, beside each other.

### ~~7. `StdOp.const` claims `clobbersFlags`, and `TargetOpMeta.setsFlags` is written 40× / read 0×~~ ✅ FIXED (`282d08421`)
`const` now declares `clobbersFlags: FALSE` — it lowers only to `movRegImm32`/`movRegImm`, a bare `mov`,
which writes no EFLAGS. The `true` was justified by a comment describing an `xor reg, reg` zeroing idiom
**the lowering does not emit and never has**. The win was bigger than the 3 instructions predicted: each
short-circuit site drops **FOUR** — `setcc`, a redundant `cmp`, **and the phi copy** (the seed literal is
now minted straight into the phi's register) — plus one fewer live register.
`TargetOpMeta.setsFlags` is **deleted**. It was a **v1 PORT ARTIFACT**: v1 genuinely reads `setsFlags` off
a *target* op (`MirToX64Conversion.maxon:409`) because it fuses compare/branch at the MIR→X64 boundary;
shv2 moved that scan **up to the Std tier** — its answer decides what the lowering emits, so it must run
before it — and kept v1's field anyway. Two homes for one fact, one unreachable, and the dead one was
quietly *right*.
⚠ **Written down while there:** the fusion rests on a precondition nobody had stated. The scan runs on
**Std** ops, but the **allocator later inserts Target ops into the very window it just proved safe**. It
survives only because every op the allocator can insert there — spill, reload, phi copy, `xchg`, a
rematerialized `const` — is `mov`-class and flag-neutral. **Add a flag-writing one and the fusion becomes
a silent miscompile the Std tier cannot see coming.**

### ~~7b. `a shl -1` silently becomes `a shl 63`~~ ✅ FIXED — **E2054**, in BOTH compilers
The hardware masks CL to its low 6 bits. Defensible for a *runtime* value (the compiler cannot see it,
and both lowerings agree). **Not** for a literal it can see: a negative count reads as "shift the other
way" and silently became the *maximum left shift*.
⇒ **A shift-count LITERAL outside `0..63` is now a compile error (E2054)**, positioned at the literal.
Also catches `shl 64` (≡ `shl 0`, a no-op) and `shl 100` (≡ `shl 36`). A shift by a **runtime** value is
UNCHANGED and still masks — pinned by two passing spec cases, so the rule cannot later be "fixed" into an
over-rejection. It was free, as predicted: **zero** out-of-range shift literals existed in any real code,
and **no golden moved** in either suite.

⚠ **The bug UNDERNEATH it was #12 wearing different clothes: the `0..63` bound was WRITTEN DOWN TWICE**
(shv2's `FoldConstOperands.MaxShiftCount` *and* `TargetDialect.ShiftCount`) **and the front end enforced
neither copy.** So the fix does not add a third: shv2 now has ONE declaration — TypeRules'
`MinShiftCount`/`MaxShiftCount` + `shiftCountInRange` — which the parser and `foldConstOperands` both ASK.
`TargetDialect.ShiftCount`'s `int(0 to 63)` is the **one forced restatement**, and says so in a comment:
a ranged typealias's bounds must be literals, so the language cannot express
`int(MinShiftCount to MaxShiftCount)`. *(A real gap, if a small one: a ranged typealias cannot be bounded
by a named constant, and `Alias.min`/`Alias.max` do not exist either — only builtin sized types have
those. It is the only reason this fact is still written twice.)*

The bootstrap check runs at **both** parse sites that can meet a shift — the expression parser *and*
`EvalConstShift`, where `let MASK = 1 shl 100` was silently evaluating to `1 shl 36` through C#'s own
`<<`. That second site was not in the original report and is the same defect one tier up.

### ~~12. ⚠ NEW — the ERROR-CODE REGISTRY is written down TWICE, and two agents collided on E3099~~ ✅ **FIXED — and this entry was STALE, which is its own instance of the disease**

> **`docs/error-codes.txt` is now the single source of truth**, with `maxon error-codes generate` producing
> four files and `maxon error-codes check` **failing the build** on a duplicate number, a duplicate name, a
> drifted generated file, or a **dead claim** — and it runs wherever a generated file is USED, not only where
> it is produced. See `.claude/CLAUDE.md` → "Error codes — ONE registry, ONE parser". Verified 2026-07-16:
> `ErrorCode.cs` is extension methods with **zero** numeric assignments, and
> `maxon-selfhosted/Compiler/ErrorCode.maxon`'s only four-digit numbers are **band boundaries**
> (`>= 3000 and < 4000`). Every build in this session echoed `error-codes: OK - 130 codes (16 reserved),
> registry hash 8feb6d75fcf1e5fe, 4 generated files up to date`.
>
> ⚠ **The lesson is about THIS FILE.** A backlog whose stated through-line is *ONE FACT WRITTEN DOWN TWICE*
> was itself carrying a fixed entry that `CLAUDE.md` documents as fixed — **the fact written twice, and the
> copy nobody executed went stale.** Exactly what #4g's dead `classifyGlobalLabel` was, one file over —
> and that one has now been deleted rather than left to rot (4h). **An entry here is not evidence; check
> it before you act on it.**

**~~The original report:~~**
`maxon-sharp/Compiler/ErrorCode.cs` and `maxon-selfhosted/Compiler/ErrorCode.maxon` are **two registries
for one number space**, and nothing makes them agree. `mcp__maxon-dev__lookup_error_code` parses only the
**`.maxon`** one — so a code added to the C# side alone is **undiscoverable** (E3098 was exactly that).

**It has already cost a real collision.** Two agents, in two worktrees, on the same day, each looked for
"the next free code" and each correctly found **E3099**:
- `SemanticCapturingClosureInField = 3099` (a capturing closure stored in a struct field)
- `semanticPromiseAlreadyAwaited = 3099` (a promise awaited twice)

Caught at merge, by hand. **Nothing in the build would have caught it** — two enums in two languages, one
number space, no cross-check. The second was renumbered to **E3100**.

⇒ **The fix is the usual one: make it ONE fact.** Either generate one registry from the other, or add a
build-time check that the two agree and that no number is issued twice. Until then, **every new error code
is a coin flip**, and the next collision will be found by a user, not by a merge.

### ~~8. The bootstrap cannot CALL a function-typed FIELD~~ ✅ FIXED (`b443f497e`) — and a bigger bug is behind it
The indirect-call lowering existed in **exactly one place**: the bare-identifier branch of `ParsePrimary`.
That is why a function-typed **parameter** worked and *nothing else did*. Every other producer of a function
value handed it back and **never looked for a `(` suffix**. Fixed by giving the postfix chain a call arm, so
a function value is callable **wherever it lands** — a field, `self.op`, a field chain, an array element, a
returned function, and as a **statement** (which had no notion of a function value at all).

⚠ **BEHIND IT, A SILENT MISCOMPILE THAT IS *NOT* ABOUT FIELDS — see #13.**

### ~~13. a capturing closure that ESCAPES its frame nil-derefs, by ANY route~~ ✅ FIXED (`99a8927b9`) — and the sweep found TWO routes nobody had listed
Closures capture **by reference** — `LowerClosureCreate` stores the *addresses of the enclosing frame's
stack slots*. So a capturing closure that outlives its frame reads a dead frame (the classic
upward-funarg problem): **it compiled clean and died at runtime.**

**E3099 was a PARTIAL FIX OF A GENERAL DEFECT** — it guarded the struct FIELD and nothing else. It is now
the rule itself: **a closure that CAPTURES may not ESCAPE its defining frame**, refused on every route the
parser sees *without* interprocedural analysis. `E3099` was REUSED, not renumbered — one mechanism, one
rule, one code — and registered in BOTH registries (see #12).

**The enumeration is the artifact.** Each route was *run*, not reasoned about:

| route | before | now |
|---|---|---|
| **`return` it** (`makeAdder` — the idiom people write) | compiles ⇒ `panic: nil pointer, in _$closure_0` | **E3099** |
| **GLOBAL / static** | `E9001` internal cast crash at lowering | **E3099** |
| **CONTAINER** (array/map literal element) | `E9001 Unknown MaxonValue type: MaxonFunctionPtr` | **E3099** |
| ⭐ **union assoc-value PAYLOAD** | **COMPILED AND RAN**, env silently dropped | **E3099** |
| ⭐ **PAYLOAD BINDING** (`run(op) then op = <closure>`) | **COMPILED AND RAN**, env silently dropped | **E3099** |
| struct FIELD | E3099 (`3f42ecd7d`) | E3099 (message generalized) |
| call ARGUMENT / call RETURN value | runtime nil-deref | **still open — DELIBERATELY**, see below |
| call it, or pass it DOWN to a callee that only calls it | works | works (**must** — shv2's own `LazyMessage`) |
| global/static **DECLARATION**, `async`, enum fn-BACKING | unreachable *by construction* | unreachable |

⭐ **The two starred routes were found by sweeping the OPS, not the four routes on the list** — and they
were the only two that were *silent*. A payload binding is the nastier: it **looks like a plain local** but
is an alias INTO the enum's heap box, so assigning through it writes back. *A rule enumerated from
symptoms finds the symptoms you already had.*

**The rule is literally "escapes ITS DEFINING frame", not "is returned":** each capturing value records
the frame its environment points into, so returning a closure an *outer* frame built stays legal — that
environment belongs to a frame still alive. **Over-rejection is the worse failure**, and the same sweep
found a **latent FALSE REJECT that predated all of this**: the capturing-var map is keyed by NAME and was
never cleared between functions, so a capturing `op` in one function could make an unrelated *parameter*
`op` in the next look like it carried an env. Cleared per function; pinned by a spec.

⚠ **STILL OPEN, ON PURPOSE — the INTERPROCEDURAL route.** A capturing closure passed as a CALL ARGUMENT to
a callee that then stores it (`Handler.create(function(n) gives n + bump)`), and symmetrically one arriving
as a call's RETURN value. At that store the value is a **parameter**, and whether it carries an env is a
fact about the **caller** — deciding it needs a per-parameter escape summary propagated over the call
graph, i.e. **escape analysis proper**. That is **exactly shv2's P1.5**, where capture-into-heap **IS**
escape and closures co-land with `async` for that reason. Scoped out of the bootstrap (which shv2 retires);
**named in a comment at the check** so the boundary is found rather than rediscovered.

*(The stray tail that used to hang off this entry — "a struct can store a function but nothing can call it
from there" — was **stale**: it belonged to #8 and was superseded by that item's own ✅ fix (`b443f497e`),
which made a function value callable wherever it lands. Removed.)*

---

### 14. ⚠ NEW — the bootstrap LEAKS on a ternary whose arms mix a BORROWED read with an OWNED value
```maxon
let t = entry.returnType if remap.isIdentity else remapMaxonReturnType(entry.returnType, remap: remap)
//      ^^^^^^^^^^^^^^^^ borrowed field read      ^^^^^^^^^^^^^^^^^^^^^^ freshly-allocated union
```
⇒ **`MM leak: 7 allocation(s) remain`, exit 101, on EVERY build.** The conditional does not reconcile the
two ownerships. Written as an `if`/`else` instead it is clean — and hoisting the loop-invariant test out of
the loop was better code anyway.

**Fails LOUDLY** (the leak gate catches it), which is the only reason it is tolerable. But it cost a **wrong
diagnosis first**: the leak arrived in the same edit as a `Map` value-type change, and the *obvious* suspect
— `Array with <union>` as a `Map` value — was **wrong**. A/B'ing one variable at a time found the real
cause. *Verify your own diagnosis; the plausible one was not it.*

⚠ **shv2 will hit this too** — a ternary is the natural way to write "remap only if the ids moved", and it
is the natural way to write a dozen other things.

### 15. ✅ FIXED — the bootstrap RESET the register allocator over any block that PANICS
`RegisterManagerBase.BlockAnalysis.FindDivergingBlocks` (shared by the **x64 AND arm64** conversions) called
any block holding an `mrt_panic` call with no terminator "diverging", and the caller then **`Reset()` the
register allocator over it** — dropping every value's register *and* its stack home.

Its own comment stated the invariant that made that safe:

> *"the diverging block accesses cross-block values through variable stack slots (memref.load), not SSA refs"*

**It was an ASSUMPTION, not a test, and it was false** the moment a panicking block could also be an
*ordinary* one — the fall-through of a guard, holding a user's `panic("… {expr} …")` whose message
interpolates a value computed BEFORE the guard:

```maxon
function f(bits Num)
	panic("v={1 shl (bits - 1)}")   // the shift's guard splits the block; the panic block reads `bits - 1`
end 'f'
```
⇒ `E9001: RegisterManager: value %3 has no register and no stack home`.

**The invariant is now ENFORCED** rather than assumed: a block is diverging only if it *also* reads no SSA
value defined in another block. Found because **maxon-shv2's own `BinaryHelpers.assertSignedReach` is exactly
that shape** — it panics with an interpolated `1 shl (bits - 1)`.

*The lesson: a comment that states a precondition is a test that was never written. This one had been true
for as long as every panicking block was a compiler-generated trampoline.*

### 16. ⚠ NEW — the bootstrap LEAKS a payload-carrying error built in a helper and thrown by its caller
```maxon
throw self.shiftCountNegative(folded, countStart: countStart, countEnd: countEnd)   // LEAKS the box
```
vs. the same error constructed at the throw site:
```maxon
let anchor = self.shiftCountAnchor(countStart, countEnd: countEnd)
throw ParseError.shiftCountNegative(folded, line: anchor.line, column: anchor.column)   // clean
```
⇒ **`MM leak: 1 allocation(s) remain`** on every compile that raises the diagnostic. `throw <call>` — where
the call RETURNS a payload-carrying error union — does not transfer ownership of the box to the throw.

**Fails loudly** (the leak gate catches it, and the spec runner surfaced it as a *stderr mismatch* between two
byte-identical messages, because the leak line was appended to the actual stderr). Worked around by
constructing at the throw site. ⚠ **This is the natural way to factor a diagnostic whose position needs
computing**, so it will recur.

### 21. ~~⭐ An enum range arm over an EXPLICIT-int-backed enum matched NOTHING — silently~~ ✅ FIXED 2026-07-15 (bootstrap)

**Found while PLANNING P1.1, by probing the oracle rather than reading it.** `IrEnumCase.Ordinal` was
**ONE FIELD WITH TWO MEANINGS** — the project's signature bug, again:

- it is the **auto-increment TAG counter**, which an explicit `= N` resets to `rawValue + 1`
  ([2-Parser.cs:3328](maxon-sharp/Compiler/2-Parser.cs#L3328)). **That reset is CORRECT and load-bearing** —
  it is what gives `enum E { a = 5  b }` the tag **6** (measured), so it cannot simply be deleted.
- but the **range-arm code read it as a DECLARATION POSITION** — for both the coverage loop and the
  comparison bounds.

⇒ For `enum Level { low = 10  medium = 20  high = 30 }` the stored ordinals are **0, 11, 21** — neither the
declaration position nor the raw value. **A third number that means nothing.** So `low to medium` compiled
to the bounds **[0, 11]**, which exclude `medium`'s own tag (**20**):

```maxon
match l 'm'
    low to medium gives 41      // <- medium is covered by THIS arm
    high gives 42
end 'm'
// classify(Level.medium) returned 0. Not 41. Exit 0, no diagnostic, no crash.
```

⚠ **It compiled clean, ran clean, and returned the wrong answer** — and the exhaustiveness checker called
the match total **while reading the same broken numbers**. The two agreed because they were wrong the same
way. The third witness: `.ordinal` the ACCESSOR returns the true declaration position (measured: `1`, `2`),
so the accessor and the field **disagreed about what "ordinal" means**.

**Why it survived:** the corpus had **no** case pairing an explicit *int* backing with a range arm — the
one explicit-value enum in `enum-match-exhaustive.md` is **float**-backed, and the float arm took a
*different* path that read `RawValue`. **One function, two opinions, and the tested half was the right one.**

✅ **FIXED**: a range arm is now the **OR of the cases it covers**, by declaration index — which is what
`/specs`'s *"ranges use the enum's ordinal order (the order cases are declared)"* means (**user ruling,
2026-07-15**: the doc is right, the code was wrong). ⭐ **The OR is the MEANING, not the codegen**: a
two-compare range over the tag says the same thing whenever no *uncovered* case's tag falls between the
covered tags' extremes — true of every auto-increment enum — so the range is kept where it is EXPRESSIBLE
and the OR paid only where it is not (`ok = 500  notFound = 200` spans the tags [200,500] and would swallow
an uncovered `serverError = 404`). **Measured: 15 fragments moved without that rule, 1 with it.** The float
special-case is **deleted**, not mirrored — one rule where there were two that disagreed.
**Gates: C# 3009/0** (3004 + 5 new), **shv2 355/0**, `specs-shv2/` untouched, the single remaining `M` is
`x < 2` → `x <= 1` (same instruction count, provably equivalent — the bound is now a real tag).
⚠ **shv2 must implement ORDINAL order at P1.1b. Do not port the raw-value reading.**

### 23. 🔴🔴 **THREE GREEN INSTRUMENTS SAT ON A 1000× MEMORY BUG — and the SIXTH consecutive rung was invisible to `scale-test`**

**The bug** *(fixed at P1.0r, `ac512711a`)*: `TargetDialect.iatCall` carried **`implicitDefs: 0`** under a
comment reading *"Full call-barrier semantics."* **ONE FACT WRITTEN TWICE, DISAGREEING** — in the op
metadata, which is exactly where the compiler trusts it most. Liveness folds `implicitDefs` into
`forbiddenPhys`, and `iatCall` has no explicit operands either, so **nothing at all was forbidden across an
OS call.**

**INERT for its whole life** — the only `iatCall` in the tree was `mrt_start`'s hand-built `ExitProcess`
stub, which has no virtual register live across it. **P1.0r put one inside a register-allocated function and
it went live the same day.** In `__slab_alloc`'s grow block, `VirtualAlloc` clobbered the register holding
`size`, so the cursor published `base + base` instead of `base + size` ⇒ `next > end` on every later call ⇒
**the bump path is never taken again: one 64 KiB VirtualAlloc per object.** Measured **133.74 MB** for 2,000
Points → **412 KB** fixed.

### ⚠⚠ THE POINT IS NOT THE BUG. IT IS THAT EVERY GATE WAS GREEN OVER IT.

**It never produced a wrong USER-VISIBLE answer**, so:
- **`specs-shv2` 357/0** — green.
- **worker-count invariance** (`-j1` vs `-j12`, byte-identical) — green.
- **`scale-test`** — green, **and structurally could not have seen it**: its corpus manifest says
  `NOT GENERATED … structs` **in its own words**. Not one line of `osAllocPages`, the slab, the refcount
  runtime or the leak gate is executed by any number in that table (`phase:runtimeAugment` is flat 54→58 —
  the runtime is correctly never installed). **Everything this rung shipped was measured OFF-instrument.**

⇒ **Only reading the emitted machine code found it**, and only because an INDEPENDENT reviewer read it. The
author, the optimizer, and three gates all passed over it.

⇒ **THIS IS #4d's "THIRD instance" — NOW THE SIXTH.** *"The scale corpus is systematically blind to the
feature just landed."* At P1.0d.4 that hole hid a compiler crash (`var f = 0.0` in a loop). Here it hid a
1000× memory regression. **It is now the highest-value gap in the tooling, and the next rung should close it
before adding a feature.** The fix is not subtle — the corpus generator must emit the constructs the rung
just landed.

> ### ✅ **THE CORPUS HALF IS CLOSED — 2026-07-16, and it was done BEFORE the next feature, as this entry asked.** See **#4d**: five knobs, the four blind rungs measured onto the ladder, and the manifest that called them covered rewritten. **Structs, the heap, globals and the idiv path are all generated now.**
>
> ⚠⚠ **BUT DO NOT READ THAT AS "THIS BUG WOULD NOW BE CAUGHT" — IT WOULD NOT, AND THE REASON IS WORTH MORE
> THAN THE KNOB.** Two facts, each verified rather than reasoned:
> - **`scale-test` COMPILES each rung and never RUNS it.** `ScaleTestRunner.compileRung` spawns `build`,
>   reads the metrics TSV, and returns; `rung0.out` is written and never executed. So the columns are about
>   **compiling**, and `__slab_alloc` — the runtime shv2 *emits* — never executes during a scale-test at all.
>   A struct knob does not change that.
> - **Even a runtime `__mm_alloc_count` column would have missed it.** Under the bug, 2,000 Points is
>   **2,000 `__mm_alloc` calls either way**. What went 133 MB → 412 KB was *pages taken from the OS*, and the
>   runtime's only globals are `__slab_cursor`/`__slab_end`/`__mm_alloc_count` — **there is no counter for
>   that.**
>
> ⇒ **The sentence above — *"the instrument was right and pointed at the wrong program"* — is a STORY THAT
> FITS, written one entry away from #22's *"attribution is not a story that fits, it is a measurement."*
> Left standing, with its correction beside it, because the error is the lesson.**
>
> ⭐ **WHAT ACTUALLY CLOSED THIS CLASS — and it is not an instrument.** The fix (`ac512711a`) did not merely
> set the mask; it added **`assertCallClobberConsistent`**, which compares `TargetDialect.iatCall.implicitDefs`
> against `RegisterAllocator.callerSavedMask` on **every `allocateRegisters` call** and panics naming the
> drift. **Verified by trying to reintroduce the bug: `implicitDefs: 0` no longer compiles anything** —
> *"iatCall clobber mask 0 != caller-saved pool 4294905799 — TargetDialect.iatCall's implicitDefs and
> RegisterAllocator.callerSavedMask drifted apart."* **The bug is now unrepresentable, not merely fixed.**
> ⇒ **For ONE FACT WRITTEN TWICE, the fix is to make the two copies CHECK each other. That beats every
> instrument, because it cannot be pointed at the wrong program.** The gap that remains is real — shv2's
> emitted runtime is measured by nothing, and P1.2's `String` and P1.5's GT scheduler live there — but it is
> a **runtime instrument** that would close it, and it needs a reporting channel that does not exist
> (a corpus program has no `print`, and an exit code is 8-bit). **That is its own rung, and it should not be
> smuggled into a corpus one.**

⚠ **And note WHICH instrument would have caught it**: `scale-test` measures **memory**, exactly, bit-for-bit
— a 1000× allocation blow-up is the *one thing it is built to see*. It missed it purely because its corpus
does not contain a struct. **The instrument was right and pointed at the wrong program.**

### 24. ⚠ NEW 2026-07-16 — four findings from the corpus rung, none fixed, each named where it lives

**Found by extending the instrument (#4d). None is a corpus bug; all four are in code the rung did not own.**

- ⭐ **`SplitLiveRanges` is O(blocks × K²)**, K = simultaneously-live **owned** values in one block — and the
  two variables were separated by MEASUREMENT: K=8 with blocks doubling reads **x2.00**; K doubling with
  blocks fixed reads **x6.48 x6.07 x4.39 x4.03** → x4.00. **It is NOT a curve in program size.** A struct
  binding is *owned*, so it does not die at its last read — it sits on `ownedBindings` until the `return`,
  where `emitScopeDrops` (`Parser.maxon:2524`) decrefs every one. **K measured across all 2,445 functions in
  `maxon-shv2/` + `stdlib/`: max = 8, mean 0.16.** Σ_f O(K_f²) is therefore linear in program size, so this
  is **DEBT, not a bug**. ⚠ **The re-measure trigger is an INLINER** (inlining N callees each holding an
  owned local into one block is what makes K scale with program size) — **not P1.4 and not P1.7**, which the
  first draft claimed: P1.4 is bounded by a **compiler cap** (`Parser.maxon:50`, six parameters) and an array
  is **one** owned value. shv2 has no inliner and **no DCE** (`PassPipeline.maxon:135`).
- **`installMmRuntime` is billed to NO PHASE.** Called at `Compiler.maxon:180`, between `pipeline.run()` and
  `buildBackend`, **outside every `PhaseProbe`** ⇒ **phases do not sum to total.** Isolated: `unattributed` =
  **746 allocations at every rung of a 16× ladder** — exactly constant; no-heap 432 vs heap 748 ⇒ the
  **316-alloc delta IS `installMmRuntime`**. Constant in time too (0.18–0.25 ms, share *falling* 2.9% → 0.3%),
  so it is **not** the "dominant cost in the wrong bucket" class that has bitten this project four times — but
  it is the same shape, and `--metrics` already emits an `unattributed` row that `scale-test` does not surface.
- ⚠ **`mainSource` re-derives 13 of 15 driver names, and an omission is SILENT.** `writeManyFunctions`/
  `funcNames` exists *precisely* so the enumeration is not written twice — its own header says a second copy
  could *"skip one (an uncalled function a future DCE deletes, silently flattening the knob)"*. **That fix was
  applied to exactly ONE knob.** All 15 are correctly wired today and **there is no DCE**, so there is no live
  defect and no trigger — but the structural fix (each knob's emitter registers its own driver name) is a
  15-site restructure of the file's spine and wants its own commit.
- **`docs/error-codes.txt`'s E1006 doc text is WRONG.** It says a literal brace *"must be doubled"*; the lexer
  says `use '\{'`, the spec says `\{`, and it was **verified both ways: `{{` fails with E2004, `\{` works.**
  One fact written down twice, disagreeing — in the registry that exists to be the single copy.

> ⭐ **A PROCESS NOTE WORTH MORE THAN ANY OF THEM: the independent REVIEW caught the independent OPTIMIZER.**
> The optimizer priced the splitter's ceiling at *"K=65 ⇒ 1.28 s in `stdlib/Internals.maxon:1274`"*. The
> reviewer looked the citation up: line 1274 is `function __slab_free(ptr MachineWord)` — **nothing to do with
> live-value counts** — and no K=65 bound exists anywhere in the tree. **It refused to record the number**:
> *"replacing a wrong claim with an unverifiable one re-mints the exact defect I was sent to remove."*
> **That is why the review is independent and why it runs LAST.** A plausible number with a citation attached
> is exactly what #22 warns of, and it very nearly went into the log.

### ~~25. 🔴 shv2 SILENTLY ACCEPTS A PRIVATE FIELD READ, and the comment excusing it states a FALSE reason~~ ✅ **FIXED 2026-07-16 (P1.1a wave 3)** — E3014 now lives in `Parser.requireFieldAccessible`, one home / two callers (read + write), TYPE-scoped. **⚠ But the fix's OWN comment then asserted "the gate is the TYPE, not the FILE" while NOTHING checked it** — the whole corpus reaches the field from `main()`, so the type-vs-file half was unpinned and a sabotage kept the suite at 392/0. Closed for real by `field-visibility-is-type-scoped.md` (see the wave-3 box in PLAN.md). The registry `doc` line was ALSO wrong the same way ("from another file") and is corrected.

### 25. 🔴 NEW 2026-07-16 — **shv2 SILENTLY ACCEPTS A PRIVATE FIELD READ, and the comment excusing it states a FALSE reason**

**Found while planning P1.1a wave 2, by porting-surveying `specs/export-var-fields.md`. Measured both ways,
same file, same program:**

```maxon
type Value
	var private as Integer          // NOT exported
	static function create() returns Self
		return Value{private: 42}
	end 'create'
end 'Value'
function main() returns ExitCode
	let v = Value.create()
	return v.private                // ← outside the type
end 'main'
```

| | |
|---|---|
| **bootstrap** (the oracle) | `error E3014: cannot access unexported field: 'private' outside of type 'Value'` |
| **shv2** | compiles clean, **returns 42** |

**⇒ shv2 accepts a program the language rejects.** The `export` bit is read and dropped by
[`readStructFieldInto`](maxon-shv2/Compiler/Parser.maxon#L1471), and the comment above it
([`Parser.maxon:1469-1470`](maxon-shv2/Compiler/Parser.maxon#L1469)) justifies the drop like this:

> *"`export` is consumed and dropped for a different reason: field visibility gates cross-FILE access
> (E3014), and shv2 has no cross-file name resolution to gate."*

**⚠ THAT REASON IS FALSE, and the corpus proves it: field visibility gates access from outside the TYPE,
in the SAME FILE.** `error.unexported-field-read` / `error.unexported-field-write` are both single-file
fragments. So the dropped bit **has a live consumer today**, and its own comment is what denies it —
this file's signature disease, wearing the mask of the founding lesson it cites. *(The neighbouring
`let`/`var` drop at :1462-1467 is the honest twin: it names its consumer and the rung that supplies it.)*

**Not folded into P1.1a wave 2 — deliberately, and named not hidden.** Visibility is a third mechanism
(wave 2 is mutability + defaults), and **E3014 has no shv2 claim in the registry** (`notEmittedBy:
[shv2]`) so it needs a registry edit + `maxon error-codes generate`. The machinery is nearly all present:
`requireConstructible` (E3076) already computes *"am I inside type T's methods?"*, which is the same
question E3014 asks. **⇒ P1.1a wave 3**, whose port is `specs/export-var-fields.md` (2 error cases; 3 of
its remaining 6 need instance methods). The RED above is already captured — do not re-derive it.

### 26. ⚠ NEW 2026-07-16 — **FUNCTION OVERLOADING IS ON NO RUNG OF THE LADDER, and a marker blames the wrong one**

`specs-shv2/structs.md`'s `struct-field-default` is marked *"P1.1a wave 2 — field defaults"*. **It is not
blocked by field defaults.** It declares two `create` overloads (arity 0 and 2), and shv2 answers:

```
error E3006: duplicate definition of function 'Counter.create'
```

**Overloading appears NOWHERE**: zero hits across `PLAN.md`, `ARCHITECTURE.md`, this file, and
`maxon-shv2/Compiler/`. Yet the corpus needs it, and **PLAN.md:487 already leans on it** — *"`Subprocess.run(exe,
arguments:)` — never the env-map overload"* — i.e. the stdlib shv2 must eventually compile has overload sets.

**This is the `top-level let`/`var` shape again** (P1.0d.5a/5b: *"the plan never listed this one either… found
by probing, not by the ladder"*), and it is a **third** instance of the same discovery route: **a marker's
stated reason is not its real blocker, and nothing checks a marker against the compiler.** A `disabled-test:`
reason is prose — §"the disabled-test reasons ARE the ranked roadmap" makes 153 of them the roadmap, and
**nothing verifies that the rung named is the rung that unblocks it.** ⇒ The roadmap can be wrong in exactly
the way this file is about, and would go on looking right.

**Wave 2 corrects THIS marker's reason to name E3006/overloading. It does not implement overloading, and it
does not survey the other 152.** Where overloading belongs on the ladder is a **PLAN decision, not a rung's** —
it is orthogonal to structs (it is function resolution), and it needs a number.

### 27. 🔴 NEW 2026-07-16 — **`StdOpMeta.isPure` HAS ZERO READERS, and SIX places called it a live correctness constraint**

**Found during the P1.1a-wave-2 review, by SABOTAGE. It is the THIRD instance of #4c's shape — a gate
reporting PASS while structurally blind — after #4c itself and #23.**

`StdOp.loadIndirect` declares `isPure: false`. Six places said, in the present tense, that this
*prevents* a hoist and that a spec *pins* it. **Nothing reads the field.** Measured three ways, each
independent:

1. **A sweep of all NINE `StdOpMeta` fields** (`StdDialect.maxon:254-308`) for readers. Only **three**
   have one: `category` (`StdDialect.maxon:743`), `isCall` (`X64PrologueEpilogue.maxon:366`),
   `clobbersFlags` (`StdToX64Conversion.maxon:791`). **Six are read NOWHERE: `role`, `isPure`,
   `isUnsupportedInInlineBody`, `isMemory`, `isStore`, `isCmp`.** The sweep is COMPLETE rather than
   merely negative: `op.rawValue` is never bound to a local anywhere in the compiler, so
   `rawValue.<field>` is the only access route there is, and `.isPure` matches nothing but a COMMENT and
   `IrFunction.maxon:231`'s `isPure: src.isPure` — **a different struct's field** (`IrFunction.isPure`,
   for inlining, and *that* one is dormant too: written `true` twice, copied once, branched on never).
2. **The pipeline cannot hoist.** `buildDefaultPipeline` is `resolveTypes → semanticCheck →
   lowerMaxonToStd → pruneDeadBlockArgs → elimTrivialBlockArgs → foldConstOperands`. No LICM, no CSE, no
   DCE beyond `foldConstOperands`' const-DCE, no inliner.
3. ⭐ **THE SABOTAGE, which is the only one of the three that could have surprised us.** `loadIndirect`
   flipped to `isPure: true` — the exact edit all six sites call a silent-wrong-answer bug — and the
   full suite reads **371 passed / 0 failed, exit 0, memoryLeak false**, including
   `global-load-not-hoisted` (P1.0d.5b's three cases) and wave 2's own
   `struct-field-load-not-hoisted`.

**The property holds because NOTHING HOISTS, not because of the flag.** The two `*-load-not-hoisted`
specs are **STANDING GUARDS, not live gates** — they pin the ANSWER (a loop returns 10; a counter
returns 5), which is worth pinning against the day a hoister lands, and they cannot fail on the flag.

✅ **FIXED: the PROSE, at all seven sites** — `ARCHITECTURE.md`, `PLAN.md` (P1.0d.5b's row), this file's
P1.0d.5b entry, `Testing/ScaleCorpus.maxon`, `Compiler/IR/Std/FoldConstOperands.maxon`,
`specs-shv2/struct-field-load-not-hoisted.md`, and `StdDialect.maxon` itself (both the `isPure` field's
own note and `loadIndirect`'s). Each now says what is true: the declaration is CORRECT and awaits its
reader. Two sites were worse than merely stale and are now precise about the mechanism that IS live:
- `FoldConstOperands.stdConstInfo`'s `binOp to osAllocPages gives notConst` arm is **the only thing
  actually stopping a load from folding to its `.data` initializer**. It read as a mere echo of the
  purity flag; it is the real check, and it has a real reader.
- `ScaleCorpus`'s `globalVars` knob credited its fold-resistance to `isPure`. Same correction: the arm
  above is what makes a global read opaque.

❌ **NOT DELETED, deliberately, and #7 is the precedent that does NOT transfer.** `TargetOpMeta.setsFlags`
was deleted (`282d08421`) because it was a **SECOND home** for a fact already live at the Std tier as
`clobbersFlags` — *"two homes for one fact, one unreachable, and the dead one was quietly right"*, a v1
port artifact whose reader had MOVED. **`isPure` is the SOLE declaration of each op's purity and its
reader is SCHEDULED, not relocated.** Deleting it destroys the correct answer for ~20 ops and forces
re-derivation — per-variant, which is the flat union's whole payoff over v1's by-category blanket — on
the day the inliner lands. Keep it.

⚠ **The real cost is that these values are now held by REVIEW ALONE, and that is not written down
anywhere a reviewer must look.** No spec can fail on them; a wrong one is invisible until the first
hoisting pass makes it a silent wrong answer in every program at once. The mitigation shipped here is
the field's own note (`StdDialect.maxon`) saying so at the point of edit.

⇒ **NOT ANSWERED HERE, and it needs a rung: what to do with the five other unread fields.** `role`,
`isUnsupportedInInlineBody`, `isMemory`, `isStore`, `isCmp` are in the identical position, and the
keep-vs-delete answer is **not uniform** — `isMemory`/`isStore` are the scheduler's barrier facts and
`isCmp` is the backends' compare/branch pairing, each with a different future reader and a different
re-derivation cost. It is a **survey with a decision per field**, it is orthogonal to structs, and it
needs a number. #24's precedent applies: **a rung named without a measurement is a rung that cannot
move the number**, so none is named here.

### 28. ⚠ NEW 2026-07-16 — **the corpus generates structs with a HARDCODED field count, so the one struct dimension with a superlinear term is invisible**

**Found by the P1.1a-wave-2 optimizer, confirmed by this review. It is the corpus-blindness class again
(#4d, #7, #23) — but a FINER form, and the finer form is the interesting part: #4d was closed by teaching
the corpus to generate structs, and the corpus it produced LOOKS like it covers them.**

`Testing/ScaleCorpus.maxon:1228` (`structTypeDecl`) emits **exactly two fields — `x` and `y` — on every
generated type, at every rung, in both struct knobs.** Its own comment states the design: the two knobs
measure *"how many of them exist, and how many times one is built"*. **Field COUNT is not a dimension of
either.** So F = 2 on every measurement this instrument has ever taken.

**That is precisely where the superlinear term is.** `StructLayout.indexOfField` (`Project.maxon`) is a
LINEAR SCAN, and P1.1a wave 2 made it hotter: every field read, every field write and every `Self{…}`
label now asks it, so a literal filling all F fields of a type costs **O(F²)**. The ladder doubles the
NUMBER of types, which grows the O(F²) term by a constant factor of 4 per type and never bends the curve.
⇒ **A quadratic in F cannot be seen by an instrument that holds F at 2.**

✅ **The optimizer did NOT touch the instrument, which is correct** (CLAUDE.md: never edit the instrument
to move a number), and it did not call the scan a defect either. It **measured** instead, and the debt
judgment is sound and is recorded in `docs/optimization-log.md`:
- across all **166** `type` declarations in `maxon-shv2/` + `stdlib/`: F max **22**, mean **4.14**;
- the P1.0r row independently measured v1's **369** types at mean **5.4**, median **3** — and v1 is 3.5×
  larger than shv2 while its median F is LOWER.
⇒ **Programs grow by adding TYPES, not by widening them.** A per-layout name→index hash map would buy a
heap Map per declared type plus a name hash per lookup to save ~4 byte-compares. **DEBT, not a bug.**

⚠ **What is worth recording is the BLIND SPOT, not the scan.** The re-measure trigger is a
**machine-GENERATED struct** — a wide type no human writes — and the corpus is the one thing that could
produce one. Today it cannot: `structTypeDecl` would need a field-count knob. No rung is named, per #24's
precedent (**a rung named without a measurement is a rung that cannot move the number** — #24's first
draft named two that could not).

⇒ **The general lesson, and it outranks the specific one: closing a corpus gap by generating a construct
does NOT close it by DIMENSION.** #4d was closed by making structs appear. They appear at one width, one
shape, one field count — and "the corpus now covers structs" reads as though it covers them. **A grep for
a construct is not a measurement of it** (project_scale_corpus_blind_sixth's own words), and neither is a
generator that emits it at a single point in its parameter space.

### 22. ⚠⚠ `bin/maxon.exe` IS GITIGNORED AND NOTHING REBUILDS IT — **a baseline can measure a tree that does not exist**

**Found 2026-07-15, and it nearly bought a fabricated entry in the optimization log.** The rung skill's
step 1 says *"never start from a claimed-green tree — build and run it yourself."* **I did, and it was still
not `main`:** `maxon build maxon-shv2` drives **`./bin/maxon.exe`**, which is **gitignored**, so it is
whatever some earlier session last built. It predated commits already on `main`.

The symptom was a **276 KB swing that looked exactly like my own change**: after I edited the bootstrap's
match lowering, shv2's binary went **3,152,748 → 2,876,696 bytes of code** (−8.8%) and **6,552 → 61,856
bytes of data** (+55 KB ≈ 6,900 eight-byte slots — *the signature of jump tables forming*). Every instinct
said "your range arms became equality chains and the jump-table pass ate them."

**All of it was false.** Two measurements killed it:
- **`dump_ir` on a dense enum**: `add to divide` still compiled to the intended **2-instruction range
  compare** `[0,3]` (`setge`/`setle`). No OR-chain, no jump table. The change was a **no-op** for the shape
  shv2's source actually uses.
- **Stash the change, rebuild the bootstrap, rebuild shv2**: **2,876,696 bytes — byte-identical.** The swing
  was `bin/maxon.exe` being stale, and my `build csharp` merely *refreshing* it.

⇒ **Two lessons, and the second is the sharp one:**
1. **A binary that nothing rebuilds is a THIRD copy of the source.** This is the same disease as the MCP
   server's binary (CLAUDE.md has a whole box on it) and the error-code enums — **ONE FACT WRITTEN DOWN
   TWICE.** The MCP box exists because *"a tool that answers confidently from stale code is worse than one
   that refuses."* `bin/maxon.exe` has **no such guard**, and it is the compiler every gate runs through.
   ⇒ **`build csharp` FIRST, before any baseline you intend to trust.**
2. ⭐ **Attribution is not a story that fits — it is a measurement.** The jump-table explanation was
   coherent, mechanically plausible, and matched the numbers in both binaries. **It was still wrong.**
   `docs/optimization-log.md` exists to record *why* a number moved, at the one moment it is still known;
   **the instrument can see WHAT moved and can never see WHY**, and a confident narrative is exactly what
   that gap fills itself with if you let it.

## 📋 Environment / process notes that cost real time

- 🟡 **IN A WORKTREE, PASS `repoRoot` TO EVERY `maxon-dev` MCP TOOL — otherwise it drives the MAIN REPO.**
  The tools default to the checkout holding the **MCP server's own binary** (resolved from
  `Process.executablePath()`), because they cannot do otherwise: **one stdio server process is shared by
  every agent in every worktree, and its cwd is the MCP host's, not yours.** All nine tools now take
  `repoRoot` — the **absolute** path of your worktree's root — and that is the whole fix.
  **Two things now make a mistake VISIBLE instead of silent:** every result **echoes the root it actually
  used** (successes in `repoRoot`, failures in `error.data.repoRoot`) — *read it back*; and a `repoRoot`
  that is not a checkout is **refused** (`invalidParams`), never quietly swapped for the main repo.
  A checkout is any tree with `stdlib/` and `maxon-sharp/`, so **a brand-new worktree qualifies before
  anything is built in it** — `build target=csharp repoRoot=<your worktree>` is the correct first call.
  ⚠ Still true, and still worth fearing, about **whatever tree you point them at**: `updateRequired`
  rewrites *that* tree's committed goldens, `run_scale_test note:` writes a row into *that* tree's
  optimization log, and `fmt` rewrites *that* tree's files in place. They do not merely report — they
  **edit**.
  *(Caught 2026-07-14 with **five agents running against it**, by an agent whose `build` succeeded on a
  tree with none of its work in it. `.claude/CLAUDE.md` said "PREFER THE MCP TOOLS" while the rung
  workflow said "work in a worktree", and the two silently contradicted each other — **the project's own
  signature bug, one fact written down twice, at the TOOLING level.** Documented `13855215b`, fixed the
  same day: `repoRoot` is that fact, written down once, by the only party who knows it.)*
- ⚠ **NEW 2026-07-15 — THE MCP STALENESS GUARD HAS A HOLE THE SHAPE OF THE RUNNING PROCESS.** The guard
  compares the **binary's** mtime against **its sources** and refuses if a source is newer. It cannot see
  the **running process**, so *rebuild-without-restart* — the one failure `.claude/CLAUDE.md` explicitly
  warns about (*"a rebuild alone does not replace the running process"*) — **sails straight through it**:
  no source is newer than the binary, so nothing refuses, and a **stale server keeps serving stale tool
  schemas**. Measured this session: `build` returned `success: true` and echoed **no `repoRoot`**, and
  `ToolSearch` showed `build` with **no `repoRoot` parameter** — while the on-disk binary contained it and
  268 source references existed. It reads exactly like "the feature was never built." **It was; the process
  predated it.** ⇒ **If a tool's schema or result disagrees with its source, suspect the PROCESS before the
  code**: compare the server's `StartTime` against the binary's mtime (`Get-Process maxon-dev-mcp`), and
  restart it. The guard is worth extending to refuse when the **binary is newer than the process's start
  time** — that is the same fact the guard already believes it is checking.
- **A FAILED BUILD LEAVES THE OLD BINARY.** `spec-test` then runs the *previous* compiler and reports a
  green suite. **Check the build's exit code before believing a test result.** Three false greens this
  session.
- **A copied `bin/maxon.exe` is FROZEN.** `bin/` is gitignored, so a worktree starts without a compiler
  and one gets copied in — but the bootstrap *compiles* shv2, so a stale copy silently reverts every
  bootstrap change on the branch. One such copy made a rung look like a **25% allocation regression that
  did not exist**. If the branch touches `maxon-sharp/`, `dotnet build maxon-sharp` **in the worktree**.
- **A FILTERED C# `spec-test` always dirties fragments** — the runner batches compiles in one process
  with a process-static id counter, so a filtered run registers a different type set and lands different
  ids. **Only a FULL run's `git status specs/` means anything.**
- **`./bin/maxon.exe fmt` with arguments reformats the ENTIRE TREE in place.** Two agents destroyed
  unrelated files this way.
- **The bash `pkill -f maxon-dev-mcp` does not match Windows processes.** The MCP exe stays locked
  (`E9001: the process cannot access the file`) and the build silently keeps failing. Use PowerShell
  `Stop-Process`. *(`buildall.sh` has this same hole.)*
- ⚠ **The C# network specs (`async-tcp`, `tcp-client`) currently TIME OUT (~12 s) on this machine** —
  2893/2 rather than 2895/0. **Environmental, not a regression:** `398c89488`, which measured 2895/0
  earlier the same day, fails identically. A fresh box should restore it.

### 29. ⚠ NEW 2026-07-16 — **two receiver-ownership facts from P1.1a wave 3, both LEFT for the ownership rung, both stated where load-bearing**

Both surfaced in the wave-3 independent review, both FIXED-or-INERT now, recorded so the ownership rung
(P1.4) does not rediscover them at 5× cost.

**(a) `next.a` compiled to `self.a` — the fixed one, kept here for its SHAPE.** A self-field alias has
`VarInfo.boundValue == 0` (a read is a `loadIndirect` through the receiver, not a value reference) **and
ValueId 0 IS the receiver** (`__self` is param 0). `parseVariableReference` guarded exactly this for the
bare-name path; three OTHER sites (`parseFieldAccess`, `parseMethodCall`, `parseFieldAssignment`) each
re-read `binding.boundValue` themselves. **One fact, four homes** — reachable via a self-referential `var
next as Node` (`parseTypeReference` mints a `structRef` for the enclosing type's own name, so the struct
gate admits box = 0). `return next.a` emitted `[rcx+0]` = `return self.a`; `next.readA()` emitted a bare
`callDirect` with no arg setup = `self.readA()`; both exit 0, no diagnostic. **Fixed by DERIVE, not a
fourth guard:** `requireStructBase` returns the box WITH the layout (`type StructBase`), so it is the one
reader of a base's `boundValue` and no caller can hold a proved layout beside an assumed box. A
struct-typed field base is REFUSED (E2015) rather than loaded — reading it correctly is the `mintPhi` trap
(no struct-typed field can be constructed here to observe the load). Pinned: `self-field-struct-typed.md`.

**(b) The receiver-borrow REASON was false; the CONCLUSION holds for a different reason.** `parseMethodCall`
claimed the consume sink is unreachable "because this rung has scalar and float fields only." **Measured
false:** `function link(other Node) → next = other` lowers to `storeBaseDispReg [rcx+8], rdx` — a struct
pointer into heap storage, **no incref**. The conclusion (receiver borrowed, no leak) survives, but only
because **bare `self`-as-a-value is refused** (E2015, naming the P1.4 deferral) and **a struct-typed field
is uninstantiable** (`next: 0` → E3005, `next` omitted → E3086, no `Node` value obtainable) — so no live
struct pointer ever reaches a field. That is #27's shape (a property holding for a reason other than the
one written). The un-increfed store is **left for P1.4**: fixing it needs the full consume analysis, not a
rung's parser work. Both the false reason and the real one are now written where they are load-bearing.

### 30. ⚠ NEW 2026-07-16 — **`scale-test` is BLIND to the instance-method mechanism P1.1a wave 3 just landed**

The wave-3 optimizer measured **zero** allocation attributable to the instance-method path — the receiver,
the self-field aliases, the method-call lowering — because **the scale corpus emits no methods**. Every
allocation in its +148→+3,496 delta is the field-DECLARATION path (which the corpus does generate). So the
mechanism this rung exists to add is bounded by *reading the code*, not by the instrument.

**This is #4d's shape exactly** (the scale corpus systematically blind to the feature just landed), which
was CLOSED for structs/heap/globals/idiv by adding generator knobs (`74ea57d1f`, `726a606f1`). **Methods
are the knob nobody added.** The optimizer correctly did NOT touch the instrument to paper over it (that is
the cardinal sin — see #23's "an agent edited the instrument to stop it complaining"). ⇒ A corpus knob that
emits a type with an instance method and calls it, so the next rung's method work is measurable rather than
argued. Not urgent — the method path is O(1)/O(fields) by inspection — but it is the same recurring gap and
it should be closed the same way, once, deliberately. **P1.1b Wave A's optimizer found the corpus is ALSO
`match`-blind** (no knob emits `match`/`then`/`gives`) — the same gap, third instance. Fold a `match` knob
in with the method one.

### 31. ⚠ NEW 2026-07-16 (P1.1b Wave A) — **a cross-register-class match EXPRESSION is refused (E2015); the float rung must PROMOTE it instead**

A match expression whose `gives` arms cross register files — an integer give and a float give feeding one
result phi — **panicked the backend** (`X64Backend.emitRegRegMove:603`, "crosses register files") until Wave A
guarded it. It is now refused with a positioned **E2015** (`Parser.finalizeMatchMerge`, pinned by
`specs-shv2/match-expr-divergent-class.md`, both directions). **That is a deliberate DIVERGENCE from the
oracle**, which UNIFIES such arms: it promotes the integer arms to float (`cvtsi2sd`) so the result is
uniformly float, then the surrounding context decides (returning that float as `ExitCode` is a separate
E3009).

**Why refuse rather than promote now:** shv2 HAS the instruction (`promoteToFloat`, `Parser.maxon:4885`) and
the lattice (`TypeRules.arithResultTag`), so the promotion is not blocked on missing machinery. It is blocked
on **there being nothing to hold the result**: a promoted match yields a float VALUE, and a float has no
nameable type in shv2 yet — `typealias F = float(…)` is itself E2015 ("a typealias over 'float' … arrives with
the milestones that give them meaning"). So a float-result match could not be bound, returned, or passed. The
promotion has **no consumer** — the `mintPhi` trap this project keeps re-learning. ⇒ **It arrives with the
float type-system rung** (the one that makes `float` nameable). At that point, replace the E2015 refusal in
`finalizeMatchMerge` with: unify the arms' result type through `arithResultTag` (any arm float ⇒ float), and
promote each non-float arm's give value **in its own exit block, before its branch to merge** (the fiddly part
— the exit blocks are terminated by the branch-to-merge loop, so the promote must be emitted before that loop
runs, not at edge-recording time). Same-class arms (int+bool) need none of this and already match the oracle
exactly (E3005 on a bool→int return, byte-for-byte).

### 32. ⚠ NEW 2026-07-16 (P1.1b Wave A) — **the E2027 duplicate-pattern check is O(P²) in one match's arm count — DEBT, kept deliberately**

`Parser.checkAndRecordDuplicate` linear-scans a `seenValues` list per known-const single-value pattern, so a
single match with P integer-literal arms costs O(P²). **Kept, and the optimizer + review both agreed:** P is a
single match's HAND-WRITTEN arm count (never bulk-generated), so whole-program cost is O(Σ Pₘ²) = linear for
bounded arm counts, quadratic only in a degenerate single-giant-match. It matches the project's own precedent
for parameter-duplicate detection (`byteArrayContains`, `Parser.maxon:558` — *"parameter lists are tiny… no
membership set is worth its heap object"*); a `Set` would allocate a heap object per match to save a bounded
scan, contradicting that precedent and adding a parse-phase allocation the instrument tracks. **Same class as
`indexOfField`'s O(F²) (#28)** — real superlinear term, invisible dimension, correctly left. Revisit only if a
real program ever writes a match with hundreds of literal arms (none does).

### 33. ⚠ NEW 2026-07-16 (P1.1b Wave B) — **a NEGATIVE float-backed enum raw value rejects with a MISLEADING E2010 where the bootstrap accepts**

`enum T { a = -1.0  b = 0.0  c = 1.0 }` — shv2 rejects at the declaration with **E2010 "Expected integer
literal but got float literal"**; the bootstrap accepts and runs. A real divergence, but a **CLEAN reject —
no crash, no wrong answer**, and no corpus case exercises it. The cause: `= -x.y` takes the minus→int parse
path and fails `consume(intLiteral)` (POSITIVE float tags like `a = 1.1` parse fine — the limitation is
*negative* floats specifically, via that one parse path).

**The message is the actual wart:** it says "integer literal" when positive floats ARE accepted, so it
misrepresents the limitation. **⭐ The implementer's stated reason for deferring was ALSO wrong and the review
corrected it:** it worried negative float tags would "misorder as signed i64" in a range — but the range
logic is set-membership over signed i64 (compile-time min/max and runtime signed compare AGREE on
signedness), so negatives would be handled CORRECTLY *if they parsed*. ⇒ Follow-up: either support them
(correct under the signed logic already in place) or emit a clear `unsupported` message naming the real gap —
needs its own spec + oracle cross-check. Left as a bounded clean reject, not a blocker.

### 34. ⚠ NEW 2026-07-16 (P1.1b Wave B) — **union `E2026`/`E2046` wording: shv2 says "union" consistently; the bootstrap hardcodes "enum"; nothing pins either**

After the review's E3034 fix (which threaded the enum/union noun through `EnumLayout.kindWord()` — see the
Wave B box), shv2 says **"union"** across all three match diagnostics (E3034 unknown-case, E2026
non-exhaustive, E2046 default-must-throw) for a union scrutinee. **The bootstrap hardcodes `{"enum"}` in E2026
and E2046** (`2-Parser.cs:13684/13960`) — an apparent oversight, since its E3034 IS conditional on `IsUnion`.
**No corpus golden pins union E2026/E2046 wording** (the corpus's union cases are payload unions, deferred to
P1.3). So shv2 is MORE internally consistent than the oracle, and nothing tests it either way. Left as shv2's
consistent behavior (documented in `Queries.maxon`). ⇒ **Coordinator's call when P1.3's union cases land**:
author union E2026/E2046 spec tests pinning shv2's "union", or decide to reproduce the bootstrap's "enum".
Not a blocker — an unpinned wording detail on a diagnostic no case currently reaches.

### ~~35. 🔴 a USER `__`-prefixed function SILENTLY MISCOMPILES; shv2 needs E2051~~ ✅ **FIXED 2026-07-16** (`f3550590e`, `6b739ebc3`, `b697c411c`)
**E2051 now emitted by shv2 at 9 declaration sites** (function/method name, parameter, `let`, `var`, `type`,
type-field, `typealias`, enum/union CASE, **enum/union NAME**) via ONE `Parser.requireUnreservedName` helper.
The `__` compiler-internal namespace is now ENFORCED, so `isCompilerInternalCallee`'s `__`-prefix predicate is
**provably sound** (no user `__` name can exist). `__add(7)` ⇒ E2051 (was exit 65543); `print("hi")` still
builds (runtime `__` fns are builder-built, never parsed). Ported `specs/reserved-double-underscore.md` (8
cases) + 2 coordinator-authored cases (`enum-name`, `union-name` — the implementer flagged the enum/union NAME
gap rather than shipping an untested guard; the coordinator closed it). `closure-parameter` deferred → P1.5.
`specs-shv2` 523→533. **One residual (minor): `b"__"` is 3 cross-referenced copies** (`Parser.ReservedNamePrefix`,
`MmRuntime.CompilerInternalPrefix`, `TargetPrinter.isRuntimeFunction`) — the same namespace marker at 3 tiers;
a single shared language-level constant is a worthwhile small cleanup, deferred (spans TargetPrinter).

### 35. ~~🔴 (P1.2 Wave A) — a USER `__`-prefixed function SILENTLY MISCOMPILES; shv2 needs E2051.~~ ✅ FIXED 2026-07-16 (`abb6bde17`, 523→533)

**✅ CLOSED:** E2051 now emitted at all reserved-`__` declaration sites via one `requireUnreservedName` helper; `specs-shv2/reserved-double-underscore.md` ports the 9 bootstrap cases + 2 coordinator-authored (enum/union name). The `__` predicate is now SOUND (no user `__` name can exist). Original finding below, kept for the lesson.

**Found by the INDEPENDENT review** (the implementer's self-review missed it — the fifth time this rung the
independent pass caught what the self-review did not). Wave A widened `MmRuntime.isCompilerInternalCallee`
from `__mm_`-only to **any `__` prefix** — NECESSARY, because the new runtime callees `__print_string`/
`__str_eq` have no signature to slot, so `SemanticCheck.validateCall` and `LowerMaxonToStd.lowerCall` must
skip the user-function checks for them. **But shv2 has NEVER emitted E2051 (`ParserReservedIdentifier`)** — it
is `notEmittedBy: [shv2]` in the registry — so the `__` namespace is NOT enforced, and the widened predicate
now routes a USER `__`-prefixed call past validation:

| program | shv2 | bootstrap (oracle) |
|---|---|---|
| `function __add(a,b)` + `return __add(7)` (missing arg) | 🔴 **compiles, runs, exit 65543** (2nd arg uninit) | `E2051: identifier '__add' is reserved` |
| `return __foo()` (undefined `__` name) | 🔴 **backend panic** (`no Std function named '__foo'`) | rejects cleanly |

**⚠ It is a REGRESSION this rung introduced:** before the widening, `__add` got normal arity validation (a
clean error); after, it is skipped. **But it affects NO in-language program** — `__` is the reserved
compiler-internal namespace, no valid program declares one, and no corpus case exercises it — which is why it
is DEFERRED, not a Wave-A blocker.

**Why not fixed in Wave A (both surgical routes fail):** (a) narrowing the predicate to an explicit runtime-fn
list needs `PrintStringName`/`StrEqName`, which live in `StringRuntime` — and `StringRuntime` already depends
on `MmRuntime`, so referencing them back from `MmRuntime.isCompilerInternalCallee` is a **circular
dependency**; (b) "is it a user-declared function?" threading fixes the miscompile but not the undefined-`__`
panic. **The clean fix is E2051 itself** — it keeps the `__` prefix predicate and makes it SOUND (the DERIVE
tier: no user `__` name can exist, so skipping validation for a `__` call is provably safe). That is a
**6-declaration-site feature** (function name, parameter, `let`, `var`, `type`, type-field — per the existing
bootstrap spec `specs/reserved-double-underscore.md`, 9 cases) + the shv2 registry claim + a red-before-green
port. ⇒ **DO THIS FIRST, before Wave B.** The review already corrected `MmRuntime.maxon:95-107`'s comment,
which had asserted the false invariant *"no user function can begin with `__`"* — so the code no longer LIES,
but the hole is open until E2051 lands.

### 36. ~~🔴 (P1.2 Wave B) — a valid program PANICS the backend when a fused compare is the function's highest ValueId~~ ✅ FIXED 2026-07-16 (`837b948d2`)

**Coordinator-found while reviewing the implementer's own workaround** — it is the signature bug (ONE FACT WRITTEN TWICE) at the backend. The value-class column was sized by `scanFunctionValueCount`, which counts the highest ValueId used as a target **OPERAND** (correct for the allocator — it only ever indexes live/register values). But `recordOpResultClass` writes a class for every value an op **DEFINES**, and a fused loop-test `cmp` defines its boolean yet is consumed only as the EFLAGS a `jcc` reads — a def that is never an operand, and invisible among the target ops entirely. When that boolean is the function's highest id the column comes up one short and the class write runs off the end:

| program | before | after |
|---|---|---|
| `if a < b 'l' return c end 'l' return a` (both branches return an already-bound value ⇒ nothing minted after the compare ⇒ the compare IS the top id) | 🔴 `panic StdToX64Conversion:344: v3 outside the 3-wide class column` | ✅ compiles + runs |

Wave B only DODGED this for `__int_to_string` (a header-guarded write loop kept the compare off the top id — "avoids it by luck"); **any user function of this shape crashed.** Fixed at the source: `setValueClass` grows the column with `growFilled` (amortized-linear) to cover every defined id; the `scanFunctionValueCount` pre-size stays as the allocator-coverage lower bound (so `assertClassColumnCovers`, a `>=` check, still holds). No second copy of "which ops define values" — `recordOpResultClass` remains the sole authority. Regression `comparison-operators/fused-compare-is-highest-value` (proven RED: `v2 outside the 2-wide column` with the grow disabled). The StringRuntime write-loop comment was corrected — the header-guard is now style/consistency, no longer load-bearing.

### 37. ~~🔴 (P1.2 Wave B) — returning an OWNED interpolation String across a call LEAKS (exit 101).~~ ✅ FIXED 2026-07-16 (`bbcf46e32`+`deac21315`)

**✅ CLOSED — the user ruled "leaks are not ok", so the interim-reject option was dropped and the FULL static-ownership convention was implemented** (not deferred to P1.4). A `returns String` function hands back a uniformly OWNED heap String; the caller owns the result (`mintOwnedCallResult` → `trackOwnedTemp`), the callee MOVES an owned return out (`removeFromOwnedBindings`/`removeFromPendingTemps`, restored after the return's own drop so sibling exit edges still see it — the review's `deac21315` fix for a returned-loop-local leak the first cut missed), and a borrowed literal/param return is heap-PROMOTED to owned (`promoteToOwnedString`). String params ARE reachable and take the promotion path. See [[project-p12-string-ownership]]. Original finding below.

**Found by the INDEPENDENT review by PROBING** (no enabled test returns an owned String, so no gate catches it — the review wrote the program). CONFIRMED:

| program | shv2 | why |
|---|---|---|
| `function build(x) returns String  return "val {x}"` + `let s = build(5); print(s)` | prints `val 5` then **exit 101** (leak) | callee correctly does NOT drop the returned owned String (a move-out), but the caller cannot recover ownership |
| `function greet() returns String  return "hello"` (a LITERAL) | **exit 0**, clean | a borrowed immortal-rdata literal is never owned, never dropped |

**Root cause:** a String's `ValueTypeTag.string` conflates owned-heap with borrowed-immortal, and the new `valueOwnsHeap` provenance column is **per-function**, so a returned owned String reads `false` in the caller and is never freed. Structs already recover cross-function ownership via `tagIsStructRef` (`let p = Point.create(...)` → exit 0, verified); String needs the same at the call boundary. **This is the caller-side half of borrow-vs-consume, DEFERRED to P1.4** (same class as the already-disabled `structs/struct-return` through-binding case, which `parseReturnStatement`'s own comment flags) — a proper fix needs a return-ownership convention + heap-promotion of literal returns + its own spec coverage. **⚠ It is NOT silent — the runtime leak checker exits 101** — so the core "no silent leak" invariant holds, but it is a real reachable leak this wave INTRODUCED (before Wave B there was no owned heap String to leak). **P1.4 must rule: reject `return <owned String>` cleanly in the interim, or implement the convention.** Lower-severity sibling, also P1.4/Wave C: `var s = "{y}"` reassignment currently exits 0 with correct output only because the bump allocator masks a use-after-free (the reassigned temp drops at statement end; the original at scope exit; counts balance by luck) — already tracked as the disabled `string-type-2/reassigned-var-equality-not-const-folded` (tagged "P1.2 wave C"). ⇒ NOW #39.

### 38. ~~🔴 (P1.2 Wave B) — an owned binding in an `if`/`while` body that control FALLS THROUGH is never dropped (leak).~~ ✅ FIXED 2026-07-16 (`3ec65cbe5`+`deac21315`)

**✅ CLOSED — the Wave B "fail-safe leak" deferral is retired** (`closeBlock`'s own comment documented it: it drained nested-block owned bindings off the list WITHOUT emitting `__mm_decref`, because no committed spec bound an owned value in a block and a wrong drop is a use-after-free). The user ruled "leaks are not ok"; found by coordinator probing (`while … let s = build(i) … end` → exit 101 per iteration). Owned bindings now drop on **every edge that leaves a block ALIVE, exactly once**: fall-through (`parseBlockBody`, guarded on `not bodyEnd.isTerminated()`), `break`/`continue` (drop down to the enclosing loop's `LoopContext.ownedFloor` via `dropLoopBodyOwned`), while `return` keeps its whole-scope drop (`emitScopeDrops` gained a `floor` param so the reverse-order decref loop lives in ONE place). Struct-in-block (the exact case the comment named) now frees too. **The review found and fixed a leak the first cut missed** (`deac21315`): a returned loop-local binding was permanently stripped from `ownedBindings` by the return move-out, so the sibling non-returning iterations' fall-through drop dropped nothing — the signature "one fact, two readers" bug. Verified across break/continue/nested/labelled/if-else/struct probes. See [[project-p12-string-ownership]].

### 39. ~~🔴 (P1.2) — reassigning a `var` owned binding does NOT drop the old box (masked leak).~~ ✅ FIXED 2026-07-16 (`4a2ae34f1`+`66754ea88`) for the COMMON case; deeper aliasing/depth cases split to #40/#41

**✅ CLOSED for function-scope drop-on-reassign**: `parseAssignment`→`reassignBindingValue` drops the overwritten value (keyed on `ownedBindings` MEMBERSHIP, not the value bit — a loop-carried `var` is a phi with no provenance bit); owned→owned moves, owned→borrowed PROMOTES the RHS to an owned copy (removing a middle `ownedBindings` entry would desync the block/loop drop marks AND trip a latent bootstrap ref-array codegen bug), borrowed→owned enrolls, self-assign guarded. Review deduped the `newIsOwned` predicate (a byte-copy of `parseBinding`'s) into `valueIsOwned`. 587/0. **Two DEEPER cases the review surfaced are NOT covered — both P1.4 borrow-vs-consume ⇒ #40, #41.** Original finding below.

`var s = build(1); s = build(2); s = build(3); print(s)` → **exit 0** (`v3`), and the loop-carried `var s = build(0); while … s = build(i) …` → **exit 0** too. Both LEAK the overwritten boxes, but the leak-check counter is not tripped: `parseAssignment` (`Parser.maxon`) rebinds via `scope.setValue` without dropping the previous owned value OR re-tracking the new one, so the leaked old box and the stale scope-exit drop of the reassigned value **cancel in the live count**, and the bump allocator (reclaims nothing) hides the use-after-decref. **Found by the INDEPENDENT review by probing.**

### 40. 🔴 (P1.2 → its own DEPTH-MODEL rung) — **borrowed→owned reassignment INSIDE a nested block is a use-after-free (MASKED, exit 0). The height-stack `ownedBindings` model cannot represent a binding that becomes owned SHALLOWER than the current block.**

**⚠ Investigated 2026-07-17 + user-DEFERRED (chose Wave D):** the clean fix is a **height-stack → per-scope-frame rework** of the drop model — the enrollment (`reassignBindingValue`'s borrowed→owned `ownedBindings.push` at `Parser.maxon:3480`) lands at the current block height, and `closeBlock` drains+drops everything above the block mark, so the append-only stack CANNOT enroll a binding at its declaration depth; a hybrid (stack + a VarInfo flag) splits ownership across two places (the signature bug), and rejecting the common `var s=""; if c s=build() end; use s` pattern is bad UX. **STAYS MASKED under the bump allocator** (v1's arena never recycles; Wave D growth still uses bump `__mm_alloc`), so deferring it is SAFE — it only becomes a real UAF once `__mm_free` recycles. Its own focused rung, best done near the recycling allocator. Original finding below.

`var s = ""; if c 'b' s = build(1) end 'b'; print(s)` → **exit 0 today** but the emitted x64 decrefs the `build(1)` box at block `b`'s `end` and then `print` reads the freed pointer in the continuation — a USE-AFTER-FREE. Cause: `reassignBindingValue`'s borrowed→owned `ownedBindings.push(binding)` lands ABOVE the block's `BlockMark.ownedCount`, so `parseBlockBody`'s fall-through drop treats the FUNCTION-scope binding as block-local and frees it at the block end. Masked (bump allocator; net count 0). **Found by the INDEPENDENT review.** ⚠ **Becomes a WRONG ANSWER the moment `__mm_free` gets a free list.** Structural: the height-stack model can't mark a binding owned at a depth shallower than the current block. Fix = rework the depth model OR reject the nested borrowed→owned case — **needs its own spec + is P1.4 borrow-vs-consume**. TIME-SENSITIVE (before the free list).

### 41. ~~🔴 (P1.2) — `s = t` between two owned bindings is a DOUBLE-FREE. Owned aliasing — a MOVE.~~ ✅ FIXED 2026-07-16 by Wave C (`6c06e698c`..`75bd75fac`)

**✅ CLOSED by P1.2 WAVE C (static single-owner MOVES + use-after-move):** `let u = t` / `s = t` from a bare owned binding now MOVES ownership — the source is poisoned (`VarInfo.movedFrom`), drop-skipped so the value frees ONCE via its new owner, and a later READ of the moved source is **E3102 use-after-move** (shv2's FIRST ownership program-rejection). Reassigning a moved var REVIVES it. Conditional moves poison conservatively past the merge (sound; the precise dataflow join deferred). `595→597`, moves specs AUTHORED (the bootstrap refcounts, so it's not the oracle here — deliberate divergence). See [[project-p12-wave-c-moves]]. Original finding below.

`var s = build(1); var t = build(2); s = t; print(s)` → **exit 101** (both `s` and `t` end in `ownedBindings` pointing at `build(2)`; scope exit decrefs it twice). The drop-on-reassign fix (#39) SURFACED this pre-existing double-free — the base parser hid it because it ALSO leaked `build(1)` and the two cancelled (a false clean). **Not a regression — the gate now catches a real double-free.** This is the second-owner/ALIASING case `MmRuntime.__mm_incref`'s comment explicitly defers to **P1.4**: under static single-owner, `s = t` is a **MOVE** (t transfers to s, `t` becomes use-after-move) — which needs the move + use-after-move machinery. The `s = s` self-assign guard does not help `s = t`. Interim option: REJECT owned aliasing with a use-after-move diagnostic until P1.4 implements moves. ⚠ **Manifests on `main` (rare pattern, not in the suite) — a loud double-free.**

### 42. ~~🔴 (P1.2 Wave C follow-up) — use-after-move enforced ONLY at bare reads, not field access / method / STORE.~~ ✅ FIXED 2026-07-16 (`51f543fdd`, 597→602)

**✅ CLOSED:** one shared guard `requireBindingLive(binding, tok)` (throws E3102 on a moved-from base, excludes `isSelfField`) at the TWO funnels every binding-use resolves through — `parseVariableReference` (bare reads, call args, `==`, interpolation, move-source) and `requireStructBase` (the SINGLE base resolver for `p.x` read, `p.x = …` store, `p.m()` receiver, and chained `p.a.b`). A field store is a USE (E3102), never a revive; only a full reassignment `p = <expr>` revives (verified: `p=create(3); p.x=5` lands in the NEW box, exit 5). The INDEPENDENT review enumerated every use-site + SABOTAGED the guard (removing it returned the exact exit-99 #42 bug) + confirmed no false rejection (602/0). ONE use-rejection reader of `movedFrom` — the consolidation the disease demanded. Original finding below.

Wave C's E3102 check lives at `parseVariableReference` (the bare-identifier read). It is ABSENT from `parseFieldAccess`, the method-call receiver, and the field-STORE receiver. Found by the INDEPENDENT review (probing):
- `let q = p; return p.x` → reads the MOVED struct's field (latent UAF once the new owner is block-local and drops first — masked today only by the non-recycling bump allocator).
- `let q = p; p.x = 99; return q.x` → returns **99**: a WRITE through moved-from `p` mutates `q`'s aliased box — an **observable WRONG ANSWER** for a program shv2 should REJECT (use-after-move).
Same "one predicate, one enforcing site" disease as the moves gate itself. **Not a regression** (the check was never at these sites) and not in the suite. Proper fix is a rung-sized piece: a shared "binding is live" guard applied at EVERY binding-read/receiver site, **+ a DESIGN RULING** (does a partial field store `p.x = …` REVIVE `p`? — it should NOT), + `maxoncstderr` spec coverage. **Do before the ownership model is relied on with a recycling `__mm_free` free list.** [[project-p12-wave-c-moves]]

### 43. 🔴 NEW 2026-07-17 (P1.2 Wave D follow-up) — **`s.append(s)` (SELF-append) is a use-after-free: the grow path frees the old buffer BEFORE the blit reads it. MASKED (bump allocator), a WRONG ANSWER under a recycling `__mm_free`.**

**Found while landing Wave D; the review flagged it as the one item to track as its own follow-up.** `__str_append(self, other)` captures `otherBuf` (other's buffer pointer) once at entry, then on a **grow** (`capacity < requiredLen` or a non-root parent → the `detach` path) runs `freeOld` = `__mm_free(selfBuf)` **before** `blit` does `__str_copy(dstAddr, otherBuf, otherLen)` (`StringRuntime.maxon:774` then `:795`). When `self` and `other` are the SAME record — `s.append(s)` — `otherBuf == selfBuf`, so the blit's SOURCE is the buffer `freeOld` just released: a **use-after-free**. **STAYS MASKED under the bump allocator** (v1's arena never recycles, so the freed bytes are still intact when the blit reads them — the output is even correct, `"ss"`), exactly the class of #40/#42: it becomes an observable wrong answer **the moment `__mm_free` gets a free list**. The in-place path (enough capacity, own root) is safe — no free, and dest `[selfLen, 2·selfLen)` never overlaps source `[0, selfLen)`.

**Fix (its own rung):** delay the old-buffer free until AFTER the blit — thread the "old root to free" through `afterDetach`→`blit` to a post-blit `freeOld` block — so `otherBuf` stays valid through the copy. (The detach already copies self's live bytes into `newBuf` via `__str_copy(newBuf, selfBuf, selfLen)`, so for the self-append case `newBuf[0,selfLen)` is correct before the blit; only the blit's *source* is stale.) **+ a DESIGN RULING**: is `s.append(s)` accepted (yielding `"ss"`) or rejected? — and an `s.append(s)` acceptance/rejection spec either way (none exists; the corpus never self-appends). Whether it is even *expressible* today depends on how the move model treats `append`'s `other` param at the aliased call — pin that in the same spec. **Do before the recycling `__mm_free` free list**, alongside #40 (both are bump-allocator-masked ownership UAFs that the free list turns real). [[project-p12-wave-b-closed]]
