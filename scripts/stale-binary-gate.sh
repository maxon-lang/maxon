#!/usr/bin/env bash
#
# THE STALE-BINARY GATE — does a harness command REFUSE to report on a binary older than its sources?
#
# A `spec-test` run on a stale binary reports a verdict about code that is not in the tree. It can be
# a false RED — measured during rung A1, exactly 4 tests, which is what identified it as staleness —
# and it can just as easily be a false GREEN, which is the same lie in the direction nobody checks.
# The same hazard reaches `scale-test`: a whole ladder read off the previous binary, which is how this
# rung got filed.
#
# The window is one keystroke wide. `build; suite` (`;`, not `&&`) runs the suite whatever the build
# did, and a failed build LEAVES THE OLD BINARY IN PLACE — see `output-lock-gate.sh`, which gates the
# one refusal that says so out loud. So the harness has to check for itself.
#
# ============================================================================================
# WHY THIS IS A SHELL GATE AND NOT A SPEC CASE
# ============================================================================================
#
# A spec is a Maxon program plus an expected exit code or stderr. No program can make the binary
# compiling it older than the tree around it: the property lives in the FILESYSTEM, in mtimes the
# harness reads before any test is selected. Same reason `output-lock-gate.sh` and
# `worker-exe-split-gate.sh` are scripts.
#
# ============================================================================================
# THE EXPERIMENT — AND WHY EACH CONTROL IS LOAD-BEARING
# ============================================================================================
#
# The refusal is easy to fake: a harness that refused every run would pass a check that only ever
# looked for the refusal. So the discriminating shape is a MATRIX over two independent facts —
# whether a compiled-in source is newer than the binary, and whether the sources sit beside it:
#
#   CHECK 1  clean tree, in-tree binary            -> RUNS      (nothing to refuse)
#   CHECK 2  newer source, in-tree binary          -> REFUSES   (this is the gate)
#   CHECK 3  newer source, binary with no sources  -> RUNS      (a RELEASED copy: ruling 1)
#   CHECK 4  newer source, binary in a bare cache  -> RUNS      (same, one directory shape down)
#   CHECK 5  newer EXCLUDED source, in-tree binary -> RUNS      (it is not in the binary), three times:
#            5a  under a `.maxonignore`d directory
#            5b  inside the compiler's own `.maxon/` output directory
#            5c  a `*.Test.maxon` — the suffix matched the way the BOOTSTRAP matches it
#   CHECK 6  restored tree, in-tree binary         -> RUNS      (the refusal was the edit)
#
# CHECKS 3 and 4 are the ones that cost something to get right, and they are why the copies are made
# with `cp -p`: a copy with a FRESH mtime would be newer than every source and would run for a reason
# that has nothing to do with the rule under test. Copied mtime-preserving, each copy is exactly as
# stale as the in-tree binary CHECK 2 just refused, so staleness is held FIXED across 2/3/4 and only
# the binary's surroundings vary. They vary in two steps, and the labels say which: CHECK 3 moves the
# binary out of a `.maxon/` directory entirely (an installed release), CHECK 4 puts it back INTO one
# with no project above it (the same release, one directory shape closer to the real thing) — which
# is the case that reaches the "are this project's sources actually here" question rather than
# stopping at "was this built in place".
#
# CHECK 5 is the false-refusal control, and it is not a hypothetical: `maxon build` does not compile
# `.maxonignore`d directories, `*.test.maxon` files, or anything inside its OWN `.maxon/` output
# directory, so none of those are "sources of this binary". 5b is the sharp one — that directory is
# where the build writes, so every file in it is newer than the binary beside it BY CONSTRUCTION, and
# a check that swept it would refuse every run forever. 5c is the one an eye passes over: the
# bootstrap matches that suffix `OrdinalIgnoreCase`, and the first version of this check matched it
# byte-exactly, so a `*.Test.maxon` was excluded from the build and counted as a source of it at the
# same time — refusing the suite over a file the compiler had never read. Both were found by REVIEW,
# after the tree was green.
#
# ⚠ VERIFIED TO GO RED, twice, and the two reds are different shapes:
#   * against the binary built from this script's parent commit — the tree with no check in it at all
#     — CHECK 2 fails (`exit=0`, `4 passed, 0 failed`: the suite ran a whole filtered pass off the
#     stale binary) and CHECK 7 fails with it, while 1, 3, 4, 5, 6 pass. That split is the
#     discriminator: everything that should RUN already ran; what did not exist was the refusal.
#   * against the binary built from the commit that ADDED the refusal, CHECK 5c fails on its own
#     (`exit=2`, naming `maxon-shv2/Testing/Probe.Test.maxon`) — a check present, working, and one
#     clause narrower than the rule it mirrors. A refusal is not enough; it has to refuse the right
#     files.
#
# ⚠ WHAT THIS CANNOT CATCH, and the message must not claim otherwise: the check ships INSIDE the
#   binary it reports on, so a stale binary runs the OLD check. That is fine for the case that
#   matters (edit a source, forget to rebuild — the old check reads the new mtimes and refuses) and it
#   is structurally blind to a change to the checking code itself. mtime is also blind to a DELETED
#   source: nothing gets newer when a file goes away.
#
# Usage:  scripts/stale-binary-gate.sh
# Exit:   0 = all checks pass · 1 = a check failed · 2 = the gate could not run (setup failure)

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 2

