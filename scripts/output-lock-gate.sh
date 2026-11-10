#!/usr/bin/env bash
#
# THE OUTPUT-LOCK GATE — when a build CANNOT replace its own output, does it say so usefully?
#
# `E6002` is the one diagnostic in this compiler that no spec case can ever reach, and the reason is
# structural rather than incidental:
#
#   * Its ordinary road is the refusal arm of `Compiler.requireOutputStageable`, and the suite runs the
#     SUCCESS arm constantly: every re-run of a spec finds the previous run's exe at the same `--output=`
#     path and replaces it. The branch is evaluated thousands of times per suite and taken exactly
#     never — which is the worst shape a branch can have, because the coverage looks incidental rather
#     than absent.
#   * Taking it needs the filesystem to REFUSE the replacement — a read-only output, or on POSIX a
#     non-writable parent directory, where the staging directory cannot be created. A spec is a Maxon
#     program plus its expected result, and no directive or block the spec harness reads (docs/SPECS.md
#     lists them) can change a permission; the harness (not the spec) owns the output path, and the
#     program a spec describes does not run until long after the compile that would have to fail.
#
# So the whole branch — a refusal that exists to stop the compiler reporting success over a stale
# binary — had ZERO executable coverage. This script is that coverage. It is a shell gate for the same
# reason `worker-exe-split-gate.sh` and `spec-wedge-repro.sh` are: the property lives OUTSIDE the
# program under test, in the state of the filesystem around it. It works in the system temp area
# (`scripts/lib/system-temp.sh`), so a lock it leaves behind can never reach the checkout.
#
# ============================================================================================
# WHAT IT ASSERTS, AND WHY EACH PART IS LOAD-BEARING
# ============================================================================================
#
# 1. The build FAILS (non-zero exit) when the output cannot be replaced.
# 2. It fails with E6002 specifically.
# 3. The message names what blocks the replacement on this host — the read-only output on Windows,
#    the uncreatable staging directory on POSIX — and not only what the OS said, which reads exactly
#    like a broken compiler.
# 4. The message says how to clear it.
# 5. The message states that the previous binary SURVIVED — and CHECK 6 proves that claim true by
#    hash. This is the one branch where a failed build leaves an output behind, which is exactly why a
#    `;`-chained `build; suite` can report a false red or a FALSE GREEN off the stale binary, and why
#    the exit code is the only honest signal that a build happened.
# 6. NEGATIVE CONTROL: with the lock removed and nothing else changed, the same build succeeds and
#    emits no E6002. Without it, a compiler that refused every build would score a perfect green here.
#
#   ⚠ CHECKS 3-5 MATCH ON WORDING, deliberately: the wording IS the property here, so a reword of
#     a reason or a likely cause in `requireOutputStageable`, or of the survival sentence in
#     `reportUnreplaceableOutput`, is a change to what this gate certifies and must be made in both
#     places. There is no way to assert "the message is useful" that does not read the message.
#
#   ⚠ A message that stops at the mechanical cause fails CHECKS 3-5 and passes 1, 2, 6 and 7. That
#     split is the point — the failure mode this gate guards is a message that is TRUE and useless,
#     not a build that succeeds when it should not.
#
#   ⚠ This covers the ORDINARY road only. A self-rebuild that cannot vacate its own image refuses
#     from `vacateRunningImage` with its own likely cause, and an output that cannot be moved aside or
#     placed refuses from `publishStagedImage`; this gate reaches neither.
#
# Usage:  scripts/output-lock-gate.sh
# Exit:   0 = all checks pass · 1 = a check failed · 2 = the gate could not run (setup failure)

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 2

# shellcheck source=lib/host-binaries.sh
. "$REPO_ROOT/scripts/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 2; }
EXE_EXT="$MAXON_EXE_EXT"; HOST_IS_WINDOWS="$MAXON_HOST_IS_WINDOWS"

MAXON="$(maxon_compiler_path .)"

