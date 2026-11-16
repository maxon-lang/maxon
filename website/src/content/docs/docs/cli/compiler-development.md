---
title: Working on the Compiler
description: "The commands for developing the compiler itself: spec-test, scale-test, and the rebuild verifiers."
sidebar:
  order: 8
---

These commands are for people changing the Maxon compiler, in a checkout of its repository. `maxon`
with no arguments does not list them; `maxon help` does. The contributor guide,
[Contributing](/docs/contributing/), covers building the compiler and the rules for a change.

In a checkout, run the tree's own compiler, `maxon`, never an installed `maxon` on
`PATH`: the compiler finds `stdlib/` by walking up from its own executable, so an installed compiler would
compile the tree against the release's standard library.

## `maxon spec-test`

Runs the compiler's own spec suite, the test cases embedded in `specs/*.md`. For a project's unit tests,
see [`maxon test`](/docs/cli/#maxon-test).

```bash
maxon spec-test [directory] [options]
```

`[directory]` is the spec directory (default `specs`). A second positional is refused. Every selected
case is compiled inside this process, so the compiler under test is the executable you ran, and with the
[lint rules](/docs/cli/#maxon-lint) enforced, as a batch of cases is.

| Option | Description |
|--------|-------------|
| `--filter=<pattern>` | Run only tests whose `<spec>/<test>` label contains `<pattern>`, a case-sensitive substring: a spec name selects that spec, a test name selects that test, and `spec/test` selects exactly one. Repeatable: the run takes every test any pattern selects, in one pool of workers. A comma is part of the pattern. |
| `--workers=<n>` | Run on `<n>` persistent worker processes (default: this machine's count, shown by `maxon help spec-test`). `1` is the same pool with one worker, not a serial mode, and output is identical for every count. |
| `--shard=<k>/<n>` | Run only the specs in shard `<k>` of `<n>`, where 1 ≤ `<k>` ≤ `<n>` ≤ 256. A spec file is in shard (H mod `<n>`) + 1, where H is the first 64 bits of the SHA-256 of its name, so every host splits the suite the same way and shards 1 to `<n>` together are the whole run. Any other value is refused as an invalid option value. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it: `wasm32-wasi` under the vendored wasmtime; a Linux target under WSL2 on a Windows host of the same CPU, and on a macOS host inside the OrbStack machine `maxon-linux`. There, a target for the Mac's own CPU is compiled and run by a copy of the compiler that `spec-test` builds for the machine, and on Apple silicon `x64-linux` cases are compiled on the Mac and run under Rosetta, which must be enabled in OrbStack (`orb config set rosetta true`, then restart OrbStack). Each entry into the machine is preceded by a `root` entry (`orb run -u root`) that refreshes the machine's cached view of the host's files, so a binary compiled a moment earlier runs at its current size. The suite a copy of the compiler runs in the machine writes its output to files the host prints, because `orb run` can drop the tail of a large piped output. On a host that cannot execute the target, each case is compiled and reported as not run. |
| `--network` | Also run the cases that open a socket to a real external host. A default run names every case it left out. |
| `--batch=on\|off` | `on` (the default) compiles a spec's plain run cases into the fewest programs whose type names do not overlap and runs each once, rerunning alone any case that did not finish cleanly. `off` compiles and runs every case on its own. Any other value is refused. |
| `--update-required` | Re-mint the inline blocks a run checks, in the spec files themselves: the trace-capture blocks, and the `TargetIr:<lane>` pin of this run's lane for every selected case of a spec that holds any pin. A pin is compiled, not run, so a lane this host cannot execute still re-mints. Review the diff, and pair it with `--filter`: unfiltered, it re-mints every selected case's blocks. |
| `--result-json=<path>` | Also write the run to `<path>` as JSON, after the printed report. See below. |

The run prints one line per test, then `N passed, M failed`, the line `N run case(s) ran batched in M
program(s); K ran alone`, and lines for skipped and not-run cases. A case whose spec carries a
`TargetIr:<lane>` block for the lane being run fails when the Target IR the compiler renders differs from
the block, and the failure shows the first differing line. A spec that holds any `TargetIr` block must give
every case that compiles a block for each native lane the case is not excluded from: a plain run refuses the
spec otherwise, naming the case, the lanes it lacks and the `--update-required --target=<lane>` run that mints
each, while a run with `--update-required` proceeds and mints the lane it is for. A lane the compiler refuses
the case on is named in the case's `unsupported-targets` marker. The Target IR suite lives in its own
directory, `maxon spec-test ir-specs`.

Every worker starts from the [library cache](/docs/cli/#the-library-cache). After the summary, the run prints
`library cache: <n> loaded, <n> written, <n> rejected` on stderr: the entries its workers loaded, wrote
and rejected, summed over the run. The next line, `batch claims: <n> case(s) asked, library names read
<n> time(s)`, counts the cases whose declared and mentioned names were read to batch them, and the times
the workers read the standard library's names for those passes, summed over the run.

A spec's shared batch program that the harness cannot build for a reason of the environment, or cannot
isolate the breaking case of within its budget of trial builds, prints a `harness error:` line on stderr
after the report, naming the cases that ran alone. Their own results decide them, and the run
exits **1** even when every case passed.

**The hash seed and tier.** A run draws one hash seed and hands it to every program it starts as
`MAXON_HASH_SEED`, with a `MAXON_HASH_TIER`, so each run hashes, and orders its maps, differently. The tier
is `aes` where the target has an `aes` tier, its programs run natively on this CPU and the CPU has AES, and
`software` on an emulated lane and on `wasm32-wasi`; a lane where no program runs takes the host's best.
A case's `<!-- env: -->` pin of either variable (`docs/SPECS.md`, "Test Section") wins for its program, and
so does a non-empty value already in `spec-test`'s own environment for the whole run. Every failure report ends
with the pair of the phase that failed, on a line of its own:

```text
    MAXON_HASH_SEED=<16 hex digits> MAXON_HASH_TIER=<tier>
```

A case that fails to compile, or whose `TargetIr` pin differs, names the run's pair, the one the compiler
ran under; a case whose program's check fails names the pair that program ran with. A case whose worker
died names the compiler's pair, then, when its program's pair differs, the same line led by `program: `.
Running with both variables set to the pair named reproduces the failing phase.

Every case's program runs with its working directory in `.spec-run-<pid>/`, a directory of the run's own
directly under the checkout root, and on a native target a case that leaves a process running fails,
naming it (`docs/SPECS.md`, "What a case leaves behind"). An entry a case leaves in that
directory is reported after the summary as `left .spec-run/<name>`, and the run exits **1**.

It refuses to start, with exit **2** and nothing run, when the compiler binary is older than the sources
it was built from, or when another command holds the checkout's [tree lock](/docs/cli/project-structure/#the-tree-lock).

A run in which any `--filter` pattern selects no test on this host exits 1 before any test runs, naming
each such pattern: `error: no tests selected matching --filter=a, --filter=b`. A pattern whose only
matches this host leaves out, such as live-network cases, counts as selecting nothing, and the lines
for the left-out cases say why. An empty `--filter=` is refused as an invalid option value.

Under `--shard`, patterns are judged against the whole directory before it is split. A pattern that
selects tests only in other shards is accepted, and a shard holding none of the selected tests prints
`0 passed, 0 failed` and exits 0. A refused sharded run lists every case the whole directory left out;
an accepted shard lists only its own.

`--result-json=<path>` writes `{passed, failed, skipped, notRun, total, cases, harnessErrors}`, where
`cases` lists every case in run order as `{spec, test, verdict}`, `verdict` is `PASS`, `FAIL`, `SKIP` or
`NOTRUN`, a `FAIL` also carries its `reason`, and `harnessErrors` holds each `harness error:` and
`left .spec-run/<name>` line the run printed. A relative `<path>` is resolved against the working directory. A
`<path>` that is not a valid path exits 1 before any test runs; a document that cannot be written exits
1 after the report.

```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --filter=arrays/ --filter=tuples/   # two specs, one run
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
maxon spec-test ir-specs --target=x64-linux
maxon spec-test --batch=off --filter=arrays/
maxon spec-test --shard=2/4   # the second of four shards
```

## `maxon scale-test`

Measures how the compiler scales: it compiles a ladder of generated programs, each rung double the last,
and reports each phase's **memory** (allocations, frees and bytes, which are exact) and the **CPU ticks**
the compiling thread spent. It does not report wall time, which depends on everything else the machine
is doing. The ratio between rungs is the growth: ×2.00 is linear, ×4.00 quadratic.

The ladder is an instrument, not a gate: it renders no verdict, exits 0 whatever the numbers say, and
exits non-zero only when the run itself broke. [`--check-row`](#checking-the-last-row) is the one mode with
a verdict, and it judges the trend log, not the numbers.

Before the first measured rung it compiles the smallest rung once, unmeasured, to warm the library cache,
so every measured rung reads the cache the same way. A phase's memory counts the compile's own work: what
the tree lock's upkeep allocates while the phase runs is subtracted from it. Every compile it measures runs
with one fixed `MAXON_HASH_SEED`, so the compiler's own hash tables lay out alike in each compile and
repeated compiles of a rung allocate the same.

Each rung is compiled by `maxon build`, run, and then checked by [`maxon lint`](/docs/cli/#maxon-lint), whose
measurements add three rows to each per-phase table and to the result document's `phases`:
`lint:semanticCheck` (the semantic check, with the discarded-result rule), `lint:checkUnusedExports` and
`lint:checkUnusedUnexported`. `--repeat` applies to them as to the build's rows. A rung's totals, and the
rows logged by `--note`, are the build's.

```bash
maxon scale-test [options]
```

| Option | Description |
|--------|-------------|
| `--rungs=<n>` | Climb `<n>` rungs (default 6, range 1–8). Each rung doubles the program. |
| `--repeat=<n>` | Compile every rung `<n>` times (default 1, range 1–25) and report the least CPU per phase. Memory comes from the first compile and must be identical in the rest; a difference is a broken run. Each compile of a rung starts with the executable and `.mxdbg` sidecar the previous one left removed, so every repeat meets the same output directory. |
| `--note=<text>` | Record the run as a dated row in `docs/optimization-log.md` with `<text>` as the reason. Without it, nothing is recorded. |
| `--emit-corpus=<dir>` | Write the generated programs to `<dir>` and compile nothing. |
| `--result-json=<path>` | Also write the whole run, including the change since the last recorded row, to `<path>` as JSON. |
| `--check-row` | Measure and record nothing: check that the last row of `docs/optimization-log.md` carries the digest of the tree in the current directory, exit 0 when it does and 1 when it does not, naming the fix (re-run `--note=<text>` on this tree); refused beside every other `scale-test` option that measures or records. |

A value out of range is refused, never clamped.

### The trend log

`--note=<text>` records the run in `docs/optimization-log.md` as one row in each of its three tables
(allocations, bytes and CPU per rung), and only in the tree the compiler was built from: run from any other
directory, it is refused before anything is measured, and nothing is written. Each row carries:

| Cell | What it holds |
|------|---------------|
| `date` | The UTC date of the run |
| `minted at` | The place: the compiler's absolute path, and the corpus directory it compiled, which sits in the working directory |
| `corpus` | A digest of the generated ladder: each generated file's corpus-relative name and bytes, in generation order. The corpus root's path is not in it. |
| `tree` | A digest of the sources the compiler was built from: the `.maxon` sources a build reads under `maxon-bin/` (output directories and subtrees holding a `.maxonignore` left out), and those under `stdlib/` and `runtime/`, each as its repository-relative path and bytes, in ordinal path order |
| `change` | The `--note` text |
| `rung 0` … | The run's count at each rung |

A digest is the first 8 bytes of a SHA-256, written as 16 lower-case hex characters. A row older than a
column reads `*(unrecorded)*` in it.

Every run reads the last row and reports itself against it, naming its own corpus and tree digests. It
subtracts only when that row's `corpus` and `minted at` cells both equal this run's: then each rung shows
its allocation and byte delta. Otherwise every delta reads `withheld`, and the reason is printed under the
table: the corpus changed, the row was minted elsewhere, or the row does not record its corpus or its
place. The absolute counts and the ratios between rungs are printed either way. The `tree` cell takes no
part in this decision.

### Checking the last row

`maxon scale-test --check-row` compiles and measures nothing. It reads the last row of each table of
`docs/optimization-log.md` in the current directory, digests the tree there, and compares the two.
`scripts/check-scale-rows.sh` runs it for each commit that adds a row, unless given `--history-only`.

| Code | Meaning |
|------|---------|
| `0` | The last row carries this tree's digest; one line names the row's date and change |
| `1` | It does not: the row's `tree` cell is `*(unrecorded)*`, or another tree's digest (both digests are shown), and the fix is to re-run `scale-test --note=<text>` on this tree. Also a refused option, or a log or tree that cannot be read. |

### The result document

`--result-json=<path>` writes the run as one JSON object, which is also what the MCP tool
[`run_scale_test`](/docs/cli/mcp-server/#run_scale_test) answers as `result`:

| Key | Value |
|-----|-------|
| `repeat` | Compiles per rung |
| `mintedAt` | This run's place, as its row's `minted at` cell spells it |
| `corpusDigest` | This run's corpus digest |
| `treeDigest` | The digest of the tree the compiler was built from; absent for a compiler built from no tree |
| `rungs` | One object per rung: `rung`, `sourceBytes`, `codeBytes`, `allocs`, `frees`, `bytes`, `cpuTicks`, `allocsDelta` and `bytesDelta` (this run less the last row), `phases` and `regalloc` (each a list of `name`, `allocs`, `frees`, `bytes` and `cpuTicks`) |
| `corpus` | Whether the generated code grew rung over rung: `grew`, `floorPermille`, and `minGrowthPermille` or a `reason` |
| `previous` | The last row: `present`, and when it is `true`, `date`, `mintedAt`, `corpus`, `tree` (each absent when the row does not record it), `comparable`, `withheld` (the reason, present only when `comparable` is `false`) and `note` |

`allocsDelta` and `bytesDelta` are both absent on every rung when `previous.comparable` is `false`, when
there is no last row, and on a rung the last row does not have. No CPU delta is written.

## `maxon verify-warm-rebuild` and `maxon verify-recheck`

Two checks on the compiler's incremental machinery. Each takes one path and exits 0 only when its
property holds, printing a `PASS` or failure line per property.

```bash
maxon verify-warm-rebuild <file>
maxon verify-recheck <file|dir>
```

- **`verify-warm-rebuild`** checks that the query layer is deterministic (two cold compiles agree byte
  for byte) and incremental (a rebuild reuses cached work, and each kind of edit invalidates exactly what
  it should), and that a compile reusing the memos of files an edit left alone emits exactly what a cold
  compile of the edited program emits. Across that warm compile it checks that each reused parse stays as
  the first merge left it: the same parse object before and after the edit, with every function, range-check
  record, struct layout and run of blocks and ops reading the same after each pipeline and after the second
  merge. That check prints `PASS immutable:`, or one `FAIL immutable:` line per change naming the file, the
  stage that made it and the part it changed. It also compiles, in one session, a second program (the file plus a
  probe that declares its own types and an interface), a sibling program and a third that shares none of the
  file's type names, and checks that the second and the third are served every library file's parse from the
  session's store and emit what a cold compile of each emits. The store keeps one parse of a library file
  for each way it is parsed, keyed on facts of the whole program that the file reads, so a program whose
  typealiases implement a library interface (`implements ElementIndex`) cannot serve one without them. The
  sibling declares what the third declares under other type names and is compiled just before it, so the
  third is served from a program that carries its key. For a program that shadows a library type, or
  declares a name the library uses in a way that [settles its names first](/docs/cli/#the-library-cache), it checks
  instead that the library is parsed cold, and the `PASS` line says `library parsed cold (shadowed)` or
  `library parsed cold (namesOverlap)`.

  A program that does not compile reports its errors and runs the cache-hit and invalidation properties,
  which need no compile. The other three each print a line,
  `NOT RUN determinism: the program does not compile` and the same for `warm` and `library reuse`, and
  the command exits 1. An input that cannot be
  read, such as a path that does not exist, is reported as that error, and the command exits non-zero before
  any property runs. The invalidation property reports `FAIL` when a probe edit changes what an edited file
  reports (the same file, code and message, as many times), since a file that cannot be read declares
  nothing, and its miss counts are then the same whatever its key.

  A property whose probes cannot be compiled prints `FAIL <property>: could not be checked, <reason>` on
  stderr, where `<property>` is `determinism`, `cache`, `invalidation`, `warm` or `library reuse`. The
  remaining properties still run, and the command exits 1. A program that declares
  `LibraryReuseProbeKind`, a name the library-reuse probe declares, prints
  `FAIL library reuse: could not be checked, a program it compiles was refused (the diagnostics are above)`.

  The invalidation and warm-equals-cold properties need at least two source files of the project's own, and
  report `FAIL` on a project of one file, where they would hold for any parse key; give the command a
  directory of two or more files.
- **`verify-recheck`** checks that one project can be re-checked the way an editor does: two checks of
  unchanged input agree, and a diagnostic introduced by an edit clears when the edit is undone. It takes
  `--define=<name>=<value>` as `build` does, so a define is checked on every re-check.

## `maxon stage-library-tier`

Stages a private copy of this compiler's library tiers and its executable under a directory, so a program
can be compiled as library-tier source: one placed under the staged `stdlib/` and built by the staged
compiler may name what only `stdlib/` and `runtime/` sources may (`__Builtins`, `.managed`,
`__ManagedMemory`, `cstring`; [E3185](/docs/cli/error-codes/#e3185--rawsurfaceoutsidelibrarytier) elsewhere) and sees the
unmarked declarations of `stdlib/`. The spec
harness's `<!-- tier: stdlib -->` cases and the debugger's fault fixtures are staged this way.

```bash
maxon stage-library-tier <directory>
```

It writes `<directory>/stdlib`, `<directory>/runtime` and `<directory>/bin/<compiler>`: the sources this
compiler loads, the unicode tables it embeds, and a copy of its executable. Anything already under
`<directory>` is removed first. Exit 0 when the tier is staged, 1 when it could not be.

## `maxon check-stdlib`

Compiles every function of the `stdlib/` beside this compiler as though a program called each one, once
for every target, and reports every diagnostic on stderr. A program compiles only the library functions
it reaches, so this is the one compile that checks a stdlib function no program calls: every body is
built, every check runs on it, the [lint rules](/docs/cli/#maxon-lint) included, and the back end compiles it.

```bash
maxon check-stdlib                         # every supported target
maxon check-stdlib --target=<cpu>-<os>     # one target
```

Each target that compiles prints `stdlib/ compiles for <target>` on stdout. A target with no runtime for
something a library function needs answers [E3104](/docs/cli/error-codes/#e3104--targetunsupportedconstruct)
or [E3074](/docs/cli/error-codes/#e3074--semanticsubprocessunsupportedtarget) for that function once every language
check has passed: the command then lists those functions on stdout, notes that the check of that target
ended with the language checks, and counts it as passing. On `wasm32-wasi` these are the
library's services (`Log`, `TraceCapture`), which need green threads.

| Code | Meaning |
|------|---------|
| `0` | The stdlib compiles for every target checked |
| `1` | It does not compile for a target: its diagnostics are on stderr, followed by `error: stdlib/ does not compile for <target>` |

CI runs it on every lane.

## A typical loop

```bash
./maxon-bin/.maxon/maxon run build                    # rebuild the compiler with itself
./maxon-bin/.maxon/maxon lint maxon-bin               # the lint rules over the compiler's own source
./maxon-bin/.maxon/maxon spec-test --filter=arrays/ --filter=tuples/   # the specs you touched, one run
./maxon-bin/.maxon/maxon spec-test > temp/spec.log 2>&1   # the whole suite, read from the file
./maxon-bin/.maxon/maxon scale-test                   # after a change to a compiler pass
```

To look at what the compiler emits for a program:

```bash
maxon build problem.maxon --emit-ir                          # writes problem.ir
maxon build problem.maxon --emit-ir-function=__managed_get   # plus a compiler-emitted function
maxon build problem.maxon --metrics=temp/phases.tsv          # where the compile time went
```

A change to the runtime the compiler emits into every program takes effect in the compiler's own
behaviour only after it has rebuilt itself twice: the first rebuild emits the new runtime into programs,
the second into the compiler. `maxon run build` does both. Its project file compares the commit the
running compiler reports with the emitted-runtime sources (`maxon-bin/Compiler/Runtime/` and
`maxon-bin/Compiler/Targets/*/*Runtime*.maxon`), committed and uncommitted, and when they changed, or
when git or the compiler cannot answer, it prints the reason to stderr and asks for
[`rebuild_with_output`](/docs/cli/project-structure/#building-again-with-the-written-program). Set `MAXON_SECOND_STAGE=1` for one
build only: the programs it builds carry the new runtime, and the compiler's own process — the
`spec-test` worker among them — carries its builder's.
