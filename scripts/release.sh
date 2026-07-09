#!/usr/bin/env bash
#
# release.sh — build and publish a GitHub release of Maxon.
#
# Produces one binary archive per supported target (see the TARGETS list near
# the top of the script — x64-windows only for now, more as they're supported),
# plus one source archive:
#   - maxon-<version>-<target>.{zip,tar.gz}   per target, each containing:
#        maxon[.exe]              the self-hosted compiler
#        stdlib/                  the standard library (resolved at runtime)
#        runtime.std / runtime_wasm.std
#        README.md, LICENSE-MIT, LICENSE-APACHE, INSTALL.md
#   - maxon-<version>-source.tar.gz            git archive of the tagged commit,
#        excluding the vendored wasm toolchain under vendor/.
#
# The build is done fresh from the current checkout so the artifacts always
# match the tagged source:
#   1. dotnet build maxon-sharp                         -> the C# bootstrap
#   2. bin/maxon build maxon-selfhosted --target=<t>    -> per-target compiler
#
# Then it creates an annotated git tag, pushes it, and runs `gh release create`
# to upload every archive.
#
# Usage:
#   scripts/release.sh [<version>] [options]
#
# Version argument (optional — defaults to 'auto'):
#   vX.Y.Z                 An explicit version, e.g. v0.2.0 (leading 'v' added
#                          if omitted). Used verbatim.
#   auto                   Derive the next version from Conventional Commits
#                          since the latest tag (feat -> minor, fix/etc ->
#                          patch, ! or BREAKING CHANGE -> major). This is the
#                          default when no version is given.
#   patch | minor | major  Force that bump level off the latest stable tag,
#                          ignoring commit analysis.
#
# On a 0.x line, a 'major' bump is treated as 'minor' per semver's pre-1.0 rule.
#
# Options:
#   --rc                   Cut a release candidate of the resolved version, e.g.
#                          v0.2.0-rc.1. Re-running bumps to -rc.2, -rc.3, ....
#                          Implies --prerelease. To finalize, run again with the
#                          explicit stable version (e.g. v0.2.0).
#   --yes, -y              Skip the interactive confirmation of a derived or
#                          bumped version. Required when stdin is not a TTY.
#   --blog / --no-blog     After a successful GitHub release, write a changelog
#                          announcement to the maxon.dev blog and push it (which
#                          triggers a Cloudflare deploy). On by default; skipped
#                          automatically for --dry-run, --draft, and rc builds.
#   --web-dir <path>       Path to the maxon.dev site checkout (default:
#                          ../maxon-web, or $MAXON_WEB_DIR).
#   --notes <text>         Release notes body. Defaults to auto-generated notes.
#   --notes-file <path>    Read release notes from a file.
#   --prerelease           Mark the GitHub release as a prerelease.
#   --draft                Create the GitHub release as a draft.
#   --skip-build           Package the existing self-hosted binary without
#                          rebuilding (use only if you just built it).
#   --skip-tests           Skip running the spec-test suite before packaging.
#   --dry-run              Build and package, but do not tag, push, or publish.
#                          Prints the archive path and the gh command instead.
#   -h, --help             Show this help and exit.
#
# Examples:
#   scripts/release.sh                 # auto-derive the next version, confirm
#   scripts/release.sh --rc            # auto-derive, cut it as -rc.1
#   scripts/release.sh minor --yes     # force a minor bump, no prompt
#   scripts/release.sh v1.0.0          # explicit version
#   scripts/release.sh v0.2.0          # finalize the v0.2.0-rc.N line
#
# Requires: dotnet, git, gh (authenticated), and a zip tool (bsdtar/7z/zip
# on Windows; zip elsewhere). Run from anywhere inside the repo.

set -euo pipefail

# --- helpers ---------------------------------------------------------------

die() { echo "error: $*" >&2; exit 1; }
info() { echo ">>> $*"; }

usage() {
	# Print the leading comment block (everything up to the first blank line
	# after the shebang) as help text.
	sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'
}

# --- version helpers -------------------------------------------------------

# Match a stable semver tag: vMAJOR.MINOR.PATCH (no pre-release suffix).
SEMVER_RE='^v([0-9]+)\.([0-9]+)\.([0-9]+)$'
# Match a release-candidate tag: vMAJOR.MINOR.PATCH-rc.N
RC_RE='^v([0-9]+)\.([0-9]+)\.([0-9]+)-rc\.([0-9]+)$'

