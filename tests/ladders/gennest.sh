#!/usr/bin/env bash
# DEPTH of loop NESTING, held independent of program SIZE.
#
# The question: `Parser.assignedBindingsIn` scans every token between a loop's header and its
# `end`, once per loop, to fix the carried-variable set before the body is parsed. A loop
# NESTED inside another therefore has its tokens scanned again by every enclosing loop, so D
# nested loops cost O(depth x tokens) in TIME while allocating one small set per loop.
# ScaleCorpus's `loopNest` knob predicted this in writing and predicted the memory-only
# instrument would read it as linear anyway.
#
# ⚠ THE TWO KNOBS ARE INDEPENDENT, WHICH IS THE WHOLE POINT. A ladder that doubles depth and
# statements together cannot tell a depth term from a size term — both read x2. Hold
# `depth * stmtsPerLevel` FIXED and vary `depth`, and the program stays ~the same size while
# the nesting deepens: any cost that moves is a DEPTH term and nothing else. That is the same
# discipline genmutchain.sh uses for the mutation fixpoint (see README).
#
# Usage: gennest.sh <depth> <stmtsPerLevel> <outfile>
#   e.g. gennest.sh 4 128 a.maxon   and   gennest.sh 8 64 b.maxon   are the SAME size.
#
# The loops are written to trip ZERO times (`while iN < 0`) so the program is cheap to run if
# anyone runs it; this ladder is about COMPILE cost and scale-test never runs its corpus.
set -euo pipefail
D="$1"; S="$2"; OUT="$3"

{
  echo "// nesting ladder: depth $D x $S statements per level (size ~ D*S)"
  echo "typealias Acc = int(0 to u64.max)"
  echo ""
  echo "function nestScan() returns Acc"
  echo -e "\tvar total = 0 as Acc"

  # Open D nested loops, each with S assignments in its body.
  #
  # ⚠ EVERY LEVEL GUARDS ON THE SAME `total`, deliberately: a per-level counter would be one
  # more value live across every enclosing loop, and this allocator REFUSES rather than spills
  # inside a loop (E5001, "the loop needs N more registers than are available"). With a counter
  # per level the ladder stops compiling at depth ~14 — which is a fact about register pressure,
  # not about the token scan this ladder exists to measure. One shared guard keeps the live set
  # at one value however deep the nesting goes.
  d=0
  while [ "$d" -lt "$D" ]; do
    ind=$(printf '\t%.0s' $(seq 0 $d))
    echo -e "${ind}while total < 0 'l${d}'"
    inner=$(printf '\t%.0s' $(seq 0 $((d + 1))))
    s=0
    while [ "$s" -lt "$S" ]; do
      echo -e "${inner}total = total + ${d}"
      s=$(( s + 1 ))
    done
    d=$(( d + 1 ))
  done

  # Close them innermost-first.
  d=$(( D - 1 ))
  while [ "$d" -ge 0 ]; do
    ind=$(printf '\t%.0s' $(seq 0 $d))
    echo -e "${ind}end 'l${d}'"
    d=$(( d - 1 ))
  done

  echo -e "\treturn total"
  echo "end 'nestScan'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\tif nestScan() > 0 'nonZero'"
  echo -e "\t\treturn 1"
  echo -e "\tend 'nonZero'"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
