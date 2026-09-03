#!/usr/bin/env bash
# THE CONDITIONAL-COMPILATION LADDER — the axis the SHARED corpus cannot express AT ALL.
#
#     `#if`/`#else`/`#endif` are resolved at the TOKEN TIER by `filterConditionalTokens`
#     (`Compiler/ConditionalCompilation.maxon`), once per file, behind the `queryActiveTokens` memo.
#     Is that one linear pass? Does the frame stack cost anything per NESTING LEVEL? Is skipping a
#     dead branch proportional to what is skipped, and no worse? Does a condition cost more than its
#     own atoms?
#
# ⭐ **WHY THE SHARED LADDER CANNOT ASK IT, AND IT IS THE BLUNT KIND OF BLINDNESS.** MEASURED on the
# corpus this tree's own `scale-test --emit-corpus` writes — 465 `.maxon` files across rungs 0-5:
#
#     grep -rn '#if\|#else\|#endif' --include='*.maxon' <corpus>   =>  0 matches
#
# and `Testing/ScaleCorpus.maxon` emits no directive anywhere. So `phase directives` on the shared
# ladder reads 33 / 38 / 48 / 68 / 108 / 188 allocations at rungs 0-5 — which is EXACTLY `files + 3`
# (the corpus has 30 / 35 / 45 / 65 / 105 / 185 files). That is the per-file memo BOOKKEEPING and
# nothing else: not one token was ever filtered while it was taken. A flat row from an instrument
# that cannot express the feature is not evidence that the feature is cheap.
#
# Usage: gendirectives.sh <n> <mode> <outdir>
#   <n>    = THE DOUBLING KNOB. What it counts depends on the mode.
#   outdir = the project directory to write (CREATED AND OVERWRITTEN; its `*.maxon` are pruned first).
#
#   e.g. gendirectives.sh 1024 regions temp/dir-regions-1024/
#
# THE MODES COME IN CONTROL PAIRS. A product term only separates when one axis is HELD while the
# other sweeps, so no mode below is meant to be read on its own:
#
#   regions / plain  — <n> FUNCTIONS either way, and THE SURVIVING PROGRAM IS IDENTICAL: same
#                      declarations, same bodies, same names, so every phase downstream of the filter
#                      sees the same input. `regions` wraps each function in its own live
#                      `#if os(<host>)` / `#endif`; `plain` writes no directive at all. The whole
#                      difference is 2n directive tokens and the fact that the filter runs. This is
#                      the COMMON CASE axis — `stdlib/FilePath.maxon` has 7 regions.
#
#   dead    / regions— <n> regions either way, same token count, same nesting. In `dead` every region
#                      is `#if os(<not-host>)` so its whole body is SKIPPED; in `regions` every region
#                      is taken. Read `phase directives` ALONE across this pair: the surviving
#                      programs differ (that is the point), so the totals are not comparable, but the
#                      filter saw the same tokens both ways. It answers "is skipping proportional to
#                      what is skipped, and is it cheaper than keeping?".
#
#   nest    / flat   — <n> DIRECTIVE REGIONS either way, so the same directive tokens are read and the
#                      same one function survives. In `nest` they are nested <n> deep, so the frame
#                      stack reaches depth <n>; in `flat` they are <n> sequential regions at depth 1,
#                      so it never exceeds 1. THE PRODUCT AXIS: if liveness were answered by WALKING
#                      the frame stack rather than reading its top, `nest` would be depth x tokens and
#                      `flat` would not. If `nest` bends and `flat` does not, the bend is the walk.
#
#   cond             — CONDITION LENGTH <n>: ONE `#if` whose condition is <n> `and`-joined atoms. The
#                      evaluator DELIBERATELY DOES NOT SHORT-CIRCUIT (so the cursor lands
#                      deterministically past the whole condition — see `evaluateCondition`), which
#                      means every atom is always parsed. This confirms that costs <n> and not more.
#
#   parens           — CONDITION NESTING DEPTH <n>: one `#if ((((...os(<host>)...))))` nested <n> deep.
#                      `evaluateAtom` recurses through `evaluateCondition` for each `(`, so this is the
#                      only input that drives the grammar's RECURSION rather than its loops. Unpaired —
#                      its own control is the ratio, since the source text only doubles.
#
#   files   / filesplain
#                    — <n> FILES either way, one function each. In `files` each file carries one live
#                      one-line `#if`/`#endif`; in `filesplain` no file has a directive. Same file
#                      count, same surviving program, so the per-file MEMO cost (two `Map with
#                      (FilePath, _)` probes, which on Windows allocate — `FilePath.hash` clones and
#                      lower-cases) is held equal and the difference is the filter's per-FILE cost:
#                      what a file pays for containing a directive at all, including rebuilding its
#                      token array instead of returning the lexer's.
#
# Read it with `--metrics`, whose `phase` rows carry the exact allocation counts:
#
#     maxon-shv2 build temp/dir-regions-1024/ -o temp/dir.exe --metrics=temp/dir.tsv
#     grep -P '^phase\t(directives|lex|signatures|parse)\t' temp/dir.tsv
#
# ⚠ **READ THE ALLOCATION COLUMN FIRST — it is exact and bit-reproducible, so a ratio off it is a
# datapoint on the first run.** The CPU column (field 7) moves a few percent with turbo and cache
# pressure. AND THIS PASS IS THE KIND THAT HIDES FROM THE ALLOCATION COLUMN: a token walk that keeps
# nothing allocates nothing, so `dead` can be arbitrarily expensive at Delta-0 allocations. For every
# mode here, read `cputicks` beside `allocs` or you are reading half the pass.
# Because the ladder DOUBLES, the RATIO between consecutive <n> IS the growth: x2 linear, x4 quadratic.