# The latest STABLE release tag (highest semver, pre-releases excluded), or
# empty if none. Uses version sort so v0.10.0 > v0.9.0. The `|| true` keeps a
# no-match grep (zero tags) from failing the pipeline under `set -o pipefail`.
latest_stable_tag() {
	git tag --list 'v[0-9]*' \
		| { grep -E "$SEMVER_RE" || true; } \
		| sort -V \
		| tail -n1
}

# The latest tag of ANY kind (stable or rc), highest first, or empty.
latest_any_tag() {
	git tag --list 'v[0-9]*' \
		| { grep -E "$SEMVER_RE|$RC_RE" || true; } \
		| sort -V \
		| tail -n1
}

# Given a base ref, echo the Conventional-Commit-implied bump level across the
# commits in `<base>..HEAD`: "major", "minor", or "patch". With no base (first
# release) or no commits, echoes "minor" — a sensible default for a 0.x line.
#   - a `!` after type/scope, or a `BREAKING CHANGE:` footer -> major
#   - any `feat:` / `feat(scope):`                            -> minor
#   - otherwise (fix, refactor, chore, docs, ...)             -> patch
detect_bump() {
	local base="$1"
	local range
	if [[ -n "$base" ]]; then
		range="${base}..HEAD"
	else
		range="HEAD"
	fi

	local subjects bodies
	subjects="$(git log --format='%s' "$range" 2>/dev/null || true)"
	bodies="$(git log --format='%B' "$range" 2>/dev/null || true)"

	if [[ -z "$subjects" ]]; then
		echo "minor"; return
	fi

	# Breaking change: `type!:` / `type(scope)!:` subject, or a BREAKING CHANGE
	# footer anywhere in a commit body.
	if grep -qE '^[a-zA-Z]+(\([^)]*\))?!:' <<<"$subjects" \
		|| grep -qE '(^|[[:space:]])BREAKING[ -]CHANGE' <<<"$bodies"; then
		echo "major"; return
	fi

	if grep -qE '^feat(\([^)]*\))?:' <<<"$subjects"; then
		echo "minor"; return
	fi

	echo "patch"
}

# Apply a bump level to a stable base version. Echoes the bumped stable version.
# Pre-1.0.0 policy: a "major" bump on a 0.x line becomes a minor bump, matching
# semver's rule that 0.x has no stability guarantee (breaking changes ride minor
# bumps until 1.0.0). Args: <bump-level> <major> <minor> <patch>
apply_bump() {
	local level="$1" major="$2" minor="$3" patch="$4"
	case "$level" in
		major)
			if [[ "$major" -eq 0 ]]; then
				echo "v0.$((minor + 1)).0"      # 0.x: breaking -> minor bump
			else
				echo "v$((major + 1)).0.0"
			fi ;;
		minor) echo "v${major}.$((minor + 1)).0" ;;
		patch) echo "v${major}.${minor}.$((patch + 1))" ;;
		*)     die "internal: unknown bump level '$level'" ;;
	esac
}

# Split a stable "vX.Y.Z" into the globals _MAJ/_MIN/_PAT. Dies on malformed.
parse_stable() {
	[[ "$1" =~ $SEMVER_RE ]] || die "not a stable version: $1"
	_MAJ="${BASH_REMATCH[1]}"; _MIN="${BASH_REMATCH[2]}"; _PAT="${BASH_REMATCH[3]}"
}

# --- changelog helpers -----------------------------------------------------

# Strip a Conventional-Commit prefix ("feat(scope): ", "fix!: ", ...) from a
# subject line, leaving just the human-readable description, capitalized.
strip_cc_prefix() {
	local s="$1"
	s="$(sed -E 's/^[a-zA-Z]+(\([^)]*\))?!?:[[:space:]]*//' <<<"$s")"
	# Capitalize the first letter for a tidy bullet.
	printf '%s' "$(tr '[:lower:]' '[:upper:]' <<<"${s:0:1}")${s:1}"
}

# Emit a Markdown changelog for the commits in `<base>..HEAD`, grouped into
# Features / Fixes / Other. `<base>` empty means "all history". Commits whose
# subject starts with `release`/`chore(release)` or `Merge ` are skipped as
# noise. Prints nothing (empty string) if there are no eligible commits.
changelog_markdown() {
	local base="$1"
	local range
	if [[ -n "$base" ]]; then range="${base}..HEAD"; else range="HEAD"; fi

	local feats="" fixes="" other=""
	local line desc
	# NUL-safe read of subjects, oldest first reversed to newest first.
	while IFS= read -r line; do
		[[ -n "$line" ]] || continue
		case "$line" in
			Merge\ *|release*|chore\(release\)*) continue ;;
		esac
		desc="$(strip_cc_prefix "$line")"
		case "$line" in
			feat*)  feats+="- ${desc}"$'\n' ;;
			fix*)   fixes+="- ${desc}"$'\n' ;;
			*)      other+="- ${desc}"$'\n' ;;
		esac
	done < <(git log --reverse --format='%s' "$range" 2>/dev/null | tac 2>/dev/null || git log --format='%s' "$range" 2>/dev/null)

	local out=""
	[[ -n "$feats" ]] && out+="### Features"$'\n\n'"$feats"$'\n'
	[[ -n "$fixes" ]] && out+="### Fixes"$'\n\n'"$fixes"$'\n'
	[[ -n "$other" ]] && out+="### Other changes"$'\n\n'"$other"$'\n'
	printf '%s' "$out"
}

