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
#   4. ⭐ THE PIN ITSELF, AND IT IS NOW PER FAMILY (SV1). A COROUTINE-ONLY
#      program reads `workers=1` and `steals=0` at every N. A SPAWN-DRIVEN one —
#      the `SPAWNING_PROGRAMS` list, which is where the fact is written down once —
#      reads `1/0` at N=1 and `workers >= 2`, `steals > 0` at every N >= 2, which
#      is this script's own prediction below, cashed.
#   5. Every program in `REFUSED_PROGRAMS` FAILS TO BUILD, with the code named.
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
# ⭐⭐ `spawn` HAS LANDED AND ASSERTION 4 HAS FLIPPED, EXACTLY WHERE THIS PARAGRAPH
# SAID IT WOULD. A `spawn` (SERVICES_DESIGN.md §"Ownership — the spine", "Send is a
# MOVE") creates REAL green threads, which are exactly what W212's ring, its
# stealing and its worker loop schedule — all built before they had a producer.
# `service-torture` is that producer, and it is the ONE program here whose row is
# asserted the other way; every other program stays coroutine-only and stays 1/0.
#
# MEASURED at SV1 wave 3, same box, this script (12 services x 400 String sends
# each): `workers/steals` of 1/0, 2/886, 7/7790 and 9/8120 at N=1/2/7/12, with a
# byte-identical `aggregate=42680` and exit 42 at every N. The N=1 row is not a
# weaker reading than the others — it is the control: with one P there is no
# second M to wake, so the services are driven cooperatively by the main thread
# and the same answer comes out.
#
# ⚠ refcount-torture IS IN THE LIST FOR ITS workers/steals ROW ONLY. Its own
# subject — whether a contended refcount word survives — is INTERMITTENT, so a
# single run per processor count cannot see it and this script never claims to:
# `refcount-race.sh` repeats it and tabulates exit codes, which is the reading
# that discriminates. What it contributes here is one more program whose spawn
# path is asserted to reach no worker M.
#
# ⭐⭐ AND IT STAYS AN `async` PROGRAM, BECAUSE THE SERVICE FORM OF IT DOES NOT
# COMPILE (SV1 wave 4). It hands ONE heap `String` to twelve tasks; a send is a
# MOVE, so the second hand-over asks this frame to give up a reference the first
# already took. `refcount-service-refused.maxon` is that program, and it is in
# `REFUSED_PROGRAMS` rather than `PROGRAMS`: the driver asserts the BUILD FAILS and
# fails with **E3102** specifically — `use of moved value`, which is sharper than the
# transferability refusal E3138 because the first send did not merely threaten a
# second owner, it TOOK the reference. The refusal is a fact about the move that no
# RUN can assert, so it lives beside the program it mirrors.
#
# ⚠ CHECKING THE CODE IS NOT PEDANTRY — IT CAUGHT THE FIRST CUT OF THAT FILE. Written
# as a loop over twelve services it was refused by `E2015` instead, a path-sensitivity
# limit on moving a value declared outside a loop, which says nothing whatever about
# transfer. A must-not-compile program that fails for the wrong reason asserts nothing,
# and this check went red on its first run rather than passing quietly.
#
# ⭐⭐ service-fanin-torture IS THE MAILBOX'S OTHER SIDE. `service-torture` is ONE
# sender and twelve mailboxes; this is twelve SENDER SERVICES and one, so the
# multi-producer half of `__mbox_send` finally has more than one producer. Its
# aggregate is accumulated by the SINK — whose handlers its own mailbox serializes
# — rather than by `main`, which is what makes the reading sound at N >= 2 without
# a cross-thread flag. MEASURED at SV1 wave 4: `aggregate=42680 taken=4800` at
# every N, with workers/steals of 1/0, 2/2, 7/20 and 12/23 at N=1/2/7/12.
#
# ⚠ AND THAT IS ALSO WHAT alloc-torture AND remote-free-torture NOW COST. Both
# exist to drive the sharded allocator's CROSS-P paths, and both do it by getting
# worker Ms to run their tasks. With no worker M they still run every task and
# still prove determinism and leak-freedom — on ONE M. Their multi-M subject has
# no producer until `spawn`, and no reading below should be read as covering it.
#
# ⭐⭐ park-torture IS THE ONLY ONE THAT PARKS, WHICH IS WHY SV1 ADDED IT. Every
# other program here spins and returns, so not one of them walks the deferred-park
# path (W218) at all — the window between registering on the store that will wake
# a green thread and its registers being saved. It contributes 3200 suspensions
# per run for about fifty milliseconds, and its own header carries the sabotage
# reading that proves it can go red (139 at N=2/7/12 with the deferral reverted
# and the window widened; clean at N=1) together with the two instruments that
# reading needs. Its rows HERE are the coroutine ones, like everything else's.
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
PROGRAMS="${PROGRAMS:-steal-torture drop-running-torture park-torture alloc-torture remote-free-torture refcount-torture service-torture service-fanin-torture}"

