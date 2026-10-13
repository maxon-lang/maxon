#!/usr/bin/env bash
# THE WIDE-REGION LADDER — the worst case of the liveness walk's dominator skip, ISOLATED.
#
#     `DomSkip` marks a block "clean" when everything that reaches it without passing its immediate
#     dominator is exactly the layout interval between the two. Proving that scans the interval, capped at
#     `SkipScanLimit`. A scan that FAILS late (the block that does not reach the join sits at the far end
#     of the interval) pays the whole interval before it gives up, so the question is whether many joins
#     over wide regions cost O(blocks x cap) — linear — or something worse.
#
# The shapes:
#   nested   N nested `if`s, each opening with an early `return` arm. The join closing level i spans every
#            level inside it, and the early-return block sits at its far end, so each scan walks the
#            whole inner structure (up to the cap) and then fails.
#   arms     one `match` over an enum of N cases, joined once: a single wide diamond whose scan succeeds
#            for N within the cap and is refused outright beyond it.
#   control  the nested shape's statements with no nesting and no early return: the same block count,
#            every join narrow.
#
# Usage: genwideregion.sh <n> <nested|arms|control> <outfile>
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"

case "$SHAPE" in
  nested|arms|control) ;;
  *)
    echo "genwideregion.sh: shape must be 'nested', 'arms' or 'control', got '$SHAPE'" >&2
    exit 2
    ;;
esac

{
  echo "// wide-region ladder: $SHAPE, n=$N"
  echo "typealias Num = int(0 to 1000000000)"
  echo ""
  echo "var counter = 0 as Num"
  echo ""
  echo "function work(a Num) returns Num"
  echo -e "\tcounter = counter + 1"
  echo -e "\treturn (a and 255) + counter"
  echo "end 'work'"
  echo ""

  if [ "$SHAPE" = "arms" ]; then
    echo "enum Arm"
    i=0
    while [ "$i" -lt "$N" ]; do
      echo -e "\tarm${i}"
      i=$(( i + 1 ))
    done
    echo "end 'Arm'"
    echo ""
  fi

  if [ "$SHAPE" = "arms" ]; then
    echo "function wide(g Num, which Arm) returns Num"
    echo -e "\tvar total = g"
    echo -e "\tmatch which 'arms'"
    i=0
    while [ "$i" -lt "$N" ]; do
      echo -e "\t\tarm${i} then total = total + work(g + ${i})"
      i=$(( i + 1 ))
    done
    echo -e "\tend 'arms'"
    echo -e "\treturn total"
    echo "end 'wide'"
  else
    echo "function wide(g Num) returns Num"
    echo -e "\tvar total = g"

    i=0
    while [ "$i" -lt "$N" ]; do
      if [ "$SHAPE" = "nested" ]; then
        echo "if g > ${i} 'l${i}'"
        echo "if g == $(( i + 3 )) 'e${i}'"
        echo "return total"
        echo "end 'e${i}'"
        echo "total = total + work(g + ${i})"
      else
        echo "if g > ${i} 'l${i}'"
        echo "total = total + work(g + ${i})"
        echo "end 'l${i}'"
      fi
      i=$(( i + 1 ))
    done

    if [ "$SHAPE" = "nested" ]; then
      i=$(( N - 1 ))
      while [ "$i" -ge 0 ]; do
        echo "end 'l${i}'"
        i=$(( i - 1 ))
      done
    fi

    echo -e "\treturn total"
    echo "end 'wide'"
  fi

  echo ""
  echo "function main() returns ExitCode"
  if [ "$SHAPE" = "arms" ]; then
    echo -e "\tlet r = wide(5, which: Arm.arm0)"
  else
    echo -e "\tlet r = wide(5)"
  fi
  echo -e "\tif r > 1000000000 'huge'"
  echo -e "\t\treturn 1"
  echo -e "\tend 'huge'"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
