# P1.0c — the stdlib cone, measured

**Date:** 2026-07-13 · **Tree:** `C:\Users\Eric\dev\maxon` @ `56aea2908` (clean)
**Scope:** measurement only. **No source file was changed.**

---

## THE HEADLINE

> ### `Map` does **NOT** land in Phase 1's EMIT set. **Zero** `Map` instantiation, **zero** `Map` bodies, anywhere in the emitted module.
> ### Reachability-seeded lowering is **SUFFICIENT** for `Map`. The `stdlib-shv2/stdlib/` pruned fork is **NOT needed as Map's backstop.**
>
> ### But **`Set` IS EMITTED** — and it is emitted *through `String.trim()`*.
> ### It brings the `Hashable` + `Equatable` constraints and a real `Character.hash` body into Phase 1 with it.
> ### **This was already true of the SERIAL harness.** PLAN.md's table has said "**`Set` — 0 uses**" wrongly the whole time.

`Set` is currently scheduled at **P2.3** ("`Set` rides Map's exact mechanism"). Measured: **`Set` is in Phase 1
and `Map` is not.** They are not the same rung, and `Set` cannot ride a mechanism that arrives after it.

---

## 1. The instrument, and why it is trustworthy

### 1.1 The artifact measured: a standalone spec-runner

PLAN.md says *"Extracting a standalone `spec-runner` program is nearly free."* It is. I built one in the
scratchpad (nothing written into the repo):

| File | Provenance |
|---|---|
| `SpecParser.maxon` (364) · `SpecTestRunner.maxon` (467) · `SpecWorkerPool.maxon` (1091) | **verbatim copies** of `maxon-shv2/Testing/*` |
| `Target.maxon` (76) | **verbatim copy** of `maxon-shv2/Compiler/Target.maxon` |
| `Diagnostics.maxon` (17) | shim: the **only two** compiler-cone symbols the harness names — `union CompileError implements Error` (verbatim from `Compiler/Diagnostics.maxon:113`) and `typealias BoolArray = Array with bool` (verbatim from `Compiler/Project.maxon:20`) |
| `Main.maxon` | `maxon-shv2/Main.maxon` with the three **compiler-driver** commands (`build` / `verify-warm-rebuild` / `scale-test`) deleted. The `spec-test` path — `runSpecTest`, `resolveSpecDir`, `reportResults`, `filterSuffix`, `firstBadOption`, `reportCompileError`, and `MaxonArgs.parse`'s flag ladder — is **copied verbatim** |

The closure is tight: the 1,922-line harness names **exactly two** symbols from the compiler cone
(`CompileError`, `BoolArray`) plus `Target`. That is the whole coupling.

### 1.2 It is a LIVE program, not just something that compiles

```
$ scratchpad/specrunner/Main.exe spec-test specs-shv2 --workers=2 --filter=arithmetic
FAIL arithmetic/addition: compilation failed (exit 1): error: unknown option: -o
FAIL arithmetic/subtraction: compilation failed (exit 1): error: unknown option: -o
FAIL arithmetic/multiplication: compilation failed (exit 1): error: unknown option: -o
FAIL arithmetic/complex-expression: compilation failed (exit 1): error: unknown option: -o

0 passed, 4 failed
```

It **parsed** the spec, **built a job plan**, **spawned a 2-worker persistent pool**, drove the **async
drain over green threads**, spoke the **protocol**, and **reassembled results in declaration order**.
Every test fails only because the compiler-under-test is `Process.executablePath()` — i.e. the runner
itself, which has no `build` command. That is the expected outcome and it proves the pool machinery ran.
**The cone below is the cone of a working program.**

### 1.3 How the EMIT set was obtained — two independent instruments, agreeing exactly

```bash
./bin/maxon.exe build <scratchpad>/specrunner --emit-ir
```

**Instrument A — the `.ir` sidecar.** `Program.cs:584` sets `irOutputPath`; `0-Compiler.cs:148-153` then does
`IrPipeline.WriteIrOutput(irResult.X86Module, irOutputPath)`. Three facts make this ground truth:

- `WriteIrOutput` (`3-MlirPipeline.cs:168`) is `IrPrinter.Print(module)` with **no filter** — unlike the
  `returnIr` path beside it, which filters `f => !f.IsStdlib`. **stdlib functions are included.**
- `irResult.X86Module` is **exactly the object handed to `X86CodeEmitterStage.Emit`**. A function in the
  `.ir` is a function that received machine code.
- `DeadFunctionElimination.Run(module)` (`3-MlirPipeline.cs:50`) runs *before* lowering and is a **fixpoint
  reachability walk from `main` + live inits**. That **is** reachability-seeded lowering — the pass this
  entire question turns on.

