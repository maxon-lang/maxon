#!/usr/bin/env bash
# THE LEAF-INLINE-SITE LADDER — the two axes `inlineLeaves` has, ISOLATED.
#
#     Does the pass cost O(sites), or O(sites × block length)? And what does it cost per OP in a
#     program it inlines nothing in?
#
# `ScaleCorpus` cannot answer either, and — as with `genmanagedsites.sh`, which this is modelled on —
# the reason is structural rather than a knob set too low. The corpus's inlinable leaves are
# `scaleOpaqueCore` (called once, from `scaleOpaque`) and `callChain`'s link 0 (called once, from link
# 1); its many-functions knob doubles how many SITES exist and can never raise how many share ONE
# BLOCK, which is the only axis the splice's split cost has.
#
# ⭐ **THE PRODUCT TERM THIS EXISTS TO REFUTE.** `LeafInliner.inlineSitesIn` claims linearity in K by
# splitting a block's K sites in REVERSE order: the k-th split moves only the ops between site k and
# site k+1, so the total moved is B − p₀ whatever K is. Split FORWARD, each continuation would take the
# whole remaining tail and the total would be Σ(B − pₖ) ≈ K·B/2 — and in `onefunc` K ∝ B, so a forward
# split reads ×4 on a doubling ladder where the backward one reads ×2. Nothing else in the pass has a
# product term to hide: eligibility is one module walk memoised per callee, and a splice's own work is
# bounded by the callee's ≤ `MaxInlinedLeafOps` body.
#
# Usage: genleafsites.sh <n> <onefunc|spread|nosites|guardone|guardspread|pressure> <outdir>
#   <n>    = THE DOUBLING KNOB: how many CALL SITES into the tiny leaf.
#   mode   = onefunc | spread | nosites | guardone | guardspread | pressure
#            `onefunc`     — <n> calls to a 4-op pure leaf in ONE straight-line block of one function.
#                            THE PRODUCT AXIS: K sites sharing one block of B ≈ 2K ops.
#            `spread`      — the SAME <n> sites, one per `if` body, so <n> blocks hold one site each.
#                            THE CONTROL. Same sites, same splices, same copied bodies; only the
#                            per-block concentration differs. If `onefunc` bends and `spread` does not,
#                            the bend is the split's product term and not the per-site cost.
#            `nosites`     — the same <n> calls with the callee made NON-INLINABLE by a `throws`
#                            clause, which also makes every site a `tryCall` the pass never rewrites.
#                            THE SCAN AXIS: what the pass costs for merely WALKING a program it inlines
#                            nothing in. This is the mode that catches a union boxed per op —
#                            `genmanagedsites.sh nosites` found exactly that in `inlineManagedPrimitives`
#                            (2 allocations and 64 bytes per Std op in the program).
#            `guardone`    — `onefunc` with a `regMaskContains`-shaped leaf: an `int(0 to 63)` parameter,
#                            so `insertRangeChecks` gives the callee an entry guard whose PANIC block the
#                            splice drops and redirects. THE SLOW-ARM AXIS: one extra block, one extra
#                            call op and one extra edge PER SITE. Linear if the redirect is per-splice.
#            `guardspread` — the same guarded sites at one per block. The control for `guardone`.
#            `pressure`    — ⭐⭐ NOT A LADDER FOR THIS PASS AT ALL. <n> cross-call values all live in
#                            ONE block (`genwidelive.sh sum`'s shape, the splitter's Θ(N)×Θ(N) region),
#                            with the opaque callee made STRUCTURALLY NON-INLINABLE — it is a wrapper
#                            over a `throws` body, so it holds a `tryCall` and `scanLeafBody` reads it
#                            as `notALeaf`. ZERO splices happen anywhere in the program.
#                            ⇒ **THE A/B CONTROL FOR THE COMPILER'S OWN EMITTED CODE.** Two compilers
#                            that differ by this pass get a BIT-IDENTICAL `regalloc` input from it
#                            (assert it: their `regalloc` ALLOCATION counts must match to the digit),
#                            so the `regalloc` CPU-tick ratio between them is the quality of the
#                            REGISTER ALLOCATOR'S OWN CODE and nothing else. That is the one question a
#                            self-compile A/B cannot answer, because there the pass changes regalloc's
#                            input at the same time as it changes regalloc's code — and it is exactly
#                            where `regMaskContains` (21% of the EC5-era profile) lives.
#                            ⚠ `genwidelive.sh`'s own `scaleOpaque` is `return x` behind an
#                            `int(0 to 100000000)` guard — a leaf this pass INLINES, which flattens the
#                            shape the ladder exists to make. That generator cannot be used for this.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
#   e.g. genleafsites.sh 256 onefunc temp/leaf-one-256/
#        genleafsites.sh 256 spread  temp/leaf-spread-256/
#        genleafsites.sh 512 nosites temp/leaf-none-512/
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/leaf-one-256/ -o temp/leaf.exe --metrics=temp/leaf.tsv
#     grep inlineLeaves temp/leaf.tsv          # nanos, allocs, frees, bytes, cputicks
#
# and confirm the sites were really taken with `--log=ir:debug`, which prints the pass's own census
# (`inlineLeaves: N site(s) inlined, …`). ⚠ **A ladder whose site count is not what <n> says measures
# nothing** — `nosites` must read 0 and `onefunc` must read <n>.
#
# ⚠ **READ THE ALLOCATION AND BYTE COLUMNS — they are exact and bit-reproducible, so a ratio off them
# is a datapoint on the first run.** And because the ladder DOUBLES, the RATIO between consecutive <n>
# IS the growth: ×2 linear, ×4 quadratic. Nothing to fit.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genleafsites.sh <n> <onefunc|spread|nosites|guardone|guardspread> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	onefunc|spread|nosites|guardone|guardspread|pressure) ;;
	*) echo "genleafsites.sh: mode must be onefunc, spread, nosites, guardone, guardspread or pressure, got '$MODE'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

