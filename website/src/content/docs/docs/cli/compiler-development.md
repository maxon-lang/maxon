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
case is compiled inside this process, so the compiler under test is the executable you ran.

| Option | Description |
|--------|-------------|
| `--filter=<pattern>` | Run only tests whose `<spec>/<test>` label contains `<pattern>`, a case-sensitive substring: a spec name selects that spec, a test name selects that test, and `spec/test` selects exactly one. Repeatable: the run takes every test any pattern selects, in one pool of workers. A comma is part of the pattern. |
| `--workers=<n>` | Run on `<n>` persistent worker processes (default: this machine's count, shown by `maxon help spec-test`). `1` is the same pool with one worker, not a serial mode, and output is identical for every count. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it under the vendored runtime. |
| `--network` | Also run the cases that open a socket to a real external host. A default run names every case it left out. |
| `--batch=on\|off` | `on` (the default) compiles a spec's plain run cases into the fewest programs whose type names do not overlap and runs each once, rerunning alone any case that did not finish cleanly. `off` compiles and runs every case on its own. Any other value is refused. |
| `--update-required` | Re-mint the inline blocks a run checks, in the spec files themselves: the trace-capture blocks, and the `TargetIr:<lane>` pin of this run's lane for every selected case of a spec that holds any pin. A pin is compiled, not run, so a lane this host cannot execute still re-mints. Review the diff, and pair it with `--filter`: unfiltered, it re-mints every selected case's blocks. |

The run prints one line per test, then `N passed, M failed`, the line `N run case(s) ran batched in M
program(s); K ran alone`, and lines for skipped and not-run cases. A case whose spec carries a
`TargetIr:<lane>` block for the lane being run fails when the Target IR the compiler renders differs from
the block, and the failure shows the first differing line. The Target IR suite lives in its own
directory, `maxon spec-test ir-specs`.

Every worker starts from the [library cache](/docs/cli/#the-library-cache). After the summary, the run prints
`library cache: <n> loaded, <n> written, <n> rejected` on stderr: the entries its workers loaded, wrote
and rejected, summed over the run.

It refuses to start, with exit **2** and nothing run, when the compiler binary is older than the sources
it was built from, or when another command holds the checkout's [tree lock](/docs/cli/project-structure/#the-tree-lock).

A run in which any `--filter` pattern selects no test on this host exits 1 before any test runs, naming
each such pattern: `error: no tests selected matching --filter=a, --filter=b`. A pattern whose only
matches this host leaves out, such as live-network cases, counts as selecting nothing, and the lines
for the left-out cases say why. An empty `--filter=` is refused as an invalid option value.

```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --filter=arrays/ --filter=tuples/   # two specs, one run
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
maxon spec-test ir-specs --target=x64-linux
maxon spec-test --batch=off --filter=arrays/
```

## `maxon scale-test`

Measures how the compiler scales: it compiles a ladder of generated programs, each rung double the last,
and reports each phase's **memory** (allocations, frees and bytes, which are exact) and the **CPU ticks**
the compiling thread spent. It does not report wall time, which depends on everything else the machine
is doing. The ratio between rungs is the growth: ×2.00 is linear, ×4.00 quadratic.

It is an instrument, not a gate: it exits 0 whatever the numbers say, and non-zero only when the run
itself broke.

```bash
maxon scale-test [options]
```

| Option | Description |
|--------|-------------|
| `--rungs=<n>` | Climb `<n>` rungs (default 6, range 1–8). Each rung doubles the program. |
| `--repeat=<n>` | Compile every rung `<n>` times (default 1, range 1–25) and report the least CPU per phase. Memory comes from the first compile and must be identical in the rest; a difference is a broken run. |
| `--note=<text>` | Record the run as a dated row in `docs/optimization-log.md` with `<text>` as the reason. Without it, nothing is recorded. |
| `--emit-corpus=<dir>` | Write the generated programs to `<dir>` and compile nothing. |
| `--result-json=<path>` | Also write the whole run, including the change since the last recorded row, to `<path>` as JSON. |

A value out of range is refused, never clamped.

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
  compile of the edited program emits. It also compiles, in one session, a second program (the file plus a
  probe that declares its own types and an interface) and a third that shares none of the file's type
  names, and checks that both are served every library file's parse from the session's store and emit what
  a cold compile of each emits; for a program that shadows a library type, or declares a name the library
  declares, it checks instead that the library is parsed cold, and the `PASS` line says
  `library parsed cold (shadowed)` or `library parsed cold (namesOverlap)`. A file with compile
  errors reports them and exits 1.
- **`verify-recheck`** checks that one project can be re-checked the way an editor does: two checks of
  unchanged input agree, and a diagnostic introduced by an edit clears when the edit is undone. It takes
  `--define=<name>=<value>` as `build` does, so a define is checked on every re-check.

## A typical loop

```bash
./maxon-bin/.maxon/maxon run build                    # rebuild the compiler with itself
./maxon-bin/.maxon/maxon spec-test --filter=arrays/ --filter=tuples/   # the specs you touched, one run
./maxon-bin/.maxon/maxon spec-test > temp/spec.log 2>&1   # the whole suite, read from the file
./maxon-bin/.maxon/maxon scale-test                   # after a change to a compiler pass
```

To look at what the compiler emits for a program:

```bash
maxon build problem.maxon --emit-ir                          # writes problem.ir
maxon build problem.maxon --emit-ir-runtime=__managed_get    # plus a compiler-emitted function
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
