#!/usr/bin/env bash
#
# Track-0 multi-core validation harness (x64-windows).
#
# Compiles alloc-torture.maxon ONCE (a plain build + a --debugstream build for
# the monitor run), then runs four checks that together prove the C#-emitted
# per-P sharded lock-free allocator + green-thread scheduler are correct under
# more than one live P. See README.md for what each check means.
#
# Knobs exercised: MAXON_MAX_PROCS (scheduler P clamp; =1 forces single-thread),
# MAXON_SLAB_STATS (dumps the [slab-stats] contention counters at exit),
# MAXON_SLAB_GLOBAL_LOCK (serialises alloc/free — the A/B bisection safety net).
#
# Exits 0 iff every check passes; non-zero (and prints FAIL) otherwise.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
MAXON="${MAXON:-$REPO/bin/maxon.exe}"
SRC="$HERE/alloc-torture.maxon"
WORK="$HERE/.work"
PLAIN="$WORK/torture.exe"
DS="$WORK/torture-ds.exe"

# How many times each core-count config is run in the determinism/stability
# sweep. >1 both proves determinism and re-exercises the multi-M spawn path
# (the intermittent crash class Track 0 is meant to catch is worker-count
# correlated, so the high-concurrency configs bear the most repetitions).
REPS="${REPS:-15}"

# Contention-counter thresholds (see check 4).
REMOTE_MIN=50   # unclamped remote_free must clear this (real worker cross-P traffic)
FLOOR_MAX=8     # single-P remote_free must stay at/under the P-less-thread floor

LEAK_EXIT=101   # the runtime's leak-check gate exit code

FAILED=0
pass() { echo "  PASS: $1"; }
bad()  { echo "  FAIL: $1"; FAILED=1; }
hdr()  { echo; echo "== $1 =="; }

# ----------------------------------------------------------------------------
# Compile once: plain + debugstream. Both land next to the source as
# alloc-torture.exe; copy each aside so we keep both.
# ----------------------------------------------------------------------------
mkdir -p "$WORK"
rm -f "$PLAIN" "$DS"

echo "Compiling $SRC (plain + --debugstream) ..."
if ! "$MAXON" build "$SRC" >"$WORK/build.log" 2>&1; then
	echo "FAIL: plain build errored"; cat "$WORK/build.log"; exit 1
fi
cp "$HERE/alloc-torture.exe" "$PLAIN"

if ! "$MAXON" build --debugstream "$SRC" >"$WORK/build-ds.log" 2>&1; then
	echo "FAIL: debugstream build errored"; cat "$WORK/build-ds.log"; exit 1
fi
cp "$HERE/alloc-torture.exe" "$DS"
echo "  built: $PLAIN  and  $DS"

# ----------------------------------------------------------------------------
# Run the plain torture in a clean env with the requested knobs, capturing
# stdout fields + the [slab-stats] line + the exit code into globals.
#   run_torture <procs|""> <stats:0|1> <glock:0|1>
# ----------------------------------------------------------------------------
run_torture() {
	local procs="$1" stats="$2" glock="$3"
	(
		[ -n "$procs" ] && export MAXON_MAX_PROCS="$procs"
		[ "$stats" = 1 ] && export MAXON_SLAB_STATS=1
		[ "$glock" = 1 ] && export MAXON_SLAB_GLOBAL_LOCK=1
		"$PLAIN"
	) >"$WORK/out.txt" 2>"$WORK/err.txt"
	RC=$?
	AGG="$(grep '^aggregate=' "$WORK/out.txt")"
	WK="$(grep -o '^workers=[0-9]*' "$WORK/out.txt" | grep -o '[0-9]*')"
	CPU="$(grep -o '^cpucount=[0-9]*' "$WORK/out.txt" | grep -o '[0-9]*')"
	STATS="$(grep 'slab-stats' "$WORK/err.txt" || true)"
	RF="$(printf '%s' "$STATS" | grep -o 'remote_free=[0-9]*' | grep -o '[0-9]*')"
	LW="$(printf '%s' "$STATS" | grep -o 'lock_wait=[0-9]*' | grep -o '[0-9]*')"
}

