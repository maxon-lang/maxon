#!/usr/bin/env bash
# THE MANAGED-PRIMITIVE-SITE LADDER — the two axes `inlineManagedPrimitives` has, ISOLATED.
#
#     Does the pass cost O(sites), or O(sites x block length)? And what does it cost per OP in a
#     program that holds no site at all?
#
# `ScaleCorpus` cannot answer either question, and the reason is structural rather than a knob being
# too low: every array/buffer group it generates is wrapped in `ownedSiteBlock`, i.e. an
# `if acc > 0 '<label>'`, so each group's sites land in a basic block of their own. The corpus can
# double how many sites exist and can never raise how many share ONE BLOCK — which is the only axis
# the split cost has. It is `genrangesites.sh`'s `onefunc`/`spread` pair, one pass over.
#
# Usage: genmanagedsites.sh <n> <onefunc|spread|nosites> <outdir>
#   <n>    = THE DOUBLING KNOB.
#   mode   = onefunc | spread | nosites
#            `onefunc` — <n> buffer `set`+`get` sites in ONE function, straight-line, so they share as
#                        much of one block as the language lets them. THE PRODUCT AXIS.
#            `spread`  — the SAME 2n sites, one pair per function, <n> functions. THE CONTROL. Same
#                        site count, same expansions, same emitted fast arms; only the per-block
#                        concentration differs. If `onefunc` bends and `spread` does not, the bend is
#                        the product term and not the per-site cost.
#            `nosites` — <n> straight-line statements and NOT ONE managed primitive. THE SCAN AXIS:
#                        what the pass costs for merely WALKING a program it expands nothing in.
#                        This is the mode that found something; see below.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
#   e.g. genmanagedsites.sh 256 onefunc temp/mp-one-256/
#        genmanagedsites.sh 256 spread  temp/mp-spread-256/
#        genmanagedsites.sh 512 nosites temp/mp-none-512/
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/mp-one-256/ -o temp/mp.exe --metrics=temp/mp.tsv
#     grep inlineManagedPrimitives temp/mp.tsv          # nanos, allocs, frees, bytes, cputicks
#
# ⚠ **READ THE ALLOCATION AND BYTE COLUMNS — they are exact and bit-reproducible, so a ratio off them
# is a datapoint on the first run.** And because the ladder DOUBLES, the RATIO between consecutive <n>
# IS the growth: x2 linear, x4 quadratic. Nothing to fit.
#
# ⭐⭐ **WHAT `onefunc` MEASURED, 2026-08-25 (EC1 optimizer): NOTHING BENDS, AND THE REASON IS NOT THE
# ONE `InsertRangeChecks.splitBlockInPlace`'s HEADER GIVES.** That header says this pass "splits at ONE
# position per call site and never repeats on a block it has already split — its continuation is what it
# scans next — so the quadratic that argument is about cannot arise". The second clause does not follow
# from the first: the continuation IS the rest of the block, so K sites in a block of B ops would copy
# the tail K times, Sum(B - p_k) ~ K*B/2, which is exactly the shape `splitChainEnd` exists to avoid.
# ✅ **THE MEASUREMENT STANDS WITHOUT ANY MECHANISM STORY: THE CONCENTRATED SHAPE READS ×2 ON A DOUBLING
# LADDER.** Σ(B − pₖ) with K ∝ B is quadratic, so `onefunc` would read ×4 — the way `phase:insertRangeChecks`
# read ×2.83 ×2.82 ×3.73 ×4.11 ×4.32 in BYTES on the 1,024-cast block that made `splitChainEnd` necessary.
# It reads ×1.99 ×1.99 ×2.00 in allocations and ×2.07 ×2.10 ×2.06 in bytes. `spread` is the control that
# says the shape is not simply missing the sites: it holds the SAME 2n sites at one per function and sits
# on the same curve.
#
# What follows is the READING of the lowering that explains it — a reading of the emission sites, not a
# probe, and it is written down so that the thing to re-check is named:
#   • the THREE throwing entries (`__managed_get` / `__managed_set` / `__managed_mem_set`) arrive as a
#     `tryCall` whose error flag is tested immediately, so the call is the LAST op in its block, and a
#     split at `pos + 1` there moves nothing;
#   • the one non-throwing carve (`__managed_get_unchecked`) has exactly ONE emission site in the whole
#     compiler — the `for`-in header (`Parser`, one call) — so a block holds at most one of them and its
#     tail is copied at most once;
#   • `__managed_count` is rewritten IN PLACE and carves nothing at all.
# ⇒ If that reading is ever wrong, THIS LADDER is what says so, and it says so in the only column that
# matters: `onefunc` leaves ×2.
# ⇒ MEASURED with this generator at n = 32/64/128/256, `phase:inlineManagedPrimitives` allocations, both
#   modes and both sides of the same day's change:
#     onefunc  before  11,093 / 22,041 / 43,935 / 87,717    ×1.99 ×1.99 ×2.00
#     onefunc  after    9,259 / 18,479 / 36,917 / 73,787    ×2.00 ×2.00 ×2.00
#     spread   before  13,039 / 26,001 / 51,920 / 103,764   ×1.99 ×2.00 ×2.00
#     spread   after   10,058 / 20,108 / 40,203 / 80,399    ×2.00 ×2.00 ×2.00
#   bytes agree, ×2.06–×2.11 in all four.
# ⇒ **SO THE PRODUCT TERM IS MEASURED-LINEAR-IN-PRACTICE, NOT ABSENT.** A SIXTH primitive that carves
# mid-block, or an inliner that merges a throwing call's blocks, reinstates Σ(B − pₖ) with nothing in
# the way. That is this mode's whole job: re-run it then.
#
# ⭐⭐ **WHAT `nosites` MEASURED, AND IT WAS A REAL COST.** The pass visits every op of every reachable
# function. Its classification and its outcome were two PAYLOAD-BEARING unions, and such a union
# heap-boxes EVERY case including the payload-free ones — so a `notInlinable` and a `noSite` were minted
# and dropped at each op that was neither. At n = 64/128/256/512 straight-line statements with ZERO
# expandable sites the phase read:
#     BEFORE   allocs 594 / 1,106 / 2,130 / 4,178      bytes 18,936 / 35,320 / 68,088 / 133,624
#     AFTER    allocs   6 /     6 /     6 /     6      bytes    216 /    216 /    216 /    216
# i.e. 2 allocations and 64 bytes per Std op in the program, now flat and independent of program size.
# The cure was to answer with a `BlockRef` (an int) instead of a union — see `expandAt`'s header.
# ⇒ **THIS MODE IS THE REGRESSION GUARD FOR THAT.** A flat row is the answer; a row that grows with <n>
# means a union got back onto the per-op path.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genmanagedsites.sh <n> <onefunc|spread|nosites> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	onefunc|spread|nosites) ;;
	*) echo "genmanagedsites.sh: mode must be onefunc, spread or nosites, got '$MODE'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

