#!/usr/bin/env bash
#
# Build every test corpus's test binary and run none of them, so a corpus no CI step runs still fails
# the run that stops it compiling. A corpus is a tracked directory directly under `tests/` holding a
# `.maxtest`; the set is derived so a new corpus is built without anyone listing it. A failure keeps
# `temp/build-test-corpora/` with each corpus's log.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/scratch-dir.sh

compiler="$(maxon_compiler_path .)"
[ -x "$compiler" ] || { echo "build-test-corpora.sh: no compiler at $compiler — run \`./maxon-bin/.maxon/maxon run build\` at the repo root" >&2; exit 1; }

out="temp/build-test-corpora"
rm -rf "$out"
mkdir -p "$out"

trap 'scratch_dir_on_exit $? "$out" "build-test-corpora.sh" 0' EXIT

tracked_tests="$(git ls-files -- tests)"
corpora="$(printf '%s\n' "$tracked_tests" | sed -n 's|^tests/\([^/]*\)/[^/]*\.maxtest$|\1|p' | sort -u)"

if [ -z "$corpora" ]; then
	echo "build-test-corpora.sh: no tracked directory under tests/ holds a .maxtest file" >&2
	exit 1
fi

echo "corpora: $(printf '%s' "$corpora" | tr '\n' ' ')"

for corpus in $corpora; do
	log="$out/$corpus.log"
	build_status=0
	"$compiler" test "tests/$corpus" --list --build > "$log" 2>&1 || build_status=$?

	if [ "$build_status" -ne 0 ]; then
		printf 'build-test-corpora.sh: tests/%s did not build (exit %s) — its output follows\n' "$corpus" "$build_status" >&2
		cat "$log" >&2
		exit "$build_status"
	fi

	echo "built tests/$corpus"
done
