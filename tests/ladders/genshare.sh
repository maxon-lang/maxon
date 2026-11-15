#!/usr/bin/env bash
set -euo pipefail
N="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  bases|owners|returns|returnsfree|doors|closure|dedupe|cycle|nested|phantom) ;;
  *) echo "genshare.sh: mode must be bases, owners, returns, returnsfree, doors, closure, dedupe, cycle, nested or phantom, got '$MODE'" >&2; exit 2 ;;
esac

emit_prelude() {
  printf 'typealias Tally = int(0 to u64.max)\n\ninterface Sink\n\tfunction total() returns Tally\nend '\''Sink'\''\n\n'
  printf 'type Door\n\tvar seen as Tally\n\n\tstatic function create() returns Self\n\t\treturn Self{seen: 0}\n\tend '\''create'\''\n\n\texport function look(n Tally)\n\t\tself.seen = self.seen + n\n\tend '\''look'\''\nend '\''Door'\''\n\n'
  printf 'type Plain implements Sink\n\tvar n as Tally\n\n\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\n\n\tfunction total() returns Tally\n\t\treturn self.n\n\tend '\''total'\''\nend '\''Plain'\''\n\n'
  printf 'type Holder\n\texport let sink as Sink\n\n\tstatic function create(sink Sink) returns Self\n\t\treturn Self{sink: sink}\n\tend '\''create'\''\nend '\''Holder'\''\n\n'
}

emit_gen() {
  local name="$1"
  printf 'type %s uses T implements Sink\n\tvar held as T\n\tvar seen as Tally\n\n\tstatic function create(held T, seen Tally) returns Self\n\t\treturn Self{held: held, seen: seen}\n\tend '\''create'\''\n\n\tfunction total() returns Tally\n\t\treturn self.seen\n\tend '\''total'\''\nend '\''%s'\''\n\n' "$name" "$name"
}

emit_wrap() {
  local name="$1" gen="$2"
  printf 'type %s uses T\n\tstatic function sink(seed T, seen Tally) returns Sink\n\t\treturn %s.create(seed, seen: seen)\n\tend '\''sink'\''\nend '\''%s'\''\n\n' "$name" "$gen" "$name"
}

emit_chain_link() {
  local k="$1" expr="$2"
  local prev
  if [ "$k" -eq 0 ]; then
    prev="0"
  else
    prev="$(printf 'use%05d()' $(( k - 1 )))"
  fi
  printf 'function use%05d() returns Tally\n\treturn %s + %s\nend '\''use%05d'\''\n\n' "$k" "$expr" "$prev" "$k"
}

emit_main() {
  local door="$1" last="$2"
  printf 'function main() returns ExitCode\n'
  printf '\tlet door = spawn Door.create()\n\tdoor.shutdown()\n'
  printf '\tlet held = %sHolder.create(Plain.create(1))\n' "$door"
  printf '\tprint("{held.sink.total() + %s}\\n")\n' "$last"
  printf '\treturn 0\nend '\''main'\''\n'
}

last_use="$(printf 'use%05d()' $(( N - 1 )))"

