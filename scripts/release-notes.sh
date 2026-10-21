#!/usr/bin/env bash
#
# Read one release's entry out of `RELEASE_NOTES.md`, or list the commits behind the next one.
#
# ⭐⭐ **`RELEASE_NOTES.md` IS WRITTEN BY HAND, AND THIS SCRIPT DOES NOT WRITE IT.** Release notes are for
# people installing a compiler, and a commit subject is written for people changing one. MEASURED on
# `v0.1.0..HEAD`: 21 commits, of which about six mean anything to a reader — the rest are CI, spec
# pins, docs and release tooling. Generating the entry and then correcting it would have meant
# rewriting seventeen of twenty-one lines, which is hand-writing with extra ceremony.
#
# ⭐ **WHAT THIS EXISTS FOR IS THAT ONE FILE FEEDS THE SITE AND THE COMPILER.** The maxon.dev post and the
# site's release notes page (`announce.sh`) both read a section out of it, the GitHub release notes
# (`release.sh`) link to those rather than copying it, and `website/scripts/sync-docs.mjs` compiles its
# `### Breaking changes` entries into the driver, so the words exist once and cannot come to disagree
# about what shipped.
#
# Usage:
#   scripts/release-notes.sh --section=<X.Y.Z>        print that release's entry
#   scripts/release-notes.sh --commits-since [<tag>]  list the commits since a tag, as reference
#
#   --section=        the body under `## <X.Y.Z>`, without the heading. ⛔ REFUSES a version the file
#                     has no heading for — a release nobody wrote an entry for must not publish
#                     silently without notes — and a file that still holds `## Unreleased`, which
#                     cutting the release renames to the version.
#   --commits-since   what landed since the last release, to consult while writing the next entry.
#                     Writes nothing, and is the only thing here that reads git history at all.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ReleaseNotesFile="RELEASE_NOTES.md"
UnreleasedHeading="## Unreleased"
ReleaseTagGlob='v[0-9]*'

mode=""
section=""
since=""

for arg in "$@"; do
	case "$arg" in
		--section=*)     mode="section"; section="${arg#--section=}" ;;
		--commits-since) mode="commits" ;;
		--help|-h)       sed -n '2,26p' "$0" | sed 's/^# \?//'; exit 0 ;;
		-*) echo "release-notes.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
		*)  since="$arg" ;;
	esac
done

case "$mode" in
	section)
		[ -f "$ReleaseNotesFile" ] || { echo "release-notes.sh: $ReleaseNotesFile does not exist" >&2; exit 1; }

		# ⛔ A RELEASE IS CUT FROM A FILE WITH NO `## Unreleased`. The section holds the breaking changes of
		# the release being prepared; left under that name it would ship as a section no version owns and
		# the compiler would report it against every release.
		if grep -qx -- "$UnreleasedHeading" "$ReleaseNotesFile"; then
			echo "release-notes.sh: $ReleaseNotesFile still has '$UnreleasedHeading' — rename it to '## $section — YYYY-MM-DD' before releasing" >&2
			exit 1
		fi

		# Everything under `## <version>` up to the next `## `. The heading itself is dropped: each
		# caller frames it its own way, such as a `## What changed` in the blog post.
		body="$(awk -v want="## $section " '
			index($0, want) == 1 { inside = 1; next }
			inside && /^## / { exit }
			inside { print }
		' "$ReleaseNotesFile")"

		# ⛔ A RELEASE WITH NO ENTRY MUST NOT PUBLISH. `release.yml`'s `guard` asks this before any
		# runner starts; `release.sh --publish` asks it again at the last moment. An authored file's
		# failure mode is that nobody wrote it, and this is the check for exactly that.
		if [ "$(printf '%s' "$body" | grep -c . || true)" -eq 0 ]; then
			echo "release-notes.sh: $ReleaseNotesFile has no entry for $section — write one before releasing" >&2
			echo "  \`scripts/release-notes.sh --commits-since\` lists what has landed since the last release." >&2
			exit 1
		fi

		# Trim the blank lines the split leaves at each end, so a caller can place it without
		# guessing how many it got.
		printf '%s\n' "$body" | sed -e '/./,$!d' | sed -e ':a' -e '/^\s*$/{$d;N;ba' -e '}'
		;;

	commits)
		# ⚠ REFERENCE MATERIAL, NOT A DRAFT. These are subjects written for someone changing the
		# compiler; the entry is written for someone installing it. Reading one to write the other is
		# the point — pasting one into the other is what this design rejects.
		[ -n "$since" ] || since="$(git tag --list "$ReleaseTagGlob" --sort=-v:refname | head -n1)"

		if [ -z "$since" ]; then
			echo "release-notes.sh: no release tag yet — every commit is in the first release" >&2
			git log --format='  %h  %s'
			exit 0
		fi

		count="$(git log --format='%H' "$since..HEAD" | grep -c . || true)"
		echo "release-notes.sh: $count commit(s) since $since" >&2
		echo "" >&2
		git log --format='  %h  %s' "$since..HEAD"
		;;

	*)
		echo "release-notes.sh: pass --section=<X.Y.Z> or --commits-since (see --help)" >&2
		exit 2 ;;
esac
