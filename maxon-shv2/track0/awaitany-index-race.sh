#!/usr/bin/env bash
#
# Track-0 AWAIT-ANY INDEX race driver — runs `awaitany-index-torture.maxon`
# REPS times at each MAXON_MAX_PROCS and tabulates the SELECT LATENCY the
# program encodes in its exit status.
#
# ⭐⭐ THE EXIT CODE IS THE WHOLE READING, AND THAT IS MEASURED RATHER THAN
# ASSUMED — the program's header carries the measurement (a `print`-instrumented
# build read 150/150 clean against 13/160 through the exit code on the same
# tree). Nothing here parses stdout, because the program writes none.
#
#   exit 150 + min(worstMs, 99)    the worst SELECT LATENCY of the run, in ms
#   exit 250                       ⇒ a round answered the WRONG INDEX, and ONLY
#                                  that — the latency scale stops at 249 so the
#                                  two verdicts can never be the same number
#   exit 60                        a reply decoded wrong (the program, not the
#                                  scheduler)
#
# ⇒ a run PASSES when `exit - 150 <= LATE_MS` (default 10). A clean run reads
# 150-152; a driver that slept through the reply reads ~175, or 250 if it also
# answered the losing slot.
#
# ⛔ THE BAR IS `procs=16`, AND THE COUNT MATTERS. The window is main's driver
# reaching its netpoll `sleepwait` arm — nothing runnable, `Slow`'s timer
# pending — before `Quick`'s reply lands, which needs worker Ms to have taken
# both handlers off main. MEASURED on the UNFIXED tree, 10 runs: 9 red at 16
# (six of them the wrong INDEX) against 0 red at 1. A driver pointed at one
# processor measures nothing at all.
#
# Usage:  awaitany-index-race.sh [reps]       (default 50)
#         MAXON=<path to shv2 binary>         to drive a staged build
#         PROCS_LIST="1 4 16"                 to change the processor counts run
#         GATED_PROCS="16"                    to change which of them decide the
#                                             exit status
#         LATE_MS=10                          to change the pass threshold
#
# ⛔ IT ASSERTS, WHICH IS THE OPPOSITE OF `refcount-race.sh` AND IS DELIBERATE.
# That script RECORDS, because what a given tree should read there depends on
# whether `async` is pinned and whether the refcount step is atomic — genuinely
# the reader's question. Here there is one right answer at every processor count
# and every tree: the select must come back with the index that FINISHED, within
# a millisecond. So this exits non-zero when a gated row is not clean, and it is
# a gate the battery can run. (This paragraph replaced a verbatim copy of
# `refcount-race.sh`'s "records and does not assert" preamble, which contradicted
# this script's own exit status.)

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

# ⛔ THE SHV2 BINARY, NOT `bin/maxon.exe` — the defect is in shv2's own emitted
# green-thread runtime, and the bootstrap's is a different scheduler entirely.
# ⚠ The fallback STRIPS `.exe`, it does not add one: the default already ends in it,
# and the non-Windows binary has no extension. `refcount-race.sh` carries the incident.
MAXON="${MAXON:-$REPO/maxon-shv2/.maxon/maxon-shv2.exe}"
[ -x "$MAXON" ] || MAXON="${MAXON%.exe}"

REPS="${1:-50}"

# ⭐ THE GATE IS `procs=16` AND THE LOWER COUNTS ARE CONTEXT. `GATED_PROCS` is what
# decides the exit status; `PROCS_LIST` is what gets run. N=1 is in the list because a
# clean row there is the CONTROL that says the defect needs a second M — and it is out
# of the gate for exactly the same reason: a count that cannot reproduce the bug cannot
# witness its absence either, so failing the run on it would be asserting the harness.
PROCS_LIST="${PROCS_LIST:-1 4 16}"
GATED_PROCS="${GATED_PROCS:-16}"
LATE_MS="${LATE_MS:-10}"

EXIT_BASE=150
WRONG_INDEX_EXIT=250
DECODE_FAILURE_EXIT=60

# Beside the script, never under `temp/` — a bootstrap `spec-test` run deletes
# every `*.exe` under `temp/` recursively, which is exactly how a staged
# measurement gets lost.
WORK="$HERE/.awaitany-race"

if [ ! -x "$MAXON" ]; then
	echo "FAIL: no shv2 binary at $MAXON (build it, or set MAXON=)"
	exit 1
fi

mkdir -p "$WORK"
echo "compiler:  $MAXON"
echo "reps:      $REPS per processor count"
echo "late when: worst select latency > ${LATE_MS} ms"
echo "gate:      procs $GATED_PROCS (other rows are recorded, not asserted)"
echo

if ! "$MAXON" build "$HERE/awaitany-index-torture.maxon" -o "$WORK/awaitany-index-torture" >"$WORK/build.log" 2>&1; then
	echo "FAIL: awaitany-index-torture did not build"
	cat "$WORK/build.log"
	exit 1
fi

: >"$WORK/runs.log"

printf '%6s %7s %7s %11s %10s %9s %6s\n' procs clean late wrongindex worstMs other gated
printf '%6s %7s %7s %11s %10s %9s %6s\n' ----- ----- ---- ---------- ------ ----- -----

status=0

for N in $PROCS_LIST; do
	clean=0; late=0; wrong=0; other=0; worst=0
	i=0
	while [ "$i" -lt "$REPS" ]; do
		MAXON_MAX_PROCS=$N "$WORK/awaitany-index-torture" >/dev/null 2>&1
		rc=$?
		verdict="?"
		if [ "$rc" -eq "$WRONG_INDEX_EXIT" ]; then
			wrong=$((wrong+1)); verdict="WRONG-INDEX"
		elif [ "$rc" -ge "$EXIT_BASE" ] && [ "$rc" -lt "$WRONG_INDEX_EXIT" ]; then
			ms=$((rc - EXIT_BASE))
			[ "$ms" -gt "$worst" ] && worst=$ms
			if [ "$ms" -gt "$LATE_MS" ]; then
				late=$((late+1));   verdict="late ${ms}ms"
			else
				clean=$((clean+1)); verdict="clean ${ms}ms"
			fi
		elif [ "$rc" -eq "$DECODE_FAILURE_EXIT" ]; then
			other=$((other+1)); verdict="DECODE-FAILURE"
		else
			other=$((other+1)); verdict="UNEXPECTED"
		fi
		echo "N=$N run=$((i+1)) exit=$rc $verdict" >>"$WORK/runs.log"
		i=$((i+1))
	done
	gated="-"
	case " $GATED_PROCS " in *" $N "*) gated="gate";; esac
	printf '%6s %7s %7s %11s %10s %9s %6s\n' "$N" "$clean" "$late" "$wrong" "$worst" "$other" "$gated"
	if [ "$gated" = "gate" ] && [ "$((late + wrong + other))" -ne 0 ]; then
		status=1
	fi
done

echo
echo "per-run log: $WORK/runs.log"

if [ "$status" -eq 0 ]; then
	echo "ALL GATED ROWS CLEAN (gate: procs $GATED_PROCS)"
else
	echo "FAILURES ON A GATED ROW (see the table)"
fi

exit "$status"
