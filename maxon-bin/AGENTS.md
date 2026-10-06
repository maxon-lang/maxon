## The compiler

One compiler builds this tree and it is written in Maxon: source `maxon-bin/`, binary
`maxon-bin/.maxon/maxon`, suite `specs/`. On Windows the binary is `maxon.exe`; commands below show
the Windows form.

⭐ **TWO SOURCE TIERS ARE READ ON EVERY COMPILE, AND BOTH LIVE AT THE CHECKOUT ROOT.** `stdlib/` is the
standard library; `runtime/` beside it is the language runtime. The compiler locates `stdlib/` by
walking UP from its own executable and reaches `runtime/` as its sibling, so a tree holding one without
the other compiles nothing.

⛔ **A `runtime/` FILE IS NOT COMPILER SOURCE AND NEEDS ONE SELF-COMPILE, NOT TWO.** It is input the
compiler READS. `Compiler/Runtime/` and the per-target `*Runtime*.maxon` files, which the compiler WRITES
into every program including itself, need two — see the box below.

**Before changing `runtime/`, or the compiler's handling of it, read `runtime/README.md`** — the tier's
families, the reserved-name doors, reach and dead-function elimination, the `.data` layout, the `__Raw`
rows and the pinned geometry. The rules that bind every tier edit: no managed value in tier source
(E3153); a restated geometry figure owes its pin, and moving one takes a staged build; tier source does
not discard a declared callee's result; a new unprefixed tier entry owes the declaration and call doors
`maxon_force_segfault` has; a new family the compiler reaches by minting a call owes a line in
`StdlibSource.runtimeTierFileIsCompilerCalled`.

- **Build it:** `./maxon-bin/.maxon/maxon run build` at the repo root. The root `maxon.maxtasks`'s
  `build` task delegates to `maxon-bin/maxon.maxproj`, whose `build` target is where the git-derived
  version comes from; `cd maxon-bin && maxon build` is the same build said from inside. ⛔ **`maxon build
  maxon-bin` from the root is a PATH build** — the root holds no `.maxproj`, so the word is a path: it
  compiles the same sources and stamps NO version, which is what the seed rule below relies on and what
  you keep away from the slot.
- **Get a compiler to build it WITH:** `scripts/fetch-seed.sh` places the latest release's binary at
  `.bootstrap/maxon.exe`, which you run directly when the slot is empty. **Re-run it after every
  release:** the stdlib may call a `__Builtins` intrinsic once a published release has it, so an older
  seed fails the first build with E3004. Maxon compiles Maxon, so there is no second
  implementation here — a previous build of this compiler is the only thing that can build it.
  ⛔ **A DECLARATION NO PUBLISHED RELEASE KNOWS IS REFUSED BY EVERY SEED THERE IS** — E2015 on a
  `__Managed*` entry this tree adds, which no re-fetch can cure. `scripts/build-from-seed.sh` stages
  `scripts/seed-shim/` over the first build for exactly that; `docs/RELEASING.md` owns the rule.
  ⛔ **NAME THE OUTPUT WHEN YOU BUILD WITH THE SEED, ALWAYS:**
  ```
  ./.bootstrap/maxon.exe build maxon-bin --output=maxon-bin/.maxon/maxon
  ```
  **A SEED OLDER THAN NAMED MANIFEST TARGETS READS `maxon-bin` AS A PATH, NOT A TARGET**, and a path
  build picks its own output name. MEASURED with the v0.1.0 release as the seed: it wrote
  `maxon-bin/Compiler/BorrowCheck.exe`, left the slot EMPTY, and **exited 0** — so the next command is
  a bare `127` about a compiler that was never written. `scripts/build-from-seed.sh` passes `--output=` for this reason;
  `CONTRIBUTING.md` spells it too.
- **Run the suite:** `./maxon-bin/.maxon/maxon.exe spec-test`.
- Exit code **101** means a memory leak was detected.
- There is **no `maxon clean`**.

