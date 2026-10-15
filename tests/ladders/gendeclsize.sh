#!/usr/bin/env bash
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

BODY=""

case "$MODE" in
  fields)
    BODY+=$'typealias Count = int(0 to 1000)\n\ntype Big\n'
    i=0
    while [ "$i" -lt "$N" ]; do
      BODY+=$'\texport var f'"$i"$' as Count\n'
      i=$(( i + 1 ))
    done
    BODY+=$'end \'Big\'\n'
    ;;
  cases)
    BODY+=$'enum Big\n'
    i=0
    while [ "$i" -lt "$N" ]; do
      BODY+=$'\tc'"$i"$'\n'
      i=$(( i + 1 ))
    done
    BODY+=$'end \'Big\'\n'
    ;;
  methods)
    BODY+=$'interface Big\n'
    i=0
    while [ "$i" -lt "$N" ]; do
      BODY+=$'\tfunction m'"$i"$'()\n'
      i=$(( i + 1 ))
    done
    BODY+=$'end \'Big\'\n'
    ;;
  payloads)
    LIST=""
    i=0
    while [ "$i" -lt "$N" ]; do
      if [ "$i" -gt 0 ]; then
        LIST+=", "
      fi
      LIST+="p$i Count"
      i=$(( i + 1 ))
    done
    BODY+=$'typealias Count = int(0 to 1000)\n\nunion Big\n\twide('"$LIST"$')\n\tnarrow\nend \'Big\'\n'
    ;;
  *)
    echo "gendeclsize.sh: unknown mode '$MODE' (fields|cases|methods|payloads)" >&2
    exit 2
    ;;
esac

BODY+=$'\nfunction main() returns ExitCode\n\treturn 0\nend \'main\'\n'
printf '%s' "$BODY" > "$OUT"
