#!/usr/bin/env bash
# Ladder generator for the ARGUMENT LIST of a WITNESS DISPATCH — the axis P1.7a slice 2b-vi added and
# the one `genwitness.sh` holds at ONE. It drives `Parser.parseWitnessMethodOnValue` →
# `Parser.parseCallArgs` → `Parser.slottedWitnessArgs` → `Project.slotCallArgs` → `argSlotPosition`,
# which is the whole chain that slice put behind a witness call's parentheses.
#
# Usage: genwitnessargs.sh <conformers> <methods> <args> <decl|reverse> <outfile>
#
# ⭐ **`ScaleCorpus` GENERATES NO INTERFACE AND NO WITNESS DISPATCH AT ALL**, so every column of
# `scale-test` reads a flat Δ0 for anything on this path — which measures UNREACHED, not free. That is
# `genwitness.sh`'s standing warning and it applies here unchanged; this generator adds the one knob
# that ladder has not got.
#
# ⭐ THE THREE SIZE KNOBS ARE SEPARABLE, and they have to be, because the costs they reach are different:
#   * `<conformers>` is the number of witness TABLES and instantiations. It moves the RELOC list and the
#     program's function count. It does NOT move the number of dispatch call sites the parser sees:
#     under dictionary-passing there is exactly ONE shared `Box.run()` body however many instantiations
#     exist, so the parse-time argument work is invariant in this knob. (Turning it and watching
#     `phase:parse` NOT move is the cheapest proof that dictionary-passing is what is being measured.)
#   * `<methods>` is slots per table AND — because `run()` dispatches once per method — the number of
#     witness dispatch CALL SITES the parser slots. This is the axis the per-call-site constant rides.
#   * `<args>` is the ARITY of every interface method, i.e. `P` in `slotCallArgs`: the length of the
#     `paramNames` list each call's labels are slotted against. This is the axis slice 2b-vi created.
# Doubling any one of them doubles what it reaches, so the ratio between two rungs IS the growth
# (x2.00 linear, x4.00 quadratic) — the same reading `scale-test`'s doubling ladder gives.
#
# ⭐⭐ `<decl|reverse>` IS THE LABEL-ORDER CONTROL, AND THE TWO ARE THE SAME PROGRAM TO THE BYTE.
# Every argument is written `aNNNN: 1` — a FIXED-WIDTH label and a fixed literal — so reversing the
# argument order permutes equal-length chunks and changes NOTHING about the program's size, its token
# count, its lexing, its IR or its codegen. The ONLY thing that differs is which parameter each
# argument names, and therefore whether `argSlotPosition`'s O(P) fast path (test `paramNames[argIndex]`
# before scanning) HITS or MISSES:
#   * `decl`    — argument `i` is labelled with parameter `i`'s name. The fast path hits every time:
#                 O(1) per argument, O(P) per call. This is Maxon's own house style and what real
#                 source overwhelmingly looks like.
#   * `reverse` — argument `i` is labelled with parameter `P-1-i`'s name. The fast path misses for every
#                 argument but the middle one, so each falls back to `indexOfParamIn`'s scan and the
#                 call costs ~P^2/2 comparisons.
# Subtracting `decl` from `reverse` is therefore the label SCAN and nothing else. That quadratic is NOT
# new and NOT specific to witness dispatch — `argSlotPosition` is the shared door, so a direct call
# written in reverse label order has always cost the same — but this is the ladder that prices it.
#
# ⚠ **THE SCAN ALLOCATES NOTHING, so the allocation columns read FLAT however quadratic it is.** Read
# the CPU column (`--log=compiler:debug`, or the `cputicks` total), which is the column that exists for
# exactly this. The allocation columns still earn their place here: they price the per-call-site
# BUFFERS (`slotCallArgs`'s two, plus the parser's argument columns), which the CPU column cannot
# separate from the scan.
#
# ⚠⚠ **`<args>` HAS A CEILING OF 63 AND IT IS THE LANGUAGE'S, NOT THIS SCRIPT'S** — measured: `<args>`
# = 64 is refused with `E2015: a function with 65 argument slots — more than the 64 a call can carry`
# (the 64th slot is the dispatch's own receiver; the limit is the width of the per-argument float mask).
# That cap is worth more than a usage note: it means `P` in `slotCallArgs` is BOUNDED BY 64 for every
# program the compiler will accept, so the `reverse` scan's P^2/2 term cannot exceed ~2,048 comparisons
# per call however the ladder is turned. A quadratic in a variable the type system caps at 64 is a
# constant, and this is the ladder that establishes which one.
#
# ⚠ EVERY interface-method parameter is a plain `int`, deliberately — an indirect call declares every
# non-float argument an i64, so a `bool` or an `ExitCode` formal on an indirectly-reachable callee is
# refused by `requireIndirectlyReachableParamsAreMachineWords`. Same constraint, same reason, as
# `genwitness.sh`; see its header for how that refusal doubles as a reach check.
#
# ⚠ Conformer, method and parameter names are all FIXED-WIDTH (`P000123`, `m00012`, `a0001`), so every
# rung's program differs in COUNT and never in how many digits N has — a byte column that moved because
# a literal grew a digit is the artifact that avoids.
#
# Returns 0 at every size, so a rung that miscompiles shows up as a wrong exit code and not merely as a
# time.
set -euo pipefail
CONFORMERS="$1"; METHODS="$2"; ARGS="$3"; ORDER="$4"; OUT="$5"