# --- blog announcement -----------------------------------------------------

# Write a release-announcement post into the maxon.dev site checkout, commit it,
# and push (Cloudflare Pages deploys on push to main). Arguments:
#   $1 version   e.g. v0.1.0
#   $2 web_dir   path to the maxon-web checkout
#   $3 prev_tag  the previous tag (changelog lower bound; may be empty)
#   $4 rel_url   the GitHub release URL (may be empty)
#   $5 iso_date  the release date, YYYY-MM-DD
# Non-fatal: on any problem it warns and returns non-zero so the caller can
# report the release as published even if the announcement didn't go out.
publish_blog() {
	local version="$1" web_dir="$2" prev_tag="$3" rel_url="$4" iso_date="$5"

	if [[ ! -d "$web_dir/.git" ]]; then
		echo "warning: web dir '$web_dir' is not a git checkout; skipping blog post" >&2
		return 1
	fi
	local blog_dir="$web_dir/src/content/docs/blog"
	if [[ ! -d "$blog_dir" ]]; then
		echo "warning: blog dir '$blog_dir' not found; skipping blog post" >&2
		return 1
	fi

	# Slug: maxon-v0-1-0 (dots -> dashes) so the URL is /blog/maxon-v0-1-0.
	local slug="maxon-${version//./-}"
	local post="$blog_dir/${slug}.md"
	if [[ -e "$post" ]]; then
		echo "warning: blog post $post already exists; skipping (not overwriting)" >&2
		return 1
	fi

	local changelog
	changelog="$(changelog_markdown "$prev_tag")"
	[[ -n "$changelog" ]] || changelog="_See the full commit history for details._"$'\n'

	local since_note=""
	[[ -n "$prev_tag" ]] && since_note=" since ${prev_tag}"

	# Build the post. The frontmatter matches the starlight-blog schema used by
	# the existing posts (title/description/date/authors/tags/excerpt).
	{
		cat <<EOF
---
title: Maxon ${version}
description: Release notes for Maxon ${version} — the changes shipped in this release of the compiler and standard library.
date: ${iso_date}
authors: maxon
tags:
  - release
excerpt: Maxon ${version} is out. Here's what changed${since_note}.
---

**Maxon ${version} is released.** This build ships the self-hosted compiler and
standard library for the host platform.

## Changes${since_note}

${changelog}
## Download

Grab the release archive from the
EOF
		if [[ -n "$rel_url" ]]; then
			echo "[GitHub releases page](${rel_url})."
		else
			echo "[GitHub releases page](https://github.com/maxon-lang/maxon/releases)."
		fi
		cat <<EOF

Each archive contains the \`maxon\` compiler, the standard library, and the
runtime it needs. See the bundled \`INSTALL.md\` for how to run it.
EOF
	} > "$post"

	info "wrote blog post $post"

	# Commit and push in the web repo. Keep this self-contained: stage only the
	# new post so we never sweep up unrelated working-tree changes in that repo.
	if ! git -C "$web_dir" add "$post"; then
		echo "warning: failed to stage blog post in $web_dir" >&2
		return 1
	fi
	if ! git -C "$web_dir" commit -q -m "blog: announce Maxon ${version}"; then
		echo "warning: failed to commit blog post in $web_dir" >&2
		return 1
	fi

	local web_branch
	web_branch="$(git -C "$web_dir" branch --show-current)"
	info "pushing blog post to maxon.dev ($web_branch) — triggers Cloudflare deploy"
	if ! git -C "$web_dir" push origin "$web_branch"; then
		echo "warning: committed the blog post but failed to push it in $web_dir" >&2
		echo "         push it manually: git -C '$web_dir' push origin $web_branch" >&2
		return 1
	fi

	info "blog announcement published (live in ~1-2 min at https://maxon.dev/blog/${slug})"
}

# --- argument parsing ------------------------------------------------------

VERSION_ARG=""
NOTES=""
NOTES_FILE=""
PRERELEASE=0
DRAFT=0
SKIP_BUILD=0
SKIP_TESTS=0
DRY_RUN=0
RC=0
ASSUME_YES=0
BLOG=1                              # publish a blog announcement after release
WEB_DIR="${MAXON_WEB_DIR:-../maxon-web}"   # the maxon.dev site checkout

while [[ $# -gt 0 ]]; do
	case "$1" in
		--rc)          RC=1; shift ;;
		--yes|-y)      ASSUME_YES=1; shift ;;
		--blog)        BLOG=1; shift ;;
		--no-blog)     BLOG=0; shift ;;
		--web-dir)     WEB_DIR="${2:-}"; shift 2 ;;
		--notes)       NOTES="${2:-}"; shift 2 ;;
		--notes-file)  NOTES_FILE="${2:-}"; shift 2 ;;
		--prerelease)  PRERELEASE=1; shift ;;
		--draft)       DRAFT=1; shift ;;
		--skip-build)  SKIP_BUILD=1; shift ;;
		--skip-tests)  SKIP_TESTS=1; shift ;;
		--dry-run)     DRY_RUN=1; shift ;;
		-h|--help)     usage; exit 0 ;;
		-*)            die "unknown option: $1" ;;
		*)
			[[ -z "$VERSION_ARG" ]] || die "unexpected extra argument: $1"
			VERSION_ARG="$1"; shift ;;
	esac
done

# Default: derive the next version from commit history.
[[ -n "$VERSION_ARG" ]] || VERSION_ARG="auto"

# --- locate repo root ------------------------------------------------------

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || die "not inside a git repository"
cd "$REPO_ROOT"

# --- resolve the version ---------------------------------------------------
#
# Turn the version argument (explicit / auto / patch|minor|major) plus --rc into
# a concrete tag. Derived versions are echoed with their rationale and confirmed
# unless --yes is given.

DERIVED=0        # 1 if the version came from analysis (needs confirmation)
BASE_STABLE=""   # the stable version an rc counts down to

# The latest existing tag BEFORE this release — the changelog's lower bound.
# Captured now, before we create the new tag.
PREV_TAG="$(latest_any_tag)"

case "$VERSION_ARG" in
	auto|patch|minor|major)
		last_stable="$(latest_stable_tag)"
		if [[ "$VERSION_ARG" == "auto" ]]; then
			level="$(detect_bump "$last_stable")"
			reason="derived from commits since ${last_stable:-<first release>}"
		else
			level="$VERSION_ARG"
			reason="forced $level bump"
		fi

		if [[ -n "$last_stable" ]]; then
			parse_stable "$last_stable"
			BASE_STABLE="$(apply_bump "$level" "$_MAJ" "$_MIN" "$_PAT")"
		else
			# No prior release: the very first release is v0.1.0 by convention,
			# regardless of the requested/derived bump level. (There is nothing
			# to bump from, and v0.0.1 / v1.0.0 are both surprising first cuts.)
			BASE_STABLE="v0.1.0"
			reason="first release"
		fi

		DERIVED=1
		;;
	v*|[0-9]*)
		# Explicit version. Normalize a single leading 'v'.
		[[ "$VERSION_ARG" == v* ]] || VERSION_ARG="v$VERSION_ARG"
		if [[ "$VERSION_ARG" =~ $RC_RE ]]; then
			# Caller wrote an explicit rc tag; honor it as-is.
			RC=1
			BASE_STABLE="v${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.${BASH_REMATCH[3]}"
			EXPLICIT_RC="$VERSION_ARG"
		elif [[ "$VERSION_ARG" =~ $SEMVER_RE ]]; then
			BASE_STABLE="$VERSION_ARG"
		else
			die "malformed version '$VERSION_ARG' (expected vX.Y.Z or vX.Y.Z-rc.N)"
		fi
		reason="explicit version"
		;;
	*)
		die "invalid version argument '$VERSION_ARG' (use vX.Y.Z, auto, patch, minor, or major)"
		;;
esac

# Fold in --rc: turn the resolved stable base into the next rc for that base.
if [[ "$RC" -eq 1 ]]; then
	if [[ -n "${EXPLICIT_RC:-}" ]]; then
		VERSION="$EXPLICIT_RC"
	else
		# Find the highest existing rc for this base and increment; else rc.1.
		next_rc=1
		while IFS= read -r t; do
			[[ "$t" =~ $RC_RE ]] || continue
			if [[ "v${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.${BASH_REMATCH[3]}" == "$BASE_STABLE" ]]; then
				n="${BASH_REMATCH[4]}"
				(( n + 1 > next_rc )) && next_rc=$(( n + 1 ))
			fi
		done < <(git tag --list 'v[0-9]*')
		VERSION="${BASE_STABLE}-rc.${next_rc}"
	fi
	PRERELEASE=1   # release candidates are always prereleases
else
	VERSION="$BASE_STABLE"
fi

TAG="$VERSION"

# Announce and (for derived versions) confirm.
rc_note=""
[[ "$RC" -eq 1 ]] && rc_note=", release candidate"
if [[ "$DERIVED" -eq 1 ]]; then
	info "next version: $VERSION  (${reason}${rc_note})"
elif [[ "$RC" -eq 1 && -z "${EXPLICIT_RC:-}" ]]; then
	info "release candidate: $VERSION"
else
	info "version: $VERSION"
fi

# Confirm derived/bumped versions (an explicit vX.Y.Z is taken as intentional).
if [[ "$DERIVED" -eq 1 && "$ASSUME_YES" -eq 0 ]]; then
	if [[ -t 0 ]]; then
		printf "Proceed with %s? [y/N] " "$VERSION"
		read -r reply
		case "$reply" in
			y|Y|yes|YES) ;;
			*) die "aborted by user" ;;
		esac
	else
		die "refusing to use a derived version ($VERSION) without confirmation; pass --yes or an explicit vX.Y.Z (stdin is not a TTY)"
	fi
fi

# --- tool checks -----------------------------------------------------------

command -v git >/dev/null 2>&1    || die "git not found on PATH"
command -v gh >/dev/null 2>&1     || die "gh (GitHub CLI) not found on PATH"
if [[ "$SKIP_BUILD" -eq 0 ]]; then
	command -v dotnet >/dev/null 2>&1 || die "dotnet not found on PATH"
fi

if [[ "$DRY_RUN" -eq 0 ]]; then
	gh auth status >/dev/null 2>&1 || die "gh is not authenticated; run 'gh auth login'"
fi

# --- supported targets -----------------------------------------------------
#
# One binary asset is built and attached per target in this list. To add a
# target, append its "arch-os" slug here — nothing else in the script needs to
# change. Keep the slugs in the form the compiler's --target flag accepts
# (see `maxon build --target`).
#
# IMPORTANT: only enable a target once `maxon build maxon-selfhosted
# --target=<t>` actually emits that target's native binary format. As of this
# writing the C# bootstrap emits a Windows PE for the self-hosted build
# regardless of --target, so a non-windows target here would package a
# mislabeled Windows binary. x64-windows is therefore the only enabled target;
# uncomment the others as the toolchain gains real cross-compilation of the
# compiler itself.
TARGETS=(
	x64-windows
	# x64-linux
	# arm64-macos
	# x64-macos
	# arm64-linux
)

# The executable extension for a target OS: ".exe" on windows, none elsewhere.
target_exe_ext() {
	case "$1" in
		*-windows) printf '.exe' ;;
		*)         printf '' ;;
	esac
}

