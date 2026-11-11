#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/scratch-dir.sh
. scripts/lib/test-corpora.sh

script="run-test-corpora.sh"
job="${1:-}"

case " $TestCorporaJobs " in
	*" $job "*) ;;
	*) echo "$script: usage: $script <job>, one of: $TestCorporaJobs" >&2; exit 2 ;;
esac

compiler="$(maxon_compiler_path .)"
[ -x "$compiler" ] || { echo "$script: no compiler at $compiler — run \`./maxon-bin/.maxon/maxon run build\` at the repo root" >&2; exit 1; }

lane="$(maxon_host_target)"
out="temp/run-test-corpora"
rm -rf "$out"
mkdir -p "$out"

trap 'scratch_dir_on_exit $? "$out" "$script" 0' EXIT

deadline_command=""

for candidate in timeout gtimeout; do
	if [ -z "$deadline_command" ] && command -v "$candidate" > /dev/null 2>&1; then
		deadline_command="$candidate"
	fi
done

if [ -z "$deadline_command" ]; then
	echo "$script: this host has neither timeout nor gtimeout, so a corpus's declared timeout-minutes is not enforced; the step's own timeout bounds the job"
fi

with_deadline() {
	minutes="$1"
	shift

	if [ -n "$deadline_command" ]; then
		"$deadline_command" "${minutes}m" "$@"
	else
		"$@"
	fi
}

count_reported() {
	awk -v word="$2" '{ sub(/\r$/, "") } $0 ~ "^ *[0-9]+ " word "$" { count = $1 } END { print count + 0 }' "$1"
}

all_corpora="$(test_corpora_discover)"

if [ -z "$all_corpora" ]; then
	echo "$script: no tracked directory under tests/ holds a .maxtest file" >&2
	exit 1
fi

selected=""

for corpus in $all_corpora; do
	corpus_job="$(test_corpus_job "$corpus")"

	if [ "$corpus_job" = "$job" ]; then
		selected="$selected $corpus"
	fi
done

if [ -z "$selected" ]; then
	echo "$script: no corpus declares job $job, so this job would run nothing" >&2
	exit 1
fi

echo "job $job on $lane: tests/ holds: $(printf '%s' "$all_corpora" | tr '\n' ' ')"
echo "this job's corpora:$selected"

summary="$out/summary.txt"
red=0

record() {
	printf '%-20s %-10s %6s %6s %6ss  %s\n' "$1" "$2" "$3" "$4" "$5" "$6" >> "$summary"
}

printf '%-20s %-10s %6s %6s %7s  %s\n' corpus verdict pass fail time note > "$summary"

for corpus in $selected; do
	log="$out/$corpus.log"
	unmet="$(test_corpus_unmet_requirements "$corpus" "$lane")"
	started="$SECONDS"

	if [ -n "$unmet" ]; then
		echo "::group::tests/$corpus is built and not run on $lane: this lane does not satisfy $unmet"
		build_status=0
		"$compiler" test "tests/$corpus" --list --build > "$log" 2>&1 || build_status=$?
		echo "::endgroup::"

		if [ "$build_status" -ne 0 ]; then
			red=1
			echo "::error title=tests/$corpus did not build::exit $build_status"
			cat "$log"
			record "$corpus" BUILD-FAIL - - "$((SECONDS - started))" "built only; lane lacks $unmet"
		else
			record "$corpus" built - - "$((SECONDS - started))" "not run: lane lacks $unmet"
		fi

		continue
	fi

	minutes="$(test_corpus_timeout_minutes "$corpus")"
	echo "::group::tests/$corpus (declared timeout $minutes min)"

	set +e
	with_deadline "$minutes" "$compiler" test "tests/$corpus" 2>&1 | tee "$log"
	status="${PIPESTATUS[0]}"
	set -e

	echo "::endgroup::"

	passes="$(count_reported "$log" pass)"
	fails="$(count_reported "$log" fail)"
	elapsed="$((SECONDS - started))"
	verdict=""
	note=""

	if [ "$status" -eq 124 ]; then
		verdict="TIMED-OUT"
		note="no result within $minutes min"
	elif [ "$status" -ne 0 ]; then
		verdict="FAIL"
		note="exit $status"
	elif [ "$fails" -ne 0 ]; then
		verdict="FAIL"
		note="exit 0 yet $fails failed"
	elif [ "$passes" -eq 0 ]; then
		verdict="NONE-RAN"
		note="exit 0 yet no pass count reported"
	else
		verdict="ok"
	fi

	if [ "$verdict" != "ok" ]; then
		red=1
		echo "::error title=tests/$corpus $verdict::$note"

		if grep -q '^FAIL ' "$log"; then
			sed -n '/^FAIL /,$p' "$log"
		else
			tail -n 80 "$log"
		fi
	fi

	record "$corpus" "$verdict" "$passes" "$fails" "$elapsed" "$note"
done

echo
cat "$summary"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
	{
		echo "### Test corpora, job $job on $lane"
		echo
		echo '```'
		cat "$summary"
		echo '```'
	} >> "$GITHUB_STEP_SUMMARY"
fi

if [ "$red" -ne 0 ]; then
	echo "$script: a corpus above is red; each one's log is in $out/" >&2
	exit 1
fi
