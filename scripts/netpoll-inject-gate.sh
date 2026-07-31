#!/usr/bin/env bash
# scripts/netpoll-inject-gate.sh — THE ACCEPTANCE TEST FOR THE ASYNC-I/O PARK PROTOCOL.
#
# WHY THIS EXISTS. The lost wakeup this protocol closes fires roughly once in 14,000 suite runs. At
# that rate none of the tools that fixed the PREVIOUS bug in this handshake are available: you cannot
# reproduce it, you cannot capture stacks from it, you cannot A/B it, and — the part that matters —
# a green suite is not evidence of anything. Five hundred clean runs are not evidence either.
#
# So the window is not measured. It is MADE ENORMOUS ON DEMAND. The runtime carries a fault
# injection point (see RuntimeEmitter.Netpoll.cs's __netpoll_inject_delay) at the exact instruction
# between a parking green thread's last self-detect and its commit — the instant the race needs — and
# MAXON_GT_PARK_DELAY_MS stretches it from ~25 instructions to milliseconds. An interleaving that was
# admissible-but-astronomically-rare becomes the common case, and a race that could not be tested
# becomes a deterministic one.
#
# ⚠⚠ THE VACUITY CHECK IS THE WHOLE POINT AND IS NOT OPTIONAL. An injection harness that never fails
# proves nothing at all — it would report a triumphant green against a compiler with the bug still
# in it, which is strictly worse than no harness. Before trusting a clean run here you MUST have
# watched this same script WEDGE against a build that still has the old protocol. Point --exe at
# such a build to do that; the instructions are in the rung's report and the recipe is:
#
#     git worktree add <dir> <pre-fix-commit>
#     # add ONLY the injection delay to the old park path there (no protocol change)
#     dotnet build maxon-sharp && ./bin/maxon build maxon-shv2
#     scripts/netpoll-inject-gate.sh 20 5 60 --exe <dir>/maxon-shv2/.maxon/maxon-shv2
#
# BAR: the OLD protocol wedges on almost every injected run; the NEW protocol on none.
#
# Usage: scripts/netpoll-inject-gate.sh [iterations] [delay-ms] [hang-seconds] [--exe PATH]
#   iterations    suite runs to attempt                                   (default 20)
#   delay-ms      MAXON_GT_PARK_DELAY_MS for each run                     (default 5)
#   hang-seconds  how long a run may take before it is declared wedged.   (default 60)
#                 ⚠ The injection SLOWS every I/O park by delay-ms, so an
#                 injected run legitimately takes far longer than the 5-9 s
#                 an ordinary one does. Too small a deadline here reports a
#                 slow run as a wedge and the harness lies in the safe
#                 direction, which is still a lie.
#   --exe PATH    the maxon-shv2 binary to test  (default: this tree's)
#
# Exits 0 when no run wedged, 1 when any did. Logs land in temp/netpoll-inject/ (gitignored).

# `-u` but deliberately not `-e`: the process-tree teardown probes pids that are allowed to have
# already exited, and an errexit shell would abandon the loop on the first of them.
set -u

repoRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
iterations=20
delayMs=5
hangSeconds=60
exe="$repoRoot/maxon-shv2/.maxon/maxon-shv2"

positional=()
while [ $# -gt 0 ]; do
	case "$1" in
		--exe)
			[ $# -ge 2 ] || { echo "netpoll-inject-gate: --exe needs a path" >&2; exit 2; }
			exe="$2"; shift 2 ;;
		*) positional+=("$1"); shift ;;
	esac
done
[ "${#positional[@]}" -ge 1 ] && iterations="${positional[0]}"
[ "${#positional[@]}" -ge 2 ] && delayMs="${positional[1]}"
[ "${#positional[@]}" -ge 3 ] && hangSeconds="${positional[2]}"

if [ ! -x "$exe" ]; then
	echo "netpoll-inject-gate: no shv2 binary at $exe — build it first" >&2
	exit 2
fi

outDir="$repoRoot/temp/netpoll-inject"
mkdir -p "$outDir"
summary="$outDir/summary.txt"
: > "$summary"

echo "netpoll-inject-gate: exe=$exe delay=${delayMs}ms iterations=$iterations deadline=${hangSeconds}s" | tee -a "$summary"

# Every descendant of $1, deepest last. `pgrep -P` one level at a time: macOS `ps` has no --forest,
# and the pool is only two levels deep (parent -> worker -> compile grandchild).
descendants() {
	local roots="$1" next all=""
	while [ -n "$roots" ]; do
		all="$all $roots"
		next=""
		for p in $roots; do
			next="$next $(pgrep -P "$p" 2>/dev/null | tr '\n' ' ')"
		done
		roots="$(echo "$next" | tr -s ' ' | sed 's/^ //;s/ $//')"
	done
	echo "$all" | tr -s ' ' | sed 's/^ //;s/ $//'
}

# A recorded pid may have exited and had its number reused, so check each against the command it is
# expected to be before signalling it. The cost of the guard is a leaked process in a race we have
# never observed; the cost of omitting it is signalling an unrelated process of the user's.
killTree() {
	local recorded="$1" signal="$2" p command
	for p in $recorded; do
		command="$(ps -o command= -p "$p" 2>/dev/null)"
		case "$command" in
			*maxon-shv2*|*wasmtime*) kill "-$signal" "$p" 2>/dev/null ;;
			*) : ;;
		esac
	done
	return 0
}

wedges=0
for i in $(seq 1 "$iterations"); do
	log="$outDir/run-$i.log"
	start="$(date +%s)"
	# `exec`, so $! IS the pool parent rather than a shell holding it — the teardown walks its
	# children to find the workers, and one extra shell in between hides them.
	( cd "$repoRoot" && MAXON_GT_PARK_DELAY_MS="$delayMs" exec "$exe" spec-test ) > "$log" 2>&1 &
	runner=$!

	wedged=0
	while kill -0 "$runner" 2>/dev/null; do
		if [ $(( $(date +%s) - start )) -ge "$hangSeconds" ]; then
			wedged=1
			break
		fi
		sleep 1
	done

	if [ "$wedged" -eq 1 ]; then
		wedges=$((wedges + 1))
		# Record the tree BEFORE signalling: once the parent is gone its grandchildren are
		# reparented to launchd and nothing here can find them again.
		tree="$(descendants "$runner")"
		echo "run $i WEDGED after ${hangSeconds}s" | tee -a "$summary"
		killTree "$tree" TERM
		sleep 2
		killTree "$tree" KILL
		wait "$runner" 2>/dev/null
	else
		wait "$runner"
		code=$?
		elapsed=$(( $(date +%s) - start ))
		# A run the pool's OWN watchdog took down is still a wedge, and counting it as an ordinary
		# failure is how this harness would quietly stop finding anything.
		if /usr/bin/grep -qi "spec worker pool wedged" "$log" 2>/dev/null; then
			wedges=$((wedges + 1))
			echo "run $i WEDGED (pool watchdog) exit=$code ${elapsed}s" | tee -a "$summary"
		else
			echo "run $i exit=$code ${elapsed}s $(tail -1 "$log")" | tee -a "$summary"
		fi
	fi
done

echo "--- $iterations injected runs, $wedges wedge(s) ---" | tee -a "$summary"
[ "$wedges" -eq 0 ]
