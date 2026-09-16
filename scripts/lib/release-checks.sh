# THE PASS / FAIL / SKIPPED CONTRACT THE RELEASE CHECKS REPORT AGAINST — written ONCE, for both halves.
#
# ⛔ **A CHECK THAT DID NOT RUN IS NOT A CHECK THAT PASSED.** `release-preflight.sh` and
#    `release-postflight.sh` exist because a green result that silently skipped its subject reads
#    exactly like a green result that tested it. So a check with no tool to run reports SKIPPED, is
#    counted, and is named AGAIN in the summary — and only FAIL decides the exit status, because a
#    skip is an unknown rather than a clean answer.
#
# ⛔ **NOTHING HERE STOPS THE RUN.** A release wants the whole list; stopping at the first failure
#    turns one release into one fix per attempt.
#
# ⇒ Source this, call `check_pass` / `check_fail` / `check_skip` once per check, and end with
#   `check_summary <script name>`, whose status is the script's.

check_passes=0
check_failures=0
check_skips=0
check_failed_names=""
check_skipped_names=""

# Continuation lines sit under the reason column, so a captured tool's own words read as part of the
# row they belong to rather than as output of the script.
CheckIndent="         "

check_row() { printf '%-8s %-25s %s\n' "$1" "$2" "$3"; }

check_pass() {
	check_passes=$((check_passes + 1))
	check_row PASS "$1" "$2"
}

check_fail() {
	check_failures=$((check_failures + 1))
	check_failed_names="$check_failed_names $1"
	check_row FAIL "$1" "$2"
}

check_skip() {
	check_skips=$((check_skips + 1))
	check_skipped_names="$check_skipped_names $1"
	check_row SKIPPED "$1" "$2"
}

# A reason too long for one line.
check_note() { printf '%s%s\n' "$CheckIndent" "$1"; }

# ⭐ A FAILING TOOL'S OWN STDERR IS THE ANSWER, not a paraphrase of it: `sync-docs.mjs` names the page
# that drifted and `extension-release-gate.sh` names the commits and the version.
check_detail() {
	if [ -s "$1" ]; then
		sed "s|^|$CheckIndent|" "$1"
	fi
}

check_summary() {
	printf '\n%s: %d passed, %d failed, %d skipped\n' "$1" "$check_passes" "$check_failures" "$check_skips"

	if [ "$check_skips" -gt 0 ]; then
		printf '%s: SKIPPED tested nothing and is not a pass —%s\n' "$1" "$check_skipped_names"
	fi

	if [ "$check_failures" -gt 0 ]; then
		printf '%s: FAILED —%s\n' "$1" "$check_failed_names" >&2
		return 1
	fi

	return 0
}

# ⛔ EVERY URL, ASSET NAME AND TAG IS BUILT FROM THIS NUMBER, so a malformed one becomes a 404 that the
#    checks then report as a broken release. Prints the bare `X.Y.Z`: a leading `v` is accepted,
#    because the tag is spelled that way everywhere else, and stripped, because nothing else is.
check_release_version() {
	if ! printf '%s' "${1#v}" | grep -E '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$' >/dev/null; then
		printf '%s: "%s" is not a version — pass X.Y.Z, e.g. 0.2.2\n' "$2" "$1" >&2
		return 1
	fi

	printf '%s' "${1#v}"
}

# ⛔ AN UNAUTHENTICATED `gh` ANSWERS NOTHING AND SAYS SO ONLY ON STDERR, so a check that ran it anyway
#    would read an auth error as a verdict about the release. Prints why it cannot answer, or nothing
#    when it can.
check_gh_reason() {
	if ! command -v gh >/dev/null 2>&1; then
		printf 'the GitHub CLI (gh) is not on PATH'
	elif ! gh auth status >/dev/null 2>&1; then
		printf 'gh is on PATH but not authenticated — run: gh auth login'
	fi
}
