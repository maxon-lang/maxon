#!/usr/bin/env bash
# Ladder generator for FUNCTION VALUES WHEN THE PROGRAM TAKES A PARAMETER BY REFERENCE — the axis
# `genfnval.sh` cannot reach, because no function it generates assigns a parameter and the whole
# by-reference convention (the per-arity mask table, the indirect call places, the cell-local prescan
# that tests every bare argument of every call that may be indirect) is gated off for such a program.
#
# Usage: genfnvalbyref.sh <aliases> <sites> <arity> <byref|plain|generic> <outfile>
#
# `byref` and `plain`: every unit is a function TYPEALIAS, a matching reading function, and a wrapper that
# takes the function as an alias-typed parameter and calls it `<sites>` times through `<arity>` bare local
# bindings. In `byref` mode ONE extra function of the same arity assigns its first parameter, so the program
# takes a parameter by reference and every one of those indirect calls is predicted, bound and lowered with
# cells. In `plain` mode that function is absent and the locals are `let`, so the same calls take their
# arguments by value and none of the by-reference machinery runs: the subtraction is that machinery.
#
# `generic`: every unit is a generic type whose callback is typed over its type parameter, with `<arity>`
# parameters. `run` calls it `<sites>` times with `let` arguments and is handed a reading function; `runVar`
# calls it `<sites>` times with `var` arguments and is handed a writing function of the same arity, so the
# flow sets, the writer sites and the E3159 checks all run at scale and none of them is an error.
#
# ⭐ THE KNOBS ARE SEPARABLE for `genfnval.sh`'s reason. `<aliases>` is the program-size knob, `<sites>`
# moves the indirect call sites at a fixed program size, and `<arity>` moves the width of every mask read
# and of every call's argument list. Doubling `<sites>` alone is the direct test of "does an indirect call
# site or an argument name cost a pass over the recorded functions?" — the question the per-arity table
# exists to answer.
#
# Names are FIXED-WIDTH, so a rung differs in count and never in digits. Returns 0 at every size in every
# mode.
set -euo pipefail
ALIASES="$1"; SITES="$2"; ARITY="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  byref|plain|generic) ;;
  *) echo "genfnvalbyref.sh: mode must be 'byref', 'plain' or 'generic', got '$MODE'" >&2; exit 2 ;;
esac

if [ "$ALIASES" -lt 1 ] || [ "$SITES" -lt 1 ] || [ "$ARITY" -lt 1 ]; then
  echo "genfnvalbyref.sh: <aliases>, <sites> and <arity> must each be at least 1" >&2
  exit 2
fi

if [ "$MODE" = "generic" ]; then
  TYPES=""
  PARAMS=""
  LET_ARGS=""
  VAR_ARGS=""
  VAR_DECLS=""
  BODY_SUM=""
  i=0
  while [ "$i" -lt "$ARITY" ]; do
    NAME=$(printf 'a%04d' "$i")
    LOCAL=$(printf 'w%04d' "$i")
    if [ "$i" -eq 0 ]; then
      TYPES="T"
      PARAMS="$NAME Integer"
      LET_ARGS="v"
      VAR_ARGS="$LOCAL"
      BODY_SUM="$NAME"
    else
      TYPES="$TYPES, T"
      PARAMS="$PARAMS, $NAME Integer"
      LET_ARGS="$LET_ARGS, v"
      VAR_ARGS="$VAR_ARGS, $LOCAL"
      BODY_SUM="$BODY_SUM + $NAME"
    fi
    if [ "$i" -eq 0 ]; then
      VAR_DECLS="$VAR_DECLS\t\tvar $LOCAL = seed\n"
    else
      VAR_DECLS="$VAR_DECLS\t\tlet $LOCAL = seed\n"
    fi
    i=$(( i + 1 ))
  done

  {
    printf '// ladder: %s generic callback unit(s) x %s site(s) x arity %s, mode generic\n\n' "$ALIASES" "$SITES" "$ARITY"
    echo "typealias Integer = int(i64.min to i64.max)"
    echo ""
    printf 'function writer(%s) returns Integer\n' "$PARAMS"
    printf '\ta0000 = a0000 + 1\n'
    printf '\treturn %s\n' "$BODY_SUM"
    printf "end 'writer'\n\n"
    i=0
    while [ "$i" -lt "$ALIASES" ]; do
      printf 'type G%06d uses T\n' "$i"
      printf '\ttypealias Step = function(%s) returns Integer\n' "$TYPES"
      printf '\tlet seed as T\n\n'
      printf "\tstatic function create(seed T) returns Self\n\t\treturn Self{seed: seed}\n\tend 'create'\n\n"
      printf '\tfunction run(step Step) returns Integer\n'
      printf '\t\tlet v = seed\n'
      printf '\t\tvar acc = 0 as Integer\n'
      s=0
      while [ "$s" -lt "$SITES" ]; do
        printf '\t\tacc = acc + step(%s)\n' "$LET_ARGS"
        s=$(( s + 1 ))
      done
      printf '\t\treturn acc\n'
      printf "\tend 'run'\n\n"
      printf '\tfunction runVar(step Step) returns Integer\n'
      printf "$VAR_DECLS"
      printf '\t\tvar acc = 0 as Integer\n'
      s=0
      while [ "$s" -lt "$SITES" ]; do
        printf '\t\tacc = acc + step(%s)\n' "$VAR_ARGS"
        s=$(( s + 1 ))
      done
      printf '\t\treturn acc\n'
      printf "\tend 'runVar'\n"
      printf "end 'G%06d'\n\n" "$i"
      printf 'typealias GI%06d = G%06d with Integer\n\n' "$i" "$i"
      printf 'function f%06d(%s) returns Integer\n\treturn %s\nend '"'"'f%06d'"'"'\n\n' "$i" "$PARAMS" "$BODY_SUM" "$i"
      i=$(( i + 1 ))
    done
    echo "function main() returns ExitCode"
    echo "	var total = 0 as Integer"
    i=0
    while [ "$i" -lt "$ALIASES" ]; do
      printf '\tlet g%06d = GI%06d.create(1 as Integer)\n' "$i" "$i"
      printf '\ttotal = total + g%06d.run(f%06d)\n' "$i" "$i"
      printf '\ttotal = total + g%06d.runVar(writer)\n' "$i"
      i=$(( i + 1 ))
    done
    echo "	if total < 0 'neg'"
    echo "		return 1"
    echo "	end 'neg'"
    echo ""
    echo "	return 0"
    echo "end 'main'"
  } > "$OUT"
  exit 0
