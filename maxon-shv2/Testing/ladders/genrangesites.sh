#!/usr/bin/env bash
# THE RANGE-CHECK-SITE LADDER — the axis the SHARED corpus does not have, and cannot get by doubling.
#
#     Does `insertRangeChecks` cost O(runtime guards), or O(guards x blocks in the function)?
#
# ⭐ **WHY THE SHARED LADDER CANNOT ASK IT, AND THE REASON IS THE HARD KIND OF BLINDNESS.**
# `Testing/ScaleCorpus.maxon` DOES declare narrow ranged typealiases — `ScaleCoverNarrow = int(0 to
# 4095)`, `ScaleWide = int(0 to u64.max)`, and 4->128 `ScaleXElem<N> = int(0 to 10<N>)` that double
# with the rung — so a reader checking the manifest for "does the corpus emit ranged aliases at all"
# gets YES and stops. But NOT ONE of them reaches a position that emits a guard:
#
#   * `ScaleXElem<N>` is only ever a generic TYPE ARGUMENT (`ScaleXBox with ScaleXElem<N>`). The
#     declared type of the field and the parameter it flows through is the type PARAMETER `T`, which
#     is opaque; the alias is erased before any door sees it. 128 narrow aliases, zero doors.
#   * every alias that DOES reach a door — `ScaleXHoldValue`, `ScaleMeasure<N>`, `ScaleElement`,
#     `ScaleNamedValue`, `ScaleRowValue`, `ScaleSigned`, `ScaleDispMeasure`, `ScaleGenElement` — is
#     declared `int(i64.min to i64.max)`, i.e. FULL RANGE, which `needsRuntimeGuard` discards at
#     `rangeIsFull` before it can emit anything.
#   * `ScaleCoverNarrow` is the one narrow alias at a real door, used at exactly ONE `as` cast
#     (`h_cover.maxon`), and that one site does not double with the rung.
#
# ⇒ **MEASURED: the compiled rung0 binary and the compiled rung5 binary each contain exactly ONE
#   range-check panic blob, the same one.** A 32x corpus emits the same single guard. So every column
#   of the shared instrument — allocations, bytes AND CPU — reads the RECORDING of sites that are then
#   discarded as vacuous, and has never once measured the EMISSION of a guard. That is the corpus
#   blindness `ScaleCorpus`'s own manifest warns about, in its subtler form: the construct is present,
#   the KNOB THAT MAKES IT COST ANYTHING is not.
#
# ⚠ **AND THE COST THIS LADDER IS AIMED AT IS ON THE EMISSION PATH ONLY.** `blockEndGuardSite` (now `guardSiteAt`) re-fetched
# the site's block with `IrModule.getBlockByIdIn`, which was a LINEAR SCAN over `func.blockRefs`; and each
# guard it then emits APPENDS TWO BLOCKS to that same function (`__rc_ok`, `__rc_panic`), so the scan the
# k-th guard paid was over a block set the previous k-1 guards had grown. THAT TERM IS GONE — the site's
# block now resolves through `buildBlockIdIndex`/`blockById` — and the ladder is kept because it is the
# only thing that can show it has NOT come back. Recording a site is O(1) and discarding a full-range one
# is O(1) — which is the whole of what the shared ladder exercises.
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
function rcOpaque(a int) returns int
	if a > 100 'big'
		return a - 100
	end 'big'
	return a + 1
end 'rcOpaque'
PRELUDE

if [ "$MODE" = "onefunc" ]; then
	{
		echo "// Generated by genrangesites.sh: $N range-check sites in ONE function."
		echo "function rcMany(a int) returns int"
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
			echo "function rcOne$i(a int) returns int"
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
