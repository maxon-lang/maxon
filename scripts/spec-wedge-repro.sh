#!/usr/bin/env bash
# scripts/spec-wedge-repro.sh — loop the shv2 spec suite until it WEDGES, and on a wedge
# CAPTURE rather than kill.
#
# WHY: the pool wedged twice in one session at roughly 2 runs in 15 (~13%), and the identical
# binary then ran the same suite green on retry. A bug at that rate cannot be studied by hand —
# by the time a human notices a hang, the interesting state is whatever they typed next. This
# loops the suite, notices the hang itself, and freezes the evidence before anything is killed:
# park stacks for the parent and for the idle workers (`sample`), the workers' open pipes
# (`lsof`), and the whole process tree.
#
# The captured stacks are the deliverable. `sample` is a macOS built-in and needs no build flags,
# so a wedge caught here is diagnosable from a release binary.
#
# Usage:  scripts/spec-wedge-repro.sh [iterations] [hang-seconds] [lanes]
#   iterations   how many suite runs to attempt          (default 40)
#   hang-seconds how long a run may take before it is    (default 90)
#                declared wedged; the suite runs in
#                5-9 s, so this is a >10x margin
#   lanes        both | host | wasm                      (default both)
#
# Artifacts land in temp/wedge-repro/ (gitignored). The loop KEEPS GOING after a wedge, so a long
# run collects several independent captures rather than one.

# `-u` but deliberately NOT `-e`: almost every command here is a probe of a process that is allowed to
# have gone away — `pgrep` with no match, `kill` on an already-dead pid, `lsof` on a vanished worker —
# and an errexit shell would abandon a half-written capture on the first of them. Exit codes that
# matter are read explicitly.
set -u

repoRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
iterations="${1:-40}"
hangSeconds="${2:-90}"
lanes="${3:-both}"

exe="$repoRoot/maxon-shv2/.maxon/maxon-shv2"
outDir="$repoRoot/temp/wedge-repro"

if [ ! -x "$exe" ]; then
	echo "spec-wedge-repro: no shv2 binary at $exe — build it first" >&2
	exit 2
fi

mkdir -p "$outDir"
summary="$outDir/summary.txt"
: > "$summary"

# Every descendant of $1, deepest last. `pgrep -P` one level at a time: macOS `ps` has no
# --forest and the pool is only two levels deep (parent -> worker -> compile/wasmtime grandchild).
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

capture() {
	local parent="$1" dir="$2"
	mkdir -p "$dir"

	local tree
	tree="$(descendants "$parent")"
	echo "$tree" > "$dir/pids.txt"

	# The process table FIRST — it is the one thing that changes if sampling perturbs anything.
	ps -o pid,ppid,etime,%cpu,stat,wq,command -p $(echo "$tree" | tr ' ' ',') > "$dir/ps.txt" 2>&1

	# Workers = direct children of the parent. Sample the parent and EVERY worker in PARALLEL, so
	# all thirteen stacks describe the same instant rather than thirteen instants 5 s apart. The
	# sample pids are collected explicitly: a bare `wait` here would also wait on the wedged suite
	# runner, which is the one process guaranteed never to finish.
	local workers
	workers="$(pgrep -P "$parent" 2>/dev/null | tr '\n' ' ')"
	echo "$workers" > "$dir/workers.txt"

	local samplers=""
	sample "$parent" 5 -f "$dir/sample-parent-$parent.txt" > /dev/null 2>&1 &
	samplers="$samplers $!"
	for w in $workers; do
		sample "$w" 5 -f "$dir/sample-worker-$w.txt" > /dev/null 2>&1 &
		samplers="$samplers $!"
	done
	for s in $samplers; do wait "$s" 2>/dev/null; done

	# Open files: is the worker's stdin pipe still open at both ends?
	/usr/sbin/lsof -p "$parent" > "$dir/lsof-parent.txt" 2>&1
	for w in $workers; do
		/usr/sbin/lsof -p "$w" > "$dir/lsof-worker-$w.txt" 2>&1
	done

	# Grandchildren: a worker mid-job has a compiler (or wasmtime) child. NONE means every worker
	# is between jobs — waiting for one, not finishing one.
	local grandchildren=""
	for w in $workers; do
		grandchildren="$grandchildren $(pgrep -P "$w" 2>/dev/null | tr '\n' ' ')"
	done
	echo "$grandchildren" | tr -s ' ' > "$dir/grandchildren.txt"

	probeArmedRegistration "$parent" "$workers" "$dir"
}

