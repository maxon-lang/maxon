#!/usr/bin/env bash
# N units, each holding FLOATS_PER floats live across ONE call. With FLOATS_PER > 10 the
# overflow past the callee-saved XMM half is a FORCED spill, so the splitter really runs
# and `growValueSpace` mints ids on every split.
set -euo pipefail
N="$1"; FLOATS_PER="$2"; OUT="$3"

{
  echo "// ladder: $N units x $FLOATS_PER cross-call floats"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\tif a > 100 'big'"
  echo -e "\t\treturn a - 100"
  echo -e "\tend 'big'"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"
  echo ""
  echo "function scaleSeed() returns LadderInt"
  echo -e "\tvar acc = 1"
  echo -e "\tvar i = 0"
  echo -e "\twhile i < 4 'seedLoop'"
  echo -e "\t\tacc = acc + i * 3"
  echo -e "\t\ti = i + 1"
  echo -e "\tend 'seedLoop'"
  echo -e "\treturn acc"
  echo "end 'scaleSeed'"
  echo ""
  echo "function driver() returns LadderInt"
  echo -e "\tlet s = scaleSeed()"
  echo -e "\tvar total = 0"
  i=0
  while [ "$i" -lt "$N" ]; do
    v=0
    while [ "$v" -lt "$FLOATS_PER" ]; do
      d=$(( v % 9 + 1 ))
      echo -e "\tvar g${i}_${v} = s + $d"
      v=$(( v + 1 ))
    done
    echo -e "\tlet k$i = scaleOpaque($i)"
    v=0
    while [ "$v" -lt "$FLOATS_PER" ]; do
      echo -e "\ttotal = total + g${i}_${v} + (k$i + 1)"
      v=$(( v + 1 ))
    done
    i=$(( i + 1 ))
  done
  echo -e "\treturn total"
  echo "end 'driver'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn driver()"
  echo "end 'main'"
} > "$OUT"