{
  emit_prelude

  case "$MODE" in
    bases)
      k=0
      while [ "$k" -lt "$N" ]; do
        emit_gen "$(printf 'Gen%05d' "$k")"
        emit_wrap "$(printf 'Wrap%05d' "$k")" "$(printf 'Gen%05d' "$k")"
        printf 'typealias TallyWrap%05d = Wrap%05d with Tally\n\n' "$k" "$k"
        emit_chain_link "$k" "$(printf 'TallyWrap%05d.sink(1, seen: 1).total()' "$k")"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
    owners)
      emit_gen "Gen"
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'type Arg%05d\n\tvar n as Tally\n\n\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\nend '\''Arg%05d'\''\n\n' "$k" "$k"
        emit_wrap "$(printf 'Wrap%05d' "$k")" "Gen"
        printf 'typealias ArgWrap%05d = Wrap%05d with Arg%05d\n\n' "$k" "$k" "$k"
        emit_chain_link "$k" "$(printf 'ArgWrap%05d.sink(Arg%05d.create(1), seen: 1).total()' "$k" "$k")"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
    closure)
      emit_gen "Gen"
      printf 'type Froze uses T\n\ttypealias TGen = Gen with T\n\n\tstatic function run(x T, seen Tally) returns Sink\n\t\treturn TGen.create(x, seen: seen)\n\tend '\''run'\''\nend '\''Froze'\''\n\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'type Arg%05d\n\tvar n as Tally\n\n\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\nend '\''Arg%05d'\''\n\n' "$k" "$k"
        printf 'type Outer%05d uses T\n\ttypealias TFroze = Froze with T\n\n\tstatic function go(x T) returns Sink\n\t\treturn TFroze.run(x, seen: 1)\n\tend '\''go'\''\nend '\''Outer%05d'\''\n\n' "$k" "$k"
        printf 'typealias ArgOuter%05d = Outer%05d with Arg%05d\n\n' "$k" "$k" "$k"
        emit_chain_link "$k" "$(printf 'ArgOuter%05d.go(Arg%05d.create(1)).total()' "$k" "$k")"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
    dedupe)
      printf 'type Cell uses T\n\texport let item as T\n\n\tstatic function create(item T) returns Self\n\t\treturn Self{item: item}\n\tend '\''create'\''\nend '\''Cell'\''\n\n'
      printf 'type Box uses T\n\ttypealias Deeper = Box with (Cell with T)\n\texport let seen as Tally\n\texport var name as String\n\n\tstatic function create(seen Tally) returns Self\n\t\treturn Self{seen: seen, name: "box"}\n\tend '\''create'\''\n\n\tfunction deeper() returns immutable Deeper\n\t\treturn freeze Deeper.create(self.seen + 1)\n\tend '\''deeper'\''\nend '\''Box'\''\n\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'type Arg%05d\n\tvar n as Tally\n\n\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\nend '\''Arg%05d'\''\n\n' "$k" "$k"
        printf 'typealias ArgBox%05d = Box with Arg%05d\n\n' "$k" "$k"
        emit_chain_link "$k" "$(printf 'ArgBox%05d.create(1).deeper().seen' "$k")"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
    cycle)
      emit_gen "Gen"
      k=0
      while [ "$k" -lt "$N" ]; do
        next=$(( k + 1 - 2 * (k % 2) ))
        printf 'type Ring%05d uses A, B\n\ttypealias Flip = Ring%05d with (B, A)\n\ttypealias AGen = Gen with A\n\n\tstatic function go(x A, y B, depth Tally) returns Sink\n\t\tif depth == 0 '\''done'\''\n\t\t\treturn AGen.create(x, seen: 1)\n\t\tend '\''done'\''\n\n\t\treturn Flip.go(y, y: x, depth: depth - 1)\n\tend '\''go'\''\nend '\''Ring%05d'\''\n\n' "$k" "$next" "$k"
        if [ $(( k % 2 )) -eq 0 ]; then
          printf 'typealias Start%05d = Ring%05d with (Tally, Tally)\n\n' "$k" "$k"
        fi
        emit_chain_link "$k" "$(printf 'Start%05d.go(1, y: 2, depth: 2).total()' $(( k - k % 2 )))"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
    nested)
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'type Grid%05d uses T\n\ttypealias Row = Array with T\n\ttypealias Rows = Array with Row\n\tvar rows as Rows\n\n\tstatic function create() returns Self\n\t\treturn Self{rows: Rows.create(0)}\n\tend '\''create'\''\n\n\tfunction count() returns Tally\n\t\treturn self.rows.count() as Tally\n\tend '\''count'\''\nend '\''Grid%05d'\''\n\n' "$k" "$k"
        printf 'typealias TallyGrid%05d = Grid%05d with Tally\n\n' "$k" "$k"
        emit_chain_link "$k" "$(printf 'TallyGrid%05d.create().count()' "$k")"
        k=$(( k + 1 ))
      done
      emit_main "" "$last_use"
      ;;
    phantom)
      printf 'type Cell uses T\n\texport let item as T\n\n\tstatic function create(item T) returns Self\n\t\treturn Self{item: item}\n\tend '\''create'\''\nend '\''Cell'\''\n\n'
      printf 'type Phantom uses U\n\texport let n as Tally\n\n\tstatic function create() returns Self\n\t\treturn Self{n: 0}\n\tend '\''create'\''\nend '\''Phantom'\''\n\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'type Arg%05d\n\tvar n as Tally\n\n\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\nend '\''Arg%05d'\''\n\n' "$k" "$k"
        printf 'type Froze%05d uses T\n\ttypealias TCell = Cell with T\n\ttypealias Wider = Froze%05d with (Phantom with T)\n\n\tstatic function run(x T) returns Tally\n\t\tlet f = freeze TCell.create(x).clone()\n\t\t_ = f\n\t\treturn 1 + Wider.touch()\n\tend '\''run'\''\n\n\tstatic function touch() returns Tally\n\t\treturn 5\n\tend '\''touch'\''\nend '\''Froze%05d'\''\n\n' "$k" "$k" "$k"
        printf 'typealias ArgFroze%05d = Froze%05d with Arg%05d\n\n' "$k" "$k" "$k"
        emit_chain_link "$k" "$(printf 'ArgFroze%05d.run(Arg%05d.create(1))' "$k" "$k")"
        k=$(( k + 1 ))
      done
      emit_main "" "$last_use"
      ;;
    returns|returnsfree)
      printf 'function pick(k Tally) returns Sink\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf '\tif k == %d '\''r%05d'\''\n\t\treturn Plain.create(%d)\n\tend '\''r%05d'\''\n\n' "$k" "$k" "$k" "$k"
        k=$(( k + 1 ))
      done
      printf '\treturn Plain.create(0)\nend '\''pick'\''\n\n'
      if [ "$MODE" = returns ]; then
        emit_main "freeze " "pick(3).total()"
      else
        emit_main "" "pick(3).total()"
      fi
      ;;
    doors)
      printf 'type Big\n\tvar n as Tally\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf '\tvar f%05d as String\n' "$k"
        k=$(( k + 1 ))
      done
      printf '\n\tstatic function make() returns Self\n\t\treturn Self{n: 1'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf ', f%05d: "x"' "$k"
        k=$(( k + 1 ))
      done
      printf '}\n\tend '\''make'\''\n\n\tfunction count() returns Tally\n\t\treturn self.n\n\tend '\''count'\''\nend '\''Big'\''\n\n'
      k=0
      while [ "$k" -lt "$N" ]; do
        printf 'function door%05d() returns Tally\n\tlet d = freeze Big.make()\n\treturn d.count()\nend '\''door%05d'\''\n\n' "$k" "$k"
        emit_chain_link "$k" "$(printf 'door%05d()' "$k")"
        k=$(( k + 1 ))
      done
      emit_main "freeze " "$last_use"
      ;;
  esac
} > "$OUT"
