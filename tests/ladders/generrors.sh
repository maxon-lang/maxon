#!/usr/bin/env bash
# Ladder generator for PARSE-ERROR RECOVERY — N independent errors in ONE file, each in its own region.
#
# Usage: generrors.sh <N> <shape> <outfile> [K]
#   shape: body|decl|binding|held|semantic|mixed|valid|ext|extlate|extvalid
#
# Every unit k in 0..N-1 emits a block of VALID declarations that leave parser state behind — a struct
# type, a generic instantiation (`Pair with Item<k>`), a typealiased array, a top-level `let`, and a
# function with a parameter default and a capturing closure — and then ONE error in the shape asked for:
#
#   body      a syntax error inside a function body (`let x: int = 0`), confined to that body
#   decl      a malformed top-level declaration head (`function bad<k>( returns Tally`), confined to it
#   binding   a refused top-level `let` initializer (`let Bad<k> = <n> as Digit`)
#   held      an undefined name inside a body: a held consequence (`undefinedVariable`)
#   semantic  a call with a mismatched argument, which SemanticCheck refuses and the parser never throws
#             on: the control, a file with N independent errors and none of them a parse refusal
#   mixed     body, decl, binding, held in rotation (unit k takes shape k mod 4)
#   valid     no error at all: the CONTROL that prices the same program with the error lines removed
#
# The three extension shapes instead emit ONE `extension Shape` over K conformers (default 4) that
# holds N valid methods and N broken ones:
#
#   ext       a syntax error in each broken method, which every conformer refuses
#   extlate   a read of a field only the last conformer lacks, so a later conformer refuses it
#   extvalid  no broken methods: the control for `ext` and `extlate`
#
# A recoverable refusal (body, decl, binding, ext, extlate) rewinds the parse to its region's entry
# and skips the region, so N errors should cost one parse plus each region's own work. `semantic`
# and `valid` never rewind, which is what makes them the controls: a bend in
# `body`/`decl`/`binding`/`mixed` that neither shows is the rewind, and a bend in `ext`/`extlate`
# over `extvalid` is the extension replay. `held` is a parse-time consequence recorded without a
# refusal, and reads as `semantic` does.
#
# Each shape must report exactly N diagnostics (none for `valid` or `extvalid`), one per planted
# error and no cascade; `main` calls every function whose head parsed, so a function whose body was
# abandoned is also checked for a spurious call error. Nothing in `valid` is unused, so its unit
# also compiles on its own.
#
# ⚠ Every list is built with printf in one pass: a `$(...)` per line forks a subshell per line and makes
# the GENERATOR the thing being measured.
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"; K="${4:-4}"

case "$SHAPE" in
  body|decl|binding|held|semantic|mixed|valid|ext|extlate|extvalid) ;;
  *) echo "generrors.sh: shape must be body, decl, binding, held, semantic, mixed, valid, ext, extlate or extvalid, got '$SHAPE'" >&2; exit 2 ;;
esac

if [ "$SHAPE" = "ext" ] || [ "$SHAPE" = "extlate" ] || [ "$SHAPE" = "extvalid" ]; then
  {
    echo "// ladder: $N method pair(s), $K conformer(s), shape $SHAPE"
    echo "typealias Tally = int(0 to 1000000)"
    echo ""
    printf 'interface Shape\n\tfunction side() returns Tally\nend '\''Shape'\''\n\n'
    c=0
    while [ "$c" -lt "$K" ]; do
      if [ "$SHAPE" = "extlate" ] && [ "$c" -lt $(( K - 1 )) ]; then
        printf 'type Square%d implements Shape\n\tvar edge as Tally\n\tvar spare as Tally\n\n\tstatic function create(edge Tally) returns Self\n\t\treturn Self{edge: edge, spare: edge}\n\tend '\''create'\''\n' "$c"
      else
        printf 'type Square%d implements Shape\n\tvar edge as Tally\n\n\tstatic function create(edge Tally) returns Self\n\t\treturn Self{edge: edge}\n\tend '\''create'\''\n' "$c"
      fi
      printf '\n\tfunction side() returns Tally\n\t\treturn self.edge\n\tend '\''side'\''\nend '\''Square%d'\''\n\n' "$c"
      c=$(( c + 1 ))
    done
    echo "extension Shape"
    k=0
    while [ "$k" -lt "$N" ]; do
      printf '\tfunction good%d(scale Tally = 2) returns Tally\n\t\tlet bump = function(n Tally) gives n + scale\n\t\treturn bump(self.side())\n\tend '\''good%d'\''\n\n' "$k" "$k"
      case "$SHAPE" in
        ext) printf '\tfunction bad%d() returns Tally\n\t\tlet x: int = 0\n\t\treturn self.side()\n\tend '\''bad%d'\''\n\n' "$k" "$k" ;;
        extlate) printf '\tfunction bad%d() returns Tally\n\t\treturn self.spare\n\tend '\''bad%d'\''\n\n' "$k" "$k" ;;
      esac
      k=$(( k + 1 ))
    done
    echo "end 'Shape'"
    echo ""
    echo "function main() returns ExitCode"
    echo -e "\tlet s = Square0.create(1)"
    echo -e "\tvar acc = 0 as Tally"
    k=0
    while [ "$k" -lt "$N" ]; do
      printf '\tacc = (acc + s.good%d()) and 7\n' "$k"
      if [ "$SHAPE" != "extvalid" ]; then
        printf '\tacc = (acc + s.bad%d()) and 7\n' "$k"
      fi
      k=$(( k + 1 ))
    done
    echo -e "\treturn (acc and 7) as ExitCode"
    echo "end 'main'"
  } > "$OUT"
  exit 0