**Instrument B — the PE COFF symbol table**, read off the actual shipped binary with `llvm-nm`.

**Result: 146 vs 146, and `diff` reports the two lists are IDENTICAL.** An IR dump and the symbol table of
the real executable agree function-for-function. The same cross-check on the serial runner: also identical.

### 1.4 The three distinctions, kept separate

| | What it means here |
|---|---|
| **PARSED** | All **48** stdlib files (49 on disk; the bootstrap excludes `Internals.maxon` — `0-Compiler.cs:879` — and emits its runtime natively). Not reported as "reached." |
| **RESOLVED** | Types laid out, managed-ness known, conformances checked. `EnvMap`'s *layout* is here. |
| **CODEGEN'd** | A `func @...` in the emitted X86 module / a `T` symbol in the PE. **This is the EMIT set.** |

WARNING — **the trap I nearly fell into, and the reader must not:** the bootstrap **monomorphizes**, so a
generic instance **loses its `stdlib.` prefix**. `Set.maxon`'s bodies are emitted as **`CharSet.insert`**, not
`stdlib.Set.insert`; `Array.maxon`'s as `SpecJobArray.push`, `ByteArray.create`, ... **Grepping for
`stdlib.Set` / `stdlib.Array` returns nothing and means nothing.** Every claim below was checked against the
full **401**-function list, not just the 146 `stdlib.*` names.

---

## 2. The numbers

