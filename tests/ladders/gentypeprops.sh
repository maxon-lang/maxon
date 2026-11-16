#!/usr/bin/env bash
# Ladder generator for the PER-TYPE PROPERTY WALKS — "does this type hold a unique value" (and why), "is this
# type linear" and "does this type hold a Reply" (`ProgramSignatures.typeNameHoldsAUnique`,
# `walkedOwnership`, `valueIsLinear`, `aggregateIsLinear`, `typeHoldsAReply`, `aggregateReplyHolding`).
#
# Usage: gentypeprops.sh <N> <unique|linear|reply|services|control> <outdir> [<depth>]
#
# Each answer is a function of the TYPE alone, yet the parser asks it at every bind, argument, return and
# match of a value, so a program that names few types and uses them everywhere pays the walk once per USE
# unless the answer is kept per type. `ScaleCorpus` declares no `unique` type, no finalizer and no `Reply`
# at all, so every column of a default run reads those questions only on the stdlib's handful of types.
#
# The program is <N>/22 CHAINS of `<depth>` levels (default 8, 22 types), the outermost level one type and
# every other level 3 types, each type holding all 3 of the next level (a record through an `Array`, a union
# through its cases), so a walk from the outermost type reaches the whole chain; and N functions, each over
# one chain, that bind, pass, return and match a value of the chain's outermost type, so the question is
# asked per USE. The chain count is derived from N at the default depth, so a larger `<depth>` lengthens
# every chain while the chain count and the call sites stay fixed.
#   unique    the innermost level's last type holds a record that is `is unique`, so every type of the chain
#             holds a unique value, and the walk finds it only after the earlier fields' subtrees; `S<c>` is
#             a union over the chain's outermost record, matched once per function.
#   linear    the chain is unions (a linear value may not sit in a record field) and the innermost level's
#             last type has a case whose payload has a finalizer, so every union of the chain is linear;
#             each function hands its value to the chain's `eat` functions, which consume it level by level.
#   reply     the chain's records are plain, and one more chain of unions holds a `Reply`, answered by a
#             deferring service message; N functions each return the plain chain's outermost record, so
#             `checkReplyDeferrals` asks every function's result type the walk, which it does only once some
#             message defers.
#   services  the `reply` program with one deferring service per chain, each spawned and asked once, so
#             the number of services grows with N.
#   control  the `unique` program with the innermost record plain: no unique value, no finalizer, no
#             Reply, the same declarations and the same call sites. The unique modes' walks also run here
#             and answer "no" (after walking the whole chain), so what `control` prices is the walks'
#             answers, not their presence.
#
# ⭐ THE PROGRAM IS A DIRECTORY: `types.maxon` holds every type and the functions about them, and each
# group of 50 call sites is its own file `units<g>.maxon`. A front-end worker parses one file against a
# private overlay of the shared index and drops what it learned at the end of the parse, so the cost of an
# answer that is not kept in the shared index is paid once per FILE per type, and a one-file ladder would
# pay it once. Every type and function a second file names is unmarked, which every file of the directory sees.
#
# Every mode compiles and exits 0 at every N. `main` calls the groups' `drive<g>` functions, so no function
# body grows with N and `regalloc` stays out of the way. Read `--metrics` allocations of `phase:parse` and
# `phase:semanticCheck` at N = 250 / 500 / 1000 / 2000: linear is x2 per doubling. For the settle's
# chain-depth term, hold N at 250 and read `phase:signatures` at depth 16 / 32 / 64 / 128.
set -euo pipefail

if [ "$#" -ne 3 ] && [ "$#" -ne 4 ]; then
  echo "usage: gentypeprops.sh <N> <unique|linear|reply|services|control> <outdir> [<depth>]" >&2
  exit 2
fi

N="$1"; MODE="$2"; OUT="$3"
DEFAULT_DEPTH=8
DEPTH="${4:-$DEFAULT_DEPTH}"
WIDTH=3
GROUP_SIZE=50

