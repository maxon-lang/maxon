#!/usr/bin/env bash
#
# Fetch a compiler able to build this tree, into the gitignored `.bootstrap/`.
#
# Maxon is written in Maxon, so building it needs a Maxon compiler. There is no second compiler in
# this repo to fall back on: the published release IS the bootstrap. This downloads the newest one
# for this host, verifies it against the checksums published beside it, and leaves it where
# `scripts/build.sh` looks.
#
# ⭐ `curl` AND NOTHING ELSE. Building this project should not require installing a vendor's CLI
# first, so the release is read through GitHub's public REST API rather than through `gh`. No
# authentication is needed for a public repo; `GITHUB_TOKEN`, if it happens to be set, is passed
# only to raise the anonymous rate limit (60 requests an hour per IP).
#
# ⚠ A DOWNLOAD IS NOT TRUSTED UNTIL IT IS VERIFIED. A mismatch is fatal and the file is deleted: a
# compiler is the one artifact where "probably fine" is not a category, since everything it emits
# inherits whatever it is.
#
# Usage:
#   scripts/bootstrap.sh [--version=vX.Y.Z] [--force]
#
#   --version=  a specific release instead of the newest.
#   --force     re-download even when `.bootstrap/` already holds that version.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

REPO="maxon-lang/maxon"
API="https://api.github.com/repos/$REPO"
DEST=".bootstrap"
version=""
force=0

for arg in "$@"; do
	case "$arg" in
		--version=*) version="${arg#*=}" ;;
		--force)     force=1 ;;
		-h|--help)   sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "bootstrap.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

command -v curl >/dev/null 2>&1 || { echo "bootstrap.sh: curl not found on PATH" >&2; exit 1; }

# The release asset naming matches what the release workflow publishes, one archive per target.
host_target="$(maxon_host_target)"

# ⚠ AN `if`, NOT `[ … ] && …`. Under `set -e` a trailing test that answers FALSE is the script's exit
# status, so the one-liner form ends the run silently whenever `GITHUB_TOKEN` is unset — which is the
# ordinary case this script is written for.
curl_args=(--fail --silent --show-error --location -H "Accept: application/vnd.github+json")
if [ -n "${GITHUB_TOKEN:-}" ]; then
	curl_args+=(-H "Authorization: Bearer $GITHUB_TOKEN")
fi

# One release's JSON, or nothing at all. A 404 (no releases yet, or no such tag) and a network
# failure both land here as empty output; the caller distinguishes them by what it was asking for.
release_json() { curl "${curl_args[@]}" "$API/releases/$1" 2>/dev/null || true; }

# ⚠ FIELD-SCRAPING, NOT JSON PARSING, AND THAT IS THE POINT. `jq` is exactly the dependency this
# script exists to avoid, and the two fields it reads — a tag name and a list of asset URLs — are
# flat strings GitHub has never nested. `grep -o` is indifferent to whether the response is
# pretty-printed.
#
# ⚠ BOTH END IN `|| true` BECAUSE "NO MATCH" IS AN ANSWER HERE, NOT A FAILURE. `grep` exits 1 when it
# finds nothing, `pipefail` promotes that to the pipeline's status, and `set -e` would then end the
# run at the exact moment the script has something to say — an unreleased repo would exit silently
# instead of printing the base-case message below.
json_field() { grep -o "\"$2\": *\"[^\"]*\"" <<<"$1" | head -n1 | sed 's/.*: *"\(.*\)"/\1/' || true; }
asset_urls() { grep -o '"browser_download_url": *"[^"]*"' <<<"$1" | sed 's/.*: *"\(.*\)"/\1/' || true; }

if [ -z "$version" ]; then
	version="$(json_field "$(release_json latest)" tag_name)"
fi

# ⛔ THE BASE CASE HAS NO ANSWER AND MUST SAY SO. Until v0.1.0 is published there is nothing to
# download, and a caller staring at a generic failure would reasonably conclude their network is
# broken rather than that the chain has no first link yet.
if [ -z "$version" ]; then
	cat >&2 <<'EOF'
bootstrap.sh: no published release to bootstrap from.

  This repo builds itself, so the first compiler has to come from somewhere else. Until the first
  release exists there is no such thing to download, and the only compiler that can build this tree
  is one built before the bootstrap was retired.

  If you have one, put it at .bootstrap/maxon (or .bootstrap/maxon.exe) and re-run scripts/build.sh.
