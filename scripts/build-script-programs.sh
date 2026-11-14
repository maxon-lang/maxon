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
. scripts/lib/script-programs.sh
. scripts/lib/subject-loop.sh

compiler="$(maxon_compiler_path .)"
[ -x "$compiler" ] || { echo "build-script-programs.sh: no compiler at $compiler — run \`./maxon-bin/.maxon/maxon run build\` at the repo root" >&2; exit 1; }

out="temp/build-script-programs"
rm -rf "$out"
mkdir -p "$out"

trap 'scratch_dir_on_exit $? "$out" "build-script-programs.sh" 0' EXIT

programs="$(script_program_dirs)"

if [ -z "$programs" ]; then
	echo "build-script-programs.sh: no tracked directory under scripts/ holds a .maxon file" >&2
	exit 1
fi

echo "program directories: $(printf '%s' "$programs" | tr '\n' ' ')"

SubjectScript="build-script-programs.sh"
SubjectDone="built"
SubjectRefusal="did not build"

for program in $programs; do
	subject_run "scripts/$program" "$out/$program.log" "$compiler" build "scripts/$program" --output="$out/$program"
done

subject_report
