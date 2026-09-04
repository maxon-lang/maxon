You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## The three compilers

| | source | binary | spec suite |
|---|---|---|---|
| **C# bootstrap** — the reference ORACLE | `maxon-sharp/` | `bin/maxon.exe` | `specs/` |
| **shv2** — the ground-up rewrite, the product | `maxon-shv2/` | `maxon-shv2/.maxon/maxon-shv2.exe` | `specs-shv2/` |
| **v1 self-hosted** — DEPRECATED, read-only source | `maxon-selfhosted/` | — | — |

Binary names differ by host OS: Windows produces `maxon.exe` / `maxon-shv2.exe`, Linux and macOS
produce `maxon` / `maxon-shv2`. Commands below show the Windows form.

- **Build the bootstrap:** `dotnet build` (from `maxon-sharp/`). Do NOT use `dotnet run` — it
  recompiles every time.
- **Build shv2:** `scripts/build-shv2.sh` (requires the bootstrap already built).
- **Run a suite:** `./bin/maxon.exe spec-test` / `./maxon-shv2/.maxon/maxon-shv2.exe spec-test`.
- Exit code **101** means a memory leak was detected.
- There is **no `maxon clean`**. To force a from-source stdlib rebuild, delete
  `stdlib/.maxon/cache/*.mxc`; the self-hosted compilers recompile the stdlib whenever the cache is
  absent. The bootstrap's stdlib cache is in-memory only, so it always builds fresh.

> ### ⭐ THE TREE'S shv2 IS THE COMPILER shv2 EMITS. THE BOOTSTRAP ONLY BUILDS THE **SEED**.
>
> `scripts/build-shv2.sh` runs **two** compiles: the bootstrap produces the seed at
> `maxon-shv2/.maxon/maxon-shv2-seed.exe` (~40 s), and the seed compiles the tree binary
> `maxon-shv2/.maxon/maxon-shv2.exe` (~170 s), which is RENAMED into place — a compiler cannot
> overwrite a running image (**E6002**). The last good binary is kept at
> `maxon-shv2/.maxon/maxon-shv2.previous.exe`; a FAILED build leaves the slot **empty** rather than
> reinstating it, because a stale compiler that answers as though it were current is the failure
> every staleness refusal in this repo exists to prevent.
>
> ⛔ **SO `./bin/maxon.exe build maxon-shv2` ON ITS OWN IS THE SEED STEP, NOT THE BUILD.** Run
> straight, it writes a bootstrap-emitted compiler into the tree slot and **nothing detects that** —
> the binary is fresh, the suite is green, and every `#if compiler(shv2)` construct (the parallel
> compile driver among them) is simply absent from the compiler you are then measuring. Use the
> script, or the MCP `build` tool with `target: "shv2"`, which spawns it.

### v1 (`maxon-selfhosted`) — DEPRECATED as a PRODUCT, not as a SOURCE

**⭐ READ IT. It is 191,487 lines of working, debugged Maxon**, written against the same language and
the **same `stdlib/`** shv2 uses. Every hard mechanism on the shv2 ladder — ownership, closures,
generics + layout descriptors, witness tables, `async`/green threads, the emitted runtime — already
exists there with its bugs paid for. **When implementing anything in shv2, find the v1 file that
already does it and read it first.**

⚠ **But do NOT blindly copy it. shv2 is a deliberate rewrite, and a number of things it does are
BETTER** — block args not phi nodes, parser-minted `ValueId`s not name strings, 3 tiers not 4, static
ownership from commit 1, the flat `StdOp`. **Where shv2 departs, the departure IS the thesis.** Both
directions need a reason: a divergence needs one, and so does a copy — *"it works in v1"* is not a
reason, because **v1 is debugged, not FAST** (its register allocator was ~74% of self-compile time —
port an algorithm and you port its cost curve).

⚠ **IT NO LONGER BUILDS** and is **not being repaired** (user ruling). That costs almost nothing:
everything v1 is still used for reads its source rather than runs it. What is lost is the ability to
*run* it — so v1's own wasm backend cannot be executed (largely moot: shv2 has its own `wasm32-wasi`
backend), and v1 cannot be used to *measure* dictionary-passing/witness-table questions.

