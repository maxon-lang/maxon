# The stdlib bring-up table — MEASURED 2026-08-05

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

**50 `.maxon` files under `stdlib/`, 18,337 lines. 16 listed (5,594 lines). 34 unlisted (12,743 lines).**

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
| `Set.maxon` | 396 | — | stops earlier, at a non-literal field default (`:19:33`) |

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
| `Sha256.maxon` | 204 | `E2015 :152:34` | `Array.createIterator` is not on shv2's `Array` roster — **one member** |
| `Ascii.maxon` | 66 | ~~`E2028 :10:4`~~ → **`E3001` ONLY (it type-checks clean)** | ⚖ **THIS ROW WENT STALE THE SAME DAY IT WAS MEASURED — re-measured 2026-08-05 after `BATCH23`.** The blocker was `match c '0' to '9'` — pattern type `int` vs scrutinee `Character` — which is exactly the character-literal WIDTH RULE `BATCH23` deleted: every literal is now a `Character`, and `Character` gained ordering and range patterns, so this file's five `match` arms type-check. **No compiler change is owed here any more; what remains is the WHITELIST entry**, which is `L-stdlib` — not a lane `BATCH23` held. ⚠ But read the headline above before listing it: `Ascii` is all `static function`s, NOT a conditional `extension`, so the criterion is NOT vacuous for this one. |
| `PrimitiveExtensions.maxon` | 101 | `E2010 :2:11` | `extension int` — the extension decl path rejects a primitive keyword as the extended type |
| `Json.maxon` | 1080 | `E2015 :77:37` | a **string-literal field default**; only signed numeric/bool literals are parsed |
| `Set.maxon` | 396 | `E2015 :19:33` | an **identifier field default** — same mechanism |
| `List.maxon` | 175 | `E3086 :21:10` | field `chain` uninitialized by the literal and has no default — the same mechanism, one door over |
| `Vector.maxon` | 50 | `E3086 :19:10` | field `managed` uninitialized by the literal |
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