case "$N" in
  ''|*[!0-9]*|0*) echo "gentypeprops.sh: N must be a whole number of at least 1, got '$N'" >&2; exit 2 ;;
esac

case "$DEPTH" in
  ''|*[!0-9]*|0*|1) echo "gentypeprops.sh: depth must be a whole number of at least 2, got '$DEPTH'" >&2; exit 2 ;;
esac

case "$MODE" in
  unique|linear|reply|services|control) ;;
  *) echo "gentypeprops.sh: mode must be 'unique', 'linear', 'reply', 'services' or 'control', got '$MODE'" >&2; exit 2 ;;
esac

TYPES_PER_CHAIN=$(( 1 + (DEPTH - 1) * WIDTH ))
TYPES_PER_DEFAULT_CHAIN=$(( 1 + (DEFAULT_DEPTH - 1) * WIDTH ))
CHAINS=$(( (N + TYPES_PER_DEFAULT_CHAIN - 1) / TYPES_PER_DEFAULT_CHAIN ))
LAST=$(( DEPTH - 1 ))
GROUPS_COUNT=$(( (N + GROUP_SIZE - 1) / GROUP_SIZE ))

mkdir -p "$OUT"
rm -f "$OUT/types.maxon" "$OUT/main.maxon" "$OUT"/units*.maxon

desk_count() {
  if [ "$MODE" = "services" ]; then
    echo "$CHAINS"
  else
    echo 1
  fi
}

desk_name() {
  if [ "$MODE" = "services" ]; then
    echo "Desk$1"
  else
    echo "Desk"
  fi
}

members_at() {
  if [ "$1" -eq 0 ]; then
    echo 1
  else
    echo "$WIDTH"
  fi
}

emit_leaf() {
  local unique="$1"
  printf 'type Seed%s\n\tvar n as Tally\n\n' "$unique"
  printf '\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\n\n'
  printf '\tfunction value() returns Tally\n\t\treturn self.n\n\tend '\''value'\''\n'
  printf 'end '\''Seed'\''\n\n'
}

emit_floor() {
  local vis="$1"
  printf '%stype Floor\n\tvar n as Tally\n\n' "$vis"
  printf '\t%sstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\n\n' "$vis"
  printf '\t%sfunction value() returns Tally\n\t\treturn self.n\n\tend '\''value'\''\n' "$vis"
  printf 'end '\''Floor'\''\n\n'
}

emit_record_chain() {
  local prefix="$1" c="$2"
  local k m j members me leafType inits fields vis
  k=0
  while [ "$k" -lt "$DEPTH" ]; do
    members=$(members_at "$k")
    m=0
    while [ "$m" -lt "$members" ]; do
      me="${prefix}${c}L${k}M${m}"
      fields=""
      inits="n: n"
      if [ "$k" -eq "$LAST" ]; then
        leafType="Floor"
        if [ "$m" -eq $(( WIDTH - 1 )) ] && [ "$prefix" = "U" ]; then
          leafType="Seed"
        fi
        fields="\tvar leaf as ${leafType}\n"
        inits="$inits, leaf: ${leafType}.create(n)"
      else
        j=0
        while [ "$j" -lt "$WIDTH" ]; do
          fields="${fields}\tvar a${j} as ${prefix}${c}L$(( k + 1 ))M${j}Arr\n"
          inits="$inits, a${j}: ${prefix}${c}L$(( k + 1 ))M${j}Arr.create(0)"
          j=$(( j + 1 ))
        done
      fi
      vis=""
      if [ "$k" -eq 0 ]; then
        vis=""
      fi
      printf '%stype %s\n\tvar n as Tally\n' "$vis" "$me"
      printf '%b' "$fields"
      printf '\n\t%sstatic function create(n Tally) returns Self\n\t\treturn Self{%s}\n\tend '\''create'\''\n\n' "$vis" "$inits"
      printf '\t%sfunction value() returns Tally\n\t\treturn self.n\n\tend '\''value'\''\n' "$vis"
      printf 'end '\''%s'\''\n\n' "$me"
      if [ "$k" -gt 0 ]; then
        printf 'typealias %sArr = Array with %s\n\n' "$me" "$me"
      fi
      m=$(( m + 1 ))
    done
    k=$(( k + 1 ))
  done
}

