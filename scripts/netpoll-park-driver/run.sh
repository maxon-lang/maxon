#!/usr/bin/env bash
# scripts/netpoll-park-driver/run.sh — THE ACCEPTANCE HARNESS FOR THE ASYNC-I/O PARK PROTOCOL.
#
# ⚠⚠ WHY A SCRIPT AND NOT A LINE IN A COMMIT MESSAGE. The driver landed as a bare `main.maxon`
# with its invocation, its expected output and both of its injection knobs recorded only in prose.
# A measurement whose command has to be reconstructed from a commit message is not repeatable, and
# the next person to touch this handshake needs to re-run it, not re-derive it.
#
# ⭐ WHAT IT DRIVES. `main.maxon` runs four green threads each reading 400 lines from a DELIBERATELY
# SLOW child (~2 ms per line). A fast producer is drained almost entirely through the self-detect
# fast path and exercises nothing.
# ⚠ HOW MUCH OF THAT REACHES THE PARK PATH IS A PER-BACKEND FACT, NOT A PROPERTY OF THE PACING. On
# arm64/kqueue essentially every read finds an empty pipe and parks; on x64-windows only ~8 of a
# reader's 400 do, because the parent end of the pipe carries
# FILE_SKIP_COMPLETION_PORT_ON_SUCCESS and the read usually completes synchronously. Both numbers
# are measured (2026-08-01) and neither is reachable by re-pacing the producer — see the arm64/
# x64-windows block below, and main.maxon's Windows arm.
#
# ⚠ THE FOUR READERS ARE LOAD-BEARING, NOT A ROUND NUMBER — one of the two knobs is UNREACHABLE
# without them. On the arm64 park handshake (EmitGtParkForIoCompletion) the parker's injection point
# sits on the `{prefix}_has_next` arm, i.e. it fires only when __gt_dequeue hands this GT a
# SUCCESSOR to switch to; with nothing else runnable the parker takes the drive-scheduler-and-
# self-detect arm instead and never reaches the commit CAS at all. Measured: a single-reader probe
# at MAXON_GT_PARK_DELAY_MS=25 adds 0 ms to the run, while this 4-reader driver at 5 ms adds ~2 s.
# A driver that parks one GT is a driver whose park arm silently measures nothing.
#
# ⭐ HOW MUCH TRAFFIC A RUN ACTUALLY BUYS, AND THE TWO NUMBERS ARE NOT THE SAME NUMBER:
#   TRAVERSALS — __netpoll_claim_done was measured firing about twice per 40 lines, so a 4x400-line
#     run makes roughly 80 full traversals of the protocol. That is the sample size behind the ratios
#     below.
#   CRITICAL-PATH EXPOSURE — far smaller, and it is what the knobs' wall-clock cost measures. From
#     the delay/elapsed slope (CLAIM=100 -> +1 s, CLAIM=1000 -> +8.5 s over a ~1.5 s baseline; the
#     knob costs TWO sleeps per traversal), only about four or five traversals per run land where a
#     reader is actually waiting on them. The rest are reaped by an M that had nothing else to do,
#     so their injected sleep hides behind the producer's 2 ms pacing and costs nothing.
# ⇒ DO NOT read a knob's small wall-clock cost as "the arm barely fired". Read the SLOPE: it is
#   linear in the delay, which is what says the injection is live. The PARK knob's slope is the
#   cleaner one (PARK=5 -> +2 s = 400 parks per reader x 5 ms, exactly), because parking is on the
#   reader's own critical path by construction and completing is not.
#
# ⭐⭐ EVERY FIGURE ABOVE WAS TAKEN ON arm64/kqueue, AND ONE OF THEM DOES NOT CARRY TO x64-WINDOWS.
# Measured 2026-08-01 (B3, the first run of this driver on Windows), 4x400 lines, ~6.5 s baseline:
#   CLAIM=1000 -> +8 s. The completer knob costs two sleeps per traversal, so that is ~4 traversals
#     per run landing where a reader waits. ⚠ COMPARE THAT AGAINST arm64's CRITICAL-PATH figure —
#     "about four or five traversals per run" from its own CLAIM=1000 -> +8.5 s — and NOT against
#     its ~80 total traversals, which is the other number this header just warned is not the same
#     number. Like for like it is 4 vs 4-5. ⇒ THE ACCEPTANCE ARM IS DRIVEN COMPARABLY ON BOTH
#     BACKENDS. (Total traversals on Windows are NOT measured; only the exposure is.)
#   PARK=1000  -> +8 s, i.e. ~8 parks per READER per run — NOT the 400 per reader the arm64 slope
#     above resolves to. On Windows the streaming pipe read returns synchronously the great majority
#     of the time (FILE_SKIP_COMPLETION_PORT_ON_SUCCESS on the parent end), so the parker's commit is
#     simply not on the path most reads take. ⇒ ~50x LESS PARK COVERAGE HERE THAN THERE.
# ⚠ THAT IS WHY PARK=5 COSTS ZERO WALL-CLOCK ON WINDOWS AND +2 s ON arm64, AND IT IS NOT A DEAD KNOB:
#   8 parks x 5 ms = 40 ms is inside the noise of a 6.5 s run. The knob was proven live by RAISING it
#   until the slope showed — which is the header's own rule, and the only way to tell an inert
#   injection from a cheap one. Anyone re-checking the knobs on Windows must do the same; a 5 ms run
#   that costs nothing is the expected reading, not the dead-knob symptom E-fixed in a90dc2f10.
# ⚠ AND IT IS NOT THE PRODUCER'S FAULT, WHICH WAS THE OBVIOUS HYPOTHESIS AND IS MEASURED FALSE. The
#   1-8 parks per reader is invariant across 2 / 10 / 25 / 50 / 200 ms pacing, across an explicitly
#   [Console]::Out.Flush()-ing producer, and across a 1-40 ms jittered one; and the producer's own
#   arrival cadence was measured directly at a flat 64 lines/s with no bursting. Raising the Windows
#   pacing buys coverage you can count on one hand and costs the run linearly.
#
# ⭐ TWO KNOBS, ONE PER SIDE OF THE HANDSHAKE — and a run that sets neither proves very little:
#
#   MAXON_GT_PARK_DELAY_MS   widens the PARKER's window: between its last self-detect and the
#                            commit CAS. This is the window the old two-word protocol lost a
#                            wakeup in, and the one B2's acceptance A/Bs.
#   MAXON_GT_CLAIM_DELAY_MS  widens the COMPLETER's window: the interval in which a completer OWNS
#                            the park word and has not yet released it. It fires at BOTH ENDS of
#                            that interval, and the two ends falsify different things:
#                              HEAD (inside __netpoll_claim, just past the winning CAS) — claimed,
#                                results not yet written. This is what a WAITER must not act on: a
#                                protocol that skipped `Claiming` lets a waiter see `Ready` here and
#                                read a stale io_result_val.
#                              TAIL (inside __netpoll_claim_done, just before the store-release) —
#                                results written, not yet released. This is what the RECOVERY NET
#                                must not act on: `status` already says ready, so the only thing
#                                between the net and a false positive is the word reading `Claiming`
#                                rather than `Parked`.
#                            ⚠ NEITHER END SUBSUMES THE OTHER, and the head was once deleted on the
#                            argument that it did. Against an injected fourth-state regression
#                            (__netpoll_claim CASing straight to `Ready`), 10 iterations each at
#                            CLAIM_DELAY=5: head alone 0/10, tail alone 0/10, correct protocol with
#                            both armed 10/10. See RuntimeEmitter.Netpoll.cs at both call sites.
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
