#!/usr/bin/env bash
#
# Build the tree's compiler at `maxon-bin/.maxon/maxon`.
#
# Maxon compiles Maxon, so this needs a working compiler to start from. It takes the first of:
#
#   1. the tree binary already in the slot — the ordinary case, a self-rebuild
#   2. `.bootstrap/maxon` — a released compiler you put there yourself (see the message below)
#
# ⛔ THE COMPILE WRITES TO `.next` AND IS RENAMED INTO PLACE, because a compiler cannot overwrite its
# own running image (E6002) and because a half-written slot is a compiler that answers as though it
# were whole. The last good binary is kept at `maxon.previous`; a FAILED build leaves the slot EMPTY
# rather than reinstating it, since a stale compiler reporting as current is the failure every
# staleness refusal here exists to prevent.
#
# Usage:
#   scripts/build.sh [--from=<compiler>] [--result-json=<path>]

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh

from=""
result_json=""

for arg in "$@"; do
	case "$arg" in
		--from=*)        from="${arg#*=}" ;;
		--result-json=*) result_json="${arg#*=}" ;;
		-h|--help)       sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "build.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

tree_bin="$(maxon_compiler_path .)"
slot_stem="${tree_bin%"$MAXON_EXE_EXT"}"
next_stem="$slot_stem.next"
next_bin="$next_stem$MAXON_EXE_EXT"
prev_bin="$slot_stem.previous$MAXON_EXE_EXT"

if [ -z "$from" ]; then
	if [ -x "$tree_bin" ]; then
		from="$tree_bin"
	elif [ -x ".bootstrap/maxon$MAXON_EXE_EXT" ]; then
		from=".bootstrap/maxon$MAXON_EXE_EXT"
	else
		cat >&2 <<EOF
build.sh: no compiler to build with.

  Maxon is written in Maxon, so building it needs a Maxon compiler, and this repo contains no second
  implementation to fall back on. The slot $tree_bin is empty and there is
  no .bootstrap/maxon$MAXON_EXE_EXT.

  Download the release for your platform from

    https://github.com/maxon-lang/maxon/releases/latest

  and put its \`maxon\` binary at .bootstrap/maxon$MAXON_EXE_EXT — the BINARY ALONE, not the unpacked
  archive: the compiler finds \`stdlib/\` by walking UP from its own executable, so a released stdlib
  left beside it would be compiled instead of this tree's, silently. Then re-run this script.

  Or point it at a compiler you already have: scripts/build.sh --from=<path>
EOF
		exit 1
	fi
fi

[ -x "$from" ] || { echo "build.sh: not executable: $from" >&2; exit 1; }

# ⛔ THE OUTPUT DIRECTORY IS GITIGNORED, SO A FRESH CLONE HAS NONE. The backend writes the file and
# does not create the path to it, and the failure that follows names the missing OUTPUT rather than the
# missing directory. MEASURED on a first build in a clone: `E6003 the backend produced no file`.
mkdir -p "$(dirname "$next_bin")"

on_exit() {
	local status=$?
	[ "$status" -eq 0 ] && return 0

	echo "" >&2
	echo "build.sh: FAILED (exit $status). The slot $tree_bin is EMPTY unless the rename completed —" >&2
	echo "  nothing is reinstated automatically, so no stale compiler can answer as though it were current." >&2
	[ -e "$prev_bin" ] && echo "  The last good binary is at $prev_bin; move it back by hand if you need one now." >&2

	return 0
}

trap on_exit EXIT

# The debug sidecar is named after the binary, so a rename that left it behind would pair a fresh
# executable with a stale `.mxdbg` — a mismatched pair that reads as a matched one.
move_binary() {
	local from_path="$1" to="$2"

	rm -f "$to" "$to.mxdbg"
	mv "$from_path" "$to"
	[ -f "$from_path.mxdbg" ] && mv "$from_path.mxdbg" "$to.mxdbg"

	return 0
}

now_s() { date +%s.%N; }
elapsed_ms() { awk -v s="$1" -v e="$2" 'BEGIN { printf "%d", (e - s) * 1000 }'; }
size_of() { wc -c < "$1" | tr -d ' \t'; }

echo "=== building maxon-bin with $from"
rm -f "$next_bin" "$next_bin.mxdbg"
start="$(now_s)"
"$from" build maxon-bin -o "$next_stem"
build_ms="$(elapsed_ms "$start" "$(now_s)")"

# The slot is vacated only once the new binary exists, so a failed compile above leaves the working
# compiler in place rather than trading it for nothing.
if [ -e "$tree_bin" ]; then
	move_binary "$tree_bin" "$prev_bin"
fi
move_binary "$next_bin" "$tree_bin"

echo ""
printf 'compiler  %s  %s bytes  %s ms\n' "$tree_bin" "$(size_of "$tree_bin")" "$build_ms"

if [ -n "$result_json" ]; then
	mkdir -p "$(dirname "$result_json")"
	printf '{"compiler":{"path":"%s","durationMs":%s,"builtWith":"%s"}}\n' \
		"$tree_bin" "$build_ms" "$from" > "$result_json"
fi
