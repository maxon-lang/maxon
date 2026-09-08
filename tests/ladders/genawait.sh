#!/usr/bin/env bash
# Ladder generator for the AWAIT-LINEARITY walk — `SemanticCheck.checkLinearAwaitInFunction` and the
# per-function block table it builds (`IrModule.blockIndexById`).
#
# Usage: genawait.sh <funcs> <ifs> <outfile>
#   <funcs>  how many AWAIT-BEARING functions the module has
#   <ifs>    how many two-way branches each of them has, i.e. BLOCKS PER await-bearing function
#
# ⚠ `ScaleCorpus`'s manifest lists async under NOT GENERATED, so every `scale-test` column reads Δ0
# for a change to this path — the instrument's blind spot, not a result. This is the only way to
# measure it.
#
# THE TWO KNOBS ARE INDEPENDENT, for `genmutchain.sh`'s reason: the analysis is gated per function by
# `functionHasAwait`, and inside a function its cost follows the BLOCK count, so a ladder that doubled
# both together would sum a per-function term and a per-block term into one column and could not say
# which moved. Doubling <funcs> at fixed <ifs> is the control (the gate itself); doubling <ifs> at
# fixed <funcs> is the one that loads the block table.
#
# ⚠ COMPILE IT WITH `--target=x64-windows` ON A NON-x64 HOST. The green-thread substrate an `async`
# lowers to is x64-windows-gated at this rung (`StdToArm64Conversion` panics on `osReadClock`), so an
# arm64/wasm build dies in the BACKEND — after the front-end phase this ladder exists to measure, but
# with no `--metrics` file written. The front end is host-independent, so the x64 build measures the
# same work.
set -euo pipefail
FUNCS="$1"; IFS_N="$2"; OUT="$3"

{
  echo "// ladder: $FUNCS await-bearing functions, $IFS_N two-way branches each"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function producer(x LadderInt) returns LadderInt"
  echo -e "\treturn x + 1"
  echo "end 'producer'"
  echo ""

  f=0
  while [ "$f" -lt "$FUNCS" ]; do
    echo "function heavy$f(seed LadderInt) returns LadderInt"
    # The promise is spawned BEFORE the branch thicket and awaited AFTER it, so the reachability
    # walk from the await has to cross every block the thicket minted. An await at the top would
    # make the walk trivial and measure nothing.
    echo -e "\tlet p = async producer(seed)"
    echo -e "\tvar acc = seed"
    k=0
    while [ "$k" -lt "$IFS_N" ]; do
      d=$(( k % 7 + 1 ))
      echo -e "\tif acc > $k 'b$k'"
      echo -e "\t\tacc = acc + $d"
      echo -e "\tend 'b$k' else 'e$k'"
      echo -e "\t\tacc = acc - 1"
      echo -e "\tend 'e$k'"
      k=$(( k + 1 ))
    done
    echo -e "\tlet r = await p"
    echo -e "\treturn acc + r"
    echo "end 'heavy$f'"
    echo ""
    f=$(( f + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar t = 0"
  f=0
  while [ "$f" -lt "$FUNCS" ]; do
    echo -e "\tt = t + heavy$f($f)"
    f=$(( f + 1 ))
  done
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
