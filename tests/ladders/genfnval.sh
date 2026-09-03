#!/usr/bin/env bash
# Ladder generator for FUNCTION VALUES AND THEIR DECLARED TYPES — the axis `ScaleCorpus` states it
# does not generate ("NOT GENERATED … `for`-in, closures, async") and which `--emit-corpus` confirms
# to the file: **ZERO `typealias … = function(…)` and ZERO function references in all 465 generated
# files, at every rung.** Every column of `scale-test` therefore reads a flat Δ0 for anything on this
# path, and a Δ0 from a ladder that cannot express the feature measures the INSTRUMENT'S BLIND SPOT,
# not the cost.
#
# Usage: genfnval.sh <aliases> <sites> <arity> <indirect|direct> <outfile>
#
# It drives every structure the "a function value's DECLARED TYPE is authoritative" rung added:
# `Project.functionAliasShapes` (built by `TypeResolution.resolveFunctionAliasShapes`),
# `declaredFunctionShapeOf`, `SemanticCheck.checkIndirectCall`, `checkFunctionTypeDoors` +
# `Project.pendingFunctionTypeDoors`, and `LowerMaxonToStd.indirectCalleeParamTypes` +
# `paramFloatMask` + `widenIntArgsToFloatParams`.
#
# ⭐ THE THREE SIZE KNOBS ARE SEPARABLE, and they have to be, because the quadratic they are here to
# refute needs two of them moving together to show:
#   * `<aliases>` is the number of function TYPEALIASES — the size of `functionAliasShapes`, the
#     length of `resolveFunctionAliasShapes`' one loop, and the number of function-type DOORS
#     (each alias contributes a `return` door and an argument door). It also moves the program's
#     function count, so it is the "whole program doubles" knob.
#   * `<sites>` is the number of INDIRECT CALL SITES PER ALIAS. It moves `checkIndirectCall` and
#     `indirectCalleeParamTypes` at a FIXED alias count, which is the axis that separates
#     *linear in call sites* from *linear in program size*. ⭐ Doubling `<sites>` alone while
#     `<aliases>` stays put is the direct test of "does a call site scan the alias registry?" — a
#     per-site scan over all aliases costs `sites x aliases`, so it reads x2.00 here and x4.00 on
#     the `<aliases>` knob, and only running BOTH tells the two apart.
#   * `<arity>` is the number of parameters in each function type — `P` in `functionShapesAgree`'s
#     comparison loop, in `paramFloatMask`'s mask walk and in `widenIntArgsToFloatParams`.
#
# ⭐⭐ `<indirect|direct>` IS THE CONTROL AND IT IS THE SAME PROGRAM SHAPE. `direct` emits the
# identical alias declarations, the identical callee functions, the identical body statements and the
# identical CALL COUNT — but calls each callee BY NAME instead of through a function value, so the
# program contains no function reference, no indirect call and no function-type door. Every path this
# rung touched runs exactly zero times in it while parse, lowering, regalloc and codegen see the same
# number of calls with the same arguments. Subtracting `direct` from `indirect` is therefore the
# function-value machinery and nothing else.
#
# ⚠ The function typealiases are still DECLARED in `direct` mode, so `resolveFunctionAliasShapes`
# runs over them in both. That is deliberate: it keeps the declaration cost out of the subtraction,
# leaving the difference to be the CALL and DOOR paths alone. To price the declaration itself,
# compare `direct` against a run with `<aliases>` halved.
#
# ⚠ Alias, callee, wrapper and parameter names are all FIXED-WIDTH (`Op000123`, `f000123`, `a0001`),
# so a rung's program differs in COUNT and never in how many digits N has — the byte-column artifact
# that avoids is the same one `genwitnessargs.sh` documents.
#
# ⚠ The parameter list, the argument list and the body sum are LOOP-INVARIANT and built ONCE. Built
# per emitted line they would fork a subshell per line, which on Windows makes the GENERATOR the
# thing being measured (the trap `genmutchain.sh` paid for).
#
# Returns 0 at every size in both modes, so a rung that miscompiles shows up as a wrong exit code
# and not merely as a number.
set -euo pipefail
ALIASES="$1"; SITES="$2"; ARITY="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  indirect|direct) ;;
  *) echo "genfnval.sh: mode must be 'indirect' or 'direct', got '$MODE'" >&2; exit 2 ;;
esac

if [ "$ALIASES" -lt 1 ]; then
  echo "genfnval.sh: <aliases> must be at least 1" >&2
  exit 2
fi

if [ "$SITES" -lt 1 ]; then
  echo "genfnval.sh: <sites> must be at least 1 — a wrapper with no call reaches no call path" >&2
  exit 2
fi

if [ "$ARITY" -lt 1 ]; then
  echo "genfnval.sh: <arity> must be at least 1 — a nullary function type has no parameter list to compare" >&2
  exit 2
fi

