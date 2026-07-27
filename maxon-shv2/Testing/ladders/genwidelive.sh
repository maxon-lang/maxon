#!/usr/bin/env bash
# N call results, ALL live simultaneously in ONE straight-line basic block — the maximum
# simultaneous liveness a single block can carry, and the exact shape
# `specs-shv2/x64-large-frame-arg7.md` is the N=800 rung of. Both modes return 7.
#
# ⚠ NOT the same shape as `gen12i.sh`, which is N units each holding a handful of values
# live across ONE call: there the live set is bounded by the unit, and although the whole
# body is still one block, the splitter's dirty region is re-swept per split either way.
# Here liveness is Theta(N) at the last call, every value is live across every later call,
# so the number of SPLITS is Theta(N) as well and the two Theta(N) terms multiply.
#
# The values must be CALL RESULTS. Written as `let v = g + N` the allocator rematerializes
# the constant instead of splitting, no live range is ever cut, and the ladder measures
# nothing — which is why the spec itself uses an opaque call.
#
# <mode> — the two differ ONLY in where the N results are consumed, so their parse,
# lowering and isel costs are the same to within a handful of ops and the DIFFERENCE
# between their `regalloc:splitting` columns is the splitting cost and nothing else:
#   sum   — all N values feed ONE N-term sum at the end, so all N are live across every
#           call after their own def. Theta(N) splits, each over a Theta(N) block.
#   dead  — the CONTROL: each result is folded into a running accumulator on the very next
#           statement, so at most two values are ever live. NO value is ever split.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  sum|dead) ;;
  *) echo "genwidelive.sh: <mode> must be 'sum' or 'dead', got '$MODE'" >&2; exit 2 ;;
esac

# The sum of the arguments the N calls are handed, 0+1+...+(N-1). Subtracting it makes both
# modes return the same 7 whatever N is, so a ladder rung that miscompiles is visible as a
# wrong exit code rather than only as a time.
TOTAL=$(( N * (N - 1) / 2 ))

{
  echo "// ladder: $N cross-call values all live in one block, mode $MODE"
  echo "typealias WideNum = int(0 to 100000000)"
  echo ""
  echo "function scaleOpaque(x WideNum) returns WideNum"
  echo -e "\treturn x"
  echo "end 'scaleOpaque'"
  echo ""
  # Seven parameters, and `base` keeps the first six live past the calls: this is the frame
  # `x64-large-frame-arg7` guards, kept so the ladder and the spec measure one shape.
  echo "function wide(a WideNum, b WideNum, c WideNum, d WideNum, e WideNum, f WideNum, g WideNum) returns WideNum"
  echo -e "\tlet base = a + b + c + d + e + f"

  if [ "$MODE" = "sum" ]; then
    i=0
    while [ "$i" -lt "$N" ]; do
      echo -e "\tlet v$i = scaleOpaque($i)"
      i=$(( i + 1 ))
    done
    terms="v0"
    i=1
    while [ "$i" -lt "$N" ]; do
      terms="$terms + v$i"
      i=$(( i + 1 ))
    done
    echo -e "\tlet total = $terms"
  else
    echo -e "\tvar total = 0"
    i=0
    while [ "$i" -lt "$N" ]; do
      echo -e "\tlet v$i = scaleOpaque($i)"
      echo -e "\ttotal = total + v$i"
      i=$(( i + 1 ))
    done
  fi

  echo -e "\tlet spread = total - $TOTAL"
  echo -e "\treturn g + spread + base - 21"
  echo "end 'wide'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn wide(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7)"
  echo "end 'main'"
} > "$OUT"