# shellcheck source=lib/host-binaries.sh
. "$REPO_ROOT/scripts/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 2; }
EXE_EXT="$MAXON_EXE_EXT"

SHV2="$(maxon_shv2_path .)"
WORK="temp/stale-binary-gate"

# The exit code the harness returns when it refuses. Distinct from 1 (a suite that RAN and had
# failures) and from 101 (a leak) on purpose: "nothing ran" and "something failed" are different
# facts, and a caller reading only the code must be able to tell them apart.
STALE_EXIT=2

# A source that IS compiled into the binary — any one would do, so it is the driver, which nobody
# could mistake for a fixture. Only its MTIME is touched; its bytes are never read or written, so the
# working tree stays clean throughout.
VICTIM="maxon-shv2/Main.maxon"

# A `.maxon` file under the project root that `maxon build` deliberately leaves OUT: `track0/` carries
# a `.maxonignore`, so its program is not in the binary and cannot make it stale.
EXCLUDED="maxon-shv2/track0/alloc-torture.maxon"

# A cheap slice of the suite, nothing interesting: every check below is about whether the harness ran
# AT ALL, so the tests themselves must be among the cheapest there are.
#
# ⚠ THE EXPECTED SUMMARY IS NOT WRITTEN DOWN — it is what CHECK 1 OBSERVES, and the rest of the matrix
# is compared against that. It WAS a literal `4 passed, 0 failed`, and that literal was a COUNT OF THE
# CORPUS pinned inside a gate that is not about the corpus. shv2's `--filter` is a SUBSTRING match on
# `<spec>/<test>` (SpecTestRunner.maxon), so `arithmetic/` selects `wide-immediate-arithmetic/` too —
# and when that spec landed on 2026-08-04 the count went 4 -> 10 and this gate went red for a reason
# with nothing to do with stale binaries. Nobody saw it for two days, because `buildall.sh` was dying
# four steps earlier.
#
# ⚖ WHAT DERIVING IT COSTS, STATED HONESTLY (BATCH29 review, 2026-08-06 — the first spelling of this
# comment claimed the derivation was "STRONGER, not weaker", which is true of one direction only):
#   * STRONGER against a DIFFERENTIAL. CHECKS 3-6 now assert the same run the control produced, so a
#     later run that silently executes a DIFFERENT number of tests than the control is caught. A
#     literal could not see that; it only ever knew one number.
#   * WEAKER against an ABSOLUTE. If the CONTROL itself runs fewer tests — a filter that stops
#     selecting a spec, a corpus that shrinks — every check agrees with a shrunken baseline and the
#     whole matrix is green. That is the one thing the literal did catch, and it is given up here.
#   * ⇒ AND IT CANNOT REACH THIS GATE'S OWN SUBJECT. The checks that decide whether the refusal works
#     — 2a/2b/2c and 7 — compare against the fixed $STALE_EXIT, an EMPTY summary and the message's own
#     words. None of them reads $EXPECTED_SUMMARY. So a poisoned baseline can weaken the RUNS-normally
#     controls and can never manufacture a passing refusal.
FILTER="arithmetic/"

