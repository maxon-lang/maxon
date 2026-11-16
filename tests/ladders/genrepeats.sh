#!/usr/bin/env bash
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"

case "$SHAPE" in
  one|bare|funcs) ;;
  *)
    echo "genrepeats.sh: shape must be 'one', 'bare' or 'funcs', got '$SHAPE'" >&2
    exit 2
    ;;
esac

ArmsPerFunction=8

emit_arms() {
  local count="$1" with_work="$2"
  local i=0
  while [ "$i" -lt "$count" ]; do
    echo -e "\tif c > ${i} 'r${i}'"
    echo -e "\t\tk = p"
    if [ "$with_work" = yes ]; then
      echo -e "\t\tw = w + ${i}"
    fi
    echo -e "\tend 'r${i}'"
    i=$(( i + 1 ))
  done
}

emit_rebind() {
  local name="$1" count="$2" with_work="$3"
  echo "function ${name}(p LadderInt, c LadderInt) returns LadderInt"
  echo -e "\tvar k = p"
  if [ "$with_work" = yes ]; then
    echo -e "\tvar w = c"
  fi
  emit_arms "$count" "$with_work"
  if [ "$with_work" = yes ]; then
    echo -e "\treturn k + w"
  else
    echo -e "\treturn k + c"
  fi
  echo "end '${name}'"
}

{
  echo "// ladder: $SHAPE, N=$N"
  echo "typealias LadderInt = int(i64.min to i64.max)"
  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"

  case "$SHAPE" in
    one|bare)
      emit_rebind rebind "$N" "$([ "$SHAPE" = one ] && echo yes || echo no)"
      echo "function main() returns ExitCode"
      echo -e "\tlet a = scaleOpaque(1)"
      echo -e "\tlet b = scaleOpaque(2)"
      echo -e "\treturn ((rebind(a, c: b) + rebind(b, c: a)) and 7) as ExitCode"
      echo "end 'main'"
      ;;
    funcs)
      f=0
      while [ "$f" -lt "$N" ]; do
        emit_rebind "$(printf 'rebind%05d' "$f")" "$ArmsPerFunction" yes
        f=$(( f + 1 ))
      done
      echo "function main() returns ExitCode"
      echo -e "\tlet a = scaleOpaque(1)"
      echo -e "\tlet b = scaleOpaque(2)"
      echo -e "\tvar sum = a"
      f=0
      while [ "$f" -lt "$N" ]; do
        printf '\tsum = sum + rebind%05d(a, c: b) + rebind%05d(b, c: a)\n' "$f" "$f"
        f=$(( f + 1 ))
      done
      echo -e "\treturn (sum and 7) as ExitCode"
      echo "end 'main'"
      ;;
  esac
} > "$OUT"
