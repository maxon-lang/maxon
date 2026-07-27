#!/usr/bin/env bash
# Ladder generator for the GENERIC-INSTANCE COMPILED-NAME path — `ProgramSignatures.mangleGenericInstance`
# and the `reservedIfDeclared` re-probe that mints a CONTESTED name behind the reserved `__` prefix.
#
# Usage: geninstances.sh <instances> <chain> <plain|contested|control> <outfile>
#
# `ScaleCorpus` DOES generate generics (its manifest lists a `Base with Arg` knob, and the standing
# ladder's instance count is 3 + 12x2^rung), so `scale-test` can see the per-instance cost — that is how
# the -1 allocation / -16 bytes per instance of 2026-07-26 was read straight off it. What the corpus can
# NOT express is the other half of this path:
#
#   * a compiled instance name a DECLARATION also claims, which is the only thing that makes
#     `reservedIfDeclared` mint anything at all — the corpus declares no such name and never will;
#   * a re-probe DEEPER THAN ONE, which needs a declaration whose own name starts with `__` and is
#     therefore E2051. Such a program cannot compile, so no corpus can contain it.
#
# ⭐ THE THREE KNOBS ARE SEPARABLE, for `genmutchain.sh`'s reason. `<instances>` alone is program size.
# `<plain|contested|control>` flips the CONTEST at a byte-identical program size — `contested` declares
# `type Box_A<i>`, the exact string instance `i` compiles to; `control` declares `type Zed_A<i>`, the same
# bytes claiming nothing. Subtracting one from the other is the per-contest cost with the extra
# declarations' own sweep cost cancelled out. `<chain>` is the re-probe DEPTH, planted against instance 0
# only, so it moves probe count without moving instance count.
#
# ⚠ ANY `<chain>` ABOVE 0 EMITS A PROGRAM THAT CANNOT COMPILE — a `__`-prefixed declaration is E2051, and
# that is exactly the point: it is the only shape in which the loop runs more than twice, which is why
# "at most 2 probes on anything that compiles" is a property and not a hope. `--metrics` is NOT written
# for a failed build, so time a chain run with a wall clock (min of 5) against the byte-identical
# `control` at the same `<chain>`, and read the DIFFERENCE.
#
# ⚠ Type-argument names are FIXED-WIDTH (`A000123`), so every compiled name is the same length at every
# rung and the byte column moves with the instance COUNT rather than with how many digits N has.
#
# ⚠ Nothing is referenced from `main()`, deliberately. The declaration SWEEP interns
# `typealias I = Box with A` whether or not it is used and `deriveInstanceNames` mangles every instance it
# interned, so an empty `main` measures the name path with `regalloc` — which otherwise dominates a
# doubling ladder — held constant.
set -euo pipefail
INSTANCES="$1"; CHAIN="$2"; MODE="$3"; OUT="$4"

case "$MODE" in
  plain)     STEM="" ;;
  contested) STEM="Box" ;;
  control)   STEM="Zed" ;;
  *) echo "geninstances.sh: mode must be 'plain', 'contested' or 'control', got '$MODE'" >&2; exit 2 ;;
esac

# The chain has to hang off a name that is claimed at all, so `plain` grows its chain against `Zed`
# (claiming nothing) rather than silently pretending to measure a re-probe it cannot cause.
CHAIN_STEM="$STEM"
if [ -z "$CHAIN_STEM" ]; then
  CHAIN_STEM="Zed"
fi

{
  # The mode is padded to a fixed width so `contested` and `control` are BYTE-IDENTICAL in length —
  # the whole value of the pair is that subtracting them cancels everything but the contest, and a
  # 2-character difference in a comment would put that claim on a program of a different size.
  printf '// ladder: %s generic instance(s), chain depth %s, mode %-9s\n' "$INSTANCES" "$CHAIN" "$MODE"
  echo "typealias Num = int(0 to 100)"
  echo ""
  echo "type Box uses T"
  echo -e "\texport var v as T"
  echo -e "\texport static function create(v T) returns Self"
  echo -e "\t\treturn Self{v: v}"
  echo -e "\tend 'create'"
  echo "end 'Box'"
  echo ""

  i=0
  while [ "$i" -lt "$INSTANCES" ]; do
    printf 'typealias A%06d = int(0 to 100)\n' "$i"
    i=$(( i + 1 ))
  done
  echo ""

  i=0
  while [ "$i" -lt "$INSTANCES" ]; do
    printf 'typealias I%06d = Box with A%06d\n' "$i" "$i"
    i=$(( i + 1 ))
  done
  echo ""

  if [ -n "$STEM" ]; then
    i=0
    while [ "$i" -lt "$INSTANCES" ]; do
      printf 'type %s_A%06d\n\texport var n as Num\nend '\''%s_A%06d'\''\n\n' "$STEM" "$i" "$STEM" "$i"
      i=$(( i + 1 ))
    done
  fi

  # Each link is exactly the candidate the previous probe minted, so under `contested` probe j finds
  # link j declared and mints link j+1. Under `control` the same bytes name a stem no instance compiles to.
  prefix=""
  j=1
  while [ "$j" -le "$CHAIN" ]; do
    prefix="__$prefix"
    printf 'type %s%s_A000000\n\texport var n as Num\nend '\''%s%s_A000000'\''\n\n' \
      "$prefix" "$CHAIN_STEM" "$prefix" "$CHAIN_STEM"
    j=$(( j + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
