#!/usr/bin/env bash
# The ENCODE phase's two dimensions, held INDEPENDENT: how many functions, and how big each is.
#
# The question: `phase:encode` reads superlinear in CPU on the scale corpus (~x2.3 per doubling
# against a ladder that doubles) while its allocations read ~x1.95. That is the shape only the CPU
# column can see — a cost that walks or re-walks without allocating. The corpus doubles
# EVERY knob it has at once, so it cannot say whether the term is per-FUNCTION or per-op-WITHIN a
# function: both read x2 there.
#
# ⚠ THE THREE KNOBS ARE INDEPENDENT, WHICH IS THE WHOLE POINT (the same discipline gennest.sh and
# genmutchain.sh use — see README). `emitFunctionChunk` builds ONE ByteArray, ONE label map and ONE
# fixup list PER FUNCTION, so a term quadratic in a function's own size is invisible to a ladder that
# grows the function COUNT, and vice versa. The three readings that matter:
#
#   • funcs doubling at fixed stmts  — program size doubles, every function stays the same size.
#     A per-function-count term bends here and only here.
#   • stmts doubling at fixed funcs  — program size doubles, the function COUNT stays fixed.
#     A term quadratic in one function's byte count / block count bends here and only here.
#   • funcs * stmts held FIXED       — program size constant, the split varies. Anything that moves
#     is a shape term; anything flat is a pure size term.
#
# `ifs` is the third axis and it is the LABEL/FIXUP density: each `if` mints two blocks and a
# `jcc rel32` + `jmp rel32` pair, so it drives `chunkLabelOffsets` (a per-function Map keyed by
# BlockId) and `rel32Fixups` without moving the op count much. Hold it at 0 to measure a pure
# straight-line encode.
#
# Usage: genemit.sh <funcs> <stmtsPerFunc> <ifsPerFunc> <outfile>
#   e.g. genemit.sh 64 128 0 a.maxon   and   genemit.sh 128 64 0 b.maxon   are the SAME size.
#
# Every generated function is CALLED from main: dead-function elimination runs BEFORE the encode
# phase (`buildBackend`), so an unreachable function is never encoded and a ladder of them would
# measure nothing. One accumulator carries the whole program so the live set stays at one value and
# the allocator never trips E5001 — this ladder is about the ENCODER, not about register pressure.
set -euo pipefail
F="$1"; S="$2"; I="$3"; OUT="$4"

{
  echo "// encode ladder: $F functions x $S statements x $I ifs each"
  echo "typealias Acc = int(0 to u64.max)"
  echo ""

  f=0
  while [ "$f" -lt "$F" ]; do
    echo "function emitUnit${f}(seed Acc) returns Acc"
    echo -e "\tvar acc = seed + ${f}"

    i=0
    while [ "$i" -lt "$I" ]; do
      k=$(( i % 7 + 1 ))
      echo -e "\tif acc > ${k} 'g${i}'"
      echo -e "\t\tacc = acc + ${k}"
      echo -e "\tend 'g${i}'"
      i=$(( i + 1 ))
    done

    s=0
    while [ "$s" -lt "$S" ]; do
      k=$(( s % 9 + 1 ))
      echo -e "\tacc = acc + ${k}"
      s=$(( s + 1 ))
    done

    echo -e "\treturn acc"
    echo "end 'emitUnit${f}'"
    echo ""
    f=$(( f + 1 ))
  done

  # `main` reaches every unit so none is pruned before encoding. The calls are sequential through the
  # one accumulator, so main itself carries a single live value however many units there are.
  echo "function main() returns ExitCode"
  echo -e "\tvar total = 0 as Acc"
  f=0
  while [ "$f" -lt "$F" ]; do
    echo -e "\ttotal = emitUnit${f}(total) mod 1000"
    f=$(( f + 1 ))
  done
  echo -e "\tif total > 0 'nonZero'"
  echo -e "\t\treturn 1"
  echo -e "\tend 'nonZero'"
  echo -e "\treturn 0"
  echo "end 'main'"
} > "$OUT"