emit_types() {
  local c k m j d members me payload eaten made

  printf 'typealias Tally = int(0 to u32.max)\n\n'

  case "$MODE" in
    unique|control)
      if [ "$MODE" = "unique" ]; then
        emit_leaf " is unique"
      else
        emit_leaf ""
      fi

      emit_floor ""

      c=0
      while [ "$c" -lt "$CHAINS" ]; do
        emit_record_chain "U" "$c"
        printf 'union S%s\n\tempty\n\tfilled(item U%sL0M0)\nend '\''S%s'\''\n\n' "$c" "$c" "$c"
        printf 'function relay%s(w U%sL0M0) returns U%sL0M0\n\treturn w\nend '\''relay%s'\''\n\n' "$c" "$c" "$c" "$c"
        c=$(( c + 1 ))
      done
      ;;
    linear)
      printf 'type Resp\n\tvar n as Tally\n\n'
      printf '\tstatic function create(n Tally) returns Self\n\t\treturn Self{n: n}\n\tend '\''create'\''\n\n'
      printf '\tfunction fin(consuming self) returns Tally\n\t\tlet held = self.n\n\t\tdestruct self\n\t\treturn held\n\tend '\''fin'\''\n'
      printf 'end '\''Resp'\''\n\n'
      emit_floor ""

      c=0
      while [ "$c" -lt "$CHAINS" ]; do
        k=0
        while [ "$k" -lt "$DEPTH" ]; do
          members=$(members_at "$k")
          m=0
          while [ "$m" -lt "$members" ]; do
            me="L${c}L${k}M${m}"
            printf 'union %s\n' "$me"
            j=0
            while [ "$j" -lt "$WIDTH" ]; do
              if [ "$k" -eq "$LAST" ]; then
                if [ "$m" -eq $(( WIDTH - 1 )) ] && [ "$j" -eq $(( WIDTH - 1 )) ]; then
                  payload="Resp"
                else
                  payload="Floor"
                fi
              else
                payload="L${c}L$(( k + 1 ))M${j}"
              fi
              printf '\tcase%s(p %s)\n' "$j" "$payload"
              j=$(( j + 1 ))
            done
            printf 'end '\''%s'\''\n\n' "$me"
            m=$(( m + 1 ))
          done
          k=$(( k + 1 ))
        done

        k=$LAST
        while [ "$k" -ge 0 ]; do
          members=$(members_at "$k")
          m=0
          while [ "$m" -lt "$members" ]; do
            me="L${c}L${k}M${m}"
            if [ "$k" -eq 0 ]; then
              printf ''
            fi
            printf 'function eat%s(v %s) returns Tally\n\tmatch v '\''level'\''\n' "$me" "$me"
            j=0
            while [ "$j" -lt "$WIDTH" ]; do
              if [ "$k" -eq "$LAST" ]; then
                if [ "$m" -eq $(( WIDTH - 1 )) ] && [ "$j" -eq $(( WIDTH - 1 )) ]; then
                  eaten="p.fin()"
                else
                  eaten="p.value()"
                fi
              else
                eaten="eatL${c}L$(( k + 1 ))M${j}(p)"
              fi
              printf '\t\tcase%s(p) then return %s\n' "$j" "$eaten"
              j=$(( j + 1 ))
            done
            printf '\tend '\''level'\''\nend '\''eat%s'\''\n\n' "$me"
            m=$(( m + 1 ))
          done
          k=$(( k - 1 ))
        done

        made="Resp.create(n)"
        k=$LAST
        while [ "$k" -ge 0 ]; do
          made="L${c}L${k}M$(( k == 0 ? 0 : WIDTH - 1 )).case$(( WIDTH - 1 ))($made)"
          k=$(( k - 1 ))
        done
        printf 'function make%s(n Tally) returns L%sL0M0\n\treturn %s\nend '\''make%s'\''\n\n' "$c" "$c" "$made" "$c"
        printf 'function relay%s(w L%sL0M0) returns L%sL0M0\n\treturn w\nend '\''relay%s'\''\n\n' "$c" "$c" "$c" "$c"
        c=$(( c + 1 ))
      done
      ;;
    reply|services)
      printf 'typealias TallyReply = Reply with Tally\n\n'
      emit_floor ""

      c=0
      while [ "$c" -lt "$CHAINS" ]; do
        emit_record_chain "P" "$c"
        printf 'function relay%s(w P%sL0M0) returns P%sL0M0\n\treturn w\nend '\''relay%s'\''\n\n' "$c" "$c" "$c" "$c"
        c=$(( c + 1 ))
      done

      k=0
      while [ "$k" -lt "$DEPTH" ]; do
        if [ "$k" -eq "$LAST" ]; then
          payload="TallyReply"
        else
          payload="HeldL$(( k + 1 ))"
        fi
        printf 'union HeldL%s\n\tempty\n\tholds(inner %s)\nend '\''HeldL%s'\''\n\n' "$k" "$payload" "$k"
        k=$(( k + 1 ))
      done

      k=$LAST
      while [ "$k" -ge 0 ]; do
        if [ "$k" -eq "$LAST" ]; then
          eaten="inner.answer(n)"
        else
          eaten="settleL$(( k + 1 ))(inner, n: n)"
        fi
        printf 'function settleL%s(v HeldL%s, n Tally)\n' "$k" "$k"
        printf '\tmatch v '\''level'\''\n\t\tempty then return\n\t\tholds(inner) then %s\n\tend '\''level'\''\n' "$eaten"
        printf 'end '\''settleL%s'\''\n\n' "$k"
        k=$(( k - 1 ))
      done

      made="pending"
      k=$LAST
      while [ "$k" -ge 0 ]; do
        made="HeldL${k}.holds($made)"
        k=$(( k - 1 ))
      done

      d=0
      while [ "$d" -lt "$(desk_count)" ]; do
        printf 'type %s\n\tstatic function create() returns Self\n\t\treturn Self{}\n\tend '\''create'\''\n\n' "$(desk_name "$d")"
        printf '\texport function ask(n Tally) returns Tally throws ReplyStopped\n'
        printf '\t\tlet pending = reply.later()\n\t\tsettleL0(%s, n: n)\n\t\treturn\n\tend '\''ask'\''\n' "$made"
        printf 'end '\''%s'\''\n\n' "$(desk_name "$d")"
        d=$(( d + 1 ))
      done
      ;;
    *) echo "gentypeprops.sh: mode '$MODE' is not handled" >&2; exit 2 ;;
  esac
}