# THE DISCRIMINATOR, and the reason this harness kills anything at all.
#
# The workers read their jobs with a PLAIN BLOCKING read(2) on fd 0 (maxon_managed_read_stdin is
# three instructions and a `read`), so a worker parked there is parked in the kernel and the kernel
# wakes it the instant a byte lands. The parent's side is the opposite: its drains park on a
# kqueue EVFILT_READ registered EV_ADD|EV_ONESHOT. That asymmetry is what this probe exploits.
#
# SIGKILL one worker and watch the parent:
#
#   * The parent ABORTS ("stdout reached EOF before WORKER_READY") => that fd's registration was
#     still ARMED, so the death delivered an event. An armed registration on an empty pipe means
#     the worker had written nothing: the wedge is in the parent->worker direction.
#   * The parent stays SILENT and keeps polling => no event was delivered, so the one-shot
#     registration was already CONSUMED — the wakeup it carried was lost. The answer may well be
#     sitting unread in that pipe: the wedge is in the worker->parent direction.
#
# One SIGKILL separates the two hypotheses that every other observation here leaves tied.
probeArmedRegistration() {
	local parent="$1" workers="$2" dir="$3"
	local first="" alive

	for w in $workers; do
		first="$w"
		break
	done
	[ -z "$first" ] && return 0

	{
		echo "probe: SIGKILL worker $first at $(date +%T)"
		kill -KILL "$first" 2>/dev/null
		sleep 5
		if kill -0 "$parent" 2>/dev/null; then
			alive="still running"
		else
			alive="EXITED"
		fi
		echo "probe: after killing one worker, parent is $alive"
		ps -o pid,etime,%cpu,stat -p "$parent" 2>&1

		echo "probe: SIGKILL the remaining workers at $(date +%T)"
		for w in $workers; do
			[ "$w" = "$first" ] && continue
			kill -KILL "$w" 2>/dev/null
		done
		sleep 5
		if kill -0 "$parent" 2>/dev/null; then
			alive="still running"
		else
			alive="EXITED"
		fi
		echo "probe: after killing every worker, parent is $alive"
		ps -o pid,etime,%cpu,stat -p "$parent" 2>&1
	} > "$dir/probe.txt" 2>&1
}

killTree() {
	local parent="$1" tree
	tree="$(descendants "$parent")"
	kill -TERM $tree 2>/dev/null
	sleep 2
	tree="$(descendants "$parent")"
	[ -n "$tree" ] && kill -KILL $tree 2>/dev/null
	return 0
}

wedges=0
for i in $(seq 1 "$iterations"); do
	case "$lanes" in
		host) lane="host" ;;
		wasm) lane="wasm" ;;
		*) if [ $((i % 2)) -eq 1 ]; then lane="host"; else lane="wasm"; fi ;;
	esac

	targetArg=""
	[ "$lane" = "wasm" ] && targetArg="--target=wasm32-wasi"

	log="$outDir/run-$i-$lane.log"
	start="$(date +%s)"
	# `exec`, so $! IS the pool parent rather than a shell holding it: the capture below walks
	# CHILDREN of this pid to find the workers, and one extra shell in between would make the pool
	# parent look like the only worker.
	( cd "$repoRoot" && exec "$exe" spec-test $targetArg ) > "$log" 2>&1 &
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
		dir="$outDir/wedge-$i-$lane"
		echo "=== WEDGE on run $i ($lane) — capturing to $dir ===" | tee -a "$summary"
		capture "$runner" "$dir"
		killTree "$runner"
		wait "$runner" 2>/dev/null
		echo "run $i $lane WEDGED after ${hangSeconds}s" >> "$summary"
	else
		wait "$runner"
		code=$?
		elapsed=$(( $(date +%s) - start ))
		echo "run $i $lane exit=$code ${elapsed}s $(tail -1 "$log")" >> "$summary"
	fi
done

echo "--- $iterations runs, $wedges wedge(s) ---" | tee -a "$summary"
