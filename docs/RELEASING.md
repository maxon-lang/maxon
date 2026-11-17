# Releasing Maxon

Cutting a release means producing four tested compilers and the package manifests that point at
them — then publishing the lot under one version.

Almost all of it is automated. This document exists for the parts that are not.

---

## ⛔ Maxon compiles Maxon, so every build needs a seed — and the seed is pinned

There is no second implementation. Every machine that builds the compiler needs a compiler first, and
the one it takes is **the seed: the `seed-*` pre-release named by [`scripts/seed-tag`](../scripts/seed-tag)**,
for its own target. `scripts/fetch-seed.sh` downloads it, checks the archive against the release's
`SHA256SUMS`, and places the binary alone at `.bootstrap/maxon`; CI, `release.yml` and the VS Code
extension workflow all start there, and `scripts/build-from-seed.sh` builds the tree with it.

⭐ **The seed is frozen, not rolling.** A release does not change it: the seed changes when a commit moves
`scripts/seed-tag`, so a commit and the compiler that builds it travel together, and a published release can
be deleted or superseded without breaking the build of any commit. A seed pre-release is never the
`releases/latest`, which the installers and `maxon upgrade` follow.

⛔ **A seed tag is never moved and its assets are never replaced.** The pin means exactly the bytes that were
frozen; a different compiler is a different `seed-*` tag. `fetch-seed.sh` refuses an archive that does not
match the release's `SHA256SUMS`.

### ⛔ A commit the pinned seed refuses lands with its own seed

