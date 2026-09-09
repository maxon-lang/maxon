You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## The compiler

One compiler builds this tree and it is written in Maxon: source `maxon-bin/`, binary
`maxon-bin/.maxon/maxon`, suite `specs/`. On Windows the binary is `maxon.exe`; commands below show
the Windows form.

- **Build it:** `./maxon-bin/.maxon/maxon build maxon-bin` at the repo root. `build.maxon` there
  declares two targets — `maxon-bin` and `dev-mcp` — so a BARE `maxon build` lists them rather than
  picking one.
- **Get a compiler to build it WITH:** put a released `maxon` binary at `.bootstrap/maxon.exe`, which
  you run directly when the slot is empty. Maxon compiles Maxon, so there is no second
  implementation here — a previous build of this compiler is the only thing that can build it.
- **Run the suite:** `./maxon-bin/.maxon/maxon.exe spec-test`.
- Exit code **101** means a memory leak was detected.
- There is **no `maxon clean`**. To force a from-source stdlib rebuild, delete
  `stdlib/.maxon/cache/*.mxc`; the compiler rebuilds the stdlib whenever the cache is absent.

> ### ⭐ THE BUILD WRITES TO `.next` AND RENAMES INTO PLACE
>
> A compiler cannot overwrite its own running image (**E6002**), and a half-written slot is a
> compiler that answers as though it were whole. So a compiler rebuilding its own slot RENAMES its
> running image to `maxon-bin/.maxon/maxon.previous` first — an OS will not let a running executable be
> deleted, but will let one be renamed — and its `.mxdbg` travels with it. A FAILED build leaves the
> slot **EMPTY** rather than reinstating anything, because a stale compiler reporting as current is the
> failure every staleness refusal in this repo exists to prevent.
>
>
> ⛔ **A CHANGE UNDER `Compiler/Runtime/` NEEDS *TWO* SELF-COMPILES BEFORE THE COMPILER ITSELF BEHAVES
> THAT WAY.** The compiler EMITS the runtime into every program it builds — including into itself — so
> with `C0` the old compiler and `S` the fixed sources:
>
> - `C0` builds `S` → `C1`. `C1`'s emitter logic is fixed, so **programs C1 builds get the new
>   runtime** — but `C1`'s OWN embedded runtime was emitted by `C0`, and is old.
> - `C1` builds `S` → `C2`. Now the compiler's own runtime is new too.
>
> ⇒ **It bites hardest where the compiler is the program under test**: `spec-test`'s worker IS the
> compiler, so a runtime fix to subprocess, the scheduler or memory management does not change what the
> HARNESS does until the second build. MEASURED: a delayed-stdin fix looked like a Windows-only lane bug
> for exactly this reason — the case failed 3/3 against `C1` and passed 3/3 against `C2`.
>
> ⚠ **`fixpoint.sh` DOES NOT CATCH THIS.** It builds `stage2` and `stage3` under `temp/` and compares
> them — both are past the convergence point, so they agree while the SLOT still holds `C1`. Build
> twice whenever the seed you built with predates a runtime change.
> ⛔ **THE COMPILER THAT BUILDS THIS TREE MUST LIVE INSIDE IT.** `stdlib/` is found by walking up from
> the EXECUTABLE, so an installed `maxon` on PATH compiles this repository against the RELEASE's
> standard library — MEASURED: it succeeds and exits 0, having built a compiler from a library that is
> not this tree's. Run `.bootstrap/maxon` or the slot binary, never a PATH one.
>
> ⛔ **`.bootstrap/` HOLDS THE BINARY AND NOTHING ELSE.** A release archive ships its own
> `stdlib/`, and the compiler resolves `stdlib/` by walking UP from its own executable — so an archive
> unpacked whole would leave a RELEASED stdlib one directory above the compiler and the tree's own
> would never be reached. The build would succeed and compile the wrong library, silently.

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
> All eight take it.
>
> ```
> build(repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> - **Every result echoes the `repoRoot` it actually used** (successes in `repoRoot`, failures in
>   `error.data.repoRoot`). **READ IT BACK.**
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`), never quietly swapped
>   for the main repo. Relative paths are refused too — they would resolve against the *server's* cwd.
>   A checkout is any tree holding `stdlib/` and `maxon-bin/`, so a brand-new worktree qualifies
>   before anything is built in it.
>
> ⚠ These tools **EDIT** the tree they are pointed at: `run_spec_test` with `updateRequired: true`
> rewrites that tree's committed goldens, `run_scale_test` with `note:` writes a row into its
> `docs/optimization-log.md`, and `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build the compiler | `build` — runs `build.maxon` at the root; `from:` names the compiler to build WITH |
| Run the spec suite | `run_spec_test` |
| Per-test PASS/FAIL detail | `spec_test_outcome` (requires `filter`) |
| MEASURE per-phase memory + CPU scaling — an instrument, **no verdict** | `run_scale_test` |
| Run an inline snippet or a file | `run_program` |
| Dump IR | `dump_ir` |
| Format a file or snippet | `fmt` |
| Look up a 4-digit error code | `lookup_error_code` |

Flags are exposed as parameters: `filter`, `updateRequired`, `log`, `target`, `network`. Always pair
`updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite. `mmTrace` and
`dumpStages` name mechanisms the compiler does not have, so they are REJECTED with an `invalidParams`
error naming the gap, never silently dropped.

