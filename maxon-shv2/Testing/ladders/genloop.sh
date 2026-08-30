#!/usr/bin/env bash
# The EXACT shape ScaleCorpus.floatSpillSource generates: N loops, each holding
# FLOATS_PER floats live across a call to scaleOpaque. Fresh names per loop, so
# peak pressure stays at one loop's worth however many loops there are.
# Usage: genloop.sh <loopCount> <floatsPerLoop> <outfile>
set -euo pipefail
N="$1"; FP="$2"; OUT="$3"
TRIPS=4

{
  echo "// loop ladder: $N loops x $FP cross-call floats"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\tif a > 100 'big'"
  echo -e "\t\treturn a - 100"
  echo -e "\tend 'big'"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"
  echo ""
  echo "function scaleFloatSpill(a LadderInt) returns LadderInt"
  echo -e "\tvar total = 0.0"
  i=0
  while [ "$i" -lt "$N" ]; do
    v=0
    while [ "$v" -lt "$FP" ]; do
      d=$(( v + 1 ))
      echo -e "\tvar g${i}_${v} = a + $d.25"
      v=$(( v + 1 ))
    done
    echo -e "\tvar j$i = 0"
    echo -e "\twhile j$i < $TRIPS 'q$i'"
    echo -e "\t\tlet k$i = scaleOpaque(j$i)"
    v=0
    while [ "$v" -lt "$FP" ]; do
      echo -e "\t\tg${i}_${v} = g${i}_${v} + (k$i + 0.5)"
      v=$(( v + 1 ))
    done
    echo -e "\t\tj$i = j$i + 1"
    echo -e "\tend 'q$i'"
    v=0
    while [ "$v" -lt "$FP" ]; do
      echo -e "\ttotal = total + g${i}_${v}"
      v=$(( v + 1 ))
    done
    i=$(( i + 1 ))
  done
  echo -e "\treturn trunc(total)"
  echo "end 'scaleFloatSpill'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn scaleFloatSpill(3)"
  echo "end 'main'"
} > "$OUT"