case "$ORDER" in
  decl|reverse) ;;
  *) echo "genwitnessargs.sh: order must be 'decl' or 'reverse', got '$ORDER'" >&2; exit 2 ;;
esac

if [ "$METHODS" -lt 1 ]; then
  echo "genwitnessargs.sh: <methods> must be at least 1 — an interface with no method mints no slot and no dispatch" >&2
  exit 2
fi

if [ "$ARGS" -lt 1 ]; then
  echo "genwitnessargs.sh: <args> must be at least 1 — a nullary method is what genwitness.sh already measures" >&2
  exit 2
fi

# The parameter list, the body's sum and the two argument orders are LOOP-INVARIANT: they depend only on
# `<args>`, so they are built ONCE. Built per method they would fork a subshell per line, which on
# Windows makes the GENERATOR the thing being measured rather than the compiler (the trap
# `genmutchain.sh` documents, paid for once already).
PARAM_DECL=""
BODY_SUM=""
ARGS_DECL=""
i=0
while [ "$i" -lt "$ARGS" ]; do
  NAME=$(printf 'a%04d' "$i")
  if [ "$i" -eq 0 ]; then
    PARAM_DECL="$NAME int"
    ARGS_DECL="$NAME: 1"
  else
    PARAM_DECL="$PARAM_DECL, $NAME int"
    ARGS_DECL="$ARGS_DECL, $NAME: 1"
  fi
  BODY_SUM="$BODY_SUM + $NAME"
  i=$(( i + 1 ))
done

# `reverse` is the same chunks in the opposite order — built by walking down rather than by reversing a
# string, so the separator placement is identical and the two really are the same length.
ARGS_REVERSE=""
i=$(( ARGS - 1 ))
while [ "$i" -ge 0 ]; do
  NAME=$(printf 'a%04d' "$i")
  if [ "$i" -eq $(( ARGS - 1 )) ]; then
    ARGS_REVERSE="$NAME: 1"
  else
    ARGS_REVERSE="$ARGS_REVERSE, $NAME: 1"
  fi
  i=$(( i - 1 ))
done

ARG_LIST="$ARGS_DECL"
if [ "$ORDER" = "reverse" ]; then
  ARG_LIST="$ARGS_REVERSE"
fi

{
  echo "typealias LadderInt = int(i64.min to i64.max)"
  printf '// ladder: %s conformer(s) x %s method(s) x %s arg(s), label order %-7s\n' \
    "$CONFORMERS" "$METHODS" "$ARGS" "$ORDER"
  echo ""

  echo "interface Digest"
  m=0
  while [ "$m" -lt "$METHODS" ]; do
    printf '\tfunction m%05d(%s) returns LadderInt\n' "$m" "$PARAM_DECL"
    m=$(( m + 1 ))
  done
  echo "end 'Digest'"
  echo ""

  i=0
  while [ "$i" -lt "$CONFORMERS" ]; do
    printf 'type P%06d implements Digest\n' "$i"
    printf '\texport var x as LadderInt\n'
    printf '\texport static function create(x LadderInt) returns Self\n'
    printf '\t\treturn Self{ x: x }\n'
    printf "\tend 'create'\n"
    m=0
    while [ "$m" -lt "$METHODS" ]; do
      printf '\texport function m%05d(%s) returns LadderInt\n' "$m" "$PARAM_DECL"
      printf '\t\treturn self.x%s\n' "$BODY_SUM"
      printf "\tend 'm%05d'\n" "$m"
      m=$(( m + 1 ))
    done
    printf "end 'P%06d'\n\n" "$i"
    i=$(( i + 1 ))
  done

  # ONE shared generic body for every instantiation — dictionary-passing, not monomorphization — so the
  # dispatch call sites the parser slots are `<methods>` in total and NOT `conformers x methods`.
  echo "type Box uses T where T is Digest"
  echo -e "\texport var item as T"
  echo -e "\texport static function create(item T) returns Self"
  echo -e "\t\treturn Self{ item: item }"
  echo -e "\tend 'create'"
  echo -e "\texport function run() returns LadderInt"
  echo -e "\t\tvar acc = 0"
  m=0
  while [ "$m" -lt "$METHODS" ]; do
    printf '\t\tacc = acc + self.item.m%05d(%s)\n' "$m" "$ARG_LIST"
    m=$(( m + 1 ))
  done
  echo -e "\t\treturn acc"
  echo -e "\tend 'run'"
  echo "end 'Box'"
  echo ""

  i=0
  while [ "$i" -lt "$CONFORMERS" ]; do
    printf 'typealias B%06d = Box with P%06d\n' "$i" "$i"
    i=$(( i + 1 ))
  done
  echo ""

  echo "function main() returns ExitCode"
  echo -e "\tvar total = 0"
  i=0
  while [ "$i" -lt "$CONFORMERS" ]; do
    printf '\tlet p%06d = P%06d.create(1)\n' "$i" "$i"
    printf '\tlet b%06d = B%06d.create(p%06d)\n' "$i" "$i" "$i"
    printf '\ttotal = total + b%06d.run()\n' "$i"
    i=$(( i + 1 ))
  done
  echo -e "\tif total > 0 'ok'"
  echo -e "\t\treturn 0"
  echo -e "\tend 'ok'"
  echo -e "\treturn 1"
  echo "end 'main'"
} > "$OUT"