> ### ⭐ A SELF-REBUILD RENAMES ITS RUNNING IMAGE OUT OF THE SLOT, THEN WRITES INTO THE EMPTY SLOT
>
> A compiler cannot overwrite its own running image (**E6002**), and a half-written slot is a
> compiler that answers as though it were whole. So a compiler rebuilding its own slot RENAMES its
> running image to `maxon-bin/.maxon/maxon.previous` — an OS will not let a running executable be
> deleted, but will let one be renamed — and its `.mxdbg` travels with it. ⭐ **THE RENAME HAPPENS ONLY
> AFTER THE COMPILE SUCCEEDS**, so a FAILED build (a compile error included) leaves the running compiler
> in the slot, able to build the fix; a write that fails after the rename moves it back. An older
> `.previous` that another process is still running (an `mcp-server` or `lsp-server` started before the
> last rebuild) is renamed aside to `maxon.retired-<stamp>`, and every self-rebuild deletes the retired
> images nothing holds any longer.
>
>
> ⛔ **A CHANGE TO EMITTED RUNTIME — `Compiler/Runtime/` OR `Compiler/Targets/*/*Runtime*.maxon` — NEEDS
> *TWO* SELF-COMPILES BEFORE THE COMPILER ITSELF BEHAVES THAT WAY.** The compiler EMITS the runtime
> into every program it builds — including into itself — so
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
> ⚠ **`fixpoint.sh` DOES NOT CATCH THIS.** It builds two stages under `temp/fixpoint/` and compares
> them — both are past the convergence point, so they agree while the SLOT still holds `C1`.
>
> ⭐⭐ **`maxon run build` DOES THE SECOND BUILD ITSELF.** The compiler stamps the commit it was built
> from, so *"has any runtime file changed since the running compiler was built"* is a `git diff` — it
> counts uncommitted changes too — and `maxon-bin/maxon.maxproj` asks it on every `run build`. When the
> answer is yes, or unknown, it prints the reason to stderr and describes the build with
> `rebuildWithOutput: true`; the driver then runs the compiler it just wrote with the same command line
> and `MAXON_SECOND_STAGE=1`, which ends the repetition. While a runtime edit is uncommitted, every
> `run build` therefore compiles twice.
> ⇒ **To iterate on runtime with ONE build, set `MAXON_SECOND_STAGE=1` for that `run build`.** Programs
> `C1` builds already get the new runtime; only the compiler's own process — the spec harness, the
> self-compile timing — needs `C2`. The MCP `build` tool with path `maxon-bin` is a path build: it runs
> no manifest, so it builds once.
> ⛔ **THIS RULE IS ONLY ABOUT EMITTED RUNTIME. EVERY OTHER CHANGE NEEDS ONE BUILD**, and a needless
> self-compile is a minute off every task that touches the compiler.
> ⛔ **THE COMPILER THAT BUILDS THIS TREE MUST LIVE INSIDE IT.** `stdlib/` and its sibling `runtime/` are
> found by walking up from the EXECUTABLE, so an installed `maxon` on PATH compiles this repository
> against the RELEASE's sources — MEASURED: it succeeds and exits 0, having built a compiler from a
> library that is not this tree's. Run `.bootstrap/maxon` or the slot binary, never a PATH one.
>
> ⛔ **`.bootstrap/` HOLDS THE BINARY AND NOTHING ELSE.** A release archive ships its own `stdlib/` and
> `runtime/`, and the compiler resolves both by walking UP from its own executable — so an archive
> unpacked whole would leave a RELEASED stdlib and runtime one directory above the compiler and the
> tree's own would never be reached. The build would succeed and compile the wrong sources, silently.

## maxon MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`**)

**The server IS the compiler**: `maxon mcp-server --dev`, implemented under `maxon-bin/Compiler/Mcp/`.
There is no separate project and nothing to build but the compiler itself, so a rebuild of the slot is
a rebuild of the server. Prefer these tools over raw Bash invocations: faster (no shell startup),
structured results. Use Bash only where no tool covers the case.

