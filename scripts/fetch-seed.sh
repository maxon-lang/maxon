#!/usr/bin/env bash
#
# Place a released compiler at `.bootstrap/maxon`, the seed that builds this tree when the slot is empty.
#
# ⛔ THE SEED IS THE BINARY ALONE. A release archive ships its own `stdlib/` and `runtime/`, and the
# compiler resolves both by walking UP from its own executable — so an archive unpacked into `.bootstrap/`
# would leave a RELEASED stdlib and runtime beside the seed, and this checkout's own would never be read.
# The build would succeed and compile the wrong sources, silently. The archive is unpacked in a temporary
# directory and only the executable is copied out.
#
# ⭐ THE SEED MUST BE A RELEASE THIS TREE CAN BE BUILT WITH. The stdlib may call a `__Builtins` intrinsic
# only once a published release has it, so an older seed fails the first build with E3004. Re-run this
# after every release to keep a local seed current; CI runs it on every build.
#
# Usage:
#   scripts/fetch-seed.sh                        # the latest release, for this host
#   scripts/fetch-seed.sh v0.2.2                 # that release
#   scripts/fetch-seed.sh arm64-macos            # another target (for a remote host)
#   scripts/fetch-seed.sh --allow-none           # no published release is a notice, not a failure
#
# Needs an authenticated `gh`.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

tag=""
target=""
allow_none=0

for argument in "$@"; do
	case "$argument" in
		--allow-none)                               allow_none=1 ;;
		v[0-9]*)                                    tag="$argument" ;;
		x64-windows|x64-linux|arm64-macos|arm64-linux) target="$argument" ;;
		*) echo "fetch-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

[ -n "$target" ] || target="$(maxon_host_target)"

if [ -z "$tag" ]; then
	tag="$(gh release list --limit 1 --exclude-drafts --exclude-pre-releases --json tagName --jq '.[0].tagName // empty')"
fi

# ⚠ NO RELEASE AT ALL is the case exactly once, before the first release; CI treats it as "nothing to
# build with yet". A release that exists but publishes no archive for the target is always a failure.
if [ -z "$tag" ]; then
	if [ "$allow_none" = 1 ]; then
		echo "fetch-seed.sh: no published release to seed from"
		exit 0
	fi

	echo "fetch-seed.sh: no published release to seed from" >&2
	exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

gh release download "$tag" --pattern "*$target*" --dir "$work"
archive="$(find "$work" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | head -n1)"
[ -n "$archive" ] || { echo "fetch-seed.sh: $tag publishes no archive for $target" >&2; exit 1; }

mkdir -p "$work/unpacked"

case "$archive" in
	*.zip)    unzip -q "$archive" -d "$work/unpacked" ;;
	*.tar.gz) tar -xzf "$archive" -C "$work/unpacked" ;;
esac

found="$(find "$work/unpacked" -type f \( -name 'maxon' -o -name 'maxon.exe' \) | head -n1)"
[ -n "$found" ] || { echo "fetch-seed.sh: no maxon binary in $tag's $target archive" >&2; exit 1; }

seed="$(maxon_downloaded_path)"
[ "$target" = "$(maxon_host_target)" ] || seed=".bootstrap/$(basename "$found")"

# Replaced by rename, so a failed copy never leaves a truncated seed where a working one stood.
mkdir -p .bootstrap
cp "$found" "$seed.partial"
chmod +x "$seed.partial"
mv -f "$seed.partial" "$seed"

echo "fetch-seed.sh: $seed is $tag ($target)"
