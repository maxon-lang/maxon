#!/usr/bin/env bash
# THE TUPLE LADDER — the axis the SHARED corpus does not have, and could not get by doubling.
#
#     A tuple is a SYNTHESIZED STRUCT minted under a mangled name (`__Tuple2.int.int`). Is minting
#     one O(1), is reading one back O(1), and does the number of DISTINCT tuple types in a program
#     cost anything more than the number of tuple SITES?
#
# ⭐ **WHY THE SHARED LADDER CANNOT ASK IT, AND IT IS THE BLUNT KIND OF BLINDNESS, NOT THE SUBTLE ONE.**
# `Testing/ScaleCorpus.maxon` EMITS NO TUPLE. Not a weak knob, not a construct that reaches a door and
# is then discarded as vacuous — the construct is absent from the generated text entirely. MEASURED,
# on the corpus this tree's own `scale-test --emit-corpus` writes (465 files, 279,453 lines across
# rungs 0-5):
#
#     let/var destructuring   `(let|var)\s*\(`              0
#     tuple element access    `\.[0-9]` (excl. float lits)  0
#     tuple literal           `=\s*\([^()]*,[^()]*\)`       0
#     tuple return type       `->\s*\(`                     0
#     tuple typealias         `typealias .*=\s*\(`          0
#     the word "tuple", any case                            0
#
# and the generator's own source (`ScaleCorpus.maxon`) contains the string "tuple" ZERO times.
#
# ⇒ The `+20 allocations, FLAT at every rung` the tuple rung reported off the shared ladder is a true
#   reading of TWO EMPTY MAPS BEING CONSTRUCTED ONCE PER COMPILE, and of nothing else. Not one line of
#   `internTupleType`, `canonicalTupleName`, `registerTupleLayout`, `parseTupleLiteral` or
#   `parseDestructuringBinding` executed while it was taken. A flat delta from an instrument that
#   cannot express the feature is not evidence that the feature is cheap — it is not evidence at all.
#
# Usage: gentuples.sh <n> <mode> <outdir>
#   <n>    = THE DOUBLING KNOB. What it counts depends on the mode.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
#   e.g. gentuples.sh 512 types  temp/tp-types-512/
#        gentuples.sh 512 sites  temp/tp-sites-512/
#
# THE MODES COME IN CONTROL PAIRS. A product term only separates when one axis is HELD while the
# other sweeps, so no mode below is meant to be read on its own:
#
#   types  / sites   — <n> tuple LITERAL SITES either way, same token count, same arity, same file
#                      count. In `types` every site is a DISTINCT tuple type; in `sites` all <n> sites
#                      are the SAME type. So `sites` is the per-SITE cost with the type count pinned at
#                      1, and the DIFFERENCE is what a distinct type costs to mint, register and
#                      canonicalize. Both encode the type in elements 1..7 of an arity-8 tuple as the
#                      base-3 digits of the site index over {int, float, bool} — 3^7 = 2187 distinct
#                      types available, so the pair holds to n=2048. Element 0 is always `int` so
#                      `t.0` types the same in both.
#
#   arity  / access  — <n> ELEMENT ACCESSES either way. In `arity` they are <n> accesses `t.0 … t.<n-1>`
#                      against ONE tuple of arity <n>; in `access` they are <n> accesses against a tuple
#                      of arity 2. THE PRODUCT AXIS: `StructLayout.indexOfField` is a LINEAR SCAN by
#                      field-name string (`Project.maxon:1422`), so <n> accesses over <n> fields is
#                      accesses x fields. `access` holds the field count at 2 and sweeps only the
#                      accesses. If `arity` bends and `access` does not, the bend is the product.
#
#   files  / fileset — <n> FILES either way, each holding one `[<identifier>…]` array literal, which is
#                      what makes the sweep call `ProgramSignatures.internArrayLiteralAggregateInstances`
#                      — historically a walk over EVERY registered struct type at EVERY such file. In
#                      `files` each file declares a DISTINCT tuple type as a `typealias` (which the SWEEP
#                      mints, so `structTypes` grows as the sweep runs); in `fileset` all <n> files share
#                      ONE tuple type, so it stops growing after the first. Same file count, same literal
#                      count, same everything else: the difference is files x types. THIS PAIR FOUND ONE
#                      — `phase signatures` allocations read x2.10 x2.24 x2.45 x2.75 against the control's
#                      x1.95 x1.97 x1.99 x1.99 — and it is kept because it is the only thing that can show
#                      the walk has not come back to the per-file position it was hoisted out of.
#
#   nest             — NESTING DEPTH <n>: `(1, (2, (3, …)))`. A nested tuple's mangled name EMBEDS its
#                      element names, so the name at depth k is O(k) bytes and building all <n> of them
#                      is O(n^2) in principle. Unpaired — its own control is the ratio: a name-length
#                      term shows as x4 against source text that only doubled.
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/tp-types-512/ -o temp/tp.exe --metrics=temp/tp.tsv
#     grep -P '^phase\t(signatures|parse|merge)\t' temp/tp.tsv
#
# ⚠ **READ THE ALLOCATION COLUMN FIRST — it is exact and bit-reproducible, so a ratio off it is a
# datapoint on the first run.** The CPU column (field 7) moves a few percent with turbo and cache
# pressure; against the x2-vs-x4 question that band is harmless, but confirm a CPU-only bend across
# repeats before believing it — and a scan of interned names ALLOCATES NOTHING, so a tuple term that
# is real can be CPU-only. And because the ladder DOUBLES, the RATIO between consecutive <n> IS the
# growth: x2 linear, x4 quadratic. Nothing to fit.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: gentuples.sh <n> <types|sites|arity|access|files|fileset|nest> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	types|sites|arity|access|files|fileset|nest) ;;
	*) echo "gentuples.sh: unknown mode '$MODE'" >&2; exit 2 ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

