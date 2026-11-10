#!/usr/bin/env bash
# Ladder generator for CLOSURES THAT PASS ONE OF THEIR PARAMETERS ON. Each of N functions hands
# `run` a closure whose body passes its own parameter `items` to `put`, and the parser records a
# use-site phrase for that use (`noteUseSite`), spelled from the statement the use sits in. The cost
# of one phrase must not grow with the size of the file before it.
#
# Usage: genclosurepass.sh <N> <out>
#
# Every size returns 0, so a rung that miscompiles shows up as a wrong exit code.
set -euo pipefail
N="$1"; OUT="$2"

{
  printf 'typealias Tally = int(0 to 1000000)\n\n'
  printf 'type Box\n\tvar weight as Tally\n\n\tstatic function create(weight Tally) returns Box\n\t\treturn Box{weight: weight}\n\tend '\''create'\''\nend '\''Box'\''\n\n'
  printf 'typealias BoxArray = Array with Box\n'
  printf 'typealias Step = function(mutable BoxArray) returns Tally\n\n'
  printf 'function put(shelf BoxArray, items mutable BoxArray) returns Tally\n\titems.push(Box.create(shelf.count() as Tally))\n\treturn items.count() as Tally\nend '\''put'\''\n\n'
  printf 'function run(step Step, items mutable BoxArray) returns Tally\n\treturn step(items)\nend '\''run'\''\n\n'

  k=0
  while [ "$k" -lt "$N" ]; do
    printf 'function unit%d(shelf BoxArray) returns Tally\n\tvar items = BoxArray.create(0)\n\treturn run(function(items mutable BoxArray) gives put(shelf, items: items), items: items)\nend '\''unit%d'\''\n\n' "$k" "$k"
    k=$(( k + 1 ))
  done

  printf 'function main() returns ExitCode\n\tlet shelf = BoxArray.create(0)\n\tvar acc = 0 as Tally\n'
  k=0
  while [ "$k" -lt "$N" ]; do
    printf '\tacc = (acc + unit%d(shelf)) and 7\n' "$k"
    k=$(( k + 1 ))
  done
  printf '\treturn (acc and 7) as ExitCode\nend '\''main'\''\n'
} > "$OUT"
