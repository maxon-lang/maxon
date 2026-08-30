#!/usr/bin/env bash
# THE `__ManagedMemory` BUFFER-SURFACE LADDER (R4.4) — and the `try`-on-a-runtime-callee axis beside it.
#
#     R4.4 makes `__ManagedMemory` a real object surface over the SAME record `Array` uses, so the two
#     surfaces of one `GenericInstanceId` are told apart by PROVENANCE: `Parser.bufferSurfaceValues`, a
#     per-function sparse `Set with ValueId` marking values minted as a buffer. Three questions follow,
#     and the shared corpus can answer none of them.
#
#         1. Is the buffer surface itself linear — the mark set, the `.managed` dispatch RE-ENTRY, the
#            `arrayManagedFieldAt` token predicate, the four buffer-only members?
#         2. What does the rung cost a program that NEVER USES IT? The predicate is asked at three
#            statement/expression dispatch sites, and `valueIsBufferSurface` is asked once per array
#            method dispatch — on every program, feature or no feature.
#         3. What does the `ArrayError` throws clause cost per `try` on a throwing array accessor?
#            R4.4 added that arm to `GtRuntime.runtimeThrowsClause`, and it is the rung's ONLY growing
#            allocation term. It is FILED AS MEASURED DEBT, so this is the ladder that re-runs it.
#
# ⭐ **WHY THE SHARED LADDER CANNOT ASK 1 OR 2 — the blunt kind of blindness, not the subtle one.**
# `Testing/ScaleCorpus.maxon` EMITS NO `__ManagedMemory` AND NO `.managed`. Not a weak knob: the
# construct is absent from the generated text entirely, and the R4.2 row says the same of file IO
# ("this corpus contains no file IO at all"). So every column of a default run reads a flat Δ0 for the
# whole surface, and a Δ0 from an instrument that cannot express the feature is not evidence about the
# feature.
#
# ⚠ **IT IS NOT BLIND TO QUESTION 3, AND THAT IS THE TRAP.** The corpus is full of `try arr.get(i)`, so
# the `ArrayError` arm DOES read on a default run — measured at +50/+102/+230/+470/+950/+1,910
# allocations in `phase:parse` across rungs 0-5 (×2.04 ×2.25 ×2.04 ×2.02 ×2.01: linear). What the
# corpus cannot do is HOLD THE OTHER AXES STILL while sweeping the `try` count, which is what separates
# a per-`try` term from a per-statement one. That is `trythrows`/`trynone`.
#
# Usage: genmm.sh <n> <mode> <outfile>
#   <n>  = THE DOUBLING KNOB — the FUNCTION COUNT in every mode, so program size doubles with it and
#          each function's live set stays fixed (the register allocator is not an axis here).
#   e.g. genmm.sh 512 buffer    temp/mm-buffer-512.maxon
#        genmm.sh 512 array     temp/mm-array-512.maxon
#
# THE MODES COME IN TWO CONTROL PAIRS. Neither member is meant to be read on its own.
#
#   buffer / array   — SAME function count, SAME statement count, SAME number of array-method call
#                      sites, SAME number of `try`s. In `buffer` every call goes through the
#                      `__ManagedMemory` surface (`create`, then `.managed.<member>()`); in `array`
#                      the identical count of calls goes through an ordinary `IntArray` and the word
#                      `__ManagedMemory` does not appear in the program. So `array` prices what R4.4
#                      costs a program that never uses the feature — the `arrayManagedFieldAt`
#                      lookahead and the `valueIsBufferSurface` probe against the module-level
#                      zero-capacity shared set — and the DIFFERENCE is the surface itself.
#                      ⚠ `array` also carries six accessor `try`s, so its cross-compiler delta is the
#                      `trythrows` term to the digit; read `trynone` for the feature-free zero.
#                      ⚠ `buffer` does NOT compile under the pre-R4.4 compiler (that is the rung), so it
#                      supports a growth reading but not a cross-compiler A/B. Measured `phase:parse`
#                      allocations 190,243 / 376,898 / 750,184 / 1,496,716 at n = 256..2048 —
#                      x1.981 x1.991 x1.995, LINEAR, which is the answer to question 1.
#
#   trythrows/trynone — SAME function count and SAME `try` count. In `trythrows` every `try` targets a
#                      THROWING ARRAY ACCESSOR (`arr.get`), so `ProgramSignatures.throwsOf` reaches
#                      `runtimeThrowsClause`'s R4.4 arm and BUILDS a clause — one heap `String`
#                      (`String.from(ArrayErrorTypeName)`) plus a `ThrowsClause` union box per ask.
#                      In `trynone` every `try` targets a USER function that throws, so
#                      `runtimeThrowsClause` answers `none` allocating nothing and the clause is read
#                      out of the `funcThrows` map instead.
#
# ⚠⚠ **DO NOT SUBTRACT `trynone` FROM `trythrows` — MEASURED, THE SUBTRACTION IS MEANINGLESS.** The two
# programs differ in the CALL SHAPE behind the `try` (a user call passes arguments and has a declaration;
# an array accessor does not), and that difference is ~90,000 allocations at n=2048 — 2.5× the term being
# looked for, and pointing the wrong way (`trynone` is HIGHER). **The clause is priced by A/B-ing ONE mode
# across TWO COMPILERS**, which holds the program byte-identical and varies only the thing under test.
# Both modes compile under the pre-R4.4 compiler, which is what makes that possible.
#
# ⭐ **WHAT THAT A/B MEASURED (`phase:parse` allocations, pre-R4.4 → R4.4, same files):**
#
#     trynone     -11  -11  -11  -11        FLAT over a x8 span — the ZERO control.
#     trythrows  +4,597 +9,205 +18,421 +36,853   x2.003 x2.001 x2.000 — LINEAR.
#     array      +4,597 +9,205 +18,421 +36,853   identical, and it has the same 6 accessor trys.
#
#   ⇒ **delta = 3 x (trys on a throwing array accessor) - 11**, exact at every rung
#     (3 x 1,536 - 11 = 4,597; 3 x 12,288 - 11 = 36,853). The -11 is `trynone`'s whole delta, so the
#     buffer-surface machinery R4.4 puts on EVERY program — the `arrayManagedFieldAt` lookahead at three
#     dispatch sites and the `valueIsBufferSurface` probe at every array method call — is allocation-free
#     to the digit. That is the measurement `trynone` exists to take, and it is why it is kept.
#
#   ⚠ At SIX accessor `try`s per function — far denser than any real program — the term is +36,853 of
#     4,568,879 total allocations, i.e. **+0.81% of the compile**. On the standing corpus it is +1,910 of
#     14.87M, **+0.013%**. Both numbers are in the R4.4 row of `docs/optimization-log.md`, where the term
#     is filed as measured debt rather than fixed; that row carries the reason and the re-measure trigger.
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/mm-buffer-512.maxon -o temp/mm.exe --metrics=temp/mm.tsv
#     grep -P '^phase\t(signatures|parse|isel)\t' temp/mm.tsv
#
# ⚠ **READ THE ALLOCATION COLUMN FIRST — it is exact and bit-reproducible, so a ratio off it is a
# datapoint on the first run.** The CPU column moves a few percent with turbo and cache pressure.
# And because the ladder DOUBLES, the RATIO between consecutive <n> IS the growth: ×2 linear,
# ×4 quadratic. Nothing to fit.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: genmm.sh <n> <buffer|array|trythrows|trynone> <outfile>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	buffer|array|trythrows|trynone) ;;
	*) echo "genmm.sh: unknown mode '$MODE'" >&2; exit 2 ;;
