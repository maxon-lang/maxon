#!/usr/bin/env bash
# THE RANGE-CHECK-SITE LADDER — the ALIAS-NAMED guard axis, ISOLATED and with a control.
#
#     Does `insertRangeChecks` cost O(runtime guards), or O(guards x blocks in the function)?
#
# ⛔⛔ **THIS HEADER USED TO OPEN "the axis the SHARED corpus does not have, and cannot get by
# doubling", AND SAID — in bold, as a MEASUREMENT — that "the compiled rung0 binary and the compiled
# rung5 binary each contain exactly ONE range-check panic blob" and that the shared ladder "has never
# once measured the EMISSION of a guard". EVERY WORD OF THAT IS FALSE, AND WAS ALREADY FALSE THE DAY IT
# WAS WRITTEN.** Re-measured 2026-07-31 the way the original claim was — by counting the blobs in the
# compiled rung binaries, `grep -c 'Range check failed' <rung>.out.exe` — against the corpus as it stood
# before A2a: **33 at rung 0 and 1,025 at rung 5.** 32 and 1,024 of them name `ScaleDivisor`.
#
# `ScaleDivisor` is the A1 divide knob's `((acc and DivisorMask) + 1) as ScaleDivisor` — a narrow
# (`int(1 to 8)`) ALIAS-NAMED cast over a value the compiler cannot fold, one per int-divide group, ALL
# IN ONE FUNCTION (`p_divide.maxon` declares exactly one), doubling with `IntDivideBase`. That is not a
# near-miss of this ladder's shape: **it IS `onefunc`, at n = 32 rising to 1,024.** So `guardSiteAt`,
# `emitGuardAt`, `emitPanicBlock`, the compare cascade, `splitChainEnd` and the whole guard CHAIN have
# been priced by the standing instrument since A1 landed that cast, and a Δ0 there means what a Δ0
# normally means.
#
# ⇒ **HOW THE ORIGINAL SURVEY GOT IT WRONG IS THE PART TO CARRY FORWARD.** It enumerated the corpus's
#   ALIAS DECLARATIONS and asked which reached a door — a sound question, correctly answered for every
#   alias it listed. `ScaleDivisor` was simply not on the list, because the survey was run once and the
#   knob that mints it landed afterwards. A survey nobody can re-run is not a survey; that is this
#   directory's own thesis, and this file was the counter-example to it. The cheap re-run is the blob
#   count above, and it is now written down as a COMMAND rather than as a number.
#
# ⇒ **WHAT THIS LADDER IS STILL FOR, stated so it can be checked:** it moves the site count with
#   NOTHING ELSE MOVING (the corpus doubles every knob it has at once, so a bend there names a phase and
#   not a term); it carries the `spread` CONTROL, which the corpus has no counterpart to at all; and it
#   drives n past what the corpus reaches without a 32x program behind it.
#
# ⚠ **A2a ADDED THE OTHER DOOR, AND IT IS NOT THIS ONE.** `ScaleCorpus`'s `p_rreturn` knob now emits a
#   doubling count of guarded ranged `return`s (8 blobs at rung 0, 256 at rung 5, on top of the divisor
#   casts). A `return` site resolves its block through `retBlockOf` and splits with `splitBlockInPlace`,
#   so it exercises neither `splitChainEnd` nor `materializeChainTails`. Two doors, two paths.
#
# ⚠ **AND THE COST THIS LADDER IS AIMED AT IS ON THE EMISSION PATH ONLY.** `blockEndGuardSite` (now
# `guardSiteAt`) re-fetched the site's block with `IrModule.getBlockByIdIn`, which was a LINEAR SCAN over
# `func.blockRefs`; and each guard it then emits APPENDS TWO BLOCKS to that same function (`__rc_ok`,
# `__rc_panic`), so the scan the k-th guard paid was over a block set the previous k-1 guards had grown.
# THAT TERM IS GONE — the site's block now resolves through `buildBlockIdIndex`/`blockById` — and this
# ladder is kept because it can show it has NOT come back with ONE knob moving instead of all of them.
# Recording a site is O(1) and discarding a full-range one is O(1), so the corpus's many vacuous
# full-range aliases cost it nothing; what the corpus DOES exercise on this path is the divisor cast
# above, and the isolation is the difference between the two, not presence versus absence.
#
# Usage: genrangesites.sh <n> <mode> <outdir>
#   <n>    = NUMBER OF RUNTIME RANGE-CHECK SITES. THE DOUBLING KNOB.
#   mode   = onefunc | spread
#            `onefunc` — all <n> sites in ONE function, straight-line, so they share one block. This is
#                        the product axis: sites x blocks-in-this-function.
#            `spread`  — the SAME <n> sites, one per function, <n> functions. THE CONTROL. Same site
#                        count, same guard count, same emitted code; only the per-function concentration
#                        differs. If `onefunc` bends and `spread` does not, the bend is the product term
#                        and not the per-site cost.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
#   e.g. genrangesites.sh 256 onefunc temp/rs-one-256/
#        genrangesites.sh 256 spread  temp/rs-spread-256/
#
# Each site is a FIELD STORE of a NON-CONSTANT value into a field declared with a narrow ranged alias
# (`box.v = <call result> and 4095`). Non-constant is load-bearing: a value the parser folds is settled
# at compile time by `needsRuntimeGuard` and emits NO code, which is the same blindness this ladder
# exists to escape. The `and 4095` keeps every store genuinely in range so the built program runs.
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/rs-one-256/ -o temp/rs.exe --metrics=temp/rs.tsv
#     grep -P '^phase\tinsertRangeChecks' temp/rs.tsv
#
# ⚠ **READ THE ALLOCATION COLUMN FIRST — it is exact and bit-reproducible, so a ratio off it is a
# datapoint on the first run.** The CPU column (field 7) moves a few percent with turbo and cache
# pressure; against the x2-vs-x4 question that band is harmless, but confirm a CPU-only bend across
# repeats before believing it. And because the ladder DOUBLES, the RATIO between consecutive <n> IS the
# growth: x2 linear, x4 quadratic. Nothing to fit.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genrangesites.sh <n> <onefunc|spread> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	onefunc|spread) ;;
	*) echo "genrangesites.sh: mode must be onefunc or spread, got '$MODE'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

