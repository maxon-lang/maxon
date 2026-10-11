#!/usr/bin/env bash
# `for … in` — the construct `ScaleCorpus` CANNOT GENERATE, priced for its COUNTED SOURCE HOLD and
# its LIVE-COUNT RE-READ.
#
# A `for` over an array holds a counted reference to its source and re-reads the live count at every
# step. The hold is decided once the whole program's mutation summaries exist, so what it costs scales
# with the events inside the loop's window and with how many windows nest. `ScaleCorpus` contains not
# one `for`, so every column of a default `scale-test` run reads a flat Δ0 for all of it: this ladder is
# the instrument.
#
# THE QUESTIONS, one per knob:
#
#   • loops    — per-loop cost: the hold marker, five blocks and one `LoopContext` per `for`.
#   • depth    — nested windows. Every enclosing loop's window contains the inner loops' events, so a
#                term that is Θ(events × depth) shows here and nowhere else.
#   • accesses — events inside each window, which the hold's decision examines.
#   • mode     — what the window holds:
#                  array   the body never writes the iterated array, so every hold is provably
#                          elidable and the loop is the bare live-count re-read.
#                  writes  the body clears the iterated array, so no hold can be elided and each one
#                          lowers to a retain/release pair.
#                  range   literal bounds: no source, no hold. Against `array` at the same knobs it
#                          isolates the loop machinery from the hold.
#                  noloop  THE CONTROL: the same accesses with NOT ONE `for` in the program.
#
# Usage: genfor.sh <loops> <depth> <accesses> <array|writes|range|noloop> <out>
#   e.g. genfor.sh 512 1 2 array a.maxon    and    genfor.sh 1024 1 2 array b.maxon
#        genfor.sh 512 1 2 noloop c.maxon   is c.maxon's control at the same access count.
#
# DEPTH IS CAPPED NEAR 10 IN EVERY LOOP MODE BY THE ALLOCATOR: every `for` mints an index phi live
# across the loops nested inside it, and the allocator refuses (E5001) rather than spill inside a
# loop. Use `noloop` and `accesses` to move the event count instead.
#
# The loops trip over a one-element array (or `0 upto 1`), so the program is cheap to run; this ladder
# is about COMPILE cost and scale-test never runs its corpus.
set -euo pipefail
L="$1"; D="$2"; A="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  array|writes|range|noloop) ;;
  *) echo "genfor.sh: mode must be array, writes, range or noloop (got '$MODE')" >&2; exit 2 ;;
esac

if [ "$D" -lt 1 ]; then echo "genfor.sh: depth must be >= 1" >&2; exit 2; fi
if [ "$MODE" != "noloop" ] && [ "$D" -gt 10 ]; then
  echo "genfor.sh: depth $D will not compile in $MODE mode — E5001 caps a 'for' nest near 10" >&2
fi

FNS=$(( L / D ))
if [ "$FNS" -lt 1 ]; then FNS=1; fi

TABS="$(printf '\t%.0s' $(seq 1 72))"

{
  echo "// for ladder: $L loops x depth $D x $A accesses per level, mode $MODE"
  echo "typealias Int = int(i64.min to i64.max)"
  echo "typealias IntArray = Array with Int"
  echo ""

  f=0
  while [ "$f" -lt "$FNS" ]; do
    echo "function fn${f}(arr IntArray, other IntArray) returns Int"
    echo -e "\tvar total = 0"

    if [ "$MODE" = "noloop" ]; then
      echo -e "\ttotal = total + arr.count()"
      n=0
      while [ "$n" -lt $(( D * A )) ]; do
        echo -e "\ttotal = total + ${n}"
        echo -e "\tother.push(total)"
        n=$(( n + 1 ))
      done
    else
      d=0
      while [ "$d" -lt "$D" ]; do
        o="${TABS:0:$(( d + 1 ))}"
        i="${TABS:0:$(( d + 2 ))}"
        if [ "$MODE" = "range" ]; then
          echo -e "${o}for it${d} in 0 upto 1 'd${d}'"
        else
          echo -e "${o}for it${d} in arr 'd${d}'"
        fi
        n=0
        while [ "$n" -lt "$A" ]; do
          echo -e "${i}total = total + it${d}"
          echo -e "${i}other.push(total)"
          if [ "$MODE" = "writes" ]; then
            echo -e "${i}arr.clear()"
          fi
          n=$(( n + 1 ))
        done
        d=$(( d + 1 ))
      done
      d=$(( D - 1 ))
      while [ "$d" -ge 0 ]; do
        echo -e "${TABS:0:$(( d + 1 ))}end 'd${d}'"
        d=$(( d - 1 ))
      done
      if [ "$MODE" = "range" ]; then
        echo -e "\ttotal = total + arr.count()"
      fi
    fi

    echo -e "\treturn total"
    echo "end 'fn${f}'"
    echo ""
    f=$(( f + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar arr = IntArray.create()"
  echo -e "\tvar other = IntArray.create()"
  echo -e "\tarr.push(1)"
  echo -e "\tvar s = 0"
  f=0
  while [ "$f" -lt "$FNS" ]; do
    echo -e "\ts = s + fn${f}(arr, other: other)"
    f=$(( f + 1 ))
  done
  echo -e "\treturn 0 if s >= 0 else 1"
  echo "end 'main'"
} > "$OUT"
