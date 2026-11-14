# THE LOOP A SCRIPT RUNS OVER ITS SUBJECTS — written ONCE: run a command per subject, log it, show a
# failing subject's output, keep going after a failure, and name every failure at the end, so one run
# reports all of them.
#
# Bash, and called in the script's own shell, never a subshell, because the failures accumulate in a
# variable. Set `SubjectScript` (the script's name), `SubjectDone` (the verb of a success line) and
# `SubjectRefusal` (the phrase naming a failure), then call `subject_run <subject> <log> <command...>` for
# each subject and `subject_report` once after the last; it exits 1 when any subject failed.

subject_failures=""

subject_run() {
	local subject="$1" log="$2" subject_status=0
	shift 2

	"$@" > "$log" 2>&1 || subject_status=$?

	if [ "$subject_status" -ne 0 ]; then
		printf '%s: %s %s (exit %s) — its output follows\n' "$SubjectScript" "$subject" "$SubjectRefusal" "$subject_status" >&2
		cat "$log" >&2
		subject_failures="$subject_failures $subject"
		return 0
	fi

	echo "$SubjectDone $subject"
}

subject_report() {
	if [ -n "$subject_failures" ]; then
		echo "$SubjectScript: $SubjectRefusal:$subject_failures" >&2
		exit 1
	fi
}