EOF
	exit 1
fi

stamp="$DEST/.version"
if [ "$force" -eq 0 ] && [ -x "$DEST/maxon$MAXON_EXE_EXT" ] && [ "$(cat "$stamp" 2>/dev/null || true)" = "$version" ]; then
	echo "bootstrap.sh: $version already in $DEST"
	exit 0
fi

json="$(release_json "tags/$version")"
[ -n "$json" ] || { echo "bootstrap.sh: no release $version in $REPO (or the network is down)" >&2; exit 1; }

# ⛔ THE EXACT NAME FIRST, AND A LOOSE MATCH ONLY AS A FALLBACK. `maxon-<version>-<target>.<ext>` is
# what the release workflow publishes; taking the first asset whose name merely CONTAINS the target
# would pick a neighbour the moment one exists — measured against a real release elsewhere, that rule
# selects a `-c-api` variant over the plain archive and the bootstrap silently holds the wrong file.
urls="$(asset_urls "$json")"
archive_url=""
for ext in zip tar.gz; do
	if [ -z "$archive_url" ]; then
		archive_url="$(grep -E "/maxon-$version-$host_target\.$ext\$" <<<"$urls" | head -n1 || true)"
	fi
done
if [ -z "$archive_url" ]; then
	archive_url="$(grep -E "/[^/]*$host_target[^/]*\.(zip|tar\.gz)\$" <<<"$urls" | head -n1 || true)"
	if [ -n "$archive_url" ]; then
		echo "bootstrap.sh: no maxon-$version-$host_target archive; falling back to $(basename "$archive_url")" >&2
	fi
fi
[ -n "$archive_url" ] || { echo "bootstrap.sh: $version publishes no archive for $host_target" >&2; exit 1; }
sums_url="$(grep -E '/SHA256SUMS[^/]*$' <<<"$urls" | head -n1 || true)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "bootstrap.sh: fetching $version for $host_target"
archive="$work/$(basename "$archive_url")"
curl "${curl_args[@]}" -o "$archive" "$archive_url" \
	|| { echo "bootstrap.sh: download failed: $archive_url" >&2; exit 1; }

if [ -n "$sums_url" ] && curl "${curl_args[@]}" -o "$work/SHA256SUMS" "$sums_url"; then
	( cd "$work" && grep "$(basename "$archive")" SHA256SUMS | sha256sum -c - ) \
		|| { rm -f "$archive"; echo "bootstrap.sh: checksum mismatch — deleted $archive" >&2; exit 1; }
else
	echo "bootstrap.sh: WARNING — no SHA256SUMS in $version; the download is UNVERIFIED" >&2
fi

rm -rf "$DEST"
mkdir -p "$DEST"
case "$archive" in
	*.zip)    ( cd "$DEST" && unzip -q "$archive" ) ;;
	*.tar.gz) tar -xzf "$archive" -C "$DEST" ;;
esac

# ⛔ KEEP THE BINARY AND NOTHING ELSE. The archive ships its own `stdlib/`, and the compiler resolves
# `stdlib/` by walking UP from its own executable — so an archive extracted whole would leave a
# RELEASED stdlib one directory above the compiler, and the tree's own `stdlib/` would never be
# reached. The build would succeed and compile the wrong library, silently. Alone in `.bootstrap/`,
# the walk continues up to the repo root and finds the tree's.
found="$(find "$DEST" -type f -name "maxon$MAXON_EXE_EXT" | head -n1)"
[ -n "$found" ] || { echo "bootstrap.sh: no maxon binary in $version's $host_target archive" >&2; exit 1; }
mv "$found" "$work/hoisted$MAXON_EXE_EXT"
rm -rf "${DEST:?}"/*
mv "$work/hoisted$MAXON_EXE_EXT" "$DEST/maxon$MAXON_EXE_EXT"

chmod +x "$DEST/maxon$MAXON_EXE_EXT" 2>/dev/null || true
[ -x "$DEST/maxon$MAXON_EXE_EXT" ] || { echo "bootstrap.sh: no maxon binary in $version's $host_target archive" >&2; exit 1; }

printf '%s' "$version" > "$stamp"
echo "bootstrap.sh: $version ready at $DEST/maxon$MAXON_EXE_EXT"
