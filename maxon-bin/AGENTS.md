## The compiler

One compiler builds this tree and it is written in Maxon: source `maxon-bin/`, binary
`maxon-bin/.maxon/maxon` (`maxon.exe` on Windows; commands below show the Windows form), suite `specs/`.
Every compile reads two source tiers at the checkout root: `stdlib/`, the standard library, and `runtime/`,
the language runtime. The compiler finds `stdlib/` by walking UP from its own executable and reaches
`runtime/` as its sibling, so a tree holding one without the other compiles nothing. `scriptlib/` beside
them holds the USER's own packages (gitignored, nothing shipped), read only by a program whose root is one
file; harnesses and gates pass `ScriptlibLoad.omitted`, so a developer's `scriptlib/` never reaches them.

- **Build it:** `./maxon-bin/.maxon/maxon run build` at the repo root. The root `maxon.maxtasks`'s `build`
  task delegates to `maxon-bin/maxon.maxproj`, whose `build` target stamps the git-derived version;
  `cd maxon-bin && maxon build` is the same build. ⛔ **`maxon build maxon-bin` from the root is a PATH
  build**: the root holds no `.maxproj`, so it compiles the same sources and stamps NO version. Keep it away
  from the slot.
- **Lint it:** `./maxon-bin/.maxon/maxon lint maxon-bin`. A build compiles past the lint rules (E3064 and
  E3065, a dropped result; E3092, an unused `export`; E3219, an unused unexported declaration), so only
  `lint` reports them. `--target=<lane>` lints another lane, which matters where `#if` branches differ.
- **Run the suite:** `./maxon-bin/.maxon/maxon.exe spec-test`. Exit code **101** means a memory leak.

## The seed

**Maxon compiles Maxon: a previous build of this compiler is the only thing that can build it.**
`scripts/fetch-seed.sh` places the pinned seed at `.bootstrap/maxon.exe`; run it directly when the slot
is empty. The seed is the `seed-*` pre-release `scripts/seed-tag` names, a frozen compiler that changes
only when a commit moves the pin, so re-run the script after pulling one that does.

⛔ **NAME THE OUTPUT WHEN YOU BUILD WITH THE SEED, ALWAYS:**
`./.bootstrap/maxon.exe build maxon-bin --output=maxon-bin/.maxon/maxon`. A seed older than named
manifest targets reads `maxon-bin` as a path: a path build names its own output, leaves the slot empty
and exits 0, so the next command fails with a bare `127`. `scripts/build-from-seed.sh` passes `--output=`
for this reason, and `CONTRIBUTING.md` spells it too.

- A seed build failing on **E6002** (a slot it could not replace: read-only, or held open in a way that
  forbids a rename) is a lock, not a refusal; the message names which. A slot binary that is only running,
  such as an editor's language server, is renamed aside and replaced.
- **A slot binary's version stamp names the last commit, never the working tree**, so a compiler built
  over uncommitted or stashed edits carries a clean-looking stamp that does not vouch for its source. When
  a build fails on a name or constant the tree plainly declares, rebuild with `scripts/build-from-seed.sh`
  before believing the diagnostic.

⛔ **THE SEED REFUSES A TREE THAT NEEDS WHAT IT DOES NOT KNOW, AND NO RE-FETCH CURES IT:** E2015 on a
`__Managed*` entry the tree adds, E3004 on a `__Builtins` intrinsic the stdlib calls, a language form newer
than the seed, or a tier the seed cannot credit (under a seed whose `build` runs the lint rules, an `export`
that only a rule in this tree makes used is E3092, on every lane). A seed-built first stage applies the
seed's rules, never this tree's. The cure is a new seed frozen from the refused commit, which lands with it
through `land.yml` instead of `git push`, from a bootstrap chain of commits the pinned seed can build
through; `docs/RELEASING.md` owns the procedure and the `compiler-workflow` skill runs it. Land it without
asking: publishing the seed and pushing its pin are standing-authorized (user ruling). When an editor's lock
stops `build-from-seed.sh`, prove the chain by hand: `.bootstrap/maxon.exe build maxon-bin
--output=temp/<task>/c1.exe`, then `temp/<task>/c1.exe build maxon-bin --output=temp/<task>/c2.exe`.

⛔ **A COMPILER THAT STARTS DEPENDING ON A RUNTIME BEHAVIOUR THE SAME CHANGE ADDS NEEDS THE SEED CHAIN.**
When the compiler's own code relies on a transfer, scheduler, allocator or walk semantic the change
introduces, every local self-compile stays green, because the slot compiler is always a post-change
builder; CI builds from the pinned seed, whose runtime lacks it, so seed → C1 → C2 can die (exit 96). Run
`scripts/fetch-seed.sh` then `scripts/build-from-seed.sh` before pushing. Stage the dependency behind a
builder-set `static let` capability that defaults to the old-safe path, as `FrontEndPool.RuntimeSharesAnyGraph`
does (a seed-built `C1` runs its front end in process), and remove it once a release carries the runtime.

## One self-compile, two for emitted runtime