FAILED=0

pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n         saw: %s\n' "$1" "$2"; FAILED=1; }

if [ ! -x "$SHV2" ]; then
	printf 'stale-binary-gate: %s is missing — build it first (`scripts/build-shv2.sh`)\n' "$SHV2" >&2
	exit 2
fi

# `find -newer` rather than `stat`, for the same reason `cross-target-gate.sh` uses it: GNU and BSD
# `stat` take opposite flags for the one field wanted here, and `-newer` is one portable spelling of
# the only question ever asked of it.
is_newer_than_binary() { [ -n "$(find "$1" -newer "$SHV2" -print -quit 2>/dev/null)" ]; }

# `cp -p` keeps the mtime, so the backup is a timestamp reference as well as a content one — and
# `touch -r` puts BOTH halves back. Nothing here edits the file's bytes.
#
# ⚠ An aged mtime that OUTLIVES this script refuses every `spec-test` and `scale-test` in the tree
# until the next rebuild — this gate would hand the repo the exact failure it exists to detect. The
# `trap` below covers every exit including INT and TERM; what it cannot cover is SIGKILL, so the
# restore is ALSO attempted from a previous run's backups before this run overwrites them.
VICTIM_BACKUP="$WORK/victim.bak"
EXCLUDED_BACKUP="$WORK/excluded.bak"

# A throwaway `.maxon` file inside the compiler's own output directory, for CHECK 5b. That directory
# is a build artifact and is gitignored, so nothing here can dirty the working tree.
SCRATCH="maxon-shv2/.maxon/stale-binary-gate-scratch.maxon"

# CHECK 5c's file, and it must sit in a directory the source walk DOES sweep — the exclusion under
# test is the SUFFIX, so putting it anywhere already excluded would prove nothing. `Test` is
# capitalised deliberately: the bootstrap matches the suffix case-insensitively, and this file is
# what makes the mirror match it the same way. Untracked in a tracked directory, so `cleanup` must
# remove it or the working tree comes back dirty.
TEST_SCRATCH="maxon-shv2/Testing/stale-binary-gate-scratch.Test.maxon"

restore_mtimes() {
	[ -f "$VICTIM_BACKUP" ] && touch -r "$VICTIM_BACKUP" "$VICTIM" 2>/dev/null
	[ -f "$EXCLUDED_BACKUP" ] && touch -r "$EXCLUDED_BACKUP" "$EXCLUDED" 2>/dev/null
	return 0
}

# Neither the aged mtimes nor the scratch files may outlive this script: a stranded `.maxon` file in
# the output directory would be a permanent refusal on a tree nobody had edited, which is the exact
# failure CHECK 5b exists to make impossible.
cleanup() {
	restore_mtimes
	rm -f "$SCRATCH" "$TEST_SCRATCH"
	return 0
}

trap cleanup EXIT INT TERM

# BEFORE the wipe, not after: `$WORK` is where a previous run's pristine mtimes are recorded, and
# `rm -rf` would take them with it — leaving this run's own `cp -p` to record an AGED mtime as the
# original and restore the tree to a state that keeps refusing.
restore_mtimes

rm -rf "$WORK" || exit 2
mkdir -p "$WORK" || exit 2

for required in "$VICTIM" "$EXCLUDED"; do
	if [ ! -f "$required" ]; then
		printf 'stale-binary-gate: %s is not there — the gate has nothing to age\n' "$required" >&2
		exit 2
	fi
done

cp -p "$VICTIM" "$VICTIM_BACKUP" || exit 2
cp -p "$EXCLUDED" "$EXCLUDED_BACKUP" || exit 2

