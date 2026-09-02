#!/usr/bin/env bash
# `maxon test` ITSELF — the one cost in this tree that `scale-test` is not merely blind to but
# STRUCTURALLY CANNOT SEE.
#
# ⛔⛔ **EVERY OTHER LADDER HERE WRITES ONE `.maxon` FILE FOR `scale-test` TO COMPILE. THIS ONE
# WRITES A PROJECT DIRECTORY, BECAUSE `maxon test` TAKES A DIRECTORY AND NOT A FILE.** That is the
# whole reason it exists as a separate shape. `ScaleCorpus` compiles ordinary programs, so a default
# run measures what the test command costs when it is ABSENT — and the answer to that is a measured,
# exact zero (the entry name defaults to `main`, the walk selection to `productionOnly`, the
# compiler-written-source list to empty). Discovery, dispatcher synthesis, staging, the executor, the
# protocol reader and both report faces are reached by NOTHING on the standing ladder. Δ0 there means
# UNREACHED, in every column, and no amount of rungs will change it.
#
# THE TWO KNOBS ARE INDEPENDENT, WHICH IS THE POINT: `maxon test`'s costs are products of the number
# of test FILES (a group, a generated dispatcher, a process launch) and the number of TESTS (a run
# index, a guard, a report line). A ladder that doubled both would read ×4 for a linear cost and could
# never tell the two apart. Hold one, double the other.
#
#   files   <F> <testsPerFile>  — F `*.test.maxon` files with `testsPerFile` tests each.
#   onefile <S> <T>             — S−1 ordinary `.maxon` sources + ONE `*.test.maxon` holding T tests.
#                                 ⭐ **THE ATTRIBUTION CONTROL.** Same source count, same test count,
#                                 same token volume, same staging IO — and exactly ONE group. Anything
#                                 that costs more under `files` than under `onefile` at matched S and
#                                 T is a term multiplied by the GROUP count and nothing else.
#   broken  <F> <testsPerFile>  — `files`, with a bare `int` (E3005) planted in every file, so the
#                                 compile aborts at parse having raised one diagnostic per file. The
#                                 way to reach the staging and diagnostic-remap paths at a rung big
#                                 enough to time without paying for codegen.
#
# ✅ **FOUND A QUADRATIC AND CLOSED IT.** `TestDispatcher.groupTestsByFile` collected the distinct
# files and then walked EVERY test again for each of them — Θ(files × tests), one `FilePath.equals`
# and one managed-struct iteration step per pair. MEASURED with `maxon test --list` (which compiles
# nothing, so the reading is discovery + grouping and not a build), one binary, idle machine:
#
#   files 512 8   (F=512, T=4096)  —  11,357 ms        onefile 512 4096  —  684 ms
#
# Same source count, same test count, same tokens; only the GROUP count differs, and it is a 16.6×.
# Holding T at 2048 and doubling F read the product straight off the ladder — F=16 552 ms, F=64
# 1073 ms, F=256 3062 ms, F=512 6099 ms. After the fix (one pass, with the previous group tried
# first) the same rungs read 380 / 414 / 596 / 1070 ms and the T-dependence at F=512 collapsed from
# ~2.65 ms per test to ~0.05: `files 512 1|2|4|8` went 1868/3199/5898/11357 ms → 979/1014/1051/1159.
# ⭐ **ACCEPTANCE WAS BYTE-IDENTITY of the `--list` output at F = 16/64/256/512**, not a green case —
# an optimization has no red.
#
# ◑ **TWO Θ(sources × test files) TERMS MEASURED AND LEFT**, both on the run path and both invisible
# to `--list`. At S=512, T=1024, planted errors: F=512 costs 4509 ms against F=1's 1795 ms for the
# identical program. They are `TestDispatcher.groupOfFile` (asked once per staged source) and
# `Queries.compilerMintedNamesIn` (a linear scan of `Project.compilerWrittenSources`, asked once per
# file per parse pass — the list is empty for every build but this one, which is why an ordinary
# compile pays one `isEmpty`). Neither is worth a map today: at F=64 a whole green run is 2181 ms of
# which 1203 is the compile, and the crossover is in the hundreds of test files.
#
# ⚠ A THIRD term is NOT here and must not be looked for: `Diagnostics.authoredPathFor` is
# Θ(diagnostics × staged sources), and holding S=512 while sweeping the diagnostic count 512 → 1 read
# 4509 ms → 4533 ms. It is inside the noise, because it fires only on a failing compile.
#
# Usage:  gentestproject.sh <files|onefile|broken> <n> <m> <outdir>
#   e.g.  gentestproject.sh files   512 8    /tmp/lad512
#         gentestproject.sh onefile 512 4096 /tmp/ctl512     # the control for the line above
#         ./maxon-shv2/.maxon/maxon-shv2 test /tmp/lad512 --list --color=never
#
# The tests all PASS and the program is cheap to run, but the ladder is about the HARNESS's cost:
# prefer `--list` (compiles nothing) to isolate discovery and grouping, and a full run to reach
# staging, the compile and the per-file process launches.
set -euo pipefail
MODE="$1"; N="$2"; M="$3"; OUT="$4"