esac

mkdir -p "$(dirname "$OUT")"

# The `try` count per generated function, held EQUAL across every mode so the desugar's own cost is a
# constant of the ladder rather than one of its axes.
TRIES_PER_FN=6

case "$MODE" in
buffer)
	# The BUFFER SURFACE: `create` mints a marked value, `.managed` re-enters the array dispatcher with
	# `viaManagedField: true`, and a `slice` of a buffer is itself marked. Six `try`s per function.
	{
		echo "// Generated by genmm.sh: $N functions on the __ManagedMemory buffer surface."
  echo "typealias LadderInt = int(i64.min to i64.max)"
		for ((i = 0; i < N; i++)); do
			echo "function mm$i() returns LadderInt"
			echo "	var mm = try __ManagedMemory.create(8, elementSize: 1) otherwise return 1"
			echo "	try mm.setLength(4) otherwise return 2"
			echo "	try mm.managed.setByte(0, value: 65) otherwise return 3"
			echo "	let b = try mm.managed.byteAt(0) otherwise return 4"
			echo "	try mm.managed.grow(32) otherwise return 5"
			echo "	let s = try mm.managed.slice(0, 2) otherwise return 6"
			echo "	let n = s.managed.length()"
			echo "	return b + n"
			echo "end 'mm$i'"
		done
	} > "$OUT"
	;;