**A `spec-test` filter is ONE CASE-SENSITIVE substring** of the `<spec>/<test>` label (`maxon test`
lowercases its own, and takes a comma-separated union). Neither is a list here —
`--filter=static-methods,enums` selects NOTHING. Run one filter per file and read every one.

The runner has no `--verbose` (it always prints a line per test), no `--no-batch` (its batching is
`RunStrategy`, chosen by target and host) and no `--debug-info`; it does have `--network`.
`checkSpecTestFlags` refuses a flag it does not support.

### Common flags

- `--filter=PATTERN`, `--update-required`, `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`),
  `--mm-trace`, `--workers=<n>`, `--target=ARCH-OS`.
- **`--workers=1` is a DEBUGGING TOOL, not a gate.** It is the same pool with one worker in it, and
  the parent buffers results and reports in fixed order — **ordering cannot vary with pool size**.
  The default pool is 12 and that is the only count these processes run the suite at.

### Targets

The compiler emits `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` and **`wasm32-wasi`** (a
WASI Preview2 component). `run_spec_test` takes `target: "wasm32-wasi"` and runs the output under the
vendored wasmtime. By hand, for ONE program:

```
./maxon-bin/.maxon/maxon build f.maxon -o out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

⛔ **`vendor/` IS GITIGNORED AND A CLONE HAS NONE OF IT.** `scripts/fetch-vendor.sh` downloads THIS
host's build of `wasmtime` and `wasm-tools`, verifies each against a pinned SHA256 or refuses, and
stamps what it placed so a re-run is a no-op. A machine therefore holds one platform's binaries under
their natural names — `wasmtime` on unix, `wasmtime.exe` on Windows — and nothing has to remember which
extensionless file is somebody else's Mach-O. Pass a target (`fetch-vendor.sh arm64-macos`) to stage
another platform's, and `wasm-opt` by name: nothing in the tree invokes it, so it is not fetched by
default.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY".** Heap, `String`, `print`, structs, arrays, closures,
interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there; the two lanes
run within a few hundred cases of each other. The families it does not run carry an explicit
`<!-- unsupported-targets: … -->` exclusion: **async / green threads, the clock builtins, argv**, plus
the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names), which are about x64's
output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on that lane, not
an out-of-slice case to refuse.**

⭐ **THE MARKER NAMES THE LANES THAT CANNOT SERVE A CASE, NEVER THE ONES THAT CAN**, so a backend that
lands inherits every unmarked case instead of being excluded from all of them at once. The harness
REFUSES a key naming no supported target, refuses a marker that excludes every one of them (that is a
suspension — spell it `<!-- disabled-test: -->`), and refuses the retired `<!-- targets: -->` spelling.

⛔⛔ **DO NOT MARK A CASE THE COMPILER ALREADY REFUSES.** A lane with no substrate answers **E3104**, and
the harness reports that as a counted **SKIP** naming the case; a marker removes the case from selection
with nothing said anywhere. The two are not two spellings of one fact — one is the fact and the other is
the fact made invisible. So async, the clock, subprocess, argv, file and directory IO and console stdin
carry NO marker on wasm: the wasm lane runs 7024 and reports 492 skips, and every one of those skips is
a case a reader can count. A marker is for a case that would otherwise go RED — an ISA-specific codegen
reading, a POSIX/Windows shell spelling, a diagnostic displaced by E3104 — and it states its reason.

## `run_scale_test` — the scaling INSTRUMENT. ⚠ NOT A GATE.

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

⇒ **The full reading guide — the three columns, the two blind spots, A/B methodology — is
the `optimize` skill.** Load it before acting on a ladder.

⚠ The compiler's own per-phase timing is a **different thing**: `--metrics=<path>` writes a TSV whose
7th field is `cputicks`, and `--log=compiler:debug` prints a timing table with a `cpu%` beside the wall
`%`. **A phase where the two disagree spent its wall time NOT RUNNING** — `load` is 51.2% of wall but
25.4% of CPU because it waits on IO; `regalloc` is 22.2% of wall and 36.1% of CPU.

## ⭐ `scripts/fixpoint.sh` — does the compiler reproduce itself exactly?

A green suite cannot answer this: every stage shares the compiler's LOGIC, so a suite exercises the
same behaviour whichever stage ran it. Only the BYTES of two successive self-compiles say whether the
emitted code is stable, and **a difference is a MISCOMPILE** — the compiler is not a fixed point of
itself, so which binary you hold decides what your programs become. Writes only under
`temp/fixpoint/`.

⛔ **THE TWO OUTPUTS SHARE A BASENAME AND DIFFER ONLY IN DIRECTORY.** On macOS the ad-hoc
code-signature identifier is taken from the output FILENAME, so `-o stage2` and `-o stage3` differ in
exactly one byte for that reason alone — a difference that reads as a miscompile and is not one.

## `tests/` — fixture corpora for the DRIVER COMMANDS

**`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
(user ruling), and could not be gated there anyway: a spec case is a Maxon PROGRAM the harness
compiles and runs, so it can reach `stdlib/` and nothing else. Driver commands are gated by spawning
the compiler at a fixture project and asserting what it reports.

