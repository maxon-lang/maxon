#!/usr/bin/env bash
# Ladder generator for THE PARSER'S SCOPE DIMENSION — mutable bindings IN SCOPE (V) against
# CONSTRUCTS that merge them (C). `ScaleCorpus` doubles both at once (`LocalsPerFunctionBase`
# and `LongIfsBase`/`DeepBlocksBase`), so a cost of the form O(V x C) reads x4 per rung there
# and CANNOT be told from a genuine per-construct cost that happens to be expensive. Two knobs
# that must be varied independently are not one knob.
#
# Usage: genscope.sh <constructs> <locals> <if|ifelse|while|match|straight> <outfile>
#
#   constructs — how many merging statements the single generated function contains (C).
#   locals     — how many SCOPE-FILLER `var`s are declared ahead of them (V). Each is folded into
#                a working accumulator on the very next line, so it is IN SCOPE for every later
#                construct but LIVE for one instruction — which is the only way V can climb into
#                the thousands without tripping E5001. (Straight from `ScaleCorpus.fillerLocalsDecl`;
#                declaring V locals and reading them at the end makes all V live at once and E5001s
#                above ~13.)
#   shape      — which construct does the merging:
#                  if       — `constructs` sequential `if`s, no else
#                  ifelse   — sequential `if`/`else`, ~3 blocks each (the `deepBlocks` shape)
#                  while    — sequential single-level `while` loops (the loop-phi path)
#                  match    — sequential `match` statements over a 3-case enum
#                  straight — THE CONTROL: the same V, the same statement count, but the
#                             statements are plain assignments and merge nothing. Any cost that
#                             tracks V x C must read x2 here and x4 on the others.
#
# Every shape assigns exactly TWO of the six working accumulators per construct, so the CARRIED
# SET is a constant 2 while V grows — the whole point. Peak register pressure is the six
# accumulators plus one, well inside the 14-GPR pool, so nothing spills and `regalloc` cannot
# quietly become the thing being measured.
#
# ⭐ WHAT IT WAS BUILT FOR, and what it found. `phase:parse` was the ONLY superlinear phase on the
# scale corpus — CPU x2.02 x2.21 x2.52 x2.90 x3.33 down the six rungs while its ALLOCATIONS held
# at x1.85-x1.99, the time-only shape the memory columns are blind to by construction. On this
# ladder the two axes separated it in three readings (min-of-3 `phase:parse` CPU):
#
#   • C alone, V held at 256:  x1.72 x1.86 x1.89   — LINEAR
#   • V alone, C held at 400:  x1.46 x1.57 x1.83   — LINEAR (climbing to 2 from below, as the
#                                                    fixed per-compile constant is amortized)
#   • BOTH doubling:           x2.21 x2.45 x2.83   — the BEND, reproduced off the corpus
#   • `straight`, BOTH:        x1.80 x1.90 x1.89   — the CONTROL: same V, same statement count,
#                                                    no merging construct, and no bend at all
#
# A term that is linear in each knob separately and quadratic in the two together IS an O(V x C)
# term, and the control says it belongs to the merging construct rather than to program size. That
# pinned it to `Parser.assignedBindingsIn`, which filtered the WHOLE `mutableVars` list through a
# per-construct assigned-name set. After the fix the same points read x1.90 x1.93 x1.94 on
# `ifelse`, x1.90 x1.93 x1.95 on `while`, x1.91 x1.95 x1.98 on `match` and x1.90 x1.90 x1.94 on
# `if`. See the 2026-07-28 entry of `docs/optimization-log.md`.
#
# ⚠ **WHAT THIS LADDER CANNOT SEE, and what was measured by hand instead.** The carried set is a
# SUBSET of the scope, and every mode here holds it at 2 while V climbs — deliberately, because
# that is the shape real code has (maxA = 2 across all 18,571 constructs of the whole scale
# corpus). The opposite shape — ONE construct assigning EVERY binding in scope, so A == V — is the
# worst case for an ordered insert, and it is NOT generated here. Measured separately at V = 250 /
# 500 / 1000 / 2000 with one wide `if`, and at C = 8 wide `if`s with V = 125 / 250 / 500 / 1000:
# both stay LINEAR (x1.95-x2.03) and the fix costs +1% to +4%, because shifting a 4-byte position
# is ~30x cheaper than the `ByteArray` hash probe it replaced and the crossover sits far past any
# realistic A. If a rung ever makes wide-carried-set code ordinary, that is the shape to add here.
#
# Every chain starts from `scaleOpaque`, an out-of-line call, for `ScaleCorpus`'s reason: shv2 has
# no inliner and no interprocedural constant propagation, so a call result is opaque and
# `foldConstOperands` cannot fold the program flat. A ladder that folds away measures an empty
# compile and reports a beautiful straight line.
#
# ⚠ The per-line text is emitted directly rather than through a `$(...)` helper — a subshell per
# line makes the GENERATOR the thing being measured (see `genmutchain.sh`).
set -euo pipefail
C="$1"; V="$2"; SHAPE="$3"; OUT="$4"

