#!/usr/bin/env bash
# THE LOOP-VERSIONING LADDER — the two axes `unswitchInvariantGuards` has, ISOLATED.
#
#     Does the pass cost O(loops), or O(loops x function size)?
#
# `ScaleCorpus` cannot ask: no rung holds an element store inside a loop, so the pass versions ZERO
# loops at rung 0 and at rung 5 alike (`--log=ir:debug` prints `versioned 0 loop(s)` on both, with an
# identical refusal tally — those are the runtime's own loops). Its `phase:unswitchInvariantGuards`
# row therefore reads the per-function GATHER on the one growing function that holds a managed callee
# and a back edge, and nothing of the per-loop analysis or the rewrite.
#
# Usage: genunswitch.sh <loops> <pad> <versioned|refused> <outfile>
#   <loops> = THE DOUBLING KNOB: ONE function holding this many loops of the versionable shape — an
#             element store and a `get` on a parameter array, an accumulator that ESCAPES each loop
#             (so every loop takes an exit phi whose readers are rewritten).
#   <pad>   = straight-line calls appended after the loops, so the FUNCTION grows while the loop
#             count stays put. THE PRODUCT AXIS: hold <loops> and double <pad>; a per-loop cost that
#             is proportional to the function reads as a doubling here.
#   mode    = versioned | refused
#             `versioned` — every loop is admitted and copied. Every mode's program checks its own
#                           answer and exits 1 on a wrong one, so a miscompiled rung is an exit code.
#             `refused`   — the same loops each also `push`, a callee whose header effect is unknown,
#                           so every loop is refused at `scanCalls`. THE CONTROL: the per-function
#                           gather and the cheap early refusal, with no analysis past the calls and no
#                           rewrite.
#   outfile = the program to write.
#
#   e.g. genunswitch.sh 100 0 versioned temp/us-100.maxon
#        genunswitch.sh 200 0 versioned temp/us-200.maxon
#        genunswitch.sh 100 4000 versioned temp/us-100-pad4000.maxon
#
# Read `phase:unswitchInvariantGuards` out of `--metrics=<tsv>` (allocs, frees, bytes, cputicks).
#
# MEASURED 2026-09-09, `versioned`, `phase:unswitchInvariantGuards` allocations at loops = 50 / 100 /
# 200 / 400. The "before" column is this shape measured BY HAND on the pass's first cut, a binary
# that no longer exists; the "after" column is this script against the linear rewrite:
#   before  244,788 / 905,150 / 3,471,957 / 13,587,858   (x3.70 x3.84 x3.91)
#   after    37,532 /  75,399 /   148,796 /    297,058   (x2.01 x1.97 x2.00)
# and at loops = 100 with pad = 0 / 2000 / 4000: 905,150 / 1,106,332 / 1,306,950 before against
# 75,399 / 74,720 / 74,758 after; `refused` at 100 / 200 loops reads 3,135 / 5,885. The product term
# was three per-loop walks of the function: the exit-phi substitution rebuilt every op of every block
# per loop, and two columns sized by the function (the hoist numbering's canonical map, the clone's
# substitution columns) were built per loop.
set -euo pipefail

if [ "$#" -ne 4 ]; then
	echo "usage: genunswitch.sh <loops> <pad> <versioned|refused> <outfile>" >&2
	exit 2
fi

N="$1"; PAD="$2"; MODE="$3"; OUT="$4"
TRIPS=8

case "$MODE" in
	versioned|refused) ;;
	*) echo "genunswitch.sh: mode must be versioned or refused, got '$MODE'" >&2; exit 2 ;;
esac

# Sum over loop k of sum over i < TRIPS of (i + k): TRIPS(TRIPS-1)/2 per loop plus TRIPS per unit of k.
# A `refused` loop's push appends past every index it reads, so it answers the same closed form; each
# pad statement adds its k, so the pad adds PAD(PAD-1)/2. Every mode checks its own answer.
EXPECTED=$(( N * TRIPS * (TRIPS - 1) / 2 + TRIPS * N * (N - 1) / 2 + PAD * (PAD - 1) / 2 ))

{
	echo "// unswitch ladder: $N loops, $PAD pad statements, mode $MODE"
	echo "typealias Word = int(i64.min to i64.max)"
	echo "typealias WordArray = Array with Word"
	echo ""
	echo "function helper(x Word, k Word) returns Word"
	echo "	return x + k"
	echo "end 'helper'"
	echo ""
	echo "function work(a WordArray, n Word) returns Word"
	echo "	var t = 0"
	for ((k = 0; k < N; k++)); do
		echo "	for i in 0 upto n 'l$k'"
		echo "		try a.set(i, value: i + $k) otherwise panic(\"l$k: i < n = a.count()\")"
		if [ "$MODE" = "refused" ]; then
			echo "		a.push(i)"
		fi
		echo "		t = t + (try a.get(i) otherwise 0)"
		echo "	end 'l$k'"
	done
	for ((k = 0; k < PAD; k++)); do
		echo "	t = helper(t, k: $k)"
	done
	echo "	return t"
	echo "end 'work'"
	echo ""
	echo "function main() returns ExitCode"
	echo "	var a = WordArray.create()"
	echo "	a.resize($TRIPS)"
	echo "	let r = work(a, n: $TRIPS)"
	echo "	print(\"{r}\\n\")"
	echo "	if r != $EXPECTED 'wrong'"
	echo "		return 1"
	echo "	end 'wrong'"
	echo "	return 0"
	echo "end 'main'"
} > "$OUT"

echo "genunswitch.sh: wrote $MODE ladder with loops=$N pad=$PAD to $OUT"