# The three element spellings the base-3 site index is rendered in, and the literal that produces one.
# `int` leads so element 0 — always digit 0 — makes `t.0` an int in every generated program.
ELEM_TYPE=(int float bool)
ELEM_VALUE=("1" "2.5" "true")

# The number of type-carrying element positions. Element 0 is pinned to `int`, so an arity-8 tuple
# encodes 3^7 = 2187 distinct types: enough for n=2048 without the ladder folding two rungs onto one
# type count, which would silently turn `types` into `sites`.
TYPE_DIGITS=7

# Render the arity-8 tuple LITERAL whose type encodes $1 in base 3. All-zero digits (site index 0)
# give the single shared type the `sites` control pins every site to.
tuple_literal() {
	local k="$1" i d out="(1"
	for ((i = 0; i < TYPE_DIGITS; i++)); do
		d=$((k % 3)); k=$((k / 3))
		out="$out, ${ELEM_VALUE[$d]}"
	done
	echo "$out)"
}

# The TYPE spelling of the same tuple — what a parameter or return annotation writes.
tuple_type() {
	local k="$1" i d out="(int"
	for ((i = 0; i < TYPE_DIGITS; i++)); do
		d=$((k % 3)); k=$((k / 3))
		out="$out, ${ELEM_TYPE[$d]}"
	done
	echo "$out)"
}