# The archive format for a target: zip for windows (the native tooling there),
# tar.gz elsewhere. Echoes "zip" or "targz".
target_archive_format() {
	case "$1" in
		*-windows) printf 'zip' ;;
		*)         printf 'targz' ;;
	esac
}

# --- detect host platform --------------------------------------------------

# The host target is the only one whose freshly built binary we can execute
# (to run spec tests). Cross-built binaries for other targets are packaged
# untested — you can't run a Linux ELF on Windows.
uname_s="$(uname -s)"
uname_m="$(uname -m)"

case "$uname_m" in
	x86_64|amd64)        HOST_ARCH="x64" ;;
	arm64|aarch64)       HOST_ARCH="arm64" ;;
	*)                   die "unsupported host architecture: $uname_m" ;;
esac

case "$uname_s" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT)  HOST_OS="windows" ;;
	Linux*)                            HOST_OS="linux" ;;
	Darwin*)                           HOST_OS="macos" ;;
	*)                                 die "unsupported host OS: $uname_s" ;;
esac

HOST_TARGET="${HOST_ARCH}-${HOST_OS}"
HOST_EXE="$(target_exe_ext "$HOST_TARGET")"
info "host platform: $HOST_TARGET"
info "building targets: ${TARGETS[*]}"

CSHARP_BIN="bin/maxon${HOST_EXE}"

