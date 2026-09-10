#!/usr/bin/env bash
#
# Is this tree ready to tag?
#
# ⭐⭐ **IT ASKS EVERY QUESTION THE PIPELINE WILL ASK, BEFORE THE TAG IS PUBLIC.** A tag is the one
# irreversible step of a release: `release.yml` fans out over four architectures, and a refusal
# twenty minutes in leaves a public tag naming a release that never shipped. Every check here is one
# a workflow already performs — this just performs them where the answer is still cheap.
#
# ⭐ **IT DELEGATES RATHER THAN RE-DECIDES.** `changelog.sh`, `extension-release-gate.sh` and
# `self-compiles-needed.sh` each own a question and answer it in one place; this runs them and
# reports. A second implementation of any of these answers is a second thing to keep in step, which
# is the failure mode this repository has spent real time removing.
#
# ⛔ **IT IS NOT THE PROCESS.** `docs/RELEASING.md` is: cut the branch, write the changelog, write the
# website's material, tag the finished tip, merge back, lock. This says whether the tree is in a fit
# state for the tag step, and nothing about the order of the others.
#
# Usage:
#   scripts/release-preflight.sh <X.Y.Z>
#
# Exits 0 when every check passes, 1 otherwise. Each line is PASS, FAIL or NOTE.

set -uo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

version="${1:-}"
[ -n "$version" ] || { echo "release-preflight: pass the version being cut, e.g. scripts/release-preflight.sh 0.1.1" >&2; exit 2; }
version="${version#v}"

failures=0

pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; failures=$((failures + 1)); }
note() { printf '  \033[33mNOTE\033[0m  %s\n' "$1"; }

echo "release-preflight: checking this tree against $version"
echo

# ── the tree ──────────────────────────────────────────────────────────────────────────────────────

if [ -z "$(git status --porcelain)" ]; then
	pass "the working tree is clean"
else
	fail "the working tree has uncommitted changes — the tag must name a committed state"
	git status --short | sed 's/^/          /'
fi

branch="$(git branch --show-current)"
case "$branch" in
	"release/$version") pass "on release/$version" ;;
	main) note "on main — a release is normally cut on release/$version, where a build already reports $version" ;;
	*) fail "on '$branch', which is neither main nor release/$version" ;;
esac

# ⚠ ONLY MEANINGFUL AGAINST A FETCHED REMOTE, and fetching is a network call this script does not
# make on the caller's behalf. It reports what the local refs say and names the staleness.
if git rev-parse --verify --quiet "@{upstream}" >/dev/null 2>&1; then
	behind="$(git rev-list --count "HEAD..@{upstream}" 2>/dev/null || echo 0)"
	if [ "$behind" = "0" ]; then
		pass "not behind the upstream branch (as of the last fetch)"
	else
		fail "$behind commit(s) behind the upstream branch — rebase before tagging"
	fi
else
	note "this branch has no upstream yet, so there is nothing to be behind"
fi

# ── the changelog ─────────────────────────────────────────────────────────────────────────────────

# ⛔ THE SAME CALL `guard` MAKES. A release with no entry publishes notes that say nothing, and the
# blog post and the website's changelog page are read out of the same section.
if scripts/changelog.sh --section="$version" >/dev/null 2>&1; then
	pass "CHANGELOG.md has an entry for $version"
else
	fail "CHANGELOG.md has no entry for $version — write one (scripts/changelog.sh --commits-since)"
fi

# ── the compiler ──────────────────────────────────────────────────────────────────────────────────

maxon="$(maxon_compiler_path .)"
if [ -x "$maxon" ]; then
	reported="$("$maxon" version | awk '{print $2}')"
	# ⛔ THE CHECK `release.sh --publish` MAKES, ASKED EARLY. It compares the tag against what the
	# BINARY says, and a binary built before the branch was cut still says `dev` — so this is the
	# check that catches "I forgot to rebuild after cutting the branch".
	if [ "$reported" = "$version" ]; then
		pass "the built compiler reports $version"
	else
		fail "the built compiler reports '$reported', not '$version' — rebuild on this branch (release.sh --publish refuses this)"
	fi

	needed="$(scripts/self-compiles-needed.sh 2>/dev/null)"
	if [ "$needed" = "once" ]; then
		pass "the slot binary is current with the emitted runtime"
	else
		note "the emitted runtime has changed since the slot was built — build twice (scripts/self-compiles-needed.sh says why)"
	fi
else
	fail "no compiler at $maxon — build one before tagging"
fi

# ── the extension ─────────────────────────────────────────────────────────────────────────────────

ext="$(scripts/extension-release-gate.sh 2>/dev/null)"
case "$ext" in
	skip)    pass "the extension did not change, so this release publishes none" ;;
	publish) pass "the extension changed and its version was bumped" ;;
	*)       fail "the extension changed without a version bump — vsce would refuse it AFTER the tag is public"
	         scripts/extension-release-gate.sh 2>&1 >/dev/null | sed 's/^/          /' ;;
esac

# ── what the downstream jobs need ─────────────────────────────────────────────────────────────────

# ⚠ NAMES ONLY, NEVER VALUES — and a missing one is a NOTE rather than a failure, because each job
# already skips with a warning when its credential is absent. What this buys is finding out before
# the tag rather than from a workflow log afterwards.
if command -v gh >/dev/null 2>&1; then
	have="$( { gh secret list --json name --jq '.[].name' 2>/dev/null; gh api orgs/maxon-lang/actions/secrets --jq '.secrets[].name' 2>/dev/null; } | sort -u)"
	for s in TAP_TOKEN VSCE_PAT OVSX_PAT CLOUDFLARE_API_TOKEN CLOUDFLARE_ACCOUNT_ID; do
		if printf '%s\n' "$have" | grep -qx "$s"; then
			pass "secret $s is set"
		else
			note "secret $s is absent — the job that needs it will skip with a warning"
		fi
	done

	if [ -n "$(gh api repos/maxon-lang/maxon/actions/variables --jq '.variables[] | select(.name=="AZURE_SIGNING_ENDPOINT") | .name' 2>/dev/null)" ]; then
		pass "the Azure signing variables are set, so the MSI will be signed"
	else
		note "AZURE_SIGNING_ENDPOINT is absent — the MSI ships unsigned and the notes say so"
	fi
else
	note "no gh CLI here, so the repository's secrets and variables were not checked"
fi

echo
if [ "$failures" -eq 0 ]; then
	echo "release-preflight: ready to tag $version"
	exit 0
fi
echo "release-preflight: $failures check(s) failed — do not tag yet" >&2
exit 1
