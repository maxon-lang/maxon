TestCorporaDeclarationFile="ci.txt"
TestCorporaJobs="corpora tools"
TestCorporaDefaultJob="corpora"
TestCorporaDefaultTimeoutMinutes=60

test_corpora_discover() {
	git ls-files -- tests | sed -n 's|^tests/\([^/]*\)/[^/]*\.maxtest$|\1|p' | LC_ALL=C sort -u
}

test_corpus_declaration() {
	declaration="tests/$1/$TestCorporaDeclarationFile"

	[ -f "$declaration" ] || return 0

	awk -v file="$declaration" -v jobs="$TestCorporaJobs" '
		function fail(message) {
			print "test-corpora.sh: " file ":" NR ": " message > "/dev/stderr"
			failed = 1
			exit 1
		}

		function once(key) {
			if (key in seen) {
				fail("`" key "` is declared twice")
			}

			seen[key] = 1
		}

		{ sub(/\r$/, "") }

		NF == 0 { next }

		$1 == "job" {
			if (NF != 2) {
				fail("`job` takes one job name")
			}

			once("job")
			job_count = split(jobs, known, " ")
			found = 0

			for (i = 1; i <= job_count; i++) {
				if (known[i] == $2) {
					found = 1
				}
			}

			if (!found) {
				fail("`job " $2 "` names no job; the jobs are: " jobs)
			}

			print $1, $2
			next
		}

		$1 == "requires" {
			if (NF != 2 || $2 !~ /^[^=]+=[^=]+$/) {
				fail("`requires` takes one COLUMN=VALUE selector over the columns of maxon-bin/targets.tsv")
			}

			print $1, $2
			next
		}

		$1 == "timeout-minutes" {
			if (NF != 2 || $2 !~ /^[1-9][0-9]*$/) {
				fail("`timeout-minutes` takes one whole number of minutes")
			}

			once("timeout-minutes")
			print $1, $2
			next
		}

		{ fail("`" $1 "` is not a declaration; the keys are job, requires and timeout-minutes") }

		END {
			if (failed) {
				exit 1
			}
		}
	' "$declaration"
}

test_corpus_values() {
	declared="$(test_corpus_declaration "$1")" || return 1

	printf '%s\n' "$declared" | awk -v key="$2" '$1 == key { print $2 }'
}

test_corpus_job() {
	job="$(test_corpus_values "$1" job)" || return 1

	printf '%s' "${job:-$TestCorporaDefaultJob}"
}

test_corpus_timeout_minutes() {
	minutes="$(test_corpus_values "$1" timeout-minutes)" || return 1

	printf '%s' "${minutes:-$TestCorporaDefaultTimeoutMinutes}"
}

test_corpus_requirements() {
	test_corpus_values "$1" requires | paste -s -d ' ' -
}

test_corpus_unmet_requirements() {
	requirements="$(test_corpus_requirements "$1")" || return 1
	unmet=""

	for requirement in $requirements; do
		met="$(maxon_table_select target "target=$2" "$requirement")" || return 1

		if [ -z "$met" ]; then
			unmet="$unmet $requirement"
		fi
	done

	printf '%s' "${unmet# }"
}
