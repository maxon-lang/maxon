#!/usr/bin/env bash
#
# DebugStream commit-bit validation harness.
#
# THE RACE IT GATES. `__ds_reserve` publishes an entry (header written, write_cursor advanced, ring
# lock released) and the CALLER writes the payload afterwards. The monitor, a separate process, can
# therefore see an entry whose payload has not been written and decode stale ring bytes as event
# data. The commit bit closes that window. These checks prove it — and, run against the unfixed
# compiler, they FAIL (torn LOG_TEXT tails), which is what makes them worth anything.
#
# CHECK 1 — the self-verifying concurrent stress. 12 green threads on real OS workers, emitting two
#           interleaved streams whose payloads are checksums of themselves. Every decoded entry must
#           satisfy its own invariant; none may be lost, duplicated, or dropped.
#
#           It is run across a BAND of producer pacings, not one. The bug is only reachable while
#           the monitor is caught up but never idle (see the operating-point note in ds-race.maxon),
#           and where that sits depends on how fast this machine is relative to the monitor. One
#           pacing would be a bet; the band is a sweep.
#
# CHECK 2 — the monitor terminates when the producer is KILLED MID-ENTRY, and REPORTS the abandoned
#           entry rather than swallowing it. Without the commit bit's counterpart in the drain loop,
#           a producer killed between reserve and commit leaves readCursor < writeCursor forever and
#           the monitor spins on it.
#
# Exits 0 iff every check passes.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
MAXON="${MAXON:-$REPO/bin/maxon.exe}"
SRC="$HERE/ds-race.maxon"
WORK="$HERE/.work"
DS="$WORK/ds-race.exe"

# Must match the constants at the top of ds-race.maxon. The verifier reconstructs (idx, seq) from
# the payload with them, so a mismatch here is a broken TEST, not a broken compiler.
THREADS=12
TEXTS_PER_THREAD=600
EVENTS_PER_TEXT=25
EVENTS_PER_THREAD=$((EVENTS_PER_TEXT * TEXTS_PER_THREAD))
PAD_LEN=16384
SEQ_BASE=1000000
CHECK_MUL=3
CHECK_ADD=11
LOG_CAT=7
LOG_LVL=5

# The producer-pacing band, fastest first. See CHECK 1 above: too fast and the monitor is backlogged
# (its memcpy reaches an in-flight entry only after the producer finished); too slow and the ring
# keeps going empty (the monitor sleeps 1-15 ms and never looks during the window). The bug lives in
# between, and exactly where depends on this machine — hence a band, not a number.
#
# The FAST end is deliberately fast enough to overflow the ring on some machines. That is fine and
# it is the point: a run that drops is INCONCLUSIVE about counts but still fully conclusive about
# torn payloads, and the deep-but-not-empty ring is where the torn payloads actually live.
PACINGS="${PACINGS:-60000 80000 100000 120000 160000 220000}"

# How long to let the kill-mid-entry monitor run before declaring it HUNG. The stress runs for a
# couple of seconds; a monitor still alive well after its producer was killed is spinning on an
# entry that will never be committed.
KILL_TIMEOUT=30

LEAK_EXIT=101

FAILED=0
pass() { echo "  PASS: $1"; }
bad()  { echo "  FAIL: $1"; FAILED=1; }
hdr()  { echo; echo "== $1 =="; }

mkdir -p "$WORK"
rm -f "$DS"

echo "Compiling $SRC (--debugstream) ..."
if ! "$MAXON" build --debugstream "$SRC" >"$WORK/build.log" 2>&1; then
	echo "FAIL: --debugstream build errored"; cat "$WORK/build.log"; exit 1
fi
cp "$HERE/ds-race.exe" "$DS"
echo "  built: $DS"

# ============================================================================
# CHECK 1 — self-verifying concurrent stress, swept across the pacing band.
# ============================================================================
hdr "Check 1: self-verifying concurrent stress (torn payloads, lost/duplicated events)"

conclusive=0

