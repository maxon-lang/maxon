#!/usr/bin/env bash
#
# What changed in each release, derived from git history.
#
# ⭐⭐ **THE VERSION POLICY IS NOT HERE AND MUST NOT BE.** *"What number does this ref claim"* is
# `build.maxon`'s question alone, and `release.sh` reads the answer off the built binary. *"Which
# commits are new"* is a different question about TAG TOPOLOGY, and it is the only one this script
# answers. The number arrives as `--release=`; nothing here derives one.
#
# ⭐ **EVERY COMMIT IN THE RANGE APPEARS. Inclusion is OPT-OUT**, so a change cannot be missed by
# nobody having remembered to mention it. `docs/changelog-overrides.txt` is where an entry is
# reworded or dropped — after the fact, which is the whole point: a commit message that came out
# wrong is exactly what cannot be fixed by anything written at commit time.
#
# Usage:
#   scripts/changelog.sh --release=<X.Y.Z> [--write]
#   scripts/changelog.sh --released-only [--write]
#   scripts/changelog.sh --check
#   scripts/changelog.sh --section=<X.Y.Z>
#   scripts/changelog.sh --scope=extension --release=<X.Y.Z> [--write]
#
#   --release=       the version being cut. Never derived — the number arrives from the caller.
#   --released-only  every version that HAS a tag, and no pending section. ⭐ THIS IS THE COMMITTED
#                    STATE BETWEEN RELEASES: a `## 0.1.1` heading in a tree where v0.1.1 does not
#                    exist advertises a release nobody can download.
#   --write          write the file. WITHOUT IT NOTHING IS WRITTEN — the default IS the dry run,
#                    so there is no separate flag to forget.
#   --check          regenerate at HEAD's tag and byte-compare the committed file. This is what
#                    enforces "generated, never hand-edited"; without it the file is hand-edited
#                    within two releases.
#   --section=       print ONE release's body, READ OUT OF CHANGELOG.md. Touches no history, so it
#                    works in a shallow tagless checkout — which is what `release.sh --publish` has.
#   --scope=         `compiler` (default) or `extension`, which filters to vscode-extension/ and
#                    writes that directory's own CHANGELOG.md.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

OverridesFile="docs/changelog-overrides.txt"
CommitUrlBase="https://github.com/maxon-lang/maxon/commit"
ReleaseTagGlob='v[0-9]*'

CompilerChangelog="CHANGELOG.md"
ExtensionChangelog="vscode-extension/CHANGELOG.md"
ExtensionPath="vscode-extension"

mode=""
release=""
section=""
scope="compiler"
write=0

for arg in "$@"; do
	case "$arg" in
		--release=*)     mode="generate"; release="${arg#--release=}" ;;
		--released-only) mode="generate"; release="" ;;
		--section=*)     mode="section";  section="${arg#--section=}" ;;
		--check)         mode="check" ;;
		--scope=*)   scope="${arg#--scope=}" ;;
		--write)     write=1 ;;
		--help|-h)   sed -n '2,30p' "$0" | sed 's/^# \?//'; exit 0 ;;
		*) echo "changelog.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
	esac
done

case "$scope" in
	compiler|extension) ;;
	*) echo "changelog.sh: --scope must be 'compiler' or 'extension', not '$scope'" >&2; exit 2 ;;
esac

changelog_path() {
	case "$scope" in
		compiler)  printf '%s' "$CompilerChangelog" ;;
		extension) printf '%s' "$ExtensionChangelog" ;;
	esac
}

# ⭐⭐ **THE PATHS A SCOPE'S HISTORY IS READ THROUGH, AND THE CHANGELOG'S OWN FILES ARE NOT AMONG
# THEM.** A commit that only regenerates a changelog is not a change to the product, and excluding it
# by PATH rather than by a "skip the changelog commit" special case is what lets the tag sit on that
# commit: `--check` at a tag then renders the same range the pre-tag `--write` did, which is the
# reproducibility the check rests on. `git log` with a pathspec lists only commits touching it, so a
# commit confined to these files produces nothing here.
scope_pathspec() {
	case "$scope" in
		compiler)  printf '%s' ". :(exclude)$CompilerChangelog :(exclude)$OverridesFile :(exclude)$ExtensionChangelog" ;;
		extension) printf '%s' "$ExtensionPath :(exclude)$ExtensionChangelog" ;;
	esac
}

