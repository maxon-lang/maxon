#!/usr/bin/env bash
#
# The multi-core validation harness (x64-windows).
#
# Compiles three programs, then runs four checks that together prove the emitted per-P sharded
# lock-free allocator and green-thread scheduler are correct under more than one live P.
#
# ⭐ TWO OF THE FOUR NEED A PROGRAM THAT REACHES A WORKER M, WHICH IS WHY THREE ARE BUILT. An `async`
# frame is a COROUTINE of the green thread that called it — published to that thread's own queue,
# never to a P ring — so `alloc-torture` runs entirely on one M and can witness neither a second
# worker nor a cross-P free. `service-torture` and `service-fanin-torture` `spawn` real green threads
# and move tens of thousands of heap `String`s between Ms, which IS the remote-free push.
#
# ⇒ checks 1 and 3 span both kinds; checks 2 and 4 belong to the service programs alone.
#
# The only knob is MAXON_MAX_PROCS (the scheduler's P clamp; =1 forces a single M).
#
# Exits 0 iff every check passes; non-zero (and prints FAIL) otherwise.

set -u

# shellcheck source=scripts/multicore-stress/lib.sh
. "$(cd "$(dirname "$0")" && pwd)/lib.sh"

WORK="$MULTICORE_HERE/.work"

ALLOC=alloc-torture
SERVICE=service-torture
FANIN=service-fanin-torture

# How many times each core-count config is run in the determinism sweep. >1 both proves determinism
# and re-exercises the multi-M spawn path, whose crash class is worker-count correlated.
REPS="${REPS:-15}"

# ⭐ MEASURED, NOT INHERITED (2026-09-09, 16-processor x64-windows box, 10 runs per cell). At
# MAXON_MAX_PROCS=1 both service programs read `remoteFrees` of exactly 0, 10 of 10 — neither does any
# IO, so neither starts a P-less OS thread whose frees would take the remote arm. At 2 and above the
# smallest reading was 72,016 (`service-fanin-torture` at 2); `service-torture` read ~240,000
# throughout. The bar sits an order of magnitude under the smallest real reading.
#
# ⚠ A PROGRAM THAT DOES IO WOULD READ A SMALL NON-ZERO FLOOR HONESTLY, so adding one here means
# re-measuring this pair rather than loosening them.
REMOTE_MIN=5000
FLOOR_MAX=0

LEAK_EXIT=101   # the runtime's leak-check gate exit code

FAILED=0
pass() { echo "  PASS: $1"; }
bad()  { echo "  FAIL: $1"; FAILED=1; }
hdr()  { echo; echo "== $1 =="; }

