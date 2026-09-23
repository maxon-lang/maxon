#!/usr/bin/env bash
# Ladder generator for the CLONE GATE'S PER-CONFORMER TERM — `typeSupportsDeepClone`'s `interfaceRef`
# arm, which asks `interfaceConformersSupportDeepClone` -> `cloneConformerRosterOf` (a pass over every
# struct name AND every enum) and then one aggregate graph walk PER CONFORMER, for every field held at
# an interface type the gate reaches.
#
# Usage: genexistentialclone.sh <holders> <conformers> <types> <gate|control> <outfile>
#
# ⚠ THE CORPUS CANNOT SEE THIS AT ALL. No `ScaleCorpus` program holds a field at an interface type, so
# every column reads a delta of zero — and that zero means UNREACHED, not cheap.
#
# THE THREE KNOBS ARE THE PRODUCT:
#   <holders>    — how many times the gate is ASKED (one record type per holder, each cloned once)
#   <conformers> — how long the ROSTER is (that many types implementing `Shape`)
#   <types>      — how much declaration registry the roster SCAN walks (unrelated `Pad` structs)
# The term this isolates is per (holder x conformer), so it shows only when both are doubled together;
# either axis alone moves one factor of it.
#
# ⭐ `control` IS THE CONTROL. It is the same program with the interface-typed field replaced by a
# `String` one, so the `interfaceRef` arm is never reached and no roster is ever listed — everything
# else, down to the holder count and the clone sites, is identical.
#
# Both modes return **7** at every rung, so a rung that miscompiles shows up as a wrong exit code
# rather than merely as a time.
set -euo pipefail
HOLDERS="$1"; CONFORMERS="$2"; TYPES="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  gate|control) ;;
  *) echo "genexistentialclone.sh: mode must be 'gate' or 'control', got '$MODE'" >&2; exit 2 ;;
esac

if [ "$CONFORMERS" -lt 1 ]; then
  echo "genexistentialclone.sh: <conformers> must be at least 1 (holder 0 constructs C0)" >&2
  exit 2
fi

{
  printf '// ladder: %s holder(s), %s conformer(s), %s pad type(s), mode %s\n' "$HOLDERS" "$CONFORMERS" "$TYPES" "$MODE"
  echo "typealias Integer = int(i64.min to i64.max)"
  echo ""

  echo "function scaleOpaque(a Integer) returns Integer"
  echo -e "\tif a > 1000000 'big'"
  echo -e "\t\treturn a - 1"
  echo -e "\tend 'big'"
  echo -e "\treturn a"
  echo "end 'scaleOpaque'"
  echo ""

  echo "interface Shape"
  echo -e "\tfunction area() returns Integer"
  echo "end 'Shape'"
  echo ""

  c=0
  while [ "$c" -lt "$CONFORMERS" ]; do
    printf 'type C%d implements Shape\n' "$c"
    echo -e "\tvar v as Integer"
    echo -e "\tstatic function create(v Integer) returns Self"
    echo -e "\t\treturn Self{v: v}"
    echo -e "\tend 'create'"
    echo -e "\tfunction area() returns Integer"
    echo -e "\t\treturn self.v"
    echo -e "\tend 'area'"
    printf "end 'C%d'\n" "$c"
    c=$(( c + 1 ))
  done
  echo ""

  t=0
  while [ "$t" -lt "$TYPES" ]; do
    printf 'type Pad%d\n' "$t"
    echo -e "\texport var v as Integer"
    echo -e "\tstatic function create(v Integer) returns Self"
    echo -e "\t\treturn Self{v: v}"
    echo -e "\tend 'create'"
    printf "end 'Pad%d'\n" "$t"
    t=$(( t + 1 ))
  done
  echo ""

  h=0
  while [ "$h" -lt "$HOLDERS" ]; do
    printf 'type H%d\n' "$h"
    if [ "$MODE" = "gate" ]; then
      echo -e "\texport var s as Shape"
    else
      echo -e "\texport var s as String"
    fi
    echo -e "\texport var k as Integer"
    if [ "$MODE" = "gate" ]; then
      echo -e "\tstatic function create(s Shape, k Integer) returns Self"
    else
      echo -e "\tstatic function create(s String, k Integer) returns Self"
    fi
    echo -e "\t\treturn Self{s: s, k: k}"
    echo -e "\tend 'create'"
    printf "end 'H%d'\n" "$h"
    h=$(( h + 1 ))
  done
  echo ""

  echo "function main() returns ExitCode"
  echo -e "\tvar acc = scaleOpaque(1)"
  h=0
  while [ "$h" -lt "$HOLDERS" ]; do
    if [ "$MODE" = "gate" ]; then
      printf '\tlet src%d = H%d.create(C0.create(%d), k: %d)\n' "$h" "$h" "$h" "$h"
    else
      printf '\tlet src%d = H%d.create("t", k: %d)\n' "$h" "$h" "$h"
    fi
    printf '\tlet dup%d = src%d.clone()\n' "$h" "$h"
    printf '\tacc = acc + scaleOpaque(dup%d.k)\n' "$h"
    h=$(( h + 1 ))
  done
  printf '\tif acc > 0 %s\n' "'ok'"
  echo -e "\t\treturn 7 as ExitCode"
  echo -e "\tend 'ok'"
  echo -e "\treturn 1 as ExitCode"
  echo "end 'main'"
} > "$OUT"
