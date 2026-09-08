#!/usr/bin/env bash
# Ladder generator for the PARAMETER-MUTATION fixpoint (`SemanticCheck.buildParamMutationSummary`).
# Usage: genmutchain.sh <chains> <depth> <params> <outfile>
#
# Emits `chains` independent chains of `depth` functions each, every function taking `params`
# managed arrays and passing all of them on to the next link. The LAST link of each chain writes
# every parameter, so the least fixpoint has to walk the whole chain backwards and EVERY function
# in the program ends up with a non-zero mask — the worst shape a mutation fixpoint has.
#
# ⭐ THE THREE AXES ARE SEPARABLE, WHICH IS THE POINT. `chains x depth` is the function count and
# the call-site count; `depth` alone is the fixpoint's iteration depth; `params` alone is how many
# BITS a mask gains, i.e. how many times a function can re-enter the worklist. Holding
# `chains * depth` FIXED while varying `depth` measures depth at constant program size — the only
# way to tell a depth term from a size term, which a ladder that doubles all three at once cannot.
#
# `main` allocates ONE set of `var` arrays and hands them to every chain head, so main's own body
# is constant across the `chains` axis and the arguments are legal (a `let` would be E3019).
#
# ⚠ The parameter/argument lists are LOOP-INVARIANT and are built ONCE. They were originally
# `$(...)` helpers called per function, which forks a subshell per line — on Windows that made the
# GENERATOR, not the compiler, the thing being measured (a 2048-function program took minutes to
# emit and the compile it fed took seconds).
set -euo pipefail
CHAINS="$1"; DEPTH="$2"; PARAMS="$3"; OUT="$4"

PARAM_DECL="p0 IntArray"
ARG_LIST="p0"
MAIN_ARGS="a0"
MAIN_DECLS=$'\tvar a0 = IntArray.create()'
LEAF_WRITES=$'\tp0.push(0)'
i=1
while [ "$i" -lt "$PARAMS" ]; do
  PARAM_DECL="$PARAM_DECL, p$i IntArray"
  ARG_LIST="$ARG_LIST, p$i: p$i"
  MAIN_ARGS="$MAIN_ARGS, p$i: a$i"
  MAIN_DECLS="$MAIN_DECLS"$'\n\t'"var a$i = IntArray.create()"
  LEAF_WRITES="$LEAF_WRITES"$'\n\t'"p$i.push($i)"
  i=$(( i + 1 ))
done

LAST=$(( DEPTH - 1 ))

{
  echo "// ladder: $CHAINS chain(s) of depth $DEPTH, $PARAMS param(s) each"
  echo "typealias Integer = int(i64.min to i64.max)"
  echo "typealias IntArray = Array with Integer"
  echo ""

  c=0
  while [ "$c" -lt "$CHAINS" ]; do
    l=0
    while [ "$l" -lt "$DEPTH" ]; do
      echo "function f${c}_${l}($PARAM_DECL)"
      if [ "$l" -eq "$LAST" ]; then
        # The leaf: write every parameter, which is what seeds the fixpoint.
        echo "$LEAF_WRITES"
      else
        echo -e "\tf${c}_$(( l + 1 ))($ARG_LIST)"
      fi
      echo "end 'f${c}_${l}'"
      l=$(( l + 1 ))
    done
    c=$(( c + 1 ))
  done

  echo "function main() returns ExitCode"
  echo "$MAIN_DECLS"
  c=0
  while [ "$c" -lt "$CHAINS" ]; do
    echo -e "\tf${c}_0($MAIN_ARGS)"
    c=$(( c + 1 ))
  done
  echo -e "\treturn try a0.get(0) otherwise -1"
  echo "end 'main'"
} > "$OUT"