**`tests/README.md` is the authority** — it lists every corpus, the constant each is reached through,
and the rules that keep the corpora honest. Read it before touching anything under `tests/`. The three
facts worth knowing before you get there:

- ⭐ **RUN EACH CORPUS AND READ ITS PASS/FAIL COUNT.** A runner broken badly enough to report green
  having run nothing cannot detect itself, so the count is what closes the circularity.
  ```
  ./maxon-bin/.maxon/maxon.exe test tests/test-command
  ```
  ⚠ **Nothing runs these automatically** — `/land`'s battery is where they belong, beside the suite
  and the self-compile.
- ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — e.g. `python
  tests/fmt/generate-expectations.py` runs the compiler and records its real answers, so a corpus
  pins what the tool DOES rather than what its author expected. Re-run the generator after changing
  any input, and read the diff: a generated expectation cannot tell you an answer is wrong.
- ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`** unless its corpus's row says so.
  Names are `<x>.fixture` and `dot-git/`, mapped back at staging time — git refuses to commit a path
  with a `.git` component, and a real `.maxon` under `tests/` is walked by `maxon fmt`, which is the
  tool under test rewriting its own oracle.

One test per file is structural, not tidiness: a file is what ONE process runs and that process has a
5 s default deadline, so twelve compiler-spawning tests in one file report a spurious `TIMED OUT`.

## `maxon fmt`

`maxon fmt [<file|directory>]` — gated byte-for-byte by `tests/fmt/`.
⚠ **With NO PATH it formats the whole current directory** — that is its documented
default. `fmt <file>` formats only that file, `fmt <dir>` that directory; `fmt --check` and `fmt a b`
are REJECTED, exit 1, nothing written. The walk prunes any directory holding `.git`, so it cannot
descend into a nested checkout or an agent worktree.

⛔ **A SUBTREE THAT IS NOT A CHECKOUT NEEDS A `.maxonignore`, AND `website/` IS THE ONE THAT DOES.**
The `.git` rule protects a sibling repository, not a directory of this one — so without the marker a
root `fmt` rewrites `website/src/examples/*.maxon` in place and walks every directory under
`node_modules/`. MEASURED: remove `website/.maxonignore`, mis-format one of those files, run `fmt`,
and it is silently reformatted. The marker is a FLAG whose contents are never read, and both walks
honour it — `fmt`'s and the compiler's own `collectMaxonSources`.

⚠ **THE FORMATTER SELF-TEST RIDES `spec-test`** (`requireFormatterPreservesItsCorpus`, called from
`SpecWorkerPool`) and REDDENS THE SUITE if formatting loses a comment, duplicates one, writes a
lexer-error sentinel into a file, or stops being idempotent. There is no `fmt-selftest` command.
It carries 8 comment shapes + 4 unlexable sources, every one sabotage-proved, with `UrlInPlainString` and
`NoMultilineLiteral` as controls that must stay GREEN under the sabotage. Three separate silent
source-corrupting defects reached the tree before it existed; a preservation check phrased as
*presence* passes duplication, so it asserts **multiplicity**.

## ⚠ Running a suite by hand: REDIRECT IT TO A FILE. Never pipe through `head`/`tail`/`grep`.

```
mkdir -p temp
./maxon-bin/.maxon/maxon.exe spec-test > temp/spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/spec.log
```

Then **read the file** at each hit for the full reason. **A pipe decides what to keep before you know
what failed**, so when the run goes red the detail is already gone and the only way back is running
the whole suite again. Grep alone is not enough either: a failed compile embeds the compiler's entire
stderr, so the marker line is a headline, not the evidence.

Do not assume the console is small: **the runner prints one line per test (~1,500) and only then
the summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines
while the reason sits thousands of lines above. `temp/` is gitignored. The MCP tools need none of this.

⚠ **THE SUITE RUNS EVERY TEST BINARY WITH CWD `temp/`**, so that directory is shared with anything
else you put there. Stage a binary you must keep somewhere else.

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

## Error codes — ONE registry, and it is the enum itself

**`maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry.** It carries the number, the canonical
name and the doc text for every code, and it is AUTHORED — edit it directly.

**To add a diagnostic:** take the next free number in the right band, add a case, write the code that
emits it.

- **A duplicate NAME does not compile**, and `ErrorCode.Foo` does not compile unless the enum declares
  it — both are structural, so neither needs a checker.
- **A duplicate NUMBER is neither**: two cases may carry one `"E3099"` and the program is well-formed.
  `Testing/ErrorCodeSelfTest.maxon` walks `allCases`/`allCaseNames` on every `spec-test` and refuses
  one, naming BOTH claimants.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written
  down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the generated member
  (`ErrorCode.semanticUnneededCast`, plus `.rawValue` for the `"E3010"` spelling). A literal `"E3010"`
  in a source file is a second copy of the number space: renumber the code and every gate stays green
  while the code that matched it silently stops matching anything.

## Spec files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests using RequiredIR fail, regenerate with `--update-required` **plus a `filter`**.
- `--update-required` regenerates RequiredIR but **not** `maxoncstderr` blocks — an error-code
  renumber moves those by hand.

## ⚠ The MCP server's binary is gitignored and nothing rebuilds it

**If you edit anything under `maxon-dev-mcp/mcp/` — or pull a commit that does — the fix has THREE
steps, in this order: (1) KILL the running MCP server process, which holds an open handle on its own
binary; (2) `maxon build dev-mcp`; (3) RESTART THE SERVER.** Rebuilding first fails with
`E6002: could not remove the previous build artifact ... it is locked or read-only`, and a rebuild
alone does not replace the running process.

You will not get away with forgetting: **every `tools/call` compares the running binary's timestamp
against its own sources and REFUSES if a source is newer**, naming the file and the fix (`tools/list`
still answers, so the host can tell you why). A tool that answers confidently from stale code is worse
than one that refuses.