array)
	# THE CONTROL: the same function count, statement count, array-method call count and `try` count,
	# through an ordinary `IntArray`. The word `__ManagedMemory` does not appear, so `bufferSurfaceValues`
	# stays the module-level shared EMPTY set for the whole compile and `arrayManagedFieldAt` never
	# matches — which is exactly the state every real program is in, and what this mode prices.
	{
		echo "// Generated by genmm.sh: $N functions, the same call and try counts on a plain IntArray."
		echo "typealias Integer = int(i64.min to i64.max)"; echo "typealias IntArray = Array with Integer"
		for ((i = 0; i < N; i++)); do
			echo "function mm$i() returns LadderInt"
			echo "	var arr = IntArray.create()"
			echo "	arr.push(7)"
			echo "	arr.push(8)"
			echo "	try arr.set(0, value: 65) otherwise return 1"
			echo "	let b = try arr.get(0) otherwise return 2"
			echo "	arr.reserve(32)"
			echo "	let s = try arr.slice(0, endIndex: 2) otherwise return 3"
			echo "	let c = try arr.first() otherwise return 4"
			echo "	let d = try arr.last() otherwise return 5"
			echo "	let e = try arr.pop() otherwise return 6"
			echo "	let n = s.count() as LadderInt"
			echo "	return b + n + c + d + e"
			echo "end 'mm$i'"
		done
	} > "$OUT"
	;;

trythrows)
	# THE `try`-ON-A-THROWING-RUNTIME-CALLEE AXIS. Every `try` here targets `arr.get`, so `throwsOf`
	# reaches `runtimeThrowsClause`'s `ArrayError` arm and builds a clause per ask.
	{
		echo "// Generated by genmm.sh: $N functions, $TRIES_PER_FN trys each on a THROWING ARRAY ACCESSOR."
		echo "typealias Integer = int(i64.min to i64.max)"; echo "typealias IntArray = Array with Integer"
		for ((i = 0; i < N; i++)); do
			echo "function mm$i() returns LadderInt"
			echo "	var arr = IntArray.create()"
			echo "	arr.push(7)"
			echo "	var acc = 0"
			for ((t = 0; t < TRIES_PER_FN; t++)); do
				echo "	acc = acc + (try arr.get(0) otherwise 0)"
			done
			echo "	return acc"
			echo "end 'mm$i'"
		done
	} > "$OUT"
	;;

trynone)
	# ITS CONTROL: the same function count and the same `try` count, every one targeting a USER function
	# that throws. `runtimeThrowsClause` answers `none` for it without allocating, and the clause is read
	# out of `funcThrows` instead of built. The array is still created and pushed so the two modes agree
	# on everything but the try TARGET.
	{
		echo "// Generated by genmm.sh: $N functions, $TRIES_PER_FN trys each on a USER throwing callee."
		echo "typealias Integer = int(i64.min to i64.max)"; echo "typealias IntArray = Array with Integer"
		echo "enum ProbeError implements Error"
		echo "	failed"
		echo "end 'ProbeError'"
		echo "function probeThrower(x LadderInt) returns LadderInt throws ProbeError"
		echo "	if x < 0 'neg'"
		echo "		throw ProbeError.failed"
		echo "	end 'neg'"
		echo "	return x"
		echo "end 'probeThrower'"
		for ((i = 0; i < N; i++)); do
			echo "function mm$i() returns LadderInt"
			echo "	var arr = IntArray.create()"
			echo "	arr.push(7)"
			echo "	var acc = 0"
			for ((t = 0; t < TRIES_PER_FN; t++)); do
				echo "	acc = acc + (try probeThrower(0) otherwise 0)"
			done
			echo "	return acc"
			echo "end 'mm$i'"
		done
	} > "$OUT"
	;;
esac

{
	echo ""
	echo "function main() returns ExitCode"
	echo "	return mm0()"
	echo "end 'main'"
} >> "$OUT"

echo "genmm.sh: wrote $MODE ladder with n=$N to $OUT"
