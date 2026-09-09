# Releasing Maxon

Cutting a release means producing four tested compilers, one Windows installer, and the package
manifests that point at them — then publishing the lot under one version.

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

Nothing writes the version down. `build.maxon` derives it from git when it stamps
`maxon-bin/Compiler/Version.maxon`, which is generated and untracked — a tag `vX.Y.Z` or a branch
`release/X.Y.Z` gives the number, and anything else is `dev`. What ships is whatever the **built
compiler reports**:

```bash
./maxon-bin/.maxon/maxon version    # maxon 0.1.1 (a1b2c3d 2026-09-09) (x64-windows)
```

The shape is rustc's: the release number, then the commit and day it was built from. The last two are
what make a bug report about "0.1.1" answerable when there have been forty builds of 0.1.1.

`release.sh` reads it from there and from nowhere else, and `--publish` refuses when the tag
disagrees with it. That single check is what keeps the archives, the MSI's `ProductVersion`, the
winget manifest and the Homebrew formula from naming different releases — a binary built before a
version bump reports the old one, and asking the artifact is the only way to notice.

⇒ **Cut a `release/X.Y.Z` branch or tag `vX.Y.Z`, then rebuild, then package.** The number follows
the ref, so there is no file to forget to edit — but packaging without the rebuild still publishes
whatever the binary already said.

---

## ⛔ A runtime change takes two self-compiles

The compiler emits its runtime into every program it builds, itself included. So a change under
`maxon-bin/Compiler/Runtime/` reaches the programs the new compiler builds after ONE build, but
reaches **the compiler's own behaviour** only after a second — the first build's output carries a
runtime emitted by the compiler that predates the change.

That matters for a release because `release.sh --package` runs the suite with the compiler it is about
to ship, and the spec-test worker IS that compiler. Build twice whenever the seed you started from
predates a runtime change; `fixpoint.sh` will not tell you, because it compares two stages that are
both past the convergence point while the slot still holds the one that is not.

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
- On `x64-windows` it also builds the MSI, if WiX is present.

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

## The Windows installer

```bash
dotnet tool install --global wix --version 5.0.2
installer/windows/build.sh              # takes the x64-windows zip from dist/
```

⛔ **The MSI carries no UI and no custom actions**, so its only executable content is the payload —
which is the least a validation sandbox can object to. winget never used the directory chooser
anyway: it passes `INSTALLDIR="<path>"` on the command line.

WiX is the one remaining .NET dependency, and it is asked of whoever cuts a release. Nothing about
*building* Maxon needs it, and it is not a contributor prerequisite.

The MSI is per-machine, installs to `C:\Program Files\Maxon` and appends to the system PATH. It is
**unsigned**, so SmartScreen warns
on first run — documented in `INSTALL.md`, the release notes and the install page. Signing is a
follow-up before the release that goes wide.

---

## Package managers

All three manifests are **generated from the published artifacts, never committed**, because each one
carries a hash — and for winget a `ProductCode` that WiX mints fresh on every build. A manifest
checked in beside the source is stale the first time anyone rebuilds.

| | |
|---|---|
| **winget** | `installer/winget/generate.sh` → `dist/winget/`. Identifier `MaxonLang.Maxon` — **not** `Maxon.*`, which is Maxon Computer GmbH's namespace and would be rejected on review. |
| **Homebrew** | `installer/homebrew/generate.sh` → `dist/homebrew/maxon.rb`, committed to `maxon-lang/homebrew-tap` as `Formula/maxon.rb`. ⛔ Installs as `maxon-lang/tap/maxon` and cannot be shortened: bare `maxon` is Maxon Computer's **cask**, the same collision as winget. |
| **VS Code** | `vscode-extension/`, published to the Marketplace and Open VSX from the same `.vsix`. |

**winget's first submission cannot be automated.** `wingetcreate update` requires the package to
already exist in `microsoft/winget-pkgs`, so v0.1.0 goes in by hand with `wingetcreate new` once the
asset URL is live. Expect validation plus human review — days, and *after* the GitHub release exists.

---

## Repository secrets

Each workflow **skips with a warning** rather than failing when its secret is absent. A red mark on
every release until someone adds a secret teaches people to ignore red marks.

| Secret | Used by | Without it |
|---|---|---|
| `TAP_TOKEN` | `homebrew.yml` | The formula is generated but not pushed; commit `dist/homebrew/maxon.rb` to the tap by hand. |
| `VSCE_PAT` | `vscode-extension.yml` | The Marketplace is skipped; publish with `npx @vscode/vsce publish --packagePath extension.vsix`. |
| `OVSX_PAT` | `vscode-extension.yml` | Open VSX is skipped — VSCodium, Cursor and Windsurf install from there. |

`GITHUB_TOKEN` covers the release itself and nothing else; it cannot write to another repository,
which is why the tap needs its own.

---

## Cutting a release

**From v0.1.1 onward** the tag is the whole procedure:

```bash
git tag -a v0.1.1 -m 'Maxon v0.1.1'
git push origin v0.1.1
```

`release.yml` fans out over `windows-latest`, `ubuntu-latest`, `macos-15` and `ubuntu-24.04-arm`,
builds and **natively suite-tests** each target, builds the MSI from the x64-windows job's own
artifact, and publishes. Then, once the assets are live:

```bash
wingetcreate update MaxonLang.Maxon --version 0.1.1 --urls <msi-url> --submit
```

**For v0.1.0**, the same steps run by hand: package each target on hardware of its own architecture,
collect the archives into one `dist/`, build the MSI, then `--publish`.

---

## Verifying a release, after it is published

Re-download from the release page — not the local `dist/` copy, which is the thing under test:

- Extract into a directory **outside any git checkout**, `cd` somewhere else entirely, and compile a
  program with the full path to the extracted `maxon`. This is what proves the walk up from the
  executable finds the packaged `stdlib/`, and that the tree lock behaves when there is no checkout
  above either the compiler or the source.
- `winget install MaxonLang.Maxon`, then `winget uninstall`, on a clean VM.
- `brew install maxon-lang/tap/maxon`, then `maxon version` in the same shell — Homebrew's symlink
  is the point, and the compiler resolves it.
- Install the published VS Code extension on a machine with **no** compiler, and confirm the
  not-found flow offers to install one rather than dead-ending.
