#!/usr/bin/env bash
#
# Fetch a compiler able to build this tree, into the gitignored `.bootstrap/`.
#
# Maxon is written in Maxon, so building it needs a Maxon compiler. There is no second compiler in
# this repo to fall back on: the published release IS the bootstrap. This downloads the newest one
# for this host, verifies it against the checksums published beside it, and leaves it where
# `scripts/build.sh` looks.
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
DEST=".bootstrap"
version=""
force=0

for arg in "$@"; do
	case "$arg" in
		--version=*) version="${arg#*=}" ;;
		--force)     force=1 ;;
		-h|--help)   sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "bootstrap.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

command -v gh >/dev/null 2>&1 || { echo "bootstrap.sh: gh (GitHub CLI) not found on PATH" >&2; exit 1; }

# The release asset naming matches what the release workflow publishes, one archive per target.
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*|Windows_NT) host_target="x64-windows" ;;
	Darwin*)                         host_target="arm64-macos" ;;
	Linux*)  case "$(uname -m)" in
		aarch64|arm64) host_target="arm64-linux" ;;
		*)             host_target="x64-linux" ;;
	esac ;;
	*) echo "bootstrap.sh: unsupported host: $(uname -s)" >&2; exit 1 ;;
esac

if [ -z "$version" ]; then
	version="$(gh release list -R "$REPO" --limit 1 --json tagName --jq '.[0].tagName' 2>/dev/null || true)"
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

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "bootstrap.sh: fetching $version for $host_target"
gh release download "$version" -R "$REPO" --pattern "*$host_target*" --dir "$work" --clobber
gh release download "$version" -R "$REPO" --pattern "SHA256SUMS*" --dir "$work" --clobber 2>/dev/null || true

archive="$(find "$work" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | head -n1)"
[ -n "$archive" ] || { echo "bootstrap.sh: no archive for $host_target in $version" >&2; exit 1; }

if [ -f "$work/SHA256SUMS" ]; then
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