emit_unit() {
  local g="$1" i c call first last
  first=$(( g * GROUP_SIZE ))
  last=$(( first + GROUP_SIZE ))
  if [ "$last" -gt "$N" ]; then
    last="$N"
  fi

  i=$first
  while [ "$i" -lt "$last" ]; do
    c=$(( i % CHAINS ))
    case "$MODE" in
      unique|control)
        printf 'function f%s(w U%sL0M0) returns Tally\n' "$i" "$c"
        printf '\tlet a = relay%s(w)\n\tlet b = relay%s(a)\n\tlet s = S%s.filled(b)\n\n' "$c" "$c" "$c"
        printf '\tmatch s '\''which'\''\n\t\tempty then return 0\n\t\tfilled(item) then return item.value()\n\tend '\''which'\''\n'
        printf 'end '\''f%s'\''\n\n' "$i"
        ;;
      linear)
        printf 'function f%s(w L%sL0M0) returns Tally\n' "$i" "$c"
        printf '\tlet a = relay%s(w)\n\treturn eatL%sL0M0(a)\n' "$c" "$c"
        printf 'end '\''f%s'\''\n\n' "$i"
        ;;
      reply|services)
        printf 'function f%s(n Tally) returns P%sL0M0\n' "$i" "$c"
        printf '\tlet a = P%sL0M0.create(n)\n\tlet b = relay%s(a)\n\treturn b\n' "$c" "$c"
        printf 'end '\''f%s'\''\n\n' "$i"
        ;;
      *) echo "gentypeprops.sh: mode '$MODE' is not handled" >&2; exit 2 ;;
    esac
    i=$(( i + 1 ))
  done

  if [ "$MODE" = "linear" ] && [ "$g" -eq 0 ]; then
    printf 'function touchResp(n Tally) returns Tally\n\tlet r = Resp.create(n)\n\treturn r.fin()\nend '\''touchResp'\''\n\n'
    printf 'function touchFloor(n Tally) returns Tally\n\tlet f = Floor.create(n)\n\treturn f.value()\nend '\''touchFloor'\''\n\n'
  fi

  printf 'function drive%s() returns Tally\n\tvar total = 0 as Tally\n' "$g"

  if [ "$MODE" = "linear" ] && [ "$g" -eq 0 ]; then
    printf '\ttotal = total + touchResp(1) + touchFloor(1)\n'
  fi

  i=$first
  while [ "$i" -lt "$last" ]; do
    c=$(( i % CHAINS ))
    case "$MODE" in
      unique|control) call="f${i}(U${c}L0M0.create(1))" ;;
      linear)         call="f${i}(make${c}(1))" ;;
      reply|services) call="f${i}(1).value()" ;;
      *) echo "gentypeprops.sh: mode '$MODE' is not handled" >&2; exit 2 ;;
    esac
    printf '\ttotal = total + %s\n' "$call"

    if [ "$MODE" = "services" ] && [ "$i" -lt "$CHAINS" ]; then
      printf '\ttotal = total + consult%s()\n' "$i"
    fi

    i=$(( i + 1 ))
  done
  printf '\treturn total\nend '\''drive%s'\''\n' "$g"
}