# ── the overrides file ────────────────────────────────────────────────────────────────────────────

# Parallel columns, indexed together. Bash has no nested maps and a subject is not a legal variable
# name, so the key column is scanned — a release has tens of overrides, not thousands.
override_keys=()
override_text=()
override_drop=()

# ⛔ A LEADING TOKEN OUTSIDE THE CLOSED SET IS A HARD ERROR, naming the line. A typo'd field name in
# a tolerant parser is an override that silently does nothing, and an override that silently does
# nothing is a correction you believe you made.
read_overrides() {
	[ -f "$OverridesFile" ] || return 0

	local lineno=0 key="" text="" drop="" line field value
	while IFS= read -r line || [ -n "$line" ]; do
		lineno=$((lineno + 1))

		case "$line" in
			'#'*) continue ;;
			'')
				flush_override "$key" "$text" "$drop" "$lineno"
				key=""; text=""; drop=""
				continue ;;
		esac

		# Split on the FIRST ': ' only, so a subject may contain one — and they routinely do
		# (`commit: docs: signing is provisioned`).
		case "$line" in
			*': '*) field="${line%%: *}"; value="${line#*: }" ;;
			*) echo "changelog.sh: $OverridesFile:$lineno: expected '<field>: <value>', got '$line'" >&2; exit 1 ;;
		esac

		case "$field" in
			commit) key="$value" ;;
			text)   text="$value" ;;
			drop)   drop="$value" ;;
			*) echo "changelog.sh: $OverridesFile:$lineno: unknown field '$field' — expected commit, text or drop" >&2; exit 1 ;;
		esac
	done < "$OverridesFile"

	flush_override "$key" "$text" "$drop" "$lineno"
}

flush_override() {
	local key="$1" text="$2" drop="$3" lineno="$4"
	[ -n "$key" ] || {
		if [ -n "$text$drop" ]; then
			echo "changelog.sh: $OverridesFile:$lineno: a record with no 'commit:' key" >&2
			exit 1
		fi
		return 0
	}

	# ⚠ THE REASON IS PART OF THE DROP, NOT A BARE FLAG. This is the one place the opt-out guarantee
	# is deliberately broken, and a dropped entry with no stated reason is indistinguishable from a
	# mistake six months later.
	if [ -n "$drop" ] && [ -n "$text" ]; then
		echo "changelog.sh: $OverridesFile: '$key' both drops and rewrites — pick one" >&2
		exit 1
	fi
	if [ -z "$drop" ] && [ -z "$text" ]; then
		echo "changelog.sh: $OverridesFile: '$key' says nothing — an override must carry 'text:' or 'drop:'" >&2
		exit 1
	fi

	override_keys+=("$key")
	override_text+=("$text")
	override_drop+=("$drop")
}

# ⛔ AN OVERRIDE MATCHING NO COMMIT IS AN ERROR, NOT A NO-OP — that is what makes opt-out safe. A key
# that has gone stale (a typo, or a rebase that mangled a subject) would otherwise become an inert
# line whose failure mode is quietly re-publishing the wording somebody corrected. Matching MORE than
# one is refused too: the correction would land on a commit nobody chose.
#
# A key matching a commit OUTSIDE the current range is inert and correct — the file accumulates
# across releases — which is why this is checked against all history and not against the range.
require_overrides_resolve() {
	local i key hits
	for i in "${!override_keys[@]}"; do
		key="${override_keys[$i]}"
		hits="$(git log --all --format='%H %s' | awk -v k="$key" 'substr($0, index($0, " ") + 1) == k {print $1}')"
		case "$(printf '%s' "$hits" | grep -c . || true)" in
			0) echo "changelog.sh: $OverridesFile: no commit has the subject '$key'" >&2; exit 1 ;;
			1) ;;
			*) echo "changelog.sh: $OverridesFile: '$key' is the subject of more than one commit:" >&2
			   printf '  %s\n' $hits >&2
			   exit 1 ;;
		esac
	done
}

