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
# from the output filename, so `-o stage2` and `-o stage3` differ in exactly one byte for that reason
# alone — a difference that reads as a miscompile and is not one. Same name, different directories.
#
# Usage:  scripts/fixpoint.sh [--keep]

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

keep=0
for arg in "$@"; do
	case "$arg" in
		--keep)    keep=1 ;;
		-h|--help) sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "fixpoint.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

start_bin="$(maxon_compiler_path .)"
[ -x "$start_bin" ] || { echo "fixpoint.sh: no compiler at $start_bin — run \`maxon build\` at the repo root" >&2; exit 1; }

out="temp/fixpoint"
rm -rf "$out"
mkdir -p "$out/a" "$out/b"
[ "$keep" -eq 1 ] || trap 'rm -rf "$out"' EXIT

echo "=== stage 2: $start_bin builds the compiler"
"$start_bin" build maxon-bin -o "$out/a/maxon" > "$out/a.log" 2>&1

echo "=== stage 3: that binary builds it again"
"$out/a/maxon$MAXON_EXE_EXT" build maxon-bin -o "$out/b/maxon" > "$out/b.log" 2>&1

a="$out/a/maxon$MAXON_EXE_EXT"
b="$out/b/maxon$MAXON_EXE_EXT"
printf 'stage 2  %s bytes\nstage 3  %s bytes\n' "$(wc -c < "$a" | tr -d ' ')" "$(wc -c < "$b" | tr -d ' ')"

if cmp -s "$a" "$b"; then
	echo "FIXPOINT HOLDS — byte-identical"
	exit 0
fi

echo "FIXPOINT BROKEN — $(cmp -l "$a" "$b" | wc -l | tr -d ' ') differing byte(s)" >&2
cmp "$a" "$b" >&2 || true
exit 1
