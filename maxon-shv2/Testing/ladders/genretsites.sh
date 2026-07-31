#!/usr/bin/env bash
# THE RANGED-`return`-SITE LADDER — the other half of `genrangesites.sh`'s axis, and the one the shared
# corpus is blind to for a SECOND reason on top of that one.
#
#     Does `insertRangeChecks` cost O(ranged return sites), or O(sites x blocks in the function)?
#
# ⭐ **WHY THIS IS A SEPARATE LADDER FROM `genrangesites.sh`.** That one drives ALIAS-NAMED sites (a
# field store through a narrow alias), which resolve their block through `guardSiteAt` /
# `buildBlockIdIndex`. A ranged `return` resolves an ENTIRELY DIFFERENT WAY — it has no recorded block,
# only a value, and `takeRetBlock` finds the block whose `ret` still returns it. Two mechanisms, two
# growth curves, and the alias ladder cannot see this one at all: no store or cast makes a function
# RETURN a narrow alias.
#
# ⚠ **AND `ScaleCorpus.maxon` CANNOT EITHER — for the reason `genrangesites.sh`'s header gives about
# guards in general, PLUS one of its own: not one generated function has a non-full ranged RETURN type.**
# `ScaleXHoldValue`, `ScaleMeasure<N>`, `ScaleElement` and the rest are all declared
# `int(i64.min to i64.max)`, and `rangeIsFull` discards a full range before a site can be recorded at
# all. So `phase:insertRangeChecks` reads a flat Δ0 for this whole path at every rung of the standing
# instrument — the CORPUS blind spot, which no amount of CPU-column coverage cures.
#
# ⭐ **WHAT IT MEASURED (A1h, 2026-07-31).** `findRetBlock` walked every `func.blockRefs` entry and read
# each terminator, ONCE PER SITE — and each guard it emits APPENDS `__rc_ok` + `__rc_panic` to the very
# set the next site's walk covers, so the k-th site scanned a set the previous k-1 had grown. On
# `onefunc` at n = 50/100/200/400, `phase:insertRangeChecks` CPU read
# 2,490,520 / 5,493,660 / 13,909,140 / 44,487,360 ticks = **x2.21 x2.53 x3.20 per DOUBLING** — a RISING
# ratio, i.e. linear + quadratic summed. **Allocations and bytes read a dead-flat x1.99 / x2.03 over the
# same span**, because a scan allocates NOTHING: this is one of the terms only the CPU column can see, so
# read that column FIRST on this ladder and the allocation column second. Cured by a per-value index
# (`buildRetBlockIndex`), after which the same span reads x1.98 x1.97 x1.93 — flat — and n=400 costs
# 14.5M ticks instead of 44.5M. The ladder is kept because it is the only thing that can show it has not
# come back.
#
# Usage: genretsites.sh <n> <mode> <outdir>
#   <n>    = NUMBER OF GUARDED `return` SITES. THE DOUBLING KNOB.
#   mode   = onefunc | spread
#            `onefunc` — all <n> `return` sites in ONE function, each behind its own `if`. This is the
#                        product axis: sites x blocks-in-this-function, and the <n> guards it emits
#                        TRIPLE that block count as they go.
#            `spread`  — the SAME <n> sites, one per function, <n> functions. THE CONTROL. Same site
#                        count, same guard count, same emitted guards; only the per-function
#                        concentration differs. If `onefunc` bends and `spread` does not, the bend is the
#                        product term and not the per-site cost.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#            ONE file, `main.maxon`, and that is not incidental: a `typealias` does not cross a file
#            boundary (a use in another file leaves it `E3062: unused typealias`), so a ranged-alias
#            ladder cannot be split into a prelude and a body the way `genrangesites.sh` is.
#
#   e.g. genretsites.sh 400 onefunc temp/rr-one-400/
#        genretsites.sh 400 spread  temp/rr-spread-400/
#
# Every site returns the PARAMETER through a narrow alias. Two properties are load-bearing:
#   * the value is a parameter, so it is UNFOLDABLE — a literal `return` is settled at compile time by
#     `needsRuntimeGuard` and emits no code, which is the blindness this ladder exists to escape;
#   * in `onefunc` every site returns the SAME parameter, so all <n> sites share ONE ValueId. That is
#     the shape a value-keyed lookup has to CONSUME rather than merely answer, and it is what an
#     ordinary wide `match` returning a ranged alias looks like.
# The program is run as well as compiled: `pick(7)` takes the 8th site and returns 7, so every rung's
# binary must exit 7 and a rung that miscompiles shows up as a wrong exit code rather than only as a time.
#
# Read it with `--metrics`, whose `phase` rows carry the counts (field 4 = allocations, 6 = bytes,
# 7 = cputicks):
#
#     maxon-shv2 build temp/rr-one-400/ -o temp/rr --metrics=temp/rr.tsv
#     grep insertRangeChecks temp/rr.tsv
#
# ⚠ The CPU column moves a few percent with turbo and cache pressure, so take the MINIMUM of a few runs
# before believing a bend of less than 2x. Against the only question a doubling ladder asks — x2 linear,
# x4 quadratic — that band has a 100% margin. And because the ladder DOUBLES, the RATIO between
# consecutive <n> IS the growth. Nothing to fit.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genretsites.sh <n> <onefunc|spread> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	onefunc|spread) ;;
	*) echo "genretsites.sh: mode must be onefunc or spread, got '$MODE'" >&2; exit 2 ;;
esac

if [ "$N" -lt 9 ]; then
	echo "genretsites.sh: <n> must be at least 9 — the run asserts pick(7) takes the 8th site" >&2
	exit 2
fi

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

{
	echo "// Generated by genretsites.sh: $N guarded \`return\` sites, mode $MODE."
	# The wide alias every value arrives as, and the narrow one every `return` is checked against.
	# `RrNarrow` must NOT be full-range, or `rangeIsFull` discards every site before it costs anything.
	echo "typealias RrWide = int(i64.min to i64.max)"
	echo "typealias RrNarrow = int(0 to 100)"
	echo ""

	if [ "$MODE" = "onefunc" ]; then
		echo "// All $N sites in ONE function, and all of ONE value: every \`return n\` names the same ValueId."
		echo "function rrPick(n RrWide) returns RrNarrow"
		for ((i = 0; i < N; i++)); do
			echo "	if n == $i 'a$i'"
			echo "		return n"
			echo "	end 'a$i'"
			echo ""
		done
		# A literal `return` closes the body: it is settled at compile time and emits no guard, so it does
		# not move the knob.
		echo "	return 0"
		echo "end 'rrPick'"
	else
		echo "// The CONTROL: the same $N sites, one per function."
		for ((i = 0; i < N; i++)); do
			echo "function rrOne$i(n RrWide) returns RrNarrow"
			echo "	if n == $i 'hit'"
			echo "		return n"
			echo "	end 'hit'"
			echo ""
			echo "	return 0"
			echo "end 'rrOne$i'"
			echo ""
		done
		# The dispatcher returns the WIDE alias, so it contributes no ranged site of its own and the knob
		# stays exactly <n>.
		echo "function rrPick(n RrWide) returns RrWide"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			echo "	acc = acc + rrOne$i(n)"
		done
		echo ""
		echo "	return acc"
		echo "end 'rrPick'"
	fi

	echo ""
	echo "function main() returns ExitCode"
	echo "	let v = rrPick(7)"
	echo ""
	echo "	return v as ExitCode"
	echo "end 'main'"
} > "$OUT/main.maxon"

echo "genretsites.sh: wrote $MODE ladder with n=$N to $OUT"
