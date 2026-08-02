#!/usr/bin/env bash
# scripts/await-handoff-driver/run.sh — THE ACCEPTANCE HARNESS FOR THE AWAIT COMPLETION HAND-OFF.
#
# ⭐ WHAT IT PROVES, AND WHY IT IS NOT A SPEC TEST. The defect it targets is a losing interleaving:
# an awaiter publishes `promise.waiter = self` and keeps running its own scheduling loop, and a
# child completing on ANOTHER M inside that window used to enqueue a green thread that was still
# executing — after which a third M resumed it onto the SP saved at its previous suspension. No
# program can ASK for that ordering, so no spec case can pin it: `specs/async-await.md`'s six
# nested cases exercise the exact shape and pass identically before and after the fix. What makes
# it observable is the fault injection the runtime already ships, not a new program.
#
# ⭐ THE KNOB. `MAXON_GT_PARK_DELAY_MS` is DEFINED as the gap between a parker's last self-detect
# and its commit CAS (see RuntimeEmitter.Netpoll.cs). An awaiter is a parker and has exactly that
# gap, so `__gt_await_commit_park` fires the same injection. Stretch it and the child completes
# squarely inside the window that used to be unguarded.
#
# ⭐ THE MEASUREMENT (2026-08-01, x64-windows, 16-core host, default MAXON_MAX_PROCS):
#   unfixed compiler + MAXON_GT_PARK_DELAY_MS=5  -> `panic: nil pointer or invalid memory access`,
#     backtrace `in outer / in __gt_spawn` — the re-entered thread running its body a second time
#     on the same stack, first try.
#   unfixed compiler, knob unset                 -> clean. The window is real and narrow; a run
#     without the knob proves nothing either way.
#   fixed compiler + MAXON_GT_PARK_DELAY_MS=5    -> clean, `total=24 expected=24`.
# ⚠ READ THE SLOPE, NOT THE VALUE. A run whose elapsed time does not move is a run whose injection
# never fired, and that is the one failure mode this harness cannot distinguish from a pass. On the
# fixed compiler, over this driver's 32 injected awaits (`main`'s own await is a main-thread awaiter
# and is outside the handshake, so it does not fire): unset 0.030 s, PARK=5 0.418 s, PARK=50
# 1.716 s. Windows rounds `Sleep` up to the ~15.6 ms timer tick, which is why PARK=5 costs ~12 ms
# per await rather than 5 and the slope is only straight once the delay clears a tick.
#
# ⚠ THE VERDICT IS THE EXACT COUNT, NOT MERELY EXIT 0 — the driver prints `total=N expected=M` and
# returns non-zero when they differ, and this script greps for the agreement as well, so neither
# half can rot without the other noticing. A double-scheduled thread does not only crash: it can
# also run its body twice and return a doubled or truncated sum.
#
# ⚠ ITERATIONS DEFAULT TO 2, DELIBERATELY. This is a DETERMINISTIC reproduction with the knob armed
# — it failed on the first attempt, not the fortieth — so a long loop buys nothing and a previous
# session's 40-run harness made the host unusable. Raise it only if you are chasing something the
# single run does not show.
#
# Usage: run.sh [iterations] [timeout-seconds]
#   Environment: set MAXON_GT_PARK_DELAY_MS before invoking; it is honoured and logged.
#
# Exits 0 only when every iteration exited 0 with a matching count. Logs land in
# temp/await-handoff-driver/ (gitignored).

# `-u` but deliberately not `-e`: a failing iteration must be COUNTED, not abandoned.
set -u

repoRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
iterations="${1:-2}"
timeoutSeconds="${2:-120}"

driverDir="$repoRoot/scripts/await-handoff-driver"
exe="$driverDir/main"
outDir="$repoRoot/temp/await-handoff-driver"
mkdir -p "$outDir"
summary="$outDir/summary.txt"
: > "$summary"

# A hang must be BOUNDED to be a result: a lost wakeup is one of the two ways this handshake can
# fail, and an unbounded wait reads as "still running" for ever.
timeoutBin="$(command -v timeout || command -v gtimeout || true)"
if [ -z "$timeoutBin" ]; then
	echo "await-handoff-driver: no timeout(1) — install coreutils; a hang must be BOUNDED to be a result" >&2
	exit 2
fi

# ⚠ BUILT BY THE BOOTSTRAP, NOT BY shv2: the async runtime under test is the bootstrap's, which is
# also where the hand-off lives.
if ! "$repoRoot/bin/maxon" build "$driverDir" > "$outDir/build.log" 2>&1; then
	echo "await-handoff-driver: build FAILED — see $outDir/build.log" >&2
	tail -20 "$outDir/build.log" >&2
	exit 2
fi

parkDelay="${MAXON_GT_PARK_DELAY_MS:-0}"
echo "await-handoff-driver: iterations=$iterations timeout=${timeoutSeconds}s park=${parkDelay}ms" \
	| tee -a "$summary"
if [ "$parkDelay" = "0" ]; then
	echo "await-handoff-driver: ⚠ park delay UNSET — this run cannot falsify anything; see the header" \
		| tee -a "$summary"
fi

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
	elif ! echo "$line" | /usr/bin/grep -qE 'total=([0-9]+) expected=\1'; then
		verdict="WRONG ANSWER (count mismatch slipped past the driver's own check)"
		failures=$((failures + 1))
	else
		verdict="ok"
	fi

	echo "run $i: $verdict  ${elapsed}s  [$line]" | tee -a "$summary"
done

echo "await-handoff-driver: $((iterations - failures))/$iterations clean" | tee -a "$summary"
[ "$failures" -eq 0 ]
