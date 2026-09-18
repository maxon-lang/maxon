#!/usr/bin/env bash
#
# One spec-suite run, reported as its own step so a crash is visible the moment it happens
# rather than after the whole shard finishes. Never fails the step: a red shard would stop
# the runs that follow it, and every run is a sample.
set -uo pipefail

run="$1"
shard="$2"
log="suite-shard$shard-run$run.log"

./maxon-bin/.maxon/maxon spec-test > "$log" 2>&1
rc=$?

line="$(grep -E '^[0-9]+ passed, [0-9]+ failed' "$log" | tail -1)"

if grep -qE 'DIED|panic:' "$log"; then
	cp "$log" "crash-shard$shard-run$run.log"
	echo "::error::shard $shard run $run CRASHED (exit $rc) | $line"
	grep -nE 'DIED|panic:|last step started|^  in ' "$log" | head -40
	exit 0
fi

echo "shard $shard run $run clean (exit $rc) | $line"