# Make a file newer than the binary AT THE RESOLUTION THE HARNESS READS — which is not the
# resolution `find` compares at, and the difference is the whole reason for the trailing sleep.
#
# `find -newer` compares full filesystem precision; `File.info().modifiedTime` is Unix epoch SECONDS,
# and a tie in whole seconds is not "newer". So on a binary written at T.900 and a `touch` landing at
# T.950, `find` says newer and the harness says tie. Sleeping one full second past a moment already
# known to be at-or-after the binary's, then touching again, closes it for good: the new mtime is at
# least binary+1s, whose whole-second value is strictly greater whatever the sub-second parts were.
#
# It is not merely a flake in CHECK 2. It is worse in 5a/5b/5c, where the expected outcome is that
# the suite RUNS: a tie there would let every false-refusal control PASS without ever having created
# the condition it controls for — a green that measured nothing.
age_forward() {
	local target="$1"
	local tries=0
	touch "$target" || return 1
	while ! is_newer_than_binary "$target"; do
		tries=$((tries + 1))
		[ "$tries" -gt 5 ] && return 1
		sleep 1
		touch "$target" || return 1
	done

	sleep 1
	touch "$target" || return 1
	return 0
}

run_suite() {
	local exe="$1"
	shift
	"$exe" spec-test "--filter=$FILTER" "$@" > "$WORK/out.log" 2>&1
	echo "$?"
}

summary() { grep -E '^[0-9]+ passed, [0-9]+ failed' "$WORK/out.log" | tail -1; }

printf '=== stale-binary gate ===\n'
printf '  binary:   %s\n' "$SHV2"
printf '  victim:   %s (mtime only)\n' "$VICTIM"
printf '  excluded: %s (mtime only)\n' "$EXCLUDED"
printf '\n'

# ---- CHECK 1: the clean tree still runs -----------------------------------------------------------
#
# The negative control for everything below. A harness that refused unconditionally would score a
# perfect green on the refusal checks alone, and this is what makes that impossible.
code="$(run_suite "$SHV2")"
got="$(summary)"
# The control DEFINES the expected summary for every later check (see FILTER). It can only assert what
# is true of a clean run whatever the corpus holds: it exited 0, it printed a summary, and nothing in
# it failed. A `0 passed` would satisfy the first two and is refused — that is a filter matching
# nothing, which would make every check below a comparison between two empty runs.
EXPECTED_SUMMARY="$got"
if [ "$code" = "0" ] && [ -n "$got" ] && [ "${got#0 passed}" = "$got" ] && [ "${got% 0 failed}" != "$got" ]; then
	pass "CHECK 1: a tree with nothing newer than the binary runs normally ($got)"
else
	fail "CHECK 1: a tree with nothing newer than the binary runs normally" "exit=$code; ${got:-no summary line}"
	printf '\nstale-binary-gate: the baseline run failed; every check below would measure the wrong thing.\n' >&2
	printf 'stale-binary-gate: see %s\n' "$WORK/out.log" >&2
	exit 2
fi

# ---- CHECK 2: THIS IS THE GATE --------------------------------------------------------------------
if ! age_forward "$VICTIM"; then
	printf 'stale-binary-gate: could not make %s newer than %s\n' "$VICTIM" "$SHV2" >&2
	exit 2
fi

code="$(run_suite "$SHV2")"
got="$(summary)"

if [ "$code" = "$STALE_EXIT" ]; then
	pass "CHECK 2a: a source newer than the binary is REFUSED, with the distinct exit $STALE_EXIT"
else
	fail "CHECK 2a: a source newer than the binary is REFUSED, with the distinct exit $STALE_EXIT" \
	     "exit=$code  <-- $( [ "$code" = "0" ] && echo 'the suite reported a verdict about code that is not in this binary' || echo 'refused, but not distinguishably from a failing suite' )"
fi

if [ -z "$got" ]; then
	pass "CHECK 2b: and no test ran — the refusal is BEFORE the suite, not a note beside it"
else
	fail "CHECK 2b: and no test ran — the refusal is BEFORE the suite, not a note beside it" \
	     "$got  <-- a warning inside 1,500 lines of PASS output is a warning nobody reads"
fi

# The message earns its place the same way E6002's does: name the binary, name the newer file, say
# what to do. A refusal that says only "STALE" sends the reader to re-derive all three.
if grep -q 'STALE' "$WORK/out.log" \
	&& grep -q 'Main\.maxon' "$WORK/out.log" \
	&& grep -q 'maxon-shv2\.exe\|maxon-shv2$\|\.maxon[/\\]maxon-shv2' "$WORK/out.log" \
	&& grep -q 'build-shv2\.sh' "$WORK/out.log"; then
	pass "CHECK 2c: the message names the binary, names the newer source, and says how to fix it"
