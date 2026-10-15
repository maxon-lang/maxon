#!/usr/bin/env bash
# Usage: genlongbody.sh <n> <held|registered|eager> <outfile>
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  held|registered|eager) ;;
  *) echo "genlongbody.sh: <mode> must be 'held', 'registered' or 'eager', got '$MODE'" >&2; exit 2 ;;
esac

{
  echo "// ladder: $N boxes in one long body, mode $MODE"
  echo "typealias Count = int(0 to 1000000)"
  echo "typealias Boxes = Array with Box"
  echo ""
  echo "type Box"
  echo -e "\texport var n as Count"
  echo ""
  echo -e "\tstatic function create(n Count) returns Self"
  echo -e "\t\treturn Self{n: n}"
  echo -e "\tend 'create'"
  echo "end 'Box'"
  echo ""
  echo "type Reg"
  echo -e "\texport var items as Boxes"
  echo ""
  echo -e "\tstatic function create() returns Self"
  echo -e "\t\treturn Self{items: Boxes.create()}"
  echo -e "\tend 'create'"
  echo "end 'Reg'"
  echo ""
  echo "function build(reg Reg) returns Count"
  echo -e "\treg.items.push(Box.create(0))"
  echo -e "\tvar total = 0 as Count"
  echo ""

  i=0
  while [ "$i" -lt "$N" ]; do
    echo -e "\tlet b$i = Box.create($((i % 1000)))"

    if [ "$MODE" = "registered" ]; then
      echo -e "\treg.items.push(b$i)"
    fi

    if [ "$MODE" = "eager" ]; then
      echo -e "\ttotal = (total + b$i.n) and 1023"
    fi

    i=$(( i + 1 ))
  done

  if [ "$MODE" != "eager" ]; then
    echo ""
    i=0
    while [ "$i" -lt "$N" ]; do
      echo -e "\ttotal = (total + b$i.n) and 1023"
      i=$(( i + 1 ))
    done
  fi

  echo ""
  echo -e "\treturn total"
  echo "end 'build'"
  echo ""
  echo "function main() returns ExitCode"
  echo -e "\tvar reg = Reg.create()"
  echo -e "\tlet total = build(reg)"
  echo -e "\tprint(\"{total} {reg.items.count()}\\\\n\")"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
