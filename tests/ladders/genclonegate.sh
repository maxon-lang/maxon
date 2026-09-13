#!/usr/bin/env bash
# Ladder generator for the DEEP-CLONE GATE's full type-graph walk —
# `ProgramSignatures.aggregateSupportsDeepClone`, reached per emitted VALUE through
# `Parser.coOwnConcreteRecordForSink` -> `valueIsAnImageableDeepBox` ->
# `structBoxIsImageableWithSlots` -> `structBoxHasADeepCloner`.
#
# Usage: genclonegate.sh <sites> <fields> <gate|control> <outfile>
#
# ⚠ THE CORPUS CANNOT SEE THIS, AND NEITHER CAN A `.clone()` LADDER. `ScaleCorpus` doubles the number
# of FUNCTIONS at a fixed body size, and every term here is per-function-body. And the population is
# not `.clone()` tokens: the gate is asked at every borrowed struct value that reaches a DURABLE SINK —
# a consumed argument, a field store, an element push — which is an ordinary shape a program has
# hundreds of without writing `.clone()` once.
#
# THE TWO KNOBS ARE THE PRODUCT, AND THEY ARE INDEPENDENT:
#   <sites>  — how many times the gate is ASKED (one consumed argument each, all in ONE function)
#   <fields> — how big the type GRAPH under the gated struct is (that many `Array with Integer` fields)
# The cost the gate adds is O(sites x fields); either knob alone moves one factor of it.
#
# ⭐ `control` IS THE CONTROL AND IT IS THE POINT OF THIS LADDER. It emits the IDENTICAL site count
# through the IDENTICAL sink door over a struct with the identical field graph, plus ONE trailing
# `String` field. A `String` field classifies `notImageable` (`structImageSlotKind` admits only scalars
# and nested boxes), so `everyStructSlotIsImageable` answers false and the `and` short-circuits BEFORE
# the graph walk.
#
# ⚠ THE CONTROL IS NOT PURE, AND THE RESIDUAL TERM HAS TO BE NAMED. A gate the walk ADMITS sends
# `coOwnConcreteRecordForSink` down `emitOwnDeepBoxCopy` where the control takes `emitOwnBoxForSink`, so
# gate−control is the walk PLUS one per-site emission difference. That term is O(1) per site and flat in
# `fields`, exactly like the walk's own per-ask overhead — so FLATNESS IN `fields` DOES NOT ATTRIBUTE A
# RESIDUAL TO EITHER. What it is safe to read off is a BEFORE/AFTER of gate−control across two compilers:
# the emission term is identical in both and subtracts out.
#
# ⚠ EVERY SITE LANDS IN ONE `main`, so `regalloc` dominates the wall clock and bends on its own known
# term (`genopaquefields.sh` carries the same warning). Read `phase:parse`, which is where the gate
# runs; the back-end columns are the same in both modes and are not this ladder's reading.
#
# Both modes return **7** at every rung, so a rung that miscompiles shows up as a wrong exit code
# rather than merely as a time.
set -euo pipefail
SITES="$1"; FIELDS="$2"; MODE="$3"; OUT="$4"

case "$MODE" in
  gate|control) ;;
  *) echo "genclonegate.sh: mode must be 'gate' or 'control', got '$MODE'" >&2; exit 2 ;;
esac

{
  printf '// ladder: %s gate site(s) over a %s-field graph, mode %s\n' "$SITES" "$FIELDS" "$MODE"
  echo "typealias Integer = int(i64.min to i64.max)"
  echo "typealias Ints = Array with Integer"
  echo ""

  echo "function scaleOpaque(a Integer) returns Integer"
  echo -e "\tif a > 1000000 'big'"
  echo -e "\t\treturn a - 1"
  echo -e "\tend 'big'"
  echo -e "\treturn a"
  echo "end 'scaleOpaque'"
  echo ""

  # Every field is a container instance, which `structImageSlotKind` classifies `pointer` — so the
  # struct passes the first clause and the gate reaches the graph walk.
  echo "type Node"
  f=0
  while [ "$f" -lt "$FIELDS" ]; do
    printf '\texport var f%d as Ints\n' "$f"
    f=$(( f + 1 ))
  done
  if [ "$MODE" = "control" ]; then
    echo -e "\texport var tag as String"
  fi
  echo -e "\texport static function create() returns Self"
  printf '\t\treturn Self{'
  f=0
  while [ "$f" -lt "$FIELDS" ]; do
    if [ "$f" -gt 0 ]; then printf ', '; fi
    printf 'f%d: Ints.create()' "$f"
    f=$(( f + 1 ))
  done
  if [ "$MODE" = "control" ]; then
    if [ "$FIELDS" -gt 0 ]; then printf ', '; fi
    printf 'tag: "t"'
  fi
  printf '}\n'
  echo -e "\tend 'create'"
  echo "end 'Node'"
  echo ""

  # `Cell.create` CONSUMES its parameter into durable storage, so a borrowed `Node` handed to it is the
  # exact position `coOwnBorrowedForConsume` gates.
  echo "type Cell"
  echo -e "\texport var n as Node"
  echo -e "\texport static function create(n Node) returns Self"
  echo -e "\t\treturn Self{n: n}"
  echo -e "\tend 'create'"
  echo "end 'Cell'"
  echo ""
  echo "typealias Cells = Array with Cell"
  echo ""

  echo "function main() returns ExitCode"
  echo -e "\tvar src = Cell.create(Node.create())"
  echo -e "\tvar out = Cells.create()"
  echo -e "\tvar acc = scaleOpaque(1)"
  s=0
  while [ "$s" -lt "$SITES" ]; do
    # `src.n` is a FIELD READ — a borrowed aggregate — so each site asks the gate afresh.
    echo -e "\tout.push(Cell.create(src.n))"
    echo -e "\tacc = acc + scaleOpaque(out.count())"
    s=$(( s + 1 ))
  done
  printf '\tif out.count() == %d and acc > 0 %s\n' "$SITES" "'ok'"
  echo -e "\t\treturn 7 as ExitCode"
  echo -e "\tend 'ok'"
  echo -e "\treturn 1 as ExitCode"
  echo "end 'main'"
} > "$OUT"