override_index_for() {
	local subject="$1" i
	for i in "${!override_keys[@]}"; do
		if [ "${override_keys[$i]}" = "$subject" ]; then printf '%s' "$i"; return 0; fi
	done
	printf ''
}

# ── one release's entries ─────────────────────────────────────────────────────────────────────────

# The newest release tag strictly below <upper>, or empty when <upper> is the first release. `^`
# excludes a tag pointing at <upper> itself, which is exactly the case when regenerating at a tag.
previous_tag() {
	git tag --list "$ReleaseTagGlob" --merged "$1^" --sort=-v:refname 2>/dev/null | head -n1
}

# ⛔ REFUSE TWO ROOTS. A repository with two root commits has no single first release, so the lower
# bound of the first section is a question rather than an answer.
root_commit() {
	local roots
	roots="$(git rev-list --max-parents=0 HEAD)"
	if [ "$(printf '%s\n' "$roots" | grep -c .)" -ne 1 ]; then
		echo "changelog.sh: this repository has more than one root commit, so the first release has no single starting point:" >&2
		printf '  %s\n' $roots >&2
		exit 1
	fi
	printf '%s' "$roots"
}

# ⚠ THE DATE IS THE NEWEST COMMIT'S COMMITTER DATE, NEVER `date`. Reproducibility is what makes
# `--check` possible at all: regenerating the same range must give the same bytes tomorrow.
range_date() {
	git log -1 --format='%cd' --date=short "$2" -- $(scope_pathspec)
}

# ⛔ A MERGE ON THIS BRANCH MEANS SOMETHING ELSE WENT WRONG. `--no-merges` would drop it silently,
# which is the opposite of what a changelog is for; this repository rebases and has none.
require_no_merges() {
	local merges
	merges="$(git log --merges --format='%h %s' "$1..$2" -- $(scope_pathspec))"
	if [ -n "$merges" ]; then
		echo "changelog.sh: $1..$2 contains merge commits, which this history is not supposed to have:" >&2
		printf '%s\n' "$merges" | sed 's/^/  /' >&2
		exit 1
	fi
}

# One release's bullets, newest first. Sections were considered and rejected: this history has no
# `feat:`/`fix:` types, and the area prefixes it does carry are internal component names.
render_entries() {
	local lower="$1" upper="$2" sha subject idx text

	require_no_merges "$lower" "$upper"

	while IFS='|' read -r sha subject; do
		[ -n "$sha" ] || continue

		idx="$(override_index_for "$subject")"
		if [ -n "$idx" ]; then
			[ -z "${override_drop[$idx]}" ] || continue
			text="${override_text[$idx]}"
		else
			# ⭐ VERBATIM. These subjects are already changelog lines — present-tense statements of
			# the new behaviour — and a transformed entry is a CLAIM ABOUT a commit where a verbatim
			# one IS the commit. Rewrites belong in the overrides file, where they are reviewable.
			text="$subject"
		fi

		printf -- '- %s ([%s](%s/%s))\n' "$text" "${sha:0:7}" "$CommitUrlBase" "$sha"
	done < <(git log --format='%H|%s' "$lower..$upper" -- $(scope_pathspec))
}

count_commits() {
	git log --format='%H' "$1..$2" -- $(scope_pathspec) | grep -c . || true
}

# ── the whole file ────────────────────────────────────────────────────────────────────────────────

# ⭐ REGENERATED WHOLE, EVERY TIME. Appending is how a generated file acquires a hand-edited past,
# and only whole regeneration makes `--check` — the thing that actually keeps it generated — possible.
render_file() {
	local upper_release="$1" tags tag lower prior

	echo "# Changelog"
	echo
	echo "Generated from git history by \`scripts/changelog.sh\`. **Do not edit by hand** — a"
	echo "correction goes in \`$OverridesFile\`, and \`--check\` fails a release whose committed"
	echo "file does not match what history says."

	# The release being cut sits on top: its commits are those after the newest tag.
	if [ -n "$upper_release" ]; then
		lower="$(previous_tag HEAD)"
		[ -n "$lower" ] || lower="$(root_commit)"
		render_section "$upper_release" "$lower" HEAD
	fi

	# Then every tag already cut, newest first.
	tags="$(git tag --list "$ReleaseTagGlob" --sort=-v:refname)"
	for tag in $tags; do
		prior="$(previous_tag "$tag")"
		[ -n "$prior" ] || prior="$(root_commit)"
		render_section "${tag#v}" "$prior" "$tag"
	done
}

