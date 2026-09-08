#!/usr/bin/env bash
# Ladder generator for the WITNESS-TABLE RELOCATION path — every `.rdata` slot that names a function,
# and the two per-compile walks over `GlobalDataTable.pendingRdataRelocs` that P1.7a slice 2b-iv-B added
# on the wasm side: `StdToWasm.bakeFuncTableIndexRelocs` (patch each slot with the callee's funcref TABLE
# INDEX) and `StdToWasm.requireIndirectlyReachableParamsAreMachineWords` (refuse a narrow parameter on an
# indirectly-reachable callee). It also drives the x64 twin, `CodeResult.bakeFuncAbs64Relocs`.
#
# Usage: genwitness.sh <conformers> <methods> <dispatch|inert> <outfile>
#
# ⭐ **`ScaleCorpus` GENERATES NO INTERFACE, NO WITNESS DISPATCH AND NO MANAGED-OPAQUE-`T` DESCRIPTOR**,
# so `pendingRdataRelocs` is EMPTY at every rung of the standing ladder and both walks run ZERO
# iterations there. Every column of `scale-test` therefore reads a flat Δ0 for any change to them — which
# measures UNREACHED, not free. This generator is the only way to put a number on that path.
#
# ⭐ THE TWO SIZE KNOBS ARE INDEPENDENT, and they have to be, because they feed the SAME list from
# different sides: **relocs = conformers × methods**.
#   * `<conformers>` is the number of witness TABLES — one `__witness_<Type>.Digest` blob per conforming
#     type per interface. It moves the reloc count AND the program's function count together.
#   * `<methods>` is the number of SLOTS PER TABLE. It moves the reloc count at a nearly fixed conformer
#     count, which is what separates "linear in relocs" from "linear in types".
# Doubling either one doubles the reloc list, so the ratio between two rungs IS the growth (×2.00 linear,
# ×4.00 quadratic) — the same reading `scale-test`'s doubling ladder gives.
#
# ⭐ `<dispatch|inert>` IS THE WHOLE-PATH CONTROL, at a BYTE-IDENTICAL program size:
#   * `dispatch` calls every interface method through the shared `where T is Digest` generic body, so each
#     call site is a `witnessCall` op that `internIndirectCallTypeFor` interns and `emitWitnessCall`
#     lowers, and each conforming type gets a witness table whose every slot is a relocation.
#   * `inert` builds the same types and the same instantiations and reads the conformer's field DIRECTLY
#     instead, so nothing dispatches. Subtracting it from `dispatch` is the ENTIRE witness cost — tables,
#     bake, guard and emit — with parse, lowering and register allocation of an equally large program
#     cancelled out.
#
# ⚠ **`inert` PRODUCES ZERO RELOCATIONS, AND THAT IS MEASURED RATHER THAN INTENDED.** A witness table
# does not survive a program that never dispatches through it: the bool-param probe below is REFUSED
# under `dispatch` (naming `__witness_P000000.Digest'+24`) and compiles CLEAN under `inert`, which is the
# reloc list reading empty. So `inert` is a control for the whole path and NOT a way to price the two
# per-reloc walks with the per-call-site emitters held out — to move relocs without moving call sites,
# turn the `<methods>` knob under `dispatch` instead. (This also means the two walks can never meet a
# slot whose callee dead-function elimination removed, which is the invariant their panics assert.)
#
# ⚠ **EVERY INTERFACE-METHOD PARAMETER HERE IS A PLAIN `int`, DELIBERATELY.** An indirect call declares
# every non-float argument an i64 (the Std op carries a per-argument FLOAT mask, not a WIDTH), so a `bool`
# or an `ExitCode` parameter on an indirectly-reachable callee is refused at compile time by
# `requireIndirectlyReachableParamsAreMachineWords`. That refusal doubles as the cheapest way to CONFIRM
# the ladder is reaching the path at all: change one `other int` below to `other bool`, build
# `--target=wasm32-wasi`, and read the slot the diagnostic names — `__witness_P0.Digest'+32` is one blob
# per conformer with a slot per method, which is the reloc count this generator claims.
#
# ⚠ Nothing here calls an opaque `scaleOpaque` the way most ladders do. It does not need one: a witness
# call is an INDIRECT call through a slot loaded from memory, which no optimizer in the tree can see
# through — the dispatch is its own clobber point (`genclosure.sh` gets its opacity the same way).
#
# ⚠ Conformer and method names are FIXED-WIDTH (`P000123`, `m00012`), so every rung's program differs in
# COUNT and not in how many digits N has — a byte column that moved because a literal grew a digit is the
# artifact this avoids.
#
# Returns 0 on success at every size, on x64 and under wasmtime alike, so a rung that miscompiles shows
# up as a wrong exit code and not merely as a time.
set -euo pipefail
CONFORMERS="$1"; METHODS="$2"; MODE="$3"; OUT="$4"

case "$MODE" in
  dispatch|inert) ;;
  *) echo "genwitness.sh: mode must be 'dispatch' or 'inert', got '$MODE'" >&2; exit 2 ;;
esac

if [ "$METHODS" -lt 1 ]; then
  echo "genwitness.sh: <methods> must be at least 1 — an interface with no method mints no slot and so no relocation" >&2
  exit 2
fi

{
  echo "typealias LadderInt = int(i64.min to i64.max)"
  # The mode is padded to a fixed width so `dispatch` and `inert` are BYTE-IDENTICAL in length: the pair
  # is only worth having if subtracting one from the other cancels everything but the dispatch.
  printf '// ladder: %s conformer(s) x %s method(s) = %s witness slot(s), mode %-8s\n' \
    "$CONFORMERS" "$METHODS" "$(( CONFORMERS * METHODS ))" "$MODE"
  echo ""

  echo "interface Digest"
  m=0
  while [ "$m" -lt "$METHODS" ]; do
    printf '\tfunction m%05d(other LadderInt) returns LadderInt\n' "$m"
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
      printf '\texport function m%05d(other LadderInt) returns LadderInt\n' "$m"
      printf '\t\treturn self.x + other\n'
      printf "\tend 'm%05d'\n" "$m"
      m=$(( m + 1 ))
    done
    printf "end 'P%06d'\n\n" "$i"
    i=$(( i + 1 ))
  done

  # ONE shared generic body for every instantiation — dictionary-passing, not monomorphization, so the
  # `witnessCall` ops live here and the per-instance cost is the WITNESS TABLE rather than a body copy.
  echo "type Box uses T where T is Digest"
  echo -e "\texport var item as T"
  echo -e "\texport static function create(item T) returns Self"
  echo -e "\t\treturn Self{ item: item }"
  echo -e "\tend 'create'"
  echo -e "\texport function run() returns LadderInt"
  echo -e "\t\tvar acc = 0"
  m=0
  while [ "$m" -lt "$METHODS" ]; do
    printf '\t\tacc = acc + self.item.m%05d(1)\n' "$m"
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
    if [ "$MODE" = "dispatch" ]; then
      printf '\ttotal = total + b%06d.run()\n' "$i"
    else
      # Byte-matched to the dispatch line — `p000000.x + 0` is exactly as long as `b000000.run()` — so
      # the control differs from the shape ONLY in where the value comes from, and the two programs are
      # the same size to the byte at every rung.
      printf '\ttotal = total + p%06d.x + 0\n' "$i"
    fi
    i=$(( i + 1 ))
  done
  echo -e "\tif total > 0 'ok'"
  echo -e "\t\treturn 0"
  echo -e "\tend 'ok'"
  echo -e "\treturn 1"
  echo "end 'main'"
} > "$OUT"