set -euo pipefail

if [ $# -ne 3 ]; then
	echo "usage: gendirectives.sh <n> <regions|plain|dead|nest|flat|cond|parens|files|filesplain> <outdir>" >&2
	exit 2
fi

N="$1"
MODE="$2"
OUT="$3"

case "$MODE" in
	regions|plain|dead|nest|flat|cond|parens|files|filesplain) ;;
	*) echo "gendirectives.sh: unknown mode '$MODE'" >&2; exit 2 ;;
esac

# The predicate that is TRUE for the compiling host, and one that is FALSE. The ladder always builds
# for the host (no --target), exactly as the shared corpus does, so these are decided here.
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*) LIVE_OS="Windows"; DEAD_OS="Linux" ;;
	Darwin)               LIVE_OS="macOS";   DEAD_OS="Linux" ;;
	*)                    LIVE_OS="Linux";   DEAD_OS="Windows" ;;
esac

mkdir -p "$OUT"
rm -f "$OUT"/*.maxon

# The body every mode wraps, parameterized by index so no two declarations collide. Deliberately
# small and branch-free: the axis is the DIRECTIVE, and a body with control flow would put regalloc
# in the measurement (see genrangesites.sh's note).
emit_fn() {
	local i="$1" indent="${2:-}"
	echo "${indent}function df$i(a LadderInt) returns LadderInt"
	echo "${indent}	let b = a + $i"
	echo "${indent}	return b * 3"
	echo "${indent}end 'df$i'"
}

emit_main() {
	{
		echo "function main() returns ExitCode"
		echo "	return 0 as ExitCode"
		echo "end 'main'"
	} > "$OUT/z_main.maxon"
}

case "$MODE" in
regions|plain|dead)
	# <n> functions. `plain` writes them bare; `regions` wraps each in a TAKEN region; `dead` wraps
	# each in a SKIPPED one. `regions` and `plain` leave the identical surviving program, so every
	# phase after the filter is held equal and their difference is the filter alone.
	{
		echo "// Generated by gendirectives.sh: $N functions, mode=$MODE."
  echo "typealias LadderInt = int(i64.min to i64.max)"
		for ((i = 0; i < N; i++)); do
			case "$MODE" in
				plain)   emit_fn "$i" ;;
				regions) echo "#if os($LIVE_OS)"; emit_fn "$i" "	"; echo "#endif" ;;
				dead)    echo "#if os($DEAD_OS)"; emit_fn "$i" "	"; echo "#endif" ;;
			esac
		done
	} > "$OUT/b_regions.maxon"
	emit_main
	;;

nest|flat)
	# <n> regions either way, one surviving function either way. `nest` drives the frame stack to
	# depth <n>; `flat` holds it at 1. The `#if` bodies are empty in `flat` so the two files carry the
	# same directive tokens and the same single declaration.
	{
		echo "// Generated by gendirectives.sh: $N regions, mode=$MODE."
		if [ "$MODE" = "nest" ]; then
			for ((i = 0; i < N; i++)); do echo "#if os($LIVE_OS)"; done
			emit_fn 0
			for ((i = 0; i < N; i++)); do echo "#endif"; done
		else
			for ((i = 0; i < N; i++)); do echo "#if os($LIVE_OS)"; echo "#endif"; done
			emit_fn 0
		fi
	} > "$OUT/b_nest.maxon"
	emit_main
	;;

cond)
	# ONE `#if` whose condition is <n> `and`-joined atoms. Every atom is evaluated — the grammar does
	# not short-circuit, deliberately — so this is <n> predicate calls in one condition.
	{
		echo "// Generated by gendirectives.sh: one condition of $N and-joined atoms."
		echo -n "#if os($LIVE_OS)"
		for ((i = 1; i < N; i++)); do echo -n " and os($LIVE_OS)"; done
		echo ""
		emit_fn 0
		echo "#endif"
	} > "$OUT/b_cond.maxon"
	emit_main
	;;

parens)
	# CONDITION NESTING DEPTH <n> — the only input that drives the grammar's RECURSION. `evaluateAtom`
	# calls `evaluateCondition` for each `(`, so the interpreter's own stack reaches depth <n>.
	{
		echo "// Generated by gendirectives.sh: a condition parenthesized $N deep."
		echo -n "#if "
		for ((i = 0; i < N; i++)); do echo -n "("; done
		echo -n "os($LIVE_OS)"
		for ((i = 0; i < N; i++)); do echo -n ")"; done
		echo ""
		emit_fn 0
		echo "#endif"
	} > "$OUT/b_parens.maxon"
	emit_main
	;;

files|filesplain)
	# <n> FILES, one function each. The per-file memo cost is held equal (same file count, same two
	# `Map with (FilePath, _)` probes per query); only whether the file HAS a directive sweeps. What a
	# file pays for containing one at all, which for a large program is the number that matters.
	for ((i = 0; i < N; i++)); do
		{
			echo "// Generated by gendirectives.sh: file $i of $N, mode=$MODE."
			if [ "$MODE" = "files" ]; then
				echo "#if os($LIVE_OS)"
				emit_fn "$i" "	"
				echo "#endif"
			else
				emit_fn "$i"
			fi
		} > "$OUT/b_f$i.maxon"
	done
	emit_main
	;;
esac

echo "gendirectives.sh: wrote $MODE ladder with n=$N to $OUT"
