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
disagrees with it. That single check is what keeps the archives, the MSI's `ProductVersion`, the
winget manifest and the Homebrew formula from naming different releases — a binary built before a
version bump reports the old one, and asking the artifact is the only way to notice.

⛔ **v0.1.1 IS CUT BY HAND, FOR THE SAME REASON v0.1.0 WAS.** `--define` is a flag the v0.1.0 seed does
not have, and the workflows build with `-o` — a PATH build, which never runs `build.maxon` and so never
supplies the version. A workflow release built from that seed reports `dev`, and `--publish` refuses the
tag: loud, but not a release. Build v0.1.1 locally, where the slot compiler runs the manifest.

⇒ **From v0.1.2 the seed understands both, so `ci.yml` and `release.yml` can drop the `-o` and build
`maxon build maxon-bin` through the manifest** — which stamps the version itself and needs nothing from
the workflow. Delete the `-o` and its comment on the day that seed lands.

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

The MSI is per-machine, installs to `C:\Program Files\Maxon` and appends to the system PATH.

### Signing

The `msi` job signs the installer with **Azure Artifact Signing** (the service formerly called
Trusted Signing). It is a signing *service*, not a certificate you hold: a certificate is minted per
request and lives about three days, so there is no key on a runner, in this repository, or on the
machine of whoever cuts a release. Authentication is **OIDC** — GitHub mints a token for one workflow
run and Azure trades it through a federated credential scoped to this repository — so no signing
credential exists that would keep working for someone who stole it.

⚠ **Only the MSI is signed.** The `maxon.exe` inside the zip and inside the MSI is not, so
SmartScreen still warns when that binary is run from an extracted archive, and winget's validation
sandbox still scans an unsigned payload. Signing the payload too means signing it in the `package`
job, before the archive is built — otherwise the MSI and the zip would carry two different compilers
for one version, which is exactly what building the MSI from the archive exists to prevent.

⭐ **Configured by repository variables, so a fork is not broken by it.** `AZURE_SIGNING_ENDPOINT`
is the switch: unset, the job builds an unsigned MSI and says so with a workflow warning; set, the
rest must be right too.

| Variable | Example |
|---|---|
| `AZURE_SIGNING_ENDPOINT` | `https://eus.codesigning.azure.net` — the region the account was created in |
| `AZURE_SIGNING_ACCOUNT` | the Artifact Signing account name |
| `AZURE_SIGNING_PROFILE` | the certificate profile name |
| `AZURE_SIGNING_CLIENT_ID` / `AZURE_SIGNING_TENANT_ID` / `AZURE_SIGNING_SUBSCRIPTION_ID` | the app registration and subscription |

They are `vars` and not `secrets` because a tenant, client and subscription id are identifiers rather
than credentials — and because `secrets` is not a context a step's `if:` can read, so gating on one
is not expressible.

⛔ **The signature is read back off the file before the MSI can be uploaded**, and the timestamp is
checked as well as the signature. A green signing step is not the question: a filter that matched
nothing leaves an unsigned installer, and an untimestamped signature is verified against a
certificate that expires within the week — it would look right in CI and fail on a user's machine
days later.

#### One-time setup, in the Azure portal

1. Register the `Microsoft.CodeSigning` resource provider on the subscription.
2. Create an **Artifact Signing account** (Basic SKU) in a supported region — its region decides the
   `endpoint` URI, e.g. East US → `https://eus.codesigning.azure.net`.
3. Assign yourself **Artifact Signing Identity Verifier** on the account (it also needs Reader at
   subscription scope), then create an **identity validation**. ⚠ It takes 1–20 business days, and it
   is the long pole — start it before anything else here matters.
   ⛔ **Individual validation is limited to the US and Canada, and its fields come from the Azure
   billing account, read-only.** The certificate subject is then a person's legal name, not
   "Maxon Language"; an organization subject needs a registered legal entity and its own validation.
4. Create a **Public Trust** certificate profile against that validation.
5. Register an Entra app, give it a **federated credential** for this repository, and assign it
   **Artifact Signing Certificate Profile Signer** — scoped to the certificate profile, not the
   subscription:
   ```
   az role assignment create --assignee <app object id>      --role "Artifact Signing Certificate Profile Signer"      --scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CodeSigning/codeSigningAccounts/<account>/certificateProfiles/<profile>"
   ```
6. Set the six repository variables above.

All of it is provisioned and signing is ON. The account is `maxonlang` in `maxon-signing` (East US),
the profile is `maxon-public-trust`, and the app registration's federated credential names the
`release` GitHub environment — which is why the `msi` job carries an `environment:` line, and why
renaming that line revokes signing rather than moving it. The signer role is scoped to the
certificate profile, not the account or the subscription.

