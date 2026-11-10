#!/usr/bin/env bash

set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
. "$REPO/scripts/lib/host-binaries.sh"

MAXON="${MAXON:-$(maxon_compiler_path "$REPO")}"
TREE="${TAG_REPORTS_TREE:-$REPO}"
WORK="$TREE/temp/tag-reports"
TRACED="$WORK/maxon-traced$MAXON_EXE_EXT"
APP="$WORK/app"
UNTAGGED_TAG="(untagged)"
MIN_DISTINCT_TAGS=3
PARSE_PHASE=parse
FAILED=0

pass() { echo "  PASS: $1"; }
bad()  { echo "  FAIL: $1"; FAILED=1; }
hdr()  { echo; echo "== $1 =="; }

trap 'rm -rf "$WORK"' EXIT

if [ ! -x "$MAXON" ]; then
	echo "FAIL: no compiler at $MAXON"
	exit 1
fi

rm -rf "$WORK"
mkdir -p "$APP"
printf "function main() returns ExitCode\n\treturn 0\nend 'main'\n" > "$APP/main.maxon"

hdr "Building the compiler with --debugstream"
if ! (cd "$TREE" && "$MAXON" build maxon-bin --debugstream --output="$TRACED") >"$WORK/build.log" 2>&1; then
	echo "FAIL: the --debugstream build of the compiler errored"
	cat "$WORK/build.log"
	FAILED=1
	exit "$FAILED"
fi
pass "built $TRACED"

run_report() {
	local label="$1" flag="$2"
	local log="$WORK/$label.log" tsv="$WORK/$label.tsv"

	(cd "$TREE" && "$TRACED" build "$APP" --log=compiler:debug "$flag" --metrics="$tsv" --output="$APP/out$MAXON_EXE_EXT") >"$log" 2>&1
	local rc=$?

	if [ "$rc" != 0 ]; then
		bad "$label: the traced compiler exited $rc under $flag"
		tail -n 20 "$log"
		return 1
	fi

	return 0
}

distinct_real_tags() {
	awk -F'\t' -v kind="$2" -v phase="$3" -v untagged="$UNTAGGED_TAG" '
		$1 == kind {
			split($2, parts, "/")
			if (parts[1] != phase) next
			tag = substr($2, length(parts[1]) + 2)
			if (tag != untagged && tag !~ /^class / && $4 > 0) seen[tag] = 1
		}
		END { n = 0; for (t in seen) n++; print n }' "$1"
}

rows_outside_phase() {
	awk -F'\t' -v kind="$2" -v phase="$3" '
		$1 == kind { split($2, parts, "/"); if (parts[1] != phase) n++ }
		END { print n + 0 }' "$1"
}

tallied_total() {
	sed -n 's/.*Σ over the sampled phases and buckets = \([0-9]*\).*/\1/p' "$1" | head -n 1
}

require_real_tags() {
	local label="$1" kind="$2" tags

	tags="$(distinct_real_tags "$WORK/$label.tsv" "$kind" "$PARSE_PHASE")"

	if [ "$tags" -ge "$MIN_DISTINCT_TAGS" ]; then
		pass "$label: $tags distinct real tags in the $kind rows of $PARSE_PHASE"
	else
		bad "$label: only $tags distinct real tags in the $kind rows of $PARSE_PHASE; the report is untagged or empty"
	fi
}

require_agreeing_self_check() {
	local label="$1" log="$WORK/$1.log" total

	total="$(tallied_total "$log")"

	if ! grep -aq ' — equal' "$log" || grep -aq 'NOT equal' "$log"; then
		bad "$label: the self-check does not read equal"
	elif [ -z "$total" ] || [ "$total" -le 0 ]; then
		bad "$label: the self-check agrees over nothing (the sampled sum is ${total:-missing})"
	else
		pass "$label: the self-check reads equal over $total tallied allocations"
	fi
}

hdr "--allocations-by-tag, every phase"
if run_report every-phase --allocations-by-tag; then
	require_real_tags every-phase churn
	require_agreeing_self_check every-phase
fi

hdr "--allocations-by-tag=$PARSE_PHASE"
if run_report named-parse --allocations-by-tag=$PARSE_PHASE; then
	require_real_tags named-parse churn
	require_agreeing_self_check named-parse

	outside="$(rows_outside_phase "$WORK/named-parse.tsv" churn "$PARSE_PHASE")"

	if [ "$outside" = 0 ]; then
		pass "named-parse: no churn row belongs to another phase"
	else
		bad "named-parse: $outside churn rows belong to a phase other than $PARSE_PHASE"
	fi
fi

hdr "--census-by-tag=$PARSE_PHASE"
if run_report census-parse --census-by-tag=$PARSE_PHASE; then
	require_real_tags census-parse tag

	if grep -aq "the whole table at $PARSE_PHASE, " "$WORK/census-parse.log" && ! grep -aq 'was not censused' "$WORK/census-parse.log"; then
		pass "census-parse: the report prints the $PARSE_PHASE table"
	else
		bad "census-parse: the report prints no $PARSE_PHASE table"
	fi
fi

echo
if [ "$FAILED" = 0 ]; then
	echo "ALL CHECKS PASSED"
else
	echo "SOME CHECKS FAILED"
fi

exit "$FAILED"
