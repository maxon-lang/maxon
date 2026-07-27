#!/usr/bin/env bash
# THREE spill ops into ONE gap of the positional pressure index, N times over — the shape that
# exhausts `SlotGap` and forces `PressureIndex.makeRoom` to re-space.
#
# WHY THREE IS THE NUMBER. The gap between two ADJACENT ops A and B takes both the store anchored
# after A's def (`insertStoreAfter`: anchorPos + 1) and every reload anchored before B
# (`bodyPositionOfUse`: B's own position, which IS anchorPos + 1). So a pair of adjacent adds
#
#     let q0 = v0 + v1      <- A: its result outlives a later peak, so ONE store lands after it
#     let q1 = v2 + v3      <- B: reads two already-spilled values, so TWO reloads land before it
#
# puts THREE ops into one gap. `freeSlotAt` subdivides at the MIDPOINT, so a gap of `SlotGap == 4`
# fits log2(4) == 2 of them and the third exhausts it.
#
# WHY IT TAKES ALTERNATING PRESSURE HUMPS. Every reload of a peak's victims lands AFTER that peak and
# every store lands at a def BEFORE it, so within ONE hump the two never share a gap — which is why
# `genwidelive.sh`, whose single hump is far wider, never fires a re-space at any N. It is the rise of
# hump h+1 that makes hump h's `q` values spillable, and their stores land in hump h's fall.
#
# WHAT IT FOUND (2026-07-27). At the SHIPPING `SlotGap = 4`, with WIDE=12, this crashed the compiler
# at every rung: `SplitEdits.insertedSlots` records each spliced op's slot as it is seated, a re-space
# mid-batch MOVES those ops, and nothing repaired the slots already recorded —
# `PressureIndex.opAtSlot: slot 166 holds no op` out of `reindexSplitValues`. It now compiles and
# returns 7 at every rung, doing only LOCAL re-spaces (8-64 slots) and no function-wide one.
#
# ⚠ WIDE IS A CLIFF, NOT A DIAL. Below ~10 the humps do not overflow the 14-register integer file
# often enough to fire anything (WIDE=8 measures zero re-spaces at any N); above ~14 every value of
# every hump is live at once and the program is a legitimate E5001 instead of a ladder. 12 is the
# window. Both the shape and its knob must be re-checked against any change to the register file size.
set -euo pipefail
N="$1"; OUT="$2"

# Values per hump. See the cliff warning above before changing it.
WIDE=${WIDE:-12}

{
  echo "// ladder: $N pressure humps of $WIDE, each fall taking one store and two reloads per gap"
  echo "typealias WideNum = int(0 to 100000000)"
  echo ""
  echo "function scaleOpaque(x WideNum) returns WideNum"
  echo -e "\treturn x"
  echo "end 'scaleOpaque'"
  echo ""
  echo "function wide(g WideNum) returns WideNum"

  h=0
  while [ "$h" -lt "$N" ]; do
    # RISE: WIDE call results, every one live across the peak this hump makes.
    i=0
    while [ "$i" -lt "$WIDE" ]; do
      echo -e "\tlet h${h}v$i = scaleOpaque($i)"
      i=$(( i + 1 ))
    done
    # FALL: ADJACENT pair adds with nothing between them, every result kept live to the very end so
    # the NEXT hump's peak spills it and its store lands in this gap.
    i=0
    while [ "$i" -lt "$WIDE" ]; do
      j=$(( i + 1 ))
      echo -e "\tlet h${h}q$i = h${h}v$i + h${h}v$j"
      i=$(( i + 2 ))
    done
    h=$(( h + 1 ))
  done

  # Consume everything, so nothing above is dead and the humps really do stack.
  terms="g"
  h=0
  while [ "$h" -lt "$N" ]; do
    i=0
    while [ "$i" -lt "$WIDE" ]; do
      terms="$terms + h${h}q$i"
      i=$(( i + 2 ))
    done
    h=$(( h + 1 ))
  done
  echo -e "\tlet total = $terms"
  # Subtracting the sum makes the result 7 whatever N is, so a rung that miscompiles shows up as a
  # wrong exit code rather than only as a time.
  echo -e "\treturn total - total + 7"
  echo "end 'wide'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\treturn wide(7)"
  echo "end 'main'"
} > "$OUT"