⚠ **A RUNNING SERVER IS THE COMPILER YOU BUILT IT FROM.** Rebuilding the slot renames the running
image to `maxon.previous` and writes a new one; the live process keeps serving from the vacated image
until the host restarts it. So after a build, a tool answer still comes from the PREVIOUS compiler —
restart the MCP server when you need the new one to answer.

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> ONE stdio server process is shared by every agent in every worktree, and its default root is the
> main checkout (derived from the SERVER's own binary path). **Say nothing and you are told
> `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree root — to EVERY tool call
> that acts in a tree**: `build`, `run_spec_test`, `run_scale_test`, `spec_test_outcome`, and `execute`,
> `test` and `fmt` when you mean YOUR tree's compiler to answer. Those last three default to the
> host's working directory and name no tree, which is right for a path you wrote yourself and wrong
> for a worktree whose compiler you want exercised. `check`, `dump_ir`, `lookup_error_code` and `info`
> take no `repoRoot` at all: the first two compile INSIDE the server, so they always answer about the
> server's own compiler — read the `executable` and `stdlibRoot` fields they carry before believing a
> verdict about your tree.
>
> ```
> build(repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> - **Every result echoes the `repoRoot` it actually used**, in the payload's `repoRoot` field —
>   answers and refusals alike. **READ IT BACK.** A tool that named no tree leaves the field out
>   rather than reporting an empty one, so an absent `repoRoot` means "the host's directory".
> - ⭐ **THE TREE'S OWN COMPILER RUNS, NOT THE SERVER'S.** A tool acting on `repoRoot` spawns
>   `<repoRoot>/maxon-bin/.maxon/maxon`, because `stdlib/` and `runtime/` are resolved by walking UP
>   from the EXECUTABLE — the server's binary would compile your worktree against the MAIN repo's
>   sources. A tree whose slot is empty is REFUSED, naming the `build` tool.
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`), never quietly swapped
>   for the main repo. Relative paths are refused too — they would resolve against the *server's* cwd.
>   A checkout is any tree holding `stdlib/`, `runtime/` and `maxon-bin/`, so a brand-new worktree qualifies
>   before anything is built in it.
>
> ⚠ These tools **EDIT** the tree they are pointed at: `run_spec_test` with `updateRequired: true`
> re-mints that tree's inline spec blocks, `run_scale_test` with `note:` writes a row into its
> `docs/optimization-log.md`, and `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build the compiler | `./maxon-bin/.maxon/maxon run build` — MCP `build` with `path: "maxon-bin"` is a path build that writes `maxon-bin/maxon-bin.exe` and leaves the slot as it was; `from:` names the compiler to build WITH |
| Run the spec suite | `run_spec_test` |
| Per-test PASS/FAIL detail | `spec_test_outcome` (requires `filter`) |
| MEASURE per-phase memory + CPU scaling — an instrument, **no verdict** | `run_scale_test` |
| Run an inline snippet or a file | `execute` — `source:` for a snippet, `path:` for a file |
| Dump IR | `dump_ir` |
| Format a file or snippet | `fmt` — `source:` returns the formatted text; `path:` rewrites in place |
| Look up a 4-digit error code | `lookup_error_code` — number, `"E3014"`, or the case name |

⭐ **AN ARGUMENT NO TOOL DECLARES IS REFUSED** (`invalidParams`), never dropped: a `mmTrace: true` run
that never traced, reported back as a clean success, is indistinguishable from a leak-free run. The
refusal is by ARRIVAL against the tool roster, so it covers `mmTrace` and `dumpStages` and every other
argument nobody thought to reject. A contributor argument sent to a server started WITHOUT `--dev` is
refused the same way.

Always pair `updateRequired` with a `filter` — unfiltered, it re-mints the inline blocks of every selected case in the suite.

⛔ **`build`'s `target:` IS A TRIPLE WHEN IT PARSES AS ONE OF THE FIVE, AND A PROJECT TARGET OTHERWISE.**
`wasm32-wasi` becomes `--target=wasm32-wasi`; any other value becomes a positional naming a target of
the `.maxproj` file, each `_` written `-` (`my-app` is `my_app`). A word naming no target is a SOURCE
PATH to `maxon build`, so a triple has to travel as `--target=`.

**A `spec-test` pattern is a CASE-SENSITIVE substring** of the `<spec>/<test>` label, and `--filter=`
is repeatable: the run takes the union, as N jobs across the pool. **Select a set spanning several spec
files in ONE run** — one `--filter=<spec>/` per file (MCP: a `filter` array) — never one run per file,
which pays the startup per file and runs each on one worker. A comma is part of a pattern
(`--filter=static-methods,enums` selects nothing and is refused). A pattern that selects no case refuses
the whole run, naming it, so a typo'd member cannot run nowhere while the rest reads green. (`maxon test`
lowercases its patterns and also splits a value on commas.)

The runner has no `--verbose` (it always prints a line per test), no `--no-batch` (`--batch=off` turns
off program batching; how a lane's executions are grouped is `RunStrategy`, chosen by target and host)
and no `--debug-info`; it does have `--network`.

### Common flags

- `--filter=PATTERN` (repeatable), `--update-required`, `--batch=on|off`, `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`),
  `--workers=<n>`, `--target=ARCH-OS`, `--network`.
- ⛔ **There is no `--mm-trace` on any command.** The driver refuses an unimplemented flag loudly
  (`Main.MaxonArgs.parse`, which cites this very spelling as the reason it must), so a leak is read off
  the RUN: the runtime's leak gate is **exit 101**, and a case pinning `exitcode 0` reddens on one.
- **`--workers=1` is a DEBUGGING TOOL, not a gate.** It is the same pool with one worker in it, and
  the parent buffers results and reports in fixed order — **ordering cannot vary with pool size**.
  The default pool is `defaultWorkerCount()`, this machine's CPU count, and that is the only count these
  processes run the suite at.

### Targets

The compiler emits `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` and **`wasm32-wasi`** (a
WASI Preview2 component).

> #### ⛔⛔ `--target=` CROSS-COMPILES THE PROGRAMS. IT NEVER HOSTS THE COMPILER.
>
> `spec-test --target=x64-linux` on a Windows host builds and runs the TEST binaries for Linux while
> the compiler stays a Windows process. So that lane never runs the compiler's own `main` **as** a
> Linux program — on a Linux green thread, through the Linux runtime, with that lane's frame layout —
> and it is not evidence about anything that only happens there. **CI hosts every target on its own
> architecture**: seed → `C1` → `C2` → the suite under `C2`, which is a whole class of defect a cross
> lane cannot reach.
>
> MEASURED: the x64 large-frame page walk touched-then-compared, so its last store landed below the
> frame base (`ca4abf52b8`). Harmless on an OS-grown stack; a SIGSEGV on a green thread's exact mmap'd
> one — and reachable only once the called-once inliner carried the compiler's own `main` past one
> page. **Three consecutive pushes to `main` died at exit 139 in `build-from-seed.sh` before the suite
> could start, while the cross lane read 7706/0 on the same tree.**
>
> ⇒ **TO HOST x64-linux LOCALLY, CROSS-BUILD ONCE AND THEN STAY INSIDE WSL** — the recipe is in the
> `compiler-workflow` skill.

`run_spec_test` takes `target: "wasm32-wasi"` and runs the output under the
vendored wasmtime. By hand, for ONE program:

```
./maxon-bin/.maxon/maxon build f.maxon --output=out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

⛔ **`vendor/` IS GITIGNORED EXCEPT `vendor/wasi-wit/`, SO A CLONE HAS NONE OF THE TOOL BINARIES** —
`scripts/fetch-vendor.sh` stages them; see the `compiler-workflow` skill.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY".** Heap, `String`, `print`, structs, arrays, closures,
interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there; the two lanes
run within a few hundred cases of each other. The families it does not run carry an explicit
`<!-- unsupported-targets: … -->` exclusion: **async / green threads, the clock builtins, argv**, plus
the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names), which are about x64's
output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on that lane, not
an out-of-slice case to refuse.**

⭐ **THE MARKER NAMES THE LANES THAT CANNOT SERVE A CASE, NEVER THE ONES THAT CAN**, so a backend that
lands inherits every unmarked case instead of being excluded from all of them at once. The harness
REFUSES a key naming no supported target, refuses a marker that excludes every one of them (a case that
runs nowhere is fixed or deleted), and refuses the retired `<!-- targets: -->` spelling.

⛔⛔ **DO NOT MARK A CASE THE COMPILER ALREADY REFUSES.** A lane with no substrate answers **E3104**, and
the harness reports that as a counted **SKIP** naming the case; a marker removes the case from selection
with nothing said anywhere. The two are not two spellings of one fact — one is the fact and the other is
the fact made invisible. So async, the clock, argv, file and directory IO and console stdin carry NO
marker on wasm: every one of those skips is a case a reader can count. A marker is for a case that would
otherwise go RED — an ISA-specific codegen reading, a POSIX/Windows shell spelling, a diagnostic
displaced by E3104 — and it states its reason.

⚠ **SUBPROCESS IS THE EXCEPTION, AND NEEDS THE MARKER.** A target that can never host a child process
answers **E3074** ahead of E3104 (`requireTargetSupportsCallee`), and the harness counts only E3104 as a
SKIP — so an unmarked subprocess case goes RED on wasm. Mark it `wasm32-wasi`.

## Measuring and self-checking — the `compiler-workflow` skill

Two instruments live in that skill, with their reading guides: `run_scale_test`, the per-phase memory/CPU
doubling ladder to run after any change to a pass, the IR, or a data structure the compiler indexes by;
and `scripts/fixpoint.sh`, which answers whether the compiler reproduces itself byte for byte — a
difference there is a MISCOMPILE, and a green suite cannot see it. Load the skill before acting on either.

## `tests/` — fixture corpora for the DRIVER COMMANDS

**`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
(user ruling), and could not be gated there anyway: a spec case is a Maxon PROGRAM the harness
compiles and runs, so it can reach `stdlib/` and `runtime/` and nothing else. Driver commands are
gated by spawning the compiler at a fixture project and asserting what it reports.

**`tests/README.md` is the authority** — it lists every corpus, the constant each is reached through,
and the rules that keep the corpora honest. Read it before touching anything under `tests/`. The three
facts worth knowing before you get there:

- ⭐ **RUN EACH CORPUS AND READ ITS PASS/FAIL COUNT.** A runner broken badly enough to report green
  having run nothing cannot detect itself, so the count is what closes the circularity.
  ```
  ./maxon-bin/.maxon/maxon.exe test tests/test-command
  ```
  ⚠ **CI runs six of them on every lane** — `tests/lsp`, `tests/fmt`, `tests/spec-harness`, `tests/ladders`, `tests/build-manifest` and
  `tests/cli` — plus `tests/debug`,
  `tests/dap` and `tests/mcp` on every native lane, each through its own host's debug backend, and `/land`'s battery runs `fmt`, `spec-harness`,
  `ladders` and `examples` beside the suite and the self-compile. Every other corpus runs only when someone names it.
- ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — e.g. `python
  tests/fmt/generate-expectations.py` runs the compiler and records its real answers, so a corpus
  pins what the tool DOES rather than what its author expected. Re-run the generator after changing
  any input, and read the diff: a generated expectation cannot tell you an answer is wrong.
  **`tests/examples/` is the one exception**: it checks the example programs against answers that
  exist outside this compiler (the Benchmarks Game's published output, an example's documented
  result), because there a generated expectation would only record whatever the compiler said.
- ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`** unless its corpus's row says so.
  Names are `<x>.fixture` and `dot-git/`, mapped back at staging time — git refuses to commit a path
  with a `.git` component, and a real `.maxon` under `tests/` is walked by `maxon fmt`, which is the
  tool under test rewriting its own oracle.

One test per file is structural, not tidiness: a file is what ONE process runs and that process has one
deadline, so twelve compiler-spawning tests in one file share it and report a spurious `TIMED OUT`.

⚠ **MOST OF `tests/debug`'s CASES DEBUG A RUNNING PROGRAM**, which is a compile plus a
debugged run inside one file's deadline.

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

⚠ **THE FORMATTER ENGINE'S GATE IS `tests/fmt/engine-cases.maxtest`, NOT `spec-test`.** It formats
the 30 sources in `tests/fmt/engine-cases/` (4 of them unlexable) with the real `maxon fmt`, twice, and
goes red if an answer differs from its `.expected`, a comment is lost or duplicated, a lexer-error
sentinel is written into a file, or a second run moves anything. `UrlInPlainString`,
`NoMultilineLiteral` and `PlainStringNoInterpolation` are negative controls that must stay GREEN.
Three separate silent source-corrupting defects reached the tree before the formatter had a gate; a
preservation check phrased as *presence* passes duplication, so it asserts **multiplicity**. Run it with
`maxon test tests/fmt`.

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

Do not assume the console is small: **the runner prints one line per test (thousands of them) and only
then the summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines
while the reason sits thousands of lines above. `temp/` is gitignored. The MCP tools need none of this.

⚠ **THE SUITE RUNS EVERY TEST BINARY WITH CWD `temp/`**, so that directory is shared with anything
else you put there. Stage a binary you must keep somewhere else.

## Error codes — ONE registry, and it is the enum itself

**`maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry.** It carries the number, the canonical
name and the doc text for every code, and it is AUTHORED — edit it directly.

**To add a diagnostic:** take the next free number in the right band, add a case, write the code that
emits it.

⛔ **Not its doc comment.** That block is a documentation surface, not a code comment —
`Mcp/McpErrorCodes.maxon` re-parses this file at runtime to serve `lookup_error_code`, and the site's
Error Codes page is generated from it — so the `documenter` skill writes it, with every other comment
the change gets, just before the commit. ⚠ `website/scripts/sync-docs.mjs` fails closed on a case with
no doc comment, which is how it finds them.

- **A duplicate NAME does not compile**, and `ErrorCode.Foo` does not compile unless the enum declares
  it — both are structural, so neither needs a checker.
- **A duplicate NUMBER is neither**: two cases may carry one `"E3099"` and the program is well-formed.
  `website/scripts/sync-docs.mjs` refuses one while it reads the registry, naming BOTH lines —
  `website.yml` runs it on every change to the registry, and `/land`'s battery runs it too. `spec-test`
  does not check this.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written
  down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the registry's member
  (`ErrorCode.semanticUnneededCast`, plus `.rawValue` for the `"E3010"` spelling). A literal `"E3010"`
  in a source file is a second copy of the number space: renumber the code and every gate stays green
  while the code that matched it silently stops matching anything.

## Spec files

- **`specs/` pins no emitted code; `ir-specs/` is the emitted-code suite.** `maxon spec-test
  ir-specs` runs the cases whose subject is the emitted code; each carries a ` ```TargetIr:<lane> ` block per
  native lane (`x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux`) holding the Target IR the compiler
  renders for it, and a block for the lane being run is a GATE: a differing compile fails the case and shows
  the first differing line. A pin is compared against the COMPILE, so `--target=arm64-macos` on Windows checks
  it too (the cases report NOTRUN). A change that moves emitted code re-mints the pins it moves with
  `--update-required --filter=<spec>/` on each lane, reads the diff, and commits it with the change; CI runs
  `spec-test ir-specs` natively on every lane beside the suite.
- **A default run batches.** A spec's plain run cases compile into the fewest programs whose type names and
  extension methods do not overlap and run once each; a case whose end marker is missing or reports green
  threads still live, and every case of a program that exited non-zero, reruns alone from the same binary,
  and output outside every marker FAILS every case of that program. A batched FAIL is compiled alone only
  to diagnose it: a solo PASS turns into a FAIL naming the batching gap, which is fixed by a new
  `SoloReason` or a `<!-- process: own -->` marker. `SpecBatch.placementOf` is the one list of reasons a
  case runs alone. `--batch=off` runs every case alone, and the summary line says how many ran batched
  in how many programs.
- A spec fence the harness does not read (` ```RequiredIR ` among them) is refused, naming the case.
- `--update-required` re-mints the trace-capture blocks (` ```mm-trace `, ` ```log-trace `) and the
  ` ```TargetIr:<lane> ` pins in a spec file but **not** its `maxoncstderr` blocks — an error-code renumber
  moves those by hand.

## ⚠ A running MCP server keeps answering from the compiler it was started as

The server is the compiler, so editing `maxon-bin/Compiler/Mcp/` and rebuilding the slot leaves the
LIVE process running the old code: a build renames the running image to `maxon.previous` rather than
overwriting it (an OS will not let a running executable be deleted, but will let one be renamed), and
the process serves on from the vacated image. **RESTART THE MCP SERVER after a build whose result you
want the tools to reflect.**

`tests/mcp/rebuild.maxtest` pins the surviving half of that: the process keeps answering across the
replacement rather than dying mid-session.
