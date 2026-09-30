#!/usr/bin/env bash

set -euo pipefail

[ "$#" -eq 1 ] && [[ "$1" =~ ^[0-9]+$ ]] || { echo "usage: scripts/fetch-goldens.sh <run-id>" >&2; exit 2; }

run_id="$1"

if [ ! -f scripts/lib/goldens.sh ] || [ -n "$(git rev-parse --show-prefix 2>/dev/null || echo outside)" ]; then
	echo "fetch-goldens.sh: run this from the repository root" >&2
	exit 2
fi

. scripts/lib/goldens.sh
. scripts/lib/scratch-dir.sh

mkdir -p temp
work="$(mktemp -d "temp/goldens-$run_id.XXXXXX")"
trap 'scratch_dir_on_exit $? "$work" "fetch-goldens.sh" 0' EXIT

gh run download "$run_id" --pattern "${GOLDENS_ARTIFACT_PREFIX}*" -D "$work"

placed="$(goldens_place "$work" .)"

echo "fetch-goldens.sh: placed the goldens of run $run_id under $GOLDENS_ROOT"
printf '%s\n' "$placed" | goldens_tally
echo "fetch-goldens.sh: nothing outside $GOLDENS_ROOT was touched, and nothing was staged or committed"
