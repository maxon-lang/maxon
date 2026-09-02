#!/usr/bin/env bash
#
# Track-0 REFCOUNT-RACE driver (EC10) — runs `refcount-torture.maxon` REPS times
# at each MAXON_MAX_PROCS and tabulates EXIT CODES.
#
# ⭐⭐ THE EXIT CODE IS THE ONLY DISCRIMINATOR, AND THAT IS MEASURED RATHER THAN
# ASSUMED. `aggregate=` is byte-identical in a passing run and a segfaulting one
# — the answer stays deterministic and only memory management breaks — and
# `live=` tracks how many P structs bring-up allocated, so it varies with the
# processor count in passing AND failing runs alike. A driver that asserted on
# either would call a crashing build correct. See the program's header.
#
#   exit 42  = clean            (the program's own success value)
#   exit 101 = the runtime's leak gate fired   — the LOST-DECREF half
#   exit 139 = segfault                        — the LOST-INCREF half, freed early
#
# ⚠ THE RACE IS INTERMITTENT, WHICH IS WHY THIS EXISTS AND A SINGLE RUN DOES NOT.
# MEASURED at EC10 slice 2, 24 runs per cell, on the FINISHED tree with the pin
# undone at one publish site: N=2 read 101 twice out of 24 while N=4 read 101
# fifteen times and 139 nine times — 24 of 24. A one-shot driver pointed at N=2
# would have called that "fine" five times out of six.
#
# Usage:  refcount-race.sh [reps]        (default 12)
#         MAXON=<path to shv2 binary>    to drive a staged build (e.g. a parent)
#
# It RECORDS and does not assert: what a given tree SHOULD read depends on
# whether `emitAdjustRefcount` is atomic and on whether `async` is pinned, and
# both of those are the reader's question rather than this script's. The three
# measured combinations are tabulated in the program's header.

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

# ⛔ THE SHV2 BINARY, NOT `bin/maxon.exe` — `validate.sh` drives the C# bootstrap
# and measures a different compiler's runtime entirely.
# ⛔ THE DEFAULT ALREADY ENDS IN `.exe`, SO THE FALLBACK STRIPS IT — it does not add
# one. It read `MAXON="$MAXON.exe"` until W219, which on macOS/Linux (where the binary
# has NO extension) produced `maxon-shv2.exe.exe` and then reported "no shv2 binary" at
# a path that never existed on any platform. Windows took the `-x` branch and never
# reached it, which is why it survived.
MAXON="${MAXON:-$REPO/maxon-shv2/.maxon/maxon-shv2.exe}"
[ -x "$MAXON" ] || MAXON="${MAXON%.exe}"

REPS="${1:-12}"
PROCS_LIST="${PROCS_LIST:-1 2 4 12}"

# Beside the script, never under `temp/` — a `temp/` path is exactly how the
# original measurement was lost, and a bootstrap `spec-test` run deletes every
# `*.exe` under `temp/` recursively.
WORK="$HERE/.refcount-race"

if [ ! -x "$MAXON" ]; then
	echo "FAIL: no shv2 binary at $MAXON (build it, or set MAXON=)"
	exit 1
fi

mkdir -p "$WORK"
echo "compiler: $MAXON"
echo "reps:     $REPS per processor count"
echo

if ! "$MAXON" build "$HERE/refcount-torture.maxon" -o "$WORK/refcount-torture" >"$WORK/build.log" 2>&1; then
	echo "FAIL: refcount-torture did not build"
	cat "$WORK/build.log"
	exit 1
fi

printf '%6s %8s %9s %9s %7s %14s\n' procs exit42 exit101 exit139 other aggregate
printf '%6s %8s %9s %9s %7s %14s\n' ----- ------ ------- ------- ----- ---------

for N in $PROCS_LIST; do
	c42=0; c101=0; c139=0; other=0; agg="-"
	i=0
	while [ "$i" -lt "$REPS" ]; do
		out="$(MAXON_MAX_PROCS=$N "$WORK/refcount-torture" 2>/dev/null)"
		rc=$?
		case "$rc" in
			42)  c42=$((c42+1));  agg="$(printf '%s' "$out" | grep -o '^aggregate=[0-9]*' | head -1)";;
			101) c101=$((c101+1));;
			139) c139=$((c139+1));;
			*)   other=$((other+1));;
		esac
		echo "N=$N run=$((i+1)) exit=$rc | $(printf '%s' "$out" | tr '\n' ' ')" >> "$WORK/runs.log"
		i=$((i+1))
	done
	printf '%6s %8s %9s %9s %7s %14s\n' "$N" "$c42" "$c101" "$c139" "$other" "$agg"
done

echo
echo "per-run log: $WORK/runs.log"
