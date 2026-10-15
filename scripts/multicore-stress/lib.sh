# What every driver in this directory needs, written once.
#
# ⛔ NO SCRIPT HERE SPELLS `.exe`. `scripts/lib/host-binaries.sh` owns that fact; three different
# answers to it is how one driver reports "no compiler" about a tree holding a perfectly good one.

MULTICORE_HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MULTICORE_REPO="$(cd "$MULTICORE_HERE/../.." && pwd)"
# shellcheck source=scripts/lib/host-binaries.sh
. "$MULTICORE_REPO/scripts/lib/host-binaries.sh"

# Overridable so the same driver can measure a compiler staged elsewhere, which is how a PARENT-commit
# reading is taken. The binary must sit inside a checkout: it locates `stdlib/` relative to itself.
MAXON="${MAXON:-$(maxon_compiler_path "$MULTICORE_REPO")}"

# Whether the space-separated list $1 holds the word $2.
list_has() {
	case " $1 " in
		*" $2 "*) return 0 ;;
		*) return 1 ;;
	esac
}

# ⭐⭐ WHICH PROGRAMS CREATE REAL GREEN THREADS — a `spawn` publishes to a P ring, an `async` to its
# caller's own coroutine queue. TWO things key off this one list: the worker-arrival prelude a program
# is compiled with, and the family `pin-matrix.sh` asserts for it. Kept here so a program added to the
# wrong one cannot inherit the other's expectation.
SPAWNING_PROGRAMS="${SPAWNING_PROGRAMS:-service-torture service-fanin-torture syscall-stack-torture}"

spawns_green_threads() {
	list_has "$SPAWNING_PROGRAMS" "$1"
}

# ⭐ WHICH PROGRAMS PRINT `monitor=` — the system monitor's three counters, which their one-worker pin is
# conditional on (`monitor-witness.maxon`). Each calls the prelude, so each is compiled with it.
#
# ⚠ **THE LIST IS NOT THE COMPLEMENT OF `SPAWNING_PROGRAMS`, AND `syscall-stack-torture` IS IN BOTH.** A
# coroutine program needs the reading because a monitor action is the only way it can reach a second M at all;
# that one needs it because its ~24,000 BLOCKING CALLS are retaken, and a retake's `__sched_handoffp` starts a
# machine on the processor it took — so its `workers` reading at ONE processor is the monitor's doing and not a
# green thread reaching a worker.
MONITOR_WITNESS_PROGRAMS="${MONITOR_WITNESS_PROGRAMS:-steal-torture drop-running-torture park-torture alloc-torture remote-free-torture refcount-torture service-torture service-fanin-torture syscall-stack-torture}"

# ⭐⭐ PROGRAMS THAT MUST NOT COMPILE, AND THE CODE EACH MUST BE REFUSED WITH.
# `refcount-torture.maxon` has twelve tasks read ONE module-level heap `String` — the
# shape its own header's 96-run table is about — and handing one box to two SERVICES is
# a compile-time refusal, because a send is a MOVE and the second one asks this frame
# to give up a reference the first one already took. That refusal is a fact about the
# transfer rule with nowhere else to live: no run can assert it, so a driver
# asserts the BUILD, and it asserts the CODE rather than merely "it failed" — a build
# that fails for an unrelated reason (a typo, a renamed builtin) would otherwise pass
# this check for ever.
#
# Each entry is `<program>:<code>`, because the code is a property of the PROGRAM
# and not of the list: two must-not-compile programs can be refused by two different
# rules, and one global code would silently accept the wrong one for the second.
REFUSED_PROGRAMS="${REFUSED_PROGRAMS:-refcount-service-refused:E3102}"

# Built by no driver, and kept out of `REFUSED_PROGRAMS`: `bootstrap-otherwise-binding-hang.maxon`'s
# header bars it from every suite, and the code it is refused with is incidental to what it pins.
REPRODUCER_PROGRAMS="${REPRODUCER_PROGRAMS:-bootstrap-otherwise-binding-hang}"

WORKER_ARRIVAL_PRELUDE=worker-arrival
MONITOR_WITNESS_PRELUDE=monitor-witness
PRELUDE_SOURCES="$WORKER_ARRIVAL_PRELUDE $MONITOR_WITNESS_PRELUDE"

refused_program_names() {
	local entry
	for entry in $REFUSED_PROGRAMS; do
		printf '%s ' "${entry%%:*}"
	done
}

torture_programs() {
	local src name
	for src in "$MULTICORE_HERE"/*.maxon; do
		if [ ! -e "$src" ]; then
			continue
		fi

		name="$(basename "$src" .maxon)"
		if list_has "$PRELUDE_SOURCES" "$name" || list_has "$REPRODUCER_PROGRAMS" "$name" || list_has "$(refused_program_names)" "$name"; then
			continue
		fi
		printf '%s\n' "$name"
	done
}

FAILED=0
pass() { echo "  PASS: $1"; }
bad() { echo "  FAIL: $1"; FAILED=1; }

require_compiler() {
	if [ ! -x "$MAXON" ]; then
		echo "FAIL: no compiler binary at $MAXON (build it, or set MAXON=)"
		exit 1
	fi
}

finish_checks() {
	echo
	if [ "$FAILED" = 0 ]; then
		echo "ALL CHECKS PASSED"
		exit 0
	else
		echo "ONE OR MORE CHECKS FAILED"
		exit 1
	fi
}

# ⚠ A PRELUDE GOES ONLY TO PROGRAMS THAT CALL IT. `RuntimeUsage.scanRuntimeUsage` filters only
# unreachable STDLIB functions, so an uncalled prelude would still install the scheduler queries into
# every binary here — changing programs whose readings are dated.
#
#   build_program <program> <output> [extra compiler flags...]
build_program() {
	local prog="$1" out="$2"
	shift 2

	# The two preludes are INDEPENDENT, because the two lists are: a program may call both, one, or neither,
	# and each is compiled in exactly where it is called.
	local preludes=""
	if spawns_green_threads "$prog"; then
		preludes="$preludes $MULTICORE_HERE/$WORKER_ARRIVAL_PRELUDE.maxon"
	fi
	if list_has "$MONITOR_WITNESS_PROGRAMS" "$prog"; then
		preludes="$preludes $MULTICORE_HERE/$MONITOR_WITNESS_PRELUDE.maxon"
	fi

	# shellcheck disable=SC2086
	"$MAXON" build "$@" "$MULTICORE_HERE/$prog.maxon" $preludes --output="$out"
}

build_or_report() {
	local prog="$1" out="$2" log="$3"
	shift 3

	if ! build_program "$prog" "$out" "$@" >"$log" 2>&1; then
		bad "$prog did not build"
		cat "$log"
		return 1
	fi
}

# One field off a program's stdout, or "-" when the program does not print it. Each program prints a
# different subset, deliberately: a driver demanding all of them would pin the harness, not the runtime.
number_field() {
	local v
	v="$(grep -o "^$2=[0-9-]*" "$1" | grep -o -- '-\?[0-9]*$' | head -1)"
	printf '%s' "${v:--}"
}

# The same, for a field whose value is a WORD. Kept separate rather than widening `number_field`: a
# numeric reader that accepts words stops being able to say a numeric field was missing.
word_field() {
	local v
	v="$(grep -o "^$2=[A-Za-z]*" "$1" | sed "s/^$2=//" | head -1)"
	printf '%s' "${v:--}"
}
