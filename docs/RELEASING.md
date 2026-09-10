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

---

## The version lives in the binary

Nothing writes the version down. `build.maxon` derives it from git and hands it to the compiler as
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
([`scripts/build-from-seed.sh`](../scripts/build-from-seed.sh)). A seed older than named manifest
targets reads `maxon-bin` as a path, so it never runs `build.maxon` — where the version comes from —
and its output reports `dev`; that output's own runtime is also the seed's. `C1` builds by name, so the
second build runs the manifest and carries this ref's version and runtime. MEASURED on the 0.1.1
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

It builds, **runs the whole suite**, stages `maxon` + `stdlib/` + `examples/` + both licences +
a generated `INSTALL.md`, and archives the result into `dist/`.

- ⛔ **A cross-built archive is not a tested one.** `--target=` cannot run the suite for a machine it
  is not, and says so on stderr; `--publish` repeats it in the release notes. Use it for a target you
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
| **Homebrew** | `installer/homebrew/generate.sh` → `dist/homebrew/maxon.rb`, committed to `maxon-lang/homebrew-tap` as `Formula/maxon.rb`. ⛔ Installs as `maxon-lang/tap/maxon` and cannot be shortened: bare `maxon` is Maxon Computer's **cask**. |
| **VS Code** | `vscode-extension/`, published to the Marketplace and Open VSX from the same `.vsix`. |

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
warnings. Those live in the release notes' Install section and each archive's `INSTALL.md`, which
already carry them; an instruction in a changelog is read months later and is either stale or
duplicated. Write it at the cut, with the whole release in view, so related changes are
described together rather than as one bullet each.

```bash
scripts/changelog.sh --commits-since
```

lists what has landed since the last release. ⚠ **It is reference material, not a draft** — read it to
write the entry; do not paste it into one.

**One file, four renderings.** `release.sh` puts the matching section at the top of the GitHub release
notes; `announce.sh` puts it in the maxon.dev post and in the site's `/docs/changelog/` page. The
words therefore exist once and cannot come to disagree about what shipped.

⛔ **A release with no entry does not ship.** `scripts/changelog.sh --section=<version>` refuses a
version the file has no heading for. `release.yml`'s `guard` asks before any runner starts, and
`release.sh --publish` asks again at the last moment — an authored file's failure mode is that nobody
wrote it.

⚠ **A hand-written `dist/NOTES.md` replaces the whole body**, "What's new" included — that rule is
unchanged.

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

### 4. Tag it

```bash
git add -A && git commit -m 'changelog: 0.1.1'
git tag -a v0.1.1 -m 'Maxon v0.1.1'
git push origin release/0.1.1 v0.1.1
```

⛔ **The tag goes on the finished branch tip.** Everything downstream reads the repository AT THE TAG:
the release notes and the maxon.dev post take their text from `CHANGELOG.md` there, and `website.yml`
builds the site from there. A fix committed after the tag is a fix nothing ships.

`release.yml` then fans out over `windows-latest`, `ubuntu-latest`, `macos-15` and
`ubuntu-24.04-arm`, builds and **natively suite-tests** each target, and publishes. The `publish` job then starts the workflows that
update Homebrew, the VS Code extension, maxon.dev and the install-script check, at the tag — so the download links go live only
once the downloads exist. ⚠ It has to start them itself: a release created with `GITHUB_TOKEN` fires no
`release: published`, and on v0.1.1 none of the three ran.

⛔ **Last, it deletes `release/X.Y.Z`.** The tag is the record of what was built and published, and the
**Release tags** ruleset (Settings → Rules → Rulesets) makes every `v*` tag immutable — it cannot be
moved or deleted, only created. A branch left behind is a place to push a commit describing a release
that never existed, which `release.yml` would build and test at the real version; one that does not
exist cannot take one. It is deleted only when its tip IS the tagged commit — a commit past the tag
fails the step and leaves the branch for a person, since deleting it would strand work nothing shipped.

### 5. Merge the tag back

```bash
git fetch origin --tags --prune
git checkout main && git merge --no-ff v0.1.1 && git push origin main
git branch -d release/0.1.1
```

The changelog entry, the website material and any fixes made while preparing all belong on `main` —
without this they exist only at a tag nobody builds from again. The branch is gone by now (`publish`
deleted it), so the TAG is what is merged; it names the same commit.

⚠ **Merge, never rebase.** A merge keeps the tag an ancestor of `main`, so `changelog.sh
--commits-since` starts after it. Rebased copies carry other ids, and the next release's listing would
offer this one's changes again as new. If `main` has moved since the branch was cut, build the merge and
run the suite before pushing it — it combines runtime work nobody has tested together.

**For v0.1.0**, the same steps ran by hand: package each target on hardware of its own architecture,
collect the archives into one `dist/`, then `--publish`.

---

## Verifying a release, after it is published

Re-download from the release page — not the local `dist/` copy, which is the thing under test:

- Extract into a directory **outside any git checkout**, `cd` somewhere else entirely, and compile a
  program with the full path to the extracted `maxon`. This is what proves the walk up from the
  executable finds the packaged `stdlib/`, and that the tree lock behaves when there is no checkout
  above either the compiler or the source.
- Both install scripts on a clean machine, then `maxon version` in a new terminal.
- `brew install maxon-lang/tap/maxon`, then `maxon version` in the same shell — Homebrew's symlink
  is the point, and the compiler resolves it.
- Install the published VS Code extension on a machine with **no** compiler, and confirm the
  not-found flow offers to install one rather than dead-ending.
