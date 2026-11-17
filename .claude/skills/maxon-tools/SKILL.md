---
name: maxon-tools
description: Drive the Maxon compiler's tools and suites and read what they report — the `maxon` MCP tools (`repoRoot` in a worktree, the task→tool table, the arguments they refuse), `spec-test` filters and flags, running a suite or corpus by hand without losing its failures, the traps that make a red, green or empty result lie, how a rebuild swaps the slot, `maxon fmt`'s paths, and the cross and wasm lanes by hand. Load it before running `build`, `spec-test`, `test`, `fmt` or any `maxon` MCP tool, and before believing a run that went red, green or silent for no visible reason.
---

# Driving the compiler's tools

`maxon-bin/AGENTS.md` holds how to build the compiler and the seed; this skill holds how to run it and read
its answers.

## The `maxon` MCP tools — prefer them

**The server IS the compiler**: `maxon mcp-server --dev`, implemented under `maxon-bin/Compiler/Mcp/`. There
is nothing to build but the compiler, so a rebuild of the slot is a rebuild of the server. Prefer these tools
over Bash — no shell startup, structured results — and use Bash only where no tool covers the case.

⚠ **A RUNNING SERVER IS THE COMPILER YOU STARTED IT FROM.** A rebuild renames the running image to
`maxon.previous`, and the live process keeps serving from it until the host restarts it, so after a build a
tool answer still comes from the PREVIOUS compiler. Restart the server when the new one must answer.
`tests/mcp/rebuild.maxtest` pins that the process keeps answering across the replacement.

