#!/usr/bin/env bash
# `for … in` — the construct `ScaleCorpus` CANNOT GENERATE, and the four doors of its
# ITERATION LOCK.
#
# ⚠ THE CORPUS IS STRUCTURALLY BLIND TO THIS RUNG. `for` did not parse until P1.8 slice A,
# so the generated ladder contains not one loop of this shape: the lowering, the
# `__arr_get_unchecked` element read and `Parser.iterationLockedBindings` are invisible to
# every column of a default `scale-test` run — allocations, bytes AND CPU alike. A Δ0 there
# measures the instrument, not the cost. This ladder is the instrument for it.
#
# THE QUESTIONS, one per knob, and they have different shapes:
#
#   • loops       — per-loop cost in the parser and the block/phi wiring. Five blocks and one
#                   `LoopContext` per `for`; a term that is per-LOOP shows here and nowhere else.
#   • depth       — `Parser.bindingIsIterationLocked` is a LINEAR SCAN of the lock stack, and the
#                   stack's length IS the enclosing `for … in` nesting depth. This is the only
#                   knob that can turn it quadratic; a flat loop-count ladder would never show it.
#   • accesses    — the lock is read at FOUR doors on ordinary binding-access paths
#                   (`immutableReceiverNameOf`, `parseFieldChainCall`, `selfFieldIsWritable`,
#                   `requireMutableBinding`), so the cost is per ACCESS, not per loop.
#   • mode        — `noloop` is THE CONTROL, and it is the reading that matters most: the same
#                   accesses through the same four doors with NOT ONE `for` in the program. It
#                   is what the feature costs a program that never uses it, which is the exact
#                   shape of the defect the P1.7a 2b-i pass found (a payload-carrying union that
#                   heap-boxed to answer "no conformer at all", taxing every struct method call
#                   in every program). A per-access tax hides inside a loop ladder and stands
#                   out here.
#
# Usage: genfor.sh <loops> <depth> <accesses> <array|range|noloop> <out>
#   e.g. genfor.sh 512 1 2 array a.maxon    and    genfor.sh 1024 1 2 array b.maxon
#        genfor.sh 512 1 2 noloop c.maxon   is c.maxon's control at the same access count.
#
# ⚠ **DEPTH IS HARD-CAPPED NEAR 10 IN `array`/`range` MODE, AND THAT IS A FACT ABOUT REGISTERS,
# NOT ABOUT THE SCAN.** Every `for` mints an INDEX PHI that is live across every loop nested
# inside it, so a nest of depth D holds D values at once and the allocator REFUSES rather than
# spill inside a loop (E5001). Measured with the body below: depth 10 compiles, depth 11 is
# "needs 1 more register(s) than are available". `gennest.sh` dodges exactly this by giving every
# `while` level the SAME guard variable — a trick a `for` cannot use, because the counter is
# minted by the lowering and not by the program. So the lock stack can never be deeper than ~10
# in a program that compiles at all, which BOUNDS the linear scan by a constant the compiler
# itself enforces. Use `noloop` and the `accesses` knob to move the door count instead.
#
# ⚠ **`range` MODE LOCKS NOTHING, AND THAT IS WHY IT IS HERE**: its bounds are LITERALS, so
# `lockIterationSource` finds no base name and the stack stays empty however deep the nest goes.
# Against `array` at the same knobs it therefore isolates the LOOP MACHINERY (five blocks, the
# index phi, the step block) from the LOCK. ⚠⚠ A range whose START bound is a NAME is a different
# program: the lock is taken before the range/array fork, so that name IS locked. Do not spell one
# here expecting the empty-stack reading.
#
# The loops are written to trip over a one-element array (or `0 upto 1`) so the program is cheap
# to run if anyone runs it; this ladder is about COMPILE cost and scale-test never runs its corpus.
set -euo pipefail
L="$1"; D="$2"; A="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  array|range|noloop) ;;
  *) echo "genfor.sh: mode must be array, range or noloop (got '$MODE')" >&2; exit 2 ;;
esac

if [ "$D" -lt 1 ]; then echo "genfor.sh: depth must be >= 1" >&2; exit 2; fi
if [ "$MODE" != "noloop" ] && [ "$D" -gt 10 ]; then
  echo "genfor.sh: depth $D will not compile in $MODE mode — E5001 caps a 'for' nest near 10 (see header)" >&2
fi

# One NEST per function, so a function stays a constant size as `loops` doubles and the
# allocator's own curves stay out of the reading (they are measured by genwidelive.sh).
FNS=$(( L / D ))
if [ "$FNS" -lt 1 ]; then FNS=1; fi

# Indentation by SUBSTRING of one pre-built tab run, not by a `$(…)` per line. A command
# substitution forks, and at a few thousand lines that fork dominates the generator's own
# runtime by orders of magnitude on a Windows checkout.
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
      # THE CONTROL: the same accesses through the same doors, and no `for` anywhere. The
      # access count matches a `depth x accesses` nest exactly, so the two are comparable.
      n=0
      while [ "$n" -lt $(( D * A )) ]; do
        # requireMutableBinding (a write to a binding) and immutableReceiverNameOf (a method
        # on a binding receiver) — two of the four doors, one statement each.
        echo -e "\ttotal = total + ${n}"
        echo -e "\tother.push(total)"
        n=$(( n + 1 ))
      done
    else
      d=0
      while [ "$d" -lt "$D" ]; do
        o="${TABS:0:$(( d + 1 ))}"
        i="${TABS:0:$(( d + 2 ))}"
        # EVERY level iterates the SAME `arr`, deliberately: the lock stack is then `depth`
        # deep and NO access below it matches, so every door walks the WHOLE stack. That is
        # the worst case the scan can be asked for, which is what a ladder is for.
        if [ "$MODE" = "array" ]; then
          echo -e "${o}for it${d} in arr 'd${d}'"
        else
          echo -e "${o}for it${d} in 0 upto 1 'd${d}'"
        fi
        n=0
        while [ "$n" -lt "$A" ]; do
          echo -e "${i}total = total + it${d}"
          echo -e "${i}other.push(total)"
          n=$(( n + 1 ))
        done
        d=$(( d + 1 ))
      done
      d=$(( D - 1 ))
      while [ "$d" -ge 0 ]; do
        echo -e "${TABS:0:$(( d + 1 ))}end 'd${d}'"
        d=$(( d - 1 ))
      done
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