> ⛔ **A CHANGE TO EMITTED RUNTIME — `Compiler/Runtime/` OR `Compiler/Targets/*/*Runtime*.maxon` — NEEDS
> *TWO* SELF-COMPILES BEFORE THE COMPILER ITSELF BEHAVES THAT WAY.** The compiler WRITES that runtime into
> every program it builds, itself included. With `C0` the old compiler and `S` the fixed sources, `C0`
> builds `S` → `C1`: programs `C1` builds get the new runtime, but `C1`'s OWN runtime was emitted by `C0`.
> `C1` builds `S` → `C2`, whose own runtime is new too. It bites hardest where the compiler is the program
> under test: `spec-test`'s worker IS the compiler, so a runtime fix to subprocess, the scheduler or memory
> management does not change what the harness does until the second build. ⚠ `fixpoint.sh` does not catch
> this: its two stages under `temp/fixpoint/` are both past convergence and agree while the slot holds `C1`.
>
> **`maxon run build` does the second build itself.** `maxon-bin/maxon.maxproj` asks on every `run build`
> whether any emitted-runtime file changed since the running compiler's stamped commit (a `git diff`, which
> counts uncommitted changes). On yes, or unknown, it prints the reason to stderr and describes the build
> with `rebuildWithOutput: true`; the driver runs the compiler it just wrote with the same command line and
> `MAXON_SECOND_STAGE=1`, which ends the repetition. So while a runtime edit is uncommitted, every
> `run build` compiles twice. ⇒ **To iterate on runtime with ONE build, set `MAXON_SECOND_STAGE=1`** for
> that `run build`: only the compiler's own process (the spec harness, the self-compile timing) needs `C2`.
> The MCP `build` tool with path `maxon-bin` is a path build: it runs no manifest, so it builds once.
>
> ⛔ **A `MAXON_SECOND_STAGE=1` BUILD IS NOT A SELF-COMPILE.** The previous compiler does the compiling, so
> a rule the change adds is never applied to the compiler's own source; only a build run BY the new compiler
> answers "does it compile itself".
>
> ⛔ **EVERY OTHER CHANGE NEEDS ONE BUILD**, and a needless self-compile is a minute off every task. A
> `runtime/` file is input the compiler READS, not compiler source: it needs one. Before changing
> `runtime/` or the compiler's handling of it, read `runtime/README.md`; the `maxon-coder` skill carries the
> rules that bind every tier edit.

## The compiler that builds this tree must live inside it

> ⛔ **RUN `.bootstrap/maxon` OR THE SLOT BINARY, NEVER A `maxon` ON PATH.** `stdlib/` and `runtime/` are
> found by walking up from the EXECUTABLE, so an installed `maxon` compiles this repository against the
> RELEASE's sources — measured: it succeeds and exits 0, having built a compiler from a library that is not
> this tree's.
>
> ⛔ **`.bootstrap/` HOLDS THE BINARY AND NOTHING ELSE.** A release archive ships its own `stdlib/` and
> `runtime/`; unpacked whole, it leaves a released library one directory above the compiler, the tree's own
> is never reached, and the build silently compiles the wrong sources.
>
> ⛔ **`maxon-bin/.maxon/` IS A CLOSED DIRECTORY.** Only the names `scripts/allowed-ignored-paths.txt` lists
> under `closed-directory: maxon-bin/.maxon` may be there; `/land`'s "No leftovers" gate and CI's "No file
> left behind" step (both `scripts/check-ignored-files.py`) fail any other. A compiler image you want to
> keep goes in `temp/<task>/` (`--output=temp/<task>/maxon.exe`); never rename the slot's own image to keep
> it. How a rebuild swaps the slot is in the `maxon-tools` skill.

## A compiler built by a wrong compiler stays wrong

⛔ **REVERTING THE SOURCE DOES NOT CURE IT.** A probe or defect in a pass or in emitted runtime acted on the
compiler's own code, so the binary it builds carries the damage: filtered specs stay green, because they
exercise the compiler's logic and not its own codegen, while a real program panics; a runtime probe fires
in every later build. Keep probe builds at `--output=temp/<task>/…`. When one reached the slot, rebuild with
`scripts/build-from-seed.sh`, then compile a real program (`examples/fannkuch-redux.maxon`) before trusting
a filtered green.

## Targets

- `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` — the native lanes; CI hosts each on its own
  architecture, and `--target=` cross-compiles programs without hosting the compiler there.
- `wasm32-wasi` — a WASI Preview2 component, run under the vendored wasmtime.

`maxon-bin/targets.tsv` is the one place a target is listed.

## Where the rest lives

- **The `maxon-tools` skill** — the `maxon` MCP tools (`repoRoot` in a worktree, the tool table, what
  they refuse), `spec-test` filters and flags, running a suite by hand and reading a red or empty result,
  the slot's rename, `maxon fmt`, cross and wasm lanes by hand. Load it before running any of them.
- **The `maxon-coder` skill** — writing Maxon; its `compiler.md` holds the rules for compiler source
  (tier edits, memory safety, ranged aliases, error codes, `targets.tsv`, cross-target register pressure,
  the query spine) and its `specs.md` the traps in writing and re-pinning spec cases.
- **The `compiler-workflow` skill** — `run_scale_test`, `fixpoint.sh` (a difference is a miscompile a
  green suite cannot see), hosting a lane locally, the Mac, `vendor/`, landing with a new seed.
- **`docs/SPECS.md`** — the spec format: markers, batching, tiers, `ir-specs/` and its `TargetIr` pins.
- **`tests/README.md`** — the driver-command corpora under `tests/`. Read it before touching anything there.
