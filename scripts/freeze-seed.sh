#!/usr/bin/env bash
#
# Freeze this tree's compiler as the bootstrap seed: build it for the four native targets, package each
# like a release archive, write SHA256SUMS and the release notes, and PRINT the `gh release create`
# command that publishes them as a `seed-*` pre-release. It publishes nothing and does not move the pin:
# `scripts/seed-tag` is bumped by hand once the pre-release exists, in the commit that relies on it.
#
# ⭐ A SEED IS A COMPILER THAT ACCEPTS THE TREE IT BUILDS. `scripts/fetch-seed.sh` downloads the tag
# `scripts/seed-tag` names, and `scripts/build-from-seed.sh` builds the tree with it. Run this when the
# tree needs something the pinned seed does not know — a declaration, a language form, a rule — and a
# `scripts/seed-shim/` patch is no longer the right answer.
#
# ⛔ THE SEED IS THE BINARY ALONE. Each archive holds one directory with `maxon` (`maxon.exe` for
# x64-windows) and nothing else: the compiler resolves `stdlib/` and `runtime/` by walking up from its own
# executable, and `fetch-seed.sh` copies only the binary out, so the tree being built is the tree whose
# sources are read.
#
# ⛔ THE COMPILER THAT RUNS THIS MUST BE BUILT FROM THE COMMIT IT FREEZES: the slot at
# `maxon-bin/.maxon/` after `maxon run build`. The seed is whatever that compiler writes for each target,
# so a slot older than a source it compiles freezes a compiler the tree no longer describes. This script
# refuses a slot older than any source under `maxon-bin/`, `stdlib/` or `runtime/` (prose is not source), and refuses an
# uncommitted change under those directories, because the tag is cut at `HEAD` and a seed built from
# more than `HEAD` is not reproducible from it.
#
# ⚠ THE BINARIES ARE BUILT UNDER THEIR FINAL NAME. `MachOWriter` bakes the output file name into the
# macOS code-signature identifier, so a post-build rename leaves a signature naming a file that no longer
# exists. Each target is built with `maxon run build --target=<t> --output=<stage>/maxon`, the way
# `scripts/release.sh --package --target=` cross-builds, so the version it reports is the git-derived one.
#
# Usage:
#   scripts/freeze-seed.sh                       # tag seed-<today>
#   scripts/freeze-seed.sh --date=2026-10-14     # tag seed-2026-10-14
#
# Writes under `temp/seed/`: `dist/` (the four archives, SHA256SUMS, NOTES.md), `stage/` (scratch),
# `build-<target>.log` and `publish.txt` (the command this script prints).

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/archive.sh

SeedTagPrefix="seed"
SeedTargets="x64-windows x64-linux arm64-macos arm64-linux"
CompilerSourceDirectories="maxon-bin stdlib runtime"
WorkDirectory="temp/seed"
DistDirectory="$WorkDirectory/dist"
StageDirectory="$WorkDirectory/stage"
NotesFile="$DistDirectory/NOTES.md"
ChecksumsFile="$DistDirectory/SHA256SUMS"
PublishFile="$WorkDirectory/publish.txt"
BuildNice=10

date_stamp="$(date +%Y-%m-%d)"

for argument in "$@"; do
	case "$argument" in
		--date=[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]) date_stamp="${argument#--date=}" ;;
		*) echo "freeze-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

tag="$SeedTagPrefix-$date_stamp"
maxon="$(maxon_compiler_path .)"
commit="$(git rev-parse HEAD)"

[ -x "$maxon" ] || { echo "freeze-seed.sh: no compiler at $maxon — run: ./maxon-bin/.maxon/maxon run build" >&2; exit 1; }
command -v nice >/dev/null 2>&1 || { echo "freeze-seed.sh: nice is not on PATH" >&2; exit 1; }

dirty="$(git status --porcelain -- $CompilerSourceDirectories ':!*.md')"
[ -z "$dirty" ] || { echo "freeze-seed.sh: uncommitted changes under $CompilerSourceDirectories — the seed is cut at HEAD:" >&2; echo "$dirty" >&2; exit 1; }