fi

if [ "$MODE" = "byref" ]; then
  BINDING="var"
else
  BINDING="let"
fi

PARAM_DECL=""
TYPE_PARAMS=""
BODY_SUM=""
ARG_LIST=""
i=0
while [ "$i" -lt "$ARITY" ]; do
  NAME=$(printf 'a%04d' "$i")
  LOCAL=$(printf 'p%04d' "$i")
  if [ "$i" -eq 0 ]; then
    PARAM_DECL="$NAME Integer"
    TYPE_PARAMS="Integer"
    BODY_SUM="$NAME"
    ARG_LIST="$LOCAL"
  else
    PARAM_DECL="$PARAM_DECL, $NAME Integer"
    TYPE_PARAMS="$TYPE_PARAMS, Integer"
    BODY_SUM="$BODY_SUM + $NAME"
    ARG_LIST="$ARG_LIST, $LOCAL"
  fi
  i=$(( i + 1 ))
done

{
  printf '// ladder: %s alias(es) x %s site(s) x arity %s, mode %s\n' \
    "$ALIASES" "$SITES" "$ARITY" "$MODE"
  echo ""
  echo "typealias Integer = int(i64.min to i64.max)"
  echo ""

  if [ "$MODE" = "byref" ]; then
    printf 'function writer(%s) returns Integer\n' "$PARAM_DECL"
    i=0
    while [ "$i" -lt "$ARITY" ]; do
      NAME=$(printf 'a%04d' "$i")
      printf '\t%s = %s + 1\n' "$NAME" "$NAME"
      i=$(( i + 1 ))
    done
    printf '\treturn %s\n' "$BODY_SUM"
    printf "end 'writer'\n\n"
  fi

  i=0
  while [ "$i" -lt "$ALIASES" ]; do
    printf 'typealias Op%06d = function(%s) returns Integer\n' "$i" "$TYPE_PARAMS"
    printf 'function f%06d(%s) returns Integer\n' "$i" "$PARAM_DECL"
    printf '\treturn %s\n' "$BODY_SUM"
    printf "end 'f%06d'\n\n" "$i"
    i=$(( i + 1 ))
  done

  i=0
  while [ "$i" -lt "$ALIASES" ]; do
    printf 'function u%06d(g Op%06d) returns Integer\n' "$i" "$i"
    j=0
    while [ "$j" -lt "$ARITY" ]; do
      printf '\t%s p%04d = 1 as Integer\n' "$BINDING" "$j"
      j=$(( j + 1 ))
    done
    printf '\tvar acc = 0 as Integer\n'
    s=0
    while [ "$s" -lt "$SITES" ]; do
      printf '\tacc = acc + g(%s)\n' "$ARG_LIST"
      s=$(( s + 1 ))
    done
    printf '\treturn acc\n'
    printf "end 'u%06d'\n\n" "$i"
    i=$(( i + 1 ))
  done

  echo "function main() returns ExitCode"
  echo "	var total = 0 as Integer"
  i=0
  while [ "$i" -lt "$ALIASES" ]; do
    printf '\ttotal = total + u%06d(f%06d)\n' "$i" "$i"
    i=$(( i + 1 ))
  done
  if [ "$MODE" = "byref" ]; then
    printf '\tvar w0 = 1 as Integer\n'
    j=1
    while [ "$j" -lt "$ARITY" ]; do
      printf '\tvar w%d = 1 as Integer\n' "$j"
      j=$(( j + 1 ))
    done
    ARGS="w0"
    j=1
    while [ "$j" -lt "$ARITY" ]; do
      ARGS="$ARGS, $(printf 'a%04d' "$j"): w$j"
      j=$(( j + 1 ))
    done
    printf '\ttotal = total + writer(%s)\n' "$ARGS"
  fi
  echo "	if total < 0 'neg'"
  echo "		return 1"
  echo "	end 'neg'"
  echo ""
  echo "	return 0"
  echo "end 'main'"
} > "$OUT"