`stdlib/` and the compiler's own sources are compiled by the seed on every CI lane and every release runner,
none of which has another compiler to reach for. A tree that uses something the pinned seed does not accept —
a `__Managed*` entry (E2015), a `__Builtins` intrinsic (E3004), a language form, a rule that widens a tier
(E3092, under a seed whose `build` runs the [lint rules](CLI_REFERENCE.md#maxon-lint)) — fails the seed's
first build, and nothing later on `main` can fix that: the compiler that accepts
the tree is the one the seed does not contain yet. So the commit and a seed frozen from it are published and
pinned together, by [`land.yml`](../.github/workflows/land.yml), before `main` moves. `main` never holds a
commit its pinned seed refuses.

**The seed chain is how a refusal is found:** `scripts/build-from-seed.sh` in a clean clone with the pinned
seed. Any failure there is a refusal, except E6002, which is a slot the build could not replace: its
message names what stood in the way.

#### The bootstrap chain

The pinned seed refuses the change, so the compiler that accepts it is built from an intermediate tree the
seed does accept — the compiler that knows a new intrinsic, say, before any source calls it. That tree is a
commit. **A seed-pinning branch is a bootstrap chain: commits on `origin/main` whose first commit the pinned
seed builds, and each later commit is built by the compiler of the one before it** (two stages each, as
`build-from-seed.sh` builds). The last commit is the change. A change that needs several intermediate trees
has several commits.

Only the change reaches `main`, as one commit: the last commit's tree, author, author date and message, with
a `Bootstrap-Chain: <sha>` trailer per chain commit in order. It is `S` on the chain's base, the merge-base of
`origin/main` and the head, and `S'` once merged onto `main`'s tip. The intermediate commits never reach
`main`, which stays linear. The ref `refs/tags/<seed-tag>-chain`, on the chain's last commit, keeps the chain
reachable; it is published with the seed, never moved, and named in the seed's `NOTES.md` with the chain's
commits.

**To rebuild a seed's compiler from its chain:** fetch the ref
(`git fetch origin tag <seed-tag>-chain`), then

```bash
scripts/land.sh chain <S>^ <seed-tag>-chain
```

walks the chain from the seed `<S>^` pins, exactly as the workflow did, and prints the path of the compiler
built from the last commit. It works in `temp/land-branch/`, its `tree/` a git worktree: remove it when done with `git worktree remove --force temp/land-branch/tree && rm -rf temp/land-branch`.

#### Landing a branch — `land.yml`

`land.yml` is the one door to `main`: every commit reaches it through a run, which merges the commit at
`main`'s tip, proves the pinned seed builds the merged tree, mints the scale row and pushes. The maintainer's
own push to `main` stays possible, as the escape when Actions is down.

A run starts on a push to any branch but `main` whose head commit carries the git trailer `Land: main` or
`Land: rehearsal`, or on a dispatch of `land.yml` on a branch. Both take write access to the repository.
The dispatch inputs are `mode` (`main` or `rehearsal`), `head` (the full commit id the dispatch lands; the
plan fails when the branch has moved from it), `attempt` (which dispatch of this land it is, 1 to 3) and, for
a chain's re-dispatch, `seed` (the seed tag its first attempt published); `scripts/land.sh submit` and the
land job fill them in. The rehearsal-only inputs `tip` and `moved` stand in for `main`'s tip when the plan and
the pin job merge and when the land job pushes, so the paths that need `main` to move can be rehearsed
without moving it; `main` mode refuses both. Another `Land:` value, or two `Land:` trailers, is an error.

A commit carries its note as a `Scale-Note: <text>` trailer when its author read the scale ladder (below).
`Land:` and `Scale-Note:` are read by one rule, git's trailer parse of the commit's final paragraph. A line
with either key anywhere else fails the plan: `move it into the final paragraph`. The steps are
[`scripts/land.sh`](../scripts/land.sh) subcommands, one per job.

1. **plan** (`contents: read`, no secret) classifies the branch by the pinned seed's verdict on its head
   tree and its commits since its base, the merge-base of `origin/main` and the head, and does everything
   that builds:

   | Commits | Pinned seed | Result |
   |---|---|---|
   | 0 | — | error: nothing to land |
   | 1 | builds it | **ordinary** |
   | more than 1 | builds the head | error: an ordinary land is one commit |
   | 1 | refuses it | error: the seed's diagnostic, and the fix — commit the intermediate tree before it |
   | more than 1 | refuses the head | **chain** |

   A branch that changes `scripts/seed-tag`, holds a merge commit, or changes a row of the scale-history
   tables in `docs/optimization-log.md` by hand is refused; the last names the `Scale-Note:` trailer. An
   **ordinary** land continues:

   - **Merge at the tip.** The overlap is the files the commit changed since the base that `main` also
     changed since the base, less the files `.gitattributes` declares `merge=union`. A non-empty overlap
     bounces the land to its author: `rebase by hand and submit again`. An empty one is merged by
     `git merge-tree --write-tree` onto `main`'s tip as one commit with the author's name, email and date, the
     message less its `Land:` line, and `github-actions[bot]` as committer. `main` always receives this
     commit, so every land has one shape.
   - **Prove the seed.** When the merge changed a seed input — `maxon-bin/`, `stdlib/`, `runtime/`,
     `vendor/`, `project.maxon`, `maxon.maxtasks`, `scripts/seed-tag`, `scripts/build-from-seed.sh`,
     `scripts/fetch-seed.sh`, `scripts/fetch-vendor.sh`, `scripts/lib/` — relative to the head the plan already
     built, the seed pinned at `main`'s tip builds the merged tree, and a refusal bounces the land. A merge
     that changed none skips the build. Everything else, including a semantic conflict between disjoint files, is
     `ci.yml`'s on `main`.
   - **Mint the row.** With a `Scale-Note:`, the plan runs `maxon scale-test --note=<note>` in the merged tree
     with the compiler it built there, and the row joins the landed commit. A refused note bounces.
   - **Hand off.** The landed commit travels to the **land** job as a git bundle artifact, with the tip it
     merged onto.

   A chain has the same base, so it needs no rebase when `main` has moved. The plan records the tag
   `seed-<date>-<first 12 hex digits of S>` and refuses one that exists as a tag or a release, and merges
   `S` onto `main`'s tip as **pin** will, so a chain that overlaps `main` bounces here, before the freeze. A
   `Scale-Note:` on a chain is refused. A run given the dispatch input `seed` skips the pinned seed's build and
   the tag check: it reads `S` from that tag, checks that `S` is what this head on this base lands as at
   the tag's commit time, that the chain ref is on the head and that the release is published, and
   **chain**, **prove** and **publish** do not run.
2. **credential** (`main` only) mints the landing App's token and drops it, so a missing or
   under-permissioned App fails before anything is published.
3. **chain** walks the chain, checks out `S` as pushed and runs `scripts/freeze-seed.sh`, which builds `maxon-bin`
   for every native target in `maxon-bin/targets.tsv` with the chain's last compiler and packages each as
   `maxon-<tag>-<target>` (in the table's `archive` format, the binary alone) with `SHA256SUMS` and
   `NOTES.md`.
4. **prove** runs on every native lane's own runner: `scripts/fetch-seed.sh <tag> <lane> --from=<dist>`, then
   one self-compile of `maxon-bin` with the frozen seed. A seed has to build the tree and nothing more.
5. **publish** pushes the tag on `S` and the chain ref, creates the pre-release through GitHub's REST API,
   and fetches every target back against its `SHA256SUMS`.
6. **pin** (`contents: read`, no secret) reads `main`'s tip as the plan did and merges `S` onto it as `S'`:
   the same author, date and message, the same `Bootstrap-Chain:` trailers, and a `Frozen-From: <S>` trailer
   when `S'` differs from `S`. When the merge changed a seed input, it fetches the published seed and builds
   the merged tree's `maxon-bin` with it once for every native target, with `--target`, on this one runner.
   A refusal fails the job, **retract** deletes the seed and the land bounces: `rebase the chain onto
   origin/main and submit again`. The job then writes the pin commit on `S'` — the tag in `scripts/seed-tag`,
   and a message that names `S`. The **land** job, which holds the push credential, runs no compiler, so the
   merge and the build of the merged tree happen here.
7. **land** pushes `main`, and in `main` mode holds the App token. It runs git alone, with no build and no
   `maxon`, for both kinds. An ordinary land checks that the plan's bundle is the head merged onto the plan's
   tip plus the one minted row. A chain recomputes `S`, `S'` and the pin commit from the plan's and **pin**'s
   outputs and checks that the seed is still published on `S`. Both push the landed commit (for a chain, the
   pin commit) with `--force-with-lease` on the tip it was merged onto, a compare-and-swap. When `main` has
   moved:
   - by commits that change no seed input, it merges again onto the new tip with git alone — an ordinary
     land merges the landed commit, a chain merges `S` as a new `S'` and writes a new pin — the overlap check
     runs again, and a row a concurrent land added to the union log merges, this land's last. It pushes
     again, up to 3 pushes. The minted row's tree digest reads seed inputs only, so a merge that changed none
     leaves the row true;
   - by a seed input, it re-dispatches the run with the next `attempt`, so **plan** merges and proves again
     against the new tip, up to 3 attempts, and bounces when they are spent. A chain's re-dispatch passes the
     published tag as `seed`: the seed is reused and never frozen again, and **pin** builds the merged tree
     against the new tip.

   It records the sha `main` received as the annotation `landed`, and a rehearsal records the sha `main`
   would receive as `rehearsed`. CI runs on the pin with the new seed.
8. **retract** runs for every chain run that did not re-dispatch, in both modes, and for a re-dispatched
   attempt whose plan did not succeed. It deletes the release, the tag and the chain ref — each checked
   against this run's commits — unless `main` pins the tag, meaning some commit on `main` set it in
   `scripts/seed-tag`, whichever commit carries the pin; it deletes nothing when `main` cannot be read.
   Re-running the job is safe.
9. **consume** runs last, whatever the outcome, landed or bounced, in either mode, unless the run
   re-dispatched itself. It deletes the branch the run ran on with a compare-and-swap push on the run's
   head, so a branch pushed again meanwhile is kept. Nobody deletes the branch by hand; a session that
   cannot force-push submits a rewritten commit under the same name with a plain push.

`S` and the pin commit are committed by `github-actions[bot]` at the time **plan** recorded, so every job
recomputes the same commits from the plan's outputs. The workflow has no concurrency group: land runs go in
parallel, no pending run is replaced, and the compare-and-swap push is the only serialisation. The workflow
never rebases. For a chain, **chain**, **prove** and **publish** run on `S` as pushed, whatever `main` does
meanwhile, and the merge at the tip is **pin**'s; every other land proceeds while a chain land runs.

⭐ **`Land: rehearsal` is the dry run, and runs every step**: the plan, and for a chain the proofs, the
publish and the fetch-back. It never mints the token that pushes `main`: the **land** job runs without it,
stops before the push, and keeps the commit it would push as the artifact `would-push`. A chain
rehearsal always retracts, so it leaves nothing behind. Like every run, it consumes its branch.

**The scale row** is minted by the door. After the final merge the plan runs `maxon scale-test --note=…`
with the compiler it built, in the merged tree, and the row lands in the same commit, so `check-scale-rows.sh`
and `scale-test --check-row` see a row that is last and carries the commit's digest. Every row is minted at
one path with one corpus, on the runner, in a fixed environment (`env -i` with `PATH`, `HOME` and `TMPDIR`
set), so row-to-row deltas print: the compiles `scale-test` measures inherit the environment, and its size
enters the Bytes rows. The author still reads the ladder and
takes its A/B against a control; it mints nothing (`docs/optimization-log.md`).

**How a commit reaches `main`:**

- an ordinary commit: one commit on `origin/main`, landed with `scripts/land.sh submit [--rehearsal]
  [--name=<n>]`, which pushes `HEAD` to `land/<n>` (`<n>` defaults to the current branch), dispatches
  `land.yml` on it and waits for the verdict; or one commit with `Land: main` in its final paragraph, pushed
  to its branch, followed with `scripts/land.sh await <branch> <head>`;
- a commit the pinned seed refuses, from any session: only through `land.yml` — push the chain with
  `Land: main` on its head, or run `scripts/land.sh submit`.

The base of a land, ordinary or chain, is the merge-base with `origin/main`, so the branch needs no rebase
when `main` has moved. `submit` pushes one name per branch, `land/<n>`, so a rewritten commit goes up under the
same name; a push the remote refuses because the name still exists means a run of it is going, or a run
kept it.

**The verdict ends the change.** `scripts/land.sh await` waits for the run and reports success only for a
successful run, printing the sha `main` received — the door's commit, which has the same author and message as
the head but is not the head. For a rehearsal it says `main` would receive that sha. A run that ends in any
other conclusion fails `await`, even after it recorded a sha. A bounce — a file
both sides changed, a seed that refuses the merged tree, a refused note, a misplaced trailer, a hand-made
scale row — prints what to fix; the author rebases by hand, re-takes only the results whose inputs changed,
and submits again under the same name. A chain's `S'` is the commit on `main` whose `Bootstrap-Chain:`
trailers end with the head commit, with `Frozen-From:` naming `S` when `S'` differs from it; the seed tag
stays on `S`.

⛔ **Old seeds stay published.** Every `seed-*` release is pinned by some commit, and bisecting and a
benchmark's control arm build from them. A release nothing pins is a retracted one, and `land.yml`
deletes it itself.

---

## The version lives in the binary

Nothing writes the version down. `maxon-bin/maxon.maxproj` derives it from git and hands it to the compiler as
`--define`, the way go's `-ldflags -X` does — a tag `vX.Y.Z` or a branch `release/X.Y.Z` gives the
number, and anything else is `dev`.

⛔ **`maxon-bin/Compiler/Version.maxon` IS TRACKED SOURCE CARRYING `dev` DEFAULTS, AND GENERATING IT
INSTEAD CANNOT WORK.** Generating it needs a compiler, and the compiler cannot be built without it, so
a fresh checkout had no `CompilerVersion` at all and every CI lane failed before reaching the suite.
A default that is honest makes the file self-sufficient; `--define` is how a build with more to say
says it, and nothing rewrites the file, so a build never dirties the working tree.

What ships is whatever the **built compiler reports**:

```bash
./maxon-bin/.maxon/maxon version    # maxon 0.1.1 (a1b2c3d 2026-09-09) (x64-windows)
```

The shape is rustc's: the release number, then the commit and day it was built from. The last two are
what make a bug report about "0.1.1" answerable when there have been forty builds of 0.1.1.

`release.sh` reads it from there and from nowhere else, and `--publish` refuses when the tag
disagrees with it. That single check is what keeps the archives and the Homebrew formula from naming
different releases — a binary built before a version bump reports the old one, and asking the
artifact is the only way to notice.

⛔ **THE WORKFLOWS BUILD TWICE: THE SEED BUILDS `C1`, AND `C1` BUILDS THE COMPILER THAT SHIPS**
([`scripts/build-from-seed.sh`](../scripts/build-from-seed.sh)). The seed builds `maxon-bin` as a
PATH, which leaves `maxon-bin/maxon.maxproj` — where the version comes from — unrun, so its output
reports `dev`; that output's own runtime is also the seed's. `C1` then runs `maxon run build`, whose
task in the root `maxon.maxtasks` delegates to `maxon-bin/maxon.maxproj`, so the second build carries
this ref's version and runtime. MEASURED on the 0.1.1
rehearsal: one build produced `maxon-dev-x64-windows`.

⇒ **Cut a `release/X.Y.Z` branch or tag `vX.Y.Z`, then rebuild, then package.** The number follows
the ref, so there is no file to forget to edit — but packaging without the rebuild still publishes
whatever the binary already said.

`release.yml`'s **`guard` job refuses any other ref before a runner starts**, because the two checks
that would otherwise catch it are both late: a compiler built on `main` reports `dev` and is refused
by `--publish` only after four runners have each built and suite-tested it. A `release/X.Y.Z` branch
**builds and tests but publishes nothing** — that is what rehearsing a release is — and only a
`vX.Y.Z` tag publishes, since a branch has no tag and could only invent a version.

---

## ⛔ A runtime change takes two self-compiles

The compiler emits its runtime into every program it builds, itself included. So a change under
`maxon-bin/Compiler/Runtime/` reaches the programs the new compiler builds after ONE build, but
reaches **the compiler's own behaviour** only after a second — the first build's output carries a
runtime emitted by the compiler that predates the change.

That matters for a release because `release.sh --package` runs the suite with the compiler it is about
to ship, and the spec-test worker IS that compiler. The workflows always build twice from the seed;
locally, `maxon run build` builds a second time by itself whenever the emitted runtime changed since the
running compiler was built, and prints why. `fixpoint.sh` is
blind to it: it compares two stages that are both past the convergence point while the slot still holds
the one before it.

---

## The pipeline

`scripts/release.sh` is two halves because they run in different places.

### `--package` — one target, on a machine of its own architecture

```bash
scripts/release.sh --package                      # this host's target
scripts/release.sh --package --target=arm64-linux # cross-build another
```

It builds, **runs the whole suite**, stages `maxon` + `stdlib/` + `runtime/` + `examples/` + `docs/` (the agent rules and every document they link, transitively) + both
licences + a generated `INSTALL.md`, and archives the result into `dist/`.

⛔ **`stdlib/` AND `runtime/` BOTH SHIP, AS SIBLINGS OF THE BINARY.** The compiler resolves `stdlib/` by
walking up from its own executable and reaches `runtime/` beside it, so an archive carrying one without
the other installs a compiler that cannot compile anything.

- ⛔ **A cross-built archive is not a tested one.** `--target=` cannot run the suite for a machine it
  is not, and says so on stderr; it leaves a `<archive>.untested` marker beside the archive, and
  `--publish` flags that archive in the release notes' asset list. Use it for a target you
  have no hardware for, never as a shortcut past a runner you do have.
- `--skip-tests` exists for iterating on the packaging itself. It is not a release path.

### `--publish` — once, anywhere, over what the first half left

```bash
scripts/release.sh --publish v0.1.0 --dry-run   # assemble, verify, upload nothing
scripts/release.sh --publish v0.1.0
```

It writes `SHA256SUMS`, writes `dist/NOTES.md` if nobody else did, checks the tag against the
binary, and creates the GitHub release. A `dist/NOTES.md` you wrote by hand wins — the generated one
is a floor, not a template to fight.

⭐ **The generated notes link to maxon.dev rather than copying it**: the release's announcement post,
the release notes page, and the installation page, then the asset list and a line about `SHA256SUMS`. The
release notes and the install instructions each live in one place, and notes published once cannot follow
the installation page as it changes. `website.yml` deploys right after the release, so the links go
live within minutes.

---

## Targets are rows of `maxon-bin/targets.tsv`

Every target the compiler accepts is one row of `maxon-bin/targets.tsv`, and the compiler, the scripts, the
workflows and the installers read their targets from it. It is TAB-separated with a header row, `-` marks an
empty cell, and a cell that holds several values is a comma list. Readers find a column by its header name.

| Column | Holds |
|---|---|
| `target` | `<cpu>-<os>`, as `--target=` spells it |
| `runner` | the GitHub runner that builds and suite-tests it natively. `-` makes it a cross target, whose suite runs on the `cross-suite` lane |
| `executable` | the extension of its executables |
| `archive` | the extension of its release archive |
| `debugger` | `yes` when `maxon debug` serves a program on that host, else `no` |
| `channels` | where a release ships it: `release`, `homebrew`, `install-sh`, `install-ps1`, `docker` |
| `docker` | its Docker platform architecture |
| `shell` | the login shell `install-script.yml` runs `install.sh` under |
| `roles` | the single-lane CI work it hosts: the steps `cpu-probe`, `codecs`, `targets-gate` and `vscode-suite` (the VS Code extension suite, in its `corpora` job), and the `cross-suite` job |
| `suite-shards` | how many jobs `ci.yml` splits that lane's spec suite into; on a `cross-suite` row, the cross-target suites too. The counts keep one run's test jobs within what GitHub Free runs at once (20 jobs, 5 of them on macOS) at similar lengths: a job past the limit waits for a runner, and the run lasts as long as its longest job |

Workflows build their matrices with `scripts/targets/matrix.sh`, which selects rows by `native`, `cross`
or `COLUMN=VALUE`. `--with NAME=A,B` crosses every row with a list, and `--count NAME=COLUMN` expands each
row into `NAME=1,…,N` with N read from that row's `COLUMN`. `ci.yml` builds each lane's compiler once, in
its `build` job, and that lane's test jobs download it; its suite jobs come from
`--count shard=suite-shards`.

### Adding a target

1. Add its row.
2. Run `scripts/targets/generate.sh`. It rewrites `maxon-bin/Compiler/TargetsGenerated.maxon`, the
   compiler's `supportedTargets()` and `debuggerTargets()`; `ci.yml` runs it with `--check` and fails on
   drift. A row naming a cpu or os with no `CpuArch` or `Os` case fails the build: what a target means —
   its backend, its runtime, and the debug interface `debugInterfaceOf` in
   `maxon-bin/Debug/Backend/BackendChoice.maxon` picks for a `debugger=yes` row — is compiler code.
3. Build, run `maxon test tests/targets`, and fix what it names: `scripts/fetch-vendor.sh` needs a URL and a
   hash for every tool on every native target.

The installers take the new target from the release itself, whose `SHA256SUMS` lists its archive.

---

## Platform-specific facts that have bitten

- ⛔ **On macOS, build the binary AS `maxon` and never rename it afterwards.** `MachOWriter` bakes the
  output filename into the ad-hoc code-signature identifier. Measured: two otherwise byte-identical
  self-compiles differ in exactly one byte when written as `stage2` and `stage3`, so a post-build
  rename leaves a signature naming a file that no longer exists.
- ⛔ **The executable bit has to be put into the archive where the filesystem cannot carry one.** `tar`
  records the mode it finds on disk, and Windows has no POSIX mode to find — `chmod +x` in Git Bash
  changes nothing a native `tar` can see. Symptom when this lapses: `bash: ./maxon: Permission denied`
  on Linux and macOS, from an archive that looks perfectly normal.
  ⚠ `--mode` is GNU tar's and **macOS ships bsdtar, which refuses it**, so `make_tar` PROBES for the
  flag rather than inferring it from the platform, and falls back to a plain tar where `chmod +x` is
  real. `require_executable_in_tar` then reads the mode back out of the listing and fails the package
  if it is not executable — this bit has shipped wrong twice and neither build nor upload noticed.
- ⛔ **PowerShell's `Compress-Archive` writes backslashes as path separators**, which extract as files
  with literal backslashes in their names everywhere but Windows. It is the last fallback and warns
  when used; `zip`, then Windows' own bsdtar, come first.
- ⚠ **`maxon.mxdbg` is not a release asset.** Only a cross-build creates one — a native package copies
  an already-built compiler — so this defect hides on whichever target you happen to build natively.
  `package_one` sweeps the stage.

---

## The install scripts

`curl -fsSL https://maxon.dev/install.sh | sh` and `irm https://maxon.dev/install.ps1 | iex` are the
primary way to install. Both scripts live in `website/public/` and are served verbatim by the website
deploy, as `text/plain` (`website/public/_headers`). They read the newest version from the redirect of
`releases/latest`, download that release's archive and `SHA256SUMS` from GitHub, and install into
`~/.maxon`. So a release is installable by script the moment it is published, with nothing to
regenerate.

Each script derives this machine's `<cpu>-<os>` and reads `SHA256SUMS`
first: it is that release's exact list of builds, so a machine it does not name is refused with
`Maxon v<version> has no build for <cpu>-<os>` before any archive is downloaded. The Docker image does the
same with the platform it is built for.

The swap replaces `bin/`, `stdlib/`, `runtime/`, `examples/` and `docs/` (a release that ships no `docs/`
leaves none), and `~/.maxon/scriptlib/`, the user's
own packages, stays as it is. After a successful swap each script runs the new compiler's
`check-scriptlib` when that directory exists and the compiler lists the command
(`help check-scriptlib`); a failing check prints its diagnostics and a `scriptlib needs updating`
notice, and the exit status stays the install's.

`install-script.yml` runs both on every platform against the latest release. `release.yml` starts it
once a release is published, and a push that touches either script runs it too.

⛔ **THE SCRIPTS' INTERFACE IS PERMANENT.** `maxon upgrade` runs them from every compiler that has
it, with `MAXON_INSTALL` set to its own install and `--no-modify-path` / `-NoPathUpdate`, and reads
only the exit status. Renaming, dropping or changing the meaning of any of those breaks upgrading from
every release already shipped. A new option is fine; a changed one is not.

---

## Package managers

The Homebrew formula is **generated from the published artifacts, never committed**, because it
carries the hash of each archive. A formula checked in beside the source is stale the first time anyone
rebuilds.

| | |
|---|---|
| **Homebrew** | `installer/homebrew/generate.sh` → `dist/homebrew/maxon.rb`, for the targets whose `channels` cell in `maxon-bin/targets.tsv` holds `homebrew`. `homebrew.yml` installs and `brew test`s it on each of them before committing it to `maxon-lang/homebrew-tap` as `Formula/maxon.rb`. ⛔ Installs as `maxon-lang/tap/maxon` and cannot be shortened: bare `maxon` is Maxon Computer's **cask**. |
| **Docker** | `installer/docker/Dockerfile`, built by `docker.yml` from the release's own archives into `ghcr.io/maxon-lang/maxon` — `debian` (the default) and `distroless` variants, for each target whose `channels` cell holds `docker`, on the platform its `docker` column names, each built on its own runner and tested before anything is pushed. Tags `X.Y.Z` and `X.Y` (plus `X` from 1.0), with `-distroless` for the second variant; `latest` and `distroless` move only when the version is the newest release. ⚠ GHCR creates the package private: make it public once, in the organisation's package settings. |
| **VS Code** | `vscode-extension/`, published to the Marketplace and Open VSX from the same `.vsix`. Its version is a calendar version, `YEAR.MONTH.PATCH` (`2026.9.0`), independent of the compiler's so the two are never confused: a release takes the month it ships in, a second that month bumps PATCH, no leading zeros. `scripts/extension-release-gate.sh` refuses a changed extension whose version did not move. |

---

## The release notes

**`RELEASE_NOTES.md` is written by hand**, for people installing a compiler rather than changing one.

⭐ **A commit subject is not a release note.** MEASURED on `v0.1.0..HEAD`: 21 commits, of which
about six mean anything to a reader — the rest are CI, spec pins, docs and the release tooling
itself. Generating the entry and then correcting it means rewriting seventeen lines of twenty-one,
which is hand-writing with extra ceremony.

Each release gets a `## X.Y.Z — YYYY-MM-DD` heading and `### Breaking changes` / `### Added` /
`### Changed` / `### Fixed` / `### Removed` beneath it, in that order and each only when it has
something to say. Write the rest at the cut, with the whole release in view, so related changes are
described together rather than as one bullet each.

**A release breaks whatever it has to.** Maxon never holds back an improvement to stay compatible, and
never ships a deprecation period: an agent applies the fix the release notes give, and the compiler shows
each project the fixes it owes on its first build with the new release.

⛔ **Every breaking change carries its fix.** A change that can break an existing project, script or
workflow — a removed or renamed spelling, command, flag, file name, syntax or standard-library API, or a
program the compiler accepted that it now refuses or runs differently — is a bullet under
`### Breaking changes`: the change, then an indented `Fix:` line saying how to put the project right.

```markdown
### Breaking changes

- `Build.buildOne` in a build manifest is renamed `Build.build`.
  Fix: call `Build.build` with the same arguments.
```

A bullet and its `Fix:` line may each wrap onto further lines indented two spaces. The fix is concrete
and short, and is checked against the documentation as it stands. The release notes may say how to fix
what a release broke; commands, install steps and usage advice for the product as a whole stay on the
website's installation page and in each archive's `INSTALL.md`.

**The compiler reads these entries.** `node website/scripts/sync-docs.mjs` compiles every release
heading and every breaking change into `maxon-bin/Compiler/BreakingChangesGenerated.maxon`, which
`maxon build` and the MCP `breaking_changes` tool answer from, so a project built with a newer
compiler is told what changed since the release that last built it. The sync fails on a heading that
is neither `## Unreleased` nor `## X.Y.Z — YYYY-MM-DD`, on sections out of newest-first order, on a
`###` heading outside the five above, and on a breaking change with no `Fix:` line; `website.yml`
fails on drift.

**`## Unreleased` holds the source breaks of the release being prepared: Maxon code that compiled with the
previous release and does not compile or behaves differently with this one.** The change that causes one adds
its entry under `## Unreleased` › `### Breaking changes` in the same commit, since it is the one that knows
the fix. A change to a command, flag, tool output or event stream is not entered there as it lands; it updates
its `docs/` source, and the release notes describe it at the cut. The section is the first in the file and
exists only while it holds something. Cutting a release renames it to `## X.Y.Z — YYYY-MM-DD` and adds the
other subsections.

⛔ **An entry is owed against the latest release only.** A change to something added since that release
owes none, even when it breaks code written against `main`, and neither does a program the latest release
already refused when only its diagnostic code or message changes. Successive changes to one surface between
releases are one bullet stating the end state against the latest release, with one `Fix:`: a change to a
surface that already has an entry rewrites that entry rather than adding another. An entry is checked by
compiling the program it says breaks with the latest release's compiler, run from its unpacked archive so
that it reads the `stdlib/` and `runtime/` it shipped with, and again with the tree's compiler.

```bash
scripts/release-notes.sh --commits-since
```

lists what has landed since the last release. ⚠ **It is reference material, not a draft** — read it to
write the entry; do not paste it into one.

**One file, rendered on the site.** `announce.sh` puts the matching section in the maxon.dev post and in
the site's `/docs/release-notes/` page, and the GitHub release notes link to both. The words therefore
exist once and cannot come to disagree about what shipped.

⛔ **A release with no entry does not ship, and neither does one still holding `## Unreleased`.**
`scripts/release-notes.sh --section=<version>` refuses a version the file has no heading for and a file
that still has the `## Unreleased` heading. `release.yml`'s `guard` asks before any runner starts, and
`release.sh --publish` asks again at the last moment — an authored file's failure mode is that nobody
wrote it.

⚠ **A hand-written `dist/NOTES.md` replaces the whole body**, the links to the site included.

⚠ **The VS Code extension keeps its own `vscode-extension/CHANGELOG.md`**, also hand-written, on its
own version line. The marketplace renders it as a tab.

---

## Repository secrets

A workflow with nothing to do **skips with a warning** rather than failing. A red mark on every
release until someone adds a secret teaches people to ignore red marks.

| Secret | Used by | Without it |
|---|---|---|
| `TAP_TOKEN` | `homebrew.yml` | The formula is generated but not pushed; commit `dist/homebrew/maxon.rb` to the tap by hand. |
| `VSCE_PAT` | `vscode-extension.yml` | ⛔ **A hard failure when the release must publish the extension.** |
| `OVSX_PAT` | `vscode-extension.yml` | ⛔ Likewise — VSCodium, Cursor and Windsurf install from Open VSX. |
| `LAND_APP_ID` | `land.yml` | ⛔ **A land in `main` mode fails before it publishes anything.** Rehearsals do not need it. |
| `LAND_APP_PRIVATE_KEY` | `land.yml` | ⛔ Likewise. |

⛔ **The two extension secrets are the exception, and only in the case that has already been
decided.** `scripts/extension-release-gate.sh` answers `skip` when nothing under `vscode-extension/`
changed, and those steps do not run at all — which is the ordinary case. When it answers `publish`,
a missing credential is a silent failure of something the gate just asserted: the marketplace would
stay on an old version of an extension whose install flow points at a release that has moved on.

`GITHUB_TOKEN` covers the release itself and nothing else; it cannot write to another repository,
which is why the tap needs its own.

**The landing App** pushes `main` for `land.yml`, because a push made with `GITHUB_TOKEN` starts no
workflow, and CI must run on what lands. It is a GitHub App installed on this repository alone, with the
repository permissions **Contents: write** and **Workflows: write** — without the second, a land whose
commit touches `.github/workflows/` is refused. `LAND_APP_ID` is its App ID and `LAND_APP_PRIVATE_KEY` a
private key generated for it. Only the jobs that mint its token name them: **credential**, which drops the
token after proving it, and **land**, which holds it for the push and runs git alone. The jobs that build a
branch's compilers (**plan**, **chain**, **prove**) hold no secret and no write access to `main`. ⚠ A run uses the `land.yml` and `scripts/land.sh` of the branch it lands, so anyone who can push a branch can reach the App: push access to this repository is the access to `main`. `GITHUB_TOKEN` creates the seed's tags and release, so a seed release starts none of the
`release: published` workflows.

---

## Cutting a release

A release happens on a **release branch**, and the tag is what turns it into one.

`/release` is the agent-facing form of this procedure and links back here for every "why".

### 1. Cut the branch

```bash
git checkout -b release/0.1.1
```

⭐ **The branch is what makes preparation possible.** A release usually takes more than one commit to
get right — the release notes entry, a last documentation fix, a version-sensitive correction — and a
compiler built on `release/X.Y.Z` already reports `X.Y.Z`, so everything can be rehearsed at the real
version before anything is public. On such a branch `release.yml` builds and suite-tests all four
targets and **publishes nothing**.

⚠ **Pushing the branch does not start it** — `release.yml` triggers on `v*` tags and
`workflow_dispatch` only. Rehearse with `gh workflow run release.yml --ref release/X.Y.Z`.

### 2. Write the release notes entry

```bash
scripts/release-notes.sh --commits-since
```

Read what landed and write the `## 0.1.1` section by hand — what a user would want to know, not what
each commit did. See **The release notes** above: rename `## Unreleased` to `## 0.1.1 — YYYY-MM-DD`, whose breaking changes are already in place, and add the other subsections. Then run `node website/scripts/sync-docs.mjs` and commit the regenerated
`maxon-bin/Compiler/BreakingChangesGenerated.maxon` with the entry: it is how the compiler learns the new
release, and the preflight fails on a stale one.

### 3. Write the website's release material

```bash
scripts/announce.sh 0.1.1
```

It writes three files under `website/` — the announcement post, the release notes page, and the
`RELEASE_VERSION` the download links are built from — and **commits nothing**. Commit them with the
release notes entry and everything else this branch carries, before the preflight:

```bash
git add -A && git commit -m 'release notes: 0.1.1'
```

### 3b. Run the preflight

```bash
scripts/release-preflight.sh X.Y.Z
```

⭐ **EVERY CHECK IT RUNS ALSO RUNS AT THE TAG, WHERE FAILING IS EXPENSIVE.** `guard` refuses a tag
whose release notes entry or extension version is missing, and `website.yml`'s `deploy` is gated on its
own build: a page that has drifted from `docs/` fails that build, `deploy` SKIPS, and the run still
reports success — leaving the release notes pointing at a post the site never received. Asking here
costs seconds and moves all of it before the tag exists.

⚠ **A SKIPPED CHECK IS NOT A PASS** and the script says so: shellcheck absent, `gh` unauthenticated,
no CI run yet. Read the skips before deciding the list is clean.

⛔ Run it AFTER the rebuild and the commit — `compiler-version` reads the built binary, whose number
comes from the ref at BUILD time, and `worktree-clean` fails on anything uncommitted, because the tag
carries only the committed tree. A fix made after the preflight is committed and the preflight run
again.

### 4. Tag it

```bash
git tag -a v0.1.1 -m 'Maxon v0.1.1'
git push origin release/0.1.1 v0.1.1
```

⛔ **The tag goes on the finished branch tip.** Everything downstream reads the repository AT THE TAG:
the maxon.dev post and release notes page take their text from `RELEASE_NOTES.md` there, and `website.yml`
builds the site from there. A fix committed after the tag is a fix nothing ships.

`release.yml` then fans out over `windows-latest`, `ubuntu-latest`, `macos-15` and
`ubuntu-24.04-arm`, builds and **natively suite-tests** each target, and publishes. The `publish` job
then starts the workflows that update Homebrew, the Docker image, the VS Code extension, maxon.dev
and the install-script check, at the tag — so the download links go live only once the downloads exist.
⚠ It has to start them itself: a release created with `GITHUB_TOKEN` fires no `release: published`,
and on v0.1.1 none of them ran.

⚠ **It does not delete `release/X.Y.Z`.** Nothing has succeeded yet: the five workflows are only just
starting and nothing has verified what shipped, and a release that fails is retried on that branch
(4b). It is retired in step 5.

⚠ **The Release tags ruleset** (Settings → Rules → Rulesets) refuses any update, deletion or
non-fast-forward of a `v*` tag, so no tag moves or disappears by accident. The one deliberate deletion
is a failed release's retry, below, which lifts it for a single push.

### 4b. If the release fails — delete the tag, fix the branch, tag again

A release has failed when `release.yml` is red, any of the five workflows it starts is red, or
`scripts/release-postflight.sh` is not clean. It is retried **at the same version** (user ruling): the
release and its tag are deleted, the fix goes on `release/X.Y.Z`, and the branch is tagged again. The
release skill's §5b carries the commands; this is why each is shaped as it is.

- **Every run at the tag finishes or is cancelled first.** Deleting a tag under a running `publish`, or
  under `homebrew.yml` committing a formula, races it.
- ⛔ **The fix goes on the branch the release was cut from, and it is still there**: nothing deletes
  `release/X.Y.Z` before the release has succeeded (step 5). If it was deleted by hand, it is pushed back
  from the tag BEFORE the tag is deleted — after that, nothing on the remote reaches those commits. A
  local `release/X.Y.Z` is already there from the first attempt, which is why the branch is reset to the
  remote one with `checkout -B`: `checkout -b` fails on it, and what runs next runs on `main`.
- **The release goes before the tag**, and while no release exists `releases/latest` resolves to the
  previous one — which both installers and `maxon upgrade` follow. The window stays short.
- ⛔ **The ruleset is lifted for the one deletion and restored at once**, then read back: `active`, with
  `update`, `deletion` and `non_fast_forward`.
- ⛔ **An extension version the failed attempt published is spent.** `vsce` and `ovsx` refuse to republish
  it, but `extension-release-gate.sh` compares only against the previous release tag and still answers
  `publish` — so `guard` passes and the retry fails at its last step, after the compiler is out. Its PATCH
  is bumped on the branch.
- **What the failed attempt published is overwritten, not recalled.** `homebrew.yml` commits a new formula
  and `docker.yml` re-pushes `X.Y.Z`, `X.Y` and `latest`, but an archive someone already downloaded no
  longer matches the new `SHA256SUMS`.
- **The release notes entry and the website material follow the fix**: the entry gains what changed and takes
  the retry's date, and `announce.sh`, which refuses to overwrite a post, runs once the old one is removed.
- **Then the rebuild, the preflight and the tag, exactly as the first time.** The merge back has not run —
  it waits for a clean postflight — so `main` holds nothing of the failed attempt.

### 5. Merge the tag back, then retire the branch

```bash
git fetch origin --tags --prune
git checkout main && git merge --no-ff v0.1.1 && git push origin main
tip="$(git rev-parse --verify --quiet origin/release/0.1.1)"
if [ -z "$tip" ]; then
	echo "origin has no release/0.1.1 to retire"
elif [ "$tip" = "$(git rev-parse 'v0.1.1^{commit}')" ]; then
	git push origin --delete release/0.1.1 && git branch -d release/0.1.1
else
	echo "release/0.1.1 has commits past v0.1.1: work nothing shipped"
fi
```

⛔ **Only once `scripts/release-postflight.sh` is clean.** This commits the release's entry in the release notes,
website material and extension version to `main`; merged ahead of a retry, `main` keeps a spent
extension version and a superseded entry, and the next release trips on them.

⛔ **The branch is retired here and nowhere earlier (user ruling).** A clean postflight is the first
point at which the release has succeeded; until then a failure is retried on the branch (4b). Once it
has, a commit on `release/X.Y.Z` would describe a release that never existed, and `release.yml` would
build and test it at the real version — a branch that does not exist cannot take one. It is deleted
only when its tip IS the tagged commit: a commit past the tag is work nothing shipped, and is left for a
person.

The release notes entry, the website material and any fixes made while preparing all belong on `main` —
without this they exist only at a tag nobody builds from again. The TAG is what is merged rather than
the branch, because the tag is exactly what shipped.

⚠ **Merge, never rebase.** A merge keeps the tag an ancestor of `main`, so `release-notes.sh
--commits-since` starts after it. Rebased copies carry other ids, and the next release's listing would
offer this one's changes again as new. If `main` has moved since the branch was cut, build the merge and
run the suite before pushing it — it combines runtime work nobody has tested together.

### 6. The seed stays where it is

A release does not touch the seed: `scripts/seed-tag` pins a `seed-*` pre-release, and the tag just cut builds with
the same seed the commit before it did. Freeze a new seed only when the tree needs one, as above.

---

## Verifying a release, after it is published

```bash
scripts/release-postflight.sh X.Y.Z
```

⛔⛔ **A GREEN WORKFLOW IS NOT EVIDENCE THAT ANYTHING SHIPPED.** This asks the artifacts instead: the
four archives and `SHA256SUMS` are attached, `releases/latest` redirects to this tag (which is what
both installers resolve), the announcement post and release notes page answer 200, the served
`install.sh` and `install.ps1` are byte-identical to this tag's copies — `maxon upgrade` fetches
those at run time, so a stale one breaks upgrading from every shipped compiler — the website run's
`deploy` job did not skip and its step logged `Deployment complete`, and all five downstream
workflows ran. A MISSING downstream run is a failure, not an absence: a release created with
`GITHUB_TOKEN` fires no `release: published`, so `publish` has to start them itself.

Then, by hand, the things no script can judge:

Re-download from the release page — not the local `dist/` copy, which is the thing under test:

- Extract into a directory **outside any git checkout**, `cd` somewhere else entirely, and compile a
  program with the full path to the extracted `maxon`. This is what proves the walk up from the
  executable finds the packaged `stdlib/` and `runtime/`, and that the tree lock behaves when there is
  no checkout above either the compiler or the source.
- Both install scripts on a clean machine, then `maxon version` in a new terminal.
- `docker run --rm ghcr.io/maxon-lang/maxon maxon version`, logged out, so the package is public.
- `brew install maxon-lang/tap/maxon` on macOS and on Linux, then `maxon version` in the same shell —
  Homebrew's symlink is the point, and the compiler resolves it.
- Install the published VS Code extension on a machine with **no** compiler, and confirm the
  not-found flow offers to install one rather than dead-ending.
