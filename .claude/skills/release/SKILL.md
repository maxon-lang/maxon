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
run builds and suite-tests the same four targets before `publish` starts. A failed target publishes
nothing and costs a retry at the same version (§5b), not a broken release.

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

## 3b · Preflight — the checks the tag would otherwise apply

```bash
scripts/release-preflight.sh X.Y.Z
```

⛔ **RUN IT, AND RUN IT AFTER THE REBUILD.** Every check here also runs at the tag, where a failure
costs a re-tag or a release whose links 404. It reads the worktree, the copied docs, the extension
gate, the changelog entry, shellcheck, CI at this commit, and the built binary's version.

⚠ **SKIPPED IS NOT PASSED.** The summary names every skip; read them rather than the exit code alone.

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

**Then verify what actually shipped**, which is not the same question as whether the jobs are green:

```bash
scripts/release-postflight.sh X.Y.Z
```

⛔⛔ **DO NOT MERGE BACK UNTIL THIS IS CLEAN.** §6 commits the release's changelog, website material
and extension version to `main`; run it over a release that has to be cut again and `main` carries a
published extension version and a superseded entry, which the NEXT release then trips over.

## 5b · If the release fails — delete the tag, fix on the branch, tag again

**The release has failed** when `release.yml` is red, any workflow it starts is red, or the postflight is
not clean. It is retried **at the same version** (user ruling). `docs/RELEASING.md` has the why of each
step below.

**First, let every run at the tag finish or cancel it** — a deletion under a running `publish` or
`homebrew` races it.

```bash
# 1. The branch the release was cut from. Nothing deletes it before the release succeeds (§6).
git fetch origin --tags --prune
git checkout -B release/X.Y.Z origin/release/X.Y.Z

# 2. The release and its assets, if `publish` got that far.
gh release delete vX.Y.Z --yes

# 3. The tag, with the ruleset lifted for this ONE push.
ruleset="$(gh api repos/maxon-lang/maxon/rulesets --jq '.[] | select(.name == "Release tags") | .id')"
gh api -X PUT "repos/maxon-lang/maxon/rulesets/$ruleset" -f enforcement=disabled
git push origin --delete vX.Y.Z
gh api -X PUT "repos/maxon-lang/maxon/rulesets/$ruleset" -f enforcement=active
gh api "repos/maxon-lang/maxon/rulesets/$ruleset" --jq '.enforcement + " " + ([.rules[].type] | join(","))'
git tag -d vX.Y.Z
```

⛔ **READ THE LAST `gh api` LINE: it must print `active update,deletion,non_fast_forward`.** A protection
left off is worse than the release that needed it lifted.

⚠ **`-B`, never `-b`.** A local `release/X.Y.Z` is already there from the first attempt, so `-b` fails —
and every command after it then runs on `main`. ⛔ If `origin/release/X.Y.Z` is missing, someone deleted
it by hand: push it back from the tag BEFORE step 3, or nothing on origin reaches the release commits
once the tag is gone — `git push origin 'vX.Y.Z^{commit}:refs/heads/release/X.Y.Z'`.

**4. Make the changes on the branch.** A fix already on `main` is cherry-picked. A change to compiler
source still owes its case red then green before it goes on: the tagged run is a battery, not an
acceptance. Then, where they apply:

- ⛔ **The VS Code extension, if the failed attempt published it** — its `vscode-extension` run logs
  `Published maxon-lang.maxon-lsp-client v<version>`. That version is spent: bump PATCH, with a line in
  `vscode-extension/CHANGELOG.md`, or the retry publishes the compiler and then fails on the extension.
- **The changelog entry** gains what the fix changed, and its heading takes the retry's date.
- **The website material**: `announce.sh` refuses an existing post, so remove
  `website/src/content/docs/blog/maxon-X-Y-Z.md` and run §3 again.

**5. Then §1's rebuild, §3b's preflight, §4's tag and §5's watch**, exactly as the first time. §6 has not
run — it waits for a clean postflight — so `main` holds nothing of the failed attempt.

## 6 · Merge the tag back, then retire the branch

```bash
git fetch origin --tags --prune
git checkout main && git merge --no-ff vX.Y.Z && git push origin main
tip="$(git rev-parse --verify --quiet origin/release/X.Y.Z)"
if [ -z "$tip" ]; then
	echo "origin has no release/X.Y.Z to retire"
elif [ "$tip" = "$(git rev-parse 'vX.Y.Z^{commit}')" ]; then
	git push origin --delete release/X.Y.Z && git branch -d release/X.Y.Z
else
	echo "release/X.Y.Z has commits past vX.Y.Z: work nothing shipped"
fi
```

⛔ **THE BRANCH IS DELETED HERE AND NOWHERE EARLIER (user ruling).** A clean postflight is the first
point at which the release has succeeded; until then a failure is retried on the branch (§5b). If the
last arm prints, the branch holds work nothing shipped — leave it and tell the user.

⚠ The merge back is not optional — the changelog entry and the website material exist only at the
tag until it happens. The TAG is what is merged rather than the branch, because the tag is exactly what
shipped.

⚠ **Merge, never rebase**, and if `main` moved, build and suite-test the merge before pushing it. A
merge keeps the tag an ancestor of `main`; rebased copies would reappear in the next release's
`--commits-since`.

## What this skill may not do

- **Never create credentials, accounts or tokens**, and never ask the user to paste a secret. Secrets
  are set with `gh secret set`, which prompts and hides the input.
- **Never move a tag with a force-push**, and never delete one except by §5b: the release first, then the
  tag with the `Release tags` ruleset lifted for that single push. ⛔ **Never leave the ruleset disabled.**
- **Never delete `release/X.Y.Z` before the release has succeeded** — a clean postflight, §6.
- **Never delete a release that went out clean.** §5b is for a release that failed; one whose postflight
  was clean and is later found wanting is superseded by the next patch version.
