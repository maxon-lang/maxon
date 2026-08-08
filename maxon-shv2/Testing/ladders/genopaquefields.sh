#!/usr/bin/env bash
# Ladder generator for the W41 GENERIC paths the `scale-test` corpus structurally cannot express:
# `ProgramSignatures.substituteInstanceArgsThrough`'s per-argument interner probe, and the
# per-instance registration inside `MmRuntime.closeDestructorNeeds` /
# `MmRuntime.rootManagedOpaqueArrayElementDrops`.
#
# Usage: genopaquefields.sh <units> <outfile>
#
# ⚠ THE CORPUS CANNOT SEE THESE. `ScaleCorpus` generates generics, but none whose body reads a
# STRUCT-TYPED field through an instance receiver — and that read is the whole of the path W41 widened:
# `substituteThroughInstanceInterned` used to return a `structRef` immediately and now does an
# `interner.nameOf` plus an `isTupleTypeName` prefix test first. A Δ0 from the standing ladder measures
# the corpus, not the cost.
#
# Each unit emits ONE plain struct `S<k>`, ONE generic `G<k> uses T` holding both an opaque `T` field and
# an `S<k>` field, ONE instantiation `I<k> = G<k> with Integer`, and — in `main` — a construction and a
# field read through the instance. So `<units>` moves declared types, generic instances, structRef
# substitutions and destructor-needs registrations together, which is what makes it the right ladder for
# "did listing another stdlib module multiply anything".
#
# ⭐ MEASURED 2026-08-07, x64-windows, units 25 → 50 → 100 → 200 → 400:
#   phase:deriveRuntimeNeeds allocations   6,119 · 7,899 · 11,415 · 18,447 · 32,473
#   per-unit delta                         71.2 · 70.3 · 70.3 · 70.1   -- DEAD FLAT ⇒ strictly LINEAR
#   phase:semanticCheck CPU (ms)           1.0 · 1.6 · 2.8 · 4.9 · 10.4   (×1.60 ×1.75 ×1.75 ×2.12)
#   phase:signatures CPU (ms)              36.9 · 41.9 · 49.9 · 69.5 · 108.0
# The destructor-needs closure is a CONSTANT 70.3 allocations per unit, not a product with the instance
# count — the shared `CascadeWorklist` (`Compiler/Runtime/CascadeNeeds.maxon`) registers each node once
# and expands it once. A row claiming `closeDestructorNeeds` is O(cascades x instances) is describing the
# pre-worklist shape and is stale.
#
# ⚠ EVERY UNIT LANDS IN ONE `main`, SO `regalloc` DOMINATES AND BENDS — 501.0 → 1,569.2 ms at units
# 200 → 400 (x3.13), of which `regalloc:splitting` is 408.2 → 1,351.8 (x3.31). That is the KNOWN and
# BUDGETED `SplitLiveRanges` term (`ARCHITECTURE.md:1336-1345`), triggered here by this ladder putting
# everything in a single growing function. It is not a finding of this ladder: read the front-end phase
# columns, which are the ones this generator exists to move.
set -euo pipefail
UNITS="$1"; OUT="$2"

{
  printf '// ladder: %s generic unit(s) with an opaque field and a struct-typed field\n' "$UNITS"
  echo "typealias Integer = int(i64.min to i64.max)"
  echo ""

  k=1
  while [ "$k" -le "$UNITS" ]; do
    printf 'type S%d\n' "$k"
    echo -e "\texport var n as Integer"
    echo -e "\texport static function create(n Integer) returns Self"
    echo -e "\t\treturn Self{n: n}"
    echo -e "\tend 'create'"
    printf "end 'S%d'\n\n" "$k"

    printf 'type G%d uses T\n' "$k"
    echo -e "\tvar v as T"
    printf '\tvar s as S%d\n' "$k"
    printf '\texport static function create(x T, y S%d) returns Self\n' "$k"
    echo -e "\t\treturn Self{v: x, s: y}"
    echo -e "\tend 'create'"
    # The structRef return is what routes through `substituteThroughInstanceInterned`.
    printf '\texport function part() returns S%d\n' "$k"
    echo -e "\t\treturn s"
    echo -e "\tend 'part'"
    printf "end 'G%d'\n\n" "$k"

    printf 'typealias I%d = G%d with Integer\n\n' "$k" "$k"
    k=$(( k + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar acc = 0"
  k=1
  while [ "$k" -le "$UNITS" ]; do
    printf '\tlet g%d = I%d.create(%d, y: S%d.create(%d))\n' "$k" "$k" "$k" "$k" "$k"
    printf '\tacc = acc + g%d.part().n\n' "$k"
    k=$(( k + 1 ))
  done
  echo -e "\tprint(\"acc={acc}\")"
  echo -e "\treturn 0 as ExitCode"
  echo "end 'main'"
} > "$OUT"
