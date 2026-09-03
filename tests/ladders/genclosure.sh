#!/usr/bin/env bash
# Ladder generator for CAPTURING CLOSURES — the construct `ScaleCorpus` states, in as many words,
# that it does not generate ("NOT GENERATED … `for`-in, closures, async"). Every column of
# `scale-test` therefore reads Δ0 for a change to the capture path, and a Δ0 from a ladder that
# cannot express the feature measures the INSTRUMENT'S BLIND SPOT, not the cost.
#
# Usage: genclosure.sh <closures> <captures> <reads> <ranged|plain> <outfile>
#
# Emits `closures` functions, each declaring `captures` parameters and containing ONE closure whose
# body reads every captured parameter `reads` times. So:
#
#   env SLOTS built           = closures x captures      (`buildClosureEnv`, one store per slot)
#   capture READ SITES        = closures x captures x reads (`emitCaptureRead`, one load per site)
#   `captureSlotFor` scan work= closures x captures x reads x captures/2
#
# ⭐ THE THREE AXES ARE SEPARABLE, AND THAT IS THE POINT — the same reason `genmutchain.sh` has
# three. `captureSlotFor` walks `captureNames` LINEARLY on every read to dedup the slot, so its
# cost is (read sites x distinct captures) while the program size is (read sites). Hold `reads`
# fixed and vary `captures` and you separate the scan's second factor from everything else; a
# ladder that doubled both at once could not tell them apart. `fieldStorageType` is asked once per
# read site and once per slot, so it moves with `reads` at fixed `captures`.
#
# `ranged` declares the captured parameters through a ranged typealias, which is the path that
# actually costs: `fieldStorageType` resolves a `named` type through `signatures.aliasOf` (a
# whole-program registry probe) and, on a miss, a `containsEnum` probe. `plain` declares them
# `Integer` — still an alias, so use it as the SHAPE control rather than a no-lookup control;
# shv2 has no bare-`int` parameter spelling to compare against.
#
# ⚠ The parameter, argument and body-expression lists are LOOP-INVARIANT and built ONCE, for
# `genmutchain.sh`'s reason: a `$(...)` helper per line forks a subshell per line and makes the
# GENERATOR the thing being measured.
set -euo pipefail
CLOSURES="$1"; CAPTURES="$2"; READS="$3"; MODE="$4"; OUT="$5"

case "$MODE" in
  ranged) CAP_TYPE="Word" ;;
  plain)  CAP_TYPE="Integer" ;;
  *) echo "genclosure.sh: mode must be 'ranged' or 'plain', got '$MODE'" >&2; exit 2 ;;
esac

# One capture is read as `(pN shr 62)` under `ranged` so the SIGNEDNESS the slot carries is
# actually observed (a `Word` zero-fills where a bare int sign-fills) — the shift rule is what
# makes the declared type load-bearing rather than decorative.
if [ "$MODE" = "ranged" ]; then
  READ_EXPR_FMT='(p%d shr 62)'
else
  READ_EXPR_FMT='(p%d + 1)'
fi

PARAM_DECL="p0 $CAP_TYPE"
CALL_ARGS="0"
BODY="n"
c=1
while [ "$c" -lt "$CAPTURES" ]; do
  PARAM_DECL="$PARAM_DECL, p$c $CAP_TYPE"
  CALL_ARGS="$CALL_ARGS, p$c: 0"
  c=$(( c + 1 ))
done

r=0
while [ "$r" -lt "$READS" ]; do
  c=0
  while [ "$c" -lt "$CAPTURES" ]; do
    # shellcheck disable=SC2059
    BODY="$BODY + $(printf "$READ_EXPR_FMT" "$c")"
    c=$(( c + 1 ))
  done
  r=$(( r + 1 ))
done

{
  echo "// ladder: $CLOSURES closure(s), $CAPTURES capture(s) each, $READS read(s) per capture, $MODE"
  echo "typealias Integer = int(i64.min to i64.max)"
  echo "typealias Word = int(0 to u64.max)"
  echo "typealias ClosureFn = function(Integer) returns Integer"
  echo ""
  echo "function applyClosure(f ClosureFn, x Integer) returns Integer"
  echo -e "\treturn f(x)"
  echo "end 'applyClosure'"
  echo ""

  i=0
  while [ "$i" -lt "$CLOSURES" ]; do
    echo "function useClosure${i}($PARAM_DECL) returns Integer"
    echo -e "\treturn applyClosure(function(n Integer) gives $BODY, x: 1)"
    echo "end 'useClosure${i}'"
    i=$(( i + 1 ))
  done

  echo ""
  echo "function main() returns ExitCode"
  echo -e "\tvar acc = 0"
  i=0
  while [ "$i" -lt "$CLOSURES" ]; do
    echo -e "\tacc = acc + useClosure${i}($CALL_ARGS)"
    i=$(( i + 1 ))
  done
  echo -e "\treturn (acc and 7) as ExitCode"
  echo "end 'main'"
} > "$OUT"