# ONE FILE PER RUNG, prelude and driver included. A ladder that moves ONE axis has no business also
# moving the file count, and a multi-file spelling would need an `export` on every generated function
# (E3008) plus whatever `checkUnusedExports` then makes of them — two mechanisms this measurement has
# nothing to say about, standing between the knob and the phase it is aimed at.
#
# `mpOpaque` is what makes each stored value real: a value the parser folds is settled at compile time
# and the store can vanish before this pass ever sees the site, which is the same blindness the ladder
# exists to escape. The `and 255` keeps every value inside the byte alias so the built program runs.

# How many slots each generated buffer publishes. Fixed: this ladder moves the SITE count, not the
# buffer size, and every index is taken modulo this so the program stays in bounds at any <n>.
SLOTS=64

{
	echo "// Generated by genmanagedsites.sh — $MODE ladder, n=$N."
	echo "function mpOpaque(a int) returns int"
	echo "	if a > 100 'big'"
	echo "		return a - 100"
	echo "	end 'big'"
	echo "	return a + 1"
	echo "end 'mpOpaque'"

	case "$MODE" in
	onefunc)
		echo "typealias MpByte = int(0 to u8.max)"
		echo "function mpMany(a int) returns int"
		echo "	var mm = try __ManagedMemory.create($SLOTS, elementSize: 1) otherwise return 0"
		echo "	try mm.setLength($SLOTS) otherwise return 0"
		echo "	var acc = 0"
		# ⚠ The stored value is INLINE, not bound to a `let`. <n> distinct live `let`s in one body is <n>
		# simultaneously-live values, which drives the REGISTER ALLOCATOR off a cliff long before this
		# phase says anything — `genrangesites.sh` measured the same trap. A ladder moves ONE axis.
		for ((i = 0; i < N; i++)); do
			echo "	try mm.set($((i % SLOTS)), (mpOpaque(a + $i) and 255) as MpByte) otherwise return 0"
			echo "	acc = acc + (try mm.get($((i % SLOTS))) otherwise 0)"
		done
		echo "	return acc"
		echo "end 'mpMany'"

		echo "function main() returns ExitCode"
		echo "	print(\"{mpMany(7)}\")"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
		;;
	spread)
		echo "typealias MpByte = int(0 to u8.max)"
		for ((i = 0; i < N; i++)); do
			echo "function mpOne$i(a int) returns int"
			echo "	var mm = try __ManagedMemory.create($SLOTS, elementSize: 1) otherwise return 0"
			echo "	try mm.setLength($SLOTS) otherwise return 0"
			echo "	try mm.set($((i % SLOTS)), (mpOpaque(a + $i) and 255) as MpByte) otherwise return 0"
			echo "	return try mm.get($((i % SLOTS))) otherwise 0"
			echo "end 'mpOne$i'"
		done

		echo "function main() returns ExitCode"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			echo "	acc = acc + mpOne$i(7)"
		done
		echo "	print(\"{acc}\")"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
		;;
	nosites)
		echo "function mpNone(a int) returns int"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			echo "	acc = acc + mpOpaque(a + $i)"
		done
		echo "	return acc"
		echo "end 'mpNone'"

		echo "function main() returns ExitCode"
		echo "	print(\"{mpNone(7)}\")"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
		;;
	esac
} > "$OUT/a_sites.maxon"

echo "genmanagedsites.sh: wrote $MODE ladder with n=$N to $OUT"