# ----------------------------------------------------------------------------
# Compile. The service programs pick up the worker-arrival prelude through
# `build_program`; `alloc-torture` does not call it and does not get it.
# ----------------------------------------------------------------------------
mkdir -p "$WORK"
rm -f "$WORK"/*torture"$MAXON_EXE_EXT" "$WORK"/*-ds"$MAXON_EXE_EXT" "$WORK"/*.mxdbg

echo "compiler: $MAXON"

build_or_die() {
	local prog="$1" out="$2"
	shift 2

	if ! build_program "$prog" "$out" "$@" >"$WORK/build.log" 2>&1; then
		echo "FAIL: $prog did not build"
		cat "$WORK/build.log"
		exit 1
	fi
}

build_or_die "$ALLOC" "$WORK/$ALLOC"
build_or_die "$ALLOC" "$WORK/$ALLOC-ds" --debugstream
build_or_die "$SERVICE" "$WORK/$SERVICE"
build_or_die "$FANIN" "$WORK/$FANIN"
echo "built:    $ALLOC (plain + --debugstream), $SERVICE, $FANIN"

BIN_ALLOC="$WORK/$ALLOC$MAXON_EXE_EXT"
BIN_DS="$WORK/$ALLOC-ds$MAXON_EXE_EXT"
BIN_SERVICE="$WORK/$SERVICE$MAXON_EXE_EXT"
BIN_FANIN="$WORK/$FANIN$MAXON_EXE_EXT"

# ----------------------------------------------------------------------------
# Run one program at one processor count, capturing every field it prints.
#   run_program <binary> <procs|"">
# `env -u` rather than "just don't prefix it": the unset case must be measurable
# even when this script's own caller has MAXON_MAX_PROCS in the environment.
# ----------------------------------------------------------------------------
run_program() {
	if [ -n "$2" ]; then
		MAXON_MAX_PROCS="$2" "$1" >"$WORK/out.txt" 2>"$WORK/err.txt"
	else
		env -u MAXON_MAX_PROCS "$1" >"$WORK/out.txt" 2>"$WORK/err.txt"
	fi
	RC=$?
	AGG="$(grep '^aggregate=' "$WORK/out.txt")"
	WK="$(number_field "$WORK/out.txt" workers)"
	CPU="$(number_field "$WORK/out.txt" cpucount)"
	PROCS="$(number_field "$WORK/out.txt" procs)"
	RF="$(number_field "$WORK/out.txt" remoteFrees)"
	WAIT="$(word_field "$WORK/out.txt" workerwait)"
}

# Detect ncpu from an unclamped run's printed cpucount.
run_program "$BIN_ALLOC" ""
NCPU="$CPU"
: "${NCPU:=1}"
echo "cpucount: $NCPU"

# The sweep: {1, 2, 7, ncpu}, clamping 7->ncpu when ncpu<7, de-duplicated in order.
RAW_PROCS=(1 2 $(( NCPU < 7 ? NCPU : 7 )) "$NCPU")
PROCS_SWEEP=()
for p in "${RAW_PROCS[@]}"; do
	seen=0
	for q in "${PROCS_SWEEP[@]:-}"; do [ "$q" = "$p" ] && seen=1; done
	[ "$seen" = 0 ] && PROCS_SWEEP+=("$p")
done

# ============================================================================
# CHECK 1 — Determinism / byte-identity across core counts (+ stability).
# ============================================================================
hdr "Check 1: determinism across MAXON_MAX_PROCS in {${PROCS_SWEEP[*]}} (x$REPS each)"

# Each result is an order-independent function of its inputs, so a difference between two core counts
# is work that ran twice, not at all, or on top of other work.
#
# ⭐ BOTH KINDS OF PROGRAM ARE SWEPT. A sweep of `alloc-torture` alone proves determinism of
# SINGLE-THREADED execution across processor counts, which is a much weaker statement than it reads
# as; `service-torture` is the one whose work actually crosses machines.
sweep_determinism() {
	local binary="$1" label="$2" ref_agg ref_rc ok=1 p i

	run_program "$binary" 1
	ref_agg="$AGG"; ref_rc="$RC"
	echo "  $label serial(1): $ref_agg exit=$ref_rc"

	for p in "${PROCS_SWEEP[@]}"; do
		i=0
		while [ "$i" -lt "$REPS" ]; do
			run_program "$binary" "$p"

			if [ "$AGG" != "$ref_agg" ]; then
				bad "$label procs=$p produced [$AGG] != serial [$ref_agg]"; ok=0; break
			fi
			if [ "$RC" != "$ref_rc" ]; then
				bad "$label procs=$p exit=$RC != serial exit=$ref_rc"; ok=0; break
			fi
			# Free observations: many more chances to catch a starved box than check 2 alone gets.
			if [ "$WAIT" = timeout ]; then
				bad "$label procs=$p read workerwait=timeout — no second worker M arrived within the budget"; ok=0; break
			fi

			i=$((i+1))
		done
		[ "$ok" = 1 ] && echo "  $label procs=$p : $REPS runs all [$ref_agg] exit=$ref_rc"
	done

	[ "$ok" = 1 ] && pass "$label: aggregate + exit code byte-identical across all core counts and equal to serial"
}

sweep_determinism "$BIN_ALLOC" "$ALLOC"
sweep_determinism "$BIN_SERVICE" "$SERVICE"

# ============================================================================
# CHECK 2 — A second worker actually ran (multi-core, not cooperative single-M).
# ============================================================================
hdr "Check 2: a spawned green thread reaches a worker M"

# ⭐ BLOCKED ON, NOT SAMPLED. The program waits for `schedMaxActiveWorkers() >= 2` with a bounded
# budget and reports `workerwait`, so a box too busy to start the M in time is a NAMED timeout rather
# than a reading of 1 that looks exactly like the coroutine pin.
run_program "$BIN_SERVICE" ""; U_WK="$WK"; U_PROCS="$PROCS"; U_WAIT="$WAIT"
run_program "$BIN_SERVICE" 1;  S_WK="$WK"; S_WAIT="$WAIT"
echo "  unclamped: procs=$U_PROCS workers=$U_WK workerwait=$U_WAIT"
echo "  procs=1:   workers=$S_WK workerwait=$S_WAIT"

if [ "$U_WAIT" = timeout ]; then
	bad "unclamped workerwait=timeout — the wait expired before a second worker M arrived"
elif [ "${U_PROCS:-1}" -lt 2 ]; then
	pass "this host resolves to $U_PROCS processor: workers=$U_WK is the correct reading and no second M can exist"
elif [ "${U_WK:-0}" -ge 2 ]; then
	pass "unclamped schedMaxActiveWorkers=$U_WK over $U_PROCS processors (a spawned green thread reached a worker M)"
else
	bad "unclamped workers=$U_WK with workerwait=ok — the wait observed a second worker and the counter denies it"
fi

if [ "${S_WK:-0}" -eq 1 ] && [ "$S_WAIT" = ok ]; then
	pass "MAXON_MAX_PROCS=1 workers=1, workerwait=ok (the clamp forces a single M and the wait short-circuits)"
elif [ "$S_WAIT" != ok ]; then
	bad "MAXON_MAX_PROCS=1 workerwait=$S_WAIT — the one-processor arm must not wait at all"
else
	bad "MAXON_MAX_PROCS=1 workers=$S_WK (expected 1)"
fi

# ============================================================================
# CHECK 3 — Leak-clean (exit-101 gate) + balanced mm-trace under the monitor.
# ============================================================================
hdr "Check 3: leak-clean + balanced mm-trace"

# 3a: no run of any program exits 101, the exact runtime leak oracle. The fan-in program earns its
# place here specifically: 48,000 heap `String`s are built by twelve green threads and dropped by a
# thirteenth, and a single lost decref is the only thing it reports.
leak_seen=0
for binary in "$BIN_ALLOC" "$BIN_SERVICE" "$BIN_FANIN"; do
	for p in "" "${PROCS_SWEEP[@]}"; do
		run_program "$binary" "$p"
		if [ "$RC" = "$LEAK_EXIT" ]; then
			bad "$(basename "$binary") procs=${p:-unclamped} exited $LEAK_EXIT (memory leak)"
			leak_seen=1
		fi
	done
done
[ "$leak_seen" = 0 ] && pass "no run exited $LEAK_EXIT across three programs and every core count"

# 3b: monitor a small single-P run and confirm alloc==free with nothing dropped.
#
# ⛔ THIS HALF BELONGS TO `alloc-torture` AND CANNOT MOVE TO A SERVICE PROGRAM. It asserts
# `dropped == 0`, which needs a workload small enough to fit the debugstream ring — and
# `alloc-torture` is the only program here that takes a small-workload argument. It runs at one
# processor for the same reason: a multi-M run floods the ring.
MAXON_MAX_PROCS=1 "$MAXON" monitor --filter=mm "$BIN_DS" small >"$WORK/mon.txt" 2>&1
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
# CHECK 4 — The cross-P remote-free road is TAKEN, not merely built.
# ============================================================================
hdr "Check 4: cross-P remote frees"

# `__Builtins.slabRemoteFreeCount()` sums one per-P word over `__sched_procs`, credited at every
# remote push. A record allocated on the M that built it and released on whichever M ran its receiver
# is exactly that push, so a service program reads its own cross-P traffic.
check_remote_free() {
	local binary="$1" label="$2" unclamped serial procs

	run_program "$binary" ""; unclamped="$RF"; procs="$PROCS"
	run_program "$binary" 1;  serial="$RF"
	echo "  $label: unclamped=$unclamped procs=1=$serial (over $procs processors)"

	if [ "$unclamped" = "-" ] || [ "$serial" = "-" ]; then
		bad "$label printed no remoteFrees= field"
		return
	fi

	if [ "${procs:-1}" -lt 2 ]; then
		echo "  NOTE: $label — one processor resolved, so there is no cross-P traffic to require."
		return
	fi

	if [ "$unclamped" -ge "$REMOTE_MIN" ] && [ "$unclamped" -gt "$serial" ]; then
		pass "$label remoteFrees=$unclamped (>=$REMOTE_MIN and >> single-P $serial) — the MPSC path is exercised"
	else
		bad "$label remoteFrees=$unclamped (expected >=$REMOTE_MIN and > single-P $serial)"
	fi

	if [ "$serial" -le "$FLOOR_MAX" ]; then
		pass "$label MAXON_MAX_PROCS=1 remoteFrees=$serial (at/under $FLOOR_MAX — no worker cross-P frees)"
	else
		bad "$label MAXON_MAX_PROCS=1 remoteFrees=$serial (> $FLOOR_MAX — cross-P traffic with one processor)"
	fi
}

check_remote_free "$BIN_SERVICE" "$SERVICE"
check_remote_free "$BIN_FANIN" "$FANIN"

# ----------------------------------------------------------------------------
echo
if [ "$FAILED" = 0 ]; then
	echo "ALL CHECKS PASSED"
	exit 0
else
	echo "ONE OR MORE CHECKS FAILED"
	exit 1
fi