else
	fail "CHECK 2c: the message names the binary, names the newer source, and says how to fix it" \
	     "$(head -4 "$WORK/out.log" | tr '\n' '|')"
fi

# ---- CHECK 3: a RELEASED binary has no sources beside it, and must still run -----------------------
#
# `cp -p`: the copy keeps the ORIGINAL mtime, so it is exactly as old as the binary CHECK 2 just
# refused. Same tree, same aged source, same staleness — opposite verdict, and the variable that
# moved is where the binary lives. A copy with a fresh mtime would run for a reason unrelated to the
# rule.
#
# ⚠ This one stops at the FIRST question the harness asks — the copy's parent is not a `.maxon/`
# output directory, so it was not built in place and has no project above it by definition. That is
# a real shape (an installed release on a PATH) and it must run, but it does NOT exercise the
# source-tree detection; CHECK 4 is what reaches that.
#
# It lives under `temp/` rather than the system temp directory because the stdlib is located by
# walking UP from the executable's own path, so a runnable copy has to be inside the checkout.
mkdir -p "$WORK/released" || exit 2
RELEASED="$WORK/released/maxon-shv2${EXE_EXT}"
cp -p "$SHV2" "$RELEASED" || exit 2

code="$(run_suite "$RELEASED")"
got="$(summary)"
if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
	pass "CHECK 3: a binary with no source tree beside it runs normally ($got)"
else
	fail "CHECK 3: a binary with no source tree beside it runs normally" \
	     "exit=$code; ${got:-no summary line}  <-- a released copy has nothing to compare against and must never refuse"
fi

# ---- CHECK 4: ... including one that IS in a build-output directory --------------------------------
#
# One directory shape further in: this copy sits in a `.maxon/` cache like the real one, so it gets
# past any test that only asks "was this built in place?" and reaches the question that actually
# decides it — whether the project's sources are there to compare against.
mkdir -p "$WORK/released-cache/.maxon" || exit 2
RELEASED_CACHE="$WORK/released-cache/.maxon/maxon-shv2${EXE_EXT}"
cp -p "$SHV2" "$RELEASED_CACHE" || exit 2

code="$(run_suite "$RELEASED_CACHE")"
got="$(summary)"
if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
	pass "CHECK 4: a binary in a build-output dir with no project sources above it runs normally ($got)"
else
	fail "CHECK 4: a binary in a build-output dir with no project sources above it runs normally" \
	     "exit=$code; ${got:-no summary line}"
fi

# ---- CHECK 5: FALSE-REFUSAL CONTROL — a source the build does not compile ---------------------------
#
# `track0/` carries a `.maxonignore`, so `maxon build maxon-shv2` never reads its program and no edit
# to it can change what the binary does. Refusing over one would wedge the whole suite over a file
# that is not in the binary — the same class of wrong answer as running on a stale one, arrived at
# from the other side.
restore_mtimes

if ! age_forward "$EXCLUDED"; then
	printf 'stale-binary-gate: could not make %s newer than %s\n' "$EXCLUDED" "$SHV2" >&2
	exit 2
fi

code="$(run_suite "$SHV2")"
got="$(summary)"
if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
	pass "CHECK 5a: a newer source under a .maxonignore'd directory does not refuse ($got)"
else
	fail "CHECK 5a: a newer source under a .maxonignore'd directory does not refuse" \
	     "exit=$code; ${got:-no summary line}  <-- $EXCLUDED is not compiled into the binary"
fi

restore_mtimes

# ---- CHECK 5b: ... and neither does one in the compiler's OWN output directory ----------------------
#
# The build's directory walk goes straight into `<project>/.maxon/`, where the binary and its cache
# live. Nothing the compiler WROTE is a source, and everything in there is newer than the binary
# beside it by construction — so a sweep that did not exclude it would refuse every run, forever, on
# a tree nobody had edited.
: > "$SCRATCH" || exit 2
if ! age_forward "$SCRATCH"; then
	printf 'stale-binary-gate: could not make %s newer than %s\n' "$SCRATCH" "$SHV2" >&2
	exit 2
