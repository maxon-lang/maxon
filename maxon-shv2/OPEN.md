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

### 7b. `a shl -1` silently becomes `a shl 63` — ⏳ IN PROGRESS
The hardware masks CL to its low 6 bits. **Verified on BOTH compilers.** Defensible for a *runtime* value
(the compiler cannot see it, and both lowerings agree). **Not** for a literal the compiler can see: a
negative count reads as "shift the other way" and silently becomes the *maximum left shift*.
⇒ Being tightened to: **a shift-count LITERAL outside `0..63` is a compile error**, which also catches
`shl 64` (≡ `shl 0`, i.e. a no-op) and `shl 100` (≡ `shl 36`). **It is free** — a tree-wide grep finds
**zero** out-of-range shift literals in any real code. One of the three hits is corroboration:
`maxon-selfhosted/.../ConstantArrayLiteralRdata.maxon:157` is a *comment* saying `(1 shl 64) - 1`
"overflows i64 left-shift, so a full-width target is left unmasked" — a developer already hit this and
worked around it in prose.

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

### 13. 🔴 NEW, AND THE REAL ONE: a capturing closure that ESCAPES its frame nil-derefs, by ANY route
Closures capture **by reference** — `LowerClosureCreate` stores the *addresses of the enclosing frame's
stack slots*. So a capturing closure that outlives its frame reads a dead frame. **It compiles clean and
dies at runtime.** This is the classic upward-funarg problem, and **fields are only one route**:

```maxon
function makeAdder(bump Integer) returns IntFn
	let f = function(n Integer) gives n + bump
	return f                    ⇒ compiles. Then: panic: nil pointer, in _$closure_0
end 'makeAdder'
```
**Measured, not inferred** (2026-07-14, against the bootstrap).

**Partially guarded (`3f42ecd7d`): `E3099` rejects a capturing closure stored in a struct FIELD** — at the
struct literal, the field assignment, and the nascent-self slot, followed through a `let` binding. The
non-capturing case needs no exception: a closure that captures nothing lowers to a plain
`MaxonFunctionRefOp` and can never carry an env, so it passes *by construction*.

**Still open — every other escape route:** `return`ing one (above), storing one in a **global**, in an
**Array/Map element**, or passing one to a function that does any of those (the **interprocedural** route:
at the store the value is a *parameter*, and whether it carries an env is a fact about the *caller*).

⇒ **The single principled rule is: a closure that CAPTURES may not ESCAPE its defining frame** — and the
mechanism that decides it is **escape analysis**, which is *exactly* shv2's **P1.5**, where capture-into-heap
**IS** escape and closures co-land with `async` for that reason. **This is the same mechanism, found in the
bootstrap first.** Whether to build a miniature of it in the bootstrap (which shv2 retires) or to accept a
partial guard there is a **scope decision, not a technical one.** — only a function-typed parameter
`spec.handler(doc, id)` ⇒ `E9001: Cannot determine function type from MaxonFieldAccessOp`
(`2-Parser.cs:9223`). The same value calls fine once it arrives as a **parameter**. So a struct can
*store* a function but nothing can call it from there.
Fails **loudly** (a compile error, not a miscompile), which is the only reason it is tolerable.
⚠ **shv2 will want exactly this shape** (a table of passes/handlers) — decide whether it supports it
*before* a pass table gets designed around the workaround.

---

## 📋 Environment / process notes that cost real time

- 🔴 **THE `maxon-dev` MCP TOOLS ALWAYS DRIVE THE *MAIN REPO*, NEVER YOUR WORKTREE — AND THEY REPORT
  SUCCESS WHILE DOING IT.** `repoRootPath()` ([`maxon-dev-mcp/mcp/Util.maxon:149`](../maxon-dev-mcp/mcp/Util.maxon#L149))
  resolves the root from **`Process.executablePath()` — the MCP *server's* own binary**, which lives in the
  main repo. **It never looks at the caller's working directory, and it cannot: one server process is
  shared by every agent.** So in a worktree, `build` returns `success: true` on a tree containing **none**
  of your changes; `run_spec_test` runs the **main** binary against the **main** specs; **`updateRequired`
  REWRITES THE MAIN TREE'S COMMITTED GOLDENS**; `run_scale_test` measures the **main** shv2 and `note:`
  writes a row into the **main** optimization log; and `fmt` — which reformats the entire tree in place
  when given arguments — runs with the **main repo** as its cwd.
  **Only `lookup_error_code` and `mm_trace_analyze` are worktree-safe.** In a worktree, drive
  `./bin/maxon.exe` and `./maxon-shv2/.maxon/maxon-shv2.exe` **by hand**.
  ⚠ **`.claude/CLAUDE.md` said "PREFER THE MCP TOOLS" while the rung workflow said "work in a worktree",
  and the two silently contradicted each other.** Caught 2026-07-14 with **five agents running against
  it** — by an agent whose `build` succeeded on a tree with none of its work in it. **The project's own
  signature bug — one fact written down twice — at the TOOLING level.** Documented in CLAUDE.md
  (`13855215b`); the real fix (a `repoRoot` param, and every result **echoing the root it actually
  used**, so a false green is *visible* rather than silent) is still to do.
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
