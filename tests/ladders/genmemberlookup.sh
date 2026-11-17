#!/usr/bin/env bash
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

if [ "$N" -lt 1 ]; then
  echo "genmemberlookup.sh: <n> must be at least 1" >&2
  exit 2
fi

case "$MODE" in
  fields)
    {
      printf 'typealias Count = int(0 to 1000)\n\ntype Big\n'
      for ((k = 0; k < N; k++)); do
        printf '\texport var f%d as Count\n' "$k"
      done
      printf '\n\tstatic function create() returns Self\n\t\treturn Self{'
      for ((k = 0; k < N; k++)); do
        if [ "$k" -gt 0 ]; then
          printf ', '
        fi
        printf 'f%d: 0' "$k"
      done
      printf '}\n\tend '\''create'\''\nend '\''Big'\''\n\n'
      for ((k = 0; k < N; k++)); do
        printf 'function r%d(b Big) returns Count\n\treturn b.f%d\nend '\''r%d'\''\n\n' "$k" "$k" "$k"
      done
      printf 'function main() returns ExitCode\n\tlet b = Big.create()\n\treturn 0 if r%d(b) == 0 else 1\nend '\''main'\''\n' "$((N - 1))"
    } > "$OUT"
    ;;
  cases)
    {
      printf 'enum Big\n'
      for ((k = 0; k < N; k++)); do
        printf '\tc%d\n' "$k"
      done
      printf 'end '\''Big'\''\n\n'
      for ((k = 0; k < N; k++)); do
        printf 'function k%d() returns Big\n\treturn Big.c%d\nend '\''k%d'\''\n\n' "$k" "$k" "$k"
      done
      printf 'function main() returns ExitCode\n\treturn 0 if k%d() == Big.c%d else 1\nend '\''main'\''\n' "$((N - 1))" "$((N - 1))"
    } > "$OUT"
    ;;
  *)
    echo "genmemberlookup.sh: unknown mode '$MODE' (fields|cases)" >&2
    exit 2
    ;;
esac
