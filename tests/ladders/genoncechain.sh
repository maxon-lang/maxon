#!/usr/bin/env bash
set -euo pipefail
N="$1"; OUT="$2"

{
  printf 'typealias Tally = int(0 to u64.max)\n\n'

  k=0
  while [ "$k" -lt "$N" ]; do
    if [ "$k" -eq $(( N - 1 )) ]; then
      printf 'function link%05d(x Tally, y Tally, depth Tally) returns Tally\n\treturn x + y + depth\nend '\''link%05d'\''\n\n' "$k" "$k"
    else
      printf 'function link%05d(x Tally, y Tally, depth Tally) returns Tally\n\tif depth == 0 '\''done'\''\n\t\treturn x\n\tend '\''done'\''\n\n\treturn link%05d(y, y: x, depth: depth - 1)\nend '\''link%05d'\''\n\n' "$k" $(( k + 1 )) "$k"
    fi
    k=$(( k + 1 ))
  done

  printf 'function main() returns ExitCode\n'
  printf '\tprint("{link00000(1, y: 2, depth: 3) + link00000(2, y: 1, depth: 5)}\\n")\n'
  printf '\treturn 0\nend '\''main'\''\n'
} > "$OUT"