emit_main() {
  local g=0

  printf 'function main() returns ExitCode\n\tvar total = 0 as Tally\n'

  while [ "$g" -lt "$GROUPS_COUNT" ]; do
    printf '\ttotal = total + drive%s()\n' "$g"
    g=$(( g + 1 ))
  done

  if [ "$MODE" = "reply" ]; then
    printf '\tlet desk = spawn Desk.create()\n'
    printf '\tlet answer = try await desk.ask(7) otherwise panic("the message answers through its Reply")\n'
    printf '\ttotal = total + answer\n'
  fi

  printf '\tprint("{total}\\n")\n\treturn 0\nend '\''main'\''\n'

  if [ "$MODE" = "services" ]; then
    local d=0
    while [ "$d" -lt "$CHAINS" ]; do
      printf '\nfunction consult%s() returns Tally\n' "$d"
      printf '\tlet desk = spawn %s.create()\n' "$(desk_name "$d")"
      printf '\treturn try await desk.ask(7) otherwise panic("the message answers through its Reply")\n'
      printf 'end '\''consult%s'\''\n' "$d"
      d=$(( d + 1 ))
    done
  fi
}

printf '// ladder: %s functions over %s chain(s) of %s types, mode %s\n' "$N" "$CHAINS" "$TYPES_PER_CHAIN" "$MODE" > "$OUT/types.maxon"
emit_types >> "$OUT/types.maxon"

g=0
while [ "$g" -lt "$GROUPS_COUNT" ]; do
  emit_unit "$g" > "$OUT/units${g}.maxon"
  g=$(( g + 1 ))
done

emit_main > "$OUT/main.maxon"
