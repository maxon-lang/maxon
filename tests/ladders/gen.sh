#!/usr/bin/env bash
# Ladder generator for the Wave-2 headline: N floats each live across ONE call.
# Usage: gen.sh <N> <float|int> <outfile>
set -euo pipefail
N="$1"; KIND="$2"; OUT="$3"

{
  echo "// ladder: $N cross-call ${KIND}s"
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
  if [ "$KIND" = "float" ]; then
    echo -e "\tvar total = 0.0"
  else
    echo -e "\tvar total = 0"
  fi
  i=0
  while [ "$i" -lt "$N" ]; do
    d=$(( i % 9 + 1 ))
    if [ "$KIND" = "float" ]; then
      echo -e "\tlet f$i = s + $d.25"
      echo -e "\tlet k$i = scaleOpaque($i)"
      echo -e "\ttotal = total + f$i + (k$i + 0.5)"
    else
      echo -e "\tlet f$i = s + $d"
      echo -e "\tlet k$i = scaleOpaque($i)"
      echo -e "\ttotal = total + f$i + (k$i + 1)"
    fi
    i=$(( i + 1 ))
  done
  if [ "$KIND" = "float" ]; then
    echo -e "\treturn trunc(total)"
  else
    echo -e "\treturn total"
  fi
  echo "end 'driver'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn driver() as ExitCode"
  echo "end 'main'"
} > "$OUT"
