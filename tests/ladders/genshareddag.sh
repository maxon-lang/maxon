#!/usr/bin/env bash
# Ladder generator for the GENERIC-INSTANCE COMPILED-NAME path when the instance argument tree is a
# SHARED DAG rather than a tree — `ProgramSignatures.mangleGenericInstance` and everything in
# `phase:signatures` that materializes a structural name.
#
# Usage: genshareddag.sh <depth> <doubling|control> <outfile>
#
# ⭐ THE TWO MODES ARE THE MEASUREMENT. They emit the SAME number of declarations, the SAME number of
# generic instances, and differ in ONE token per line:
#
#   doubling   typealias P<k> = Pair with (P<k-1>, P<k-1>)   -- both arguments are the SAME alias
#   control    typealias P<k> = Pair with (P<k-1>, Count)    -- the second argument is a leaf
#
# A structural name is `Pair_<name(A)>_<name(B)>`, so under `doubling` |name(k)| = 2*|name(k-1)| + c and
# the name of the deepest alias is 2^depth characters. Under `control` it is O(depth). Subtracting one
# from the other cancels declaration count, instance count, parse cost and the whole stdlib, and leaves
# exactly the cost of materializing a name over a shared DAG.
#
# ⚠ THE DEEPEST ALIAS MUST BE `export`ed. Without it the compile stops at E3062 `unused typealias`, and
# the ladder then measures an error path — which still shows the blow-up (the work happens before the
# diagnostic) but is not a compile. `export` is what makes every rung a real, complete compile.
#
# ⚠ NOTHING BUILDS A VALUE OF THE DEEP TYPE, and it cannot: a `P22` value has 2^22 leaves. `main` builds
# a `P0` only. That is deliberate — it holds `regalloc` (which otherwise dominates any ladder) flat while
# the name path moves, so `phase:signatures` reads as its own column.
#
# ⚠ COST IS EXPONENTIAL IN `<depth>` UNDER `doubling`, so climb it carefully. MEASURED 2026-08-07 on
# x64-windows, `phase:signatures` bytes: depth 18 = 36,156,421 · depth 20 = 139,977,003 · depth 22 =
# 555,220,169 — ×2 per ADDED LINE OF SOURCE. Depth 25 is ~4.4 GB and past the memory budget; depth 30
# will not finish. The `control` at the same depths is 1,557,391 → 1,584,775 bytes, DEAD FLAT.
#
# ⚠ THE ALLOCATION COUNT IS FLAT IN BOTH MODES (31,441 → 31,671 across depth 16→22) and only the BYTE
# column moves. Same allocations, each one twice as big per added line — which is what identifies the
# cost as name LENGTH rather than as a walk that visits each node.
set -euo pipefail
DEPTH="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  doubling|control) ;;
  *) echo "genshareddag.sh: mode must be 'doubling' or 'control', got '$MODE'" >&2; exit 2 ;;
esac

{
  # Padded to a fixed width so the two modes' headers are byte-identical in length: the pair is only
  # worth subtracting if the two programs differ in the argument and in nothing else.
  printf '// ladder: shared-DAG instance names, depth %s, mode %-8s\n' "$DEPTH" "$MODE"
  echo "typealias Count = int(0 to u64.max)"
  echo ""
  echo "type Pair uses A, B"
  echo -e "\tvar a as A"
  echo -e "\tvar b as B"
  echo -e "\texport static function create(x A, y B) returns Self"
  echo -e "\t\treturn Self{a: x, b: y}"
  echo -e "\tend 'create'"
  echo "end 'Pair'"
  echo ""

  echo "typealias P0 = Pair with (Count, Count)"

  k=1
  while [ "$k" -le "$DEPTH" ]; do
    prev=$(( k - 1 ))
    # Only the deepest alias is exported; every earlier one is used by its successor.
    lead=""
    if [ "$k" -eq "$DEPTH" ]; then
      lead="export "
    fi
    if [ "$MODE" = "doubling" ]; then
      printf '%stypealias P%d = Pair with (P%d, P%d)\n' "$lead" "$k" "$prev" "$prev"
    else
      printf '%stypealias P%d = Pair with (P%d, Count)\n' "$lead" "$k" "$prev"
    fi
    k=$(( k + 1 ))
  done
  echo ""

  echo "function main() returns ExitCode"
  echo -e "\tlet base = P0.create(1, y: 2)"
  echo -e "\treturn 7 as ExitCode"
  echo "end 'main'"
} > "$OUT"
