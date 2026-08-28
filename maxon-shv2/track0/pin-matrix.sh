#!/usr/bin/env bash
#
# Track-0 PIN MATRIX (EC10) — the positive control for "an `async` frame is a
# COROUTINE of its green thread", driven by the SHV2 compiler.
#
# ⛔ IT IS NOT `validate.sh`, AND IT IS NOT A RENAME OF IT. `validate.sh` drives
# `$REPO/bin/maxon.exe` — the C# BOOTSTRAP — and one of its checks calls
# `maxon monitor`, which shv2 does not have. So it measures the bootstrap's
# scheduler and allocator and says nothing whatever about shv2's. W212 drove the
# torture programs under shv2 BY HAND ("240 runs at 1/2/7/12") and left no script;
# this is that script, so the next person re-measures rather than re-invents.
#
# What it asserts, per program, across MAXON_MAX_PROCS in {1, 2, 7, 12}:
#
#   1. `aggregate=` (or `leaked=`) is BYTE-IDENTICAL at every N and equal to the
#      N=1 answer. Each torture program's result is an order-independent function
#      of its inputs, so a difference is a thread having run twice, not at all, or
#      on top of another.
#   2. The exit code is identical at every N, and is never 101 (the runtime's leak
#      gate) or 139 (a segfault).
#   3. `leaked=0` from drop-running-torture at every N.
#   4. ⭐ THE PIN ITSELF: `workers=1` and `steals=0` at EVERY N.
#
# ⭐⭐ ASSERTION 4 IS WHAT THIS SCRIPT EXISTS FOR, AND IT IS THE ONE THAT FLIPPED.
# Before EC10 every `async f(...)` published a GT to the SCHEDULER, so at N >= 2 a
# worker M popped, stole and ran an `async` frame on a different M than its caller
# — MEASURED on the parent commit, same box, this script: steal-torture read
# `workers/steals` of 1/0, 2/24, 7/3997 and 8/3996 at N=1/2/7/12, and
# drop-running-torture 1/0, 2/6, 7/35 and 11/51.
# After the pin an `async` frame is a coroutine of the green thread that
# called it: it is published only to that green thread's coroutine queue, never to
# a P ring or the global queue, so nothing calls `__sched_wake_or_spawn` and no
# worker M is ever created. `workers=1 steals=0` at every N IS the pin, read
# directly off the runtime's own counters rather than inferred from timing.
#
# ⭐ AND THE PIN IS WHAT PRODUCES THOSE NUMBERS, WHICH IS A SEPARATE MEASUREMENT.
# Sabotaged — `__gt_spawn`'s owner stamp changed to `gt` itself on the FINISHED
# tree, so every `async` frame is a GREEN THREAD again; one line, nothing else
# changed — drop-running-torture reads 1/0, 2/6, 7/42 and 11/43 and steal-torture
# 1/0, 2/52, 7/3970 and 7/3985, with byte-identical aggregates throughout
# (MEASURED at SV1 wave 1; the pre-EC10 spelling of the same sabotage read
# 2/242, 7/393 and 12/410). A gate nobody has seen move is not a gate.
#
# ⚠ THE DAY `spawn` LANDS, ASSERTION 4 FLIPS BACK AND MUST. A `spawn` primitive
# (SERVICES_DESIGN.md §"Ownership — the spine", "Send is a MOVE") creates REAL green threads, which
# are exactly what W212's ring, its stealing and its worker loop schedule — all
# still built, all still here. That rung re-pins this assertion to `workers >= 2`
# and `steals > 0` for a spawn-driven program, and keeps it at 1/0 for a
# coroutine-only one.
#
# ⚠ refcount-torture IS IN THE LIST FOR ITS workers/steals ROW ONLY. Its own
# subject — whether a contended refcount word survives — is INTERMITTENT, so a
# single run per processor count cannot see it and this script never claims to:
# `refcount-race.sh` repeats it and tabulates exit codes, which is the reading
# that discriminates. What it contributes here is one more program whose spawn
# path is asserted to reach no worker M.
#
# ⚠ AND THAT IS ALSO WHAT alloc-torture AND remote-free-torture NOW COST. Both
# exist to drive the sharded allocator's CROSS-P paths, and both do it by getting
# worker Ms to run their tasks. With no worker M they still run every task and
# still prove determinism and leak-freedom — on ONE M. Their multi-M subject has
# no producer until `spawn`, and no reading below should be read as covering it.
#
# Exits 0 iff every assertion holds; non-zero (and prints FAIL) otherwise.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

