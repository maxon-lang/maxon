#!/usr/bin/env bash
# Ladder generator for `Map`/`Set` keyed by an `int` whose entropy sits above bit 31.
# Usage: genintkeys.sh <n> <high|low> <outfile>
#
# Emits a program that upserts `n` keys into a `Map with (Key, Count)` and then asks `contains` of each one,
# timing the two loops with `Clock.nowNanos` and printing the elapsed milliseconds. The RUN is the
# measurement, not the compile: read the printed number, and double `n`.
#
# `high`: key `i` is `(i shl 32) or 2`, the shape of a packed `line shl 32 or column` position and of
# `HopClasses`' `(kind shl 32) or slot`. `int.hash` must carry the high word into the 32-bit hash or every key
# lands on one probe chain and the fill is quadratic.
#
# `low`: key `i` is `(i shl 3) or 2`, the same number of keys spread over the LOW word. It is the control: it
# pays everything a `Map` pays except the collision chain, so a `high` time that departs from it names the hash.
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  high) SHIFT=32 ;;
  low) SHIFT=3 ;;
  *)
    echo "genintkeys.sh: unknown mode '$MODE' (high|low)" >&2
    exit 2
    ;;
esac

PROGRAM=$'typealias Key = int(0 to u64.max)\ntypealias Count = int(0 to 4294967295)\ntypealias Table = Map with (Key, Count)\n\nfunction fill(n Count, shift Key) returns Count\n\tvar table = Table.create()\n\n\tfor i in 0 upto n \'eachKey\'\n\t\ttable.upsert(((i as Key) shl shift) or 2, value: i)\n\tend \'eachKey\'\n\n\tvar found = 0 as Count\n\n\tfor i in 0 upto n \'eachRead\'\n\t\tif table.contains(((i as Key) shl shift) or 2) \'present\'\n\t\t\tfound = found + 1\n\t\tend \'present\'\n\tend \'eachRead\'\n\n\treturn found\nend \'fill\'\n\nfunction main() returns ExitCode\n\tlet start = Clock.nowNanos()\n\tlet found = fill('"$N"$', shift: '"$SHIFT"$')\n\tlet stop = Clock.nowNanos()\n\tprint("{(stop - start) / 1000000} ms for '"$N"$' keys, {found} found\\n")\n\treturn 0\nend \'main\'\n'

printf '%s' "$PROGRAM" > "$OUT"