# The shared prelude: the narrow alias, the box whose field carries it, and the opaque producer that
# keeps every stored value out of the parser's constant view.
cat > "$OUT/a_prelude.maxon" <<'PRELUDE'
// Generated by genrangesites.sh — the narrow ranged alias every site in this program stores through.
typealias RcNarrow = int(0 to 4095)

type RcBox
	export var v as RcNarrow

	export static function create(v RcNarrow) returns Self
		return Self{v: v}
	end 'create'
end 'RcBox'

// Opaque to the parser's constant folder, so every value stored through `RcNarrow` needs a RUNTIME
// guard rather than being settled at compile time.
function rcOpaque(a LadderInt) returns LadderInt
	if a > 100 'big'
		return a - 100
	end 'big'
	return a + 1
end 'rcOpaque'
PRELUDE

if [ "$MODE" = "onefunc" ]; then
	{
		echo "// Generated by genrangesites.sh: $N range-check sites in ONE function."
  echo "typealias LadderInt = int(i64.min to i64.max)"
		echo "function rcMany(a LadderInt) returns LadderInt"
		echo "	var box = RcBox.create(0)"
		echo "	var acc = 0"
		# ⚠ The stored value is INLINE, not bound to a `let`. <n> distinct live `let`s in one body is <n>
		# simultaneously-live values, which drives the REGISTER ALLOCATOR off a cliff long before this
		# phase says anything (measured: n=32 panics in `splitLiveRanges`). A ladder must move ONE axis;
		# an inline store keeps every value short-lived so the axis stays "range-check sites".
		for ((i = 0; i < N; i++)); do
			echo "	box.v = rcOpaque(a + $i) and 4095"
			echo "	acc = acc + box.v"
		done
		echo "	return acc"
		echo "end 'rcMany'"
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	print(\"{rcMany(7)}\")"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
else
	{
		echo "// Generated by genrangesites.sh: $N range-check sites, ONE PER FUNCTION."
		for ((i = 0; i < N; i++)); do
			echo "function rcOne$i(a LadderInt) returns LadderInt"
			echo "	var box = RcBox.create(0)"
			echo "	let x = rcOpaque(a + $i) and 4095"
			echo "	box.v = x"
			echo "	return box.v"
			echo "end 'rcOne$i'"
		done
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			echo "	acc = acc + rcOne$i(7)"
		done
		echo "	print(\"{acc}\")"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
fi

echo "genrangesites.sh: wrote $MODE ladder with n=$N to $OUT"
