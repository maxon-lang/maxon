You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## maxon-dev MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`. SEE THE BOX.**)

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> **The tools default to the main checkout, and they cannot do otherwise: ONE stdio server process is
> shared by every agent in every worktree, and its working directory is the MCP host's, not yours.**
> The default root is derived from `Process.executablePath()` — the SERVER's own binary — which lives
> in the main repo. **So if you are in a worktree and you do not say which tree you mean, you will be
> told `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree's root — to EVERY tool call.**
> All nine take it. That is the whole fix; there is nothing else to remember.
>
> ```
> build(target: "csharp", repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> **Two things make a mistake here VISIBLE rather than silent, which is what it was before:**
>
> - **Every result echoes the `repoRoot` it actually used** — successes in `repoRoot`, failures in
>   `error.data.repoRoot`. **READ IT BACK.** A `build` that answers
>   `"repoRoot": "C:\Users\Eric\dev\maxon"` when you are in `.claude/worktrees/agent-xyz/` has just
>   built the wrong tree, and now says so. An instrument states what it measured.
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`, naming what was missing).
>   It is never quietly swapped for the main repo — that fallback would be the original bug wearing
>   your intention as a mask. Relative paths are refused for the same reason: they would resolve
>   against the *server's* working directory, not yours. A checkout is any tree holding `stdlib/` and
>   `maxon-sharp/` — **a brand-new worktree qualifies, before anything has been built in it**, so
>   `build target=csharp repoRoot=<your worktree>` is the correct first call.
>
> ⚠ Still worth knowing what these do to the tree they are pointed at: **`run_spec_test` with
> `updateRequired: true` REWRITES that tree's committed goldens**; **`run_scale_test` with `note:`
> writes a row into that tree's `docs/optimization-log.md`**; and **`fmt` rewrites files in place,
> relative to that tree**. Point them at the wrong one and they do not merely report the wrong answer —
> they *edit*. Which is exactly why the root is now yours to state and theirs to echo.
>
> *(Found 2026-07-14, when an agent's `build` "succeeded" on a tree with none of its work in it. It
> burned five agents at once: this file said "PREFER THE MCP TOOLS" while the rung workflow said "work
> in an isolated worktree", and the two had been silently contradicting each other for some time. It is
> the project's own signature bug — ONE FACT WRITTEN DOWN TWICE — at the tooling level. Fixed the same
> day: `repoRoot` is that fact, written down once, by the only party who knows it.)*

Prefer the `maxon-dev` MCP tools over raw Bash invocations of the compiler binaries. They are faster (no shell startup), return structured results, and are the canonical way to drive builds, tests, and one-off snippets in this project. Bash invocation of `./bin/maxon.exe` should only be used when no MCP tool covers the case. **In a worktree, pass `repoRoot` (see the box).**

Two compilers are driveable, and every tool that drives one takes a `compiler`
(or `target`) naming which: `"csharp"` (the C# bootstrap) or `"shv2"` (the
ground-up rewrite, whose suite is `specs-shv2`). **The v1 self-hosted compiler
(`maxon-selfhosted`) is DEPRECATED and no longer reachable from the MCP** — no
`selfhosted` token, no build target, no spec-test runner. Drive it by hand if you
must (see Building and Testing below).

| Task | Use this tool |
|------|---------------|
| Build the C# compiler | `mcp__maxon-dev__build` with `target: "csharp"` |
| Build the shv2 compiler | `mcp__maxon-dev__build` with `target: "shv2"` (built BY the bootstrap — build `csharp` first if it is stale) |
| Run a spec-test suite | `mcp__maxon-dev__run_spec_test` (set `compiler` to pick the suite; `"shv2"` runs `specs-shv2`) |
| MEASURE compile-time + memory SCALING (shv2) — an instrument, **no verdict** | `mcp__maxon-dev__run_scale_test` (no `compiler` arg — shv2 only) |
| Get per-test PASS/FAIL detail for a filter | `mcp__maxon-dev__spec_test_outcome` (requires `filter`; either compiler) |
| Run an inline Maxon snippet or a file | `mcp__maxon-dev__run_program` (requires `compiler`) |
| Dump IR (optionally per-stage) | `mcp__maxon-dev__dump_ir` (requires `compiler`; `dumpStages: true` for stage-by-stage artifacts — csharp only) |
| Format a Maxon file or snippet | `mcp__maxon-dev__fmt` (csharp `maxon fmt`) |
| Look up a 4-digit error code | `mcp__maxon-dev__lookup_error_code` |
| Debug memory-management issues | `mcp__maxon-dev__mm_trace_analyze` |

`build` takes ONE compiler per call — the old `both` / `all` chains sequenced the bootstrap ahead of the retired self-hosted compiler and are gone.

**Every tool in that table takes `repoRoot`** — the tree it acts on. Omit it and you get the main checkout (the one hosting the server binary); in a worktree that is a false green. See the box.

Flags like `--filter`, `--update-required`, `--log`, `--mm-trace`, and `--target` are exposed as parameters on the relevant tools (`filter`, `updateRequired`, `log`, `mmTrace`, `target`). When iterating on a specific failing test, pass `filter` to `run_spec_test` or use `spec_test_outcome` for per-test detail. `target` cross-compiles to what the CHOSEN compiler can emit — the bootstrap has x64 and arm64 emitters (`"x64-windows"`, `"arm64-macos"`), and shv2 rejects the flag outright. **The wasm backend was only ever in `maxon-selfhosted`, so `target: "wasm32-wasi"` is no longer reachable through the MCP** — run it by hand against that compiler's binary if you need it.

### `run_scale_test` — the scaling INSTRUMENT (shv2 only). ⚠ NOT A GATE.

**`scale-test` collects data for TREND ANALYSIS. It has no verdict, and there is nothing to pass.** It compiles a ladder of generated programs — six rungs, each double the last — measures time and memory per phase per rung, and fits a growth exponent to each. **Run it after any change to a pass, the IR, or a data structure the compiler indexes by, and READ it.** A default run is ~20 s.

**The artifact is the trend: `docs/optimization-log.md`** — a dated table you read downwards. The question it answers is *"what has this compiler's cost actually done, change by change?"*, not *"may I merge?"*

⚠ **DO NOT CHASE A GREEN SCALE-TEST. There isn't one.** A curve that looks wrong is a **reading to explain**, not a light to turn green. And **never touch the instrument to make a number look better** — a past pass exempted `regalloc:liveness` from a noise check to stop it complaining, which was treating the symptom of a verdict that should not have existed. The right response to a curve that bends is to say WHY it bends. (`liveness` bills two call sites into one bucket — one per function, linear; one after every split, superlinear — so it is a *sum of two exponents* and bends on a perfectly idle machine.)

✅ **The gate apparatus is GONE** (2026-07-14): the committed memory goldens, the exponent budgets, `--update-required` and the PASS/FAIL/VOID/NOISY verdicts have all been deleted, along with `ScaleGates.maxon` and `ScaleBaseline.maxon`. **Do not reintroduce them.** `scale-test` exits **0** whatever the numbers say; a non-zero exit means the **RUN ITSELF BROKE** (a degenerate corpus, a rung that failed to compile, an IO failure) and produced no valid data — never that a number was surprising.

### ⚠ IT COLLECTS MEMORY. NOT TIME, AND NO CURVES.

**`scale-test` measures per-rung, per-phase MEMORY — allocations, frees, bytes — and nothing else.** No timing, no exponent fits, no residuals. That is deliberate, and both halves have a reason:

- **The ladder DOUBLES, so the RATIO between consecutive rungs IS the growth.** Linear ⇒ allocations double. Quadratic ⇒ they quadruple. **You read it straight off the raw numbers.** An exponent fit adds no information the doubling ladder does not already give you — it is *interpretation dressed up as measurement*, and it is what dragged in the residual, which dragged in the NOISY verdict, which is what once led an agent to **edit the instrument to stop it complaining**.
- **Time cannot be trended.** It is machine-dependent, so a dated table would be comparing a loaded box in July against an idle one in August. **Memory is exact and bit-for-bit reproducible — it is the only column where a difference MEANS something.** *(Measured: allocation deltas read 0.000 across every curve on an unchanged compiler while time deltas read +0.09…+0.29, purely because the machine was busy.)*

⚠ This is only true of **`scale-test`**. The compiler's own per-phase timing (`--metrics=<path>`, `--log=compiler:debug`) is a different thing, is useful interactively, and stays.

**So: read the per-rung memory numbers. Any movement for the same input is REAL, every time.** Explain it, attribute it, and record the reason in the log at the one moment it is still known — the instrument can see exactly WHAT moved and can never see WHY.

`perType: true` adds an untimed `--mm-trace` pass that prints TWO ranked tables, each with its own growth exponent:

- **by TYPE** — the way to answer "the memory numbers moved, but of *what*?". Names the data structure (a `LiveIndexColumn` at exponent 2.17 is a quadratic).
- **by SCOPE** — the function that made the allocation, which the type table structurally *cannot* tell you: a `String` row can never say that 150 of them came from `emitFixedToken`. A constant-factor hog hides inside its type; it is a single named row here. **This is the column that finds things.**

It is slow (several minutes) and off by default.

There is **no `maxon clean` command** — it prints usage and exits 1. To force a from-source stdlib rebuild, delete `stdlib/.maxon/cache/*.mxc` yourself; the self-hosted compiler recompiles the stdlib whenever its cache is absent. The C# bootstrap's stdlib cache is in-memory only, so it always builds the stdlib fresh.

**shv2 does less, and the tools say so rather than pretending.** Its runner implements only `--filter`, `--update-required`, and `--log`; `mmTrace`, `target`, and `dumpStages` are REJECTED with an `invalidParams` error naming the gap, never silently dropped. It has no `fmt` and no `--dump-stages`. Always pair `updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite.

## Building and Testing

Binary names differ by host OS: Windows produces `maxon.exe` / `maxon-shv2.exe`, Linux and macOS produce `maxon` / `maxon-shv2` (no extension). Commands below show the Windows form; drop the `.exe` on Linux/macOS.

### C# bootstrap compiler (maxon-sharp)

- **Build:** `dotnet build` (run from `maxon-sharp/`)
- **Spec tests:** `./bin/maxon.exe spec-test`

The C# compiler binary is at `./bin/maxon.exe` (Windows) or `./bin/maxon` (Linux/macOS).

### shv2 compiler (maxon-shv2)

- **Build:** `./bin/maxon.exe build maxon-shv2` (requires C# compiler already built)
- **Spec tests:** `./maxon-shv2/.maxon/maxon-shv2.exe spec-test` (the `specs-shv2` suite)

The shv2 compiler binary is at `./maxon-shv2/.maxon/maxon-shv2.exe` (Windows) or `./maxon-shv2/.maxon/maxon-shv2` (Linux/macOS).

### Self-hosted compiler (maxon-selfhosted) — DEPRECATED as a PRODUCT, not as a SOURCE

**⭐ READ AND PORT FROM IT.** It is **191,487 lines of working, debugged Maxon**, written against the
same language and the **same `stdlib/`** shv2 uses. Every hard mechanism on the shv2 ladder — ownership,
closures, generics + layout descriptors, witness tables, `async`/green threads, the emitted runtime —
already exists there with its bugs paid for. **When implementing anything in shv2, find the v1 file that
already does it, and reuse its code where it fits.** Justify divergences.
*(One exception: the register allocator ports **lessons**, not code — shv2's is a deliberately different
linear SSA-chordal design.)*

⚠ **IT NO LONGER BUILDS** (verified 2026-07-13). It has bit-rotted against the current bootstrap:
`error E3005: Cannot return 'TypeNameIdArray' from function declared to return 'RegIntArray'` in
`Targets/X64/X64RegisterAlloc.maxon` and `Targets/Arm64/Arm64RegisterAlloc.maxon` — a consequence of
`e4146cf8e` ("a generic's RANGED element type is part of its type"), which made two array typealiases
over different ranges distinct types.

✅ **This is ACCEPTED, and it is NOT being repaired** (user, 2026-07-14). It costs almost nothing,
because **everything v1 is still USED for reads its source rather than runs it**:
- **Porting its code into shv2 — its whole remaining job — is unaffected.**
- **`lookup_error_code` still works**: it parses `ErrorCode.maxon`, it does not execute it.

What is genuinely lost: you cannot **run** it. So the **wasm backend** is unreachable (it is "Beyond the
two phases" anyway), and v1 — the only dictionary-passing + witness-table compiler in the tree — cannot
be used to *measure* whether shv2 needs a witness slot for a `Hashable` constraint. That question
therefore rests on an inference (under dictionary-passing there is no route to `element.hash()` on a type
parameter except a witness slot), and shv2 will answer it definitively when it reaches generics at P1.6.

Not driveable from the `maxon-dev` MCP. To attempt it by hand:

- **Build:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
- **Spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

The self-hosted compiler binary is at `./maxon-selfhosted/.maxon/maxon-selfhosted.exe` (Windows) or `./maxon-selfhosted/.maxon/maxon-selfhosted` (Linux/macOS).

### Common flags

- `--filter=PATTERN` — run only tests matching a pattern
- `--update-required` — regenerate RequiredIR blocks
- `--log=CATEGORY:LEVEL` — enable detailed logging (e.g., `--log=ir:debug`, `--log=codegen:trace`)
- `--mm-trace` — trace memory management operations (useful for memory leak debugging)
- `--target=ARCH-OS` — test a specific target (`x64-windows`, `arm64-macos`; `wasm32-wasi` is self-hosted only)

Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.

Exit code 101 means a memory leak was detected.

## Code Quality

Apply these standards when writing or reviewing any code:

- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the unhandled path, not return a default value. Never use a bare `default` case in `match` — use `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error instead.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **Comments explain "why", not "what"** — don't restate the code.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — if a `match` has multiple cases with the same result, collapse them into a single case.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — functions that cannot return a valid value must throw, not return `""`, `-1`, `null`, or similar.
- **Blank lines for readability** — add blank lines around control flow statements and between logical sections.
- **No magic values** — replace bare literal constants (numbers, strings) with named `static` constants that describe their meaning. When a set of related constants belongs together, group them into a `static enum` instead of scattering individual constants.

## Spec Files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests that use RequiredIR fail, regenerate with `--update-required`.
