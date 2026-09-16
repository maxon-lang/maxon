#!/bin/sh
#
# Every question a release must answer BEFORE the tag is pushed. It reads and reports; it writes,
# commits and publishes nothing.
#
# ⭐⭐ **THESE QUESTIONS ARE ALL ASKED AGAIN AT THE TAG, WHERE THEY CAN NO LONGER BE ANSWERED.**
# `release.yml`'s guard refuses a tag with no changelog entry or a changed extension whose version did
# not move, and `website.yml` fails its build on a page that has drifted from `docs/` — by which point
# the tag exists, is immutable, and four runners are building. Every one of them is a one-line edit
# while it is still asked here.
#
# ⛔ **THE WHOLE LIST RUNS AND EVERY ANSWER IS PRINTED.** Stopping at the first failure turns one
# release into one fix per attempt.
#
# Usage:
#   scripts/release-preflight.sh <X.Y.Z>
#
#   <X.Y.Z>   the version about to be tagged, with or without a leading `v`
#
# Non-zero when any check FAILED. A SKIPPED check tested nothing, is not a pass, and is named again in
# the summary.

set -eu

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
# The libraries are resolved against the repository root, which shellcheck has no way to know.
# shellcheck disable=SC1091
. scripts/lib/release-checks.sh
# shellcheck disable=SC1091
. scripts/lib/host-binaries.sh

WebsiteDir="website"
SyncDocsScript="scripts/sync-docs.mjs"
InstallShPath="website/public/install.sh"
CiWorkflow="ci.yml"

version=""

for arg in "$@"; do
	case "$arg" in
		-h|--help) sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		-*)        echo "release-preflight.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
		*)         version="$arg" ;;
	esac
done

[ -n "$version" ] || { echo "release-preflight.sh: pass the version about to be tagged, e.g. scripts/release-preflight.sh 0.2.2" >&2; exit 2; }
version="$(check_release_version "$version" release-preflight.sh)" || exit 2

# Captured stderr lands here rather than in the tree, which the first check reads.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT INT TERM

head_commit="$(git rev-parse HEAD)"
printf 'release-preflight.sh: %s at %s\n\n' "$version" "$(git rev-parse --short HEAD)"

# ⛔ EVERYTHING DOWNSTREAM READS THE REPOSITORY AT THE TAG. An uncommitted file is one the release does
# not carry, and every check below reads the working tree — so a dirty tree makes each of them a
# statement about something that is not being released.
if [ -n "$(git status --porcelain)" ]; then
	check_fail worktree-clean "uncommitted changes — the tag carries the committed tree, not this one"
	git status --porcelain > "$work/dirty"
	check_detail "$work/dirty"
else
	check_pass worktree-clean "nothing uncommitted"
fi

# ⛔ **A DRIFTED COPY OF `docs/` FAILS `website.yml`'s BUILD, ITS DEPLOY IS GATED `needs: build` AND
# THEREFORE SKIPS, AND THE RUN STILL REPORTS SUCCESS.** maxon.dev then stays on the previous release
# while the published notes link to a post that does not exist.
if [ ! -f "$WebsiteDir/$SyncDocsScript" ]; then
	check_fail website-docs-sync "$WebsiteDir/$SyncDocsScript is missing"
elif ! command -v node >/dev/null 2>&1; then
	check_fail website-docs-sync "node is not on PATH, so the check that gates the website build cannot run here"
elif ( cd "$WebsiteDir" && node "$SyncDocsScript" --check ) > "$work/sync.out" 2>&1; then
	check_pass website-docs-sync "the pages copied from docs/ still match"
else
	check_fail website-docs-sync "a page copied from docs/ has drifted — website.yml's build fails on this and its deploy then skips"
	check_detail "$work/sync.out"
fi

# The gate answers `skip` or `publish` on stdout and explains itself on stderr; exit 1 is a changed
# extension whose version did not move, which `vsce` refuses at the very last step of a release.
if extension_decision="$(scripts/extension-release-gate.sh 2>"$work/extension.err")"; then
	check_pass extension-gate "the VS Code extension is a '$extension_decision' for this release"
