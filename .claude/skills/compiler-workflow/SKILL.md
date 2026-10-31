---
name: compiler-workflow
description: Procedures for measuring and host-testing the Maxon compiler that are too long to keep always-loaded — the `run_scale_test` doubling ladder and how to read it, `scripts/fixpoint.sh` (does the compiler reproduce itself byte for byte), hosting the x64-linux lane locally under WSL, hosting the arm64-macos lane on the shared Mac through `scripts/mac-host.sh`, resolving a CI fault's rip on a local build, freezing a new bootstrap seed with `scripts/freeze-seed.sh`, and staging `vendor/`. Load when running or interpreting the scale ladder, when checking that the compiler is a fixed point of itself, when a defect might be specific to a host rather than a target, when anything must run on the Mac, when the pinned seed no longer accepts the tree, or when `vendor/wasmtime` is missing.
---

# Compiler workflow — instruments and host lanes

The always-loaded rules live in `maxon-bin/AGENTS.md`. This skill holds the procedures that are
reference material rather than standing constraints.

## `run_scale_test` — the scaling INSTRUMENT. ⚠ NOT A GATE.

It compiles a ladder of generated programs — six rungs, each double the last — and measures **MEMORY
and CPU TIME per phase per rung**. ~17 s. Run it after any change to a pass, the IR, or a data structure
the compiler indexes by, and READ it.

- **It has no verdict and there is nothing to pass.** It exits **0** whatever the numbers say; a
  non-zero exit means the **RUN ITSELF BROKE** (a degenerate corpus, a rung that failed to compile, an
  IO failure) and produced no valid data.
- **Give it no gate apparatus** — no committed memory goldens, no exponent budgets, no PASS/FAIL
  verdict.
- ⚠ **DO NOT CHASE A GREEN SCALE-TEST. There isn't one**, and **never touch the instrument to make a
  number look better.** A curve that looks wrong is a **reading to explain**.
- **The ladder DOUBLES, so the RATIO between rungs IS the growth** — ×2 linear, ×4 quadratic. Read it
  off the **ALLOCATION** columns, which are exact and bit-for-bit reproducible; the CPU column carries a
  few-percent noise band and a platform-defined unit, and there is no wall time at all.
- **The artifact is the trend: `docs/optimization-log.md`.** A run writes a row there only when given a
  reason — `note:` (`--note=` on the CLI) — and that reason is the WHY, recorded at the one moment it
  is still known: the instrument sees exactly WHAT moved and can never see WHY. **Write no row you did
  not measure.**
- Through MCP the ladder comes back as `result`, the `--result-json` document (per rung and phase:
  `allocs`, `frees`, `bytes`, `cpuTicks`), with the printed tables in `rawTail`.

⇒ **The full reading guide — the memory and CPU columns, the two blind spots, A/B methodology — is
the `optimize` skill.** Load it before acting on a ladder.

⚠ The compiler's own per-phase timing is a **different thing**: `--metrics=<path>` writes a TSV whose
7th field is `cputicks`, and `--log=compiler:debug` prints a timing table with a `cpu%` beside the wall
`%`. **A phase where the two disagree spent its wall time NOT RUNNING** — `load`, which waits on IO,
takes a far larger share of wall time than of CPU.

## ⭐ `scripts/fixpoint.sh` — does the compiler reproduce itself exactly?

A green suite cannot answer this: every stage shares the compiler's LOGIC, so a suite exercises the
same behaviour whichever stage ran it. Only the BYTES of two successive self-compiles say whether the
emitted code is stable, and **a difference is a MISCOMPILE** — the compiler is not a fixed point of
itself, so which binary you hold decides what your programs become. Writes only under
`temp/fixpoint/`.

⛔ **THE TWO OUTPUTS SHARE A BASENAME AND DIFFER ONLY IN DIRECTORY.** On macOS the ad-hoc
code-signature identifier is taken from the output FILENAME, so `--output=stage2` and `--output=stage3` differ in
exactly one byte for that reason alone — a difference that reads as a miscompile and is not one.