newer="$(find $CompilerSourceDirectories -path '*/.maxon' -prune -o -type f ! -name '*.md' -newer "$maxon" -print | head -n1)"
[ -z "$newer" ] || { echo "freeze-seed.sh: $newer is newer than $maxon — rebuild it first: ./maxon-bin/.maxon/maxon run build" >&2; exit 1; }

rm -rf "$DistDirectory" "$StageDirectory"
mkdir -p "$DistDirectory" "$StageDirectory"

archive_name() { printf 'maxon-seed-%s-%s' "$date_stamp" "$1"; }

seed_binary_name() {
	case "$1" in
		*-windows) printf 'maxon.exe' ;;
		*) printf 'maxon' ;;
	esac
}

freeze_one() {
	local target="$1"
	local name stage binary archive log

	name="$(archive_name "$target")"
	stage="$StageDirectory/$name"
	binary="$stage/$(seed_binary_name "$target")"
	log="$WorkDirectory/build-$target.log"

	mkdir -p "$stage"
	echo "freeze-seed.sh: building $target (log: $log)"
	nice -n "$BuildNice" "$maxon" run build --target="$target" --output="$binary" > "$log" 2>&1 \
		|| { echo "freeze-seed.sh: the $target build failed — see $log" >&2; return 1; }
	[ -f "$binary" ] || { echo "freeze-seed.sh: the $target build wrote no $binary — see $log" >&2; return 1; }

	find "$stage" -maxdepth 1 -type f -name '*.mxdbg' -delete
	chmod +x "$binary"

	case "$target" in
		*-windows)
			archive="$DistDirectory/$name.zip"
			make_zip "$archive" "$stage" ;;
		*)
			archive="$DistDirectory/$name.tar.gz"
			make_tar "$archive" "$stage" ;;
	esac

	[ -s "$archive" ] || { echo "freeze-seed.sh: packaging $target wrote no $archive" >&2; return 1; }
	echo "freeze-seed.sh: wrote $archive"
}

for target in $SeedTargets; do
	freeze_one "$target"
done

( cd "$DistDirectory" && sha256sum maxon-seed-*.zip maxon-seed-*.tar.gz > "$(basename "$ChecksumsFile")" )

host_target="$(maxon_host_target)"
host_binary="$StageDirectory/$(archive_name "$host_target")/$(seed_binary_name "$host_target")"
seed_version="$("$host_binary" version)"

{
	printf 'Frozen bootstrap seed, not a release. Built from commit %s by `scripts/freeze-seed.sh`.\n\n' "$commit"
	printf 'Each archive holds the compiler binary alone, for one target. `scripts/seed-tag` pins this tag and\n'
	printf '`scripts/fetch-seed.sh` downloads it; CI and `scripts/build-from-seed.sh` build the tree with it.\n'
	printf 'It is a pre-release so that `releases/latest`, which the installers and `maxon upgrade` follow, never\n'
	printf 'resolves to it. Never move this tag or replace an asset: the pin means exactly these bytes.\n\n'
	printf 'Compiler: %s\n\n' "$seed_version"
	printf '`SHA256SUMS` lists the archives.\n'
} > "$NotesFile"

assets="$(find "$DistDirectory" -maxdepth 1 -type f \( -name '*.zip' -o -name '*.tar.gz' \) | LC_ALL=C sort | tr '\n' ' ')"

{
	printf 'gh release create %s --prerelease --target %s --title "Maxon bootstrap seed %s" --notes-file %s %s%s\n' \
		"$tag" "$commit" "$date_stamp" "$NotesFile" "$assets" "$ChecksumsFile"
} > "$PublishFile"

echo
echo "freeze-seed.sh: $tag is ready and NOT published. Compiler: $seed_version"
sed 's/^/  /' "$ChecksumsFile"
echo
echo "To publish (after $commit is on the remote, since --target names it):"
sed 's/^/  /' "$PublishFile"
echo
echo "Then pin it, in the commit that relies on it:"
echo "  printf '%s\\n' $tag > scripts/seed-tag"
