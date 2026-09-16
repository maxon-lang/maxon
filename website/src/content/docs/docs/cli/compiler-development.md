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
| `--filter=<pattern>` | Run only tests whose `<spec>/<test>` label contains `<pattern>`. One case-sensitive substring, not a list: a spec name selects that spec, a test name selects that test, and `spec/test` selects exactly one. |
| `--workers=<n>` | Run on `<n>` persistent worker processes (default: this machine's count, shown by `maxon help spec-test`). `1` is the same pool with one worker, not a serial mode, and output is identical for every count. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it under the vendored runtime. |
| `--network` | Also run the cases that open a socket to a real external host. A default run names every case it left out. |
| `--update-required` | Rewrite the committed IR goldens instead of checking against them. Review the diff, and pair it with `--filter`: unfiltered, it rewrites every golden. |

The run prints one line per test, then `N passed, M failed` and lines for skipped, not-run and drifted
cases. A golden whose text differs from this run's output is reported as drift and does not fail the
case; only `--update-required` rewrites one.

It refuses to start, with exit **2** and nothing run, when the compiler binary is older than the sources
it was built from, or when another command holds the checkout's [tree lock](/docs/cli/project-structure/#the-tree-lock).

```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
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
  it should). A file with compile errors reports them and exits 1.
- **`verify-recheck`** checks that one project can be re-checked the way an editor does: two checks of
  unchanged input agree, and a diagnostic introduced by an edit clears when the edit is undone.

## A typical loop

```bash
./maxon-bin/.maxon/maxon build maxon-bin              # rebuild the compiler with itself
./maxon-bin/.maxon/maxon spec-test --filter=arrays    # the specs you touched
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
the second into the compiler.