# ⛔ **A SECTION WITH NO ENTRIES IS NOT WRITTEN AT ALL.** Under `--scope=extension` this is the common
# case rather than an edge one — most compiler releases change nothing under `vscode-extension/` — and
# a bare `## 0.1.1` with nothing beneath it reads, on the marketplace's Changelog tab, as a release
# that shipped and did nothing. The heading is therefore rendered only once its entries exist.
render_section() {
	local heading="$1" lower="$2" upper="$3" entries

	entries="$(render_entries "$lower" "$upper")"
	[ -n "$entries" ] || return 0

	echo
	echo "## $heading — $(range_date "$lower" "$upper")"
	echo
	printf '%s\n' "$entries"
}

# ── modes ─────────────────────────────────────────────────────────────────────────────────────────

case "$mode" in
	generate)
		read_overrides
		require_overrides_resolve
		lower="$(previous_tag HEAD)"; [ -n "$lower" ] || lower="$(root_commit)"
		echo "changelog.sh: $(count_commits "$lower" HEAD) commit(s) since ${lower#v} in scope '$scope'" >&2

		out="$(changelog_path)"
		if [ "$write" -eq 1 ]; then
			render_file "$release" > "$out"
			echo "changelog.sh: wrote $out" >&2
		else
			render_file "$release"
			echo "changelog.sh: nothing written (pass --write)" >&2
		fi
		;;

	check)
		read_overrides
		require_overrides_resolve
		tag="$(git tag --list "$ReleaseTagGlob" --points-at HEAD | head -n1)"
		[ -n "$tag" ] || { echo "changelog.sh: --check needs HEAD to be a release tag" >&2; exit 1; }

		out="$(changelog_path)"
		[ -f "$out" ] || { echo "changelog.sh: $out does not exist" >&2; exit 1; }

		# ⚠ Regenerated WITHOUT a pending release: at a tag, every section is already a tag's.
		if diff -u "$out" <(render_file "") >/dev/null; then
			echo "changelog.sh: $out matches history at $tag"
		else
			echo "changelog.sh: $out does not match what history says at $tag:" >&2
			diff -u "$out" <(render_file "") >&2 || true
			echo "changelog.sh: regenerate it with --release=${tag#v} --write" >&2
			exit 1
		fi
		;;

	section)
		# ⭐ READS THE COMMITTED FILE, NOT HISTORY. `release.sh --publish` runs in a shallow, tagless
		# checkout, and the notes it ships are then byte-for-byte the ones reviewed before the tag —
		# so CI cannot compute a different answer from the one a person read.
		out="$(changelog_path)"
		[ -f "$out" ] || { echo "changelog.sh: $out does not exist" >&2; exit 1; }

		body="$(awk -v want="## $section " '
			index($0, want) == 1 { inside = 1; next }
			inside && /^## / { exit }
			inside { print }
		' "$out")"

		# ⛔ A RELEASE WHOSE CHANGELOG WAS NEVER REGENERATED MUST NOT PUBLISH SILENTLY WITHOUT NOTES.
		if [ -z "$(printf '%s' "$body" | grep -c . || true)" ] || [ "$(printf '%s' "$body" | grep -c . || true)" -eq 0 ]; then
			echo "changelog.sh: $out has no section for $section — regenerate it before publishing" >&2
			exit 1
		fi

		printf '%s\n' "$body" | sed -e '/./,$!d'
		;;

	*)
		echo "changelog.sh: pass --release=<X.Y.Z>, --check or --section=<X.Y.Z> (see --help)" >&2
		exit 2 ;;
esac