TempNamesTable="$REPO_ROOT/scripts/lib/temp-names.tsv"
# shellcheck source=lib/system-temp.sh
. "$REPO_ROOT/scripts/lib/system-temp.sh" || { echo "cannot source scripts/lib/system-temp.sh" >&2; exit 2; }

LOG_NAMES="build-baseline.log build-locked.log build-unlocked.log"

FAILED=0

pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; FAILED=1; }

# Make `$OUT` impossible to replace, and put it back. The two hosts need different levers and the
# difference is not cosmetic: a POSIX unlink is authorised by the DIRECTORY's write bit, so clearing
# the file's own mode there would change nothing at all and the gate would silently test nothing.
if [ "$HOST_IS_WINDOWS" = "1" ]; then
	LOCK_REASON='it is read-only'
	LOCK_FIX='Clear its read-only flag'
else
	LOCK_REASON='that stages its replacement could not be created'
	LOCK_FIX="directory is writable"
fi

lock_output() {
	if [ "$HOST_IS_WINDOWS" = "1" ]; then
		attrib +R "$(cygpath -w "$OUT")" || return 1
	else
		chmod a-w "$LOCK_DIR" || return 1
	fi
	return 0
}

unlock_output() {
	if [ "$HOST_IS_WINDOWS" = "1" ]; then
		attrib -R "$(cygpath -w "$OUT")" 2>/dev/null
	else
		chmod u+w "$LOCK_DIR" 2>/dev/null
	fi
	return 0
}

# The lock must not outlive this script whatever happens to it — a read-only directory or image left
# in the tree would break every later build in exactly the way this gate exists to describe. It is
# lifted before the directory is removed, because on POSIX a directory without its write bit cannot
# have its entries unlinked.
cleanup() { unlock_output; temp_remove_all; }

print_logs() {
	for log_name in $LOG_NAMES; do
		[ -f "$WORK/$log_name" ] || continue
		printf '\n--- %s ---\n' "$log_name"
		cat "$WORK/$log_name"
	done
}

if [ ! -x "$MAXON" ]; then
	printf 'output-lock-gate: %s is missing — build it first (see CONTRIBUTING.md)\n' "$MAXON" >&2
	exit 2
fi

if [ "$HOST_IS_WINDOWS" = "0" ] && [ "$(id -u)" = "0" ]; then
	printf 'output-lock-gate: refusing to run as root — root ignores the directory write bit, so the lock would not lock\n' >&2
	exit 2
fi

temp_make dir output-lock-gate || exit 2
WORK="$TempMade"

# ⚠ THE LOCKED DIRECTORY HOLDS THE BUILD OUTPUT AND NOTHING ELSE, and that separation is what makes
# the POSIX arm test anything at all. There, the lock is `chmod a-w` on the PARENT — a POSIX unlink is
# authorised by the directory's write bit, not the file's — so anything else living in that directory
# becomes uncreatable for the duration. With the logs inside it, `build > $WORK/build-locked.log` fails
# in the SHELL, at the redirect, and the compiler never runs: CHECK 2a then reads the shell's own exit
# 1 as "the build refused" (a pass for entirely the wrong reason) while CHECKS 2b-5 fail on a log that
# does not exist. The source and the logs therefore stay in `$WORK`, which is never
# locked, and only `$LOCK_DIR` is made unwritable.
LOCK_DIR="$WORK/out"
SRC="$WORK/prog.maxon"
OUT="$LOCK_DIR/prog${EXE_EXT}"
trap cleanup EXIT

mkdir -p "$LOCK_DIR" || exit 2

cat > "$SRC" <<'MAXON'
export function main() returns ExitCode
	return 7
end 'main'
MAXON

printf '=== output-lock gate ===\n'

# ---- CHECK 1: the baseline build succeeds and produces the output -------------------------------
"$MAXON" build "$SRC" --output="$LOCK_DIR/prog" > "$WORK/build-baseline.log" 2>&1
BASELINE_STATUS=$?