| | funcs emitted (total) | of which `stdlib.*` |
|---|---|---|
| **Parallel spec-runner** (today's harness) | **401** | **146** |
| **Serial spec-runner** (pre-pool, git `2e998a7ca`) | 340 | **123** |
| Delta | | **+23** |

The serial cone is a **strict subset** of the parallel cone — the pool **removed nothing**.

---

## 3. THE `Map` QUESTION — settled, with the machine code

**`stdlib/Subprocess.maxon:120` — `typealias EnvMap = Map with String, String`.**

The harness **must** have `Subprocess` (it spawns the compiler under test). `Subprocess.Configuration`
carries an `Environment`, whose union arms carry `EnvMap` payloads:

```maxon
export union Environment
	inherit
	inheritUpdating(overrides EnvMap)     // EnvMap = Map with String, String
	custom(vars EnvMap)
end 'Environment'
```

**`__destruct_Environment` IS emitted.** So the union — `EnvMap` arms and all — is fully laid out. And yet:

```
$ grep -E "\.(upsert|remove|getCapacity)$|MapIterator|__destruct_Map|EnvMap" emit-all.txt
  *** NONE — zero Map instantiation anywhere in the 401-func module ***
```

**Why**, in the emitted code. This is `__destruct_Environment`'s payload arm, verbatim from the `.ir`:

```
  case_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+8]      // the EnvMap payload pointer
    x64.test rcx, rcx
    x64.jz __nonnull_skip_0
    x64.call mm_decref        // <-- a generic REFCOUNT DROP, not a call to any Map method
```

The destructor emits **`mm_decref`** — a runtime primitive that reads the destructor pointer out of the
object *header* (written at allocation time). It needs to know only that the payload **is managed**, which
is a **RESOLVE**-time fact. It never names `Map`, never calls a `Map` body, and never needs one — because
**no `Map` is ever allocated**, so that arm is dead at runtime.

> **That is the EMIT/DECLARE split, demonstrated in machine code.** `Map` is *laid out* (DECLARE) and
> *zero Map bodies are codegen'd* (not EMIT). Reachability-seeded lowering does exactly what PLAN.md
> requires of it.

Supporting detail, and it is a good one: the env-map arms **are not even implemented**.
`Subprocess.maxon:548-557`, `requireInheritEnv` — which **is** emitted — throws
`"Environment.inheritUpdating: not yet implemented (runtime env-block builder pending)"`. The harness takes
the `inherit` arm.

**=> `Map` stays in Phase 2. The pruned fork is not needed for it.**

---

## 4. THE REAL FINDING — `Set` **is** in Phase 1, via `String.trim()`

Emitted, and unambiguous:

```
CharSet.init · CharSet.insert · CharSet.contains$element · CharSet.grow
HashSlotArray.{get,set,reserve,resize} · StateArray.{get,set,reserve,resize} · Array_Character.{...}
__destruct_CharSet · __destruct_HashSlotArray · __destruct_StateArray
__destruct___ManagedMemory_SetSlotState · __destruct___ManagedMemory_HashValue
stdlib.Character.hash · stdlib.Character.equals        <-- the Hashable / Equatable constraint bodies
stdlib.CharacterSet.contains · stdlib.CharacterSet.whitespacesAndNewlines
CharacterSet.cachedWhitespacesAndNewlines.__lazy_init
```

**The chain, every link verified in source:**

1. The harness calls **`.trim()` — 13 sites** across `SpecParser` + `SpecTestRunner`
   (`SpecParser.maxon:167,175,198,292,299,331`; `SpecTestRunner.maxon:393,414,415,427`; ...).
2. `stdlib/String.maxon` — the no-arg `trim()` delegates to `trim(chars CharacterSet)`. **Both are emitted**
   (`stdlib.String.trim`, `stdlib.String.trim$chars`).
3. `trim$chars` calls **`CharacterSet.whitespacesAndNewlines()`** (`stdlib/CharacterSet.maxon:74`).
4. That returns `static let cachedWhitespacesAndNewlines = CharacterSet{chars: CharSet from ['\t','\n',...]}`
   (`CharacterSet.maxon:37`).
5. **`CharacterSet.maxon:19` — `typealias CharSet = Set with Character`.**
6. `Set.insert` / `Set.contains` / `Set.grow` hash their element => **`stdlib.Character.hash`** is codegen'd
   (`Character.maxon:101`, in the **type body** of `export type Character implements ... Hashable, Equatable ...`
   — *not* an extension, *not* conditional conformance).

**Confirmed callers, straight from the IR:**

```
$ awk '/^  func @/{fn=$0} /x64.call stdlib.Character.hash/{print fn}' Main.ir
  func @CharSet.contains$element(...)
  func @CharSet.grow(...)
  func @CharSet.insert(...)
```

**And it is not new.** `CharSet.*`, `HashSlotArray.*` and `Character.hash` are all present in the **serial**
runner's 123-function cone too. The pool did not drag `Set` in — **`String.trim()` did, and always has.**

> **`String.trim()` is about the most innocuous call in the stdlib, and it pulls in `Set` + `Hashable` +
> `Equatable`.** This is precisely the class of thing PLAN.md's §"stdlib cone" was written to catch, and it
> caught `Map` while missing `Set`.

### 4.1 WARNING — the witness-table question: what I measured, and what I could **not**

**MEASURED (both instruments): the emitted module contains ZERO indirect calls, ZERO witness tables, ZERO
vtables, ZERO layout-descriptor symbols.** `CharSet.insert` reaches `Character.hash` by
`x64.call stdlib.Character.hash` — a **static, direct** call.

**But that is a fact about the BOOTSTRAP's design, and it does NOT transfer to shv2.** The C# bootstrap
**MONOMORPHIZES** (`MonomorphizationPass`), which discharges `Element is Hashable` at compile time.
**shv2's locked decision is the opposite — dictionary-passing + 64-byte layout descriptors + witness
tables** (PLAN.md, Locked decisions: *"Rejected: monomorphization"*).

Cleanly separated:

- **TRANSFERS (measured):** *which stdlib bodies the program reaches.* That is a **call-graph** property,
  independent of dispatch strategy. shv2 will reach `Set.insert`, `Set.contains`, `Set.grow` and
  `Character.hash` for this same program — same stdlib, same call graph.
- **DOES NOT TRANSFER, and I could NOT measure it:** *whether shv2's dictionary-passing design needs a
  witness table for `Character: Hashable`.*

**Why I could not measure it — stated plainly rather than estimated.** The architecture-matched compiler is
v1 (`maxon-selfhosted`), which *has* dictionary-passing + witness tables. **It no longer builds:**

```
$ ./bin/maxon.exe build maxon-selfhosted
[CMP] ERROR: error E3005: maxon-selfhosted/Compiler/Targets/X64/X64RegisterAlloc.maxon:133:2:
      Cannot return 'TypeNameIdArray' from function declared to return 'RegIntArray'
[CMP] ERROR: ... :138:2  (same)
[CMP] ERROR: ... :143:2  (same)
exit 1
```

> **Incidental finding, worth knowing:** `CLAUDE.md` states the v1 tree *"still builds, so drive it by hand
> when you need something only it has (notably the wasm backend...)"*. **That is now FALSE.** v1 has
> bit-rotted against the current bootstrap — a per-instance-typealias regression in `X64RegisterAlloc.maxon`.
> The wasm backend and the complete 4-digit `ErrorCode.maxon` registry are, as of today, **not reachable by
> any route.**

**Inference — flagged as inference, NOT measurement.** Under dictionary-passing there is no way to call
`element.hash()` on a *type parameter* without going through a witness slot. So shv2 most likely needs
**one witness table (`Character: Hashable`, `Character: Equatable`) in Phase 1** — unless it monomorphizes
this single instance, special-cases it, or prunes `CharacterSet` out of the fork. **This should be settled by
measurement before it is planned around, and the only instrument that could settle it is currently broken.**

### 4.2 => This is where the pruned fork earns its keep — for `Set`, not for `Map`

PLAN.md un-deferred `stdlib-shv2/stdlib/` as the backstop *"if reachability-seeded lowering proves
insufficient"*, naming `Map`/`EnvMap` as the case. **Measured, that is exactly backwards:**

- Reachability-seeded lowering **is** sufficient for `Map`. (PASS)
- It is sufficient for `Set` too — and `Set` **is reached anyway.** (FAIL)

The fork's real job, if you want one, is to cut the **`String.trim()` -> `CharacterSet` -> `Set`** edge (e.g.
a `trim()` over an ASCII-whitespace predicate instead of a `Set with Character`). That is a **stdlib** edit,
explicitly sanctioned (*"The principle applies to shv2's source, not the stdlib"*). **The alternative is to
accept `Set` + one `Hashable` witness into Phase 1** — which, by this plan's own *"do the hard things early"*
principle, is arguably the right answer. Either way it is now a **decision**, not an oversight.

