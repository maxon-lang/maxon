#!/usr/bin/env bash
# THE TRY-EACH LADDER — one function holding N owned values live across N throwing calls.
#
#     `each`   N `let sI = try make(seed + I) otherwise return 0`, then N uses: every value stays live to
#              the end, across every later call and its error edge. `inlineLeaves`' register-budget checks
#              ask the live-in peak of exactly this shape, so it is the ladder behind its scaling row in
#              docs/optimization-log.md.
#     `block`  the same N lets inside ONE `try 'work'` block: every throwing call drops each owned value
#              already bound, so the emitted code itself holds N(N+1)/2 drops — a shape whose IR, and every
#              pass over it, is quadratic by construction.
#
# Usage: gentryeach.sh <n> <each|block> <outfile>
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"

case "$SHAPE" in
  each|block) ;;
  *)
    echo "gentryeach.sh: shape must be 'each' or 'block', got '$SHAPE'" >&2
    exit 2
    ;;
esac

{
  printf 'typealias Tally = int(0 to u64.max)\n\n'
  printf 'enum Oops implements Error\n\tbad\nend %s\n\n' "'Oops'"
  printf 'function make(n Tally) returns String throws Oops\n'
  printf '\tif n == 1000000 %s\n\t\tthrow Oops.bad\n\tend %s\n\n' "'big'" "'big'"
  printf '\treturn "v{n}"\nend %s\n\n' "'make'"
  printf 'function ladder(seed Tally) returns Tally\n'
  printf '\tvar total = 0 as Tally\n\n'

  if [ "$SHAPE" = each ]; then
    for ((i = 0; i < N; i++)); do
      printf '\tlet s%d = try make(seed + %d) otherwise return 0\n' "$i" "$i"
    done
    printf '\n'
    for ((i = 0; i < N; i++)); do
      printf '\ttotal = total + s%d.byteLength() as Tally\n' "$i"
    done
  else
    printf '\ttry %s\n' "'work'"
    for ((i = 0; i < N; i++)); do
      printf '\t\tlet s%d = make(seed + %d)\n' "$i" "$i"
    done
    for ((i = 0; i < N; i++)); do
      printf '\t\ttotal = total + s%d.byteLength() as Tally\n' "$i"
    done
    printf '\tend %s\n' "'work'"
    printf '\totherwise (e) %s\n' "'handler'"
    printf '\t\tmatch e %s\n\t\t\tbad then total = total + 1\n\t\tend %s\n' "'which'" "'which'"
    printf '\tend %s\n' "'handler'"
  fi

  printf '\n\treturn total\nend %s\n\n' "'ladder'"
  printf 'function main() returns ExitCode\n\tlet r = ladder(5)\n\n'
  printf '\tif r == 0 %s\n\t\treturn 1\n\tend %s\n\n' "'zero'" "'zero'"
  printf '\treturn 0\nend %s\n' "'main'"
} > "$OUT"
