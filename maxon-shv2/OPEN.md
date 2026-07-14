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

---

## 🔴 Wrong answers — a correct program compiles and returns the wrong number

### 1. Cross-file callee types DEFER into an unsound ACCEPT
```
a.maxon:  export function isReady() returns bool
b.maxon:  let x = isReady()
          return x + 41        ⇒ shv2 COMPILES it and returns 42
                               ⇒ bootstrap: E2004: Cannot operate on bool and int
```
The identical defect the bool/int rung fixed *within* one file, surviving where the parser cannot see.
Its twin is worse — the deferral does not merely fail to reject, **it mints a FALSE TAG**:
`flag and crossFileInt()` yields a merge phi tagged `boolean` carrying the int `7`, so `if m` branches
on 7.

**KEEP the deferral.** Refusing an `unresolved` operand would reject `crossFileInt() + 1` — a *correct*
program — and over-rejection is the worse failure. Making `wordOpResultTag` defer on `unresolved` (the
tempting one-liner) would type `flag and crossFileBool()` as unknown, and `not` refuses an unknown
operand, so `if not (ready and enabled())` would stop compiling. Bad trade.

⇒ **The fix is UPWARD: a whole-program signature query**, so `unresolved` is never the answer for a
callee some file declares. That means a signature table over every file's tokens **and re-keying the
per-file parse memo on it** — `parseCacheValid` (`QueryEngine.maxon:107`) keys on the file's *own*
content hash alone, so a cross-file input goes stale under an incremental edit. Blast radius: the query
spine. Gate: `verify-warm-rebuild`.

⚠ **PREREQUISITE — it cannot be gated today.** The `.test` fragment format **cannot express a
multi-file case**. The harness must grow multi-file spec support *first*, or this ships with no
regression test.

**Sibling, same sentinel:** `calleeResultType` returns `unresolved` for **both** a cross-file callee
**and a VOID one** — one sentinel, two meanings — so `let x = noop()` then `x + 4` compiles too
(bootstrap: `E2004: Function 'noop' does not return a value`). Needs a distinct *"no value"* tag.

### 2. `a / 0` is a raw hardware trap, not a panic  *(= PLAN.md's P1.0d.3)*
Escapes as `0xC0000094`. `specs/safety.md` demands exit **1** + `panic: integer divide by zero` on
stderr. Same for `mod`. ⇒ Needs a **minimal emitted panic runtime** (write to stderr + exit) — the first
slice of **Workstream R**, arriving early because a correctness gap forces it.

### 3. Awaiting one promise TWICE double-frees its managed result
`mm_decref: refcount underflow (already zero)`. The payload is handed out as an owned **+1 per await**
though the thunk owns it once. **Not** an error-type bug — a *non-throwing* promise with a managed
result double-frees identically.
⇒ A language decision: make `await` **linear** (a promise is awaited once, the compiler rejects a
second), or incref per await. **LOAD-BEARING FOR P1.5** — decide it *before* shv2 ports `async`,
alongside `Promise with (T, E)` (see PLAN.md). A promise that carries its error type **and** has a
defined await-arity has no representable version of either bug.

---

## ⚠ Gaps the plan never listed — all found by the corpus

### 4. Top-level `var` (GLOBALS) is missing entirely  *(= PLAN.md's P1.0d.5)*
`specs/short-circuit-evaluation.md` is **0/12, and NINE of the twelve need a global** — a global is how
most spec files *observe a side effect*, so expect it to gate a wide slice.
**Not merely a parser rule:** storage (rdata/data), initialization order, and — for a managed global —
a lifetime.
⚠ **It already cost coverage:** with no globals, short-circuit `and`/`or` had **no way to prove the
right operand is SKIPPED** (an eager `and` returns the *same answer* on every input, so no value test
can see it). `specs-shv2/short-circuit-elision.md` works around it — the guarded operand **divides by
zero**, so a clean exit *is* the proof. **Retire that spec when globals land.**

### 5. The parser's SSA construction is **O(V·B)** — and the corpus is BLIND to it
Each `if` snapshots the whole mutable-var set; each `while` mints a phi per mutable var in scope.
**Measured:** dead block-args go **54 → 252 → 1080 → 4464** across a doubling ladder — a ratio
converging on **x4.00 while the program only DOUBLES**. ValueIds are minted even for phis that are later
pruned, so `blockArgIdBound` (and every dense column sized by it) is **O(L²)**.

⇒ **FIX THE CORPUS'S REALISM, NOT THE PROBE** (user directive). `ScaleCorpus`'s `longFunction` emits
`var acc = a` then N `if`s all mutating that **ONE** accumulator — so V=1 and O(V·B) collapses to O(B).
Real Maxon functions carry several locals mutated across many branches. **Make the generated functions
realistic and the cost surfaces on its own**, along with any other V-dependent cost nobody has thought
to look for. *A knob built to expose a bug you already found only finds bugs you already found.*

⚠ The real fix (a write-trail, so a merge costs O(assignments-in-branch)) **cannot be byte-identical on
the loop half** — minting fewer phis renumbers values, so fragments WILL move. That is a reviewable
codegen diff, not a regression.

*(Confirmed NOT a multiplier: `emitShortCircuit` mints one phi and takes no snapshot — exactly 58 parse
allocations at V = 8, 32, 128 and 256.)*

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

### 7. `StdOp.const` claims `clobbersFlags`, and `TargetOpMeta.setsFlags` is written 40× / read 0×
`mov r, imm` writes no flags. This defeats compare/branch fusion on `if a > 0 and b > 0` — **exactly 3
wasted instructions** (`setcc` + `movzx` + a redundant `cmp`), because the short-circuit's seed literal
sits between the compare and its branch. One line to fix, **but it moves fragment goldens**, so it wants
the suite as its own gate.
`TargetOpMeta.setsFlags` already **contradicts** the Std tier's `clobbersFlags` on `const`/`binOpImm`/
`unaryOp` — two descriptions of one hardware fact, one of them dead and disagreeing. Delete it, or derive
one from the other so they cannot drift.
*(Also: `a shl -1` silently becomes `a shl 63` — the hardware masks CL. Defensible for a runtime value;
not for a negative literal the compiler can see.)*

### 8. The bootstrap cannot CALL a function-typed FIELD — only a function-typed parameter
`spec.handler(doc, id)` ⇒ `E9001: Cannot determine function type from MaxonFieldAccessOp`
(`2-Parser.cs:9223`). The same value calls fine once it arrives as a **parameter**. So a struct can
*store* a function but nothing can call it from there.
Fails **loudly** (a compile error, not a miscompile), which is the only reason it is tolerable.
⚠ **shv2 will want exactly this shape** (a table of passes/handlers) — decide whether it supports it
*before* a pass table gets designed around the workaround.

---

## 📋 Environment / process notes that cost real time

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