# ONE FILE PER RUNG, prelude and driver included — `genmanagedsites.sh`'s reason exactly: a multi-file
# spelling would need an `export` on every generated function (E3008) plus whatever `checkUnusedExports`
# then makes of them, which is two mechanisms this measurement has nothing to say about.
#
# The leaf's second argument is the LOOP-FREE varying part, so no two sites are the same call; `a` comes
# from `main` and is not foldable, which keeps the call a real `call` op at the Std tier — a site the
# parser folded away is the blindness this ladder exists to escape.

# The `int(0 to 63)` alias the guarded modes narrow their second parameter to. Its entry guard is the
# whole point: it is the shape `regMaskContains` has, and the panic block it ends in is what the splice
# redirects to a slow arm.
GUARD_ALIAS="LeafReg"

{
	echo "// Generated by genleafsites.sh — $MODE ladder, n=$N."
  echo "typealias LadderInt = int(i64.min to i64.max)"

	case "$MODE" in
	onefunc|spread)
		echo "function leafPure(a LadderInt, b LadderInt) returns LadderInt"
		echo "	return ((a + b) * 3) - 1"
		echo "end 'leafPure'"
		;;
	nosites)
		# ⚠ THE CALLEE IS REFUSED BY ITS SIGNATURE, NOT BY ITS SIZE — a `throws` body exits through
		# `errorReturn`, which `scanLeafBody` reads as `notALeaf`, and its call sites are `tryCall`s
		# `collectSites` does not even look at. Same op count, same site count, same walk; zero splices.
		#
		# ⛔⛔ **AND ONE SEEDED SITE, WHICH IS WHAT MAKES THIS MODE MEASURE THE WALK AT ALL.**
		# `inlineWithNameIndex` RETURNS after `buildLeafPlan` when the module holds no eligible callee
		# (`nothingToInline`), so a ladder with zero of them measures the plan walk and never reaches
		# `collectSites` — the per-block scan that runs on every real program. `leafSeed`, called once
		# from `main`, keeps `eligibleCount` at 1 so the whole pass runs while <n> moves only ops the
		# pass inlines nothing at.
		echo "function leafSeed(a LadderInt) returns LadderInt"
		echo "	return a + 1"
		echo "end 'leafSeed'"
		echo "enum LeafErr"
		echo "	never"
		echo "end 'LeafErr'"
		echo "function leafPure(a LadderInt, b LadderInt) returns LadderInt throws LeafErr"
		echo "	if a < -1000000 'never'"
		echo "		throw LeafErr.never"
		echo "	end 'never'"
		echo "	return ((a + b) * 3) - 1"
		echo "end 'leafPure'"
		;;
	pressure)
		# The opaque call the N live values are held across. `scaleOpaque` holds a `tryCall`, so
		# `leafOpRole` reads it as `calling` and `scanLeafBody` refuses it; `widePure` exits through
		# `errorReturn` and is refused for that. Neither is ever spliced, and no other function here is a
		# leaf — so the pass reports 0 sites and regalloc sees the same program from either compiler.
		echo "typealias WideNum = int(0 to 100000000)"
		echo "enum WideErr"
		echo "	never"
		echo "end 'WideErr'"
		echo "function widePure(x WideNum) returns WideNum throws WideErr"
		echo "	if x > 99999999 'never'"
		echo "		throw WideErr.never"
		echo "	end 'never'"
		echo "	return x"
		echo "end 'widePure'"
		echo "function scaleOpaque(x WideNum) returns WideNum"
		echo "	return try widePure(x) otherwise 0"
		echo "end 'scaleOpaque'"
		;;
	guardone|guardspread)
		echo "typealias $GUARD_ALIAS = int(0 to 63)"
		echo "function leafPure(mask LadderInt, r $GUARD_ALIAS) returns LadderInt"
		echo "	return (mask shr r) and 1"
		echo "end 'leafPure'"
		;;
	esac

	case "$MODE" in
	onefunc|nosites|guardone)
		# ⚠ The result is folded into ONE accumulator rather than bound to <n> distinct `let`s: <n>
		# simultaneously-live values drives the REGISTER ALLOCATOR off a cliff long before this phase
		# says anything (`genrangesites.sh` and `genmanagedsites.sh` both measured that trap). A ladder
		# moves ONE axis.
		echo "function leafMany(a LadderInt) returns LadderInt"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			case "$MODE" in
			nosites) echo "	acc = acc + (try leafPure(a, b: $i) otherwise 0)" ;;
			guardone) echo "	acc = acc + leafPure(a, r: $((i % 64)) as $GUARD_ALIAS)" ;;
			*) echo "	acc = acc + leafPure(a, b: $i)" ;;
			esac
		done
		echo "	return acc"
		echo "end 'leafMany'"
		;;
	pressure)
		# `genwidelive.sh sum`'s body, verbatim in shape: seven parameters (the `x64-large-frame-arg7`
		# frame), N call results all live to the final sum, so Θ(N) splits each over a Θ(N) block.
		TOTAL=$(( N * (N - 1) / 2 ))
		echo "function leafMany(a WideNum, b WideNum, c WideNum, d WideNum, e WideNum, f WideNum, g WideNum) returns WideNum"
		echo "	let base = a + b + c + d + e + f"
		for ((i = 0; i < N; i++)); do
			echo "	let v$i = scaleOpaque($i)"
		done
		terms="v0"
		for ((i = 1; i < N; i++)); do
			terms="$terms + v$i"
		done
		echo "	let total = $terms"
		echo "	let spread = total - $TOTAL"
		echo "	return g + spread + base - 21"
		echo "end 'leafMany'"
		;;
	spread|guardspread)
		# ⭐ ONE SITE PER BLOCK. The `if` bodies are what put each call in a block of its own; the
		# condition is against `a`, which `main` supplies, so no arm folds away.
		echo "function leafMany(a LadderInt) returns LadderInt"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do
			echo "	if a > $i 'g$i'"
			case "$MODE" in
			guardspread) echo "		acc = acc + leafPure(a, r: $((i % 64)) as $GUARD_ALIAS)" ;;
			*) echo "		acc = acc + leafPure(a, b: $i)" ;;
			esac
			echo "	end 'g$i'"
		done
		echo "	return acc"
		echo "end 'leafMany'"
		;;
	esac

	echo "function main() returns ExitCode"
	case "$MODE" in
	nosites) echo "	print(\"{leafMany(7) + leafSeed(1)}\")" ;;
	pressure) echo "	print(\"{leafMany(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7)}\")" ;;
	*) echo "	print(\"{leafMany(7)}\")" ;;
	esac
	echo "	return 0 as ExitCode"
	echo "end 'main'"
} > "$OUT/a_leafsites.maxon"

echo "genleafsites.sh: wrote $MODE ladder with n=$N to $OUT"
