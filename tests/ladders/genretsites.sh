#!/usr/bin/env bash
# THE RANGED-`return`-SITE LADDER — the other half of `genrangesites.sh`'s axis, and the one the shared
# corpus is blind to for a SECOND reason on top of that one.
#
#     Does `insertRangeChecks` cost O(ranged return sites), or O(sites x blocks in the function)?
#
# ⭐ **WHY THIS IS A SEPARATE LADDER FROM `genrangesites.sh`.** That one drives ALIAS-NAMED sites (a
# field store through a narrow alias); this one drives ranged `return`s. Both now resolve their block
# through the SAME `guardChainTail` index (`guardSiteAt` for a store's POSITION, `retBlockOf` for a
# `return`'s `ret`), but they reach it from different site records and emit through different splits
# (`splitChainEnd` vs `splitBlockInPlace`) — two code paths, two growth curves, and the alias ladder
# cannot see this one at all: no store or cast makes a function RETURN a narrow alias.
#
# ⚠ **`ScaleCorpus.maxon` COULD NOT EITHER, UNTIL A2a — and the reason it could not is worth keeping.**
# `ScaleInt`, the alias every other generated `return` declares, is
# `int(i64.min to i64.max)`, and `rangeIsFull` discards a full range before a site can be recorded at
# all, so not one generated function had a non-full ranged RETURN type and `phase:insertRangeChecks` read
# a flat Δ0 for this whole path at every rung — the CORPUS blind spot, which no amount of CPU-column
# coverage cures. The corpus's `p_rreturn` knob (`RangedReturnSitesBase`) now generates exactly this
# shape's `onefunc` mode at a DOUBLING site count, 8 at rung 0 to 256 at rung 5, so the axis is on the
# standing instrument and the cure below has something that re-checks it. THIS LADDER STAYS, for what the
# corpus knob deliberately is not: it drives n to 400+ in one function with everything else held still,
# and it carries the `spread` CONTROL the corpus does not pay for at every rung.
#
# ⭐ **WHAT IT MEASURED (A1h, 2026-07-31).** `findRetBlock` walked every `func.blockRefs` entry and read
# each terminator, ONCE PER SITE — and each guard it emits APPENDS `__rc_ok` + `__rc_panic` to the very
# set the next site's walk covers, so the k-th site scanned a set the previous k-1 had grown. On
# `onefunc` at n = 50/100/200/400, `phase:insertRangeChecks` CPU read
# 2,490,520 / 5,493,660 / 13,909,140 / 44,487,360 ticks = **x2.21 x2.53 x3.20 per DOUBLING** — a RISING
# ratio, i.e. linear + quadratic summed. **Allocations and bytes read a dead-flat x1.99 / x2.03 over the
# same span**, because a scan allocates NOTHING: this is one of the terms only the CPU column can see, so
# read that column FIRST on this ladder and the allocation column second. Cured by resolving the site's
# own block through the existing `guardChainTail` index (`retBlockOf`), after which the same span reads
# x1.96 x1.94 x2.03 — flat — and n=400 costs 15.1M ticks instead of 44.5M. The ladder is kept because it
# is the only thing that can show it has not come back.
#
# ⚠ A1h's FIRST cure was a consumed per-VALUE chain, which was equally O(1) and was REVERTED by its own
# review: it paired the k-th site with the k-th `ret` IN BLOCK ORDER, which is not the sites' order, and
# so reported the wrong `return`'s line. This ladder cannot see that — `onefunc` guards every site
# identically, so a permuted pairing emits the same code — which is exactly why the two
# `ranged-typealias.md` cases named `return-in-a-loop-body-guards-its-own-line` and
# `return-guarded-behind-a-cast-in-the-same-branch` exist beside it. A LADDER PRICES A PATH; IT DOES NOT
# CHECK IT.
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
