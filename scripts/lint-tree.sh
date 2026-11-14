#!/usr/bin/env bash
#
# Lint every first-party Maxon program, test corpus and manifest that CI compiles, because `maxon build`,
# `execute`, `run` and `test` run none of the lint rules (a discarded or dropped result, an unused export,
# an unused unexported declaration), so compiling gates none of them. `maxon lint` runs the front end and
# every check and writes nothing.
#
#   scripts/lint-tree.sh [<group>]
#
# With no group every group below is linted; with one, only that group, so a developer can lint `maxon-bin`
# alone. A group is derived from the tracked files, so a new subject is linted without anyone listing it:
#
#   maxon-bin         the compiler's own directory.
#   script-programs   each program directory `scripts/build-script-programs.sh` builds.
#   multicore-stress  each program `scripts/multicore-stress/compile-all.sh` builds, linted as one program
#                     with the preludes it is compiled with, in the order it is built with them.
#   test-corpora      each corpus `scripts/run-test-corpora.sh` can run, found the way it finds them and
#                     linted with `--tests`, as `maxon test` compiles it, whatever job or lane it declares.
#   manifests         each tracked `.maxproj` and `.maxtasks` file outside the fixture directories,
#                     linted as its runner compiles it.
#
# Every subject is linted even after one fails, so one run names all of them. A failure keeps
# `temp/lint-tree/` with each subject's log.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/scratch-dir.sh
. scripts/lib/script-programs.sh
. scripts/lib/subject-loop.sh
. scripts/lib/test-corpora.sh
. scripts/lib/word-lists.sh
. scripts/multicore-stress/lib.sh

groups="maxon-bin script-programs multicore-stress test-corpora manifests"
selected="$groups"

if [ "$#" -gt 1 ]; then
	echo "usage: lint-tree.sh [<group>] — one of: $groups" >&2
	exit 1
fi

if [ "$#" -eq 1 ]; then
	list_has "$groups" "$1" || { echo "lint-tree.sh: '$1' is not a group — one of: $groups" >&2; exit 1; }
	selected="$1"
fi

compiler="$(maxon_compiler_path .)"
[ -x "$compiler" ] || { echo "lint-tree.sh: no compiler at $compiler — run \`./maxon-bin/.maxon/maxon run build\` at the repo root" >&2; exit 1; }

out="temp/lint-tree"
rm -rf "$out"
mkdir -p "$out"

trap 'scratch_dir_on_exit $? "$out" "lint-tree.sh" 0' EXIT

SubjectScript="lint-tree.sh"
SubjectDone="linted"
SubjectRefusal="did not lint clean"

group_subjects() {
	case "$1" in
		maxon-bin)
			printf '%s\n' "maxon-bin" ;;
		script-programs)
			script_program_dirs | sed 's|^|scripts/|' ;;
		multicore-stress)
			torture_programs | sed 's|^\(.*\)$|scripts/multicore-stress/\1.maxon|' ;;
		test-corpora)
			test_corpora_discover | sed 's|^|tests/|' ;;
		manifests)
			git ls-files | grep -E '\.(maxproj|maxtasks)$' | grep -Ev '(^|/)(test-)?fixtures/' ;;
		*)
			echo "lint-tree.sh: no derivation for group '$1'" >&2
			return 1 ;;
	esac
}

subject_arguments() {
	local group="$1" subject="$2" prelude

	if [ "$group" = "test-corpora" ]; then
		printf '%s\n' "--tests"
	fi

	printf '%s\n' "$subject"

	if [ "$group" = "multicore-stress" ]; then
		for prelude in $(program_preludes "$(basename "$subject" .maxon)"); do
			printf '%s\n' "${prelude#"$repo_root"/}"
		done
	fi
}

for group in $selected; do
	subjects="$(group_subjects "$group")"

	if [ -z "$subjects" ]; then
		echo "lint-tree.sh: the $group group derived no subject" >&2
		exit 1
	fi

	for subject in $subjects; do
		arguments="$(subject_arguments "$group" "$subject")"

		subject_run "$subject" "$out/$(printf '%s' "$subject" | tr '/' '-').log" "$compiler" lint $arguments
	done
done

subject_report
