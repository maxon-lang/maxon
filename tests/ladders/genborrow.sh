#!/usr/bin/env bash
# COUNTED ELEMENT READS — four per-function shapes `ScaleCorpus` cannot drive past K = 1.
#
# An element read out of a managed array (`get`, `first`, the `for` element) takes its own counted
# reference. `ScaleCorpus`'s `v_marray.maxon` holds exactly ONE function (`scaleManagedArrayOps`) and
# exactly one `let mgN = try msN.get(0) otherwise ""` per managed-Array group, so every storage it reads
# is read ONCE and every read is bound to a name. A non-zero from that corpus is not evidence that K reads
# of one storage, or K reads interleaved with writes, scale in ONE body. This ladder is the instrument for
# that, and it is the twin of `genfor.sh` one rung on.
#
# THE SHAPES, one per mode:
#
#   • pending  — K element ASSIGNMENTS to one `var`: `s = try arr.get(0) otherwise ""` (an assignment,
#                not a binding), so every read releases the value the previous one left in `s`.
#   • subjects — K DISTINCT STORAGES, each read once and written once, each in its own block.
#   • dead     — ONE storage, K bound reads and then K writes. Every read's binding is last used
#                before the first write.
#   • trivial — THE CONTROL: the same array-method calls through the same doors with NOT ONE counted
#                read in the program (a TRIVIAL element type). It is what the counted read costs a
#                program that never takes one — the shape `genfor.sh`'s own `noloop` mode exists for.
#                A per-access tax hides inside the other three modes and stands out here.
#
# EVERYTHING GOES IN ONE FUNCTION, deliberately: every term these shapes can reach is PER-FUNCTION, so a
# ladder that widened the function COUNT would divide K by the very thing it doubles and read x2 for
# a quadratic. K doubles inside one body; the ladder doubles the program at the same time, so
# rung-over-rung x2.00 is linear and x4.00 is quadratic exactly as `scale-test` reads.
#
# Usage: genborrow.sh <K> <pending|subjects|dead|trivial> <out>
#   e.g. genborrow.sh 256 pending a.maxon   and   genborrow.sh 512 pending b.maxon
#        genborrow.sh 512 trivial c.maxon  is the same access count with no counted read at all.
#
# Compile-only: `main` never calls into the generated body with a populated array, and this ladder
# is about COMPILE cost — `scale-test` never runs its corpus either.
set -euo pipefail
K="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  pending|subjects|dead|trivial) ;;
  *) echo "genborrow.sh: mode must be pending, subjects, dead or trivial (got '$MODE')" >&2; exit 2 ;;
esac
if [ "$K" -lt 1 ]; then echo "genborrow.sh: K must be >= 1" >&2; exit 2; fi

{
  echo "// counted-read ladder: K=$K, mode $MODE"
  echo "typealias Int = int(i64.min to i64.max)"
  if [ "$MODE" = trivial ]; then
    echo "typealias IntArray = Array with Int"
  else
    echo "typealias StringArray = Array with String"
  fi
  echo ""
  echo "function fn() returns Int"

  case "$MODE" in
    pending)
      # An ASSIGNMENT, not a binding: every read replaces the value the previous one left in `s`, and the
      # `try … otherwise <value>` merges the read with its fallback at each.
      echo -e "\tvar arr = StringArray.create()"
      echo -e "\tvar s = \"\""
      echo -e "\tvar total = 0 as BytePos"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\ts = try arr.get(0) otherwise \"\""
        echo -e "\ttotal = total + s.byteLength()"
        n=$(( n + 1 ))
      done
      echo -e "\treturn total as Int"
      ;;
    subjects)
      # K DISTINCT storages, each read once and written once.
      #
      # ⚠ **EACH ONE LIVES IN ITS OWN BLOCK, AND THAT IS A FACT ABOUT REGISTERS, NOT ABOUT THE SHAPE**
      # — the same cap `genfor.sh`'s depth knob documents. K arrays declared side by side in one frame
      # are all live to the function's `end` (each owes a drop there), and the allocator REFUSES at
      # K=128 rather than spill (E5001). A block gives each one a scope to die in while the FUNCTION
      # still holds all K storages.
      echo -e "\tvar seed = 1 as BytePos"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\tif seed > 0 'b${n}'"
        echo -e "\t\tvar a${n} = StringArray.create()"
        echo -e "\t\tlet s${n} = try a${n}.get(0) otherwise \"\""
        echo -e "\t\tseed = seed + s${n}.byteLength()"
        echo -e "\t\ta${n}.clear()"
        echo -e "\tend 'b${n}'"
        n=$(( n + 1 ))
      done
      echo -e "\treturn seed as Int"
      ;;
    dead)
      # ONE storage, K reads and K writes. Every read's binding is last used before the first write.
      echo -e "\tvar arr = StringArray.create()"
      n=0
      while [ "$n" -lt "$K" ]; do echo -e "\tlet s${n} = try arr.get(0) otherwise \"\""; echo -e "\t_ = s${n}"; n=$(( n + 1 )); done
      n=0
      while [ "$n" -lt "$K" ]; do echo -e "\tarr.clear()"; n=$(( n + 1 )); done
      echo -e "\treturn 0"
      ;;
    trivial)
      # THE CONTROL. A TRIVIAL element type takes no counted reference whatever the method, while the
      # receiver still travels all the doors `parseArrayMethodCall` has.
      echo -e "\tvar arr = IntArray.create()"
      echo -e "\tvar total = 0 as Count"
      n=0
      while [ "$n" -lt "$K" ]; do
        echo -e "\tarr.push(${n})"
        echo -e "\ttotal = total + arr.count()"
        n=$(( n + 1 ))
      done
      echo -e "\treturn total as Int"
      ;;
  esac

  echo "end 'fn'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn 0 if fn() >= 0 else 1"
  echo "end 'main'"
} > "$OUT"