case "$SHAPE" in
  if|ifelse|while|match|straight) ;;
  *)
    echo "genscope.sh: shape must be 'if', 'ifelse', 'while', 'match' or 'straight', got '$SHAPE'" >&2
    exit 2
    ;;
esac

{
  echo "// ladder: $SHAPE, constructs=$C, locals=$V"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"

  if [ "$SHAPE" = "match" ]; then
    echo "enum ScaleTag"
    echo -e "\tred"
    echo -e "\tgreen"
    echo -e "\tblue"
    echo "end 'ScaleTag'"
  fi

  echo "function big(a LadderInt) returns LadderInt"
  # The six working accumulators the constructs mutate — a BOUNDED set, so the carried set of
  # every construct is 2 however large V grows.
  w=0
  while [ "$w" -lt 6 ]; do
    echo -e "\tvar w${w} = a + $(( w + 1 ))"
    w=$(( w + 1 ))
  done

  # The V dimension: in scope for every construct below, live for one instruction each.
  v=0
  while [ "$v" -lt "$V" ]; do
    echo -e "\tvar l${v} = a + $(( v + 1 ))"
    echo -e "\tw$(( v % 6 )) = w$(( v % 6 )) + l${v}"
    v=$(( v + 1 ))
  done

  if [ "$SHAPE" = "match" ]; then
    echo -e "\tvar tag = ScaleTag.red"
    echo -e "\tif w0 > 3 'pick'"
    echo -e "\t\ttag = ScaleTag.blue"
    echo -e "\tend 'pick'"
  fi

  i=0
  while [ "$i" -lt "$C" ]; do
    p=$(( i % 6 ))
    q=$(( (i + 3) % 6 ))
    case "$SHAPE" in
      if)
        echo -e "\tif w${p} > ${i} 'c${i}'"
        echo -e "\t\tw${p} = w${p} + ${i}"
        echo -e "\t\tw${q} = w${q} + 1"
        echo -e "\tend 'c${i}'"
        ;;
      ifelse)
        echo -e "\tif w${p} > ${i} 'c${i}'"
        echo -e "\t\tw${p} = w${p} + ${i}"
        echo -e "\tend 'c${i}' else 'e${i}'"
        echo -e "\t\tw${q} = w${q} + 1"
        echo -e "\tend 'e${i}'"
        ;;
      while)
        echo -e "\tvar n${i} = 4"
        echo -e "\twhile n${i} > 0 'c${i}'"
        echo -e "\t\tw${p} = w${p} + ${i}"
        echo -e "\t\tw${q} = w${q} + 1"
        echo -e "\t\tn${i} = n${i} - 1"
        echo -e "\tend 'c${i}'"
        ;;
      match)
        echo -e "\tmatch tag 'c${i}'"
        echo -e "\t\tred then w${p} = w${p} + ${i}"
        echo -e "\t\tgreen then w${q} = w${q} + 1"
        echo -e "\t\tblue then w${p} = w${p} + 2"
        echo -e "\tend 'c${i}'"
        ;;
      straight)
        echo -e "\tw${p} = w${p} + ${i}"
        echo -e "\tw${q} = w${q} + 1"
        ;;
    esac
    i=$(( i + 1 ))
  done

  echo -e "\tvar sum = a"
  w=0
  while [ "$w" -lt 6 ]; do
    echo -e "\tsum = sum + w${w}"
    w=$(( w + 1 ))
  done
  echo -e "\treturn sum"
  echo "end 'big'"

  echo "function main() returns ExitCode"
  echo -e "\treturn (big(scaleOpaque(1)) and 7) as ExitCode"
  echo "end 'main'"
} > "$OUT"