case "$MODE" in
types|sites)
	# <n> tuple literal sites, one per function so no function's live set grows with the rung (the
	# register allocator is not the axis here — see genrangesites.sh's note on inlining the value).
	# `types` gives every site its own type; `sites` pins them all to type 0.
	{
		echo "// Generated by gentuples.sh: $N tuple literal sites, mode=$MODE."
  echo "typealias LadderInt = int(i64.min to i64.max)"
		for ((i = 0; i < N; i++)); do
			if [ "$MODE" = "types" ]; then lit=$(tuple_literal "$i"); else lit=$(tuple_literal 0); fi
			echo "function tp$i() returns LadderInt"
			echo "	let t = $lit"
			echo "	return t.0"
			echo "end 'tp$i'"
		done
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	return tp0() as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
	;;

arity)
	# ONE tuple of arity <n>, read <n> times — the accesses x fields product. Each read is its own
	# statement so exactly one tuple element is live at a time.
	{
		echo "// Generated by gentuples.sh: one arity-$N tuple, read $N times."
		echo -n "function tpWide() returns LadderInt"
		echo ""
		echo -n "	let t = (1"
		for ((i = 1; i < N; i++)); do echo -n ", 1"; done
		echo ")"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do echo "	acc = acc + t.$i"; done
		echo "	return acc"
		echo "end 'tpWide'"
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	return tpWide() as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
	;;

access)
	# THE CONTROL FOR `arity`: the same <n> element accesses against a tuple of arity 2, so the field
	# count is pinned and only the access count sweeps.
	{
		echo "// Generated by gentuples.sh: $N element accesses against an arity-2 tuple."
		echo "function tpRead() returns LadderInt"
		echo "	let t = (1, 2)"
		echo "	var acc = 0"
		for ((i = 0; i < N; i++)); do echo "	acc = acc + t.$((i % 2))"; done
		echo "	return acc"
		echo "end 'tpRead'"
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	return tpRead() as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
	;;

files|fileset)
	# <n> FILES, each with an `[<identifier>…]` array literal — the trigger for the sweep's
	# `internArrayLiteralAggregateInstances` walk over every registered struct type. `files` gives each
	# file its own tuple type; `fileset` gives every file the SAME type, so `structTypes` stops growing
	# after the first file and the walk's count-equality early-out engages.
	#
	# ⚠ **THE TUPLE IS WRITTEN AS A `typealias`, AND THAT IS LOAD-BEARING RATHER THAN COSMETIC.** A tuple
	# spelled inline in a function SIGNATURE is minted by the REAL PARSE, not by the sweep — MEASURED:
	# with the tuple inline in the parameter list, <n> distinct types and <n> copies of one type give
	# BYTE-IDENTICAL `phase signatures` allocation counts (200,794 at n=1024, both), so `structTypes`
	# does not grow while the sweep runs and the pair cannot test the early-out at all. The `typealias`
	# RHS goes through `readTypeAliasDeclaration`, which the sweep DOES run (+62 allocations per distinct
	# type in the sweep, measured on a 4-file probe), which is what puts the growth where the walk is.
	for ((i = 0; i < N; i++)); do
		if [ "$MODE" = "files" ]; then ty=$(tuple_type "$i"); else ty=$(tuple_type 0); fi
		{
			echo "// Generated by gentuples.sh: file $i of $N, mode=$MODE."
			echo "typealias TP$i = $ty"
			echo "function tf$i(t TP$i) returns LadderInt"
			echo "	let q = t.0"
			echo "	let xs = [q]"
			echo "	return xs.count() as LadderInt"
			echo "end 'tf$i'"
		} > "$OUT/b_f$i.maxon"
	done

	{
		echo "function main() returns ExitCode"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
	;;

nest)
	# NESTING DEPTH <n>. The mangled name at depth k embeds every name below it, so the names alone are
	# O(n^2) bytes over the ladder while the source text is O(n).
	{
		echo "// Generated by gentuples.sh: a tuple nested $N deep."
		echo "function tpNest() returns LadderInt"
		echo -n "	let t = (1"
		for ((i = 1; i < N; i++)); do echo -n ", (1"; done
		echo -n ", 1"
		for ((i = 1; i < N; i++)); do echo -n ")"; done
		echo ")"
		echo "	return t.0"
		echo "end 'tpNest'"
	} > "$OUT/b_sites.maxon"

	{
		echo "function main() returns ExitCode"
		echo "	return tpNest() as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
	;;
esac

echo "gentuples.sh: wrote $MODE ladder with n=$N to $OUT"