---

## 5. What the PARALLEL pool added — **+23, all of it async-stdio**

Not in any existing table. Full delta (parallel minus serial); **nothing was removed**:

| | added |
|---|---|
| **`StreamingSubprocess`** x11 | `spawn` · `spawnWithCwd` · `wait` · `waitWithTimeout` · `release` · `closeStdin` · `writeStdinLine` · `readStdoutLine` · `readStdoutLineCapped` · `readStderrLine` · `readStderrLineCapped` |
| **`Stdin`** x7 | `create` · `readLine` · `fillOnce` · `findNewline` · `discardOne` · `shiftLeft` · `takePrefix` |
| free / misc x5 | `Console.stdin` · `stdlib.sleep` · `stdlib.readLineResultToString` · `stdlib.stripTrailingCR$bytes` · `stdlib.stripTrailingCR$s` |

**The RUNTIME delta (not stdlib — this is Workstream R3's real surface, measured):** three call sites present
in the parallel runner and **absent** from the serial one —

```
x64.call __gt_spawn        x64.call __gt_try_await        x64.call __gt_is_complete
```

### 5.1 `Promise` contributes **ZERO** stdlib bodies

`export type Promise uses Element` (`stdlib/Builtins.maxon:87`) is a **facade**: *"The compiler synthesises
the underlying representation (an i64 handle into the green-thread scheduler's promise table) and
special-cases the async/await operations."* No `Promise.*` function is emitted anywhere.
**`async`/`await` is compiler + runtime, not stdlib.**

### 5.2 WARNING — the pool added **NO CLOSURES**. PLAN.md's table is wrong on this.

PLAN.md: *"WARNING **`async` / `await` / Promises + closures** — from the parallel worker pool."*

**Measured: ZERO closures.** No lambda syntax anywhere in the harness; no closure / env / thunk function in
the 401-function emit set. The single `async` site is a **direct free-function call**:

```maxon
// SpecWorkerPool.maxon:1000
let drain = async drainResultsThunk(handle.child)
```

...and `SpecWorkerPool.maxon:588` says the free function was chosen **deliberately** for exactly this reason:
*"A FREE FUNCTION, not a method on PersistentWorkerHandle, and deliberately so: `async handle.drain()` would
capture the handle STRUCT..."*

> **This does NOT weaken P1.5's escape argument — it sharpens it.** `handle.child` is a **managed
> `StreamingSubprocess`, passed by value into a green thread**: that *is* a capture into a task frame, and it
> *is* an escape. But the capture channel is the **async call's ARGUMENTS**, not a closure env. So `async`
> and closures are **two distinct capture channels**, and the harness exercises **only the async one**.
> `EscapeAnalysis` still wants both from birth (the `LazyMessage` dogfood at P2.5 is the closure channel) —
> but Phase 1's *acceptance test* covers only the async channel, and the plan should say so rather than claim
> a closure test it does not have.

---

## 6. The DECLARE-only set — verified by name, not by inspection

**Every method declared in an `extension` block, checked against the full 401-function EMIT set:**

| extension-declared method | emitted? |
|---|---|
| `Array.contains` (x2 overloads), `Array.sort`, `Array.sortUnstable` (`Array.maxon:312,376,397`) | **NO** |
| **`Array.hash`, `Array.equals`** — the **conditional conformance**, `Array.maxon:415` | **NO** |
| `Iterator.advanceBy`, `BidirectionalIterator.retreatBy`, `Iterable.withIterator` (`Interfaces.maxon:81,98,117`) | **NO** |
| `int.{hash,equals,compare,toString,clone}`, `float.{...}`, `bool.{...}` (`PrimitiveExtensions.maxon:2,30,75`) | **NO** |

The only `.contains` / `.hash` / `.equals` / `.toString` / `.clone` symbols in the module are **type-body**
methods — `String.contains$needle`, `CharacterSet.contains`, `CharSet.contains$element`, `Character.hash`,
`Character.equals`, `String.equals`, `FilePath.toString`, `SpecJob.clone`. **No extension. No conditional
conformance.**

=> **PLAN.md is CORRECT** on all three of its DECLARE-only claims:

- **`extension` — 0 emitted.** *(Note that `int.fromString`, which the harness **does** call at
  `SpecParser.maxon:299`, is **not** a counterexample: it is declared in `Builtins.maxon:112`, a **builtin
  type body**, not an extension. It is emitted as `stdlib.__int_fromString`.)*
- **Conditional conformance — not reached.** *(PLAN cites `Array.maxon:406`; it is now **`:415`** — stale.)*
- **`Map` — not reached.**

**`Hashable` and `Equatable` ARE reached** (§4), but only as **static conformance**. No value is ever stored
at interface type; hence zero indirect calls.

### 6.1 Per-FILE classification (48 parsed)

**EMIT — 18 files:** `Array` *(as `SpecJobArray`, `ByteArray`, `StringArray`, `BoolArray`, `FilePathArray`, ...)* ·
`Builtins` · `Character` · `CharacterSet` · **`Set`** *(as `CharSet`)* · `CommandLine` · `Console` · `Directory` ·
`File` · `FilePath` · `Print` · `PrintError` · `Process` · `Sleep` · `String` · `Subprocess` · `URL` ·
`helpers/string/{grapheme, unicodeCategory, utf8, views}` · `helpers/url/urlHelpers`

**DECLARE-only — parsed + resolved, ZERO codegen:**
`Ascii` · `Build` · `Clock` · `HttpClient` · `Interfaces` · `Json` · `List` · `Log` · **`Map`** · `Math` ·
`PrimitiveExtensions` · `Range` · `Sha256` · `TcpClient` · `Unicode` · `Vector` ·
`helpers/http/httpHelpers` · `helpers/itertools/withIterator` · `helpers/sort/*` (6 files) ·
`helpers/string/hash` · `helpers/string/utf16`

*(`Internals.maxon` is **excluded** by the bootstrap — `0-Compiler.cs:879` — and its runtime emitted natively.
Not reported as "reached.")*

A consistency check that lands: **`helpers/string/hash.maxon`** (`hashManagedString`, `equalsManagedString`,
...) is **NOT** emitted — exactly what you would expect when there is no `Map with String` and no
`Set with String` anywhere. The only hashing that survives is `Character`'s.

---

## 7. A FOURTH correction — the harness **does** interpolate a struct

PLAN.md: *"`String` + interpolation of **primitives/String only** (0 struct interpolation)"* and
*"witness-table dispatch — 0 `implements`, 0 struct interpolation."*

**Measured: `Main.maxon:233-236` (`reportCompileError`) interpolates a bare `FilePath` — 4 sites.**

```maxon
fileNotFound(p) then printError("error: file not found: {p}\n")     // p is a FilePath STRUCT
```

`FilePath.maxon:29` — `export type FilePath implements InitableFromStringLiteral, Equatable, Hashable,
Stringable`. The bootstrap lowers `{p}` to a **direct `x64.call stdlib.FilePath.toString`** (statically
resolved, because it monomorphizes).

**Why PLAN.md missed it, and the methodological lesson:** PLAN.md says its boundary was *"Grep-verified
against `maxon-shv2/Testing/`"*. **`Testing/` is genuinely clean** — 0 bare struct interpolations there; all
13 of its `.toString()` calls are *explicit* method calls. But **the Phase-1 artifact is `Testing/` + a
`Main`**, and the Main's error reporter sits on `runSpecTest`'s error edge.
=> **The boundary must be measured against the PROGRAM, not the directory.**

**The stdlib does not force it:** `Subprocess.maxon:380,422,664` all use explicit `.toString()`.

=> **It is a one-character fix per site — `{p}` -> `{p.toString()}` — if Phase 1 wants Stringable-through-a-
witness kept out.** Free under the bootstrap's monomorphization; under shv2's witness tables it would
otherwise pull P2.1's `stringable` arm into Phase 1.

---

## 8. => CORRECTED BOUNDARY TABLE — ready to paste into PLAN.md

> Replace PLAN.md §"Phase 1 — the measured boundary" table **and** the stale WARNING note under it.

<!-- ============================ PASTE FROM HERE ============================ -->

**MEASURED 2026-07-13 (P1.0c) against the UPGRADED, PARALLEL harness** — `maxon-shv2/Testing/`
(`SpecParser` 364 + `SpecTestRunner` 467 + `SpecWorkerPool` 1091 = **1,922 lines**) plus the `Main` a
standalone runner needs. **Not grepped — compiled.** A standalone `spec-runner` was extracted (the harness
verbatim + `Compiler/Target.maxon` verbatim + a 2-symbol shim for `CompileError`/`BoolArray` + `Main` minus
the driver commands), built with `maxon.exe --emit-ir`, and its EMIT set read off **both** the emitted X86
module **and** the PE COFF symbol table (`llvm-nm`) — **146 stdlib functions, the two lists identical**. The
extracted runner **runs**: it drives a real 2-worker green-thread pool.

| The harness **EMITs** (=> Phase 1 must CODEGEN) | The harness **DECLAREs only** (=> Phase 2) |
|---|---|
| structs · enums · **unions** · `match` | **`Map`** — **0** instantiation, **0** bodies. Its `EnvMap` arms in `Subprocess.Environment` compile to a bare `mm_decref` => **laid out, never codegen'd**. *Reachability-seeded lowering is SUFFICIENT here — the pruned fork is NOT needed for `Map`* |
| **`String`** + interpolation of primitives, `String`, **and one STRUCT** (`FilePath`, via `Stringable`) — `Main.maxon:233-236`. **A 1-char fix (`{p}` -> `{p.toString()}`) removes it** | **conditional conformance** — `Array implements Hashable, Equatable where Element is Hashable and Equatable` (`Array.maxon:415`) is **NOT reached** |
| heap · ownership · drops | **`extension`** — **0**. Not one of the 4 `Array` / 3 `Interfaces` / 3 `PrimitiveExtensions` extension blocks emits a single method |
| **owned `String` payloads in union cases** — `compilerError(text String)`, `fail(reason String)` | **existentials / interface-typed storage** — 0. **Zero indirect calls in the entire module** |
| `throws` / `try` / `otherwise` — **80 `try`, 16 `throws`** | the entire compiler cone — no IR, no allocator, no backend |
| **generics** — `Array with {SpecTest, SpecTestResult, FilePath, SpecJob, SpecPlan, String, bool, ...}` => managed elements | `Json` · `List` · `Math` · `Range` · `Sha256` · `Vector` · `Log` · `Clock` · `HttpClient` · `TcpClient` · `Ascii` · `Unicode` · **`helpers/sort/*`** (6 files) · `helpers/string/{hash,utf16}` |
| **`Array`** · `for-in` — **18** sites | |
| **ranged typealiases** — **8** (`int(0 to u32.max)`, `int(1 to u32.max)`, `int(i64.min to i64.max)`, ...) | |
| **`Set` — EMITTED**, and *not* by anything the harness wrote. `CharSet = Set with Character` (`CharacterSet.maxon:19`) arrives via **`String.trim()` (13 sites)** -> `CharacterSet.whitespacesAndNewlines()`. Emits `CharSet.{init,insert,contains,grow}` + `HashSlotArray` / `StateArray` | |
| **`Hashable` + `Equatable` — EMITTED**, as `Set`'s element constraints: **`stdlib.Character.hash`** and **`stdlib.Character.equals`** (type-body conformances, `Character.maxon:101,56`) | |
| **`async` / `await` / `Promise`** — from the pool. **`Promise` has ZERO stdlib bodies** (`Builtins.maxon:87` is a compiler-synthesised facade); the real surface is **runtime**: `__gt_spawn` · `__gt_try_await` · `__gt_is_complete` => **Workstream R3** | |
| **async subprocess stdio** — `StreamingSubprocess` x11 + `Stdin` x7 + `Console.stdin` + `sleep` **(+23 stdlib fns — the pool's ENTIRE stdlib delta over the serial harness)** | |
| stdlib: `File` · `FilePath` · `Directory` · `Subprocess` · `Process` · `CommandLine` · `Console` · `Print` · `String` · `Character` · `CharacterSet` · `URL` · `Builtins` · `Sleep` · `helpers/string/{grapheme,utf8,views,unicodeCategory}` · `helpers/url` | |
| **CLOSURES — 0.** *(The previous table claimed the pool added them. It did not.)* `SpecWorkerPool.maxon:1000` is `async drainResultsThunk(handle.child)` — `async` on a **direct free-function call**, chosen deliberately (`:588`) to avoid capturing a struct. **The escape channel Phase 1 exercises is the async call's ARGUMENTS, not a closure env — two distinct channels, and only one is tested here** | |

> **=> TWO PLAN CHANGES THIS FORCES.**
>
> 1. **`Set` is a PHASE 1 mechanism, not P2.3.** *"`Set` rides Map's exact mechanism"* is now false as
>    sequencing: **`Set` is reached and `Map` is not.** Either implement `Set` + one `Hashable` constraint in
>    Phase 1, **or** use the `stdlib-shv2/stdlib/` fork to cut the **`String.trim()` -> `CharacterSet` ->
>    `Set`** edge. **The fork's real job is `Set`, not `Map`** — the opposite of what it was un-deferred for.
>
> 2. **The witness-table consequence is UNMEASURED, and must be settled before it is planned around.** The
>    bootstrap **monomorphizes**, so it discharges `Element is Hashable` as a static call and emits **no**
>    witness table. **shv2's locked design is dictionary-passing + witness tables**, under which
>    `element.hash()` on a type parameter has no route *except* a witness slot. The compiler that could
>    settle this — v1, `maxon-selfhosted` — **NO LONGER BUILDS** (`E3005` x3 at
>    `X64RegisterAlloc.maxon:133,138,143`). **`CLAUDE.md`'s claim that the v1 tree "still builds" is FALSE.**

<!-- ============================= PASTE TO HERE ============================= -->

---

## Appendix A — every command run

```bash
cd C:/Users/Eric/dev/maxon
SP=<scratchpad>

# 1. validate the instrument on a trivial program first
./bin/maxon.exe build $SP/hello --emit-ir
./llvm-project/bin/llvm-nm.exe $SP/hello/Main.exe | grep -i stdlib      # -> stdlib.print, and only that

# 2. extract the standalone PARALLEL spec-runner (nothing written into the repo)
cp maxon-shv2/Testing/SpecParser.maxon $SP/specrunner/
cp maxon-shv2/Testing/SpecTestRunner.maxon $SP/specrunner/
cp maxon-shv2/Testing/SpecWorkerPool.maxon $SP/specrunner/
cp maxon-shv2/Compiler/Target.maxon $SP/specrunner/
#   + Diagnostics.maxon  (CompileError, BoolArray -- verbatim, 2 symbols)
#   + Main.maxon         (shv2's Main, driver commands stripped; spec-test path verbatim)
./bin/maxon.exe build $SP/specrunner --emit-ir

# 3. the EMIT set -- TWO INDEPENDENT INSTRUMENTS
grep -o "^  func @stdlib\.[^(]*" $SP/specrunner/Main.ir | sed 's/^  func @//' | sort > emit-ir.txt
./llvm-project/bin/llvm-nm.exe $SP/specrunner/Main.exe | grep " T stdlib\." | sed 's/.* T //' | sort > emit-nm.txt
diff emit-ir.txt emit-nm.txt          # IDENTICAL -- 146 == 146

# 4. the FULL module (401 funcs) -- monomorphized instances lose the `stdlib.` prefix
grep -o "^  func @[^(]*" $SP/specrunner/Main.ir | sed 's/^  func @//' | sort > emit-all.txt
grep -E "\.(upsert|remove|getCapacity)$|MapIterator|__destruct_Map|EnvMap" emit-all.txt   # -> NONE
grep "^CharSet\." emit-all.txt                                                            # -> 4 fns

# 5. how is the Hashable constraint dispatched?
awk '/^  func @/{fn=$0} /x64.call stdlib.Character.hash/{print fn}' $SP/specrunner/Main.ir
grep -o "x64.call \(rax\|rbx\|rcx\|rdx\|r[0-9]*\)\b" $SP/specrunner/Main.ir    # -> ZERO indirect calls

# 6. the Map question, in machine code
awk '/^  func @__destruct_Environment\(/,/^  }$/' $SP/specrunner/Main.ir       # -> a bare mm_decref

# 7. the SERIAL baseline (pre-pool), straight from git
git show 2e998a7ca:maxon-shv2/Testing/SpecParser.maxon     > $SP/specrunner-serial/SpecParser.maxon
git show 2e998a7ca:maxon-shv2/Testing/SpecTestRunner.maxon > $SP/specrunner-serial/SpecTestRunner.maxon
./bin/maxon.exe build $SP/specrunner-serial --emit-ir
comm -13 emit-serial.txt emit-ir.txt        # -> +23, all StreamingSubprocess / Stdin
comm -23 emit-serial.txt emit-ir.txt        # -> nothing removed

# 8. green-thread runtime delta
grep -o "x64.call __gt_[a-z_]*" $SP/specrunner-serial/Main.ir | sort | uniq -c    # -> none
grep -o "x64.call __gt_[a-z_]*" $SP/specrunner/Main.ir        | sort | uniq -c    # -> spawn/try_await/is_complete

# 9. faithfulness: the extracted runner actually RUNS the pool
$SP/specrunner/Main.exe spec-test specs-shv2 --workers=2 --filter=arithmetic

# 10. the architecture-matched compiler (dictionary-passing + witness tables) -- COULD NOT MEASURE
./bin/maxon.exe build maxon-selfhosted      # -> E3005 x3, exit 1. v1 NO LONGER BUILDS.
```

## Appendix B — the 146 stdlib functions that receive codegen

*(Parallel spec-runner. The 23 marked `[+pool]` are the ones the worker pool added over the serial harness.)*

```
ArrayIterator.create
Character.{codepoint, equals, hash, init}                       <-- hash/equals = Set's Hashable/Equatable
CharacterSet.{contains, whitespacesAndNewlines}                 <-- the door Set comes through
CollectedOutput.{create, exitCode}
CommandLine.{args, optionValue}
Configuration.{create, run}
Console.stdin                                                   [+pool]
Directory.{create, currentPath, exists, list}
File.{delete, readText, writeText}
FilePath.{changeExtension, create, fileExtension, filename, from, hasInvalidChars, isEmpty,
          join$component_FilePath, join$component_String, lastSepPlusOne, normalizeSeparators,
          parent, resolveFileURL, separator, stem, toString}
FileReader.{open, readAll}   FileWriter.{open, writeAll}
PlatformOptions.defaults     Process.executablePath
Stdin.{create, discardOne, fillOnce, findNewline, readLine, shiftLeft, takePrefix}          [+pool x7]
StdioRuntimeTriple.create
StreamingSubprocess.{closeStdin, readStderrLine, readStderrLineCapped, readStdoutLine,
                     readStdoutLineCapped, release, spawn, spawnWithCwd, wait,
                     waitWithTimeout, writeStdinLine}                                       [+pool x11]
String.{byteLength, codepoints, contains$needle, count, cstr, endIndex, endsWith, equals,
        findFirst, from, indexAfter, init, isEmpty, replace, slice$endIndex, split, startIndex,
        startsWith, toByteArray, toLower, trim, trim$chars}     <-- trim + trim$chars = the Set door
StringIndex.{bytePos, charIndex, create}
Subprocess.{run$arguments, runConfiguration}                    <-- NOT the env-map overload
SubprocessError.displayReason   TerminationStatus.code
URL.{parse, path, scheme}
__int_fromString  (Builtins.maxon:112 -- a builtin type body, NOT an extension)
appendNulTerminated · attachedFlagsFor · buildArgvBlob · endsWithManaged · findManaged · fpByteAt
lastErrorMessage · outputTripleFromDestination · print · printError · requireInheritEnv
resolveByName · resolveExecutablePath · scanIsAscii · startsWithManaged · stdinTripleFromSource
subprocessEmptyManaged · terminationStatusFromKind · urlByteAt
readLineResultToString · sleep · stripTrailingCR$bytes · stripTrailingCR$s                  [+pool x4]
helpers.string.{CodepointIterator.{advance, create, current}, CodepointView.{create, createIterator},
                GraphemeState.create, byteIndexToGraphemeIndex, countGraphemesManaged,
                countGraphemesManagedRange, findGraphemeEndManagedRange, findGraphemeStartManaged,
                findPrevCodepointStart, graphemeBreakProperty, graphemeStateInit,
                graphemeStateUpdate, isExtendedPictographic, isGraphemeBreak,
                makeCharacterFromManagedRange, unicodeGeneralCategory, utf8ByteLengthAt,
                utf8DecodeAt, utf8IsContinuation}
helpers.url.{safeByteAt, urlIsAlpha, urlIsDigit, urlIsHexDigit, urlIsSchemeChar,
             urlValidatePercentEncoding}
```

**Plus, in the same emitted module (monomorphized generic instances — no `stdlib.` prefix):**

```
CharSet.{init, insert, contains$element, grow}                  <-- Set with Character. THE FINDING.
HashSlotArray.{get, set, reserve, resize}    StateArray.{get, set, reserve, resize}
Array_Character.{count, createIterator, get, reserve, resize, set}
ByteArray · StringArray · BoolArray · FilePathArray · SpecJobArray · SpecPlanArray ·
SpecTestArray · SpecTestResultArray · DrainPromiseArray · PersistentWorkerHandleArray ·
SpecResultBuckets                                               <-- all Array with X instances
__destruct_CharSet · __destruct_HashSlotArray · __destruct_StateArray ·
__destruct___ManagedMemory_SetSlotState · __destruct___ManagedMemory_HashValue
```

**And NOT present, anywhere in the 401:** any `Map` instance · `MapIterator` · `__destruct_Map` ·
`Array.contains` · `Array.hash` · `Array.equals` · `sort` · `sortUnstable` · `advanceBy` · `retreatBy` ·
`withIterator` · `int.hash` · `float.*` · `bool.*` · any witness table · any vtable · any layout
descriptor · **any indirect call**.