## Hosting the x64-linux lane locally

`--target=` cross-compiles the PROGRAMS and never hosts the compiler — that rule, and the defect that
proved it, are in `maxon-bin/AGENTS.md`. To actually host the lane:

⇒ **CROSS-BUILD ONCE AND THEN STAY INSIDE WSL.** `stdlib/` resolves by walking UP from the executable,
so a binary under `temp/` in this tree reaches this tree's library. WSL starts in the Windows working
directory, so nothing has to `cd`:

```
./maxon-bin/.maxon/maxon.exe build maxon-bin --target=x64-linux --output=temp/linux-lane/maxon
wsl -- chmod +x temp/linux-lane/maxon
wsl -- ./temp/linux-lane/maxon build maxon-bin --output=temp/linux-lane/maxon2   # C2, hosted on Linux
wsl -- ./temp/linux-lane/maxon2 spec-test
```

`C1` is built by the Windows compiler and `C2` by `C1` running on Linux, so `C2` is the first binary a
Linux-HOSTED compiler produced — the defect class the cross lane cannot reach. When the emitted
runtime changed since the Windows slot was built, it is also the first whose own
emitted runtime is this tree's.

## Hosting the arm64-macos lane on the shared Mac

⛔ **EVERY STEP GOES THROUGH `scripts/mac-host.sh`, and nothing is built or run in the Mac's shared
`~/Dev/maxon`** — it may hold a person's uncommitted work. Each agent gets its own clone under
`~/Dev/agents/<session>/`, and the host lock keeps two suites from sharing the machine, which flakes
the timing-sensitive specs. `scripts/mac-host.sh` with no arguments prints the full usage.

```
scripts/mac-host.sh status
scripts/mac-host.sh clone <session> <commit> --seed             # prints session-key=<KEY>
scripts/mac-host.sh lock <session> --wait=1800 <purpose>        # prints token=<TOKEN>
scripts/mac-host.sh run <session> <TOKEN> bash -c '<line>'      # prints job=, pid=, log=
scripts/mac-host.sh job <session>                               # alive / finished (exit N) + log tail
scripts/mac-host.sh fetch <session> <path> <destination>        # copy a file out of the clone
scripts/mac-host.sh unlock <session> <TOKEN>                    # only while no job runs
scripts/mac-host.sh drop <session> <KEY>
```

- `<commit>` must be pushed: the clone is made from `origin`. `--seed` places the pinned seed's
  arm64-macos compiler at `.bootstrap/maxon`. Clone before locking — a clone needs no lock and does
  not refresh one.
- **A lock runs one job, and the job's exit releases it.** Watch the job with a Monitor that polls
  `job <session>`; while the lock is held with no job running, the same poll calls `heartbeat <session>
  <TOKEN>`, or the lock goes stale and any agent may break it.
- `fetch` needs no lock and no key; run it before `drop`, which deletes the clone.
- `drop` the session when done; it releases a lock the session still holds.
- ⚠ **`mac-host.sh: skipped — MAXON_MAC_HOST is not set` means no Mac is configured.** It exits 0 and
  touches nothing. Skip the arm64-macos work and say so in the report — it is not a failure.
- **Before a lane run, cross-compile that lane from the local host** with
  `spec-test --target=arm64-macos --filter=<spec>/`: it compiles every case the lane selects without
  running it, in seconds.
- **To tell a hang from a crash on macOS (no `timeout`),** background the program and poll `kill -0`;
  `sample <pid> 2` prints the parked stack.