# --- working-tree sanity ---------------------------------------------------

if [[ "$DRY_RUN" -eq 0 ]]; then
	if ! git diff-index --quiet HEAD -- 2>/dev/null; then
		echo "warning: working tree has uncommitted changes; the tag will point at HEAD," >&2
		echo "         which does not include them." >&2
	fi
	if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
		die "tag $TAG already exists"
	fi
	if gh release view "$TAG" >/dev/null 2>&1; then
		die "a GitHub release named $TAG already exists"
	fi
fi

# --- build the C# bootstrap (once) -----------------------------------------
#
# The C# compiler drives every target's self-hosted build, so it's built once
# up front regardless of how many targets we package.

if [[ "$SKIP_BUILD" -eq 0 ]]; then
	info "building C# bootstrap compiler (dotnet build maxon-sharp)"
	dotnet build maxon-sharp
	[[ -x "$CSHARP_BIN" ]] || die "expected C# compiler at $CSHARP_BIN after build"
else
	info "--skip-build: using existing binaries"
fi

DIST_DIR="$REPO_ROOT/dist"
mkdir -p "$DIST_DIR"

# Zip a staged directory into ARCHIVE, preserving a single top-level folder.
# Args: <format: zip|targz> <stage_dir> <pkg_name> <out_archive>
make_archive() {
	local fmt="$1" stage="$2" pkg="$3" out="$4"
	rm -f "$out"
	if [[ "$fmt" == "zip" ]]; then
		if command -v 7z >/dev/null 2>&1; then
			( cd "$stage" && 7z a -tzip "$out" "$pkg" >/dev/null )
		elif command -v bsdtar >/dev/null 2>&1; then
			( cd "$stage" && bsdtar -a -cf "$out" "$pkg" )
		elif command -v zip >/dev/null 2>&1; then
			( cd "$stage" && zip -q -r "$out" "$pkg" )
		else
			# PowerShell fallback (always present on Windows hosts). Point -Path
			# at the staged dir itself (no trailing \*) so the zip nests a single
			# top-level folder, matching the other tools.
			local sw dw
			sw="$(cygpath -w "$stage/$pkg" 2>/dev/null || echo "$stage/$pkg")"
			dw="$(cygpath -w "$out" 2>/dev/null || echo "$out")"
			powershell -NoProfile -Command \
				"Compress-Archive -Path '$sw' -DestinationPath '$dw' -Force" \
				|| die "no zip tool found (tried 7z, bsdtar, zip, PowerShell Compress-Archive)"
		fi
	else
		# On a Windows host, GNU tar reads a "C:/..." output path as a
		# "host:path" remote spec and fails; --force-local keeps it local. That
		# flag is GNU-only, so only pass it on Windows (where the path has a
		# drive letter and GNU tar is what's installed) — BSD tar on macOS
		# neither needs nor accepts it.
		if [[ "$HOST_OS" == "windows" ]]; then
			tar --force-local -C "$stage" -czf "$out" "$pkg"
		else
			tar -C "$stage" -czf "$out" "$pkg"
		fi
	fi
	[[ -f "$out" ]] || die "archive was not created: $out"
}