fi

code="$(run_suite "$SHV2")"
got="$(summary)"
rm -f "$SCRATCH"

if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
	pass "CHECK 5b: a newer .maxon file inside the build-output directory does not refuse ($got)"
else
	fail "CHECK 5b: a newer .maxon file inside the build-output directory does not refuse" \
	     "exit=$code; ${got:-no summary line}  <-- the compiler's own output is not its source"
fi

# ---- CHECK 5c: ... and neither does a *.Test.maxon, matched the BOOTSTRAP's way ----------------------
#
# The suffix is matched `OrdinalIgnoreCase` by `SourceCollector.IsTestFile`, so `Api.Test.maxon` is
# left out of the build on every platform — while a byte-exact mirror calls it a source of the very
# binary it was excluded from. MEASURED against the first version of this check: `exit=2`, naming
# `maxon-shv2/Testing/Probe.Test.maxon`, with the bootstrap reporting `Skipped 1 test file(s)` for
# the same name in the same tree. The file goes in `Testing/` — a directory the walk really does
# sweep — because the exclusion under test is the SUFFIX and nothing else.
: > "$TEST_SCRATCH" || exit 2
if ! age_forward "$TEST_SCRATCH"; then
	printf 'stale-binary-gate: could not make %s newer than %s\n' "$TEST_SCRATCH" "$SHV2" >&2
	exit 2
fi

code="$(run_suite "$SHV2")"
got="$(summary)"
rm -f "$TEST_SCRATCH"

if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
	pass "CHECK 5c: a newer *.Test.maxon does not refuse — the suffix is matched case-insensitively ($got)"
else
	fail "CHECK 5c: a newer *.Test.maxon does not refuse — the suffix is matched case-insensitively" \
	     "exit=$code; ${got:-no summary line}  <-- the bootstrap excludes *.test.maxon OrdinalIgnoreCase, so this file is not in the binary"
fi

# ---- CHECK 6: and the tree comes back ---------------------------------------------------------------
#
# Proves the refusal in CHECK 2 was caused by the edit and by nothing that outlived it — the same
# thing a red-then-green pair proves, inside one run.
restore_mtimes

if is_newer_than_binary "$VICTIM" || is_newer_than_binary "$EXCLUDED"; then
	fail "CHECK 6: the restored tree runs again" "a touched file's mtime did not come back — 'touch -r' left it newer than the binary"
else
	code="$(run_suite "$SHV2")"
	got="$(summary)"
	if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SUMMARY" ]; then
		pass "CHECK 6: the restored tree runs again ($got)"
	else
		fail "CHECK 6: the restored tree runs again" "exit=$code; ${got:-no summary line}"
	fi
fi

# ---- CHECK 7: scale-test refuses too ----------------------------------------------------------------
#
# The incident that filed this rung was a whole scaling LADDER read off the previous binary, not a
# suite — so the command that produces those numbers has to refuse on the same terms. It is checked
# with the ladder's smallest possible shape because the refusal happens before a single rung is
# generated; if it ever stops refusing, this check turns into a real (slow) scale run and says so.
if ! age_forward "$VICTIM"; then
	printf 'stale-binary-gate: could not make %s newer than %s\n' "$VICTIM" "$SHV2" >&2
	exit 2
fi

"$SHV2" scale-test --rungs=1 > "$WORK/scale.log" 2>&1
code=$?
restore_mtimes

if [ "$code" = "$STALE_EXIT" ] && grep -q 'STALE' "$WORK/scale.log"; then
	pass "CHECK 7: scale-test refuses on the same terms (exit $code)"
else
	fail "CHECK 7: scale-test refuses on the same terms" \
	     "exit=$code  <-- a ladder measured on a stale binary is the incident that filed this rung"
fi

printf '\n'

if [ "$FAILED" -eq 0 ]; then
	printf 'stale-binary gate: PASS\n'
	exit 0
fi

printf 'stale-binary gate: FAIL — logs in %s\n' "$WORK"
exit 1