case "$MODE" in
  files|onefile|broken) ;;
  *) echo "gentestproject.sh: mode must be files, onefile or broken (got '$MODE')" >&2; exit 2 ;;
esac
if [ "$N" -lt 1 ] || [ "$M" -lt 1 ]; then echo "gentestproject.sh: both counts must be >= 1" >&2; exit 2; fi

rm -rf "$OUT"
mkdir -p "$OUT"

# One helper plus `count` tests against it. `broken` declares the helper over a bare `int`, which is
# E3005 — one diagnostic per file, raised during the sweep, so the compile aborts before codegen.
emit_test_file() {
  local idx="$1" count="$2" broken="$3" path="$4"
  {
    if [ "$broken" = "yes" ]; then
      echo "function helper${idx}(x int) returns int"
    else
      echo "typealias Val${idx} = int(0 to 100000000)"
      echo "function helper${idx}(x Val${idx}) returns Val${idx}"
    fi
    printf '\treturn x + %s\n' "$idx"
    echo "end 'helper${idx}'"
    echo ""
    local t=0
    while [ "$t" -lt "$count" ]; do
      echo "test 'module ${idx} case ${t} behaves'"
      printf '\ttry Expect.equal(helper%s(%s), expected: %s)\n' "$idx" "$t" "$(( t + idx ))"
      echo "end 'module ${idx} case ${t} behaves'"
      echo ""
      t=$(( t + 1 ))
    done
  } > "$path"
}

# An ordinary source — no `test` in it, so it is compiled WITH the tests, staged like them, and
# grouped by nothing. What separates "how many sources does this project have" from "how many of them
# declare tests".
emit_ordinary_file() {
  local idx="$1" path="$2"
  {
    echo "typealias Ord${idx} = int(0 to 100000000)"
    echo "function ordinary${idx}(x Ord${idx}) returns Ord${idx}"
    printf '\treturn x + %s\n' "$idx"
    echo "end 'ordinary${idx}'"
  } > "$path"
}

case "$MODE" in
  files|broken)
    broken="no"
    if [ "$MODE" = "broken" ]; then broken="yes"; fi
    f=0
    while [ "$f" -lt "$N" ]; do
      emit_test_file "$f" "$M" "$broken" "$OUT/mod${f}.test.maxon"
      f=$(( f + 1 ))
    done
    ;;
  onefile)
    # `N` is the SOURCE count here and `M` the whole run's test count, so the pair is matched against
    # a `files` rung by S = N and T = F × testsPerFile.
    f=1
    while [ "$f" -lt "$N" ]; do
      emit_ordinary_file "$f" "$OUT/ord${f}.maxon"
      f=$(( f + 1 ))
    done
    emit_test_file 0 "$M" "no" "$OUT/solo.test.maxon"
    ;;
esac