# ⭐⭐ WHICH PROGRAMS CREATE REAL GREEN THREADS — the one fact assertion 4 is keyed
# by, written down once so a program added to either family cannot inherit the
# other's expectation by being appended to the wrong list. A `spawn` publishes to a
# P RING; an `async` publishes to its caller's own coroutine queue.
SPAWNING_PROGRAMS="${SPAWNING_PROGRAMS:-service-torture service-fanin-torture}"

spawns_green_threads() {
	case " $SPAWNING_PROGRAMS " in
		*" $1 "*) return 0 ;;
		*) return 1 ;;
	esac
}

# ⭐⭐ PROGRAMS THAT MUST NOT COMPILE, AND THE CODE EACH MUST BE REFUSED WITH.
# `refcount-torture.maxon` hands ONE heap `String` to twelve tasks — the shape its
# own header's 96-run table is about — and the same program written with SERVICES is
# a compile-time refusal, because a send is a MOVE and the second one asks this frame
# to give up a reference the first one already took. That refusal is a fact about the
# transfer rule with nowhere else to live: no run can assert it, so this driver
# asserts the BUILD, and it asserts the CODE rather than merely "it failed" — a build
# that fails for an unrelated reason (a typo, a renamed builtin) would otherwise pass
# this check for ever.
#
# Each entry is `<program>:<code>`, because the code is a property of the PROGRAM
# and not of the list: two must-not-compile programs can be refused by two different
# rules, and one global code would silently accept the wrong one for the second.
REFUSED_PROGRAMS="${REFUSED_PROGRAMS:-refcount-service-refused:E3102}"

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

# The other half of the compile step: the programs whose whole content is that they
# DO NOT build. Asserted here rather than in a spec case because what they mirror is
# a torture program, and a reader comparing the two has to find them side by side.
for entry in $REFUSED_PROGRAMS; do
	prog="${entry%%:*}"
	code="${entry##*:}"

	if "$MAXON" build "$HERE/$prog.maxon" -o "$WORK/$prog" >"$WORK/$prog.build.log" 2>&1; then
		bad "$prog COMPILED — it must be refused with $code; the rule it mirrors has stopped covering the shape it was written for"
		continue
	fi

	if ! grep -q "$code" "$WORK/$prog.build.log"; then
		bad "$prog was refused, but not with $code — a build that fails for an unrelated reason asserts nothing"
		head -3 "$WORK/$prog.build.log"
		continue
	fi

	pass "$prog refused with $code"
done
echo "refused: $REFUSED_PROGRAMS"
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

		# 4: THE PIN, per family — see the header. A program that prints neither
		# counter (`-`) is asserted nothing, which is how it has always been.
		if spawns_green_threads "$prog"; then
			# A SPAWN-driven program. At N=1 there is no second M to wake, so it reads
			# like a coroutine program and that row is the CONTROL for the others.
			if [ "$p" = 1 ]; then
				if [ "$wk" != "-" ] && [ "$wk" != 1 ]; then
					bad "$prog procs=1 workers=$wk — with one P there is no second M for a spawned green thread to wake"
				fi
				if [ "$st" != "-" ] && [ "$st" != 0 ]; then
					bad "$prog procs=1 steals=$st — with one P there is nobody to steal from"
				fi
			else
				if [ "$wk" != "-" ] && [ "$wk" -lt 2 ]; then
					bad "$prog procs=$p workers=$wk — a spawned green thread reached no worker M, so nothing published it to a P ring"
				fi
				if [ "$st" != "-" ] && [ "$st" -lt 1 ]; then
					bad "$prog procs=$p steals=$st — a spawned green thread was never stolen, so the ring's work never crossed an M"
				fi
			fi
		else
			if [ "$wk" != "-" ] && [ "$wk" != 1 ]; then
				bad "$prog procs=$p workers=$wk — a worker M ran, so something published a coroutine to the scheduler"
			fi
			if [ "$st" != "-" ] && [ "$st" != 0 ]; then
				bad "$prog procs=$p steals=$st — a coroutine was stolen, so it reached a P ring"
			fi
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
