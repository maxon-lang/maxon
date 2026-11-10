#!/usr/bin/env bash
# Ladder generator for ONE FUNCTION HOLDING N UNDECIDED CALL RESULTS, each captured by its own closure that
# calls it. `pick(holder)` over the function's unmarked parameter returns a `mutates captures` function value
# whose capability is decided only once the whole program is known, so every such result is filed as an
# undecided result of `run`; each closure that calls its result writes that capture, and the capability
# check asks, per capture, which op defines the captured value. The cost of that question must not grow
# with the number of undecided results the function holds.
#
# Usage: genundecided.sh <N> <out>
#
# Every size returns 0, so a rung that miscompiles shows up as a wrong exit code.
#
# ⚠ Names are written without padding because the counts are compared across a doubling ladder, where a
# digit the name gains moves the byte columns by the same amount at every rung.
set -euo pipefail
N="$1"; OUT="$2"

if [ "$N" -lt 1 ]; then
  echo "genundecided.sh: <N> must be at least 1" >&2
  exit 2
fi

{
  printf 'typealias Tally = int(0 to 4000000000)\n'
  printf 'typealias Bump = function() returns Tally mutates captures\n\n'
  printf 'type Total\n\tvar n as Tally\n\n\tstatic function create() returns Self\n\t\treturn Self{n: 0}\n\tend '\''create'\''\n\n\tfunction bump() returns Tally\n\t\tself.n = 1\n\t\treturn self.n\n\tend '\''bump'\''\nend '\''Total'\''\n\n'
  printf 'type Holder\n\texport let callback as Bump\n\n\tstatic function create() returns Self\n\t\tvar total = Total.create()\n\t\treturn Self{callback: function() gives total.bump()}\n\tend '\''create'\''\nend '\''Holder'\''\n\n'
  printf 'function pick(holder Holder) returns Bump\n\treturn holder.callback\nend '\''pick'\''\n\n'
  printf 'function run(holder Holder) returns Tally\n'
  for ((k = 0; k < N; k++)); do
    printf '\tlet g%d = pick(holder)\n' "$k"
    printf '\tlet f%d = function() gives g%d()\n' "$k" "$k"
  done
  printf '\tvar total = 0 as Tally\n'
  for ((k = 0; k < N; k++)); do
    printf '\ttotal = f%d()\n' "$k"
  done
  printf '\treturn total\nend '\''run'\''\n\n'
  printf 'function main() returns ExitCode\n\tvar holder = Holder.create()\n\tlet total = run(holder)\n\n\tif total != 1 '\''wrong'\''\n\t\treturn 1\n\tend '\''wrong'\''\n\n\treturn 0\nend '\''main'\''\n'
} > "$OUT"
