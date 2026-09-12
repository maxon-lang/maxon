---
name: compiler-workflow
description: Procedures for measuring and host-testing the Maxon compiler that are too long to keep always-loaded — the `run_scale_test` doubling ladder and how to read it, `scripts/fixpoint.sh` (does the compiler reproduce itself byte for byte), hosting the x64-linux lane locally under WSL, and staging `vendor/`. Load when running or interpreting the scale ladder, when checking that the compiler is a fixed point of itself, when a defect might be specific to a host rather than a target, or when `vendor/wasmtime` is missing.
---

# Compiler workflow — instruments and host lanes

The always-loaded rules live in `maxon-bin/CLAUDE.md`. This skill holds the four procedures that are
reference material rather than standing constraints.

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

## Hosting the x64-linux lane locally

`--target=` cross-compiles the PROGRAMS and never hosts the compiler — that rule, and the defect that
proved it, are in `maxon-bin/CLAUDE.md`. To actually host the lane:

⇒ **CROSS-BUILD ONCE AND THEN STAY INSIDE WSL.** `stdlib/` resolves by walking UP from the executable,
so a binary under `temp/` in this tree reaches this tree's library. WSL starts in the Windows working
directory, so nothing has to `cd`:

```
./maxon-bin/.maxon/maxon.exe build maxon-bin --target=x64-linux -o temp/linux-lane/maxon
wsl -- chmod +x temp/linux-lane/maxon
wsl -- ./temp/linux-lane/maxon build maxon-bin -o temp/linux-lane/maxon2   # C2, hosted on Linux
wsl -- ./temp/linux-lane/maxon2 spec-test
```

`C1` is emitted by the Windows compiler and `C2` by `C1`, so `C2` is the first binary whose own
runtime came from this tree — the same two-stage rule `Compiler/Runtime/` follows everywhere else.

## Staging `vendor/`

⛔ **`vendor/` IS GITIGNORED AND A CLONE HAS NONE OF IT.** `scripts/fetch-vendor.sh` downloads THIS
host's build of `wasmtime` and `wasm-tools`, verifies each against a pinned SHA256 or refuses, and
stamps what it placed so a re-run is a no-op. A machine therefore holds one platform's binaries under
their natural names — `wasmtime` on unix, `wasmtime.exe` on Windows — and nothing has to remember which
extensionless file is somebody else's Mach-O. Pass a target (`fetch-vendor.sh arm64-macos`) to stage
another platform's, and `wasm-opt` by name: nothing in the tree invokes it, so it is not fetched by
default.