fi

emit_valid() {
  local k="$1"
  printf 'type Item%d\n\tvar weight as Tally\n\n\tstatic function create(weight Tally) returns Item%d\n\t\treturn Item%d{weight: weight}\n\tend '\''create'\''\n\n\tfunction heavy(extra Tally = 1) returns Tally\n\t\treturn self.weight + extra\n\tend '\''heavy'\''\nend '\''Item%d'\''\n\n' "$k" "$k" "$k" "$k"
  printf 'typealias Item%dArray = Array with Item%d\n' "$k" "$k"
  printf 'typealias Item%dPair = Pair with Item%d\n\n' "$k" "$k"
  printf 'let Base%d = %d\n\n' "$k" $(( k % 100 ))
  printf 'function total%d(scale Tally = 2) returns Tally\n\tvar items = Item%dArray.create(0)\n\titems.push(Item%d.create(Base%d))\n\n\tlet bump = function(n Tally) gives n + scale\n\tvar sum = 0 as Tally\n\n\tfor it in items '\''eachItem'\''\n\t\tsum = bump(it.heavy())\n\tend '\''eachItem'\''\n\n\treturn sum\nend '\''total%d'\''\n\n' "$k" "$k" "$k" "$k" "$k"
  printf 'function pair%d() returns Tally\n\tlet p = Item%dPair.create(Item%d.create(1), second: Item%d.create(2))\n\treturn p.sum()\nend '\''pair%d'\''\n\n' "$k" "$k" "$k" "$k" "$k"
}

emit_error() {
  local shape="$1" k="$2"
  case "$shape" in
    body)
      printf 'function bad%d() returns Tally\n\tlet x: int = 0\n\treturn Base%d\nend '\''bad%d'\''\n\n' "$k" "$k" "$k" ;;
    decl)
      printf 'function bad%d( returns Tally\n\treturn Base%d\nend '\''bad%d'\''\n\n' "$k" "$k" "$k" ;;
    binding)
      printf 'let Bad%d = %d as Digit\n\n' "$k" $(( 10 + k )) ;;
    held)
      printf 'function bad%d() returns Tally\n\treturn missing%d\nend '\''bad%d'\''\n\n' "$k" "$k" "$k" ;;
    semantic)
      printf 'function bad%d() returns Tally\n\treturn total%d(true)\nend '\''bad%d'\''\n\n' "$k" "$k" "$k" ;;
    *)
      echo "generrors.sh: no error form for '$shape'" >&2; exit 2 ;;
  esac
}

emit_call() {
  local shape="$1" k="$2"
  case "$shape" in
    body|held|semantic) printf '\tacc = (acc + bad%d()) and 7\n' "$k" ;;
    decl|binding|valid) ;;
    *) echo "generrors.sh: no call form for '$shape'" >&2; exit 2 ;;
  esac
}

KSHAPE=""
pick_shape() {
  local k="$1"
  if [ "$SHAPE" != "mixed" ]; then
    KSHAPE="$SHAPE"
    return
  fi
  case $(( k % 4 )) in
    0) KSHAPE=body ;;
    1) KSHAPE=decl ;;
    2) KSHAPE=binding ;;
    3) KSHAPE=held ;;
  esac
}

{
  echo "// ladder: $N unit(s), shape $SHAPE"
  echo "typealias Tally = int(0 to 1000000)"
  if [ "$SHAPE" = "binding" ] || [ "$SHAPE" = "mixed" ]; then
    echo "typealias Digit = int(0 to 9)"
  fi
  echo ""
  printf 'type Pair uses T\n\tvar first as T\n\tvar second as T\n\n\tstatic function create(first T, second T) returns Self\n\t\treturn Self{first: first, second: second}\n\tend '\''create'\''\n\n\tfunction sum() returns Tally\n\t\treturn 1\n\tend '\''sum'\''\nend '\''Pair'\''\n\n'

  k=0
  while [ "$k" -lt "$N" ]; do
    emit_valid "$k"
    pick_shape "$k"
    if [ "$KSHAPE" != "valid" ]; then
      emit_error "$KSHAPE" "$k"
    fi
    k=$(( k + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar acc = 0 as Tally"
  k=0
  while [ "$k" -lt "$N" ]; do
    printf '\tacc = (acc + total%d()) and 7\n' "$k"
    printf '\tacc = (acc + pair%d()) and 7\n' "$k"
    pick_shape "$k"
    emit_call "$KSHAPE" "$k"
    k=$(( k + 1 ))
  done
  echo -e "\treturn (acc and 7) as ExitCode"
  echo "end 'main'"
} > "$OUT"
