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
fields here"*; `RdataRelocKind.dataSectionRipRelDisp32` and `GlobalLabelClass.dataSection` already exist;
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

### 4d. ⚠ THE SCALE CORPUS IS SYSTEMATICALLY BLIND TO THE FEATURE JUST LANDED — **THIRD instance**
`scale-test` reported P1.0d.5a's cost as `2 × files + 1`, exact on all six rungs. **It measured the feature
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

### 4g. ⚠ Three comments claimed mechanisms that do not exist (found by P1.0d.5b's review)
Same family as `throws_` being **write-only for its entire life** while a comment claimed *"the runtime
branches on it"*, and `TargetOpMeta.setsFlags` (written 40× / read 0×), and the `xor reg,reg` idiom the
lowering never emitted. **Now four instances of "a comment describing a compiler nobody wrote."**
- ⭐ **`classifyGlobalLabel` has ZERO CALLERS** (`CodeResult.maxon:103-119`) — dead since M1-A. Its comment
  claimed it routes globals to `.data` by the `__data_` prefix, that a label without it *"would fault on its
  first store to a read-only page"*, and that *"the two are one fact and this is its name."* **All false**,
  and **disproved by measurement**: set the prefix to `"zzz_"`, rebuild — globals still land in `.data`, still
  take stores, still return the right answer. Routing is by `RdataRelocKind.dataSectionRipRelDisp32`, filed
  unconditionally. ⚠ **And the fact IS written twice** (`DataLabelPrefix` vs the literal at
  `CodeResult.maxon:107`) — **they cannot disagree only because one is dead.** *(Left in place: it plausibly
  becomes live when `__slab_`/`__gt_` runtime globals arrive at P1.2/P1.5. **Delete-or-wire is that rung's
  call**, and a comment now names what must happen first.)*
- `PeWriter.maxon:34-37` carried the same false credit.
- `TargetPrinter.maxon` claimed `RequiredData` blocks *"are compared against exactly these lines"* — **the
  rung contradicted itself**: `SpecTestRunner` and `PeSectionReader` both correctly state the opposite, and
  `compareDataSection` never touches `printDataSection`.

⭐ **AND NEW CODE SHIPPED UNTESTED BEHIND A CITED PIN THAT WAS DISABLED.** `parseTopLevelAssignment`'s `let`
arm cited `top-level-let-struct-reassign-error` as its test — **which is `disabled-test:` behind P1.1
structs**, and `assignment.md`'s E2013 cases are all locals on a different path. The property needs no
structs at all. ⇒ `top-level-let-scalar-reassign-error` added, verified to pass **and** to fail when
perturbed. **A citation is not a test. Check the pin is ENABLED.**

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
**hoists out of a loop that writes it** (a silent wrong answer; pinned by a spec, verified: a loop returns 10).

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

---

## 🔧 Instrument & tooling

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

### 12. ⚠ NEW — the ERROR-CODE REGISTRY is written down TWICE, and two agents collided on E3099
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