# Detect ncpu from an unclamped run's printed cpucount.
run_torture "" 0 0
NCPU="$CPU"
REF_AGG="$AGG"          # reference aggregate (unclamped)
: "${NCPU:=1}"
echo "  detected cpucount=$NCPU, reference $REF_AGG"

# Build the core-count sweep: {1, 2, 7, ncpu}, clamping 7->ncpu when ncpu<7,
# then de-duplicating while preserving order.
RAW_PROCS=(1 2 $(( NCPU < 7 ? NCPU : 7 )) "$NCPU")
PROCS=()
for p in "${RAW_PROCS[@]}"; do
	skip=0
	for q in "${PROCS[@]:-}"; do [ "$q" = "$p" ] && skip=1; done
	[ "$skip" = 0 ] && PROCS+=("$p")
done

# ============================================================================
# CHECK 1 — Determinism / byte-identity across core counts (+ stability).
# ============================================================================
hdr "Check 1: determinism across MAXON_MAX_PROCS in {${PROCS[*]}} (x$REPS each)"

# Serial (procs=1) reference.
run_torture 1 0 0
SERIAL_AGG="$AGG"; SERIAL_RC="$RC"
echo "  serial(1): $SERIAL_AGG exit=$SERIAL_RC"

det_ok=1
for p in "${PROCS[@]}"; do
	for _ in $(seq 1 "$REPS"); do
		run_torture "$p" 0 0
		if [ "$AGG" != "$SERIAL_AGG" ]; then
			bad "procs=$p produced '$AGG' != serial '$SERIAL_AGG'"; det_ok=0; break
		fi
		if [ "$RC" != "$SERIAL_RC" ]; then
			bad "procs=$p exit=$RC != serial exit=$SERIAL_RC"; det_ok=0; break
		fi
	done
	[ "$det_ok" = 1 ] && echo "  procs=$p : $REPS runs all '$SERIAL_AGG' exit=$SERIAL_RC"
done
[ "$det_ok" = 1 ] && pass "aggregate + exit code byte-identical across all core counts and equal to serial"

# ============================================================================
# CHECK 2 — A second worker actually ran (multi-core, not cooperative single-M).
# ============================================================================
hdr "Check 2: >=2 workers unclamped, exactly 1 under MAXON_MAX_PROCS=1"

run_torture "" 0 0; UNCLAMP_WK="$WK"
run_torture 1 0 0;  SERIAL_WK="$WK"
echo "  workers: unclamped=$UNCLAMP_WK  procs=1=$SERIAL_WK"

if [ "${UNCLAMP_WK:-0}" -ge 2 ]; then
	pass "unclamped schedMaxActiveWorkers=$UNCLAMP_WK (>=2 distinct worker Ms ran)"
else
	bad "unclamped schedMaxActiveWorkers=$UNCLAMP_WK (<2 — no second worker observed)"
fi
if [ "${SERIAL_WK:-0}" -eq 1 ]; then
	pass "MAXON_MAX_PROCS=1 schedMaxActiveWorkers=1 (clamp forces single M)"
else
	bad "MAXON_MAX_PROCS=1 schedMaxActiveWorkers=$SERIAL_WK (expected 1)"
fi

# ============================================================================
# CHECK 3 — Leak-clean (exit-101 gate) + balanced mm-trace under the monitor.
# ============================================================================
hdr "Check 3: leak-clean + balanced mm-trace"

# 3a: no run in the whole sweep exited 101 (the exact runtime leak oracle).
leak_seen=0
for p in "" "${PROCS[@]}"; do
	run_torture "$p" 0 0
	[ "$RC" = "$LEAK_EXIT" ] && { bad "procs=${p:-unclamped} exited $LEAK_EXIT (memory leak)"; leak_seen=1; }
done
[ "$leak_seen" = 0 ] && pass "no run exited $LEAK_EXIT (leak-check gate clean across all core counts)"

# 3b: monitor a small (drop-free) single-P run and confirm alloc==free.
MAXON_MAX_PROCS=1 "$MAXON" monitor --filter=mm "$DS" small >"$WORK/mon.txt" 2>&1
MRC=$?
MALLOC="$(grep -c 'mm_alloc ' "$WORK/mon.txt")"
MFREE="$(grep -c 'mm_free ' "$WORK/mon.txt")"
MDROP="$(grep -o '[0-9]* dropped' "$WORK/mon.txt" | grep -o '^[0-9]*' | head -1)"
: "${MDROP:=0}"
echo "  monitor: exit=$MRC  mm_alloc=$MALLOC  mm_free=$MFREE  dropped=$MDROP"

