# Releasing Maxon

Cutting a release means producing four tested compilers and the package manifests that point at
them — then publishing the lot under one version.

Almost all of it is automated. This document exists for the parts that are not, and for the one
release that cannot use the automation at all.

---

## ⛔ Maxon compiles Maxon, so a release needs a release

There is no second implementation. Every machine that builds the compiler needs a compiler first, and
the one it takes is **the previous published release for its own target**. That makes the chain
self-sustaining from v0.1.1 onward and makes **v0.1.0 the base case it cannot serve** — there is
nothing to seed from, so its four archives are built and tested by hand and published without the
workflow. `release.yml` says so and refuses rather than failing obscurely.

The same fact makes `ci.yml` inert until the first release exists. Its seed step skips the run and
writes a notice; it becomes real the moment a release is published, with no edit to the file.

### ⛔ A tree can declare something its own seed refuses, and a shim is how it is still built

`stdlib/` is parsed by every compile, so a `__Managed*` entry the previous release has never heard of
fails the seed's FIRST build with E2015 — not only in this tree but on every CI lane and every release
runner, none of which has another compiler to reach for. Nothing later on `main` can fix that: the
compiler that accepts the entry is the one the release has not shipped yet.

`scripts/seed-shim/` closes it. Each patch there withdraws one such declaration, and
`scripts/build-from-seed.sh` stages them only when a plain seed build has already been refused, then
restores every file before `C1` builds `C2`.

⭐ **A patch withdraws the DECLARATION and never the compiler source that implements the entry**, which
is what makes this sound rather than a way of shipping a hobbled compiler: the seed accepts the
compiler's own tables, so `C1` knows the builtin, and `C1` compiles the unshimmed tree. Only `C1`'s own
copy of the withdrawn function is stubbed, and nothing in a build calls it.

⛔ **Delete the patch in the release after the one that ships the entry.** It is inert from the moment a
published seed accepts the declaration — the plain build succeeds and the directory is never read — and
a patch left to rot no longer applies, turning the next genuine refusal into a confusing failure.

---

## The version lives in the binary

Nothing writes the version down. `project.maxon` derives it from git and hands it to the compiler as
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
PATH, so it never runs `project.maxon` — where the version comes from — and its output reports `dev`;
that output's own runtime is also the seed's. `C1` then runs `maxon run build`, so the second build
goes through the manifest and carries this ref's version and runtime. MEASURED on the 0.1.1
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
locally, `scripts/self-compiles-needed.sh` says whether you need one build or two. `fixpoint.sh` will
not tell you, because it compares two stages that are both past the convergence point while the slot
still holds the one that is not.

---

## The pipeline

`scripts/release.sh` is two halves because they run in different places.

### `--package` — one target, on a machine of its own architecture

```bash
scripts/release.sh --package                      # this host's target
scripts/release.sh --package --target=arm64-linux # cross-build another
```

It builds, **runs the whole suite**, stages `maxon` + `stdlib/` + `runtime/` + `examples/` + both
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
the changelog page, and the installation page, then the asset list and a line about `SHA256SUMS`. The
changelog and the install instructions each live in one place, and notes published once cannot follow
the installation page as it changes. `website.yml` deploys right after the release, so the links go
live within minutes.

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
| **Homebrew** | `installer/homebrew/generate.sh` → `dist/homebrew/maxon.rb`, for arm64 macOS and x64/arm64 Linux. `homebrew.yml` installs and `brew test`s it on all three before committing it to `maxon-lang/homebrew-tap` as `Formula/maxon.rb`. ⛔ Installs as `maxon-lang/tap/maxon` and cannot be shortened: bare `maxon` is Maxon Computer's **cask**. |
| **Docker** | `installer/docker/Dockerfile`, built by `docker.yml` from the release's own archives into `ghcr.io/maxon-lang/maxon` — `debian` (the default) and `distroless` variants, amd64 and arm64, each built on its own runner and tested before anything is pushed. Tags `X.Y.Z` and `X.Y` (plus `X` from 1.0), with `-distroless` for the second variant; `latest` and `distroless` move only when the version is the newest release. ⚠ GHCR creates the package private: make it public once, in the organisation's package settings. |
| **VS Code** | `vscode-extension/`, published to the Marketplace and Open VSX from the same `.vsix`. Its version is a calendar version, `YEAR.MONTH.PATCH` (`2026.9.0`), independent of the compiler's so the two are never confused: a release takes the month it ships in, a second that month bumps PATCH, no leading zeros. `scripts/extension-release-gate.sh` refuses a changed extension whose version did not move. |

---

## The changelog

**`CHANGELOG.md` is written by hand**, for people installing a compiler rather than changing one.

⭐ **A commit subject is not a changelog entry.** MEASURED on `v0.1.0..HEAD`: 21 commits, of which
about six mean anything to a reader — the rest are CI, spec goldens, docs and the release tooling
itself. Generating the entry and then correcting it means rewriting seventeen lines of twenty-one,
which is hand-writing with extra ceremony.

Each release gets a `## X.Y.Z — YYYY-MM-DD` heading and `### Added` / `### Changed` / `### Fixed` /
`### Removed` beneath it.

⛔ **It states what changed and never how to do anything** — no commands, no install steps, no usage
warnings. Those live on the website's installation page and in each archive's `INSTALL.md`, which
already carry them; an instruction in a changelog is read months later and is either stale or
duplicated. Write it at the cut, with the whole release in view, so related changes are
described together rather than as one bullet each.

