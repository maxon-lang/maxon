#!/usr/bin/env bash
#
# Build every Maxon program directory under `scripts/` and run none of them, so a script nobody runs in
# CI still fails the run that stops it compiling. A program directory is a tracked directory directly
# under `scripts/` holding a `.maxon` file; the set is derived so a new one is built without anyone
# listing it. A directory holding a `compile-all.sh` builds its own programs and is left to that script.
# Every directory is built even after one fails, so one run names all of them. A failure keeps
# `temp/build-script-programs/` with each directory's log.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/scratch-dir.sh

compiler="$(maxon_compiler_path .)"
[ -x "$compiler" ] || { echo "build-script-programs.sh: no compiler at $compiler — run \`./maxon-bin/.maxon/maxon run build\` at the repo root" >&2; exit 1; }

out="temp/build-script-programs"
rm -rf "$out"
mkdir -p "$out"

trap 'scratch_dir_on_exit $? "$out" "build-script-programs.sh" 0' EXIT

tracked_scripts="$(git ls-files -- scripts)"
program_dirs="$(printf '%s\n' "$tracked_scripts" | sed -n 's|^scripts/\([^/]*\)/.*\.maxon$|\1|p' | sort -u)"
self_building_dirs="$(printf '%s\n' "$tracked_scripts" | sed -n 's|^scripts/\([^/]*\)/compile-all\.sh$|\1|p' | sort -u)"
programs="$(comm -23 <(printf '%s\n' "$program_dirs") <(printf '%s\n' "$self_building_dirs"))"

if [ -z "$programs" ]; then
	echo "build-script-programs.sh: no tracked directory under scripts/ holds a .maxon file" >&2
	exit 1
fi

echo "program directories: $(printf '%s' "$programs" | tr '\n' ' ')"

failed=""

for program in $programs; do
	log="$out/$program.log"
	build_status=0
	"$compiler" build "scripts/$program" --output="$out/$program" > "$log" 2>&1 || build_status=$?

	if [ "$build_status" -ne 0 ]; then
		printf 'build-script-programs.sh: scripts/%s did not build (exit %s) — its output follows\n' "$program" "$build_status" >&2
		cat "$log" >&2
		failed="$failed $program"
		continue
	fi

	echo "built scripts/$program"
done

if [ -n "$failed" ]; then
	echo "build-script-programs.sh: did not build:$failed" >&2
	exit 1
fi
