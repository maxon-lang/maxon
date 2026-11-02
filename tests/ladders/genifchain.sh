#!/usr/bin/env bash
# THE IF-CHAIN LADDER — ONE function `chain` of N `if seed == I` blocks over a `var`, for the move-state
# join at every `if` and for the extent and write lookups an `else if` chain repeats per arm.
#
#     managed   N sequential `if … cells = grow(cells) … else cells = fresh() …` blocks over a managed
#               `var`: each join meets the door of every earlier block on its else path.
#     arms      ONE `else if` chain of N arms that each `cells = grow(cells)`, then a final `else`.
#     scalar    the `managed` shape over an `int` `var`: the CONTROL, which hands no record to a door.
#
# `managed` and `arms` print 1 at every N and `scalar` prints 2 (0 at N = 1), so a rung that miscompiles
# shows up as a wrong total.
#
# Usage: genifchain.sh <n> <managed|arms|scalar> <outfile>
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  managed|arms|scalar) ;;
  *) echo "genifchain.sh: <mode> must be 'managed', 'arms' or 'scalar', got '$MODE'" >&2; exit 2 ;;
esac

{
  printf 'typealias Tally = int(0 to u64.max)\ntypealias TallyArray = Array with Tally\n\n'
  printf 'function grow(cells TallyArray) returns TallyArray\n\tcells.push(1)\n\n\treturn cells\nend %s\n\n' "'grow'"
  printf 'function fresh() returns TallyArray\n\tvar cells = TallyArray.create()\n\tcells.push(2)\n\n\treturn cells\nend %s\n\n' "'fresh'"
  printf 'function chain(seed Tally) returns Tally\n\tvar cells = TallyArray.create()\n'

  if [ "$MODE" = "scalar" ]; then
    printf '\tvar total = 0 as Tally\n'
  fi

  printf '\n'

  for ((i = 0; i < N; i++)); do
    case "$MODE" in
      managed)
        printf '\tif seed == %d %s\n\t\tcells = grow(cells)\n\tend %s else %s\n\t\tcells = fresh()\n\tend %s\n\n' "$i" "'c$i'" "'c$i'" "'o$i'" "'o$i'"
        ;;
      scalar)
        printf '\tif seed == %d %s\n\t\ttotal = seed\n\tend %s else %s\n\t\ttotal = 2\n\tend %s\n\n' "$i" "'c$i'" "'c$i'" "'o$i'" "'o$i'"
        ;;
      arms)
        if [ "$i" -eq 0 ]; then
          printf '\tif seed == %d %s\n\t\tcells = grow(cells)\n' "$i" "'c$i'"
        else
          printf '\tend %s else if seed == %d %s\n\t\tcells = grow(cells)\n' "'c$((i - 1))'" "$i" "'c$i'"
        fi
        ;;
      *)
        echo "genifchain.sh: unhandled mode '$MODE'" >&2; exit 2
        ;;
    esac
  done

  if [ "$MODE" = "arms" ]; then
    printf '\tend %s else %s\n\t\tcells = fresh()\n\tend %s\n\n' "'c$((N - 1))'" "'other'" "'other'"
  fi

  if [ "$MODE" = "scalar" ]; then
    printf '\treturn (cells.count() as Tally) + total + seed\nend %s\n\n' "'chain'"
  else
    printf '\treturn (cells.count() as Tally) + seed\nend %s\n\n' "'chain'"
  fi

  printf 'function main() returns ExitCode\n\tprint("{chain(0)}\\n")\n\treturn 0\nend %s\n' "'main'"
} > "$OUT"