if [ "$BASELINE_STATUS" -eq 0 ] && [ -f "$OUT" ]; then
	pass "CHECK 1: baseline build succeeded and wrote $OUT"
else
	fail "CHECK 1: baseline build exited $BASELINE_STATUS and left $OUT $( [ -f "$OUT" ] && echo present || echo absent ) — printed below"
	print_logs
	printf '\noutput-lock-gate: the setup build failed; the rest of the gate would measure nothing.\n' >&2
	exit 2
fi

BEFORE_HASH="$(git hash-object "$OUT")"

if ! lock_output; then
	printf 'output-lock-gate: could not make %s un-removable\n' "$OUT" >&2
	exit 2
fi

# Unedited, the locked rebuild is up to date: it writes nothing and never meets E6002.
touch "$SRC"

# ---- CHECK 2: the locked rebuild fails, with E6002 ----------------------------------------------
"$MAXON" build "$SRC" --output="$LOCK_DIR/prog" > "$WORK/build-locked.log" 2>&1
LOCKED_STATUS=$?

if [ "$LOCKED_STATUS" -ne 0 ]; then
	pass "CHECK 2a: the locked rebuild failed (exit $LOCKED_STATUS)"
else
	fail "CHECK 2a: the locked rebuild exited 0 — a build that cannot replace its output must not report success"
fi

if grep -q 'error E6002' "$WORK/build-locked.log"; then
	pass "CHECK 2b: it failed with E6002"
else
	fail "CHECK 2b: no 'error E6002' in build-locked.log"
fi

# ---- CHECKS 3-5: the message earns its place -----------------------------------------------------
if grep -Fq "$LOCK_REASON" "$WORK/build-locked.log"; then
	pass "CHECK 3: the message names what blocks the replacement ($LOCK_REASON)"
else
	fail "CHECK 3: the message does not name what blocks the replacement ($LOCK_REASON) — it says only what the OS said, which reads as a broken build"
fi

if grep -Fq "$LOCK_FIX" "$WORK/build-locked.log"; then
	pass "CHECK 4: and it says how to clear it ($LOCK_FIX)"
else
	fail "CHECK 4: the message does not say how to clear the lock ($LOCK_FIX), so the reader cannot tell what to change"
fi

if grep -q 'still in place' "$WORK/build-locked.log"; then
	pass "CHECK 5: the message warns that the previous binary survived"
else
	fail "CHECK 5: the message does not warn that the stale binary survived — the false-green hazard is unstated"
fi

# ---- CHECK 6: and the survival claim is TRUE -----------------------------------------------------
AFTER_HASH="$(git hash-object "$OUT")"

if [ "$AFTER_HASH" = "$BEFORE_HASH" ]; then
	pass "CHECK 6: the previous output survived byte-identical — the warning in CHECK 5 is accurate"
else
	fail "CHECK 6: the output at $OUT changed under a refused build ($BEFORE_HASH -> $AFTER_HASH)"
fi

# ---- CHECK 7: negative control -------------------------------------------------------------------
unlock_output

"$MAXON" build "$SRC" --output="$LOCK_DIR/prog" > "$WORK/build-unlocked.log" 2>&1
UNLOCKED_STATUS=$?

if [ "$UNLOCKED_STATUS" -eq 0 ] && ! grep -q 'error E6002' "$WORK/build-unlocked.log" && grep -q 'Compiled ->' "$WORK/build-unlocked.log"; then
	pass "CHECK 7: with the lock removed the same build compiles, succeeds and emits no E6002"
else
	fail "CHECK 7: the unlocked rebuild exited $UNLOCKED_STATUS, or did not compile, or emitted E6002 — E6002 is firing on builds it should not, or the control never compiled, so CHECKS 2-6 prove nothing"
fi

printf '\n'

if [ "$FAILED" -eq 0 ]; then
	printf 'output-lock gate: PASS\n'
	exit 0
fi

print_logs
printf '\noutput-lock gate: FAIL\n'
exit 1