for pacing in $PACINGS; do
	echo
	echo "  --- pacing=$pacing ---"

	# stderr is merged in: the monitor's `[debugstream] ... N dropped ...` summary goes there, and
	# the verifier needs it to tell a DROPPED event (ring overflow) from a TORN one (the bug).
	"$MAXON" monitor --filter=log "$DS" "$pacing" >"$WORK/trace-$pacing.txt" 2>&1
	rc=$?

	if [ "$rc" = "$LEAK_EXIT" ]; then
		bad "pacing=$pacing: monitored run exited $LEAK_EXIT (memory leak under tracing)"
	fi

	awk -v threads="$THREADS" -v perThread="$EVENTS_PER_THREAD" -v texts="$TEXTS_PER_THREAD" \
	    -v seqBase="$SEQ_BASE" -v mul="$CHECK_MUL" -v add="$CHECK_ADD" \
	    -v cat="$LOG_CAT" -v lvl="$LOG_LVL" -v padLen="$PAD_LEN" \
	    -f "$HERE/verify.awk" "$WORK/trace-$pacing.txt"
	vrc=$?

	case "$vrc" in
		0)
			pass "pacing=$pacing: no torn payloads; counts exact"
			conclusive=$((conclusive + 1))
			# A clean trace is ~130 MB of 16 KB text lines and has nothing left to say. A DIRTY one
			# is the evidence, so it stays on disk.
			rm -f "$WORK/trace-$pacing.txt"
			;;
		2)
			pass "pacing=$pacing: no torn payloads (counts inconclusive — the ring overflowed)"
			rm -f "$WORK/trace-$pacing.txt"
			;;
		*)
			bad "pacing=$pacing: the trace is not self-consistent (see above)"
			echo "       evidence kept: $WORK/trace-$pacing.txt"
			;;
	esac
done

# Every pacing answered the INTEGRITY question. At least one must also have answered the
# COMPLETENESS one — otherwise the whole sweep overflowed the ring and nothing proved that events
# are neither lost nor duplicated.
echo
if [ "$conclusive" -gt 0 ]; then
	pass "$conclusive of the swept pacings ran drop-free, so the exact-count check was answered"
else
	bad "every pacing overflowed the ring — no run could answer the exact-count check"
fi

# ============================================================================
# CHECK 2 — the monitor terminates when the producer is killed mid-entry.
# ============================================================================
hdr "Check 2: producer killed mid-entry — the monitor must not hang"

# Kill the PRODUCER (the child the monitor spawned), not the monitor. Killing it while its threads
# are inside the ring leaves, with high probability, an entry that was reserved and will never be
# committed. The monitor must notice the producer is gone, report the abandoned entry, and STOP —
# the pre-commit-bit loop condition (readCursor < writeCursor) would spin on it forever.
"$MAXON" monitor --filter=log "$DS" >"$WORK/kill-trace.txt" 2>&1 &
MON_PID=$!

# The stress runs for seconds, so any moment in here is mid-flight — this is not a window we are
# trying to hit precisely.
sleep 0.6
taskkill //F //IM ds-race.exe >"$WORK/kill.log" 2>&1
killed=$?

waited=0
while kill -0 "$MON_PID" 2>/dev/null && [ "$waited" -lt "$KILL_TIMEOUT" ]; do
	sleep 1
	waited=$((waited + 1))
done

if kill -0 "$MON_PID" 2>/dev/null; then
	kill -9 "$MON_PID" 2>/dev/null
	bad "the monitor was STILL RUNNING ${KILL_TIMEOUT}s after the producer was killed — it HUNG"
else
	wait "$MON_PID"
	krc=$?
	echo "  monitor exit=$krc after ${waited}s (taskkill rc=$killed)"

	if [ "$killed" != 0 ]; then
		bad "taskkill did not find ds-race.exe — the producer had already finished, so nothing was tested"
	else
		pass "the monitor terminated after the producer was killed mid-entry"
	fi
fi

# The abandoned count is REPORTED, not swallowed. Whether the kill lands inside an entry or between
# two of them is a race, so a zero count is not a failure — but a non-zero one must be visible.
if grep -aq 'abandoned (producer died mid-entry)' "$WORK/kill-trace.txt"; then
	echo "  reported: $(grep -ao '[0-9]* abandoned (producer died mid-entry)' "$WORK/kill-trace.txt" | head -1)"
	pass "the abandoned entry was reported in the summary"
else
	echo "  (the kill landed between entries this run — nothing was abandoned)"
	grep -a '^\[debugstream\]' "$WORK/kill-trace.txt" | head -1 | sed 's/^/  /'
fi

echo
if [ "$FAILED" = 0 ]; then
	echo "ALL CHECKS PASSED"
else
	echo "SOME CHECKS FAILED"
fi
exit "$FAILED"
