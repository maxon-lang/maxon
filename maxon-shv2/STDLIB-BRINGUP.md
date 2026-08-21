# The stdlib bring-up table — MEASURED 2026-08-05

> ## ⚠ THE LIVE STATE IS THE LAST SECTION OF THIS FILE. EVERY TABLE ABOVE IT IS DATED.
>
> **This file is a stack of dated measurements, not a status page** — it is appended to and corrected,
> never edited into agreement, because that is what caught four classification errors in one day. So
> **read the last section first**, and treat every earlier count, class and blocker as true *on its own
> date*. The remainder is deliberately written down in ONE place here (and on `PLAN.md`'s board); a
> second copy in this banner would be the duplicate-fact bug this file keeps filing against others.
>
> ⇒ **RE-PROBE BEFORE PLANNING AGAINST ANY ROW.** It is one command per module and it has changed the
> answer every single time somebody bothered.

**Tree:** `C:\Users\Eric\dev\maxon` @ `7bc83ac7d`, clean. **Binaries:** `bin/maxon.exe` and
`maxon-shv2/.maxon/maxon-shv2.exe` both rebuilt immediately before the run.
**Scope:** measurement only. **No source file was changed** (`git status --short` empty after the run).

This file replaces PLAN.md §"First blocker per module" (`:4366`), which was dated **2026-07-24** and had
gone stale in both directions — it listed modules that have since landed, and it listed six files as
blocked on `extension` that are not blocked on `extension` at all.

---

## ⛔ THE HEADLINE: THE READINESS CRITERION IS VACUOUS FOR A CONDITIONAL-EXTENSION MODULE

`StdlibLoader.maxon` prescribes one check before an entry may be listed (`:538`, `:595`, `:667`):

> `maxon-shv2 build stdlib/<Module>.maxon` must exit 1 with **`E3001: No 'main' function found` AND
> NOTHING ELSE.**

**MEASURED: it cannot see inside a body the compiler declines to specialize.** The control, run twice on
copies outside `stdlib/` so nothing in the tree was touched:

| Probe | Result |
|---|---|
| `stdlib/Log.maxon` (ordinary `static function`s), verbatim copy | `E3001` only |
| …same copy, `let bogus = NoSuchType.definitelyUndefined(1)` injected into a static's body | `E3001` **+ `E3004 call to undefined function 'NoSuchType.definitelyUndefined'`** ✅ the criterion sees it |
| `stdlib/helpers/sort/insertionSort.maxon` (one `extension Array where Element is Comparable`), verbatim copy | `E3001` only |
| …same copy, **the identical injection** into `internalInsertionSortRange`'s body | **`E3001` only** ⛔ the criterion is blind |

⇒ **A module whose whole content is a conditional `extension` passes the criterion without a single one of
its bodies being analyzed.** That is all six `helpers/sort/*` files — **1,455 lines** — and it is also true
of `stdlib/Array.maxon`'s four `extension` blocks once its declaration blocker clears.

The independent confirmation is in the source: all six sort helpers call **`Log.trace(…)`** (e.g.
`driftsort.maxon:154,165,185,190,194,255,326,365`; `insertionSort.maxon:29`), `stdlib/Log.maxon` is **not
loaded**, there is no `#if` guarding those calls — and the probe reports the files clean.

**⇒ THE RULE FOR THIS WORKSTREAM: a green `E3001` is evidence only for the declarations the compiler
actually analyzed.** For any module carrying a conditional `extension`, pair it with a driver that forces
specialization (a concrete `Array with <T>` whose element satisfies the `where` clause, calling the
extension method) and read *that*. Otherwise the entry is being weighed on a check that cannot fail.

*(This is the same defect class the tree has hit repeatedly — a gate that cannot fire. See BATCH10's
thesis, `A3m`, and `G9`/`G10`.)*

---

## The count

**50 `.maxon` files under `stdlib/`, 18,431 lines.** ⚠ **The listed count in this file is a DATED SNAPSHOT and the authority is `StdlibLoader.whitelistedStdlibModules` itself** — it is ONE `listWhitelistedModule` call per listed module, so count the calls in that function and never trust a number written anywhere else. It read **16** when this table was measured (2026-08-05), **18** as of 2026-08-06 (`Log` via `S2g`, `Ascii` via `S2k`), and **36** as of 2026-08-10, the whole `stdlib/Array.maxon` + `helpers/sort/` cone having landed with `3c9f6a1d91`. A prose count of a list is the second copy this project keeps being bitten by; the sentence stays only to date the rest of the table.

⚠ Several places in the tree still say **48 / 49 files** and **"3 whitelisted"**: `PLAN.md:88`, `:2092`,
`:3605`, `:3694`; `StdlibLoader.maxon`'s header; `Testing/ladders/genwhitelist.sh`'s header. The most
recent board text (BATCH2 slice 8) is correct at *"stdlib cone 11 → 16 of 50"*.

---

## The 34 unlisted modules, by first diagnostic

`READY` = `E3001` and nothing else. ⚠ = `READY` but **unexamined** per the headline above.

### A. Ready today (7 files, 1,519 lines)

⚠⚠ **"READY" MEANS THE MODULE COMPILES. IT DOES NOT MEAN THE ENTRY IS FREE — MEASURED ON `Log.maxon`
2026-08-05, WHICH WAS THE FIRST ONE TRIED.** Listing it broke `StdlibLoader.maxon`'s byte-neutrality
invariant: a same-worktree A/B (two binaries, one trivial program) gave **1,592 code bytes without the
entry and 2,978 with it — +1,386, +87%** — for `function main() returns ExitCode / return 7`, which never
spells `Log`. `Log.maxon` is the first candidate with mutable **top-level state**, and the two mechanisms
that keep an unused module free prune the wrong things: DFE prunes FUNCTIONS, `StdlibFacts.unreachable`
prunes LOWERING, and a top-level `var` with an initializer runs in `__module_init` — neither. The entry
was withdrawn and the blocker filed as board row **`S2h`**.

⇒ **A third question belongs beside "does it compile?" and "are its bodies analyzed?": *what does the
entry cost a program that does not use it?* Answer it with a two-binary A/B, not by reading.** Every
module below carrying a top-level `var`/`let` inherits this.

| Module | Lines | Verdict |
|---|--:|---|
| `Log.maxon` | 64 | compiles clean (verified by injection) — but **the ENTRY is blocked on `S2h`**, see above |
| `helpers/sort/insertionSort.maxon` | 49 | ⚠ READY-UNEXAMINED |
| `helpers/sort/smallSort.maxon` | 138 | ⚠ READY-UNEXAMINED |
| `helpers/sort/driftQuicksort.maxon` | 171 | ⚠ READY-UNEXAMINED |
| `helpers/sort/pdqsort.maxon` | 309 | ⚠ READY-UNEXAMINED |
| `helpers/sort/driftsort.maxon` | 382 | ⚠ READY-UNEXAMINED |
| `helpers/sort/mergeSort.maxon` | 406 | ⚠ READY-UNEXAMINED |

⇒ **PLAN.md:4366's "top-level `extension` — PrimitiveExtensions, all 6 `helpers/sort/*`" is WRONG about
the six.** Top-level `extension Array` parses and analyzes. The real `extension` gap is **`extension` on a
PRIMITIVE** (`PrimitiveExtensions.maxon`, below) — a different and much smaller thing.

### B. Blocked on a COMPILER-OWNED NAME (7 files, 3,134 lines)

`E2015: Unsupported: a declaration of the type name '<X>', which the compiler owns`. Each needs its name
retired from `TypeResolution.isCompilerOwnedTypeName` (or the equivalent syntactic settling) one at a time,
via the `__Managed*Error` precedent (`Project.declaredBuiltinErrorEnum`).

| Module | Lines | Name it declares | Notes |
|---|--:|---|---|
| `Interfaces.maxon` | 240 | `IterationError` (`:28`) | then `Ordering` (`:34`). **The protocol root — everything in §C waits on it** |
| `Array.maxon` | 664 | `ArrayError` (`:6`) | then the `Array` base itself |
| `String.maxon` | 1066 | `StringError` (`:6`) | then `String` — needs marker-interface discovery |
| `Map.maxon` | 395 | `MapError` (`:6`) | |
| `Character.maxon` | 236 | `Character` (`:19`) | needs `BuiltinCharLiteral` discovery |
| `CharacterSet.maxon` | 137 | `CharSet` (`:19`) | `CharacterMemberSetAliasName` |
| `Set.maxon` | 396 | — | stops earlier — was the field default (`:19:33`, closed by `S2f`), now `E3076 :43:15`, see `S2i` |

### C. Blocked ONLY on `Interfaces.maxon` not being loaded (2 files, 301 lines)

`E3015: type '<T>' implements unknown interface '<I>'` — the protocol interfaces are synthesized as
*conformances* but are not nameable as interfaces a stdlib type may declare.

| Module | Lines | Errors | Unknown interfaces |
|---|--:|--:|---|
| `Range.maxon` | 84 | 4 | `Iterator`, `BidirectionalIterator`, `Iterable` |
| `helpers/string/views.maxon` | 217 | 7 | `Iterable`, `Iterator` (×6) + `Unknown type 'ByteCount'` |

⇒ **These two fall out of Wave 3 for free.** They need no work of their own.

### D. Blocked on a MISSING INTRINSIC (4 files, 256 lines)

`E3004: call to undefined function '__Builtins.<x>'`.

| Module | Lines | Missing intrinsic | Notes |
|---|--:|---|---|
| `Print.maxon` | 11 | `__Builtins.writeStdout` | ⚠ **also needs the bare-name `print` builtin retired first** — `parseCallNamed`'s chain claims it |
| `PrintError.maxon` | 6 | `__Builtins.writeStderr` | same shape |
| `Process.maxon` | 33 | `__Builtins.executablePath` | ⚠ also declares `ExitCode`, a compiler-owned name |
| `Console.maxon` | 206 | `__Builtins.readStdin` | |

### E. Blocked on one ordinary language gap each (8 files, 2,283 lines)

| Module | Lines | First diagnostic | The gap |
|---|--:|---|---|
| `Sha256.maxon` | 204 | ~~`E2015 :152:34`~~ → ~~`E5001`~~ → **compiles and RUNS; unlisted only** | ✅✅ **TWO BLOCKERS CLEARED AND NEITHER WAS THE ONE THIS ROW NAMED.** `createIterator` was never the real blocker: past it lay **`E5001` — 23 live values against a pool of 14** the moment a program CALLED `sha256`, which `build stdlib/Sha256.maxon` alone cannot see because it never runs the register allocator. `S2m-b` (user ruling) rewrote the round state onto a `Vector` and added constant-index bounds-check elision: **23 → 12 live values, margin 2**, and `sha256("abc".toByteArray())` now answers **186 (0xba)** under shv2, byte-identical to the oracle. The rewrite also removed the `createIterator()` call entirely, dissolving `S2l`'s sha256 half. ⇒ **the only thing left is the whitelist entry** — `S2q`. |
| `Ascii.maxon` | 66 | ~~`E2028 :10:4`~~ → **`E3001` ONLY (it type-checks clean)** | ⚖ **THIS ROW WENT STALE THE SAME DAY IT WAS MEASURED — re-measured 2026-08-05 after `BATCH23`.** The blocker was `match c '0' to '9'` — pattern type `int` vs scrutinee `Character` — which is exactly the character-literal WIDTH RULE `BATCH23` deleted: every literal is now a `Character`, and `Character` gained ordering and range patterns, so this file's five `match` arms type-check. **No compiler change is owed here any more; what remains is the WHITELIST entry**, which is `L-stdlib` — not a lane `BATCH23` held. ⚠ But read the headline above before listing it: `Ascii` is all `static function`s, NOT a conditional `extension`, so the criterion is NOT vacuous for this one. |
| `PrimitiveExtensions.maxon` | 101 | `E2010 :2:11` | `extension int` — the extension decl path rejects a primitive keyword as the extended type |
| `Json.maxon` | 1080 | ~~`E2015 :77:37`~~ -> **`E2015 :281:3`** | ✅ **THE FIELD-DEFAULT BLOCKER IS GONE — `S2f` closed it 2026-08-05.** Re-probed on `ba7f8271f`: this module now advances 204 lines to a **field access through `doc`, a struct-typed FIELD of the enclosing type**. A different mechanism, unowned by any row. |
| `Set.maxon` | 396 | ~~`E2015 :19:33`~~ -> ~~`E3076 :43:15`~~ -> **`E2015 :55:11`** | ✅ TWO blockers cleared: the field default (`S2f`) and `ElementArray{}` (`S2i`). Now stops at **`insert` reading `sizeof` of the type parameter** — a generics rung, not a stdlib one. |
| `List.maxon` | 175 | `E3086 :21:10` | field `chain` uninitialized by the literal and has no default — the same mechanism, one door over |
| `Vector.maxon` | 50 | ~~`E3086 :19:10`~~ → **`E3001` only** | ✅ **CLEARED, and the module type-checks clean** (`W86`, re-measured 2026-08-13). ⛔ **The real blocker was never a field default: it is the SIZE.** `Self` inside `type Vector uses Element` states no count, while every vector VALUE has one (`GenericInstanceRegistry.fixedSizes`, part of the intern key), so a corpus method's `self` can never match its receiver — `E3005 expected 'Vector_Int', got 'Vec3'`. See `W86`. |
| `Testing.maxon` | 343 | `E2051 :34:13` | `__TestReport` is `__`-reserved — needs the exemption `Builtins.maxon` has via `Queries.CompilerInternalDeclaringModule` |

⭐ **Four of these are ONE mechanism**: a **non-literal field default** (`Json`, `Set`) and its dual, a
field with no default that a literal omits (`List`, `Vector`). Build the captured-initializer-replayed-at-
every-literal mechanism once and four modules move — **1,701 lines behind one door.**

### F. Blocked on a CALL-SYNTAX divergence (2 files, 170 lines)

`E2053: the second and later arguments must be named ('name: value')` — both at a call into the
**`__`-reserved intrinsic namespace**, where the corpus passes positionally.

| Module | Lines | Site |
|---|--:|---|
| `helpers/string/unicodeCategory.maxon` | 75 | `:55:44` — `__Builtins.ucdByteAt("__ucd_bmp", cp)` |
| `TcpClient.maxon` | 95 | `:23:70` — `__ManagedSocket.tcpConnect(host.addressableBytes(), port)` |

⚖ **Needs a ruling**: either intrinsic calls are exempt from the named-argument rule (the references pass
positionally), or the corpus is edited at these two sites. It is one decision covering both.

### G. Blocked on something larger (3 files, 1,208 lines)

| Module | Lines | First diagnostic | Notes |
|---|--:|---|---|
| `helpers/itertools/withIterator.maxon` | 19 | `E2015 :12:36` | **a tuple whose element 0 is a type parameter** — the deep one; `Interfaces.maxon` reaches it |
| `HttpClient.maxon` | 248 | `E3005 :29:22` | `cannot assign 'unknown' to variable 'HttpHeaders.headers' of type 'struct'` — **not** a socket blocker |
| `Subprocess.maxon` | 806 | `E2015 :357:25` | **overloading `Subprocess.run` where one declaration `throws`** — one of `D7`'s four residual overload shapes |
| `helpers/http/httpHelpers.maxon` | 154 | `E3011 :4:9` | `Unknown type 'HttpMethod'` — only its sibling not being loaded; **list the two as one cone entry** |

⚠ **`Subprocess.maxon`'s first blocker is NOT `Map`.** PLAN.md:3675 records it *"panics immediately
(`SignatureIndex.maxon:3073: base struct 'Map' is not declared`)"*. Measured today it does not panic at
all — it reaches `:357` and gives a clean overload diagnostic. That row is stale.

### H. Excluded permanently

| Module | Lines | First diagnostic | Decision |
|---|--:|---|---|
| `Internals.maxon` | 4117 | `E2051 :26:10` (`__mm_incref` is `__`-reserved) | **EXCLUDE** — user ruling 2026-08-05. It is v1's slab allocator + refcount runtime over the privileged `__Internals.*` namespace (98 raw runtime functions); the C# bootstrap excludes it by name (`0-Compiler.cs:1413`) and emits its runtime natively. shv2's runtime is Std-tier IR (`Runtime/MmRuntime.maxon`); loading this would be a second allocator. Wave 7 turns the whitelist's incidental exclusion into one named, documented one. |

---

## What this changes about the wave plan

1. **Wave 1 gets a free head start.** `Log.maxon` is listable today with no compiler work at all.
2. **The six sort helpers are NOT a Wave-5 generics problem — they are an UNEXAMINED-CODE problem.** Before
   listing any of them, build the forcing driver. Their real content is the `SortComparator` function value
   threaded through mutual recursion, and nothing has ever compiled it.
3. **One mechanism unlocks four modules** — the field-default door (`Json` · `Set` · `List` · `Vector`,
   1,701 lines). It should lead Wave 1, not trail it.
4. **`Range.maxon` and `helpers/string/views.maxon` are free riders on Wave 3.** Add them to that batch;
   they need nothing of their own.
5. **`HttpClient` and `Subprocess` are not blocked on what the tree says they are** (sockets / `Map`). Their
   real first blockers — a struct-typed field inference failure and a throwing overload — are ordinary
   language gaps that may well clear before Wave 6.
6. **`§F` needs one ruling** covering both sites before either module can move.

## Re-probe after `S2f` — MEASURED 2026-08-05 on `ba7f8271f`

All 34 unlisted modules re-probed after `S2f` (field defaults may be an arbitrary expression) and after
`BATCH23` (every character literal is a `Character`). What moved:

| module | was | now |
|---|---|---|
| `Json.maxon` | `E2015 :77:37` field default | **`E2015 :281:3`** — struct-typed field access through `doc` |
| `Set.maxon` | `E2015 :19:33` field default | **`E3076 :43:15`** — `ElementArray{}`, see `S2i` |
| `Ascii.maxon` | `E2028 :10:4` pattern type | **`E3001` only** (BATCH23) |
| `List.maxon` · `Vector.maxon` | `E3086` | ~~**unchanged** — they need v1's THIRD arm (a managed-builtin init for an omitted builtin-typed field), which `S2f` deliberately did not build~~ ⛔ **THAT PREDICTION IS FALSE FOR `Vector`, MEASURED 2026-08-13 (`W86`)** — the module now probes **`E3001` alone**, so whatever cleared the field-default family reached it and no "third arm" is owed. The prediction named a CURE from a symptom, which is this file's own standing warning. `List.maxon` still reads `E3086 :21:10` and is **unre-diagnosed** — do not assume it shares `Vector`'s story either. |

⚠ **RE-PROBE BEFORE TRUSTING THE LIST BELOW — it is dated 2026-08-05 and four rungs have landed since.** Known movement: `Ascii` and `Log` are now LISTED, not merely ready; `Sha256` compiles and runs (above); `Json` and `Set` advanced past their field-default blocker via `S2f`; `Set` then advanced again via `S2i`. The probe is one command per file and takes seconds — see "Reproducing this table".

**Modules giving `E3001` and nothing else, as of the 2026-08-05 probe (8):** `Ascii` · `Log` · all six
`helpers/sort/*`. ⚠ Six of those eight are conditional `extension`s, so the criterion is VACUOUS for them
(see the headline); `Ascii` and `Log` are the two where it means something — and `Log`'s ENTRY is still
blocked on `S2h` for a reason the criterion cannot see either.

⛔ **`S2i` — `<InnerAlias>{}` IS LEGAL IN THE ORACLE AND E3076 IN shv2, AND IT BLOCKS THREE MODULES.**
MEASURED both compilers on a minimal repro:

| shape | oracle | shv2 |
|---|---|---|
| TOP-LEVEL `typealias Nums = Array with Integer`, `Nums{}` in a free function | `E3076` | `E3076` — **agree** |
| INNER `typealias ElementArray = Array with Element` inside `type Holder uses Element`, constructed inside `Holder`'s own static | **compiles, runs (exit 5)** | **`E3076`** |

So the rule the oracle implements is that **a type's own body is a legitimate construction site for the
aliases that body declares**; shv2 applies the restriction without that exemption.

✅ **CLOSED by `S2i`. And the claim below was WRONG — corrected, because a wrong blocker attribution is how
a workstream mis-plans its own order.** It said "the FIRST blocker in three modules". Measured: it is the
first blocker in **`Set.maxon` alone**, which advanced `:43:15` → **`:55:11`** (a `sizeof` of the type
parameter through `insert`). `Map.maxon` and `Interfaces.maxon` **never reach it** — they stop far earlier
at the compiler-owned-name door (`MapError` `:6:13`, `IterationError` `:28:13`). They do write the
construct and would have met it, so the rung is still owed by them; it is a LATER blocker there.

The three modules that write it, all the same shape — an inner alias declared in the body that constructs it:
`stdlib/Interfaces.maxon:188` (used `:193`, `:201`), `stdlib/Map.maxon:18-19` (used `:52,54,81,83,273,275`),
`stdlib/Set.maxon` (used `:43,45,47`).

⚠⚠ **DO NOT "FIX" THE CORPUS HERE.** An earlier note claimed the bootstrap refuses this construct too and
prescribed rewriting `stdlib/` to `ElementArray.create()`. That measurement was taken on the TOP-LEVEL
shape, where the two compilers really do agree; it does not transfer to the inner-alias shape, which is
what all three modules actually write. Rewriting the corpus would paper over a compiler divergence and
make three reference files disagree with the oracle they are the reference for.

## Reproducing this table

```bash
cd C:/Users/Eric/dev/maxon
# rebuild BOTH binaries first — a suite/probe result is a claim about a BINARY
./bin/maxon.exe build maxon-shv2
while read -r f; do
  ./maxon-shv2/.maxon/maxon-shv2.exe build "stdlib/$f" > "temp/probe-$f.log" 2>&1
done < <list of unlisted modules>
```

And the control that produced the headline — **run it for every conditional-extension module**:
copy the module outside `stdlib/`, inject `let bogus = NoSuchType.definitelyUndefined(1)` into a body,
and re-probe. If the diagnostic does not appear, that body is not being analyzed.

---

# ⟳ REFRESH — 2026-08-06 (cone 20 of 50, all 30 unlisted modules re-probed)

**Rows rot. This table was re-measured from scratch at `a5296ad5a`, not edited.** Every number below is
one `maxon-shv2 build stdlib/<M>.maxon` at that commit.

⚠ **METHODOLOGY BUG, FOUND AND FIXED MID-SWEEP — read this before trusting any probe table, including
the one above.** My first pass classified `String.maxon` and `Character.maxon` as **CLEAN** because it
grepped for `^error `. **They do not error — they PANIC**, and a panic writes a different prefix. A probe
that greps for one failure spelling reports the other as success. ⇒ **classify on the EXIT CODE and on
`^panic` as well as `^error `.**

## The two reachable compiler PANICS — ✅ **FIXED at W16**

Both were the Wave 4 pivot, and they located it precisely:

```
stdlib/String.maxon      panic at Parser.maxon:42773  enclosingLayout:      `type String` is being parsed
stdlib/Character.maxon   panic at Parser.maxon:33155  requireConstructible: `type Character` is being parsed
```

Both said the same thing: *"the declaration sweep never recorded it — `recordScannedType` and
`parseTypeDeclaration` disagree about what opens a type declaration"*.

⛔ **AND THAT MESSAGE NAMED THE WRONG PAIR. Those two functions agreed perfectly.** The disagreement was
between the two doors over the *compiler-owned name* rule: `Parser.requireTypeNameNotCompilerOwned` ADMITS a
stdlib file (`livesUnderStdlibDirectory`), while `ProgramSignatures.recordStruct` dropped EVERY declaration of
such a name, the corpus's included. So the real parse walked into `type String`'s body and the registry had
just thrown its layout away. Giving that one write the provenance bit `Project.upsertDeclaredEnum` already
carried cures both — **the rule is now one sentence in one place**
(`Project.declarationContestsACompilerOwnedName`): *a compiler-owned type name may be declared by the CORPUS
and by nothing else.*

⚠ **THE PANIC WAS A REGRESSION THAT MASKED THE REAL BLOCKER, AND THIS FILE'S OWN TABLE RECORDED THE BLOCKER
BEFORE IT.** `StdlibLoader.maxon`'s `utf16.maxon` entry had already measured `Character.maxon` at
`E3005 Cannot return 'struct' from function declared to return 'Character'`. Adding `String`/`Character` to
`isCompilerOwnedTypeName` (BATCH2 slice 2 — correct, and it closes two silent wrong answers) turned that
diagnostic into a crash, because the strict drop had no provenance bit. **Post-fix, MEASURED, the E3005 is
back and current:**

```
stdlib/String.maxon      E3005 :115:3  Cannot return 'struct' from function declared to return 'String'
                       + E3004 Interfaces.maxon:194:3  undefined 'String.createIterator'
stdlib/Character.maxon   E3005  :30:3  Cannot return 'struct' from function declared to return 'Character'
```

⭐ **THE ORACLE COMPILES BOTH MODULES CLEAN** — `maxon build stdlib/String.maxon` and `.../Character.maxon`
each exit 1 at `E3001: No 'main' function found`, the readiness criterion. So the remaining gap is shv2's
alone, and it is **not** a rule about who may spell the name: shv2 keeps String's REPRESENTATION itself (a
fused 48-byte record of six flat 8-byte slots, `Runtime/StringRuntime.StringRecordBytes`) while the corpus
declares two fields, so a corpus `String{managed: …}` builds against offsets the builtin does not read. Both
reference compilers instead take String's whole layout FROM that file and neither has a name-reservation list
at all. ⇒ **listing these two is CONVERGENCE, a separate slice**; the panic cure is not a step toward it that
can be taken further without moving the representation.

