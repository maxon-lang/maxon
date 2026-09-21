#!/usr/bin/env bash
#
# Does the compiler reproduce itself exactly? Build it with itself, build it again with THAT, and
# compare the two.
#
# A green suite cannot answer this: every stage shares the compiler's LOGIC, so a suite exercises the
# same behaviour whichever stage ran it. Only the BYTES of two successive self-compiles say whether
# the emitted code is stable, and a difference is a MISCOMPILE — the compiler is not a fixed point of
# itself, so which binary you hold decides what your programs become.
#
# ⚠ WHAT THIS CANNOT ANSWER. A fixpoint says the compiler is STABLE, never that it is RIGHT: every stage
# shares one implementation's logic, so a wrong answer both stages agree on is invisible here and green in
# the suite. Nothing in this tree holds a second implementation to compare against — the specs pin behaviour
# against REGRESSION, not against an independent answer — so a defect that predates the pin stays pinned.
#
# ⛔ THE TWO OUTPUTS MUST SHARE A BASENAME. On macOS the ad-hoc code-signature identifier is taken
# from the output filename, so `--output=stage2` and `--output=stage3` differ in exactly one byte for that reason
# alone — a difference that reads as a miscompile and is not one. Same name, different directories.
#
# A failing stage's own output goes to stderr where it fails, named, and its exit status is carried out
# of the script: a compiler's status is a diagnosis here, so flattening it to 1 throws away the first
# fact a reader wants. Any nonzero exit KEEPS temp/fixpoint and says so, so the two stage binaries and
# both logs outlive the run that proved they matter. `--keep` holds them when the fixpoint holds too.
#
# Usage:  scripts/fixpoint.sh [--keep]

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/scratch-dir.sh

keep=0
for arg in "$@"; do
	case "$arg" in
		--keep)    keep=1 ;;
		-h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "fixpoint.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

start_bin="$(maxon_compiler_path .)"
[ -x "$start_bin" ] || { echo "fixpoint.sh: no compiler at $start_bin — run \`maxon build\` at the repo root" >&2; exit 1; }

out="temp/fixpoint"
rm -rf "$out"
mkdir -p "$out/a" "$out/b"

trap 'scratch_dir_on_exit $? "$out" "fixpoint.sh" "$keep"' EXIT

run_stage() {
	stage="$1"
	stage_log="$2"
	shift 2

	stage_status=0
	"$@" > "$stage_log" 2>&1 || stage_status=$?

	if [ "$stage_status" -ne 0 ]; then
		printf 'fixpoint.sh: %s exited %s — its output follows\n' "$stage" "$stage_status" >&2
		cat "$stage_log" >&2
		exit "$stage_status"
	fi
}

# A stage can exit 0 and write no `--output=` file at all, and nothing downstream notices: `wc -c` and `cmp` are
# the comparison's only readers, and a failure of either inside a command substitution leaves the
# enclosing `printf` returning 0, so `set -e` never fires — two empty files report FIXPOINT HOLDS on a
# fixpoint nothing tested. The requirement is per stage because stage 2's binary is EXECUTED by stage 3
# while stage 3's is only read.
require_stage_binary() {
	stage="$1"
	binary="$2"
	needs="$3"

	fault=""

	case "$needs" in
		executable) [ -x "$binary" ] || fault="no executable binary" ;;
		readable)   [ -r "$binary" ] || fault="no readable binary" ;;
		*) echo "fixpoint.sh: require_stage_binary: unknown requirement '$needs'" >&2; exit 2 ;;
	esac

	if [ -z "$fault" ] && [ ! -s "$binary" ]; then
		fault="an empty binary"
	fi

	if [ -n "$fault" ]; then
		printf 'fixpoint.sh: %s exited 0 but left %s at %s\n' "$stage" "$fault" "$binary" >&2
		exit 1
	fi
}

echo "=== stage 2: $start_bin builds the compiler"
run_stage "stage 2" "$out/a.log" "$start_bin" build maxon-bin --output="$out/a/maxon"
a="$out/a/maxon$MAXON_EXE_EXT"

# Between the stages, never after both: a missing or non-executable stage 2 otherwise surfaces from the
# stage-3 line as exit 127/126 and is reported against stage 3.
require_stage_binary "stage 2" "$a" executable

echo "=== stage 3: that binary builds it again"
run_stage "stage 3" "$out/b.log" "$a" build maxon-bin --output="$out/b/maxon"
b="$out/b/maxon$MAXON_EXE_EXT"
require_stage_binary "stage 3" "$b" readable

printf 'stage 2  %s bytes\nstage 3  %s bytes\n' "$(wc -c < "$a" | tr -d ' ')" "$(wc -c < "$b" | tr -d ' ')"

if cmp -s "$a" "$b"; then
	echo "FIXPOINT HOLDS — byte-identical"
	exit 0
fi

echo "FIXPOINT BROKEN — $(cmp -l "$a" "$b" | wc -l | tr -d ' ') differing byte(s)" >&2
cmp "$a" "$b" >&2 || true
exit 1
