#!/usr/bin/env bash
# THE HANDED-OFF-RETURNS LADDER — N owned values, then N early returns each handing off ONE of them.
#
#     Every `return sI` owes a drop of every other owner but the one it returns. The exits share one
#     release chain, so the emitted code, `semanticCheck` and the exe grow linearly; it is the ladder
#     behind the 2026-10-14 shared-release-chain row in docs/optimization-log.md.
#
# Usage: genreturns.sh <n> <outfile>
set -euo pipefail
N="$1"; OUT="$2"

{
  printf 'typealias Tally = int(0 to u64.max)\n\n'
  printf 'function ladder(seed Tally) returns String\n'
  for ((i = 0; i < N; i++)); do
    printf '\tlet s%d = "v{seed}x%d"\n' "$i" "$i"
  done
  printf '\n'
  for ((i = 0; i < N; i++)); do
    printf '\tif seed == %d %s\n\t\treturn s%d\n\tend %s\n\n' "$i" "'r$i'" "$i" "'r$i'"
  done
  printf '\treturn "end"\nend %s\n\n' "'ladder'"
  printf 'function main() returns ExitCode\n\tlet r = ladder(5)\n\n'
  printf '\tif r.byteLength() == 0 %s\n\t\treturn 1\n\tend %s\n\n' "'zero'" "'zero'"
  printf '\treturn 0\nend %s\n' "'main'"
} > "$OUT"
