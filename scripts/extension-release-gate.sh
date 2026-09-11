#!/usr/bin/env bash
#
# Should this release publish the VS Code extension?
#
# ⭐⭐ **THREE ANSWERS, NOT TWO, AND THE THIRD IS THE ONE WORTH HAVING.** `vsce` REFUSES to republish a
# version that already exists, so an extension that changed without its version being bumped fails at
# the very last step of a release — after the compiler is published and the tag is public. The bump is
# hand-maintained and nothing anywhere reminded anyone to make it.
#
#   skip      nothing under vscode-extension/ changed in this range. Publish nothing. NOT a failure:
#             measured, 0 of the 21 commits since v0.1.0 touch it, so this is the ordinary case.
#   publish   it changed and the version was bumped.
#   (exit 1)  it changed and the version was NOT bumped. Named commits, named version.
#
# ⚠ THE EXTENSION'S VERSION IS ITS OWN, NOT THE COMPILER'S. Lockstep would republish a byte-identical
# extension under a new number at every compiler release and make its version history meaningless.
# It is a calendar version, YEAR.MONTH.PATCH (`2026.9.0`), so it is never read as a compiler version:
# a release takes the year and month it ships in, a second one that month bumps PATCH, and no part has
# a leading zero, which the Marketplace refuses.
#
# Usage:
#   scripts/extension-release-gate.sh            decide against the newest release tag
#   scripts/extension-release-gate.sh <tag>      decide against a given tag

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ExtensionPath="vscode-extension"
ExtensionManifest="$ExtensionPath/package.json"
ReleaseTagGlob='v[0-9]*'

previous="${1:-}"
if [ -z "$previous" ]; then
	previous="$(git tag --list "$ReleaseTagGlob" --sort=-v:refname | head -n1)"
fi

# ⚠ NO PRIOR RELEASE MEANS EVERYTHING IS NEW, so there is nothing to compare a bump against and the
# first release publishes whatever the manifest says.
if [ -z "$previous" ]; then
	echo "extension-release-gate: no prior release tag; the first release publishes the extension as-is" >&2
	echo "publish"
	exit 0
fi

# ⛔ THE CHANGELOG IS EXCLUDED, for `changelog.sh`'s reason: regenerating it is not a change to the
# extension, and counting it would demand a version bump for a file the marketplace renders rather
# than runs.
changed="$(git log --format='%h %s' "$previous..HEAD" -- "$ExtensionPath" ':(exclude)'"$ExtensionPath/CHANGELOG.md")"

if [ -z "$changed" ]; then
	echo "extension-release-gate: nothing under $ExtensionPath/ changed since $previous" >&2
	echo "skip"
	exit 0
fi

manifest_version() { git show "$1:$ExtensionManifest" 2>/dev/null | sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n1; }

was="$(manifest_version "$previous")"
now="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ExtensionManifest" | head -n1)"

[ -n "$now" ] || { echo "extension-release-gate: $ExtensionManifest declares no version" >&2; exit 1; }

if [ "$was" = "$now" ]; then
	echo "extension-release-gate: the extension changed since $previous but its version is still $now." >&2
	echo "  vsce refuses to republish an existing version, so this release would publish the compiler and" >&2
	echo "  then fail to publish the extension. Set \"version\" in $ExtensionManifest to this month's" >&2
	echo "  YEAR.MONTH.0, or bump PATCH if the extension already shipped this month." >&2
	echo "" >&2
	echo "  Commits touching $ExtensionPath/:" >&2
	printf '%s\n' "$changed" | sed 's/^/    /' >&2
	exit 1
fi

echo "extension-release-gate: $ExtensionPath/ changed since $previous and the version went $was -> $now" >&2
echo "publish"