The certificate subject is **`CN=Eric Stern, O=Eric Stern, L=El Cajon, S=ca, C=US`** — an individual
identity validation, so it names a person and not "Maxon Language". That is what a UAC prompt shows
and what winget reports as the publisher. Changing it means a new identity validation and a new
profile; it cannot be edited.

⚠ **Identity Verifier is not implied by Owner.** The role table grants "manage identity validation"
to that role alone, so a subscription owner finds the portal's **New identity** button dimmed with
nothing explaining why.

⚠ **Identity validation is not an ARM resource**, so nothing outside the portal can read it — not the
`artifact-signing` CLI extension, and not the REST API at any version the provider advertises. Its id
is needed to create a certificate profile, and copying it out of the portal is the only way to get it.

⛔ **Artifact Signing refuses free, trial and sponsored subscriptions**, so a subscription still on
a free-trial offer must be upgraded to pay-as-you-go before the account can be created at all. The
error names the cause plainly (`BadResourceOperation`) and comes back from account creation, not from
signing, so it is found at setup rather than at a release.

⚠ **An MSI built by hand is unsigned.** `installer/windows/build.sh` calls no signing service, so a
release packaged locally rather than by `release.yml` ships an unsigned installer. `release.sh` reads
the signature state out of the MSI itself and writes the matching SmartScreen line into the release
notes, so the notes cannot promise a signature the file does not carry.

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

## The changelog

**`CHANGELOG.md` is generated from git history by `scripts/changelog.sh` and is never edited by
hand.** A correction goes in `docs/changelog-overrides.txt`, which is committed beside it.

⭐ **Inclusion is opt-out: every commit in the range appears.** That is deliberate — a change cannot
be missed because nobody remembered to mention it, and the cost is that internal churn shows up until
an override drops it. Reading the whole list once per release is the editorial pass, and it is the
only one.

There is **no `Changelog:` commit trailer**, on purpose. A trailer is only ever as right as the commit
carrying it and cannot be fixed after a push — which is exactly the case the overrides file exists to
serve.

| Override | Means |
|---|---|
| `commit: <exact subject>` | which commit this record is about. The key is the **subject**, because a rebase preserves messages and shas it does not. |
| `text: <line>` | what the entry should say instead. The commit link is still appended. |
| `drop: <reason>` | why this entry does not belong. The reason is required, never a bare flag. |

⛔ **An override naming no commit, or more than one, is refused.** A key that has gone stale would
otherwise be an inert line whose failure mode is re-publishing the wording somebody corrected.

Between releases the committed file holds only versions that **have a tag** — regenerate it that way
with `scripts/changelog.sh --released-only --write`. A `## 0.1.1` heading in a tree where v0.1.1 does
not exist advertises a release nobody can download.

**One source, three renderings.** `release.sh` puts the matching section at the top of the GitHub
release notes (reading the committed file, not history, so what ships is byte-for-byte what was
reviewed); `announce.sh` puts it in the maxon.dev post; and `--scope=extension` writes the extension's
own `CHANGELOG.md`, which the marketplace renders as a tab.

⚠ **A hand-written `dist/NOTES.md` replaces the whole body**, "What's new" included — that rule is
unchanged. To alter only the wording of one entry, use the overrides file.

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

**From v0.1.1 onward**, regenerate the changelog, commit it, and tag THAT commit:

```bash
scripts/changelog.sh --release=0.1.1 --write
```

Read the diff. Anything that reads wrong, or that has no business in release notes, gets a record in
`docs/changelog-overrides.txt`; regenerate and read it again. Then:

```bash
git add CHANGELOG.md docs/changelog-overrides.txt
git commit -m 'changelog: 0.1.1'
git tag -a v0.1.1 -m 'Maxon v0.1.1'
git push origin v0.1.1
```

⛔ **The tag goes on the changelog commit, not before it.** The tag then contains the changelog, and
`guard`'s `--check` — which is what keeps that file generated rather than hand-edited — can
regenerate at the tag and get the same bytes back.

`release.yml` fans out over `windows-latest`, `ubuntu-latest`, `macos-15` and `ubuntu-24.04-arm`,
builds and **natively suite-tests** each target, builds the MSI from the x64-windows job's own
artifact, and publishes. Then, once the assets are live:

```bash
wingetcreate update MaxonLang.Maxon --version 0.1.1 --urls <msi-url> --submit
scripts/announce.sh 0.1.1
```

`announce.sh` writes the release post to the `maxon-web` checkout and pushes it, which is what
deploys maxon.dev. It runs **by hand and last** because that repository needs push credentials this
one's CI does not have — and because a step that runs after the release is live cannot fail a release
that has already happened. Preview it first with `--dry-run`.

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