- **Poll the remote for liveness** (`scripts/mac-host.sh job`, the log's modification time), never the
  local driver's log: a dead remote and a slow one look identical from here.

## Resolving a CI fault

A fault panic from a CI lane (`… at rip=0x… diag_base=0x…`) resolves to an exact instruction on a local
build of the same commit from the same seed (`scripts/fetch-seed.sh <target>`, then
`scripts/build-from-seed.sh`, as CI does).

1. `diag_base` is the runtime address of the text base, `mrt_start`: slide = CI's `diag_base` minus that
   symbol's unslid address (`nm`); unslid rip = `rip` minus slide.
2. Confirm the unslid rip lands inside the frame the backtrace named — that proves the binaries match.
3. Disassemble that function (`otool -tvV` on macOS, `llvm-objdump -d` elsewhere).

⚠ **The panic words are the same for a nil dereference and a use-after-free; the faulting load tells them
apart.** Freed payloads are poisoned with `0x3F` and stay mapped, so a read through a freed buffer returns
poison without faulting — only a pointer read OUT of poison faults, on `0x3F3F3F3F3F3F3F3F`. A true nil
faults on the first load.

## Freezing a bootstrap seed

The seed that builds every commit is the `seed-*` pre-release `scripts/seed-tag` pins (`docs/RELEASING.md`
owns the rule). ⭐ **CI freezes, proves and pins it** on the first green `main` push whose tree carries a
`scripts/seed-shim/*.patch`, so the usual cure for E2015 or E3004 from `build-from-seed.sh` is a shim patch
landed with the change. Freeze by hand only when the pinned seed refuses the tree and no patch covers it.

```
./maxon-bin/.maxon/maxon run build        # the slot must be built from HEAD; the script refuses a stale one
scripts/freeze-seed.sh                    # four builds, serial, below-normal priority; ~1 minute each
```

It writes `temp/seed/dist/` (four archives, `SHA256SUMS`, `NOTES.md`) and `temp/seed/publish.txt`, and prints
the `gh release create` command **without running it**. Publishing, tagging and moving the pin are the
user's. Before the pin moves, prove the seed accepts the tree with NO shim:

- **Windows:** a fresh clone of `HEAD` at a short path (`git clone --no-hardlinks . /c/s1`), every
  `scripts/seed-shim/*.patch` deleted, then in the clone
  `bash scripts/fetch-seed.sh <tag> x64-windows --from=<the dist directory>` and
  `bash scripts/build-from-seed.sh --unshimmed`: exit 0. `--from` reads the unpublished archives.
- **x64-linux:** the same clone, run under WSL as in the section above
  (`wsl -- bash scripts/fetch-seed.sh <tag> x64-linux --from=…`, then `wsl -- bash scripts/build-from-seed.sh --unshimmed`).
- The arm64 archives run only on arm64, so from an x64 host their proof is CI's seed step on the commit that
  moves the pin.

## Staging `vendor/`

⛔ **`vendor/` IS GITIGNORED EXCEPT `vendor/wasi-wit/`**, the committed WIT source the compiler reads to
build a Preview2 component — so a clone has none of the tool BINARIES. (`vendor/go/`, a local copy of
Go's source, is optional and fetched by nothing: `scripts/gen-slab-classes.sh --check` reads it and
skips when it is absent.) `scripts/fetch-vendor.sh` downloads THIS
host's build of `wasmtime` and `wasm-tools`, verifies each against a pinned SHA256 or refuses, and
stamps what it placed so a re-run is a no-op. A machine therefore holds one platform's binaries under
their natural names — `wasmtime` on unix, `wasmtime.exe` on Windows — and nothing has to remember which
extensionless file is somebody else's Mach-O. Pass a target (`fetch-vendor.sh arm64-macos`) to stage
another platform's, and `wasm-opt` by name: nothing in the tree invokes it, so it is not fetched by
default.

**For a runtime design question** (signals, alt stacks, thread suspension, preemption, scheduler,
netpoller, allocator), follow Go's runtime (`src/runtime/` in Go's source; `vendor/go/` when staged)
rather than re-deriving it — the tree's runtime is built on its mechanisms, and a brief for such work
names the Go function followed. `vendor/delve/` (Delve's source, `pkg/proc/gdbserial/`, also fetched by
nothing) is the reference for driving Apple's `debugserver` over the GDB remote protocol; read it before
changing `maxon-bin/Debug/Backend/GdbRemoteBackend.maxon`.