# Build, stage, and archive one target's binary asset. Appends the produced
# archive path to the ASSETS array. Args: <target>
build_target_asset() {
	local target="$1"
	local exe fmt pkg stage pkgdir sh_bin archive
	exe="$(target_exe_ext "$target")"
	fmt="$(target_archive_format "$target")"

	# Build the self-hosted compiler for this target (the C# bootstrap emits the
	# target's native format). The host target may already be built; other
	# targets are cross-compiled.
	if [[ "$SKIP_BUILD" -eq 0 ]]; then
		info "[$target] building self-hosted compiler"
		"$CSHARP_BIN" build maxon-selfhosted --target="$target"
	fi

	# The self-hosted build writes .maxon/maxon-selfhosted with the target's
	# extension (.exe only for windows targets).
	sh_bin="maxon-selfhosted/.maxon/maxon-selfhosted${exe}"
	[[ -f "$sh_bin" ]] || die "[$target] self-hosted compiler not found at $sh_bin after build"

	# Run spec tests only for the host target — a cross-built binary can't run
	# here. (The suite still exercises the compiler for the host arch/os.)
	if [[ "$SKIP_TESTS" -eq 0 ]]; then
		if [[ "$target" == "$HOST_TARGET" ]]; then
			info "[$target] running self-hosted spec tests"
			"$sh_bin" spec-test
			info "[$target] spec tests passed"
		else
			info "[$target] cross-target build — skipping spec tests (cannot run on host)"
		fi
	else
		info "[$target] --skip-tests: skipping the spec-test suite"
	fi

	# --- stage the payload ---
	pkg="maxon-${VERSION}-${target}"
	stage="$(mktemp -d "${TMPDIR:-/tmp}/maxon-release.XXXXXX")"
	STAGE_DIRS+=("$stage")   # cleaned up by the EXIT trap
	pkgdir="$stage/$pkg"
	mkdir -p "$pkgdir"

	info "[$target] staging release payload"

	# The compiler binary, renamed to the friendly command name `maxon`.
	cp "$sh_bin" "$pkgdir/maxon${exe}"

	# The self-hosted compiler resolves its runtime dependencies by walking *up*
	# from the current working directory (NOT from the binary's location): a
	# `stdlib/` directory and a `runtime.std` file. Both must live at the package
	# root so a project created inside the extracted package resolves them.
	# `runtime_wasm.std` sits next to `runtime.std` for wasm cross-compilation.
	[[ -d stdlib ]] || die "stdlib/ directory not found at repo root"
	cp -R stdlib "$pkgdir/stdlib"
	rm -rf "$pkgdir/stdlib/.maxon"   # drop any accumulated build cache

	local runtime_src="maxon-selfhosted/Compiler/Runtime"
	[[ -f "$runtime_src/runtime.std" ]] || die "runtime.std not found at $runtime_src/"
	cp "$runtime_src/runtime.std" "$pkgdir/runtime.std"
	[[ -f "$runtime_src/runtime_wasm.std" ]] && cp "$runtime_src/runtime_wasm.std" "$pkgdir/runtime_wasm.std"

	local f
	for f in README.md LICENSE-MIT LICENSE-APACHE; do
		[[ -f "$f" ]] && cp "$f" "$pkgdir/$f"
	done

	cat > "$pkgdir/INSTALL.md" <<EOF
# Maxon $VERSION ($target)

This package contains the self-hosted Maxon compiler and everything it needs
at runtime.

## Layout

    maxon${exe}          the compiler
    stdlib/              the standard library
    runtime.std          native runtime IR
    runtime_wasm.std     wasm runtime IR (for --target=wasm32-wasi)

## Running

The compiler locates \`stdlib/\` and \`runtime.std\` by searching **upward from
the current working directory**. The simplest way to use this build is to put
your Maxon project inside this directory and run the compiler from there:

    cd $pkg
    ./maxon${exe} build path/to/your/project.maxon

Or add this directory to your PATH and always invoke the compiler from a
working directory at or below it.

## Notes

- This is the self-hosted compiler (Maxon compiling Maxon), still under active
  development. See README.md for project status.
- WebAssembly cross-compilation (\`--target=wasm32-wasi\`) additionally requires
  the vendored \`wasm-tools\`, \`wasm-opt\`, and WASI \`.wit\` toolchain, which is
  not bundled here to keep the download small. Build from source for wasm.

Licensed under MIT OR Apache-2.0 (see LICENSE-MIT / LICENSE-APACHE).
EOF

	# --- archive ---
	if [[ "$fmt" == "zip" ]]; then
		archive="$DIST_DIR/${pkg}.zip"
	else
		archive="$DIST_DIR/${pkg}.tar.gz"
	fi
	info "[$target] creating archive $archive"
	make_archive "$fmt" "$stage" "$pkg" "$archive"
	info "[$target] archive ready: $archive ($(du -h "$archive" | cut -f1))"

	ASSETS+=("$archive")
}

# --- build every target's binary asset -------------------------------------

ASSETS=()          # every archive to attach to the release
STAGE_DIRS=()      # staging dirs to clean up on exit
trap 'for d in "${STAGE_DIRS[@]:-}"; do [[ -n "$d" ]] && rm -rf "$d"; done' EXIT

host_target_in_list=0
for _t in "${TARGETS[@]}"; do
	[[ "$_t" == "$HOST_TARGET" ]] && host_target_in_list=1
done
if [[ "$SKIP_TESTS" -eq 0 && "$host_target_in_list" -eq 0 ]]; then
	echo "warning: host target ($HOST_TARGET) is not in the target list; no spec tests will run." >&2
	echo "         Pass --skip-tests to silence this, or add $HOST_TARGET to TARGETS." >&2
fi

for target in "${TARGETS[@]}"; do
	build_target_asset "$target"
done

[[ "${#ASSETS[@]}" -gt 0 ]] || die "no binary assets were produced"

# --- source archive --------------------------------------------------------
#
# A reproducible snapshot of the source at the release commit, produced with
# `git archive` (tracked files only, respecting .gitignore). The vendored
# cross-platform wasm toolchain under vendor/ is excluded to keep the download
# small — native builds don't need it. (GitHub also auto-attaches a full
# "Source code" zip/tar.gz that DOES include vendor/, for anyone who needs it.)
#
# The tag doesn't exist yet at this point (it's created from HEAD a few steps
# below, just before publishing), so we archive HEAD — which IS the release
# commit. `git archive` uses the committed tree, not the working directory, so
# uncommitted changes are correctly excluded.
SOURCE_ARCHIVE="$DIST_DIR/maxon-${VERSION}-source.tar.gz"
info "creating source archive $SOURCE_ARCHIVE (excludes vendor/)"
rm -f "$SOURCE_ARCHIVE"
git archive --format=tar.gz --prefix="maxon-${VERSION}/" -o "$SOURCE_ARCHIVE" \
	HEAD -- . ':(exclude)vendor' ':(exclude)vendor/*' \
	|| die "git archive failed to build the source archive"
[[ -f "$SOURCE_ARCHIVE" ]] || die "source archive was not created: $SOURCE_ARCHIVE"
info "source archive ready: $SOURCE_ARCHIVE ($(du -h "$SOURCE_ARCHIVE" | cut -f1))"

ASSETS+=("$SOURCE_ARCHIVE")

# --- resolve release notes -------------------------------------------------

NOTES_ARGS=()
if [[ -n "$NOTES_FILE" ]]; then
	[[ -f "$NOTES_FILE" ]] || die "notes file not found: $NOTES_FILE"
	NOTES_ARGS=(--notes-file "$NOTES_FILE")
elif [[ -n "$NOTES" ]]; then
	NOTES_ARGS=(--notes "$NOTES")
else
	NOTES_ARGS=(--generate-notes)
fi

RELEASE_FLAGS=()
[[ "$PRERELEASE" -eq 1 ]] && RELEASE_FLAGS+=(--prerelease)
[[ "$DRAFT" -eq 1 ]] && RELEASE_FLAGS+=(--draft)

# --- dry run stops here ----------------------------------------------------

if [[ "$DRY_RUN" -eq 1 ]]; then
	echo ""
	info "--dry-run: no tag, push, or release created."
	echo "Assets (${#ASSETS[@]}):"
	for a in "${ASSETS[@]}"; do echo "  $a"; done
	echo ""
	echo "To publish manually:"
	echo "  git tag -a $TAG -m 'Maxon $VERSION'"
	echo "  git push origin $TAG"
	printf "  gh release create %s" "$TAG"
	for a in "${ASSETS[@]}"; do printf " '%s'" "$a"; done
	printf " --title '%s'" "Maxon $VERSION"
	for a in "${NOTES_ARGS[@]}" "${RELEASE_FLAGS[@]}"; do printf " %q" "$a"; done
	echo ""

	# Preview the blog announcement that a real run would publish.
	if [[ "$BLOG" -eq 1 && "$RC" -eq 0 && "$DRAFT" -eq 0 ]]; then
		echo ""
		echo "Blog announcement (would be written to $WEB_DIR/src/content/docs/blog/maxon-${VERSION//./-}.md and pushed):"
		echo "  changelog since ${PREV_TAG:-<first release>}:"
		cl="$(changelog_markdown "$PREV_TAG")"
		if [[ -n "$cl" ]]; then
			sed 's/^/    /' <<<"$cl"
		else
			echo "    (no eligible commits found)"
		fi
	elif [[ "$BLOG" -eq 1 ]]; then
		echo ""
		echo "Blog announcement: skipped (release candidate / draft)."
	fi
	exit 0
fi

# --- tag, push, publish ----------------------------------------------------

info "creating annotated tag $TAG"
git tag -a "$TAG" -m "Maxon $VERSION"

info "pushing tag to origin"
git push origin "$TAG"

info "creating GitHub release $TAG and uploading ${#ASSETS[@]} asset(s)"
gh release create "$TAG" "${ASSETS[@]}" \
	--title "Maxon $VERSION" \
	"${NOTES_ARGS[@]}" \
	"${RELEASE_FLAGS[@]}"

info "done. Release $TAG published:"
REL_URL="$(gh release view "$TAG" --json url --jq .url 2>/dev/null || true)"
echo "${REL_URL:-https://github.com/maxon-lang/maxon/releases/tag/$TAG}"

# --- blog announcement -----------------------------------------------------
#
# Only for real, final releases: skip release candidates (rc = prerelease) and
# drafts, since neither is something to announce to the world yet.
if [[ "$BLOG" -eq 1 ]]; then
	if [[ "$RC" -eq 1 || "$DRAFT" -eq 1 ]]; then
		info "skipping blog announcement (release candidate / draft)"
	else
		info "publishing blog announcement to maxon.dev"
		if publish_blog "$VERSION" "$WEB_DIR" "$PREV_TAG" "$REL_URL" "$(date +%Y-%m-%d)"; then
			:
		else
			echo "warning: the release is published, but the blog announcement did not go out." >&2
			echo "         Re-run with --no-blog once fixed, or add the post manually." >&2
		fi
	fi
fi
