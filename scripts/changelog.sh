#!/usr/bin/env bash
#
# Read one release's entry out of `CHANGELOG.md`, or list the commits behind the next one.
#
# ⭐⭐ **`CHANGELOG.md` IS WRITTEN BY HAND, AND THIS SCRIPT DOES NOT WRITE IT.** A changelog is for
# people installing a compiler, and a commit subject is written for people changing one. MEASURED on
# `v0.1.0..HEAD`: 21 commits, of which about six mean anything to a reader — the rest are CI, spec
# goldens, docs and release tooling. Generating the entry and then correcting it would have meant
# rewriting seventeen of twenty-one lines, which is hand-writing with extra ceremony.
#
# ⭐ **WHAT THIS EXISTS FOR IS THAT ONE FILE FEEDS FOUR PLACES.** The GitHub release notes
# (`release.sh`), the maxon.dev post and the site's changelog page (`announce.sh`) all read a section
# out of it, so the words exist once and cannot come to disagree about what shipped.
#
# Usage:
#   scripts/changelog.sh --section=<X.Y.Z>        print that release's entry
#   scripts/changelog.sh --commits-since [<tag>]  list the commits since a tag, as reference
#
#   --section=        the body under `## <X.Y.Z>`, without the heading. ⛔ REFUSES a version the file
#                     has no heading for — a release nobody wrote an entry for must not publish
#                     silently without notes.
#   --commits-since   what landed since the last release, to consult while writing the next entry.
#                     Writes nothing, and is the only thing here that reads git history at all.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ChangelogFile="CHANGELOG.md"
ReleaseTagGlob='v[0-9]*'

mode=""
section=""
since=""

for arg in "$@"; do
	case "$arg" in
		--section=*)     mode="section"; section="${arg#--section=}" ;;
		--commits-since) mode="commits" ;;
		--help|-h)       sed -n '2,23p' "$0" | sed 's/^# \?//'; exit 0 ;;
		-*) echo "changelog.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
		*)  since="$arg" ;;
	esac
done

case "$mode" in
	section)
		[ -f "$ChangelogFile" ] || { echo "changelog.sh: $ChangelogFile does not exist" >&2; exit 1; }

		# Everything under `## <version>` up to the next `## `. The heading itself is dropped: each
		# caller frames it its own way — a `## What's new` in the release notes, a `## What changed`
		# in the blog post.
		body="$(awk -v want="## $section " '
			index($0, want) == 1 { inside = 1; next }
			inside && /^## / { exit }
			inside { print }
		' "$ChangelogFile")"

		# ⛔ A RELEASE WITH NO ENTRY MUST NOT PUBLISH. `release.yml`'s `guard` asks this before any
		# runner starts; `release.sh --publish` asks it again at the last moment. An authored file's
		# failure mode is that nobody wrote it, and this is the check for exactly that.
		if [ "$(printf '%s' "$body" | grep -c . || true)" -eq 0 ]; then
			echo "changelog.sh: $ChangelogFile has no entry for $section — write one before releasing" >&2
			echo "  \`scripts/changelog.sh --commits-since\` lists what has landed since the last release." >&2
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
			echo "changelog.sh: no release tag yet — every commit is in the first release" >&2
			git log --format='  %h  %s'
			exit 0
		fi

		count="$(git log --format='%H' "$since..HEAD" | grep -c . || true)"
		echo "changelog.sh: $count commit(s) since $since" >&2
		echo "" >&2
		git log --format='  %h  %s' "$since..HEAD"
		;;

	*)
		echo "changelog.sh: pass --section=<X.Y.Z> or --commits-since (see --help)" >&2
		exit 2 ;;
esac
