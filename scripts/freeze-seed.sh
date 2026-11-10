#!/usr/bin/env bash
#
# Freeze this tree's compiler as a bootstrap seed: build it for the four native targets, package each
# like a release archive, and write SHA256SUMS and the release notes. It publishes nothing and does not
# move the pin: `scripts/pin-seed.sh` runs it, then proves, publishes and pins what it wrote.
#
# ⭐ A SEED IS A COMPILER THAT ACCEPTS THE TREE IT BUILDS. `scripts/fetch-seed.sh` downloads the tag
# `scripts/seed-tag` names, and `scripts/build-from-seed.sh` builds the tree with it.
#
# ⛔ THE SEED IS THE BINARY ALONE. Each archive holds one directory with `maxon` (`maxon.exe` for
# x64-windows) and nothing else: the compiler resolves `stdlib/` and `runtime/` by walking up from its own
# executable, and `fetch-seed.sh` copies only the binary out, so the tree being built is the tree whose
# sources are read.
#
# ⛔ THE COMPILER THAT RUNS THIS MUST BE BUILT FROM THE COMMIT IT FREEZES: the slot at
# `maxon-bin/.maxon/` after `maxon run build` or `scripts/build-from-seed.sh`. The seed is whatever
# that compiler writes for each target, so a slot older than a source it compiles freezes a compiler
# the tree no longer describes. This script refuses a slot older than any source under `maxon-bin/`,
# `stdlib/` or `runtime/` (prose is not source), and refuses an uncommitted change under those
# directories, because the tag is cut at `HEAD` and a seed built from more than `HEAD` is not
# reproducible from it.
#
# ⚠ THE BINARIES ARE BUILT UNDER THEIR FINAL NAME. `MachOWriter` bakes the output file name into the
# macOS code-signature identifier, so a post-build rename leaves a signature naming a file that no longer
# exists. Each target is built with `maxon run build --target=<t> --output=<stage>/maxon`, the way
# `scripts/release.sh --package --target=` cross-builds, so the version it reports is the git-derived one.
#
# Usage:
#   scripts/freeze-seed.sh                       # tag seed-<today>-<first 12 of HEAD>
#   scripts/freeze-seed.sh --date=2026-10-14     # tag seed-2026-10-14-<first 12 of HEAD>
#
# Writes under `temp/seed/`: `dist/` (the four archives, SHA256SUMS, NOTES.md), `stage/` (scratch),
# and `build-<target>.log`.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/archive.sh
. scripts/lib/checksums.sh
. scripts/lib/seed.sh

CompilerSourceDirectories="maxon-bin stdlib runtime"
StageDirectory="$SeedWorkDirectory/stage"
BuildNice=10

date_stamp="$(date +"$SeedDateFormat")"

for argument in "$@"; do
	case "$argument" in
		--date=$SeedDatePattern) date_stamp="${argument#--date=}" ;;
		*) echo "freeze-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

maxon="$(maxon_compiler_path .)"
commit="$(git rev-parse HEAD)"
tag="$(seed_tag "$date_stamp" "$commit")"

[ -x "$maxon" ] || { echo "freeze-seed.sh: no compiler at $maxon — run: ./maxon-bin/.maxon/maxon run build" >&2; exit 1; }
command -v nice >/dev/null 2>&1 || { echo "freeze-seed.sh: nice is not on PATH" >&2; exit 1; }

dirty="$(git status --porcelain -- $CompilerSourceDirectories ':!*.md')"
[ -z "$dirty" ] || { echo "freeze-seed.sh: uncommitted changes under $CompilerSourceDirectories — the seed is cut at HEAD:" >&2; echo "$dirty" >&2; exit 1; }

newer="$(find $CompilerSourceDirectories -path '*/.maxon' -prune -o -type f ! -name '*.md' -newer "$maxon" -print | head -n1)"
[ -z "$newer" ] || { echo "freeze-seed.sh: $newer is newer than $maxon — rebuild it first: ./maxon-bin/.maxon/maxon run build" >&2; exit 1; }

rm -rf "$SeedDistDirectory" "$StageDirectory"
mkdir -p "$SeedDistDirectory" "$StageDirectory"

freeze_one() {
	local target="$1"
	local stage_name exe_ext archive_name stage binary archive log

	stage_name="$(maxon_archive_stem "$tag" "$target")"
	exe_ext="$(maxon_target_exe_ext "$target")"
	archive_name="$(maxon_archive_file "$tag" "$target")"
	stage="$StageDirectory/$stage_name"
	binary="$stage/maxon$exe_ext"
	archive="$SeedDistDirectory/$archive_name"
	log="$SeedWorkDirectory/build-$target.log"

	mkdir -p "$stage"
	echo "freeze-seed.sh: building $target (log: $log)"
	nice -n "$BuildNice" "$maxon" run build --target="$target" --output="$binary" > "$log" 2>&1 \
		|| { echo "freeze-seed.sh: the $target build failed — see $log" >&2; return 1; }
	[ -f "$binary" ] || { echo "freeze-seed.sh: the $target build wrote no $binary — see $log" >&2; return 1; }

	find "$stage" -maxdepth 1 -type f -name '*.mxdbg' -delete
	chmod +x "$binary"

	make_archive "$archive" "$stage"

	[ -s "$archive" ] || { echo "freeze-seed.sh: packaging $target wrote no $archive" >&2; return 1; }
	echo "freeze-seed.sh: wrote $archive"
}

archives=()

for target in $MaxonTargets; do
	freeze_one "$target"
	archive_name="$(maxon_archive_file "$tag" "$target")"
	archives+=("$archive_name")
done

write_checksums "$SeedDistDirectory" "${archives[@]}"

host_target="$(maxon_host_target)"
host_stage_name="$(maxon_archive_stem "$tag" "$host_target")"
host_exe_ext="$(maxon_target_exe_ext "$host_target")"
host_binary="$StageDirectory/$host_stage_name/maxon$host_exe_ext"
seed_version="$("$host_binary" version)"

{
	printf 'Frozen bootstrap seed, not a release. Built from commit %s by `scripts/freeze-seed.sh`.\n\n' "$commit"
	printf 'Each archive holds the compiler binary alone, for one target. `scripts/seed-tag` pins this tag and\n'
	printf '`scripts/fetch-seed.sh` downloads it; CI and `scripts/build-from-seed.sh` build the tree with it.\n'
	printf 'It is a pre-release so that `releases/latest`, which the installers and `maxon upgrade` follow, never\n'
	printf 'resolves to it. Never move this tag or replace an asset: the pin means exactly these bytes.\n\n'
	printf 'Compiler: %s\n\n' "$seed_version"
	printf '`%s` lists the archives.\n' "$ChecksumsAsset"
} > "$SeedNotesFile"

echo
echo "freeze-seed.sh: $tag is ready and NOT published. Compiler: $seed_version"
sed 's/^/  /' "$SeedChecksumsFile"
