#!/usr/bin/env bash
# Ladder generator for the BUILTIN-CONFORMANCE DIRECT-DISPATCH path on a FLOAT receiver — P1.7a's last
# slice (`float` is `Hashable`, via `StdUnaryOpcode.bitcastF64ToI64` / x64 `movqGprXmm` / arm64
# `arm64FmovGprFp`) and the parse-time lookup that routes a `<float>.<method>()` call to it.
#
# Usage: genfloathash.sh <sites> <hash|equals|inthash|control> <outfile>
#
# ⭐ **`ScaleCorpus` GENERATES NO `.hash()`, NO `.equals()` AND NO `.compare()` ON A FLOAT RECEIVER — NOT
# ONE, AT ANY RUNG.** The install gate is REFERENCED-but-undefined ⟺ INSTALLED, so on the standing ladder
# `buildFloatHash` is never called, the new `bitcastF64ToI64` never reaches an isel, and `movqGprXmm` is
# never encoded. Every column of a default `scale-test` therefore reads a flat Δ0 for the whole slice,
# which measures UNREACHED and not free. This generator is the only way to put a number on it.
#
# ⭐⭐ **THE MODE IS THE WHOLE INSTRUMENT, because two of the four compile under BOTH the pre-rung and the
# post-rung compiler and so support a true A/B on ONE byte-identical file:**
#
#   * `hash`    — `<float>.hash()`, the capability this rung ADDS. Post-rung only: before it, the same
#                 file is a positioned reject (`float` supplied `equals`/`compare` and no `hash`). So this
#                 mode measures a SLOPE within one binary — allocations per call site — and never a delta.
#   * `equals`  — `<float>.equals(<float>)`, which shipped in slice 2b-iii and is UNTOUCHED by this rung
#                 in behaviour. ⭐ **IT IS THE MODE THAT CAN REGRESS, and it is the reason this generator
#                 has four modes rather than one.** `builtinConformerMethod` walks
#                 `builtinConformableProtocolNames()` in the fixed order Hashable, Equatable, Comparable,
#                 `continue`s past a protocol the receiver does not conform to, and RETURNS at the first
#                 protocol whose declaration carries the method. Admitting `float` to `Hashable` converts
#                 that leading `continue` into a `requireBuiltinInterface(Hashable)` — a WHOLE `IrInterface`
#                 synthesized, searched for `equals`, missed, and discarded — on every float `.equals()`
#                 and `.compare()` call site in the program. Same file, two binaries, and the difference
#                 is that one synth.
#   * `inthash` — `<int>.hash()`. THE CONTROL, and it is not a smaller number of the same thing: `int` has
#                 conformed to all three protocols since slice 2b-ii, so its row in
#                 `isIntrinsicBuiltinConformance` is the width this rung just gave `float`, and this rung
#                 cannot have moved it. A non-zero delta here would mean the cost is in the walk itself
#                 rather than in float's row, and would refute the attribution above.
#   * `control` — the same program, the same float receivers, the same branch and accumulator shape, and
#                 NOT ONE conformance method call. Subtracting it removes parse, lowering, regalloc and
#                 encode of an equally large program and leaves the dispatch path alone.
#
# ⚠ **RECEIVERS COME FROM `scaleOpaque`, NOT FROM LITERALS.** `f.hash()` on a literal is a candidate for
# constant folding at some future rung, and a ladder that folds away measures nothing — the corpus's own
# `grew` check exists because that has happened. Every receiver here derives from an opaque call the
# folder cannot see through.
#
# ⚠ Unit and site names are FIXED-WIDTH (`u000123`, `v00`), so a rung differs from the one below it in
# COUNT and not in how many digits N has — a byte column that moved because a literal grew a digit is the
# artifact that avoids.
#
# ⚠ The float and int receiver seeds are written `1.25` and `1000` — FOUR CHARACTERS EACH, so `hash` and
# `inthash` differ in the receiver's TYPE and not in the program's length.
#
# Every mode returns 0 at every size, so a rung that miscompiles shows up as a wrong exit code and not
# merely as a number.
set -euo pipefail
SITES="$1"; MODE="$2"; OUT="$3"

case "$MODE" in
  hash|equals|inthash|control) ;;
  *) echo "genfloathash.sh: mode must be hash|equals|inthash|control, got '$MODE'" >&2; exit 2 ;;
esac

# Sites per function, held FIXED so that doubling `<sites>` doubles the FUNCTION COUNT and leaves each
# function's size — and therefore its register pressure and its splitting cost — exactly where it was.
# Growing one function instead would mix this ladder's axis with the splitter's known K^2 term.
SITES_PER_UNIT=8

if [ "$(( SITES % SITES_PER_UNIT ))" -ne 0 ]; then
  echo "genfloathash.sh: <sites> must be a multiple of $SITES_PER_UNIT so every unit is the same size" >&2
  exit 2
fi

UNITS=$(( SITES / SITES_PER_UNIT ))

{
  echo "typealias LadderInt = int(i64.min to i64.max)"
  printf '// ladder: %s call site(s) in %s unit(s) of %s, mode %-8s\n' \
    "$SITES" "$UNITS" "$SITES_PER_UNIT" "$MODE"
  echo ""

  echo "function scaleOpaque(a LadderInt) returns LadderInt"
  echo -e "\tif a > 100 'big'"
  echo -e "\t\treturn a - 100"
  echo -e "\tend 'big'"
  echo -e "\treturn a + 1"
  echo "end 'scaleOpaque'"
  echo ""

  u=0
  while [ "$u" -lt "$UNITS" ]; do
    printf 'function u%06d(s LadderInt) returns LadderInt\n' "$u"
    echo -e "\tvar acc = 0"
    j=0
    while [ "$j" -lt "$SITES_PER_UNIT" ]; do
      if [ "$MODE" = "inthash" ]; then
        printf '\tlet v%02d = s + 1000\n' "$j"
      else
        printf '\tlet v%02d = s + 1.25\n' "$j"
      fi

      case "$MODE" in
        hash|inthash)
          printf '\tif v%02d.hash() != 0 \047c%02d\047\n' "$j" "$j"
          ;;
        equals)
          # A SECOND receiver, because `equals` is the two-operand surface. It is derived from the same
          # opaque seed, so the pair is never constant-comparable.
          printf '\tlet w%02d = s + 2.25\n' "$j"
          printf '\tif v%02d.equals(w%02d) \047c%02d\047\n' "$j" "$j" "$j"
          ;;
        control)
          printf '\tif v%02d != 0.0 \047c%02d\047\n' "$j" "$j"
          ;;
      esac

      echo -e "\t\tacc = acc + 1"
      printf '\tend \047c%02d\047\n' "$j"
      j=$(( j + 1 ))
    done
    echo -e "\treturn acc"
    printf 'end \047u%06d\047\n\n' "$u"
    u=$(( u + 1 ))
  done

  echo "function main() returns ExitCode"
  echo -e "\tvar total = 0"
  u=0
  while [ "$u" -lt "$UNITS" ]; do
    printf '\tlet k%06d = scaleOpaque(%d)\n' "$u" "$u"
    printf '\ttotal = total + u%06d(k%06d)\n' "$u" "$u"
    u=$(( u + 1 ))
  done
  echo -e "\tif total < 0 'impossible'"
  echo -e "\t\treturn 1"
  echo -e "\tend 'impossible'"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
