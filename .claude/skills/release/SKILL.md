---
name: release
description: Cut a release of the compiler — a release branch, the hand-written changelog entry, the website's release material, then the tag that publishes everything. Use when asked to cut, tag, ship or publish a release, or to rehearse one on a release branch. Invoke as `/release <X.Y.Z>`.
---

# Cut a release

**`docs/RELEASING.md` IS THE PROCESS AND THIS IS NOT A SECOND COPY OF IT.** Read it. It carries the
whole picture — the bootstrap chain, the double self-compile, the platform gotchas, the install
scripts, the package managers, the verification afterwards. This file is the ORDER of operations
and the checks, and it links out for every "why".

⛔ **THE TAG IS THE STEP THAT PUBLISHES.** `release.yml`'s `guard` refuses a tag with no changelog
entry or an unbumped extension before any runner starts, and `publish` runs only after all four
targets have built and natively suite-tested — so a failed target publishes nothing.

## 0 · No rehearsal unless the user asks for one

Go straight to the tag (user ruling): every commit on `main` was already tested by CI, and the tagged
run builds and suite-tests the same four targets before `publish` starts. A failed target costs a
re-tag at the next patch version, not a broken release.

When the user does ask, a `release/X.Y.Z` branch runs the whole pipeline at the real version with no
tag: `guard` recognises the ref and sets `publish=no`. ⚠ **Pushing the branch does not start it** —
`release.yml` triggers on `v*` tags and `workflow_dispatch` only:
```bash
gh workflow run release.yml --ref release/X.Y.Z
```
`guard`'s changelog and extension checks run only on a tag, so no rehearsal reaches them.

## 1 · Cut the branch

```bash
git checkout -b release/X.Y.Z
```

⛔ **THEN REBUILD, BEFORE ANYTHING ELSE.** The version comes from the ref at BUILD time, so a compiler
built before the branch existed still reports `dev`, and `release.sh --publish` refuses that. Ask
`scripts/self-compiles-needed.sh` whether this needs one build or two.

## 2 · Write the changelog entry, by hand

```bash
scripts/changelog.sh --commits-since
```

⭐ **THAT LISTING IS REFERENCE MATERIAL, NOT A DRAFT.** A commit subject is written for someone
changing the compiler; the entry is for someone installing it. Measured on `v0.1.0..HEAD`: 21 commits,
about six of which mean anything to a reader. Write `## X.Y.Z — YYYY-MM-DD` with
`### Added` / `### Changed` / `### Fixed` / `### Removed` beneath it.

⛔ **DO NOT PASTE COMMIT SUBJECTS IN.** That was tried, and generating the entry then correcting it
meant rewriting seventeen lines of twenty-one.

⛔ **STATE WHAT CHANGED, NEVER HOW TO DO ANYTHING.** No commands, no install steps, no "click More
info", no warnings about how to use it. Those belong on the website's installation page and in
each archive's `INSTALL.md`, which already carry them — a changelog is read months later by someone
asking "what is different", and an instruction in it is either stale or duplicated.

## 3 · Write the website's release material

```bash
scripts/announce.sh X.Y.Z
```

Three files under `website/` — the announcement post, the changelog page, and the `RELEASE_VERSION`
the download links are built from. It **commits nothing**; the files are committed with everything
else this branch carries.

## 4 · Tag

```bash
git add -A && git commit -m 'changelog: X.Y.Z'
git tag -a vX.Y.Z -m 'Maxon vX.Y.Z'
git push origin release/X.Y.Z vX.Y.Z
```

⛔ **THE TAG GOES ON THE FINISHED BRANCH TIP.** Everything downstream reads the repository AT THE TAG
— the maxon.dev post and changelog page, which the release notes link to, take their text from
`CHANGELOG.md` there, and
`website.yml` builds the site from there. A fix committed after the tag is a fix nothing ships.

## 5 · Watch it

`release.yml` builds and natively suite-tests four targets and publishes, then starts the Homebrew,
Docker, VS Code extension, maxon.dev and install-script workflows at the tag — so the download links
go live only once the downloads exist. Check all five actually ran: a release created with `GITHUB_TOKEN`
fires no `release: published`. `install-script` green is what says both one-line installers can
install the new release.

**Report what actually happened**, per job, and read the deploy step's log rather than its exit code:
a missing credential SKIPS with a notice and still reports success.

Last, `publish` deletes `release/X.Y.Z` — the tag is the record, and the **Release tags** ruleset makes
every `v*` tag immutable. ⚠ If the branch has a commit past the tag, that step FAILS and leaves the
branch: tell the user, since it is work nothing shipped.

## 6 · Merge the tag back

```bash
git fetch origin --tags --prune
git checkout main && git merge --no-ff vX.Y.Z && git push origin main
git branch -d release/X.Y.Z
```

⚠ The merge back is not optional — the changelog entry and the website material exist only at the
tag until it happens. The branch is already gone, so the TAG is what is merged.

⚠ **Merge, never rebase**, and if `main` moved, build and suite-test the merge before pushing it. A
merge keeps the tag an ancestor of `main`; rebased copies would reappear in the next release's
`--commits-since`.

## What this skill may not do

- **Never create credentials, accounts or tokens**, and never ask the user to paste a secret. Secrets
  are set with `gh secret set`, which prompts and hides the input.
- **Never force-push a tag**, and never delete a published one. A wrong release is superseded by the
  next patch version, which is what patch versions are.