## ⭐ SHARED BLOCKERS — the leverage, and where the plan is wrong

**Five blockers account for 17 of the 30 unlisted modules.** Fixing one unblocks a family, which is the
batching the plan asks for:

| blocker | modules it gates | worth |
|---|---|---|
| tuple over a type PARAMETER (**blocker 6**) | `Map.maxon`, `withIterator.maxon`, **and transitively `Range.maxon` + `views.maxon`** | **+4** |
| E3086 — a field not initialized by a struct literal | `Array.maxon`, `Vector.maxon`, `List.maxon` | **+3** |
| `__Builtins.writeStdout` / `writeStderr` | `Print.maxon`, `PrintError.maxon` | **+2** (⚠ also needs Wave 2's bare-name retirement) |
| E3001-vacuous (see below) | the 6 `helpers/sort/*` files | **+6, BUT NOT CHEAP** |

⛔ **TWO CORRECTIONS TO THE PLAN, AND ONE TO A CLAIM I MADE EARLIER TODAY.**

1. ⛔ **THIS ITEM WAS ITSELF WRONG WHEN FIRST WRITTEN, AND IS CORRECTED HERE (same day, `b77223fd0` → next
   commit).** I wrote that `Range.maxon` and `views.maxon` are *not* blocked on blocker 6 but on the E3005
   `Cannot return 'unknown'` diagnostic (`S2r`), on the strength of the probe's first line alone. **Reproducing it
   showed the opposite.** The corpus site is `Interfaces.maxon:219`,
   `typealias WithIterSelf = WithIterIterator with Iter, Element` — and `WithIterIterator` is declared in
   `helpers/itertools/withIterator.maxon`, which is **UNLISTED**. So it resolves to `unknown` and E3005 fires at
   `:223`. **A better diagnostic would still refuse.** What unblocks `Range` and `views` is listing
   `withIterator.maxon`, and that needs blocker 6. ⇒ **blocker 6 is worth +4**, not +2, and `S2r` unblocks
   NOTHING on its own — it is a real diagnostic-quality defect (the error names the USE, not the DECLARATION)
   and no more. ⚠ `W4`'s own commit message states this cone plainly (*"the entry is a CONE of two files and the
   second needs blocker 6"*); it was in context and the refresh contradicted it anyway. **Reading a probe's first
   line is not reproducing it.**
2. **The plan's Wave 1 trap note says `Process.maxon` declares `ExitCode`. It does not** — it declares
   `ProcessIntrospectionError` and `Process`. No compiler-owned name to retire there.

## ⛔ THE READINESS CRITERION IS VACUOUS FOR THE SORT FAMILY — MEASURED, NOT INFERRED

All six `helpers/sort/*` files probe at exactly `E3001: No 'main' function found` — the criterion
`StdlibLoader.maxon` prescribes. **That reads as "six modules ready to list". It is false.**

Every one of them is a single `export extension Array` body (my first grep used `^extension ` and missed
`export extension` — a second spelling bug in one sweep). Injection control, run on `insertionSort.maxon`
with `__Builtins.thisDoesNotExistAtAll()` placed **inside** the extension's function body:

```
insertionSort.maxon + an undefined call in the extension body  ⇒  E3001 only    ⛔ BLIND
```

Same result as the original `insertionSort` control at the top of this file, and the same conclusion:
**the bodies are never analyzed, so the clean probe measures the instrument.** ⇒ the sort family is Wave
5 work behind `Array`, not a cheap +6. **Do not batch it on the strength of the E3001 reading.**

## Full probe table — 30 unlisted, 2026-08-06

| module | first diagnostic |
|---|---|
| `Array.maxon` | E3086 `:126:10` field `managed` not initialized by this literal (2 errors) |
| `Character.maxon` | E3005 `:30:3` Cannot return 'struct' from function declared to return 'Character' (**panic FIXED at `W16`**; blocker is CONVERGENCE) |
| `CharacterSet.maxon` | E2010 `:31:9` Expected `function` but got `let` |
| `Console.maxon` | E3004 `:62:12` `__Builtins.readStdin` undefined — **`W5` in flight** |
| `HttpClient.maxon` | E2015 `:176:18` member access `send` on an `unknown` value |
| `Internals.maxon` | E2051 `:26:10` `__mm_incref` reserved — **EXCLUDED PERMANENTLY** (user ruling) |
| `Json.maxon` | E2015 `:281:3` field access through `doc`, a struct-typed field |
| `List.maxon` | E3086 `:21:10` field `chain` not initialized by this literal |
| `Map.maxon` | E2015 `:20:20` tuple whose element 0 is a type parameter (**blocker 6**) |
| `PrimitiveExtensions.maxon` | E2010 `:2:11` Expected identifier but got `int` — `extension int` unparseable |
| `Print.maxon` | E3004 `:10:2` `__Builtins.writeStdout` undefined |
| `PrintError.maxon` | E3004 `:5:2` `__Builtins.writeStderr` undefined |
| `Process.maxon` | E3004 `:29:17` `__Builtins.executablePath` undefined — **`W5` in flight** |
| `Range.maxon` | E3005 via `Interfaces.maxon:223` `Cannot return 'unknown'` (**`S2r`**) |
| `Set.maxon` | E2015 `:55:11` `insert` reads `sizeof` of the type parameter |
| `String.maxon` | E3005 `:115:3` Cannot return 'struct' from function declared to return 'String' (**panic FIXED at `W16`**; blocker is CONVERGENCE) |
| `Subprocess.maxon` | E2015 `:357:25` overloading `Subprocess.run` |
| `TcpClient.maxon` | E2053 `:23:70` second and later arguments must be named |
| `Testing.maxon` | E2051 `:34:13` `__TestReport` reserved (**`S2n` contracted**) |
| `Vector.maxon` | E3086 `:19:10` field `managed` not initialized by this literal (2 errors) |
| `helpers/http/httpHelpers.maxon` | E3011 `:4:9` Unknown type `HttpMethod` |
| `helpers/itertools/withIterator.maxon` | E2015 `:12:36` tuple whose element 0 is a type parameter (**blocker 6**) |
| `helpers/sort/driftQuicksort.maxon` | E3001 — ⛔ **VACUOUS**, extension-bodied |
| `helpers/sort/driftsort.maxon` | E3001 — ⛔ **VACUOUS**, extension-bodied |
| `helpers/sort/insertionSort.maxon` | E3001 — ⛔ **VACUOUS**, injection control run and BLIND |
| `helpers/sort/mergeSort.maxon` | E3001 — ⛔ **VACUOUS**, extension-bodied |
| `helpers/sort/pdqsort.maxon` | E3001 — ⛔ **VACUOUS**, extension-bodied |
| `helpers/sort/smallSort.maxon` | E3001 — ⛔ **VACUOUS**, extension-bodied |
| `helpers/string/unicodeCategory.maxon` | E2053 `:55:44` second and later arguments must be named |
| `helpers/string/views.maxon` | E3005 via `Interfaces.maxon:223` `Cannot return 'unknown'` (**`S2r`**) |

---

## Full probe table — 12 unlisted, 2026-08-12

**MEASURED on `cb307d7f69`**, one `maxon-shv2 build stdlib/<m>.maxon` per module, logs in
`temp/stdlibprobe/*.log`. **`StdlibLoader.whitelistedStdlibModules` now lists 37 of the 50 files.**
`Internals.maxon` is the 13th unlisted file and is **EXCLUDED PERMANENTLY** (§H, user ruling
2026-08-05), so **12 is the actionable remainder.**

⚠ **A first error is a LOWER BOUND on a module's ladder, never its depth.** shv2 reports one error per
file here — every row is *"at least this"*, and only the `E3001` rows have a known depth.

| module | first diagnostic | class |
|---|---|---|
| `Set.maxon` | `E3001` only | ⚠ **READY BUT INERT** — `Set` is SYNTHESIZED (26 refs); `W63` listed-built-withdrew and measured it |
| `Vector.maxon` | `E3001` only | ⚠ **READY BUT INERT** — `Vector` is SYNTHESIZED (45 refs) |
| `helpers/string/unicodeCategory.maxon` | ✅ **LISTED AND LIVE** | ✅ **DONE — this row's "inert?" reading was refuted by §1 below and is now settled by code.** `W115` listed BOTH this module and `CharacterSet.maxon` (`StdlibLoader.whitelistedStdlibModules`) and `W129` deleted the `__ucd_cat` synthesis it was said to be shadowed by, so `unicodeGeneralCategory` is the ONE implementation, reached through the surviving raw table loads `__ucd_bmp_at`/`__ucd_supp_at` |
| `Json.maxon` | `E2015 :281:3` field access through `doc`, a struct-typed field | **⇒ `W66`** — a legal program refused, oracle compiles it |
| `List.maxon` | `E3086 :21:10` field `chain` not initialized, no default | field default / recursive field |
| `CharacterSet.maxon` | `E2004 :31:33` Undefined constant `CharacterSet` | ⚠ **MOVED** from `E2010 :31:9` — `CharacterSet` is a compiler-owned builtin type name (it appears in `E2015`'s own member-carrying list) |
| `PrimitiveExtensions.maxon` | `E2010 :2:11` Expected identifier but got `int` | `extension int implements …` — extending a PRIMITIVE does not parse |
| `Subprocess.maxon` | `E2015 :357:25` overloading `Subprocess.run`, one declaration `throws` | ⚖ ruling-grade — the refusal states a design position |
| `Testing.maxon` | `E2051 :34:13` `__TestReport` reserved | the `__`-prefix door; no collision with a compiler name |
| `TcpClient.maxon` | `E3004 :23:18` no `__ManagedSocket.tcpConnect` intrinsic | ⚠ **MOVED** from `E2053 :23:70` (that ruling landed at `W64`) — now Workstream R, a runtime slice |
| `HttpClient.maxon` | `E2015 :176:18` member access `send` on an `unknown` value | behind `TcpClient` |
| `helpers/http/httpHelpers.maxon` | `E3011 :4:9` Unknown type `HttpMethod` | behind `HttpClient` |

### ⭐⭐ THE HEADLINE IS THE THREE `E3001` ROWS, AND IT IS NOT GOOD NEWS

The readiness criterion this file already calls VACUOUS for the sort family is vacuous **a second way**,
and the two are different: §"THE READINESS CRITERION IS VACUOUS" is about a body nothing *analyzes*;
this is about a name the compiler *synthesizes*. All three rows above pass the probe **and the injection
control**, and all three would be **INERT** — the listed declaration is never consulted, because the
synthesized type wins silently. `stdlib/Map.maxon` already shipped this way (`W43` listed it, `W52` says
the actual job was never begun).

⇒ **The listed count of 37 overstates the working cone**, and the check that separates the two is
neither the probe nor the injection control but the **DIFFERING-DECLARATIONS control**: change what the
module ANSWERS and see whether the program's answer or its emitted bytes move. If nothing moves, the
entry is inert. *(Measured for `Set` at `BATCH36`: sabotaged `count()`, program byte-identical at 5,225
bytes.)*

### The honest remaining shape

| kind | modules | note |
|---|---|---|
| **Reconciliation chains** (retirements, not listings) | `Set` (`W8`) · `Map` (`W52`) · `Vector` | each is an `ARR0…ARR4`-shaped chain, not a whitelist line |
| **Runtime slice** | `TcpClient` → `HttpClient` → `httpHelpers` | Workstream R; needs socket intrinsics |
| **Ruling** | `Subprocess` | throwing overload |
| **Ordinary compiler gaps** | `Json` (`W66`) · `List` · `CharacterSet` · `PrimitiveExtensions` · `Testing` | the tractable five |

---

## Full probe table — 11 actionable, 2026-08-13

**MEASURED on `08c0d6e83b`** (`main`, clean), **both binaries rebuilt immediately before the sweep** —
`build csharp` then `build shv2` — one `maxon-shv2 build stdlib/<m>.maxon` per unlisted module, logs in
`temp/stdlib-probe/*.log`. **Re-measured from scratch, not edited from the table above**, which is this
file's standing rule and is what caught both moves below.

`whitelistedStdlibModules` now makes **38 `listWhitelistedModule` calls** — count the calls, never a
prose number. **50 − 38 = 12 unlisted; `Internals.maxon` is excluded permanently ⇒ 11 actionable.**
`Json.maxon` is the one that left the list (`W69`).

| module | first diagnostic | class |
|---|---|---|
| `Set.maxon` | `E3001` only | ⚠ **READY BUT INERT** — unchanged; `Set` is synthesized |
| `Vector.maxon` | `E3001` only | ⚠ **READY BUT INERT** — unchanged; `Vector` is synthesized |
| `helpers/string/unicodeCategory.maxon` | `E3001` only | ⚠ **READY BUT INERT?** — unchanged; its only stdlib consumer is `CharacterSet.maxon`, itself unlisted |
| **`Testing.maxon`** | **`E3001` only** | ⭐ **MOVED** from `E2051 :34:13` `__TestReport` reserved — **three doors, three consecutive rungs**: `W72` the `__` declaration door, `W74` the per-member parameter default, `W77` the agreeing-`throws` overload set at `:182:25`. **The only module in this table whose probe improved into readiness**, and `Testing` is synthesized NOWHERE in the compiler (`grep "\"Testing\""` over `Compiler/` and `Runtime/` is empty), so unlike the three rows above it is not inert *by that mechanism*. ⇒ **the one candidate listing available today** — still owes `StdlibLoader`'s collision checklist AND the differing-declarations control before anyone calls it a line of work |
| `List.maxon` | `E3086 :21:10` field `chain` not initialized, no default | unchanged |
| `CharacterSet.maxon` | `E2004 :31:33` Undefined constant `CharacterSet` | unchanged |
| `PrimitiveExtensions.maxon` | `E2010 :2:11` Expected identifier but got `int` | unchanged — `extension int` does not parse |
| **`Subprocess.maxon`** | **`E2053 :387:72`** the second and later arguments must be named | ⭐ **MOVED** from `E2015 :357:25` (overloading `run`, one declaration `throws`) — **the ruling-grade blocker is GONE**, cleared by `W77` exactly as that row predicted. The new site is a 15-argument positional `__Builtins.subprocessSpawn(…)`: a CALL-SYNTAX divergence (§F), and a first error is a LOWER BOUND — whether `subprocessSpawn` exists as an shv2 intrinsic at all is behind it and unmeasured | ⭐⭐ **SUPERSEDED 2026-08-19: THE `E2053` IS GONE AND IT WAS A COMPILER DEFECT, NOT A CALL-SYNTAX DIVERGENCE.** shv2 demanded `name:` labels an intrinsic has no parameter names to carry, so **no spelling of that shared file could satisfy it** — the bootstrap, reading the identical bytes, has no labelled spelling of an intrinsic call AT ALL (`__Builtins.subprocessWaitCollect(-1, timeoutMs: 0)` → `E2004: Undefined variable 'timeoutMs'`) and compiles the positional form clean. Fixed in the compiler (`Parser.argLabelRuleForCallee`), not in the corpus. ⚠ Also: the call site takes **fourteen** arguments, not fifteen. **The first blocker is now `E3004 :387:27` — `__Builtins.subprocessSpawn` does not exist** — which is this row's own predicted next reading and confirms the RUNTIME-SLICE classification below rather than the ordinary-gap one
| `TcpClient.maxon` | `E3004 :23:18` no `__ManagedSocket.tcpConnect` intrinsic | unchanged |
| `HttpClient.maxon` | `E2015 :176:18` member access `send` on an `unknown` value | unchanged — behind `TcpClient` |
| `helpers/http/httpHelpers.maxon` | `E3011 :4:9` Unknown type `HttpMethod` | unchanged — behind `HttpClient` |

### ⚠ THE TABLE WENT STALE IN A DAY, AND NOT BECAUSE THE MOVES WERE UNPREDICTABLE — NOBODY REFRESHED IT

Every one of the three changes was **named in advance by the rung that made it**, which is the
uncomfortable part. `W69`'s row is *"list `stdlib/Json.maxon`"*. `W77`'s row says outright *"it is the
CURRENT first blocker of TWO modules at once — `Testing.maxon:182:25` and `Subprocess.maxon:357:25`.
Neither may be LISTED by this rung; **re-probe both and report their next blockers verbatim**"* — and
`Testing`'s two earlier doors were the stated payoff of `W72` (*"the real blocker behind
stdlib/Testing.maxon"*) and `W74` (*"Testing.maxon's blocker"*) before it. **Four consecutive rungs
worked this table's rows and none of them wrote back into it.**

⇒ this file is a **DATED MEASUREMENT that no rung updates as a side effect**, so the rule is not "trust
the newest table" but **re-probe before planning against any row**, every time. *(`Testing` alone took
`W72` → `W74` → `W77` to reach `E3001`: a first error is a lower bound, and clearing one door only
reveals the next.)*

⭐ **And the moves went the direction the classes predict, which is the useful part:** the rows that
moved were the *ruling* and the *ordinary gaps* — the classes an ordinary compiler rung clears, and
`Testing`'s three doors were each cleared by a rung whose subject was a language rule, not a module.
**Nothing moved in the retirement chains or the runtime slice**, and nothing will until someone works
them directly: no amount of adjacent rung traffic retires a synthesized `Set` or mints a socket
intrinsic. ⇒ **the four ordinary gaps will keep falling out of unrelated rungs; the four chains and the
runtime slice will not.**

### The honest remaining shape — 2026-08-13

| kind | modules | note |
|---|---|---|
| **Reconciliation chains** (retirements, not listings) | `Set` (`W8`) · `Map` (`W52`) · `Vector` | unchanged, and **`Map` is already LISTED and inert**, so the chain count exceeds the unlisted count. `HashTableRuntime` is shared by `Map` and `Set` ⇒ those two cannot run in parallel |
| **Runtime slice** | `TcpClient` → `HttpClient` → `httpHelpers` | unchanged; Workstream R, socket intrinsics |
| **Ordinary compiler gaps** | `List` · `CharacterSet` · `PrimitiveExtensions` · `Subprocess` | **`Subprocess` moved INTO this class**; `Json` left it (listed at `W69`) |
| **Candidate listing** | `Testing` | probes clean and is not synthesized — the only row that is plausibly a whitelist line rather than a rung |

⚠ **11 is not 11 units of work, and it is wrong in both directions.** It OVERSTATES — three of the
eleven are inert-if-listed and need chains, and a fourth chain (`Map`) is not in the eleven at all
because it already shipped listed. It UNDERSTATES — every non-`E3001` row is a first error, i.e. a lower
bound on that module's ladder.

---

## ⛔ THE CLASSES WERE WRONG — RE-MEASURED 2026-08-13 (second sweep, same day, `08c0d6e83b`)

**Both binaries rebuilt again**, all 11 actionable modules re-probed (logs in `temp/probe-0813b/`).
**Every FIRST DIAGNOSTIC above reproduced verbatim — not one row moved.** What moved is the
**classification**, and it moved for three of the four modules the table above calls *"ordinary compiler
gaps … the classes an ordinary compiler rung clears"*.

⇒ **that sentence was the useful-looking part of the table and it was the wrong part.** The row above
predicts *"the four ordinary gaps will keep falling out of unrelated rungs"*. **Three of the four cannot**,
for exactly the reason the same table gives for the chains: no amount of adjacent rung traffic retires a
synthesized name or mints an intrinsic.

| module | table above says | MEASURED | the evidence |
|---|---|---|---|
| `List.maxon` | ordinary gap (`E3086`) | ⛔ **SYNTHESIZED TWIN — a chain, like `Set`/`Vector`** | `SignatureIndex.maxon:4002` `export let ListBuiltinBaseName = b"List"`. Clearing `E3086` would buy a **listed and inert** entry, which is `W8`'s hazard exactly |
| `Subprocess.maxon` | ordinary gap (`E2053`) | ⛔ **RUNTIME SLICE — no intrinsic exists** | shv2's whole `__Builtins.*` set is 14 names (`bitsToFloat` `commandLineArg` `commandLineCount` `currentTimeMs` `currentTimeNanos` `currentUnixTimeSeconds` `executablePath` `floatToBits` `readStdin` `sleep` `ucdByteAt` `ucdI64At` `writeStderr` `writeStdout`). **`subprocessSpawn` is not among them, nor `subprocessGetPid`, nor `subprocessWaitCollect`.** Naming the 15 arguments per `W64`'s ruling MOVES the error to `E3004`; it does not clear it |
| `CharacterSet.maxon` | ordinary gap (`E2004`) | ⛔ **CONVERGENCE — a compiler-owned name, like `String`/`Character`** | `SignatureIndex.maxon:4509` `CharacterSetBuiltinName = b"CharacterSet"` and `:5279` `CharacterMemberSetAliasName = b"CharSet"`, both in `TypeResolution.isCompilerOwnedTypeName:986`. The module declares **both** names (`:19` `typealias CharSet`, `:22` `type CharacterSet`) |
| `PrimitiveExtensions.maxon` | ordinary gap (`E2010`) | ✅ **CORRECT — the only one** | `extension int implements …` does not parse. A parser feature, `L-parser-decl`, and nothing else is behind that door that has been measured |

### ⭐ `stdlib/Testing.maxon` — READINESS CONFIRMED AGAINST BOTH CONTROLS, AND ONE OF THEM HAD TO MOVE

The table above calls it *"the one candidate listing available today"* and correctly says it still owed
the two controls. **Both were run. Both pass:**

- **NO SYNTHESIZED TWIN.** `grep 'b"Testing"' / 'b"Expect"' / 'b"TestFailure"' / 'b"__TestReport"'` over
  `maxon-shv2/Compiler/` is **empty**, and none of the seven `*BuiltinBaseName` roots is any of them.
- **THE INJECTION CONTROL FIRES** — `let bogus = NoSuchType.definitelyUndefined(1)` in
  `__TestReport.threw`'s body answers `E3001` **+ `E3004 … 'NoSuchType.definitelyUndefined'`** at
  `stdlib/Testing.maxon:53:15`. Its bodies are analyzed; this is not the `helpers/sort/*` blindness.

⚠⚠ **BUT THE CONTROL AS THIS FILE PRESCRIBES IT — *"copy the module outside `stdlib/`"* — CANNOT BE RUN
ON THIS MODULE, AND ANSWERS THE WRONG QUESTION IF YOU TRY.** The copy dies at
`E2051: identifier '__TestReport' is reserved` **before reaching any body**, because `W72`'s door admits a
`__` declaration only under `stdlib/`. A reader following the recipe verbatim gets a refusal that has
nothing to do with the injection and everything to do with the copy's PATH. ⇒ **for a module declaring a
`__` name, inject IN PLACE and restore** (`cp` a backup first; `git status stdlib/` after). The recipe in
"Reproducing this table" is correct for every other module and wrong for this one.

### The honest remaining shape — 2026-08-13, second sweep

| kind | modules | can an unrelated rung clear it? |
|---|---|---|
| **Candidate listing** | `Testing` | — it is ready NOW, and it is a rung of its own (`W81`) |
| **Ordinary compiler gap** | `PrimitiveExtensions` | ✅ yes — `extension int` is a parser feature |
| **Synthesized-twin chains** | `Set` · `Vector` · `List` · (`Map`, already listed and inert) | ⛔ no |
| **Inert-if-listed** | ~~`unicodeCategory`~~ — **CLASS NOW EMPTY** | ✅ cleared: listed at `W115`, and the `__ucd_cat` synthesis this row rested on deleted at `W129` |
| **Convergence** (compiler-owned name) | ~~`CharacterSet`~~ — **CLASS NOW EMPTY** | ✅ cleared: listed at `W115`; the compiler-owned `__CharacterSet` layout and the whole `__cs_*` runtime deleted at `W129`, leaving only the NAME reservation |
| **Runtime slices** (intrinsics that do not exist) | `Subprocess` · `TcpClient` → `HttpClient` → `httpHelpers` | ⛔ no |

⚠ **THE COUNT BELOW IS DATED 2026-08-13 AND HAS NOT BEEN RE-CENSUSED (noted at W129's review, 2026-08-17).**
Two of the four workstream classes above have since emptied, so *"four workstreams"* and *"NINE modules"* are
both high; `PLAN.md` and the memory index carry the live remainder. Re-census before planning off this line.

⇒ **the remaining bring-up is ONE listing, ONE parser feature, and NINE modules behind four workstreams
that only direct work moves.** The optimistic reading — *"four ordinary gaps will keep falling out"* —
survives for exactly one module.

---

## ⛔⛔ AND THE LAST "ORDINARY GAP" IS NOT ONE EITHER — MEASURED 2026-08-13, THIRD SWEEP, AFTER `W81`

The section above corrected three of the four "ordinary compiler gaps" and let the fourth stand:
*"`PrimitiveExtensions` … ✅ **CORRECT — the only one**"*. **That was wrong too, and it was wrong in the
way this file keeps warning about: I classified it from its FIRST DIAGNOSTIC and never asked the
synthesized-twin question.**

`stdlib/PrimitiveExtensions.maxon` is three `extension <primitive> implements …` blocks — `int` (`:2`),
`float` (`:30`), `bool` (`:75`) — declaring `Hashable`, `Equatable`, `Comparable`, `Stringable`,
`Cloneable`. Its first blocker is real (`E2010 :2:11`, `extension int` does not parse). **Behind it is a
synthesized twin, and the compiler says so in its own source:**

> `Compiler/ConformanceCheck.maxon:780` — *"An intrinsic builtin-type conformance (`int`/`String`
> implements `Hashable`/`Equatable`, **synthesized natively**) ORed with the user-declared registry — so
> 'int conforms to Hashable' is decided in …"*

**MEASURED behaviourally, on `0bd44d3641` with the module UNLISTED**, which is the whole point:

```
let a = 7   a.equals(b) => false    a.hash() => 7    a.toString() => "7"    a.clone() => 7
let f = 2.5 f.equals(f) => true     f.toString() => "2.5"
let t = true                        t.toString() => "true"
```

**Every member the module declares already resolves and runs without it.** ⇒ listing it would put a
SECOND declaration of facts the compiler already synthesizes into the program, and the synthesized one
wins silently — `W63`/`BATCH36`'s hazard exactly. **The `extension int` parse gap is the FIRST blocker,
not the LAST one**, and clearing it alone would buy an inert entry.

### ⇒ THE BRING-UP HAS NO ORDINARY COMPILER GAPS LEFT. NONE.

| kind | modules | can an unrelated rung clear it? |
|---|---|---|
| **Synthesized-twin chains** | `Set` (`W8`) · `Vector` · `List` · `PrimitiveExtensions` · (`Map` `W52`, already listed and inert) | ⛔ no |
| **Inert-if-listed** | `unicodeCategory` | ⛔ no |
| **Convergence** (compiler-owned name) | `CharacterSet` | ⛔ no |
| **Runtime slices** (intrinsics that do not exist) | `Subprocess` · `TcpClient` → `HttpClient` → `httpHelpers` | ⛔ no |

**10 actionable modules, and not one of them is a diagnostic away from listing.** The earlier reading —
*"the four ordinary gaps will keep falling out of unrelated rungs"* — is now **zero**: `Json` and
`Testing` were the two that really were ordinary, and both have LANDED (`W69`, `W81`). What is left is
the residue, and residue does not fall out of anything.

⚠ **THE LESSON THIS FILE KEEPS RE-LEARNING, NOW AT THREE DIFFERENT DEPTHS.** A module's class is not its
first diagnostic. It takes THREE questions, and the tree has been bitten by each in turn: *(1) does it
compile?* (the probe) — *(2) are its bodies ANALYZED?* (the injection control, which the six
`helpers/sort/*` files fail) — *(3) does the compiler already SYNTHESIZE what it declares?* (the
differing-declarations control, which `Set`, `Vector`, `Map`, `unicodeCategory` and now
`PrimitiveExtensions` fail). **A row that answers only (1) is a guess wearing a measurement's clothes**,
and four of my own rows this morning were exactly that.

---

## ⛔ `Map` IS NOT INERT — IT IS LIVE, AND THIS FILE HAS SAID OTHERWISE TWICE

**MEASURED 2026-08-13 on `8e9bf119ff`**, after `S2u`, with the differing-declarations control this file
demands — both halves, in place, restored after (`git status stdlib/` clean):

| control | result |
|---|---|
| undefined call injected into `stdlib/Map.maxon`'s `count()` | **`E3004` at `stdlib/Map.maxon:170:26`** — the corpus body IS analyzed |
| `Map.insert` called without `try` | **`E3057 … 'stdlib.Map.insert'`** — the corpus module's own throwing signature is what the call is checked against |
| `count()` rewritten to `return count + 100` | program prints **`count=102`** — the corpus body's arithmetic reaches the ANSWER |

⇒ **`stdlib/Map.maxon` is listed AND live.** It is not the `Set` case and it never was: `W63`/`BATCH36`
measured `Set` inert (byte-identical, sabotage-proof) and measured `Map` **live** in the same review, and
that review said so plainly — *"Map's module is LIVE; Set's is not. Whatever still calls `Map`
synthesized, it is not the same relationship."*

**This file then wrote the opposite, twice**: *"`Map` is already LISTED and inert"* in the 2026-08-13
shape table, and *"(`Map`, already listed and inert)"* in the correction below it. **Both are wrong, and
the correction repeated the error it was written to fix** — which is the exact failure mode this file
exists to catch, committed by the file itself.

### What IS missing on `Map`, measured rather than inferred

`NumMap from ["a": 1, "b": 2]` gives **`E2004: Undefined variable 'NumMap'`** — the
`BuiltinDictionaryLiteral` path does not reach. That much is measured.

⛔ **AND I FIRST WROTE THAT THIS MAKES `W52` "one conformance, not a retirement". THAT WAS WRONG, IN THE
EXACT WAY THIS SECTION IS ABOUT, AND IT IS RETRACTED HERE.** I inferred the size of what is left from a
surface that works, without reading the rest of `W52`'s own row — which names the real work behind the
conformance: `SignatureIndex.genericInstanceBoxSize` answers `MapRecordBytes` (64) from its
`isMapInstance` arm **before any declared layout is consulted**, so a corpus `Map` and the fused record
can disagree about FIELD OFFSETS **with no diagnostic** — `StdlibLoader.maxon` calls this the one
collision shape that is SILENT. Plus **133 references across 12 files**, a `HashTableRuntime` **shared
with `Set`**, and map literals and `for k, v in map` both needing to re-point at the corpus type.

⚠ **MY `count=102` DOES NOT RULE THAT OUT.** It proves the corpus body runs and its arithmetic reaches
the answer; it says nothing about whether every field sits where the other reader thinks it does. A
silent offset collision is precisely the defect that a passing read cannot disprove. ⇒ **`Map` is LIVE
and `W52` is a full retirement rung. Both are true, and I briefly wrote that the second followed from
the first.**

### ⛔⛔ AND BOTH OF MY `Map` CLAIMS ABOVE ARE WRONG — RETRACTED 2026-08-13 BY `W52`, WHICH RAN THE CONTROLS

**(1) THE DICTIONARY-LITERAL "BLOCKER" WAS MY OWN PROBE ARTIFACT.** I wrote that `NumMap from ["a": 1, "b": 2]`
giving `E2004: Undefined variable 'NumMap'` shows *"the `BuiltinDictionaryLiteral` path does not reach"*.
**There is no dictionary-literal `from` form in the language at all.** `from` is the ARRAY-literal door;
a map is built with `[k: v]`, which **works** (`map/literal.basic` and 40+ siblings pass). Measured:
`Point from [1, 2]` and `String from [1, 2]` give the **identical** `E2004: Undefined variable '<X>'`, so
the noun is general to every type without a `from` door — nothing to do with `Map`. The oracle refuses
the same program too (`E3005: Type 'NumMap' does not conform to BuiltinArrayLiteral or
InitableFromArrayLiteral`). ⇒ **`BuiltinDictionaryLiteral` is fixed and has been**, exactly as `W52`'s row
prescribed (`ConformanceCheck.maxon:2117-2138`, gating on the spelling carried from `parseInterfaceMethod`'s
tokens). **I invented a blocker by mis-spelling a construct and then reasoned from the error I got.**

**(2) "`W52` IS A FULL RETIREMENT RUNG" IS ALSO WRONG — THE RETIREMENT HAD ALREADY LANDED.**
`SignatureIndex.isMapBaseName` routes through `structOf(name)` and answers **false** for a name the corpus
declares, so with `stdlib/Map.maxon` listed the synthesized regime is **unreachable**: the fused
`MapRecordBytes` record is never built, and the *silent field-offset disagreement* the row called "the real
work" **cannot occur, because the second reader does not exist.** It landed in `4c9afeba92` + the `W41`
slice, after `W43` closed ◑ PARTIAL — so the row never learned of it. ⭐ **Verified with a POSITIVE CONTROL,
twice, by two agents**: a panic wired into that predicate's TRUE arm reads **0 hits across 5,812 compiles**,
and the same probe with `Map.maxon` de-listed **fires instantly** at `parseMapLiteralBody`. Plus **0 of
5,825** x64-windows fragments emit a `__map_*` op. ⭐ And the synthesized `Map` is **unusable BY
CONSTRUCTION**, not merely unused: it takes its error ordinals from a *declared* `MapError`, and
`grep -rn "enum MapError"` finds **exactly one** — `stdlib/Map.maxon:6`, the module whose absence would
select it.

⇒ **`Map` is DONE.** Listed, live, corpus-served, and its builtin twin is unreachable. What remains is
**dead code** (172 lines across 15 files, 78 of them actual code), deliberately left for a combined
`Map`+`Set` rung because ~16 `carriesValues` branch sites live in the `HashTableRuntime.maxon` that `Set`
still executes — **volume was never the argument, and an earlier count of "385 lines / 22 files" was also
wrong.**

⚠⚠ **THAT IS FOUR CLASSIFICATION ERRORS ON THIS FILE IN ONE DAY, THREE OF THEM MINE, AND EVERY ONE CAME
FROM REASONING WHERE A CONTROL WAS AVAILABLE.** The controls cost two builds. **Run them.**

### The remaining shape, corrected again

| kind | modules | change |
|---|---|---|
| **Synthesized-twin chains** | `Set` · `Vector` · `List` · `PrimitiveExtensions` | **`Map` REMOVED** — it is live |
| **Listed but incomplete** | `Map` (dictionary literals only, `W52`) | **new class** — a module can be neither unlisted nor finished |
| **Inert-if-listed** | `unicodeCategory` | unchanged |
| **Convergence** | `CharacterSet` | unchanged |
| **Runtime slices** | `Subprocess` · `TcpClient` → `HttpClient` → `httpHelpers` | unchanged |

⚠ **THE LESSON, AND IT IS THE THIRD TIME TODAY:** every one of this file's classification errors has come
from **inheriting a class rather than re-running the control**. The controls are cheap — an injection and
a `+ 100` are two builds — and each time one was actually run it moved a module between classes.
**A module's class is a MEASUREMENT with a date, never a property.**

---

## ✅ `Set` IS RETIRED — 2026-08-14, `W90` (+`W95`), main `4db9343b88`

**Written back BY the rung that did it**, because this file's standing complaint is that four consecutive
rungs worked its rows and none of them wrote back. That is the whole reason it keeps going stale.

`stdlib/Set.maxon` is **LISTED AND LIVE**. The differing-declarations control this file demands, run in
both directions: `count()` sabotaged to `return count + 100` ⇒ the program answers **102**; restored ⇒
**2**, `git status stdlib/` clean. **Before the rung it answered 2 EITHER WAY at a byte-identical 6,077
code bytes** — that Δ0 was the RED. Code size 6,077 → 11,795. `Set` leaves the synthesized-twin class.

### The remaining shape — 9 actionable modules

⛔ **SUPERSEDED 2026-08-20** — eight of these nine have since landed or been retired; see the final
section. Kept for its dated reading and for the `Set` control below it.

| kind | modules | can an unrelated rung clear it? |
|---|---|---|
| **Synthesized-twin chains** | ~~`Set`~~ ✅ · `Vector` · `List` · `PrimitiveExtensions` | ⛔ no |
| **Inert-if-listed** | `unicodeCategory` | ⛔ no |
| **Convergence** (compiler-owned name) | `CharacterSet` | ⛔ no |
| **Runtime slices** (intrinsics that do not exist) | `Subprocess` · `TcpClient` → `HttpClient` → `httpHelpers` | ⛔ no |
| **Listed but incomplete** | — (`Map` closed at `W52`) | — |

⭐ **`CharacterSet` GOT CHEAPER, AND IT WAS NOT PREDICTED HERE.** `W90`'s row once ordered `CharacterSet`
*before* `Set` on the belief that a private member store was needed. `W96` refuted it and `W90` proved it:
`setInstanceForCharacter()` already interns `Set with Character` through the same interner a user
`typealias CharSet = Set with Character` folds into, so **only the CALLEES moved** — six sites, two cuts.
The convergence that remains for `CharacterSet` is the NAME (`CharacterSetBuiltinName`/`CharSet` in
`isCompilerOwnedTypeName`), not the member store.

### ⚠ What retiring a container now COSTS, measured — read this before the next chain

**Any program whose stdlib cone reaches a `String` trim now compiles four corpus `Set` bodies plus
`__fieldDefault`/`spreadHash`** — measured by roster-diffing goldens, not by line counts: `url` 42/42,
`directory` 9/9, `file-io` 7/7, but `async-await` 33/47 and `process-executable-path` 4/7. The partial
families are the evidence that this is REACHABILITY and not a blanket emit. ⇒ **each remaining
retirement adds its own bodies to unrelated programs**, and the payback is the DELETION half
(`installSetRuntime`, `SetRuntime.maxon`, the `Map` half of `HashTableRuntime.maxon` — now dead weight,
and `W93` is unblocked). **Recount the residue after a deletion, never before.**

⚖ **AND A STANDING RULING NOW APPLIES TO EVERY REMAINING CHAIN** (user, 2026-08-14): **E3019 is a
BUILTIN-SURFACE rule and a declared type is exempt**, so a retirement DROPS the immutable-receiver
refusal by design. `Set` cost 3 cases, rewritten as value-asserting `ok` cases. `Vector`, `List`,
`PrimitiveExtensions` — and eventually `Array`/`String` — will each do the same. **That is not a
regression to file; it is the ruling arriving.**

## ✅ THE `Set`/`Map` RUNTIME IS DELETED, AND A SHARED HELPER IS LISTED — 2026-08-14, `W105` + `W93`

**Written back BY the rung that did it**, on this file's standing rule.

⭐ **THE DELETION HALF (`W105`) IS THE PAYBACK THE `Set` SECTION ABOVE PREDICTED, AND THE RESIDUE IT SAID
TO RECOUNT AFTERWARDS IS 1,745 LINES, NOT 172.** `Runtime/SetRuntime.maxon` (218) +
`Runtime/MapRuntime.maxon` (383) + `Runtime/HashTableRuntime.maxon` (1,144) are gone, together with the
whole synthesized regime they served — the shared retirement switch
(`isUnretiredBuiltinBaseName`/`isSetBaseName`/`isMapBaseName` and the three instance predicates over
them), the parser's builtin `Set`/`Map` surfaces, the hash-table key roster and the `MapError` ordinal
claim. Net **−2,569 lines**.

⇒ **`Set` AND `Map` HAVE NO SYNTHESIZED TWIN AT ALL NOW, WHICH IS A STRONGER STATE THAN "RETIRED".** A
retirement means the declaration WINS a contest; this means there is no contest. Un-listing either module
no longer falls back to a builtin — it removes the type.

⚠ **UNREACHABILITY WAS MEASURED WITH BOTH HALVES OF `W52`'s INSTRUMENT.** A `panic` in the switch's TRUE
arm read **0 hits across 5,890 compiles**; the same probe with the module de-listed fires on the FIRST
`typealias` naming the base. The acceptance was BYTE-IDENTITY, not a green suite: **0 of 5,890 committed
x64-windows goldens moved.**

⭐ **THE LISTING HALF (`W93`) IS A NEW SHARED MODULE, `helpers/hashtable/slotScan.maxon`.** `Map.maxon` and
`Set.maxon` each declared a three-case slot-state enum and a byte-identical `findNextOccupied` scan over
it. **The hazard was ASYMMETRIC**: a fourth state is a compile error at one file's `match`es and SILENCE at
the other's. One enum now serves both.

Its readiness was checked against this file's three questions, each by a control rather than a reading:
*(1) does it compile?* yes; *(2) are its bodies ANALYZED?* — an undefined call injected into
`findNextOccupied` answers **`E3004 … 'NoSuchType.definitelyUndefined'` at `slotScan.maxon:54`**, so it is
not the `W63`/`BATCH36` inert-listing shape; *(3) does the compiler SYNTHESIZE what it declares?* no —
`SlotState` has no builtin twin and the scan no runtime one.

⚠ **AND THE CURE IS SABOTAGE-PROVEN IN BOTH DIRECTIONS.** With a fourth case added to the ONE enum, a
`Set`-only program fails at `Set.maxon:101` and a `Map`-only program at `Map.maxon:214` — the asymmetry
gone. With both containers' own `match`es widened to admit it, the remaining refusal is the ONE shared body
(`slotScan.maxon:62`).

⚠ **IT COST 10 GOLDEN FRAGMENTS, AND THE DIFF IS ENTIRELY POSITION AND NAME.** Eight are pure LINE
PERMUTATIONS (`findNextOccupied` now sits at a different point in the module, because the loader walks
`stdlib/` in enumeration order and the function changed file — the ordering property this file's loader
header already records); the other two additionally rename `findNextOccupiedSet` → `findNextOccupied`. Not
one instruction changed. `5890 passed, 0 failed` throughout.

### The remaining shape — 9 actionable modules, unchanged

⛔ **SUPERSEDED 2026-08-20** — see the final section.

`W105`/`W93` clear no bring-up row: `slotScan.maxon` is a NEW file rather than one of the 12, and the
deletion removes compiler code rather than unlisting anything. The table under *The remaining shape — 9
actionable modules* stands as written.

---

## ⛔ TWO CLASSIFICATIONS IN THIS FILE ARE REFUTED — 2026-08-14, by `W115`, which landed nothing

**Written back by the attempt that measured them**, per this file's own standing rule.

### 1. `unicodeCategory` IS NOT "inert-if-listed"

It is listed in the **Inert-if-listed** class on the ground that its one export is synthesized as
`__ucd_cat`. **MEASURED on a scaffolded convergence tree:** once `CharacterSet.maxon` is listed,
`CharacterSet.contains` calls `unicodeGeneralCategory`, and sabotaging that function's BMP arm to
`return 0` moves the program's answer from `[hello] true false` to **`[  hello  ] false false`**.
⇒ **it becomes LIVE.** `CharacterSet` is a **THREE-module cone** (`CharacterSet` +
`helpers/string/unicodeCategory`, retiring `__ucd_cat` with them), not one module.

### 2. `CharacterSet`'s first blocker is NOT the compiler-owned name — §"11 actionable" and `:528` are wrong

Both say `E2004 :31:33` is caused by `isCompilerOwnedTypeName` claiming `CharacterSet`/`CharSet`.
**CONTROL: `type Pair` — on NO reservation list — with `static let origin = Pair{a: 1, b: 2}` gives the
IDENTICAL `E2004: Undefined constant 'Pair'`, and the oracle compiles and runs it.** The real first
blocker is **a missing struct-literal arm in the top-level / lazy-static initializer evaluator**
(`W116`); the reservation is blocker **2**. A third, `W117`, needs a design ruling.

⇒ **`CharacterSet` is not a name retirement. It is a three-module cone behind two prerequisites that
have nothing to do with it**, and the sizing is measured: on a scaffolded tree the suite reads
**5878/19 against a 5897/0 baseline with 3555 of 5878 goldens differing.**

### ⭐ THE ONE PIECE OF GOOD NEWS, AND IT IS A FIRST

**The differing-declarations control PASSES for this cone** — two independent sabotages both move the
program's answer, and an injected undefined call proves the body is analyzed. **`W63` (`Set`), `W86` and
`W112` (`Vector`) all WITHDREW on this control.** This is the first remaining module whose listing would
buy something real.

### ⚠⚠ AND THE CONTROL ITSELF CAN READ FALSE — `W118`

`W115`'s first sabotage did not move the answer *and the executable was byte-identical*. The cause was
not inertness: **shv2 DISCARDS a `return` and runs the code after it** (no `E3071`, and the early return
is absent from the emitted IR). ⇒ **a sabotage placed before an existing `return` is silently
discarded, so this file's central instrument reads INERT on a module that is LIVE.** ~~Until `W118`
lands,~~ ✅ **`W118` CLOSED 2026-08-14** — a block's terminator lived in a SLOT rather than as the last
op, so `setTerminator` on an already-terminated block silently dropped the earlier one. The condition
is met; keep the practice anyway, because it costs nothing:
**place the sabotage where no earlier `return` precedes it.**

---

# ✅✅ `Vector` IS FINISHED AND THE REMAINDER IS ONE CONE — 2026-08-20, `W190`, main `ce302984b3`

**Written back by the refresh that measured it, on this file's standing rule** — and it was six days
overdue: nothing had refreshed the tables above since 2026-08-14, while `W153` (`List`), `W188`
(`PrimitiveExtensions`), `W189` + `W190` (`Vector`) and the attached-subprocess runtime (`Subprocess`)
all landed. **Every number below is one command on `ce302984b3`, clean, with BOTH binaries rebuilt
immediately before the sweep** (`build csharp` → `build shv2`).

## The census — counted, not scraped

```
find stdlib -name '*.maxon' | wc -l                                  ⇒ 52
grep -c '^\s*try listWhitelistedModule' Compiler/StdlibLoader.maxon  ⇒ 48
```

**52 − 48 = 4 unlisted; `Internals.maxon` is EXCLUDED PERMANENTLY (§H) ⇒ 3 ACTIONABLE, AND ALL THREE
ARE ONE CONE.** ⚠ Count the CALLS: `Builtins.maxon` is listed through the constant
`CompilerInternalDeclaringModule`, so every argument scrape ever run against this loader has missed it
(`W131` made that mistake three times).

| module | first diagnostic, re-probed today | class |
|---|---|---|
| `TcpClient.maxon` | `E3004 :23:34` — `__ManagedSocket.tcpConnect`, no intrinsic of that name exists | **runtime slice** |
| `HttpClient.maxon` | `E2015 :176:18` — member access `send` on an `unknown` value | behind `TcpClient` |
| `helpers/http/httpHelpers.maxon` | `E3011 :4:9` — Unknown type `HttpMethod` | behind `HttpClient` |
| `Internals.maxon` | `E2051 :26:10` — `__mm_incref` is reserved | **EXCLUDED**, user ruling 2026-08-05 |

⚠ **A first error is still a LOWER BOUND.** `HttpClient` and `httpHelpers` have never been measured
past `TcpClient`'s absence, so their rows say only *"at least this"*.

## `Vector`'s residue is ZERO — the first surface ever to reach it

`vectorSurfaceMemberNames`, `dispatchVectorMethod`, `elementAccessCallee`, `MemberSurface.vector`,
`vectorMethodMutatesReceiver` and `Runtime/VectorRuntime.maxon` are **DELETED**. Six roster functions
remain (`buffer` 19 · `cursor` 6 · `array` 9 · `string` 0 hand-written · `character` 3 ·
`stringIndex` 2) and `vector` is not among them; the 13 surviving mentions under `Compiler/` are
graves. **`ARR4`'s rule — *a roster is the RESIDUE of what could not retire* — has reached zero on a
surface for the first time.**

⭐ **THE DIFFERING-DECLARATIONS CONTROL, RUN HERE RATHER THAN INHERITED, ON ALL THREE MEMBERS A
PROGRAM CAN OBSERVE.** Injected IN PLACE against a backup, `git status stdlib/` empty afterwards, and
each sabotage placed where no earlier `return` precedes it:

| sabotage in `stdlib/Vector.maxon` | intact | sabotaged |
|---|---|---|
| `count()` ⇒ `return countof(Self) + 100` | `count=3` | **`count=103`** |
| `get` ⇒ `managed.get(index + 1000)` | `g0=7` | **`g0=-1`** (the `otherwise` arm) |
| `create()` ⇒ writes `42` into slot 1 | `g1=0` | **`g1=42`** |

⇒ **listed, live, and the declaration is the whole implementation.** (`W118` is CLOSED — 2026-08-14 —
so the condition on this file's closing warning is met; the placement rule under it costs nothing and
stays.)

### ⛔ THE PLANNED MECHANISM WAS WRONG, WAS BUILT BEFORE IT WAS DISBELIEVED, AND SO WAS ITS REPAIR

`W190`'s row prescribed a tenth layout-descriptor word `fixedElementCount@72`. It was built and then
MEASURED: a `Vector with 4 Element` field reached from inside `type Holder uses Element` answered
**exit 64 where 68 is the answer**, the capacity reading 0. `emitInstanceDescriptorAddr` FORWARDS the
caller's descriptor blocks when the callee's instance is written over the caller's parameters — sound
only because **every word in that blob is a fact about the type ARGUMENT, and a count is a fact about
the INSTANCE.** The row's own fallback (*"refuse the forward, minting is always correct"*) is
unreachable in exactly the case the forward fires, and `vector.md`'s already-green
`capacity-is-part-of-instance-identity` is that shape. ⇒ the count travels in its own hidden trailing
parameter, reserved by the same whole-program fixpoint over the same self-call graph, correct because
the count is a compile-time constant at every call site. **A briefed mechanism is a prediction; this
one was disbelieved by its own first measurement.**

### ⛔⛔ THE CONSTRAINT THIS RUNG FOUND THAT NOTHING IN THE TREE HAD WRITTEN DOWN — READ IT BEFORE THE NEXT RETIREMENT

**`stdlib/` IS COMPILED BY THE BOOTSTRAP, SO A NEW shv2 LANGUAGE FEATURE IS UNUSABLE BY THE CORPUS
UNTIL THE BOOTSTRAP IMPLEMENTS IT TOO.** Measured on the first build after the corpus was edited:

```
error E2004: stdlib/Vector.maxon:30:26: Undefined function 'countof'
    out of  ./bin/maxon.exe build maxon-shv2
```

`countof` is now in BOTH compilers (the bootstrap monomorphizes and folds it to a literal; shv2 passes
the parameter). ⇒ **every remaining retirement that needs a new spelling costs a bootstrap change
first**, and this file's three readiness questions do not ask about it. It is a fourth.

### The cost, both numbers, and what the rung filed

**5,802 → 6,761 code bytes (+16.5%)** on a program using all four retired members; **+6.4%** on image
for another — accepted by user ruling, not discovered. `Vector.count` went from a prologue +
`callDirect __managed_count` + epilogue to a `movRegReg`, which was **not** the argument for doing it.
Gate: shv2 **6441/0**, C# **3397/0**, no leak; ladder A/B'd like-for-like, no bend. Three rows filed:
**`W191`** (the deleted constant-index bounds-check elision, and why it must not come back as a
call-site rule), **`W192`** 🔴 (`count()` answers from the TYPE while `get` and the walk answer from the
RECORD — `managed.clear()` through an `extension Vector` gives `count=3 walked=0`), **`W193`** 🔴🔴
(pre-existing: `sizeof(Nope)` on an undeclared name compiles and answers 8, where the bootstrap
refuses).

## ⚠ LISTED IS NOT FINISHED — the residual rosters, counted today

The remaining bring-up is no longer mostly about the whitelist. **48 of 52 modules are listed; what is
left inside them is what the compiler still serves itself:**

| surface | members still compiler-served | note |
|---|--:|---|
| `Vector` | **0** | ✅ `W190` — the first zero |
| `String` | **0 hand-written** | the roster is DERIVED (`builtinConformerMethodNameList`); `W49`/`W55` emptied the hand-written half |
| `StringIndex` | 2 | `charIndex`, `bytePos` — no corpus module declares this type |
| `Character` | 3 | `byteView`, `byteLength`, `asciiValue` |
| `Array` | **9** | `managed` `get` `set` `first` `count` `push` `resize` `append` `appendMemory` — the `ARR3`/`ARR4` chain, and `push` alone is FIVE independent mechanisms |
| `__ManagedMemory` buffer · cursor | 19 · 6 | **BY DESIGN** — the raw primitive the corpus is written OVER, like `__ManagedList`/`__ManagedFile`. Not bring-up residue |

Two more that are not rosters:

- **`PrimitiveExtensions` retains `int.toString`** (`Parser.primitiveMembersTheCompilerRetains`, `W188`):
  the renderer reads the receiver's DECLARED RANGE and the corpus `self` erases it to a signed `i64`.
- **`Interfaces.maxon` owes TWO CONVERGENCE RUNGS**, both stated at its own entry
  (`StdlibLoader.maxon:1044-1075`): the ten protocol interfaces are **declared AND synthesized**, and
  `lookupInterface` answers from the synthesized copy first — so the parsed declarations are **INERT**,
  and moving that moves the WITNESS layout; and `HashValue`'s declared `int(0 to u32.max)` bounds are
  not enforced (it erases to `integer` through `isSynthesizedIntAliasName`), so `insertRangeChecks`
  guards no `hash()` return.

⚠⚠ **AND ONE LIVE WRONG ANSWER SITS INSIDE THE LISTED CONE — RE-MEASURED TODAY, BOTH COMPILERS.**
`W101`: two `Array with String` each holding `"a"` ⇒ shv2 **`equals=false`**, bootstrap **`equals=true`**.
`Array.hash`/`equals` are byte-identity, so a managed element satisfies `Hashable` **without its
semantics**. A retirement moves a member into the corpus; it does not make the member right.

⛔ **AND ONE CLAIM THIS FILE'S NEIGHBOURS CARRY IS STALE, CORRECTED HERE RATHER THAN LEFT TO ROT:**
`ARR3`'s *"`contains` COMPILES AND FAULTS (`0xC0000005`)"* **does not reproduce** —
`[10,20].contains(10)` answers `true`, and the closure form
`contains(function(x int) gives x == 10)` answers `true`. Whatever fixed it did not write back either.

## The remaining shape — 2026-08-20

| kind | what | can an unrelated rung clear it? |
|---|---|---|
| **Runtime slice** (the last listing) | `TcpClient` → `HttpClient` → `httpHelpers` | ⛔ no — no socket intrinsic exists |
| **Roster residue** | `Array` (9) · `Character` (3) · `StringIndex` (2) · `int.toString` | ⛔ no — each is a retirement with its own blockers |
| **Convergence** | `Interfaces.maxon`'s ten synthesized protocols · `HashValue`'s bounds | ⛔ no |
| **Defects inside a listed module** | `W101` · `W192` · `W193` | — ordinary rungs |
| **Excluded permanently** | `Internals.maxon` | — user ruling |

**Not one of these is a whitelist line, and there has been no "ordinary gap that falls out of an
unrelated rung" in this workstream since `Json` and `Testing` landed** — the third sweep's finding,
still holding six days and eight modules later.

## The socket slice, SIZED rather than predicted

```
grep -c ManagedSocket maxon-shv2/Compiler/Parser.maxon   ⇒  0
```

**The whole builtin type is absent — this is not one missing intrinsic.** What the two references
agree on, and what the corpus actually calls:

- **The type**: a single-i64-handle struct with a runtime-owned destructor that closes on drop —
  bootstrap `2-Parser.cs:1458-1464`, v1 `LowerMaxonToStd.registerManagedSocketType:2118`. It is the
  `__ManagedFile` handle mechanism `R4.1` already built in shv2, one type over.
- **The error enum** `__ManagedSocketError` (bootstrap `2-Parser.cs:1496-1500`).
- **Exactly four methods**, and `stdlib/TcpClient.maxon` calls all four and nothing else:
  `tcpConnect` (`:23`), `sendFrom` (`:43`), `recv` (`:65`), `close` (`:93`) — bootstrap
  `2-Parser.cs:1576-1583`.
- **An x64-windows socket runtime**, with **REFUSALS and not fake lowerings** on arm64 and wasm. The
  attached-subprocess rung (`180037f32b`, 2026-08-20) is the shape precedent in every respect,
  including its own hard-won rule: ⛔ **the surface without the floor is worse than neither** — an
  earlier attempt declared the names with no runtime behind them and turned a clean `E3004` into a
  backend panic.

**Acceptance material already exists and is canonical**: `managed-socket` (3 cases) · `tcp-client` (3) ·
`async-tcp` (5) · `http-client` (7) = **18**, of which v1 excludes **7 as live-host** (`tcpbin.com`,
`httpbin.org` — `liveNetworkFragments`) and **2 as unported async traces** ⇒ **9 deterministic
error-path cases**, which is what a first slice can gate on. v1 runs the set host-only on
**x64-windows only** (`targetRunsNetworking`) — narrower than subprocess's any-non-wasm gate.
⚠ `W125` is the standing warning here: **a suite whose verdict depends on the public internet is not
always a claim about the code.**

---

## The socket cone is LISTED — 2026-08-21

`stdlib/TcpClient.maxon`, `stdlib/HttpClient.maxon` and `stdlib/helpers/http/httpHelpers.maxon` are on
the whitelist. **That is the last runtime slice this file tracked**, and with it the "Runtime slice" row
of every table above is closed.

| module | probe before listing | outcome |
|---|---|---|
| `TcpClient.maxon` | `E3001` only | listed; `tcp-client/` **2 passed, 0 failed**, `memoryLeak: false` |
| `HttpClient.maxon` | `E3001` only, *with the helper listed alongside it* | listed; `http-client/` **2 passed, 0 failed**, `memoryLeak: false` |
| `helpers/http/httpHelpers.maxon` | ⚠ **NOT PROBEABLE AS A ROOT** — see below | listed |

### ⛔ THE ROW THIS FILE CARRIED FOR `HttpClient` WAS STALE, IN BOTH DIRECTIONS

`:176` recorded `E3005 :29:22 — cannot assign 'unknown' to variable 'HttpHeaders.headers' of type
'struct'` and noted it was *"**not** a socket blocker"*. **RE-MEASURED 2026-08-21: that error does not
reproduce.** Line 29 is `HttpHeaders.create`'s `CaseInsensitiveHeaders.create()`, and it has compiled
since `Map.maxon` was listed. What the probe answers now, in order, as each blocker clears:

| tree state | `maxon-shv2 build stdlib/HttpClient.maxon` |
|---|---|
| before this tick | `E2015 :176:18` — member access `send` on an `unknown` value (**this IS the socket blocker**) |
| with `TcpClient` listed | `E3005 :219:3` — cannot return `unknown` from a function declared to return `HttpResponse` (`httpParseResponse`, in the helper) |
| with both listed | `E3001` only ✅ |

⇒ the row was right that the module has a blocker of its own and wrong about what it was: the recorded
one had already been cleared by an unrelated rung, and the *live* first error was the socket dependency
the row explicitly denied. **The third sweep's instruction — RE-PROBE BEFORE PLANNING AGAINST ANY ROW —
held again.**

### ⚠ A NEW BLIND SPOT IN THE READINESS CRITERION, ALONGSIDE THE CONDITIONAL-`extension` ONE

This file's headline records that the criterion cannot see inside a body the compiler declines to
specialize. **Here is a second shape it cannot see, and it is not the same one:** a module that lives in
a `stdlib/` SUBDIRECTORY cannot be probed as a build root at all.

```
maxon-shv2 build stdlib/helpers/http/httpHelpers.maxon
  ⇒ E3001, PLUS  E3088 :84:28 and :144:30
     function 'String.addressableBytes' is module-scoped and not visible from this directory
```

As a build ROOT the file becomes USER source (`StdlibLoader.registeredPathsWhenTheRootIsInsideStdlib`
deliberately skips it from the stdlib load), so it sits in `stdlib/helpers/http/` outside `stdlib/`'s
module scope and every stdlib-only call in it is refused. **The diagnostic is an artifact of the probe's
own root, not a property of the module** — loaded as stdlib it is stdlib source and both calls resolve.

⇒ **For a `helpers/**` module the readiness probe is a PROGRAM THAT CALLS IT**, and that is the
discriminating instrument anyway: with `let bogus = NoSuchType.definitelyUndefined(1)` injected into
`httpBuildRequest`, `http-client.invalid-url` fails to COMPILE naming
`stdlib\helpers\http\httpHelpers.maxon:19:25`, while `http-client.build-request` still PASSES because
that case never reaches the function. One case proves the bodies are analyzed; the other proves the
failure is reach-dependent rather than a whole-file refusal.

### ⛔⛔ AND THE LISTING EXPOSED A `for`-LOWERING LEAK WITH NO HTTP IN IT

`httpBuildRequest` writes `for (hdrName, hdrValue) in request.headers().headers`. A `for` whose source
reads a field out of a CALL RESULT enrols that result as an owned binding of the loop's frame — the same
frame the cursor is enrolled in — and the empty-`Iterable` entry edge deliberately skips that frame's
drops because on it no cursor was ever created. A request with no user headers iterates an **empty**
`Map`, so **every `HttpClient.get` leaked one `HttpHeaders`**: exit **101** where the bootstrap answers 0.

Reduced to thirty lines of user code with no HTTP and no socket in it, and fixed in
`Parser.parseForStatement` by giving the cursor a scope of its own. ⇒ **`StdlibLoader`'s own rule stands
and is worth restating here: an entry's readiness probe answers whether the module COMPILES, and it has
never asked whether it RUNS. Run it.**

### What is left after this

| kind | what | can an unrelated rung clear it? |
|---|---|---|
| **Runtime slice** | — **NONE. The last one landed here.** | — |
| **Roster residue** | `Array` (9) · `Character` (3) · `StringIndex` (2) · `int.toString` | ⛔ no — each is a retirement with its own blockers |
| **Convergence** | `Interfaces.maxon`'s ten synthesized protocols · `HashValue`'s bounds | ⛔ no |
| **Defects inside a listed module** | `W101` · `W192` · `W193` | — ordinary rungs |
| **Excluded permanently** | `Internals.maxon` | — user ruling |

⚠ **`async-tcp` is NOT closed by this listing, and the two halves must not be confused.**
`async-tcp.connect-error` and `.resolve-error` went red⇒green with the `TcpClient` line alone. ⛔ **The
reason recorded here was wrong** — it said *"E3073's yield roster already carried `__ms_`"*, and
`SemanticCheck.ioYieldingRuntimeCallee` has never named `isManagedSocketRuntimeCallee`. What accepts those
spawns is the UNKNOWN-CALLEE FALLBACK: `checkAsyncYielding` walks the Maxon tier, where every `__ms_*` is an
undeclared Std-tier name, and `calleeYields` resolves an unknown toward "yields". Sabotage-measured
2026-08-21 — with the roster neutered outright, `async File.exists` and `async sleep()` still compile while
the pure-function CONTROL still answers E3073.

⛔ **And `async-tcp.trace-connect-error` / `.trace-mixed-io` are GREEN, not "still red".** They pin
per-operation trace labels (`io_yield #1 [net_connect]`); the per-operation tag table landed after this
paragraph was written, and `async-tcp/` now runs **4 passed, 0 failed** with the live `echo` case excluded
(measured 2026-08-21). Neither half was ever a socket-LISTING question.