if [ "$MRC" = "$LEAK_EXIT" ]; then
	bad "monitored run exited $LEAK_EXIT (leak under tracing)"
elif [ "${MALLOC:-0}" -gt 0 ] && [ "$MALLOC" = "$MFREE" ] && [ "$MDROP" = 0 ]; then
	pass "mm-trace balanced: $MALLOC allocs == $MFREE frees, 0 events dropped, exit=$MRC"
else
	bad "mm-trace not balanced/complete (alloc=$MALLOC free=$MFREE dropped=$MDROP exit=$MRC)"
fi

# ============================================================================
# CHECK 4 — Remote-free path exercised + global-lock A/B parity.
# ============================================================================
hdr "Check 4: cross-P remote frees + global-lock A/B parity"

run_torture "" 1 0; UNCLAMP_RF="$RF"; UNCLAMP_AGG="$AGG"; UNCLAMP_RC="$RC"
run_torture 1 1 0;  SERIAL_RF="$RF"
: "${UNCLAMP_RF:=0}"; : "${SERIAL_RF:=0}"
echo "  remote_free: unclamped=$UNCLAMP_RF  procs=1=$SERIAL_RF  (single-P floor = P-less aux-thread frees)"

# 4a: unclamped remote_free is large (worker cross-P traffic) and dwarfs the
# single-P aux-thread floor; single-P has no genuine worker cross-P frees.
if [ "$UNCLAMP_RF" -ge "$REMOTE_MIN" ] && [ "$UNCLAMP_RF" -gt "$SERIAL_RF" ]; then
	pass "unclamped remote_free=$UNCLAMP_RF (>=$REMOTE_MIN, >> single-P) — cross-P MPSC path exercised"
else
	bad "unclamped remote_free=$UNCLAMP_RF (expected >=$REMOTE_MIN and > single-P=$SERIAL_RF)"
fi
if [ "$SERIAL_RF" -le "$FLOOR_MAX" ]; then
	pass "MAXON_MAX_PROCS=1 remote_free=$SERIAL_RF (at/under aux-thread floor $FLOOR_MAX — no worker cross-P frees)"
else
	bad "MAXON_MAX_PROCS=1 remote_free=$SERIAL_RF (> floor $FLOOR_MAX — unexpected cross-P traffic single-threaded)"
fi

# 4b: global-lock (serialised) run must agree with the lock-free run and be
# leak-clean — bisection: the lock-free path is validated because locked and
# unlocked agree on the deterministic result.
run_torture "" 1 1; GLOCK_RF="$RF"; GLOCK_AGG="$AGG"; GLOCK_RC="$RC"; GLOCK_LW="$LW"
: "${GLOCK_LW:=0}"
echo "  global-lock: $GLOCK_AGG exit=$GLOCK_RC lock_wait=$GLOCK_LW remote_free=$GLOCK_RF"

if [ "$GLOCK_AGG" = "$UNCLAMP_AGG" ] && [ "$GLOCK_RC" = "$UNCLAMP_RC" ] && [ "$GLOCK_RC" != "$LEAK_EXIT" ]; then
	pass "MAXON_SLAB_GLOBAL_LOCK=1 matches lock-free ($GLOCK_AGG exit=$GLOCK_RC) and is leak-clean"
else
	bad "global-lock run diverged (agg=$GLOCK_AGG vs $UNCLAMP_AGG, exit=$GLOCK_RC vs $UNCLAMP_RC)"
fi
if [ "$GLOCK_LW" -gt 0 ]; then
	pass "global lock observed real contention (lock_wait=$GLOCK_LW spins)"
else
	echo "  NOTE: global-lock lock_wait=0 (no contention observed this run; not a failure)"
fi

# ----------------------------------------------------------------------------
echo
if [ "$FAILED" = 0 ]; then
	echo "ALL CHECKS PASSED"
	exit 0
else
	echo "ONE OR MORE CHECKS FAILED"
	exit 1
fi