else
	check_fail extension-gate "the extension changed and its version did not move"
	check_detail "$work/extension.err"
fi

# ⛔ A RELEASE WITH NO ENTRY DOES NOT SHIP: the maxon.dev post and the site's changelog page are both
# rendered from this section, and the release notes link to them.
if scripts/changelog.sh --section="$version" >/dev/null 2>"$work/changelog.err"; then
	check_pass changelog-entry "CHANGELOG.md has an entry for $version"
else
	check_fail changelog-entry "CHANGELOG.md has no entry for $version"
	check_detail "$work/changelog.err"
fi

# ⛔ `maxon upgrade` RUNS THIS SCRIPT FROM EVERY SHIPPED COMPILER, so `install-script.yml` lints it on
# every platform at the tag. Absent shellcheck the answer here is unknown, never clean.
if ! command -v shellcheck >/dev/null 2>&1; then
	check_skip install-sh-shellcheck "shellcheck is not on PATH — install-script.yml runs it at the tag"
elif shellcheck --shell=sh "$InstallShPath" > "$work/shellcheck.out" 2>&1; then
	check_pass install-sh-shellcheck "$InstallShPath is clean"
else
	check_fail install-sh-shellcheck "shellcheck refuses $InstallShPath"
	check_detail "$work/shellcheck.out"
fi

gh_unavailable="$(check_gh_reason)"

# ⚠ NOT EVERY COMMIT HAS A RUN, AND THAT IS NOT A VERDICT. `ci.yml` triggers on `main` and on pull
# requests but not on `release/*`, and its path filter skips a commit that touches nothing it reads —
# so a changelog commit on a release branch legitimately has none, and the honest answer is "unknown".
if [ -n "$gh_unavailable" ]; then
	check_skip ci-green "$gh_unavailable"
elif ! ci_run="$(gh run list --commit "$head_commit" --workflow="$CiWorkflow" --limit 1 --json status,conclusion,url --jq '.[] | "\(.status) \(.conclusion) \(.url)"' 2>"$work/ci.err")"; then
	check_skip ci-green "gh could not list $CiWorkflow runs for this commit"
	check_detail "$work/ci.err"
elif [ -z "$ci_run" ]; then
	check_skip ci-green "no $CiWorkflow run at this commit — it runs on main and pull requests, not on release/*"
else
	ci_status="$(printf '%s' "$ci_run" | cut -d' ' -f1)"
	ci_conclusion="$(printf '%s' "$ci_run" | cut -d' ' -f2)"
	ci_url="$(printf '%s' "$ci_run" | cut -d' ' -f3)"

	if [ "$ci_status" != completed ]; then
		check_skip ci-green "$CiWorkflow is still $ci_status — $ci_url"
	elif [ "$ci_conclusion" = success ]; then
		check_pass ci-green "$CiWorkflow is green at this commit"
	else
		check_fail ci-green "$CiWorkflow is $ci_conclusion at this commit — $ci_url"
	fi
fi

# ⛔ THE VERSION IS DERIVED FROM THE REF AT BUILD TIME AND LIVES NOWHERE ELSE, so the artifact is the
# only thing that can be asked. `release.sh --publish` refuses a tag the binary disagrees with, and
# that refusal arrives after four runners have each built and suite-tested the compiler.
maxon="$(maxon_compiler_path .)"

if [ ! -x "$maxon" ]; then
	check_fail compiler-version "no compiler at $maxon — build one on this branch before releasing"
elif ! reported="$("$maxon" version 2>"$work/version.err")"; then
	check_fail compiler-version "$maxon could not report its version"
	check_detail "$work/version.err"
else
	case "$reported" in
		*"maxon $version "*)
			check_pass compiler-version "$reported" ;;
		*)
			check_fail compiler-version "the compiler reports '$reported'"
			check_note "The number comes from the ref AT BUILD TIME: a vX.Y.Z tag or a release/X.Y.Z branch"
			check_note "gives it and anything else gives 'dev', so a branch cut after the last build still"
			check_note "reports the old number. Rebuild on the release branch, then run this again." ;;
	esac
fi

if ! check_summary release-preflight.sh; then
	exit 1
fi
