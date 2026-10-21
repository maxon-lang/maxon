#!/usr/bin/env bash
#
# Place the pinned seed compiler at `.bootstrap/maxon`, the compiler that builds this tree when the slot is empty.
#
# ⭐ THE SEED IS THE RELEASE `scripts/seed-tag` NAMES. It is a compiler frozen by
# `scripts/freeze-seed.sh` and published as a `seed-*` pre-release, and it stays the seed until a
# commit moves the pin: a new release does not change what builds this tree. The pin is read from
# the checkout, so a commit and the seed that builds it travel together.
#
# ⛔ THE SEED IS THE BINARY ALONE. A release archive may ship its own `stdlib/` and `runtime/`, and the
# compiler resolves both by walking UP from its own executable — so an archive unpacked into `.bootstrap/`
# would leave a RELEASED stdlib and runtime beside the seed, and this checkout's own would never be read.
# The build would succeed and compile the wrong sources, silently. The archive is unpacked in a temporary
# directory and only the executable is copied out.
#
# ⛔ THE ARCHIVE IS CHECKED AGAINST THE `SHA256SUMS` BESIDE IT BEFORE IT IS UNPACKED, and a release
# or `--from` directory without one is refused: a pinned seed means exactly the bytes that were
# frozen.
#
# Usage:
#   scripts/fetch-seed.sh                        # the pinned seed, for this host
#   scripts/fetch-seed.sh seed-2026-10-20-0123456789ab
#                                                # that seed
#   scripts/fetch-seed.sh v0.2.2                 # that release
#   scripts/fetch-seed.sh arm64-macos            # another target (for a remote host)
#   scripts/fetch-seed.sh --allow-none           # a pinned release this repository has not published is a
#                                                # notice, not a failure (a fork, before it publishes its own)
#   scripts/fetch-seed.sh arm64-macos --output=PATH
#                                                # to PATH, leaving this host's own seed in place;
#                                                # a relative PATH is from the caller's directory
#   scripts/fetch-seed.sh TAG --from=DIR         # from DIR's archives and SHA256SUMS, as
#                                                # freeze-seed.sh writes them to temp/seed/dist,
#                                                # before TAG is published
#
# A missing or empty `scripts/seed-tag` is always a failure. Needs an authenticated `gh`, except
# with `--from`.

set -euo pipefail

caller_dir="$(pwd)"
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/seed.sh

tag=""
target=""
allow_none=0
output=""
from=""

caller_path() {
	case "$1" in
		/*|[A-Za-z]:*) printf '%s' "$1" ;;
		*)             printf '%s/%s' "$caller_dir" "$1" ;;
	esac
}

for argument in "$@"; do
	case "$argument" in
		--allow-none)                               allow_none=1 ;;
		--from=?*)                                  from="$(caller_path "${argument#--from=}")" ;;
		--output=?*)                                output="$(caller_path "${argument#--output=}")" ;;
		v[0-9]*|seed-?*)                            tag="$argument" ;;
		x64-windows|x64-linux|arm64-macos|arm64-linux) target="$argument" ;;
		*) echo "fetch-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

[ -n "$target" ] || target="$(maxon_host_target)"
[ -z "$from" ] || [ "$allow_none" = 0 ] || { echo "fetch-seed.sh: --from and --allow-none cannot be combined — a local directory either holds the seed or does not" >&2; exit 2; }
[ -z "$from" ] || [ -n "$tag" ] || { echo "fetch-seed.sh: --from needs the tag its archives were frozen as" >&2; exit 2; }

if [ -z "$tag" ]; then
	[ -f "$SeedPinFile" ] || { echo "fetch-seed.sh: $SeedPinFile is missing — it names the seed release this tree is built with" >&2; exit 1; }
	tag="$(tr -d '[:space:]' < "$SeedPinFile")"
	[ -n "$tag" ] || { echo "fetch-seed.sh: $SeedPinFile is empty" >&2; exit 1; }
fi

sha256_of() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | cut -d' ' -f1
	else
		shasum -a 256 "$1" | cut -d' ' -f1
	fi
}

download_release() {
	if ! seed_release_published "$tag"; then
		if [ "$allow_none" = 1 ]; then
			echo "fetch-seed.sh: $tag is not published in this repository — no seed to build with"
			exit 0
		fi

		echo "fetch-seed.sh: $tag is not published in this repository" >&2
		exit 1
	fi

	gh release download "$tag" --pattern "*$target*" --pattern "$SeedChecksumsAsset" --dir "$work"
}

copy_local_release() {
	local archive

	[ -d "$from" ] || { echo "fetch-seed.sh: --from=$from is not a directory" >&2; exit 1; }

	archive="$from/$(seed_archive_file "$tag" "$target")"
	[ -f "$archive" ] || { echo "fetch-seed.sh: $from holds no $(basename "$archive") — no $target archive frozen as $tag" >&2; exit 1; }
	[ -f "$from/$SeedChecksumsAsset" ] || { echo "fetch-seed.sh: $from holds no $SeedChecksumsAsset" >&2; exit 1; }

	cp "$archive" "$from/$SeedChecksumsAsset" "$work"
}

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ -n "$from" ]; then
	copy_local_release
else
	download_release
fi

archive="$(find "$work" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | head -n1)"
[ -n "$archive" ] || { echo "fetch-seed.sh: $tag publishes no archive for $target" >&2; exit 1; }
[ -f "$work/$SeedChecksumsAsset" ] || { echo "fetch-seed.sh: $tag publishes no $SeedChecksumsAsset" >&2; exit 1; }

expected="$(awk -v name="$(basename "$archive")" '$2 == name || $2 == "*" name { print $1 }' "$work/$SeedChecksumsAsset")"
[ -n "$expected" ] || { echo "fetch-seed.sh: $SeedChecksumsAsset of $tag does not list $(basename "$archive")" >&2; exit 1; }
[ "$expected" = "$(sha256_of "$archive")" ] || { echo "fetch-seed.sh: $(basename "$archive") does not match $SeedChecksumsAsset of $tag" >&2; exit 1; }

mkdir -p "$work/unpacked"

case "$archive" in
	*.zip)    unzip -q "$archive" -d "$work/unpacked" ;;
	*.tar.gz) tar -xzf "$archive" -C "$work/unpacked" ;;
	*) echo "fetch-seed.sh: cannot unpack $archive" >&2; exit 1 ;;
esac

found="$(find "$work/unpacked" -type f \( -name 'maxon' -o -name 'maxon.exe' \) | head -n1)"
[ -n "$found" ] || { echo "fetch-seed.sh: no maxon binary in $tag's $target archive" >&2; exit 1; }

seed="$(maxon_downloaded_path)"
[ "$target" = "$(maxon_host_target)" ] || seed=".bootstrap/$(basename "$found")"
[ -z "$output" ] || seed="$output"

# Replaced by rename, so a failed copy never leaves a truncated seed where a working one stood.
mkdir -p "$(dirname "$seed")"
cp "$found" "$seed.partial"
chmod +x "$seed.partial"
mv -f "$seed.partial" "$seed"

echo "fetch-seed.sh: $seed is $tag ($target)"