> ### 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> One stdio server process is shared by every agent in every worktree, and its default root is the main
> checkout (derived from the server's own binary path). **Say nothing and you are told `success: true`
> about a tree containing none of your work.**
>
> ⇒ **Pass `repoRoot` — the ABSOLUTE path of your worktree root — to every tool call that acts in a
> tree**: `build`, `run_spec_test`, `run_scale_test`, and `execute`, `test` and `fmt` when you mean YOUR
> tree's compiler to answer. Those three default to the host's working directory and name no tree, which
> is right for a path you wrote yourself and wrong for a worktree whose compiler you want exercised.
> `check`, `dump_ir`, `lookup_error_code` and `info` take no `repoRoot`: the first two compile INSIDE the
> server, so they answer about the server's own compiler — read the `executable` and `stdlibRoot` fields
> they carry before believing a verdict about your tree.
>
> ```
> build(repoRoot: "<absolute path of your worktree>")
> ```
>
> - **Every result, answer or refusal, echoes the `repoRoot` it used** in the payload's `repoRoot` field.
>   **Read it back.** A tool that named no tree leaves the field out, so an absent `repoRoot` means "the
>   host's directory".
> - ⭐ **The tree's own compiler runs, not the server's.** A tool acting on `repoRoot` spawns
>   `<repoRoot>/maxon-bin/.maxon/maxon`, because the server's binary would compile your worktree against
>   the main repo's `stdlib/` and `runtime/`. A tree whose slot is empty is refused, naming the `build` tool.
> - **A `repoRoot` that is not a Maxon checkout is refused** (`invalidParams`), never swapped for the main
>   repo, and so is a relative path, which would resolve against the server's cwd. A checkout is any tree
>   holding `stdlib/`, `runtime/` and `maxon-bin/`, so a new worktree qualifies before anything is built.
>
> ⚠ These tools **EDIT** the tree they point at: `run_spec_test` with `updateRequired: true` re-mints its
> inline spec blocks, `run_scale_test` with `note:` writes a row into its `docs/optimization-log.md`, and
> `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build the compiler | `./maxon-bin/.maxon/maxon run build` — MCP `build` with `path: "maxon-bin"` is a path build that writes `maxon-bin/maxon-bin.exe` and leaves the slot as it was; `from:` names the compiler to build WITH |
| Run the spec suite | `run_spec_test` — the failures come back inline, in `failures` |
| Per-test PASS/FAIL detail | `run_spec_test` with `cases: true` |
| Measure per-phase memory and CPU scaling — an instrument, **no verdict** | `run_scale_test` (the `compiler-workflow` skill reads it) |
| Run an inline snippet or a file | `execute` — `source:` for a snippet, `path:` for a file |
| Dump IR | `dump_ir` |
| Format a file or snippet | `fmt` — `source:` returns the formatted text; `path:` rewrites in place |
| Look up a 4-digit error code | `lookup_error_code` — number, `"E3014"`, or the case name |

- ⭐ **An argument no tool declares is refused** (`invalidParams`), never dropped: a `mmTrace: true` run
  that never traced, reported as a clean success, is indistinguishable from a leak-free run. The refusal
  is by arrival against the tool roster, so it covers `mmTrace`, `dumpStages` and every other argument. A
  contributor argument sent to a server started without `--dev` is refused the same way.
- **Always pair `updateRequired` with a `filter`**: unfiltered, it re-mints the inline blocks of every
  selected case in the suite.
- ⛔ **`build`'s `target:` is a triple when it parses as one of the five, and a project target
  otherwise.** `wasm32-wasi` becomes `--target=wasm32-wasi`; any other value becomes a positional naming a
  target of the `.maxproj`, each `_` written `-` (`my-app` is `my_app`). A word naming no target is a
  SOURCE PATH to `maxon build`, so a triple has to travel as `--target=`.

## `spec-test` — selecting and flags

- **A pattern is a CASE-SENSITIVE substring** of the `<spec>/<test>` label. `--filter=` is repeatable and
  the run takes the union, as N jobs across the pool. **Select a set spanning several spec files in ONE
  run** — one `--filter=<spec>/` per file (MCP: a `filter` array) — never one run per file, which pays the
  startup per file and runs each on one worker.
- A comma is part of a pattern (`--filter=static-methods,enums` selects nothing and is refused). A pattern
  that selects no case refuses the whole run, naming it. `maxon test` instead lowercases its patterns and
  splits a value on commas.
- Flags: `--filter=PATTERN` (repeatable), `--update-required`, `--batch=on|off` (program batching),
  `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`), `--workers=<n>`, `--shard=K/N`, `--target=ARCH-OS`,
  `--network`. How a lane's executions are grouped is `RunStrategy`, chosen by target and host. The runner
  always prints a line per test.
- ⛔ **There is no `--mm-trace` on any command.** The driver refuses an unimplemented flag loudly
  (`Main.MaxonArgs.parse`), so a leak is read off the run: the runtime's leak gate is **exit 101**, and a
  case pinning `exitcode 0` reddens on one.
- **`--workers=1` is a debugging tool, not a gate.** It is the same pool with one worker; the parent
  buffers results and reports in fixed order, so ordering cannot vary with pool size. The default pool is
  `defaultWorkerCount()`, this machine's CPU count, and that is the only count the suite is run at.
- A `tests/` corpus runs with `./maxon-bin/.maxon/maxon.exe test tests/<corpus>`; read its pass/fail count
  (`tests/README.md`).

## Running a suite by hand: REDIRECT IT TO A FILE

Never pipe a suite through `head`/`tail`/`grep`. The MCP tools need none of this.

```
./maxon-bin/.maxon/maxon.exe spec-test > <scratchpad>/spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' <scratchpad>/spec.log
```

Then **read the file** at each hit for the full reason. A pipe decides what to keep before you know what
failed, so when the run goes red the detail is gone and the only way back is the whole suite again. Grep
alone is not enough: a failed compile embeds the compiler's entire stderr, so the marker line is a headline,
not the evidence. The runner prints one line per test (thousands) and only then the summary, with failures
wherever their tests fall in declaration order, so `tail` shows PASS lines while the reason sits thousands
of lines above. `<scratchpad>` is the scratchpad directory your system prompt names.

- ⛔ **A chained or backgrounded command reports the exit status of its LAST command**: `build > log;
  echo $?; tail log` reports `tail`'s 0 over a failed build. Write the status into the log (`…; echo
  EXIT=$? >> log`), read it back, and confirm the artifact exists. Join build and test with `&&`, so a
  failed build cannot be followed by a suite that runs the stale binary and reads green.
- A run that died before testing prints no `FAIL` lines: require the `N passed, M failed` summary line. A
  repeat-run loop counts the iterations that printed it, never the iterations attempted: a refused run
  (stale binary, busy tree) prints none, and a loop over a control that never built reads `0 leaks in 6 runs`.
- **The suite runs every test binary with cwd `.spec-run-<pid>/`**, a directory of that run's own at the
  checkout root. An entry a case leaves there fails the run (`left .spec-run/<name>`), and so does a process
  a case leaves running (`docs/SPECS.md`, "What a case leaves behind").
- **The tree lock judges liveness by heartbeat, never by pid** (`TreeLock.maxon`): pids are reused and a
  wedged process is alive forever. A tool-call timeout does not kill the compiler it spawned, so the retry
  meets the lock (`error: … is BUSY … Nothing was run.`, non-zero exit) — the lock working, not a red gate.
  The BUSY refusal prints the holder's argv and its last progress: a live holder means wait, and a lock
  untouched for `AbandonedAfterSeconds` (60) breaks itself on the next command.
- ⚠ **A red run can be the machine, not the code.** Failures uniform across unrelated specs, with no
  compiler output and an OS status for the exit code (Windows `3221225794`, `0xC0000142`: the process never
  started), mean orphaned workers from a killed run. Timeouts hitting a disjoint set on each run mean a
  contended machine: re-measure alone on an idle box. Before killing anything, list parents
  (`Get-CimInstance Win32_Process`): a `spec-test` parent, or new start times between two listings, means
  the run is alive. A tool reporting a task killed is not evidence its process tree died.
- ⛔ **A search that never ran reads as zero hits.** Never discard the stderr of a grep used as evidence of
  absence: `grep -P` is refused under some locales, and `2>/dev/null` turns that refusal into the cleanest
  possible answer. Prove the instrument on a hit you know exists, and prefer the Grep tool, which reports a
  failure.
- There is **no `maxon clean`**; `maxon cache clear` empties the run and library caches. Clearing the cache
  rules the cache out; it is not a fix. A hand-built program that crashes after a compiler change while the
  suite is green is a real miscompile until shown otherwise (the suite may not exercise the pattern): read
  the emitted code with `dump_ir` or a disassembler.

## How a rebuild swaps the slot

A compiler cannot overwrite its own running image, and a half-written slot is a compiler that answers as
though it were whole. So every build writes into `.staging-<hex>/` beside its output, and a compiler
rebuilding its own slot RENAMES its running image (with its `.mxdbg`) to `maxon-bin/.maxon/maxon.previous` —
an OS will not delete a running executable but will rename one — then renames the staged image into the
empty slot. ⭐ **The rename happens only after the compile succeeds**, so a failed build, a compile error
included, leaves the running compiler in the slot, able to build the fix; a staged image that cannot be
renamed in moves the running one back (E6002). An older `.previous` another process still runs (an
`mcp-server` or `lsp-server` started before the last rebuild) is renamed aside to `maxon.retired.<block>`
(`Run/ScratchName.maxon`). A self-rebuild, and any build into a slot with a `.previous` beside it (the seed,
or another tree's compiler), deletes the retired images nothing holds; only a self-rebuild also deletes the
older numbered `maxon.retired-<digits>` spelling.

`maxon-bin/.maxon/` therefore holds the slot's `maxon[.exe]`, `maxon.previous[.exe]`, their `.mxdbg`
sidecars, the `maxon.retired.<block>` images, `.staging-<hex>/`, and the `maxon-<cpu>-linux` guest compilers
`spec-test` builds on a macOS host — and nothing else (`maxon-bin/AGENTS.md`, the closed directory).

## `maxon fmt`

`maxon fmt [<file|directory>]`. ⚠ **With NO PATH it formats the whole current directory** — its documented
default. `fmt <file>` formats only that file, `fmt <dir>` that directory; `fmt --check` and `fmt a b` are
refused, exit 1, nothing written. The walk prunes any directory holding `.git`, so it cannot descend into a
nested checkout or an agent worktree. `stdlib/` and `runtime/` are held fmt-clean by a gate; `maxon-bin/`
is not, so run `maxon fmt` on every compiler file you change. The formatter's own gates are in
`tests/README.md` (`fmt/`).

## Lanes

⚠ **`--target=` cross-compiles the programs; it never hosts the compiler.** `spec-test --target=x64-linux`
on a Windows host builds and runs the TEST binaries for Linux while the compiler stays a Windows process,
so that lane never runs the compiler's own `main` as a Linux program — on a Linux green thread with its
exact mmap'd stack, through the Linux runtime, with that lane's frame layout — and a defect that needs
those stays green there. **CI hosts every target on its own architecture** (seed → `C1` → `C2` → the suite
under `C2`). To host x64-linux locally, cross-build once and then stay inside WSL: the `compiler-workflow`
skill has the recipe.

**On arm64-macos, `spec-test --target=x64-linux` is a cross lane through OrbStack's Rosetta emulation:**
every case is compiled on the Mac, and each spec's binaries run in one `orb run` entry on the `maxon-linux`
machine. Rosetta must be enabled (`orb config set rosetta true`, then restart OrbStack); the preflight runs
a minimal x86-64 ELF in the machine and names that remedy when it fails. `--target=arm64-linux` hands the
suite to a compiler built for the machine and run inside it.

⚠ **An intermittent compiler death on arm64 alone (exit 86, a SIGSEGV in a pool worker, exit 101 with
every test passing) points at memory ordering first.** x86-TSO forbids the reorder, so a clean x64 run is
no evidence. A self-compile loop is a poor instrument: compare full suites per arm, control included, and
read a clean arm against the base rate (at one death per suite, six clean suites is p≈0.25%).
Instrumentation on a hot path moves the timing that causes the death, so a silent probe excludes nothing
by itself.

### wasm32-wasi

`run_spec_test` takes `target: "wasm32-wasi"` and runs the output under the vendored wasmtime. By hand,
for one program:

```
./maxon-bin/.maxon/maxon build f.maxon --output=out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y -S inherit-env=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

`vendor/` is gitignored except `vendor/wasi-wit/`, so a clone has none of the tool binaries:
`scripts/fetch-vendor.sh` stages them (the `compiler-workflow` skill).

- ⚠ **The wasm lane is not "scalar only".** Heap, `String`, `print`, structs, arrays, closures,
  interfaces and floats (arithmetic and shortest-round-trip printing) all work there, and the two lanes run
  within a few hundred cases of each other. A float or `String` case failing on wasm is a BUG on that lane,
  not an out-of-slice case to refuse. Which cases a lane may skip, and how a case is marked, is
  `docs/SPECS.md`'s.
- ⚠ **A failure on wasm alone can be a fact the other lanes are blind to.** A call-signature width
  disagreement (an i32 result declared as i64) is invisible on x64 and arm64, which pass both in one 64-bit
  register; wasm types every function value exactly and traps `indirect call type mismatch`. The other
  lanes cannot see the disagreement; the wasm backend is not what is broken.
