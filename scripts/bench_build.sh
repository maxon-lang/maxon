#!/usr/bin/env bash
# Benchmark: cold build of maxon-selfhosted.
#
# Measures wall-clock time of `maxon.exe build maxon-selfhosted` from a
# cold state (cache deleted + binary removed) across N runs, then reports
# best/worst/median. Also reports the produced selfhosted.exe size, which
# is deterministic across runs and so gets a single reading. Both are
# primary "does an optimization change selfhosted" signals for the
# refcount-optimization roadmap.
#
# Usage: ./scripts/bench_build.sh [--runs N]
# Default runs: 3.

set -euo pipefail

runs=3
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs) runs="$2"; shift 2 ;;
    --runs=*) runs="${1#--runs=}"; shift ;;
    -h|--help)
      sed -n '2,10p' "$0" | sed 's/^# *//'
      exit 0 ;;
    *) echo "bench_build.sh: unknown arg: $1" >&2; exit 2 ;;
  esac
done

# Resolve repo root from the script's location so we can be invoked from
# anywhere. bench file paths are repo-relative.
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

if [[ ! -x bin/maxon.exe ]]; then
  echo "bench_build.sh: bin/maxon.exe not found — run 'dotnet build' in maxon-sharp first." >&2
  exit 1
fi

# Times are recorded in seconds (floating-point) and sorted numerically.
# We clear the cache before each run; the `.maxon` output dir is recreated
# by maxon itself as part of the build.
times=()
for i in $(seq 1 "$runs"); do
  rm -rf maxon-selfhosted/.maxon
  printf 'run %d/%d ... ' "$i" "$runs"
  # `time -p` prints POSIX-format real/user/sys on three lines to stderr.
  # We capture only `real` and let the build's own stdout scroll past.
  t=$({ time -p ./bin/maxon.exe build maxon-selfhosted >/dev/null 2>&1; } 2>&1 | awk '/^real/ {print $2}')
  if [[ -z "$t" ]]; then
    echo "failed (no real time captured)" >&2
    exit 1
  fi
  printf '%ss\n' "$t"
  times+=("$t")
done

# Sort ascending, pick best/median/worst.
sorted=($(printf '%s\n' "${times[@]}" | sort -n))
best=${sorted[0]}
worst=${sorted[$((runs-1))]}
median=${sorted[$((runs/2))]}

echo
printf 'runs: %d\n' "$runs"
printf 'best:   %s s\n' "$best"
printf 'median: %s s\n' "$median"
printf 'worst:  %s s\n' "$worst"

# Binary size. Emission is deterministic so a single reading is enough.
exe="maxon-selfhosted/.maxon/maxon-selfhosted.exe"
if [[ -f "$exe" ]]; then
  size=$(stat -c %s "$exe" 2>/dev/null || stat -f %z "$exe")
  printf 'exe:    %s bytes (%s)\n' "$size" "$exe"
fi