# ⛔ THE SHV2 BINARY, NOT `bin/maxon.exe`. Overridable so the same script can take
# the PARENT reading from a binary staged elsewhere — which is how the delta above
# was measured, and the only way it can be.
MAXON="${MAXON:-$REPO/maxon-shv2/.maxon/maxon-shv2.exe}"
[ -x "$MAXON" ] || MAXON="$MAXON.exe"

WORK="$HERE/.pin-matrix"
PROCS_LIST="${PROCS_LIST:-1 2 7 12}"
PROGRAMS="${PROGRAMS:-steal-torture drop-running-torture alloc-torture remote-free-torture refcount-torture}"

LEAK_EXIT=101
SEGV_EXIT=139

FAILED=0
pass() { echo "  PASS: $1"; }
bad()  { echo "  FAIL: $1"; FAILED=1; }

if [ ! -x "$MAXON" ]; then
	echo "FAIL: no shv2 binary at $MAXON (build it, or set MAXON=)"
	exit 1
fi

mkdir -p "$WORK"
echo "compiler: $MAXON"
echo "procs:    $PROCS_LIST"
echo

# ----------------------------------------------------------------------------
# Compile each program once, into $WORK. `maxon build <src> -o <out>` keeps the
# binaries out of the source directory, so a second run of this script cannot
# measure a stale one it forgot to overwrite.
# ----------------------------------------------------------------------------
for prog in $PROGRAMS; do
	if ! "$MAXON" build "$HERE/$prog.maxon" -o "$WORK/$prog" >"$WORK/$prog.build.log" 2>&1; then
		echo "FAIL: $prog did not build"
		cat "$WORK/$prog.build.log"
		exit 1
	fi
done
echo "built: $PROGRAMS"
echo

# Read one field off a program's stdout, or "-" when the program does not print it.
# Each torture program prints a different subset, deliberately, and a driver that
# demanded all of them would be pinning the harness rather than the runtime.
field() {
	local file="$1" name="$2" v
	v="$(grep -o "^$name=[0-9-]*" "$file" | grep -o -- '-\?[0-9]*$' | head -1)"
	printf '%s' "${v:--}"
}

printf '%-24s %5s %14s %9s %9s %6s %8s\n' program procs aggregate workers steals exit leaked
printf '%-24s %5s %14s %9s %9s %6s %8s\n' ------- ----- --------- ------- ------ ---- ------

for prog in $PROGRAMS; do
	REF_AGG=""
	REF_RC=""

	for p in $PROCS_LIST; do
		MAXON_MAX_PROCS="$p" "$WORK/$prog" >"$WORK/$prog.$p.out" 2>"$WORK/$prog.$p.err"
		rc=$?

		agg="$(field "$WORK/$prog.$p.out" aggregate)"
		wk="$(field "$WORK/$prog.$p.out" workers)"
		st="$(field "$WORK/$prog.$p.out" steals)"
		lk="$(field "$WORK/$prog.$p.out" leaked)"
		printf '%-24s %5s %14s %9s %9s %6s %8s\n' "$prog" "$p" "$agg" "$wk" "$st" "$rc" "$lk"

		# 1 + 2: determinism against the N=1 row.
		if [ -z "$REF_AGG" ]; then
			REF_AGG="$agg"
			REF_RC="$rc"
		else
			[ "$agg" = "$REF_AGG" ] || bad "$prog procs=$p aggregate=$agg != serial $REF_AGG"
			[ "$rc" = "$REF_RC" ]   || bad "$prog procs=$p exit=$rc != serial exit=$REF_RC"
		fi
		[ "$rc" != "$LEAK_EXIT" ] || bad "$prog procs=$p exited $LEAK_EXIT (memory leak)"
		[ "$rc" != "$SEGV_EXIT" ] || bad "$prog procs=$p exited $SEGV_EXIT (segfault)"

		# 3: the drop-while-running reading.
		if [ "$lk" != "-" ] && [ "$lk" != 0 ]; then
			bad "$prog procs=$p leaked=$lk (a dropped coroutine's struct was stranded)"
		fi

		# 4: THE PIN.
		if [ "$wk" != "-" ] && [ "$wk" != 1 ]; then
			bad "$prog procs=$p workers=$wk — a worker M ran, so something published a coroutine to the scheduler"
		fi
		if [ "$st" != "-" ] && [ "$st" != 0 ]; then
			bad "$prog procs=$p steals=$st — a coroutine was stolen, so it reached a P ring"
		fi
	done
done

echo
if [ "$FAILED" = 0 ]; then
	echo "ALL CHECKS PASSED"
	exit 0
else
	echo "ONE OR MORE CHECKS FAILED"
	exit 1
fi
