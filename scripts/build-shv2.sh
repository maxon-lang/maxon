#!/usr/bin/env bash
# build-shv2.sh — PRODUCE THIS CHECKOUT'S shv2 COMPILER, WHICH IS THE ONE shv2 EMITS.
#
# `maxon-shv2/.maxon/maxon-shv2` is the binary every script, every suite and every agent in this tree
# runs. It is a STAGE-2 compiler: shv2 compiled by shv2. The bootstrap's product is the SEED, and the
# seed's only job is to compile that one.
#
# ⭐ WHY THE EVERYDAY BINARY IS NOT THE SEED. Code behind `#if compiler(shv2)` exists only in a binary
# shv2 emitted — the parallel compile driver is exactly that — so a bootstrap-built binary is a
# DIFFERENT COMPILER from the one this tree is about, and every verdict read off it is a verdict about
# code the tree does not ship.
#
# THE TWO PHASES:
#
#   phase 1  the bootstrap compiles maxon-shv2 into the tree slot — it has no `-o` — and the product
#            is moved to `<slot>-seed`. The slot is EMPTY from there until phase 2 lands.
#   phase 2  the seed compiles maxon-shv2 with `-o <slot>.next`, and `<slot>.next` is RENAMED over
#            the slot.
#
# ⛔ NOTHING IS EVER COMPILED INTO THE LIVE SLOT, and that is the whole design. A compiler refuses to
# replace an output it cannot delete (E6002, `Compiler.discardPreviousOutput`) and a running image
# cannot be deleted on Windows — which a tree binary compiling ITSELF hits every time. Renaming over a
# running image IS allowed there, and a plain rename works on POSIX, so the swap is a rename and every
# compile writes a path nothing is holding open.
#
# ⛔ A FAILED RUN LEAVES NO BINARY IN THE SLOT. The previous one is kept at `<slot>.previous`
# (gitignored) and can be moved back by hand; it is never restored automatically, because a compiler
# silently reinstated after a failed build is the false green that `E6002` and `CompilerFreshness`
# both exist to refuse.
#
# The staging paths live in `maxon-shv2/.maxon/` and not under `temp/`: a BOOTSTRAP `spec-test` run
# deletes every `*.exe` below `temp/` recursively (`TestRunner.CleanupExecutables`), which would take
# a staged compiler with it.
#
# ⚠ ONLY PHASE 1 HOLDS THE CHECKOUT'S TREE LOCK. Phase 2 passes `-o`, and a `-o` build takes no lock
# by design — the caller owns the output (`Main.acquireBuildTreeLock`). Phase 1's lock excludes two
# overlapping runs in every ordering but one: a second run started after the first has left phase 1.
# That one fails loudly on a rename rather than silently, and both runs compile identical sources.
#
# Usage:
#   scripts/build-shv2.sh
#   scripts/build-shv2.sh --result-json=<path>   also write {seed,treeBinary}:{path,durationMs}

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
# shellcheck source=lib/host-binaries.sh
. scripts/lib/host-binaries.sh

result_json=""
for arg in "$@"; do
	case "$arg" in
		--result-json=*) result_json="${arg#--result-json=}" ;;
		-h|--help) sed -n '2,41p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "build-shv2.sh: unknown arg: $arg" >&2; exit 2 ;;
	esac
done

bootstrap="$(maxon_bootstrap_path .)"
tree_bin="$(maxon_shv2_path .)"
seed_bin="$(maxon_shv2_seed_path .)"

slot_stem="${tree_bin%"$MAXON_EXE_EXT"}"
next_stem="$slot_stem.next"
next_bin="$next_stem$MAXON_EXE_EXT"
prev_bin="$slot_stem.previous$MAXON_EXE_EXT"

if [ ! -x "$bootstrap" ]; then
	echo "build-shv2.sh: no bootstrap at $bootstrap — build it first (\`dotnet build\` in maxon-sharp/, or the MCP build tool with target=csharp)" >&2
	exit 1
fi

on_exit() {
	local status=$?
	[ "$status" -eq 0 ] && return 0

	echo "" >&2
	echo "build-shv2.sh: FAILED (exit $status). The slot $tree_bin is EMPTY unless phase 2 completed —" >&2
	echo "  nothing is reinstated automatically, so no stale compiler can answer as though it were current." >&2
	[ -e "$prev_bin" ] && echo "  The last good binary is at $prev_bin; move it back by hand if you need one now." >&2

	return 0
}

trap on_exit EXIT

# The debug sidecar is named after the binary, so a rename that left it behind would pair a fresh
# executable with a stale `.mxdbg` — a mismatched pair that reads as a matched one.
move_binary() {
	local from="$1" to="$2"

	rm -f "$to" "$to.mxdbg"
	mv "$from" "$to"
	[ -f "$from.mxdbg" ] && mv "$from.mxdbg" "$to.mxdbg"

	return 0
}

# Wall-clock milliseconds. Wall time is the honest reading here and nowhere near the scaling
# instrument: these are minutes-long single compiles, so the machine's other work moves them by far
# less than what a reader of these two numbers is asking about.
now_s() { date +%s.%N; }
elapsed_ms() { awk -v s="$1" -v e="$2" 'BEGIN { printf "%d", (e - s) * 1000 }'; }

size_of() { wc -c < "$1" | tr -d ' \t'; }

if [ -e "$tree_bin" ]; then
	move_binary "$tree_bin" "$prev_bin"
fi

echo "=== phase 1/2: the SEED — $bootstrap build maxon-shv2"
seed_start="$(now_s)"
"$bootstrap" build maxon-shv2
seed_ms="$(elapsed_ms "$seed_start" "$(now_s)")"
move_binary "$tree_bin" "$seed_bin"

echo "=== phase 2/2: the TREE BINARY — $seed_bin build maxon-shv2 -o $next_stem"
rm -f "$next_bin" "$next_bin.mxdbg"
stage2_start="$(now_s)"
"$seed_bin" build maxon-shv2 -o "$next_stem"
stage2_ms="$(elapsed_ms "$stage2_start" "$(now_s)")"
move_binary "$next_bin" "$tree_bin"

echo ""
printf 'seed         %s  %s bytes  %s ms\n' "$seed_bin" "$(size_of "$seed_bin")" "$seed_ms"
printf 'tree binary  %s  %s bytes  %s ms\n' "$tree_bin" "$(size_of "$tree_bin")" "$stage2_ms"

if [ -n "$result_json" ]; then
	mkdir -p "$(dirname "$result_json")"
	printf '{"seed":{"path":"%s","durationMs":%s},"treeBinary":{"path":"%s","durationMs":%s}}\n' \
		"$seed_bin" "$seed_ms" "$tree_bin" "$stage2_ms" > "$result_json"
fi
