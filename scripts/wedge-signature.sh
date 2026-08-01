# scripts/wedge-signature.sh — IS THIS POOL WEDGED, OR MERELY SLOW? Sourced by spec-wedge-repro.sh,
# which is the harness that has to tell those apart.
#
# ⚠ IT MUST STAY TRACKED. It landed UNCOMMITTED beside the committed `. "$repoRoot/scripts/…"` that
# reads it, and the failure that produces is quiet in the worst direction: with `set -u` and no
# `-e`, a missing source leaves `wedgeProgressCheck` undefined, the verdict comes back EMPTY, and
# the caller's `case` treats empty as "still progressing" — so the harness extends its deadline
# forever and can never report a wedge. A green light wired to nothing, which is exactly the
# standard the netpoll inject-gate was deleted for failing.
#
# ⚠⚠ WHY THIS FILE EXISTS: A DEADLINE IS NOT A DETECTOR, AND TRUSTING ONE PRODUCED THREE FALSE
# WEDGES IN ONE 250-RUN BATCH. The spec pool buffers its report to the very end, so there is no
# progress line to watch and both harnesses originally declared a wedge on the clock alone — "this
# run took longer than N seconds". That is a measurement of the MACHINE, not of the pool. A batch run
# beside a `dotnet build` reported runs 3, 15 and 183 as wedges whose own logs each ended
# `2696 passed, 0 failed`: they finished, they were just slow. Raising the deadline does not fix
# that; it only moves the threshold at which the instrument starts lying, and it lies in the
# expensive direction — a detector that fires on load makes BOTH arms of an A/B unreadable, and
# B2's whole acceptance is an A/B.
#
# ⭐ SO ASK FOR PROGRESS, NOT FOR PATIENCE. Sample every process's ACCUMULATED CPU TIME, wait, and
# sample again. A wedged pool is the one shape that cannot fake it: its parent is parked in `kevent`
# with a wakeup that will never arrive and its workers are blocked in `read(0)`, so not one of them
# burns a further microsecond, however long you look. A slow run advances SOMETHING, always, no
# matter how contended the box — that is what "slow" means. CPU time is immune to load in exactly
# the way wall time is not.
#
# ⚠ NOT `%CPU`. macOS reports it as a decaying average over up to a minute of real time, so a pool
# that wedged 40 s ago still shows residue from the work it did before it stopped, and a pool that
# is running in short bursts can read 0.0 between them. The threshold that separates those two does
# not exist. A DIFFERENCE between two readings of a monotonically accumulating counter needs no
# threshold at all.

# wedgeProgressCheck <parentPid> <observeSeconds> -> prints "WEDGED" or "PROGRESSING <pid>"
#
# Exits nonzero (and prints "GONE") if the parent is no longer alive, which means the run finished
# while we were deciding — itself proof it was never wedged.
wedgeProgressCheck() {
	local parent="$1" observeSeconds="$2"
	local pidList before after

	kill -0 "$parent" 2>/dev/null || { echo "GONE"; return 1; }

	# The parent and its workers, as a ps-ready comma list. Grandchildren (a worker's compile
	# subprocess) are deliberately NOT included: one existing is proof of progress on its own, which
	# `wedgeHasGrandchild` reports separately, and a worker blocked waiting on a child accrues no CPU
	# of its own, so including them would only add noise.
	pidList="$(printf '%s\n%s\n' "$parent" "$(pgrep -P "$parent" 2>/dev/null)" \
		| /usr/bin/grep -E '^[0-9]+$' | tr '\n' ',' | sed 's/,$//')"
	[ -n "$pidList" ] || { echo "GONE"; return 1; }

	# ⚠ ONE `ps` CALL, COMPARED AS A WHOLE STRING — no per-pid packing and no parsing. An earlier
	# version of this built a `pid:cputime` list joined by spaces and split it back apart with
	# `tr ' ' '\n'`, which silently broke the moment a field contained a space (macOS pads cputime
	# to `  0:00.02`), and the function then answered backwards: WEDGED for a busy pool and
	# PROGRESSING for an idle one. The whole comparison is "did this table change?", so ask exactly
	# that and give the shell nothing to mis-split.
	before="$(ps -o pid=,cputime= -p "$pidList" 2>/dev/null)"
	sleep "$observeSeconds"
	after="$(ps -o pid=,cputime= -p "$pidList" 2>/dev/null)"

	# Identical table ⇒ not one of these processes was scheduled for `observeSeconds`. A process that
	# exited changes the table too, and an exit is progress.
	if [ "$before" = "$after" ]; then
		echo "WEDGED"
	else
		echo "PROGRESSING"
	fi
	return 0
}