## maxon-dev MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`**)

Prefer the `maxon-dev` MCP tools over raw Bash invocations of the compiler binaries: faster (no shell
startup), structured results. Use Bash only where no tool covers the case.

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> ONE stdio server process is shared by every agent in every worktree, and its default root is the
> main checkout (derived from the SERVER's own binary path). **Say nothing and you are told
> `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree root — to EVERY tool call.**
> All nine take it.
>
> ```
> build(target: "csharp", repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> - **Every result echoes the `repoRoot` it actually used** (successes in `repoRoot`, failures in
>   `error.data.repoRoot`). **READ IT BACK.**
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`), never quietly swapped
>   for the main repo. Relative paths are refused too — they would resolve against the *server's* cwd.
>   A checkout is any tree holding `stdlib/` and `maxon-sharp/`, so a brand-new worktree qualifies
>   before anything is built in it.
>
> ⚠ These tools **EDIT** the tree they are pointed at: `run_spec_test` with `updateRequired: true`
> rewrites that tree's committed goldens, `run_scale_test` with `note:` writes a row into its
> `docs/optimization-log.md`, and `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build a compiler | `build` — `target: "csharp"` or `"shv2"` (ONE per call; `shv2` spawns the two-compile script, ~4 min) |
| Run a spec-test suite | `run_spec_test` (`compiler` picks the suite) |
| Per-test PASS/FAIL detail | `spec_test_outcome` (requires `filter`) |
| MEASURE per-phase memory + CPU scaling (shv2 only) — an instrument, **no verdict** | `run_scale_test` |
| Run an inline snippet or a file | `run_program` (requires `compiler`) |
| Dump IR | `dump_ir` (`dumpStages: true` for per-stage artifacts — csharp only) |
| Format a file or snippet | `fmt` (optional `compiler`, default `csharp` — the two must agree byte for byte) |
| Look up a 4-digit error code | `lookup_error_code` |
| Debug memory-management issues | `mm_trace_analyze` |

Flags are exposed as parameters: `filter`, `updateRequired`, `log`, `mmTrace`, `target`. Always pair
`updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite. `mmTrace` and
`dumpStages` are REJECTED for shv2 with an `invalidParams` error naming the gap, never silently
dropped.

**Filters are ONE case-insensitive substring in BOTH runners.** Neither takes a list —
`--filter=static-methods,enums` selects NOTHING. Run one filter per file and read every one.

What shv2's runner lacks against the bootstrap's: `--verbose` (it always prints a line per test),
`--no-batch` (its batching is `RunStrategy`, chosen by target and host) and `--debug-info`. What the
bootstrap's lacks: `--network`. `checkSpecTestFlags` refuses in both directions.

### Common flags

- `--filter=PATTERN`, `--update-required`, `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`),
  `--mm-trace`, `--workers=<n>`, `--target=ARCH-OS`.
- **`--workers=1` is a DEBUGGING TOOL, not a gate.** It is the same pool with one worker in it, and
  the parent buffers results and reports in fixed order — **ordering cannot vary with pool size**.
  The default pool is 12 and that is the only count these processes run the suite at.

### Targets

The bootstrap emits `x64-windows` and `arm64-macos`; shv2 adds **`wasm32-wasi`** (a WASI Preview2
component). `run_spec_test` takes `target: "wasm32-wasi"` for shv2 and runs the output under the
vendored wasmtime. By hand, for ONE program:

```
./maxon-shv2/.maxon/maxon-shv2 build f.maxon -o out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

On Windows the runnable binary is `wasmtime.exe` — the extensionless `wasmtime` beside it is a Mach-O
for the Mac lane.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY".** Heap, `String`, `print`, structs, arrays, closures,
interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there; the two lanes
run within a few hundred cases of each other. The families it does not run carry an explicit
`<!-- targets: … -->` restriction: **async / green threads, file and directory IO, the clock builtins,
argv**, plus the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names), which
are about x64's output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on
that lane, not an out-of-slice case to refuse.** v1's wasm backend is the read-only reference for the
families still absent.

## `run_scale_test` — the scaling INSTRUMENT (shv2 only). ⚠ NOT A GATE.

It compiles a ladder of generated programs — six rungs, each double the last — and measures **MEMORY
and CPU TIME per phase per rung**. ~17 s. Run it after any change to a pass, the IR, or a data structure
the compiler indexes by, and READ it.

- **It has no verdict and there is nothing to pass.** It exits **0** whatever the numbers say; a
  non-zero exit means the **RUN ITSELF BROKE** (a degenerate corpus, a rung that failed to compile, an
  IO failure) and produced no valid data.
- ✅ **The gate apparatus is GONE** — committed memory goldens, exponent budgets, `--update-required`
  and the PASS/FAIL/VOID/NOISY verdicts are all deleted. **Do not reintroduce them.**
- ⚠ **DO NOT CHASE A GREEN SCALE-TEST. There isn't one**, and **never touch the instrument to make a
  number look better.** A curve that looks wrong is a **reading to explain**.
- **The ladder DOUBLES, so the RATIO between rungs IS the growth** — ×2 linear, ×4 quadratic. Read it
  off the **ALLOCATION** columns, which are exact and bit-for-bit reproducible; the CPU column carries a
  few-percent noise band and a platform-defined unit, and there is no wall time at all.
- **The artifact is the trend: `docs/optimization-log.md`.** Record WHY a number moved at the one moment
  it is still known — the instrument sees exactly WHAT moved and can never see WHY. **Write no row you
  did not measure.**

⇒ **The full reading guide — the three columns, the two blind spots, `--per-type`, A/B methodology — is
the `optimize` skill.** Load it before acting on a ladder.

⚠ The compiler's own per-phase timing is a **different thing**: `--metrics=<path>` writes a TSV whose
7th field is `cputicks`, and `--log=compiler:debug` prints a timing table with a `cpu%` beside the wall
`%`. **A phase where the two disagree spent its wall time NOT RUNNING** — `load` is 51.2% of wall but
25.4% of CPU because it waits on IO; `regalloc` is 22.2% of wall and 36.1% of CPU.

## ⭐ `scripts/self-host-ab.sh` — the EMITTED-CODE instrument

A green suite and `scale-test` both measure the compiler's LOGIC, which every stage of the self-host
chain shares byte for byte. **The QUALITY OF THE CODE shv2 EMITS is a different question, and this is
the one command that answers it.** It builds stage-2 and stage-3, `cmp`s them (the fixpoint gate — a
difference is a MISCOMPILE), times both self-compiles, and runs `scale-test` on stage-1 and stage-2
INTERLEAVED. ~15 min; writes only under `temp/selfhost/`. **How to read its ratio table is in the
`optimize` skill.**

## `tests/` — fixture corpora for the DRIVER COMMANDS

**`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
(user ruling), and could not be gated there anyway: a spec case is a Maxon PROGRAM the harness
compiles and runs, so it can reach `stdlib/` and nothing else. Driver commands are gated by spawning
the compiler at a fixture project and asserting what it reports.

**`tests/README.md` is the authority** — it lists every corpus, the constant each is reached through,
and the rules that keep the corpora honest. Read it before touching anything under `tests/`. The three
facts worth knowing before you get there:

- ⭐ **RUN EACH CORPUS UNDER BOTH COMPILERS, AND THEY MUST AGREE** (same pass/fail, same count). That
  is what closes the circularity: a runner broken badly enough to report green having run nothing
  cannot detect itself, so the bootstrap is the independent oracle.
  ```
  ./maxon-shv2/.maxon/maxon-shv2.exe test tests/test-command
  ./bin/maxon.exe                    test tests/test-command
  ```
  ⚠ **Nothing runs these automatically** — `/land`'s battery is where they belong, beside the suite
  and the self-compile.
- ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — e.g. `python
  tests/fmt/generate-expectations.py` runs the BOOTSTRAP and records its real answers. That is what
  makes a corpus a parity oracle for a port rather than a record of what its author expected. Re-run
  the generator after changing any input.
- ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`** unless its corpus's row says so.
  Names are `<x>.fixture` and `dot-git/`, mapped back at staging time — git refuses to commit a path
  with a `.git` component, and a real `.maxon` under `tests/` is walked by `maxon fmt`, which is the
  tool under test rewriting its own oracle.

One test per file is structural, not tidiness: a file is what ONE process runs and that process has a
5 s default deadline, so twelve compiler-spawning tests in one file report a spurious `TIMED OUT`.

## `maxon fmt`

`maxon fmt [<file|directory>]` — both compilers, gated byte-for-byte against each other by
`tests/fmt/`. ⚠ **With NO PATH it formats the whole current directory** — that is its documented
default. `fmt <file>` formats only that file, `fmt <dir>` that directory; `fmt --check` and `fmt a b`
are REJECTED, exit 1, nothing written. The walk prunes any directory holding `.git`, so it cannot
descend into a nested checkout or an agent worktree.

⚠ **`maxon fmt-selftest` runs on every `dotnet build` and FAILS IT** if formatting loses a comment,
duplicates one, writes a lexer-error sentinel into a file, or stops being idempotent. It carries 8
comment shapes + 4 unlexable sources, every one sabotage-proved, with `UrlInPlainString` and
`NoMultilineLiteral` as controls that must stay GREEN under the sabotage. Three separate silent
source-corrupting defects reached the tree before it existed; a preservation check phrased as
*presence* passes duplication, so it asserts **multiplicity**.

## ⚠ Running a suite by hand: REDIRECT IT TO A FILE. Never pipe through `head`/`tail`/`grep`.

```
mkdir -p temp
./maxon-shv2/.maxon/maxon-shv2.exe spec-test > temp/shv2-spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/shv2-spec.log          # shv2's marker; the bootstrap's is `[FAIL]`
```

Then **read the file** at each hit for the full reason. **A pipe decides what to keep before you know
what failed**, so when the run goes red the detail is already gone and the only way back is running
the whole suite again. Grep alone is not enough either: a failed compile embeds the compiler's entire
stderr, so the marker line is a headline, not the evidence.

Do not assume the console is small: **shv2's runner prints one line per test (~1,500) and only then
the summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines
while the reason sits thousands of lines above. (The bootstrap prints only failures unless
`--verbose`.) `temp/` is gitignored. The MCP tools need none of this.

⚠ **A BOOTSTRAP `spec-test` RUN DELETES EVERY `*.exe`, `*.ir_exe` AND `*.mxdbg` UNDER `temp/`,
RECURSIVELY** (`TestRunner.CleanupExecutables`, `maxon-sharp/Testing/TestRunner.cs:890`, on EVERY run,
filtered or not). It is cleaning fragment binaries and cannot tell them from one you staged there
yourself — an A/B that parks two compilers under `temp/` loses them to the next C# suite run,
silently, exit 0. shv2's runner deletes only its own `specs-shv2/.spec-tmp/` files but runs every test
binary with cwd `temp/`, so the directory is shared. **Stage a binary you must keep somewhere else.**

## Code Quality

Apply these standards when writing or reviewing any code:

- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes
  pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the
  unhandled path, not return a default value. Never use a bare `default` case in `match` — use
  `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully
  implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of
  `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there
  is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent
  change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — collapse multiple cases with the same result into one.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — a function that cannot return a valid value must throw, not return
  `""`, `-1`, `null`, or similar.
- **Blank lines for readability** — around control flow statements and between logical sections.
- **No magic values** — replace bare literal constants with named `static` constants that describe
  their meaning. Group a related set into a `static enum` rather than scattering them.

### ⭐ COMMENTS — CONCISE, MINIMAL, "WHY" ONLY, PRESENT TENSE

Binding on every comment you write **and on every comment you touch**:

- **Minimal and concise.** The default is **no comment**. Write one only where the code cannot carry
  the point by itself, and then in as few words as it takes. A comment per line, a banner over every
  section, and a restated signature above a function are all noise — and noise is unverified prose
  that rots while the code keeps working.
- **"Why", never "how".** The code is the "how". Comment what the reader cannot recover from it: the
  constraint, the invariant, the reason this order/bound/branch is correct, the cost that motivated an
  unobvious shape.
- ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
  "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit, no reference
  to a former name. **Git holds the history; a comment holds the present.** The reason a guard exists
  is a *why* and belongs — but state the constraint that still binds ("callers may hand this an
  unsorted list"), never the edit that introduced it. *(This bans history in SOURCE COMMENTS. Docs and
  commit messages are where a measurement, an incident and a correction get recorded.)*
- **Editing a comment means REWRITING it to conform** — or deleting it. Never leave a conforming edit
  inside a non-conforming comment.

## Error codes — ONE registry, ONE parser, and neither is a compiler's

**`docs/error-codes.txt` is the single source of truth for the 4-digit error-code space.**
**FOUR** files are **GENERATED** from it and must never be hand-edited:

```
maxon-sharp/Compiler/ErrorCode.g.cs                (C# bootstrap's enum)
maxon-selfhosted/Compiler/ErrorCodeRegistry.maxon  (v1's enum)
maxon-shv2/Compiler/ErrorCodeRegistry.maxon        (shv2's enum)
docs/error-codes.json                              (the artifact TOOLS read)
```

**To add a diagnostic:** take the next free number in the right band, add an entry to
`docs/error-codes.txt` (canonical name + `doc` + a `csharp`/`selfhosted`/`shv2` line per compiler that
will emit it), run **`maxon error-codes generate`**, **write the code that emits it**, and commit the
regenerated files with your change.

- **`maxon error-codes check` FAILS THE BUILD** on a duplicate number (naming both claimants), a
  duplicate name, a generated file that has drifted, or a **DEAD CLAIM**. Do not grep an enum to find
  a free number — the enums are derived, and grepping one of three copies is how two agents took
  E3099 on the same day.
- **It runs wherever a generated file is USED, not only where it is produced**: on every
  `dotnet build`, AND on `maxon build maxon-shv2` / `maxon build maxon-selfhosted`.
- **A CLAIM MUST BE LIVE** — a `csharp`/`selfhosted`/`shv2` line must name a member that compiler's
  source actually mentions. (The converse is structural: `ErrorCode.Foo` does not compile unless the
  registry generated `Foo`. Keep it that way.)
- **A reserved number is a real entry** (`reserved <why>`, no compiler claims). A reservation written
  in a comment is not a reservation.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written
  down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the generated member
  (`ErrorCode.SemanticUnneededCast`, plus `.Format()` for the `"E3010"` spelling). A literal `"E3010"`
  in a source file is a fourth copy of the number space: renumber the code and every gate stays green
  while the code that matched it silently stops matching anything.
- **The format has exactly ONE parser** (`maxon-sharp/Compiler/ErrorCodeRegistry.cs`). **Do not write
  a second one.** Tools read `docs/error-codes.json`, which the parser generates and stamps with a
  hash of the registry's bytes. `lookup_error_code` re-hashes before trusting it and **REFUSES** if
  stale — run `maxon error-codes generate`.

## Spec files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests using RequiredIR fail, regenerate with `--update-required` **plus a `filter`**.
- shv2's `--update-required` regenerates RequiredIR but **not** `maxoncstderr` blocks — an error-code
  renumber moves those by hand.

## ⚠ The MCP server's binary is gitignored and nothing rebuilds it

**If you edit anything under `maxon-dev-mcp/mcp/` — or pull a commit that does — the fix has THREE
steps, in this order: (1) KILL the running MCP server process, which holds an open handle on its own
binary; (2) `maxon build maxon-dev-mcp/mcp`; (3) RESTART THE SERVER.** Rebuilding first fails with
`E6002: could not remove the previous build artifact ... it is locked or read-only`, and a rebuild
alone does not replace the running process.

You will not get away with forgetting: **every `tools/call` compares the running binary's timestamp
against its own sources and REFUSES if a source is newer**, naming the file and the fix (`tools/list`
still answers, so the host can tell you why). A tool that answers confidently from stale code is worse
than one that refuses.
