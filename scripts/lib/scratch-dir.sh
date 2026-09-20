# WHETHER A SCRATCH DIRECTORY SURVIVES THE RUN — written ONCE, for every script that makes one.
#
# ⛔ A NONZERO EXIT KEEPS THE DIRECTORY AND NAMES IT; A CLEAN EXIT REMOVES IT. The directory holds the
#    logs and the binaries of whatever just failed, and an unconditional `rm -rf` in an EXIT trap
#    destroys them at the one moment they are worth having — leaving a bare nonzero status with nothing
#    to diagnose it by, which costs a blind re-run to learn nothing. What is kept is under `temp/` or
#    `mktemp -d`, so the next run clears it and nothing accumulates.
#
# ⇒ Source this and install `trap 'scratch_dir_on_exit $? "$dir" "<script>" "$keep"' EXIT`. `$?` must be
#   the trap's first word, or the status it reads is the trap's own. Pass 1 for the keep flag to hold
#   the directory on a clean exit too. Every name here is prefixed `scratch_`, because a sourced file
#   has no locals.

scratch_dir_on_exit() {
	scratch_status="$1"
	scratch_dir="$2"
	scratch_label="$3"
	scratch_keep_when_clean="$4"

	if [ "$scratch_status" -ne 0 ]; then
		printf '%s: kept %s — it holds whatever this run produced\n' "$scratch_label" "$scratch_dir" >&2
		return
	fi

	if [ "$scratch_keep_when_clean" -eq 0 ]; then
		rm -rf "$scratch_dir"
	fi
}
