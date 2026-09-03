#!/usr/bin/env bash
# Ladder generator for BLOCKS PER FUNCTION vs FUNCTIONS PER MODULE — the two axes a module can
# grow along that `ScaleCorpus` doubles TOGETHER, and which therefore cannot be told apart on it.
#
# Usage: genblocks.sh <n> <blocks|funcs|straight> <outfile>
#
#   blocks    — ONE function with `n` sequential `if`s, so ~3n BASIC BLOCKS in a single function
#               and a per-function structure of size n. This is the shape ARCHITECTURE.md's own
#               headline is measured on (a 3,200-`if` function) and the one `ScaleCorpus`'s
#               `longFunction` / `deepBlocks` knobs generate.
#   funcs     — `n` functions of FIXED size (one `if` each), so the same block count spread across
#               n per-function structures. The CONTROL: a cost that is per-block reads the same on
#               both axes; a cost that is quadratic in blocks-PER-FUNCTION reads ×2 here and ×4
#               there, which is exactly how the two are separated.
#   straight  — ONE function, ONE block, `n` arithmetic ops. The second control: it isolates
#               growth of the emitted BYTE BUFFER from growth of the block COUNT, so "the code
#               array is the problem" can be ruled in or out without guessing.
#
# ⭐ WHAT IT WAS BUILT FOR, and what it found. `phase:encode` read ×3.0 per doubling on the scale
# corpus while its ALLOCATIONS read ×1.96 — the time-only shape the memory columns are blind to.
# On `blocks` the encode phase read ×3.21 ×3.37 ×3.69; on `funcs` it read ×1.80 ×2.08 ×2.08 ×2.02
# and on `straight` ×1.86 ×1.92 ×2.00 ×1.95. So the cost was quadratic in blocks per FUNCTION and
# linear in everything else — which pinned it to the per-function block-offset table and, from
# there, to `Map`'s unmixed integer hashing (see `stdlib/Interfaces.spreadHash`).
#
# Every chain starts from `scaleOpaque`, an out-of-line call, for `ScaleCorpus`'s reason: shv2 has
# no inliner and no interprocedural constant propagation, so a call result is opaque and
# `foldConstOperands` cannot fold the program flat. A ladder that folds away measures an empty
# compile and reports a beautiful straight line.
#
# ⚠ The per-line text is emitted directly rather than through a `$(...)` helper — a subshell per
# line makes the GENERATOR the thing being measured (see `genmutchain.sh`).
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"

{
  echo "// ladder: $SHAPE, n=$N"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"

  case "$SHAPE" in
    blocks)
      echo "function big(a LadderInt) returns LadderInt"
      echo -e "\tvar acc = scaleOpaque(a)"
      i=0
      while [ "$i" -lt "$N" ]; do
        echo -e "\tif acc > $i 'b${i}'"
        echo -e "\t\tacc = acc + $i"
        echo -e "\tend 'b${i}'"
        i=$(( i + 1 ))
      done
      echo -e "\treturn acc"
      echo "end 'big'"
      echo "function main() returns ExitCode"
      echo -e "\treturn (big(1) and 7) as ExitCode"
      echo "end 'main'"
      ;;

    funcs)
      i=0
      while [ "$i" -lt "$N" ]; do
        echo "function f${i}(a LadderInt) returns LadderInt"
        echo -e "\tvar acc = a"
        echo -e "\tif acc > 3 'g'"
        echo -e "\t\tacc = acc + 1"
        echo -e "\tend 'g'"
        echo -e "\treturn scaleOpaque(acc)"
        echo "end 'f${i}'"
        i=$(( i + 1 ))
      done
      echo "function main() returns ExitCode"
      echo -e "\tvar acc = 1"
      i=0
      while [ "$i" -lt "$N" ]; do
        echo -e "\tacc = acc + f${i}(acc)"
        i=$(( i + 1 ))
      done
      echo -e "\treturn (acc and 7) as ExitCode"
      echo "end 'main'"
      ;;

    straight)
      echo "function big(a LadderInt) returns LadderInt"
      echo -e "\tvar acc = scaleOpaque(a)"
      i=0
      while [ "$i" -lt "$N" ]; do
        echo -e "\tacc = acc + $(( i % 97 )) + acc"
        i=$(( i + 1 ))
      done
      echo -e "\treturn acc"
      echo "end 'big'"
      echo "function main() returns ExitCode"
      echo -e "\treturn (big(1) and 7) as ExitCode"
      echo "end 'main'"
      ;;

    *)
      echo "genblocks.sh: shape must be 'blocks', 'funcs' or 'straight', got '$SHAPE'" >&2
      exit 2
      ;;
  esac
} > "$OUT"
