#!/usr/bin/env bash
# THE RETURNED-PARAMETER LADDER — ONE function that rebinds a `var` through a function returning its parameter
# N times, so the borrowed-return rewrite, the retain/release pairing and the join bypass each meet N sites in a
# single function body.
#
#     straight   N `cells = touch(cells)` statements in ONE block: N call sites rewritten, N retain/release
#                pairs adjacent in one block, one block that holds all of them.
#     joins      N `if … cells = touch(cells) … else cells = fresh() …` blocks in a row: N pairs that each
#                cross a join whose other predecessor does not retain, so N bypasses (and N image-fork
#                diamonds) land in one function.
#
# Both print the same count for a given N, so a rung that miscompiles shows up as a wrong printed total.
#
# Usage: genreturnedparams.sh <n> <straight|joins> <outfile>
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

{
  printf 'typealias Tally = int(0 to u64.max)\ntypealias TallyArray = Array with Tally\n\n'
  printf 'function fill(cells TallyArray, count Tally)\n\tfor i in 0 upto count %s\n\t\tcells.push(i)\n\tend %s\nend %s\n\n' "'each'" "'each'" "'fill'"
  printf 'function touch(cells TallyArray) returns TallyArray\n\tfill(cells, count: 1)\n\n\treturn cells\nend %s\n\n' "'touch'"
  printf 'function fresh() returns TallyArray\n\tvar cells = TallyArray.create(0)\n\tfill(cells, count: 2)\n\n\treturn cells\nend %s\n\n' "'fresh'"
  printf 'function ladder(seed Tally) returns Tally\n\tvar cells = TallyArray.create(0)\n\n'
  for ((i = 0; i < N; i++)); do
    if [ "$MODE" = "straight" ]; then
      printf '\tcells = touch(cells)\n'
    else
      printf '\tif seed == %d %s\n\t\tcells = touch(cells)\n\tend %s else %s\n\t\tcells = fresh()\n\tend %s\n\n' "$i" "'c$i'" "'c$i'" "'o$i'" "'o$i'"
    fi
  done
  printf '\n\treturn (cells.count() as Tally) + seed\nend %s\n\n' "'ladder'"
  printf 'function main() returns ExitCode\n\tprint("{ladder(0)}\\n")\n\treturn 0\nend %s\n' "'main'"
} > "$OUT"