```bash
scripts/changelog.sh --commits-since
```

lists what has landed since the last release. ⚠ **It is reference material, not a draft** — read it to
write the entry; do not paste it into one.

**One file, rendered on the site.** `announce.sh` puts the matching section in the maxon.dev post and in
the site's `/docs/changelog/` page, and the GitHub release notes link to both. The words therefore
exist once and cannot come to disagree about what shipped.

⛔ **A release with no entry does not ship.** `scripts/changelog.sh --section=<version>` refuses a
version the file has no heading for. `release.yml`'s `guard` asks before any runner starts, and
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

⛔ **The two extension secrets are the exception, and only in the case that has already been
decided.** `scripts/extension-release-gate.sh` answers `skip` when nothing under `vscode-extension/`
changed, and those steps do not run at all — which is the ordinary case. When it answers `publish`,
a missing credential is a silent failure of something the gate just asserted: the marketplace would
stay on an old version of an extension whose install flow points at a release that has moved on.

`GITHUB_TOKEN` covers the release itself and nothing else; it cannot write to another repository,
which is why the tap needs its own.

---

## Cutting a release

A release happens on a **release branch**, and the tag is what turns it into one.

`/release` is the agent-facing form of this procedure and links back here for every "why".

### 1. Cut the branch

```bash
git checkout -b release/0.1.1
```

⭐ **The branch is what makes preparation possible.** A release usually takes more than one commit to
get right — the changelog entry, a last documentation fix, a version-sensitive correction — and a
compiler built on `release/X.Y.Z` already reports `X.Y.Z`, so everything can be rehearsed at the real
version before anything is public. On such a branch `release.yml` builds and suite-tests all four
targets and **publishes nothing**.

⚠ **Pushing the branch does not start it** — `release.yml` triggers on `v*` tags and
`workflow_dispatch` only. Rehearse with `gh workflow run release.yml --ref release/X.Y.Z`.

### 2. Write the changelog entry

```bash
scripts/changelog.sh --commits-since
```

Read what landed and write the `## 0.1.1` section by hand — what a user would want to know, not what
each commit did. See **The changelog** above.

### 3. Write the website's release material

```bash
scripts/announce.sh 0.1.1
```

It writes three files under `website/` — the announcement post, the changelog page, and the
`RELEASE_VERSION` the download links are built from — and **commits nothing**. Commit them with
everything else this branch carries.

### 3b. Run the preflight

```bash
scripts/release-preflight.sh X.Y.Z
```

⭐ **EVERY CHECK IT RUNS ALSO RUNS AT THE TAG, WHERE FAILING IS EXPENSIVE.** `guard` refuses a tag
whose changelog entry or extension version is missing, and `website.yml`'s `deploy` is gated on its
own build: a page that has drifted from `docs/` fails that build, `deploy` SKIPS, and the run still
reports success — leaving the release notes pointing at a post the site never received. Asking here
costs seconds and moves all of it before the tag exists.

⚠ **A SKIPPED CHECK IS NOT A PASS** and the script says so: shellcheck absent, `gh` unauthenticated,
no CI run yet. Read the skips before deciding the list is clean.

⛔ Run it AFTER the rebuild, not before — `compiler-version` reads the built binary, and the number
comes from the ref at BUILD time.

### 4. Tag it

```bash
git add -A && git commit -m 'changelog: 0.1.1'
git tag -a v0.1.1 -m 'Maxon v0.1.1'
git push origin release/0.1.1 v0.1.1
```

⛔ **The tag goes on the finished branch tip.** Everything downstream reads the repository AT THE TAG:
the maxon.dev post and changelog page take their text from `CHANGELOG.md` there, and `website.yml`
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
- **The changelog entry and the website material follow the fix**: the entry gains what changed and takes
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

⛔ **Only once `scripts/release-postflight.sh` is clean.** This commits the release's changelog entry,
website material and extension version to `main`; merged ahead of a retry, `main` keeps a spent
extension version and a superseded entry, and the next release trips on them.

⛔ **The branch is retired here and nowhere earlier (user ruling).** A clean postflight is the first
point at which the release has succeeded; until then a failure is retried on the branch (4b). Once it
has, a commit on `release/X.Y.Z` would describe a release that never existed, and `release.yml` would
build and test it at the real version — a branch that does not exist cannot take one. It is deleted
only when its tip IS the tagged commit: a commit past the tag is work nothing shipped, and is left for a
person.

The changelog entry, the website material and any fixes made while preparing all belong on `main` —
without this they exist only at a tag nobody builds from again. The TAG is what is merged rather than
the branch, because the tag is exactly what shipped.

⚠ **Merge, never rebase.** A merge keeps the tag an ancestor of `main`, so `changelog.sh
--commits-since` starts after it. Rebased copies carry other ids, and the next release's listing would
offer this one's changes again as new. If `main` has moved since the branch was cut, build the merge and
run the suite before pushing it — it combines runtime work nobody has tested together.

**For v0.1.0**, the same steps ran by hand: package each target on hardware of its own architecture,
collect the archives into one `dist/`, then `--publish`.

---

## Verifying a release, after it is published

```bash
scripts/release-postflight.sh X.Y.Z
```

⛔⛔ **A GREEN WORKFLOW IS NOT EVIDENCE THAT ANYTHING SHIPPED.** This asks the artifacts instead: the
four archives and `SHA256SUMS` are attached, `releases/latest` redirects to this tag (which is what
both installers resolve), the announcement post and changelog page answer 200, the served
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