# Loop-invariant chunks: the callee's parameter declaration, the body sum over those parameters, the
# function TYPE's bare parameter-type list, and the two argument lists.
#
# ⚠ THE TWO MODES CANNOT SHARE ONE ARGUMENT LIST, AND THE REASON IS THE LANGUAGE'S: a DIRECT call's
# second and later arguments MUST be labelled (E2053), while an INDIRECT call's are positional and
# CANNOT be (a function TYPE has no parameter names — `Parser.readFunctionTypeParam` discards them).
# So `direct` is a SHAPE control and not a byte-identical one: it carries `aNNNN: ` per argument past
# the first, which is a few tokens more per call site to lex and parse. Read the subtraction as an
# upper bound on the function-value machinery, never as an exact one — and read the RATIO within each
# mode, which the label bytes cannot touch because they are the same at every rung.
PARAM_DECL=""
TYPE_PARAMS=""
BODY_SUM=""
ARG_LIST=""
ARG_LIST_LABELLED=""
i=0
while [ "$i" -lt "$ARITY" ]; do
  NAME=$(printf 'a%04d' "$i")
  if [ "$i" -eq 0 ]; then
    PARAM_DECL="$NAME Integer"
    TYPE_PARAMS="Integer"
    BODY_SUM="$NAME"
    ARG_LIST="1"
    ARG_LIST_LABELLED="1"
  else
    PARAM_DECL="$PARAM_DECL, $NAME Integer"
    TYPE_PARAMS="$TYPE_PARAMS, Integer"
    BODY_SUM="$BODY_SUM + $NAME"
    ARG_LIST="$ARG_LIST, 1"
    ARG_LIST_LABELLED="$ARG_LIST_LABELLED, $NAME: 1"
  fi
  i=$(( i + 1 ))
done

{
  printf '// ladder: %s alias(es) x %s site(s) x arity %s, mode %s\n' \
    "$ALIASES" "$SITES" "$ARITY" "$MODE"
  echo ""
  echo "typealias Integer = int(i64.min to i64.max)"
  echo ""

  # One function TYPEALIAS and one matching declared FUNCTION per unit. Both modes emit both, so the
  # declaration cost is common and cancels in the subtraction.
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
    if [ "$MODE" = "indirect" ]; then
      # The wrapper takes the callee as an ALIAS-TYPED PARAMETER, so every call in it is an
      # `indirectCall` validated against `Op%06d`'s declared shape, and the argument at its call site
      # in `main` is a function value meeting a function-typed place (the ARGUMENT door,
      # `SemanticCheck.checkOneArgType`'s `functionArg` arm).
      printf 'function u%06d(g Op%06d) returns Integer\n' "$i" "$i"
      printf '\tvar acc = 0\n'
      s=0
      while [ "$s" -lt "$SITES" ]; do
        printf '\tacc = acc + g(%s)\n' "$ARG_LIST"
        s=$(( s + 1 ))
      done
      printf '\treturn acc\n'
      printf "end 'u%06d'\n" "$i"
      # A `return` door: a function value returned where a function TYPE is declared
      # (`Parser.parseReturnStatement` records it, `checkFunctionTypeDoors` drains it).
      printf 'function p%06d() returns Op%06d\n' "$i" "$i"
      printf '\treturn f%06d\n' "$i"
      printf "end 'p%06d'\n\n" "$i"
    else
      # CONTROL: the same statement count and the same call count, by NAME. No function value exists,
      # so no door is recorded and no indirect call is emitted.
      printf 'function u%06d(g Integer) returns Integer\n' "$i"
      printf '\tvar acc = 0\n'
      s=0
      while [ "$s" -lt "$SITES" ]; do
        printf '\tacc = acc + f%06d(%s)\n' "$i" "$ARG_LIST_LABELLED"
        s=$(( s + 1 ))
      done
      printf '\treturn acc\n'
      printf "end 'u%06d'\n" "$i"
      printf 'function p%06d() returns Integer\n' "$i"
      printf '\treturn f%06d(%s)\n' "$i" "$ARG_LIST_LABELLED"
      printf "end 'p%06d'\n\n" "$i"
    fi
    i=$(( i + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar total = 0"
  i=0
  while [ "$i" -lt "$ALIASES" ]; do
    if [ "$MODE" = "indirect" ]; then
      printf '\ttotal = total + u%06d(f%06d)\n' "$i" "$i"
      printf '\tlet q%06d = p%06d()\n' "$i" "$i"
      printf '\ttotal = total + q%06d(%s)\n' "$i" "$ARG_LIST"
    else
      printf '\ttotal = total + u%06d(1)\n' "$i"
      printf '\ttotal = total + p%06d()\n' "$i"
    fi
    i=$(( i + 1 ))
  done
  echo -e "\tif total > 0 'ok'"
  echo -e "\t\treturn 0 as ExitCode"
  echo -e "\tend 'ok'"
  echo -e "\treturn 1 as ExitCode"
  echo "end 'main'"
} > "$OUT"
