#!/usr/bin/env bash
# scripts/netpoll-park-driver/run.sh — THE ACCEPTANCE HARNESS FOR THE ASYNC-I/O PARK PROTOCOL.
#
# ⚠⚠ WHY A SCRIPT AND NOT A LINE IN A COMMIT MESSAGE. The driver landed as a bare `main.maxon`
# with its invocation, its expected output and both of its injection knobs recorded only in prose.
# A measurement whose command has to be reconstructed from a commit message is not repeatable, and
# the next person to touch this handshake needs to re-run it, not re-derive it.
#
# ⭐ WHAT IT DRIVES. `main.maxon` runs four green threads each reading 400 lines from a DELIBERATELY
# SLOW child (~2 ms per line), so every read finds an empty pipe and takes the async park path. A
# fast producer is drained almost entirely through the self-detect fast path and exercises nothing.
#
# ⭐ TWO KNOBS, ONE PER SIDE OF THE HANDSHAKE — and a run that sets neither proves very little:
#
#   MAXON_GT_PARK_DELAY_MS   widens the PARKER's window: between its last self-detect and the
#                            commit CAS. This is the window the old two-word protocol lost a
#                            wakeup in, and the one B2's acceptance A/Bs.
#   MAXON_GT_CLAIM_DELAY_MS  widens the COMPLETER's window: between `status = ready` and the claim
#                            of the park word. A waiter that self-detects on `status` inside it
#                            releases a word its completer has not claimed yet.
#
# ⚠ THE VERDICT IS THE LINE COUNT, NOT MERELY EXIT 0. A lost wakeup hangs and the timeout catches
# that; a spuriously aborted park instead delivers the PREVIOUS completion's result, which comes
# back as a SHORT COUNT and exits 0 under a `> 0` check. The driver prints `lines=N expected=M` and
# returns non-zero when they differ; this script also greps the output, so neither half can rot
# without the other noticing.
#
# ⚠⚠ AND THE THIRD ARM: A RESCUED RUN IS A FAILED RUN. __netpoll_recover is a REGRESSION DETECTOR
# that happens to also rescue the run — under a correct protocol its counter is exactly 0, so its
# warning line is a bug report, not a note. This script used to ignore it, and the only reason that
# never produced a false green is that the runs which tripped the net ALSO leaked: in the arm that
# opened this slice, 4 of 5 runs printed the warning and all 5 exited 101, so the leak check caught
# what this check missed. A future regression the net rescues CLEANLY would have been reported `ok`
# — a harness that reports green while the runtime is printing "the park protocol lost one".
#
# Usage: run.sh [iterations] [timeout-seconds]
#   Environment: set either injection knob before invoking; both are honoured and both are logged.
#
# Exits 0 only when every iteration exited 0 with a matching count. Logs land in
# temp/netpoll-park-driver/ (gitignored).

# `-u` but deliberately not `-e`: a failing iteration must be COUNTED, not abandoned — the whole
# point is the pass/fail ratio over N runs.
set -u

repoRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
iterations="${1:-15}"
timeoutSeconds="${2:-120}"

driverDir="$repoRoot/scripts/netpoll-park-driver"
exe="$driverDir/main"
outDir="$repoRoot/temp/netpoll-park-driver"
mkdir -p "$outDir"
summary="$outDir/summary.txt"
: > "$summary"

# `timeout` is coreutils; macOS ships it only via Homebrew. Say so rather than hanging forever on a
# wedge, which is the one outcome this harness exists to detect.
timeoutBin="$(command -v timeout || command -v gtimeout || true)"
if [ -z "$timeoutBin" ]; then
	echo "netpoll-park-driver: no timeout(1) — install coreutils; a hang must be BOUNDED to be a result" >&2
	exit 2
fi

# ⚠ BUILT BY THE BOOTSTRAP, NOT BY shv2, AND THAT IS NOT AN ACCIDENT: shv2 cannot lower the P1.5
# IOCP ops on arm64, so the async runtime under test has to be the bootstrap's — which is also
# where the park protocol lives.
if ! "$repoRoot/bin/maxon" build "$driverDir" > "$outDir/build.log" 2>&1; then
	echo "netpoll-park-driver: build FAILED — see $outDir/build.log" >&2
	tail -20 "$outDir/build.log" >&2
	exit 2
fi

parkDelay="${MAXON_GT_PARK_DELAY_MS:-0}"
claimDelay="${MAXON_GT_CLAIM_DELAY_MS:-0}"
echo "netpoll-park-driver: iterations=$iterations timeout=${timeoutSeconds}s park=${parkDelay}ms claim=${claimDelay}ms" \
	| tee -a "$summary"

failures=0
for i in $(seq 1 "$iterations"); do
	log="$outDir/run-$i.log"
	start=$(date +%s)
	"$timeoutBin" "$timeoutSeconds" "$exe" > "$log" 2>&1
	code=$?
	elapsed=$(( $(date +%s) - start ))

	line="$(tr -d '\r' < "$log" | tr '\n' ' ')"
	# 124 is timeout(1)'s own code for "deadline expired" — i.e. the wedge.
	if [ "$code" -eq 124 ]; then
		verdict="WEDGED (no completion in ${timeoutSeconds}s)"
		failures=$((failures + 1))
	elif [ "$code" -ne 0 ]; then
		verdict="FAILED exit=$code"
		failures=$((failures + 1))
	elif ! echo "$line" | /usr/bin/grep -qE 'lines=([0-9]+) expected=\1'; then
		# Belt and braces: the driver already returns non-zero on a mismatch, so reaching here
		# means the two halves of the verdict disagree, which is itself a finding.
		verdict="WRONG ANSWER (count mismatch slipped past the driver's own check)"
		failures=$((failures + 1))
	elif echo "$line" | /usr/bin/grep -q 'netpoll safety net'; then
		# The right answer, arrived at the wrong way. See the header: the net's own message says
		# the protocol lost a wakeup, and that is the thing under test.
		verdict="RESCUED (recovery net fired — the protocol lost a wakeup)"
		failures=$((failures + 1))
	else
		verdict="ok"
	fi

	echo "run $i: $verdict  ${elapsed}s  [$line]" | tee -a "$summary"
done

echo "netpoll-park-driver: $((iterations - failures))/$iterations clean" | tee -a "$summary"
[ "$failures" -eq 0 ]
